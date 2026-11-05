using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.BreadDrivers
{
    /// <summary>
    /// Series 2 Pass 6 (bread drivers): the callable bakery production
    /// driver. It is NOT a daily-tick hook and it is NOT wired to any
    /// bootstrapper or scene: Codex calls PlanBatches, then
    /// RunScheduledDay, from the production day driver. The shop runtime
    /// is the surviving production model (Pass 2 decision); the staged
    /// chain reaches the shelf only through the existing
    /// Pass2BakeryStagedTransfer bridge (called directly by Codex, not
    /// wrapped here), with baked-day aging, never transfer-day aging,
    /// unless Kennedy decides otherwise in the single place that rule lives.
    ///
    /// Schedule gate, stated plainly: RunScheduledDay refuses a day
    /// earlier than the planned day (nothing bakes early) and refuses
    /// when nothing was planned (WorkDay is never called as a generic
    /// daily tick by accident). A later day is allowed as catch-up and
    /// is diagnosed as such. WorkDay itself is day-wide: it advances
    /// every active batch, so a driver that plans for several days at
    /// once must plan one day at a time or accept that behaviour; this
    /// driver plans one day per call and records it.
    ///
    /// Pre-checks, all loud refusals, nothing truncated: flour, notions,
    /// fuel (through ISeries2BakeryFuelSource), a ready oven, and a
    /// usable baker's hand kit. Labour is REPORTED, never enforced here:
    /// planned prep + bake minutes are compared against the shop's
    /// BakerMinutesPerDay (a code value, not canon) and against any
    /// caller-supplied daily budget; an over-budget day is diagnosed
    /// and still runs through WorkDay, which owns the labour budget.
    /// The WorkTimeBudget cap is never changed, truncated, or deleted
    /// by this driver. No seconds or sub-minute work: all durations are
    /// the existing minute values from BakeryBreadCatalog.
    /// Plain C#, no Unity dependency, Roslyn-testable.
    /// </summary>
    public sealed class Series2BakeryProductionDriver
    {
        private readonly BakeryShopRuntime bakery;
        private ISeries2BakeryFuelSource fuelSource;

        public Series2BakeryProductionDriver(
            BakeryShopRuntime bakery, ISeries2BakeryFuelSource fuelSource)
        {
            this.bakery = bakery;
            this.fuelSource = fuelSource;
        }

        /// <summary>Rebinds the fuel authority after bootstrap or restore.</summary>
        public void AttachFuelSource(ISeries2BakeryFuelSource source)
        {
            if (source != null) fuelSource = source;
        }

        public BakeryShopRuntime Bakery => bakery;

        /// <summary>Day index of the most recent PlanBatches call, or -1 if nothing planned.</summary>
        public int PlannedDayIndex { get; private set; } = -1;

        /// <summary>Batch ids from the most recent PlanBatches call.</summary>
        public List<string> PlannedBatchIds { get; } = new List<string>();

        /// <summary>
        /// Plans batches for one day through the real shop runtime.
        /// Returns the batch ids, or null with a loud diagnostic when
        /// the runtime refuses (unknown product, non-positive count).
        /// Planning dispenses nothing; the pre-check runs at RunScheduledDay.
        /// </summary>
        public List<string> PlanBatches(string productId, int batchCount, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (bakery == null)
            {
                diag.Add("Series2BakeryProductionDriver: no bakery runtime; nothing planned.");
                return null;
            }
            List<string> ids = bakery.PlanBatch(productId, batchCount, dayIndex, diag);
            if (ids == null) return null;
            PlannedDayIndex = Math.Max(0, dayIndex);
            PlannedBatchIds.Clear();
            PlannedBatchIds.AddRange(ids);
            diag.Add($"Series2BakeryProductionDriver: {ids.Count} batch(es) of '{productId}' planned for day {PlannedDayIndex}; run with RunScheduledDay.");
            return ids;
        }

        /// <summary>
        /// Series 2 Pass 9, additive only (PlanBatches and
        /// RunScheduledDay are untouched): clear-then-load restore
        /// of the planned day and batch ids. Planned state was in NO
        /// save DTO before this pass — a reload lost the plan, and
        /// RunScheduledDay would refuse the day as unplanned. The
        /// ids are preserved verbatim; a batch id the runtime no
        /// longer holds (baked, removed) is warned about, never
        /// silently dropped, and RunScheduledDay still re-validates
        /// through Precheck before any bake, so a restored plan can
        /// never bake blind. Restoring the same state twice yields
        /// the same plan (idempotent).
        /// </summary>
        public void RestorePlannedState(int dayIndex, List<string> batchIds, List<string> diag)
        {
            PlannedDayIndex = dayIndex;
            PlannedBatchIds.Clear();
            if (batchIds == null) return;
            foreach (string batchId in batchIds)
            {
                if (string.IsNullOrWhiteSpace(batchId)) continue;
                string trimmed = batchId.Trim();
                if (!PlannedBatchIds.Contains(trimmed)) PlannedBatchIds.Add(trimmed);
            }

            if (bakery != null && diag != null)
            {
                foreach (string batchId in PlannedBatchIds)
                {
                    bool found = false;
                    foreach (var batch in bakery.Batches)
                    {
                        if (batch != null && string.Equals(batch.BatchId, batchId, System.StringComparison.Ordinal))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                        diag.Add($"Series2BakeryProductionDriver: restored planned batch '{batchId}' is not in the bakery runtime's batches; the plan is preserved, and Precheck will refuse a bake it cannot validate.");
                }
            }
        }

        /// <summary>
        /// Pre-check for the planned batches: flour, notions, fuel, ready
        /// oven, hand kit. Returns null when the day may run, or a
        /// refusal diagnostic when it must not. Never truncates, never
        /// conjures, never calls WorkDay.
        /// </summary>
        public string Precheck(
            int dayIndex, bool bakerKitUsable,
            WorkstationDefinition ovenDefinition,
            Func<string, WorkstationComponentView?> findComponent,
            int? callerDailyBudgetMinutes, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (bakery == null) return "Series2BakeryProductionDriver pre-check refused: no bakery runtime.";
            if (PlannedBatchIds.Count == 0)
                return $"Series2BakeryProductionDriver pre-check refused: no batches planned for day {dayIndex}; plan first, never run an empty day.";
            if (fuelSource == null) return "Series2BakeryProductionDriver pre-check refused: no fuel source supplied; Pass 8 owns the fuel source, Pass 6 never guesses one.";

            int batches = PlannedBatchIds.Count;
            string productId = string.Empty;
            foreach (var batch in bakery.Batches)
            {
                if (batch != null && PlannedBatchIds.Contains(batch.BatchId)) { productId = batch.ProductId; break; }
            }
            BakeryProductSpec spec = BakeryBreadCatalog.GetSpec(productId);
            if (string.IsNullOrEmpty(spec.ProductId))
                return $"Series2BakeryProductionDriver pre-check refused: planned product '{productId}' has no catalog spec.";

            int flourNeeded = batches * spec.FlourUnitsPerBatch;
            int flourOnHand = bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId);
            if (flourOnHand < flourNeeded)
                return $"Series2BakeryProductionDriver pre-check refused: flour {flourOnHand} lb on hand, need {flourNeeded} lb for {batches} batch(es); bake nothing, batches stay planned.";
            int notionsNeeded = batches * spec.NotionUnitsPerBatch;
            int notionsOnHand = bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId);
            if (notionsOnHand < notionsNeeded)
                return $"Series2BakeryProductionDriver pre-check refused: notions {notionsOnHand}u on hand, need {notionsNeeded}u for {batches} batch(es); bake nothing, batches stay planned.";
            int fuelNeeded = batches * BakeryBreadCatalog.FuelUnitsPerFiring;
            int fuelOnHand = fuelSource.FuelUnitsOnHand();
            if (fuelOnHand < fuelNeeded)
                return $"Series2BakeryProductionDriver pre-check refused: fuel {fuelOnHand}u on hand, need {fuelNeeded}u for {batches} firing(s); bake nothing, batches stay planned.";
            if (!bakerKitUsable)
                return "Series2BakeryProductionDriver pre-check refused: no usable baker's hand kit (NX-1 teeth gate); bake nothing, batches stay planned.";
            int readyOvens = bakery.CountReadyOvens(ovenDefinition, findComponent, diag);
            if (readyOvens <= 0)
                return "Series2BakeryProductionDriver pre-check refused: no ready bake oven; bake nothing, batches stay planned.";

            int plannedMinutes = batches * (spec.PrepMinutes + spec.BakeMinutes);
            diag.Add($"Series2BakeryProductionDriver pre-check passed for day {dayIndex}: {batches} batch(es), flour {flourOnHand}/{flourNeeded} lb, notions {notionsOnHand}/{notionsNeeded}u, fuel {fuelOnHand}/{fuelNeeded}u, ready ovens {readyOvens}, planned labour {plannedMinutes}m vs shop day {BakeryShopRuntime.BakerMinutesPerDay}m (code value, not canon).");
            if (callerDailyBudgetMinutes.HasValue && plannedMinutes > callerDailyBudgetMinutes.Value)
                diag.Add($"Series2BakeryProductionDriver labour note: planned {plannedMinutes}m exceeds caller daily budget {callerDailyBudgetMinutes.Value}m by {plannedMinutes - callerDailyBudgetMinutes.Value}m; reported only, roster NOT truncated, cap NOT changed (WorkTimeBudget decision still open).");
            return null;
        }

        /// <summary>
        /// Runs the planned day: schedule gate, pre-check, fuel supply
        /// through the interface, then the real WorkDay. Returns the
        /// number of batches baked, or -1 on refusal (nothing baked,
        /// batches stay planned). Baked-day aging applies to the shelf
        /// inside WorkDay; this driver adds no aging of its own.
        /// </summary>
        public int RunScheduledDay(
            int dayIndex, bool bakerKitUsable,
            WorkstationDefinition ovenDefinition,
            Func<string, WorkstationComponentView?> findComponent,
            int? callerDailyBudgetMinutes, List<string> diag,
            EntityIdRegistry idRegistry = null)
        {
            diag = diag ?? new List<string>();
            if (bakery == null)
            {
                diag.Add("Series2BakeryProductionDriver: no bakery runtime; nothing baked.");
                return -1;
            }
            if (PlannedDayIndex < 0 || PlannedBatchIds.Count == 0)
            {
                diag.Add($"Series2BakeryProductionDriver: day {dayIndex} refused — nothing planned; call PlanBatches first, never run an empty day.");
                return -1;
            }
            if (dayIndex < PlannedDayIndex)
            {
                diag.Add($"Series2BakeryProductionDriver: day {dayIndex} refused — batches are planned for day {PlannedDayIndex}; nothing bakes early.");
                return -1;
            }
            if (dayIndex > PlannedDayIndex)
                diag.Add($"Series2BakeryProductionDriver: day {dayIndex} is a catch-up run for day {PlannedDayIndex} batches; baking late is diagnosed, never hidden.");

            string refusal = Precheck(dayIndex, bakerKitUsable, ovenDefinition, findComponent, callerDailyBudgetMinutes, diag);
            if (refusal != null)
            {
                diag.Add(refusal);
                return -1;
            }
            string fuelProblem = fuelSource.SupplyTo(bakery, PlannedBatchIds.Count * BakeryBreadCatalog.FuelUnitsPerFiring, dayIndex, diag);
            if (fuelProblem != null)
            {
                diag.Add($"Series2BakeryProductionDriver: day {dayIndex} refused — {fuelProblem}");
                return -1;
            }
            int baked = bakery.WorkDay(dayIndex, bakerKitUsable, ovenDefinition, findComponent, diag, idRegistry);
            int loaves = 0;
            foreach (var lot in bakery.BreadShelf)
            {
                if (lot != null && lot.BakedDayIndex == dayIndex) loaves += lot.UnitCount;
            }
            diag.Add($"Series2BakeryProductionDriver: day {dayIndex} baked {baked} batch(es), {loaves} unit(s) on the shelf baked today; shelf lots total {bakery.BreadShelf.Count}.");
            return baked;
        }

        // NOTE (Pass 6 scope): staged-chain lots reach the sellable shelf
        // ONLY through the existing Pass 2 bridge
        // (Pass2BakeryStagedTransfer.Transfer: baked-day aging, stale
        // refused, staged lot removed only on success). This driver
        // deliberately adds NO wrapper and NO second transfer path, so
        // the bridge stays the single staged-to-sellable route and its
        // staged-chain closure stays out of this driver's callers.
        // Codex calls the bridge directly with the shop runtime.
    }
}
