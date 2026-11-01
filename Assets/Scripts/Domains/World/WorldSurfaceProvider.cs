using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.World
{
    public enum WorldStartupMode
    {
        UsePreAuthoredTerrain = 0,
        GenerateProceduralTerrain = 1
    }

    public enum WorldPlacementMaskType
    {
        Blocked = 0,
        Water = 1,
        NoTown = 2,
        NoBuilding = 3,
        NoRoad = 4,
        ResourceAllowed = 5,
        ResourceBlocked = 6,
        TownCandidateZone = 7
    }

    public struct WorldSurfaceSample
    {
        public Vector3 position;
        public float height;
        public Vector3 normal;
        public float slopeDegrees;
        public bool isInsideWorld;
        public bool isWater;
        public bool isBlocked;
        public bool isBuildable;
    }

    public struct PlacementQuery
    {
        public float maximumSlopeDegrees;
        public bool forTown;
        public bool forBuilding;
    }

    public struct RoadQuery
    {
        public float maximumSlopeDegrees;
    }

    public struct ResourcePlacementQuery
    {
        public float maximumSlopeDegrees;
        public MineralResourceKind resourceKind;
    }

    public interface IWorldSurfaceProvider
    {
        Bounds WorldBounds { get; }
        bool TrySampleHeight(Vector3 worldPosition, out float height);
        bool TrySampleSurface(Vector3 worldPosition, out WorldSurfaceSample sample);
        bool IsInsideWorld(Vector3 worldPosition);
        bool IsBuildable(Vector3 worldPosition, PlacementQuery query);
        bool IsRoadCompatible(Vector3 worldPosition, RoadQuery query);
        bool IsResourceCompatible(Vector3 worldPosition, ResourcePlacementQuery query);
    }

    internal interface IWorldSurfaceCandidateZones
    {
        IReadOnlyList<Bounds> TownCandidateZones { get; }
        bool IsInsideTownCandidateZone(Vector3 worldPosition);
    }

    internal interface IWorldSurfaceResourceZones
    {
        IReadOnlyList<Bounds> GetResourceAllowedZones(MineralResourceKind kind);
    }

    public static class WorldStartupRouting
    {
        public static bool ShouldGenerateProceduralTerrain(WorldStartupMode mode)
        {
            return mode == WorldStartupMode.GenerateProceduralTerrain;
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class WorldPlacementMaskVolume : MonoBehaviour
    {
        [SerializeField] private WorldPlacementMaskType maskType = WorldPlacementMaskType.Blocked;
        [SerializeField] private MineralResourceKind resourceKind = MineralResourceKind.Coal;
        [SerializeField] private bool appliesToAllResourceKinds = true;

        public WorldPlacementMaskType MaskType => maskType;
        public Collider VolumeCollider => GetComponent<Collider>();
        public MineralResourceKind ResourceKind => resourceKind;
        public bool AppliesToAllResourceKinds => appliesToAllResourceKinds;

        public bool Contains(Vector3 worldPosition)
        {
            Collider volume = VolumeCollider;
            if (volume == null || !volume.enabled || !volume.gameObject.activeInHierarchy || !volume.bounds.Contains(worldPosition))
            {
                return false;
            }

            Vector3 closest = volume.ClosestPoint(worldPosition);
            return (closest - worldPosition).sqrMagnitude <= 0.000001f;
        }

        public bool AppliesToResource(MineralResourceKind kind)
        {
            return appliesToAllResourceKinds || resourceKind == kind;
        }

        public bool OverlapsResourceKinds(WorldPlacementMaskVolume other)
        {
            return other != null
                && (appliesToAllResourceKinds
                    || other.appliesToAllResourceKinds
                    || resourceKind == other.resourceKind);
        }
    }

    public sealed class PreAuthoredTerrainSurfaceProvider : IWorldSurfaceProvider, IWorldSurfaceCandidateZones, IWorldSurfaceResourceZones
    {
        private readonly Terrain[] terrains;
        private readonly WorldPlacementMaskVolume[] masks;
        private readonly bool useWaterHeight;
        private readonly float waterHeight;
        private readonly float defaultBuildingSlope;
        private readonly float defaultTownSlope;
        private readonly float defaultRoadSlope;
        private readonly float defaultResourceSlope;
        private readonly bool allowBroadResourcePlacementWhenNoAllowedZones;
        private readonly List<Bounds> townCandidateZones = new();

        public PreAuthoredTerrainSurfaceProvider(
            Terrain[] terrains,
            WorldPlacementMaskVolume[] masks,
            Bounds? boundsOverride,
            bool useWaterHeight,
            float waterHeight,
            float defaultBuildingSlope,
            float defaultTownSlope,
            float defaultRoadSlope,
            float defaultResourceSlope,
            bool allowBroadResourcePlacementWhenNoAllowedZones = true)
        {
            this.terrains = FilterTerrains(terrains);
            this.masks = masks ?? Array.Empty<WorldPlacementMaskVolume>();
            this.useWaterHeight = useWaterHeight;
            this.waterHeight = waterHeight;
            this.defaultBuildingSlope = Mathf.Max(0f, defaultBuildingSlope);
            this.defaultTownSlope = Mathf.Max(0f, defaultTownSlope);
            this.defaultRoadSlope = Mathf.Max(0f, defaultRoadSlope);
            this.defaultResourceSlope = Mathf.Max(0f, defaultResourceSlope);
            this.allowBroadResourcePlacementWhenNoAllowedZones = allowBroadResourcePlacementWhenNoAllowedZones;
            WorldBounds = boundsOverride ?? CalculateBounds(this.terrains);

            for (int i = 0; i < this.masks.Length; i++)
            {
                WorldPlacementMaskVolume mask = this.masks[i];
                if (mask != null && mask.MaskType == WorldPlacementMaskType.TownCandidateZone && mask.VolumeCollider != null)
                {
                    townCandidateZones.Add(mask.VolumeCollider.bounds);
                }
            }
        }

        public Bounds WorldBounds { get; }
        public IReadOnlyList<Bounds> TownCandidateZones => townCandidateZones;

        public bool IsInsideTownCandidateZone(Vector3 worldPosition)
        {
            bool hasZones = false;
            for (int i = 0; i < masks.Length; i++)
            {
                WorldPlacementMaskVolume mask = masks[i];
                if (mask == null || mask.MaskType != WorldPlacementMaskType.TownCandidateZone)
                {
                    continue;
                }

                hasZones = true;
                if (mask.Contains(worldPosition))
                {
                    return true;
                }
            }

            return !hasZones;
        }

        public IReadOnlyList<Bounds> GetResourceAllowedZones(MineralResourceKind kind)
        {
            List<Bounds> result = new();
            for (int i = 0; i < masks.Length; i++)
            {
                WorldPlacementMaskVolume mask = masks[i];
                if (mask != null
                    && mask.MaskType == WorldPlacementMaskType.ResourceAllowed
                    && mask.AppliesToResource(kind)
                    && mask.VolumeCollider != null)
                {
                    result.Add(mask.VolumeCollider.bounds);
                }
            }

            return result;
        }

        public bool HasPlacementMask(Vector3 worldPosition, WorldPlacementMaskType type)
        {
            return HasMask(worldPosition, type);
        }

        public bool TrySampleHeight(Vector3 worldPosition, out float height)
        {
            if (TryResolveTerrain(worldPosition, out Terrain terrain, out _))
            {
                height = terrain.transform.position.y + terrain.SampleHeight(worldPosition);
                return IsFinite(height);
            }

            height = 0f;
            return false;
        }

        public bool TrySampleSurface(Vector3 worldPosition, out WorldSurfaceSample sample)
        {
            sample = default;
            if (!TryResolveTerrain(worldPosition, out Terrain terrain, out Vector2 normalized))
            {
                return false;
            }

            TerrainData data = terrain.terrainData;
            float height = terrain.transform.position.y + terrain.SampleHeight(worldPosition);
            if (!IsFinite(height))
            {
                height = 0f;
                return false;
            }

            Vector3 normal = data.GetInterpolatedNormal(normalized.x, normalized.y);
            float slope = data.GetSteepness(normalized.x, normalized.y);
            if (!IsFinite(normal) || !IsFinite(slope))
            {
                return false;
            }

            Vector3 samplePosition = new(worldPosition.x, height, worldPosition.z);
            bool water = useWaterHeight && height <= waterHeight
                || HasMask(samplePosition, WorldPlacementMaskType.Water);
            bool blocked = HasMask(samplePosition, WorldPlacementMaskType.Blocked)
                || water;

            sample = new WorldSurfaceSample
            {
                position = samplePosition,
                height = height,
                normal = normal,
                slopeDegrees = slope,
                isInsideWorld = true,
                isWater = water,
                isBlocked = blocked,
                isBuildable = !water && !blocked && !HasMask(samplePosition, WorldPlacementMaskType.NoBuilding)
            };
            return true;
        }

        public bool IsInsideWorld(Vector3 worldPosition)
        {
            return TryResolveTerrain(worldPosition, out _, out _);
        }

        public bool IsBuildable(Vector3 worldPosition, PlacementQuery query)
        {
            if (!TrySampleSurface(worldPosition, out WorldSurfaceSample sample))
            {
                return false;
            }

            float maxSlope = query.maximumSlopeDegrees > 0f
                ? query.maximumSlopeDegrees
                : query.forTown ? defaultTownSlope : defaultBuildingSlope;
            if (!sample.isBuildable || sample.slopeDegrees > maxSlope)
            {
                return false;
            }

            if (query.forTown && HasMask(sample.position, WorldPlacementMaskType.NoTown))
            {
                return false;
            }

            return !query.forBuilding || !HasMask(sample.position, WorldPlacementMaskType.NoBuilding);
        }

        public bool IsRoadCompatible(Vector3 worldPosition, RoadQuery query)
        {
            if (!TrySampleSurface(worldPosition, out WorldSurfaceSample sample))
            {
                return false;
            }

            float maxSlope = query.maximumSlopeDegrees > 0f ? query.maximumSlopeDegrees : defaultRoadSlope;
            return !sample.isWater
                && !sample.isBlocked
                && sample.slopeDegrees <= maxSlope
                && !HasMask(sample.position, WorldPlacementMaskType.NoRoad);
        }

        public bool IsResourceCompatible(Vector3 worldPosition, ResourcePlacementQuery query)
        {
            if (!TrySampleSurface(worldPosition, out WorldSurfaceSample sample))
            {
                return false;
            }

            float maxSlope = query.maximumSlopeDegrees > 0f ? query.maximumSlopeDegrees : defaultResourceSlope;
            if (sample.isWater
                || sample.isBlocked
                || sample.slopeDegrees > maxSlope
                || HasResourceMask(sample.position, WorldPlacementMaskType.ResourceBlocked, query.resourceKind))
            {
                return false;
            }

            bool hasAllowedZones = HasAnyResourceMask(WorldPlacementMaskType.ResourceAllowed, query.resourceKind);
            return hasAllowedZones
                ? HasResourceMask(sample.position, WorldPlacementMaskType.ResourceAllowed, query.resourceKind)
                : allowBroadResourcePlacementWhenNoAllowedZones;
        }

        private bool TryResolveTerrain(Vector3 worldPosition, out Terrain terrain, out Vector2 normalized)
        {
            Terrain best = null;
            Vector2 bestNormalized = default;
            for (int i = 0; i < terrains.Length; i++)
            {
                Terrain candidate = terrains[i];
                if (candidate == null || candidate.terrainData == null || !candidate.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Vector3 origin = candidate.transform.position;
                Vector3 size = candidate.terrainData.size;
                if (worldPosition.x < origin.x
                    || worldPosition.x > origin.x + size.x
                    || worldPosition.z < origin.z
                    || worldPosition.z > origin.z + size.z)
                {
                    continue;
                }

                Vector2 candidateNormalized = new(
                    Mathf.Clamp01((worldPosition.x - origin.x) / Mathf.Max(0.001f, size.x)),
                    Mathf.Clamp01((worldPosition.z - origin.z) / Mathf.Max(0.001f, size.z)));
                if (best == null || CompareTerrainPriority(candidate, best) > 0)
                {
                    best = candidate;
                    bestNormalized = candidateNormalized;
                }
            }

            terrain = best;
            normalized = bestNormalized;
            return terrain != null;
        }

        private bool HasMask(Vector3 position, WorldPlacementMaskType type)
        {
            for (int i = 0; i < masks.Length; i++)
            {
                WorldPlacementMaskVolume mask = masks[i];
                if (mask != null && mask.MaskType == type && mask.Contains(position))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasAnyResourceMask(WorldPlacementMaskType type, MineralResourceKind kind)
        {
            for (int i = 0; i < masks.Length; i++)
            {
                WorldPlacementMaskVolume mask = masks[i];
                if (mask != null && mask.MaskType == type && mask.AppliesToResource(kind))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasResourceMask(Vector3 position, WorldPlacementMaskType type, MineralResourceKind kind)
        {
            for (int i = 0; i < masks.Length; i++)
            {
                WorldPlacementMaskVolume mask = masks[i];
                if (mask != null && mask.MaskType == type && mask.AppliesToResource(kind) && mask.Contains(position))
                {
                    return true;
                }
            }

            return false;
        }

        private static Terrain[] FilterTerrains(Terrain[] source)
        {
            List<Terrain> valid = new();
            if (source != null)
            {
                for (int i = 0; i < source.Length; i++)
                {
                    if (source[i] != null && source[i].terrainData != null)
                    {
                        valid.Add(source[i]);
                    }
                }
            }

            valid.Sort(CompareTerrains);
            return valid.ToArray();
        }

        private static int CompareTerrainPriority(Terrain left, Terrain right)
        {
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

        private static int CompareTerrains(Terrain left, Terrain right)
        {
            return CompareTerrainPriority(left, right);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static Bounds CalculateBounds(Terrain[] source)
        {
            Bounds result = default;
            bool initialized = false;
            for (int i = 0; i < source.Length; i++)
            {
                Terrain terrain = source[i];
                if (terrain == null || terrain.terrainData == null)
                {
                    continue;
                }

                Vector3 size = terrain.terrainData.size;
                Bounds bounds = new(terrain.transform.position + size * 0.5f, size);
                if (!initialized)
                {
                    result = bounds;
                    initialized = true;
                }
                else
                {
                    result.Encapsulate(bounds);
                }
            }

            return result;
        }
    }

    public sealed class ProceduralTerrainSurfaceProvider : IWorldSurfaceProvider
    {
        private readonly Collider terrainCollider;
        private readonly Bounds fallbackBounds;
        private readonly float raycastHeight;
        private readonly float raycastDistance;
        private readonly LayerMask terrainLayers;

        public ProceduralTerrainSurfaceProvider(
            Collider terrainCollider,
            Bounds fallbackBounds,
            float raycastHeight,
            float raycastDistance,
            LayerMask terrainLayers)
        {
            this.terrainCollider = terrainCollider;
            this.fallbackBounds = fallbackBounds;
            this.raycastHeight = Mathf.Max(1f, raycastHeight);
            this.raycastDistance = Mathf.Max(1f, raycastDistance);
            this.terrainLayers = terrainLayers;
        }

        public Bounds WorldBounds => terrainCollider != null ? terrainCollider.bounds : fallbackBounds;

        public bool TrySampleHeight(Vector3 worldPosition, out float height)
        {
            if (TrySampleSurface(worldPosition, out WorldSurfaceSample sample))
            {
                height = sample.height;
                return true;
            }

            height = 0f;
            return false;
        }

        public bool TrySampleSurface(Vector3 worldPosition, out WorldSurfaceSample sample)
        {
            sample = default;
            Vector3 origin = new(worldPosition.x, WorldBounds.max.y + raycastHeight, worldPosition.z);
            Ray ray = new(origin, Vector3.down);
            RaycastHit hit;
            bool found = terrainCollider != null
                ? terrainCollider.Raycast(ray, out hit, raycastDistance)
                : Physics.Raycast(ray, out hit, raycastDistance, terrainLayers, QueryTriggerInteraction.Ignore);
            if (!found)
            {
                return false;
            }

            sample = new WorldSurfaceSample
            {
                position = hit.point,
                height = hit.point.y,
                normal = hit.normal,
                slopeDegrees = Vector3.Angle(Vector3.up, hit.normal),
                isInsideWorld = true,
                isBuildable = true
            };
            return true;
        }

        public bool IsInsideWorld(Vector3 worldPosition)
        {
            Bounds bounds = WorldBounds;
            return worldPosition.x >= bounds.min.x
                && worldPosition.x <= bounds.max.x
                && worldPosition.z >= bounds.min.z
                && worldPosition.z <= bounds.max.z;
        }

        public bool IsBuildable(Vector3 worldPosition, PlacementQuery query)
        {
            return TrySampleSurface(worldPosition, out WorldSurfaceSample sample)
                && sample.slopeDegrees <= Mathf.Max(0f, query.maximumSlopeDegrees);
        }

        public bool IsRoadCompatible(Vector3 worldPosition, RoadQuery query)
        {
            return TrySampleSurface(worldPosition, out WorldSurfaceSample sample)
                && sample.slopeDegrees <= Mathf.Max(0f, query.maximumSlopeDegrees);
        }

        public bool IsResourceCompatible(Vector3 worldPosition, ResourcePlacementQuery query)
        {
            return TrySampleSurface(worldPosition, out WorldSurfaceSample sample)
                && sample.slopeDegrees <= Mathf.Max(0f, query.maximumSlopeDegrees);
        }
    }
}
