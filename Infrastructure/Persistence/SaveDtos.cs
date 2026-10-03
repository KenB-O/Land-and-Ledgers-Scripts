using System;
using System.Collections.Generic;
using LandLedgers.Civic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Valuation;
using LandLedgers.MVP;
using LandLedgers.Pathing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Reputation;
using LandLedgers.Time;
using LandLedgers.World;

namespace LandLedgers.Persistence
{
    [Serializable]
    public sealed class LandLedgersSaveGameDto
    {
        public SaveManifestDto manifest = new();
        public MigrationManifestDto migrationManifest;
        public WorldSaveDto world = new();
        public TimeSaveDto time = new();
        public PopulationSaveDto population = new();
        public BusinessPortfolioSaveDto businesses = new();
        public AcquisitionSaveDto acquisition = new();
        public CivicSaveDto civic = new();
        public PlayerDebtSaveDto debt = new();
        public PlayerPortfolioSaveDto portfolio = new();
        public FirstSessionGuidanceSaveDto firstSessionGuidance = new();
        /// <summary>
        /// HF-1: universal per-kind entity-ID cursors (EntityIdSaveAdapter). Legacy
        /// nextPersonId/nextHouseholdId/nextBuildingId fields remain the authority for
        /// those kinds; this list additionally carries every other kind's cursor.
        /// </summary>
        public List<EntityIdCursorDto> entityIdCursors = new();
    }

    [Serializable]
    public sealed class MigrationManifestDto
    {
        public int schemaVersion;
        public int originalSchemaVersion;
        public string migrationRunId = string.Empty;
        public List<MigrationStepRecordDto> appliedSteps = new();
    }

    [Serializable]
    public sealed class MigrationStepRecordDto
    {
        public string stepId = string.Empty;
        public string appliedAtUtc = string.Empty;
        public int fromVersion;
        public int toVersion;
    }

    [Serializable]
    public sealed class SaveManifestDto
    {
        public int formatVersion = 1;
        public string saveId = string.Empty;
        public string savedAtUtc = string.Empty;
        public string sceneName = string.Empty;
        public string displayName = string.Empty;
        public int worldSeed;
        public int absoluteDayIndex;
        public int cashCents;
        public int liquidCashCents;
        public int netWorthCents;
        public int businessCashCents;
        public int debtLiabilityCents;
        public int watchlistedAcquisitionCount;
        public int activeAcquisitionCount;
        public int tentativeAcquisitionCount;
        public int ownedLandCount;
        public int ownedBusinessCount;
        public string wealthHeadline = string.Empty;
        public string liquidityPressureSummary = string.Empty;
        public string acquisitionReadinessHeadline = string.Empty;
        public string acquisitionProcessLedger = string.Empty;
        public string acquisitionDecisionClimate = string.Empty;
        public string acquisitionActionChecklist = string.Empty;
        public string acquisitionWeeklyReview = string.Empty;
        public string selectedLeadReadiness = string.Empty;
        public string townSettlementHeadline = string.Empty;
        public string townCommerceHeadline = string.Empty;
        public string townCommerceLedger = string.Empty;
        public string townServicePressureHeadline = string.Empty;
        public string townDecisionClimate = string.Empty;
        public string ownerCommitmentClimate = string.Empty;
        public string ownerExecutionLoad = string.Empty;
        public string selectedLeadRunway = string.Empty;
        public string selectedLeadExecutionLoad = string.Empty;
        public string saveIntegritySummary = string.Empty;
        public int saveIntegrityWarningCount;
        public int saveIntegrityFatalCount;
    }

    [Serializable]
    public sealed class WorldSaveDto
    {
        public string settingsName = string.Empty;
        public int seed;
        public bool hasTerrainStartupMetadata;
        public WorldStartupMode startupMode = WorldStartupMode.GenerateProceduralTerrain;
        public string terrainProfileId = string.Empty;
        public int terrainContentRevision;
        public float townAnchorX;
        public float townAnchorY;
        public float townAnchorZ;
        public int gridWidthCells;
        public int gridDepthCells;
        public float cellSizeMeters;
        public float gridOriginX;
        public float gridOriginY;
        public float gridOriginZ;
        public RegionalWorldSaveDto regionalWorld = new();
        public RegionalResourceSaveDto regionalResources = new();
        public bool hasExplicitResourceState;
        public bool hasExplicitRoadState;
        public List<RoadCellSaveDto> roadCells = new();
        public List<PlotSaveDto> plots = new();
        public List<BuildingSaveDto> buildings = new();
        public int nextBuildingId;
    }

    [Serializable]
    public sealed class RoadCellSaveDto
    {
        public int x;
        public int z;
        public RoadType roadType;
    }

