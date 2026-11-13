// Plan 07 (2026-11-13, Slice 07): Bakery chain driving, first slice.
//
// The smallest coherent vertical slice that makes one staged chain
// actually run in production: a plain-C# driver (harness-testable,
// following the Series2BakeryProductionDriver precedent) that begins
// one batch a day through a real employee, real TaskAuthority labour,
// real runtime stocks, and the existing staged-to-sellable transfer
// bridge. One batch at a time, one baker, BreadLoafId only.
//
// Event routing (decided, Plan 07 section 4): the chain's due events
// travel on their OWN DueEventScheduler instance, owned by this driver
// (ChainDueEvents). ctx.DueEvents is that scheduler, never
// hub.DueEvents. The drivers' DispatchDueEvent never learns the
// "stageproc:" payload, and PumpDueEvents can never consume a hub
// due event. Do NOT add a DispatchDueEvent case, a "stageproc:" skip,
// or a PopDue filter: that is the rejected alternative.
//
// Labour legitimacy (Plan 07 section 5): the chain's labour tasks get
// NO schedule-window reservation in this slice, so
// AdvanceAutonomousWork cannot double-accrue them. The sweep skip
// (StaleInProgressSweep) and the TaskCompletionDue backstop exclusion
// (SimulationDrivers.SyncTaskCompletionDueEvents) are the guard rails
// that keep the autonomous drivers from completing a staged task out
// from under the stage machine.
using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Generic;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;

namespace LandLedgers.Economy.Businesses.Bakery
{
    /// <summary>
    /// Drives the staged Bakery chain (BakeryStagedProductionChain) in
    /// production. Plain C#, no Unity dependency, Roslyn-testable. The
    /// driver owns no calendar: every public method takes dayIndex
    /// explicitly. The driver never logs to Debug directly; diagnostics
    /// go to the caller-supplied list, which SimulationDrivers routes
    /// under [BakeryProduction].
    /// </summary>
    public sealed class StagedBakeryChainDriver
    {
        /// <summary>Prefix of every staged labour task definition id (bridge convention).</summary>
        public const string StagedTaskDefinitionPrefix = "generic-production:";

        public BakeryStagedProductionChain Chain { get; }
        public DueEventScheduler ChainDueEvents { get; }
        public BakeryShopRuntime Bakery { get; }

        private readonly TaskAuthority tasks;
        private readonly WorkTimeBudgetStore budgets;
        private readonly IUnifiedReservationAuthority reservations;
        private readonly EntityIdRegistry ids;
        private readonly Func<float> clockGameSeconds;
        private readonly EntityId businessId;

        // Day-keyed refusal log (Plan 05 pattern): one line per distinct
        // refusal per day, so a refused tick does not spam the log.
        private readonly HashSet<string> refusalLoggedKeys = new HashSet<string>(StringComparer.Ordinal);
        private int refusalLoggedDay = int.MinValue;

        /// <summary>Creates a driver over a fresh chain and a fresh private due-event scheduler.</summary>
        public StagedBakeryChainDriver(
            BakeryShopRuntime bakery,
            TaskAuthority tasks,
            WorkTimeBudgetStore budgets,
            IUnifiedReservationAuthority reservations,
            EntityIdRegistry ids,
            Func<float> clockGameSeconds,
            EntityId businessId)
            : this(bakery, new BakeryStagedProductionChain(), new DueEventScheduler(),
                tasks, budgets, reservations, ids, clockGameSeconds, businessId)
        {
        }

