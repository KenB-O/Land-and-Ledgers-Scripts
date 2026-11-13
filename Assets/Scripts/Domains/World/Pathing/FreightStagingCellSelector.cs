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
using LandLedgers.World;

namespace LandLedgers.Pathing
{
    public static class FreightStagingCellSelector
    {
        /// <summary>
        /// Freight may use only premises explicitly assigned to its business and
        /// accepted by the shared business-site authority. An operator's home is
        /// not a substitute for commercial premises.
        /// </summary>
        public static bool TryValidateCommercialPremises(
            int assignedBuildingId,
            int resolvedBuildingId,
            bool businessAssignmentValid,
            out string reason)
        {
            if (assignedBuildingId < 0)
            {
                reason = "No commercial premises are assigned to the freight business; residential operator property is not an authorized staging fallback.";
                return false;
            }
            if (resolvedBuildingId < 0 || assignedBuildingId != resolvedBuildingId)
            {
                reason = $"Assigned freight premises Building {assignedBuildingId} did not resolve to the same active building.";
                return false;
            }
            if (!businessAssignmentValid)
            {
                reason = $"Assigned Building {assignedBuildingId} is not a valid operating site for this freight business.";
                return false;
            }

            reason = string.Empty;
            return true;
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
