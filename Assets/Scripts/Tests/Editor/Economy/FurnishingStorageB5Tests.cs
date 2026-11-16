using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Storage;
using LandLedgers.Population;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// B5 (Agent B, expanded campaign 2026-11-16): cabinet/shelf storage +
    /// display policy. Proves the storage binding (installed fixture to
    /// PhysicalStorageZone, capacity/category/item enforcement, one lot one
    /// place across zones, removal never touches the inventory) and the
    /// display policy (closed cabinets hide contents, shelves show only
    /// what the inventory authority holds, pooled per SKU, capped meshes,
    /// zero-stock SKUs display nothing, stale displays are detected).
    ///
    /// Measure function is test-local calibration ONLY: household item
    /// specs carry Unknown mass/volume in this tree ("no authored mass in
    /// source"), so the capacity tests measure with a local table.
    /// Production masses come from the item-spec authority when F wires
    /// them; the zone is measurement-agnostic, so the enforcement
    /// behaviour pinned here is what matters.
    ///
    /// No test claims Unity compilation or PlayMode; shelves have no
    /// tracked prefab in this checkout (Codex instruction 1), so live
    /// placement waits on Codex publishing them, and the per-tick storage
    /// driver is Agent G's wiring.
    /// </summary>
    [TestFixture]
    public sealed class FurnishingStorageB5Tests
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

        private static EquipmentAsset MakeBed(string assetId, float condition = 1f)
        {
            return new EquipmentAsset
            {
                AssetId = assetId,
                Kind = "furniture.bed",
                DisplayName = "Bed",
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

        /// <summary>
        /// Test-local measure table. See the class doc: production masses are not authored in this tree.
        /// The measure is the LOT's total load (the zone enforces the whole lot's load, not a per-unit
        /// figure), so each entry scales by the lot's live QuantityUnits.
        /// </summary>
        private static StorageLoadMeasurement MeasureLot(HouseholdItemLot lot)
        {
            switch (lot.ItemId)
            {
                case HouseholdItemCatalog.FlourId: return StorageLoadMeasurement.MassAndVolume(lot.QuantityUnits * 1f, lot.QuantityUnits * 0.002f);
                case HouseholdItemCatalog.FuelWoodId: return StorageLoadMeasurement.MassAndVolume(lot.QuantityUnits * 2f, lot.QuantityUnits * 0.004f);
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

        private static StorageFixture MakeStorageFixture(string buildingName = "B5 storage fixture")
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

        private static FurnishingDisplayAllocation AllocationFor(FurnishingStorageBinding binding, string skuId)
        {
            foreach (FurnishingDisplayAllocation allocation in binding.Allocations)
            {
                if (allocation != null && allocation.SkuId == skuId) return allocation;
            }
            return null;
        }

        private static void Consume(HouseholdInventory inventory, string itemId, int units)
        {
            var records = new List<HouseholdConsumptionRecord>();
            Assert.IsNull(inventory.Consume(itemId, units, Day, "test consumption", "test", records));
        }

        [Test]
        public void Cabinet_Closed_HidesContentsButStores()
        {
            var f = MakeStorageFixture();
            try
            {
                InstallStorageAsset(f, "cabinet-1", FurnishingStorageBinding.CabinetSmallAssetKind, new Vector3(1f, 0f, 0f));
                FurnishingStorageBinding binding = BindFixture(f.Storage, "cabinet-1", StorageVisibility.Closed);
                int lotId = AddLot(f.Inventory, HouseholdItemCatalog.FlourId, 5);
                Assert.IsNull(binding.TryPlaceLot(lotId));
                Assert.IsTrue(binding.ContainsLot(lotId), "the lot is physically in the cabinet");
                Assert.AreEqual(0, binding.Allocations.Count, "a closed cabinet displays nothing");
                Assert.AreEqual(0, f.Storage.Validate().Count, "validate is clean: hidden contents are not a problem");
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void Cabinet_Closed_StillEnforcesCapacity()
        {
            var f = MakeStorageFixture();
            try
            {
                InstallStorageAsset(f, "cabinet-1", FurnishingStorageBinding.CabinetSmallAssetKind, new Vector3(1f, 0f, 0f));
                FurnishingStorageBinding binding = BindFixture(f.Storage, "cabinet-1", StorageVisibility.Closed, massCapKg: 5f);
                int lotId = AddLot(f.Inventory, HouseholdItemCatalog.FlourId, 10); // 10 kg against a 5 kg cap.
                string error = binding.TryPlaceLot(lotId);
                Assert.IsNotNull(error, "over-capacity placement must refuse");
                Assert.IsTrue(error.Contains("Mass capacity exceeded"), "refusal names the mass cap, got: " + error);
                Assert.IsFalse(binding.ContainsLot(lotId), "nothing was placed");
                Assert.AreEqual(10, f.Inventory.GetTotalUnits(HouseholdItemCatalog.FlourId), "the inventory is untouched");
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void Shelf_OpenDisplay_ShowsWhatInventoryHolds()
        {
            var f = MakeStorageFixture();
            try
            {
                InstallStorageAsset(f, "shelf-1", FurnishingStorageBinding.ShelfAssetKind, new Vector3(1f, 0f, 0f));
                FurnishingStorageBinding binding = BindFixture(f.Storage, "shelf-1", StorageVisibility.OpenDisplay);
                int lotId = AddLot(f.Inventory, HouseholdItemCatalog.FlourId, 5);
                Assert.IsNull(binding.TryPlaceLot(lotId));
                FurnishingDisplayAllocation allocation = AllocationFor(binding, HouseholdItemCatalog.FlourId);
                Assert.NotNull(allocation, "flour on the shelf is allocated a display");
                Assert.AreEqual(5, allocation.DisplayedUnits, "display shows the live unit count");
                Assert.AreEqual(5, allocation.MeshCount, "5 units need 5 representative meshes");
                Assert.AreEqual(0, f.Storage.Validate().Count);
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void Shelf_Display_NeverExceedsActualUnits()
        {
            var f = MakeStorageFixture();
            try
            {
                InstallStorageAsset(f, "shelf-1", FurnishingStorageBinding.ShelfAssetKind, new Vector3(1f, 0f, 0f));
                FurnishingStorageBinding binding = BindFixture(f.Storage, "shelf-1", StorageVisibility.OpenDisplay);
                int lotId = AddLot(f.Inventory, HouseholdItemCatalog.FlourId, 5);
                Assert.IsNull(binding.TryPlaceLot(lotId));
                Consume(f.Inventory, HouseholdItemCatalog.FlourId, 2); // 5 -> 3, display not yet recomputed.
                Assert.AreEqual(1, f.Storage.Validate().Count, "stale display is detected, not silently kept");
                Assert.IsTrue(f.Storage.Validate()[0].Contains("stale"), "the problem names staleness, got: " + f.Storage.Validate()[0]);
                f.Storage.RecomputeDisplays();
                FurnishingDisplayAllocation allocation = AllocationFor(binding, HouseholdItemCatalog.FlourId);
                Assert.NotNull(allocation);
                Assert.AreEqual(3, allocation.DisplayedUnits, "display follows the inventory down");
                Assert.AreEqual(3, allocation.MeshCount);
                Assert.AreEqual(0, f.Storage.Validate().Count, "recompute clears the staleness problem");
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void Shelf_Display_CapsMeshesAtTwelve()
        {
            var f = MakeStorageFixture();
            try
            {
                InstallStorageAsset(f, "shelf-1", FurnishingStorageBinding.ShelfAssetKind, new Vector3(1f, 0f, 0f));
                FurnishingStorageBinding binding = BindFixture(f.Storage, "shelf-1", StorageVisibility.OpenDisplay, massCapKg: 500f);
                int lotId = AddLot(f.Inventory, HouseholdItemCatalog.FlourId, 100);
                Assert.IsNull(binding.TryPlaceLot(lotId));
                FurnishingDisplayAllocation allocation = AllocationFor(binding, HouseholdItemCatalog.FlourId);
                Assert.NotNull(allocation);
                Assert.AreEqual(100, allocation.DisplayedUnits, "the display still counts every unit on hand");
                Assert.AreEqual(12, allocation.MeshCount, "but renders at most the O-5 cap of 12 meshes per SKU");
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void Shelf_ZeroStockSku_DisplaysNothing()
        {
            var f = MakeStorageFixture();
            try
            {
                InstallStorageAsset(f, "shelf-1", FurnishingStorageBinding.ShelfAssetKind, new Vector3(1f, 0f, 0f));
                FurnishingStorageBinding binding = BindFixture(f.Storage, "shelf-1", StorageVisibility.OpenDisplay);
                int lotId = AddLot(f.Inventory, HouseholdItemCatalog.FlourId, 10);
                Assert.IsNull(binding.TryPlaceLot(lotId));
                Assert.NotNull(AllocationFor(binding, HouseholdItemCatalog.FlourId));
                Consume(f.Inventory, HouseholdItemCatalog.FlourId, 10); // the lot is consumed to zero and removed.
                f.Storage.RecomputeDisplays();
                Assert.IsNull(AllocationFor(binding, HouseholdItemCatalog.FlourId),
                    "a zero-stock SKU gets no allocation at all — the shelf never shows an absent product");
                Assert.AreEqual(0, binding.Allocations.Count);
                Assert.AreEqual(1, f.Storage.Validate().Count,
                    "the placement is now stale — the lot is gone, and Validate flags it until pruned");
                Assert.AreEqual(1, f.Storage.PruneStalePlacements(), "pruning removes the stale placement");
                Assert.AreEqual(0, f.Storage.Validate().Count, "pruning clears the problem");
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void Shelf_Display_PoolsAcrossLots()
        {
            var f = MakeStorageFixture();
            try
            {
                InstallStorageAsset(f, "shelf-1", FurnishingStorageBinding.ShelfAssetKind, new Vector3(1f, 0f, 0f));
                FurnishingStorageBinding binding = BindFixture(f.Storage, "shelf-1", StorageVisibility.OpenDisplay);
                int lotA = AddLot(f.Inventory, HouseholdItemCatalog.FlourId, 5);
                int lotB = AddLot(f.Inventory, HouseholdItemCatalog.FlourId, 3);
                Assert.AreNotEqual(lotA, lotB, "two purchases are two lots");
                Assert.IsNull(binding.TryPlaceLot(lotA));
                Assert.IsNull(binding.TryPlaceLot(lotB));
                Assert.AreEqual(1, binding.Allocations.Count, "one SKU, one pooled allocation");
                FurnishingDisplayAllocation allocation = AllocationFor(binding, HouseholdItemCatalog.FlourId);
                Assert.AreEqual(8, allocation.DisplayedUnits, "the pooled display counts both lots");
                Assert.AreEqual(8, allocation.MeshCount);
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void LotThatDoesNotResolve_IsRefused()
        {
            var f = MakeStorageFixture();
            try
            {
                InstallStorageAsset(f, "shelf-1", FurnishingStorageBinding.ShelfAssetKind, new Vector3(1f, 0f, 0f));
                FurnishingStorageBinding binding = BindFixture(f.Storage, "shelf-1", StorageVisibility.OpenDisplay);
                string error = binding.TryPlaceLot(9999);
                Assert.IsNotNull(error, "a lot that is not in the inventory must refuse");
                Assert.IsTrue(error.Contains("does not resolve"), "refusal names the resolution failure, got: " + error);
                Assert.AreEqual(0, binding.Zone.Placements.Count);
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void SameLot_CannotBePlacedInTwoZones()
        {
            var f = MakeStorageFixture();
            try
            {
                InstallStorageAsset(f, "shelf-1", FurnishingStorageBinding.ShelfAssetKind, new Vector3(1f, 0f, 0f));
                InstallStorageAsset(f, "cabinet-1", FurnishingStorageBinding.CabinetSmallAssetKind, new Vector3(-1f, 0f, 0f));
                FurnishingStorageBinding shelf = BindFixture(f.Storage, "shelf-1", StorageVisibility.OpenDisplay);
                FurnishingStorageBinding cabinet = BindFixture(f.Storage, "cabinet-1", StorageVisibility.Closed);
                int lotId = AddLot(f.Inventory, HouseholdItemCatalog.FlourId, 5);
                Assert.IsNull(f.Storage.TryPlaceLot(shelf.ZoneId, lotId));
                string error = f.Storage.TryPlaceLot(cabinet.ZoneId, lotId);
                Assert.IsNotNull(error, "the same lot cannot be placed in two zones");
                Assert.IsTrue(error.Contains(shelf.ZoneId), "refusal names the zone already holding the lot, got: " + error);
                Assert.IsFalse(cabinet.ContainsLot(lotId));
                Assert.AreEqual(0, f.Storage.Validate().Count);
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void RemoveLot_KeepsInventoryStock()
        {
            var f = MakeStorageFixture();
            try
            {
                InstallStorageAsset(f, "shelf-1", FurnishingStorageBinding.ShelfAssetKind, new Vector3(1f, 0f, 0f));
                FurnishingStorageBinding binding = BindFixture(f.Storage, "shelf-1", StorageVisibility.OpenDisplay);
                int lotId = AddLot(f.Inventory, HouseholdItemCatalog.FlourId, 10);
                Assert.IsNull(binding.TryPlaceLot(lotId));
                Assert.NotNull(AllocationFor(binding, HouseholdItemCatalog.FlourId));
                Assert.IsTrue(binding.RemoveLot(lotId), "removal reports success");
                Assert.IsFalse(binding.ContainsLot(lotId), "the placement is gone");
                Assert.AreEqual(10, f.Inventory.GetTotalUnits(HouseholdItemCatalog.FlourId),
                    "removing a lot from a zone does not change the inventory authority's stock");
                Assert.AreEqual(0, binding.Allocations.Count, "the display follows the removal");
                Assert.IsFalse(binding.RemoveLot(lotId), "removing twice reports nothing removed");
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void CategoryRestriction_BarrelAdmitsOnlyFuelWood()
        {
            var f = MakeStorageFixture();
            try
            {
                InstallStorageAsset(f, "barrel-1", FurnishingStorageBinding.BarrelAssetKind, new Vector3(1f, 0f, 0f));
                FurnishingStorageBinding binding = BindFixture(f.Storage, "barrel-1", StorageVisibility.Closed,
                    allowedReserveCategoryId: "fuel_wood");
                int flourLot = AddLot(f.Inventory, HouseholdItemCatalog.FlourId, 5);
                string error = binding.TryPlaceLot(flourLot);
                Assert.IsNotNull(error, "flour (staple_food) must refuse a fuel_wood-only barrel");
                Assert.IsTrue(error.Contains("fuel_wood"), "refusal names the restriction, got: " + error);
                int woodLot = AddLot(f.Inventory, HouseholdItemCatalog.FuelWoodId, 4);
                Assert.IsNull(binding.TryPlaceLot(woodLot), "fuel wood is admitted");
                Assert.IsTrue(binding.ContainsLot(woodLot));
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void ItemRestriction_ShelfAdmitsOnlyListedItems()
        {
            var f = MakeStorageFixture();
            try
            {
                InstallStorageAsset(f, "shelf-1", FurnishingStorageBinding.ShelfAssetKind, new Vector3(1f, 0f, 0f));
                FurnishingStorageBinding binding = BindFixture(f.Storage, "shelf-1", StorageVisibility.OpenDisplay,
                    allowedItemIds: new List<string> { HouseholdItemCatalog.FlourId });
                int breadLot = AddLot(f.Inventory, HouseholdItemCatalog.BreadId, 5);
                string error = binding.TryPlaceLot(breadLot);
                Assert.IsNotNull(error, "bread is not on the allowed-item list");
                Assert.IsTrue(error.Contains("allowed-item list"), "refusal names the list, got: " + error);
                int flourLot = AddLot(f.Inventory, HouseholdItemCatalog.FlourId, 5);
                Assert.IsNull(binding.TryPlaceLot(flourLot), "flour is admitted");
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void Bind_UninstalledAsset_IsRefused()
        {
            var f = MakeStorageFixture();
            try
            {
                Assert.IsNull(f.Register.TryRegisterAsset(MakeStorageAsset("cabinet-1", FurnishingStorageBinding.CabinetSmallAssetKind)));
                string error = f.Storage.TryBindFixture("cabinet-1", StorageVisibility.Closed,
                    StorageCapacityLimit.Known(40f), StorageCapacityLimit.Known(0.5f),
                    MeasureLot, "", null, FurnishingStorageBinding.DefaultDisplayMeshCapPerSku,
                    out FurnishingStorageBinding binding);
                Assert.IsNotNull(error, "a cabinet that is not installed cannot be bound");
                Assert.IsTrue(error.Contains("no live placement binding"), "refusal names the missing install, got: " + error);
                Assert.IsNull(binding);
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void Bind_NonStorageKind_IsRefused()
        {
            var f = MakeStorageFixture();
            try
            {
                Assert.IsNull(f.Register.TryRegisterAsset(MakeBed("bed-1")));
                BuildingEquipmentPlacement placement = MakePlacement("bed-1", new Vector3(1f, 0f, 0f));
                f.ItemObjects.Add(placement.gameObject);
                BuildingFootprintAuthority building = f.BuildingObject.GetComponent<BuildingFootprintAuthority>();
                InstallFixture(f.Register, building, placement, "bed-1");
                string error = f.Storage.TryBindFixture("bed-1", StorageVisibility.Closed,
                    StorageCapacityLimit.Known(40f), StorageCapacityLimit.Known(0.5f),
                    MeasureLot, "", null, FurnishingStorageBinding.DefaultDisplayMeshCapPerSku,
                    out FurnishingStorageBinding binding);
                Assert.IsNotNull(error, "a bed is not a storage fixture");
                Assert.IsTrue(error.Contains("not a storage fixture"), "refusal names the kind, got: " + error);
                Assert.IsNull(binding);
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void Bind_AlreadyBound_IsRefused()
        {
            var f = MakeStorageFixture();
            try
            {
                InstallStorageAsset(f, "cabinet-1", FurnishingStorageBinding.CabinetSmallAssetKind, new Vector3(1f, 0f, 0f));
                BindFixture(f.Storage, "cabinet-1", StorageVisibility.Closed);
                string error = f.Storage.TryBindFixture("cabinet-1", StorageVisibility.Closed,
                    StorageCapacityLimit.Known(40f), StorageCapacityLimit.Known(0.5f),
                    MeasureLot, "", null, FurnishingStorageBinding.DefaultDisplayMeshCapPerSku,
                    out FurnishingStorageBinding binding);
                Assert.IsNotNull(error, "one fixture has at most one storage binding");
                Assert.IsTrue(error.Contains("already bound"), "refusal names the existing binding, got: " + error);
                Assert.IsNull(binding);
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void Bind_NoKnownCapacity_IsRefused()
        {
            var f = MakeStorageFixture();
            try
            {
                InstallStorageAsset(f, "cabinet-1", FurnishingStorageBinding.CabinetSmallAssetKind, new Vector3(1f, 0f, 0f));
                string error = f.Storage.TryBindFixture("cabinet-1", StorageVisibility.Closed,
                    StorageCapacityLimit.Unknown, StorageCapacityLimit.Unknown,
                    MeasureLot, "", null, FurnishingStorageBinding.DefaultDisplayMeshCapPerSku,
                    out FurnishingStorageBinding binding);
                Assert.IsNotNull(error, "a zone with no enforced axis is not a zone");
                Assert.IsTrue(error.Contains("at least one capacity axis"), "refusal names the missing axis, got: " + error);
                Assert.IsNull(binding);
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void UnknownMeasure_OnBoundedAxis_IsRefused()
        {
            var f = MakeStorageFixture();
            try
            {
                InstallStorageAsset(f, "shelf-1", FurnishingStorageBinding.ShelfAssetKind, new Vector3(1f, 0f, 0f));
                string error = f.Storage.TryBindFixture("shelf-1", StorageVisibility.OpenDisplay,
                    StorageCapacityLimit.Known(40f), StorageCapacityLimit.Known(0.5f),
                    null, "", null, FurnishingStorageBinding.DefaultDisplayMeshCapPerSku,
                    out FurnishingStorageBinding binding);
                Assert.IsNull(error, error);
                int lotId = AddLot(f.Inventory, HouseholdItemCatalog.FlourId, 5);
                error = binding.TryPlaceLot(lotId);
                Assert.IsNotNull(error, "an unmeasured lot must refuse on a bounded axis rather than assume a size");
                Assert.IsTrue(error.Contains("unknown"), "refusal names the unknown measure, got: " + error);
                Assert.IsFalse(binding.ContainsLot(lotId));
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void Prune_RemovesPlacementsWhoseLotsAreGone()
        {
            var f = MakeStorageFixture();
            try
            {
                InstallStorageAsset(f, "shelf-1", FurnishingStorageBinding.ShelfAssetKind, new Vector3(1f, 0f, 0f));
                FurnishingStorageBinding binding = BindFixture(f.Storage, "shelf-1", StorageVisibility.OpenDisplay);
                int lotId = AddLot(f.Inventory, HouseholdItemCatalog.FlourId, 5);
                Assert.IsNull(binding.TryPlaceLot(lotId));
                Consume(f.Inventory, HouseholdItemCatalog.FlourId, 5); // Consume removes the emptied lot.
                Assert.AreEqual(0, f.Inventory.Lots.Count, "the lot is gone from the inventory");
                f.Storage.RecomputeDisplays(); // the display follows the inventory; the stale placement still needs pruning.
                Assert.AreEqual(1, f.Storage.Validate().Count, "the stale placement is flagged before pruning");
                Assert.AreEqual(1, f.Storage.PruneStalePlacements(), "exactly one stale placement is pruned");
                Assert.IsFalse(binding.ContainsLot(lotId));
                Assert.AreEqual(0, binding.Allocations.Count);
                Assert.AreEqual(0, f.Storage.Validate().Count, "pruning clears the problem");
                Assert.AreEqual(0, f.Storage.PruneStalePlacements(), "pruning again removes nothing");
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void Validate_FlagsOrphanedBindingAndUnzonedFixture()
        {
            var f = MakeStorageFixture();
            try
            {
                InstallStorageAsset(f, "cabinet-1", FurnishingStorageBinding.CabinetSmallAssetKind, new Vector3(1f, 0f, 0f));
                Assert.AreEqual(1, f.Storage.Validate().Count,
                    "an installed cabinet with no storage zone is flagged — placed, not stored");
                Assert.IsTrue(f.Storage.Validate()[0].Contains("has no storage zone"), "got: " + f.Storage.Validate()[0]);
                FurnishingStorageBinding binding = BindFixture(f.Storage, "cabinet-1", StorageVisibility.Closed);
                Assert.AreEqual(0, f.Storage.Validate().Count, "binding the cabinet clears the flag");
                Assert.IsNull(f.Register.TryRemove("cabinet-1"), "B0 removal ends the placement binding");
                List<string> problems = f.Storage.Validate();
                Assert.AreEqual(1, problems.Count, "the orphaned zone is flagged after removal");
                Assert.IsTrue(problems[0].Contains("orphaned"), "got: " + problems[0]);
                Assert.IsTrue(f.Storage.DropBindingForAsset("cabinet-1"), "dropping the orphaned binding succeeds");
                Assert.AreEqual(0, f.Storage.Validate().Count, "dropping the binding clears the flag");
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void StorageFixtures_ListsBoundFixturesOnly()
        {
            var f = MakeStorageFixture();
            try
            {
                InstallStorageAsset(f, "cabinet-1", FurnishingStorageBinding.CabinetSmallAssetKind, new Vector3(1f, 0f, 0f));
                InstallStorageAsset(f, "cabinet-2", FurnishingStorageBinding.CabinetLargeAssetKind, new Vector3(-1f, 0f, 0f));
                BindFixture(f.Storage, "cabinet-1", StorageVisibility.Closed);
                List<FurnishingPlacementBinding> fixtures = f.Storage.StorageFixtures();
                Assert.AreEqual(1, fixtures.Count, "only the bound cabinet is a storage fixture");
                Assert.AreEqual("cabinet-1", fixtures[0].AssetId);
                Assert.AreEqual(FurnishingCapabilityKind.StorageZone, fixtures[0].CapabilityKind);
                Assert.IsTrue(f.Storage.DropBindingForAsset("cabinet-1"));
                Assert.AreEqual(0, f.Storage.StorageFixtures().Count, "dropping the binding unlists the fixture");
                Assert.AreEqual(FurnishingCapabilityKind.None,
                    f.Register.LiveBindingFor("cabinet-1").CapabilityKind,
                    "dropping clears the storage stamp on the B0 binding");
                Assert.IsFalse(f.Storage.DropBindingForAsset("cabinet-1"), "dropping twice reports nothing dropped");
            }
            finally { DestroyFixture(f); }
        }

        [Test]
        public void Conditional_BehavesLikeClosedForDisplay()
        {
            var f = MakeStorageFixture();
            try
            {
                InstallStorageAsset(f, "shelf-1", FurnishingStorageBinding.ShelfAssetKind, new Vector3(1f, 0f, 0f));
                FurnishingStorageBinding binding = BindFixture(f.Storage, "shelf-1", StorageVisibility.Conditional);
                int lotId = AddLot(f.Inventory, HouseholdItemCatalog.FlourId, 5);
                Assert.IsNull(binding.TryPlaceLot(lotId), "conditional fixtures still store");
                Assert.IsTrue(binding.ContainsLot(lotId));
                Assert.AreEqual(0, binding.Allocations.Count,
                    "conditional contents stay hidden until an inspection interaction exists");
                Assert.AreEqual(0, f.Storage.Validate().Count);
            }
            finally { DestroyFixture(f); }
        }
    }
}
