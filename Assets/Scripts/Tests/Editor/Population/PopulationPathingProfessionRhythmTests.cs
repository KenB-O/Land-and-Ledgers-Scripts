using System.Reflection;
using LandLedgers.FirstLedger;
using LandLedgers.Pathing;
using LandLedgers.Population;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Population
{
    public sealed class PopulationPathingProfessionRhythmTests
    {
        [Test]
        public void NewPathingDirectorDoesNotManufactureAmbientStreetJourneysByDefault()
        {
            GameObject owner = new GameObject("Pathing configuration test");
            try
            {
                PopulationPathingDirector director = owner.AddComponent<PopulationPathingDirector>();
                FieldInfo field = typeof(PopulationPathingDirector).GetField(
                    "minimumVisibleStreetAgents",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                Assert.NotNull(field);
                Assert.That((int)field.GetValue(director), Is.Zero,
                    "Ambient walking is not a simulated reason for travel and must remain opt-in.");
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void PopulationPathingWaitsForTheMatchingGeneratedWorldGrid()
        {
            GameObject worldObject = new("Population readiness world");
            GameObject otherWorldObject = new("Other world");
            GameObject pathingObject = new("Population readiness pathing");
            PathingSettings settings = ScriptableObject.CreateInstance<PathingSettings>();
            try
            {
                TownWorldController world = worldObject.AddComponent<TownWorldController>();
                TownWorldController otherWorld = otherWorldObject.AddComponent<TownWorldController>();
                PathingManager pathing = pathingObject.AddComponent<PathingManager>();
                pathing.Configure(world, settings);

                Assert.IsFalse(PopulationPathingDirector.IsGeneratedWorldReadyForPopulation(world, pathing),
                    "An early tick before town generation must wait instead of attempting agent creation without door anchors.");

                TownGrid generatedGrid = new(8, 8, 2f, Vector3.zero);
                typeof(TownWorldController).GetField("grid", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(world, generatedGrid);

                Assert.IsTrue(PopulationPathingDirector.IsGeneratedWorldReadyForPopulation(world, pathing));
                Assert.IsFalse(PopulationPathingDirector.IsGeneratedWorldReadyForPopulation(otherWorld, pathing),
                    "A pathing manager bound to a different world must not be treated as ready for this population.");
            }
            finally
            {
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(pathingObject);
                Object.DestroyImmediate(otherWorldObject);
                Object.DestroyImmediate(worldObject);
            }
        }

        [Test]
        public void ProductionAndFarmWorkersLeaveBeforeRetailWorkers()
        {
            PersonState rancher = new() { professionId = "ranch_hand" };
            PersonState clerk = new() { professionId = "general_store_clerk" };

            float ranchDeparture = InvokeScheduleTime("GetWorkDepartureTime01", rancher);
            float clerkDeparture = InvokeScheduleTime("GetWorkDepartureTime01", clerk);

            Assert.Less(ranchDeparture, clerkDeparture);
        }

        [Test]
        public void WorkDepartureWindowClosesAfterScheduledReturn()
        {
            Assert.IsTrue(PopulationPathingDirector.IsWithinWorkDepartureWindow(0.40f, 0.30f, 0.74f));
            Assert.IsFalse(PopulationPathingDirector.IsWithinWorkDepartureWindow(0.75f, 0.30f, 0.74f),
                "A worker who returned home after the shift must not be sent back to work by a stale departure threshold.");
        }

        [Test]
        public void DomesticWindowRejectsOverlapWithScheduledPaidWork()
        {
            Assert.IsTrue(PopulationPathingDirector.OverlapsScheduledWorkWindow(660, 41, 450, 1065));
            Assert.IsFalse(PopulationPathingDirector.OverlapsScheduledWorkWindow(1080, 41, 450, 1065));
        }

        [Test]
        public void RouteAwareDepartureLeavesBeforeScheduledArrivalForTravelPreparationAndBuffer()
        {
            float departure = PopulationPathingDirector.CalculateRouteAwareDepartureTime01(
                465f / 1440f, routeMinutes: 15f, preparationMinutes: 10f, arrivalBufferMinutes: 5f);

            Assert.AreEqual(435f / 1440f, departure, 0.00001f,
                "a 7:45 start with a 15-minute route leaves by 7:15 after preparation and arrival buffer");
        }

        [Test]
        public void WorkRouteWaitsUntilNonInterruptibleSleepReservationEnds()
        {
            PersonScheduleTracker schedules = new();
            Assert.IsNull(schedules.TryReserve(3, PersonActivityKind.Sleeping, 12,
                0, 420, "sleep (morning)"));

            Assert.IsTrue(PopulationPathingDirector.IsReservedForSleepAt(schedules, 3, 12, 419));
            Assert.IsFalse(PopulationPathingDirector.IsReservedForSleepAt(schedules, 3, 12, 420));
            Assert.IsFalse(PopulationPathingDirector.IsReservedForSleepAt(schedules, 3, 13, 100));
        }

        [Test]
        public void LodgingAndSaloonWorkersReturnLaterThanProductionWorkers()
        {
            PersonState sawyer = new() { professionId = "sawmill_sawyer" };
            PersonState lodging = new() { professionId = "boarding_house_keeper" };

            float sawyerReturn = InvokeScheduleTime("GetWorkReturnTime01", sawyer);
            float lodgingReturn = InvokeScheduleTime("GetWorkReturnTime01", lodging);

            Assert.Greater(lodgingReturn, sawyerReturn);
        }

        [TestCase(PopulationScheduleState.AtWork, true)]
        [TestCase(PopulationScheduleState.GoingToWork, false)]
        [TestCase(PopulationScheduleState.GoingShopping, false)]
        [TestCase(PopulationScheduleState.AtHome, false)]
        [TestCase(PopulationScheduleState.ReturningHome, false)]
        public void ScheduledWorkReturnOnlyRetargetsWorkersWhoReachedWork(PopulationScheduleState state, bool expected)
        {
            Assert.AreEqual(expected, PopulationPathingDirector.IsEligibleForScheduledWorkReturn(state));
        }

        [Test]
        public void LocalVisualEligibilityExcludesOffMapDeadAndUnhousedRecords()
        {
            PopulationState population = new();
            population.households.Add(new HouseholdState { id = 4 });
            PersonState resident = new() { id = 19, householdId = 4, deathDayIndex = -1 };
            PersonState departed = new() { id = 20, householdId = 4, deathDayIndex = -1, isOffMap = true };
            PersonState deceased = new() { id = 21, householdId = 4, deathDayIndex = 12 };
            PersonState unhoused = new() { id = 22, householdId = -1, deathDayIndex = -1 };

            Assert.IsTrue(PopulationPathingDirector.CanRepresentOnMap(resident, population));
            Assert.IsFalse(PopulationPathingDirector.CanRepresentOnMap(departed, population));
            Assert.IsFalse(PopulationPathingDirector.CanRepresentOnMap(deceased, population));
            Assert.IsFalse(PopulationPathingDirector.CanRepresentOnMap(unhoused, population));
        }

        [Test]
        public void VisualIdentityBindingResolvesTheSameAuthoritativePerson()
        {
            PopulationState population = new();
            PersonState resident = new() { id = 73, firstName = "Ada", lastName = "Miller" };
            population.people.Add(resident);
            GameObject actor = new GameObject("Person actor binding test");
            try
            {
                PersonVisualIdentityBinding binding = actor.AddComponent<PersonVisualIdentityBinding>();
                Assert.IsTrue(binding.Bind(resident.id));
                Assert.IsTrue(binding.TryResolve(population, out PersonState resolved));
                Assert.AreSame(resident, resolved);
                Assert.AreEqual(resident.id, binding.PersonId);
                Assert.IsTrue(binding.Bind(resident.id), "Binding the same stable identity is idempotent.");
                Assert.IsFalse(binding.Bind(74), "A visual actor cannot silently become a different Person.");
                Assert.AreEqual(resident.id, binding.PersonId);
            }
            finally
            {
                Object.DestroyImmediate(actor);
            }
        }

        [TestCase(2, true)]
        [TestCase(3, false)]
        public void VisualPrefabSelectionUsesPersonAppearanceInsteadOfIdentityParity(int personId, bool isFemale)
        {
            GameObject malePrefab = new("Male appearance test prefab");
            GameObject femalePrefab = new("Female appearance test prefab");
            try
            {
                PersonState person = new() { id = personId, isFemale = isFemale };
                MethodInfo method = typeof(PopulationPathingDirector).GetMethod(
                    "ResolveAgentVisualPrefab",
                    BindingFlags.NonPublic | BindingFlags.Static);

                Assert.NotNull(method);
                GameObject selected = (GameObject)method.Invoke(
                    null,
                    new object[] { person, malePrefab, femalePrefab });

                Assert.AreSame(isFemale ? femalePrefab : malePrefab, selected);
            }
            finally
            {
                Object.DestroyImmediate(malePrefab);
                Object.DestroyImmediate(femalePrefab);
            }
        }

        private static float InvokeScheduleTime(string methodName, PersonState person)
        {
            MethodInfo method = typeof(PopulationPathingDirector).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (float)method.Invoke(null, new object[] { person });
        }
    }
}
