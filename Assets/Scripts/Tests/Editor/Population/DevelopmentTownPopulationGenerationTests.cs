using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using LandLedgers.FirstLedger;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

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
            for (int i = 0; i < 8; i++)
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
            while (buildings.Count > 2) buildings.RemoveAt(buildings.Count - 1);
            PopulationGenerationResult result = PopulationGenerator.GenerateDevelopmentTown(
                world, settings, sixPersonTestData, new KinshipRegistry(), new HouseholdLedgerRegistry(), 0, 1886, 3, 1);
            Assert.IsTrue(result.HasErrors);
            Assert.IsTrue(result.State.validationMessages.Any(message => message.Contains("needs 3 distinct real homes")));
            Assert.AreEqual(0, result.State.people.Count);
            Assert.AreEqual(0, result.State.households.Count);
        }

        [Test]
        public void AuthoredTownGeneration_PreservesConfiguredIdentitiesAndMemberships()
        {
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(
                "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            Assert.IsNotNull(configuration);
            manager.GeneratePopulationSnapshot(configuration);

            Assert.AreEqual(configuration.People.Count, manager.State.people.Count);
            Assert.AreEqual(configuration.Households.Count, manager.State.households.Count);
            Assert.AreEqual(configuration.People.Count, manager.Memberships.ActiveCount);
            foreach (var householdDefinition in configuration.Households)
            {
                HouseholdState household = manager.State.GetHousehold(householdDefinition.runtimeHouseholdId);
                Assert.IsNotNull(household, householdDefinition.key);
                CollectionAssert.AreEquivalent(householdDefinition.memberPersonKeys.Select(configuration.ResolveRuntimePersonId),
                    household.memberIds, householdDefinition.key);
            }
            foreach (var personDefinition in configuration.People)
            {
                PersonState person = manager.State.GetPerson(configuration.ResolveRuntimePersonId(personDefinition.key));
                Assert.IsNotNull(person, personDefinition.key);
                Assert.AreEqual(personDefinition.firstName + " " + personDefinition.lastName, person.DisplayName);
                DevelopmentTownConfiguration.HouseholdDefinition home = configuration.Households.Single(h =>
                    h.memberPersonKeys.Contains(personDefinition.key));
                Assert.AreEqual(home.runtimeHouseholdId, manager.Memberships.GetActiveHouseholdId(person.id));
            }
        }

        [Test]
        public void OpeningHouseholdCash_IsSeededOnceAcrossRepeatedGeneration()
        {
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(
                "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            manager.GeneratePopulationSnapshot(configuration);
            int householdId = configuration.Households[0].runtimeHouseholdId;
            HouseholdLedger ledger = manager.HouseholdLedgers.Get(householdId);
            Assert.IsNotNull(ledger);
            Assert.AreEqual(configuration.Households[0].openingCashCents, ledger.GetBalanceCents());

            manager.GeneratePopulationSnapshot(configuration);
            ledger = manager.HouseholdLedgers.Get(householdId);
            Assert.AreEqual(configuration.Households[0].openingCashCents, ledger.GetBalanceCents(),
                "Rebuilding a development population must not duplicate configured opening cash.");
        }

        [Test]
        public void AuthoredPopulationSaveLoad_PreservesStablePersonAndHouseholdIds()
        {
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(
                "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            manager.GeneratePopulationSnapshot(configuration);
            PopulationSaveDto dto = manager.CaptureSaveDto();
            GameObject freshObject = null;
            try
            {
                PopulationManager fresh = CreateFreshManager(out freshObject);
                fresh.LoadFromSaveDto(dto);
                Assert.AreEqual(configuration.People.Count, fresh.State.people.Count);
                Assert.AreEqual(configuration.Households.Count, fresh.State.households.Count);
                Assert.AreEqual(configuration.People.Count, fresh.Memberships.ActiveCount);
                foreach (var person in configuration.People)
                    Assert.IsNotNull(fresh.State.GetPerson(configuration.ResolveRuntimePersonId(person.key)), person.key);
                foreach (var household in configuration.Households)
                    Assert.IsNotNull(fresh.State.GetHousehold(household.runtimeHouseholdId), household.key);
            }
            finally
            {
                if (freshObject != null) Object.DestroyImmediate(freshObject);
            }
        }

        private PopulationManager CreateFreshManager(out GameObject freshObject)
        {
            // Plan 01 Slice 2: a second manager sharing the test world,
            // configured exactly like the SetUp manager, for load-into-
            // fresh-manager round-trip tests. The caller destroys it.
            freshObject = new GameObject("FreshPopulationManager");
            var fresh = freshObject.AddComponent<PopulationManager>();
            fresh.Configure(world, settings, false);
            fresh.HouseholdLedgers = new HouseholdLedgerRegistry();
            return fresh;
        }

        [Test]
        public void DuplicatePopulationManager_LogsErrorNamingEveryInstance()
        {
            // Plan 01 Slice 1a: the bootstrapper's duplicate-authority audit
            // must cover PopulationManager (the mechanism-B diagnostic) and
            // name every instance found. Detection only - the scene keeps
            // running on the first found instance.
            GameObject duplicateObject = new GameObject("DuplicatePopulationManager");
            GameObject bootstrapperObject = new GameObject("DuplicateAuthorityTestBootstrapper");
            try
            {
                duplicateObject.AddComponent<PopulationManager>();
                FirstLedgerSliceBootstrapper bootstrapper =
                    bootstrapperObject.AddComponent<FirstLedgerSliceBootstrapper>();
                MethodInfo reportDuplicates = typeof(FirstLedgerSliceBootstrapper).GetMethod(
                    "ReportDuplicateSceneAuthorities", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(reportDuplicates, "The duplicate-authority audit must exist on the bootstrapper.");

                LogAssert.Expect(LogType.Error, new Regex("Duplicate 'PopulationManager' authority in scene"));
                reportDuplicates.Invoke(bootstrapper, null);
            }
            finally
            {
                Object.DestroyImmediate(duplicateObject);
                Object.DestroyImmediate(bootstrapperObject);
                // The bootstrapper's Awake wires supporting objects when the
                // scene has none; remove what this test created so later
                // tests start from the same scene.
                GameObject saveLoadObject = GameObject.Find("Save Load Manager");
                if (saveLoadObject != null)
                {
                    Object.DestroyImmediate(saveLoadObject);
                }
                GameObject eventSystemObject = GameObject.Find("UI Event System");
                if (eventSystemObject != null)
                {
                    Object.DestroyImmediate(eventSystemObject);
                }
                GameObject civicObject = GameObject.Find("Civic Foundation");
                if (civicObject != null)
                {
                    Object.DestroyImmediate(civicObject);
                }
            }
        }

        private static DevelopmentTownConfiguration CreateSixPersonDatasetUsingCommonType()
        {
            var config = ScriptableObject.CreateInstance<DevelopmentTownConfiguration>();
            config.Settings.configurationId = "six-person-regression-data";
            config.Settings.seedOffset = 9217;
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
            var storeBusiness = new DevelopmentTownConfiguration.BusinessDefinition
            { key = "store", operationId = "general-store", ownerPersonKey = "person-0", premisesRequired = true };
            storeBusiness.opening.openingHorseId = "A0";
            storeBusiness.opening.openingWagonId = "test-wagon";
            SetPrivateField(config, "businesses", new[] { storeBusiness });
            config.Settings.freightBusinessKey = "store";
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
