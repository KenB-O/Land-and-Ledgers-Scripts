using System.Collections.Generic;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Pathing
{
    public enum PathingProfile
    {
        Pedestrian = 0,
        LogisticsWagon = 1
    }

    public enum PathingFailureCategory
    {
        None = 0,
        WorldUnavailable = 1,
        StartOutsideGrid = 2,
        TargetOutsideGrid = 3,
        StartNotWalkable = 4,
        TargetNotWalkable = 5,
        SearchLimitExceeded = 6,
        NoRoute = 7
    }

    public readonly struct PathingRequest
    {
        private readonly bool suppressDiagnostics;

        public PathingRequest(
            GridCoord start,
            GridCoord target,
            Object requester = null,
            string label = null,
            PathingProfile profile = PathingProfile.Pedestrian,
            bool publishDiagnostics = true,
            bool suppressFailureLog = false,
            int floorIndex = 0,
            string functionalSpaceId = null,
            int startInteriorBuildingId = -1,
            int targetInteriorBuildingId = -1)
        {
            Start = start;
            Target = target;
            Requester = requester;
            Label = label;
            Profile = profile;
            FloorIndex = floorIndex;
            FunctionalSpaceId = functionalSpaceId;
            StartInteriorBuildingId = startInteriorBuildingId;
            TargetInteriorBuildingId = targetInteriorBuildingId;
            suppressDiagnostics = !publishDiagnostics;
            SuppressFailureLog = suppressFailureLog;
        }

        public GridCoord Start { get; }
        public GridCoord Target { get; }
        public Object Requester { get; }
        public string Label { get; }
        public PathingProfile Profile { get; }
        public int FloorIndex { get; }
        public string FunctionalSpaceId { get; }
        public int StartInteriorBuildingId { get; }
        public int TargetInteriorBuildingId { get; }
        public bool PublishDiagnostics => !suppressDiagnostics;
        public bool SuppressFailureLog { get; }

        public static PathingRequest CreateProbe(
            GridCoord start,
            GridCoord target,
            Object requester = null,
            string label = null,
            PathingProfile profile = PathingProfile.Pedestrian,
            int floorIndex = 0,
            string functionalSpaceId = null,
            int startInteriorBuildingId = -1,
            int targetInteriorBuildingId = -1)
        {
            // Candidate probes return normal success/failure data without replacing the visible debug path or logging expected misses.
            return new PathingRequest(
                start,
                target,
                requester,
                label,
                profile,
                publishDiagnostics: false,
                suppressFailureLog: true,
                floorIndex: floorIndex,
                functionalSpaceId: functionalSpaceId,
                startInteriorBuildingId: startInteriorBuildingId,
                targetInteriorBuildingId: targetInteriorBuildingId);
        }
    }

    public sealed class PathingResult
    {
        public PathingResult(PathingRequest request)
        {
            Request = request;
            Path = new List<GridCoord>(64);
            FailureReason = string.Empty;
            FailureCategory = PathingFailureCategory.None;
        }

        public PathingRequest Request { get; }
        public bool Success { get; set; }
        public int TotalCost { get; set; }
        public string FailureReason { get; set; }
        public PathingFailureCategory FailureCategory { get; set; }
        public List<GridCoord> Path { get; }
    }
}
