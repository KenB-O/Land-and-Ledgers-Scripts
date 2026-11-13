using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Core;
using LandLedgers.FirstLedger;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Pathing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World;
using LandLedgers.World.Journeys;
using UnityEngine;

namespace LandLedgers.Economy.Transport
{
    [DisallowMultipleComponent]
    public sealed class OpeningTransportPresentationController : MonoBehaviour
    {
        private const string AssetsResourcePath = "OpeningTransportPresentationAssets";
        private const string OpeningHorseId = "A0";
        private const string OpeningWagonId = "opening-freight-wagon";
        private const string HitchPostResourcePath = "HitchingPost";
        private const string OpeningHitchPostId = "opening-freight-hitching-post";

        private GameObject hitchingPost;
        private HorseWorldView activeHorseView;
        private WagonWorldView activeWagonView;
        private Transform horseContainer;
        private Transform wagonContainer;
        private Transform fixtureContainer;
        private PopulationPathingDirector populationPathingDirector;
        private PhysicalRegionalTownRuntime physicalRegionalTowns;
        // Overnight 2026-11-04: the neutral root a hitched pair shares.
        // The horse used to be parented under the wagon's hitch anchor,
        // which compounded the wagon's scale onto the horse and snapped
        // the horse onto the anchor point. Hitched horse and wagon are
        // siblings here instead, and neither is ever the other's child.
        private Transform transportGroup;
        private bool hitchingPostIsAuthoredCandidate;
        [NonSerialized] private readonly List<HitchCandidateDiagnostic> hitchCandidateDiagnostics = new();

        private readonly struct HitchCandidateDiagnostic
        {
            public HitchCandidateDiagnostic(string label, Vector3 position, bool feasible, string reason)
            {
                Label = label;
                Position = position;
                Feasible = feasible;
                Reason = reason ?? string.Empty;
            }

            public string Label { get; }
            public Vector3 Position { get; }
            public bool Feasible { get; }
            public string Reason { get; }
        }

        private sealed class HitchCandidate
        {
            public string label;
            public Vector3 position;
            public Transform source;
            public Vector3 outward;
            public bool duplicate;
            public bool hasInstalledHitchHardware;
        }

        public void Present(
            SimulationSystemsHub systems,
            IReadOnlyList<BusinessInstanceState> businesses,
            TownWorldController townWorld,
            PopulationState population)
        {
            hitchCandidateDiagnostics.Clear();
            if (systems == null || businesses == null || townWorld == null)
            {
                Debug.LogError("[OpeningTransportPresentation] Refused to present opening transport: runtime authority, business list, or town world is missing.", this);
                return;
            }

            OpeningTransportPresentationAssets assets = Resources.Load<OpeningTransportPresentationAssets>(AssetsResourcePath);
            if (assets == null || assets.HorsePrefab == null || assets.WagonPrefab == null)
            {
                Debug.LogError($"[OpeningTransportPresentation] Refused to present opening transport: Resources/{AssetsResourcePath} is missing a horse or wagon prefab reference.", this);
                return;
            }
            physicalRegionalTowns = GetComponent<PhysicalRegionalTownRuntime>()
                ?? gameObject.AddComponent<PhysicalRegionalTownRuntime>();
            if (!physicalRegionalTowns.Initialize(assets.RegionalTownPrefab, townWorld))
            {
                Debug.LogError("[RegionalTowns] The authored physical regional-town prefab could not be installed; freight departure to regional suppliers will remain refused.", this);
                physicalRegionalTowns = null;
            }

            BusinessInstanceState freightBusiness = FindFreightBusiness(businesses);
            if (freightBusiness == null)
            {
                Debug.LogError("[OpeningTransportPresentation] Refused to present opening transport: Freight Company B3 is missing.", this);
                return;
            }

            var businessHorses = new List<AnimalState>();
            foreach (AnimalState animal in systems.Animals.ActiveAnimals)
            {
                if (animal != null && animal.IsActive && animal.Species == AnimalSpecies.Horse
                    && animal.OwnerKind == AnimalOwnerKind.Business
                    && animal.OwnerId == freightBusiness.InstanceId)
                    businessHorses.Add(animal);
            }

            AnimalState horse = FindOpeningHorse(businessHorses);
            if (horse == null)
            {
                Debug.LogError($"[OpeningTransportPresentation] Refused to present opening transport: no active freight horse is owned by '{freightBusiness.InstanceId}'.", this);
                return;
            }

            EquipmentAsset wagon = systems.TransportAssets.Get(OpeningWagonId);
            if (wagon == null || !wagon.IsWagon)
            {
                Debug.LogError($"[OpeningTransportPresentation] Refused to present opening transport: wagon '{OpeningWagonId}' is not registered.", this);
                return;
            }
            if (wagon.OwnerKind != "business" || wagon.OwnerId != freightBusiness.InstanceId)
            {
                Debug.LogError($"[OpeningTransportPresentation] Refused to present wagon '{wagon.AssetId}': owner is '{wagon.OwnerKind}:{wagon.OwnerId}', expected business '{freightBusiness.InstanceId}'.", this);
                return;
            }

            if (businessHorses.Count != 1)
                Debug.LogError($"[OpeningTransportPresentation] Freight Company '{freightBusiness.InstanceId}' has {businessHorses.Count} active owned horses; expected exactly one.", this);
            if (horse.AnimalId.ToString() != OpeningHorseId)
                Debug.LogError($"[OpeningTransportPresentation] Freight horse identity is '{horse.AnimalId}', expected '{OpeningHorseId}'.", this);

            PersonState operatorPerson = freightBusiness.Owner != null && freightBusiness.Owner.OwnerKind == BusinessOwnerKind.Npc
                ? population?.GetPerson(freightBusiness.Owner.PersonId)
                : null;
            if (!TryResolveBusinessRoadAccess(
                    townWorld, freightBusiness, operatorPerson,
                    out Vector3 accessPoint, out Quaternion facing, out string stagingLocation,
                    out GameObject authoredHitchFixture, out string positionRefusal))
            {
                Debug.LogError($"[OpeningTransportPresentation] Refused to place opening transport for '{freightBusiness.InstanceId}': {positionRefusal}", this);
                return;
            }

            // Slice S6: the pair is placed in-line from the authored
            // geometry. pairLateral runs toward/away from the building
            // (the axis the horse is offset from the road cell along);
            // travelAxis runs along the road (the pair's facing direction
            // — the wagon's length axis, +X in its root frame). The horse
            // stands beside the road cell and the wagon 2.3736 m behind
            // it, matching the combined `Horse and Wagon.prefab`.
            Vector3 pairLateral = facing * Vector3.forward;
            Vector3 travelAxis = facing * Vector3.right;
            TransportPairPlacement.Resolve(accessPoint, pairLateral, travelAxis,
                out Vector3 horsePosition, out Vector3 wagonPosition, out Vector3 postQuery);
            if (authoredHitchFixture != null) postQuery = authoredHitchFixture.transform.position;
            if (!TrySampleGround(townWorld, horsePosition, out horsePosition)
                || !TrySampleGround(townWorld, wagonPosition, out wagonPosition))
            {
                Debug.LogError($"[OpeningTransportPresentation] Refused to place opening transport: the horse staging point or adjacent wagon position near building {freightBusiness.AssignedBuildingId} has no valid dry in-world surface.", this);
                return;
            }

            EnsurePhysicalContainers(townWorld.transform);

            // Slice S6: Instantiate(prefab, position, rotation, parent)
            // overwrites the prefab root's rotation, discarding the
            // baked Y90 on Horse.prefab's root — so the yaw is composed
            // here. The wagon root rotation is identity, so it loses
            // nothing and keeps `facing`.
            Quaternion horseFacing = facing * Quaternion.Euler(0f, 90f, 0f);

            if (!TryFindUniqueHorseView(horse.AnimalId.ToString(), out HorseWorldView horseView)) return;
            if (horseView == null)
            {
                GameObject horseRoot = Instantiate(assets.HorsePrefab, horsePosition, horseFacing, horseContainer);
                horseView = horseRoot.GetComponent<HorseWorldView>();
                if (horseView == null) Destroy(horseRoot);
            }
            else if (horseView.transform.parent != horseContainer && !IsAttachedToWagon(horse))
                horseView.transform.SetParent(horseContainer, true);
            if (horseView == null)
            {
                Debug.LogError("[OpeningTransportPresentation] Horse prefab has no HorseWorldView; no substitute animal record was created.", this);
                return;
            }
            horseView.BindAnimal(horse.AnimalId.ToString());
            horseView.transform.SetPositionAndRotation(horsePosition, horseFacing);
            horseView.gameObject.name = $"Opening Freight Horse {horse.AnimalId}";
            horseView.gameObject.SetActive(true);
            Animator horseRigAnimator = FindHorseRigAnimator(horseView);
            if (horseRigAnimator != null && assets.HorseIdleController != null)
            {
                horseRigAnimator.runtimeAnimatorController = assets.HorseIdleController;
                horseRigAnimator.applyRootMotion = false;
                horseRigAnimator.Rebind();
                horseRigAnimator.Update(0f);
            }
            // The horse's real skinned rig is below the presentation root.
            // Pause and animation-rate changes must reach that rig, rather
            // than only the empty transport-state Animator on the root.
            horseView.GetComponent<TransportAnimatorState>()?.BindAnimator(horseRigAnimator);
            horseView.GetComponent<TransportAnimatorState>()?.SetState(TransportVisualState.Idle);

            if (!TryFindUniqueWagonView(wagon.AssetId, out WagonWorldView wagonView)) return;
            if (wagonView == null)
            {
                GameObject wagonRoot = Instantiate(assets.WagonPrefab, wagonPosition, facing, wagonContainer);
                wagonView = wagonRoot.GetComponent<WagonWorldView>();
                if (wagonView == null) Destroy(wagonRoot);
            }
            else if (wagonView.transform.parent != wagonContainer && !IsAttachedToHorse(horse, wagon))
                wagonView.transform.SetParent(wagonContainer, true);
            if (wagonView == null)
            {
                Debug.LogError("[OpeningTransportPresentation] Wagon prefab has no WagonWorldView; no duplicate equipment record was created.", this);
                return;
            }
            wagonView.BindAsset(wagon.AssetId);
            wagonView.ApplyCanvasMaterial(wagon);
            wagonView.transform.SetPositionAndRotation(wagonPosition, facing);
            wagonView.gameObject.name = $"Opening Freight Wagon {wagon.AssetId}";
            wagonView.gameObject.SetActive(true);
            wagonView.GetComponent<TransportAnimatorState>()?.SetState(TransportVisualState.Idle);

            if (authoredHitchFixture != null)
            {
                if (hitchingPost != null && !hitchingPostIsAuthoredCandidate) hitchingPost.SetActive(false);
                hitchingPost = authoredHitchFixture;
                hitchingPostIsAuthoredCandidate = true;
            }
            else
            {
                if (hitchingPostIsAuthoredCandidate) hitchingPost = null;
                hitchingPostIsAuthoredCandidate = false;
                GameObject postPrefab = Resources.Load<GameObject>(HitchPostResourcePath);
                if (postPrefab == null)
                {
                    Debug.LogError($"[OpeningTransportPresentation] Resources/{HitchPostResourcePath}.prefab is missing; A0 cannot be secured at its opening hitch post.", this);
                    return;
                }
                if (hitchingPost == null && !TryFindUniqueHitchPost(out hitchingPost)) return;
                if (hitchingPost == null) hitchingPost = Instantiate(postPrefab, fixtureContainer);
                else if (hitchingPost.transform.parent != fixtureContainer) hitchingPost.transform.SetParent(fixtureContainer, true);
                hitchingPost.name = "Freight Hitching Post";
            }
            // Slice S6: postQuery comes from TransportPairPlacement —
            // same 1.1 m magnitude as before, now on the pair-lateral
            // axis instead of the travel axis.
            if (authoredHitchFixture == null && TrySampleGround(townWorld, postQuery, out Vector3 postPosition))
                hitchingPost.transform.SetPositionAndRotation(postPosition, facing);
            hitchingPost.SetActive(true);

            if (!string.Equals(horse.AttachedWagonAssetId, wagon.AssetId, System.StringComparison.Ordinal)
                && (horse.TransportState == HorseTransportState.AvailableUnsecured
                    || horse.TransportState == HorseTransportState.SecuredAtHitchingPost))
            {
                horse.TransportState = HorseTransportState.SecuredAtHitchingPost;
                horse.SecuredFixtureId = OpeningHitchPostId;
                horse.PhysicalLocationId = OpeningHitchPostId;
            }
            activeHorseView = horseView;
            activeWagonView = wagonView;
            RefreshPresentationState(horse, wagon);

            bool horseIdle = horse.CurrentUse == HorseUseKind.None && string.IsNullOrWhiteSpace(horse.CurrentAssignmentId);
            bool wagonIdle = !wagon.IsTransportAssigned && !wagon.IsReserved;
            Debug.Log($"[OpeningTransportPresentation] startup: freight='{freightBusiness.RuntimeDisplayName}' ({freightBusiness.InstanceId}), " +
                $"operator='{freightBusiness.Owner?.DisplayName ?? "unknown"}' person={freightBusiness.Owner?.PersonId ?? -1} " +
                $"workplaceBuilding={operatorPerson?.workplaceBuildingId ?? -1} assignedBusinessBuilding={freightBusiness.AssignedBuildingId} " +
                $"stagingLocation='{stagingLocation}'; " +
                $"horse='{horse.AnimalId}' owner='{horse.OwnerId}' position={FormatPosition(horseView.transform.position)} " +
                $"visualActive={horseView.gameObject.activeInHierarchy} work={(horseIdle ? "idle" : "assigned")}; " +
                $"wagon='{wagon.AssetId}' owner='{wagon.OwnerId}' position={FormatPosition(wagonView.transform.position)} " +
                $"visualActive={wagonView.gameObject.activeInHierarchy} work={(wagonIdle ? "idle" : "assigned")}. " +
                $"Owned horse records={businessHorses.Count}; registered opening wagon records=1; wagon remains separate from horse.", this);
        }

