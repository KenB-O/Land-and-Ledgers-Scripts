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
//
// R3: on the return leg's arrival at staging the controller dismounts
// Ethan (director.TryDismountRider — re-parent to the agent root at the
// arrival cell centre, standing idle) before logging arrival; on
// dismount failure he stays aboard rather than being placed wrong.
using System.Collections.Generic;
using LandLedgers.FirstLedger;
using LandLedgers.Orchestration.Systems;
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
        private Quaternion lastHeading = Quaternion.identity;
        private bool legActive;
        private bool regionalLegActive;
        private Transform regionalDestination;
        private Vector3 regionalTargetPosition;
        private string physicalJourneyId = string.Empty;
        private string physicalDestinationLocationId = string.Empty;
        private string physicalDestinationBusinessId = string.Empty;
        private string physicalVehicleId = string.Empty;
        private string physicalOriginLocationId = string.Empty;
        private bool regionalReturnToTownPath;
        private HorseWorldView horseView;
        private WagonWorldView wagonView;

        public bool IsLegActive => legActive || regionalLegActive;
        public string PhysicalJourneyId => physicalJourneyId;

        /// <summary>
        /// Starts walking the given path. Refuses (log + false, stays put)
        /// when the path is null or has fewer than two cells, the cell size
        /// or wagon speed is not positive, the separation is negative, or
        /// the horse/wagon views are not children of this group. The
        /// shipment/journey start conditions (Planned + authorized +
        /// journey active/Returned) are checked by the caller (F5's Begin
        /// methods); this controller only walks the path it is given.
        /// A physical freight leg requires the assigned rider identity.
        /// Missing person authority or identity refuses the leg; a team
        /// cannot begin a freight journey driverless.
        /// </summary>
        /// <param name="hideOnArrival">True for an outbound leg that reaches
        /// the current town-map boundary. Until a physical regional route is
        /// available, the active transport remains visible at that boundary;
        /// no person, animal, wagon, or renderer is hidden. False for the
        /// return leg, which ends with the team idle at staging.</param>
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
            bool hideOnArrival,
            Transform regionalDestination = null,
            string physicalJourneyId = null,
            string physicalDestinationLocationId = null,
            string physicalDestinationBusinessId = null,
            string physicalVehicleId = null,
            string physicalOriginLocationId = null)
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
            if (director == null || ethanPersonId < 0)
            {
                Debug.LogWarning($"[CoupledTransport] Refused to start leg '{legLabel}': assigned driver authority or person identity is unresolved (personId={ethanPersonId}).", this);
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
            this.regionalDestination = regionalDestination;
            this.physicalJourneyId = physicalJourneyId ?? string.Empty;
            this.physicalDestinationLocationId = physicalDestinationLocationId ?? string.Empty;
            this.physicalDestinationBusinessId = physicalDestinationBusinessId ?? string.Empty;
            this.physicalVehicleId = physicalVehicleId ?? string.Empty;
            this.physicalOriginLocationId = physicalOriginLocationId ?? string.Empty;
            regionalReturnToTownPath = false;
            regionalTargetPosition = regionalDestination != null ? regionalDestination.position : Vector3.zero;
            regionalLegActive = false;
            distanceTravelledMetres = 0f;
            lastHeading = Quaternion.identity;
            legActive = true;
            SetDriving(true);
            Debug.Log($"[CoupledTransport] {this.legLabel} started: {path.Count} waypoints from {path[0]} to {path[path.Count - 1]} at {wagonSpeedMetresPerSecond} m/s.", this);
            return true;
        }

        public void StopLeg()
        {
            if (!legActive && !regionalLegActive) return;
            legActive = false;
            regionalLegActive = false;
            SetDriving(false);
        }

        /// <summary>
        /// Starts the return portion beyond the town boundary. The team remains
        /// physically active and drives to the first point of the reversed
        /// authored town route, then continues along that route to its origin.
        /// Origin confirmation is performed only after reaching the route end.
        /// </summary>
        public bool StartRegionalReturn(
            IReadOnlyList<GridCoord> reversedPath,
            float cellSizeMetres,
            Vector3 gridOrigin,
            float wagonSpeedMetresPerSecond,
            float pairSeparationMetres,
            float turnSpeedDegreesPerSecond,
            PopulationPathingDirector director,
            int ethanPersonId,
            string journeyId,
            string originLocationId,
            string vehicleId)
        {
            if (IsLegActive || reversedPath == null || reversedPath.Count < 2
                || cellSizeMetres <= 0f || wagonSpeedMetresPerSecond <= 0f
                || director == null || ethanPersonId < 0
                || string.IsNullOrWhiteSpace(journeyId)
                || string.IsNullOrWhiteSpace(originLocationId)
                || string.IsNullOrWhiteSpace(vehicleId))
                return false;

            horseView = GetComponentInChildren<HorseWorldView>();
            wagonView = GetComponentInChildren<WagonWorldView>();
            if (horseView == null || wagonView == null) return false;

            path = reversedPath;
            this.cellSizeMetres = cellSizeMetres;
            this.gridOrigin = gridOrigin;
            this.wagonSpeedMetresPerSecond = wagonSpeedMetresPerSecond;
            this.pairSeparationMetres = Mathf.Max(0f, pairSeparationMetres);
            this.turnSpeedDegreesPerSecond = Mathf.Max(1f, turnSpeedDegreesPerSecond);
            this.director = director;
            this.ethanPersonId = ethanPersonId;
            legLabel = $"physical return journey '{journeyId}'";
            hideOnArrival = false;
            physicalJourneyId = journeyId;
            physicalVehicleId = vehicleId;
            physicalOriginLocationId = originLocationId;
            regionalDestination = null;
            regionalTargetPosition = WagonRiderPlacement.CellCenter(reversedPath[0], cellSizeMetres, gridOrigin);
            regionalTargetPosition.y = horseView.transform.position.y;
            regionalReturnToTownPath = true;
            distanceTravelledMetres = 0f;
            lastHeading = Quaternion.identity;
            regionalLegActive = true;
            SetDriving(true);
            Debug.Log($"[CoupledTransport] {legLabel} started from {FormatWorld(horseView.transform.position)} toward the authored town gateway and return route.", this);
            return true;
        }

        private void Update()
        {
            if ((!legActive && !regionalLegActive) || horseView == null || wagonView == null) return;

            // AgentMover.GetMovementTimeScale pattern: 0 when paused, else
            // game seconds per real second. No TimeManager (tests/scenes
            // without one) behaves as 1x.
            float timeScale = MovementTimeScale();
            if (timeScale <= 0f) return;
            float simulationDeltaSeconds = UnityEngine.Time.unscaledDeltaTime * timeScale;
            if (simulationDeltaSeconds <= 0f) return;

            if (regionalLegActive)
            {
                AdvanceRegionalLeg(simulationDeltaSeconds);
                return;
            }

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
                // Slice R3: keep the last non-zero heading as the
                // dismount facing — Ethan stands facing the direction
                // the team travelled when the return leg ends.
                lastHeading = Quaternion.LookRotation(heading, Vector3.up);
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
                if (!hideOnArrival && !string.IsNullOrWhiteSpace(physicalOriginLocationId))
                {
                    SimulationDrivers drivers = FindAnyObjectByType<SimulationDrivers>();
                    if (drivers == null || !drivers.ConfirmPhysicalFreightOriginArrival(
                            physicalVehicleId, ethanPersonId, physicalJourneyId, physicalOriginLocationId))
                    {
                        legActive = false;
                        SetDriving(false);
                        Debug.LogWarning($"[CoupledTransport] Reached the town return point for {legLabel}, but journey authority refused physical origin confirmation; freight remains active and unsettled.", this);
                        return;
                    }
                    physicalOriginLocationId = string.Empty;
                }
                legActive = false;
                SetDriving(false);
                if (hideOnArrival)
                {
                    if (regionalDestination != null
                        && !string.IsNullOrWhiteSpace(physicalJourneyId)
                        && !string.IsNullOrWhiteSpace(physicalDestinationLocationId)
                        && !string.IsNullOrWhiteSpace(physicalVehicleId))
                    {
                        regionalLegActive = true;
                        SetDriving(true);
                        Vector3 regionalTarget = regionalDestination.position;
                        regionalTarget.y = horseView.transform.position.y;
                        float remainingRegionalMetres = Vector3.Distance(horseView.transform.position, regionalTarget);
                        Debug.Log($"[CoupledTransport] continuing physically beyond the town boundary for {legLabel}: " +
                            $"horse={FormatWorld(horseView.transform.position)}, destination={FormatWorld(regionalTarget)}, " +
                            $"remaining={remainingRegionalMetres:0.0}m; regional destination trigger required.", this);
                    }
                    else
                    {
                        Debug.LogWarning($"[CoupledTransport] team reached the town-map boundary for {legLabel}, but its physical destination binding is incomplete; Ethan, horse, and wagon remain active and visible at the boundary.", this);
                    }
                }
                else
                {
                    // Slice R3 (Objective 22): the return leg ends with
                    // Ethan dismounting at the staging cell — after
                    // legActive = false and SetDriving(false), before
                    // the arrival log. On dismount failure he stays
                    // aboard rather than being dropped somewhere wrong
                    // (a stuck rider is visible and diagnosable; a
                    // wrongly-placed Ethan is silent corruption). No
                    // director / no rider means no dismount was ever
                    // boarded, so nothing is attempted and nothing is
                    // logged — the pair-moves-without-Ethan warning
                    // already fired at leg start.
                    if (director != null && ethanPersonId >= 0
                        && !director.TryDismountRider(ethanPersonId,
                            WagonRiderPlacement.CellCenter(path[path.Count - 1], cellSizeMetres, gridOrigin),
                            lastHeading))
                    {
                        Debug.LogWarning($"[CoupledTransport] rider dismount failed for {legLabel}; Ethan stays aboard the wagon.", this);
                    }
                    Debug.Log($"[CoupledTransport] {legLabel} arrived at {arrival}; team idle.", this);
                }
            }
        }

        private void AdvanceRegionalLeg(float simulationDeltaSeconds)
        {
            if (regionalDestination == null && !regionalReturnToTownPath)
            {
                Debug.LogWarning($"[CoupledTransport] regional travel for {legLabel} lost its physical destination; team remains active at its current position.", this);
                StopLeg();
                return;
            }

            Vector3 currentHorsePosition = horseView.transform.position;
            Vector3 target = regionalReturnToTownPath ? regionalTargetPosition : regionalDestination.position;
            target.y = currentHorsePosition.y;
            Vector3 offset = target - currentHorsePosition;
            float distanceRemaining = offset.magnitude;
            if (distanceRemaining > 0.001f)
            {
                float step = Mathf.Min(distanceRemaining,
                    wagonSpeedMetresPerSecond * simulationDeltaSeconds);
                Vector3 delta = offset * (step / distanceRemaining);
                transform.position += delta;
                Vector3 heading = offset.normalized;
                if (heading.sqrMagnitude > 1e-6f)
                {
                    lastHeading = Quaternion.LookRotation(heading, Vector3.up);
                    Quaternion wagonFacing = lastHeading * Quaternion.Euler(0f, -90f, 0f);
                    Quaternion horseFacing = wagonFacing * Quaternion.Euler(0f, 90f, 0f);
                    float turnDegrees = turnSpeedDegreesPerSecond * simulationDeltaSeconds;
                    wagonView.transform.rotation = Quaternion.RotateTowards(wagonView.transform.rotation, wagonFacing, turnDegrees);
                    horseView.transform.rotation = Quaternion.RotateTowards(horseView.transform.rotation, horseFacing, turnDegrees);
                }
            }

            if (regionalReturnToTownPath && distanceRemaining <= 0.001f)
            {
                regionalLegActive = false;
                regionalReturnToTownPath = false;
                regionalDestination = null;
                distanceTravelledMetres = 0f;
                legActive = true;
                Debug.Log($"[CoupledTransport] {legLabel} reached the town gateway at {FormatWorld(horseView.transform.position)} and continues along the authored road path.", this);
                return;
            }

            // The sensor is a real kinematic rigidbody/collider and the
            // destination is the authored trigger. Synchronize transform
            // movement before asking physics for actual overlap, which also
            // covers a large accelerated frame that crosses a thin trigger.
            Physics.SyncTransforms();
            Collider[] overlaps = Physics.OverlapSphere(horseView.transform.position, 0.5f,
                Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < overlaps.Length; i++)
            {
                PhysicalRegionalDestinationTrigger trigger = overlaps[i] != null
                    ? overlaps[i].GetComponent<PhysicalRegionalDestinationTrigger>()
                    : null;
                if (trigger != null
                    && string.Equals(trigger.LocationId, physicalDestinationLocationId, System.StringComparison.Ordinal)
                    && string.Equals(trigger.BusinessId, physicalDestinationBusinessId, System.StringComparison.Ordinal))
                {
                    OnRegionalDestinationTrigger(trigger.LocationId, trigger.BusinessId);
                    return;
                }
            }
        }

        public bool OnRegionalDestinationTrigger(string locationId, string businessId)
        {
            if (!regionalLegActive
                || !string.Equals(locationId, physicalDestinationLocationId, System.StringComparison.Ordinal)
                || !string.Equals(businessId, physicalDestinationBusinessId, System.StringComparison.Ordinal))
                return false;

            SimulationDrivers drivers = FindAnyObjectByType<SimulationDrivers>();
            if (drivers == null || !drivers.ConfirmPhysicalFreightDestinationArrival(
                    physicalVehicleId, ethanPersonId, physicalJourneyId, locationId))
            {
                Debug.LogWarning($"[CoupledTransport] physical trigger '{locationId}' was entered by {legLabel}, but the matching journey authority refused arrival confirmation; team remains present and active.", this);
                return false;
            }

            regionalLegActive = false;
            legActive = false;
            SetDriving(false);
            Debug.Log($"[CoupledTransport] physically arrived at business '{businessId}' location '{locationId}' for journey '{physicalJourneyId}'; Ethan, horse, and wagon remain active at the destination.", this);
            return true;
        }

        private static string FormatWorld(Vector3 position)
            => $"({position.x:0.00}, {position.y:0.00}, {position.z:0.00})";

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
