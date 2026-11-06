// Slice R1 (2026-11-06, Objective 22): pure board-gate + dismount
// cell-centre math for Ethan riding the wagon.
//
// The director (PopulationPathingDirector, a MonoBehaviour) owns the
// SetParent calls, the local-rotation composition, and the Animator
// cross-fade — all Unity-API or MonoBehaviour code, source-reviewed
// only. This helper owns the decision logic the Roslyn harness can
// pin: whether boarding is allowed, and where a dismounting rider
// stands. The S6 TransportPairPlacement / F4 CoupledTransportProgress
// pattern: no Quaternion here.
//
// Coordinate frame: cell centres mirror TownGrid.CoordToWorldCenter —
// origin + ((x + 0.5) * cellSize, origin.y, (z + 0.5) * cellSize) —
// the same formula CoupledTransportProgress uses internally. The
// caller passes the grid origin (the controller passes
// townWorld.Grid.Origin with y set to the presentation height the LRM
// sampler uses, 0.08 m).
//
// Boarding tolerance: the owner-approved 5 m acceptance threshold for
// "is he with the team" (Plan 03 Note-1). It is NOT the operative
// arrival radius (that stays 1.5 m); it only gates whether the rider
// is close enough to the seat to board. The tolerance is inclusive:
// a rider exactly 5 m away boards.
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy.Transport
{
    public static class WagonRiderPlacement
    {
        /// <summary>
        /// The single home of the approved 5 m boarding acceptance
        /// threshold. Passed in by callers, never a literal at a call
        /// site.
        /// </summary>
        public const float BoardToleranceMetres = 5f;

        /// <summary>
        /// The board gate, pure: refuses when a route is active (never
        /// board mid-route — StartRoute would snap a parented agent off
        /// the seat), when the rider is hidden (cannot board a hidden
        /// agent), or when the rider's XZ distance to the seat's world
        /// position exceeds the tolerance. Returns true with an empty
        /// reason otherwise. The director calls it and logs the reason.
        /// </summary>
        public static bool TryAllowBoarding(
            bool isRouteActive,
            bool isHidden,
            Vector3 seatWorldPosition,
            Vector3 riderWorldPosition,
            float boardToleranceMetres,
            out string refusalReason)
        {
            refusalReason = string.Empty;
            if (isRouteActive)
            {
                refusalReason = "route active";
                return false;
            }
            if (isHidden)
            {
                refusalReason = "rider hidden";
                return false;
            }
            Vector2 seatXZ = new Vector2(seatWorldPosition.x, seatWorldPosition.z);
            Vector2 riderXZ = new Vector2(riderWorldPosition.x, riderWorldPosition.z);
            float distanceMetres = Vector2.Distance(seatXZ, riderXZ);
            if (distanceMetres > boardToleranceMetres)
            {
                refusalReason = $"rider {distanceMetres:F2} m from the seat (tolerance {boardToleranceMetres:F2} m)";
                return false;
            }
            return true;
        }

        /// <summary>
        /// The arrival-cell centre for the dismount ground position —
        /// the same formula CoupledTransportProgress uses, exposed
        /// rather than duplicated by the controller.
        /// </summary>
        public static Vector3 CellCenter(GridCoord cell, float cellSizeMetres, Vector3 origin)
        {
            return new Vector3(
                origin.x + (cell.x + 0.5f) * cellSizeMetres,
                origin.y,
                origin.z + (cell.z + 0.5f) * cellSizeMetres);
        }
    }
}
