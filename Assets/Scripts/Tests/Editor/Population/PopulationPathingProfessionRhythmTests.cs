using System.Reflection;
using LandLedgers.FirstLedger;
using LandLedgers.Population;
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
        public void ProductionAndFarmWorkersLeaveBeforeRetailWorkers()
        {
            PersonState rancher = new() { professionId = "ranch_hand" };
            PersonState clerk = new() { professionId = "general_store_clerk" };

            float ranchDeparture = InvokeScheduleTime("GetWorkDepartureTime01", rancher);
            float clerkDeparture = InvokeScheduleTime("GetWorkDepartureTime01", clerk);

            Assert.Less(ranchDeparture, clerkDeparture);
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
