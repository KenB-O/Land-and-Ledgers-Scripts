using System;
using System.Collections.Generic;
using System.Text;
using LandLedgers.Civic;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Rivals;
using LandLedgers.Economy.Valuation;
using LandLedgers.FirstLedger;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Progression;
using LandLedgers.Reputation;
using LandLedgers.Time;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy
{
    public enum AcquisitionMarketSection
    {
        Land = 0,
        Businesses = 1
    }

    public enum AcquisitionListingKind
    {
        Land = 0,
        Business = 1
    }

    public enum AcquisitionDealStage
    {
        None = 0,
        Inquiry = 1,
        EarnestCommitted = 2,
        DiligenceComplete = 3,
        TentativeAgreement = 4,
        Closed = 5,
        Failed = 6
    }

    public enum AcquisitionBuyerSeriousness
    {
        Watching = 0,
        Exploring = 1,
        Active = 2,
        Ready = 3
    }

    public enum AcquisitionIntegrationStance
    {
        None = 0,
        Stabilize = 1,
        Repair = 2,
        Reposition = 3,
        Expand = 4
    }

    public enum AcquisitionDiligenceClueKind
    {
        VisibleCondition = 0,
        Staffing = 1,
        SupplierWeakness = 2,
        DemandFit = 3,
        RoughValue = 4,
        Reliability = 5
    }

    public enum AcquisitionListingSource
    {
        InitialMarket = 0,
        DevelopmentInventory = 1,
        PressureRelease = 2,
        OwnerTurnover = 3,
        ServiceShortage = 4,
        RivalAction = 5
    }

    public enum AcquisitionLeadCategory
    {
        Unset = 0,
        PublicListing = 1,
        QuietOpportunity = 2,
        RumorLead = 3,
        EstateSale = 4,
        DistressSale = 5,
        StrategicHoldout = 6
    }

    public enum AcquisitionProofStatus
    {
        NotRequested = 0,
        Requested = 1,
        Presented = 2,
        Verified = 3
    }

    [Flags]
    public enum AcquisitionDiligenceLayer
    {
        None = 0,
        Title = 1 << 0,
        SiteCondition = 1 << 1,
        AccessAndFrontage = 1 << 2,
        Operations = 1 << 3,
        BooksAndReceivables = 1 << 4,
        SupplierExposure = 1 << 5
    }

    public enum AcquisitionClosingFailureCause
    {
        None = 0,
        LenderRetreat = 1,
        TitleDefect = 2,
        SellerWithdrew = 3,
        RivalPressure = 4,
        AdverseDiligence = 5
    }


    public enum ConstructionProjectProgressState
    {
        Queued = 0,
        InProgress = 1,
        Completed = 2,
        Failed = 3,
        WaitingForLane = 4,
        BlockedBeforeStart = 5,
        Stalled = 6
    }

    [Serializable]
    public sealed class ConstructionProjectState
    {
        public string projectId = string.Empty;
        public ConstructionProjectKind projectKind;
        public ConstructionProjectProgressState progressState = ConstructionProjectProgressState.Queued;
        public string title = string.Empty;
        public int plotId = -1;
        public int buildingId = -1;
        public string buildingDefinitionId = string.Empty;
        public bool hasBusinessIntent;
        public BusinessType businessIntent = BusinessType.GeneralStore;
        public int householdUpgradeKind = -1;
        public int queuedWeekKey = -1;
        public int startedWeekKey = -1;
        public int completedWeekKey = -1;
        public int weeksRequired = 1;
        public int weeksProgressed;
        public int estimatedCashCostCents;
        public int plannedLumberUnits;
        public int plannedNailsUnits;
        public int plannedLaborUnits;
        public bool inputsCommitted;
        public string committedInputSummary = string.Empty;
        public string statusText = string.Empty;
        public string blockedReason = string.Empty;
        public string lastWeeklyProgressSummary = string.Empty;
        public int blockedWeeks;
        public int readyWeeks;

        public bool IsResolved => progressState == ConstructionProjectProgressState.Completed || progressState == ConstructionProjectProgressState.Failed;
        public bool IsActive => !IsResolved;
        public bool IsStarted => progressState == ConstructionProjectProgressState.InProgress
            || progressState == ConstructionProjectProgressState.Stalled
            || inputsCommitted;
        public int WeeksRemaining => Mathf.Max(0, Mathf.Max(1, weeksRequired) - Mathf.Max(0, weeksProgressed));

        public ConstructionProjectSaveDto CaptureSaveDto()
        {
            return new ConstructionProjectSaveDto
            {
                projectId = projectId ?? string.Empty,
                projectKind = projectKind,
                progressState = progressState,
                title = title ?? string.Empty,
                plotId = plotId,
                buildingId = buildingId,
                buildingDefinitionId = buildingDefinitionId ?? string.Empty,
                hasBusinessIntent = hasBusinessIntent,
                businessIntent = businessIntent,
                householdUpgradeKind = householdUpgradeKind,
                queuedWeekKey = queuedWeekKey,
                startedWeekKey = startedWeekKey,
                completedWeekKey = completedWeekKey,
                weeksRequired = Mathf.Max(1, weeksRequired),
                weeksProgressed = Mathf.Max(0, weeksProgressed),
                estimatedCashCostCents = Mathf.Max(0, estimatedCashCostCents),
                plannedLumberUnits = Mathf.Max(0, plannedLumberUnits),
                plannedNailsUnits = Mathf.Max(0, plannedNailsUnits),
                plannedLaborUnits = Mathf.Max(0, plannedLaborUnits),
                inputsCommitted = inputsCommitted,
                committedInputSummary = committedInputSummary ?? string.Empty,
                statusText = statusText ?? string.Empty,
                blockedReason = blockedReason ?? string.Empty,
                lastWeeklyProgressSummary = lastWeeklyProgressSummary ?? string.Empty,
                blockedWeeks = Mathf.Max(0, blockedWeeks),
                readyWeeks = Mathf.Max(0, readyWeeks)
            };
        }

        public static ConstructionProjectState FromSaveDto(ConstructionProjectSaveDto dto)
        {
            if (dto == null)
            {
                return null;
            }

            return new ConstructionProjectState
            {
                projectId = dto.projectId ?? string.Empty,
                projectKind = dto.projectKind,
                progressState = dto.progressState,
                title = dto.title ?? string.Empty,
                plotId = dto.plotId,
                buildingId = dto.buildingId,
                buildingDefinitionId = dto.buildingDefinitionId ?? string.Empty,
                hasBusinessIntent = dto.hasBusinessIntent,
                businessIntent = dto.businessIntent,
                householdUpgradeKind = dto.householdUpgradeKind,
                queuedWeekKey = dto.queuedWeekKey,
                startedWeekKey = dto.startedWeekKey,
                completedWeekKey = dto.completedWeekKey,
                weeksRequired = Mathf.Max(1, dto.weeksRequired),
                weeksProgressed = Mathf.Max(0, dto.weeksProgressed),
                estimatedCashCostCents = Mathf.Max(0, dto.estimatedCashCostCents),
                plannedLumberUnits = Mathf.Max(0, dto.plannedLumberUnits),
                plannedNailsUnits = Mathf.Max(0, dto.plannedNailsUnits),
                plannedLaborUnits = Mathf.Max(0, dto.plannedLaborUnits),
                inputsCommitted = dto.inputsCommitted,
                committedInputSummary = dto.committedInputSummary ?? string.Empty,
                statusText = dto.statusText ?? string.Empty,
                blockedReason = dto.blockedReason ?? string.Empty,
                lastWeeklyProgressSummary = dto.lastWeeklyProgressSummary ?? string.Empty,
                blockedWeeks = Mathf.Max(0, dto.blockedWeeks),
                readyWeeks = Mathf.Max(0, dto.readyWeeks)
            };
        }
    }

    [Serializable]
    public sealed class AcquisitionListing
    {
        public AcquisitionListingKind kind;
        public string listingId;
        public int plotId = -1;
        public int buildingId = -1;
        public string businessInstanceId;
        public BusinessType businessType;
        public string businessDisplayName;
        public string businessTypeDisplayName;
        public string ownerDisplayName;
        public string buildingDisplayName;
        public string title;
        public string reason;
        public int askingPriceCents;
        public PlotZone plotZone;
        public Vector2Int siteSizeCells;
        public int frontageCells;
        public bool improved;
        public bool playerOwned;
        public string detail;
        public int firstSeenDayIndex = -1;
        public int playerFirstUntilDayIndex = -1;
        public int ageDays;
        public float pressure01;
        public bool aiEligible;
        public AcquisitionListingSource source = AcquisitionListingSource.InitialMarket;
        public string sourceReason = string.Empty;
    }

    public sealed class AcquisitionSellerMeetingUnavailableReadout
    {
        public string Title = string.Empty;
        public string Stage = string.Empty;
        public string SellerIdentity = string.Empty;
        public string SelectedFile = string.Empty;
        public string ProcessAction = string.Empty;
        public string SellerLine = string.Empty;
        public string DialoguePrompt = string.Empty;
        public string Posture = string.Empty;
        public string Readiness = string.Empty;
        public string ResponseReadout = string.Empty;
        public string PrimaryActionLabel = "No Action";
    }

    [Serializable]
    public sealed class AcquisitionOpportunityWindowState
    {
        public string listingId = string.Empty;
        public AcquisitionListingKind kind;
        public int firstSeenDayIndex = -1;
        public int playerFirstUntilDayIndex = -1;
        public float pressure01;
        public string sourceReason = string.Empty;
        public bool aiEligible;

        public AcquisitionOpportunityWindowSaveDto CaptureSaveDto()
        {
            return new AcquisitionOpportunityWindowSaveDto
            {
                listingId = listingId ?? string.Empty,
                kind = kind,
                firstSeenDayIndex = firstSeenDayIndex,
                playerFirstUntilDayIndex = playerFirstUntilDayIndex,
                pressure01 = Mathf.Clamp01(pressure01),
                sourceReason = sourceReason ?? string.Empty,
                aiEligible = aiEligible
            };
        }

        public static AcquisitionOpportunityWindowState FromSaveDto(AcquisitionOpportunityWindowSaveDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.listingId))
            {
                return null;
            }

            return new AcquisitionOpportunityWindowState
            {
                listingId = dto.listingId,
                kind = dto.kind,
                firstSeenDayIndex = dto.firstSeenDayIndex,
                playerFirstUntilDayIndex = dto.playerFirstUntilDayIndex,
                pressure01 = Mathf.Clamp01(dto.pressure01),
                sourceReason = dto.sourceReason ?? string.Empty,
                aiEligible = dto.aiEligible
            };
        }
    }

    [Serializable]
    public sealed class AcquisitionDealState
    {
        public AcquisitionDealStage stage = AcquisitionDealStage.None;
        public AcquisitionListingKind kind;
        public string listingId = string.Empty;
        public int plotId = -1;
        public int buildingId = -1;
        public string title = string.Empty;
        public SellerMotive sellerMotive = SellerMotive.Holding;
        public float sellerPressure01;
        public float sellerRelationshipSensitivity01;
        public int inquiryDayIndex = -1;
        public AcquisitionBuyerSeriousness seriousness = AcquisitionBuyerSeriousness.Exploring;
        public string scoutingSummary = string.Empty;
        public int earnestMoneyCents;
        public int optionDeadlineDayIndex = -1;
        public int diligenceDayIndex = -1;
        public int estimatedValueCents;
        public int valuationBandLowCents;
        public int valuationBandHighCents;
        public float diligenceScore01;
        public string diligenceSummary = string.Empty;
        public int tentativeAgreementDayIndex = -1;
        public int tentativePriceCents;
        public AcquisitionIntegrationStance integrationStance = AcquisitionIntegrationStance.None;
        public int closingDeadlineDayIndex = -1;
        public int financingNeedCents;
        public float closingRisk01;
        public bool financingContingencyPresent = true;
        public string statusText = string.Empty;
        public int lastProgressDayIndex = -1;
        public int weeklyReviewCount;
        public int stalledReviewCount;
        public int revealedDiligenceMask;
        public string quickDiligenceLedger = string.Empty;
        public int lastQuickDiligenceDayIndex = -1;
        public AcquisitionLeadCategory leadCategory = AcquisitionLeadCategory.Unset;
        public string leadQualitySummary = string.Empty;
        public string proofOfFundsSummary = string.Empty;
        public string diligenceLayerSummary = string.Empty;
        public string closingRiskSummary = string.Empty;
        public AcquisitionProofStatus proofStatus = AcquisitionProofStatus.NotRequested;
        public bool proofRequired;
        public int proofPresentedDayIndex = -1;
        public int requiredDiligenceMask;
        public int completedDiligenceMask;
        public AcquisitionClosingFailureCause closingFailureCause = AcquisitionClosingFailureCause.None;
        public string closingFailureSummary = string.Empty;
        public AcquisitionDealStage failedFromStage = AcquisitionDealStage.None;

        public AcquisitionDealSaveDto CaptureSaveDto()
        {
            return new AcquisitionDealSaveDto
            {
                stage = stage,
                kind = kind,
                listingId = listingId ?? string.Empty,
                plotId = plotId,
                buildingId = buildingId,
                title = title ?? string.Empty,
                sellerMotive = sellerMotive,
                sellerPressure01 = Mathf.Clamp01(sellerPressure01),
                sellerRelationshipSensitivity01 = Mathf.Clamp01(sellerRelationshipSensitivity01),
                inquiryDayIndex = inquiryDayIndex,
                seriousness = seriousness,
                scoutingSummary = scoutingSummary ?? string.Empty,
                earnestMoneyCents = Mathf.Max(0, earnestMoneyCents),
                optionDeadlineDayIndex = optionDeadlineDayIndex,
                diligenceDayIndex = diligenceDayIndex,
                estimatedValueCents = Mathf.Max(0, estimatedValueCents),
                valuationBandLowCents = Mathf.Max(0, valuationBandLowCents),
                valuationBandHighCents = Mathf.Max(0, valuationBandHighCents),
                diligenceScore01 = Mathf.Clamp01(diligenceScore01),
                diligenceSummary = diligenceSummary ?? string.Empty,
                tentativeAgreementDayIndex = tentativeAgreementDayIndex,
                tentativePriceCents = Mathf.Max(0, tentativePriceCents),
                integrationStance = integrationStance,
                closingDeadlineDayIndex = closingDeadlineDayIndex,
                financingNeedCents = Mathf.Max(0, financingNeedCents),
                closingRisk01 = Mathf.Clamp01(closingRisk01),
                financingContingencyPresent = financingContingencyPresent,
                statusText = statusText ?? string.Empty,
                lastProgressDayIndex = lastProgressDayIndex,
                weeklyReviewCount = Mathf.Max(0, weeklyReviewCount),
                stalledReviewCount = Mathf.Max(0, stalledReviewCount),
                revealedDiligenceMask = revealedDiligenceMask,
                quickDiligenceLedger = quickDiligenceLedger ?? string.Empty,
                lastQuickDiligenceDayIndex = lastQuickDiligenceDayIndex,
                leadCategory = leadCategory,
                leadQualitySummary = leadQualitySummary ?? string.Empty,
                proofOfFundsSummary = proofOfFundsSummary ?? string.Empty,
                diligenceLayerSummary = diligenceLayerSummary ?? string.Empty,
                closingRiskSummary = closingRiskSummary ?? string.Empty,
                proofStatus = proofStatus,
                proofRequired = proofRequired,
                proofPresentedDayIndex = proofPresentedDayIndex,
                requiredDiligenceMask = requiredDiligenceMask,
                completedDiligenceMask = completedDiligenceMask,
                closingFailureCause = closingFailureCause,
                closingFailureSummary = EncodeFailureSummaryForSave(closingFailureSummary, failedFromStage)
            };
        }

        public static AcquisitionDealState FromSaveDto(AcquisitionDealSaveDto dto)
        {
            if (dto == null)
            {
                return null;
            }

            return new AcquisitionDealState
            {
                stage = dto.stage,
                kind = dto.kind,
                listingId = dto.listingId ?? string.Empty,
                plotId = dto.plotId,
                buildingId = dto.buildingId,
                title = dto.title ?? string.Empty,
                sellerMotive = dto.sellerMotive,
                sellerPressure01 = Mathf.Clamp01(dto.sellerPressure01),
                sellerRelationshipSensitivity01 = Mathf.Clamp01(dto.sellerRelationshipSensitivity01),
                inquiryDayIndex = dto.inquiryDayIndex,
                seriousness = dto.seriousness,
                scoutingSummary = dto.scoutingSummary ?? string.Empty,
                earnestMoneyCents = Mathf.Max(0, dto.earnestMoneyCents),
                optionDeadlineDayIndex = dto.optionDeadlineDayIndex,
                diligenceDayIndex = dto.diligenceDayIndex,
                estimatedValueCents = Mathf.Max(0, dto.estimatedValueCents),
                valuationBandLowCents = Mathf.Max(0, dto.valuationBandLowCents),
                valuationBandHighCents = Mathf.Max(0, dto.valuationBandHighCents),
                diligenceScore01 = Mathf.Clamp01(dto.diligenceScore01),
                diligenceSummary = dto.diligenceSummary ?? string.Empty,
                tentativeAgreementDayIndex = dto.tentativeAgreementDayIndex,
                tentativePriceCents = Mathf.Max(0, dto.tentativePriceCents),
                integrationStance = dto.integrationStance,
                closingDeadlineDayIndex = dto.closingDeadlineDayIndex,
                financingNeedCents = Mathf.Max(0, dto.financingNeedCents),
                closingRisk01 = Mathf.Clamp01(dto.closingRisk01),
                financingContingencyPresent = dto.financingContingencyPresent,
                statusText = dto.statusText ?? string.Empty,
                lastProgressDayIndex = dto.lastProgressDayIndex,
                weeklyReviewCount = Mathf.Max(0, dto.weeklyReviewCount),
                stalledReviewCount = Mathf.Max(0, dto.stalledReviewCount),
                revealedDiligenceMask = dto.revealedDiligenceMask,
                quickDiligenceLedger = dto.quickDiligenceLedger ?? string.Empty,
                lastQuickDiligenceDayIndex = dto.lastQuickDiligenceDayIndex,
                leadCategory = dto.leadCategory,
                leadQualitySummary = dto.leadQualitySummary ?? string.Empty,
                proofOfFundsSummary = dto.proofOfFundsSummary ?? string.Empty,
                diligenceLayerSummary = dto.diligenceLayerSummary ?? string.Empty,
                closingRiskSummary = dto.closingRiskSummary ?? string.Empty,
                proofStatus = dto.proofStatus,
                proofRequired = dto.proofRequired,
                proofPresentedDayIndex = dto.proofPresentedDayIndex,
                requiredDiligenceMask = dto.requiredDiligenceMask,
                completedDiligenceMask = dto.completedDiligenceMask,
                closingFailureCause = dto.closingFailureCause,
                closingFailureSummary = StripFailureSummaryMetadata(dto.closingFailureSummary),
                failedFromStage = InferFailedFromStage(dto)
            };
        }

        // Preserve the failed breakpoint through the existing summary field so saves remain compatible
        // with the current DTO surface in this lane.
        private static string EncodeFailureSummaryForSave(string summary, AcquisitionDealStage failedFromStage)
        {
            string cleanSummary = StripFailureSummaryMetadata(summary);
            if (failedFromStage == AcquisitionDealStage.None)
            {
                return cleanSummary;
            }

            return $"@@FAIL_STAGE={failedFromStage}@@{cleanSummary}";
        }

        private static string StripFailureSummaryMetadata(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
            {
                return string.Empty;
            }

            string trimmed = summary.Trim();
            if (!trimmed.StartsWith("@@FAIL_STAGE=", StringComparison.Ordinal))
            {
                return trimmed;
            }

            int markerEnd = trimmed.IndexOf("@@", "@@FAIL_STAGE=".Length, StringComparison.Ordinal);
            if (markerEnd < 0)
            {
                return trimmed;
            }

            return trimmed.Substring(markerEnd + 2).TrimStart();
        }

        private static AcquisitionDealStage ParseFailureStageMetadata(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
            {
                return AcquisitionDealStage.None;
            }

            string trimmed = summary.Trim();
            if (!trimmed.StartsWith("@@FAIL_STAGE=", StringComparison.Ordinal))
            {
                return AcquisitionDealStage.None;
            }

            int markerEnd = trimmed.IndexOf("@@", "@@FAIL_STAGE=".Length, StringComparison.Ordinal);
            if (markerEnd < 0)
            {
                return AcquisitionDealStage.None;
            }

            string stageToken = trimmed.Substring("@@FAIL_STAGE=".Length, markerEnd - "@@FAIL_STAGE=".Length);
            return Enum.TryParse(stageToken, true, out AcquisitionDealStage parsedStage)
                ? parsedStage
                : AcquisitionDealStage.None;
        }

        private static AcquisitionDealStage InferFailedFromStage(AcquisitionDealSaveDto dto)
        {
            if (dto == null || dto.stage != AcquisitionDealStage.Failed)
            {
                return AcquisitionDealStage.None;
            }

            AcquisitionDealStage parsedStage = ParseFailureStageMetadata(dto.closingFailureSummary);
            if (parsedStage != AcquisitionDealStage.None)
            {
                return parsedStage;
            }

            string status = dto.statusText ?? string.Empty;
            string failureSummary = StripFailureSummaryMetadata(dto.closingFailureSummary);
            string combined = $"{failureSummary} {status}";
            if (combined.IndexOf("before earnest", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return AcquisitionDealStage.Inquiry;
            }

            if (combined.IndexOf("before diligence", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("diligence finished", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return AcquisitionDealStage.EarnestCommitted;
            }

            if (combined.IndexOf("before terms", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("terms were agreed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return AcquisitionDealStage.DiligenceComplete;
            }

            if (combined.IndexOf("closing", StringComparison.OrdinalIgnoreCase) >= 0
                || dto.tentativeAgreementDayIndex >= 0)
            {
                return AcquisitionDealStage.TentativeAgreement;
            }

            bool formalDiligenceComplete = dto.requiredDiligenceMask != 0
                && (dto.completedDiligenceMask & dto.requiredDiligenceMask) == dto.requiredDiligenceMask;
            if (formalDiligenceComplete || dto.diligenceDayIndex >= 0)
            {
                return AcquisitionDealStage.DiligenceComplete;
            }

            return dto.inquiryDayIndex >= 0
                ? AcquisitionDealStage.Inquiry
                : AcquisitionDealStage.None;
        }
    }

    [Serializable]
    public sealed class OwnedPlotBuildabilityState
    {
        public int plotId = -1;
        public PlotZone plotZone;
        public Vector2Int siteSizeCells;
        public int frontageCells;
        public bool playerOwned;
        public bool empty;
        public bool buildable;
        public string reason;
        public List<BuildingDefinition> buildOptions = new();
    }

    [Serializable]
    public sealed class BusinessActivationCandidateState
    {
        public BusinessType businessType;
        public string displayName;
        public int startupCostCents;
        public bool eligible;
        public string reason;
        public float fitScore01 = 1f;
        public float costMultiplier = 1f;
        public string fitWarningSummary;
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(285)]
    public sealed class AcquisitionMarketManager : MonoBehaviour
    {
        private const string DefaultProfilesResourcePath = "Core/Economy/BusinessProfiles";
        private const string DefaultConstructionSupportResourcePath = "Core/Economy/ConstructionSupportNodes";
        private const string DefaultBlacksmithHardwareCategoryId = "tools_hardware";
        private const string DefaultLumberCategoryId = "lumber";
        private const string DefaultRegionalHardwareFallbackSourceLabel = "Regional freight hardware";
        private const string DefaultRegionalHardwareFallbackLeadTimeLabel = "1 week freight lead";

        private static readonly BusinessType[] ActivationBusinessTypes =
        {
            BusinessType.GeneralStore,
            BusinessType.Blacksmith,
            BusinessType.Butcher,
            BusinessType.Ranch,
            BusinessType.CropFarm,
            BusinessType.Sawmill,
            BusinessType.LumberYard,
            BusinessType.BoardingHouse,
            BusinessType.LiveryFreight,
            BusinessType.Builder,
            BusinessType.FuelDealer,
            BusinessType.GrainMill,
            BusinessType.Bakery,
            BusinessType.Tailor,
            BusinessType.Saloon,
            BusinessType.Barber,
            BusinessType.Wheelwright
        };

        [Header("Sources")]
        [SerializeField]
        private TownWorldController townWorld;

        [SerializeField]
        private GeneralStoreRuntimeManager playerCashSource;

        [SerializeField]
        private PlayerPortfolioManager playerPortfolio;

        [SerializeField]
        private PlayerDebtManager playerDebtManager;

        [SerializeField]
        private SharedBusinessRuntimeManager sharedBusinessRuntime;

        [SerializeField]
        private PopulationManager populationManager;

        [SerializeField]
        private TimeManager timeManager;

        [SerializeField]
        private CivicFoundationManager civicFoundation;

        [SerializeField]
        private OpportunityPressureRuntimeManager opportunityPressureRuntime;

        [Header("Listing Caps")]
        [SerializeField, Range(1, 8)]
        private int maxLandListings = 4;

        [SerializeField, Range(0, 6)]
        private int maxBusinessListings = 2;

        [SerializeField]
        private int marketSeed = 1902;

        [Header("Land Pricing")]
        [SerializeField, Min(1)]
        private int baseLandPricePerCellCents = 55;

        [SerializeField, Min(0)]
        private int roadFrontagePremiumPerCellCents = 22;

        [SerializeField, Range(0f, 2f)]
        private float businessDistrictLandMultiplier = 1.35f;

        [SerializeField, Range(0f, 2f)]
        private float residentialLandMultiplier = 1f;

        [Header("Business Pricing")]
        [SerializeField, Min(1)]
        private int buildingValuePerFootprintCellCents = 420;

        [SerializeField, Min(0)]
        private int businessIncomeProxyCents = 12500;

        [SerializeField, Range(0.25f, 2f)]
        private float mixedUseBusinessMultiplier = 1.12f;

        [Header("Construction Inputs")]
        [SerializeField]
        private ConstructionSupportNodeDefinition[] constructionSupportDefinitions = Array.Empty<ConstructionSupportNodeDefinition>();

        [SerializeField, Min(0)]
        private int blacksmithHardwareUnitCostCents = 250;

        [SerializeField, Min(1f)]
        private float regionalHardwareFallbackUnitCostMultiplier = 1.75f;

        [SerializeField]
        private string regionalHardwareFallbackSourceLabel = DefaultRegionalHardwareFallbackSourceLabel;

        [SerializeField]
        private string regionalHardwareFallbackLeadTimeLabel = DefaultRegionalHardwareFallbackLeadTimeLabel;

        [SerializeField, Min(0)]
        private int laborUnitCostCents = 65;

        [SerializeField, Min(1)]
        private int laborUnitsPerEligibleWorker = 8;

        [SerializeField]
        private string blacksmithHardwareCategoryId = DefaultBlacksmithHardwareCategoryId;

        [Header("Runtime Listings")]
        [SerializeField]
        private List<AcquisitionListing> landListings = new();

        [SerializeField]
        private List<AcquisitionListing> businessListings = new();

        [SerializeField]
        private List<int> ownedPlotIds = new();

        [SerializeField]
        private List<int> ownedBusinessBuildingIds = new();

        [SerializeField]
        private List<LandAppreciationState> ownedLandAppreciations = new();

        [SerializeField]
        private List<AcquisitionDealState> activeDeals = new();

        [SerializeField]
        private List<string> watchedListingIds = new();

        [SerializeField]
        private List<AcquisitionOpportunityWindowState> opportunityWindows = new();

        [SerializeField]
        private int retailPressureStreakWeeks;

        [SerializeField]
        private int housingPressureStreakWeeks;

        [SerializeField]
        private int laborPressureStreakWeeks;

        [SerializeField]
        private int servicePressureStreakWeeks;

        [SerializeField]
        private int expansionPressureStreakWeeks;

        [SerializeField]
        private int developmentInventoryPressure;

        [SerializeField, TextArea(2, 5)]
        private string lastTownActionSummary = string.Empty;

        [SerializeField]
        private List<OwnedPlotBuildabilityState> ownedBuildablePlots = new();

        [SerializeField]
        private List<ConstructionSupportNodeState> constructionSupportNodes = new();

        [SerializeField]
        private List<ConstructionProjectState> activeConstructionProjects = new();

        [Header("Ownership Aptitude")]
        [SerializeField]
        private OwnershipAptitudeState ownershipAptitude = new();

        [SerializeField]
        private int selectedLandIndex;

        [SerializeField]
        private int selectedBusinessIndex;

        [SerializeField]
        private string marketStatus = "Not built.";

        [SerializeField, TextArea(2, 6)]
        private string lastPurchaseSummary = "No acquisitions yet.";

        [SerializeField, TextArea(2, 6)]
        private string lastConstructionSupportSummary = "No construction support yet.";

        [SerializeField, TextArea(2, 6)]
        private string lastAcquisitionReviewSummary = "No weekly acquisition review yet.";

        [SerializeField, TextArea(2, 6)]
        private string lastConstructionQueueSummary = "No active construction queue.";

        [SerializeField]
        private int nextConstructionProjectSerial = 1;

        private bool marketBuilt;
        private bool subscribedToTime;

#if UNITY_EDITOR
        public bool ForceLateConstructionFailureForTests { get; set; }
        public bool ForceLateBusinessStartFailureForTests { get; set; }
#endif
        private readonly LandAppreciationEvaluator landAppreciationEvaluator = new();
        private readonly OwnershipAptitudeEvaluator ownershipAptitudeEvaluator = new();

        public IReadOnlyList<AcquisitionListing> LandListings => landListings;
        public IReadOnlyList<AcquisitionListing> BusinessListings => businessListings;
        public int OwnedLandCount => ownedPlotIds.Count;
        public int OwnedBusinessCount => ownedBusinessBuildingIds.Count;
        public IReadOnlyList<AcquisitionDealState> ActiveDeals => activeDeals;
        public IReadOnlyList<ConstructionProjectState> ActiveConstructionProjects => activeConstructionProjects;
        public IReadOnlyList<string> WatchedListingIds => watchedListingIds;
        public string LastTownActionSummary => lastTownActionSummary ?? string.Empty;
        public IReadOnlyList<LandAppreciationState> OwnedLandAppreciations
        {
            get
            {
                AutoWire();
                if (townWorld == null || townWorld.Grid == null)
                {
                    return ownedLandAppreciations;
                }

                EnsureMarket();
                RefreshOwnedLandAppreciations();
                return ownedLandAppreciations;
            }
        }
        public int CalculateOwnedAssetValueCents()
        {
            AutoWire();
            if (townWorld == null || townWorld.Grid == null)
            {
                return 0;
            }

            EnsureMarket();
            RefreshOwnedLandAppreciations();
            int total = 0;
            for (int i = 0; i < ownedLandAppreciations.Count; i++)
            {
                LandAppreciationState state = ownedLandAppreciations[i];
                if (state == null)
                {
                    continue;
                }

                TownPlot plot = GetPlot(state.plotId);
                PlacedBuilding building = GetBuilding(state.buildingId >= 0 ? state.buildingId : plot != null ? plot.buildingId : -1);
                if (IsPlayerOwnedPlot(plot) || IsPlayerOwnedBusiness(building))
                {
                    total += Mathf.Max(state.currentEstimatedValueCents, state.TotalBasisCents);
                }
            }

            return Mathf.Max(0, total);
        }

        public bool TryEstimatePlayerBusinessSaleOffer(int buildingId, out int offerCents, out string summary)
        {
            EnsureMarket();
            offerCents = 0;
            summary = "Business sale unavailable.";
            PlacedBuilding building = GetBuilding(buildingId);
            if (building == null || !IsPlayerOwnedBusiness(building))
            {
                summary = "Business sale unavailable: select a player-owned operating business.";
                return false;
            }

            BusinessInstanceState business = sharedBusinessRuntime != null ? sharedBusinessRuntime.FindByBuildingId(buildingId) : null;
            if (business == null || business.RuntimeState == null || business.Owner == null || business.Owner.OwnerKind != BusinessOwnerKind.Player)
            {
                summary = "Business sale unavailable: active player business records are missing.";
                return false;
            }

            TownPlot plot = GetPlot(building.plotId);
            LandAppreciationState appreciation = plot != null ? EnsureLandAppreciationState(plot, building, LandAppreciationImprovementState.OperatingBusiness) : null;
            int landValue = appreciation != null
                ? Mathf.Max(appreciation.currentEstimatedValueCents, appreciation.TotalBasisCents)
                : plot != null ? CalculateLandPriceCents(plot) : building.siteSizeCells.x * building.siteSizeCells.y * baseLandPricePerCellCents;
            BusinessRuntimeState runtime = business.RuntimeState;
            int operatingValue = Mathf.Max(0, runtime.AllTimeNetCents / 2) + Mathf.Max(0, runtime.MonthToDateNetCents * 2);
            int staffValue = runtime.TargetWorkerCount <= 0
                ? 0
                : Mathf.RoundToInt(landValue * 0.18f * Mathf.Clamp01((float)runtime.FilledWorkerCount / runtime.TargetWorkerCount));
            int qualityValue = Mathf.RoundToInt(landValue * Mathf.Clamp01(business.OperatingEfficiency01) * 0.22f);
            int cashPressure = SharedBusinessRuntimeManager.CalculateSharedSurvivalCashReserveCents(business) - runtime.CurrentCashCents;
            float distressMultiplier = cashPressure > 0 ? Mathf.Lerp(0.72f, 0.9f, Mathf.Clamp01(runtime.CurrentCashCents / (float)Mathf.Max(1, SharedBusinessRuntimeManager.CalculateSharedSurvivalCashReserveCents(business)))) : 1f;
            float weakPerformanceMultiplier = runtime.AllTimeNetCents < 0 ? 0.86f : runtime.WeekToDateNetCents < 0 ? 0.94f : 1.04f;
            int gross = Mathf.Max(1, landValue + operatingValue + staffValue + qualityValue);
            offerCents = Mathf.Max(1, Mathf.RoundToInt(gross * distressMultiplier * weakPerformanceMultiplier));
            string distress = cashPressure > 0 ? $"distress discount {FormatMoney(cashPressure)} reserve shortfall" : "no distress discount";
            summary = $"Likely buyer offer {FormatMoney(offerCents)} | land {FormatMoney(landValue)} | staff {FormatMoney(staffValue)} | operating quality {FormatMoney(qualityValue + operatingValue)} | {distress}.";
            return true;
        }

        public bool TrySellPlayerBusiness(int buildingId, out string message)
        {
            if (!TryEstimatePlayerBusinessSaleOffer(buildingId, out int offerCents, out string saleRead))
            {
                message = saleRead;
                return false;
            }

            PlacedBuilding building = GetBuilding(buildingId);
            BusinessInstanceState business = sharedBusinessRuntime != null ? sharedBusinessRuntime.FindByBuildingId(buildingId) : null;
            if (building == null || business == null)
            {
                message = "Business sale unavailable: business records changed.";
                return false;
            }

            string buyerName = BuildBusinessBuyerName(business, offerCents);
            string transferMessage = string.Empty;
            bool transferred = sharedBusinessRuntime != null
                && sharedBusinessRuntime.TryTransferBusinessToTown(buildingId, buyerName, out _, out transferMessage);
            if (!transferred)
            {
                message = string.IsNullOrWhiteSpace(transferMessage) ? "Business sale failed during ownership transfer." : transferMessage;
                return false;
            }

            building.playerOwned = false;
            ownedBusinessBuildingIds.Remove(building.id);
            TownPlot plot = GetPlot(building.plotId);
            if (plot != null)
            {
                plot.playerOwned = false;
                ownedPlotIds.Remove(plot.id);
            }

            RemoveBusinessListingForBuilding(building.id);
            playerPortfolio?.AddOwnerCash(offerCents, $"{business.RuntimeDisplayName} sale");
            RefreshOwnedBuildability();
            RebuildMarket();
            lastPurchaseSummary = $"Sold {business.RuntimeDisplayName} to {buyerName} for {FormatMoney(offerCents)}. {saleRead}";
            marketStatus = lastPurchaseSummary;
            message = $"{lastPurchaseSummary} {transferMessage}";
            Debug.Log($"[Acquisitions] {lastPurchaseSummary}", this);
            return true;
        }

        private static string BuildBusinessBuyerName(BusinessInstanceState business, int offerCents)
        {
            string trade = business != null ? BusinessRuntimeNaming.GetBusinessTypeDisplayName(business.BusinessType) : "Business";
            string tier = offerCents >= 50000 ? "Capital Buyer" : offerCents >= 25000 ? "Local Buyer" : "Distress Buyer";
            return $"{tier} - {trade}";
        }
        public IReadOnlyList<OwnedPlotBuildabilityState> OwnedBuildablePlots => ownedBuildablePlots;
        public IReadOnlyList<ConstructionSupportNodeState> ConstructionSupportNodes => constructionSupportNodes;
        public string LastPurchaseSummary => lastPurchaseSummary;
        public string LastConstructionSupportSummary => lastConstructionSupportSummary;
        public string LastConstructionQueueSummary => lastConstructionQueueSummary;
        public string MarketStatus => marketStatus;
        public OwnershipAptitudeState OwnershipAptitude
        {
            get
            {
                EnsureOwnershipAptitude();
                return ownershipAptitude;
            }
        }
        public OwnershipAptitudeResult OwnershipAptitudeEffects => ownershipAptitudeEvaluator.Evaluate(OwnershipAptitude);

        public AcquisitionSaveDto CaptureSaveDto()
        {
            EnsureConstructionSupportNodesLoaded();
            RefreshOwnedLandAppreciations();
            AcquisitionSaveDto dto = new()
            {
                marketSeed = marketSeed,
                maxLandListings = maxLandListings,
                maxBusinessListings = maxBusinessListings,
                retailPressureStreakWeeks = Mathf.Max(0, retailPressureStreakWeeks),
                housingPressureStreakWeeks = Mathf.Max(0, housingPressureStreakWeeks),
                laborPressureStreakWeeks = Mathf.Max(0, laborPressureStreakWeeks),
                servicePressureStreakWeeks = Mathf.Max(0, servicePressureStreakWeeks),
                expansionPressureStreakWeeks = Mathf.Max(0, expansionPressureStreakWeeks),
                developmentInventoryPressure = Mathf.Max(0, developmentInventoryPressure),
                lastTownActionSummary = lastTownActionSummary ?? string.Empty,
                selectedLandIndex = selectedLandIndex,
                selectedBusinessIndex = selectedBusinessIndex,
                marketStatus = marketStatus,
                lastPurchaseSummary = lastPurchaseSummary,
                lastConstructionSupportSummary = lastConstructionSupportSummary,
                lastConstructionQueueSummary = lastConstructionQueueSummary,
                ownershipAptitude = OwnershipAptitude.CaptureSaveDto()
            };

            for (int i = 0; i < ownedPlotIds.Count; i++)
            {
                dto.ownedPlotIds.Add(ownedPlotIds[i]);
            }

            for (int i = 0; i < ownedBusinessBuildingIds.Count; i++)
            {
                dto.ownedBusinessBuildingIds.Add(ownedBusinessBuildingIds[i]);
            }

            for (int i = 0; i < activeDeals.Count; i++)
            {
                if (activeDeals[i] != null && activeDeals[i].stage != AcquisitionDealStage.Closed)
                {
                    dto.activeDeals.Add(activeDeals[i].CaptureSaveDto());
                }
            }

            for (int i = 0; i < watchedListingIds.Count; i++)
            {
                string watched = watchedListingIds[i];
                if (!string.IsNullOrWhiteSpace(watched))
                {
                    dto.watchedListingIds.Add(watched);
                }
            }

            for (int i = 0; i < opportunityWindows.Count; i++)
            {
                if (opportunityWindows[i] != null && !string.IsNullOrWhiteSpace(opportunityWindows[i].listingId))
                {
                    dto.opportunityWindows.Add(opportunityWindows[i].CaptureSaveDto());
                }
            }

            for (int i = 0; i < ownedLandAppreciations.Count; i++)
            {
                if (ownedLandAppreciations[i] != null)
                {
                    dto.landAppreciations.Add(CaptureLandAppreciationSaveDto(ownedLandAppreciations[i]));
                }
            }

            for (int i = 0; i < constructionSupportNodes.Count; i++)
            {
                if (constructionSupportNodes[i] != null)
                {
                    dto.constructionSupportNodes.Add(constructionSupportNodes[i].CaptureSaveDto());
                }
            }

            for (int i = 0; i < activeConstructionProjects.Count; i++)
            {
                if (activeConstructionProjects[i] != null && activeConstructionProjects[i].IsActive)
                {
                    dto.constructionProjects.Add(activeConstructionProjects[i].CaptureSaveDto());
                }
            }

            return dto;
        }

        public void LoadFromSaveDto(AcquisitionSaveDto dto)
        {
            AutoWire();
            landListings.Clear();
            businessListings.Clear();
            ownedPlotIds.Clear();
            ownedBusinessBuildingIds.Clear();
            ownedLandAppreciations.Clear();
            activeDeals.Clear();
            watchedListingIds.Clear();
            opportunityWindows.Clear();
            ownedBuildablePlots.Clear();
            constructionSupportNodes.Clear();
            activeConstructionProjects.Clear();
            ownershipAptitude = new OwnershipAptitudeState();

            if (dto == null)
            {
                EnsureConstructionSupportNodesLoaded();
                marketBuilt = false;
                RebuildMarket();
                return;
            }

            ownershipAptitude = OwnershipAptitudeState.FromSaveDto(dto.ownershipAptitude);
            marketSeed = dto.marketSeed;
            maxLandListings = Mathf.Clamp(dto.maxLandListings <= 0 ? maxLandListings : dto.maxLandListings, 1, 8);
            maxBusinessListings = Mathf.Clamp(dto.maxBusinessListings < 0 ? maxBusinessListings : dto.maxBusinessListings, 0, 6);
            retailPressureStreakWeeks = Mathf.Max(0, dto.retailPressureStreakWeeks);
            housingPressureStreakWeeks = Mathf.Max(0, dto.housingPressureStreakWeeks);
            laborPressureStreakWeeks = Mathf.Max(0, dto.laborPressureStreakWeeks);
            servicePressureStreakWeeks = Mathf.Max(0, dto.servicePressureStreakWeeks);
            expansionPressureStreakWeeks = Mathf.Max(0, dto.expansionPressureStreakWeeks);
            developmentInventoryPressure = Mathf.Max(0, dto.developmentInventoryPressure);
            lastTownActionSummary = dto.lastTownActionSummary ?? string.Empty;

            if (dto.ownedPlotIds != null)
            {
                for (int i = 0; i < dto.ownedPlotIds.Count; i++)
                {
                    if (!ownedPlotIds.Contains(dto.ownedPlotIds[i]))
                    {
                        ownedPlotIds.Add(dto.ownedPlotIds[i]);
                    }
                }
            }

            if (dto.ownedBusinessBuildingIds != null)
            {
                for (int i = 0; i < dto.ownedBusinessBuildingIds.Count; i++)
                {
                    if (!ownedBusinessBuildingIds.Contains(dto.ownedBusinessBuildingIds[i]))
                    {
                        ownedBusinessBuildingIds.Add(dto.ownedBusinessBuildingIds[i]);
                    }
                }
            }

            if (dto.activeDeals != null)
            {
                for (int i = 0; i < dto.activeDeals.Count; i++)
                {
                    AcquisitionDealState deal = AcquisitionDealState.FromSaveDto(dto.activeDeals[i]);
                    if (deal != null && !string.IsNullOrWhiteSpace(deal.listingId) && deal.stage != AcquisitionDealStage.Closed)
                    {
                        activeDeals.Add(deal);
                    }
                }
            }

            if (dto.watchedListingIds != null)
            {
                for (int i = 0; i < dto.watchedListingIds.Count; i++)
                {
                    AddWatchedListingId(dto.watchedListingIds[i]);
                }
            }

            if (dto.opportunityWindows != null)
            {
                for (int i = 0; i < dto.opportunityWindows.Count; i++)
                {
                    AcquisitionOpportunityWindowState window = AcquisitionOpportunityWindowState.FromSaveDto(dto.opportunityWindows[i]);
                    if (window != null)
                    {
                        opportunityWindows.Add(window);
                    }
                }
            }

            RestoreLandAppreciations(dto.landAppreciations);
            RestoreConstructionSupportNodes(dto.constructionSupportNodes);
            RestoreConstructionProjects(dto.constructionProjects);
            SyncSavedOwnershipToWorld();
            marketBuilt = false;
            RebuildMarket();
            selectedLandIndex = WrapIndex(dto.selectedLandIndex, landListings.Count);
            selectedBusinessIndex = WrapIndex(dto.selectedBusinessIndex, businessListings.Count);
            lastPurchaseSummary = string.IsNullOrWhiteSpace(dto.lastPurchaseSummary) ? lastPurchaseSummary : dto.lastPurchaseSummary;
            lastConstructionSupportSummary = string.IsNullOrWhiteSpace(dto.lastConstructionSupportSummary) ? lastConstructionSupportSummary : dto.lastConstructionSupportSummary;
            lastConstructionQueueSummary = string.IsNullOrWhiteSpace(dto.lastConstructionQueueSummary) ? lastConstructionQueueSummary : dto.lastConstructionQueueSummary;
            marketStatus = string.IsNullOrWhiteSpace(dto.marketStatus) ? marketStatus : dto.marketStatus;
            nextConstructionProjectSerial = Mathf.Max(nextConstructionProjectSerial, dto.constructionProjects != null ? dto.constructionProjects.Count + 1 : 1);
            RefreshOwnedLandAppreciations();
            RefreshOwnedBuildability();
        }

        public void Configure(
            TownWorldController newTownWorld,
            GeneralStoreRuntimeManager newPlayerCashSource,
            SharedBusinessRuntimeManager newSharedBusinessRuntime = null,
            PopulationManager newPopulationManager = null,
            TimeManager newTimeManager = null,
            CivicFoundationManager newCivicFoundation = null,
            PlayerPortfolioManager newPlayerPortfolio = null)
        {
            UnsubscribeFromTime();
            townWorld = newTownWorld;
            playerCashSource = newPlayerCashSource;
            playerPortfolio = newPlayerPortfolio;
            sharedBusinessRuntime = newSharedBusinessRuntime;
            populationManager = newPopulationManager;
            timeManager = newTimeManager;
            civicFoundation = newCivicFoundation;
            marketBuilt = false;
            SubscribeToTime();
        }

        public void ApplyFreshWorldSeedAuthority(int worldSeed)
        {
            int masterSeed = Mathf.Max(1, worldSeed);
            marketSeed = WorldSeedAuthority.DeriveChildSeed(masterSeed, WorldSeedAuthority.AcquisitionMarketSeedCategory);
            marketBuilt = false;
            Debug.Log($"[AcquisitionMarket] Market Seed Source: World Seed Authority | World Seed: {masterSeed} | Market Seed: {marketSeed}.", this);
        }

        [ContextMenu("Rebuild Acquisition Market")]
        public void RebuildMarket()
        {
            AutoWire();
            landListings.Clear();
            businessListings.Clear();
            selectedLandIndex = 0;
            selectedBusinessIndex = 0;

            if (townWorld == null)
            {
                marketStatus = "Missing TownWorldController.";
                marketBuilt = true;
                return;
            }

            if (townWorld.Grid == null)
            {
                townWorld.GenerateTownShell();
            }

            sharedBusinessRuntime?.InitializeIfNeeded(playerCashSource != null ? playerCashSource.CurrentBusiness : null);
            EnsureConstructionSupportNodesLoaded();
            ResolvePassiveDevelopmentInventory(false);
            BuildLandListings();
            BuildBusinessListings();
            RefreshOwnedBuildability();
            RefreshOwnedLandAppreciations();
            marketBuilt = true;
            marketStatus = $"Market ready. Land={landListings.Count}/{maxLandListings}, Businesses={businessListings.Count}/{maxBusinessListings}.";
        }

        public string BuildAcquisitionText(AcquisitionMarketSection section)
        {
            EnsureMarket();

            StringBuilder builder = new();
            builder.AppendLine($"Liquid Cash {FormatMoney(GetAvailableCashCents())}");
            builder.AppendLine($"Market: Land {landListings.Count}/{maxLandListings} | Businesses {businessListings.Count}/{maxBusinessListings}");
            builder.AppendLine(BuildWatchlistSummaryText());
            builder.AppendLine(BuildDealPipelineSummary());
            builder.AppendLine(BuildAcquisitionProcessLedgerSummary());
            builder.AppendLine(BuildAcquisitionLiquiditySummary());
            AppendConstructionInputAvailability(builder);
            builder.AppendLine();
            builder.AppendLine(BuildSelectedProcessSummary(section));
            builder.AppendLine();

            if (section == AcquisitionMarketSection.Businesses)
            {
                AppendCurrentBusinessListing(builder);
            }
            else
            {
                AppendCurrentLandListing(builder);
            }

            return builder.ToString();
        }

        public string BuildConstructionInputAvailabilityText()
        {
            EnsureMarket();
            StringBuilder builder = new();
            AppendConstructionInputAvailability(builder);
            string queueSummary = BuildConstructionQueueSummary();
            if (!string.IsNullOrWhiteSpace(queueSummary))
            {
                builder.AppendLine(queueSummary);
            }

            string detail = BuildConstructionQueueDetailText();
            if (!string.IsNullOrWhiteSpace(detail))
            {
                builder.AppendLine(detail);
            }

            return builder.ToString();
        }

        public string BuildConstructionQueueSummary()
        {
            EnsureMarket();
            int queued = 0;
            int waitingForLane = 0;
            int inProgress = 0;
            int blocked = 0;
            int stalled = 0;
            int nearestWeeks = int.MaxValue;
            ConstructionProjectState nextProject = null;
            for (int i = 0; i < activeConstructionProjects.Count; i++)
            {
                ConstructionProjectState project = activeConstructionProjects[i];
                if (project == null || !project.IsActive)
                {
                    continue;
                }

                if (project.progressState == ConstructionProjectProgressState.WaitingForLane)
                {
                    waitingForLane++;
                }
                else if (project.IsStarted)
                {
                    inProgress++;
                }
                else
                {
                    queued++;
                }

                if (project.progressState == ConstructionProjectProgressState.Stalled)
                {
                    stalled++;
                }

                if (project.progressState == ConstructionProjectProgressState.BlockedBeforeStart
                    || project.progressState == ConstructionProjectProgressState.Stalled
                    || !string.IsNullOrWhiteSpace(project.blockedReason))
                {
                    blocked++;
                }

                int remaining = project.WeeksRemaining;
                if (remaining < nearestWeeks)
                {
                    nearestWeeks = remaining;
                    nextProject = project;
                }
            }

            if (queued <= 0 && waitingForLane <= 0 && inProgress <= 0 && stalled <= 0)
            {
                return "Construction queue: no active projects.";
            }

            int builderCapacity = EstimateWeeklyConstructionBuilderCapacity(townWorld != null ? townWorld.Settings : null);
            string next = string.Empty;
            string stance = string.Empty;
            if (nextProject != null)
            {
                next = $" Next: {nextProject.title} | {BuildConstructionProjectStageText(nextProject)}.";
                if (TryBuildConstructionSupportReadForProject(nextProject, out _, out _, out int constraintScore))
                {
                    stance = $" Pressure: {BuildConstructionConstraintBandText(constraintScore)}.";
                }
            }

            return $"Construction queue: builder lane {builderCapacity}/wk | {queued} queued | {waitingForLane} waiting for lane | {inProgress} underway | {stalled} stalled | {blocked} blocked.{next}{stance}";
        }

        public string BuildConstructionQueueDetailText()
        {
            EnsureMarket();
            ConstructionProjectState nextProject = GetNextConstructionProject();
            if (nextProject == null)
            {
                return string.Empty;
            }

            string authority = string.Empty;
            string review = string.Empty;
            if (TryBuildConstructionSupportReadForProject(nextProject, out string startAuthority, out string weeklyReview, out _))
            {
                authority = string.IsNullOrWhiteSpace(startAuthority)
                    ? string.Empty
                    : TrimTrailingPeriod(startAuthority);
                review = string.IsNullOrWhiteSpace(weeklyReview)
                    ? string.Empty
                    : TrimTrailingPeriod(weeklyReview);
            }

            string blocker = string.IsNullOrWhiteSpace(nextProject.blockedReason)
                ? "None"
                : TrimTrailingPeriod(nextProject.blockedReason);
            string latest = string.IsNullOrWhiteSpace(nextProject.lastWeeklyProgressSummary)
                ? "No weekly note yet"
                : TrimTrailingPeriod(nextProject.lastWeeklyProgressSummary);
            string action = BuildConstructionRecommendedActionText(nextProject, authority, review);
            string inputLabel = nextProject.inputsCommitted ? "Inputs committed" : "Inputs needed";

            StringBuilder builder = new();
            builder.AppendLine($"Next project: {nextProject.title}");
            builder.AppendLine($"Stage: {BuildConstructionProjectStageText(nextProject)}");
            builder.AppendLine($"{inputLabel}: {BuildProjectInputSnapshotText(nextProject)}");
            if (!string.IsNullOrWhiteSpace(authority))
            {
                builder.AppendLine($"Start authority: {authority}");
            }

            if (!string.IsNullOrWhiteSpace(review))
            {
                builder.AppendLine($"Support review: {review}");
            }

            builder.AppendLine($"Next step: {action}");
            builder.AppendLine($"Current blocker: {blocker}");
            builder.Append($"Latest weekly note: {latest}");
            return builder.ToString();
        }

        private static string BuildProjectInputSnapshotText(ConstructionProjectState project)
        {
            if (project == null)
            {
                return "no tracked inputs";
            }

            List<string> parts = new();
            if (project.plannedLumberUnits > 0)
            {
                parts.Add($"{project.plannedLumberUnits} lumber");
            }

            if (project.plannedNailsUnits > 0)
            {
                parts.Add($"{project.plannedNailsUnits} nails/hardware");
            }

            if (project.plannedLaborUnits > 0)
            {
                parts.Add($"{project.plannedLaborUnits} labor");
            }

            return parts.Count > 0 ? string.Join(", ", parts) : "no tracked inputs";
        }
        private static string BuildConstructionProjectStageText(ConstructionProjectState project)
        {
            if (project == null)
            {
                return "No active project";
            }

            if (project.progressState == ConstructionProjectProgressState.WaitingForLane)
            {
                return "Waiting for builder lane";
            }

            if (project.progressState == ConstructionProjectProgressState.BlockedBeforeStart)
            {
                return "Blocked before start";
            }

            if (project.progressState == ConstructionProjectProgressState.Stalled)
            {
                return $"Stalled after week {Mathf.Max(0, project.weeksProgressed)}/{Mathf.Max(1, project.weeksRequired)}";
            }

            if (!string.IsNullOrWhiteSpace(project.blockedReason))
            {
                return project.IsStarted
                    ? $"Crew stalled after week {Mathf.Max(0, project.weeksProgressed)}/{Mathf.Max(1, project.weeksRequired)}"
                    : "Blocked before start";
            }

            if (project.IsStarted)
            {
                return $"Week {Mathf.Max(1, project.weeksProgressed)}/{Mathf.Max(1, project.weeksRequired)} underway";
            }

            return project.readyWeeks > 0
                ? "Ready to move when a builder lane opens"
                : "Awaiting builder start";
        }

        private static string BuildConstructionRecommendedActionText(ConstructionProjectState project, string startAuthority, string review)
        {
            if (project == null)
            {
                return "No queued construction action.";
            }

            string combined = $"{project.blockedReason} {startAuthority} {review}".ToLowerInvariant();
            if (combined.Contains("hardware") || combined.Contains("blacksmith") || combined.Contains("nails"))
            {
                return "Restore nails/simple hardware supply or rely on regional freight coverage.";
            }

            if (combined.Contains("lumber") || combined.Contains("timber") || combined.Contains("log"))
            {
                return "Restore lumber flow, sawmill output, or town yard stock before expecting progress.";
            }

            if (combined.Contains("labor") || combined.Contains("crew") || combined.Contains("staff"))
            {
                return "Restore construction labor coverage before expecting the crew to move this project.";
            }

            if (combined.Contains("queue") || combined.Contains("builder") || combined.Contains("carpenter"))
            {
                return "Ease builder pressure or wait for a free construction lane.";
            }

            if (combined.Contains("cash") || combined.Contains("fund"))
            {
                return "Strengthen owner liquidity before committing more construction cash.";
            }

            if (project.IsStarted)
            {
                return "Keep materials and support steady while the crew works.";
            }

            return "Hold materials ready and let the next builder lane open.";
        }

        private bool TryBuildConstructionSupportReadForProject(ConstructionProjectState project, out string startAuthority, out string weeklyReview, out int constraintScore)
        {
            startAuthority = string.Empty;
            weeklyReview = string.Empty;
            constraintScore = 0;
            if (project == null)
            {
                return false;
            }

            if (!TryBuildCurrentConstructionQuote(project, out ConstructionInputQuote quote, out BuildingDefinition definition, out _))
            {
                return false;
            }

            ConstructionSupportNodeState supportNode = GetPrimaryLumberSupportNode();
            if (supportNode == null)
            {
                return false;
            }

            TownGenerationSettings settings = townWorld != null ? townWorld.Settings : null;
            List<ConstructionProjectState> ordered = GetWeeklyConstructionProcessingOrder();
            int projectIndex = -1;
            for (int i = 0; i < ordered.Count; i++)
            {
                ConstructionProjectState candidate = ordered[i];
                if (candidate == project || string.Equals(candidate?.projectId, project.projectId, StringComparison.Ordinal))
                {
                    projectIndex = i;
                    break;
                }
            }

            int queueAhead = projectIndex >= 0 ? CountConstructionProjectsAhead(ordered, projectIndex) : 0;
            int lumberUnits = GetQuoteUnits(quote, ConstructionResourceKind.Lumber);
            startAuthority = supportNode.BuildProjectStartAuthoritySummary(settings, definition, queueAhead, lumberUnits);
            weeklyReview = supportNode.BuildWeeklyConstructionReviewSummary(settings, definition, queueAhead, lumberUnits);
            constraintScore = supportNode.EvaluateProjectConstraintScore(settings, definition, queueAhead, lumberUnits);
            return true;
        }

        private static string BuildConstructionConstraintBandText(int constraintScore)
        {
            if (constraintScore >= 5)
            {
                return "high-risk delay";
            }

            if (constraintScore >= 3)
            {
                return "pressured";
            }

            if (constraintScore >= 1)
            {
                return "manageable with caution";
            }

            return "clear";
        }

        private static string TrimTrailingPeriod(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim().TrimEnd('.');
        }

        public bool TryQueueConstructOnOwnedPlot(int plotId, int optionIndex, out string message)
        {
            message = string.Empty;
            EnsureMarket();
            RefreshOwnedBuildability();

            OwnedPlotBuildabilityState state = FindOwnedBuildabilityState(plotId);
            if (state == null)
            {
                message = $"Plot {plotId:000} is not tracked as a player-owned plot.";
                return false;
            }

            if (!state.playerOwned)
            {
                message = $"Plot {plotId:000} is not player-owned.";
                return false;
            }

            if (!state.empty)
            {
                message = $"Plot {plotId:000} already has an improved site.";
                return false;
            }

            if (!state.buildable || state.buildOptions == null || state.buildOptions.Count == 0)
            {
                message = string.IsNullOrWhiteSpace(state.reason)
                    ? $"Plot {plotId:000} has no fitting building shell options."
                    : state.reason;
                return false;
            }

            BuildingDefinition definition = state.buildOptions[Mathf.Clamp(optionIndex, 0, state.buildOptions.Count - 1)];
            if (definition == null)
            {
                message = "No building shell definition is selected.";
                return false;
            }

            return TryQueueResolvedShellConstruction(plotId, definition, null, out message);
        }

        public bool TryQueueConstructHouseOnOwnedPlot(int plotId, int optionIndex, out string message)
        {
            if (!TryResolveHouseBuildOption(plotId, optionIndex, out BuildingDefinition definition, out _, out message))
            {
                return false;
            }

            return TryQueueResolvedShellConstruction(plotId, definition, null, out message);
        }

        public bool TryQueueConstructBusinessShellOnOwnedPlot(int plotId, int businessOptionIndex, int shellOptionIndex, out string message)
        {
            if (!TryGetBusinessDevelopmentCandidate(businessOptionIndex, out BusinessActivationCandidateState candidate) || candidate == null)
            {
                message = "No business type is selected.";
                return false;
            }

            if (!candidate.eligible)
            {
                message = string.IsNullOrWhiteSpace(candidate.reason)
                    ? $"{candidate.displayName} is not available."
                    : candidate.reason;
                return false;
            }

            if (!TryResolveBusinessBuildOption(plotId, businessOptionIndex, shellOptionIndex, out BusinessType businessType, out BuildingDefinition definition, out _, out message))
            {
                return false;
            }

            return TryQueueResolvedShellConstruction(plotId, definition, businessType, out message);
        }

        public bool TryQueueStartBusinessFromOwnedShell(int buildingId, int optionIndex, out string message)
        {
            EnsureMarket();

            if (!TryGetBusinessActivationCandidate(buildingId, optionIndex, out BusinessActivationCandidateState candidate) || candidate == null)
            {
                message = "No business activation candidate is selected.";
                return false;
            }

            if (!candidate.eligible)
            {
                message = string.IsNullOrWhiteSpace(candidate.reason)
                    ? $"{candidate.displayName} cannot start at this site."
                    : candidate.reason;
                return false;
            }

            if (HasActiveConstructionProjectForBuilding(buildingId, ConstructionProjectKind.BusinessFitOut))
            {
                message = $"A fit-out is already queued for Building {buildingId:000}.";
                return false;
            }

            PlacedBuilding shell = GetBuilding(buildingId);
            if (shell == null)
            {
                message = $"Building {buildingId:000} is no longer available.";
                return false;
            }

            ConstructionInputQuote quote = BuildBusinessFitOutQuote(candidate, shell);
            int weeks = Mathf.Max(1, townWorld != null && townWorld.Settings != null ? townWorld.Settings.GetTypicalRepairWeeks() : 1);
            ConstructionProjectState project = new()
            {
                projectId = GenerateConstructionProjectId(),
                projectKind = ConstructionProjectKind.BusinessFitOut,
                progressState = ConstructionProjectProgressState.Queued,
                title = $"{candidate.displayName} fit-out",
                plotId = shell.plotId,
                buildingId = buildingId,
                hasBusinessIntent = true,
                businessIntent = candidate.businessType,
                queuedWeekKey = GetCurrentWeekKey(),
                weeksRequired = weeks,
                estimatedCashCostCents = quote.CashCostCents,
                plannedLumberUnits = GetQuoteUnits(quote, ConstructionResourceKind.Lumber),
                plannedNailsUnits = GetQuoteUnits(quote, ConstructionResourceKind.Nails),
                plannedLaborUnits = GetQuoteUnits(quote, ConstructionResourceKind.Labor),
                statusText = BuildQueuedConstructionStatusText(quote, shell.definition, weeks, $"Queued fit-out at Building {buildingId:000}.")
            };

            activeConstructionProjects.Add(project);
            lastConstructionQueueSummary = BuildConstructionQueueSummary();
            marketStatus = $"Queued {candidate.displayName} fit-out at Building {buildingId:000}.";
            message = project.statusText;
            return true;
        }

        public bool TryQueueBuildHouseholdUpgrade(int homeBuildingId, int optionIndex, out string message)
        {
            message = string.Empty;
            if (!TryGetHouseholdUpgradeDefinition(optionIndex, out HouseholdUpgradeDefinition definition) || definition == null)
            {
                message = "No household upgrade is selected.";
                return false;
            }

            if (!TryResolveHouseholdUpgradeTarget(homeBuildingId, definition.Kind, out _, out _, out message))
            {
                return false;
            }

            if (HasActiveConstructionProjectForBuilding(homeBuildingId, ConstructionProjectKind.HouseholdUpgrade))
            {
                message = $"A household upgrade is already queued for Building {homeBuildingId:000}.";
                return false;
            }

            ConstructionInputQuote quote = BuildHouseholdUpgradeQuote(definition, homeBuildingId);
            int weeks = Mathf.Max(1, townWorld != null && townWorld.Settings != null ? townWorld.Settings.GetTypicalRepairWeeks() : 1);
            ConstructionProjectState project = new()
            {
                projectId = GenerateConstructionProjectId(),
                projectKind = ConstructionProjectKind.HouseholdUpgrade,
                progressState = ConstructionProjectProgressState.Queued,
                title = definition.DisplayName,
                buildingId = homeBuildingId,
                plotId = GetBuilding(homeBuildingId) != null ? GetBuilding(homeBuildingId).plotId : -1,
                householdUpgradeKind = (int)definition.Kind,
                queuedWeekKey = GetCurrentWeekKey(),
                weeksRequired = weeks,
                estimatedCashCostCents = quote.CashCostCents,
                plannedLumberUnits = GetQuoteUnits(quote, ConstructionResourceKind.Lumber),
                plannedNailsUnits = GetQuoteUnits(quote, ConstructionResourceKind.Nails),
                plannedLaborUnits = GetQuoteUnits(quote, ConstructionResourceKind.Labor),
                statusText = BuildQueuedConstructionStatusText(quote, null, weeks, $"Queued household upgrade for Building {homeBuildingId:000}.")
            };

            activeConstructionProjects.Add(project);
            lastConstructionQueueSummary = BuildConstructionQueueSummary();
            marketStatus = $"Queued {definition.DisplayName} for Building {homeBuildingId:000}.";
            message = project.statusText;
            return true;
        }

        public bool ResolveWeeklyConstructionQueue(out string summary)
        {
            AutoWire();
            EnsureMarket();
            EnsureConstructionSupportNodesLoaded();

            List<ConstructionProjectState> ordered = GetWeeklyConstructionProcessingOrder();
            if (ordered.Count <= 0)
            {
                summary = "Construction queue: no active projects.";
                lastConstructionQueueSummary = summary;
                return false;
            }

            TownGenerationSettings settings = townWorld != null ? townWorld.Settings : null;
            int weeklyBuilderCapacity = EstimateWeeklyConstructionBuilderCapacity(settings);
            int builderSlotsRemaining = weeklyBuilderCapacity;
            int startedThisWeek = 0;
            int progressedThisWeek = 0;
            int completedThisWeek = 0;
            int blockedThisWeek = 0;
            int waitingForLaneThisWeek = 0;

            for (int i = 0; i < ordered.Count; i++)
            {
                ConstructionProjectState project = ordered[i];
                if (project == null || !project.IsActive)
                {
                    continue;
                }

                int queueAhead = CountConstructionProjectsAhead(ordered, i);
                if (builderSlotsRemaining <= 0)
                {
                    string laneReason = BuildProjectQueueCapacityBlockedReason(settings, project, queueAhead);
                    if (project.IsStarted)
                    {
                        BlockConstructionProject(project, laneReason);
                        blockedThisWeek++;
                    }
                    else
                    {
                        MarkConstructionProjectWaitingForLane(project, laneReason);
                        waitingForLaneThisWeek++;
                    }

                    continue;
                }

                if (!project.inputsCommitted)
                {
                    if (!TryStartQueuedConstructionProject(project, queueAhead, out string startMessage))
                    {
                        if (ShouldFailQueuedConstructionProject(project, startMessage))
                        {
                            FailConstructionProject(project, startMessage);
                        }
                        else
                        {
                            BlockConstructionProject(project, startMessage);
                            blockedThisWeek++;
                        }

                        continue;
                    }

                    startedThisWeek++;
                }

                builderSlotsRemaining--;
                project.progressState = ConstructionProjectProgressState.InProgress;
                project.readyWeeks = Mathf.Max(0, project.readyWeeks) + 1;
                project.blockedWeeks = 0;
                project.blockedReason = string.Empty;
                project.weeksProgressed = Mathf.Min(Mathf.Max(1, project.weeksRequired), Mathf.Max(0, project.weeksProgressed) + 1);
                project.lastWeeklyProgressSummary = $"Worked this week. {BuildProjectProgressText(project)}";
                project.statusText = $"{project.title} - {BuildProjectProgressText(project)}";
                progressedThisWeek++;

                if (project.weeksProgressed >= Mathf.Max(1, project.weeksRequired))
                {
                    if (TryCompleteQueuedConstructionProject(project, out string completionMessage))
                    {
                        project.progressState = ConstructionProjectProgressState.Completed;
                        project.completedWeekKey = GetCurrentWeekKey();
                        project.statusText = completionMessage;
                        project.lastWeeklyProgressSummary = completionMessage;
                        completedThisWeek++;
                    }
                    else
                    {
                        FailConstructionProject(project, completionMessage);
                        blockedThisWeek++;
                    }
                }
            }

            activeConstructionProjects.RemoveAll(project => project == null || project.progressState == ConstructionProjectProgressState.Completed || project.progressState == ConstructionProjectProgressState.Failed);

            int queued = 0;
            int inProgress = 0;
            int waitingForLane = 0;
            int stalled = 0;
            for (int i = 0; i < activeConstructionProjects.Count; i++)
            {
                ConstructionProjectState project = activeConstructionProjects[i];
                if (project == null || !project.IsActive)
                {
                    continue;
                }

                if (project.progressState == ConstructionProjectProgressState.WaitingForLane)
                {
                    waitingForLane++;
                }
                else if (project.progressState == ConstructionProjectProgressState.Stalled)
                {
                    stalled++;
                    inProgress++;
                }
                else if (project.IsStarted)
                {
                    inProgress++;
                }
                else
                {
                    queued++;
                }
            }

            summary = $"Construction queue weekly review: builder capacity {weeklyBuilderCapacity} | started {startedThisWeek} | progressed {progressedThisWeek} | completed {completedThisWeek} | waiting for lane {waitingForLaneThisWeek} | blocked/stalled {blockedThisWeek} | remaining {queued + waitingForLane + inProgress} ({stalled} stalled).";
            lastConstructionQueueSummary = summary;
            if (completedThisWeek > 0)
            {
                marketStatus = summary;
            }

            return startedThisWeek > 0 || progressedThisWeek > 0 || completedThisWeek > 0;
        }

        public bool ResolveWeeklyConstructionSupportProduction()
        {
            AutoWire();
            EnsureConstructionSupportNodesLoaded();
            SyncSawmillRemoteProductionSites();

            if (!TryGetConstructionSupportNode(ConstructionResourceKind.Lumber, out ConstructionSupportNodeState sawmill)
                || sawmill == null
                || !sawmill.RemoteProductionEnabled)
            {
                lastConstructionSupportSummary = "No active remote sawmill production node.";
                return false;
            }

            if (sharedBusinessRuntime != null
                && sharedBusinessRuntime.TryResolveWeeklySawmillProduction(
                    sawmill,
                    GetCurrentWeekKey(),
                    out string sharedSawmillSummary,
                    out bool sharedSawmillProduced))
            {
                lastConstructionSupportSummary = string.IsNullOrWhiteSpace(sharedSawmillSummary)
                    ? sawmill.LastWeeklyProductionSummary
                    : sharedSawmillSummary;
                return sharedSawmillProduced;
            }

            int availableLabor = GetAvailableLaborUnits(out string laborSource);
            int cutLogs = 0;
            int processedLogs = 0;
            int producedLumber = 0;
            List<string> blockedReasons = new();

            if (availableLabor <= 0)
            {
                blockedReasons.Add("no available labor for remote sawmill crew");
            }
            else
            {
                int effectiveCutCapacity = Mathf.Min(sawmill.WeeklyCutCapacityLogs, availableLabor);
                cutLogs = sawmill.CutStandingTimberToLogs(effectiveCutCapacity);

                int logsByStorage = sawmill.LumberPerLog > 0
                    ? sawmill.LumberStorageRoomUnits / sawmill.LumberPerLog
                    : 0;
                int logsReadyToProcess = Mathf.Min(
                    sawmill.LogStockUnits,
                    sawmill.WeeklySawCapacityLogs,
                    availableLabor,
                    logsByStorage);

                if (logsReadyToProcess > 0)
                {
                    int maintenanceUnits = sawmill.HardwareMaintenanceUnitsPerWeek;
                    if (TryConsumeSawmillMaintenanceHardware(maintenanceUnits, out string hardwareSource, out string hardwareBlockReason))
                    {
                        processedLogs = sawmill.ProcessLogsToLumber(logsReadyToProcess, out producedLumber);
                        if (maintenanceUnits > 0 && !string.IsNullOrWhiteSpace(hardwareSource))
                        {
                            lastConstructionSupportSummary = $"Sawmill maintained by {hardwareSource}.";
                        }
                    }
                    else
                    {
                        blockedReasons.Add(hardwareBlockReason);
                    }
                }
                else if (sawmill.LogStockUnits <= 0)
                {
                    blockedReasons.Add("no staged logs at the sawmill");
                }
                else if (logsByStorage <= 0)
                {
                    blockedReasons.Add("sawmill lumber storage full");
                }
            }

            if (producedLumber <= 0 && cutLogs <= 0 && sawmill.StandingTimberUnits <= 0)
            {
                blockedReasons.Add("standing timber depleted");
            }

            string blockedReason = producedLumber <= 0 ? BuildDistinctReasonSummary(blockedReasons) : string.Empty;
            string summary = BuildSawmillProductionSummary(sawmill, cutLogs, processedLogs, producedLumber, laborSource, blockedReason);
            sawmill.RecordSawmillWeeklyResult(cutLogs, processedLogs, producedLumber, summary, blockedReason);
            lastConstructionSupportSummary = summary;
            return producedLumber > 0;
        }

        public void SelectPrevious(AcquisitionMarketSection section)
        {
            EnsureMarket();
            if (section == AcquisitionMarketSection.Businesses)
            {
                selectedBusinessIndex = WrapIndex(selectedBusinessIndex - 1, businessListings.Count);
            }
            else
            {
                selectedLandIndex = WrapIndex(selectedLandIndex - 1, landListings.Count);
            }
        }

        public void SelectNext(AcquisitionMarketSection section)
        {
            EnsureMarket();
            if (section == AcquisitionMarketSection.Businesses)
            {
                selectedBusinessIndex = WrapIndex(selectedBusinessIndex + 1, businessListings.Count);
            }
            else
            {
                selectedLandIndex = WrapIndex(selectedLandIndex + 1, landListings.Count);
            }
        }

        public bool TryPurchaseSelected(AcquisitionMarketSection section, out string message)
        {
            EnsureMarket();
            return TryAdvanceSelectedAcquisition(section, out message);
        }

        public bool TryAdvanceSelectedAcquisition(AcquisitionMarketSection section, out string message)
        {
            EnsureMarket();
            if (!TryGetSelectedListing(section, out AcquisitionListing listing) || listing == null)
            {
                message = section == AcquisitionMarketSection.Businesses
                    ? "No business listing is selected."
                    : "No land listing is selected.";
                return false;
            }

            if (!ValidateListingStillAvailable(listing, out message))
            {
                RebuildMarket();
                return false;
            }

            AcquisitionDealState deal = FindDeal(listing.listingId);
            if (deal == null || deal.stage == AcquisitionDealStage.None)
            {
                return StartInquiry(listing, out message);
            }

            if (deal.stage == AcquisitionDealStage.Failed)
            {
                return AdvanceFailedDealFile(listing, deal, out message);
            }

            return AdvanceDealFromCurrentStage(listing, deal, out message);
        }

        public string GetSelectedAcquisitionActionLabel(AcquisitionMarketSection section)
        {
            EnsureMarket();
            if (!TryGetSelectedListing(section, out AcquisitionListing listing) || listing == null)
            {
                return "No Listing";
            }

            AcquisitionDealState deal = FindDeal(listing.listingId);
            if (deal == null || deal.stage == AcquisitionDealStage.None)
            {
                return "Inquire";
            }

            return GetActionLabelForDeal(deal);
        }

        public bool ToggleSelectedWatch(AcquisitionMarketSection section, out string message)
        {
            EnsureMarket();
            if (!TryGetSelectedListing(section, out AcquisitionListing listing) || listing == null)
            {
                message = "No listing is selected.";
                return false;
            }

            if (IsWatchedListing(listing.listingId))
            {
                RemoveWatchedListingId(listing.listingId);
                message = $"{GetListingDisplayName(listing)} removed from the watchlist.";
                marketStatus = message;
                return true;
            }

            AddWatchedListingId(listing.listingId);
            message = $"{GetListingDisplayName(listing)} marked for later review.";
            marketStatus = message;
            return true;
        }

        public bool TrySelectListingByPlotId(int plotId, out AcquisitionMarketSection section, out AcquisitionListing listing)
        {
            EnsureMarket();
            section = AcquisitionMarketSection.Land;
            listing = null;
            if (plotId < 0)
            {
                return false;
            }

            for (int i = 0; i < landListings.Count; i++)
            {
                AcquisitionListing candidate = landListings[i];
                if (candidate != null && candidate.plotId == plotId)
                {
                    selectedLandIndex = i;
                    section = AcquisitionMarketSection.Land;
                    listing = candidate;
                    return true;
                }
            }

            for (int i = 0; i < businessListings.Count; i++)
            {
                AcquisitionListing candidate = businessListings[i];
                if (candidate != null && candidate.plotId == plotId)
                {
                    selectedBusinessIndex = i;
                    section = AcquisitionMarketSection.Businesses;
                    listing = candidate;
                    return true;
                }
            }

            return false;
        }

        public bool TrySelectListingByBuildingId(int buildingId, out AcquisitionMarketSection section, out AcquisitionListing listing)
        {
            EnsureMarket();
            section = AcquisitionMarketSection.Businesses;
            listing = null;
            if (buildingId < 0)
            {
                return false;
            }

            for (int i = 0; i < businessListings.Count; i++)
            {
                AcquisitionListing candidate = businessListings[i];
                if (candidate != null && candidate.buildingId == buildingId)
                {
                    selectedBusinessIndex = i;
                    section = AcquisitionMarketSection.Businesses;
                    listing = candidate;
                    return true;
                }
            }

            for (int i = 0; i < landListings.Count; i++)
            {
                AcquisitionListing candidate = landListings[i];
                if (candidate != null && candidate.buildingId == buildingId)
                {
                    selectedLandIndex = i;
                    section = AcquisitionMarketSection.Land;
                    listing = candidate;
                    return true;
                }
            }

            return false;
        }

        public bool TryToggleWatchListing(string listingId, out string message)
        {
            EnsureMarket();
            AcquisitionListing listing = FindListingById(listingId);
            if (listing == null)
            {
                message = "No matching acquisition lead is available.";
                return false;
            }

            if (IsWatchedListing(listing.listingId))
            {
                RemoveWatchedListingId(listing.listingId);
                message = $"{GetListingDisplayName(listing)} removed from the watchlist.";
                marketStatus = message;
                return true;
            }

            AddWatchedListingId(listing.listingId);
            message = $"{GetListingDisplayName(listing)} marked for later review.";
            marketStatus = message;
            return true;
        }

        public bool TryAdvanceListing(string listingId, out string message)
        {
            EnsureMarket();
            AcquisitionListing listing = FindListingById(listingId);
            if (listing == null)
            {
                message = "No matching acquisition lead is available.";
                return false;
            }

            if (!ValidateListingStillAvailable(listing, out message))
            {
                RebuildMarket();
                return false;
            }

            AcquisitionDealState deal = FindDeal(listing.listingId);
            if (deal == null || deal.stage == AcquisitionDealStage.None)
            {
                return StartInquiry(listing, out message);
            }

            if (deal.stage == AcquisitionDealStage.Failed)
            {
                return AdvanceFailedDealFile(listing, deal, out message);
            }

            return AdvanceDealFromCurrentStage(listing, deal, out message);
        }

        private bool AdvanceDealFromCurrentStage(AcquisitionListing listing, AcquisitionDealState deal, out string message)
        {
            if (listing == null)
            {
                message = "No matching acquisition lead is available.";
                return false;
            }

            if (deal == null || deal.stage == AcquisitionDealStage.None)
            {
                return StartInquiry(listing, out message);
            }

            if (deal.stage == AcquisitionDealStage.Failed)
            {
                return AdvanceFailedDealFile(listing, deal, out message);
            }

            return deal.stage switch
            {
                AcquisitionDealStage.Inquiry => NeedsProofOfFunds(deal)
                    ? PresentProofOfFunds(listing, deal, out message)
                    : CommitEarnest(listing, deal, out message),
                AcquisitionDealStage.EarnestCommitted => RunDiligence(listing, deal, out message),
                AcquisitionDealStage.DiligenceComplete => AgreeTentativeTerms(listing, deal, out message),
                AcquisitionDealStage.TentativeAgreement => CloseAcquisition(listing, deal, out message),
                AcquisitionDealStage.Closed => FailMessage("This acquisition is already closed.", out message),
                _ => FailMessage("This acquisition is not ready for the next step.", out message)
            };
        }

        private bool AdvanceFailedDealFile(AcquisitionListing listing, AcquisitionDealState deal, out string message)
        {
            if (listing == null)
            {
                message = "Failed acquisition file is no longer attached to a live lead.";
                return false;
            }

            if (deal == null || deal.stage != AcquisitionDealStage.Failed)
            {
                return StartInquiry(listing, out message);
            }

            if (!IsFailedDealReadyToReopen(deal))
            {
                return ReviewFailedDealFile(listing, deal, out message);
            }

            if (!CanReopenFailedDealFile(listing, deal, out string blockReason))
            {
                deal.statusText = blockReason;
                marketStatus = blockReason;
                lastPurchaseSummary = blockReason;
                message = blockReason;
                return false;
            }

            return ReopenFailedDealFile(listing, deal, out message);
        }

        private bool ReviewFailedDealFile(AcquisitionListing listing, AcquisitionDealState deal, out string message)
        {
            if (listing == null || deal == null)
            {
                message = "No failed acquisition file is available for review.";
                return false;
            }

            deal.stalledReviewCount = Mathf.Max(0, deal.stalledReviewCount) + 1;
            RefreshDealWorkflowReadouts(listing, deal);
            string breakpoint = BuildFailureBreakpointLabel(deal.failedFromStage);
            string failure = string.IsNullOrWhiteSpace(deal.closingFailureSummary)
                ? "No specific failure note was preserved; review funding, deadline, seller posture, and diligence before trying again."
                : deal.closingFailureSummary.Trim();
            deal.statusText = $"Failed file reviewed: {GetListingDisplayName(listing)} failed during {breakpoint}. {failure} Next: reopen inquiry only if the file conditions have materially changed; otherwise leave it parked.";
            marketStatus = deal.statusText;
            lastPurchaseSummary = deal.statusText;
            message = deal.statusText;
            return true;
        }

        private bool CanReopenFailedDealFile(AcquisitionListing listing, AcquisitionDealState deal, out string reason)
        {
            if (listing == null || deal == null)
            {
                reason = "Reopen blocked: no failed acquisition file is attached.";
                return false;
            }

            if (deal.stage != AcquisitionDealStage.Failed)
            {
                reason = "Reopen blocked: only failed files use the reopen review path.";
                return false;
            }

            if (!IsFailedDealReadyToReopen(deal))
            {
                reason = "Reopen blocked: review the failure first so the file does not silently restart.";
                return false;
            }

            if (!ValidateListingStillAvailable(listing, out reason))
            {
                reason = $"Reopen blocked: {reason}";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private bool ReopenFailedDealFile(AcquisitionListing listing, AcquisitionDealState deal, out string message)
        {
            int currentDay = GetCurrentDayIndex();
            string priorFailure = string.IsNullOrWhiteSpace(deal.closingFailureSummary)
                ? "prior failure was not preserved"
                : deal.closingFailureSummary.Trim();
            string priorBreakpoint = BuildFailureBreakpointLabel(deal.failedFromStage);
            string previousScouting = deal.scoutingSummary ?? string.Empty;
            string previousQuickLedger = deal.quickDiligenceLedger ?? string.Empty;
            int previousDiligenceMask = deal.revealedDiligenceMask;

            SellerProfile seller = BuildSellerProfile(listing);
            SellerPressureResult pressure = new SellerPressureEvaluator().Evaluate(seller);
            int earnest = CalculateEarnestMoneyCents(listing, pressure);
            int likelyFinancingNeed = Mathf.Max(0, listing.askingPriceCents - GetAvailableCashCents());
            AcquisitionBuyerSeriousness seriousness = EvaluateBuyerSeriousness(listing, likelyFinancingNeed, GetAvailableCashCents(), IsWatchedListing(listing.listingId));
            string reopenNote = $"Reopened after {priorBreakpoint} failure review: {priorFailure}";

            deal.kind = listing.kind;
            deal.listingId = listing.listingId ?? string.Empty;
            deal.plotId = listing.plotId;
            deal.buildingId = listing.buildingId;
            deal.title = listing.title ?? string.Empty;
            deal.sellerMotive = seller.motive;
            deal.sellerPressure01 = pressure.Pressure01;
            deal.sellerRelationshipSensitivity01 = pressure.RelationshipSensitivity01;
            deal.inquiryDayIndex = currentDay;
            deal.seriousness = seriousness;
            deal.scoutingSummary = string.IsNullOrWhiteSpace(previousScouting)
                ? $"{BuildScoutingSummary(listing)} {reopenNote}".Trim()
                : $"{previousScouting} {reopenNote}".Trim();
            deal.earnestMoneyCents = earnest;
            deal.optionDeadlineDayIndex = currentDay + CalculateOptionWindowDays(listing, pressure);
            deal.diligenceDayIndex = -1;
            deal.estimatedValueCents = 0;
            deal.valuationBandLowCents = 0;
            deal.valuationBandHighCents = 0;
            deal.diligenceScore01 = 0f;
            deal.diligenceSummary = string.Empty;
            deal.tentativeAgreementDayIndex = -1;
            deal.tentativePriceCents = 0;
            deal.integrationStance = AcquisitionIntegrationStance.None;
            deal.closingDeadlineDayIndex = -1;
            deal.financingNeedCents = likelyFinancingNeed;
            deal.closingRisk01 = Mathf.Clamp01(0.16f + pressure.RelationshipSensitivity01 * 0.2f + (likelyFinancingNeed > 0 ? 0.14f : 0f));
            deal.financingContingencyPresent = likelyFinancingNeed > 0;
            deal.weeklyReviewCount = 0;
            deal.revealedDiligenceMask = previousDiligenceMask;
            deal.quickDiligenceLedger = previousQuickLedger;
            deal.leadCategory = AcquisitionLeadCategory.Unset;
            deal.proofRequired = ShouldRequireProofOfFunds(listing, seller, pressure);
            deal.proofStatus = deal.proofRequired ? AcquisitionProofStatus.Requested : AcquisitionProofStatus.NotRequested;
            deal.proofPresentedDayIndex = -1;
            deal.requiredDiligenceMask = (int)ResolveRequiredDiligenceLayers(listing);
            deal.completedDiligenceMask = 0;

            MarkDealProgress(deal, AcquisitionDealStage.Inquiry, currentDay);
            RefreshDealWorkflowReadouts(listing, deal);
            deal.statusText = $"Inquiry reopened after failed-file review. Prior break: {priorBreakpoint}. {deal.proofOfFundsSummary} Earnest expected: {FormatMoney(earnest)}.";
            UpsertDeal(deal);
            marketStatus = deal.statusText;
            lastPurchaseSummary = deal.statusText;
            string inquiryNextStep = deal.proofRequired
                ? $"present proof of funds before earnest by day {deal.optionDeadlineDayIndex}"
                : $"commit earnest by day {deal.optionDeadlineDayIndex}";
            message = $"{deal.statusText} Next: {inquiryNextStep}.";
            Debug.Log($"[Acquisitions] {message}", this);
            return true;
        }

        public bool TryBuildSelectedConversationState(AcquisitionMarketSection section, out AcquisitionConversationState state)
        {
            EnsureMarket();
            state = null;
            if (!TryGetSelectedListing(section, out AcquisitionListing listing) || listing == null)
            {
                return false;
            }

            return TryBuildConversationState(listing.listingId, out state);
        }

        public bool TryBuildConversationState(string listingId, out AcquisitionConversationState state)
        {
            EnsureMarket();
            state = null;
            AcquisitionListing listing = FindListingById(listingId);
            if (listing == null)
            {
                return false;
            }

            AcquisitionDealState deal = FindDeal(listing.listingId);
            SellerProfile seller = BuildSellerProfile(listing, deal);
            SellerPressureResult pressure = new SellerPressureEvaluator().Evaluate(seller);
            int financingNeed = deal != null
                ? Mathf.Max(0, deal.financingNeedCents)
                : Mathf.Max(0, listing.askingPriceCents - GetAvailableCashCents());
            AcquisitionBuyerSeriousness seriousness = deal != null
                ? deal.seriousness
                : EvaluateBuyerSeriousness(listing, financingNeed, GetAvailableCashCents(), IsWatchedListing(listing.listingId));

            state = AcquisitionConversationPresenter.BuildPlaceholderState(
                listing,
                deal,
                BuildConversationActionLabel(deal),
                FormatSellerMotive(seller.motive),
                $"Pressure {FormatPercent(pressure.Pressure01)}",
                FormatBuyerSeriousness(seriousness),
                BuildSelectedFundingSummary(listing, deal),
                BuildLeadQualitySummary(listing, deal),
                BuildProofOfFundsSummary(listing, deal),
                BuildDiligenceLayerSummary(listing, deal),
                BuildClosingRiskSummary(listing, deal),
                BuildSelectedTimingPressureSummary(deal),
                BuildSelectedNextStepSummary(listing, deal),
                BuildSelectedLeadDecisionSummary(listing, deal),
                BuildSelectedLeadActionChecklist(listing, deal),
                BuildSelectedLeadAccessSummary(listing, deal));

            return state != null;
        }

        public bool TryBuildUnavailableConversationReadout(string listingId, out AcquisitionSellerMeetingUnavailableReadout readout)
        {
            EnsureMarket();
            readout = BuildUnavailableConversationReadout(listingId);
            return readout != null;
        }

        public bool TryAdvanceConversationDeal(string listingId, out string message)
        {
            return TryAdvanceListing(listingId, out message);
        }

        public bool TryApplyConversationOption(string listingId, AcquisitionConversationOptionKind kind, out string message)
        {
            EnsureMarket();
            AcquisitionListing listing = FindListingById(listingId);
            if (listing == null)
            {
                message = "No matching acquisition lead is available.";
                return false;
            }

            AcquisitionDealState deal = FindDeal(listing.listingId);
            switch (kind)
            {
                case AcquisitionConversationOptionKind.SellerPosition:
                    return RecordConversationReadout(listing, deal, BuildConversationSellerPositionReadout(listing, deal), out message);
                case AcquisitionConversationOptionKind.Funding:
                    return RecordConversationReadout(listing, deal, BuildConversationFundingReadout(listing, deal), out message);
                case AcquisitionConversationOptionKind.Diligence:
                    return TryRecordConversationDiligenceReadout(listing, out message);
                case AcquisitionConversationOptionKind.ClosingRisk:
                    return RecordConversationReadout(listing, deal, BuildConversationClosingRiskReadout(listing, deal), out message);
                case AcquisitionConversationOptionKind.DealFollowUp:
                    return RecordConversationReadout(listing, deal, BuildConversationFollowUpReadout(listing, deal), out message);
                case AcquisitionConversationOptionKind.Information:
                default:
                    return RecordConversationReadout(listing, deal, BuildConversationProcessReadout(listing, deal), out message);
            }
        }

        private AcquisitionSellerMeetingUnavailableReadout BuildUnavailableConversationReadout(string listingId)
        {
            string rawFileId = string.IsNullOrWhiteSpace(listingId) ? string.Empty : listingId.Trim();
            string fileId = string.IsNullOrWhiteSpace(rawFileId) ? "none" : rawFileId;
            AcquisitionDealState deal = FindDeal(rawFileId);
            string reason = BuildUnavailableListingReason(rawFileId, deal);
            string stage = deal != null
                ? $"Stage: {BuildDealStageReadout(deal)}"
                : "Stage: no live meeting file";
            string title = deal != null && deal.stage == AcquisitionDealStage.Failed
                ? "Acquisition File Failed"
                : deal != null && deal.stage == AcquisitionDealStage.Closed
                    ? "Acquisition Transfer Recorded"
                    : "Seller Meeting Unavailable";
            string action = deal != null && deal.stage == AcquisitionDealStage.Failed
                ? "Formal action: reopen requires a live listing and a recorded failure review."
                : "Formal action: no seller-meeting advance is available from this overlay state.";

            return new AcquisitionSellerMeetingUnavailableReadout
            {
                Title = title,
                Stage = stage,
                SellerIdentity = $"Counterparty: unavailable | Seller posture: not currently in meeting | Buyer footing: read the file reason",
                SelectedFile = $"Selected file: {fileId}",
                ProcessAction = action,
                SellerLine = reason,
                DialoguePrompt = "Return to the acquisition workflow and decide whether this file belongs in watch, review, or replacement lead work.",
                Posture = BuildUnavailablePostureSummary(deal),
                Readiness = BuildUnavailableReadinessSummary(rawFileId, deal),
                ResponseReadout = reason,
                PrimaryActionLabel = "No Action"
            };
        }

        private static string BuildUnavailablePostureSummary(AcquisitionDealState deal)
        {
            if (deal == null)
            {
                return "Posture: no active seller posture is available.";
            }

            if (deal.stage == AcquisitionDealStage.Failed)
            {
                return BuildFailureChecklistSummary(deal);
            }

            if (deal.stage == AcquisitionDealStage.Closed)
            {
                return "Posture: acquisition already closed; seller meeting is no longer the right surface.";
            }

            return "Posture: the live meeting could not be rebuilt from the current market state.";
        }

        private string BuildUnavailableReadinessSummary(string listingId, AcquisitionDealState deal)
        {
            if (deal != null && deal.stage == AcquisitionDealStage.Failed)
            {
                string next = BuildFailedLeadNextStepSummary(deal);
                string timing = BuildSelectedTimingPressureSummary(deal);
                return $"Readiness: {next}. {timing}";
            }

            if (deal != null && deal.stage == AcquisitionDealStage.Closed)
            {
                return "Readiness: shift to transfer, integration, and portfolio follow-through.";
            }

            if (deal != null)
            {
                return $"Readiness: {BuildDealStageReadout(deal)} file exists, but the live listing could not be rebuilt.";
            }

            if (TryParseAcquisitionListingKey(listingId, out AcquisitionListingKind kind, out int id))
            {
                if (kind == AcquisitionListingKind.Business)
                {
                    PlacedBuilding building = GetBuilding(id);
                    if (building != null && IsPlayerOwnedBusiness(building))
                    {
                        return "Readiness: this business is already in the player portfolio; shift to integration or management.";
                    }
                }
                else
                {
                    TownPlot plot = GetPlot(id);
                    if (plot != null && IsPlayerOwnedPlot(plot))
                    {
                        return "Readiness: this land is already in the player portfolio; shift to project, resale, or assembly decisions.";
                    }
                }
            }

            return "Readiness: select a current live lead before opening another seller meeting.";
        }

        private string BuildUnavailableListingReason(string listingId, AcquisitionDealState deal)
        {
            if (string.IsNullOrWhiteSpace(listingId))
            {
                return "No seller meeting can open because no acquisition lead is selected.";
            }

            if (deal != null && deal.stage == AcquisitionDealStage.Closed)
            {
                return "This acquisition has already closed; the useful work is now transfer and integration follow-through.";
            }

            if (deal != null && deal.stage == AcquisitionDealStage.Failed)
            {
                string breakpoint = BuildFailureBreakpointLabel(deal.failedFromStage);
                string failure = string.IsNullOrWhiteSpace(deal.closingFailureSummary)
                    ? "No detailed failure note was preserved."
                    : deal.closingFailureSummary.Trim();
                return $"This acquisition file failed during {breakpoint}. {failure}";
            }

            if (!TryParseAcquisitionListingKey(listingId, out AcquisitionListingKind kind, out int id))
            {
                return "The seller meeting references a file that is not part of the current acquisition market.";
            }

            if (kind == AcquisitionListingKind.Business)
            {
                PlacedBuilding building = GetBuilding(id);
                if (building == null)
                {
                    return "The business listing no longer maps to a known building; the market file was removed or regenerated.";
                }

                if (IsPlayerOwnedBusiness(building))
                {
                    return "That business is already owned by the player; continue from portfolio management rather than seller meeting.";
                }

                return "That business is no longer in the current acquisition list. The seller may have withdrawn, the file may have expired, or another buyer may have moved.";
            }

            TownPlot plot = GetPlot(id);
            if (plot == null)
            {
                return "The land listing no longer maps to a known plot; the market file was removed or regenerated.";
            }

            if (IsPlayerOwnedPlot(plot))
            {
                return "That land is already owned by the player; continue from project, resale, or portfolio management.";
            }

            if (plot.buildingId >= 0)
            {
                return "That land file no longer represents an empty parcel; the site has changed and must be reviewed through the current market.";
            }

            return "That land is no longer in the current acquisition list. The seller may have withdrawn, the file may have expired, or another buyer may have moved.";
        }

        private static bool TryParseAcquisitionListingKey(string listingId, out AcquisitionListingKind kind, out int id)
        {
            kind = AcquisitionListingKind.Land;
            id = -1;
            if (string.IsNullOrWhiteSpace(listingId))
            {
                return false;
            }

            string trimmed = listingId.Trim();
            if (trimmed.StartsWith("business_", StringComparison.OrdinalIgnoreCase))
            {
                kind = AcquisitionListingKind.Business;
                return int.TryParse(trimmed.Substring("business_".Length), out id);
            }

            if (trimmed.StartsWith("land_", StringComparison.OrdinalIgnoreCase))
            {
                kind = AcquisitionListingKind.Land;
                return int.TryParse(trimmed.Substring("land_".Length), out id);
            }

            return false;
        }

        public bool TryPresentProofOfFunds(string listingId, out string message)
        {
            EnsureMarket();
            AcquisitionListing listing = FindListingById(listingId);
            if (listing == null)
            {
                message = "No matching acquisition lead is available.";
                return false;
            }

            AcquisitionDealState deal = FindDeal(listingId);
            if (deal == null || deal.stage != AcquisitionDealStage.Inquiry)
            {
                message = "Proof of funds is only presented during an open inquiry.";
                return false;
            }

            return PresentProofOfFunds(listing, deal, out message);
        }

        public bool TryCycleListingSeriousness(string listingId, out string message)
        {
            EnsureMarket();
            AcquisitionListing listing = FindListingById(listingId);
            if (listing == null)
            {
                message = "No matching acquisition lead is available.";
                return false;
            }

            AcquisitionDealState deal = EnsureLeadNoteDeal(listing);
            deal.seriousness = NextSeriousness(deal.seriousness);
            marketStatus = $"{GetListingDisplayName(listing)} seriousness set to {FormatBuyerSeriousness(deal.seriousness)}.";
            lastPurchaseSummary = marketStatus;
            message = marketStatus;
            return true;
        }

        public bool TryCycleListingIntegrationStance(string listingId, out string message)
        {
            EnsureMarket();
            AcquisitionListing listing = FindListingById(listingId);
            if (listing == null)
            {
                message = "No matching acquisition lead is available.";
                return false;
            }

            AcquisitionDealState deal = EnsureLeadNoteDeal(listing);
            deal.integrationStance = NextIntegrationStance(deal.integrationStance);
            marketStatus = $"{GetListingDisplayName(listing)} stance set to {FormatIntegrationStance(deal.integrationStance)}.";
            lastPurchaseSummary = marketStatus;
            message = marketStatus;
            return true;
        }

        public bool TryRevealQuickDiligenceClue(string listingId, AcquisitionDiligenceClueKind kind, out string message)
        {
            EnsureMarket();
            AcquisitionListing listing = FindListingById(listingId);
            if (listing == null)
            {
                message = "No matching acquisition lead is available.";
                return false;
            }

            AcquisitionDealState deal = EnsureLeadNoteDeal(listing);
            int bit = 1 << Mathf.Clamp((int)kind, 0, 30);
            if ((deal.revealedDiligenceMask & bit) != 0)
            {
                message = BuildQuickDiligenceLedgerLine(listing, deal, kind);
                marketStatus = $"Already known: {message}";
                return true;
            }

            deal.revealedDiligenceMask |= bit;
            deal.lastQuickDiligenceDayIndex = GetCurrentDayIndex();
            string line = BuildQuickDiligenceLedgerLine(listing, deal, kind);
            deal.quickDiligenceLedger = AppendLedgerLine(deal.quickDiligenceLedger, $"Day {deal.lastQuickDiligenceDayIndex}: {line}");
            deal.scoutingSummary = string.IsNullOrWhiteSpace(deal.scoutingSummary)
                ? line
                : $"{deal.scoutingSummary} {line}";
            deal.statusText = $"Quick diligence: {line}";
            marketStatus = deal.statusText;
            lastPurchaseSummary = deal.statusText;
            message = deal.statusText;
            return true;
        }

        private bool TryRecordConversationDiligenceReadout(AcquisitionListing listing, out string message)
        {
            if (listing == null)
            {
                message = "No matching acquisition lead is available.";
                return false;
            }

            AcquisitionDealState deal = EnsureLeadNoteDeal(listing);
            if (TryResolveNextConversationDiligenceClue(listing, deal, out AcquisitionDiligenceClueKind clueKind))
            {
                return TryRevealQuickDiligenceClue(listing.listingId, clueKind, out message);
            }

            string ledger = BuildRecentQuickDiligenceSummary(deal);
            string nextFormalStep = deal.stage == AcquisitionDealStage.EarnestCommitted
                ? "Use the formal advance action to clear the next diligence layer."
                : "Open inquiry and commit earnest before treating these notes as formal diligence.";
            return RecordConversationReadout(
                listing,
                deal,
                $"Quick diligence: first-pass field notes are already recorded. {ledger} {nextFormalStep}".Trim(),
                out message);
        }

        private bool RecordConversationReadout(AcquisitionListing listing, AcquisitionDealState deal, string readout, out string message)
        {
            message = string.IsNullOrWhiteSpace(readout)
                ? "Conversation note recorded."
                : readout.Trim();

            if (deal != null)
            {
                RefreshDealWorkflowReadouts(listing, deal);
                deal.statusText = message;
            }

            marketStatus = message;
            lastPurchaseSummary = message;
            return true;
        }

        private string BuildConversationSellerPositionReadout(AcquisitionListing listing, AcquisitionDealState deal)
        {
            SellerProfile seller = BuildSellerProfile(listing, deal);
            SellerPressureResult pressure = new SellerPressureEvaluator().Evaluate(seller);
            string motive = FormatSellerMotive(seller.motive);
            string access = BuildSelectedLeadAccessSummary(listing, deal);
            string decision = BuildSelectedLeadDecisionSummary(listing, deal);
            return $"Seller position: motive {motive}; pressure {FormatPercent(pressure.Pressure01)}; relationship sensitivity {FormatPercent(pressure.RelationshipSensitivity01)}. {access} {decision}";
        }

        private string BuildConversationFundingReadout(AcquisitionListing listing, AcquisitionDealState deal)
        {
            string funding = BuildSelectedFundingSummary(listing, deal);
            string proof = BuildProofOfFundsSummary(listing, deal);
            string owner = BuildSelectedOwnerReadSummary(listing, deal);
            return $"Funding read: {funding} {proof} {owner}";
        }

        private string BuildConversationProcessReadout(AcquisitionListing listing, AcquisitionDealState deal)
        {
            string stage = deal == null ? "Scouting" : FormatDealStage(deal.stage);
            string next = BuildSelectedNextStepSummary(listing, deal);
            string timing = BuildSelectedTimingPressureSummary(deal);
            string checklist = BuildSelectedLeadActionChecklist(listing, deal);
            return $"Process read: {stage}. Next: {next}. {timing} {checklist}";
        }

        private string BuildConversationClosingRiskReadout(AcquisitionListing listing, AcquisitionDealState deal)
        {
            string risk = BuildClosingRiskSummary(listing, deal);
            string timing = BuildSelectedTimingPressureSummary(deal);
            string runway = BuildSelectedRunwayPressureSummary(listing, deal);
            return $"Closing risk read: {risk} {timing} {runway}";
        }

        private string BuildConversationFollowUpReadout(AcquisitionListing listing, AcquisitionDealState deal)
        {
            string next = BuildSelectedNextStepSummary(listing, deal);
            string decision = BuildSelectedLeadDecisionSummary(listing, deal);
            string checklist = BuildSelectedLeadActionChecklist(listing, deal);
            return $"Follow-up read: next move is to {next}. {decision} {checklist}";
        }

        private bool TryResolveNextConversationDiligenceClue(
            AcquisitionListing listing,
            AcquisitionDealState deal,
            out AcquisitionDiligenceClueKind clueKind)
        {
            AcquisitionDiligenceClueKind[] ordered = listing != null && listing.kind == AcquisitionListingKind.Business
                ? new[]
                {
                    AcquisitionDiligenceClueKind.VisibleCondition,
                    AcquisitionDiligenceClueKind.Staffing,
                    AcquisitionDiligenceClueKind.SupplierWeakness,
                    AcquisitionDiligenceClueKind.RoughValue,
                    AcquisitionDiligenceClueKind.Reliability,
                    AcquisitionDiligenceClueKind.DemandFit
                }
                : new[]
                {
                    AcquisitionDiligenceClueKind.VisibleCondition,
                    AcquisitionDiligenceClueKind.DemandFit,
                    AcquisitionDiligenceClueKind.RoughValue,
                    AcquisitionDiligenceClueKind.SupplierWeakness,
                    AcquisitionDiligenceClueKind.Reliability,
                    AcquisitionDiligenceClueKind.Staffing
                };

            for (int i = 0; i < ordered.Length; i++)
            {
                int bit = 1 << Mathf.Clamp((int)ordered[i], 0, 30);
                if (deal == null || (deal.revealedDiligenceMask & bit) == 0)
                {
                    clueKind = ordered[i];
                    return true;
                }
            }

            clueKind = AcquisitionDiligenceClueKind.VisibleCondition;
            return false;
        }

        private static string BuildRecentQuickDiligenceSummary(AcquisitionDealState deal)
        {
            if (deal == null || string.IsNullOrWhiteSpace(deal.quickDiligenceLedger))
            {
                return "No ledger lines are available.";
            }

            string[] lines = deal.quickDiligenceLedger.Replace("\r", string.Empty).Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string line = lines[i].Trim();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    return $"Latest note: {line}";
                }
            }

            return "No ledger lines are available.";
        }

        public bool IsSelectedListingWatched(AcquisitionMarketSection section)
        {
            EnsureMarket();
            return TryGetSelectedListing(section, out AcquisitionListing listing)
                && listing != null
                && IsWatchedListing(listing.listingId);
        }

        public bool CycleSelectedIntegrationStance(AcquisitionMarketSection section, out string message)
        {
            EnsureMarket();
            if (!TryGetSelectedListing(section, out AcquisitionListing listing) || listing == null)
            {
                message = "No listing is selected.";
                return false;
            }

            AcquisitionDealState deal = FindDeal(listing.listingId);
            if (deal == null)
            {
                message = "Open the process first before setting a post-close stance.";
                return false;
            }

            deal.integrationStance = NextIntegrationStance(deal.integrationStance);
            marketStatus = $"{GetListingDisplayName(listing)} stance set to {FormatIntegrationStance(deal.integrationStance)}.";
            lastPurchaseSummary = marketStatus;
            message = marketStatus;
            return true;
        }

        public string GetSelectedIntegrationStanceLabel(AcquisitionMarketSection section)
        {
            EnsureMarket();
            if (!TryGetSelectedListing(section, out AcquisitionListing listing) || listing == null)
            {
                return "No Stance";
            }

            AcquisitionDealState deal = FindDeal(listing.listingId);
            return deal == null ? "No Stance" : FormatIntegrationStance(deal.integrationStance);
        }

        public bool CycleSelectedSeriousness(AcquisitionMarketSection section, out string message)
        {
            EnsureMarket();
            if (!TryGetSelectedListing(section, out AcquisitionListing listing) || listing == null)
            {
                message = "No listing is selected.";
                return false;
            }

            AcquisitionDealState deal = FindDeal(listing.listingId);
            if (deal == null)
            {
                message = "Open the process first before adjusting buyer seriousness.";
                return false;
            }

            deal.seriousness = NextSeriousness(deal.seriousness);
            marketStatus = $"{GetListingDisplayName(listing)} seriousness set to {FormatBuyerSeriousness(deal.seriousness)}.";
            lastPurchaseSummary = marketStatus;
            message = marketStatus;
            return true;
        }

        public string GetSelectedSeriousnessLabel(AcquisitionMarketSection section)
        {
            EnsureMarket();
            if (!TryGetSelectedListing(section, out AcquisitionListing listing) || listing == null)
            {
                return "No Seriousness";
            }

            AcquisitionDealState deal = FindDeal(listing.listingId);
            return deal == null ? "No Seriousness" : FormatBuyerSeriousness(deal.seriousness);
        }

        public string BuildSelectedProcessSummary(AcquisitionMarketSection section)
        {
            EnsureMarket();
            if (!TryGetSelectedListing(section, out AcquisitionListing listing) || listing == null)
            {
                return section == AcquisitionMarketSection.Businesses
                    ? "Business Acquisition: no selected opportunity."
                    : "Land Acquisition: no selected opportunity.";
            }

            AcquisitionDealState deal = FindDeal(listing.listingId);
            string headline = section == AcquisitionMarketSection.Businesses ? "Business Acquisition" : "Land Acquisition";
            string stageText = deal == null ? "Scouting" : FormatDealStage(deal.stage);
            string seriousnessText = deal == null ? BuildBuyerReadinessSummary(listing) : FormatBuyerSeriousness(deal.seriousness);
            string stanceText = deal == null || deal.integrationStance == AcquisitionIntegrationStance.None
                ? "No stance yet"
                : FormatIntegrationStance(deal.integrationStance);
            string proofText = deal == null ? "not opened" : FormatProofStatus(deal.proofStatus);
            string diligenceText = deal == null
                ? "0/0"
                : $"{CountDiligenceLayers(deal.completedDiligenceMask)}/{Mathf.Max(1, CountDiligenceLayers(deal.requiredDiligenceMask))}";
            string watchText = IsWatchedListing(listing.listingId) ? "Marked for later" : "Not marked";
            string fundingText = BuildSelectedFundingSummary(listing, deal);
            string nextStepText = BuildSelectedNextStepSummary(listing, deal);
            string timingText = BuildSelectedTimingPressureSummary(deal);
            return $"{headline}: {GetListingDisplayName(listing)} | Stage {stageText} | Seriousness {seriousnessText} | Proof {proofText} | Diligence {diligenceText} | Stance {stanceText} | {watchText} | {fundingText} | Next {nextStepText} | {timingText}";
        }

        public string BuildDealPipelineSummary()
        {
            EnsureMarket();
            int leadNotes = CountLeadNoteOnlyDeals();
            int failed = CountFailedDealFiles();
            int liveTotal = CountActiveLiveDeals();
            if (liveTotal <= 0)
            {
                if (leadNotes > 0 || failed > 0)
                {
                    return $"Pipeline: no live acquisition process | {leadNotes} scouting file(s) | {failed} failed file(s).";
                }

                return "Pipeline: no active acquisition processes.";
            }

            int inquiry = 0, earnest = 0, diligence = 0, tentative = 0;
            for (int i = 0; i < activeDeals.Count; i++)
            {
                AcquisitionDealState deal = activeDeals[i];
                if (!IsLiveAcquisitionProcess(deal))
                {
                    continue;
                }

                switch (deal.stage)
                {
                    case AcquisitionDealStage.Inquiry: inquiry++; break;
                    case AcquisitionDealStage.EarnestCommitted: earnest++; break;
                    case AcquisitionDealStage.DiligenceComplete: diligence++; break;
                    case AcquisitionDealStage.TentativeAgreement: tentative++; break;
                }
            }

            string fileTail = leadNotes > 0 || failed > 0
                ? $" | {leadNotes} scouting file(s) | {failed} failed file(s)"
                : string.Empty;
            return $"Pipeline: {inquiry} inquiry | {earnest} earnest | {diligence} diligence | {tentative} tentative | {liveTotal} live total{fileTail}.";
        }

        public string BuildAcquisitionLiquiditySummary()
        {
            int cash = Mathf.Max(0, GetAvailableCashCents());
            bool lenderReady = playerDebtManager != null && playerDebtManager.CanSubmitApplication;
            return lenderReady
                ? $"Liquidity: {FormatMoney(cash)} on hand | financing remains available."
                : $"Liquidity: {FormatMoney(cash)} on hand | financing is limited.";
        }

        public string BuildWatchlistSummaryText()
        {
            EnsureMarket();
            if (watchedListingIds.Count <= 0)
            {
                return "Watchlist: none.";
            }

            int landCount = 0;
            int businessCount = 0;
            for (int i = 0; i < watchedListingIds.Count; i++)
            {
                string listingId = watchedListingIds[i];
                if (string.IsNullOrWhiteSpace(listingId))
                {
                    continue;
                }

                if (listingId.StartsWith("business_", StringComparison.OrdinalIgnoreCase))
                {
                    businessCount++;
                }
                else
                {
                    landCount++;
                }
            }

            return $"Watchlist: {landCount} land | {businessCount} businesses.";
        }

        public string BuildAcquisitionDashboardSnapshotSummary(AcquisitionMarketSection section)
        {
            EnsureMarket();
            string sectionLabel = section == AcquisitionMarketSection.Businesses ? "Businesses" : "Land";
            StringBuilder builder = new();
            builder.AppendLine("Market Snapshot");
            builder.AppendLine($"Section: {sectionLabel}");
            builder.AppendLine(BuildWatchlistSummaryText());
            builder.AppendLine(BuildDealPipelineSummary());
            builder.AppendLine(BuildAcquisitionLiquiditySummary());
            builder.AppendLine(BuildAcquisitionProcessPostureSummary());
            return builder.ToString().Trim();
        }

        public string BuildSelectedLeadCompactSummary(AcquisitionMarketSection section)
        {
            EnsureMarket();
            if (!TryGetSelectedListing(section, out AcquisitionListing listing) || listing == null)
            {
                return section == AcquisitionMarketSection.Businesses
                    ? "Selected Lead\nNo business opportunity selected."
                    : "Selected Lead\nNo land opportunity selected.";
            }

            AcquisitionDealState deal = FindDeal(listing.listingId);
            string stage = deal == null ? "Scouting" : FormatDealStage(deal.stage);
            string watch = IsWatchedListing(listing.listingId) ? "Marked for later" : "Not watched";

            StringBuilder builder = new();
            builder.AppendLine("Selected Lead");
            builder.AppendLine(GetListingDisplayName(listing));
            builder.AppendLine($"Ask: {FormatMoney(listing.askingPriceCents)} | Stage: {stage} | {watch}");
            builder.AppendLine(BuildFieldReadSummary(listing));
            builder.AppendLine(BuildSelectedLeadAccessSummary(listing, deal));
            builder.AppendLine($"Next step: {BuildSelectedNextStepSummary(listing, deal)}");
            return builder.ToString().Trim();
        }

        public string BuildAcquisitionCompactProcessNotesSummary(AcquisitionMarketSection section)
        {
            EnsureMarket();
            StringBuilder builder = new();
            builder.AppendLine("Process Notes");

            AcquisitionDealState selectedDeal = null;
            if (TryGetSelectedListing(section, out AcquisitionListing listing) && listing != null)
            {
                selectedDeal = FindDeal(listing.listingId);
                if (selectedDeal != null)
                {
                    builder.AppendLine($"Current step: {FormatDealStage(selectedDeal.stage)} | Next: {GetActionLabelForDeal(selectedDeal)}");
                }
                else
                {
                    builder.AppendLine("Current step: scouting only. No live process is open yet.");
                }

                if (!string.IsNullOrWhiteSpace(selectedDeal != null ? selectedDeal.quickDiligenceLedger : string.Empty))
                {
                    builder.AppendLine("Quick diligence");
                    string[] ledgerLines = selectedDeal.quickDiligenceLedger.Replace("\r", string.Empty).Split('\n');
                    int kept = 0;
                    for (int i = 0; i < ledgerLines.Length; i++)
                    {
                        string line = ledgerLines[i].Trim();
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        builder.AppendLine($"• {line}");
                        kept++;
                        if (kept >= 4)
                        {
                            break;
                        }
                    }
                }
                else
                {
                    builder.AppendLine("Quick diligence: none recorded yet.");
                }
            }
            else
            {
                builder.AppendLine("Current step: no selected lead.");
            }

            builder.AppendLine(BuildAcquisitionProcessLedgerSummary());
            builder.AppendLine(BuildAcquisitionWeeklyReviewSummary());
            return builder.ToString().Trim();
        }

        public bool IsDiligenceClueRevealed(string listingId, AcquisitionDiligenceClueKind kind)
        {
            AcquisitionDealState deal = FindDeal(listingId);
            if (deal == null)
            {
                return false;
            }

            int bit = 1 << (int)kind;
            return (deal.revealedDiligenceMask & bit) != 0;
        }


        public string BuildAcquisitionReadinessHeadline()
        {
            EnsureMarket();
            string selectedLeadRead = BuildSelectedLeadReadinessSummary(AcquisitionMarketSection.Land);
            if (string.IsNullOrWhiteSpace(selectedLeadRead))
            {
                selectedLeadRead = BuildSelectedLeadReadinessSummary(AcquisitionMarketSection.Businesses);
            }

            string processLedger = BuildAcquisitionProcessLedgerSummary();
            string decisionClimate = BuildAcquisitionDecisionClimateSummary();
            string weeklyReview = BuildAcquisitionWeeklyReviewSummary();
            return string.IsNullOrWhiteSpace(selectedLeadRead)
                ? $"{BuildWatchlistSummaryText()} {BuildDealPipelineSummary()} {BuildAcquisitionLiquiditySummary()} {BuildAcquisitionProcessPostureSummary()} {processLedger} {decisionClimate} {weeklyReview}".Trim()
                : $"{BuildWatchlistSummaryText()} {BuildDealPipelineSummary()} {BuildAcquisitionLiquiditySummary()} {BuildAcquisitionProcessPostureSummary()} {processLedger} {decisionClimate} {weeklyReview} {selectedLeadRead}".Trim();
        }

        public string BuildAcquisitionWeeklyReviewSummary()
        {
            EnsureMarket();
            if (!string.IsNullOrWhiteSpace(lastAcquisitionReviewSummary))
            {
                return lastAcquisitionReviewSummary;
            }

            int currentDay = GetCurrentDayIndex();
            int live = 0;
            int urgent = 0;
            int financingLed = 0;
            int stalled = 0;
            for (int i = 0; i < activeDeals.Count; i++)
            {
                AcquisitionDealState deal = activeDeals[i];
                if (!IsLiveAcquisitionProcess(deal))
                {
                    continue;
                }

                live++;
                if (deal.financingNeedCents > 0)
                {
                    financingLed++;
                }

                if (IsDealUrgent(deal, currentDay))
                {
                    urgent++;
                }

                stalled += Mathf.Max(0, deal.stalledReviewCount);
            }

            if (live <= 0)
            {
                int leadNotes = CountLeadNoteOnlyDeals();
                return leadNotes > 0
                    ? $"Weekly review: {leadNotes} scouting file(s) have notes, but no live acquisition process requires action."
                    : "Weekly review: no live acquisition process requires action.";
            }

            return $"Weekly review: {live} live | {urgent} urgent | {financingLed} financing-led | {stalled} stalled review mark(s).";
        }

        public string ProcessWeeklyAcquisitionReview()
        {
            EnsureMarket();
            return RunWeeklyAcquisitionReview(GetCurrentDayIndex());
        }

        public string DebugRunWeeklyAcquisitionReviewAtDay(int currentDayIndex)
        {
            EnsureMarket();
            return RunWeeklyAcquisitionReview(currentDayIndex);
        }

        private int EstimateProjectedWeeklyBurdenCents(AcquisitionListing listing, AcquisitionDealState deal)
        {
            int priced = deal != null && deal.tentativePriceCents > 0
                ? deal.tentativePriceCents
                : Mathf.Max(0, listing != null ? listing.askingPriceCents : 0);
            int financingNeed = Mathf.Max(0, deal != null ? deal.financingNeedCents : 0);
            if (financingNeed > 0)
            {
                return Mathf.Max(5000, Mathf.Max(financingNeed / 24, priced / 180));
            }

            return priced > 0 ? Mathf.Max(2500, priced / 220) : 0;
        }

        private int CountActiveLiveDeals()
        {
            int count = 0;
            for (int i = 0; i < activeDeals.Count; i++)
            {
                if (IsLiveAcquisitionProcess(activeDeals[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private int CountLeadNoteOnlyDeals()
        {
            int count = 0;
            for (int i = 0; i < activeDeals.Count; i++)
            {
                if (IsLeadNoteOnlyDeal(activeDeals[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private int CountFailedDealFiles()
        {
            int count = 0;
            for (int i = 0; i < activeDeals.Count; i++)
            {
                AcquisitionDealState deal = activeDeals[i];
                if (deal != null && deal.stage == AcquisitionDealStage.Failed)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsLiveAcquisitionProcess(AcquisitionDealState deal)
        {
            return deal != null
                && deal.stage != AcquisitionDealStage.None
                && deal.stage != AcquisitionDealStage.Failed
                && deal.stage != AcquisitionDealStage.Closed;
        }

        private static bool IsLeadNoteOnlyDeal(AcquisitionDealState deal)
        {
            return deal != null && deal.stage == AcquisitionDealStage.None;
        }

        private int CountStalledLiveDeals()
        {
            int stalled = 0;
            for (int i = 0; i < activeDeals.Count; i++)
            {
                AcquisitionDealState deal = activeDeals[i];
                if (!IsLiveAcquisitionProcess(deal))
                {
                    continue;
                }

                stalled += Mathf.Max(0, deal.stalledReviewCount);
            }

            return stalled;
        }

        private string BuildSelectedRunwayPressureSummary(AcquisitionListing listing, AcquisitionDealState deal)
        {
            if (playerPortfolio == null)
            {
                return string.Empty;
            }

            int priced = deal != null && deal.tentativePriceCents > 0 ? deal.tentativePriceCents : Mathf.Max(0, listing != null ? listing.askingPriceCents : 0);
            int earnest = Mathf.Max(0, deal != null ? deal.earnestMoneyCents : 0);
            int burden = EstimateProjectedWeeklyBurdenCents(listing, deal);
            return playerPortfolio.BuildOwnerCommitmentClimateSummary(priced, earnest, burden, CountActiveLiveDeals(), CountStalledLiveDeals());
        }

        public string BuildSelectedLeadRunwaySummary(AcquisitionMarketSection section)
        {
            EnsureMarket();
            if (!TryGetSelectedListing(section, out AcquisitionListing listing) || listing == null)
            {
                return string.Empty;
            }

            AcquisitionDealState deal = FindDeal(listing.listingId);
            return BuildSelectedRunwayPressureSummary(listing, deal);
        }

        private string BuildSelectedExecutionLoadSummary(AcquisitionListing listing, AcquisitionDealState deal)
        {
            if (playerPortfolio == null)
            {
                return string.Empty;
            }

            int priced = deal != null && deal.tentativePriceCents > 0 ? deal.tentativePriceCents : Mathf.Max(0, listing != null ? listing.askingPriceCents : 0);
            int earnest = Mathf.Max(0, deal != null ? deal.earnestMoneyCents : 0);
            int burden = EstimateProjectedWeeklyBurdenCents(listing, deal);
            return playerPortfolio.BuildOwnerExecutionClimateSummary(priced, earnest, burden, CountActiveLiveDeals(), CountStalledLiveDeals());
        }

        public string BuildSelectedLeadExecutionLoadSummary(AcquisitionMarketSection section)
        {
            EnsureMarket();
            if (!TryGetSelectedListing(section, out AcquisitionListing listing) || listing == null)
            {
                return string.Empty;
            }

            AcquisitionDealState deal = FindDeal(listing.listingId);
            return BuildSelectedExecutionLoadSummary(listing, deal);
        }

        public string BuildSelectedLeadReadinessSummary(AcquisitionMarketSection section)
        {
            EnsureMarket();
            if (!TryGetSelectedListing(section, out AcquisitionListing listing) || listing == null)
            {
                return string.Empty;
            }

            AcquisitionDealState deal = FindDeal(listing.listingId);
            string ownerRead = BuildSelectedOwnerReadSummary(listing, deal);
            string stagedCommitmentRead = BuildSelectedStagedCommitmentSummary(listing, deal);
            string runway = BuildSelectedRunwayPressureSummary(listing, deal);
            string execution = BuildSelectedExecutionLoadSummary(listing, deal);
            string timing = BuildSelectedTimingPressureSummary(deal);
            string action = BuildSelectedNextStepSummary(listing, deal);
            string decision = BuildSelectedLeadDecisionSummary(listing, deal);
            string checklist = BuildSelectedLeadActionChecklist(listing, deal);
            string actionChecklist = checklist.StartsWith("Checklist:", StringComparison.OrdinalIgnoreCase)
                ? "Action checklist:" + checklist.Substring("Checklist:".Length)
                : $"Action checklist: {checklist}";

            StringBuilder builder = new();
            builder.AppendLine("Funding & Readiness");
            builder.AppendLine(ownerRead);
            builder.AppendLine(stagedCommitmentRead);
            builder.AppendLine(runway);
            builder.AppendLine(execution);
            builder.AppendLine(decision);
            builder.AppendLine(BuildSelectedLeadAccessSummary(listing, deal));
            builder.AppendLine(actionChecklist.Trim());
            builder.AppendLine($"Next step: {action}");
            builder.AppendLine(timing);
            return builder.ToString().Trim();
        }

        public string BuildSelectedLeadActionChecklist(AcquisitionMarketSection section)
        {
            EnsureMarket();
            if (!TryGetSelectedListing(section, out AcquisitionListing listing) || listing == null)
            {
                return string.Empty;
            }

            return BuildSelectedLeadActionChecklist(listing, FindDeal(listing.listingId));
        }

        public string BuildAcquisitionDecisionClimateSummary()
        {
            EnsureMarket();
            int live = 0;
            int ready = 0;
            int stalled = 0;
            int financingLed = 0;
            for (int i = 0; i < activeDeals.Count; i++)
            {
                AcquisitionDealState deal = activeDeals[i];
                if (!IsLiveAcquisitionProcess(deal))
                {
                    continue;
                }

                live++;
                if (deal.seriousness >= AcquisitionBuyerSeriousness.Ready)
                {
                    ready++;
                }

                if (deal.financingNeedCents > 0)
                {
                    financingLed++;
                }

                stalled += Mathf.Max(0, deal.stalledReviewCount);
            }

            return $"Decision climate: {live} live lead(s) | {ready} ready | {financingLed} financing-led | {stalled} stalled review(s) | {watchedListingIds.Count} watched.";
        }

        public string BuildAcquisitionProcessLedgerSummary()
        {
            EnsureMarket();
            int liveTotal = CountActiveLiveDeals();
            int leadNotes = CountLeadNoteOnlyDeals();
            int failed = CountFailedDealFiles();
            if (liveTotal <= 0)
            {
                if (leadNotes > 0 || failed > 0)
                {
                    return $"Process ledger: no live deal is open | {leadNotes} scouting file(s) | {failed} failed file(s) | {watchedListingIds.Count} watched.";
                }

                return watchedListingIds.Count > 0
                    ? $"Process ledger: {watchedListingIds.Count} watched lead(s), but no live deal is open yet."
                    : "Process ledger: no watched or active lead needs action yet.";
            }

            int inquiry = 0;
            int earnest = 0;
            int diligence = 0;
            int tentative = 0;
            int stalled = 0;
            for (int i = 0; i < activeDeals.Count; i++)
            {
                AcquisitionDealState deal = activeDeals[i];
                if (!IsLiveAcquisitionProcess(deal))
                {
                    continue;
                }

                switch (deal.stage)
                {
                    case AcquisitionDealStage.Inquiry:
                        inquiry++;
                        break;
                    case AcquisitionDealStage.EarnestCommitted:
                        earnest++;
                        break;
                    case AcquisitionDealStage.DiligenceComplete:
                        diligence++;
                        break;
                    case AcquisitionDealStage.TentativeAgreement:
                        tentative++;
                        break;
                }

                stalled += Mathf.Max(0, deal.stalledReviewCount);
            }

            string fileTail = leadNotes > 0 || failed > 0
                ? $" | Scouting files {leadNotes} | Failed files {failed}"
                : string.Empty;
            return $"Process ledger: {liveTotal} live | Inquiry {inquiry} | Earnest {earnest} | Diligence {diligence} | Tentative {tentative} | Stalled {stalled}{fileTail}.";
        }

        public string BuildAcquisitionProcessPostureSummary()
        {
            EnsureMarket();
            int liveTotal = CountActiveLiveDeals();
            if (liveTotal <= 0)
            {
                int leadNotes = CountLeadNoteOnlyDeals();
                return leadNotes > 0
                    ? $"Process posture: {leadNotes} scouting file(s) recorded, but no active deal is open yet."
                    : "Process posture: no active deal is open yet.";
            }

            int ready = 0;
            int financeHeavy = 0;
            for (int i = 0; i < activeDeals.Count; i++)
            {
                AcquisitionDealState deal = activeDeals[i];
                if (!IsLiveAcquisitionProcess(deal))
                {
                    continue;
                }

                if (deal.seriousness == AcquisitionBuyerSeriousness.Ready)
                {
                    ready++;
                }

                if (deal.financingNeedCents > 0)
                {
                    financeHeavy++;
                }
            }

            return $"Process posture: {ready} ready | {financeHeavy} still financing-led | {liveTotal} live.";
        }

        private string BuildSelectedLeadDecisionSummary(AcquisitionListing listing, AcquisitionDealState deal)
        {
            if (listing == null)
            {
                return "Decision: no lead selected.";
            }

            string seriousness = deal != null ? FormatBuyerSeriousness(deal.seriousness) : BuildBuyerReadinessSummary(listing);
            string stance = deal != null && deal.integrationStance != AcquisitionIntegrationStance.None
                ? FormatIntegrationStance(deal.integrationStance)
                : FormatIntegrationStance(RecommendIntegrationStance(listing, deal));
            return $"Decision: {seriousness}; stance {stance}.";
        }

        private string BuildSelectedLeadActionChecklist(AcquisitionListing listing, AcquisitionDealState deal)
        {
            if (listing == null)
            {
                return string.Empty;
            }

            if (deal == null)
            {
                return IsWatchedListing(listing.listingId)
                    ? "Checklist: open inquiry, confirm funding, then set seriousness."
                    : "Checklist: watch lead or open inquiry before spending diligence time.";
            }

            return deal.stage switch
            {
                AcquisitionDealStage.Inquiry => NeedsProofOfFunds(deal)
                    ? "Checklist: present proof of funds, set seriousness, then commit earnest."
                    : "Checklist: set seriousness, choose integration stance, then commit earnest if funding clears.",
                AcquisitionDealStage.EarnestCommitted => IsFormalDiligenceComplete(deal)
                    ? "Checklist: complete pricing review, confirm financing, and protect closing runway."
                    : $"Checklist: clear diligence layers ({BuildRemainingDiligenceLayerSummary(deal)}), confirm financing, and protect closing runway.",
                AcquisitionDealStage.DiligenceComplete => "Checklist: negotiate final terms or walk before the option window closes.",
                AcquisitionDealStage.TentativeAgreement => "Checklist: close funding, make final payment, and plan integration.",
                AcquisitionDealStage.Closed => "Checklist: transfer ownership and execute integration plan.",
                AcquisitionDealStage.Failed => BuildFailureChecklistSummary(deal),
                _ => "Checklist: open inquiry and confirm buyer posture."
            };
        }

        private string BuildSelectedLeadAccessSummary(AcquisitionListing listing, AcquisitionDealState deal)
        {
            if (listing == null)
            {
                return string.Empty;
            }

            if (deal != null && (deal.stage == AcquisitionDealStage.Closed || deal.stage == AcquisitionDealStage.Failed))
            {
                return "Access: live exclusivity no longer matters; read the file as a completed or failed case.";
            }

            int currentDay = GetCurrentDayIndex();
            string windowRead = BuildLeadAccessWindowRead(listing, deal, currentDay);
            string pressureRead = listing.pressure01 >= 0.68f
                ? "seller pressure is high"
                : listing.pressure01 >= 0.4f
                    ? "seller pressure is workable"
                    : "seller pressure is patient";
            string ageRead = listing.ageDays <= 0
                ? "freshly surfaced"
                : $"file age {Mathf.Max(0, listing.ageDays)} day(s)";

            return $"Access: {windowRead}; {pressureRead}; {ageRead}.";
        }

        // Keep access reads stage-aware so a protected earnest or closing lane is not flattened back into
        // the looser pre-inquiry listing window language.
        private static string BuildLeadAccessWindowRead(AcquisitionListing listing, AcquisitionDealState deal, int currentDay)
        {
            if (listing == null)
            {
                return string.Empty;
            }

            if (deal == null || deal.stage == AcquisitionDealStage.None)
            {
                return BuildPreInquiryAccessWindowRead(listing, currentDay);
            }

            if (deal.stage == AcquisitionDealStage.Inquiry)
            {
                return $"inquiry is open but not yet protected; {BuildPreInquiryAccessWindowRead(listing, currentDay)}";
            }

            if (deal.stage == AcquisitionDealStage.EarnestCommitted || deal.stage == AcquisitionDealStage.DiligenceComplete)
            {
                return deal.optionDeadlineDayIndex > 0
                    ? $"earnest protects the lane; {BuildOptionWindowRead(deal.optionDeadlineDayIndex - currentDay)}"
                    : "earnest protects the lane while diligence and terms are still being worked";
            }

            if (deal.stage == AcquisitionDealStage.TentativeAgreement)
            {
                return deal.closingDeadlineDayIndex > 0
                    ? $"the file is in protected closing; {BuildClosingWindowRead(deal.closingDeadlineDayIndex - currentDay)}"
                    : "the file is in protected closing while final paper and funding are being cleared";
            }

            return BuildPreInquiryAccessWindowRead(listing, currentDay);
        }

        private static string BuildPreInquiryAccessWindowRead(AcquisitionListing listing, int currentDay)
        {
            if (listing == null)
            {
                return string.Empty;
            }

            if (listing.aiEligible)
            {
                return listing.playerFirstUntilDayIndex >= 0
                    ? "rival buyers can act now"
                    : "the lead is open to broader competition";
            }

            if (listing.playerFirstUntilDayIndex >= 0)
            {
                int days = listing.playerFirstUntilDayIndex - currentDay;
                if (days <= 0)
                {
                    return "player-first lane is at its edge";
                }

                if (days <= 2)
                {
                    return $"player-first lane is short, about {days} day(s) remain";
                }

                return $"player-first lane holds about {days} more day(s)";
            }

            return "lead is not yet open to broader competition";
        }

        private static string BuildOptionWindowRead(int daysRemaining)
        {
            if (daysRemaining <= 0)
            {
                return "the option window is at its edge";
            }

            if (daysRemaining <= 2)
            {
                return $"option pressure is high, about {daysRemaining} day(s) remain";
            }

            return $"the option window holds about {daysRemaining} more day(s)";
        }

        private static string BuildClosingWindowRead(int daysRemaining)
        {
            if (daysRemaining <= 0)
            {
                return "the closing window is expiring now";
            }

            if (daysRemaining <= 2)
            {
                return $"closing pressure is high, about {daysRemaining} day(s) remain";
            }

            return $"the closing window holds about {daysRemaining} more day(s)";
        }

        public string BuildListingInspectionSummaryByPlotId(int plotId)
        {
            EnsureMarket();
            if (plotId < 0)
            {
                return string.Empty;
            }

            AcquisitionListing listing = null;
            for (int i = 0; i < landListings.Count; i++)
            {
                AcquisitionListing candidate = landListings[i];
                if (candidate != null && candidate.plotId == plotId)
                {
                    listing = candidate;
                    break;
                }
            }

            return BuildListingInspectionSummary(listing);
        }

        public string BuildListingInspectionSummaryByBuildingId(int buildingId)
        {
            EnsureMarket();
            if (buildingId < 0)
            {
                return string.Empty;
            }

            AcquisitionListing listing = null;
            for (int i = 0; i < businessListings.Count; i++)
            {
                AcquisitionListing candidate = businessListings[i];
                if (candidate != null && candidate.buildingId == buildingId)
                {
                    listing = candidate;
                    break;
                }
            }

            return BuildListingInspectionSummary(listing);
        }

        private string BuildListingInspectionSummary(AcquisitionListing listing)
        {
            if (listing == null)
            {
                return string.Empty;
            }

            AcquisitionDealState deal = FindDeal(listing.listingId);
            string watchText = IsWatchedListing(listing.listingId) ? "Watched lead." : "Not watched.";
            string processText = deal == null
                ? "No active process yet."
                : $"Process: {BuildDealStageReadout(deal)} | Seriousness {FormatBuyerSeriousness(deal.seriousness)} | Stance {(deal.integrationStance == AcquisitionIntegrationStance.None ? "none" : FormatIntegrationStance(deal.integrationStance))}.";
            string fieldRead = deal != null && !string.IsNullOrWhiteSpace(deal.scoutingSummary)
                ? deal.scoutingSummary
                : BuildFieldReadSummary(listing);
            string quickLedger = deal != null && !string.IsNullOrWhiteSpace(deal.quickDiligenceLedger)
                ? $" Quick diligence ledger: {deal.quickDiligenceLedger.Replace("\n", " | ")}"
                : string.Empty;

            string nextStep = BuildSelectedNextStepSummary(listing, deal);
            string timing = BuildSelectedTimingPressureSummary(deal);
            string ownerRead = BuildSelectedOwnerReadSummary(listing, deal);
            string decision = BuildSelectedLeadDecisionSummary(listing, deal);
            string checklist = BuildSelectedLeadActionChecklist(listing, deal);
            string access = BuildSelectedLeadAccessSummary(listing, deal);
            string climate = BuildAcquisitionDecisionClimateSummary();
            return $"Acquisition Lead - {GetListingDisplayName(listing)} | Ask {FormatMoney(listing.askingPriceCents)} | {watchText} {processText} {ownerRead} {decision} {checklist} {access} Next: {nextStep}. {timing} {climate} {fieldRead}{quickLedger}";
        }

        private string BuildFieldReadSummary(AcquisitionListing listing)
        {
            if (listing == null)
            {
                return string.Empty;
            }

            string kind = listing.kind == AcquisitionListingKind.Land ? "land" : "business";
            string site = listing.plotId >= 0 ? $"Plot {listing.plotId:000}" : "site unassigned";
            string building = listing.buildingId >= 0 ? $"Building {listing.buildingId:000}" : "no building record";
            string business = !string.IsNullOrWhiteSpace(listing.businessDisplayName)
                ? listing.businessDisplayName
                : listing.businessTypeDisplayName;
            string asset = listing.kind == AcquisitionListingKind.Business && !string.IsNullOrWhiteSpace(business)
                ? business
                : GetListingDisplayName(listing);
            return $"Field read: {kind} lead | {asset} | {site} | {building}.";
        }

        private AcquisitionListing FindListingById(string listingId)
        {
            if (string.IsNullOrWhiteSpace(listingId))
            {
                return null;
            }

            for (int i = 0; i < landListings.Count; i++)
            {
                AcquisitionListing listing = landListings[i];
                if (listing != null && string.Equals(listing.listingId, listingId, StringComparison.OrdinalIgnoreCase))
                {
                    return listing;
                }
            }

            for (int i = 0; i < businessListings.Count; i++)
            {
                AcquisitionListing listing = businessListings[i];
                if (listing != null && string.Equals(listing.listingId, listingId, StringComparison.OrdinalIgnoreCase))
                {
                    return listing;
                }
            }

            return null;
        }

        private string BuildSelectedOwnerReadSummary(AcquisitionListing listing, AcquisitionDealState deal)
        {
            int askingPriceCents = deal != null && deal.tentativePriceCents > 0
                ? deal.tentativePriceCents
                : Mathf.Max(0, listing != null ? listing.askingPriceCents : 0);
            int earnestCents = deal != null ? Mathf.Max(0, deal.earnestMoneyCents) : 0;
            int financingNeedCents = deal != null
                ? Mathf.Max(0, deal.financingNeedCents)
                : Mathf.Max(0, askingPriceCents - GetAvailableCashCents());

            if (playerPortfolio != null)
            {
                return playerPortfolio.BuildAcquisitionOwnerReadSummary(askingPriceCents, earnestCents, financingNeedCents);
            }

            return financingNeedCents > 0
                ? $"Owner Read - likely financing-led. Gap {FormatMoney(financingNeedCents)}."
                : $"Owner Read - liquid cash may be enough for a clean move. Ask {FormatMoney(askingPriceCents)}.";
        }

        private string BuildSelectedStagedCommitmentSummary(AcquisitionListing listing, AcquisitionDealState deal)
        {
            int askingPriceCents = deal != null && deal.tentativePriceCents > 0
                ? deal.tentativePriceCents
                : Mathf.Max(0, listing != null ? listing.askingPriceCents : 0);
            int earnestCents = deal != null ? Mathf.Max(0, deal.earnestMoneyCents) : 0;
            int projectedWeeklyBurdenCents = deal != null && deal.financingNeedCents > 0
                ? Mathf.Max(0, Mathf.RoundToInt(deal.financingNeedCents * 0.05f))
                : 0;

            if (playerPortfolio != null)
            {
                return playerPortfolio.BuildStagedCommitmentSummary(askingPriceCents, earnestCents, projectedWeeklyBurdenCents);
            }

            int dueAfterEarnest = Mathf.Max(0, askingPriceCents - earnestCents);
            return $"Commitment Read - Earnest {FormatMoney(earnestCents)} | Due after earnest {FormatMoney(dueAfterEarnest)}.";
        }

        private string BuildSelectedNextStepSummary(AcquisitionListing listing, AcquisitionDealState deal)
        {
            if (listing == null)
            {
                return "select a lead";
            }

            if (deal == null || deal.stage == AcquisitionDealStage.None)
            {
                return IsWatchedListing(listing.listingId) ? "open inquiry when liquidity is ready" : "mark or open inquiry";
            }

            return deal.stage switch
            {
                AcquisitionDealStage.Inquiry => NeedsProofOfFunds(deal)
                    ? "present proof of funds before earnest or step back before the option window closes"
                    : "commit earnest or step back before the option window closes",
                AcquisitionDealStage.EarnestCommitted => "finish diligence and decide whether the site is truly worth terms",
                AcquisitionDealStage.DiligenceComplete => "draft terms around the diligence read and funding reality",
                AcquisitionDealStage.TentativeAgreement => deal.financingNeedCents > 0
                    ? "line up financing and close before the deadline"
                    : "close cleanly before the deadline",
                AcquisitionDealStage.Closed => "shift from deal work into post-close execution",
                AcquisitionDealStage.Failed => BuildFailedLeadNextStepSummary(deal),
                _ => GetActionLabelForDeal(deal).ToLowerInvariant()
            };
        }

        private string BuildSelectedTimingPressureSummary(AcquisitionDealState deal)
        {
            if (deal == null)
            {
                return "Timing: no live clock.";
            }

            if (deal.stage == AcquisitionDealStage.Failed)
            {
                string breakpoint = BuildFailureBreakpointLabel(deal.failedFromStage);
                return IsFailedDealReadyToReopen(deal)
                    ? $"Timing: failed during {breakpoint}; reviewed once and eligible for deliberate reopen if the lead still exists."
                    : $"Timing: failed during {breakpoint}; record a review before any reopen.";
            }

            int currentDay = GetCurrentDayIndex();
            if (deal.stage == AcquisitionDealStage.TentativeAgreement && deal.closingDeadlineDayIndex > 0)
            {
                int days = deal.closingDeadlineDayIndex - currentDay;
                if (days <= 0)
                {
                    return "Timing: closing window is expiring now.";
                }

                if (days <= 2)
                {
                    return $"Timing: close within about {days} day(s).";
                }

                return $"Timing: close window runs about {days} more day(s).";
            }

            if (deal.optionDeadlineDayIndex > 0)
            {
                int days = deal.optionDeadlineDayIndex - currentDay;
                if (days <= 0)
                {
                    return "Timing: option window is at its edge.";
                }

                if (days <= 2)
                {
                    return $"Timing: option pressure is high, about {days} day(s) remain.";
                }

                return $"Timing: option window has about {days} more day(s).";
            }

            return "Timing: no live deadline yet.";
        }

        private static bool FailMessage(string text, out string message)
        {
            message = text;
            return false;
        }

        [Obsolete("Use TryAdvanceSelectedAcquisition; retained for UI compatibility.")]
        private bool TryDirectPurchaseSelected(AcquisitionMarketSection section, out string message)
        {
            return section == AcquisitionMarketSection.Businesses
                ? TryPurchaseBusiness(out message)
                : TryPurchaseLand(out message);
        }

        public bool TryGetSelectedListing(AcquisitionMarketSection section, out AcquisitionListing listing)
        {
            EnsureMarket();
            if (section == AcquisitionMarketSection.Businesses)
            {
                if (businessListings.Count == 0)
                {
                    listing = null;
                    return false;
                }

                selectedBusinessIndex = WrapIndex(selectedBusinessIndex, businessListings.Count);
                listing = businessListings[selectedBusinessIndex];
                return listing != null;
            }

            if (landListings.Count == 0)
            {
                listing = null;
                return false;
            }

            selectedLandIndex = WrapIndex(selectedLandIndex, landListings.Count);
            listing = landListings[selectedLandIndex];
            return listing != null;
        }

        public void RecordOwnershipAptitudeSource(OwnershipAptitudeSource source)
        {
            RecordOwnershipAptitudeGain(source);
        }

        public bool TryGetLandAppreciationForPlot(int plotId, out LandAppreciationState appreciation)
        {
            EnsureMarket();
            RefreshOwnedLandAppreciations();
            appreciation = FindLandAppreciation(plotId);
            return appreciation != null;
        }

        public bool TryRecoverDefaultedLoanCollateral(
            LoanContract loan,
            int excludedBuildingId,
            out string recoveredLabel,
            out int recoveredValueCents,
            out string message)
        {
            recoveredLabel = string.Empty;
            recoveredValueCents = 0;
            if (loan == null)
            {
                message = "No defaulted loan available for collateral recovery.";
                return false;
            }

            if (TryRecoverPledgedCollateral(loan, out recoveredLabel, out recoveredValueCents, out message))
            {
                return true;
            }

            return TryRecoverLeastValuablePlayerHolding(excludedBuildingId, out recoveredLabel, out recoveredValueCents, out message);
        }

        public bool TryRecoverLeastValuablePlayerHolding(
            int excludedBuildingId,
            out string recoveredLabel,
            out int recoveredValueCents,
            out string message)
        {
            EnsureMarket();
            RefreshOwnedLandAppreciations();
            recoveredLabel = string.Empty;
            recoveredValueCents = 0;

            LandAppreciationState selected = null;
            int selectedValue = int.MaxValue;
            for (int i = 0; i < ownedLandAppreciations.Count; i++)
            {
                LandAppreciationState state = ownedLandAppreciations[i];
                if (state == null)
                {
                    continue;
                }

                TownPlot plot = GetPlot(state.plotId);
                int buildingId = state.buildingId >= 0 ? state.buildingId : plot != null ? plot.buildingId : -1;
                if (buildingId == excludedBuildingId)
                {
                    continue;
                }

                PlacedBuilding building = GetBuilding(buildingId);
                bool ownedPlot = plot != null && IsPlayerOwnedPlot(plot);
                bool ownedBuilding = building != null && IsPlayerOwnedBusiness(building);
                if (!ownedPlot && !ownedBuilding)
                {
                    continue;
                }

                int value = Mathf.Max(state.currentEstimatedValueCents, state.TotalBasisCents);
                if (selected == null || value < selectedValue)
                {
                    selected = state;
                    selectedValue = value;
                }
            }

            if (selected == null)
            {
                message = "No recoverable non-core player holding found.";
                return false;
            }

            TownPlot selectedPlot = GetPlot(selected.plotId);
            PlacedBuilding selectedBuilding = GetBuilding(selected.buildingId >= 0 ? selected.buildingId : selectedPlot != null ? selectedPlot.buildingId : -1);
            return RecoverPlayerHolding(
                selected,
                selectedPlot,
                selectedBuilding,
                "Fallback holding recovered after no pledged collateral was available.",
                out recoveredLabel,
                out recoveredValueCents,
                out message);
        }

        private bool TryRecoverPledgedCollateral(
            LoanContract loan,
            out string recoveredLabel,
            out int recoveredValueCents,
            out string message)
        {
            EnsureMarket();
            RefreshOwnedLandAppreciations();
            recoveredLabel = string.Empty;
            recoveredValueCents = 0;
            message = string.Empty;

            if (loan == null || loan.collateralIds == null || loan.collateralIds.Count <= 0)
            {
                message = "No pledged collateral was recorded for the defaulted loan.";
                return false;
            }

            for (int i = 0; i < loan.collateralIds.Count; i++)
            {
                string collateralId = loan.collateralIds[i];
                if (TryFindRecoverableCollateral(collateralId, out LandAppreciationState state, out TownPlot plot, out PlacedBuilding building))
                {
                    return RecoverPlayerHolding(
                        state,
                        plot,
                        building,
                        $"Pledged collateral {collateralId} recovered before fallback holdings.",
                        out recoveredLabel,
                        out recoveredValueCents,
                        out message);
                }
            }

            message = "Pledged collateral was not recoverable; fallback recovery required.";
            return false;
        }

        private bool TryFindRecoverableCollateral(
            string collateralId,
            out LandAppreciationState state,
            out TownPlot plot,
            out PlacedBuilding building)
        {
            state = null;
            plot = null;
            building = null;

            if (TryParseCollateralId(collateralId, "building_", out int buildingId)
                || TryParseCollateralId(collateralId, "business_", out buildingId))
            {
                building = GetBuilding(buildingId);
                plot = building != null ? GetPlot(building.plotId) : null;
                if (building != null && IsPlayerOwnedBusiness(building))
                {
                    state = plot != null ? FindLandAppreciation(plot.id) : null;
                    state ??= EnsureLandAppreciationState(plot, building, LandAppreciationImprovementState.OperatingBusiness);
                    return true;
                }
            }

            if (TryParseCollateralId(collateralId, "plot_", out int plotId)
                || TryParseCollateralId(collateralId, "land_", out plotId))
            {
                plot = GetPlot(plotId);
                building = plot != null ? GetBuilding(plot.buildingId) : null;
                if (plot != null && (IsPlayerOwnedPlot(plot) || IsPlayerOwnedBusiness(building)))
                {
                    state = FindLandAppreciation(plot.id);
                    LandAppreciationImprovementState fallbackImprovement = building != null
                        ? LandAppreciationImprovementState.ImprovedShell
                        : LandAppreciationImprovementState.Empty;
                    state ??= EnsureLandAppreciationState(plot, building, fallbackImprovement);
                    return true;
                }
            }

            return false;
        }

        private bool RecoverPlayerHolding(
            LandAppreciationState state,
            TownPlot plot,
            PlacedBuilding building,
            string authorityMessage,
            out string recoveredLabel,
            out int recoveredValueCents,
            out string message)
        {
            recoveredLabel = BuildRecoveredHoldingLabel(plot, building);
            recoveredValueCents = state != null
                ? Mathf.Max(state.currentEstimatedValueCents, state.TotalBasisCents)
                : EstimateFallbackBasisCents(plot, building);

            if (building != null)
            {
                building.playerOwned = false;
                ownedBusinessBuildingIds.Remove(building.id);
                if (sharedBusinessRuntime != null
                    && sharedBusinessRuntime.TryTransferBusinessToTown(building.id, "Bank Receiver", out _, out string transferMessage))
                {
                    message = $"{authorityMessage} {transferMessage}";
                }
                else
                {
                    message = $"{authorityMessage} {recoveredLabel} transferred to bank recovery control.";
                }
            }
            else
            {
                message = $"{authorityMessage} {recoveredLabel} transferred to bank recovery control.";
            }

            if (plot != null)
            {
                plot.playerOwned = false;
                ownedPlotIds.Remove(plot.id);
            }

            if (state != null)
            {
                ownedLandAppreciations.Remove(state);
            }

            marketStatus = $"Forced recovery completed: {recoveredLabel} at {FormatMoney(recoveredValueCents)}.";
            lastPurchaseSummary = marketStatus;
            marketBuilt = false;
            RebuildMarket();
            return true;
        }

        private static bool TryParseCollateralId(string collateralId, string prefix, out int id)
        {
            id = -1;
            if (string.IsNullOrWhiteSpace(collateralId) || string.IsNullOrWhiteSpace(prefix))
            {
                return false;
            }

            string trimmed = collateralId.Trim();
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return int.TryParse(trimmed.Substring(prefix.Length), out id);
        }

        public IReadOnlyList<OwnedPlotBuildabilityState> GetOwnedBuildablePlots()
        {
            EnsureMarket();
            RefreshOwnedLandAppreciations();
            RefreshOwnedBuildability();
            return ownedBuildablePlots;
        }

        public IReadOnlyList<BuildingDefinition> GetBuildOptionsForOwnedPlot(int plotId)
        {
            EnsureMarket();
            RefreshOwnedBuildability();
            for (int i = 0; i < ownedBuildablePlots.Count; i++)
            {
                if (ownedBuildablePlots[i].plotId == plotId)
                {
                    return ownedBuildablePlots[i].buildOptions;
                }
            }

            return Array.Empty<BuildingDefinition>();
        }

        public IReadOnlyList<BuildingDefinition> GetHouseBuildOptionsForOwnedPlot(int plotId)
        {
            EnsureMarket();
            RefreshOwnedBuildability();
            TownPlot plot = GetPlot(plotId);
            return plot != null ? BuildHouseShellOptions(plot) : Array.Empty<BuildingDefinition>();
        }

        public int GetBusinessDevelopmentCandidateCount()
        {
            EnsureMarket();
            return GetVisibleActivationBusinessTypeCount();
        }

        public int GetFirstEligibleBusinessDevelopmentCandidateIndex()
        {
            EnsureMarket();
            int count = GetVisibleActivationBusinessTypeCount();
            for (int i = 0; i < count; i++)
            {
                if (TryGetBusinessDevelopmentCandidate(i, out BusinessActivationCandidateState candidate)
                    && candidate != null
                    && candidate.eligible)
                {
                    return i;
                }
            }

            return count > 0 ? 0 : -1;
        }

        public bool TryGetBusinessDevelopmentCandidate(int optionIndex, out BusinessActivationCandidateState candidate)
        {
            EnsureMarket();
            candidate = null;
            if (!TryGetVisibleActivationBusinessType(optionIndex, out BusinessType businessType))
            {
                return false;
            }

            candidate = new BusinessActivationCandidateState
            {
                businessType = businessType,
                displayName = BusinessRuntimeNaming.GetBusinessTypeDisplayName(businessType),
                startupCostCents = GetStartupCostCents(businessType),
                eligible = true,
                reason = "Select a shell plan, then build this owned-land business shell."
            };

            BusinessProfileDefinition profile = ResolveActivationProfile(businessType);
            if (profile == null)
            {
                candidate.eligible = false;
                candidate.reason = $"No business profile found for {candidate.displayName}.";
                return true;
            }

            if (businessType == BusinessType.GeneralStore)
            {
                if (playerCashSource == null)
                {
                    candidate.eligible = false;
                    candidate.reason = "General Store unavailable.";
                }
                else if (playerCashSource.CurrentBusiness != null)
                {
                    candidate.eligible = false;
                    candidate.reason = $"The General Store is already open at building {playerCashSource.StoreBuildingId:000}. Additional locations are not available yet.";
                }
            }
            else if (sharedBusinessRuntime == null)
            {
                candidate.eligible = false;
                candidate.reason = "Business system unavailable.";
            }

            return true;
        }

        public IReadOnlyList<BuildingDefinition> GetBusinessBuildOptionsForOwnedPlot(int plotId, int businessOptionIndex)
        {
            EnsureMarket();
            RefreshOwnedBuildability();
            if (!TryResolveBusinessDevelopmentType(businessOptionIndex, out BusinessType businessType))
            {
                return Array.Empty<BuildingDefinition>();
            }

            TownPlot plot = GetPlot(plotId);
            return plot != null ? BuildBusinessShellOptions(plot, businessType) : Array.Empty<BuildingDefinition>();
        }

        public int GetConstructionCostForOwnedPlotOption(int plotId, int optionIndex)
        {
            return TryGetConstructionQuoteForOwnedPlotOption(plotId, optionIndex, out ConstructionInputQuote quote, out _)
                ? quote.CashCostCents
                : 0;
        }

        public bool TryGetConstructionFitForOwnedPlotOption(
            int plotId,
            int optionIndex,
            out PlayerDevelopmentFitResult fit)
        {
            fit = null;
            EnsureMarket();
            RefreshOwnedBuildability();
            OwnedPlotBuildabilityState state = FindOwnedBuildabilityState(plotId);
            if (state == null || state.buildOptions == null || state.buildOptions.Count == 0)
            {
                fit = PlayerDevelopmentFitResult.Blocked($"Plot {plotId:000} has no selected building plan.");
                return false;
            }

            BuildingDefinition definition = state.buildOptions[Mathf.Clamp(optionIndex, 0, state.buildOptions.Count - 1)];
            TownPlot plot = GetPlot(plotId);
            fit = EvaluatePlayerOwnedShellConstructionFit(definition, plot);
            return fit != null && fit.canProceed;
        }

        public bool TryGetHouseConstructionFitForOwnedPlotOption(
            int plotId,
            int optionIndex,
            out PlayerDevelopmentFitResult fit)
        {
            fit = null;
            if (!TryResolveHouseBuildOption(plotId, optionIndex, out BuildingDefinition definition, out TownPlot plot, out _))
            {
                fit = PlayerDevelopmentFitResult.Blocked($"Plot {plotId:000} has no selected house plan.");
                return false;
            }

            fit = EvaluatePlayerOwnedShellConstructionFit(definition, plot);
            return fit != null && fit.canProceed;
        }

        public bool TryGetBusinessConstructionFitForOwnedPlotOption(
            int plotId,
            int businessOptionIndex,
            int shellOptionIndex,
            out PlayerDevelopmentFitResult fit)
        {
            fit = null;
            if (!TryResolveBusinessBuildOption(plotId, businessOptionIndex, shellOptionIndex, out BusinessType businessType, out BuildingDefinition definition, out TownPlot plot, out string message))
            {
                fit = PlayerDevelopmentFitResult.Blocked(message);
                return false;
            }

            fit = EvaluatePlayerOwnedBusinessShellConstructionFit(definition, plot, businessType);
            return fit != null && fit.canProceed;
        }

        public bool TryGetConstructionQuoteForOwnedPlotOption(
            int plotId,
            int optionIndex,
            out ConstructionInputQuote quote,
            out string message)
        {
            quote = null;
            EnsureMarket();
            RefreshOwnedBuildability();
            OwnedPlotBuildabilityState state = FindOwnedBuildabilityState(plotId);
            if (state == null)
            {
                message = $"Plot {plotId:000} is not tracked as a player-owned plot.";
                return false;
            }

            if (!state.playerOwned)
            {
                message = $"Plot {plotId:000} is not player-owned.";
                return false;
            }

            if (!state.empty)
            {
                message = $"Plot {plotId:000} already has an improved site.";
                return false;
            }

            if (!state.buildable || state.buildOptions == null || state.buildOptions.Count == 0)
            {
                message = string.IsNullOrWhiteSpace(state.reason)
                    ? $"Plot {plotId:000} has no fitting building shell options."
                    : state.reason;
                return false;
            }

            BuildingDefinition definition = state.buildOptions[Mathf.Clamp(optionIndex, 0, state.buildOptions.Count - 1)];
            TownPlot plot = GetPlot(plotId);
            if (definition == null || plot == null)
            {
                message = definition == null
                    ? "No building shell definition is selected."
                    : $"Plot {plotId:000} is missing from the generated town.";
                return false;
            }

            quote = BuildShellConstructionQuote(definition, plot);
            message = quote.CanProceed ? "Construction inputs available." : BuildQuoteBlockedMessage(quote);
            return true;
        }

        public bool TryGetHouseConstructionQuoteForOwnedPlotOption(
            int plotId,
            int optionIndex,
            out ConstructionInputQuote quote,
            out string message)
        {
            quote = null;
            if (!TryResolveHouseBuildOption(plotId, optionIndex, out BuildingDefinition definition, out TownPlot plot, out message))
            {
                return false;
            }

            quote = BuildShellConstructionQuote(definition, plot);
            message = quote.CanProceed ? "House construction inputs available." : BuildQuoteBlockedMessage(quote);
            return true;
        }

        public bool TryGetBusinessConstructionQuoteForOwnedPlotOption(
            int plotId,
            int businessOptionIndex,
            int shellOptionIndex,
            out ConstructionInputQuote quote,
            out string message)
        {
            quote = null;
            if (!TryGetBusinessDevelopmentCandidate(businessOptionIndex, out BusinessActivationCandidateState candidate) || candidate == null)
            {
                message = "No business type is selected.";
                return false;
            }

            if (!candidate.eligible)
            {
                message = candidate.reason;
                return false;
            }

            if (!TryResolveBusinessBuildOption(plotId, businessOptionIndex, shellOptionIndex, out BusinessType businessType, out BuildingDefinition definition, out TownPlot plot, out message))
            {
                return false;
            }

            quote = BuildShellConstructionQuote(definition, plot, businessType);
            message = quote.CanProceed ? "Business shell construction inputs available." : BuildQuoteBlockedMessage(quote);
            return true;
        }

        public bool TryGetBusinessActivationQuote(
            int buildingId,
            int optionIndex,
            out ConstructionInputQuote quote,
            out string message)
        {
            quote = null;
            EnsureMarket();
            if (!TryGetBusinessActivationCandidate(buildingId, optionIndex, out BusinessActivationCandidateState candidate) || candidate == null)
            {
                message = "No business activation candidate is selected.";
                return false;
            }

            PlacedBuilding building = GetBuilding(buildingId);
            if (building == null)
            {
                message = $"Building {buildingId:000} is no longer available.";
                return false;
            }

            quote = BuildBusinessFitOutQuote(candidate, building);
            if (!candidate.eligible)
            {
                quote.blockReason = string.IsNullOrWhiteSpace(candidate.reason)
                    ? $"{candidate.displayName} cannot start at this site."
                    : candidate.reason;
            }

            message = quote.CanProceed ? "Business fit-out inputs available." : BuildQuoteBlockedMessage(quote);
            return true;
        }

        public int GetHouseholdUpgradeOptionCount()
        {
            return HouseholdUpgradeCatalog.Count;
        }

        public bool TryGetHouseholdUpgradeDefinition(int optionIndex, out HouseholdUpgradeDefinition definition)
        {
            definition = HouseholdUpgradeCatalog.GetByIndex(optionIndex);
            return definition != null;
        }

        public bool TryGetHouseholdUpgradeQuote(
            int homeBuildingId,
            int optionIndex,
            out ConstructionInputQuote quote,
            out string message)
        {
            quote = null;
            EnsureMarket();
            if (!TryGetHouseholdUpgradeDefinition(optionIndex, out HouseholdUpgradeDefinition definition) || definition == null)
            {
                message = "No household upgrade is selected.";
                return false;
            }

            if (!TryResolveHouseholdUpgradeTarget(homeBuildingId, definition.Kind, out _, out _, out message))
            {
                return false;
            }

            quote = BuildHouseholdUpgradeQuote(definition, homeBuildingId);
            message = quote.CanProceed ? "Household upgrade inputs available." : BuildQuoteBlockedMessage(quote);
            return true;
        }

        public bool TryBuildHouseholdUpgrade(
            int homeBuildingId,
            int optionIndex,
            out HouseholdUpgradeState builtUpgrade,
            out string message)
        {
            builtUpgrade = null;
            EnsureMarket();
            if (!TryGetHouseholdUpgradeDefinition(optionIndex, out HouseholdUpgradeDefinition definition) || definition == null)
            {
                message = "No household upgrade is selected.";
                return false;
            }

            if (!TryResolveHouseholdUpgradeTarget(homeBuildingId, definition.Kind, out HouseholdState household, out PlacedBuilding building, out message))
            {
                return false;
            }

            ConstructionInputQuote quote = BuildHouseholdUpgradeQuote(definition, homeBuildingId);
            if (!quote.CanProceed)
            {
                message = BuildQuoteBlockedMessage(quote);
                return false;
            }

            int cost = quote.CashCostCents;
            if (!TryValidateConstructionInputsAvailable(quote, out message))
            {
                return false;
            }

            if (!TrySpend(cost, $"{definition.DisplayName} household upgrade", out message))
            {
                return false;
            }

            ConstructionInputRollbackState rollback = new(this);
            if (!TryConsumeConstructionInputs(quote, rollback, out message))
            {
                rollback.Rollback();
                RefundSpend(cost, $"{definition.DisplayName} household upgrade");
                return false;
            }

#if UNITY_EDITOR
            if (ForceLateConstructionFailureForTests)
            {
                rollback.Rollback();
                RefundSpend(cost, $"{definition.DisplayName} household upgrade");
                message = $"Forced late construction failure for {definition.DisplayName} household upgrade.";
                return false;
            }
#endif

            int builtDay = timeManager != null ? timeManager.CurrentDate.AbsoluteDayIndex : 0;
            if (!HouseholdUpgradeCatalog.TryAddBuiltUpgrade(household, definition.Kind, builtDay))
            {
                rollback.Rollback();
                RefundSpend(cost, $"{definition.DisplayName} household upgrade");
                message = $"{definition.DisplayName} is already built for {household.householdName}.";
                return false;
            }

            builtUpgrade = household.upgrades[household.upgrades.Count - 1];
            lastConstructionSupportSummary = BuildSupportConsumptionSummary(quote);
            lastPurchaseSummary = $"{definition.DisplayName} built for {household.householdName} at Building {building.id:000} for {FormatMoney(cost)}. Inputs: {lastConstructionSupportSummary}.";
            marketStatus = lastPurchaseSummary;
            message = lastPurchaseSummary;
            Debug.Log($"[Acquisitions] {lastPurchaseSummary}", this);
            return true;
        }

        public bool TryConstructOnOwnedPlot(int plotId, int optionIndex, out PlacedBuilding building, out string message)
        {
            building = null;
            EnsureMarket();
            RefreshOwnedBuildability();

            OwnedPlotBuildabilityState state = FindOwnedBuildabilityState(plotId);
            if (state == null)
            {
                message = $"Plot {plotId:000} is not tracked as a player-owned plot.";
                return false;
            }

            if (!state.playerOwned)
            {
                message = $"Plot {plotId:000} is not player-owned.";
                return false;
            }

            if (!state.empty)
            {
                message = $"Plot {plotId:000} already has an improved site.";
                return false;
            }

            if (!state.buildable || state.buildOptions == null || state.buildOptions.Count == 0)
            {
                message = string.IsNullOrWhiteSpace(state.reason)
                    ? $"Plot {plotId:000} has no fitting building shell options."
                    : state.reason;
                return false;
            }

            BuildingDefinition definition = state.buildOptions[Mathf.Clamp(optionIndex, 0, state.buildOptions.Count - 1)];
            if (definition == null)
            {
                message = "No building shell definition is selected.";
                return false;
            }

            TownPlot plot = GetPlot(plotId);
            if (plot == null)
            {
                message = $"Plot {plotId:000} is missing from the generated town.";
                return false;
            }

            ConstructionInputQuote quote = BuildShellConstructionQuote(definition, plot);
            if (!quote.CanProceed)
            {
                message = BuildQuoteBlockedMessage(quote);
                return false;
            }

            int cost = quote.CashCostCents;
            if (townWorld == null)
            {
                message = "Town world is not available for construction.";
                return false;
            }

            if (!TryValidateConstructionInputsAvailable(quote, out message))
            {
                return false;
            }

            if (!TrySpend(cost, $"{definition.DisplayName} building shell", out message))
            {
                return false;
            }

            ConstructionInputRollbackState rollback = new(this);
            if (!TryConsumeConstructionInputs(quote, rollback, out message))
            {
                rollback.Rollback();
                RefundSpend(cost, $"{definition.DisplayName} building shell");
                return false;
            }

#if UNITY_EDITOR
            if (ForceLateConstructionFailureForTests)
            {
                rollback.Rollback();
                RefundSpend(cost, $"{definition.DisplayName} building shell");
                message = $"Forced late construction failure for {definition.DisplayName} building shell.";
                return false;
            }
#endif

            if (!townWorld.TryConstructBuildingShell(plotId, definition, true, out building, out string buildMessage))
            {
                rollback.Rollback();
                RefundSpend(cost, $"{definition.DisplayName} building shell");
                message = buildMessage;
                return false;
            }

            if (!ownedPlotIds.Contains(plot.id))
            {
                ownedPlotIds.Add(plot.id);
            }

            CapitalizeLandImprovement(plot, building, cost, LandAppreciationImprovementState.ImprovedShell);
            RemoveLandListingForPlot(plot.id);
            RefreshOwnedBuildability();
            marketStatus = $"Construction complete. {definition.DisplayName} shell on Plot {plot.id:000}.";
            lastConstructionSupportSummary = BuildSupportConsumptionSummary(quote);
            PlayerDevelopmentFitResult fit = EvaluatePlayerOwnedShellConstructionFit(definition, plot);
            string fitNote = fit != null && fit.HasWarnings ? $" Fit warnings: {fit.WarningSummary}." : string.Empty;
            lastPurchaseSummary = $"{definition.DisplayName} building shell completed on Plot {plot.id:000} for {FormatMoney(cost)}. Inputs: {lastConstructionSupportSummary}. It is an improved site only; operator assignment and business activation are pending.{fitNote}";
            RecordOwnershipAptitudeGain(OwnershipAptitudeSource.ParcelDeveloped);
            message = lastPurchaseSummary;
            Debug.Log($"[Acquisitions] {lastPurchaseSummary}", this);
            return true;
        }

        public bool TryConstructHouseOnOwnedPlot(int plotId, int optionIndex, out PlacedBuilding building, out string message)
        {
            building = null;
            if (!TryResolveHouseBuildOption(plotId, optionIndex, out BuildingDefinition definition, out TownPlot _, out message))
            {
                return false;
            }

            return TryConstructResolvedShellOnOwnedPlot(plotId, definition, null, out building, out message);
        }

        public bool TryConstructBusinessShellOnOwnedPlot(
            int plotId,
            int businessOptionIndex,
            int shellOptionIndex,
            out PlacedBuilding building,
            out string message)
        {
            building = null;
            if (!TryGetBusinessDevelopmentCandidate(businessOptionIndex, out BusinessActivationCandidateState candidate) || candidate == null)
            {
                message = "No business type is selected.";
                return false;
            }

            if (!candidate.eligible)
            {
                message = candidate.reason;
                return false;
            }

            if (!TryResolveBusinessBuildOption(plotId, businessOptionIndex, shellOptionIndex, out BusinessType businessType, out BuildingDefinition definition, out TownPlot _, out message))
            {
                return false;
            }

            return TryConstructResolvedShellOnOwnedPlot(plotId, definition, businessType, out building, out message);
        }

        private bool TryConstructResolvedShellOnOwnedPlot(
            int plotId,
            BuildingDefinition definition,
            BusinessType? businessIntent,
            out PlacedBuilding building,
            out string message)
        {
            building = null;
            EnsureMarket();
            RefreshOwnedBuildability();

            OwnedPlotBuildabilityState state = FindOwnedBuildabilityState(plotId);
            if (state == null)
            {
                message = $"Plot {plotId:000} is not tracked as a player-owned plot.";
                return false;
            }

            if (!state.playerOwned)
            {
                message = $"Plot {plotId:000} is not player-owned.";
                return false;
            }

            if (!state.empty)
            {
                message = $"Plot {plotId:000} already has an improved site.";
                return false;
            }

            if (definition == null)
            {
                message = "No building shell definition is selected.";
                return false;
            }

            TownPlot plot = GetPlot(plotId);
            if (plot == null)
            {
                message = $"Plot {plotId:000} is missing from the generated town.";
                return false;
            }

            TownGenerationSettings settings = townWorld != null ? townWorld.Settings : null;
            if (!CanFitBuildingOnPlot(plot, definition, settings))
            {
                message = $"{definition.DisplayName} does not physically fit Plot {plotId:000}.";
                return false;
            }

            ConstructionInputQuote quote = BuildShellConstructionQuote(definition, plot, businessIntent);
            if (!quote.CanProceed)
            {
                message = BuildQuoteBlockedMessage(quote);
                return false;
            }

            int cost = quote.CashCostCents;
            if (townWorld == null)
            {
                message = "Town world is not available for construction.";
                return false;
            }

            if (!TryValidateConstructionInputsAvailable(quote, out message))
            {
                return false;
            }

            if (!TrySpend(cost, $"{definition.DisplayName} building shell", out message))
            {
                return false;
            }

            ConstructionInputRollbackState rollback = new(this);
            if (!TryConsumeConstructionInputs(quote, rollback, out message))
            {
                rollback.Rollback();
                RefundSpend(cost, $"{definition.DisplayName} building shell");
                return false;
            }

#if UNITY_EDITOR
            if (ForceLateConstructionFailureForTests)
            {
                rollback.Rollback();
                RefundSpend(cost, $"{definition.DisplayName} building shell");
                message = $"Forced late construction failure for {definition.DisplayName} building shell.";
                return false;
            }
#endif

            if (!townWorld.TryConstructBuildingShell(plotId, definition, true, out building, out string buildMessage))
            {
                rollback.Rollback();
                RefundSpend(cost, $"{definition.DisplayName} building shell");
                message = buildMessage;
                return false;
            }

            if (!ownedPlotIds.Contains(plot.id))
            {
                ownedPlotIds.Add(plot.id);
            }

            CapitalizeLandImprovement(plot, building, cost, LandAppreciationImprovementState.ImprovedShell);
            RemoveLandListingForPlot(plot.id);
            RefreshOwnedBuildability();
            marketStatus = $"Construction complete. {definition.DisplayName} shell on Plot {plot.id:000}.";
            lastConstructionSupportSummary = BuildSupportConsumptionSummary(quote);
            PlayerDevelopmentFitResult fit = businessIntent.HasValue
                ? EvaluatePlayerOwnedBusinessShellConstructionFit(definition, plot, businessIntent.Value)
                : EvaluatePlayerOwnedShellConstructionFit(definition, plot);
            string fitNote = fit != null && fit.HasWarnings ? $" Fit warnings: {fit.WarningSummary}." : string.Empty;
            string nextStep = businessIntent.HasValue
                ? $" Business intent: {BusinessRuntimeNaming.GetBusinessTypeDisplayName(businessIntent.Value)}; start the business from this owned shell when fit-out inputs are ready."
                : " House mode selected; household upgrades remain available, and business startup can still be selected on owned land.";
            lastPurchaseSummary = $"{definition.DisplayName} building shell completed on Plot {plot.id:000} for {FormatMoney(cost)}. Inputs: {lastConstructionSupportSummary}.{nextStep}{fitNote}";
            RecordOwnershipAptitudeGain(OwnershipAptitudeSource.ParcelDeveloped);
            message = lastPurchaseSummary;
            Debug.Log($"[Acquisitions] {lastPurchaseSummary}", this);
            return true;
        }

        public int GetBusinessActivationCandidateCount(int buildingId)
        {
            EnsureMarket();
            return GetVisibleActivationBusinessTypeCount();
        }

        public int GetFirstEligibleBusinessActivationCandidateIndex(int buildingId)
        {
            EnsureMarket();
            int count = GetVisibleActivationBusinessTypeCount();
            for (int i = 0; i < count; i++)
            {
                if (TryGetVisibleActivationBusinessType(i, out BusinessType businessType))
                {
                    BusinessActivationCandidateState candidate = CreateActivationCandidate(buildingId, businessType);
                    if (candidate != null && candidate.eligible)
                    {
                        return i;
                    }
                }
            }

            return count > 0 ? 0 : -1;
        }

        public bool TryGetBusinessActivationCandidate(int buildingId, int optionIndex, out BusinessActivationCandidateState candidate)
        {
            EnsureMarket();
            candidate = null;
            if (!TryGetVisibleActivationBusinessType(optionIndex, out BusinessType businessType))
            {
                return false;
            }

            candidate = CreateActivationCandidate(buildingId, businessType);
            return candidate != null;
        }

        public bool TryStartBusinessFromOwnedShell(int buildingId, int optionIndex, out BusinessInstanceState business, out string message)
        {
            EnsureMarket();
            business = null;

            if (!TryGetBusinessActivationCandidate(buildingId, optionIndex, out BusinessActivationCandidateState candidate) || candidate == null)
            {
                message = "No business activation candidate is selected.";
                return false;
            }

            if (!candidate.eligible)
            {
                message = string.IsNullOrWhiteSpace(candidate.reason)
                    ? $"{candidate.displayName} cannot start at this site."
                    : candidate.reason;
                return false;
            }

            PlacedBuilding building = GetBuilding(buildingId);
            if (building == null)
            {
                message = $"Building {buildingId:000} is no longer available.";
                return false;
            }

            ConstructionInputQuote quote = BuildBusinessFitOutQuote(candidate, building);
            if (!quote.CanProceed)
            {
                message = BuildQuoteBlockedMessage(quote);
                return false;
            }

            string startupLabel = $"{candidate.displayName} startup";
            if (!TryValidateConstructionInputsAvailable(quote, out message))
            {
                return false;
            }

            if (!TrySpend(quote.CashCostCents, startupLabel, out message))
            {
                return false;
            }

            ConstructionInputRollbackState rollback = new(this);
            if (!TryConsumeConstructionInputs(quote, rollback, out message))
            {
                rollback.Rollback();
                RefundSpend(quote.CashCostCents, startupLabel);
                return false;
            }

#if UNITY_EDITOR
            if (ForceLateBusinessStartFailureForTests)
            {
                rollback.Rollback();
                RefundSpend(quote.CashCostCents, startupLabel);
                message = $"Forced late business start failure for {candidate.displayName}.";
                return false;
            }
#endif

            bool started;
            message = string.Empty;
            if (candidate.businessType == BusinessType.GeneralStore)
            {
                started = playerCashSource != null && playerCashSource.TryOpenAtPlayerOwnedShell(buildingId, out business, out message);
            }
            else
            {
                started = sharedBusinessRuntime != null && sharedBusinessRuntime.TryStartPlayerBusinessAtShell(candidate.businessType, buildingId, out business, out message);
            }

            if (!started)
            {
                rollback.Rollback();
                RefundSpend(quote.CashCostCents, startupLabel);
                message = !string.IsNullOrWhiteSpace(message) ? message : $"Could not start {candidate.displayName} at this shell.";
                return false;
            }

            ConfigurePlayerBusinessStabilizationDefaults(business);
            playerPortfolio?.RegisterCurrentBusinessCashCheckpoint(business, GetCurrentWeekKey(), true);

            building.playerOwned = true;
            if (candidate.businessType != BusinessType.GeneralStore && !ownedBusinessBuildingIds.Contains(building.id))
            {
                ownedBusinessBuildingIds.Add(building.id);
            }

            TownPlot plot = GetPlot(building.plotId);
            if (plot != null)
            {
                plot.playerOwned = true;
                if (!ownedPlotIds.Contains(plot.id))
                {
                    ownedPlotIds.Add(plot.id);
                }
            }

            UpdateLandAppreciationImprovement(plot, building, LandAppreciationImprovementState.OperatingBusiness);
            RefreshOwnedBuildability();
            string businessLabel = business != null ? business.RuntimeDisplayName : $"Player {candidate.displayName}";
            string buildingLabel = building.definition != null ? building.definition.DisplayName : $"Building {building.id:000}";
            lastConstructionSupportSummary = BuildSupportConsumptionSummary(quote);
            string fitNote = string.IsNullOrWhiteSpace(candidate.fitWarningSummary)
                ? string.Empty
                : $" Fit warnings: {candidate.fitWarningSummary}.";
            lastPurchaseSummary = $"{businessLabel} opened at {buildingLabel}, Plot {building.plotId:000}, for {FormatMoney(quote.CashCostCents)} startup cost. Inputs: {lastConstructionSupportSummary}.{fitNote}";
            marketStatus = lastPurchaseSummary;
            RecordOwnershipAptitudeGain(OwnershipAptitudeSource.BusinessOpened);
            message = lastPurchaseSummary;
            Debug.Log($"[Acquisitions] {lastPurchaseSummary}", this);
            return true;
        }

        private BusinessActivationCandidateState CreateActivationCandidate(int buildingId, BusinessType businessType)
        {
            int baseStartupCostCents = GetStartupCostCents(businessType);
            BusinessActivationCandidateState candidate = new()
            {
                businessType = businessType,
                displayName = BusinessRuntimeNaming.GetBusinessTypeDisplayName(businessType),
                startupCostCents = baseStartupCostCents
            };

            candidate.eligible = IsBusinessActivationEligible(buildingId, businessType, out string reason, out PlayerDevelopmentFitResult fit);
            candidate.reason = reason;
            if (fit != null)
            {
                candidate.fitScore01 = Mathf.Clamp01(fit.fitScore01);
                candidate.costMultiplier = Mathf.Max(1f, fit.costMultiplier);
                candidate.fitWarningSummary = fit.WarningSummary;
                candidate.startupCostCents = ApplyCostMultiplier(baseStartupCostCents, candidate.costMultiplier);
            }

            return candidate;
        }

        private bool IsBusinessActivationEligible(int buildingId, BusinessType businessType, out string reason, out PlayerDevelopmentFitResult fit)
        {
            fit = null;
            AutoWire();
            PlacedBuilding building = GetBuilding(buildingId);
            if (building == null)
            {
                reason = $"Building {buildingId:000} is not part of the generated town.";
                fit = PlayerDevelopmentFitResult.Blocked(reason);
                return false;
            }

            if (!IsPlayerOwnedBusiness(building))
            {
                reason = $"Building {buildingId:000} is not player-owned.";
                fit = PlayerDevelopmentFitResult.Blocked(reason);
                return false;
            }

            if (building.definition == null)
            {
                reason = $"Building {buildingId:000} is missing its shell definition.";
                fit = PlayerDevelopmentFitResult.Blocked(reason);
                return false;
            }

            if (sharedBusinessRuntime != null && sharedBusinessRuntime.FindByBuildingId(building.id) != null)
            {
                reason = $"Building {building.id:000} already has an active business.";
                fit = PlayerDevelopmentFitResult.Blocked(reason);
                return false;
            }

            if (playerCashSource != null && playerCashSource.CurrentBusiness != null && playerCashSource.StoreBuildingId == building.id)
            {
                reason = "This shell is already the General Store.";
                fit = PlayerDevelopmentFitResult.Blocked(reason);
                return false;
            }

            if (businessType == BusinessType.GeneralStore)
            {
                if (playerCashSource == null)
                {
                    reason = "General Store unavailable.";
                    fit = PlayerDevelopmentFitResult.Blocked(reason);
                    return false;
                }

                if (playerCashSource.CurrentBusiness != null && playerCashSource.StoreBuildingId != building.id)
                {
                    reason = $"The General Store is already open at building {playerCashSource.StoreBuildingId:000}. Additional locations are not available yet.";
                    fit = PlayerDevelopmentFitResult.Blocked(reason);
                    return false;
                }
            }
            else if (sharedBusinessRuntime == null)
            {
                reason = "Business system unavailable.";
                fit = PlayerDevelopmentFitResult.Blocked(reason);
                return false;
            }

            TownPlot plot = GetPlot(building.plotId);
            if (plot == null)
            {
                reason = $"Plot {building.plotId:000} is missing from the generated town.";
                fit = PlayerDevelopmentFitResult.Blocked(reason);
                return false;
            }

            BusinessProfileDefinition profile = ResolveActivationProfile(businessType);
            if (profile == null)
            {
                reason = $"No business profile found for {BusinessRuntimeNaming.GetBusinessTypeDisplayName(businessType)}.";
                fit = PlayerDevelopmentFitResult.Blocked(reason);
                return false;
            }

            if (!BusinessSiteSuitabilityEvaluator.TryEvaluatePlayerOwnedDevelopmentFit(profile, building, plot, out fit))
            {
                reason = fit != null ? fit.reason : $"{BusinessRuntimeNaming.GetBusinessTypeDisplayName(businessType)} cannot start at this site.";
                return false;
            }

            reason = fit.reason;
            return true;
        }

        private BusinessProfileDefinition ResolveActivationProfile(BusinessType businessType)
        {
            BusinessProfileDefinition profile = FindActivationProfile(businessType);
            if (profile != null)
            {
                return profile;
            }

            return businessType == BusinessType.GeneralStore && playerCashSource != null
                ? playerCashSource.StoreDefinition
                : null;
        }

        private void ConfigurePlayerBusinessStabilizationDefaults(BusinessInstanceState business)
        {
            if (playerPortfolio == null || business == null || business.RuntimeState == null)
            {
                return;
            }

            int protectedReserve = business.BusinessType == BusinessType.GeneralStore && playerCashSource != null
                ? playerCashSource.EffectivePostReorderCashBufferForLocalSupplyCents
                : SharedBusinessRuntimeManager.CalculateSharedOperatingCashReserveCents(business);
            playerPortfolio.ConfigureDefaultBusinessCashTransfers(
                business,
                protectedReserve,
                business.RuntimeState.LastWeeklyReorderBudgetCents,
                GetCurrentWeekKey());
        }

        private static BusinessProfileDefinition FindActivationProfile(BusinessType businessType)
        {
            BusinessProfileDefinition[] profiles = Resources.LoadAll<BusinessProfileDefinition>(DefaultProfilesResourcePath);
            for (int i = 0; i < profiles.Length; i++)
            {
                BusinessProfileDefinition profile = profiles[i];
                if (profile != null && profile.Business.BusinessType == businessType)
                {
                    return profile;
                }
            }

            return null;
        }

        private static int GetStartupCostCents(BusinessType businessType)
        {
            return businessType switch
            {
                BusinessType.GeneralStore => 8500,
                BusinessType.Blacksmith => 11500,
                BusinessType.Butcher => 10000,
                BusinessType.Ranch => 13000,
                BusinessType.CropFarm => 9000,
                BusinessType.Doctor => 7500,
                BusinessType.Sawmill => 12000,
                BusinessType.LumberYard => 9500,
                BusinessType.BoardingHouse => 8000,
                BusinessType.LiveryFreight => 10500,
                BusinessType.Builder => 7000,
                BusinessType.FuelDealer => 6500,
                BusinessType.GrainMill => 11000,
                BusinessType.Bakery => 8500,
                BusinessType.Tailor => 5500,
                BusinessType.Saloon => 12000,
                BusinessType.Barber => 4500,
                BusinessType.Wheelwright => 9500,
                _ => 10000
            };
        }

        private ConstructionInputQuote BuildShellConstructionQuote(
            BuildingDefinition definition,
            TownPlot plot,
            BusinessType? businessIntent = null)
        {
            EnsureConstructionSupportNodesLoaded();
            Vector2Int footprint = definition.FootprintSizeCells;
            int footprintArea = Mathf.Max(1, footprint.x) * Mathf.Max(1, footprint.y);
            int lumberUnits = footprintArea * definition.StoreyCount * 2;
            int nailsUnits = Mathf.CeilToInt(lumberUnits * 0.2f);
            int laborUnits = footprintArea + (plot != null ? Mathf.Max(0, plot.frontageCells) : 0);
            int supportLumberUnitCost = GetSupportNodeUnitCost(ConstructionResourceKind.Lumber);
            int lumberUnitCost = supportLumberUnitCost;
            int nailsUnitCost = Mathf.Max(0, blacksmithHardwareUnitCostCents);
            int sitePrep = plot != null ? plot.frontageCells * roadFrontagePremiumPerCellCents : 0;
            float mixedUseMultiplier = definition.IsMixedUse ? mixedUseBusinessMultiplier : 1f;
            int baseCashCost = Mathf.Max(1, Mathf.RoundToInt(
                (lumberUnits * lumberUnitCost
                + nailsUnits * nailsUnitCost
                + laborUnits * Mathf.Max(0, laborUnitCostCents)
                + sitePrep)
                * mixedUseMultiplier));
            PlayerDevelopmentFitResult fit = businessIntent.HasValue
                ? EvaluatePlayerOwnedBusinessShellConstructionFit(definition, plot, businessIntent.Value)
                : EvaluatePlayerOwnedShellConstructionFit(definition, plot);
            int cashCost = ApplyCostMultiplier(baseCashCost, fit != null ? fit.costMultiplier : 1f);

            ConstructionInputQuote quote = CreateBaseQuote(
                ConstructionProjectKind.BuildingShell,
                $"{definition.DisplayName} building shell",
                cashCost,
                GetAvailableCashCents(),
                "Owner cash");
            AddConstructionResourceLines(
                quote,
                lumberUnits,
                nailsUnits,
                laborUnits,
                plot != null ? plot.id : -1);
            return quote;
        }

        private ConstructionInputQuote BuildBusinessFitOutQuote(BusinessActivationCandidateState candidate, PlacedBuilding building)
        {
            ConstructionInputQuote quote = CreateBaseQuote(
                ConstructionProjectKind.BusinessFitOut,
                $"{candidate.displayName} fit-out",
                Mathf.Max(0, candidate.startupCostCents),
                GetAvailableCashForBusinessActivation(candidate),
                "Owner cash");
            AddConstructionResourceLines(
                quote,
                4,
                2,
                6,
                building != null ? building.plotId : -1);
            return quote;
        }

        private ConstructionInputQuote BuildHouseholdUpgradeQuote(HouseholdUpgradeDefinition definition, int homeBuildingId)
        {
            EnsureConstructionSupportNodesLoaded();
            int lumberUnits = definition != null ? definition.LumberUnits : 0;
            int nailsUnits = definition != null ? definition.NailsUnits : 0;
            int laborUnits = definition != null ? definition.LaborUnits : 0;
            int cashCost = Mathf.Max(0,
                lumberUnits * GetSupportNodeUnitCost(ConstructionResourceKind.Lumber)
                + nailsUnits * Mathf.Max(0, blacksmithHardwareUnitCostCents)
                + laborUnits * Mathf.Max(0, laborUnitCostCents));

            ConstructionInputQuote quote = CreateBaseQuote(
                ConstructionProjectKind.HouseholdUpgrade,
                $"{(definition != null ? definition.DisplayName : "Household upgrade")}",
                cashCost,
                GetAvailableCashCents(),
                "Owner cash");
            int buyerPlotId = -1;
            PlacedBuilding home = GetBuilding(homeBuildingId);
            if (home != null)
            {
                buyerPlotId = home.plotId;
            }

            AddConstructionResourceLines(quote, lumberUnits, nailsUnits, laborUnits, buyerPlotId);
            return quote;
        }

        private PlayerDevelopmentFitResult EvaluatePlayerOwnedShellConstructionFit(BuildingDefinition definition, TownPlot plot)
        {
            if (definition == null)
            {
                return PlayerDevelopmentFitResult.Blocked("No building shell definition is selected.");
            }

            if (plot == null)
            {
                return PlayerDevelopmentFitResult.Blocked("Missing owned plot.");
            }

            List<string> warnings = new();
            float score = 1f;
            if (!definition.CanUsePlot(plot.zone))
            {
                AddFitWarning(
                    warnings,
                    $"{definition.DisplayName} was originally recommended for {definition.AllowedPlotZone}; Plot {plot.id:000} was seeded as {plot.zone}",
                    0.18f,
                    ref score);
            }

            if (plot.frontageCells < 4)
            {
                AddFitWarning(
                    warnings,
                    $"frontage {plot.frontageCells} is tight for construction staging",
                    0.08f,
                    ref score);
            }

            return PlayerDevelopmentFitResult.Allowed(score, warnings);
        }

        private PlayerDevelopmentFitResult EvaluatePlayerOwnedBusinessShellConstructionFit(
            BuildingDefinition definition,
            TownPlot plot,
            BusinessType businessType)
        {
            PlayerDevelopmentFitResult baseFit = EvaluatePlayerOwnedShellConstructionFit(definition, plot);
            if (baseFit == null || !baseFit.canProceed)
            {
                return baseFit;
            }

            List<string> warnings = baseFit.warnings != null
                ? new List<string>(baseFit.warnings)
                : new List<string>();
            float score = Mathf.Clamp01(baseFit.fitScore01);
            string businessName = BusinessRuntimeNaming.GetBusinessTypeDisplayName(businessType);
            string shellName = definition != null ? definition.DisplayName : "Selected shell";

            if (definition == null)
            {
                return PlayerDevelopmentFitResult.Blocked("No building shell definition is selected.");
            }

            if (!definition.CanHostWorkplace)
            {
                AddFitWarning(
                    warnings,
                    $"{shellName} was originally residential/non-workplace; {businessName} fit-out needs more adaptation",
                    0.22f,
                    ref score);
            }
            else if (!definition.IsSuitableForBusiness(businessType))
            {
                AddFitWarning(
                    warnings,
                    $"{shellName} was not recommended for {businessName}; workflow and fixtures need adaptation",
                    0.16f,
                    ref score);
            }

            BusinessProfileDefinition profile = ResolveActivationProfile(businessType);
            BusinessSiteRequirementDefinition requirements = profile != null ? profile.SiteRequirements : null;
            if (requirements != null && plot != null)
            {
                if (!PlotZoneMatchesBusinessRecommendation(requirements, plot.zone))
                {
                    AddFitWarning(
                        warnings,
                        $"Plot {plot.id:000} was seeded as {plot.zone}; {businessName} prefers {BuildBusinessAllowedZoneSummary(requirements)}",
                        0.12f,
                        ref score);
                }

                Vector2Int minimumSize = requirements.MinimumSiteSizeCells;
                if (plot.siteSizeCells.x < minimumSize.x || plot.siteSizeCells.y < minimumSize.y)
                {
                    AddFitWarning(
                        warnings,
                        $"site {plot.siteSizeCells.x}x{plot.siteSizeCells.y} is below the recommended {minimumSize.x}x{minimumSize.y}",
                        0.14f,
                        ref score);
                }

                if (requirements.RequiresRoadFrontage && plot.frontageCells <= 0)
                {
                    AddFitWarning(
                        warnings,
                        "no road frontage; customer and delivery access will be awkward",
                        0.18f,
                        ref score);
                }
                else if (plot.frontageCells < requirements.MinimumFrontageCells)
                {
                    AddFitWarning(
                        warnings,
                        $"frontage {plot.frontageCells} is below the recommended {requirements.MinimumFrontageCells}",
                        0.10f,
                        ref score);
                }

                int requiredBuildableArea = requirements.MinimumBuildableAreaCells;
                if (requiredBuildableArea > 0)
                {
                    int buildableArea = plot.candidateFootprint.IsValid ? plot.candidateFootprint.Area : plot.bounds.Area;
                    if (buildableArea < requiredBuildableArea)
                    {
                        AddFitWarning(
                            warnings,
                            $"buildable area {buildableArea} cells is below the recommended {requiredBuildableArea}",
                            0.12f,
                            ref score);
                    }
                }
            }

            return PlayerDevelopmentFitResult.Allowed(score, warnings);
        }

        private static void AddFitWarning(List<string> warnings, string warning, float penalty, ref float score)
        {
            if (!string.IsNullOrWhiteSpace(warning))
            {
                warnings.Add(warning);
            }

            score -= Mathf.Max(0f, penalty);
        }

        private static int ApplyCostMultiplier(int baseCostCents, float multiplier)
        {
            return Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(0, baseCostCents) * Mathf.Max(1f, multiplier)));
        }

        private ConstructionInputQuote CreateBaseQuote(
            ConstructionProjectKind projectKind,
            string projectLabel,
            int cashCostCents,
            int availableCashCents,
            string cashSourceLabel)
        {
            return new ConstructionInputQuote
            {
                projectKind = projectKind,
                projectLabel = projectLabel ?? string.Empty,
                cashCostCents = Mathf.Max(0, cashCostCents),
                availableCashCents = Mathf.Max(0, availableCashCents),
                cashSourceLabel = string.IsNullOrWhiteSpace(cashSourceLabel) ? "Owner cash" : cashSourceLabel
            };
        }

        private void AddConstructionResourceLines(
            ConstructionInputQuote quote,
            int lumberUnits,
            int nailsUnits,
            int laborUnits,
            int buyerPlotId)
        {
            if (quote == null)
            {
                return;
            }

            int lumberAvailable = GetAvailableSupportStock(ConstructionResourceKind.Lumber, out string lumberSource);
            int supportLumberUnitCost = GetSupportNodeUnitCost(ConstructionResourceKind.Lumber);
            int lumberUnitCost = supportLumberUnitCost;
            List<ConstructionResourceSourceAllocation> lumberAllocations = new();
            if (sharedBusinessRuntime != null
                && sharedBusinessRuntime.TryBuildLumberPurchaseAllocations(
                    buyerPlotId,
                    lumberUnits,
                    lumberUnitCost,
                    lumberAllocations,
                    out int sharedAvailable,
                    out int sharedUnitCost,
                    out string sharedSource))
            {
                lumberAvailable = sharedAvailable;
                lumberUnitCost = sharedUnitCost;
                lumberSource = string.IsNullOrWhiteSpace(sharedSource) ? "Shared lumber sellers" : sharedSource;
            }

            quote.cashCostCents = Mathf.Max(0, quote.cashCostCents + (lumberUnitCost - supportLumberUnitCost) * Mathf.Max(0, lumberUnits));

            quote.resources.Add(ConstructionResourceQuoteLine.Create(
                ConstructionResourceKind.Lumber,
                "Lumber",
                lumberUnits,
                lumberAvailable,
                lumberUnitCost,
                lumberSource,
                lumberAllocations));

            ConstructionResourceQuoteLine hardwareLine = BuildHardwareQuoteLine(nailsUnits);
            int localHardwareCost = Mathf.Max(0, blacksmithHardwareUnitCostCents) * Mathf.Max(0, nailsUnits);
            quote.cashCostCents = Mathf.Max(0, quote.cashCostCents + GetQuoteLineAllocatedCost(hardwareLine) - localHardwareCost);
            quote.resources.Add(hardwareLine);

            quote.resources.Add(ConstructionResourceQuoteLine.Create(
                ConstructionResourceKind.Labor,
                "Labor",
                laborUnits,
                GetAvailableLaborUnits(out string laborSource),
                Mathf.Max(0, laborUnitCostCents),
                laborSource));
        }

        private ConstructionResourceQuoteLine BuildHardwareQuoteLine(int nailsUnits)
        {
            int requested = Mathf.Max(0, nailsUnits);
            int localUnitCost = Mathf.Max(0, blacksmithHardwareUnitCostCents);
            int fallbackUnitCost = GetRegionalHardwareFallbackUnitCostCents();
            int blacksmithAvailable = GetAvailableBlacksmithHardwareStock(
                out string hardwareSource,
                out BusinessInstanceState blacksmith,
                out _);

            if (blacksmithAvailable >= requested)
            {
                return ConstructionResourceQuoteLine.Create(
                    ConstructionResourceKind.Nails,
                    "Nails / Simple Hardware",
                    requested,
                    blacksmithAvailable,
                    localUnitCost,
                    hardwareSource);
            }

            int blacksmithUnits = Mathf.Min(Mathf.Max(0, blacksmithAvailable), requested);
            int fallbackUnits = Mathf.Max(0, requested - blacksmithUnits);
            List<ConstructionResourceSourceAllocation> allocations = new();
            if (blacksmithUnits > 0 && blacksmith != null)
            {
                allocations.Add(ConstructionResourceSourceAllocation.Create(
                    BusinessType.Blacksmith,
                    blacksmith.InstanceId,
                    blacksmith.RuntimeDisplayName,
                    blacksmithUnits,
                    localUnitCost,
                    100));
            }

            if (fallbackUnits > 0)
            {
                allocations.Add(ConstructionResourceSourceAllocation.Create(
                    BusinessType.Blacksmith,
                    string.Empty,
                    GetRegionalHardwareFallbackSourceLabel(),
                    fallbackUnits,
                    fallbackUnitCost,
                    0));
            }

            string sourceLabel = blacksmithUnits > 0
                ? $"{hardwareSource} + {GetRegionalHardwareFallbackSourceLabel()}"
                : GetRegionalHardwareFallbackSourceLabel();
            ConstructionResourceQuoteLine line = ConstructionResourceQuoteLine.Create(
                ConstructionResourceKind.Nails,
                "Nails / Simple Hardware",
                requested,
                requested,
                GetWeightedUnitCostCents(allocations, fallbackUnitCost),
                sourceLabel,
                allocations);
            line.procurementNote = BuildRegionalHardwareFallbackProcurementNote(fallbackUnits);
            line.procurementLeadWeeks = fallbackUnits > 0 ? 1 : 0;
            return line;
        }

        private void AppendConstructionInputAvailability(StringBuilder builder)
        {
            if (builder == null)
            {
                return;
            }

            EnsureConstructionSupportNodesLoaded();
            TownGenerationSettings settings = townWorld != null ? townWorld.Settings : null;
            int lumberUnits = GetAvailableSupportStock(ConstructionResourceKind.Lumber, out string lumberSource);
            int lumberBaselineCost = GetSupportNodeUnitCost(ConstructionResourceKind.Lumber);
            if (sharedBusinessRuntime != null
                && sharedBusinessRuntime.TryGetSharedLumberAvailability(-1, lumberBaselineCost, out int sharedLumberUnits, out string sharedLumberSource))
            {
                lumberUnits = sharedLumberUnits;
                lumberSource = string.IsNullOrWhiteSpace(sharedLumberSource) ? "Shared lumber sellers" : sharedLumberSource;
            }

            int hardwareUnits = GetAvailableBlacksmithHardwareStock(out string hardwareSource);
            int laborUnits = GetAvailableLaborUnits(out string laborSource);

            builder.AppendLine("Construction Inputs");
            if (settings != null)
            {
                builder.AppendLine($"Builder support: {settings.BuildCarpenterAvailabilitySummary()}");
            }

            builder.AppendLine($"Lumber: {lumberUnits} available ({lumberSource})");
            if (TryGetConstructionSupportNode(ConstructionResourceKind.Lumber, out ConstructionSupportNodeState lumberNode)
                && lumberNode != null
                && lumberNode.RemoteProductionEnabled)
            {
                builder.AppendLine(lumberNode.BuildSawmillStatusLine());
            }

            builder.AppendLine($"Nails / simple hardware: {hardwareUnits} available ({hardwareSource})");
            builder.AppendLine($"Labor: {laborUnits} units available ({laborSource})");
        }

        private int GetAvailableCashForBusinessActivation(BusinessActivationCandidateState candidate)
        {
            return GetAvailableCashCents();
        }

        private string BuildQuoteBlockedMessage(ConstructionInputQuote quote)
        {
            if (quote == null)
            {
                return "Construction quote is unavailable.";
            }

            return $"{quote.projectLabel} is blocked. Missing: {quote.BuildMissingSummary()}.";
        }

        private string BuildSupportConsumptionSummary(ConstructionInputQuote quote)
        {
            if (quote == null || quote.resources == null || quote.resources.Count == 0)
            {
                return "no tracked inputs";
            }

            StringBuilder builder = new();
            for (int i = 0; i < quote.resources.Count; i++)
            {
                ConstructionResourceQuoteLine line = quote.resources[i];
                if (line == null || line.requiredUnits <= 0)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(line.requiredUnits);
                builder.Append(' ');
                builder.Append(line.displayName);
                if (!string.IsNullOrWhiteSpace(line.sourceLabel))
                {
                    builder.Append(" from ");
                    builder.Append(line.sourceLabel);
                }

                if (!string.IsNullOrWhiteSpace(line.procurementNote))
                {
                    builder.Append(" (");
                    builder.Append(line.procurementNote);
                    builder.Append(')');
                }
            }

            return builder.Length == 0 ? "no tracked inputs" : builder.ToString();
        }

        private sealed class ConstructionInputRollbackState
        {
            private readonly AcquisitionMarketManager owner;
            private readonly List<SupportNodeSnapshot> supportNodeSnapshots = new();
            private readonly List<BusinessSnapshot> businessSnapshots = new();

            public ConstructionInputRollbackState(AcquisitionMarketManager owner)
            {
                this.owner = owner;
            }

            public void TrackSupportNode(ConstructionSupportNodeState node)
            {
                if (node == null)
                {
                    return;
                }

                for (int i = 0; i < supportNodeSnapshots.Count; i++)
                {
                    if (ReferenceEquals(supportNodeSnapshots[i].Node, node))
                    {
                        return;
                    }
                }

                supportNodeSnapshots.Add(new SupportNodeSnapshot(node));
            }

            public void TrackSharedLumberAllocations(IReadOnlyList<ConstructionResourceSourceAllocation> allocations)
            {
                if (allocations == null || owner == null)
                {
                    return;
                }

                for (int i = 0; i < allocations.Count; i++)
                {
                    ConstructionResourceSourceAllocation allocation = allocations[i];
                    if (allocation == null || allocation.allocatedUnits <= 0 || !allocation.HasBusinessSeller)
                    {
                        continue;
                    }

                    TrackBusinessCategory(
                        owner.FindBusinessByInstanceId(allocation.businessInstanceId),
                        DefaultLumberCategoryId);
                }
            }

            public void TrackSawmillBusiness()
            {
                TrackBusinessCategory(owner?.sharedBusinessRuntime?.FindByType(BusinessType.Sawmill), DefaultLumberCategoryId);
            }

            public void TrackBusinessCategory(BusinessInstanceState business, string categoryId)
            {
                if (business == null || business.RuntimeState == null || string.IsNullOrWhiteSpace(categoryId))
                {
                    return;
                }

                BusinessSnapshot businessSnapshot = GetOrAddBusinessSnapshot(business);
                businessSnapshot.TrackCategory(categoryId);
            }

            public void Rollback()
            {
                for (int i = businessSnapshots.Count - 1; i >= 0; i--)
                {
                    businessSnapshots[i].Restore();
                }

                for (int i = supportNodeSnapshots.Count - 1; i >= 0; i--)
                {
                    SupportNodeSnapshot snapshot = supportNodeSnapshots[i];
                    snapshot.Restore();
                    if (snapshot.Node != null && snapshot.Node.ResourceKind == ConstructionResourceKind.Lumber)
                    {
                        owner?.sharedBusinessRuntime?.SetSawmillConstructionSupportNode(snapshot.Node);
                    }
                }
            }

            private BusinessSnapshot GetOrAddBusinessSnapshot(BusinessInstanceState business)
            {
                for (int i = 0; i < businessSnapshots.Count; i++)
                {
                    if (ReferenceEquals(businessSnapshots[i].Business, business))
                    {
                        return businessSnapshots[i];
                    }
                }

                BusinessSnapshot snapshot = new(business);
                businessSnapshots.Add(snapshot);
                return snapshot;
            }

            private sealed class SupportNodeSnapshot
            {
                public SupportNodeSnapshot(ConstructionSupportNodeState node)
                {
                    Node = node;
                    currentStockUnits = node != null ? node.CurrentStockUnits : 0;
                }

                private readonly int currentStockUnits;

                public ConstructionSupportNodeState Node { get; }

                public void Restore()
                {
                    if (Node == null)
                    {
                        return;
                    }

                    if (Node.ResourceKind == ConstructionResourceKind.Lumber)
                    {
                        Node.SetConstructionAvailableStockFromSharedSellers(currentStockUnits);
                        return;
                    }

                    int missingUnits = Mathf.Max(0, currentStockUnits - Node.CurrentStockUnits);
                    if (missingUnits > 0)
                    {
                        Node.AddStock(missingUnits, false);
                    }
                }
            }

            private sealed class BusinessSnapshot
            {
                private readonly BusinessRuntimeState runtime;
                private readonly BusinessRuntimeTransactionSnapshot transactionSnapshot;
                private readonly List<CategorySnapshot> categorySnapshots = new();

                public BusinessSnapshot(BusinessInstanceState business)
                {
                    Business = business;
                    runtime = business != null ? business.RuntimeState : null;
                    transactionSnapshot = runtime != null
                        ? runtime.CaptureTransactionSnapshot()
                        : default;
                }

                public BusinessInstanceState Business { get; }

                public void TrackCategory(string categoryId)
                {
                    if (runtime == null || string.IsNullOrWhiteSpace(categoryId))
                    {
                        return;
                    }

                    for (int i = 0; i < categorySnapshots.Count; i++)
                    {
                        if (string.Equals(categorySnapshots[i].CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                    }

                    CategoryStockState stock = runtime.GetCategoryStock(categoryId);
                    if (stock != null)
                    {
                        categorySnapshots.Add(new CategorySnapshot(categoryId, stock.CurrentStockUnits));
                    }
                }

                public void Restore()
                {
                    if (runtime == null)
                    {
                        return;
                    }

                    for (int i = 0; i < categorySnapshots.Count; i++)
                    {
                        runtime.SetCategoryStockUnits(categorySnapshots[i].CategoryId, categorySnapshots[i].CurrentStockUnits);
                    }

                    runtime.RestoreTransactionSnapshot(transactionSnapshot);
                    Business?.RefreshCapacityState();
                }
            }

            private readonly struct CategorySnapshot
            {
                public CategorySnapshot(string categoryId, int currentStockUnits)
                {
                    CategoryId = categoryId ?? string.Empty;
                    CurrentStockUnits = Mathf.Max(0, currentStockUnits);
                }

                public string CategoryId { get; }
                public int CurrentStockUnits { get; }
            }
        }

        private bool TryConsumeConstructionInputs(ConstructionInputQuote quote, ConstructionInputRollbackState rollback, out string message)
        {
            if (quote == null)
            {
                message = "Construction quote is unavailable.";
                return false;
            }

            if (!quote.CanProceed)
            {
                message = BuildQuoteBlockedMessage(quote);
                return false;
            }

            ConstructionResourceQuoteLine lumber = quote.GetLine(ConstructionResourceKind.Lumber);
            if (lumber != null && lumber.requiredUnits > 0)
            {
                if (lumber.HasSourceAllocations && sharedBusinessRuntime != null)
                {
                    rollback?.TrackSharedLumberAllocations(lumber.sourceAllocations);
                    if (!sharedBusinessRuntime.TryConsumeLumberPurchaseAllocations(
                        lumber.sourceAllocations,
                        lumber.requiredUnits,
                        out int consumed,
                        out _,
                        out string consumeMessage)
                        || consumed < lumber.requiredUnits)
                    {
                        message = consumeMessage;
                        return false;
                    }

                }
                else
                {
                    if (!TryGetConstructionSupportNode(ConstructionResourceKind.Lumber, out ConstructionSupportNodeState node)
                        || node == null)
                    {
                        message = $"Could not consume {lumber.requiredUnits} lumber from the local sawmill.";
                        return false;
                    }

                    rollback?.TrackSupportNode(node);
                    rollback?.TrackSawmillBusiness();
                    if (!node.TryConsume(lumber.requiredUnits))
                    {
                        message = $"Could not consume {lumber.requiredUnits} lumber from the local sawmill.";
                        return false;
                    }

                    sharedBusinessRuntime?.RecordSawmillConstructionLumberSale(lumber.requiredUnits, lumber.unitCostCents);
                }
            }

            ConstructionResourceQuoteLine nails = quote.GetLine(ConstructionResourceKind.Nails);
            if (nails != null && nails.requiredUnits > 0)
            {
                if (nails.HasSourceAllocations)
                {
                    int satisfied = 0;
                    for (int i = 0; i < nails.sourceAllocations.Count; i++)
                    {
                        ConstructionResourceSourceAllocation allocation = nails.sourceAllocations[i];
                        if (allocation == null || allocation.allocatedUnits <= 0)
                        {
                            continue;
                        }

                        if (IsRegionalHardwareFallbackAllocation(allocation))
                        {
                            satisfied += allocation.allocatedUnits;
                            continue;
                        }

                        BusinessInstanceState blacksmith = FindBusinessByInstanceId(allocation.businessInstanceId);
                        rollback?.TrackBusinessCategory(blacksmith, GetBlacksmithHardwareCategoryId());
                        if (blacksmith == null
                            || blacksmith.RuntimeState == null
                            || !blacksmith.RuntimeState.TryConsumeCategoryStockUnits(GetBlacksmithHardwareCategoryId(), allocation.allocatedUnits, out int consumed)
                            || consumed < allocation.allocatedUnits)
                        {
                            message = $"Could not consume {allocation.allocatedUnits} nails/simple hardware from {allocation.sourceLabel}.";
                            return false;
                        }

                        RecordBlacksmithHardwareSale(blacksmith, consumed, allocation.unitCostCents);
                        satisfied += consumed;
                    }

                    if (satisfied < nails.requiredUnits)
                    {
                        message = $"Could not source {nails.requiredUnits} nails/simple hardware from {nails.sourceLabel}.";
                        return false;
                    }
                }
                else
                {
                    BusinessInstanceState blacksmith = FindActiveBlacksmith();
                    rollback?.TrackBusinessCategory(blacksmith, GetBlacksmithHardwareCategoryId());
                    if (blacksmith == null
                        || blacksmith.RuntimeState == null
                        || !blacksmith.RuntimeState.TryConsumeCategoryStockUnits(GetBlacksmithHardwareCategoryId(), nails.requiredUnits, out int consumed)
                        || consumed < nails.requiredUnits)
                    {
                        message = $"Could not consume {nails.requiredUnits} nails/simple hardware from an active blacksmith.";
                        return false;
                    }

                    RecordBlacksmithHardwareSale(blacksmith, consumed, nails.unitCostCents);
                }
            }

            message = "Construction inputs consumed.";
            return true;
        }

        private bool TryValidateConstructionInputsAvailable(ConstructionInputQuote quote, out string message)
        {
            if (quote == null)
            {
                message = "Construction quote is unavailable.";
                return false;
            }

            if (!quote.CanProceed)
            {
                message = BuildQuoteBlockedMessage(quote);
                return false;
            }

            ConstructionResourceQuoteLine lumber = quote.GetLine(ConstructionResourceKind.Lumber);
            if (lumber != null && lumber.requiredUnits > 0)
            {
                if (lumber.HasSourceAllocations)
                {
                    int allocatedUnits = 0;
                    for (int i = 0; i < lumber.sourceAllocations.Count; i++)
                    {
                        allocatedUnits += lumber.sourceAllocations[i] != null ? lumber.sourceAllocations[i].allocatedUnits : 0;
                    }

                    if (allocatedUnits < lumber.requiredUnits)
                    {
                        message = $"Not enough lumber from shared lumber sellers. Need {lumber.requiredUnits}, have {allocatedUnits}.";
                        return false;
                    }
                }
                else if (!TryGetConstructionSupportNode(ConstructionResourceKind.Lumber, out ConstructionSupportNodeState node)
                    || node == null
                    || !node.Active
                    || node.CurrentStockUnits < lumber.requiredUnits)
                {
                    message = $"Not enough lumber from the local sawmill. Need {lumber.requiredUnits}, have {Mathf.Max(0, node != null ? node.CurrentStockUnits : 0)}.";
                    return false;
                }
            }

            ConstructionResourceQuoteLine nails = quote.GetLine(ConstructionResourceKind.Nails);
            if (nails != null && nails.requiredUnits > 0)
            {
                if (nails.HasSourceAllocations)
                {
                    int allocatedUnits = 0;
                    for (int i = 0; i < nails.sourceAllocations.Count; i++)
                    {
                        ConstructionResourceSourceAllocation allocation = nails.sourceAllocations[i];
                        if (allocation == null || allocation.allocatedUnits <= 0)
                        {
                            continue;
                        }

                        allocatedUnits += allocation.allocatedUnits;
                        if (IsRegionalHardwareFallbackAllocation(allocation))
                        {
                            continue;
                        }

                        BusinessInstanceState blacksmith = FindBusinessByInstanceId(allocation.businessInstanceId);
                        CategoryStockState stock = blacksmith != null && blacksmith.RuntimeState != null
                            ? blacksmith.RuntimeState.GetCategoryStock(GetBlacksmithHardwareCategoryId())
                            : null;
                        int available = stock != null ? stock.CurrentStockUnits : 0;
                        if (available < allocation.allocatedUnits)
                        {
                            message = $"Not enough nails/simple hardware from {allocation.sourceLabel}. Need {allocation.allocatedUnits}, have {available}.";
                            return false;
                        }
                    }

                    if (allocatedUnits < nails.requiredUnits)
                    {
                        message = $"Not enough nails/simple hardware from {nails.sourceLabel}. Need {nails.requiredUnits}, have {allocatedUnits}.";
                        return false;
                    }
                }
                else
                {
                    BusinessInstanceState blacksmith = FindActiveBlacksmith();
                    CategoryStockState stock = blacksmith != null && blacksmith.RuntimeState != null
                        ? blacksmith.RuntimeState.GetCategoryStock(GetBlacksmithHardwareCategoryId())
                        : null;
                    int available = stock != null ? stock.CurrentStockUnits : 0;
                    if (available < nails.requiredUnits)
                    {
                        message = $"Not enough nails/simple hardware from an active blacksmith. Need {nails.requiredUnits}, have {available}.";
                        return false;
                    }
                }
            }

            ConstructionResourceQuoteLine labor = quote.GetLine(ConstructionResourceKind.Labor);
            if (labor != null && labor.requiredUnits > 0)
            {
                int availableLabor = GetAvailableLaborUnits(out _);
                if (availableLabor < labor.requiredUnits)
                {
                    message = $"Not enough available labor. Need {labor.requiredUnits}, have {availableLabor}.";
                    return false;
                }
            }

            message = "Construction inputs available.";
            return true;
        }

        private bool TryConsumeSawmillMaintenanceHardware(int requiredUnits, out string sourceLabel, out string blockedReason)
        {
            sourceLabel = string.Empty;
            blockedReason = string.Empty;
            int requested = Mathf.Max(0, requiredUnits);
            if (requested <= 0)
            {
                sourceLabel = "no hardware maintenance required";
                return true;
            }

            BusinessInstanceState blacksmith = FindActiveBlacksmith();
            CategoryStockState stock = blacksmith != null && blacksmith.RuntimeState != null
                ? blacksmith.RuntimeState.GetCategoryStock(GetBlacksmithHardwareCategoryId())
                : null;
            int localUnits = Mathf.Min(requested, Mathf.Max(0, stock != null ? stock.CurrentStockUnits : 0));
            int fallbackUnits = Mathf.Max(0, requested - localUnits);
            int fallbackCostCents = fallbackUnits * GetRegionalHardwareFallbackUnitCostCents();
            BusinessInstanceState sawmillBusiness = fallbackUnits > 0 ? FindBusinessByType(BusinessType.Sawmill) : null;
            if (fallbackCostCents > 0)
            {
                if (sawmillBusiness == null || sawmillBusiness.RuntimeState == null)
                {
                    blockedReason = $"missing {requested} nails/simple hardware for sawmill maintenance; {GetRegionalHardwareFallbackSourceLabel()} requires a paying sawmill account";
                    return false;
                }

                if (sawmillBusiness.RuntimeState.CurrentCashCents < fallbackCostCents)
                {
                    blockedReason = $"missing {requested} nails/simple hardware for sawmill maintenance; {GetRegionalHardwareFallbackSourceLabel()} costs {FormatMoney(fallbackCostCents)}";
                    return false;
                }
            }

            int consumed = 0;
            if (localUnits > 0)
            {
                if (blacksmith == null
                    || blacksmith.RuntimeState == null
                    || !blacksmith.RuntimeState.TryConsumeCategoryStockUnits(GetBlacksmithHardwareCategoryId(), localUnits, out consumed)
                    || consumed < localUnits)
                {
                    blockedReason = $"missing {requested} nails/simple hardware for sawmill maintenance";
                    return false;
                }

                int hardwareRevenueCents = consumed * Mathf.Max(0, blacksmithHardwareUnitCostCents);
                if (hardwareRevenueCents > 0)
                {
                    blacksmith.RuntimeState.AddWeeklyLocalTransferRevenue(
                        hardwareRevenueCents,
                        $"sold {consumed} Nails / Simple Hardware to sawmill maintenance for {FormatMoney(hardwareRevenueCents)}");
                }

                blacksmith.RefreshCapacityState();
            }

            if (fallbackCostCents > 0)
            {
                sawmillBusiness.RuntimeState.AddWeeklyLocalTransferCost(
                    fallbackCostCents,
                    $"bought {fallbackUnits} Nails / Simple Hardware through {GetRegionalHardwareFallbackSourceLabel()} for {FormatMoney(fallbackCostCents)}");
                sawmillBusiness.RefreshCapacityState();
            }

            if (consumed > 0 && fallbackUnits > 0)
            {
                sourceLabel = $"{blacksmith.RuntimeDisplayName} + {GetRegionalHardwareFallbackSourceLabel()}";
            }
            else if (consumed > 0)
            {
                sourceLabel = blacksmith.RuntimeDisplayName;
            }
            else
            {
                sourceLabel = GetRegionalHardwareFallbackSourceLabel();
            }

            return true;
        }

        private static string BuildSawmillProductionSummary(
            ConstructionSupportNodeState sawmill,
            int cutLogs,
            int processedLogs,
            int producedLumber,
            string laborSource,
            string blockedReason)
        {
            if (sawmill == null)
            {
                return "Remote sawmill production unavailable.";
            }

            StringBuilder builder = new();
            builder.Append($"cut {Mathf.Max(0, cutLogs)} timber, processed {Mathf.Max(0, processedLogs)} logs, produced {Mathf.Max(0, producedLumber)} lumber");
            builder.Append($"; stock {sawmill.CurrentStockUnits}/{sawmill.LumberStorageCapacityUnits} lumber, {sawmill.LogStockUnits} logs, {sawmill.StandingTimberUnits} standing timber");
            if (!string.IsNullOrWhiteSpace(laborSource))
            {
                builder.Append($"; crew {sawmill.SawmillCrewLabel} from {laborSource}");
            }

            if (!string.IsNullOrWhiteSpace(blockedReason))
            {
                builder.Append($"; blocked: {blockedReason}");
            }

            return builder.ToString();
        }

        private static string BuildDistinctReasonSummary(List<string> reasons)
        {
            if (reasons == null || reasons.Count == 0)
            {
                return string.Empty;
            }

            List<string> distinct = new();
            for (int i = 0; i < reasons.Count; i++)
            {
                string reason = reasons[i];
                if (string.IsNullOrWhiteSpace(reason))
                {
                    continue;
                }

                reason = reason.Trim();
                bool exists = false;
                for (int j = 0; j < distinct.Count; j++)
                {
                    if (string.Equals(distinct[j], reason, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    distinct.Add(reason);
                }
            }

            return distinct.Count > 0 ? string.Join("; ", distinct) : string.Empty;
        }

        private void SyncSawmillRemoteProductionSites()
        {
            if (townWorld == null || townWorld.Plots == null)
            {
                return;
            }

            int plotId = FindSawmillYardPlotId();
            if (plotId < 0)
            {
                return;
            }

            for (int i = 0; i < constructionSupportNodes.Count; i++)
            {
                ConstructionSupportNodeState node = constructionSupportNodes[i];
                if (node != null && node.ResourceKind == ConstructionResourceKind.Lumber)
                {
                    node.SetRemotePropertyPlotId(plotId);
                }
            }
        }

        private int FindSawmillYardPlotId()
        {
            if (townWorld == null || townWorld.Plots == null)
            {
                return -1;
            }

            for (int i = 0; i < townWorld.Plots.Count; i++)
            {
                TownPlot plot = townWorld.Plots[i];
                if (plot != null
                    && plot.zone == PlotZone.Agricultural
                    && plot.agriculturalSiteRole == AgriculturalSiteRole.SawmillYard)
                {
                    return plot.id;
                }
            }

            return -1;
        }

        private void EnsureConstructionSupportNodesLoaded()
        {
            if (constructionSupportDefinitions == null || constructionSupportDefinitions.Length == 0)
            {
                constructionSupportDefinitions = Resources.LoadAll<ConstructionSupportNodeDefinition>(DefaultConstructionSupportResourcePath);
            }

            if (constructionSupportNodes.Count == 0)
            {
                if (constructionSupportDefinitions != null && constructionSupportDefinitions.Length > 0)
                {
                    for (int i = 0; i < constructionSupportDefinitions.Length; i++)
                    {
                        ConstructionSupportNodeDefinition definition = constructionSupportDefinitions[i];
                        if (definition != null)
                        {
                            constructionSupportNodes.Add(ConstructionSupportNodeState.FromDefinition(definition));
                        }
                    }
                }

                if (constructionSupportNodes.Count == 0)
                {
                    constructionSupportNodes.Add(ConstructionSupportNodeState.CreateFallbackSawmill());
                }
            }

            MergeMissingConstructionSupportDefinitions();
            SyncSawmillRemoteProductionSites();
            LinkSawmillSupportNodeToSharedRuntime();
        }

        private void LinkSawmillSupportNodeToSharedRuntime()
        {
            if (sharedBusinessRuntime == null)
            {
                return;
            }

            for (int i = 0; i < constructionSupportNodes.Count; i++)
            {
                ConstructionSupportNodeState node = constructionSupportNodes[i];
                if (node != null && node.Active && node.ResourceKind == ConstructionResourceKind.Lumber)
                {
                    sharedBusinessRuntime.SetSawmillConstructionSupportNode(node);
                    return;
                }
            }

            sharedBusinessRuntime.SetSawmillConstructionSupportNode(null);
        }

        private void RestoreConstructionSupportNodes(IReadOnlyList<ConstructionSupportNodeSaveDto> dtos)
        {
            constructionSupportNodes.Clear();
            if (dtos != null)
            {
                for (int i = 0; i < dtos.Count; i++)
                {
                    ConstructionSupportNodeState state = ConstructionSupportNodeState.FromSaveDto(dtos[i]);
                    if (state != null)
                    {
                        constructionSupportNodes.Add(state);
                    }
                }
            }

            EnsureConstructionSupportNodesLoaded();
        }

        private void RestoreConstructionProjects(IReadOnlyList<ConstructionProjectSaveDto> dtos)
        {
            activeConstructionProjects.Clear();
            nextConstructionProjectSerial = 1;
            if (dtos == null)
            {
                return;
            }

            for (int i = 0; i < dtos.Count; i++)
            {
                ConstructionProjectState state = ConstructionProjectState.FromSaveDto(dtos[i]);
                if (state == null || !state.IsActive)
                {
                    continue;
                }

                activeConstructionProjects.Add(state);
                nextConstructionProjectSerial = Mathf.Max(nextConstructionProjectSerial, TryExtractConstructionProjectSerial(state.projectId) + 1);
            }
        }

        private void RestoreLandAppreciations(IReadOnlyList<LandAppreciationSaveDto> dtos)
        {
            ownedLandAppreciations.Clear();
            if (dtos == null)
            {
                return;
            }

            for (int i = 0; i < dtos.Count; i++)
            {
                LandAppreciationState state = CreateLandAppreciationState(dtos[i]);
                if (state == null || state.plotId < 0 || FindLandAppreciation(state.plotId) != null)
                {
                    continue;
                }

                ownedLandAppreciations.Add(state);
            }
        }

        private void RefreshOwnedLandAppreciations()
        {
            AutoWire();
            if (townWorld == null || townWorld.Plots == null)
            {
                return;
            }

            BackfillMissingLandAppreciationsForOwnedPlots();
            int currentDayIndex = GetCurrentDayIndex();
            for (int i = ownedLandAppreciations.Count - 1; i >= 0; i--)
            {
                LandAppreciationState state = ownedLandAppreciations[i];
                if (state == null)
                {
                    ownedLandAppreciations.RemoveAt(i);
                    continue;
                }

                TownPlot plot = GetPlot(state.plotId);
                if (plot == null || !IsPlayerOwnedPlot(plot))
                {
                    ownedLandAppreciations.RemoveAt(i);
                    continue;
                }

                RefreshLandAppreciationState(state, plot, ResolveLandAppreciationBuilding(plot, state), currentDayIndex);
            }
        }

        private void BackfillMissingLandAppreciationsForOwnedPlots()
        {
            if (townWorld == null || townWorld.Plots == null)
            {
                return;
            }

            int currentDayIndex = GetCurrentDayIndex();
            for (int i = 0; i < townWorld.Plots.Count; i++)
            {
                TownPlot plot = townWorld.Plots[i];
                if (plot == null || !IsPlayerOwnedPlot(plot) || FindLandAppreciation(plot.id) != null)
                {
                    continue;
                }

                PlacedBuilding building = plot.buildingId >= 0 ? GetBuilding(plot.buildingId) : null;
                int basis = EstimateFallbackBasisCents(plot, building);
                LandAppreciationState state = new()
                {
                    plotId = plot.id,
                    buildingId = building != null ? building.id : -1,
                    holdingKind = LandAppreciationHoldingKind.UrbanParcel,
                    improvementState = ResolveCurrentImprovementState(plot, building, LandAppreciationImprovementState.Empty),
                    purchaseBasisCents = basis,
                    capitalizedImprovementCents = 0,
                    currentEstimatedValueCents = basis,
                    appreciationDeltaCents = 0,
                    purchaseDayIndex = currentDayIndex,
                    lastValuationDayIndex = currentDayIndex,
                    townGrowthPressure01 = EstimateTownGrowthPressure01(plot),
                    developmentReadiness01 = EstimateDevelopmentReadiness01(plot, building, ResolveCurrentImprovementState(plot, building, LandAppreciationImprovementState.Empty))
                };

                ownedLandAppreciations.Add(state);
            }
        }

        private LandAppreciationState RecordLandAppreciationPurchase(
            TownPlot plot,
            PlacedBuilding building,
            int purchaseBasisCents,
            LandAppreciationImprovementState improvementState)
        {
            if (plot == null)
            {
                return null;
            }

            LandAppreciationState state = FindLandAppreciation(plot.id);
            int currentDayIndex = GetCurrentDayIndex();
            if (state == null)
            {
                state = new LandAppreciationState
                {
                    plotId = plot.id,
                    purchaseDayIndex = currentDayIndex,
                    lastValuationDayIndex = currentDayIndex
                };
                ownedLandAppreciations.Add(state);
            }

            state.buildingId = building != null ? building.id : state.buildingId;
            state.holdingKind = LandAppreciationHoldingKind.UrbanParcel;
            state.improvementState = MaxImprovementState(state.improvementState, improvementState);
            state.purchaseBasisCents = Mathf.Max(1, purchaseBasisCents);
            state.capitalizedImprovementCents = Mathf.Max(0, state.capitalizedImprovementCents);
            state.currentEstimatedValueCents = Mathf.Max(1, state.currentEstimatedValueCents);
            RefreshLandAppreciationState(state, plot, building, currentDayIndex);
            return state;
        }

        private void CapitalizeLandImprovement(
            TownPlot plot,
            PlacedBuilding building,
            int improvementCostCents,
            LandAppreciationImprovementState improvementState)
        {
            LandAppreciationState state = EnsureLandAppreciationState(plot, building, improvementState);
            if (state == null)
            {
                return;
            }

            state.capitalizedImprovementCents = Mathf.Max(0, state.capitalizedImprovementCents) + Mathf.Max(0, improvementCostCents);
            state.improvementState = MaxImprovementState(state.improvementState, improvementState);
            RefreshLandAppreciationState(state, plot, building, GetCurrentDayIndex());
        }

        private void UpdateLandAppreciationImprovement(
            TownPlot plot,
            PlacedBuilding building,
            LandAppreciationImprovementState improvementState)
        {
            LandAppreciationState state = EnsureLandAppreciationState(plot, building, improvementState);
            if (state == null)
            {
                return;
            }

            state.improvementState = MaxImprovementState(state.improvementState, improvementState);
            RefreshLandAppreciationState(state, plot, building, GetCurrentDayIndex());
        }

        private LandAppreciationState EnsureLandAppreciationState(
            TownPlot plot,
            PlacedBuilding building,
            LandAppreciationImprovementState improvementState)
        {
            if (plot == null)
            {
                return null;
            }

            LandAppreciationState state = FindLandAppreciation(plot.id);
            if (state != null)
            {
                return state;
            }

            int currentDayIndex = GetCurrentDayIndex();
            int basis = EstimateFallbackBasisCents(plot, building);
            state = new LandAppreciationState
            {
                plotId = plot.id,
                buildingId = building != null ? building.id : -1,
                holdingKind = LandAppreciationHoldingKind.UrbanParcel,
                improvementState = improvementState,
                purchaseBasisCents = basis,
                currentEstimatedValueCents = basis,
                appreciationDeltaCents = 0,
                purchaseDayIndex = currentDayIndex,
                lastValuationDayIndex = currentDayIndex
            };
            ownedLandAppreciations.Add(state);
            return state;
        }

        private void RefreshLandAppreciationState(
            LandAppreciationState state,
            TownPlot plot,
            PlacedBuilding building,
            int currentDayIndex)
        {
            if (state == null || plot == null)
            {
                return;
            }

            state.plotId = plot.id;
            state.buildingId = building != null ? building.id : (plot.buildingId >= 0 ? plot.buildingId : -1);
            state.holdingKind = LandAppreciationHoldingKind.UrbanParcel;
            state.improvementState = ResolveCurrentImprovementState(plot, building, state.improvementState);
            if (state.purchaseBasisCents <= 0)
            {
                state.purchaseBasisCents = EstimateFallbackBasisCents(plot, building);
            }

            state.capitalizedImprovementCents = Mathf.Max(0, state.capitalizedImprovementCents);
            state.purchaseDayIndex = Mathf.Max(0, state.purchaseDayIndex);
            state.townGrowthPressure01 = EstimateTownGrowthPressure01(plot);
            state.developmentReadiness01 = EstimateDevelopmentReadiness01(plot, building, state.improvementState);

            LandAppreciationResult result = landAppreciationEvaluator.Evaluate(new LandAppreciationInputs
            {
                holdingKind = state.holdingKind,
                improvementState = state.improvementState,
                purchaseBasisCents = state.purchaseBasisCents,
                capitalizedImprovementCents = state.capitalizedImprovementCents,
                purchaseDayIndex = state.purchaseDayIndex,
                currentDayIndex = currentDayIndex,
                townGrowthPressure01 = state.townGrowthPressure01,
                developmentReadiness01 = state.developmentReadiness01
            });

            state.currentEstimatedValueCents = result.EstimatedValueCents;
            state.appreciationDeltaCents = result.AppreciationDeltaCents;
            state.lastValuationDayIndex = currentDayIndex;
        }

        private LandAppreciationState FindLandAppreciation(int plotId)
        {
            for (int i = 0; i < ownedLandAppreciations.Count; i++)
            {
                LandAppreciationState state = ownedLandAppreciations[i];
                if (state != null && state.plotId == plotId)
                {
                    return state;
                }
            }

            return null;
        }

        private PlacedBuilding ResolveLandAppreciationBuilding(TownPlot plot, LandAppreciationState state)
        {
            if (state != null && state.buildingId >= 0)
            {
                PlacedBuilding savedBuilding = GetBuilding(state.buildingId);
                if (savedBuilding != null)
                {
                    return savedBuilding;
                }
            }

            return plot != null && plot.buildingId >= 0 ? GetBuilding(plot.buildingId) : null;
        }

        private int EstimateFallbackBasisCents(TownPlot plot, PlacedBuilding building)
        {
            int landValue = plot != null
                ? CalculateLandPriceCents(plot)
                : building != null ? building.siteSizeCells.x * building.siteSizeCells.y * baseLandPricePerCellCents : 1;
            if (building == null)
            {
                return Mathf.Max(1, landValue);
            }

            int buildingValue = building.footprint.Area * buildingValuePerFootprintCellCents;
            int businessValue = IsOperatingBusinessAtBuilding(building.id) ? businessIncomeProxyCents : 0;
            float mixedUseMultiplier = building.definition != null && building.definition.IsMixedUse ? mixedUseBusinessMultiplier : 1f;
            return Mathf.Max(1, Mathf.RoundToInt((landValue + buildingValue + businessValue) * mixedUseMultiplier));
        }

        private float EstimateTownGrowthPressure01(TownPlot plot)
        {
            if (plot == null)
            {
                return 0.5f;
            }

            float zonePressure = plot.zone switch
            {
                PlotZone.Business => 0.78f,
                PlotZone.MixedUse => 0.66f,
                PlotZone.Residential => 0.48f,
                _ => 0.5f
            };
            float centerPressure = 0.5f;
            if (townWorld != null && townWorld.Grid != null)
            {
                float halfDepth = Mathf.Max(1f, townWorld.Grid.Depth * 0.5f);
                centerPressure = 1f - Mathf.Clamp01(Mathf.Abs(plot.roadAccessCell.z - halfDepth) / halfDepth);
            }

            float frontagePressure = Mathf.Clamp01(plot.frontageCells / 8f);
            float baseline = Mathf.Clamp01(zonePressure * 0.45f + centerPressure * 0.35f + frontagePressure * 0.2f);
            return Mathf.Clamp01(baseline + GetTownHallContribution().LandConfidence01);
        }

        private float EstimateDevelopmentReadiness01(
            TownPlot plot,
            PlacedBuilding building,
            LandAppreciationImprovementState improvementState)
        {
            if (plot == null)
            {
                return ApplyTownHallMaturityContribution(0.5f);
            }

            if (improvementState == LandAppreciationImprovementState.OperatingBusiness)
            {
                return ApplyTownHallMaturityContribution(0.92f);
            }

            if (improvementState == LandAppreciationImprovementState.ImprovedShell || building != null)
            {
                return ApplyTownHallMaturityContribution(0.78f);
            }

            float siteAreaScore = Mathf.Clamp01(plot.siteSizeCells.x * plot.siteSizeCells.y / 64f);
            float frontageScore = Mathf.Clamp01(plot.frontageCells / 8f);
            float buildableScore = plot.bounds.IsValid && plot.frontageCells > 0 ? 1f : 0.25f;
            float readiness = Mathf.Clamp01(0.25f + siteAreaScore * 0.25f + frontageScore * 0.25f + buildableScore * 0.25f);
            return ApplyTownHallMaturityContribution(readiness);
        }

        private float ApplyTownHallMaturityContribution(float readiness01)
        {
            return Mathf.Clamp01(Mathf.Clamp01(readiness01) + GetTownHallContribution().TownMaturity01 * 0.5f);
        }

        private TownHallContribution GetTownHallContribution()
        {
            AutoWire();
            return civicFoundation != null ? civicFoundation.CurrentTownHallContribution : TownHallContribution.Zero;
        }

        private LandAppreciationImprovementState ResolveCurrentImprovementState(
            TownPlot plot,
            PlacedBuilding building,
            LandAppreciationImprovementState fallback)
        {
            PlacedBuilding resolvedBuilding = building;
            if (resolvedBuilding == null && plot != null && plot.buildingId >= 0)
            {
                resolvedBuilding = GetBuilding(plot.buildingId);
            }

            if (resolvedBuilding == null)
            {
                return (int)fallback > (int)LandAppreciationImprovementState.Empty ? fallback : LandAppreciationImprovementState.Empty;
            }

            if (IsOperatingBusinessAtBuilding(resolvedBuilding.id))
            {
                return LandAppreciationImprovementState.OperatingBusiness;
            }

            return fallback == LandAppreciationImprovementState.OperatingBusiness
                ? fallback
                : LandAppreciationImprovementState.ImprovedShell;
        }

        private bool IsOperatingBusinessAtBuilding(int buildingId)
        {
            if (buildingId < 0)
            {
                return false;
            }

            if (playerCashSource != null && playerCashSource.StoreBuildingId == buildingId && playerCashSource.CurrentBusiness != null)
            {
                return true;
            }

            return sharedBusinessRuntime != null && sharedBusinessRuntime.FindByBuildingId(buildingId) != null;
        }

        private int GetCurrentDayIndex()
        {
            AutoWire();
            return timeManager != null ? Mathf.Max(0, timeManager.CurrentAbsoluteDayIndex) : 0;
        }

        private int GetCurrentWeekKey()
        {
            AutoWire();
            return timeManager != null ? Mathf.Max(0, timeManager.CurrentWeek) : -1;
        }

        private static LandAppreciationImprovementState MaxImprovementState(
            LandAppreciationImprovementState left,
            LandAppreciationImprovementState right)
        {
            return (LandAppreciationImprovementState)Mathf.Max((int)left, (int)right);
        }

        private static LandAppreciationSaveDto CaptureLandAppreciationSaveDto(LandAppreciationState state)
        {
            return new LandAppreciationSaveDto
            {
                plotId = state.plotId,
                buildingId = state.buildingId,
                holdingKind = state.holdingKind,
                improvementState = state.improvementState,
                purchaseBasisCents = state.purchaseBasisCents,
                capitalizedImprovementCents = state.capitalizedImprovementCents,
                currentEstimatedValueCents = state.currentEstimatedValueCents,
                appreciationDeltaCents = state.appreciationDeltaCents,
                purchaseDayIndex = state.purchaseDayIndex,
                lastValuationDayIndex = state.lastValuationDayIndex,
                townGrowthPressure01 = state.townGrowthPressure01,
                developmentReadiness01 = state.developmentReadiness01
            };
        }

        private static LandAppreciationState CreateLandAppreciationState(LandAppreciationSaveDto dto)
        {
            if (dto == null)
            {
                return null;
            }

            return new LandAppreciationState
            {
                plotId = dto.plotId,
                buildingId = dto.buildingId,
                holdingKind = dto.holdingKind,
                improvementState = dto.improvementState,
                purchaseBasisCents = Mathf.Max(0, dto.purchaseBasisCents),
                capitalizedImprovementCents = Mathf.Max(0, dto.capitalizedImprovementCents),
                currentEstimatedValueCents = Mathf.Max(0, dto.currentEstimatedValueCents),
                appreciationDeltaCents = dto.appreciationDeltaCents,
                purchaseDayIndex = Mathf.Max(0, dto.purchaseDayIndex),
                lastValuationDayIndex = Mathf.Max(0, dto.lastValuationDayIndex),
                townGrowthPressure01 = Mathf.Clamp01(dto.townGrowthPressure01),
                developmentReadiness01 = Mathf.Clamp01(dto.developmentReadiness01)
            };
        }

        private void MergeMissingConstructionSupportDefinitions()
        {
            if (constructionSupportDefinitions == null)
            {
                return;
            }

            for (int i = 0; i < constructionSupportDefinitions.Length; i++)
            {
                ConstructionSupportNodeDefinition definition = constructionSupportDefinitions[i];
                if (definition == null || HasSupportNode(definition.NodeId))
                {
                    continue;
                }

                constructionSupportNodes.Add(ConstructionSupportNodeState.FromDefinition(definition));
            }
        }

        private bool HasSupportNode(string nodeId)
        {
            for (int i = 0; i < constructionSupportNodes.Count; i++)
            {
                if (constructionSupportNodes[i] != null
                    && string.Equals(constructionSupportNodes[i].NodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetConstructionSupportNode(ConstructionResourceKind resourceKind, out ConstructionSupportNodeState node)
        {
            EnsureConstructionSupportNodesLoaded();
            for (int i = 0; i < constructionSupportNodes.Count; i++)
            {
                ConstructionSupportNodeState candidate = constructionSupportNodes[i];
                if (candidate != null && candidate.Active && candidate.ResourceKind == resourceKind)
                {
                    node = candidate;
                    return true;
                }
            }

            node = null;
            return false;
        }

        private int GetAvailableSupportStock(ConstructionResourceKind resourceKind, out string sourceLabel)
        {
            if (TryGetConstructionSupportNode(resourceKind, out ConstructionSupportNodeState node) && node != null)
            {
                sourceLabel = node.DisplayName;
                return node.CurrentStockUnits;
            }

            sourceLabel = resourceKind == ConstructionResourceKind.Lumber ? "No local sawmill" : "No support node";
            return 0;
        }

        private int GetSupportNodeUnitCost(ConstructionResourceKind resourceKind)
        {
            if (TryGetConstructionSupportNode(resourceKind, out ConstructionSupportNodeState node) && node != null)
            {
                return node.UnitCostCents;
            }

            return resourceKind == ConstructionResourceKind.Lumber ? 85 : 0;
        }

        private int GetAvailableBlacksmithHardwareStock(out string sourceLabel)
        {
            return GetAvailableBlacksmithHardwareStock(out sourceLabel, out _, out _);
        }

        private int GetAvailableBlacksmithHardwareStock(
            out string sourceLabel,
            out BusinessInstanceState blacksmith,
            out CategoryStockState stock)
        {
            blacksmith = FindActiveBlacksmith();
            stock = null;
            if (blacksmith == null || blacksmith.RuntimeState == null)
            {
                sourceLabel = "active blacksmith supply unavailable";
                return 0;
            }

            stock = blacksmith.RuntimeState.GetCategoryStock(GetBlacksmithHardwareCategoryId());
            sourceLabel = $"active blacksmith supply ({blacksmith.RuntimeDisplayName})";
            return stock != null ? stock.CurrentStockUnits : 0;
        }

        private int GetRegionalHardwareFallbackUnitCostCents()
        {
            int localUnitCost = Mathf.Max(0, blacksmithHardwareUnitCostCents);
            return Mathf.Max(localUnitCost, Mathf.CeilToInt(localUnitCost * Mathf.Max(1f, regionalHardwareFallbackUnitCostMultiplier)));
        }

        private string GetRegionalHardwareFallbackSourceLabel()
        {
            return string.IsNullOrWhiteSpace(regionalHardwareFallbackSourceLabel)
                ? DefaultRegionalHardwareFallbackSourceLabel
                : regionalHardwareFallbackSourceLabel.Trim();
        }

        private string GetRegionalHardwareFallbackLeadTimeLabel()
        {
            return string.IsNullOrWhiteSpace(regionalHardwareFallbackLeadTimeLabel)
                ? DefaultRegionalHardwareFallbackLeadTimeLabel
                : regionalHardwareFallbackLeadTimeLabel.Trim();
        }

        private string BuildRegionalHardwareFallbackProcurementNote(int fallbackUnits)
        {
            int units = Mathf.Max(0, fallbackUnits);
            if (units <= 0)
            {
                return string.Empty;
            }

            return $"{GetRegionalHardwareFallbackSourceLabel()}: {units} unit(s), {GetRegionalHardwareFallbackLeadTimeLabel()}, higher landed cost than local blacksmith supply.";
        }

        private static int GetQuoteLineAllocatedCost(ConstructionResourceQuoteLine line)
        {
            if (line == null)
            {
                return 0;
            }

            if (line.HasSourceAllocations)
            {
                int total = 0;
                for (int i = 0; i < line.sourceAllocations.Count; i++)
                {
                    ConstructionResourceSourceAllocation allocation = line.sourceAllocations[i];
                    if (allocation != null)
                    {
                        total += allocation.TotalCostCents;
                    }
                }

                return Mathf.Max(0, total);
            }

            return Mathf.Max(0, line.requiredUnits) * Mathf.Max(0, line.unitCostCents);
        }

        private static int GetWeightedUnitCostCents(
            IReadOnlyList<ConstructionResourceSourceAllocation> allocations,
            int fallbackUnitCostCents)
        {
            if (allocations == null || allocations.Count == 0)
            {
                return Mathf.Max(0, fallbackUnitCostCents);
            }

            int units = 0;
            int cost = 0;
            for (int i = 0; i < allocations.Count; i++)
            {
                ConstructionResourceSourceAllocation allocation = allocations[i];
                if (allocation == null || allocation.allocatedUnits <= 0)
                {
                    continue;
                }

                units += allocation.allocatedUnits;
                cost += allocation.TotalCostCents;
            }

            return units > 0 ? Mathf.Max(1, Mathf.CeilToInt(cost / (float)units)) : Mathf.Max(0, fallbackUnitCostCents);
        }

        private BusinessInstanceState FindBusinessByInstanceId(string instanceId)
        {
            sharedBusinessRuntime?.InitializeIfNeeded(playerCashSource != null ? playerCashSource.CurrentBusiness : null);
            if (sharedBusinessRuntime == null || string.IsNullOrWhiteSpace(instanceId))
            {
                return null;
            }

            for (int i = 0; i < sharedBusinessRuntime.Businesses.Count; i++)
            {
                BusinessInstanceState business = sharedBusinessRuntime.Businesses[i];
                if (business != null && string.Equals(business.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    return business;
                }
            }

            return null;
        }

        private BusinessInstanceState FindBusinessByType(BusinessType businessType)
        {
            sharedBusinessRuntime?.InitializeIfNeeded(playerCashSource != null ? playerCashSource.CurrentBusiness : null);
            return sharedBusinessRuntime != null ? sharedBusinessRuntime.FindByType(businessType) : null;
        }

        private bool IsRegionalHardwareFallbackAllocation(ConstructionResourceSourceAllocation allocation)
        {
            return allocation != null
                && !allocation.HasBusinessSeller
                && string.Equals(allocation.sourceLabel, GetRegionalHardwareFallbackSourceLabel(), StringComparison.OrdinalIgnoreCase);
        }

        private static void RecordBlacksmithHardwareSale(BusinessInstanceState blacksmith, int units, int unitCostCents)
        {
            if (blacksmith == null || blacksmith.RuntimeState == null)
            {
                return;
            }

            int consumed = Mathf.Max(0, units);
            int hardwareRevenueCents = consumed * Mathf.Max(0, unitCostCents);
            if (hardwareRevenueCents > 0)
            {
                blacksmith.RuntimeState.AddWeeklyLocalTransferRevenue(
                    hardwareRevenueCents,
                    $"sold {consumed} Nails / Simple Hardware to construction for {FormatMoney(hardwareRevenueCents)}");
            }

            blacksmith.RefreshCapacityState();
        }

        private BusinessInstanceState FindActiveBlacksmith()
        {
            sharedBusinessRuntime?.InitializeIfNeeded(playerCashSource != null ? playerCashSource.CurrentBusiness : null);
            if (sharedBusinessRuntime == null)
            {
                return null;
            }

            for (int i = 0; i < sharedBusinessRuntime.Businesses.Count; i++)
            {
                BusinessInstanceState business = sharedBusinessRuntime.Businesses[i];
                if (business != null
                    && business.BusinessType == BusinessType.Blacksmith
                    && business.RuntimeState != null
                    && business.OperatingEfficiency01 > 0f)
                {
                    return business;
                }
            }

            return null;
        }

        private int GetAvailableLaborUnits(out string sourceLabel)
        {
            AutoWire();
            PopulationState population = populationManager != null ? populationManager.State : null;
            if (population == null || population.people == null)
            {
                sourceLabel = "No population labor pool";
                return 0;
            }

            int eligiblePeople = 0;
            for (int i = 0; i < population.people.Count; i++)
            {
                PersonState person = population.people[i];
                if (NewcomerSettlementEvaluator.IsAvailableForLabor(person, LaborAccessLevel.YoungWorker))
                {
                    eligiblePeople++;
                }
            }

            int poolUnits = eligiblePeople * Mathf.Max(1, laborUnitsPerEligibleWorker);
            int builderUnits = sharedBusinessRuntime != null ? sharedBusinessRuntime.GetBuilderLaborCapacityUnits() : 0;
            sourceLabel = builderUnits > 0
                ? $"Local labor pool ({eligiblePeople} eligible) + builder/carpenter crew"
                : $"Local labor pool ({eligiblePeople} eligible)";
            return poolUnits + builderUnits;
        }

        private string GetBlacksmithHardwareCategoryId()
        {
            return string.IsNullOrWhiteSpace(blacksmithHardwareCategoryId)
                ? DefaultBlacksmithHardwareCategoryId
                : blacksmithHardwareCategoryId;
        }

        private void Awake()
        {
            AutoWire();
        }

        private void Start()
        {
            SubscribeToTime();
        }

        private void OnEnable()
        {
            SubscribeToTime();
        }

        private void OnDisable()
        {
            UnsubscribeFromTime();
        }

        private void AutoWire()
        {
            townWorld ??= FindAnyObjectByType<TownWorldController>();
            playerCashSource ??= FindAnyObjectByType<GeneralStoreRuntimeManager>();
            playerPortfolio ??= FindAnyObjectByType<PlayerPortfolioManager>();
            playerDebtManager ??= FindAnyObjectByType<PlayerDebtManager>();
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
            populationManager ??= FindAnyObjectByType<PopulationManager>();
            timeManager ??= TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>();
            civicFoundation ??= FindAnyObjectByType<CivicFoundationManager>();
            opportunityPressureRuntime ??= FindAnyObjectByType<OpportunityPressureRuntimeManager>();
        }

        private void SubscribeToTime()
        {
            AutoWire();
            if (subscribedToTime || timeManager == null)
            {
                return;
            }

            timeManager.WeekChanged += OnWeekChanged;
            subscribedToTime = true;
        }

        private void UnsubscribeFromTime()
        {
            if (!subscribedToTime || timeManager == null)
            {
                subscribedToTime = false;
                return;
            }

            timeManager.WeekChanged -= OnWeekChanged;
            subscribedToTime = false;
        }

        private void OnWeekChanged(SimulationDateChangedContext context)
        {
            ResolveWeeklyConstructionSupportProduction();
            ResolveWeeklyConstructionQueue(out _);
            ResolveWeeklyTownActionPressure();
            RunWeeklyAcquisitionReview(GetCurrentDayIndex());
        }

        private void EnsureMarket()
        {
            if (!marketBuilt)
            {
                RebuildMarket();
            }
        }

        private void ResolveWeeklyTownActionPressure()
        {
            AutoWire();
            float retail = EvaluateRetailTownActionPressure();
            float housing = populationManager != null ? Mathf.Clamp01(populationManager.HousingPressure01) : 0f;
            float labor = populationManager != null && populationManager.State != null ? 1f - Mathf.Clamp01(populationManager.State.laborAbsorption01) : 0f;
            float service = EvaluateServiceTownActionPressure();
            float expansion = Mathf.Max(retail, housing, labor, service);

            retailPressureStreakWeeks = UpdatePressureStreak(retailPressureStreakWeeks, retail);
            housingPressureStreakWeeks = UpdatePressureStreak(housingPressureStreakWeeks, housing);
            laborPressureStreakWeeks = UpdatePressureStreak(laborPressureStreakWeeks, labor);
            servicePressureStreakWeeks = UpdatePressureStreak(servicePressureStreakWeeks, service);
            expansionPressureStreakWeeks = UpdatePressureStreak(expansionPressureStreakWeeks, expansion);

            List<string> actions = new();
            if (housingPressureStreakWeeks >= 1 && housing >= 0.55f)
            {
                developmentInventoryPressure += 2 + Mathf.RoundToInt(housing * 2f);
                actions.Add("housing pressure pushed new residential parcels toward market");
            }

            if (retailPressureStreakWeeks >= 2 && retail >= 0.45f)
            {
                developmentInventoryPressure++;
                actions.Add("retail shortfalls increased commercial deal flow");
            }

            if (servicePressureStreakWeeks >= 2 && service >= 0.45f)
            {
                developmentInventoryPressure++;
                actions.Add("service gaps increased startup interest");
            }

            ResolvePassiveDevelopmentInventory(true);
            lastTownActionSummary = actions.Count > 0
                ? "Town action: " + string.Join("; ", actions) + "."
                : "Town action: pressure checked, no new autonomous action.";
            marketStatus = lastTownActionSummary;
        }

        private void ResolvePassiveDevelopmentInventory(bool allowWorldExpansion)
        {
            if (townWorld == null || developmentInventoryPressure <= 0)
            {
                return;
            }

            int released = 0;
            IReadOnlyList<TownPlot> plots = townWorld.Plots;
            for (int i = 0; i < plots.Count && developmentInventoryPressure > 0 && released < 3; i++)
            {
                TownPlot plot = plots[(i + GetCurrentWeekKey()) % plots.Count];
                if (plot == null || plot.playerOwned || plot.buildingId >= 0 || plot.reservedForLandSale)
                {
                    continue;
                }

                if (plot.zone != PlotZone.Residential && plot.zone != PlotZone.MixedUse && plot.zone != PlotZone.Business)
                {
                    continue;
                }

                plot.reservedForLandSale = true;
                developmentInventoryPressure--;
                released++;
            }

            if (allowWorldExpansion && released == 0 && plots.Count > 0)
            {
                int seedPlotId = Mathf.Clamp(GetCurrentWeekKey() % plots.Count, 0, plots.Count - 1);
                if (townWorld.TrySurfaceExpansionPlotsAfterLandPurchase(seedPlotId, out int created) && created > 0)
                {
                    developmentInventoryPressure = Mathf.Max(0, developmentInventoryPressure - created);
                }
            }
        }

        private static int UpdatePressureStreak(int current, float pressure01)
        {
            return pressure01 >= 0.45f ? Mathf.Min(52, Mathf.Max(0, current) + 1) : 0;
        }

        private float EvaluateRetailTownActionPressure()
        {
            int pressuredSignals = 0;
            if (populationManager != null && populationManager.StrongerHousingNeedCount > 0)
            {
                pressuredSignals++;
            }

            if (sharedBusinessRuntime != null)
            {
                string service = sharedBusinessRuntime.BuildTownServicePressureHeadline();
                if (!string.IsNullOrWhiteSpace(service) && service.IndexOf("stable", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    pressuredSignals++;
                }
            }

            return Mathf.Clamp01(pressuredSignals / 2f);
        }

        private float EvaluateServiceTownActionPressure()
        {
            if (sharedBusinessRuntime == null)
            {
                return 0f;
            }

            string headline = sharedBusinessRuntime.BuildTownServicePressureHeadline();
            if (string.IsNullOrWhiteSpace(headline))
            {
                return 0f;
            }

            return headline.IndexOf("doctor", StringComparison.OrdinalIgnoreCase) >= 0
                || headline.IndexOf("gap", StringComparison.OrdinalIgnoreCase) >= 0
                || headline.IndexOf("pressure", StringComparison.OrdinalIgnoreCase) >= 0
                ? 0.65f
                : 0.15f;
        }

        private float EvaluateLandListingPressure(TownPlot plot)
        {
            float housing = populationManager != null ? populationManager.HousingPressure01 : 0f;
            float expansion = expansionPressureStreakWeeks > 0 ? Mathf.Clamp01(expansionPressureStreakWeeks / 4f) : 0f;
            float zoneFit = plot != null && (plot.zone == PlotZone.Residential || plot.zone == PlotZone.MixedUse) ? 0.18f : 0.08f;
            return Mathf.Clamp01(Mathf.Max(housing, expansion) + zoneFit);
        }

        private float EvaluateBusinessListingPressure(BusinessInstanceState business)
        {
            float efficiencyGap = business != null ? 1f - Mathf.Clamp01(business.OperatingEfficiency01) : 0.25f;
            float service = business != null && business.BusinessType == BusinessType.Doctor ? Mathf.Clamp01(servicePressureStreakWeeks / 3f) : 0f;
            float retail = Mathf.Clamp01(retailPressureStreakWeeks / 4f);
            return Mathf.Clamp01(Mathf.Max(efficiencyGap, service, retail));
        }

        private void ApplyOpportunityWindow(AcquisitionListing listing, AcquisitionListingSource source, float pressure01, string sourceReason)
        {
            if (listing == null || string.IsNullOrWhiteSpace(listing.listingId))
            {
                return;
            }

            AcquisitionOpportunityWindowState window = FindOrCreateOpportunityWindow(listing, pressure01, sourceReason);
            int currentDay = GetCurrentDayIndex();
            listing.firstSeenDayIndex = window.firstSeenDayIndex;
            listing.playerFirstUntilDayIndex = window.playerFirstUntilDayIndex;
            listing.ageDays = Mathf.Max(0, currentDay - Mathf.Max(0, window.firstSeenDayIndex));
            listing.pressure01 = Mathf.Max(Mathf.Clamp01(pressure01), Mathf.Clamp01(window.pressure01));
            listing.aiEligible = currentDay >= window.playerFirstUntilDayIndex;
            listing.source = source;
            listing.sourceReason = string.IsNullOrWhiteSpace(window.sourceReason) ? sourceReason ?? string.Empty : window.sourceReason;
            window.pressure01 = listing.pressure01;
            window.aiEligible = listing.aiEligible;
        }

        private AcquisitionOpportunityWindowState FindOrCreateOpportunityWindow(AcquisitionListing listing, float pressure01, string sourceReason)
        {
            for (int i = 0; i < opportunityWindows.Count; i++)
            {
                AcquisitionOpportunityWindowState window = opportunityWindows[i];
                if (window != null && string.Equals(window.listingId, listing.listingId, StringComparison.OrdinalIgnoreCase))
                {
                    return window;
                }
            }

            int currentDay = GetCurrentDayIndex();
            int windowDays = listing.kind == AcquisitionListingKind.Business ? 21 : 14;
            if (pressure01 >= 0.75f)
            {
                windowDays = Mathf.Max(7, windowDays - 5);
            }

            AcquisitionOpportunityWindowState created = new()
            {
                listingId = listing.listingId,
                kind = listing.kind,
                firstSeenDayIndex = currentDay,
                playerFirstUntilDayIndex = currentDay + windowDays,
                pressure01 = Mathf.Clamp01(pressure01),
                sourceReason = sourceReason ?? string.Empty,
                aiEligible = false
            };
            opportunityWindows.Add(created);
            return created;
        }

        private void TryApplyPressureHandoffForListing(AcquisitionListing listing)
        {
            if (listing == null || opportunityPressureRuntime == null)
            {
                return;
            }

            OpportunityPressureSnapshot snapshot = opportunityPressureRuntime.CurrentSnapshot;
            IReadOnlyList<OpportunityNotice> notices = snapshot != null ? snapshot.Notices : null;
            if (notices == null || notices.Count == 0)
            {
                return;
            }

            OpportunityNotice bestNotice = null;
            float bestScore = Mathf.Clamp01(listing.pressure01);
            for (int i = 0; i < notices.Count; i++)
            {
                OpportunityNotice notice = notices[i];
                if (notice == null || !IsAcquisitionRelevantNotice(listing, notice))
                {
                    continue;
                }

                if (bestNotice == null || notice.Score01 > bestScore)
                {
                    bestNotice = notice;
                    bestScore = notice.Score01;
                }
            }

            if (bestNotice == null)
            {
                return;
            }

            AcquisitionListingSource source = bestNotice.PressureType == OpportunityPressureType.Expansion
                ? AcquisitionListingSource.PressureRelease
                : AcquisitionListingSource.ServiceShortage;
            ApplyOpportunityWindow(listing, source, Mathf.Max(listing.pressure01, bestNotice.Score01), bestNotice.Title + ": " + bestNotice.Detail);
        }

        private static bool IsAcquisitionRelevantNotice(AcquisitionListing listing, OpportunityNotice notice)
        {
            if (listing == null || notice == null)
            {
                return false;
            }

            if (listing.kind == AcquisitionListingKind.Land)
            {
                return notice.PressureType == OpportunityPressureType.Housing
                    || notice.PressureType == OpportunityPressureType.Expansion
                    || notice.PressureType == OpportunityPressureType.Service;
            }

            string text = (notice.NoticeId + " " + notice.Title + " " + notice.Detail).ToLowerInvariant();
            switch (listing.businessType)
            {
                case BusinessType.LiveryFreight:
                    return text.Contains("freight") || text.Contains("hauling") || text.Contains("mine");
                case BusinessType.Sawmill:
                case BusinessType.LumberYard:
                    return text.Contains("construction") || text.Contains("lumber") || text.Contains("mine");
                case BusinessType.Builder:
                    return text.Contains("construction") || notice.PressureType == OpportunityPressureType.Expansion;
                case BusinessType.Mine:
                    return text.Contains("mine");
                case BusinessType.Blacksmith:
                    return text.Contains("hardware") || text.Contains("mine");
                case BusinessType.Doctor:
                    return text.Contains("doctor") || text.Contains("service") || text.Contains("medicine");
                case BusinessType.Butcher:
                case BusinessType.Bakery:
                case BusinessType.FuelDealer:
                case BusinessType.Tailor:
                    return notice.PressureType == OpportunityPressureType.Retail;
                default:
                    return notice.PressureType == OpportunityPressureType.Expansion || notice.PressureType == OpportunityPressureType.Service;
            }
        }

        private void BuildLandListings()
        {
            List<TownPlot> candidates = new();
            for (int i = 0; i < townWorld.Plots.Count; i++)
            {
                TownPlot plot = townWorld.Plots[i];
                if (plot == null || IsPlayerOwnedPlot(plot) || plot.buildingId >= 0)
                {
                    continue;
                }

                if (!plot.reservedForLandSale && candidates.Count >= maxLandListings)
                {
                    continue;
                }

                candidates.Add(plot);
            }

            candidates.Sort((left, right) => GetMarketSortKey(left.id).CompareTo(GetMarketSortKey(right.id)));
            int count = Mathf.Min(maxLandListings, candidates.Count);
            for (int i = 0; i < count; i++)
            {
                AcquisitionListing listing = CreateLandListing(candidates[i]);
                ApplyOpportunityWindow(listing, AcquisitionListingSource.InitialMarket, EvaluateLandListingPressure(candidates[i]), "Town lot released for private sale");
                TryApplyPressureHandoffForListing(listing);
                landListings.Add(listing);
            }
        }

        private void BuildBusinessListings()
        {
            List<BusinessInstanceState> businessCandidates = new();
            if (sharedBusinessRuntime != null)
            {
                IReadOnlyList<BusinessInstanceState> businesses = sharedBusinessRuntime.Businesses;
                for (int i = 0; i < businesses.Count; i++)
                {
                    BusinessInstanceState business = businesses[i];
                    if (!CanListBusinessInstance(business))
                    {
                        continue;
                    }

                    businessCandidates.Add(business);
                }
            }

            if (businessCandidates.Count > 0)
            {
                businessCandidates.Sort((left, right) => GetMarketSortKey(left.AssignedBuildingId + 1000).CompareTo(GetMarketSortKey(right.AssignedBuildingId + 1000)));
                int mappedCount = Mathf.Min(maxBusinessListings, businessCandidates.Count);
                for (int i = 0; i < mappedCount; i++)
                {
                    BusinessInstanceState business = businessCandidates[i];
                    PlacedBuilding building = GetBuilding(business.AssignedBuildingId);
                    if (building != null)
                    {
                        AcquisitionListing listing = CreateBusinessListing(building, business);
                        ApplyOpportunityWindow(listing, AcquisitionListingSource.OwnerTurnover, EvaluateBusinessListingPressure(business), listing.reason);
                        TryApplyPressureHandoffForListing(listing);
                        businessListings.Add(listing);
                    }
                }

                return;
            }
        }

        private bool CanListBusinessInstance(BusinessInstanceState business)
        {
            if (business == null || business.BusinessType == BusinessType.GeneralStore)
            {
                return false;
            }

            if (business.Owner != null && business.Owner.OwnerKind == BusinessOwnerKind.Player)
            {
                return false;
            }

            PlacedBuilding building = GetBuilding(business.AssignedBuildingId);
            if (building == null || building.definition == null || IsPlayerOwnedBusiness(building))
            {
                return false;
            }

            if (building.id == (playerCashSource != null ? playerCashSource.StoreBuildingId : -1))
            {
                return false;
            }

            return sharedBusinessRuntime == null || sharedBusinessRuntime.IsBusinessAssignmentValid(business);
        }

        private AcquisitionListing CreateLandListing(TownPlot plot)
        {
            int price = CalculateLandPriceCents(plot);
            return new AcquisitionListing
            {
                kind = AcquisitionListingKind.Land,
                listingId = $"land_{plot.id}",
                plotId = plot.id,
                buildingId = -1,
                title = $"Plot {plot.id:000} {plot.zone} parcel",
                reason = "Town lot released for private sale",
                askingPriceCents = price,
                plotZone = plot.zone,
                siteSizeCells = plot.siteSizeCells,
                frontageCells = plot.frontageCells,
                improved = false,
                playerOwned = IsPlayerOwnedPlot(plot),
                detail = $"Empty {plot.siteSizeCells.x}x{plot.siteSizeCells.y} plot with {plot.frontageCells} frontage cells on the {plot.roadFrontageDirection} road edge."
            };
        }

        private AcquisitionListing CreateBusinessListing(PlacedBuilding building, BusinessInstanceState business)
        {
            TownPlot plot = GetPlot(building.plotId);
            string reason = GetBusinessSaleReason(building.id);
            int price = CalculateBusinessPriceCents(building, plot, reason);
            string buildingDisplayName = building.definition != null ? building.definition.DisplayName : $"Building {building.id:000}";
            BusinessInstanceState mappedBusiness = business ?? sharedBusinessRuntime?.FindByBuildingId(building.id);
            string businessDisplayName = mappedBusiness != null ? mappedBusiness.RuntimeDisplayName : "Unassigned Holding";
            string businessTypeDisplayName = mappedBusiness != null
                ? BusinessRuntimeNaming.GetBusinessTypeDisplayName(mappedBusiness.BusinessType)
                : "Commercial Holding";
            string ownerDisplayName = mappedBusiness != null && mappedBusiness.Owner != null
                ? mappedBusiness.Owner.DisplayName
                : "No operator";
            string title = mappedBusiness != null ? businessDisplayName : $"Vacant {buildingDisplayName}";
            string detail = mappedBusiness != null
                ? $"Established {businessTypeDisplayName} operated by {ownerDisplayName}. Site: {buildingDisplayName}, Plot {building.plotId:000}. Footprint {building.footprint.width}x{building.footprint.depth}; site {building.siteSizeCells.x}x{building.siteSizeCells.y}."
                : $"Vacant commercial holding. Site: {buildingDisplayName}, Plot {building.plotId:000}. Footprint {building.footprint.width}x{building.footprint.depth}; site {building.siteSizeCells.x}x{building.siteSizeCells.y}.";
            return new AcquisitionListing
            {
                kind = AcquisitionListingKind.Business,
                listingId = $"business_{building.id}",
                plotId = building.plotId,
                buildingId = building.id,
                businessInstanceId = mappedBusiness != null ? mappedBusiness.InstanceId : string.Empty,
                businessType = mappedBusiness != null ? mappedBusiness.BusinessType : BusinessType.GeneralStore,
                businessDisplayName = businessDisplayName,
                businessTypeDisplayName = businessTypeDisplayName,
                ownerDisplayName = ownerDisplayName,
                buildingDisplayName = buildingDisplayName,
                title = title,
                reason = reason,
                askingPriceCents = price,
                plotZone = plot != null ? plot.zone : PlotZone.Business,
                siteSizeCells = building.siteSizeCells,
                frontageCells = plot != null ? plot.frontageCells : building.footprint.depth,
                improved = true,
                playerOwned = IsPlayerOwnedBusiness(building),
                detail = detail
            };
        }

        private bool ValidateListingStillAvailable(AcquisitionListing listing, out string message)
        {
            if (listing == null)
            {
                message = "No acquisition listing is selected.";
                return false;
            }

            if (listing.kind == AcquisitionListingKind.Business)
            {
                PlacedBuilding building = GetBuilding(listing.buildingId);
                if (listing.playerOwned || building == null || IsPlayerOwnedBusiness(building))
                {
                    message = building != null && IsPlayerOwnedBusiness(building)
                        ? "That business is already owned by the player."
                        : "That business listing is no longer available.";
                    return false;
                }

                message = string.Empty;
                return true;
            }

            TownPlot plot = GetPlot(listing.plotId);
            if (listing.playerOwned || plot == null || IsPlayerOwnedPlot(plot) || plot.buildingId >= 0)
            {
                message = plot != null && IsPlayerOwnedPlot(plot)
                    ? "That land is already owned by the player."
                    : "That land listing is no longer available.";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private bool StartInquiry(AcquisitionListing listing, out string message)
        {
            int currentDay = GetCurrentDayIndex();
            SellerProfile seller = BuildSellerProfile(listing);
            SellerPressureResult pressure = new SellerPressureEvaluator().Evaluate(seller);
            int earnest = CalculateEarnestMoneyCents(listing, pressure);
            int likelyFinancingNeed = Mathf.Max(0, listing.askingPriceCents - GetAvailableCashCents());
            string scoutingRead = BuildScoutingSummary(listing);
            AcquisitionBuyerSeriousness seriousness = EvaluateBuyerSeriousness(listing, likelyFinancingNeed, GetAvailableCashCents(), IsWatchedListing(listing.listingId));

            AcquisitionDealState deal = new AcquisitionDealState
            {
                stage = AcquisitionDealStage.Inquiry,
                kind = listing.kind,
                listingId = listing.listingId ?? string.Empty,
                plotId = listing.plotId,
                buildingId = listing.buildingId,
                title = listing.title ?? string.Empty,
                sellerMotive = seller.motive,
                sellerPressure01 = pressure.Pressure01,
                sellerRelationshipSensitivity01 = pressure.RelationshipSensitivity01,
                inquiryDayIndex = currentDay,
                seriousness = seriousness,
                scoutingSummary = scoutingRead,
                earnestMoneyCents = earnest,
                optionDeadlineDayIndex = currentDay + CalculateOptionWindowDays(listing, pressure),
                financingNeedCents = likelyFinancingNeed,
                closingRisk01 = Mathf.Clamp01(0.12f + pressure.RelationshipSensitivity01 * 0.18f + (likelyFinancingNeed > 0 ? 0.12f : 0f)),
                financingContingencyPresent = likelyFinancingNeed > 0,
                lastProgressDayIndex = currentDay,
                weeklyReviewCount = 0,
                stalledReviewCount = 0,
                proofRequired = ShouldRequireProofOfFunds(listing, seller, pressure),
                proofStatus = AcquisitionProofStatus.NotRequested,
                proofPresentedDayIndex = -1,
                requiredDiligenceMask = (int)ResolveRequiredDiligenceLayers(listing),
                completedDiligenceMask = 0,
                closingFailureCause = AcquisitionClosingFailureCause.None,
                closingFailureSummary = string.Empty,
                failedFromStage = AcquisitionDealStage.None
            };

            if (deal.proofRequired)
            {
                deal.proofStatus = AcquisitionProofStatus.Requested;
            }

            RefreshDealWorkflowReadouts(listing, deal);
            deal.statusText = $"Inquiry opened. Seller motive: {FormatSellerMotive(deal.sellerMotive)}; pressure {FormatPercent(deal.sellerPressure01)}; seriousness {FormatBuyerSeriousness(deal.seriousness)}. {deal.leadQualitySummary} {deal.proofOfFundsSummary} Earnest expected: {FormatMoney(earnest)}.";
            UpsertDeal(deal);
            marketStatus = deal.statusText;
            lastPurchaseSummary = deal.statusText;
            string inquiryNextStep = deal.proofRequired
                ? $"present proof of funds before earnest by day {deal.optionDeadlineDayIndex}"
                : $"commit earnest by day {deal.optionDeadlineDayIndex}";
            message = $"{deal.statusText} Next: {inquiryNextStep}.";
            Debug.Log($"[Acquisitions] {message}", this);
            return true;
        }

        private bool PresentProofOfFunds(AcquisitionListing listing, AcquisitionDealState deal, out string message)
        {
            if (listing == null || deal == null)
            {
                message = "Proof file is unavailable.";
                return false;
            }

            if (!deal.proofRequired)
            {
                deal.proofStatus = AcquisitionProofStatus.Verified;
                RefreshDealWorkflowReadouts(listing, deal);
                message = "Proof of funds is not required on this file.";
                return true;
            }

            AcquisitionProofStatus status = EvaluateProofStatus(listing, deal, out float proofRatio, out float requiredRatio, out bool lenderReady);
            deal.proofStatus = status;
            deal.proofPresentedDayIndex = status >= AcquisitionProofStatus.Presented ? GetCurrentDayIndex() : -1;
            RefreshDealWorkflowReadouts(listing, deal);
            if (status == AcquisitionProofStatus.Verified)
            {
                deal.statusText = $"Proof of funds accepted. Visible coverage {FormatPercent(proofRatio)} against file threshold {FormatPercent(requiredRatio)}.";
                marketStatus = deal.statusText;
                lastPurchaseSummary = deal.statusText;
                message = $"{deal.statusText} Next: commit earnest.";
                return true;
            }

            deal.statusText = lenderReady
                ? $"Proof shown but seller still wants a stronger file. Visible coverage {FormatPercent(proofRatio)} against threshold {FormatPercent(requiredRatio)}."
                : $"Proof blocked. Visible coverage {FormatPercent(proofRatio)} is below the seller threshold {FormatPercent(requiredRatio)}.";
            marketStatus = deal.statusText;
            lastPurchaseSummary = deal.statusText;
            message = deal.statusText;
            return false;
        }

        private bool CommitEarnest(AcquisitionListing listing, AcquisitionDealState deal, out string message)
        {
            if (IsDealExpired(deal))
            {
                ApplyDealFailure(deal, "Option window expired before earnest was committed.", true, out message);
                return false;
            }

            if (NeedsProofOfFunds(deal))
            {
                deal.statusText = "Earnest blocked: seller requires accepted proof of funds before taking earnest.";
                marketStatus = deal.statusText;
                lastPurchaseSummary = deal.statusText;
                message = deal.statusText;
                return false;
            }

            int earnest = Mathf.Clamp(deal.earnestMoneyCents <= 0 ? CalculateEarnestMoneyCents(listing, new SellerPressureEvaluator().Evaluate(BuildSellerProfile(listing))) : deal.earnestMoneyCents, 0, listing.askingPriceCents);
            if (!TrySpend(earnest, $"{listing.title} earnest money", out message))
            {
                deal.statusText = $"Earnest blocked: {message}";
                marketStatus = deal.statusText;
                lastPurchaseSummary = deal.statusText;
                return false;
            }

            deal.earnestMoneyCents = earnest;
            MarkDealProgress(deal, AcquisitionDealStage.EarnestCommitted, GetCurrentDayIndex());
            deal.seriousness = EscalateSeriousness(deal.seriousness, deal.financingNeedCents <= 0 ? AcquisitionBuyerSeriousness.Ready : AcquisitionBuyerSeriousness.Active);
            deal.financingNeedCents = Mathf.Max(0, listing.askingPriceCents - earnest - GetAvailableCashCents());
            deal.financingContingencyPresent = deal.financingNeedCents > 0;
            RefreshDealWorkflowReadouts(listing, deal);
            deal.statusText = $"Earnest committed: {FormatMoney(earnest)}. Option runs through day {deal.optionDeadlineDayIndex}. {deal.proofOfFundsSummary}";
            marketStatus = deal.statusText;
            lastPurchaseSummary = deal.statusText;
            message = $"{deal.statusText} Next: run diligence.";
            Debug.Log($"[Acquisitions] {message}", this);
            return true;
        }

        private bool RunDiligence(AcquisitionListing listing, AcquisitionDealState deal, out string message)
        {
            if (IsDealExpired(deal))
            {
                ApplyDealFailure(deal, "Option window expired before diligence finished.", true, out message);
                return false;
            }

            if (deal.requiredDiligenceMask == 0)
            {
                deal.requiredDiligenceMask = (int)ResolveRequiredDiligenceLayers(listing);
            }

            if (!IsFormalDiligenceComplete(deal))
            {
                if (!AdvanceFormalDiligenceLayer(listing, deal, out message))
                {
                    return false;
                }

                if (!IsFormalDiligenceComplete(deal))
                {
                    return true;
                }
            }

            if (!TryBuildParcelValuation(listing, out ParcelValuationResult valuation))
            {
                message = "Diligence unavailable: site valuation could not be built.";
                deal.statusText = message;
                marketStatus = message;
                lastPurchaseSummary = message;
                return false;
            }

            int price = listing.askingPriceCents;
            float valueRatio = valuation.EstimatedValueCents / (float)Mathf.Max(1, price);
            float overBandPenalty = price > valuation.ValuationBandHighCents
                ? Mathf.Clamp01((price - valuation.ValuationBandHighCents) / (float)Mathf.Max(1, price))
                : 0f;
            float diligenceScore = Mathf.Clamp01(valuation.Confidence01 * 0.42f + Mathf.Clamp01(valueRatio) * 0.38f + (1f - overBandPenalty) * 0.2f);

            MarkDealProgress(deal, AcquisitionDealStage.DiligenceComplete, GetCurrentDayIndex());
            deal.diligenceDayIndex = GetCurrentDayIndex();
            deal.estimatedValueCents = valuation.EstimatedValueCents;
            deal.valuationBandLowCents = valuation.ValuationBandLowCents;
            deal.valuationBandHighCents = valuation.ValuationBandHighCents;
            deal.diligenceScore01 = diligenceScore;
            deal.closingRisk01 = Mathf.Clamp01(deal.closingRisk01 + overBandPenalty * 0.28f + (1f - valuation.Confidence01) * 0.18f);
            deal.diligenceSummary = BuildDiligenceSummary(listing, valuation, diligenceScore);
            deal.integrationStance = RecommendIntegrationStance(listing, deal);
            deal.completedDiligenceMask = deal.requiredDiligenceMask;
            RefreshDealWorkflowReadouts(listing, deal);
            deal.statusText = $"{deal.diligenceSummary} {deal.diligenceLayerSummary} Recommended stance: {FormatIntegrationStance(deal.integrationStance)}.";
            marketStatus = deal.statusText;
            lastPurchaseSummary = deal.statusText;
            message = $"{deal.statusText} Next: agree terms.";
            Debug.Log($"[Acquisitions] {message}", this);
            return true;
        }

        private bool AgreeTentativeTerms(AcquisitionListing listing, AcquisitionDealState deal, out string message)
        {
            if (IsDealExpired(deal))
            {
                ApplyDealFailure(deal, "Option window expired before terms were agreed.", true, out message);
                return false;
            }

            if (deal.estimatedValueCents <= 0 && !RunDiligence(listing, deal, out message))
            {
                return false;
            }

            ParcelValuationResult valuation = BuildSavedValuationResult(deal);
            SellerProfile seller = BuildSellerProfile(listing, deal);
            BuyerOfferProfile buyer = BuildPlayerBuyerProfile(deal);
            AcquisitionOfferTerms terms = BuildOfferTerms(listing, deal);
            OfferEvaluationResult result = new OfferEvaluationEngine().Evaluate(valuation, seller, buyer, terms);

            if (result.Rejected)
            {
                AcquisitionDealStage failedFromStage = ResolveFailureSourceStage(deal);
                MarkDealProgress(deal, AcquisitionDealStage.Failed, GetCurrentDayIndex());
                deal.failedFromStage = failedFromStage == AcquisitionDealStage.None ? AcquisitionDealStage.DiligenceComplete : failedFromStage;
                deal.closingFailureCause = AcquisitionClosingFailureCause.AdverseDiligence;
                deal.closingFailureSummary = $"Offer {FormatMoney(terms.offerPriceCents)} did not clear seller requirements.";
                deal.statusText = $"Seller declined the tentative agreement. {deal.closingFailureSummary}";
                marketStatus = deal.statusText;
                lastPurchaseSummary = deal.statusText;
                message = deal.statusText;
                return false;
            }

            int agreedPrice = result.Countered ? result.CounterofferPriceCents : terms.offerPriceCents;
            MarkDealProgress(deal, AcquisitionDealStage.TentativeAgreement, GetCurrentDayIndex());
            deal.tentativeAgreementDayIndex = GetCurrentDayIndex();
            deal.tentativePriceCents = Mathf.Max(1, agreedPrice);
            deal.closingDeadlineDayIndex = Mathf.Min(
                deal.optionDeadlineDayIndex > 0 ? deal.optionDeadlineDayIndex : deal.tentativeAgreementDayIndex + 10,
                deal.tentativeAgreementDayIndex + 10);
            if (deal.closingDeadlineDayIndex <= deal.tentativeAgreementDayIndex)
            {
                deal.closingDeadlineDayIndex = deal.tentativeAgreementDayIndex + 3;
            }

            deal.financingNeedCents = Mathf.Max(0, deal.tentativePriceCents - deal.earnestMoneyCents - GetAvailableCashCents());
            deal.seriousness = EscalateSeriousness(deal.seriousness, deal.financingNeedCents <= 0 ? AcquisitionBuyerSeriousness.Ready : AcquisitionBuyerSeriousness.Active);
            deal.financingContingencyPresent = deal.financingNeedCents > 0;
            deal.closingRisk01 = Mathf.Clamp01(deal.closingRisk01 + (result.Countered ? 0.08f : 0f) + (deal.financingNeedCents > 0 ? 0.1f : 0f));
            RefreshDealWorkflowReadouts(listing, deal);
            deal.statusText = result.Countered
                ? $"Tentative agreement reached at seller counter {FormatMoney(deal.tentativePriceCents)}. Close by day {deal.closingDeadlineDayIndex}. {deal.closingRiskSummary}"
                : $"Tentative agreement reached at {FormatMoney(deal.tentativePriceCents)}. Close by day {deal.closingDeadlineDayIndex}. {deal.closingRiskSummary}";
            marketStatus = deal.statusText;
            lastPurchaseSummary = deal.statusText;
            message = $"{deal.statusText} Next: close.";
            Debug.Log($"[Acquisitions] {message}", this);
            return true;
        }

        private bool CloseAcquisition(AcquisitionListing listing, AcquisitionDealState deal, out string message)
        {
            if (GetCurrentDayIndex() > deal.closingDeadlineDayIndex)
            {
                ApplyDealFailure(deal, "Closing deadline missed.", true, out message);
                return false;
            }

            if (!ValidateListingStillAvailable(listing, out message))
            {
                ApplyDealFailure(deal, message, false, out message);
                return false;
            }

            int finalPrice = Mathf.Max(1, deal.tentativePriceCents > 0 ? deal.tentativePriceCents : listing.askingPriceCents);
            int finalDueCents = Mathf.Max(0, finalPrice - Mathf.Max(0, deal.earnestMoneyCents));
            if (TryResolveClosingFailure(listing, deal, out AcquisitionClosingFailureCause failureCause, out string failureSummary))
            {
                deal.closingFailureCause = failureCause;
                deal.closingFailureSummary = failureSummary;
                RefreshDealWorkflowReadouts(listing, deal);
                ApplyDealFailure(deal, failureSummary, failureCause == AcquisitionClosingFailureCause.LenderRetreat, out message);
                return false;
            }

            if (!TryEnsureClosingFunds(listing, deal, finalPrice, finalDueCents, out string fundingMessage))
            {
                message = fundingMessage;
                return false;
            }

            if (!TrySpend(finalDueCents, $"{listing.title} closing balance", out message))
            {
                deal.statusText = $"Closing blocked: {message}";
                marketStatus = deal.statusText;
                lastPurchaseSummary = deal.statusText;
                return false;
            }

            if (deal.integrationStance == AcquisitionIntegrationStance.None)
            {
                deal.integrationStance = RecommendIntegrationStance(listing, deal);
            }

            bool closed = listing.kind == AcquisitionListingKind.Business
                ? CompleteBusinessAcquisition(listing, deal, finalPrice, fundingMessage, out message)
                : CompleteLandAcquisition(listing, deal, finalPrice, fundingMessage, out message);

            if (!closed)
            {
                RefundSpend(finalDueCents, $"{listing.title} closing balance");
                return false;
            }

            MarkDealProgress(deal, AcquisitionDealStage.Closed, GetCurrentDayIndex());
            RemoveDeal(listing.listingId);
            marketStatus = message;
            lastPurchaseSummary = message;
            Debug.Log($"[Acquisitions] {message}", this);
            return true;
        }

        private bool TryResolveClosingFailure(
            AcquisitionListing listing,
            AcquisitionDealState deal,
            out AcquisitionClosingFailureCause cause,
            out string summary)
        {
            cause = AcquisitionClosingFailureCause.None;
            summary = string.Empty;
            if (deal == null)
            {
                return false;
            }

            int failureThreshold = Mathf.RoundToInt(Mathf.Clamp01(deal.closingRisk01 - 0.45f) * 35f);
            if (failureThreshold <= 0)
            {
                return false;
            }

            if (deal.closingRisk01 >= 0.95f)
            {
                cause = ResolvePrimaryClosingRiskCause(listing, deal);
                summary = BuildClosingFailureSummary(listing, deal, cause);
                return cause != AcquisitionClosingFailureCause.None;
            }

            int seed = (listing != null ? listing.plotId + 1 : 1) * 131
                + (listing != null ? listing.buildingId + 17 : 17) * 397
                + marketSeed
                + deal.tentativeAgreementDayIndex * 53;
            int roll = PositiveHash(seed) % 100;
            if (roll >= failureThreshold)
            {
                return false;
            }

            cause = ResolvePrimaryClosingRiskCause(listing, deal);
            summary = BuildClosingFailureSummary(listing, deal, cause);
            return true;
        }

        private bool TryEnsureClosingFunds(
            AcquisitionListing listing,
            AcquisitionDealState deal,
            int finalPriceCents,
            int finalDueCents,
            out string message)
        {
            int availableCash = GetAvailableCashCents();
            if (availableCash >= finalDueCents)
            {
                deal.financingNeedCents = 0;
                message = "Closing funded from owner cash.";
                return true;
            }

            int cashContribution = Mathf.Clamp(availableCash + Mathf.Max(0, deal.earnestMoneyCents), 0, finalPriceCents);
            int need = Mathf.Max(0, finalPriceCents - cashContribution);
            deal.financingNeedCents = need;
            if (playerDebtManager == null)
            {
                message = $"Closing blocked: need {FormatMoney(finalDueCents)}, have {FormatMoney(availableCash)}. No financing manager is available.";
                deal.statusText = message;
                marketStatus = message;
                lastPurchaseSummary = message;
                return false;
            }

            int collateralValue = Mathf.Max(deal.estimatedValueCents, finalPriceCents);
            LoanCollateralKind collateralKind = listing.kind == AcquisitionListingKind.Business
                ? LoanCollateralKind.OperatingBusiness
                : LoanCollateralKind.Land;
            string collateralId = listing.kind == AcquisitionListingKind.Business
                ? $"building_{listing.buildingId:000}"
                : $"plot_{listing.plotId:000}";

            if (playerDebtManager.TryActivateAcquisitionLoan(
                deal.listingId,
                finalPriceCents,
                cashContribution,
                collateralValue,
                collateralKind,
                collateralId,
                out int fundedPrincipalCents,
                out message))
            {
                deal.financingNeedCents = fundedPrincipalCents;
                string paymentWarning = playerDebtManager.ActiveDebtPaymentCents > 0
                    ? $" Next payment {FormatMoney(playerDebtManager.ActiveDebtPaymentCents)} comes from Owner Cash."
                    : " Payments come from Owner Cash.";
                string collateralWarning = collateralKind == LoanCollateralKind.OperatingBusiness
                    ? $" Collateral/default risk: pledged business {collateralId} can be recovered by the lender."
                    : $" Collateral/default risk: pledged land {collateralId} can be recovered by the lender.";
                message = $"Closing financed: {FormatMoney(fundedPrincipalCents)} acquisition loan approved.{paymentWarning}{collateralWarning}";
                return GetAvailableCashCents() >= finalDueCents;
            }

            ApplyDealFailure(deal, $"Financing declined: {message}", true, out message);
            return false;
        }

        private bool CompleteLandAcquisition(
            AcquisitionListing listing,
            AcquisitionDealState deal,
            int finalPriceCents,
            string fundingMessage,
            out string message)
        {
            TownPlot plot = GetPlot(listing.plotId);
            if (plot == null || IsPlayerOwnedPlot(plot) || plot.buildingId >= 0)
            {
                message = "That land listing is no longer available.";
                return false;
            }

            plot.playerOwned = true;
            if (!ownedPlotIds.Contains(plot.id))
            {
                ownedPlotIds.Add(plot.id);
            }

            RecordLandAppreciationPurchase(plot, null, finalPriceCents, LandAppreciationImprovementState.Empty);
            listing.playerOwned = true;
            RecordOwnershipAptitudeGain(OwnershipAptitudeSource.LandPurchased);
            developmentInventoryPressure += 1;
            RemoveLandListingForPlot(plot.id);
            int replenished = ReplenishLandListingsAfterPurchase(plot.id);
            RefreshOwnedBuildability();
            string replenishmentRead = replenished > 0 ? $" Town released {replenished} replacement parcel(s)." : string.Empty;
            string integrationBrief = BuildPostCloseIntegrationBrief(listing, deal, finalPriceCents);
            message = $"{listing.title} closed for {FormatMoney(finalPriceCents)}. Earnest credited: {FormatMoney(deal.earnestMoneyCents)}. {fundingMessage}{replenishmentRead} {integrationBrief}".Trim();
            return true;
        }

        private bool CompleteBusinessAcquisition(
            AcquisitionListing listing,
            AcquisitionDealState deal,
            int finalPriceCents,
            string fundingMessage,
            out string message)
        {
            PlacedBuilding building = GetBuilding(listing.buildingId);
            if (building == null || IsPlayerOwnedBusiness(building))
            {
                message = building != null && IsPlayerOwnedBusiness(building)
                    ? "That business is already owned by the player."
                    : "That business listing is no longer available.";
                return false;
            }

            building.playerOwned = true;
            if (!ownedBusinessBuildingIds.Contains(building.id))
            {
                ownedBusinessBuildingIds.Add(building.id);
            }

            TownPlot plot = GetPlot(building.plotId);
            if (plot != null)
            {
                plot.playerOwned = true;
                if (!ownedPlotIds.Contains(plot.id))
                {
                    ownedPlotIds.Add(plot.id);
                }
            }

            LandAppreciationImprovementState purchasedImprovementState = !string.IsNullOrWhiteSpace(listing.businessInstanceId)
                ? LandAppreciationImprovementState.OperatingBusiness
                : LandAppreciationImprovementState.ImprovedShell;
            RecordLandAppreciationPurchase(plot, building, finalPriceCents, purchasedImprovementState);
            listing.playerOwned = true;

            string acquiredLabel = !string.IsNullOrWhiteSpace(listing.businessDisplayName) ? listing.businessDisplayName : listing.title;
            if (sharedBusinessRuntime != null
                && sharedBusinessRuntime.TryTransferBusinessToPlayer(
                    building.id,
                    out BusinessInstanceState acquiredBusiness,
                    out string transferMessage,
                    false))
            {
                acquiredLabel = acquiredBusiness != null ? acquiredBusiness.RuntimeDisplayName : acquiredLabel;
                listing.businessInstanceId = acquiredBusiness != null ? acquiredBusiness.InstanceId : listing.businessInstanceId;
                listing.businessDisplayName = acquiredLabel;
                listing.ownerDisplayName = "Player";
                string openingCashMessage = ApplyPostAcquisitionOpeningCash(acquiredBusiness, deal != null ? deal.sellerMotive : GetSellerMotive(listing.reason));
                playerPortfolio?.RegisterCurrentBusinessCashCheckpoint(acquiredBusiness, GetCurrentWeekKey(), true);
                string integrationBrief = BuildPostCloseIntegrationBrief(listing, deal, finalPriceCents);
                message = $"{acquiredLabel} closed for {FormatMoney(finalPriceCents)}. Earnest credited: {FormatMoney(deal.earnestMoneyCents)}. {fundingMessage} {transferMessage} {openingCashMessage} {integrationBrief}".Trim();
            }
            else
            {
                string integrationBrief = BuildPostCloseIntegrationBrief(listing, deal, finalPriceCents);
                message = !string.IsNullOrWhiteSpace(listing.businessInstanceId)
                    ? $"{acquiredLabel} closed for {FormatMoney(finalPriceCents)}. Earnest credited: {FormatMoney(deal.earnestMoneyCents)}. {fundingMessage} Ownership recorded on the site. {integrationBrief}".Trim()
                    : $"{acquiredLabel} closed for {FormatMoney(finalPriceCents)}. Earnest credited: {FormatMoney(deal.earnestMoneyCents)}. {fundingMessage} Held as an improved site. {integrationBrief}".Trim();
            }

            RecordOwnershipAptitudeGain(OwnershipAptitudeSource.BusinessPurchased);
            RemoveBusinessListingForBuilding(building.id);
            RefreshOwnedBuildability();
            return true;
        }

        private string BuildPostCloseIntegrationBrief(AcquisitionListing listing, AcquisitionDealState deal, int finalPriceCents)
        {
            if (listing == null)
            {
                return "Post-close handoff: return to the portfolio view and verify the transfer.";
            }

            AcquisitionIntegrationStance stance = deal != null && deal.integrationStance != AcquisitionIntegrationStance.None
                ? deal.integrationStance
                : RecommendIntegrationStance(listing, deal);
            string stanceText = stance == AcquisitionIntegrationStance.None ? "review first" : FormatIntegrationStance(stance);
            string firstOrder = BuildPostCloseFirstOrder(listing, stance);
            string watch = BuildPostCloseWatchItem(listing, deal, finalPriceCents);
            return $"Post-close handoff: {stanceText}. First order: {firstOrder} Watch: {watch}";
        }

        private static string BuildPostCloseFirstOrder(AcquisitionListing listing, AcquisitionIntegrationStance stance)
        {
            if (listing == null)
            {
                return "verify ownership and cash exposure.";
            }

            if (listing.kind == AcquisitionListingKind.Land)
            {
                return "confirm practical use, frontage, access, and conversion cost before committing project money.";
            }

            return stance switch
            {
                AcquisitionIntegrationStance.Stabilize => "protect operating cash, retain useful staff, and restore basic stock or service coverage before growth.",
                AcquisitionIntegrationStance.Repair => "schedule condition and presentation work before expecting the old reputation drag to clear.",
                AcquisitionIntegrationStance.Reposition => "review pricing, demand fit, supplier ties, and public presentation before pushing volume.",
                AcquisitionIntegrationStance.Expand => "confirm staffing, supply, storage, and freight capacity before adding new load.",
                _ => "verify books, staffing, stock, condition, and supplier continuity before changing policy."
            };
        }

        private string BuildPostCloseWatchItem(AcquisitionListing listing, AcquisitionDealState deal, int finalPriceCents)
        {
            if (listing == null)
            {
                return "transfer state.";
            }

            if (listing.kind == AcquisitionListingKind.Land)
            {
                return "idle carrying cost and whether the parcel should support a project, resale, or later assembly.";
            }

            if (deal != null && deal.closingRisk01 >= 0.55f)
            {
                return $"closing-risk residue ({FormatPercent(deal.closingRisk01)}) and any inherited supplier, title, or labor weakness.";
            }

            SellerMotive motive = deal != null ? deal.sellerMotive : GetSellerMotive(listing.reason);
            return motive switch
            {
                SellerMotive.FinancialDistress => "thin working capital and unpaid-problem baggage from the prior owner.",
                SellerMotive.DebtPressure => "thin working capital and unpaid-problem baggage from the prior owner.",
                SellerMotive.Liquidating => "missing stock, tool gaps, and whether customer trust survived the selloff.",
                SellerMotive.EstateSale => "unclear books, acting staff, and delayed supplier confidence after transfer.",
                SellerMotive.Retirement => "operator knowledge leaving the business and whether staff can cover the old owner's role.",
                SellerMotive.OwnerOperatorAttachment => "operator knowledge leaving the business and whether staff can cover the old owner's role.",
                SellerMotive.Relocation => "supplier continuity and whether local customers read the change as stable.",
                _ => finalPriceCents > 0 && deal != null && deal.estimatedValueCents > 0 && finalPriceCents > deal.estimatedValueCents
                    ? "overpay risk and the speed needed to turn the site into real earning power."
                    : "early stock discipline, staffing coverage, and customer confidence during the first review cycle."
            };
        }

        private string ApplyPostAcquisitionOpeningCash(BusinessInstanceState business, SellerMotive motive)
        {
            if (business == null || business.RuntimeState == null)
            {
                return "Opening operating cash could not be adjusted because the business runtime is unavailable.";
            }

            BusinessRuntimeState runtime = business.RuntimeState;
            int previousCash = runtime.CurrentCashCents;
            int payroll = runtime.FilledWeeklyPayrollCents;
            int survivalReserve = SharedBusinessRuntimeManager.CalculateSharedSurvivalCashReserveCents(business);
            int openingCash = CalculatePostAcquisitionOpeningCashCents(previousCash, payroll, survivalReserve, motive);
            runtime.SetCurrentCashCents(openingCash, false);
            string motiveText = FormatSellerMotive(motive);
            return $"Opening operating cash set to {FormatMoney(openingCash)} from {motiveText} sale condition; seller cash of {FormatMoney(previousCash)} was not acquired.";
        }

        private static int CalculatePostAcquisitionOpeningCashCents(
            int previousCashCents,
            int weeklyPayrollCents,
            int survivalReserveCents,
            SellerMotive motive)
        {
            int previous = Mathf.Max(0, previousCashCents);
            int payroll = Mathf.Max(0, weeklyPayrollCents);
            int reserve = Mathf.Max(0, survivalReserveCents);

            int target = motive switch
            {
                SellerMotive.FinancialDistress => Mathf.Clamp(payroll / 4, 500, 2500),
                SellerMotive.DebtPressure => Mathf.Clamp(payroll / 3, 500, 3500),
                SellerMotive.Liquidating => Mathf.Clamp(payroll / 2, 1000, 5000),
                SellerMotive.Retirement => Mathf.Max(5000, Mathf.Min(reserve, Mathf.Max(payroll, reserve / 2))),
                SellerMotive.EstateSale => Mathf.Max(4000, Mathf.Min(reserve, Mathf.Max(payroll, reserve / 3))),
                SellerMotive.Relocation => Mathf.Max(4000, Mathf.Min(reserve, Mathf.Max(payroll, reserve / 3))),
                _ => Mathf.Max(2500, Mathf.Min(reserve, Mathf.Max(payroll, reserve / 4)))
            };

            return Mathf.Min(previous, Mathf.Max(0, target));
        }

        private bool TryPurchaseLand(out string message)
        {
            if (landListings.Count == 0)
            {
                message = "No land listings are available.";
                return false;
            }

            AcquisitionListing listing = landListings[Mathf.Clamp(selectedLandIndex, 0, landListings.Count - 1)];
            TownPlot plot = GetPlot(listing.plotId);
            if (listing.playerOwned || plot == null || IsPlayerOwnedPlot(plot) || plot.buildingId >= 0)
            {
                message = plot != null && IsPlayerOwnedPlot(plot)
                    ? "That land is already owned by the player."
                    : "That land listing is no longer available.";
                RebuildMarket();
                return false;
            }

            if (!TrySpend(listing.askingPriceCents, listing.title, out message))
            {
                return false;
            }

            plot.playerOwned = true;
            if (!ownedPlotIds.Contains(plot.id))
            {
                ownedPlotIds.Add(plot.id);
            }

            RecordLandAppreciationPurchase(plot, null, listing.askingPriceCents, LandAppreciationImprovementState.Empty);
            listing.playerOwned = true;
            lastPurchaseSummary = $"{listing.title} purchased for {FormatMoney(listing.askingPriceCents)}.";
            RecordOwnershipAptitudeGain(OwnershipAptitudeSource.LandPurchased);
            developmentInventoryPressure += 1;
            landListings.RemoveAt(selectedLandIndex);
            int replenished = ReplenishLandListingsAfterPurchase(plot.id);
            if (replenished > 0)
            {
                lastPurchaseSummary += $" Town released {replenished} replacement parcel(s).";
            }

            selectedLandIndex = WrapIndex(selectedLandIndex, landListings.Count);
            RefreshOwnedBuildability();
            Debug.Log($"[Acquisitions] {lastPurchaseSummary}", this);
            return true;
        }

        private int ReplenishLandListingsAfterPurchase(int purchasedPlotId)
        {
            int created = 0;
            if (townWorld != null)
            {
                townWorld.TrySurfaceExpansionPlotsAfterLandPurchase(purchasedPlotId, out created);
            }

            landListings.Clear();
            BuildLandListings();
            selectedLandIndex = WrapIndex(selectedLandIndex, landListings.Count);
            return Mathf.Max(0, created);
        }

        private bool TryPurchaseBusiness(out string message)
        {
            if (businessListings.Count == 0)
            {
                message = "No business listings are available.";
                return false;
            }

            AcquisitionListing listing = businessListings[Mathf.Clamp(selectedBusinessIndex, 0, businessListings.Count - 1)];
            PlacedBuilding building = GetBuilding(listing.buildingId);
            if (listing.playerOwned || building == null || IsPlayerOwnedBusiness(building))
            {
                message = building != null && IsPlayerOwnedBusiness(building)
                    ? "That business is already owned by the player."
                    : "That business listing is no longer available.";
                RebuildMarket();
                return false;
            }

            if (!TrySpend(listing.askingPriceCents, listing.title, out message))
            {
                return false;
            }

            building.playerOwned = true;
            if (!ownedBusinessBuildingIds.Contains(building.id))
            {
                ownedBusinessBuildingIds.Add(building.id);
            }

            TownPlot plot = GetPlot(building.plotId);
            if (plot != null)
            {
                plot.playerOwned = true;
                if (!ownedPlotIds.Contains(plot.id))
                {
                    ownedPlotIds.Add(plot.id);
                }
            }

            LandAppreciationImprovementState purchasedImprovementState = !string.IsNullOrWhiteSpace(listing.businessInstanceId)
                ? LandAppreciationImprovementState.OperatingBusiness
                : LandAppreciationImprovementState.ImprovedShell;
            RecordLandAppreciationPurchase(plot, building, listing.askingPriceCents, purchasedImprovementState);
            listing.playerOwned = true;
            string acquiredLabel = !string.IsNullOrWhiteSpace(listing.businessDisplayName) ? listing.businessDisplayName : listing.title;
            if (sharedBusinessRuntime != null
                && sharedBusinessRuntime.TryTransferBusinessToPlayer(
                    building.id,
                    out BusinessInstanceState acquiredBusiness,
                    out string transferMessage,
                    false))
            {
                acquiredLabel = acquiredBusiness != null ? acquiredBusiness.RuntimeDisplayName : acquiredLabel;
                listing.businessInstanceId = acquiredBusiness != null ? acquiredBusiness.InstanceId : listing.businessInstanceId;
                listing.businessDisplayName = acquiredLabel;
                listing.ownerDisplayName = "Player";
                string openingCashMessage = ApplyPostAcquisitionOpeningCash(acquiredBusiness, GetSellerMotive(listing.reason));
                playerPortfolio?.RegisterCurrentBusinessCashCheckpoint(acquiredBusiness, GetCurrentWeekKey(), true);
                lastPurchaseSummary = $"{acquiredLabel} purchased for {FormatMoney(listing.askingPriceCents)}. {transferMessage} {openingCashMessage}";
            }
            else
            {
                lastPurchaseSummary = !string.IsNullOrWhiteSpace(listing.businessInstanceId)
                    ? $"{acquiredLabel} purchased for {FormatMoney(listing.askingPriceCents)}. Ownership recorded on the site."
                    : $"{acquiredLabel} purchased for {FormatMoney(listing.askingPriceCents)}. Held as an improved site until operator assignment and business activation are implemented.";
            }

            RecordOwnershipAptitudeGain(OwnershipAptitudeSource.BusinessPurchased);
            businessListings.RemoveAt(selectedBusinessIndex);
            selectedBusinessIndex = WrapIndex(selectedBusinessIndex, businessListings.Count);
            RefreshOwnedBuildability();
            message = lastPurchaseSummary;
            Debug.Log($"[Acquisitions] {lastPurchaseSummary}", this);
            return true;
        }

        private bool TrySpend(int cents, string label, out string message)
        {
            if (playerPortfolio == null)
            {
                message = "No owner cash source is wired.";
                return false;
            }

            return playerPortfolio.TrySpendOwnerCash(cents, label, out message);
        }

        private void RefundSpend(int cents, string label)
        {
            if (playerPortfolio == null || cents <= 0)
            {
                return;
            }

            playerPortfolio.RefundOwnerCash(cents, label);
            marketStatus = $"Refunded {FormatMoney(cents)} after failed {label}.";
        }

        private AcquisitionDealState FindDeal(string listingId)
        {
            if (string.IsNullOrWhiteSpace(listingId))
            {
                return null;
            }

            for (int i = 0; i < activeDeals.Count; i++)
            {
                AcquisitionDealState deal = activeDeals[i];
                if (deal != null && string.Equals(deal.listingId, listingId, StringComparison.OrdinalIgnoreCase))
                {
                    return deal;
                }
            }

            return null;
        }

        private AcquisitionDealState EnsureLeadNoteDeal(AcquisitionListing listing)
        {
            AcquisitionDealState deal = listing != null ? FindDeal(listing.listingId) : null;
            if (deal != null)
            {
                return deal;
            }

            int currentDay = GetCurrentDayIndex();
            int financingNeed = Mathf.Max(0, (listing != null ? listing.askingPriceCents : 0) - GetAvailableCashCents());
            deal = new AcquisitionDealState
            {
                stage = AcquisitionDealStage.None,
                kind = listing != null ? listing.kind : AcquisitionListingKind.Land,
                listingId = listing != null ? listing.listingId ?? string.Empty : string.Empty,
                plotId = listing != null ? listing.plotId : -1,
                buildingId = listing != null ? listing.buildingId : -1,
                title = listing != null ? listing.title ?? string.Empty : string.Empty,
                inquiryDayIndex = -1,
                seriousness = EvaluateBuyerSeriousness(listing, financingNeed, GetAvailableCashCents(), listing != null && IsWatchedListing(listing.listingId)),
                scoutingSummary = listing != null ? BuildScoutingSummary(listing) : string.Empty,
                financingNeedCents = financingNeed,
                financingContingencyPresent = financingNeed > 0,
                lastProgressDayIndex = currentDay,
                statusText = "Lead note opened from scouting."
            };
            UpsertDeal(deal);
            return deal;
        }

        private string BuildQuickDiligenceLedgerLine(AcquisitionListing listing, AcquisitionDealState deal, AcquisitionDiligenceClueKind kind)
        {
            if (listing == null)
            {
                return "No useful field note could be taken.";
            }

            int seed = PositiveHash((listing.plotId + 31) * 43 + (listing.buildingId + 47) * 19 + (int)kind * 97 + marketSeed);
            if (listing.kind == AcquisitionListingKind.Business)
            {
                return BuildBusinessQuickDiligenceLine(listing, seed, kind);
            }

            return BuildLandQuickDiligenceLine(listing, seed, kind);
        }

        private string BuildBusinessQuickDiligenceLine(AcquisitionListing listing, int seed, AcquisitionDiligenceClueKind kind)
        {
            BusinessInstanceState business = sharedBusinessRuntime != null ? sharedBusinessRuntime.FindByBuildingId(listing.buildingId) : null;
            switch (kind)
            {
                case AcquisitionDiligenceClueKind.VisibleCondition:
                    return (seed % 3) switch
                    {
                        0 => "Visible condition: storefront and yard read serviceable, with ordinary wear.",
                        1 => "Visible condition: deferred repair is visible around the work areas.",
                        _ => "Visible condition: public face is tidy, but rear service condition is uncertain."
                    };
                case AcquisitionDiligenceClueKind.Staffing:
                    if (business != null)
                    {
                        return $"Staffing clue: {business.RuntimeDisplayName} appears staffed enough to trade, but role depth still needs formal review.";
                    }
                    return (seed % 2) == 0
                        ? "Staffing clue: the counter is covered, but peak-hour help looks thin."
                        : "Staffing clue: work rhythm suggests an owner-operator is carrying too much of the load.";
                case AcquisitionDiligenceClueKind.SupplierWeakness:
                    return (seed % 3) switch
                    {
                        0 => "Supplier weakness: stock movement suggests a reliable but narrow supply line.",
                        1 => "Supplier weakness: visible stock gaps point to freight or local sourcing pressure.",
                        _ => "Supplier weakness: supply looks adequate today, but resilience is not proven."
                    };
                case AcquisitionDiligenceClueKind.DemandFit:
                    return !string.IsNullOrWhiteSpace(listing.detail)
                        ? $"Demand-fit clue: {listing.detail}"
                        : "Demand-fit clue: location and traffic look plausible, but seasonal demand still needs confirmation.";
                case AcquisitionDiligenceClueKind.RoughValue:
                    return $"Rough value clue: asking price is {FormatMoney(listing.askingPriceCents)}; a formal diligence pass should verify the earning base.";
                case AcquisitionDiligenceClueKind.Reliability:
                    return (seed % 3) switch
                    {
                        0 => "Reliability clue: trade cadence looks steady enough for a cautious process.",
                        1 => "Reliability clue: operating rhythm looks uneven; insist on stronger terms.",
                        _ => "Reliability clue: the public floor looks orderly, but back-room dependability is still unknown."
                    };
                default:
                    return "Field note: no specific clue was recorded.";
            }
        }

        private string BuildLandQuickDiligenceLine(AcquisitionListing listing, int seed, AcquisitionDiligenceClueKind kind)
        {
            switch (kind)
            {
                case AcquisitionDiligenceClueKind.VisibleCondition:
                    return listing.improved
                        ? "Visible condition: improved ground needs title and structure checks before commitment."
                        : (seed % 2) == 0
                            ? "Visible condition: ground reads practical with no obvious obstruction."
                            : "Visible condition: ground is usable, but prep work may eat into the first project budget.";
                case AcquisitionDiligenceClueKind.Staffing:
                    return "Staffing clue: no direct staff transfers with bare land; future use will need separate hiring capacity.";
                case AcquisitionDiligenceClueKind.SupplierWeakness:
                    return "Supplier weakness: construction inputs, not deed transfer, are the likely bottleneck.";
                case AcquisitionDiligenceClueKind.DemandFit:
                    return $"Demand-fit clue: {listing.plotZone} frontage and {listing.siteSizeCells.x}x{listing.siteSizeCells.y} site size should be matched to a specific project before earnest money.";
                case AcquisitionDiligenceClueKind.RoughValue:
                    if (TryBuildParcelValuation(listing, out ParcelValuationResult valuation))
                    {
                        return $"Rough value clue: desk range reads {FormatMoney(valuation.ValuationBandLowCents)}-{FormatMoney(valuation.ValuationBandHighCents)} before formal diligence.";
                    }
                    return $"Rough value clue: asking price is {FormatMoney(listing.askingPriceCents)}; valuation needs a cleaner site read.";
                case AcquisitionDiligenceClueKind.Reliability:
                    return (seed % 3) switch
                    {
                        0 => "Reliability clue: access and frontage look dependable for routine development.",
                        1 => "Reliability clue: site use looks sensitive to project choice and input timing.",
                        _ => "Reliability clue: parcel reads promising, but utility and service pressure remain open questions."
                    };
                default:
                    return "Field note: no specific clue was recorded.";
            }
        }

        private static string AppendLedgerLine(string ledger, string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return ledger ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(ledger))
            {
                return line;
            }

            return ledger.TrimEnd() + "\n" + line;
        }

        private void UpsertDeal(AcquisitionDealState deal)
        {
            if (deal == null || string.IsNullOrWhiteSpace(deal.listingId))
            {
                return;
            }

            for (int i = 0; i < activeDeals.Count; i++)
            {
                if (activeDeals[i] != null && string.Equals(activeDeals[i].listingId, deal.listingId, StringComparison.OrdinalIgnoreCase))
                {
                    activeDeals[i] = deal;
                    return;
                }
            }

            activeDeals.Add(deal);
        }

        private void RemoveDeal(string listingId)
        {
            if (string.IsNullOrWhiteSpace(listingId))
            {
                return;
            }

            for (int i = activeDeals.Count - 1; i >= 0; i--)
            {
                if (activeDeals[i] != null && string.Equals(activeDeals[i].listingId, listingId, StringComparison.OrdinalIgnoreCase))
                {
                    activeDeals.RemoveAt(i);
                }
            }
        }

        private string RunWeeklyAcquisitionReview(int currentDay)
        {
            EnsureMarket();
            string rivalAction = ResolveWeeklyRivalOpportunityAction(currentDay);
            int liveProcessCount = CountActiveLiveDeals();
            if (liveProcessCount <= 0)
            {
                int leadNotes = CountLeadNoteOnlyDeals();
                lastAcquisitionReviewSummary = !string.IsNullOrWhiteSpace(rivalAction)
                    ? $"Weekly review: {rivalAction}"
                    : leadNotes > 0
                    ? $"Weekly review: {leadNotes} scouting file(s) have notes, but no live deal needs action."
                    : watchedListingIds.Count > 0
                    ? $"Weekly review: {watchedListingIds.Count} watched lead(s), but no live deal needs action."
                    : "Weekly review: no live acquisition process requires action.";
                return lastAcquisitionReviewSummary;
            }

            int live = 0;
            int urgent = 0;
            int financingLed = 0;
            int failedThisReview = 0;
            int stalled = 0;
            int ready = 0;
            int cooling = 0;
            List<string> reviewNotes = new();

            for (int i = 0; i < activeDeals.Count; i++)
            {
                AcquisitionDealState deal = activeDeals[i];
                if (!IsLiveAcquisitionProcess(deal))
                {
                    continue;
                }

                AcquisitionListing listing = FindListingById(deal.listingId);
                if (listing == null)
                {
                    continue;
                }

                deal.weeklyReviewCount++;
                RefreshDealFinancialRead(listing, deal, currentDay);

                if (IsDealExpired(deal, currentDay))
                {
                    ApplyDealFailureAtDay(deal, "Option window expired during weekly review.", true, currentDay, out string failureMessage);
                    failedThisReview++;
                    reviewNotes.Add(GetListingDisplayName(listing) + " cooled after option pressure.");
                    continue;
                }

                if (HasClosingDeadlineMissed(deal, currentDay))
                {
                    ApplyDealFailureAtDay(deal, "Closing deadline missed during weekly review.", true, currentDay, out string failureMessage);
                    failedThisReview++;
                    reviewNotes.Add(GetListingDisplayName(listing) + " slipped past closing timing.");
                    continue;
                }

                UpdateWeeklyDealPressure(listing, deal, currentDay);
                if (EvaluateWeeklyDealStall(listing, deal, currentDay, reviewNotes, out string stallFailure))
                {
                    if (!string.IsNullOrWhiteSpace(stallFailure))
                    {
                        failedThisReview++;
                        continue;
                    }
                }

                ApplyWeeklySeriousnessPressure(listing, deal, currentDay);
                deal.statusText = BuildWeeklyDealStatus(listing, deal, currentDay);

                live++;
                stalled += Mathf.Max(0, deal.stalledReviewCount);
                if (deal.seriousness >= AcquisitionBuyerSeriousness.Ready)
                {
                    ready++;
                }
                else if (deal.seriousness <= AcquisitionBuyerSeriousness.Exploring)
                {
                    cooling++;
                }
                if (deal.financingNeedCents > 0)
                {
                    financingLed++;
                }

                if (IsDealUrgent(deal, currentDay))
                {
                    urgent++;
                }
            }

            if (!string.IsNullOrWhiteSpace(rivalAction))
            {
                reviewNotes.Add(rivalAction);
            }

            string noteText = reviewNotes.Count > 0 ? string.Join(" ", reviewNotes) : string.Empty;
            lastAcquisitionReviewSummary = live <= 0
                ? (failedThisReview > 0
                    ? $"Weekly review: no live deal remains after {failedThisReview} failure(s). {noteText}".Trim()
                    : "Weekly review: no live acquisition process requires action.")
                : $"Weekly review: {live} live | {urgent} urgent | {ready} ready | {cooling} cooling | {financingLed} financing-led | {failedThisReview} failed this week | {stalled} stalled review mark(s). {noteText}".Trim();

            marketStatus = lastAcquisitionReviewSummary;
            lastPurchaseSummary = lastAcquisitionReviewSummary;
            return lastAcquisitionReviewSummary;
        }

        private string ResolveWeeklyRivalOpportunityAction(int currentDay)
        {
            List<AcquisitionListing> eligibleListings = new();
            AddAiEligibleListings(landListings, currentDay, eligibleListings);
            AddAiEligibleListings(businessListings, currentDay, eligibleListings);
            if (eligibleListings.Count == 0)
            {
                return string.Empty;
            }

            RivalOwnerRuntimeState owner = BuildWeeklyRivalOwnerState();
            RivalOwnerTraits traits = new()
            {
                aggression01 = Mathf.Clamp01(0.45f + Mathf.Max(expansionPressureStreakWeeks, housingPressureStreakWeeks) * 0.05f),
                decisionQuality01 = 0.58f,
                riskTolerance01 = 0.52f,
                liquidityDiscipline01 = 0.55f,
                patience01 = 0.45f,
                expansionBias01 = Mathf.Clamp01(0.45f + expansionPressureStreakWeeks * 0.06f)
            };
            RivalOwnerScaleSnapshot scale = new(
                owner.ownerId,
                owner.capitalCents,
                0,
                owner.ownedBusinessIds.Count,
                owner.ownedPlotIds.Count,
                0.45f,
                0.35f,
                0.35f,
                0.42f,
                Mathf.Clamp01(Mathf.Max(expansionPressureStreakWeeks, housingPressureStreakWeeks) / 5f),
                RivalImportanceTier.LocalCompetitor);

            List<RivalOpportunitySignal> signals = new();
            for (int i = 0; i < eligibleListings.Count; i++)
            {
                signals.Add(BuildRivalSignal(eligibleListings[i]));
            }

            IReadOnlyList<RivalDecisionProposal> proposals = new RivalOwnerDecisionEvaluator().Evaluate(owner, traits, scale, signals);
            for (int i = 0; i < proposals.Count; i++)
            {
                RivalDecisionProposal proposal = proposals[i];
                if (proposal.Action != RivalDecisionAction.Bid)
                {
                    continue;
                }

                AcquisitionListing listing = FindListingById(proposal.Opportunity.opportunityId);
                if (listing == null)
                {
                    continue;
                }

                if (listing.kind == AcquisitionListingKind.Land && ApplyNpcLandPurchase(listing, owner, out string landMessage))
                {
                    return landMessage;
                }

                if (listing.kind == AcquisitionListingKind.Business && ApplyNpcBusinessPurchase(listing, owner, out string businessMessage))
                {
                    return businessMessage;
                }
            }

            return string.Empty;
        }

        private void AddAiEligibleListings(IReadOnlyList<AcquisitionListing> source, int currentDay, List<AcquisitionListing> target)
        {
            if (source == null || target == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                AcquisitionListing listing = source[i];
                if (listing == null)
                {
                    continue;
                }

                listing.aiEligible = currentDay >= listing.playerFirstUntilDayIndex;
                if (listing.aiEligible && !HasActiveDealForListing(listing.listingId))
                {
                    target.Add(listing);
                }
            }
        }

        private RivalOwnerRuntimeState BuildWeeklyRivalOwnerState()
        {
            return new RivalOwnerRuntimeState
            {
                ownerId = "phase3_local_rival",
                capitalCents = 45000 + Mathf.Max(housingPressureStreakWeeks, expansionPressureStreakWeeks) * 3500,
                reservedCapitalCents = 9000,
                reputation01 = 0.52f,
                localTrust01 = 0.5f,
                priorDealReliability01 = 0.56f,
                communityFit01 = 0.48f
            };
        }

        private RivalOpportunitySignal BuildRivalSignal(AcquisitionListing listing)
        {
            float fit = listing.kind == AcquisitionListingKind.Land
                ? listing.plotZone == PlotZone.Residential || listing.plotZone == PlotZone.MixedUse ? 0.72f : 0.55f
                : 0.68f;
            float urgency = Mathf.Clamp01(listing.ageDays / 28f + listing.pressure01 * 0.35f);
            return new RivalOpportunitySignal
            {
                opportunityId = listing.listingId,
                kind = listing.kind == AcquisitionListingKind.Land ? RivalOpportunityKind.Land : RivalOpportunityKind.BusinessAcquisition,
                categoryId = listing.kind == AcquisitionListingKind.Land ? listing.plotZone.ToString() : listing.businessType.ToString(),
                estimatedValueCents = Mathf.RoundToInt(listing.askingPriceCents * Mathf.Lerp(1.02f, 1.18f, listing.pressure01)),
                askingPriceCents = listing.askingPriceCents,
                marketPressure01 = listing.pressure01,
                strategicFit01 = fit,
                urgency01 = urgency,
                score01 = Mathf.Clamp01(fit * 0.42f + urgency * 0.3f + listing.pressure01 * 0.28f)
            };
        }

        private bool ApplyNpcLandPurchase(AcquisitionListing listing, RivalOwnerRuntimeState owner, out string message)
        {
            message = string.Empty;
            TownPlot plot = GetPlot(listing.plotId);
            if (plot == null || plot.playerOwned || plot.buildingId >= 0)
            {
                return false;
            }

            plot.reservedForLandSale = false;
            RemoveLandListingForPlot(plot.id);
            developmentInventoryPressure = Mathf.Max(0, developmentInventoryPressure - 1);
            message = $"Rival move: {GetRivalDisplayName(owner)} bought {listing.title} after the player-first window closed.";
            lastTownActionSummary = message;
            marketStatus = message;
            return true;
        }

        private bool ApplyNpcBusinessPurchase(AcquisitionListing listing, RivalOwnerRuntimeState owner, out string message)
        {
            message = string.Empty;
            PlacedBuilding building = GetBuilding(listing.buildingId);
            if (building == null || building.playerOwned)
            {
                return false;
            }

            if (sharedBusinessRuntime != null
                && sharedBusinessRuntime.TryTransferBusinessToTown(building.id, GetRivalDisplayName(owner), out _, out string transferMessage))
            {
                RemoveBusinessListingForBuilding(building.id);
                message = $"Rival move: {GetRivalDisplayName(owner)} took over {listing.title} after the player-first window closed. {transferMessage}";
                lastTownActionSummary = message;
                marketStatus = message;
                return true;
            }

            return false;
        }

        private static string GetRivalDisplayName(RivalOwnerRuntimeState owner)
        {
            return owner != null && !string.IsNullOrWhiteSpace(owner.ownerId) ? "Mercer interests" : "A local rival";
        }

        private bool HasActiveDealForListing(string listingId)
        {
            if (string.IsNullOrWhiteSpace(listingId))
            {
                return false;
            }

            for (int i = 0; i < activeDeals.Count; i++)
            {
                AcquisitionDealState deal = activeDeals[i];
                if (IsLiveAcquisitionProcess(deal)
                    && string.Equals(deal.listingId, listingId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshDealFinancialRead(AcquisitionListing listing, AcquisitionDealState deal, int currentDay)
        {
            if (listing == null || deal == null)
            {
                return;
            }

            int priceCents = deal.tentativePriceCents > 0 ? deal.tentativePriceCents : Mathf.Max(0, listing.askingPriceCents);
            int earnestCents = Mathf.Max(0, deal.earnestMoneyCents);
            deal.financingNeedCents = Mathf.Max(0, priceCents - earnestCents - GetAvailableCashCents());
            deal.financingContingencyPresent = deal.financingNeedCents > 0;

            AcquisitionBuyerSeriousness target = EvaluateBuyerSeriousness(
                listing,
                deal.financingNeedCents,
                GetAvailableCashCents(),
                IsWatchedListing(listing.listingId));

            if (deal.stage == AcquisitionDealStage.EarnestCommitted || deal.stage == AcquisitionDealStage.DiligenceComplete || deal.stage == AcquisitionDealStage.TentativeAgreement)
            {
                target = deal.financingNeedCents <= 0
                    ? AcquisitionBuyerSeriousness.Ready
                    : AcquisitionBuyerSeriousness.Active;
            }

            if (DaysUntilDeadline(deal, currentDay) <= 2 && target < AcquisitionBuyerSeriousness.Active)
            {
                target = AcquisitionBuyerSeriousness.Active;
            }

            deal.seriousness = EscalateSeriousness(deal.seriousness, target);
        }

        private void UpdateWeeklyDealPressure(AcquisitionListing listing, AcquisitionDealState deal, int currentDay)
        {
            if (listing == null || deal == null)
            {
                return;
            }

            int daysLeft = DaysUntilDeadline(deal, currentDay);
            if (daysLeft <= 2)
            {
                deal.sellerPressure01 = Mathf.Clamp01(deal.sellerPressure01 + 0.08f);
                deal.closingRisk01 = Mathf.Clamp01(deal.closingRisk01 + 0.05f);
            }
            else if (daysLeft <= 5)
            {
                deal.sellerPressure01 = Mathf.Clamp01(deal.sellerPressure01 + 0.03f);
                deal.closingRisk01 = Mathf.Clamp01(deal.closingRisk01 + 0.02f);
            }
            else
            {
                deal.sellerPressure01 = Mathf.Clamp01(deal.sellerPressure01 + 0.01f);
            }

            if (deal.financingNeedCents > 0)
            {
                deal.closingRisk01 = Mathf.Clamp01(deal.closingRisk01 + 0.02f);
            }

            if (deal.stage == AcquisitionDealStage.DiligenceComplete && deal.integrationStance == AcquisitionIntegrationStance.None)
            {
                deal.integrationStance = RecommendIntegrationStance(listing, deal);
            }
        }

        private void ApplyWeeklySeriousnessPressure(AcquisitionListing listing, AcquisitionDealState deal, int currentDay)
        {
            if (listing == null || deal == null)
            {
                return;
            }

            int weeklyBurden = EstimateProjectedWeeklyBurdenCents(listing, deal);
            int activeDeals = CountActiveLiveDeals();
            int stalledDeals = CountStalledLiveDeals();
            int riskScore = playerPortfolio != null
                ? playerPortfolio.EvaluateCommitmentRiskScore(
                    deal.tentativePriceCents > 0 ? deal.tentativePriceCents : Mathf.Max(0, listing.askingPriceCents),
                    Mathf.Max(0, deal.earnestMoneyCents),
                    weeklyBurden,
                    activeDeals,
                    stalledDeals)
                : 1;
            int executionScore = playerPortfolio != null
                ? playerPortfolio.EvaluateExecutionLoadScore(activeDeals, stalledDeals)
                : 0;

            bool urgent = IsDealUrgent(deal, currentDay);
            bool advancedStage = deal.stage == AcquisitionDealStage.EarnestCommitted
                || deal.stage == AcquisitionDealStage.DiligenceComplete
                || deal.stage == AcquisitionDealStage.TentativeAgreement;

            if (advancedStage && deal.financingNeedCents <= 0 && urgent && riskScore <= 1 && executionScore <= 1)
            {
                deal.seriousness = EscalateSeriousness(deal.seriousness, AcquisitionBuyerSeriousness.Ready);
                return;
            }

            if (deal.stalledReviewCount <= 0 || urgent)
            {
                return;
            }

            if (deal.stage == AcquisitionDealStage.Inquiry && (riskScore >= 2 || executionScore >= 2))
            {
                deal.seriousness = RelaxSeriousness(deal.seriousness, AcquisitionBuyerSeriousness.Watching);
                return;
            }

            if (advancedStage && (riskScore >= 4 || executionScore >= 3 || (deal.financingNeedCents > 0 && deal.stalledReviewCount >= 2)))
            {
                deal.seriousness = RelaxSeriousness(deal.seriousness, AcquisitionBuyerSeriousness.Active);
            }
        }

        private string BuildWeeklyDealStatus(AcquisitionListing listing, AcquisitionDealState deal, int currentDay)
        {
            string nextStep = BuildSelectedNextStepSummary(listing, deal);
            string timing = BuildTimingPressureSummary(deal, currentDay);
            string ownerRead = BuildSelectedOwnerReadSummary(listing, deal);
            string runway = BuildSelectedRunwayPressureSummary(listing, deal);
            string execution = BuildSelectedExecutionLoadSummary(listing, deal);
            string seriousness = deal != null ? $"Seriousness {FormatBuyerSeriousness(deal.seriousness)}." : string.Empty;
            string stall = deal != null && deal.stalledReviewCount > 0 ? $" Stall pressure {deal.stalledReviewCount}." : string.Empty;
            return $"Weekly review - {GetListingDisplayName(listing)} | {FormatDealStage(deal.stage)} | {seriousness} {ownerRead} | {runway} | {execution} | {timing}{stall} Next: {nextStep}.";
        }

        private void MarkDealProgress(AcquisitionDealState deal, AcquisitionDealStage newStage, int currentDay)
        {
            if (deal == null)
            {
                return;
            }

            deal.stage = newStage;
            deal.lastProgressDayIndex = currentDay;
            deal.stalledReviewCount = 0;
            if (newStage != AcquisitionDealStage.Failed)
            {
                deal.closingFailureCause = AcquisitionClosingFailureCause.None;
                deal.closingFailureSummary = string.Empty;
                deal.failedFromStage = AcquisitionDealStage.None;
            }
        }

        private bool EvaluateWeeklyDealStall(AcquisitionListing listing, AcquisitionDealState deal, int currentDay, List<string> reviewNotes, out string failureMessage)
        {
            failureMessage = string.Empty;
            if (listing == null || deal == null)
            {
                return false;
            }

            int lastProgressDay = deal.lastProgressDayIndex >= 0 ? deal.lastProgressDayIndex : Mathf.Max(0, deal.inquiryDayIndex);
            int stalledDays = Mathf.Max(0, currentDay - lastProgressDay);
            if (stalledDays < 7)
            {
                return false;
            }

            deal.stalledReviewCount++;
            deal.sellerPressure01 = Mathf.Clamp01(deal.sellerPressure01 + 0.04f);
            deal.closingRisk01 = Mathf.Clamp01(deal.closingRisk01 + 0.03f);
            reviewNotes?.Add(GetListingDisplayName(listing) + $" stalled for about {stalledDays} day(s).");

            int weeklyBurden = EstimateProjectedWeeklyBurdenCents(listing, deal);
            int activeDeals = CountActiveLiveDeals();
            int stalledDeals = CountStalledLiveDeals();
            int riskScore = playerPortfolio != null
                ? playerPortfolio.EvaluateCommitmentRiskScore(
                    deal.tentativePriceCents > 0 ? deal.tentativePriceCents : Mathf.Max(0, listing.askingPriceCents),
                    Mathf.Max(0, deal.earnestMoneyCents),
                    weeklyBurden,
                    activeDeals,
                    stalledDeals)
                : 1;
            int executionScore = playerPortfolio != null
                ? playerPortfolio.EvaluateExecutionLoadScore(activeDeals, stalledDeals)
                : 0;

            bool shouldFail = false;
            string reason = string.Empty;
            if (deal.stage == AcquisitionDealStage.Inquiry && deal.stalledReviewCount >= 2 && deal.seriousness <= AcquisitionBuyerSeriousness.Exploring)
            {
                shouldFail = true;
                reason = "Seller lost patience with a passive inquiry during weekly review.";
            }
            else if (deal.stage == AcquisitionDealStage.Inquiry && deal.stalledReviewCount >= 1 && (riskScore >= 3 || executionScore >= 2) && deal.seriousness < AcquisitionBuyerSeriousness.Active)
            {
                shouldFail = true;
                reason = "Seller cooled as a thinly funded passive lead stalled under weekly review.";
            }
            else if ((deal.stage == AcquisitionDealStage.EarnestCommitted || deal.stage == AcquisitionDealStage.DiligenceComplete)
                && deal.stalledReviewCount >= 2
                && deal.financingNeedCents > 0
                && deal.seriousness < AcquisitionBuyerSeriousness.Ready)
            {
                shouldFail = true;
                reason = "Seller cooled while financing and readiness lagged during weekly review.";
            }
            else if ((deal.stage == AcquisitionDealStage.EarnestCommitted || deal.stage == AcquisitionDealStage.DiligenceComplete || deal.stage == AcquisitionDealStage.TentativeAgreement)
                && deal.stalledReviewCount >= 1
                && (riskScore >= 3 || executionScore >= 3)
                && deal.financingNeedCents > 0)
            {
                shouldFail = true;
                reason = "Seller lost confidence as the deal stayed stalled under a strained commitment posture.";
            }

            if (!shouldFail)
            {
                return true;
            }

            ApplyDealFailureAtDay(deal, reason, deal.financingNeedCents > 0, currentDay, out failureMessage);
            return true;
        }

        private int DaysUntilDeadline(AcquisitionDealState deal, int currentDay)
        {
            if (deal == null)
            {
                return int.MaxValue;
            }

            if (deal.stage == AcquisitionDealStage.TentativeAgreement && deal.closingDeadlineDayIndex >= 0)
            {
                return deal.closingDeadlineDayIndex - currentDay;
            }

            if (deal.optionDeadlineDayIndex >= 0)
            {
                return deal.optionDeadlineDayIndex - currentDay;
            }

            return int.MaxValue;
        }

        private bool IsDealUrgent(AcquisitionDealState deal, int currentDay)
        {
            return deal != null && DaysUntilDeadline(deal, currentDay) <= 2;
        }

        private string BuildTimingPressureSummary(AcquisitionDealState deal, int currentDay)
        {
            if (deal == null)
            {
                return "Timing: no live clock.";
            }

            if (deal.stage == AcquisitionDealStage.Failed)
            {
                return "Timing: file is no longer on a live clock.";
            }

            if (deal.stage == AcquisitionDealStage.TentativeAgreement && deal.closingDeadlineDayIndex > 0)
            {
                int days = deal.closingDeadlineDayIndex - currentDay;
                if (days <= 0)
                {
                    return "Timing: closing window is expiring now.";
                }

                if (days <= 2)
                {
                    return $"Timing: close within about {days} day(s).";
                }

                return $"Timing: close window runs about {days} more day(s).";
            }

            if (deal.optionDeadlineDayIndex > 0)
            {
                int days = deal.optionDeadlineDayIndex - currentDay;
                if (days <= 0)
                {
                    return "Timing: option window is expiring now.";
                }

                if (days <= 2)
                {
                    return $"Timing: commit within about {days} day(s).";
                }

                return $"Timing: option window runs about {days} more day(s).";
            }

            return "Timing: no live deadline yet.";
        }

        private bool IsDealExpired(AcquisitionDealState deal, int currentDay)
        {
            return deal != null
                && deal.optionDeadlineDayIndex >= 0
                && currentDay > deal.optionDeadlineDayIndex;
        }

        private bool HasClosingDeadlineMissed(AcquisitionDealState deal, int currentDay)
        {
            return deal != null
                && deal.stage == AcquisitionDealStage.TentativeAgreement
                && deal.closingDeadlineDayIndex >= 0
                && currentDay > deal.closingDeadlineDayIndex;
        }

        private void ApplyDealFailureAtDay(AcquisitionDealState deal, string reason, bool financingFailure, int currentDay, out string message)
        {
            if (deal == null)
            {
                message = reason;
                return;
            }

            FinancingFailureResult result = new FinancingFailureEvaluator().EvaluateFailure(new FinancingFailureRequest
            {
                tentativeAgreementId = deal.listingId,
                offeredPriceCents = deal.tentativePriceCents,
                earnestMoneyCents = deal.earnestMoneyCents,
                hasTentativeAgreement = deal.stage == AcquisitionDealStage.TentativeAgreement,
                financingContingencyPresent = financingFailure && deal.financingContingencyPresent,
                closingDeadlineMissed = deal.closingDeadlineDayIndex >= 0 && currentDay > deal.closingDeadlineDayIndex,
                sellerPressure01 = deal.sellerPressure01,
                sellerRelationshipSensitivity01 = deal.sellerRelationshipSensitivity01,
                applicantReputation01 = GetOwnerHeadlineReputation01(),
                lenderTrust01 = playerDebtManager != null ? playerDebtManager.LenderTrust01 : 0.55f,
                failureReason = reason ?? string.Empty,
                daysUntilClosingDeadline = deal.closingDeadlineDayIndex >= 0 ? deal.closingDeadlineDayIndex - currentDay : 0
            });

            int forfeited = Mathf.Clamp(result.ForfeitedEarnestMoneyCents, 0, Mathf.Max(0, deal.earnestMoneyCents));
            int refundable = Mathf.Max(0, deal.earnestMoneyCents - forfeited);
            if (refundable > 0)
            {
                RefundSpend(refundable, $"{deal.title} protected earnest refund");
            }

            if (financingFailure)
            {
                playerDebtManager?.RecordAcquisitionFinancingFailure(result);
            }

            AcquisitionDealStage failedFromStage = ResolveFailureSourceStage(deal);
            MarkDealProgress(deal, AcquisitionDealStage.Failed, currentDay);
            deal.failedFromStage = failedFromStage;
            if (string.IsNullOrWhiteSpace(deal.closingFailureSummary))
            {
                deal.closingFailureSummary = reason ?? string.Empty;
            }
            if (deal.closingFailureCause == AcquisitionClosingFailureCause.None && financingFailure)
            {
                deal.closingFailureCause = AcquisitionClosingFailureCause.LenderRetreat;
            }
            deal.statusText = $"{reason} {result.Summary} Earnest forfeited: {FormatMoney(forfeited)}. Cooldown: {result.CooldownDays} days.";
            marketStatus = deal.statusText;
            lastPurchaseSummary = deal.statusText;
            message = deal.statusText;
            Debug.Log($"[Acquisitions] {message}", this);
        }

        private bool IsDealExpired(AcquisitionDealState deal)
        {
            return IsDealExpired(deal, GetCurrentDayIndex());
        }

        private void ApplyDealFailure(AcquisitionDealState deal, string reason, bool financingFailure, out string message)
        {
            ApplyDealFailureAtDay(deal, reason, financingFailure, GetCurrentDayIndex(), out message);
        }

        private static AcquisitionDealStage ResolveFailureSourceStage(AcquisitionDealState deal)
        {
            if (deal == null)
            {
                return AcquisitionDealStage.None;
            }

            return deal.stage switch
            {
                AcquisitionDealStage.Inquiry => AcquisitionDealStage.Inquiry,
                AcquisitionDealStage.EarnestCommitted => AcquisitionDealStage.EarnestCommitted,
                AcquisitionDealStage.DiligenceComplete => AcquisitionDealStage.DiligenceComplete,
                AcquisitionDealStage.TentativeAgreement => AcquisitionDealStage.TentativeAgreement,
                _ => deal.tentativeAgreementDayIndex >= 0
                    ? AcquisitionDealStage.TentativeAgreement
                    : deal.diligenceDayIndex >= 0
                        ? AcquisitionDealStage.DiligenceComplete
                        : AcquisitionDealStage.Inquiry
            };
        }

        private static string BuildFailureBreakpointLabel(AcquisitionDealStage failedFromStage)
        {
            return failedFromStage switch
            {
                AcquisitionDealStage.Inquiry => "inquiry",
                AcquisitionDealStage.EarnestCommitted => "diligence",
                AcquisitionDealStage.DiligenceComplete => "terms",
                AcquisitionDealStage.TentativeAgreement => "closing",
                _ => "the live file"
            };
        }

        private static string BuildFailureChecklistSummary(AcquisitionDealState deal)
        {
            if (deal == null)
            {
                return "Checklist: review why the lead failed before reopening similar deals.";
            }

            string breakpoint = BuildFailureBreakpointLabel(deal.failedFromStage);
            string action = IsFailedDealReadyToReopen(deal)
                ? " Reopen only if funding, deadline, seller posture, or diligence concerns now read materially better."
                : " Record a failure review before reopening the file.";
            return string.IsNullOrWhiteSpace(deal.closingFailureSummary)
                ? $"Checklist: review why the {breakpoint} file failed before reopening similar deals.{action}"
                : $"Checklist: review the {breakpoint} failure - {deal.closingFailureSummary}.{action}";
        }

        private static string BuildFailedLeadNextStepSummary(AcquisitionDealState deal)
        {
            string breakpoint = BuildFailureBreakpointLabel(deal != null ? deal.failedFromStage : AcquisitionDealStage.None);
            if (IsFailedDealReadyToReopen(deal))
            {
                return $"reopen inquiry only if the {breakpoint} failure has materially changed";
            }

            return deal != null && !string.IsNullOrWhiteSpace(deal.closingFailureSummary)
                ? $"review the {breakpoint} failure before reopening it"
                : $"review why the {breakpoint} file failed before reopening it";
        }

        private static bool IsFailedDealReadyToReopen(AcquisitionDealState deal)
        {
            return deal != null && deal.stage == AcquisitionDealStage.Failed && deal.stalledReviewCount > 0;
        }

        private static string BuildDealStageReadout(AcquisitionDealState deal)
        {
            if (deal == null)
            {
                return "No active process";
            }

            if (deal.stage != AcquisitionDealStage.Failed)
            {
                return FormatDealStage(deal.stage);
            }

            return $"Failed during {BuildFailureBreakpointLabel(deal.failedFromStage)}";
        }

        private SellerProfile BuildSellerProfile(AcquisitionListing listing)
        {
            return BuildSellerProfile(listing, null);
        }

        private SellerProfile BuildSellerProfile(AcquisitionListing listing, AcquisitionDealState deal)
        {
            SellerMotive motive = listing != null && listing.kind == AcquisitionListingKind.Business
                ? GetSellerMotive(listing.reason)
                : SellerMotive.Liquidating;
            float pressure = GetSellerPressure(motive);
            if (deal != null && deal.sellerPressure01 > 0f)
            {
                pressure = deal.sellerPressure01;
                motive = deal.sellerMotive;
            }

            float askToValueRatio = deal != null && deal.estimatedValueCents > 0 && listing != null
                ? listing.askingPriceCents / (float)Mathf.Max(1, deal.estimatedValueCents)
                : 1f;

            return new SellerProfile
            {
                sellerId = listing != null ? $"seller_{listing.listingId}" : "seller",
                motive = motive,
                pressure01 = pressure,
                urgency01 = Mathf.Clamp01(pressure * 0.75f),
                debtPressure01 = motive == SellerMotive.DebtPressure || motive == SellerMotive.FinancialDistress ? pressure : 0.15f,
                cashNeed01 = motive == SellerMotive.FinancialDistress || motive == SellerMotive.Liquidating ? Mathf.Clamp01(pressure + 0.12f) : pressure * 0.55f,
                attachment01 = motive == SellerMotive.OwnerOperatorAttachment || motive == SellerMotive.StrategicHoldout ? 0.78f : 0.28f,
                relocationNeed01 = motive == SellerMotive.Relocation ? 0.82f : 0.1f,
                reputationSensitivity01 = motive == SellerMotive.Retirement || motive == SellerMotive.OwnerOperatorAttachment ? 0.72f : 0.4f,
                buyerPreferenceSensitivity01 = motive == SellerMotive.OwnerOperatorAttachment ? 0.75f : 0.35f,
                listedForSale = true,
                minimumAcceptableRatio = Mathf.Clamp(askToValueRatio * Mathf.Lerp(0.88f, 0.98f, 1f - pressure), 0.72f, 1.08f),
                counterofferFlexibility01 = Mathf.Clamp01(0.35f + pressure * 0.45f)
            };
        }

        private BuyerOfferProfile BuildPlayerBuyerProfile(AcquisitionDealState deal)
        {
            float headlineReputation = GetOwnerHeadlineReputation01();
            float dealTrust = playerDebtManager != null && playerDebtManager.Reputation != null ? playerDebtManager.Reputation.dealTrust01 : headlineReputation;
            float lenderTrust = playerDebtManager != null ? playerDebtManager.LenderTrust01 : 0.58f;
            float localTrust = GetOwnerLocalTrust01();
            float operationalReliability = GetOwnerOperationalReliability01();
            int expectedPrice = deal != null && deal.tentativePriceCents > 0 ? deal.tentativePriceCents : deal != null ? deal.estimatedValueCents : 0;
            float cashCertainty = expectedPrice <= 0
                ? 0.55f
                : Mathf.Clamp01((GetAvailableCashCents() + (deal != null ? deal.earnestMoneyCents : 0)) / (float)Mathf.Max(1, expectedPrice));
            if (deal != null && deal.financingNeedCents > 0)
            {
                cashCertainty = Mathf.Clamp01(cashCertainty * 0.75f + lenderTrust * 0.25f);
            }

            return new BuyerOfferProfile
            {
                buyerId = "player",
                buyerKind = BuyerKind.Player,
                reputation01 = Mathf.Clamp01(headlineReputation * 0.7f + dealTrust * 0.3f),
                localTrust01 = localTrust,
                priorDealReliability01 = dealTrust,
                strategicThreat01 = Mathf.Lerp(0.18f, 0.06f, Mathf.Clamp01((localTrust + operationalReliability) * 0.5f)),
                communityFit01 = Mathf.Clamp01(localTrust * 0.65f + operationalReliability * 0.35f),
                cashCertainty01 = cashCertainty,
                relationshipWithSeller01 = Mathf.Clamp01(localTrust * 0.5f + operationalReliability * 0.25f + dealTrust * 0.25f)
            };
        }

        private float GetOwnerHeadlineReputation01()
        {
            if (playerDebtManager == null || playerDebtManager.Reputation == null)
            {
                return 0.55f;
            }

            float headline = new ReputationEvaluator().EvaluateHeadline01(playerDebtManager.Reputation);
            return headline > 0f ? headline : 0.55f;
        }

        private float GetOwnerLocalTrust01()
        {
            return playerDebtManager != null && playerDebtManager.Reputation != null
                ? Mathf.Clamp01(playerDebtManager.Reputation.localSocialTrust01)
                : 0.5f;
        }

        private float GetOwnerOperationalReliability01()
        {
            return playerDebtManager != null && playerDebtManager.Reputation != null
                ? Mathf.Clamp01(playerDebtManager.Reputation.operationalReliability01)
                : 0.55f;
        }

        private AcquisitionOfferTerms BuildOfferTerms(AcquisitionListing listing, AcquisitionDealState deal)
        {
            int offerPrice = Mathf.Max(1, listing != null ? listing.askingPriceCents : deal != null ? deal.estimatedValueCents : 1);
            int daysLeft = deal != null && deal.optionDeadlineDayIndex >= 0 ? deal.optionDeadlineDayIndex - GetCurrentDayIndex() : 7;
            float financingNeedRatio = deal != null && offerPrice > 0 ? Mathf.Clamp01(deal.financingNeedCents / (float)offerPrice) : 0f;
            return new AcquisitionOfferTerms
            {
                offerPriceCents = offerPrice,
                earnestMoneyCents = deal != null ? deal.earnestMoneyCents : 0,
                closingSpeed01 = Mathf.Clamp01(1f - Mathf.Clamp01(daysLeft / 21f) * 0.35f),
                contingencyBurden01 = financingNeedRatio > 0f ? Mathf.Lerp(0.18f, 0.36f, financingNeedRatio) : 0.06f,
                inspectionStrictness01 = deal != null ? Mathf.Lerp(0.45f, 0.12f, deal.diligenceScore01) : 0.25f,
                sellerFinancingRequested = false,
                nonPriceConcessions01 = 0.16f
            };
        }

        private bool TryBuildParcelValuation(AcquisitionListing listing, out ParcelValuationResult valuation)
        {
            valuation = null;
            if (listing == null)
            {
                return false;
            }

            TownPlot plot = GetPlot(listing.plotId);
            PlacedBuilding building = GetBuilding(listing.buildingId);
            Vector2Int siteSize = listing.siteSizeCells;
            if (siteSize.x <= 0 || siteSize.y <= 0)
            {
                siteSize = plot != null ? plot.siteSizeCells : building != null ? building.siteSizeCells : Vector2Int.one;
            }

            LandAppreciationImprovementState improvementState = listing.kind == AcquisitionListingKind.Business
                ? (!string.IsNullOrWhiteSpace(listing.businessInstanceId) ? LandAppreciationImprovementState.OperatingBusiness : LandAppreciationImprovementState.ImprovedShell)
                : LandAppreciationImprovementState.Empty;
            float centerScore = EstimateCenterAccess01(plot);
            ParcelValuationInputs inputs = new ParcelValuationInputs
            {
                assetId = listing.listingId,
                plotId = listing.plotId,
                buildingId = listing.buildingId,
                assetKind = listing.kind == AcquisitionListingKind.Business ? ParcelAssetKind.OperatingBusiness : ParcelAssetKind.Land,
                zone = listing.plotZone,
                siteSizeCells = siteSize,
                buildableAreaCells = building != null ? building.footprint.Area : plot != null ? plot.bounds.Area : siteSize.x * siteSize.y,
                frontageCells = listing.frontageCells,
                depthCells = plot != null ? plot.depthCells : siteSize.y,
                roadFrontageDirection = plot != null ? plot.roadFrontageDirection : GridDirection.North,
                distanceToMainStreet01 = 1f - centerScore,
                distanceToTownCenter01 = 1f - centerScore,
                trafficExposure01 = Mathf.Clamp01(centerScore * 0.65f + Mathf.Clamp01(listing.frontageCells / 8f) * 0.35f),
                nearbyHouseholds01 = listing.plotZone == PlotZone.Residential || listing.plotZone == PlotZone.MixedUse ? 0.68f : 0.45f,
                nearbyBusinesses01 = listing.plotZone == PlotZone.Business || listing.plotZone == PlotZone.MixedUse ? 0.72f : 0.35f,
                competitionPressure01 = listing.kind == AcquisitionListingKind.Business ? 0.28f : 0.12f,
                commercialFit01 = listing.kind == AcquisitionListingKind.Business ? 0.82f : listing.plotZone == PlotZone.Business ? 0.72f : 0.48f,
                developmentReadiness01 = EstimateDevelopmentReadiness01(plot, building, improvementState),
                improvementCondition01 = building != null ? Mathf.Lerp(0.58f, 0.86f, Mathf.Clamp01(listing.frontageCells / 8f)) : 1f,
                knownConstraints01 = Mathf.Clamp01((PositiveHash((listing.plotId + 1) * 31 + (listing.buildingId + 7) * 17 + marketSeed) % 22) / 100f),
                listedForSale = true,
                currentAskingPriceCents = listing.askingPriceCents
            };

            valuation = new ParcelValuationEvaluator().Evaluate(inputs);
            return valuation != null;
        }

        private ParcelValuationResult BuildSavedValuationResult(AcquisitionDealState deal)
        {
            int estimate = Mathf.Max(1, deal != null ? deal.estimatedValueCents : 1);
            int low = Mathf.Max(1, deal != null && deal.valuationBandLowCents > 0 ? deal.valuationBandLowCents : Mathf.RoundToInt(estimate * 0.85f));
            int high = Mathf.Max(low, deal != null && deal.valuationBandHighCents > 0 ? deal.valuationBandHighCents : Mathf.RoundToInt(estimate * 1.15f));
            float confidence = Mathf.Clamp01(deal != null && deal.diligenceScore01 > 0f ? deal.diligenceScore01 : 0.55f);
            ValuationScorecard scorecard = new ValuationScorecard(confidence, Array.Empty<ScorecardRow>());
            return new ParcelValuationResult(estimate, low, high, confidence, confidence, confidence, confidence, confidence, scorecard, Array.Empty<string>(), Array.Empty<string>());
        }

        private string BuildDiligenceSummary(AcquisitionListing listing, ParcelValuationResult valuation, float diligenceScore01)
        {
            string band = $"{FormatMoney(valuation.ValuationBandLowCents)}-{FormatMoney(valuation.ValuationBandHighCents)}";
            string priceRead = listing.askingPriceCents > valuation.ValuationBandHighCents
                ? "asking is above the diligence band"
                : listing.askingPriceCents < valuation.ValuationBandLowCents
                    ? "asking is below the diligence band"
                    : "asking sits inside the diligence band";
            return $"Diligence complete: value {FormatMoney(valuation.EstimatedValueCents)} (band {band}); {priceRead}; score {FormatPercent(diligenceScore01)}.";
        }

        private int CalculateEarnestMoneyCents(AcquisitionListing listing, SellerPressureResult sellerPressure)
        {
            int price = Mathf.Max(0, listing != null ? listing.askingPriceCents : 0);
            if (price <= 0)
            {
                return 0;
            }

            int target = Mathf.RoundToInt(price * Mathf.Lerp(0.03f, 0.07f, sellerPressure.Pressure01));
            int minimum = Mathf.Min(price, 100);
            return Mathf.Clamp(Mathf.Max(minimum, target), 0, price);
        }

        private static int CalculateOptionWindowDays(AcquisitionListing listing, SellerPressureResult sellerPressure)
        {
            int baseDays = listing != null && listing.kind == AcquisitionListingKind.Business ? 21 : 14;
            int pressureReduction = Mathf.RoundToInt(sellerPressure.Pressure01 * 5f);
            return Mathf.Clamp(baseDays - pressureReduction, 7, 21);
        }

        private static SellerMotive GetSellerMotive(string reason)
        {
            return reason switch
            {
                "Owner retirement" => SellerMotive.Retirement,
                "Debt pressure" => SellerMotive.DebtPressure,
                "Relocation" => SellerMotive.Relocation,
                "Financial distress" => SellerMotive.FinancialDistress,
                "Owner liquidating" => SellerMotive.Liquidating,
                _ => SellerMotive.Holding
            };
        }

        private static float GetSellerPressure(SellerMotive motive)
        {
            return motive switch
            {
                SellerMotive.FinancialDistress => 0.86f,
                SellerMotive.DebtPressure => 0.76f,
                SellerMotive.Liquidating => 0.68f,
                SellerMotive.Relocation => 0.58f,
                SellerMotive.Retirement => 0.48f,
                _ => 0.38f
            };
        }

        private float EstimateCenterAccess01(TownPlot plot)
        {
            if (plot == null || townWorld == null || townWorld.Grid == null)
            {
                return 0.5f;
            }

            float halfDepth = Mathf.Max(1f, townWorld.Grid.Depth * 0.5f);
            return 1f - Mathf.Clamp01(Mathf.Abs(plot.roadAccessCell.z - halfDepth) / halfDepth);
        }

        private static string FormatSellerMotive(SellerMotive motive)
        {
            return motive switch
            {
                SellerMotive.TestingMarket => "testing market",
                SellerMotive.DebtPressure => "debt pressure",
                SellerMotive.FinancialDistress => "financial distress",
                SellerMotive.EstateSale => "estate sale",
                SellerMotive.StrategicHoldout => "strategic holdout",
                SellerMotive.OwnerOperatorAttachment => "owner attachment",
                SellerMotive.Liquidating => "liquidating",
                SellerMotive.Relocation => "relocation",
                SellerMotive.Retirement => "retirement",
                _ => "holding"
            };
        }

        private static string FormatPercent(float value01)
        {
            return $"{Mathf.RoundToInt(Mathf.Clamp01(value01) * 100f)}%";
        }

        private void RecordOwnershipAptitudeGain(OwnershipAptitudeSource source)
        {
            EnsureOwnershipAptitude();
            ownershipAptitude.RecordSource(source);
            if (source != OwnershipAptitudeSource.OwnershipMilestone)
            {
                ownershipAptitude.RecordOwnershipMilestones(ownedPlotIds.Count, ownedBusinessBuildingIds.Count);
            }
        }

        private void EnsureOwnershipAptitude()
        {
            ownershipAptitude ??= new OwnershipAptitudeState();
            ownershipAptitude.Sanitize();
        }

        private int CalculateLandPriceCents(TownPlot plot)
        {
            float zoneMultiplier = plot.zone == PlotZone.Business
                ? businessDistrictLandMultiplier
                : residentialLandMultiplier;
            float visibilityMultiplier = GetVisibilityMultiplier(plot);
            int basePrice = plot.bounds.Area * baseLandPricePerCellCents;
            int frontagePremium = plot.frontageCells * roadFrontagePremiumPerCellCents;
            return Mathf.Max(1, Mathf.RoundToInt((basePrice + frontagePremium) * zoneMultiplier * visibilityMultiplier));
        }

        private int CalculateBusinessPriceCents(PlacedBuilding building, TownPlot plot, string reason)
        {
            int landValue = plot != null ? CalculateLandPriceCents(plot) : building.siteSizeCells.x * building.siteSizeCells.y * baseLandPricePerCellCents;
            int buildingValue = building.footprint.Area * buildingValuePerFootprintCellCents;
            float mixedUseMultiplier = building.definition != null && building.definition.IsMixedUse ? mixedUseBusinessMultiplier : 1f;
            float reasonMultiplier = GetReasonPriceMultiplier(reason);
            int price = Mathf.RoundToInt((landValue + buildingValue + businessIncomeProxyCents) * mixedUseMultiplier * reasonMultiplier);
            return Mathf.Max(1, price);
        }

        private int CalculateConstructionCostCents(BuildingDefinition definition, TownPlot plot)
        {
            if (definition == null || plot == null)
            {
                return 0;
            }

            return BuildShellConstructionQuote(definition, plot).CashCostCents;
        }

        private float GetVisibilityMultiplier(TownPlot plot)
        {
            if (townWorld == null || townWorld.Grid == null)
            {
                return 1f;
            }

            float centerDistance = Mathf.Abs(plot.roadAccessCell.z - townWorld.Grid.Depth * 0.5f);
            float normalized = 1f - Mathf.Clamp01(centerDistance / Mathf.Max(1f, townWorld.Grid.Depth * 0.5f));
            return 1f + normalized * 0.25f;
        }

        private string GetBusinessSaleReason(int buildingId)
        {
            return (PositiveHash(buildingId + marketSeed) % 5) switch
            {
                0 => "Owner retirement",
                1 => "Debt pressure",
                2 => "Relocation",
                3 => "Financial distress",
                _ => "Owner liquidating"
            };
        }

        private static float GetReasonPriceMultiplier(string reason)
        {
            return reason switch
            {
                "Financial distress" => 0.78f,
                "Debt pressure" => 0.86f,
                "Owner liquidating" => 0.9f,
                "Relocation" => 0.96f,
                _ => 1.05f
            };
        }

        private int GetMarketSortKey(int id)
        {
            return PositiveHash(id * 397 + marketSeed);
        }

        private static int PositiveHash(int value)
        {
            unchecked
            {
                uint x = (uint)value;
                x ^= x >> 16;
                x *= 0x7feb352d;
                x ^= x >> 15;
                x *= 0x846ca68b;
                x ^= x >> 16;
                return (int)(x & 0x7fffffff);
            }
        }


        private string GenerateConstructionProjectId()
        {
            int serial = Mathf.Max(1, nextConstructionProjectSerial);
            nextConstructionProjectSerial = serial + 1;
            return $"construction_project_{serial:0000}";
        }

        private int TryExtractConstructionProjectSerial(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                return 0;
            }

            int underscore = projectId.LastIndexOf('_');
            if (underscore < 0 || underscore >= projectId.Length - 1)
            {
                return 0;
            }

            return int.TryParse(projectId.Substring(underscore + 1), out int parsed) ? Mathf.Max(0, parsed) : 0;
        }

        private ConstructionProjectState GetNextConstructionProject()
        {
            List<ConstructionProjectState> ordered = GetWeeklyConstructionProcessingOrder();
            return ordered.Count > 0 ? ordered[0] : null;
        }

        private List<ConstructionProjectState> GetWeeklyConstructionProcessingOrder()
        {
            List<ConstructionProjectState> ordered = new();
            for (int i = 0; i < activeConstructionProjects.Count; i++)
            {
                ConstructionProjectState project = activeConstructionProjects[i];
                if (project != null && project.IsActive)
                {
                    ordered.Add(project);
                }
            }

            ordered.Sort((left, right) =>
            {
                if (left == null && right == null)
                {
                    return 0;
                }

                if (left == null)
                {
                    return 1;
                }

                if (right == null)
                {
                    return -1;
                }

                int phase = left.IsStarted == right.IsStarted ? 0 : (left.IsStarted ? -1 : 1);
                if (phase != 0)
                {
                    return phase;
                }

                int week = left.queuedWeekKey.CompareTo(right.queuedWeekKey);
                if (week != 0)
                {
                    return week;
                }

                return string.CompareOrdinal(left.projectId, right.projectId);
            });

            return ordered;
        }

        private int CountConstructionProjectsAhead(List<ConstructionProjectState> ordered, int index)
        {
            int count = 0;
            if (ordered == null)
            {
                return 0;
            }

            for (int i = 0; i < index && i < ordered.Count; i++)
            {
                if (ordered[i] != null && ordered[i].IsActive)
                {
                    count++;
                }
            }

            return count;
        }

        private int EstimateWeeklyConstructionBuilderCapacity(TownGenerationSettings settings)
        {
            int configured = settings != null ? settings.GetBuilderCapacityPerWeek() : 1;
            int availableLaborUnits = GetAvailableLaborUnits(out _);
            if (availableLaborUnits <= 0)
            {
                return 0;
            }

            int laborBackedCapacity = Mathf.Max(1, availableLaborUnits / Mathf.Max(1, laborUnitsPerEligibleWorker * 4));
            return Mathf.Max(0, Mathf.Min(Mathf.Max(1, configured), laborBackedCapacity));
        }

        private string BuildProjectQueueCapacityBlockedReason(TownGenerationSettings settings, ConstructionProjectState project, int queueAhead)
        {
            BuildingDefinition definition = FindBuildingDefinitionById(project != null ? project.buildingDefinitionId : string.Empty);
            string queueRead = settings != null
                ? settings.BuildProjectQueueSummary(definition, Mathf.Max(1, queueAhead + 1), false)
                : "Builder queue looks full this week.";
            return $"Builder queue is full this week. {queueRead}";
        }

        private static int GetQuoteUnits(ConstructionInputQuote quote, ConstructionResourceKind kind)
        {
            ConstructionResourceQuoteLine line = quote != null ? quote.GetLine(kind) : null;
            return line != null ? Mathf.Max(0, line.requiredUnits) : 0;
        }

        private string BuildQueuedConstructionStatusText(ConstructionInputQuote quote, BuildingDefinition definition, int weeksRequired, string prefix)
        {
            TownGenerationSettings settings = townWorld != null ? townWorld.Settings : null;
            ConstructionSupportNodeState supportNode = GetPrimaryLumberSupportNode();
            int lumberUnits = GetQuoteUnits(quote, ConstructionResourceKind.Lumber);
            string authority = supportNode != null
                ? supportNode.BuildProjectStartAuthoritySummary(settings, definition, Mathf.Max(0, activeConstructionProjects.Count), lumberUnits)
                : "Start stance: waiting on local support.";
            string missing = quote != null && !quote.CanProceed
                ? $" Missing now: {quote.BuildMissingSummary()}."
                : " Inputs look available when a builder slot opens.";
            return $"{prefix} Typical duration about {Mathf.Max(1, weeksRequired)} week(s). {authority}{missing}";
        }

        private ConstructionSupportNodeState GetPrimaryLumberSupportNode()
        {
            EnsureConstructionSupportNodesLoaded();
            for (int i = 0; i < constructionSupportNodes.Count; i++)
            {
                ConstructionSupportNodeState node = constructionSupportNodes[i];
                if (node != null && node.Active && node.ResourceKind == ConstructionResourceKind.Lumber)
                {
                    return node;
                }
            }

            return null;
        }

        private bool HasActiveConstructionProjectForPlot(int plotId, ConstructionProjectKind projectKind)
        {
            for (int i = 0; i < activeConstructionProjects.Count; i++)
            {
                ConstructionProjectState project = activeConstructionProjects[i];
                if (project != null
                    && project.IsActive
                    && project.projectKind == projectKind
                    && project.plotId == plotId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasActiveConstructionProjectForBuilding(int buildingId, ConstructionProjectKind projectKind)
        {
            for (int i = 0; i < activeConstructionProjects.Count; i++)
            {
                ConstructionProjectState project = activeConstructionProjects[i];
                if (project != null
                    && project.IsActive
                    && project.projectKind == projectKind
                    && project.buildingId == buildingId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryQueueResolvedShellConstruction(int plotId, BuildingDefinition definition, BusinessType? businessIntent, out string message)
        {
            message = string.Empty;
            EnsureMarket();
            RefreshOwnedBuildability();

            OwnedPlotBuildabilityState state = FindOwnedBuildabilityState(plotId);
            if (state == null)
            {
                message = $"Plot {plotId:000} is not tracked as a player-owned plot.";
                return false;
            }

            if (!state.playerOwned)
            {
                message = $"Plot {plotId:000} is not player-owned.";
                return false;
            }

            if (!state.empty)
            {
                message = $"Plot {plotId:000} already has an improved site.";
                return false;
            }

            if (definition == null)
            {
                message = "No building shell definition is selected.";
                return false;
            }

            if (HasActiveConstructionProjectForPlot(plotId, ConstructionProjectKind.BuildingShell))
            {
                message = $"A shell project is already queued for Plot {plotId:000}.";
                return false;
            }

            TownPlot plot = GetPlot(plotId);
            if (plot == null)
            {
                message = $"Plot {plotId:000} is missing from the generated town.";
                return false;
            }

            TownGenerationSettings settings = townWorld != null ? townWorld.Settings : null;
            if (!CanFitBuildingOnPlot(plot, definition, settings))
            {
                message = $"{definition.DisplayName} does not physically fit Plot {plotId:000}.";
                return false;
            }

            ConstructionInputQuote quote = BuildShellConstructionQuote(definition, plot, businessIntent);
            int weeks = Mathf.Max(1, settings != null ? settings.GetTypicalBuildWeeksForDefinition(definition) : 1);
            ConstructionProjectState project = new()
            {
                projectId = GenerateConstructionProjectId(),
                projectKind = ConstructionProjectKind.BuildingShell,
                progressState = ConstructionProjectProgressState.Queued,
                title = definition.DisplayName,
                plotId = plotId,
                buildingDefinitionId = definition.BuildingId,
                hasBusinessIntent = businessIntent.HasValue,
                businessIntent = businessIntent ?? BusinessType.GeneralStore,
                queuedWeekKey = GetCurrentWeekKey(),
                weeksRequired = weeks,
                estimatedCashCostCents = quote.CashCostCents,
                plannedLumberUnits = GetQuoteUnits(quote, ConstructionResourceKind.Lumber),
                plannedNailsUnits = GetQuoteUnits(quote, ConstructionResourceKind.Nails),
                plannedLaborUnits = GetQuoteUnits(quote, ConstructionResourceKind.Labor),
                statusText = BuildQueuedConstructionStatusText(quote, definition, weeks, $"Queued shell on Plot {plotId:000}.")
            };

            activeConstructionProjects.Add(project);
            lastConstructionQueueSummary = BuildConstructionQueueSummary();
            marketStatus = $"Queued {definition.DisplayName} on Plot {plotId:000}.";
            message = project.statusText;
            return true;
        }

        private BuildingDefinition FindBuildingDefinitionById(string buildingId)
        {
            if (string.IsNullOrWhiteSpace(buildingId))
            {
                return null;
            }

            TownGenerationSettings settings = townWorld != null ? townWorld.Settings : null;
            BuildingDefinition[] catalog = settings != null ? settings.buildingCatalog : null;
            if (catalog != null)
            {
                for (int i = 0; i < catalog.Length; i++)
                {
                    BuildingDefinition definition = catalog[i];
                    if (definition != null && string.Equals(definition.BuildingId, buildingId, StringComparison.Ordinal))
                    {
                        return definition;
                    }
                }
            }

            if (townWorld != null && townWorld.Buildings != null)
            {
                for (int i = 0; i < townWorld.Buildings.Count; i++)
                {
                    BuildingDefinition definition = townWorld.Buildings[i] != null ? townWorld.Buildings[i].definition : null;
                    if (definition != null && string.Equals(definition.BuildingId, buildingId, StringComparison.Ordinal))
                    {
                        return definition;
                    }
                }
            }

            return null;
        }

        private void BlockConstructionProject(ConstructionProjectState project, string blockedReason)
        {
            if (project == null)
            {
                return;
            }

            project.blockedWeeks = Mathf.Max(0, project.blockedWeeks) + 1;
            project.readyWeeks = 0;
            project.progressState = project.IsStarted
                ? ConstructionProjectProgressState.Stalled
                : ConstructionProjectProgressState.BlockedBeforeStart;
            project.blockedReason = blockedReason ?? string.Empty;
            project.lastWeeklyProgressSummary = project.blockedReason;
            project.statusText = project.IsStarted
                ? $"{project.title} - stalled. {project.blockedReason}"
                : $"{project.title} - blocked before start. {project.blockedReason}";
        }

        private void MarkConstructionProjectWaitingForLane(ConstructionProjectState project, string reason)
        {
            if (project == null)
            {
                return;
            }

            project.progressState = ConstructionProjectProgressState.WaitingForLane;
            project.blockedWeeks = Mathf.Max(0, project.blockedWeeks) + 1;
            project.readyWeeks = 0;
            project.blockedReason = reason ?? string.Empty;
            project.lastWeeklyProgressSummary = project.blockedReason;
            project.statusText = $"{project.title} - waiting for builder lane. {project.blockedReason}";
        }

        private void FailConstructionProject(ConstructionProjectState project, string reason)
        {
            if (project == null)
            {
                return;
            }

            project.progressState = ConstructionProjectProgressState.Failed;
            project.blockedReason = reason ?? string.Empty;
            project.lastWeeklyProgressSummary = project.blockedReason;
            project.statusText = $"{project.title} - failed. {project.blockedReason}";
        }

        private static bool ShouldFailQueuedConstructionProject(ConstructionProjectState project, string message)
        {
            if (project == null || string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            return project.projectKind switch
            {
                ConstructionProjectKind.BuildingShell => message.Contains("already has an improved site", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("missing from the generated town", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("no longer available", StringComparison.OrdinalIgnoreCase),
                ConstructionProjectKind.BusinessFitOut => message.Contains("no longer available", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("cannot start", StringComparison.OrdinalIgnoreCase),
                ConstructionProjectKind.HouseholdUpgrade => message.Contains("already built", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("no resident household", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("not player-owned", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        private string BuildProjectProgressText(ConstructionProjectState project)
        {
            if (project == null)
            {
                return "No progress.";
            }

            return $"Week {Mathf.Max(0, project.weeksProgressed)}/{Mathf.Max(1, project.weeksRequired)} complete; {Mathf.Max(0, project.WeeksRemaining)} week(s) remaining.";
        }

        private bool TryStartQueuedConstructionProject(ConstructionProjectState project, int queueAhead, out string message)
        {
            message = string.Empty;
            if (project == null)
            {
                message = "Missing queued project.";
                return false;
            }

            if (!TryBuildCurrentConstructionQuote(project, out ConstructionInputQuote quote, out BuildingDefinition definition, out string resolveMessage))
            {
                message = resolveMessage;
                return false;
            }

            TownGenerationSettings settings = townWorld != null ? townWorld.Settings : null;
            ConstructionSupportNodeState supportNode = GetPrimaryLumberSupportNode();
            int lumberUnits = GetQuoteUnits(quote, ConstructionResourceKind.Lumber);
            string startAuthority = supportNode != null
                ? supportNode.BuildProjectStartAuthoritySummary(settings, definition, queueAhead, lumberUnits)
                : string.Empty;

            if (!quote.CanProceed)
            {
                message = $"{BuildQuoteBlockedMessage(quote)} {startAuthority}".Trim();
                return false;
            }

            if (supportNode != null && !supportNode.IsProjectLikelyStartReady(settings, lumberUnits, queueAhead, out string supportReason))
            {
                message = $"{supportReason} {startAuthority}".Trim();
                return false;
            }

            if (!TryValidateConstructionInputsAvailable(quote, out message))
            {
                message = $"{message} {startAuthority}".Trim();
                return false;
            }

            string spendLabel = quote.projectLabel;
            if (!TrySpend(quote.CashCostCents, spendLabel, out message))
            {
                message = $"{message} {startAuthority}".Trim();
                return false;
            }

            ConstructionInputRollbackState rollback = new(this);
            if (!TryConsumeConstructionInputs(quote, rollback, out message))
            {
                rollback.Rollback();
                RefundSpend(quote.CashCostCents, spendLabel);
                message = $"{message} {startAuthority}".Trim();
                return false;
            }

            project.inputsCommitted = true;
            project.startedWeekKey = GetCurrentWeekKey();
            project.progressState = ConstructionProjectProgressState.InProgress;
            project.estimatedCashCostCents = quote.CashCostCents;
            project.plannedLumberUnits = GetQuoteUnits(quote, ConstructionResourceKind.Lumber);
            project.plannedNailsUnits = GetQuoteUnits(quote, ConstructionResourceKind.Nails);
            project.plannedLaborUnits = GetQuoteUnits(quote, ConstructionResourceKind.Labor);
            project.committedInputSummary = BuildSupportConsumptionSummary(quote);
            project.blockedReason = string.Empty;
            project.statusText = $"{project.title} started. {BuildProjectProgressText(project)}";
            project.lastWeeklyProgressSummary = $"Started this week. Inputs: {project.committedInputSummary}.";
            message = $"{project.title} started. Inputs: {project.committedInputSummary}.";
            return true;
        }

        private bool TryBuildCurrentConstructionQuote(ConstructionProjectState project, out ConstructionInputQuote quote, out BuildingDefinition definition, out string message)
        {
            quote = null;
            definition = null;
            message = string.Empty;
            if (project == null)
            {
                message = "Missing queued project.";
                return false;
            }

            switch (project.projectKind)
            {
                case ConstructionProjectKind.BuildingShell:
                    {
                        TownPlot plot = GetPlot(project.plotId);
                        if (plot == null)
                        {
                            message = $"Plot {project.plotId:000} is missing from the generated town.";
                            return false;
                        }

                        if (plot.buildingId >= 0)
                        {
                            message = $"Plot {project.plotId:000} already has an improved site.";
                            return false;
                        }

                        definition = FindBuildingDefinitionById(project.buildingDefinitionId);
                        if (definition == null)
                        {
                            message = $"Building shell definition {project.buildingDefinitionId} is no longer available.";
                            return false;
                        }

                        TownGenerationSettings settings = townWorld != null ? townWorld.Settings : null;
                        if (!CanFitBuildingOnPlot(plot, definition, settings))
                        {
                            message = $"{definition.DisplayName} no longer fits Plot {project.plotId:000}.";
                            return false;
                        }

                        quote = BuildShellConstructionQuote(definition, plot, project.hasBusinessIntent ? project.businessIntent : (BusinessType?)null);
                        return true;
                    }

                case ConstructionProjectKind.BusinessFitOut:
                    {
                        PlacedBuilding building = GetBuilding(project.buildingId);
                        if (building == null)
                        {
                            message = $"Building {project.buildingId:000} is no longer available.";
                            return false;
                        }

                        definition = building.definition;
                        BusinessActivationCandidateState candidate = CreateActivationCandidate(project.buildingId, project.businessIntent);
                        if (candidate == null || !candidate.eligible)
                        {
                            message = candidate != null && !string.IsNullOrWhiteSpace(candidate.reason)
                                ? candidate.reason
                                : $"{BusinessRuntimeNaming.GetBusinessTypeDisplayName(project.businessIntent)} cannot start at this site.";
                            return false;
                        }

                        quote = BuildBusinessFitOutQuote(candidate, building);
                        return true;
                    }

                case ConstructionProjectKind.HouseholdUpgrade:
                    {
                        HouseholdUpgradeDefinition upgradeDefinition = HouseholdUpgradeCatalog.Get((HouseholdUpgradeKind)Mathf.Max(0, project.householdUpgradeKind));
                        if (upgradeDefinition == null)
                        {
                            message = "Queued household upgrade definition is no longer available.";
                            return false;
                        }

                        PlacedBuilding building;
                        if (!TryResolveHouseholdUpgradeTarget(project.buildingId, upgradeDefinition.Kind, out _, out building, out message))
                        {
                            return false;
                        }

                        definition = building != null ? building.definition : null;
                        quote = BuildHouseholdUpgradeQuote(upgradeDefinition, project.buildingId);
                        return true;
                    }

                default:
                    message = "Unknown queued project kind.";
                    return false;
            }
        }

        private bool TryCompleteQueuedConstructionProject(ConstructionProjectState project, out string message)
        {
            message = string.Empty;
            if (project == null)
            {
                message = "Missing queued project.";
                return false;
            }

            switch (project.projectKind)
            {
                case ConstructionProjectKind.BuildingShell:
                    return TryCompleteQueuedShellConstruction(project, out message);
                case ConstructionProjectKind.BusinessFitOut:
                    return TryCompleteQueuedBusinessFitOut(project, out message);
                case ConstructionProjectKind.HouseholdUpgrade:
                    return TryCompleteQueuedHouseholdUpgrade(project, out message);
                default:
                    message = "Unknown queued project kind.";
                    return false;
            }
        }

        private bool TryCompleteQueuedShellConstruction(ConstructionProjectState project, out string message)
        {
            message = string.Empty;
            BuildingDefinition definition = FindBuildingDefinitionById(project.buildingDefinitionId);
            if (definition == null)
            {
                message = $"Building shell definition {project.buildingDefinitionId} is no longer available.";
                return false;
            }

            TownPlot plot = GetPlot(project.plotId);
            if (plot == null)
            {
                message = $"Plot {project.plotId:000} is missing from the generated town.";
                return false;
            }

            if (plot.buildingId >= 0)
            {
                message = $"Plot {project.plotId:000} already has an improved site.";
                return false;
            }

            PlacedBuilding building;
            string buildMessage = string.Empty;
            if (townWorld == null || !townWorld.TryConstructBuildingShell(project.plotId, definition, true, out building, out buildMessage))
            {
                message = string.IsNullOrWhiteSpace(buildMessage) ? $"Could not complete {definition.DisplayName} shell." : buildMessage;
                return false;
            }

            if (!ownedPlotIds.Contains(plot.id))
            {
                ownedPlotIds.Add(plot.id);
            }

            CapitalizeLandImprovement(plot, building, project.estimatedCashCostCents, LandAppreciationImprovementState.ImprovedShell);
            RemoveLandListingForPlot(plot.id);
            RefreshOwnedBuildability();
            marketStatus = $"Construction complete. {definition.DisplayName} shell on Plot {plot.id:000}.";
            lastConstructionSupportSummary = string.IsNullOrWhiteSpace(project.committedInputSummary)
                ? lastConstructionSupportSummary
                : project.committedInputSummary;

            PlayerDevelopmentFitResult fit = project.hasBusinessIntent
                ? EvaluatePlayerOwnedBusinessShellConstructionFit(definition, plot, project.businessIntent)
                : EvaluatePlayerOwnedShellConstructionFit(definition, plot);
            string fitNote = fit != null && fit.HasWarnings ? $" Fit warnings: {fit.WarningSummary}." : string.Empty;
            string nextStep = project.hasBusinessIntent
                ? $" Business intent: {BusinessRuntimeNaming.GetBusinessTypeDisplayName(project.businessIntent)}; start the business from this owned shell when fit-out inputs are ready."
                : " House mode selected; household upgrades remain available, and business startup can still be selected on owned land.";
            lastPurchaseSummary = $"{definition.DisplayName} building shell completed on Plot {plot.id:000} after {Mathf.Max(1, project.weeksRequired)} week(s). Inputs: {lastConstructionSupportSummary}.{nextStep}{fitNote}";
            RecordOwnershipAptitudeGain(OwnershipAptitudeSource.ParcelDeveloped);
            message = lastPurchaseSummary;
            return true;
        }

        private bool TryCompleteQueuedBusinessFitOut(ConstructionProjectState project, out string message)
        {
            message = string.Empty;
            PlacedBuilding building = GetBuilding(project.buildingId);
            if (building == null)
            {
                message = $"Building {project.buildingId:000} is no longer available.";
                return false;
            }

            bool started;
            BusinessInstanceState business = null;
            if (project.businessIntent == BusinessType.GeneralStore)
            {
                started = playerCashSource != null && playerCashSource.TryOpenAtPlayerOwnedShell(project.buildingId, out business, out message);
            }
            else
            {
                started = sharedBusinessRuntime != null && sharedBusinessRuntime.TryStartPlayerBusinessAtShell(project.businessIntent, project.buildingId, out business, out message);
            }

            if (!started || business == null)
            {
                message = !string.IsNullOrWhiteSpace(message)
                    ? message
                    : $"Could not start {BusinessRuntimeNaming.GetBusinessTypeDisplayName(project.businessIntent)} at this shell.";
                return false;
            }

            ConfigurePlayerBusinessStabilizationDefaults(business);
            playerPortfolio?.RegisterCurrentBusinessCashCheckpoint(business, GetCurrentWeekKey(), true);

            building.playerOwned = true;
            if (project.businessIntent != BusinessType.GeneralStore && !ownedBusinessBuildingIds.Contains(building.id))
            {
                ownedBusinessBuildingIds.Add(building.id);
            }

            lastConstructionSupportSummary = string.IsNullOrWhiteSpace(project.committedInputSummary)
                ? lastConstructionSupportSummary
                : project.committedInputSummary;
            lastPurchaseSummary = $"{BusinessRuntimeNaming.GetBusinessTypeDisplayName(project.businessIntent)} opened at Building {project.buildingId:000} after {Mathf.Max(1, project.weeksRequired)} week(s). Inputs: {lastConstructionSupportSummary}.";
            marketStatus = $"{BusinessRuntimeNaming.GetBusinessTypeDisplayName(project.businessIntent)} opened from queued fit-out.";
            message = lastPurchaseSummary;
            return true;
        }

        private bool TryCompleteQueuedHouseholdUpgrade(ConstructionProjectState project, out string message)
        {
            message = string.Empty;
            HouseholdUpgradeDefinition definition = HouseholdUpgradeCatalog.Get((HouseholdUpgradeKind)Mathf.Max(0, project.householdUpgradeKind));
            if (definition == null)
            {
                message = "Queued household upgrade definition is no longer available.";
                return false;
            }

            if (!TryResolveHouseholdUpgradeTarget(project.buildingId, definition.Kind, out HouseholdState household, out _, out message))
            {
                return false;
            }

            int builtDay = GetCurrentDayIndex();
            if (!HouseholdUpgradeCatalog.TryAddBuiltUpgrade(household, definition.Kind, builtDay))
            {
                message = $"Could not apply {definition.DisplayName} to {household.householdName}.";
                return false;
            }

            HouseholdUpgradeState builtUpgrade = household.upgrades[household.upgrades.Count - 1];
            lastConstructionSupportSummary = string.IsNullOrWhiteSpace(project.committedInputSummary)
                ? lastConstructionSupportSummary
                : project.committedInputSummary;
            lastPurchaseSummary = $"{definition.DisplayName} completed for {household.householdName} after {Mathf.Max(1, project.weeksRequired)} week(s). Inputs: {lastConstructionSupportSummary}.";
            marketStatus = $"{definition.DisplayName} completed for {household.householdName}.";
            message = $"{lastPurchaseSummary} Built day {builtUpgrade.builtDayIndex}.";
            return true;
        }

        private TownPlot GetPlot(int plotId)
        {
            if (townWorld == null || plotId < 0 || plotId >= townWorld.Plots.Count)
            {
                return null;
            }

            return townWorld.Plots[plotId];
        }

        private bool TryResolveHouseholdUpgradeTarget(
            int homeBuildingId,
            HouseholdUpgradeKind upgradeKind,
            out HouseholdState household,
            out PlacedBuilding building,
            out string message)
        {
            AutoWire();
            household = null;
            building = GetBuilding(homeBuildingId);
            if (building == null)
            {
                message = $"Building {homeBuildingId:000} is no longer available.";
                return false;
            }

            if (!building.playerOwned)
            {
                message = $"Building {homeBuildingId:000} is not player-owned.";
                return false;
            }

            if (building.definition == null || !building.definition.CanHostHouseholds || building.definition.CanHostWorkplace)
            {
                message = $"Building {homeBuildingId:000} is not a residential household holding.";
                return false;
            }

            household = populationManager != null ? populationManager.GetHouseholdByHomeBuildingId(homeBuildingId) : null;
            if (household == null)
            {
                message = "No resident household assigned; upgrades unavailable.";
                return false;
            }

            if (HouseholdUpgradeCatalog.HasBuiltUpgrade(household, upgradeKind))
            {
                HouseholdUpgradeDefinition definition = HouseholdUpgradeCatalog.Get(upgradeKind);
                message = $"{(definition != null ? definition.DisplayName : upgradeKind.ToString())} is already built for {household.householdName}.";
                return false;
            }

            message = "Household upgrade target ready.";
            return true;
        }

        private PlacedBuilding GetBuilding(int buildingId)
        {
            if (townWorld == null || buildingId < 0 || buildingId >= townWorld.Buildings.Count)
            {
                return null;
            }

            return townWorld.Buildings[buildingId];
        }

        private OwnedPlotBuildabilityState FindOwnedBuildabilityState(int plotId)
        {
            for (int i = 0; i < ownedBuildablePlots.Count; i++)
            {
                if (ownedBuildablePlots[i] != null && ownedBuildablePlots[i].plotId == plotId)
                {
                    return ownedBuildablePlots[i];
                }
            }

            return null;
        }

        private void RemoveLandListingForPlot(int plotId)
        {
            for (int i = landListings.Count - 1; i >= 0; i--)
            {
                if (landListings[i] != null && landListings[i].plotId == plotId)
                {
                    landListings.RemoveAt(i);
                }
            }

            selectedLandIndex = WrapIndex(selectedLandIndex, landListings.Count);
        }

        private void RemoveBusinessListingForBuilding(int buildingId)
        {
            for (int i = businessListings.Count - 1; i >= 0; i--)
            {
                if (businessListings[i] != null && businessListings[i].buildingId == buildingId)
                {
                    businessListings.RemoveAt(i);
                }
            }

            selectedBusinessIndex = WrapIndex(selectedBusinessIndex, businessListings.Count);
        }

        private static string BuildRecoveredHoldingLabel(TownPlot plot, PlacedBuilding building)
        {
            if (building != null)
            {
                string buildingName = building.definition != null ? building.definition.DisplayName : "Building";
                return $"{buildingName} {building.id:000}";
            }

            return plot != null ? $"Plot {plot.id:000}" : "Recovered holding";
        }

        private void SyncSavedOwnershipToWorld()
        {
            for (int i = 0; i < ownedPlotIds.Count; i++)
            {
                TownPlot plot = GetPlot(ownedPlotIds[i]);
                if (plot != null)
                {
                    plot.playerOwned = true;
                }
            }

            for (int i = 0; i < ownedBusinessBuildingIds.Count; i++)
            {
                PlacedBuilding building = GetBuilding(ownedBusinessBuildingIds[i]);
                if (building == null)
                {
                    continue;
                }

                building.playerOwned = true;
                TownPlot plot = GetPlot(building.plotId);
                if (plot != null)
                {
                    plot.playerOwned = true;
                    if (!ownedPlotIds.Contains(plot.id))
                    {
                        ownedPlotIds.Add(plot.id);
                    }
                }
            }
        }

        private bool IsPlayerOwnedPlot(TownPlot plot)
        {
            return plot != null && (plot.playerOwned || ownedPlotIds.Contains(plot.id));
        }

        private bool IsPlayerOwnedBusiness(PlacedBuilding building)
        {
            return building != null && (building.playerOwned || ownedBusinessBuildingIds.Contains(building.id));
        }

        private void RefreshOwnedBuildability()
        {
            ownedBuildablePlots.Clear();
            if (townWorld == null || townWorld.Plots == null)
            {
                return;
            }

            for (int i = 0; i < townWorld.Plots.Count; i++)
            {
                TownPlot plot = townWorld.Plots[i];
                if (!IsPlayerOwnedPlot(plot))
                {
                    continue;
                }

                ownedBuildablePlots.Add(CreateBuildabilityState(plot));
            }
        }

        private OwnedPlotBuildabilityState CreateBuildabilityState(TownPlot plot)
        {
            OwnedPlotBuildabilityState state = new()
            {
                plotId = plot != null ? plot.id : -1,
                plotZone = plot != null ? plot.zone : PlotZone.MixedUse,
                siteSizeCells = plot != null ? plot.siteSizeCells : Vector2Int.zero,
                frontageCells = plot != null ? plot.frontageCells : 0,
                playerOwned = plot != null && IsPlayerOwnedPlot(plot),
                empty = plot != null && plot.buildingId < 0
            };

            if (plot == null)
            {
                state.buildable = false;
                state.reason = "Missing plot.";
                return state;
            }

            if (!state.playerOwned)
            {
                state.buildable = false;
                state.reason = "Plot is not player-owned.";
                return state;
            }

            if (plot.buildingId >= 0)
            {
                state.buildable = false;
                state.reason = "Plot already has a building.";
                return state;
            }

            if (!plot.bounds.IsValid || plot.frontageCells <= 0)
            {
                state.buildable = false;
                state.reason = "Plot has no valid buildable footprint or road frontage.";
                return state;
            }

            AddBuildOptions(plot, state.buildOptions);
            state.buildable = state.buildOptions.Count > 0;
            state.reason = state.buildable
                ? "Buildable owned empty plot. Parcel use is advisory for player-owned development."
                : "No current building catalog options physically fit this plot.";
            return state;
        }

        private bool TryResolveBusinessDevelopmentType(int optionIndex, out BusinessType businessType)
        {
            businessType = BusinessType.GeneralStore;
            return TryGetVisibleActivationBusinessType(optionIndex, out businessType);
        }

        private int GetVisibleActivationBusinessTypeCount()
        {
            int count = 0;
            for (int i = 0; i < ActivationBusinessTypes.Length; i++)
            {
                if (!ShouldHideActivationBusinessType(ActivationBusinessTypes[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private bool TryGetVisibleActivationBusinessType(int optionIndex, out BusinessType businessType)
        {
            businessType = BusinessType.GeneralStore;
            int count = GetVisibleActivationBusinessTypeCount();
            if (count <= 0)
            {
                return false;
            }

            int target = WrapIndex(optionIndex, count);
            int visibleIndex = 0;
            for (int i = 0; i < ActivationBusinessTypes.Length; i++)
            {
                BusinessType candidate = ActivationBusinessTypes[i];
                if (ShouldHideActivationBusinessType(candidate))
                {
                    continue;
                }

                if (visibleIndex == target)
                {
                    businessType = candidate;
                    return true;
                }

                visibleIndex++;
            }

            return false;
        }

        private bool ShouldHideActivationBusinessType(BusinessType businessType)
        {
            return businessType == BusinessType.GeneralStore
                && playerCashSource != null
                && playerCashSource.CurrentBusiness != null;
        }

        private bool TryResolveHouseBuildOption(
            int plotId,
            int optionIndex,
            out BuildingDefinition definition,
            out TownPlot plot,
            out string message)
        {
            definition = null;
            plot = null;
            EnsureMarket();
            RefreshOwnedBuildability();

            OwnedPlotBuildabilityState state = FindOwnedBuildabilityState(plotId);
            if (state == null)
            {
                message = $"Plot {plotId:000} is not tracked as a player-owned plot.";
                return false;
            }

            if (!state.playerOwned)
            {
                message = $"Plot {plotId:000} is not player-owned.";
                return false;
            }

            if (!state.empty)
            {
                message = $"Plot {plotId:000} already has an improved site.";
                return false;
            }

            plot = GetPlot(plotId);
            if (plot == null)
            {
                message = $"Plot {plotId:000} is missing from the generated town.";
                return false;
            }

            if (!plot.bounds.IsValid || plot.frontageCells <= 0)
            {
                message = "Plot has no valid buildable footprint or road frontage.";
                return false;
            }

            IReadOnlyList<BuildingDefinition> options = BuildHouseShellOptions(plot);
            if (options.Count <= 0)
            {
                message = "No house-oriented shell plan physically fits this plot.";
                return false;
            }

            definition = options[WrapIndex(optionIndex, options.Count)];
            if (definition == null)
            {
                message = "No house shell definition is selected.";
                return false;
            }

            message = "House shell plan selected.";
            return true;
        }

        private bool TryResolveBusinessBuildOption(
            int plotId,
            int businessOptionIndex,
            int shellOptionIndex,
            out BusinessType businessType,
            out BuildingDefinition definition,
            out TownPlot plot,
            out string message)
        {
            businessType = BusinessType.GeneralStore;
            definition = null;
            plot = null;

            if (!TryGetBusinessDevelopmentCandidate(businessOptionIndex, out BusinessActivationCandidateState candidate) || candidate == null)
            {
                message = "No business type is selected.";
                return false;
            }

            businessType = candidate.businessType;
            if (!candidate.eligible)
            {
                message = string.IsNullOrWhiteSpace(candidate.reason)
                    ? $"{candidate.displayName} is not available."
                    : candidate.reason;
                return false;
            }

            EnsureMarket();
            RefreshOwnedBuildability();
            OwnedPlotBuildabilityState state = FindOwnedBuildabilityState(plotId);
            if (state == null)
            {
                message = $"Plot {plotId:000} is not tracked as a player-owned plot.";
                return false;
            }

            if (!state.playerOwned)
            {
                message = $"Plot {plotId:000} is not player-owned.";
                return false;
            }

            if (!state.empty)
            {
                message = $"Plot {plotId:000} already has an improved site.";
                return false;
            }

            plot = GetPlot(plotId);
            if (plot == null)
            {
                message = $"Plot {plotId:000} is missing from the generated town.";
                return false;
            }

            if (!plot.bounds.IsValid || plot.frontageCells <= 0)
            {
                message = "Plot has no valid buildable footprint or road frontage.";
                return false;
            }

            IReadOnlyList<BuildingDefinition> options = BuildBusinessShellOptions(plot, businessType);
            if (options.Count <= 0)
            {
                message = $"No shell plan physically fits this plot for {candidate.displayName}.";
                return false;
            }

            definition = options[WrapIndex(shellOptionIndex, options.Count)];
            if (definition == null)
            {
                message = "No business shell definition is selected.";
                return false;
            }

            message = "Business shell plan selected.";
            return true;
        }

        private void AddBuildOptions(TownPlot plot, List<BuildingDefinition> options)
        {
            options.Clear();
            TownGenerationSettings settings = townWorld != null ? townWorld.Settings : null;
            BuildingDefinition[] catalog = settings != null ? settings.buildingCatalog : null;
            if (catalog == null)
            {
                return;
            }

            List<BuildingDefinition> matchingOptions = new();
            List<BuildingDefinition> offRecommendationOptions = new();
            for (int i = 0; i < catalog.Length; i++)
            {
                BuildingDefinition definition = catalog[i];
                if (definition == null || !CanFitBuildingOnPlot(plot, definition, settings))
                {
                    continue;
                }

                if (definition.CanUsePlot(plot.zone))
                {
                    matchingOptions.Add(definition);
                }
                else
                {
                    offRecommendationOptions.Add(definition);
                }
            }

            options.AddRange(matchingOptions);
            options.AddRange(offRecommendationOptions);
        }

        private IReadOnlyList<BuildingDefinition> BuildHouseShellOptions(TownPlot plot)
        {
            List<BuildingDefinition> primaryMatching = new();
            List<BuildingDefinition> primaryOffRecommendation = new();
            List<BuildingDefinition> mixedMatching = new();
            List<BuildingDefinition> mixedOffRecommendation = new();
            TownGenerationSettings settings = townWorld != null ? townWorld.Settings : null;
            BuildingDefinition[] catalog = settings != null ? settings.buildingCatalog : null;
            if (catalog == null)
            {
                return Array.Empty<BuildingDefinition>();
            }

            for (int i = 0; i < catalog.Length; i++)
            {
                BuildingDefinition definition = catalog[i];
                if (definition == null
                    || definition.PrimaryUse == BuildingUseType.Civic
                    || !definition.CanHostHouseholds
                    || !CanFitBuildingOnPlot(plot, definition, settings))
                {
                    continue;
                }

                if (definition.CanHostWorkplace)
                {
                    AddShellOptionByPlotRecommendation(plot, definition, mixedMatching, mixedOffRecommendation);
                }
                else
                {
                    AddShellOptionByPlotRecommendation(plot, definition, primaryMatching, primaryOffRecommendation);
                }
            }

            List<BuildingDefinition> options = new();
            options.AddRange(primaryMatching);
            options.AddRange(primaryOffRecommendation);
            options.AddRange(mixedMatching);
            options.AddRange(mixedOffRecommendation);
            return options;
        }

        private IReadOnlyList<BuildingDefinition> BuildBusinessShellOptions(TownPlot plot, BusinessType businessType)
        {
            List<BuildingDefinition> suitableMatching = new();
            List<BuildingDefinition> suitableOffRecommendation = new();
            TownGenerationSettings settings = townWorld != null ? townWorld.Settings : null;
            BuildingDefinition[] catalog = settings != null ? settings.buildingCatalog : null;
            if (catalog == null)
            {
                return Array.Empty<BuildingDefinition>();
            }

            for (int i = 0; i < catalog.Length; i++)
            {
                BuildingDefinition definition = catalog[i];
                if (definition == null
                    || definition.PrimaryUse == BuildingUseType.Civic
                    || !CanFitBuildingOnPlot(plot, definition, settings))
                {
                    continue;
                }

                if (definition.CanHostWorkplace && definition.IsSuitableForBusiness(businessType))
                {
                    AddShellOptionByPlotRecommendation(plot, definition, suitableMatching, suitableOffRecommendation);
                }
            }

            List<BuildingDefinition> options = new();
            options.AddRange(suitableMatching);
            options.AddRange(suitableOffRecommendation);
            return options;
        }

        private static void AddShellOptionByPlotRecommendation(
            TownPlot plot,
            BuildingDefinition definition,
            List<BuildingDefinition> matchingOptions,
            List<BuildingDefinition> offRecommendationOptions)
        {
            if (definition.CanUsePlot(plot.zone))
            {
                matchingOptions.Add(definition);
            }
            else
            {
                offRecommendationOptions.Add(definition);
            }
        }

        private static bool PlotZoneMatchesBusinessRecommendation(BusinessSiteRequirementDefinition requirements, PlotZone plotZone)
        {
            ReadOnlySpan<PlotZone> zones = requirements != null
                ? requirements.AllowedPlotZones
                : ReadOnlySpan<PlotZone>.Empty;
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

        private static string BuildBusinessAllowedZoneSummary(BusinessSiteRequirementDefinition requirements)
        {
            ReadOnlySpan<PlotZone> zones = requirements != null
                ? requirements.AllowedPlotZones
                : ReadOnlySpan<PlotZone>.Empty;
            if (zones.Length == 0)
            {
                return "any zone";
            }

            StringBuilder builder = new();
            for (int i = 0; i < zones.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append("/");
                }

                builder.Append(zones[i]);
            }

            return builder.ToString();
        }

        private static bool CanFitBuildingOnPlot(TownPlot plot, BuildingDefinition definition, TownGenerationSettings settings)
        {
            if (plot == null || definition == null || settings == null || !plot.bounds.IsValid)
            {
                return false;
            }

            Vector2Int footprint = definition.FootprintSizeCells;
            int usableWidth = Mathf.Max(0, plot.bounds.width);
            int usableDepth = Mathf.Max(0, plot.bounds.depth - settings.buildingSetbackCells);
            return footprint.x <= usableWidth && footprint.y <= usableDepth;
        }

        private void AppendCurrentLandListing(StringBuilder builder)
        {
            if (landListings.Count == 0)
            {
                builder.AppendLine("No land listed.");
                return;
            }

            selectedLandIndex = WrapIndex(selectedLandIndex, landListings.Count);
            AppendListing(builder, landListings[selectedLandIndex], selectedLandIndex, landListings.Count);
        }

        private void AppendCurrentBusinessListing(StringBuilder builder)
        {
            if (businessListings.Count == 0)
            {
                builder.AppendLine("No businesses listed.");
                return;
            }

            selectedBusinessIndex = WrapIndex(selectedBusinessIndex, businessListings.Count);
            AppendListing(builder, businessListings[selectedBusinessIndex], selectedBusinessIndex, businessListings.Count);
        }

        private void AppendListing(StringBuilder builder, AcquisitionListing listing, int index, int count)
        {
            string watchState = IsWatchedListing(listing.listingId) ? "Marked for later" : "Not marked";
            string scouting = BuildScoutingSummary(listing);
            string readiness = BuildBuyerReadinessSummary(listing);
            string leadQuality = BuildLeadQualitySummary(listing, FindDeal(listing.listingId));
            string proof = BuildProofOfFundsSummary(listing, FindDeal(listing.listingId));
            string leadAccess = BuildSelectedLeadAccessSummary(listing, FindDeal(listing.listingId));

            if (listing.kind == AcquisitionListingKind.Business)
            {
                string businessName = !string.IsNullOrWhiteSpace(listing.businessDisplayName) ? listing.businessDisplayName : listing.title;
                string operatorName = !string.IsNullOrWhiteSpace(listing.ownerDisplayName) ? listing.ownerDisplayName : "No operator";
                builder.AppendLine($"Listing {index + 1}/{count}: {businessName}");
                builder.AppendLine($"{FormatMoney(listing.askingPriceCents)} | Operator: {operatorName} | Plot {listing.plotId:000}");
                builder.AppendLine($"Reason: {listing.reason}");
                builder.AppendLine(leadQuality);
                builder.AppendLine(proof);
                builder.AppendLine($"Scouting: {scouting}");
                builder.AppendLine($"Buyer Read: {readiness}");
                builder.AppendLine($"Watchlist: {watchState}");
                builder.AppendLine(leadAccess);
                AppendDealStatus(builder, listing);
                return;
            }

            builder.AppendLine($"Listing {index + 1}/{count}: {listing.title}");
            builder.AppendLine($"{FormatMoney(listing.askingPriceCents)} | {listing.plotZone} | {listing.siteSizeCells.x}x{listing.siteSizeCells.y} | Frontage {listing.frontageCells}");
            builder.AppendLine($"Reason: {listing.reason}");
            builder.AppendLine(leadQuality);
            builder.AppendLine(proof);
            builder.AppendLine($"Scouting: {scouting}");
            builder.AppendLine($"Buyer Read: {readiness}");
            builder.AppendLine($"Watchlist: {watchState}");
            builder.AppendLine(leadAccess);
            AppendDealStatus(builder, listing);
        }

        private void AppendDealStatus(StringBuilder builder, AcquisitionListing listing)
        {
            AcquisitionDealState deal = listing != null ? FindDeal(listing.listingId) : null;
            if (deal == null || deal.stage == AcquisitionDealStage.None)
            {
                builder.AppendLine("Process: not opened | Next: inquire");
                return;
            }

            builder.AppendLine($"Process: {FormatDealStage(deal.stage)} | Next: {GetActionLabelForDeal(deal)}");
            builder.AppendLine($"Seller: {FormatSellerMotive(deal.sellerMotive)} | Pressure {FormatPercent(deal.sellerPressure01)} | Seriousness {FormatBuyerSeriousness(deal.seriousness)} | Proof {FormatProofStatus(deal.proofStatus)} | Earnest {FormatMoney(deal.earnestMoneyCents)} | Option day {deal.optionDeadlineDayIndex}");
            if (!string.IsNullOrWhiteSpace(deal.scoutingSummary))
            {
                builder.AppendLine($"Field Read: {deal.scoutingSummary}");
            }
            if (deal.requiredDiligenceMask != 0)
            {
                builder.AppendLine($"Diligence Layers: {CountDiligenceLayers(deal.completedDiligenceMask)}/{Mathf.Max(1, CountDiligenceLayers(deal.requiredDiligenceMask))} cleared");
            }
            if (deal.diligenceDayIndex >= 0)
            {
                builder.AppendLine($"Diligence: {FormatPercent(deal.diligenceScore01)} | Value {FormatMoney(deal.estimatedValueCents)} | Risk {FormatPercent(deal.closingRisk01)}");
            }

            if (deal.integrationStance != AcquisitionIntegrationStance.None)
            {
                builder.AppendLine($"Post-Close Stance: {FormatIntegrationStance(deal.integrationStance)}");
            }

            if (deal.stage == AcquisitionDealStage.TentativeAgreement)
            {
                builder.AppendLine($"Agreement: {FormatMoney(deal.tentativePriceCents)} | Close by day {deal.closingDeadlineDayIndex} | Financing need {FormatMoney(deal.financingNeedCents)}");
            }

            if (!string.IsNullOrWhiteSpace(deal.statusText))
            {
                builder.AppendLine(deal.statusText);
            }
            if (deal.closingFailureCause != AcquisitionClosingFailureCause.None)
            {
                builder.AppendLine($"Failure Cause: {FormatClosingFailureCause(deal.closingFailureCause)}");
            }
            if (!string.IsNullOrWhiteSpace(deal.quickDiligenceLedger))
            {
                builder.AppendLine("Quick Diligence:");
                builder.AppendLine(deal.quickDiligenceLedger);
            }
        }


        private string BuildScoutingSummary(AcquisitionListing listing)
        {
            if (listing == null)
            {
                return "No scouting read available.";
            }

            int seed = PositiveHash((listing.plotId + 17) * 31 + (listing.buildingId + 29) * 17 + marketSeed);
            if (listing.kind == AcquisitionListingKind.Business)
            {
                string staffing = (seed % 3) switch
                {
                    0 => "staffing looks steady from the street",
                    1 => "staffing looks thin at peak hours",
                    _ => "staffing looks uneven; follow-up recommended"
                };
                string tradeRead = ((seed / 7) % 3) switch
                {
                    0 => "trade rhythm appears routine",
                    1 => "trade rhythm feels quiet for the district",
                    _ => "trade rhythm looks active but somewhat strained"
                };
                return $"{staffing}; {tradeRead}.";
            }

            string parcelRead = (seed % 3) switch
            {
                0 => "frontage reads practical and easy to work",
                1 => "site looks serviceable but may need prep",
                _ => "site reads promising, but the fit will depend on use"
            };
            string pressureRead = ((seed / 5) % 3) switch
            {
                0 => "no obvious outside pressure shows at first glance",
                1 => "local pressure may favor quicker follow-up",
                _ => "the parcel feels worth noting before another buyer moves"
            };
            return $"{parcelRead}; {pressureRead}.";
        }

        private void RefreshDealWorkflowReadouts(AcquisitionListing listing, AcquisitionDealState deal)
        {
            if (deal == null)
            {
                return;
            }

            deal.leadCategory = ResolveLeadCategory(listing, deal);
            if (deal.requiredDiligenceMask == 0)
            {
                deal.requiredDiligenceMask = (int)ResolveRequiredDiligenceLayers(listing);
            }

            deal.leadQualitySummary = BuildLeadQualitySummary(listing, deal);
            deal.proofOfFundsSummary = BuildProofOfFundsSummary(listing, deal);
            deal.diligenceLayerSummary = BuildDiligenceLayerSummary(listing, deal);
            deal.closingRiskSummary = BuildClosingRiskSummary(listing, deal);
        }

        private AcquisitionLeadCategory ResolveLeadCategory(AcquisitionListing listing, AcquisitionDealState deal)
        {
            if (deal != null && deal.leadCategory != AcquisitionLeadCategory.Unset)
            {
                return deal.leadCategory;
            }

            SellerMotive motive = deal != null ? deal.sellerMotive : BuildSellerProfile(listing).motive;
            if (motive == SellerMotive.EstateSale)
            {
                return AcquisitionLeadCategory.EstateSale;
            }

            if (motive == SellerMotive.FinancialDistress || motive == SellerMotive.DebtPressure || motive == SellerMotive.Liquidating)
            {
                return AcquisitionLeadCategory.DistressSale;
            }

            if (motive == SellerMotive.StrategicHoldout || motive == SellerMotive.OwnerOperatorAttachment)
            {
                return AcquisitionLeadCategory.StrategicHoldout;
            }

            if (listing == null)
            {
                return AcquisitionLeadCategory.PublicListing;
            }

            return listing.source switch
            {
                AcquisitionListingSource.ServiceShortage => AcquisitionLeadCategory.RumorLead,
                AcquisitionListingSource.OwnerTurnover => AcquisitionLeadCategory.QuietOpportunity,
                AcquisitionListingSource.PressureRelease => AcquisitionLeadCategory.QuietOpportunity,
                AcquisitionListingSource.RivalAction => AcquisitionLeadCategory.RumorLead,
                _ => AcquisitionLeadCategory.PublicListing
            };
        }

        private string BuildLeadQualitySummary(AcquisitionListing listing, AcquisitionDealState deal)
        {
            AcquisitionLeadCategory category = ResolveLeadCategory(listing, deal);
            float pressure = Mathf.Clamp01(deal != null ? deal.sellerPressure01 : listing != null ? listing.pressure01 : 0.35f);
            string categoryLabel = FormatLeadCategory(category);
            string quality = pressure >= 0.68f ? "hot" : pressure >= 0.4f ? "workable" : "patient";
            string source = listing != null && !string.IsNullOrWhiteSpace(listing.sourceReason)
                ? listing.sourceReason
                : listing != null ? FormatListingSource(listing.source) : "market lead";
            return $"Lead: {categoryLabel}; {quality} quality; {source}.";
        }

        private string BuildProofOfFundsSummary(AcquisitionListing listing, AcquisitionDealState deal)
        {
            int price = deal != null && deal.tentativePriceCents > 0
                ? deal.tentativePriceCents
                : Mathf.Max(0, listing != null ? listing.askingPriceCents : 0);
            int earnest = Mathf.Max(0, deal != null ? deal.earnestMoneyCents : 0);
            int availableCash = Mathf.Max(0, GetAvailableCashCents());
            int visibleFunds = Mathf.Min(price, availableCash + earnest);
            float proofRatio = price <= 0 ? 0f : Mathf.Clamp01(visibleFunds / (float)Mathf.Max(1, price));
            bool lenderReady = playerDebtManager != null && playerDebtManager.CanSubmitApplication;
            if (deal != null && deal.proofRequired)
            {
                float requiredRatio = ResolveRequiredProofRatio(listing, deal);
                return deal.proofStatus switch
                {
                    AcquisitionProofStatus.Verified => $"Proof: accepted; {FormatMoney(visibleFunds)} shown against {FormatMoney(price)} with seller threshold {FormatPercent(requiredRatio)}.",
                    AcquisitionProofStatus.Presented => $"Proof: shown but not yet satisfactory; visible coverage {FormatPercent(proofRatio)} against seller threshold {FormatPercent(requiredRatio)}.",
                    _ => $"Proof: seller requires proof before earnest; visible coverage {FormatPercent(proofRatio)} against seller threshold {FormatPercent(requiredRatio)}."
                };
            }

            if (proofRatio >= 1f)
            {
                return $"Proof: cash-clear file; visible funds cover {FormatMoney(price)}.";
            }

            if (proofRatio >= 0.25f && lenderReady)
            {
                return $"Proof: financeable file; {FormatMoney(visibleFunds)} visible against {FormatMoney(price)} with lender access.";
            }

            if (proofRatio >= 0.1f || lenderReady)
            {
                return $"Proof: thin but discussable; seller may require earnest discipline before terms.";
            }

            return "Proof: weak file; treat as watchlist or inquiry only until cash or lender posture improves.";
        }

        private string BuildDiligenceLayerSummary(AcquisitionListing listing, AcquisitionDealState deal)
        {
            if (deal == null || deal.stage == AcquisitionDealStage.None)
            {
                return "Diligence: street read only; no formal checks opened.";
            }

            int layers = CountRevealedDiligenceLayers(deal.revealedDiligenceMask);
            int requiredLayers = CountDiligenceLayers(deal.requiredDiligenceMask);
            int completedLayers = CountDiligenceLayers(deal.completedDiligenceMask);
            if (deal.diligenceDayIndex >= 0 || deal.stage >= AcquisitionDealStage.DiligenceComplete)
            {
                string value = deal.estimatedValueCents > 0
                    ? $"valuation band {FormatMoney(deal.valuationBandLowCents)}-{FormatMoney(deal.valuationBandHighCents)}"
                    : "valuation band unresolved";
                return $"Diligence: formal review complete; {completedLayers}/{Mathf.Max(1, requiredLayers)} layers cleared; {value}; confidence {FormatPercent(deal.diligenceScore01)}; quick layers {layers}.";
            }

            if (completedLayers > 0)
            {
                return $"Diligence: {completedLayers}/{Mathf.Max(1, requiredLayers)} formal layer(s) cleared; remaining {BuildRemainingDiligenceLayerSummary(deal)}.";
            }

            return listing != null && listing.kind == AcquisitionListingKind.Business
                ? "Diligence: inspect condition, staff reliability, supplier exposure, and ledger value before terms."
                : "Diligence: inspect frontage, title fit, site prep, and practical build value before terms.";
        }

        private string BuildClosingRiskSummary(AcquisitionListing listing, AcquisitionDealState deal)
        {
            float risk = Mathf.Clamp01(deal != null ? deal.closingRisk01 : 0.18f + (listing != null && listing.aiEligible ? 0.08f : 0f));
            string band = risk >= 0.65f ? "high" : risk >= 0.38f ? "material" : "controlled";
            AcquisitionClosingFailureCause primaryCause = ResolvePrimaryClosingRiskCause(listing, deal);
            string driver = primaryCause != AcquisitionClosingFailureCause.None
                ? FormatClosingFailureCause(primaryCause).ToLowerInvariant()
                : deal != null && deal.financingNeedCents > 0
                    ? $"financing need {FormatMoney(deal.financingNeedCents)}"
                    : "cash closing posture";
            if (deal != null && deal.stage == AcquisitionDealStage.TentativeAgreement && deal.closingDeadlineDayIndex >= 0)
            {
                driver += $"; close by day {deal.closingDeadlineDayIndex}";
            }

            return $"Closing risk: {band} ({FormatPercent(risk)}); {driver}.";
        }

        private static bool NeedsProofOfFunds(AcquisitionDealState deal)
        {
            return deal != null
                && deal.proofRequired
                && deal.proofStatus < AcquisitionProofStatus.Verified;
        }

        private static bool IsFormalDiligenceComplete(AcquisitionDealState deal)
        {
            return deal != null
                && deal.requiredDiligenceMask != 0
                && (deal.completedDiligenceMask & deal.requiredDiligenceMask) == deal.requiredDiligenceMask;
        }

        private AcquisitionProofStatus EvaluateProofStatus(
            AcquisitionListing listing,
            AcquisitionDealState deal,
            out float proofRatio,
            out float requiredRatio,
            out bool lenderReady)
        {
            int price = deal != null && deal.tentativePriceCents > 0
                ? deal.tentativePriceCents
                : Mathf.Max(0, listing != null ? listing.askingPriceCents : 0);
            int visibleFunds = Mathf.Max(0, GetAvailableCashCents()) + Mathf.Max(0, deal != null ? deal.earnestMoneyCents : 0);
            proofRatio = price <= 0 ? 1f : Mathf.Clamp01(visibleFunds / (float)Mathf.Max(1, price));
            requiredRatio = ResolveRequiredProofRatio(listing, deal);
            lenderReady = playerDebtManager != null && playerDebtManager.CanSubmitApplication;

            if (!NeedsProofOfFunds(deal))
            {
                return AcquisitionProofStatus.Verified;
            }

            if (proofRatio >= requiredRatio || (lenderReady && proofRatio >= Mathf.Max(0.1f, requiredRatio * 0.6f)))
            {
                return AcquisitionProofStatus.Verified;
            }

            if (proofRatio >= Mathf.Max(0.08f, requiredRatio * 0.5f) || lenderReady)
            {
                return AcquisitionProofStatus.Presented;
            }

            return AcquisitionProofStatus.Requested;
        }

        private bool ShouldRequireProofOfFunds(
            AcquisitionListing listing,
            SellerProfile seller,
            SellerPressureResult pressure)
        {
            if (listing == null)
            {
                return false;
            }

            if (listing.kind == AcquisitionListingKind.Business)
            {
                return true;
            }

            if (seller.motive == SellerMotive.StrategicHoldout
                || seller.motive == SellerMotive.OwnerOperatorAttachment
                || seller.motive == SellerMotive.EstateSale)
            {
                return true;
            }

            return listing.askingPriceCents >= 650000
                || pressure.RelationshipSensitivity01 >= 0.58f;
        }

        private float ResolveRequiredProofRatio(AcquisitionListing listing, AcquisitionDealState deal)
        {
            float ratio = listing != null && listing.kind == AcquisitionListingKind.Business ? 0.16f : 0.1f;
            if (deal != null)
            {
                if (deal.leadCategory == AcquisitionLeadCategory.QuietOpportunity)
                {
                    ratio += 0.04f;
                }

                if (deal.leadCategory == AcquisitionLeadCategory.StrategicHoldout
                    || deal.sellerMotive == SellerMotive.StrategicHoldout
                    || deal.sellerMotive == SellerMotive.OwnerOperatorAttachment)
                {
                    ratio += 0.08f;
                }

                ratio += deal.sellerRelationshipSensitivity01 * 0.08f;
            }

            return Mathf.Clamp(ratio, 0.1f, 0.35f);
        }

        private AcquisitionDiligenceLayer ResolveRequiredDiligenceLayers(AcquisitionListing listing)
        {
            if (listing == null)
            {
                return AcquisitionDiligenceLayer.Title;
            }

            if (listing.kind == AcquisitionListingKind.Business)
            {
                return AcquisitionDiligenceLayer.Title
                    | AcquisitionDiligenceLayer.SiteCondition
                    | AcquisitionDiligenceLayer.Operations
                    | AcquisitionDiligenceLayer.BooksAndReceivables;
            }

            return AcquisitionDiligenceLayer.Title
                | AcquisitionDiligenceLayer.SiteCondition
                | AcquisitionDiligenceLayer.AccessAndFrontage;
        }

        private bool AdvanceFormalDiligenceLayer(AcquisitionListing listing, AcquisitionDealState deal, out string message)
        {
            message = string.Empty;
            if (listing == null || deal == null)
            {
                message = "Diligence file is unavailable.";
                return false;
            }

            AcquisitionDiligenceLayer nextLayer = GetNextIncompleteDiligenceLayer(deal);
            if (nextLayer == AcquisitionDiligenceLayer.None)
            {
                message = "All required diligence layers are already cleared.";
                return true;
            }

            deal.completedDiligenceMask |= (int)nextLayer;
            deal.lastQuickDiligenceDayIndex = GetCurrentDayIndex();
            string layerLine = BuildFormalDiligenceLedgerLine(listing, nextLayer);
            deal.quickDiligenceLedger = AppendLedgerLine(deal.quickDiligenceLedger, $"Day {deal.lastQuickDiligenceDayIndex}: {layerLine}");
            deal.revealedDiligenceMask |= MapDiligenceLayerToQuickMask(nextLayer);
            ApplyDiligenceLayerEffects(listing, deal, nextLayer);
            RefreshDealWorkflowReadouts(listing, deal);

            if (IsFormalDiligenceComplete(deal))
            {
                message = $"{layerLine} Formal diligence layers are now complete.";
                return true;
            }

            deal.statusText = $"{layerLine} Remaining: {BuildRemainingDiligenceLayerSummary(deal)}.";
            marketStatus = deal.statusText;
            lastPurchaseSummary = deal.statusText;
            message = $"{deal.statusText} Next: continue diligence.";
            return true;
        }

        private static AcquisitionDiligenceLayer GetNextIncompleteDiligenceLayer(AcquisitionDealState deal)
        {
            if (deal == null)
            {
                return AcquisitionDiligenceLayer.None;
            }

            AcquisitionDiligenceLayer[] ordered =
            {
                AcquisitionDiligenceLayer.Title,
                AcquisitionDiligenceLayer.SiteCondition,
                AcquisitionDiligenceLayer.AccessAndFrontage,
                AcquisitionDiligenceLayer.Operations,
                AcquisitionDiligenceLayer.BooksAndReceivables,
                AcquisitionDiligenceLayer.SupplierExposure
            };

            for (int i = 0; i < ordered.Length; i++)
            {
                int bit = (int)ordered[i];
                if ((deal.requiredDiligenceMask & bit) != 0 && (deal.completedDiligenceMask & bit) == 0)
                {
                    return ordered[i];
                }
            }

            return AcquisitionDiligenceLayer.None;
        }

        private string BuildFormalDiligenceLedgerLine(AcquisitionListing listing, AcquisitionDiligenceLayer layer)
        {
            bool business = listing != null && listing.kind == AcquisitionListingKind.Business;
            return layer switch
            {
                AcquisitionDiligenceLayer.Title => "Title chain and seller authority checked.",
                AcquisitionDiligenceLayer.SiteCondition => business
                    ? "Building condition and repair burden checked."
                    : "Ground condition and site prep burden checked.",
                AcquisitionDiligenceLayer.AccessAndFrontage => "Frontage, access, and practical fit checked.",
                AcquisitionDiligenceLayer.Operations => "Staffing rhythm and operating reliability checked.",
                AcquisitionDiligenceLayer.BooksAndReceivables => "Books, receivables, and working-cash posture checked.",
                AcquisitionDiligenceLayer.SupplierExposure => "Supplier concentration and freight exposure checked.",
                _ => "Formal diligence layer checked."
            };
        }

        private void ApplyDiligenceLayerEffects(AcquisitionListing listing, AcquisitionDealState deal, AcquisitionDiligenceLayer layer)
        {
            if (deal == null)
            {
                return;
            }

            switch (layer)
            {
                case AcquisitionDiligenceLayer.Title:
                    deal.closingRisk01 = Mathf.Clamp01(deal.closingRisk01 - 0.04f);
                    break;
                case AcquisitionDiligenceLayer.SiteCondition:
                    deal.closingRisk01 = Mathf.Clamp01(deal.closingRisk01 - 0.02f);
                    break;
                case AcquisitionDiligenceLayer.AccessAndFrontage:
                    deal.closingRisk01 = Mathf.Clamp01(deal.closingRisk01 - 0.015f);
                    break;
                case AcquisitionDiligenceLayer.Operations:
                    deal.closingRisk01 = Mathf.Clamp01(deal.closingRisk01 - 0.02f);
                    break;
                case AcquisitionDiligenceLayer.BooksAndReceivables:
                    deal.closingRisk01 = Mathf.Clamp01(deal.closingRisk01 - 0.03f);
                    break;
                case AcquisitionDiligenceLayer.SupplierExposure:
                    deal.closingRisk01 = Mathf.Clamp01(deal.closingRisk01 - 0.015f);
                    break;
            }
        }

        private static int MapDiligenceLayerToQuickMask(AcquisitionDiligenceLayer layer)
        {
            return layer switch
            {
                AcquisitionDiligenceLayer.SiteCondition => 1 << (int)AcquisitionDiligenceClueKind.VisibleCondition,
                AcquisitionDiligenceLayer.Operations => 1 << (int)AcquisitionDiligenceClueKind.Staffing,
                AcquisitionDiligenceLayer.SupplierExposure => 1 << (int)AcquisitionDiligenceClueKind.SupplierWeakness,
                AcquisitionDiligenceLayer.AccessAndFrontage => 1 << (int)AcquisitionDiligenceClueKind.DemandFit,
                AcquisitionDiligenceLayer.BooksAndReceivables => 1 << (int)AcquisitionDiligenceClueKind.RoughValue,
                _ => 1 << (int)AcquisitionDiligenceClueKind.Reliability
            };
        }

        private static int CountDiligenceLayers(int mask)
        {
            int count = 0;
            AcquisitionDiligenceLayer[] ordered =
            {
                AcquisitionDiligenceLayer.Title,
                AcquisitionDiligenceLayer.SiteCondition,
                AcquisitionDiligenceLayer.AccessAndFrontage,
                AcquisitionDiligenceLayer.Operations,
                AcquisitionDiligenceLayer.BooksAndReceivables,
                AcquisitionDiligenceLayer.SupplierExposure
            };

            for (int i = 0; i < ordered.Length; i++)
            {
                if ((mask & (int)ordered[i]) != 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static string BuildRemainingDiligenceLayerSummary(AcquisitionDealState deal)
        {
            if (deal == null)
            {
                return "none";
            }

            List<string> remaining = new();
            AcquisitionDiligenceLayer[] ordered =
            {
                AcquisitionDiligenceLayer.Title,
                AcquisitionDiligenceLayer.SiteCondition,
                AcquisitionDiligenceLayer.AccessAndFrontage,
                AcquisitionDiligenceLayer.Operations,
                AcquisitionDiligenceLayer.BooksAndReceivables,
                AcquisitionDiligenceLayer.SupplierExposure
            };

            for (int i = 0; i < ordered.Length; i++)
            {
                int bit = (int)ordered[i];
                if ((deal.requiredDiligenceMask & bit) != 0 && (deal.completedDiligenceMask & bit) == 0)
                {
                    remaining.Add(FormatDiligenceLayer(ordered[i]));
                }
            }

            return remaining.Count <= 0 ? "none" : string.Join(", ", remaining);
        }

        private static int CountRevealedDiligenceLayers(int mask)
        {
            int count = 0;
            for (int i = 0; i < 30; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static string FormatLeadCategory(AcquisitionLeadCategory category)
        {
            return category switch
            {
                AcquisitionLeadCategory.QuietOpportunity => "quiet opportunity",
                AcquisitionLeadCategory.RumorLead => "rumor lead",
                AcquisitionLeadCategory.EstateSale => "estate sale",
                AcquisitionLeadCategory.DistressSale => "distress sale",
                AcquisitionLeadCategory.StrategicHoldout => "strategic holdout",
                _ => "public listing"
            };
        }

        private static string FormatListingSource(AcquisitionListingSource source)
        {
            return source switch
            {
                AcquisitionListingSource.DevelopmentInventory => "development inventory",
                AcquisitionListingSource.PressureRelease => "pressure release",
                AcquisitionListingSource.OwnerTurnover => "owner turnover",
                AcquisitionListingSource.ServiceShortage => "service shortage",
                AcquisitionListingSource.RivalAction => "rival action",
                _ => "public market board"
            };
        }

        private AcquisitionClosingFailureCause ResolvePrimaryClosingRiskCause(AcquisitionListing listing, AcquisitionDealState deal)
        {
            if (deal == null)
            {
                return AcquisitionClosingFailureCause.None;
            }

            if (deal.financingNeedCents > 0 || deal.proofStatus < AcquisitionProofStatus.Verified)
            {
                return AcquisitionClosingFailureCause.LenderRetreat;
            }

            if ((deal.requiredDiligenceMask & (int)AcquisitionDiligenceLayer.Title) != 0
                && deal.diligenceScore01 < 0.55f
                && listing != null
                && listing.kind == AcquisitionListingKind.Land)
            {
                return AcquisitionClosingFailureCause.TitleDefect;
            }

            if (listing != null && listing.aiEligible && listing.pressure01 >= 0.65f)
            {
                return AcquisitionClosingFailureCause.RivalPressure;
            }

            if (deal.sellerRelationshipSensitivity01 >= 0.65f || deal.sellerMotive == SellerMotive.OwnerOperatorAttachment)
            {
                return AcquisitionClosingFailureCause.SellerWithdrew;
            }

            if (deal.diligenceScore01 < 0.58f || (listing != null && deal.estimatedValueCents > 0 && listing.askingPriceCents > deal.valuationBandHighCents))
            {
                return AcquisitionClosingFailureCause.AdverseDiligence;
            }

            return deal.closingRisk01 >= 0.55f
                ? AcquisitionClosingFailureCause.SellerWithdrew
                : AcquisitionClosingFailureCause.None;
        }

        private string BuildClosingFailureSummary(
            AcquisitionListing listing,
            AcquisitionDealState deal,
            AcquisitionClosingFailureCause cause)
        {
            return cause switch
            {
                AcquisitionClosingFailureCause.LenderRetreat => "Closing failed during final review: the lender withdrew from the file when proof and financing posture did not hold.",
                AcquisitionClosingFailureCause.TitleDefect => "Closing failed during final review: a title defect or authority defect surfaced before deed transfer.",
                AcquisitionClosingFailureCause.SellerWithdrew => "Closing failed during final review: the seller withdrew rather than carry the file to signing.",
                AcquisitionClosingFailureCause.RivalPressure => "Closing failed during final review: a rival offer displaced the file before signatures were complete.",
                AcquisitionClosingFailureCause.AdverseDiligence => "Closing failed during final review: adverse diligence facts changed the file too late to close cleanly.",
                _ => "Closing failed during final review."
            };
        }

        private static string FormatDiligenceLayer(AcquisitionDiligenceLayer layer)
        {
            return layer switch
            {
                AcquisitionDiligenceLayer.Title => "title",
                AcquisitionDiligenceLayer.SiteCondition => "condition",
                AcquisitionDiligenceLayer.AccessAndFrontage => "frontage/access",
                AcquisitionDiligenceLayer.Operations => "operations",
                AcquisitionDiligenceLayer.BooksAndReceivables => "books",
                AcquisitionDiligenceLayer.SupplierExposure => "suppliers",
                _ => "diligence"
            };
        }

        private static string FormatClosingFailureCause(AcquisitionClosingFailureCause cause)
        {
            return cause switch
            {
                AcquisitionClosingFailureCause.LenderRetreat => "Lender retreat",
                AcquisitionClosingFailureCause.TitleDefect => "Title defect",
                AcquisitionClosingFailureCause.SellerWithdrew => "Seller withdrew",
                AcquisitionClosingFailureCause.RivalPressure => "Rival pressure",
                AcquisitionClosingFailureCause.AdverseDiligence => "Adverse diligence",
                _ => "No named failure"
            };
        }

        private string BuildBuyerReadinessSummary(AcquisitionListing listing)
        {
            if (listing == null)
            {
                return "No buyer read available.";
            }

            int asking = Mathf.Max(0, listing.askingPriceCents);
            int availableCash = Mathf.Max(0, GetAvailableCashCents());
            bool canUseDebt = playerDebtManager != null && playerDebtManager.CanSubmitApplication;
            float ownerHeadline = GetOwnerHeadlineReputation01();
            float localTrust = GetOwnerLocalTrust01();
            float operationalReliability = GetOwnerOperationalReliability01();
            if (availableCash >= asking && asking > 0)
            {
                return ownerHeadline >= 0.38f
                    ? "cash position is strong enough to be taken seriously now"
                    : "cash position is strong, but owner standing still reads mixed";
            }

            if (canUseDebt && availableCash >= Mathf.Max(500, asking / 10))
            {
                return ownerHeadline >= 0.56f && operationalReliability >= 0.5f
                    ? "cash is light, but financing and owner standing should support a serious move"
                    : "cash is light; financing exists, but owner standing still needs proof";
            }

            if (availableCash > 0 || ownerHeadline >= 0.55f || localTrust >= 0.58f)
            {
                return localTrust >= 0.58f
                    ? "interest is credible, but cash looks thin for fast leverage"
                    : "interest is visible, but local confidence is still thin";
            }

            return "cash position is weak; this is better treated as a watchlist lead";
        }

        private AcquisitionBuyerSeriousness EvaluateBuyerSeriousness(AcquisitionListing listing, int financingNeedCents, int availableCashCents, bool watched)
        {
            int asking = listing != null ? Mathf.Max(1, listing.askingPriceCents) : 1;
            float ownerHeadline = GetOwnerHeadlineReputation01();
            float dealTrust = playerDebtManager != null && playerDebtManager.Reputation != null
                ? Mathf.Clamp01(playerDebtManager.Reputation.dealTrust01)
                : ownerHeadline;
            float localTrust = GetOwnerLocalTrust01();
            float operationalReliability = GetOwnerOperationalReliability01();
            bool standingReady = ownerHeadline >= 0.38f && localTrust >= 0.3f;
            bool standingActive = ownerHeadline >= 0.56f && dealTrust >= 0.58f && operationalReliability >= 0.5f;

            if (availableCashCents >= asking)
            {
                return standingReady
                    ? AcquisitionBuyerSeriousness.Ready
                    : AcquisitionBuyerSeriousness.Active;
            }

            if (playerDebtManager != null && playerDebtManager.CanSubmitApplication && availableCashCents >= Mathf.Max(500, asking / 10))
            {
                return standingActive
                    ? AcquisitionBuyerSeriousness.Active
                    : AcquisitionBuyerSeriousness.Exploring;
            }

            if (watched || availableCashCents > 0 || financingNeedCents < asking || ownerHeadline >= 0.62f)
            {
                return ownerHeadline >= 0.72f && localTrust >= 0.64f && availableCashCents >= Mathf.Max(250, asking / 20)
                    ? AcquisitionBuyerSeriousness.Active
                    : AcquisitionBuyerSeriousness.Exploring;
            }

            return AcquisitionBuyerSeriousness.Watching;
        }

        private static AcquisitionBuyerSeriousness EscalateSeriousness(AcquisitionBuyerSeriousness current, AcquisitionBuyerSeriousness target)
        {
            return target > current ? target : current;
        }

        private static AcquisitionBuyerSeriousness RelaxSeriousness(AcquisitionBuyerSeriousness current, AcquisitionBuyerSeriousness floor)
        {
            int lowered = Mathf.Max((int)floor, (int)current - 1);
            return (AcquisitionBuyerSeriousness)lowered;
        }

        private static AcquisitionBuyerSeriousness NextSeriousness(AcquisitionBuyerSeriousness seriousness)
        {
            return seriousness switch
            {
                AcquisitionBuyerSeriousness.Watching => AcquisitionBuyerSeriousness.Exploring,
                AcquisitionBuyerSeriousness.Exploring => AcquisitionBuyerSeriousness.Active,
                AcquisitionBuyerSeriousness.Active => AcquisitionBuyerSeriousness.Ready,
                _ => AcquisitionBuyerSeriousness.Watching
            };
        }

        private AcquisitionIntegrationStance RecommendIntegrationStance(AcquisitionListing listing, AcquisitionDealState deal)
        {
            if (listing == null || deal == null)
            {
                return AcquisitionIntegrationStance.None;
            }

            if (listing.kind == AcquisitionListingKind.Land)
            {
                return AcquisitionIntegrationStance.Expand;
            }

            if (deal.diligenceScore01 < 0.45f || deal.closingRisk01 > 0.45f)
            {
                return AcquisitionIntegrationStance.Stabilize;
            }

            if (deal.estimatedValueCents > 0 && listing.askingPriceCents > deal.estimatedValueCents)
            {
                return AcquisitionIntegrationStance.Repair;
            }

            return listing.businessType switch
            {
                BusinessType.BoardingHouse => AcquisitionIntegrationStance.Expand,
                BusinessType.LumberYard => AcquisitionIntegrationStance.Expand,
                BusinessType.GeneralStore => AcquisitionIntegrationStance.Stabilize,
                _ => AcquisitionIntegrationStance.Reposition
            };
        }

        private string BuildSelectedFundingSummary(AcquisitionListing listing, AcquisitionDealState deal)
        {
            int askingPriceCents = deal != null && deal.tentativePriceCents > 0
                ? deal.tentativePriceCents
                : Mathf.Max(0, listing != null ? listing.askingPriceCents : 0);
            int earnestCents = deal != null ? Mathf.Max(0, deal.earnestMoneyCents) : 0;
            int financingNeedCents = deal != null
                ? Mathf.Max(0, deal.financingNeedCents)
                : Mathf.Max(0, askingPriceCents - GetAvailableCashCents());

            if (playerPortfolio != null)
            {
                return playerPortfolio.BuildAcquisitionFundingSummary(askingPriceCents, earnestCents, financingNeedCents);
            }

            return $"Funding: asking {FormatMoney(askingPriceCents)} | earnest {FormatMoney(earnestCents)} | financing need {FormatMoney(financingNeedCents)}.";
        }

        private static string FormatBuyerSeriousness(AcquisitionBuyerSeriousness seriousness)
        {
            return seriousness switch
            {
                AcquisitionBuyerSeriousness.Watching => "watching",
                AcquisitionBuyerSeriousness.Exploring => "exploring",
                AcquisitionBuyerSeriousness.Active => "active",
                AcquisitionBuyerSeriousness.Ready => "ready",
                _ => "exploring"
            };
        }

        private static string FormatProofStatus(AcquisitionProofStatus status)
        {
            return status switch
            {
                AcquisitionProofStatus.Requested => "requested",
                AcquisitionProofStatus.Presented => "presented",
                AcquisitionProofStatus.Verified => "verified",
                _ => "not requested"
            };
        }

        private static string FormatIntegrationStance(AcquisitionIntegrationStance stance)
        {
            return stance switch
            {
                AcquisitionIntegrationStance.Stabilize => "stabilize",
                AcquisitionIntegrationStance.Repair => "repair / refurbish",
                AcquisitionIntegrationStance.Reposition => "reposition",
                AcquisitionIntegrationStance.Expand => "expand / integrate",
                _ => "not set"
            };
        }

        private static AcquisitionIntegrationStance NextIntegrationStance(AcquisitionIntegrationStance stance)
        {
            return stance switch
            {
                AcquisitionIntegrationStance.None => AcquisitionIntegrationStance.Stabilize,
                AcquisitionIntegrationStance.Stabilize => AcquisitionIntegrationStance.Repair,
                AcquisitionIntegrationStance.Repair => AcquisitionIntegrationStance.Reposition,
                AcquisitionIntegrationStance.Reposition => AcquisitionIntegrationStance.Expand,
                _ => AcquisitionIntegrationStance.None
            };
        }

        private static string GetListingDisplayName(AcquisitionListing listing)
        {
            if (listing == null)
            {
                return "Listing";
            }

            if (listing.kind == AcquisitionListingKind.Business)
            {
                if (!string.IsNullOrWhiteSpace(listing.businessDisplayName))
                {
                    return listing.businessDisplayName;
                }

                if (!string.IsNullOrWhiteSpace(listing.title))
                {
                    return listing.title;
                }

                return "Business listing";
            }

            return !string.IsNullOrWhiteSpace(listing.title) ? listing.title : "Land listing";
        }

        private bool IsWatchedListing(string listingId)
        {
            if (string.IsNullOrWhiteSpace(listingId))
            {
                return false;
            }

            for (int i = 0; i < watchedListingIds.Count; i++)
            {
                if (string.Equals(watchedListingIds[i], listingId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void AddWatchedListingId(string listingId)
        {
            if (string.IsNullOrWhiteSpace(listingId) || IsWatchedListing(listingId))
            {
                return;
            }

            watchedListingIds.Add(listingId);
        }

        private void RemoveWatchedListingId(string listingId)
        {
            if (string.IsNullOrWhiteSpace(listingId))
            {
                return;
            }

            for (int i = watchedListingIds.Count - 1; i >= 0; i--)
            {
                if (string.Equals(watchedListingIds[i], listingId, StringComparison.OrdinalIgnoreCase))
                {
                    watchedListingIds.RemoveAt(i);
                }
            }
        }

        private static string FormatDealStage(AcquisitionDealStage stage)
        {
            return stage switch
            {
                AcquisitionDealStage.Inquiry => "Inquiry",
                AcquisitionDealStage.EarnestCommitted => "Earnest committed",
                AcquisitionDealStage.DiligenceComplete => "Diligence complete",
                AcquisitionDealStage.TentativeAgreement => "Tentative agreement",
                AcquisitionDealStage.Closed => "Closed",
                AcquisitionDealStage.Failed => "Failed",
                _ => "Not contacted"
            };
        }

        private static string GetActionLabelForDeal(AcquisitionDealState deal)
        {
            if (deal == null || deal.stage == AcquisitionDealStage.None)
            {
                return "Inquire";
            }

            if (deal.stage == AcquisitionDealStage.Failed)
            {
                return IsFailedDealReadyToReopen(deal) ? "Reopen inquiry" : "Review failure";
            }

            return deal.stage switch
            {
                AcquisitionDealStage.Inquiry => NeedsProofOfFunds(deal) ? "Present proof" : "Commit earnest",
                AcquisitionDealStage.EarnestCommitted => IsFormalDiligenceComplete(deal) ? "Agree terms" : "Continue diligence",
                AcquisitionDealStage.DiligenceComplete => "Agree terms",
                AcquisitionDealStage.TentativeAgreement => "Close",
                AcquisitionDealStage.Closed => "Closed",
                _ => "Inquire"
            };
        }

        private static string BuildConversationActionLabel(AcquisitionDealState deal)
        {
            if (deal == null || deal.stage == AcquisitionDealStage.None)
            {
                return "Open Inquiry";
            }

            if (deal.stage == AcquisitionDealStage.Failed)
            {
                return IsFailedDealReadyToReopen(deal) ? "Reopen Inquiry" : "Review Failure";
            }

            return deal.stage switch
            {
                AcquisitionDealStage.Inquiry => NeedsProofOfFunds(deal) ? "Present Proof of Funds" : "Commit Earnest",
                AcquisitionDealStage.EarnestCommitted => IsFormalDiligenceComplete(deal) ? "Agree Terms" : "Continue Diligence",
                AcquisitionDealStage.DiligenceComplete => "Agree Terms",
                AcquisitionDealStage.TentativeAgreement => "Close Acquisition",
                AcquisitionDealStage.Closed => "Closed",
                _ => "Open Inquiry"
            };
        }

        private int GetAvailableCashCents()
        {
            return playerPortfolio != null ? playerPortfolio.OwnerCashCents : 0;
        }

        private static int WrapIndex(int index, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            if (index < 0)
            {
                return count - 1;
            }

            return index % count;
        }

        private static string FormatMoney(int cents)
        {
            return "$" + (cents / 100f).ToString("N2");
        }
    }
}
