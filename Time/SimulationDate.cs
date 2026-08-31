using System;
using UnityEngine;

namespace LandLedgers.Time
{
    [Serializable]
    public struct SimulationDate : IEquatable<SimulationDate>
    {
        [SerializeField]
        private int absoluteDayIndex;

        [SerializeField]
        private int dayOfWeekIndex;

        [SerializeField]
        private int dayOfMonth;

        [SerializeField]
        private int week;

        [SerializeField]
        private int weekOfMonth;

        [SerializeField]
        private int month;

        [SerializeField]
        private int year;

        public int AbsoluteDayIndex => absoluteDayIndex;
        public int DayOfWeekIndex => dayOfWeekIndex;
        public int DayOfMonth => dayOfMonth;
        public int Week => week;
        public int WeekOfMonth => weekOfMonth;
        public int Month => month;
        public int Year => year;

        public SimulationDate(
            int absoluteDayIndex,
            int dayOfWeekIndex,
            int dayOfMonth,
            int week,
            int weekOfMonth,
            int month,
            int year)
        {
            this.absoluteDayIndex = absoluteDayIndex;
            this.dayOfWeekIndex = dayOfWeekIndex;
            this.dayOfMonth = dayOfMonth;
            this.week = week;
            this.weekOfMonth = weekOfMonth;
            this.month = month;
            this.year = year;
        }

        public bool Equals(SimulationDate other)
        {
            return absoluteDayIndex == other.absoluteDayIndex
                && dayOfWeekIndex == other.dayOfWeekIndex
                && dayOfMonth == other.dayOfMonth
                && week == other.week
                && weekOfMonth == other.weekOfMonth
                && month == other.month
                && year == other.year;
        }

        public override bool Equals(object obj)
        {
            return obj is SimulationDate other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + absoluteDayIndex;
                hash = hash * 31 + dayOfWeekIndex;
                hash = hash * 31 + dayOfMonth;
                hash = hash * 31 + week;
                hash = hash * 31 + weekOfMonth;
                hash = hash * 31 + month;
                hash = hash * 31 + year;
                return hash;
            }
        }

        public override string ToString()
        {
            return $"Year {year}, Month {month}, Week {week}, Day {dayOfMonth}";
        }
    }
}