        public void RefreshPresentationState(AnimalState horse, EquipmentAsset wagon)
        {
            if (horse == null || wagon == null || activeHorseView == null || activeWagonView == null) return;
            bool attached = horse.TransportState == HorseTransportState.AttachedToWagon
                && horse.AttachedWagonAssetId == wagon.AssetId
                && wagon.HitchingAnimalIds != null && wagon.HitchingAnimalIds.Contains(horse.AnimalId.ToString());
            if (attached)
            {
                // Overnight 2026-11-04: siblings under the neutral group
                // root, never parent and child. World transforms are
                // preserved (worldPositionStays), so the horse keeps the
                // position the hitch task left it in, the wagon's scale
                // no longer compounds onto the horse, and the authored
                // hitch anchors are untouched. This is hierarchy only;
                // coupled navigation of the pair is still future work.
                if (transportGroup == null)
                {
                    Debug.LogError("[OpeningTransportPresentation] Refused to show attached state: no transport group exists; horse and wagon left unparented rather than re-attached under the wagon.", this);
                    return;
                }
                if (activeHorseView.transform.parent != transportGroup)
                    activeHorseView.transform.SetParent(transportGroup, true);
                if (activeWagonView.transform.parent != transportGroup)
                    activeWagonView.transform.SetParent(transportGroup, true);
                // Slice F4: attach the coupled-movement controller to the
                // group at runtime (never by scene/prefab). It is dormant
                // until F5's BeginCoupledDeparture/BeginCoupledReturn hands
                // it a leg; attachment alone never moves the pair.
                if (transportGroup.GetComponent<CoupledTransportMovementController>() == null)
                    transportGroup.gameObject.AddComponent<CoupledTransportMovementController>();
                // Attachment is not movement. Keep the horse at rest until a
                // transport movement authority actually advances the coupled
                // pair; dispatch preparation alone must not play a walking loop.
                activeHorseView.gameObject.GetComponent<TransportAnimatorState>()?.SetState(TransportVisualState.Idle);
                Debug.Log($"[OpeningTransportPresentation] attached state shown: horse='{horse.AnimalId}' and wagon='{wagon.AssetId}' are siblings under the transport group; identities and world transforms unchanged.", this);
            }
            else
            {
                if (horseContainer != null && activeHorseView.transform.parent != horseContainer)
                    activeHorseView.transform.SetParent(horseContainer, true);
                if (wagonContainer != null && activeWagonView.transform.parent != wagonContainer)
                    activeWagonView.transform.SetParent(wagonContainer, true);
                if (hitchingPost != null && !hitchingPostIsAuthoredCandidate
                    && horse.TransportState == HorseTransportState.SecuredAtHitchingPost)
                    // Slice S6: the horse's own right is the pair
                    // lateral (its world rotation is facing * Y90, so its
                    // local +X maps to -pairLateral) — not world +X.
                    // The 0.35 m magnitude is kept.
                    activeHorseView.transform.position = hitchingPost.transform.position + activeHorseView.transform.right * 0.35f;
                activeHorseView.gameObject.GetComponent<TransportAnimatorState>()?.SetState(TransportVisualState.Idle);
            }
        }

