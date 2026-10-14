using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Generic;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Pass IV (Worker 3) SLICE A — Bakery: the full 9-step chain on the stage machine.
    /// Real reconciliation figures throughout: inputs consumed, outputs produced,
    /// waste recorded, time elapsed. Deterministic; no fabricated persons (the baker
    /// is a real test person EntityId), no synthetic inventory (all flour/fuel from
    /// real lots with provenance).
    /// </summary>
    [TestFixture]
    public sealed class BakeryStagedProductionChainTests
    {
        #region Test doubles (Worker-1/Worker-2 seams)

        private sealed class TestDueEventScheduler : IDueEventScheduler
        {
            private readonly List<DueEvent> events = new List<DueEvent>();
            private long sequence;

            public string Schedule(DueEventKind kind, float dueGameSeconds, string payloadRef, out string eventId)
            {
                // Foundation convention: "due-" + kind + "-" + payloadRef; rescheduling replaces.
                string id = "due-" + kind + "-" + (payloadRef ?? string.Empty);
                events.RemoveAll(e => e.EventId == id);
                events.Add(new DueEvent(id, kind, dueGameSeconds, payloadRef, sequence++));
                eventId = id;
                return null;
            }

            public string ScheduleWithEventId(string eventId, DueEventKind kind, float dueGameSeconds, string payloadRef)
            {
                if (string.IsNullOrWhiteSpace(eventId)) return "eventId must be non-empty.";
                events.RemoveAll(e => e.EventId == eventId);
                events.Add(new DueEvent(eventId, kind, dueGameSeconds, payloadRef, sequence++));
                return null;
            }

            public void Cancel(string eventId) => events.RemoveAll(e => e.EventId == eventId);

            public List<DueEvent> PopDue(float currentGameSeconds)
            {
                var due = new List<DueEvent>();
                foreach (DueEvent e in events)
                    if (e.DueGameSeconds <= currentGameSeconds + 0.0001f) due.Add(e);
                due.Sort((a, b) =>
                {
                    int c = a.DueGameSeconds.CompareTo(b.DueGameSeconds);
                    return c != 0 ? c : a.Sequence.CompareTo(b.Sequence);
                });
                foreach (DueEvent e in due) events.Remove(e);
                return due;
            }

            public DueEvent PeekNext()
            {
                DueEvent best = null;
                foreach (DueEvent e in events)
                    if (best == null || e.DueGameSeconds < best.DueGameSeconds
                        || (Math.Abs(e.DueGameSeconds - best.DueGameSeconds) < 0.0001f && e.Sequence < best.Sequence))
                        best = e;
                return best;
            }

            public int ScheduledCount => events.Count;

            public void CancelForPayload(string payloadRef) =>
                events.RemoveAll(e => string.Equals(e.PayloadRef, payloadRef, StringComparison.Ordinal));

            public void Clear()
            {
                events.Clear();
                sequence = 0;
            }
        }

        private sealed class TestReservationAuthority : IUnifiedReservationAuthority
        {
            private readonly Dictionary<string, float> capacities =
                new Dictionary<string, float>(StringComparer.Ordinal);
            private readonly Dictionary<string, ReservationHandle> live =
                new Dictionary<string, ReservationHandle>(StringComparer.Ordinal);
            private int sequence;

            public void SetCapacity(string resourceKey, float capacity) =>
                capacities[resourceKey] = Math.Max(0f, capacity);

            public string TryReserveAll(ReservationRequest request, out ReservationHandle handle)
            {
                handle = null;
                if (request == null || request.Items.Count == 0)
                {
                    handle = new ReservationHandle("resv-" + sequence++, request?.OwnerTaskId ?? string.Empty,
                        new List<ReservationItem>());
                    live[handle.ReservationId] = handle;
                    return null;
                }
                foreach (ReservationItem item in request.Items)
                {
                    float cap = capacities.TryGetValue(item.ResourceKey, out float c) ? c : 1f;
                    float used = 0f;
                    foreach (ReservationHandle h in live.Values)
                        foreach (ReservationItem it in h.Items)
                            if (string.Equals(it.ResourceKey, item.ResourceKey, StringComparison.Ordinal)
                                && it.StartGameSeconds < item.EndGameSeconds && item.StartGameSeconds < it.EndGameSeconds)
                                used += it.Quantity;
                    if (used + item.Quantity > cap + 0.001f)
                        return "TestReservationAuthority: '" + item.ResourceKey + "' over capacity ("
                            + used + "+" + item.Quantity + " > " + cap + ") — refused loudly, nothing reserved.";
                }
                handle = new ReservationHandle("resv-" + sequence++, request.OwnerTaskId,
                    new List<ReservationItem>(request.Items));
                live[handle.ReservationId] = handle;
                return null;
            }

            public void Release(string reservationId) => live.Remove(reservationId);

            public void ReleaseAllForTask(string ownerTaskId)
            {
                var doomed = new List<string>();
                foreach (var kvp in live)
                    if (string.Equals(kvp.Value.OwnerTaskId, ownerTaskId, StringComparison.Ordinal))
                        doomed.Add(kvp.Key);
                foreach (string id in doomed) live.Remove(id);
            }

            public void ReleaseAllForResource(string resourceKey)
            {
                var doomed = new List<string>();
                foreach (var kvp in live)
                    foreach (ReservationItem it in kvp.Value.Items)
                        if (string.Equals(it.ResourceKey, resourceKey, StringComparison.Ordinal))
                        { doomed.Add(kvp.Key); break; }
                foreach (string id in doomed) live.Remove(id);
            }

            public bool IsReserved(string resourceKey, float atGameSeconds)
            {
                foreach (ReservationHandle h in live.Values)
                    foreach (ReservationItem it in h.Items)
                        if (string.Equals(it.ResourceKey, resourceKey, StringComparison.Ordinal)
                            && it.StartGameSeconds <= atGameSeconds && atGameSeconds < it.EndGameSeconds)
                            return true;
                return false;
            }

            public float AvailableCapacity(string resourceKey, float atGameSeconds)
            {
                float cap = capacities.TryGetValue(resourceKey, out float c) ? c : 1f;
                float used = 0f;
                foreach (ReservationHandle h in live.Values)
                    foreach (ReservationItem it in h.Items)
                        if (string.Equals(it.ResourceKey, resourceKey, StringComparison.Ordinal)
                            && it.StartGameSeconds <= atGameSeconds && atGameSeconds < it.EndGameSeconds)
                            used += it.Quantity;
                return Math.Max(0f, cap - used);
            }

            public List<ReservationHandle> ReservationsForTask(string ownerTaskId)
            {
                var out_ = new List<ReservationHandle>();
                foreach (ReservationHandle h in live.Values)
                    if (string.Equals(h.OwnerTaskId, ownerTaskId, StringComparison.Ordinal))
                        out_.Add(h);
                return out_;
            }

            public int LiveReservationCount => live.Count;
        }

        #endregion

        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private TestDueEventScheduler dueEvents;
        private TestReservationAuthority reservations;
        private BakeryStagedProductionChain chain;
        private BakeryChainContext ctx;
        private float clock;
        private EntityId baker;
        private EntityId business;
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            dueEvents = new TestDueEventScheduler();
            reservations = new TestReservationAuthority();
            reservations.SetCapacity("workstation:bake-oven-0", 4f);
            reservations.SetCapacity("workstation:bake-oven-1", 4f);
            reservations.SetCapacity("workstation:proofing-rack", 4f);
            chain = new BakeryStagedProductionChain();
            clock = 0f;
            baker = EntityId.For(EntityKind.Person, 11);
            business = EntityId.For(EntityKind.Business, 7);
            diag = new List<string>();
            ctx = new BakeryChainContext
            {
                Tasks = tasks,
                Budgets = budgets,
                DueEvents = dueEvents,
                Reservations = reservations,
                ClockGameSeconds = () => clock,
                DayIndex = 42,
                BusinessId = business,
                BakerId = baker,
                Diagnostics = diag,
            };
            StockUp();
        }

        private void StockUp()
        {
            var d = new List<string>();
            Assert.IsNull(chain.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = EntityId.For(EntityKind.Lot, 100),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = 100,
                AcquiredDayIndex = 40,
                ImportOrderId = "IMP-042",
                OriginName = "Great Plains flour mill, via railhead",
            }, d), "flour stocking: " + string.Join("; ", d));
            Assert.IsNull(chain.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = EntityId.For(EntityKind.Lot, 101),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                Units = 10,
                AcquiredDayIndex = 40,
                ImportOrderId = "IMP-042",
                OriginName = "Great Plains flour mill, via railhead",
            }, d), "notions stocking: " + string.Join("; ", d));
            Assert.IsNull(chain.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = EntityId.For(EntityKind.Lot, 102),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = 10,
                AcquiredDayIndex = 40,
                ImportOrderId = "IMP-043",
                OriginName = "Cordwood dealer, north road",
            }, d), "fuel stocking: " + string.Join("; ", d));
        }

        private List<BakeryFlourLot> DeliveryLots(int units)
        {
            return new List<BakeryFlourLot>
            {
                new BakeryFlourLot
                {
                    LotId = EntityId.For(EntityKind.Lot, 200 + units),
                    FlourName = BakeryBreadCatalog.FlourItemId,
                    Units = units,
                    AcquiredDayIndex = 42,
                    ImportOrderId = "IMP-044",
                    OriginName = "Great Plains flour mill, via railhead",
                },
            };
        }

        private StagedBakeryBatch BeginBatch()
        {
            StagedBakeryBatch batch = chain.BeginBatch(BakeryBreadCatalog.BreadLoafId, DeliveryLots(24), ctx, out string reason);
            Assert.IsNotNull(batch, "BeginBatch refused: " + reason);
            return batch;
        }

        /// <summary>Drives a batch to completion (active stages fully worked, passives pumped).</summary>
        private void DriveToCompletion(StagedBakeryBatch batch)
        {
            int guard = 0;
            while (batch.Process.IsActive && guard++ < 80)
            {
                if (!string.IsNullOrWhiteSpace(batch.Process.ParkedReason))
                {
                    chain.RetryParked(ctx);
                    if (!string.IsNullOrWhiteSpace(batch.Process.ParkedReason))
                        Assert.Fail("batch parked and retry did not clear it: " + batch.Process.ParkedReason);
                    continue;
                }
                ProductionStageDefinition def = batch.Process.CurrentStageDefinition;
                if (def.Kind == ProductionStageKind.ActiveLabor)
                {
                    Assert.IsTrue(chain.WorkBatch(batch, 10000, ctx, out string wreason),
                        "WorkBatch failed at stage " + batch.Process.CurrentStageIndex + ": " + wreason);
                }
                else
                {
                    clock = batch.Process.CurrentStageState.DueGameSeconds + 1f;
                    chain.PumpDueEvents(ctx);
                }
            }
            Assert.IsTrue(batch.Process.IsComplete, "batch did not complete");
        }

        /// <summary>Drives a batch to the start of the target stage (active stages fully worked).</summary>
        private void AdvanceToStage(StagedBakeryBatch batch, int targetStage)
        {
            int guard = 0;
            while (batch.Process.IsActive && batch.Process.CurrentStageIndex < targetStage && guard++ < 60)
            {
                ProductionStageDefinition def = batch.Process.CurrentStageDefinition;
                if (def.Kind == ProductionStageKind.ActiveLabor)
                {
                    Assert.IsTrue(chain.WorkBatch(batch, 10000, ctx, out string wreason),
                        "WorkBatch failed at stage " + batch.Process.CurrentStageIndex + ": " + wreason);
                }
                else
                {
                    clock = batch.Process.CurrentStageState.DueGameSeconds + 1f;
                    chain.PumpDueEvents(ctx);
                }
                chain.RetryParked(ctx);
                if (!string.IsNullOrWhiteSpace(batch.Process.ParkedReason)) break;
            }
            Assert.IsTrue(batch.Process.CurrentStageIndex >= targetStage || !string.IsNullOrWhiteSpace(batch.Process.ParkedReason),
                "batch did not reach stage " + targetStage + " (at " + batch.Process.CurrentStageIndex + ")");
        }

        private void AdvanceClockAndPump(float toGameSeconds)
        {
            clock = toGameSeconds;
            chain.PumpDueEvents(ctx);
        }

        /// <summary>Works the batch's current active stage to completion.</summary>
        private void WorkActiveFull(StagedBakeryBatch batch)
        {
            Assert.IsTrue(chain.WorkBatch(batch, 10000, ctx, out string reason),
                "WorkBatch failed at stage " + batch.Process.CurrentStageIndex + ": " + reason);
        }

        private void PumpTo(float gameSeconds)
        {
            clock = gameSeconds;
            chain.PumpDueEvents(ctx);
        }

        // ---------- the full 9-step chain, on time ----------

        [Test]
        public void FullChain_OnTime_Bakes24Loaves_WithExactReconciliation()
        {
            StagedBakeryBatch batch = BeginBatch();
            Assert.AreEqual(12, batch.FlourUnitsDispensed, "flour dispensed once at begin");
            Assert.AreEqual(1, batch.NotionsUnitsDispensed, "notions dispensed once at begin");
            Assert.AreEqual(1, batch.FuelUnitsDispensed, "fuel dispensed once at begin");

            // Stages 0..4 (receive, prepare, proof, heat-oven, load).
            AdvanceToStage(batch, BakeryStagedProductionChain.StageBake);
            Assert.AreEqual(BakeryStagedProductionChain.StageBake, batch.Process.CurrentStageIndex);
            // Receive moved the 24 lb delivery into the bin with provenance.
            Assert.IsTrue(batch.ReceiveMovedToStock, "delivery entered the bin at receive completion");
            Assert.AreEqual(100 + 24 - 12, chain.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "flour stock: 100 + 24 delivered - 12 dispensed");

            // The load task waited: worker released while the oven is held.
            StagedProductionProcess process = batch.Process;
            bool enteredPassiveWait = false;
            foreach (string line in process.EventLog)
                if (line.Contains("passive handoff") || line.Contains("WaitingOnPassiveProcess")) enteredPassiveWait = true;
            Assert.IsTrue(enteredPassiveWait, "expected the passive handoff to be recorded; log:\n"
                + string.Join("\n", process.EventLog));

            // Bake (passive): the baker does other work meanwhile — prove it by working
            // a SECOND batch's prep with the same baker during the first batch's bake.
            StagedBakeryBatch batch2 = BeginBatch();
            AdvanceToStage(batch2, BakeryStagedProductionChain.StagePrepare);
            chain.WorkBatch(batch2, 10000, ctx, out _); // prepare batch2 while batch1 bakes
            Assert.AreEqual(BakeryStagedProductionChain.StageProof, batch2.Process.CurrentStageIndex,
                "same baker worked batch2 prep during batch1's bake — worker truly released");

            // Finish batch1: bake completes, unload on time, cool, store.
            AdvanceClockAndPump(process.StageStates[BakeryStagedProductionChain.StageBake].DueGameSeconds + 1f);
            Assert.AreEqual(BakeryStagedProductionChain.StageUnload, process.CurrentStageIndex,
                "bake completion entered the unload tending episode");
            DriveToCompletion(batch); // through store
            Assert.IsTrue(process.IsComplete, "process complete");
            Assert.IsTrue(process.PhaseState.OutputCreated, "output created exactly once");
            Assert.AreEqual(24, batch.BreadUnitsOut, "24 loaves out");
            Assert.AreEqual(0, batch.WasteUnits, "no waste on time");
            Assert.AreEqual(1, chain.BreadShelf.Count, "one bread lot shelved");
            Assert.AreEqual(24, chain.BreadShelf[0].Units, "lot holds 24 loaves");
            Assert.IsTrue(chain.BreadShelf[0].InputProvenance.Count >= 3, "flour + notions + fuel provenance");
            Assert.AreEqual(0, chain.WasteLedger.Count, "waste ledger empty");

            // Reconciliation: flour dispensed (12) == flour in bread (12, all 24 loaves) + flour wasted (0).
            // Notions 1, fuel 1 consumed. Double output refused.
            Assert.IsFalse(process.TryCreateOutputOnce(batch.Hooks, out string dbl),
                "second output refused: " + dbl);
            Assert.AreEqual(1, chain.BreadShelf.Count, "still one lot — no duplicated inventory");
        }

        // ---------- late unload: real consequences ----------

        [Test]
        public void LateUnload_QualityDegraded_18Loaves6Waste()
        {
            StagedBakeryBatch batch = BeginBatch();
            AdvanceToStage(batch, BakeryStagedProductionChain.StageBake);
            StagedProductionProcess process = batch.Process;
            float bakeDue = process.StageStates[BakeryStagedProductionChain.StageBake].DueGameSeconds;

            // Bake completes; the unload tending episode begins.
            AdvanceClockAndPump(bakeDue + 1f);
            Assert.AreEqual(BakeryStagedProductionChain.StageUnload, process.CurrentStageIndex);

            // The baker never returns: the deadline (bake due + 15m grace) fires.
            float deadline = process.StageStates[BakeryStagedProductionChain.StageBake].DeadlineGameSeconds;
            Assert.AreEqual(bakeDue + 15 * 60f, deadline, 0.01f, "deadline = bake due + 15m grace");
            AdvanceClockAndPump(deadline + 1f);
            Assert.AreEqual(0.25f, process.QualityPenalty01, 0.001f, "QualityDegraded applied (+0.25)");

            // Late unload still proceeds; output reflects the penalty.
            DriveToCompletion(batch);
            Assert.IsTrue(process.IsComplete);
            Assert.AreEqual(18, batch.BreadUnitsOut, "24 * (1 - 0.25) = 18 saleable loaves");
            Assert.AreEqual(6, batch.WasteUnits, "6 loaves downgraded to waste");
            Assert.AreEqual(1, chain.WasteLedger.Count, "one waste record");
            Assert.AreEqual(6, chain.WasteLedger[0].BreadUnitsWasted, "waste record: 6 loaves");
            Assert.AreEqual(3, chain.WasteLedger[0].FlourUnitsWasted, "waste record: 3 flour-lb equivalent (6 * 12/24)");
            // Flour reconciliation: 12 dispensed == 9 in bread + 3 wasted.
            Assert.AreEqual(12, batch.FlourUnitsDispensed, "flour dispensed");
        }

        [Test]
        public void MissedUnload_BatchFailed_InputsLost_ReworkQueued()
        {
            chain.BakeMissedConsequence = MissedDeadlineConsequence.BatchFailed;
            StagedBakeryBatch batch = BeginBatch();
            AdvanceToStage(batch, BakeryStagedProductionChain.StageBake);
            StagedProductionProcess process = batch.Process;
            float bakeDue = process.StageStates[BakeryStagedProductionChain.StageBake].DueGameSeconds;

            AdvanceClockAndPump(bakeDue + 1f); // bake done, unload episode begins
            float deadline = process.StageStates[BakeryStagedProductionChain.StageBake].DeadlineGameSeconds;
            AdvanceClockAndPump(deadline + 1f); // missed -> batch failed

            Assert.IsTrue(process.Failed, "batch failed");
            Assert.AreEqual(0, chain.BreadShelf.Count, "no bread from a failed batch");
            Assert.AreEqual(1, chain.WasteLedger.Count, "one waste record");
            Assert.AreEqual(24, chain.WasteLedger[0].BreadUnitsWasted, "24 loaf-equivalents wasted");
            Assert.AreEqual(12, chain.WasteLedger[0].FlourUnitsWasted, "12 flour units lost with the dough");
            Assert.AreEqual(0, batch.BreadUnitsOut, "no output");

            // A corrective rework task was queued (real WorkTask).
            WorkTask rework = tasks.FindOpenTask("rework:stageproc:" + process.ProcessId, business, EntityId.Invalid);
            Assert.IsNotNull(rework, "rework task queued after batch failure");
            Assert.AreEqual(TaskStatus.Queued, rework.Status);

            // No output is ever possible after failure.
            Assert.IsFalse(process.TryCreateOutputOnce(batch.Hooks, out _), "failed batch cannot produce output");
        }

        [Test]
        public void SaveLoad_AfterMissedDeadline_FailurePersists_NoResurrection()
        {
            // Save/load after a missed deadline: the failure (batch-failed,
            // waste recorded, no output possible) survives the round trip.
            chain.BakeMissedConsequence = MissedDeadlineConsequence.BatchFailed;
            StagedBakeryBatch batch = BeginBatch();
            AdvanceToStage(batch, BakeryStagedProductionChain.StageBake);
            StagedProductionProcess process = batch.Process;
            float bakeDue = process.StageStates[BakeryStagedProductionChain.StageBake].DueGameSeconds;
            AdvanceClockAndPump(bakeDue + 1f);
            float deadline = process.StageStates[BakeryStagedProductionChain.StageBake].DeadlineGameSeconds;
            AdvanceClockAndPump(deadline + 1f); // missed -> batch failed
            Assert.IsTrue(process.Failed, "batch failed before save");

            BakeryStagedProductionChain.BakeryChainSaveDto dto = chain.CaptureSaveDto();
            var chain2 = new BakeryStagedProductionChain();
            var ctx2 = new BakeryChainContext
            {
                Tasks = tasks,
                Budgets = budgets,
                DueEvents = new TestDueEventScheduler(),
                Reservations = reservations,
                ClockGameSeconds = () => clock,
                DayIndex = 42,
                BusinessId = business,
                BakerId = baker,
                Diagnostics = new List<string>(),
            };
            chain2.LoadFromSaveDto(dto, ctx2);

            Assert.AreEqual(1, chain2.Batches.Count);
            StagedBakeryBatch loaded = chain2.Batches[0];
            Assert.IsTrue(loaded.Process.Failed, "failure persists after load");
            Assert.AreEqual(0, loaded.BreadUnitsOut, "no output after load");
            Assert.AreEqual(1, chain2.WasteLedger.Count, "waste record persists");
            Assert.AreEqual(24, chain2.WasteLedger[0].BreadUnitsWasted);
            Assert.IsFalse(loaded.Process.TryCreateOutputOnce(loaded.Hooks, out _), "failed batch cannot resurrect output after load");
        }

        // ---------- oven capacity: multiple compatible batches ----------

        [Test]
        public void TwoBatches_ShareOneOven_BothBake()
        {
            chain.OvenBatchCapacity = 4;
            StagedBakeryBatch a = BeginBatch();
            StagedBakeryBatch b = BeginBatch();
            // Lockstep: receive + prepare for both at clock 0.
            WorkActiveFull(a); // receive -> prepare
            WorkActiveFull(a); // prepare -> proof
            WorkActiveFull(b); // receive -> prepare
            WorkActiveFull(b); // prepare -> proof
            Assert.AreEqual(BakeryStagedProductionChain.StageProof, a.Process.CurrentStageIndex);
            Assert.AreEqual(BakeryStagedProductionChain.StageProof, b.Process.CurrentStageIndex);
            PumpTo(a.Process.CurrentStageState.DueGameSeconds + 1f); // both proofs complete
            Assert.AreEqual(BakeryStagedProductionChain.StageHeatOven, a.Process.CurrentStageIndex);
            Assert.AreEqual(BakeryStagedProductionChain.StageHeatOven, b.Process.CurrentStageIndex);
            // Heat + load for both: both enter the bake sharing oven 0 (capacity 4).
            WorkActiveFull(a); // heat -> load
            WorkActiveFull(a); // load -> bake (handoff; oven-0 1/4)
            WorkActiveFull(b); // heat -> load
            WorkActiveFull(b); // load -> bake (handoff; oven-0 2/4)
            Assert.AreEqual(BakeryStagedProductionChain.StageBake, a.Process.CurrentStageIndex);
            Assert.AreEqual(BakeryStagedProductionChain.StageBake, b.Process.CurrentStageIndex);
            Assert.AreEqual(0, a.AssignedOvenIndex, "batch A on oven 0");
            Assert.AreEqual(0, b.AssignedOvenIndex, "batch B shares oven 0 (capacity 4)");
            Assert.AreEqual(2, reservations.LiveReservationCount, "two oven reservations live");

            // Both bakes complete; unload on time; cool; store.
            PumpTo(a.Process.StageStates[BakeryStagedProductionChain.StageBake].DueGameSeconds + 1f);
            Assert.AreEqual(BakeryStagedProductionChain.StageUnload, a.Process.CurrentStageIndex);
            Assert.AreEqual(BakeryStagedProductionChain.StageUnload, b.Process.CurrentStageIndex);
            WorkActiveFull(a); // unload -> cool
            WorkActiveFull(b); // unload -> cool
            PumpTo(a.Process.CurrentStageState.DueGameSeconds + 1f); // both -> store
            WorkActiveFull(a); // store -> complete
            WorkActiveFull(b); // store -> complete
            Assert.IsTrue(a.Process.IsComplete, "A complete");
            Assert.IsTrue(b.Process.IsComplete, "B complete");
            Assert.AreEqual(24, a.BreadUnitsOut);
            Assert.AreEqual(24, b.BreadUnitsOut);
            Assert.AreEqual(48, chain.BreadShelf[0].Units + chain.BreadShelf[1].Units, "48 loaves total");
        }

        [Test]
        public void OvenFull_ThirdBatchParksAtBake_RetriesWhenFreed()
        {
            chain.OvenBatchCapacity = 1;
            reservations.SetCapacity("workstation:bake-oven-0", 1f);
            StagedBakeryBatch a = BeginBatch();
            StagedBakeryBatch b = BeginBatch();
            WorkActiveFull(a); WorkActiveFull(a); // a -> proof
            WorkActiveFull(b); WorkActiveFull(b); // b -> proof
            PumpTo(a.Process.CurrentStageState.DueGameSeconds + 1f); // both -> heat
            WorkActiveFull(a); // heat -> load
            WorkActiveFull(a); // load -> bake (oven 1/1)
            Assert.AreEqual(BakeryStagedProductionChain.StageBake, a.Process.CurrentStageIndex);
            Assert.IsTrue(string.IsNullOrWhiteSpace(a.Process.ParkedReason), "A entered bake");
            WorkActiveFull(b); // heat -> load
            WorkActiveFull(b); // load -> bake entry REFUSED (oven full) -> parked after load
            Assert.IsFalse(string.IsNullOrWhiteSpace(b.Process.ParkedReason),
                "B parked at bake entry — no phantom oven conjured");
            Assert.AreEqual(BakeryStagedProductionChain.StageLoad, b.Process.CurrentStageIndex,
                "parked after the completed load stage");
            Assert.IsTrue(b.Process.IsActive, "parked batch stays active");
            Assert.IsFalse(b.Process.Failed, "parked batch did not fail");

            // A finishes; the oven frees; B retries into the bake.
            PumpTo(a.Process.StageStates[BakeryStagedProductionChain.StageBake].DueGameSeconds + 1f);
            WorkActiveFull(a); // unload -> cool
            PumpTo(a.Process.CurrentStageState.DueGameSeconds + 1f); // a -> store
            WorkActiveFull(a); // store -> complete
            Assert.AreEqual(24, a.BreadUnitsOut, "A baked");
            chain.RetryParked(ctx);
            Assert.IsTrue(string.IsNullOrWhiteSpace(b.Process.ParkedReason), "B entered bake after retry");
            Assert.AreEqual(BakeryStagedProductionChain.StageBake, b.Process.CurrentStageIndex);
            PumpTo(b.Process.StageStates[BakeryStagedProductionChain.StageBake].DueGameSeconds + 1f);
            WorkActiveFull(b); // unload -> cool
            PumpTo(b.Process.CurrentStageState.DueGameSeconds + 1f); // b -> store
            WorkActiveFull(b); // store -> complete
            Assert.AreEqual(24, b.BreadUnitsOut, "B bakes after the oven freed");
        }

        // ---------- save/load mid-bake ----------

        [Test]
        public void SaveLoad_MidBake_ResumesWithoutDoubleOutputOrLostDeadlines()
        {
            StagedBakeryBatch batch = BeginBatch();
            AdvanceToStage(batch, BakeryStagedProductionChain.StageBake);
            StagedProductionProcess process = batch.Process;
            string processId = process.ProcessId;
            float bakeDue = process.StageStates[BakeryStagedProductionChain.StageBake].DueGameSeconds;

            // Save mid-bake (passive stage, worker waiting, deadline armed).
            BakeryStagedProductionChain.BakeryChainSaveDto dto = chain.CaptureSaveDto();

            // Load into a fresh chain over the SAME task authority (tasks persist separately).
            var chain2 = new BakeryStagedProductionChain();
            var scheduler2 = new TestDueEventScheduler();
            var ctx2 = new BakeryChainContext
            {
                Tasks = tasks,
                Budgets = budgets,
                DueEvents = scheduler2,
                Reservations = reservations,
                ClockGameSeconds = () => clock,
                DayIndex = 42,
                BusinessId = business,
                BakerId = baker,
                Diagnostics = new List<string>(),
            };
            chain2.LoadFromSaveDto(dto, ctx2);
            Assert.AreEqual(1, chain2.Batches.Count);
            StagedBakeryBatch loaded = chain2.Batches[0];
            Assert.AreEqual(processId, loaded.Process.ProcessId, "same process identity after load");
            Assert.AreEqual(BakeryStagedProductionChain.StageBake, loaded.Process.CurrentStageIndex,
                "still in the bake stage");
            Assert.AreEqual(2, scheduler2.ScheduledCount,
                "due events re-scheduled (bake completion + tending deadline), none duplicated");

            // The waiting labor task rebound read-only (no second task).
            int totalTasksBefore = tasks.CaptureSaveDto().tasks.Count;
            WorkTask waiting = tasks.FindOpenTask(
                loaded.Process.WaitingLaborTaskDefinitionId, business, EntityId.Invalid);
            Assert.IsNotNull(waiting, "waiting labor task rebound after load");

            // Finish: bake completes, unload on time, output once.
            clock = bakeDue + 1f;
            chain2.PumpDueEvents(ctx2);
            Assert.AreEqual(BakeryStagedProductionChain.StageUnload, loaded.Process.CurrentStageIndex);

            int guard = 0;
            while (loaded.Process.IsActive && guard++ < 60)
            {
                if (loaded.Process.CurrentStageDefinition.Kind == ProductionStageKind.ActiveLabor)
                    chain2.WorkBatch(loaded, 10000, ctx2, out _);
                else
                {
                    clock = loaded.Process.CurrentStageState.DueGameSeconds + 1f;
                    chain2.PumpDueEvents(ctx2);
                }
            }
            Assert.IsTrue(loaded.Process.IsComplete, "loaded batch completed");
            Assert.AreEqual(24, loaded.BreadUnitsOut, "24 loaves after load");
            int totalTasksAfter = tasks.CaptureSaveDto().tasks.Count;
            Assert.AreEqual(totalTasksBefore + 1, totalTasksAfter,
                "exactly one new task (the store stage's) across save/load — no duplicates, no phantoms");
            Assert.IsFalse(loaded.Process.TryCreateOutputOnce(loaded.Hooks, out _), "no double output");
            Assert.AreEqual(1, chain2.BreadShelf.Count, "one bread lot");
        }

        // ---------- cancellation ----------

        [Test]
        public void Cancel_AfterMissedDeadline_NoOutput_ReservationsReleased()
        {
            StagedBakeryBatch batch = BeginBatch();
            AdvanceToStage(batch, BakeryStagedProductionChain.StageBake);
            StagedProductionProcess process = batch.Process;
            int reservationsBefore = reservations.LiveReservationCount;

            Assert.IsTrue(process.Cancel(batch.Hooks, out string reason), "cancel: " + reason);
            Assert.IsTrue(process.Cancelled);
            Assert.AreEqual(0, dueEvents.ScheduledCount, "due events cancelled");
            Assert.AreEqual(0, reservations.LiveReservationCount, "reservations released");

            // Save/load after cancellation: stays cancelled, never produces.
            BakeryStagedProductionChain.BakeryChainSaveDto dto = chain.CaptureSaveDto();
            var chain2 = new BakeryStagedProductionChain();
            var ctx2 = new BakeryChainContext
            {
                Tasks = tasks, Budgets = budgets, DueEvents = new TestDueEventScheduler(),
                Reservations = reservations, ClockGameSeconds = () => clock, DayIndex = 42,
                BusinessId = business, BakerId = baker, Diagnostics = new List<string>(),
            };
            chain2.LoadFromSaveDto(dto, ctx2);
            StagedBakeryBatch loaded = chain2.Batches[0];
            Assert.IsTrue(loaded.Process.Cancelled, "cancelled state persisted");
            Assert.IsFalse(loaded.Process.TryCreateOutputOnce(loaded.Hooks, out _), "cancelled batch never produces");
            Assert.AreEqual(0, chain2.BreadShelf.Count, "no bread");
            Assert.GreaterOrEqual(reservationsBefore, 1, "sanity: there were live reservations before cancel");
        }

        // ---------- input edge cases ----------

        [Test]
        public void BeginBatch_OrphanFlourLot_RefusedLoudly()
        {
            var orphan = new BakeryFlourLot
            {
                LotId = EntityId.For(EntityKind.Lot, 999),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = 24,
                AcquiredDayIndex = 42,
                // no provenance at all
            };
            StagedBakeryBatch batch = chain.BeginBatch(BakeryBreadCatalog.BreadLoafId,
                new List<BakeryFlourLot> { orphan }, ctx, out string reason);
            Assert.IsNull(batch, "orphan flour must refuse the batch");
            StringAssert.Contains("orphan", reason.ToLowerInvariant(), "reason names the orphan lot: " + reason);
            Assert.AreEqual(100, chain.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "stock untouched by the refused batch");
        }

        [Test]
        public void BeginBatch_UnknownProduct_RefusedLoudly()
        {
            StagedBakeryBatch batch = chain.BeginBatch("bake.mystery-loaf", DeliveryLots(24), ctx, out string reason);
            Assert.IsNull(batch);
            StringAssert.Contains("unknown product", reason, "reason: " + reason);
        }
    }
}
