using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Storage;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// G-4 (Agent G, expanded campaign 2026-11-16): the storage
    /// bindings save seam. Proves the capture round-trips the bind
    /// parameters and the placed lots with their saved loads; the
    /// Continue shape (Pass 9 register round trip + RestoreBindings
    /// on the re-pointed inventory) re-binds the fixture and re-places
    /// the lots with the same zone, capacity, visibility and display;
    /// a consumed lot is skipped while the rest restores; a missing
    /// asset restores nothing; restore is idempotent; a derived zone
    /// id that no longer matches the saved zone id drops the binding;
    /// and null inputs never throw. The bootstrapper call site is
    /// harness-excluded and source-reviewed only — no test here claims
    /// Unity compilation or PlayMode.
    ///
    /// Measure function is test-local calibration ONLY (copied from
    /// the B5 tests): household item specs carry Unknown mass/volume
    /// in this tree, so the capacity tests measure with a local table.
    /// Production masses come from the item-spec authority when F wires
    /// them; the zone is measurement-agnostic, so the enforcement
    /// behaviour pinned here is what matters.
    /// </summary>
    [TestFixture]
    public sealed class FurnishingStorageSaveG4Tests
    {
        private const int HouseholdId = 7;
        private const int BuildingInstanceId = 101;
        private const int Day = 100;

        private static HouseholdFurnishingRegister MakeRegister(int householdId = HouseholdId)
        {
            return new HouseholdFurnishingRegister { HouseholdId = householdId };
        }

        private static EquipmentAsset MakeStorageAsset(string assetId, string kind, float condition = 1f)
        {
            return new EquipmentAsset
            {
                AssetId = assetId,
                Kind = kind,
                DisplayName = kind,
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

        private static FurnishingPlacementBinding InstallFixture(
            HouseholdFurnishingRegister register,
            BuildingFootprintAuthority building,
            BuildingEquipmentPlacement placement,
            string assetId)
        {
            string error = register.TryInstall(assetId, placement, building, BuildingInstanceId, 0,
                FurnishingCapabilityKind.None, string.Empty,
                FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                out FurnishingPlacementBinding binding);
            Assert.IsNull(error, error);
            Assert.NotNull(binding);
            return binding;
        }

        private static StorageLoadMeasurement MeasureLot(HouseholdItemLot lot)
        {
            switch (lot.ItemId)
            {
                case HouseholdItemCatalog.FlourId: return StorageLoadMeasurement.MassAndVolume(lot.QuantityUnits * 1f, lot.QuantityUnits * 0.002f);
                case HouseholdItemCatalog.BreadId: return StorageLoadMeasurement.MassAndVolume(lot.QuantityUnits * 0.5f, lot.QuantityUnits * 0.001f);
                default: return StorageLoadMeasurement.Unknown;
            }
        }

        private sealed class StorageFixture
        {
            public GameObject BuildingObject;
            public List<GameObject> ItemObjects = new List<GameObject>();
            public HouseholdFurnishingRegister Register;
            public HouseholdInventory Inventory;
            public FurnishingStorageRegistry Storage;
        }

        private static StorageFixture MakeStorageFixture(string buildingName = "G4 storage fixture")
        {
            BuildingFootprintAuthority building = MakeBuilding(buildingName);
            var fixture = new StorageFixture { BuildingObject = building.gameObject };
            fixture.Register = MakeRegister();
            var inventories = new HouseholdInventoryRegistry();
            fixture.Inventory = inventories.GetOrCreate(HouseholdId);
            fixture.Storage = new FurnishingStorageRegistry
            {
                HouseholdId = HouseholdId,
                Furnishings = fixture.Register,
                Inventory = fixture.Inventory,
            };
            return fixture;
        }

        private static void DestroyFixture(StorageFixture fixture)
        {
            if (fixture == null) return;
            foreach (GameObject item in fixture.ItemObjects)
            {
                if (item != null) Object.DestroyImmediate(item);
            }
            if (fixture.BuildingObject != null) Object.DestroyImmediate(fixture.BuildingObject);
        }

        private static string InstallStorageAsset(StorageFixture fixture, string assetId, string kind, Vector3 position)
        {
            Assert.IsNull(fixture.Register.TryRegisterAsset(MakeStorageAsset(assetId, kind)));
            BuildingEquipmentPlacement placement = MakePlacement(assetId, position);
            fixture.ItemObjects.Add(placement.gameObject);
            BuildingFootprintAuthority building = fixture.BuildingObject.GetComponent<BuildingFootprintAuthority>();
            InstallFixture(fixture.Register, building, placement, assetId);
            return assetId;
        }

        private static FurnishingStorageBinding BindFixture(
            FurnishingStorageRegistry storage,
            string assetId,
            StorageVisibility visibility = StorageVisibility.OpenDisplay,
            float massCapKg = 100f,
            float volumeCapM3 = 1f,
            string allowedReserveCategoryId = "",
            IReadOnlyList<string> allowedItemIds = null,
            int displayMeshCap = FurnishingStorageBinding.DefaultDisplayMeshCapPerSku)
        {
            string error = storage.TryBindFixture(assetId, visibility,
                StorageCapacityLimit.Known(massCapKg),
                StorageCapacityLimit.Known(volumeCapM3),
                MeasureLot,
                allowedReserveCategoryId, allowedItemIds, displayMeshCap,
                out FurnishingStorageBinding binding);
            Assert.IsNull(error, error);
            Assert.NotNull(binding);
            return binding;
        }

        private static int AddLot(HouseholdInventory inventory, string itemId, int units)
        {
            int before = inventory.Lots.Count;
            Assert.IsNull(inventory.AddLot(itemId, units, Day, "test purchase"));
            Assert.AreEqual(before + 1, inventory.Lots.Count, "AddLot appends exactly one lot");
            return inventory.Lots[before].LotId;
        }

        private static void Consume(HouseholdInventory inventory, string itemId, int units)
        {
            var records = new List<HouseholdConsumptionRecord>();
            Assert.IsNull(inventory.Consume(itemId, units, Day, "test consumption", "test", records));
        }

        private static FurnishingDisplayAllocation AllocationFor(FurnishingStorageBinding binding, string skuId)
        {
            foreach (FurnishingDisplayAllocation allocation in binding.Allocations)
            {
                if (allocation != null && allocation.SkuId == skuId) return allocation;
            }
            return null;
        }

        /// <summary>A bound shelf with flour + bread placed, and its captured save data.</summary>
        private static (StorageFixture fixture, FurnishingStorageBinding binding, int flourLot, int breadLot, List<FurnishingStorageBindingSaveDto> saved)
            MakeBoundFixture()
        {
            StorageFixture fixture = MakeStorageFixture();
            InstallStorageAsset(fixture, "shelf-1", FurnishingStorageBinding.ShelfAssetKind, new Vector3(1f, 0f, 0f));
            FurnishingStorageBinding binding = BindFixture(fixture.Storage, "shelf-1",
                StorageVisibility.OpenDisplay, 100f, 1f, "", new[] { "flour", "bread" }, 8);
            int flourLot = AddLot(fixture.Inventory, HouseholdItemCatalog.FlourId, 10);
            int breadLot = AddLot(fixture.Inventory, HouseholdItemCatalog.BreadId, 4);
            Assert.IsNull(fixture.Storage.TryPlaceLot(binding.ZoneId, flourLot));
            Assert.IsNull(fixture.Storage.TryPlaceLot(binding.ZoneId, breadLot));
            List<FurnishingStorageBindingSaveDto> saved = fixture.Storage.CaptureBindings();
            return (fixture, binding, flourLot, breadLot, saved);
        }

        /// <summary>The Pass 9 round trip for the furnishing register — the real Continue shape for the B0 stamp.</summary>
        private static HouseholdFurnishingRegister RoundTripRegister(HouseholdFurnishingRegister register)
        {
            var source = new Series2Pass9SaveState();
            var diag = new List<string>();
            source.SyncRegistersFromHub(new BusinessEquipmentRegister[0], new[] { register }, diag);
            Series2Pass9SaveDto dto = source.Capture(diag);
            var target = new Series2Pass9SaveState();
            target.Restore(dto, null, diag);
            Assert.AreEqual(1, target.HouseholdFurnishingRegisters.Count);
            return target.HouseholdFurnishingRegisters[0];
        }

        [Test]
        public void CaptureBindings_RoundTripsBindParametersAndPlacements()
        {
            (StorageFixture fixture, FurnishingStorageBinding binding, int flourLot, int breadLot, List<FurnishingStorageBindingSaveDto> saved) = MakeBoundFixture();
            try
            {
                Assert.AreEqual(1, saved.Count);
                FurnishingStorageBindingSaveDto dto = saved[0];
                Assert.AreEqual(HouseholdId, dto.HouseholdId);
                Assert.AreEqual("shelf-1", dto.AssetId);
                Assert.AreEqual(binding.ZoneId, dto.ZoneId);
                Assert.AreEqual(BuildingInstanceId, dto.BuildingInstanceId);
                Assert.AreEqual(StorageVisibility.OpenDisplay, dto.Visibility);
                Assert.AreEqual(8, dto.DisplayMeshCap);
                Assert.AreEqual(2, dto.AllowedItemIds.Count);
                Assert.IsTrue(dto.MassCapacity.IsKnown);
                Assert.AreEqual(100f, dto.MassCapacity.Value);
                Assert.IsTrue(dto.VolumeCapacity.IsKnown);
                Assert.AreEqual(1f, dto.VolumeCapacity.Value);
                Assert.AreEqual(2, dto.PlacedLots.Count);
                Assert.AreEqual(flourLot.ToString(), dto.PlacedLots[0].LotId);
                Assert.AreEqual(10f, dto.PlacedLots[0].Load.MassKg, "The saved load is the load measured at placement.");
                Assert.AreEqual(breadLot.ToString(), dto.PlacedLots[1].LotId);
                Assert.AreEqual(2f, dto.PlacedLots[1].Load.MassKg);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void RestoreBindings_ContinueShape_RebindsAndReplacesLots()
        {
            (StorageFixture fixture, FurnishingStorageBinding binding, int flourLot, int breadLot, List<FurnishingStorageBindingSaveDto> saved) = MakeBoundFixture();
            try
            {
                HouseholdFurnishingRegister restoredRegister = RoundTripRegister(fixture.Register);
                FurnishingPlacementBinding live = restoredRegister.LiveBindingFor("shelf-1");
                Assert.NotNull(live);
                Assert.AreEqual(binding.ZoneId, live.BindingId, "The B0 binding id must survive the round trip — the zone id derives from it.");
                Assert.AreEqual(FurnishingCapabilityKind.StorageZone, live.CapabilityKind);
                Assert.AreEqual(binding.ZoneId, live.CapabilityReferenceId);

                var restored = new FurnishingStorageRegistry
                {
                    HouseholdId = HouseholdId,
                    Furnishings = restoredRegister,
                    Inventory = fixture.Inventory,
                };
                restored.RefreshInventory(fixture.Inventory);
                var diag = new List<string>();
                int count = restored.RestoreBindings(saved, diag);

                Assert.AreEqual(1, count);
                FurnishingStorageBinding reborn = restored.BindingForAsset("shelf-1");
                Assert.NotNull(reborn, "The binding must exist after restore.");
                Assert.AreEqual(binding.ZoneId, reborn.ZoneId);
                Assert.AreEqual(StorageVisibility.OpenDisplay, reborn.Visibility);
                Assert.AreEqual(8, reborn.DisplayMeshCap);
                Assert.AreEqual(2, reborn.AllowedItemIds.Count);
                Assert.IsTrue(reborn.Zone.Definition.MassCapacityKg.IsKnown);
                Assert.AreEqual(100f, reborn.Zone.Definition.MassCapacityKg.Value);
                Assert.AreEqual(2, reborn.Zone.Placements.Count, "Both lots re-place with their saved loads.");
                Assert.IsTrue(reborn.ContainsLot(flourLot));
                Assert.IsTrue(reborn.ContainsLot(breadLot));
                FurnishingDisplayAllocation flour = AllocationFor(reborn, HouseholdItemCatalog.FlourId);
                Assert.NotNull(flour, "An OpenDisplay shelf re-derives its display from the restored placements.");
                Assert.AreEqual(10, flour.DisplayedUnits);
                Assert.AreEqual(8, flour.MeshCount, "The restored display cap (8) caps the meshes.");
                Assert.IsTrue(diag[diag.Count - 1].Contains("restored 1"), "Summary line must report the restore.");
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void RestoreBindings_SkipsConsumedLot_KeepsTheRest()
        {
            (StorageFixture fixture, FurnishingStorageBinding binding, int flourLot, int breadLot, List<FurnishingStorageBindingSaveDto> saved) = MakeBoundFixture();
            try
            {
                Consume(fixture.Inventory, HouseholdItemCatalog.BreadId, 4);
                HouseholdFurnishingRegister restoredRegister = RoundTripRegister(fixture.Register);
                var restored = new FurnishingStorageRegistry
                {
                    HouseholdId = HouseholdId,
                    Furnishings = restoredRegister,
                    Inventory = fixture.Inventory,
                };
                restored.RefreshInventory(fixture.Inventory);
                var diag = new List<string>();
                int count = restored.RestoreBindings(saved, diag);

                Assert.AreEqual(1, count, "The binding still restores when one lot is gone.");
                FurnishingStorageBinding reborn = restored.BindingForAsset("shelf-1");
                Assert.NotNull(reborn);
                Assert.AreEqual(1, reborn.Zone.Placements.Count, "Only the surviving lot re-places.");
                Assert.IsTrue(reborn.ContainsLot(flourLot));
                Assert.IsTrue(!reborn.ContainsLot(breadLot));
                Assert.IsTrue(diag[diag.Count - 1].Contains("skipped 1 placed lots"), "The skipped lot is reported.");
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void RestoreBindings_MissingAsset_RestoresNothing()
        {
            (StorageFixture fixture, FurnishingStorageBinding binding, int flourLot, int breadLot, List<FurnishingStorageBindingSaveDto> saved) = MakeBoundFixture();
            try
            {
                var restored = new FurnishingStorageRegistry
                {
                    HouseholdId = HouseholdId,
                    Furnishings = MakeRegister(),
                    Inventory = fixture.Inventory,
                };
                var diag = new List<string>();
                int count = restored.RestoreBindings(saved, diag);

                Assert.AreEqual(0, count, "An asset that is no longer installed cannot re-bind.");
                Assert.AreEqual(0, restored.Bindings.Count);
                Assert.IsTrue(diag[diag.Count - 1].Contains("skipped 1 bindings"));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void RestoreBindings_IsIdempotent()
        {
            (StorageFixture fixture, FurnishingStorageBinding binding, int flourLot, int breadLot, List<FurnishingStorageBindingSaveDto> saved) = MakeBoundFixture();
            try
            {
                HouseholdFurnishingRegister restoredRegister = RoundTripRegister(fixture.Register);
                var restored = new FurnishingStorageRegistry
                {
                    HouseholdId = HouseholdId,
                    Furnishings = restoredRegister,
                    Inventory = fixture.Inventory,
                };
                restored.RefreshInventory(fixture.Inventory);
                var diag = new List<string>();

                Assert.AreEqual(1, restored.RestoreBindings(saved, diag));
                Assert.AreEqual(0, restored.RestoreBindings(saved, diag), "A second restore binds nothing new — already bound.");
                Assert.AreEqual(1, restored.Bindings.Count);
                Assert.AreEqual(2, restored.BindingForAsset("shelf-1").Zone.Placements.Count, "No duplicate placements.");
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void RestoreBindings_ZoneIdMismatch_DropsBinding()
        {
            (StorageFixture fixture, FurnishingStorageBinding binding, int flourLot, int breadLot, List<FurnishingStorageBindingSaveDto> saved) = MakeBoundFixture();
            try
            {
                saved[0].ZoneId = "wrong-zone";
                HouseholdFurnishingRegister restoredRegister = RoundTripRegister(fixture.Register);
                var restored = new FurnishingStorageRegistry
                {
                    HouseholdId = HouseholdId,
                    Furnishings = restoredRegister,
                    Inventory = fixture.Inventory,
                };
                restored.RefreshInventory(fixture.Inventory);
                var diag = new List<string>();
                int count = restored.RestoreBindings(saved, diag);

                Assert.AreEqual(0, count, "A saved zone id that no longer matches the derived zone id must not bind.");
                Assert.AreEqual(0, restored.Bindings.Count);
                Assert.IsTrue(diag[diag.Count - 1].Contains("skipped 1 bindings"));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void CaptureBindings_EmptyRegistryCapturesNothing()
        {
            StorageFixture fixture = MakeStorageFixture();
            try
            {
                Assert.AreEqual(0, fixture.Storage.CaptureBindings().Count);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void RestoreBindings_NullInputs_NeverThrow()
        {
            StorageFixture fixture = MakeStorageFixture();
            try
            {
                Assert.DoesNotThrow(() => fixture.Storage.RestoreBindings(null, null));
                Assert.DoesNotThrow(() => fixture.Storage.RestoreBindings(new List<FurnishingStorageBindingSaveDto>(), null));
                Assert.DoesNotThrow(() => fixture.Storage.RestoreBindings(
                    new List<FurnishingStorageBindingSaveDto> { null }, new List<string>()));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }
    }
}
