// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Slice A2 (2026-11-13) — QA-05 reserve-first schedule windows.
//
// BeginLoading/BeginUnloading reserve the handler's calendar window
// before creating the handling task; a refused window means no task, no
// budget commitment, and no cargo moved. The window releases exactly when
// the task terminalizes, and stays held while the task is open (the
// physical-receiving gate pins keep it held; the pin bound releases it).
// When no calendar is wired the previous behaviour is kept and one
// diagnostic names the missing calendar.

using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Transport;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class FreightScheduleReservationTests
    {
        private const int Day = 42;
        private const int MinuteOfDay = 480;

        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private PersonScheduleTracker schedules;
        private FreightShipmentService service;
        private FreightShipmentHooks hooks;
        private EntityId loader;
        private Dictionary<string, int> recipientStock;

        [SetUp]
        public void SetUp()
        {
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(Day);
            schedules = new PersonScheduleTracker();
            service = new FreightShipmentService();
            recipientStock = new Dictionary<string, int>(StringComparer.Ordinal);
            hooks = new FreightShipmentHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                DayIndex = Day,
                Diagnostics = new List<string>(),
                Schedules = schedules,
                MinuteOfDay = MinuteOfDay,
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    string key = string.IsNullOrWhiteSpace(shipment.destinationBusinessInstanceId)
                        ? "nowhere" : shipment.destinationBusinessInstanceId;
                    recipientStock[key] = recipientStock.TryGetValue(key, out int have) ? have + quantityUnits : quantityUnits;
                    return quantityUnits;
                },
                CommitSourceLot = (LogisticsShipmentState s, int quantity, out string reason) =>
                {
                    reason = string.Empty;
                    return true;
                },
            };
            loader = EntityId.For(EntityKind.Person, 701);
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

        private LogisticsShipmentState CreateLotCustodyShipment(string id, string wagonId = "wagon-1", string lotId = "flour-lot-1")
        {
            LogisticsShipmentState shipment = service.CreateShipment(id, "mill-1", "bakery-1",
                10, GrainSpec(), wagonId, hooks, out string createReason);
            Assert.IsNotNull(shipment, createReason);
            Assert.IsTrue(service.TryBindSourceLot(shipment, lotId, "flour", out string bindReason), bindReason);
            return shipment;
        }

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

        private EntityId OpenLoadTaskId(string shipmentId)
        {
            List<WorkTask> open = tasks.GetOpenTasks(EntityId.For(EntityKind.Business, 3));
            Assert.AreEqual(1, open.Count, "exactly one open freight task is expected");
            return open[0].TaskId;
        }

        private bool HasWindow(EntityId person, string label)
        {
            foreach (PersonActivityReservation reservation in schedules.ActiveFor(person.Id, Day))
            {
                if (reservation.Label == label) return true;
            }
            return false;
        }

        private PersonActivityReservation ReservedWindow(EntityId person, string label)
        {
            foreach (PersonActivityReservation reservation in schedules.ActiveFor(person.Id, Day))
                if (reservation.Label == label) return reservation;
            return null;
        }

        private void AssertLoaderCanStartUnrelatedWork(string context)
        {
            tasks.RegisterDefinition(new TaskDefinition("other-work", "Unrelated legitimate job", 30), out _);
            WorkTask other = tasks.CreateTask("other-work", EntityId.For(EntityKind.Business, 3), Day);
            Assert.IsTrue(tasks.AssignTask(other.TaskId, loader, budgets, out string assignReason), context + ": " + assignReason);
            Assert.IsTrue(tasks.StartTask(other.TaskId, Day, out string startReason), context + ": " + startReason);
        }

        // ---------- Load: refusal before any task exists ----------

        [Test]
        public void LoadFindsLaterWindowWhenShoppingWindowOverlaps()
        {
            Assert.IsNull(schedules.TryReserve(loader.Id, PersonActivityKind.Shopping, Day, 480, 60, "bread errand"),
                "the shopping window itself must reserve cleanly");

            LogisticsShipmentState shipment = CreateLotCustodyShipment("load-overlap");
            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out string reason), reason);

            Assert.AreEqual(540, ReservedWindow(loader, "freight:load:load-overlap").StartMinuteOfDay);
            Assert.AreEqual(1, tasks.GetOpenTasks(EntityId.For(EntityKind.Business, 3)).Count);
            Assert.AreEqual(5, budgets.GetOrCreate(loader).MinutesCommitted);
            Assert.IsFalse(shipment.loadApplied, "no cargo may move");
            Assert.AreEqual(0, shipment.RemainingQuantityUnits);
            Assert.AreEqual(LogisticsShipmentStatus.Loading, shipment.Status);
            Assert.IsTrue(HasWindow(loader, "bread errand"), "the existing shopping reservation is preserved");
        }

        [Test]
        public void ShoppingRefusedOverLoadWindow()
        {
            LogisticsShipmentState shipment = CreateLotCustodyShipment("load-first");
            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out string beginReason), beginReason);
            Assert.IsTrue(HasWindow(loader, "freight:load:load-first"), "the load window must be held");

            string shoppingRefusal = schedules.TryReserve(loader.Id, PersonActivityKind.Shopping, Day, 480, 30, "shopping");
            Assert.IsNotNull(shoppingRefusal, "a shopping plan over the load minutes must refuse: " + shoppingRefusal);
        }

        // ---------- Load: release on completion ----------

        [Test]
        public void LoadCompletionReleasesWindow()
        {
            LogisticsShipmentState shipment = CreateLotCustodyShipment("load-complete");
            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out string beginReason), beginReason);
            Assert.IsTrue(HasWindow(loader, "freight:load:load-complete"));

            Assert.IsTrue(service.WorkLoading(shipment, 60, hooks, out string workReason), workReason);
            Assert.IsTrue(shipment.loadApplied, "the load itself must complete");

            Assert.IsFalse(HasWindow(loader, "freight:load:load-complete"), "the window must release when the task completes");
            Assert.IsNull(schedules.TryReserve(loader.Id, PersonActivityKind.Work, Day, 480, 5, "rebook"),
                "the same minutes must be bookable again after release");
        }

        // ---------- Load: release on cancel after a StartTask refusal ----------

        [Test]
        public void StartRefusalReleasesWindow()
        {
            AssertLoaderCanStartUnrelatedWork("setup: the loader is occupied by other work");

            LogisticsShipmentState shipment = CreateLotCustodyShipment("load-start-refused");
            Assert.IsFalse(service.BeginLoading(shipment, loader, hooks, out string beginReason), "the load must refuse: " + beginReason);

            List<WorkTask> open = tasks.GetOpenTasks(EntityId.For(EntityKind.Business, 3));
            Assert.AreEqual(1, open.Count, "only the unrelated task may remain; the minted load task must be cancelled");
            Assert.AreEqual("other-work", open[0].DefinitionId);
            Assert.IsFalse(HasWindow(loader, "freight:load:load-start-refused"), "the window must release when the start is refused");
            Assert.IsNull(schedules.TryReserve(loader.Id, PersonActivityKind.Work, Day, 480, 5, "rebook"),
                "the minutes must be bookable again after the cancelled start");
        }

        // ---------- Unload equivalents ----------

        [Test]
        public void UnloadFindsLaterWindowWhenPreferredWindowOverlaps()
        {
            Assert.IsNull(schedules.TryReserve(loader.Id, PersonActivityKind.Shopping, Day, 480, 60, "bread errand"));

            LogisticsShipmentState shipment = CreateArrivedCustodyShipment("unload-overlap");
            Assert.IsTrue(service.BeginUnloading(shipment, loader, hooks, out string reason), reason);

            Assert.AreEqual(540, ReservedWindow(loader, "freight:unload:unload-overlap").StartMinuteOfDay,
                "the freight work uses the next free same-day time slot");
            Assert.AreEqual(1, tasks.GetOpenTasks(EntityId.For(EntityKind.Business, 3)).Count);
            Assert.AreEqual(5, budgets.GetOrCreate(loader).MinutesCommitted);
            Assert.AreEqual(10, shipment.RemainingQuantityUnits, "cargo stays in wagon custody");
            Assert.IsFalse(shipment.deliveryApplied);
            Assert.AreEqual(LogisticsShipmentStatus.Unloading, shipment.Status);
            Assert.IsTrue(HasWindow(loader, "bread errand"), "the existing shopping reservation is preserved");
        }

        [Test]
        public void UnloadStillRefusesWhenNoSameDayWindowExists()
        {
            Assert.IsNull(schedules.TryReserve(loader.Id, PersonActivityKind.Work, Day,
                480, 960, "full-day-work"));

            LogisticsShipmentState shipment = CreateArrivedCustodyShipment("unload-no-window");
            Assert.IsFalse(service.BeginUnloading(shipment, loader, hooks, out string reason));
            StringAssert.Contains("no available work window remains today", reason);
            Assert.AreEqual(0, tasks.GetOpenTasks(EntityId.For(EntityKind.Business, 3)).Count);
            Assert.AreEqual(0, budgets.GetOrCreate(loader).MinutesCommitted);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.Status);
            Assert.AreEqual(10, shipment.RemainingQuantityUnits);
        }

        [Test]
        public void UnloadCompletionReleasesWindow()
        {
            LogisticsShipmentState shipment = CreateArrivedCustodyShipment("unload-complete");
            Assert.IsTrue(service.BeginUnloading(shipment, loader, hooks, out string beginReason), beginReason);
            Assert.IsTrue(HasWindow(loader, "freight:unload:unload-complete"));

            Assert.IsTrue(service.WorkUnloading(shipment, 60, hooks, out string workReason), workReason);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status);
            Assert.AreEqual(10, recipientStock["bakery-1"], "the unload itself must complete");

            Assert.IsFalse(HasWindow(loader, "freight:unload:unload-complete"), "the window must release when the task completes");
            Assert.IsNull(schedules.TryReserve(loader.Id, PersonActivityKind.Work, Day, 480, 5, "rebook"),
                "the same minutes must be bookable again after release");
        }

        // ---------- The gate pins keep the window; the bound releases it ----------

        [Test]
        public void GatePinKeepsReservation_BoundReleasesIt()
        {
            LogisticsShipmentState shipment = CreateArrivedCustodyShipment("gate-pins");
            hooks.ConfirmPhysicalReceiving = (LogisticsShipmentState received, EntityId h,
                out bool arrived, out string reason) =>
            {
                arrived = false;
                reason = "no pathing director at destination";
                return false;
            };

            Assert.IsTrue(service.BeginUnloading(shipment, loader, hooks, out string beginReason), beginReason);
            EntityId unloadTaskId = OpenLoadTaskId("gate-pins");

            // Pins 1 and 2: the task is still open, so the window stays held.
            for (int pin = 1; pin <= 2; pin++)
            {
                Assert.IsTrue(service.WorkUnloading(shipment, 60, hooks, out _), "pin " + pin);
                Assert.AreEqual(TaskStatus.InProgress, tasks.FindTask(unloadTaskId).Status, "pin " + pin);
                Assert.IsTrue(HasWindow(loader, "freight:unload:gate-pins"), "pin " + pin + ": the window must stay held while the task is open");
                Assert.IsNotNull(schedules.TryReserve(loader.Id, PersonActivityKind.Work, Day, 480, 5, "competing"),
                    "pin " + pin + ": the held minutes must refuse a competing booking");
            }

            // Pin 3 (the bound): the task fails, the window releases.
            Assert.IsTrue(service.WorkUnloading(shipment, 60, hooks, out _), "pin 3");
            Assert.AreEqual(TaskStatus.Failed, tasks.FindTask(unloadTaskId).Status, "the unload task must reach a terminal state");
            Assert.IsFalse(HasWindow(loader, "freight:unload:gate-pins"), "the window must release when the task terminalizes");
            Assert.IsNull(schedules.TryReserve(loader.Id, PersonActivityKind.Work, Day, 480, 5, "rebook"),
                "the minutes must be bookable again after the bound");
        }

        // ---------- No calendar wired: previous behaviour plus a diagnostic ----------

        [Test]
        public void NullSchedules_KeepsBehaviourAndDiagnoses()
        {
            hooks.Schedules = null;

            LogisticsShipmentState shipment = CreateLotCustodyShipment("no-calendar");
            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out string beginReason), beginReason);
            Assert.IsTrue(service.WorkLoading(shipment, 60, hooks, out string workReason), workReason);
            Assert.IsTrue(shipment.loadApplied, "the load itself must still complete without a calendar");

            bool diagnosed = false;
            foreach (string line in hooks.Diagnostics)
            {
                if (line.Contains("no PersonScheduleTracker wired")) diagnosed = true;
            }
            Assert.IsTrue(diagnosed, "the missing calendar must be named in diagnostics: " + string.Join("; ", hooks.Diagnostics));
        }

        // ---------- The caller-side flag skips the reserve and the diagnostic ----------

        [Test]
        public void CrewWindowsReservedByCaller_SkipsReserveAndDiagnostic()
        {
            // Case 1: flag true with a calendar wired (the CMF shape) — the
            // load proceeds, but no sub-window is reserved inside the
            // caller's whole-day crew windows, and nothing is diagnosed.
            hooks.CrewWindowsReservedByCaller = true;
            LogisticsShipmentState wired = CreateLotCustodyShipment("flag-wired");
            Assert.IsTrue(service.BeginLoading(wired, loader, hooks, out string wiredReason), wiredReason);
            Assert.IsFalse(HasWindow(loader, "freight:load:flag-wired"), "the flag must skip the reserve even with a calendar wired");
            Assert.IsTrue(service.WorkLoading(wired, 60, hooks, out string wiredWorkReason), wiredWorkReason);
            Assert.IsTrue(wired.loadApplied, "the load itself must still complete");

            // Case 2: flag true with NO calendar wired — the load proceeds
            // and the missing-calendar diagnostic is skipped, because the
            // caller holds the crew windows itself. A second wagon and lot:
            // the first shipment still holds wagon-1 and flour-lot-1.
            hooks.Schedules = null;
            LogisticsShipmentState unwired = CreateLotCustodyShipment("flag-null", "wagon-2", "flour-lot-2");
            Assert.IsTrue(service.BeginLoading(unwired, loader, hooks, out string unwiredReason), unwiredReason);
            Assert.IsTrue(service.WorkLoading(unwired, 60, hooks, out string unwiredWorkReason), unwiredWorkReason);
            Assert.IsTrue(unwired.loadApplied, "the load itself must still complete");
            foreach (string line in hooks.Diagnostics)
            {
                Assert.IsFalse(line.Contains("no PersonScheduleTracker wired"),
                    "the flag must suppress the missing-calendar diagnostic: " + line);
            }
        }
    }
}
