using System;
using LandLedgers.Economy;
using UnityEngine;
using UnityEngine.Serialization;

namespace LandLedgers.World
{
    [Serializable]
    public struct BuildingAnchorRule
    {
        [Tooltip("Anchor role exposed to later pathing and economy systems.")]
        public AnchorType type;

        [Tooltip("Authoring side of the footprint used as the anchor reference.")]
        public BuildingAnchorSide side;

        [FormerlySerializedAs("offsetFromFrontCenter")]
        [Tooltip("Offset from the center of the selected footprint side. X moves along that side; Y moves outward away from the building.")]
        public Vector2Int offsetFromSideCenter;
    }

    [Serializable]
    public struct BusinessSuitabilityEntry
    {
        public BusinessType businessType;
        public bool suitable;
    }

    public enum BuildingAnchorExpectation
    {
        Optional = 0,
        Preferred = 1,
        Required = 2
    }

    [CreateAssetMenu(
        fileName = "BuildingDefinition",
        menuName = "Land & Ledgers/World/Building Definition",
        order = 120)]
    public sealed class BuildingDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private string buildingId = "building";

        [SerializeField]
        private string displayName = "Building";

        [Header("Footprint Fallback")]
        [SerializeField, Min(1)]
        [Tooltip("Legacy fallback used only when the visual prefab has no BuildingFootprintAuthority component.")]
        private int footprintWidthCells = 4;

        [SerializeField, Min(1)]
        [Tooltip("Legacy fallback used only when the visual prefab has no BuildingFootprintAuthority component.")]
        private int footprintDepthCells = 4;

        [SerializeField]
        private PlotZone allowedPlotZone = PlotZone.MixedUse;

        [Header("Use")]
        [SerializeField]
        private BuildingUseType primaryUse = BuildingUseType.Residential;

        [SerializeField, Min(1)]
        private int storeyCount = 1;

        [SerializeField]
        private bool workplaceEligible = false;

        [SerializeField]
        private BuildingResidentialMode residentialMode = BuildingResidentialMode.StandaloneHousehold;

        [SerializeField, Min(0)]
        [Tooltip("Current MVP supports one household well; higher values are reserved for later apartment/multi-family work.")]
        private int residentHouseholdCapacity = 1;

        [Header("Agricultural Site")]
        [SerializeField]
        [Tooltip("Physical agricultural site role used by world generation and parcel dressing. Business suitability remains the compatibility gate.")]
        private AgriculturalSiteRole agriculturalSiteRole = AgriculturalSiteRole.None;

        [Header("Business Suitability")]
        [SerializeField]
        [Tooltip("Explicit per-business shell suitability. This describes whether the physical shell can host the business, not how much total land that business needs.")]
        private BusinessSuitabilityEntry[] businessSuitability = Array.Empty<BusinessSuitabilityEntry>();

        [Header("Fallback Anchors")]
        [SerializeField]
        [Tooltip("Compatibility fallback used only when the visual prefab has no matching anchor marker. Prefer prefab children named Anchor_FrontDoor, Anchor_ServicePoint, and Anchor_DropOffPoint, or BuildingAnchorMarker components.")]
        private BuildingAnchorRule[] anchorRules =
        {
            new BuildingAnchorRule { type = AnchorType.FrontDoor, side = BuildingAnchorSide.Front, offsetFromSideCenter = new Vector2Int(0, 1) },
            new BuildingAnchorRule { type = AnchorType.Service, side = BuildingAnchorSide.Back, offsetFromSideCenter = new Vector2Int(0, 1) },
            new BuildingAnchorRule { type = AnchorType.DropOff, side = BuildingAnchorSide.Front, offsetFromSideCenter = new Vector2Int(0, 2) }
        };

        [Header("Prefab Visual")]
        [SerializeField]
        [Tooltip("Optional presentation prefab spawned for generated buildings. The grid footprint remains the source of truth.")]
        private GameObject visualPrefab;

        [SerializeField]
        [Tooltip("Uniform scale multiplier applied on top of the authored prefab scale. Runtime building visuals preserve authored scale and are not auto-fit to the plot footprint.")]
        [Min(0.01f)]
        private float visualScaleMultiplier = 1f;

        [SerializeField]
        [Tooltip("Extra yaw offset in degrees after the building is rotated to face its road frontage.")]
        private float visualYawOffsetDegrees = 0f;

        [SerializeField]
        [Tooltip("Local world-space offset applied after footprint placement and rotation.")]
        private Vector3 visualPositionOffset = Vector3.zero;

