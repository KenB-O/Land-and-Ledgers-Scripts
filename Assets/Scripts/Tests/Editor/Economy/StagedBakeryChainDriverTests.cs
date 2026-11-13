// Plan 07 (2026-11-13, Slice 07): staged bakery chain driver tests.
//
// Tests the DRIVER (StagedBakeryChainDriver), not SimulationDrivers:
// construct the driver with the existing test doubles (TestDueEventScheduler
// and TestReservationAuthority copied from BakeryStagedProductionChainTests;
// they are private nested classes there and cannot be reused across files),
// a real TaskAuthority, a real WorkTimeBudgetStore, a real
// BakeryShopRuntime, and a fake clock. Seed the runtime stocks the way
// StockUp seeds the chain stocks (real lots with provenance, import order
// ids, named origins). Product is always BreadLoafId.
using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Generic;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class StagedBakeryChainDriverTests
    {
        #region Test doubles (copied from BakeryStagedProductionChainTests)

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

        private const int Day = 42;

        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private TestReservationAuthority reservations;
        private BakeryShopRuntime bakery;
        private EntityIdRegistry ids;
        private StagedBakeryChainDriver driver;
        private float clock;
        private EntityId baker;
        private EntityId business;
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(Day);
            reservations = new TestReservationAuthority();
            reservations.SetCapacity("workstation:bake-oven-0", 4f);
            reservations.SetCapacity("workstation:bake-oven-1", 4f);
            reservations.SetCapacity("workstation:proofing-rack", 4f);
            bakery = new BakeryShopRuntime("bakery-test");
            ids = new EntityIdRegistry();
            clock = 0f;
            baker = EntityId.For(EntityKind.Person, 11);
            business = EntityId.For(EntityKind.Business, 7);
            diag = new List<string>();
            driver = new StagedBakeryChainDriver(
                bakery, tasks, budgets, reservations, ids, () => clock, business);
            StockRuntime();
        }

        private void StockRuntime()
        {
            var d = new List<string>();
            Assert.IsNull(bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = EntityId.For(EntityKind.Lot, 100),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = 100,
                AcquiredDayIndex = 40,
                ImportOrderId = "IMP-042",
                OriginName = "Great Plains flour mill, via railhead",
            }, d), "flour stocking: " + string.Join("; ", d));
            Assert.IsNull(bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = EntityId.For(EntityKind.Lot, 101),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                Units = 10,
                AcquiredDayIndex = 40,
                ImportOrderId = "IMP-042",
                OriginName = "Great Plains flour mill, via railhead",
            }, d), "notions stocking: " + string.Join("; ", d));
            Assert.IsNull(bakery.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = EntityId.For(EntityKind.Lot, 102),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = 10,
                AcquiredDayIndex = 40,
                ImportOrderId = "IMP-043",
                OriginName = "Cordwood dealer, north road",
            }, d), "fuel stocking: " + string.Join("; ", d));
        }

        private StagedBakeryBatch BeginViaDriver()
        {
            Assert.IsTrue(driver.TryBeginBatch(Day, baker, diag), "begin refused: " + string.Join(" | ", diag));
            Assert.AreEqual(1, driver.Chain.Batches.Count, "exactly one batch after begin");
            return driver.Chain.Batches[0];
        }

        /// <summary>Drives a batch to completion through the driver only (active stages fully worked, passives pumped).</summary>
        private void DriveToCompletion(StagedBakeryBatch batch, StagedBakeryChainDriver d)
        {
            int guard = 0;
            while (batch.Process.IsActive && guard++ < 80)
            {
                if (!string.IsNullOrWhiteSpace(batch.Process.ParkedReason))
                {
                    d.DriveTick(Day, 10000, baker, diag);
                    if (!string.IsNullOrWhiteSpace(batch.Process.ParkedReason))
                        Assert.Fail("batch parked and retry did not clear it: " + batch.Process.ParkedReason);
                    continue;
                }
                ProductionStageDefinition def = batch.Process.CurrentStageDefinition;
                if (def.Kind == ProductionStageKind.ActiveLabor)
                {
                    d.DriveTick(Day, 10000, baker, diag);
                }
                else
                {
                    clock = batch.Process.CurrentStageState.DueGameSeconds + 1f;
                    d.DriveTick(Day, 1, baker, diag);
                }
            }
            Assert.IsTrue(batch.Process.IsComplete, "batch did not complete");
        }

        /// <summary>Drives a batch to the start of the target stage through the driver only (active stages fully worked).</summary>
        private void AdvanceToStage(StagedBakeryBatch batch, int targetStage, StagedBakeryChainDriver d)
        {
            int guard = 0;
            while (batch.Process.IsActive && batch.Process.CurrentStageIndex < targetStage && guard++ < 60)
            {
                ProductionStageDefinition def = batch.Process.CurrentStageDefinition;
                if (def.Kind == ProductionStageKind.ActiveLabor)
                {
                    d.DriveTick(Day, 10000, baker, diag);
                }
                else
                {
                    clock = batch.Process.CurrentStageState.DueGameSeconds + 1f;
                    d.DriveTick(Day, 1, baker, diag);
                }
                if (!string.IsNullOrWhiteSpace(batch.Process.ParkedReason)) break;
            }
            Assert.IsTrue(batch.Process.CurrentStageIndex >= targetStage || !string.IsNullOrWhiteSpace(batch.Process.ParkedReason),
                "batch did not reach stage " + targetStage + " (at " + batch.Process.CurrentStageIndex + ")");
        }

        /// <summary>Starts a plain TaskAuthority task for the baker (InProgress): the blocking task for test 2.</summary>
        private WorkTask StartBlockingTask()
        {
            Assert.IsTrue(tasks.RegisterDefinition(new TaskDefinition("test.blocking", "test.blocking", 60), out string reason), reason);
            WorkTask task = tasks.CreateTaskWithPlannedMinutes("test.blocking", business, Day, 60, customerRef: string.Empty);
            Assert.IsNotNull(task, "blocking task was not created");
            Assert.IsTrue(tasks.AssignTask(task.TaskId, baker, budgets, out reason), reason);
            Assert.IsTrue(tasks.StartTask(task.TaskId, Day, out reason), reason);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);
            return task;
        }

        [Test]
        public void DrivenBatch_AdvancesThroughNineStages_ToSellableShelf()
        {
            StagedBakeryBatch batch = BeginViaDriver();
            Assert.AreEqual(12, batch.FlourUnitsDispensed, "flour dispensed once at begin");
            Assert.AreEqual(1, batch.NotionsUnitsDispensed, "notions dispensed once at begin");
            Assert.AreEqual(1, batch.FuelUnitsDispensed, "fuel dispensed once at begin");
            // Pre-staging moved exactly one batch's inputs out of the runtime stocks.
            Assert.AreEqual(88, bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "runtime flour: 100 - 12 pre-staged");
            Assert.AreEqual(9, bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId),
                "runtime notions: 10 - 1 pre-staged");
            Assert.AreEqual(9, bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                "runtime fuel: 10 - 1 pre-staged");

            DriveToCompletion(batch, driver);

            Assert.IsTrue(batch.Process.IsComplete, "batch did not complete");
            Assert.AreEqual(0, driver.Chain.BreadShelf.Count, "the staged lot transferred off the staged shelf");
            Assert.AreEqual(1, bakery.BreadShelf.Count, "one shop lot on the sellable shelf");
            Assert.AreEqual(24, bakery.BreadShelf[0].UnitCount, "one batch yields 24 loaves (spec YieldUnitsPerBatch)");
            Assert.IsTrue(bakery.TransferredStagedLotIds.Contains("bread-" + batch.BatchId),
                "the staged lot id is recorded in the runtime's transferred set");
        }

        [Test]
        public void RefusedStageEntry_Parks_AndPerTickDrive_RetriesIt()
        {
            StagedBakeryBatch batch = BeginViaDriver();
            // Drive to the bake (passive) stage: receive, prepare, proof,
            // heat-oven, load. The load task handed off to passive wait,
            // releasing the baker.
            AdvanceToStage(batch, BakeryStagedProductionChain.StageBake, driver);
            Assert.AreEqual(BakeryStagedProductionChain.StageBake, batch.Process.CurrentStageIndex);

            // Block the baker with a plain InProgress task, then fire the
            // bake completion: the unload entry's tending resume refuses
            // on occupancy, so the batch parks after the bake.
            WorkTask blocking = StartBlockingTask();
            clock = batch.Process.StageStates[BakeryStagedProductionChain.StageBake].DueGameSeconds + 1f;
            driver.DriveTick(Day, 1, baker, diag);
            Assert.IsFalse(string.IsNullOrWhiteSpace(batch.Process.ParkedReason),
                "unload entry should park while the baker is busy");
            Assert.AreEqual(BakeryStagedProductionChain.StageBake, batch.Process.CurrentStageIndex,
                "parked after the completed bake stage");
            Assert.IsTrue(batch.Process.IsActive, "parked batch stays active");
            Assert.IsFalse(batch.Process.Failed, "parked batch did not fail");

            // Free the baker; the next per-tick drive retries the park
            // (Pump, RetryParked, Work) and the stage is entered.
            Assert.IsTrue(tasks.CompleteTask(blocking.TaskId, TaskOutcome.Success, Day, budgets),
                "blocking task should complete");
            driver.DriveTick(Day, 1, baker, diag);
            Assert.IsTrue(string.IsNullOrWhiteSpace(batch.Process.ParkedReason),
                "retry should clear the park once the baker is free");
            Assert.AreEqual(BakeryStagedProductionChain.StageUnload, batch.Process.CurrentStageIndex,
                "unload entered after retry");
            string carriedDefinitionId = "generic-production:" + batch.Process.ProcessId + ":phase:4:definition";
            WorkTask unloadTask = null;
            foreach (WorkTask t in tasks.GetAllOpenTasks())
            {
                if (t != null && t.Status == TaskStatus.InProgress && t.AssigneeId.Equals(baker)
                    && string.Equals(t.DefinitionId, carriedDefinitionId, StringComparison.Ordinal))
                    unloadTask = t;
            }
            Assert.IsNotNull(unloadTask,
                "unload continues on the carried load-stage task, InProgress for the baker");
        }

        [Test]
        public void StageprocDueEvent_DoesNotEmitUnknownTaskDiagnostic()
        {
            StagedBakeryBatch batch = BeginViaDriver();
            AdvanceToStage(batch, BakeryStagedProductionChain.StageBake, driver);
            Assert.AreEqual(BakeryStagedProductionChain.StageBake, batch.Process.CurrentStageIndex);
            Assert.IsTrue(driver.ChainDueEvents.ScheduledCount >= 1,
                "the bake stage scheduled its completion on the private scheduler");

            var tickDiag = new List<string>();
            clock = batch.Process.StageStates[BakeryStagedProductionChain.StageBake].DueGameSeconds + 1f;
            driver.DriveTick(Day, 1, baker, tickDiag);

            string all = string.Join(" | ", tickDiag) + " | " + string.Join(" | ", diag);
            Assert.IsFalse(all.Contains("unknown task"),
                "a stageproc: event must never reach the drivers' unknown-task diagnostic; diag: " + all);
            Assert.IsFalse(all.Contains("no staged process"),
                "the private scheduler's events must dispatch to a registered process; diag: " + all);
            Assert.IsTrue(batch.Process.CurrentStageIndex >= BakeryStagedProductionChain.StageUnload
                || !string.IsNullOrWhiteSpace(batch.Process.ParkedReason),
                "the batch advanced past bake (unload entered, or parked for the baker — both are forward motion, not a swallowed event)");
        }

        [Test]
        public void BeginBatch_RefusesWhenRuntimeStockShort_RollsBackCleanly()
        {
            // Seed short: drain the notions out of the runtime stock.
            var drainDiag = new List<string>();
            List<BakeryFlourDispenseLine> drained = bakery.FlourStock.TryDispenseUnits(
                BakeryBreadCatalog.NotionsItemId, 10, Day, drainDiag);
            Assert.IsNotNull(drained, "setup: notions drain failed: " + string.Join(" | ", drainDiag));
            Assert.AreEqual(0, bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId));

            int flourBefore = bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId);
            int fuelBefore = bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId);

            var beginDiag = new List<string>();
            Assert.IsFalse(driver.TryBeginBatch(Day, baker, beginDiag),
                "begin must refuse when the runtime stocks are short");
            Assert.IsTrue(string.Join(" | ", beginDiag).Contains("notions"),
                "the refusal names the shortfall: " + string.Join(" | ", beginDiag));
            Assert.AreEqual(0, driver.Chain.Batches.Count, "no batch exists after a refused begin");
            Assert.AreEqual(flourBefore, bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "flour untouched by the refused begin (all-or-nothing)");
            Assert.AreEqual(fuelBefore, bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                "fuel untouched by the refused begin (all-or-nothing)");
            Assert.AreEqual(0, bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId),
                "notions still drained — nothing moved back");
        }

        [Test]
        public void BeginBatch_RefusesSecondBatchWhileOneActive()
        {
            StagedBakeryBatch first = BeginViaDriver();
            Assert.IsNotNull(first);
            int flourAfterFirst = bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId);
            int notionsAfterFirst = bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId);
            int fuelAfterFirst = bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId);

            var secondDiag = new List<string>();
            Assert.IsFalse(driver.TryBeginBatch(Day, baker, secondDiag),
                "a second begin must refuse while one batch is active");
            Assert.AreEqual(1, driver.Chain.Batches.Count, "no second batch");
            Assert.AreEqual(flourAfterFirst, bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "no flour moved for the refused second batch");
            Assert.AreEqual(notionsAfterFirst, bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId),
                "no notions moved for the refused second batch");
            Assert.AreEqual(fuelAfterFirst, bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                "no fuel moved for the refused second batch");
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesActiveBatchAndScheduler()
        {
            StagedBakeryBatch batch = BeginViaDriver();
            AdvanceToStage(batch, BakeryStagedProductionChain.StageBake, driver);
            Assert.AreEqual(BakeryStagedProductionChain.StageBake, batch.Process.CurrentStageIndex);
            Assert.IsTrue(driver.ChainDueEvents.ScheduledCount >= 1,
                "the bake stage scheduled its completion on the private scheduler");

            BakeryStagedProductionChain.BakeryChainSaveDto dto = driver.CaptureSaveDto();
            Assert.IsNotNull(dto.DueEvents, "the chain DTO carries the private scheduler's events");

            // A SECOND driver over a fresh chain and scheduler, same
            // runtime / authorities / clock, loads the DTO.
            var reservations2 = new TestReservationAuthority();
            reservations2.SetCapacity("workstation:bake-oven-0", 4f);
            reservations2.SetCapacity("workstation:bake-oven-1", 4f);
            reservations2.SetCapacity("workstation:proofing-rack", 4f);
            var driver2 = new StagedBakeryChainDriver(
                bakery, tasks, budgets, reservations2, ids, () => clock, business);
            driver2.LoadFromSaveDto(dto, Day, baker);

            Assert.AreEqual(1, driver2.Chain.Batches.Count, "the batch restores");
            StagedBakeryBatch loaded = driver2.Chain.Batches[0];
            Assert.AreEqual(batch.BatchId, loaded.BatchId, "same batch id after load");
            Assert.AreEqual(BakeryStagedProductionChain.StageBake, loaded.Process.CurrentStageIndex,
                "same stage index after load");
            Assert.IsTrue(driver2.ChainDueEvents.ScheduledCount >= 1,
                "the private scheduler still holds the pending bake completion after load");

            // Drive the loaded driver to completion; the bread lands on the shop shelf.
            DriveToCompletion(loaded, driver2);
            Assert.IsTrue(loaded.Process.IsComplete, "loaded batch completes");
            Assert.AreEqual(0, driver2.Chain.BreadShelf.Count, "the staged lot transferred off the staged shelf");
            Assert.AreEqual(1, bakery.BreadShelf.Count, "one shop lot on the sellable shelf");
            Assert.AreEqual(24, bakery.BreadShelf[0].UnitCount, "one batch yields 24 loaves");
        }
    }
}
