using System;
using System.Collections.Generic;
using LandLedgers.FirstLedger;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Pathing
{
    [DisallowMultipleComponent]
    public sealed class PathingManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private TownWorldController worldController;

        [SerializeField]
        private PathingSettings settings;

        [Header("World Readiness")]
        [SerializeField]
        private bool generateWorldIfMissing = true;

        [Header("Runtime Summary")]
        [SerializeField]
        private int lastPathCellCount;

        [SerializeField]
        private int lastPathCost;

        [SerializeField]
        private string lastFailureReason;

        private readonly List<GridCoord> lastPath = new();
        private readonly GridCoord[] cardinalNeighborOffsets =
        {
            new(0, 1),
            new(1, 0),
            new(0, -1),
            new(-1, 0)
        };

        private readonly GridCoord[] diagonalNeighborOffsets =
        {
            new(0, 1),
            new(1, 0),
            new(0, -1),
            new(-1, 0),
            new(1, 1),
            new(1, -1),
            new(-1, -1),
            new(-1, 1)
        };

        public TownWorldController WorldController => worldController;
        public PathingSettings Settings => settings;
        public TownGrid Grid => worldController != null ? worldController.Grid : null;
        public IReadOnlyList<GridCoord> LastPath => lastPath;

        public void Configure(TownWorldController newWorldController, PathingSettings newSettings)
        {
            worldController = newWorldController;
            settings = newSettings;
        }

        public bool TryFindPath(GridCoord start, GridCoord target, out PathingResult result, UnityEngine.Object requester = null, string label = null, PathingProfile profile = PathingProfile.Pedestrian)
        {
            return TryFindPath(new PathingRequest(start, target, requester, label, profile), out result);
        }

        public bool TryFindPath(BuildingAnchor start, BuildingAnchor target, out PathingResult result, UnityEngine.Object requester = null, string label = null, PathingProfile profile = PathingProfile.Pedestrian)
        {
            return TryFindPath(start.coord, target.coord, out result, requester, label, profile);
        }

        public bool TryGetMovementCost(GridCoord coord, out int cost)
        {
            EnsureReferences();
            return TryGetTraversalCost(coord, out cost);
        }

        public bool TryFindPath(PathingRequest request, out PathingResult result)
        {
            EnsureReferences();
            settings?.Sanitize();
            result = new PathingResult(request);
            if (request.PublishDiagnostics)
            {
                lastPath.Clear();
                lastPathCellCount = 0;
                lastPathCost = 0;
                lastFailureReason = string.Empty;
            }

            if (!EnsureWorldReady())
            {
                return Fail(result, "Town world is not generated.", PathingFailureCategory.WorldUnavailable);
            }

            TownGrid grid = worldController.Grid;
            if (!grid.IsInBounds(request.Start))
            {
                return Fail(result, $"Start cell {request.Start} is outside the grid.", PathingFailureCategory.StartOutsideGrid);
            }

            if (!grid.IsInBounds(request.Target))
            {
                return Fail(result, $"Target cell {request.Target} is outside the grid.", PathingFailureCategory.TargetOutsideGrid);
            }

            if (!TryGetTraversalCost(request.Start, request, out _))
            {
                return Fail(result, $"Start cell {request.Start} is not walkable.", PathingFailureCategory.StartNotWalkable);
            }

            if (!TryGetTraversalCost(request.Target, request, out _))
            {
                return Fail(result, $"Target cell {request.Target} is not walkable.", PathingFailureCategory.TargetNotWalkable);
            }

            if (request.Start.Equals(request.Target))
            {
                result.Success = true;
                result.Path.Add(request.Start);
                if (request.PublishDiagnostics)
                {
                    CacheLastPath(result);
                }

                return true;
            }

            bool found = RunAStar(request, result);
            if (!found)
            {
                string reason = string.IsNullOrWhiteSpace(result.FailureReason)
                    ? $"No path found from {request.Start} to {request.Target}."
                    : result.FailureReason;
                return Fail(result, reason, result.FailureCategory == PathingFailureCategory.None ? PathingFailureCategory.NoRoute : result.FailureCategory);
            }

            result.Success = true;
            if (request.PublishDiagnostics)
            {
                CacheLastPath(result);
            }

            return true;
        }

        public bool TryFindDoorAnchorByDefinitionId(string buildingDefinitionId, out PlacedBuilding building, out BuildingAnchor anchor)
        {
            string requestedId = buildingDefinitionId ?? string.Empty;
            return TryFindAnchor(
                placedBuilding => placedBuilding.definition != null
                    && string.Equals(placedBuilding.definition.BuildingId, requestedId, StringComparison.OrdinalIgnoreCase),
                AnchorType.FrontDoor,
                out building,
                out anchor);
        }

        public bool TryFindDoorAnchorByPlotZone(PlotZone zone, out PlacedBuilding building, out BuildingAnchor anchor)
        {
            return TryFindAnchor(
                placedBuilding => TryGetPlot(placedBuilding.plotId, out TownPlot plot) && plot.zone == zone,
                AnchorType.FrontDoor,
                out building,
                out anchor);
        }

        public bool TryFindDoorAnchor(Predicate<PlacedBuilding> buildingFilter, out PlacedBuilding building, out BuildingAnchor anchor)
        {
            return TryFindAnchor(buildingFilter, AnchorType.FrontDoor, out building, out anchor);
        }

        public bool TryFindAnchorByBuildingId(int buildingId, AnchorType anchorType, out PlacedBuilding building, out BuildingAnchor anchor)
        {
            return TryFindAnchor(placedBuilding => placedBuilding.id == buildingId, anchorType, out building, out anchor);
        }

        public bool TryFindAnchor(Predicate<PlacedBuilding> buildingFilter, AnchorType anchorType, out PlacedBuilding building, out BuildingAnchor anchor)
        {
            building = null;
            anchor = default;

            if (!EnsureWorldReady())
            {
                return false;
            }

            foreach (PlacedBuilding candidate in worldController.Buildings)
            {
                if (candidate == null || buildingFilter != null && !buildingFilter(candidate))
                {
                    continue;
                }

                foreach (BuildingAnchor candidateAnchor in candidate.anchors)
                {
                    if (candidateAnchor.type != anchorType || !TryGetTraversalCost(candidateAnchor.coord, out _))
                    {
                        continue;
                    }

                    building = candidate;
                    anchor = candidateAnchor;
                    return true;
                }
            }

            return false;
        }
        public bool TryFindDoorAnchorByDefinitionIdReachableFrom(
            GridCoord origin,
            string buildingDefinitionId,
            out PlacedBuilding building,
            out BuildingAnchor anchor,
            out PathingResult pathResult,
            PathingProfile profile = PathingProfile.Pedestrian)
        {
            string requestedId = buildingDefinitionId ?? string.Empty;
            return TryFindAnchorReachableFrom(
                origin,
                placedBuilding => placedBuilding.definition != null
                    && string.Equals(placedBuilding.definition.BuildingId, requestedId, StringComparison.OrdinalIgnoreCase),
                AnchorType.FrontDoor,
                out building,
                out anchor,
                out pathResult,
                profile);
        }

        public bool TryFindDoorAnchorByPlotZoneReachableFrom(
            GridCoord origin,
            PlotZone zone,
            out PlacedBuilding building,
            out BuildingAnchor anchor,
            out PathingResult pathResult,
            PathingProfile profile = PathingProfile.Pedestrian)
        {
            return TryFindAnchorReachableFrom(
                origin,
                placedBuilding => TryGetPlot(placedBuilding.plotId, out TownPlot plot) && plot.zone == zone,
                AnchorType.FrontDoor,
                out building,
                out anchor,
                out pathResult,
                profile);
        }

        public bool TryFindDoorAnchorReachableFrom(
            GridCoord origin,
            Predicate<PlacedBuilding> buildingFilter,
            out PlacedBuilding building,
            out BuildingAnchor anchor,
            out PathingResult pathResult,
            PathingProfile profile = PathingProfile.Pedestrian)
        {
            return TryFindAnchorReachableFrom(origin, buildingFilter, AnchorType.FrontDoor, out building, out anchor, out pathResult, profile);
        }

        public bool TryFindAnchorReachableFrom(
            GridCoord origin,
            Predicate<PlacedBuilding> buildingFilter,
            AnchorType anchorType,
            out PlacedBuilding building,
            out BuildingAnchor anchor,
            out PathingResult pathResult,
            PathingProfile profile = PathingProfile.Pedestrian)
        {
            building = null;
            anchor = default;
            pathResult = null;

            if (!EnsureWorldReady())
            {
                return false;
            }

            int bestCost = int.MaxValue;
            int bestDistance = int.MaxValue;
            foreach (PlacedBuilding candidate in worldController.Buildings)
            {
                if (candidate == null || buildingFilter != null && !buildingFilter(candidate))
                {
                    continue;
                }

                foreach (BuildingAnchor candidateAnchor in candidate.anchors)
                {
                    if (candidateAnchor.type != anchorType || !TryGetTraversalCost(candidateAnchor.coord, profile, out _))
                    {
                        continue;
                    }

                    PathingRequest probe = PathingRequest.CreateProbe(origin, candidateAnchor.coord, this, $"anchor probe {candidate.id}", profile);
                    if (!TryFindPath(probe, out PathingResult candidatePath) || candidatePath == null || !candidatePath.Success)
                    {
                        continue;
                    }

                    int distance = ManhattanDistance(origin, candidateAnchor.coord);
                    if (candidatePath.TotalCost > bestCost || candidatePath.TotalCost == bestCost && distance >= bestDistance)
                    {
                        continue;
                    }

                    bestCost = candidatePath.TotalCost;
                    bestDistance = distance;
                    building = candidate;
                    anchor = candidateAnchor;
                    pathResult = candidatePath;
                }
            }

            return building != null;
        }

        private static int ManhattanDistance(GridCoord a, GridCoord b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
        }


        public Vector3 CoordToPathWorld(GridCoord coord)
        {
            EnsureReferences();
            TownGrid grid = Grid;
            if (grid == null || !grid.IsInBounds(coord))
            {
                return transform.position;
            }

            float height = worldController != null && worldController.Settings != null
                ? worldController.Settings.worldCenter.y
                : 0f;

            TownCell cell = grid.GetCell(coord);
            if (cell.terrainZone != TerrainZone.Unknown)
            {
                height = cell.height;
            }

            float yOffset = settings != null ? settings.agentGroundOffsetMeters : 0.08f;
            return grid.CoordToWorldCenter(coord, height + yOffset);
        }

        private bool RunAStar(PathingRequest request, PathingResult result)
        {
            TownGrid grid = worldController.Grid;
            int cellCount = grid.CellCount;
            int[] gScore = new int[cellCount];
            int[] parent = new int[cellCount];
            byte[] closed = new byte[cellCount];
            Array.Fill(gScore, int.MaxValue);
            Array.Fill(parent, -1);

            int startIndex = grid.IndexOf(request.Start);
            int targetIndex = grid.IndexOf(request.Target);
            gScore[startIndex] = 0;

            MinHeap open = new(Mathf.Min(cellCount, 512));
            open.Push(startIndex, HeuristicCost(request.Start, request.Target, request.Profile));

            int visited = 0;
            while (open.Count > 0)
            {
                int currentIndex = open.Pop();
                if (closed[currentIndex] != 0)
                {
                    continue;
                }

                closed[currentIndex] = 1;
                visited++;
                if (currentIndex == targetIndex)
                {
                    result.TotalCost = gScore[currentIndex];
                    ReconstructPath(grid, parent, currentIndex, result.Path);
                    return true;
                }

                if (visited >= settings.maxVisitedCells)
                {
                    result.FailureReason = $"Search exceeded max visited cells ({settings.maxVisitedCells}).";
                    result.FailureCategory = PathingFailureCategory.SearchLimitExceeded;
                    return false;
                }

                GridCoord current = CoordFromIndex(grid, currentIndex);
                GridCoord[] offsets = settings.allowDiagonalMovement ? diagonalNeighborOffsets : cardinalNeighborOffsets;
                for (int i = 0; i < offsets.Length; i++)
                {
                    GridCoord offset = offsets[i];
                    GridCoord neighbor = current + offset;
                    if (!grid.IsInBounds(neighbor)
                        || !CanTraverseHeightChange(current, neighbor)
                        || !TryGetTraversalCost(neighbor, request, out int stepCost))
                    {
                        continue;
                    }

                    bool isDiagonal = IsDiagonalOffset(offset);
                    if (isDiagonal && !CanTraverseDiagonal(current, offset, request))
                    {
                        continue;
                    }

                    int neighborIndex = grid.IndexOf(neighbor);
                    if (closed[neighborIndex] != 0)
                    {
                        continue;
                    }

                    if (isDiagonal)
                    {
                        stepCost = Mathf.RoundToInt(stepCost * 1.4142f);
                    }

                    int tentativeCost = gScore[currentIndex] + stepCost;
                    if (tentativeCost >= gScore[neighborIndex])
                    {
                        continue;
                    }

                    parent[neighborIndex] = currentIndex;
                    gScore[neighborIndex] = tentativeCost;
                    int priority = tentativeCost + HeuristicCost(neighbor, request.Target, request.Profile);
                    open.Push(neighborIndex, priority);
                }
            }

            return false;
        }

        private bool CanTraverseHeightChange(GridCoord from, GridCoord to)
        {
            TownGrid grid = Grid;
            if (grid == null || settings == null || !grid.IsInBounds(from) || !grid.IsInBounds(to))
            {
                return false;
            }

            TownCell fromCell = grid.GetCell(from);
            TownCell toCell = grid.GetCell(to);
            return Mathf.Abs(fromCell.height - toCell.height) <= settings.maximumTraversableStepHeightMeters;
        }

        private bool TryGetTraversalCost(GridCoord coord, out int cost)
        {
            return TryGetTraversalCost(coord, PathingProfile.Pedestrian, out cost);
        }

        private bool TryGetTraversalCost(GridCoord coord, PathingProfile profile, out int cost)
        {
            if (!TryGetBaseTraversalCost(coord, out int baseCost, out PathingSurfaceKind surfaceKind))
            {
                cost = int.MaxValue;
                return false;
            }

            cost = ApplyProfileCost(baseCost, surfaceKind, profile);
            return true;
        }

        private bool TryGetTraversalCost(GridCoord coord, PathingRequest request, out int cost)
        {
            if (TryGetTraversalCost(coord, request.Profile, out cost))
            {
                return true;
            }

            cost = int.MaxValue;
            TownGrid grid = Grid;
            if (settings == null || grid == null || !grid.IsInBounds(coord))
            {
                return false;
            }

            TownCell cell = grid.GetCell(coord);
            if (!IsRequestBuildingInteriorTraversalCell(grid, cell, request))
            {
                return false;
            }

            cost = ApplyProfileCost(settings.yardCost, PathingSurfaceKind.Yard, request.Profile);
            return true;
        }

        private static bool IsDiagonalOffset(GridCoord offset)
        {
            return offset.x != 0 && offset.z != 0;
        }

        private bool CanTraverseDiagonal(GridCoord current, GridCoord offset, PathingRequest request)
        {
            TownGrid grid = Grid;
            if (grid == null)
            {
                return false;
            }

            GridCoord horizontal = new GridCoord(current.x + offset.x, current.z);
            GridCoord vertical = new GridCoord(current.x, current.z + offset.z);
            return grid.IsInBounds(horizontal)
                && grid.IsInBounds(vertical)
                && TryGetTraversalCost(horizontal, request, out _)
                && TryGetTraversalCost(vertical, request, out _);
        }

        private bool TryGetBaseTraversalCost(GridCoord coord, out int cost, out PathingSurfaceKind surfaceKind)
        {
            cost = int.MaxValue;
            surfaceKind = PathingSurfaceKind.GeneralWalkable;
            TownGrid grid = Grid;
            if (settings == null || grid == null || !grid.IsInBounds(coord))
            {
                return false;
            }

            TownCell cell = grid.GetCell(coord);
            if (cell.terrainZone == TerrainZone.Unknown
                || cell.terrainZone == TerrainZone.Blocked
                || float.IsNaN(cell.height)
                || float.IsInfinity(cell.height))
            {
                return false;
            }

            if (cell.IsRoad)
            {
                cost = settings.roadCost;
                surfaceKind = PathingSurfaceKind.Road;
                return true;
            }

            if ((cell.occupancy & CellOccupancy.Anchor) != 0)
            {
                cost = settings.anchorCost;
                surfaceKind = PathingSurfaceKind.Anchor;
                return true;
            }

            if (cell.HasBuilding)
            {
                return false;
            }

            if ((cell.occupancy & CellOccupancy.Plot) != 0)
            {
                cost = settings.yardCost;
                surfaceKind = PathingSurfaceKind.Yard;
                return true;
            }

            if (settings.allowRoughTerrain && cell.terrainZone == TerrainZone.Steep)
            {
                cost = settings.roughWalkableCost;
                surfaceKind = PathingSurfaceKind.RoughWalkable;
                return true;
            }

            if (cell.terrainZone == TerrainZone.Buildable)
            {
                cost = settings.generalWalkableCost;
                surfaceKind = PathingSurfaceKind.GeneralWalkable;
                return true;
            }

            return false;
        }

        private int ApplyProfileCost(int baseCost, PathingSurfaceKind surfaceKind, PathingProfile profile)
        {
            if (settings == null || profile != PathingProfile.LogisticsWagon)
            {
                return Mathf.Max(1, baseCost);
            }

            float multiplier = surfaceKind switch
            {
                PathingSurfaceKind.Road => settings.logisticsWagonRoadCostMultiplier,
                PathingSurfaceKind.Anchor => settings.logisticsWagonAnchorCostMultiplier,
                PathingSurfaceKind.Yard => settings.logisticsWagonYardCostMultiplier,
                PathingSurfaceKind.RoughWalkable => settings.logisticsWagonRoughWalkableCostMultiplier,
                _ => settings.logisticsWagonGeneralWalkableCostMultiplier
            };

            // Logistics paths use the same grid authority as pedestrians, but heavier wagon costs bias route choice toward roads.
            return Mathf.Max(1, Mathf.RoundToInt(baseCost * Mathf.Max(0.1f, multiplier)));
        }

        private static bool IsRequestBuildingInteriorTraversalCell(TownGrid grid, TownCell cell, PathingRequest request)
        {
            if (!cell.HasBuilding || cell.buildingId < 0)
            {
                return false;
            }

            return EndpointAllowsInteriorTraversal(grid, request.Start, cell.buildingId)
                || EndpointAllowsInteriorTraversal(grid, request.Target, cell.buildingId);
        }

        private static bool EndpointAllowsInteriorTraversal(TownGrid grid, GridCoord endpoint, int buildingId)
        {
            if (grid == null || !grid.IsInBounds(endpoint))
            {
                return false;
            }

            TownCell endpointCell = grid.GetCell(endpoint);
            return endpointCell.HasBuilding
                && endpointCell.buildingId == buildingId
                && (endpointCell.occupancy & CellOccupancy.Anchor) != 0;
        }

        private bool TryGetPlot(int plotId, out TownPlot plot)
        {
            plot = null;
            if (!EnsureWorldReady())
            {
                return false;
            }

            foreach (TownPlot candidate in worldController.Plots)
            {
                if (candidate.id == plotId)
                {
                    plot = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool EnsureWorldReady()
        {
            EnsureReferences();
            if (worldController == null)
            {
                return false;
            }

            if (worldController.Grid != null)
            {
                return true;
            }

            if (!generateWorldIfMissing)
            {
                return false;
            }

            FirstLedgerSliceBootstrapper bootstrapper = FindAnyObjectByType<FirstLedgerSliceBootstrapper>();
            if (bootstrapper != null)
            {
                bootstrapper.InitializeSlice();
                return worldController.Grid != null;
            }

            if (Application.isPlaying && !worldController.IsStartupConfigured)
            {
                Debug.LogError("[Pathing] Town grid is missing and terrain startup has not been configured. Pathing will not create an uncontrolled fresh world.", this);
                return false;
            }

            worldController.GenerateTownShell();
            return worldController.Grid != null;
        }

        private void EnsureReferences()
        {
            if (worldController == null)
            {
                worldController = FindAnyObjectByType<TownWorldController>();
            }
        }

        private bool Fail(PathingResult result, string fallbackReason, PathingFailureCategory category)
        {
            result.Success = false;
            if (result.FailureCategory == PathingFailureCategory.None)
            {
                result.FailureCategory = category;
            }

            if (string.IsNullOrWhiteSpace(result.FailureReason))
            {
                result.FailureReason = fallbackReason;
            }

            if (result.Request.PublishDiagnostics)
            {
                lastFailureReason = result.FailureCategory == PathingFailureCategory.None
                    ? result.FailureReason
                    : $"{result.FailureCategory}: {result.FailureReason}";
            }

            if (!result.Request.SuppressFailureLog && settings != null && settings.logFailedRequests)
            {
                UnityEngine.Object context = result.Request.Requester != null ? result.Request.Requester : this;
                Debug.LogWarning($"Pathing request failed ({result.FailureCategory}): {result.FailureReason}", context);
            }

            return false;
        }

        private void CacheLastPath(PathingResult result)
        {
            lastPath.Clear();
            lastPath.AddRange(result.Path);
            lastPathCellCount = lastPath.Count;
            lastPathCost = result.TotalCost;
            lastFailureReason = result.Success ? string.Empty : $"{result.FailureCategory}: {result.FailureReason}";
        }

        private int HeuristicCost(GridCoord from, GridCoord target, PathingProfile profile)
        {
            int dx = Mathf.Abs(from.x - target.x);
            int dz = Mathf.Abs(from.z - target.z);
            int roadCost = ResolveMinimumStepCost(profile);

            if (settings != null && settings.allowDiagonalMovement)
            {
                int diagonalSteps = Mathf.Min(dx, dz);
                int straightSteps = Mathf.Max(dx, dz) - diagonalSteps;
                int diagonalCost = Mathf.RoundToInt(roadCost * 1.4142f);
                return diagonalSteps * diagonalCost + straightSteps * roadCost;
            }

            return (dx + dz) * roadCost;
        }

        private int ResolveMinimumStepCost(PathingProfile profile)
        {
            int baseRoadCost = settings != null ? Mathf.Max(1, settings.roadCost) : 1;
            if (settings == null || profile != PathingProfile.LogisticsWagon)
            {
                return baseRoadCost;
            }

            return Mathf.Max(1, Mathf.RoundToInt(baseRoadCost * Mathf.Max(0.1f, settings.logisticsWagonRoadCostMultiplier)));
        }

        private static GridCoord CoordFromIndex(TownGrid grid, int index)
        {
            return new GridCoord(index % grid.Width, index / grid.Width);
        }

        private static void ReconstructPath(TownGrid grid, int[] parent, int endIndex, List<GridCoord> output)
        {
            output.Clear();
            int current = endIndex;
            while (current >= 0)
            {
                output.Add(CoordFromIndex(grid, current));
                current = parent[current];
            }

            output.Reverse();
        }

        private void Reset()
        {
            EnsureReferences();
        }

        private void OnValidate()
        {
            settings?.Sanitize();
        }

        private void OnDrawGizmos()
        {
            if (settings == null || !settings.drawManagerLastPath || lastPath.Count < 2)
            {
                return;
            }

            Gizmos.color = settings.lastPathColor;
            for (int i = 1; i < lastPath.Count; i++)
            {
                Vector3 a = CoordToPathWorld(lastPath[i - 1]) + Vector3.up * settings.debugLineHeight;
                Vector3 b = CoordToPathWorld(lastPath[i]) + Vector3.up * settings.debugLineHeight;
                Gizmos.DrawLine(a, b);
            }
        }

        private enum PathingSurfaceKind
        {
            Road,
            Anchor,
            Yard,
            GeneralWalkable,
            RoughWalkable
        }

        private struct HeapNode
        {
            public int index;
            public int priority;
        }

        private sealed class MinHeap
        {
            private readonly List<HeapNode> nodes;

            public MinHeap(int capacity)
            {
                nodes = new List<HeapNode>(Mathf.Max(16, capacity));
            }

            public int Count => nodes.Count;

            public void Push(int index, int priority)
            {
                nodes.Add(new HeapNode { index = index, priority = priority });
                SiftUp(nodes.Count - 1);
            }

            public int Pop()
            {
                int index = nodes[0].index;
                int last = nodes.Count - 1;
                nodes[0] = nodes[last];
                nodes.RemoveAt(last);
                if (nodes.Count > 0)
                {
                    SiftDown(0);
                }

                return index;
            }

            private void SiftUp(int child)
            {
                while (child > 0)
                {
                    int parent = (child - 1) / 2;
                    if (nodes[parent].priority <= nodes[child].priority)
                    {
                        return;
                    }

                    (nodes[parent], nodes[child]) = (nodes[child], nodes[parent]);
                    child = parent;
                }
            }

            private void SiftDown(int parent)
            {
                while (true)
                {
                    int left = parent * 2 + 1;
                    int right = left + 1;
                    int smallest = parent;

                    if (left < nodes.Count && nodes[left].priority < nodes[smallest].priority)
                    {
                        smallest = left;
                    }

                    if (right < nodes.Count && nodes[right].priority < nodes[smallest].priority)
                    {
                        smallest = right;
                    }

                    if (smallest == parent)
                    {
                        return;
                    }

                    (nodes[parent], nodes[smallest]) = (nodes[smallest], nodes[parent]);
                    parent = smallest;
                }
            }
        }
    }
}
