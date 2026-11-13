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
// Time: route distance advances only from the processed game-time delta
// published by TimeManager. A Unity render frame never contributes travel
// time, and hidden/missing render views do not prevent arrival progression.
// The trip duration in game time remains distance / authored wagon speed.
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
using System;
using LandLedgers.FirstLedger;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy.Transport
{
    public sealed class CoupledTransportMovementController : MonoBehaviour
    {
        /// <summary>Measured movement of the visible wagon body, emitted after a route step.</summary>
        public static event Action<Vector3, Vector3> ActualWagonMovementSegment;
        private IReadOnlyList<GridCoord> path;
        private float cellSizeMetres;
        private Vector3 gridOrigin;
        private Terrain[] groundTerrains;
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
        private float regionalTravelElapsedSeconds;
        private float regionalTravelDurationSeconds;
        private Renderer[] regionalPresentationRenderers;
        private bool[] regionalRendererWasEnabled;
        private string physicalJourneyId = string.Empty;
        private string physicalDestinationLocationId = string.Empty;
        private string physicalDestinationBusinessId = string.Empty;
        private string physicalVehicleId = string.Empty;
        private string physicalOriginLocationId = string.Empty;
        private bool regionalReturnToTownPath;
        private HorseWorldView horseView;
        private WagonWorldView wagonView;
        private LandLedgers.Time.TimeManager subscribedTimeManager;
        private Vector3 authoritativeHorsePosition;

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

            if (!SubscribeToSimulationClock())
            {
                Debug.LogWarning($"[CoupledTransport] Refused to start leg '{legLabel}': the authoritative simulation clock is unavailable.", this);
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
            groundTerrains = Terrain.activeTerrains;
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
            authoritativeHorsePosition = horseView.transform.position;
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

            if (!SubscribeToSimulationClock()) return false;

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
            authoritativeHorsePosition = horseView.transform.position;
            regionalTravelElapsedSeconds = 0f;
            regionalTravelDurationSeconds = Vector3.Distance(horseView.transform.position, regionalTargetPosition)
                / wagonSpeedMetresPerSecond;
            regionalReturnToTownPath = true;
            SetRegionalPresentationVisible(false);
            distanceTravelledMetres = 0f;
            lastHeading = Quaternion.identity;
            regionalLegActive = true;
            SetDriving(true);
            Debug.Log($"[CoupledTransport] {legLabel} started from {FormatWorld(horseView.transform.position)} toward the authored town gateway and return route.", this);
            return true;
        }

        /// <summary>
        /// Advances the active wagon route by a delta already processed by
        /// TimeManager. Rendering cadence and animator speed never contribute travel time.
        /// The route remains authoritative if its renderers or view transforms
        /// are temporarily unavailable after a valid departure.
        /// </summary>
        public void AdvanceSimulationTime(float simulationDeltaSeconds)
        {
            if ((!legActive && !regionalLegActive) || simulationDeltaSeconds <= 0f) return;

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

            Vector3 wagonMovementStart = wagonView != null ? wagonView.transform.position : Vector3.zero;

            // Move the group by the world delta that lands the horse on the
            // path position; the group is a neutral root (world-zero, never
            // rotated), so the horse keeps its S6 placement relative to it
            // while the pair advances as one.
            horsePosition = GroundPosition(horsePosition);
            authoritativeHorsePosition = horsePosition;
            if (horseView != null)
                transform.position += horsePosition - horseView.transform.position;

            // The wagon trails ALONG THE PATH (not rigidly behind the
            // horse's current facing), so it follows the road around
            // corners instead of cutting them.
            if (wagonView != null && CoupledTransportProgress.TrailingPosition(path, distanceTravelledMetres, pairSeparationMetres,
                    cellSizeMetres, gridOrigin, out Vector3 wagonPosition))
            {
                wagonView.transform.position = GroundPosition(wagonPosition);
            }
            Vector3 wagonMovementEnd = wagonView != null ? wagonView.transform.position : Vector3.zero;
            if (wagonView != null && (wagonMovementEnd - wagonMovementStart).sqrMagnitude > 0.0001f)
                ActualWagonMovementSegment?.Invoke(wagonMovementStart, wagonMovementEnd);

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
                Quaternion wagonFacing = lastHeading * Quaternion.Euler(0f, -90f, 0f);
                Quaternion horseFacing = wagonFacing * Quaternion.Euler(0f, 90f, 0f);
                float turnDegrees = turnSpeedDegreesPerSecond * simulationDeltaSeconds;
                if (wagonView != null)
                    wagonView.transform.rotation = Quaternion.RotateTowards(wagonView.transform.rotation, wagonFacing, turnDegrees);
                if (horseView != null)
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
                    if (!string.IsNullOrWhiteSpace(physicalJourneyId)
                        && !string.IsNullOrWhiteSpace(physicalDestinationLocationId)
                        && !string.IsNullOrWhiteSpace(physicalVehicleId))
                    {
                        regionalLegActive = true;
                        SetDriving(true);
                        Vector3 regionalTarget = regionalTargetPosition;
                        regionalTarget.y = authoritativeHorsePosition.y;
                        regionalTravelElapsedSeconds = 0f;
                        regionalTravelDurationSeconds = Vector3.Distance(authoritativeHorsePosition, regionalTarget)
                            / wagonSpeedMetresPerSecond;
                        SetRegionalPresentationVisible(false);
                        float remainingRegionalMetres = Vector3.Distance(authoritativeHorsePosition, regionalTarget);
                        Debug.Log($"[CoupledTransport] continuing physically beyond the town boundary for {legLabel}: " +
                            $"horse={FormatWorld(authoritativeHorsePosition)}, destination={FormatWorld(regionalTarget)}, " +
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
                    if (director != null && horseView != null && ethanPersonId >= 0
                        && !director.TryDismountRider(ethanPersonId,
                            GroundPosition(WagonRiderPlacement.CellCenter(path[path.Count - 1], cellSizeMetres, gridOrigin)),
                            lastHeading))
                    {
                        Debug.LogWarning($"[CoupledTransport] rider dismount failed for {legLabel}; Ethan stays aboard the wagon.", this);
                    }
                    Debug.Log($"[CoupledTransport] {legLabel} arrived at {arrival}; team idle.", this);
                }
            }
        }

        private void OnSimulationTimeAdvanced(LandLedgers.Time.SimulationFrameContext context)
        {
            // TimeManager reports the game-time increment it actually
            // processed. Do not rescale wall-clock frame time here.
            AdvanceSimulationTime(context.GameDeltaSeconds);
        }

        private bool SubscribeToSimulationClock()
        {
            if (subscribedTimeManager != null) return true;
            subscribedTimeManager = LandLedgers.Time.TimeManager.Instance;
            if (subscribedTimeManager == null) return false;
            subscribedTimeManager.SimulationTimeAdvanced += OnSimulationTimeAdvanced;
            return true;
        }

        private void OnDestroy()
        {
            if (subscribedTimeManager != null)
                subscribedTimeManager.SimulationTimeAdvanced -= OnSimulationTimeAdvanced;
            subscribedTimeManager = null;
        }

        private void AdvanceRegionalLeg(float simulationDeltaSeconds)
        {
            if (!regionalReturnToTownPath && string.IsNullOrWhiteSpace(physicalDestinationLocationId))
            {
                Debug.LogWarning($"[CoupledTransport] regional travel for {legLabel} lost its physical destination; team remains active at its current position.", this);
                StopLeg();
                return;
            }

            regionalTravelElapsedSeconds += simulationDeltaSeconds;
            if (regionalTravelElapsedSeconds < regionalTravelDurationSeconds) return;

            Vector3 currentHorsePosition = authoritativeHorsePosition;
            Vector3 target = regionalTargetPosition;
            target.y = currentHorsePosition.y;
            Vector3 heading = target - currentHorsePosition;
            if (heading.sqrMagnitude > 1e-6f)
            {
                lastHeading = Quaternion.LookRotation(heading, Vector3.up);
                if (wagonView != null) wagonView.transform.rotation = lastHeading * Quaternion.Euler(0f, -90f, 0f);
                if (horseView != null) horseView.transform.rotation = lastHeading;
            }
            // Regional transit still consumes its full distance/speed duration
            // in simulation time, but the player does not watch a presentation
            // object cross tens of kilometres of empty Unity space.
            authoritativeHorsePosition = target;
            if (horseView != null) transform.position += target - horseView.transform.position;
            if (horseView != null || wagonView != null) SetRegionalPresentationVisible(true);

            if (regionalReturnToTownPath)
            {
                regionalLegActive = false;
                regionalReturnToTownPath = false;
                regionalDestination = null;
                distanceTravelledMetres = 0f;
                legActive = true;
                Debug.Log($"[CoupledTransport] {legLabel} reached the town gateway at {FormatWorld(authoritativeHorsePosition)} and continues along the authored road path.", this);
                return;
            }
            // A successful destination binding was resolved when the journey
            // began. Arrival is emitted only after the simulated regional
            // distance/speed duration above has elapsed; shipment authority
            // still validates the vehicle, driver, journey and location.
            OnRegionalDestinationTrigger(physicalDestinationLocationId, physicalDestinationBusinessId);
        }

        private void SetRegionalPresentationVisible(bool visible)
        {
            if (regionalPresentationRenderers == null)
            {
                regionalPresentationRenderers = GetComponentsInChildren<Renderer>(true);
                regionalRendererWasEnabled = new bool[regionalPresentationRenderers.Length];
                for (int i = 0; i < regionalPresentationRenderers.Length; i++)
                    regionalRendererWasEnabled[i] = regionalPresentationRenderers[i] != null
                        && regionalPresentationRenderers[i].enabled;
            }
            for (int i = 0; i < regionalPresentationRenderers.Length; i++)
            {
                Renderer renderer = regionalPresentationRenderers[i];
                if (renderer != null) renderer.enabled = visible && regionalRendererWasEnabled[i];
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

        private Vector3 GroundPosition(Vector3 position)
        {
            if (groundTerrains == null) groundTerrains = Terrain.activeTerrains;
            foreach (Terrain terrain in groundTerrains)
            {
                if (terrain == null || terrain.terrainData == null) continue;
                Vector3 origin = terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                if (position.x < origin.x || position.x > origin.x + size.x
                    || position.z < origin.z || position.z > origin.z + size.z) continue;
                position.y = origin.y + terrain.SampleHeight(position);
                return position;
            }
            // Outside authored terrain, retain the last supported ground
            // elevation rather than reverting to the flat debug-road height.
            if (horseView != null) position.y = horseView.transform.position.y;
            return position;
        }

        private static string FormatWorld(Vector3 position)
            => $"({position.x:0.00}, {position.y:0.00}, {position.z:0.00})";

        private void SetDriving(bool driving)
        {
            TransportVisualState state = driving ? TransportVisualState.DrivingWagon : TransportVisualState.Idle;
            horseView?.gameObject.GetComponent<TransportAnimatorState>()?.SetState(state);
            wagonView?.gameObject.GetComponent<TransportAnimatorState>()?.SetState(state);
        }

    }
}