        // Slice F5 (2026-11-06, Objective 25): visible departure + return.
        //
        // BeginCoupledDeparture starts the outbound leg ONLY on a real
        // departure: SimulationDrivers calls it from
        // TryDepartMillbrookShipment after the journey record is created
        // and the save is synced — never from dispatch-task start,
        // authorization alone, or the daily retry scan without a record.
        // The gate is therefore the call site; this method additionally
        // refuses a shipment with no usable route (the controller refuses
        // a degenerate path as well), so a departure with an empty
        // visiblePath moves nothing instead of teleporting.
        //
        // BeginCoupledReturn starts the return leg for a journey that
        // completed today: SimulationDrivers calls it from
        // ProcessFreightJourneyReturns for a returned vehicle record with
        // a linked live shipment. The trio reactivates at the exit cell
        // and walks the reversed path back to staging.
        //
        // Both methods read shipment/route state only and write nothing
        // beyond the log lines and the presentation (transforms, animator
        // states, view active flags, Ethan's mover visibility). No cash,
        // lot, task, reservation, or shipment field is touched.
        public bool BeginCoupledDeparture(LogisticsShipmentState shipment, string destinationLocationId)
        {
            if (shipment?.RoutePlan?.VisiblePath == null || shipment.RoutePlan.VisiblePath.Count < 2)
            {
                Debug.LogWarning($"[CoupledTransport] Refused to start the outbound leg for shipment '{shipment?.ShipmentId}': no usable visible path.", this);
                return false;
            }
            CoupledTransportMovementController controller = ResolveMovementController();
            if (controller == null) return false;
            if (physicalRegionalTowns == null
                || !physicalRegionalTowns.TryGetDestination(destinationLocationId,
                    out Transform regionalDestination, out string destinationBusinessId))
            {
                Debug.LogWarning($"[CoupledTransport] Refused to start the outbound leg for shipment '{shipment.ShipmentId}': physical destination '{destinationLocationId}' is not registered.", this);
                return false;
            }
            if (!ResolveMovementInputs(shipment, out float cellSize, out Vector3 origin,
                    out float wagonSpeed, out float turnSpeed, out PopulationPathingDirector director, out int ethanId))
            {
                return false;
            }

            // The journey id rides on the shipment's location label
            // ("off-map:<journeyId>", written at departure in step 8) —
            // it names the journey in the exit log.
            string journeyId = ParseOffMapJourneyId(shipment.physicalLocationId);
            string legLabel = string.IsNullOrWhiteSpace(journeyId)
                ? $"off-map journey for shipment '{shipment.ShipmentId}'"
                : $"off-map journey '{journeyId}'";
            if (string.IsNullOrWhiteSpace(journeyId))
            {
                Debug.LogWarning($"[CoupledTransport] Refused to start the outbound leg for shipment '{shipment.ShipmentId}': its stable off-map journey identity is missing.", this);
                return false;
            }
            // Slice R1 (Objective 22): board Ethan onto the DriverSeat
            // before the leg starts. A refused board degrades to a
            // riderless leg; a missing seat refuses the leg.
            if (!TryBoardRiderForLeg(director, ethanId, legLabel)) return false;
            EnsureArrivalSensor(controller);
            return controller.StartLeg(shipment.RoutePlan.VisiblePath, cellSize, origin,
                wagonSpeed, TransportPairPlacement.PairSeparationMetres, turnSpeed,
                director, ethanId, legLabel, hideOnArrival: true,
                regionalDestination, journeyId, destinationLocationId, destinationBusinessId,
                shipment.physicalWagonAssetId);
        }

        public bool CanBeginCoupledDeparture(string destinationLocationId)
        {
            return physicalRegionalTowns != null
                && physicalRegionalTowns.TryGetDestination(destinationLocationId, out _, out _)
                && activeHorseView != null
                && activeWagonView != null
                && transportGroup != null;
        }

        private void EnsureArrivalSensor(CoupledTransportMovementController controller)
        {
            if (controller == null || activeHorseView == null) return;
            Transform sensor = transportGroup.Find("Freight Arrival Sensor");
            if (sensor == null)
            {
                var sensorObject = new GameObject("Freight Arrival Sensor");
                sensor = sensorObject.transform;
                sensor.SetParent(transportGroup, false);
                sensorObject.AddComponent<SphereCollider>().radius = 1.5f;
                Rigidbody body = sensorObject.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }
            sensor.position = activeHorseView.transform.position;
        }

        public bool BeginCoupledReturn(LogisticsShipmentState shipment)
        {
            if (shipment?.RoutePlan?.VisiblePath == null || shipment.RoutePlan.VisiblePath.Count < 2)
            {
                Debug.LogWarning($"[CoupledTransport] Refused to start the return leg for shipment '{shipment?.ShipmentId}': no usable visible path.", this);
                return false;
            }
            CoupledTransportMovementController controller = ResolveMovementController();
            if (controller == null) return false;
            if (!ResolveMovementInputs(shipment, out float cellSize, out Vector3 origin,
                    out float wagonSpeed, out float turnSpeed, out PopulationPathingDirector director, out int ethanId))
            {
                return false;
            }

            // Reactivate the trio where the outbound leg left them: the
            // exit cell (first cell of the reversed path). Ethan comes back
            // via his mover's show path, at his current location — by the
            // exit, where the outbound leg hid him (still parented to
            // the seat; his transform never moved while hidden).
            var reversed = new List<GridCoord>(shipment.RoutePlan.VisiblePath);
            reversed.Reverse();
            // The outbound boundary now keeps the same active views and
            // rider visible, so re-entry must not reset Ethan's mover or
            // substitute another presentation.
            Debug.Log($"[CoupledTransport] team re-entered at {reversed[0]}, returning to staging.", this);
            // Slice R1 (Objective 22): board him again AFTER the show —
            // ShowAtCurrentLocation reverts his Animator to Locomotion,
            // so boarding must follow it (Note-R2). He was never
            // unparented at the exit, so this is an idempotent
            // re-application of the seat + sitting pose.
            string returnLegLabel = $"return leg for shipment '{shipment.ShipmentId}'";
            if (!TryBoardRiderForLeg(director, ethanId, returnLegLabel)) return false;
            return controller.StartLeg(reversed, cellSize, origin,
                wagonSpeed, TransportPairPlacement.PairSeparationMetres, turnSpeed,
                director, ethanId, returnLegLabel, hideOnArrival: false);
        }

