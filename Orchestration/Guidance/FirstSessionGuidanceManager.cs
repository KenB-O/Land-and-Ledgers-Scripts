using System;
using LandLedgers.Economy;
using LandLedgers.Persistence;
using LandLedgers.Time;
using LandLedgers.UI;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.FirstLedger
{
    public enum FirstSessionObjectiveStep
    {
        OpenManagement = 0,
        InspectGeneralStore = 1,
        HireFirstWorker = 2,
        ObserveFirstSales = 3,
        ReviewMoneyModel = 4,
        OpenAcquisitions = 5,
        CompleteFirstLandPurchase = 6,
        ReturnToProperties = 7,
        BuildFirstBusinessShell = 8,
        StartFirstBusiness = 9,
        Complete = 10
    }

    public struct FirstSessionGuidanceFacts
    {
        public bool ManagementOpen;
        public ManagementPanelTab CurrentTab;
        public AcquisitionMarketSection CurrentAcquisitionSection;
        public bool GeneralStoreSelected;
        public bool GeneralStoreStaffed;
        public bool FirstSalesObserved;
        public int OwnedLandCount;
        public int OwnedBusinessCount;
        public int LandListingCount;
        public bool HasSelectedLandListing;
        public string SelectedLandActionLabel;
        public string SelectedAcquisitionCurrentRead;
        public string SelectedAcquisitionNextAction;
        public string SelectedAcquisitionBlocker;
        public int OwnerCashCents;
        public int StoreCashCents;
        public int StoreProtectedReserveCents;
        public int StorePendingReorderUnits;
        public int StoreCashHeldReorderUnits;
        public int StoreCashHeldReorderCostCents;
        public string StoreSupplyReadinessLine;
        public bool IsSelectedOwnedLand;
        public bool IsSelectedStartableOwnedShell;
        public string PropertyGuidanceHint;
        public string SelectedPropertyCurrentRead;
        public string SelectedPropertyNextAction;
        public string SelectedPropertyBlocker;
        public bool SelectedPropertyShellReady;
        public bool SelectedPropertyStartupReady;
        public bool StartableOwnedShellExists;
        public bool PlayerOwnedExpansionBusinessExists;
    }

    public readonly struct FirstSessionObjectiveState
    {
        public FirstSessionObjectiveState(FirstSessionObjectiveStep step, string objective, string why, string action)
        {
            Step = step;
            Objective = objective ?? string.Empty;
            Why = why ?? string.Empty;
            Action = action ?? string.Empty;
        }

        public FirstSessionObjectiveStep Step { get; }
        public string Objective { get; }
        public string Why { get; }
        public string Action { get; }
    }

    public sealed class FirstSessionGuidanceProgress
    {
        private int baselineOwnedLandCount;
        private int baselineOwnedBusinessCount;
        private bool baselinesCaptured;
        private bool managementOpened;
        private bool generalStoreInspected;
        private bool firstWorkerHired;
        private bool firstSalesObserved;
        private bool moneyModelReviewed;
        private bool acquisitionsOpened;
        private bool firstLandPurchased;
        private bool returnedToProperties;
        private bool firstBusinessShellBuilt;
        private bool firstBusinessStarted;

        public bool BaselinesCaptured => baselinesCaptured;
        public FirstSessionObjectiveStep CurrentStep { get; private set; } = FirstSessionObjectiveStep.OpenManagement;

        public void Reset()
        {
            baselineOwnedLandCount = 0;
            baselineOwnedBusinessCount = 0;
            baselinesCaptured = false;
            managementOpened = false;
            generalStoreInspected = false;
            firstWorkerHired = false;
            firstSalesObserved = false;
            moneyModelReviewed = false;
            acquisitionsOpened = false;
            firstLandPurchased = false;
            returnedToProperties = false;
            firstBusinessShellBuilt = false;
            firstBusinessStarted = false;
            CurrentStep = FirstSessionObjectiveStep.OpenManagement;
        }

        public void CaptureBaselines(int ownedLandCount, int ownedBusinessCount)
        {
            if (baselinesCaptured)
            {
                return;
            }

            baselineOwnedLandCount = Mathf.Max(0, ownedLandCount);
            baselineOwnedBusinessCount = Mathf.Max(0, ownedBusinessCount);
            baselinesCaptured = true;
        }

        public FirstSessionGuidanceSaveDto CaptureSaveDto()
        {
            return new FirstSessionGuidanceSaveDto
            {
                initialized = true,
                currentStep = CurrentStep,
                baselineOwnedLandCount = baselineOwnedLandCount,
                baselineOwnedBusinessCount = baselineOwnedBusinessCount,
                baselinesCaptured = baselinesCaptured,
                managementOpened = managementOpened,
                generalStoreInspected = generalStoreInspected,
                firstWorkerHired = firstWorkerHired,
                firstSalesObserved = firstSalesObserved,
                moneyModelReviewed = moneyModelReviewed,
                acquisitionsOpened = acquisitionsOpened,
                firstLandPurchased = firstLandPurchased,
                returnedToProperties = returnedToProperties,
                firstBusinessShellBuilt = firstBusinessShellBuilt,
                firstBusinessStarted = firstBusinessStarted
            };
        }

        public void LoadFromSaveDto(FirstSessionGuidanceSaveDto save)
        {
            if (save == null || !save.initialized)
            {
                return;
            }

            baselineOwnedLandCount = Mathf.Max(0, save.baselineOwnedLandCount);
            baselineOwnedBusinessCount = Mathf.Max(0, save.baselineOwnedBusinessCount);
            baselinesCaptured = save.baselinesCaptured;
            managementOpened = save.managementOpened;
            generalStoreInspected = save.generalStoreInspected;
            firstWorkerHired = save.firstWorkerHired;
            firstSalesObserved = save.firstSalesObserved;
            moneyModelReviewed = save.moneyModelReviewed;
            acquisitionsOpened = save.acquisitionsOpened;
            firstLandPurchased = save.firstLandPurchased;
            returnedToProperties = save.returnedToProperties;
            firstBusinessShellBuilt = save.firstBusinessShellBuilt;
            firstBusinessStarted = save.firstBusinessStarted;
            CurrentStep = save.currentStep;
        }

        public void BackfillFromFacts(FirstSessionGuidanceFacts facts)
        {
            bool advancedBeyondStore = facts.OwnedLandCount > baselineOwnedLandCount
                || facts.IsSelectedOwnedLand
                || facts.StartableOwnedShellExists
                || facts.IsSelectedStartableOwnedShell
                || facts.SelectedPropertyShellReady
                || facts.SelectedPropertyStartupReady
                || facts.PlayerOwnedExpansionBusinessExists
                || facts.OwnedBusinessCount > baselineOwnedBusinessCount;

            bool hasExpansionBusiness = facts.PlayerOwnedExpansionBusinessExists || facts.OwnedBusinessCount > baselineOwnedBusinessCount;
            bool hasBuiltShell = facts.StartableOwnedShellExists
                || facts.IsSelectedStartableOwnedShell
                || facts.SelectedPropertyShellReady
                || facts.SelectedPropertyStartupReady
                || hasExpansionBusiness;
            bool returnedToOwnedProperty = facts.IsSelectedOwnedLand
                || facts.IsSelectedStartableOwnedShell
                || facts.SelectedPropertyShellReady
                || facts.SelectedPropertyStartupReady
                || hasExpansionBusiness;

            managementOpened |= advancedBeyondStore;
            generalStoreInspected |= facts.GeneralStoreSelected || facts.GeneralStoreStaffed || facts.FirstSalesObserved || advancedBeyondStore;
            firstWorkerHired |= facts.GeneralStoreStaffed || advancedBeyondStore;
            firstSalesObserved |= facts.FirstSalesObserved || advancedBeyondStore;
            moneyModelReviewed |= advancedBeyondStore;
            acquisitionsOpened |= advancedBeyondStore;
            firstLandPurchased |= advancedBeyondStore;
            returnedToProperties |= returnedToOwnedProperty;
            firstBusinessShellBuilt |= hasBuiltShell;
            firstBusinessStarted |= hasExpansionBusiness;
            CurrentStep = ResolveCurrentStep();
        }

        public FirstSessionObjectiveState Evaluate(FirstSessionGuidanceFacts facts)
        {
            CaptureBaselines(facts.OwnedLandCount, facts.OwnedBusinessCount);

            managementOpened |= facts.ManagementOpen;
            generalStoreInspected |= facts.ManagementOpen
                && facts.CurrentTab == ManagementPanelTab.Properties
                && facts.GeneralStoreSelected;
            firstWorkerHired |= facts.GeneralStoreStaffed;
            firstSalesObserved |= firstWorkerHired && facts.FirstSalesObserved;
            moneyModelReviewed |= facts.ManagementOpen && facts.CurrentTab == ManagementPanelTab.Finances;
            acquisitionsOpened |= facts.ManagementOpen
                && facts.CurrentTab == ManagementPanelTab.Acquisitions;
            // This legacy progress field is retained for save compatibility. The
            // first expansion is now a business formation, not an implicit land
            // purchase; premises remain a separate follow-up when required.
            firstLandPurchased |= facts.OwnedLandCount > baselineOwnedLandCount
                || facts.OwnedBusinessCount > baselineOwnedBusinessCount;
            returnedToProperties |= (firstLandPurchased || facts.OwnedBusinessCount > baselineOwnedBusinessCount)
                && facts.ManagementOpen
                && facts.CurrentTab == ManagementPanelTab.Properties;
            firstBusinessShellBuilt |= facts.StartableOwnedShellExists
                || facts.OwnedBusinessCount > baselineOwnedBusinessCount;
            firstBusinessStarted |= firstBusinessShellBuilt
                && (facts.PlayerOwnedExpansionBusinessExists || facts.OwnedBusinessCount > baselineOwnedBusinessCount);

            CurrentStep = ResolveCurrentStep();
            return BuildObjective(CurrentStep);
        }

        private FirstSessionObjectiveStep ResolveCurrentStep()
        {
            if (!managementOpened)
            {
                return FirstSessionObjectiveStep.OpenManagement;
            }

            if (!generalStoreInspected)
            {
                return FirstSessionObjectiveStep.InspectGeneralStore;
            }

            if (!firstWorkerHired)
            {
                return FirstSessionObjectiveStep.HireFirstWorker;
            }

            if (!firstSalesObserved)
            {
                return FirstSessionObjectiveStep.ObserveFirstSales;
            }

            if (!moneyModelReviewed)
            {
                return FirstSessionObjectiveStep.ReviewMoneyModel;
            }

            if (!acquisitionsOpened)
            {
                return FirstSessionObjectiveStep.OpenAcquisitions;
            }

            if (!firstLandPurchased)
            {
                return FirstSessionObjectiveStep.CompleteFirstLandPurchase;
            }

            if (!returnedToProperties)
            {
                return FirstSessionObjectiveStep.ReturnToProperties;
            }

            if (!firstBusinessShellBuilt)
            {
                return FirstSessionObjectiveStep.BuildFirstBusinessShell;
            }

            if (!firstBusinessStarted)
            {
                return FirstSessionObjectiveStep.StartFirstBusiness;
            }

            return FirstSessionObjectiveStep.Complete;
        }

        private static FirstSessionObjectiveState BuildObjective(FirstSessionObjectiveStep step)
        {
            return step switch
            {
                FirstSessionObjectiveStep.OpenManagement => new FirstSessionObjectiveState(
                    step,
                    "Open Management",
                    "Management is where the store, finances, acquisitions, and properties live.",
                    "Press C."),
                FirstSessionObjectiveStep.InspectGeneralStore => new FirstSessionObjectiveState(
                    step,
                    "Inspect the General Store",
                    "This is your first owned business; its cash keeps the store operating.",
                    "Use Properties and select the General Store row."),
                FirstSessionObjectiveStep.HireFirstWorker => new FirstSessionObjectiveState(
                    step,
                    "Hire your first store worker",
                    "The store needs staff before it can reliably turn inventory into sales.",
                    "Open Labor / Staffing, review the real candidate list, then hire the person who fits the business."),
                FirstSessionObjectiveStep.ObserveFirstSales => new FirstSessionObjectiveState(
                    step,
                    "Observe first store sales",
                    "Sales prove the store is turning inventory into cash before you expand.",
                    "Click a speed button such as 10x or 25x, then watch the store snapshot update."),
                FirstSessionObjectiveStep.ReviewMoneyModel => new FirstSessionObjectiveState(
                    step,
                    "Review Liquid Cash vs Store Cash",
                    FirstSessionGuidanceText.MoneyModelSummary,
                    "Open the Finances tab."),
                FirstSessionObjectiveStep.OpenAcquisitions => new FirstSessionObjectiveState(
                    step,
                    "Open Acquisitions",
                    "Liquid Cash / Owner Cash funds expansion; acquisition leads and business formation are separate decisions from premises.",
                    "Open the Acquisitions tab."),
                FirstSessionObjectiveStep.CompleteFirstLandPurchase => new FirstSessionObjectiveState(
                    step,
                    "Establish your first business",
                    "Form the business entity first. Premises and property are separate decisions made afterward when the operation requires them.",
                    "Open Businesses, choose Create Business, then select the business type that fits your plan."),
                FirstSessionObjectiveStep.ReturnToProperties => new FirstSessionObjectiveState(
                    step,
                    "Configure the formed business",
                    "The business now exists as its own owned organization. Open it from Businesses and add premises, assets, labor, and inputs only where its operation requires them.",
                    "Open Businesses and select the newly formed business."),
                FirstSessionObjectiveStep.BuildFirstBusinessShell => new FirstSessionObjectiveState(
                    step,
                    "Establish operating capability",
                    "A formed business becomes operational only after it has the premises, assets, labor, route capability, and inputs its business type actually requires.",
                    "Open the business management sections and complete the requirements shown for this operation."),
                FirstSessionObjectiveStep.StartFirstBusiness => new FirstSessionObjectiveState(
                    step,
                    "Operate the business",
                    "Ownership and formation are not the same as operation. Let real capability and commerce move this business into operating status.",
                    "Use the business management panel to fund, staff, supply, and operate the business."),
                _ => new FirstSessionObjectiveState(
                    FirstSessionObjectiveStep.Complete,
                    "First expansion loop complete",
                    "You have moved from store ownership to land, construction, and another operating asset.",
                    "Watch Finances, keep businesses funded, and repeat the loop.")
            };
        }
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(360)]
    public sealed class FirstSessionGuidanceManager : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private LandLedgersHUDController hudController;
        [SerializeField] private GeneralStorePanelController managementPanel;
        [SerializeField] private GeneralStoreRuntimeManager storeRuntime;
        [SerializeField] private AcquisitionMarketManager acquisitionMarket;
        [SerializeField] private PlayerPortfolioManager playerPortfolio;
        [SerializeField] private SharedBusinessRuntimeManager sharedBusinessRuntime;
        [SerializeField] private TownWorldController townWorld;
        [SerializeField] private TimeManager timeManager;

        [Header("Runtime")]
        [SerializeField] private bool guidanceEnabled = true;
        [SerializeField] private FirstSessionObjectiveStep currentStep;
        [SerializeField] private bool salesBaselineCaptured;
        [SerializeField] private int salesBaselineDayIndex = -1;
        [SerializeField] private int salesBaselineWeekRevenueCents;
        [SerializeField] private int salesBaselineDailyRevenueCents;
        [SerializeField] private int salesBaselineUnitsSold;

        private readonly FirstSessionGuidanceProgress progress = new();
        private string lastObjective = string.Empty;
        private string lastWhy = string.Empty;
        private string lastAction = string.Empty;
        private string lastBlocker = string.Empty;

        public FirstSessionObjectiveStep CurrentStep => currentStep;

        public FirstSessionGuidanceSaveDto CaptureSaveDto()
        {
            FirstSessionGuidanceSaveDto save = progress.CaptureSaveDto();
            save.guidanceEnabled = guidanceEnabled;
            save.salesBaselineCaptured = salesBaselineCaptured;
            save.salesBaselineDayIndex = salesBaselineDayIndex;
            save.salesBaselineWeekRevenueCents = salesBaselineWeekRevenueCents;
            save.salesBaselineDailyRevenueCents = salesBaselineDailyRevenueCents;
            save.salesBaselineUnitsSold = salesBaselineUnitsSold;
            return save;
        }

        public void LoadFromSaveDto(FirstSessionGuidanceSaveDto save)
        {
            AutoWire();
            if (save != null && save.initialized)
            {
                guidanceEnabled = save.guidanceEnabled;
                progress.LoadFromSaveDto(save);
                salesBaselineCaptured = save.salesBaselineCaptured;
                salesBaselineDayIndex = save.salesBaselineDayIndex;
                salesBaselineWeekRevenueCents = save.salesBaselineWeekRevenueCents;
                salesBaselineDailyRevenueCents = save.salesBaselineDailyRevenueCents;
                salesBaselineUnitsSold = save.salesBaselineUnitsSold;
            }
            else
            {
                ApplyLegacySaveFallback();
            }

            currentStep = progress.CurrentStep;
            lastObjective = string.Empty;
            lastWhy = string.Empty;
            lastAction = string.Empty;
            lastBlocker = string.Empty;
            RefreshGuidance();
        }

        [Obsolete("Use the Configure overload that accepts PlayerPortfolioManager and TimeManager so restore-side rebinding stays explicit.", false)]
        public void Configure(
            LandLedgersHUDController newHudController,
            GeneralStorePanelController newManagementPanel,
            GeneralStoreRuntimeManager newStoreRuntime,
            AcquisitionMarketManager newAcquisitionMarket,
            SharedBusinessRuntimeManager newSharedBusinessRuntime,
            TownWorldController newTownWorld)
        {
            // Keep the legacy convenience overload for older callers, but refresh the cached
            // portfolio/time sources first so it delegates through the same full-scene path.
            AutoWire();
            Configure(
                newHudController,
                newManagementPanel,
                newStoreRuntime,
                newAcquisitionMarket,
                playerPortfolio,
                newSharedBusinessRuntime,
                newTownWorld,
                timeManager);
        }

        public void Configure(
            LandLedgersHUDController newHudController,
            GeneralStorePanelController newManagementPanel,
            GeneralStoreRuntimeManager newStoreRuntime,
            AcquisitionMarketManager newAcquisitionMarket,
            PlayerPortfolioManager newPlayerPortfolio,
            SharedBusinessRuntimeManager newSharedBusinessRuntime,
            TownWorldController newTownWorld,
            TimeManager newTimeManager)
        {
            // Save/load may need to rebind the guidance manager without rebuilding it,
            // so keep this as the single orchestration path for refreshing scene references.
            hudController = newHudController;
            managementPanel = newManagementPanel;
            storeRuntime = newStoreRuntime;
            acquisitionMarket = newAcquisitionMarket;
            playerPortfolio = newPlayerPortfolio;
            sharedBusinessRuntime = newSharedBusinessRuntime;
            townWorld = newTownWorld;
            timeManager = newTimeManager;
            CaptureBaselinesIfReady();
            RefreshGuidance();
        }

        public void ResetGuidanceForNewSession()
        {
            progress.Reset();
            currentStep = FirstSessionObjectiveStep.OpenManagement;
            salesBaselineCaptured = false;
            salesBaselineDayIndex = -1;
            salesBaselineWeekRevenueCents = 0;
            salesBaselineDailyRevenueCents = 0;
            salesBaselineUnitsSold = 0;
            lastObjective = string.Empty;
            lastWhy = string.Empty;
            lastAction = string.Empty;
            lastBlocker = string.Empty;
            hudController?.ClearFirstSessionGuidance();
            CaptureBaselinesIfReady();
            RefreshGuidance();
        }

        private void Awake()
        {
            AutoWire();
        }

        private void OnEnable()
        {
            RefreshGuidance();
        }

        private void Update()
        {
            RefreshGuidance();
        }

        private void RefreshGuidance()
        {
            AutoWire();
            if (!guidanceEnabled)
            {
                hudController?.ClearFirstSessionGuidance();
                return;
            }

            CaptureBaselinesIfReady();
            FirstSessionGuidanceFacts facts = BuildFacts();
            FirstSessionObjectiveState objective = progress.Evaluate(facts);
            currentStep = objective.Step;
            string blocker = BuildCurrentBlockerText(facts);

            string objectiveText = $"Objective: {objective.Objective}";
            string whyText = $"Why: {BuildWhyText(objective.Step, objective.Why, facts)}";
            string actionText = $"Action: {BuildActionText(objective.Step, objective.Action, facts)}";
            if (objectiveText == lastObjective
                && whyText == lastWhy
                && actionText == lastAction
                && blocker == lastBlocker)
            {
                return;
            }

            lastObjective = objectiveText;
            lastWhy = whyText;
            lastAction = actionText;
            lastBlocker = blocker;
            hudController?.SetFirstSessionGuidance(objectiveText, whyText, actionText, blocker);
        }

        private void CaptureBaselinesIfReady()
        {
            if (progress.BaselinesCaptured || acquisitionMarket == null)
            {
                return;
            }

            progress.CaptureBaselines(acquisitionMarket.OwnedLandCount, acquisitionMarket.OwnedBusinessCount);
        }

        private void ApplyLegacySaveFallback()
        {
            progress.Reset();
            int legacyBusinessBaseline = storeRuntime != null && storeRuntime.CurrentBusiness != null ? 1 : 0;
            progress.CaptureBaselines(0, legacyBusinessBaseline);
            guidanceEnabled = true;
            progress.BackfillFromFacts(BuildFacts());
            currentStep = progress.CurrentStep;
            if (currentStep == FirstSessionObjectiveStep.Complete)
            {
                guidanceEnabled = false;
            }
        }

        private FirstSessionGuidanceFacts BuildFacts()
        {
            bool managementOpen = managementPanel != null && managementPanel.IsVisible;
            bool generalStoreSelected = managementPanel != null && managementPanel.IsGeneralStoreSelected;
            AcquisitionMarketSection acquisitionSection = managementPanel != null
                ? managementPanel.CurrentAcquisitionSection
                : AcquisitionMarketSection.Land;
            bool hasSelectedLandListing = acquisitionMarket != null
                && acquisitionMarket.TryGetSelectedListing(AcquisitionMarketSection.Land, out _);
            if (managementOpen && generalStoreSelected)
            {
                CaptureSalesBaselineIfNeeded();
            }

            string acquisitionCurrentRead = string.Empty;
            string acquisitionNextAction = string.Empty;
            string acquisitionBlocker = string.Empty;
            string acquisitionActionLabel = acquisitionMarket != null
                ? NormalizeAcquisitionActionLabel(acquisitionMarket.GetSelectedAcquisitionActionLabel(AcquisitionMarketSection.Land))
                : string.Empty;

            string propertyGuidanceHint = string.Empty;
            string propertyCurrentRead = string.Empty;
            string propertyNextAction = string.Empty;
            string propertyBlocker = string.Empty;
            bool propertyShellReady = false;
            bool propertyStartupReady = false;
            if (managementPanel != null)
            {
                managementPanel.TryGetCurrentFirstSessionAcquisitionCoaching(
                    out acquisitionCurrentRead,
                    out acquisitionNextAction,
                    out acquisitionBlocker,
                    out acquisitionActionLabel);

                managementPanel.TryGetCurrentFirstSessionPropertyCoaching(
                    out propertyCurrentRead,
                    out propertyNextAction,
                    out propertyBlocker,
                    out propertyShellReady,
                    out propertyStartupReady);
                propertyGuidanceHint = propertyBlocker;
            }

            return new FirstSessionGuidanceFacts
            {
                ManagementOpen = managementOpen,
                CurrentTab = managementPanel != null ? managementPanel.CurrentTab : ManagementPanelTab.Properties,
                CurrentAcquisitionSection = acquisitionSection,
                GeneralStoreSelected = generalStoreSelected,
                GeneralStoreStaffed = storeRuntime != null && storeRuntime.FilledWorkerSlotCount > 0,
                FirstSalesObserved = managementOpen
                    && generalStoreSelected
                    && HasStoreSalesAfterBaseline(),
                OwnedLandCount = acquisitionMarket != null ? acquisitionMarket.OwnedLandCount : 0,
                OwnedBusinessCount = acquisitionMarket != null ? acquisitionMarket.OwnedBusinessCount : 0,
                LandListingCount = acquisitionMarket != null && acquisitionMarket.LandListings != null
                    ? acquisitionMarket.LandListings.Count
                    : 0,
                HasSelectedLandListing = hasSelectedLandListing,
                SelectedLandActionLabel = acquisitionActionLabel ?? string.Empty,
                SelectedAcquisitionCurrentRead = acquisitionCurrentRead ?? string.Empty,
                SelectedAcquisitionNextAction = acquisitionNextAction ?? string.Empty,
                SelectedAcquisitionBlocker = acquisitionBlocker ?? string.Empty,
                OwnerCashCents = playerPortfolio != null ? playerPortfolio.OwnerCashCents : 0,
                StoreCashCents = storeRuntime != null ? storeRuntime.CurrentCashCents : 0,
                StoreProtectedReserveCents = storeRuntime != null ? storeRuntime.ProtectedBusinessCashReserveCents : 0,
                StorePendingReorderUnits = storeRuntime != null ? storeRuntime.LastWeeklyReorderUnitsRemaining : 0,
                StoreCashHeldReorderUnits = storeRuntime != null ? storeRuntime.LastWeeklyCashConstrainedReorderUnits : 0,
                StoreCashHeldReorderCostCents = storeRuntime != null ? storeRuntime.LastWeeklyCashConstrainedReorderCostCents : 0,
                StoreSupplyReadinessLine = storeRuntime != null ? storeRuntime.BuildStoreSupplyReadinessLine() : string.Empty,
                IsSelectedOwnedLand = managementPanel != null && managementPanel.IsSelectedOwnedLand,
                IsSelectedStartableOwnedShell = managementPanel != null && managementPanel.IsSelectedStartableOwnedShell,
                PropertyGuidanceHint = propertyGuidanceHint ?? string.Empty,
                SelectedPropertyCurrentRead = propertyCurrentRead ?? string.Empty,
                SelectedPropertyNextAction = propertyNextAction ?? string.Empty,
                SelectedPropertyBlocker = propertyBlocker ?? string.Empty,
                SelectedPropertyShellReady = propertyShellReady,
                SelectedPropertyStartupReady = propertyStartupReady,
                StartableOwnedShellExists = HasStartableOwnedShell(),
                PlayerOwnedExpansionBusinessExists = HasPlayerOwnedExpansionBusiness()
            };
        }

        private void CaptureSalesBaselineIfNeeded()
        {
            if (salesBaselineCaptured)
            {
                return;
            }

            BusinessRuntimeState runtime = storeRuntime != null ? storeRuntime.RuntimeState : null;
            salesBaselineDayIndex = timeManager != null ? timeManager.CurrentAbsoluteDayIndex : -1;
            salesBaselineWeekRevenueCents = runtime != null ? runtime.WeekToDateRevenueCents : 0;
            salesBaselineDailyRevenueCents = runtime != null ? runtime.LastDailyRevenueCents : 0;
            salesBaselineUnitsSold = storeRuntime != null ? storeRuntime.LastDailyUnitsSold : 0;
            salesBaselineCaptured = true;
        }

        private bool HasStoreSalesAfterBaseline()
        {
            if (!salesBaselineCaptured || storeRuntime == null || storeRuntime.RuntimeState == null)
            {
                return false;
            }

            BusinessRuntimeState runtime = storeRuntime.RuntimeState;
            if (timeManager == null)
            {
                return runtime.WeekToDateRevenueCents > salesBaselineWeekRevenueCents
                    || runtime.LastDailyRevenueCents > salesBaselineDailyRevenueCents
                    || storeRuntime.LastDailyUnitsSold > salesBaselineUnitsSold;
            }

            bool dayAdvanced = timeManager.CurrentAbsoluteDayIndex > salesBaselineDayIndex;
            // Procurement can be healthy store activity, but it is not proof that households bought from the store.
            return dayAdvanced
                && (runtime.LastDailyRevenueCents > 0
                    || runtime.WeekToDateRevenueCents > salesBaselineWeekRevenueCents
                    || storeRuntime.LastDailyUnitsSold > 0);
        }

        private string BuildWhyText(FirstSessionObjectiveStep step, string fallback, FirstSessionGuidanceFacts facts)
        {
            return step switch
            {
                FirstSessionObjectiveStep.ObserveFirstSales => BuildObserveFirstSalesWhy(facts, fallback),
                FirstSessionObjectiveStep.ReviewMoneyModel => BuildMoneyModelWhy(facts),
                FirstSessionObjectiveStep.OpenAcquisitions => BuildOpenAcquisitionsWhy(facts),
                FirstSessionObjectiveStep.CompleteFirstLandPurchase => BuildLandPurchaseWhy(facts),
                FirstSessionObjectiveStep.ReturnToProperties => BuildReturnToPropertiesWhy(facts),
                FirstSessionObjectiveStep.BuildFirstBusinessShell => BuildBusinessShellWhy(facts),
                FirstSessionObjectiveStep.StartFirstBusiness => BuildStartBusinessWhy(facts),
                _ => fallback ?? string.Empty
            };
        }

        private string BuildActionText(FirstSessionObjectiveStep step, string fallback, FirstSessionGuidanceFacts facts)
        {
            return step switch
            {
                FirstSessionObjectiveStep.ObserveFirstSales => BuildObserveFirstSalesAction(facts, fallback),
                FirstSessionObjectiveStep.ReviewMoneyModel => BuildMoneyModelAction(facts),
                FirstSessionObjectiveStep.OpenAcquisitions => BuildOpenAcquisitionsAction(facts),
                FirstSessionObjectiveStep.CompleteFirstLandPurchase => BuildLandPurchaseAction(facts),
                FirstSessionObjectiveStep.ReturnToProperties => BuildReturnToPropertiesAction(facts),
                FirstSessionObjectiveStep.BuildFirstBusinessShell => BuildBusinessShellAction(facts),
                FirstSessionObjectiveStep.StartFirstBusiness => BuildStartBusinessAction(facts),
                _ => fallback ?? string.Empty
            };
        }

        private string BuildObserveFirstSalesWhy(FirstSessionGuidanceFacts facts, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(facts.StoreSupplyReadinessLine)
                && !IsSteadySupplyLine(facts.StoreSupplyReadinessLine))
            {
                return $"Sales prove households are buying from the store; {LowercaseFirst(facts.StoreSupplyReadinessLine)}";
            }

            return fallback ?? string.Empty;
        }

        private string BuildObserveFirstSalesAction(FirstSessionGuidanceFacts facts, string fallback)
        {
            if (!facts.ManagementOpen)
            {
                return "Press C, select the General Store, then let time run until a real sale posts.";
            }

            if (!facts.GeneralStoreSelected)
            {
                return "Select the General Store row so you can watch revenue and units sold, then let time run.";
            }

            return fallback ?? "Let time run and watch for actual revenue or units sold; procurement alone does not complete this step.";
        }

        private string BuildMoneyModelWhy(FirstSessionGuidanceFacts facts)
        {
            int transferable = Mathf.Max(0, facts.StoreCashCents - facts.StoreProtectedReserveCents);
            if (facts.StoreCashHeldReorderUnits > 0)
            {
                return $"Store Cash is working capital: {facts.StoreCashHeldReorderUnits} reorder unit(s) are already held for Store Cash ({FormatMoney(facts.StoreCashHeldReorderCostCents)}), while Liquid Cash / Owner Cash funds deals and projects.";
            }

            return $"Store Cash keeps the store operating; only surplus above reserve is safely transferable. Liquid Cash / Owner Cash funds deals and projects. Current safe surplus: {FormatMoney(transferable)}.";
        }

        private string BuildMoneyModelAction(FirstSessionGuidanceFacts facts)
        {
            if (!facts.ManagementOpen)
            {
                return "Press C, then open Finances to compare Liquid Cash / Owner Cash against Store Cash.";
            }

            if (facts.CurrentTab != ManagementPanelTab.Finances)
            {
                return "Open the Finances tab and compare Liquid Cash / Owner Cash, Store Cash, operating reserve, and transferable surplus.";
            }

            return "Read Store Cash as operating capital first; withdraw only true surplus if you need more Liquid Cash / Owner Cash for expansion.";
        }

        private string BuildOpenAcquisitionsWhy(FirstSessionGuidanceFacts facts)
        {
            if (facts.OwnerCashCents <= 0)
            {
                return "Acquisitions spend Liquid Cash / Owner Cash, not Store Cash, so you need expansion money ready before a land deal can close.";
            }

            if (!string.IsNullOrWhiteSpace(facts.SelectedAcquisitionCurrentRead))
            {
                return $"{facts.SelectedAcquisitionCurrentRead} Liquid Cash / Owner Cash funds expansion, and the Land market is where the first new parcel enters your portfolio.";
            }

            return "Liquid Cash / Owner Cash funds expansion, and the Land market is where the first new parcel enters your portfolio.";
        }

        private string BuildLandPurchaseWhy(FirstSessionGuidanceFacts facts)
        {
            if (facts.CurrentAcquisitionSection != AcquisitionMarketSection.Land)
            {
                return "The first expansion loop starts from a land parcel, so stay on the Land market until the parcel closes.";
            }

            if (!string.IsNullOrWhiteSpace(facts.SelectedAcquisitionCurrentRead))
            {
                return facts.SelectedLandActionLabel switch
                {
                    "Open Inquiry" => $"{facts.SelectedAcquisitionCurrentRead} Start the deal cleanly so the seller and the desk move from listing to a real lead.",
                    "Commit Earnest" => $"{facts.SelectedAcquisitionCurrentRead} Earnest shows seriousness and holds the lane before you spend more time on diligence.",
                    "Run Diligence" => $"{facts.SelectedAcquisitionCurrentRead} Diligence is where you check fit, risk, and practical value before committing harder.",
                    "Agree Terms" => $"{facts.SelectedAcquisitionCurrentRead} Use what you learned to settle price, structure, and timing before closing.",
                    "Close Acquisition" => $"{facts.SelectedAcquisitionCurrentRead} Closing turns the lead into owned land so you can move back to Properties and improve it.",
                    _ => $"{facts.SelectedAcquisitionCurrentRead} Land ownership is the bridge between store profits and the next improved business site."
                };
            }

            return facts.SelectedLandActionLabel switch
            {
                "Open Inquiry" => "Start the deal cleanly so the seller and the desk move from listing to a real lead.",
                "Commit Earnest" => "Earnest shows seriousness and holds the lane before you spend more time on diligence.",
                "Run Diligence" => "Diligence is where you check fit, risk, and practical value before committing harder.",
                "Agree Terms" => "Use what you learned to settle price, structure, and timing before closing.",
                "Close Acquisition" => "Closing turns the lead into owned land so you can move back to Properties and improve it.",
                _ => "Land ownership is the bridge between store profits and the next improved business site."
            };
        }

        private string BuildOpenAcquisitionsAction(FirstSessionGuidanceFacts facts)
        {
            if (!facts.ManagementOpen)
            {
                return "Press C, then open the Acquisitions tab.";
            }

            if (facts.CurrentTab != ManagementPanelTab.Acquisitions)
            {
                return "Open the Acquisitions tab.";
            }

            if (facts.CurrentAcquisitionSection != AcquisitionMarketSection.Land)
            {
                return "Switch the market to Land for the first expansion parcel.";
            }

            if (!string.IsNullOrWhiteSpace(facts.SelectedAcquisitionNextAction))
            {
                return NormalizeGuidanceAction(facts.SelectedAcquisitionNextAction);
            }

            return "Stay on Land, inspect the live lead, and read the deal desk before committing Liquid Cash / Owner Cash.";
        }

        private string BuildLandPurchaseAction(FirstSessionGuidanceFacts facts)
        {
            if (!facts.ManagementOpen)
            {
                return "Press C, open Acquisitions, and switch to Land.";
            }

            if (facts.CurrentTab != ManagementPanelTab.Acquisitions)
            {
                return "Open the Acquisitions tab and switch to Land.";
            }

            if (facts.CurrentAcquisitionSection != AcquisitionMarketSection.Land)
            {
                return "Switch the market to Land for the first expansion parcel.";
            }

            if (facts.LandListingCount <= 0)
            {
                return "Stay on Land and wait for a live land listing.";
            }

            if (!facts.HasSelectedLandListing)
            {
                return "Use the listing controls to choose a live land lead.";
            }

            if (!string.IsNullOrWhiteSpace(facts.SelectedAcquisitionNextAction))
            {
                return NormalizeGuidanceAction(facts.SelectedAcquisitionNextAction);
            }

            return facts.SelectedLandActionLabel switch
            {
                "Open Inquiry" => "Inspect the selected parcel, then press Open Inquiry to start the deal.",
                "Commit Earnest" => "Press Commit Earnest to reserve the lane before deeper work.",
                "Run Diligence" => "Press Run Diligence, then read fit, pressure, and readiness before terms.",
                "Agree Terms" => "Press Agree Terms when the lead still reads sensible for your cash and next build plan.",
                "Close Acquisition" => "Press Close Acquisition to move the parcel into your owned properties.",
                "Closed" => "The parcel is effectively closed; return to Properties when the owned plot appears.",
                _ => "Stay on Land and keep advancing the selected lead until the parcel closes."
            };
        }

        private string BuildReturnToPropertiesWhy(FirstSessionGuidanceFacts facts)
        {
            if (facts.CurrentTab != ManagementPanelTab.Properties)
            {
                return "The land deal is done; the next step moves out of the market and back into Properties where owned sites are improved.";
            }

            if (facts.IsSelectedStartableOwnedShell)
            {
                return "The parcel is already improved into a shell, so startup is now the next move from Properties.";
            }

            if (facts.IsSelectedOwnedLand)
            {
                return !string.IsNullOrWhiteSpace(facts.SelectedPropertyCurrentRead)
                    ? $"{facts.SelectedPropertyCurrentRead} This is the correct site for the next expansion step."
                    : "The owned plot is the correct site. From here you can set business intent and prepare the first shell.";
            }

            return "Construction and startup happen from the owned property list, not the acquisition desk after closing.";
        }

        private string BuildBusinessShellWhy(FirstSessionGuidanceFacts facts)
        {
            if (facts.IsSelectedStartableOwnedShell)
            {
                return "The shell is already built, so the next move is startup rather than another construction step.";
            }

            if (facts.CurrentTab != ManagementPanelTab.Properties)
            {
                return "Shell construction is a property action on owned land, not a market action.";
            }

            if (!facts.IsSelectedOwnedLand)
            {
                return "The first shell has to be built on an owned plot you control.";
            }

            if (facts.SelectedPropertyShellReady)
            {
                return "The selected owned plot is already staged for shell construction, so the next move is to build rather than reopen the market.";
            }

            if (!string.IsNullOrWhiteSpace(facts.SelectedPropertyBlocker))
            {
                return LooksLikeBusinessIntentSwitchHint(facts.SelectedPropertyBlocker)
                    ? "The first expansion site should be a business parcel, not a house, so the shell becomes a commercial holding."
                    : "The owned plot is selected, but shell construction is still blocked by a practical readiness issue that needs to be cleared first.";
            }

            if (!string.IsNullOrWhiteSpace(facts.SelectedPropertyCurrentRead))
            {
                return $"{facts.SelectedPropertyCurrentRead} This plot turns from raw land into a usable commercial shell.";
            }

            return "The shell turns raw land into a usable commercial site that can host a real business.";
        }

        private string BuildStartBusinessWhy(FirstSessionGuidanceFacts facts)
        {
            if (facts.CurrentTab != ManagementPanelTab.Properties)
            {
                return "Startup happens from the vacant building you already own, not from the acquisition market.";
            }

            if (!facts.IsSelectedStartableOwnedShell)
            {
                return "A built shell becomes startable once you select the vacant building from Properties.";
            }

            if (facts.SelectedPropertyStartupReady)
            {
                return "The selected shell is already ready for startup, so the next move is to open the business rather than keep building.";
            }

            if (!string.IsNullOrWhiteSpace(facts.SelectedPropertyBlocker))
            {
                return "The vacant shell is selected, but startup is blocked by fit-out or readiness needs that have to be cleared first.";
            }

            if (!string.IsNullOrWhiteSpace(facts.SelectedPropertyCurrentRead))
            {
                return $"{facts.SelectedPropertyCurrentRead} Startup turns this shell into an operating business with its own pressure and cash flow.";
            }

            return "Starting the business turns the shell into a live business with its own cash, staffing, and operating pressure.";
        }

        private string BuildReturnToPropertiesAction(FirstSessionGuidanceFacts facts)
        {
            if (!facts.ManagementOpen)
            {
                return "Press C, open Properties, and select the new Plot row.";
            }

            if (facts.CurrentTab != ManagementPanelTab.Properties)
            {
                return "Open the Properties tab.";
            }

            if (facts.IsSelectedOwnedLand)
            {
                return "Keep the new plot selected; this is the site you will improve next.";
            }

            if (facts.IsSelectedStartableOwnedShell)
            {
                return "The shell is already selected. Move straight into startup when you are ready.";
            }

            return "Select the new Plot row from Properties.";
        }

        private string BuildBusinessShellAction(FirstSessionGuidanceFacts facts)
        {
            if (!facts.ManagementOpen)
            {
                return "Press C, open Properties, and select the owned plot.";
            }

            if (facts.CurrentTab != ManagementPanelTab.Properties)
            {
                return "Open Properties and select the owned plot.";
            }

            if (!facts.IsSelectedOwnedLand)
            {
                return "Select the owned Plot row that will become the first expansion site.";
            }

            if (!string.IsNullOrWhiteSpace(facts.SelectedPropertyNextAction))
            {
                return NormalizeGuidanceAction(facts.SelectedPropertyNextAction);
            }

            if (!string.IsNullOrWhiteSpace(facts.PropertyGuidanceHint))
            {
                return NormalizeGuidanceAction(facts.PropertyGuidanceHint);
            }

            return "Set the plot to Business, choose a business, use Next Shell to pick the plan, then press Build Shell.";
        }

        private string BuildStartBusinessAction(FirstSessionGuidanceFacts facts)
        {
            if (!facts.ManagementOpen)
            {
                return "Press C, open Properties, and select the vacant building shell.";
            }

            if (facts.CurrentTab != ManagementPanelTab.Properties)
            {
                return "Open Properties and select the vacant building shell.";
            }

            if (!facts.IsSelectedStartableOwnedShell)
            {
                if (facts.IsSelectedOwnedLand)
                {
                    return "Build the shell first. Then select the vacant building once it appears in Properties.";
                }

                return "Select the vacant building shell from Properties.";
            }

            if (!string.IsNullOrWhiteSpace(facts.SelectedPropertyNextAction))
            {
                return NormalizeGuidanceAction(facts.SelectedPropertyNextAction);
            }

            if (!string.IsNullOrWhiteSpace(facts.PropertyGuidanceHint))
            {
                return NormalizeGuidanceAction(facts.PropertyGuidanceHint);
            }

            return "Choose the business with Prev/Next Business, then press Start Business.";
        }

        private string BuildCurrentBlockerText(FirstSessionGuidanceFacts facts)
        {
            if (currentStep == FirstSessionObjectiveStep.HireFirstWorker)
            {
                string staffingBlocker = BuildStoreStaffingBlocker();
                if (!string.IsNullOrWhiteSpace(staffingBlocker))
                {
                    return staffingBlocker;
                }
            }

            if (currentStep == FirstSessionObjectiveStep.ObserveFirstSales)
            {
                string salesBlocker = BuildObserveFirstSalesBlocker(facts);
                if (!string.IsNullOrWhiteSpace(salesBlocker))
                {
                    return salesBlocker;
                }
            }

            if (currentStep == FirstSessionObjectiveStep.ReviewMoneyModel)
            {
                string moneyBlocker = BuildMoneyModelBlocker(facts);
                if (!string.IsNullOrWhiteSpace(moneyBlocker))
                {
                    return moneyBlocker;
                }
            }

            if (currentStep == FirstSessionObjectiveStep.CompleteFirstLandPurchase)
            {
                string acquisitionBlocker = BuildAcquisitionBlocker(facts);
                if (!string.IsNullOrWhiteSpace(acquisitionBlocker))
                {
                    return acquisitionBlocker;
                }
            }

            if (currentStep == FirstSessionObjectiveStep.ReturnToProperties)
            {
                string propertyReturnBlocker = BuildReturnToPropertiesBlocker(facts);
                if (!string.IsNullOrWhiteSpace(propertyReturnBlocker))
                {
                    return propertyReturnBlocker;
                }
            }

            if (currentStep == FirstSessionObjectiveStep.BuildFirstBusinessShell)
            {
                string buildShellBlocker = BuildBusinessShellBlocker(facts);
                if (!string.IsNullOrWhiteSpace(buildShellBlocker))
                {
                    return buildShellBlocker;
                }
            }

            if (currentStep == FirstSessionObjectiveStep.StartFirstBusiness)
            {
                string startupBlocker = BuildStartBusinessBlocker(facts);
                if (!string.IsNullOrWhiteSpace(startupBlocker))
                {
                    return startupBlocker;
                }
            }

            if (managementPanel == null || !managementPanel.IsVisible)
            {
                return string.Empty;
            }

            if (managementPanel.TryGetCurrentFirstSessionBlockerGuidance(out string panelBlocker)
                && !string.IsNullOrWhiteSpace(panelBlocker))
            {
                return panelBlocker;
            }

            string status = managementPanel.CurrentPanelStatus;
            if (!LooksLikeBlocker(status))
            {
                return string.Empty;
            }

            return $"Blocked: {Compact(status)} {FirstSessionGuidanceText.BuildConstructionBlockerNextStep(status)}";
        }

        private string BuildObserveFirstSalesBlocker(FirstSessionGuidanceFacts facts)
        {
            if (facts.StoreCashHeldReorderUnits > 0)
            {
                return $"Some reorder stock is held for Store Cash ({facts.StoreCashHeldReorderUnits}u / {FormatMoney(facts.StoreCashHeldReorderCostCents)}). Deposit cash or let the store earn before expecting stronger sales.";
            }

            if (facts.StorePendingReorderUnits > 0)
            {
                return $"{facts.StorePendingReorderUnits} reorder unit(s) are still pending cash or freight. Keep the store selected and let actual sales, not procurement, post before moving on.";
            }

            return string.Empty;
        }

        private string BuildMoneyModelBlocker(FirstSessionGuidanceFacts facts)
        {
            if (!facts.ManagementOpen || facts.CurrentTab != ManagementPanelTab.Finances)
            {
                return string.Empty;
            }

            if (facts.StoreCashHeldReorderUnits > 0)
            {
                return $"Working-capital warning: {facts.StoreCashHeldReorderUnits} reorder unit(s) are held for Store Cash. Avoid drawing the store below reserve before expansion.";
            }

            if (facts.StoreCashCents < facts.StoreProtectedReserveCents)
            {
                return $"Store Cash is below its protected reserve ({FormatMoney(facts.StoreCashCents)} / {FormatMoney(facts.StoreProtectedReserveCents)}). Deposit Liquid Cash / Owner Cash before drawing.";
            }

            return string.Empty;
        }

        private string BuildReturnToPropertiesBlocker(FirstSessionGuidanceFacts facts)
        {
            if (!facts.ManagementOpen || facts.CurrentTab != ManagementPanelTab.Properties)
            {
                return string.Empty;
            }

            if (facts.IsSelectedOwnedLand || facts.IsSelectedStartableOwnedShell)
            {
                return string.Empty;
            }

            if (facts.StartableOwnedShellExists)
            {
                return "The shell is built, but it is not selected. Pick the vacant building from Properties.";
            }

            if (facts.OwnedLandCount > 0)
            {
                return "The parcel is owned, but no plot is selected. Pick the new Plot row from Properties.";
            }

            return string.Empty;
        }

        private string BuildBusinessShellBlocker(FirstSessionGuidanceFacts facts)
        {
            if (!facts.ManagementOpen || facts.CurrentTab != ManagementPanelTab.Properties)
            {
                return string.Empty;
            }

            if (facts.IsSelectedStartableOwnedShell)
            {
                return "The shell is already built. Select the vacant building and move to startup.";
            }

            if (!facts.IsSelectedOwnedLand)
            {
                return facts.OwnedLandCount > 0
                    ? "No owned plot is selected. Pick the new Plot row before you try to build the first shell."
                    : string.Empty;
            }

            if (facts.SelectedPropertyShellReady)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(facts.SelectedPropertyBlocker))
            {
                return NormalizeGuidanceAction(facts.SelectedPropertyBlocker);
            }

            return string.IsNullOrWhiteSpace(facts.PropertyGuidanceHint)
                ? string.Empty
                : NormalizeGuidanceAction(facts.PropertyGuidanceHint);
        }

        private string BuildStartBusinessBlocker(FirstSessionGuidanceFacts facts)
        {
            if (!facts.ManagementOpen || facts.CurrentTab != ManagementPanelTab.Properties)
            {
                return string.Empty;
            }

            if (!facts.IsSelectedStartableOwnedShell)
            {
                if (facts.PlayerOwnedExpansionBusinessExists)
                {
                    return string.Empty;
                }

                if (facts.StartableOwnedShellExists)
                {
                    return "A vacant shell is ready, but it is not selected. Pick the new building from Properties.";
                }

                if (facts.IsSelectedOwnedLand)
                {
                    return "The shell is not built yet. Finish construction before startup.";
                }

                return string.Empty;
            }

            if (facts.SelectedPropertyStartupReady)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(facts.SelectedPropertyBlocker))
            {
                return NormalizeGuidanceAction(facts.SelectedPropertyBlocker);
            }

            return string.IsNullOrWhiteSpace(facts.PropertyGuidanceHint)
                ? string.Empty
                : NormalizeGuidanceAction(facts.PropertyGuidanceHint);
        }

        private string BuildStoreStaffingBlocker()
        {
            if (storeRuntime == null)
            {
                return "Store staffing is unavailable right now. Reopen Management and inspect the General Store.";
            }

            if (storeRuntime.FilledWorkerSlotCount > 0)
            {
                return string.Empty;
            }

            if (storeRuntime.OpenWorkerSlotCount <= 0)
            {
                return "No store roles are open right now. Check Staffing again after the store setup updates.";
            }

            if (storeRuntime.GetAvailableWorkerCandidateCount() <= 0)
            {
                return "No eligible workers are ready. Let the labor pool refresh, then check Staffing again.";
            }

            if (storeRuntime.CurrentCashCents <= 0)
            {
                return "Store Cash is empty. Use Finances to deposit Liquid Cash / Owner Cash before hiring.";
            }

            return string.Empty;
        }

        private string BuildAcquisitionBlocker(FirstSessionGuidanceFacts facts)
        {
            if (acquisitionMarket == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(facts.SelectedAcquisitionBlocker))
            {
                return NormalizeGuidanceAction(facts.SelectedAcquisitionBlocker);
            }

            if (facts.CurrentAcquisitionSection != AcquisitionMarketSection.Land)
            {
                return "Switch to Land for the first expansion parcel.";
            }

            if (facts.LandListingCount <= 0)
            {
                return "No land is listed. Let the market refresh, then check Land again.";
            }

            if (!facts.HasSelectedLandListing
                || string.IsNullOrWhiteSpace(facts.SelectedLandActionLabel)
                || string.Equals(facts.SelectedLandActionLabel, "No Listing", StringComparison.OrdinalIgnoreCase))
            {
                return "No live land lead is selected. Use the listing controls to inspect a parcel first.";
            }

            if (facts.OwnerCashCents <= 0)
            {
                return "Liquid Cash / Owner Cash is empty. Store Cash keeps the store alive; wait for distributions, manually draw business cash with reserve risk in mind, or use a bank loan that pays into Liquid Cash / Owner Cash.";
            }

            return string.Empty;
        }

        private static bool LooksLikeBusinessIntentSwitchHint(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.ToLowerInvariant();
            return normalized.Contains("switch the plot to business")
                || normalized.Contains("set this plot to business")
                || normalized.Contains("business for the first expansion");
        }

        private static string NormalizeGuidanceAction(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            if (trimmed.StartsWith("Next:", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring("Next:".Length).Trim();
            }

            return trimmed;
        }

        private static string NormalizeAcquisitionActionLabel(string label)
        {
            return string.Equals(label, "Close", StringComparison.OrdinalIgnoreCase)
                ? "Close Acquisition"
                : (label ?? string.Empty);
        }

        private bool HasStartableOwnedShell()
        {
            if (townWorld == null || townWorld.Buildings == null)
            {
                return false;
            }

            int storeBuildingId = storeRuntime != null ? storeRuntime.StoreBuildingId : -1;
            for (int i = 0; i < townWorld.Buildings.Count; i++)
            {
                PlacedBuilding building = townWorld.Buildings[i];
                if (building == null
                    || !building.playerOwned
                    || building.id == storeBuildingId
                    || building.definition == null)
                {
                    continue;
                }

                if (sharedBusinessRuntime == null || sharedBusinessRuntime.FindByBuildingId(building.id) == null)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasPlayerOwnedExpansionBusiness()
        {
            if (sharedBusinessRuntime == null || sharedBusinessRuntime.Businesses == null)
            {
                return false;
            }

            int storeBuildingId = storeRuntime != null ? storeRuntime.StoreBuildingId : -1;
            for (int i = 0; i < sharedBusinessRuntime.Businesses.Count; i++)
            {
                BusinessInstanceState business = sharedBusinessRuntime.Businesses[i];
                if (business == null
                    || business.BusinessType == BusinessType.GeneralStore
                    || business.AssignedBuildingId == storeBuildingId
                    || business.Owner == null
                    || business.Owner.OwnerKind != BusinessOwnerKind.Player)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private void AutoWire()
        {
            hudController ??= FindAnyObjectByType<LandLedgersHUDController>();
            managementPanel ??= FindAnyObjectByType<GeneralStorePanelController>();
            storeRuntime ??= FindAnyObjectByType<GeneralStoreRuntimeManager>();
            acquisitionMarket ??= FindAnyObjectByType<AcquisitionMarketManager>();
            playerPortfolio ??= FindAnyObjectByType<PlayerPortfolioManager>();
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
            townWorld ??= FindAnyObjectByType<TownWorldController>();
            timeManager ??= FindAnyObjectByType<TimeManager>();
        }

        private static bool IsSteadySupplyLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            string normalized = value.ToLowerInvariant();
            return normalized.Contains("stock steady") || normalized.Contains("local intake received") || normalized.Contains("freight received");
        }

        private static string LowercaseFirst(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            return trimmed.Length == 1
                ? trimmed.ToLowerInvariant()
                : char.ToLowerInvariant(trimmed[0]) + trimmed.Substring(1);
        }

        private static string FormatMoney(int cents)
        {
            int absoluteCents = Mathf.Abs(cents);
            string amount = "$" + (absoluteCents / 100f).ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
            return cents < 0 ? "-" + amount : amount;
        }

        private static bool LooksLikeBlocker(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.ToLowerInvariant();
            return normalized.Contains("blocked")
                || normalized.Contains("not enough")
                || normalized.Contains("missing")
                || normalized.Contains("unavailable")
                || normalized.Contains("cannot")
                || normalized.Contains("can't");
        }

        private static string Compact(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim().Replace('\n', ' ');
            return trimmed.Length <= 120 ? trimmed : trimmed.Substring(0, 117) + "...";
        }
    }
}
