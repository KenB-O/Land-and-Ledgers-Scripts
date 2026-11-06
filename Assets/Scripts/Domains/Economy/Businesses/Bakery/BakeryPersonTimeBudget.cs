using System;
using LandLedgers.Time;

namespace LandLedgers.Economy.Businesses.Bakery
{
    /// <summary>
    /// W6 (W-5, Obj 9 design) — the baker person-time BUDGET
    /// reconciliation, as a pure helper. No booking lives here: this only
    /// answers which budget binds and whether a batch count fits.
    ///
    /// Two budgets govern one bake day:
    /// - the RUNTIME budget: BakeryShopRuntime.BakerMinutesPerDay (840, a
    ///   code value, not canon — D1D width-fix: 4 bread batches need 720
    ///   hands-on minutes, so 840 lets the oven's 4 firings bind);
    /// - the PERSON budget: WorkTimeBudget.DefaultDailyWorkMinutes (600, a
    ///   10-hour tuning day) × the capacity multiplier (nutrition × health,
    ///   DailyNeedsService.WorkCapacityFactor, 0..1).
    /// A batch costs its hands-on minutes (bread-loaf: 120 prep + 60 bake
    /// = 180; rolls/pie: 90 + 45 = 135). The binding budget is the one
    /// whose batch count is smaller; on a tie the runtime budget is named.
    /// At capacity 1.00 the person budget binds for bread-loaf (600/180 =
    /// 3 batches vs the runtime's 840/180 = 4); at capacity 0.25 the
    /// person budget is 150 minutes and not even one 180-minute batch fits
    /// — that is the hunger→capacity interaction, flagged for the Obj 45
    /// ruling (see the W6 design record in the session checkpoint): until
    /// the ruling lands, the Precheck overrun report becomes a refusal
    /// when the person budget cannot fit a batch, and no booking is
    /// implemented.
    /// </summary>
    public static class BakeryPersonTimeBudget
    {
        /// <summary>Hands-on minutes per bread-loaf batch (120 prep + 60 bake).</summary>
        public const int BreadLoafMinutesPerBatch = 180;

        /// <summary>Hands-on minutes per rolls/pie batch (90 prep + 45 bake).</summary>
        public const int RollsPieMinutesPerBatch = 135;

        /// <summary>Person budget for one day: 600 × capacity, floored, capacity clamped 0..1.</summary>
        public static int PersonMinutesPerDay(float capacity01)
        {
            float clamped = capacity01 < 0f ? 0f : capacity01 > 1f ? 1f : capacity01;
            return (int)Math.Floor(WorkTimeBudget.DefaultDailyWorkMinutes * clamped);
        }

        public static int MaxBatchesByPersonBudget(int minutesPerBatch, float capacity01)
        {
            if (minutesPerBatch <= 0) return 0;
            return PersonMinutesPerDay(capacity01) / minutesPerBatch;
        }

        public static int MaxBatchesByRuntimeBudget(int minutesPerBatch)
        {
            if (minutesPerBatch <= 0) return 0;
            return BakeryShopRuntime.BakerMinutesPerDay / minutesPerBatch;
        }

        /// <summary>The binding budget for this batch cost and capacity: "person" when the person budget fits fewer batches, else "runtime".</summary>
        public static string BindingBudget(int minutesPerBatch, float capacity01)
        {
            return MaxBatchesByPersonBudget(minutesPerBatch, capacity01) < MaxBatchesByRuntimeBudget(minutesPerBatch)
                ? "person"
                : "runtime";
        }

        /// <summary>
        /// Null when the batch count fits BOTH budgets; else a refusal
        /// naming the binding budget and the numbers. A single batch that
        /// does not fit the person budget is the hunger→capacity case: the
        /// baker cannot complete one batch today, however the runtime
        /// budget looks.
        /// </summary>
        public static string TryFitBatches(int batches, int minutesPerBatch, float capacity01)
        {
            if (batches <= 0) return "no batches were planned.";
            if (minutesPerBatch <= 0) return "no hands-on minutes per batch were supplied.";
            int personMinutes = PersonMinutesPerDay(capacity01);
            int personMax = MaxBatchesByPersonBudget(minutesPerBatch, capacity01);
            int runtimeMax = MaxBatchesByRuntimeBudget(minutesPerBatch);
            if (batches <= personMax && batches <= runtimeMax) return null;
            bool personBinds = personMax < runtimeMax;
            string binding = personBinds ? "person" : "runtime";
            int bindingMinutes = personBinds ? personMinutes : BakeryShopRuntime.BakerMinutesPerDay;
            int bindingMax = Math.Min(personMax, runtimeMax);
            return $"{batches} batch(es) at {minutesPerBatch}m each ({batches * minutesPerBatch}m) do not fit the {binding} budget " +
                $"({bindingMinutes}m, {bindingMax} batch(es)); person budget is {personMinutes}m at capacity {capacity01:0.00}, runtime budget is {BakeryShopRuntime.BakerMinutesPerDay}m.";
        }
    }
}
