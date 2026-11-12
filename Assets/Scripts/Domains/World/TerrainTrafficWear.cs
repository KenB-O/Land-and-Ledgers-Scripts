using System;
using System.Collections.Generic;
using LandLedgers.Economy.Transport;
using LandLedgers.Pathing;
using UnityEngine;

namespace LandLedgers.World
{
    /// <summary>
    /// Runtime-only terrain wear driven by measured presentation movement.
    /// TerrainData is cloned in Play Mode so traffic never writes into an authored asset.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerrainTrafficWear : MonoBehaviour
    {
        public enum TrafficKind
        {
            Pedestrian = 0,
            Wagon = 1
        }

        private const int RuntimeAlphamapResolution = 1024;
        private const int UpdateTileSize = 16;
        private const float PedestrianWearPerPass = 0.006f;
        private const float WagonWearPerPass = 0.008f;
        private const float PedestrianRadiusMetres = 1.35f;
        private const float WagonRadiusMetres = 0.7f;
        private const float MaximumSoilBlend = 0.72f;

        [Header("Runtime accumulation")]
        [SerializeField, Min(0.25f)] private float flushIntervalSeconds = 1.5f;
        [SerializeField, Range(1, 12)] private int maximumTilesPerFlush = 3;
        [SerializeField, Min(64)] private int maximumPendingTiles = 512;

        [Header("Runtime diagnostics")]
        [SerializeField] private int pedestrianSamples;
        [SerializeField] private int wagonSamples;
        [SerializeField] private int dirtyTiles;

        private Terrain terrain;
        private TerrainCollider terrainCollider;
        private TerrainData authoredData;
        private TerrainData runtimeData;
        private float[,] accumulatedWear;
        private readonly HashSet<int> dirtyTileIndices = new();
        private readonly Queue<int> dirtyTileOrder = new();
        private int soilLayerIndex = -1;
        private float flushElapsed;

        public int PedestrianSamples => pedestrianSamples;
        public int WagonSamples => wagonSamples;
        public int DirtyTileCount => dirtyTileIndices.Count;
        public bool RuntimeTerrainCloneActive => runtimeData != null && terrain != null && terrain.terrainData == runtimeData;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            AgentMover.ActualMovementSegment += HandlePedestrianMovement;
            CoupledTransportMovementController.ActualWagonMovementSegment += HandleWagonMovement;
            TryCreateRuntimeTerrain();
        }

        private void OnDisable()
        {
            AgentMover.ActualMovementSegment -= HandlePedestrianMovement;
            CoupledTransportMovementController.ActualWagonMovementSegment -= HandleWagonMovement;
            RestoreAuthoredTerrainReference();
        }

        private void Update()
        {
            if (!Application.isPlaying || runtimeData == null || dirtyTileIndices.Count == 0) return;
            flushElapsed += UnityEngine.Time.unscaledDeltaTime;
            if (flushElapsed < Mathf.Max(0.25f, flushIntervalSeconds)) return;
            flushElapsed = 0f;

            int budget = Mathf.Clamp(maximumTilesPerFlush, 1, 12);
            while (budget-- > 0 && dirtyTileOrder.Count > 0)
            {
                int tile = dirtyTileOrder.Dequeue();
                if (!dirtyTileIndices.Remove(tile)) continue;
                ApplyDirtyTile(tile);
            }

            dirtyTiles = dirtyTileIndices.Count;
        }

        private void HandlePedestrianMovement(Vector3 from, Vector3 to)
        {
            if (TryAccumulatePath(from, to, TrafficKind.Pedestrian)) pedestrianSamples++;
        }

        private void HandleWagonMovement(Vector3 from, Vector3 to)
        {
            if (TryAccumulatePath(from, to, TrafficKind.Wagon)) wagonSamples++;
        }

        private bool TryAccumulatePath(Vector3 from, Vector3 to, TrafficKind kind)
        {
            if (!Application.isPlaying || runtimeData == null || accumulatedWear == null) return false;
            Vector3 delta = to - from;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance < 0.015f) return false;

            float cellSize = Mathf.Min(runtimeData.size.x, runtimeData.size.z) / runtimeData.alphamapResolution;
            float sampleSpacing = Mathf.Max(0.75f, cellSize * 0.65f);
            int samples = Mathf.Max(1, Mathf.CeilToInt(distance / sampleSpacing));
            Vector3 perpendicular = distance > 0.001f
                ? new Vector3(-delta.z, 0f, delta.x) / distance
                : Vector3.right;
            float contribution = kind == TrafficKind.Wagon ? WagonWearPerPass : PedestrianWearPerPass;
            float contributionPerSample = contribution * (distance / samples) / sampleSpacing;
            float radius = kind == TrafficKind.Wagon ? WagonRadiusMetres : PedestrianRadiusMetres;

