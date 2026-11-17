using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Storage;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.EditorTests.Infrastructure
{
    /// <summary>
    /// G-4 (Agent G, expanded campaign 2026-11-16): hub-to-state sync
    /// for the stove fuel stores and the storage bindings. Proves the
    /// state methods directly (no hub, no Unity): live fuel is captured
    /// into the Pass 9 state list so the next Capture persists it and
    /// the bootstrapper restore returns it; a live storage binding is
    /// captured the same way; an empty registry contributes nothing; a
    /// stale state-only entry is dropped (clear-then-load — fuel and
    /// bindings are fully determined by the live registries, unlike the
    /// adopt-only register sync); sync is idempotent; null inputs never
    /// throw; and captured entries survive a real Capture/Restore
    /// round trip. The SaveLoadManager call site and the hub accessors
    /// are harness-excluded and source-reviewed only — no test here
    /// claims Unity compilation or PlayMode.
    /// </summary>
    [TestFixture]
    public sealed class FuelAndStorageSyncG4Tests
    {
        private const int HouseholdId = 7;
        private const int BuildingInstanceId = 101;

        private static HouseholdFurnishingRegister MakeRegister(int householdId = HouseholdId)
        {
            return new HouseholdFurnishingRegister { HouseholdId = householdId };
        }

        private static EquipmentAsset MakeStove(string assetId, float condition = 1f)
        {
            return new EquipmentAsset
            {
                AssetId = assetId,
                Kind = HouseholdStoveRegistry.StoveAssetKind,
                DisplayName = "Cooking Stove",
                Condition01 = condition,
                OwnerKind = "household",
                OwnerId = HouseholdId.ToString(),
            };
        }

        private static List<InteriorFloorAuthoring> GetInteriorFloors(BuildingFootprintAuthority authority)
        {
            FieldInfo field = typeof(BuildingFootprintAuthority).GetField("interiorFloors", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (List<InteriorFloorAuthoring>)field.GetValue(authority);
        }

        private static void AddWalkableTestFloor(BuildingFootprintAuthority authority)
        {
            InteriorFloorAuthoring floor = new() { floorIndex = 0, floorHeightLocal = 0f };
            floor.walkableAreas.Add(new InteriorPolygonAuthoring
            {
                label = "Test Floor",
                vertices = new List<Vector3>
                {
                    new(-3f, 0f, -3f), new(3f, 0f, -3f), new(3f, 0f, 3f), new(-3f, 0f, 3f),
                },
            });
            GetInteriorFloors(authority).Add(floor);
        }

        private static BuildingFootprintAuthority MakeBuilding(string name)
        {
            GameObject building = new(name);
            BuildingFootprintAuthority authority = building.AddComponent<BuildingFootprintAuthority>();
            AddWalkableTestFloor(authority);
            return authority;
        }

        private static BuildingEquipmentPlacement MakePlacement(string name, Vector3 position)
        {
            GameObject item = new(name);
            item.transform.position = position;
            return item.AddComponent<BuildingEquipmentPlacement>();
        }

        private static HouseholdStoveRegistry MakeStoveRegistry(int fuelUnits, string stoveAssetId = "stove-1")
        {
            GameObject buildingObject = null;
            GameObject placementObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("G4 fuel fixture");
                buildingObject = building.gameObject;
                var register = MakeRegister();
                Assert.IsNull(register.TryRegisterAsset(MakeStove(stoveAssetId)));
                BuildingEquipmentPlacement placement = MakePlacement("Stove", Vector3.zero);
                placementObject = placement.gameObject;
                string error = register.TryInstall(stoveAssetId, placement, building, BuildingInstanceId, 0,
                    FurnishingCapabilityKind.StoveWorkstation, "stove",
                    FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                    out FurnishingPlacementBinding binding);
                Assert.IsNull(error, error);
                var stoves = new HouseholdStoveRegistry(HouseholdId, register)
                {
                    HomeBuildingInstanceId = BuildingInstanceId,
                };
                stoves.RebuildFromRegister();
                HouseholdStove stove = stoves.FindStove(stoveAssetId);
                Assert.NotNull(stove, "The installed stove must derive.");
                stove.FuelWoodUnitsOnHand = fuelUnits;
                return stoves;
            }
            finally
            {
                if (placementObject != null) Object.DestroyImmediate(placementObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        private static FurnishingStorageRegistry MakeStorageRegistryWithBinding()
        {
            var zone = new PhysicalStorageZone(
                new PhysicalStorageZoneDefinition("binding-1", "building:101:cabinet-1", StorageExposure.Indoor,
                    StorageCapacityLimit.Known(100f), StorageCapacityLimit.Known(1f)),
                (authority, lotId) => true);
            var binding = new FurnishingStorageBinding
            {
                HouseholdId = HouseholdId,
                AssetId = "cabinet-1",
                ZoneId = "binding-1",
                BuildingInstanceId = BuildingInstanceId,
                PlacementId = "cabinet-1",
                Visibility = StorageVisibility.OpenDisplay,
                DisplayMeshCap = 12,
                AllowedReserveCategoryId = string.Empty,
                AllowedItemIds = new List<string> { "flour" },
                Zone = zone,
            };
            Assert.IsTrue(zone.TryPlace(
                new StoredLotReference(CustodyTransferLog.HouseholdInventoryAuthority, "12",
                    StorageLoadMeasurement.MassAndVolume(2f, 0.004f)), out string refusal), refusal);
            var registry = new FurnishingStorageRegistry { HouseholdId = HouseholdId };
            registry.Bindings.Add(binding);
            return registry;
        }

        [Test]
        public void SyncFuelStoresFromHub_CapturesLiveFuelIntoState()
        {
            var state = new Series2Pass9SaveState();
            HouseholdStoveRegistry stoves = MakeStoveRegistry(fuelUnits: 5);
            var diag = new List<string>();

            state.SyncFuelStoresFromHub(new[] { stoves }, diag);

            Assert.AreEqual(1, state.HouseholdStoveFuelStores.Count);
            Assert.AreEqual(HouseholdId, state.HouseholdStoveFuelStores[0].HouseholdId);
            Assert.AreEqual("stove-1", state.HouseholdStoveFuelStores[0].StoveAssetId);
            Assert.AreEqual(5, state.HouseholdStoveFuelStores[0].FuelWoodUnits);
            Assert.IsTrue(diag[diag.Count - 1].Contains("captured 1"), "Summary line must report the capture.");
        }

        [Test]
        public void SyncFuelStoresFromHub_EmptyRegistryContributesNothing()
        {
            var state = new Series2Pass9SaveState();
            HouseholdStoveRegistry stoves = MakeStoveRegistry(fuelUnits: 0);
            var diag = new List<string>();

            state.SyncFuelStoresFromHub(new[] { stoves }, diag);

            Assert.AreEqual(0, state.HouseholdStoveFuelStores.Count);
        }

        [Test]
        public void SyncFuelStoresFromHub_DropsStaleStateOnlyEntry()
        {
            var state = new Series2Pass9SaveState();
            state.HouseholdStoveFuelStores.Add(new HouseholdStoveFuelStoreDto
            {
                HouseholdId = HouseholdId,
                StoveAssetId = "stove-9",
                FuelWoodUnits = 3,
            });
            HouseholdStoveRegistry stoves = MakeStoveRegistry(fuelUnits: 0);
            var diag = new List<string>();

            state.SyncFuelStoresFromHub(new[] { stoves }, diag);

            Assert.AreEqual(0, state.HouseholdStoveFuelStores.Count,
                "Clear-then-load: fuel for a stove that no longer holds it must not survive the sync.");
        }

        [Test]
        public void SyncFuelStoresFromHub_IsIdempotent()
        {
            var state = new Series2Pass9SaveState();
            HouseholdStoveRegistry stoves = MakeStoveRegistry(fuelUnits: 5);
            var diag = new List<string>();

            state.SyncFuelStoresFromHub(new[] { stoves }, diag);
            state.SyncFuelStoresFromHub(new[] { stoves }, diag);

            Assert.AreEqual(1, state.HouseholdStoveFuelStores.Count);
            Assert.AreEqual(5, state.HouseholdStoveFuelStores[0].FuelWoodUnits);
        }

        [Test]
        public void SyncFuelStoresFromHub_NullInputs_NeverThrow()
        {
            var state = new Series2Pass9SaveState();
            Assert.DoesNotThrow(() => state.SyncFuelStoresFromHub(null, null));
            Assert.DoesNotThrow(() => state.SyncFuelStoresFromHub(new HouseholdStoveRegistry[0], null));
            Assert.DoesNotThrow(() => state.SyncFuelStoresFromHub(new HouseholdStoveRegistry[] { null }, new List<string>()));
        }

        [Test]
        public void SyncFuelStores_RoundTrip_RestoreYieldsSavedFuel()
        {
            var source = new Series2Pass9SaveState();
            HouseholdStoveRegistry stoves = MakeStoveRegistry(fuelUnits: 5);
            var diag = new List<string>();
            source.SyncFuelStoresFromHub(new[] { stoves }, diag);

            Series2Pass9SaveDto dto = source.Capture(diag);
            var target = new Series2Pass9SaveState();
            target.Restore(dto, null, diag);

            Assert.AreEqual(1, target.HouseholdStoveFuelStores.Count);
            Assert.AreEqual(5, target.HouseholdStoveFuelStores[0].FuelWoodUnits);

            // The bootstrapper's Continue shape: a fresh registry
            // rebuilds from the restored register, then restores fuel.
            HouseholdStoveRegistry reloaded = MakeStoveRegistry(fuelUnits: 0);
            reloaded.RestoreFuelStores(target.HouseholdStoveFuelStores);
            Assert.AreEqual(5, reloaded.FindStove("stove-1").FuelWoodUnitsOnHand);
        }

        [Test]
        public void SyncStorageBindingsFromHub_CapturesLiveBindingsIntoState()
        {
            var state = new Series2Pass9SaveState();
            FurnishingStorageRegistry storage = MakeStorageRegistryWithBinding();
            var diag = new List<string>();

            state.SyncStorageBindingsFromHub(new[] { storage }, diag);

            Assert.AreEqual(1, state.HouseholdStorageBindings.Count);
            FurnishingStorageBindingSaveDto saved = state.HouseholdStorageBindings[0];
            Assert.AreEqual(HouseholdId, saved.HouseholdId);
            Assert.AreEqual("cabinet-1", saved.AssetId);
            Assert.AreEqual("binding-1", saved.ZoneId);
            Assert.AreEqual(StorageVisibility.OpenDisplay, saved.Visibility);
            Assert.AreEqual(1, saved.PlacedLots.Count);
            Assert.AreEqual("12", saved.PlacedLots[0].LotId);
            Assert.IsTrue(diag[diag.Count - 1].Contains("captured 1"), "Summary line must report the capture.");
        }

        [Test]
        public void SyncStorageBindingsFromHub_EmptyRegistryContributesNothing()
        {
            var state = new Series2Pass9SaveState();
            var storage = new FurnishingStorageRegistry { HouseholdId = HouseholdId };
            var diag = new List<string>();

            state.SyncStorageBindingsFromHub(new[] { storage }, diag);

            Assert.AreEqual(0, state.HouseholdStorageBindings.Count);
        }

        [Test]
        public void SyncStorageBindingsFromHub_DropsStaleStateOnlyEntry()
        {
            var state = new Series2Pass9SaveState();
            state.HouseholdStorageBindings.Add(new FurnishingStorageBindingSaveDto
            {
                HouseholdId = HouseholdId,
                AssetId = "cabinet-9",
                ZoneId = "binding-9",
            });
            var storage = new FurnishingStorageRegistry { HouseholdId = HouseholdId };
            var diag = new List<string>();

            state.SyncStorageBindingsFromHub(new[] { storage }, diag);

            Assert.AreEqual(0, state.HouseholdStorageBindings.Count,
                "Clear-then-load: a binding for a fixture that is no longer bound must not survive the sync.");
        }

        [Test]
        public void SyncStorageBindingsFromHub_IsIdempotent()
        {
            var state = new Series2Pass9SaveState();
            FurnishingStorageRegistry storage = MakeStorageRegistryWithBinding();
            var diag = new List<string>();

            state.SyncStorageBindingsFromHub(new[] { storage }, diag);
            state.SyncStorageBindingsFromHub(new[] { storage }, diag);

            Assert.AreEqual(1, state.HouseholdStorageBindings.Count);
            Assert.AreEqual("cabinet-1", state.HouseholdStorageBindings[0].AssetId);
        }

        [Test]
        public void SyncStorageBindingsFromHub_NullInputs_NeverThrow()
        {
            var state = new Series2Pass9SaveState();
            Assert.DoesNotThrow(() => state.SyncStorageBindingsFromHub(null, null));
            Assert.DoesNotThrow(() => state.SyncStorageBindingsFromHub(new FurnishingStorageRegistry[0], null));
            Assert.DoesNotThrow(() => state.SyncStorageBindingsFromHub(new FurnishingStorageRegistry[] { null }, new List<string>()));
        }

        [Test]
        public void SyncStorageBindings_RoundTrip_PreservesBindingAndPlacements()
        {
            var source = new Series2Pass9SaveState();
            FurnishingStorageRegistry storage = MakeStorageRegistryWithBinding();
            var diag = new List<string>();
            source.SyncStorageBindingsFromHub(new[] { storage }, diag);

            Series2Pass9SaveDto dto = source.Capture(diag);
            var target = new Series2Pass9SaveState();
            target.Restore(dto, null, diag);

            Assert.AreEqual(1, target.HouseholdStorageBindings.Count);
            FurnishingStorageBindingSaveDto saved = target.HouseholdStorageBindings[0];
            Assert.AreEqual("cabinet-1", saved.AssetId);
            Assert.AreEqual(StorageVisibility.OpenDisplay, saved.Visibility);
            Assert.AreEqual(12, saved.DisplayMeshCap);
            Assert.AreEqual(1, saved.AllowedItemIds.Count);
            Assert.AreEqual("flour", saved.AllowedItemIds[0]);
            Assert.IsTrue(saved.MassCapacity.IsKnown);
            Assert.AreEqual(100f, saved.MassCapacity.Value);
            Assert.AreEqual(1, saved.PlacedLots.Count);
            Assert.AreEqual("12", saved.PlacedLots[0].LotId);
            Assert.AreEqual(2f, saved.PlacedLots[0].Load.MassKg);

            // The sync's CaptureBindings deep-copied the placements, so
            // the state's list is independent of the live registry: a
            // live removal after the sync must not rewrite the saved
            // section.
            storage.Bindings[0].Zone.Remove(CustodyTransferLog.HouseholdInventoryAuthority, "12");
            Series2Pass9SaveDto dto2 = source.Capture(diag);
            Assert.AreEqual(1, dto2.HouseholdStorageBindings[0].PlacedLots.Count,
                "The state's captured binding must survive a live removal after the sync.");
        }
    }
}
