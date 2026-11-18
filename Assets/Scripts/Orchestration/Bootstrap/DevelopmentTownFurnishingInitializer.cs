using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Orchestration
{
    /// <summary>
    /// Applies the finite household furniture endowments authored on the
    /// development-town configuration. The furnishing register is ownership;
    /// this class only creates its scene presentation and physical binding.
    /// </summary>
    public static class DevelopmentTownFurnishingInitializer
    {
        public sealed class Result
        {
            public int HouseholdsProcessed;
            public int AssetsInstalled;
            public int PresentationsRebuilt;
            public readonly List<string> Diagnostics = new();
        }

        /// <summary>Creates configured, finite, household-owned New Game furnishings once.</summary>
        public static Result SeedNewGame(
            DevelopmentTownConfiguration configuration,
            PopulationManager population,
            TownWorldController townWorld,
            SimulationSystemsHub systemsHub)
        {
            var result = new Result();
            if (configuration == null || population?.State?.households == null || townWorld == null || systemsHub == null)
            {
                result.Diagnostics.Add("Household furnishing seed skipped: configuration, population, town, or systems hub is unavailable.");
                return result;
            }

            foreach (DevelopmentTownConfiguration.HouseholdDefinition definition in configuration.Households)
            {
                if (definition == null || definition.openingFurnishingVariants == null || definition.openingFurnishingVariants.Length == 0)
                    continue;
                DevelopmentTownConfiguration.HouseholdFurnishingVariant variant =
                    configuration.SelectOpeningFurnishingVariant(definition, ResolveScenarioSeed(configuration, townWorld));
                if (variant == null)
                {
                    result.Diagnostics.Add($"Household '{definition.key}' has furnishing variants but none has a positive weight.");
                    continue;
                }

                HouseholdState household = population.State.households.FirstOrDefault(value =>
                    value != null && value.id == definition.runtimeHouseholdId);
                if (household == null)
                {
                    result.Diagnostics.Add($"Household furnishing variant '{variant.variantId}' names missing runtime household {definition.runtimeHouseholdId} ('{definition.key}').");
                    continue;
                }
                PlacedBuilding home = townWorld.Buildings.FirstOrDefault(value => value != null && value.id == household.homeBuildingId);
                if (home?.runtimeFootprintAuthority == null)
                {
                    result.Diagnostics.Add($"Household {household.id} ('{definition.key}') home building {household.homeBuildingId} has no live BuildingFootprintAuthority; furnishings were not created.");
                    continue;
                }

                result.HouseholdsProcessed++;
                HouseholdFurnishingRegister register = systemsHub.FurnishingRegisterFor(household.id);
                foreach (DevelopmentTownConfiguration.HouseholdFurnishingItem item in variant.items ?? Array.Empty<DevelopmentTownConfiguration.HouseholdFurnishingItem>())
                {
                    string error = EnsureInstalled(configuration, household, home, register, item, variant.variantId, out bool installed);
                    if (error != null) result.Diagnostics.Add(error);
                    if (installed) result.AssetsInstalled++;
                }

                RebuildDerivedRegistries(household, systemsHub);
                ValidateFunctionalActivityRoutes(home.runtimeFootprintAuthority, register, result.Diagnostics);
            }

            return result;
        }

        /// <summary>Recreates runtime visual objects from saved ownership and placement bindings after Continue.</summary>
        public static Result RestorePresentations(
            DevelopmentTownConfiguration configuration,
            PopulationManager population,
            TownWorldController townWorld,
            SimulationSystemsHub systemsHub)
        {
            var result = new Result();
            if (configuration == null || population?.State?.households == null || townWorld == null || systemsHub == null)
                return result;

            foreach (HouseholdState household in population.State.households)
            {
                if (household == null) continue;
                HouseholdFurnishingRegister register = systemsHub.FurnishingRegisterFor(household.id);
                PlacedBuilding home = townWorld.Buildings.FirstOrDefault(value => value != null && value.id == household.homeBuildingId);
                if (home?.runtimeFootprintAuthority == null || register?.Bindings == null) continue;
                result.HouseholdsProcessed++;
                foreach (FurnishingPlacementBinding binding in register.Bindings.ToArray())
                {
                    if (binding == null) continue;
                    EquipmentAsset asset = register.FindAsset(binding.AssetId);
                    if (asset == null || asset.OwnerKind != HouseholdFurnishingRegister.OwnerKindHousehold
                        || asset.OwnerId != HouseholdFurnishingRegister.OwnerIdForHousehold(household.id)
                        || binding.BuildingInstanceId != household.homeBuildingId)
                    {
                        result.Diagnostics.Add($"Household {household.id} furnishing binding '{binding.AssetId}' does not match its owner or current home; no presentation was created.");
                        continue;
                    }
                    DevelopmentTownConfiguration.HouseholdFurnishingItem item = FindItem(configuration, binding.AssetId);
                    if (item == null)
                    {
                        // Purchased/manufactured furnishings may be added by
                        // separate physical-delivery wiring. Do not guess a
                        // prefab from a visual name or silently create one.
                        continue;
                    }
                    if (HasPresentation(home.runtimeFootprintAuthority, binding.AssetId)) continue;
                    string error = CreatePresentation(home.runtimeFootprintAuthority, binding.LocalPosition,
                        binding.LocalRotation, item, binding.AssetId, binding.DefinitionId,
                        binding.CapabilityReferenceId, out _);
                    if (error != null) result.Diagnostics.Add(error);
                    else result.PresentationsRebuilt++;
                }
                RebuildDerivedRegistries(household, systemsHub);
                ValidateFunctionalActivityRoutes(home.runtimeFootprintAuthority, register, result.Diagnostics);
            }
            return result;
        }

        private static int ResolveScenarioSeed(DevelopmentTownConfiguration configuration, TownWorldController townWorld)
        {
            int worldSeed = townWorld.Settings != null ? townWorld.Settings.seed : 0;
            return unchecked(worldSeed * 397 ^ configuration.SeedOffset);
        }

        private static string EnsureInstalled(
            DevelopmentTownConfiguration configuration,
            HouseholdState household,
            PlacedBuilding home,
            HouseholdFurnishingRegister register,
            DevelopmentTownConfiguration.HouseholdFurnishingItem item,
            string variantId,
            out bool installed)
        {
            installed = false;
            if (item == null) return $"Household {household.id} furnishing variant '{variantId}' contains a null item.";
            if (register.FindAsset(item.assetId) is EquipmentAsset existing)
            {
                if (existing.OwnerKind != HouseholdFurnishingRegister.OwnerKindHousehold
                    || existing.OwnerId != HouseholdFurnishingRegister.OwnerIdForHousehold(household.id)
                    || !string.Equals(existing.Kind, item.kind, StringComparison.Ordinal))
                    return $"Opening furnishing identity collision for {item.assetId}: existing asset owner/kind does not match household {household.id} and '{item.kind}'.";
                if (register.LiveBindingFor(item.assetId) != null)
                {
                    if (!HasPresentation(home.runtimeFootprintAuthority, item.assetId))
                    {
                        FurnishingPlacementBinding binding = register.LiveBindingFor(item.assetId);
                        string restoreError = CreatePresentation(home.runtimeFootprintAuthority, binding.LocalPosition,
                            binding.LocalRotation, item, item.assetId, binding.DefinitionId,
                            binding.CapabilityReferenceId, out _);
                        if (restoreError != null) return restoreError;
                    }
                    return null;
                }
                // A prior partial New Game attempt may have registered the
                // asset before placement. Keep identity and title; finish its
                // legitimate installation using the same stable item record.
            }

            BuildingFootprintAuthority authority = home.runtimeFootprintAuthority;
            if (item.prefab == null)
                return $"Opening furnishing {item.assetId} has no resolved prefab in the development-town configuration.";
            if (item.prefab.GetComponentsInChildren<Renderer>(true).Length == 0)
                return $"Opening furnishing {item.assetId} prefab '{item.prefab.name}' has no rendered geometry.";
            Vector3 floorPoint = item.localPosition;
            floorPoint.y = FloorHeight(authority, item.floorIndex, item.localPosition.y);
            if (!authority.TryEvaluateInteriorLocalPoint(floorPoint, item.floorIndex, null, out string blocked))
                return $"Opening furnishing {item.assetId} floor position is not walkable in building {home.id}: {blocked}";
            if (item.registerRuntimeActivityLocation
                && !authority.ActivityLocations.Any(location => location != null && location.id == item.activityLocationId)
                && !authority.TryEvaluateInteriorLocalPoint(item.activityPositionLocal, item.floorIndex, null, out blocked))
                return $"Opening furnishing {item.assetId} interaction approach is not walkable in building {home.id}: {blocked}";
            if (!item.registerRuntimeActivityLocation && !string.IsNullOrWhiteSpace(item.activityLocationId)
                && !authority.TryGetActivityLocation(item.activityLocationId, home.id, out _, out string activityError))
                return $"Opening furnishing {item.assetId} references unavailable activity location '{item.activityLocationId}' in building {home.id}: {activityError}";

            EquipmentAsset asset = register.FindAsset(item.assetId);
            bool addedAsset = false;
            if (asset == null)
            {
                asset = new EquipmentAsset
                {
                    AssetId = item.assetId.Trim(),
                    Kind = item.kind.Trim(),
                    DisplayName = item.displayName.Trim(),
                    Condition01 = 1f,
                    OwnerKind = HouseholdFurnishingRegister.OwnerKindHousehold,
                    OwnerId = HouseholdFurnishingRegister.OwnerIdForHousehold(household.id),
                    LocationId = HouseholdFurnishingRegister.HouseholdCustodyLocationId(household.id),
                    MadeDayIndex = -1,
                };
                string registerError = register.TryRegisterAsset(asset);
                if (registerError != null) return registerError;
                addedAsset = true;
            }

            string installId = ConfigurationId(configuration, household.id, variantId, item.assetId);
            string presentationError = CreatePresentation(authority, item.localPosition,
                Quaternion.Euler(item.localEulerAngles), item, item.assetId, item.kind,
                item.activityLocationId, out BuildingEquipmentPlacement placement);
            if (presentationError != null)
            {
                if (addedAsset) register.TryDeregisterAsset(item.assetId);
                return presentationError;
            }

            string error = register.TryInstall(item.assetId, placement, authority, home.id, item.floorIndex,
                item.capability, item.activityLocationId, FurnishingAcquisitionSource.ScenarioEndowment,
                installId, out _);
            if (error != null)
            {
                if (item.registerRuntimeActivityLocation)
                    authority.UnregisterRuntimeActivityLocation(item.activityLocationId);
                UnityEngine.Object.Destroy(placement.gameObject);
                if (addedAsset) register.TryDeregisterAsset(item.assetId);
                return error;
            }

            HouseholdFurnishingRuntimePresentation marker = placement.GetComponent<HouseholdFurnishingRuntimePresentation>();
            if (marker == null) marker = placement.gameObject.AddComponent<HouseholdFurnishingRuntimePresentation>();
            marker.Configure(item.assetId, authority,
                item.registerRuntimeActivityLocation ? item.activityLocationId : string.Empty,
                item.activityPositionLocal, item.activityForwardLocal);
            installed = true;
            return null;
        }

        private static string CreatePresentation(
            BuildingFootprintAuthority authority,
            Vector3 localPosition,
            Quaternion localRotation,
            DevelopmentTownConfiguration.HouseholdFurnishingItem item,
            string assetId,
            string definitionId,
            string activityLocationId,
            out BuildingEquipmentPlacement placement)
        {
            placement = null;
            if (authority == null || item?.prefab == null) return $"Furnishing {assetId} cannot be presented because its building or prefab is missing.";
            GameObject instance = UnityEngine.Object.Instantiate(item.prefab, authority.transform, false);
            if (instance == null) return $"Furnishing {assetId} prefab instantiation returned no object.";
            instance.name = $"{item.displayName} [{assetId}]";
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation;
            instance.transform.localScale = item.localScale;

            placement = instance.GetComponent<BuildingEquipmentPlacement>();
            if (placement == null) placement = instance.AddComponent<BuildingEquipmentPlacement>();
            placement.Configure(assetId, definitionId, item.displayName, item.floorIndex);
            placement.AssociateWithBuilding(authority);
            if (item.registerRuntimeActivityLocation)
            {
                string runtimeError = authority.RegisterRuntimeActivityLocation(activityLocationId,
                    item.displayName + " approach", item.activityType, item.activityPositionLocal,
                    item.activityForwardLocal, item.floorIndex);
                if (runtimeError != null && !IsSameRuntimeLocation(authority, activityLocationId, item.activityPositionLocal))
                {
                    UnityEngine.Object.Destroy(instance);
                    placement = null;
                    return $"Furnishing {assetId} interaction point failed: {runtimeError}";
                }
            }
            return null;
        }

        private static bool IsSameRuntimeLocation(BuildingFootprintAuthority authority, string id, Vector3 position)
        {
            return authority != null && authority.TryGetActivityLocation(id, 0, out ResolvedBuildingActivityLocation location, out _)
                && (location.WorldPosition - authority.transform.TransformPoint(position)).sqrMagnitude < 0.0001f;
        }

        private static bool HasPresentation(BuildingFootprintAuthority authority, string assetId)
        {
            if (authority == null || string.IsNullOrWhiteSpace(assetId)) return false;
            foreach (HouseholdFurnishingRuntimePresentation marker in authority.GetComponentsInChildren<HouseholdFurnishingRuntimePresentation>(true))
                if (marker != null && string.Equals(marker.AssetId, assetId, StringComparison.Ordinal)) return true;
            return false;
        }

        private static DevelopmentTownConfiguration.HouseholdFurnishingItem FindItem(
            DevelopmentTownConfiguration configuration, string assetId)
        {
            foreach (DevelopmentTownConfiguration.HouseholdDefinition household in configuration.Households)
                foreach (DevelopmentTownConfiguration.HouseholdFurnishingVariant variant in household?.openingFurnishingVariants ?? Array.Empty<DevelopmentTownConfiguration.HouseholdFurnishingVariant>())
                    foreach (DevelopmentTownConfiguration.HouseholdFurnishingItem item in variant?.items ?? Array.Empty<DevelopmentTownConfiguration.HouseholdFurnishingItem>())
                        if (item != null && string.Equals(item.assetId, assetId, StringComparison.Ordinal)) return item;
            return null;
        }

        private static void RebuildDerivedRegistries(HouseholdState household, SimulationSystemsHub systemsHub)
        {
            systemsHub.SleepingPlaceRegistryFor(household.id).RebuildFromRegister();
            systemsHub.HouseholdStoveRegistryFor(household.id).RebuildFromRegister();
            systemsHub.DiningPlaceRegistryFor(household.id).HomeBuildingInstanceId = household.homeBuildingId;
        }

        private static void ValidateFunctionalActivityRoutes(
            BuildingFootprintAuthority authority,
            HouseholdFurnishingRegister register,
            List<string> diagnostics)
        {
            if (authority == null || register?.Bindings == null) return;
            foreach (FurnishingPlacementBinding binding in register.Bindings)
            {
                if (binding == null || binding.BuildingInstanceId < 0
                    || (binding.CapabilityKind != FurnishingCapabilityKind.SleepingPlace
                        && binding.CapabilityKind != FurnishingCapabilityKind.StoveWorkstation
                        && binding.CapabilityKind != FurnishingCapabilityKind.DiningSeat)) continue;
                if (!authority.TryGetActivityLocation(binding.CapabilityReferenceId, binding.BuildingInstanceId,
                        out _, out string reason))
                {
                    diagnostics.Add($"Installed {binding.CapabilityKind} asset {binding.AssetId} has no usable activity location: {reason}");
                    continue;
                }
                if (!authority.TryBuildInteriorActivityRoute("main-entrance", binding.CapabilityReferenceId,
                        out _, out reason))
                    diagnostics.Add($"Installed {binding.CapabilityKind} asset {binding.AssetId} cannot be reached from the main entrance: {reason}");
            }
        }

        private static float FloorHeight(BuildingFootprintAuthority authority, int floorIndex, float fallback)
        {
            if (authority?.InteriorFloors == null) return fallback;
            foreach (InteriorFloorAuthoring floor in authority.InteriorFloors)
                if (floor != null && floor.floorIndex == floorIndex) return floor.floorHeightLocal;
            return fallback;
        }

        private static string ConfigurationId(DevelopmentTownConfiguration configuration, int householdId,
            string variantId, string assetId) =>
            $"scenario-endowment:{configuration.ConfigurationId}:household-{householdId}:{variantId}:{assetId}";
    }

    /// <summary>Runtime scene presentation for one already-owned, installed furnishing asset.</summary>
    [DisallowMultipleComponent]
    public sealed class HouseholdFurnishingRuntimePresentation : MonoBehaviour
    {
        [SerializeField] private string assetId = string.Empty;
        private BuildingFootprintAuthority activityAuthority;
        private string activityLocationId = string.Empty;
        private Vector3 activityOffsetFromFurniture;
        private Vector3 activityForwardInFurnitureSpace = Vector3.forward;
        private Matrix4x4 lastFurnitureLocalMatrix;
        private bool hasFurnitureLocalMatrix;
        private string lastMoveFailure = string.Empty;

        public string AssetId => assetId;

        public void Configure(string stableAssetId, BuildingFootprintAuthority authority,
            string runtimeActivityLocationId, Vector3 activityPositionLocal, Vector3 activityForwardLocal)
        {
            assetId = stableAssetId ?? string.Empty;
            activityAuthority = authority;
            activityLocationId = runtimeActivityLocationId ?? string.Empty;
            activityForwardInFurnitureSpace = Quaternion.Inverse(transform.localRotation) * activityForwardLocal;
            activityOffsetFromFurniture = Quaternion.Inverse(transform.localRotation)
                * (activityPositionLocal - transform.localPosition);
            lastFurnitureLocalMatrix = transform.localToWorldMatrix;
            hasFurnitureLocalMatrix = true;
        }

        private void Update()
        {
            if (activityAuthority == null || string.IsNullOrWhiteSpace(activityLocationId)) return;
            Matrix4x4 current = transform.localToWorldMatrix;
            if (hasFurnitureLocalMatrix && current == lastFurnitureLocalMatrix) return;
            lastFurnitureLocalMatrix = current;
            hasFurnitureLocalMatrix = true;
            Vector3 position = transform.localPosition + transform.localRotation * activityOffsetFromFurniture;
            Vector3 forward = transform.localRotation * activityForwardInFurnitureSpace;
            string error = activityAuthority.UpdateRuntimeActivityLocation(activityLocationId, position, forward);
            if (!string.IsNullOrWhiteSpace(error) && error != lastMoveFailure)
            {
                lastMoveFailure = error;
                Debug.LogWarning("[HouseholdFurnishing] " + error, this);
            }
        }

        private void OnDestroy()
        {
            if (activityAuthority != null && !string.IsNullOrWhiteSpace(activityLocationId))
                activityAuthority.UnregisterRuntimeActivityLocation(activityLocationId);
        }
    }
}
