using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Logistics;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// B2 (Agent B, expanded campaign 2026-11-16): household stove
    /// workstation + stove fuel store + stoveTier derivation. Proves
    /// the stove is a real object: the workstation definition and its
    /// gate (ready stove + fuel = stove cooking, anything else = cold
    /// food fallback that never blocks eating), the fuel store
    /// (reserve-to-stove feeding, stove-only consumption, removal
    /// keeping the fuel with the stove, re-install restoring it), the
    /// tier derivation (installed stove wins over the legacy field),
    /// the heating regression (an installed stove burns the same as a
    /// legacy stove of the same tier), the driver hook (planned
    /// meal-prep tasks carry their mode), and the Pass 9 save/load
    /// round-trip for fuel stores. No test claims Unity compilation
    /// or PlayMode.
    /// </summary>
    [TestFixture]
    public sealed class HouseholdStoveRegistryTests
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

        private static EquipmentAsset MakeBed(string assetId)
        {
            return new EquipmentAsset
            {
                AssetId = assetId,
                Kind = "furniture.bed",
                DisplayName = "Bed",
                Condition01 = 1f,
                OwnerKind = "household",
                OwnerId = HouseholdId.ToString(),
            };
        }

        // B1's building helpers, verbatim: the walkable test floor is
        // authored through the private interiorFloors field, because
        // InteriorFloorAuthoring is plain data, not a Component.
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

        private static HouseholdStoveRegistry MakeRegistry(
            HouseholdFurnishingRegister furnishings, int homeBuildingInstanceId = BuildingInstanceId)
        {
            var stoves = new HouseholdStoveRegistry(HouseholdId, furnishings)
            {
                HomeBuildingInstanceId = homeBuildingInstanceId,
            };
            stoves.RebuildFromRegister();
            return stoves;
        }

        private static HouseholdState CreateHousehold(int id, HouseholdStoveTier stoveTier)
        {
            return new HouseholdState
            {
                id = id,
                householdName = "Test Household",
                surname = "Test",
                homeBuildingId = 1,
                weeklyIncomeSnapshot = 20,
                spendingMoneyCents = 100000,
                stoveTier = stoveTier,
                memberIds = new List<int> { 1, 2, 3, 4 },
            };
        }

        private static HouseholdState CreateHouseholdWithFuelReserve(int id, int fuelWoodUnits)
        {
            HouseholdState household = CreateHousehold(id, HouseholdStoveTier.Basic);
            household.EnsureHouseholdReservesInitialized(true);
            household.GetReserve("fuel_wood").currentUnits = fuelWoodUnits;
            return household;
        }

        [Test]
        public void Catalog_HouseholdStove_DefinedWithStoveComponentAndOvenWoodSupport()
        {
            WorkstationDefinition def = WorkstationCatalog.HouseholdStove;
            Assert.AreEqual("household-stove", def.WorkstationId);
            Assert.AreEqual("home", def.RequiredSpaceKind);
            Assert.IsTrue(def.CapabilitiesGranted.Contains("cook-meals"), "The stove must offer the cook-meals capability.");
            Assert.AreEqual(1, def.Components.Count);
            Assert.AreEqual("furniture.cooking-stove", def.Components[0].EquipmentKind);
            Assert.AreEqual(1, def.Components[0].Count);
            Assert.AreEqual(1, def.SupportRequirements.Count);
            Assert.AreEqual("fuel", def.SupportRequirements[0].Kind);
            Assert.AreEqual("oven-wood", def.SupportRequirements[0].Detail);
            Assert.AreEqual(1, def.SupportRequirements[0].UnitsPerDay);
            Assert.IsTrue(WorkstationCatalog.All.Any(d => d.WorkstationId == "household-stove"),
                "The household stove must be listed in WorkstationCatalog.All.");
        }

        [Test]
        public void RebuildFromRegister_DerivesInstalledStoveAndIgnoresBed()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            register.TryRegisterAsset(MakeBed("bed-1"));
            BuildingFootprintAuthority building = MakeBuilding("B2 derive building");
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));
            string bedError = register.TryInstall("bed-1", MakePlacement("Bed", new Vector3(1f, 0f, 1f)),
                building, BuildingInstanceId, 0, FurnishingCapabilityKind.SleepingPlace, "bed-1",
                FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1", out _);
            Assert.IsNull(bedError, bedError);

            HouseholdStoveRegistry stoves = MakeRegistry(register);
            Assert.AreEqual(1, stoves.Stoves.Count);
            Assert.AreEqual("stove-1", stoves.Stoves[0].StoveAssetId);
            Assert.AreEqual("stove", stoves.Stoves[0].ActivityLocationId);
            Assert.AreEqual("household-stove:stove-1", stoves.Stoves[0].WorkstationInstanceId);
            Assert.AreEqual(BuildingInstanceId, stoves.Stoves[0].BuildingInstanceId);
        }

        [Test]
        public void RebuildFromRegister_IgnoresUnusableStoveAndStoveWithoutCapability()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("broken-stove", condition: 0.04f));
            register.TryRegisterAsset(MakeStove("no-capability-stove"));
            BuildingFootprintAuthority building = MakeBuilding("B2 unusable building");
            string brokenError = register.TryInstall("broken-stove", MakePlacement("Broken Stove", Vector3.zero),
                building, BuildingInstanceId, 0, FurnishingCapabilityKind.StoveWorkstation, "stove",
                FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1", out _);
            Assert.IsNull(brokenError, brokenError);
            // A stove installed with no stove capability is a real
            // placement (a heater, a decoration) but not a cooking stove.
            string plainError = register.TryInstall("no-capability-stove",
                MakePlacement("Plain Stove", new Vector3(1f, 0f, 1f)), building, BuildingInstanceId, 0,
                FurnishingCapabilityKind.None, "stove",
                FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1", out _);
            Assert.IsNull(plainError, plainError);

            HouseholdStoveRegistry stoves = MakeRegistry(register);
            Assert.AreEqual(0, stoves.Stoves.Count,
                "A stove at condition 0.04 is unusable and a stove without the stove capability is not a cooking stove.");
        }

        [Test]
        public void Gate_ReadyStoveAndReserveFuel_CooksOnStove()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B2 gate building");
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));
            HouseholdStoveRegistry stoves = MakeRegistry(register);
            List<string> diag = new List<string>();

            HouseholdStoveMealPrepMode mode = stoves.CheckMealPrep(0, 3, diag, out string reason);
            Assert.AreEqual(HouseholdStoveMealPrepMode.StoveCooking, mode);
            Assert.IsNull(reason);
        }

        [Test]
        public void Gate_NoStove_FallsBackToColdFoodAndNamesTheStove()
        {
            HouseholdStoveRegistry stoves = MakeRegistry(MakeRegister());
            List<string> diag = new List<string>();

            HouseholdStoveMealPrepMode mode = stoves.CheckMealPrep(0, 3, diag, out string reason);
            Assert.AreEqual(HouseholdStoveMealPrepMode.ColdFoodFallback, mode);
            Assert.IsTrue(reason.Contains("furniture.cooking-stove"),
                "The fallback reason must name the missing stove component so the diagnostic is actionable, got: " + reason);
            Assert.IsTrue(diag.Count > 0, "The fallback must be recorded loudly in the diagnostics.");
        }

        [Test]
        public void Gate_StoveButNoFuelAnywhere_FallsBackAndNamesFuel()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B2 no-fuel building");
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));
            HouseholdStoveRegistry stoves = MakeRegistry(register);
            List<string> diag = new List<string>();

            HouseholdStoveMealPrepMode mode = stoves.CheckMealPrep(0, 0, diag, out string reason);
            Assert.AreEqual(HouseholdStoveMealPrepMode.ColdFoodFallback, mode);
            Assert.IsTrue(reason.Contains("oven-wood"),
                "The fallback reason must name the missing fuel, got: " + reason);
        }

        [Test]
        public void Gate_StoveStoreFuelSatisfiesSupportWhenReserveIsEmpty()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B2 store-fuel building");
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));
            HouseholdStoveRegistry stoves = MakeRegistry(register);
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 5);
            List<string> diag = new List<string>();
            Assert.IsNull(stoves.TryFeedStove("stove-1", 2, household, diag));
            // The reserve is now spent; only the stove store holds fuel.
            household.GetReserve("fuel_wood").currentUnits = 0;

            HouseholdStoveMealPrepMode mode = stoves.CheckMealPrep(0, 0, diag, out string reason);
            Assert.AreEqual(HouseholdStoveMealPrepMode.StoveCooking, mode,
                "Fuel already in the stove store satisfies the gate even when the reserve is empty, got: " + reason);
        }

        [Test]
        public void Gate_UnusableStove_FallsBackAndNamesCondition()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("broken-stove", condition: 0.04f));
            BuildingFootprintAuthority building = MakeBuilding("B2 broken building");
            string error = register.TryInstall("broken-stove", MakePlacement("Broken Stove", Vector3.zero),
                building, BuildingInstanceId, 0, FurnishingCapabilityKind.StoveWorkstation, "stove",
                FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1", out _);
            Assert.IsNull(error, error);
            HouseholdStoveRegistry stoves = MakeRegistry(register);
            List<string> diag = new List<string>();

            HouseholdStoveMealPrepMode mode = stoves.CheckMealPrep(0, 3, diag, out string reason);
            Assert.AreEqual(HouseholdStoveMealPrepMode.ColdFoodFallback, mode);
            Assert.IsTrue(reason.Contains("condition"),
                "The fallback reason must name the unusable stove's condition, got: " + reason);
        }

        [Test]
        public void TryFeedStove_MovesReserveFuelIntoStoveStoreWithTotalUnchanged()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B2 feed building");
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));
            HouseholdStoveRegistry stoves = MakeRegistry(register);
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 5);
            List<string> diag = new List<string>();

            Assert.IsNull(stoves.TryFeedStove("stove-1", 2, household, diag));
            Assert.AreEqual(2, stoves.FuelWoodUnitsOnHand("stove-1"));
            Assert.AreEqual(3, household.GetReserve("fuel_wood").currentUnits);
            Assert.AreEqual(5, stoves.TotalFuelWoodUnits(household),
                "Feeding moves fuel, never creates or destroys it.");
        }

        [Test]
        public void TryFeedStove_RefusesWhenReserveIsShortAndMovesNothing()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B2 short building");
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));
            HouseholdStoveRegistry stoves = MakeRegistry(register);
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 1);
            List<string> diag = new List<string>();

            string error = stoves.TryFeedStove("stove-1", 2, household, diag);
            Assert.IsNotNull(error);
            Assert.AreEqual(0, stoves.FuelWoodUnitsOnHand("stove-1"));
            Assert.AreEqual(1, household.GetReserve("fuel_wood").currentUnits);
        }

        [Test]
        public void TryFeedStove_RefusesUnknownStoveAndNonPositiveUnits()
        {
            HouseholdStoveRegistry stoves = MakeRegistry(MakeRegister());
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 5);
            List<string> diag = new List<string>();
            Assert.IsNotNull(stoves.TryFeedStove("no-such-stove", 1, household, diag));
            Assert.IsNotNull(stoves.TryFeedStove("stove-1", 0, household, diag));
            Assert.IsNotNull(stoves.TryFeedStove("stove-1", -1, household, diag));
            Assert.AreEqual(5, household.GetReserve("fuel_wood").currentUnits);
        }

        [Test]
        public void TryConsumeStoveFuel_DebitsOnlyTheStoveStore()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B2 consume building");
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));
            HouseholdStoveRegistry stoves = MakeRegistry(register);
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 5);
            List<string> diag = new List<string>();
            Assert.IsNull(stoves.TryFeedStove("stove-1", 3, household, diag));

            Assert.IsNull(stoves.TryConsumeStoveFuel("stove-1", 1, diag));
            Assert.AreEqual(2, stoves.FuelWoodUnitsOnHand("stove-1"));
            Assert.AreEqual(2, household.GetReserve("fuel_wood").currentUnits,
                "Consumption debits the stove store only, never the reserve.");
            Assert.AreEqual(4, stoves.TotalFuelWoodUnits(household));
        }

        [Test]
        public void TryConsumeStoveFuel_RefusesWhenStoreIsShort()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B2 consume-short building");
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));
            HouseholdStoveRegistry stoves = MakeRegistry(register);
            List<string> diag = new List<string>();

            Assert.IsNotNull(stoves.TryConsumeStoveFuel("stove-1", 1, diag));
            Assert.AreEqual(0, stoves.FuelWoodUnitsOnHand("stove-1"));
        }

        [Test]
        public void Removal_KeepsFuelWithStoveAndReinstallRestoresIt()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B2 removal building");
            BuildingEquipmentPlacement placement = MakePlacement("Stove", Vector3.zero);
            InstallStove(register, building, placement);
            HouseholdStoveRegistry stoves = MakeRegistry(register);
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 5);
            List<string> diag = new List<string>();
            Assert.IsNull(stoves.TryFeedStove("stove-1", 3, household, diag));
            Assert.AreEqual(3, stoves.FuelWoodUnitsOnHand("stove-1"));

            string removeError = register.TryRemove("stove-1");
            Assert.IsNull(removeError, removeError);
            stoves.RebuildFromRegister();
            Assert.AreEqual(0, stoves.Stoves.Count, "The stove is no longer installed.");
            Assert.AreEqual(5, stoves.TotalFuelWoodUnits(household),
                "Fuel in a removed stove is orphaned, never silently dropped — the household total is unchanged (2 in the reserve + 3 in the removed stove).");
            Assert.AreEqual(1, stoves.Validate().Count, "Validate must report the orphaned fuel so the sale flow settles it.");

            string installError = register.TryInstall("stove-1", placement, building, BuildingInstanceId, 0,
                FurnishingCapabilityKind.StoveWorkstation, "stove",
                FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1", out _);
            Assert.IsNull(installError, installError);
            stoves.RebuildFromRegister();
            Assert.AreEqual(3, stoves.FuelWoodUnitsOnHand("stove-1"),
                "Re-installing the stove restores its fuel.");
            Assert.AreEqual(0, stoves.Validate().Count);
        }

        [Test]
        public void TryReturnStoveFuelToReserve_MovesOrphanedFuelBack()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B2 return building");
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));
            HouseholdStoveRegistry stoves = MakeRegistry(register);
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 5);
            List<string> diag = new List<string>();
            Assert.IsNull(stoves.TryFeedStove("stove-1", 3, household, diag));
            Assert.IsNull(register.TryRemove("stove-1"));
            stoves.RebuildFromRegister();

            Assert.IsNull(stoves.TryReturnStoveFuelToReserve("stove-1", household, diag));
            Assert.AreEqual(5, household.GetReserve("fuel_wood").currentUnits);
            Assert.AreEqual(0, stoves.Validate().Count);
        }

        [Test]
        public void DeriveStoveTier_InstalledStoveWinsOverLegacyField()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B2 tier building");
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));
            HouseholdStoveRegistry stoves = MakeRegistry(register);
            // Legacy field says Efficient, but the installed stove is
            // the standard cooking stove: the instance wins.
            HouseholdState household = CreateHousehold(HouseholdId, HouseholdStoveTier.Efficient);

            Assert.AreEqual(HouseholdStoveTier.Basic, stoves.DeriveStoveTier(household));
        }

        [Test]
        public void DeriveStoveTier_NoStoveKeepsLegacyField()
        {
            HouseholdStoveRegistry stoves = MakeRegistry(MakeRegister());
            HouseholdState efficient = CreateHousehold(HouseholdId, HouseholdStoveTier.Efficient);
            Assert.AreEqual(HouseholdStoveTier.Efficient, stoves.DeriveStoveTier(efficient));
            HouseholdState basic = CreateHousehold(HouseholdId, HouseholdStoveTier.Basic);
            Assert.AreEqual(HouseholdStoveTier.Basic, stoves.DeriveStoveTier(basic));
            Assert.AreEqual(HouseholdStoveTier.Basic, stoves.DeriveStoveTier(null));
        }

        [Test]
        public void DeriveStoveTier_RemovalRevertsToLegacyField()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B2 tier-removal building");
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));
            HouseholdStoveRegistry stoves = MakeRegistry(register);
            HouseholdState household = CreateHousehold(HouseholdId, HouseholdStoveTier.Efficient);
            Assert.AreEqual(HouseholdStoveTier.Basic, stoves.DeriveStoveTier(household));

            Assert.IsNull(register.TryRemove("stove-1"));
            stoves.RebuildFromRegister();
            Assert.AreEqual(HouseholdStoveTier.Efficient, stoves.DeriveStoveTier(household));
        }

        [Test]
        public void HeatingRegression_InstalledStoveBurnsSameAsLegacyStoveOfSameTier()
        {
            // R-2 (plan §13): an installed standard stove must burn
            // exactly like a legacy Basic household, and an installed
            // stove must override a stale Efficient legacy field.
            // Heating lane (Nov 20 2026): the burn is temperature-driven,
            // so the comparison is pinned to an explicit cold January day
            // (-20 C): at that differential a 4-member household burns 4
            // units on Basic and 3 on Efficient, so the tier difference is
            // visible in integers. (At the January mean the size factor
            // pushes both tiers to 3 and the rounding hides the difference
            // — the multiplier is real, the integer rounding hides it.)
            HouseholdState legacyBasic = CreateHousehold(7, HouseholdStoveTier.Basic);
            HouseholdState legacyEfficient = CreateHousehold(8, HouseholdStoveTier.Efficient);

            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B2 heating building");
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));
            HouseholdStoveRegistry stoves = MakeRegistry(register);
            HouseholdState installedBasic = CreateHousehold(HouseholdId, HouseholdStoveTier.Basic);
            HouseholdState staleEfficient = CreateHousehold(9, HouseholdStoveTier.Efficient);

            // Agent G's wiring writes the derived tier back onto the
            // household before the heating burn is computed.
            installedBasic.stoveTier = stoves.DeriveStoveTier(installedBasic);
            staleEfficient.stoveTier = stoves.DeriveStoveTier(staleEfficient);

            int legacyBasicUse = HouseholdHeatingEvaluator.CalculateDailyFuelUseUnits(legacyBasic, -20f);
            int installedBasicUse = HouseholdHeatingEvaluator.CalculateDailyFuelUseUnits(installedBasic, -20f);
            int legacyEfficientUse = HouseholdHeatingEvaluator.CalculateDailyFuelUseUnits(legacyEfficient, -20f);
            int staleEfficientUse = HouseholdHeatingEvaluator.CalculateDailyFuelUseUnits(staleEfficient, -20f);
            Assert.AreEqual(legacyBasicUse, installedBasicUse,
                "An installed standard stove burns the same as a legacy Basic stove.");
            Assert.IsTrue(legacyEfficientUse < legacyBasicUse,
                "A genuinely Efficient stove burns less than a Basic one.");
            Assert.AreEqual(legacyBasicUse, staleEfficientUse,
                "The installed standard stove overrides the stale Efficient legacy field.");
        }

        [Test]
        public void DriverHook_StampsMealPrepModeOnPlannedTask()
        {
            List<DomesticLaborDriver.PendingDomesticTask> planned;

            // No stove at all (empty registry): the task is still
            // planned, in cold-food fallback mode, with the reason on
            // the note.
            planned = PlanMealPrep(new DriverFixture(), MakeRegistry(MakeRegister()), -1);
            Assert.AreEqual(1, planned.Count, "Meal-prep is still planned without a stove.");
            Assert.AreEqual(HouseholdStoveMealPrepMode.ColdFoodFallback, planned[0].MealPrepMode);
            Assert.IsTrue(planned[0].Note.Contains("cold food"), planned[0].Note);

            // A ready stove with reserve fuel: stove cooking.
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B2 driver building");
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));
            HouseholdStoveRegistry stoves = MakeRegistry(register);
            planned = PlanMealPrep(new DriverFixture(), stoves, 3);
            Assert.AreEqual(1, planned.Count);
            Assert.AreEqual(HouseholdStoveMealPrepMode.StoveCooking, planned[0].MealPrepMode);
            Assert.IsTrue(planned[0].Note.Contains("stove cooking"), planned[0].Note);

            // No stove registry (legacy caller): the mode is Unspecified.
            planned = PlanMealPrepWithNoRegistry(new DriverFixture());
            Assert.AreEqual(1, planned.Count);
            Assert.AreEqual(HouseholdStoveMealPrepMode.Unspecified, planned[0].MealPrepMode);
        }

        private static List<DomesticLaborDriver.PendingDomesticTask> PlanMealPrep(
            DriverFixture fixture, HouseholdStoveRegistry stoves, int reserveUnits)
        {
            DomesticLaborDriver.DomesticDayInputs inputs = fixture.Inputs(HouseholdId, new[] { 3, 4 });
            inputs.Stoves = stoves;
            inputs.FuelWoodReserveUnits = reserveUnits;
            fixture.Driver.DriveDay(inputs, 0, fixture.Tasks, fixture.Budgets, fixture.Schedules,
                fixture.Domestic, fixture.DueEvents, new List<string>());
            return fixture.Driver.Pending
                .Where(t => t.DefinitionId == DomesticTaskCatalog.MealPrepId).ToList();
        }

        private static List<DomesticLaborDriver.PendingDomesticTask> PlanMealPrepWithNoRegistry(
            DriverFixture fixture)
        {
            DomesticLaborDriver.DomesticDayInputs inputs = fixture.Inputs(HouseholdId, new[] { 3, 4 });
            fixture.Driver.DriveDay(inputs, 0, fixture.Tasks, fixture.Budgets, fixture.Schedules,
                fixture.Domestic, fixture.DueEvents, new List<string>());
            return fixture.Driver.Pending
                .Where(t => t.DefinitionId == DomesticTaskCatalog.MealPrepId).ToList();
        }

        [Test]
        public void SaveLoad_FuelStoresRoundTripAndRestoreTwiceIsIdempotent()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            register.TryRegisterAsset(MakeStove("stove-2"));
            BuildingFootprintAuthority building = MakeBuilding("B2 saveload building");
            InstallStove(register, building, MakePlacement("Stove 1", Vector3.zero), "stove-1", "stove");
            InstallStove(register, building, MakePlacement("Stove 2", new Vector3(1f, 0f, 1f)), "stove-2", "stove");
            HouseholdStoveRegistry stoves = MakeRegistry(register);
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 10);
            List<string> diag = new List<string>();
            Assert.IsNull(stoves.TryFeedStove("stove-1", 4, household, diag));
            Assert.IsNull(stoves.TryFeedStove("stove-2", 2, household, diag));

            // The sync seam (Agent G's wiring in production) copies the
            // registry's captured stores into the state before Capture.
            Series2Pass9SaveState state = new Series2Pass9SaveState();
            state.HouseholdStoveFuelStores.AddRange(stoves.CaptureFuelStores());
            Series2Pass9SaveDto dto = state.Capture(new List<string>());

            Series2Pass9SaveState restored = new Series2Pass9SaveState();
            restored.Restore(dto, new FreightShipmentHooks(), new List<string>());
            Assert.AreEqual(2, restored.HouseholdStoveFuelStores.Count);
            Assert.AreEqual(4, restored.HouseholdStoveFuelStores.First(s => s.StoveAssetId == "stove-1").FuelWoodUnits);
            Assert.AreEqual(2, restored.HouseholdStoveFuelStores.First(s => s.StoveAssetId == "stove-2").FuelWoodUnits);

            // Restore into a fresh registry, twice: idempotent.
            HouseholdStoveRegistry fresh = MakeRegistry(register);
            fresh.RestoreFuelStores(restored.HouseholdStoveFuelStores);
            Assert.AreEqual(4, fresh.FuelWoodUnitsOnHand("stove-1"));
            fresh.RestoreFuelStores(restored.HouseholdStoveFuelStores);
            Assert.AreEqual(4, fresh.FuelWoodUnitsOnHand("stove-1"));
            Assert.AreEqual(2, fresh.FuelWoodUnitsOnHand("stove-2"));

            // Deep-copy isolation: mutating the restored DTO must not
            // rewrite the original registry's stores.
            restored.HouseholdStoveFuelStores[0].FuelWoodUnits = 99;
            Assert.AreEqual(4, stoves.FuelWoodUnitsOnHand("stove-1"));
        }

        [Test]
        public void SaveLoad_OldSaveWithNoSectionRestoresEmptyStores()
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding("B2 old-save building");
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));
            HouseholdStoveRegistry stoves = MakeRegistry(register);
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 5);
            List<string> diag = new List<string>();
            Assert.IsNull(stoves.TryFeedStove("stove-1", 2, household, diag));

            Series2Pass9SaveState state = new Series2Pass9SaveState();
            Series2Pass9SaveDto dto = state.Capture(new List<string>());
            Series2Pass9SaveState restored = new Series2Pass9SaveState();
            restored.Restore(dto, new FreightShipmentHooks(), new List<string>());
            Assert.AreEqual(0, restored.HouseholdStoveFuelStores.Count);

            HouseholdStoveRegistry fresh = MakeRegistry(register);
            fresh.RestoreFuelStores(restored.HouseholdStoveFuelStores);
            Assert.AreEqual(0, fresh.FuelWoodUnitsOnHand("stove-1"),
                "An old save with no fuel section loads with empty stores — the honest old-save default.");
        }

        /// <summary>
        /// B2: a local copy of the DomesticLaborDriverTests fixture
        /// (that fixture is private to its own test class). Same inputs
        /// shape — two adult members, two meals at home, cleaning
        /// needed — so meal-prep is wanted and a free member plans it.
        /// </summary>
        private sealed class DriverFixture
        {
            public readonly TaskAuthority Tasks = new TaskAuthority();
            public readonly WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public readonly PersonScheduleTracker Schedules = new PersonScheduleTracker();
            public readonly DomesticLaborService Domestic = new DomesticLaborService();
            public readonly DomesticLaborDriver Driver = new DomesticLaborDriver();
            public readonly FamilyLaborLedger FamilyLabor = new FamilyLaborLedger();
            public readonly SimpleDueEventScheduler DueEvents = new SimpleDueEventScheduler();

            public DriverFixture()
            {
                var diag = new List<string>();
                DomesticTaskCatalog.RegisterAll(Tasks, diag);
            }

            public DomesticLaborDriver.DomesticDayInputs Inputs(int householdId, params int[] members)
            {
                return new DomesticLaborDriver.DomesticDayInputs
                {
                    HouseholdId = householdId,
                    MemberIds = new List<int>(members),
                    AgeBandFor = _ => AgeBand.Adult18Plus,
                    MealsAtHomeCount = 2,
                    ColdSeason = false,
                    CleaningNeeded = true,
                    HasAnimals = false,
                };
            }
        }
    }
}
