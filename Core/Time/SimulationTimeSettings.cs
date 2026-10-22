using UnityEngine;

namespace LandLedgers.Time
{
    [CreateAssetMenu(
        fileName = "SimulationTimeSettings",
        menuName = "Land & Ledgers/Time/Simulation Time Settings",
        order = 120)]
    public sealed class SimulationTimeSettings : ScriptableObject
    {
        [Header("Calendar")]
        [Min(1)]
        public int daysPerWeek = 7;

        /// <summary>
        /// Pass V P0-1 LEGACY: the 336-day fiction (4 weeks x 28 days per month)
        /// is removed. Real month lengths come from <see cref="GregorianDateMath"/>
        /// via the anchor below. Kept serialized so older settings assets still
        /// load; no longer read by any calendar derivation.
        /// </summary>
        [Min(1)]
        public int weeksPerMonth = 4;

        [Min(1)]
        public int monthsPerYear = 12;

        [Header("Gregorian Anchor")]
        [Tooltip("Pass V P0-1: the real Gregorian date that absoluteDayIndex 0 maps to. Default 1880-03-01 (a Monday). All Y/M/D derivation is true Gregorian math from this anchor: real month lengths, leap years, weekday alignment to history.")]
        public int anchorYear = CalendarAnchor.DefaultAnchorYear;

        [Tooltip("Pass V P0-1: anchor month (1-12).")]
        public int anchorMonth = CalendarAnchor.DefaultAnchorMonth;

        [Tooltip("Pass V P0-1: anchor day of month.")]
        public int anchorDay = CalendarAnchor.DefaultAnchorDay;

        [Tooltip("Editor-facing day labels. If the array is empty or too short, fallback names are used.")]
        public string[] dayNames =
        {
            "Monday",
            "Tuesday",
            "Wednesday",
            "Thursday",
            "Friday",
            "Saturday",
            "Sunday"
        };

        [Tooltip("Editor-facing month labels. If the array is empty or too short, numeric labels are used.")]
        public string[] monthNames =
        {
            "January",
            "February",
            "March",
            "April",
            "May",
            "June",
            "July",
            "August",
            "September",
            "October",
            "November",
            "December"
        };

        [Tooltip("Editor-facing season labels. Seasons are derived from the current month, not stored as separate save state.")]
        public string[] seasonNames =
        {
            "Winter",
            "Spring",
            "Summer",
            "Autumn"
        };

        [Header("Starting Date")]
        // Pass IX WS6 (A1-02) reconciliation: startingYear/Month/DayOfMonth are
        // the WORLD-CHRONOLOGY label of the default scenario — the era the
        // world presents as — NOT the session clock. The live session clock
        // derives from the calendar anchor (CalendarAnchor, default
        // 1880-03-01) plus the scenario preset's start date, resolved by
        // ScenarioBootstrapFactory and applied through
        // TimeManager.LoadFromSaveDto. Do not read these fields as "today".
        [Min(1)]
        public int startingYear = 1892;

        [Min(1)]
        public int startingMonth = 3;

        [Min(1)]
        [Tooltip("Pass V P0-1: scenario start day-of-month on the true Gregorian calendar. Replaces the 336-day week/day-of-week start fields below.")]
        public int startingDayOfMonth = 1;

        /// <summary>
        /// Pass V P0-1 LEGACY: week-of-month start belonged to the 336-day
        /// calendar. Kept serialized so older settings assets still load;
        /// the scenario start now uses <see cref="startingDayOfMonth"/>.
        /// </summary>
        [Min(1)]
        public int startingWeekOfMonth = 1;

        /// <summary>
        /// Pass V P0-1 LEGACY: day-of-week start belonged to the 336-day
        /// calendar. Kept serialized so older settings assets still load.
        /// </summary>
        [Min(1)]
        public int startingDayOfWeek = 1;

        [Range(0f, 0.999f)]
        public float startingTimeOfDay01 = 0.25f;

        [Header("Progression")]
        [Tooltip("Real seconds needed for one full in-game day at 1x speed.")]
        [Min(1f)]
        public float realSecondsPerGameDayAt1x = 60f;

        [Tooltip("Short simulation tick interval in in-game minutes. Used for schedules, economy sampling, and other small-step systems.")]
        [Min(1f)]
        public float shortTickIntervalGameMinutes = 15f;

        [Header("Speeds")]
        [Min(0f)]
        public float speed1xMultiplier = 1f;

        [Min(0f)]
        public float speed2xMultiplier = 2f;

        [Min(0f)]
        public float speed4xMultiplier = 4f;