        /// <summary>
        /// Adopts an existing chain and scheduler (restore path: the Pass 9
        /// state holds the live instances and the driver is rebuilt around
        /// them when the instance changes).
        /// </summary>
        public StagedBakeryChainDriver(
            BakeryShopRuntime bakery,
            BakeryStagedProductionChain chain,
            DueEventScheduler chainDueEvents,
            TaskAuthority tasks,
            WorkTimeBudgetStore budgets,
            IUnifiedReservationAuthority reservations,
            EntityIdRegistry ids,
            Func<float> clockGameSeconds,
            EntityId businessId)
        {
            Bakery = bakery;
            Chain = chain ?? new BakeryStagedProductionChain();
            ChainDueEvents = chainDueEvents ?? new DueEventScheduler();
            this.tasks = tasks;
            this.budgets = budgets;
            this.reservations = reservations;
            this.ids = ids;
            this.clockGameSeconds = clockGameSeconds;
            this.businessId = businessId;
        }

        public bool HasActiveBatch
        {
            get
            {
                foreach (StagedBakeryBatch batch in Chain.Batches)
                    if (batch != null && batch.Process != null && batch.Process.IsActive)
                        return true;
                return false;
            }
        }

        private BakeryChainContext BuildContext(int dayIndex, EntityId bakerId, List<string> diag)
        {
            return new BakeryChainContext
            {
                Tasks = tasks,
                Budgets = budgets,
                // PRIVATE scheduler (Plan 07 section 4): never hub.DueEvents.
                DueEvents = ChainDueEvents,
                Reservations = reservations,
                ClockGameSeconds = clockGameSeconds,
                DayIndex = dayIndex,
                BusinessId = businessId,
                BakerId = bakerId,
                Diagnostics = diag,
            };
        }

