using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LandLedgers.Population;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class SixPersonDevelopmentFixtureTests
    {
        private GameObject worldObject;
        private GameObject managerObject;
        private TownWorldController world;
        private PopulationManager manager;
        private PopulationGenerationSettings settings;
        private SixPersonDevelopmentFixture fixture;
        private readonly List<BuildingDefinition> definitions = new();

        [SetUp]
        public void SetUp()
        {
            worldObject = new GameObject("SixPersonFixtureWorld");
            world = worldObject.AddComponent<TownWorldController>();
            SetPrivateField(world, "settings", new TownGenerationSettings { seed = 1886 });
            SetPrivateField(world, "grid", new TownGrid(60, 60, 2f, Vector3.zero));

            var plots = (List<TownPlot>)GetPrivateField(world, "plots");
            var buildings = (List<PlacedBuilding>)GetPrivateField(world, "buildings");
            for (int i = 0; i < 3; i++)
            {
                plots.Add(new TownPlot { id = 10 + i, zone = PlotZone.Residential });
                BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                definition.ConfigureRuntimeFallback(
                    $"fixture_house_{i}", $"Fixture House {i}", PlotZone.Residential,
                    new Vector2Int(4, 4), Color.white, 4f);
                definitions.Add(definition);
                buildings.Add(new PlacedBuilding
                {
                    id = 101 + i,
                    plotId = 10 + i,
                    definition = definition
                });
            }

            settings = ScriptableObject.CreateInstance<PopulationGenerationSettings>();
            fixture = ScriptableObject.CreateInstance<SixPersonDevelopmentFixture>();
            managerObject = new GameObject("SixPersonFixturePopulationManager");
            manager = managerObject.AddComponent<PopulationManager>();
            manager.Configure(world, settings, false);
        }

        [TearDown]
        public void TearDown()
        {
            if (managerObject != null) Object.DestroyImmediate(managerObject);
            if (worldObject != null) Object.DestroyImmediate(worldObject);
            foreach (BuildingDefinition definition in definitions)
            {
                if (definition != null) Object.DestroyImmediate(definition);
            }
            if (settings != null) Object.DestroyImmediate(settings);
            if (fixture != null) Object.DestroyImmediate(fixture);
        }

        [Test]
        public void FixtureGeneration_UsesPopulationManagerAndBindsSixPeopleToThreeRealHomes()
        {
            manager.GeneratePopulationSnapshot(fixture);

            PopulationState state = manager.State;
            Assert.AreEqual(6, state.people.Count);
            Assert.AreEqual(3, state.households.Count);
            CollectionAssert.AreEqual(
                new[] { "Kennedy Baldwin-Ooms", "Emma Baldwin-Ooms", "Glen Pierce", "Cassidy Pierce", "Jacob Boniecki", "Grace Boniecki" },
                state.people.OrderBy(person => person.id).Select(person => person.DisplayName).ToArray());
            CollectionAssert.AreEqual(Enumerable.Range(1, 6), state.people.OrderBy(person => person.id).Select(person => person.id));
            CollectionAssert.AreEqual(Enumerable.Range(1, 3), state.households.OrderBy(household => household.id).Select(household => household.id));
            Assert.AreEqual(7, state.NextPersonId);
            Assert.AreEqual(4, state.NextHouseholdId);

            int[][] expectedPairs = { new[] { 1, 2 }, new[] { 3, 4 }, new[] { 5, 6 } };
            for (int i = 0; i < expectedPairs.Length; i++)
            {
                HouseholdState household = state.GetHousehold(i + 1);
                CollectionAssert.AreEquivalent(expectedPairs[i], household.memberIds);
                Assert.GreaterOrEqual(household.homeBuildingId, 0);
                Assert.IsTrue(world.Buildings.Any(building => building.id == household.homeBuildingId));
                Assert.AreEqual(household.homeBuildingId, state.GetPerson(expectedPairs[i][0]).homeBuildingId);
                Assert.AreEqual(household.homeBuildingId, state.GetPerson(expectedPairs[i][1]).homeBuildingId);
                Assert.IsNotNull(household.reserves, "Household reserve/needs state must be initialized by the existing population path.");
            }

            Assert.AreEqual(3, state.households.Select(household => household.homeBuildingId).Distinct().Count());
            foreach (PersonState person in state.people)
            {
                Assert.GreaterOrEqual(person.age, 18);
                Assert.IsTrue(person.birthDate.IsValid);
                Assert.IsFalse(state.validationMessages.Any(message => message.Contains($"Person {person.id} age")
                    && message.Contains("disagrees with birth date")));
                Assert.IsNotNull(person.nutrition);
                Assert.GreaterOrEqual(manager.Memberships.GetActiveHouseholdId(person.id), 1);
                Assert.AreEqual(person.householdId, manager.Memberships.GetActiveHouseholdId(person.id));
                Assert.AreEqual(-1, person.workplaceBuildingId);
                Assert.AreEqual("unemployed", person.professionId);
            }

            Assert.IsNotNull(manager.Lifecycle);
            Assert.AreEqual(6, manager.Memberships.ActiveCount);
        }

        [Test]
        public void FixtureGeneration_RefusesToInventHomesWhenTownWorldHasFewerThanThree()
        {
            List<PlacedBuilding> buildings = (List<PlacedBuilding>)GetPrivateField(world, "buildings");
            buildings.RemoveAt(buildings.Count - 1);

            PopulationGenerationResult result = PopulationGenerator.GenerateDevelopmentFixture(
                world, settings, fixture, new KinshipRegistry(), 1886, 3, 1);

            Assert.IsTrue(result.HasErrors);
            Assert.IsTrue(result.State.validationMessages.Any(message => message.Contains("requires 3 distinct residential home buildings")));
            Assert.AreEqual(0, result.State.people.Count);
            Assert.AreEqual(0, result.State.households.Count);
        }

        private static object GetPrivateField(object target, string name)
        {
            return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }
    }
}