        [Min(0f)]
        public float speed6xMultiplier = 6f;

        [Tooltip("Hidden/testing fast-forward multiplier.")]
        [Min(0f)]
        public float speed10xMultiplier = 10f;

        [Tooltip("Hidden/testing fast-forward multiplier.")]
        [Min(0f)]
        public float speed25xMultiplier = 25f;

        [Tooltip("Hidden/testing fast-forward multiplier.")]
        [Min(0f)]
        public float speed50xMultiplier = 50f;

        [Tooltip("Fast-forward multiplier.")]
        [Min(0f)]
        public float speed100xMultiplier = 100f;

        [Tooltip("Speed applied when the TimeManager starts.")]
        public SimulationSpeed startingSpeed = SimulationSpeed.Speed1x;

        [Header("Runtime Safety")]
        [Tooltip("Limits catch-up work during a frame spike so subscribers are not flooded by a single frame.")]
        [Min(1)]
        public int maxShortTicksPerFrame = 64;

        [Tooltip("Limits day rollover work during a frame spike or heavy fast-forward.")]
        [Min(1)]
        public int maxDayRolloversPerFrame = 7;

        /// <summary>
        /// Pass V P0-1: the explicit Gregorian anchor. absoluteDayIndex 0 maps to
        /// this date (default 1880-03-01, a Monday).
        /// </summary>
        public CalendarAnchor Anchor => new CalendarAnchor(anchorYear, anchorMonth, anchorDay);

        /// <summary>
        /// Pass V P0-1: the scenario start as an absolute day index, derived from
        /// the Gregorian anchor. The canon March start is preserved (default
        /// 1892-03-01): only the calendar math changed, not the start date.
        /// </summary>
        public int GetScenarioStartAbsoluteDayIndex()
        {
            CalendarAnchor anchor = Anchor;
            int month = Mathf.Clamp(startingMonth, 1, 12);
            int day = Mathf.Clamp(startingDayOfMonth, 1, GregorianDateMath.DaysInMonth(Mathf.Max(1, startingYear), month));
            return Mathf.Max(0, anchor.ToAbsoluteDay(Mathf.Max(1, startingYear), month, day));
        }

        public float GameSecondsPerDay => 24f * 60f * 60f;
        public float ShortTickIntervalGameSeconds => Mathf.Max(1f, shortTickIntervalGameMinutes) * 60f;

        public float GetMultiplier(SimulationSpeed speed)
        {
            return speed switch
            {
                SimulationSpeed.Paused => 0f,
                SimulationSpeed.Speed1x => speed1xMultiplier,
                SimulationSpeed.Speed2x => speed2xMultiplier,
                SimulationSpeed.Speed4x => speed4xMultiplier,
                SimulationSpeed.Speed6x => speed6xMultiplier,
                SimulationSpeed.Speed10x => speed10xMultiplier,
                SimulationSpeed.Speed25x => speed25xMultiplier,
                SimulationSpeed.Speed50x => speed50xMultiplier,
                SimulationSpeed.Speed100x => speed100xMultiplier,
                _ => speed1xMultiplier
            };
        }

        public string GetDayName(int zeroBasedDayOfWeekIndex)
        {
            int safeDaysPerWeek = Mathf.Max(1, daysPerWeek);
            int index = Mathf.Clamp(zeroBasedDayOfWeekIndex, 0, safeDaysPerWeek - 1);
            if (dayNames != null && index >= 0 && index < dayNames.Length && !string.IsNullOrWhiteSpace(dayNames[index]))
            {
                return dayNames[index];
            }

            return $"Day {index + 1}";
        }

        public string GetMonthName(int oneBasedMonth)
        {
            int safeMonthsPerYear = Mathf.Max(1, monthsPerYear);
            int index = Mathf.Clamp(oneBasedMonth - 1, 0, safeMonthsPerYear - 1);
            if (monthNames != null && index >= 0 && index < monthNames.Length && !string.IsNullOrWhiteSpace(monthNames[index]))
            {
                return monthNames[index];
            }

            return $"Month {index + 1}";
        }

        public int GetSeasonIndex(SimulationDate date)
        {
            return GetSeasonIndex(date.Month);
        }