        [SerializeField]
        [Tooltip("Legacy authoring flag kept for backward compatibility. Runtime building visuals preserve authored prefab scale and footprint fit is handled by plot/placement logic instead of auto-scaling.")]
        private bool fitVisualToFootprint = false;

        [SerializeField]
        [Tooltip("How much of the footprint the fitted visual should fill.")]
        [Range(0.25f, 1.25f)]
        private float footprintFill = 0.92f;

        [Header("Greybox Fallback")]
        [SerializeField, Min(0.25f)]
        private float greyboxHeightMeters = 5f;

        [SerializeField]
        private Color greyboxColor = new(0.74f, 0.58f, 0.38f, 1f);

        public string BuildingId => string.IsNullOrWhiteSpace(buildingId) ? name : buildingId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? BuildingId : displayName;
        public Vector2Int FootprintSizeCells => MinimumFootprintSizeCells;
        public Vector2Int MinimumFootprintSizeCells => ResolveMinimumFootprintSizeCells();
        public Vector2Int LegacyFootprintSizeCells => new(Mathf.Max(1, footprintWidthCells), Mathf.Max(1, footprintDepthCells));
        public bool HasPrefabFootprintAuthority => TryGetPrefabFootprintAuthority(out _);
        public PlotZone AllowedPlotZone => allowedPlotZone;
        public BuildingUseType PrimaryUse => primaryUse;
        public int StoreyCount => Mathf.Max(1, storeyCount);
        public bool CanHostWorkplace => workplaceEligible;
        public BuildingResidentialMode ResidentialMode => residentialMode;
        public int ResidentHouseholdCapacity
        {
            get
            {
                if (residentialMode == BuildingResidentialMode.None)
                {
                    return 0;
                }

                if (residentialMode == BuildingResidentialMode.UpperFloorHousehold && !IsDoubleStoreyCommercial)
                {
                    return 0;
                }

                return Mathf.Max(1, residentHouseholdCapacity);
            }
        }

        public bool CanHostHouseholds => ResidentHouseholdCapacity > 0;
        public bool IsMixedUse => CanHostWorkplace && CanHostHouseholds;
        public bool IsDoubleStoreyCommercial => CanHostWorkplace && StoreyCount >= 2;
        public bool UsesUpperFloorResidential => residentialMode == BuildingResidentialMode.UpperFloorHousehold && CanHostHouseholds;
        public bool HasDedicatedAgriculturalRole => agriculturalSiteRole != AgriculturalSiteRole.None;
        public AgriculturalSiteRole AgriculturalSiteRole => agriculturalSiteRole;
        public ReadOnlySpan<BusinessSuitabilityEntry> BusinessSuitability => businessSuitability ?? Array.Empty<BusinessSuitabilityEntry>();
        public ReadOnlySpan<BuildingAnchorRule> AnchorRules => anchorRules != null ? anchorRules : Array.Empty<BuildingAnchorRule>();
        public GameObject VisualPrefab => visualPrefab;
        public float VisualScaleMultiplier => Mathf.Max(0.01f, visualScaleMultiplier);
        public float VisualYawOffsetDegrees => visualYawOffsetDegrees;
        public Vector3 VisualPositionOffset => visualPositionOffset;
        public bool FitVisualToFootprint => fitVisualToFootprint; // Legacy-only compatibility flag; runtime building visuals preserve authored scale.
        public float FootprintFill => Mathf.Clamp(footprintFill, 0.25f, 1.25f);
        public float GreyboxHeightMeters => Mathf.Max(0.25f, greyboxHeightMeters);
        public Color GreyboxColor => greyboxColor;

        public bool TryGetRoadAccessLocalPoint(out Vector3 localPoint, out string source)
        {
            if (TryGetPrefabFootprintAuthority(out BuildingFootprintAuthority authority)
                && authority.TryGetRoadAccessLocalPose(out localPoint, out _))
            {
                source = authority.UsesFrontPointAsRoadAccessAnchor
                    ? "BuildingFootprintAuthority road-access point"
                    : "BuildingFootprintAuthority front-edge center";
                return true;
            }

            if (TryGetAuthoredMarkerLocalPoint(AnchorType.FrontDoor, preferRoadAccessMarker: true, out localPoint, out source))
            {
                return true;
            }

            localPoint = Vector3.zero;
            source = string.Empty;
            return false;
        }