        /// <summary>
        /// Begins the physical regional return after the shipment's source lot
        /// has been committed into the wagon. The controller traverses back to
        /// the real town-route boundary and then follows the existing reversed
        /// route to staging; journey authority is confirmed only at that origin.
        /// </summary>
        public bool BeginCoupledPhysicalReturn(LogisticsShipmentState shipment, OffMapJourneyRecord journey, int operatorPersonId)
        {
            if (shipment == null || journey == null || !journey.IsActive
                || !journey.RequiresPhysicalArrivalConfirmation
                || !journey.PhysicalDestinationArrivalConfirmed
                || journey.PhysicalOriginArrivalConfirmed
                || !shipment.loadApplied
                || !string.Equals(shipment.physicalWagonAssetId, journey.VehicleId, System.StringComparison.Ordinal)
                || operatorPersonId < 0
                || !string.Equals(shipment.physicalDriverPersonId, "P" + operatorPersonId, System.StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(journey.OriginLocationId)
                || shipment.RoutePlan?.VisiblePath == null || shipment.RoutePlan.VisiblePath.Count < 2)
            {
                return false;
            }

            Transform destination = null;
            bool destinationResolved = physicalRegionalTowns != null
                && physicalRegionalTowns.TryGetDestination(journey.DestinationLocationId, out destination, out _);
            if (!destinationResolved
                || activeHorseView == null || activeWagonView == null || transportGroup == null
                || Vector3.Distance(activeHorseView.transform.position, destination.position) > 8f)
                return false;

            CoupledTransportMovementController controller = ResolveMovementController();
            if (controller == null) return false;
            if (controller.IsLegActive)
                return string.Equals(controller.PhysicalJourneyId, journey.JourneyId, System.StringComparison.Ordinal);
            if (!ResolveMovementInputs(shipment, out float cellSize, out Vector3 origin,
                    out float wagonSpeed, out float turnSpeed, out PopulationPathingDirector director, out int ethanId)
                || ethanId != operatorPersonId)
            {
                return false;
            }
            if (!TryBoardRiderForLeg(director, ethanId, $"physical return journey '{journey.JourneyId}'")) return false;

            var reversed = new List<GridCoord>(shipment.RoutePlan.VisiblePath);
            reversed.Reverse();
            return controller.StartRegionalReturn(reversed, cellSize, origin, wagonSpeed,
                TransportPairPlacement.PairSeparationMetres, turnSpeed, director, ethanId,
                journey.JourneyId, journey.OriginLocationId, journey.VehicleId);
        }

        /// <summary>
        /// Slice R1 (Objective 22): board the driver onto the wagon's
        /// DriverSeat for a leg. A missing seat, unresolved driver, or
        /// failed boarding refuses the leg; physical freight never starts
        /// without its assigned operator.
        /// </summary>
        private bool TryBoardRiderForLeg(PopulationPathingDirector director, int ethanPersonId, string legLabel)
        {
            Transform seat = activeWagonView != null ? activeWagonView.DriverSeat : null;
            if (seat == null)
            {
                Debug.LogWarning($"[CoupledTransport] Refused to start {legLabel}: the wagon has no DriverSeat.", this);
                return false;
            }
            if (director == null || ethanPersonId < 0)
            {
                Debug.LogWarning($"[CoupledTransport] Refused to start {legLabel}: assigned driver authority or person identity is unresolved.", this);
                return false;
            }
            if (!director.TryApproachTransportSeat(ethanPersonId, seat,
                    WagonRiderPlacement.BoardToleranceMetres, out bool readyToBoard) || !readyToBoard)
                return false;
            if (!director.TryBoardRider(ethanPersonId, seat, legLabel))
            {
                Debug.LogWarning($"[CoupledTransport] Refused to start {legLabel}: assigned rider P{ethanPersonId} could not be boarded.", this);
                return false;
            }
            return true;
        }

        public bool IsDriverApproachingOrStaged(int personId)
        {
            PopulationPathingDirector director = FindAnyObjectByType<PopulationPathingDirector>();
            return activeWagonView != null && director != null
                && director.IsPersonApproachingTransportSeat(personId, activeWagonView.DriverSeat);
        }

        public bool TryPrepareDriverAtStaging(int personId, out bool readyToDepart)
        {
            readyToDepart = false;
            PopulationPathingDirector director = FindAnyObjectByType<PopulationPathingDirector>();
            Transform seat = activeWagonView != null ? activeWagonView.DriverSeat : null;
            if (director == null || seat == null) return false;
            if (!director.TryApproachTransportSeat(personId, seat, WagonRiderPlacement.BoardToleranceMetres, out readyToDepart))
                return false;
            return true;
        }

        private CoupledTransportMovementController ResolveMovementController()
        {
            if (transportGroup == null)
            {
                Debug.LogWarning("[CoupledTransport] Refused to start a leg: no transport group exists.", this);
                return null;
            }
            CoupledTransportMovementController controller =
                transportGroup.GetComponent<CoupledTransportMovementController>();
            if (controller == null)
                controller = transportGroup.gameObject.AddComponent<CoupledTransportMovementController>();
            return controller;
        }

        private bool ResolveMovementInputs(LogisticsShipmentState shipment,
            out float cellSizeMetres, out Vector3 gridOrigin,
            out float wagonSpeedMetresPerSecond, out float turnSpeedDegreesPerSecond,
            out PopulationPathingDirector director, out int ethanPersonId)
        {
            cellSizeMetres = 0f;
            gridOrigin = Vector3.zero;
            wagonSpeedMetresPerSecond = 0f;
            turnSpeedDegreesPerSecond = 0f;
            director = null;
            ethanPersonId = -1;

            TownWorldController townWorld = FindAnyObjectByType<TownWorldController>();
            if (townWorld == null || townWorld.Grid == null)
            {
                Debug.LogWarning("[CoupledTransport] Refused to start a leg: no town world grid.", this);
                return false;
            }
            cellSizeMetres = townWorld.Grid.CellSizeMeters;
            // Flat presentation height, matching the LRM sampler
            // (TrySampleVisibleShipment passes 0.08 m).
            gridOrigin = new Vector3(townWorld.Grid.Origin.x, 0.08f, townWorld.Grid.Origin.z);

            // Authored speeds from the live PathingSettings when present;
            // a fresh instance carries the same authored defaults (2.2 m/s,
            // 720 deg/s), so the fallback invents nothing.
            PathingSettings settings = FindAnyObjectByType<PathingManager>()?.Settings ?? new PathingSettings();
            wagonSpeedMetresPerSecond = settings.logisticsLoadedWagonSpeedMetersPerSecond;
            turnSpeedDegreesPerSecond = settings.turnSpeedDegreesPerSecond;

            // Bootstrap owns the live population pathing authority. Prefer the
            // direct runtime binding so a coupled freight presentation cannot
            // silently become riderless when Unity's global object lookup runs
            // before the bootstrap-created director is registered.
            director = populationPathingDirector != null
                ? populationPathingDirector
                : FindAnyObjectByType<PopulationPathingDirector>();
            ethanPersonId = ParsePersonId(shipment?.physicalDriverPersonId);
            if (director == null || ethanPersonId < 0)
            {
                Debug.LogWarning($"[CoupledTransport] Rider resolution failed for shipment '{shipment?.ShipmentId}': director={(director != null ? director.name : "missing")}, serializedDriverId='{shipment?.physicalDriverPersonId ?? string.Empty}', parsedPersonId={ethanPersonId}.", this);
                return false;
            }
            return true;
        }

        public void BindPopulationPathingDirector(PopulationPathingDirector director)
        {
            populationPathingDirector = director;
        }

        private static string ParseOffMapJourneyId(string physicalLocationId)
        {
            const string prefix = "off-map:";
            if (string.IsNullOrWhiteSpace(physicalLocationId)
                || !physicalLocationId.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                return string.Empty;
            }
            return physicalLocationId.Substring(prefix.Length);
        }

        private static int ParsePersonId(string driverPersonId)
        {
            if (string.IsNullOrWhiteSpace(driverPersonId)) return -1;
            // EntityId.ToString is "<kind prefix><id>" (e.g. "P7") — the
            // same shape FreightProductionDriver.TryParseWithKind reads.
            if (!EntityKindCatalog.TryGetPrefix(EntityKind.Person, out string prefix)
                || !driverPersonId.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                return -1;
            }
            return int.TryParse(driverPersonId.Substring(prefix.Length), out int id) ? id : -1;
        }

        private bool IsAttachedToWagon(AnimalState horse)
            => horse != null && horse.TransportState == HorseTransportState.AttachedToWagon
               && !string.IsNullOrWhiteSpace(horse.AttachedWagonAssetId);

        private bool IsAttachedToHorse(AnimalState horse, EquipmentAsset wagon)
            => horse != null && wagon != null
               && horse.TransportState == HorseTransportState.AttachedToWagon
               && string.Equals(horse.AttachedWagonAssetId, wagon.AssetId, System.StringComparison.Ordinal);

        private void EnsurePhysicalContainers(Transform worldRoot)
        {
            Transform runtimeRoot = worldRoot.Find("Runtime Entities");
            if (runtimeRoot == null)
            {
                var root = new GameObject("Runtime Entities");
                runtimeRoot = root.transform;
                runtimeRoot.SetParent(worldRoot, false);
            }
            horseContainer = EnsureContainer(runtimeRoot, "Animals");
            wagonContainer = EnsureContainer(runtimeRoot, "Transport");
            fixtureContainer = EnsureContainer(runtimeRoot, "World Fixtures");
            // Find-or-create by name: repeated hitching, and repeated
            // presentation, must never duplicate the group.
            transportGroup = EnsureContainer(runtimeRoot, "Horse and Wagon Transport Group");
        }

        private static Transform EnsureContainer(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) return child;
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            return root.transform;
        }

