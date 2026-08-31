using System;
using System.Collections.Generic;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Pathing
{
    public sealed class LogisticsRoutePlanner
    {
        private readonly TownWorldController townWorld;
        private readonly PathingManager pathingManager;

        public LogisticsRoutePlanner(TownWorldController townWorld, PathingManager pathingManager)
        {
            this.townWorld = townWorld;
            this.pathingManager = pathingManager;
        }

        public bool TryPlanRoute(LogisticsRouteRequest request, out LogisticsRoutePlan plan)
        {
            plan = CreatePlanShell(request);
            TownGrid grid = townWorld != null ? townWorld.Grid : null;
            PathingSettings settings = pathingManager != null ? pathingManager.Settings : null;
            settings?.Sanitize();

            if (grid == null || pathingManager == null)
            {
                Fail(plan, LogisticsRouteFailureCategory.MissingDependency, PathingFailureCategory.WorldUnavailable, "Missing grid or pathing manager.");
                return false;
            }

            List<GridCoord> startCandidates = ResolveEndpointCandidates(request.Start, request.StartKind, request.Target, grid, plan, true);
            if (startCandidates.Count == 0)
            {
                Fail(plan, LogisticsRouteFailureCategory.NoEdgeRoadCandidate, PathingFailureCategory.None, "No valid edge road found for shipment entry.");
                return false;
            }

            List<GridCoord> targetCandidates = ResolveEndpointCandidates(request.Target, request.TargetKind, request.Start, grid, plan, false);
            if (targetCandidates.Count == 0)
            {
                Fail(plan, LogisticsRouteFailureCategory.NoEdgeRoadCandidate, PathingFailureCategory.None, "No valid edge road found for shipment exit.");
                return false;
            }

            PathingResult bestResult = null;
            GridCoord bestStart = default;
            GridCoord bestTarget = default;
            int bestScore = int.MaxValue;
            int testedPairs = 0;
            string firstFailure = string.Empty;
            string lastFailure = string.Empty;
            PathingFailureCategory firstFailureCategory = PathingFailureCategory.None;
            PathingFailureCategory lastFailureCategory = PathingFailureCategory.None;

            for (int i = 0; i < startCandidates.Count; i++)
            {
                for (int j = 0; j < targetCandidates.Count; j++)
                {
                    GridCoord candidateStart = startCandidates[i];
                    GridCoord candidateTarget = targetCandidates[j];
                    testedPairs++;

                    PathingRequest probe = PathingRequest.CreateProbe(
                        candidateStart,
                        candidateTarget,
                        pathingManager,
                        $"logistics probe {request.Label}",
                        PathingProfile.LogisticsWagon);

                    if (!pathingManager.TryFindPath(probe, out PathingResult candidateResult))
                    {
                        string reason = candidateResult != null ? candidateResult.FailureReason : string.Empty;
                        PathingFailureCategory category = candidateResult != null ? candidateResult.FailureCategory : PathingFailureCategory.None;
                        if (string.IsNullOrWhiteSpace(firstFailure))
                        {
                            firstFailure = reason;
                            firstFailureCategory = category;
                        }

                        lastFailure = reason;
                        lastFailureCategory = category;
                        continue;
                    }

                    int endpointPenalty = ManhattanDistance(request.Start, candidateStart) + ManhattanDistance(request.Target, candidateTarget);
                    int score = candidateResult.TotalCost + endpointPenalty * ResolveEdgeCandidatePenalty(settings);
                    if (score >= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;
                    bestStart = candidateStart;
                    bestTarget = candidateTarget;
                    bestResult = candidateResult;
                }
            }

            plan.edgeRouteCandidatePairsTested = testedPairs;
            if (bestResult == null)
            {
                string reason = string.IsNullOrWhiteSpace(firstFailure) && string.IsNullOrWhiteSpace(lastFailure)
                    ? $"No candidate logistics route found after testing {testedPairs} candidate pair(s)."
                    : $"No candidate logistics route found after testing {testedPairs} candidate pair(s). First: {firstFailure}. Last: {lastFailure}.";
                Fail(plan, LogisticsRouteFailureCategory.NoCandidateRoute, lastFailureCategory != PathingFailureCategory.None ? lastFailureCategory : firstFailureCategory, reason);
                return false;
            }

            if (!pathingManager.TryFindPath(bestStart, bestTarget, out PathingResult confirmedResult, pathingManager, request.Label, PathingProfile.LogisticsWagon))
            {
                Fail(
                    plan,
                    LogisticsRouteFailureCategory.FinalConfirmationFailed,
                    confirmedResult != null ? confirmedResult.FailureCategory : PathingFailureCategory.None,
                    string.IsNullOrWhiteSpace(confirmedResult != null ? confirmedResult.FailureReason : string.Empty)
                        ? "Selected logistics route failed final confirmation."
                        : confirmedResult.FailureReason);
                return false;
            }

            plan.visibleStart = bestStart;
            plan.visibleEnd = bestTarget;
            plan.visiblePath = new List<GridCoord>(confirmedResult.Path);
            plan.visibleTravelCells = Mathf.Max(0, confirmedResult.Path.Count - 1);
            plan.offMapTravelCells = CalculateOffMapCells(request, bestStart, bestTarget, grid);
            plan.totalTravelCells = plan.visibleTravelCells + plan.offMapTravelCells;
            AnalyzeSurfaceMix(plan, grid);
            CalculateTravelTimes(plan, grid, settings);
            ClassifyRouteQuality(plan, settings);
            plan.edgeRouteSelectionReason = $"Selected {bestStart} -> {bestTarget} from {testedPairs} candidate pair(s).";
            return true;
        }

        public bool TryFindNearestEdgeRoad(GridCoord pivot, out GridCoord roadCoord)
        {
            roadCoord = default;
            TownGrid grid = townWorld != null ? townWorld.Grid : null;
            if (grid == null)
            {
                return false;
            }

            List<GridCoord> candidates = CollectEdgeRoadCandidates(pivot, grid, 1);
            if (candidates.Count <= 0)
            {
                return false;
            }

            roadCoord = candidates[0];
            return true;
        }

        private LogisticsRoutePlan CreatePlanShell(LogisticsRouteRequest request)
        {
            return new LogisticsRoutePlan
            {
                label = request.Label ?? string.Empty,
                requestedStart = request.Start,
                requestedTarget = request.Target,
                requestedStartKind = request.StartKind,
                requestedTargetKind = request.TargetKind,
                visibleStart = request.Start,
                visibleEnd = request.Target,
                pathingProfile = PathingProfile.LogisticsWagon,
                quality = LogisticsRouteQuality.Unknown,
                failureCategory = LogisticsRouteFailureCategory.None,
                pathingFailureCategory = PathingFailureCategory.None
            };
        }

        private List<GridCoord> ResolveEndpointCandidates(
            GridCoord requestedCoord,
            LogisticsPathEndpointKind endpointKind,
            GridCoord oppositePivot,
            TownGrid grid,
            LogisticsRoutePlan plan,
            bool isStart)
        {
            if (endpointKind == LogisticsPathEndpointKind.GridCoord)
            {
                return new List<GridCoord> { requestedCoord };
            }

            int limit = Mathf.Max(1, pathingManager != null && pathingManager.Settings != null
                ? pathingManager.Settings.logisticsEdgeRoadCandidateLimit
                : 8);
            List<GridCoord> candidates = CollectEdgeRoadCandidates(oppositePivot, grid, limit);
            if (isStart)
            {
                plan.startEdgeRoadCandidateCount = candidates.Count;
            }
            else
            {
                plan.targetEdgeRoadCandidateCount = candidates.Count;
            }

            return candidates;
        }

        private static List<GridCoord> CollectEdgeRoadCandidates(GridCoord pivot, TownGrid grid, int limit)
        {
            List<GridCoord> candidates = new();
            if (grid == null)
            {
                return candidates;
            }

            for (int z = 0; z < grid.Depth; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    bool onEdge = x == 0 || z == 0 || x == grid.Width - 1 || z == grid.Depth - 1;
                    if (!onEdge)
                    {
                        continue;
                    }

                    GridCoord coord = new(x, z);
                    TownCell cell = grid.GetCell(coord);
                    if (cell.IsRoad)
                    {
                        candidates.Add(coord);
                    }
                }
            }

            candidates.Sort((a, b) =>
            {
                int distanceComparison = ManhattanDistance(a, pivot).CompareTo(ManhattanDistance(b, pivot));
                if (distanceComparison != 0)
                {
                    return distanceComparison;
                }

                int xComparison = a.x.CompareTo(b.x);
                return xComparison != 0 ? xComparison : a.z.CompareTo(b.z);
            });

            if (candidates.Count > limit)
            {
                candidates.RemoveRange(limit, candidates.Count - limit);
            }

            return candidates;
        }

        private int CalculateOffMapCells(LogisticsRouteRequest request, GridCoord visibleStart, GridCoord visibleEnd, TownGrid grid)
        {
            int cells = Mathf.Max(0, request.ExtraOffMapCells);
            if (request.StartKind == LogisticsPathEndpointKind.MapEdgeRoad)
            {
                cells += EstimateOutsideGridCells(request.Start, visibleStart, grid);
            }

            if (request.TargetKind == LogisticsPathEndpointKind.MapEdgeRoad)
            {
                cells += EstimateOutsideGridCells(request.Target, visibleEnd, grid);
            }

            return Mathf.Max(0, cells);
        }

        private static int EstimateOutsideGridCells(GridCoord requestedCoord, GridCoord visibleEdgeCoord, TownGrid grid)
        {
            // In-grid map-edge requests use the requested coordinate as a routing pivot; the active-grid leg is already in visiblePath.
            if (grid != null && grid.IsInBounds(requestedCoord))
            {
                return 0;
            }

            return ManhattanDistance(requestedCoord, visibleEdgeCoord);
        }

        private void AnalyzeSurfaceMix(LogisticsRoutePlan plan, TownGrid grid)
        {
            if (plan.visiblePath == null || grid == null || plan.visiblePath.Count <= 0)
            {
                plan.averageSurfacePenalty = 1f;
                plan.roadShare = 0f;
                return;
            }

            float penaltySum = 0f;
            for (int i = 0; i < plan.visiblePath.Count; i++)
            {
                GridCoord coord = plan.visiblePath[i];
                if (!grid.IsInBounds(coord))
                {
                    plan.unknownSurfaceCellCount++;
                    penaltySum += 1.8f;
                    continue;
                }

                TownCell cell = grid.GetCell(coord);
                if (cell.IsRoad)
                {
                    plan.roadCellCount++;
                    penaltySum += 1f;
                }
                else if ((cell.occupancy & CellOccupancy.Anchor) != 0)
                {
                    plan.anchorCellCount++;
                    penaltySum += 1.08f;
                }
                else if ((cell.occupancy & CellOccupancy.Plot) != 0)
                {
                    plan.yardOrPlotCellCount++;
                    penaltySum += 1.25f;
                }
                else if (cell.terrainZone == TerrainZone.Steep)
                {
                    plan.roughCellCount++;
                    penaltySum += 1.7f;
                }
                else
                {
                    plan.generalWalkableCellCount++;
                    penaltySum += 1.45f;
                }
            }

            int count = Mathf.Max(1, plan.visiblePath.Count);
            plan.roadShare = plan.roadCellCount / (float)count;
            plan.averageSurfacePenalty = Mathf.Max(1f, penaltySum / count);
        }

        private static void CalculateTravelTimes(LogisticsRoutePlan plan, TownGrid grid, PathingSettings settings)
        {
            float cellMeters = Mathf.Max(0.1f, grid != null ? grid.CellSizeMeters : 2f);
            float wagonSpeed = Mathf.Max(0.1f, settings != null ? settings.logisticsLoadedWagonSpeedMetersPerSecond : 2.2f);
            float offMapSpeed = Mathf.Max(0.1f, settings != null ? settings.logisticsOffMapRoadSpeedMetersPerSecond : 3.6f);
            plan.visibleTravelGameSeconds = Mathf.Max(0f, (plan.visibleTravelCells * cellMeters / wagonSpeed) * plan.AverageSurfacePenalty);
            plan.offMapTravelGameSeconds = Mathf.Max(0f, plan.offMapTravelCells * cellMeters / offMapSpeed);
            plan.totalTravelGameSeconds = plan.visibleTravelGameSeconds + plan.offMapTravelGameSeconds;
        }

        private static void ClassifyRouteQuality(LogisticsRoutePlan plan, PathingSettings settings)
        {
            float roadLinkedThreshold = settings != null ? Mathf.Max(1f, settings.logisticsRoadLinkedPenaltyThreshold) : 1.08f;
            float mixedThreshold = settings != null ? Mathf.Max(roadLinkedThreshold, settings.logisticsMixedSurfacePenaltyThreshold) : 1.35f;
            plan.quality = plan.AverageSurfacePenalty <= roadLinkedThreshold
                ? LogisticsRouteQuality.RoadLinked
                : plan.AverageSurfacePenalty <= mixedThreshold
                    ? LogisticsRouteQuality.MixedSurface
                    : LogisticsRouteQuality.RoughLink;

            plan.qualityReason = $"road {plan.RoadShare * 100f:0}%, penalty {plan.AverageSurfacePenalty:0.00}, "
                + $"road/anchor/yard/rough/general/unknown {plan.RoadCellCount}/{plan.AnchorCellCount}/{plan.YardCellCount}/{plan.RoughCellCount}/{plan.GeneralWalkableCellCount}/{plan.UnknownSurfaceCellCount}";
        }

        private static void Fail(
            LogisticsRoutePlan plan,
            LogisticsRouteFailureCategory failureCategory,
            PathingFailureCategory pathingFailureCategory,
            string reason)
        {
            plan.failureCategory = failureCategory;
            plan.pathingFailureCategory = pathingFailureCategory;
            plan.failureReason = reason ?? string.Empty;
            plan.quality = LogisticsRouteQuality.Unknown;
        }

        private static int ResolveEdgeCandidatePenalty(PathingSettings settings)
        {
            int roadCost = settings != null ? Mathf.Max(1, settings.roadCost) : 10;
            float multiplier = settings != null ? Mathf.Max(0.1f, settings.logisticsWagonRoadCostMultiplier) : 1f;
            return Mathf.Max(1, Mathf.RoundToInt(roadCost * multiplier));
        }

        private static int ManhattanDistance(GridCoord a, GridCoord b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
        }
    }
}