    [Serializable]
    public sealed class RegionalResourceSaveDto
    {
        public List<MineralDistrictSaveDto> districts = new();
        public List<RemoteIndustrySiteSaveDto> remoteSites = new();
        public float totalFreightPressure01;
        public float totalSettlementPressure01;
        public float totalRemoteDevelopmentPressure01;
        public bool HasRecords => (districts != null && districts.Count > 0)
            || (remoteSites != null && remoteSites.Count > 0);
    }

    [Serializable]
    public sealed class MineralDistrictSaveDto
    {
        public string id = string.Empty;
        public MineralResourceKind kind;
        public float centerX;
        public float centerY;
        public float centerZ;
        public GridRectSaveDto bounds = new();
        public float radiusMeters;
        public float strength01;
        public float suitability01;
        public float freightPressure01;
        public float settlementPressure01;
        public float industrialBackboneSignificance01;
        public float exportSpeculationSignificance01;
        public string notes = string.Empty;
    }

    [Serializable]
    public sealed class RemoteIndustrySiteSaveDto
    {
        public string id = string.Empty;
        public MineralResourceKind dominantResource;
        public float anchorX;
        public float anchorY;
        public float anchorZ;
        public GridRectSaveDto anchorArea = new();
        public List<string> linkedDistrictIds = new();
        public float siteStrength01;
        public float parcelRelevance01;
        public float futureCampRelevance01;
        public float futureBoomtownRelevance01;
        public float freightPressure01;
        public float settlementPressure01;
        public string debugLabel = string.Empty;
    }

    [Serializable]
    public sealed class PlotSaveDto
    {
        public int id;
        public PlotZone zone;
        public GridRectSaveDto bounds = new();
        public GridRectSaveDto candidateFootprint = new();
        public int siteSizeX;
        public int siteSizeY;
        public int intendedFootprintX;
        public int intendedFootprintY;
        public AgriculturalSiteRole agriculturalSiteRole = AgriculturalSiteRole.None;
        public PublicSiteRole publicSiteRole = PublicSiteRole.None;
        public int frontageCells;
        public int depthCells;
        public GridDirection roadFrontageDirection;
        public GridCoordSaveDto roadAccessCell = new();
        public int buildingId = -1;
        public bool reservedForLandSale;
        public bool playerOwned;
    }

    [Serializable]
    public sealed class BuildingSaveDto
    {
        public int id;
        public int plotId;
        public string buildingDefinitionId = string.Empty;
        public GridRectSaveDto footprint = new();
        public int siteSizeX;
        public int siteSizeY;
        public int intendedFootprintX;
        public int intendedFootprintY;
        public GridDirection frontageDirection;
        public PublicSiteRole publicSiteRole = PublicSiteRole.None;
        public bool playerOwned;
        public List<BuildingAnchorSaveDto> anchors = new();
    }

    [Serializable]
    public sealed class BuildingAnchorSaveDto
    {
        public AnchorType type;
        public GridCoordSaveDto coord = new();
        public bool fromAuthoredMarker;
        public bool fromFallbackRule;
        public string source = string.Empty;
    }

    [Serializable]
    public sealed class GridCoordSaveDto
    {
        public int x;
        public int z;
    }

    [Serializable]
    public sealed class GridRectSaveDto
    {
        public int xMin;
        public int zMin;
        public int width;
        public int depth;
    }

    [Serializable]
    public sealed class TimeSaveDto
    {
        public SimulationDate currentDate;
        public int absoluteDayIndex;
        public float timeOfDay01;
        public float gameSecondsIntoDay;
        public int shortTickIndex;
        public float shortTickAccumulatorGameSeconds;
        public SimulationSpeed currentSpeed;
        public SimulationSpeed lastNonPausedSpeed;
        public bool isPaused;
    }

    [Serializable]
    public sealed class PopulationSaveDto
    {
        public List<PersonSaveDto> people = new();
        public List<HouseholdSaveDto> households = new();
        public int nextPersonId;
        public int nextHouseholdId;
        public List<string> validationMessages = new();
        public List<RentalPropertySaveDto> rentalProperties = new();
        public List<RentalApplicantSaveDto> rentalApplicants = new();
        public int lastHealthResolutionDayIndex = -1;
        public string lastHealthSummary = string.Empty;
        public int boardingCapacity;
        public int boardingUsed;
        public int boardingHouseCapacity;
        public int boardingHouseUsed;
        public int renterHouseholdCount;
        public int kinPlacementCount;
        public int transientPersonCount;
        public int unstableHouseholdCount;
        public int departedUnsettledCount;
        public int rentalVacancyCount;
        public int playerOwnedRentalVacancyCount;
        public int rentalApplicantCount;
        public int strongerHousingNeedCount;
        public float housingPressure01;
        public float rentalPressure01;
        public float laborAbsorption01 = 1f;
        public string lastSettlementSummary = string.Empty;
        public string lastRentalSummary = string.Empty;
    }