        private bool TryFindUniqueHitchPost(out GameObject found)
        {
            found = null;
            foreach (Transform candidate in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate == null || candidate.gameObject.scene != gameObject.scene || candidate.name != "Freight Hitching Post") continue;
                if (found != null)
                {
                    Debug.LogError("[OpeningTransportPresentation] Duplicate hitching-post fixtures exist; refusing to create or select another.", this);
                    return false;
                }
                found = candidate.gameObject;
            }
            return true;
        }

        private static BusinessInstanceState FindFreightBusiness(IReadOnlyList<BusinessInstanceState> businesses)
        {
            if (businesses == null) return null;
            foreach (BusinessInstanceState business in businesses)
                if (business != null && business.BusinessType == BusinessType.LiveryFreight
                    && business.InstanceId == "livery_freight_003") return business;
            foreach (BusinessInstanceState business in businesses)
                if (business != null && business.BusinessType == BusinessType.LiveryFreight) return business;
            return null;
        }

        private static AnimalState FindOpeningHorse(List<AnimalState> horses)
        {
            if (horses == null) return null;
            foreach (AnimalState horse in horses)
                if (horse.AnimalId.ToString() == OpeningHorseId) return horse;
            foreach (AnimalState horse in horses)
                if (horse.PhysicalLocationId == OpeningTransportStock.OpeningStorageId
                    || horse.Breed == "opening-draft-horse") return horse;
            return horses.Count == 1 ? horses[0] : null;
        }

        private static Animator FindHorseRigAnimator(HorseWorldView horseView)
        {
            if (horseView == null) return null;
            Animator[] animators = horseView.GetComponentsInChildren<Animator>(true);
            foreach (Animator candidate in animators)
                if (candidate != null && candidate.transform != horseView.transform && candidate.avatar != null)
                    return candidate;
            return null;
        }

        private bool TryResolveBusinessRoadAccess(
            TownWorldController townWorld,
            BusinessInstanceState business,
            PersonState operatorPerson,
            out Vector3 accessPoint,
            out Quaternion facing,
            out string stagingLocation,
            out GameObject authoredHitchFixture,
            out string refusal)
        {
            accessPoint = default;
            facing = Quaternion.identity;
            stagingLocation = string.Empty;
            authoredHitchFixture = null;
            refusal = string.Empty;

            PlacedBuilding building = null;
            if (business.AssignedBuildingId >= 0
                && townWorld.TryGetBuildingById(business.AssignedBuildingId, out PlacedBuilding businessBuilding)
                && businessBuilding != null)
                building = businessBuilding;

            SharedBusinessRuntimeManager businessAuthority = FindAnyObjectByType<SharedBusinessRuntimeManager>();
            bool assignmentValid = businessAuthority != null
                && businessAuthority.IsBusinessAssignmentValid(business);
            if (!FreightStagingCellSelector.TryValidateCommercialPremises(
                    business.AssignedBuildingId, building != null ? building.id : -1,
                    assignmentValid, out string premisesRefusal))
            {
                refusal = $"Freight Company '{business.InstanceId}' cannot stage its horse and wagon: {premisesRefusal}";
                return false;
            }
            stagingLocation = $"Freight Company commercial premises, building {building.id}";
            TownGrid grid = townWorld.Grid;
            if (grid == null)
            {
                refusal = "the active town grid is unavailable for vehicle staging.";
                return false;
            }

            GridCoord roadAccess = default;
            bool hasRoadAccess = building.TryGetRoadAccessCoord(out GridCoord authoredRoadAccess)
                && TryResolveAdjacentRoadCell(grid, authoredRoadAccess, out roadAccess);
            if (!hasRoadAccess && townWorld.TryGetPlotById(building.plotId, out TownPlot accessPlot)
                && accessPlot != null && accessPlot.id == building.plotId && accessPlot.buildingId == building.id
                && TryResolveAdjacentRoadCell(grid, accessPlot.roadAccessCell, out roadAccess))
            {
                hasRoadAccess = true;
                Debug.Log($"[OpeningTransportPresentation] Building {building.id} uses its owning plot's validated road-access cell {accessPlot.roadAccessCell}; its authored building anchor has no adjacent road cell.", this);
            }
            if (!hasRoadAccess)
            {
                string anchorText = building.TryGetRoadAccessCoord(out GridCoord anchorForLog) ? anchorForLog.ToString() : "missing";
                refusal = $"staging building {building.id} road-access anchor {anchorText} and its matching plot frontage have no adjacent road cell.";
                return false;
            }

            Vector3 roadCellCenter = townWorld.GetWorldCenterForRect(new GridRect(roadAccess.x, roadAccess.z, 1, 1));
            if (!TrySampleGround(townWorld, roadCellCenter, out Vector3 sampledRoadPosition))
            {
                refusal = $"road-access cell {roadAccess} for staging building {building.id} does not resolve to a dry in-world surface.";
                return false;
            }

            if (!TryResolvePlotYardStaging(townWorld, building, roadAccess, sampledRoadPosition,
                    out accessPoint, out facing, out authoredHitchFixture, out string stagingRefusal))
            {
                refusal = stagingRefusal;
                return false;
            }
            stagingLocation += $" plot {building.plotId} validated hitch area";
            return true;
        }

