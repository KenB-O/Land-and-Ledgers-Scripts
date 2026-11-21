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
            int targetInteriorBuildingId = -1,
            string attribution = null)
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
            Attribution = attribution;
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

        /// <summary>
        /// Lane 9c (M-R9): caller-held attribution for the failure warning —
        /// task family, person and intended destination where the emitter
        /// holds them (e.g. "task='domestic.meal-prep'
        /// activity='stove-h1-a-stove'"). Free text, never parsed; the
        /// label already carries the mover name ("Person 003 Glen Pierce
        /// door-to-door"), so this field adds what the label cannot.
        /// </summary>
        public string Attribution { get; }

        /// <summary>
        /// Lane 9c (M-R9): the StartNotWalkable (and other pathing
        /// failure) warning text, with attribution. The historical text
        /// "Pathing request failed ({category}): {reason}" is preserved
        /// as a strict prefix — the Lane 1/2 refusal classifiers match
        /// on its substrings — and the attribution rides as a suffix
        /// bracket: request label, caller attribution, and the
        /// from/to cells (plus the target interior building when the
        /// request carries one), so the next run's warnings are
        /// attributable without the checklist.
        /// </summary>
        public static string FormatFailureWarning(PathingRequest request, PathingFailureCategory category, string reason)
        {
            string message = $"Pathing request failed ({category}): {reason}";
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(request.Label)) parts.Add(request.Label);
            if (!string.IsNullOrWhiteSpace(request.Attribution)) parts.Add(request.Attribution);
            parts.Add($"from {request.Start} to {request.Target}");
            if (request.TargetInteriorBuildingId >= 0) parts.Add($"target building {request.TargetInteriorBuildingId}");
            if (parts.Count == 0) return message;
            return message + " [" + string.Join(" | ", parts) + "]";
        }

        public static PathingRequest CreateProbe(
            GridCoord start,
            GridCoord target,
            Object requester = null,
            string label = null,
            PathingProfile profile = PathingProfile.Pedestrian,
            int floorIndex = 0,
            string functionalSpaceId = null,
            int startInteriorBuildingId = -1,
            int targetInteriorBuildingId = -1,
            string attribution = null)
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
                targetInteriorBuildingId: targetInteriorBuildingId,
                attribution: attribution);
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
