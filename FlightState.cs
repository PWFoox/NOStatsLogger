using System;

namespace NOStatsLogger
{
    // На этом шаге FlightState только собирает данные в памяти и пишет
    // в Plugin.Log — никакого похода на API/CSV. Это подключим отдельно,
    // когда по логам подтвердим, что все хуки срабатывают верно.
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

        // Флаг: мы получили OnTouchdown и ждём 0.7с — не окажется ли это на
        // самом деле сбитием/катапультированием, которые могут прийти чуть позже.
        // См. комментарий в HookPoints.cs про гонку между OnTouchdown и
        // onEject/UnitDisabled.
        public bool PendingLandConfirmation;

        public void BeginFlight(Aircraft aircraft)
        {
            if (aircraft == null)
                return;

            string niceName = aircraft.definition != null
                ? aircraft.definition.unitName
                : aircraft.name;

            Current = new FlightState
            {
                AircraftName = niceName,
                TrackedAircraft = aircraft,
                AirKills = 0,
                GroundKills = 0,
                StartedAt = DateTime.UtcNow,
                Active = true,
                PendingLandConfirmation = false,
                Result = ResultLanded
            };

            Plugin.Log?.LogInfo($"[FlightState] BEGIN FLIGHT -> {niceName}");
        }

        // Заготовка под фраги — вызывать будем, когда подключим хук FactionHQ.RewardPlayer.
        // isAirKill = true -> воздушный фраг, false -> наземный.
        public void RegisterKill(bool isAirKill)
        {
            if (!Active)
                return;

            if (isAirKill)
                AirKills++;
            else
                GroundKills++;

            Plugin.Log?.LogInfo($"[FlightState] KILL registered. air={AirKills} ground={GroundKills}");
        }

        public void EndFlight(string result)
        {
            if (!Active)
                return;

            Active = false;
            Result = result;

            int durationSeconds = (int)Math.Max(0, (DateTime.UtcNow - StartedAt).TotalSeconds);

            Plugin.Log?.LogInfo(
                $"[FlightState] END FLIGHT -> {AircraftName} | " +
                $"airKills={AirKills} groundKills={GroundKills} " +
                $"result={Result} duration={durationSeconds}s"
            );
        }
    }
}
