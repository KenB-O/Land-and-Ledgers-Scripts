using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Population;
using LandLedgers.Time;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// B4 (Agent B, expanded campaign 2026-11-16): dining seats +
    /// planner hook. Proves the dining registry (chair-at-a-table
    /// resolution, table-required rule, one chair one diner one meal
    /// window, fallback eating that never blocks a meal, release
    /// after the meal, the uncovered-diner need signal), the
    /// DailyActivityPlanner meal hook (seat id and mode on the Eating
    /// activities), and the physical-arrival path check through the
    /// interior route engine. No test claims Unity compilation or
    /// PlayMode; tables and chairs have no tracked prefab in this
    /// checkout (Codex instruction 1), so live placement waits on
    /// Codex publishing them, and the per-tick movement to the chair
    /// is Agent G's wiring.
    /// </summary>
    [TestFixture]
    public sealed class DiningPlaceRegistryTests
    {
        private const int HouseholdId = 7;
        private const int BuildingInstanceId = 101;
        private const int OtherBuildingInstanceId = 102;
        private const int Day = 100;
        private const int MorningMeal = 450;
        private const int EveningMeal = 1110;
        private const int MealDuration = 30;

        private static HouseholdFurnishingRegister MakeRegister(int householdId = HouseholdId)
        {
            return new HouseholdFurnishingRegister { HouseholdId = householdId };
        }

        private static EquipmentAsset MakeChair(string assetId, int householdId, float condition = 1f)
        {
            return new EquipmentAsset
            {
                AssetId = assetId,
                Kind = "furniture.chair",
                DisplayName = "Chair",
                Condition01 = condition,
                OwnerKind = "household",
                OwnerId = householdId.ToString(),
            };
        }

        private static EquipmentAsset MakeTable(string assetId, int householdId, float condition = 1f)
        {
            return new EquipmentAsset
            {
                AssetId = assetId,
                Kind = "furniture.table",
                DisplayName = "Table",
                Condition01 = condition,
                OwnerKind = "household",
                OwnerId = householdId.ToString(),
            };
        }

        private static List<InteriorFloorAuthoring> GetInteriorFloors(BuildingFootprintAuthority authority)
        {
            FieldInfo field = typeof(BuildingFootprintAuthority).GetField("interiorFloors", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (List<InteriorFloorAuthoring>)field.GetValue(authority);
        }

        private static List<BuildingEntrancePairAuthoring> GetEntrancePairs(BuildingFootprintAuthority authority)
        {
            FieldInfo field = typeof(BuildingFootprintAuthority).GetField("entrancePairs", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (List<BuildingEntrancePairAuthoring>)field.GetValue(authority);
        }

        private static List<BuildingActivityLocationAuthoring> GetActivityLocations(BuildingFootprintAuthority authority)
        {
            FieldInfo field = typeof(BuildingFootprintAuthority).GetField("activityLocations", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (List<BuildingActivityLocationAuthoring>)field.GetValue(authority);
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

        /// <summary>A building with a main entrance and authored dining locations, for arrival-path tests.</summary>
        private static BuildingFootprintAuthority MakeBuildingWithDiningLocation(string name)
        {
            BuildingFootprintAuthority authority = MakeBuilding(name);
            GetEntrancePairs(authority).Add(new BuildingEntrancePairAuthoring
            {
                id = "main-entrance",
                indoorPositionLocal = new Vector3(-2f, 0f, -2f),
                outdoorApproachLocal = new Vector3(-2f, 0f, -4f),
                clearWidthMeters = 1.1f,
                floorIndex = 0,
            });
            GetActivityLocations(authority).Add(new BuildingActivityLocationAuthoring
            {
                id = "dining",
                displayName = "Dining approach",
                type = BuildingActivityLocationType.Dining,
                floorIndex = 0,
                positionLocal = new Vector3(0f, 0f, 0f),
                forwardLocal = Vector3.forward,
                requiresWalkableInterior = true,
            });
            GetActivityLocations(authority).Add(new BuildingActivityLocationAuthoring
            {
                id = "dining-chair-1",
                displayName = "Chair 1 access",
                type = BuildingActivityLocationType.Dining,
                floorIndex = 0,
                positionLocal = new Vector3(1f, 0f, 0f),
                forwardLocal = Vector3.forward,
                requiresWalkableInterior = true,
            });
            return authority;
        }

        private static BuildingEquipmentPlacement MakePlacement(string name, Vector3 position)
        {
            GameObject item = new(name);
            item.transform.position = position;
            return item.AddComponent<BuildingEquipmentPlacement>();
        }

        private static FurnishingPlacementBinding InstallChair(
            HouseholdFurnishingRegister register,
            BuildingFootprintAuthority building,
            BuildingEquipmentPlacement placement,
            string assetId = "chair-1",
            string activityLocationId = "dining-chair-1",
            int buildingInstanceId = BuildingInstanceId,
            FurnishingCapabilityKind capability = FurnishingCapabilityKind.DiningSeat)
        {
            string error = register.TryInstall(assetId, placement, building, buildingInstanceId, 0,
                capability, activityLocationId,
                FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                out FurnishingPlacementBinding binding);
            Assert.IsNull(error, error);
            Assert.NotNull(binding);
            return binding;
        }

        private static FurnishingPlacementBinding InstallTable(
            HouseholdFurnishingRegister register,
            BuildingFootprintAuthority building,
            BuildingEquipmentPlacement placement,
            string assetId = "table-1",
            string activityLocationId = "dining",
            int buildingInstanceId = BuildingInstanceId,
            FurnishingCapabilityKind capability = FurnishingCapabilityKind.DiningTable)
        {
            string error = register.TryInstall(assetId, placement, building, buildingInstanceId, 0,
                capability, activityLocationId,
                FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                out FurnishingPlacementBinding binding);
            Assert.IsNull(error, error);
            Assert.NotNull(binding);
            return binding;
        }

        /// <summary>One table + N chairs (DiningSeat / DiningTable capabilities) plus the registry, with cleanup.</summary>
        private sealed class DiningFixture
        {
            public GameObject BuildingObject;
            public List<GameObject> ItemObjects = new List<GameObject>();
            public HouseholdFurnishingRegister Register;
            public DiningPlaceRegistry Registry;
        }

        private static DiningFixture MakeDiningFixture(int chairCount = 2, bool withTable = true, string buildingName = "B4 dining fixture")
        {
            BuildingFootprintAuthority building = MakeBuilding(buildingName);
            var fixture = new DiningFixture { BuildingObject = building.gameObject };
            var register = MakeRegister();
            if (withTable)
            {
                Assert.IsNull(register.TryRegisterAsset(MakeTable("table-1", HouseholdId)));
                BuildingEquipmentPlacement tablePlacement = MakePlacement("Table", Vector3.zero);
                fixture.ItemObjects.Add(tablePlacement.gameObject);
                InstallTable(register, building, tablePlacement);
            }
            var chairPositions = new[] { new Vector3(1f, 0f, 0f), new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, 1f) };
            for (int i = 0; i < chairCount; i++)
            {
                string assetId = "chair-" + (i + 1);
                Assert.IsNull(register.TryRegisterAsset(MakeChair(assetId, HouseholdId)));
                BuildingEquipmentPlacement chairPlacement = MakePlacement("Chair " + (i + 1), chairPositions[i]);
                fixture.ItemObjects.Add(chairPlacement.gameObject);
                InstallChair(register, building, chairPlacement, assetId, "dining-chair-" + (i + 1));
            }
            fixture.Register = register;
            fixture.Registry = MakeRegistry(register);
            return fixture;
        }

        private static void DestroyFixture(DiningFixture fixture)
        {
            if (fixture == null) return;
            foreach (GameObject item in fixture.ItemObjects)
            {
                if (item != null) Object.DestroyImmediate(item);
            }
            if (fixture.BuildingObject != null) Object.DestroyImmediate(fixture.BuildingObject);
        }

        private static DiningPlaceRegistry MakeRegistry(HouseholdFurnishingRegister register)
        {
            return new DiningPlaceRegistry
            {
                HouseholdId = HouseholdId,
                Furnishings = register,
                HomeBuildingInstanceId = BuildingInstanceId,
            };
        }

        private static DailyActivityPlanner.PersonDayPlanInputs Inputs(int personId)
        {
            return new DailyActivityPlanner.PersonDayPlanInputs
            {
                PersonId = personId,
                HouseholdId = HouseholdId,
                AgeBand = AgeBand.Adult18Plus,
            };
        }

        private static List<DailyActivityPlanner.PlannedActivity> MealActivities(DailyActivityPlanner.PersonDayPlan plan)
        {
            var meals = new List<DailyActivityPlanner.PlannedActivity>();
            foreach (DailyActivityPlanner.PlannedActivity activity in plan.Activities)
            {
                if (activity.Kind == PersonActivityKind.Eating) meals.Add(activity);
            }
            return meals;
        }

        [Test]
        public void Resolution_SeatedWhenTableAndChair()
        {
            DiningFixture fixture = null;
            try
            {
                fixture = MakeDiningFixture(chairCount: 1);
                var registry = fixture.Registry;
                Assert.AreEqual(1, registry.InstalledTableCount());
                Assert.AreEqual(1, registry.InstalledChairSeatCount());

                Assert.IsNull(registry.TryReserveDiningSeat(1, Day, MorningMeal, MealDuration, out DiningSeatReservation reservation));
                Assert.AreEqual(DiningMode.Seated, reservation.Mode);
                Assert.AreEqual("chair:chair-1", reservation.SeatId);
                Assert.AreEqual("chair-1", reservation.AssetId);
                Assert.AreEqual(0, registry.Validate().Count, string.Join("; ", registry.Validate()));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void NoTable_FallsBack_ChairIsPlacedButNotASeat()
        {
            DiningFixture fixture = null;
            try
            {
                fixture = MakeDiningFixture(chairCount: 2, withTable: false);
                var registry = fixture.Registry;
                Assert.AreEqual(0, registry.InstalledTableCount());
                Assert.AreEqual(0, registry.InstalledChairSeatCount(), "Chairs in a building with no table are not dining seats.");

                Assert.IsNull(registry.TryReserveDiningSeat(1, Day, MorningMeal, MealDuration, out DiningSeatReservation reservation));
                Assert.AreEqual(DiningMode.Fallback, reservation.Mode);
                Assert.AreEqual(string.Empty, reservation.SeatId);

                List<string> problems = registry.Validate();
                Assert.AreEqual(2, problems.Count, string.Join("; ", problems));
                Assert.IsTrue(problems[0].Contains("no table"), problems[0]);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void NoChair_FallsBack()
        {
            DiningFixture fixture = null;
            try
            {
                fixture = MakeDiningFixture(chairCount: 0);
                var registry = fixture.Registry;
                Assert.AreEqual(1, registry.InstalledTableCount());
                Assert.AreEqual(0, registry.InstalledChairSeatCount());

                Assert.IsNull(registry.TryReserveDiningSeat(1, Day, MorningMeal, MealDuration, out DiningSeatReservation reservation));
                Assert.AreEqual(DiningMode.Fallback, reservation.Mode);
                Assert.AreEqual(0, registry.Validate().Count, string.Join("; ", registry.Validate()));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void NoFurniture_FallsBack()
        {
            // No table and no chairs: the meal still happens, at home, with no seat.
            var registry = MakeRegistry(MakeRegister());
            Assert.AreEqual(0, registry.InstalledTableCount());
            Assert.AreEqual(0, registry.InstalledChairSeatCount());

            Assert.IsNull(registry.TryReserveDiningSeat(1, Day, MorningMeal, MealDuration, out DiningSeatReservation reservation));
            Assert.AreEqual(DiningMode.Fallback, reservation.Mode);
            Assert.AreEqual(0, registry.Validate().Count, string.Join("; ", registry.Validate()));
        }

        [Test]
        public void TableSeatsOnlyAsManyAsItsChairs()
        {
            DiningFixture fixture = null;
            try
            {
                fixture = MakeDiningFixture(chairCount: 2);
                var registry = fixture.Registry;
                var members = new List<int> { 1, 2, 3 };

                Assert.IsNull(registry.AssignHouseholdDining(members, Day, MorningMeal, MealDuration));
                Assert.AreEqual(DiningMode.Seated, registry.LiveReservationFor(1, Day, MorningMeal).Mode);
                Assert.AreEqual(DiningMode.Seated, registry.LiveReservationFor(2, Day, MorningMeal).Mode);
                Assert.AreEqual(DiningMode.Fallback, registry.LiveReservationFor(3, Day, MorningMeal).Mode);
                List<int> uncovered = registry.UncoveredDiners(members, Day, MorningMeal, MealDuration);
                Assert.AreEqual(1, uncovered.Count, "The uncovered simulation agrees with the live reservations.");
                Assert.AreEqual(3, uncovered[0]);
                Assert.AreEqual(0, registry.Validate().Count, string.Join("; ", registry.Validate()));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void SameChair_TwoMeals_BothSeated()
        {
            // The two-holds proof: one chair serves the morning and the
            // evening meal. An EquipmentAsset hold could not express
            // two concurrent holds on one chair, which is why dining
            // reservations are registry-local and windowed.
            DiningFixture fixture = null;
            try
            {
                fixture = MakeDiningFixture(chairCount: 1);
                var registry = fixture.Registry;

                Assert.IsNull(registry.TryReserveDiningSeat(1, Day, MorningMeal, MealDuration, out DiningSeatReservation morning));
                Assert.AreEqual(DiningMode.Seated, morning.Mode);
                Assert.IsNull(registry.TryReserveDiningSeat(1, Day, EveningMeal, MealDuration, out DiningSeatReservation evening));
                Assert.AreEqual(DiningMode.Seated, evening.Mode);
                Assert.AreEqual("chair:chair-1", morning.SeatId);
                Assert.AreEqual("chair:chair-1", evening.SeatId);
                Assert.AreNotEqual(morning.ReservationId, evening.ReservationId);
                Assert.AreEqual(0, registry.Validate().Count, string.Join("; ", registry.Validate()));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void TwoDiners_OneChair_SecondFallsBack_OtherWindowFree()
        {
            DiningFixture fixture = null;
            try
            {
                fixture = MakeDiningFixture(chairCount: 1);
                var registry = fixture.Registry;

                Assert.IsNull(registry.TryReserveDiningSeat(1, Day, MorningMeal, MealDuration, out DiningSeatReservation first));
                Assert.AreEqual(DiningMode.Seated, first.Mode);

                // The second diner resolves past the held chair to the fallback.
                Assert.IsNull(registry.TryReserveDiningSeat(2, Day, MorningMeal, MealDuration, out DiningSeatReservation second));
                Assert.AreEqual(DiningMode.Fallback, second.Mode);

                // Re-planning the same window reuses the fallback reservation, never double-reserves.
                Assert.IsNull(registry.TryReserveDiningSeat(2, Day, MorningMeal, MealDuration, out DiningSeatReservation again));
                Assert.AreEqual(second.ReservationId, again.ReservationId);

                // A different window on the same chair is free.
                Assert.IsNull(registry.TryReserveDiningSeat(2, Day, EveningMeal, MealDuration, out DiningSeatReservation evening));
                Assert.AreEqual(DiningMode.Seated, evening.Mode);
                Assert.AreEqual(0, registry.Validate().Count, string.Join("; ", registry.Validate()));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Release_AfterMeal_FreesChair()
        {
            DiningFixture fixture = null;
            try
            {
                fixture = MakeDiningFixture(chairCount: 1);
                var registry = fixture.Registry;
                Assert.IsNull(registry.TryReserveDiningSeat(1, Day, MorningMeal, MealDuration, out _));

                Assert.IsTrue(registry.ReleaseDiningSeat(1, Day, MorningMeal));
                Assert.IsNull(registry.LiveReservationFor(1, Day, MorningMeal));

                // The released chair is free again the same window.
                Assert.IsNull(registry.TryReserveDiningSeat(2, Day, MorningMeal, MealDuration, out DiningSeatReservation second));
                Assert.AreEqual(DiningMode.Seated, second.Mode);

                // Releasing twice is a no-op, not a failure.
                Assert.IsFalse(registry.ReleaseDiningSeat(1, Day, MorningMeal));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void EarlierWindow_StaysLiveWhenLaterWindowReserved()
        {
            // At plan time BOTH of the day's meals are still in the
            // future: reserving the evening meal must NOT close the
            // morning reservation (a first version of this registry
            // did exactly that, and the household planner test caught
            // it — P2's morning meal then found P1's seat free).
            // Past windows are inert by construction (every check is
            // a same-day window-overlap check), so no cleanup runs.
            DiningFixture fixture = null;
            try
            {
                fixture = MakeDiningFixture(chairCount: 1);
                var registry = fixture.Registry;
                Assert.IsNull(registry.TryReserveDiningSeat(1, Day, MorningMeal, MealDuration, out DiningSeatReservation morning));
                Assert.AreEqual(DiningMode.Seated, morning.Mode);

                Assert.IsNull(registry.TryReserveDiningSeat(1, Day, EveningMeal, MealDuration, out DiningSeatReservation evening));
                Assert.AreEqual(DiningMode.Seated, evening.Mode);
                Assert.AreEqual(morning.ReservationId, registry.LiveReservationFor(1, Day, MorningMeal).ReservationId,
                    "The morning reservation stays live after the evening one is made.");
                Assert.AreEqual(0, registry.Validate().Count, string.Join("; ", registry.Validate()));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void UnusableChair_NotASeat_TableWithoutCapability_NotATable()
        {
            GameObject buildingObject = null;
            GameObject tableObject = null;
            GameObject chairObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B4 unusable building");
                buildingObject = building.gameObject;
                var register = MakeRegister();
                Assert.IsNull(register.TryRegisterAsset(MakeTable("table-1", HouseholdId)));
                BuildingEquipmentPlacement tablePlacement = MakePlacement("Table", Vector3.zero);
                tableObject = tablePlacement.gameObject;
                // Table installed with no capability: placed, but seats nobody.
                InstallTable(register, building, tablePlacement, "table-1", "dining",
                    BuildingInstanceId, FurnishingCapabilityKind.None);
                Assert.IsNull(register.TryRegisterAsset(MakeChair("chair-1", HouseholdId, 0.05f)));
                BuildingEquipmentPlacement chairPlacement = MakePlacement("Chair", new Vector3(1f, 0f, 0f));
                chairObject = chairPlacement.gameObject;
                InstallChair(register, building, chairPlacement, "chair-1", "dining-chair-1");
                var registry = MakeRegistry(register);

                Assert.AreEqual(0, registry.InstalledTableCount());
                Assert.AreEqual(0, registry.InstalledChairSeatCount(), "A chair at condition 0.05 is not usable.");
                Assert.IsNull(registry.TryReserveDiningSeat(1, Day, MorningMeal, MealDuration, out DiningSeatReservation reservation));
                Assert.AreEqual(DiningMode.Fallback, reservation.Mode);

                List<string> problems = registry.Validate();
                Assert.AreEqual(1, problems.Count, string.Join("; ", problems));
                Assert.IsTrue(problems[0].Contains("no DiningTable capability"), problems[0]);
            }
            finally
            {
                if (chairObject != null) Object.DestroyImmediate(chairObject);
                if (tableObject != null) Object.DestroyImmediate(tableObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void ChairWithoutCapability_NotASeat()
        {
            GameObject buildingObject = null;
            GameObject tableObject = null;
            GameObject chairObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B4 no-capability building");
                buildingObject = building.gameObject;
                var register = MakeRegister();
                Assert.IsNull(register.TryRegisterAsset(MakeTable("table-1", HouseholdId)));
                BuildingEquipmentPlacement tablePlacement = MakePlacement("Table", Vector3.zero);
                tableObject = tablePlacement.gameObject;
                InstallTable(register, building, tablePlacement);
                Assert.IsNull(register.TryRegisterAsset(MakeChair("chair-1", HouseholdId)));
                BuildingEquipmentPlacement chairPlacement = MakePlacement("Chair", new Vector3(1f, 0f, 0f));
                chairObject = chairPlacement.gameObject;
                // Chair installed with no capability: placed, but not a dining seat.
                InstallChair(register, building, chairPlacement, "chair-1", "dining-chair-1",
                    BuildingInstanceId, FurnishingCapabilityKind.None);
                var registry = MakeRegistry(register);

                Assert.AreEqual(1, registry.InstalledTableCount());
                Assert.AreEqual(0, registry.InstalledChairSeatCount());
                Assert.IsNull(registry.TryReserveDiningSeat(1, Day, MorningMeal, MealDuration, out DiningSeatReservation reservation));
                Assert.AreEqual(DiningMode.Fallback, reservation.Mode);
            }
            finally
            {
                if (chairObject != null) Object.DestroyImmediate(chairObject);
                if (tableObject != null) Object.DestroyImmediate(tableObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void ChairInOtherBuilding_NotASeat()
        {
            GameObject buildingObject = null;
            GameObject otherObject = null;
            GameObject tableObject = null;
            GameObject chairObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B4 home building");
                buildingObject = building.gameObject;
                BuildingFootprintAuthority other = MakeBuilding("B4 other building");
                otherObject = other.gameObject;
                var register = MakeRegister();
                Assert.IsNull(register.TryRegisterAsset(MakeTable("table-1", HouseholdId)));
                BuildingEquipmentPlacement tablePlacement = MakePlacement("Table", Vector3.zero);
                tableObject = tablePlacement.gameObject;
                InstallTable(register, building, tablePlacement);
                Assert.IsNull(register.TryRegisterAsset(MakeChair("chair-1", HouseholdId)));
                BuildingEquipmentPlacement chairPlacement = MakePlacement("Chair", Vector3.zero);
                chairObject = chairPlacement.gameObject;
                // The chair lives in the other building, away from the table.
                InstallChair(register, other, chairPlacement, "chair-1", "dining-chair-1",
                    OtherBuildingInstanceId);
                var registry = MakeRegistry(register);

                Assert.AreEqual(1, registry.InstalledTableCount());
                Assert.AreEqual(0, registry.InstalledChairSeatCount(), "A chair in another building is not a seat at this table.");
                Assert.IsNull(registry.TryReserveDiningSeat(1, Day, MorningMeal, MealDuration, out DiningSeatReservation reservation));
                Assert.AreEqual(DiningMode.Fallback, reservation.Mode);
            }
            finally
            {
                if (chairObject != null) Object.DestroyImmediate(chairObject);
                if (tableObject != null) Object.DestroyImmediate(tableObject);
                if (otherObject != null) Object.DestroyImmediate(otherObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void UncoveredDiners_SignalsChairWant()
        {
            DiningFixture fixture = null;
            try
            {
                fixture = MakeDiningFixture(chairCount: 1);
                var registry = fixture.Registry;
                var members = new List<int> { 1, 2, 3 };

                List<int> uncovered = registry.UncoveredDiners(members, Day, MorningMeal, MealDuration);
                Assert.AreEqual(2, uncovered.Count);
                Assert.AreEqual(2, uncovered[0]);
                Assert.AreEqual(3, uncovered[1]);
                Assert.AreEqual(0, registry.LiveReservations.Count, "Peek only: the simulation reserves nothing.");

                // With a live seat already held, the uncovered set judges by it.
                Assert.IsNull(registry.TryReserveDiningSeat(1, Day, MorningMeal, MealDuration, out _));
                uncovered = registry.UncoveredDiners(members, Day, MorningMeal, MealDuration);
                Assert.AreEqual(2, uncovered.Count);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Validate_SeatGone_FlagsReservation()
        {
            DiningFixture fixture = null;
            try
            {
                fixture = MakeDiningFixture(chairCount: 1);
                var registry = fixture.Registry;
                Assert.IsNull(registry.TryReserveDiningSeat(1, Day, MorningMeal, MealDuration, out _));

                // The chair breaks after the meal was planned: the seat
                // no longer derives, and Validate says so loudly.
                fixture.Register.FindAsset("chair-1").Condition01 = 0f;
                List<string> problems = registry.Validate();
                Assert.AreEqual(1, problems.Count, string.Join("; ", problems));
                Assert.IsTrue(problems[0].Contains("no longer derives"), problems[0]);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Planner_MealWindowsCarrySeatIdAndMode()
        {
            DiningFixture fixture = null;
            try
            {
                fixture = MakeDiningFixture(chairCount: 1);
                var registry = fixture.Registry;

                var planner = new DailyActivityPlanner();
                var schedules = new PersonScheduleTracker();
                var budgets = new WorkTimeBudgetStore();
                var diag = new List<string>();
                DailyActivityPlanner.PersonDayPlan plan =
                    planner.PlanPersonDay(Inputs(3), Day, schedules, budgets, diag, null, registry);

                Assert.AreEqual(0, plan.Conflicts.Count, string.Join(" | ", plan.Conflicts));
                List<DailyActivityPlanner.PlannedActivity> meals = MealActivities(plan);
                Assert.AreEqual(2, meals.Count, "Two default meal windows: 07:30 and 18:30.");
                foreach (DailyActivityPlanner.PlannedActivity meal in meals)
                {
                    Assert.IsTrue(meal.Reserved);
                    Assert.AreEqual("chair:chair-1", meal.DiningSeatId);
                    Assert.AreEqual(DiningMode.Seated, meal.DiningMode);
                }
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Planner_HouseholdAssignment_OneChair()
        {
            DiningFixture fixture = null;
            try
            {
                fixture = MakeDiningFixture(chairCount: 1);
                var registry = fixture.Registry;
                var planner = new DailyActivityPlanner();
                var schedules = new PersonScheduleTracker();
                var budgets = new WorkTimeBudgetStore();
                var diag = new List<string>();
                var members = new List<int> { 1, 2 };

                List<DailyActivityPlanner.PersonDayPlan> plans =
                    planner.PlanHouseholdDay(members, Inputs, Day, schedules, budgets, diag, null, registry);

                List<DailyActivityPlanner.PlannedActivity> first = MealActivities(plans[0]);
                Assert.AreEqual(2, first.Count);
                foreach (DailyActivityPlanner.PlannedActivity meal in first)
                {
                    Assert.AreEqual(DiningMode.Seated, meal.DiningMode);
                    Assert.AreEqual("chair:chair-1", meal.DiningSeatId);
                }
                List<DailyActivityPlanner.PlannedActivity> second = MealActivities(plans[1]);
                Assert.AreEqual(2, second.Count);
                foreach (DailyActivityPlanner.PlannedActivity meal in second)
                {
                    Assert.AreEqual(DiningMode.Fallback, meal.DiningMode);
                    Assert.AreEqual(string.Empty, meal.DiningSeatId);
                }
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Planner_WithoutRegistry_PlansMealsAsBefore()
        {
            var planner = new DailyActivityPlanner();
            var schedules = new PersonScheduleTracker();
            var budgets = new WorkTimeBudgetStore();
            var diag = new List<string>();
            DailyActivityPlanner.PersonDayPlan plan =
                planner.PlanPersonDay(Inputs(3), Day, schedules, budgets, diag);

            Assert.AreEqual(0, plan.Conflicts.Count, string.Join(" | ", plan.Conflicts));
            List<DailyActivityPlanner.PlannedActivity> meals = MealActivities(plan);
            Assert.AreEqual(2, meals.Count);
            foreach (DailyActivityPlanner.PlannedActivity meal in meals)
            {
                Assert.IsTrue(meal.Reserved);
                Assert.AreEqual(string.Empty, meal.DiningSeatId);
                Assert.AreEqual(DiningMode.Unspecified, meal.DiningMode);
            }
        }

        [Test]
        public void Arrival_SeatPathValidates()
        {
            GameObject buildingObject = null;
            GameObject tableObject = null;
            GameObject chairObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuildingWithDiningLocation("B4 arrival building");
                buildingObject = building.gameObject;
                var register = MakeRegister();
                Assert.IsNull(register.TryRegisterAsset(MakeTable("table-1", HouseholdId)));
                BuildingEquipmentPlacement tablePlacement = MakePlacement("Table", Vector3.zero);
                tableObject = tablePlacement.gameObject;
                InstallTable(register, building, tablePlacement);
                Assert.IsNull(register.TryRegisterAsset(MakeChair("chair-1", HouseholdId)));
                BuildingEquipmentPlacement chairPlacement = MakePlacement("Chair", new Vector3(1f, 0f, 0f));
                chairObject = chairPlacement.gameObject;
                InstallChair(register, building, chairPlacement);
                var registry = MakeRegistry(register);

                Assert.AreEqual(1, registry.DiningSeats().Count);
                DiningSeat seat = registry.DiningSeats()[0];
                Assert.IsNull(DiningArrival.TryValidateArrivalPath(seat, building));
                Assert.IsNull(DiningArrival.TryValidateFallbackArrivalPath(building));
            }
            finally
            {
                if (chairObject != null) Object.DestroyImmediate(chairObject);
                if (tableObject != null) Object.DestroyImmediate(tableObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void Arrival_SeatWithoutLocation_Refuses_FallbackNeedsEntrance()
        {
            GameObject buildingObject = null;
            GameObject tableObject = null;
            GameObject chairObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B4 no-location building");
                buildingObject = building.gameObject;
                var register = MakeRegister();
                Assert.IsNull(register.TryRegisterAsset(MakeTable("table-1", HouseholdId)));
                BuildingEquipmentPlacement tablePlacement = MakePlacement("Table", Vector3.zero);
                tableObject = tablePlacement.gameObject;
                InstallTable(register, building, tablePlacement, "table-1", "dining");
                Assert.IsNull(register.TryRegisterAsset(MakeChair("chair-1", HouseholdId)));
                BuildingEquipmentPlacement chairPlacement = MakePlacement("Chair", new Vector3(1f, 0f, 0f));
                chairObject = chairPlacement.gameObject;
                InstallChair(register, building, chairPlacement, "chair-1", string.Empty);
                var registry = MakeRegistry(register);

                Assert.AreEqual(1, registry.DiningSeats().Count);
                DiningSeat seat = registry.DiningSeats()[0];
                string refusal = DiningArrival.TryValidateArrivalPath(seat, building);
                Assert.IsNotNull(refusal);
                Assert.IsTrue(refusal.Contains("dining-chair"), refusal);

                // No entrance pair at all: even the fallback cannot be reached.
                Assert.IsNotNull(DiningArrival.TryValidateFallbackArrivalPath(building));
            }
            finally
            {
                if (chairObject != null) Object.DestroyImmediate(chairObject);
                if (tableObject != null) Object.DestroyImmediate(tableObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void EnumValues_AreAppendedNotRenumbered()
        {
            Assert.AreEqual(14, (int)BuildingActivityLocationType.Dining);
            Assert.AreEqual(6, (int)FurnishingCapabilityKind.DiningTable);
            Assert.AreEqual(3, (int)FurnishingCapabilityKind.DiningSeat, "The existing DiningSeat value is untouched.");
        }
    }
}
