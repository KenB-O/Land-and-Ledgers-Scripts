// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Lane 9d tests (M-R11): freight loading/unloading requires arrival.
//
// Pins the pre-work arrival gates on FreightShipmentService: no handling
// minutes accrue before the loader/handler reaches the loading/unloading
// point; pinned ticks accrue nothing; at the pin bound the handling task
// closes as failed and the shipment Delays with the cargo preserved. When
// no gate is wired the abstract behaviour is kept and recorded once per
// shipment in Diagnostics — a known absence of a check, not a silent
// pass. The post-work ConfirmPhysicalReceiving gate is untouched by
// these tests except where noted: it stays post-work, so a handler still
// carrying can finish the task minutes and then wait for receipt.
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Logistics;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;
using LogisticsShipmentState = global::LandLedgers.Economy.LogisticsShipmentState;
using LogisticsShipmentStatus = global::LandLedgers.Economy.LogisticsShipmentStatus;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class Lane9dFreightHandlingArrivalTests
    {
        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private FreightShipmentService service;
        private FreightShipmentHooks hooks;
        private EntityId loader;
        private Dictionary<string, int> recipientStock;

        [SetUp]
        public void SetUp()
        {
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(42);
            service = new FreightShipmentService();
            recipientStock = new Dictionary<string, int>(System.StringComparer.Ordinal);
            hooks = new FreightShipmentHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                DayIndex = 42,
                Diagnostics = new List<string>(),
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    if (quantityUnits <= 0) return 0;
                    string key = string.IsNullOrWhiteSpace(shipment.destinationBusinessInstanceId)
                        ? "nowhere" : shipment.destinationBusinessInstanceId;
                    recipientStock[key] = recipientStock.TryGetValue(key, out int have) ? have + quantityUnits : quantityUnits;
                    return quantityUnits;
                },
            };
            loader = EntityId.For(EntityKind.Person, 701);

            var wagon = new EquipmentAsset
            {
                AssetId = "wagon-1",
                Kind = "farm-wagon",
                DisplayName = "Farm wagon",
                Condition01 = 0.9f,
                PayloadMassCapacityKg = 1000f,
                CargoVolumeCapacityM3 = 4f,
            };
            service.RegisterWagon(wagon);
        }

        private static FreightCargoSpec GrainSpec() =>
            new FreightCargoSpec { MassKgPerUnit = 2f, VolumeM3PerUnit = 0.01f };

        private LogisticsShipmentState CreatePlannedShipment(string shipmentId)
        {
            LogisticsShipmentState shipment = service.CreateShipment(shipmentId, "mill-1", "bakery-1",
                10, GrainSpec(), "wagon-1", hooks, out string createReason);
            Assert.IsNotNull(shipment, createReason);
            return shipment;
        }

        private LogisticsShipmentState CreateArrivedShipment(string shipmentId)
        {
            LogisticsShipmentState shipment = CreatePlannedShipment(shipmentId);
            Assert.IsTrue(service.TryBindSourceLot(shipment, "flour-lot-1", "flour", out string bindReason),
                bindReason);
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

        private static ConfirmFreightPhysicalHandlingHandler Gate(bool arrived, string reason = null)
        {
            return (LogisticsShipmentState shipment, EntityId handlerId,
                out bool physicallyArrived, out string gateReason) =>
            {
                physicallyArrived = arrived;
                gateReason = reason ?? (arrived ? string.Empty : "Handler has not reached the handling point.");
                return true;
            };
        }

        private static int CountDiagnosticsContaining(List<string> diagnostics, string fragment)
        {
            int count = 0;
            foreach (string line in diagnostics)
                if (line != null && line.Contains(fragment)) count++;
            return count;
        }

        [Test]
        public void LoadGate_BlocksMinutesUntilArrival_ThenCompletes()
        {
            LogisticsShipmentState shipment = CreatePlannedShipment("load-gate");
            bool arrived = false;
            hooks.ConfirmPhysicalLoading = (LogisticsShipmentState s, EntityId handlerId,
                out bool physicallyArrived, out string gateReason) =>
            {
                physicallyArrived = arrived;
                gateReason = arrived ? string.Empty : "Loader has not reached the source loading premises.";
                return true;
            };

            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out string beginReason), beginReason);
            Assert.IsTrue(service.WorkLoading(shipment, 60, hooks, out string firstReason), firstReason);
            Assert.AreEqual(LogisticsShipmentStatus.Loading, shipment.Status);
            Assert.IsFalse(shipment.loadApplied, "No load minutes accrue before arrival.");
            StringAssert.Contains("not yet confirmed", string.Join(" ", hooks.Diagnostics));

            arrived = true;
            Assert.IsTrue(service.WorkLoading(shipment, 60, hooks, out string secondReason), secondReason);
            Assert.IsTrue(shipment.loadApplied, "Arrival lets the load complete.");
            // Load completion sets loadApplied, not a new status: the
            // shipment stays Loading until Dispatch moves it InTransit.
            Assert.AreEqual(LogisticsShipmentStatus.Loading, shipment.Status);
            Assert.AreEqual("wagon:wagon-1", shipment.physicalLocationId);
        }

        [Test]
        public void LoadGate_PinBoundFailsTaskAndDelaysShipmentWithCargoPreserved()
        {
            LogisticsShipmentState shipment = CreatePlannedShipment("load-gate-bound");
            hooks.ConfirmPhysicalLoading = Gate(false);

            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out string beginReason), beginReason);
            // Pinned ticks return true (the tick was consumed by waiting)
            // and accrue nothing; the bound tick closes the task as failed
            // and returns false, exactly like the debit-refusal path.
            Assert.IsTrue(service.WorkLoading(shipment, 60, hooks, out string firstReason), firstReason);
            Assert.IsFalse(shipment.loadApplied, "Pinned ticks accrue nothing.");
            Assert.AreEqual(LogisticsShipmentStatus.Loading, shipment.Status);
            Assert.IsTrue(service.WorkLoading(shipment, 60, hooks, out string secondReason), secondReason);
            Assert.IsFalse(shipment.loadApplied);
            Assert.IsFalse(service.WorkLoading(shipment, 60, hooks, out string boundReason), boundReason);
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status);
            StringAssert.Contains("pinned for 3 ticks", shipment.BlockedReason);
            Assert.IsFalse(shipment.loadApplied);
            Assert.AreEqual(0, tasks.GetOpenTasks(EntityId.For(EntityKind.Business, 3)).Count,
                "The failed load task is closed and frees the loader.");
        }

        [Test]
        public void UnloadGate_BlocksMinutesUntilArrival_ThenCompletes()
        {
            LogisticsShipmentState shipment = CreateArrivedShipment("unload-gate");
            bool arrived = false;
            hooks.ConfirmPhysicalUnloadingArrival = (LogisticsShipmentState s, EntityId handlerId,
                out bool physicallyArrived, out string gateReason) =>
            {
                physicallyArrived = arrived;
                gateReason = arrived ? string.Empty : "Handler has not reached the destination premises.";
                return true;
            };

            Assert.IsTrue(service.BeginUnloading(shipment, loader, hooks, out string beginReason), beginReason);
            Assert.IsTrue(service.WorkUnloading(shipment, 60, hooks, out string firstReason), firstReason);
            Assert.AreEqual(LogisticsShipmentStatus.Unloading, shipment.Status);
            Assert.IsFalse(shipment.deliveryApplied, "No unload minutes accrue before arrival.");
            Assert.IsFalse(recipientStock.ContainsKey("bakery-1"), "No stock receipt before arrival.");

            arrived = true;
            Assert.IsTrue(service.WorkUnloading(shipment, 60, hooks, out string secondReason), secondReason);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status);
            Assert.AreEqual(10, recipientStock["bakery-1"]);
            Assert.IsTrue(shipment.deliveryApplied);
        }

        [Test]
        public void UnloadGate_PinBoundFailsTaskAndDelaysShipmentWithCargoInWagon()
        {
            LogisticsShipmentState shipment = CreateArrivedShipment("unload-gate-bound");
            hooks.ConfirmPhysicalUnloadingArrival = Gate(false);

            Assert.IsTrue(service.BeginUnloading(shipment, loader, hooks, out string beginReason), beginReason);
            for (int tick = 0; tick < FreightShipmentService.PhysicalReceivingPinLimit; tick++)
            {
                Assert.IsTrue(service.WorkUnloading(shipment, 60, hooks, out string reason), reason);
                Assert.IsFalse(shipment.deliveryApplied, "Pinned ticks accrue nothing.");
            }
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.resumeStateAfterDelay);
            StringAssert.Contains("pinned for 3 ticks", shipment.BlockedReason);
            Assert.AreEqual(10, shipment.remainingQuantityUnits, "The cargo stays in wagon custody.");
            Assert.IsFalse(recipientStock.ContainsKey("bakery-1"));
        }

        [Test]
        public void UnwiredGate_KeepsAbstractBehaviourAndNotesItOncePerShipment()
        {
            LogisticsShipmentState shipment = CreatePlannedShipment("unwired-gate");
            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out string beginReason), beginReason);
            // Two sub-completion ticks: the 5-minute task still has minutes
            // left after each, so both ticks pass through the unwired gate
            // — but the missing-check note is recorded only once.
            Assert.IsTrue(service.WorkLoading(shipment, 1, hooks, out string firstReason), firstReason);
            Assert.IsFalse(shipment.loadApplied);
            Assert.IsTrue(service.WorkLoading(shipment, 1, hooks, out string secondReason), secondReason);
            Assert.IsFalse(shipment.loadApplied);
            Assert.AreEqual(1, CountDiagnosticsContaining(hooks.Diagnostics,
                "loading proceeds without a physical arrival check"),
                "The missing check is recorded once per shipment, not once per tick.");
            Assert.IsTrue(service.WorkLoading(shipment, 60, hooks, out string thirdReason), thirdReason);
            Assert.IsTrue(shipment.loadApplied,
                "No gate wired: the abstract behaviour is kept and the load completes.");
            Assert.AreEqual(1, CountDiagnosticsContaining(hooks.Diagnostics,
                "loading proceeds without a physical arrival check"));
        }

        [Test]
        public void ReceivingGate_StaysPostWork_WhenUnloadGatePasses()
        {
            // The existing post-work receipt contract is unchanged: the
            // unload task accrues while the handler is still carrying, and
            // only the receipt waits for physical arrival.
            LogisticsShipmentState shipment = CreateArrivedShipment("receiving-still-postwork");
            hooks.ConfirmPhysicalUnloadingArrival = Gate(true);
            bool physicallyAtReceivingPoint = false;
            hooks.ConfirmPhysicalReceiving = (LogisticsShipmentState received, EntityId handler,
                out bool arrived, out string reason) =>
            {
                arrived = physicallyAtReceivingPoint;
                reason = arrived ? string.Empty : "Carrying: route in progress.";
                return true;
            };

            Assert.IsTrue(service.BeginUnloading(shipment, loader, hooks, out string beginReason), beginReason);
            Assert.IsTrue(service.WorkUnloading(shipment, 60, hooks, out string firstReason), firstReason);
            Assert.AreEqual(LogisticsShipmentStatus.Unloading, shipment.Status);
            Assert.IsFalse(shipment.deliveryApplied, "The post-work receipt gate still holds the delivery.");

            physicallyAtReceivingPoint = true;
            Assert.IsTrue(service.WorkUnloading(shipment, 1, hooks, out string secondReason), secondReason);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status);
            Assert.AreEqual(10, recipientStock["bakery-1"]);
        }
    }
}
