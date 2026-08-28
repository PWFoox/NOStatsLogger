using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NOStatsLogger
{
    internal static class ProgressionManager
    {
        public static long TotalExperience { get; private set; } = 0;
        public static int CurrentLevel { get; private set; } = 1;
        public static long ExpForCurrentLevel { get; private set; } = 0;
        public static long ExpForNextLevel { get; private set; } = 1000;

        private static string SavePath => Path.Combine(StatsStorage.LogDirectory, "progression.json");

        private static readonly string[] Ranks = new string[]
        {
            "Cadet", "Trainee", "Junior Sergeant", "Sergeant", "Staff Sergeant",
            "Flight Sergeant", "Junior Lieutenant", "Lieutenant", "Senior Lieutenant", "Captain",
            "Major", "Lieutenant Colonel", "Colonel", "Brigadier", "Major General",
            "Lieutenant General", "Colonel General", "Marshal", "Ace Trainee", "Junior Ace",
            "Ace", "Senior Ace", "Elite Ace", "Veteran", "Senior Veteran",
            "Elite Veteran", "Master", "Senior Master", "Grand Master", "Legend",
            "Sky Hero", "Commander", "Flag Officer", "Archon", "Grand Archon",
            "Interceptor", "Vanguard", "Elite Vanguard", "Sky Guardian", "Supreme Guardian",
            "Commander-in-Chief", "Supreme Commander", "Sector Elite", "Sky Lord", "Stormbringer",
            "Angel of Death", "God of War", "Transcendent", "Harbinger of Apocalypse", "Absolute Pilot"
        };

        public static void Load()
        {
            try
            {
                if (File.Exists(SavePath))
                {
                    string content = File.ReadAllText(SavePath).Trim();
                    if (long.TryParse(content, out long parsedExp))
                    {
                        TotalExperience = parsedExp;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[Progression] Load error: {ex}");
            }
            RecalculateLevel();
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(SavePath, TotalExperience.ToString());
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[Progression] Save error: {ex}");
            }
        }

        public static void AddExperienceFromFlight(FlightRecord record)
        {
            long earned = record.DurationSeconds;
            earned += record.AirKills * 250;
            earned += record.GroundKills * 100;

            if (record.Result == FlightState.ResultLanded) earned += 500;
            else if (record.Result == FlightState.ResultEjected) earned += 100;

            TotalExperience += earned;
            RecalculateLevel();
            Save();
        }

        public static string GetCurrentRank()
        {
            int index = Mathf.Clamp(CurrentLevel - 1, 0, Ranks.Length - 1);
            return Ranks[index];
        }

        private static void RecalculateLevel()
        {
            int level = 1;
            long expCumulative = 0;
            long nextLevelReq = 1000;

            while (level < 50 && TotalExperience >= expCumulative + nextLevelReq)
            {
                expCumulative += nextLevelReq;
                level++;
                nextLevelReq = (long)(nextLevelReq * 1.2f);
            }

            CurrentLevel = level;
            ExpForCurrentLevel = expCumulative;
            ExpForNextLevel = nextLevelReq;
        }
    }
}