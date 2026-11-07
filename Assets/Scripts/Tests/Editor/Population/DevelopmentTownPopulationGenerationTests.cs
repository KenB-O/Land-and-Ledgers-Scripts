using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class DevelopmentTownPopulationGenerationTests
    {
        private GameObject worldObject;
        private GameObject managerObject;
        private GameObject interactionObject;
        private TownWorldController world;
        private PopulationManager manager;
        private PopulationGenerationSettings settings;
        private TownGenerationSettings townSettings;
        private DevelopmentTownConfiguration sixPersonTestData;
        private readonly List<BuildingDefinition> definitions = new();

        [SetUp]
        public void SetUp()
        {
            worldObject = new GameObject("TownConfigurationTestWorld");
            world = worldObject.AddComponent<TownWorldController>();
            townSettings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            townSettings.seed = 1886;
            SetPrivateField(world, "settings", townSettings);
            SetPrivateField(world, "grid", new TownGrid(60, 60, 2f, Vector3.zero));

            var plots = (List<TownPlot>)GetPrivateField(world, "plots");
            var buildings = (List<PlacedBuilding>)GetPrivateField(world, "buildings");
            for (int i = 0; i < 5; i++)
            {
                plots.Add(new TownPlot { id = 10 + i, zone = PlotZone.Residential });
                BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                definition.ConfigureRuntimeFallback($"fixture_house_{i}", $"Fixture House {i}", PlotZone.Residential,
                    new Vector2Int(4, 4), Color.white, 4f);
                definitions.Add(definition);
                buildings.Add(new PlacedBuilding { id = 101 + i, plotId = 10 + i, definition = definition });
            }

            settings = ScriptableObject.CreateInstance<PopulationGenerationSettings>();
            sixPersonTestData = CreateSixPersonDatasetUsingCommonType();
            managerObject = new GameObject("TownConfigurationPopulationManager");
            manager = managerObject.AddComponent<PopulationManager>();
            manager.Configure(world, settings, false);
            manager.HouseholdLedgers = new HouseholdLedgerRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            if (managerObject != null) Object.DestroyImmediate(managerObject);
            if (interactionObject != null) Object.DestroyImmediate(interactionObject);
            if (worldObject != null) Object.DestroyImmediate(worldObject);
            foreach (BuildingDefinition definition in definitions) if (definition != null) Object.DestroyImmediate(definition);
            if (settings != null) Object.DestroyImmediate(settings);
            if (townSettings != null) Object.DestroyImmediate(townSettings);
            if (sixPersonTestData != null) Object.DestroyImmediate(sixPersonTestData);
        }

        [Test]
        public void SmallerRegressionDataset_UsesTheSameReusableConfigurationAndPopulationAuthorities()
        {
            manager.GeneratePopulationSnapshot(sixPersonTestData);
            PopulationState state = manager.State;
            Assert.AreEqual(6, state.people.Count);
            Assert.AreEqual(3, state.households.Count);
            CollectionAssert.AreEqual(new[] { "Kennedy Baldwin-Ooms", "Emma Baldwin-Ooms", "Glen Pierce", "Tannis Pierce", "Jacob Boniecki", "Grace Boniecki" },
                state.people.OrderBy(person => person.id).Select(person => person.DisplayName).ToArray());
            CollectionAssert.AreEqual(Enumerable.Range(1, 6), state.people.OrderBy(person => person.id).Select(person => person.id));
            CollectionAssert.AreEqual(Enumerable.Range(1, 3), state.households.OrderBy(household => household.id).Select(household => household.id));

            for (int i = 0; i < 3; i++)
            {
                HouseholdState household = state.GetHousehold(i + 1);
                CollectionAssert.AreEquivalent(new[] { i * 2 + 1, i * 2 + 2 }, household.memberIds);
                Assert.GreaterOrEqual(household.homeBuildingId, 0);
                Assert.IsNotNull(household.reserves);
            }
            Assert.AreEqual(3, state.households.Select(household => household.homeBuildingId).Distinct().Count());
            Assert.AreEqual(6, manager.Memberships.ActiveCount);
            AssertSpousePair(1, 2);
            AssertSpousePair(3, 4);
            AssertSpousePair(5, 6);

            PopulationSaveDto saved = manager.CaptureSaveDto();
            manager.LoadFromSaveDto(saved);
            Assert.AreEqual(6, manager.Kinship.EdgeCount);
            AssertSpousePair(1, 2);
        }

        [Test]
        public void PersonInspection_ShowsSpouseNamesAndKeepsIdsInDevelopmentDetailsOnly()
        {
            manager.GeneratePopulationSnapshot(sixPersonTestData);
            interactionObject = new GameObject("PersonInspectionTest");
            WorldInteractionController controller = interactionObject.AddComponent<WorldInteractionController>();
            SetPrivateField(controller, "populationManager", manager);
            MethodInfo buildText = typeof(WorldInteractionController).GetMethod("BuildPersonInspectionText", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(buildText);

            SetPrivateField(controller, "showDevelopmentPersonDetails", false);
            string playerText = (string)buildText.Invoke(controller, new object[] { manager.State.GetPerson(1) });
            StringAssert.Contains("Spouse: Emma Baldwin-Ooms", playerText);
            StringAssert.DoesNotContain("Person ID:", playerText);

            SetPrivateField(controller, "showDevelopmentPersonDetails", true);
            string developerText = (string)buildText.Invoke(controller, new object[] { manager.State.GetPerson(1) });
            StringAssert.Contains("Person ID: P001", developerText);
            StringAssert.Contains("Emma Baldwin-Ooms [P002]", developerText);
        }

        [Test]
        public void CommonConfigurationRefusesToInventHomesWhenTownWorldIsTooSmall()
        {
            List<PlacedBuilding> buildings = (List<PlacedBuilding>)GetPrivateField(world, "buildings");
            buildings.RemoveAt(buildings.Count - 1);
            buildings.RemoveAt(buildings.Count - 1);
            buildings.RemoveAt(buildings.Count - 1);
            PopulationGenerationResult result = PopulationGenerator.GenerateDevelopmentTown(
                world, settings, sixPersonTestData, new KinshipRegistry(), new HouseholdLedgerRegistry(), 0, 1886, 3, 1);
            Assert.IsTrue(result.HasErrors);
            Assert.IsTrue(result.State.validationMessages.Any(message => message.Contains("needs 3 distinct real homes")));
            Assert.AreEqual(0, result.State.people.Count);
            Assert.AreEqual(0, result.State.households.Count);
        }

        [Test]
        public void AuthoredNineResidentTown_GeneratesDirectlyWithoutCompositionOrAppend()
        {
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(
                "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            Assert.IsNotNull(configuration);
            manager.GeneratePopulationSnapshot(configuration);
            Assert.AreEqual(9, manager.State.people.Count);
            Assert.AreEqual(5, manager.State.households.Count);
            Assert.AreEqual(5, manager.State.households.Select(household => household.homeBuildingId).Distinct().Count());
            Assert.AreEqual("Ethan Boniecki", manager.State.GetPerson(configuration.ResolveRuntimePersonId("ethan")).DisplayName);
            Assert.AreEqual("Shane Bryan", manager.State.GetPerson(configuration.ResolveRuntimePersonId("shane")).DisplayName);
            Assert.AreEqual(10, manager.Kinship.EdgeCount, "Five authored relationships should be restored once in the shared kinship authority.");
        }

        [Test]
        public void AuthoredNineResidentTown_MembershipBoundary_MapsNinePersonsToFiveHouseholds()
        {
            // Plan 01 Slice 0(c): membership boundary at generation. Authored
            // mapping (person IDs allocated 1-9 in asset order, households 1-5
            // in asset order): H1 {1, 2}, H2 {3, 4}, H3 {5, 6}, H4 {7, 8}, H5 {9}.
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(
                "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            Assert.IsNotNull(configuration);
            manager.GeneratePopulationSnapshot(configuration);

            Assert.AreEqual(9, manager.Memberships.ActiveCount,
                "Exactly one Active membership per resident must exist at generation.");
            CollectionAssert.AreEqual(new[] { 1, 2 }, manager.Memberships.GetActiveMembers(1));
            CollectionAssert.AreEqual(new[] { 3, 4 }, manager.Memberships.GetActiveMembers(2));
            CollectionAssert.AreEqual(new[] { 5, 6 }, manager.Memberships.GetActiveMembers(3));
            CollectionAssert.AreEqual(new[] { 7, 8 }, manager.Memberships.GetActiveMembers(4));
            CollectionAssert.AreEqual(new[] { 9 }, manager.Memberships.GetActiveMembers(5));

            int[][] authoredHouseholds =
            {
                new[] { 1, 2 }, new[] { 3, 4 }, new[] { 5, 6 }, new[] { 7, 8 }, new[] { 9 },
            };
            for (int personId = 1; personId <= 9; personId++)
            {
                int expectedHousehold = -1;
                for (int h = 0; h < authoredHouseholds.Length; h++)
                {
                    if (System.Array.IndexOf(authoredHouseholds[h], personId) >= 0)
                    {
                        expectedHousehold = h + 1;
                    }
                }
                Assert.AreEqual(expectedHousehold, manager.Memberships.GetActiveHouseholdId(personId),
                    $"P{personId} must resolve to its authored household.");
                HouseholdMembership membership = manager.Memberships.GetActiveMembership(personId);
                Assert.IsNotNull(membership, $"P{personId} must hold a non-null Active membership.");
                Assert.IsTrue(membership.IsActive);
            }

            // ActiveCount == 9 plus per-household sets covering exactly persons
            // 1-9 once proves no person holds two Active memberships.
            var covered = new List<int>();
            for (int householdId = 1; householdId <= 5; householdId++)
            {
                covered.AddRange(manager.Memberships.GetActiveMembers(householdId));
            }
            covered.Sort();
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, covered);
            CollectionAssert.IsEmpty(manager.Memberships.Diagnostics,
                "No Register rejection may fire at generation; any rejection diagnostic fails this boundary.");
        }

        [Test]
        public void AuthoredNineResidentTown_RegistryIdentityStable_GenerationThroughFirstDailyNeeds()
        {
            // Plan 01 Slice 0(c): the registry instance the daily drivers read
            // at first use must be the same live instance generation populated.
            // A replaced or stale instance is the Slice 0 mechanism-A signature.
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(
                "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            Assert.IsNotNull(configuration);
            manager.GeneratePopulationSnapshot(configuration);

            HouseholdMembershipRegistry registryAtGeneration = manager.Memberships;
            Assert.IsNotNull(registryAtGeneration);
            Assert.AreEqual(9, registryAtGeneration.ActiveCount);
            var membersAtGeneration = new Dictionary<int, List<int>>();
            for (int householdId = 1; householdId <= 5; householdId++)
            {
                membersAtGeneration[householdId] = registryAtGeneration.GetActiveMembers(householdId);
            }

            // First daily needs event, read through the same manager property
            // the runtime drivers read (SimulationDrivers.DriveDailyNeeds
            // re-reads populationManager.Memberships fresh every day).
            HouseholdMembershipRegistry registryAtFirstUse = manager.Memberships;
            Assert.IsTrue(object.ReferenceEquals(registryAtGeneration, registryAtFirstUse),
                "The registry instance at first daily needs use must be the instance generation populated.");
            var diag = new List<string>();
            DailyNeedsService.DayReport report = new DailyNeedsService().ExecuteDay(
                manager.State, new HouseholdConsumptionPlanner(), null, 0, diag,
                inventories: new HouseholdInventoryRegistry(), mealLog: new HouseholdMealLogRegistry(),
                mealPolicy: MealSchedulingPolicy.Default, memberships: registryAtFirstUse);
            Assert.AreEqual(5, report.HouseholdsServed,
                "All five households must resolve an acting adult at the first daily needs event.");
            Assert.IsTrue(diag.TrueForAll(message => !message.Contains("no acting adult")),
                "No household may report 'no acting adult' at the first daily needs event.");

            Assert.IsTrue(object.ReferenceEquals(registryAtGeneration, manager.Memberships),
                "The registry instance must still be the generation instance after the first daily needs event.");
            Assert.AreEqual(9, manager.Memberships.ActiveCount);
            for (int householdId = 1; householdId <= 5; householdId++)
            {
                CollectionAssert.AreEqual(membersAtGeneration[householdId],
                    manager.Memberships.GetActiveMembers(householdId),
                    $"H{householdId} memberships must be unchanged by the first daily needs event.");
            }
        }

        private static DevelopmentTownConfiguration CreateSixPersonDatasetUsingCommonType()
        {
            var config = ScriptableObject.CreateInstance<DevelopmentTownConfiguration>();
            SetPrivateField(config, "configurationId", "six-person-regression-data");
            SetPrivateField(config, "seedOffset", 9217);
            string[] first = { "Kennedy", "Emma", "Glen", "Tannis", "Jacob", "Grace" };
            string[] last = { "Baldwin-Ooms", "Baldwin-Ooms", "Pierce", "Pierce", "Boniecki", "Boniecki" };
            var people = new DevelopmentTownConfiguration.PersonDefinition[6];
            for (int i = 0; i < people.Length; i++)
                people[i] = new DevelopmentTownConfiguration.PersonDefinition
                { key = "person-" + i, firstName = first[i], lastName = last[i], age = 30 + i, isFemale = i % 2 == 1, homeBuildingOrdinal = i / 2 };
            var households = new DevelopmentTownConfiguration.HouseholdDefinition[3];
            for (int i = 0; i < households.Length; i++)
                households[i] = new DevelopmentTownConfiguration.HouseholdDefinition
                { key = "household-" + i, surname = last[i * 2], displayName = last[i * 2] + " Household",
                    memberPersonKeys = new[] { "person-" + (i * 2), "person-" + (i * 2 + 1) }, homeBuildingOrdinal = i };
            var relationships = new DevelopmentTownConfiguration.RelationshipDefinition[3];
            for (int i = 0; i < relationships.Length; i++)
                relationships[i] = new DevelopmentTownConfiguration.RelationshipDefinition
                { firstPersonKey = "person-" + (i * 2), secondPersonKey = "person-" + (i * 2 + 1), relationship = DevelopmentTownConfiguration.RelationshipKind.Spouse };
            SetPrivateField(config, "people", people);
            SetPrivateField(config, "households", households);
            SetPrivateField(config, "relationships", relationships);
            SetPrivateField(config, "businesses", new[] { new DevelopmentTownConfiguration.BusinessDefinition
            { key = "store", operationId = "general-store", ownerPersonKey = "person-0", premisesRequired = true } });
            SetPrivateField(config, "freightBusinessKey", "store");
            SetPrivateField(config, "openingHorseId", "A0");
            SetPrivateField(config, "openingWagonId", "test-wagon");
            Assert.IsTrue(config.Validate(out string error), error);
            return config;
        }

        private static object GetPrivateField(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void SetPrivateField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private void AssertSpousePair(int personA, int personB)
        {
            CollectionAssert.Contains(manager.Kinship.GetSpouses(personA), personB);
            CollectionAssert.Contains(manager.Kinship.GetSpouses(personB), personA);
            Assert.IsTrue(manager.Kinship.AllEdges.Any(edge => edge.FromPersonId == personA && edge.ToPersonId == personB
                && edge.Relation == KinshipRelation.Spouse && edge.Source == "development town configuration:six-person-regression-data"));
        }
    }
}