        private bool TryResolvePlotYardStaging(
            TownWorldController townWorld,
            PlacedBuilding building,
            GridCoord roadAccess,
            Vector3 roadPosition,
            out Vector3 accessPoint,
            out Quaternion facing,
            out GameObject authoredHitchFixture,
            out string refusal)
        {
            accessPoint = default;
            facing = Quaternion.identity;
            authoredHitchFixture = null;
            refusal = string.Empty;
            TownGrid grid = townWorld != null ? townWorld.Grid : null;
            if (grid == null || building == null || building.plotId < 0
                || !townWorld.TryGetPlotById(building.plotId, out TownPlot plot)
                || plot == null || !grid.ContainsRect(plot.bounds))
            {
                refusal = $"building {building?.id ?? -1} has no valid containing parcel for vehicle staging.";
                return false;
            }

            OpeningTransportPresentationAssets assets = Resources.Load<OpeningTransportPresentationAssets>(AssetsResourcePath);
            if (assets == null || assets.HorsePrefab == null || assets.WagonPrefab == null)
            {
                refusal = "opening horse/wagon prefab clearance data is unavailable.";
                return false;
            }

            PathingManager pathing = FindAnyObjectByType<PathingManager>();
            if (pathing == null || pathing.Grid != grid)
            {
                refusal = "the active town pathing authority is unavailable for hitch-area access checks.";
                return false;
            }

            BuildingFootprintAuthority authority = building.runtimeFootprintAuthority;
            if (authority == null)
            {
                refusal = $"building {building.id} has no runtime footprint authority for side and obstacle checks.";
                return false;
            }

            if (!TryGetPrefabLocalBounds(assets.HorsePrefab, out Bounds horseBounds)
                || !TryGetPrefabLocalBounds(assets.WagonPrefab, out Bounds wagonBounds))
            {
                refusal = "the authored horse or wagon has no measurable physical bounds.";
                return false;
            }

            List<HitchCandidate> candidates = CollectHitchCandidates(authority, grid);
            if (candidates.Count == 0)
            {
                refusal = $"building {building.id} has no authored hitch candidates or valid side space.";
                return false;
            }

            float bestCost = float.PositiveInfinity;
            Vector3 bestHorsePosition = default;
            Vector3 bestPairLateral = default;
            Quaternion bestFacing = Quaternion.identity;
            string bestLabel = string.Empty;
            GameObject bestAuthoredFixture = null;
            string lastRejection = "no candidate was feasible.";

            foreach (HitchCandidate candidate in candidates)
            {
                if (candidate.duplicate)
                {
                    RecordHitchCandidate(candidate, false, "duplicate authored position");
                    continue;
                }

                Vector3 pairLateral = candidate.outward;
                Quaternion candidateFacing = Quaternion.LookRotation(pairLateral, Vector3.up);
                Quaternion horseFacing = candidateFacing * Quaternion.Euler(0f, 90f, 0f);
                Vector3 travelAxis = candidateFacing * Vector3.right;
                if (candidate.source != null && !candidate.hasInstalledHitchHardware)
                {
                    lastRejection = "authored HorseStand has no active physical hitching hardware.";
                    RecordHitchCandidate(candidate, false, lastRejection);
                    continue;
                }

                Vector3 horseQuery = candidate.position;
                if (candidate.source != null)
                {
                    float fixtureRadius = GetColliderRadiusAlong(candidate.source, pairLateral);
                    float horseRadius = GetProjectedBoundsRadius(horseBounds,
                        assets.HorsePrefab.transform.localScale, horseFacing, pairLateral);
                    horseQuery += pairLateral * (fixtureRadius + horseRadius + 0.12f);
                }
                if (!TrySampleGround(townWorld, horseQuery, out Vector3 horsePosition))
                {
                    lastRejection = "horse position is outside dry walkable terrain.";
                    RecordHitchCandidate(candidate, false, lastRejection);
                    continue;
                }

                Vector3 wagonQuery = horsePosition - travelAxis * TransportPairPlacement.PairSeparationMetres;
                Vector3 postQuery = candidate.source != null
                    ? candidate.position
                    : horsePosition + pairLateral * TransportPairPlacement.HitchPostOffsetMetres;
                if (!TrySampleGround(townWorld, wagonQuery, out Vector3 wagonPosition)
                    || !TrySampleGround(townWorld, postQuery, out Vector3 postPosition))
                {
                    lastRejection = "wagon or required hitch hardware position has no dry terrain sample.";
                    RecordHitchCandidate(candidate, false, lastRejection);
                    continue;
                }

                string horseReason = string.Empty;
                string wagonReason = string.Empty;
                bool horseClear = TryValidateVehicleFootprint(townWorld, plot, building, horsePosition, horseFacing,
                    assets.HorsePrefab, horseBounds, candidate.source, out horseReason);
                bool wagonClear = horseClear && TryValidateVehicleFootprint(townWorld, plot, building, wagonPosition, candidateFacing,
                    assets.WagonPrefab, wagonBounds, null, out wagonReason);
                if (!horseClear || !wagonClear)
                {
                    lastRejection = !string.IsNullOrEmpty(horseReason) ? "horse " + horseReason : "wagon " + wagonReason;
                    RecordHitchCandidate(candidate, false, lastRejection);
                    continue;
                }

                if (!TryValidateHitchFixtureCell(grid, plot, postPosition, out string fixtureReason))
                {
                    lastRejection = fixtureReason;
                    RecordHitchCandidate(candidate, false, lastRejection);
                    continue;
                }

                GridCoord horseCoord = grid.WorldToCoord(horsePosition);
                GridCoord wagonCoord = grid.WorldToCoord(wagonPosition);
                if (!pathing.TryFindPath(horseCoord, roadAccess, out PathingResult horseRoute, this,
                        "opening hitch horse approach", PathingProfile.Pedestrian)
                    || !pathing.TryFindPath(wagonCoord, roadAccess, out PathingResult wagonRoute, this,
                        "opening hitch wagon approach", PathingProfile.LogisticsWagon))
                {
                    lastRejection = "no path exists between the candidate and its road access for both horse and wagon.";
                    RecordHitchCandidate(candidate, false, lastRejection);
                    continue;
                }

                if (!TryValidateWagonApproachCells(grid, plot, wagonBounds, assets.WagonPrefab.transform.localScale,
                        wagonRoute.Path, out string turnReason))
                {
                    lastRejection = turnReason;
                    RecordHitchCandidate(candidate, false, lastRejection);
                    continue;
                }

                float routeCost = horseRoute.TotalCost + wagonRoute.TotalCost;
                RecordHitchCandidate(candidate, true,
                    $"clear parcel and connected road access; route cost {routeCost}");
                if (routeCost >= bestCost) continue;
                bestCost = routeCost;
                bestHorsePosition = horsePosition;
                bestPairLateral = pairLateral;
                bestFacing = candidateFacing;
                bestLabel = candidate.label;
                bestAuthoredFixture = candidate.source != null ? candidate.source.gameObject : null;
            }

            if (float.IsPositiveInfinity(bestCost))
            {
                string details = string.Empty;
                foreach (HitchCandidateDiagnostic candidate in hitchCandidateDiagnostics)
                    details += $" {candidate.Label}@{FormatPosition(candidate.Position)}={candidate.Reason};";
                refusal = $"no feasible horse-and-wagon hitch side for building {building.id}: {lastRejection} Candidates:{details}";
                return false;
            }

            facing = bestFacing;
            accessPoint = bestHorsePosition - bestPairLateral * TransportPairPlacement.HorseRoadOffsetMetres;
            authoredHitchFixture = bestAuthoredFixture;
            Debug.Log($"[OpeningTransportPresentation] Selected hitch candidate '{bestLabel}' for building {building.id} at {FormatPosition(bestHorsePosition)}; all other candidates were rejected or had a higher connected-route cost.", this);
            return true;
        }

        private List<HitchCandidate> CollectHitchCandidates(BuildingFootprintAuthority authority, TownGrid grid)
        {
            List<HitchCandidate> candidates = new();
            if (authority.ActivityLocations != null)
            {
                foreach (BuildingActivityLocationAuthoring location in authority.ActivityLocations)
                {
                    if (location == null || location.type != BuildingActivityLocationType.HitchCandidate) continue;
                    Vector3 outward = ResolveSideOutward(authority, location.positionLocal);
                    AddCandidate(candidates, location.id, authority.transform.TransformPoint(location.positionLocal), null, outward);
                }
            }

            foreach (Transform child in authority.GetComponentsInChildren<Transform>(true))
            {
                if (child == authority.transform || child.name.IndexOf("HorseStand", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Vector3 localPosition = authority.transform.InverseTransformPoint(child.position);
                bool hasHardware = child.gameObject.activeInHierarchy;
                Collider[] colliders = child.GetComponentsInChildren<Collider>(true);
                bool hasEnabledCollider = false;
                foreach (Collider collider in colliders)
                    if (collider != null && collider.enabled && !collider.isTrigger) { hasEnabledCollider = true; break; }
                AddCandidate(candidates, child.name, child.position, child,
                    ResolveSideOutward(authority, localPosition), hasHardware && hasEnabledCollider);
            }

            Vector3 right = (authority.FrontRightCornerLocal - authority.FrontLeftCornerLocal).normalized;
            bool hasFront = HasCandidateForSide(authority, candidates, authority.FrontOutwardLocalDirection);
            bool hasBack = HasCandidateForSide(authority, candidates, -authority.FrontOutwardLocalDirection);
            bool hasRight = HasCandidateForSide(authority, candidates, right);
            bool hasLeft = HasCandidateForSide(authority, candidates, -right);
            if (!hasFront || !hasBack || !hasRight || !hasLeft)
            {
                // Geometry-derived previews are only candidates. A real opening HitchingPost
                // fixture is still required before the horse can be marked secured.
                OpeningTransportPresentationAssets assets = Resources.Load<OpeningTransportPresentationAssets>(AssetsResourcePath);
                float margin = assets != null && TryGetPrefabLocalBounds(assets.HorsePrefab, out Bounds horseBounds)
                    ? Mathf.Max(horseBounds.extents.x, horseBounds.extents.z) + grid.CellSizeMeters * 0.5f
                    : grid.CellSizeMeters * 0.5f;
                if (!hasFront) AddGeneratedCandidate(authority, "Front", authority.FrontEdgeCenterLocalPoint,
                    authority.FrontOutwardLocalDirection, margin, candidates);
                if (!hasBack) AddGeneratedCandidate(authority, "Back", authority.BackEdgeCenterLocalPoint,
                    -authority.FrontOutwardLocalDirection, margin, candidates);
                if (!hasRight) AddGeneratedCandidate(authority, "Right",
                    (authority.FrontRightCornerLocal + authority.BackRightCornerLocal) * 0.5f, right, margin, candidates);
                if (!hasLeft) AddGeneratedCandidate(authority, "Left",
                    (authority.FrontLeftCornerLocal + authority.BackLeftCornerLocal) * 0.5f, -right, margin, candidates);
            }
            return candidates;
        }

        private static bool HasCandidateForSide(BuildingFootprintAuthority authority,
            List<HitchCandidate> candidates, Vector3 sideOutwardLocal)
        {
            Vector3 expected = authority.transform.TransformDirection(sideOutwardLocal).normalized;
            foreach (HitchCandidate candidate in candidates)
            {
                if (Vector3.Dot(candidate.outward, expected) > 0.95f) return true;
            }
            return false;
        }

        private static void AddGeneratedCandidate(BuildingFootprintAuthority authority, string side, Vector3 edgeCenter,
            Vector3 outwardLocal, float margin, List<HitchCandidate> candidates)
        {
            Vector3 local = edgeCenter + outwardLocal.normalized * margin;
            Vector3 world = authority.transform.TransformPoint(local);
            Vector3 outwardWorld = authority.transform.TransformDirection(outwardLocal).normalized;
            AddCandidate(candidates, $"{side} geometry candidate", world, null, outwardWorld, false);
        }

        private static void AddCandidate(List<HitchCandidate> candidates, string label, Vector3 position,
            Transform source, Vector3 outward, bool hasInstalledHitchHardware = false)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                HitchCandidate existing = candidates[i];
                Vector2 delta = new(position.x - existing.position.x, position.z - existing.position.z);
                if (delta.sqrMagnitude > 0.0025f) continue;
                candidates.Add(new HitchCandidate
                {
                    label = label,
                    position = position,
                    source = source,
                    outward = outward.normalized,
                    duplicate = true,
                    hasInstalledHitchHardware = hasInstalledHitchHardware
                });
                return;
            }
            candidates.Add(new HitchCandidate
            {
                label = label,
                position = position,
                source = source,
                outward = outward.normalized,
                hasInstalledHitchHardware = hasInstalledHitchHardware
            });
        }

