using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.GenericBusiness;
using LandLedgers.Economy.Storage;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Orchestration
{
    /// <summary>Small, explicit transform calibrations for installed furnishing presentations.</summary>
    public static class FurnishingPresentationCalibration
    {
        public static Vector3 ApplyScale(Vector3 prefabAuthoredScale, Vector3 multiplier) =>
            Vector3.Scale(prefabAuthoredScale, multiplier);

        public static Vector3 AlignToFloor(Vector3 authoredPosition, float floorHeightLocal, bool alignRootToFloor)
        {
            if (alignRootToFloor) authoredPosition.y = floorHeightLocal;
            return authoredPosition;
        }

        /// <summary>
        /// Aligns the lowest rendered point of a placed prop with its building-local
        /// floor plane. Prefab pivots vary; using the root pivot as the floor point
        /// can leave otherwise valid furnishings partly below the floor.
        /// </summary>
        public static bool AlignRenderedBottomToFloor(Transform instance, float floorHeightLocal)
        {
            if (instance == null || instance.parent == null || float.IsNaN(floorHeightLocal)
                || float.IsInfinity(floorHeightLocal)) return false;

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return false;

            Transform floorSpace = instance.parent;
            float minimumLocalY = float.PositiveInfinity;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled) continue;
                Bounds bounds = renderer.localBounds;
                Vector3 min = bounds.min;
                Vector3 max = bounds.max;
                for (int x = 0; x < 2; x++)
                    for (int y = 0; y < 2; y++)
                        for (int z = 0; z < 2; z++)
                        {
                            Vector3 rendererLocalCorner = new(x == 0 ? min.x : max.x,
                                y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
                            Vector3 worldCorner = renderer.transform.TransformPoint(rendererLocalCorner);
                            minimumLocalY = Mathf.Min(minimumLocalY, floorSpace.InverseTransformPoint(worldCorner).y);
                        }
            }

            if (float.IsInfinity(minimumLocalY)) return false;
            Vector3 localPosition = instance.localPosition;
            localPosition.y += floorHeightLocal - minimumLocalY;
            instance.localPosition = localPosition;
            return true;
        }

        public static Quaternion FaceDirection(Vector3 prefabForwardLocal, Vector3 desiredDirectionLocal, Quaternion fallback)
        {
            prefabForwardLocal.y = 0f;
            desiredDirectionLocal.y = 0f;
            if (prefabForwardLocal.sqrMagnitude < 0.0001f || desiredDirectionLocal.sqrMagnitude < 0.0001f)
                return fallback;
            float angle = Vector3.SignedAngle(prefabForwardLocal.normalized, desiredDirectionLocal.normalized, Vector3.up);
            return Quaternion.AngleAxis(angle, Vector3.up);
        }
    }

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
            // Lane 9f (F R-3): bindings skipped because no configured
            // dev-town item exists for them (purchased/manufactured pieces)
            // — counted, not silently absent, so a piece that never
            // re-appears after Continue is visible and countable.
            public int PresentationsSkipped;
            public readonly List<string> SkippedBindingIds = new();
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
                    string error = EnsureInstalled(configuration, household, home, register, item, variant.items, variant.variantId, out bool installed);
                    if (error != null) result.Diagnostics.Add(error);
                    if (installed) result.AssetsInstalled++;
                }

                RebuildDerivedRegistries(household, systemsHub);
                ValidateFunctionalActivityRoutes(home.runtimeFootprintAuthority, register, result.Diagnostics);
                // Lane 9c (B-7): per-home startup access validation —
                // stove, cleaning, firewood and dining chair, reachable
                // from main-entrance where interior. Gaps are warnings,
                // not diagnostics-list entries, so a home with no stove,
                // no cleaning point, no firewood pile or no dining chair
                // surfaces at startup (B-01..B-04) instead of as silent
                // task failures.
                ValidateHomeAccess(home.runtimeFootprintAuthority, home.id, household.id, definition.key);
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
                        // Purchased/manufactured furnishings have no configured
                        // dev-town item, so no prefab can be resolved for them
                        // here. Do not guess a prefab from a visual name or
                        // silently create one — the skip is counted and
                        // diagnosed (Lane 9f F R-3) so a purchased piece that
                        // never re-appears after Continue is visible instead
                        // of silently absent. A real presentation path for
                        // purchased pieces needs a kind-to-prefab mapping
                        // that does not exist anywhere in source; inventing
                        // one here would furnish the wrong object.
                        result.PresentationsSkipped++;
                        result.SkippedBindingIds.Add(binding.AssetId);
                        result.Diagnostics.Add($"Household {household.id} furnishing binding '{binding.AssetId}' ({asset.Kind}) in building {binding.BuildingInstanceId} has no configured dev-town item — no presentation was recreated after Continue; purchased/manufactured pieces need a kind-to-prefab mapping that does not exist yet.");
                        continue;
                    }
                    HouseholdFurnishingRuntimePresentation existingPresentation =
                        FindPresentation(home.runtimeFootprintAuthority, binding.AssetId);
                    if (existingPresentation != null)
                    {
                        string rebindError = RebindPresentationActivityLocation(
                            home.runtimeFootprintAuthority, item, existingPresentation);
                        if (rebindError != null) result.Diagnostics.Add(rebindError);
                        continue;
                    }
                    string error = CreatePresentation(home.runtimeFootprintAuthority, binding.LocalPosition,
                        binding.LocalRotation, item, binding.AssetId, binding.DefinitionId,
                        binding.CapabilityReferenceId, false, out BuildingEquipmentPlacement placement);
                    if (error == null)
                        error = register.TryRebindPlacementForRestoration(binding.AssetId, placement,
                            home.runtimeFootprintAuthority, household.homeBuildingId);
                    if (error != null)
                    {
                        if (item.registerRuntimeActivityLocation)
                            home.runtimeFootprintAuthority.UnregisterRuntimeActivityLocation(item.activityLocationId);
                        if (placement != null) UnityEngine.Object.Destroy(placement.gameObject);
                        result.Diagnostics.Add(error);
                    }
                    else
                    {
                        if (item.registerRuntimeActivityLocation && placement != null)
                        {
                            HouseholdFurnishingRuntimePresentation marker = placement.GetComponent<HouseholdFurnishingRuntimePresentation>();
                            if (marker == null) marker = placement.gameObject.AddComponent<HouseholdFurnishingRuntimePresentation>();
                            marker.Configure(binding.AssetId, home.runtimeFootprintAuthority, item.activityLocationId,
                                item.activityPositionLocal, item.activityForwardLocal);
                        }
                        result.PresentationsRebuilt++;
                    }
                }
                RebuildDerivedRegistries(household, systemsHub);
                ValidateFunctionalActivityRoutes(home.runtimeFootprintAuthority, register, result.Diagnostics);
                // Lane 9c (B-7): same per-home access validation as the
                // New Game seed, so a Continue re-surfaces a home whose
                // access regressed (or was never authored).
                ValidateHomeAccess(home.runtimeFootprintAuthority, home.id, household.id, $"household {household.id}");
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
            DevelopmentTownConfiguration.HouseholdFurnishingItem[] variantItems,
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
                if (register.LiveBindingFor(item.assetId) is FurnishingPlacementBinding existingBinding)
                {
                    HouseholdFurnishingRuntimePresentation existingPresentation =
                        FindPresentation(home.runtimeFootprintAuthority, item.assetId);
                    if (existingPresentation != null)
                    {
                        string restoreError = RebindPresentationActivityLocation(
                            home.runtimeFootprintAuthority, item, existingPresentation);
                        if (restoreError != null) return restoreError;
                    }
                    else
                    {
                        string restoreError = CreatePresentation(home.runtimeFootprintAuthority, existingBinding.LocalPosition,
                            existingBinding.LocalRotation, item, item.assetId, existingBinding.DefinitionId,
                            existingBinding.CapabilityReferenceId, false, out BuildingEquipmentPlacement restoredPlacement);
                        if (restoreError != null) return restoreError;
                        restoreError = register.TryRebindPlacementForRestoration(item.assetId, restoredPlacement,
                            home.runtimeFootprintAuthority, household.homeBuildingId);
                        if (restoreError != null)
                        {
                            if (item.registerRuntimeActivityLocation)
                                home.runtimeFootprintAuthority.UnregisterRuntimeActivityLocation(item.activityLocationId);
                            if (restoredPlacement != null) UnityEngine.Object.Destroy(restoredPlacement.gameObject);
                            return restoreError;
                        }
                        HouseholdFurnishingRuntimePresentation restoredMarker = restoredPlacement.GetComponent<HouseholdFurnishingRuntimePresentation>();
                        if (restoredMarker == null) restoredMarker = restoredPlacement.gameObject.AddComponent<HouseholdFurnishingRuntimePresentation>();
                        restoredMarker.Configure(item.assetId, home.runtimeFootprintAuthority,
                            item.registerRuntimeActivityLocation ? item.activityLocationId : string.Empty,
                            item.activityPositionLocal, item.activityForwardLocal);
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
            Vector3 localPosition = FurnishingPresentationCalibration.AlignToFloor(
                item.localPosition, FloorHeight(authority, item.floorIndex, item.localPosition.y), item.alignRootToFloor);
            Vector3 floorPoint = localPosition;
            if (!authority.TryEvaluateInteriorLocalPoint(floorPoint, item.floorIndex, null, out string blocked))
                return $"Opening furnishing {item.assetId} floor position is not walkable in building {home.id}: {blocked}";
            if (item.registerRuntimeActivityLocation
                && !authority.ActivityLocations.Any(location => location != null && location.id == item.activityLocationId)
                && !authority.TryEvaluateInteriorLocalPoint(item.activityPositionLocal, item.floorIndex, null, out blocked))
                return $"Opening furnishing {item.assetId} interaction approach is not walkable in building {home.id}: {blocked}";
            if (!item.registerRuntimeActivityLocation && item.capability != FurnishingCapabilityKind.DiningTable
                && !string.IsNullOrWhiteSpace(item.activityLocationId)
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
            Quaternion localRotation = ResolvePlacementRotation(item, variantItems);
            string presentationError = CreatePresentation(authority, localPosition,
                localRotation, item, item.assetId, item.kind,
                item.activityLocationId, true, out BuildingEquipmentPlacement placement);
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
            bool applyPivotOffset,
            out BuildingEquipmentPlacement placement)
        {
            placement = null;
            if (authority == null || item?.prefab == null) return $"Furnishing {assetId} cannot be presented because its building or prefab is missing.";
            GameObject instance = UnityEngine.Object.Instantiate(item.prefab, authority.transform, false);
            if (instance == null) return $"Furnishing {assetId} prefab instantiation returned no object.";
            instance.name = $"{item.displayName} [{assetId}]";
            instance.transform.localPosition = localPosition + (applyPivotOffset ? item.pivotOffsetLocal : Vector3.zero);
            instance.transform.localRotation = localRotation;
            instance.transform.localScale = FurnishingPresentationCalibration.ApplyScale(
                instance.transform.localScale, item.visualScaleMultiplier);
            FurnishingPresentationCalibration.AlignRenderedBottomToFloor(instance.transform,
                FloorHeight(authority, item.floorIndex, localPosition.y));

            placement = instance.GetComponent<BuildingEquipmentPlacement>();
            if (placement == null) placement = instance.AddComponent<BuildingEquipmentPlacement>();
            placement.Configure(assetId, definitionId, item.displayName, item.floorIndex);
            placement.ConfigureNavigationFootprint(item.useCollisionFootprintOverride,
                item.collisionFootprintCenterLocal, item.collisionFootprintSizeMeters);
            placement.AssociateWithBuilding(authority);
            if (item.registerRuntimeActivityLocation)
            {
                string runtimeError = authority.RegisterRuntimeActivityLocation(activityLocationId,
                    item.displayName + " approach", item.activityType, item.activityPositionLocal,
                    item.activityForwardLocal, item.floorIndex, string.Empty, placement);
                if (runtimeError != null && !IsSameRuntimeLocation(authority, activityLocationId, item.activityPositionLocal))
                {
                    UnityEngine.Object.Destroy(instance);
                    placement = null;
                    return $"Furnishing {assetId} interaction point failed: {runtimeError}";
                }
            }
            return null;
        }

        private static Quaternion ResolvePlacementRotation(
            DevelopmentTownConfiguration.HouseholdFurnishingItem item,
            DevelopmentTownConfiguration.HouseholdFurnishingItem[] variantItems)
        {
            Quaternion authored = Quaternion.Euler(item.localEulerAngles);
            if (item.capability != FurnishingCapabilityKind.DiningSeat || item.visualForwardAxisLocal.sqrMagnitude < 0.0001f)
                return authored;

            DevelopmentTownConfiguration.HouseholdFurnishingItem table = (variantItems ?? Array.Empty<DevelopmentTownConfiguration.HouseholdFurnishingItem>())
                .Where(candidate => candidate != null && candidate.capability == FurnishingCapabilityKind.DiningTable)
                .OrderBy(candidate => (candidate.localPosition - item.localPosition).sqrMagnitude)
                .FirstOrDefault();
            if (table == null) return authored;
            Vector3 toTable = table.localPosition - item.localPosition;
            toTable.y = 0f;
            if (toTable.sqrMagnitude < 0.0001f) return authored;
            return FurnishingPresentationCalibration.FaceDirection(item.visualForwardAxisLocal, toTable.normalized, authored);
        }

        private static bool IsSameRuntimeLocation(BuildingFootprintAuthority authority, string id, Vector3 position)
        {
            return authority != null && authority.TryGetActivityLocation(id, 0, out ResolvedBuildingActivityLocation location, out _)
                && (location.WorldPosition - authority.transform.TransformPoint(position)).sqrMagnitude < 0.0001f;
        }

        private static HouseholdFurnishingRuntimePresentation FindPresentation(
            BuildingFootprintAuthority authority, string assetId)
        {
            if (authority == null || string.IsNullOrWhiteSpace(assetId)) return null;
            foreach (HouseholdFurnishingRuntimePresentation marker in authority.GetComponentsInChildren<HouseholdFurnishingRuntimePresentation>(true))
                if (marker != null && string.Equals(marker.AssetId, assetId, StringComparison.Ordinal)) return marker;
            return null;
        }

        private static string RebindPresentationActivityLocation(
            BuildingFootprintAuthority authority,
            DevelopmentTownConfiguration.HouseholdFurnishingItem item,
            HouseholdFurnishingRuntimePresentation presentation)
        {
            if (authority == null || item == null || presentation == null)
                return "Household furnishing activity restoration is missing its building, item, or presentation.";

            if (item.registerRuntimeActivityLocation)
            {
                BuildingEquipmentPlacement placement = presentation.GetComponent<BuildingEquipmentPlacement>();
                if (!authority.TryGetActivityLocation(item.activityLocationId, 0, out _, out _))
                {
                    string registrationError = authority.RegisterRuntimeActivityLocation(
                        item.activityLocationId, item.displayName + " approach", item.activityType,
                        item.activityPositionLocal, item.activityForwardLocal, item.floorIndex,
                        string.Empty, placement);
                    if (registrationError != null
                        && !IsSameRuntimeLocation(authority, item.activityLocationId, item.activityPositionLocal))
                        return $"Furnishing {item.assetId} interaction point could not be restored: {registrationError}";
                }
                if (!authority.TryGetActivityLocation(item.activityLocationId, 0,
                        out ResolvedBuildingActivityLocation resolved, out string resolveError))
                    return $"Furnishing {item.assetId} interaction point could not be resolved after restoration: {resolveError}";

                // Preserve the currently authoritative point (which may have
                // moved with the furniture) while rebuilding the marker's
                // nonserialized tracking state after a reload.
                Vector3 positionLocal = authority.transform.InverseTransformPoint(resolved.WorldPosition);
                Vector3 forwardLocal = authority.transform.InverseTransformDirection(resolved.WorldForward);
                presentation.Configure(item.assetId, authority, item.activityLocationId,
                    positionLocal, forwardLocal);
                return null;
            }

            presentation.Configure(item.assetId, authority, string.Empty,
                item.activityPositionLocal, item.activityForwardLocal);
            return null;
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

        /// <summary>
        /// Lane 9c (B-7): per-home startup access validation. A home
        /// needs a Stove, a Cleaning point, a Firewood point and at
        /// least one Dining chair. Interior types must be reachable from
        /// main-entrance via the type-based resolver; Firewood is an
        /// exterior pile, so presence is the whole requirement. Gaps are
        /// logged as warnings so they surface at startup (B-01..B-04)
        /// instead of as silent task failures. The check computation is
        /// shared with the business validation in
        /// DevelopmentBusinessInteriorInitializer.
        /// </summary>
        private static void ValidateHomeAccess(
            BuildingFootprintAuthority authority, int buildingId, int householdId, string householdLabel)
        {
            if (authority == null) return;
            var checks = new List<StartupAccessValidator.ActivityAccessCheck>
            {
                DevelopmentBusinessInteriorInitializer.BuildAccessCheck(authority, buildingId, BuildingActivityLocationType.Stove, requiresInteriorRoute: true),
                DevelopmentBusinessInteriorInitializer.BuildAccessCheck(authority, buildingId, BuildingActivityLocationType.Cleaning, requiresInteriorRoute: true),
                DevelopmentBusinessInteriorInitializer.BuildAccessCheck(authority, buildingId, BuildingActivityLocationType.Firewood, requiresInteriorRoute: false),
                DevelopmentBusinessInteriorInitializer.BuildAccessCheck(authority, buildingId, BuildingActivityLocationType.Dining, requiresInteriorRoute: true),
            };
            var gaps = new List<string>();
            StartupAccessValidator.AppendAccessGaps(
                $"Home '{householdLabel}' (household {householdId}, building {buildingId})", checks, gaps);
            foreach (string gap in gaps)
                Debug.LogWarning("[StartupAccess] " + gap);
        }

        private static void ValidateFunctionalActivityRoutes(
            BuildingFootprintAuthority authority,
            HouseholdFurnishingRegister register,
            List<string> diagnostics)
        {
            if (authority == null || register?.Bindings == null) return;
            // Lane 10b (I-R3): the placement-table check runs for every
            // installed binding, not only the activity kinds below — a
            // shelf installed mid-room is a placement failure even though
            // no activity route references it. The context is built
            // without scene placements (ValidateInstalledPlacement scores
            // the binding's own pose); when it cannot be built the check
            // is skipped, never guessed. A kind with no spec row has no
            // shipped-form footprint to score, so it is skipped too.
            FurniturePlacementContext placementContext = null;
            if (authority.TryBuildFurniturePlacementContext(null, out FurniturePlacementContext builtContext, out _))
                placementContext = builtContext;
            foreach (FurnishingPlacementBinding binding in register.Bindings)
            {
                if (binding == null || binding.BuildingInstanceId < 0) continue;
                if (placementContext != null)
                {
                    EquipmentAsset asset = register.FindAsset(binding.AssetId);
                    if (asset != null && FurnitureItemSpecs.TryGetRow(asset.Kind, out FurnitureItemSpecs.Row row) && row != null)
                    {
                        var pose = new FurniturePlacementPose(
                            binding.LocalPosition.x, binding.LocalPosition.z, binding.LocalRotation.eulerAngles.y);
                        string placementDiagnostic = FurniturePlacementScoring.ValidateInstalledPlacement(
                            asset.Kind, pose, row.LengthM, row.WidthM, placementContext);
                        if (!string.IsNullOrWhiteSpace(placementDiagnostic))
                            diagnostics.Add($"Installed asset {binding.AssetId} ({asset.Kind}): {placementDiagnostic}");
                    }
                }
                if (binding.CapabilityKind != FurnishingCapabilityKind.SleepingPlace
                    && binding.CapabilityKind != FurnishingCapabilityKind.StoveWorkstation
                    && binding.CapabilityKind != FurnishingCapabilityKind.DiningSeat) continue;
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
