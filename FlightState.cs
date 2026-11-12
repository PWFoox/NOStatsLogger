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
        /// (mission restart, quit to menu, aircraft removed). If the aircraft is on the
        /// ground it counts as landed; otherwise the flight is discarded, so an aborted
        /// sortie does not add a flight record or XP.
        /// </summary>
        public void ResolveInterrupted(string reason)
        {
            if (!Active) return;

            bool landed = false;
            try
            {
                landed = !ReferenceEquals(TrackedAircraft, null) && TrackedAircraft.IsLanded();
            }
            catch (Exception)
            {
                landed = false;
            }

            if (landed)
            {
                EndFlight(ResultLanded);
                return;
            }

            Active = false;
            Plugin.Log?.LogInfo($"[FlightState] {AircraftName ?? "Unknown"} flight discarded: {reason} while airborne");
        }
    }
}