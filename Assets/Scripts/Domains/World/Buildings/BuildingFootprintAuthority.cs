using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LandLedgers.World
{
    public enum BuildingActivityLocationType
    {
        Bed = 0,
        Stove = 1,
        Cleaning = 2,
        Counter = 3,
        Storage = 4,
        Receiving = 5,
        Delivery = 6,
        Oven = 7,
        Workshop = 8,
        Porch = 9,
        Firewood = 10,
        Water = 11,
        Loading = 12,
        HitchCandidate = 13
    }

    [System.Serializable]
    public sealed class BuildingEntrancePairAuthoring
    {
        public string id = "main-entrance";
        [Tooltip("Existing front-door marker or selected door geometry used to discover this pair.")]
        public Transform sourceTransform;
        public Vector3 indoorPositionLocal;
        public Vector3 outdoorApproachLocal;
        [Min(0.2f)] public float clearWidthMeters = 1.1f;
        public int floorIndex;
        [Tooltip("Optional HousingAuthority FunctionalSpace ID. Leave empty for a shared floor area.")]
        public string functionalSpaceId = string.Empty;
    }

    [System.Serializable]
    public sealed class BuildingActivityLocationAuthoring
    {
        public string id = "activity-location";
        public string displayName = "Activity Location";
        public BuildingActivityLocationType type;
        public int floorIndex;
        public Vector3 positionLocal;
        public Vector3 forwardLocal = Vector3.forward;
        [Tooltip("Optional HousingAuthority FunctionalSpace ID. Leave empty for a shared location.")]
        public string functionalSpaceId = string.Empty;
        [Tooltip("Whether the point must be inside a walkable floor polygon and outside its exclusions.")]
        public bool requiresWalkableInterior = true;
        [Tooltip("Optional source furniture or authored prop. This reference documents the location; it does not grant access or task permission.")]
        public Transform sourceObject;
        [Tooltip("Optional requirements for the caller, such as a tool or installed equipment. These are descriptive, not automatic permissions.")]
        public List<string> optionalRequirements = new();
    }

    public readonly struct ResolvedBuildingActivityLocation
    {
        public ResolvedBuildingActivityLocation(
            int buildingInstanceId,
            string id,
            string displayName,
            BuildingActivityLocationType type,
            Vector3 worldPosition,
            Vector3 worldForward,
            int floorIndex,
            string functionalSpaceId,
            bool requiresWalkableInterior,
            IReadOnlyList<string> optionalRequirements)
        {
            BuildingInstanceId = buildingInstanceId;
            Id = id;
            DisplayName = displayName;
            Type = type;
            WorldPosition = worldPosition;
            WorldForward = worldForward;
            FloorIndex = floorIndex;
            FunctionalSpaceId = functionalSpaceId;
            RequiresWalkableInterior = requiresWalkableInterior;
            OptionalRequirements = optionalRequirements;
        }

        public int BuildingInstanceId { get; }
        public string Id { get; }
        public string DisplayName { get; }
        public BuildingActivityLocationType Type { get; }
        public Vector3 WorldPosition { get; }
        public Vector3 WorldForward { get; }
        public int FloorIndex { get; }
        public string FunctionalSpaceId { get; }
        public bool RequiresWalkableInterior { get; }
        public IReadOnlyList<string> OptionalRequirements { get; }
    }

    [System.Serializable]
    public sealed class InteriorPolygonAuthoring
    {
        public string label = "Walkable Area";
        [Tooltip("Optional HousingAuthority functional-space id. Empty means shared floor area.")]
        public string functionalSpaceId = string.Empty;
        public List<Vector3> vertices = new();
    }

    [System.Serializable]
    public sealed class InteriorFloorAuthoring
    {
        public string displayName = "Ground Floor";
        public int floorIndex;
        public float floorHeightLocal;
        public List<InteriorPolygonAuthoring> walkableAreas = new();
        public List<InteriorPolygonAuthoring> exclusionAreas = new();
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Land & Ledgers/World/Building Footprint Authority")]
    public sealed class BuildingFootprintAuthority : MonoBehaviour
    {
        [Header("Authoring Mode")]
        [SerializeField]
        [Tooltip("When enabled, the footprint is resolved from the four authored corner points instead of the legacy width/depth integers.")]
        private bool useCornerAuthoring = true;

        [SerializeField, Min(0.1f)]
        [Tooltip("Cell size used to convert authored local-space corner dimensions into minimum grid cells. Match the town grid cell size; the current default is 2 meters.")]
        private float authoringCellSizeMeters = 2f;

        [Header("Legacy Integer Fallback")]
        [SerializeField, Min(1)]
        [Tooltip("Fallback minimum width used when corner authoring is disabled.")]
        private int minimumFootprintWidthCells = 1;

        [SerializeField, Min(1)]
        [Tooltip("Fallback minimum depth used when corner authoring is disabled.")]
        private int minimumFootprintDepthCells = 1;

        [Header("Corner Authoring")]
        [SerializeField]
        private Vector3 frontLeftCornerLocal = new(-2f, 0f, 2f);

        [SerializeField]
        private Vector3 frontRightCornerLocal = new(2f, 0f, 2f);

        [SerializeField]
        private Vector3 backRightCornerLocal = new(2f, 0f, -2f);

        [SerializeField]
        private Vector3 backLeftCornerLocal = new(-2f, 0f, -2f);

        [Header("Front Authoring")]
        [SerializeField]
        [Tooltip("When enabled, the brown front handle is kept centered on the authored front edge with the configured outward offset.")]
        private bool autoCenterFrontDoorPoint = true;

        [SerializeField, Min(0f)]
        [Tooltip("How far outward from the authored front edge the brown front point should sit.")]
        private float frontDoorOutwardOffsetMeters = 0.35f;

        [SerializeField]
        [Tooltip("Brown front authoring point. TownWorldController can use this as the authored front-door anchor when no named prefab marker exists.")]
        private Vector3 frontDoorLocalPoint = new(0f, 0f, 2.35f);

        [SerializeField]
        [Tooltip("When enabled, placement code may treat the brown front point as the exact frontage/road-access point to pin to the nearest road edge instead of centering the building in the parcel.")]
        private bool useFrontPointAsRoadAccessAnchor = true;

        [Header("Renderer Cross-Check")]
        [SerializeField]
        [Tooltip("When enabled, authoring warnings compare the footprint handles against building-shell renderers. Helper, marker, terrain, route, collider, and extreme outlier renderers are ignored so imported prefabs do not produce impossible footprint warnings.")]
        private bool warnWhenRendererBoundsExceedCorners = true;

        [SerializeField, Min(0)]
        [Tooltip("Small slack for renderer/corner mismatch warnings. This prevents trim, bevels, and small material overhangs from being reported as footprint-authority problems.")]
        private int rendererBoundsWarningSlackCells = 2;

        [SerializeField, Min(16f)]
        [Tooltip("Local-space renderer bounds larger than this many meters, or many times larger than the authored handles, are treated as imported-helper/outlier bounds and ignored for footprint warnings.")]
        private float rendererBoundsOutlierFloorMeters = 64f;

        [SerializeField, HideInInspector]
        private List<InteriorFloorAuthoring> interiorFloors = new();

        [Header("Building Entrances and Activity Locations")]
        [SerializeField, HideInInspector]
        private List<BuildingEntrancePairAuthoring> entrancePairs = new();

        [SerializeField, HideInInspector]
        private List<BuildingActivityLocationAuthoring> activityLocations = new();

        public bool UsesCornerAuthoring => useCornerAuthoring;
        public float AuthoringCellSizeMeters => Mathf.Max(0.1f, authoringCellSizeMeters);
        public int MinimumFootprintWidthCells => MinimumFootprintSizeCells.x;
        public int MinimumFootprintDepthCells => MinimumFootprintSizeCells.y;
        public Vector2Int MinimumFootprintSizeCells => ResolveMinimumFootprintSizeCells();
        public Vector3 FrontDoorLocalPoint => frontDoorLocalPoint;
        public bool UsesFrontPointAsRoadAccessAnchor => useFrontPointAsRoadAccessAnchor;
        public Vector3 FrontLeftCornerLocal => frontLeftCornerLocal;
        public Vector3 FrontRightCornerLocal => frontRightCornerLocal;
        public Vector3 BackRightCornerLocal => backRightCornerLocal;
        public Vector3 BackLeftCornerLocal => backLeftCornerLocal;
        public Vector3 RoadAccessLocalPoint => useFrontPointAsRoadAccessAnchor ? frontDoorLocalPoint : FrontEdgeCenterLocalPoint;
        public Vector3 FrontEdgeCenterLocalPoint => (frontLeftCornerLocal + frontRightCornerLocal) * 0.5f;
        public Vector3 BackEdgeCenterLocalPoint => (backLeftCornerLocal + backRightCornerLocal) * 0.5f;
        public Vector3 FrontOutwardLocalDirection => ResolveFrontOutwardLocalDirection();
        public IReadOnlyList<InteriorFloorAuthoring> InteriorFloors => interiorFloors;
        public IReadOnlyList<BuildingEntrancePairAuthoring> EntrancePairs => entrancePairs;
        public IReadOnlyList<BuildingActivityLocationAuthoring> ActivityLocations => activityLocations;

        /// <summary>
        /// Find an authored station of the requested type with a valid route from
        /// the specified entrance. This establishes geometry only; callers must
        /// separately check access rights and reserve the worker's time.
        /// </summary>
        public bool TryFindReachableActivityLocation(BuildingActivityLocationType type, string entranceId,
            int buildingInstanceId, out ResolvedBuildingActivityLocation location,
            out List<Vector3> worldWaypoints, out string reason)
        {
            location = default;
            worldWaypoints = null;
            reason = $"No reachable {type} activity is authored on '{name}'.";
            if (activityLocations == null) return false;
            if (!TryGetEntrancePair(entranceId, out var entrance, out reason)) return false;
            foreach (BuildingActivityLocationAuthoring candidate in activityLocations)
            {
                if (candidate == null || candidate.type != type) continue;
                if (!TryGetActivityLocation(candidate.id, buildingInstanceId, out var resolved, out string invalid))
                {
                    reason = invalid;
                    continue;
                }
                if (entrance.floorIndex != resolved.FloorIndex
                    || !MatchesSpace(entrance.functionalSpaceId, resolved.FunctionalSpaceId))
                {
                    reason = $"Activity '{candidate.id}' is on a different floor or functional space from entrance '{entranceId}'.";
                    continue;
                }
                if (!TryBuildInteriorActivityRoute(entranceId, candidate.id, out var route, out string blocked))
                {
                    reason = blocked;
                    continue;
                }
                location = resolved;
                worldWaypoints = route;
                reason = string.Empty;
                return true;
            }
            return false;
        }

        public bool TryGetActivityLocation(string id, int buildingInstanceId, out ResolvedBuildingActivityLocation location, out string reason)
        {
            location = default;
            reason = string.Empty;
            if (activityLocations == null || string.IsNullOrWhiteSpace(id))
            {
                reason = "Activity location id is empty or no locations are authored.";
                return false;
            }

            BuildingActivityLocationAuthoring match = null;
            foreach (BuildingActivityLocationAuthoring candidate in activityLocations)
            {
                if (candidate == null || !string.Equals(candidate.id, id, System.StringComparison.Ordinal)) continue;
                if (match != null)
                {
                    reason = $"Activity location id '{id}' is duplicated on '{name}'.";
                    return false;
                }
                match = candidate;
            }

            if (match == null)
            {
                reason = $"Activity location '{id}' is not authored on '{name}'.";
                return false;
            }
            if (!IsFinite(match.positionLocal) || !IsFinite(match.forwardLocal) || match.forwardLocal.sqrMagnitude < 0.0001f)
            {
                reason = $"Activity location '{id}' has an invalid position or orientation.";
                return false;
            }
            if (match.sourceObject != null && !match.sourceObject.IsChildOf(transform))
            {
                reason = $"Activity location '{id}' references an object outside building '{name}'.";
                return false;
            }
            if (match.requiresWalkableInterior
                && !TryEvaluateInteriorLocalPoint(match.positionLocal, match.floorIndex, match.functionalSpaceId, out reason))
            {
                reason = $"Activity location '{id}' is unreachable: {reason}";
                return false;
            }

            Vector3 worldForward = transform.TransformDirection(match.forwardLocal).normalized;
            if (worldForward.sqrMagnitude < 0.0001f)
            {
                reason = $"Activity location '{id}' has no usable world orientation.";
                return false;
            }
            location = new ResolvedBuildingActivityLocation(
                buildingInstanceId,
                match.id,
                match.displayName,
                match.type,
                transform.TransformPoint(match.positionLocal),
                worldForward,
                match.floorIndex,
                match.functionalSpaceId,
                match.requiresWalkableInterior,
                match.optionalRequirements);
            return true;
        }

        public bool TryGetEntrancePair(string id, out BuildingEntrancePairAuthoring pair, out string reason)
        {
            pair = null;
            reason = string.Empty;
            if (entrancePairs == null || string.IsNullOrWhiteSpace(id))
            {
                reason = "Entrance-pair id is empty or no pairs are authored.";
                return false;
            }
            foreach (BuildingEntrancePairAuthoring candidate in entrancePairs)
            {
                if (candidate == null || !string.Equals(candidate.id, id, System.StringComparison.Ordinal)) continue;
                if (pair != null)
                {
                    reason = $"Entrance-pair id '{id}' is duplicated on '{name}'.";
                    pair = null;
                    return false;
                }
                pair = candidate;
            }
            if (pair == null)
            {
                reason = $"Entrance pair '{id}' is not authored on '{name}'.";
                return false;
            }
            if (!IsFinite(pair.indoorPositionLocal) || !IsFinite(pair.outdoorApproachLocal)
                || new Vector2(pair.outdoorApproachLocal.x - pair.indoorPositionLocal.x,
                    pair.outdoorApproachLocal.z - pair.indoorPositionLocal.z).sqrMagnitude < 0.04f
                || pair.clearWidthMeters <= 0f)
            {
                reason = $"Entrance pair '{id}' has invalid points or clearance.";
                pair = null;
                return false;
            }
            if (pair.sourceTransform != null && !pair.sourceTransform.IsChildOf(transform))
            {
                reason = $"Entrance pair '{id}' references an object outside building '{name}'.";
                pair = null;
                return false;
            }
            if (HasInteriorWalkableArea(pair.floorIndex, pair.functionalSpaceId)
                && !TryEvaluateInteriorLocalPoint(pair.indoorPositionLocal, pair.floorIndex, pair.functionalSpaceId, out reason))
            {
                reason = $"Entrance pair '{id}' has an unreachable indoor point: {reason}";
                pair = null;
                return false;
            }
            return true;
        }

        public bool AllowsDoorwayBoundaryTransition(Vector3 fromWorld, Vector3 toWorld)
        {
            if (entrancePairs == null) return false;
            foreach (BuildingEntrancePairAuthoring pair in entrancePairs)
            {
                if (pair == null || pair.clearWidthMeters <= 0f) continue;
                Vector3 inside = transform.TransformPoint(pair.indoorPositionLocal);
                Vector3 outside = transform.TransformPoint(pair.outdoorApproachLocal);
                Vector3 axis = outside - inside;
                axis.y = 0f;
                if (axis.sqrMagnitude < 0.0001f) continue;
                axis.Normalize();
                Vector3 threshold = (inside + outside) * 0.5f;
                float fromSide = Vector3.Dot(new Vector3(fromWorld.x - threshold.x, 0f, fromWorld.z - threshold.z), axis);
                float toSide = Vector3.Dot(new Vector3(toWorld.x - threshold.x, 0f, toWorld.z - threshold.z), axis);
                if (fromSide * toSide > 0f || Mathf.Abs(fromSide - toSide) < 0.0001f) continue;
                float t = Mathf.Clamp01(fromSide / (fromSide - toSide));
                Vector3 crossing = Vector3.Lerp(fromWorld, toWorld, t);
                Vector2 lateral = new(crossing.x - threshold.x, crossing.z - threshold.z);
                if (lateral.sqrMagnitude <= pair.clearWidthMeters * pair.clearWidthMeters * 0.25f) return true;
            }
            return false;
        }

        public bool TryBuildInteriorActivityRoute(string entranceId, string activityId,
            out List<Vector3> worldWaypoints, out string reason)
        {
            List<Vector3> waypoints = new();
            worldWaypoints = waypoints;
            reason = string.Empty;
            if (!TryGetEntrancePair(entranceId, out BuildingEntrancePairAuthoring entrance, out reason)
                || !TryGetActivityLocation(activityId, 0, out ResolvedBuildingActivityLocation activity, out reason)) return false;
            InteriorFloorAuthoring floor = FindInteriorFloor(activity.FloorIndex);
            List<InteriorPolygonAuthoring> boundaries = floor?.walkableAreas?
                .FindAll(area => area != null && MatchesSpace(area.functionalSpaceId, activity.FunctionalSpaceId));
            if (boundaries == null || boundaries.Count == 0 || boundaries.Exists(area => area.vertices == null || area.vertices.Count < 3))
            {
                reason = $"Activity '{activityId}' has no matching walkable floor polygon.";
                return false;
            }

            float minX = boundaries.SelectMany(area => area.vertices).Min(v => v.x);
            float maxX = boundaries.SelectMany(area => area.vertices).Max(v => v.x);
            float minZ = boundaries.SelectMany(area => area.vertices).Min(v => v.z);
            float maxZ = boundaries.SelectMany(area => area.vertices).Max(v => v.z);
            const float step = 0.4f;
            int width = Mathf.CeilToInt((maxX - minX) / step) + 1;
            int depth = Mathf.CeilToInt((maxZ - minZ) / step) + 1;
            if (width <= 1 || depth <= 1 || width * depth > 10000)
            {
                reason = "Walkable floor bounds are too small or too large for an interior route.";
                return false;
            }
            Vector3 Node(int x, int z) => new(minX + x * step, floor.floorHeightLocal, minZ + z * step);
            bool Walkable(int x, int z) => x >= 0 && x < width && z >= 0 && z < depth
                && TryEvaluateInteriorLocalPoint(Node(x, z), activity.FloorIndex, activity.FunctionalSpaceId, out _);
            bool TryNearest(Vector3 point, out Vector2Int cell)
            {
                int cx = Mathf.Clamp(Mathf.RoundToInt((point.x - minX) / step), 0, width - 1);
                int cz = Mathf.Clamp(Mathf.RoundToInt((point.z - minZ) / step), 0, depth - 1);
                float best = float.PositiveInfinity; cell = default;
                for (int radius = 0; radius < Mathf.Max(width, depth); radius++)
                {
                    bool found = false;
                    for (int z = Mathf.Max(0, cz - radius); z <= Mathf.Min(depth - 1, cz + radius); z++)
                        for (int x = Mathf.Max(0, cx - radius); x <= Mathf.Min(width - 1, cx + radius); x++)
                        {
                            if (!Walkable(x, z)) continue;
                            float distance = (Node(x,z) - point).sqrMagnitude;
                            if (distance < best) { best = distance; cell = new Vector2Int(x,z); found = true; }
                        }
                    if (found) return true;
                }
                return false;
            }
            Vector3 activityLocal = transform.InverseTransformPoint(activity.WorldPosition);
            if (!TryNearest(entrance.indoorPositionLocal, out Vector2Int start)
                || !TryNearest(activityLocal, out Vector2Int target))
            {
                reason = "No walkable interior route point exists near the entrance or activity location.";
                return false;
            }
            int count = width * depth, startIndex = start.y * width + start.x, targetIndex = target.y * width + target.x;
            int[] parent = new int[count]; float[] score = new float[count]; bool[] closed = new bool[count];
            Array.Fill(parent, -1); Array.Fill(score, float.PositiveInfinity); score[startIndex] = 0f;
            List<int> open = new() { startIndex };
            int[] dx = { 0, 1, 1, 1, 0, -1, -1, -1 }, dz = { 1, 1, 0, -1, -1, -1, 0, 1 };
            while (open.Count > 0)
            {
                int bestIndex = 0;
                for (int i = 1; i < open.Count; i++)
                {
                    int a = open[i], b = open[bestIndex];
                    Vector2Int ac = new(a % width, a / width), bc = new(b % width, b / width);
                    float af = score[a] + Vector2.Distance(ac, target), bf = score[b] + Vector2.Distance(bc, target);
                    if (af < bf) bestIndex = i;
                }
                int current = open[bestIndex]; open.RemoveAt(bestIndex);
                if (closed[current]) continue;
                closed[current] = true;
                if (current == targetIndex) break;
                int x = current % width, z = current / width;
                for (int i = 0; i < dx.Length; i++)
                {
                    int nx = x + dx[i], nz = z + dz[i];
                    if (!Walkable(nx,nz)) continue;
                    if (dx[i] != 0 && dz[i] != 0 && (!Walkable(x + dx[i],z) || !Walkable(x,z + dz[i]))) continue;
                    int next = nz * width + nx;
                    float candidate = score[current] + ((dx[i] == 0 || dz[i] == 0) ? 1f : 1.4142f);
                    if (candidate >= score[next]) continue;
                    score[next] = candidate; parent[next] = current; open.Add(next);
                }
            }
            if (!closed[targetIndex])
            {
                reason = $"No interior route connects entrance '{entranceId}' to activity '{activityId}' without crossing an exclusion.";
                return false;
            }
            List<int> route = new();
            for (int cursor = targetIndex; cursor >= 0; cursor = parent[cursor])
            {
                route.Add(cursor);
                if (cursor == startIndex) break;
            }
            route.Reverse();
            void Append(Vector3 local)
            {
                Vector3 world = transform.TransformPoint(local);
                if (waypoints.Count == 0 || Vector3.Distance(waypoints[^1], world) > 0.05f) waypoints.Add(world);
            }
            Append(entrance.indoorPositionLocal);
            foreach (int index in route) Append(Node(index % width, index / width));
            Append(activityLocal);
            return true;
        }

        public bool HasInteriorWalkableArea(int floorIndex, string functionalSpaceId = null)
        {
            if (interiorFloors == null) return false;
            foreach (InteriorFloorAuthoring floor in interiorFloors)
            {
                if (floor == null || floor.floorIndex != floorIndex || floor.walkableAreas == null) continue;
                foreach (InteriorPolygonAuthoring area in floor.walkableAreas)
                {
                    if (area != null && MatchesSpace(area.functionalSpaceId, functionalSpaceId)) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Builds a point-to-point path inside one authored residential floor.
        /// Both endpoints and every grid step are checked against its walkable
        /// polygons and furniture exclusions. Used when a resident already
        /// stands inside a home; it does not grant access or occupancy.
        /// </summary>
        public bool TryBuildInteriorPath(Vector3 startWorld, Vector3 targetWorld, int floorIndex,
            string functionalSpaceId, out List<Vector3> worldWaypoints, out string reason)
        {
            worldWaypoints = new List<Vector3>();
            reason = string.Empty;
            InteriorFloorAuthoring floor = FindInteriorFloor(floorIndex);
            List<InteriorPolygonAuthoring> areas = floor?.walkableAreas?
                .FindAll(area => area != null && MatchesSpace(area.functionalSpaceId, functionalSpaceId));
            if (areas == null || areas.Count == 0 || areas.Exists(area => area.vertices == null || area.vertices.Count < 3))
            {
                reason = $"No valid walkable polygon exists for floor {floorIndex} and functional space '{DisplaySpace(functionalSpaceId)}'.";
                return false;
            }
            Vector3 startLocal = transform.InverseTransformPoint(startWorld);
            Vector3 targetLocal = transform.InverseTransformPoint(targetWorld);
            if (!TryEvaluateInteriorLocalPoint(startLocal, floorIndex, functionalSpaceId, out reason)) return false;

            float minX = areas.SelectMany(area => area.vertices).Min(v => v.x);
            float maxX = areas.SelectMany(area => area.vertices).Max(v => v.x);
            float minZ = areas.SelectMany(area => area.vertices).Min(v => v.z);
            float maxZ = areas.SelectMany(area => area.vertices).Max(v => v.z);
            const float step = 0.4f;
            int width = Mathf.CeilToInt((maxX - minX) / step) + 1;
            int depth = Mathf.CeilToInt((maxZ - minZ) / step) + 1;
            if (width <= 1 || depth <= 1 || width * depth > 10000)
            {
                reason = "Walkable floor bounds are too small or too large for an interior route.";
                return false;
            }
            Vector3 Node(int x, int z) => new(minX + x * step, floor.floorHeightLocal, minZ + z * step);
            bool Walkable(int x, int z) => x >= 0 && x < width && z >= 0 && z < depth
                && TryEvaluateInteriorLocalPoint(Node(x, z), floorIndex, functionalSpaceId, out _);
            bool Nearest(Vector3 point, out Vector2Int cell)
            {
                int cx = Mathf.Clamp(Mathf.RoundToInt((point.x - minX) / step), 0, width - 1);
                int cz = Mathf.Clamp(Mathf.RoundToInt((point.z - minZ) / step), 0, depth - 1);
                float best = float.PositiveInfinity; cell = default;
                for (int radius = 0; radius < Mathf.Max(width, depth); radius++)
                {
                    bool found = false;
                    for (int z = Mathf.Max(0, cz - radius); z <= Mathf.Min(depth - 1, cz + radius); z++)
                        for (int x = Mathf.Max(0, cx - radius); x <= Mathf.Min(width - 1, cx + radius); x++)
                        {
                            if (!Walkable(x, z)) continue;
                            float d = (Node(x, z) - point).sqrMagnitude;
                            if (d < best) { best = d; cell = new Vector2Int(x, z); found = true; }
                        }
                    if (found) return true;
                }
                return false;
            }
            if (!Nearest(startLocal, out Vector2Int start) || !Nearest(targetLocal, out Vector2Int target))
            {
                reason = "No walkable interior route point exists near the resident or activity location.";
                return false;
            }
            if (Vector3.Distance(Node(target.x, target.y), targetLocal) > 1f)
            {
                reason = "The destination is too far from the authored walkable floor to approach safely.";
                return false;
            }
            int count = width * depth, startIndex = start.y * width + start.x, targetIndex = target.y * width + target.x;
            int[] parent = new int[count]; float[] score = new float[count]; bool[] closed = new bool[count];
            Array.Fill(parent, -1); Array.Fill(score, float.PositiveInfinity); score[startIndex] = 0f;
            List<int> open = new() { startIndex };
            int[] dx = { 0, 1, 1, 1, 0, -1, -1, -1 }, dz = { 1, 1, 0, -1, -1, -1, 0, 1 };
            while (open.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < open.Count; i++)
                {
                    int a = open[i], b = open[best];
                    float af = score[a] + Vector2.Distance(new Vector2(a % width, a / width), target);
                    float bf = score[b] + Vector2.Distance(new Vector2(b % width, b / width), target);
                    if (af < bf) best = i;
                }
                int current = open[best]; open.RemoveAt(best);
                if (closed[current]) continue;
                closed[current] = true;
                if (current == targetIndex) break;
                int x = current % width, z = current / width;
                for (int i = 0; i < dx.Length; i++)
                {
                    int nx = x + dx[i], nz = z + dz[i];
                    if (!Walkable(nx, nz) || (dx[i] != 0 && dz[i] != 0
                        && (!Walkable(x + dx[i], z) || !Walkable(x, z + dz[i])))) continue;
                    int next = nz * width + nx;
                    float candidate = score[current] + ((dx[i] == 0 || dz[i] == 0) ? 1f : 1.4142f);
                    if (candidate >= score[next]) continue;
                    score[next] = candidate; parent[next] = current; open.Add(next);
                }
            }
            if (!closed[targetIndex])
            {
                reason = "No interior route connects the resident to the activity without crossing an exclusion.";
                return false;
            }
            List<int> route = new();
            for (int cursor = targetIndex; cursor >= 0; cursor = parent[cursor])
            {
                route.Add(cursor);
                if (cursor == startIndex) break;
            }
            route.Reverse();
            worldWaypoints.Add(startWorld);
            foreach (int index in route)
            {
                Vector3 world = transform.TransformPoint(Node(index % width, index / width));
                if (Vector3.Distance(worldWaypoints[^1], world) > 0.05f) worldWaypoints.Add(world);
            }
            return true;
        }

        public bool TryEvaluateInteriorWorldPoint(
            Vector3 worldPoint,
            int floorIndex,
            string functionalSpaceId,
            out string reason)
        {
            return TryEvaluateInteriorLocalPoint(transform.InverseTransformPoint(worldPoint), floorIndex, functionalSpaceId, out reason);
        }

        public bool TryEvaluateInteriorLocalPoint(
            Vector3 localPoint,
            int floorIndex,
            string functionalSpaceId,
            out string reason)
        {
            reason = string.Empty;
            InteriorFloorAuthoring floor = FindInteriorFloor(floorIndex);
            if (floor == null)
            {
                reason = $"No authored interior floor {floorIndex} exists on '{name}'.";
                return false;
            }

            bool hasMatchingArea = false;
            bool insideWalkableArea = false;
            if (floor.walkableAreas != null)
            {
                foreach (InteriorPolygonAuthoring area in floor.walkableAreas)
                {
                    if (area == null || !MatchesSpace(area.functionalSpaceId, functionalSpaceId)) continue;
                    hasMatchingArea = true;
                    if (!TryValidateInteriorPolygon(area.vertices, out string validationError))
                    {
                        reason = $"Walkable area '{area.label}' is invalid: {validationError}";
                        return false;
                    }

                    if (ContainsPoint(area.vertices, localPoint)) insideWalkableArea = true;
                }
            }

            if (!hasMatchingArea)
            {
                reason = $"No walkable area is authored for floor {floorIndex} and functional space '{DisplaySpace(functionalSpaceId)}'.";
                return false;
            }

            if (!insideWalkableArea)
            {
                reason = $"Point is outside the authored walkable floor area on floor {floorIndex}.";
                return false;
            }

            if (floor.exclusionAreas != null)
            {
                foreach (InteriorPolygonAuthoring exclusion in floor.exclusionAreas)
                {
                    if (exclusion == null || !MatchesSpace(exclusion.functionalSpaceId, functionalSpaceId)) continue;
                    if (!TryValidateInteriorPolygon(exclusion.vertices, out string validationError))
                    {
                        reason = $"Furniture exclusion '{exclusion.label}' is invalid: {validationError}";
                        return false;
                    }

                    if (ContainsPoint(exclusion.vertices, localPoint))
                    {
                        reason = $"Point is blocked by furniture exclusion '{exclusion.label}'.";
                        return false;
                    }
                }
            }

            return true;
        }

        public static bool TryValidateInteriorPolygon(IReadOnlyList<Vector3> vertices, out string error)
        {
            error = string.Empty;
            if (vertices == null || vertices.Count < 3)
            {
                error = "at least three vertices are required";
                return false;
            }

            const float duplicateDistanceSquared = 0.000001f;
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 point = vertices[i];
                if (float.IsNaN(point.x) || float.IsInfinity(point.x)
                    || float.IsNaN(point.y) || float.IsInfinity(point.y)
                    || float.IsNaN(point.z) || float.IsInfinity(point.z))
                {
                    error = $"vertex {i + 1} contains a non-finite coordinate";
                    return false;
                }

                for (int j = i + 1; j < vertices.Count; j++)
                {
                    Vector3 other = vertices[j];
                    if (new Vector2(point.x - other.x, point.z - other.z).sqrMagnitude <= duplicateDistanceSquared)
                    {
                        error = $"vertices {i + 1} and {j + 1} are duplicates";
                        return false;
                    }
                }
            }

            for (int i = 0; i < vertices.Count; i++)
            {
                int iNext = (i + 1) % vertices.Count;
                for (int j = i + 1; j < vertices.Count; j++)
                {
                    int jNext = (j + 1) % vertices.Count;
                    if (iNext == j || jNext == i) continue;
                    if (SegmentsIntersect(ToXZ(vertices[i]), ToXZ(vertices[iNext]), ToXZ(vertices[j]), ToXZ(vertices[jNext])))
                    {
                        error = $"edges {i + 1} and {j + 1} intersect";
                        return false;
                    }
                }
            }

            float twiceArea = 0f;
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 a = vertices[i];
                Vector3 b = vertices[(i + 1) % vertices.Count];
                twiceArea += a.x * b.z - b.x * a.z;
            }

            if (Mathf.Abs(twiceArea) < 0.02f)
            {
                error = "polygon area is too small";
                return false;
            }

            if (twiceArea < 0f)
            {
                error = "vertices must use counter-clockwise winding in building-local XZ";
                return false;
            }

            return true;
        }

        private InteriorFloorAuthoring FindInteriorFloor(int floorIndex)
        {
            if (interiorFloors == null) return null;
            foreach (InteriorFloorAuthoring floor in interiorFloors)
                if (floor != null && floor.floorIndex == floorIndex) return floor;
            return null;
        }

        private static bool MatchesSpace(string polygonSpaceId, string querySpaceId)
        {
            if (string.IsNullOrWhiteSpace(querySpaceId)) return string.IsNullOrWhiteSpace(polygonSpaceId);
            return string.IsNullOrWhiteSpace(polygonSpaceId)
                || string.Equals(polygonSpaceId.Trim(), querySpaceId.Trim(), System.StringComparison.Ordinal);
        }

        private static string DisplaySpace(string spaceId)
            => string.IsNullOrWhiteSpace(spaceId) ? "shared floor" : spaceId;

        private static Vector2 ToXZ(Vector3 point) => new(point.x, point.z);

        private static bool ContainsPoint(IReadOnlyList<Vector3> polygon, Vector3 point)
        {
            Vector2 p = ToXZ(point);
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = ToXZ(polygon[j]);
                Vector2 b = ToXZ(polygon[i]);
                if (DistanceToSegment(p, a, b) <= 0.001f) return true;
                if ((a.y > p.y) != (b.y > p.y)
                    && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }

            return inside;
        }

        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            const float epsilon = 0.00001f;
            float abC = Cross(b - a, c - a);
            float abD = Cross(b - a, d - a);
            float cdA = Cross(d - c, a - c);
            float cdB = Cross(d - c, b - c);
            if (Mathf.Abs(abC) <= epsilon && PointOnSegment(c, a, b)) return true;
            if (Mathf.Abs(abD) <= epsilon && PointOnSegment(d, a, b)) return true;
            if (Mathf.Abs(cdA) <= epsilon && PointOnSegment(a, c, d)) return true;
            if (Mathf.Abs(cdB) <= epsilon && PointOnSegment(b, c, d)) return true;
            return (abC > 0f) != (abD > 0f) && (cdA > 0f) != (cdB > 0f);
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool PointOnSegment(Vector2 point, Vector2 a, Vector2 b)
            => point.x >= Mathf.Min(a.x, b.x) - 0.00001f
                && point.x <= Mathf.Max(a.x, b.x) + 0.00001f
                && point.y >= Mathf.Min(a.y, b.y) - 0.00001f
                && point.y <= Mathf.Max(a.y, b.y) + 0.00001f;

        /// <summary>Returns whether a local-space XZ point lies inside the authored footprint polygon.</summary>
        public bool ContainsFootprintLocalPoint(Vector3 localPoint, float toleranceMeters = 0.25f)
        {
            Vector3[] corners = { frontLeftCornerLocal, frontRightCornerLocal, backRightCornerLocal, backLeftCornerLocal };
            Vector2 point = new(localPoint.x, localPoint.z);
            bool inside = false;
            float tolerance = Mathf.Max(0f, toleranceMeters);

            for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
            {
                Vector2 a = new(corners[j].x, corners[j].z);
                Vector2 b = new(corners[i].x, corners[i].z);
                if (DistanceToSegment(point, a, b) <= tolerance)
                {
                    return true;
                }

                bool crosses = (a.y > point.y) != (b.y > point.y)
                    && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
                if (crosses)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 edge = b - a;
            float lengthSquared = edge.sqrMagnitude;
            if (lengthSquared <= 0.000001f)
            {
                return Vector2.Distance(point, a);
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - a, edge) / lengthSquared);
            return Vector2.Distance(point, a + edge * t);
        }

        public bool TryGetRoadAccessLocalPose(out Vector3 localPoint, out Vector3 localForward)
        {
            localPoint = RoadAccessLocalPoint;
            localForward = ResolveFrontOutwardLocalDirection();
            return localForward.sqrMagnitude > 0.0001f;
        }

        public Vector3 ResolveRoadAccessWorldPoint(Vector3 rootWorldPosition, Quaternion rootWorldRotation, Vector3 rootWorldScale)
        {
            Vector3 scaledLocalPoint = Vector3.Scale(RoadAccessLocalPoint, rootWorldScale);
            return rootWorldPosition + rootWorldRotation * scaledLocalPoint;
        }

        public Vector2Int ReconcileWithDefinitionFootprint(Vector2Int definitionFootprintSizeCells)
        {
            return ReconcileWithDefinitionFootprint(definitionFootprintSizeCells, out _, out _, out _);
        }

        public Vector2Int ReconcileWithDefinitionFootprint(
            Vector2Int definitionFootprintSizeCells,
            out Vector2Int authorityMinimumSizeCells,
            out bool expandedByAuthority,
            out string note)
        {
            Vector2Int definitionMinimum = SanitizeCellSize(definitionFootprintSizeCells);
            authorityMinimumSizeCells = SanitizeCellSize(MinimumFootprintSizeCells);
            Vector2Int resolved = new(
                Mathf.Max(definitionMinimum.x, authorityMinimumSizeCells.x),
                Mathf.Max(definitionMinimum.y, authorityMinimumSizeCells.y));

            expandedByAuthority = resolved.x > definitionMinimum.x || resolved.y > definitionMinimum.y;
            note = expandedByAuthority
                ? $"Prefab footprint authority expanded the grid claim from definition {definitionMinimum.x}x{definitionMinimum.y} to {resolved.x}x{resolved.y}. Visual prefab scale remains authored."
                : $"Definition footprint {definitionMinimum.x}x{definitionMinimum.y} satisfies prefab footprint authority minimum {authorityMinimumSizeCells.x}x{authorityMinimumSizeCells.y}. Visual prefab scale remains authored.";
            return resolved;
        }

        public bool TryBuildAuthoringWarningSummary(out string warningSummary)
        {
            List<string> warnings = new();
            float cellSize = AuthoringCellSizeMeters;

            if (!useCornerAuthoring)
            {
                warnings.Add("corner authoring is disabled; runtime minimum comes from integer fallback and renderer bounds");
            }

            Vector3 frontEdge = frontRightCornerLocal - frontLeftCornerLocal;
            Vector3 backEdge = backRightCornerLocal - backLeftCornerLocal;
            Vector3 depthLeft = frontLeftCornerLocal - backLeftCornerLocal;
            Vector3 depthRight = frontRightCornerLocal - backRightCornerLocal;
            Vector3 frontToBack = FrontEdgeCenterLocalPoint - BackEdgeCenterLocalPoint;

            if (frontEdge.magnitude < cellSize * 0.25f || backEdge.magnitude < cellSize * 0.25f)
            {
                warnings.Add("front/back edge span is nearly collapsed");
            }

            if (depthLeft.magnitude < cellSize * 0.25f || depthRight.magnitude < cellSize * 0.25f)
            {
                warnings.Add("side/depth edge span is nearly collapsed");
            }

            if (frontToBack.sqrMagnitude < 0.01f)
            {
                warnings.Add("front direction is ambiguous because front and back centers are nearly overlapping");
            }
            else
            {
                Vector3 outward = frontToBack.normalized;
                float frontDoorSignedDistance = Vector3.Dot(frontDoorLocalPoint - FrontEdgeCenterLocalPoint, outward);
                if (frontDoorSignedDistance < -0.05f)
                {
                    warnings.Add("front-door point sits behind the authored front edge");
                }
            }

            if (warnWhenRendererBoundsExceedCorners
                && TryResolveRendererMinimumFootprintSizeCells(out Vector2Int rendererMinimum, out RendererBoundsRead rendererRead))
            {
                Vector2Int authoredMinimum = MinimumFootprintSizeCells;
                int slack = Mathf.Max(0, rendererBoundsWarningSlackCells);
                if (rendererMinimum.x > authoredMinimum.x + slack || rendererMinimum.y > authoredMinimum.y + slack)
                {
                    warnings.Add($"building-shell renderer bounds suggest about {rendererMinimum.x}x{rendererMinimum.y} cells, larger than authored minimum {authoredMinimum.x}x{authoredMinimum.y}; {rendererRead.BuildSummary()}");
                }
                else if (rendererRead.IgnoredRendererCount > 0 && rendererRead.IncludedRendererCount == 0)
                {
                    warnings.Add($"renderer footprint cross-check skipped because only helper/outlier renderers were found; {rendererRead.BuildSummary()}");
                }
            }

            if (warnings.Count == 0)
            {
                warningSummary = string.Empty;
                return false;
            }

            StringBuilder builder = new();
            for (int i = 0; i < warnings.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append("; ");
                }

                builder.Append(warnings[i]);
            }

            warningSummary = builder.ToString();
            return true;
        }


        [ContextMenu("Fit Authoring Corners To Renderer Bounds")]
        private void FitAuthoringCornersToRendererBounds()
        {
            if (!TryGetLocalRendererBounds(out Bounds bounds))
            {
                return;
            }

            float y = 0f;
            frontLeftCornerLocal = new Vector3(bounds.min.x, y, bounds.max.z);
            frontRightCornerLocal = new Vector3(bounds.max.x, y, bounds.max.z);
            backRightCornerLocal = new Vector3(bounds.max.x, y, bounds.min.z);
            backLeftCornerLocal = new Vector3(bounds.min.x, y, bounds.min.z);
            NormalizeAuthoring();
        }

        private void Reset()
        {
            if (TryGetLocalRendererBounds(out Bounds bounds))
            {
                float y = 0f;
                frontLeftCornerLocal = new Vector3(bounds.min.x, y, bounds.max.z);
                frontRightCornerLocal = new Vector3(bounds.max.x, y, bounds.max.z);
                backRightCornerLocal = new Vector3(bounds.max.x, y, bounds.min.z);
                backLeftCornerLocal = new Vector3(bounds.min.x, y, bounds.min.z);
            }

            NormalizeAuthoring();
        }

        private void OnValidate()
        {
            NormalizeAuthoring();
        }

        private void OnDrawGizmosSelected()
        {
            DrawAuthoringGizmos();
        }

        private void NormalizeAuthoring()
        {
            authoringCellSizeMeters = Mathf.Max(0.1f, authoringCellSizeMeters);
            minimumFootprintWidthCells = Mathf.Max(1, minimumFootprintWidthCells);
            minimumFootprintDepthCells = Mathf.Max(1, minimumFootprintDepthCells);
            frontDoorOutwardOffsetMeters = Mathf.Max(0f, frontDoorOutwardOffsetMeters);

            FlattenPointToGround(ref frontLeftCornerLocal);
            FlattenPointToGround(ref frontRightCornerLocal);
            FlattenPointToGround(ref backRightCornerLocal);
            FlattenPointToGround(ref backLeftCornerLocal);
            FlattenPointToGround(ref frontDoorLocalPoint);

            if (autoCenterFrontDoorPoint)
            {
                frontDoorLocalPoint = ResolveDefaultFrontDoorLocalPoint();
            }
        }

        private Vector3 ResolveFrontOutwardLocalDirection()
        {
            Vector3 outward = FrontEdgeCenterLocalPoint - BackEdgeCenterLocalPoint;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.0001f)
            {
                return Vector3.forward;
            }

            return outward.normalized;
        }

        private Vector2Int ResolveMinimumFootprintSizeCells()
        {
            if (useCornerAuthoring)
            {
                return ResolveCornerMinimumFootprintSizeCells();
            }

            Vector2Int authoredMinimum = new(
                Mathf.Max(1, minimumFootprintWidthCells),
                Mathf.Max(1, minimumFootprintDepthCells));

            if (!TryResolveRendererMinimumFootprintSizeCells(out Vector2Int rendererMinimum))
            {
                return authoredMinimum;
            }

            return new Vector2Int(
                Mathf.Max(authoredMinimum.x, rendererMinimum.x),
                Mathf.Max(authoredMinimum.y, rendererMinimum.y));
        }

        private Vector2Int ResolveCornerMinimumFootprintSizeCells()
        {
            Vector3 min = frontLeftCornerLocal;
            Vector3 max = frontLeftCornerLocal;
            Encapsulate(ref min, ref max, frontRightCornerLocal);
            Encapsulate(ref min, ref max, backRightCornerLocal);
            Encapsulate(ref min, ref max, backLeftCornerLocal);

            float widthMeters = Mathf.Abs(max.x - min.x);
            float depthMeters = Mathf.Abs(max.z - min.z);
            float cellSize = AuthoringCellSizeMeters;

            return new Vector2Int(
                Mathf.Max(1, Mathf.CeilToInt(widthMeters / cellSize)),
                Mathf.Max(1, Mathf.CeilToInt(depthMeters / cellSize)));
        }

        private bool TryResolveRendererMinimumFootprintSizeCells(out Vector2Int size)
        {
            return TryResolveRendererMinimumFootprintSizeCells(out size, out _);
        }

        private bool TryResolveRendererMinimumFootprintSizeCells(out Vector2Int size, out RendererBoundsRead rendererRead)
        {
            size = default;
            if (!TryGetLocalRendererBounds(out Bounds bounds, out rendererRead))
            {
                return false;
            }

            float cellSize = AuthoringCellSizeMeters;
            size = new Vector2Int(
                Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(bounds.size.x) / cellSize)),
                Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(bounds.size.z) / cellSize)));
            return true;
        }

        private Vector3 ResolveDefaultFrontDoorLocalPoint()
        {
            Vector3 frontCenter = FrontEdgeCenterLocalPoint;
            Vector3 backCenter = BackEdgeCenterLocalPoint;
            Vector3 outward = ResolveFrontOutwardLocalDirection();
            return frontCenter + outward * frontDoorOutwardOffsetMeters;
        }

        private bool TryGetLocalRendererBounds(out Bounds bounds)
        {
            return TryGetLocalRendererBounds(out bounds, out _);
        }

        private bool TryGetLocalRendererBounds(out Bounds bounds, out RendererBoundsRead rendererRead)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            bool initialized = false;
            bounds = default;
            rendererRead = new RendererBoundsRead(renderers != null ? renderers.Length : 0);

            if (renderers == null)
            {
                return false;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!ShouldUseRendererForFootprintBounds(renderer, out string ignoredReason))
                {
                    rendererRead.RecordIgnored(renderer, ignoredReason);
                    continue;
                }

                if (!TryBuildRendererLocalBounds(renderer, out Bounds rendererLocalBounds))
                {
                    rendererRead.RecordIgnored(renderer, "invalid renderer bounds");
                    continue;
                }

                if (IsRendererBoundsOutlier(rendererLocalBounds, out string outlierReason))
                {
                    rendererRead.RecordIgnored(renderer, outlierReason);
                    continue;
                }

                rendererRead.RecordIncluded(renderer);
                if (!initialized)
                {
                    bounds = rendererLocalBounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(rendererLocalBounds);
                }
            }

            return initialized;
        }

        private static bool ShouldUseRendererForFootprintBounds(Renderer renderer, out string ignoredReason)
        {
            ignoredReason = string.Empty;
            if (renderer == null)
            {
                ignoredReason = "missing renderer";
                return false;
            }

            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
            {
                ignoredReason = "non-building renderer";
                return false;
            }

            string objectName = renderer.gameObject != null ? renderer.gameObject.name.ToLowerInvariant() : string.Empty;
            string rendererName = renderer.name != null ? renderer.name.ToLowerInvariant() : string.Empty;
            string combined = objectName + " " + rendererName;
            string[] helperTokens =
            {
                "anchor", "marker", "gizmo", "handle", "bounds", "collider", "trigger", "proxy",
                "preview", "icon", "terrain", "water", "route", "scenic", "evidence", "parcel",
                "settlement", "debug", "lod1", "lod2", "lod3", "shadow"
            };

            for (int i = 0; i < helperTokens.Length; i++)
            {
                if (combined.Contains(helperTokens[i]))
                {
                    ignoredReason = $"helper renderer '{helperTokens[i]}'";
                    return false;
                }
            }

            return true;
        }

        private bool TryBuildRendererLocalBounds(Renderer renderer, out Bounds localBounds)
        {
            localBounds = default;
            if (renderer == null)
            {
                return false;
            }

            Vector3 worldCenter = renderer.bounds.center;
            Vector3 worldExtents = renderer.bounds.extents;
            if (!IsFinite(worldCenter) || !IsFinite(worldExtents) || worldExtents.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            Vector3[] worldCorners =
            {
                worldCenter + new Vector3(-worldExtents.x, -worldExtents.y, -worldExtents.z),
                worldCenter + new Vector3(-worldExtents.x, -worldExtents.y, worldExtents.z),
                worldCenter + new Vector3(-worldExtents.x, worldExtents.y, -worldExtents.z),
                worldCenter + new Vector3(-worldExtents.x, worldExtents.y, worldExtents.z),
                worldCenter + new Vector3(worldExtents.x, -worldExtents.y, -worldExtents.z),
                worldCenter + new Vector3(worldExtents.x, -worldExtents.y, worldExtents.z),
                worldCenter + new Vector3(worldExtents.x, worldExtents.y, -worldExtents.z),
                worldCenter + new Vector3(worldExtents.x, worldExtents.y, worldExtents.z)
            };

            bool initialized = false;
            for (int cornerIndex = 0; cornerIndex < worldCorners.Length; cornerIndex++)
            {
                Vector3 localCorner = transform.InverseTransformPoint(worldCorners[cornerIndex]);
                if (!IsFinite(localCorner))
                {
                    continue;
                }

                if (!initialized)
                {
                    localBounds = new Bounds(localCorner, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    localBounds.Encapsulate(localCorner);
                }
            }

            return initialized;
        }

        private bool IsRendererBoundsOutlier(Bounds localBounds, out string reason)
        {
            reason = string.Empty;
            float maxDimension = Mathf.Max(Mathf.Abs(localBounds.size.x), Mathf.Abs(localBounds.size.z));
            if (maxDimension <= 0f)
            {
                reason = "empty local footprint bounds";
                return true;
            }

            Vector2 cornerSize = ResolveCornerFootprintSizeMeters();
            float authoredMax = Mathf.Max(cornerSize.x, cornerSize.y);
            float limit = Mathf.Max(rendererBoundsOutlierFloorMeters, Mathf.Max(AuthoringCellSizeMeters * 8f, authoredMax * 6f));
            if (maxDimension > limit)
            {
                reason = $"outlier bounds {maxDimension:0.#}m exceeds {limit:0.#}m diagnostic limit";
                return true;
            }

            return false;
        }

        private Vector2 ResolveCornerFootprintSizeMeters()
        {
            Vector3 min = frontLeftCornerLocal;
            Vector3 max = frontLeftCornerLocal;
            Encapsulate(ref min, ref max, frontRightCornerLocal);
            Encapsulate(ref min, ref max, backRightCornerLocal);
            Encapsulate(ref min, ref max, backLeftCornerLocal);
            return new Vector2(Mathf.Abs(max.x - min.x), Mathf.Abs(max.z - min.z));
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private struct RendererBoundsRead
        {
            private string sampleIgnoredRenderer;
            private string sampleIgnoredReason;

            public RendererBoundsRead(int totalRendererCount)
            {
                TotalRendererCount = Mathf.Max(0, totalRendererCount);
                IncludedRendererCount = 0;
                IgnoredRendererCount = 0;
                sampleIgnoredRenderer = string.Empty;
                sampleIgnoredReason = string.Empty;
            }

            public int TotalRendererCount { get; }
            public int IncludedRendererCount { get; private set; }
            public int IgnoredRendererCount { get; private set; }

            public void RecordIncluded(Renderer renderer)
            {
                IncludedRendererCount++;
            }

            public void RecordIgnored(Renderer renderer, string reason)
            {
                IgnoredRendererCount++;
                if (string.IsNullOrWhiteSpace(sampleIgnoredRenderer))
                {
                    sampleIgnoredRenderer = renderer != null ? renderer.name : string.Empty;
                    sampleIgnoredReason = reason ?? string.Empty;
                }
            }

            public string BuildSummary()
            {
                string ignored = IgnoredRendererCount > 0
                    ? ", ignored " + IgnoredRendererCount + (string.IsNullOrWhiteSpace(sampleIgnoredRenderer) ? string.Empty : $" (sample {sampleIgnoredRenderer}: {sampleIgnoredReason})")
                    : string.Empty;
                return $"renderer cross-check used {IncludedRendererCount}/{TotalRendererCount} renderer(s){ignored}";
            }
        }

        private void DrawAuthoringGizmos()
        {
            const float sphereScale = 0.14f;
            float radius = Mathf.Max(0.08f, AuthoringCellSizeMeters * sphereScale);
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;

            Gizmos.matrix = transform.localToWorldMatrix;

            Color outlineColor = new Color(0.2f, 0.9f, 0.25f, 1f);
            Color frontPointColor = new Color(0.45f, 0.24f, 0.06f, 1f);

            Vector3[] corners =
            {
                frontLeftCornerLocal,
                frontRightCornerLocal,
                backRightCornerLocal,
                backLeftCornerLocal
            };

            Gizmos.color = outlineColor;
            for (int i = 0; i < corners.Length; i++)
            {
                Gizmos.DrawSphere(corners[i], radius);
                Gizmos.DrawLine(corners[i], corners[(i + 1) % corners.Length]);
            }

            Gizmos.color = frontPointColor;
            Gizmos.DrawSphere(frontDoorLocalPoint, radius * 0.9f);
            Gizmos.DrawLine(FrontEdgeCenterLocalPoint, frontDoorLocalPoint);

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        private static Vector2Int SanitizeCellSize(Vector2Int size)
        {
            return new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
        }

        private static void FlattenPointToGround(ref Vector3 point)
        {
            point.y = 0f;
        }

        private static void Encapsulate(ref Vector3 min, ref Vector3 max, Vector3 point)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

#if UNITY_EDITOR
        internal void EditorRefreshAfterHandleMove(bool movedCornerHandle)
        {
            if (movedCornerHandle && autoCenterFrontDoorPoint)
            {
                frontDoorLocalPoint = ResolveDefaultFrontDoorLocalPoint();
            }

            NormalizeAuthoring();
            EditorUtility.SetDirty(this);
        }
#endif
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(BuildingFootprintAuthority))]
    internal sealed class BuildingFootprintAuthorityEditor : Editor
    {
        private static readonly Color MovableCornerHandleColor = new(0.2f, 0.9f, 0.25f, 1f);
        private static readonly Color LockedCornerHandleColor = new(0.9f, 0.15f, 0.12f, 1f);
        private static readonly Color FrontHandleColor = new(0.45f, 0.24f, 0.06f, 1f);
        private static readonly Color WalkableFillColor = new(0.2f, 0.9f, 0.35f, 0.18f);
        private static readonly Color WalkableOutlineColor = new(0.15f, 0.8f, 0.25f, 1f);
        private static readonly Color ExclusionFillColor = new(0.95f, 0.18f, 0.12f, 0.22f);
        private static readonly Color ExclusionOutlineColor = new(0.95f, 0.12f, 0.08f, 1f);
        private bool showInteriorAreas = true;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            BuildingFootprintAuthority authority = (BuildingFootprintAuthority)target;
            if (authority == null)
            {
                return;
            }

            Vector2Int minimum = authority.MinimumFootprintSizeCells;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                $"Runtime grid-claim minimum: {minimum.x}x{minimum.y} cells. This component claims placement footprint only; it does not scale or normalize the visual prefab.",
                MessageType.Info);

            if (authority.TryBuildAuthoringWarningSummary(out string warningSummary))
            {
                EditorGUILayout.HelpBox(warningSummary, MessageType.Warning);
            }

            serializedObject.Update();
            DrawInteriorFloorAuthoring();
            DrawEntranceAndActivityAuthoring();
            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            BuildingFootprintAuthority authority = (BuildingFootprintAuthority)target;
            if (authority == null)
            {
                return;
            }

            serializedObject.Update();

            bool movedCornerHandle = false;
            bool movedFrontHandle = false;

            SerializedProperty frontLeftCorner = serializedObject.FindProperty("frontLeftCornerLocal");
            SerializedProperty frontRightCorner = serializedObject.FindProperty("frontRightCornerLocal");
            SerializedProperty backRightCorner = serializedObject.FindProperty("backRightCornerLocal");
            SerializedProperty backLeftCorner = serializedObject.FindProperty("backLeftCornerLocal");

            movedCornerHandle |= DrawMoveHandle(frontLeftCorner, authority, MovableCornerHandleColor);
            DrawLockedHandle(frontRightCorner, authority, LockedCornerHandleColor);
            movedCornerHandle |= DrawMoveHandle(backRightCorner, authority, MovableCornerHandleColor);
            DrawLockedHandle(backLeftCorner, authority, LockedCornerHandleColor);
            movedFrontHandle |= DrawFreeMoveHandle(serializedObject.FindProperty("frontDoorLocalPoint"), authority, FrontHandleColor);

            bool movedInteriorHandle = showInteriorAreas && DrawInteriorPolygonHandles(authority);
            bool movedFunctionalHandle = DrawEntranceAndActivityHandles(authority);

            if (movedCornerHandle || movedFrontHandle || movedInteriorHandle || movedFunctionalHandle)
            {
                if (movedCornerHandle)
                {
                    SyncLockedCornersToEditableDiagonal(frontLeftCorner, frontRightCorner, backRightCorner, backLeftCorner);
                }

                serializedObject.ApplyModifiedProperties();
                authority.EditorRefreshAfterHandleMove(movedCornerHandle);
                if (movedInteriorHandle)
                {
                    EditorUtility.SetDirty(authority);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(authority);
                    SceneView.RepaintAll();
                }
                if (movedFunctionalHandle)
                {
                    EditorUtility.SetDirty(authority);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(authority);
                    SceneView.RepaintAll();
                }
            }
            else
            {
                serializedObject.ApplyModifiedProperties();
            }

            DrawInteriorPolygonsInScene(authority);
        }

        private void DrawInteriorPolygonsInScene(BuildingFootprintAuthority authority)
        {
            if (!showInteriorAreas) return;
            SerializedProperty floors = serializedObject.FindProperty("interiorFloors");
            if (floors == null) return;
            for (int f = 0; f < floors.arraySize; f++)
            {
                SerializedProperty floor = floors.GetArrayElementAtIndex(f);
                float height = floor.FindPropertyRelative("floorHeightLocal").floatValue;
                DrawPolygonListInScene(authority, floor.FindPropertyRelative("walkableAreas"), height, false);
                DrawPolygonListInScene(authority, floor.FindPropertyRelative("exclusionAreas"), height, true);
            }
        }

        private bool DrawInteriorPolygonHandles(BuildingFootprintAuthority authority)
        {
            bool moved = false;
            SerializedProperty floors = serializedObject.FindProperty("interiorFloors");
            if (floors == null) return false;
            for (int f = 0; f < floors.arraySize && !moved; f++)
            {
                SerializedProperty floor = floors.GetArrayElementAtIndex(f);
                float height = floor.FindPropertyRelative("floorHeightLocal").floatValue;
                moved |= DrawPolygonHandles(authority, floor.FindPropertyRelative("walkableAreas"), height, "walkable");
                if (!moved) moved |= DrawPolygonHandles(authority, floor.FindPropertyRelative("exclusionAreas"), height, "furniture exclusion");
            }

            return moved;
        }

        private void DrawPolygonListInScene(
            BuildingFootprintAuthority authority,
            SerializedProperty polygons,
            float floorHeight,
            bool exclusion)
        {
            if (polygons == null) return;
            for (int i = 0; i < polygons.arraySize; i++)
            {
                SerializedProperty polygon = polygons.GetArrayElementAtIndex(i);
                List<Vector3> points = ReadVertices(polygon.FindPropertyRelative("vertices"));
                if (points.Count < 2) continue;
                bool valid = BuildingFootprintAuthority.TryValidateInteriorPolygon(points, out string error);
                Color outline = valid ? (exclusion ? ExclusionOutlineColor : WalkableOutlineColor) : new Color(1f, 0.55f, 0f, 1f);
                Color fill = exclusion ? ExclusionFillColor : WalkableFillColor;
                List<Vector3> world = points.Select(p => authority.transform.TransformPoint(new Vector3(p.x, floorHeight, p.z)) + authority.transform.up * 0.025f).ToList();

                if (valid)
                {
                    List<int> triangles = TriangulateConcave(points);
                    using (new Handles.DrawingScope(fill))
                    {
                        for (int t = 0; t + 2 < triangles.Count; t += 3)
                            Handles.DrawAAConvexPolygon(world[triangles[t]], world[triangles[t + 1]], world[triangles[t + 2]]);
                    }
                }

                Vector3[] outlinePoints = new Vector3[world.Count + 1];
                world.CopyTo(outlinePoints);
                outlinePoints[outlinePoints.Length - 1] = world[0];
                using (new Handles.DrawingScope(outline)) Handles.DrawAAPolyLine(3f, outlinePoints);
                Vector3 center = Vector3.zero;
                foreach (Vector3 point in world) center += point;
                center /= world.Count;
                string label = polygon.FindPropertyRelative("label").stringValue;
                Handles.Label(center, valid ? label : label + " — " + error, EditorStyles.boldLabel);
            }
        }

        private bool DrawPolygonHandles(
            BuildingFootprintAuthority authority,
            SerializedProperty polygons,
            float floorHeight,
            string description)
        {
            if (polygons == null) return false;
            Transform root = authority.transform;
            for (int p = 0; p < polygons.arraySize; p++)
            {
                SerializedProperty vertices = polygons.GetArrayElementAtIndex(p).FindPropertyRelative("vertices");
                for (int v = 0; v < vertices.arraySize; v++)
                {
                    SerializedProperty vertex = vertices.GetArrayElementAtIndex(v);
                    Vector3 local = vertex.vector3Value;
                    local.y = floorHeight;
                    Vector3 world = root.TransformPoint(local);
                    float size = Mathf.Max(0.06f, HandleUtility.GetHandleSize(world) * 0.07f);
                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.Slider2D(
                        world,
                        root.up,
                        root.right,
                        root.forward,
                        size,
                        Handles.SphereHandleCap,
                        Vector2.zero);
                    if (!EditorGUI.EndChangeCheck()) continue;

                    Undo.RecordObject(authority, $"Move {description} polygon vertex");
                    Vector3 movedLocal = root.InverseTransformPoint(moved);
                    vertex.vector3Value = new Vector3(movedLocal.x, floorHeight, movedLocal.z);
                    return true;
                }
            }

            return false;
        }

        private static List<int> TriangulateConcave(IReadOnlyList<Vector3> polygon)
        {
            List<int> triangles = new();
            List<int> remaining = Enumerable.Range(0, polygon.Count).ToList();
            int guard = polygon.Count * polygon.Count;
            while (remaining.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int previous = remaining[(i + remaining.Count - 1) % remaining.Count];
                    int current = remaining[i];
                    int next = remaining[(i + 1) % remaining.Count];
                    Vector2 a = new(polygon[previous].x, polygon[previous].z);
                    Vector2 b = new(polygon[current].x, polygon[current].z);
                    Vector2 c = new(polygon[next].x, polygon[next].z);
                    if (Cross2D(b - a, c - b) <= 0.00001f) continue;

                    bool containsVertex = false;
                    for (int j = 0; j < remaining.Count; j++)
                    {
                        int test = remaining[j];
                        if (test == previous || test == current || test == next) continue;
                        if (PointInTriangle(new Vector2(polygon[test].x, polygon[test].z), a, b, c))
                        {
                            containsVertex = true;
                            break;
                        }
                    }

                    if (containsVertex) continue;
                    triangles.Add(previous);
                    triangles.Add(current);
                    triangles.Add(next);
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }

                if (!clipped) return new List<int>();
            }

            if (remaining.Count == 3)
            {
                triangles.AddRange(remaining);
            }
            return triangles;
        }

        private static float Cross2D(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            float ab = Cross2D(b - a, point - a);
            float bc = Cross2D(c - b, point - b);
            float ca = Cross2D(a - c, point - c);
            return ab >= -0.00001f && bc >= -0.00001f && ca >= -0.00001f;
        }

        private void DrawEntranceAndActivityAuthoring()
        {
            SerializedProperty entrances = serializedObject.FindProperty("entrancePairs");
            SerializedProperty activities = serializedObject.FindProperty("activityLocations");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Entrances and Activity Locations", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Entrance pairs describe an indoor threshold and outdoor approach. Activity points describe positions only; callers still enforce ownership, FunctionalSpace occupancy, equipment, and task permissions.",
                MessageType.Info);

            string discoveryMessage = null;
            if (GUILayout.Button("Discover Front Door and Create Entrance Pair"))
            {
                BuildingFootprintAuthority authority = (BuildingFootprintAuthority)target;
                if (TryDiscoverDoorwaySource(authority, out Transform source, out Vector3 sourceWorld,
                        out Vector3 outwardWorld, out float width, out string reason))
                {
                    int existing = FindEquivalentEntrance(entrances, source);
                    int index = existing >= 0 ? existing : entrances.arraySize;
                    if (existing < 0) entrances.InsertArrayElementAtIndex(index);
                    SerializedProperty pair = entrances.GetArrayElementAtIndex(index);
                    pair.FindPropertyRelative("id").stringValue = "main-entrance";
                    pair.FindPropertyRelative("sourceTransform").objectReferenceValue = source;
                    pair.FindPropertyRelative("indoorPositionLocal").vector3Value = authority.transform.InverseTransformPoint(sourceWorld - outwardWorld);
                    pair.FindPropertyRelative("outdoorApproachLocal").vector3Value = authority.transform.InverseTransformPoint(sourceWorld + outwardWorld);
                    pair.FindPropertyRelative("clearWidthMeters").floatValue = Mathf.Max(0.85f, width);
                    pair.FindPropertyRelative("floorIndex").intValue = 0;
                    pair.FindPropertyRelative("functionalSpaceId").stringValue = string.Empty;
                }
                else discoveryMessage = reason;
            }
            if (!string.IsNullOrWhiteSpace(discoveryMessage))
                EditorGUILayout.HelpBox(discoveryMessage + " You can author a manual pair below.", MessageType.Warning);

            if (GUILayout.Button("Add Manual Entrance Pair"))
            {
                int index = entrances.arraySize;
                entrances.InsertArrayElementAtIndex(index);
                SerializedProperty pair = entrances.GetArrayElementAtIndex(index);
                pair.FindPropertyRelative("id").stringValue = $"manual-entrance-{index + 1}";
                pair.FindPropertyRelative("sourceTransform").objectReferenceValue = null;
                pair.FindPropertyRelative("indoorPositionLocal").vector3Value = Vector3.zero;
                pair.FindPropertyRelative("outdoorApproachLocal").vector3Value = Vector3.forward;
                pair.FindPropertyRelative("clearWidthMeters").floatValue = 1f;
                pair.FindPropertyRelative("floorIndex").intValue = 0;
                pair.FindPropertyRelative("functionalSpaceId").stringValue = string.Empty;
            }

            DrawEntrancePairs(entrances, (BuildingFootprintAuthority)target);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Typed Activity Points", EditorStyles.boldLabel);
            if (activities == null) return;
            for (int i = 0; i < activities.arraySize; i++)
            {
                SerializedProperty activity = activities.GetArrayElementAtIndex(i);
                string label = activity.FindPropertyRelative("displayName").stringValue;
                int typeIndex = activity.FindPropertyRelative("type").enumValueIndex;
                activity.isExpanded = EditorGUILayout.Foldout(activity.isExpanded,
                    $"{label} ({(BuildingActivityLocationType)typeIndex})", true);
                if (!activity.isExpanded) continue;
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    EditorGUILayout.PropertyField(activity.FindPropertyRelative("id"));
                    EditorGUILayout.PropertyField(activity.FindPropertyRelative("displayName"));
                    EditorGUILayout.PropertyField(activity.FindPropertyRelative("type"));
                    EditorGUILayout.PropertyField(activity.FindPropertyRelative("floorIndex"));
                    EditorGUILayout.PropertyField(activity.FindPropertyRelative("positionLocal"));
                    EditorGUILayout.PropertyField(activity.FindPropertyRelative("forwardLocal"));
                    EditorGUILayout.PropertyField(activity.FindPropertyRelative("functionalSpaceId"));
                    EditorGUILayout.PropertyField(activity.FindPropertyRelative("requiresWalkableInterior"));
                    EditorGUILayout.PropertyField(activity.FindPropertyRelative("sourceObject"));
                    EditorGUILayout.PropertyField(activity.FindPropertyRelative("optionalRequirements"), true);
                    if (GUILayout.Button("Remove Activity Point"))
                    {
                        activities.DeleteArrayElementAtIndex(i);
                        break;
                    }

                    BuildingFootprintAuthority authority = (BuildingFootprintAuthority)target;
                    if (!authority.TryGetActivityLocation(activity.FindPropertyRelative("id").stringValue, 0, out _, out string error))
                        EditorGUILayout.HelpBox(error, MessageType.Error);
                    else
                        EditorGUILayout.HelpBox("Valid position and orientation.", MessageType.Info);
                }
            }

            if (GUILayout.Button("Add Activity Point"))
            {
                int index = activities.arraySize;
                activities.InsertArrayElementAtIndex(index);
                SerializedProperty activity = activities.GetArrayElementAtIndex(index);
                activity.FindPropertyRelative("id").stringValue = $"activity-{index + 1}";
                activity.FindPropertyRelative("displayName").stringValue = "Activity Location";
                activity.FindPropertyRelative("type").enumValueIndex = (int)BuildingActivityLocationType.Workshop;
                activity.FindPropertyRelative("floorIndex").intValue = 0;
                activity.FindPropertyRelative("positionLocal").vector3Value = Vector3.zero;
                activity.FindPropertyRelative("forwardLocal").vector3Value = Vector3.forward;
                activity.FindPropertyRelative("functionalSpaceId").stringValue = string.Empty;
                activity.FindPropertyRelative("requiresWalkableInterior").boolValue = true;
                activity.FindPropertyRelative("sourceObject").objectReferenceValue = null;
                activity.FindPropertyRelative("optionalRequirements").ClearArray();
                activity.isExpanded = true;
            }
        }

        private void DrawEntrancePairs(SerializedProperty entrances, BuildingFootprintAuthority authority)
        {
            if (entrances == null) return;
            for (int i = 0; i < entrances.arraySize; i++)
            {
                SerializedProperty pair = entrances.GetArrayElementAtIndex(i);
                string id = pair.FindPropertyRelative("id").stringValue;
                pair.isExpanded = EditorGUILayout.Foldout(pair.isExpanded, $"Entrance Pair: {id}", true);
                if (!pair.isExpanded) continue;
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    EditorGUILayout.PropertyField(pair.FindPropertyRelative("id"));
                    EditorGUILayout.PropertyField(pair.FindPropertyRelative("sourceTransform"), new GUIContent("Door / Marker Source"));
                    EditorGUILayout.PropertyField(pair.FindPropertyRelative("indoorPositionLocal"), new GUIContent("Indoor Threshold (Local)"));
                    EditorGUILayout.PropertyField(pair.FindPropertyRelative("outdoorApproachLocal"), new GUIContent("Outdoor Approach (Local)"));
                    EditorGUILayout.PropertyField(pair.FindPropertyRelative("clearWidthMeters"));
                    EditorGUILayout.PropertyField(pair.FindPropertyRelative("floorIndex"));
                    EditorGUILayout.PropertyField(pair.FindPropertyRelative("functionalSpaceId"));
                    if (GUILayout.Button("Remove Entrance Pair"))
                    {
                        entrances.DeleteArrayElementAtIndex(i);
                        break;
                    }
                    if (!authority.TryGetEntrancePair(id, out _, out string error))
                        EditorGUILayout.HelpBox(error, MessageType.Error);
                    else
                        EditorGUILayout.HelpBox("Valid paired doorway.", MessageType.Info);
                }
            }
        }

        private static int FindEquivalentEntrance(SerializedProperty entrances, Transform source)
        {
            if (entrances == null || source == null) return -1;
            for (int i = 0; i < entrances.arraySize; i++)
                if (entrances.GetArrayElementAtIndex(i).FindPropertyRelative("sourceTransform").objectReferenceValue == source) return i;
            return -1;
        }

        private static bool TryDiscoverDoorwaySource(BuildingFootprintAuthority authority, out Transform source,
            out Vector3 worldPosition, out Vector3 outwardWorld, out float clearWidth, out string reason)
        {
            source = null;
            worldPosition = Vector3.zero;
            outwardWorld = Vector3.zero;
            clearWidth = 1.1f;
            reason = string.Empty;
            BuildingAnchorMarker[] frontMarkers = authority.GetComponentsInChildren<BuildingAnchorMarker>(true)
                .Where(marker => marker != null && marker.Type == AnchorType.FrontDoor).ToArray();
            if (frontMarkers.Length == 1)
            {
                source = frontMarkers[0].transform;
                worldPosition = source.position;
                outwardWorld = source.forward;
                return NormalizeDoorDirection(authority, ref outwardWorld, out reason);
            }
            if (frontMarkers.Length > 1)
            {
                reason = "More than one authored FrontDoor marker exists. Select the intended entrance source explicitly.";
                return false;
            }

            Transform namedAnchor = FindChildByExactName(authority.transform, "Anchor_FrontDoor");
            if (namedAnchor != null)
            {
                source = namedAnchor;
                worldPosition = source.position;
                outwardWorld = source.forward;
                return NormalizeDoorDirection(authority, ref outwardWorld, out reason);
            }

            List<Renderer> candidates = new();
            foreach (Renderer renderer in authority.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled || IsLodOne(renderer.transform, authority.transform)) continue;
                string rendererName = renderer.name.ToLowerInvariant();
                string meshName = renderer is MeshRenderer ? renderer.GetComponent<MeshFilter>()?.sharedMesh?.name.ToLowerInvariant() : string.Empty;
                if ((!HasDoorwayTerm(rendererName) && !HasDoorwayTerm(meshName))
                    || rendererName.Contains("frame") || rendererName.Contains("handle") || rendererName.Contains("knob")) continue;
                candidates.Add(renderer);
            }
            if (candidates.Count != 1)
            {
                reason = candidates.Count == 0
                    ? "No unique door geometry or authored FrontDoor marker was found. Assign a source manually or add a BuildingAnchorMarker."
                    : $"Doorway discovery is ambiguous ({candidates.Count} LOD0 geometry candidates). Assign the intended source manually.";
                return false;
            }

            Renderer candidate = candidates[0];
            source = candidate.transform;
            worldPosition = candidate.bounds.center;
            outwardWorld = authority.transform.TransformDirection(authority.FrontOutwardLocalDirection);
            clearWidth = Mathf.Max(0.85f, Mathf.Max(candidate.bounds.size.x, candidate.bounds.size.z));
            return NormalizeDoorDirection(authority, ref outwardWorld, out reason);
        }

        private static bool NormalizeDoorDirection(BuildingFootprintAuthority authority, ref Vector3 outward, out string reason)
        {
            reason = string.Empty;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.0001f) outward = authority.transform.TransformDirection(authority.FrontOutwardLocalDirection);
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.0001f)
            {
                reason = "Doorway orientation is ambiguous. Set the front authoring direction or provide a manual marker.";
                return false;
            }
            outward.Normalize();
            return true;
        }

        private static bool HasDoorwayTerm(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return value.Contains("door") || value.Contains("entrance") || value.Contains("entry")
                || value.Contains("threshold") || value.Contains("portal") || value.Contains("gate");
        }

        private static Transform FindChildByExactName(Transform root, string exactName)
        {
            if (root == null) return null;
            foreach (Transform child in root)
            {
                if (string.Equals(child.name, exactName, StringComparison.Ordinal)) return child;
                Transform nested = FindChildByExactName(child, exactName);
                if (nested != null) return nested;
            }
            return null;
        }

        private static bool IsLodOne(Transform value, Transform root)
        {
            for (Transform current = value; current != null && current != root; current = current.parent)
            {
                string name = current.name.ToLowerInvariant().Replace(" ", string.Empty);
                if (name.Contains("_lod1") || name.Contains("lod_1") || name.EndsWith("lod1")) return true;
            }
            return false;
        }

        private bool DrawEntranceAndActivityHandles(BuildingFootprintAuthority authority)
        {
            bool changed = false;
            SerializedProperty entrances = serializedObject.FindProperty("entrancePairs");
            if (entrances != null)
            {
                for (int i = 0; i < entrances.arraySize; i++)
                {
                    SerializedProperty pair = entrances.GetArrayElementAtIndex(i);
                    changed |= DrawLocalPointHandle(authority, pair.FindPropertyRelative("indoorPositionLocal"),
                        new Color(0.2f, 0.8f, 1f, 1f), $"{pair.FindPropertyRelative("id").stringValue} indoor");
                    changed |= DrawLocalPointHandle(authority, pair.FindPropertyRelative("outdoorApproachLocal"),
                        new Color(1f, 0.72f, 0.15f, 1f), $"{pair.FindPropertyRelative("id").stringValue} outdoor");
                }
            }
            SerializedProperty activities = serializedObject.FindProperty("activityLocations");
            if (activities != null)
            {
                for (int i = 0; i < activities.arraySize; i++)
                {
                    SerializedProperty activity = activities.GetArrayElementAtIndex(i);
                    changed |= DrawLocalPointHandle(authority, activity.FindPropertyRelative("positionLocal"),
                        new Color(0.75f, 0.85f, 0.2f, 1f), activity.FindPropertyRelative("displayName").stringValue);
                }
            }
            return changed;
        }

        private static bool DrawLocalPointHandle(BuildingFootprintAuthority authority, SerializedProperty property, Color color, string label)
        {
            Vector3 world = authority.transform.TransformPoint(property.vector3Value);
            float size = Mathf.Max(0.06f, HandleUtility.GetHandleSize(world) * 0.055f);
            using (new Handles.DrawingScope(color))
            {
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.PositionHandle(world, authority.transform.rotation);
                Handles.Label(world + authority.transform.up * size, label);
                if (!EditorGUI.EndChangeCheck()) return false;
                Undo.RecordObject(authority, $"Move {label}");
                property.vector3Value = authority.transform.InverseTransformPoint(moved);
                return true;
            }
        }

        private void DrawInteriorFloorAuthoring()
        {
            SerializedProperty floors = serializedObject.FindProperty("interiorFloors");
            if (floors == null) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Interior Navigation Authoring", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Polygons use building-local XZ coordinates. They describe traversable floor area only; they do not grant ownership, occupancy, or task permission.",
                MessageType.Info);

            for (int floorIndex = 0; floorIndex < floors.arraySize; floorIndex++)
            {
                SerializedProperty floor = floors.GetArrayElementAtIndex(floorIndex);
                floor.isExpanded = EditorGUILayout.Foldout(floor.isExpanded, FloorLabel(floor), true);
                if (!floor.isExpanded) continue;

                using (new EditorGUILayout.VerticalScope("box"))
                {
                    EditorGUILayout.PropertyField(floor.FindPropertyRelative("displayName"));
                    EditorGUILayout.PropertyField(floor.FindPropertyRelative("floorIndex"));
                    EditorGUILayout.PropertyField(floor.FindPropertyRelative("floorHeightLocal"));
                    DrawPolygonList(floor.FindPropertyRelative("walkableAreas"), floor.FindPropertyRelative("floorHeightLocal").floatValue, false);
                    DrawPolygonList(floor.FindPropertyRelative("exclusionAreas"), floor.FindPropertyRelative("floorHeightLocal").floatValue, true);
                    if (GUILayout.Button("Remove Floor"))
                    {
                        floors.DeleteArrayElementAtIndex(floorIndex);
                        break;
                    }
                }
            }

            if (GUILayout.Button("Add Logical Floor"))
            {
                int index = floors.arraySize;
                floors.InsertArrayElementAtIndex(index);
                SerializedProperty floor = floors.GetArrayElementAtIndex(index);
                int nextFloor = 0;
                for (int i = 0; i < index; i++)
                    nextFloor = Mathf.Max(nextFloor, floors.GetArrayElementAtIndex(i).FindPropertyRelative("floorIndex").intValue + 1);
                floor.FindPropertyRelative("displayName").stringValue = nextFloor == 0 ? "Ground Floor" : $"Floor {nextFloor}";
                floor.FindPropertyRelative("floorIndex").intValue = nextFloor;
                floor.FindPropertyRelative("floorHeightLocal").floatValue = nextFloor * 2.5f;
                floor.FindPropertyRelative("walkableAreas").ClearArray();
                floor.FindPropertyRelative("exclusionAreas").ClearArray();
            }

            showInteriorAreas = EditorGUILayout.ToggleLeft("Show interior polygons and handles in Scene view", showInteriorAreas);
        }

        private void DrawPolygonList(SerializedProperty polygons, float floorHeight, bool exclusion)
        {
            if (polygons == null) return;
            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField(exclusion ? "Furniture Exclusions" : "Walkable Areas", EditorStyles.boldLabel);

            for (int polygonIndex = 0; polygonIndex < polygons.arraySize; polygonIndex++)
            {
                SerializedProperty polygon = polygons.GetArrayElementAtIndex(polygonIndex);
                SerializedProperty vertices = polygon.FindPropertyRelative("vertices");
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    EditorGUILayout.PropertyField(polygon.FindPropertyRelative("label"), new GUIContent(exclusion ? "Furniture / Blocked Region" : "Area Name"));
                    EditorGUILayout.PropertyField(polygon.FindPropertyRelative("functionalSpaceId"), new GUIContent("Functional Space ID"));
                    vertices.isExpanded = EditorGUILayout.Foldout(vertices.isExpanded, $"Vertices ({vertices.arraySize})", true);
                    if (vertices.isExpanded)
                    {
                        for (int i = 0; i < vertices.arraySize; i++)
                        {
                            SerializedProperty vertex = vertices.GetArrayElementAtIndex(i);
                            Vector3 point = vertex.vector3Value;
                            Vector2 xz = EditorGUILayout.Vector2Field($"Point {i + 1} (X/Z)", new Vector2(point.x, point.z));
                            vertex.vector3Value = new Vector3(xz.x, floorHeight, xz.y);
                        }
                    }

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Add Vertex")) InsertMidpointVertex(vertices, floorHeight);
                    using (new EditorGUI.DisabledScope(vertices.arraySize <= 3))
                    {
                        if (GUILayout.Button("Remove Last Vertex") && vertices.arraySize > 3)
                            vertices.DeleteArrayElementAtIndex(vertices.arraySize - 1);
                    }
                    if (GUILayout.Button("Clear Area")) vertices.ClearArray();
                    EditorGUILayout.EndHorizontal();

                    if (!BuildingFootprintAuthority.TryValidateInteriorPolygon(ReadVertices(vertices), out string error))
                        EditorGUILayout.HelpBox("Invalid polygon: " + error, MessageType.Error);
                    else
                        EditorGUILayout.HelpBox("Valid simple polygon · CCW · Scene-view query enabled", MessageType.Info);

                    if (GUILayout.Button("Remove Polygon"))
                    {
                        polygons.DeleteArrayElementAtIndex(polygonIndex);
                        break;
                    }
                }
            }

            if (GUILayout.Button(exclusion ? "Add 4-Point Furniture Exclusion" : "Add 4-Point Walkable Area"))
            {
                int index = polygons.arraySize;
                polygons.InsertArrayElementAtIndex(index);
                SerializedProperty polygon = polygons.GetArrayElementAtIndex(index);
                SetPolygonDefaults(polygon, exclusion ? "Furniture Exclusion" : "Walkable Area", floorHeight, string.Empty);
            }
        }

        private static string FloorLabel(SerializedProperty floor)
        {
            string name = floor.FindPropertyRelative("displayName").stringValue;
            int index = floor.FindPropertyRelative("floorIndex").intValue;
            return $"{(string.IsNullOrWhiteSpace(name) ? "Floor" : name)} (index {index})";
        }

        private static void SetPolygonDefaults(SerializedProperty polygon, string label, float y, string spaceId)
        {
            polygon.FindPropertyRelative("label").stringValue = label;
            polygon.FindPropertyRelative("functionalSpaceId").stringValue = spaceId;
            SerializedProperty vertices = polygon.FindPropertyRelative("vertices");
            vertices.ClearArray();
            Vector3[] defaults =
            {
                new(-2f, y, -2f), new(2f, y, -2f), new(2f, y, 2f), new(-2f, y, 2f)
            };
            for (int i = 0; i < defaults.Length; i++)
            {
                vertices.InsertArrayElementAtIndex(i);
                vertices.GetArrayElementAtIndex(i).vector3Value = defaults[i];
            }
            vertices.isExpanded = true;
        }

        private static List<Vector3> ReadVertices(SerializedProperty vertices)
        {
            List<Vector3> result = new(vertices.arraySize);
            for (int i = 0; i < vertices.arraySize; i++) result.Add(vertices.GetArrayElementAtIndex(i).vector3Value);
            return result;
        }

        private static void InsertMidpointVertex(SerializedProperty vertices, float floorHeight)
        {
            if (vertices == null || vertices.arraySize < 2) return;
            int edge = 0;
            float longest = -1f;
            Vector3 midpoint = default;
            for (int i = 0; i < vertices.arraySize; i++)
            {
                Vector3 a = vertices.GetArrayElementAtIndex(i).vector3Value;
                Vector3 b = vertices.GetArrayElementAtIndex((i + 1) % vertices.arraySize).vector3Value;
                float length = (new Vector2(a.x - b.x, a.z - b.z)).sqrMagnitude;
                if (length <= longest) continue;
                longest = length;
                edge = i;
                midpoint = new Vector3((a.x + b.x) * 0.5f, floorHeight, (a.z + b.z) * 0.5f);
            }

            int insertIndex = edge + 1;
            vertices.InsertArrayElementAtIndex(insertIndex);
            vertices.GetArrayElementAtIndex(insertIndex).vector3Value = midpoint;
        }

        private static bool DrawFreeMoveHandle(SerializedProperty property, BuildingFootprintAuthority authority, Color color)
        {
            return DrawMoveHandle(property, authority, color);
        }

        private static bool DrawMoveHandle(SerializedProperty property, BuildingFootprintAuthority authority, Color color)
        {
            if (property == null || authority == null)
            {
                return false;
            }

            Transform transform = authority.transform;
            Vector3 worldPosition = transform.TransformPoint(property.vector3Value);
            float handleSize = Mathf.Max(0.06f, HandleUtility.GetHandleSize(worldPosition) * 0.08f);

            using (new Handles.DrawingScope(color))
            {
                EditorGUI.BeginChangeCheck();
                Vector3 updatedWorldPosition = Handles.Slider2D(
                    worldPosition,
                    transform.up,
                    transform.right,
                    transform.forward,
                    handleSize,
                    Handles.SphereHandleCap,
                    Vector2.zero);

                if (!EditorGUI.EndChangeCheck())
                {
                    return false;
                }

                Undo.RecordObject(authority, "Move Building Footprint Authoring Handle");
                property.vector3Value = transform.InverseTransformPoint(updatedWorldPosition);
                return true;
            }
        }

        private static void DrawLockedHandle(SerializedProperty property, BuildingFootprintAuthority authority, Color color)
        {
            if (property == null || authority == null)
            {
                return;
            }

            Transform transform = authority.transform;
            Vector3 worldPosition = transform.TransformPoint(property.vector3Value);
            float handleSize = Mathf.Max(0.06f, HandleUtility.GetHandleSize(worldPosition) * 0.08f);

            using (new Handles.DrawingScope(color))
            {
                Handles.SphereHandleCap(0, worldPosition, Quaternion.identity, handleSize, EventType.Repaint);
            }
        }

        private static void SyncLockedCornersToEditableDiagonal(
            SerializedProperty frontLeftCorner,
            SerializedProperty frontRightCorner,
            SerializedProperty backRightCorner,
            SerializedProperty backLeftCorner)
        {
            if (frontLeftCorner == null || frontRightCorner == null || backRightCorner == null || backLeftCorner == null)
            {
                return;
            }

            Vector3 frontLeft = frontLeftCorner.vector3Value;
            Vector3 backRight = backRightCorner.vector3Value;
            frontRightCorner.vector3Value = new Vector3(backRight.x, frontLeft.y, frontLeft.z);
            backLeftCorner.vector3Value = new Vector3(frontLeft.x, backRight.y, backRight.z);
        }
    }
#endif
}
