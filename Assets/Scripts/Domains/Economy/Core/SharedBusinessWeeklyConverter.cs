using System;
using System.Collections.Generic;
using LandLedgers.Economy;

namespace LandLedgers.Economy.Core
{
    /// <summary>
    /// Plan 04 (2026-11-06) slice M3 (Obj 43, D-S3/D-S4): the first pure
    /// piece of the weekly converter, extracted ahead of the M6
    /// extraction — the task-backed production seam for Bakery/FuelDealer
    /// plus the cross-check guard. SBRM's converter delegates to it;
    /// the converter tests run against it.
    ///
    /// Slice M6 (Obj 43): the full converter extraction — the goods-path
    /// conversion math moved verbatim from ResolveBusinessOperation
    /// (planned units, input-limited output, secondary inputs, storage
    /// shortfall) operating on explicit inputs (business type, runtime,
    /// planned units, input/output category ids, input category list,
    /// blocked-reasons list) and returning an explicit result struct.
    /// Values unchanged: the ratio table moves as data, not as new
    /// numbers (the 7 SBRM consts + Tailor/Wheelwright inline 2 +
    /// default 1). SBRM keeps the service path, the Bakery/FuelDealer
    /// seam, the procurement call (it owns the demand signal + budget),
    /// and the record/summary. SharedBusinessWeeklyProcurement stays
    /// as the converter's procurement delegate (M6 decision: procurement
    /// is a separate responsibility from conversion, not conversion
    /// math — folding it in would merge two helpers for no benefit).
    ///
    /// MATH NOTES: the helper uses System.Math, not Mathf — the harness
    /// shim implements Mathf.RoundToInt/CeilToInt as (int)Math.Round /
    /// (int)Math.Ceiling, and Unity's real Mathf does the same, so the
    /// rounding is identical in both compilers.
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
        /// <summary>
        /// The converter ratio table, moved verbatim from SBRM as data
        /// (the 7 private consts + Tailor/Wheelwright inline 2 + default
        /// 1). Current values, not endorsed. SBRM's GetOutputUnitsPerInput
        /// and the 7 consts were deleted in M6; this is the single source
        /// of truth.
        /// </summary>
        public static int GetOutputUnitsPerInput(BusinessType businessType)
        {
            return businessType switch
            {
                BusinessType.CropFarm => 8,
                BusinessType.Ranch => 2,
                BusinessType.Butcher => 8,
                BusinessType.Blacksmith => 4,
                BusinessType.FuelDealer => 5,
                BusinessType.GrainMill => 4,
                BusinessType.Bakery => 3,
                BusinessType.Tailor => 2,
                BusinessType.Wheelwright => 2,
                _ => 1
            };
        }

        /// <summary>
        /// Planned weekly output units: round(throughput x efficiency),
        /// throughput clamped to 0. Moved verbatim from
        /// ResolveBusinessOperation (Mathf.RoundToInt → System.Math,
        /// identical rounding — see the class doc).
        /// </summary>
        public static int ComputePlannedUnits(int baselineWeeklyThroughputUnits, float operatingEfficiency01) =>
            (int)Math.Round(Math.Max(0, baselineWeeklyThroughputUnits) * operatingEfficiency01);

        /// <summary>
        /// Input-limited output: available inputs x output-per-input.
        /// </summary>
        public static int ComputeInputLimitedOutput(int availableInputs, int outputUnitsPerInput) =>
            availableInputs * outputUnitsPerInput;

        /// <summary>
        /// Input units needed for a given operable output: rounds UP, so
        /// a partial input unit still buys the output. The divisor is
        /// clamped to 1 (the ratio table never yields 0; the M0 pin
        /// guarded the same way).
        /// </summary>
        public static int ComputeInputUnitsNeeded(int operableUnits, int outputUnitsPerInput) =>
            (int)Math.Ceiling(operableUnits / (float)Math.Max(1, outputUnitsPerInput));

