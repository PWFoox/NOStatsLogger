using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace NOStatsLogger
{
    [BepInPlugin("NOStatsLogger", "NO Stats Logger", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;

            Harmony harmony = new Harmony("NOStatsLogger");
            harmony.PatchAll();

            Log.LogInfo("NO Stats: мод загружен. Хуки: OnStartClient, OnTouchdown, onEject, UnitDisabled.");
        }
    }
}