        private void RecordHitchCandidate(HitchCandidate candidate, bool feasible, string reason)
        {
            if (candidate?.source != null && candidate.source.gameObject.activeSelf != feasible)
                candidate.source.gameObject.SetActive(feasible);
            hitchCandidateDiagnostics.Add(new HitchCandidateDiagnostic(candidate?.label ?? "unknown",
                candidate != null ? candidate.position : default, feasible, reason));
        }

        private static float GetColliderRadiusAlong(Transform root, Vector3 direction)
        {
            if (root == null) return 0f;
            direction.y = 0f;
            direction.Normalize();
            float radius = 0f;
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            foreach (Collider collider in colliders)
            {
                if (collider == null || collider.isTrigger) continue;
                Bounds bounds = collider.bounds;
                Vector3 offset = bounds.center - root.position;
                radius = Mathf.Max(radius,
                    Mathf.Abs(Vector3.Dot(offset, direction))
                    + Mathf.Abs(direction.x) * bounds.extents.x + Mathf.Abs(direction.z) * bounds.extents.z);
            }
            return radius;
        }

        private static float GetProjectedBoundsRadius(Bounds localBounds, Vector3 scale,
            Quaternion rotation, Vector3 direction)
        {
            scale = Abs(scale);
            direction.y = 0f;
            direction.Normalize();
            Vector3 center = rotation * Vector3.Scale(localBounds.center, scale);
            Vector3 extents = Vector3.Scale(localBounds.extents, scale);
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;
            return Mathf.Abs(Vector3.Dot(center, direction))
                + Mathf.Abs(Vector3.Dot(right, direction)) * extents.x
                + Mathf.Abs(Vector3.Dot(up, direction)) * extents.y
                + Mathf.Abs(Vector3.Dot(forward, direction)) * extents.z;
        }

        private static Vector3 ResolveSideOutward(BuildingFootprintAuthority authority, Vector3 localPoint)
        {
            Vector3 fl = authority.FrontLeftCornerLocal;
            Vector3 fr = authority.FrontRightCornerLocal;
            Vector3 br = authority.BackRightCornerLocal;
            Vector3 bl = authority.BackLeftCornerLocal;
            float front = DistanceToSegmentXZ(localPoint, fl, fr);
            float right = DistanceToSegmentXZ(localPoint, fr, br);
            float back = DistanceToSegmentXZ(localPoint, br, bl);
            float left = DistanceToSegmentXZ(localPoint, bl, fl);
            Vector3 localNormal = front <= right && front <= back && front <= left
                ? authority.FrontOutwardLocalDirection
                : right <= back && right <= left
                    ? (fr - fl).normalized
                    : back <= left
                        ? -authority.FrontOutwardLocalDirection
                        : -(fr - fl).normalized;
            Vector3 worldNormal = authority.transform.TransformDirection(localNormal);
            worldNormal.y = 0f;
            return worldNormal.sqrMagnitude > 0.0001f ? worldNormal.normalized : authority.transform.forward;
        }

        private static float DistanceToSegmentXZ(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector2 p = new(point.x, point.z);
            Vector2 start = new(a.x, a.z);
            Vector2 end = new(b.x, b.z);
            Vector2 edge = end - start;
            float t = edge.sqrMagnitude > 0.000001f ? Mathf.Clamp01(Vector2.Dot(p - start, edge) / edge.sqrMagnitude) : 0f;
            return Vector2.Distance(p, start + edge * t);
        }