    [Serializable]
    public sealed class PersonSaveDto
    {
        public int id;
        public string firstName = string.Empty;
        public string lastName = string.Empty;
        public int age;
        public AgeBand ageBand;
        public LaborAccessLevel laborAccessLevel;
        public int householdId;
        public string professionId = string.Empty;
        public string professionName = string.Empty;
        public WageSnapshot wage;
        public PersonHealthState health = new();
        public WorkerVisibleProfile visibleWorkerProfile = new();
        public WorkerHiddenTraits hiddenWorkerTraits = new();
        public WorkerTraitVisibility workerTraitVisibility = new();
        public WorkerApprenticeshipState apprenticeship = new();
        public int homeBuildingId;
        public int workplaceBuildingId;
        public PopulationScheduleState scheduleState;
        public int currentDestinationBuildingId;
        public NewcomerArrivalProfile arrivalProfile = NewcomerArrivalProfile.SettledResident;
        public SettlementArrangement settlementArrangement = SettlementArrangement.StableHousehold;
        public SettlementPressureState settlementPressure;
        public int startingCashCents;
        public float laborUrgency01 = 0.45f;
        public int laborReadinessModifier;
        public string preferredProfessionBias = string.Empty;
        public int settlementDifficulty;
        public int hostHouseholdId = -1;
    }

    [Serializable]
    public sealed class HouseholdSaveDto
    {
        public int id;
        public string householdName = string.Empty;
        public string surname = string.Empty;
        public List<int> memberIds = new();
        public int homeBuildingId;
        public int weeklyIncomeSnapshot;
        public int spendingMoneyCents;
        public int lastStoreSpendCents;
        public int lifetimeStoreSpendCents;
        public HouseholdDemandSnapshot demandSnapshot;
        public List<HouseholdUpgradeSaveDto> upgrades = new();
        public List<HouseholdReserveSaveDto> reserves = new();
        public int foodReserveUnits;
        public int lastReserveDepletionDayIndex = -1;
        public int lastDailyReserveUseUnits;
        public int lastDailyReserveLocalPurchaseUnits;
        public int lastDailyReserveOffMapPurchaseUnits;
        public int lastWeeklyUpgradeIncomeCents;
        public int lastWeeklyUpgradeUpkeepCents;
        public int lastWeeklyReserveDeltaUnits;
        public int lastDailyMedicalSpendCents;
        public int lastWeeklyLaborLossCents;
        public int lifetimeMedicalSpendCents;
        public HouseholdHeatRetentionTier heatRetentionTier = HouseholdHeatRetentionTier.Basic;
        public HouseholdStoveTier stoveTier = HouseholdStoveTier.Basic;
        public HouseholdFuelType preferredFuel = HouseholdFuelType.Wood;
        public float lastWinterStrain01;
        public float lastHeatingPressure01;
        public int lastHeatingFuelUseUnits;
        public int lastHeatingFuelShortfallUnits;
        public NewcomerArrivalProfile arrivalProfile = NewcomerArrivalProfile.SettledResident;
        public SettlementArrangement settlementArrangement = SettlementArrangement.StableHousehold;
        public HouseholdDwellingKind dwellingKind = HouseholdDwellingKind.OwnedHome;
        public string dwellingSummary = string.Empty;
        public SettlementPressureState settlementPressure;
        public int settlementReserveStrengthCents;
        public int baseBoardingCapacity;
        public int boardingCapacity;
        public List<int> boarderPersonIds = new();
        public bool hostsBoarders;
        public bool isRenterHousehold;
        public bool hasKinAbsorptionPressure;
        public bool isTransientHousehold;
        public bool isBoardingHouseLodging;
        public string boardingBusinessInstanceId = string.Empty;
        public bool isBoardingHouseGuestHousehold;
        public string lodgingBusinessInstanceId = string.Empty;
        public float crowdingPressure01;
        public float boarderOverloadPressure01;
        public float kinAbsorptionPressure01;
        public string lastSettlementSummary = string.Empty;
    }

    [Serializable]
    public sealed class RentalPropertySaveDto
    {
        public int buildingId = -1;
        public string displayName = string.Empty;
        public int residentHouseholdCapacity;
        public int occupiedHouseholds;
        public int vacantHouseholdSlots;
        public bool playerOwned;
        public bool mixedUse;
        public bool rentalCapable = true;
        public string occupancySummary = string.Empty;
    }

    [Serializable]
    public sealed class RentalApplicantSaveDto
    {
        public int personId = -1;
        public int householdId = -1;
        public int preferredBuildingId = -1;
        public string displayName = string.Empty;
        public NewcomerArrivalProfile arrivalProfile = NewcomerArrivalProfile.SettledResident;
        public SettlementArrangement currentArrangement = SettlementArrangement.StableHousehold;
        public int savingsCents;
        public int weeklyIncomeCents;
        public int weeksWaiting;
        public float score01;
        public bool acceptedLastWeek;
        public string status = string.Empty;
    }