        /// <summary>
        /// The no-mint guard reason (Review 2 Pass C): the weekly
        /// converter must never mint output from nothing. Single source
        /// of truth — SBRM's guard reads this const via
        /// CheckNoMintGuard, and ConvertGoods refuses standalone too.
        /// </summary>
        public const string NoInputNoMintReason =
            "no input category: weekly conversion cannot mint output from nothing";

        /// <summary>
        /// No-mint guard, checked at the converter's entry: returns the
        /// refusal when an empty input category meets a non-empty output
        /// category (output would be credited with no input consumed and
        /// no cash spent), else null. The caller must NOT convert and
        /// must record the refusal when non-null.
        /// </summary>
        public static string CheckNoMintGuard(string inputCategoryId, string outputCategoryId)
        {
            return string.IsNullOrWhiteSpace(inputCategoryId) && !string.IsNullOrWhiteSpace(outputCategoryId)
                ? NoInputNoMintReason
                : null;
        }

        /// <summary>
        /// The explicit result of one weekly goods conversion. The
        /// caller (SBRM) applies the record/summary; the helper only
        /// moves stock and reports what happened.
        /// </summary>
        public struct GoodsConversionResult
        {
            public int PlannedUnits { get; }
            public int ConsumedUnits { get; }
            public int ProducedUnits { get; }

            /// <summary>Non-null when the M3 cross-check refused an
            /// output credit to a Bakery/FuelDealer pool.</summary>
            public string TaskBackedOutputRefusal { get; }

            /// <summary>Non-null when the no-mint guard refused the
            /// conversion (empty input, non-empty output). Unreachable in
            /// production while SBRM's guard stands; the helper refuses
            /// standalone so a direct caller can never mint.</summary>
            public string NoMintRefusal { get; }

            public GoodsConversionResult(
                int plannedUnits,
                int consumedUnits,
                int producedUnits,
                string taskBackedOutputRefusal,
                string noMintRefusal)
            {
                PlannedUnits = plannedUnits;
                ConsumedUnits = consumedUnits;
                ProducedUnits = producedUnits;
                TaskBackedOutputRefusal = taskBackedOutputRefusal;
                NoMintRefusal = noMintRefusal;
            }
        }

