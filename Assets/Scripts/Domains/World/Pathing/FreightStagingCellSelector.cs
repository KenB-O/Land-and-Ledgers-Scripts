// Slice F1 (2026-11-06, Objectives 16-17): freight staging-cell selection.
//
// Pure decision seam for the freight exit route's staging cell. The
// caller (LogisticsRuntimeManager.TryPlanFreightExitRouteForCarrier)
// resolves candidate cells from authored building/plot data; this helper
// only decides which candidate wins. First candidate that is present and
// usable (in-bounds AND a road cell, as judged by the caller's predicate)
// wins; otherwise it refuses with a reason naming both candidates, so a
// staging refusal can say which cells were tried and why each failed.
//
// No world position is invented here: every candidate comes from authored
// data supplied by the caller. No Unity types beyond GridCoord are used
// (no Quaternion), following the S6 TransportPairPlacement rule.
using System;
using System.Collections.Generic;
using LandLedgers.World;

namespace LandLedgers.Pathing
{
    public static class FreightStagingCellSelector
    {
        public readonly struct RoadsideGroundCandidate
        {
            public RoadsideGroundCandidate(GridCoord groundCoord, GridCoord adjacentRoadCoord)
            {
                GroundCoord = groundCoord;
                AdjacentRoadCoord = adjacentRoadCoord;
                OutwardStep = new GridCoord(groundCoord.x - adjacentRoadCoord.x,
                    groundCoord.z - adjacentRoadCoord.z);
            }

            public GridCoord GroundCoord { get; }
            public GridCoord AdjacentRoadCoord { get; }
            public GridCoord OutwardStep { get; }
        }

        /// <summary>A physical freight exit needs at least one traversable edge.</summary>
        public static bool HasUsableExitPath(IReadOnlyList<GridCoord> visiblePath)
        {
            return visiblePath != null && visiblePath.Count >= 2;
        }

        /// <summary>
        /// Selects a real building that can provide a physical road-access
        /// reference for staging. Commercial ownership/assignment is not a
        /// freight loading requirement; an operator's home is a valid fallback
        /// when the carrier has no assigned building.
        /// </summary>
        public static bool TrySelectPhysicalStagingBuilding(
            int assignedBuildingId,
            int resolvedBuildingId,
            int operatorHomeBuildingId,
            int resolvedHomeBuildingId,
            out int selectedBuildingId,
            out string source,
            out string reason)
        {
            selectedBuildingId = -1;
            source = string.Empty;
            if (assignedBuildingId >= 0 && resolvedBuildingId == assignedBuildingId)
            {
                selectedBuildingId = resolvedBuildingId;
                source = "carrier-location";
                reason = string.Empty;
                return true;
            }
            if (operatorHomeBuildingId >= 0 && resolvedHomeBuildingId == operatorHomeBuildingId)
            {
                selectedBuildingId = resolvedHomeBuildingId;
                source = "operator-home-location";
                reason = string.Empty;
                return true;
            }
            reason = "No assigned carrier building or operator home resolves to a physical building location for road-access lookup.";
            return false;
        }