        public bool TryGetAuthoredMarkerLocalPoint(AnchorType anchorType, out Vector3 localPoint, out string source)
        {
            return TryGetAuthoredMarkerLocalPoint(anchorType, preferRoadAccessMarker: false, out localPoint, out source);
        }

        public bool TryResolveVisualRootPositionForRoadAccess(
            Vector3 targetRoadAccessWorldPoint,
            Quaternion visualRotation,
            out Vector3 visualRootWorldPosition,
            out string source)
        {
            if (!TryGetRoadAccessLocalPoint(out Vector3 localPoint, out source))
            {
                visualRootWorldPosition = targetRoadAccessWorldPoint;
                return false;
            }

            Vector3 scaledLocalPoint = Vector3.Scale(localPoint, ResolveEffectiveVisualRootScale());
            visualRootWorldPosition = targetRoadAccessWorldPoint - visualRotation * scaledLocalPoint;
            return true;
        }

        public bool TryResolveFootprintOriginForRoadAccess(
            Vector3 targetRoadAccessWorldPoint,
            Quaternion visualRotation,
            out Vector3 footprintOriginWorldPosition,
            out string source)
        {
            if (!TryGetRoadAccessLocalPoint(out Vector3 localPoint, out source))
            {
                footprintOriginWorldPosition = targetRoadAccessWorldPoint;
                return false;
            }

            Vector3 scaledLocalPoint = Vector3.Scale(localPoint, ResolveEffectiveVisualRootScale());
            Vector3 rotatedVisualOffset = visualRotation * (visualPositionOffset + scaledLocalPoint);
            footprintOriginWorldPosition = targetRoadAccessWorldPoint - rotatedVisualOffset;
            return true;
        }

        public bool CanUsePlot(PlotZone plotZone)
        {
            if (allowedPlotZone == plotZone)
            {
                return true;
            }

            if (allowedPlotZone == PlotZone.Agricultural || plotZone == PlotZone.Agricultural)
            {
                return false;
            }

            return allowedPlotZone == PlotZone.MixedUse || plotZone == PlotZone.MixedUse;
        }

        public string BuildUseSummary()
        {
            if (HasDedicatedAgriculturalRole)
            {
                return BuildAgriculturalRoleSummary();
            }

            if (CanHostWorkplace && CanHostHouseholds)
            {
                return UsesUpperFloorResidential
                    ? "Mixed-use shell | storefront below, household space above"
                    : "Mixed-use shell | business and household space";
            }

            if (CanHostWorkplace)
            {
                return StoreyCount > 1
                    ? $"Commercial shell | {StoreyCount} storeys"
                    : "Commercial shell";
            }

            if (CanHostHouseholds)
            {
                return ResidentHouseholdCapacity > 1
                    ? $"Residential shell | up to {ResidentHouseholdCapacity} households"
                    : "Residential shell | single-household home";
            }

            if (PrimaryUse == BuildingUseType.Civic)
            {
                return "Civic shell";
            }

            return "Special-purpose shell";
        }

        public string BuildInspectionSummary()
        {
            string footprintSource = HasPrefabFootprintAuthority ? "prefab authority" : "legacy fallback";
            string footprint = $"Footprint {FootprintSizeCells.x}x{FootprintSizeCells.y} cells ({footprintSource})";
            string plotUse = AllowedPlotZone == PlotZone.MixedUse ? "Mixed-use frontage" : AllowedPlotZone.ToString();
            string work = CanHostWorkplace ? "work-ready" : "not work-ready";
            string households = CanHostHouseholds ? $"{ResidentHouseholdCapacity} household slot{(ResidentHouseholdCapacity == 1 ? string.Empty : "s")}" : "no household slots";
            return BuildUseSummary() + " | " + footprint + " | " + plotUse + " | " + work + " | " + households;
        }

        public BuildingAnchorExpectation GetAnchorExpectation(AnchorType anchorType)
        {
            switch (anchorType)
            {
                case AnchorType.FrontDoor:
                    return BuildingAnchorExpectation.Required;
                case AnchorType.Service:
                    if (HasDedicatedAgriculturalRole || CanHostWorkplace)
                    {
                        return BuildingAnchorExpectation.Required;
                    }

                    if (PrimaryUse == BuildingUseType.Civic)
                    {
                        return BuildingAnchorExpectation.Preferred;
                    }

                    return BuildingAnchorExpectation.Optional;
                case AnchorType.DropOff:
                    if (HasDedicatedAgriculturalRole)
                    {
                        return BuildingAnchorExpectation.Required;
                    }

                    if (CanHostWorkplace || PrimaryUse == BuildingUseType.Civic)
                    {
                        return BuildingAnchorExpectation.Preferred;
                    }

                    return BuildingAnchorExpectation.Optional;
                default:
                    return BuildingAnchorExpectation.Optional;
            }
        }

