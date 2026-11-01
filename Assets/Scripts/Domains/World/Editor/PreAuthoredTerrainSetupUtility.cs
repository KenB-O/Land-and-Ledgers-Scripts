using System;
using System.Collections.Generic;
using LandLedgers.FirstLedger;
using LandLedgers.Population;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LandLedgers.World.Editor
{
    public static class PreAuthoredTerrainSetupUtility
    {
        private const string ScenePath = "Assets/Main Scene.unity";

        [MenuItem("Land & Ledgers/World/Validate Pre-Authored Terrain Setup")]
        public static void ValidateMainScene()
        {
            Scene scene = default;
            bool openedAdditively = false;
            try
            {
                scene = SceneManager.GetActiveScene();
                if (!string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase))
                {
                    scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                    openedAdditively = true;
                }

                ValidateScene(scene);
                Debug.Log("[WorldStartup] Main Scene pre-authored terrain validation passed. No scene or TerrainData content was changed.");
            }
            finally
            {
                if (openedAdditively && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [MenuItem("Land & Ledgers/World/Setup Main Scene Pre-Authored Terrain")]
        public static void SetupMainScene()
        {
            Debug.LogWarning("[WorldStartup] The legacy setup action is retired because it could overwrite handcrafted TerrainData. Running validation only.");
            ValidateMainScene();
        }

        public static void ValidateScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException("Pre-authored terrain validation requires a loaded scene.");
            }

            FirstLedgerSliceBootstrapper bootstrapper = FindInScene<FirstLedgerSliceBootstrapper>(scene);
            PreAuthoredTerrainWorldProfile profile = FindInScene<PreAuthoredTerrainWorldProfile>(scene);
            TownWorldController townWorld = FindInScene<TownWorldController>(scene);
            PopulationManager population = FindInScene<PopulationManager>(scene);
            RegionalTerrainTileView terrainView = FindInScene<RegionalTerrainTileView>(scene, true);
            List<string> failures = new();

            if (bootstrapper == null)
            {
                failures.Add("FirstLedgerSliceBootstrapper is missing");
            }

            if (profile == null)
            {
                failures.Add("PreAuthoredTerrainWorldProfile is missing");
            }
            else if (!profile.TryValidateConfiguration(out string profileError))
            {
                failures.Add(profileError);
            }

            if (bootstrapper != null)
            {
                SerializedObject serialized = new(bootstrapper);
                if ((WorldStartupMode)serialized.FindProperty("startupMode").enumValueIndex != WorldStartupMode.UsePreAuthoredTerrain)
                {
                    failures.Add("startup mode is not UsePreAuthoredTerrain");
                }

                if (serialized.FindProperty("preAuthoredTerrainProfile").objectReferenceValue != profile)
                {
                    failures.Add("bootstrapper terrain profile reference is missing or points elsewhere");
                }
            }

            if (townWorld == null)
            {
                failures.Add("TownWorldController is missing");
            }
            else
            {
                SerializedObject serialized = new(townWorld);
                if (serialized.FindProperty("generateOnStart").boolValue)
                {
                    failures.Add("TownWorldController.generateOnStart must be false");
                }

                if (serialized.FindProperty("refreshRegionalTerrainTilesDuringRuntimeStartup").boolValue)
                {
                    failures.Add("runtime regional terrain refresh must be false");
                }

                Transform generatedRoot = townWorld.transform.Find("WorldVisualRoot");
                Transform authoredRoot = FindTransformInScene(scene, "AuthoredWorldContent");
                if (generatedRoot == null || authoredRoot == null || generatedRoot == authoredRoot)
                {
                    failures.Add("separate WorldVisualRoot and AuthoredWorldContent roots are required");
                }
            }

            if (population != null)
            {
                SerializedObject serialized = new(population);
                if (serialized.FindProperty("generateOnStart").boolValue)
                {
                    failures.Add("PopulationManager.generateOnStart must be false");
                }
            }

            if (terrainView != null)
            {
                SerializedObject serialized = new(terrainView);
                if (serialized.FindProperty("buildOnStart").boolValue)
                {
                    failures.Add("RegionalTerrainTileView.buildOnStart must be false");
                }
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException("Pre-authored terrain validation failed: " + string.Join("; ", failures) + ".");
            }
        }

        private static T FindInScene<T>(Scene scene, bool includeInactive = false) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T found = roots[i].GetComponentInChildren<T>(includeInactive);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Transform FindTransformInScene(Scene scene, string objectName)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform[] transforms = roots[i].GetComponentsInChildren<Transform>(true);
                for (int t = 0; t < transforms.Length; t++)
                {
                    if (transforms[t] != null && string.Equals(transforms[t].name, objectName, StringComparison.Ordinal))
                    {
                        return transforms[t];
                    }
                }
            }

            return null;
        }
    }
}
