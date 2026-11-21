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
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// B3 (Agent B, expanded campaign 2026-11-16): the fuel + cooking
    /// driver end to end. Proves the two-leg fuel-handling arrival
    /// (firewood pile, then stove) with the in-transit hold, the
    /// reserve-to-store custody (the reserve is debited only on stove
    /// receipt, a failed trip releases the hold with nothing lost),
    /// the stove-store/reserve heating reconciliation (one burn, one
    /// debit), and the cooking settlement (StoveCooking burns exactly
    /// one stove fuel unit and converts 2 flour into 1 loaf through
    /// the meal authority; anything else produces nothing, and the
    /// meal count never changes — DEF-015). The physical-arrival
    /// seam is stubbed (the callback returns Arrived/Unsupported per
    /// leg); the resolver's location picking in SimulationDrivers is
    /// harness-excluded and source-reviewed only, with Unity
    /// compilation proof owed from Codex. No test claims Unity
    /// compilation or PlayMode.
    /// </summary>
    [TestFixture]
    public sealed class HouseholdFuelCookingDriverB3Tests
    {
        private const int HouseholdId = 7;
        private const int BuildingInstanceId = 101;
        private const int JanuaryDay = 306;

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

        private static HouseholdStoveRegistry MakeStoveRegistry(string buildingName)
        {
            HouseholdFurnishingRegister register = MakeRegister();
            register.TryRegisterAsset(MakeStove("stove-1"));
            BuildingFootprintAuthority building = MakeBuilding(buildingName);
            InstallStove(register, building, MakePlacement("Stove", Vector3.zero));
            return MakeRegistry(register);
        }

        /// <summary>
        /// B3: the physical-arrival stub. Returns a configured result
        /// per fuel-trip leg (leg 0 = to the firewood pile, leg 1 = to
        /// the stove) and Arrived for every other flight. This stands
        /// in for the resolver + pathing director, which the harness
        /// cannot exercise (SimulationDrivers is harness-excluded).
        /// </summary>
        private sealed class StubRoute
        {
            public DomesticActivityMovementResult Leg0 = DomesticActivityMovementResult.Arrived;
            public DomesticActivityMovementResult Leg1 = DomesticActivityMovementResult.Arrived;

            public DomesticActivityMovementResult Result(DomesticLaborDriver.InFlightDomesticTask flight, float seconds)
            {
                if (flight != null && string.Equals(flight.DefinitionId, DomesticTaskCatalog.FuelHandlingId, StringComparison.Ordinal))
                    return flight.FuelHandlingLeg <= 0 ? Leg0 : Leg1;
                return DomesticActivityMovementResult.Arrived;
            }
        }

        /// <summary>
        /// B3: a local copy of the B2 DriverFixture, extended with the
        /// meal log and the inventory registry the cooking settlement
        /// reads. Same inputs shape — two adult members, two meals at
        /// home — with the fuel trip wanted and cleaning off, so only
        /// meal-prep and fuel-handling are planned.
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
            public readonly HouseholdMealLogRegistry MealLog = new HouseholdMealLogRegistry();
            public readonly HouseholdInventoryRegistry Inventories = new HouseholdInventoryRegistry();

            public DriverFixture()
            {
                var diag = new List<string>();
                DomesticTaskCatalog.RegisterAll(Tasks, diag);
            }

            public DomesticLaborDriver.DomesticDayInputs Inputs(
                int householdId, HouseholdState household, HouseholdStoveRegistry stoves,
                HouseholdInventory inventory, params int[] members)
            {
                return new DomesticLaborDriver.DomesticDayInputs
                {
                    HouseholdId = householdId,
                    MemberIds = new List<int>(members),
                    AgeBandFor = _ => AgeBand.Adult18Plus,
                    MealsAtHomeCount = 2,
                    FuelHandlingNeeded = true,
                    ColdSeason = false,
                    CleaningNeeded = false,
                    HasAnimals = false,
                    Household = household,
                    Stoves = stoves,
                    Inventory = inventory,
                    MealService = new HouseholdMealService(MealLog),
                };
            }
        }

        private static DomesticLaborDriver.PendingDomesticTask PlannedTask(
            DomesticLaborDriver driver, string definitionId)
        {
            return driver.Pending.First(t => t.DefinitionId == definitionId);
        }

        private static DomesticLaborDriver.InFlightDomesticTask InFlightTask(
            DomesticLaborDriver driver, string definitionId)
        {
            return driver.InFlight.First(t => t.DefinitionId == definitionId);
        }

        private static float StartSeconds(int dayIndex, DomesticLaborDriver.PendingDomesticTask planned)
        {
            return dayIndex * 86400f + planned.StartMinuteOfDay * 60f;
        }

        private static void CompleteDueTask(
            DriverFixture f, DomesticLaborDriver driver, float dueAt, int dayIndex, List<string> diag)
        {
            List<DueEvent> due = f.DueEvents.PopDue(dueAt);
            Assert.AreEqual(1, due.Count, "exactly one due event at the completion time");
            foreach (DueEvent ev in due)
            {
                driver.HandleDueEvent(ev, f.Tasks, f.Budgets, f.Schedules,
                    f.Domestic, f.FamilyLabor, f.DueEvents, dayIndex, diag);
            }
        }

        [Test]
        public void CargoRule_NextDayHeatingPlusCookingCappedByReserve()
        {
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 10);
            HouseholdStoveRegistry stoves = MakeStoveRegistry("B3 cargo building");
            // Day 306 is January (the default anchor is 1880-03-01, so
            // day 306 is 1881-01-01, strain 1.0): a 4-member Basic
            // household burns 3 (the B2 pin). The trip carries tomorrow's
            // burn plus one cooking unit when a meal-prep task is planned.
            Assert.AreEqual(3, HouseholdHeatingEvaluator.CalculateDailyFuelUseUnits(household, JanuaryDay + 1),
                "January 4-member Basic burns 3 — the B2 heating pin.");
            Assert.AreEqual(4, DomesticLaborDriver.ComputeFuelTripCargo(household, JanuaryDay, stoves, hasMealPrepToday: true));
            Assert.AreEqual(3, DomesticLaborDriver.ComputeFuelTripCargo(household, JanuaryDay, stoves, hasMealPrepToday: false));
            household.GetReserve("fuel_wood").currentUnits = 2;
            Assert.AreEqual(2, DomesticLaborDriver.ComputeFuelTripCargo(household, JanuaryDay, stoves, hasMealPrepToday: true),
                "the cargo is capped by the reserve on hand");
            Assert.AreEqual(0, DomesticLaborDriver.ComputeFuelTripCargo(household, JanuaryDay, null, hasMealPrepToday: true));
            Assert.AreEqual(0, DomesticLaborDriver.ComputeFuelTripCargo(null, JanuaryDay, stoves, hasMealPrepToday: true));
            // July burns nothing: the trip carries only the cooking unit.
            int julyDay = CalendarAnchor.Default.ToAbsoluteDay(1880, 7, 15);
            Assert.AreEqual(0, HouseholdHeatingEvaluator.CalculateDailyFuelUseUnits(household, julyDay));
            Assert.AreEqual(1, DomesticLaborDriver.ComputeFuelTripCargo(household, julyDay, stoves, hasMealPrepToday: true));
            Assert.AreEqual(0, DomesticLaborDriver.ComputeFuelTripCargo(household, julyDay, stoves, hasMealPrepToday: false));
        }

        [Test]
        public void FuelTrip_TwoLegArrival_FeedsStoveAndSchedulesDueEventOnlyAfterDelivery()
        {
            var f = new DriverFixture();
            HouseholdStoveRegistry stoves = MakeStoveRegistry("B3 fuel building");
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 10);
            HouseholdInventory inventory = f.Inventories.GetOrCreate(HouseholdId);
            var diag = new List<string>();
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(HouseholdId, household, stoves, inventory, 3, 4);
            inputs.FuelWoodReserveUnits = 10;
            f.Driver.DriveDay(inputs, JanuaryDay, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(1, f.Driver.Pending.Count(t => t.DefinitionId == DomesticTaskCatalog.FuelHandlingId),
                "the fuel-handling task is planned");
            Assert.AreEqual(1, f.Driver.Pending.Count(t => t.DefinitionId == DomesticTaskCatalog.MealPrepId),
                "a meal-prep task is planned today, so the cargo carries a cooking unit");

            var route = new StubRoute();
            float fuelStart = StartSeconds(JanuaryDay, PlannedTask(f.Driver, DomesticTaskCatalog.FuelHandlingId));
            f.Driver.AdvanceTick(fuelStart, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag, route.Result);

            // Leg 0 arrival (firewood pile): the cargo is picked up and
            // held in transit — the reserve is NOT debited yet, and no
            // due event is scheduled (the work has not started).
            DomesticLaborDriver.InFlightDomesticTask flight = InFlightTask(f.Driver, DomesticTaskCatalog.FuelHandlingId);
            Assert.AreEqual(1, flight.FuelHandlingLeg, "the trip advances to leg 1 after the pickup");
            Assert.AreEqual(4, flight.FuelUnitsInTransit, "next-day heating (3) plus one cooking unit");
            Assert.AreEqual("stove-1", flight.FuelStoveAssetId);
            Assert.AreEqual(string.Empty, flight.DueEventId, "no due event after the pickup — the trip is still travelling");
            Assert.AreEqual(10, household.GetReserve("fuel_wood").currentUnits, "the reserve is debited only on stove receipt");
            Assert.AreEqual(0, stoves.FuelWoodUnitsOnHand("stove-1"));

            // Leg 1 arrival (stove): the carried fuel is delivered into
            // the store — the reserve is debited exactly what the store
            // is credited — and only then is the due event scheduled.
            float leg2 = fuelStart + 60f;
            f.Driver.AdvanceTick(leg2, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag, route.Result);
            flight = InFlightTask(f.Driver, DomesticTaskCatalog.FuelHandlingId);
            Assert.AreEqual(0, flight.FuelUnitsInTransit, "the hold converts into custody on delivery");
            Assert.AreEqual(string.Empty, flight.FuelStoveAssetId);
            Assert.IsFalse(string.IsNullOrEmpty(flight.DueEventId), "the due event is scheduled only after delivery");
            Assert.AreEqual(6, household.GetReserve("fuel_wood").currentUnits);
            Assert.AreEqual(4, stoves.FuelWoodUnitsOnHand("stove-1"));
            Assert.AreEqual(10, stoves.TotalFuelWoodUnits(household), "feeding moves fuel, never creates or destroys it");

            // The handling work runs and completes with family-labour credit.
            CompleteDueTask(f, f.Driver, leg2 + 30 * 60f, JanuaryDay, diag);
            Assert.AreEqual(1, f.FamilyLabor.ContributionCount, "the completed fuel trip credits family labour once");
            Assert.AreEqual(0, f.Driver.InFlight.Count);
            Assert.AreEqual(6, household.GetReserve("fuel_wood").currentUnits);
            Assert.AreEqual(4, stoves.FuelWoodUnitsOnHand("stove-1"));
        }

        [Test]
        public void FuelTrip_LegOneArrivalUnsupported_ReleasesHoldAndDefersWithNothingLost()
        {
            var f = new DriverFixture();
            HouseholdStoveRegistry stoves = MakeStoveRegistry("B3 failed-trip building");
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 10);
            HouseholdInventory inventory = f.Inventories.GetOrCreate(HouseholdId);
            var diag = new List<string>();
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(HouseholdId, household, stoves, inventory, 3, 4);
            inputs.FuelWoodReserveUnits = 10;
            f.Driver.DriveDay(inputs, JanuaryDay, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);

            var route = new StubRoute { Leg1 = DomesticActivityMovementResult.Unsupported };
            float fuelStart = StartSeconds(JanuaryDay, PlannedTask(f.Driver, DomesticTaskCatalog.FuelHandlingId));
            f.Driver.AdvanceTick(fuelStart, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag, route.Result);
            Assert.AreEqual(4, InFlightTask(f.Driver, DomesticTaskCatalog.FuelHandlingId).FuelUnitsInTransit,
                "the pickup happened before the stove leg failed");

            float leg2 = fuelStart + 60f;
            f.Driver.AdvanceTick(leg2, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag, route.Result);
            Assert.AreEqual(0, f.Driver.InFlight.Count, "the failed trip is removed");
            Assert.AreEqual(10, household.GetReserve("fuel_wood").currentUnits, "the reserve is whole — the hold was released, nothing was debited");
            Assert.AreEqual(0, stoves.FuelWoodUnitsOnHand("stove-1"));
            Assert.AreEqual(0, f.FamilyLabor.ContributionCount, "a failed trip earns no labour credit");
            Assert.IsTrue(string.Join("\n", diag).Contains("released the in-transit hold"),
                "the hold release is recorded loudly, got: " + string.Join(" | ", diag));
            WorkTask open = f.Tasks.GetOpenTasks(EntityId.For(EntityKind.Household, HouseholdId))
                .First(t => t.DefinitionId == DomesticTaskCatalog.FuelHandlingId);
            Assert.AreEqual(TaskStatus.Queued, open.Status, "the task is requeued without completion");
        }

        [Test]
        public void FuelTrip_DeliveryRefusedWhenReserveDrained_ReleasesHoldAndDefersWithNothingLost()
        {
            var f = new DriverFixture();
            HouseholdStoveRegistry stoves = MakeStoveRegistry("B3 drained-reserve building");
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 10);
            HouseholdInventory inventory = f.Inventories.GetOrCreate(HouseholdId);
            var diag = new List<string>();
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(HouseholdId, household, stoves, inventory, 3, 4);
            inputs.FuelWoodReserveUnits = 10;
            f.Driver.DriveDay(inputs, JanuaryDay, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);

            var route = new StubRoute();
            float fuelStart = StartSeconds(JanuaryDay, PlannedTask(f.Driver, DomesticTaskCatalog.FuelHandlingId));
            f.Driver.AdvanceTick(fuelStart, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag, route.Result);
            Assert.AreEqual(4, InFlightTask(f.Driver, DomesticTaskCatalog.FuelHandlingId).FuelUnitsInTransit);

            // The midnight edge case (plan §5.1 step 3): a burn between
            // pickup and delivery takes the reserved units, so the stove
            // receipt refuses — the trip fails without losing anything.
            household.GetReserve("fuel_wood").currentUnits = 0;
            float leg2 = fuelStart + 60f;
            f.Driver.AdvanceTick(leg2, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag, route.Result);
            Assert.AreEqual(0, f.Driver.InFlight.Count, "the failed trip is removed");
            Assert.AreEqual(0, stoves.FuelWoodUnitsOnHand("stove-1"), "nothing was delivered");
            Assert.AreEqual(0, household.GetReserve("fuel_wood").currentUnits, "nothing was created by the failed delivery");
            Assert.AreEqual(0, f.FamilyLabor.ContributionCount, "a failed trip earns no labour credit");
            Assert.IsTrue(string.Join("\n", diag).Contains("failed at the stove"),
                "the delivery failure is recorded loudly, got: " + string.Join(" | ", diag));
        }

        [Test]
        public void FuelTrip_NoStove_NoHandlingTaskPlanned()
        {
            var f = new DriverFixture();
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 10);
            var diag = new List<string>();
            // An empty registry: no installed stove at all.
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(HouseholdId, household, MakeRegistry(MakeRegister()), null, 3, 4);
            inputs.FuelWoodReserveUnits = 10;
            f.Driver.DriveDay(inputs, 0, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(0, f.Driver.Pending.Count(t => t.DefinitionId == DomesticTaskCatalog.FuelHandlingId),
                "no stove means no fuel-handling task — the need is a procurement need, not a trip");
            Assert.AreEqual(1, f.Driver.Pending.Count(t => t.DefinitionId == DomesticTaskCatalog.MealPrepId),
                "meal-prep is still planned (cold-food fallback)");
            Assert.IsTrue(string.Join("\n", diag).Contains("no installed stove"),
                "the guard is recorded loudly, got: " + string.Join(" | ", diag));
        }

        [Test]
        public void Cooking_StoveCooking_BurnsOneFuelAndProducesOneLoaf()
        {
            var f = new DriverFixture();
            HouseholdStoveRegistry stoves = MakeStoveRegistry("B3 cooking building");
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 10);
            HouseholdInventory inventory = f.Inventories.GetOrCreate(HouseholdId);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 10, JanuaryDay, "general store purchase"));
            var diag = new List<string>();
            Assert.IsNull(stoves.TryFeedStove("stove-1", 4, household, diag));
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(HouseholdId, household, stoves, inventory, 3, 4);
            inputs.FuelHandlingNeeded = false;
            inputs.FuelWoodReserveUnits = 6;
            f.Driver.DriveDay(inputs, JanuaryDay, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);
            DomesticLaborDriver.PendingDomesticTask planned = PlannedTask(f.Driver, DomesticTaskCatalog.MealPrepId);
            Assert.AreEqual(HouseholdStoveMealPrepMode.StoveCooking, planned.MealPrepMode);
            Assert.AreEqual(0, f.MealLog.GetMeals(HouseholdId).Count);

            var route = new StubRoute();
            float mealStart = StartSeconds(JanuaryDay, planned);
            f.Driver.AdvanceTick(mealStart, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag, route.Result);
            Assert.AreEqual(1, f.Driver.InFlight.Count);
            Assert.AreEqual(HouseholdStoveMealPrepMode.StoveCooking, InFlightTask(f.Driver, DomesticTaskCatalog.MealPrepId).MealPrepMode,
                "the mode travels with the flight to the settlement");

            CompleteDueTask(f, f.Driver, mealStart + 45 * 60f, JanuaryDay, diag);
            Assert.AreEqual(1, f.FamilyLabor.ContributionCount, "the completed meal-prep credits family labour once");
            Assert.AreEqual(3, stoves.FuelWoodUnitsOnHand("stove-1"), "cooking burns exactly one stove fuel unit");
            Assert.AreEqual(8, inventory.GetAvailableUnits(HouseholdItemCatalog.FlourId), "cooking consumes exactly 2 flour");
            Assert.AreEqual(1, inventory.GetAvailableUnits(HouseholdItemCatalog.BreadId), "cooking produces exactly 1 loaf");
            Assert.AreEqual(1, f.MealLog.GetPreparations(HouseholdId).Count, "one production record");
            Assert.AreEqual(0, f.MealLog.GetMeals(HouseholdId).Count, "cooking serves no meal and changes no meal count (DEF-015)");
        }

        [Test]
        public void Cooking_NoFuelInStore_ProducesNothingAndBurnsNothing()
        {
            var f = new DriverFixture();
            HouseholdStoveRegistry stoves = MakeStoveRegistry("B3 no-fuel cooking building");
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 10);
            HouseholdInventory inventory = f.Inventories.GetOrCreate(HouseholdId);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 10, JanuaryDay, "general store purchase"));
            var diag = new List<string>();
            // The store is empty; the gate still cooks on the stove
            // because the reserve holds fuel — the settlement is where
            // the missing stove fuel is caught.
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(HouseholdId, household, stoves, inventory, 3, 4);
            inputs.FuelHandlingNeeded = false;
            inputs.FuelWoodReserveUnits = 10;
            f.Driver.DriveDay(inputs, JanuaryDay, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(HouseholdStoveMealPrepMode.StoveCooking, PlannedTask(f.Driver, DomesticTaskCatalog.MealPrepId).MealPrepMode);

            var route = new StubRoute();
            float mealStart = StartSeconds(JanuaryDay, PlannedTask(f.Driver, DomesticTaskCatalog.MealPrepId));
            f.Driver.AdvanceTick(mealStart, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag, route.Result);
            CompleteDueTask(f, f.Driver, mealStart + 45 * 60f, JanuaryDay, diag);
            Assert.AreEqual(1, f.FamilyLabor.ContributionCount, "the labour credit stands — the time was genuinely spent");
            Assert.AreEqual(0, stoves.FuelWoodUnitsOnHand("stove-1"));
            Assert.AreEqual(10, inventory.GetAvailableUnits(HouseholdItemCatalog.FlourId), "no flour consumed");
            Assert.AreEqual(0, inventory.GetAvailableUnits(HouseholdItemCatalog.BreadId));
            Assert.AreEqual(0, f.MealLog.GetPreparations(HouseholdId).Count, "no production record");
            Assert.IsTrue(string.Join("\n", diag).Contains("no fuel in any stove store"),
                "the refusal is recorded loudly, got: " + string.Join(" | ", diag));
        }

        [Test]
        public void Cooking_NoFlour_ProducesNothingAndBurnsNoFuel()
        {
            var f = new DriverFixture();
            HouseholdStoveRegistry stoves = MakeStoveRegistry("B3 no-flour cooking building");
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 10);
            HouseholdInventory inventory = f.Inventories.GetOrCreate(HouseholdId);
            var diag = new List<string>();
            Assert.IsNull(stoves.TryFeedStove("stove-1", 4, household, diag));
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(HouseholdId, household, stoves, inventory, 3, 4);
            inputs.FuelHandlingNeeded = false;
            inputs.FuelWoodReserveUnits = 6;
            f.Driver.DriveDay(inputs, JanuaryDay, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(HouseholdStoveMealPrepMode.StoveCooking, PlannedTask(f.Driver, DomesticTaskCatalog.MealPrepId).MealPrepMode);

            var route = new StubRoute();
            float mealStart = StartSeconds(JanuaryDay, PlannedTask(f.Driver, DomesticTaskCatalog.MealPrepId));
            f.Driver.AdvanceTick(mealStart, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag, route.Result);
            CompleteDueTask(f, f.Driver, mealStart + 45 * 60f, JanuaryDay, diag);
            Assert.AreEqual(1, f.FamilyLabor.ContributionCount);
            Assert.AreEqual(4, stoves.FuelWoodUnitsOnHand("stove-1"), "no fuel burned when there is nothing to cook with");
            Assert.AreEqual(0, inventory.GetAvailableUnits(HouseholdItemCatalog.BreadId));
            Assert.AreEqual(0, f.MealLog.GetPreparations(HouseholdId).Count, "no production record");
            Assert.IsTrue(string.Join("\n", diag).Contains("no flour"),
                "the refusal is recorded loudly, got: " + string.Join(" | ", diag));
        }

        [Test]
        public void Cooking_ColdFoodFallback_ProducesNothingAndCreditsLabour()
        {
            var f = new DriverFixture();
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 10);
            HouseholdInventory inventory = f.Inventories.GetOrCreate(HouseholdId);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 10, JanuaryDay, "general store purchase"));
            var diag = new List<string>();
            // No stove at all: the task falls back to cold food.
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(HouseholdId, household, MakeRegistry(MakeRegister()), inventory, 3, 4);
            inputs.FuelHandlingNeeded = false;
            inputs.FuelWoodReserveUnits = 10;
            f.Driver.DriveDay(inputs, JanuaryDay, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(HouseholdStoveMealPrepMode.ColdFoodFallback, PlannedTask(f.Driver, DomesticTaskCatalog.MealPrepId).MealPrepMode);

            var route = new StubRoute();
            float mealStart = StartSeconds(JanuaryDay, PlannedTask(f.Driver, DomesticTaskCatalog.MealPrepId));
            f.Driver.AdvanceTick(mealStart, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag, route.Result);
            CompleteDueTask(f, f.Driver, mealStart + 45 * 60f, JanuaryDay, diag);
            Assert.AreEqual(1, f.FamilyLabor.ContributionCount, "the fallback task still credits family labour");
            Assert.AreEqual(10, inventory.GetAvailableUnits(HouseholdItemCatalog.FlourId), "the fallback cooks nothing");
            Assert.AreEqual(0, inventory.GetAvailableUnits(HouseholdItemCatalog.BreadId));
            Assert.AreEqual(0, f.MealLog.GetPreparations(HouseholdId).Count);
            Assert.AreEqual(0, f.MealLog.GetMeals(HouseholdId).Count);
        }

        [Test]
        public void Cooking_MissingContext_SettlesNothingAndSaysSoLoudly()
        {
            var f = new DriverFixture();
            // No DriveDay at all: the transient context maps are empty,
            // as after a mid-day Continue before the next day boundary.
            var flight = new DomesticLaborDriver.InFlightDomesticTask
            {
                HouseholdId = HouseholdId,
                PersonId = 3,
                DefinitionId = DomesticTaskCatalog.MealPrepId,
                DayIndex = JanuaryDay,
                MealPrepMode = HouseholdStoveMealPrepMode.StoveCooking,
            };
            var diag = new List<string>();
            f.Driver.SettleCooking(flight, JanuaryDay, 45, diag);
            Assert.IsTrue(string.Join("\n", diag).Contains("no stove registry"),
                "the missing context is recorded loudly, got: " + string.Join(" | ", diag));
        }

        [Test]
        public void Heating_FedStoreDebitsStoreFirstThenReserve()
        {
            HouseholdStoveRegistry stoves = MakeStoveRegistry("B3 heating fed building");
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 10);
            var diag = new List<string>();
            Assert.IsNull(stoves.TryFeedStove("stove-1", 4, household, diag));
            int totalBefore = stoves.TotalFuelWoodUnits(household);
            Assert.AreEqual(10, totalBefore);
            int consumed = HouseholdHeatingEvaluator.ResolveDailyHeatingUse(
                household, household.GetReserve("fuel_wood"), JanuaryDay, CalendarAnchor.Default, stoves);
            Assert.AreEqual(3, consumed, "January 4-member Basic burns 3 (the B2 pin).");
            Assert.AreEqual(1, stoves.FuelWoodUnitsOnHand("stove-1"), "the store pays first");
            Assert.AreEqual(6, household.GetReserve("fuel_wood").currentUnits, "the reserve is untouched while the store covers the burn");
            Assert.AreEqual(totalBefore - consumed, stoves.TotalFuelWoodUnits(household), "one burn, one debit");
            Assert.AreEqual(3, household.lastHeatingFuelUseUnits);
            Assert.AreEqual(0, household.lastHeatingFuelShortfallUnits);
        }

        [Test]
        public void Heating_EmptyStoreDebitsReserveAsToday()
        {
            HouseholdStoveRegistry stoves = MakeStoveRegistry("B3 heating empty building");
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 10);
            int totalBefore = stoves.TotalFuelWoodUnits(household);
            int consumed = HouseholdHeatingEvaluator.ResolveDailyHeatingUse(
                household, household.GetReserve("fuel_wood"), JanuaryDay, CalendarAnchor.Default, stoves);
            Assert.AreEqual(3, consumed);
            Assert.AreEqual(0, stoves.FuelWoodUnitsOnHand("stove-1"));
            Assert.AreEqual(7, household.GetReserve("fuel_wood").currentUnits);
            Assert.AreEqual(totalBefore - consumed, stoves.TotalFuelWoodUnits(household));
        }

        [Test]
        public void Heating_PartiallyFedStoreSplitsBetweenStoreAndReserve()
        {
            HouseholdStoveRegistry stoves = MakeStoveRegistry("B3 heating partial building");
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 10);
            var diag = new List<string>();
            Assert.IsNull(stoves.TryFeedStove("stove-1", 2, household, diag));
            int totalBefore = stoves.TotalFuelWoodUnits(household);
            int consumed = HouseholdHeatingEvaluator.ResolveDailyHeatingUse(
                household, household.GetReserve("fuel_wood"), JanuaryDay, CalendarAnchor.Default, stoves);
            Assert.AreEqual(3, consumed);
            Assert.AreEqual(0, stoves.FuelWoodUnitsOnHand("stove-1"), "the store is emptied first");
            Assert.AreEqual(7, household.GetReserve("fuel_wood").currentUnits, "the remainder comes from the reserve");
            Assert.AreEqual(totalBefore - consumed, stoves.TotalFuelWoodUnits(household), "one burn, one debit");
        }

        [Test]
        public void Heating_NullRegistryKeepsTodaysBehaviour()
        {
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 10);
            int consumed = HouseholdHeatingEvaluator.ResolveDailyHeatingUse(
                household, household.GetReserve("fuel_wood"), JanuaryDay, CalendarAnchor.Default);
            Assert.AreEqual(3, consumed);
            Assert.AreEqual(7, household.GetReserve("fuel_wood").currentUnits, "without a registry the burn debits the reserve as today");
        }

        [Test]
        public void Heating_ResolveDailyUseThreadsStoveRegistry()
        {
            HouseholdStoveRegistry stoves = MakeStoveRegistry("B3 heating thread building");
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 10);
            var diag = new List<string>();
            Assert.IsNull(stoves.TryFeedStove("stove-1", 4, household, diag));
            HouseholdReserveEvaluator.ResolveDailyUse(household, JanuaryDay, CalendarAnchor.Default, stoves);
            Assert.AreEqual(1, stoves.FuelWoodUnitsOnHand("stove-1"), "the store pays first through the evaluator");
            Assert.AreEqual(6, household.GetReserve("fuel_wood").currentUnits);
            Assert.AreEqual(3, household.lastHeatingFuelUseUnits);
        }

        [Test]
        public void HeatingAndStoveCustody_ConsumeAndReturnTheSamePhysicalFuelLots()
        {
            HouseholdStoveRegistry stoves = MakeStoveRegistry("B3 lot conservation building");
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 10);
            HouseholdInventory inventory = new HouseholdInventory(HouseholdId);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FuelWoodId, 10, JanuaryDay,
                "paid fuel dealer receipt"));
            inventory.State.SeededFromReserves = true;
            stoves.Inventory = inventory;

            var diag = new List<string>();
            Assert.IsNull(stoves.TryFeedStove("stove-1", 4, household, diag));
            Assert.AreEqual(6, inventory.GetAvailableUnits(HouseholdItemCatalog.FuelWoodId),
                "feeding moves the same units out of household lots and into stove custody");
            Assert.AreEqual(6, household.GetReserve("fuel_wood").currentUnits,
                "the reserve remains a mirror of the remaining household lot stock");
            Assert.AreEqual(4, stoves.FuelWoodUnitsOnHand("stove-1"));

            int consumed = HouseholdHeatingEvaluator.ResolveDailyHeatingUse(
                household, household.GetReserve("fuel_wood"), JanuaryDay,
                CalendarAnchor.Default, stoves);
            Assert.AreEqual(3, consumed);
            Assert.AreEqual(6, inventory.GetAvailableUnits(HouseholdItemCatalog.FuelWoodId),
                "the burn used fuel already transferred into the stove; it must not debit pantry lots twice");
            Assert.AreEqual(1, stoves.FuelWoodUnitsOnHand("stove-1"));

            Assert.IsNull(stoves.TryReturnStoveFuelToReserve("stove-1", household, diag));
            Assert.AreEqual(7, inventory.GetAvailableUnits(HouseholdItemCatalog.FuelWoodId),
                "returning unburned stove fuel restores the real household lot quantity");
            Assert.AreEqual(7, household.GetReserve("fuel_wood").currentUnits);
            Assert.AreEqual(0, stoves.FuelWoodUnitsOnHand("stove-1"));
        }

        [Test]
        public void Heating_ReserveBurnConsumesTheMatchingPhysicalFuelLot()
        {
            HouseholdStoveRegistry stoves = MakeStoveRegistry("B3 reserve burn lot building");
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 10);
            HouseholdInventory inventory = new HouseholdInventory(HouseholdId);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FuelWoodId, 10, JanuaryDay,
                "paid fuel dealer receipt"));
            inventory.State.SeededFromReserves = true;
            stoves.Inventory = inventory;

            int consumed = HouseholdHeatingEvaluator.ResolveDailyHeatingUse(
                household, household.GetReserve("fuel_wood"), JanuaryDay,
                CalendarAnchor.Default, stoves);

            Assert.AreEqual(3, consumed);
            Assert.AreEqual(7, inventory.GetAvailableUnits(HouseholdItemCatalog.FuelWoodId),
                "direct reserve burn must consume actual owned fuel lots");
            Assert.AreEqual(7, household.GetReserve("fuel_wood").currentUnits,
                "the reserve mirror falls by the same physical quantity");
        }

        [Test]
        public void SaveLoad_FuelTripMidTripRoundTripsWithHoldIntactAndCompletesAfterRestore()
        {
            var f = new DriverFixture();
            HouseholdStoveRegistry stoves = MakeStoveRegistry("B3 saveload building");
            HouseholdState household = CreateHouseholdWithFuelReserve(HouseholdId, 10);
            HouseholdInventory inventory = f.Inventories.GetOrCreate(HouseholdId);
            var diag = new List<string>();
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(HouseholdId, household, stoves, inventory, 3, 4);
            inputs.FuelWoodReserveUnits = 10;
            f.Driver.DriveDay(inputs, JanuaryDay, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);

            var route = new StubRoute();
            float fuelStart = StartSeconds(JanuaryDay, PlannedTask(f.Driver, DomesticTaskCatalog.FuelHandlingId));
            f.Driver.AdvanceTick(fuelStart, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag, route.Result);
            DomesticLaborDriver.InFlightDomesticTask flight = InFlightTask(f.Driver, DomesticTaskCatalog.FuelHandlingId);
            Assert.AreEqual(1, flight.FuelHandlingLeg);
            Assert.AreEqual(4, flight.FuelUnitsInTransit);

            // A Continue mid-trip: the flight round-trips with its leg
            // and its hold intact.
            DomesticLaborDriverSaveDto dto = f.Driver.CaptureSaveDto();
            var restoredDriver = new DomesticLaborDriver();
            restoredDriver.LoadFromSaveDto(dto);
            DomesticLaborDriver.InFlightDomesticTask restoredFlight = InFlightTask(restoredDriver, DomesticTaskCatalog.FuelHandlingId);
            Assert.AreEqual(1, restoredFlight.FuelHandlingLeg);
            Assert.AreEqual(4, restoredFlight.FuelUnitsInTransit);
            Assert.AreEqual("stove-1", restoredFlight.FuelStoveAssetId);
            Assert.AreEqual(flight.TaskId, restoredFlight.TaskId, "the same authoritative task resumes");

            // The restored driver has no context until the next
            // DriveDay repopulates it — then the trip completes.
            DomesticLaborDriver.DomesticDayInputs restoredInputs = f.Inputs(HouseholdId, household, stoves, inventory, 3, 4);
            restoredInputs.FuelWoodReserveUnits = 10;
            restoredDriver.DriveDay(restoredInputs, JanuaryDay, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(1, restoredDriver.InFlight.Count, "the restored trip is not duplicated by the new DriveDay");
            float leg2 = fuelStart + 60f;
            restoredDriver.AdvanceTick(leg2, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag, route.Result);
            Assert.AreEqual(0, InFlightTask(restoredDriver, DomesticTaskCatalog.FuelHandlingId).FuelUnitsInTransit);
            Assert.AreEqual(6, household.GetReserve("fuel_wood").currentUnits);
            Assert.AreEqual(4, stoves.FuelWoodUnitsOnHand("stove-1"));
            CompleteDueTask(f, restoredDriver, leg2 + 30 * 60f, JanuaryDay, diag);
            Assert.AreEqual(1, f.FamilyLabor.ContributionCount);
        }
    }
}
