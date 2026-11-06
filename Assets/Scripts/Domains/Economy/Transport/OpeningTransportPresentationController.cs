using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Core;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Pathing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World;
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
        // Overnight 2026-11-04: the neutral root a hitched pair shares.
        // The horse used to be parented under the wagon's hitch anchor,
        // which compounded the wagon's scale onto the horse and snapped
        // the horse onto the anchor point. Hitched horse and wagon are
        // siblings here instead, and neither is ever the other's child.
        private Transform transportGroup;

        public void Present(
            SimulationSystemsHub systems,
            IReadOnlyList<BusinessInstanceState> businesses,
            TownWorldController townWorld,
            PopulationState population)
        {
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
                    out Vector3 accessPoint, out Quaternion facing, out string stagingLocation, out string positionRefusal))
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
            wagonView.transform.SetPositionAndRotation(wagonPosition, facing);
            wagonView.gameObject.name = $"Opening Freight Wagon {wagon.AssetId}";
            wagonView.gameObject.SetActive(true);
            wagonView.GetComponent<TransportAnimatorState>()?.SetState(TransportVisualState.Idle);

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
            // Slice S6: postQuery comes from TransportPairPlacement —
            // same 1.1 m magnitude as before, now on the pair-lateral
            // axis instead of the travel axis.
            if (TrySampleGround(townWorld, postQuery, out Vector3 postPosition))
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
                if (hitchingPost != null && horse.TransportState == HorseTransportState.SecuredAtHitchingPost)
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
        public bool BeginCoupledDeparture(LogisticsShipmentState shipment)
        {
            if (shipment?.RoutePlan?.VisiblePath == null || shipment.RoutePlan.VisiblePath.Count < 2)
            {
                Debug.LogWarning($"[CoupledTransport] Refused to start the outbound leg for shipment '{shipment?.ShipmentId}': no usable visible path.", this);
                return false;
            }
            CoupledTransportMovementController controller = ResolveMovementController();
            if (controller == null) return false;
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
            // Slice R1 (Objective 22): board Ethan onto the DriverSeat
            // before the leg starts. A refused board degrades to a
            // riderless leg; a missing seat refuses the leg.
            if (!TryBoardRiderForLeg(director, ethanId, legLabel)) return false;
            return controller.StartLeg(shipment.RoutePlan.VisiblePath, cellSize, origin,
                wagonSpeed, TransportPairPlacement.PairSeparationMetres, turnSpeed,
                director, ethanId, legLabel, hideOnArrival: true);
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
            if (activeHorseView != null) activeHorseView.gameObject.SetActive(true);
            if (activeWagonView != null) activeWagonView.gameObject.SetActive(true);
            if (director != null && ethanId >= 0) director.TryShowPersonAtCurrentLocation(ethanId);
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
        /// Slice R1 (Objective 22): board the driver onto the wagon's
        /// DriverSeat for a leg. Returns false ONLY when the wagon has
        /// no DriverSeat — that refuses the leg start (same posture as
        /// a missing controller). A refused board degrades to a
        /// riderless leg: the refusal is logged loudly and the leg
        /// still starts (a presentation failure must not strand a
        /// shipment). When no director/driver resolved, there is no
        /// rider to board and the leg starts as before
        /// (ResolveMovementInputs already warned).
        /// </summary>
        private bool TryBoardRiderForLeg(PopulationPathingDirector director, int ethanPersonId, string legLabel)
        {
            Transform seat = activeWagonView != null ? activeWagonView.DriverSeat : null;
            if (seat == null)
            {
                Debug.LogWarning($"[CoupledTransport] Refused to start {legLabel}: the wagon has no DriverSeat.", this);
                return false;
            }
            if (director == null || ethanPersonId < 0) return true;
            if (!director.TryBoardRider(ethanPersonId, seat, legLabel))
            {
                Debug.LogWarning($"[CoupledTransport] rider boarding refused for {legLabel}; team moves without a rider.", this);
            }
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

            director = FindAnyObjectByType<PopulationPathingDirector>();
            ethanPersonId = ParsePersonId(shipment?.physicalDriverPersonId);
            if (director == null || ethanPersonId < 0)
            {
                Debug.LogWarning("[CoupledTransport] No director or driver person resolved for the rider; the pair moves without Ethan.", this);
                director = null;
                ethanPersonId = -1;
            }
            return true;
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
            foreach (Transform candidate in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
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

        private static bool TryResolveBusinessRoadAccess(
            TownWorldController townWorld,
            BusinessInstanceState business,
            PersonState operatorPerson,
            out Vector3 accessPoint,
            out Quaternion facing,
            out string stagingLocation,
            out string refusal)
        {
            accessPoint = default;
            facing = Quaternion.identity;
            stagingLocation = string.Empty;
            refusal = string.Empty;

            PlacedBuilding building = null;
            if (business.AssignedBuildingId >= 0
                && townWorld.TryGetBuildingById(business.AssignedBuildingId, out PlacedBuilding businessBuilding)
                && businessBuilding != null)
            {
                building = businessBuilding;
                stagingLocation = $"Freight Company building {business.AssignedBuildingId}";
            }

            // The opening freight business has no dedicated premises in the
            // expanded fixture. Keep its ordinary transport at its operator's
            // authoritative home frontage until a real yard is assigned.
            if (building == null && operatorPerson != null && operatorPerson.homeBuildingId >= 0
                && townWorld.TryGetBuildingById(operatorPerson.homeBuildingId, out PlacedBuilding homeBuilding)
                && homeBuilding != null)
            {
                building = homeBuilding;
                stagingLocation = $"operator home frontage, building {operatorPerson.homeBuildingId}";
            }

            if (building == null)
            {
                refusal = $"Freight Company '{business.InstanceId}' has no placed building and its operator has no resolvable home building.";
                return false;
            }
            if (!building.TryGetRoadAccessCoord(out GridCoord roadAccess))
            {
                refusal = $"staging building {building.id} has no road-access anchor.";
                return false;
            }

            float yaw = ((int)building.frontageDirection) * 90f;
            facing = Quaternion.Euler(0f, yaw, 0f);
            Vector3 roadCellCenter = townWorld.GetWorldCenterForRect(new GridRect(roadAccess.x, roadAccess.z, 1, 1));
            if (!TrySampleGround(townWorld, roadCellCenter, out accessPoint))
            {
                refusal = $"road-access cell {roadAccess} for staging building {building.id} does not resolve to a dry in-world surface.";
                return false;
            }
            return true;
        }

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
            foreach (HorseWorldView candidate in Object.FindObjectsByType<HorseWorldView>(FindObjectsInactive.Include))
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
            foreach (WagonWorldView candidate in Object.FindObjectsByType<WagonWorldView>(FindObjectsInactive.Include))
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
