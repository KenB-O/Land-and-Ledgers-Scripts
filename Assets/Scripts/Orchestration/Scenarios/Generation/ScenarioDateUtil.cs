using System;
using LandLedgers.Time;

namespace LandLedgers.Orchestration.Scenarios.Generation
{
    /// <summary>
    /// Pass V P1-1 (Worker 1): Gregorian date helpers for scenario configuration.
    /// The scenario start date is authored as Y/M/D; validation needs real month
    /// lengths and leap years (1880 and 1904 leap, 1900 not). This is pure
    /// proleptic-Gregorian math for configuration only — the simulation clock
    /// authority (TimeManager, Worker 2 P0-1) owns runtime calendar behavior.
    ///
    /// Pass IX WS6 (A1-02): this util is also the canonical calendar ANCHOR
    /// frame for scenario dates. Day indexes in the anchor frame count from
    /// Jan 1 of <see cref="AnchorYear"/> (the CalendarAnchor default year):
    /// DayIndexOfJan1(AnchorYear) == 0, and the default anchor date
    /// 1880-03-01 sits at day 60 of that frame. The TimeManager absolute
    /// frame (anchor date = 0) converts by subtracting
    /// CalendarAnchor.AnchorDayIndex. One frame, one conversion — no rival
    /// date authority.
    /// </summary>
    public static class ScenarioDateUtil
    {
        /// <summary>Scenario epoch: 1870-01-01. Day indexes derived from authored
        /// start dates are stable integers used for generation provenance.</summary>
        public const int EpochYear = 1870;

        /// <summary>
        /// Pass IX WS6: the canonical anchor year — the CalendarAnchor
        /// default year (1880). Derived from CalendarAnchor so the two can
        /// never drift apart.
        /// </summary>
        public const int AnchorYear = CalendarAnchor.DefaultAnchorYear;

        /// <summary>
        /// Pass IX WS6: days from the scenario epoch (1870-01-01) to Jan 1
        /// of the anchor year: 10 years including the 1872 and 1876 leap
        /// days = 3652. (A value of 3478 sometimes quoted for this constant
        /// is arithmetically wrong: epoch + 3478 days lands on 1879-07-11,
        /// not on any Jan 1.)
        /// </summary>
        public const int DaysBeforeAnchorYear = 3652;

        /// <summary>Anchor-frame day index of Jan 1 of a year; DayIndexOfJan1(AnchorYear) == 0.</summary>
        public static int DayIndexOfJan1(int year)
        {
            return ToDayIndex(year, 1, 1) - DaysBeforeAnchorYear;
        }

        /// <summary>Anchor-frame day index of a Gregorian date (Jan 1 of AnchorYear = 0).</summary>
        public static int DayIndexForDate(int year, int month, int day)
        {
            return ToDayIndex(year, month, day) - DaysBeforeAnchorYear;
        }

        /// <summary>Alias matching the bootstrap factory's call shape.</summary>
        public static int DayIndexFor(int year, int month, int day)
        {
            return DayIndexForDate(year, month, day);
        }

        /// <summary>Anchor-frame day index of the first of a month.</summary>
        public static int DayIndexForFirstOfMonth(int year, int month)
        {
            return DayIndexForDate(year, month, 1);
        }

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
