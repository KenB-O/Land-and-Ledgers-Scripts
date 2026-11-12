using System;
using System.Collections.Generic;
using System.IO;
using LandLedgers.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LandLedgers.EnvironmentDevelopment.Editor
{
    public static class RuralEnvironmentAuthoring
    {
        private const string RootName = "Rural Landscape Dressing";
        private const string AssetRoot = "Assets/EnvironmentDevelopment";
        private const string BackupPath = AssetRoot + "/Backups/BeforeRuralPass/MainAuthoredTerrainData.asset";
        private const string Pack = "Assets/Asset Packs/NatureManufacture Assets/Forest Environment Dynamic Nature/";
        private static readonly Vector2[] Groves =
        {
            new Vector2(-145, 125), new Vector2(155, 140), new Vector2(-160, -135),
            new Vector2(170, -125), new Vector2(-270, 230), new Vector2(300, 260),
            new Vector2(-300, -250), new Vector2(275, -270)
        };

        public static float GroveInfluence(Vector2 point)
        {
            float influence = 0;
            foreach (Vector2 center in Groves)
            {
                float radius = Mathf.Abs(center.x) > 200 ? 70 : 46;
                float distance = Vector2.Distance(point, center);
                influence = Mathf.Max(influence, 1 - Mathf.SmoothStep(0, 1, distance / radius));
            }
            return influence * Mathf.Lerp(0.75f, 1, Mathf.PerlinNoise(point.x * 0.019f + 32.7f, point.y * 0.019f + 82.3f));
        }

        [MenuItem("Land & Ledgers/Environment/Bake Rural Ground And Vegetation")]
        public static void Bake()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Bake in Edit Mode only.");
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != "Assets/Main Scene.unity") throw new InvalidOperationException("This bake targets Main Scene only.");
            Terrain terrain = null;
            Transform authored = null;
            TownWorldController town = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "AuthoredWorldContent") authored = root.transform;
                if (root.GetComponent<Terrain>() != null) terrain = root.GetComponent<Terrain>();
                if (root.GetComponent<TownWorldController>() != null) town = root.GetComponent<TownWorldController>();
            }
            if (terrain == null || authored == null || town == null) throw new InvalidOperationException("Required authored scene references missing.");
            if (!AssetDatabase.AssetPathExists(BackupPath)) throw new InvalidOperationException("Restorable TerrainData backup is required before baking.");
            TerrainData original = AssetDatabase.LoadAssetAtPath<TerrainData>(BackupPath);
            TerrainData data = terrain.terrainData;
            if (original == null || original == data) throw new InvalidOperationException("Independent terrain backup missing.");
            if (authored.Find(RootName) != null) throw new InvalidOperationException("A rural bake already exists. Preserve it; do not overwrite hand edits.");
            TownGenerationSettings settings = town.Settings;
            if (settings == null || settings.generateForestEnvironmentDressing) throw new InvalidOperationException("Disable runtime forest dressing before authoring a persistent bake.");
            _protectedBounds.Clear();
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            {
                if (sceneRoot.name == "Prefab Environment" || sceneRoot.name == "Town World")
                    foreach (Renderer renderer in sceneRoot.GetComponentsInChildren<Renderer>(true))
                        _protectedBounds.Add(renderer.bounds);
            }
            Undo.RegisterCompleteObjectUndo(data, "Bake rural ground palette");
            System.IO.Directory.CreateDirectory(AssetRoot + "/TerrainLayers");
            AssetDatabase.Refresh();
            float[,,] old = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            _roadMap = original.GetAlphamaps(0, 0, original.alphamapWidth, original.alphamapHeight);
            var layers = new List<TerrainLayer>(data.terrainLayers);
            int meadow = layers.Count; layers.Add(CopyLayer("Terrain_Layer4_Grass_Plants", "Meadow Grass", 2.8f));
            int soilIndex = layers.Count; layers.Add(CopyLayer("Terrain_Layer2_Soil", "Disturbed Soil", 2.6f));
            int litterIndex = layers.Count; layers.Add(CopyLayer("Terrain_Layer5_Leaves", "Grove Leaf Litter", 3.2f));
            int wetIndex = layers.Count; layers.Add(CopyLayer("Terrain_Layer3_Soil_Wet", "Damp Lowland Earth", 3.0f));
            int rockIndex = layers.Count; layers.Add(CopyLayer("Terrain_Layer8_Stones", "Exposed Stones", 3.5f));
            data.terrainLayers = layers.ToArray();
            int resolution = data.alphamapResolution;
            float[,,] map = new float[resolution, resolution, layers.Count];
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                float nx = x / (float)(resolution - 1), nz = z / (float)(resolution - 1);
                Vector2 p = new Vector2(terrain.transform.position.x + nx * data.size.x, terrain.transform.position.z + nz * data.size.z);
                float worn = old.GetLength(2) > 2 ? old[z, x, 2] : 0;
                float grove = GroveInfluence(p);
                float broad = Mathf.PerlinNoise(p.x * 0.010f + 73.2f, p.y * 0.010f + 26.8f);
                float dry = Mathf.SmoothStep(0, 1, Mathf.Clamp01((broad - 0.43f) * 4.0f));
                float slope = data.GetSteepness(nx, nz);
                float rocky = Mathf.Clamp01((slope - 7) / 15) * 0.45f + dry * 0.06f;
                float soil = dry * 0.26f;
                float litter = grove * 0.72f;
                float height = terrain.transform.position.y + data.GetInterpolatedHeight(nx, nz);
                float damp = Mathf.Clamp01((0.5f - height) / 2.5f) * 0.36f * (1 - dry);
                float grass = Mathf.Max(0.05f, 1 - rocky - soil - litter - damp);
                float shoulder = Mathf.Clamp01(RoadStrength(terrain, p, 5) * 0.8f + Disturbance(p));
                float retained = Mathf.Clamp01(worn);
                float blend = 1 - retained;
                // Preserve the original worn road core exactly; blend dry shoulders beside it.
                for (int l = 0; l < old.GetLength(2); l++) map[z,x,l] = old[z,x,l] * retained;
                map[z,x,meadow] = grass * blend * (1 - shoulder);
                map[z,x,soilIndex] = (soil * (1 - shoulder) + shoulder) * blend;
                map[z,x,litterIndex] = litter * blend * (1 - shoulder);
                map[z,x,wetIndex] = damp * blend * (1 - shoulder);
                map[z,x,rockIndex] = rocky * blend * (1 - shoulder);
                float sum = 0; for (int l = 0; l < layers.Count; l++) sum += map[z,x,l];
                for (int l = 0; l < layers.Count; l++) map[z,x,l] /= sum;
            }
            data.SetAlphamaps(0, 0, map);
            EditorUtility.SetDirty(data);
            var rootObject = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(rootObject, "Bake rural vegetation");
            rootObject.transform.SetParent(authored, false);
            var random = new System.Random(18861107);
            var positions = new List<Vector2>();
            var detailPositions = new List<Vector2>();
            int trees = 0, understory = 0;
            GameObject[] canopy = { AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Environment/Vegetation/Trees/prefab_beech_tree_02.prefab"), AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Environment/Vegetation/Trees/prefab_beech_tree_05.prefab") };
            GameObject[] shrubs = { AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Environment/Vegetation/Trees/prefab_beech_plant_02.prefab"), AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Environment/Vegetation/Shrubs/Prefab_Forest_black_cherry_02.prefab") };
            GameObject[] ground = { AssetDatabase.LoadAssetAtPath<GameObject>(Pack + "Foliage and Grass/Prefabs/prefab_fern_01_2.prefab"), AssetDatabase.LoadAssetAtPath<GameObject>(Pack + "Foliage and Grass/Prefabs/prefab_grass_02_2.prefab") };
            GameObject[] deadfall = { AssetDatabase.LoadAssetAtPath<GameObject>(Pack + "Stumps Roots and Branches/Prefabs/prefab_dead_log_03.prefab"), AssetDatabase.LoadAssetAtPath<GameObject>(Pack + "Stumps Roots and Branches/Prefabs/prefab_beech_forest_stump_01_1.prefab"), AssetDatabase.LoadAssetAtPath<GameObject>(Pack + "Stumps Roots and Branches/Prefabs/prefab_beech_forest_branch_01_2.prefab") };
            GameObject[] rocks = { AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Environment/Props/SmallRock02.prefab") };
            for (int g = 0; g < Groves.Length; g++)
            {
                var groveRoot = new GameObject("Grove " + (g + 1));
                Undo.RegisterCreatedObjectUndo(groveRoot, "Create grove");
                groveRoot.transform.SetParent(rootObject.transform, false);
                float radius = g < 4 ? 36 : 52;
                int target = trees + (g < 4 ? 13 : 18);
                for (int i = 0; i < 400 && trees < target; i++)
                {
                    float angle = (float)random.NextDouble() * Mathf.PI * 2;
                    float r = Mathf.Sqrt((float)random.NextDouble()) * radius;
                    Vector2 p = Groves[g] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
                    if (!SafePoint(terrain, p) || TooClose(positions, p, 8)) continue;
                    GameObject prefab = Pick(canopy, random);
                    if (prefab == null) continue;
                    Place(prefab, groveRoot.transform, terrain, p, random, 0.82f, 1.12f, false);
                    positions.Add(p);
                    trees++;
                }
                target = understory + 28;
                for (int i = 0; i < 300 && understory < target; i++)
                {
                    float angle = (float)random.NextDouble() * Mathf.PI * 2;
                    float r = Mathf.Sqrt((float)random.NextDouble()) * (radius + 8);
                    Vector2 p = Groves[g] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
                    if (!SafePoint(terrain, p) || TooClose(detailPositions, p, 2) || TooClose(positions, p, 2.5f)) continue;
                    double roll = random.NextDouble();
                    GameObject[] group = roll < 0.30 ? shrubs : roll < 0.76 ? ground : roll < 0.93 ? deadfall : rocks;
                    GameObject prefab = Pick(group, random);
                    if (prefab == null) continue;
                    Place(prefab, groveRoot.transform, terrain, p, random, 0.85f, 1.18f, roll >= 0.76);
                    detailPositions.Add(p);
                    understory++;
                }
            }
            var meadowRoot = new GameObject("Meadow Edge Clumps");
            Undo.RegisterCreatedObjectUndo(meadowRoot, "Create meadow edges");
            meadowRoot.transform.SetParent(rootObject.transform, false);
            int meadowCount = 0;
            for (int i = 0; i < 800 && meadowCount < 64; i++)
            {
                Vector2 p = new Vector2((float)random.NextDouble() * 450 - 225, (float)random.NextDouble() * 450 - 225);
                if (!SafePoint(terrain, p) || GroveInfluence(p) > 0.25f || TooClose(detailPositions, p, 3)) continue;
                Place(ground[1], meadowRoot.transform, terrain, p, random, 0.85f, 1.15f, false);
                detailPositions.Add(p); meadowCount++;
            }
            terrain.Flush();
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Main Scene save failed.");
            Debug.Log("[RuralEnvironment] Persistent editor bake: " + trees + " canopy trees, " + understory + " understory/deadfall/rocks, " + meadowCount + " meadow clumps. Heights unchanged. Original Terrain Layers retained at indices 0–2.");
        }

        private static readonly List<Bounds> _protectedBounds = new List<Bounds>();
        private static float[,,] _roadMap;

        private static float Disturbance(Vector2 p)
        {
            float strength = 0;
            foreach (Bounds b in _protectedBounds)
            {
                float dx = Mathf.Max(0, Mathf.Abs(p.x - b.center.x) - b.extents.x);
                float dz = Mathf.Max(0, Mathf.Abs(p.y - b.center.z) - b.extents.z);
                strength = Mathf.Max(strength, (1 - Mathf.SmoothStep(0, 1, Mathf.Sqrt(dx * dx + dz * dz) / 5)) * 0.85f);
            }
            return strength;
        }

        private static float RoadStrength(Terrain terrain, Vector2 p, float radius)
        {
            if (_roadMap == null || _roadMap.GetLength(2) < 3) return 0;
            float strength = 0;
            for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++)
            {
                int ix = Mathf.Clamp(Mathf.RoundToInt((p.x + x * radius - terrain.transform.position.x) / terrain.terrainData.size.x * (_roadMap.GetLength(1) - 1)), 0, _roadMap.GetLength(1) - 1);
                int iz = Mathf.Clamp(Mathf.RoundToInt((p.y + z * radius - terrain.transform.position.z) / terrain.terrainData.size.z * (_roadMap.GetLength(0) - 1)), 0, _roadMap.GetLength(0) - 1);
                strength = Mathf.Max(strength, _roadMap[iz,ix,2]);
            }
            return strength;
        }

        private static TerrainLayer CopyLayer(string sourceName, string name, float scale)
        {
            string path = AssetRoot + "/TerrainLayers/" + name + ".terrainlayer";
            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer != null) return layer;
            var source = AssetDatabase.LoadAssetAtPath<TerrainLayer>(Pack + "Terrain Layers/" + sourceName + ".terrainlayer");
            if (source == null) throw new InvalidOperationException("Missing layer " + sourceName);
            layer = UnityEngine.Object.Instantiate(source);
            layer.name = name;
            layer.tileSize = new Vector2(scale, scale);
            Vector4 lo = layer.maskMapRemapMin, hi = layer.maskMapRemapMax;
            lo.x = hi.x = 0;
            hi.w = sourceName.Contains("Wet") ? 0.48f : 0.30f;
            layer.maskMapRemapMin = lo;
            layer.maskMapRemapMax = hi;
            layer.metallic = 0;
            layer.normalScale = 0.75f;
            layer.smoothness = sourceName.Contains("Wet") ? 0.48f : 0.30f;
            AssetDatabase.CreateAsset(layer, path);
            return layer;
        }

        private static bool SafePoint(Terrain terrain, Vector2 p)
        {
            if (Mathf.Abs(p.x) < 120 && Mathf.Abs(p.y) < 120) return false;
            if (Mathf.Abs(p.x) > 480 || Mathf.Abs(p.y) > 480) return false;
            foreach (Bounds b in _protectedBounds)
                if (Mathf.Abs(p.x - b.center.x) < b.extents.x + 12 && Mathf.Abs(p.y - b.center.z) < b.extents.z + 12) return false;
            if (RoadStrength(terrain, p, 10) > 0.15f) return false;
            TerrainData d = terrain.terrainData;
            float x = (p.x - terrain.transform.position.x) / d.size.x;
            float z = (p.y - terrain.transform.position.z) / d.size.z;
            return d.GetSteepness(x,z) < 16;
        }

        private static bool TooClose(List<Vector2> positions, Vector2 p, float distance)
        {
            foreach (Vector2 other in positions) if ((other - p).sqrMagnitude < distance * distance) return true;
            return false;
        }

        private static GameObject Pick(GameObject[] group, System.Random random)
        {
            return group == null || group.Length == 0 ? null : group[random.Next(group.Length)];
        }

        private static void Place(GameObject prefab, Transform parent, Terrain terrain, Vector2 p, System.Random random, float min, float max, bool alignSlope)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Undo.RegisterCreatedObjectUndo(instance, "Place rural detail");
            Vector3 position = new Vector3(p.x, terrain.transform.position.y + terrain.SampleHeight(new Vector3(p.x, 0, p.y)), p.y);
            Quaternion yaw = Quaternion.Euler(0, (float)random.NextDouble() * 360, 0);
            float x = (p.x - terrain.transform.position.x) / terrain.terrainData.size.x;
            float z = (p.y - terrain.transform.position.z) / terrain.terrainData.size.z;
            instance.transform.SetPositionAndRotation(position, alignSlope ? Quaternion.FromToRotation(Vector3.up, terrain.terrainData.GetInterpolatedNormal(x,z)) * yaw : yaw);
            instance.transform.localScale *= Mathf.Lerp(min, max, (float)random.NextDouble());
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (LODGroup lod in instance.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] levels = lod.GetLODs();
                float[] thresholds = {0.32f, 0.16f, 0.045f, 0.008f};
                for (int i = 0; i < levels.Length; i++) levels[i].screenRelativeTransitionHeight = thresholds[Mathf.Min(i, 3)];
                lod.SetLODs(levels);
                lod.ForceLOD(-1);
            }
            // Root pivots on these nature prefabs are ground pivots; preserve roots and buried roots.
        }
    }
}
