using System;
using System.IO;
using System.Text;

namespace NOStatsLogger
{
    internal static class StatsClient
    {
        private static readonly object FileLock = new object();

        private static string LogDirectory
        {
            get
            {
                string dir = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "BepInEx",
                    "plugins",
                    "NOStatsLogger",
                    "stats"
                );

                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string FlightsFile
        {
            get { return Path.Combine(LogDirectory, "flights.csv"); }
        }

        private static string KillsFile
        {
            get { return Path.Combine(LogDirectory, "kills.csv"); }
        }

        public static void Initialize()
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);

                EnsureFlightsHeader();
                EnsureKillsHeader();

                Plugin.Log?.LogInfo(
                    "NO Stats: локальная статистика будет записываться в " +
                    LogDirectory
                );
            }
            catch (Exception e)
            {
                Plugin.Log?.LogError(
                    "NO Stats: не удалось создать директорию статистики: " +
                    e
                );
            }
        }

        public static void SendFlight(
            string player,
            string aircraft,
            int airKills,
            int vehicleKills,
            int buildingKills,
            int missileKills,
            int shipKills,
            string result,
            int durationSeconds,
            string mission)
        {
            try
            {
                string date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                int totalKills =
                    airKills +
                    vehicleKills +
                    buildingKills +
                    missileKills +
                    shipKills;

                string line = string.Join(",",
                    Csv(date),
                    Csv(player),
                    Csv(aircraft),
                    totalKills.ToString(),
                    airKills.ToString(),
                    vehicleKills.ToString(),
                    buildingKills.ToString(),
                    missileKills.ToString(),
                    shipKills.ToString(),
                    Csv(result),
                    durationSeconds.ToString(),
                    Csv(mission)
                );

                lock (FileLock)
                {
                    EnsureFlightsHeader();

                    File.AppendAllText(
                        FlightsFile,
                        line + Environment.NewLine,
                        Encoding.UTF8
                    );
                }

                Plugin.Log?.LogInfo(
                    "NO Stats: вылет записан. " +
                    player + " | " +
                    aircraft +
                    " | kills=" +
                    totalKills +
                    " | result=" +
                    result
                );
            }
            catch (Exception e)
            {
                Plugin.Log?.LogError(
                    "NO Stats: ошибка записи вылета: " + e
                );
            }
        }

        public static void LogKill(
            string player,
            string aircraft,
            string targetName,
            string killType,
            string dateTime)
        {
            try
            {
                string line = string.Join(",",
                    Csv(dateTime),
                    Csv(player),
                    Csv(aircraft),
                    Csv(targetName),
                    Csv(killType)
                );

                lock (FileLock)
                {
                    EnsureKillsHeader();

                    File.AppendAllText(
                        KillsFile,
                        line + Environment.NewLine,
                        Encoding.UTF8
                    );
                }

                Plugin.Log?.LogInfo(
                    "NO Stats: kill записан: " +
                    player + " | " +
                    aircraft +
                    " -> " +
                    targetName +
                    " [" +
                    killType +
                    "]"
                );
            }
            catch (Exception e)
            {
                Plugin.Log?.LogError(
                    "NO Stats: ошибка записи kill: " + e
                );
            }
        }

        private static void EnsureFlightsHeader()
        {
            if (File.Exists(FlightsFile))
                return;

            string header =
                "date,player,aircraft,total_kills,aircraft_kills,vehicle_kills," +
                "building_kills,missile_kills,ship_kills,result," +
                "duration_seconds,mission" +
                Environment.NewLine;

            File.WriteAllText(
                FlightsFile,
                header,
                Encoding.UTF8
            );
        }

        private static void EnsureKillsHeader()
        {
            if (File.Exists(KillsFile))
                return;

            string header =
                "date,player,aircraft,target,kill_type" +
                Environment.NewLine;

            File.WriteAllText(
                KillsFile,
                header,
                Encoding.UTF8
            );
        }

        private static string Csv(string value)
        {
            if (value == null)
                value = "";

            return "\"" +
                   value.Replace("\"", "\"\"") +
                   "\"";
        }
    }
}