        public bool ShouldValidateAnchor(AnchorType anchorType)
        {
            return GetAnchorExpectation(anchorType) != BuildingAnchorExpectation.Optional;
        }

        public bool PrefersRoadFacingFrontDoor
        {
            get
            {
                if (HasDedicatedAgriculturalRole)
                {
                    return false;
                }

                return CanHostHouseholds || CanHostWorkplace || PrimaryUse == BuildingUseType.Civic;
            }
        }

        public bool PrefersBroadFrontage
        {
            get
            {
                if (HasDedicatedAgriculturalRole)
                {
                    return false;
                }

                return CanHostWorkplace || UsesUpperFloorResidential || PrimaryUse == BuildingUseType.Civic;
            }
        }

        public bool PrefersDeepYard
        {
            get
            {
                if (HasDedicatedAgriculturalRole)
                {
                    return true;
                }

                if (CanHostHouseholds && !CanHostWorkplace)
                {
                    return true;
                }

                return UsesUpperFloorResidential;
            }
        }

        public bool ToleratesTightPad
        {
            get
            {
                if (HasDedicatedAgriculturalRole)
                {
                    return false;
                }

                if (CanHostWorkplace && !CanHostHouseholds)
                {
                    return true;
                }

                return PrimaryUse == BuildingUseType.Civic;
            }
        }

        public string BuildAnchorExpectationSummary()
        {
            return $"Anchor profile: front door {FormatAnchorExpectation(GetAnchorExpectation(AnchorType.FrontDoor))} | service {FormatAnchorExpectation(GetAnchorExpectation(AnchorType.Service))} | drop-off {FormatAnchorExpectation(GetAnchorExpectation(AnchorType.DropOff))}";
        }

        public string BuildSiteIntentSummary()
        {
            string frontage = PrefersBroadFrontage
                ? "frontage-forward shell"
                : HasDedicatedAgriculturalRole
                    ? "yard-forward work shell"
                    : CanHostHouseholds
                        ? "domestic frontage shell"
                        : "balanced shell";
            string yard = PrefersDeepYard
                ? "benefits from deeper yard room"
                : ToleratesTightPad
                    ? "can tolerate a tighter build pad"
                    : "reads best with some support ground";
            string conversion = IsMixedUse
                ? "mixed-use conversion remains sensible"
                : UsesUpperFloorResidential
                    ? "supports storefront-below household-above use"
                    : HasDedicatedAgriculturalRole
                        ? "best when the parcel stays a working yard"
                        : CanHostWorkplace
                            ? "best on practical commercial frontage"
                            : CanHostHouseholds
                                ? "best on a domestic or quieter mixed-use lot"
                                : "special siting depends on frontage and access";
            return $"Site intent: {frontage} | {yard} | {conversion}";
        }

        public bool MatchesAgriculturalSiteRole(AgriculturalSiteRole role)
        {
            return role != AgriculturalSiteRole.None && agriculturalSiteRole == role;
        }

        public bool IsFallbackAgriculturalFit(AgriculturalSiteRole role)
        {
            if (!CanHostWorkplace || role == AgriculturalSiteRole.None)
            {
                return false;
            }

            return role switch
            {
                AgriculturalSiteRole.CropProductionYard => IsSuitableForBusiness(BusinessType.CropFarm),
                AgriculturalSiteRole.LivestockYard => IsSuitableForBusiness(BusinessType.Ranch),
                AgriculturalSiteRole.SawmillYard => IsSuitableForBusiness(BusinessType.Sawmill) || IsSuitableForBusiness(BusinessType.LumberYard),
                _ => false
            };
        }