    [Serializable]
    public sealed class HouseholdReserveSaveDto
    {
        public string categoryId = string.Empty;
        public string displayName = string.Empty;
        public int currentUnits;
        public int targetUnits;
        public int lowThresholdUnits;
        public int lastDailyUseUnits;
        public int lastRequestedPurchaseUnits;
        public int lastLocalPurchaseUnits;
        public int lastOffMapPurchaseUnits;
        public float lastUrgency01;
        public int lastShoppingAttemptDayIndex = -1;
        public int consecutiveLocalShortfallDays;
        public int rememberedStockoutUnits;
        public float stockoutFrustration01;
    }

    [Serializable]
    public sealed class HouseholdUpgradeSaveDto
    {
        public HouseholdUpgradeKind kind;
        public bool built;
        public int builtDayIndex;
    }

    [Serializable]
    public sealed class BusinessPortfolioSaveDto
    {
        public GeneralStoreSaveDto deepGeneralStore = new();
        public List<BusinessInstanceSaveDto> sharedBusinesses = new();
        public List<LocalRecurringOrderRelationshipSaveDto> localRecurringOrderRelationships = new();
        public List<BusinessTransferAgreementSaveDto> transferAgreements = new();
        public string lastWeeklyRecurringLocalOrderSummary = string.Empty;
        public string lastWeeklyTransferAgreementSummary = string.Empty;
        public LogisticsRuntimeSaveDto logistics = new();
        public TownPulseSaveDto townPulse = new();
    }

    [Serializable]
    public sealed class LogisticsRuntimeSaveDto
    {
        public string lastTownLedgerSummary = string.Empty;
        public List<LogisticsShipmentSaveDto> shipments = new();
    }

    [Serializable]
    public sealed class LogisticsShipmentSaveDto
    {
        public string shipmentId = string.Empty;
        public string transferAgreementId = string.Empty;
        public LogisticsShipmentKind shipmentKind;
        public LogisticsCargoClass cargoClass;
        public LogisticsSupplierClass supplierClass;
        public LogisticsShipmentEndpointKind sourceEndpointKind;
        public LogisticsShipmentEndpointKind destinationEndpointKind;
        public LogisticsShipmentDeliveryMode deliveryMode;
        public ShipmentHaulingMode haulingMode;
        public LogisticsShipmentStatus state;
        public LogisticsShipmentStatus resumeStateAfterDelay;
        public string sourceBusinessInstanceId = string.Empty;
        public string destinationBusinessInstanceId = string.Empty;
        public string carrierBusinessInstanceId = string.Empty;
        public string freightPayerBusinessInstanceId = string.Empty;
        public string sourceCategoryId = string.Empty;
        public string destinationCategoryId = string.Empty;
        public string summaryLabel = string.Empty;
        public string blockedReason = string.Empty;
        public int plannedQuantityUnits;
        public int remainingQuantityUnits;
        public int sellerUnitRevenueCents;
        public int buyerUnitCostCents;
        public int freightChargeCents;
        public bool sourceCommittedAtSchedule;
        public bool loadApplied;
        public bool deliveryApplied;
        public float loadingDurationGameSeconds;
        public float transitDurationGameSeconds;
        public float unloadingDurationGameSeconds;
        public float stageElapsedGameSeconds;
        public float totalElapsedGameSeconds;
        public LogisticsRoutePlanSaveDto routePlan = new();
    }

    [Serializable]
    public sealed class BusinessTransferAgreementSaveDto
    {
        public string agreementId = string.Empty;
        public string sourceBusinessInstanceId = string.Empty;
        public string destinationBusinessInstanceId = string.Empty;
        public string sourceCategoryId = string.Empty;
        public string destinationCategoryId = string.Empty;
        public BusinessTransferAllocationMode allocationMode;
        public int allocationValue;
        public BusinessCadence cadence = BusinessCadence.Weekly;
        public int cadenceInterval = 1;
        public BusinessTransferPricingMode pricingMode = BusinessTransferPricingMode.Market;
        public float priceModifier = 1f;
        public int sourceReserveUnits;
        public LocalRecurringOrderHaulingResponsibility haulingResponsibility;
        public bool active = true;
        public int nextDueWeekKey = -1;
        public int lastResolvedWeekKey = -1;
        public int lastScheduledUnits;
        public int lastDeliveredUnits;
        public int lastScheduledTotalPriceCents;
        public int lastDeliveredTotalPriceCents;
        public string lastShipmentId = string.Empty;
        public string lastFulfillmentSummary = string.Empty;
    }

    [Serializable]
    public sealed class LogisticsRoutePlanSaveDto
    {
        public string label = string.Empty;
        public GridCoordSaveDto visibleStart = new();
        public GridCoordSaveDto visibleEnd = new();
        public int totalTravelCells;
        public int visibleTravelCells;
        public float totalTravelGameSeconds;
        public float visibleTravelGameSeconds;
        public LogisticsRouteQuality quality;
        public string failureReason = string.Empty;
        public List<GridCoordSaveDto> visiblePath = new();
    }

