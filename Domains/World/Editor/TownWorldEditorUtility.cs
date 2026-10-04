using System.IO;
using LandLedgers.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LandLedgers.Editor
{
    public static class TownWorldEditorUtility
    {
        private const string SettingsPath = "Assets/Core/World/DefaultTownGenerationSettings.asset";
        private const string BuildingsFolder = "Assets/Core/World/Buildings";
        private const string RoadMaterialPath = "Assets/Asset Packs/Materials/Dirt.mat";
        private const string ForestPackRoot = "Assets/Asset Packs/NatureManufacture Assets/Forest Environment Dynamic Nature";

        private static readonly string[] ForestCanopyPrefabPaths =
        {
            $"{ForestPackRoot}/Beech Trees/Prefabs/prefab_beech_tree_00_A.prefab",
            $"{ForestPackRoot}/Beech Trees/Prefabs/prefab_beech_tree_01.prefab",
            $"{ForestPackRoot}/Beech Trees/Prefabs/prefab_beech_tree_02.prefab",
            $"{ForestPackRoot}/Beech Trees/Prefabs/prefab_beech_tree_04.prefab",
            $"{ForestPackRoot}/Beech Trees/Prefabs/prefab_beech_tree_06.prefab",
            $"{ForestPackRoot}/Beech Trees/Prefabs/prefab_beech_tree_08.prefab"
        };

        private static readonly string[] ForestUnderstoryPrefabPaths =
        {
            $"{ForestPackRoot}/Beech Trees/Prefabs/prefab_beech_plant_00.prefab",
            $"{ForestPackRoot}/Beech Trees/Prefabs/prefab_beech_plant_01.prefab",
            $"{ForestPackRoot}/Beech Trees/Prefabs/prefab_beech_plant_02.prefab",
            $"{ForestPackRoot}/Bushes/Prefabs/Prefab_Forest_black_cherry_00.prefab",
            $"{ForestPackRoot}/Bushes/Prefabs/Prefab_Forest_black_cherry_02.prefab",
            $"{ForestPackRoot}/Bushes/Prefabs/Prefab_Forest_black_cherry_04.prefab"
        };

        private static readonly string[] ForestGroundCoverPrefabPaths =
        {
            $"{ForestPackRoot}/Foliage and Grass/Prefabs/prefab_fern_01_1.prefab",
            $"{ForestPackRoot}/Foliage and Grass/Prefabs/prefab_fern_01_2.prefab",
            $"{ForestPackRoot}/Foliage and Grass/Prefabs/prefab_grass_01_1.prefab",
            $"{ForestPackRoot}/Foliage and Grass/Prefabs/prefab_grass_02_1.prefab",
            $"{ForestPackRoot}/Foliage and Grass/Prefabs/prefab_plant_01_1.prefab",
            $"{ForestPackRoot}/Foliage and Grass/Prefabs/prefab_lily_valley_01_1.prefab"
        };

        private static readonly string[] ForestWetGroundPrefabPaths =
        {
            $"{ForestPackRoot}/Foliage and Grass/Prefabs/prefab_fern_01_3.prefab",
            $"{ForestPackRoot}/Foliage and Grass/Prefabs/prefab_fern_01_4.prefab",
            $"{ForestPackRoot}/Foliage and Grass/Prefabs/prefab_lily_valley_01_2.prefab",
            $"{ForestPackRoot}/Details/Prefabs/prefab_detail_moss_01_1.prefab",
            $"{ForestPackRoot}/Details/Prefabs/prefab_detail_moss_01_2.prefab"
        };

        private static readonly string[] ForestDeadfallPrefabPaths =
        {
            $"{ForestPackRoot}/Stumps Roots and Branches/Prefabs/prefab_beech_forest_stump_01_1.prefab",
            $"{ForestPackRoot}/Stumps Roots and Branches/Prefabs/prefab_Beech_forest_stump_02_01.prefab",
            $"{ForestPackRoot}/Stumps Roots and Branches/Prefabs/prefab_dead_log_01.prefab",
            $"{ForestPackRoot}/Stumps Roots and Branches/Prefabs/prefab_dead_log_03.prefab",
            $"{ForestPackRoot}/Stumps Roots and Branches/Prefabs/prefab_beech_old_roots_01.prefab",
            $"{ForestPackRoot}/Stumps Roots and Branches/Prefabs/prefab_beech_forest_branch_01_2.prefab",
            $"{ForestPackRoot}/Stumps Roots and Branches/Prefabs/prefab_beech_forest_scarp_01.prefab"
        };

        private static readonly string[] ForestMeadowPrefabPaths =
        {
            $"{ForestPackRoot}/Foliage and Grass/Prefabs/prefab_grass_01_2.prefab",
            $"{ForestPackRoot}/Foliage and Grass/Prefabs/prefab_grass_02_2.prefab",
            $"{ForestPackRoot}/Foliage and Grass/Prefabs/prefab_grass_03_1.prefab",
            $"{ForestPackRoot}/Foliage and Grass/Prefabs/prefab_plant_01_2.prefab",
            $"{ForestPackRoot}/Foliage and Grass/Prefabs/prefab_plant_02_1.prefab"
        };

        private static readonly string[] ForestRockPrefabPaths =
        {
            $"{ForestPackRoot}/Rocks/Prefabs Leaves/prefab_beech_forest_stones_01_1_Leaves.prefab",
            $"{ForestPackRoot}/Rocks/Prefabs Leaves/prefab_beech_forest_stones_01_2_Leaves.prefab",
            $"{ForestPackRoot}/Rocks/Prefabs Leaves/prefab_beech_forest_stones_01_3_Leaves.prefab",
            $"{ForestPackRoot}/Rocks/Prefabs Leaves/prefab_beech_forest_stones_01_6_Leaves.prefab",
            $"{ForestPackRoot}/Rocks/Prefabs Leaves/prefab_beech_forest_stones_01_10_Leaves.prefab"
        };

        [MenuItem("Land & Ledgers/World/Create Default Town Generation Settings")]
        public static void CreateDefaultSettingsAsset()
        {
            Selection.activeObject = GetOrCreateSettingsAsset();
        }

        [MenuItem("Land & Ledgers/World/Create Town World In Scene")]
        public static void CreateTownWorldInScene()
        {
            TownGenerationSettings settings = GetOrCreateSettingsAsset();
            TownWorldController controller = Object.FindAnyObjectByType<TownWorldController>();

            if (controller == null)
            {
                GameObject world = new("Town World");
                Undo.RegisterCreatedObjectUndo(world, "Create Town World");
                controller = world.AddComponent<TownWorldController>();
            }

            ConfigureController(controller, settings);
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            Selection.activeObject = controller.gameObject;
        }

        [MenuItem("Land & Ledgers/World/Generate Selected Town Shell")]
        public static void GenerateSelectedTownShell()
        {
            TownWorldController controller = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<TownWorldController>()
                : Object.FindAnyObjectByType<TownWorldController>();

            if (controller == null)
            {
                Debug.LogWarning("No TownWorldController found. Use Land & Ledgers/World/Create Town World In Scene first.");
                return;
            }

            ConfigureController(controller, GetOrCreateSettingsAsset());
            controller.GenerateTownShell();
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        }

        public static void BootstrapDefaultSceneAndAssets()
        {
            TownGenerationSettings settings = GetOrCreateSettingsAsset();
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene("Assets/Main Scene.unity");
            TownWorldController controller = Object.FindAnyObjectByType<TownWorldController>();

            if (controller == null)
            {
                GameObject world = new("Town World");
                controller = world.AddComponent<TownWorldController>();
            }

            ConfigureController(controller, settings);
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        public static TownGenerationSettings GetOrCreateSettingsAsset()
        {
            TownGenerationSettings settings = AssetDatabase.LoadAssetAtPath<TownGenerationSettings>(SettingsPath);
            if (settings != null)
            {
                EnsureRoadMaterial(settings);
                EnsureBuildingCatalog(settings);
                EnsureForestEnvironmentPrefabs(settings);
                return settings;
            }

            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            settings.Sanitize();
            EnsureRoadMaterial(settings);
            AssetDatabase.CreateAsset(settings, SettingsPath);
            EnsureBuildingCatalog(settings);
            EnsureForestEnvironmentPrefabs(settings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return settings;
        }

        private static void EnsureRoadMaterial(TownGenerationSettings settings)
        {
            if (settings.roadMaterial != null)
            {
                return;
            }

            Material roadMaterial = AssetDatabase.LoadAssetAtPath<Material>(RoadMaterialPath);
            if (roadMaterial == null)
            {
                Debug.LogWarning($"Default road material was not found at '{RoadMaterialPath}'. Generated town roads will keep using the road debug color fallback.", settings);
                return;
            }

            settings.roadMaterial = roadMaterial;
            EditorUtility.SetDirty(settings);
        }

        private static void EnsureBuildingCatalog(TownGenerationSettings settings)
        {
            System.IO.Directory.CreateDirectory(BuildingsFolder);

            BuildingDefinition[] authoredCatalog = LoadAuthoredBuildingCatalog();
            if (authoredCatalog.Length > 0)
            {
                settings.buildingCatalog = authoredCatalog;
                EditorUtility.SetDirty(settings);
                return;
            }

            BuildingDefinition generalStore = GetOrCreateBuilding("GeneralStore.asset", "general_store", "General Store", PlotZone.Business, new Vector2Int(5, 5), new Color(0.65f, 0.43f, 0.24f, 1f), 6f, "Assets/Environment/Buildings/Single Story Business Building Natural Wood.prefab");
            BuildingDefinition house = GetOrCreateBuilding("House.asset", "house", "House", PlotZone.Residential, new Vector2Int(4, 4), new Color(0.58f, 0.48f, 0.35f, 1f), 4.5f, "Assets/Environment/Buildings/House Natural Wood.prefab");
            BuildingDefinition smallBusiness = GetOrCreateBuilding("SmallBusiness.asset", "small_business", "Small Business", PlotZone.Business, new Vector2Int(4, 5), new Color(0.56f, 0.38f, 0.23f, 1f), 5.5f, "Assets/Environment/Buildings/Single Story Business Building Green Wood.prefab");
            BuildingDefinition saloon = GetOrCreateBuilding("SaloonPlaceholder.asset", "saloon_placeholder", "Saloon Placeholder", PlotZone.Business, new Vector2Int(6, 6), new Color(0.55f, 0.29f, 0.22f, 1f), 6.5f, "Assets/Environment/Buildings/Double Story Saloon Building Corner Natural Wood.prefab");

            settings.buildingCatalog = new[] { generalStore, house, smallBusiness, saloon };
            EditorUtility.SetDirty(settings);
        }

        private static BuildingDefinition[] LoadAuthoredBuildingCatalog()
        {
            string[] guids = AssetDatabase.FindAssets("t:BuildingDefinition", new[] { BuildingsFolder });
            System.Collections.Generic.List<BuildingDefinition> definitions = new(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                BuildingDefinition definition = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(path);
                if (definition != null && definition.PrimaryUse != BuildingUseType.Civic)
                {
                    definitions.Add(definition);
                }
            }

            definitions.Sort((left, right) => string.Compare(left.BuildingId, right.BuildingId, System.StringComparison.OrdinalIgnoreCase));
            return definitions.ToArray();
        }

        private static void EnsureForestEnvironmentPrefabs(TownGenerationSettings settings)
        {
            bool dirty = false;
            dirty |= AssignPrefabsIfMissing(ref settings.forestCanopyTreePrefabs, ForestCanopyPrefabPaths);
            dirty |= AssignPrefabsIfMissing(ref settings.forestUnderstoryPrefabs, ForestUnderstoryPrefabPaths);
            dirty |= AssignPrefabsIfMissing(ref settings.forestGroundCoverPrefabs, ForestGroundCoverPrefabPaths);
            dirty |= AssignPrefabsIfMissing(ref settings.forestWetGroundPrefabs, ForestWetGroundPrefabPaths);
            dirty |= AssignPrefabsIfMissing(ref settings.forestDeadfallPrefabs, ForestDeadfallPrefabPaths);
            dirty |= AssignPrefabsIfMissing(ref settings.forestMeadowPrefabs, ForestMeadowPrefabPaths);
            dirty |= AssignPrefabsIfMissing(ref settings.forestRockPrefabs, ForestRockPrefabPaths);

            if (dirty)
            {
                EditorUtility.SetDirty(settings);
            }
        }

        private static bool AssignPrefabsIfMissing(ref GameObject[] target, string[] paths)
        {
            if (HasAnyAssignedPrefab(target))
            {
                return false;
            }

            target = LoadPrefabArray(paths);
            return target.Length > 0;
        }

        private static GameObject[] LoadPrefabArray(string[] paths)
        {
            if (paths == null || paths.Length == 0)
            {
                return System.Array.Empty<GameObject>();
            }

            System.Collections.Generic.List<GameObject> prefabs = new();
            for (int i = 0; i < paths.Length; i++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                if (prefab != null)
                {
                    prefabs.Add(prefab);
                }
            }

            return prefabs.ToArray();
        }

        private static bool HasAnyAssignedPrefab(GameObject[] prefabs)
        {
            if (prefabs == null)
            {
                return false;
            }

            for (int i = 0; i < prefabs.Length; i++)
            {
                if (prefabs[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static BuildingDefinition GetOrCreateBuilding(string fileName, string id, string label, PlotZone zone, Vector2Int footprint, Color color, float height, string visualPrefabPath)
        {
            string path = $"{BuildingsFolder}/{fileName}";
            BuildingDefinition definition = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(path);
            if (definition != null)
            {
                definition.ConfigureVisual(AssetDatabase.LoadAssetAtPath<GameObject>(visualPrefabPath), 1f, 0f, Vector3.zero, true, 0.92f);
                EditorUtility.SetDirty(definition);
                return definition;
            }

            definition = ScriptableObject.CreateInstance<BuildingDefinition>();
            definition.name = label;
            definition.ConfigureRuntimeFallback(id, label, zone, footprint, color, height);
            definition.ConfigureVisual(AssetDatabase.LoadAssetAtPath<GameObject>(visualPrefabPath), 1f, 0f, Vector3.zero, true, 0.92f);
            AssetDatabase.CreateAsset(definition, path);
            return definition;
        }

        private static void ConfigureController(TownWorldController controller, TownGenerationSettings settings)
        {
            Transform visualRoot = controller.transform.Find("WorldVisualRoot");
            if (visualRoot == null)
            {
                GameObject visualRootObject = new("WorldVisualRoot");
                Undo.RegisterCreatedObjectUndo(visualRootObject, "Create World Visual Root");
                visualRootObject.transform.SetParent(controller.transform, false);
                visualRoot = visualRootObject.transform;
            }

            Collider terrainCollider = null;
            GameObject ground = GameObject.Find("Ground");
            if (ground != null)
            {
                terrainCollider = ground.GetComponent<Collider>();
            }

            controller.Configure(settings, terrainCollider, visualRoot);
        }
    }
}
