using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Population;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// G-4 (Agent G, expanded campaign 2026-11-16): the meal tick
    /// driver. Proves the driver moves a diner to their reserved seat
    /// at the meal window, holds them through the meal, releases at
    /// the window end so the seat can be re-reserved, drives both
    /// daily meals, validates standing room for Fallback meals without
    /// requesting movement, eats in place (loudly, once) when the
    /// arrival path fails, the authority is missing, movement is
    /// unsupported, or no movement resolver is bound, closes a stale
    /// past-day reservation on the next tick, and falls back when the
    /// seat no longer derives between planning and the meal. The
    /// production movement resolver (SimulationDrivers, over the
    /// pathing director) is harness-excluded and source-reviewed only;
    /// Unity compilation proof is owed from Codex. No test claims
    /// PlayMode. Meal preparation is not tested here: DomesticLaborDriver
    /// already plans meal-prep tasks and DriveDomesticLaborDay runs
    /// daily — this driver is the eating half only.
    /// </summary>
    [TestFixture]
    public sealed class MealTickDriverG4Tests
    {
        private const int HouseholdId = 7;
        private const int BuildingInstanceId = 101;
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

        /// <summary>A building with a main entrance, a dining approach point, and one authored dining-chair location per chair.</summary>
        private static BuildingFootprintAuthority MakeBuildingWithDiningLocations(string name, int chairCount)
        {
            GameObject building = new(name);
            BuildingFootprintAuthority authority = building.AddComponent<BuildingFootprintAuthority>();
            AddWalkableTestFloor(authority);
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
            for (int i = 0; i < chairCount; i++)
            {
                GetActivityLocations(authority).Add(new BuildingActivityLocationAuthoring
                {
                    id = "dining-chair-" + (i + 1),
                    displayName = "Chair " + (i + 1) + " access",
                    type = BuildingActivityLocationType.Dining,
                    floorIndex = 0,
                    positionLocal = new Vector3(1f, 0f, 0f),
                    forwardLocal = Vector3.forward,
                    requiresWalkableInterior = true,
                });
            }
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
            string activityLocationId = "dining-chair-1")
        {
            string error = register.TryInstall(assetId, placement, building, BuildingInstanceId, 0,
                FurnishingCapabilityKind.DiningSeat, activityLocationId,
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
            string activityLocationId = "dining")
        {
            string error = register.TryInstall(assetId, placement, building, BuildingInstanceId, 0,
                FurnishingCapabilityKind.DiningTable, activityLocationId,
                FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                out FurnishingPlacementBinding binding);
            Assert.IsNull(error, error);
            Assert.NotNull(binding);
            return binding;
        }

        private sealed class DiningFixture
        {
            public GameObject BuildingObject;
            public BuildingFootprintAuthority Authority;
            public List<GameObject> ItemObjects = new List<GameObject>();
            public HouseholdFurnishingRegister Register;
            public DiningPlaceRegistry Registry;
        }

        private static DiningFixture MakeDiningFixture(int chairCount = 1, bool withTable = true, string buildingName = "G4 meal fixture")
        {
            BuildingFootprintAuthority building = MakeBuildingWithDiningLocations(buildingName, chairCount);
            var fixture = new DiningFixture { BuildingObject = building.gameObject, Authority = building };
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
            fixture.Registry = new DiningPlaceRegistry
            {
                HouseholdId = HouseholdId,
                Furnishings = register,
                HomeBuildingInstanceId = BuildingInstanceId,
            };
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

        private static DiningSeatReservation Reserve(DiningPlaceRegistry registry, int personId, int dayIndex, int mealStart)
        {
            Assert.IsNull(registry.TryReserveDiningSeat(personId, dayIndex, mealStart, MealDuration, out DiningSeatReservation reservation));
            Assert.NotNull(reservation);
            return reservation;
        }

        private static void Advance(MealTickDriver driver, int dayIndex, int minute, DiningPlaceRegistry registry,
            BuildingFootprintAuthority authority, List<string> diag,
            MealTickDriver.MealArrivalCheck arrivalCheck,
            MealTickDriver.MealFallbackCheck fallbackCheck,
            MealTickDriver.MealMovement moveToSeat)
        {
            driver.AdvanceTick(dayIndex, minute, new[] { registry },
                personId => authority, arrivalCheck, fallbackCheck, moveToSeat, diag);
        }

        [Test]
        public void SeatedMeal_MovesDinerToSeat_AndReleasesAtWindowEnd()
        {
            DiningFixture fixture = MakeDiningFixture();
            try
            {
                DiningSeatReservation reservation = Reserve(fixture.Registry, 1, Day, MorningMeal);
                Assert.AreEqual(DiningMode.Seated, reservation.Mode);
                var driver = new MealTickDriver();
                var diag = new List<string>();
                var moved = new List<string>();

                Advance(driver, Day, MorningMeal - 1, fixture.Registry, fixture.Authority, diag,
                    null, null, (personId, seat, auth) =>
                    {
                        moved.Add(seat.SeatId);
                        return DomesticActivityMovementResult.Arrived;
                    });
                Assert.AreEqual(0, moved.Count, "No movement before the meal window.");
                Assert.AreEqual(1, fixture.Registry.LiveReservations.Count);

                Advance(driver, Day, MorningMeal, fixture.Registry, fixture.Authority, diag,
                    null, null, (personId, seat, auth) =>
                    {
                        moved.Add(seat.SeatId);
                        return DomesticActivityMovementResult.Arrived;
                    });
                Assert.AreEqual(1, moved.Count);
                Assert.AreEqual("chair:chair-1", moved[0]);
                Assert.AreEqual(1, fixture.Registry.LiveReservations.Count, "The seat holds through the meal.");

                Advance(driver, Day, MorningMeal + 10, fixture.Registry, fixture.Authority, diag,
                    null, null, (personId, seat, auth) =>
                    {
                        moved.Add(seat.SeatId);
                        return DomesticActivityMovementResult.Arrived;
                    });
                Assert.AreEqual(1, moved.Count, "No second movement once the diner is eating.");

                Advance(driver, Day, MorningMeal + MealDuration, fixture.Registry, fixture.Authority, diag,
                    null, null, (personId, seat, auth) =>
                    {
                        moved.Add(seat.SeatId);
                        return DomesticActivityMovementResult.Arrived;
                    });
                Assert.AreEqual(0, fixture.Registry.LiveReservations.Count, "The seat releases at the window end.");
                Assert.AreEqual(0, diag.Count, "A routine meal stays silent.");
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void SeatedMeal_TravellingKeepsRequesting_ThenArrives()
        {
            DiningFixture fixture = MakeDiningFixture();
            try
            {
                Reserve(fixture.Registry, 1, Day, MorningMeal);
                var driver = new MealTickDriver();
                var diag = new List<string>();
                int calls = 0;
                MealTickDriver.MealMovement move = (personId, seat, auth) =>
                {
                    calls++;
                    return calls < 3 ? DomesticActivityMovementResult.Traveling : DomesticActivityMovementResult.Arrived;
                };

                Advance(driver, Day, MorningMeal, fixture.Registry, fixture.Authority, diag, null, null, move);
                Advance(driver, Day, MorningMeal + 1, fixture.Registry, fixture.Authority, diag, null, null, move);
                Advance(driver, Day, MorningMeal + 2, fixture.Registry, fixture.Authority, diag, null, null, move);
                Assert.AreEqual(3, calls, "Travelling keeps requesting until Arrived.");
                Assert.AreEqual(1, fixture.Registry.LiveReservations.Count);

                Advance(driver, Day, MorningMeal + MealDuration, fixture.Registry, fixture.Authority, diag, null, null, move);
                Assert.AreEqual(0, fixture.Registry.LiveReservations.Count);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void FallbackMeal_ValidatesStandingRoom_NoMovement_AndReleases()
        {
            DiningFixture fixture = MakeDiningFixture(chairCount: 0, withTable: false);
            try
            {
                DiningSeatReservation reservation = Reserve(fixture.Registry, 1, Day, MorningMeal);
                Assert.AreEqual(DiningMode.Fallback, reservation.Mode);
                var driver = new MealTickDriver();
                var diag = new List<string>();
                int moves = 0;

                Advance(driver, Day, MorningMeal, fixture.Registry, fixture.Authority, diag,
                    null, null, (personId, seat, auth) =>
                    {
                        moves++;
                        return DomesticActivityMovementResult.Arrived;
                    });
                Assert.AreEqual(0, moves, "A Fallback meal requests no movement — there is no seat to move to.");
                Assert.AreEqual(1, fixture.Registry.LiveReservations.Count, "The diner eats in place through the meal.");

                Advance(driver, Day, MorningMeal + MealDuration, fixture.Registry, fixture.Authority, diag,
                    null, null, (personId, seat, auth) =>
                    {
                        moves++;
                        return DomesticActivityMovementResult.Arrived;
                    });
                Assert.AreEqual(0, fixture.Registry.LiveReservations.Count, "The Fallback reservation releases at the window end.");
                Assert.AreEqual(0, diag.Count, "A routine Fallback meal stays silent.");
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void ArrivalFailure_EatsInPlace_AndStillReleases()
        {
            DiningFixture fixture = MakeDiningFixture();
            try
            {
                Reserve(fixture.Registry, 1, Day, MorningMeal);
                var driver = new MealTickDriver();
                var diag = new List<string>();
                int moves = 0;

                Advance(driver, Day, MorningMeal, fixture.Registry, fixture.Authority, diag,
                    (seat, auth) => "test: no interior route to the chair",
                    null, (personId, seat, auth) =>
                    {
                        moves++;
                        return DomesticActivityMovementResult.Arrived;
                    });
                Assert.AreEqual(0, moves, "A failed arrival requests no movement.");
                Assert.AreEqual(1, diag.Count, "The failure is loud once.");
                Assert.AreEqual(1, fixture.Registry.LiveReservations.Count);

                Advance(driver, Day, MorningMeal + 5, fixture.Registry, fixture.Authority, diag,
                    (seat, auth) => "test: no interior route to the chair",
                    null, (personId, seat, auth) =>
                    {
                        moves++;
                        return DomesticActivityMovementResult.Arrived;
                    });
                Assert.AreEqual(1, diag.Count, "The failure is not repeated on later ticks.");

                Advance(driver, Day, MorningMeal + MealDuration, fixture.Registry, fixture.Authority, diag,
                    (seat, auth) => "test: no interior route to the chair",
                    null, (personId, seat, auth) =>
                    {
                        moves++;
                        return DomesticActivityMovementResult.Arrived;
                    });
                Assert.AreEqual(0, fixture.Registry.LiveReservations.Count, "The meal still releases at the window end.");
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void NoAuthority_EatsInPlace_AndStillReleases()
        {
            DiningFixture fixture = MakeDiningFixture();
            try
            {
                Reserve(fixture.Registry, 1, Day, MorningMeal);
                var driver = new MealTickDriver();
                var diag = new List<string>();
                int moves = 0;

                Advance(driver, Day, MorningMeal, fixture.Registry, null, diag,
                    null, null, (personId, seat, auth) =>
                    {
                        moves++;
                        return DomesticActivityMovementResult.Arrived;
                    });
                Assert.AreEqual(0, moves);
                Assert.AreEqual(1, diag.Count);
                Assert.AreEqual(1, fixture.Registry.LiveReservations.Count);

                Advance(driver, Day, MorningMeal + MealDuration, fixture.Registry, null, diag,
                    null, null, (personId, seat, auth) =>
                    {
                        moves++;
                        return DomesticActivityMovementResult.Arrived;
                    });
                Assert.AreEqual(0, fixture.Registry.LiveReservations.Count);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void MovementUnsupported_EatsInPlace_AndStillReleases()
        {
            DiningFixture fixture = MakeDiningFixture();
            try
            {
                Reserve(fixture.Registry, 1, Day, MorningMeal);
                var driver = new MealTickDriver();
                var diag = new List<string>();

                Advance(driver, Day, MorningMeal, fixture.Registry, fixture.Authority, diag,
                    null, null, (personId, seat, auth) => DomesticActivityMovementResult.Unsupported);
                Assert.AreEqual(1, diag.Count, "Unsupported movement is loud once.");
                Assert.AreEqual(1, fixture.Registry.LiveReservations.Count);

                Advance(driver, Day, MorningMeal + MealDuration, fixture.Registry, fixture.Authority, diag,
                    null, null, (personId, seat, auth) => DomesticActivityMovementResult.Unsupported);
                Assert.AreEqual(0, fixture.Registry.LiveReservations.Count);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void NoMovementResolver_EatsInPlace_AndStillReleases()
        {
            DiningFixture fixture = MakeDiningFixture();
            try
            {
                Reserve(fixture.Registry, 1, Day, MorningMeal);
                var driver = new MealTickDriver();
                var diag = new List<string>();

                Advance(driver, Day, MorningMeal, fixture.Registry, fixture.Authority, diag, null, null, null);
                Assert.AreEqual(1, diag.Count, "A missing movement resolver is loud once.");
                Assert.AreEqual(1, fixture.Registry.LiveReservations.Count);

                Advance(driver, Day, MorningMeal + MealDuration, fixture.Registry, fixture.Authority, diag, null, null, null);
                Assert.AreEqual(0, fixture.Registry.LiveReservations.Count);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void FallbackArrivalFailure_EatsInPlace_AndStillReleases()
        {
            DiningFixture fixture = MakeDiningFixture(chairCount: 0, withTable: false);
            try
            {
                Reserve(fixture.Registry, 1, Day, MorningMeal);
                var driver = new MealTickDriver();
                var diag = new List<string>();
                int moves = 0;

                Advance(driver, Day, MorningMeal, fixture.Registry, fixture.Authority, diag,
                    null, (auth) => "test: no walkable standing room",
                    (personId, seat, auth) =>
                    {
                        moves++;
                        return DomesticActivityMovementResult.Arrived;
                    });
                Assert.AreEqual(0, moves, "A Fallback meal never requests movement, even when its arrival check fails.");
                Assert.AreEqual(1, diag.Count);

                Advance(driver, Day, MorningMeal + MealDuration, fixture.Registry, fixture.Authority, diag,
                    null, (auth) => "test: no walkable standing room",
                    (personId, seat, auth) =>
                    {
                        moves++;
                        return DomesticActivityMovementResult.Arrived;
                    });
                Assert.AreEqual(0, fixture.Registry.LiveReservations.Count);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void StalePastDayReservation_ReleasedOnNextTick()
        {
            DiningFixture fixture = MakeDiningFixture();
            try
            {
                Reserve(fixture.Registry, 1, Day - 1, MorningMeal);
                var driver = new MealTickDriver();
                var diag = new List<string>();

                Advance(driver, Day, 0, fixture.Registry, fixture.Authority, diag,
                    null, null, (personId, seat, auth) => DomesticActivityMovementResult.Arrived);
                Assert.AreEqual(0, fixture.Registry.LiveReservations.Count, "A past-day reservation releases on the next tick.");
                Assert.AreEqual(1, diag.Count, "The stale close is loud once.");
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void SeatGoneBetweenPlanningAndMeal_FallsBack_AndReleases()
        {
            DiningFixture fixture = MakeDiningFixture();
            try
            {
                Reserve(fixture.Registry, 1, Day, MorningMeal);
                // The chair is unbound between planning and the meal:
                // the seat no longer derives from the register.
                FurnishingPlacementBinding live = fixture.Register.LiveBindingFor("chair-1");
                Assert.NotNull(live);
                live.CapabilityKind = FurnishingCapabilityKind.None;
                Assert.AreEqual(0, fixture.Registry.DiningSeats().Count);

                var driver = new MealTickDriver();
                var diag = new List<string>();
                int moves = 0;

                Advance(driver, Day, MorningMeal, fixture.Registry, fixture.Authority, diag,
                    null, null, (personId, seat, auth) =>
                    {
                        moves++;
                        return DomesticActivityMovementResult.Arrived;
                    });
                Assert.AreEqual(0, moves, "A gone seat falls back — no movement to a seat that no longer exists.");
                Assert.AreEqual(1, diag.Count, "The lost seat is loud once.");
                Assert.AreEqual(1, fixture.Registry.LiveReservations.Count);

                Advance(driver, Day, MorningMeal + MealDuration, fixture.Registry, fixture.Authority, diag,
                    null, null, (personId, seat, auth) =>
                    {
                        moves++;
                        return DomesticActivityMovementResult.Arrived;
                    });
                Assert.AreEqual(0, fixture.Registry.LiveReservations.Count);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void TwoMealsSameDay_BothDriven()
        {
            DiningFixture fixture = MakeDiningFixture();
            try
            {
                Reserve(fixture.Registry, 1, Day, MorningMeal);
                Reserve(fixture.Registry, 1, Day, EveningMeal);
                Assert.AreEqual(2, fixture.Registry.LiveReservations.Count);
                var driver = new MealTickDriver();
                var diag = new List<string>();
                var moved = new List<string>();
                MealTickDriver.MealMovement move = (personId, seat, auth) =>
                {
                    moved.Add(seat.SeatId);
                    return DomesticActivityMovementResult.Arrived;
                };

                Advance(driver, Day, MorningMeal, fixture.Registry, fixture.Authority, diag, null, null, move);
                Advance(driver, Day, MorningMeal + MealDuration, fixture.Registry, fixture.Authority, diag, null, null, move);
                Assert.AreEqual(1, fixture.Registry.LiveReservations.Count, "The morning seat releases; the evening meal still holds.");

                Advance(driver, Day, EveningMeal, fixture.Registry, fixture.Authority, diag, null, null, move);
                Advance(driver, Day, EveningMeal + MealDuration, fixture.Registry, fixture.Authority, diag, null, null, move);
                Assert.AreEqual(0, fixture.Registry.LiveReservations.Count, "The evening seat releases too.");
                Assert.AreEqual(2, moved.Count, "The same chair serves both meals.");
                Assert.AreEqual("chair:chair-1", moved[0]);
                Assert.AreEqual("chair:chair-1", moved[1]);
                Assert.AreEqual(0, diag.Count);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void AdvanceTick_NullInputs_NeverThrows()
        {
            var driver = new MealTickDriver();
            Assert.DoesNotThrow(() => driver.AdvanceTick(Day, MorningMeal, null, null, null, null, null, null));
            Assert.DoesNotThrow(() => driver.AdvanceTick(Day, MorningMeal, new DiningPlaceRegistry[0], null, null, null, null, null));
        }
    }
}
