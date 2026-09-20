using System;
using LandLedgers.Persistence;
using UnityEngine;

namespace LandLedgers.Time
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class TimeManager : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField]
        private SimulationTimeSettings settings;

        [Header("Runtime State")]
        [SerializeField]
        private SimulationSpeed currentSpeed = SimulationSpeed.Speed1x;

        [SerializeField]
        private SimulationSpeed lastNonPausedSpeed = SimulationSpeed.Speed1x;

        [SerializeField]
        private SimulationDate currentDate;

        [SerializeField]
        [Range(0f, 0.999f)]
        private float timeOfDay01;

        [SerializeField]
        private float gameSecondsIntoDay;

        [SerializeField]
        private int shortTickIndex;

        [SerializeField]
        private bool isPaused;

        [Header("Debug")]
        [SerializeField]
        private bool logMajorTicks;

        [SerializeField]
        private string currentDayName;

        [SerializeField]
        private string currentMonthName;

        [SerializeField]
        private string currentSeasonName;

        [SerializeField]
        private string readableState;

        [SerializeField]
        private int collapsedDayRolloversLastFrame;

        [SerializeField]
        private int cappedDayRolloverFrames;

        [SerializeField]
        private int compressedShortTicksLastFrame;

        [SerializeField]
        private int cappedShortTickFrames;

        private int absoluteDayIndex;
        private float shortTickAccumulatorGameSeconds;
        private bool initialized;
        private bool shortTickCapHitThisFrame;

        public event Action<SimulationFrameContext> VisualFrameUpdated;
        public event Action<SimulationTickContext> ShortTick;
        public event Action<SimulationDateChangedContext> DayChanged;
        public event Action<SimulationDateChangedContext> WeekChanged;
        public event Action<SimulationDateChangedContext> MonthChanged;
        public event Action<SimulationSpeedChangedContext> SpeedChanged;
        public event Action<bool> PauseStateChanged;

        public static TimeManager Instance { get; private set; }

        public SimulationTimeSettings Settings => settings;
        public SimulationSpeed CurrentSpeed => currentSpeed;
        public SimulationDate CurrentDate => currentDate;
        public int CurrentDayIndex => currentDate.DayOfWeekIndex;
        public int CurrentAbsoluteDayIndex => absoluteDayIndex;
        public string CurrentDayName => currentDayName;
        public int CurrentWeek => currentDate.Week;
        public int CurrentMonth => currentDate.Month;
        public string CurrentMonthName => currentMonthName;
        public int CurrentSeasonIndex => settings != null ? settings.GetSeasonIndex(currentDate) : 0;
        public string CurrentSeasonName => currentSeasonName;
        public int CurrentYear => currentDate.Year;
        public float TimeOfDay01 => timeOfDay01;
        public float GameSecondsIntoDay => gameSecondsIntoDay;
        public int ShortTickIndex => shortTickIndex;
        public bool IsPaused => isPaused;
        public float CurrentSpeedMultiplier => ResolveSpeedMultiplier(currentSpeed);
        public int CollapsedDayRolloversLastFrame => collapsedDayRolloversLastFrame;
        public int CappedDayRolloverFrames => cappedDayRolloverFrames;
        public int CompressedShortTicksLastFrame => compressedShortTicksLastFrame;
        public int CappedShortTickFrames => cappedShortTickFrames;

        public void SetSpeed(SimulationSpeed speed)
        {
            SimulationSpeed previousSpeed = currentSpeed;
            float previousMultiplier = ResolveSpeedMultiplier(previousSpeed);
            bool wasPaused = isPaused;

            currentSpeed = speed;
            float currentMultiplier = ResolveSpeedMultiplier(currentSpeed);
            isPaused = currentSpeed == SimulationSpeed.Paused || currentMultiplier <= 0f;
            if (!isPaused)
            {
                lastNonPausedSpeed = currentSpeed;
            }

            RefreshDebugState();

            if (previousSpeed != currentSpeed || !Mathf.Approximately(previousMultiplier, currentMultiplier))
            {
                SpeedChanged?.Invoke(new SimulationSpeedChangedContext(
                    previousSpeed,
                    currentSpeed,
                    previousMultiplier,
                    currentMultiplier));
            }

            if (wasPaused != isPaused)
            {
                PauseStateChanged?.Invoke(isPaused);
            }
        }

        public void SetPaused(bool paused)
        {
            if (paused)
            {
                SetSpeed(SimulationSpeed.Paused);
                return;
            }

            if (currentSpeed == SimulationSpeed.Paused)
            {
                SetSpeed(lastNonPausedSpeed != SimulationSpeed.Paused ? lastNonPausedSpeed : SimulationSpeed.Speed1x);
            }
        }

        public void TogglePaused()
        {
            SetPaused(!isPaused);
        }

        public void ResetToStartingDate()
        {
            InitializeState(true);
        }

        public void JumpToDate(int year, int month, int weekOfMonth, int dayOfWeek, float newTimeOfDay01)
        {
            if (settings == null)
            {
                return;
            }

            settings.Sanitize();
            int clampedYear = Mathf.Max(1, year);
            int clampedMonth = Mathf.Clamp(month, 1, settings.monthsPerYear);
            int clampedWeek = Mathf.Clamp(weekOfMonth, 1, settings.weeksPerMonth);
            int clampedDay = Mathf.Clamp(dayOfWeek, 1, settings.daysPerWeek);

            int yearsSinceStart = Mathf.Max(0, clampedYear - settings.startingYear);
            int monthOffset = yearsSinceStart * settings.monthsPerYear + (clampedMonth - settings.startingMonth);
            int dayOffset =
                monthOffset * settings.DaysPerMonth
                + (clampedWeek - settings.startingWeekOfMonth) * settings.daysPerWeek
                + (clampedDay - settings.startingDayOfWeek);

            absoluteDayIndex = Mathf.Max(0, dayOffset);
            currentDate = settings.BuildDateFromAbsoluteDay(absoluteDayIndex);
            ApplyTimeOfDay(Mathf.Clamp(newTimeOfDay01, 0f, 0.999f) * settings.GameSecondsPerDay);
            ResetShortTickStateForCurrentTimeOfDay();
            ResetFrameDiagnostics();
            RefreshDebugState();
        }

        public TimeSaveDto CaptureSaveDto()
        {
            return new TimeSaveDto
            {
                currentDate = currentDate,
                absoluteDayIndex = absoluteDayIndex,
                timeOfDay01 = timeOfDay01,
                gameSecondsIntoDay = gameSecondsIntoDay,
                shortTickIndex = shortTickIndex,
                shortTickAccumulatorGameSeconds = shortTickAccumulatorGameSeconds,
                currentSpeed = currentSpeed,
                lastNonPausedSpeed = lastNonPausedSpeed,
                isPaused = isPaused
            };
        }

        public void LoadFromSaveDto(TimeSaveDto dto)
        {
            if (dto == null)
            {
                return;
            }

            if (settings != null)
            {
                settings.Sanitize();
            }

            absoluteDayIndex = Mathf.Max(0, dto.absoluteDayIndex);
            currentDate = settings != null
                ? settings.BuildDateFromAbsoluteDay(absoluteDayIndex)
                : dto.currentDate;

            if (settings != null)
            {
                ApplyTimeOfDay(dto.gameSecondsIntoDay);
            }
            else
            {
                gameSecondsIntoDay = Mathf.Max(0f, dto.gameSecondsIntoDay);
                timeOfDay01 = Mathf.Clamp(dto.timeOfDay01, 0f, 0.999f);
            }

            shortTickIndex = Mathf.Max(0, dto.shortTickIndex);
            shortTickAccumulatorGameSeconds = Mathf.Max(0f, dto.shortTickAccumulatorGameSeconds);
            currentSpeed = dto.currentSpeed;
            lastNonPausedSpeed = dto.lastNonPausedSpeed == SimulationSpeed.Paused ? SimulationSpeed.Speed1x : dto.lastNonPausedSpeed;
            isPaused = dto.isPaused || currentSpeed == SimulationSpeed.Paused || ResolveSpeedMultiplier(currentSpeed) <= 0f;
            if (isPaused)
            {
                currentSpeed = SimulationSpeed.Paused;
            }

            ResetFrameDiagnostics();
            initialized = true;
            RefreshDebugState();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"Multiple TimeManager instances found. Disabling duplicate on {name}.", this);
                enabled = false;
                return;
            }

            Instance = this;
            InitializeState(false);
        }

        private void OnEnable()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            InitializeState(false);
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnValidate()
        {
            settings?.Sanitize();

            if (!Application.isPlaying)
            {
                InitializeState(true);
            }
        }

        private void Update()
        {
            if (settings == null)
            {
                return;
            }

            if (!initialized)
            {
                InitializeState(false);
            }

            settings.Sanitize();
            float realDeltaSeconds = UnityEngine.Time.unscaledDeltaTime;
            float speedMultiplier = ResolveSpeedMultiplier(currentSpeed);
            isPaused = currentSpeed == SimulationSpeed.Paused || speedMultiplier <= 0f;

            if (realDeltaSeconds <= 0f || isPaused)
            {
                VisualFrameUpdated?.Invoke(new SimulationFrameContext(
                    currentDate,
                    currentSpeed,
                    speedMultiplier,
                    Mathf.Max(0f, realDeltaSeconds),
                    0f,
                    timeOfDay01,
                    gameSecondsIntoDay));
                return;
            }

            float gameDeltaSeconds = realDeltaSeconds * speedMultiplier * (settings.GameSecondsPerDay / settings.realSecondsPerGameDayAt1x);
            AdvanceGameSeconds(gameDeltaSeconds);

            VisualFrameUpdated?.Invoke(new SimulationFrameContext(
                currentDate,
                currentSpeed,
                speedMultiplier,
                realDeltaSeconds,
                gameDeltaSeconds,
                timeOfDay01,
                gameSecondsIntoDay));
        }

        private float ResolveSpeedMultiplier(SimulationSpeed speed)
        {
            if (settings != null)
            {
                return settings.GetMultiplier(speed);
            }

            // Missing settings should stop simulation advancement, but speed UI/debug listeners
            // still need a stable fallback multiplier when SetSpeed is called during bootstrap or repair.
            return speed == SimulationSpeed.Paused ? 0f : Mathf.Max(0f, (float)(int)speed);
        }

        private void InitializeState(bool force)
        {
            if (initialized && !force)
            {
                return;
            }

            if (settings == null)
            {
                initialized = false;
                return;
            }

            settings.Sanitize();
            absoluteDayIndex = 0;
            currentDate = settings.BuildDateFromAbsoluteDay(absoluteDayIndex);
            ApplyTimeOfDay(settings.startingTimeOfDay01 * settings.GameSecondsPerDay);
            ResetShortTickStateForCurrentTimeOfDay();
            currentSpeed = settings.startingSpeed;
            isPaused = currentSpeed == SimulationSpeed.Paused || ResolveSpeedMultiplier(currentSpeed) <= 0f;
            lastNonPausedSpeed = isPaused ? SimulationSpeed.Speed1x : currentSpeed;
            ResetFrameDiagnostics();
            initialized = true;
            RefreshDebugState();
        }

        private void AdvanceGameSeconds(float gameDeltaSeconds)
        {
            if (gameDeltaSeconds <= 0f)
            {
                return;
            }

            ResetFrameDiagnostics();
            float remainingGameSeconds = gameDeltaSeconds;
            int rolloversProcessed = 0;

            while (remainingGameSeconds > 0f)
            {
                float secondsUntilTomorrow = Mathf.Max(0f, settings.GameSecondsPerDay - gameSecondsIntoDay);
                float step = Mathf.Min(remainingGameSeconds, secondsUntilTomorrow);
                gameSecondsIntoDay += step;
                shortTickAccumulatorGameSeconds += step;
                remainingGameSeconds -= step;

                RefreshTimeOfDay01FromCurrentSeconds();
                ProcessShortTicks();

                if (gameSecondsIntoDay < settings.GameSecondsPerDay)
                {
                    continue;
                }

                if (rolloversProcessed >= settings.maxDayRolloversPerFrame)
                {
                    CollapseRemainingDayRollovers(remainingGameSeconds);
                    remainingGameSeconds = 0f;
                    break;
                }

                ProcessDayRollover();
                rolloversProcessed++;
            }

            ApplyTimeOfDay(gameSecondsIntoDay);
            RefreshDebugState();
        }

        private void CollapseRemainingDayRollovers(float remainingGameSeconds)
        {
            double secondsPerDay = settings.GameSecondsPerDay;
            double totalSecondsToPlace = Math.Max(0d, (double)gameSecondsIntoDay + Math.Max(0d, remainingGameSeconds));
            int daysToAdvance = Math.Max(0, (int)Math.Floor(totalSecondsToPlace / secondsPerDay));
            if (daysToAdvance <= 0)
            {
                ApplyTimeOfDay((float)totalSecondsToPlace);
                return;
            }

            SimulationDate previousDate = currentDate;
            int remainingIntSpace = int.MaxValue - absoluteDayIndex;
            daysToAdvance = Mathf.Min(daysToAdvance, Mathf.Max(0, remainingIntSpace));
            double finalSecondsIntoDay = totalSecondsToPlace - (daysToAdvance * secondsPerDay);

            absoluteDayIndex += daysToAdvance;
            currentDate = settings.BuildDateFromAbsoluteDay(absoluteDayIndex);
            ApplyTimeOfDay((float)finalSecondsIntoDay);
            ResetShortTickStateForCurrentTimeOfDay();

            collapsedDayRolloversLastFrame = daysToAdvance;
            cappedDayRolloverFrames++;
            RecordCollapsedShortTickCompression(remainingGameSeconds);

            // A hitch or test fast-forward can cross more days than subscribers should
            // process in one frame. Keep the calendar truthful and emit one collapsed
            // date-change signal instead of silently wrapping time-of-day backward.
            DayChanged?.Invoke(new SimulationDateChangedContext(previousDate, currentDate));

            if (previousDate.Week != currentDate.Week)
            {
                WeekChanged?.Invoke(new SimulationDateChangedContext(previousDate, currentDate));
            }

            if (previousDate.Month != currentDate.Month || previousDate.Year != currentDate.Year)
            {
                MonthChanged?.Invoke(new SimulationDateChangedContext(previousDate, currentDate));
            }

            if (logMajorTicks)
            {
                RefreshDebugState();
                Debug.Log(
                    $"Simulation date advanced to {GetReadableDate()} after collapsing {daysToAdvance} capped day rollover(s).",
                    this);
            }
        }

        private void ProcessShortTicks()
        {
            float interval = settings.ShortTickIntervalGameSeconds;
            int ticksProcessed = 0;

            while (shortTickAccumulatorGameSeconds >= interval && ticksProcessed < settings.maxShortTicksPerFrame)
            {
                shortTickAccumulatorGameSeconds -= interval;
                shortTickIndex++;
                ticksProcessed++;

                ShortTick?.Invoke(new SimulationTickContext(
                    currentDate,
                    shortTickIndex,
                    interval,
                    timeOfDay01,
                    gameSecondsIntoDay));
            }

            if (ticksProcessed >= settings.maxShortTicksPerFrame)
            {
                RecordShortTickCompression(interval);
                shortTickAccumulatorGameSeconds = Mathf.Min(shortTickAccumulatorGameSeconds, interval);
            }
        }

        private void RecordShortTickCompression(float interval)
        {
            int unprocessedFullTicks = Mathf.Max(0, Mathf.FloorToInt(shortTickAccumulatorGameSeconds / Mathf.Max(0.001f, interval)));
            if (unprocessedFullTicks <= 0)
            {
                return;
            }

            compressedShortTicksLastFrame += unprocessedFullTicks;
            if (!shortTickCapHitThisFrame)
            {
                cappedShortTickFrames++;
                shortTickCapHitThisFrame = true;
            }
        }

        private void RecordCollapsedShortTickCompression(float skippedGameSeconds)
        {
            float interval = settings.ShortTickIntervalGameSeconds;
            if (interval <= 0f || skippedGameSeconds <= 0f)
            {
                return;
            }

            int collapsedTicks = Math.Max(0, (int)Math.Floor(skippedGameSeconds / interval));
            if (collapsedTicks <= 0)
            {
                return;
            }

            compressedShortTicksLastFrame += collapsedTicks;
            if (!shortTickCapHitThisFrame)
            {
                cappedShortTickFrames++;
                shortTickCapHitThisFrame = true;
            }
        }

        private void ProcessDayRollover()
        {
            gameSecondsIntoDay -= settings.GameSecondsPerDay;
            SimulationDate previousDate = currentDate;
            absoluteDayIndex++;
            currentDate = settings.BuildDateFromAbsoluteDay(absoluteDayIndex);
            ResetShortTickStateForCurrentTimeOfDay();

            DayChanged?.Invoke(new SimulationDateChangedContext(previousDate, currentDate));

            if (previousDate.Week != currentDate.Week)
            {
                WeekChanged?.Invoke(new SimulationDateChangedContext(previousDate, currentDate));
            }

            if (previousDate.Month != currentDate.Month || previousDate.Year != currentDate.Year)
            {
                MonthChanged?.Invoke(new SimulationDateChangedContext(previousDate, currentDate));
            }

            if (logMajorTicks)
            {
                RefreshDebugState();
                Debug.Log($"Simulation date advanced to {GetReadableDate()}.", this);
            }
        }

        private void ApplyTimeOfDay(float newGameSecondsIntoDay)
        {
            gameSecondsIntoDay = Mathf.Clamp(newGameSecondsIntoDay, 0f, settings.GameSecondsPerDay - 0.001f);
            timeOfDay01 = gameSecondsIntoDay / settings.GameSecondsPerDay;
        }

        private void RefreshTimeOfDay01FromCurrentSeconds()
        {
            timeOfDay01 = Mathf.Clamp(gameSecondsIntoDay / settings.GameSecondsPerDay, 0f, 0.999f);
        }

        private void ResetShortTickStateForCurrentTimeOfDay()
        {
            float interval = settings.ShortTickIntervalGameSeconds;
            shortTickIndex = Mathf.FloorToInt(gameSecondsIntoDay / interval);
            shortTickAccumulatorGameSeconds = Mathf.Repeat(gameSecondsIntoDay, interval);
        }

        private void ResetFrameDiagnostics()
        {
            collapsedDayRolloversLastFrame = 0;
            compressedShortTicksLastFrame = 0;
            shortTickCapHitThisFrame = false;
        }

        private void RefreshDebugState()
        {
            if (settings == null)
            {
                currentDayName = string.Empty;
                currentMonthName = string.Empty;
                currentSeasonName = string.Empty;
                readableState = "No settings assigned.";
                return;
            }

            currentDayName = settings.GetDayName(currentDate.DayOfWeekIndex);
            currentMonthName = settings.GetMonthName(currentDate.Month);
            currentSeasonName = settings.GetSeasonName(currentDate);
            readableState = $"{GetReadableDate()} | {currentSeasonName} | {GetReadableClock()} | {currentSpeed}";
            if (collapsedDayRolloversLastFrame > 0)
            {
                readableState += $" | collapsed day rollovers: {collapsedDayRolloversLastFrame}";
            }

            if (compressedShortTicksLastFrame > 0)
            {
                readableState += $" | compressed short ticks: {compressedShortTicksLastFrame}";
            }
        }

        public string GetReadableDate()
        {
            return $"{currentDayName}, {currentMonthName} {currentDate.DayOfMonth}, Year {currentDate.Year} (Week {currentDate.Week})";
        }

        public string GetReadableDateWithSeason()
        {
            return $"{GetReadableDate()} - {currentSeasonName}";
        }

        public string GetReadableClock()
        {
            int totalMinutes = Mathf.FloorToInt(timeOfDay01 * 24f * 60f);
            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            return $"{hours:00}:{minutes:00}";
        }
    }
}
