using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NOStatsLogger
{
    public class FlightRecord
    {
        public string Date;
        public string Player;
        public string Aircraft;
        public int TotalKills;
        public int AircraftKills;
        public int VehicleKills;
        public int BuildingKills;
        public int MissileKills;
        public int ShipKills;
        public string Result;
        public int DurationSeconds;
        public string Mission;
    }

    internal static class StatsStorage
    {
        private static readonly object FileLock = new object();

        private static string LogDirectory => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "BepInEx",
            "plugins",
            "NOStatsLogger",
            "stats"
        );

        private static string FlightsFile => Path.Combine(LogDirectory, "flights.csv");

        public static void Initialize()
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                EnsureFlightsHeader();
            }
            catch (Exception e)
            {
                Plugin.Log?.LogError($"NO Stats: Ошибка инициализации хранилища: {e}");
            }
        }

        public static void SaveFlight(FlightState state)
        {
            try
            {
                string date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                int totalKills = state.AirKills + state.GroundKills;
                int duration = (int)Math.Max(0, (DateTime.UtcNow - state.StartedAt).TotalSeconds);

                string line = string.Join(",",
                    Csv(date),
                    Csv("Player"),
                    Csv(state.AircraftName ?? "Unknown"),
                    totalKills.ToString(),
                    state.AirKills.ToString(),
                    state.GroundKills.ToString(),
                    "0", "0", "0",
                    Csv(state.Result),
                    duration.ToString(),
                    Csv("Custom")
                );

                lock (FileLock)
                {
                    EnsureFlightsHeader();
                    File.AppendAllText(FlightsFile, line + Environment.NewLine, Encoding.UTF8);
                }

                Plugin.Log?.LogInfo($"NO Stats: Вылет записан на диск -> {state.AircraftName} | Kills={totalKills} | Result={state.Result}");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"NO Stats: Ошибка сохранения вылета: {ex}");
            }
        }

        public static List<FlightRecord> LoadAllFlights()
        {
            var list = new List<FlightRecord>();
            if (!File.Exists(FlightsFile)) return list;

            try
            {
                string[] lines;
                lock (FileLock)
                {
                    lines = File.ReadAllLines(FlightsFile, Encoding.UTF8);
                }

                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    string[] parts = ParseCsvLine(line);
                    if (parts.Length < 11) continue;

                    var rec = new FlightRecord
                    {
                        Date = parts[0],
                        Player = parts[1],
                        Aircraft = parts[2],
                        TotalKills = int.TryParse(parts[3], out int tk) ? tk : 0,
                        AircraftKills = int.TryParse(parts[4], out int ak) ? ak : 0,
                        VehicleKills = int.TryParse(parts[5], out int vk) ? vk : 0,
                        Result = parts[9],
                        DurationSeconds = int.TryParse(parts[10], out int ds) ? ds : 0,
                    };
                    list.Add(rec);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"NO Stats: Ошибка загрузки CSV: {ex}");
            }

            return list;
        }

        private static void EnsureFlightsHeader()
        {
            if (File.Exists(FlightsFile)) return;
            string header = "date,player,aircraft,total_kills,aircraft_kills,vehicle_kills,building_kills,missile_kills,ship_kills,result,duration_seconds,mission" + Environment.NewLine;
            File.WriteAllText(FlightsFile, header, Encoding.UTF8);
        }

        private static string Csv(string val) => $"\"{val?.Replace("\"", "\"\"")}\"";

        private static string[] ParseCsvLine(string line)
        {
            List<string> result = new List<string>();
            bool inQuotes = false;
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            result.Add(sb.ToString());
            return result.ToArray();
        }
    }
}