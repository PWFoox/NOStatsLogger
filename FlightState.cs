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
                Result = ResultLanded
            };

            Plugin.Log?.LogInfo($"[FlightState] BEGIN FLIGHT -> {niceName}");
        }

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

        // Меняет Result уже ЗАВЕРШЁННОГО вылета (Active уже false), не трогая
        // остальные поля. Нужно для случая: Player.SetAircraft принудительно
        // вызвала StartEjectionSequence на старом борту (сняла authority), из-за
        // чего наш патч на StartEjectionSequence успел выставить result=ejected —
        // но раз игрок после этого сел в другой самолёт (а не остался пилотом
        // без борта), это была на самом деле нормальная посадка, а не настоящее
        // катапультирование. Использовать ТОЛЬКО из Player_SetAircraft_Postfix.
        public void OverrideResult(string result)
        {
            if (Active)
            {
                // Не должно случаться — если вылет ещё активен, нужно звать
                // EndFlight, а не это. Просто логируем на всякий случай.
                Plugin.Log?.LogWarning("[FlightState] OverrideResult вызван для ещё активного вылета — проигнорировано.");
                return;
            }

            string oldResult = Result;
            Result = result;

            Plugin.Log?.LogInfo(
                $"[FlightState] RESULT OVERRIDE -> {AircraftName} | {oldResult} -> {result} " +
                "(из-за пересадки в другой самолёт сразу после принудительного StartEjectionSequence)."
            );
        }
    }
}