        /// <summary>
        /// The weekly goods conversion, moved verbatim from SBRM's
        /// ResolveBusinessOperation goods path. Callers must run the
        /// procurement step BEFORE this (it feeds the stock this reads)
        /// and the no-mint guard before that (SBRM does both). The
        /// helper reads/writes only the given runtime's category pools
        /// and the blocked-reasons list: it never touches cash (the
        /// procurement step owns spend), never credits a task-backed
        /// pool (M3 guard), and never mints from an empty input (the
        /// no-mint guard, checked here too as a second line of defence).
        /// Blocked reasons are appended in the original order: no
        /// staffed capacity, output storage full, input shortage,
        /// task-backed refusal, output storage shortfall.
        /// </summary>
        public static GoodsConversionResult ConvertGoods(
            BusinessType businessType,
            BusinessRuntimeState runtime,
            int plannedUnits,
            string inputCategoryId,
            string outputCategoryId,
            ReadOnlySpan<string> inputCategoryIds,
            List<string> blockedReasons)
        {
            string noMintRefusal = CheckNoMintGuard(inputCategoryId, outputCategoryId);
            if (noMintRefusal != null)
            {
                AddReason(blockedReasons, noMintRefusal);
                return new GoodsConversionResult(plannedUnits, 0, 0, null, noMintRefusal);
            }

            if (runtime == null)
            {
                return new GoodsConversionResult(plannedUnits, 0, 0, null, null);
            }

            int outputUnitsPerInput = GetOutputUnitsPerInput(businessType);
            int outputRoom = GetAvailableCategoryCapacity(runtime, outputCategoryId);
            int availableInputs = string.IsNullOrWhiteSpace(inputCategoryId)
                ? plannedUnits
                : GetCategoryStockUnits(runtime, inputCategoryId);
            int inputLimitedOutput = string.IsNullOrWhiteSpace(inputCategoryId)
                ? plannedUnits
                : ComputeInputLimitedOutput(availableInputs, outputUnitsPerInput);

            if (plannedUnits <= 0)
            {
                AddReason(blockedReasons, "no staffed weekly capacity");
            }

            if (!string.IsNullOrWhiteSpace(outputCategoryId) && outputRoom <= 0)
            {
                AddReason(blockedReasons, $"output storage full: {outputCategoryId}");
            }

            if (!string.IsNullOrWhiteSpace(inputCategoryId) && availableInputs <= 0)
            {
                AddReason(blockedReasons, $"input shortage: {inputCategoryId}");
            }

            // M3 cross-check (Plan 04, D-S3/D-S4): if the converter ever
            // reaches the credit site for a Bakery/FuelDealer pool, refuse
            // loudly instead of crediting. Unreachable while the seam
            // early return above stands; the guard is the second line of
            // defence, checked BEFORE any input is consumed or output
            // credited so a trip never destroys inputs.
            string taskBackedOutputRefusal = CheckTaskBackedOutputCredit(businessType, outputCategoryId, plannedUnits);
            if (taskBackedOutputRefusal != null)
            {
                AddReason(blockedReasons, taskBackedOutputRefusal);
            }

            int consumedUnits = 0;
            int producedUnits = 0;
            if (taskBackedOutputRefusal == null && plannedUnits > 0 && !string.IsNullOrWhiteSpace(outputCategoryId) && outputRoom > 0)
            {
                int operableUnits = Math.Min(plannedUnits, outputRoom);
                if (!string.IsNullOrWhiteSpace(inputCategoryId))
                {
                    operableUnits = Math.Min(operableUnits, inputLimitedOutput);
                    int inputUnitsNeeded = ComputeInputUnitsNeeded(operableUnits, outputUnitsPerInput);
                    if (operableUnits > 0 && runtime.TryConsumeCategoryStockUnits(inputCategoryId, inputUnitsNeeded, out int consumed))
                    {
                        consumedUnits = consumed;
                        operableUnits = Math.Min(operableUnits, consumed * outputUnitsPerInput);
                    }
                    else
                    {
                        operableUnits = 0;
                    }
                }

                operableUnits = ConsumeSecondaryInputs(businessType, runtime, inputCategoryIds, inputCategoryId, operableUnits, blockedReasons);
                producedUnits = runtime.AddCategoryStockUnits(outputCategoryId, operableUnits);
                if (operableUnits > 0 && producedUnits < operableUnits)
                {
                    // PASS7-007: storage accepted less than planned — record the
                    // shortfall instead of over-counting production. Unreachable
                    // through this path today (operableUnits is already capped
                    // by outputRoom, so the credit always fits); kept as the
                    // second line of defence if the cap ever changes.
                    AddReason(blockedReasons, $"output storage shortfall: stored {producedUnits}/{operableUnits} {outputCategoryId}");
                }
            }

            return new GoodsConversionResult(plannedUnits, consumedUnits, producedUnits, taskBackedOutputRefusal, null);
        }

