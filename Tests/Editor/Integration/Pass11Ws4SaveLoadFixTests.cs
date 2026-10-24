// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Pass XI WS4 save/load fixes: production persistence for the two gaps the
// WS4 30-day settlement proof found and compensated for in its restore path:
//  1. ConstructionProjectExecutor.sequence was not in ConstructionProjectSaveDto,
//     so a restored executor re-minted an existing project id.
//  2. FreightShipmentService.dispatchAuthorities was not in FreightServiceSaveDto,
//     so a restored in-transit shipment completed without releasing its wagon
//     reservation, leaving a stale hold that refused later shipments.
// These tests exercise the production fix directly (no reflection compensation).

using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Logistics;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Editor.Integration
{
    [TestFixture]
    public sealed class Pass11Ws4SaveLoadFixTests
    {
        // ---------- Gap 1: construction project sequence ----------

        private static BuildingDesign CabinDesign()
        {
            var design = new BuildingDesign
            {
                DesignId = "cabin-a",
                DisplayName = "Settler cabin",
                WorksKind = ConstructionWorksKind.Unspecified,
                KindLabel = "cabin",
            };
            var foundation = new BuildingDesignPhase
            {
                PhaseName = "Foundation", Sequence = 0, LaborMinutes = 240, Notes = "stone footings",
            };
            var framing = new BuildingDesignPhase
            {
                PhaseName = "Framing", Sequence = 1, LaborMinutes = 480, Notes = "log walls and roof",
            };
            design.Phases.Add(foundation);
            design.Phases.Add(framing);
            design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
            return design;
        }

        private static List<ConstructionProject> BuildTwoProjects(out ConstructionProjectExecutor executor)
        {
            var diag = new List<string>();
            var catalog = new BuildingDesignCatalog();
            Assert.IsNull(catalog.RegisterDesign(CabinDesign(), diag));
            executor = new ConstructionProjectExecutor();
            ConstructionProject p1 = executor.CreateProject(catalog, 1, "cabin-a", "parcel-9", 10, diag);
            Assert.IsNotNull(p1);
            Assert.IsNull(executor.CreateWorkPackages(p1, catalog.FindDesign("cabin-a"), 10, diag));
            ConstructionProject p2 = executor.CreateProject(catalog, 1, "cabin-a", "parcel-10", 10, diag);
            Assert.IsNotNull(p2);
            // Sequence consumed: p1 = cproj-0, its packages wpkg-cproj-0-1/-2, p2 = cproj-3.
            Assert.AreEqual("cproj-0", p1.ProjectId);
            Assert.AreEqual("cproj-3", p2.ProjectId);
            return new List<ConstructionProject> { p1, p2 };
        }

        [Test]
        public void Construction_SaveRestore_NextProjectIdStaysUnique()
        {
            List<ConstructionProject> projects = BuildTwoProjects(out ConstructionProjectExecutor executor);
            ConstructionProjectExecutor.ConstructionProjectSaveDto dto =
                executor.CaptureSaveDto(projects);

            var restoredExecutor = new ConstructionProjectExecutor();
            var diag = new List<string>();
            List<ConstructionProject> restored = restoredExecutor.LoadFromSaveDto(dto, diag);
            Assert.AreEqual(2, restored.Count);

            var diag2 = new List<string>();
            var catalog = new BuildingDesignCatalog();
            Assert.IsNull(catalog.RegisterDesign(CabinDesign(), diag2));
            ConstructionProject next = restoredExecutor.CreateProject(catalog, 1, "cabin-a", "parcel-11", 10, diag2);
            Assert.IsNotNull(next);
            Assert.AreEqual("cproj-4", next.ProjectId, "sequence continues past the restored projects");
            Assert.IsFalse(string.Equals(next.ProjectId, restored[0].ProjectId, StringComparison.Ordinal));
            Assert.IsFalse(string.Equals(next.ProjectId, restored[1].ProjectId, StringComparison.Ordinal));
            Assert.IsNull(restoredExecutor.CreateWorkPackages(next, catalog.FindDesign("cabin-a"), 10, diag2));
            foreach (WorkPackage package in next.WorkPackages)
                foreach (ConstructionProject old in restored)
                    foreach (WorkPackage oldPackage in old.WorkPackages)
                        Assert.IsFalse(string.Equals(package.PackageId, oldPackage.PackageId, StringComparison.Ordinal),
                            "package id collision: " + package.PackageId);
        }

        [Test]
        public void Construction_OldSaveWithoutSequenceField_LoadsWithoutCollision()
        {
            List<ConstructionProject> projects = BuildTwoProjects(out ConstructionProjectExecutor _);
            // Old-save shape: the DTO carries projects but no sequence value
            // (field default -1 = absent), exactly as a pre-fix save loads.
            var oldDto = new ConstructionProjectExecutor.ConstructionProjectSaveDto();
            oldDto.Projects.AddRange(projects);

            var executor = new ConstructionProjectExecutor();
            List<ConstructionProject> restored = executor.LoadFromSaveDto(oldDto, new List<string>());
            Assert.AreEqual(2, restored.Count);

            var diag = new List<string>();
            var catalog = new BuildingDesignCatalog();
            Assert.IsNull(catalog.RegisterDesign(CabinDesign(), diag));
            ConstructionProject next = executor.CreateProject(catalog, 1, "cabin-a", "parcel-11", 10, diag);
            Assert.IsNotNull(next);
            Assert.AreEqual("cproj-4", next.ProjectId,
                "recomputed floor (max restored suffix + 1) prevents the cproj-0 re-mint");
        }

        [Test]
        public void Construction_OldSaveWithZeroedSequenceField_LoadsWithoutCollision()
        {
            // A deserializer that zeroes an absent int field must still be safe:
            // the recomputed floor, not the zero, drives the sequence.
            List<ConstructionProject> projects = BuildTwoProjects(out ConstructionProjectExecutor _);
            var zeroedDto = new ConstructionProjectExecutor.ConstructionProjectSaveDto();
            zeroedDto.Projects.AddRange(projects);
            zeroedDto.ExecutorSequence = 0;

            var executor = new ConstructionProjectExecutor();
            executor.LoadFromSaveDto(zeroedDto, new List<string>());

            var diag = new List<string>();
            var catalog = new BuildingDesignCatalog();
            Assert.IsNull(catalog.RegisterDesign(CabinDesign(), diag));
            ConstructionProject next = executor.CreateProject(catalog, 1, "cabin-a", "parcel-11", 10, diag);
            Assert.AreEqual("cproj-4", next.ProjectId);
        }

        [Test]
        public void Construction_PersistedSequenceAheadOfProjects_Wins()
        {
            List<ConstructionProject> projects = BuildTwoProjects(out ConstructionProjectExecutor executor);
            ConstructionProjectExecutor.ConstructionProjectSaveDto dto =
                executor.CaptureSaveDto(projects);
            // Captured list was a subset of a busier executor: the persisted
            // sequence (here the real captured value is 4; push it ahead to
            // prove the field, not just the recompute, is honoured).
            dto.ExecutorSequence = 99;

            var restoredExecutor = new ConstructionProjectExecutor();
            restoredExecutor.LoadFromSaveDto(dto, new List<string>());

            var diag = new List<string>();
            var catalog = new BuildingDesignCatalog();
            Assert.IsNull(catalog.RegisterDesign(CabinDesign(), diag));
            ConstructionProject next = restoredExecutor.CreateProject(catalog, 1, "cabin-a", "parcel-11", 10, diag);
            Assert.AreEqual("cproj-99", next.ProjectId);
        }

        // ---------- Gap 2: freight dispatch authorities ----------

        private sealed class FreightWorld
        {
            public TaskAuthority Tasks;
            public WorkTimeBudgetStore Budgets;
            public EquipmentReservationService Reservations;
            public Dictionary<string, int> RecipientStock;
            public FreightShipmentHooks Hooks;
            public EntityId Loader;
            public EntityId Driver;
        }

        private static FreightWorld NewWorld(EquipmentReservationService reservations)
        {
            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(42);
            var stock = new Dictionary<string, int>(StringComparer.Ordinal);
            var world = new FreightWorld
            {
                Tasks = tasks,
                Budgets = budgets,
                Reservations = reservations,
                RecipientStock = stock,
                Loader = EntityId.For(EntityKind.Person, 701),
                Driver = EntityId.For(EntityKind.Person, 702),
            };
            world.Hooks = new FreightShipmentHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                DayIndex = 42,
                Diagnostics = new List<string>(),
                Reservations = reservations,
                NowGameSeconds = 100f,
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    if (quantityUnits <= 0) return 0;
                    string key = string.IsNullOrWhiteSpace(shipment.destinationBusinessInstanceId)
                        ? "nowhere" : shipment.destinationBusinessInstanceId;
                    stock[key] = stock.TryGetValue(key, out int have) ? have + quantityUnits : quantityUnits;
                    return quantityUnits;
                },
            };
            return world;
        }

        private static FreightShipmentService NewServiceWithWagon()
        {
            var service = new FreightShipmentService();
            service.RegisterWagon(new EquipmentAsset
            {
                AssetId = "wagon-1",
                Kind = "farm-wagon",
                DisplayName = "Farm wagon",
                Condition01 = 0.9f,
                PayloadMassCapacityKg = 1000f,
                CargoVolumeCapacityM3 = 4f,
            });
            return service;
        }

        private static LogisticsShipmentState DispatchInTransit(FreightShipmentService service, FreightWorld world,
            string shipmentId)
        {
            LogisticsShipmentState shipment = service.CreateShipment(shipmentId, "mill-1", "store-1",
                50, new FreightCargoSpec { MassKgPerUnit = 2f, VolumeM3PerUnit = 0.01f },
                "wagon-1", world.Hooks, out string creason);
            Assert.IsNotNull(shipment, "create: " + creason);
            Assert.IsTrue(service.BeginLoading(shipment, world.Loader, world.Hooks, out string lreason), lreason);
            int lguard = 0;
            while (!shipment.loadApplied && lguard++ < 10)
                Assert.IsTrue(service.WorkLoading(shipment, 60, world.Hooks, out string wreason), wreason);
            Assert.IsTrue(service.Dispatch(shipment, world.Driver, null, world.Hooks, out string dreason), dreason);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status);
            shipment.transitDurationGameSeconds = 120f;
            return shipment;
        }

        private static void CompleteShipment(FreightShipmentService service, FreightWorld world,
            LogisticsShipmentState shipment)
        {
            service.AdvanceTransit(shipment, 200f, world.Hooks);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.Status, "arrived");
            Assert.IsTrue(service.BeginUnloading(shipment, world.Loader, world.Hooks, out string ureason), ureason);
            int uguard = 0;
            while (shipment.Status != LogisticsShipmentStatus.Completed && uguard++ < 10)
                Assert.IsTrue(service.WorkUnloading(shipment, 60, world.Hooks, out string wreason), wreason);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status, "completed");
        }

        private static void AssertWagonFreeForNextShipment(FreightShipmentService service, FreightWorld world)
        {
            Assert.IsFalse(world.Reservations.IsReserved(
                ReservationKeyConventions.Asset("wagon-1"), 100f), "wagon reservation released");
            LogisticsShipmentState next = service.CreateShipment("ship-next", "mill-1", "store-1",
                10, new FreightCargoSpec { MassKgPerUnit = 2f, VolumeM3PerUnit = 0.01f },
                "wagon-1", world.Hooks, out string reason);
            Assert.IsNotNull(next, "later shipment refused on a stale hold: " + reason);
        }

        [Test]
        public void Freight_RestoreThenComplete_ReleasesWagonReservationExactlyOnce()
        {
            var world1 = NewWorld(new EquipmentReservationService());
            FreightShipmentService service1 = NewServiceWithWagon();
            LogisticsShipmentState shipment = DispatchInTransit(service1, world1, "ship-1");
            Assert.IsTrue(world1.Reservations.IsReserved(
                ReservationKeyConventions.Asset("wagon-1"), 100f), "hold taken at dispatch");

            // Save mid-transit; restore into a fresh service + a fresh
            // reservation authority loaded from the saved holds (the real
            // load path: the authority instance is new, its holds restored).
            FreightShipmentService.FreightServiceSaveDto dto =
                service1.CaptureSaveDto(new List<LogisticsShipmentState> { shipment });
            Assert.Contains("ship-1", dto.ShipmentIdsWithDispatchAuthorities);
            var reservations2 = new EquipmentReservationService();
            reservations2.LoadFromSaveDto(world1.Reservations.CaptureSaveDto());
            var world2 = NewWorld(reservations2);
            FreightShipmentService service2 = NewServiceWithWagon();
            List<LogisticsShipmentState> restored = service2.RestoreSaveDto(dto, world2.Hooks);
            Assert.AreEqual(1, restored.Count);
            LogisticsShipmentState r = restored[0];
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, r.Status);
            Assert.IsTrue(world2.Reservations.IsReserved(
                ReservationKeyConventions.Asset("wagon-1"), 100f), "hold survives restore");

            CompleteShipment(service2, world2, r);
            Assert.AreEqual(50, world2.RecipientStock["store-1"], "goods credited exactly once");
            AssertWagonFreeForNextShipment(service2, world2);
        }

        [Test]
        public void Freight_DoubleRestore_ReleasesExactlyOnce()
        {
            var world1 = NewWorld(new EquipmentReservationService());
            FreightShipmentService service1 = NewServiceWithWagon();
            LogisticsShipmentState shipment = DispatchInTransit(service1, world1, "ship-1");
            FreightShipmentService.FreightServiceSaveDto dto =
                service1.CaptureSaveDto(new List<LogisticsShipmentState> { shipment });

            var reservations2 = new EquipmentReservationService();
            reservations2.LoadFromSaveDto(world1.Reservations.CaptureSaveDto());
            var world2 = NewWorld(reservations2);
            FreightShipmentService service2 = NewServiceWithWagon();
            // Restoring the same DTO twice must not create a second hold:
            // the rebind overwrites the same map key with the same authority.
            // (The second restore's objects are duplicates Track() rejects;
            // the tracked shipment is the first restore's, so complete that.)
            List<LogisticsShipmentState> restored = service2.RestoreSaveDto(dto, world2.Hooks);
            Assert.AreEqual(1, restored.Count);
            List<LogisticsShipmentState> duplicates = service2.RestoreSaveDto(dto, world2.Hooks);
            Assert.AreEqual(1, duplicates.Count);
            Assert.AreEqual(1, service2.TrackedShipments.Count, "no duplicate tracked shipment");

            CompleteShipment(service2, world2, restored[0]);
            Assert.AreEqual(50, world2.RecipientStock["store-1"], "goods credited exactly once");
            AssertWagonFreeForNextShipment(service2, world2);
        }

        [Test]
        public void Freight_OldSaveWithoutDispatchList_RestoredInTransitShipmentStillReleases()
        {
            var world1 = NewWorld(new EquipmentReservationService());
            FreightShipmentService service1 = NewServiceWithWagon();
            LogisticsShipmentState shipment = DispatchInTransit(service1, world1, "ship-1");
            // Old-save shape: shipment DTO only, no dispatch-authority list.
            var oldDto = new FreightShipmentService.FreightServiceSaveDto();
            oldDto.Shipments.Add(shipment.CaptureSaveDto());
            Assert.AreEqual(0, oldDto.ShipmentIdsWithDispatchAuthorities.Count);

            var reservations2 = new EquipmentReservationService();
            reservations2.LoadFromSaveDto(world1.Reservations.CaptureSaveDto());
            var world2 = NewWorld(reservations2);
            FreightShipmentService service2 = NewServiceWithWagon();
            List<LogisticsShipmentState> restored = service2.RestoreSaveDto(oldDto, world2.Hooks);
            Assert.AreEqual(1, restored.Count);

            CompleteShipment(service2, world2, restored[0]);
            Assert.AreEqual(50, world2.RecipientStock["store-1"], "goods credited exactly once");
            AssertWagonFreeForNextShipment(service2, world2);
        }

        [Test]
        public void Freight_FreshRun_Completes_ReleasesExactlyOnce()
        {
            // Control: the fresh (never restored) path is unchanged.
            var world = NewWorld(new EquipmentReservationService());
            FreightShipmentService service = NewServiceWithWagon();
            LogisticsShipmentState shipment = DispatchInTransit(service, world, "ship-1");
            CompleteShipment(service, world, shipment);
            Assert.AreEqual(50, world.RecipientStock["store-1"], "goods credited exactly once");
            AssertWagonFreeForNextShipment(service, world);
        }
    }
}
