// Slice F4 (2026-11-06, Objective 23): coupled movement controller.
//
// A MonoBehaviour attached AT RUNTIME to the "Horse and Wagon Transport
// Group" by OpeningTransportPresentationController.RefreshPresentationState
// (never by scene/prefab/asset). Harness-excluded, source-reviewed.
//
// What it does: walks a given grid path (the shipment's
// routePlan.visiblePath, handed in by F5's BeginCoupledDeparture /
// BeginCoupledReturn) at the authored wagon grid speed, moving the
// transport group so the horse lands on the path, placing the wagon at
// its path-trailing position, facing both views to the path heading.
// Ethan rides the wagon, parented to its DriverSeat by the director
// (R1, Objective 22) — he inherits the wagon's motion, so no escort
// is needed or allowed: the F4 waypoint escort is deleted (it would
// snap a parented agent off the seat via StartRoute).
//
// What it never does: no teleporting (every position is an interpolation
// along the planned path, via CoupledTransportProgress); no fake movement
// (progress is distance ÷ authored speed, and StartLeg refuses a
// degenerate path); no duplicated Ethan (one PersonState, one visual
// agent — the controller moves the existing agent, it never instantiates
// one); no state writes (it reads the path and the movement parameters
// it was handed, and touches only Transforms and animator states — never
// cash, lots, tasks, reservations, or shipment fields).
//
// Time scale: the same as AgentMover — simulation seconds per real
// second from TimeManager.CurrentGameSecondsPerRealSecond (0 when
// paused), so the team moves coherently with Ethan's own mover at 1x,
// 100x, and (when it lands) 250x. The trip duration in game time equals
// the planner's visibleTravelGameSeconds by construction (the helper
// measures the same centre-to-centre length the planner priced).
//
// Animator: DrivingWagon while moving, Idle on arrival. Codex-1b note —
// the horse rig is assigned HorseIdleController, which has no
// DrivingWagon state, so SetState(DrivingWagon) is a no-op for the horse
// today and it will glide idle-posed; that is the asset gap, not a
// controller failure.
//
// F5: on the outbound leg the controller hides the trio at the exit
// cell (views inactive, Ethan via AgentMover.HideImmediately through the
// director — the mover's hide path, confirmed by the F5 verification
// point; Ethan's root GameObject is never deactivated) and logs the exit
// line. The return leg reactivates the trio at the exit cell (the
// presentation controller's BeginCoupledReturn) and walks the reversed
// path back to staging.
using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy.Transport
{
    public sealed class CoupledTransportMovementController : MonoBehaviour
    {
        private IReadOnlyList<GridCoord> path;
        private float cellSizeMetres;
        private Vector3 gridOrigin;
        private float wagonSpeedMetresPerSecond;
        private float pairSeparationMetres;
        private float turnSpeedDegreesPerSecond;
        private PopulationPathingDirector director;
        private int ethanPersonId = -1;
        private string legLabel = string.Empty;
        private bool hideOnArrival;
        private float distanceTravelledMetres;
        private bool legActive;
        private HorseWorldView horseView;
        private WagonWorldView wagonView;

        public bool IsLegActive => legActive;

        /// <summary>
        /// Starts walking the given path. Refuses (log + false, stays put)
        /// when the path is null or has fewer than two cells, the cell size
        /// or wagon speed is not positive, the separation is negative, or
        /// the horse/wagon views are not children of this group. The
        /// shipment/journey start conditions (Planned + authorized +
        /// journey active/Returned) are checked by the caller (F5's Begin
        /// methods); this controller only walks the path it is given.
        /// director/ethanPersonId may be absent (hide/dismount skipped,
        /// logged once) — the pair still moves; Ethan's presence is
        /// presentation, not the trip itself.
        /// </summary>
        /// <param name="hideOnArrival">Slice F5: true on the outbound leg —
        /// on arrival at the exit cell the trio leaves the map visually
        /// (horse/wagon views inactive, Ethan hidden via his mover's hide
        /// path) and the exit line is logged. False on the return leg,
        /// which ends with the team idle at staging.</param>
        public bool StartLeg(
            IReadOnlyList<GridCoord> path,
            float cellSizeMetres,
            Vector3 gridOrigin,
            float wagonSpeedMetresPerSecond,
            float pairSeparationMetres,
            float turnSpeedDegreesPerSecond,
            PopulationPathingDirector director,
            int ethanPersonId,
            string legLabel,
            bool hideOnArrival)
        {
            if (path == null || path.Count < 2)
            {
                Debug.LogWarning($"[CoupledTransport] Refused to start leg '{legLabel}': no usable path (null or fewer than two waypoints); the team stays put.", this);
                return false;
            }
            if (cellSizeMetres <= 0f || wagonSpeedMetresPerSecond <= 0f || pairSeparationMetres < 0f)
            {
                Debug.LogWarning($"[CoupledTransport] Refused to start leg '{legLabel}': invalid movement parameters (cellSize={cellSizeMetres}, wagonSpeed={wagonSpeedMetresPerSecond}, separation={pairSeparationMetres}).", this);
                return false;
            }

            horseView = GetComponentInChildren<HorseWorldView>();
            wagonView = GetComponentInChildren<WagonWorldView>();
            if (horseView == null || wagonView == null)
            {
                Debug.LogWarning($"[CoupledTransport] Refused to start leg '{legLabel}': horse or wagon view is not under the transport group.", this);
                return false;
            }

            this.path = path;
            this.cellSizeMetres = cellSizeMetres;
            this.gridOrigin = gridOrigin;
            this.wagonSpeedMetresPerSecond = wagonSpeedMetresPerSecond;
            this.pairSeparationMetres = pairSeparationMetres;
            this.turnSpeedDegreesPerSecond = Mathf.Max(1f, turnSpeedDegreesPerSecond);
            this.director = director;
            this.ethanPersonId = ethanPersonId;
            this.legLabel = string.IsNullOrWhiteSpace(legLabel) ? "coupled leg" : legLabel;
            this.hideOnArrival = hideOnArrival;
            distanceTravelledMetres = 0f;
            legActive = true;
            SetDriving(true);
            Debug.Log($"[CoupledTransport] {this.legLabel} started: {path.Count} waypoints from {path[0]} to {path[path.Count - 1]} at {wagonSpeedMetresPerSecond} m/s.", this);
            return true;
        }

        public void StopLeg()
        {
            if (!legActive) return;
            legActive = false;
            SetDriving(false);
        }

        private void Update()
        {
            if (!legActive || path == null || horseView == null || wagonView == null) return;

            // AgentMover.GetMovementTimeScale pattern: 0 when paused, else
            // game seconds per real second. No TimeManager (tests/scenes
            // without one) behaves as 1x.
            float timeScale = MovementTimeScale();
            if (timeScale <= 0f) return;
            float simulationDeltaSeconds = Time.unscaledDeltaTime * timeScale;
            if (simulationDeltaSeconds <= 0f) return;

            distanceTravelledMetres += wagonSpeedMetresPerSecond * simulationDeltaSeconds;

            if (!CoupledTransportProgress.TryAdvance(path, distanceTravelledMetres, cellSizeMetres, gridOrigin,
                    out int segmentIndex, out Vector3 horsePosition, out Vector3 heading, out float remainingMetres))
            {
                Debug.LogWarning($"[CoupledTransport] {legLabel}: path sampling failed mid-leg; stopping where the team stands.", this);
                StopLeg();
                return;
            }

            // Move the group by the world delta that lands the horse on the
            // path position; the group is a neutral root (world-zero, never
            // rotated), so the horse keeps its S6 placement relative to it
            // while the pair advances as one.
            transform.position += horsePosition - horseView.transform.position;

            // The wagon trails ALONG THE PATH (not rigidly behind the
            // horse's current facing), so it follows the road around
            // corners instead of cutting them.
            if (CoupledTransportProgress.TrailingPosition(path, distanceTravelledMetres, pairSeparationMetres,
                    cellSizeMetres, gridOrigin, out Vector3 wagonPosition))
            {
                wagonView.transform.position = wagonPosition;
            }

            // Face both views to the path heading (AgentMover.TickMove
            // pattern: RotateTowards at the authored turn rate). The
            // wagon's length runs along its local +X, so its facing is
            // LookRotation(heading) turned -90° about Y; the horse's baked
            // Y90 is composed on top, exactly as Present does
            // (horseFacing = facing * Y90).
            if (heading.sqrMagnitude > 1e-6f)
            {
                Quaternion wagonFacing = Quaternion.LookRotation(heading, Vector3.up) * Quaternion.Euler(0f, -90f, 0f);
                Quaternion horseFacing = wagonFacing * Quaternion.Euler(0f, 90f, 0f);
                float turnDegrees = turnSpeedDegreesPerSecond * simulationDeltaSeconds;
                wagonView.transform.rotation = Quaternion.RotateTowards(wagonView.transform.rotation, wagonFacing, turnDegrees);
                horseView.transform.rotation = Quaternion.RotateTowards(horseView.transform.rotation, horseFacing, turnDegrees);
            }

            // R1 (Objective 22): no escort. Ethan rides the wagon,
            // parented to its DriverSeat; the F4 waypoint escort is
            // deleted — it would snap him off the seat via StartRoute.

            if (remainingMetres <= 0f)
            {
                GridCoord arrival = path[path.Count - 1];
                legActive = false;
                SetDriving(false);
                if (hideOnArrival)
                {
                    // Slice F5: the team leaves the map VISUALLY at the
                    // exit cell. The journey record already says off-map
                    // in state (from departure, day D); the visual exit
                    // lands the same day. Ethan is hidden via his mover's
                    // hide path (AgentMover.HideImmediately, through the
                    // director) — his root GameObject is never deactivated,
                    // so his mover state stays truthful and the return leg
                    // can show him again at his current location.
                    if (horseView != null) horseView.gameObject.SetActive(false);
                    if (wagonView != null) wagonView.gameObject.SetActive(false);
                    if (director != null && ethanPersonId >= 0) director.TryHidePerson(ethanPersonId);
                    Debug.Log($"[CoupledTransport] team reached the map exit, {legLabel} continues.", this);
                }
                else
                {
                    Debug.Log($"[CoupledTransport] {legLabel} arrived at {arrival}; team idle.", this);
                }
            }
        }

        private void SetDriving(bool driving)
        {
            TransportVisualState state = driving ? TransportVisualState.DrivingWagon : TransportVisualState.Idle;
            horseView?.gameObject.GetComponent<TransportAnimatorState>()?.SetState(state);
            wagonView?.gameObject.GetComponent<TransportAnimatorState>()?.SetState(state);
        }

        private static float MovementTimeScale()
        {
            LandLedgers.Time.TimeManager timeManager = LandLedgers.Time.TimeManager.Instance;
            if (timeManager == null) return 1f;
            if (timeManager.IsPaused) return 0f;
            return Mathf.Max(0f, timeManager.CurrentGameSecondsPerRealSecond);
        }
    }
}
