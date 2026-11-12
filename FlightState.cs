using System;

namespace NOStatsLogger
{
    internal class FlightState
    {
        public const string ResultLanded = "landed";
        public const string ResultEjected = "ejected";
        public const string ResultShotDown = "shot_down";

        // Same altitude limit as Aircraft.IsLanded(), but without its speed check,
        // so an aircraft taxiing or rolling on the ground still counts as on the ground.
        private const float GroundRadarAltitude = 5f;

        public static FlightState Current = new FlightState();

        public string AircraftName;
        public Aircraft TrackedAircraft;
        public int AirKills;
        public int GroundKills;
        public string Result = ResultLanded;
        public DateTime StartedAt = DateTime.UtcNow;
        public bool Active;

        // Last radar altitude seen while the simulation was running (updated from
        // Aircraft.CheckRadarAlt), so a mission restart or quit is judged on the state
        // right before the player paused, not on whatever the unloading scene reports.
        public float LastRadarAlt;

        public void BeginFlight(Aircraft aircraft)
        {
            if (aircraft == null) return;

            // Never silently overwrite a flight that is still open.
            if (Current.Active && !ReferenceEquals(Current.TrackedAircraft, aircraft))
            {
                Current.ResolveInterrupted("a new aircraft was spawned");
            }

            string niceName = aircraft.definition != null ? aircraft.definition.unitName : aircraft.name;

            Current = new FlightState
            {
                AircraftName = niceName,
                TrackedAircraft = aircraft,
                AirKills = 0,
                GroundKills = 0,
                StartedAt = DateTime.UtcNow,
                Active = true,
                Result = ResultLanded,
                LastRadarAlt = aircraft.radarAlt
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

            var record = new FlightRecord
            {
                Timestamp = DateTime.Now,
                Aircraft = AircraftName ?? "Unknown",
                AirKills = AirKills,
                GroundKills = GroundKills,
                Result = Result,
                DurationSeconds = durationSeconds
            };

            StatsStorage.SaveFlight(record);
            ProgressionManager.AddExperienceFromFlight(record);
        }

        /// <summary>
        /// Closes a flight that ended without a landing exit, ejection or shoot-down
        /// (mission restart, quit to menu or game, aircraft removed). On the ground it
        /// counts as landed. In the air it counts as shot down: leaving mid-flight loses
        /// the aircraft, so quitting cannot be used to dodge a shoot-down in the stats.
        /// </summary>
        public void ResolveInterrupted(string reason)
        {
            if (!Active) return;

            string name = AircraftName ?? "Unknown";
            bool onGround = LastRadarAlt < GroundRadarAltitude;
            string result = onGround ? ResultLanded : ResultShotDown;

            Plugin.Log?.LogInfo($"[FlightState] {name}: {reason} {(onGround ? "on the ground" : "while airborne")} (radar alt {LastRadarAlt:F1} m), saved as {result}");
            EndFlight(result);
        }
    }
}