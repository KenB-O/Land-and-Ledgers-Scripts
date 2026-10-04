using System;
using System.Collections;
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
