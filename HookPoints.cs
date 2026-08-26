using System;
using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Networking;

namespace NOStatsLogger
{
    // ВАЖНО: здесь патчатся следующие точки, все подтверждены через dnSpy:
    //   - private void OnStartClient()   -> вылет + точка, где Player уже не null
    //   - public event Action OnTouchdown -> только диагностика (см. комментарий ниже)
    //   - public void StartEjectionSequence() -> катапультирование (основной источник)
    //   - public event Action onEject     -> катапультирование (диагностика, не основной источник)
    //   - public override void UnitDisabled(bool oldState, bool newState) -> сбитие/разбитие
    //   - MessageManager.UserCode_TargetCreditMessage_106951341 -> фраги (воздух/земля)
    //   - Player.SetAircraft(Aircraft) -> завершение вылета при пересадке в другой самолёт
    [HarmonyPatch]
    internal static class HookPoints
    {
        // Чтобы не подписываться на события Aircraft повторно,
        // если OnStartClient вызовется второй раз на том же объекте.
        private static readonly HashSet<int> hookedInstances = new HashSet<int>();

        // ================== ВЫЛЕТ (spawn) + подписка на события ==================
        [HarmonyPatch(typeof(Aircraft), "OnStartClient")]
        [HarmonyPostfix]
        private static void Aircraft_OnStartClient_Postfix(Aircraft __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                var player = __instance.Player;

                Plugin.Log?.LogInfo(
                    $"[OnStartClient] instanceId={__instance.GetInstanceID()} " +
                    $"aircraftType={(__instance.definition != null ? __instance.definition.unitName : "NULL")} " +
                    $"player={(player == null ? "NULL" : "не null")} " +
                    $"isLocal={(player != null && player.IsLocalPlayer)}"
                );

                if (player == null || !player.IsLocalPlayer)
                {
                    // Чужой игрок / AI — не наш самолёт, пропускаем.
                    return;
                }

                string aircraftName = __instance.definition != null
                    ? __instance.definition.unitName
                    : __instance.name;

                Plugin.Log?.LogInfo($"[ВЫЛЕТ] Локальный игрок сел в самолёт: {aircraftName}");

                FlightState.Current.BeginFlight(__instance);

                int id = __instance.GetInstanceID();
                if (hookedInstances.Contains(id))
                {
                    Plugin.Log?.LogWarning($"[OnStartClient] instanceId={id} уже был захукан ранее, повторно не подписываемся на события.");
                    return;
                }
                hookedInstances.Add(id);

                // ---- Посадка (только диагностика, НЕ завершает вылет!) ----
                // ВАЖНО: OnTouchdown срабатывает и при обычной посадке для дозаправки/
                // перевооружения с последующим повторным взлётом в ТОМ ЖЕ самолёте —
                // OnStartClient в этом случае повторно не вызывается (тот же инстанс),
                // поэтому если завершать вылет здесь, весь дальнейший полёт (включая
                // возможные новые фраги или итоговое крушение) потеряется.
                // Реальное завершение вылета происходит только через UnitDisabled,
                // StartEjectionSequence, или через переход в другой самолёт
                // (см. патч на Player.SetAircraft ниже).
                __instance.OnTouchdown += () =>
                {
                    if (FlightState.Current.TrackedAircraft != __instance)
                        return;

                    Plugin.Log?.LogInfo(
                        $"[ПОСАДКА] {aircraftName} (instanceId={id}) — коснулся земли " +
                        "(вылет не завершается автоматически, см. комментарий в коде)."
                    );
                };

                // ---- Катапультирование (диагностика через сам onEject) ----
                // ВАЖНО: это событие срабатывает НЕ ВСЕГДА при катапультировании!
                // Смотри UserCode_RpcJettisonCanopy_1196305304 в дампе Aircraft:
                // если к моменту нажатия игра считает самолёт уже "приземлившимся"
                // (IsLanded(): radarAlt<5 && speed<2.5 — это как раз типично после
                // жёсткого крушения, когда самолёт быстро тормозит об землю),
                // onEject вообще не вызывается — только открываются фонари.
                // Поэтому основным источником катапультирования сделан хук на
                // StartEjectionSequence() ниже, а это оставлено для диагностики.
                __instance.onEject += () =>
                {
                    if (!FlightState.Current.Active || FlightState.Current.TrackedAircraft != __instance)
                        return;

                    Plugin.Log?.LogInfo($"[КАТАПУЛЬТИРОВАНИЕ-onEject] {aircraftName} (instanceId={id})");
                    FlightState.Current.EndFlight(FlightState.ResultEjected);
                };
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[OnStartClient_Postfix] Исключение: {ex}");
            }
        }

