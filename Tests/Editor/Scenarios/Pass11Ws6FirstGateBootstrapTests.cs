using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Orchestration.Scenarios;
using LandLedgers.Orchestration.Scenarios.SaloonCircuit;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Scenarios
{
    /// <summary>
    /// Pass XI WS6 first-gate bootstrap tests — Unity Test Runner only.
    /// These exercise the bootstrap MonoBehaviours themselves, which
    /// cannot compile in the Roslyn harness (SaloonCircuitBootstrap pulls
    /// SimulationSystemsHub / SharedBusinessRuntimeManager, excluded from
    /// the harness subset, and the shim lacks FindAnyObjectByType). The
    /// pure data flow they drive is covered in the harness by
    /// Pass11Ws6FirstGateSeamTests (Tests/Editor/Orchestration).
    ///
    /// M1: a Saloon load with NO First Ledger bootstrap anywhere in the
    /// scene still restores the director, the evaluator, and the saved
    /// goals — the director had no active scenario before the Saloon
    /// restore ran, so nobody else could have restored it.
    /// M2: a Saloon bootstrap awakening AFTER the first start (director
    /// already active, hub service already tracking the scenario)
    /// registers its qualifiers on Awake, idempotently.
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws6FirstGateBootstrapTests
    {
        private GameObject directorObject;
        private ScenarioDirector director;
        private GameObject hubObject;
        private SimulationSystemsHub hub;
        private GameObject bootstrapObject;
        private SaloonCircuitBootstrap bootstrap;

        [TearDown]
        public void TearDown()
        {
            if (bootstrapObject != null) Object.DestroyImmediate(bootstrapObject);
            if (hubObject != null) Object.DestroyImmediate(hubObject);
            if (directorObject != null) Object.DestroyImmediate(directorObject);
            bootstrapObject = null;
            bootstrap = null;
            hubObject = null;
            hub = null;
            directorObject = null;
            director = null;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private void BeginDirector(string scenarioId)
        {
            directorObject = new GameObject("TestScenarioDirector");
            director = directorObject.AddComponent<ScenarioDirector>();
            var asset = ScriptableObject.CreateInstance<ScenarioAsset>();
            SetPrivateField(asset, "scenarioId", scenarioId);
            SetPrivateField(asset, "goals", SaloonCircuitScenario.BuildGoals());
            Assert.IsTrue(director.Service.RegisterScenario(asset, out string reason), reason);
        }

        [Test]
        public void SaloonRestore_WithoutFirstLedgerBootstrap_RestoresDirectorAndEvaluatorGoals()
        {
            BeginDirector(SaloonCircuitScenario.ScenarioId);
            Assert.IsFalse(director.HasActiveScenario,
                "Precondition: nobody has restored or started the director yet — there is no First Ledger bootstrap in this scene.");

            bootstrapObject = new GameObject("TestSaloonBootstrap");
            bootstrap = bootstrapObject.AddComponent<SaloonCircuitBootstrap>();
            // Awake found the director but no hub, so it stopped before
            // creating its evaluator; the restore path owns the evaluator
            // once one is wired (as a scene bootstrap's Awake would).
            SetPrivateField(bootstrap, "evaluator", new SaloonCircuitGoalEvaluator());

            var dto = new ScenarioSaveDto
            {
                activeScenarioId = SaloonCircuitScenario.ScenarioId,
                completedGoalIds = new List<string> { "ash-creek-ownership", "crown-rank-one" },
            };
            bootstrap.RestoreFromSaveDto(dto);

            Assert.IsTrue(director.HasActiveScenario, "The Saloon restore ran the director restore itself.");
            Assert.AreEqual(SaloonCircuitScenario.ScenarioId, director.ActiveScenarioId);
            Assert.IsTrue(director.Service.ActiveState.IsGoalCompleted("ash-creek-ownership"),
                "The saved Saloon milestone reaches director runtime state.");
            Assert.IsTrue(bootstrap.Evaluator.HistoricalCompleted.Contains("ash-creek-ownership"),
                "The saved Saloon milestone reaches the evaluator.");
            Assert.IsFalse(bootstrap.Evaluator.PredicatesHolding.Contains("crown-rank-one"),
                "M4: the lapsed final-state goal in the same dto does not resurrect into predicateHolding.");
            Assert.IsFalse(bootstrap.Evaluator.CompletedGoalIds().Contains("crown-rank-one"),
                "M4: the saved list no longer claims the lapsed final.");
        }

        [Test]
        public void LateAwakeningBootstrap_RegistersQualifiersOnAwake_Idempotently()
        {
            BeginDirector(SaloonCircuitScenario.ScenarioId);
            hubObject = new GameObject("TestSystemsHub");
            hub = hubObject.AddComponent<SimulationSystemsHub>();
            // The slice-bootstrapper-style assignment, made BEFORE the
            // first start so the hub service tracks the scenario.
            director.ObjectiveService = hub.ScenarioObjectives;
            Assert.IsTrue(director.SwitchScenario(SaloonCircuitScenario.ScenarioId));
            Assert.AreEqual(SaloonCircuitScenario.ScenarioId, hub.ScenarioObjectives.ScenarioId);
            Assert.AreEqual(0, hub.ScenarioObjectives.QualifierRegistrations.Count,
                "Precondition: the start fired with no bootstrap present, so no qualifiers were registered.");

            // The bootstrap awakens late, after the first start.
            bootstrapObject = new GameObject("TestSaloonBootstrap");
            bootstrap = bootstrapObject.AddComponent<SaloonCircuitBootstrap>();
            Assert.AreEqual(SaloonCircuitScenario.GoalIdsInOrder.Length,
                hub.ScenarioObjectives.QualifierRegistrations.Count,
                "Late Awake registered every Saloon qualifier even though ScenarioStarted already fired.");

            // Live evaluation works through the rebuilt qualifiers, and
            // repeating registration (a later ScenarioStarted, or a
            // second bootstrap) duplicates nothing.
            bootstrap.Evaluator.RestoreHistoricalGoals(new List<string> { "ash-creek-ownership" });
            hub.ScenarioObjectives.Evaluate(0);
            Assert.AreEqual(1, hub.ScenarioObjectives.State.LatchedCompleteGoalIds.Count);
            Assert.IsTrue(hub.ScenarioObjectives.State.LatchedCompleteGoalIds.Contains("ash-creek-ownership"));
            bootstrap.RegisterObjectiveQualifiers();
            Assert.AreEqual(SaloonCircuitScenario.GoalIdsInOrder.Length,
                hub.ScenarioObjectives.QualifierRegistrations.Count,
                "Re-registration never duplicates a qualifier registration.");
            hub.ScenarioObjectives.Evaluate(0);
            Assert.AreEqual(1, hub.ScenarioObjectives.State.LatchedCompleteGoalIds.Count,
                "Re-registration never duplicates a latched goal.");
        }
    }
}
