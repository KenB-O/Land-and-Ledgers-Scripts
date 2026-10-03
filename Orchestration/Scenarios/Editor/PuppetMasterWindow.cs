using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Orchestration.Scenarios.Editor
{
    /// <summary>
    /// DEV-2: the edit-mode mirror of the runtime PuppetMasterPanel. Kennedy's requirement:
    /// everything the puppet master does at runtime must also be editable in the editor.
    ///
    /// Edit mode can: browse/create ScenarioAssets, edit goals, objectives, tunables and
    /// the player-household declaration directly on the assets. Runtime-only operations
    /// (scenario switching, task injection, time controls, entity inspection) are shown
    /// disabled with an explanation — they need a live simulation, and this window says
    /// so instead of pretending.
    ///
    /// Open via "Land &amp; Ledgers/Scenario/Puppet Master Window".
    /// </summary>
    public sealed class PuppetMasterWindow : EditorWindow
    {
        private Vector2 scrollPosition;
        private ScenarioAsset selectedAsset;
        private SerializedObject serializedAsset;
        private string message = string.Empty;

        [MenuItem("Land & Ledgers/Scenario/Puppet Master Window")]
        public static void Open()
        {
            GetWindow<PuppetMasterWindow>("Puppet Master");
        }

        [MenuItem("Land & Ledgers/Scenario/Add Puppet Master To Scene")]
        public static void AddPuppetMasterToScene()
        {
            PuppetMasterPanel existing = FindAnyObjectByType<PuppetMasterPanel>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("A PuppetMasterPanel already exists in the open scene.", existing);
                return;
            }

            var managerObject = new GameObject("Puppet Master");
            managerObject.AddComponent<PuppetMasterPanel>();
            Selection.activeGameObject = managerObject;
            Undo.RegisterCreatedObjectUndo(managerObject, "Add Puppet Master");
        }

        [MenuItem("Land & Ledgers/Scenario/Add Scenario Director To Scene")]
        public static void AddScenarioDirectorToScene()
        {
            ScenarioDirector existing = FindAnyObjectByType<ScenarioDirector>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("A ScenarioDirector already exists in the open scene.", existing);
                return;
            }

            var managerObject = new GameObject("Scenario Director");
            managerObject.AddComponent<ScenarioDirector>();
            Selection.activeGameObject = managerObject;
            Undo.RegisterCreatedObjectUndo(managerObject, "Add Scenario Director");
        }

        private void OnGUI()
        {
            GUILayout.Label("Puppet Master — Edit Mode", EditorStyles.boldLabel);
            GUILayout.Label("Mirrors the runtime panel. Asset edits here are permanent; " +
                            "runtime-only operations need play mode.");

            DrawAssetPicker();

            if (selectedAsset == null)
            {
                EditorGUILayout.HelpBox(
                    "Select a ScenarioAsset to edit it. Create one via Assets > Create > Land & Ledgers > Scenario Asset.",
                    MessageType.Info);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            serializedAsset = new SerializedObject(selectedAsset);

            DrawIdentitySection();
            DrawPlayerHouseholdSection();
            DrawPopulationSection();
            DrawGoalsSection();
            DrawObjectivesSection();
            DrawTunablesSection();

            serializedAsset.ApplyModifiedProperties();

            DrawRuntimeOnlySection();

            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(message))
            {
                EditorGUILayout.HelpBox(message, MessageType.Info);
            }
        }

        private void DrawAssetPicker()
        {
            GUILayout.BeginHorizontal();
            ScenarioAsset picked = (ScenarioAsset)EditorGUILayout.ObjectField(
                "Scenario Asset", selectedAsset, typeof(ScenarioAsset), false);
            if (picked != selectedAsset)
            {
                selectedAsset = picked;
                message = string.Empty;
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(70)))
            {
                selectedAsset = null;
            }

            GUILayout.EndHorizontal();
        }

        private void DrawIdentitySection()
        {
            GUILayout.Label("Identity", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedAsset.FindProperty("scenarioId"));
            EditorGUILayout.PropertyField(serializedAsset.FindProperty("displayName"));
            EditorGUILayout.PropertyField(serializedAsset.FindProperty("description"));

            if (GUILayout.Button("Validate Asset"))
            {
                List<string> problems = selectedAsset.Validate();
                message = problems.Count == 0
                    ? "Asset is valid."
                    : "Problems:\n- " + string.Join("\n- ", problems);
            }
        }

        private void DrawPlayerHouseholdSection()
        {
            GUILayout.Space(6);
            GUILayout.Label("Player Household (GHOST-DEF-006)", EditorStyles.boldLabel);
            SerializedProperty declaration = serializedAsset.FindProperty("playerHousehold");
            EditorGUILayout.PropertyField(declaration.FindPropertyRelative("scenarioPlayerId"));
            EditorGUILayout.PropertyField(declaration.FindPropertyRelative("householdName"));
            EditorGUILayout.PropertyField(declaration.FindPropertyRelative("founderDisplayNames"), true);
        }

        private void DrawPopulationSection()
        {
            GUILayout.Space(6);
            GUILayout.Label("Population Inclusion (Tech X §2.1)", EditorStyles.boldLabel);
            SerializedProperty rules = serializedAsset.FindProperty("populationInclusion");
            EditorGUILayout.PropertyField(rules.FindPropertyRelative("includePlayerHousehold"));
            EditorGUILayout.PropertyField(rules.FindPropertyRelative("includeTownResidents"));
            EditorGUILayout.PropertyField(rules.FindPropertyRelative("includeRuralServicePopulation"));
            EditorGUILayout.PropertyField(rules.FindPropertyRelative("includeTransients"));
            EditorGUILayout.PropertyField(rules.FindPropertyRelative("notes"));
        }

        private void DrawGoalsSection()
        {
            GUILayout.Space(6);
            GUILayout.Label("Goals", EditorStyles.boldLabel);
            SerializedProperty goals = serializedAsset.FindProperty("goals");
            EditorGUILayout.PropertyField(goals, true);
        }

        private void DrawObjectivesSection()
        {
            GUILayout.Space(6);
            GUILayout.Label("Objectives", EditorStyles.boldLabel);
            SerializedProperty objectives = serializedAsset.FindProperty("objectives");
            EditorGUILayout.PropertyField(objectives, true);
        }

        private void DrawTunablesSection()
        {
            GUILayout.Space(6);
            GUILayout.Label("Tunables", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedAsset.FindProperty("tunables"), true);
            EditorGUILayout.HelpBox(
                "Tunables edited here change the authored asset permanently. " +
                "At runtime, the puppet panel edits them as overrides unless you tick 'Persist to ASSET'.",
                MessageType.None);
        }

        private void DrawRuntimeOnlySection()
        {
            GUILayout.Space(10);
            GUILayout.Label("Runtime-only (needs play mode)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Scenario switching, entity inspection by id, task injection and time controls " +
                "operate on the live simulation. Enter play mode and press F8 for the runtime Puppet Master panel.",
                MessageType.Info);
        }
    }
}
