// DEF-070 (Decision 13): terminal disposition for a failed shipment.
// A Delayed state is only honest when a driver will resume the shipment;
// construction shipments have no driver (the detailed driver only iterates
// company lists), so a failed chain must end here instead of staying live
// forever holding its wagon, its team, and its cargo. A never-loaded
// shipment owns zero cargo (MR-P001); a loaded shipment keeps its remaining
// units on the record with an explicit stranded-cargo note (PASS7-008
// quarantine doctrine) — recorded, visible, never silently dropped and never
// silently returned to the supplier.
// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.

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
    public sealed class FreightFailedShipmentDef070Tests
    {
        private const int Day = 42;

        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private FreightShipmentService service;
        private FreightShipmentHooks hooks;
        private EntityId loader;
        private EntityId driverA;
        private List<EntityId> teamA;

        [SetUp]
        public void SetUp()
        {
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(Day);
            service = new FreightShipmentService();
            hooks = new FreightShipmentHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                DayIndex = Day,
                Diagnostics = new List<string>(),
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    return quantityUnits;
                },
            };
            loader = EntityId.For(EntityKind.Person, 701);
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

        private LogisticsShipmentState CreateShipment(string id, string wagonId)
        {
            LogisticsShipmentState shipment = service.CreateShipment(id, "mill-1", "bakery-1",
                10, GrainSpec(), wagonId, hooks, out string createReason);
            Assert.IsNotNull(shipment, createReason);
            return shipment;
        }

        private void LoadShipment(LogisticsShipmentState shipment)
        {
            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out string beginReason), beginReason);
            int guard = 0;
            while (!shipment.loadApplied && guard++ < 100)
                Assert.IsTrue(service.WorkLoading(shipment, 60, hooks, out string workReason), workReason);
            Assert.IsTrue(shipment.loadApplied, "loading must apply");
        }

        private void DispatchAndArrive(LogisticsShipmentState shipment)
        {
            Assert.IsTrue(service.Dispatch(shipment, driverA, teamA, hooks, out string dispatchReason), dispatchReason);
            service.AdvanceTransit(shipment, 100000f, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.Status, "the haul must arrive");
        }

        [Test]
        public void NeverLoadedShipment_FailsTerminally_OwnsZeroCargo_FreesWagonAndLoader()
        {
            LogisticsShipmentState shipment = CreateShipment("fail-1", "wagon-1");
            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out string beginReason), beginReason);
            Assert.AreEqual(1, tasks.GetOpenTasks(EntityId.For(EntityKind.Business, 3)).Count,
                "the open load task is the probe: the loader is bound before disposition");

            service.FailAbandonedShipment(shipment, "loading refused by the driver", hooks);

            Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status);
            Assert.IsTrue(!shipment.loadApplied, "nothing was ever loaded");
            Assert.AreEqual(0, shipment.RemainingQuantityUnits,
                "a never-loaded shipment owns zero cargo (MR-P001)");
            Assert.AreEqual(0, tasks.GetOpenTasks(EntityId.For(EntityKind.Business, 3)).Count,
                "the open load task must close — the loader is freed");
            Assert.IsNull(service.FindExclusivityConflict(null, "wagon-1", EntityId.Invalid, null),
                "the wagon must be free for other shipments");
            Assert.IsTrue(!FreightShipmentService.IsLiveShipment(shipment),
                "a Failed shipment is not live");
        }

        [Test]
        public void LoadedShipment_FailsTerminally_CargoQuarantinedOnRecord_TeamFreed()
        {
            LogisticsShipmentState shipment = CreateShipment("fail-2", "wagon-1");
            LoadShipment(shipment);
            DispatchAndArrive(shipment);

            service.FailAbandonedShipment(shipment, "unloading refused at the site", hooks);

            Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status);
            Assert.IsTrue(shipment.loadApplied, "the load was applied before the failure");
            Assert.AreEqual(10, shipment.RemainingQuantityUnits,
                "loaded cargo stays on the record (PASS7-008) — never zeroed, never silently returned");
            Assert.IsTrue(shipment.BlockedReason.Contains("STRANDED CARGO"),
                "the failure names the stranded cargo, but got '" + shipment.BlockedReason + "'");
            Assert.IsTrue(shipment.BlockedReason.Contains("10u"),
                "the note names the stranded units, but got '" + shipment.BlockedReason + "'");
            Assert.IsTrue(!shipment.deliveryApplied, "nothing was delivered");
            Assert.IsTrue(!shipment.physicalTransportBound, "the transport binding must clear");
            Assert.IsNull(service.FindExclusivityConflict(null, "wagon-1", driverA, teamA),
                "wagon, driver, and animals must all be free after disposition");
        }

        [Test]
        public void AlreadyFailedShipment_DispositionIsIdempotent()
        {
            LogisticsShipmentState shipment = CreateShipment("fail-3", "wagon-1");
            LoadShipment(shipment);
            service.FailAbandonedShipment(shipment, "first abandonment", hooks);
            string blockedReason = shipment.BlockedReason;
            int remaining = shipment.RemainingQuantityUnits;

            service.FailAbandonedShipment(shipment, "second abandonment", hooks);

            Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status);
            Assert.AreEqual(remaining, shipment.RemainingQuantityUnits, "the cargo record must not change");
            Assert.AreEqual(blockedReason, shipment.BlockedReason,
                "the second call must not overwrite the first disposition");
        }

        [Test]
        public void FailedShipment_ReleasesItsSourceLotClaim()
        {
            LogisticsShipmentState first = CreateShipment("fail-4", "wagon-1");
            Assert.IsTrue(service.TryBindSourceLot(first, "flour-lot-1", "flour", out string bindReason), bindReason);
            hooks.CommitSourceLot = (LogisticsShipmentState s, int quantity, out string reason) =>
            {
                reason = string.Empty;
                return true;
            };
            LoadShipment(first);
            service.FailAbandonedShipment(first, "dispatch refused", hooks);

            LogisticsShipmentState second = CreateShipment("fail-5", "wagon-2");
            Assert.IsTrue(service.TryBindSourceLot(second, "flour-lot-1", "flour", out string rebindReason),
                "a Failed shipment must release its lot claim, but got '" + rebindReason + "'");
            Assert.AreEqual("flour-lot-1", second.CargoLotId);
        }

        [Test]
        public void PreLoadBlockedShipment_DispositionIsNoOp()
        {
            // Over capacity at creation: BlockPreLoad already failed it with
            // zero cargo owned. Disposition must leave that record exactly
            // as it is.
            LogisticsShipmentState shipment = service.CreateShipment("fail-6", "mill-1", "bakery-1",
                10000, GrainSpec(), "wagon-1", hooks, out string createReason);
            Assert.IsNotNull(shipment, createReason);
            Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status,
                "over capacity must block pre-load");
            Assert.AreEqual(0, shipment.RemainingQuantityUnits, "a blocked shipment owns zero cargo");

            service.FailAbandonedShipment(shipment, "second disposition attempt", hooks);

            Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status);
            Assert.AreEqual(0, shipment.RemainingQuantityUnits);
            Assert.IsTrue(!shipment.loadApplied);
        }
    }
}
