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
    /// <summary>Read-only terrain layout audit and bounded outer-tile ground authoring.</summary>
    public static class RegionalTerrainAuditAndPaint
    {
        private const string ReportPath = "Reports/Diagnostics/OUTER_TERRAIN_AUDIT_2026-11-09.md";
        private const string LayerRoot = "Assets/EnvironmentDevelopment/TerrainLayers/";
        private const string CenterName = "Pre-Authored Terrain";
        private const string OuterLayerRoot = LayerRoot + "OuterTiles/";

        private static readonly Dictionary<string, float> OuterLayerScales = new Dictionary<string, float>
        {
            { "Meadow Grass", 4.2f },
            { "Disturbed Soil", 3.9f },
            { "Grove Leaf Litter", 4.8f },
            { "Damp Lowland Earth", 4.5f },
            { "Exposed Stones", 5.25f }
        };

        [MenuItem("Land & Ledgers/Environment/Audit Regional Terrain Layout")]
        public static void Audit()
        {
            Terrain[] terrains = UnityEngine.Object.FindObjectsByType<Terrain>();
            Array.Sort(terrains, (a, b) => string.CompareOrdinal(a.name, b.name));
            var sb = new StringBuilder();
            sb.AppendLine("# Regional Terrain Layout Audit — 2026-11-09");
            sb.AppendLine();
            sb.AppendLine("Read-only Editor capture. No TerrainData or scene changes were made by this command.");
            sb.AppendLine();
            sb.AppendLine("| Terrain | Position (m) | Dimensions (m) | Heightmap | Alphamap | Trees | Detail layers | Terrain layers |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---|");
            foreach (Terrain terrain in terrains)
            {
                TerrainData data = terrain.terrainData;
                if (data == null) continue;
                string layers = string.Join(", ", data.terrainLayers.Select(layer => layer == null ? "<null>" : $"{layer.name} ({layer.tileSize.x:0.#}m)"));
                int detailLayers = data.detailPrototypes == null ? 0 : data.detailPrototypes.Length;
                sb.AppendLine($"| {terrain.name} | {terrain.transform.position.x:0.###}, {terrain.transform.position.y:0.###}, {terrain.transform.position.z:0.###} | {data.size.x:0.###} × {data.size.y:0.###} × {data.size.z:0.###} | {data.heightmapResolution} | {data.alphamapResolution} | {data.treeInstances.Length} | {detailLayers} | {layers} |");
                float[,] heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
                float lo = 1f, hi = 0f;
                for (int z = 0; z < data.heightmapResolution; z++)
                for (int x = 0; x < data.heightmapResolution; x++)
                {
                    float h = heights[z, x];
                    if (h < lo) lo = h;
                    if (h > hi) hi = h;
                }
                sb.AppendLine($"  - Height range: {(hi - lo) * data.size.y:0.###} m; serialized asset: `{AssetDatabase.GetAssetPath(data)}`.");
                sb.AppendLine($"  - Neighbors L/R/T/B: {NameOrNone(terrain.leftNeighbor)} / {NameOrNone(terrain.rightNeighbor)} / {NameOrNone(terrain.topNeighbor)} / {NameOrNone(terrain.bottomNeighbor)}.");
            }

            if (terrains.Length > 1)
            {
                sb.AppendLine();
                sb.AppendLine("## Shared-edge height differences");
                sb.AppendLine();
                for (int leftIndex = 0; leftIndex < terrains.Length; leftIndex++)
                for (int rightIndex = leftIndex + 1; rightIndex < terrains.Length; rightIndex++)
                {
                    Terrain first = terrains[leftIndex];
                    Terrain second = terrains[rightIndex];
                    if (first.terrainData == null || second.terrainData == null) continue;
                    Vector3 a = first.transform.position;
                    Vector3 b = second.transform.position;
                    Vector3 asz = first.terrainData.size;
                    Vector3 bsz = second.terrainData.size;
                    float overlapMinZ = Mathf.Max(a.z, b.z);
                    float overlapMaxZ = Mathf.Min(a.z + asz.z, b.z + bsz.z);
                    float overlapMinX = Mathf.Max(a.x, b.x);
                    float overlapMaxX = Mathf.Min(a.x + asz.x, b.x + bsz.x);
                    bool vertical = overlapMaxZ - overlapMinZ > 0.02f
                        && (Mathf.Abs((a.x + asz.x) - b.x) < 0.02f || Mathf.Abs((b.x + bsz.x) - a.x) < 0.02f);
                    bool horizontal = overlapMaxX - overlapMinX > 0.02f
                        && (Mathf.Abs((a.z + asz.z) - b.z) < 0.02f || Mathf.Abs((b.z + bsz.z) - a.z) < 0.02f);
                    if (!vertical && !horizontal) continue; // Corner-only contacts are not shared edges.
                    float edgeX = Mathf.Abs((a.x + asz.x) - b.x) < 0.02f ? a.x + asz.x : a.x;
                    float edgeZ = Mathf.Abs((a.z + asz.z) - b.z) < 0.02f ? a.z + asz.z : a.z;
                    float spanMin = vertical ? overlapMinZ : overlapMinX;
                    float spanMax = vertical ? overlapMaxZ : overlapMaxX;
                    float max = 0, total = 0;
                    int samples = 65;
                    for (int i = 0; i < samples; i++)
                    {
                        float span = Mathf.Lerp(spanMin, spanMax, i / (float)(samples - 1));
                        float worldX = vertical ? edgeX : span;
                        float worldZ = vertical ? span : edgeZ;
                        float ax = Mathf.Clamp01((worldX - a.x) / asz.x);
                        float az = Mathf.Clamp01((worldZ - a.z) / asz.z);
                        float bx = Mathf.Clamp01((worldX - b.x) / bsz.x);
                        float bz = Mathf.Clamp01((worldZ - b.z) / bsz.z);
                        float ha = a.y + first.terrainData.GetInterpolatedHeight(ax, az);
                        float hb = b.y + second.terrainData.GetInterpolatedHeight(bx, bz);
                        float delta = Mathf.Abs(ha - hb);
                        max = Mathf.Max(max, delta);
                        total += delta;
                    }
                    sb.AppendLine($"- {first.name} ↔ {second.name}: mean {total / samples:0.###} m, max {max:0.###} m over {samples} samples.");
                }
            }
            global::System.IO.Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"[RegionalTerrainAudit] Wrote read-only terrain audit to {ReportPath}; found {terrains.Length} terrain object(s).");
            EditorUtility.DisplayDialog("Regional terrain audit complete", $"Found {terrains.Length} Terrain objects. Read the report at {ReportPath}.", "Close");
        }

        [MenuItem("Land & Ledgers/Environment/Paint Natural Ground On Outer Tiles")]
        public static void PaintOuterTiles()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Paint outer terrain in Edit Mode only.");
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != "Assets/Main Scene.unity") throw new InvalidOperationException("This operation targets Main Scene only.");

            Transform authored = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == "AuthoredWorldContent") authored = root.transform;
            Transform terrainRoot = authored == null ? null : authored.Find("Terrain");
            Terrain center = terrainRoot == null ? null : terrainRoot.Find(CenterName)?.GetComponent<Terrain>();
            if (center == null || center.terrainData == null) throw new InvalidOperationException("The authored center terrain was not found.");

            Terrain[] outer = terrainRoot.GetComponentsInChildren<Terrain>(true)
                .Where(t => t != center).OrderBy(t => t.name, StringComparer.Ordinal).ToArray();
            if (outer.Length != 8) throw new InvalidOperationException($"Expected eight existing outer tiles; found {outer.Length}. No data was changed.");
            TerrainData centerData = center.terrainData;
            if (Mathf.Abs(centerData.size.x - 1024f) > 0.1f || Mathf.Abs(centerData.size.z - 1024f) > 0.1f)
                throw new InvalidOperationException("Expected the authored center to be 1024m × 1024m. No data was changed.");

            var uniqueData = new HashSet<TerrainData> { centerData };
            foreach (Terrain tile in outer)
            {
                if (tile.terrainData == null || !uniqueData.Add(tile.terrainData))
                    throw new InvalidOperationException($"{tile.name} has missing or shared TerrainData. No data was changed.");
                if (Mathf.Abs(tile.terrainData.size.x - centerData.size.x) > 0.1f || Mathf.Abs(tile.terrainData.size.z - centerData.size.z) > 0.1f)
                    throw new InvalidOperationException($"{tile.name} dimensions do not match the center. No data was changed.");
            }

            TerrainLayer[] sourceLayers = centerData.terrainLayers;
            if (sourceLayers.Length < 8) throw new InvalidOperationException("Center terrain does not contain the five authored rural ground layers.");
            int meadow = FindLayer(sourceLayers, "Meadow Grass");
            int soil = FindLayer(sourceLayers, "Disturbed Soil");
            int leaf = FindLayer(sourceLayers, "Grove Leaf Litter");
            int damp = FindLayer(sourceLayers, "Damp Lowland Earth");
            int stone = FindLayer(sourceLayers, "Exposed Stones");
            if (meadow < 0 || soil < 0 || leaf < 0 || damp < 0 || stone < 0)
                throw new InvalidOperationException("One or more authored rural ground layers are missing. No data was changed.");

            if (!AssetDatabase.IsValidFolder(OuterLayerRoot.TrimEnd('/')))
                AssetDatabase.CreateFolder(LayerRoot.TrimEnd('/'), "OuterTiles");
            TerrainLayer[] outerLayers = (TerrainLayer[])sourceLayers.Clone();
            foreach (var kvp in OuterLayerScales)
            {
                int index = FindLayer(sourceLayers, kvp.Key);
                string assetPath = OuterLayerRoot + "Outer " + kvp.Key + ".terrainlayer";
                TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(assetPath);
                if (layer == null)
                {
                    layer = UnityEngine.Object.Instantiate(sourceLayers[index]);
                    layer.name = "Outer " + kvp.Key;
                    layer.tileSize = Vector2.one * kvp.Value;
                    AssetDatabase.CreateAsset(layer, assetPath);
                }
                outerLayers[index] = layer;
            }
            AssetDatabase.SaveAssets();

            var treePositions = new List<Vector2>();
            Transform[] vegetationRoots =
            {
                authored.Find("Regional Countryside Vegetation"),
                authored.Find("Outer Regional Woodlots")
            };
            foreach (Transform vegetationRoot in vegetationRoots)
            {
                if (vegetationRoot == null) continue;
                foreach (Transform child in vegetationRoot.GetComponentsInChildren<Transform>(true))
                {
                    string lowerName = child.name.ToLowerInvariant();
                    if (!lowerName.Contains("prefab_beech_tree")) continue;
                    treePositions.Add(new Vector2(child.position.x, child.position.z));
                }
            }

            float lowestWorldHeight = float.PositiveInfinity;
            float highestWorldHeight = float.NegativeInfinity;
            foreach (Terrain tile in outer)
            {
                TerrainData data = tile.terrainData;
                float[,] heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
                for (int z = 0; z < data.heightmapResolution; z++)
                for (int x = 0; x < data.heightmapResolution; x++)
                {
                    float y = tile.transform.position.y + heights[z, x] * data.size.y;
                    lowestWorldHeight = Mathf.Min(lowestWorldHeight, y);
                    highestWorldHeight = Mathf.Max(highestWorldHeight, y);
                }
            }

            float[,,] centerMap = centerData.GetAlphamaps(0, 0, centerData.alphamapWidth, centerData.alphamapHeight);
            Undo.RecordObjects(outer.Select(t => (UnityEngine.Object)t.terrainData).ToArray(), "Paint natural ground on regional tiles");
            int painted = 0;
            foreach (Terrain tile in outer)
            {
                TerrainData data = tile.terrainData;
                int resolution = data.alphamapResolution;
                float[,,] map = new float[resolution, resolution, outerLayers.Length];
                float slopeMax = 0;
                var nearbyTrees = treePositions.Where(p => p.x >= tile.transform.position.x - 42f
                    && p.x <= tile.transform.position.x + data.size.x + 42f
                    && p.y >= tile.transform.position.z - 42f
                    && p.y <= tile.transform.position.z + data.size.z + 42f).ToArray();

                for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                {
                    float nx = x / (float)(resolution - 1);
                    float nz = z / (float)(resolution - 1);
                    float worldX = tile.transform.position.x + nx * data.size.x;
                    float worldZ = tile.transform.position.z + nz * data.size.z;
                    float worldY = tile.transform.position.y + data.GetInterpolatedHeight(nx, nz);
                    float slope = data.GetSteepness(nx, nz);
                    slopeMax = Mathf.Max(slopeMax, slope);

                    float broad = Mathf.PerlinNoise(worldX * 0.0027f + 73.2f, worldZ * 0.0027f + 26.8f);
                    float fine = Mathf.PerlinNoise(worldX * 0.012f + 51.3f, worldZ * 0.012f + 19.1f);
                    float dry = Mathf.SmoothStep(0.43f, 0.70f, broad);
                    float lowland = 1f - Mathf.SmoothStep(lowestWorldHeight + 2.5f, lowestWorldHeight + 8f, worldY);
                    float treeCover = 0f;
                    foreach (Vector2 tree in nearbyTrees)
                    {
                        float distance = Vector2.Distance(new Vector2(worldX, worldZ), tree);
                        treeCover = Mathf.Max(treeCover, 1f - Mathf.SmoothStep(7f, 23f, distance));
                    }
                    float canopyMottle = Mathf.Lerp(0.72f, 1f, fine);
                    float litterWeight = 0.015f + treeCover * 0.54f * canopyMottle;
                    float dampWeight = 0.025f + lowland * (1f - dry * 0.25f) * 0.20f;
                    float soilWeight = 0.035f + dry * 0.12f + fine * 0.045f;
                    float rockWeight = Mathf.Clamp01((slope - 10f) / 20f) * 0.24f;
                    float meadowWeight = Mathf.Max(0.08f, 1f - litterWeight - dampWeight - soilWeight - rockWeight);
                    float total = meadowWeight + litterWeight + dampWeight + soilWeight + rockWeight;
                    meadowWeight /= total; litterWeight /= total; dampWeight /= total; soilWeight /= total; rockWeight /= total;

                    // Feather the existing town ground palette outward for 80m at the center seam.
                    float cx = Mathf.Clamp(worldX, center.transform.position.x, center.transform.position.x + centerData.size.x);
                    float cz = Mathf.Clamp(worldZ, center.transform.position.z, center.transform.position.z + centerData.size.z);
                    float outsideDistance = Vector2.Distance(new Vector2(worldX, worldZ), new Vector2(cx, cz));
                    float centerBlend = 1f - Mathf.SmoothStep(0f, 80f, outsideDistance);
                    int ci = Mathf.Clamp(Mathf.RoundToInt((cx - center.transform.position.x) / centerData.size.x * (centerData.alphamapWidth - 1)), 0, centerData.alphamapWidth - 1);
                    int czIndex = Mathf.Clamp(Mathf.RoundToInt((cz - center.transform.position.z) / centerData.size.z * (centerData.alphamapHeight - 1)), 0, centerData.alphamapHeight - 1);

                    for (int layer = 0; layer < outerLayers.Length; layer++)
                    {
                        float ruralWeight = layer == meadow ? meadowWeight
                            : layer == soil ? soilWeight
                            : layer == leaf ? litterWeight
                            : layer == damp ? dampWeight
                            : layer == stone ? rockWeight : 0f;
                        float boundaryWeight = centerMap[czIndex, ci, layer];
                        map[z, x, layer] = Mathf.Lerp(ruralWeight, boundaryWeight, centerBlend);
                    }
                }

                data.terrainLayers = outerLayers;
                data.SetAlphamaps(0, 0, map);
                EditorUtility.SetDirty(data);
                tile.Flush();
                Debug.Log($"[RegionalTerrainPaint] {tile.name}: painted {resolution}² ground map with {nearbyTrees.Length} nearby authored canopy trees; peak slope {slopeMax:0.0}°. Heightmap, collider, dimensions, and bounds unchanged.");
                painted++;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[RegionalTerrainPaint] Completed natural ground variation on {painted} outer tiles. Center terrain and scene objects were not changed.");
            EditorUtility.DisplayDialog("Outer terrain ground painted", $"Applied the existing rural ground palette to {painted} outer tiles. The center town tile, heights, colliders and world bounds were left unchanged.", "Close");
        }

        private static int FindLayer(TerrainLayer[] layers, string name)
        {
            for (int i = 0; i < layers.Length; i++)
                if (layers[i] != null && string.Equals(layers[i].name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private static string NameOrNone(Terrain terrain) => terrain == null ? "none" : terrain.name;
    }
}
