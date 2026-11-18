using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Orchestration
{
    /// <summary>
    /// Applies finite, explicitly authored development-business fixtures.
    /// The equipment register owns each piece; this class only creates its
    /// physical presentation and building association. It never touches stock,
    /// cash, work authority, or workstation readiness.
    /// </summary>
    public static class DevelopmentBusinessInteriorInitializer
    {
        public sealed class Result
        {
            public int BusinessesProcessed;
            public int EquipmentAssetsRegistered;
            public int PresentationsCreated;
            public readonly List<string> Diagnostics = new();
        }

        public static Result SeedNewGame(
            DevelopmentTownConfiguration configuration,
            SharedBusinessRuntimeManager businessRuntime,
            TownWorldController townWorld,
            SimulationSystemsHub systemsHub,
            int dayIndex)
        {
            return Apply(configuration, businessRuntime, townWorld, systemsHub, dayIndex, true);
        }

        public static Result RestorePresentations(
            DevelopmentTownConfiguration configuration,
            SharedBusinessRuntimeManager businessRuntime,
            TownWorldController townWorld,
            SimulationSystemsHub systemsHub)
        {
            return Apply(configuration, businessRuntime, townWorld, systemsHub, 0, false);
        }

        /// <summary>Validates the authoring needed to prevent invisible or unidentifiable fixtures.</summary>
        public static string ValidateFixture(DevelopmentTownConfiguration.BusinessInteriorFixture fixture)
        {
            if (fixture == null) return "fixture record is null";
            if (string.IsNullOrWhiteSpace(fixture.assetId)) return "equipment asset ID is blank";
            if (string.IsNullOrWhiteSpace(fixture.kind)) return $"fixture '{fixture.assetId}' has no equipment kind";
            if (fixture.prefab == null) return $"fixture '{fixture.assetId}' has no approved prefab";
            if (fixture.prefab.GetComponentsInChildren<Renderer>(true).Length == 0)
                return $"fixture '{fixture.assetId}' prefab '{fixture.prefab.name}' has no rendered geometry";
            if (fixture.floorIndex < 0) return $"fixture '{fixture.assetId}' has an invalid floor index";
            if (!IsFinite(fixture.localPosition) || !IsFinite(fixture.localEulerAngles)
                || !IsFinite(fixture.pivotOffsetLocal) || !IsFinite(fixture.visualScaleMultiplier)
                || Mathf.Abs(fixture.visualScaleMultiplier.x) < 0.0001f
                || Mathf.Abs(fixture.visualScaleMultiplier.y) < 0.0001f
                || Mathf.Abs(fixture.visualScaleMultiplier.z) < 0.0001f)
                return $"fixture '{fixture.assetId}' has non-finite transforms or a zero visual scale multiplier";
            if (!string.IsNullOrWhiteSpace(fixture.activityLocationId)
                && (!IsFinite(fixture.activityPositionLocal) || !IsFinite(fixture.activityForwardLocal)
                    || fixture.activityForwardLocal.sqrMagnitude < 0.0001f))
                return $"fixture '{fixture.assetId}' has an invalid activity approach point";
            if (fixture.useCollisionFootprintOverride
                && (fixture.collisionFootprintSizeMeters.x <= 0f || fixture.collisionFootprintSizeMeters.y <= 0f))
                return $"fixture '{fixture.assetId}' has a non-positive navigation footprint";
            return null;
        }

        public static string ValidateRegisteredOwner(EquipmentAsset asset, string businessInstanceId, string expectedKind)
        {
            if (asset == null) return "equipment asset is not registered";
            if (string.IsNullOrWhiteSpace(businessInstanceId) || string.IsNullOrWhiteSpace(expectedKind))
                return "business identity and equipment kind are required";
            if (!string.Equals(asset.Kind, expectedKind.Trim(), StringComparison.Ordinal)
                || !string.Equals(asset.OwnerKind, "business", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(asset.OwnerId, businessInstanceId, StringComparison.Ordinal))
                return $"equipment '{asset.AssetId}' does not match business owner '{businessInstanceId}' and kind '{expectedKind}'";
            return null;
        }

        /// <summary>
        /// Development configuration uses readable hyphenated operation IDs,
        /// while some existing runtime profiles use underscores. Those are
        /// formatting variants of the same stable business operation key.
        /// </summary>
        public static bool OperationMatchesRuntimeProfile(string operationId, string profileId)
        {
            return NormalizeOperationId(operationId) == NormalizeOperationId(profileId);
        }

        private static string NormalizeOperationId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        private static Result Apply(
            DevelopmentTownConfiguration configuration,
            SharedBusinessRuntimeManager businessRuntime,
            TownWorldController townWorld,
            SimulationSystemsHub systemsHub,
            int dayIndex,
            bool allowOpeningEndowment)
        {
            var result = new Result();
            if (configuration == null || businessRuntime == null || townWorld == null || systemsHub == null)
            {
                result.Diagnostics.Add("Business interior setup skipped: configuration, business runtime, town, or systems hub is unavailable.");
                return result;
            }

            var globallySeenAssetIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (DevelopmentTownConfiguration.BusinessDefinition definition in configuration.Businesses)
            {
                DevelopmentTownConfiguration.BusinessInteriorFixture[] fixtures = definition?.opening?.interiorFixtures;
                if (fixtures == null || fixtures.Length == 0) continue;
                if (!TryResolveBusiness(definition, configuration, businessRuntime.Businesses, out BusinessInstanceState business, out string resolveError))
                {
                    result.Diagnostics.Add(resolveError);
                    continue;
                }
                if (business.AssignedBuildingId < 0
                    || !townWorld.TryGetBuildingById(business.AssignedBuildingId, out PlacedBuilding building)
                    || building?.runtimeFootprintAuthority == null)
                {
                    result.Diagnostics.Add($"Business '{business.RuntimeDisplayName}' ({business.InstanceId}) has no assigned building with a live BuildingFootprintAuthority; fixtures were not placed.");
                    continue;
                }

                BusinessEquipmentRegister register = systemsHub.EquipmentRegisterFor(business.InstanceId);
                BusinessRuntimeState businessState = business.RuntimeState;
                if (register == null || businessState == null)
                {
                    result.Diagnostics.Add($"Business '{business.RuntimeDisplayName}' has no equipment register or runtime state; fixtures were not installed.");
                    continue;
                }

                result.BusinessesProcessed++;
                string endowmentId = $"development-business-interior:{configuration.ConfigurationId}:{definition.key}:{business.InstanceId}";
                bool mayCreateAssets = allowOpeningEndowment && !businessState.HasAppliedScenarioEndowment(endowmentId);

                foreach (DevelopmentTownConfiguration.BusinessInteriorFixture fixture in fixtures)
                {
                    string invalid = ValidateFixture(fixture);
                    if (invalid != null)
                    {
                        result.Diagnostics.Add($"Business '{definition.key}' interior: {invalid}; skipped.");
                        continue;
                    }
                    if (!globallySeenAssetIds.Add(fixture.assetId))
                    {
                        result.Diagnostics.Add($"Business fixture equipment identity '{fixture.assetId}' appears more than once in the configuration; duplicate placement refused.");
                        continue;
                    }

                    EquipmentAsset asset = register.Assets?.FirstOrDefault(candidate => candidate != null
                        && string.Equals(candidate.AssetId, fixture.assetId, StringComparison.Ordinal));
                    if (asset == null)
                    {
                        if (!mayCreateAssets) continue;
                        asset = new EquipmentAsset
                        {
                            AssetId = fixture.assetId.Trim(),
                            Kind = fixture.kind.Trim(),
                            DisplayName = string.IsNullOrWhiteSpace(fixture.displayName) ? fixture.kind.Trim() : fixture.displayName.Trim(),
                            Condition01 = 1f,
                            OwnerKind = "business",
                            OwnerId = business.InstanceId,
                            LocationId = business.InstanceId,
                            MadeDayIndex = -1,
                        };
                        register.RegisterAsset(asset);
                        result.EquipmentAssetsRegistered++;
                    }
                    else if (ValidateRegisteredOwner(asset, business.InstanceId, fixture.kind) is string ownerError)
                    {
                        result.Diagnostics.Add($"Fixture '{fixture.assetId}' refused: {ownerError}.");
                        continue;
                    }

                    if (HasPresentation(building.runtimeFootprintAuthority, fixture.assetId)) continue;
                    if (HasPresentationInOtherBuilding(townWorld, fixture.assetId))
                    {
                        result.Diagnostics.Add($"Fixture '{fixture.assetId}' already has a presentation under a different building; duplicate placement refused.");
                        continue;
                    }

                    string placementError = CreatePresentation(building.runtimeFootprintAuthority, building.id, business.InstanceId, fixture, asset);
                    if (placementError != null) result.Diagnostics.Add($"Business '{definition.key}' fixture '{fixture.assetId}': {placementError}");
                    else result.PresentationsCreated++;
                }

                if (mayCreateAssets)
                    businessState.RecordAppliedScenarioEndowment(endowmentId);
            }
            return result;
        }

        private static bool TryResolveBusiness(
            DevelopmentTownConfiguration.BusinessDefinition definition,
            DevelopmentTownConfiguration configuration,
            IReadOnlyList<BusinessInstanceState> businesses,
            out BusinessInstanceState match,
            out string error)
        {
            match = null;
            error = string.Empty;
            if (definition == null || businesses == null)
            {
                error = "Business interior configuration cannot resolve a null business roster.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(definition.businessId))
                match = businesses.FirstOrDefault(candidate => candidate != null
                    && string.Equals(candidate.InstanceId, definition.businessId.Trim(), StringComparison.Ordinal));
            else
            {
                List<BusinessInstanceState> candidates = businesses.Where(candidate => candidate != null
                    && OperationMatchesRuntimeProfile(definition.operationId, candidate.ProfileId)).ToList();
                if (candidates.Count == 1) match = candidates[0];
                else if (candidates.Count > 1 && configuration != null
                    && configuration.ResolveRuntimePersonId(definition.ownerPersonKey) > 0)
                {
                    int ownerId = configuration.ResolveRuntimePersonId(definition.ownerPersonKey);
                    candidates = candidates.Where(candidate => candidate.Owner != null
                        && candidate.Owner.OwnerKind == BusinessOwnerKind.Npc
                        && candidate.Owner.PersonId == ownerId).ToList();
                    if (candidates.Count == 1) match = candidates[0];
                }
            }

            if (match != null) return true;
            error = $"Business interior '{definition.key}' could not resolve exactly one live business instance (operation '{definition.operationId}', configured ID '{definition.businessId}'); no fixtures were placed.";
            return false;
        }

        private static string CreatePresentation(
            BuildingFootprintAuthority authority,
            int buildingInstanceId,
            string businessId,
            DevelopmentTownConfiguration.BusinessInteriorFixture fixture,
            EquipmentAsset asset)
        {
            if (!authority.TryEvaluateInteriorLocalPoint(fixture.localPosition, fixture.floorIndex, null, out string reason))
                return $"configured fixture position is not walkable on floor {fixture.floorIndex}: {reason}";

            GameObject instance = UnityEngine.Object.Instantiate(fixture.prefab, authority.transform, false);
            if (instance == null) return "prefab instantiation returned no object";
            string label = string.IsNullOrWhiteSpace(fixture.displayName) ? fixture.kind : fixture.displayName;
            instance.name = $"{label} [{fixture.assetId}]";
            instance.transform.localPosition = fixture.localPosition + fixture.pivotOffsetLocal;
            instance.transform.localRotation = Quaternion.Euler(fixture.localEulerAngles);
            instance.transform.localScale = FurnishingPresentationCalibration.ApplyScale(
                instance.transform.localScale, fixture.visualScaleMultiplier);

            BuildingEquipmentPlacement placement = instance.GetComponent<BuildingEquipmentPlacement>();
            if (placement == null) placement = instance.AddComponent<BuildingEquipmentPlacement>();
            placement.Configure(fixture.assetId, "business." + fixture.kind.Trim(), label, fixture.floorIndex);
            placement.ConfigureNavigationFootprint(fixture.useCollisionFootprintOverride,
                fixture.collisionFootprintCenterLocal, fixture.collisionFootprintSizeMeters);
            placement.AssociateWithBuilding(authority);
            placement.SetEconomicRecord(asset.OwnerId, $"scenario-opening:{fixture.assetId}");

            var marker = instance.GetComponent<BusinessInteriorFixturePresentation>();
            if (marker == null) marker = instance.AddComponent<BusinessInteriorFixturePresentation>();
            marker.Configure(businessId, fixture.assetId, authority, fixture.activityLocationId);

            if (!string.IsNullOrWhiteSpace(fixture.activityLocationId))
            {
                if (authority.TryGetActivityLocation(fixture.activityLocationId, buildingInstanceId, out _, out _))
                    return null; // Reuse an authored physical approach location.
                string activityError = authority.RegisterRuntimeActivityLocation(
                    fixture.activityLocationId, label + " approach", fixture.activityType,
                    fixture.activityPositionLocal, fixture.activityForwardLocal,
                    fixture.floorIndex, fixture.functionalSpaceId, placement);
                if (activityError != null)
                {
                    marker.ClearActivityLocation();
                    return "fixture was placed, but its optional approach point was not registered: " + activityError;
                }
                marker.MarkOwnsActivityLocation();
            }
            return null;
        }

        private static bool HasPresentation(BuildingFootprintAuthority authority, string assetId)
        {
            return authority != null && authority.GetComponentsInChildren<BusinessInteriorFixturePresentation>(true)
                .Any(marker => marker != null && string.Equals(marker.AssetId, assetId, StringComparison.Ordinal));
        }

        private static bool HasPresentationInOtherBuilding(TownWorldController world, string assetId)
        {
            foreach (PlacedBuilding building in world.Buildings)
                if (building?.runtimeFootprintAuthority != null && HasPresentation(building.runtimeFootprintAuthority, assetId))
                    return true;
            return false;
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

}
