// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Lane 9f (F R-4, expanded campaign 2026-11-20): storage re-bind after
// Continue — the G-4 save seam (CaptureBindings / RestoreBindings) must
// re-bind a household's storage fixtures to the restored registers, so a
// just-loaded household reads its restored storage and the cabinet want
// does not fire on its stored lots. The bootstrapper calls
// RefreshInventory then RestoreBindings before presentations rebuild.
// This test pins the evaluator's input — StorageFixtures() — not the
// binding itself: G-4's Unity test already proves the binding round-trip
// (parameters, placed lots, idempotency, loud skips); what this lane
// adds is the proof that the Continue shape flips the storage-want
// input from empty to live. A cabinet that is installed but not yet
// bound is placed, not a storage fixture.

using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.GenericBusiness;
using LandLedgers.Economy.Storage;
using LandLedgers.Population;
using LandLedgers.World;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public class Lane9fStorageRebindTests
    {
        private const int HouseholdId = 7;
        private const int BuildingInstanceId = 12;

        private BuildingFootprintAuthority building;
        private List<BuildingEquipmentPlacement> placements;
        private HouseholdFurnishingRegister register;
        private HouseholdInventory inventory;

        [SetUp]
        public void SetUp()
        {
            building = new BuildingFootprintAuthority();
            placements = new List<BuildingEquipmentPlacement>();
            register = new HouseholdFurnishingRegister { HouseholdId = HouseholdId };
            inventory = new HouseholdInventory(HouseholdId);
        }

        private bool PlacementIsBound(string placementId)
        {
            if (string.IsNullOrWhiteSpace(placementId)) return false;
            if (register?.Bindings == null) return false;
            foreach (FurnishingPlacementBinding binding in register.Bindings)
            {
                if (binding != null && string.Equals(binding.BindingId, placementId, System.StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private BuildingEquipmentPlacement MakePlacement(string placementId, string definitionId)
        {
            var placement = new BuildingEquipmentPlacement();
            placement.AssociateWithBuilding(building);
            placement.Configure(placementId, definitionId, "Cabinet", 0);
            placements.Add(placement);
            return placement;
        }

        private static EquipmentAsset MakeCabinet(string assetId)
        {
            return new EquipmentAsset
            {
                AssetId = assetId,
                Kind = FurnitureItemSpecs.CabinetSmallId,
                DisplayName = FurnitureItemSpecs.CabinetSmallId,
                Condition01 = 1f,
                OwnerKind = "household",
                OwnerId = HouseholdId.ToString(),
            };
        }

        private FurnishingStorageRegistry MakeRegistry()
        {
            return new FurnishingStorageRegistry
            {
                HouseholdId = HouseholdId,
                Furnishings = register,
                Inventory = inventory,
            };
        }

        private FurnishingPlacementBinding InstallCabinet(string assetId)
        {
            MakePlacement("plc-cabinet", FurnitureItemSpecs.CabinetSmallId);
            EquipmentAsset asset = MakeCabinet(assetId);
            string refusal = FurnitureInstallBinding.TryInstallHouseholdAsset(
                register, asset, building, BuildingInstanceId, placements,
                PlacementIsBound, FurnishingAcquisitionSource.Purchase, "store-1",
                out FurnishingPlacementBinding binding);
            Assert.IsNull(refusal, "cabinet install should succeed: " + refusal);
            Assert.NotNull(binding);
            Assert.AreEqual(FurnishingCapabilityKind.StorageZone, binding.CapabilityKind);
            return binding;
        }

        [Test]
        public void RestoreBindings_AfterRefreshInventory_StorageFixturesGoLive()
        {
            InstallCabinet("cab-1");
            FurnishingStorageRegistry before = MakeRegistry();

            // Installed but not yet bound: placed, not a storage fixture.
            Assert.AreEqual(0, before.StorageFixtures().Count,
                "an installed cabinet with no storage binding is not a storage fixture.");

            string bindRefusal = before.TryBindFixture(
                "cab-1", StorageVisibility.Closed,
                StorageCapacityLimit.Known(50f), StorageCapacityLimit.Unknown,
                null, string.Empty, new List<string>(),
                FurnishingStorageBinding.DefaultDisplayMeshCapPerSku,
                out FurnishingStorageBinding bound);
            Assert.IsNull(bindRefusal, "cabinet bind should succeed: " + bindRefusal);
            Assert.NotNull(bound);
            Assert.AreEqual(1, before.StorageFixtures().Count);

            List<FurnishingStorageBindingSaveDto> saved = before.CaptureBindings();
            Assert.AreEqual(1, saved.Count);

            // Continue shape: a fresh storage registry (empty Bindings) on
            // the restored furnishing register and live inventory — the
            // placement binding survived via the register's own restore,
            // the storage binding did not yet.
            FurnishingStorageRegistry after = MakeRegistry();
            Assert.AreEqual(0, after.StorageFixtures().Count,
                "before RestoreBindings the just-loaded household reads as having no storage — the stale-comment state that fired the spurious cabinet want.");

            var diag = new List<string>();
            after.RefreshInventory(inventory);
            int restored = after.RestoreBindings(saved, diag);
            Assert.AreEqual(1, restored);
            Assert.AreEqual(1, diag.Count, "clean restore logs only its summary line: " + string.Join("; ", diag));
            Assert.IsTrue(diag[0].Contains("restored 1, skipped 0 bindings, skipped 0 placed lots"), "unexpected summary: " + diag[0]);
            Assert.AreEqual(1, after.StorageFixtures().Count,
                "after RestoreBindings the restored storage fixture must be live so the cabinet want does not fire.");
            Assert.AreEqual("cab-1", after.StorageFixtures()[0].AssetId);
            Assert.AreEqual(FurnishingCapabilityKind.StorageZone, after.StorageFixtures()[0].CapabilityKind);

            // Idempotent: a second restore binds nothing new.
            int restoredAgain = after.RestoreBindings(saved, diag);
            Assert.AreEqual(0, restoredAgain);
            Assert.AreEqual(1, after.StorageFixtures().Count);
        }

        [Test]
        public void RestoreBindings_UninstalledAsset_SkipsLoudlyAndLeavesNoFixture()
        {
            FurnishingStorageRegistry after = MakeRegistry();
            var saved = new List<FurnishingStorageBindingSaveDto>
            {
                new FurnishingStorageBindingSaveDto
                {
                    HouseholdId = HouseholdId,
                    AssetId = "cab-gone",
                    ZoneId = "storage-cab-gone",
                    BuildingInstanceId = BuildingInstanceId,
                    PlacementId = "plc-cabinet",
                    Visibility = StorageVisibility.Closed,
                    DisplayMeshCap = FurnishingStorageBinding.DefaultDisplayMeshCapPerSku,
                    MassCapacity = StorageCapacityLimit.Known(50f),
                    VolumeCapacity = StorageCapacityLimit.Unknown,
                },
            };
            var diag = new List<string>();
            after.RefreshInventory(inventory);
            int restored = after.RestoreBindings(saved, diag);
            Assert.AreEqual(0, restored);
            // The skipped binding is diagnosed by name, plus the summary line.
            Assert.AreEqual(2, diag.Count, "expected one skip line + the summary line: " + string.Join("; ", diag));
            Assert.IsTrue(diag[0].Contains("cab-gone"), "the skip line must name the skipped asset: " + diag[0]);
            Assert.IsTrue(diag[1].Contains("restored 0, skipped 1 bindings"), "unexpected summary: " + diag[1]);
            Assert.AreEqual(0, after.StorageFixtures().Count);
        }
    }
}
