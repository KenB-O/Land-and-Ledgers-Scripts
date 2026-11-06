// Slice F4 (2026-11-06, Objective 23): pure coupled-transport progress
// along a grid path.
//
// The controller (CoupledTransportMovementController, a MonoBehaviour)
// walks the shipment's routePlan.visiblePath at the authored wagon grid
// speed; this helper owns the path-distance math so the harness can pin
// it. It is Vector3/GridCoord-only by design: the rotation composition
// (wagon local +X along the heading, horse baked Y90) lives in the
// controller, which runs in real Unity — the S6 TransportPairPlacement
// rule. No Quaternion here.
//
// Coordinate frame: cell centres mirror TownGrid.CoordToWorldCenter —
// origin + ((x + 0.5) * cellSize, origin.y, (z + 0.5) * cellSize). The
// caller passes the grid origin (the controller passes
// townWorld.Grid.Origin with y set to the presentation height the LRM
// sampler uses, 0.08 m) and the grid cell size. Distances are metres, so
// the trip duration equals the planner's visibleTravelGameSeconds by
// construction: the planner computes visibleTravelCells * cellMeters /
// wagonSpeed, and visibleTravelCells is Path.Count - 1 — exactly the
// geometric length of the centre-to-centre path this helper measures.
//
// Speeds and the pair separation are passed in, never literals: the
// caller reads PathingSettings.logisticsLoadedWagonSpeedMetersPerSecond
// (2.2 m/s) and TransportPairPlacement.PairSeparationMetres (2.3736 m).
using System.Collections.Generic;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy.Transport
{
    public static class CoupledTransportProgress
    {
        /// <summary>
        /// Samples the lead (horse) position at a travelled distance along
        /// the path. Returns false — no movement, no teleport to a default —
        /// when the path is null, has fewer than two cells, has zero total
        /// length (all cells identical), or the cell size is not positive.
        /// segmentIndex is the segment the lead is on (path[i] → path[i+1]);
        /// the controller uses it for the waypoint escort. remainingMetres
        /// is the distance left to the final waypoint (0 on arrival).
        /// </summary>
        public static bool TryAdvance(
            IReadOnlyList<GridCoord> path,
            float distanceTravelledMetres,
            float cellSizeMetres,
            Vector3 origin,
            out int segmentIndex,
            out Vector3 position,
            out Vector3 heading,
            out float remainingMetres)
        {
            segmentIndex = 0;
            position = Vector3.zero;
            heading = Vector3.zero;
            remainingMetres = 0f;
            if (!SampleAt(path, distanceTravelledMetres, cellSizeMetres, origin,
                    out segmentIndex, out position, out heading, out float totalLength))
            {
                return false;
            }
            remainingMetres = Mathf.Max(0f, totalLength - Mathf.Clamp(distanceTravelledMetres, 0f, totalLength));
            return true;
        }

        /// <summary>
        /// The trailing (wagon) position: lagMetres behind the lead along
        /// the path, clamped at the path start (the wagon starts at staging
        /// while the horse is already lagMetres ahead) and at the path end.
        /// Returns false on the same degenerate inputs as TryAdvance.
        /// </summary>
        public static bool TrailingPosition(
            IReadOnlyList<GridCoord> path,
            float leadDistanceMetres,
            float lagMetres,
            float cellSizeMetres,
            Vector3 origin,
            out Vector3 position)
        {
            position = Vector3.zero;
            if (lagMetres < 0f) return false;
            return SampleAt(path, leadDistanceMetres - lagMetres, cellSizeMetres, origin,
                out _, out position, out _, out _);
        }

        /// <summary>
        /// Total centre-to-centre length of the path in metres. Returns
        /// false for a null path, fewer than two cells, or a non-positive
        /// cell size.
        /// </summary>
        public static bool TryTotalLength(
            IReadOnlyList<GridCoord> path,
            float cellSizeMetres,
            Vector3 origin,
            out float totalLengthMetres)
        {
            totalLengthMetres = 0f;
            if (path == null || path.Count < 2 || cellSizeMetres <= 0f) return false;
            totalLengthMetres = TotalLength(path, cellSizeMetres, origin);
            return totalLengthMetres > 0f;
        }

        private static bool SampleAt(
            IReadOnlyList<GridCoord> path,
            float distanceMetres,
            float cellSizeMetres,
            Vector3 origin,
            out int segmentIndex,
            out Vector3 position,
            out Vector3 heading,
            out float totalLengthMetres)
        {
            segmentIndex = 0;
            position = Vector3.zero;
            heading = Vector3.zero;
            totalLengthMetres = 0f;
            if (path == null || path.Count < 2 || cellSizeMetres <= 0f) return false;

            totalLengthMetres = TotalLength(path, cellSizeMetres, origin);
            if (totalLengthMetres <= 0f) return false;

            float clamped = Mathf.Clamp(distanceMetres, 0f, totalLengthMetres);
            float walked = 0f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector3 a = CellCenter(path[i], cellSizeMetres, origin);
                Vector3 b = CellCenter(path[i + 1], cellSizeMetres, origin);
                float length = Vector3.Distance(a, b);
                if (length <= 0f) continue; // duplicate consecutive cell: no length
                if (clamped <= walked + length || i == path.Count - 2)
                {
                    float t = Mathf.Clamp01((clamped - walked) / length);
                    position = Vector3.Lerp(a, b, t);
                    heading = (b - a).normalized;
                    segmentIndex = i;
                    return true;
                }
                walked += length;
            }

            // Reached only when the trailing segments are all zero-length:
            // the lead is at the final waypoint.
            segmentIndex = path.Count - 2;
            position = CellCenter(path[path.Count - 1], cellSizeMetres, origin);
            Vector3 last = CellCenter(path[path.Count - 1], cellSizeMetres, origin);
            Vector3 prev = CellCenter(path[path.Count - 2], cellSizeMetres, origin);
            heading = (last - prev).normalized;
            return true;
        }

        private static float TotalLength(IReadOnlyList<GridCoord> path, float cellSizeMetres, Vector3 origin)
        {
            float total = 0f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector3 a = CellCenter(path[i], cellSizeMetres, origin);
                Vector3 b = CellCenter(path[i + 1], cellSizeMetres, origin);
                total += Vector3.Distance(a, b);
            }
            return total;
        }

        private static Vector3 CellCenter(GridCoord coord, float cellSizeMetres, Vector3 origin)
        {
            return new Vector3(
                origin.x + (coord.x + 0.5f) * cellSizeMetres,
                origin.y,
                origin.z + (coord.z + 0.5f) * cellSizeMetres);
        }
    }
}