            for (int sample = 0; sample <= samples; sample++)
            {
                Vector3 position = Vector3.Lerp(from, to, sample / (float)samples);
                if (kind == TrafficKind.Wagon)
                {
                    // The two wheel lines are separate and narrower than a foot-worn path.
                    AccumulateKernel(position + perpendicular * 0.55f, radius, contributionPerSample);
                    AccumulateKernel(position - perpendicular * 0.55f, radius, contributionPerSample);
                }
                else
                {
                    AccumulateKernel(position, radius, contributionPerSample);
                }
            }

            dirtyTiles = dirtyTileIndices.Count;
            return true;
        }

        private void AccumulateKernel(Vector3 worldPosition, float radiusMetres, float contribution)
        {
            Vector3 origin = terrain.transform.position;
            Vector3 size = runtimeData.size;
            float normalizedX = (worldPosition.x - origin.x) / size.x;
            float normalizedZ = (worldPosition.z - origin.z) / size.z;
            if (normalizedX < 0f || normalizedX > 1f || normalizedZ < 0f || normalizedZ > 1f) return;

            int resolution = runtimeData.alphamapResolution;
            int centerX = Mathf.Clamp(Mathf.RoundToInt(normalizedX * (resolution - 1)), 0, resolution - 1);
            int centerZ = Mathf.Clamp(Mathf.RoundToInt(normalizedZ * (resolution - 1)), 0, resolution - 1);
            float cellX = size.x / resolution;
            float cellZ = size.z / resolution;
            int radiusX = Mathf.Max(1, Mathf.CeilToInt(radiusMetres / cellX));
            int radiusZ = Mathf.Max(1, Mathf.CeilToInt(radiusMetres / cellZ));

            for (int z = Math.Max(0, centerZ - radiusZ); z <= Math.Min(resolution - 1, centerZ + radiusZ); z++)
            {
                float worldDz = (z - centerZ) * cellZ;
                for (int x = Math.Max(0, centerX - radiusX); x <= Math.Min(resolution - 1, centerX + radiusX); x++)
                {
                    float worldDx = (x - centerX) * cellX;
                    float normalizedDistance = Mathf.Sqrt(worldDx * worldDx + worldDz * worldDz) / radiusMetres;
                    if (normalizedDistance > 1f) continue;

                    // Smooth radial edge avoids a hard or square patch.
                    float weight = 0.5f + 0.5f * Mathf.Cos(normalizedDistance * Mathf.PI);
                    accumulatedWear[z, x] = Mathf.Min(1f, accumulatedWear[z, x] + contribution * weight);
                    MarkDirty(x, z);
                }
            }
        }

        private void MarkDirty(int x, int z)
        {
            int tilesPerAxis = Mathf.CeilToInt(runtimeData.alphamapResolution / (float)UpdateTileSize);
            int tileIndex = (z / UpdateTileSize) * tilesPerAxis + (x / UpdateTileSize);
            if (dirtyTileIndices.Count >= Mathf.Max(64, maximumPendingTiles) || !dirtyTileIndices.Add(tileIndex)) return;
            dirtyTileOrder.Enqueue(tileIndex);
        }

        private void ApplyDirtyTile(int tileIndex)
        {
            int resolution = runtimeData.alphamapResolution;
            int tilesPerAxis = Mathf.CeilToInt(resolution / (float)UpdateTileSize);
            int startX = (tileIndex % tilesPerAxis) * UpdateTileSize;
            int startZ = (tileIndex / tilesPerAxis) * UpdateTileSize;
            int width = Mathf.Min(UpdateTileSize, resolution - startX);
            int height = Mathf.Min(UpdateTileSize, resolution - startZ);
            if (width <= 0 || height <= 0) return;

            float[,,] alphamaps = runtimeData.GetAlphamaps(startX, startZ, width, height);
            int layerCount = alphamaps.GetLength(2);
            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    float wear = accumulatedWear[startZ + z, startX + x];
                    if (wear <= 0f) continue;
                    ApplyWearBlend(alphamaps, z, x, layerCount, wear);
                }
            }

            runtimeData.SetAlphamaps(startX, startZ, alphamaps);
        }

        private void ApplyWearBlend(float[,,] alphamaps, int z, int x, int layerCount, float wear)
        {
            if (soilLayerIndex < 0 || soilLayerIndex >= layerCount) return;
            float cap = MaximumSoilBlend;
            // A wagon's higher contribution makes paired wheel marks emerge gradually,
            // without a single passage carving a deep or opaque track.
            float desiredSoil = Mathf.Min(cap, wear * 3.5f);
            float currentSoil = alphamaps[z, x, soilLayerIndex];
            if (desiredSoil <= currentSoil) return;

            float remove = Mathf.Min(desiredSoil - currentSoil, 1f - currentSoil);
            float otherWeight = 1f - currentSoil;
            if (otherWeight <= 0.0001f) return;
            for (int layer = 0; layer < layerCount; layer++)
            {
                if (layer == soilLayerIndex) continue;
                float source = alphamaps[z, x, layer];
                float transfer = remove * (source / otherWeight);
                alphamaps[z, x, layer] = Mathf.Max(0f, source - transfer);
            }

            alphamaps[z, x, soilLayerIndex] = currentSoil + remove;
            float total = 0f;
            for (int layer = 0; layer < layerCount; layer++) total += alphamaps[z, x, layer];
            if (total > 0f)
                for (int layer = 0; layer < layerCount; layer++) alphamaps[z, x, layer] /= total;
        }

        private void TryCreateRuntimeTerrain()
        {
            if (terrain != null && runtimeData != null) return;
            terrain = GetComponent<Terrain>();
            terrainCollider = GetComponent<TerrainCollider>();
            if (terrain == null || terrain.terrainData == null)
            {
                Debug.LogWarning("TerrainTrafficWear requires an authored Terrain on the same GameObject.", this);
                enabled = false;
                return;
            }

            authoredData = terrain.terrainData;
            int targetLayer = -1;
            TerrainLayer[] layers = authoredData.terrainLayers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] != null && layers[i].name.IndexOf("Disturbed Soil", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    targetLayer = i;
                    break;
                }
            }

            if (targetLayer < 0)
            {
                Debug.LogWarning("TerrainTrafficWear found no authored Disturbed Soil TerrainLayer; traffic will be counted but not painted.", this);
                enabled = false;
                return;
            }

            soilLayerIndex = targetLayer;
            runtimeData = Instantiate(authoredData);
            runtimeData.name = authoredData.name + " (Runtime Traffic Wear)";
            runtimeData.hideFlags = HideFlags.HideAndDontSave;
            int requestedResolution = Mathf.Max(RuntimeAlphamapResolution, authoredData.alphamapResolution);
            if (runtimeData.alphamapResolution != requestedResolution)
            {
                runtimeData.alphamapResolution = requestedResolution;
                CopyAuthoredAlphamapDistribution(authoredData, runtimeData);
            }

            terrain.terrainData = runtimeData;
            if (terrainCollider != null) terrainCollider.terrainData = runtimeData;
            accumulatedWear = new float[runtimeData.alphamapResolution, runtimeData.alphamapResolution];
        }

        private static void CopyAuthoredAlphamapDistribution(TerrainData source, TerrainData destination)
        {
            int sourceWidth = source.alphamapWidth;
            int sourceHeight = source.alphamapHeight;
            int destinationWidth = destination.alphamapWidth;
            int destinationHeight = destination.alphamapHeight;
            float[,,] sourceMaps = source.GetAlphamaps(0, 0, sourceWidth, sourceHeight);
            int layerCount = Mathf.Min(sourceMaps.GetLength(2), destination.terrainLayers.Length);
            if (layerCount <= 0)
            {
                return;
            }

            float[,,] destinationMaps = new float[destinationHeight, destinationWidth, layerCount];
            float xScale = sourceWidth > 1 && destinationWidth > 1
                ? (sourceWidth - 1f) / (destinationWidth - 1f)
                : 0f;
            float zScale = sourceHeight > 1 && destinationHeight > 1
                ? (sourceHeight - 1f) / (destinationHeight - 1f)
                : 0f;

            for (int z = 0; z < destinationHeight; z++)
            {
                float sourceZ = z * zScale;
                int z0 = Mathf.FloorToInt(sourceZ);
                int z1 = Mathf.Min(sourceHeight - 1, z0 + 1);
                float zBlend = sourceZ - z0;

                for (int x = 0; x < destinationWidth; x++)
                {
                    float sourceX = x * xScale;
                    int x0 = Mathf.FloorToInt(sourceX);
                    int x1 = Mathf.Min(sourceWidth - 1, x0 + 1);
                    float xBlend = sourceX - x0;
                    float total = 0f;

                    for (int layer = 0; layer < layerCount; layer++)
                    {
                        float lower = Mathf.Lerp(sourceMaps[z0, x0, layer], sourceMaps[z0, x1, layer], xBlend);
                        float upper = Mathf.Lerp(sourceMaps[z1, x0, layer], sourceMaps[z1, x1, layer], xBlend);
                        float value = Mathf.Lerp(lower, upper, zBlend);
                        destinationMaps[z, x, layer] = value;
                        total += value;
                    }

                    if (total > 0.0001f)
                    {
                        for (int layer = 0; layer < layerCount; layer++)
                        {
                            destinationMaps[z, x, layer] /= total;
                        }
                    }
                    else
                    {
                        destinationMaps[z, x, 0] = 1f;
                    }
                }
            }

            destination.SetAlphamaps(0, 0, destinationMaps);
        }

        private void RestoreAuthoredTerrainReference()
        {
            if (runtimeData == null) return;
            if (terrain != null && terrain.terrainData == runtimeData) terrain.terrainData = authoredData;
            if (terrainCollider != null && terrainCollider.terrainData == runtimeData) terrainCollider.terrainData = authoredData;
            if (Application.isPlaying) Destroy(runtimeData);
            else DestroyImmediate(runtimeData);

            runtimeData = null;
            authoredData = null;
            accumulatedWear = null;
            dirtyTileIndices.Clear();
            dirtyTileOrder.Clear();
            dirtyTiles = 0;
        }
    }
}
