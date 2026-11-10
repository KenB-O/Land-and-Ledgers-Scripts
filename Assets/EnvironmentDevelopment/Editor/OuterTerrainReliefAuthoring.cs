using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LandLedgers.EnvironmentDevelopment.Editor
{
    /// <summary>
    /// Adds a single, reversible broad-relief pass to the eight authored outer tiles.
    /// It preserves each shared height edge and never changes the town center or bounds.
    /// </summary>
    public static class OuterTerrainReliefAuthoring
    {
        private const string ReportPath = "Reports/Diagnostics/OUTER_TERRAIN_RELIEF_2026-11-09.md";
        private const string AuthoredRootName = "AuthoredWorldContent";
        private const string TerrainRootName = "Terrain";
        private const string CenterName = "Pre-Authored Terrain";
        private const float EdgeFadeMetres = 160f;

        [MenuItem("Land & Ledgers/Environment/Add Gentle Rolling Relief To Outer Tiles")]
        public static void ApplyGentleRollingRelief()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Stop Play Mode before authoring regional terrain relief.");

            Scene scene = EditorSceneManager.GetActiveScene();
            if (scene.path != "Assets/Main Scene.unity")
                throw new InvalidOperationException("This bounded authoring pass only targets the saved Main Scene.");

            string absoluteReport = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ReportPath);
            if (System.IO.File.Exists(absoluteReport))
                throw new InvalidOperationException($"A relief-pass report already exists at {ReportPath}; refusing to apply the pass twice.");

            Transform authored = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                authored = root.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(candidate => string.Equals(candidate.name, AuthoredRootName, StringComparison.Ordinal));
                if (authored != null) break;
            }
            Transform terrainRoot = authored == null ? null : authored.Find(TerrainRootName);
            Terrain center = terrainRoot == null ? null : terrainRoot.Find(CenterName)?.GetComponent<Terrain>();
            if (center == null || center.terrainData == null)
                throw new InvalidOperationException($"Could not resolve the authored terrain chain (AuthoredWorldContent={authored != null}, Terrain={terrainRoot != null}, Center={center != null}). No data was changed.");

            Terrain[] outer = terrainRoot.GetComponentsInChildren<Terrain>(true)
                .Where(tile => tile != center)
                .OrderBy(tile => tile.name, StringComparer.Ordinal)
                .ToArray();
            if (outer.Length != 8)
                throw new InvalidOperationException($"Expected eight outer terrain tiles; found {outer.Length}. No data was changed.");

            var unique = new System.Collections.Generic.HashSet<TerrainData> { center.terrainData };
            foreach (Terrain tile in outer)
            {
                if (!tile.gameObject.activeInHierarchy || !tile.enabled || tile.terrainData == null || !unique.Add(tile.terrainData))
                    throw new InvalidOperationException($"{tile.name} is inactive, disabled, missing TerrainData, or shares data. No data was changed.");
                if (Mathf.Abs(tile.terrainData.size.x - center.terrainData.size.x) > 0.01f
                    || Mathf.Abs(tile.terrainData.size.z - center.terrainData.size.z) > 0.01f
                    || tile.terrainData.heightmapResolution != center.terrainData.heightmapResolution)
                    throw new InvalidOperationException($"{tile.name} dimensions/resolution differ from the center. No data was changed.");
            }

            var before = outer.Select(Measure).ToArray();
            Undo.RegisterCompleteObjectUndo(outer.Select(tile => (UnityEngine.Object)tile.terrainData).ToArray(),
                "Add gentle rolling relief to outer regional terrain");
            var reports = new StringBuilder();
            reports.AppendLine("# Outer Terrain Relief Pass — 2026-11-09");
            reports.AppendLine();
            reports.AppendLine("One-time deterministic broad-relief authoring pass for the eight existing outer TerrainData assets. The authored town-center TerrainData and Main Scene transforms are not modified.");
            reports.AppendLine();
            reports.AppendLine("The added field combines world-coordinate Perlin variation at approximately 625 m and 250 m scales. The theoretical maximum contribution is ±4 m, while the sampled Perlin range is expected to produce gentler relief near ±2 m. A 160 m smooth fade reaches zero on every tile edge, preserving all shared edge samples and producing a gradual transition near the town tile. The pass creates no sharp peaks, changes no tile dimensions, collider bounds, terrain positions, scene objects, road logic, or simulation/pathfinding bounds.");
            reports.AppendLine();
            reports.AppendLine("| Tile | Before elevation range (m) | After elevation range (m) | Max absolute change (m) | Sampled peak slope after (degrees) |");
            reports.AppendLine("|---|---:|---:|---:|---:|");

            try
            {
                for (int tileIndex = 0; tileIndex < outer.Length; tileIndex++)
                {
                    Terrain tile = outer[tileIndex];
                    TerrainData data = tile.terrainData;
                    float[,] oldHeights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
                    float[,] newHeights = (float[,])oldHeights.Clone();
                    float maxChange = 0f;
                    float sizeY = data.size.y;
                    Vector3 position = tile.transform.position;
                    int last = data.heightmapResolution - 1;

                    for (int z = 0; z <= last; z++)
                    {
                        float nz = z / (float)last;
                        float worldZ = position.z + nz * data.size.z;
                        for (int x = 0; x <= last; x++)
                        {
                            float nx = x / (float)last;
                            float distanceToEdge = Mathf.Min(nx * data.size.x, (1f - nx) * data.size.x,
                                nz * data.size.z, (1f - nz) * data.size.z);
                            float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distanceToEdge / EdgeFadeMetres));
                            if (fade <= 0f) continue;

                            float worldX = position.x + nx * data.size.x;
                            float broad = Mathf.PerlinNoise((worldX + 1371.4f) * 0.0016f, (worldZ - 821.7f) * 0.0016f);
                            float rolling = Mathf.PerlinNoise((worldX - 433.2f) * 0.004f, (worldZ + 1297.3f) * 0.004f);
                            float metres = ((broad - 0.5f) * 6f + (rolling - 0.5f) * 2f) * fade;
                            float normalizedChange = metres / sizeY;
                            newHeights[z, x] = Mathf.Clamp01(oldHeights[z, x] + normalizedChange);
                            maxChange = Mathf.Max(maxChange, Mathf.Abs(newHeights[z, x] - oldHeights[z, x]) * sizeY);
                        }
                    }

                    data.SetHeights(0, 0, newHeights);
                    tile.Flush();
                    EditorUtility.SetDirty(data);
                    TerrainMeasure after = Measure(tile);
                    reports.AppendLine($"| {tile.name} | {before[tileIndex].Range:0.###} | {after.Range:0.###} | {maxChange:0.###} | {after.PeakSlope:0.###} |");
                    Debug.Log($"[OuterTerrainRelief] {tile.name}: broad relief applied; elevation range {before[tileIndex].Range:0.##}m -> {after.Range:0.##}m, peak slope {after.PeakSlope:0.##}°.");
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                reports.AppendLine();
                reports.AppendLine("## Verification");
                reports.AppendLine();
                reports.AppendLine("- The center TerrainData was excluded and retains the same asset reference and serialized content.");
                reports.AppendLine("- All tile positions and TerrainData dimensions are unchanged; terrain colliders remain enabled and use their existing TerrainData references.");
                reports.AppendLine("- The relief contribution is zero at shared edges. Run `Land & Ledgers > Environment > Audit Regional Terrain Layout` after import to remeasure every shared seam and confirm the reciprocal neighbor graph.");
                reports.AppendLine("- Ground painting should be refreshed with `Land & Ledgers > Environment > Paint Natural Ground On Outer Tiles` so the existing slope-aware palette responds to the new relief.");
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(absoluteReport));
                System.IO.File.WriteAllText(absoluteReport, reports.ToString(), new UTF8Encoding(false));
                EditorUtility.DisplayDialog("Outer terrain relief saved", "Added gentle, edge-faded rolling relief to the eight outer terrains. The town-center terrain, tile positions, and simulation bounds were left unchanged. Refresh the outer ground palette, then run the terrain layout audit.", "Close");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static TerrainMeasure Measure(Terrain terrain)
        {
            TerrainData data = terrain.terrainData;
            float[,] heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            for (int z = 0; z < data.heightmapResolution; z++)
            for (int x = 0; x < data.heightmapResolution; x++)
            {
                float height = terrain.transform.position.y + heights[z, x] * data.size.y;
                min = Mathf.Min(min, height);
                max = Mathf.Max(max, height);
            }
            float peakSlope = 0f;
            int last = data.heightmapResolution - 1;
            for (int z = 0; z <= last; z += 4)
            for (int x = 0; x <= last; x += 4)
                peakSlope = Mathf.Max(peakSlope, data.GetSteepness(x / (float)last, z / (float)last));
            return new TerrainMeasure { Range = max - min, PeakSlope = peakSlope };
        }

        private struct TerrainMeasure
        {
            public float Range;
            public float PeakSlope;
        }
    }
}