        public string BuildAgriculturalFitSummary(AgriculturalSiteRole plotRole)
        {
            if (plotRole == AgriculturalSiteRole.None)
            {
                return HasDedicatedAgriculturalRole
                    ? $"Agricultural fit: dedicated {FormatAgriculturalRole(agriculturalSiteRole)} shell"
                    : string.Empty;
            }

            if (MatchesAgriculturalSiteRole(plotRole))
            {
                return $"Agricultural fit: dedicated {FormatAgriculturalRole(plotRole)} shell";
            }

            if (HasDedicatedAgriculturalRole)
            {
                return $"Agricultural fit: {FormatAgriculturalRole(agriculturalSiteRole)} shell on {FormatAgriculturalRole(plotRole)} site";
            }

            if (IsFallbackAgriculturalFit(plotRole))
            {
                return $"Agricultural fit: generic work shell fallback for {FormatAgriculturalRole(plotRole)}";
            }

            return $"Agricultural fit: weak for {FormatAgriculturalRole(plotRole)}";
        }

        public bool IsSuitableForBusiness(BusinessType businessType)
        {
            if (!CanHostWorkplace || businessSuitability == null)
            {
                return false;
            }

            // Last authored entry wins so designers can override older suitability rows without reordering the whole list.
            for (int i = businessSuitability.Length - 1; i >= 0; i--)
            {
                if (businessSuitability[i].businessType == businessType)
                {
                    return businessSuitability[i].suitable;
                }
            }

            return IsDefaultCommercialProfessionFit(businessType);
        }

        public void ConfigureRuntimeFallback(string id, string label, PlotZone zone, Vector2Int footprintSize, Color color, float heightMeters)
        {
            buildingId = id;
            displayName = label;
            allowedPlotZone = zone;
            footprintWidthCells = Mathf.Max(1, footprintSize.x);
            footprintDepthCells = Mathf.Max(1, footprintSize.y);
            greyboxColor = color;
            greyboxHeightMeters = Mathf.Max(0.25f, heightMeters);
            visualPrefab = null;
            primaryUse = zone == PlotZone.Residential ? BuildingUseType.Residential : BuildingUseType.Commercial;
            storeyCount = 1;
            workplaceEligible = zone != PlotZone.Residential;
            residentialMode = zone == PlotZone.Residential ? BuildingResidentialMode.StandaloneHousehold : BuildingResidentialMode.None;
            residentHouseholdCapacity = zone == PlotZone.Residential ? 1 : 0;
            agriculturalSiteRole = AgriculturalSiteRole.None;
            businessSuitability = CreateFallbackSuitability(zone);
            ApplyAuthoringGuardrails();
        }

        public void ConfigureRuntimeCivicFallback(string id, string label, PlotZone zone, Vector2Int footprintSize, Color color, float heightMeters)
        {
            buildingId = id;
            displayName = label;
            allowedPlotZone = zone;
            footprintWidthCells = Mathf.Max(1, footprintSize.x);
            footprintDepthCells = Mathf.Max(1, footprintSize.y);
            greyboxColor = color;
            greyboxHeightMeters = Mathf.Max(0.25f, heightMeters);
            visualPrefab = null;
            primaryUse = BuildingUseType.Civic;
            storeyCount = 1;
            workplaceEligible = false;
            residentialMode = BuildingResidentialMode.None;
            residentHouseholdCapacity = 0;
            agriculturalSiteRole = AgriculturalSiteRole.None;
            businessSuitability = CreateNoBusinessSuitability();
            ApplyAuthoringGuardrails();
        }

        public void ConfigureAgriculturalSiteRole(AgriculturalSiteRole role)
        {
            agriculturalSiteRole = role;
            ApplyAuthoringGuardrails();
        }

        public void ConfigureVisual(GameObject prefab, float scaleMultiplier, float yawOffsetDegrees, Vector3 positionOffset, bool fitToFootprint, float fill)
        {
            visualPrefab = prefab;
            visualScaleMultiplier = Mathf.Max(0.01f, scaleMultiplier);
            visualYawOffsetDegrees = yawOffsetDegrees;
            visualPositionOffset = positionOffset;
            fitVisualToFootprint = fitToFootprint;
            footprintFill = Mathf.Clamp(fill, 0.25f, 1.25f);
        }

        private void OnValidate()
        {
            ApplyAuthoringGuardrails();
        }

