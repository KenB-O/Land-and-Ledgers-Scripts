using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Time;
using LandLedgers.World;
using LandLedgers.World.Journeys;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// W-G2 (Agent G, expanded campaign 2026-11-16): the sleep tick
    /// driver. Proves the driver moves a sleeper to their reserved place
    /// at 22:00, holds them through the night, releases on waking at
    /// 07:00 with rest quality recorded, closes a missed wake so the bed
    /// can be re-reserved, and cancels unreached reservations without
    /// awarding rest or bed-use credit,
    /// and threads LastRestQuality01 into the WorkCapacityFactor call
    /// sites through DailyNeedsService.RestQualityForPerson (both the
    /// reserve and the lot meal paths). The production movement resolver
    /// (SimulationDrivers, over the pathing director) is harness-excluded
    /// and source-reviewed only; Unity compilation proof is owed from
    /// Codex. No test claims PlayMode.
    /// </summary>
    [TestFixture]
    public sealed class SleepTickDriverTests
    {
        private const int HouseholdId = 7;
        private const int BuildingInstanceId = 101;
        private const int Day = 100;

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

        /// <summary>A building with a main entrance and an authored bed-1 location, for arrival-path tests.</summary>
        private static BuildingFootprintAuthority MakeBuildingWithBedLocation(string name)
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
                id = "bed-1",
                displayName = "Bed access",
                type = BuildingActivityLocationType.Bed,
                floorIndex = 0,
                positionLocal = new Vector3(2f, 0f, 2f),
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

        private static FurnishingPlacementBinding InstallBed(
            HouseholdFurnishingRegister register,
            BuildingFootprintAuthority building,
            BuildingEquipmentPlacement placement,
            string assetId = "bed-1",
            string activityLocationId = "bed-1")
        {
            string error = register.TryInstall(assetId, placement, building, BuildingInstanceId, 0,
                FurnishingCapabilityKind.SleepingPlace, activityLocationId,
                FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                out FurnishingPlacementBinding binding);
            Assert.IsNull(error, error);
            Assert.NotNull(binding);
            return binding;
        }

        private static HouseholdFurnishingRegister RegisterWithBed(string assetId = "bed-1")
        {
            var register = MakeRegister();
            Assert.IsNull(register.TryRegisterAsset(MakeBed(assetId, HouseholdId)));
            return register;
        }

        private static SleepingPlaceRegistry MakeRegistry(HouseholdFurnishingRegister register)
        {
            return new SleepingPlaceRegistry
            {
                HouseholdId = HouseholdId,
                Furnishings = register,
                HomeBuildingInstanceId = BuildingInstanceId,
            };
        }

        /// <summary>One installed bed (SleepingPlace capability, "bed-1") plus its registry, with cleanup.</summary>
        private sealed class BedFixture
        {
            public GameObject BuildingObject;
            public GameObject ItemObject;
            public BuildingFootprintAuthority Authority;
            public HouseholdFurnishingRegister Register;
            public SleepingPlaceRegistry Registry;
        }

        private static BedFixture MakeBedFixture(string buildingName = "B1 bed fixture")
        {
            BuildingFootprintAuthority building = MakeBuildingWithBedLocation(buildingName);
            BuildingEquipmentPlacement placement = MakePlacement("Bed", Vector3.zero);
            var register = RegisterWithBed();
            InstallBed(register, building, placement);
            return new BedFixture
            {
                BuildingObject = building.gameObject,
                ItemObject = placement.gameObject,
                Authority = building,
                Register = register,
                Registry = MakeRegistry(register),
            };
        }

        private static void DestroyFixture(BedFixture fixture)
        {
            if (fixture == null) return;
            if (fixture.ItemObject != null) Object.DestroyImmediate(fixture.ItemObject);
            if (fixture.BuildingObject != null) Object.DestroyImmediate(fixture.BuildingObject);
        }

        /// <summary>A scripted movement resolver: records every call and answers from a queue (default Arrived).</summary>
        private sealed class FakeMovement
        {
            public readonly List<string> Calls = new List<string>();
            public readonly List<string> RouteIdentities = new List<string>();
            public readonly List<SleepingPlaceKind> Kinds = new List<SleepingPlaceKind>();
            private readonly Queue<DomesticActivityMovementResult> answers = new Queue<DomesticActivityMovementResult>();

            public void Answer(params DomesticActivityMovementResult[] results)
            {
                foreach (DomesticActivityMovementResult result in results) answers.Enqueue(result);
            }

            public DomesticActivityMovementResult Move(int personId, string routeIdentity, SleepingPlace place,
                BuildingFootprintAuthority authority, out string failureReason)
            {
                Calls.Add("P" + personId + ":" + place.PlaceId);
                RouteIdentities.Add(routeIdentity);
                Kinds.Add(place.Kind);
                failureReason = answers.Count > 0
                    && answers.Peek() == DomesticActivityMovementResult.Unsupported
                    ? "No physical route reaches the reserved activity location."
                    : string.Empty;
                return answers.Count > 0 ? answers.Dequeue() : DomesticActivityMovementResult.Arrived;
            }
        }

        /// <summary>A scripted arrival check: records calls and answers with a configured failure (null = reachable).</summary>
        private sealed class FakeArrivalCheck
        {
            public readonly List<string> Calls = new List<string>();
            public string Failure;

            public string Check(SleepingPlace place, BuildingFootprintAuthority authority)
            {
                Calls.Add(place.PlaceId);
                return Failure;
            }
        }

        private static void Reserve(BedFixture fixture, int personId, int dayIndex)
        {
            Assert.IsNull(fixture.Registry.TryReserveSleepingPlace(personId, dayIndex, 1320, 420, null, out _));
        }

        [Test]
        public void Driver_MovesSleeperToBed_HoldsThroughNight_ReleasesOnWaking()
        {
            BedFixture fixture = null;
            try
            {
                fixture = MakeBedFixture();
                Reserve(fixture, 1, Day);
                var driver = new SleepTickDriver();
                var movement = new FakeMovement();
                movement.Answer(DomesticActivityMovementResult.Traveling, DomesticActivityMovementResult.Arrived);
                var arrival = new FakeArrivalCheck();
                var diag = new List<string>();
                var registries = new List<SleepingPlaceRegistry> { fixture.Registry };

                // Before the sleep start the hold simply stands: no movement.
                driver.AdvanceTick(Day, 1260, registries, _ => fixture.Authority, arrival.Check, movement.Move, diag);
                Assert.AreEqual(0, movement.Calls.Count, "No movement before 22:00.");
                Assert.AreEqual(1, fixture.Registry.LiveReservations.Count);

                // 22:00: arrival validated and movement requested, travelling.
                driver.AdvanceTick(Day, 1320, registries, _ => fixture.Authority, arrival.Check, movement.Move, diag);
                Assert.AreEqual(1, movement.Calls.Count);
                Assert.AreEqual(1, arrival.Calls.Count, "Arrival is checked when movement is requested.");
                Assert.AreEqual("P1:bed:bed-1", movement.Calls[0]);
                Assert.AreEqual(1, fixture.Registry.LiveReservations.Count);

                // 22:30: arrived — the sleeper is asleep; later ticks hold without re-requesting movement.
                driver.AdvanceTick(Day, 1350, registries, _ => fixture.Authority, arrival.Check, movement.Move, diag);
                Assert.AreEqual(2, movement.Calls.Count);
                driver.AdvanceTick(Day, 1380, registries, _ => fixture.Authority, arrival.Check, movement.Move, diag);
                Assert.AreEqual(2, movement.Calls.Count, "No movement re-request once asleep.");
                Assert.AreEqual(2, arrival.Calls.Count, "A still-travelling route is validated before movement is retried.");

                // 03:00 the next morning: still asleep, reservation live.
                driver.AdvanceTick(Day + 1, 180, registries, _ => fixture.Authority, arrival.Check, movement.Move, diag);
                Assert.AreEqual(2, movement.Calls.Count);
                Assert.AreEqual(1, fixture.Registry.LiveReservations.Count);

                // 07:00: wake releases the reservation and records rest quality (bed 1.0).
                driver.AdvanceTick(Day + 1, 420, registries, _ => fixture.Authority, arrival.Check, movement.Move, diag);
                Assert.AreEqual(0, fixture.Registry.LiveReservations.Count);
                Assert.AreEqual(1.0f, fixture.Registry.LastRestQuality01(1));
                Assert.AreEqual(0, diag.Count, "A routine wake logs nothing: " + string.Join(" | ", diag));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Driver_UsesRealArrivalCheckWhenNoneSupplied()
        {
            BedFixture fixture = null;
            try
            {
                fixture = MakeBedFixture();
                Reserve(fixture, 1, Day);
                var driver = new SleepTickDriver();
                var movement = new FakeMovement();
                var diag = new List<string>();

                // arrivalCheck null: the driver calls the B1 contract
                // (SleepingPlaceArrival.TryValidateArrivalPath) directly
                // against the real building authority.
                driver.AdvanceTick(Day, 1320, new List<SleepingPlaceRegistry> { fixture.Registry },
                    _ => fixture.Authority, null, movement.Move, diag);
                Assert.AreEqual(1, movement.Calls.Count, "The real B1 arrival check passed the bed-1 location, so movement was requested: " + string.Join(" | ", diag));
                Assert.AreEqual("P1:bed:bed-1", movement.Calls[0]);
                Assert.AreEqual(0, diag.Count);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Driver_RetriesArrivalFailure_AndDoesNotCreditUnreachedBed()
        {
            BedFixture fixture = null;
            try
            {
                fixture = MakeBedFixture();
                Reserve(fixture, 1, Day);
                var driver = new SleepTickDriver();
                var movement = new FakeMovement();
                var arrival = new FakeArrivalCheck { Failure = "no interior route from main-entrance to bed:bed-1" };
                var diag = new List<string>();
                var registries = new List<SleepingPlaceRegistry> { fixture.Registry };

                driver.AdvanceTick(Day, 1320, registries, _ => fixture.Authority, arrival.Check, movement.Move, diag);
                Assert.AreEqual(0, movement.Calls.Count, "No movement is requested when the arrival path fails.");
                Assert.AreEqual(1, diag.Count);
                Assert.IsTrue(diag[0].Contains("Arrival"), diag[0]);

                // A later reachable route is retried during the same sleep window.
                arrival.Failure = null;
                movement.Answer(DomesticActivityMovementResult.Arrived);
                driver.AdvanceTick(Day, 1400, registries, _ => fixture.Authority, arrival.Check, movement.Move, diag);
                Assert.AreEqual(1, movement.Calls.Count);
                Assert.AreEqual(2, arrival.Calls.Count);
                Assert.AreEqual(1, diag.Count);

                driver.AdvanceTick(Day + 1, 420, registries, _ => fixture.Authority, arrival.Check, movement.Move, diag);
                Assert.AreEqual(0, fixture.Registry.LiveReservations.Count);
                Assert.AreEqual(1.0f, fixture.Registry.LastRestQuality01(1));
                Assert.AreEqual(Day, fixture.Registry.LastBedNightDayIndex(1));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Driver_NoAuthorityDoesNotAwardRestOrBedUse()
        {
            BedFixture fixture = null;
            try
            {
                fixture = MakeBedFixture();
                Reserve(fixture, 1, Day);
                var driver = new SleepTickDriver();
                var movement = new FakeMovement();
                var arrival = new FakeArrivalCheck();
                var diag = new List<string>();
                var registries = new List<SleepingPlaceRegistry> { fixture.Registry };

                driver.AdvanceTick(Day, 1320, registries, _ => null, arrival.Check, movement.Move, diag);
                Assert.AreEqual(0, movement.Calls.Count);
                Assert.AreEqual(0, arrival.Calls.Count, "No arrival check is possible without an authority.");
                Assert.AreEqual(1, diag.Count);
                Assert.IsTrue(diag[0].Contains("No building authority"), diag[0]);

                driver.AdvanceTick(Day + 1, 420, registries, _ => null, arrival.Check, movement.Move, diag);
                Assert.AreEqual(0, fixture.Registry.LiveReservations.Count);
                Assert.AreEqual(-1, fixture.Registry.LastBedNightDayIndex(1));
                Assert.IsTrue(diag.Exists(line => line.Contains("without bed-use or rest-quality credit")),
                    string.Join(" | ", diag));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Driver_MissedWakeClosesStaleNightSoBedCanBeReReserved()
        {
            BedFixture fixture = null;
            try
            {
                fixture = MakeBedFixture();
                Reserve(fixture, 1, Day);
                var driver = new SleepTickDriver();
                var movement = new FakeMovement();
                var arrival = new FakeArrivalCheck();
                var diag = new List<string>();
                var registries = new List<SleepingPlaceRegistry> { fixture.Registry };

                driver.AdvanceTick(Day, 1320, registries, _ => fixture.Authority, arrival.Check, movement.Move, diag);
                Assert.AreEqual(1, fixture.Registry.LiveReservations.Count);

                // No tick runs at 07:00 the next morning (saved mid-night,
                // or the driver was not running). The first tick two days
                // later closes the stale night loudly.
                driver.AdvanceTick(Day + 2, 600, registries, _ => fixture.Authority, arrival.Check, movement.Move, diag);
                Assert.AreEqual(0, fixture.Registry.LiveReservations.Count);
                Assert.AreEqual(1.0f, fixture.Registry.LastRestQuality01(1));
                Assert.AreEqual(1, diag.Count);
                Assert.IsTrue(diag[0].Contains("Stale night"), diag[0]);

                // The bed is free again: tonight re-reserves.
                Assert.IsNull(fixture.Registry.TryReserveSleepingPlace(1, Day + 2, 1320, 420, null, out SleepingPlaceReservation next));
                Assert.AreEqual("bed:bed-1", next.PlaceId);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Driver_FloorPlaceMovesSleeperHome()
        {
            var registry = MakeRegistry(MakeRegister());
            Assert.IsNull(registry.TryReserveSleepingPlace(1, Day, 1320, 420, null, out SleepingPlaceReservation reservation));
            Assert.AreEqual(SleepingPlaceKind.Floor, reservation.Kind);
            Assert.AreEqual("floor:101:P1", reservation.PlaceId);

            BuildingFootprintAuthority building = null;
            try
            {
                building = MakeBuilding("Floor fixture");
                var driver = new SleepTickDriver();
                var movement = new FakeMovement();
                var arrival = new FakeArrivalCheck();
                var diag = new List<string>();
                var registries = new List<SleepingPlaceRegistry> { registry };

                // A floor place has no authored activity location, so the
                // driver still requests movement (the production resolver
                // routes to the home building); the arrival check passes.
                driver.AdvanceTick(Day, 1320, registries, _ => building, arrival.Check, movement.Move, diag);
                Assert.AreEqual(1, movement.Calls.Count);
                Assert.AreEqual("P1:floor:101:P1", movement.Calls[0]);
                Assert.AreEqual(SleepingPlaceKind.Floor, movement.Kinds[0]);
                Assert.AreEqual(0, diag.Count);

                // Held through the night, released at waking with floor rest quality.
                driver.AdvanceTick(Day, 1400, registries, _ => building, arrival.Check, movement.Move, diag);
                Assert.AreEqual(1, movement.Calls.Count);
                driver.AdvanceTick(Day + 1, 420, registries, _ => building, arrival.Check, movement.Move, diag);
                Assert.AreEqual(0, registry.LiveReservations.Count);
                Assert.AreEqual(0.6f, registry.LastRestQuality01(1));
            }
            finally
            {
                if (building != null) Object.DestroyImmediate(building.gameObject);
            }
        }

        [Test]
        public void Driver_UnsupportedMovementRetries_AndRecordsOnlyAfterArrival()
        {
            BedFixture fixture = null;
            try
            {
                fixture = MakeBedFixture();
                Reserve(fixture, 1, Day);
                var driver = new SleepTickDriver();
                var movement = new FakeMovement();
                movement.Answer(DomesticActivityMovementResult.Unsupported);
                var arrival = new FakeArrivalCheck();
                var diag = new List<string>();
                var registries = new List<SleepingPlaceRegistry> { fixture.Registry };

                driver.AdvanceTick(Day, 1320, registries, _ => fixture.Authority, arrival.Check, movement.Move, diag);
                Assert.AreEqual(1, movement.Calls.Count);
                Assert.AreEqual(1, diag.Count);
                Assert.IsTrue(diag[0].Contains("unsupported"), diag[0]);

                // A route may become available later in the same sleep window.
                movement.Answer(DomesticActivityMovementResult.Arrived);
                driver.AdvanceTick(Day, 1400, registries, _ => fixture.Authority, arrival.Check, movement.Move, diag);
                Assert.AreEqual(2, movement.Calls.Count);
                Assert.AreEqual(1, diag.Count);

                driver.AdvanceTick(Day + 1, 420, registries, _ => fixture.Authority, arrival.Check, movement.Move, diag);
                Assert.AreEqual(0, fixture.Registry.LiveReservations.Count);
                Assert.AreEqual(1.0f, fixture.Registry.LastRestQuality01(1));
                Assert.AreEqual(Day, fixture.Registry.LastBedNightDayIndex(1));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Driver_UnreachedNightHasStableRouteIdentityAndReleasesOnlyThatRouteAtWake()
        {
            BedFixture fixture = null;
            try
            {
                fixture = MakeBedFixture();
                Reserve(fixture, 1, Day);
                var driver = new SleepTickDriver();
                var movement = new FakeMovement();
                movement.Answer(DomesticActivityMovementResult.Traveling,
                    DomesticActivityMovementResult.Traveling);
                var arrival = new FakeArrivalCheck();
                var released = new List<string>();
                var diag = new List<string>();
                var registries = new List<SleepingPlaceRegistry> { fixture.Registry };

                driver.AdvanceTick(Day, 1320, registries, _ => fixture.Authority,
                    arrival.Check, movement.Move, diag,
                    (personId, routeIdentity, place) => released.Add(
                        "P" + personId + ":" + routeIdentity + ":" + place.PlaceId));
                driver.AdvanceTick(Day, 1400, registries, _ => fixture.Authority,
                    arrival.Check, movement.Move, diag);

                Assert.AreEqual(2, movement.Calls.Count);
                Assert.AreEqual("sleep:H" + HouseholdId + ":P1:D" + Day,
                    movement.RouteIdentities[0]);
                Assert.AreEqual(movement.RouteIdentities[0], movement.RouteIdentities[1],
                    "Repeated requests for one reserved night must retain route ownership.");

                driver.AdvanceTick(Day + 1, 420, registries, _ => fixture.Authority,
                    arrival.Check, movement.Move, diag,
                    (personId, routeIdentity, place) => released.Add(
                        "P" + personId + ":" + routeIdentity + ":" + place.PlaceId));

                Assert.AreEqual(1, released.Count,
                    "A missed arrival must release the movement owned by this expired sleep reservation.");
                Assert.AreEqual("P1:" + movement.RouteIdentities[0] + ":bed:bed-1", released[0]);
                Assert.AreEqual(0, fixture.Registry.LiveReservations.Count);
                Assert.AreEqual(-1, fixture.Registry.LastBedNightDayIndex(1),
                    "No rest credit is given when physical arrival never occurred.");
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Driver_UnreachedBedIsReleasedAtWakeWithoutRestOrBedNightCredit()
        {
            BedFixture fixture = null;
            try
            {
                fixture = MakeBedFixture();
                Reserve(fixture, 1, Day);
                var driver = new SleepTickDriver();
                var movement = new FakeMovement();
                movement.Answer(DomesticActivityMovementResult.Unsupported,
                    DomesticActivityMovementResult.Unsupported);
                var arrival = new FakeArrivalCheck();
                var diag = new List<string>();
                var registries = new List<SleepingPlaceRegistry> { fixture.Registry };

                driver.AdvanceTick(Day, 1320, registries, _ => fixture.Authority, arrival.Check, movement.Move, diag);
                driver.AdvanceTick(Day, 1400, registries, _ => fixture.Authority, arrival.Check, movement.Move, diag);
                Assert.AreEqual(1, fixture.Registry.LiveReservations.Count, "Hold remains available for retries during the sleep window.");

                driver.AdvanceTick(Day + 1, 420, registries, _ => fixture.Authority, arrival.Check, movement.Move, diag);
                Assert.AreEqual(0, fixture.Registry.LiveReservations.Count, "Failed sleeper must not monopolize the bed after wake.");
                Assert.AreEqual(-1, fixture.Registry.LastBedNightDayIndex(1));
                Assert.AreEqual(1.0f, fixture.Registry.LastRestQuality01(1), "Unreached reservation leaves rest history unchanged.");
                Assert.IsTrue(diag.Exists(line => line.Contains("without bed-use or rest-quality credit")),
                    string.Join(" | ", diag));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        private sealed class TestSupplier : IGoodsSupplier
        {
            public string SupplierBusinessId => "store-1";
            public string SupplierName => "General Store";
            public string LocationId => "store";
            public readonly Dictionary<string, int> Stock = new Dictionary<string, int>();
            public int PriceCents = 10;

            public bool HasCategory(string categoryId) => Stock.ContainsKey(categoryId);
            public int StockUnits(string categoryId) => Stock.TryGetValue(categoryId, out int u) ? u : 0;
            public int PricePerUnitCents(string categoryId) => PriceCents;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics)
            {
                int sold = Math.Min(requestedUnits, StockUnits(categoryId));
                if (sold > 0) Stock[categoryId] -= sold;
                return sold;
            }
        }

        private static PersonState Adult(int id, string name, int householdId)
        {
            return new PersonState
            {
                id = id, firstName = name, lastName = "Test", age = 30,
                ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = householdId,
            };
        }

        private static (PopulationState Population, HouseholdConsumptionPlanner Planner,
            IEmbodiedPurchaseExecutor Executor, DailyNeedsService Service) MakeNeedsFixture()
        {
            var population = new PopulationState();
            population.people.Add(Adult(1, "Abe", 7));
            population.people.Add(Adult(2, "Bea", 7));
            var household = new HouseholdState { id = 7 };
            household.memberIds.Add(1);
            household.memberIds.Add(2);
            household.reserves.Add(new HouseholdReserveState
            {
                categoryId = "staple_food", displayName = "Staple food",
                currentUnits = 100, lowThresholdUnits = 20, targetUnits = 30,
            });
            population.households.Add(household);

            var ledgers = new HouseholdLedgerRegistry();
            ledgers.GetOrCreate(7).RecordInflow(0, 100000, HouseholdIncomeSource.OwnerContribution,
                "test-capital", "test funding", "test");

            var suppliers = new SupplierDirectory();
            var store = new TestSupplier();
            store.Stock["staple_food"] = 1000;
            suppliers.Register(store);

            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Home", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "Store", 2f, 0f));
            journeys.AddEdge("home", "store", 2.0f, "road");

            var executor = new EmbodiedPurchaseExecutor(population, ledgers, suppliers, journeys, pid => "home");
            return (population, new HouseholdConsumptionPlanner(), executor, new DailyNeedsService());
        }

        [Test]
        public void RestQualityForPerson_ReachesWorkCapacityBudget_ReservePath()
        {
            var (population, planner, executor, service) = MakeNeedsFixture();
            population.people[0].nutrition.nutrition01 = 1f;
            population.people[1].nutrition.nutrition01 = 1f;
            var budgets = new WorkTimeBudgetStore();
            var diag = new List<string>();

            // P1 slept on the floor last night (0.6); P2 slept in a bed (1.0).
            service.RestQualityForPerson = pid => pid == 1 ? 0.6f : 1f;
            DailyNeedsService.DayReport report = service.ExecuteDay(population, planner, executor, 0, diag, budgets);

            Assert.AreEqual(4, report.MealsEaten, "2 members x 2 meals.");
            Assert.AreEqual(0.6f, budgets.GetOrCreate(EntityId.For(EntityKind.Person, 1)).CapacityMultiplier01, 0.001f,
                "The floor night reaches the reserve-path work-capacity budget.");
            Assert.AreEqual(1.0f, budgets.GetOrCreate(EntityId.For(EntityKind.Person, 2)).CapacityMultiplier01, 0.001f);
        }

        [Test]
        public void RestQualityForPerson_ReachesWorkCapacityBudget_LotPath()
        {
            var (population, planner, _, service) = MakeNeedsFixture();
            var inventories = new HouseholdInventoryRegistry();
            HouseholdInventory inventory = inventories.GetOrCreate(7);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 1, 0, "actual household purchase"));
            var mealLog = new HouseholdMealLogRegistry();
            population.people[0].nutrition.nutrition01 = 1f;
            population.people[1].nutrition.nutrition01 = 1f;
            var budgets = new WorkTimeBudgetStore();
            var diag = new List<string>();

            service.RestQualityForPerson = pid => pid == 1 ? 0.6f : 1f;
            DailyNeedsService.DayReport report = service.ExecuteDay(
                population, planner, null, 0, diag, budgets,
                inventories: inventories, mealLog: mealLog,
                mealPolicy: MealSchedulingPolicy.Default);

            Assert.Greater(report.MealsEaten, 0, "The lot path served meals.");
            Assert.AreEqual(0.6f, budgets.GetOrCreate(EntityId.For(EntityKind.Person, 1)).CapacityMultiplier01, 0.001f,
                "The floor night reaches the lot-path work-capacity budget.");
            Assert.AreEqual(1.0f, budgets.GetOrCreate(EntityId.For(EntityKind.Person, 2)).CapacityMultiplier01, 0.001f);
        }

        [Test]
        public void WorkCapacityFactor_MultipliesRestQuality()
        {
            var person = Adult(1, "Abe", 7);
            person.nutrition.nutrition01 = 1f;

            Assert.AreEqual(1.0f, DailyNeedsService.WorkCapacityFactor(person, false, 1.0f), 0.001f);
            Assert.AreEqual(0.6f, DailyNeedsService.WorkCapacityFactor(person, false, 0.6f), 0.001f,
                "A floor night costs 40% of usable work minutes.");
            Assert.AreEqual(0.8f, DailyNeedsService.WorkCapacityFactor(person, false, 0.8f), 0.001f);
            Assert.AreEqual(1.0f, DailyNeedsService.WorkCapacityFactor(person, true, 0.6f), 0.001f,
                "Rest is suppressed together with the other condition debuffs under development immortality.");
            Assert.AreEqual(1.0f, DailyNeedsService.WorkCapacityFactor(person, false), 0.001f,
                "Callers that pass no rest quality are unchanged.");
        }
    }
}