        // ================== КАТАПУЛЬТИРОВАНИЕ (основной источник) ==================
        // Патчим StartEjectionSequence(), а не событие onEject.
        // StartEjectionSequence вызывается СРАЗУ в момент нажатия игроком кнопки
        // катапультирования, БЕЗУСЛОВНО (первым делом ставит aircraft.ejected = true),
        // в отличие от onEject, который вызывается позже и не всегда (см. комментарий
        // у подписки на onEject выше). Поэтому это — надёжный сигнал.
        //
        // ВАЖНО: StartEjectionSequence также вызывается ДВИЖКОМ ПРИНУДИТЕЛЬНО из
        // Player.SetAircraft, когда игрок пересаживается в новый самолёт, пока
        // старый ещё жив (см. дамп Player.SetAircraft) — это НЕ настоящее
        // катапультирование игрока, а техническая мера снятия authority со
        // старого борта. Патч на Player.SetAircraft ниже вызывается ПОСЛЕ этого
        // принудительного StartEjectionSequence и корректно перезаписывает
        // результат на "landed", так что порядок EndFlight-вызовов сам всё
        // разруливает: реальное катапультирование, набранное здесь, будет
        // перезаписано на landed только если игрок действительно пересел в
        // другой борт — если же он просто катапультировался и не сел ни в какой
        // новый самолёт, Player.SetAircraft не вызовется, и результат "ejected"
        // останется.
        [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.StartEjectionSequence))]
        [HarmonyPostfix]
        private static void Aircraft_StartEjectionSequence_Postfix(Aircraft __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                var player = __instance.Player;
                if (player == null || !player.IsLocalPlayer)
                    return;

                var state = FlightState.Current;
                if (!state.Active || state.TrackedAircraft != __instance)
                    return;

                string aircraftName = __instance.definition != null
                    ? __instance.definition.unitName
                    : __instance.name;

                Plugin.Log?.LogInfo(
                    $"[КАТАПУЛЬТИРОВАНИЕ] {aircraftName} (instanceId={__instance.GetInstanceID()}) " +
                    "— по факту нажатия (StartEjectionSequence)."
                );

                state.EndFlight(FlightState.ResultEjected);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[StartEjectionSequence_Postfix] Исключение: {ex}");
            }
        }

        // ================== ФРАГИ (через MessageManager.UserCode_TargetCreditMessage) ==================
        // ВАЖНО: раньше пробовали патчить FactionHQ.RewardPlayer, но он помечен
        // [Server] и выполняется ТОЛЬКО на хосте. Если ты просто клиент на чужом
        // сервере — он у тебя вообще не вызывается, поэтому фраги не считались.
        //
        // TargetCreditMessage — это [ClientRpc(target = RpcTarget.Player)],
        // то есть RPC, адресованный конкретно тому клиенту, которому начислена
        // награда. Он выполняется на твоей машине НЕЗАВИСИМО от того, хост ты
        // или клиент — именно поэтому патчим его, а не RewardPlayer.
        //
        // Как и RewardPlayer, actionType может быть не только Kill (Recon,
        // Jamming, Supply, Refuel, Repair, RescuePilots, CapturePilots,
        // CaptureLocation) — фильтруем строго по Kill.
        [HarmonyPatch(typeof(MessageManager), "UserCode_TargetCreditMessage_106951341")]
        [HarmonyPostfix]
        private static void MessageManager_TargetCreditMessage_Postfix(
            PersistentID killedID,
            float creditAwarded,
            FactionHQ.RewardType actionType)
        {
            try
            {
                var state = FlightState.Current;
                if (!state.Active)
                    return;

                Plugin.Log?.LogInfo(
                    $"[TargetCreditMessage] actionType={actionType} killedID={killedID} creditAwarded={creditAwarded}"
                );

                if (actionType != FactionHQ.RewardType.Kill)
                {
                    // Не фраг (разведка/дозаправка/ремонт/т.д.) — пропускаем.
                    return;
                }

                PersistentUnit killedUnit;
                bool found = UnitRegistry.TryGetPersistentUnit(killedID, out killedUnit);

                if (!found || killedUnit == null || killedUnit.unit == null)
                {
                    Plugin.Log?.LogWarning("[TargetCreditMessage] actionType=Kill, но цель не найдена в UnitRegistry — пропускаем.");
                    return;
                }

                bool isAirKill = killedUnit.unit is Aircraft;

                Plugin.Log?.LogInfo(
                    $"[ФРАГ] {(isAirKill ? "ВОЗДУХ" : "ЗЕМЛЯ")}: {killedUnit.unit.unitName} " +
                    $"(тип цели={killedUnit.unit.GetType().Name})"
                );

                state.RegisterKill(isAirKill);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[TargetCreditMessage_Postfix] Исключение: {ex}");
            }
        }

        // ================== СБИТИЕ / ВЫВОД ИЗ СТРОЯ ==================
        [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.UnitDisabled))]
        [HarmonyPostfix]
        private static void Aircraft_UnitDisabled_Postfix(Aircraft __instance, bool oldState, bool newState)
        {
            try
            {
                if (__instance == null)
                    return;

                var player = __instance.Player;
                if (player == null || !player.IsLocalPlayer)
                    return;

                string aircraftName = __instance.definition != null
                    ? __instance.definition.unitName
                    : __instance.name;

                Plugin.Log?.LogInfo(
                    $"[UnitDisabled] {aircraftName} oldState={oldState} newState={newState} instanceId={__instance.GetInstanceID()}"
                );

                // newState == true значит самолёт стал disabled (сбит/разбился).
                // Если вылет уже завершён через посадку/катапультирование/пересадку —
                // EndFlight ничего не сделает (см. Active flag).
                if (newState && FlightState.Current.Active && FlightState.Current.TrackedAircraft == __instance)
                {
                    FlightState.Current.EndFlight(FlightState.ResultShotDown);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[UnitDisabled_Postfix] Исключение: {ex}");
            }
        }

        // ================== ЗАВЕРШЕНИЕ ВЫЛЕТА ПРИ ПЕРЕСАДКЕ В ДРУГОЙ САМОЛЁТ ==================
        // Player.SetAircraft(aircraft) вызывается каждый раз, когда игрок садится в
        // новый борт (в том числе — при первом вылете; в этом случае у нас ещё нет
        // активного FlightState.TrackedAircraft, и патч ничего не делает).
        //
        // Если к моменту вызова предыдущий вылет всё ещё Active (то есть не было
        // ни сбития, ни настоящего катапультирования) — единственное разумное
        // объяснение: игрок нормально приземлился на предыдущем борту и пересел
        // в другой (или тот же вернувшийся из ангара) самолёт.
        //
        // ВАЖНО: SetAircraft сама вызывает StartEjectionSequence() на СТАРОМ борту
        // принудительно (см. дамп Player.SetAircraft — это чтобы снять authority),
        // поэтому наш патч на StartEjectionSequence мог уже успеть отработать и
        // выставить result=ejected. Этот патч, выполняясь ПОСЛЕ, корректно
        // перезаписывает результат на landed — так что порядок в коде важен и
        // работает на нас: EndFlight здесь строго переопределяет предыдущий вызов,
        // если TrackedAircraft всё ещё указывает на старый (уже отключённый) борт.
        [HarmonyPatch(typeof(Player), nameof(Player.SetAircraft))]
        [HarmonyPostfix]
        private static void Player_SetAircraft_Postfix(Player __instance, Aircraft aircraft)
        {
            try
            {
                if (__instance == null || !__instance.IsLocalPlayer)
                    return;

                var state = FlightState.Current;

                // Если TrackedAircraft уже недействителен ИЛИ вылет ещё Active —
                // считаем, что предыдущий вылет (если это другой самолёт) на самом
                // деле завершился нормальной посадкой.
                if (state.TrackedAircraft != null && state.TrackedAircraft != aircraft)
                {
                    string prevName = state.AircraftName ?? "Unknown";
                    string newName = aircraft != null
                        ? (aircraft.definition != null ? aircraft.definition.unitName : aircraft.name)
                        : "Unknown";

                    Plugin.Log?.LogInfo(
                        $"[ПЕРЕСАДКА] {prevName} -> {newName}. " +
                        (state.Active
                            ? "Предыдущий вылет считаем нормальной посадкой (result переопределяется на landed)."
                            : "Предыдущий вылет уже был завершён другим событием.")
                    );

                    // Принудительно переопределяем результат на landed, даже если
                    // Active уже false из-за вынужденного StartEjectionSequence
                    // внутри SetAircraft — поэтому здесь НЕ используем EndFlight
                    // (он бы просто не сработал из-за Active==false), а меняем
                    // состояние напрямую.
                    if (state.Active)
                    {
                        state.EndFlight(FlightState.ResultLanded);
                    }
                    else
                    {
                        state.OverrideResult(FlightState.ResultLanded);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[Player_SetAircraft_Postfix] Исключение: {ex}");
            }
        }
    }
}