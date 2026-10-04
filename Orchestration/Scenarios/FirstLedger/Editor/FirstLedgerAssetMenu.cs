using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Orchestration.Scenarios.FirstLedger.Editor
{
    /// <summary>
    /// BIZ-6: in-editor authoring for the First Ledger scenario asset. The hand-authored
    /// <c>FirstLedger.asset</c> ships in the repo; this menu item is the reliable
    /// fallback — it rebuilds the asset from <see cref="FirstLedgerScenario"/> code so
    /// Kennedy never depends on hand-written YAML alone.
    /// </summary>
    public static class FirstLedgerAssetMenu
    {
        [MenuItem("Land & Ledgers/Scenario/Create First Ledger Asset")]
        public static void CreateFirstLedgerAsset()
        {
            var asset = ScriptableObject.CreateInstance<ScenarioAsset>();

            // Serialized fields are private; use SerializedObject for reliable writes.
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("scenarioId").stringValue = FirstLedgerScenario.ScenarioId;
            serialized.FindProperty("displayName").stringValue = "First Ledger";
            serialized.FindProperty("description").stringValue = FirstLedgerScenario.OpeningNarration +
                "\n\nNOTE: name your household founders on this asset (Player Household section) " +
                "before bootstrapping — the scenario declares the player household (GHOST-DEF-006).";

            SerializedProperty household = serialized.FindProperty("playerHousehold");
            household.FindPropertyRelative("scenarioPlayerId").stringValue = "player";
            household.FindPropertyRelative("householdName").stringValue = "Player Household";

            SerializedProperty inclusion = serialized.FindProperty("populationInclusion");
            inclusion.FindPropertyRelative("includePlayerHousehold").boolValue = true;
            inclusion.FindPropertyRelative("includeTownResidents").boolValue = true;
            inclusion.FindPropertyRelative("includeRuralServicePopulation").boolValue = false;
            inclusion.FindPropertyRelative("includeTransients").boolValue = false;
            inclusion.FindPropertyRelative("notes").stringValue =
                "Money-goals scenario (Tech X §2.1): the player household and town residents count; " +
                "rural-service and transient populations are excluded from scenario read models.";

            SerializedProperty goals = serialized.FindProperty("goals");
            goals.ClearArray();
            foreach (ScenarioGoal goal in FirstLedgerScenario.BuildGoals())
            {
                goals.InsertArrayElementAtIndex(goals.arraySize);
                SerializedProperty element = goals.GetArrayElementAtIndex(goals.arraySize - 1);
                element.FindPropertyRelative("goalId").stringValue = goal.GoalId;
                element.FindPropertyRelative("text").stringValue = goal.Text;
                element.FindPropertyRelative("targetText").stringValue = goal.TargetText;
                element.FindPropertyRelative("optional").boolValue = goal.Optional;
            }

            SerializedProperty objectives = serialized.FindProperty("objectives");
            objectives.ClearArray();
            foreach (ScenarioObjective objective in FirstLedgerScenario.BuildObjectives())
            {
                objectives.InsertArrayElementAtIndex(objectives.arraySize);
                SerializedProperty element = objectives.GetArrayElementAtIndex(objectives.arraySize - 1);
                element.FindPropertyRelative("objectiveId").stringValue = objective.ObjectiveId;
                element.FindPropertyRelative("linkedGoalId").stringValue = objective.LinkedGoalId;
                element.FindPropertyRelative("text").stringValue = objective.Text;
            }

            SerializedProperty tunables = serialized.FindProperty("tunables");
            tunables.ClearArray();
            foreach (TunableValue tunable in FirstLedgerScenario.BuildTunables())
            {
                tunables.InsertArrayElementAtIndex(tunables.arraySize);
                SerializedProperty element = tunables.GetArrayElementAtIndex(tunables.arraySize - 1);
                element.FindPropertyRelative("key").stringValue = tunable.Key;
                element.FindPropertyRelative("kind").intValue = (int)tunable.Kind;
                element.FindPropertyRelative("description").stringValue = tunable.Description;
                switch (tunable.Kind)
                {
                    case TunableValue.ValueKind.Float:
                        element.FindPropertyRelative("floatValue").floatValue = tunable.FloatValue;
                        break;
                    case TunableValue.ValueKind.Int:
                        element.FindPropertyRelative("intValue").intValue = tunable.IntValue;
                        break;
                    default:
                        element.FindPropertyRelative("textValue").stringValue = tunable.TextValue;
                        break;
                }
            }

            serialized.ApplyModifiedProperties();

            const string path = "Assets/Orchestration/Scenarios/FirstLedger/FirstLedger.asset";
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            Debug.Log($"First Ledger scenario asset created at {path}.", asset);
        }
    }
}
