using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Logistics;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Time;
using LandLedgers.World;
using LandLedgers.World.Property;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// B1 (Agent B, expanded campaign 2026-11-16): sleeping places +
    /// planner hook. Proves the sleeping-place registry (bed/bedroll/
    /// floor resolution, one place one person one night, reservation
    /// release on waking, rotation so the worst-rested member gets the
    /// bed), the DailyActivityPlanner sleep hook (place id and rest
    /// quality on the sleep activities), rest-quality ordering and the
    /// cold-season floor penalty, ProvidedSleepingPlaces from installed
    /// beds, the work-capacity rest feed, the physical-arrival path
    /// check through the interior route engine, and that a bed hold
    /// survives a Pass 9 save/load round-trip via the asset reservation.
    /// No test claims Unity compilation or PlayMode; the per-tick
    /// movement to bed is Agent G's wiring, and obstruction proof is
    /// Codex Unity work (the harness shim returns no renderers).
    /// </summary>
    [TestFixture]
    public sealed class SleepingPlaceRegistryTests
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

        private static EquipmentAsset MakeBedroll(string assetId, int householdId, float condition = 1f)
        {
            return new EquipmentAsset
            {
                AssetId = assetId,
                Kind = "furniture.bedroll",
                DisplayName = "Bedroll",
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

        /// <summary>One installed bed (SleepingPlace capability, "bed-1") plus its registry, with cleanup.</summary>
        private sealed class BedFixture
        {
            public GameObject BuildingObject;
            public GameObject ItemObject;
            public HouseholdFurnishingRegister Register;
            public SleepingPlaceRegistry Registry;
        }

        private static BedFixture MakeBedFixture(string buildingName = "B1 bed fixture")
        {
            BuildingFootprintAuthority building = MakeBuilding(buildingName);
            BuildingEquipmentPlacement placement = MakePlacement("Bed", Vector3.zero);
            var register = RegisterWithBed();
            InstallBed(register, building, placement);
            return new BedFixture
            {
                BuildingObject = building.gameObject,
                ItemObject = placement.gameObject,
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

        private static SleepingPlaceRegistry MakeRegistry(HouseholdFurnishingRegister register)
        {
            return new SleepingPlaceRegistry
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

        [Test]
        public void Resolution_PrefersBedThenBedrollThenFloor()
        {
            BedFixture fixture = null;
            try
            {
                fixture = MakeBedFixture();
                Assert.IsNull(fixture.Register.TryRegisterAsset(MakeBedroll("roll-1", HouseholdId)));
                var registry = fixture.Registry;

                Assert.IsNull(registry.TryReserveSleepingPlace(1, Day, 1320, 420, null, out SleepingPlaceReservation first));
                Assert.AreEqual(SleepingPlaceKind.Bed, first.Kind);
                Assert.AreEqual("bed:bed-1", first.PlaceId);
                Assert.AreEqual(1.0f, first.RestQuality01);

                Assert.IsNull(registry.TryReserveSleepingPlace(2, Day, 1320, 420, null, out SleepingPlaceReservation second));
                Assert.AreEqual(SleepingPlaceKind.Bedroll, second.Kind);
                Assert.AreEqual("bedroll:roll-1", second.PlaceId);
                Assert.AreEqual(0.8f, second.RestQuality01);

                Assert.IsNull(registry.TryReserveSleepingPlace(3, Day, 1320, 420, null, out SleepingPlaceReservation third));
                Assert.AreEqual(SleepingPlaceKind.Floor, third.Kind);
                Assert.AreEqual("floor:101:P3", third.PlaceId);
                Assert.AreEqual(0.6f, third.RestQuality01);

                Assert.AreEqual(0, registry.Validate().Count, string.Join("; ", registry.Validate()));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void NoBed_StillSleepsOnFloor()
        {
            // A bed is NOT required for habitability: an unfurnished
            // house still sleeps its people, on the floor.
            var registry = MakeRegistry(MakeRegister());
            Assert.AreEqual(0, registry.InstalledBedPlaceCount());

            Assert.IsNull(registry.TryReserveSleepingPlace(1, Day, 1320, 420, null, out SleepingPlaceReservation reservation));
            Assert.AreEqual(SleepingPlaceKind.Floor, reservation.Kind);
            Assert.AreEqual(0.6f, reservation.RestQuality01);
            Assert.AreEqual(1, registry.UncoveredSleepers(new[] { 1 }, Day).Count,
                "A floor sleeper is an uncovered sleeper — the B6 furnishing need.");
        }

        [Test]
        public void TwoPersons_CannotReserveSameBed()
        {
            GameObject buildingObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B1 double-book building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", Vector3.zero);
                itemObject = placement.gameObject;
                var register = RegisterWithBed();
                InstallBed(register, building, placement);
                var registry = MakeRegistry(register);

                Assert.IsNull(registry.TryReserveSleepingPlace(1, Day, 1320, 420, null, out SleepingPlaceReservation first));
                Assert.AreEqual(SleepingPlaceKind.Bed, first.Kind);
                Assert.IsTrue(register.FindAsset("bed-1").IsReserved);
                Assert.AreEqual("sleep:P1:D100", register.FindAsset("bed-1").ReservedBy);

                // The second person resolves past the held bed to the floor.
                Assert.IsNull(registry.TryReserveSleepingPlace(2, Day, 1320, 420, null, out SleepingPlaceReservation second));
                Assert.AreEqual(SleepingPlaceKind.Floor, second.Kind);

                // And the same person cannot hold two places for one night.
                Assert.IsNotNull(registry.TryReserveSleepingPlace(1, Day, 1320, 420, null, out _));
                Assert.AreEqual(0, registry.Validate().Count, string.Join("; ", registry.Validate()));
            }
            finally
            {
                if (itemObject != null) Object.DestroyImmediate(itemObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void Release_OnWaking_ReleasesBedAndRecordsRest()
        {
            BedFixture fixture = null;
            try
            {
                fixture = MakeBedFixture();
                var register = fixture.Register;
                var registry = fixture.Registry;
                Assert.IsNull(registry.TryReserveSleepingPlace(1, Day, 1320, 420, null, out _));

                Assert.IsTrue(registry.ReleaseSleepingPlace(1, Day, out float quality));
                Assert.AreEqual(1.0f, quality);
                Assert.IsFalse(register.FindAsset("bed-1").IsReserved);
                Assert.IsNull(registry.LiveReservationFor(1, Day));
                Assert.AreEqual(1.0f, registry.LastRestQuality01(1));
                Assert.AreEqual(Day, registry.LastBedNightDayIndex(1));

                // The released bed is free again the same night.
                Assert.IsNull(registry.TryReserveSleepingPlace(2, Day, 1320, 420, null, out SleepingPlaceReservation second));
                Assert.AreEqual(SleepingPlaceKind.Bed, second.Kind);

                // Waking twice is a no-op, not a failure.
                Assert.IsFalse(registry.ReleaseSleepingPlace(1, Day, out _));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void MissedWake_NextNightRollsHoldForward()
        {
            // No sleep driver exists yet, so the wake call may never
            // run. Reserving the next night must close the stale night
            // as slept and roll the asset hold forward — never orphan
            // the mirror and never strand the bed.
            BedFixture fixture = null;
            try
            {
                fixture = MakeBedFixture();
                var register = fixture.Register;
                var registry = fixture.Registry;
                Assert.IsNull(registry.TryReserveSleepingPlace(1, Day, 1320, 420, null, out _));

                Assert.IsNull(registry.TryReserveSleepingPlace(1, Day + 1, 1320, 420, null, out SleepingPlaceReservation next));
                Assert.AreEqual(SleepingPlaceKind.Bed, next.Kind);
                Assert.IsNull(registry.LiveReservationFor(1, Day), "The stale night closes as slept.");
                Assert.AreEqual("sleep:P1:D101", register.FindAsset("bed-1").ReservedBy);
                Assert.AreEqual(Day, registry.LastBedNightDayIndex(1));
                Assert.AreEqual(0, registry.Validate().Count, string.Join("; ", registry.Validate()));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Cancel_DoesNotRecordHistory()
        {
            BedFixture fixture = null;
            try
            {
                fixture = MakeBedFixture();
                var register = fixture.Register;
                var registry = fixture.Registry;
                Assert.IsNull(registry.TryReserveSleepingPlace(1, Day, 1320, 420, null, out _));

                Assert.IsTrue(registry.CancelSleepingPlace(1, Day));
                Assert.IsFalse(register.FindAsset("bed-1").IsReserved);
                Assert.IsNull(registry.LiveReservationFor(1, Day));
                Assert.AreEqual(1.0f, registry.LastRestQuality01(1), "A cancelled night is not a night slept.");
                Assert.AreEqual(-1, registry.LastBedNightDayIndex(1));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void Rotation_WorstRestedGetsBedNextNight()
        {
            BedFixture fixture = null;
            try
            {
                fixture = MakeBedFixture();
                var registry = fixture.Registry;
                var members = new List<int> { 1, 2 };

                // Night 100: no history, id order — P1 gets the only bed.
                Assert.IsNull(registry.AssignHouseholdSleep(members, Day, null, null));
                Assert.AreEqual(SleepingPlaceKind.Bed, registry.LiveReservationFor(1, Day).Kind);
                Assert.AreEqual(SleepingPlaceKind.Floor, registry.LiveReservationFor(2, Day).Kind);
                Assert.IsTrue(registry.ReleaseSleepingPlace(1, Day, out _));
                Assert.IsTrue(registry.ReleaseSleepingPlace(2, Day, out _));

                // Night 101: P2 slept worst (0.6 vs 1.0), so P2 gets the bed.
                Assert.IsNull(registry.AssignHouseholdSleep(members, Day + 1, null, null));
                Assert.AreEqual(SleepingPlaceKind.Floor, registry.LiveReservationFor(1, Day + 1).Kind);
                Assert.AreEqual(SleepingPlaceKind.Bed, registry.LiveReservationFor(2, Day + 1).Kind);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void RestQuality_ColdSeasonFloorPenalty()
        {
            Assert.AreEqual(1.0f, SleepingPlaceRegistry.RestQualityFor(SleepingPlaceKind.Bed, true, false),
                "Cold and unheated never penalises a bed.");
            Assert.AreEqual(0.8f, SleepingPlaceRegistry.RestQualityFor(SleepingPlaceKind.Bedroll, true, false));
            Assert.AreEqual(0.6f, SleepingPlaceRegistry.RestQualityFor(SleepingPlaceKind.Floor, false, false));
            Assert.AreEqual(0.6f, SleepingPlaceRegistry.RestQualityFor(SleepingPlaceKind.Floor, true, true),
                "Fuel burned that day: no penalty.");
            Assert.AreEqual(0.5f, SleepingPlaceRegistry.RestQualityFor(SleepingPlaceKind.Floor, true, false),
                "Cold season, no fuel burned: a small penalty only.");

            var registry = MakeRegistry(MakeRegister());
            registry.IsColdSeason = true;
            registry.HeatingFuelBurnedToday = false;
            Assert.IsNull(registry.TryReserveSleepingPlace(1, Day, 1320, 420, null, out SleepingPlaceReservation reservation));
            Assert.AreEqual(0.5f, reservation.RestQuality01);
        }

        [Test]
        public void Planner_SleepWindowCarriesPlaceIdAndRestQuality()
        {
            GameObject buildingObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B1 planner building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", Vector3.zero);
                itemObject = placement.gameObject;
                var register = RegisterWithBed();
                InstallBed(register, building, placement);
                var registry = MakeRegistry(register);

                var planner = new DailyActivityPlanner();
                var schedules = new PersonScheduleTracker();
                var budgets = new WorkTimeBudgetStore();
                var diag = new List<string>();
                DailyActivityPlanner.PersonDayPlan plan =
                    planner.PlanPersonDay(Inputs(3), Day, schedules, budgets, diag, registry);

                Assert.AreEqual(0, plan.Conflicts.Count, string.Join(" | ", plan.Conflicts));
                int sleepActivities = 0;
                foreach (DailyActivityPlanner.PlannedActivity activity in plan.Activities)
                {
                    if (activity.Kind != PersonActivityKind.Sleeping) continue;
                    Assert.IsTrue(activity.Reserved);
                    Assert.AreEqual("bed:bed-1", activity.SleepingPlaceId);
                    Assert.AreEqual(1.0f, activity.RestQuality01);
                    sleepActivities++;
                }
                Assert.AreEqual(2, sleepActivities, "Sleep wraps midnight: evening + morning activities.");
                Assert.IsFalse(schedules.IsFree(3, Day, 1380, 60), "P3 should be sleeping at 23:00");
                Assert.IsFalse(schedules.IsFree(3, Day, 0, 60), "P3 should be sleeping at 00:30");
            }
            finally
            {
                if (itemObject != null) Object.DestroyImmediate(itemObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void Planner_HouseholdAssignment_RotationAware()
        {
            BedFixture fixture = null;
            try
            {
                fixture = MakeBedFixture();
                var registry = fixture.Registry;
                var planner = new DailyActivityPlanner();
                var schedules = new PersonScheduleTracker();
                var budgets = new WorkTimeBudgetStore();
                var diag = new List<string>();
                var members = new List<int> { 1, 2 };

                List<DailyActivityPlanner.PersonDayPlan> night100 =
                    planner.PlanHouseholdDay(members, Inputs, Day, schedules, budgets, diag, registry);
                Assert.AreEqual("bed:bed-1", SleepPlaceOf(night100[0]));
                Assert.AreEqual("floor:101:P2", SleepPlaceOf(night100[1]));

                Assert.IsTrue(registry.ReleaseSleepingPlace(1, Day, out _));
                Assert.IsTrue(registry.ReleaseSleepingPlace(2, Day, out _));

                List<DailyActivityPlanner.PersonDayPlan> night101 =
                    planner.PlanHouseholdDay(members, Inputs, Day + 1, schedules, budgets, diag, registry);
                Assert.AreEqual("floor:101:P1", SleepPlaceOf(night101[0]), "P1 slept well; P2 gets the bed tonight.");
                Assert.AreEqual("bed:bed-1", SleepPlaceOf(night101[1]));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        private static string SleepPlaceOf(DailyActivityPlanner.PersonDayPlan plan)
        {
            foreach (DailyActivityPlanner.PlannedActivity activity in plan.Activities)
            {
                if (activity.Kind == PersonActivityKind.Sleeping && activity.Reserved)
                    return activity.SleepingPlaceId;
            }
            return null;
        }

        [Test]
        public void Planner_WithoutRegistry_PlansSleepAsBefore()
        {
            var planner = new DailyActivityPlanner();
            var schedules = new PersonScheduleTracker();
            var budgets = new WorkTimeBudgetStore();
            var diag = new List<string>();
            DailyActivityPlanner.PersonDayPlan plan =
                planner.PlanPersonDay(Inputs(3), Day, schedules, budgets, diag);

            Assert.AreEqual(0, plan.Conflicts.Count, string.Join(" | ", plan.Conflicts));
            Assert.IsFalse(schedules.IsFree(3, Day, 1380, 60));
            foreach (DailyActivityPlanner.PlannedActivity activity in plan.Activities)
            {
                if (activity.Kind != PersonActivityKind.Sleeping) continue;
                Assert.AreEqual(string.Empty, activity.SleepingPlaceId);
                Assert.AreEqual(1.0f, activity.RestQuality01);
            }
        }

        [Test]
        public void Suitability_ProvidedPlacesReflectInstalledBeds()
        {
            GameObject buildingObject = null;
            GameObject itemObject = null;
            GameObject secondObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B1 suitability building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", Vector3.zero);
                itemObject = placement.gameObject;
                var register = RegisterWithBed();
                InstallBed(register, building, placement);
                Assert.IsNull(register.TryRegisterAsset(MakeBed("bed-2", HouseholdId)));
                BuildingEquipmentPlacement second = MakePlacement("Bed 2", new Vector3(1f, 0f, 1f));
                secondObject = second.gameObject;
                InstallBed(register, building, second, "bed-2", "bed-2");
                var registry = MakeRegistry(register);
                Assert.AreEqual(2, registry.InstalledBedPlaceCount());

                var housing = new HousingAuthority();
                var diag = new List<string>();
                housing.RegisterBuilding("b-1", "p-1", "house", 0, "test fixture", diag);
                AccommodationSpace room = housing.DefineSpace("b-1", 1, diag);
                var members = new List<PersonState>
                {
                    new PersonState { id = 1, age = 34, householdId = HouseholdId, deathDayIndex = -1 },
                    new PersonState { id = 2, age = 31, householdId = HouseholdId, deathDayIndex = -1 },
                };
                housing.Occupy(1, HouseholdId, room.SpaceId, AccommodationArrangement.Rental, 5, diag);
                housing.Occupy(2, HouseholdId, room.SpaceId, AccommodationArrangement.Rental, 5, diag);

                var evaluator = new HouseholdShelterSuitabilityEvaluator();
                HouseholdShelterSuitability withoutRegistry = evaluator.Evaluate(HouseholdId, members, housing, diag);
                Assert.AreEqual(1, withoutRegistry.ProvidedSleepingPlaces, "Space capacity stands without a registry.");

                HouseholdShelterSuitability withRegistry =
                    evaluator.Evaluate(HouseholdId, members, housing, diag, registry);
                Assert.AreEqual(2, withRegistry.ProvidedSleepingPlaces, "Installed beds are the provision.");
                Assert.AreEqual(2, withRegistry.Composition.RequiredSleepingPlaces);
                Assert.IsTrue(withRegistry.IsSuitable);
                Assert.IsTrue(withRegistry.Summary.Contains("(installed beds)"), withRegistry.Summary);

                // A registry for another household is ignored, loudly.
                var otherRegistry = new SleepingPlaceRegistry { HouseholdId = 8, Furnishings = register, HomeBuildingInstanceId = BuildingInstanceId };
                HouseholdShelterSuitability mismatched =
                    evaluator.Evaluate(HouseholdId, members, housing, diag, otherRegistry);
                Assert.AreEqual(1, mismatched.ProvidedSleepingPlaces);
            }
            finally
            {
                if (secondObject != null) Object.DestroyImmediate(secondObject);
                if (itemObject != null) Object.DestroyImmediate(itemObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void WorkCapacity_RestQualityMultiplies()
        {
            var person = new PersonState { id = 1, age = 30, householdId = HouseholdId, deathDayIndex = -1 };
            Assert.AreEqual(1.0f, DailyNeedsService.WorkCapacityFactor(person));
            Assert.AreEqual(1.0f, DailyNeedsService.WorkCapacityFactor(person, false, 1.0f));
            Assert.AreEqual(0.6f, DailyNeedsService.WorkCapacityFactor(person, false, 0.6f),
                "A floor night costs 40% of the next day's capacity through the existing factor.");
            Assert.AreEqual(0.8f, DailyNeedsService.WorkCapacityFactor(person, false, 0.8f));
            Assert.AreEqual(1.0f, DailyNeedsService.WorkCapacityFactor(person, true, 0.6f),
                "Condition debuffs suppressed under development immortality: rest included.");
        }

        [Test]
        public void Arrival_BedRequiresAuthoredLocationAndReachableRoute()
        {
            GameObject buildingObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuildingWithBedLocation("B1 arrival building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", new Vector3(2f, 0f, 2f));
                itemObject = placement.gameObject;
                var register = RegisterWithBed();
                InstallBed(register, building, placement);
                var registry = MakeRegistry(register);

                SleepingPlace bed = registry.BedPlaces()[0];
                Assert.AreEqual("bed-1", bed.ActivityLocationId);
                Assert.IsNull(SleepingPlaceArrival.TryValidateArrivalPath(bed, building),
                    "The bedside point is reachable from main-entrance through the interior route engine.");

                var unlocated = new SleepingPlace
                {
                    PlaceId = "bed:bed-1",
                    Kind = SleepingPlaceKind.Bed,
                    AssetId = "bed-1",
                    BuildingInstanceId = BuildingInstanceId,
                    ActivityLocationId = string.Empty,
                };
                Assert.IsNotNull(SleepingPlaceArrival.TryValidateArrivalPath(unlocated, building),
                    "A bed with no authored activity location is refused loudly, not silently adopted.");

                SleepingPlace floor = registry.FloorPlaceFor(1);
                Assert.IsNull(SleepingPlaceArrival.TryValidateArrivalPath(floor, building),
                    "Floor sleeping validates as standing room inside the home.");

                Assert.IsNotNull(SleepingPlaceArrival.TryValidateArrivalPath(bed, null));
                Assert.IsNotNull(SleepingPlaceArrival.TryValidateArrivalPath(null, building));
            }
            finally
            {
                if (itemObject != null) Object.DestroyImmediate(itemObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void Arrival_FloorRefusesHomeWithNoEntrance()
        {
            GameObject buildingObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B1 no-entrance building");
                buildingObject = building.gameObject;
                var registry = MakeRegistry(MakeRegister());
                Assert.IsNotNull(SleepingPlaceArrival.TryValidateArrivalPath(registry.FloorPlaceFor(1), building),
                    "No main entrance, no way in — the floor place is not reachable.");
            }
            finally
            {
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void SaveLoad_BedHoldSurvivesRestore()
        {
            GameObject buildingObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B1 saveload building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", Vector3.zero);
                itemObject = placement.gameObject;
                var register = RegisterWithBed();
                InstallBed(register, building, placement);
                var registry = MakeRegistry(register);
                Assert.IsNull(registry.TryReserveSleepingPlace(1, Day, 1320, 420, null, out _));

                var state = new Series2Pass9SaveState();
                state.HouseholdFurnishingRegisters.Add(register);
                Series2Pass9SaveDto dto = state.Capture(new List<string>());

                var restored = new Series2Pass9SaveState();
                restored.Restore(dto, new FreightShipmentHooks(), new List<string>());
                HouseholdFurnishingRegister roundTripped = restored.HouseholdFurnishingRegisters[0];
                EquipmentAsset bed = roundTripped.FindAsset("bed-1");
                Assert.IsTrue(bed.IsReserved, "The asset hold is the reservation that survives the load.");
                Assert.AreEqual("sleep:P1:D100", bed.ReservedBy);
                Assert.AreEqual("sleep bed:bed-1", bed.ReservedFor);

                var restoredRegistry = new SleepingPlaceRegistry
                {
                    HouseholdId = HouseholdId,
                    Furnishings = roundTripped,
                    HomeBuildingInstanceId = BuildingInstanceId,
                };
                restoredRegistry.RebuildFromRegister();
                SleepingPlaceReservation live = restoredRegistry.LiveReservationFor(1, Day);
                Assert.IsNotNull(live, "The mirror rebuilds from the surviving hold.");
                Assert.AreEqual("bed:bed-1", live.PlaceId);
                Assert.AreEqual(0, restoredRegistry.Validate().Count, string.Join("; ", restoredRegistry.Validate()));

                // The restored hold still blocks a second sleeper that night.
                Assert.IsNull(restoredRegistry.TryReserveSleepingPlace(2, Day, 1320, 420, null, out SleepingPlaceReservation second));
                Assert.AreEqual(SleepingPlaceKind.Floor, second.Kind);
            }
            finally
            {
                if (itemObject != null) Object.DestroyImmediate(itemObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void Validate_ReportsBedWithoutCapability()
        {
            GameObject buildingObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("B1 capability building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", Vector3.zero);
                itemObject = placement.gameObject;
                var register = RegisterWithBed();
                string error = register.TryInstall("bed-1", placement, building, BuildingInstanceId, 0,
                    FurnishingCapabilityKind.None, string.Empty,
                    FurnishingAcquisitionSource.ScenarioEndowment, "endowment-qa-town-1",
                    out _);
                Assert.IsNull(error, error);
                var registry = MakeRegistry(register);

                Assert.AreEqual(0, registry.BedPlaces().Count, "Placed without the capability, it is not a sleeping place yet.");
                Assert.AreEqual(1, registry.Validate().Count, string.Join("; ", registry.Validate()));
                Assert.IsNull(registry.TryReserveSleepingPlace(1, Day, 1320, 420, null, out SleepingPlaceReservation reservation));
                Assert.AreEqual(SleepingPlaceKind.Floor, reservation.Kind);
            }
            finally
            {
                if (itemObject != null) Object.DestroyImmediate(itemObject);
                if (buildingObject != null) Object.DestroyImmediate(buildingObject);
            }
        }

        [Test]
        public void UncoveredSleepers_ListsFloorSleepers()
        {
            BedFixture fixture = null;
            try
            {
                fixture = MakeBedFixture();
                var registry = fixture.Registry;
                var members = new List<int> { 1, 2, 3 };

                // Peek only: no reservation yet, one bed for three people.
                List<int> uncovered = registry.UncoveredSleepers(members, Day);
                Assert.AreEqual(2, uncovered.Count);
                Assert.IsTrue(uncovered.Contains(2));
                Assert.IsTrue(uncovered.Contains(3));

                // After assignment the same members are uncovered.
                Assert.IsNull(registry.AssignHouseholdSleep(members, Day, null, null));
                uncovered = registry.UncoveredSleepers(members, Day);
                Assert.AreEqual(2, uncovered.Count);
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }

        [Test]
        public void ReleaseAllForPerson_ClearsEveryHold()
        {
            BedFixture fixture = null;
            try
            {
                fixture = MakeBedFixture();
                var register = fixture.Register;
                var registry = fixture.Registry;
                Assert.IsNull(registry.TryReserveSleepingPlace(1, Day, 1320, 420, null, out _));
                Assert.IsNull(registry.TryReserveSleepingPlace(1, Day + 1, 1320, 420, null, out _));

                Assert.AreEqual(1, registry.ReleaseAllForPerson(1), "The stale night closed on the second reserve; one live hold remains.");
                Assert.IsFalse(register.FindAsset("bed-1").IsReserved);
                Assert.IsNull(registry.LiveReservationFor(1, Day + 1));
                Assert.AreEqual(0, registry.ReleaseAllForPerson(1));
            }
            finally
            {
                DestroyFixture(fixture);
            }
        }
    }
}
