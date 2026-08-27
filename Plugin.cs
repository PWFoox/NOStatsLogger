using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace NOStatsLogger
{
    [BepInPlugin("NOStatsLogger", "NO Stats Logger", "0.6")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;

            Harmony harmony = new Harmony("NOStatsLogger");
            harmony.PatchAll();

            Log.LogInfo("NO Stats: мод успешно загружен.");
        }
    }
}