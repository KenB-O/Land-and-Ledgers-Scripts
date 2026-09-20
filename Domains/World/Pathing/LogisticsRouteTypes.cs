using System;
using System.Collections.Generic;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Pathing
{
    public enum LogisticsPathEndpointKind
    {
        GridCoord = 0,
        MapEdgeRoad = 1
    }

    public enum LogisticsRouteQuality
    {
        Unknown = 0,
        RoadLinked = 1,
        MixedSurface = 2,
        RoughLink = 3
    }

    public enum LogisticsRouteFailureCategory
    {
        None = 0,
        MissingDependency = 1,
        NoEdgeRoadCandidate = 2,
        NoCandidateRoute = 3,
        FinalConfirmationFailed = 4
    }

    [Serializable]
    public struct LogisticsRouteRequest
    {
        public LogisticsRouteRequest(
            GridCoord start,
            GridCoord target,
            LogisticsPathEndpointKind startKind = LogisticsPathEndpointKind.GridCoord,
            LogisticsPathEndpointKind targetKind = LogisticsPathEndpointKind.GridCoord,
            int extraOffMapCells = 0,
            string label = null)
        {
            Start = start;
            Target = target;
            StartKind = startKind;
            TargetKind = targetKind;
            ExtraOffMapCells = Mathf.Max(0, extraOffMapCells);
            Label = label ?? string.Empty;
        }

        public GridCoord Start { get; }
        public GridCoord Target { get; }
        public LogisticsPathEndpointKind StartKind { get; }
        public LogisticsPathEndpointKind TargetKind { get; }
        public int ExtraOffMapCells { get; }
        public string Label { get; }
    }

    [Serializable]
    public sealed class LogisticsRoutePlan
    {
        public string label = string.Empty;
        public GridCoord requestedStart;
        public GridCoord requestedTarget;
        public LogisticsPathEndpointKind requestedStartKind;
        public LogisticsPathEndpointKind requestedTargetKind;
        public GridCoord visibleStart;
        public GridCoord visibleEnd;
        public PathingProfile pathingProfile = PathingProfile.LogisticsWagon;
        public int totalTravelCells;
        public int visibleTravelCells;
        public int offMapTravelCells;
        public float totalTravelGameSeconds;
        public float visibleTravelGameSeconds;
        public float offMapTravelGameSeconds;
        public float averageSurfacePenalty = 1f;
        public float roadShare;
        public int roadCellCount;
        public int anchorCellCount;
        public int yardOrPlotCellCount;
        public int roughCellCount;
        public int generalWalkableCellCount;
        public int unknownSurfaceCellCount;
        public int startEdgeRoadCandidateCount;
        public int targetEdgeRoadCandidateCount;
        public int edgeRouteCandidatePairsTested;
        public LogisticsRouteQuality quality;
        public string qualityReason = string.Empty;
        public string edgeRouteSelectionReason = string.Empty;
        public LogisticsRouteFailureCategory failureCategory;
        public PathingFailureCategory pathingFailureCategory;
        public string failureReason = string.Empty;
        public List<GridCoord> visiblePath = new();

        public string Label => label ?? string.Empty;
        public GridCoord RequestedStart => requestedStart;
        public GridCoord RequestedTarget => requestedTarget;
        public LogisticsPathEndpointKind RequestedStartKind => requestedStartKind;
        public LogisticsPathEndpointKind RequestedTargetKind => requestedTargetKind;
        public GridCoord VisibleStart => visibleStart;
        public GridCoord VisibleEnd => visibleEnd;
        public PathingProfile PathingProfile => pathingProfile;
        public int TotalTravelCells => Mathf.Max(0, totalTravelCells);
        public int VisibleTravelCells => Mathf.Max(0, visibleTravelCells);
        public int OffMapTravelCells => Mathf.Max(0, offMapTravelCells);
        public float TotalTravelGameSeconds => Mathf.Max(0f, totalTravelGameSeconds);
        public float VisibleTravelGameSeconds => Mathf.Max(0f, visibleTravelGameSeconds);
        public float OffMapTravelGameSeconds => Mathf.Max(0f, offMapTravelGameSeconds);
        public float AverageSurfacePenalty => Mathf.Max(1f, averageSurfacePenalty);
        public float RoadShare => Mathf.Clamp01(roadShare);
        public int RoadCellCount => Mathf.Max(0, roadCellCount);
        public int AnchorCellCount => Mathf.Max(0, anchorCellCount);
        public int YardCellCount => Mathf.Max(0, yardOrPlotCellCount);
        public int RoughCellCount => Mathf.Max(0, roughCellCount);
        public int GeneralWalkableCellCount => Mathf.Max(0, generalWalkableCellCount);
        public int UnknownSurfaceCellCount => Mathf.Max(0, unknownSurfaceCellCount);
        public int StartEdgeRoadCandidateCount => Mathf.Max(0, startEdgeRoadCandidateCount);
        public int TargetEdgeRoadCandidateCount => Mathf.Max(0, targetEdgeRoadCandidateCount);
        public int EdgeRouteCandidatePairsTested => Mathf.Max(0, edgeRouteCandidatePairsTested);
        public LogisticsRouteQuality Quality => quality;
        public string QualityReason => qualityReason ?? string.Empty;
        public string EdgeRouteSelectionReason => edgeRouteSelectionReason ?? string.Empty;
        public LogisticsRouteFailureCategory FailureCategory => failureCategory;
        public PathingFailureCategory PathingFailureCategory => pathingFailureCategory;
        public string FailureReason => failureReason ?? string.Empty;
        public IReadOnlyList<GridCoord> VisiblePath => visiblePath;
    }
}
