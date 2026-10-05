using System;
using System.Collections;
using System.Collections.Generic;
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
            object freightRuntimeState = freight.GetType().GetProperty("RuntimeState")?.GetValue(freight);

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

        private static object CreateBusiness(Component sharedRuntime, Type sharedRuntimeType, object businessType, string name)
        {
            object[] arguments = { businessType, name, null, null };
            bool success = (bool)sharedRuntimeType.GetMethod("TryCreatePlayerBusiness")
                .Invoke(sharedRuntime, arguments);
            Assert.IsTrue(success, $"Production formation must create {name}: {arguments[3]}");
            return arguments[2];
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
