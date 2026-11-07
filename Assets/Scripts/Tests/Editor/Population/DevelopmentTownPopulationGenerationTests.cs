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

        [Test]
        public void AuthoredNineResidentTown_DailyNeeds_LotBackedMealsServeAllHouseholds()
        {
            // Plan 01 Slice 3, Unity Test Runner only: the full end-to-end
            // path through the real manager. With the repaired registry,
            // ExecuteDay over the generated population serves all 5
            // households with a resolved acting adult and lot-backed meal
            // consumption: forty loaves per household is 3.3+ staple days of
            // supply for a two-person household, so the flexible midday slot
            // is served - three scheduled slots per resident, 27 meals, each
            // consuming one real loaf and recorded in the meal log. Passing
            // is left for Codex; this test never runs in the Roslyn harness
            // (this file is harness-excluded).
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(
                "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            Assert.IsNotNull(configuration);
            manager.GeneratePopulationSnapshot(configuration);
            Assert.AreEqual(9, manager.Memberships.ActiveCount,
                "Exactly one Active membership per resident must exist at generation.");

            var inventories = new HouseholdInventoryRegistry();
            var mealLog = new HouseholdMealLogRegistry();
            for (int householdId = 1; householdId <= 5; householdId++)
            {
                Assert.IsNull(inventories.GetOrCreate(householdId).AddLot(
                    HouseholdItemCatalog.BreadId, 40, 0, "test larder"),
                    "Bread lots must land in the household inventory.");
            }

            var diag = new List<string>();
            DailyNeedsService.DayReport report = new DailyNeedsService().ExecuteDay(
                manager.State, new HouseholdConsumptionPlanner(), null, 0, diag,
                inventories: inventories, mealLog: mealLog,
                mealPolicy: MealSchedulingPolicy.Default, memberships: manager.Memberships);

            Assert.AreEqual(5, report.HouseholdsServed,
                "All five households must resolve an acting adult at the first daily needs event.");
            Assert.AreEqual(27, report.MealsEaten,
                "Nine residents x three scheduled slots, all lot-backed.");
            Assert.AreEqual(0, report.MealsMissed);
            Assert.IsTrue(diag.TrueForAll(message => !message.Contains("no acting adult")),
                "No household may report 'no acting adult' at the first daily needs event.");
            int[][] authoredHouseholds =
            {
                new[] { 1, 2 }, new[] { 3, 4 }, new[] { 5, 6 }, new[] { 7, 8 }, new[] { 9 },
            };
            int loggedMeals = 0;
            for (int h = 0; h < authoredHouseholds.Length; h++)
            {
                loggedMeals += mealLog.GetMeals(h + 1).Count;
                Assert.AreEqual(40 - 3 * authoredHouseholds[h].Length,
                    inventories.GetOrCreate(h + 1).GetAvailableUnits(HouseholdItemCatalog.BreadId),
                    $"H{h + 1} bread must fall by exactly one loaf per meal served.");
            }
            Assert.AreEqual(27, loggedMeals, "Every served meal must be recorded in the meal log.");
        }

        [Test]
        public void AuthoredNineResidentTown_RebuildTwice_IsIdempotent()
        {
            // Plan 01 Slice 1a: a second generation on the same manager is
            // clear-then-load on the SAME registry instance - no duplicates,
            // no ActiveCount growth, identical members, no rejections.
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(
                "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            Assert.IsNotNull(configuration);
            manager.GeneratePopulationSnapshot(configuration);
            HouseholdMembershipRegistry registryAfterFirst = manager.Memberships;
            Assert.IsNotNull(registryAfterFirst);
            Assert.AreEqual(9, registryAfterFirst.ActiveCount);

            manager.GeneratePopulationSnapshot(configuration);

            Assert.IsTrue(object.ReferenceEquals(registryAfterFirst, manager.Memberships),
                "Rebuild must clear-then-load the same registry instance, never replace it.");
            Assert.AreEqual(9, manager.Memberships.ActiveCount);
            CollectionAssert.AreEqual(new[] { 1, 2 }, manager.Memberships.GetActiveMembers(1));
            CollectionAssert.AreEqual(new[] { 3, 4 }, manager.Memberships.GetActiveMembers(2));
            CollectionAssert.AreEqual(new[] { 5, 6 }, manager.Memberships.GetActiveMembers(3));
            CollectionAssert.AreEqual(new[] { 7, 8 }, manager.Memberships.GetActiveMembers(4));
            CollectionAssert.AreEqual(new[] { 9 }, manager.Memberships.GetActiveMembers(5));
            for (int personId = 1; personId <= 9; personId++)
            {
                Assert.IsNotNull(manager.Memberships.GetActiveMembership(personId),
                    $"P{personId} must hold a membership after the second generation.");
            }
            CollectionAssert.IsEmpty(manager.Memberships.Diagnostics,
                "No Register rejection may fire on a rebuild.");
        }

        [Test]
        public void AuthoredNineResidentTown_GenerateAfterClear_MatchesLatestGenerationOnly()
        {
            // Plan 01 Slice 1a: fresh New Game after a previous session -
            // generate, clear, generate again on the same manager. The
            // registry must match the latest generation only, with no stale
            // members from the earlier session. (Slice 2: ClearPopulationSnapshot
            // now also clears the registry on the same instance, so the
            // intermediate state is empty; the dedicated
            // ClearPopulationSnapshot_ClearsRegistryOnSameInstance test
            // asserts that, and this test asserts the regeneration.)
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(
                "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            Assert.IsNotNull(configuration);
            manager.GeneratePopulationSnapshot(configuration);
            HouseholdMembershipRegistry registryBeforeClear = manager.Memberships;
            Assert.AreEqual(9, registryBeforeClear.ActiveCount);

            manager.ClearPopulationSnapshot();
            Assert.AreEqual(0, manager.State.people.Count);
            Assert.AreEqual(0, manager.State.households.Count);

            manager.GeneratePopulationSnapshot(configuration);

            Assert.IsTrue(object.ReferenceEquals(registryBeforeClear, manager.Memberships),
                "The registry instance must survive a clear and a regeneration; holders keep reading the same live object.");
            Assert.AreEqual(9, manager.Memberships.ActiveCount,
                "The registry must hold exactly the latest generation's nine memberships, no more.");
            CollectionAssert.AreEqual(new[] { 1, 2 }, manager.Memberships.GetActiveMembers(1));
            CollectionAssert.AreEqual(new[] { 3, 4 }, manager.Memberships.GetActiveMembers(2));
            CollectionAssert.AreEqual(new[] { 5, 6 }, manager.Memberships.GetActiveMembers(3));
            CollectionAssert.AreEqual(new[] { 7, 8 }, manager.Memberships.GetActiveMembers(4));
            CollectionAssert.AreEqual(new[] { 9 }, manager.Memberships.GetActiveMembers(5));
            CollectionAssert.IsEmpty(manager.Memberships.Diagnostics,
                "No Register rejection may fire on the regeneration after clear.");
        }

        [Test]
        public void AuthoredNineResidentTown_SaveLoadRoundTrip_PreservesMembershipsAndCursor()
        {
            // Plan 01 Slice 2 (UNITY-OWED): generate, capture the save
            // DTO, load it into a FRESH manager, and assert identical
            // memberships - same membership IDs, same person/household
            // pairs, same ActiveCount - with the membership allocator
            // cursor restored so the next membership ID (9) does not
            // collide with the restored set (0-8).
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(
                "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            Assert.IsNotNull(configuration);
            manager.GeneratePopulationSnapshot(configuration);

            var membershipIdsBefore = new Dictionary<int, int>();
            var membersBefore = new Dictionary<int, List<int>>();
            for (int personId = 1; personId <= 9; personId++)
            {
                membershipIdsBefore[personId] = manager.Memberships.GetActiveMembership(personId).MembershipId;
            }
            for (int householdId = 1; householdId <= 5; householdId++)
            {
                membersBefore[householdId] = manager.Memberships.GetActiveMembers(householdId);
            }

            PopulationSaveDto saved = manager.CaptureSaveDto();
            Assert.IsNotNull(saved.householdMemberships);
            Assert.AreEqual(9, saved.householdMemberships.memberships.Count);

            GameObject freshObject = null;
            try
            {
                PopulationManager fresh = CreateFreshManager(out freshObject);
                fresh.LoadFromSaveDto(saved);

                Assert.IsNotNull(fresh.Memberships);
                Assert.AreEqual(9, fresh.Memberships.ActiveCount,
                    "The loaded registry must hold exactly the nine saved memberships.");
                CollectionAssert.IsEmpty(fresh.Memberships.Diagnostics,
                    "No Register rejection may fire when restoring saved memberships.");
                for (int personId = 1; personId <= 9; personId++)
                {
                    HouseholdMembership membership = fresh.Memberships.GetActiveMembership(personId);
                    Assert.IsNotNull(membership, $"P{personId} must hold a membership after load.");
                    Assert.AreEqual(membershipIdsBefore[personId], membership.MembershipId,
                        $"P{personId} membership id must survive the round-trip unchanged.");
                }
                for (int householdId = 1; householdId <= 5; householdId++)
                {
                    CollectionAssert.AreEqual(membersBefore[householdId], fresh.Memberships.GetActiveMembers(householdId),
                        $"H{householdId} members must be identical after load.");
                }

                var entityIds = (EntityIdRegistry)GetPrivateField(fresh, "entityIds");
                Assert.IsNotNull(entityIds);
                Assert.AreEqual(9, entityIds.PeekNext(EntityKind.Membership),
                    "The membership allocator cursor must be restored to 9 so the next allocated membership id cannot collide with the restored 0-8.");
            }
            finally
            {
                if (freshObject != null) Object.DestroyImmediate(freshObject);
            }
        }

        [Test]
        public void AuthoredNineResidentTown_SaveLoadLegacyNullDto_RebuildsFromMemberIds()
        {
            // Plan 01 Slice 2 (UNITY-OWED): a save DTO with
            // householdMemberships == null (legacy path) rebuilds the
            // memberships from the persisted household memberIds mirrors,
            // reproducing the same pairs and ActiveCount.
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(
                "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            Assert.IsNotNull(configuration);
            manager.GeneratePopulationSnapshot(configuration);

            PopulationSaveDto saved = manager.CaptureSaveDto();
            saved.householdMemberships = null;

            GameObject freshObject = null;
            try
            {
                PopulationManager fresh = CreateFreshManager(out freshObject);
                fresh.LoadFromSaveDto(saved);

                Assert.IsNotNull(fresh.Memberships);
                Assert.AreEqual(9, fresh.Memberships.ActiveCount,
                    "The legacy rebuild must reproduce all nine memberships from memberIds.");
                CollectionAssert.IsEmpty(fresh.Memberships.Diagnostics,
                    "No Register rejection may fire on the legacy rebuild.");
                CollectionAssert.AreEqual(new[] { 1, 2 }, fresh.Memberships.GetActiveMembers(1));
                CollectionAssert.AreEqual(new[] { 3, 4 }, fresh.Memberships.GetActiveMembers(2));
                CollectionAssert.AreEqual(new[] { 5, 6 }, fresh.Memberships.GetActiveMembers(3));
                CollectionAssert.AreEqual(new[] { 7, 8 }, fresh.Memberships.GetActiveMembers(4));
                CollectionAssert.AreEqual(new[] { 9 }, fresh.Memberships.GetActiveMembers(5));
                for (int personId = 1; personId <= 9; personId++)
                {
                    HouseholdMembership membership = fresh.Memberships.GetActiveMembership(personId);
                    Assert.IsNotNull(membership, $"P{personId} must hold a membership after the legacy rebuild.");
                    Assert.AreEqual(personId - 1, membership.MembershipId,
                        "A fresh manager rebuilds membership ids 0-8 in household-then-member order.");
                }
            }
            finally
            {
                if (freshObject != null) Object.DestroyImmediate(freshObject);
            }
        }

        [Test]
        public void AuthoredNineResidentTown_SaveLoadTwice_IsIdempotent()
        {
            // Plan 01 Slice 2 (UNITY-OWED): loading the same save DTO
            // twice is clear-then-load on the SAME registry instance -
            // identical memberships, no duplicates, no ActiveCount growth.
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(
                "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            Assert.IsNotNull(configuration);
            manager.GeneratePopulationSnapshot(configuration);

            PopulationSaveDto saved = manager.CaptureSaveDto();

            GameObject freshObject = null;
            try
            {
                PopulationManager fresh = CreateFreshManager(out freshObject);
                fresh.LoadFromSaveDto(saved);
                HouseholdMembershipRegistry registryAfterFirstLoad = fresh.Memberships;
                Assert.IsNotNull(registryAfterFirstLoad);
                Assert.AreEqual(9, registryAfterFirstLoad.ActiveCount);

                fresh.LoadFromSaveDto(saved);

                Assert.IsTrue(object.ReferenceEquals(registryAfterFirstLoad, fresh.Memberships),
                    "Double load must clear-then-load the same registry instance, never replace it.");
                Assert.AreEqual(9, fresh.Memberships.ActiveCount,
                    "A second load must not grow ActiveCount or duplicate memberships.");
                CollectionAssert.IsEmpty(fresh.Memberships.Diagnostics,
                    "No Register rejection may fire on the second load.");
                for (int personId = 1; personId <= 9; personId++)
                {
                    HouseholdMembership membership = fresh.Memberships.GetActiveMembership(personId);
                    Assert.IsNotNull(membership, $"P{personId} must hold a membership after the second load.");
                    Assert.AreEqual(personId - 1, membership.MembershipId,
                        $"P{personId} membership id must be unchanged by the second load.");
                }
                CollectionAssert.AreEqual(new[] { 1, 2 }, fresh.Memberships.GetActiveMembers(1));
                CollectionAssert.AreEqual(new[] { 3, 4 }, fresh.Memberships.GetActiveMembers(2));
                CollectionAssert.AreEqual(new[] { 5, 6 }, fresh.Memberships.GetActiveMembers(3));
                CollectionAssert.AreEqual(new[] { 7, 8 }, fresh.Memberships.GetActiveMembers(4));
                CollectionAssert.AreEqual(new[] { 9 }, fresh.Memberships.GetActiveMembers(5));
            }
            finally
            {
                if (freshObject != null) Object.DestroyImmediate(freshObject);
            }
        }

        [Test]
        public void AuthoredNineResidentTown_ClearPopulationSnapshot_ClearsRegistryOnSameInstance()
        {
            // Plan 01 Slice 2 (UNITY-OWED): ClearPopulationSnapshot also
            // clears the registry on the same instance, so a New Game
            // reset cannot leave stale memberships behind. This is the
            // intermediate state the Slice 1 GenerateAfterClear test
            // deliberately did not assert.
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(
                "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            Assert.IsNotNull(configuration);
            manager.GeneratePopulationSnapshot(configuration);
            HouseholdMembershipRegistry registryBeforeClear = manager.Memberships;
            Assert.IsNotNull(registryBeforeClear);
            Assert.AreEqual(9, registryBeforeClear.ActiveCount);

            manager.ClearPopulationSnapshot();

            Assert.IsTrue(object.ReferenceEquals(registryBeforeClear, manager.Memberships),
                "Clear must empty the registry in place, never replace the instance.");
            Assert.AreEqual(0, manager.Memberships.ActiveCount,
                "No stale membership may survive a population snapshot clear.");
            CollectionAssert.IsEmpty(manager.Memberships.Diagnostics,
                "Clear drops prior diagnostics with the records.");
            for (int householdId = 1; householdId <= 5; householdId++)
            {
                CollectionAssert.IsEmpty(manager.Memberships.GetActiveMembers(householdId),
                    $"H{householdId} must have no members after the clear.");
            }
            for (int personId = 1; personId <= 9; personId++)
            {
                Assert.AreEqual(-1, manager.Memberships.GetActiveHouseholdId(personId),
                    $"P{personId} must hold no membership after the clear.");
                Assert.IsNull(manager.Memberships.GetActiveMembership(personId));
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
