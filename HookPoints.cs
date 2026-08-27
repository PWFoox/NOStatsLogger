using System;
using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Networking;

namespace NOStatsLogger
{
    [HarmonyPatch]
    internal static class HookPoints
    {
        private static readonly HashSet<int> hookedInstances = new HashSet<int>();
        private static bool forcedEjectInProgress;

        [HarmonyPatch(typeof(Aircraft), "OnStartClient")]
        [HarmonyPostfix]
        private static void Aircraft_OnStartClient_Postfix(Aircraft __instance)
        {
            try
            {
                if (__instance == null) return;

                var player = __instance.Player;
                if (player == null || !player.IsLocalPlayer) return;

                string aircraftName = __instance.definition != null ? __instance.definition.unitName : __instance.name;
                FlightState.Current.BeginFlight(__instance);

                int id = __instance.GetInstanceID();
                if (hookedInstances.Contains(id)) return;
                hookedInstances.Add(id);

                __instance.OnTouchdown += () =>
                {
                    if (FlightState.Current.TrackedAircraft != __instance) return;
                    Plugin.Log?.LogInfo($"[ПОСАДКА] {aircraftName} (instanceId={id})");
                };

                __instance.onEject += () =>
                {
                    if (!FlightState.Current.Active || FlightState.Current.TrackedAircraft != __instance) return;
                    FlightState.Current.EndFlight(FlightState.ResultEjected);
                };
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[OnStartClient_Postfix] Исключение: {ex}");
            }
        }

        [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.StartEjectionSequence))]
        [HarmonyPostfix]
        private static void Aircraft_StartEjectionSequence_Postfix(Aircraft __instance)
        {
            try
            {
                if (__instance == null) return;
                var player = __instance.Player;
                if (player == null || !player.IsLocalPlayer) return;

                var state = FlightState.Current;
                if (!state.Active || state.TrackedAircraft != __instance) return;

                if (forcedEjectInProgress) return;

                if (__instance.IsLanded())
                {
                    state.EndFlight(FlightState.ResultLanded);
                    return;
                }

                state.EndFlight(FlightState.ResultEjected);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[StartEjectionSequence_Postfix] Исключение: {ex}");
            }
        }

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
                if (!state.Active || actionType != FactionHQ.RewardType.Kill) return;

                PersistentUnit killedUnit;
                if (!UnitRegistry.TryGetPersistentUnit(killedID, out killedUnit) || killedUnit == null || killedUnit.unit == null) return;

                bool isAirKill = killedUnit.unit is Aircraft;
                state.RegisterKill(isAirKill);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[TargetCreditMessage_Postfix] Исключение: {ex}");
            }
        }

        [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.UnitDisabled))]
        [HarmonyPostfix]
        private static void Aircraft_UnitDisabled_Postfix(Aircraft __instance, bool oldState, bool newState)
        {
            try
            {
                if (__instance == null) return;
                var player = __instance.Player;
                if (player == null || !player.IsLocalPlayer) return;

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
                if (__instance == null || !__instance.IsLocalPlayer) return;
                forcedEjectInProgress = false;

                var state = FlightState.Current;
                if (state.Active && state.TrackedAircraft != null && state.TrackedAircraft != aircraft)
                {
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