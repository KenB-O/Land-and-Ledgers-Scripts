using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LandLedgers.PlayMode
{
    public sealed class PlayableScenarioStartupPlayModeTests
    {
        [UnityTest]
        public IEnumerator MainSceneLoadsTheAuthoredScenarioEntryAndStartsFirstLedger()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(load, "Main Scene must be present in the build settings.");
            while (!load.isDone)
            {
                yield return null;
            }

            Type startPanelType = RuntimeType("LandLedgers.FirstLedger.ScenarioStartPanelController");
            Type bootstrapperType = RuntimeType("LandLedgers.FirstLedger.FirstLedgerSliceBootstrapper");
            Type directorType = RuntimeType("LandLedgers.Orchestration.Scenarios.ScenarioDirector");
            Type timeManagerType = RuntimeType("LandLedgers.Time.TimeManager");
            Type populationManagerType = RuntimeType("LandLedgers.Population.PopulationManager");

            Component startPanel = FindComponent(startPanelType);
            Assert.NotNull(startPanel, "Main Scene must expose the authored scenario start panel.");
            Assert.NotNull(FindComponent(bootstrapperType), "Main Scene must own the real world bootstrapper.");

            Component director = FindComponent(directorType);
            Assert.NotNull(director, "Main Scene must own the real ScenarioDirector.");
            Component populationManager = FindComponent(populationManagerType);
            Assert.NotNull(populationManager, "Main Scene must own the authoritative PopulationManager.");
            Component timeManager = FindComponent(timeManagerType);
            Assert.NotNull(timeManager, "Main Scene must own the authoritative clock.");

            yield return null;
            yield return null;
            Assert.AreSame(
                populationManagerType.GetProperty("Lifecycle")?.GetValue(populationManager),
                directorType.GetProperty("Lifecycle")?.GetValue(director),
                "Scenario household declarations must use PopulationManager's authoritative lifecycle, not a parallel membership registry.");
            Assert.IsTrue((bool)timeManagerType.GetProperty("IsPaused")?.GetValue(timeManager),
                "The first-session scenario choice promises the campaign clock will not advance before Start.");
            int dayBeforeStart = (int)timeManagerType.GetProperty("CurrentAbsoluteDayIndex")?.GetValue(timeManager);
            float secondsBeforeStart = (float)timeManagerType.GetProperty("GameSecondsIntoDay")?.GetValue(timeManager);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.AreEqual(dayBeforeStart, (int)timeManagerType.GetProperty("CurrentAbsoluteDayIndex")?.GetValue(timeManager));
            Assert.AreEqual(secondsBeforeStart, (float)timeManagerType.GetProperty("GameSecondsIntoDay")?.GetValue(timeManager),
                0.001f, "The authoritative clock must remain fixed while the scenario choice is open.");

            startPanelType.GetMethod("StartScenario", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(startPanel, null);
            Assert.IsFalse((bool)timeManagerType.GetProperty("IsPaused")?.GetValue(timeManager),
                "Selecting First Ledger should resume the existing authoritative clock.");
            Assert.AreEqual("first-ledger", directorType.GetProperty("ActiveScenarioId")?.GetValue(director));

            yield return null;
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator MainSceneDailyNeedsBuildsJourneysFromRealRoadsAndBuildingAccess()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(load, "Main Scene must be present in the build settings.");
            while (!load.isDone) yield return null;

            Type harnessType = RuntimeType("LandLedgers.Orchestration.Scenarios.Development.ScenarioValidationHarness");
            Component harness = FindComponent(harnessType);
            Assert.NotNull(harness, "The Main Scene must initialize its supported scenario bootstrap.");
            yield return WaitForScenarioRegistration(harness, harnessType);
            Assert.IsTrue((bool)harnessType.GetMethod("StartScenario")?.Invoke(harness, new object[] { "first-ledger" }),
                "Journey verification starts through the authored scenario authority.");
            harnessType.GetMethod("AdvanceHours")?.Invoke(harness, new object[] { 24 });
            yield return null;

            Type worldType = RuntimeType("LandLedgers.World.TownWorldController");
            Type driversType = RuntimeType("LandLedgers.Orchestration.Systems.SimulationDrivers");
            Type hubType = RuntimeType("LandLedgers.Orchestration.Systems.SimulationSystemsHub");
            Component world = FindComponent(worldType);
            Component drivers = FindComponent(driversType);
            Component hub = FindComponent(hubType);
            Assert.NotNull(world);
            Assert.NotNull(drivers);
            Assert.NotNull(hub);
            Assert.NotNull(worldType.GetProperty("Grid")?.GetValue(world),
                "World generation must provide the real road grid before journey wiring.");

            object journeys = hubType.GetProperty("Journeys")?.GetValue(hub);
            Assert.NotNull(journeys, "The journey authority must be the SystemsHub-owned model.");
            object locations = journeys.GetType().GetMethod("AllLocations")?.Invoke(journeys, null);
            Assert.NotNull(locations);
            var townLocations = new List<string>();
            foreach (object location in (System.Collections.IEnumerable)locations)
            {
                string id = location.GetType().GetField("LocationId")?.GetValue(location) as string;
                if (!string.IsNullOrWhiteSpace(id)) townLocations.Add(id);
            }

            Assert.Contains("town-center", townLocations,
                "The live graph must include the selected road junction nearest the real town center.");
            string buildingLocation = townLocations.Find(id => id.StartsWith("building:", StringComparison.Ordinal));
            Assert.IsNotNull(buildingLocation,
                "At least one actual building with physical road access must register as a journey destination.");

            Type travelModeType = RuntimeType("LandLedgers.World.Journeys.TravelMode");
            object foot = Enum.Parse(travelModeType, "Foot");
            object route = journeys.GetType().GetMethod("FindRoute")?.Invoke(
                journeys, new[] { (object)"town-center", buildingLocation, foot });
            Assert.IsTrue((bool)route.GetType().GetField("Found")?.GetValue(route),
                "The Main Scene route must be backed by connected road cells and its building access anchor.");

            object actualBuildings = worldType.GetProperty("Buildings")?.GetValue(world);
            var actualIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (object building in (System.Collections.IEnumerable)actualBuildings)
            {
                int id = (int)building.GetType().GetField("id")?.GetValue(building);
                actualIds.Add("building:" + id);
            }
            Assert.IsTrue(actualIds.Contains(buildingLocation),
                "A journey destination must resolve to a real TownWorldController building identity.");

            Type populationManagerType = RuntimeType("LandLedgers.Population.PopulationManager");
            Component populationManager = FindComponent(populationManagerType);
            object populationState = populationManagerType.GetProperty("State")?.GetValue(populationManager);
            object householdStates = populationState?.GetType().GetField("households")?.GetValue(populationState);
            Assert.NotNull(householdStates, "Generated households must remain in the authoritative population registry.");
            int housedHouseholds = 0;
            int routableHomes = 0;
            int missingHomeRoutes = 0;
            string sampleHomeLocation = null;
            foreach (object household in (System.Collections.IEnumerable)householdStates)
            {
                int homeBuildingId = (int)household.GetType().GetField("homeBuildingId")?.GetValue(household);
                if (homeBuildingId < 0) continue;
                housedHouseholds++;
                string id = "building:" + homeBuildingId;
                if (actualIds.Contains(id) && townLocations.Contains(id))
                {
                    routableHomes++;
                    sampleHomeLocation ??= id;
                }
                else
                {
                    missingHomeRoutes++;
                }
            }
            Assert.Greater(housedHouseholds, 0, "The New Game must have real household home-building relationships.");
            Assert.AreEqual(0, missingHomeRoutes,
                "Each registered household home must resolve to its actual, road-accessible TownWorld building.");
            Assert.Greater(routableHomes, 0);
            object homeRoute = journeys.GetType().GetMethod("FindRoute")?.Invoke(
                journeys, new[] { (object)sampleHomeLocation, "town-center", foot });
            Assert.IsTrue((bool)homeRoute.GetType().GetField("Found")?.GetValue(homeRoute),
                "A generated household must have a real road route from its home to the town junction.");

            object authoritativeMemberships = populationManagerType.GetProperty("Memberships")?.GetValue(populationManager);
            Assert.NotNull(authoritativeMemberships,
                "PopulationManager must expose the same membership authority used by household life-cycle systems.");
            object boundMemberships = driversType.GetField("householdMemberships",
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(drivers);
            Assert.AreSame(authoritativeMemberships, boundMemberships,
                "The production shopping loop must use PopulationManager's registry, not a parallel membership copy.");

            object shoppingLoop = driversType.GetField("householdShoppingLoop",
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(drivers);
            Assert.NotNull(shoppingLoop,
                "The daily driver must bind household needs to the real purchasing loop even when no seller can fill them.");
            object tripHistory = shoppingLoop.GetType().GetProperty("TripHistory")?.GetValue(shoppingLoop);
            Assert.NotNull(tripHistory);
            Assert.Greater(((System.Collections.ICollection)tripHistory).Count, 0,
                "Shortage needs should reach the real shopping authority and produce an explicit refusal when no stocked seller exists.");

            object needs = hubType.GetProperty("PurchasingNeeds")?.GetValue(hub);
            Assert.NotNull(needs);
            var getOpenNeeds = needs.GetType().GetMethod("GetOpenNeeds");
            int openNeeds = 0;
            foreach (object household in (System.Collections.IEnumerable)householdStates)
            {
                int householdId = (int)household.GetType().GetField("id")?.GetValue(household);
                var householdNeeds = getOpenNeeds?.Invoke(needs, new object[] { householdId })
                    as System.Collections.IEnumerable;
                if (householdNeeds == null) continue;
                foreach (object need in householdNeeds)
                {
                    openNeeds++;
                    var responses = need.GetType().GetField("SuggestedResponses")?.GetValue(need)
                        as System.Collections.IEnumerable;
                    if (responses == null) continue;
                    foreach (object response in responses)
                    {
                        Assert.AreNotEqual("purchase", response as string,
                            "The fresh opening has no formed, stocked, reachable retailer; need guidance must not promise a purchase.");
                    }
                }
            }
            Assert.Greater(openNeeds, 0,
                "The actual daily shortage monitor should preserve unmet needs when the opening has no stocked seller.");
        }

        [UnityTest]
        public IEnumerator EditorialValidationHarnessEnumeratesAndReportsTheAuthoredScenario()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(load, "Main Scene must be present in the build settings.");
            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;
            yield return null;

            Type harnessType = RuntimeType("LandLedgers.Orchestration.Scenarios.Development.ScenarioValidationHarness");
            Component harness = FindComponent(harnessType);
            Assert.NotNull(harness, "Development builds must expose the editorial scenario validation harness.");
            yield return WaitForScenarioRegistration(harness, harnessType);

            object scenarioIds = harnessType.GetProperty("ScenarioIds")?.GetValue(harness);
            Assert.NotNull(scenarioIds, "The harness must read scenario ids from ScenarioDirector.Service.");
            Assert.IsTrue(ContainsEnumerableValue(scenarioIds, "first-ledger"),
                "The authored First Ledger scenario must be discoverable rather than hard-coded into the panel.");

            bool started = (bool)harnessType.GetMethod("StartScenario")?.Invoke(harness, new object[] { "first-ledger" });
            Assert.IsTrue(started, "The harness must start the selected scenario through ScenarioDirector.");
            object reports = harnessType.GetMethod("GetObjectiveReports")?.Invoke(harness, null);
            Assert.NotNull(reports, "The harness must expose live objective reports.");
            Assert.Greater(((System.Collections.ICollection)reports).Count, 0,
                "The harness must enumerate the authored scenario goals/objectives.");

            string formattedEquity = (string)harnessType.GetMethod("FormatCentsForDisplay")?.Invoke(
                null,
                new object[] { 1570757 });
            Assert.AreEqual("$15,707.57 (1,570,757 cents)", formattedEquity,
                "Scenario diagnostics must not present fixed-point cents as whole dollars.");

            Assert.IsFalse(harnessType.GetMethods().Any(method => method.Name.Contains("Complete", StringComparison.OrdinalIgnoreCase)
                || method.Name.Contains("Win", StringComparison.OrdinalIgnoreCase)),
                "Editorial validation must never expose a direct objective-completion or victory operation.");
        }

        [UnityTest]
        public IEnumerator EditorialPredicateProbeReachesVictoryOnlyThroughTheLiveEvaluator()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(load, "Main Scene must be present in the build settings.");
            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;
            yield return null;

            Type harnessType = RuntimeType("LandLedgers.Orchestration.Scenarios.Development.ScenarioValidationHarness");
            Component harness = FindComponent(harnessType);
            Assert.NotNull(harness);
            yield return WaitForScenarioRegistration(harness, harnessType);
            Assert.IsTrue((bool)harnessType.GetMethod("StartScenario")?.Invoke(harness, new object[] { "first-ledger" }));

            Type businessType = RuntimeType("LandLedgers.Economy.BusinessType");
            foreach (string typeName in new[] { "GeneralStore", "CropFarm", "LiveryFreight" })
            {
                object[] arguments = { Enum.Parse(businessType, typeName), $"Editorial {typeName}", null, null };
                bool formed = (bool)harnessType.GetMethod("TryCreateBusiness")?.Invoke(
                    harness,
                    new[] { arguments[0], arguments[1], null, null });
                Assert.IsTrue(formed, $"Editorial probe must use real formation for {typeName}.");
            }

            int probed = (int)harnessType.GetMethod("ProbeAllPlayerBusinessAssets")?.Invoke(harness, new object[] { 1500000 });
            Assert.GreaterOrEqual(probed, 3, "The probe must establish valuation evidence on real player businesses.");
            Assert.IsTrue((bool)harnessType.GetMethod("ReevaluateObjectives")?.Invoke(harness, null),
                "The real First Ledger evaluator must discover the qualifying source state and trigger victory.");

            object reports = harnessType.GetMethod("GetObjectiveReports")?.Invoke(harness, null);
            foreach (object report in (System.Collections.IEnumerable)reports)
            {
                bool complete = (bool)report.GetType().GetProperty("Complete")?.GetValue(report);
                string id = report.GetType().GetProperty("Id")?.GetValue(report) as string;
                if (id != null && id.StartsWith("equity-", StringComparison.Ordinal))
                {
                    Assert.IsTrue(complete, $"Valuation predicate report must be complete for {id}.");
                }
            }
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator FirstLedgerFourWeekProductionReportsHonestProgress()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(load, "Main Scene must be present in the build settings.");
            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;
            yield return null;

            Type harnessType = RuntimeType("LandLedgers.Orchestration.Scenarios.Development.ScenarioValidationHarness");
            Component harness = FindComponent(harnessType);
            Assert.NotNull(harness);
            yield return WaitForScenarioRegistration(harness, harnessType);
            Assert.IsTrue((bool)harnessType.GetMethod("StartScenario")?.Invoke(harness, new object[] { "first-ledger" }));

            Type businessType = RuntimeType("LandLedgers.Economy.BusinessType");
            Type sharedTypeForHiring = RuntimeType("LandLedgers.Economy.SharedBusinessRuntimeManager");
            Component sharedForHiring = FindComponent(sharedTypeForHiring);
            Type portfolioType = RuntimeType("LandLedgers.Economy.PlayerPortfolioManager");
            Component portfolio = FindComponent(portfolioType);
            foreach (string typeName in new[] { "GeneralStore", "CropFarm", "LiveryFreight" })
            {
                object[] arguments = { Enum.Parse(businessType, typeName), $"Normal {typeName}", null, null };
                bool formed = (bool)harnessType.GetMethod("TryCreateBusiness")?.Invoke(harness, arguments);
                Assert.IsTrue(formed, $"Normal production formation must create {typeName}: {arguments[3]}");
                // Ordinary opening capital allocation: the player has the authored
                // starting stake and may choose how much working capital each
                // formed business receives.  The store needs enough legitimate
                // liquidity to keep its ordinary procurement cycle alive while the
                // valuation window matures; this is not a valuation/debug injection.
                int openingCapitalCents = typeName == "GeneralStore" ? 175000 : typeName == "CropFarm" ? 25000 : 50000;
                object[] funding = { arguments[2], openingCapitalCents, 0, null };
                bool funded = (bool)(portfolioType.GetMethod("TryTransferOwnerBusinessCash")?.Invoke(portfolio, funding) ?? false);
                Assert.IsTrue(funded, $"Normal production funding must use the owner-to-business ledger for {typeName}: {funding[3]}");
                if (typeName == "GeneralStore")
                {
                    Type sharedType = sharedTypeForHiring;
                    Component shared = sharedForHiring;
                    object[] premises = { arguments[2], null };
                    Assert.IsTrue((bool)sharedType.GetMethod("TryAssignPlayerBusinessPremises")?.Invoke(shared, premises), premises[1] as string);
                    if (typeName == "GeneralStore")
                    {
                        Type storeType = RuntimeType("LandLedgers.FirstLedger.GeneralStoreRuntimeManager");
                        Component storeRuntime = FindComponent(storeType);
                        object[] bind = { arguments[2], null };
                        Assert.IsTrue((bool)storeType.GetMethod("TryBindFormedPlayerBusiness")?.Invoke(storeRuntime, bind), bind[1] as string);
                        object[] pricing = { 0.50f, null };
                        Assert.IsTrue((bool)storeType.GetMethod("TrySetStoreMarginAdjustment")?.Invoke(storeRuntime, pricing), pricing[1] as string);
                    }
                }

                object[] hire = { arguments[2], 0, null };
                bool hired = (bool)(sharedTypeForHiring.GetMethod("TryAssignCandidateToOpenSlot")?.Invoke(sharedForHiring, hire) ?? false);
                Debug.Log($"[FirstLedgerNormalPath] {typeName}: hire={hired}; message={hire[2]}");

                if (typeName == "LiveryFreight")
                {
                    object[] transportPurchase = { arguments[2], null };
                    bool purchased = (bool)(sharedTypeForHiring.GetMethod("TryAcquireOpeningFreightTransport")?.Invoke(sharedForHiring, transportPurchase) ?? false);
                    Assert.IsTrue(purchased, $"Normal freight formation must acquire the authored opening transport through the production purchase path: {transportPurchase[1]}");
                }
            }

            // Formation, premises, funding, and hiring above are normal production
            // actions. Resolve the live operation boundary, then advance only the
            // authoritative simulation clock. No editorial state or valuation probe
            // is allowed in this normal-path test.
            sharedTypeForHiring.GetMethod("ResolveWeeklySharedOperations")?.Invoke(sharedForHiring, null);
            // Exercise a bounded operating season through the normal calendar path:
            // weekly production, logistics, store receiving, customer demand and
            // valuation evidence all remain authoritative.
            bool previousLogging = Debug.unityLogger.logEnabled;
            Debug.unityLogger.logEnabled = false;
            try
            {
                harnessType.GetMethod("AdvanceHours")?.Invoke(harness, new object[] { 24 * 7 * 4 });
            }
            finally
            {
                Debug.unityLogger.logEnabled = previousLogging;
            }
            yield return null;

            Type bootstrapType = RuntimeType("LandLedgers.Orchestration.Scenarios.FirstLedger.FirstLedgerBootstrap");
            Component bootstrap = FindComponent(bootstrapType);
            int owned = (int)(bootstrapType.GetProperty("PlayerOwnedBusinessCount")?.GetValue(bootstrap) ?? -1);
            int employees = (int)(bootstrapType.GetProperty("ActivePlayerEmployeeCount")?.GetValue(bootstrap) ?? -1);
            int equity = (int)(bootstrapType.GetProperty("PlayerOwnerEquityCents")?.GetValue(bootstrap) ?? -1);
            int employmentRegistryCount = (int)(sharedTypeForHiring.GetProperty("EmploymentRegistryCount")?.GetValue(sharedForHiring) ?? -1);
            bool complete = (bool)(bootstrapType.GetProperty("IsScenarioComplete")?.GetValue(bootstrap) ?? false);
            Assert.GreaterOrEqual(owned, 3,
                $"Four-week production should preserve the three legitimately formed player Businesses. equity={equity}c");
            Assert.GreaterOrEqual(employees, 3,
                $"Four-week production should preserve the three legitimate employee relationships. equity={equity}c");
            Assert.AreEqual(3, employmentRegistryCount,
                "The four-week path must not duplicate the authoritative employment registry.");
            Assert.Less(equity, FirstLedgerEquityTargetCents(),
                "The four-week opening is an honest-progress checkpoint; it must not reach the final equity target without the required economic activity.");
            Assert.IsFalse(complete,
                $"First Ledger must remain incomplete until the real 1,500,000-cent equity objective is reached. " +
                $"owned={owned}, employees={employees}, registry={employmentRegistryCount}, equity={equity}c");
        }

        private static int FirstLedgerEquityTargetCents()
        {
            return 1500000;
        }

        [UnityTest]
        public IEnumerator EditorialVictoryAndScenarioProgressSurviveSaveLoad()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(load, "Main Scene must be present in the build settings.");
            while (!load.isDone) yield return null;
            yield return null;

            Type harnessType = RuntimeType("LandLedgers.Orchestration.Scenarios.Development.ScenarioValidationHarness");
            Component harness = FindComponent(harnessType);
            Assert.NotNull(harness);
            yield return WaitForScenarioRegistration(harness, harnessType);
            Assert.IsTrue((bool)harnessType.GetMethod("StartScenario")?.Invoke(harness, new object[] { "first-ledger" }));

            Type businessType = RuntimeType("LandLedgers.Economy.BusinessType");
            foreach (string typeName in new[] { "GeneralStore", "CropFarm", "LiveryFreight" })
            {
                object[] args = { Enum.Parse(businessType, typeName), $"Save {typeName}", null, null };
                Assert.IsTrue((bool)harnessType.GetMethod("TryCreateBusiness")?.Invoke(harness, args), args[3] as string);
            }

            Assert.GreaterOrEqual((int)(harnessType.GetMethod("ProbeAllPlayerBusinessAssets")?.Invoke(harness, new object[] { 1500000 }) ?? 0), 3);
            Assert.IsTrue((bool)harnessType.GetMethod("ReevaluateObjectives")?.Invoke(harness, null));

            Type saveLoadType = RuntimeType("LandLedgers.Persistence.SaveLoadManager");
            Component saveLoad = FindComponent(saveLoadType);
            Assert.NotNull(saveLoad);
            string path = saveLoadType.GetProperty("DefaultSlotPath")?.GetValue(saveLoad) as string;
            string backupPath = path + ".bak";
            string tempPath = Path.Combine(Application.temporaryCachePath, "land-ledgers-scenario-state-backup.json");
            string tempBackupPath = tempPath + ".bak";
            bool hadSave = File.Exists(path);
            bool hadBackup = File.Exists(backupPath);
            if (hadSave) File.Copy(path, tempPath, true);
            if (hadBackup) File.Copy(backupPath, tempBackupPath, true);

            try
            {
                object[] saveArgs = { null };
                Assert.IsTrue((bool)saveLoadType.GetMethod("SaveDefaultSlot")?.Invoke(saveLoad, saveArgs), saveArgs[0] as string);
                object[] loadArgs = { null };
                Assert.IsTrue((bool)saveLoadType.GetMethod("LoadDefaultSlot")?.Invoke(saveLoad, loadArgs), loadArgs[0] as string);
                yield return null;

                harness = FindComponent(harnessType);
                Assert.AreEqual("first-ledger", harnessType.GetProperty("ActiveScenarioId")?.GetValue(harness));
                bool completeAfterLoad = (bool)harnessType.GetMethod("ReevaluateObjectives")?.Invoke(harness, null);
                Assert.IsTrue(completeAfterLoad, "Reloaded scenario state must remain complete through the live evaluator.");
                Assert.IsTrue(harnessType.GetMethod("GetObjectiveReports")?.Invoke(harness, null) is System.Collections.IEnumerable,
                    "Reloaded scenario must still expose its objective reports.");
            }
            finally
            {
                if (hadSave) File.Copy(tempPath, path, true);
                else if (File.Exists(path)) File.Delete(path);
                if (hadBackup) File.Copy(tempBackupPath, backupPath, true);
                else if (File.Exists(backupPath)) File.Delete(backupPath);
                if (File.Exists(tempPath)) File.Delete(tempPath);
                if (File.Exists(tempBackupPath)) File.Delete(tempBackupPath);
            }

            yield return ReloadMainSceneForTestIsolation();
        }

        [UnityTest]
        public IEnumerator ProductionBusinessAuthorityFormsTheScenarioThreeBusinessRoute()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(load, "Main Scene must be present in the build settings.");
            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;

            Type sharedRuntimeType = RuntimeType("LandLedgers.Economy.SharedBusinessRuntimeManager");
            Type businessType = RuntimeType("LandLedgers.Economy.BusinessType");
            Component sharedRuntime = FindComponent(sharedRuntimeType);
            Assert.NotNull(sharedRuntime, "The playable scene must own the shared business authority.");
            Type formedStoreRuntimeType = RuntimeType("LandLedgers.FirstLedger.GeneralStoreRuntimeManager");
            Component formedStoreRuntime = FindComponent(formedStoreRuntimeType);
            Assert.NotNull(formedStoreRuntime, "The scene must own the household retail runtime.");
            object registeredStorePort = formedStoreRuntimeType.GetMethod("BuildHouseholdTradingPort")
                .Invoke(formedStoreRuntime, null);
            Assert.NotNull(registeredStorePort);
            Assert.AreEqual("general-store", registeredStorePort.GetType().GetProperty("StoreBusinessId")?.GetValue(registeredStorePort),
                "Before formation, the registered port is an unbound placeholder and must have no identity or stock to sell.");
            Assert.AreEqual(0, (int)registeredStorePort.GetType().GetMethod("CategoryStockUnits")
                .Invoke(registeredStorePort, new object[] { "staple_food" }));
            object declaredOffers = registeredStorePort.GetType().GetProperty("CategoryOffers")?.GetValue(registeredStorePort);
            Assert.NotNull(declaredOffers);
            Assert.Greater(((System.Collections.ICollection)declaredOffers).Count, 0,
                "The port should declare authored offers before formation while keeping stock at zero.");
            Type generalStoreAdapterType = RuntimeType("LandLedgers.Economy.Businesses.GeneralStore.GeneralStoreSupplierAdapter");
            object supplierAdapter = Activator.CreateInstance(generalStoreAdapterType, registeredStorePort);
            Assert.IsTrue((bool)generalStoreAdapterType.GetMethod("OffersItem").Invoke(supplierAdapter, new object[] { "flour" }),
                "The live adapter must resolve the authored staple offer even when no Business has been formed yet.");
            Assert.AreEqual(0, (int)generalStoreAdapterType.GetMethod("ItemStockUnits")
                .Invoke(supplierAdapter, new object[] { "flour" }), "An unformed store still has no saleable inventory.");
            object beforeFormationCapability = registeredStorePort.GetType().GetProperty("TradingCapability")
                ?.GetValue(registeredStorePort);
            Assert.IsFalse((bool)beforeFormationCapability?.GetType().GetProperty("Staffed")?.GetValue(beforeFormationCapability),
                "A pre-registered supplier must not be treated as open before a real store and clerk exist.");

            var created = new List<string>();
            foreach (string typeName in new[] { "GeneralStore", "CropFarm", "LiveryFreight" })
            {
                object enumValue = Enum.Parse(businessType, typeName);
                object[] arguments = { enumValue, $"Player {typeName}", null, null };
                bool success = (bool)sharedRuntimeType.GetMethod("TryCreatePlayerBusiness")
                    .Invoke(sharedRuntime, arguments);
                Assert.IsTrue(success, $"Production formation must create {typeName}: {arguments[3]}");

                object formedBusiness = arguments[2];
                Assert.NotNull(formedBusiness, $"Formation must return the {typeName} entity.");
                if (typeName == "GeneralStore")
                {
                    object[] premisesArguments = { formedBusiness, null };
                    Assert.IsTrue((bool)sharedRuntimeType.GetMethod("TryAssignPlayerBusinessPremises")
                        .Invoke(sharedRuntime, premisesArguments), premisesArguments[1] as string);

                    object[] hireArguments = { formedBusiness, 0, null };
                    Assert.IsTrue((bool)sharedRuntimeType.GetMethod("TryAssignCandidateToOpenSlot")
                        .Invoke(sharedRuntime, hireArguments), hireArguments[2] as string);

                    object[] bindingArguments = { formedBusiness, null };
                    bool bound = (bool)formedStoreRuntimeType.GetMethod("TryBindFormedPlayerBusiness")
                        .Invoke(formedStoreRuntime, bindingArguments);
                    Assert.IsTrue(bound, $"The formed General Store must bind to its operating runtime: {bindingArguments[1]}");

                    string businessId = formedBusiness.GetType().GetProperty("InstanceId")?.GetValue(formedBusiness) as string;
                    Assert.AreEqual(businessId, registeredStorePort.GetType().GetProperty("StoreBusinessId")?.GetValue(registeredStorePort),
                        "The originally registered port must follow the formed Business, rather than remaining the startup placeholder.");
                    Assert.Greater((int)registeredStorePort.GetType().GetMethod("CategoryStockUnits")
                        .Invoke(registeredStorePort, new object[] { "staple_food" }), 0,
                        "The adapter must expose only the formed store's actual runtime stock.");
                    Assert.Greater((int)generalStoreAdapterType.GetMethod("ItemStockUnits")
                        .Invoke(supplierAdapter, new object[] { "flour" }), 0,
                        "The already-registered supplier adapter must observe formed store inventory.");
                    Assert.Greater((int)generalStoreAdapterType.GetMethod("ItemPricePerUnitCents")
                        .Invoke(supplierAdapter, new object[] { "flour" }), 0,
                        "A sale requires a positive price from the live store authority.");
                    object afterHireCapability = registeredStorePort.GetType().GetProperty("TradingCapability")
                        ?.GetValue(registeredStorePort);
                    Assert.IsTrue((bool)afterHireCapability?.GetType().GetProperty("Staffed")?.GetValue(afterHireCapability),
                        "Trading capability must refresh after a real clerk is assigned.");
                }
                string instanceId = formedBusiness.GetType().GetProperty("InstanceId")?.GetValue(formedBusiness) as string;
                created.Add(instanceId);
            }

            Assert.AreEqual(3, created.Count);
            Assert.IsTrue(created.TrueForAll(id => !string.IsNullOrWhiteSpace(id)),
                "Every formed business must have a persistent instance id.");

            Type storeRuntimeType = RuntimeType("LandLedgers.FirstLedger.GeneralStoreRuntimeManager");
            Component storeRuntime = FindComponent(storeRuntimeType);
            Assert.NotNull(storeRuntime, "The scene must own the real General Store runtime.");
            object currentStore = storeRuntimeType.GetProperty("CurrentBusiness")?.GetValue(storeRuntime);
            Assert.NotNull(currentStore, "The formed General Store must be bound to its operating runtime.");
            Assert.AreEqual(created[0], currentStore.GetType().GetProperty("InstanceId")?.GetValue(currentStore));
        }

        [UnityTest]
        public IEnumerator OpeningWorldContainsOwnedHorseAndWagonAuthorities()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(load, "Main Scene must be present in the build settings.");
            while (!load.isDone) yield return null;
            yield return null;

            Type hubType = RuntimeType("LandLedgers.Orchestration.Systems.SimulationSystemsHub");
            Component hub = FindComponent(hubType);
            Assert.NotNull(hub, "The opening world must own the shared systems hub.");

            object animals = hubType.GetProperty("Animals")?.GetValue(hub);
            Assert.NotNull(animals, "Opening animals must use the shared AnimalRegistry.");
            object activeAnimals = animals.GetType().GetProperty("ActiveAnimals")?.GetValue(animals);
            bool foundHorse = false;
            foreach (object animal in (System.Collections.IEnumerable)activeAnimals)
            {
                object species = animal?.GetType().GetProperty("Species")?.GetValue(animal)
                    ?? animal?.GetType().GetField("Species")?.GetValue(animal);
                if (species?.ToString() == "Horse")
                {
                    foundHorse = true;
                    break;
                }
            }
            Assert.IsTrue(foundHorse, "A fresh authored opening must contain a real Horse AnimalState.");

            object transport = hubType.GetProperty("TransportAssets")?.GetValue(hub);
            object wagons = transport?.GetType().GetProperty("Assets")?.GetValue(transport);
            Assert.NotNull(wagons, "Opening transport must use the persistent transport asset registry.");
            Assert.Greater(((System.Collections.ICollection)wagons).Count, 0,
                "A fresh authored opening must contain a real Wagon EquipmentAsset.");
        }

        [UnityTest]
        public IEnumerator ProductionWeeklyPassRegistersFarmFieldAndRespectsWinter()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(load, "Main Scene must be present in the build settings.");
            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;

            Type sharedRuntimeType = RuntimeType("LandLedgers.Economy.SharedBusinessRuntimeManager");
            Type businessType = RuntimeType("LandLedgers.Economy.BusinessType");
            Component sharedRuntime = FindComponent(sharedRuntimeType);
            Assert.NotNull(sharedRuntime, "The playable scene must own the shared business authority.");

            object farmType = Enum.Parse(businessType, "CropFarm");
            object freightType = Enum.Parse(businessType, "LiveryFreight");
            object storeType = Enum.Parse(businessType, "GeneralStore");
            object farm = FindExistingBusiness(sharedRuntime, sharedRuntimeType, farmType);
            object freight = CreateBusiness(sharedRuntime, sharedRuntimeType, freightType, "Route Freight");
            object store = CreateBusiness(sharedRuntime, sharedRuntimeType, storeType, "Route Store");
            Assert.NotNull(farm);
            Assert.NotNull(freight);
            Assert.NotNull(store);
            object runtimeState = farm.GetType().GetProperty("RuntimeState")?.GetValue(farm);
            Assert.NotNull(runtimeState, "The farm must expose its authoritative runtime state.");
            object cropStock = runtimeState.GetType().GetMethod("GetCategoryStock")?.Invoke(runtimeState, new object[] { "crop_food" });
            Assert.NotNull(cropStock, "Farm production must use the authored crop output category.");
            Assert.AreEqual(0, (int)cropStock.GetType().GetProperty("CurrentStockUnits")?.GetValue(cropStock),
                "A newly formed farm must not begin with unproven crop output before planting or harvest.");
            object[] storePremisesArguments = { store, null };
            bool storePremisesAssigned = (bool)sharedRuntimeType.GetMethod("TryAssignPlayerBusinessPremises")
                .Invoke(sharedRuntime, storePremisesArguments);
            Assert.IsTrue(storePremisesAssigned, $"The formed store must establish compatible premises before receiving goods: {storePremisesArguments[1]}");

            object[] hireArguments = { freight, 0, null };
            bool hired = (bool)sharedRuntimeType.GetMethod("TryAssignCandidateToOpenSlot")
                .Invoke(sharedRuntime, hireArguments);
            Assert.IsTrue(hired, $"The route must hire a real freight worker through the production staffing authority: {hireArguments[2]}");

            Type storeRuntimeType = RuntimeType("LandLedgers.FirstLedger.GeneralStoreRuntimeManager");
            Component storeRuntime = FindComponent(storeRuntimeType);
            object[] bindArguments = { store, null };
            bool bound = (bool)storeRuntimeType.GetMethod("TryBindFormedPlayerBusiness")
                .Invoke(storeRuntime, bindArguments);
            Assert.IsTrue(bound, $"The formed store must bind before commerce: {bindArguments[1]}");

            sharedRuntimeType.GetMethod("ResolveWeeklySharedOperations")?.Invoke(sharedRuntime, null);

            int farmOutput = (int)cropStock.GetType().GetProperty("CurrentStockUnits")?.GetValue(cropStock);
            Assert.AreEqual(0, farmOutput,
                "The January opening must not create crop inventory before a valid planting and harvest cycle.");

            Type hubType = RuntimeType("LandLedgers.Orchestration.Systems.SimulationSystemsHub");
            Component hub = FindComponent(hubType);
            Assert.NotNull(hub, "The playable scene must own the authoritative systems hub.");
            object farmSlice = hubType.GetProperty("FarmSlice")?.GetValue(hub);
            object crops = farmSlice?.GetType().GetProperty("Crops")?.GetValue(farmSlice);
            object fields = crops?.GetType().GetProperty("Fields")?.GetValue(crops);
            object allFields = fields?.GetType().GetMethod("AllFields")?.Invoke(fields, null);
            object registeredField = null;
            foreach (object candidate in (System.Collections.IEnumerable)allFields)
            {
                string candidateFarmId = candidate.GetType().GetField("FarmId")?.GetValue(candidate) as string;
                if (string.Equals(candidateFarmId,
                    farm.GetType().GetProperty("InstanceId")?.GetValue(farm) as string,
                    StringComparison.OrdinalIgnoreCase))
                {
                    registeredField = candidate;
                    break;
                }
            }
            Assert.NotNull(registeredField,
                "A formed farm with legitimate agricultural premises must register a persistent CropFieldState.");
            Assert.Greater((float)registeredField.GetType().GetField("Acres")?.GetValue(registeredField), 0f);
            Assert.IsFalse(string.IsNullOrWhiteSpace(
                registeredField.GetType().GetField("LinkedPlotName")?.GetValue(registeredField) as string));

            Type logisticsType = RuntimeType("LandLedgers.Economy.LogisticsRuntimeManager");
            Component logistics = FindComponent(logisticsType);
            Assert.NotNull(logistics, "The playable scene must own the real logistics authority.");
            object shipments = logisticsType.GetProperty("Shipments")?.GetValue(logistics);
            Assert.NotNull(shipments, "The logistics authority must expose scheduled physical shipments.");
            string farmId = farm.GetType().GetProperty("InstanceId")?.GetValue(farm) as string;
            string storeId = store.GetType().GetProperty("InstanceId")?.GetValue(store) as string;
            object shipment = null;
            foreach (object candidate in (System.Collections.IEnumerable)shipments)
            {
                string sourceId = candidate.GetType().GetField("sourceBusinessInstanceId")?.GetValue(candidate) as string;
                string destinationId = candidate.GetType().GetField("destinationBusinessInstanceId")?.GetValue(candidate) as string;
                if (string.Equals(sourceId, farmId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(destinationId, storeId, StringComparison.OrdinalIgnoreCase))
                {
                    shipment = candidate;
                    break;
                }
            }
            Assert.IsNull(shipment,
                "No farm shipment may be created before real crop output and a legitimate buyer order exist.");
        }

        [UnityTest]
        public IEnumerator GeneralStoreDailySaleKeepsRealHouseholdAndPersonProvenance()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(load, "Main Scene must be present in the build settings.");
            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;

            Type sharedRuntimeType = RuntimeType("LandLedgers.Economy.SharedBusinessRuntimeManager");
            Type businessType = RuntimeType("LandLedgers.Economy.BusinessType");
            Component sharedRuntime = FindComponent(sharedRuntimeType);
            Component storeRuntime = FindComponent(RuntimeType("LandLedgers.FirstLedger.GeneralStoreRuntimeManager"));
            Assert.NotNull(sharedRuntime);
            Assert.NotNull(storeRuntime);

            object[] createArguments = { Enum.Parse(businessType, "GeneralStore"), "Customer Ledger Store", null, null };
            Assert.IsTrue((bool)sharedRuntimeType.GetMethod("TryCreatePlayerBusiness")
                .Invoke(sharedRuntime, createArguments), createArguments[3] as string);

            object[] premisesArguments = { createArguments[2], null };
            Assert.IsTrue((bool)sharedRuntimeType.GetMethod("TryAssignPlayerBusinessPremises")
                .Invoke(sharedRuntime, premisesArguments), premisesArguments[1] as string);

            object[] hireArguments = { createArguments[2], 0, null };
            Assert.IsTrue((bool)sharedRuntimeType.GetMethod("TryAssignCandidateToOpenSlot")
                .Invoke(sharedRuntime, hireArguments), hireArguments[2] as string);

            Type storeRuntimeType = storeRuntime.GetType();
            object[] bindArguments = { createArguments[2], null };
            Assert.IsTrue((bool)storeRuntimeType.GetMethod("TryBindFormedPlayerBusiness")
                .Invoke(storeRuntime, bindArguments), bindArguments[1] as string);

            storeRuntimeType.GetMethod("ResolveDailySales")?.Invoke(storeRuntime, null);

            int customers = (int)storeRuntimeType.GetProperty("LastDailyCustomerHouseholds")?.GetValue(storeRuntime);
            Assert.Greater(customers, 0, "A stocked operating store must serve an authored household customer.");
            Assert.GreaterOrEqual((int)storeRuntimeType.GetProperty("LastCustomerHouseholdId")?.GetValue(storeRuntime), 0);
            Assert.GreaterOrEqual((int)storeRuntimeType.GetProperty("LastCustomerPersonId")?.GetValue(storeRuntime), 0,
                "A household sale must retain the real acting adult Person, not only an anonymous household count.");
            Assert.Greater((int)storeRuntimeType.GetProperty("LastCustomerSaleUnits")?.GetValue(storeRuntime), 0);
            Assert.Greater((int)storeRuntimeType.GetProperty("LastCustomerSaleRevenueCents")?.GetValue(storeRuntime), 0);
        }

        // Save/load intentionally runs last: it restores a full campaign snapshot and
        // therefore must not leave a loaded-world lifecycle for unrelated startup tests.
        [UnityTest, Order(100000), Timeout(900000)]
        public IEnumerator CampaignSaveRoundTripPreservesProductionBusinessIdentity()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(load, "Main Scene must be present in the build settings.");
            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;

            Type saveLoadType = RuntimeType("LandLedgers.Persistence.SaveLoadManager");
            Component saveLoad = FindComponent(saveLoadType);
            Assert.NotNull(saveLoad, "The playable scene must own the production save/load authority.");
            Type sharedRuntimeType = RuntimeType("LandLedgers.Economy.SharedBusinessRuntimeManager");
            Component sharedRuntime = FindComponent(sharedRuntimeType);
            object businesses = sharedRuntimeType.GetProperty("Businesses")?.GetValue(sharedRuntime);
            var idsBefore = new List<string>();
            foreach (object business in (System.Collections.IEnumerable)businesses)
            {
                string id = business?.GetType().GetProperty("InstanceId")?.GetValue(business) as string;
                if (!string.IsNullOrWhiteSpace(id)) idsBefore.Add(id);
            }
            Component storeRuntime = FindComponent(RuntimeType("LandLedgers.FirstLedger.GeneralStoreRuntimeManager"));
            object currentStore = storeRuntime?.GetType().GetProperty("CurrentBusiness")?.GetValue(storeRuntime);
            string currentStoreId = currentStore?.GetType().GetProperty("InstanceId")?.GetValue(currentStore) as string;
            if (!string.IsNullOrWhiteSpace(currentStoreId) && !idsBefore.Contains(currentStoreId)) idsBefore.Add(currentStoreId);
            Assert.IsNotEmpty(idsBefore, "The authored opening state must contain a real business before saving.");

            string path = saveLoadType.GetProperty("DefaultSlotPath")?.GetValue(saveLoad) as string;
            string backupPath = path + ".bak";
            string tempPath = Path.Combine(Application.temporaryCachePath, "land-ledgers-save-roundtrip-backup.json");
            string tempBackupPath = tempPath + ".bak";
            bool hadSave = File.Exists(path);
            bool hadBackup = File.Exists(backupPath);
            if (hadSave) File.Copy(path, tempPath, true);
            if (hadBackup) File.Copy(backupPath, tempBackupPath, true);

            try
            {
                object[] saveArgs = { null };
                bool saved = (bool)saveLoadType.GetMethod("SaveDefaultSlot")?.Invoke(saveLoad, saveArgs);
                Assert.IsTrue(saved, saveArgs[0] as string);
                object[] loadArgs = { null };
                bool loaded = (bool)saveLoadType.GetMethod("LoadDefaultSlot")?.Invoke(saveLoad, loadArgs);
                Assert.IsTrue(loaded, loadArgs[0] as string);
                yield return null;

                sharedRuntime = FindComponent(sharedRuntimeType);
                businesses = sharedRuntimeType.GetProperty("Businesses")?.GetValue(sharedRuntime);
                var idsAfter = new HashSet<string>();
                foreach (object business in (System.Collections.IEnumerable)businesses)
                {
                    string id = business?.GetType().GetProperty("InstanceId")?.GetValue(business) as string;
                    if (!string.IsNullOrWhiteSpace(id)) idsAfter.Add(id);
                }
                storeRuntime = FindComponent(RuntimeType("LandLedgers.FirstLedger.GeneralStoreRuntimeManager"));
                currentStore = storeRuntime?.GetType().GetProperty("CurrentBusiness")?.GetValue(storeRuntime);
                currentStoreId = currentStore?.GetType().GetProperty("InstanceId")?.GetValue(currentStore) as string;
                if (!string.IsNullOrWhiteSpace(currentStoreId)) idsAfter.Add(currentStoreId);
                foreach (string id in idsBefore)
                    Assert.IsTrue(idsAfter.Contains(id), $"Save/load must preserve business identity {id}.");
            }
            finally
            {
                if (hadSave) File.Copy(tempPath, path, true);
                else if (File.Exists(path)) File.Delete(path);
                if (hadBackup) File.Copy(tempBackupPath, backupPath, true);
                else if (File.Exists(backupPath)) File.Delete(backupPath);
                if (File.Exists(tempPath)) File.Delete(tempPath);
                if (File.Exists(tempBackupPath)) File.Delete(tempBackupPath);
            }

            yield return ReloadMainSceneForTestIsolation();
        }

        private static object CreateBusiness(Component sharedRuntime, Type sharedRuntimeType, object businessType, string name)
        {
            object[] arguments = { businessType, name, null, null };
            bool success = (bool)sharedRuntimeType.GetMethod("TryCreatePlayerBusiness")
                .Invoke(sharedRuntime, arguments);
            Assert.IsTrue(success, $"Production formation must create {name}: {arguments[3]}");
            return arguments[2];
        }

        private static IEnumerator WaitForScenarioRegistration(Component harness, Type harnessType)
        {
            // Full fixture runs may follow a save/restore scene and require the
            // complete authored-town bootstrap, not merely one rendered frame.
            for (int frame = 0; frame < 5000; frame++)
            {
                object ids = harnessType.GetProperty("ScenarioIds")?.GetValue(harness);
                if (ids is System.Collections.IEnumerable enumerable)
                {
                    foreach (object id in enumerable)
                    {
                        if (string.Equals(id as string, "first-ledger", StringComparison.Ordinal))
                        {
                            yield break;
                        }
                    }
                }

                yield return null;
            }

            Assert.Fail("The editorial harness did not observe the authored First Ledger scenario registration.");
        }

        private static IEnumerator ReloadMainSceneForTestIsolation()
        {
            AsyncOperation reload = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
            Assert.NotNull(reload, "Main Scene must remain available for PlayMode test isolation.");
            while (!reload.isDone)
            {
                yield return null;
            }

            yield return null;
        }

        private static object FindExistingBusiness(Component sharedRuntime, Type sharedRuntimeType, object wantedType)
        {
            object businesses = sharedRuntimeType.GetProperty("Businesses")?.GetValue(sharedRuntime);
            foreach (object candidate in (System.Collections.IEnumerable)businesses)
            {
                if (candidate.GetType().GetProperty("BusinessType")?.GetValue(candidate)?.Equals(wantedType) == true)
                {
                    return candidate;
                }
            }

            Assert.Fail($"The authored opening world must contain a real {wantedType} business for the production route.");
            return null;
        }

        private static Type RuntimeType(string fullName)
        {
            return Type.GetType(fullName + ", Assembly-CSharp")
                ?? Type.GetType(fullName);
        }

        private static bool ContainsEnumerableValue(object values, string expected)
        {
            foreach (object value in (System.Collections.IEnumerable)values)
            {
                if (string.Equals(value as string, expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static Component FindComponent(Type componentType)
        {
            Assert.NotNull(componentType, "The production runtime type must be loadable.");
            MonoBehaviour[] components = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < components.Length; i++)
            {
                if (componentType.IsInstanceOfType(components[i]))
                {
                    return components[i];
                }
            }

            return null;
        }
    }
}
