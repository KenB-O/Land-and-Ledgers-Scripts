// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Slice A1 (2026-11-13) — stalled detailed-shipment recovery.
//
// QA-01: a load task whose source debit refuses after its minutes hit zero
// must reach a terminal state and free the loader, with the shipment
// Delayed (retryable) instead of stuck in Loading.
// QA-04: the physical-receiving gate must not pin an unload task forever;
// after the bound the task closes as failed, the unloader is freed, and
// the shipment is Delayed with the gate's own reason.
// QA-02: the production tick itself resumes Delayed shipments whose resume
// state it can drive from; no manual ResumeShipment call is needed.
// QA-03 (partial): a stalled Delayed shipment still holding a dispatch
// reservation is named in diagnostics.
// LRM lot guard (Agent 3 finding 1): a lot-backed shipment routed into the
// clock-system delivery path must not be credited as anonymous stock.

using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Transport;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class FreightStalledShipmentRecoveryTests
    {
        private const int Day = 42;

        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private FreightShipmentService service;
        private FreightShipmentHooks hooks;
        private EntityId loader;
        private EntityId handler;
        private EntityId driverA;
        private List<EntityId> teamA;
        private Dictionary<string, int> recipientStock;
        private bool recipientAccepts;

        [SetUp]
        public void SetUp()
        {
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(Day);
            service = new FreightShipmentService();
            recipientStock = new Dictionary<string, int>(StringComparer.Ordinal);
            recipientAccepts = true;
            hooks = new FreightShipmentHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                DayIndex = Day,
                Diagnostics = new List<string>(),
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    if (!recipientAccepts)
                    {
                        reason = "recipient refused the delivery";
                        return 0;
                    }
                    string key = string.IsNullOrWhiteSpace(shipment.destinationBusinessInstanceId)
                        ? "nowhere" : shipment.destinationBusinessInstanceId;
                    recipientStock[key] = recipientStock.TryGetValue(key, out int have) ? have + quantityUnits : quantityUnits;
                    return quantityUnits;
                },
            };
            loader = EntityId.For(EntityKind.Person, 701);
            handler = EntityId.For(EntityKind.Person, 701);
            driverA = EntityId.For(EntityKind.Person, 702);
            teamA = new List<EntityId> { EntityId.For(EntityKind.Animal, 11), EntityId.For(EntityKind.Animal, 12) };
            service.RegisterWagon(Wagon("wagon-1"));
            service.RegisterWagon(Wagon("wagon-2"));
        }

        private static EquipmentAsset Wagon(string id) => new EquipmentAsset
        {
            AssetId = id,
            Kind = "farm-wagon",
            DisplayName = id,
            Condition01 = 0.9f,
            PayloadMassCapacityKg = 1000f,
            CargoVolumeCapacityM3 = 4f,
        };

        private static FreightCargoSpec GrainSpec() => new FreightCargoSpec { MassKgPerUnit = 2f, VolumeM3PerUnit = 0.01f };

        private LogisticsShipmentState CreateLotCustodyShipment(string id)
        {
            LogisticsShipmentState shipment = service.CreateShipment(id, "mill-1", "bakery-1",
                10, GrainSpec(), "wagon-1", hooks, out string createReason);
            Assert.IsNotNull(shipment, createReason);
            Assert.IsTrue(service.TryBindSourceLot(shipment, "flour-lot-1", "flour", out string bindReason), bindReason);
            return shipment;
        }

        private EntityId OpenLoadTaskId(string shipmentId)
        {
            List<WorkTask> open = tasks.GetOpenTasks(EntityId.For(EntityKind.Business, 3));
            Assert.AreEqual(1, open.Count, "exactly one open freight task is expected");
            return open[0].TaskId;
        }

        private void AssertLoaderCanStartUnrelatedWork(string context)
        {
            tasks.RegisterDefinition(new TaskDefinition("other-work", "Unrelated legitimate job", 30), out _);
            WorkTask other = tasks.CreateTask("other-work", EntityId.For(EntityKind.Business, 3), Day);
            Assert.IsTrue(tasks.AssignTask(other.TaskId, loader, budgets, out string assignReason), context + ": " + assignReason);
            Assert.IsTrue(tasks.StartTask(other.TaskId, Day, out string startReason), context + ": " + startReason);
        }

        // ---------- QA-01: debit refusal after the load task is done ----------

        [Test]
        public void LoadDebitRefusal_ClosesTask_FreesLoader_DelaysShipment()
        {
            LogisticsShipmentState shipment = CreateLotCustodyShipment("debit-refused");
            int debitCalls = 0;
            hooks.CommitSourceLot = (LogisticsShipmentState s, int quantity, out string reason) =>
            {
                debitCalls++;
                reason = "supplier refused: lot committed elsewhere";
                return false;
            };

            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out string beginReason), beginReason);
            EntityId loadTaskId = OpenLoadTaskId("debit-refused");
            Assert.IsFalse(service.WorkLoading(shipment, 60, hooks, out string workReason), "the load did not complete: " + workReason);

            Assert.AreEqual(1, debitCalls, "the debit was attempted exactly once");
            Assert.AreEqual(TaskStatus.Failed, tasks.FindTask(loadTaskId).Status, "the load task must reach a terminal state");
            Assert.AreEqual(0, tasks.GetOpenTasks(EntityId.For(EntityKind.Business, 3)).Count, "no open freight task may remain");
            AssertLoaderCanStartUnrelatedWork("the loader must be freed after a failed debit");
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status);
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.resumeStateAfterDelay, "the retry resumes at Planned so BeginLoading re-attempts the load");
            Assert.IsFalse(shipment.sourceLotDebitApplied, "a refused debit moves no cargo");
            Assert.IsFalse(shipment.loadApplied);
            Assert.AreEqual(0, shipment.RemainingQuantityUnits);
            Assert.IsTrue(shipment.BlockedReason.Contains("committed elsewhere"), shipment.BlockedReason);
        }

        [Test]
        public void LoadDebitRefusal_NoAuthorityWiredAfterStart_ClosesTask()
        {
            LogisticsShipmentState shipment = CreateLotCustodyShipment("debit-unwired");
            hooks.CommitSourceLot = (LogisticsShipmentState s, int quantity, out string reason) =>
            {
                reason = string.Empty;
                return true;
            };

            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out string beginReason), beginReason);
            EntityId loadTaskId = OpenLoadTaskId("debit-unwired");

            // The debit authority is unwired between the load start and the
            // work tick (the per-day hooks are rebuilt by the daily drive).
            hooks.CommitSourceLot = null;
            Assert.IsFalse(service.WorkLoading(shipment, 60, hooks, out _));

            Assert.AreEqual(TaskStatus.Failed, tasks.FindTask(loadTaskId).Status, "the load task must reach a terminal state");
            AssertLoaderCanStartUnrelatedWork("the loader must be freed when the debit authority is missing");
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status);
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.resumeStateAfterDelay);
            Assert.IsFalse(shipment.sourceLotDebitApplied);
            Assert.IsFalse(shipment.loadApplied);
        }

        // ---------- QA-04: bounded physical-receiving wait ----------

        private LogisticsShipmentState CreateArrivedCustodyShipment(string id)
        {
            LogisticsShipmentState shipment = CreateLotCustodyShipment(id);
            shipment.sourceLotDebitApplied = true;
            shipment.loadApplied = true;
            shipment.remainingQuantityUnits = shipment.PlannedQuantityUnits;
            shipment.state = LogisticsShipmentStatus.Arrived;
            hooks.ReceiveCargoLot = (LogisticsShipmentState received, int quantity, out string reason) =>
            {
                reason = string.Empty;
                recipientStock[received.destinationBusinessInstanceId] = quantity;
                return true;
            };
            return shipment;
        }

        [Test]
        public void PhysicalReceivingGate_PinsThenDelays_FreesUnloader()
        {
            LogisticsShipmentState shipment = CreateArrivedCustodyShipment("gate-pinned");
            hooks.ConfirmPhysicalReceiving = (LogisticsShipmentState received, EntityId h,
                out bool arrived, out string reason) =>
            {
                arrived = false;
                reason = "no pathing director at destination";
                return false;
            };

            Assert.IsTrue(service.BeginUnloading(shipment, loader, hooks, out string beginReason), beginReason);
            EntityId unloadTaskId = OpenLoadTaskId("gate-pinned");

            // Pins 1 and 2: still Unloading, task still open, no receipt.
            for (int pin = 1; pin <= 2; pin++)
            {
                Assert.IsTrue(service.WorkUnloading(shipment, 60, hooks, out _), "pin " + pin);
                Assert.AreEqual(LogisticsShipmentStatus.Unloading, shipment.Status, "pin " + pin);
                Assert.AreEqual(TaskStatus.InProgress, tasks.FindTask(unloadTaskId).Status, "pin " + pin);
                Assert.IsFalse(shipment.deliveryApplied, "pin " + pin);
                Assert.IsFalse(recipientStock.ContainsKey("bakery-1"), "pin " + pin);
            }

            // Pin 3 (the bound): Delayed with the gate's reason, task failed,
            // unloader freed, cargo still in wagon custody.
            Assert.IsTrue(service.WorkUnloading(shipment, 60, hooks, out _), "pin 3");
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status);
            Assert.IsTrue(shipment.BlockedReason.Contains("no pathing director"), shipment.BlockedReason);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.resumeStateAfterDelay);
            Assert.AreEqual(TaskStatus.Failed, tasks.FindTask(unloadTaskId).Status, "the unload task must reach a terminal state");
            AssertLoaderCanStartUnrelatedWork("the unloader must be freed after the gate bound");
            Assert.AreEqual(10, shipment.RemainingQuantityUnits, "cargo stays in wagon custody");
            Assert.IsFalse(shipment.deliveryApplied);
            Assert.IsFalse(recipientStock.ContainsKey("bakery-1"));
        }

        [Test]
        public void PhysicalReceivingGate_PassesAfterTwoPins_CompletesExactlyOnce()
        {
            LogisticsShipmentState shipment = CreateArrivedCustodyShipment("gate-passes");
            int gateCalls = 0;
            hooks.ConfirmPhysicalReceiving = (LogisticsShipmentState received, EntityId h,
                out bool arrived, out string reason) =>
            {
                gateCalls++;
                arrived = gateCalls > 2;
                reason = arrived ? string.Empty : "Carrying: route in progress.";
                return true;
            };

            Assert.IsTrue(service.BeginUnloading(shipment, loader, hooks, out string beginReason), beginReason);
            Assert.IsTrue(service.WorkUnloading(shipment, 60, hooks, out _), "pin 1");
            Assert.AreEqual(LogisticsShipmentStatus.Unloading, shipment.Status);
            Assert.IsTrue(service.WorkUnloading(shipment, 60, hooks, out _), "pin 2");
            Assert.AreEqual(LogisticsShipmentStatus.Unloading, shipment.Status);
            Assert.IsTrue(service.WorkUnloading(shipment, 60, hooks, out _), "gate passes");
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status);
            Assert.AreEqual(10, recipientStock["bakery-1"]);
            Assert.AreEqual(10, shipment.DeliveredQuantityUnits);
            Assert.IsTrue(shipment.deliveryApplied);

            // A further unload attempt finds no open task and delivers nothing twice.
            Assert.IsFalse(service.WorkUnloading(shipment, 60, hooks, out _));
            Assert.AreEqual(10, recipientStock["bakery-1"], "no duplicated delivery");
        }

        // ---------- QA-02: the production tick resumes Delayed shipments ----------

        private FreightProductionDriver NewDriverWithCarrier(out BusinessRuntimeState carrierRuntime)
        {
            var driver = new FreightProductionDriver(service);
            carrierRuntime = new BusinessRuntimeState();
            carrierRuntime.BindBusinessIdentity("biz-freight-1");
            driver.EnsureCompany("biz-freight-1", carrierRuntime);
            var payerRuntime = new BusinessRuntimeState();
            payerRuntime.BindBusinessIdentity("biz-store-1");
            payerRuntime.SetCurrentCashCents(100000);
            driver.RegisterBusinessRuntime("biz-store-1", payerRuntime);
            return driver;
        }

        private LogisticsShipmentState CreateViaDriver(FreightProductionDriver driver, string id, string wagonId)
        {
            LogisticsShipmentState shipment = driver.CreateShipment(
                "biz-freight-1", id, "mill-1", "store-1", 50, GrainSpec(), wagonId, hooks,
                driverA, teamA, 500, "biz-store-1", new List<string>(), out string reason);
            Assert.IsNotNull(shipment, reason);
            return shipment;
        }

        private void TickUntil(FreightProductionDriver driver, Func<bool> done, List<string> diag)
        {
            int guard = 0;
            while (!done() && guard++ < 60)
                driver.AdvanceTick(3600f, hooks, handler, diag);
        }

        [Test]
        public void DelayedShipment_RetriedByProductionTick_CompletesExactlyOnce()
        {
            FreightProductionDriver driver = NewDriverWithCarrier(out BusinessRuntimeState carrierRuntime);
            var diag = new List<string>();
            recipientAccepts = false;
            LogisticsShipmentState shipment = CreateViaDriver(driver, "ship-1", "wagon-1");
            int cashBefore = carrierRuntime.CurrentCashCents;

            TickUntil(driver, () => shipment.Status == LogisticsShipmentStatus.Delayed, diag);
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status, string.Join("; ", diag));
            Assert.AreEqual(50, shipment.RemainingQuantityUnits, "refused cargo stays in wagon custody");
            Assert.IsFalse(shipment.deliveryApplied);
            Assert.IsFalse(recipientStock.ContainsKey("store-1"));
            Assert.AreEqual(cashBefore, carrierRuntime.CurrentCashCents, "no revenue for unaccepted cargo");

            // The recipient relents. The production tick itself resumes the
            // shipment: no manual ResumeShipment call is made in this test.
            recipientAccepts = true;
            TickUntil(driver, () => shipment.Status == LogisticsShipmentStatus.Completed, diag);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status, string.Join("; ", diag));
            Assert.AreEqual(50, recipientStock["store-1"], "the same cargo completes exactly once");
            Assert.AreEqual(50, shipment.DeliveredQuantityUnits);
            Assert.AreEqual(0, shipment.RemainingQuantityUnits);
            Assert.AreEqual(cashBefore + 500, carrierRuntime.CurrentCashCents, "revenue settles only after the tick retry is accepted");

            TickUntil(driver, () => false, diag);
            Assert.AreEqual(cashBefore + 500, carrierRuntime.CurrentCashCents, "settlement is exactly-once");
            Assert.AreEqual(50, recipientStock["store-1"], "no duplicated delivery on later ticks");
        }

        [Test]
        public void PermanentlyRefusingRecipient_CyclesDelayed_CargoConserved_NoRevenue()
        {
            FreightProductionDriver driver = NewDriverWithCarrier(out BusinessRuntimeState carrierRuntime);
            var diag = new List<string>();
            recipientAccepts = false;
            LogisticsShipmentState shipment = CreateViaDriver(driver, "ship-1", "wagon-1");
            int cashBefore = carrierRuntime.CurrentCashCents;

            TickUntil(driver, () => shipment.Status == LogisticsShipmentStatus.Delayed, diag);
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status, string.Join("; ", diag));

            // Twelve more production ticks: the shipment cycles through the
            // retry (Delayed -> Arrived -> Unloading -> refused -> Delayed)
            // and never completes, never settles, never loses cargo.
            int delayedVisits = 0;
            for (int i = 0; i < 12; i++)
            {
                driver.AdvanceTick(3600f, hooks, handler, diag);
                if (shipment.Status == LogisticsShipmentStatus.Delayed) delayedVisits++;
                Assert.IsFalse(shipment.deliveryApplied, "tick " + i);
                Assert.AreEqual(50, shipment.RemainingQuantityUnits, "tick " + i);
                Assert.AreEqual(0, shipment.DeliveredQuantityUnits, "tick " + i);
                Assert.IsFalse(recipientStock.ContainsKey("store-1"), "tick " + i);
                Assert.AreEqual(cashBefore, carrierRuntime.CurrentCashCents, "tick " + i);
                Assert.IsTrue(service.VerifyConservation(shipment, 0, out string cons), "tick " + i + ": " + cons);
            }
            Assert.Greater(delayedVisits, 1, "the shipment must return to Delayed repeatedly, not stall in one state: " + string.Join("; ", diag));
        }

        // ---------- QA-03 (partial): the stalled hold is named ----------

        [Test]
        public void StalledHoldDiagnostic_NamesDelayedShipmentWithUndrivableResume()
        {
            var authority = new EquipmentReservationService();
            hooks.Reservations = authority;
            hooks.NowGameSeconds = 1000f;
            FreightProductionDriver driver = NewDriverWithCarrier(out _);
            var diag = new List<string>();
            LogisticsShipmentState shipment = CreateViaDriver(driver, "ship-1", "wagon-1");

            TickUntil(driver, () => shipment.Status == LogisticsShipmentStatus.InTransit, diag);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status, string.Join("; ", diag));
            Assert.IsTrue(authority.IsReserved(ReservationKeyConventions.Asset("wagon-1"), 1000f),
                "dispatch reserved the wagon through the authority");

            // Force the stall with an explicitly undrivable resume state
            // (Unloading, the MarkDelayed default): the tick cannot drive
            // from Unloading without an open unload task, so it stays
            // Delayed for this tick. Note DelayShipment's own default
            // resume is InTransit, which IS drivable, so it is not used
            // here.
            service.DelayShipment(shipment, "test stall", hooks, LogisticsShipmentStatus.Unloading);
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status);

            diag.Clear();
            driver.AdvanceTick(3600f, hooks, handler, diag);
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status, "an undrivable resume state waits, it is not forced");
            string joined = string.Join("; ", diag);
            Assert.IsTrue(joined.Contains("ship-1") && joined.Contains("still holds its dispatch reservation"), joined);
            Assert.IsTrue(authority.IsReserved(ReservationKeyConventions.Asset("wagon-1"), 1000f),
                "the hold is still held (this slice only names it; release on completion is unchanged)");
        }

        // ---------- LRM lot guard (Agent 3 finding 1) ----------
        //
        // The guard itself (LogisticsRuntimeManager.TryApplyDelivery refuses
        // any shipment with requiresSourceLotCustody) has NO Roslyn-harness
        // test in this slice: LogisticsRuntimeManager.cs is not in the
        // harness closure at this baseline (it pulls FirstLedger,
        // Orchestration.Systems and World.Journeys), and
        // LogisticsConservationGateTests.cs is explicitly excluded from
        // sources.txt, so the type is not compiled by the harness at all.
        // The guard is source-verified only; Unity Test Runner verification
        // is owed. The guard sits at the top of TryApplyDelivery, before
        // any credit branch, and fires only when requiresSourceLotCustody
        // is set, which no LRM factory sets today.
    }
}
