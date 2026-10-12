using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Generic;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Bakery
{
    /// <summary>
    /// Pass IV (Worker 3) SLICE A — Bakery: the full 9-step chain on the production
    /// stage machine.
    ///
    ///   0 receive  (active)   — flour lots with provenance enter the bin
    ///   1 prepare  (active)   — dough: flour + notions dispensed into batch custody
    ///   2 proof    (passive)  — proofing rack held, no labor
    ///   3 heat-oven(active)   — oven prepared
    ///   4 load     (active)   — dough loaded; the task WAITS through the bake
    ///   5 bake     (passive)  — oven held (multi-batch capacity), worker released,
    ///                           may do other tasks; tending deadline with consequence
    ///   6 unload   (active)   — waiting task resumes; late unload has consequences
    ///   7 cool     (passive)  — cooling, no labor
    ///   8 store    (active)   — finished goods shelved; output created once
    ///
    /// Demonstrates: multiple compatible batches in one oven where capacity permits
    /// (oven as a ProcessingCapacity reservation); a late/failed batch with real
    /// consequences (quality/waste/batch-failed); input/output/waste reconciliation
    /// with exact figures; save/load mid-bake.
    ///
    /// Reuses: BakeryFlourStock/BakeryFuelStock (provenance lots), BakeryBreadCatalog
    /// (specs), StagedProductionProcess (stage machine), TaskAuthority (labor),
    /// IDueEventScheduler (passive progression), IUnifiedReservationAuthority
    /// (oven/rack capacity). No second production engine: the existing
    /// BakeryShopRuntime is untouched.
    ///
    /// Canon: XXXI Part III §§3.1-3.7. Technical: XIV Part II §§2.1-2.8.
    /// </summary>
    public sealed class BakeryStagedProductionChain
    {
        public const int StageReceive = 0;
        public const int StagePrepare = 1;
        public const int StageProof = 2;
        public const int StageHeatOven = 3;
        public const int StageLoad = 4;
        public const int StageBake = 5;
        public const int StageUnload = 6;
        public const int StageCool = 7;
        public const int StageStore = 8;

        /// <summary>TUNING: unload grace after the bake completes before consequences.</summary>
        public const int UnloadGraceMinutes = 15;

        /// <summary>TUNING: batches one oven firing holds.</summary>
        public int OvenCount = 1;

        /// <summary>TUNING: compatible batches per oven (capacity model).</summary>
        public int OvenBatchCapacity = 4;

        public int ProofingRackSlots = 4;

        /// <summary>Missed-unload consequence for the bake stage (per-batch override).</summary>
        public MissedDeadlineConsequence BakeMissedConsequence = MissedDeadlineConsequence.QualityDegraded;

        public readonly BakeryFlourStock FlourStock = new BakeryFlourStock();
        public readonly BakeryFuelStock FuelStock = new BakeryFuelStock();
        public readonly List<StagedBakeryBatch> Batches = new List<StagedBakeryBatch>();
        public readonly List<BakeryStagedBreadLot> BreadShelf = new List<BakeryStagedBreadLot>();
        public readonly List<BakeryStagedWasteRecord> WasteLedger = new List<BakeryStagedWasteRecord>();

        private readonly ProductionDueEventDriver dueDriver = new ProductionDueEventDriver();
        private int batchSeq;

        public string OvenReservationKey(int ovenIndex) => "workstation:bake-oven-" + ovenIndex;
        public string ProofingRackKey => "workstation:proofing-rack";

        // ---------- batch lifecycle ----------

        /// <summary>
        /// Begins a 9-step batch. Flour, notions and fuel are dispensed from stock into
        /// the batch's custody EXACTLY ONCE at begin (ConsumeInputsOnce with rollback);
        /// the delivery lots ride along and enter the bin when the receive stage
        /// completes. Refuses loudly on unknown product, orphan lots, missing inputs,
        /// or no oven capacity.
        /// </summary>
        public StagedBakeryBatch BeginBatch(
            string productId, List<BakeryFlourLot> incomingLots, BakeryChainContext ctx, out string reason)
        {
            reason = string.Empty;
            var batch = new StagedBakeryBatch { BatchId = "staged-batch-" + batchSeq++, ProductId = productId ?? string.Empty };
            if (!BakeryBreadCatalog.IsKnownProduct(productId))
            {
                reason = "BakeryStagedProductionChain: unknown product '" + productId + "'.";
                return null;
            }
            if (incomingLots != null)
            {
                foreach (BakeryFlourLot lot in incomingLots)
                {
                    if (lot == null) continue;
                    // Orphan rule mirrors BakeryFlourStock.ReceiveLot: no miller and no
                    // import order/origin means no chain — refused loudly, never stocked.
                    bool orphan = !lot.IsBootstrapEndowment
                        && string.IsNullOrWhiteSpace(lot.MillerBusinessId)
                        && (string.IsNullOrWhiteSpace(lot.ImportOrderId) || string.IsNullOrWhiteSpace(lot.OriginName));
                    if (orphan)
                    {
                        reason = "BakeryStagedProductionChain: incoming lot '" + lot.FlourName
                            + "' names no miller and no import order/origin — orphan flour refused loudly.";
                        return null;
                    }
                    batch.IncomingLots.Add(lot);
                }
            }

            // Oven assignment: most available capacity wins; none available refuses loudly.
            int ovenIndex = SelectOven(ctx, out reason);
            if (ovenIndex < 0) return null;
            batch.AssignedOvenIndex = ovenIndex;

            BakeryProductSpec spec = BakeryBreadCatalog.GetSpec(productId);
            ProductionMethodDefinition method = BuildNineStepMethod("bakery.staged." + batch.BatchId, spec);
            ProductionStagePlan plan = BuildNineStepPlan(spec, ovenIndex);

            var process = new StagedProductionProcess
            {
                ProcessId = "bakery-" + batch.BatchId,
                StagePlan = plan,
            };
            batch.ProductId = productId;
            batch.Process = process;
            batch.Method = method;

            StagedProcessHooks hooks = BuildHooks(batch, ctx);
            batch.Hooks = hooks;

            // Exactly-once input consumption: flour + notions + fuel into batch custody.
            if (!process.PhaseState.ConsumeInputsOnce(method,
                input => DispenseInput(batch, input, ctx),
                input => RollbackInput(batch, input, ctx)))
            {
                reason = "BakeryStagedProductionChain: inputs could not be dispensed exactly once — batch refused.";
                return null;
            }
            batch.InputsDispensed = true;

            if (!process.Begin(hooks, ctx.BakerId, out reason))
            {
                // Begin failed: return the dispensed inputs (provenance-preserving reversal).
                foreach (ProductionInputRequirement input in method.InputRequirements)
                    RollbackInput(batch, input, ctx);
                batch.InputsDispensed = false;
                reason = "BakeryStagedProductionChain: batch begin failed — " + reason;
                return null;
            }

            dueDriver.Register(process, hooks, ctx.BakerId);
            Batches.Add(batch);
            ctx.Log("BakeryStagedProductionChain: batch " + batch.BatchId + " (" + spec.DisplayName
                + ") began on oven " + ovenIndex + "; inputs dispensed once ("
                + batch.FlourUnitsDispensed + " flour, " + batch.NotionsUnitsDispensed
                + " notions, " + batch.FuelUnitsDispensed + " fuel).");
            return batch;
        }

        private int SelectOven(BakeryChainContext ctx, out string reason)
        {
            reason = string.Empty;
            int best = -1;
            float bestAvail = 0f;
            float now = ctx.Now();
            for (int i = 0; i < OvenCount; i++)
            {
                float avail = ctx.Reservations != null
                    ? ctx.Reservations.AvailableCapacity(OvenReservationKey(i), now)
                    : OvenBatchCapacity;
                if (avail > bestAvail)
                {
                    bestAvail = avail;
                    best = i;
                }
            }
            if (best < 0 || bestAvail < 1f)
            {
                reason = "BakeryStagedProductionChain: no oven capacity — all " + OvenCount
                    + " oven(s) fully booked. The batch waits; no phantom oven is conjured.";
                return -1;
            }
            return best;
        }

        private bool DispenseInput(StagedBakeryBatch batch, ProductionInputRequirement input, BakeryChainContext ctx)
        {
            var diag = new List<string>();
            if (string.Equals(input.ProductId, BakeryBreadCatalog.FlourItemId, StringComparison.Ordinal))
            {
                var lines = FlourStock.TryDispenseUnits(input.ProductId, (int)input.Quantity, ctx.DayIndex, diag);
                if (lines == null) return false;
                batch.FlourCustody.AddRange(lines);
                foreach (BakeryFlourDispenseLine line in lines)
                {
                    batch.FlourUnitsDispensed += line.UnitsTaken;
                    batch.ProvenanceLines.Add(line.UnitsTaken + "x " + line.FlourName + " from " + line.ProvenanceChain);
                }
                return true;
            }
            if (string.Equals(input.ProductId, BakeryBreadCatalog.NotionsItemId, StringComparison.Ordinal))
            {
                var lines = FlourStock.TryDispenseUnits(input.ProductId, (int)input.Quantity, ctx.DayIndex, diag);
                if (lines == null) return false;
                batch.NotionCustody.AddRange(lines);
                foreach (BakeryFlourDispenseLine line in lines)
                {
                    batch.NotionsUnitsDispensed += line.UnitsTaken;
                    batch.ProvenanceLines.Add(line.UnitsTaken + "x " + line.FlourName + " from " + line.ProvenanceChain);
                }
                return true;
            }
            if (string.Equals(input.ProductId, BakeryBreadCatalog.FuelItemId, StringComparison.Ordinal))
            {
                var lines = FuelStock.TryDispenseUnits(input.ProductId, (int)input.Quantity, ctx.DayIndex, diag);
                if (lines == null) return false;
                batch.FuelCustody.AddRange(lines);
                foreach (BakeryFuelDispenseLine line in lines)
                {
                    batch.FuelUnitsDispensed += line.UnitsTaken;
                    batch.ProvenanceLines.Add("fired with " + line.UnitsTaken + "x " + line.FuelName + " from " + line.ProvenanceChain);
                }
                return true;
            }
            return false;
        }

        private void RollbackInput(StagedBakeryBatch batch, ProductionInputRequirement input, BakeryChainContext ctx)
        {
            var diag = new List<string>();
            if (input == null) return;
            if (string.Equals(input.ProductId, BakeryBreadCatalog.FuelItemId, StringComparison.Ordinal))
            {
                foreach (BakeryFuelDispenseLine line in batch.FuelCustody)
                {
                    FuelStock.ReceiveLot(new BakeryFuelLot
                    {
                        FuelName = line.FuelName,
                        Units = line.UnitsTaken,
                        AcquiredDayIndex = ctx.DayIndex,
                        ImportOrderId = "return-" + batch.BatchId,
                        OriginName = "Returned to stock from failed batch dispense",
                        SupplierNote = "Originally: " + line.ProvenanceChain,
                    }, diag);
                }
                batch.FuelCustody.Clear();
                batch.FuelUnitsDispensed = 0;
                return;
            }
            List<BakeryFlourDispenseLine> lines = string.Equals(input.ProductId, BakeryBreadCatalog.FlourItemId, StringComparison.Ordinal)
                ? batch.FlourCustody : batch.NotionCustody;
            foreach (BakeryFlourDispenseLine line in lines)
            {
                FlourStock.ReceiveLot(new BakeryFlourLot
                {
                    FlourName = line.FlourName,
                    Units = line.UnitsTaken,
                    AcquiredDayIndex = ctx.DayIndex,
                    ImportOrderId = "return-" + batch.BatchId,
                    OriginName = "Returned to bin from failed batch dispense",
                    SupplierNote = "Originally: " + line.ProvenanceChain,
                }, diag);
            }
            lines.Clear();
            if (string.Equals(input.ProductId, BakeryBreadCatalog.FlourItemId, StringComparison.Ordinal))
                batch.FlourUnitsDispensed = 0;
            else batch.NotionsUnitsDispensed = 0;
        }

        // ---------- driving ----------

        /// <summary>
        /// Works the batch's current active stage, then handles stage transitions
        /// (receive lots enter the bin), completion (output once), and failure
        /// (waste recorded once).
        /// </summary>
        public bool WorkBatch(StagedBakeryBatch batch, int minutes, BakeryChainContext ctx, out string reason)
        {
            reason = string.Empty;
            if (batch == null || batch.Process == null) { reason = "No batch."; return false; }
            if (!batch.Process.IsActive)
            {
                DriveBatchBookkeeping(batch, ctx);
                reason = "Batch " + batch.BatchId + " is not active.";
                return false;
            }
            bool ok = batch.Process.WorkActiveStage(batch.Hooks, ctx.BakerId, minutes, out reason);
            DriveBatchBookkeeping(batch, ctx);
            return ok;
        }

        /// <summary>Pumps due events for all batches; returns staged events handled.</summary>
        public int PumpDueEvents(BakeryChainContext ctx)
        {
            if (ctx.DueEvents == null) return 0;
            List<DueEvent> popped = ctx.DueEvents.PopDue(ctx.Now());
            int handled = dueDriver.Dispatch(popped, ctx.Diagnostics);
            foreach (StagedBakeryBatch batch in Batches)
                DriveBatchBookkeeping(batch, ctx);
            return handled;
        }

        /// <summary>Retries parked stage entries (e.g. after oven capacity freed up).</summary>
        public void RetryParked(BakeryChainContext ctx)
        {
            foreach (StagedBakeryBatch batch in Batches)
            {
                if (batch == null || batch.Process == null) continue;
                if (string.IsNullOrWhiteSpace(batch.Process.ParkedReason)) continue;
                batch.Process.RetryParkedStage(batch.Hooks, ctx.BakerId, out string reason);
                if (!string.IsNullOrWhiteSpace(reason)) ctx.Log("BakeryStagedProductionChain: " + reason);
                DriveBatchBookkeeping(batch, ctx);
            }
        }

        private void DriveBatchBookkeeping(StagedBakeryBatch batch, BakeryChainContext ctx)
        {
            if (batch == null || batch.Process == null) return;
            StagedProductionProcess process = batch.Process;

            // Receive completion: the delivery lots enter the bin (provenance preserved).
            // Detected on the 0 -> 1 transition; parked batches (still at 0) keep custody.
            if (!batch.ReceiveMovedToStock && process.CurrentStageIndex > StageReceive && !process.Failed && !process.Cancelled)
            {
                var diag = new List<string>();
                foreach (BakeryFlourLot lot in batch.IncomingLots)
                {
                    string refusal = FlourStock.ReceiveLot(lot, diag);
                    if (refusal != null) diag.Add("BakeryStagedProductionChain: lot refused at receive — " + refusal);
                }
                batch.IncomingLots.Clear();
                batch.ReceiveMovedToStock = true;
                foreach (string d in diag) ctx.Log(d);
                ctx.Log("BakeryStagedProductionChain: batch " + batch.BatchId
                    + " receive complete — delivery entered the bin with provenance.");
            }
            batch.LastSeenStageIndex = process.CurrentStageIndex;

            if (process.IsComplete && !process.PhaseState.OutputCreated)
            {
                process.TryCreateOutputOnce(batch.Hooks, out string outputReason);
                if (!string.IsNullOrWhiteSpace(outputReason)) ctx.Log("BakeryStagedProductionChain: " + outputReason);
            }
            if (process.Failed && !batch.FailureRecorded)
            {
                RecordBatchFailureWaste(batch, ctx);
            }
        }

        private void RecordBatchFailureWaste(StagedBakeryBatch batch, BakeryChainContext ctx)
        {
            batch.FailureRecorded = true;
            BakeryProductSpec spec = BakeryBreadCatalog.GetSpec(batch.ProductId);
            int loafEquivalents = spec.YieldUnitsPerBatch;
            // The dispensed inputs are lost with the failed dough (consumed stays consumed).
            WasteLedger.Add(new BakeryStagedWasteRecord
            {
                BatchId = batch.BatchId,
                ProductId = batch.ProductId,
                BreadUnitsWasted = loafEquivalents,
                FlourUnitsWasted = batch.FlourUnitsDispensed,
                DayIndex = ctx.DayIndex,
                Reason = "batch failed: " + batch.Process.FailedReason,
                ProvenanceChain = string.Join(" | ", batch.ProvenanceLines),
            });
            batch.WasteUnits = loafEquivalents;
            ctx.Log("BakeryStagedProductionChain: batch " + batch.BatchId + " FAILED (" + batch.Process.FailedReason
                + ") — " + batch.FlourUnitsDispensed + " flour units lost with the dough; rework task queued.");
        }

        /// <summary>
        /// Output creation (exactly once): finished-goods lot with full provenance,
        /// quality/waste applied from the process record. Reconciliation:
        /// flour dispensed == flour in bread + flour wasted (loaf-proportional).
        /// </summary>
        private bool CreateBreadOutput(StagedProductionProcess process)
        {
            StagedBakeryBatch batch = FindBatch(process.ProcessId);
            if (batch == null) return false;
            BakeryProductSpec spec = BakeryBreadCatalog.GetSpec(batch.ProductId);
            int expected = spec.YieldUnitsPerBatch;
            int qualityLost = (int)Math.Floor(expected * process.QualityPenalty01);
            int wasteLost = 0;
            foreach (StageWasteRecord w in process.WasteRecords)
                wasteLost += (int)Math.Floor(expected * w.WasteFraction01);
            int saleable = Math.Max(0, expected - qualityLost - wasteLost);
            int wasted = expected - saleable;

            var lot = new BakeryStagedBreadLot
            {
                LotId = "bread-" + batch.BatchId,
                BatchId = batch.BatchId,
                ProductId = batch.ProductId,
                BakedDayIndex = DayIndexOf(process),
                Units = saleable,
                InputProvenance = new List<string>(batch.ProvenanceLines),
            };
            BreadShelf.Add(lot);
            batch.BreadUnitsOut = saleable;
            batch.WasteUnits = wasted;
            if (wasted > 0)
            {
                WasteLedger.Add(new BakeryStagedWasteRecord
                {
                    BatchId = batch.BatchId,
                    ProductId = batch.ProductId,
                    BreadUnitsWasted = wasted,
                    FlourUnitsWasted = (int)Math.Round(wasted * (batch.FlourUnitsDispensed / (float)Math.Max(1, expected))),
                    DayIndex = lot.BakedDayIndex,
                    Reason = "quality/waste at output (penalty " + process.QualityPenalty01.ToString("0.00") + ")",
                    ProvenanceChain = string.Join(" | ", batch.ProvenanceLines),
                });
            }
            // Custody lines move into the bread: the batch never double-counts.
            batch.FlourCustody.Clear();
            batch.NotionCustody.Clear();
            batch.FuelCustody.Clear();
            return true;
        }

        private int DayIndexOf(StagedProductionProcess process)
        {
            // The chain doesn't own the calendar; the hooks' day index is authoritative.
            // Stored per batch at output time via the calling context is cleaner — but the
            // callback signature is fixed, so recover from the first registered hooks.
            foreach (StagedBakeryBatch b in Batches)
                if (b != null && b.Process == process && b.Hooks != null)
                    return b.Hooks.DayIndex;
            return 0;
        }

        private StagedBakeryBatch FindBatch(string processId)
        {
            foreach (StagedBakeryBatch b in Batches)
                if (b != null && b.Process != null && string.Equals(b.Process.ProcessId, processId, StringComparison.Ordinal))
                    return b;
            return null;
        }

        private StagedProcessHooks BuildHooks(StagedBakeryBatch batch, BakeryChainContext ctx)
        {
            return new StagedProcessHooks
            {
                Method = batch.Method,
                Tasks = ctx.Tasks,
                Budgets = ctx.Budgets,
                TaskBridge = new GenericProductionTaskBridge(ctx.Tasks, ctx.Budgets, ctx.BusinessId, ctx.DayIndex),
                DueEvents = ctx.DueEvents,
                Reservations = ctx.Reservations,
                ClockGameSeconds = ctx.ClockGameSeconds,
                DayIndex = ctx.DayIndex,
                BusinessId = ctx.BusinessId,
                CreateOutput = CreateBreadOutput,
                TendingWorkerId = ctx.BakerId,
                TendingWorkMinutes = 5,
                Diagnostics = ctx.Diagnostics,
            };
        }

        // ---------- method + plan authoring ----------

        public static ProductionMethodDefinition BuildNineStepMethod(string methodId, BakeryProductSpec spec)
        {
            var method = new ProductionMethodDefinition
            {
                MethodId = methodId,
                OutputProductId = spec.ProductId,
                OutputQuantity = spec.YieldUnitsPerBatch,
            };
            method.InputRequirements.Add(new ProductionInputRequirement { ProductId = BakeryBreadCatalog.FlourItemId, Quantity = spec.FlourUnitsPerBatch });
            method.InputRequirements.Add(new ProductionInputRequirement { ProductId = BakeryBreadCatalog.NotionsItemId, Quantity = spec.NotionUnitsPerBatch });
            method.InputRequirements.Add(new ProductionInputRequirement { ProductId = BakeryBreadCatalog.FuelItemId, Quantity = BakeryBreadCatalog.FuelUnitsPerFiring });
            method.Phases.Add(new ProductionPhaseDefinition { PhaseId = "receive", ElapsedMinutes = 15, RequiresPerson = true });
            method.Phases.Add(new ProductionPhaseDefinition { PhaseId = "prepare", ElapsedMinutes = spec.PrepMinutes, RequiresPerson = true });
            method.Phases.Add(new ProductionPhaseDefinition { PhaseId = "proof", ElapsedMinutes = spec.ProofMinutes, RequiresPerson = false });
            method.Phases.Add(new ProductionPhaseDefinition { PhaseId = "heat-oven", ElapsedMinutes = 30, RequiresPerson = true });
            method.Phases.Add(new ProductionPhaseDefinition { PhaseId = "load", ElapsedMinutes = 20, RequiresPerson = true });
            method.Phases.Add(new ProductionPhaseDefinition { PhaseId = "bake", ElapsedMinutes = spec.BakeMinutes, RequiresPerson = false });
            method.Phases.Add(new ProductionPhaseDefinition { PhaseId = "unload", ElapsedMinutes = 15, RequiresPerson = true });
            method.Phases.Add(new ProductionPhaseDefinition { PhaseId = "cool", ElapsedMinutes = 30, RequiresPerson = false });
            method.Phases.Add(new ProductionPhaseDefinition { PhaseId = "store", ElapsedMinutes = 10, RequiresPerson = true });
            method.PerformanceSkillId = BakeryBreadCatalog.BakingSkillId;
            return method;
        }

        public ProductionStagePlan BuildNineStepPlan(BakeryProductSpec spec, int ovenIndex)
        {
            var plan = new ProductionStagePlan();
            plan.Stages.Add(new ProductionStageDefinition(ProductionStageKind.ActiveLabor, 15 * 60f)); // receive
            plan.Stages.Add(new ProductionStageDefinition(ProductionStageKind.ActiveLabor, spec.PrepMinutes * 60f)); // prepare
            var proof = new ProductionStageDefinition(ProductionStageKind.PassiveProcess, spec.ProofMinutes * 60f); // proof
            proof.StageReservationKeys.Add(ProofingRackKey);
            plan.Stages.Add(proof);
            plan.Stages.Add(new ProductionStageDefinition(ProductionStageKind.ActiveLabor, 30 * 60f)); // heat-oven
            plan.Stages.Add(new ProductionStageDefinition(ProductionStageKind.ActiveLabor, 20 * 60f)); // load
            var bake = new ProductionStageDefinition(
                ProductionStageKind.PassiveProcess,
                spec.BakeMinutes * 60f,
                0f,
                UnloadGraceMinutes * 60f,
                BakeMissedConsequence,
                0f,
                string.Empty,
                new List<string> { OvenReservationKey(ovenIndex) });
            plan.Stages.Add(bake);
            plan.Stages.Add(new ProductionStageDefinition(ProductionStageKind.ActiveLabor, 15 * 60f)); // unload
            plan.Stages.Add(new ProductionStageDefinition(ProductionStageKind.PassiveProcess, 30 * 60f)); // cool
            plan.Stages.Add(new ProductionStageDefinition(ProductionStageKind.ActiveLabor, 10 * 60f)); // store
            return plan;
        }

        // ---------- save/load ----------

        [Serializable]
        public sealed class BakeryChainSaveDto
        {
            public List<StagedBakeryBatchSaveDto> Batches = new List<StagedBakeryBatchSaveDto>();
            public BakeryFlourStock.BakeryFlourStockSaveDto FlourStock;
            public BakeryFuelStock.BakeryFuelStockSaveDto FuelStock;
            public List<BakeryStagedBreadLot> BreadShelf = new List<BakeryStagedBreadLot>();
            public List<BakeryStagedWasteRecord> WasteLedger = new List<BakeryStagedWasteRecord>();
            public int BatchSeq;
            public int OvenCount;
            public int OvenBatchCapacity;
        }

        public BakeryChainSaveDto CaptureSaveDto()
        {
            var dto = new BakeryChainSaveDto
            {
                FlourStock = FlourStock.CaptureSaveDto(),
                FuelStock = FuelStock.CaptureSaveDto(),
                BreadShelf = new List<BakeryStagedBreadLot>(BreadShelf),
                WasteLedger = new List<BakeryStagedWasteRecord>(WasteLedger),
                BatchSeq = batchSeq,
                OvenCount = OvenCount,
                OvenBatchCapacity = OvenBatchCapacity,
            };
            foreach (StagedBakeryBatch b in Batches)
            {
                if (b == null) continue;
                dto.Batches.Add(new StagedBakeryBatchSaveDto
                {
                    BatchId = b.BatchId,
                    ProductId = b.ProductId,
                    Method = b.Method,
                    IncomingLots = new List<BakeryFlourLot>(b.IncomingLots),
                    FlourCustody = new List<BakeryFlourDispenseLine>(b.FlourCustody),
                    NotionCustody = new List<BakeryFlourDispenseLine>(b.NotionCustody),
                    FuelCustody = new List<BakeryFuelDispenseLine>(b.FuelCustody),
                    ProvenanceLines = new List<string>(b.ProvenanceLines),
                    FlourUnitsDispensed = b.FlourUnitsDispensed,
                    NotionsUnitsDispensed = b.NotionsUnitsDispensed,
                    FuelUnitsDispensed = b.FuelUnitsDispensed,
                    InputsDispensed = b.InputsDispensed,
                    ReceiveMovedToStock = b.ReceiveMovedToStock,
                    LastSeenStageIndex = b.LastSeenStageIndex,
                    BreadUnitsOut = b.BreadUnitsOut,
                    WasteUnits = b.WasteUnits,
                    FailureRecorded = b.FailureRecorded,
                    AssignedOvenIndex = b.AssignedOvenIndex,
                    Process = b.Process != null ? b.Process.CaptureSaveDto() : null,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(BakeryChainSaveDto dto, BakeryChainContext ctx)
        {
            Batches.Clear();
            BreadShelf.Clear();
            WasteLedger.Clear();
            if (dto == null) return;
            batchSeq = dto.BatchSeq;
            OvenCount = Math.Max(1, dto.OvenCount);
            OvenBatchCapacity = Math.Max(1, dto.OvenBatchCapacity);
            if (dto.FlourStock != null) FlourStock.LoadFromSaveDto(dto.FlourStock);
            if (dto.FuelStock != null) FuelStock.LoadFromSaveDto(dto.FuelStock);
            if (dto.BreadShelf != null) BreadShelf.AddRange(dto.BreadShelf);
            if (dto.WasteLedger != null) WasteLedger.AddRange(dto.WasteLedger);
            if (dto.Batches == null) return;
            foreach (StagedBakeryBatchSaveDto bd in dto.Batches)
            {
                if (bd == null) continue;
                var batch = new StagedBakeryBatch
                {
                    BatchId = bd.BatchId,
                    ProductId = bd.ProductId,
                    Method = bd.Method,
                    IncomingLots = bd.IncomingLots ?? new List<BakeryFlourLot>(),
                    FlourCustody = bd.FlourCustody ?? new List<BakeryFlourDispenseLine>(),
                    NotionCustody = bd.NotionCustody ?? new List<BakeryFlourDispenseLine>(),
                    FuelCustody = bd.FuelCustody ?? new List<BakeryFuelDispenseLine>(),
                    ProvenanceLines = bd.ProvenanceLines ?? new List<string>(),
                    FlourUnitsDispensed = bd.FlourUnitsDispensed,
                    NotionsUnitsDispensed = bd.NotionsUnitsDispensed,
                    FuelUnitsDispensed = bd.FuelUnitsDispensed,
                    InputsDispensed = bd.InputsDispensed,
                    ReceiveMovedToStock = bd.ReceiveMovedToStock,
                    LastSeenStageIndex = bd.LastSeenStageIndex,
                    BreadUnitsOut = bd.BreadUnitsOut,
                    WasteUnits = bd.WasteUnits,
                    FailureRecorded = bd.FailureRecorded,
                    AssignedOvenIndex = bd.AssignedOvenIndex,
                };
                var process = new StagedProductionProcess();
                process.LoadFromSaveDto(bd.Process);
                batch.Process = process;
                batch.Hooks = BuildHooks(batch, ctx);
                string reacquireReason;
                if (process.IsActive && !process.ReacquireAfterLoad(batch.Hooks, out reacquireReason))
                    ctx.Log("BakeryStagedProductionChain: reacquire failed for " + batch.BatchId + " — " + reacquireReason);
                dueDriver.Register(process, batch.Hooks, ctx.BakerId);
                Batches.Add(batch);
            }
        }
    }

    /// <summary>Pass IV (Worker 3): injected authorities for the bakery chain (rebuilt after load).</summary>
    public sealed class BakeryChainContext
    {
        public TaskAuthority Tasks;
        public WorkTimeBudgetStore Budgets;
        public IDueEventScheduler DueEvents;
        public IUnifiedReservationAuthority Reservations;
        public Func<float> ClockGameSeconds;
        public int DayIndex;
        public EntityId BusinessId;
        public EntityId BakerId;
        public List<string> Diagnostics;

        public float Now() => ClockGameSeconds != null ? ClockGameSeconds() : 0f;
        public void Log(string message) { if (Diagnostics != null) Diagnostics.Add(message); }
    }

    /// <summary>Pass IV (Worker 3): one staged bakery batch — custody, provenance, reconciliation.</summary>
    [Serializable]
    public sealed class StagedBakeryBatch
    {
        public string BatchId = string.Empty;
        public string ProductId = string.Empty;
        public ProductionMethodDefinition Method;
        public List<BakeryFlourLot> IncomingLots = new List<BakeryFlourLot>();
        public List<BakeryFlourDispenseLine> FlourCustody = new List<BakeryFlourDispenseLine>();
        public List<BakeryFlourDispenseLine> NotionCustody = new List<BakeryFlourDispenseLine>();
        public List<BakeryFuelDispenseLine> FuelCustody = new List<BakeryFuelDispenseLine>();
        public List<string> ProvenanceLines = new List<string>();
        public int FlourUnitsDispensed;
        public int NotionsUnitsDispensed;
        public int FuelUnitsDispensed;
        public bool InputsDispensed;
        public bool ReceiveMovedToStock;
        public int LastSeenStageIndex;
        public int BreadUnitsOut;
        public int WasteUnits;
        public bool FailureRecorded;
        public int AssignedOvenIndex;

        [NonSerialized] public StagedProductionProcess Process;
        [NonSerialized] public StagedProcessHooks Hooks;
    }

    [Serializable]
    public sealed class StagedBakeryBatchSaveDto
    {
        public string BatchId = string.Empty;
        public string ProductId = string.Empty;
        public ProductionMethodDefinition Method;
        public List<BakeryFlourLot> IncomingLots = new List<BakeryFlourLot>();
        public List<BakeryFlourDispenseLine> FlourCustody = new List<BakeryFlourDispenseLine>();
        public List<BakeryFlourDispenseLine> NotionCustody = new List<BakeryFlourDispenseLine>();
        public List<BakeryFuelDispenseLine> FuelCustody = new List<BakeryFuelDispenseLine>();
        public List<string> ProvenanceLines = new List<string>();
        public int FlourUnitsDispensed;
        public int NotionsUnitsDispensed;
        public int FuelUnitsDispensed;
        public bool InputsDispensed;
        public bool ReceiveMovedToStock;
        public int LastSeenStageIndex;
        public int BreadUnitsOut;
        public int WasteUnits;
        public bool FailureRecorded;
        public int AssignedOvenIndex;
        public StagedProductionProcess.StagedProductionProcessSaveDto Process;
    }

    /// <summary>Pass IV (Worker 3): one finished-goods lot from the staged chain.</summary>
    [Serializable]
    public sealed class BakeryStagedBreadLot
    {
        public string LotId = string.Empty;
        public string BatchId = string.Empty;
        public string ProductId = string.Empty;
        public int BakedDayIndex;
        public int Units;
        public List<string> InputProvenance = new List<string>();
    }

    /// <summary>Pass IV (Worker 3): one waste record from the staged chain.</summary>
    [Serializable]
    public sealed class BakeryStagedWasteRecord
    {
        public string BatchId = string.Empty;
        public string ProductId = string.Empty;
        public int BreadUnitsWasted;
        public int FlourUnitsWasted;
        public int DayIndex;
        public string Reason = string.Empty;
        public string ProvenanceChain = string.Empty;
    }
}
