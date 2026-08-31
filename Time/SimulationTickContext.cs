using System;

namespace LandLedgers.Time
{
    public readonly struct SimulationFrameContext
    {
        public SimulationFrameContext(
            SimulationDate date,
            SimulationSpeed speed,
            float speedMultiplier,
            float realDeltaSeconds,
            float gameDeltaSeconds,
            float timeOfDay01,
            float gameSecondsIntoDay)
        {
            Date = date;
            Speed = speed;
            SpeedMultiplier = speedMultiplier;
            RealDeltaSeconds = realDeltaSeconds;
            GameDeltaSeconds = gameDeltaSeconds;
            TimeOfDay01 = timeOfDay01;
            GameSecondsIntoDay = gameSecondsIntoDay;
        }

        public SimulationDate Date { get; }
        public SimulationSpeed Speed { get; }
        public float SpeedMultiplier { get; }
        public float RealDeltaSeconds { get; }
        public float GameDeltaSeconds { get; }
        public float TimeOfDay01 { get; }
        public float GameSecondsIntoDay { get; }
    }

    public readonly struct SimulationTickContext
    {
        public SimulationTickContext(
            SimulationDate date,
            int tickIndex,
            float tickIntervalGameSeconds,
            float timeOfDay01,
            float gameSecondsIntoDay)
        {
            Date = date;
            TickIndex = tickIndex;
            TickIntervalGameSeconds = tickIntervalGameSeconds;
            TimeOfDay01 = timeOfDay01;
            GameSecondsIntoDay = gameSecondsIntoDay;
        }

        public SimulationDate Date { get; }
        public int TickIndex { get; }
        public float TickIntervalGameSeconds { get; }
        public float TimeOfDay01 { get; }
        public float GameSecondsIntoDay { get; }
    }

    public readonly struct SimulationDateChangedContext
    {
        public SimulationDateChangedContext(SimulationDate previousDate, SimulationDate currentDate)
        {
            PreviousDate = previousDate;
            CurrentDate = currentDate;
        }

        public SimulationDate PreviousDate { get; }
        public SimulationDate CurrentDate { get; }
    }

    public readonly struct SimulationSpeedChangedContext
    {
        public SimulationSpeedChangedContext(
            SimulationSpeed previousSpeed,
            SimulationSpeed currentSpeed,
            float previousMultiplier,
            float currentMultiplier)
        {
            PreviousSpeed = previousSpeed;
            CurrentSpeed = currentSpeed;
            PreviousMultiplier = previousMultiplier;
            CurrentMultiplier = currentMultiplier;
        }

        public SimulationSpeed PreviousSpeed { get; }
        public SimulationSpeed CurrentSpeed { get; }
        public float PreviousMultiplier { get; }
        public float CurrentMultiplier { get; }
        public bool IsPaused => CurrentSpeed == SimulationSpeed.Paused || Math.Abs(CurrentMultiplier) <= float.Epsilon;
    }
}
