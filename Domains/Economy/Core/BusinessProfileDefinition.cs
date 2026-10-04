using System;
using System.Collections.Generic;
using LandLedgers.Persistence;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy
{
    public enum BusinessThroughputMode
    {
        Retail = 0,
        Producer = 1,
        Converter = 2,
        Service = 3
    }

    public enum BusinessArchetype
    {
        Unspecified = 0,
        GoodsTransformer = 1,
        ServiceThroughput = 2,
        LogisticsMovement = 3,
        Lodging = 4,
        ConstructionProject = 5,
        SpecialProduction = 6
    }

    [Serializable]
    public sealed class BusinessMainFocusDefinition
    {
        [SerializeField]
        private string focusId = "balanced";

        [SerializeField]
        private string displayName = "Balanced";

        [SerializeField, TextArea(1, 3)]
        private string playerFacingSummary = "Keeps the business balanced across its first-pass role.";

        public string FocusId => string.IsNullOrWhiteSpace(focusId) ? "balanced" : focusId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "Balanced" : displayName;
        public string PlayerFacingSummary => string.IsNullOrWhiteSpace(playerFacingSummary)
            ? "Keeps the business balanced across its first-pass role."
            : playerFacingSummary;
        public bool IsConfigured => !string.Equals(FocusId, "balanced", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(DisplayName, "Balanced", StringComparison.OrdinalIgnoreCase);
    }

    [Serializable]
    public sealed class BusinessMainFocusState
    {
        [SerializeField]
        private string focusId = "balanced";

        [SerializeField]
        private string displayName = "Balanced";

        [SerializeField, TextArea(1, 3)]
        private string playerFacingSummary = "Keeps the business balanced across its first-pass role.";

        public string FocusId => string.IsNullOrWhiteSpace(focusId) ? "balanced" : focusId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "Balanced" : displayName;
        public string PlayerFacingSummary => string.IsNullOrWhiteSpace(playerFacingSummary)
            ? "Keeps the business balanced across its first-pass role."
            : playerFacingSummary;

        public static BusinessMainFocusState FromDefinition(BusinessMainFocusDefinition definition, BusinessType businessType)
        {
            if (definition != null && definition.IsConfigured)
            {
                return new BusinessMainFocusState
                {
                    focusId = definition.FocusId,
                    displayName = definition.DisplayName,
                    playerFacingSummary = definition.PlayerFacingSummary
                };
            }

            return CreateFallback(businessType);
        }

        public BusinessMainFocusSaveDto CaptureSaveDto()
        {
            return new BusinessMainFocusSaveDto
            {
                focusId = FocusId,
                displayName = DisplayName,
                playerFacingSummary = PlayerFacingSummary
            };
        }

        public static BusinessMainFocusState FromSaveDto(BusinessMainFocusSaveDto dto, BusinessType fallbackBusinessType)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.focusId))
            {
                return CreateFallback(fallbackBusinessType);
            }

            if (fallbackBusinessType == BusinessType.Sawmill
                && string.Equals(dto.focusId, "small_mill", StringComparison.OrdinalIgnoreCase))
            {
                return Create("small_sawmill", "Small Sawmill", string.IsNullOrWhiteSpace(dto.playerFacingSummary)
                    ? "Cuts remote timber into construction lumber with simple offcut sales."
                    : dto.playerFacingSummary);
            }

            return Create(dto.focusId, dto.displayName, dto.playerFacingSummary);
        }

        private static BusinessMainFocusState CreateFallback(BusinessType businessType)
        {
            return businessType switch
            {
                BusinessType.GeneralStore => Create("balanced_staples", "Balanced Staples", "Keeps staple shelves reliable while carrying household goods, hardware, and remedies."),
                BusinessType.Blacksmith => Create("repair_support", "Repair Support", "Prioritizes practical repairs and hardware support for farms, ranches, and wagons."),
                BusinessType.Butcher => Create("fresh_sales", "Fresh Sales", "Keeps fresh meat moving through the town before preserving or bulk sales matter."),
                BusinessType.CropFarm => Create("staple_production", "Staple Production", "Turns seed and field labor into steady food crop output for town demand."),
                BusinessType.Ranch => Create("beef", "Beef", "Maintains feed and livestock capacity around cattle output for local meat supply."),
                BusinessType.Doctor => Create("balanced_service", "Balanced Service", "Balances office visits, house calls, and remedy stock for basic town care."),
                BusinessType.Sawmill => Create("small_sawmill", "Small Sawmill", "Cuts timber, stages logs, and keeps lumber moving into local construction supply."),
                BusinessType.LumberYard => Create("lumber_yard", "Lumber Yard", "Keeps finished lumber stocked near town construction demand."),
                BusinessType.BoardingHouse => Create("lodging_house", "Lodging House", "Keeps rented beds, meals, and basic supervision available for newcomers before permanent settlement."),
                BusinessType.LiveryFreight => Create("livery_freight", "Livery & Freight", "Keeps teams, wagons, and freight service available for local hauling."),
                BusinessType.Builder => Create("builder_crew", "Builder", "Provides carpentry labor capacity for construction, fit-outs, and repairs."),
                BusinessType.FuelDealer => Create("fuel_yard", "Fuel Yard", "Turns slabs, offcuts, and cordwood into reliable household fuel supply."),
                BusinessType.GrainMill => Create("grist_mill", "Grist Mill", "Turns local grain into flour for bakers, stores, and households."),
                BusinessType.Bakery => Create("bake_shop", "Bake Shop", "Turns flour and heat into bread for households and store counters."),
                BusinessType.Tailor => Create("tailor_room", "Tailor", "Turns cloth and notions into workwear, mending, and clothing service."),
                BusinessType.Saloon => Create("saloon", "Saloon", "Serves meals, drink, and social table service before a mature hotel trade."),
                BusinessType.Barber => Create("barber", "Barber", "Provides periodic haircut, shave, and bath service without a heavy stock chain."),
                BusinessType.Wheelwright => Create("wheelwright", "Wheelwright", "Repairs wagons, wheels, and practical wooden gear for town work."),
                BusinessType.Mine => Create("working_mine", "Working Mine", "Pulls ore or coal through a staffed mine ledger without leaving the shared business shell."),
                BusinessType.Tannery => Create("tannery", "Tannery", "Turns hides and bark tannin into workable leather for saddlers and harness makers."),
                BusinessType.PostOffice => Create("post_office", "Post Office", "Moves the town's letters and parcels on real schedules — the information network made physical."),
                BusinessType.Newspaper => Create("weekly_paper", "Weekly Paper", "Prints the town's weekly on a real press — editions, paid ads, subscriptions, and job printing."),
                BusinessType.Bank => Create("bank", "Bank", "Takes deposits as real liabilities, keeps vault specie, and lends only what it holds — the town's money business."),
                _ => Create("balanced", "Balanced", "Keeps the business balanced across its first-pass role.")
            };
        }

        private static BusinessMainFocusState Create(string id, string label, string summary)
        {
            return new BusinessMainFocusState
            {
                focusId = id,
                displayName = label,
                playerFacingSummary = summary
            };
        }
    }

    [Serializable]
    public sealed class BusinessSiteRequirementDefinition
    {
        [SerializeField]
        private Vector2Int minimumSiteSizeCells = new(1, 1);

        [SerializeField, Min(0)]
        private int minimumFrontageCells = 0;

        [SerializeField, Min(0)]
        private int minimumBuildableAreaCells = 0;

        [SerializeField]
        private PlotZone[] allowedPlotZones = { PlotZone.Business, PlotZone.MixedUse };

        [SerializeField]
        private bool requiresRoadFrontage = true;

        public Vector2Int MinimumSiteSizeCells => new(Mathf.Max(1, minimumSiteSizeCells.x), Mathf.Max(1, minimumSiteSizeCells.y));
        public int MinimumFrontageCells => Mathf.Max(0, minimumFrontageCells);
        public int MinimumBuildableAreaCells => Mathf.Max(0, minimumBuildableAreaCells);
        public ReadOnlySpan<PlotZone> AllowedPlotZones => allowedPlotZones ?? Array.Empty<PlotZone>();
        public bool RequiresRoadFrontage => requiresRoadFrontage;

        public bool Meets(TownPlot plot)
        {
            if (plot == null)
            {
                return false;
            }

            Vector2Int minimumSize = MinimumSiteSizeCells;
            if (plot.siteSizeCells.x < minimumSize.x || plot.siteSizeCells.y < minimumSize.y)
            {
                return false;
            }

            if (RequiresRoadFrontage && plot.frontageCells <= 0)
            {
                return false;
            }

            if (plot.frontageCells < MinimumFrontageCells)
            {
                return false;
            }

            int requiredBuildableArea = MinimumBuildableAreaCells;
            if (requiredBuildableArea > 0)
            {
                int buildableArea = plot.candidateFootprint.IsValid ? plot.candidateFootprint.Area : plot.bounds.Area;
                if (buildableArea < requiredBuildableArea)
                {
                    return false;
                }
            }

            ReadOnlySpan<PlotZone> zones = AllowedPlotZones;
            if (zones.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i] == plot.zone)
                {
                    return true;
                }

                if (zones[i] == PlotZone.Agricultural || plot.zone == PlotZone.Agricultural)
                {
                    continue;
                }

                if (zones[i] == PlotZone.MixedUse || plot.zone == PlotZone.MixedUse)
                {
                    return true;
                }
            }

            return false;
        }
    }

    [Serializable]
    public sealed class PlayerDevelopmentFitResult
    {
        public bool canProceed = true;
        public float fitScore01 = 1f;
        public float costMultiplier = 1f;
        public string reason = "Allowed on owned land.";
        public List<string> warnings = new();

        public bool HasWarnings => warnings != null && warnings.Count > 0;

        public string WarningSummary
        {
            get
            {
                if (!HasWarnings)
                {
                    return string.Empty;
                }

                return string.Join("; ", warnings);
            }
        }

        public static PlayerDevelopmentFitResult Blocked(string reason)
        {
            return new PlayerDevelopmentFitResult
            {
                canProceed = false,
                fitScore01 = 0f,
                costMultiplier = 1f,
                reason = string.IsNullOrWhiteSpace(reason) ? "Missing development requirement." : reason
            };
        }

        public static PlayerDevelopmentFitResult Allowed(float fitScore01, List<string> warnings)
        {
            float score = Mathf.Clamp(fitScore01, 0.35f, 1f);
            PlayerDevelopmentFitResult result = new()
            {
                canProceed = true,
                fitScore01 = score,
                costMultiplier = Mathf.Clamp(1f + (1f - score) * 0.65f, 1f, 1.45f),
                warnings = warnings ?? new List<string>()
            };

            result.reason = result.HasWarnings
                ? $"Allowed on owned land with fit warnings: {result.WarningSummary}."
                : "Recommended fit for this owned site.";
            return result;
        }
    }

    public static class BusinessSiteSuitabilityEvaluator
    {
        public static bool CanOperate(BusinessProfileDefinition profile, PlacedBuilding building, TownPlot plot)
        {
            return CanOperate(profile, building, plot, out _);
        }

        public static bool CanOperate(BusinessProfileDefinition profile, PlacedBuilding building, TownPlot plot, out string reason)
        {
            if (profile == null)
            {
                reason = "Missing business profile.";
                return false;
            }

            if (building == null || building.definition == null)
            {
                reason = "Missing building shell.";
                return false;
            }

            if (plot == null)
            {
                reason = "Missing business site plot.";
                return false;
            }

            BusinessType businessType = profile.Business.BusinessType;
            bool agriculturalSiteMatch = IsMatchingAgriculturalBusinessSite(businessType, building.definition, plot);
            if (!agriculturalSiteMatch && !building.definition.IsSuitableForBusiness(businessType))
            {
                reason = $"{building.definition.DisplayName} is not suitable for {profile.Business.DisplayName}.";
                return false;
            }

            if (!profile.SiteRequirements.Meets(plot))
            {
                reason = $"{profile.Business.DisplayName} site requirements are not met by Plot {plot.id:000}.";
                return false;
            }

            reason = "Business shell and site requirements met.";
            return true;
        }

        public static bool TryEvaluatePlayerOwnedDevelopmentFit(
            BusinessProfileDefinition profile,
            PlacedBuilding building,
            TownPlot plot,
            out PlayerDevelopmentFitResult fit)
        {
            if (profile == null)
            {
                fit = PlayerDevelopmentFitResult.Blocked("Missing business profile.");
                return false;
            }

            if (building == null)
            {
                fit = PlayerDevelopmentFitResult.Blocked("Missing building shell.");
                return false;
            }

            if (building.definition == null)
            {
                fit = PlayerDevelopmentFitResult.Blocked($"Building {building.id:000} is missing its shell definition.");
                return false;
            }

            if (plot == null)
            {
                fit = PlayerDevelopmentFitResult.Blocked("Missing business site plot.");
                return false;
            }

            List<string> warnings = new();
            float score = 1f;
            BusinessType businessType = profile.Business.BusinessType;
            string businessName = profile.Business.DisplayName;
            string shellName = building.definition.DisplayName;
            BusinessSiteRequirementDefinition requirements = profile.SiteRequirements;
            bool agriculturalBusiness = TryGetRequiredAgriculturalSiteRole(businessType, out _);
            bool agriculturalSiteMatch = IsMatchingAgriculturalBusinessSite(businessType, building.definition, plot);

            if (agriculturalBusiness)
            {
                if (!agriculturalSiteMatch)
                {
                    fit = PlayerDevelopmentFitResult.Blocked($"{businessName} requires a matching agricultural parcel.");
                    return false;
                }

                if (!requirements.Meets(plot))
                {
                    fit = PlayerDevelopmentFitResult.Blocked($"{businessName} site requirements are not met by Plot {plot.id:000}.");
                    return false;
                }
            }

            if (!building.definition.CanHostWorkplace)
            {
                AddWarning(
                    warnings,
                    $"{shellName} was originally residential/non-workplace; fit-out needs adaptation",
                    0.22f,
                    ref score);
            }
            else if (!agriculturalSiteMatch && !building.definition.IsSuitableForBusiness(businessType))
            {
                AddWarning(
                    warnings,
                    $"{shellName} was not recommended for {businessName}; workflow and fixtures need adaptation",
                    0.18f,
                    ref score);
            }

            if (!PlotZoneMatchesRecommendation(requirements, plot.zone))
            {
                AddWarning(
                    warnings,
                    $"Plot {plot.id:000} was seeded as {plot.zone}; {businessName} prefers {BuildAllowedZoneSummary(requirements)}",
                    0.16f,
                    ref score);
            }

            Vector2Int minimumSize = requirements.MinimumSiteSizeCells;
            if (plot.siteSizeCells.x < minimumSize.x || plot.siteSizeCells.y < minimumSize.y)
            {
                AddWarning(
                    warnings,
                    $"site {plot.siteSizeCells.x}x{plot.siteSizeCells.y} is below the recommended {minimumSize.x}x{minimumSize.y}",
                    0.18f,
                    ref score);
            }

            if (requirements.RequiresRoadFrontage && plot.frontageCells <= 0)
            {
                AddWarning(
                    warnings,
                    "no road frontage; customer and delivery access will be awkward",
                    0.18f,
                    ref score);
            }
            else if (plot.frontageCells < requirements.MinimumFrontageCells)
            {
                AddWarning(
                    warnings,
                    $"frontage {plot.frontageCells} is below the recommended {requirements.MinimumFrontageCells}",
                    0.12f,
                    ref score);
            }

            int requiredBuildableArea = requirements.MinimumBuildableAreaCells;
            if (requiredBuildableArea > 0)
            {
                int buildableArea = plot.candidateFootprint.IsValid ? plot.candidateFootprint.Area : plot.bounds.Area;
                if (buildableArea < requiredBuildableArea)
                {
                    AddWarning(
                        warnings,
                        $"buildable area {buildableArea} cells is below the recommended {requiredBuildableArea}",
                        0.14f,
                        ref score);
                }
            }

            fit = PlayerDevelopmentFitResult.Allowed(score, warnings);
            return true;
        }

        private static void AddWarning(List<string> warnings, string warning, float penalty, ref float score)
        {
            if (!string.IsNullOrWhiteSpace(warning))
            {
                warnings.Add(warning);
            }

            score -= Mathf.Max(0f, penalty);
        }

        private static bool IsMatchingAgriculturalBusinessSite(BusinessType businessType, BuildingDefinition definition, TownPlot plot)
        {
            if (!TryGetRequiredAgriculturalSiteRole(businessType, out AgriculturalSiteRole requiredRole)
                || definition == null
                || !definition.CanHostWorkplace
                || plot == null
                || plot.zone != PlotZone.Agricultural)
            {
                return false;
            }

            return ResolveAgriculturalSiteRole(plot, definition) == requiredRole;
        }

        private static AgriculturalSiteRole ResolveAgriculturalSiteRole(TownPlot plot, BuildingDefinition definition)
        {
            if (plot != null && plot.agriculturalSiteRole != AgriculturalSiteRole.None)
            {
                return plot.agriculturalSiteRole;
            }

            return definition != null ? definition.AgriculturalSiteRole : AgriculturalSiteRole.None;
        }

        private static bool TryGetRequiredAgriculturalSiteRole(BusinessType businessType, out AgriculturalSiteRole role)
        {
            switch (businessType)
            {
                case BusinessType.CropFarm:
                    role = AgriculturalSiteRole.CropProductionYard;
                    return true;
                case BusinessType.Ranch:
                    role = AgriculturalSiteRole.LivestockYard;
                    return true;
                case BusinessType.Sawmill:
                    role = AgriculturalSiteRole.SawmillYard;
                    return true;
                default:
                    role = AgriculturalSiteRole.None;
                    return false;
            }
        }

        private static bool PlotZoneMatchesRecommendation(BusinessSiteRequirementDefinition requirements, PlotZone plotZone)
        {
            ReadOnlySpan<PlotZone> zones = requirements.AllowedPlotZones;
            if (zones.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i] == plotZone)
                {
                    return true;
                }

                if (zones[i] == PlotZone.Agricultural || plotZone == PlotZone.Agricultural)
                {
                    continue;
                }

                if (zones[i] == PlotZone.MixedUse || plotZone == PlotZone.MixedUse)
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildAllowedZoneSummary(BusinessSiteRequirementDefinition requirements)
        {
            ReadOnlySpan<PlotZone> zones = requirements.AllowedPlotZones;
            if (zones.Length == 0)
            {
                return "any zone";
            }

            string[] labels = new string[zones.Length];
            for (int i = 0; i < zones.Length; i++)
            {
                labels[i] = zones[i].ToString();
            }

            return string.Join("/", labels);
        }
    }

    [CreateAssetMenu(
        fileName = "BusinessProfileDefinition",
        menuName = "Land & Ledgers/Economy/Business Profile Definition",
        order = 181)]
    public class BusinessProfileDefinition : ScriptableObject
    {
        [Header("Business")]
        [SerializeField]
        private BusinessDefinition business = new();

        [Header("Category-First Slots")]
        [SerializeField]
        private ItemCategoryDefinition[] categories = Array.Empty<ItemCategoryDefinition>();

        [Header("Starter Goods / Inputs / Services")]
        [SerializeField]
        private ItemDefinition[] items = Array.Empty<ItemDefinition>();

        [Header("Site Requirements")]
        [SerializeField]
        private BusinessSiteRequirementDefinition siteRequirements = new();

        [Header("Main Focus")]
        [SerializeField]
        private BusinessMainFocusDefinition mainFocus = new();

        [Header("Shared Throughput")]
        [SerializeField]
        private BusinessArchetype archetype = BusinessArchetype.Unspecified;

        [SerializeField]
        private BusinessThroughputMode throughputMode = BusinessThroughputMode.Retail;

        [SerializeField, Min(0)]
        private int baselineWeeklyThroughputUnits = 12;

        [SerializeField, Min(0)]
        private int baselineDailyServiceCapacity = 0;

        [SerializeField]
        private string[] inputCategoryIds = Array.Empty<string>();

        [SerializeField]
        private string[] outputCategoryIds = Array.Empty<string>();

        [SerializeField]
        private string[] serviceCategoryIds = Array.Empty<string>();

        public BusinessDefinition Business => business;
        public ReadOnlySpan<ItemCategoryDefinition> Categories => categories ?? Array.Empty<ItemCategoryDefinition>();
        public ReadOnlySpan<ItemDefinition> Items => items ?? Array.Empty<ItemDefinition>();
        public BusinessSiteRequirementDefinition SiteRequirements => siteRequirements ?? new BusinessSiteRequirementDefinition();
        public BusinessMainFocusState MainFocus => BusinessMainFocusState.FromDefinition(mainFocus, business.BusinessType);
        public BusinessArchetype Archetype => ResolveArchetype(archetype, throughputMode, business != null ? business.BusinessType : BusinessType.GeneralStore);
        public BusinessThroughputMode ThroughputMode => throughputMode;
        public int BaselineWeeklyThroughputUnits => Mathf.Max(0, baselineWeeklyThroughputUnits);
        public int BaselineDailyServiceCapacity => Mathf.Max(0, baselineDailyServiceCapacity);
        public ReadOnlySpan<string> InputCategoryIds => inputCategoryIds ?? Array.Empty<string>();
        public ReadOnlySpan<string> OutputCategoryIds => outputCategoryIds ?? Array.Empty<string>();
        public ReadOnlySpan<string> ServiceCategoryIds => serviceCategoryIds ?? Array.Empty<string>();

        public BusinessRuntimeState CreateRuntimeState()
        {
            return BusinessRuntimeState.CreateFrom(
                business,
                categories ?? Array.Empty<ItemCategoryDefinition>(),
                items ?? Array.Empty<ItemDefinition>(),
                MainFocus,
                throughputMode,
                BaselineWeeklyThroughputUnits,
                BaselineDailyServiceCapacity);
        }

        /// <summary>
        /// BIZ-1: code fallback profile for business types without an authored
        /// ScriptableObject profile, so any of the 19 BusinessTypes is creatable
        /// through the formation workflow (Canon §3.1).
        /// </summary>
        public static BusinessProfileDefinition CreateFallback(BusinessType type, string displayName)
        {
            var profile = CreateInstance<BusinessProfileDefinition>();
            profile.business = new BusinessDefinition(type, $"biz_{type}", displayName);
            return profile;
        }

        public ItemCategoryDefinition FindCategory(string categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId) || categories == null)
            {
                return null;
            }

            for (int i = 0; i < categories.Length; i++)
            {
                if (string.Equals(categories[i].CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
                {
                    return categories[i];
                }
            }

            return null;
        }

        public ItemDefinition FindItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId) || items == null)
            {
                return null;
            }

            for (int i = 0; i < items.Length; i++)
            {
                if (string.Equals(items[i].ItemId, itemId, StringComparison.OrdinalIgnoreCase))
                {
                    return items[i];
                }
            }

            return null;
        }

        private static BusinessArchetype ResolveArchetype(BusinessArchetype configured, BusinessThroughputMode mode, BusinessType businessType)
        {
            if (configured != BusinessArchetype.Unspecified)
            {
                if (configured == BusinessArchetype.ServiceThroughput && businessType == BusinessType.BoardingHouse)
                {
                    return BusinessArchetype.Lodging;
                }

                if (configured == BusinessArchetype.ServiceThroughput && businessType == BusinessType.Builder)
                {
                    return BusinessArchetype.ConstructionProject;
                }

                return configured;
            }

            return businessType switch
            {
                BusinessType.BoardingHouse => BusinessArchetype.Lodging,
                BusinessType.Builder => BusinessArchetype.ConstructionProject,
                BusinessType.LiveryFreight => BusinessArchetype.LogisticsMovement,
                BusinessType.Sawmill => BusinessArchetype.SpecialProduction,
                BusinessType.Mine => BusinessArchetype.SpecialProduction,
                _ => mode == BusinessThroughputMode.Service
                    ? BusinessArchetype.ServiceThroughput
                    : BusinessArchetype.GoodsTransformer
            };
        }
    }
}
