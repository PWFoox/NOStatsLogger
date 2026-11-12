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
        /// (mission restart, quit to menu, aircraft removed). If the aircraft was on the
        /// ground it counts as landed; otherwise the flight is discarded, so an aborted
        /// sortie does not add a flight record or XP.
        /// </summary>
        public void ResolveInterrupted(string reason)
        {
            if (!Active) return;

            string name = AircraftName ?? "Unknown";

            if (LastRadarAlt < GroundRadarAltitude)
            {
                Plugin.Log?.LogInfo($"[FlightState] {name}: {reason} on the ground (radar alt {LastRadarAlt:F1} m), saved as landed");
                EndFlight(ResultLanded);
                return;
            }

            Active = false;
            Plugin.Log?.LogInfo($"[FlightState] {name} flight discarded: {reason} while airborne (radar alt {LastRadarAlt:F1} m)");
        }
    }
}