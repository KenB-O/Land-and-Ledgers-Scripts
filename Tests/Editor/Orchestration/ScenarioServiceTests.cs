using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>
    /// DEV-1: the scenario registry, validation, live-edit operations and the tunable
    /// write policy — all pure logic, exercised without play mode.
    /// </summary>
    [TestFixture]
    public sealed class ScenarioServiceTests
    {
        private global::LandLedgers.Orchestration.Scenarios.ScenarioService service;

        [SetUp]
        public void SetUp()
        {
            service = new global::LandLedgers.Orchestration.Scenarios.ScenarioService();
        }

        private static global::LandLedgers.Orchestration.Scenarios.ScenarioAsset BuildAsset(
            string scenarioId,
            string[] goalIds = null,
            string[] objectiveIds = null)
        {
            var asset = ScriptableObject.CreateInstance<global::LandLedgers.Orchestration.Scenarios.ScenarioAsset>();
            var so = new SerializedObject(asset);
            so.FindProperty("scenarioId").stringValue = scenarioId;
            so.FindProperty("displayName").stringValue = "Test " + scenarioId;

            if (goalIds != null)
            {
                SerializedProperty goalsProp = so.FindProperty("goals");
                goalsProp.arraySize = goalIds.Length;
                for (int i = 0; i < goalIds.Length; i++)
                {
                    SerializedProperty el = goalsProp.GetArrayElementAtIndex(i);
                    el.FindPropertyRelative("goalId").stringValue = goalIds[i];
                    el.FindPropertyRelative("text").stringValue = "Goal " + goalIds[i];
                }
            }

            if (objectiveIds != null)
            {
                SerializedProperty objectivesProp = so.FindProperty("objectives");
                objectivesProp.arraySize = objectiveIds.Length;
                for (int i = 0; i < objectiveIds.Length; i++)
                {
                    SerializedProperty el = objectivesProp.GetArrayElementAtIndex(i);
                    el.FindPropertyRelative("objectiveId").stringValue = objectiveIds[i];
                    el.FindPropertyRelative("text").stringValue = "Objective " + objectiveIds[i];
                }
            }

            so.ApplyModifiedProperties();
            return asset;
        }

        [Test]
        public void RegisterScenario_AcceptsValid_RejectsDuplicatesAndNull()
        {
            Assert.IsTrue(service.RegisterScenario(BuildAsset("s1"), out string reason), reason);
            Assert.IsNull(reason);

            Assert.IsFalse(service.RegisterScenario(BuildAsset("s1"), out reason));
            Assert.IsTrue(reason.Contains("Duplicate"));

            Assert.IsFalse(service.RegisterScenario(null, out reason));
            Assert.IsNotNull(reason);

            Assert.IsFalse(service.RegisterScenario(BuildAsset(""), out reason));
            Assert.IsTrue(reason.Contains("empty"));
        }

        [Test]
        public void BeginScenario_UnknownId_RejectedWithRegisteredList()
        {
            service.RegisterScenario(BuildAsset("s1"), out _);

            Assert.IsFalse(service.BeginScenario("nope", out string reason));
            Assert.IsTrue(reason.Contains("s1"));
            Assert.IsFalse(service.HasActiveScenario);
        }

        [Test]
        public void BeginScenario_DuplicateGoalIds_RejectedLoudly()
        {
            service.RegisterScenario(BuildAsset("s1", new[] { "g1", "g1" }), out _);

            Assert.IsFalse(service.BeginScenario("s1", out string reason));
            Assert.IsTrue(reason.Contains("Duplicate GoalId"));
        }

        [Test]
        public void BeginScenario_EmptyFounderListUsesRuntimeResolutionContract()
        {
            service.RegisterScenario(BuildAsset("s1"), out _);

            Assert.IsTrue(service.BeginScenario("s1", out string reason), reason);
            Assert.IsTrue(service.HasActiveScenario);

            bool warned = false;
            foreach (string diagnostic in service.ActiveState.Diagnostics)
            {
                if (diagnostic.Contains("GHOST-DEF-006"))
                {
                    warned = true;
                }
            }

            Assert.IsFalse(warned,
                "A valid household declaration with runtime-resolved founders must not report the household as absent.");
        }

        [Test]
        public void SetGoalCompleted_FlipsState_UnknownGoalRejected()
        {
            service.RegisterScenario(BuildAsset("s1", new[] { "g1" }), out _);
            Assert.IsTrue(service.BeginScenario("s1", out _));

            Assert.IsFalse(service.ActiveState.IsGoalCompleted("g1"));
            Assert.IsTrue(service.SetGoalCompleted("g1", true, out string reason), reason);
            Assert.IsTrue(service.ActiveState.IsGoalCompleted("g1"));

            Assert.IsFalse(service.SetGoalCompleted("ghost-goal", true, out reason));
            Assert.IsTrue(reason.Contains("not declared"));
        }

        [Test]
        public void LiveEdit_WithoutActiveScenario_Rejected()
        {
            Assert.IsFalse(service.SetGoalCompleted("g1", true, out string reason));
            Assert.IsTrue(reason.Contains("No active scenario"));

            Assert.IsFalse(service.SetTunable("k", new global::LandLedgers.Orchestration.Scenarios.TunableValue("k", 1f), false, out reason));
            Assert.IsTrue(reason.Contains("No active scenario"));
        }

        [Test]
        public void RetargetObjectiveText_OverridesWithoutTouchingAsset()
        {
            service.RegisterScenario(BuildAsset("s1", null, new[] { "o1" }), out _);
            Assert.IsTrue(service.BeginScenario("s1", out _));

            Assert.IsTrue(service.RetargetObjectiveText("o1", "New wording", out string reason), reason);
            Assert.AreEqual("New wording", service.ActiveState.GetObjectiveText("o1"));

            // The authored asset still carries the original text.
            Assert.AreEqual("Objective o1", service.ActiveState.Asset.Objectives[0].Text);

            Assert.IsFalse(service.RetargetObjectiveText("nope", "x", out reason));
            Assert.IsTrue(reason.Contains("not declared"));
        }

        [Test]
        public void AddObjectiveRuntime_AddsObjective_DuplicatesRejected()
        {
            service.RegisterScenario(BuildAsset("s1", null, new[] { "o1" }), out _);
            Assert.IsTrue(service.BeginScenario("s1", out _));

            Assert.IsTrue(service.AddObjectiveRuntime("o2", "Runtime objective", "", out string reason), reason);
            Assert.AreEqual("Runtime objective", service.ActiveState.GetObjectiveText("o2"));

            Assert.IsFalse(service.AddObjectiveRuntime("o1", "dup", "", out reason));
            Assert.IsTrue(reason.Contains("already exists"));
        }

        [Test]
        public void SetTunable_RuntimeOverride_DoesNotTouchAsset()
        {
            service.RegisterScenario(BuildAsset("s1"), out _);
            Assert.IsTrue(service.BeginScenario("s1", out _));

            var overrideValue = new global::LandLedgers.Orchestration.Scenarios.TunableValue("speed", 2.5f);
            Assert.IsTrue(service.SetTunable("speed", overrideValue, false, out string reason), reason);

            global::LandLedgers.Orchestration.Scenarios.TunableValue effective = service.GetEffectiveTunable("speed");
            Assert.IsNotNull(effective);
            Assert.AreEqual(2.5f, effective.FloatValue, 0.0001f);

            // Asset has no tunables — the override lives only in runtime state.
            Assert.AreEqual(0, service.ActiveState.Asset.Tunables.Count);

            bool auditNotesOverride = false;
            foreach (string entry in service.AuditLog)
            {
                if (entry.Contains("RUNTIME OVERRIDE"))
                {
                    auditNotesOverride = true;
                }
            }

            Assert.IsTrue(auditNotesOverride, "Audit log must record the runtime-override policy.");
        }

        [Test]
        public void SetTunable_PersistToAsset_WritesBackWithAudit()
        {
            service.RegisterScenario(BuildAsset("s1"), out _);
            Assert.IsTrue(service.BeginScenario("s1", out _));

            Assert.IsTrue(
                service.SetTunable("speed", new global::LandLedgers.Orchestration.Scenarios.TunableValue("speed", 3f), true, out string reason),
                reason);

            Assert.AreEqual(1, service.ActiveState.Asset.Tunables.Count);
            Assert.AreEqual(3f, service.ActiveState.Asset.Tunables[0].FloatValue, 0.0001f);

            bool auditNotesAsset = false;
            foreach (string entry in service.AuditLog)
            {
                if (entry.Contains("written to ASSET"))
                {
                    auditNotesAsset = true;
                }
            }

            Assert.IsTrue(auditNotesAsset, "Audit log must record explicit asset write-back.");
        }
    }
}
