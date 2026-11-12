using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LandLedgers.EnvironmentDevelopment.Editor
{
    /// <summary>
    /// Persistent, deterministic woodland dressing for the eight authored regional terrain tiles.
    /// This affects presentation only and never changes TerrainData or simulation bounds.
    /// </summary>
    public static class OuterWoodlandAuthoring
    {
        private const string AuthoredRootName = "AuthoredWorldContent";
        private const string TerrainRootName = "Terrain";
        private const string WoodlandRootName = "Outer Regional Woodlots";
        private const string ReportPath = "Reports/Diagnostics/OUTER_WOODLAND_PASS_2026-11-09.md";
        private const string CoverageReportPath = "Reports/Diagnostics/OUTER_WOODLAND_COVERAGE_2026-11-09.md";

        private const string Tree02 = "Assets/Environment/Vegetation/Trees/prefab_beech_tree_02.prefab";
        private const string Tree05 = "Assets/Environment/Vegetation/Trees/prefab_beech_tree_05.prefab";
        private const string Tree08 = "Assets/Environment/Vegetation/Trees/prefab_beech_tree_08.prefab";
        private const string Sapling02 = "Assets/Environment/Vegetation/Trees/prefab_beech_plant_02.prefab";
        private const string CherryShrub = "Assets/Environment/Vegetation/Shrubs/Prefab_Forest_black_cherry_02.prefab";
        private const string Fern = "Assets/Asset Packs/NatureManufacture Assets/Forest Environment Dynamic Nature/Foliage and Grass/Prefabs/prefab_fern_01_2.prefab";
        private const string Grass = "Assets/Asset Packs/NatureManufacture Assets/Forest Environment Dynamic Nature/Foliage and Grass/Prefabs/prefab_grass_02_2.prefab";
        private const string Log = "Assets/Asset Packs/NatureManufacture Assets/Forest Environment Dynamic Nature/Stumps Roots and Branches/Prefabs/prefab_dead_log_03.prefab";
        private const string Stone = "Assets/Environment/Props/SmallRock02.prefab";

        [MenuItem("Land & Ledgers/Environment/Audit Outer Woodland Coverage")]
        public static void AuditCoverage()
        {
            SceneSetup();
            Transform authored = FindAuthoredRoot();
            Transform terrainRoot = authored.Find(TerrainRootName);
            Terrain[] tiles = terrainRoot.GetComponentsInChildren<Terrain>(true)
                .Where(t => t != null && t.name != "Pre-Authored Terrain")
                .OrderBy(t => t.name, StringComparer.Ordinal).ToArray();
            var sb = new StringBuilder();
            sb.AppendLine("# Outer Woodland Coverage — 2026-11-09");
            sb.AppendLine();
            sb.AppendLine("Read-only scene inventory. No assets were changed by this command.");
            sb.AppendLine();
            sb.AppendLine("| Tile | Existing authored vegetation renderers | Height range (m) | Position (m) | Dimensions (m) |");
            sb.AppendLine("|---|---:|---:|---|---|");
            Transform[] vegetationRoots =
            {
                authored.Find("Rural Landscape Dressing"),
                authored.Find("Regional Countryside Vegetation"),
                authored.Find(WoodlandRootName)
            };
            foreach (Terrain tile in tiles)
            {
                int rendererCount = 0;
                foreach (Transform vegetationRoot in vegetationRoots)
                {
                    if (vegetationRoot == null) continue;
                    rendererCount += vegetationRoot.GetComponentsInChildren<Renderer>(true)
                        .Count(r => tile.terrainData.bounds.Contains(tile.transform.InverseTransformPoint(r.bounds.center)));
                }
                float min = float.PositiveInfinity;
                float max = float.NegativeInfinity;
                TerrainData data = tile.terrainData;
                float[,] heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
                for (int z = 0; z < data.heightmapResolution; z++)
                for (int x = 0; x < data.heightmapResolution; x++)
                {
                    float h = tile.transform.position.y + heights[z, x] * data.size.y;
                    min = Mathf.Min(min, h);
                    max = Mathf.Max(max, h);
                }
                sb.AppendLine($"| {tile.name} | {rendererCount} | {min:0.##}–{max:0.##} | {tile.transform.position.x:0.##}, {tile.transform.position.y:0.##}, {tile.transform.position.z:0.##} | {data.size.x:0.##} × {data.size.y:0.##} × {data.size.z:0.##} |");
            }
            if (authored.Find(WoodlandRootName) != null)
                sb.AppendLine($"\nExisting `{WoodlandRootName}` root found; the authoring command will refuse to duplicate it.");
            WriteReport(CoverageReportPath, sb.ToString());
            Debug.Log($"[OuterWoodland] Coverage audit complete for {tiles.Length} outer terrain tiles. Report: {CoverageReportPath}");
        }

        [MenuItem("Land & Ledgers/Environment/Author Natural Woodlots On Outer Tiles")]
        public static void AuthorWoodlots()
        {
            SceneSetup();
            Transform authored = FindAuthoredRoot();
            Transform terrainRoot = authored.Find(TerrainRootName);
            Terrain[] tiles = terrainRoot.GetComponentsInChildren<Terrain>(true)
                .Where(t => t != null && t.name != "Pre-Authored Terrain")
                .OrderBy(t => t.name, StringComparer.Ordinal).ToArray();
            if (tiles.Length != 8)
                throw new InvalidOperationException($"Expected eight outer terrain tiles; found {tiles.Length}. No scene objects were created.");
            if (authored.Find(WoodlandRootName) != null)
                throw new InvalidOperationException($"`{WoodlandRootName}` already exists. Refusing to duplicate persistent vegetation.");

            GameObject treeA = LoadPrefab(Tree02);
            GameObject treeB = LoadPrefab(Tree05);
            GameObject treeC = LoadPrefab(Tree08);
            GameObject sapling = LoadPrefab(Sapling02);
            GameObject shrub = LoadPrefab(CherryShrub);
            GameObject fern = LoadPrefab(Fern);
            GameObject grass = LoadPrefab(Grass);
            GameObject log = LoadPrefab(Log);
            GameObject stone = LoadPrefab(Stone);
            GameObject[] canopyPrefabs = { treeA, treeB, treeC };
            GameObject[] understoryPrefabs = { sapling, shrub, shrub, fern, fern, grass, log, stone };

            var root = new GameObject(WoodlandRootName);
            Undo.RegisterCreatedObjectUndo(root, "Author persistent outer regional woodlots");
            root.transform.SetParent(authored, false);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            int treeCount = 0;
            int understoryCount = 0;
            var tileSummaries = new List<string>();
            for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
            {
                Terrain tile = tiles[tileIndex];
                TerrainData data = tile.terrainData;
                if (data == null || data.size.x < 256f || data.size.z < 256f)
                    throw new InvalidOperationException($"{tile.name} has unexpected TerrainData. Review and undo the just-created empty root before retrying.");

                var tileRoot = new GameObject(tile.name + " Woodland");
                Undo.RegisterCreatedObjectUndo(tileRoot, "Create regional woodland group");
                tileRoot.transform.SetParent(root.transform, false);
                var canopyRoot = new GameObject("Canopy");
                Undo.RegisterCreatedObjectUndo(canopyRoot, "Create regional canopy group");
                canopyRoot.transform.SetParent(tileRoot.transform, false);
                var understoryRoot = new GameObject("Forest Floor and Edge");
                Undo.RegisterCreatedObjectUndo(understoryRoot, "Create regional forest floor group");
                understoryRoot.transform.SetParent(tileRoot.transform, false);

                var random = new System.Random(18971109 + tileIndex * 7919);
                Vector2[] clusterCenters = ChooseClusterCenters(data, tileIndex, random);
                var placedTrees = new List<Vector2>();
                int localTrees = 0;
                int localUnderstory = 0;

                for (int clusterIndex = 0; clusterIndex < clusterCenters.Length; clusterIndex++)
                {
                    Vector2 center = clusterCenters[clusterIndex];
                    int targetTrees = clusterIndex == 0 ? 9 : 7;
                    for (int attempt = 0; attempt < 600 && localTrees < (clusterIndex == 0 ? 9 : 9 + 7 * clusterIndex); attempt++)
                    {
                        float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                        float radius = Mathf.Sqrt((float)random.NextDouble()) * (clusterIndex == 0 ? 56f : 43f);
                        Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                        if (!IsValidPoint(tile, point, 22f) || TooClose(placedTrees, point, 10.5f)) continue;
                        GameObject prefab = canopyPrefabs[random.Next(canopyPrefabs.Length)];
                        Place(prefab, canopyRoot.transform, tile, point, random, 0.88f, 1.14f);
                        placedTrees.Add(point);
                        localTrees++;
                    }

                    int targetUnderstory = clusterIndex == 0 ? 8 : 7;
                    var placedDetails = new List<Vector2>();
                    for (int attempt = 0; attempt < 700 && localUnderstory < (clusterIndex == 0 ? 8 : 8 + 7 * clusterIndex); attempt++)
                    {
                        float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                        float radius = Mathf.Sqrt((float)random.NextDouble()) * (clusterIndex == 0 ? 67f : 55f);
                        Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                        if (!IsValidPoint(tile, point, 28f) || TooClose(placedTrees, point, 3f) || TooClose(placedDetails, point, 5f)) continue;
                        Place(understoryPrefabs[random.Next(understoryPrefabs.Length)], understoryRoot.transform, tile, point, random, 0.82f, 1.17f);
                        placedDetails.Add(point);
                        localUnderstory++;
                    }
                }

                treeCount += localTrees;
                understoryCount += localUnderstory;
                tileSummaries.Add($"- {tile.name}: {localTrees} canopy trees, {localUnderstory} forest-floor/edge instances in {clusterCenters.Length} irregular copses.");
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            if (!EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()))
                throw new IOException("Main Scene save failed after woodlot creation. The complete pre-pass scene checkpoint is preserved outside the project.");
            AssetDatabase.SaveAssets();

            var report = new StringBuilder();
            report.AppendLine("# Outer Regional Woodlots — 2026-11-09");
            report.AppendLine();
            report.AppendLine("## Changes");
            report.AppendLine();
            report.AppendLine($"Added {treeCount} persistent canopy trees and {understoryCount} forest-floor/edge prefabs to the eight existing outer tiles. Placements use a fixed per-tile seed, irregular two-copse layouts, terrain-sampled ground height, slope checks, LOD-capable source prefabs, and no new physics colliders.");
            report.AppendLine("The authored center terrain, terrain heights, dimensions, neighbors, TerrainData, roads, opening anchor, startup profile, simulation bounds, and pre-existing vegetation roots were not altered by this command.");
            report.AppendLine();
            report.AppendLine("## Per-tile placement");
            report.AppendLine();
            foreach (string line in tileSummaries) report.AppendLine(line);
            report.AppendLine();
            report.AppendLine("## Validation status");
            report.AppendLine();
            report.AppendLine("- Authored as persistent scene prefab instances, visible in Edit Mode.");
            report.AppendLine("- Source prefabs are reused; source prefab assets are not modified.");
            report.AppendLine("- Decorative colliders are disabled on instances to avoid altering navigation and collision behavior.");
            report.AppendLine("- Unity scene save was requested and completed. Daytime Game-view comparison and runtime frame-time profiling remain to be checked.");
            WriteReport(ReportPath, report.ToString());
            Debug.Log($"[OuterWoodland] Saved persistent outer regional woodland: {treeCount} trees + {understoryCount} forest-floor/edge instances across {tiles.Length} tiles. Report: {ReportPath}");
        }

        private static void SceneSetup()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Outer woodland authoring is Edit Mode only.");
            if (EditorSceneManager.GetActiveScene().path != "Assets/Main Scene.unity")
                throw new InvalidOperationException("This authoring pass targets the open Main Scene only.");
        }

        private static Transform FindAuthoredRoot()
        {
            Transform result = null;
            foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == AuthoredRootName) result = root.transform;
            if (result == null) throw new InvalidOperationException("AuthoredWorldContent was not found. No scene objects were created.");
            Transform terrainRoot = result.Find(TerrainRootName);
            if (terrainRoot == null) throw new InvalidOperationException("Authored terrain group was not found. No scene objects were created.");
            return result;
        }

        private static GameObject LoadPrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new FileNotFoundException("Required existing environment prefab is missing; no vegetation was created.", path);
            return prefab;
        }

        private static Vector2[] ChooseClusterCenters(TerrainData data, int index, System.Random random)
        {
            float margin = 165f;
            float minX = margin;
            float maxX = data.size.x - margin;
            float minZ = margin;
            float maxZ = data.size.z - margin;
            float baseU = ((index * 0.371f) % 0.34f) + 0.31f;
            float baseV = ((index * 0.227f) % 0.34f) + 0.31f;
            Vector2 a = new Vector2(Mathf.Lerp(minX, maxX, baseU), Mathf.Lerp(minZ, maxZ, baseV));
            Vector2 b;
            int tries = 0;
            do
            {
                float u = Mathf.Lerp(0.22f, 0.78f, (float)random.NextDouble());
                float v = Mathf.Lerp(0.22f, 0.78f, (float)random.NextDouble());
                b = new Vector2(Mathf.Lerp(minX, maxX, u), Mathf.Lerp(minZ, maxZ, v));
                tries++;
            } while (Vector2.Distance(a, b) < 270f && tries < 40);
            return new[] { a, b };
        }

        private static bool IsValidPoint(Terrain tile, Vector2 point, float edgeMargin)
        {
            TerrainData data = tile.terrainData;
            if (point.x < edgeMargin || point.y < edgeMargin || point.x > data.size.x - edgeMargin || point.y > data.size.z - edgeMargin) return false;
            float nx = point.x / data.size.x;
            float nz = point.y / data.size.z;
            return data.GetSteepness(nx, nz) < 24f;
        }

        private static bool TooClose(List<Vector2> points, Vector2 point, float minimumDistance)
        {
            float minSquared = minimumDistance * minimumDistance;
            foreach (Vector2 other in points)
                if ((other - point).sqrMagnitude < minSquared) return true;
            return false;
        }

        private static void Place(GameObject prefab, Transform parent, Terrain terrain, Vector2 localXZ, System.Random random, float minScale, float maxScale)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Undo.RegisterCreatedObjectUndo(instance, "Place persistent outer vegetation");
            float nx = localXZ.x / terrain.terrainData.size.x;
            float nz = localXZ.y / terrain.terrainData.size.z;
            float y = terrain.transform.position.y + terrain.terrainData.GetInterpolatedHeight(nx, nz);
            Vector3 world = new Vector3(terrain.transform.position.x + localXZ.x, y, terrain.transform.position.z + localXZ.y);
            instance.transform.SetPositionAndRotation(world, Quaternion.Euler(0, (float)random.NextDouble() * 360f, 0));
            instance.transform.localScale *= Mathf.Lerp(minScale, maxScale, (float)random.NextDouble());
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        }

        private static void WriteReport(string path, string report)
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, report, new UTF8Encoding(false));
        }
    }
}