        private void ApplyAuthoringGuardrails()
        {
            footprintWidthCells = Mathf.Max(1, footprintWidthCells);
            footprintDepthCells = Mathf.Max(1, footprintDepthCells);
            storeyCount = Mathf.Max(1, storeyCount);

            if (residentialMode == BuildingResidentialMode.UpperFloorHousehold)
            {
                workplaceEligible = true;
                storeyCount = Mathf.Max(2, storeyCount);
            }

            if (HasDedicatedAgriculturalRole)
            {
                allowedPlotZone = PlotZone.Agricultural;
                workplaceEligible = true;
                if (residentialMode == BuildingResidentialMode.UpperFloorHousehold)
                {
                    residentialMode = BuildingResidentialMode.None;
                }
            }

            residentHouseholdCapacity = residentialMode == BuildingResidentialMode.None
                ? Mathf.Max(0, residentHouseholdCapacity)
                : Mathf.Max(1, residentHouseholdCapacity);

            NormalizeBusinessSuitabilityEntries();
            if (!workplaceEligible)
            {
                ClearEnabledBusinessSuitability();
            }

            NormalizeAnchorRules();
            if (HasDedicatedAgriculturalRole)
            {
                EnsureAgriculturalRoleSuitability();
            }

            visualScaleMultiplier = Mathf.Max(0.01f, visualScaleMultiplier);
            footprintFill = Mathf.Clamp(footprintFill, 0.25f, 1.25f);
            greyboxHeightMeters = Mathf.Max(0.25f, greyboxHeightMeters);
        }

        private Vector2Int ResolveMinimumFootprintSizeCells()
        {
            if (TryGetPrefabFootprintAuthority(out BuildingFootprintAuthority authority))
            {
                return authority.MinimumFootprintSizeCells;
            }

            return LegacyFootprintSizeCells;
        }

        private bool TryGetPrefabFootprintAuthority(out BuildingFootprintAuthority authority)
        {
            if (visualPrefab == null)
            {
                authority = null;
                return false;
            }

            authority = visualPrefab.GetComponent<BuildingFootprintAuthority>();
            return authority != null;
        }

        private bool TryGetAuthoredMarkerLocalPoint(
            AnchorType anchorType,
            bool preferRoadAccessMarker,
            out Vector3 localPoint,
            out string source)
        {
            localPoint = Vector3.zero;
            source = string.Empty;

            if (visualPrefab == null)
            {
                return false;
            }

            BuildingAnchorMarker[] markers = visualPrefab.GetComponentsInChildren<BuildingAnchorMarker>(true);
            BuildingAnchorMarker best = null;
            for (int i = 0; i < markers.Length; i++)
            {
                BuildingAnchorMarker marker = markers[i];
                if (marker == null || marker.Type != anchorType)
                {
                    continue;
                }

                if (best == null)
                {
                    best = marker;
                }

                if (preferRoadAccessMarker && marker.UseAsRoadAccessAnchor)
                {
                    best = marker;
                    break;
                }
            }

            if (best == null)
            {
                return false;
            }

            localPoint = visualPrefab.transform.InverseTransformPoint(best.transform.position);
            string markerName = string.IsNullOrWhiteSpace(best.name) ? anchorType.ToString() : best.name;
            source = best.UseAsRoadAccessAnchor
                ? $"BuildingAnchorMarker road-access candidate '{markerName}'"
                : $"BuildingAnchorMarker '{markerName}'";
            return true;
        }

        private Vector3 ResolveEffectiveVisualRootScale()
        {
            Vector3 authoredScale = visualPrefab != null ? visualPrefab.transform.localScale : Vector3.one;
            float multiplier = VisualScaleMultiplier;
            return new Vector3(
                authoredScale.x * multiplier,
                authoredScale.y * multiplier,
                authoredScale.z * multiplier);
        }

        private void NormalizeBusinessSuitabilityEntries()
        {
            if (businessSuitability == null || businessSuitability.Length <= 1)
            {
                businessSuitability ??= Array.Empty<BusinessSuitabilityEntry>();
                return;
            }

            // Keep the first visible slot for each business type, but let the latest authored value win inside that slot.
            System.Collections.Generic.Dictionary<BusinessType, int> indexByType = new();
            System.Collections.Generic.List<BusinessSuitabilityEntry> normalized = new(businessSuitability.Length);
            for (int i = 0; i < businessSuitability.Length; i++)
            {
                BusinessSuitabilityEntry entry = businessSuitability[i];
                if (indexByType.TryGetValue(entry.businessType, out int existingIndex))
                {
                    normalized[existingIndex] = entry;
                    continue;
                }

                indexByType[entry.businessType] = normalized.Count;
                normalized.Add(entry);
            }

            businessSuitability = normalized.ToArray();
        }

