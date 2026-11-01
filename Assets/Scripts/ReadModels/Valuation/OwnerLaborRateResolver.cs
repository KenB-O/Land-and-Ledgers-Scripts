using System.Collections.Generic;
using LandLedgers.Economy;

namespace LandLedgers.ReadModels.Valuation
{
    /// <summary>
    /// NX-1B drive-by: owner-labor replacement-cost rate. The valuation's
    /// SetOwnerLabor took a caller-supplied rate that defaulted to 0 in
    /// practice — valuing the owner's hours at nothing. This resolver never
    /// invents silently; it returns a source alongside every rate.
    ///
    /// Resolution order (honest precedence):
    /// 1. Local market evidence: median weekly wage of the business's own
    ///    active employees, converted to an hourly equivalent. A competent
    ///    replacement costs what the business already pays hired hands.
    /// 2. Documented calibration fallback: 10¢/hour. Historical basis —
    ///    1870s US farm laborers earned roughly $0.75–$1.00 for a 10-hour
    ///    day (NCPedia cost-of-living series; 19thcentury.us agricultural
    ///    worker survey). Marked CALIBRATION, never presented as measured.
    /// </summary>
    public static class OwnerLaborRateResolver
    {
        /// <summary>Hours per week assumed when converting weekly wages (calibration).</summary>
        public const float HoursPerWeekAssumption = 60f;

        /// <summary>
        /// Calibration fallback: 10 cents/hour (historical basis documented
        /// above). Used only when no local wage evidence exists.
        /// </summary>
        public const int CalibrationFallbackCentsPerHour = 10;

        public enum RateSource
        {
            /// <summary>Derived from the business's own active payroll.</summary>
            LocalPayroll,
            /// <summary>Documented historical calibration (no local evidence).</summary>
            CalibrationFallback,
        }

        public struct ResolvedRate
        {
            public int CentsPerHour;
            public RateSource Source;
            public string BasisNote;
        }

        /// <summary>
        /// Resolves the owner's replacement hourly rate. Returns the rate plus
        /// its source and a human-readable basis note for the valuation record.
        /// </summary>
        public static ResolvedRate Resolve(
            EmploymentRelationshipRegistry employments,
            string businessId)
        {
            var rate = new ResolvedRate
            {
                CentsPerHour = CalibrationFallbackCentsPerHour,
                Source = RateSource.CalibrationFallback,
                BasisNote = $"CALIBRATION: no active payroll on record for {businessId}; " +
                    $"using {CalibrationFallbackCentsPerHour}¢/hour (1870s farm-labor basis, ~$1/day ÷ 10h). " +
                    "Replace with measured local wages when available.",
            };

            if (employments == null || string.IsNullOrEmpty(businessId))
                return rate;

            List<EmploymentRelationship> active = employments.GetActiveByEmployer(businessId);
            if (active == null || active.Count == 0)
                return rate;

            var weeklyWages = new List<int>();
            foreach (EmploymentRelationship rel in active)
            {
                if (rel != null && rel.Compensation.AgreedWeeklyWageCents > 0)
                    weeklyWages.Add(rel.Compensation.AgreedWeeklyWageCents);
            }
            if (weeklyWages.Count == 0)
                return rate;

            weeklyWages.Sort();
            int medianWeekly = weeklyWages[weeklyWages.Count / 2];
            int hourly = (int)System.Math.Round(medianWeekly / HoursPerWeekAssumption);
            rate.CentsPerHour = System.Math.Max(1, hourly);
            rate.Source = RateSource.LocalPayroll;
            rate.BasisNote =
                $"Local payroll: median active weekly wage {medianWeekly}¢ ÷ {HoursPerWeekAssumption}h " +
                $"across {weeklyWages.Count} employee(s) at {businessId}.";
            return rate;
        }
    }
}