        public int GetSeasonIndex(int oneBasedMonth)
        {
            int safeMonthsPerYear = Mathf.Max(1, monthsPerYear);
            int normalizedMonth = Mathf.Clamp(oneBasedMonth, 1, safeMonthsPerYear);

            if (safeMonthsPerYear == 12)
            {
                // The default Dakota-style calendar uses Northern Hemisphere seasons.
                // Winter wraps around year-end; spring begins with the canon March start.
                if (normalizedMonth == 12 || normalizedMonth <= 2)
                {
                    return 0;
                }

                if (normalizedMonth <= 5)
                {
                    return 1;
                }

                if (normalizedMonth <= 8)
                {
                    return 2;
                }

                return 3;
            }

            int safeSeasonCount = Mathf.Max(1, seasonNames != null && seasonNames.Length > 0 ? seasonNames.Length : 4);
            float normalizedYear = (normalizedMonth - 0.5f) / safeMonthsPerYear;
            return Mathf.Clamp(Mathf.FloorToInt(normalizedYear * safeSeasonCount), 0, safeSeasonCount - 1);
        }

        public string GetSeasonName(SimulationDate date)
        {
            return GetSeasonName(date.Month);
        }

        public string GetSeasonName(int oneBasedMonth)
        {
            int index = GetSeasonIndex(oneBasedMonth);
            if (seasonNames != null && index >= 0 && index < seasonNames.Length && !string.IsNullOrWhiteSpace(seasonNames[index]))
            {
                return seasonNames[index];
            }

            return index switch
            {
                0 => "Winter",
                1 => "Spring",
                2 => "Summer",
                3 => "Autumn",
                _ => $"Season {index + 1}"
            };
        }

        /// <summary>
        /// Pass V P0-1: true Gregorian derivation from the anchor. Real month
        /// lengths, leap years (1880 leap, 1900 not, 1904 leap), weekday
        /// alignment to real history. absoluteDayIndex stays the persisted truth;
        /// every other field is derived (additive migration: old saves recompute).
        /// </summary>
        public SimulationDate BuildDateFromAbsoluteDay(int absoluteDayIndex)
        {
            return Anchor.BuildDate(absoluteDayIndex);
        }

        public void Sanitize()
        {
            daysPerWeek = Mathf.Max(1, daysPerWeek);
            weeksPerMonth = Mathf.Max(1, weeksPerMonth);
            monthsPerYear = Mathf.Max(1, monthsPerYear);
            // New anchor fields on older settings assets deserialize as 0; repair
            // to the documented default rather than deriving from a broken anchor.
            if (anchorYear < 1 || anchorMonth < 1 || anchorMonth > 12 || anchorDay < 1)
            {
                anchorYear = CalendarAnchor.DefaultAnchorYear;
                anchorMonth = CalendarAnchor.DefaultAnchorMonth;
                anchorDay = CalendarAnchor.DefaultAnchorDay;
            }

            anchorDay = Mathf.Clamp(anchorDay, 1, GregorianDateMath.DaysInMonth(anchorYear, anchorMonth));
            startingYear = Mathf.Max(1, startingYear);
            startingMonth = Mathf.Clamp(startingMonth, 1, monthsPerYear);
            startingDayOfMonth = Mathf.Clamp(startingDayOfMonth, 1, GregorianDateMath.DaysInMonth(startingYear, startingMonth));
            startingWeekOfMonth = Mathf.Clamp(startingWeekOfMonth, 1, weeksPerMonth);
            startingDayOfWeek = Mathf.Clamp(startingDayOfWeek, 1, daysPerWeek);
            startingTimeOfDay01 = Mathf.Clamp(startingTimeOfDay01, 0f, 0.999f);
            realSecondsPerGameDayAt1x = Mathf.Max(1f, realSecondsPerGameDayAt1x);
            shortTickIntervalGameMinutes = Mathf.Max(1f, shortTickIntervalGameMinutes);
            speed1xMultiplier = Mathf.Max(0f, speed1xMultiplier);
            speed2xMultiplier = Mathf.Max(0f, speed2xMultiplier);
            speed4xMultiplier = Mathf.Max(0f, speed4xMultiplier);
            speed6xMultiplier = Mathf.Max(0f, speed6xMultiplier);
            speed10xMultiplier = Mathf.Max(0f, speed10xMultiplier);
            speed25xMultiplier = Mathf.Max(0f, speed25xMultiplier);
            speed50xMultiplier = Mathf.Max(0f, speed50xMultiplier);
            speed100xMultiplier = Mathf.Max(0f, speed100xMultiplier);
            maxShortTicksPerFrame = Mathf.Max(1, maxShortTicksPerFrame);
            maxDayRolloversPerFrame = Mathf.Max(1, maxDayRolloversPerFrame);
        }

        private void OnValidate()
        {
            Sanitize();
        }
    }
}
