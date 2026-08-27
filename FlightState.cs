using System;

namespace NOStatsLogger
{
    internal class FlightState
    {
        public const string ResultLanded = "landed";
        public const string ResultEjected = "ejected";
        public const string ResultShotDown = "shot_down";

        public static FlightState Current = new FlightState();

        public string AircraftName;
        public Aircraft TrackedAircraft;
        public int AirKills;
        public int GroundKills;
        public string Result = ResultLanded;
        public DateTime StartedAt = DateTime.UtcNow;
        public bool Active;

        public void BeginFlight(Aircraft aircraft)
        {
            if (aircraft == null) return;

            string niceName = aircraft.definition != null ? aircraft.definition.unitName : aircraft.name;

            Current = new FlightState
            {
                AircraftName = niceName,
                TrackedAircraft = aircraft,
                AirKills = 0,
                GroundKills = 0,
                StartedAt = DateTime.UtcNow,
                Active = true,
                Result = ResultLanded
            };
        }

        public void RegisterKill(bool isAirKill)
        {
            if (!Active) return;

            if (isAirKill) AirKills++;
            else GroundKills++;
        }

        public void EndFlight(string result)
        {
            if (!Active) return;

            Active = false;
            Result = result;

            int durationSeconds = (int)Math.Max(0, (DateTime.UtcNow - StartedAt).TotalSeconds);

            StatsStorage.SaveFlight(new FlightRecord
            {
                Timestamp = DateTime.Now,
                Aircraft = AircraftName ?? "Unknown",
                AirKills = AirKills,
                GroundKills = GroundKills,
                Result = Result,
                DurationSeconds = durationSeconds
            });
        }
    }
}