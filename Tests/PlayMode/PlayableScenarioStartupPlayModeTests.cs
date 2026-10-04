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
