using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Core;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
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

            Vector3 lateral = facing * Vector3.right;
            if (!TrySampleGround(townWorld, accessPoint - lateral * 1.8f, out Vector3 horsePosition)
                || !TrySampleGround(townWorld, horsePosition + facing * Vector3.forward * 2.8f, out Vector3 wagonPosition))
            {
                Debug.LogError($"[OpeningTransportPresentation] Refused to place opening transport: the horse staging point or adjacent wagon position near building {freightBusiness.AssignedBuildingId} has no valid dry in-world surface.", this);
                return;
            }

            EnsurePhysicalContainers(townWorld.transform);

            if (!TryFindUniqueHorseView(horse.AnimalId.ToString(), out HorseWorldView horseView)) return;
            if (horseView == null)
            {
                GameObject horseRoot = Instantiate(assets.HorsePrefab, horsePosition, facing, horseContainer);
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
            horseView.transform.SetPositionAndRotation(horsePosition, facing);
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
            Vector3 postQuery = horsePosition - lateral * 1.1f;
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
                    activeHorseView.transform.position = hitchingPost.transform.position + Vector3.right * 0.35f;
                activeHorseView.gameObject.GetComponent<TransportAnimatorState>()?.SetState(TransportVisualState.Idle);
            }
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
