using System;

namespace LandLedgers.Orchestration.Scenarios.Generation
{
    /// <summary>
    /// Pass V P1-1 (Worker 1): Gregorian date helpers for scenario configuration.
    /// The scenario start date is authored as Y/M/D; validation needs real month
    /// lengths and leap years (1880 and 1904 leap, 1900 not). This is pure
    /// proleptic-Gregorian math for configuration only — the simulation clock
    /// authority (TimeManager, Worker 2 P0-1) owns runtime calendar behavior.
    /// </summary>
    public static class ScenarioDateUtil
    {
        /// <summary>Scenario epoch: 1870-01-01. Day indexes derived from authored
        /// start dates are stable integers used for generation provenance.</summary>
        public const int EpochYear = 1870;

        public static bool IsValidDate(int year, int month, int day)
        {
            if (year < 1870 || year > 1905)
            {
                return false;
            }

            if (month < 1 || month > 12)
            {
                return false;
            }

            return day >= 1 && day <= DaysInMonth(year, month);
        }

        public static int DaysInMonth(int year, int month)
        {
            switch (month)
            {
                case 2: return IsLeapYear(year) ? 29 : 28;
                case 4:
                case 6:
                case 9:
                case 11: return 30;
                default: return 31;
            }
        }

        public static bool IsLeapYear(int year)
        {
            return year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
        }

        /// <summary>
        /// Days since the scenario epoch (1870-01-01 = 0), proleptic Gregorian.
        /// Deterministic and platform-stable for identical inputs.
        /// </summary>
        public static int ToDayIndex(int year, int month, int day)
        {
            if (!IsValidDate(year, month, day))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(year), $"Invalid scenario date {year}-{month:00}-{day:00}.");
            }

            int days = 0;
            for (int y = EpochYear; y < year; y++)
            {
                days += IsLeapYear(y) ? 366 : 365;
            }

            for (int m = 1; m < month; m++)
            {
                days += DaysInMonth(year, m);
            }

            return days + (day - 1);
        }

        public static string ToIsoDate(int year, int month, int day)
        {
            return $"{year:0000}-{month:00}-{day:00}";
        }
    }
}
