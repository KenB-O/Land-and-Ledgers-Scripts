using System;

namespace LandLedgers.Time
{
    /// <summary>
    /// Pass V P0-1 contract: pure integer Gregorian calendar math for the simulation
    /// date authority. Proleptic Gregorian; no dependency on device locale, timezone,
    /// or wall clock. Day numbers use Howard Hinnant's days_from_civil epoch
    /// (day 0 = 0000-03-01), so all conversions are O(1) and exact.
    /// Weekday index: 0 = Monday .. 6 = Sunday (calibrated: 1970-01-01 was Thursday,
    /// and days_from_civil(1970,1,1) mod 7 == 3).
    /// </summary>
    public static class GregorianDateMath
    {
        public static bool IsLeapYear(int year)
        {
            return year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
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

        public static int DaysInYear(int year)
        {
            return IsLeapYear(year) ? 366 : 365;
        }

        /// <summary>Days from civil 0000-03-01 to the given Gregorian date.</summary>
        public static int DaysFromCivil(int year, int month, int day)
        {
            int y = month <= 2 ? year - 1 : year;
            int era = (y >= 0 ? y : y - 399) / 400;
            int yoe = y - era * 400;
            int mp = (month + 9) % 12;
            int doy = (153 * mp + 2) / 5 + day - 1;
            int doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
            return era * 146097 + doe - 719468;
        }

        /// <summary>Gregorian date from a day number (inverse of DaysFromCivil).</summary>
        public static void CivilFromDays(int dayNumber, out int year, out int month, out int day)
        {
            int z = dayNumber + 719468;
            int era = (z >= 0 ? z : z - 146096) / 146097;
            int doe = z - era * 146097;
            int yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
            int y = yoe + era * 400;
            int doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
            int mp = (5 * doy + 2) / 153;
            day = doy - (153 * mp + 2) / 5 + 1;
            month = mp < 10 ? mp + 3 : mp - 9;
            year = month <= 2 ? y + 1 : y;
        }

        /// <summary>
        /// Monday-zero weekday index for a Gregorian date.
        /// DaysFromCivil counts from 1970-01-01, which was a Thursday
        /// (Monday-zero index 3); the +3 offset aligns the epoch to Monday.
        /// </summary>
        public static int DayOfWeekMondayZero(int year, int month, int day)
        {
            int n = (DaysFromCivil(year, month, day) + 3) % 7;
            return n < 0 ? n + 7 : n;
        }

        /// <summary>
        /// Completed calendar years between a birth date and a current date.
        /// A person born 1860-03-15 is 19 on 1880-03-14 and 20 on 1880-03-15.
        /// Uses the project-wide leap-day convention: a Feb 29 birth observes
        /// Feb 28 in non-leap years, so the anniversary comparison always uses
        /// <see cref="AnniversaryDay"/> — one convention, used everywhere.
        /// </summary>
        public static int CompletedYears(
            int birthYear, int birthMonth, int birthDay,
            int currentYear, int currentMonth, int currentDay)
        {
            int years = currentYear - birthYear;
            int anniversaryDay = AnniversaryDay(birthMonth, birthDay, currentYear);
            if (currentMonth < birthMonth || (currentMonth == birthMonth && currentDay < anniversaryDay))
            {
                years--;
            }
            return Math.Max(0, years);
        }

        /// <summary>
        /// Absolute simulation day index for a Gregorian date, given the epoch anchor
        /// (the Gregorian date of absoluteDayIndex 0).
        /// </summary>
        public static int ToAbsoluteDay(
            int year, int month, int day,
            int anchorYear, int anchorMonth, int anchorDay)
        {
            return DaysFromCivil(year, month, day) - DaysFromCivil(anchorYear, anchorMonth, anchorDay);
        }

        /// <summary>
        /// Gregorian date for an absolute simulation day index, given the epoch anchor.
        /// </summary>
        public static void FromAbsoluteDay(
            int absoluteDay,
            int anchorYear, int anchorMonth, int anchorDay,
            out int year, out int month, out int day)
        {
            CivilFromDays(DaysFromCivil(anchorYear, anchorMonth, anchorDay) + absoluteDay,
                out year, out month, out day);
        }

        /// <summary>
        /// Anniversary day-of-month for a birth date in a given year, applying the
        /// project-wide leap-day convention: Feb 29 births observe Feb 28 in
        /// non-leap years. One convention, used everywhere.
        /// </summary>
        public static int AnniversaryDay(int birthMonth, int birthDay, int year)
        {
            if (birthMonth == 2 && birthDay == 29 && !IsLeapYear(year))
            {
                return 28;
            }
            return birthDay;
        }
    }
}
