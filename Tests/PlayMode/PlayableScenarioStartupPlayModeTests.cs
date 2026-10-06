using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            Assert.NotNull(FindComponent(startPanelType), "Main Scene must expose the authored scenario start panel.");
            Assert.NotNull(FindComponent(bootstrapperType), "Main Scene must own the real world bootstrapper.");

            Component director = FindComponent(directorType);
            Assert.NotNull(director, "Main Scene must own the real ScenarioDirector.");

            object started = directorType.GetMethod("SwitchScenario")?.Invoke(director, new object[] { "first-ledger" });
            Assert.IsTrue(started is bool && (bool)started,
                "The authored First Ledger scenario must be registered and startable through the runtime director.");
            Assert.AreEqual("first-ledger", directorType.GetProperty("ActiveScenarioId")?.GetValue(director));

            yield return null;
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
        public IEnumerator FirstLedgerNormalProductionReachesTheLiveLadder()
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
            Assert.IsTrue(complete,
                $"The first authored scenario must reach Victory through ordinary production and authoritative time, not a completion flag, manual reevaluation, or valuation probe. " +
                $"owned={owned}, employees={employees}, registry={employmentRegistryCount}, equity={equity}c");
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
                    Type formedStoreRuntimeType = RuntimeType("LandLedgers.FirstLedger.GeneralStoreRuntimeManager");
                    Component formedStoreRuntime = FindComponent(formedStoreRuntimeType);
                    Assert.NotNull(formedStoreRuntime, "The scene must own the real General Store runtime.");
                    object[] bindingArguments = { formedBusiness, null };
                    bool bound = (bool)formedStoreRuntimeType.GetMethod("TryBindFormedPlayerBusiness")
                        .Invoke(formedStoreRuntime, bindingArguments);
                    Assert.IsTrue(bound, $"The formed General Store must bind to its operating runtime: {bindingArguments[1]}");
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
        public IEnumerator ProductionWeeklyPassProducesFarmOutputAndQueuesRealLogistics()
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

            object runtimeState = farm.GetType().GetProperty("RuntimeState")?.GetValue(farm);
            Assert.NotNull(runtimeState, "The farm must expose its authoritative runtime state.");
            object cropStock = runtimeState.GetType().GetMethod("GetCategoryStock")?.Invoke(runtimeState, new object[] { "crop_food" });
            Assert.NotNull(cropStock, "Farm production must use the authored crop output category.");
            int farmOutput = (int)cropStock.GetType().GetProperty("CurrentStockUnits")?.GetValue(cropStock);
            Assert.Greater(farmOutput, 0, "A staffed formed farm must produce physical crop inventory.");

            Type logisticsType = RuntimeType("LandLedgers.Economy.LogisticsRuntimeManager");
            Component logistics = FindComponent(logisticsType);
            Assert.NotNull(logistics, "The playable scene must own the real logistics authority.");
            object shipments = logisticsType.GetProperty("Shipments")?.GetValue(logistics);
            Assert.NotNull(shipments, "The logistics authority must expose scheduled physical shipments.");
            Assert.Greater(((System.Collections.ICollection)shipments).Count, 0,
                "Farm output must enter the real shipment ledger before store receipt.");
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
            Assert.NotNull(shipment, "Farm output must queue a shipment addressed to the formed General Store.");
            Assert.AreEqual("HiredFreight", shipment.GetType().GetProperty("HaulingMode")?.GetValue(shipment)?.ToString(),
                "The route must use the formed Freight business when one is available.");
            string carrierId = shipment.GetType().GetProperty("CarrierBusinessInstanceId")?.GetValue(shipment) as string;
            string freightId = freight.GetType().GetProperty("InstanceId")?.GetValue(freight) as string;
            Assert.AreEqual(freightId, carrierId, "The shipment must name the real Freight business as carrier.");
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
        [UnityTest, Order(100000)]
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
