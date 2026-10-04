using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Orchestration.Scenarios;
using LandLedgers.Orchestration.Scenarios.FirstLedger;
using LandLedgers.Orchestration.Systems;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.EditorTests.Scenarios
{
    /// <summary>
    /// P7 scenario-progression journey: live goal completions produced by the
    /// scenario evaluators must reach the ScenarioDirector's runtime state
    /// (the puppet master panel reads ScenarioRuntimeState, not the
    /// evaluators — before P7 the panel showed every goal incomplete forever),
    /// and the SimulationDrivers scenario tick must be multicast so two
    /// scenario bootstraps in one scene cannot silently steal each other's
    /// evaluations.
    /// </summary>
    [TestFixture]
    public sealed class ScenarioGoalForwardingTests
    {
        private GameObject directorObject;
        private ScenarioDirector director;

        [SetUp]
        public void SetUp()
        {
            directorObject = new GameObject("TestScenarioDirector");
            director = directorObject.AddComponent<ScenarioDirector>();
        }

        [TearDown]
        public void TearDown()
        {
            if (directorObject != null)
            {
                UnityEngine.Object.DestroyImmediate(directorObject);
                directorObject = null;
                director = null;
            }
        }

        private static ScenarioAsset BuildAsset(string scenarioId, params string[] goalIds)
        {
            var asset = ScriptableObject.CreateInstance<ScenarioAsset>();
            var so = new SerializedObject(asset);
            so.FindProperty("scenarioId").stringValue = scenarioId;
            so.FindProperty("displayName").stringValue = "Test " + scenarioId;
            SerializedProperty goalsProp = so.FindProperty("goals");
            goalsProp.arraySize = goalIds.Length;
            for (int i = 0; i < goalIds.Length; i++)
            {
                SerializedProperty el = goalsProp.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("goalId").stringValue = goalIds[i];
                el.FindPropertyRelative("text").stringValue = "Goal " + goalIds[i];
            }

            so.ApplyModifiedProperties();
            return asset;
        }

        private void BeginFirstLedger()
        {
            Assert.IsTrue(
                director.Service.RegisterScenario(
                    BuildAsset(FirstLedgerScenario.ScenarioId, FirstLedgerScenario.GoalIdsInOrder),
                    out string registerReason),
                registerReason);
            Assert.IsTrue(
                director.Service.BeginScenario(FirstLedgerScenario.ScenarioId, out string beginReason),
                beginReason);
        }

        [Test]
        public void RecordLiveGoalCompletion_ForwardsToActiveMatchingScenario()
        {
            BeginFirstLedger();

            Assert.IsTrue(director.RecordLiveGoalCompletion(
                FirstLedgerScenario.ScenarioId, "acquire-first-business"));
            Assert.IsTrue(director.Service.ActiveState.IsGoalCompleted("acquire-first-business"));
            // Other goals are untouched.
            Assert.IsFalse(director.Service.ActiveState.IsGoalCompleted("hire-first-employee"));
        }

        [Test]
        public void RecordLiveGoalCompletion_IgnoresWhenNoActiveScenario()
        {
            Assert.IsFalse(director.RecordLiveGoalCompletion(
                FirstLedgerScenario.ScenarioId, "acquire-first-business"));
        }

        [Test]
        public void RecordLiveGoalCompletion_IgnoresMismatchedScenarioId()
        {
            BeginFirstLedger();

            Assert.IsFalse(director.RecordLiveGoalCompletion(
                "saloon-circuit", "acquire-first-business"));
            Assert.IsFalse(director.Service.ActiveState.IsGoalCompleted("acquire-first-business"));
        }

        [Test]
        public void RecordLiveGoalCompletion_RejectsUnknownGoalIdQuietly()
        {
            BeginFirstLedger();

            Assert.IsFalse(director.RecordLiveGoalCompletion(
                FirstLedgerScenario.ScenarioId, "not-a-real-goal"));
        }

        [Test]
        public void RecordLiveGoalCompletion_RejectsBlankInputs()
        {
            BeginFirstLedger();

            Assert.IsFalse(director.RecordLiveGoalCompletion(null, "acquire-first-business"));
            Assert.IsFalse(director.RecordLiveGoalCompletion(FirstLedgerScenario.ScenarioId, ""));
            Assert.IsFalse(director.RecordLiveGoalCompletion(null, null));
        }

        private sealed class StubGameState : IFirstLedgerGameState
        {
            public int OwnedBusinesses;
            public int ActiveEmployees;
            public int OwnerEquityCents;

            public int PlayerOwnedBusinessCount => OwnedBusinesses;
            public int ActivePlayerEmployeeCount => ActiveEmployees;
            public int PlayerOwnerEquityCents => OwnerEquityCents;
        }

        [Test]
        public void EvaluatorCompletion_ReachesRuntimeState_EndToEnd()
        {
            BeginFirstLedger();

            var evaluator = new FirstLedgerGoalEvaluator();
            var state = new StubGameState { OwnedBusinesses = 1 };
            List<string> newlyCompleted = evaluator.Evaluate(state);

            Assert.Contains("acquire-first-business", newlyCompleted);
            foreach (string goalId in newlyCompleted)
            {
                Assert.IsTrue(
                    director.RecordLiveGoalCompletion(FirstLedgerScenario.ScenarioId, goalId),
                    "forwarding failed for " + goalId);
            }

            Assert.IsTrue(director.Service.ActiveState.IsGoalCompleted("acquire-first-business"));

            // Re-evaluation does not re-fire, and re-forwarding is idempotent.
            Assert.AreEqual(0, evaluator.Evaluate(state).Count);
            Assert.IsTrue(director.RecordLiveGoalCompletion(
                FirstLedgerScenario.ScenarioId, "acquire-first-business"));
        }

        [Test]
        public void OnScenarioTick_IsMulticastAcrossBootstraps()
        {
            var driversObject = new GameObject("TestSimulationDrivers");
            try
            {
                var drivers = driversObject.AddComponent<SimulationDrivers>();
                int firstCalls = 0;
                int secondCalls = 0;
                Action first = () => firstCalls++;
                Action second = () => secondCalls++;
                drivers.OnScenarioTick += first;
                drivers.OnScenarioTick += second;

                Action tick = ReadOnScenarioTick(drivers);
                Assert.IsNotNull(tick, "OnScenarioTick backing field not found.");
                Assert.AreEqual(2, tick.GetInvocationList().Length,
                    "Both subscribers must be retained — last-writer-wins would drop one.");

                tick();

                Assert.AreEqual(1, firstCalls);
                Assert.AreEqual(1, secondCalls);

                // Unsubscribe removes only its own handler.
                drivers.OnScenarioTick -= first;
                Action remaining = ReadOnScenarioTick(drivers);
                Assert.AreEqual(1, remaining.GetInvocationList().Length);
                remaining();
                Assert.AreEqual(1, firstCalls);
                Assert.AreEqual(2, secondCalls);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(driversObject);
            }
        }

        private static Action ReadOnScenarioTick(SimulationDrivers drivers)
        {
            FieldInfo field = typeof(SimulationDrivers).GetField(
                "OnScenarioTick", BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null ? (Action)field.GetValue(drivers) : null;
        }
    }
}
