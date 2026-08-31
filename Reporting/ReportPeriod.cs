using System;
using LandLedgers.Time;

namespace LandLedgers.Reporting
{
    public enum ReportPeriodKind
    {
        Weekly = 0,
        Monthly = 1
    }

    [Serializable]
    public struct ReportPeriod : IEquatable<ReportPeriod>
    {
        public ReportPeriodKind Kind;
        public int Year;
        public int Month;
        public int Week;
        public SimulationDate StartDate;
        public SimulationDate EndDate;
        public string Label;
        public bool IsCompletePeriod;

        public static ReportPeriod Weekly(SimulationDate startDate, SimulationDate endDate, bool isCompletePeriod = true)
        {
            return new ReportPeriod
            {
                Kind = ReportPeriodKind.Weekly,
                Year = startDate.Year,
                Month = startDate.Month,
                Week = startDate.Week,
                StartDate = startDate,
                EndDate = endDate,
                Label = $"Week {startDate.Week}, {startDate.Year}",
                IsCompletePeriod = isCompletePeriod
            };
        }

        public static ReportPeriod Monthly(SimulationDate startDate, SimulationDate endDate, bool isCompletePeriod = true)
        {
            return new ReportPeriod
            {
                Kind = ReportPeriodKind.Monthly,
                Year = startDate.Year,
                Month = startDate.Month,
                Week = 0,
                StartDate = startDate,
                EndDate = endDate,
                Label = $"Month {startDate.Month}, {startDate.Year}",
                IsCompletePeriod = isCompletePeriod
            };
        }

        public bool Equals(ReportPeriod other)
        {
            return Kind == other.Kind
                && Year == other.Year
                && Month == other.Month
                && Week == other.Week
                && StartDate.Equals(other.StartDate)
                && EndDate.Equals(other.EndDate)
                && string.Equals(Label ?? string.Empty, other.Label ?? string.Empty, StringComparison.Ordinal)
                && IsCompletePeriod == other.IsCompletePeriod;
        }

        public override bool Equals(object obj)
        {
            return obj is ReportPeriod other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Kind.GetHashCode();
                hash = hash * 31 + Year;
                hash = hash * 31 + Month;
                hash = hash * 31 + Week;
                hash = hash * 31 + StartDate.GetHashCode();
                hash = hash * 31 + EndDate.GetHashCode();
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Label ?? string.Empty);
                hash = hash * 31 + IsCompletePeriod.GetHashCode();
                return hash;
            }
        }

        public string GetDisplayLabel()
        {
            if (!string.IsNullOrWhiteSpace(Label))
            {
                return Label.Trim();
            }

            return Kind == ReportPeriodKind.Monthly
                ? $"Month {Month}, {Year}"
                : $"Week {Week}, {Year}";
        }

        public string GetStableKey()
        {
            string kind = Kind.ToString().ToLowerInvariant();
            string label = string.IsNullOrWhiteSpace(Label) ? "none" : Label.Trim();
            return $"{kind}_{Year}_{Month}_{Week}_{StartDate.GetHashCode()}_{EndDate.GetHashCode()}_{(IsCompletePeriod ? "complete" : "partial")}_{label}";
        }

        public override string ToString()
        {
            return GetDisplayLabel();
        }
    }
}