    [Serializable]
    public sealed class LocalRecurringOrderRelationshipSaveDto
    {
        public string relationshipId = string.Empty;
        public string orderId = string.Empty;
        public string sellerInstanceId = string.Empty;
        public string buyerInstanceId = string.Empty;
        public string sellerDisplayName = string.Empty;
        public string buyerDisplayName = string.Empty;
        public string sellerCategoryId = string.Empty;
        public string buyerCategoryId = string.Empty;
        public int targetQuantity;
        public int cadenceWeeks = 1;
        public int nextDueWeekKey = -1;
        public LocalRecurringOrderPriceMode priceMode;
        public LocalRecurringOrderHaulingResponsibility haulingResponsibility;
        public float relationshipHealth01 = 0.5f;
        public int renewalCount;
        public bool active = true;
        public string cancellationReason = string.Empty;
        public int lastRequestedUnits;
        public int lastFulfilledUnits;
        public int lastPaidCents;
        public int lastResolvedWeekKey = -1;
        public int cleanFulfillmentCount;
        public int shortFulfillmentCount;
        public int failedFulfillmentCount;
        public int buyerCashBreachCount;
        public int consecutiveCleanFulfillmentWeeks;
        public int consecutiveBreachWeeks;
        public string lastBreachReason = string.Empty;
        public LocalRecurringOrderFulfillmentStatus lastStatus;
        public SupplierRelationshipState supplierRelationship = new();
    }

    [Serializable]
    public sealed class TownPulseSaveDto
    {
        public bool active;
        public string activePulseId = string.Empty;
        public int activeDefinitionIndex = -1;
        public int activeStartDayIndex = -1;
        public int activeEndDayIndexExclusive = -1;
        public int nextPulseStartDayIndex = 4;
        public int nextDefinitionIndex;
        public int lastResolvedDayIndex = -1;
        public int lastDailyRequestedUnits;
        public int lastDailyFulfilledUnits;
        public int lastDailyMissedUnits;
        public List<TownPulseCategoryResultSaveDto> lastDailyCategoryResults = new();
    }

    [Serializable]
    public sealed class TownPulseCategoryResultSaveDto
    {
        public string categoryId = string.Empty;
        public int requestedUnits;
        public int fulfilledUnits;
        public int missedUnits;
    }

    [Serializable]
    public sealed class PlayerDebtSaveDto
    {
        public int requestedAmountCents = 10000;
        public BankLoanApplicationState application = new();
        public LoanContract activeLoan;
        public List<LoanContract> activeLoans = new();
        public PlayerReputationState reputation = new();
        public int consecutiveMissedPayments;
        public int lifetimeMissedPayments;
        public int defaultCount;
        public int priorFailedFinancingCount;
        public int lastMissedPaymentDayIndex = -1;
        public string lastStatusSummary = string.Empty;
        public string lastDecisionSummary = string.Empty;
        public string lastRepaymentSummary = string.Empty;
        public string lastRecoverySummary = string.Empty;
    }

    [Serializable]
    public sealed class PlayerPortfolioSaveDto
    {
        public bool initialized;
        public int ownerCashCents;
        public string lastStatusSummary = string.Empty;
        public string lastTransactionSummary = string.Empty;
        public int lastWeeklyDistributionCents;
        public int lastWeeklyDistributionWeekKey = -1;
        public string lastWeeklyDistributionSummary = string.Empty;
        public int netWorthWinTargetCents = 1000000;
        public int lastNetWorthCents;
        public int lastBusinessCashCents;
        public int lastBusinessEquityCents;
        public int lastBusinessLiabilityCents;
        public int lastAssetValueCents;
        public int lastDebtLiabilityCents;
        public bool netWorthWinReached;
        public List<OwnerDistributionCheckpointSaveDto> distributionCheckpoints = new();
    }

    [Serializable]
    public sealed class OwnerDistributionCheckpointSaveDto
    {
        public string businessInstanceId = string.Empty;
        public int lastCashCheckpointCents;
        public int lastDistributionCents;
        public int lastDistributionWeekKey = -1;
        public int openedWeekKey = -1;
    }


    [Serializable]
    public sealed class FirstSessionGuidanceSaveDto
    {
        public bool initialized;
        public bool guidanceEnabled = true;
        public FirstSessionObjectiveStep currentStep = FirstSessionObjectiveStep.OpenManagement;
        public int baselineOwnedLandCount;
        public int baselineOwnedBusinessCount;
        public bool baselinesCaptured;
        public bool managementOpened;
        public bool generalStoreInspected;
        public bool firstWorkerHired;
        public bool firstSalesObserved;
        public bool moneyModelReviewed;
        public bool acquisitionsOpened;
        public bool firstLandPurchased;
        public bool returnedToProperties;
        public bool firstBusinessShellBuilt;
        public bool firstBusinessStarted;
        public bool salesBaselineCaptured;
        public int salesBaselineDayIndex = -1;
        public int salesBaselineWeekRevenueCents;
        public int salesBaselineDailyRevenueCents;
        public int salesBaselineUnitsSold;
    }