        private void ClearEnabledBusinessSuitability()
        {
            if (businessSuitability == null || businessSuitability.Length == 0)
            {
                businessSuitability ??= Array.Empty<BusinessSuitabilityEntry>();
                return;
            }

            for (int i = 0; i < businessSuitability.Length; i++)
            {
                BusinessSuitabilityEntry entry = businessSuitability[i];
                if (!entry.suitable)
                {
                    continue;
                }

                entry.suitable = false;
                businessSuitability[i] = entry;
            }
        }

        private void NormalizeAnchorRules()
        {
            if (anchorRules == null || anchorRules.Length == 0)
            {
                anchorRules = Array.Empty<BuildingAnchorRule>();
            }

            System.Collections.Generic.Dictionary<AnchorType, int> indexByType = new();
            System.Collections.Generic.List<BuildingAnchorRule> normalized = new(anchorRules.Length + 3);
            for (int i = 0; i < anchorRules.Length; i++)
            {
                BuildingAnchorRule rule = anchorRules[i];
                rule.offsetFromSideCenter = new Vector2Int(rule.offsetFromSideCenter.x, Mathf.Max(0, rule.offsetFromSideCenter.y));
                if (indexByType.TryGetValue(rule.type, out int existingIndex))
                {
                    normalized[existingIndex] = rule;
                    continue;
                }

                indexByType[rule.type] = normalized.Count;
                normalized.Add(rule);
            }

            EnsureAnchorRuleExists(AnchorType.FrontDoor, normalized, indexByType);
            if (ShouldValidateAnchor(AnchorType.Service))
            {
                EnsureAnchorRuleExists(AnchorType.Service, normalized, indexByType);
            }

            if (ShouldValidateAnchor(AnchorType.DropOff))
            {
                EnsureAnchorRuleExists(AnchorType.DropOff, normalized, indexByType);
            }

            anchorRules = normalized.ToArray();
        }

        private static void EnsureAnchorRuleExists(
            AnchorType anchorType,
            System.Collections.Generic.List<BuildingAnchorRule> normalized,
            System.Collections.Generic.Dictionary<AnchorType, int> indexByType)
        {
            if (indexByType.ContainsKey(anchorType))
            {
                return;
            }

            indexByType[anchorType] = normalized.Count;
            normalized.Add(CreateDefaultAnchorRule(anchorType));
        }

        private static BuildingAnchorRule CreateDefaultAnchorRule(AnchorType anchorType)
        {
            return anchorType switch
            {
                AnchorType.FrontDoor => new BuildingAnchorRule
                {
                    type = AnchorType.FrontDoor,
                    side = BuildingAnchorSide.Front,
                    offsetFromSideCenter = new Vector2Int(0, 1)
                },
                AnchorType.Service => new BuildingAnchorRule
                {
                    type = AnchorType.Service,
                    side = BuildingAnchorSide.Back,
                    offsetFromSideCenter = new Vector2Int(0, 1)
                },
                AnchorType.DropOff => new BuildingAnchorRule
                {
                    type = AnchorType.DropOff,
                    side = BuildingAnchorSide.Front,
                    offsetFromSideCenter = new Vector2Int(0, 2)
                },
                _ => new BuildingAnchorRule
                {
                    type = anchorType,
                    side = BuildingAnchorSide.Front,
                    offsetFromSideCenter = new Vector2Int(0, 1)
                }
            };
        }

        private void EnsureAgriculturalRoleSuitability()
        {
            if (!TryGetPrimaryBusinessForAgriculturalRole(agriculturalSiteRole, out BusinessType businessType))
            {
                return;
            }

            if (businessSuitability == null)
            {
                businessSuitability = Array.Empty<BusinessSuitabilityEntry>();
            }
            for (int i = 0; i < businessSuitability.Length; i++)
            {
                if (businessSuitability[i].businessType == businessType)
                {
                    return;
                }
            }

            BusinessSuitabilityEntry[] expanded = new BusinessSuitabilityEntry[businessSuitability.Length + 1];
            if (businessSuitability.Length > 0)
            {
                Array.Copy(businessSuitability, expanded, businessSuitability.Length);
            }

            expanded[expanded.Length - 1] = new BusinessSuitabilityEntry
            {
                businessType = businessType,
                suitable = true
            };
            businessSuitability = expanded;
        }

        private static string BuildAgriculturalRoleSummary(AgriculturalSiteRole role)
        {
            return role switch
            {
                AgriculturalSiteRole.CropProductionYard => "Agricultural shell | crop-production yard",
                AgriculturalSiteRole.LivestockYard => "Agricultural shell | livestock yard",
                AgriculturalSiteRole.SawmillYard => "Industrial yard shell | sawmill work yard",
                _ => "Agricultural shell"
            };
        }

