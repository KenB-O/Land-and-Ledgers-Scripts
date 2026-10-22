using System;
using UnityEngine;

namespace LandLedgers.Time
{
    /// <summary>
    /// Pass V P0-1: the explicit Gregorian anchor for the simulation calendar.
    /// absoluteDayIndex 0 maps to the anchor date (default 1880-03-01, verified a
    /// Monday). All calendar derivation (Y/M/D, weekday, anniversaries) flows
    /// through <see cref="GregorianDateMath"/> from this anchor; there is no
    /// 336-day fiction anywhere in the derivation path.
    ///
    /// The anchor is the calendar EPOCH, not the scenario start. Scenarios begin
    /// later (see SimulationTimeSettings startingYear/startingMonth/
    /// startingDayOfMonth); pre-start history (birth dates back to 1700) converts
    /// through the same anchor, including negative day offsets.
    /// </summary>
    [Serializable]
    public struct CalendarAnchor : IEquatable<CalendarAnchor>
    {
        public const int DefaultAnchorYear = 1880;
        public const int DefaultAnchorMonth = 3;
        public const int DefaultAnchorDay = 1;

        [SerializeField]
        private int year;

        [SerializeField]
        private int month;

        [SerializeField]
        private int day;

        public int Year => year;
        public int Month => month;
        public int Day => day;

        public CalendarAnchor(int year, int month, int day)
        {
            this.year = Mathf.Max(1, year);
            this.month = Mathf.Clamp(month, 1, 12);
            this.day = Mathf.Clamp(day, 1, GregorianDateMath.DaysInMonth(this.year, this.month));
        }

        public static CalendarAnchor Default => new CalendarAnchor(DefaultAnchorYear, DefaultAnchorMonth, DefaultAnchorDay);

        public bool IsValid => year >= 1 && month >= 1 && month <= 12 && day >= 1 && day <= GregorianDateMath.DaysInMonth(year, month);

        /// <summary>Absolute simulation day index for a Gregorian date.</summary>
        public int ToAbsoluteDay(int targetYear, int targetMonth, int targetDay)
        {
            return GregorianDateMath.ToAbsoluteDay(targetYear, targetMonth, targetDay, year, month, day);
        }

        /// <summary>
        /// Pass IX WS6 (A1-02): this anchor date's day index in the canonical
        /// anchor frame (Jan 1 of <see cref="DefaultAnchorYear"/> = 0) — the
        /// frame ScenarioDateUtil.DayIndexForDate reports. For the default
        /// anchor (1880-03-01) this is 60. The conversion between the
        /// TimeManager absolute frame (anchor date = 0) and the anchor frame
        /// is exactly this value: anchorFrameDay = absoluteDay + AnchorDayIndex.
        /// Computed through the same Gregorian math ScenarioDateUtil uses,
        /// with the anchor year taken from this type's own DefaultAnchorYear
        /// (ScenarioDateUtil.AnchorYear in turn derives from it — one
        /// authority, no drift). Core may not reference Orchestration, so the
        /// delegation runs through the shared constant, not a type reference.
        /// </summary>
        public int AnchorDayIndex => GregorianDateMath.ToAbsoluteDay(year, month, day, DefaultAnchorYear, 1, 1);

        /// <summary>Gregorian date for an absolute simulation day index.</summary>
        public void FromAbsoluteDay(int absoluteDay, out int targetYear, out int targetMonth, out int targetDay)
        {
            GregorianDateMath.FromAbsoluteDay(absoluteDay, year, month, day, out targetYear, out targetMonth, out targetDay);
        }

        /// <summary>
        /// True Gregorian <see cref="SimulationDate"/> for an absolute day index.
        /// Week fields are derived approximations on the real calendar: global week
        /// is absoluteDay / 7 + 1, week-of-month is (dayOfMonth - 1) / 7 + 1.
        /// </summary>
        public SimulationDate BuildDate(int absoluteDayIndex)
        {
            int safeDay = Math.Max(0, absoluteDayIndex);
            FromAbsoluteDay(safeDay, out int y, out int m, out int d);
            int dayOfWeekIndex = GregorianDateMath.DayOfWeekMondayZero(y, m, d);
            int week = safeDay / 7 + 1;
            int weekOfMonth = (d - 1) / 7 + 1;
            return new SimulationDate(safeDay, dayOfWeekIndex, d, week, weekOfMonth, m, y);
        }

        public bool Equals(CalendarAnchor other)
        {
            return year == other.year && month == other.month && day == other.day;
        }

        public override bool Equals(object obj)
        {
            return obj is CalendarAnchor other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + year;
                hash = hash * 31 + month;
                hash = hash * 31 + day;
                return hash;
            }
        }

        public override string ToString()
        {
            return $"{year:D4}-{month:D2}-{day:D2}";
        }
    }
}
