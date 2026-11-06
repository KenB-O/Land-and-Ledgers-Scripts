using System;
using LandLedgers.Economy;

namespace LandLedgers.Economy.Core
{
    /// <summary>
    /// Plan 04 (2026-11-06) slice M3 (Obj 43, D-S3/D-S4): the first pure
    /// piece of the weekly converter, extracted ahead of the M6
    /// extraction — the task-backed production seam for Bakery/FuelDealer
    /// plus the cross-check guard. SBRM's converter delegates to it;
    /// the converter tests run against it. M6 grows this file into the
    /// full converter extraction (ratio table, goods path, service path)
    /// and moves the M0 characterization tests onto it.
    ///
    /// SEAM RULE: Bakery/FuelDealer production is owned by task-backed
    /// drivers (BakeryWholesaleDailyDriver / Series2DealerBakeryFuelSource),
    /// NOT by the weekly category converter. The converter credits 0
    /// output for them, records the standing reason on the business's
    /// weekly operation record, and appends a seam summary naming the
    /// owning driver and the driver-side stock/cash it can read read-only
    /// from the shared RuntimeState. It never reads or writes the
    /// driver's bin: the forwarder's bin values (flour lb in the bin,
    /// fuel units at the yard) are not reachable from SBRM without a new
    /// authority reference — that read path is owner/Codex work.
    /// GUARD RULE: if the converter ever credits output to a
    /// Bakery/FuelDealer category pool, refuse loudly (a refusal string,
    /// never silent). The seam early return is the first line of defence;
    /// this guard, checked at the credit site, is the second.
    /// </summary>
    public static class SharedBusinessWeeklyConverter
    {
        public const string TaskBackedExclusionReason =
            "Task-backed production driver is not connected; weekly category conversion is disabled";

        public static bool IsTaskBackedProductionType(BusinessType businessType) =>
            businessType == BusinessType.Bakery || businessType == BusinessType.FuelDealer;

        public static string TaskBackedDriverName(BusinessType businessType)
        {
            switch (businessType)
            {
                case BusinessType.Bakery:
                    return "BakeryWholesaleDailyDriver";
                case BusinessType.FuelDealer:
                    return "Series2DealerBakeryFuelSource";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Records the 0-output exclusion on the business's weekly
        /// operation record (0 consumed, 0 produced, standing reason)
        /// and returns the reason for the caller's operation summary.
        /// Stock and cash are untouched. A null runtime records nothing
        /// but still returns the reason.
        /// </summary>
        public static string RecordTaskBackedExclusion(BusinessRuntimeState runtime)
        {
            runtime?.RecordWeeklyOperation(0, 0, "No production completed", TaskBackedExclusionReason);
            return TaskBackedExclusionReason;
        }

        /// <summary>
        /// The seam summary line: names the owning driver and the
        /// driver-side stock/cash readable read-only from the shared
        /// RuntimeState (cash + the business's own profile input/output
        /// category pools). States the 0-output exclusion.
        /// </summary>
        public static string BuildTaskBackedSeamSummary(
            BusinessType businessType,
            int cashCents,
            string inputCategoryId,
            int inputStockUnits,
            string outputCategoryId,
            int outputStockUnits)
        {
            string driver = TaskBackedDriverName(businessType) ?? "unknown task-backed driver";
            string input = string.IsNullOrWhiteSpace(inputCategoryId)
                ? "no input category"
                : $"{inputCategoryId} stock {Math.Max(0, inputStockUnits)}";
            string output = string.IsNullOrWhiteSpace(outputCategoryId)
                ? "no output category"
                : $"{outputCategoryId} stock {Math.Max(0, outputStockUnits)}";
            return $"{businessType} task-backed production: driver {driver}; cash {cashCents}c; {input}; {output} (read-only — production owned by the driver; weekly converter credits 0 output)";
        }

        /// <summary>
        /// Cross-check guard, checked at the converter's output-credit
        /// site: returns a refusal string when the converter would credit
        /// output to a Bakery/FuelDealer category pool, else null. The
        /// caller must NOT credit and must record the refusal loudly
        /// (blocked reason + summary) when non-null.
        /// </summary>
        public static string CheckTaskBackedOutputCredit(BusinessType businessType, string outputCategoryId, int wouldCreditUnits)
        {
            if (!IsTaskBackedProductionType(businessType))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(outputCategoryId) || wouldCreditUnits <= 0)
            {
                return null;
            }

            return $"converter refused to credit {wouldCreditUnits} {outputCategoryId} to {businessType}: output is owned by {TaskBackedDriverName(businessType)} (task-backed production)";
        }
    }
}