        /// <summary>
        /// Begins one batch of BreadLoafId. Refuses (loudly, in diag, no
        /// state changed) when a batch is already active, when the baker
        /// id is invalid, or when the runtime stocks cannot cover one
        /// batch. Otherwise pre-stages exactly one batch's inputs from
        /// the runtime stocks into the chain stocks, then calls
        /// chain.BeginBatch with an EMPTY incoming-lots list (the receive
        /// stage then completes with no delivery to move). One batch per
        /// call; the day drive calls it at most once per day.
        /// </summary>
        public bool TryBeginBatch(int dayIndex, EntityId bakerId, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (Bakery == null)
            {
                diag.Add("StagedBakeryChainDriver: batch refused — no bakery runtime supplied.");
                return false;
            }
            if (!bakerId.IsValid)
            {
                diag.Add("StagedBakeryChainDriver: batch refused — no baker resolves for the bakery.");
                return false;
            }
            if (HasActiveBatch)
            {
                diag.Add("StagedBakeryChainDriver: batch refused — a staged batch is already active (one batch at a time).");
                return false;
            }

            BakeryProductSpec spec = BakeryBreadCatalog.GetSpec(BakeryBreadCatalog.BreadLoafId);

            // All-or-nothing pre-check first: every input must be covered
            // before ANY unit moves, so a shortfall leaves the runtime
            // stocks exactly as seeded (TryDispenseUnits is atomic on
            // shortfall too; the pre-check is belt-and-braces).
            int flourOnHand = Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId);
            if (flourOnHand < spec.FlourUnitsPerBatch)
            {
                diag.Add("StagedBakeryChainDriver: batch refused — flour shortfall: "
                    + flourOnHand + " units of '" + BakeryBreadCatalog.FlourItemId + "' on hand, need "
                    + spec.FlourUnitsPerBatch + ".");
                return false;
            }
            int notionsOnHand = Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId);
            if (notionsOnHand < spec.NotionUnitsPerBatch)
            {
                diag.Add("StagedBakeryChainDriver: batch refused — notions shortfall: "
                    + notionsOnHand + " units of '" + BakeryBreadCatalog.NotionsItemId + "' on hand, need "
                    + spec.NotionUnitsPerBatch + ".");
                return false;
            }
            int fuelOnHand = Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId);
            if (fuelOnHand < BakeryBreadCatalog.FuelUnitsPerFiring)
            {
                diag.Add("StagedBakeryChainDriver: batch refused — fuel shortfall: "
                    + fuelOnHand + " units of '" + BakeryBreadCatalog.FuelItemId + "' on hand, need "
                    + BakeryBreadCatalog.FuelUnitsPerFiring + ".");
                return false;
            }

            // Dispense from the runtime stocks, then receive into the
            // chain stocks. A failure after any dispense returns the
            // already-dispensed lines to the runtime stocks by the
            // reverse path, so the batch still refuses with no stock moved.
            List<BakeryFlourDispenseLine> flourLines = Bakery.FlourStock.TryDispenseUnits(
                BakeryBreadCatalog.FlourItemId, spec.FlourUnitsPerBatch, dayIndex, diag);
            if (flourLines == null)
            {
                diag.Add("StagedBakeryChainDriver: batch refused — flour dispense failed after the pre-check passed.");
                return false;
            }
            List<BakeryFlourDispenseLine> notionLines = Bakery.FlourStock.TryDispenseUnits(
                BakeryBreadCatalog.NotionsItemId, spec.NotionUnitsPerBatch, dayIndex, diag);
            if (notionLines == null)
            {
                ReturnLinesToRuntime(dayIndex, flourLines, null, null, diag);
                diag.Add("StagedBakeryChainDriver: batch refused — notions dispense failed; dispensed flour returned.");
                return false;
            }
            List<BakeryFuelDispenseLine> fuelLines = Bakery.FuelStock.TryDispenseUnits(
                BakeryBreadCatalog.FuelItemId, BakeryBreadCatalog.FuelUnitsPerFiring, dayIndex, diag);
            if (fuelLines == null)
            {
                ReturnLinesToRuntime(dayIndex, flourLines, notionLines, null, diag);
                diag.Add("StagedBakeryChainDriver: batch refused — fuel dispense failed; dispensed flour and notions returned.");
                return false;
            }

            // Receive the dispensed lines into the chain stocks. The lot
            // construction follows the chain's own RollbackInput precedent
            // (BakeryStagedProductionChain.RollbackInput): a fresh lot per
            // dispense line, the transfer named as its trade link, the
            // original provenance carried in SupplierNote. Nothing is
            // created: every unit left a real runtime lot. On a refusal,
            // the quantities already staged go back out of the chain
            // stocks and the lines not yet staged go straight back to
            // the runtime stocks — each unit returns exactly once.
            int stagedFlour = 0;
            int stagedNotions = 0;
            int stagedFuel = 0;
            for (int i = 0; i < flourLines.Count; i++)
            {
                BakeryFlourDispenseLine line = flourLines[i];
                string refusal = Chain.FlourStock.ReceiveLot(new BakeryFlourLot
                {
                    FlourName = line.FlourName,
                    Units = line.UnitsTaken,
                    AcquiredDayIndex = dayIndex,
                    ImportOrderId = "staged-transfer-day-" + dayIndex,
                    OriginName = "Transferred from bakery runtime stock for a staged batch",
                    SupplierNote = "Originally: " + line.ProvenanceChain,
                }, diag);
                if (refusal != null)
                {
                    ReturnStagedQuantitiesToRuntime(dayIndex, stagedFlour, stagedNotions, stagedFuel, diag);
                    ReturnLinesToRuntime(dayIndex, Remaining(flourLines, i), notionLines, fuelLines, diag);
                    diag.Add("StagedBakeryChainDriver: batch refused — chain flour stock refused a staged lot: " + refusal);
                    return false;
                }
                stagedFlour += line.UnitsTaken;
            }
            for (int i = 0; i < notionLines.Count; i++)
            {
                BakeryFlourDispenseLine line = notionLines[i];
                string refusal = Chain.FlourStock.ReceiveLot(new BakeryFlourLot
                {
                    FlourName = line.FlourName,
                    Units = line.UnitsTaken,
                    AcquiredDayIndex = dayIndex,
                    ImportOrderId = "staged-transfer-day-" + dayIndex,
                    OriginName = "Transferred from bakery runtime stock for a staged batch",
                    SupplierNote = "Originally: " + line.ProvenanceChain,
                }, diag);
                if (refusal != null)
                {
                    ReturnStagedQuantitiesToRuntime(dayIndex, stagedFlour, stagedNotions, stagedFuel, diag);
                    ReturnLinesToRuntime(dayIndex, null, Remaining(notionLines, i), fuelLines, diag);
                    diag.Add("StagedBakeryChainDriver: batch refused — chain flour stock refused a staged notions lot: " + refusal);
                    return false;
                }
                stagedNotions += line.UnitsTaken;
            }
            for (int i = 0; i < fuelLines.Count; i++)
            {
                BakeryFuelDispenseLine line = fuelLines[i];
                string refusal = Chain.FuelStock.ReceiveLot(new BakeryFuelLot
                {
                    FuelName = line.FuelName,
                    Units = line.UnitsTaken,
                    AcquiredDayIndex = dayIndex,
                    ImportOrderId = "staged-transfer-day-" + dayIndex,
                    OriginName = "Transferred from bakery runtime stock for a staged batch",
                    SupplierNote = "Originally: " + line.ProvenanceChain,
                }, diag);
                if (refusal != null)
                {
                    ReturnStagedQuantitiesToRuntime(dayIndex, stagedFlour, stagedNotions, stagedFuel, diag);
                    ReturnLinesToRuntime(dayIndex, null, null, Remaining(fuelLines, i), diag);
                    diag.Add("StagedBakeryChainDriver: batch refused — chain fuel stock refused a staged lot: " + refusal);
                    return false;
                }
                stagedFuel += line.UnitsTaken;
            }

            BakeryChainContext ctx = BuildContext(dayIndex, bakerId, diag);
            StagedBakeryBatch batch = Chain.BeginBatch(
                BakeryBreadCatalog.BreadLoafId, new List<BakeryFlourLot>(), ctx, out string reason);
            if (batch == null)
            {
                // The begin refused after pre-staging (e.g. no oven
                // capacity): return the staged inputs to the runtime
                // stocks so the refusal leaves no stock moved.
                ReturnStagedQuantitiesToRuntime(dayIndex, stagedFlour, stagedNotions, stagedFuel, diag);
                diag.Add("StagedBakeryChainDriver: batch refused — " + reason + " (staged inputs returned).");
                return false;
            }

            diag.Add("StagedBakeryChainDriver: batch " + batch.BatchId + " began ("
                + spec.DisplayName + "; " + stagedFlour + " flour, " + stagedNotions
                + " notions, " + stagedFuel + " fuel pre-staged from runtime stocks).");
            return true;
        }

        /// <summary>
        /// The per-tick drive, in this exact order: (1) pump the PRIVATE
        /// scheduler's due events; (2) retry parked stage entries (the
        /// Plan 04 debt: a refused stage-entry resume parks, the next
        /// tick re-attempts it); (3) work each active batch's current
        /// stage; (4) transfer every lot on the staged shelf through the
        /// existing bridge. A refused tick neither fails, completes, nor
        /// deletes the batch: active stages wait, passive stages keep
        /// their own clock. A transfer refusal is logged (day-keyed) and
        /// the lot stays on the staged shelf; a stale refusal is NOT
        /// converted to waste in this slice. No-op when there is no
        /// active batch and the staged shelf is empty.
        /// </summary>
        public void DriveTick(int dayIndex, int wholeMinutes, EntityId bakerId, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (wholeMinutes <= 0) return;
            if (!HasActiveBatch && Chain.BreadShelf.Count == 0) return;

            BakeryChainContext ctx = BuildContext(dayIndex, bakerId, diag);

            int handled = Chain.PumpDueEvents(ctx);
            if (handled > 0)
                diag.Add("StagedBakeryChainDriver: pumped " + handled + " staged due event(s).");

            Chain.RetryParked(ctx);

            // Snapshot: WorkBatch can complete a batch (output once) and
            // DriveBatchBookkeeping runs inside it; iterate a copy.
            var active = new List<StagedBakeryBatch>(Chain.Batches);
            foreach (StagedBakeryBatch batch in active)
            {
                if (batch == null || batch.Process == null || !batch.Process.IsActive) continue;
                if (!Chain.WorkBatch(batch, wholeMinutes, ctx, out string reason))
                    LogWorkRefusalDayKeyed(dayIndex, batch, reason, diag);
            }

            // Transfer: every lot on the staged shelf goes through the
            // single staged-to-sellable route. Success removes the staged
            // lot (removal IS the delivered mark). Refusal leaves the lot
            // put; stale lots are logged and left (waste deferred).
            var shelf = new List<BakeryStagedBreadLot>(Chain.BreadShelf);
            foreach (BakeryStagedBreadLot lot in shelf)
            {
                if (lot == null) continue;
                BakeryBreadLot moved = Pass2BakeryStagedTransfer.Transfer(Chain, Bakery, lot.LotId, dayIndex, ids, diag);
                if (moved == null)
                {
                    LogRefusalDayKeyed(dayIndex, "transfer:" + lot.LotId,
                        "StagedBakeryChainDriver: staged lot '" + lot.LotId
                        + "' was not transferred to the sellable shelf; it stays on the staged shelf.", diag);
                }
            }
        }

        /// <summary>Captures the chain state plus the private scheduler's events.</summary>
        public BakeryStagedProductionChain.BakeryChainSaveDto CaptureSaveDto()
        {
            BakeryStagedProductionChain.BakeryChainSaveDto dto = Chain.CaptureSaveDto();
            dto.DueEvents = ChainDueEvents.CaptureSaveDto();
            return dto;
        }

        /// <summary>
        /// Restores a captured DTO into this driver's chain and private
        /// scheduler. The scheduler loads FIRST, so the chain load's
        /// ReacquireAfterLoad re-schedules future due events with replace
        /// semantics and cannot double-schedule.
        /// </summary>
        public void LoadFromSaveDto(BakeryStagedProductionChain.BakeryChainSaveDto dto, int dayIndex, EntityId bakerId)
        {
            if (dto == null) return;
            ChainDueEvents.LoadFromSaveDto(dto.DueEvents);
            Chain.LoadFromSaveDto(dto, BuildContext(dayIndex, bakerId, new List<string>()));
        }

        private void LogWorkRefusalDayKeyed(int dayIndex, StagedBakeryBatch batch, string reason, List<string> diag)
        {
            if (string.IsNullOrWhiteSpace(reason)) return;
            // A passive stage waiting on its own clock is expected every
            // tick, not a refusal: stay silent for it.
            if (string.Equals(reason, "Current stage is not ActiveLabor.", StringComparison.Ordinal)
                || string.Equals(reason, "Process is not active.", StringComparison.Ordinal))
                return;
            LogRefusalDayKeyed(dayIndex, "work:" + batch.BatchId + ":" + reason,
                "StagedBakeryChainDriver: batch " + batch.BatchId + " work refused — " + reason, diag);
        }

        private void LogRefusalDayKeyed(int dayIndex, string key, string message, List<string> diag)
        {
            if (dayIndex != refusalLoggedDay)
            {
                refusalLoggedDay = dayIndex;
                refusalLoggedKeys.Clear();
            }
            if (!refusalLoggedKeys.Add(key)) return;
            diag.Add(message);
        }

        private static List<T> Remaining<T>(List<T> lines, int fromIndex)
        {
            if (lines == null || fromIndex >= lines.Count) return null;
            var rest = new List<T>();
            for (int i = fromIndex; i < lines.Count; i++) rest.Add(lines[i]);
            return rest;
        }

        /// <summary>
        /// Returns dispense lines that never reached the chain stocks to
        /// the runtime stocks (reverse path, RollbackInput shape).
        /// </summary>
        private void ReturnLinesToRuntime(int dayIndex,
            List<BakeryFlourDispenseLine> flourLines,
            List<BakeryFlourDispenseLine> notionLines,
            List<BakeryFuelDispenseLine> fuelLines,
            List<string> diag)
        {
            if (flourLines != null)
            {
                foreach (BakeryFlourDispenseLine line in flourLines)
                {
                    string refusal = Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
                    {
                        FlourName = line.FlourName,
                        Units = line.UnitsTaken,
                        AcquiredDayIndex = dayIndex,
                        ImportOrderId = "return-staged-day-" + dayIndex,
                        OriginName = "Returned to runtime stock from a refused staged batch",
                        SupplierNote = "Originally: " + line.ProvenanceChain,
                    }, diag);
                    if (refusal != null)
                        diag.Add("StagedBakeryChainDriver: WARNING — dispensed flour could not be returned: " + refusal);
                }
            }
            if (notionLines != null)
            {
                foreach (BakeryFlourDispenseLine line in notionLines)
                {
                    string refusal = Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
                    {
                        FlourName = line.FlourName,
                        Units = line.UnitsTaken,
                        AcquiredDayIndex = dayIndex,
                        ImportOrderId = "return-staged-day-" + dayIndex,
                        OriginName = "Returned to runtime stock from a refused staged batch",
                        SupplierNote = "Originally: " + line.ProvenanceChain,
                    }, diag);
                    if (refusal != null)
                        diag.Add("StagedBakeryChainDriver: WARNING — dispensed notions could not be returned: " + refusal);
                }
            }
            if (fuelLines != null)
            {
                foreach (BakeryFuelDispenseLine line in fuelLines)
                {
                    string refusal = Bakery.FuelStock.ReceiveLot(new BakeryFuelLot
                    {
                        FuelName = line.FuelName,
                        Units = line.UnitsTaken,
                        AcquiredDayIndex = dayIndex,
                        ImportOrderId = "return-staged-day-" + dayIndex,
                        OriginName = "Returned to runtime stock from a refused staged batch",
                        SupplierNote = "Originally: " + line.ProvenanceChain,
                    }, diag);
                    if (refusal != null)
                        diag.Add("StagedBakeryChainDriver: WARNING — dispensed fuel could not be returned: " + refusal);
                }
            }
        }

        /// <summary>
        /// Returns quantities already received into the chain stocks to
        /// the runtime stocks: dispense the exact quantities back out of
        /// the chain stocks (units are fungible; provenance travels in
        /// the return lots' SupplierNote) and receive them into the
        /// runtime stocks.
        /// </summary>
        private void ReturnStagedQuantitiesToRuntime(int dayIndex, int flourUnits, int notionUnits, int fuelUnits, List<string> diag)
        {
            if (flourUnits > 0)
            {
                List<BakeryFlourDispenseLine> lines = Chain.FlourStock.TryDispenseUnits(
                    BakeryBreadCatalog.FlourItemId, flourUnits, dayIndex, diag);
                if (lines != null) ReturnLinesToRuntime(dayIndex, lines, null, null, diag);
                else diag.Add("StagedBakeryChainDriver: WARNING — staged flour could not be dispensed back out of the chain stock.");
            }
            if (notionUnits > 0)
            {
                List<BakeryFlourDispenseLine> lines = Chain.FlourStock.TryDispenseUnits(
                    BakeryBreadCatalog.NotionsItemId, notionUnits, dayIndex, diag);
                if (lines != null) ReturnLinesToRuntime(dayIndex, null, lines, null, diag);
                else diag.Add("StagedBakeryChainDriver: WARNING — staged notions could not be dispensed back out of the chain stock.");
            }
            if (fuelUnits > 0)
            {
                List<BakeryFuelDispenseLine> lines = Chain.FuelStock.TryDispenseUnits(
                    BakeryBreadCatalog.FuelItemId, fuelUnits, dayIndex, diag);
                if (lines != null) ReturnLinesToRuntime(dayIndex, null, null, lines, diag);
                else diag.Add("StagedBakeryChainDriver: WARNING — staged fuel could not be dispensed back out of the chain stock.");
            }
        }
    }
}