        /// <summary>Rejects any horse or wagon footprint cell outside clear parcel ground.</summary>
        public static bool IsClearVehicleGroundCell(
            bool inBounds,
            bool insideParcel,
            int cellPlotId,
            int requiredPlotId,
            bool isRoad,
            bool hasBuilding,
            bool blocked,
            bool anchor,
            bool terrainUsable,
            float slopeDegrees,
            float maximumSlopeDegrees,
            out string reason)
        {
            if (!inBounds) reason = "outside the town grid";
            else if (!insideParcel || cellPlotId != requiredPlotId) reason = "outside the assigned parcel";
            else if (isRoad) reason = "overlaps a road";
            else if (hasBuilding) reason = "overlaps a building footprint";
            else if (blocked || anchor) reason = "overlaps blocked ground or an access anchor";
            else if (!terrainUsable || float.IsNaN(slopeDegrees) || float.IsInfinity(slopeDegrees)
                || slopeDegrees > maximumSlopeDegrees) reason = "terrain is unknown, blocked, or too steep";
            else
            {
                reason = string.Empty;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Validates physical staging ground without requiring a parcel or building
        /// authorization. Freight may be staged on any clear, traversable town cell;
        /// roads remain route surfaces rather than parking footprints.
        /// </summary>
        public static bool IsClearVehicleGroundCellAnywhere(
            bool inBounds,
            bool isRoad,
            bool hasBuilding,
            bool blocked,
            bool anchor,
            bool terrainUsable,
            float slopeDegrees,
            float maximumSlopeDegrees,
            out string reason)
        {
            if (!inBounds) reason = "outside the town grid";
            else if (isRoad) reason = "overlaps a road";
            else if (hasBuilding) reason = "overlaps a building footprint";
            else if (blocked || anchor) reason = "overlaps blocked ground or an access anchor";
            else if (!terrainUsable || float.IsNaN(slopeDegrees) || float.IsInfinity(slopeDegrees)
                || slopeDegrees > maximumSlopeDegrees) reason = "terrain is unknown, blocked, or too steep";
            else
            {
                reason = string.Empty;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Returns the extra outward offset needed to keep a parked vehicle's
        /// projected footprint clear of the adjacent road cell. Ground
        /// candidates sit one cell beside the road, which is not sufficient
        /// for a wagon wider than that clear margin.
        /// </summary>
        public static float RoadsideVehicleSetbackMeters(
            float centerDistanceFromRoadMeters,
            float roadCellSizeMeters,
            float projectedVehicleRadiusMeters,
            float clearanceMarginMeters = 0.1f)
        {
            if (!IsFiniteNonNegative(centerDistanceFromRoadMeters)
                || !IsFinitePositive(roadCellSizeMeters)
                || !IsFiniteNonNegative(projectedVehicleRadiusMeters)
                || !IsFiniteNonNegative(clearanceMarginMeters))
                return float.PositiveInfinity;

            float requiredCenterDistance = roadCellSizeMeters * 0.5f
                + projectedVehicleRadiusMeters + clearanceMarginMeters;
            return Math.Max(0f, requiredCenterDistance - centerDistanceFromRoadMeters);
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
        }

        /// <summary>
        /// Finds deterministic, nearest-first clear-ground cells beside existing
        /// roads. The caller supplies physical ground validation; this selector
        /// does not require a parcel or building permission.
        /// </summary>
        public static IReadOnlyList<RoadsideGroundCandidate> CollectNearbyRoadsideGroundCandidates(
            TownGrid grid, GridCoord originRoad, int maxCandidates, Func<GridCoord, bool> isClearGround)
        {
            var results = new List<RoadsideGroundCandidate>();
            if (grid == null || maxCandidates <= 0 || isClearGround == null || !grid.IsInBounds(originRoad))
                return results;

            GridCoord[] roadOffsets = { new(0, 1), new(1, 0), new(0, -1), new(-1, 0) };
            int maximumRadius = Math.Max(grid.Width, grid.Depth);
            for (int radius = 0; radius <= maximumRadius && results.Count < maxCandidates; radius++)
            {
                int minX = Math.Max(0, originRoad.x - radius);
                int maxX = Math.Min(grid.Width - 1, originRoad.x + radius);
                int minZ = Math.Max(0, originRoad.z - radius);
                int maxZ = Math.Min(grid.Depth - 1, originRoad.z + radius);
                for (int z = minZ; z <= maxZ && results.Count < maxCandidates; z++)
                for (int x = minX; x <= maxX && results.Count < maxCandidates; x++)
                {
                    if (Math.Max(Math.Abs(x - originRoad.x), Math.Abs(z - originRoad.z)) != radius) continue;
                    GridCoord coord = new(x, z);
                    if (!isClearGround(coord)) continue;

                    foreach (GridCoord offset in roadOffsets)
                    {
                        GridCoord neighbor = coord + offset;
                        if (!grid.IsInBounds(neighbor) || !grid.GetCell(neighbor).IsRoad) continue;
                        results.Add(new RoadsideGroundCandidate(coord, neighbor));
                        break;
                    }
                }
            }
            return results;
        }

        /// <summary>
        /// Select the staging cell from an anchor candidate (a building's
        /// road-access anchor coord) and a plot candidate (the building
        /// plot's roadAccessCell). The anchor wins when usable; the plot
        /// cell is the fallback. On success <paramref name="source"/> is
        /// "anchor" or "plot" and <paramref name="reason"/> is empty. On
        /// refusal <paramref name="reason"/> names both candidates and why
        /// neither was usable.
        /// </summary>
        public static bool TrySelectStagingCell(
            GridCoord? anchorCoord,
            GridCoord? plotCell,
            Func<GridCoord, bool> isUsableRoadCell,
            out GridCoord cell,
            out string source,
            out string reason)
        {
            cell = default;
            source = string.Empty;
            reason = string.Empty;

            if (isUsableRoadCell == null)
            {
                reason = "FreightStagingCellSelector: no road-cell predicate supplied — cannot validate a staging candidate.";
                return false;
            }

            if (anchorCoord.HasValue && isUsableRoadCell(anchorCoord.Value))
            {
                cell = anchorCoord.Value;
                source = "anchor";
                return true;
            }

            if (plotCell.HasValue && isUsableRoadCell(plotCell.Value))
            {
                cell = plotCell.Value;
                source = "plot";
                return true;
            }

            string anchorText = anchorCoord.HasValue
                ? $"({anchorCoord.Value.x}, {anchorCoord.Value.z})"
                : "absent";
            string plotText = plotCell.HasValue
                ? $"({plotCell.Value.x}, {plotCell.Value.z})"
                : "absent";
            reason = $"FreightStagingCellSelector: neither staging candidate is a usable road cell — " +
                $"anchor {anchorText} is absent or not a usable road cell, plot {plotText} is absent or not a usable road cell.";
            return false;
        }
    }
}