    [Serializable]
    public sealed class GeneralStoreSaveDto
    {
        public int storeBuildingId = -1;
        public int generatedHouseholdCount;
        public int lastDailyCustomerHouseholds;
        public int lastDailyUnitsSold;
        public int lastDailyWalletSpendCents;
        public int lastDailyCostOfGoodsSoldCents;
        public int lastDailyHouseholdWalletBeforeCents;
        public int lastDailyHouseholdWalletAfterCents;
        public int lastDailyReserveLocalUnits;
        public int lastDailyReserveLocalSpendCents;
        public int lastDailyReserveOffMapUnits;
        public int lastDailyReserveOffMapLostDemandCents;
        public int currentWeekCustomerHouseholds;
        public int weekToDateCostOfGoodsSoldCents;
        public int currentSalesWeek = -1;
        public int lastWeeklyReorderSpendCents;
        public int lastWeeklyReorderUnitsQueued;
        public int lastWeeklyReorderUnitsReceived;
        public int lastWeeklyReorderUnitsRemaining;
        public int lastWeeklyReorderQueuedCostCents;
        public bool lastWeeklyReorderTriggered;
        public int lastWeeklyLocalSupplySpendCents;
        public int lastWeeklyLocalSupplyUnitsReceived;
        public string lastWeeklyLocalSupplySummary = string.Empty;
        public bool reorderNeeded;
        public float storeMarginAdjustment01;
        public string status = string.Empty;
        public string lastDailySalesDebugSummary = string.Empty;
        public string lastWeeklySettlementDebugSummary = string.Empty;
        public string lastReorderDebugSummary = string.Empty;
        public string lastStaffingSummary = string.Empty;
        public string lastCategoryDemandSummary = string.Empty;
        public BusinessInstanceSaveDto currentBusiness = new();
    }

    [Serializable]
    public sealed class BusinessInstanceSaveDto
    {
        public string instanceId = string.Empty;
        public string profileId = string.Empty;
        public BusinessType businessType;
        public int assignedBuildingId = -1;
        public string runtimeDisplayName = string.Empty;
        public BusinessOwnerSaveDto owner = new();
        public BusinessControlState controlState;
        public ManagerPolicyPreset managerPolicy = ManagerPolicyPreset.StabilityFirst;
        public BusinessThroughputMode throughputMode;
        public int baselineWeeklyThroughputUnits;
        public int baselineDailyServiceCapacity;
        public int lastWeeklyThroughputUnits;
        public int lastDailyServiceVisits;
        public BusinessCashTransferRuleSaveDto cashTransferRule = new();
        public BusinessReputationSaveDto businessReputation = new();
        public BusinessRuntimeSaveDto runtime = new();
        public MineRuntimeSaveDto mine = new();
    }

    [Serializable]
    public sealed class MineRuntimeSaveDto
    {
        public MineralResourceKind mineralKind;
        public float depositConfidence01 = 0.5f;
        public MineDevelopmentStage developmentStage;
        public int lastWeeklyOutputUnits;
        public string stockpileCategoryId = string.Empty;
        public float safetyRisk01 = 0.2f;
        public float remainingRichness01 = 0.8f;
        public float campPressure01;
        public string lastWeeklyOutputSummary = string.Empty;
    }

    [Serializable]
    public sealed class BusinessReputationSaveDto
    {
        public float stockReliability01 = 0.5f;
        public float valueFairness01 = 0.5f;
        public float serviceExperience01 = 0.5f;
        public float conditionPresentationTrust01 = 0.5f;
        public float productTradeConfidence01 = 0.5f;
        public bool initializedFromRuntime;
        public List<BusinessStockoutMemorySaveDto> stockoutMemory = new();
    }

    [Serializable]
    public sealed class BusinessStockoutMemorySaveDto
    {
        public string categoryId = string.Empty;
        public int lastMissedDayIndex = -1;
        public int lastMissedWeekKey = -1;
        public int oneOffMissCount;
        public int consecutiveMissCount;
        public int recoveryStreak;
        public int lifetimeMissCount;
        public float lastSeverity01;
    }

    [Serializable]
    public sealed class BusinessCashTransferRuleSaveDto
    {
        public bool autoTransferEnabled;
        public int lowerThresholdCents;
        public int upperThresholdCents;
        public string lastTransferSummary = string.Empty;
        public int lastTransferWeekKey = -1;
    }

    [Serializable]
    public sealed class BusinessOwnerSaveDto
    {
        public BusinessOwnerKind ownerKind;
        public int personId = -1;
        public string displayName = string.Empty;
        public string surname = string.Empty;
    }

