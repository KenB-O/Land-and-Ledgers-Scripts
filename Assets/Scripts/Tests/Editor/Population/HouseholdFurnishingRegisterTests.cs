using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Logistics;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// B0 (Agent B, expanded campaign 2026-11-16): household furnishing
    /// register + placement binding. Proves the foundation slice only —
    /// register CRUD and ownership enforcement, install/remove/move
    /// round-trips with exactly one asset record and at most one live
    /// binding, unauthorised-placement reporting, condition-weighted
    /// valuation, and the Pass 9 save/load round-trip. No test claims
    /// Unity compilation or PlayMode.
    /// </summary>
    [TestFixture]
    public sealed class HouseholdFurnishingRegisterTests
    {
        private const int HouseholdId = 7;
        private const int OtherHouseholdId = 8;
        private const int BuildingInstanceId = 101;
        private const int OtherBuildingInstanceId = 102;

        private static HouseholdFurnishingRegister MakeRegister(int householdId = HouseholdId)
        {
            return new HouseholdFurnishingRegister { HouseholdId = householdId };
        }

        private static EquipmentAsset MakeBed(string assetId, int householdId, float condition = 1f)
        {
            return new EquipmentAsset
            {
                AssetId = assetId,
                Kind = "furniture.bed",
                DisplayName = "Bed",
                Condition01 = condition,
                OwnerKind = "household",
                OwnerId = householdId.ToString(),
            };
        }

        private static EquipmentAsset MakeCabinet(string assetId, int householdId, float condition = 1f)
        {
            return new EquipmentAsset
            {
                AssetId = assetId,
                Kind = "furniture.cabinet-small",
                DisplayName = "Small Cabinet",
                Condition01 = condition,
                OwnerKind = "household",
                OwnerId = householdId.ToString(),
            };
        }

        private static int BedCost(string kind) => kind == "furniture.bed" ? 10000 : 1000;

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
            // Harness note: GameObject.CreatePrimitive / PrimitiveType do not
            // exist in the Roslyn shim, and the shim's
            // Component.GetComponentsInChildren<Renderer> returns empty, so
            // renderer-based obstruction is not expressible here. Placement
            // obstruction proof is a Codex Unity job; these tests prove the
            // binding, ownership and validation contract instead.
            GameObject item = new(name);
            item.transform.position = position;
            return item.AddComponent<BuildingEquipmentPlacement>();
        }

        private static HouseholdFurnishingRegister RegisterWithBed(string assetId = "bed-1")
        {
            var register = MakeRegister();
            Assert.IsNull(register.TryRegisterAsset(MakeBed(assetId, HouseholdId)));
            return register;
        }

        private static FurnishingPlacementBinding InstallBed(
            HouseholdFurnishingRegister register,
            BuildingFootprintAuthority building,
            BuildingEquipmentPlacement placement,
            int buildingInstanceId = BuildingInstanceId)
        {
            string error = register.TryInstall("bed-1", placement, building, buildingInstanceId, 0,
                FurnishingCapabilityKind.None, string.Empty,
                FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                out FurnishingPlacementBinding binding);
            Assert.IsNull(error, error);
            Assert.NotNull(binding);
            return binding;
        }

        [Test]
        public void RegisterAsset_AcceptsHouseholdOwnedAsset()
        {
            var register = MakeRegister();
            Assert.IsNull(register.TryRegisterAsset(MakeBed("bed-1", HouseholdId)));
            Assert.AreEqual(1, register.Assets.Count);
            Assert.AreEqual(0, register.Validate().Count, string.Join("; ", register.Validate()));
        }

        [Test]
        public void RegisterAsset_DeduplicatesByAssetId()
        {
            var register = MakeRegister();
            Assert.IsNull(register.TryRegisterAsset(MakeBed("bed-1", HouseholdId, 1f)));
            Assert.IsNull(register.TryRegisterAsset(MakeBed("bed-1", HouseholdId, 0.4f)));
            Assert.AreEqual(1, register.Assets.Count);
            Assert.AreEqual(0.4f, register.FindAsset("bed-1").Condition01);
        }

        [Test]
        public void RegisterAsset_RefusesAssetOwnedByAnotherHousehold()
        {
            var first = MakeRegister(HouseholdId);
            var second = MakeRegister(OtherHouseholdId);
            EquipmentAsset bed = MakeBed("bed-1", HouseholdId);
            Assert.IsNull(first.TryRegisterAsset(bed));
            string error = second.TryRegisterAsset(bed);
            Assert.IsNotNull(error, "Two households cannot own the same asset id.");
            Assert.AreEqual(0, second.Assets.Count);

            var businessOwned = new EquipmentAsset
            {
                AssetId = "plow-1", Kind = "moldboard-plow", Condition01 = 1f,
                OwnerKind = "business", OwnerId = "biz-1",
            };
            Assert.IsNotNull(first.TryRegisterAsset(businessOwned));
            Assert.AreEqual(1, first.Assets.Count);
        }

        [Test]
        public void RegisterAsset_RefusesNullOrBlankId()
        {
            var register = MakeRegister();
            Assert.IsNotNull(register.TryRegisterAsset(null));
            Assert.IsNotNull(register.TryRegisterAsset(new EquipmentAsset { AssetId = "", OwnerKind = "household", OwnerId = "7" }));
            Assert.AreEqual(0, register.Assets.Count);
        }

        [Test]
        public void Register_TransferableValue_IsConditionWeighted()
        {
            var register = MakeRegister();
            Assert.IsNull(register.TryRegisterAsset(MakeBed("bed-1", HouseholdId, 1f)));
            Assert.IsNull(register.TryRegisterAsset(MakeCabinet("cab-1", HouseholdId, 0.3f)));
            Assert.AreEqual(10300, register.TransferableValueCents(BedCost));
            Assert.AreEqual(0, MakeRegister().TransferableValueCents(BedCost));

            var wrecked = MakeRegister();
            Assert.IsNull(wrecked.TryRegisterAsset(MakeBed("bed-9", HouseholdId, 0.02f)));
            Assert.AreEqual(0, wrecked.TransferableValueCents(BedCost),
                "A wrecked bed is not a floor contributor (Canon §11.6).");
        }

        [Test]
        public void Register_ReplacementNeed_OnlyForWornEquipment()
        {
            var register = MakeRegister();
            Assert.IsNull(register.TryRegisterAsset(MakeBed("bed-1", HouseholdId, 1f)));
            Assert.AreEqual(0, register.ReplacementNeedCents(BedCost));
            Assert.IsNull(register.TryRegisterAsset(MakeCabinet("cab-1", HouseholdId, 0.3f)));
            Assert.AreEqual(700, register.ReplacementNeedCents(BedCost),
                "Only the worn cabinet needs replacement capital (Canon §11.5).");
        }

        [Test]
        public void Install_CreatesExactlyOneBindingAndPopulatesPlacement()
        {
            GameObject buildingObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B0 install building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", Vector3.zero);
                itemObject = placement.gameObject;
                var register = RegisterWithBed();

                FurnishingPlacementBinding binding = InstallBed(register, building, placement);

                Assert.AreEqual(1, register.Bindings.Count);
                Assert.AreEqual(1, register.Assets.Count, "Install must never duplicate the asset.");
                Assert.AreEqual("bed-1", binding.AssetId);
                Assert.AreEqual(BuildingInstanceId, binding.BuildingInstanceId);
                Assert.AreEqual(placement.PlacementId, binding.PlacementId);
                Assert.AreEqual(binding.BindingId, placement.PlacementId, "The placement id is the stable binding id.");
                Assert.AreEqual(0, binding.FloorIndex);
                Assert.AreEqual(Vector3.zero, binding.LocalPosition);
                Assert.AreEqual("furniture.bed", binding.DefinitionId);
                Assert.AreEqual(FurnishingAcquisitionSource.ScenarioEndowment, binding.AcquisitionSource);
                Assert.AreEqual("endowment-qa-town-1", binding.AcquisitionSourceId);
                Assert.AreEqual("7", placement.EconomicOwnerId);
                Assert.AreEqual("endowment-qa-town-1", placement.AcquisitionSourceId);
                Assert.AreEqual("building:101", register.FindAsset("bed-1").LocationId);
                Assert.AreEqual(0, register.Validate().Count, string.Join("; ", register.Validate()));
                // Obstruction of the installed point is NOT asserted here:
                // the harness shim returns no renderers, so
                // BlocksInteriorPoint is always false under the harness.
                // Unity-side obstruction proof is Codex's job.
            }
            finally
            {
                if (itemObject != null) Object.DestroyImmediate(itemObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void Install_RefusesUnregisteredAsset()
        {
            GameObject buildingObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B0 refusal building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", Vector3.zero);
                itemObject = placement.gameObject;
                var register = MakeRegister();
                string error = register.TryInstall("bed-1", placement, building, BuildingInstanceId, 0,
                    FurnishingCapabilityKind.None, string.Empty,
                    FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                    out FurnishingPlacementBinding binding);
                Assert.IsNotNull(error);
                Assert.IsNull(binding);
                Assert.AreEqual(0, register.Bindings.Count);
                Assert.AreEqual(string.Empty, placement.EconomicOwnerId, "A refused install must not stamp ownership.");
            }
            finally
            {
                if (itemObject != null) Object.DestroyImmediate(itemObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void Install_RefusesUnspecifiedAcquisitionSource()
        {
            GameObject buildingObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B0 source building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", Vector3.zero);
                itemObject = placement.gameObject;
                var register = RegisterWithBed();
                string error = register.TryInstall("bed-1", placement, building, BuildingInstanceId, 0,
                    FurnishingCapabilityKind.None, string.Empty,
                    FurnishingAcquisitionSource.Unspecified, string.Empty,
                    out FurnishingPlacementBinding binding);
                Assert.IsNotNull(error, "Physical placement is never proof of ownership.");
                Assert.IsNull(binding);
                Assert.AreEqual(0, register.Bindings.Count);
            }
            finally
            {
                if (itemObject != null) Object.DestroyImmediate(itemObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void Install_RefusesSecondInstallOfBoundAsset()
        {
            GameObject buildingObject = null;
            GameObject firstObject = null;
            GameObject secondObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B0 double-install building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement first = MakePlacement("Bed", Vector3.zero);
                firstObject = first.gameObject;
                BuildingEquipmentPlacement second = MakePlacement("Bed 2", new Vector3(2f, 0f, 0f));
                secondObject = second.gameObject;
                var register = RegisterWithBed();
                InstallBed(register, building, first);
                string error = register.TryInstall("bed-1", second, building, BuildingInstanceId, 0,
                    FurnishingCapabilityKind.None, string.Empty,
                    FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                    out FurnishingPlacementBinding binding);
                Assert.IsNotNull(error, "One asset has at most one live binding — use move, not a second install.");
                Assert.IsNull(binding);
                Assert.AreEqual(1, register.Bindings.Count);
            }
            finally
            {
                if (secondObject != null) Object.DestroyImmediate(secondObject);
                if (firstObject != null) Object.DestroyImmediate(firstObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void Install_RefusesUnwalkablePoint()
        {
            GameObject buildingObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B0 unwalkable building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", new Vector3(10f, 0f, 0f));
                itemObject = placement.gameObject;
                var register = RegisterWithBed();
                string error = register.TryInstall("bed-1", placement, building, BuildingInstanceId, 0,
                    FurnishingCapabilityKind.None, string.Empty,
                    FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                    out FurnishingPlacementBinding binding);
                Assert.IsNotNull(error, "A point outside the walkable floor must refuse install.");
                Assert.IsNull(binding);
                Assert.AreEqual(0, register.Bindings.Count);
                Assert.AreEqual(string.Empty, placement.EconomicOwnerId);
            }
            finally
            {
                if (itemObject != null) Object.DestroyImmediate(itemObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void Install_PreAssociatedPlacementSucceedsWithoutPointCheck()
        {
            GameObject buildingObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B0 pre-associated building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", Vector3.zero);
                itemObject = placement.gameObject;
                placement.AssociateWithBuilding(building);
                var register = RegisterWithBed();
                FurnishingPlacementBinding binding = InstallBed(register, building, placement);
                Assert.AreEqual(1, register.Bindings.Count);
                Assert.AreEqual(Vector3.zero, binding.LocalPosition);
                Assert.AreEqual("7", placement.EconomicOwnerId);
                // The pre-associated branch skips the point walkability
                // check (the item's own footprint legitimately occupies
                // its point). Under the harness the check would pass
                // anyway (no renderers), so this test proves the
                // pre-associated install path works; the skip itself is
                // Unity-verified by Codex.
            }
            finally
            {
                if (itemObject != null) Object.DestroyImmediate(itemObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void Install_RefusesPlacementOfAnotherBuilding()
        {
            GameObject firstObject = null;
            GameObject secondObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority first = MakeBuilding("B0 building A");
                firstObject = first.gameObject;
                BuildingFootprintAuthority second = MakeBuilding("B0 building B");
                secondObject = second.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", Vector3.zero);
                itemObject = placement.gameObject;
                placement.AssociateWithBuilding(first);
                var register = RegisterWithBed();
                string error = register.TryInstall("bed-1", placement, second, OtherBuildingInstanceId, 0,
                    FurnishingCapabilityKind.None, string.Empty,
                    FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                    out FurnishingPlacementBinding binding);
                Assert.IsNotNull(error);
                Assert.IsNull(binding);
                Assert.AreEqual(0, register.Bindings.Count);
            }
            finally
            {
                if (itemObject != null) Object.DestroyImmediate(itemObject);
                if (secondObject != null) Object.DestroyImmediate(secondObject);
                if (firstObject != null) Object.DestroyImmediate(firstObject);
            }
        }

        [Test]
        public void Move_EndsOldBindingAndCreatesNewOneWithSameAsset()
        {
            GameObject firstObject = null;
            GameObject secondObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority first = MakeBuilding("B0 move building A");
                firstObject = first.gameObject;
                BuildingFootprintAuthority second = MakeBuilding("B0 move building B");
                secondObject = second.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", Vector3.zero);
                itemObject = placement.gameObject;
                var register = RegisterWithBed();
                FurnishingPlacementBinding oldBinding = InstallBed(register, first, placement);

                placement.AssociateWithBuilding(second);
                string error = register.TryMove("bed-1", placement, second, OtherBuildingInstanceId, 0,
                    out FurnishingPlacementBinding newBinding);
                Assert.IsNull(error, error);
                Assert.NotNull(newBinding);
                Assert.AreNotEqual(oldBinding.BindingId, newBinding.BindingId, "A move ends the old binding and creates a new one.");
                Assert.AreEqual("bed-1", newBinding.AssetId);
                Assert.AreEqual(OtherBuildingInstanceId, newBinding.BuildingInstanceId);
                Assert.AreEqual(FurnishingAcquisitionSource.ScenarioEndowment, newBinding.AcquisitionSource,
                    "A move does not re-acquire the item.");
                Assert.AreEqual("endowment-qa-town-1", newBinding.AcquisitionSourceId);
                Assert.AreEqual(1, register.Bindings.Count);
                Assert.AreEqual(1, register.Assets.Count, "A move must never duplicate the asset.");
                Assert.AreEqual("building:102", register.FindAsset("bed-1").LocationId);
                Assert.AreEqual(0, register.Validate().Count, string.Join("; ", register.Validate()));
            }
            finally
            {
                if (itemObject != null) Object.DestroyImmediate(itemObject);
                if (secondObject != null) Object.DestroyImmediate(secondObject);
                if (firstObject != null) Object.DestroyImmediate(firstObject);
            }
        }

        [Test]
        public void Move_ToFreshPlacementClearsOldEconomicRecord()
        {
            GameObject buildingObject = null;
            GameObject oldObject = null;
            GameObject newObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B0 move-fresh building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement oldPlacement = MakePlacement("Bed", Vector3.zero);
                oldObject = oldPlacement.gameObject;
                BuildingEquipmentPlacement newPlacement = MakePlacement("Bed moved", new Vector3(2f, 0f, 0f));
                newObject = newPlacement.gameObject;
                var register = RegisterWithBed();
                InstallBed(register, building, oldPlacement);
                string error = register.TryMove("bed-1", newPlacement, building, BuildingInstanceId, 0,
                    out FurnishingPlacementBinding newBinding,
                    FurnishingCapabilityKind.None, null, oldPlacement);
                Assert.IsNull(error, error);
                Assert.AreEqual(1, register.Bindings.Count);
                Assert.AreEqual(newPlacement.PlacementId, newBinding.PlacementId);
                Assert.AreEqual(string.Empty, oldPlacement.EconomicOwnerId,
                    "The retired placement must no longer claim ownership.");
                Assert.AreEqual(string.Empty, oldPlacement.AcquisitionSourceId);
                Assert.AreEqual("7", newPlacement.EconomicOwnerId);
            }
            finally
            {
                if (newObject != null) Object.DestroyImmediate(newObject);
                if (oldObject != null) Object.DestroyImmediate(oldObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void Move_RefusesUninstalledAsset()
        {
            GameObject buildingObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B0 move refusal building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", Vector3.zero);
                itemObject = placement.gameObject;
                var register = RegisterWithBed();
                string error = register.TryMove("bed-1", placement, building, BuildingInstanceId, 0,
                    out FurnishingPlacementBinding binding);
                Assert.IsNotNull(error);
                Assert.IsNull(binding);
            }
            finally
            {
                if (itemObject != null) Object.DestroyImmediate(itemObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void Remove_KeepsAssetOwnedWithCustodyLocation()
        {
            GameObject buildingObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B0 remove building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", Vector3.zero);
                itemObject = placement.gameObject;
                var register = RegisterWithBed();
                InstallBed(register, building, placement);
                Assert.IsNull(register.TryRemove("bed-1", placement));
                Assert.AreEqual(0, register.Bindings.Count);
                Assert.AreEqual(1, register.Assets.Count, "Removing an item must never delete the owned asset.");
                Assert.AreEqual("household:7", register.FindAsset("bed-1").LocationId);
                Assert.AreEqual(string.Empty, placement.EconomicOwnerId);
                Assert.IsNotNull(register.TryRemove("bed-1"), "Removing an uninstalled asset refuses loudly.");
                Assert.AreEqual(0, register.Validate().Count, string.Join("; ", register.Validate()));
            }
            finally
            {
                if (itemObject != null) Object.DestroyImmediate(itemObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void TransferOwnership_EndsBindingAndMovesTitle()
        {
            GameObject buildingObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B0 transfer building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", Vector3.zero);
                itemObject = placement.gameObject;
                var register = RegisterWithBed();
                InstallBed(register, building, placement);
                Assert.IsNull(register.TryTransferOwnership("bed-1", "household", "8", "sold to household 8", 12));
                Assert.AreEqual(0, register.Bindings.Count, "Selling ends the seller's binding.");
                EquipmentAsset bed = register.FindAsset("bed-1");
                Assert.AreEqual("household", bed.OwnerKind);
                Assert.AreEqual("8", bed.OwnerId);
                Assert.AreEqual(1, bed.OwnershipHistory.Count);
                Assert.AreEqual("building:101", bed.LocationId,
                    "Title moved; the item is still physically in building 101 until it is moved.");
                Assert.AreEqual(1, register.Validate().Count,
                    "The seller still holding an asset it no longer owns is reported, not hidden — the sale flow must deregister from the seller.");
                Assert.IsNull(register.TryDeregisterAsset("bed-1"));
                Assert.AreEqual(0, register.Validate().Count, string.Join("; ", register.Validate()));
                var buyer = MakeRegister(OtherHouseholdId);
                Assert.IsNull(buyer.TryRegisterAsset(bed), "The buyer registers the same asset record after the transfer — no duplicate.");
                Assert.AreEqual(1, buyer.Assets.Count);
            }
            finally
            {
                if (itemObject != null) Object.DestroyImmediate(itemObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void TransferOwnership_RefusesReservedAsset()
        {
            var register = RegisterWithBed();
            EquipmentAsset bed = register.FindAsset("bed-1");
            Assert.IsNull(bed.Reserve("task-1", "sleeping"));
            Assert.IsNotNull(register.TryTransferOwnership("bed-1", "household", "8", "sold", 12));
            Assert.AreEqual("7", bed.OwnerId, "A reserved asset cannot change hands.");
        }

        [Test]
        public void Deregister_RefusesWhileInstalled()
        {
            GameObject buildingObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B0 deregister building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", Vector3.zero);
                itemObject = placement.gameObject;
                var register = RegisterWithBed();
                InstallBed(register, building, placement);
                Assert.IsNotNull(register.TryDeregisterAsset("bed-1"),
                    "A binding must never outlive its asset.");
                Assert.IsNull(register.TryRemove("bed-1"));
                Assert.IsNull(register.TryDeregisterAsset("bed-1"));
                Assert.AreEqual(0, register.Assets.Count);
            }
            finally
            {
                if (itemObject != null) Object.DestroyImmediate(itemObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void UnauthorisedPlacements_ReportedWhenNoBinding()
        {
            GameObject buildingObject = null;
            GameObject bareObject = null;
            GameObject ownedObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B0 unauthorised building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement bare = MakePlacement("Bare Shelf", new Vector3(2f, 0f, 0f));
                bareObject = bare.gameObject;
                bare.Configure("bare-placement", "furniture.shelf", "Bare Shelf", 0);
                BuildingEquipmentPlacement owned = MakePlacement("Bed", Vector3.zero);
                ownedObject = owned.gameObject;
                var register = RegisterWithBed();
                InstallBed(register, building, owned);

                List<string> problems = register.FindUnauthorisedPlacements(new[] { bare, owned });
                Assert.AreEqual(1, problems.Count, string.Join("; ", problems));
                Assert.IsTrue(problems[0].Contains("bare-placement"), problems[0]);

                Assert.AreEqual(0, register.FindUnauthorisedPlacements(new[] { owned }).Count,
                    "An installed placement is accounted for.");
                Assert.AreEqual(0, register.FindUnauthorisedPlacements(null).Count);
                Assert.AreEqual(0, register.FindUnauthorisedPlacements(new BuildingEquipmentPlacement[0]).Count);

                owned.SetEconomicRecord("8", "endowment-qa-town-1");
                List<string> tampered = register.FindUnauthorisedPlacements(new[] { owned });
                Assert.AreEqual(1, tampered.Count, "A placement whose recorded owner disagrees with its binding is reported.");
            }
            finally
            {
                if (ownedObject != null) Object.DestroyImmediate(ownedObject);
                if (bareObject != null) Object.DestroyImmediate(bareObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void SaveLoad_RoundTripPreservesRegisterAndBindings()
        {
            GameObject buildingObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B0 saveload building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", Vector3.zero);
                itemObject = placement.gameObject;
                var register = RegisterWithBed();
                Assert.IsNull(register.TryRegisterAsset(MakeCabinet("cab-1", HouseholdId, 0.5f)));
                FurnishingPlacementBinding binding = InstallBed(register, building, placement);
                register.FindAsset("bed-1").RecordMaintenance("Tightened joints", 3);

                var state = new Series2Pass9SaveState();
                state.HouseholdFurnishingRegisters.Add(register);
                Series2Pass9SaveDto dto = state.Capture(new List<string>());

                var restored = new Series2Pass9SaveState();
                restored.Restore(dto, new FreightShipmentHooks(), new List<string>());
                Assert.AreEqual(1, restored.HouseholdFurnishingRegisters.Count);
                HouseholdFurnishingRegister roundTripped = restored.HouseholdFurnishingRegisters[0];
                Assert.AreEqual(HouseholdId, roundTripped.HouseholdId);
                Assert.AreEqual(2, roundTripped.Assets.Count);
                Assert.AreEqual(1, roundTripped.Bindings.Count);
                Assert.AreNotSame(register, roundTripped, "Capture/Restore must deep-copy, not share the live register.");
                Assert.AreNotSame(register.FindAsset("bed-1"), roundTripped.FindAsset("bed-1"));

                EquipmentAsset bed = roundTripped.FindAsset("bed-1");
                Assert.AreEqual("furniture.bed", bed.Kind);
                Assert.AreEqual(1f, bed.Condition01);
                Assert.AreEqual("household", bed.OwnerKind);
                Assert.AreEqual("7", bed.OwnerId);
                Assert.AreEqual("building:101", bed.LocationId);
                Assert.AreEqual(1, bed.MaintenanceLog.Count);
                Assert.AreEqual(0.5f, roundTripped.FindAsset("cab-1").Condition01);

                FurnishingPlacementBinding roundTrippedBinding = roundTripped.Bindings[0];
                Assert.AreEqual(binding.BindingId, roundTrippedBinding.BindingId);
                Assert.AreEqual("bed-1", roundTrippedBinding.AssetId);
                Assert.AreEqual(BuildingInstanceId, roundTrippedBinding.BuildingInstanceId);
                Assert.AreEqual(placement.PlacementId, roundTrippedBinding.PlacementId);
                Assert.AreEqual(0, roundTrippedBinding.FloorIndex);
                Assert.AreEqual(Vector3.zero, roundTrippedBinding.LocalPosition);
                Assert.AreEqual("furniture.bed", roundTrippedBinding.DefinitionId);
                Assert.AreEqual(FurnishingAcquisitionSource.ScenarioEndowment, roundTrippedBinding.AcquisitionSource);
                Assert.AreEqual("endowment-qa-town-1", roundTrippedBinding.AcquisitionSourceId);
                Assert.AreEqual(0, roundTripped.Validate().Count, string.Join("; ", roundTripped.Validate()));

                roundTripped.FindAsset("bed-1").ApplyWear(0.5f);
                Assert.AreEqual(1f, register.FindAsset("bed-1").Condition01,
                    "Mutating the restored copy must not rewrite the saved live register.");
            }
            finally
            {
                if (itemObject != null) Object.DestroyImmediate(itemObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void SaveLoad_RestoreTwiceIsIdempotent()
        {
            var register = RegisterWithBed();
            var state = new Series2Pass9SaveState();
            state.HouseholdFurnishingRegisters.Add(register);
            Series2Pass9SaveDto dto = state.Capture(new List<string>());
            var restored = new Series2Pass9SaveState();
            restored.Restore(dto, new FreightShipmentHooks(), new List<string>());
            restored.Restore(dto, new FreightShipmentHooks(), new List<string>());
            Assert.AreEqual(1, restored.HouseholdFurnishingRegisters.Count, "Restoring twice rebuilds the same registers, never duplicates.");
            Assert.AreEqual(1, restored.HouseholdFurnishingRegisters[0].Assets.Count);
        }

        [Test]
        public void SaveLoad_OldSaveRestoresEmpty()
        {
            var restored = new Series2Pass9SaveState();
            restored.Restore(new Series2Pass9SaveDto { HouseholdFurnishingRegisters = null },
                new FreightShipmentHooks(), new List<string>());
            Assert.AreEqual(0, restored.HouseholdFurnishingRegisters.Count);
            restored.Restore(null, new FreightShipmentHooks(), new List<string>());
            Assert.AreEqual(0, restored.HouseholdFurnishingRegisters.Count);
        }

        [Test]
        public void Validate_ReportsBindingWithoutAssetAndDuplicates()
        {
            var register = RegisterWithBed();
            register.Bindings.Add(new FurnishingPlacementBinding
            {
                BindingId = "bogus-binding", AssetId = "ghost-asset", BuildingInstanceId = BuildingInstanceId,
                PlacementId = "bogus-placement", FloorIndex = 0,
                AcquisitionSource = FurnishingAcquisitionSource.Purchase, AcquisitionSourceId = "order-1",
            });
            List<string> problems = register.Validate();
            Assert.AreEqual(1, problems.Count, string.Join("; ", problems));
            Assert.IsTrue(problems[0].Contains("ghost-asset"), problems[0]);

            register.Bindings.Add(new FurnishingPlacementBinding
            {
                BindingId = "dup-binding", AssetId = "bed-1", BuildingInstanceId = BuildingInstanceId,
                PlacementId = "dup-placement", FloorIndex = 0,
                AcquisitionSource = FurnishingAcquisitionSource.Purchase, AcquisitionSourceId = "order-1",
            });
            register.Bindings.Add(new FurnishingPlacementBinding
            {
                BindingId = "dup-binding-2", AssetId = "bed-1", BuildingInstanceId = BuildingInstanceId,
                PlacementId = "dup-placement-2", FloorIndex = 0,
                AcquisitionSource = FurnishingAcquisitionSource.Purchase, AcquisitionSourceId = "order-1",
            });
            problems = register.Validate();
            Assert.IsTrue(problems.Count >= 2, string.Join("; ", problems));
            Assert.IsTrue(problems.Exists(p => p.Contains("more than one live binding")), string.Join("; ", problems));
        }
    }
}
