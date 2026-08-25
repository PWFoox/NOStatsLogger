using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using NuclearOption.Networking;

namespace NOStatsLogger
{
    // ВАЖНО: здесь патчатся ТОЛЬКО те методы/события Aircraft, которые мы
    // подтвердили через dnSpy (см. дамп класса Aircraft из чата):
    //   - private void OnStartClient()   -> вылет + точка, где Player уже не null
    //   - public event Action OnTouchdown -> посадка
    //   - public event Action onEject     -> катапультирование
    //   - public override void UnitDisabled(bool oldState, bool newState) -> сбитие/разбитие
    //
    // Фраги (FactionHQ.RewardPlayer) сюда пока НЕ добавлены — ждём дамп
    // класса FactionHQ из dnSpy, чтобы не гадать с сигнатурой.
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

                // ---- Посадка ----
                // ВАЖНО: OnTouchdown — чисто физическое событие (пересечение порога
                // высоты), оно срабатывает и при нормальной посадке, и при падении
                // после того как самолёт сбили. Поэтому не завершаем вылет по
                // OnTouchdown сразу, а ждём — вдруг игрок в ближайшие секунды
                // катапультируется (см. StartEjectionSequence ниже) или самолёт
                // будет официально помечен как disabled (UnitDisabled).
                //
                // Окно увеличено до 3 секунд: после жёсткого приземления/крушения
                // игроку нужно время среагировать и нажать катапультирование, а
                // само событие onEject (см. ниже) в этот момент часто НЕ срабатывает
                // вообще (см. комментарий у StartEjectionSequence) — поэтому именно
                // этот таймаут остаётся резервным способом поймать "просто посадку".
                __instance.OnTouchdown += () =>
                {
                    var state = FlightState.Current;
                    if (!state.Active || state.TrackedAircraft != __instance)
                        return;

                    if (state.PendingLandConfirmation)
                    {
                        // Уже ждём подтверждения (самолёт мог подпрыгнуть на посадке
                        // и вызвать OnTouchdown несколько раз подряд) — не плодим
                        // повторные отложенные проверки.
                        return;
                    }
                    state.PendingLandConfirmation = true;

                    Plugin.Log?.LogInfo(
                        $"[ПОСАДКА-кандидат] {aircraftName} (instanceId={id}), " +
                        "ждём 3с — не окажется ли это на самом деле сбитием/катапультированием..."
                    );

                    Task.Delay(3000).ContinueWith(_ =>
                    {
                        if (state.Active && state == FlightState.Current)
                        {
                            Plugin.Log?.LogInfo($"[ПОСАДКА] {aircraftName} (instanceId={id}) — подтверждено.");
                            state.EndFlight(FlightState.ResultLanded);
                        }
                        else
                        {
                            Plugin.Log?.LogInfo(
                                $"[ПОСАДКА-кандидат] {aircraftName} (instanceId={id}) — отменено, " +
                                "вылет уже завершён другим событием (сбитие/катапультирование)."
                            );
                        }
                    });
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
                // Если вылет уже завершён через посадку/катапультирование — EndFlight ничего не сделает (см. Active flag).
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
    }
}
