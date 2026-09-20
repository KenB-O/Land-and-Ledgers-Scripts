using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.World
{
    [DisallowMultipleComponent]
    public sealed class PreAuthoredTerrainWorldProfile : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string terrainProfileId = "main-authored-terrain-v1";
        [SerializeField, Min(1)] private int terrainContentRevision = 1;

        [Header("Opening Town")]
        [SerializeField] private bool useFixedTownAnchor;
        [SerializeField] private Transform fixedTownAnchor;

        [Header("Terrain")]
        [SerializeField] private bool autoDiscoverTerrains = true;
        [SerializeField] private Terrain[] terrains;
        [SerializeField] private bool overrideWorldBounds;
        [SerializeField] private Bounds worldBoundsOverride = new(Vector3.zero, new Vector3(1024f, 128f, 1024f));

        [Header("Placement")]
        [SerializeField, Range(0f, 60f)] private float defaultMaxBuildingSlope = 8f;
        [SerializeField, Range(0f, 60f)] private float defaultMaxTownSlope = 6f;
        [SerializeField, Range(0f, 60f)] private float defaultMaxRoadSlope = 10f;
        [SerializeField, Range(0f, 60f)] private float defaultMaxResourceSlope = 28f;
        [SerializeField, Min(0f)] private float maximumBuildingFootprintHeightDifference = 1.25f;
        [SerializeField, Min(0f)] private float maximumRoadConnectionHeightDifference = 1.5f;
        [SerializeField, Min(0f)] private float minimumTownDistanceFromMapEdge = 24f;
        [SerializeField, Min(0f)] private float minimumDepositDistanceFromTownCore = 90f;
        [SerializeField, Min(8)] private int townCandidateCount = 96;
        [SerializeField, Range(3, 9)] private int townValidationGridResolution = 5;

        [Header("Water And Masks")]
        [SerializeField] private bool useWaterHeight;
        [SerializeField] private float waterHeight;
        [SerializeField] private bool autoDiscoverMaskVolumes = true;
        [SerializeField] private WorldPlacementMaskVolume[] maskVolumes;
        [SerializeField] private bool allowBroadResourcePlacementWhenNoAllowedZones = true;

        [Header("Diagnostics")]
        [SerializeField] private bool verboseStartupLogging;

        public string TerrainProfileId => terrainProfileId != null ? terrainProfileId.Trim() : string.Empty;
        public int TerrainContentRevision => Mathf.Max(1, terrainContentRevision);
        public bool UseFixedTownAnchor => useFixedTownAnchor;
        public Transform FixedTownAnchor => fixedTownAnchor;
        public float DefaultMaxBuildingSlope => defaultMaxBuildingSlope;
        public float DefaultMaxTownSlope => defaultMaxTownSlope;
        public float DefaultMaxRoadSlope => defaultMaxRoadSlope;
        public float DefaultMaxResourceSlope => defaultMaxResourceSlope;
        public float MaximumBuildingFootprintHeightDifference => Mathf.Max(0f, maximumBuildingFootprintHeightDifference);
        public float MaximumRoadConnectionHeightDifference => Mathf.Max(0f, maximumRoadConnectionHeightDifference);
        public float MinimumTownDistanceFromMapEdge => Mathf.Max(0f, minimumTownDistanceFromMapEdge);
        public float MinimumDepositDistanceFromTownCore => Mathf.Max(0f, minimumDepositDistanceFromTownCore);
        public int TownCandidateCount => Mathf.Max(8, townCandidateCount);
        public int TownValidationGridResolution => Mathf.Clamp(townValidationGridResolution, 3, 9);
        public bool AllowBroadResourcePlacementWhenNoAllowedZones => allowBroadResourcePlacementWhenNoAllowedZones;
        public bool VerboseStartupLogging => verboseStartupLogging;

        public Terrain[] GetResolvedTerrains()
        {
            return ResolveTerrains();
        }

        public WorldPlacementMaskVolume[] GetResolvedMaskVolumes()
        {
            return ResolveMasks();
        }

        public bool TryValidateConfiguration(out string error)
        {
            List<string> failures = new();
            if (string.IsNullOrWhiteSpace(terrainProfileId))
            {
                failures.Add("terrain profile ID is empty");
            }

            if (terrainContentRevision < 1)
            {
                failures.Add("terrain content revision must be at least 1");
            }

            Terrain[] resolvedTerrains = ResolveTerrains();
            if (resolvedTerrains.Length == 0)
            {
                failures.Add("no valid Unity Terrain references are configured");
            }
            else
            {
                for (int i = 0; i < resolvedTerrains.Length; i++)
                {
                    Terrain terrain = resolvedTerrains[i];
                    TerrainCollider collider = terrain != null ? terrain.GetComponent<TerrainCollider>() : null;
                    if (collider == null || collider.terrainData != terrain.terrainData)
                    {
                        failures.Add($"terrain '{terrain?.name ?? "<missing>"}' has no matching TerrainCollider");
                    }
                }
            }

            if (useFixedTownAnchor && fixedTownAnchor == null)
            {
                failures.Add("fixed town anchor mode is enabled but no anchor Transform is assigned");
            }

            WorldPlacementMaskVolume[] resolvedMasks = ResolveMasks();
            for (int i = 0; i < resolvedMasks.Length; i++)
            {
                WorldPlacementMaskVolume mask = resolvedMasks[i];
                if (mask == null)
                {
                    failures.Add($"placement mask reference {i} is missing");
                    continue;
                }

                Collider collider = mask.VolumeCollider;
                if (collider == null || !collider.enabled || !mask.gameObject.activeInHierarchy)
                {
                    failures.Add($"placement mask '{mask.name}' has an invalid or disabled collider");
                }

                if (mask.GetComponentInParent<GeneratedWorldContentMarker>(true) != null)
                {
                    failures.Add($"placement mask '{mask.name}' is parented beneath generated world content");
                }
            }

            for (int leftIndex = 0; leftIndex < resolvedMasks.Length; leftIndex++)
            {
                WorldPlacementMaskVolume left = resolvedMasks[leftIndex];
                if (left == null || left.MaskType != WorldPlacementMaskType.ResourceAllowed || left.VolumeCollider == null)
                {
                    continue;
                }

                for (int rightIndex = 0; rightIndex < resolvedMasks.Length; rightIndex++)
                {
                    WorldPlacementMaskVolume right = resolvedMasks[rightIndex];
                    if (right == null
                        || right.MaskType != WorldPlacementMaskType.ResourceBlocked
                        || right.VolumeCollider == null
                        || !left.OverlapsResourceKinds(right)
                        || !left.VolumeCollider.bounds.Intersects(right.VolumeCollider.bounds))
                    {
                        continue;
                    }

                    failures.Add($"resource masks '{left.name}' and '{right.name}' overlap with contradictory allowed/blocked rules");
                }
            }

            error = failures.Count == 0
                ? string.Empty
                : "Pre-authored terrain profile validation failed: " + string.Join("; ", failures) + ".";
            return failures.Count == 0;
        }

        public bool TryCreateProvider(out IWorldSurfaceProvider provider, out string error)
        {
            if (!TryValidateConfiguration(out error))
            {
                provider = null;
                return false;
            }

            Terrain[] resolvedTerrains = ResolveTerrains();
            if (resolvedTerrains.Length == 0)
            {
                provider = null;
                error = "Pre-authored terrain profile has no valid Unity Terrain references.";
                return false;
            }

            provider = new PreAuthoredTerrainSurfaceProvider(
                resolvedTerrains,
                ResolveMasks(),
                overrideWorldBounds ? worldBoundsOverride : null,
                useWaterHeight,
                waterHeight,
                defaultMaxBuildingSlope,
                defaultMaxTownSlope,
                defaultMaxRoadSlope,
                defaultMaxResourceSlope,
                allowBroadResourcePlacementWhenNoAllowedZones);
            error = string.Empty;
            return true;
        }

        public Terrain GetPrimaryTerrain()
        {
            Terrain[] resolved = ResolveTerrains();
            return resolved.Length > 0 ? resolved[0] : null;
        }

        private Terrain[] ResolveTerrains()
        {
            if (HasValidTerrain(terrains))
            {
                Terrain[] resolved = Array.FindAll(terrains, terrain => terrain != null && terrain.terrainData != null);
                Array.Sort(resolved, CompareTerrains);
                return resolved;
            }

            if (!autoDiscoverTerrains)
            {
                return Array.Empty<Terrain>();
            }

            Terrain[] found = FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Array.Sort(found, CompareTerrains);
            return found;
        }

        private WorldPlacementMaskVolume[] ResolveMasks()
        {
            if (maskVolumes != null && maskVolumes.Length > 0)
            {
                return maskVolumes;
            }

            return autoDiscoverMaskVolumes
                ? FindObjectsByType<WorldPlacementMaskVolume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                : Array.Empty<WorldPlacementMaskVolume>();
        }

        private static bool HasValidTerrain(Terrain[] candidates)
        {
            if (candidates == null)
            {
                return false;
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] != null && candidates[i].terrainData != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareTerrains(Terrain left, Terrain right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            Vector3 leftPosition = left.transform.position;
            Vector3 rightPosition = right.transform.position;
            int x = leftPosition.x.CompareTo(rightPosition.x);
            if (x != 0)
            {
                return x;
            }

            int z = leftPosition.z.CompareTo(rightPosition.z);
            return z != 0 ? z : string.CompareOrdinal(left.name, right.name);
        }

        private void OnValidate()
        {
            terrainProfileId = string.IsNullOrWhiteSpace(terrainProfileId)
                ? "main-authored-terrain-v1"
                : terrainProfileId.Trim();
            terrainContentRevision = Mathf.Max(1, terrainContentRevision);
            maximumBuildingFootprintHeightDifference = Mathf.Max(0f, maximumBuildingFootprintHeightDifference);
            maximumRoadConnectionHeightDifference = Mathf.Max(0f, maximumRoadConnectionHeightDifference);
            minimumTownDistanceFromMapEdge = Mathf.Max(0f, minimumTownDistanceFromMapEdge);
            minimumDepositDistanceFromTownCore = Mathf.Max(0f, minimumDepositDistanceFromTownCore);
            townCandidateCount = Mathf.Max(8, townCandidateCount);
            townValidationGridResolution = Mathf.Clamp(townValidationGridResolution, 3, 9);
        }
    }
}
