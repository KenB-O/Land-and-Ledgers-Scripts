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