    [Serializable]
    public sealed class BusinessRuntimeSaveDto
    {
        public string businessId = string.Empty;
        public BusinessType businessType;
        public int currentCashCents;
        public int lastDailyRevenueCents;
        public int lastWeeklyPayrollCents;
        public int lastWeeklyReorderBudgetCents;
        public int lastWeeklyInputUnitsConsumed;
        public int lastWeeklyOutputUnitsProduced;
        public int lastWeeklyInputProcurementSpendCents;
        public int lastWeeklyLocalTransferRevenueCents;
        public int lastWeeklyLocalTransferCostCents;
        public int lastWeeklyCashTransferInCents;
        public int lastWeeklyCashTransferOutCents;
        public int lastWeeklyOwnerDistributionCents;
        public int lastWeeklyCashBeforeCents;
        public int lastWeeklyCashAfterCents;
        public int accruedLiabilityCents;
        public int lastWeeklyAccruedLiabilityCents;
        public string lastWeeklyBlockedReason = string.Empty;
        public string lastWeeklyOperationSummary = string.Empty;
        public string lastWeeklyTransferSummary = string.Empty;
        public int weekToDateRevenueCents;
        public int weekToDateUnitsSold;
        public int lastDailyNetCents;
        public int weekToDateNetCents;
        public int monthToDateNetCents;
        public int yearToDateNetCents;
        public int allTimeNetCents;
        public float stockHealth01 = 1f;
        public float reliability01 = 1f;
        public float competitionPressure01;
        public List<CategoryStockSaveDto> categoryStock = new();
        public List<WorkerSlotSaveDto> workerSlots = new();
        public List<string> lastSuspendedPayrollWorkerIds = new();
        public BusinessMainFocusSaveDto mainFocus = new();
        public BusinessCapacitySaveDto capacity = new();
    }

    [Serializable]
    public sealed class BusinessMainFocusSaveDto
    {
        public string focusId = string.Empty;
        public string displayName = string.Empty;
        public string playerFacingSummary = string.Empty;
    }

    [Serializable]
    public sealed class BusinessCapacitySaveDto
    {
        public int storageCapacityUnits;
        public int currentStoredUnits;
        public int processingCapacityUnitsPerWeek;
        public int currentProcessingUnitsPerWeek;
        public int serviceCapacityVisitsPerDay;
        public int currentServiceVisitsPerDay;
    }

    [Serializable]
    public sealed class CategoryStockSaveDto
    {
        public string categoryId = string.Empty;
        public int currentStockUnits;
        public int targetStockUnits;
        public int pendingReorderUnits;
        public int lastDailyUnitsSold;
        public int lastDailyRevenueCents;
        public int weekToDateUnitsSold;
        public int weekToDateRevenueCents;
    }

    [Serializable]
    public sealed class WorkerSlotSaveDto
    {
        public string slotId = string.Empty;
        public string slotDisplayName = string.Empty;
        public string assignedWorkerId = string.Empty;
        public string assignedWorkerDisplayName = string.Empty;
        public int weeklyWageCents;
        public bool requiredForOpening;
        public bool paidActive;
        public bool suspendedForMissedPayroll;
    }

    [Serializable]
    public sealed class CivicSaveDto
    {
        public TownHallSaveDto townHall = new();
        public SchoolhouseSaveDto schoolhouse = new();
    }

    [Serializable]
    public sealed class TownHallSaveDto
    {
        public bool built;
        public int buildingId = -1;
        public int plotId = -1;
        public string holdingId = TownHallState.TownHallBuildingId;
        public string displayName = "Town Hall";
        public CivicOwnerKind ownerKind = CivicOwnerKind.Town;
        public string ownerDisplayName = "Town Council";
        public string civicStatusLabel = "Civic";
    }

    [Serializable]
    public sealed class SchoolhouseSaveDto
    {
        public bool built;
        public int buildingId = -1;
        public int plotId = -1;
        public string holdingId = SchoolhouseState.SchoolhouseBuildingId;
        public string displayName = "Schoolhouse";
        public CivicOwnerKind ownerKind = CivicOwnerKind.Town;
        public string ownerDisplayName = "School Board";
        public string civicStatusLabel = "Civic";
        public int teacherPersonId = -1;
        public string teacherDisplayName = string.Empty;
    }

    [Serializable]
    public sealed class AcquisitionSaveDto
    {
        public int marketSeed;
        public int maxLandListings;
        public int maxBusinessListings;
        public List<AcquisitionOpportunityWindowSaveDto> opportunityWindows = new();
        public int retailPressureStreakWeeks;
        public int housingPressureStreakWeeks;
        public int laborPressureStreakWeeks;
        public int servicePressureStreakWeeks;
        public int expansionPressureStreakWeeks;
        public int developmentInventoryPressure;
        public string lastTownActionSummary = string.Empty;
        public List<int> ownedPlotIds = new();
        public List<int> ownedBusinessBuildingIds = new();
        public List<AcquisitionDealSaveDto> activeDeals = new();
        public List<string> watchedListingIds = new();
        public List<LandAppreciationSaveDto> landAppreciations = new();
        public List<ConstructionSupportNodeSaveDto> constructionSupportNodes = new();
        public List<ConstructionProjectSaveDto> constructionProjects = new();
        public OwnershipAptitudeSaveDto ownershipAptitude = new();
        public int selectedLandIndex;
        public int selectedBusinessIndex;
        public string marketStatus = string.Empty;
        public string lastPurchaseSummary = string.Empty;
        public string lastConstructionSupportSummary = string.Empty;
        public string lastConstructionQueueSummary = string.Empty;
    }

