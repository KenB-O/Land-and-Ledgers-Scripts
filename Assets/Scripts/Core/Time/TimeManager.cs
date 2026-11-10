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
        // Keeps economic event indices non-negative while allowing a scenario's
        // Gregorian calendar to begin before the shared calendar epoch.
        private int calendarDayOffset;
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
        /// <summary>
        /// Calendar epoch for the current campaign timeline. A new scenario can
        /// begin before the shared serialized settings epoch; keeping its own
        /// runtime epoch lets all date-index consumers (birthdays, seasons and
        /// demographic events) agree with the date shown by the clock without
        /// assigning negative campaign day IDs.
        /// </summary>
        public CalendarAnchor TimelineAnchor
        {
            get
            {
                if (settings == null) return CalendarAnchor.Default;
                settings.Anchor.FromAbsoluteDay(calendarDayOffset, out int year, out int month, out int day);
                return new CalendarAnchor(year, month, day);
            }
        }
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

        /// <summary>
        /// Pass IV (Worker 1): absolute game seconds on the authoritative timeline —
        /// the time base for the due-event schedule. Never a second clock.
        /// </summary>
        public float AbsoluteGameSeconds =>
            settings != null
                ? (float)((double)absoluteDayIndex * settings.GameSecondsPerDay + gameSecondsIntoDay)
                : gameSecondsIntoDay;
        public int ShortTickIndex => shortTickIndex;
        public bool IsPaused => isPaused;
        public float CurrentSpeedMultiplier => ResolveSpeedMultiplier(currentSpeed);
        /// <summary>Authoritative game seconds advanced per unscaled real second at the selected speed.</summary>
        public float CurrentGameSecondsPerRealSecond => settings != null
            ? ResolveSpeedMultiplier(currentSpeed) * settings.GameSecondsPerDay / Mathf.Max(1f, settings.realSecondsPerGameDayAt1x)
            : currentSpeed == SimulationSpeed.Paused ? 0f : currentSpeed == SimulationSpeed.RealTime ? 1f : ResolveSpeedMultiplier(currentSpeed);
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

        /// <summary>
        /// Advances the authoritative simulation clock without waiting for wall-clock
        /// frames. This is intentionally a clock operation only: all normal ShortTick,
        /// DayChanged and other simulation listeners still receive their ordinary events.
        /// Development/editorial validation uses this to make long authored scenarios
        /// repeatable without introducing a second debug clock.
        /// </summary>
        public void AdvanceForValidation(float gameSeconds)
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
            float remainingGameSeconds = Mathf.Max(0f, gameSeconds);

            // Validation acceleration is still the authoritative clock, but a large
            // single call must not trigger the ordinary per-frame rollover cap. That
            // cap intentionally compresses visual/runtime frames; it would otherwise
            // skip DayChanged/WeekChanged listeners that post payroll, profit and
            // scenario progress. Process at most one authored day per step so every
            // ordinary calendar event is delivered in order.
            float oneDay = Mathf.Max(1f, settings.GameSecondsPerDay);
            while (remainingGameSeconds > 0f)
            {
                float step = Mathf.Min(remainingGameSeconds, oneDay);
                AdvanceGameSeconds(step);
                remainingGameSeconds -= step;
            }
        }

        /// <summary>
        /// Begins a fresh scenario on its authored Gregorian date. The campaign
        /// day index starts at zero, so existing economic authorities that use
        /// non-negative day indices and -1 sentinels remain save-compatible.
        /// </summary>
        public bool StartNewScenarioAtDate(int year, int month, int day)
        {
            if (settings == null || year < 1 || month < 1 || month > 12
                || day < 1 || day > GregorianDateMath.DaysInMonth(year, month))
            {
                return false;
            }

            settings.Sanitize();
            calendarDayOffset = settings.Anchor.ToAbsoluteDay(year, month, day);
            absoluteDayIndex = 0;
            currentDate = BuildTimelineDate();
            ApplyTimeOfDay(settings.startingTimeOfDay01 * settings.GameSecondsPerDay);
            ResetShortTickStateForCurrentTimeOfDay();
            ResetFrameDiagnostics();
            initialized = true;
            RefreshDebugState();
            return currentDate.Year == year && currentDate.Month == month && currentDate.DayOfMonth == day;
        }

        public TimeSaveDto CaptureSaveDto()
        {
            return new TimeSaveDto
            {
                currentDate = currentDate,
                absoluteDayIndex = absoluteDayIndex,
                calendarDayOffset = calendarDayOffset,
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
            calendarDayOffset = dto.calendarDayOffset;
            currentDate = settings != null ? BuildTimelineDate() : dto.currentDate;

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
            LandLedgers.Presentation.DayNightVisualController.EnsureAttached(this);
        }

        private void OnEnable()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            InitializeState(false);
            if (Instance == this)
            {
                LandLedgers.Presentation.DayNightVisualController.EnsureAttached(this);
            }
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
            return speed switch
            {
                SimulationSpeed.Paused => 0f,
                SimulationSpeed.RealTime => 1f,
                _ => Mathf.Max(0f, (float)(int)speed)
            };
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
            // Pass V P0-1: the clock starts on the scenario start date derived
            // from the Gregorian anchor (default 1892-03-01). absoluteDayIndex
            // remains the persisted truth; the start is an anchor offset now.
            calendarDayOffset = 0;
            absoluteDayIndex = settings.GetScenarioStartAbsoluteDayIndex();
            currentDate = BuildTimelineDate();
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

            int remainingIntSpace = int.MaxValue - absoluteDayIndex;
            daysToAdvance = Mathf.Min(daysToAdvance, Mathf.Max(0, remainingIntSpace));

            collapsedDayRolloversLastFrame = daysToAdvance;
            cappedDayRolloverFrames++;

            // Pass IV (Worker 1): every crossed day runs its daily drivers. DayChanged
            // (and WeekChanged/MonthChanged as crossed) fires PER DAY through
            // ProcessDayRollover, so payroll weeks and production days are never
            // silently skipped by a collapsed rollover.
            for (int day = 0; day < daysToAdvance; day++)
            {
                // The first boundary was already accumulated by AdvanceGameSeconds.
                // Later days must accrue their own work intervals before rollover.
                if (day > 0) shortTickAccumulatorGameSeconds += settings.GameSecondsPerDay;
                // Normalize: each iteration rolls exactly one pending day.
                gameSecondsIntoDay = settings.GameSecondsPerDay;
                ProcessDayRollover();
            }

            float remainder = (float)(totalSecondsToPlace - daysToAdvance * secondsPerDay);
            ApplyTimeOfDay(remainder);
            shortTickAccumulatorGameSeconds += remainder;
            ProcessShortTicks();

            if (logMajorTicks)
            {
                RefreshDebugState();
                Debug.Log(
                    $"Simulation date advanced to {GetReadableDate()} after processing {daysToAdvance} capped day rollover(s) individually.",
                    this);
            }
        }

        private void ProcessShortTicks()
        {
            float interval = settings.ShortTickIntervalGameSeconds;
            int ticksProcessed = 0;
            double secondsPerDay = settings.GameSecondsPerDay;
            double absoluteSecondsAtEnd = (double)absoluteDayIndex * secondsPerDay + gameSecondsIntoDay;

            while (shortTickAccumulatorGameSeconds >= interval && ticksProcessed < settings.maxShortTicksPerFrame)
            {
                shortTickAccumulatorGameSeconds -= interval;
                ticksProcessed++;

                // A high-speed frame may accumulate several ShortTicks before
                // this method runs. Give each callback the time at the end of
                // its own interval, not the final clock time for the frame.
                // Otherwise scheduled-work consumers can count the same
                // reservation overlap once for every tick in the burst.
                double tickEndAbsoluteSeconds = absoluteSecondsAtEnd - shortTickAccumulatorGameSeconds;
                int tickDayIndex = AbsoluteDayIndexForTick(tickEndAbsoluteSeconds, secondsPerDay);
                double tickSecondsIntoDay = tickEndAbsoluteSeconds - (double)tickDayIndex * secondsPerDay;
                tickSecondsIntoDay = Math.Max(0d, Math.Min(secondsPerDay, tickSecondsIntoDay));
                int tickIndex = Math.Max(0, (int)Math.Floor(tickSecondsIntoDay / interval + 0.000001d));
                shortTickIndex = tickIndex;
                SimulationDate tickDate = BuildTimelineDate(tickDayIndex);
                float tickTimeOfDay01 = (float)(tickSecondsIntoDay / secondsPerDay);

                ShortTick?.Invoke(new SimulationTickContext(
                    tickDate,
                    shortTickIndex,
                    interval,
                    tickTimeOfDay01,
                    (float)tickSecondsIntoDay));
            }

            if (ticksProcessed >= settings.maxShortTicksPerFrame)
            {
                RecordShortTickCompression(interval);
                // Pass IV (Worker 1): DEFERRAL, not discard. The leftover accumulator
                // carries into later frames so accelerated time never loses simulated
                // seconds; the per-frame cap still bounds work per frame.
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
            // Complete the closing day's work before daily drivers reset its
            // reservations, budgets, or needs. Ordinary intraday frames still
            // defer excess ticks; crossing midnight must preserve event order.
            while (shortTickAccumulatorGameSeconds >= settings.ShortTickIntervalGameSeconds)
            {
                ProcessShortTicks();
            }
            gameSecondsIntoDay -= settings.GameSecondsPerDay;
            SimulationDate previousDate = currentDate;
            absoluteDayIndex++;
            currentDate = BuildTimelineDate();
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

        private SimulationDate BuildTimelineDate()
        {
            return BuildTimelineDate(absoluteDayIndex);
        }

        private SimulationDate BuildTimelineDate(int dayIndex)
        {
            if (settings == null)
            {
                return currentDate;
            }

            settings.Anchor.FromAbsoluteDay(calendarDayOffset + dayIndex,
                out int year, out int month, out int day);
            int weekday = GregorianDateMath.DayOfWeekMondayZero(year, month, day);
            int week = dayIndex / 7 + 1;
            return new SimulationDate(
                dayIndex,
                weekday,
                day,
                week,
                (day - 1) / 7 + 1,
                month,
                year);
        }

        private static int AbsoluteDayIndexForTick(double tickEndAbsoluteSeconds, double secondsPerDay)
        {
            if (secondsPerDay <= 0d || tickEndAbsoluteSeconds <= 0d) return 0;
            // An interval ending exactly at midnight belongs to the day it
            // just completed. The next interval belongs to the new day.
            return Math.Max(0, (int)Math.Ceiling(tickEndAbsoluteSeconds / secondsPerDay - 0.0000001d) - 1);
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
            // Pass VIII (Worker 9): whole deferred ticks carry across midnight
            // instead of being silently dropped. ProcessShortTicks promises
            // deferral-not-discard ("accelerated time never loses simulated
            // seconds"); resetting the accumulator to the bare intraday
            // remainder broke that promise at every day boundary where the
            // per-frame cap had bound. The per-frame cap still bounds how many
            // fire per call — the remainder keeps deferring into later frames.
            // shortTickIndex restarts at the new day's intraday position; it is
            // only a determinism salt, never a conserved counter.
            float carriedWholeTicks = Mathf.Floor(
                Mathf.Max(0f, shortTickAccumulatorGameSeconds) / Mathf.Max(0.001f, interval)) * interval;
            shortTickAccumulatorGameSeconds = carriedWholeTicks + Mathf.Repeat(gameSecondsIntoDay, interval);
            shortTickIndex = Mathf.FloorToInt(gameSecondsIntoDay / interval);
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

        public string GetReadableClock()
        {
            int totalMinutes = Mathf.FloorToInt(timeOfDay01 * 24f * 60f);
            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            return $"{hours:00}:{minutes:00}";
        }
    }
}
