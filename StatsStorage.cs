using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NOStatsLogger
{
    internal struct FlightRecord
    {
        public DateTime Timestamp;
        public string Aircraft;
        public int AirKills;
        public int GroundKills;
        public string Result;
        public int DurationSeconds;
    }

    internal static class StatsStorage
    {
        private static readonly object FileLock = new object();

        public static string LogDirectory
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

        private static string FlightsFile => Path.Combine(LogDirectory, "flights.csv");

        private const string Header = "timestamp,aircraft,air_kills,ground_kills,result,duration_seconds";

        public static void SaveFlight(FlightRecord record)
        {
            try
            {
                string line = string.Join(",",
                    Csv(record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                    Csv(record.Aircraft),
                    record.AirKills.ToString(),
                    record.GroundKills.ToString(),
                    Csv(record.Result),
                    record.DurationSeconds.ToString()
                );

                lock (FileLock)
                {
                    EnsureHeader();
                    File.AppendAllText(FlightsFile, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception e)
            {
                Plugin.Log?.LogError("NO Stats: ошибка сохранения вылета в CSV: " + e);
            }
        }

        public static List<FlightRecord> LoadAll()
        {
            var result = new List<FlightRecord>();

            try
            {
                lock (FileLock)
                {
                    if (!File.Exists(FlightsFile))
                        return result;

                    string[] lines = File.ReadAllLines(FlightsFile, Encoding.UTF8);

                    for (int i = 1; i < lines.Length; i++)
                    {
                        if (string.IsNullOrWhiteSpace(lines[i]))
                            continue;

                        string[] fields = ParseCsvLine(lines[i]);
                        if (fields.Length < 6)
                            continue;

                        DateTime timestamp;
                        DateTime.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);

                        int airKills, groundKills, duration;
                        int.TryParse(fields[2], out airKills);
                        int.TryParse(fields[3], out groundKills);
                        int.TryParse(fields[5], out duration);

                        result.Add(new FlightRecord
                        {
                            Timestamp = timestamp,
                            Aircraft = fields[1],
                            AirKills = airKills,
                            GroundKills = groundKills,
                            Result = fields[4],
                            DurationSeconds = duration
                        });
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log?.LogError("NO Stats: ошибка чтения flights.csv: " + e);
            }

            return result;
        }

        private static void EnsureHeader()
        {
            if (File.Exists(FlightsFile))
                return;

            File.WriteAllText(FlightsFile, Header + Environment.NewLine, Encoding.UTF8);
        }

        private static string Csv(string value)
        {
            if (value == null)
                value = "";

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string[] ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        fields.Add(sb.ToString());
                        sb.Clear();
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }
            fields.Add(sb.ToString());

            return fields.ToArray();
        }
    }
}