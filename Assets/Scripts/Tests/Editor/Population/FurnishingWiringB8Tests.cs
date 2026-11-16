using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Population;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// B8 (Agent B, expanded campaign 2026-11-16): wiring-audit support
    /// for Agent G. Proves the stove registry's creation seam — the
    /// CreateDerived factory that derives installed stoves from the B0
    /// furnishing register at creation, so a registry built through it
    /// never reads empty when a stove is installed — and the fuel-store
    /// order the Continue hook relies on: rebuild first, then restore
    /// saved fuel onto the re-derived stoves (fuel for a removed stove
    /// becomes orphaned, never silently dropped). The hub accessors,
    /// the domestic-drive stove pass-through, the heating call site,
    /// the storage day-boundary poll, and the bootstrapper Continue
    /// hook live in harness-excluded files, so they are source-reviewed
    /// and documented as such in the B8 record — the W-G2 precedent —
    /// and are NOT claimed as harness-verified here. No test claims
    /// Unity compilation or PlayMode.
    /// </summary>
    [TestFixture]
    public sealed class FurnishingWiringB8Tests
    {
        private const int HouseholdId = 7;
        private const int BuildingInstanceId = 101;

        private static HouseholdFurnishingRegister MakeRegister()
        {
            return new HouseholdFurnishingRegister { HouseholdId = HouseholdId };
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

        // B1's building helpers, verbatim: the walkable test floor is
        // authored through the private interiorFloors field, because
        // InteriorFloorAuthoring is plain data, not a Component.
        private static List<InteriorFloorAuthoring> GetInteriorFloors(BuildingFootprintAuthority authority)
        {
            System.Reflection.FieldInfo field = typeof(BuildingFootprintAuthority).GetField(
                "interiorFloors",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
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

        private static FurnishingPlacementBinding InstallStove(
            HouseholdFurnishingRegister register,
            BuildingFootprintAuthority building,
            BuildingEquipmentPlacement placement,
            string assetId = "stove-1",
            string activityLocationId = "stove")
        {
            string error = register.TryInstall(assetId, placement, building, BuildingInstanceId, 0,
                FurnishingCapabilityKind.StoveWorkstation, activityLocationId,
                FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                out FurnishingPlacementBinding binding);
            Assert.IsNull(error, error);
            Assert.NotNull(binding);
            return binding;
        }

        private static HouseholdState CreateHouseholdWithFuelReserve(int id, int fuelWoodUnits)
        {
            var household = new HouseholdState
            {
                id = id,
                householdName = "Test Household",
                surname = "Test",
                homeBuildingId = BuildingInstanceId,
                weeklyIncomeSnapshot = 20,
                spendingMoneyCents = 100000,
                stoveTier = HouseholdStoveTier.Basic,
                memberIds = new List<int> { 1, 2, 3, 4 },
            };
            household.EnsureHouseholdReservesInitialized(true);
            household.GetReserve("fuel_wood").currentUnits = fuelWoodUnits;
            return household;
        }

        [Test]
        public void CreateDerived_DerivesInstalledStove()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B8 derive building");
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));

            HouseholdStoveRegistry stoves = HouseholdStoveRegistry.CreateDerived(HouseholdId, register);
            Assert.AreEqual(HouseholdId, stoves.HouseholdId);
            Assert.AreEqual(1, stoves.Stoves.Count);
            Assert.AreEqual("stove-1", stoves.Stoves[0].StoveAssetId);
            Assert.AreEqual("stove", stoves.Stoves[0].ActivityLocationId);
            Assert.AreEqual("household-stove:stove-1", stoves.Stoves[0].WorkstationInstanceId);
            Assert.AreEqual(BuildingInstanceId, stoves.Stoves[0].BuildingInstanceId);
        }

        [Test]
        public void CreateDerived_NoStoveDerivesEmpty()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            HouseholdStoveRegistry stoves = HouseholdStoveRegistry.CreateDerived(HouseholdId, register);
            Assert.AreEqual(0, stoves.Stoves.Count);
            Assert.AreEqual(0, stoves.Validate().Count);
        }

        [Test]
        public void CreateDerived_NullRegisterDerivesEmptyWithoutThrow()
        {
            HouseholdStoveRegistry stoves = HouseholdStoveRegistry.CreateDerived(HouseholdId, null);
            Assert.AreEqual(0, stoves.Stoves.Count);
            Assert.AreEqual(1, stoves.Validate().Count,
                "A registry with no furnishing register must report the gap (no stoves can be derived), not silently claim consistency.");
            Assert.IsTrue(stoves.Validate()[0].Contains("no furnishing register"));
        }

        [Test]
        public void CreateDerived_RebuildIsIdempotent()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B8 idempotent building");
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));

            HouseholdStoveRegistry stoves = HouseholdStoveRegistry.CreateDerived(HouseholdId, register);
            stoves.RebuildFromRegister();
            stoves.RebuildFromRegister();
            Assert.AreEqual(1, stoves.Stoves.Count,
                "A second and third rebuild must not duplicate the stove or drop it.");
            Assert.AreEqual("stove-1", stoves.Stoves[0].StoveAssetId);
        }

        [Test]
        public void CreateDerived_OrphanedFuelSurvivesRebuild()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B8 orphan building");
            BuildingEquipmentPlacement placement = MakePlacement("Stove", Vector3.zero);
            InstallStove(register, building, placement);
            HouseholdStoveRegistry stoves = HouseholdStoveRegistry.CreateDerived(HouseholdId, register);
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 5);
            List<string> diag = new List<string>();
            Assert.IsNull(stoves.TryFeedStove("stove-1", 3, household, diag));
            Assert.AreEqual(3, stoves.FuelWoodUnitsOnHand("stove-1"));

            Assert.IsNull(register.TryRemove("stove-1"));
            stoves.RebuildFromRegister();
            Assert.AreEqual(0, stoves.Stoves.Count, "The stove is no longer installed.");
            Assert.AreEqual(3, stoves.FuelWoodUnitsOnHand("stove-1"),
                "Fuel in a removed stove is orphaned, never silently dropped by a rebuild.");
            Assert.AreEqual(5, stoves.TotalFuelWoodUnits(household),
                "The household total is unchanged (2 in the reserve + 3 in the removed stove).");
        }

        [Test]
        public void ContinueOrder_RebuildThenRestoreYieldsSavedFuelOnRightStove()
        {
            // The exact order the bootstrapper's B8 Continue hook runs:
            // the saved fuel stores are captured from the live registry,
            // the furnishing register is re-attached, the stove registry
            // is rebuilt from it, and only then are the saved stores
            // restored — so the fuel lands on the re-derived stove
            // instances, not on an empty registry.
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B8 continue building");
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));
            HouseholdStoveRegistry stoves = HouseholdStoveRegistry.CreateDerived(HouseholdId, register);
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 5);
            List<string> diag = new List<string>();
            Assert.IsNull(stoves.TryFeedStove("stove-1", 3, household, diag));
            List<HouseholdStoveFuelStoreDto> saved = stoves.CaptureFuelStores();
            Assert.AreEqual(1, saved.Count);
            Assert.AreEqual(3, saved[0].FuelWoodUnits);

            HouseholdStoveRegistry reloaded = HouseholdStoveRegistry.CreateDerived(HouseholdId, register);
            reloaded.RestoreFuelStores(saved);
            Assert.AreEqual(3, reloaded.FuelWoodUnitsOnHand("stove-1"),
                "Saved fuel must land on the re-derived stove after the rebuild, not be lost.");
            Assert.AreEqual(0, reloaded.Validate().Count);
        }

        [Test]
        public void ContinueOrder_RestoreBeforeRebuildStillHealsViaReadoption()
        {
            // The order the bootstrapper's B8 Continue hook runs is
            // rebuild first, then restore — but the order is a clarity
            // convention, not load-bearing, and this test pins why:
            // RebuildFromRegister re-adopts orphaned fuel for a stove
            // that is still installed, so restoring saved fuel BEFORE
            // the rebuild still ends with the fuel on the installed
            // stove (it lands as orphaned fuel first, then the rebuild
            // takes it back). The hook still rebuilds first, because
            // deriving the instances before filling their stores reads
            // as the sequence it is.
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B8 wrong-order building");
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));
            HouseholdStoveRegistry stoves = HouseholdStoveRegistry.CreateDerived(HouseholdId, register);
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 5);
            List<string> diag = new List<string>();
            Assert.IsNull(stoves.TryFeedStove("stove-1", 3, household, diag));
            List<HouseholdStoveFuelStoreDto> saved = stoves.CaptureFuelStores();

            var reloaded = new HouseholdStoveRegistry(HouseholdId, register);
            reloaded.RestoreFuelStores(saved);
            reloaded.RebuildFromRegister();
            Assert.AreEqual(1, reloaded.Stoves.Count);
            Assert.AreEqual(3, reloaded.FuelWoodUnitsOnHand("stove-1"),
                "Restoring before the rebuild still ends with the fuel on the installed stove — the rebuild re-adopts orphaned fuel.");
            Assert.AreEqual(0, reloaded.Validate().Count);
        }

        [Test]
        public void ContinueOrder_RestoreFuelForRemovedStoveBecomesOrphaned()
        {
            // A stove removed before the save: its saved fuel must
            // restore as orphaned fuel on the re-derived registry, never
            // silently dropped and never invented onto another stove.
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B8 removed building");
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));
            HouseholdStoveRegistry stoves = HouseholdStoveRegistry.CreateDerived(HouseholdId, register);
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 5);
            List<string> diag = new List<string>();
            Assert.IsNull(stoves.TryFeedStove("stove-1", 3, household, diag));
            List<HouseholdStoveFuelStoreDto> saved = stoves.CaptureFuelStores();

            Assert.IsNull(register.TryRemove("stove-1"));
            HouseholdStoveRegistry reloaded = HouseholdStoveRegistry.CreateDerived(HouseholdId, register);
            reloaded.RestoreFuelStores(saved);
            Assert.AreEqual(0, reloaded.Stoves.Count, "The stove is still removed after Continue.");
            Assert.AreEqual(3, reloaded.FuelWoodUnitsOnHand("stove-1"),
                "Fuel for a removed stove restores as orphaned fuel, in custody.");
            Assert.AreEqual(5, reloaded.TotalFuelWoodUnits(household));
        }
    }
}