        private string BuildAgriculturalRoleSummary()
        {
            return BuildAgriculturalRoleSummary(agriculturalSiteRole);
        }

        private static string FormatAnchorExpectation(BuildingAnchorExpectation expectation)
        {
            return expectation switch
            {
                BuildingAnchorExpectation.Required => "required",
                BuildingAnchorExpectation.Preferred => "preferred",
                _ => "optional"
            };
        }

        private static string FormatAgriculturalRole(AgriculturalSiteRole role)
        {
            return role switch
            {
                AgriculturalSiteRole.CropProductionYard => "crop-production yard",
                AgriculturalSiteRole.LivestockYard => "livestock yard",
                AgriculturalSiteRole.SawmillYard => "sawmill yard",
                _ => "working yard"
            };
        }

        private static bool TryGetPrimaryBusinessForAgriculturalRole(AgriculturalSiteRole role, out BusinessType businessType)
        {
            switch (role)
            {
                case AgriculturalSiteRole.CropProductionYard:
                    businessType = BusinessType.CropFarm;
                    return true;
                case AgriculturalSiteRole.LivestockYard:
                    businessType = BusinessType.Ranch;
                    return true;
                case AgriculturalSiteRole.SawmillYard:
                    businessType = BusinessType.Sawmill;
                    return true;
                default:
                    businessType = default;
                    return false;
            }
        }

        private static BusinessSuitabilityEntry[] CreateFallbackSuitability(PlotZone zone)
        {
            if (zone == PlotZone.Residential)
            {
                return CreateNoBusinessSuitability();
            }

            return new[]
            {
                new BusinessSuitabilityEntry { businessType = BusinessType.GeneralStore, suitable = true },
                new BusinessSuitabilityEntry { businessType = BusinessType.Blacksmith, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.Butcher, suitable = true },
                new BusinessSuitabilityEntry { businessType = BusinessType.Ranch, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.CropFarm, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.Doctor, suitable = true },
                new BusinessSuitabilityEntry { businessType = BusinessType.Sawmill, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.LumberYard, suitable = true },
                new BusinessSuitabilityEntry { businessType = BusinessType.BoardingHouse, suitable = true },
                new BusinessSuitabilityEntry { businessType = BusinessType.LiveryFreight, suitable = true },
                new BusinessSuitabilityEntry { businessType = BusinessType.Builder, suitable = true },
                new BusinessSuitabilityEntry { businessType = BusinessType.FuelDealer, suitable = true },
                new BusinessSuitabilityEntry { businessType = BusinessType.GrainMill, suitable = true },
                new BusinessSuitabilityEntry { businessType = BusinessType.Bakery, suitable = true },
                new BusinessSuitabilityEntry { businessType = BusinessType.Tailor, suitable = true },
                new BusinessSuitabilityEntry { businessType = BusinessType.Saloon, suitable = true },
                new BusinessSuitabilityEntry { businessType = BusinessType.Barber, suitable = true },
                new BusinessSuitabilityEntry { businessType = BusinessType.Wheelwright, suitable = true },
                new BusinessSuitabilityEntry { businessType = BusinessType.Bank, suitable = true }
            };
        }

        private static bool IsDefaultCommercialProfessionFit(BusinessType businessType)
        {
            return businessType == BusinessType.LiveryFreight
                || businessType == BusinessType.Builder
                || businessType == BusinessType.FuelDealer
                || businessType == BusinessType.GrainMill
                || businessType == BusinessType.Bakery
                || businessType == BusinessType.Tailor
                || businessType == BusinessType.Saloon
                || businessType == BusinessType.Barber
                || businessType == BusinessType.Wheelwright
                || businessType == BusinessType.Bank;
        }

        private static BusinessSuitabilityEntry[] CreateNoBusinessSuitability()
        {
            return new[]
            {
                new BusinessSuitabilityEntry { businessType = BusinessType.GeneralStore, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.Blacksmith, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.Butcher, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.Ranch, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.CropFarm, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.Doctor, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.Sawmill, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.LumberYard, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.BoardingHouse, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.LiveryFreight, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.Builder, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.FuelDealer, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.GrainMill, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.Bakery, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.Tailor, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.Saloon, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.Barber, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.Wheelwright, suitable = false },
                new BusinessSuitabilityEntry { businessType = BusinessType.Bank, suitable = false }
            };
        }
    }
}