        /// <summary>
        /// Secondary inputs, moved verbatim from SBRM's
        /// ConsumeSecondaryInputsForOutput: every profile input category
        /// other than the primary is consumed at its own output-per-input
        /// rate, limiting (or zeroing) the operable output. A secondary
        /// category with no stock pool is skipped, exactly as before.
        /// </summary>
        public static int ConsumeSecondaryInputs(
            BusinessType businessType,
            BusinessRuntimeState runtime,
            ReadOnlySpan<string> inputCategoryIds,
            string primaryInputCategoryId,
            int outputUnits,
            List<string> blockedReasons)
        {
            if (runtime == null || outputUnits <= 0)
            {
                return Math.Max(0, outputUnits);
            }

            int limitedOutputUnits = outputUnits;
            for (int i = 0; i < inputCategoryIds.Length; i++)
            {
                string categoryId = inputCategoryIds[i];
                if (string.IsNullOrWhiteSpace(categoryId)
                    || string.Equals(categoryId, primaryInputCategoryId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                CategoryStockState stock = runtime.GetCategoryStock(categoryId);
                if (stock == null)
                {
                    continue;
                }

                int outputPerInput = GetSecondaryOutputUnitsPerInputUnit(businessType, categoryId);
                int desiredInputUnits = (int)Math.Ceiling(limitedOutputUnits / (float)outputPerInput);
                if (desiredInputUnits <= 0)
                {
                    continue;
                }

                int supportedOutput = stock.CurrentStockUnits * outputPerInput;
                if (supportedOutput <= 0)
                {
                    AddBlockedReason(blockedReasons, $"input shortage: {categoryId}");
                    limitedOutputUnits = 0;
                    continue;
                }

                if (supportedOutput < limitedOutputUnits)
                {
                    AddBlockedReason(blockedReasons, $"short input: {categoryId}");
                    limitedOutputUnits = supportedOutput;
                }

                int consumeUnits = (int)Math.Ceiling(limitedOutputUnits / (float)outputPerInput);
                if (consumeUnits > 0)
                {
                    runtime.TryConsumeCategoryStockUnits(categoryId, consumeUnits, out _);
                }
            }

            return Math.Max(0, limitedOutputUnits);
        }

        /// <summary>
        /// Secondary-input rate, moved verbatim from SBRM: Bakery fuel
        /// wood converts at 8 output per input unit; everything else at
        /// 4. The category id mirrors SBRM's CategoryFuelWood const
        /// ("fuel_wood"); that const stays in SBRM (used elsewhere).
        /// </summary>
        private static int GetSecondaryOutputUnitsPerInputUnit(BusinessType businessType, string categoryId)
        {
            if (businessType == BusinessType.Bakery && string.Equals(categoryId, "fuel_wood", StringComparison.OrdinalIgnoreCase))
            {
                return 8;
            }

            return 4;
        }

        private static int GetAvailableCategoryCapacity(BusinessRuntimeState runtime, string categoryId)
        {
            CategoryStockState stock = runtime != null ? runtime.GetCategoryStock(categoryId) : null;
            if (stock == null)
            {
                return 0;
            }

            return stock.TargetStockUnits > 0
                ? Math.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits)
                : int.MaxValue;
        }

        private static int GetCategoryStockUnits(BusinessRuntimeState runtime, string categoryId)
        {
            CategoryStockState stock = runtime != null ? runtime.GetCategoryStock(categoryId) : null;
            return stock != null ? stock.CurrentStockUnits : 0;
        }

        /// <summary>
        /// Appends a reason, tolerating a null list. Direct append (no
        /// dedup) — the converter's main reasons are each added at most
        /// once per pass, exactly as SBRM appended them.
        /// </summary>
        private static void AddReason(List<string> blockedReasons, string reason)
        {
            if (blockedReasons == null || string.IsNullOrWhiteSpace(reason))
            {
                return;
            }

            blockedReasons.Add(reason);
        }

        /// <summary>
        /// Dedup append, copied verbatim from SBRM's AddBlockedReason —
        /// the secondary-inputs path dedups its reasons.
        /// </summary>
        private static void AddBlockedReason(List<string> blockedReasons, string reason)
        {
            if (blockedReasons == null || string.IsNullOrWhiteSpace(reason))
            {
                return;
            }

            string trimmed = reason.Trim();
            for (int i = 0; i < blockedReasons.Count; i++)
            {
                if (string.Equals(blockedReasons[i], trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            blockedReasons.Add(trimmed);
        }

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
