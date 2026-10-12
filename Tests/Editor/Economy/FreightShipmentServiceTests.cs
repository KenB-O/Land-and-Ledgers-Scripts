// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Pass IV (Worker 3) — Slice E tests: freight shipment service.
//
// Real reconciliation figures: units loaded/unloaded, wagon capacity
// enforcement, conservation (planned == delivered + remaining).

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Logistics;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class FreightShipmentServiceTests
    {
        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private FreightShipmentService service;
        private FreightShipmentHooks hooks;
        private EntityId loader;
        private EntityId driver;

        [SetUp]
        public void SetUp()
        {
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(42);
            service = new FreightShipmentService();
            hooks = new FreightShipmentHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                DayIndex = 42,
                Diagnostics = new List<string>(),
            };
            loader = EntityId.For(EntityKind.Person, 701);
            driver = EntityId.For(EntityKind.Person, 702);

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

        private FreightCargoSpec GrainSpec() => new FreightCargoSpec { MassKgPerUnit = 2f, VolumeM3PerUnit = 0.01f };

        [Test]
        public void FullShipment_LoadTravelUnload_RecipientCustody_Conserved()
        {
            LogisticsShipmentState shipment = service.CreateShipment("ship-1", "mill-1", "store-1",
                100, GrainSpec(), "wagon-1", hooks, out string creason);
            Assert.IsNotNull(shipment, "create: " + creason);
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status);
            Assert.AreEqual("mill-1", shipment.physicalLocationId, "custody: source");

            // Load: real handling time (100 units -> 10 min task).
            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out string lreason), lreason);
            Assert.AreEqual(LogisticsShipmentStatus.Loading, shipment.Status);
            int lguard = 0;
            while (!shipment.loadApplied && lguard++ < 10)
                Assert.IsTrue(service.WorkLoading(shipment, 60, hooks, out string wreason), wreason);
            Assert.IsTrue(shipment.loadApplied, "loaded");
            Assert.AreEqual(100, shipment.RemainingQuantityUnits, "wagon holds 100");
            Assert.AreEqual("wagon:wagon-1", shipment.physicalLocationId, "custody: wagon");
            Assert.IsTrue(service.VerifyConservation(shipment, 0, out string cons1), cons1);

            // Dispatch with a real driver and team.
            var team = new List<EntityId> { EntityId.For(EntityKind.Animal, 11), EntityId.For(EntityKind.Animal, 12) };
            Assert.IsTrue(service.Dispatch(shipment, driver, team, hooks, out string dreason), dreason);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status);
            Assert.IsTrue(shipment.physicalTransportBound, "transport bound");

            // Travel: advance past the transit duration.
            shipment.transitDurationGameSeconds = 120f;
            service.AdvanceTransit(shipment, 60f, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status, "still travelling");
            Assert.Greater(shipment.physicalProgress01, 0f, "progress advancing");
            service.AdvanceTransit(shipment, 60f, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.Status, "arrived");
            Assert.AreEqual(1f, shipment.physicalProgress01, 0.001f, "progress complete");

            // Unload: real handling time; recipient takes custody.
            Assert.IsTrue(service.BeginUnloading(shipment, loader, hooks, out string ureason), ureason);
            int uguard = 0;
            while (shipment.Status != LogisticsShipmentStatus.Completed && uguard++ < 10)
                Assert.IsTrue(service.WorkUnloading(shipment, 60, hooks, out string wureason), wureason);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status, "completed");
            Assert.IsTrue(shipment.deliveryApplied, "delivery applied");
            Assert.AreEqual(0, shipment.RemainingQuantityUnits, "wagon empty");
            Assert.AreEqual("store-1", shipment.physicalLocationId, "custody: recipient");
            Assert.IsFalse(shipment.physicalTransportBound, "transport released");
            Assert.IsTrue(service.VerifyConservation(shipment, 100, out string cons2), cons2);
        }

        [Test]
        public void OverweightCargo_BlockedPreLoad_ZeroCargo_MR_P001()
        {
            // 1000 units x 2kg = 2000kg > 1000kg capacity.
            LogisticsShipmentState shipment = service.CreateShipment("ship-heavy", "mill-1", "store-1",
                1000, GrainSpec(), "wagon-1", hooks, out string reason);
            Assert.IsNotNull(shipment, "blocked record returned");
            Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status, "failed");
            Assert.IsTrue(shipment.IsPreLoadBlocked, "MR-P001: pre-load blocked owns zero cargo");
            Assert.AreEqual(0, shipment.RemainingQuantityUnits, "zero cargo");
            Assert.IsFalse(shipment.loadApplied, "never loaded");
            StringAssert.Contains("mass", shipment.BlockedReason.ToLowerInvariant(), "reason: " + shipment.BlockedReason);

            // A blocked shipment cannot be loaded.
            Assert.IsFalse(service.BeginLoading(shipment, loader, hooks, out string lreason));
            StringAssert.Contains("blocked", lreason.ToLowerInvariant(), "load refused: " + lreason);
            Assert.IsTrue(service.VerifyConservation(shipment, 0, out string cons), cons);
        }

        [Test]
        public void OverVolumeCargo_BlockedPreLoad()
        {
            // 1000 units x 0.01m3 = 10m3 > 4m3 capacity (mass 2000kg also over, but volume checked too).
            var bulky = new FreightCargoSpec { MassKgPerUnit = 0.5f, VolumeM3PerUnit = 0.01f };
            LogisticsShipmentState shipment = service.CreateShipment("ship-bulky", "mill-1", "store-1",
                1000, bulky, "wagon-1", hooks, out _);
            Assert.IsTrue(shipment.IsPreLoadBlocked, "blocked on volume");
            StringAssert.Contains("volume", shipment.BlockedReason.ToLowerInvariant(), "reason: " + shipment.BlockedReason);
        }

        [Test]
        public void PhantomWagon_ShipmentRefused()
        {
            LogisticsShipmentState shipment = service.CreateShipment("ship-ghost", "mill-1", "store-1",
                10, GrainSpec(), "wagon-999", hooks, out string reason);
            Assert.IsNull(shipment, "refused");
            StringAssert.Contains("wagon", reason.ToLowerInvariant(), "reason: " + reason);
        }

        [Test]
        public void DelayedShipment_Holds_Resumes_Completes()
        {
            LogisticsShipmentState shipment = service.CreateShipment("ship-2", "mill-1", "store-1",
                50, GrainSpec(), "wagon-1", hooks, out _);
            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out _));
            int lguard = 0;
            while (!shipment.loadApplied && lguard++ < 10)
                service.WorkLoading(shipment, 60, hooks, out _);
            Assert.IsTrue(service.Dispatch(shipment, driver, null, hooks, out _));

            shipment.transitDurationGameSeconds = 120f;
            service.AdvanceTransit(shipment, 60f, hooks);
            service.DelayShipment(shipment, "bridge washed out", hooks);
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status, "delayed");
            float progressAtDelay = shipment.physicalProgress01;
            service.AdvanceTransit(shipment, 600f, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status, "still delayed — no phantom progress");
            Assert.AreEqual(50, shipment.RemainingQuantityUnits, "cargo intact during delay");

            service.ResumeShipment(shipment, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status, "resumed");
            service.AdvanceTransit(shipment, 120f, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.Status, "arrived after resume");
        }

        [Test]
        public void SaveLoad_MidTransit_ResumesWithoutDuplicationOrLoss()
        {
            LogisticsShipmentState shipment = service.CreateShipment("ship-3", "mill-1", "store-1",
                80, GrainSpec(), "wagon-1", hooks, out _);
            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out _));
            int lguard = 0;
            while (!shipment.loadApplied && lguard++ < 10)
                service.WorkLoading(shipment, 60, hooks, out _);
            Assert.IsTrue(service.Dispatch(shipment, driver, null, hooks, out _));
            shipment.transitDurationGameSeconds = 200f;
            service.AdvanceTransit(shipment, 80f, hooks); // partway

            // Save mid-transit, restore into a fresh service.
            FreightShipmentService.FreightServiceSaveDto dto =
                service.CaptureSaveDto(new List<LogisticsShipmentState> { shipment });
            var service2 = new FreightShipmentService();
            var wagon = new EquipmentAsset
            {
                AssetId = "wagon-1", Kind = "farm-wagon", PayloadMassCapacityKg = 1000f, CargoVolumeCapacityM3 = 4f,
            };
            service2.RegisterWagon(wagon);
            List<LogisticsShipmentState> restored = service2.RestoreSaveDto(dto, hooks);
            Assert.AreEqual(1, restored.Count, "one shipment restored");
            LogisticsShipmentState r = restored[0];
            Assert.AreEqual("ship-3", r.ShipmentId, "identity preserved");
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, r.Status, "status preserved");
            Assert.AreEqual(80, r.RemainingQuantityUnits, "cargo preserved — no loss, no duplication");
            Assert.IsTrue(r.loadApplied, "load flag preserved");
            Assert.AreEqual("wagon:wagon-1", r.physicalLocationId, "custody preserved");

            // Finish the restored shipment.
            service2.AdvanceTransit(r, 200f, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, r.Status, "arrived");
            Assert.IsTrue(service2.BeginUnloading(r, loader, hooks, out _));
            int uguard = 0;
            while (r.Status != LogisticsShipmentStatus.Completed && uguard++ < 10)
                service2.WorkUnloading(r, 60, hooks, out _);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, r.Status, "completed after load");
            Assert.IsTrue(service2.VerifyConservation(r, 80, out string cons), cons);
        }

        [Test]
        public void NoDuplicateGoods_DoubleUnloadImpossible()
        {
            LogisticsShipmentState shipment = service.CreateShipment("ship-4", "mill-1", "store-1",
                40, GrainSpec(), "wagon-1", hooks, out _);
            Assert.IsTrue(service.BeginLoading(shipment, loader, hooks, out _));
            int lguard = 0;
            while (!shipment.loadApplied && lguard++ < 10)
                service.WorkLoading(shipment, 60, hooks, out _);
            Assert.IsTrue(service.Dispatch(shipment, driver, null, hooks, out _));
            shipment.transitDurationGameSeconds = 10f;
            service.AdvanceTransit(shipment, 10f, hooks);
            Assert.IsTrue(service.BeginUnloading(shipment, loader, hooks, out _));
            int uguard = 0;
            while (shipment.Status != LogisticsShipmentStatus.Completed && uguard++ < 10)
                service.WorkUnloading(shipment, 60, hooks, out _);

            // Unloading again is impossible: no task, no status, no cargo.
            Assert.IsFalse(service.BeginUnloading(shipment, loader, hooks, out string reason),
                "second unload refused");
            Assert.IsFalse(service.WorkUnloading(shipment, 60, hooks, out _), "work unload refused");
            Assert.AreEqual(0, shipment.RemainingQuantityUnits, "still zero — no duplicated goods");
            Assert.IsTrue(service.VerifyConservation(shipment, 40, out string cons), cons);
        }
    }
}
