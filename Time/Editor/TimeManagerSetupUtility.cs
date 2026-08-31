using System.IO;
using LandLedgers.Time;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LandLedgers.Editor
{
    public static class TimeManagerSetupUtility
    {
        private const string SettingsPath = "Assets/Core/Time/DefaultSimulationTimeSettings.asset";

        [MenuItem("Land & Ledgers/Time/Create Time Manager In Scene")]
        public static void CreateTimeManagerInScene()
        {
            SimulationTimeSettings settings = GetOrCreateSettingsAsset();
            TimeManager existing = Object.FindAnyObjectByType<TimeManager>();

            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("A TimeManager already exists in the open scene.", existing);
                return;
            }

            GameObject managerObject = new("Time Manager");
            TimeManager manager = managerObject.AddComponent<TimeManager>();
            SerializedObject serializedObject = new(manager);
            serializedObject.FindProperty("settings").objectReferenceValue = settings;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            Undo.RegisterCreatedObjectUndo(managerObject, "Create Time Manager");
            Selection.activeGameObject = managerObject;
            EditorSceneManager.MarkSceneDirty(managerObject.scene);
        }

        [MenuItem("Land & Ledgers/Time/Create Default Simulation Time Settings")]
        public static void CreateDefaultSettings()
        {
            Selection.activeObject = GetOrCreateSettingsAsset();
        }

        public static SimulationTimeSettings GetOrCreateSettingsAsset()
        {
            SimulationTimeSettings settings = AssetDatabase.LoadAssetAtPath<SimulationTimeSettings>(SettingsPath);
            if (settings != null)
            {
                return settings;
            }

            string directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            settings.Sanitize();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return settings;
        }
    }
}
