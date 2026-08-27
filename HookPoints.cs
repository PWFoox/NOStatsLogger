using System;
using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Networking;

namespace NOStatsLogger
{
    // ВАЖНО: здесь патчатся следующие точки, все подтверждены через dnSpy:
    //   - private void OnStartClient()   -> вылет + точка, где Player уже не null
    //   - public event Action OnTouchdown -> только диагностика (см. комментарий ниже)
    //   - public void StartEjectionSequence() -> катапультирование (основной источник, с проверкой IsLanded())
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

        // Взводится Prefix-ом на Player.SetAircraft перед вызовом оригинального
        // метода и означает: "если сейчас сработает StartEjectionSequence — это
        // ТЕХНИЧЕСКИЙ вызов изнутри SetAircraft (снятие authority со старого
        // борта при пересадке на новый), а не настоящее катапультирование
        // игрока". См. дамп Player.SetAircraft:
        //   if (base.IsServer && this.Aircraft != null) {
        //       this.RemoveAircraftAuthority(this.Aircraft);
        //       this.Aircraft.StartEjectionSequence();   // <-- вот этот вызов
        //   }
        // Сбрасывается в начале Player_SetAircraft_Postfix, то есть живёт ровно
        // на время выполнения тела оригинального SetAircraft.
        private static bool forcedEjectInProgress;

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
        // старого борта. Раньше мы пытались распознать это постфактум через
        // Player.SetAircraft-патч, но подтвердилось на практике, что это
        // ненадёжно (Player.Aircraft не всегда успевает обнулиться одинаково,
        // из-за чего то срабатывает, то нет). Поэтому теперь используем флаг
        // forcedEjectInProgress, который взводится ДО вызова оригинального
        // SetAircraft (см. Player_SetAircraft_Prefix ниже) — если он взведён,
        // это гарантированно технический вызов, и мы просто не завершаем вылет.
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

                if (forcedEjectInProgress)
                {
                    Plugin.Log?.LogInfo(
                        $"[КАТАПУЛЬТИРОВАНИЕ-техническое] {aircraftName} (instanceId={__instance.GetInstanceID()}) " +
                        "— вызвано изнутри Player.SetAircraft при пересадке, НЕ считается реальным катапультированием."
                    );
                    return;
                }

                // ВАЖНО: та же клавиша StartEjectionSequence вызывается и при
                // экстренном катапультировании в воздухе, И при обычном выходе
                // из уже приземлившегося самолёта (чтобы пойти к другому борту).
                // Игра сама различает эти случаи внутри
                // UserCode_RpcJettisonCanopy через IsLanded(), но
                // StartEjectionSequence вызывается безусловно в обоих случаях.
                // Поэтому проверяем IsLanded() сами: если самолёт уже
                // приземлился (radarAlt<5 && speed<2.5) — это спокойный выход
                // из кабины, а не настоящее катапультирование.
                if (__instance.IsLanded())
                {
                    Plugin.Log?.LogInfo(
                        $"[ВЫХОД-ИЗ-КАБИНЫ] {aircraftName} (instanceId={__instance.GetInstanceID()}) " +
                        "— нажатие катапультирования на земле (IsLanded()==true), считаем нормальным выходом, НЕ катапультированием."
                    );
                    state.EndFlight(FlightState.ResultLanded);
                    return;
                }

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
        // Prefix взводит forcedEjectInProgress ДО выполнения оригинального
        // SetAircraft — если внутри него вызовется StartEjectionSequence на
        // старом борту (техническое снятие authority), наш патч на
        // StartEjectionSequence увидит этот флаг и НЕ завершит вылет как
        // "ejected" (см. комментарий там). Поэтому к моменту Postfix вылет
        // либо всё ещё Active (если старый борт просто улетел/приземлился и
        // ничего фатального не произошло), либо уже был честно завершён
        // ДРУГИМ событием (сбитие/настоящее катапультирование), случившимся
        // раньше и НЕ связанным с этим вызовом SetAircraft.
        [HarmonyPatch(typeof(Player), nameof(Player.SetAircraft))]
        [HarmonyPrefix]
        private static void Player_SetAircraft_Prefix(Player __instance)
        {
            if (__instance != null && __instance.IsLocalPlayer)
            {
                forcedEjectInProgress = true;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetAircraft))]
        [HarmonyPostfix]
        private static void Player_SetAircraft_Postfix(Player __instance, Aircraft aircraft)
        {
            try
            {
                if (__instance == null || !__instance.IsLocalPlayer)
                    return;

                // Флаг сделал своё дело на время выполнения оригинального метода —
                // сбрасываем его сразу, чтобы не зацепить случайный последующий
                // вызов StartEjectionSequence, не связанный с этим SetAircraft.
                forcedEjectInProgress = false;

                var state = FlightState.Current;

                // Если TrackedAircraft ещё указывает на предыдущий борт и вылет
                // всё ещё Active — значит, ничего фатального с ним не произошло
                // (не сбили, не катапультировался по-настоящему), а игрок просто
                // пересел в другой самолёт. Значит, предыдущий вылет — обычная
                // посадка.
                if (state.Active && state.TrackedAircraft != null && state.TrackedAircraft != aircraft)
                {
                    string prevName = state.AircraftName ?? "Unknown";
                    string newName = aircraft != null
                        ? (aircraft.definition != null ? aircraft.definition.unitName : aircraft.name)
                        : "Unknown";

                    Plugin.Log?.LogInfo(
                        $"[ПЕРЕСАДКА] {prevName} -> {newName}. Предыдущий вылет считаем нормальной посадкой."
                    );

                    state.EndFlight(FlightState.ResultLanded);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[Player_SetAircraft_Postfix] Исключение: {ex}");
            }
        }
    }
}