        private static bool TryGetPrefabLocalBounds(GameObject prefab, out Bounds bounds)
        {
            bounds = default;
            if (prefab == null) return false;
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                Bounds local = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new(
                        (corner & 1) == 0 ? local.min.x : local.max.x,
                        (corner & 2) == 0 ? local.min.y : local.max.y,
                        (corner & 4) == 0 ? local.min.z : local.max.z);
                    Vector3 rootLocal = prefab.transform.InverseTransformPoint(renderer.transform.TransformPoint(point));
                    if (!IsFinite(rootLocal)) continue;
                    if (!hasBounds) { bounds = new Bounds(rootLocal, Vector3.zero); hasBounds = true; }
                    else bounds.Encapsulate(rootLocal);
                }
            }
            return hasBounds && bounds.size.sqrMagnitude > 0.0001f;
        }

        private bool TryValidateVehicleFootprint(TownWorldController townWorld, TownPlot plot, PlacedBuilding building,
            Vector3 position, Quaternion rotation, GameObject prefab, Bounds localBounds, Transform candidateSource,
            out string reason)
        {
            reason = string.Empty;
            TownGrid grid = townWorld.Grid;
            Vector3 scale = Abs(prefab.transform.localScale);
            Bounds worldBounds = TransformBounds(localBounds, position, rotation, scale);
            Vector3 localCenter = Vector3.Scale(localBounds.center, scale);
            Vector3 halfExtents = Vector3.Scale(localBounds.extents, scale);
            Vector3 orientedCenter = position + rotation * localCenter;
            if (!TryValidateGroundFootprint(townWorld, plot, building, orientedCenter, rotation,
                    halfExtents, worldBounds, out reason)) return false;

            Vector3 physicsHalfExtents = Vector3.Max(halfExtents * 0.96f, Vector3.one * 0.05f);
            Collider[] overlaps = Physics.OverlapBox(orientedCenter, physicsHalfExtents, rotation,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            foreach (Collider overlap in overlaps)
            {
                if (overlap == null || overlap is TerrainCollider) continue;
                if (candidateSource != null && (overlap.transform == candidateSource || overlap.transform.IsChildOf(candidateSource))) continue;
                if (activeHorseView != null && (overlap.transform == activeHorseView.transform || overlap.transform.IsChildOf(activeHorseView.transform))) continue;
                if (activeWagonView != null && (overlap.transform == activeWagonView.transform || overlap.transform.IsChildOf(activeWagonView.transform))) continue;
                if (overlap.bounds.max.y <= worldBounds.min.y + 0.02f) continue;
                reason = $"physical clearance overlaps '{overlap.name}'.";
                return false;
            }
            return true;
        }

        private static bool TryValidateGroundFootprint(TownWorldController townWorld, TownPlot plot,
            PlacedBuilding building, Vector3 orientedCenter, Quaternion rotation, Vector3 halfExtents,
            Bounds footprint, out string reason)
        {
            reason = string.Empty;
            TownGrid grid = townWorld.Grid;
            GridCoord min = grid.WorldToCoord(footprint.min + new Vector3(0.001f, 0f, 0.001f));
            GridCoord max = grid.WorldToCoord(footprint.max - new Vector3(0.001f, 0f, 0.001f));
            float maxSlope = townWorld.Settings != null ? townWorld.Settings.maxBuildableSlopeDegrees : 8f;
            for (int z = min.z; z <= max.z; z++)
            for (int x = min.x; x <= max.x; x++)
            {
                GridCoord coord = new(x, z);
                Vector3 cellCenter = grid.CoordToWorldCenter(coord);
                if (!OrientedFootprintIntersectsCell(orientedCenter, rotation, halfExtents, cellCenter,
                        grid.CellSizeMeters * 0.5f)) continue;
                if (!grid.IsInBounds(coord) || !plot.bounds.Contains(coord))
                {
                    reason = "body footprint crosses the parcel boundary.";
                    return false;
                }
                bool inBounds = grid.IsInBounds(coord);
                TownCell cell = inBounds ? grid.GetCell(coord) : default;
                if (!FreightStagingCellSelector.IsClearVehicleGroundCell(
                        inBounds,
                        plot.bounds.Contains(coord),
                        cell.plotId,
                        plot.id,
                        cell.IsRoad,
                        cell.HasBuilding,
                        cell.blocked || (cell.occupancy & CellOccupancy.Blocked) != 0,
                        (cell.occupancy & CellOccupancy.Anchor) != 0,
                        cell.terrainZone != TerrainZone.Unknown && cell.terrainZone != TerrainZone.Blocked,
                        cell.slopeDegrees,
                        maxSlope,
                        out string cellRefusal))
                {
                    reason = $"body footprint {cellRefusal} at {coord}.";
                    return false;
                }
            }
            return true;
        }

        private static bool OrientedFootprintIntersectsCell(Vector3 center, Quaternion rotation,
            Vector3 halfExtents, Vector3 cellCenter, float cellHalfSize)
        {
            Vector3 right = rotation * Vector3.right;
            Vector3 forward = rotation * Vector3.forward;
            Vector3 delta = center - cellCenter;
            if (Mathf.Abs(delta.x) > Mathf.Abs(right.x) * halfExtents.x
                    + Mathf.Abs(forward.x) * halfExtents.z + cellHalfSize
                || Mathf.Abs(delta.z) > Mathf.Abs(right.z) * halfExtents.x
                    + Mathf.Abs(forward.z) * halfExtents.z + cellHalfSize)
                return false;
            if (Mathf.Abs(Vector3.Dot(delta, right)) > halfExtents.x
                    + cellHalfSize * (Mathf.Abs(right.x) + Mathf.Abs(right.z))
                || Mathf.Abs(Vector3.Dot(delta, forward)) > halfExtents.z
                    + cellHalfSize * (Mathf.Abs(forward.x) + Mathf.Abs(forward.z)))
                return false;
            return true;
        }

        private static bool TryValidateHitchFixtureCell(TownGrid grid, TownPlot plot, Vector3 position, out string reason)
        {
            GridCoord coord = grid.WorldToCoord(position);
            reason = string.Empty;
            if (!grid.IsInBounds(coord) || !plot.bounds.Contains(coord))
            {
                reason = "required hitch fixture would be outside the parcel.";
                return false;
            }
            TownCell cell = grid.GetCell(coord);
            if (cell.plotId != plot.id || cell.blocked || cell.IsRoad || cell.HasBuilding
                || (cell.occupancy & (CellOccupancy.Blocked | CellOccupancy.Anchor)) != 0)
            {
                reason = $"required hitch fixture has no clear parcel cell at {coord}.";
                return false;
            }
            return true;
        }

        private static bool TryValidateWagonApproachCells(TownGrid grid, TownPlot plot, Bounds wagonBounds,
            Vector3 prefabScale, IReadOnlyList<GridCoord> route, out string reason)
        {
            reason = string.Empty;
            if (route == null || route.Count == 0)
            {
                reason = "wagon approach has no physical route.";
                return false;
            }
            for (int i = 0; i < route.Count; i++)
            {
                Vector3 direction = i + 1 < route.Count
                    ? grid.CoordToWorldCenter(route[i + 1]) - grid.CoordToWorldCenter(route[i])
                    : i > 0 ? grid.CoordToWorldCenter(route[i]) - grid.CoordToWorldCenter(route[i - 1]) : Vector3.right;
                direction.y = 0f;
                Quaternion rotation = direction.sqrMagnitude > 0.0001f
                    ? Quaternion.FromToRotation(Vector3.right, direction.normalized)
                    : Quaternion.identity;
                Bounds bounds = TransformBounds(wagonBounds, grid.CoordToWorldCenter(route[i]), rotation, Abs(prefabScale));
                GridCoord min = grid.WorldToCoord(bounds.min + new Vector3(0.001f, 0f, 0.001f));
                GridCoord max = grid.WorldToCoord(bounds.max - new Vector3(0.001f, 0f, 0.001f));
                for (int z = min.z; z <= max.z; z++)
                for (int x = min.x; x <= max.x; x++)
                {
                    GridCoord coord = new(x, z);
                    if (!grid.IsInBounds(coord))
                    {
                        reason = $"wagon cannot turn through route cell {route[i]}; clearance reaches outside the town grid.";
                        return false;
                    }
                    TownCell cell = grid.GetCell(coord);
                    if (cell.IsRoad) continue;
                    if (!plot.bounds.Contains(coord) || cell.plotId != plot.id || cell.blocked || cell.HasBuilding)
                    {
                        reason = $"wagon turning clearance is blocked beside route cell {route[i]} at {coord}.";
                        return false;
                    }
                }
            }
            return true;
        }

        private static Bounds TransformBounds(Bounds localBounds, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Vector3 center = Vector3.Scale(localBounds.center, scale);
            Vector3 extents = Vector3.Scale(localBounds.extents, scale);
            Vector3 worldCenter = position + rotation * center;
            Vector3 axisX = rotation * Vector3.right * extents.x;
            Vector3 axisY = rotation * Vector3.up * extents.y;
            Vector3 axisZ = rotation * Vector3.forward * extents.z;
            Vector3 worldExtents = new(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(worldCenter, worldExtents * 2f);
        }

        private static Vector3 Abs(Vector3 value) => new(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

        private static bool TryResolveAdjacentRoadCell(TownGrid grid, GridCoord anchor, out GridCoord roadCell)
        {
            roadCell = default;
            if (grid == null || !grid.IsInBounds(anchor)) return false;
            if (grid.GetCell(anchor).IsRoad) { roadCell = anchor; return true; }
            GridCoord[] offsets = { new(0, 1), new(1, 0), new(0, -1), new(-1, 0) };
            foreach (GridCoord offset in offsets)
            {
                GridCoord candidate = anchor + offset;
                if (!grid.IsInBounds(candidate) || !grid.GetCell(candidate).IsRoad) continue;
                roadCell = candidate;
                return true;
            }
            return false;
        }

        private void OnDrawGizmos()
        {
#if UNITY_EDITOR
            if (UnityEditor.SceneView.currentDrawingSceneView == null || hitchCandidateDiagnostics == null) return;
            foreach (HitchCandidateDiagnostic candidate in hitchCandidateDiagnostics)
            {
                Gizmos.color = candidate.Feasible ? new Color(0.15f, 0.9f, 0.25f, 0.95f) : new Color(0.95f, 0.15f, 0.12f, 0.95f);
                Gizmos.DrawSphere(candidate.Position + Vector3.up * 0.15f, 0.18f);
                UnityEditor.Handles.Label(candidate.Position + Vector3.up * 0.45f,
                    candidate.Label + (candidate.Feasible ? " — valid" : " — rejected: " + candidate.Reason));
            }
#endif
        }

        private static bool IsFinite(Vector3 value)
            => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool TrySampleGround(TownWorldController townWorld, Vector3 query, out Vector3 groundPosition)
        {
            groundPosition = default;
            if (townWorld == null || !townWorld.TrySampleWorldSurface(query, out WorldSurfaceSample sample)
                || !sample.isInsideWorld || sample.isWater)
                return false;
            groundPosition = sample.position;
            return true;
        }

        private static bool TryFindUniqueHorseView(string animalId, out HorseWorldView found)
        {
            found = null;
            foreach (HorseWorldView candidate in UnityEngine.Object.FindObjectsByType<HorseWorldView>(FindObjectsInactive.Include))
            {
                if (candidate == null || candidate.AnimalId != animalId) continue;
                if (found != null)
                {
                    Debug.LogError($"[OpeningTransportPresentation] Duplicate horse visuals already bind animal '{animalId}'.");
                    return false;
                }
                found = candidate;
            }
            return true;
        }

        private static bool TryFindUniqueWagonView(string assetId, out WagonWorldView found)
        {
            found = null;
            foreach (WagonWorldView candidate in UnityEngine.Object.FindObjectsByType<WagonWorldView>(FindObjectsInactive.Include))
            {
                if (candidate == null || candidate.AssetId != assetId) continue;
                if (found != null)
                {
                    Debug.LogError($"[OpeningTransportPresentation] Duplicate wagon visuals already bind asset '{assetId}'.");
                    return false;
                }
                found = candidate;
            }
            return true;
        }

        private static string FormatPosition(Vector3 position)
        {
            return $"({position.x:0.00}, {position.y:0.00}, {position.z:0.00})";
        }
    }
}