    [Serializable]
    public sealed class AcquisitionOpportunityWindowSaveDto
    {
        public string listingId = string.Empty;
        public AcquisitionListingKind kind;
        public int firstSeenDayIndex = -1;
        public int playerFirstUntilDayIndex = -1;
        public float pressure01;
        public string sourceReason = string.Empty;
        public bool aiEligible;
    }

    [Serializable]
    public sealed class ConstructionProjectSaveDto
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
    }

    [Serializable]
    public sealed class AcquisitionDealSaveDto
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
        public LandLedgers.Economy.AcquisitionLeadCategory leadCategory = LandLedgers.Economy.AcquisitionLeadCategory.Unset;
        public string leadQualitySummary = string.Empty;
        public string proofOfFundsSummary = string.Empty;
        public string diligenceLayerSummary = string.Empty;
        public string closingRiskSummary = string.Empty;
        public LandLedgers.Economy.AcquisitionProofStatus proofStatus = LandLedgers.Economy.AcquisitionProofStatus.NotRequested;
        public bool proofRequired;
        public int proofPresentedDayIndex = -1;
        public int requiredDiligenceMask;
        public int completedDiligenceMask;
        public LandLedgers.Economy.AcquisitionClosingFailureCause closingFailureCause = LandLedgers.Economy.AcquisitionClosingFailureCause.None;
        public string closingFailureSummary = string.Empty;
    }

    [Serializable]
    public sealed class OwnershipAptitudeSaveDto
    {
        public int acquisitionExperiencePoints;
        public int operationsExperiencePoints;
        public int peopleReputationExperiencePoints;
        public int landPurchasedCount;
        public int businessPurchasedCount;
        public int parcelsDevelopedCount;
        public int businessesOpenedCount;
        public int turnaroundsCompletedCount;
        public int ownershipMilestonesReached;
        public int highestOwnershipMilestoneCount;
    }

    [Serializable]
    public sealed class LandAppreciationSaveDto
    {
        public int plotId = -1;
        public int buildingId = -1;
        public LandAppreciationHoldingKind holdingKind;
        public LandAppreciationImprovementState improvementState;
        public int purchaseBasisCents;
        public int capitalizedImprovementCents;
        public int currentEstimatedValueCents;
        public int appreciationDeltaCents;
        public int purchaseDayIndex;
        public int lastValuationDayIndex;
        public float townGrowthPressure01;
        public float developmentReadiness01;
    }

    [Serializable]
    public sealed class ConstructionSupportNodeSaveDto
    {
        public string nodeId = string.Empty;
        public string displayName = string.Empty;
        public ConstructionResourceKind resourceKind;
        public int currentStockUnits;
        public int unitCostCents;
        public bool active = true;
        public bool remoteProductionEnabled;
        public bool remoteProductionInitialized;
        public int remotePropertyPlotId = -1;
        public int standingTimberUnits;
        public int logStockUnits;
        public int weeklyCutCapacityLogs;
        public int weeklySawCapacityLogs;
        public int lumberPerLog;
        public int lumberStorageCapacityUnits;
        public int hardwareMaintenanceUnitsPerWeek;
        public bool hasWorkerCabins;
        public bool internalHaulingOwned = true;
        public bool hiredHaulingFallbackAllowed = true;
        public int hiredHaulingCostPerLogCents = ConstructionSupportNodeDefinition.DefaultSawmillHiredHaulingCostPerLogCents;
        public int lastWeeklyTimberCutUnits;
        public int lastWeeklyLogsProcessedUnits;
        public int lastWeeklyLumberProducedUnits;
        public int lastWeeklySlabsOffcutsProducedUnits;
        public int lastWeeklySlabsOffcutsSoldUnits;
        public int lastWeeklyLogsHauledUnits;
        public int lastWeeklyHaulingCostCents;
        public string lastWeeklyHaulingMode = string.Empty;
        public int lastResolvedSawmillWeekKey = -1;
        public string lastWeeklyProductionSummary = string.Empty;
        public string lastWeeklyBlockedReason = string.Empty;
        public int lastProjectConstraintScore;
        public int blockedProjectReviewCount;
        public int readyProjectReviewCount;
        public string lastWeeklyProjectReviewSummary = string.Empty;
    }
}
