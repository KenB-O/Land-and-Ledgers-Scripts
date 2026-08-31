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

        [Min(1)]
        public int weeksPerMonth = 4;

        [Min(1)]
        public int monthsPerYear = 12;

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
        [Min(1)]
        public int startingYear = 1892;

        [Min(1)]
        public int startingMonth = 3;

        [Min(1)]
        public int startingWeekOfMonth = 1;

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

        public int DaysPerMonth => Mathf.Max(1, daysPerWeek) * Mathf.Max(1, weeksPerMonth);
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

        public SimulationDate BuildDateFromAbsoluteDay(int absoluteDayIndex)
        {
            int safeAbsoluteDay = Mathf.Max(0, absoluteDayIndex);
            int safeDaysPerWeek = Mathf.Max(1, daysPerWeek);
            int safeWeeksPerMonth = Mathf.Max(1, weeksPerMonth);
            int daysPerMonthSafe = Mathf.Max(1, safeDaysPerWeek * safeWeeksPerMonth);
            int monthsPerYearSafe = Mathf.Max(1, monthsPerYear);
            int startDayOfWeekZeroBased = Mathf.Clamp(startingDayOfWeek - 1, 0, safeDaysPerWeek - 1);
            int startDayOfMonthZeroBased =
                (Mathf.Clamp(startingWeekOfMonth, 1, safeWeeksPerMonth) - 1) * safeDaysPerWeek
                + startDayOfWeekZeroBased;

            int totalDayOfMonthZeroBased = startDayOfMonthZeroBased + safeAbsoluteDay;
            int monthOffset = totalDayOfMonthZeroBased / daysPerMonthSafe;
            int dayOfMonthZeroBased = totalDayOfMonthZeroBased % daysPerMonthSafe;
            int totalMonthZeroBased = Mathf.Max(0, startingMonth - 1) + monthOffset;
            int year = Mathf.Max(1, startingYear) + totalMonthZeroBased / monthsPerYearSafe;
            int month = totalMonthZeroBased % monthsPerYearSafe + 1;
            int dayOfWeekIndex = dayOfMonthZeroBased % safeDaysPerWeek;
            int weekOfMonth = dayOfMonthZeroBased / safeDaysPerWeek + 1;
            int globalWeek = (startDayOfMonthZeroBased + safeAbsoluteDay) / safeDaysPerWeek + 1;

            return new SimulationDate(
                safeAbsoluteDay,
                dayOfWeekIndex,
                dayOfMonthZeroBased + 1,
                globalWeek,
                weekOfMonth,
                month,
                year);
        }

        public void Sanitize()
        {
            daysPerWeek = Mathf.Max(1, daysPerWeek);
            weeksPerMonth = Mathf.Max(1, weeksPerMonth);
            monthsPerYear = Mathf.Max(1, monthsPerYear);
            startingYear = Mathf.Max(1, startingYear);
            startingMonth = Mathf.Clamp(startingMonth, 1, monthsPerYear);
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
