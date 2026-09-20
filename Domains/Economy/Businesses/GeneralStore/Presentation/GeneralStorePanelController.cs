using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LandLedgers.CameraSystem;
using LandLedgers.Civic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Valuation;
using LandLedgers.Population;
using LandLedgers.Time;
using LandLedgers.UI;
using LandLedgers.World;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LandLedgers.MVP
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(320)]
    public sealed class GeneralStorePanelController : MonoBehaviour
    {
        private const string UiCanvasTag = "UI Canvas";
        private const int ManagementPanelTabCount = 5;
        private enum OwnedKind { Store, Renting, Business, Holding, Land }
        private enum OwnedLandDevelopmentIntent { House, Business }

        private readonly struct FinanceBusinessSnapshot
        {
            public FinanceBusinessSnapshot(
                BusinessInstanceState business,
                BusinessPortfolioSummary summary,
                int ownedEntryIndex,
                string label,
                bool isGeneralStore,
                int lastDistributionCents,
                string pressure,
                string action)
            {
                Business = business;
                Summary = summary;
                OwnedEntryIndex = ownedEntryIndex;
                Label = label;
                IsGeneralStore = isGeneralStore;
                LastDistributionCents = lastDistributionCents;
                Pressure = pressure;
                Action = action;
            }

            public readonly BusinessInstanceState Business;
            public readonly BusinessPortfolioSummary Summary;
            public readonly int OwnedEntryIndex;
            public readonly string Label;
            public readonly bool IsGeneralStore;
            public readonly int LastDistributionCents;
            public readonly string Pressure;
            public readonly string Action;
            public int CurrentCashCents => Summary.BusinessCashCents;
            public int ProtectedReserveCents => Summary.SurvivalReserveCents;
            public int WithdrawableCashCents => Summary.TransferableCashCents;
            public string Health => Summary.FinanceHealthLabel;
        }

        private readonly struct OwnedEntry
        {
            public OwnedEntry(OwnedKind kind, int buildingId, int plotId, string label)
            {
                Kind = kind;
                BuildingId = buildingId;
                PlotId = plotId;
                Label = label;
            }

            public readonly OwnedKind Kind;
            public readonly int BuildingId;
            public readonly int PlotId;
            public readonly string Label;
            public string SelectionKey => $"{Kind}:{BuildingId}:{PlotId}";
            public string Signature => $"{SelectionKey}:{Label}";
        }

        [Header("Sources")]
        [SerializeField] private GeneralStoreRuntimeManager storeRuntime;
        [SerializeField] private AcquisitionMarketManager acquisitionMarket;
        [SerializeField] private SharedBusinessRuntimeManager sharedBusinessRuntime;
        [SerializeField] private PlayerDebtManager playerDebtManager;
        [SerializeField] private PlayerPortfolioManager playerPortfolio;
        [SerializeField] private CivicFoundationManager civicFoundation;
        [SerializeField] private PopulationManager populationManager;
        [SerializeField] private TownWorldController townWorld;
        [SerializeField] private OpportunityPressureRuntimeManager opportunityPressureRuntime;
        [SerializeField] private StrategyCameraController strategyCamera;
        [SerializeField] private OwnershipWorkflowController workflowController;
        [SerializeField] private AcquisitionSellerMeetingController sellerMeetingController;

        [Header("Persistent UI")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private ManagementPanelView view;

        [Header("Input")]
        [SerializeField] private bool enableKeyboardToggle = true;
        [SerializeField, Tooltip("C is near WASD and does not conflict with the current camera bindings.")]
        private Key managementToggleKey = Key.C;
        [SerializeField] private bool enablePanelKeyboardShortcuts = true;
        [SerializeField] private Key closePanelKey = Key.Escape;
        [SerializeField] private Key focusSelectionKey = Key.V;
        [SerializeField] private bool startVisible;
        [SerializeField] private ManagementPanelTab startTab = ManagementPanelTab.Properties;

        private readonly List<OwnedEntry> ownedEntries = new();
        private readonly List<Button> ownedEntryButtons = new();
        private readonly List<GameObject> spawnedRows = new();
        private readonly List<Button> financeBusinessButtons = new();
        private readonly List<GameObject> spawnedFinanceRows = new();
        private bool visible;
        private bool listenersBound;
        private int selectedOwnedIndex;
        private int selectedWorkerCandidateIndex;
        private int selectedBuildOptionIndex;
        private int selectedBusinessBuildOptionIndex;
        private int selectedHouseholdUpgradeIndex;
        private int selectedBusinessActivationIndex;
        private int selectedBusinessActivationBuildingId = -1;
        private int selectedBusinessDevelopmentPlotId = -1;
        private OwnedLandDevelopmentIntent selectedOwnedLandDevelopmentIntent = OwnedLandDevelopmentIntent.Business;
        private string ownedSignature = string.Empty;
        private string financeBusinessSignature = string.Empty;
        private string pendingOwnedContinuityStatus = string.Empty;
        private string panelStatus = "Ready.";
        private ManagementPanelTab currentTab;
        private AcquisitionMarketSection acquisitionSection = AcquisitionMarketSection.Land;

        public bool IsVisible => visible;
        public ManagementPanelTab CurrentTab => currentTab;
        public AcquisitionMarketSection CurrentAcquisitionSection => acquisitionSection;
        public string CurrentPanelStatus => panelStatus ?? string.Empty;
        public bool IsGeneralStoreSelected => TryGetSelectedOwnedEntry(out OwnedEntry selected) && selected.Kind == OwnedKind.Store;
        public bool IsSelectedGeneralStore => IsGeneralStoreSelected;
        public bool IsSelectedOwnedLand => TryGetSelectedOwnedEntry(out OwnedEntry selected) && selected.Kind == OwnedKind.Land;
        public bool IsSelectedStartableOwnedShell => TryGetSelectedOwnedEntry(out OwnedEntry selected) && IsStartableOwnedShell(selected);

        // Shared controller-side action contract for property development/readiness.
        // Keep HUD coaching, button state, row reads, and detail text on this same contract
        // so first-session guidance stays aligned when a plot advances into a shell or business.
        private readonly struct PropertyProjectActionState
        {
            public readonly string ActionLabel;
            public readonly string SelectedLabel;
            public readonly string ReadyText;
            public readonly string BlockedReason;
            public readonly string NextMove;
            public readonly bool IsBlocked;
            public readonly bool IsComplete;

            public PropertyProjectActionState(
                string actionLabel,
                string selectedLabel,
                string readyText,
                string blockedReason,
                string nextMove,
                bool isBlocked,
                bool isComplete)
            {
                ActionLabel = actionLabel ?? string.Empty;
                SelectedLabel = selectedLabel ?? string.Empty;
                ReadyText = readyText ?? string.Empty;
                BlockedReason = blockedReason ?? string.Empty;
                NextMove = nextMove ?? string.Empty;
                IsBlocked = isBlocked;
                IsComplete = isComplete;
            }
        }

        private void Awake()
        {
            AutoWireSources();
            ResolveView();
            currentTab = startTab;
            SetTab(startTab);
            SetVisible(startVisible);
        }

        private void OnEnable()
        {
            AddButtonListeners();
        }

        private void OnDisable()
        {
            RemoveButtonListeners();
            SetCameraArrowKeySuppression(false);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || UnityEditor.EditorApplication.isCompiling)
            {
                return;
            }

            canvas ??= ResolveCanvas();
            if (view == null && canvas != null)
            {
                view = canvas.GetComponentInChildren<ManagementPanelView>(true);
            }

            view?.BindFromChildren();
        }
#endif

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (enableKeyboardToggle && WasKeyPressed(keyboard, managementToggleKey))
            {
                SetVisible(!visible);
            }

            if (visible)
            {
                HandlePanelKeyboardShortcuts(keyboard);
            }

            if (visible)
            {
                Refresh();
            }
        }

        public void Refresh()
        {
            AutoWireSources();
            if (!ResolveView())
            {
                return;
            }

            storeRuntime?.InitializeIfNeeded();
            sharedBusinessRuntime?.InitializeIfNeeded(storeRuntime != null ? storeRuntime.CurrentBusiness : null);
            civicFoundation?.RefreshFromWorld();
            RebuildOwnedRowsIfNeeded();
            SetButtonLabel(view.GovernmentTabButton, "Civic");
            SetButtonLabel(view.ResourcesTabButton, "Resources");
            view.ShowTab(currentTab);
            UpdateShellText();
            UpdateProperties();
            UpdateFinances();
            UpdateAcquisitions();
            UpdateGovernment();
            UpdateResources();
            RefreshDynamicTooltips();
        }

        public void OpenOwnedBuildingManagement(int buildingId)
        {
            AutoWireSources();
            RebuildOwnedRowsIfNeeded();
            SelectOwnedBuildingIfPresent(buildingId);
            SetVisible(true);
            SetTab(ManagementPanelTab.Properties);
        }

        public void OpenOwnedPlotManagement(int plotId)
        {
            AutoWireSources();
            RebuildOwnedRowsIfNeeded();
            SelectOwnedPlotIfPresent(plotId);
            SetVisible(true);
            SetTab(ManagementPanelTab.Properties);
        }

        public void OpenAcquisitionLeadForBuilding(int buildingId)
        {
            AutoWireSources();
            if (acquisitionMarket != null
                && acquisitionMarket.TrySelectListingByBuildingId(buildingId, out AcquisitionMarketSection section, out AcquisitionListing listing))
            {
                acquisitionSection = section;
                SetVisible(true);
                SetTab(ManagementPanelTab.Acquisitions);
                EnsureWorkflowController()?.OpenAcquisitionWorkflow(listing, section);
                return;
            }

            OpenPropertyInspection(-1, buildingId);
        }

        public void OpenAcquisitionLeadForPlot(int plotId)
        {
            AutoWireSources();
            if (acquisitionMarket != null
                && acquisitionMarket.TrySelectListingByPlotId(plotId, out AcquisitionMarketSection section, out AcquisitionListing listing))
            {
                acquisitionSection = section;
                SetVisible(true);
                SetTab(ManagementPanelTab.Acquisitions);
                EnsureWorkflowController()?.OpenAcquisitionWorkflow(listing, section);
                return;
            }

            OpenPropertyInspection(plotId, -1);
        }

        public void OpenPropertyInspection(int plotId, int buildingId)
        {
            AutoWireSources();
            SetVisible(false);
            EnsureWorkflowController()?.OpenInspection(plotId, buildingId);
        }

        public void OpenLoanWorkflow()
        {
            AutoWireSources();
            SetVisible(true);
            SetTab(ManagementPanelTab.Finances);
            EnsureWorkflowController()?.OpenLoanWorkflow();
        }

        private void SetVisible(bool newVisible)
        {
            bool changed = visible != newVisible;
            visible = newVisible;
            SetCameraArrowKeySuppression(visible);
            if (ResolveView())
            {
                view.SetVisible(visible);
                if (changed)
                {
                    LLFeedbackService.Play(
                        visible ? LLFeedbackKind.UIPanel : LLFeedbackKind.UIBack,
                        visible ? "management panel open" : "management panel close",
                        canvas);
                }

                if (visible)
                {
                    Refresh();
                }
            }
        }

        private void SetTab(ManagementPanelTab tab)
        {
            bool changed = currentTab != tab;
            currentTab = tab;
            if (ResolveView())
            {
                view.ShowTab(tab);
                if (changed)
                {
                    LLFeedbackService.Play(LLFeedbackKind.UISelect, $"management {tab} tab switch", canvas);
                }
            }
            Refresh();
        }

        private void HandlePanelKeyboardShortcuts(Keyboard keyboard)
        {
            if (!enablePanelKeyboardShortcuts || keyboard == null)
            {
                return;
            }

            if (WasKeyPressed(keyboard, closePanelKey))
            {
                SetVisible(false);
                return;
            }

            if (keyboard.tabKey.wasPressedThisFrame)
            {
                CycleTab(IsShiftPressed(keyboard) ? -1 : 1);
                return;
            }

            if (keyboard.upArrowKey.wasPressedThisFrame)
            {
                NavigateVertical(-1);
                return;
            }

            if (keyboard.downArrowKey.wasPressedThisFrame)
            {
                NavigateVertical(1);
                return;
            }

            if (keyboard.leftArrowKey.wasPressedThisFrame)
            {
                CycleCurrentContextOption(-1);
                return;
            }

            if (keyboard.rightArrowKey.wasPressedThisFrame)
            {
                CycleCurrentContextOption(1);
                return;
            }

            if (WasConfirmPressed(keyboard))
            {
                ActivateCurrentPrimaryAction();
                return;
            }

            if (WasKeyPressed(keyboard, focusSelectionKey))
            {
                FocusCurrentContext();
            }
        }

        private void CycleTab(int delta)
        {
            SetTab((ManagementPanelTab)WrapIndex((int)currentTab + delta, ManagementPanelTabCount));
        }

        private void NavigateVertical(int delta)
        {
            if (currentTab == ManagementPanelTab.Properties)
            {
                SelectOwnedEntryDelta(delta);
                return;
            }

            if (currentTab == ManagementPanelTab.Acquisitions)
            {
                AcquisitionMarketSection target = delta < 0
                    ? AcquisitionMarketSection.Land
                    : AcquisitionMarketSection.Businesses;
                if (acquisitionSection != target)
                {
                    SelectAcquisitionSection(target);
                }
            }
        }

        private void SelectOwnedEntryDelta(int delta)
        {
            AutoWireSources();
            if (!ResolveView())
            {
                return;
            }

            RebuildOwnedRowsIfNeeded();
            if (ownedEntries.Count <= 0)
            {
                return;
            }

            selectedOwnedIndex = WrapIndex(selectedOwnedIndex + delta, ownedEntries.Count);
            selectedBusinessActivationBuildingId = -1;
            currentTab = ManagementPanelTab.Properties;
            Refresh();
        }

        private void CycleCurrentContextOption(int delta)
        {
            if (currentTab == ManagementPanelTab.Properties)
            {
                if (delta < 0)
                {
                    SelectPreviousWorkerCandidate();
                }
                else
                {
                    SelectNextWorkerCandidate();
                }

                return;
            }

            if (currentTab == ManagementPanelTab.Acquisitions)
            {
                if (delta < 0)
                {
                    PreviousListing();
                }
                else
                {
                    NextListing();
                }
            }
        }

        private void ActivateCurrentPrimaryAction()
        {
            if (!ResolveView())
            {
                return;
            }

            switch (currentTab)
            {
                case ManagementPanelTab.Properties:
                    if (TryGetSelectedOwnedEntry(out OwnedEntry activeSelection)
                        && activeSelection.Kind == OwnedKind.Business
                        && GetBusinessForBuilding(activeSelection.BuildingId) != null
                        && IsButtonUsable(view.AssignWorkerButton))
                    {
                        AssignSelectedWorkerCandidate();
                    }
                    else if (IsButtonUsable(view.BuildProjectButton))
                    {
                        ChooseBuildingForSelectedPlot();
                    }
                    else if (IsButtonUsable(view.AssignWorkerButton))
                    {
                        AssignSelectedWorkerCandidate();
                    }
                    break;
                case ManagementPanelTab.Finances:
                    if (IsButtonUsable(view.LoanSubmitButton))
                    {
                        SubmitLoanApplication();
                    }
                    break;
                case ManagementPanelTab.Acquisitions:
                    if (IsButtonUsable(view.BuyListingButton))
                    {
                        BuyListing();
                    }
                    break;
                case ManagementPanelTab.Government:
                    if (IsButtonUsable(view.GovernmentFocusButton))
                    {
                        FocusTownHall();
                    }
                    break;
                case ManagementPanelTab.Resources:
                    if (IsButtonUsable(view.ResourcesTabButton))
                    {
                        FocusStrongestResourceSite();
                    }
                    break;
            }
        }

        private void FocusCurrentContext()
        {
            if (!ResolveView())
            {
                return;
            }

            switch (currentTab)
            {
                case ManagementPanelTab.Properties:
                    if (IsButtonUsable(view.FocusPropertyButton))
                    {
                        FocusSelectedProperty();
                        Refresh();
                    }
                    break;
                case ManagementPanelTab.Acquisitions:
                    if (IsButtonUsable(view.AcquisitionFocusButton))
                    {
                        FocusSelectedAcquisitionListing();
                    }
                    break;
                case ManagementPanelTab.Government:
                    if (IsButtonUsable(view.GovernmentFocusButton))
                    {
                        FocusTownHall();
                    }
                    break;
                case ManagementPanelTab.Resources:
                    FocusStrongestResourceSite();
                    break;
            }
        }

        private void SetCameraArrowKeySuppression(bool suppress)
        {
            if (strategyCamera == null)
            {
                strategyCamera = FindAnyObjectByType<StrategyCameraController>();
            }

            if (strategyCamera != null)
            {
                strategyCamera.SuppressArrowKeyPan = suppress;
                strategyCamera.SuppressWheelZoomUnlessRotating = suppress;
            }
        }

        private static bool WasKeyPressed(Keyboard keyboard, Key key)
        {
            return keyboard != null && key != Key.None && keyboard[key].wasPressedThisFrame;
        }

        private static bool WasConfirmPressed(Keyboard keyboard)
        {
            return keyboard != null
                && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
        }

        private static bool IsShiftPressed(Keyboard keyboard)
        {
            return keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        }

        private static bool IsButtonUsable(Button button)
        {
            return button != null && button.gameObject.activeInHierarchy && button.interactable;
        }

        private void AutoWireSources()
        {
            storeRuntime ??= FindAnyObjectByType<GeneralStoreRuntimeManager>();
            acquisitionMarket ??= FindAnyObjectByType<AcquisitionMarketManager>();
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
            playerDebtManager ??= FindAnyObjectByType<PlayerDebtManager>();
            playerPortfolio ??= FindAnyObjectByType<PlayerPortfolioManager>();
            civicFoundation ??= FindAnyObjectByType<CivicFoundationManager>();
            populationManager ??= FindAnyObjectByType<PopulationManager>();
            townWorld ??= FindAnyObjectByType<TownWorldController>();
            opportunityPressureRuntime ??= FindAnyObjectByType<OpportunityPressureRuntimeManager>();
            strategyCamera ??= FindAnyObjectByType<StrategyCameraController>();
            workflowController ??= FindAnyObjectByType<OwnershipWorkflowController>();
            sellerMeetingController ??= FindAnyObjectByType<AcquisitionSellerMeetingController>();
            SetCameraArrowKeySuppression(visible);
            if (playerDebtManager == null && Application.isPlaying)
            {
                GameObject debtObject = new("Player Debt Manager");
                playerDebtManager = debtObject.AddComponent<PlayerDebtManager>();
            }

            // Keep the seller-meeting controller authoritative at the panel level before the
            // workflow wakes or reconfigures, otherwise the workflow can spawn/bind a second instance.
            if (sellerMeetingController == null && Application.isPlaying)
            {
                GameObject sellerMeetingObject = new("Acquisition Seller Meeting Controller");
                sellerMeetingController = sellerMeetingObject.AddComponent<AcquisitionSellerMeetingController>();
            }

            if (workflowController == null && Application.isPlaying)
            {
                GameObject workflowObject = new("Ownership Workflow Controller");
                workflowController = workflowObject.AddComponent<OwnershipWorkflowController>();
            }

            sellerMeetingController ??= FindAnyObjectByType<AcquisitionSellerMeetingController>();

            playerDebtManager?.Configure(
                TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>(),
                storeRuntime,
                acquisitionMarket,
                sharedBusinessRuntime,
                playerPortfolio);
            sellerMeetingController?.Configure(canvas, acquisitionMarket);
            workflowController?.Configure(canvas, townWorld, acquisitionMarket, playerDebtManager, sellerMeetingController);
        }

        private bool ResolveView()
        {
            if (view == null)
            {
                canvas = ResolveCanvas();
                view = canvas != null ? canvas.GetComponentInChildren<ManagementPanelView>(true) : null;
            }

            if (view == null)
            {
                Debug.LogError("ManagementPanelView is missing. Main Scene must contain one tagged UI Canvas scene instance with an authored Main Panel.", this);
                return false;
            }

            if (!view.BindFromChildren())
            {
                Debug.LogError("ManagementPanelView has missing required authored references. Repair UI Canvas > Main Panel instead of building a fallback UI.", view);
                return false;
            }

            view.EnsureBusinessCashTransferControls(Application.isPlaying);
            view.EnsureFinanceDashboardControls(Application.isPlaying);
            view.EnsureBankLoanControls(Application.isPlaying);
            view.EnsureResourcesControls(true);
            return true;
        }

        private Canvas ResolveCanvas()
        {
            if (canvas != null)
            {
                return canvas;
            }

            GameObject taggedCanvas = null;
            try
            {
                taggedCanvas = GameObject.FindWithTag(UiCanvasTag);
            }
            catch (UnityException)
            {
                return null;
            }

            return taggedCanvas != null ? taggedCanvas.GetComponent<Canvas>() : null;
        }

        private void AddButtonListeners()
        {
            if (listenersBound || !ResolveView())
            {
                return;
            }

            view.PropertiesTabButton?.onClick.AddListener(() => SetTab(ManagementPanelTab.Properties));
            view.FinancesTabButton?.onClick.AddListener(() => SetTab(ManagementPanelTab.Finances));
            view.AcquisitionsTabButton?.onClick.AddListener(() => SetTab(ManagementPanelTab.Acquisitions));
            view.GovernmentTabButton?.onClick.AddListener(() => SetTab(ManagementPanelTab.Government));
            view.ResourcesTabButton?.onClick.AddListener(() => SetTab(ManagementPanelTab.Resources));
            view.FocusPropertyButton?.onClick.AddListener(FocusSelectedProperty);
            view.DecreaseMarginButton?.onClick.AddListener(DecreaseStoreMargin);
            view.IncreaseMarginButton?.onClick.AddListener(IncreaseStoreMargin);
            view.PreviousWorkerButton?.onClick.AddListener(SelectPreviousWorkerCandidate);
            view.NextWorkerButton?.onClick.AddListener(SelectNextWorkerCandidate);
            view.AssignWorkerButton?.onClick.AddListener(AssignSelectedWorkerCandidate);
            view.BuildProjectButton?.onClick.AddListener(ChooseBuildingForSelectedPlot);
            view.AcquisitionLandButton?.onClick.AddListener(() => SelectAcquisitionSection(AcquisitionMarketSection.Land));
            view.AcquisitionBusinessesButton?.onClick.AddListener(() => SelectAcquisitionSection(AcquisitionMarketSection.Businesses));
            view.AcquisitionWorkflowButton?.onClick.AddListener(OpenSelectedAcquisitionWorkflow);
            view.AcquisitionFocusButton?.onClick.AddListener(FocusSelectedAcquisitionListing);
            view.PreviousListingButton?.onClick.AddListener(PreviousListing);
            view.NextListingButton?.onClick.AddListener(NextListing);
            view.BuyListingButton?.onClick.AddListener(BuyListing);
            view.GovernmentFocusButton?.onClick.AddListener(FocusTownHall);
            view.LoanIncreaseOneButton?.onClick.AddListener(() => AdjustLoanRequest(1));
            view.LoanIncreaseTenButton?.onClick.AddListener(() => AdjustLoanRequest(10));
            view.LoanIncreaseHundredButton?.onClick.AddListener(() => AdjustLoanRequest(100));
            view.LoanDecreaseOneButton?.onClick.AddListener(() => AdjustLoanRequest(-1));
            view.LoanDecreaseTenButton?.onClick.AddListener(() => AdjustLoanRequest(-10));
            view.LoanDecreaseHundredButton?.onClick.AddListener(() => AdjustLoanRequest(-100));
            view.LoanWorkflowButton?.onClick.AddListener(OpenLoanWorkflow);
            view.LoanSubmitButton?.onClick.AddListener(SubmitLoanApplication);
            view.CashTransferDepositOneButton?.onClick.AddListener(() => TransferSelectedBusinessCash(1));
            view.CashTransferDepositTenButton?.onClick.AddListener(() => TransferSelectedBusinessCash(10));
            view.CashTransferDepositHundredButton?.onClick.AddListener(() => TransferSelectedBusinessCash(100));
            view.CashTransferDepositExactButton?.onClick.AddListener(DepositExactBusinessCash);
            view.CashTransferWithdrawOneButton?.onClick.AddListener(() => TransferSelectedBusinessCash(-1));
            view.CashTransferWithdrawTenButton?.onClick.AddListener(() => TransferSelectedBusinessCash(-10));
            view.CashTransferWithdrawHundredButton?.onClick.AddListener(() => TransferSelectedBusinessCash(-100));
            view.CashTransferWithdrawExactButton?.onClick.AddListener(WithdrawExactBusinessCash);
            view.CashTransferAutoToggleButton?.onClick.AddListener(ToggleSelectedBusinessAutoTransfer);
            view.CashTransferLowerMinusTenButton?.onClick.AddListener(() => AdjustSelectedBusinessCashThreshold(true, -10));
            view.CashTransferLowerPlusTenButton?.onClick.AddListener(() => AdjustSelectedBusinessCashThreshold(true, 10));
            view.CashTransferUpperMinusTenButton?.onClick.AddListener(() => AdjustSelectedBusinessCashThreshold(false, -10));
            view.CashTransferUpperPlusTenButton?.onClick.AddListener(() => AdjustSelectedBusinessCashThreshold(false, 10));
            view.CashTransferApplyReserveButton?.onClick.AddListener(ApplyExactBusinessCashThresholds);
            view.CashTransferDepositInput?.onSubmit.AddListener(_ => DepositExactBusinessCash());
            view.CashTransferWithdrawInput?.onSubmit.AddListener(_ => WithdrawExactBusinessCash());
            view.CashTransferLowerThresholdInput?.onSubmit.AddListener(_ => ApplyExactBusinessCashThresholds());
            view.CashTransferUpperThresholdInput?.onSubmit.AddListener(_ => ApplyExactBusinessCashThresholds());
            listenersBound = true;
        }

        private void RemoveButtonListeners()
        {
            if (!listenersBound || view == null)
            {
                return;
            }

            view.PropertiesTabButton?.onClick.RemoveAllListeners();
            view.FinancesTabButton?.onClick.RemoveAllListeners();
            view.AcquisitionsTabButton?.onClick.RemoveAllListeners();
            view.GovernmentTabButton?.onClick.RemoveAllListeners();
            view.ResourcesTabButton?.onClick.RemoveAllListeners();
            view.FocusPropertyButton?.onClick.RemoveAllListeners();
            view.DecreaseMarginButton?.onClick.RemoveAllListeners();
            view.IncreaseMarginButton?.onClick.RemoveAllListeners();
            view.PreviousWorkerButton?.onClick.RemoveAllListeners();
            view.NextWorkerButton?.onClick.RemoveAllListeners();
            view.AssignWorkerButton?.onClick.RemoveAllListeners();
            view.BuildProjectButton?.onClick.RemoveAllListeners();
            view.AcquisitionLandButton?.onClick.RemoveAllListeners();
            view.AcquisitionBusinessesButton?.onClick.RemoveAllListeners();
            view.AcquisitionWorkflowButton?.onClick.RemoveAllListeners();
            view.AcquisitionFocusButton?.onClick.RemoveAllListeners();
            view.PreviousListingButton?.onClick.RemoveAllListeners();
            view.NextListingButton?.onClick.RemoveAllListeners();
            view.BuyListingButton?.onClick.RemoveAllListeners();
            view.GovernmentFocusButton?.onClick.RemoveAllListeners();
            view.LoanIncreaseOneButton?.onClick.RemoveAllListeners();
            view.LoanIncreaseTenButton?.onClick.RemoveAllListeners();
            view.LoanIncreaseHundredButton?.onClick.RemoveAllListeners();
            view.LoanDecreaseOneButton?.onClick.RemoveAllListeners();
            view.LoanDecreaseTenButton?.onClick.RemoveAllListeners();
            view.LoanDecreaseHundredButton?.onClick.RemoveAllListeners();
            view.LoanWorkflowButton?.onClick.RemoveAllListeners();
            view.LoanSubmitButton?.onClick.RemoveAllListeners();
            view.CashTransferDepositOneButton?.onClick.RemoveAllListeners();
            view.CashTransferDepositTenButton?.onClick.RemoveAllListeners();
            view.CashTransferDepositHundredButton?.onClick.RemoveAllListeners();
            view.CashTransferDepositExactButton?.onClick.RemoveAllListeners();
            view.CashTransferWithdrawOneButton?.onClick.RemoveAllListeners();
            view.CashTransferWithdrawTenButton?.onClick.RemoveAllListeners();
            view.CashTransferWithdrawHundredButton?.onClick.RemoveAllListeners();
            view.CashTransferWithdrawExactButton?.onClick.RemoveAllListeners();
            view.CashTransferAutoToggleButton?.onClick.RemoveAllListeners();
            view.CashTransferLowerMinusTenButton?.onClick.RemoveAllListeners();
            view.CashTransferLowerPlusTenButton?.onClick.RemoveAllListeners();
            view.CashTransferUpperMinusTenButton?.onClick.RemoveAllListeners();
            view.CashTransferUpperPlusTenButton?.onClick.RemoveAllListeners();
            view.CashTransferApplyReserveButton?.onClick.RemoveAllListeners();
            view.CashTransferDepositInput?.onSubmit.RemoveAllListeners();
            view.CashTransferWithdrawInput?.onSubmit.RemoveAllListeners();
            view.CashTransferLowerThresholdInput?.onSubmit.RemoveAllListeners();
            view.CashTransferUpperThresholdInput?.onSubmit.RemoveAllListeners();
            for (int i = 0; i < ownedEntryButtons.Count; i++)
            {
                ownedEntryButtons[i]?.onClick.RemoveAllListeners();
            }

            for (int i = 0; i < financeBusinessButtons.Count; i++)
            {
                financeBusinessButtons[i]?.onClick.RemoveAllListeners();
            }

            listenersBound = false;
        }

        private void UpdateShellText()
        {
            if (view.TitleText != null)
            {
                view.TitleText.text = "Management";
            }

            if (view.HintText != null)
            {
                view.HintText.text = $"{managementToggleKey} or Esc close | Tab switch tabs | Arrows select rows/listings | Enter action | {focusSelectionKey} view site";
            }

            view.SetStatusText(panelStatus);
            view.SetActiveTab(currentTab);

            if (view.RuntimeFallbackText != null)
            {
                view.RuntimeFallbackText.gameObject.SetActive(false);
            }
        }

        private void UpdatePanelStatusLine()
        {
            if (view != null)
            {
                view.SetStatusText(panelStatus);
            }
        }

        private void UpdateProperties()
        {
            if (view.PropertyListTitleText != null)
            {
                view.PropertyListTitleText.text = $"Properties ({ownedEntries.Count})";
            }

            ConsumePendingOwnedContinuityStatus();

            if (ownedEntries.Count == 0)
            {
                SetText(view.PropertyDetailTitleText, "No Properties");
                SetText(view.PropertyDetailSummaryText, BuildPropertiesEmptyStateSummary());
                SetText(view.RentingOverviewText, BuildRentingAndLodgingSummary());
                SetText(view.StoreOverviewText, BuildPropertiesEmptyStateDetail());
                SetText(view.StoreFinanceText, string.Empty);
                SetText(view.StoreStaffingText, BuildPropertiesEmptyStateAction());
                SetText(view.StoreStockText, string.Empty);
                view.RefreshPropertyDetailLayout();
                RefreshOwnedRowSelectionStyles();
                UpdatePropertyActionBar(default, false);
                UpdateBusinessCashTransferPanel();
                return;
            }

            selectedOwnedIndex = Mathf.Clamp(selectedOwnedIndex, 0, ownedEntries.Count - 1);
            RefreshOwnedRowSelectionStyles();
            OwnedEntry selected = ownedEntries[selectedOwnedIndex];
            SetText(view.PropertyDetailTitleText, selected.Label);
            SetText(view.PropertyDetailSummaryText, BuildPropertySummary(selected));
            SetText(view.RentingOverviewText, selected.Kind == OwnedKind.Renting ? BuildRentingAndLodgingSummary() : string.Empty);

            if (selected.Kind == OwnedKind.Store && storeRuntime != null)
            {
                SetText(view.StoreOverviewText, BuildStoreOverviewWithPriority());
                SetText(view.StoreFinanceText, BuildStoreFinanceBlock());
                SetText(view.StoreStaffingText, storeRuntime.BuildStoreWorkersText(selectedWorkerCandidateIndex));
                SetText(view.StoreStockText, storeRuntime.BuildStoreStockText());
                view.RefreshPropertyDetailLayout();
                UpdatePropertyActionBar(selected, true);
                UpdateBusinessCashTransferPanel();
                return;
            }

            SetText(view.StoreOverviewText, BuildNonStoreDetail(selected));
            if (selected.Kind == OwnedKind.Renting)
            {
                SetText(view.StoreFinanceText, string.Empty);
                SetText(view.StoreStaffingText, BuildRentingAndBoardingOperationsText());
                SetText(view.StoreStockText, BuildRentingAndBoardingListingsText());
                view.RefreshPropertyDetailLayout();
                UpdatePropertyActionBar(selected, true);
                UpdateBusinessCashTransferPanel();
                return;
            }

            if (selected.Kind == OwnedKind.Land)
            {
                SetText(view.StoreStaffingText, BuildOwnedLandBuildabilityText(selected.PlotId));
                SetText(view.StoreStockText, BuildConstructionInputAvailabilityText());
            }
            else
            {
                BusinessInstanceState business = selected.Kind == OwnedKind.Business ? GetBusinessForBuilding(selected.BuildingId) : null;
                if (business == null && IsStartableOwnedShell(selected))
                {
                    EnsureBusinessActivationSelection(selected.BuildingId);
                }

                SetText(view.StoreFinanceText, business != null ? BuildManagedBusinessLedgerBlock(business) : string.Empty);
                SetText(view.StoreStaffingText, BuildNonStoreOperationsText(selected));
                SetText(view.StoreStockText, selected.Kind == OwnedKind.Holding
                    ? BuildConstructionInputAvailabilityText()
                    : business == null
                    ? BuildConstructionInputAvailabilityText()
                    : sharedBusinessRuntime != null ? sharedBusinessRuntime.BuildBusinessInventoryText(business) : string.Empty);
            }

            view.RefreshPropertyDetailLayout();
            UpdatePropertyActionBar(selected, true);
            UpdateBusinessCashTransferPanel();
        }

        private void UpdatePropertyActionBar(OwnedEntry selected, bool hasSelection)
        {
            if (view == null)
            {
                return;
            }

            if (!hasSelection)
            {
                SetButtonVisible(view.FocusPropertyButton, false);
                SetButtonVisible(view.DecreaseMarginButton, false);
                SetButtonVisible(view.IncreaseMarginButton, false);
                SetButtonVisible(view.NextWorkerButton, false);
                SetButtonVisible(view.PreviousWorkerButton, false);
                SetButtonVisible(view.AssignWorkerButton, false);
                SetButtonVisible(view.BuildProjectButton, false);
                return;
            }

            SetButtonVisible(view.FocusPropertyButton, true);
            SetButtonLabel(view.FocusPropertyButton, "Inspect Site");
            SetButtonInteractable(view.FocusPropertyButton, true);

            bool isStore = selected.Kind == OwnedKind.Store;
            bool isRenting = selected.Kind == OwnedKind.Renting;
            bool isLand = selected.Kind == OwnedKind.Land;
            BusinessInstanceState sharedBusiness = selected.Kind == OwnedKind.Business ? GetBusinessForBuilding(selected.BuildingId) : null;
            bool isManagedBusiness = sharedBusiness != null && sharedBusiness.Owner != null && sharedBusiness.Owner.OwnerKind == BusinessOwnerKind.Player;
            bool isVacantShell = IsStartableOwnedShell(selected);
            bool isHolding = selected.Kind == OwnedKind.Holding;
            if (isRenting)
            {
                SetButtonVisible(view.FocusPropertyButton, false);
                SetButtonVisible(view.DecreaseMarginButton, false);
                SetButtonVisible(view.IncreaseMarginButton, false);
                SetButtonVisible(view.NextWorkerButton, false);
                SetButtonVisible(view.PreviousWorkerButton, false);
                SetButtonVisible(view.AssignWorkerButton, false);
                SetButtonVisible(view.BuildProjectButton, false);
                return;
            }

            SetButtonVisible(view.DecreaseMarginButton, isStore || isLand || isManagedBusiness);
            SetButtonVisible(view.IncreaseMarginButton, isStore || isLand || isManagedBusiness);
            SetButtonVisible(view.NextWorkerButton, isStore || isLand || isManagedBusiness || isVacantShell || isHolding);
            SetButtonVisible(view.PreviousWorkerButton, isStore || isLand || isManagedBusiness || isVacantShell || isHolding);
            SetButtonVisible(view.AssignWorkerButton, isStore || isManagedBusiness || (isLand && selectedOwnedLandDevelopmentIntent == OwnedLandDevelopmentIntent.Business));
            SetButtonVisible(view.BuildProjectButton, isLand || isVacantShell || isHolding);

            if (isStore)
            {
                bool fireMode = IsWorkerFireMode();
                int selectionCount = GetWorkerSelectionCount();
                SetButtonLabel(view.DecreaseMarginButton, "Price -");
                SetButtonLabel(view.IncreaseMarginButton, "Price +");
                SetButtonInteractable(view.DecreaseMarginButton, storeRuntime != null && storeRuntime.CanDecreaseStoreMargin);
                SetButtonInteractable(view.IncreaseMarginButton, storeRuntime != null && storeRuntime.CanIncreaseStoreMargin);
                SetButtonLabel(view.PreviousWorkerButton, "Prev Worker");
                SetButtonLabel(view.NextWorkerButton, "Next Worker");
                SetButtonLabel(view.AssignWorkerButton, fireMode ? "Dismiss" : "Hire");
                SetButtonInteractable(view.NextWorkerButton, selectionCount > 1);
                SetButtonInteractable(view.PreviousWorkerButton, selectionCount > 1);
                SetButtonInteractable(view.AssignWorkerButton, selectionCount > 0);
                SetButtonInteractable(view.BuildProjectButton, false);
                return;
            }

            if (isLand)
            {
                SetButtonLabel(view.DecreaseMarginButton, selectedOwnedLandDevelopmentIntent == OwnedLandDevelopmentIntent.House ? "House selected" : "House");
                SetButtonLabel(view.IncreaseMarginButton, selectedOwnedLandDevelopmentIntent == OwnedLandDevelopmentIntent.Business ? "Business selected" : "Business");
                SetButtonInteractable(view.DecreaseMarginButton, true);
                SetButtonInteractable(view.IncreaseMarginButton, true);

                if (selectedOwnedLandDevelopmentIntent == OwnedLandDevelopmentIntent.House)
                {
                    int optionCount = GetHouseBuildOptionCount(selected.PlotId);
                    selectedBuildOptionIndex = WrapIndex(selectedBuildOptionIndex, optionCount);
                    PropertyProjectActionState projectAction = TryGetSelectedPropertyProjectActionState(out PropertyProjectActionState houseActionState)
                        ? houseActionState
                        : new PropertyProjectActionState("Build House", string.Empty, "ready to build.", string.Empty, string.Empty, false, false);
                    SetButtonVisible(view.AssignWorkerButton, false);
                    SetButtonLabel(view.PreviousWorkerButton, "Prev House");
                    SetButtonLabel(view.NextWorkerButton, "Next House");
                    SetButtonLabel(view.BuildProjectButton, BuildPropertyActionButtonLabel(projectAction));
                    SetButtonInteractable(view.PreviousWorkerButton, optionCount > 1);
                    SetButtonInteractable(view.NextWorkerButton, optionCount > 1);
                    SetButtonInteractable(view.AssignWorkerButton, false);
                    SetButtonInteractable(view.BuildProjectButton, !projectAction.IsBlocked && !projectAction.IsComplete);
                }
                else
                {
                    EnsureOwnedLandBusinessSelection(selected.PlotId);
                    int candidateCount = GetBusinessDevelopmentCandidateCount();
                    int shellCount = GetBusinessBuildOptionCount(selected.PlotId);
                    selectedBusinessActivationIndex = WrapIndex(selectedBusinessActivationIndex, candidateCount);
                    selectedBusinessBuildOptionIndex = WrapIndex(selectedBusinessBuildOptionIndex, shellCount);
                    PropertyProjectActionState projectAction = TryGetSelectedPropertyProjectActionState(out PropertyProjectActionState landBusinessActionState)
                        ? landBusinessActionState
                        : new PropertyProjectActionState("Build Shell", string.Empty, "ready to build.", string.Empty, string.Empty, false, false);
                    SetButtonVisible(view.AssignWorkerButton, true);
                    SetButtonLabel(view.PreviousWorkerButton, "Prev Business");
                    SetButtonLabel(view.NextWorkerButton, "Next Business");
                    SetButtonLabel(view.AssignWorkerButton, "Next Shell");
                    SetButtonLabel(view.BuildProjectButton, BuildPropertyActionButtonLabel(projectAction));
                    SetButtonInteractable(view.PreviousWorkerButton, candidateCount > 1);
                    SetButtonInteractable(view.NextWorkerButton, candidateCount > 1);
                    SetButtonInteractable(view.AssignWorkerButton, shellCount > 1);
                    SetButtonInteractable(view.BuildProjectButton, !projectAction.IsBlocked && !projectAction.IsComplete);
                }
            }
            else if (isManagedBusiness)
            {
                bool fireMode = sharedBusinessRuntime != null && sharedBusinessRuntime.IsWorkerFireMode(sharedBusiness);
                int selectionCount = sharedBusinessRuntime != null ? sharedBusinessRuntime.GetWorkerSelectionCount(sharedBusiness) : 0;
                SetButtonLabel(view.DecreaseMarginButton, "Price -");
                SetButtonLabel(view.IncreaseMarginButton, "Price +");
                SetButtonInteractable(view.DecreaseMarginButton, sharedBusinessRuntime != null);
                SetButtonInteractable(view.IncreaseMarginButton, sharedBusinessRuntime != null);
                SetButtonLabel(view.PreviousWorkerButton, "Prev Worker");
                SetButtonLabel(view.NextWorkerButton, "Next Worker");
                SetButtonLabel(view.AssignWorkerButton, fireMode ? "Dismiss" : "Hire");
                SetButtonInteractable(view.NextWorkerButton, selectionCount > 1);
                SetButtonInteractable(view.PreviousWorkerButton, selectionCount > 1);
                SetButtonInteractable(view.AssignWorkerButton, selectionCount > 0);
                SetButtonVisible(view.BuildProjectButton, true);
                SetButtonLabel(view.BuildProjectButton, "Sell Business");
                SetButtonInteractable(view.BuildProjectButton, acquisitionMarket != null);
            }
            else if (isVacantShell)
            {
                int candidateCount = GetBusinessActivationCandidateCount(selected.BuildingId);
                EnsureBusinessActivationSelection(selected.BuildingId);
                selectedBusinessActivationIndex = WrapIndex(selectedBusinessActivationIndex, candidateCount);
                SetButtonLabel(view.PreviousWorkerButton, "Prev Business");
                SetButtonLabel(view.NextWorkerButton, "Next Business");
                PropertyProjectActionState projectAction = TryGetSelectedPropertyProjectActionState(out PropertyProjectActionState shellActionState)
                    ? shellActionState
                    : new PropertyProjectActionState("Start Business", string.Empty, "ready to open.", string.Empty, string.Empty, false, false);
                SetButtonLabel(view.BuildProjectButton, BuildPropertyActionButtonLabel(projectAction));
                SetButtonInteractable(view.NextWorkerButton, candidateCount > 1);
                SetButtonInteractable(view.PreviousWorkerButton, candidateCount > 1);
                SetButtonInteractable(view.AssignWorkerButton, false);
                SetButtonInteractable(view.BuildProjectButton, !projectAction.IsBlocked && !projectAction.IsComplete);
            }
            else if (isHolding)
            {
                int upgradeCount = GetHouseholdUpgradeOptionCount();
                selectedHouseholdUpgradeIndex = WrapIndex(selectedHouseholdUpgradeIndex, upgradeCount);
                PropertyProjectActionState projectAction = TryGetSelectedPropertyProjectActionState(out PropertyProjectActionState holdingActionState)
                    ? holdingActionState
                    : new PropertyProjectActionState("Build Upgrade", string.Empty, "ready to build.", string.Empty, string.Empty, false, false);
                SetButtonLabel(view.PreviousWorkerButton, "Prev Upgrade");
                SetButtonLabel(view.NextWorkerButton, "Next Upgrade");
                SetButtonLabel(view.BuildProjectButton, BuildPropertyActionButtonLabel(projectAction));
                SetButtonInteractable(view.NextWorkerButton, upgradeCount > 1);
                SetButtonInteractable(view.PreviousWorkerButton, upgradeCount > 1);
                SetButtonInteractable(view.AssignWorkerButton, false);
                SetButtonInteractable(view.BuildProjectButton, upgradeCount > 0 && !projectAction.IsComplete && !projectAction.IsBlocked);
            }
            else
            {
                SetButtonInteractable(view.NextWorkerButton, false);
                SetButtonInteractable(view.PreviousWorkerButton, false);
                SetButtonInteractable(view.AssignWorkerButton, false);
                SetButtonInteractable(view.BuildProjectButton, false);
            }
        }

        private void RefreshDynamicTooltips()
        {
            if (view == null || !Application.isPlaying)
            {
                return;
            }

            AttachContextTooltip(view.FocusPropertyButton, "View Site", "Move the camera to the selected holding.");
            AttachContextTooltip(view.DecreaseMarginButton, BuildButtonTitle(view.DecreaseMarginButton, "Previous Option"), () => BuildPropertyActionTooltip(view.DecreaseMarginButton));
            AttachContextTooltip(view.IncreaseMarginButton, BuildButtonTitle(view.IncreaseMarginButton, "Next Option"), () => BuildPropertyActionTooltip(view.IncreaseMarginButton));
            AttachContextTooltip(view.PreviousWorkerButton, BuildButtonTitle(view.PreviousWorkerButton, "Previous"), () => BuildPropertyActionTooltip(view.PreviousWorkerButton));
            AttachContextTooltip(view.NextWorkerButton, BuildButtonTitle(view.NextWorkerButton, "Next"), () => BuildPropertyActionTooltip(view.NextWorkerButton));
            AttachContextTooltip(view.AssignWorkerButton, BuildButtonTitle(view.AssignWorkerButton, "Staff Action"), () => BuildPropertyActionTooltip(view.AssignWorkerButton));
            AttachContextTooltip(view.BuildProjectButton, BuildButtonTitle(view.BuildProjectButton, "Property Action"), () => BuildPropertyActionTooltip(view.BuildProjectButton));

            AttachContextTooltip(view.AcquisitionFocusButton, "View Listing", "Move the camera to the selected listing when it has a town location.");
            AttachContextTooltip(view.PreviousListingButton, "Previous Listing", "Select the previous listing in the active acquisition section.");
            AttachContextTooltip(view.NextListingButton, "Next Listing", "Select the next listing in the active acquisition section.");
            AttachContextTooltip(view.BuyListingButton, BuildButtonTitle(view.BuyListingButton, "Acquisition Action"), () =>
            {
                string label = GetButtonLabel(view.BuyListingButton);
                return BuildButtonTooltip(view.BuyListingButton, $"Advance the selected listing with {label}.");
            });

            AttachContextTooltip(view.LoanSubmitButton, "Submit Loan Application", "Send the current bank loan request for review.");
            AttachContextTooltip(view.GovernmentFocusButton, BuildButtonTitle(view.GovernmentFocusButton, "View Civic Site"), "Move the camera to the civic site.");
        }

        private static void AttachContextTooltip(Button button, string title, string body)
        {
            UITooltipRegistry.Attach(button, title, () => BuildButtonTooltip(button, body));
        }

        private static void AttachContextTooltip(Button button, string title, Func<string> bodyProvider)
        {
            UITooltipRegistry.Attach(button, title, bodyProvider);
        }

        private string BuildPropertyActionTooltip(Button button)
        {
            if (button == view?.BuildProjectButton && TryGetSelectedPropertyProjectActionState(out PropertyProjectActionState projectAction))
            {
                return BuildButtonTooltip(button, BuildPropertyProjectActionTooltip(projectAction));
            }

            string label = GetButtonLabel(button);
            if (!string.IsNullOrWhiteSpace(label) && label.StartsWith("Blocked:", StringComparison.OrdinalIgnoreCase))
            {
                return BuildButtonTooltip(button, BuildSelectedPropertyBlockedTooltip());
            }

            string body = label switch
            {
                "Price -" => "Lower the price adjustment to favor sales volume.",
                "Price +" => "Raise the price adjustment to favor profit per sale.",
                "House" or "House selected" => "Set this plot to residential construction.",
                "Business" or "Business selected" => "Set this plot to business construction.",
                "Prev Worker" => "Move to the previous worker candidate or assigned staff member.",
                "Next Worker" => "Move to the next worker candidate or assigned staff member.",
                "Hire" => "Hire the selected worker into the first open role.",
                "Dismiss" => "Remove the selected worker from this business.",
                "Prev House" => "Choose the previous housing plan for this plot.",
                "Next House" => "Choose the next housing plan for this plot.",
                "Prev Business" => "Choose the previous business option for this site.",
                "Next Business" => "Choose the next business option for this site.",
                "Next Shell" => "Choose the next building plan for this business site.",
                "Build Shell" => "Build the selected business-ready building on this plot.",
                "Start Business" => "Open the selected business in this vacant building.",
                "Control" => "Cycle how directly you control this business.",
                "Policy" => "Cycle the manager policy for this business.",
                "Prev Upgrade" => "Choose the previous upgrade for this holding.",
                "Next Upgrade" => "Choose the next upgrade for this holding.",
                "Build Upgrade" => "Build the selected upgrade for this holding.",
                "Built" => "This selected upgrade is already built.",
                _ => BuildFallbackActionTooltip(label)
            };

            return BuildButtonTooltip(button, body);
        }

        private string BuildSelectedPropertyBlockedTooltip()
        {
            if (TryGetSelectedPropertyProjectActionState(out PropertyProjectActionState state) && state.IsBlocked)
            {
                return BuildPropertyProjectActionTooltip(state);
            }

            return "This action is blocked until the listed requirement is resolved.";
        }

        private static string BuildFallbackActionTooltip(string label)
        {
            return "Use this action for the selected property.";
        }

        private bool TryGetSelectedPropertyProjectActionState(out PropertyProjectActionState state)
        {
            if (!TryGetSelectedOwnedEntry(out OwnedEntry selected))
            {
                state = default;
                return false;
            }

            return TryGetPropertyProjectActionState(selected, out state);
        }

        private bool TryGetPropertyProjectActionState(OwnedEntry selected, out PropertyProjectActionState state)
        {
            state = default;

            if (selected.Kind == OwnedKind.Land)
            {
                if (selectedOwnedLandDevelopmentIntent == OwnedLandDevelopmentIntent.House)
                {
                    IReadOnlyList<BuildingDefinition> houseOptions = acquisitionMarket != null ? acquisitionMarket.GetHouseBuildOptionsForOwnedPlot(selected.PlotId) : null;
                    int optionCount = houseOptions != null ? houseOptions.Count : 0;
                    selectedBuildOptionIndex = WrapIndex(selectedBuildOptionIndex, optionCount);
                    string selectedPlan = optionCount > 0 && houseOptions[selectedBuildOptionIndex] != null
                        ? houseOptions[selectedBuildOptionIndex].DisplayName
                        : "No house plan";
                    if (optionCount <= 0)
                    {
                        state = new PropertyProjectActionState(
                            "Build House",
                            selectedPlan,
                            string.Empty,
                            "No house plan is available for this plot.",
                            "Cycle to another parcel or reopen the acquisition view.",
                            true,
                            false);
                        return true;
                    }

                    if (TryGetSelectedHouseConstructionBlockedReason(selected.PlotId, out string blockedReason))
                    {
                        state = new PropertyProjectActionState(
                            "Build House",
                            selectedPlan,
                            string.Empty,
                            blockedReason,
                            FirstSessionGuidanceText.BuildConstructionBlockerNextStep(blockedReason),
                            true,
                            false);
                        return true;
                    }

                    state = new PropertyProjectActionState(
                        "Build House",
                        selectedPlan,
                        "ready to build.",
                        string.Empty,
                        "Press Build House, or cycle the house plan.",
                        false,
                        false);
                    return true;
                }

                EnsureOwnedLandBusinessSelection(selected.PlotId);
                int candidateCount = GetBusinessDevelopmentCandidateCount();
                selectedBusinessActivationIndex = WrapIndex(selectedBusinessActivationIndex, candidateCount);
                if (candidateCount <= 0
                    || acquisitionMarket == null
                    || !acquisitionMarket.TryGetBusinessDevelopmentCandidate(selectedBusinessActivationIndex, out BusinessActivationCandidateState candidate)
                    || candidate == null)
                {
                    state = new PropertyProjectActionState(
                        "Build Shell",
                        "No business start selected",
                        string.Empty,
                        "No supported business start is selected for this plot.",
                        "Cycle to another business type or inspect another parcel.",
                        true,
                        false);
                    return true;
                }

                IReadOnlyList<BuildingDefinition> shellOptions = acquisitionMarket.GetBusinessBuildOptionsForOwnedPlot(selected.PlotId, selectedBusinessActivationIndex);
                int shellCount = shellOptions != null ? shellOptions.Count : 0;
                selectedBusinessBuildOptionIndex = WrapIndex(selectedBusinessBuildOptionIndex, shellCount);
                string selectedShell = shellCount > 0 && shellOptions[selectedBusinessBuildOptionIndex] != null
                    ? shellOptions[selectedBusinessBuildOptionIndex].DisplayName
                    : "No shell plan";
                string selectedLabel = $"{candidate.displayName} — {selectedShell}";
                if (shellCount <= 0)
                {
                    state = new PropertyProjectActionState(
                        "Build Shell",
                        selectedLabel,
                        string.Empty,
                        "No building shell is available for the selected business start.",
                        "Cycle the business type or inspect another parcel.",
                        true,
                        false);
                    return true;
                }

                if (TryGetSelectedBusinessConstructionBlockedReason(selected.PlotId, out string businessBlockedReason))
                {
                    state = new PropertyProjectActionState(
                        "Build Shell",
                        selectedLabel,
                        string.Empty,
                        businessBlockedReason,
                        FirstSessionGuidanceText.BuildConstructionBlockerNextStep(businessBlockedReason),
                        true,
                        false);
                    return true;
                }

                state = new PropertyProjectActionState(
                    "Build Shell",
                    selectedLabel,
                    "ready to build.",
                    string.Empty,
                    "Press Build Shell, or cycle the business and shell.",
                    false,
                    false);
                return true;
            }

            if (selected.Kind == OwnedKind.Business && IsStartableOwnedShell(selected))
            {
                if (acquisitionMarket == null)
                {
                    state = new PropertyProjectActionState(
                        "Start Business",
                        "Startup unavailable",
                        string.Empty,
                        "Startup planning is unavailable for this shell right now.",
                        "Reopen the management panel once acquisition systems are ready.",
                        true,
                        false);
                    return true;
                }

                EnsureBusinessActivationSelection(selected.BuildingId);
                int candidateCount = GetBusinessActivationCandidateCount(selected.BuildingId);
                selectedBusinessActivationIndex = WrapIndex(selectedBusinessActivationIndex, candidateCount);
                if (candidateCount <= 0
                    || !acquisitionMarket.TryGetBusinessActivationCandidate(selected.BuildingId, selectedBusinessActivationIndex, out BusinessActivationCandidateState candidate)
                    || candidate == null)
                {
                    state = new PropertyProjectActionState(
                        "Start Business",
                        "No business start selected",
                        string.Empty,
                        "No supported business start is selected for this shell.",
                        "Cycle the business type or inspect another shell.",
                        true,
                        false);
                    return true;
                }

                if (TryGetSelectedStartupBlockedReason(selected.BuildingId, out string blockedReason))
                {
                    state = new PropertyProjectActionState(
                        "Start Business",
                        candidate.displayName,
                        string.Empty,
                        blockedReason,
                        FirstSessionGuidanceText.BuildConstructionBlockerNextStep(blockedReason),
                        true,
                        false);
                    return true;
                }

                state = new PropertyProjectActionState(
                    "Start Business",
                    candidate.displayName,
                    "ready to open.",
                    string.Empty,
                    "Press Start Business, or cycle the business type.",
                    false,
                    false);
                return true;
            }

            if (selected.Kind == OwnedKind.Holding)
            {
                int optionCount = GetHouseholdUpgradeOptionCount();
                selectedHouseholdUpgradeIndex = WrapIndex(selectedHouseholdUpgradeIndex, optionCount);
                if (optionCount <= 0 || !TryGetSelectedHouseholdUpgradeDefinition(out HouseholdUpgradeDefinition definition) || definition == null)
                {
                    state = new PropertyProjectActionState(
                        "Build Upgrade",
                        "No upgrade selected",
                        string.Empty,
                        "No upgrade is available for this holding.",
                        "Select another holding or leave the house as-is.",
                        true,
                        false);
                    return true;
                }

                if (IsSelectedHouseholdUpgradeBuilt(selected.BuildingId))
                {
                    state = new PropertyProjectActionState(
                        "Build Upgrade",
                        definition.DisplayName,
                        "already built.",
                        string.Empty,
                        "Cycle to another upgrade, or leave the house as-is.",
                        false,
                        true);
                    return true;
                }

                if (TryGetSelectedHouseholdUpgradeBlockedReason(selected.BuildingId, out string blockedReason))
                {
                    state = new PropertyProjectActionState(
                        "Build Upgrade",
                        definition.DisplayName,
                        string.Empty,
                        blockedReason,
                        FirstSessionGuidanceText.BuildConstructionBlockerNextStep(blockedReason),
                        true,
                        false);
                    return true;
                }

                state = new PropertyProjectActionState(
                    "Build Upgrade",
                    definition.DisplayName,
                    "ready to build.",
                    string.Empty,
                    "Press Build Upgrade, or cycle to another improvement.",
                    false,
                    false);
                return true;
            }

            return false;
        }

        private static string BuildPropertyActionButtonLabel(PropertyProjectActionState state)
        {
            if (state.IsComplete)
            {
                return "Built";
            }

            if (state.IsBlocked)
            {
                return $"Blocked: {CompactActionReason(state.BlockedReason)}";
            }

            return state.ActionLabel;
        }

        private static string BuildPropertyActionPanelStatus(PropertyProjectActionState state)
        {
            string selected = CompactSelectionLabel(state.SelectedLabel);
            if (state.IsComplete)
            {
                return string.IsNullOrWhiteSpace(selected)
                    ? $"{state.ActionLabel} complete."
                    : $"{state.ActionLabel} complete — {selected}.";
            }

            if (state.IsBlocked)
            {
                return $"{state.ActionLabel} blocked — {CompactReason(state.BlockedReason)}";
            }

            return string.IsNullOrWhiteSpace(selected)
                ? $"{state.ActionLabel} ready."
                : $"{state.ActionLabel} ready — {selected}.";
        }

        private static string BuildPropertyActionDetailStatusLine(PropertyProjectActionState state)
        {
            if (state.IsComplete)
            {
                return "Status: already built.";
            }

            if (state.IsBlocked)
            {
                return $"Status: blocked — {CompactReason(state.BlockedReason)}";
            }

            return $"Status: {state.ReadyText}";
        }

        private static string BuildPropertyActionDetailNextMoveLine(PropertyProjectActionState state)
        {
            return string.IsNullOrWhiteSpace(state.NextMove)
                ? string.Empty
                : $"Next move: {state.NextMove}";
        }

        private bool TryGetSelectedPropertyProjectActionState(string expectedActionLabel, out PropertyProjectActionState state)
        {
            if (TryGetSelectedPropertyProjectActionState(out state)
                && string.Equals(state.ActionLabel, expectedActionLabel, StringComparison.Ordinal))
            {
                return true;
            }

            state = default;
            return false;
        }

        private void AppendSelectedPropertyActionRead(StringBuilder builder, string expectedActionLabel, string fallbackStatus, string fallbackNextMove)
        {
            if (builder == null)
            {
                return;
            }

            if (TryGetSelectedPropertyProjectActionState(expectedActionLabel, out PropertyProjectActionState state))
            {
                builder.AppendLine(BuildPropertyActionDetailStatusLine(state));
                string nextMove = BuildPropertyActionDetailNextMoveLine(state);
                if (!string.IsNullOrWhiteSpace(nextMove))
                {
                    builder.AppendLine(nextMove);
                }

                return;
            }

            builder.AppendLine($"Status: {fallbackStatus}");
            if (!string.IsNullOrWhiteSpace(fallbackNextMove))
            {
                builder.AppendLine($"Next move: {fallbackNextMove}");
            }
        }

        private static string CompactSelectionLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            return trimmed.Length <= 64 ? trimmed : trimmed.Substring(0, 61) + "...";
        }

        private string BuildPropertyProjectActionTooltip(PropertyProjectActionState state)
        {
            string body = state.ActionLabel switch
            {
                "Build House" => "Build the selected residential plan on this plot.",
                "Build Shell" => "Build the selected business-ready building on this plot.",
                "Start Business" => "Open the selected business in this vacant building.",
                "Build Upgrade" => "Build the selected upgrade for this holding.",
                _ => "Use this action for the selected property."
            };

            if (!string.IsNullOrWhiteSpace(state.SelectedLabel))
            {
                body += $" Selected: {CompactSelectionLabel(state.SelectedLabel)}.";
            }

            if (state.IsComplete)
            {
                body += $" Status: {state.ReadyText} {state.NextMove}";
                return body.Trim();
            }

            if (state.IsBlocked)
            {
                body += $" Blocked: {CompactReason(state.BlockedReason)}";
                if (!string.IsNullOrWhiteSpace(state.NextMove))
                {
                    body += $" Next: {state.NextMove}";
                }

                return body.Trim();
            }

            if (!string.IsNullOrWhiteSpace(state.ReadyText))
            {
                body += $" Status: {state.ReadyText}";
            }

            if (!string.IsNullOrWhiteSpace(state.NextMove))
            {
                body += $" Next: {state.NextMove}";
            }

            return body.Trim();
        }

        private void RefreshSelectedPropertyActionPanelStatus()
        {
            if (TryGetSelectedPropertyProjectActionState(out PropertyProjectActionState state))
            {
                panelStatus = BuildPropertyActionPanelStatus(state);
            }
        }

        private static string BuildButtonTooltip(Button button, string enabledBody)
        {
            if (button == null)
            {
                return enabledBody ?? string.Empty;
            }

            string body = string.IsNullOrWhiteSpace(enabledBody) ? "Use this control." : enabledBody.Trim();
            if (button.gameObject.activeInHierarchy && button.interactable)
            {
                return body;
            }

            string label = GetButtonLabel(button);
            if (string.Equals(label, "Built", StringComparison.OrdinalIgnoreCase)
                || body.IndexOf("Blocked:", StringComparison.OrdinalIgnoreCase) >= 0
                || body.IndexOf("already built", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return body;
            }

            string reason = string.Empty;
            if (!string.IsNullOrWhiteSpace(label) && label.StartsWith("Blocked:", StringComparison.OrdinalIgnoreCase))
            {
                reason = label.Substring("Blocked:".Length).Trim();
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = "not available for the current selection.";
            }

            return $"{body} Currently unavailable: {reason}";
        }

        private static string BuildButtonTitle(Button button, string fallback)
        {
            string label = GetButtonLabel(button);
            return string.IsNullOrWhiteSpace(label) ? fallback : label;
        }

        private static string GetButtonLabel(Button button)
        {
            TMP_Text text = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
            return text != null ? text.text.Trim() : string.Empty;
        }

        private string BuildPropertySummary(OwnedEntry entry)
        {
            if (entry.Kind == OwnedKind.Store && storeRuntime != null && storeRuntime.RuntimeState != null)
            {
                string focus = storeRuntime.CurrentBusiness != null ? storeRuntime.CurrentBusiness.MainFocus.DisplayName : "Balanced";
                BusinessRuntimeState runtime = storeRuntime.RuntimeState;
                string pressure = BuildStorePriorityState(runtime);
                string summary = $"{focus}{BuildControlPolicySummary(storeRuntime.CurrentBusiness)} | Staff {runtime.FilledWorkerCount}/{runtime.TargetWorkerCount} | Stock {runtime.StockHealth01:P0} | Business Cash {FormatMoney(storeRuntime.CurrentCashCents)} | {pressure}";
                string bottleneck = storeRuntime.CurrentBusiness != null && storeRuntime.CurrentBusiness.Capacity != null
                    ? BuildBottleneckSegment(storeRuntime.CurrentBusiness.Capacity.BottleneckDisplayName)
                    : string.Empty;
                return summary + bottleneck;
            }

            if (entry.Kind == OwnedKind.Renting)
            {
                PopulationState population = GetRefreshedPopulationForRenting();
                if (population == null)
                {
                    return "Renting and boarding records unavailable.";
                }

                int openBoardingBeds = Mathf.Max(0, population.boardingHouseCapacity - population.boardingHouseUsed);
                return $"Vacancies {population.rentalVacancyCount} ({population.playerOwnedRentalVacancyCount} player-owned) | Applicants {population.rentalApplicantCount} | Boarding House {population.boardingHouseUsed}/{population.boardingHouseCapacity} ({openBoardingBeds} open) | Transients {population.transientPersonCount}";
            }

            PlacedBuilding building = GetBuilding(entry.BuildingId);
            TownPlot plot = GetPlot(entry.PlotId);
            if (entry.Kind == OwnedKind.Land && plot != null)
            {
                return BuildOwnedLandPropertySummary(plot);
            }

            if (building != null)
            {
                BusinessInstanceState business = GetBusinessForBuilding(building.id);
                if (business != null)
                {
                    return BuildManagedBusinessPropertySummary(business, building, plot);
                }

                if (entry.Kind == OwnedKind.Holding && IsResidentialHolding(building))
                {
                    return BuildResidentialHoldingPropertySummary(building, plot);
                }

                if (IsPlayerOwnedShellWithDefinition(building))
                {
                    return BuildVacantShellPropertySummary(building, plot);
                }

                return $"Property | Site {building.siteSizeCells.x}x{building.siteSizeCells.y}{BuildTownContextSummarySegment(plot)}";
            }

            if (plot != null)
            {
                return BuildOwnedLandPropertySummary(plot);
            }

            return "Property details unavailable.";
        }

        private static string BuildBottleneckSegment(string bottleneckDisplayName)
        {
            if (string.IsNullOrWhiteSpace(bottleneckDisplayName)
                || string.Equals(bottleneckDisplayName, "none", System.StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return $" | Bottleneck: {bottleneckDisplayName}";
        }


        private string BuildStoreOverviewWithPriority()
        {
            if (storeRuntime == null || storeRuntime.RuntimeState == null)
            {
                return BuildStoreUnavailableOverviewFallback(view == null || view.StoreFinanceText == null);
            }

            BusinessRuntimeState runtime = storeRuntime.RuntimeState;
            StringBuilder builder = new();
            builder.AppendLine("<b>Store Status</b>");
            builder.AppendLine($"Condition: {ColorizeStatus(BuildStorePriorityState(runtime))}");
            builder.AppendLine($"Pressure: {BuildStoreMainProblem(runtime)}");
            string guidance = BuildStoreRecommendedAction(runtime);
            if (!string.IsNullOrWhiteSpace(guidance))
            {
                builder.AppendLine($"Manager Read: {guidance}");
            }

            builder.AppendLine(BuildStoreOperatingPressureLine(runtime));
            if (view == null || view.StoreFinanceText == null)
            {
                builder.AppendLine();
                builder.Append(BuildStoreFinanceBlock());
            }

            return builder.ToString();
        }

        private static string BuildStoreUnavailableOverviewFallback(bool includeFinanceFallback)
        {
            StringBuilder builder = new();
            builder.AppendLine("<b>Store Status</b>");
            builder.AppendLine("Condition: unavailable");
            builder.AppendLine("Pressure: store records unavailable");
            builder.Append("Operations: no current readout");
            if (includeFinanceFallback)
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine("<b>Store Ledger</b>");
                builder.AppendLine("Store Cash             unavailable");
                builder.AppendLine("Operating Reserve      unavailable");
                builder.AppendLine("Available to Transfer  unavailable");
                builder.AppendLine();
                builder.AppendLine("<b>Net Performance</b>");
                builder.AppendLine("Today      unavailable");
                builder.AppendLine("Week       unavailable");
                builder.AppendLine("Month      unavailable");
                builder.AppendLine("Year       unavailable");
                builder.AppendLine("All-Time   unavailable");
                builder.AppendLine();
                builder.Append("<b>Pricing & Margin</b>\nPrice Adjustment    unavailable");
            }

            return builder.ToString();
        }

        private string BuildStoreFinanceBlock()
        {
            BusinessRuntimeState runtime = storeRuntime != null ? storeRuntime.RuntimeState : null;
            if (runtime == null)
            {
                return "<b>Store Ledger</b>\nStore ledger unavailable.";
            }

            int storeCash = storeRuntime != null ? storeRuntime.CurrentCashCents : runtime != null ? runtime.CurrentCashCents : 0;
            int protectedReserve = storeRuntime != null ? storeRuntime.ProtectedBusinessCashReserveCents : 0;
            StringBuilder builder = new();
            builder.AppendLine("<b>Store Ledger</b>");
            builder.AppendLine(BuildStoreCashLedgerText(storeCash, protectedReserve));
            builder.AppendLine();
            builder.Append(BuildBusinessNetReadout(runtime));
            builder.AppendLine();
            builder.AppendLine("Owner cash transfers move working capital only; this net read is operating performance.");
            builder.AppendLine();
            if (storeRuntime != null && storeRuntime.CurrentBusiness != null)
            {
                builder.AppendLine(ManagerPolicyEffects.BuildControlPolicyLine(storeRuntime.CurrentBusiness));
                builder.AppendLine(storeRuntime.BuildStorePolicyTelemetryText());
                builder.AppendLine();
            }

            builder.Append(storeRuntime != null
                ? storeRuntime.BuildStorePricingMarginText()
                : "<b>Pricing & Margin</b>\nPrice Adjustment    unavailable");
            return builder.ToString();
        }

        private string BuildManagedBusinessLedgerBlock(BusinessInstanceState business)
        {
            BusinessRuntimeState runtime = business != null ? business.RuntimeState : null;
            if (business == null || runtime == null)
            {
                return "<b>Business Ledger</b>\nBusiness ledger unavailable.";
            }

            BusinessPortfolioSummary summary = BuildBusinessPortfolioSummary(business);
            StringBuilder builder = new();
            builder.AppendLine("<b>Business Ledger</b>");
            builder.AppendLine($"Business Cash         {FormatMoney(summary.BusinessCashCents)}");
            builder.AppendLine($"Operating Reserve     {FormatMoney(summary.OperatingReserveCents)}");
            builder.AppendLine($"Survival Reserve      {FormatMoney(summary.SurvivalReserveCents)}");
            builder.AppendLine($"Available to Transfer {FormatMoney(summary.TransferableCashCents)}");
            builder.AppendLine($"Continuity            {summary.ContinuitySummary}");
            builder.AppendLine();
            builder.Append(BuildBusinessNetReadout(runtime));
            builder.AppendLine();
            builder.AppendLine("Owner cash transfers move working capital only; this net read is operating performance.");
            builder.AppendLine();
            builder.AppendLine(ManagerPolicyEffects.BuildControlPolicyLine(business));
            if (sharedBusinessRuntime != null)
            {
                builder.AppendLine(sharedBusinessRuntime.BuildBusinessPricingMarginText(business));
            }

            return builder.ToString();
        }

        private static string BuildStoreCashLedgerText(int storeCashCents, int operatingReserveCents)
        {
            int cash = Mathf.Max(0, storeCashCents);
            int reserve = Mathf.Max(0, operatingReserveCents);
            int available = Mathf.Max(0, cash - reserve);
            StringBuilder builder = new();
            builder.AppendLine($"Store Cash             {FormatMoney(cash)}");
            builder.AppendLine($"Operating Reserve      {FormatMoney(reserve)}");
            builder.Append($"Available to Transfer  {FormatMoney(available)}");
            return builder.ToString();
        }

        private static string BuildBusinessNetReadout(BusinessRuntimeState runtime)
        {
            if (runtime == null)
            {
                return "<b>Net Performance</b>\nToday      unavailable\nWeek       unavailable\nMonth      unavailable\nYear       unavailable\nAll-Time   unavailable";
            }

            StringBuilder builder = new();
            builder.AppendLine("<b>Net Performance</b>");
            AppendNetRow(builder, "Today", runtime.LastDailyNetCents);
            AppendNetRow(builder, "Week", runtime.WeekToDateNetCents);
            AppendNetRow(builder, "Month", runtime.MonthToDateNetCents);
            AppendNetRow(builder, "Year", runtime.YearToDateNetCents);
            AppendNetRow(builder, "All-Time", runtime.AllTimeNetCents);
            return builder.ToString().TrimEnd();
        }

        private static string BuildBusinessNetCompactReadout(BusinessRuntimeState runtime)
        {
            if (runtime == null)
            {
                return "Net unavailable";
            }

            return $"Net Today {FormatSignedMoney(runtime.LastDailyNetCents)} | Week {FormatSignedMoney(runtime.WeekToDateNetCents)} | Month {FormatSignedMoney(runtime.MonthToDateNetCents)} | Year {FormatSignedMoney(runtime.YearToDateNetCents)} | All-Time {FormatSignedMoney(runtime.AllTimeNetCents)}";
        }

        private string BuildStoreOperatingPressureLine(BusinessRuntimeState runtime)
        {
            if (runtime == null)
            {
                return "Operations: store records unavailable.";
            }

            string restock = storeRuntime != null && storeRuntime.ReorderNeeded ? "Reorder needed" : "Stock steady";
            string blocked = string.IsNullOrWhiteSpace(runtime.LastWeeklyBlockedReason)
                ? "No weekly blocker"
                : CompactReason(runtime.LastWeeklyBlockedReason);
            string supply = storeRuntime != null ? storeRuntime.BuildStoreSupplyReadinessLine() : string.Empty;
            string supplySegment = string.IsNullOrWhiteSpace(supply) ? string.Empty : $" | {supply}";
            return $"Operations: Staff {runtime.FilledWorkerCount}/{runtime.TargetWorkerCount} | Stock {runtime.StockHealth01:P0} | {restock} | {blocked}{supplySegment}";
        }

        private string BuildStorePriorityState(BusinessRuntimeState runtime)
        {
            if (runtime == null)
            {
                return "Store records unavailable.";
            }

            if (!string.IsNullOrWhiteSpace(runtime.LastWeeklyBlockedReason))
            {
                return "Blocked";
            }

            if (storeRuntime != null && storeRuntime.CurrentCashCents < storeRuntime.ProtectedBusinessCashReserveCents)
            {
                return "Below reserve";
            }

            if (runtime.FilledWorkerCount < runtime.TargetWorkerCount)
            {
                return "Understaffed";
            }

            if (storeRuntime != null && storeRuntime.ReorderNeeded)
            {
                return "Needs restock";
            }

            if (runtime.StockHealth01 < 0.35f)
            {
                return "Thin stock";
            }

            return "Stable";
        }


        private string BuildStoreMainProblem(BusinessRuntimeState runtime)
        {
            if (runtime == null)
            {
                return "Store records unavailable.";
            }

            if (runtime.FilledWorkerCount < runtime.TargetWorkerCount)
            {
                return "Open staff slot is limiting service.";
            }

            if (storeRuntime != null && storeRuntime.CurrentCashCents < storeRuntime.ProtectedBusinessCashReserveCents)
            {
                return "Business cash is below the protected reserve.";
            }

            if (!string.IsNullOrWhiteSpace(runtime.LastWeeklyBlockedReason))
            {
                return CompactReason(runtime.LastWeeklyBlockedReason);
            }

            if (storeRuntime != null && storeRuntime.ReorderNeeded)
            {
                return "Stock is below the reorder target.";
            }

            if (runtime.StockHealth01 < 0.35f)
            {
                return "Stock is thin.";
            }

            return "No urgent issue.";
        }

        private string BuildStoreRecommendedAction(BusinessRuntimeState runtime)
        {
            if (runtime == null)
            {
                return "Refresh store records.";
            }

            if (runtime.FilledWorkerCount < runtime.TargetWorkerCount)
            {
                return "Review candidates and fill the open position.";
            }

            if (storeRuntime != null && storeRuntime.CurrentCashCents < storeRuntime.ProtectedBusinessCashReserveCents)
            {
                return "Fund the operating reserve before reorder or payroll.";
            }

            if (storeRuntime != null && storeRuntime.ReorderNeeded)
            {
                return "Keep cash ready for the weekly reorder.";
            }

            if (runtime.StockHealth01 < 0.35f)
            {
                return "Hold pricing steady until stock improves.";
            }

            return string.Empty;
        }

        private static string BuildControlPolicySummary(BusinessInstanceState business)
        {
            return business == null
                ? string.Empty
                : $" | {business.ControlStateDisplayName}/{business.ManagerPolicyDisplayName}";
        }

        private string BuildNonStoreDetail(OwnedEntry selected)
        {
            if (selected.Kind == OwnedKind.Renting)
            {
                return BuildRentingAndBoardingDetail();
            }

            PlacedBuilding building = GetBuilding(selected.BuildingId);
            TownPlot plot = GetPlot(selected.PlotId);
            if (selected.Kind == OwnedKind.Land && plot != null)
            {
                return BuildOwnedLandInspectionDetail(plot);
            }

            if (building != null)
            {
                BusinessInstanceState business = GetBusinessForBuilding(building.id);
                if (business != null)
                {
                    return BuildManagedBusinessInspectionDetail(business, building, plot);
                }

                if (selected.Kind == OwnedKind.Holding && IsResidentialHolding(building))
                {
                    return BuildResidentialHoldingInspectionDetail(building, plot);
                }

                if (IsPlayerOwnedShellWithDefinition(building))
                {
                    return BuildVacantShellInspectionDetail(building, plot);
                }
            }

            StringBuilder builder = new();
            if (building != null)
            {
                string display = building.definition != null ? building.definition.DisplayName : $"Building {building.id:000}";
                builder.AppendLine("Property");
                builder.AppendLine($"Site: {display}, Plot {building.plotId:000}");
                builder.AppendLine("Current read: player-owned building with no active operating summary.");
                builder.AppendLine("Next move: Inspect the site, then review build or staffing options if available.");
                builder.AppendLine($"Footprint {building.footprint.width}x{building.footprint.depth} | Site {building.siteSizeCells.x}x{building.siteSizeCells.y}");
            }

            if (plot != null)
            {
                if (builder.Length == 0)
                {
                    builder.AppendLine("Land");
                }

                builder.AppendLine($"Town context: {plot.zone} | Site {plot.siteSizeCells.x}x{plot.siteSizeCells.y} | Frontage {plot.frontageCells}");
                AppendLandAppreciationLine(builder, plot.id);
            }

            return builder.Length == 0 ? "Property details unavailable." : builder.ToString();
        }

        private string BuildManagedBusinessPropertySummary(BusinessInstanceState business, PlacedBuilding building, TownPlot plot)
        {
            if (business == null)
            {
                return "Business details unavailable.";
            }

            BusinessRuntimeState runtime = business.RuntimeState;
            BusinessPortfolioSummary summary = BuildBusinessPortfolioSummary(business);
            string status = BuildBusinessSummaryStatus(summary);
            string staff = runtime != null ? $"Staff {runtime.FilledWorkerCount}/{runtime.TargetWorkerCount}" : "Staff unavailable";
            string cash = $"Business Cash {FormatMoney(summary.BusinessCashCents)}";
            string reserve = $"Reserve {FormatMoney(summary.SurvivalReserveCents)}";
            string transferable = $"Transferable {FormatMoney(summary.TransferableCashCents)}";
            string equity = $"Equity {FormatMoney(summary.EquityContributionCents)}";
            string liabilities = $"Liabilities {FormatMoney(summary.BusinessLiabilityCents)}";
            return $"{business.RuntimeDisplayName} | {staff} | {cash} | {reserve} | {transferable} | {equity} | {liabilities} | {status}{BuildTownContextSummarySegment(plot)}";
        }

        private string BuildOwnedLandPropertySummary(TownPlot plot)
        {
            if (plot == null)
            {
                return "Land details unavailable.";
            }

            return $"Land | Intent {selectedOwnedLandDevelopmentIntent} | Site {plot.siteSizeCells.x}x{plot.siteSizeCells.y} | Frontage {plot.frontageCells} | {BuildOwnedLandCompactStatus(plot.id)}{BuildTownContextSummarySegment(plot)}";
        }

        private string BuildVacantShellPropertySummary(PlacedBuilding building, TownPlot plot)
        {
            if (building == null)
            {
                return "Vacant building details unavailable.";
            }

            return $"Vacant Building | Site {building.siteSizeCells.x}x{building.siteSizeCells.y} | {BuildVacantShellCompactStatus(building.id)}{BuildTownContextSummarySegment(plot)}";
        }

        private string BuildResidentialHoldingPropertySummary(PlacedBuilding building, TownPlot plot)
        {
            if (building == null)
            {
                return "Residence details unavailable.";
            }

            HouseholdState household = GetResidentHousehold(building.id);
            if (household != null)
            {
                int capacity = HouseholdUpgradeCatalog.GetFoodReserveCapacityUnits(household);
                int upgrades = HouseholdUpgradeCatalog.CountBuiltUpgrades(household);
                return $"Home | {household.householdName} | Reserves {household.foodReserveUnits}/{capacity} | Upgrades {upgrades}/{HouseholdUpgradeCatalog.Count}{BuildResidentialRentalSummarySegment(building.id)}{BuildTownContextSummarySegment(plot)}";
            }

            return $"Home | No resident household assigned | Upgrade review pending{BuildResidentialRentalSummarySegment(building.id)}{BuildTownContextSummarySegment(plot)}";
        }

        private string BuildManagedBusinessInspectionDetail(BusinessInstanceState business, PlacedBuilding building, TownPlot plot)
        {
            StringBuilder builder = new();
            string display = building != null && building.definition != null ? building.definition.DisplayName : $"Building {building.id:000}";
            builder.AppendLine(business.RuntimeDisplayName);
            builder.AppendLine($"{BusinessRuntimeNaming.GetBusinessTypeDisplayName(business.BusinessType)} | Operator {GetOwnerDisplayName(business)}");
            builder.AppendLine($"Current read: {BuildBusinessMainProblem(business)}");
            builder.AppendLine($"Next move: {BuildBusinessRecommendedAction(business)}");
            builder.AppendLine(ManagerPolicyEffects.BuildControlPolicyLine(business));
            builder.AppendLine(BuildBusinessPortfolioSummary(business).BuildLedgerLine());
            if (business.RuntimeState != null)
            {
                builder.AppendLine(BuildBusinessNetCompactReadout(business.RuntimeState));
            }

            if (acquisitionMarket != null && acquisitionMarket.TryEstimatePlayerBusinessSaleOffer(business.AssignedBuildingId, out _, out string saleRead))
            {
                builder.AppendLine($"Sale read: {saleRead}");
            }

            if (sharedBusinessRuntime != null)
            {
                builder.AppendLine(sharedBusinessRuntime.BuildFocusLine(business));
            }

            builder.AppendLine($"Site: {display}, Plot {building.plotId:000}");
            builder.AppendLine($"Footprint {building.footprint.width}x{building.footprint.depth} | Site {building.siteSizeCells.x}x{building.siteSizeCells.y}");
            if (plot != null)
            {
                builder.AppendLine($"Town context: {plot.zone} | Frontage {plot.frontageCells}");
                AppendLandAppreciationLine(builder, plot.id);
            }

            return builder.ToString();
        }

        private string BuildOwnedLandInspectionDetail(TownPlot plot)
        {
            StringBuilder builder = new();
            builder.AppendLine("Owned Land");
            builder.AppendLine($"Site: Plot {plot.id:000} | {plot.siteSizeCells.x}x{plot.siteSizeCells.y} | Frontage {plot.frontageCells}");
            builder.AppendLine($"Town context: {plot.zone}");

            OwnedPlotBuildabilityState buildability = FindBuildabilityState(plot.id);
            if (buildability == null)
            {
                builder.AppendLine("Current read: build planning is unavailable for this parcel.");
                builder.AppendLine("Next move: Refresh acquisitions or reopen Properties.");
                AppendLandAppreciationLine(builder, plot.id);
                return builder.ToString();
            }

            if (!buildability.playerOwned)
            {
                builder.AppendLine("Current read: this parcel is not in the player portfolio.");
                builder.AppendLine("Next move: select an owned parcel before planning development.");
                AppendLandAppreciationLine(builder, plot.id);
                return builder.ToString();
            }

            if (!buildability.empty)
            {
                builder.AppendLine("Current read: this parcel is already occupied and not ready for a fresh shell plan.");
                builder.AppendLine("Next move: select an empty owned parcel or inspect the occupied site.");
                AppendLandAppreciationLine(builder, plot.id);
                return builder.ToString();
            }

            if (!buildability.buildable)
            {
                builder.AppendLine("Current read: this parcel is in the portfolio but not buildable right now.");
                if (!string.IsNullOrWhiteSpace(buildability.reason))
                {
                    builder.AppendLine($"Status: blocked — {CompactReason(buildability.reason)}");
                    builder.AppendLine($"Next move: {FirstSessionGuidanceText.BuildConstructionBlockerNextStep(buildability.reason)}");
                }

                AppendLandAppreciationLine(builder, plot.id);
                return builder.ToString();
            }

            builder.AppendLine($"Current read: empty owned parcel ready for {selectedOwnedLandDevelopmentIntent.ToString().ToLowerInvariant()} planning.");
            builder.AppendLine($"Intent: {selectedOwnedLandDevelopmentIntent}");
            AppendOwnedLandSelectionLines(builder, plot.id);
            AppendLandAppreciationLine(builder, plot.id);
            return builder.ToString();
        }

        private void AppendOwnedLandSelectionLines(StringBuilder builder, int plotId)
        {
            if (builder == null)
            {
                return;
            }

            if (selectedOwnedLandDevelopmentIntent == OwnedLandDevelopmentIntent.House)
            {
                IReadOnlyList<BuildingDefinition> houseOptions = acquisitionMarket != null ? acquisitionMarket.GetHouseBuildOptionsForOwnedPlot(plotId) : null;
                int optionCount = houseOptions != null ? houseOptions.Count : 0;
                selectedBuildOptionIndex = WrapIndex(selectedBuildOptionIndex, optionCount);
                string selectedPlan = optionCount > 0 && houseOptions[selectedBuildOptionIndex] != null
                    ? houseOptions[selectedBuildOptionIndex].DisplayName
                    : "No house plan";
                builder.AppendLine($"Selected plan: {selectedPlan}");
                AppendSelectedPropertyActionRead(
                    builder,
                    "Build House",
                    "ready to build.",
                    "Press Build House, or cycle the house plan.");

                return;
            }

            EnsureOwnedLandBusinessSelection(plotId);
            int candidateCount = GetBusinessDevelopmentCandidateCount();
            selectedBusinessActivationIndex = WrapIndex(selectedBusinessActivationIndex, candidateCount);
            if (candidateCount <= 0
                || acquisitionMarket == null
                || !acquisitionMarket.TryGetBusinessDevelopmentCandidate(selectedBusinessActivationIndex, out BusinessActivationCandidateState candidate)
                || candidate == null)
            {
                builder.AppendLine("Selected start: none");
                AppendSelectedPropertyActionRead(
                    builder,
                    "Build Shell",
                    "no supported business start is selected.",
                    "Cycle to another parcel or reopen the acquisition view.");
                return;
            }

            builder.AppendLine($"Selected start: {candidate.displayName}");
            IReadOnlyList<BuildingDefinition> shellOptions = acquisitionMarket.GetBusinessBuildOptionsForOwnedPlot(plotId, selectedBusinessActivationIndex);
            int shellCount = shellOptions != null ? shellOptions.Count : 0;
            selectedBusinessBuildOptionIndex = WrapIndex(selectedBusinessBuildOptionIndex, shellCount);
            string selectedShell = shellCount > 0 && shellOptions[selectedBusinessBuildOptionIndex] != null
                ? shellOptions[selectedBusinessBuildOptionIndex].DisplayName
                : "No shell plan";
            builder.AppendLine($"Selected shell: {selectedShell}");
            AppendSelectedPropertyActionRead(
                builder,
                "Build Shell",
                "ready to build.",
                "Press Build Shell, or cycle the business and shell.");
        }

        private string BuildVacantShellInspectionDetail(PlacedBuilding building, TownPlot plot)
        {
            StringBuilder builder = new();
            string display = building.definition != null ? building.definition.DisplayName : $"Building {building.id:000}";
            builder.AppendLine("Vacant Building");
            builder.AppendLine($"Site: {display}, Plot {building.plotId:000}");
            builder.AppendLine("Current read: player-owned shell with no active business yet.");
            if (plot != null)
            {
                builder.AppendLine($"Town context: {plot.zone} | Frontage {plot.frontageCells}");
            }

            if (acquisitionMarket == null)
            {
                builder.AppendLine("Status: startup planning unavailable.");
                builder.AppendLine("Next move: reopen the management panel once acquisition systems are ready.");
                return builder.ToString();
            }

            EnsureBusinessActivationSelection(building.id);
            int candidateCount = GetBusinessActivationCandidateCount(building.id);
            selectedBusinessActivationIndex = WrapIndex(selectedBusinessActivationIndex, candidateCount);
            if (candidateCount <= 0
                || !acquisitionMarket.TryGetBusinessActivationCandidate(building.id, selectedBusinessActivationIndex, out BusinessActivationCandidateState candidate)
                || candidate == null)
            {
                builder.AppendLine("Selected start: none");
                AppendSelectedPropertyActionRead(
                    builder,
                    "Start Business",
                    "no supported business start is selected.",
                    "Cycle to a shell that supports a stronger startup.");
                return builder.ToString();
            }

            builder.AppendLine($"Selected start: {candidate.displayName}");
            AppendSelectedPropertyActionRead(
                builder,
                "Start Business",
                "ready to open.",
                "Press Start Business, or cycle the business type.");

            builder.AppendLine($"Footprint {building.footprint.width}x{building.footprint.depth} | Site {building.siteSizeCells.x}x{building.siteSizeCells.y}");
            return builder.ToString();
        }

        private string BuildResidentialHoldingInspectionDetail(PlacedBuilding building, TownPlot plot)
        {
            StringBuilder builder = new();
            string display = building.definition != null ? building.definition.DisplayName : $"Building {building.id:000}";
            builder.AppendLine("Residence");
            builder.AppendLine($"Site: {display}, Building {building.id:000}");
            if (plot != null)
            {
                builder.AppendLine($"Town context: {plot.zone} | Frontage {plot.frontageCells}");
            }

            HouseholdState household = GetResidentHousehold(building.id);
            if (household == null)
            {
                builder.AppendLine("Current read: no resident household is assigned yet.");
                builder.AppendLine("Next move: place or attract a resident household before expecting upgrade value.");
                return builder.ToString();
            }

            int capacity = HouseholdUpgradeCatalog.GetFoodReserveCapacityUnits(household);
            int builtCount = HouseholdUpgradeCatalog.CountBuiltUpgrades(household);
            builder.AppendLine($"Current read: {household.householdName} is living here with reserves {household.foodReserveUnits}/{capacity}.");
            builder.AppendLine($"Upgrade posture: {builtCount}/{HouseholdUpgradeCatalog.Count} built{BuildResidentialRentalSummarySegment(building.id)}");

            int optionCount = GetHouseholdUpgradeOptionCount();
            selectedHouseholdUpgradeIndex = WrapIndex(selectedHouseholdUpgradeIndex, optionCount);
            if (optionCount <= 0 || !TryGetSelectedHouseholdUpgradeDefinition(out HouseholdUpgradeDefinition definition) || definition == null)
            {
                builder.AppendLine("Selected upgrade: none");
                builder.AppendLine("Next move: no upgrade is available for this holding.");
                return builder.ToString();
            }

            builder.AppendLine($"Selected upgrade: {definition.DisplayName}");
            AppendSelectedPropertyActionRead(
                builder,
                "Build Upgrade",
                "ready to build.",
                "Press Build Upgrade, or cycle to another improvement.");

            return builder.ToString();
        }

        private string BuildBusinessSummaryStatus(BusinessInstanceState business)
        {
            return BuildBusinessSummaryStatus(BuildBusinessPortfolioSummary(business));
        }

        private static string BuildBusinessSummaryStatus(BusinessPortfolioSummary summary)
        {
            if (string.IsNullOrWhiteSpace(summary.InstanceId) && summary.BusinessCashCents <= 0 && summary.SurvivalReserveCents <= 0)
            {
                return "Status unavailable";
            }

            return summary.ContinuityStatus switch
            {
                BusinessContinuityStatus.MissedPayroll => "Missed payroll",
                BusinessContinuityStatus.CashBelowSurvivalReserve => "Out of cash",
                BusinessContinuityStatus.Blocked => "Blocked",
                BusinessContinuityStatus.MissingRequiredRoles => "Required roles missing",
                BusinessContinuityStatus.OwnerOperatorFallback => "Owner-operator fallback",
                BusinessContinuityStatus.Understaffed => "Understaffed",
                BusinessContinuityStatus.Pressured => "Low efficiency",
                BusinessContinuityStatus.Unavailable => "Status unavailable",
                _ => "Operating"
            };
        }

        private string BuildOwnedLandCompactStatus(int plotId)
        {
            OwnedPlotBuildabilityState buildability = FindBuildabilityState(plotId);
            if (buildability == null)
            {
                return "Planning unavailable";
            }

            if (!buildability.playerOwned)
            {
                return "Not in portfolio";
            }

            if (!buildability.empty)
            {
                return "Already occupied";
            }

            if (!buildability.buildable)
            {
                return "Blocked";
            }

            if (selectedOwnedLandDevelopmentIntent == OwnedLandDevelopmentIntent.House)
            {
                IReadOnlyList<BuildingDefinition> houseOptions = acquisitionMarket != null ? acquisitionMarket.GetHouseBuildOptionsForOwnedPlot(plotId) : null;
                int optionCount = houseOptions != null ? houseOptions.Count : 0;
                selectedBuildOptionIndex = WrapIndex(selectedBuildOptionIndex, optionCount);
                string selectedPlan = optionCount > 0 && houseOptions[selectedBuildOptionIndex] != null
                    ? houseOptions[selectedBuildOptionIndex].DisplayName
                    : "House plan";
                return TryGetSelectedHouseConstructionBlockedReason(plotId, out _)
                    ? $"{selectedPlan} blocked"
                    : $"{selectedPlan} ready";
            }

            EnsureOwnedLandBusinessSelection(plotId);
            int candidateCount = GetBusinessDevelopmentCandidateCount();
            selectedBusinessActivationIndex = WrapIndex(selectedBusinessActivationIndex, candidateCount);
            if (candidateCount <= 0
                || acquisitionMarket == null
                || !acquisitionMarket.TryGetBusinessDevelopmentCandidate(selectedBusinessActivationIndex, out BusinessActivationCandidateState candidate)
                || candidate == null)
            {
                return "No business start";
            }

            return TryGetSelectedBusinessConstructionBlockedReason(plotId, out _)
                ? $"{candidate.displayName} blocked"
                : $"{candidate.displayName} ready";
        }

        private string BuildVacantShellCompactStatus(int buildingId)
        {
            if (acquisitionMarket == null)
            {
                return "Startup unavailable";
            }

            EnsureBusinessActivationSelection(buildingId);
            int candidateCount = GetBusinessActivationCandidateCount(buildingId);
            selectedBusinessActivationIndex = WrapIndex(selectedBusinessActivationIndex, candidateCount);
            if (candidateCount <= 0
                || !acquisitionMarket.TryGetBusinessActivationCandidate(buildingId, selectedBusinessActivationIndex, out BusinessActivationCandidateState candidate)
                || candidate == null)
            {
                return "No business start";
            }

            return TryGetSelectedStartupBlockedReason(buildingId, out _)
                ? $"{candidate.displayName} blocked"
                : $"{candidate.displayName} ready";
        }

        private static string BuildTownContextSummarySegment(TownPlot plot)
        {
            return plot != null ? $" | Town Context {plot.zone}" : string.Empty;
        }

        private void AppendLandAppreciationLine(StringBuilder builder, int plotId)
        {
            if (builder == null
                || acquisitionMarket == null
                || !acquisitionMarket.TryGetLandAppreciationForPlot(plotId, out LandAppreciationState appreciation)
                || appreciation == null)
            {
                return;
            }

            builder.AppendLine($"Value: Basis {FormatMoney(appreciation.TotalBasisCents)} | Est {FormatMoney(appreciation.currentEstimatedValueCents)} | Gain/Loss {FormatSignedMoney(appreciation.appreciationDeltaCents)}");
        }

        private string BuildOwnedLandBuildabilityText(int plotId)
        {
            StringBuilder builder = new();
            if (acquisitionMarket == null)
            {
                builder.AppendLine("Build planning unavailable.");
                return builder.ToString();
            }

            OwnedPlotBuildabilityState buildability = FindBuildabilityState(plotId);
            if (buildability == null)
            {
                builder.AppendLine("No build plan available.");
                return builder.ToString();
            }

            if (!buildability.playerOwned || !buildability.empty || !buildability.buildable)
            {
                builder.AppendLine("No fitting building plan.");
                if (!string.IsNullOrWhiteSpace(buildability.reason))
                {
                    builder.AppendLine($"Blocked: {buildability.reason}");
                }

                return builder.ToString();
            }

            if (selectedOwnedLandDevelopmentIntent == OwnedLandDevelopmentIntent.House)
            {
                builder.AppendLine("Development: House");
                IReadOnlyList<BuildingDefinition> houseOptions = acquisitionMarket.GetHouseBuildOptionsForOwnedPlot(plotId);
                int optionCount = houseOptions != null ? houseOptions.Count : 0;
                if (optionCount <= 0)
                {
                    builder.AppendLine("No house-oriented shell plan physically fits this plot.");
                    AppendSelectedPropertyActionRead(
                        builder,
                        "Build House",
                        "no house plan is available for this plot.",
                        "Cycle to another parcel or reopen the acquisition view.");
                    return builder.ToString();
                }

                selectedBuildOptionIndex = WrapIndex(selectedBuildOptionIndex, optionCount);
                BuildingDefinition selectedDefinition = houseOptions[selectedBuildOptionIndex];
                builder.AppendLine($"House plan {selectedBuildOptionIndex + 1}/{optionCount}: {(selectedDefinition != null ? selectedDefinition.DisplayName : "House")}");
                if (acquisitionMarket.TryGetHouseConstructionFitForOwnedPlotOption(plotId, selectedBuildOptionIndex, out PlayerDevelopmentFitResult fit) && fit != null)
                {
                    AppendFitSummary(builder, "Build fit", fit.fitScore01, fit.costMultiplier, fit.WarningSummary);
                }

                if (acquisitionMarket.TryGetHouseConstructionQuoteForOwnedPlotOption(plotId, selectedBuildOptionIndex, out ConstructionInputQuote quote, out string quoteMessage))
                {
                    AppendConstructionQuote(builder, quote);
                }
                else
                {
                    builder.AppendLine("Cost unavailable | Blocked");
                    builder.AppendLine($"Missing: {CompactReason(quoteMessage)}");
                    AppendBlockerNextStep(builder, quoteMessage);
                }

                AppendSelectedPropertyActionRead(
                    builder,
                    "Build House",
                    "ready to build.",
                    "Press Build House, or cycle the house plan.");
                return builder.ToString();
            }

            builder.AppendLine("Development: Business");
            EnsureOwnedLandBusinessSelection(plotId);
            int candidateCount = GetBusinessDevelopmentCandidateCount();
            selectedBusinessActivationIndex = WrapIndex(selectedBusinessActivationIndex, candidateCount);
            if (candidateCount <= 0
                || !acquisitionMarket.TryGetBusinessDevelopmentCandidate(selectedBusinessActivationIndex, out BusinessActivationCandidateState candidate)
                || candidate == null)
            {
                builder.AppendLine("No supported business starts.");
                AppendSelectedPropertyActionRead(
                    builder,
                    "Build Shell",
                    "no supported business start is selected.",
                    "Cycle to another business type or inspect another parcel.");
                return builder.ToString();
            }

            builder.AppendLine($"Business {selectedBusinessActivationIndex + 1}/{candidateCount}: {candidate.displayName}");
            if (!candidate.eligible)
            {
                builder.AppendLine($"Blocked: {CompactReason(candidate.reason)}");
                AppendBlockerNextStep(builder, candidate.reason);
                return builder.ToString();
            }

            IReadOnlyList<BuildingDefinition> shellOptions = acquisitionMarket.GetBusinessBuildOptionsForOwnedPlot(plotId, selectedBusinessActivationIndex);
            int shellCount = shellOptions != null ? shellOptions.Count : 0;
            if (shellCount <= 0)
            {
                builder.AppendLine("No shell plan physically fits this plot.");
                AppendSelectedPropertyActionRead(
                    builder,
                    "Build Shell",
                    "no building shell is available for the selected business start.",
                    "Cycle the business type or inspect another parcel.");
                return builder.ToString();
            }

            selectedBusinessBuildOptionIndex = WrapIndex(selectedBusinessBuildOptionIndex, shellCount);
            BuildingDefinition shellDefinition = shellOptions[selectedBusinessBuildOptionIndex];
            builder.AppendLine($"Shell {selectedBusinessBuildOptionIndex + 1}/{shellCount}: {(shellDefinition != null ? shellDefinition.DisplayName : "Building")}");
            if (acquisitionMarket.TryGetBusinessConstructionFitForOwnedPlotOption(plotId, selectedBusinessActivationIndex, selectedBusinessBuildOptionIndex, out PlayerDevelopmentFitResult businessFit) && businessFit != null)
            {
                AppendFitSummary(builder, "Build fit", businessFit.fitScore01, businessFit.costMultiplier, businessFit.WarningSummary);
            }

            if (acquisitionMarket.TryGetBusinessConstructionQuoteForOwnedPlotOption(plotId, selectedBusinessActivationIndex, selectedBusinessBuildOptionIndex, out ConstructionInputQuote businessQuote, out string businessQuoteMessage))
            {
                AppendConstructionQuote(builder, businessQuote);
            }
            else
            {
                builder.AppendLine("Cost unavailable | Blocked");
                builder.AppendLine($"Missing: {CompactReason(businessQuoteMessage)}");
                AppendBlockerNextStep(builder, businessQuoteMessage);
            }

            AppendExpansionReadiness(
                builder,
                CreateExpansionReadinessService().BuildForOwnedLand(
                    plotId,
                    selectedBusinessActivationIndex,
                    selectedBusinessBuildOptionIndex));
            AppendSelectedPropertyActionRead(
                builder,
                "Build Shell",
                "ready to build.",
                "Press Build Shell, or cycle the business and shell.");
            return builder.ToString();
        }

        private OwnedPlotBuildabilityState FindBuildabilityState(int plotId)
        {
            if (acquisitionMarket == null)
            {
                return null;
            }

            IReadOnlyList<OwnedPlotBuildabilityState> states = acquisitionMarket.GetOwnedBuildablePlots();
            for (int i = 0; i < states.Count; i++)
            {
                if (states[i] != null && states[i].plotId == plotId)
                {
                    return states[i];
                }
            }

            return null;
        }

        private void UpdateFinances()
        {
            if (view == null)
            {
                return;
            }

            view.EnsureFinanceDashboardControls(Application.isPlaying);
            List<FinanceBusinessSnapshot> snapshots = BuildFinanceBusinessSnapshots();
            SetText(view.FinancesSummaryText, BuildFinanceOverviewText(snapshots));
            SetText(view.FinancesDebtPressureText, BuildFinanceDebtPressureText());
            SetText(view.FinancesDistributionText, BuildFinanceDistributionText(snapshots));
            SetText(view.FinancesAffordabilityText, BuildFinanceAffordabilityText(snapshots));
            UpdateFinanceBusinessRows(snapshots);
            UpdateBusinessCashTransferPanel();
            UpdateBankLoanPanel();
        }

        private List<FinanceBusinessSnapshot> BuildFinanceBusinessSnapshots()
        {
            List<FinanceBusinessSnapshot> snapshots = new();
            HashSet<string> seenInstanceIds = new(StringComparer.OrdinalIgnoreCase);
            HashSet<int> seenBuildingIds = new();

            BusinessInstanceState storeBusiness = storeRuntime != null ? storeRuntime.CurrentBusiness : null;
            if (storeBusiness != null)
            {
                AddFinanceSnapshot(
                    snapshots,
                    seenInstanceIds,
                    seenBuildingIds,
                    storeBusiness,
                    true);
            }

            if (sharedBusinessRuntime != null)
            {
                IReadOnlyList<BusinessInstanceState> businesses = sharedBusinessRuntime.Businesses;
                for (int i = 0; i < businesses.Count; i++)
                {
                    BusinessInstanceState business = businesses[i];
                    if (business == null)
                    {
                        continue;
                    }

                    bool isGeneralStore = business == storeBusiness || business.BusinessType == BusinessType.GeneralStore;
                    if (isGeneralStore && storeBusiness != null && business != storeBusiness)
                    {
                        continue;
                    }

                    AddFinanceSnapshot(
                        snapshots,
                        seenInstanceIds,
                        seenBuildingIds,
                        business,
                        isGeneralStore);
                }
            }

            return snapshots;
        }

        private void AddFinanceSnapshot(
            List<FinanceBusinessSnapshot> snapshots,
            HashSet<string> seenInstanceIds,
            HashSet<int> seenBuildingIds,
            BusinessInstanceState business,
            bool isGeneralStore)
        {
            if (business == null
                || business.RuntimeState == null
                || business.Owner == null
                || business.Owner.OwnerKind != BusinessOwnerKind.Player)
            {
                return;
            }

            string instanceId = business.InstanceId;
            if (!string.IsNullOrWhiteSpace(instanceId) && !seenInstanceIds.Add(instanceId))
            {
                return;
            }

            if (business.AssignedBuildingId >= 0 && !seenBuildingIds.Add(business.AssignedBuildingId))
            {
                return;
            }

            int ownedIndex = FindOwnedEntryIndexForBusiness(business, isGeneralStore);
            string label = ResolveFinanceBusinessLabel(business, ownedIndex, isGeneralStore);
            BusinessPortfolioSummary summary = BuildBusinessPortfolioSummary(business);
            BusinessRuntimeState runtime = business.RuntimeState;
            string pressure = BuildFinanceBusinessPressure(summary);
            string action = BuildFinanceBusinessAction(summary, isGeneralStore);

            snapshots.Add(new FinanceBusinessSnapshot(
                business,
                summary,
                ownedIndex,
                label,
                isGeneralStore,
                runtime.LastWeeklyOwnerDistributionCents,
                pressure,
                action));
        }

        private string BuildFinanceOverviewText(IReadOnlyList<FinanceBusinessSnapshot> snapshots)
        {
            int ownerCash = playerPortfolio != null ? playerPortfolio.OwnerCashCents : 0;
            int nextDebtPayment = playerDebtManager != null ? playerDebtManager.ActiveDebtPaymentCents : 0;
            int starving = CountFinanceBusinessesWithHealth(snapshots, "Starving");
            int pressured = CountFinanceBusinessesWithHealth(snapshots, "Pressured");
            bool defaultedDebt = playerDebtManager != null
                && playerDebtManager.ActiveLoan != null
                && playerDebtManager.ActiveLoan.status == LoanStatus.Defaulted;
            string safety = BuildFinanceSafetyLabel(ownerCash, nextDebtPayment, starving, pressured, defaultedDebt);
            int landCount = acquisitionMarket != null ? acquisitionMarket.OwnedLandCount : 0;
            int withdrawable = 0;
            int protectedBusinessCash = 0;
            int businessEquity = 0;
            int businessLiabilities = 0;
            for (int i = 0; i < snapshots.Count; i++)
            {
                withdrawable += snapshots[i].WithdrawableCashCents;
                protectedBusinessCash += snapshots[i].ProtectedReserveCents;
                businessEquity += snapshots[i].Summary.EquityContributionCents;
                businessLiabilities += snapshots[i].Summary.BusinessLiabilityCents;
            }

            StringBuilder builder = new();
            builder.AppendLine(playerPortfolio != null ? playerPortfolio.BuildWealthHeadline() : $"Liquid cash {FormatMoney(ownerCash)}");
            builder.AppendLine($"Safety: {safety}");
            builder.AppendLine($"Holdings: {snapshots.Count} businesses | {landCount} land parcels");
            builder.AppendLine($"Liquid Cash: {FormatMoney(ownerCash)} now | {FormatMoney(Mathf.Max(0, ownerCash - nextDebtPayment))} after next debt");
            builder.AppendLine($"Business cash: {FormatMoney(withdrawable)} transferable | {FormatMoney(protectedBusinessCash)} held in reserve");
            builder.AppendLine($"Business Equity: {FormatMoney(businessEquity)} | Liabilities: {FormatMoney(businessLiabilities)}");
            builder.AppendLine($"Business posture: {starving} starving | {pressured} pressured | {Mathf.Max(0, snapshots.Count - starving - pressured)} steady");
            if (playerPortfolio != null)
            {
                builder.AppendLine(playerPortfolio.BuildWealthBreakdownSummary());
                builder.AppendLine(playerPortfolio.BuildWealthGoalProgressSummary());
            }
            builder.AppendLine(BuildExpansionCashLine());
            return builder.ToString();
        }

        private string BuildFinanceDebtPressureText()
        {
            StringBuilder builder = new();
            if (playerDebtManager == null)
            {
                builder.AppendLine("Debt records unavailable.");
                return builder.ToString();
            }

            int ownerCash = playerPortfolio != null ? playerPortfolio.OwnerCashCents : 0;
            int nextPayment = playerDebtManager.ActiveDebtPaymentCents;
            if (playerDebtManager.HasPendingApplication)
            {
                builder.AppendLine("Loan application is with the bank.");
                builder.AppendLine(playerDebtManager.BuildRequestedAmountText());
                builder.AppendLine(playerDebtManager.BuildLenderStandingText());
                return builder.ToString();
            }

            LoanContract activeLoan = playerDebtManager.ActiveLoan;
            if (activeLoan == null)
            {
                builder.AppendLine("Active loan: none.");
                builder.AppendLine(playerDebtManager.BuildLenderStandingText());
                return builder.ToString();
            }

            string coverage = nextPayment <= 0
                ? "No immediate payment"
                : ownerCash >= nextPayment
                ? $"Covered by owner cash with {FormatMoney(ownerCash - nextPayment)} left"
                : $"Short {FormatMoney(nextPayment - ownerCash)} from owner cash";
            builder.AppendLine($"Status: {activeLoan.status} | Principal {FormatMoney(activeLoan.remainingPrincipalCents)}");
            builder.AppendLine($"Next due: {FormatMoney(nextPayment)} on {activeLoan.nextPaymentDueDate}");
            builder.AppendLine($"Cash coverage: {coverage} | Missed payments {playerDebtManager.ConsecutiveMissedPayments}");
            builder.AppendLine(playerDebtManager.BuildLenderStandingText());
            if (playerPortfolio != null)
            {
                builder.AppendLine(playerPortfolio.BuildLiquidityPressureSummary());
            }
            return builder.ToString();
        }

        private string BuildFinanceDistributionText(IReadOnlyList<FinanceBusinessSnapshot> snapshots)
        {
            StringBuilder builder = new();
            if (playerPortfolio == null)
            {
                builder.AppendLine("Distribution records unavailable.");
                return builder.ToString();
            }

            builder.AppendLine($"Owner draw this week: {FormatMoney(playerPortfolio.LastWeeklyDistributionCents)}");
            builder.AppendLine(playerPortfolio.LastWeeklyDistributionSummary);
            int payingCount = 0;
            for (int i = 0; i < snapshots.Count; i++)
            {
                if (snapshots[i].LastDistributionCents > 0)
                {
                    payingCount++;
                }
            }

            builder.AppendLine($"Businesses producing draw: {payingCount}/{snapshots.Count}");
            return builder.ToString();
        }

        private string BuildFinanceAffordabilityText(IReadOnlyList<FinanceBusinessSnapshot> snapshots)
        {
            int ownerCash = playerPortfolio != null ? playerPortfolio.OwnerCashCents : 0;
            int nextDebtPayment = playerDebtManager != null ? playerDebtManager.ActiveDebtPaymentCents : 0;
            int withdrawable = 0;
            for (int i = 0; i < snapshots.Count; i++)
            {
                withdrawable += snapshots[i].WithdrawableCashCents;
            }

            StringBuilder builder = new();
            builder.AppendLine(playerPortfolio != null ? playerPortfolio.BuildWealthHeadline() : $"Liquid cash now: {FormatMoney(ownerCash)}");
            builder.AppendLine($"After next debt payment: {FormatMoney(Mathf.Max(0, ownerCash - nextDebtPayment))}");
            builder.AppendLine($"Business surplus above reserve: {FormatMoney(withdrawable)}");
            if (playerPortfolio != null)
            {
                builder.AppendLine(playerPortfolio.BuildLiquidityPressureSummary());
            }
            builder.AppendLine("Land, projects, and debt draw from Liquid Cash first.");
            return builder.ToString();
        }

        private void UpdateFinanceBusinessRows(IReadOnlyList<FinanceBusinessSnapshot> snapshots)
        {
            if (view == null || view.BusinessBreakdownContent == null || view.FinanceBusinessRowTemplate == null)
            {
                return;
            }

            string signature = BuildFinanceBusinessSignature(snapshots);
            if (signature == financeBusinessSignature && financeBusinessButtons.Count == snapshots.Count)
            {
                return;
            }

            ClearFinanceRows();
            financeBusinessSignature = signature;
            view.FinanceBusinessRowTemplate.gameObject.SetActive(false);

            for (int i = 0; i < snapshots.Count; i++)
            {
                FinanceBusinessSnapshot snapshot = snapshots[i];
                int ownedIndex = snapshot.OwnedEntryIndex;
                Button row = Instantiate(view.FinanceBusinessRowTemplate, view.BusinessBreakdownContent);
                row.name = $"FinanceBusinessRow_{i:00}_{SanitizeName(snapshot.Label)}";
                row.gameObject.SetActive(true);
                view.ConfigureFinanceRowButton(row, BuildFinanceBusinessRowText(snapshot));
                row.interactable = ownedIndex >= 0;
                row.onClick.AddListener(() => SelectFinanceBusiness(ownedIndex));
                UITooltipRegistry.Attach(row, "Select Business", () => $"Open {snapshot.Label} details and cash actions.");
                financeBusinessButtons.Add(row);
                spawnedFinanceRows.Add(row.gameObject);
            }
        }

        private string BuildFinanceBusinessSignature(IReadOnlyList<FinanceBusinessSnapshot> snapshots)
        {
            StringBuilder builder = new();
            for (int i = 0; i < snapshots.Count; i++)
            {
                FinanceBusinessSnapshot snapshot = snapshots[i];
                builder.Append(snapshot.Label).Append('|')
                    .Append(snapshot.OwnedEntryIndex).Append('|')
                    .Append(snapshot.CurrentCashCents).Append('|')
                    .Append(snapshot.Summary.OperatingReserveCents).Append('|')
                    .Append(snapshot.ProtectedReserveCents).Append('|')
                    .Append(snapshot.WithdrawableCashCents).Append('|')
                    .Append(snapshot.Summary.EquityContributionCents).Append('|')
                    .Append(snapshot.Summary.BusinessLiabilityCents).Append('|')
                    .Append(snapshot.Summary.ContinuityStatus).Append('|')
                    .Append(snapshot.LastDistributionCents).Append('|')
                    .Append(snapshot.Health).Append('|')
                    .Append(snapshot.Pressure).Append('|');
                BusinessRuntimeState runtime = snapshot.Business != null ? snapshot.Business.RuntimeState : null;
                if (runtime != null)
                {
                    builder.Append(runtime.LastDailyNetCents).Append('|')
                        .Append(runtime.WeekToDateNetCents).Append('|')
                        .Append(runtime.MonthToDateNetCents).Append('|')
                        .Append(runtime.YearToDateNetCents).Append('|')
                        .Append(runtime.AllTimeNetCents);
                }

                builder.Append(';');
            }

            return builder.ToString();
        }

        private string BuildFinanceBusinessRowText(FinanceBusinessSnapshot snapshot)
        {
            string net = snapshot.Business != null ? $"\n{BuildBusinessNetCompactReadout(snapshot.Business.RuntimeState)}" : string.Empty;
            return $"{snapshot.Label} | {snapshot.Health}\nBusiness Cash {FormatMoney(snapshot.CurrentCashCents)} | Reserve {FormatMoney(snapshot.ProtectedReserveCents)} | Transferable {FormatMoney(snapshot.WithdrawableCashCents)} | Equity {FormatMoney(snapshot.Summary.EquityContributionCents)} | Liabilities {FormatMoney(snapshot.Summary.BusinessLiabilityCents)} | Last draw {FormatMoney(snapshot.LastDistributionCents)}{net}\nContinuity: {snapshot.Summary.ContinuitySummary}\nPressure: {snapshot.Pressure}\nNext: {snapshot.Action}";
        }

        private void SelectFinanceBusiness(int ownedIndex)
        {
            if (ownedIndex < 0 || ownedIndex >= ownedEntries.Count)
            {
                panelStatus = "Business detail unavailable.";
                UpdateFinances();
                return;
            }

            selectedOwnedIndex = ownedIndex;
            selectedBusinessActivationBuildingId = -1;
            panelStatus = $"Selected {ownedEntries[ownedIndex].Label}. Cash actions stay in this business ledger.";
            currentTab = ManagementPanelTab.Properties;
            Refresh();
        }

        private int FindOwnedEntryIndexForBusiness(BusinessInstanceState business, bool isGeneralStore)
        {
            if (business == null)
            {
                return -1;
            }

            for (int i = 0; i < ownedEntries.Count; i++)
            {
                OwnedEntry entry = ownedEntries[i];
                if (isGeneralStore && entry.Kind == OwnedKind.Store)
                {
                    return i;
                }

                if (entry.Kind == OwnedKind.Business && entry.BuildingId == business.AssignedBuildingId)
                {
                    return i;
                }
            }

            return -1;
        }

        private string ResolveFinanceBusinessLabel(BusinessInstanceState business, int ownedIndex, bool isGeneralStore)
        {
            if (ownedIndex >= 0 && ownedIndex < ownedEntries.Count && !string.IsNullOrWhiteSpace(ownedEntries[ownedIndex].Label))
            {
                return ownedEntries[ownedIndex].Label;
            }

            if (business != null && !string.IsNullOrWhiteSpace(business.RuntimeDisplayName))
            {
                return business.RuntimeDisplayName;
            }

            return isGeneralStore ? "General Store" : "Business";
        }

        private BusinessPortfolioSummary BuildBusinessPortfolioSummary(BusinessInstanceState business)
        {
            if (business == null)
            {
                return default;
            }

            if (storeRuntime != null && business == storeRuntime.CurrentBusiness)
            {
                return business.BuildPortfolioSummary(
                    storeRuntime.OperatingBusinessCashReserveCents,
                    storeRuntime.ProtectedBusinessCashReserveCents);
            }

            return business.BuildPortfolioSummary(
                SharedBusinessRuntimeManager.CalculateSharedOperatingCashReserveCents(business),
                SharedBusinessRuntimeManager.CalculateSharedSurvivalCashReserveCents(business));
        }

        private static int CountFinanceBusinessesWithHealth(IReadOnlyList<FinanceBusinessSnapshot> snapshots, string health)
        {
            if (snapshots == null || string.IsNullOrWhiteSpace(health))
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < snapshots.Count; i++)
            {
                if (string.Equals(snapshots[i].Health, health, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CalculateWithdrawableBusinessCashCents(int currentCashCents, int protectedReserveCents)
        {
            return Mathf.Max(0, Mathf.Max(0, currentCashCents) - Mathf.Max(0, protectedReserveCents));
        }

        private static string BuildFinanceSafetyLabel(
            int ownerCashCents,
            int nextDebtPaymentCents,
            int starvingBusinessCount,
            int pressuredBusinessCount,
            bool defaultedDebt)
        {
            if (defaultedDebt || starvingBusinessCount > 0 || nextDebtPaymentCents > ownerCashCents)
            {
                return "Unsafe";
            }

            if (pressuredBusinessCount > 0 || (nextDebtPaymentCents > 0 && nextDebtPaymentCents >= ownerCashCents / 2))
            {
                return "Tight";
            }

            return "Safe";
        }

        private static string ClassifyFinanceBusinessHealth(
            int currentCashCents,
            int protectedReserveCents,
            int filledWorkerCount,
            int targetWorkerCount,
            float operatingHealth01,
            string blockedReason,
            IReadOnlyList<string> suspendedPayrollWorkerIds)
        {
            int cash = Mathf.Max(0, currentCashCents);
            int reserve = Mathf.Max(0, protectedReserveCents);
            bool cashBlocked = ContainsCashPressure(blockedReason);
            bool missedPayroll = suspendedPayrollWorkerIds != null && suspendedPayrollWorkerIds.Count > 0;
            if (cash <= 0 || (reserve > 0 && cash < reserve) || cashBlocked || missedPayroll)
            {
                return "Starving";
            }

            bool understaffed = targetWorkerCount > 0 && filledWorkerCount < targetWorkerCount;
            bool lowOperatingHealth = operatingHealth01 < 0.5f;
            bool nearReserve = reserve > 0 && cash < reserve * 2;
            if (understaffed || lowOperatingHealth || nearReserve || !string.IsNullOrWhiteSpace(blockedReason))
            {
                return "Pressured";
            }

            bool strongCash = reserve <= 0 || cash >= reserve * 3;
            bool strongOperatingHealth = operatingHealth01 >= 0.75f;
            bool fullyStaffed = targetWorkerCount <= 0 || filledWorkerCount >= targetWorkerCount;
            return strongCash && strongOperatingHealth && fullyStaffed ? "Healthy" : "Stable";
        }

        private static string BuildFinanceBusinessPressure(
            string health,
            int currentCashCents,
            int protectedReserveCents,
            int filledWorkerCount,
            int targetWorkerCount,
            float operatingHealth01,
            string blockedReason)
        {
            if (ContainsCashPressure(blockedReason))
            {
                return $"Cash-limited: {CompactReason(blockedReason)}";
            }

            int cash = Mathf.Max(0, currentCashCents);
            int reserve = Mathf.Max(0, protectedReserveCents);
            if (cash <= 0)
            {
                return "Cash is empty";
            }

            if (reserve > 0 && cash < reserve)
            {
                return $"Below reserve by {FormatMoney(reserve - cash)}";
            }

            if (targetWorkerCount > 0 && filledWorkerCount < targetWorkerCount)
            {
                return $"Staff short {targetWorkerCount - filledWorkerCount}";
            }

            if (operatingHealth01 < 0.5f)
            {
                return $"Operating health {Mathf.RoundToInt(Mathf.Clamp01(operatingHealth01) * 100f)}%";
            }

            if (!string.IsNullOrWhiteSpace(blockedReason))
            {
                return CompactReason(blockedReason);
            }

            return string.Equals(health, "Healthy", StringComparison.OrdinalIgnoreCase)
                ? "Healthy cash and operations"
                : "No urgent pressure";
        }

        private static string BuildFinanceBusinessPressure(BusinessPortfolioSummary summary)
        {
            if (summary.ContinuityStatus == BusinessContinuityStatus.CashBelowSurvivalReserve)
            {
                return summary.ContinuitySummary;
            }

            if (summary.ContinuityStatus == BusinessContinuityStatus.MissedPayroll)
            {
                return "Missed payroll exposure";
            }

            if (summary.ContinuityStatus == BusinessContinuityStatus.MissingRequiredRoles
                || summary.ContinuityStatus == BusinessContinuityStatus.OwnerOperatorFallback
                || summary.ContinuityStatus == BusinessContinuityStatus.Understaffed)
            {
                return summary.RoleCoverage.BuildSummary();
            }

            if (summary.ContinuityStatus == BusinessContinuityStatus.Blocked)
            {
                return summary.ContinuitySummary;
            }

            if (summary.BusinessLiabilityCents > 0)
            {
                return $"Liabilities outstanding {FormatMoney(summary.BusinessLiabilityCents)}";
            }

            return string.Equals(summary.FinanceHealthLabel, "Healthy", StringComparison.OrdinalIgnoreCase)
                ? "Healthy cash and operations"
                : "No urgent pressure";
        }

        private static string BuildFinanceBusinessAction(string health, bool isGeneralStore, string blockedReason)
        {
            if (string.Equals(health, "Starving", StringComparison.OrdinalIgnoreCase))
            {
                return "Open details and deposit owner cash";
            }

            if (ContainsCashPressure(blockedReason))
            {
                return "Raise cash before next operation";
            }

            if (string.Equals(health, "Pressured", StringComparison.OrdinalIgnoreCase))
            {
                return isGeneralStore ? "Check stock, staffing, and cash" : "Check staffing, policy, and cash";
            }

            if (string.Equals(health, "Healthy", StringComparison.OrdinalIgnoreCase))
            {
                return "Can support distributions or surplus withdrawal";
            }

            return "Keep reserves funded";
        }

        private static string BuildFinanceBusinessAction(BusinessPortfolioSummary summary, bool isGeneralStore)
        {
            if (summary.ContinuityStatus == BusinessContinuityStatus.CashBelowSurvivalReserve
                || summary.ContinuityStatus == BusinessContinuityStatus.MissedPayroll)
            {
                return "Open details and deposit owner cash";
            }

            if (summary.ContinuityStatus == BusinessContinuityStatus.Blocked)
            {
                return "Resolve the blocked operating step";
            }

            if (summary.ContinuityStatus == BusinessContinuityStatus.MissingRequiredRoles
                || summary.ContinuityStatus == BusinessContinuityStatus.OwnerOperatorFallback
                || summary.ContinuityStatus == BusinessContinuityStatus.Understaffed
                || summary.ContinuityStatus == BusinessContinuityStatus.Pressured)
            {
                return isGeneralStore ? "Check stock, staffing, and cash" : "Check staffing, policy, and cash";
            }

            if (summary.TransferableCashCents > 0 && summary.BusinessLiabilityCents <= 0)
            {
                return "Can support distributions or surplus withdrawal";
            }

            return "Keep reserves funded";
        }

        private static bool ContainsCashPressure(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf("cash", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void UpdateBusinessCashTransferPanel()
        {
            if (view == null)
            {
                return;
            }

            UpdatePanelStatusLine();
            view.EnsureBusinessCashTransferControls(Application.isPlaying);
            if (view.CashTransferRoot == null)
            {
                return;
            }

            if (!IsActiveOwnedBusinessTabContext())
            {
                view.CashTransferRoot.gameObject.SetActive(false);
                return;
            }

            bool hasBusiness = TryGetSelectedOwnedBusinessForCashTransfer(out BusinessInstanceState business, out int protectedReserveCents, out _);
            view.CashTransferRoot.gameObject.SetActive(hasBusiness);
            if (!hasBusiness || business == null || business.RuntimeState == null)
            {
                return;
            }

            BusinessCashTransferRuleState rule = business.CashTransferRule;
            string businessName = GetSelectedBusinessCashTransferLabel(business);
            int ownerCash = playerPortfolio != null ? playerPortfolio.OwnerCashCents : 0;
            int businessCash = business.RuntimeState.CurrentCashCents;
            int lower = rule.GetEffectiveLowerThresholdCents(protectedReserveCents);
            int upper = rule.GetEffectiveUpperThresholdCents(protectedReserveCents);
            int withdrawable = Mathf.Max(0, businessCash - Mathf.Max(0, protectedReserveCents));

            SetText(view.CashTransferTitleText, $"{businessName} Cash Transfers");
            SetText(view.CashTransferHelpText, $"<b>Store Cash Position</b>\nStore Cash            {FormatMoney(businessCash)}\nOperating Reserve     {FormatMoney(protectedReserveCents)}\nAvailable to Transfer {FormatMoney(withdrawable)}");
            SetText(view.CashTransferThresholdText, $"<b>Reserve Rules</b>\nAuto Reserve {(rule.AutoTransferEnabled ? "On" : "Off")}\nRefill Below {FormatMoney(lower)}\nSweep Above {FormatMoney(upper)}");
            SetText(view.CashTransferStatusText, $"Last Transfer: {rule.LastTransferSummary}");

            SetButtonLabel(view.CashTransferAutoToggleButton, rule.AutoTransferEnabled ? "Auto Reserve On" : "Auto Reserve Off");
            SetButtonLabel(view.CashTransferDepositOneButton, "+$1");
            SetButtonLabel(view.CashTransferDepositTenButton, "+$10");
            SetButtonLabel(view.CashTransferDepositHundredButton, "+$100");
            SetButtonLabel(view.CashTransferDepositExactButton, "Add");
            SetButtonLabel(view.CashTransferWithdrawOneButton, "-$1");
            SetButtonLabel(view.CashTransferWithdrawTenButton, "-$10");
            SetButtonLabel(view.CashTransferWithdrawHundredButton, "-$100");
            SetButtonLabel(view.CashTransferWithdrawExactButton, "Pull");
            SetButtonLabel(view.CashTransferApplyReserveButton, "Apply");
            SetInputPlaceholder(view.CashTransferDepositInput, "Deposit");
            SetInputPlaceholder(view.CashTransferWithdrawInput, "Withdraw");
            SetInputTextIfNotFocused(view.CashTransferLowerThresholdInput, FormatMoney(lower));
            SetInputTextIfNotFocused(view.CashTransferUpperThresholdInput, FormatMoney(upper));
            SetButtonInteractable(view.CashTransferDepositOneButton, playerPortfolio != null && ownerCash >= 100);
            SetButtonInteractable(view.CashTransferDepositTenButton, playerPortfolio != null && ownerCash >= 1000);
            SetButtonInteractable(view.CashTransferDepositHundredButton, playerPortfolio != null && ownerCash >= 10000);
            SetButtonInteractable(view.CashTransferDepositExactButton, playerPortfolio != null && ownerCash > 0);
            SetButtonInteractable(view.CashTransferWithdrawOneButton, playerPortfolio != null && withdrawable >= 100);
            SetButtonInteractable(view.CashTransferWithdrawTenButton, playerPortfolio != null && withdrawable >= 1000);
            SetButtonInteractable(view.CashTransferWithdrawHundredButton, playerPortfolio != null && withdrawable >= 10000);
            SetButtonInteractable(view.CashTransferWithdrawExactButton, playerPortfolio != null && withdrawable > 0);
            SetButtonInteractable(view.CashTransferAutoToggleButton, playerPortfolio != null);
            SetButtonInteractable(view.CashTransferLowerMinusTenButton, playerPortfolio != null && lower > protectedReserveCents);
            SetButtonInteractable(view.CashTransferLowerPlusTenButton, playerPortfolio != null && lower + 1000 < upper);
            SetButtonInteractable(view.CashTransferUpperMinusTenButton, playerPortfolio != null && upper - 1000 > lower + BusinessCashTransferRuleState.MinimumThresholdGapCents);
            SetButtonInteractable(view.CashTransferUpperPlusTenButton, playerPortfolio != null);
            SetButtonInteractable(view.CashTransferApplyReserveButton, playerPortfolio != null);
        }

        private string GetSelectedBusinessCashTransferLabel(BusinessInstanceState business)
        {
            if (TryGetSelectedOwnedEntry(out OwnedEntry selected)
                && (selected.Kind == OwnedKind.Store || selected.Kind == OwnedKind.Business)
                && !string.IsNullOrWhiteSpace(selected.Label))
            {
                return selected.Label;
            }

            return business != null && !string.IsNullOrWhiteSpace(business.RuntimeDisplayName)
                ? business.RuntimeDisplayName
                : "Business";
        }

        private void UpdateBankLoanPanel()
        {
            if (view == null)
            {
                return;
            }

            UpdatePanelStatusLine();
            view.EnsureBankLoanControls(Application.isPlaying);
            SetButtonLabel(view.LoanIncreaseOneButton, "+1");
            SetButtonLabel(view.LoanIncreaseTenButton, "+10");
            SetButtonLabel(view.LoanIncreaseHundredButton, "+100");
            SetButtonLabel(view.LoanDecreaseOneButton, "-1");
            SetButtonLabel(view.LoanDecreaseTenButton, "-10");
            SetButtonLabel(view.LoanDecreaseHundredButton, "-100");
            SetButtonLabel(view.LoanSubmitButton, "Submit Application");

            if (playerDebtManager == null)
            {
                SetText(view.BankLoanTitleText, "Bank Loan Desk");
                SetText(view.BankLoanHelpText, "Set the request, check payment pressure, then send it to the bank.");
                SetText(view.BankLoanRequestedAmountText, "Requested amount: unavailable");
                SetText(view.BankLoanEstimatedPaymentText, "Estimated payment: unavailable");
                SetText(view.BankLoanPaymentScheduleText, "Payment schedule: unavailable");
                SetText(view.BankLoanEstimatedTotalText, "Estimated total owed: unavailable");
                SetText(view.BankLoanLenderStandingText, "Lender standing: unavailable");
                SetText(view.BankLoanStatusText, "Bank loan records unavailable.");
                SetText(view.BankLoanActiveLoanText, string.Empty);
                SetBankLoanAmountButtons(false);
                SetButtonLabel(view.LoanWorkflowButton, "Open Loan Application");
                SetButtonInteractable(view.LoanWorkflowButton, false);
                SetButtonInteractable(view.LoanSubmitButton, false);
                return;
            }

            string title = "Bank Loan Desk";
            if (playerDebtManager.HasPendingApplication)
            {
                title = "Loan Application Submitted";
            }
            else if (playerDebtManager.ActiveLoan != null && playerDebtManager.ActiveLoan.status == LoanStatus.Defaulted)
            {
                title = "Loan Defaulted";
            }
            else if (playerDebtManager.ActiveLoan != null && playerDebtManager.ActiveLoan.status == LoanStatus.PaidOff)
            {
                title = "Loan Paid Off";
            }
            else if (playerDebtManager.ActiveLoan != null && playerDebtManager.ActiveLoan.status == LoanStatus.Recovered)
            {
                title = "Loan Resolved";
            }
            else if (playerDebtManager.Application != null && playerDebtManager.Application.status == BankLoanApplicationStatus.Approved)
            {
                title = "Loan Approved";
            }
            else if (playerDebtManager.Application != null && playerDebtManager.Application.status == BankLoanApplicationStatus.Declined)
            {
                title = "Loan Declined";
            }

            SetText(view.BankLoanTitleText, title);
            SetText(view.BankLoanHelpText, playerDebtManager.HasPendingApplication
                ? "The bank has the request. Return tomorrow for a decision."
                : "Set the request, check payment pressure, then send it to the bank.");
            SetText(view.BankLoanRequestedAmountText, playerDebtManager.BuildRequestedAmountText());
            SetText(view.BankLoanEstimatedPaymentText, playerDebtManager.BuildEstimatedPaymentText());
            SetText(view.BankLoanPaymentScheduleText, playerDebtManager.BuildPaymentScheduleText());
            SetText(view.BankLoanEstimatedTotalText, playerDebtManager.BuildEstimatedTotalOwedText());
            SetText(view.BankLoanLenderStandingText, playerDebtManager.BuildLenderStandingText());
            SetText(view.BankLoanStatusText, playerDebtManager.BuildPanelStatusText());
            SetText(view.BankLoanActiveLoanText, playerDebtManager.BuildActiveLoanText());
            SetBankLoanAmountButtons(playerDebtManager.CanEditRequestedAmount);
            SetButtonLabel(view.LoanWorkflowButton, "Open Loan Application");
            SetButtonInteractable(view.LoanWorkflowButton, true);
            SetButtonInteractable(view.LoanSubmitButton, playerDebtManager.CanSubmitApplication);
        }

        private void AdjustLoanRequest(int deltaDollars)
        {
            if (playerDebtManager == null)
            {
                panelStatus = "Bank loan records unavailable.";
                UpdateBankLoanPanel();
                return;
            }

            playerDebtManager.AdjustRequestedAmountDollars(deltaDollars);
            panelStatus = playerDebtManager.BuildRequestedAmountText();
            UpdateBankLoanPanel();
        }

        private void SubmitLoanApplication()
        {
            if (playerDebtManager == null)
            {
                panelStatus = "Bank loan records unavailable.";
                LLFeedbackService.Play(LLFeedbackKind.Warning, "bank loan submit blocked", canvas);
                UpdateBankLoanPanel();
                return;
            }

            playerDebtManager.SubmitApplication(out string message);
            panelStatus = message;
            LLFeedbackService.Play(
                playerDebtManager.HasPendingApplication ? LLFeedbackKind.StampApproval : LLFeedbackKind.Warning,
                playerDebtManager.HasPendingApplication ? "bank loan application submit" : "bank loan application submit blocked",
                canvas);
            UpdateBankLoanPanel();
        }

        private void DepositExactBusinessCash()
        {
            if (!TryParseMoneyInputToCents(view != null ? view.CashTransferDepositInput?.text : null, out int requestedCents))
            {
                panelStatus = "Blocked: enter a valid deposit amount above $0.00.";
                UpdateBusinessCashTransferPanel();
                return;
            }

            if (!TryGetSelectedOwnedBusinessForCashTransfer(out BusinessInstanceState business, out int protectedReserveCents, out string message))
            {
                panelStatus = message;
                UpdateBusinessCashTransferPanel();
                return;
            }

            if (playerPortfolio == null)
            {
                panelStatus = "Owner cash unavailable.";
                UpdateBusinessCashTransferPanel();
                return;
            }

            int amount = Mathf.Min(requestedCents, Mathf.Max(0, playerPortfolio.OwnerCashCents));
            if (amount <= 0)
            {
                panelStatus = "Blocked: no owner cash available for deposit.";
                UpdateBusinessCashTransferPanel();
                return;
            }

            playerPortfolio.TryTransferOwnerBusinessCash(business, amount, protectedReserveCents, out message);
            panelStatus = message;
            LLFeedbackService.Play(ResolveMoneyFeedbackKind(amount), "business cash deposit", canvas);
            view?.CashTransferDepositInput?.SetTextWithoutNotify(string.Empty);
            UpdateBusinessCashTransferPanel();
        }

        private void WithdrawExactBusinessCash()
        {
            if (!TryParseMoneyInputToCents(view != null ? view.CashTransferWithdrawInput?.text : null, out int requestedCents))
            {
                panelStatus = "Blocked: enter a valid withdrawal amount above $0.00.";
                UpdateBusinessCashTransferPanel();
                return;
            }

            if (!TryGetSelectedOwnedBusinessForCashTransfer(out BusinessInstanceState business, out int protectedReserveCents, out string message))
            {
                panelStatus = message;
                UpdateBusinessCashTransferPanel();
                return;
            }

            if (playerPortfolio == null)
            {
                panelStatus = "Owner cash unavailable.";
                UpdateBusinessCashTransferPanel();
                return;
            }

            int available = Mathf.Max(0, business.RuntimeState.CurrentCashCents - Mathf.Max(0, protectedReserveCents));
            int amount = Mathf.Min(requestedCents, available);
            if (amount <= 0)
            {
                panelStatus = "Blocked: no store cash is available above the operating reserve.";
                UpdateBusinessCashTransferPanel();
                return;
            }

            playerPortfolio.TryTransferOwnerBusinessCash(business, -amount, protectedReserveCents, out message);
            panelStatus = message;
            LLFeedbackService.Play(ResolveMoneyFeedbackKind(amount), "business cash withdrawal", canvas);
            view?.CashTransferWithdrawInput?.SetTextWithoutNotify(string.Empty);
            UpdateBusinessCashTransferPanel();
        }

        private void ApplyExactBusinessCashThresholds()
        {
            if (!TryGetSelectedOwnedBusinessForCashTransfer(out BusinessInstanceState business, out int protectedReserveCents, out string message))
            {
                panelStatus = message;
                UpdateBusinessCashTransferPanel();
                return;
            }

            if (playerPortfolio == null)
            {
                panelStatus = "Owner cash unavailable.";
                UpdateBusinessCashTransferPanel();
                return;
            }

            if (!TryParseMoneyInputToCents(view != null ? view.CashTransferLowerThresholdInput?.text : null, out int lowerCents)
                || !TryParseMoneyInputToCents(view != null ? view.CashTransferUpperThresholdInput?.text : null, out int upperCents))
            {
                panelStatus = "Blocked: enter valid reserve thresholds above $0.00.";
                UpdateBusinessCashTransferPanel();
                return;
            }

            playerPortfolio.TrySetBusinessCashThresholds(business, lowerCents, upperCents, protectedReserveCents, out message);
            message = ResolveSelectedBusinessAutoCashTransferIfNeeded(business, protectedReserveCents, message);
            panelStatus = message;
            LLFeedbackService.Play(LLFeedbackKind.UIConfirm, "business cash reserve thresholds apply", canvas);
            UpdateBusinessCashTransferPanel();
        }

        private void TransferSelectedBusinessCash(int signedDollars)
        {
            if (!TryGetSelectedOwnedBusinessForCashTransfer(out BusinessInstanceState business, out int protectedReserveCents, out string message))
            {
                panelStatus = message;
                UpdateBusinessCashTransferPanel();
                return;
            }

            if (playerPortfolio == null)
            {
                panelStatus = "Owner cash unavailable.";
                UpdateBusinessCashTransferPanel();
                return;
            }

            playerPortfolio.TryTransferOwnerBusinessCash(
                business,
                signedDollars * 100,
                protectedReserveCents,
                out message);
            panelStatus = message;
            LLFeedbackService.Play(ResolveMoneyFeedbackKind(Mathf.Abs(signedDollars) * 100), "business cash quick transfer", canvas);
            UpdateBusinessCashTransferPanel();
        }

        private static bool TryParseMoneyInputToCents(string input, out int cents)
        {
            cents = 0;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string sanitized = input.Trim()
                .Replace("$", string.Empty, StringComparison.Ordinal)
                .Replace(",", string.Empty, StringComparison.Ordinal);
            if (!decimal.TryParse(sanitized, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal dollars)
                || dollars <= 0m)
            {
                return false;
            }

            decimal centsValue = decimal.Round(dollars * 100m, 0, MidpointRounding.AwayFromZero);
            if (centsValue <= 0m || centsValue > int.MaxValue)
            {
                return false;
            }

            cents = (int)centsValue;
            return true;
        }

        private static string ResolveCashTransferSubmitActionName(string inputObjectName)
        {
            return inputObjectName switch
            {
                "BusinessCashTransfer_DepositInput" => nameof(DepositExactBusinessCash),
                "BusinessCashTransfer_WithdrawInput" => nameof(WithdrawExactBusinessCash),
                "BusinessCashTransfer_LowerThresholdInput" => nameof(ApplyExactBusinessCashThresholds),
                "BusinessCashTransfer_UpperThresholdInput" => nameof(ApplyExactBusinessCashThresholds),
                _ => string.Empty
            };
        }

        private void ToggleSelectedBusinessAutoTransfer()
        {
            if (!TryGetSelectedOwnedBusinessForCashTransfer(out BusinessInstanceState business, out int protectedReserveCents, out string message))
            {
                panelStatus = message;
                UpdateBusinessCashTransferPanel();
                return;
            }

            if (playerPortfolio == null)
            {
                panelStatus = "Owner cash unavailable.";
                UpdateBusinessCashTransferPanel();
                return;
            }

            bool enabled = !business.CashTransferRule.AutoTransferEnabled;
            playerPortfolio.TrySetBusinessAutoTransferEnabled(business, enabled, protectedReserveCents, out message);
            message = ResolveSelectedBusinessAutoCashTransferIfNeeded(business, protectedReserveCents, message);
            panelStatus = message;
            LLFeedbackService.Play(LLFeedbackKind.UIConfirm, "business cash auto reserve toggle", canvas);
            UpdateBusinessCashTransferPanel();
        }

        private void AdjustSelectedBusinessCashThreshold(bool lowerThreshold, int deltaDollars)
        {
            if (!TryGetSelectedOwnedBusinessForCashTransfer(out BusinessInstanceState business, out int protectedReserveCents, out string message))
            {
                panelStatus = message;
                UpdateBusinessCashTransferPanel();
                return;
            }

            if (playerPortfolio == null)
            {
                panelStatus = "Owner cash unavailable.";
                UpdateBusinessCashTransferPanel();
                return;
            }

            int deltaCents = deltaDollars * 100;
            bool changed = lowerThreshold
                ? playerPortfolio.TryAdjustBusinessCashLowerThreshold(business, deltaCents, protectedReserveCents, out message)
                : playerPortfolio.TryAdjustBusinessCashUpperThreshold(business, deltaCents, protectedReserveCents, out message);
            if (changed)
            {
                message = ResolveSelectedBusinessAutoCashTransferIfNeeded(business, protectedReserveCents, message);
            }

            panelStatus = changed ? message : CompactReason(message);
            LLFeedbackService.Play(changed ? LLFeedbackKind.UIConfirm : LLFeedbackKind.Warning, "business cash reserve threshold adjust", canvas);
            UpdateBusinessCashTransferPanel();
        }

        private string ResolveSelectedBusinessAutoCashTransferIfNeeded(BusinessInstanceState business, int protectedReserveCents, string fallbackMessage)
        {
            if (playerPortfolio == null || business == null || business.RuntimeState == null || !business.CashTransferRule.AutoTransferEnabled)
            {
                return fallbackMessage;
            }

            const int immediateUiWeekKey = 0;
            if (playerPortfolio.TryResolveAutomaticBusinessCashTransfer(
                    business,
                    protectedReserveCents,
                    immediateUiWeekKey,
                    BusinessCashAutoTransferMode.LowerOnly,
                    out _,
                    out string lowerMessage))
            {
                return lowerMessage;
            }

            if (playerPortfolio.TryResolveAutomaticBusinessCashTransfer(
                    business,
                    protectedReserveCents,
                    immediateUiWeekKey,
                    BusinessCashAutoTransferMode.UpperOnly,
                    out _,
                    out string upperMessage))
            {
                return upperMessage;
            }

            return fallbackMessage;
        }

        private bool TryGetSelectedOwnedBusinessForCashTransfer(
            out BusinessInstanceState business,
            out int protectedReserveCents,
            out string message)
        {
            business = null;
            protectedReserveCents = 0;
            if (!IsActiveOwnedBusinessTabContext())
            {
                message = "Open a player-owned business tab to use cash transfers.";
                return false;
            }

            if (!TryGetSelectedOwnedEntry(out OwnedEntry selected))
            {
                message = "Select a player-owned business.";
                return false;
            }

            if (selected.Kind == OwnedKind.Store)
            {
                business = storeRuntime != null ? storeRuntime.CurrentBusiness : null;
                protectedReserveCents = storeRuntime != null ? storeRuntime.ProtectedBusinessCashReserveCents : 0;
            }
            else if (selected.Kind == OwnedKind.Business)
            {
                business = GetBusinessForBuilding(selected.BuildingId);
                protectedReserveCents = SharedBusinessRuntimeManager.CalculateSharedSurvivalCashReserveCents(business);
            }

            if (business == null || business.RuntimeState == null)
            {
                message = "Select an active player-owned business.";
                return false;
            }

            if (business.Owner == null || business.Owner.OwnerKind != BusinessOwnerKind.Player)
            {
                message = "Selected business is not player-owned.";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private bool IsActiveOwnedBusinessTabContext()
        {
            if (!visible || currentTab != ManagementPanelTab.Properties)
            {
                return false;
            }

            if (!TryGetSelectedOwnedEntry(out OwnedEntry selected))
            {
                return false;
            }

            return selected.Kind == OwnedKind.Store || selected.Kind == OwnedKind.Business;
        }

        private void SetBankLoanAmountButtons(bool interactable)
        {
            SetButtonInteractable(view.LoanIncreaseOneButton, interactable);
            SetButtonInteractable(view.LoanIncreaseTenButton, interactable);
            SetButtonInteractable(view.LoanIncreaseHundredButton, interactable);
            SetButtonInteractable(view.LoanDecreaseOneButton, interactable);
            SetButtonInteractable(view.LoanDecreaseTenButton, interactable);
            SetButtonInteractable(view.LoanDecreaseHundredButton, interactable);
        }

        private void UpdateAcquisitions()
        {
            view.SetAcquisitionSectionActive(acquisitionSection == AcquisitionMarketSection.Land);

            if (acquisitionMarket == null)
            {
                SetText(view.AcquisitionForSaleText, "Acquisitions unavailable.");
                SetText(view.AcquisitionOffMarketText, "Quiet leads unavailable.");
                SetText(view.AcquisitionSelectedLeadText, string.Empty);
                SetText(view.AcquisitionHistoryText, string.Empty);
                return;
            }

            bool hasListing = acquisitionMarket.TryGetSelectedListing(acquisitionSection, out _);
            SetText(view.AcquisitionForSaleText, BuildAcquisitionMarketSnapshotText());
            SetText(view.AcquisitionOffMarketText, BuildOffMarketText());
            SetText(view.AcquisitionSelectedLeadText, BuildAcquisitionSelectedLeadText());
            SetText(view.AcquisitionHistoryText, BuildAcquisitionActionDeskText());
            SetButtonVisible(view.AcquisitionFocusButton, true);
            SetButtonLabel(view.AcquisitionFocusButton, hasListing ? "Inspect Selected Site" : "Inspect Site");
            SetButtonInteractable(view.AcquisitionFocusButton, hasListing);
            SetButtonVisible(view.AcquisitionWorkflowButton, true);
            SetButtonLabel(view.AcquisitionWorkflowButton, hasListing ? "Open Formal Acquisition" : "Formal Acquisition");
            SetButtonInteractable(view.AcquisitionWorkflowButton, hasListing);
            SetButtonLabel(view.BuyListingButton, NormalizeAcquisitionActionLabel(acquisitionMarket.GetSelectedAcquisitionActionLabel(acquisitionSection)));
            SetButtonInteractable(view.BuyListingButton, hasListing);
        }

        private string BuildAcquisitionMarketSnapshotText()
        {
            if (acquisitionMarket == null)
            {
                return "Acquisition records unavailable.";
            }

            string raw = StripLeadingHeading(acquisitionMarket.BuildAcquisitionDashboardSnapshotSummary(acquisitionSection), "Market Snapshot");
            StringBuilder builder = new();
            AppendLabeledSummaryLine(builder, raw, new[] { "For sale", "Market", "Service pressure", "Town commerce", "Town", "Owned" });
            AppendLabeledSummaryLine(builder, raw, new[] { "Pipeline", "Watchlist", "Liquidity", "Decision climate" });
            AppendRawSummaryLines(builder, raw, 4,
                "Market Snapshot", "Selected Lead", "Checklist", "Funding & Readiness", "Process Notes", "Last");
            return builder.ToString().TrimEnd();
        }

        private string BuildAcquisitionSelectedLeadText()
        {
            if (acquisitionMarket == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new();

            if (!acquisitionMarket.TryGetSelectedListing(acquisitionSection, out _))
            {
                builder.AppendLine("No live lead selected.");
                builder.AppendLine("Cycle listings to inspect price, urgency, and fit before opening a formal acquisition.");
                return builder.ToString().TrimEnd();
            }

            string leadRaw = StripLeadingHeading(acquisitionMarket.BuildSelectedLeadCompactSummary(acquisitionSection), "Selected Lead");
            string readinessRaw = StripLeadingHeading(acquisitionMarket.BuildSelectedLeadReadinessSummary(acquisitionSection), "Funding & Readiness");
            string actionLabel = NormalizeAcquisitionActionLabel(acquisitionMarket.GetSelectedAcquisitionActionLabel(acquisitionSection));

            AppendLabeledSummaryLine(builder, leadRaw, new[] { "Lead", "Use", "Seller", "Owner", "Price", "Asking" });
            AppendLabeledSummaryLine(builder, leadRaw, new[] { "Source", "Confidence", "Urgency", "Exclusivity", "Deadline", "Rival" });
            AppendLabeledSummaryLine(builder, leadRaw, new[] { "State", "Value", "Plot", "Building" });
            builder.Append("Next move: ");
            builder.AppendLine(BuildAcquisitionActionGuidance(actionLabel));
            AppendLabeledSummaryLine(builder, readinessRaw, new[] { "Funding", "Readiness", "Commitment", "Runway", "Execution", "Liquidity" });

            ExpansionReadinessResult readiness = CreateExpansionReadinessService().BuildForSelectedAcquisition(acquisitionSection);
            string expansionSummary = readiness.BuildSummaryText();
            if (!string.IsNullOrWhiteSpace(expansionSummary))
            {
                AppendRawSummaryLines(builder, expansionSummary, 2, "Expansion Readiness", "Readiness", "Next step");
            }

            AppendRawSummaryLines(builder, leadRaw, 2,
                "Selected Lead", "Lead", "Use", "Seller", "Owner", "Price", "Asking", "Source", "Confidence", "Urgency", "Exclusivity", "Deadline", "Rival", "State", "Value", "Plot", "Building");
            return builder.ToString().TrimEnd();
        }

        private string BuildAcquisitionActionDeskText()
        {
            if (acquisitionMarket == null)
            {
                return string.Empty;
            }

            string processRaw = StripLeadingHeading(acquisitionMarket.BuildAcquisitionCompactProcessNotesSummary(acquisitionSection), "Process Notes");
            string checklistRaw = StripLeadingHeading(acquisitionMarket.BuildSelectedLeadActionChecklist(acquisitionSection), "Checklist");
            StringBuilder builder = new();
            AppendLabeledSummaryLine(builder, processRaw, new[] { "Pipeline", "Watchlist", "Process posture", "Process ledger", "Decision climate", "Decision" });
            AppendRawSummaryLines(builder, checklistRaw, 3, "Checklist");
            AppendRawSummaryLines(builder, processRaw, 5,
                "Process Notes", "Pipeline", "Watchlist", "Process posture", "Process ledger", "Decision climate", "Decision");
            AppendOpportunityDeskSummary(builder, "Acquisitions");
            return builder.ToString().TrimEnd();
        }

        private void AppendOpportunityDeskSummary(StringBuilder builder, string deskTarget)
        {
            if (builder == null)
            {
                return;
            }

            opportunityPressureRuntime ??= FindAnyObjectByType<OpportunityPressureRuntimeManager>();
            if (opportunityPressureRuntime == null || string.IsNullOrWhiteSpace(deskTarget))
            {
                return;
            }

            string summary = opportunityPressureRuntime.BuildDeskSummary(deskTarget, 2);
            if (string.IsNullOrWhiteSpace(summary))
            {
                return;
            }

            builder.AppendLine(summary);
        }

        private static string NormalizeAcquisitionActionLabel(string label)
        {
            return string.Equals(label, "Close", StringComparison.OrdinalIgnoreCase)
                ? "Close Acquisition"
                : (label ?? string.Empty);
        }

        private static string BuildSelectedAcquisitionCurrentRead(string actionLabel)
        {
            return actionLabel switch
            {
                "Open Inquiry" => "Land lead selected and ready for first contact.",
                "Commit Earnest" => "Land lead selected and waiting on earnest to prove seriousness.",
                "Run Diligence" => "Land lead selected and in the diligence stage.",
                "Agree Terms" => "Land lead selected and ready to turn diligence into real terms.",
                "Close Acquisition" => "Land lead selected and ready to close into owned property.",
                "Closed" => "Land lead is effectively closed and should now flow back into Properties.",
                _ => "Land lead selected; review fit, pressure, and readiness before committing cash."
            };
        }

        private static string BuildAcquisitionActionGuidance(string actionLabel)
        {
            return actionLabel switch
            {
                "Open Inquiry" => "Open the lead and see whether the seller treats you as serious.",
                "Commit Earnest" => "Reserve the lane before you spend more time on diligence.",
                "Run Diligence" => "Check condition, staffing, suppliers, demand, and value before terms.",
                "Agree Terms" => "Turn what you learned into price, structure, and close timing.",
                "Close Acquisition" => "Close cleanly and be ready for inherited pressure on day one.",
                "Closed" => "Shift to integration, staffing, and cash control.",
                _ => "Review the lead before committing capital."
            };
        }

        private static void AppendLabeledSummaryLine(StringBuilder builder, string raw, string[] preferredPrefixes)
        {
            if (builder == null)
            {
                return;
            }

            string line = FindFirstSummaryLine(raw, preferredPrefixes);
            if (!string.IsNullOrWhiteSpace(line))
            {
                builder.AppendLine(ClampSummaryLine(line, 120));
            }
        }

        private static void AppendRawSummaryLines(StringBuilder builder, string raw, int maxLines, params string[] blockedPrefixes)
        {
            if (builder == null || maxLines <= 0)
            {
                return;
            }

            List<string> lines = CollectSummaryLines(raw, blockedPrefixes);
            int appended = 0;
            for (int i = 0; i < lines.Count && appended < maxLines; i++)
            {
                builder.AppendLine(ClampSummaryLine(lines[i], 120));
                appended++;
            }
        }

        private static string FindFirstSummaryLine(string raw, string[] preferredPrefixes)
        {
            if (preferredPrefixes == null || preferredPrefixes.Length <= 0)
            {
                return string.Empty;
            }

            List<string> lines = CollectSummaryLines(raw);
            for (int prefixIndex = 0; prefixIndex < preferredPrefixes.Length; prefixIndex++)
            {
                string prefix = preferredPrefixes[prefixIndex];
                for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                {
                    if (SummaryLineStartsWith(lines[lineIndex], prefix))
                    {
                        return lines[lineIndex];
                    }
                }
            }

            return lines.Count > 0 ? lines[0] : string.Empty;
        }

        private static List<string> CollectSummaryLines(string raw, params string[] blockedPrefixes)
        {
            List<string> lines = new();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return lines;
            }

            string[] split = raw.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < split.Length; i++)
            {
                string line = NormalizeSummaryLine(split[i]);
                if (string.IsNullOrWhiteSpace(line) || IsBlockedSummaryLine(line, blockedPrefixes))
                {
                    continue;
                }

                bool duplicate = false;
                for (int existingIndex = 0; existingIndex < lines.Count; existingIndex++)
                {
                    if (string.Equals(lines[existingIndex], line, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    lines.Add(line);
                }
            }

            return lines;
        }

        private static bool IsBlockedSummaryLine(string line, string[] blockedPrefixes)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return true;
            }

            if (blockedPrefixes != null)
            {
                for (int i = 0; i < blockedPrefixes.Length; i++)
                {
                    if (SummaryLineStartsWith(line, blockedPrefixes[i]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool SummaryLineStartsWith(string line, string prefix)
        {
            return !string.IsNullOrWhiteSpace(line)
                && !string.IsNullOrWhiteSpace(prefix)
                && line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string StripLeadingHeading(string value, string heading)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(heading))
            {
                return value ?? string.Empty;
            }

            string normalized = value.Replace("\r", string.Empty).Trim();
            if (normalized.StartsWith(heading + "\n", StringComparison.OrdinalIgnoreCase))
            {
                return normalized.Substring(heading.Length).TrimStart('\n').Trim();
            }

            return normalized;
        }

        private static string NormalizeSummaryLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Replace('	', ' ').Replace("  ", " ").Trim();
            while (normalized.Contains("  "))
            {
                normalized = normalized.Replace("  ", " ");
            }

            return normalized;
        }

        private static string ClampSummaryLine(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength - 1).TrimEnd() + "…";
        }

        private void UpdateGovernment()
        {
            if (civicFoundation == null)
            {
                SetText(view.GovernmentTitleText, "Civic");
                SetText(view.GovernmentSummaryText, "Civic records unavailable. Town Hall authority has not been established in this save.");
                SetText(view.GovernmentEffectsText, "Confidence +0% | Town maturity +0% | Immigration pull +0% | Land confidence +0%");
                SetText(view.GovernmentOwnerImplicationsText, BuildGovernmentOwnerImplicationsBodyText("No civic uplift is improving land confidence, migration, or town-center prestige yet."));
                SetButtonVisible(view.GovernmentFocusButton, false);
                return;
            }

            civicFoundation.RefreshFromWorld();
            TownHallState townHall = civicFoundation.TownHall;
            TownHallContribution contribution = civicFoundation.CurrentTownHallContribution;
            bool built = townHall.Built;

            SetText(view.GovernmentTitleText, built ? $"Civic: {townHall.displayName}" : $"Civic: {townHall.displayName} Not Built");
            SetText(view.GovernmentSummaryText, BuildGovernmentSummaryText(townHall));
            SetText(view.GovernmentEffectsText, BuildGovernmentEffectsText(contribution));
            SetText(view.GovernmentOwnerImplicationsText, BuildGovernmentOwnerImplicationsBodyText(civicFoundation.BuildCompactSummaryText()));
            SetButtonVisible(view.GovernmentFocusButton, built);
            SetButtonLabel(view.GovernmentFocusButton, "View Civic Site");
            SetButtonInteractable(view.GovernmentFocusButton, built);
        }

        private void UpdateResources()
        {
            if (view == null || view.ResourcesSummaryText == null || view.ResourcesDistrictListText == null || view.ResourcesDetailText == null)
            {
                return;
            }

            if (townWorld == null || townWorld.RegionalResources == null || !townWorld.RegionalResources.HasMeaningfulMineralOpportunity)
            {
                SetText(view.ResourcesSummaryText, "Regional resources unavailable. Generate the town shell to inspect mineral districts and remote proto-sites.");
                SetText(view.ResourcesDistrictListText, "Districts: none.");
                SetText(view.ResourcesDetailText, "Future prospecting, claims, camps, and industrial management hooks will anchor here once mineral opportunity exists.");
                return;
            }

            RegionalResourceSnapshot resources = townWorld.RegionalResources;
            SetText(view.ResourcesSummaryText, resources.BuildSummaryText());
            SetText(view.ResourcesDistrictListText, BuildResourceDistrictListText(resources));
            SetText(view.ResourcesDetailText, BuildResourceDetailText(resources));
        }

        private static string BuildResourceDistrictListText(RegionalResourceSnapshot resources)
        {
            StringBuilder builder = new();
            builder.AppendLine("District Ledger");
            for (int i = 0; i < 4; i++)
            {
                MineralResourceKind kind = (MineralResourceKind)i;
                MineralDistrictRecord strongest = resources.GetStrongestDistrict(kind);
                if (strongest == null)
                {
                    builder.AppendLine($"{kind}: no district generated.");
                    continue;
                }

                builder.AppendLine($"{kind}: {resources.GetDistrictCount(kind)} district(s) | strongest {strongest.Strength01:P0} | freight {strongest.FreightPressure01:P0} | settlement {strongest.SettlementPressure01:P0}");
            }

            return builder.ToString().TrimEnd();
        }

        private static string BuildResourceDetailText(RegionalResourceSnapshot resources)
        {
            StringBuilder builder = new();
            builder.AppendLine("Proto-Site Detail");
            for (int i = 0; i < resources.RemoteSites.Count; i++)
            {
                RemoteIndustrySiteRecord site = resources.RemoteSites[i];
                if (site == null)
                {
                    continue;
                }

                builder.AppendLine($"{site.DebugLabel} | site {site.SiteStrength01:P0} | camp {site.FutureCampRelevance01:P0} | boomtown {site.FutureBoomtownRelevance01:P0}");
            }

            if (resources.RemoteSites.Count <= 0)
            {
                builder.AppendLine("No proto-sites cleared the current threshold.");
            }

            builder.AppendLine("Next hooks: prospecting, claims, mine works, camps, freight demand, and off-map export.");
            return builder.ToString().TrimEnd();
        }

        private void FocusStrongestResourceSite()
        {
            if (townWorld == null || strategyCamera == null || townWorld.RegionalResources == null)
            {
                return;
            }

            RemoteIndustrySiteRecord focusSite = null;
            float bestStrength = float.MinValue;
            for (int i = 0; i < townWorld.RegionalResources.RemoteSites.Count; i++)
            {
                RemoteIndustrySiteRecord candidate = townWorld.RegionalResources.RemoteSites[i];
                if (candidate == null || candidate.SiteStrength01 <= bestStrength)
                {
                    continue;
                }

                focusSite = candidate;
                bestStrength = candidate.SiteStrength01;
            }

            if (focusSite != null)
            {
                strategyCamera.SetFocus(focusSite.AnchorPosition);
            }
        }

        private string BuildGovernmentSummaryText(TownHallState townHall)
        {
            if (townHall == null || !townHall.Built)
            {
                string owner = townHall != null && !string.IsNullOrWhiteSpace(townHall.ownerDisplayName)
                    ? townHall.ownerDisplayName
                    : "Town Council";
                string status = townHall != null && !string.IsNullOrWhiteSpace(townHall.civicStatusLabel)
                    ? townHall.civicStatusLabel
                    : "Civic";
                return $"Status: Not built | Steward: {owner} | {status}\nPublic civic anchor inactive; town confidence remains mostly private-market led.";
            }

            return $"Status: Built | Steward: {townHall.ownerDisplayName} | {townHall.civicStatusLabel}\nPublic civic site: Plot {townHall.plotId:000} | Building {townHall.buildingId:000}";
        }

        private string BuildGovernmentEffectsText(TownHallContribution contribution)
        {
            StringBuilder builder = new();
            builder.AppendLine($"Confidence +{FormatContributionPercent(contribution.CivicConfidence01)} | Town maturity +{FormatContributionPercent(contribution.TownMaturity01)}");
            builder.AppendLine($"Immigration pull +{FormatContributionPercent(contribution.ImmigrationPull01)} | Land confidence +{FormatContributionPercent(contribution.LandConfidence01)}");
            return builder.ToString();
        }

        private static string BuildGovernmentOwnerImplicationsBodyText(string compactSummary)
        {
            string summary = string.IsNullOrWhiteSpace(compactSummary)
                ? "Town Hall: Not built | Civic confidence +0% | Maturity +0%"
                : compactSummary.Trim();
            return summary.StartsWith("Implication:", StringComparison.OrdinalIgnoreCase)
                ? summary
                : $"Implication: {summary}";
        }

        private string BuildOffMarketText()
        {
            if (townWorld == null)
            {
                return "Off-market unavailable.";
            }

            int offMarketBusinesses = 0;
            if (sharedBusinessRuntime != null)
            {
                for (int i = 0; i < sharedBusinessRuntime.Businesses.Count; i++)
                {
                    BusinessInstanceState business = sharedBusinessRuntime.Businesses[i];
                    if (business == null || business.BusinessType == BusinessType.GeneralStore || (business.Owner != null && business.Owner.OwnerKind == BusinessOwnerKind.Player))
                    {
                        continue;
                    }

                    PlacedBuilding building = GetBuilding(business.AssignedBuildingId);
                    if (building == null)
                    {
                        continue;
                    }

                    string marketState = building.playerOwned ? "owned site" : IsListedBusiness(building.id) ? "for sale" : "not listed";
                    if (marketState == "not listed")
                    {
                        offMarketBusinesses++;
                    }
                }
            }

            int offMarketLand = 0;
            for (int i = 0; i < townWorld.Plots.Count; i++)
            {
                TownPlot plot = townWorld.Plots[i];
                if (plot == null || plot.playerOwned || plot.buildingId >= 0 || IsListedPlot(plot.id))
                {
                    continue;
                }

                offMarketLand++;
            }

            int vacantBuildings = 0;
            for (int i = 0; i < townWorld.Buildings.Count; i++)
            {
                PlacedBuilding building = townWorld.Buildings[i];
                if (building == null || building.playerOwned || IsListedBusiness(building.id) || GetBusinessForBuilding(building.id) != null || building.id == (storeRuntime != null ? storeRuntime.StoreBuildingId : -1))
                {
                    continue;
                }

                vacantBuildings++;
            }

            return BuildOffMarketBodyText(offMarketBusinesses, offMarketLand, vacantBuildings);
        }

        private static string BuildOffMarketBodyText(int offMarketBusinesses, int offMarketLand, int vacantBuildings)
        {
            StringBuilder builder = new();
            builder.AppendLine($"Quiet opportunities: {offMarketBusinesses} businesses | {offMarketLand} land");
            builder.AppendLine($"Unlisted vacant buildings: {vacantBuildings}");
            builder.AppendLine("Implication: use these counts as soft scouting pressure, not live deal availability.");
            return builder.ToString().TrimEnd();
        }

        private string BuildAcquisitionHistoryText()
        {
            StringBuilder builder = new();
            builder.AppendLine("Process Notes");
            builder.AppendLine($"{acquisitionMarket.OwnedLandCount} land | {GetOwnedBusinessCount()} businesses");
            builder.AppendLine(acquisitionMarket.BuildDealPipelineSummary());
            builder.AppendLine(acquisitionMarket.BuildWatchlistSummaryText());
            builder.AppendLine(acquisitionMarket.BuildAcquisitionProcessPostureSummary());
            builder.AppendLine(acquisitionMarket.BuildAcquisitionProcessLedgerSummary());
            builder.AppendLine(acquisitionMarket.BuildAcquisitionDecisionClimateSummary());
            string actionChecklist = acquisitionMarket.BuildSelectedLeadActionChecklist(AcquisitionMarketSection.Land);
            if (string.IsNullOrWhiteSpace(actionChecklist))
            {
                actionChecklist = acquisitionMarket.BuildSelectedLeadActionChecklist(AcquisitionMarketSection.Businesses);
            }
            if (!string.IsNullOrWhiteSpace(actionChecklist))
            {
                builder.AppendLine(actionChecklist);
            }
            string selectedLead = acquisitionMarket.BuildSelectedLeadReadinessSummary(AcquisitionMarketSection.Land);
            if (string.IsNullOrWhiteSpace(selectedLead))
            {
                selectedLead = acquisitionMarket.BuildSelectedLeadReadinessSummary(AcquisitionMarketSection.Businesses);
            }
            if (!string.IsNullOrWhiteSpace(selectedLead))
            {
                builder.AppendLine(selectedLead);
            }
            if (playerPortfolio != null)
            {
                builder.AppendLine(playerPortfolio.BuildWealthHeadline());
                builder.AppendLine(playerPortfolio.BuildLiquidityPressureSummary());
            }

            if (sharedBusinessRuntime != null)
            {
                builder.AppendLine(sharedBusinessRuntime.BuildTownCommerceHeadline());
                builder.AppendLine(sharedBusinessRuntime.BuildTownCommerceLedgerSummary());
                builder.AppendLine(sharedBusinessRuntime.BuildTownDecisionClimateHeadline());
                builder.AppendLine(sharedBusinessRuntime.BuildTownProcessClimateSummary());
            }

            builder.AppendLine($"Last: {BuildCompactLastAcquisition()}");
            return builder.ToString();
        }

        private void DecreaseStoreMargin()
        {
            if (TryGetSelectedOwnedEntry(out OwnedEntry selected) && selected.Kind == OwnedKind.Land)
            {
                selectedOwnedLandDevelopmentIntent = OwnedLandDevelopmentIntent.House;
                selectedBuildOptionIndex = 0;
                selectedBusinessBuildOptionIndex = 0;
                selectedBusinessDevelopmentPlotId = -1;
                RefreshSelectedPropertyActionPanelStatus();
                SetTab(ManagementPanelTab.Properties);
                return;
            }

            if (selected.Kind == OwnedKind.Business)
            {
                BusinessInstanceState business = GetBusinessForBuilding(selected.BuildingId);
                string marginMessage = string.Empty;
                if (business != null && sharedBusinessRuntime != null && sharedBusinessRuntime.TryAdjustBusinessMarginPolicy(business, -1, out marginMessage))
                {
                    panelStatus = marginMessage;
                }
                else
                {
                    panelStatus = sharedBusinessRuntime != null ? marginMessage : "Business unavailable.";
                    Debug.Log(panelStatus, this);
                }

                SetTab(ManagementPanelTab.Properties);
                return;
            }

            AdjustStoreMargin(-1);
        }

        private void IncreaseStoreMargin()
        {
            if (TryGetSelectedOwnedEntry(out OwnedEntry selected) && selected.Kind == OwnedKind.Land)
            {
                selectedOwnedLandDevelopmentIntent = OwnedLandDevelopmentIntent.Business;
                selectedBusinessBuildOptionIndex = 0;
                selectedBusinessDevelopmentPlotId = -1;
                EnsureOwnedLandBusinessSelection(selected.PlotId);
                RefreshSelectedPropertyActionPanelStatus();
                SetTab(ManagementPanelTab.Properties);
                return;
            }

            if (selected.Kind == OwnedKind.Business)
            {
                BusinessInstanceState business = GetBusinessForBuilding(selected.BuildingId);
                string marginMessage = string.Empty;
                if (business != null && sharedBusinessRuntime != null && sharedBusinessRuntime.TryAdjustBusinessMarginPolicy(business, 1, out marginMessage))
                {
                    panelStatus = marginMessage;
                }
                else
                {
                    panelStatus = sharedBusinessRuntime != null ? marginMessage : "Business unavailable.";
                    Debug.Log(panelStatus, this);
                }

                SetTab(ManagementPanelTab.Properties);
                return;
            }

            AdjustStoreMargin(1);
        }

        private void AdjustStoreMargin(int stepDelta)
        {
            if (!TryGetSelectedOwnedEntry(out OwnedEntry selected) || selected.Kind != OwnedKind.Store)
            {
                panelStatus = "Select the General Store before changing price adjustment.";
                SetTab(ManagementPanelTab.Properties);
                return;
            }

            string message = string.Empty;
            if (storeRuntime != null && storeRuntime.TryAdjustStoreMarginSteps(stepDelta, out message))
            {
                panelStatus = message;
            }
            else
            {
                panelStatus = storeRuntime != null ? message : "Store unavailable.";
                Debug.Log(panelStatus, this);
            }

            SetTab(ManagementPanelTab.Properties);
        }

        private void SelectPreviousWorkerCandidate()
        {
            if (TryGetSelectedOwnedEntry(out OwnedEntry selected) && selected.Kind == OwnedKind.Land)
            {
                if (selectedOwnedLandDevelopmentIntent == OwnedLandDevelopmentIntent.Business)
                {
                    selectedBusinessActivationIndex = WrapIndex(selectedBusinessActivationIndex - 1, GetBusinessDevelopmentCandidateCount());
                    selectedBusinessBuildOptionIndex = 0;
                    selectedBusinessDevelopmentPlotId = selected.PlotId;
                }
                else
                {
                    selectedBuildOptionIndex = WrapIndex(selectedBuildOptionIndex - 1, GetHouseBuildOptionCount(selected.PlotId));
                }

                RefreshSelectedPropertyActionPanelStatus();
                SetTab(ManagementPanelTab.Properties);
                return;
            }

            if (IsStartableOwnedShell(selected))
            {
                selectedBusinessActivationIndex = WrapIndex(selectedBusinessActivationIndex - 1, GetBusinessActivationCandidateCount(selected.BuildingId));
                selectedBusinessActivationBuildingId = selected.BuildingId;
                RefreshSelectedPropertyActionPanelStatus();
                SetTab(ManagementPanelTab.Properties);
                return;
            }

            if (selected.Kind == OwnedKind.Holding)
            {
                selectedHouseholdUpgradeIndex = WrapIndex(selectedHouseholdUpgradeIndex - 1, GetHouseholdUpgradeOptionCount());
                RefreshSelectedPropertyActionPanelStatus();
                SetTab(ManagementPanelTab.Properties);
                return;
            }

            selectedWorkerCandidateIndex = WrapIndex(selectedWorkerCandidateIndex - 1, GetWorkerSelectionCount(selected));
            SetTab(ManagementPanelTab.Properties);
        }

        private void SelectNextWorkerCandidate()
        {
            if (TryGetSelectedOwnedEntry(out OwnedEntry selected) && selected.Kind == OwnedKind.Land)
            {
                if (selectedOwnedLandDevelopmentIntent == OwnedLandDevelopmentIntent.Business)
                {
                    selectedBusinessActivationIndex = WrapIndex(selectedBusinessActivationIndex + 1, GetBusinessDevelopmentCandidateCount());
                    selectedBusinessBuildOptionIndex = 0;
                    selectedBusinessDevelopmentPlotId = selected.PlotId;
                }
                else
                {
                    selectedBuildOptionIndex = WrapIndex(selectedBuildOptionIndex + 1, GetHouseBuildOptionCount(selected.PlotId));
                }

                RefreshSelectedPropertyActionPanelStatus();
                SetTab(ManagementPanelTab.Properties);
                return;
            }

            if (IsStartableOwnedShell(selected))
            {
                selectedBusinessActivationIndex = WrapIndex(selectedBusinessActivationIndex + 1, GetBusinessActivationCandidateCount(selected.BuildingId));
                selectedBusinessActivationBuildingId = selected.BuildingId;
                RefreshSelectedPropertyActionPanelStatus();
                SetTab(ManagementPanelTab.Properties);
                return;
            }

            if (selected.Kind == OwnedKind.Holding)
            {
                selectedHouseholdUpgradeIndex = WrapIndex(selectedHouseholdUpgradeIndex + 1, GetHouseholdUpgradeOptionCount());
                RefreshSelectedPropertyActionPanelStatus();
                SetTab(ManagementPanelTab.Properties);
                return;
            }

            selectedWorkerCandidateIndex = WrapIndex(selectedWorkerCandidateIndex + 1, GetWorkerSelectionCount(selected));
            SetTab(ManagementPanelTab.Properties);
        }

        private void AssignSelectedWorkerCandidate()
        {
            if (!TryGetSelectedOwnedEntry(out OwnedEntry selected))
            {
                panelStatus = "Select a managed property before changing staffing.";
                SetTab(ManagementPanelTab.Properties);
                return;
            }

            if (selected.Kind == OwnedKind.Land)
            {
                if (selectedOwnedLandDevelopmentIntent == OwnedLandDevelopmentIntent.Business)
                {
                    selectedBusinessBuildOptionIndex = WrapIndex(selectedBusinessBuildOptionIndex + 1, GetBusinessBuildOptionCount(selected.PlotId));
                }

                RefreshSelectedPropertyActionPanelStatus();
                SetTab(ManagementPanelTab.Properties);
                return;
            }

            if (selected.Kind == OwnedKind.Holding)
            {
                panelStatus = "This holding has no active business staffing.";
                SetTab(ManagementPanelTab.Properties);
                return;
            }

            string message = string.Empty;
            BusinessInstanceState sharedBusiness = selected.Kind == OwnedKind.Business ? GetBusinessForBuilding(selected.BuildingId) : null;
            if (selected.Kind == OwnedKind.Business && sharedBusiness == null)
            {
                panelStatus = "Use Start Business to activate this owned building shell.";
                SetTab(ManagementPanelTab.Properties);
                return;
            }

            if (selected.Kind == OwnedKind.Business && sharedBusiness != null)
            {
                if (sharedBusinessRuntime != null && sharedBusinessRuntime.IsWorkerFireMode(sharedBusiness))
                {
                    if (sharedBusinessRuntime.TryFireWorkerFromSlot(sharedBusiness, selectedWorkerCandidateIndex, out message))
                    {
                        panelStatus = "Worker removed.";
                    }
                    else
                    {
                        panelStatus = message;
                        Debug.Log(panelStatus, this);
                    }
                }
                else if (sharedBusinessRuntime != null && sharedBusinessRuntime.TryAssignCandidateToOpenSlot(sharedBusiness, selectedWorkerCandidateIndex, out message))
                {
                    panelStatus = "Worker assignment updated.";
                }
                else
                {
                    panelStatus = sharedBusinessRuntime != null ? message : "Business unavailable.";
                    Debug.Log(panelStatus, this);
                }

                selectedWorkerCandidateIndex = WrapIndex(selectedWorkerCandidateIndex, GetWorkerSelectionCount(selected));
                SetTab(ManagementPanelTab.Properties);
                return;
            }

            if (IsWorkerFireMode())
            {
                if (storeRuntime != null && storeRuntime.TryFireWorkerFromSlot(selectedWorkerCandidateIndex, out message))
                {
                    panelStatus = "Worker removed.";
                }
                else
                {
                    panelStatus = storeRuntime != null ? message : "Store unavailable.";
                    Debug.Log(panelStatus, this);
                }
            }
            else if (storeRuntime != null && storeRuntime.TryAssignCandidateToOpenSlot(selectedWorkerCandidateIndex, out message))
            {
                panelStatus = "Worker assignment updated.";
            }
            else
            {
                panelStatus = storeRuntime != null ? message : "Store unavailable.";
                Debug.Log(panelStatus, this);
            }

            selectedWorkerCandidateIndex = WrapIndex(selectedWorkerCandidateIndex, GetWorkerSelectionCount(selected));
            SetTab(ManagementPanelTab.Properties);
        }

        private void ChooseBuildingForSelectedPlot()
        {
            if (!TryGetSelectedOwnedEntry(out OwnedEntry selected))
            {
                panelStatus = "Select owned empty land or an owned building shell first.";
                SetTab(ManagementPanelTab.Properties);
                return;
            }

            if (selected.Kind == OwnedKind.Holding)
            {
                string upgradeMessage = "Acquisitions unavailable.";
                if (acquisitionMarket != null && acquisitionMarket.TryBuildHouseholdUpgrade(selected.BuildingId, selectedHouseholdUpgradeIndex, out _, out upgradeMessage))
                {
                    panelStatus = upgradeMessage;
                    selectedHouseholdUpgradeIndex = WrapIndex(selectedHouseholdUpgradeIndex, GetHouseholdUpgradeOptionCount());
                    ownedSignature = string.Empty;
                }
                else
                {
                    panelStatus = upgradeMessage;
                    Debug.Log(panelStatus, this);
                }

                SetTab(ManagementPanelTab.Properties);
                return;
            }

            if (selected.Kind != OwnedKind.Land && selected.Kind != OwnedKind.Business)
            {
                panelStatus = "Select owned empty land or an owned building shell first.";
                SetTab(ManagementPanelTab.Properties);
                return;
            }

            if (acquisitionMarket == null)
            {
                panelStatus = "Acquisitions unavailable.";
                Debug.Log(panelStatus, this);
                SetTab(ManagementPanelTab.Properties);
                return;
            }

            if (selected.Kind == OwnedKind.Business)
            {
                BusinessInstanceState activeBusiness = GetBusinessForBuilding(selected.BuildingId);
                if (activeBusiness != null)
                {
                    string saleMessage = "Business sale unavailable.";
                    if (acquisitionMarket != null && acquisitionMarket.TrySellPlayerBusiness(selected.BuildingId, out saleMessage))
                    {
                        panelStatus = saleMessage;
                        selectedWorkerCandidateIndex = 0;
                        selectedBusinessActivationBuildingId = -1;
                        ownedSignature = string.Empty;
                    }
                    else
                    {
                        panelStatus = saleMessage;
                        Debug.Log(panelStatus, this);
                    }

                    SetTab(ManagementPanelTab.Properties);
                    return;
                }

                if (!IsStartableOwnedShell(selected))
                {
                    panelStatus = "This property is missing a usable owned shell definition.";
                    SetTab(ManagementPanelTab.Properties);
                    return;
                }

                string activationMessage;
                if (acquisitionMarket.TryStartBusinessFromOwnedShell(selected.BuildingId, selectedBusinessActivationIndex, out BusinessInstanceState business, out activationMessage))
                {
                    panelStatus = !string.IsNullOrWhiteSpace(activationMessage)
                        ? activationMessage
                        : (business != null ? $"{business.RuntimeDisplayName} opened." : "Business opened.");
                    selectedBusinessActivationIndex = 0;
                    selectedBusinessActivationBuildingId = -1;
                    selectedWorkerCandidateIndex = 0;
                    ownedSignature = string.Empty;
                }
                else
                {
                    panelStatus = activationMessage;
                    Debug.Log(panelStatus, this);
                }

                SetTab(ManagementPanelTab.Properties);
                return;
            }

            string message;
            PlacedBuilding building;
            bool constructed;
            if (selectedOwnedLandDevelopmentIntent == OwnedLandDevelopmentIntent.Business)
            {
                constructed = acquisitionMarket.TryConstructBusinessShellOnOwnedPlot(
                    selected.PlotId,
                    selectedBusinessActivationIndex,
                    selectedBusinessBuildOptionIndex,
                    out building,
                    out message);
            }
            else
            {
                constructed = acquisitionMarket.TryConstructHouseOnOwnedPlot(
                    selected.PlotId,
                    selectedBuildOptionIndex,
                    out building,
                    out message);
            }

            if (constructed)
            {
                panelStatus = !string.IsNullOrWhiteSpace(message)
                    ? message
                    : (building != null ? $"Built shell on Plot {selected.PlotId:000}." : "Building shell constructed.");
                selectedBuildOptionIndex = 0;
                selectedBusinessBuildOptionIndex = 0;
                selectedBusinessDevelopmentPlotId = -1;
                ownedSignature = string.Empty;
                if (building != null)
                {
                    RebuildOwnedRowsIfNeeded();
                    SelectOwnedBuildingIfPresent(building.id);
                }
            }
            else
            {
                panelStatus = message;
                Debug.Log(panelStatus, this);
            }

            SetTab(ManagementPanelTab.Properties);
        }

        private bool IsWorkerFireMode()
        {
            return storeRuntime != null
                && storeRuntime.OpenWorkerSlotCount <= 0
                && storeRuntime.FilledWorkerSlotCount > 0;
        }

        private int GetWorkerSelectionCount()
        {
            if (storeRuntime == null)
            {
                return 0;
            }

            return IsWorkerFireMode()
                ? storeRuntime.FilledWorkerSlotCount
                : storeRuntime.GetAvailableWorkerCandidateCount();
        }

        private int GetWorkerSelectionCount(OwnedEntry selected)
        {
            if (selected.Kind == OwnedKind.Holding || selected.Kind == OwnedKind.Renting)
            {
                return 0;
            }

            if (selected.Kind == OwnedKind.Business && sharedBusinessRuntime != null)
            {
                return sharedBusinessRuntime.GetWorkerSelectionCount(GetBusinessForBuilding(selected.BuildingId));
            }

            return GetWorkerSelectionCount();
        }

        private void SelectAcquisitionSection(AcquisitionMarketSection section)
        {
            acquisitionSection = section;
            panelStatus = section == AcquisitionMarketSection.Businesses
                ? "Viewing business acquisition listings."
                : "Viewing land acquisition listings.";
            LLFeedbackService.Play(LLFeedbackKind.UISelect, $"acquisition {section} section switch", canvas);
            SetTab(ManagementPanelTab.Acquisitions);
        }

        private void OpenSelectedAcquisitionWorkflow()
        {
            if (acquisitionMarket == null || !acquisitionMarket.TryGetSelectedListing(acquisitionSection, out _))
            {
                panelStatus = "No acquisition lead is selected.";
                SetTab(ManagementPanelTab.Acquisitions);
                return;
            }

            EnsureWorkflowController()?.OpenAcquisitionWorkflow(acquisitionSection);
            panelStatus = "Opened formal acquisition workflow.";
            LLFeedbackService.Play(LLFeedbackKind.UIPanel, "formal acquisition workflow open", canvas);
            SetTab(ManagementPanelTab.Acquisitions);
        }

        private void PreviousListing()
        {
            if (acquisitionMarket == null)
            {
                panelStatus = "Acquisitions unavailable.";
                SetTab(ManagementPanelTab.Acquisitions);
                return;
            }

            acquisitionMarket.SelectPrevious(acquisitionSection);
            panelStatus = acquisitionSection == AcquisitionMarketSection.Businesses
                ? "Selected previous business listing."
                : "Selected previous land listing.";
            LLFeedbackService.Play(LLFeedbackKind.UISelect, "previous acquisition listing select", canvas);
            SetTab(ManagementPanelTab.Acquisitions);
        }

        private void NextListing()
        {
            if (acquisitionMarket == null)
            {
                panelStatus = "Acquisitions unavailable.";
                SetTab(ManagementPanelTab.Acquisitions);
                return;
            }

            acquisitionMarket.SelectNext(acquisitionSection);
            panelStatus = acquisitionSection == AcquisitionMarketSection.Businesses
                ? "Selected next business listing."
                : "Selected next land listing.";
            LLFeedbackService.Play(LLFeedbackKind.UISelect, "next acquisition listing select", canvas);
            SetTab(ManagementPanelTab.Acquisitions);
        }

        private void BuyListing()
        {
            if (acquisitionMarket != null && acquisitionMarket.TryGetSelectedListing(acquisitionSection, out _))
            {
                EnsureSellerMeetingController()?.OpenForSelected(acquisitionSection);
                panelStatus = "Opened seller meeting.";
                LLFeedbackService.Play(LLFeedbackKind.UIPanel, "seller meeting open", canvas);
            }
            else
            {
                panelStatus = acquisitionMarket != null ? "No acquisition lead is selected." : "Acquisitions unavailable.";
                LLFeedbackService.Play(LLFeedbackKind.Warning, "seller meeting open blocked", canvas);
                Debug.Log(panelStatus, this);
            }

            SetTab(ManagementPanelTab.Acquisitions);
        }

        private void FocusSelectedAcquisitionListing()
        {
            if (acquisitionMarket == null)
            {
                panelStatus = "Acquisitions unavailable.";
                Debug.Log(panelStatus, this);
                SetTab(ManagementPanelTab.Acquisitions);
                return;
            }

            if (strategyCamera == null || townWorld == null || townWorld.Grid == null)
            {
                panelStatus = "No strategy camera or generated town grid available to focus.";
                Debug.Log(panelStatus, this);
                SetTab(ManagementPanelTab.Acquisitions);
                return;
            }

            if (!acquisitionMarket.TryGetSelectedListing(acquisitionSection, out AcquisitionListing listing) || listing == null)
            {
                panelStatus = acquisitionSection == AcquisitionMarketSection.Businesses
                    ? "No business listing is selected."
                    : "No land listing is selected.";
                SetTab(ManagementPanelTab.Acquisitions);
                return;
            }

            if (!TryGetListingWorldCenter(listing, out Vector3 center))
            {
                panelStatus = $"Cannot focus {listing.title}: site is unavailable.";
                Debug.Log(panelStatus, this);
                SetTab(ManagementPanelTab.Acquisitions);
                return;
            }

            strategyCamera.SetFocus(center);
            panelStatus = $"Focused {listing.title}.";
            SetTab(ManagementPanelTab.Acquisitions);
        }

        private void FocusTownHall()
        {
            if (civicFoundation == null)
            {
                panelStatus = "Town Hall records unavailable.";
                SetTab(ManagementPanelTab.Government);
                return;
            }

            if (strategyCamera == null || townWorld == null || townWorld.Grid == null)
            {
                panelStatus = "No strategy camera or generated town grid available to focus.";
                SetTab(ManagementPanelTab.Government);
                return;
            }

            if (!civicFoundation.TryGetTownHallBuilding(out PlacedBuilding building) || building == null)
            {
                panelStatus = "Town Hall is not built.";
                SetTab(ManagementPanelTab.Government);
                return;
            }

            strategyCamera.SetFocus(RectCenter(building.footprint));
            string display = building.definition != null ? building.definition.DisplayName : "Town Hall";
            panelStatus = $"Focused {display}.";
            SetTab(ManagementPanelTab.Government);
        }

        private void RebuildOwnedRowsIfNeeded()
        {
            bool hadPreviousSelection = TryGetSelectedOwnedEntry(out OwnedEntry previousSelected);
            string signature = BuildOwnedSignature();
            if (signature == ownedSignature && ownedEntryButtons.Count == ownedEntries.Count)
            {
                return;
            }

            ownedSignature = signature;
            ApplyOwnedSelectionContinuity(hadPreviousSelection ? previousSelected : default, hadPreviousSelection);
            RebuildOwnedRows();
        }


        // Preserve site continuity when the portfolio entry changes form in-place
        // (for example land -> shell or shell -> operating business).
        private void ApplyOwnedSelectionContinuity(OwnedEntry previousSelected, bool hadPreviousSelection)
        {
            if (ownedEntries.Count == 0)
            {
                selectedOwnedIndex = 0;
                return;
            }

            if (!hadPreviousSelection)
            {
                selectedOwnedIndex = Mathf.Clamp(selectedOwnedIndex, 0, ownedEntries.Count - 1);
                return;
            }

            int continuityIndex = FindOwnedContinuityIndex(previousSelected);
            if (continuityIndex >= 0)
            {
                OwnedEntry currentSelected = ownedEntries[continuityIndex];
                bool selectionChanged = continuityIndex != Mathf.Clamp(selectedOwnedIndex, 0, Mathf.Max(0, ownedEntries.Count - 1));
                bool stateChanged = previousSelected.SelectionKey != currentSelected.SelectionKey || !string.Equals(previousSelected.Label, currentSelected.Label, StringComparison.Ordinal);
                selectedOwnedIndex = continuityIndex;
                if (selectionChanged || stateChanged)
                {
                    string continuityStatus = BuildOwnedContinuityStatus(previousSelected, currentSelected);
                    if (!string.IsNullOrWhiteSpace(continuityStatus))
                    {
                        pendingOwnedContinuityStatus = continuityStatus;
                    }
                }

                return;
            }

            selectedOwnedIndex = Mathf.Clamp(selectedOwnedIndex, 0, ownedEntries.Count - 1);
            if (ownedEntries.Count > 0)
            {
                pendingOwnedContinuityStatus = $"Previously selected property is no longer available. Showing {BuildOwnedEntryRowTitle(ownedEntries[selectedOwnedIndex])}.";
            }
        }

        private int FindOwnedContinuityIndex(OwnedEntry previousSelected)
        {
            for (int i = 0; i < ownedEntries.Count; i++)
            {
                if (ownedEntries[i].SelectionKey == previousSelected.SelectionKey)
                {
                    return i;
                }
            }

            if (previousSelected.BuildingId >= 0)
            {
                for (int i = 0; i < ownedEntries.Count; i++)
                {
                    if (ownedEntries[i].BuildingId == previousSelected.BuildingId)
                    {
                        return i;
                    }
                }
            }

            if (previousSelected.PlotId >= 0)
            {
                for (int i = 0; i < ownedEntries.Count; i++)
                {
                    if (ownedEntries[i].PlotId == previousSelected.PlotId)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private string BuildOwnedContinuityStatus(OwnedEntry previousSelected, OwnedEntry currentSelected)
        {
            if (previousSelected.Kind == OwnedKind.Land && currentSelected.BuildingId >= 0 && currentSelected.PlotId == previousSelected.PlotId)
            {
                return "Selected plot advanced into a building entry. Review the new property state and next action.";
            }

            if (previousSelected.BuildingId >= 0 && currentSelected.BuildingId == previousSelected.BuildingId && previousSelected.Kind != currentSelected.Kind)
            {
                return currentSelected.Kind == OwnedKind.Business
                    ? "Selected building is now operating. Review staffing, cash, and next moves from the updated entry."
                    : "Selected property changed state. Review the updated entry and next action.";
            }

            if (previousSelected.PlotId >= 0 && currentSelected.PlotId == previousSelected.PlotId && previousSelected.SelectionKey != currentSelected.SelectionKey)
            {
                return "Selected site changed state. Review the updated property entry and next action.";
            }

            return string.Empty;
        }

        private void ConsumePendingOwnedContinuityStatus()
        {
            if (string.IsNullOrWhiteSpace(pendingOwnedContinuityStatus))
            {
                return;
            }

            panelStatus = pendingOwnedContinuityStatus;
            pendingOwnedContinuityStatus = string.Empty;
        }

        private string BuildPropertiesEmptyStateSummary()
        {
            int landListings = acquisitionMarket != null ? acquisitionMarket.LandListings.Count : 0;
            int businessListings = acquisitionMarket != null ? acquisitionMarket.BusinessListings.Count : 0;
            if (landListings > 0 || businessListings > 0)
            {
                return $"Nothing is in your portfolio yet. Land listings {landListings} | Business listings {businessListings}.";
            }

            return "Owned land, businesses, houses, and rental property appear here after acquisition or construction.";
        }

        private string BuildPropertiesEmptyStateDetail()
        {
            if (acquisitionMarket == null)
            {
                return "Open Acquisitions to inspect market opportunities. Closed deals and finished builds appear here automatically.";
            }

            if (acquisitionSection == AcquisitionMarketSection.Businesses && acquisitionMarket.BusinessListings.Count > 0)
            {
                return $"Open Acquisitions and inspect the business market. {acquisitionMarket.BusinessListings.Count} business listing{(acquisitionMarket.BusinessListings.Count == 1 ? string.Empty : "s")} currently visible.";
            }

            if (acquisitionMarket.LandListings.Count > 0)
            {
                return $"Open Acquisitions and inspect Land listings. {acquisitionMarket.LandListings.Count} parcel{(acquisitionMarket.LandListings.Count == 1 ? string.Empty : "s")} currently visible for expansion.";
            }

            return "Open Acquisitions and inspect the market. Your first closed parcel or finished shell will appear here automatically.";
        }

        private string BuildPropertiesEmptyStateAction()
        {
            if (acquisitionMarket != null)
            {
                if (acquisitionMarket.LandListings.Count > 0)
                {
                    return "Next move: open Acquisitions, switch to Land if needed, inspect a parcel, and begin inquiry when the site fits your cash and plan.";
                }

                if (acquisitionMarket.BusinessListings.Count > 0)
                {
                    return "Next move: open Acquisitions and review business opportunities, but land expansion remains the cleanest first build path.";
                }
            }

            return "Next move: open Acquisitions, review available opportunities, and close a site that can support your next build step.";
        }

        private string BuildOwnedSignature()
        {
            ownedEntries.Clear();
            if (storeRuntime != null)
            {
                int buildingId = storeRuntime.StoreBuildingId;
                PlacedBuilding building = GetBuilding(buildingId);
                string name = storeRuntime.CurrentBusiness != null
                    ? storeRuntime.CurrentBusiness.RuntimeDisplayName
                    : (storeRuntime.StoreDefinition != null ? storeRuntime.StoreDefinition.Business.DisplayName : "General Store");
                ownedEntries.Add(new OwnedEntry(OwnedKind.Store, buildingId, building != null ? building.plotId : -1, name));
            }

            if (populationManager != null || sharedBusinessRuntime != null)
            {
                ownedEntries.Add(new OwnedEntry(OwnedKind.Renting, -1, -1, "Renting & Boarding"));
            }

            if (townWorld != null)
            {
                int storeBuildingId = storeRuntime != null ? storeRuntime.StoreBuildingId : -1;
                for (int i = 0; i < townWorld.Buildings.Count; i++)
                {
                    PlacedBuilding building = townWorld.Buildings[i];
                    if (building == null
                        || !building.playerOwned
                        || building.id == storeBuildingId
                        || building.publicSiteRole == PublicSiteRole.TownHall)
                    {
                        continue;
                    }

                    OwnedKind kind = GetOwnedBuildingKind(building);
                    ownedEntries.Add(new OwnedEntry(kind, building.id, building.plotId, GetOwnedBusinessLabel(building)));
                }

                for (int i = 0; i < townWorld.Plots.Count; i++)
                {
                    TownPlot plot = townWorld.Plots[i];
                    if (plot == null || !plot.playerOwned || plot.buildingId >= 0)
                    {
                        continue;
                    }

                    ownedEntries.Add(new OwnedEntry(OwnedKind.Land, -1, plot.id, $"Plot {plot.id:000} | {plot.zone} | {plot.siteSizeCells.x}x{plot.siteSizeCells.y}"));
                }
            }

            StringBuilder signature = new();
            for (int i = 0; i < ownedEntries.Count; i++)
            {
                signature.Append(BuildOwnedEntryRowSignature(ownedEntries[i], i == selectedOwnedIndex)).Append('|');
            }

            return signature.ToString();
        }

        private void RebuildOwnedRows()
        {
            ClearSpawnedRows();
            ownedEntryButtons.Clear();
            if (view == null || view.PropertyListContent == null || view.PropertyRowTemplate == null)
            {
                return;
            }

            view.PropertyRowTemplate.gameObject.SetActive(false);
            for (int i = 0; i < ownedEntries.Count; i++)
            {
                int captured = i;
                OwnedEntry entry = ownedEntries[i];
                bool isSelected = i == selectedOwnedIndex;
                Button row = Instantiate(view.PropertyRowTemplate, view.PropertyListContent);
                row.name = $"PropertyRow_{i:00}_{SanitizeName(entry.Label)}";
                row.gameObject.SetActive(true);
                SetButtonLabel(row, BuildOwnedEntryRowLabel(entry, isSelected));
                row.onClick.AddListener(() => SelectOwnedEntry(captured));
                string tooltipTitle = entry.Kind == OwnedKind.Renting ? "Select Renting" : "Select Property";
                UITooltipRegistry.Attach(row, tooltipTitle, () => BuildOwnedEntryRowTooltip(entry, isSelected));
                ownedEntryButtons.Add(row);
                spawnedRows.Add(row.gameObject);
            }

            RefreshOwnedRowSelectionStyles();
        }

        private void RefreshOwnedRowSelectionStyles()
        {
            if (view == null)
            {
                return;
            }

            for (int i = 0; i < ownedEntryButtons.Count; i++)
            {
                view.SetButtonSelected(ownedEntryButtons[i], i == selectedOwnedIndex);
            }
        }

        private string BuildOwnedEntryRowSignature(OwnedEntry entry, bool isSelected)
        {
            return $"{entry.Signature}:{BuildOwnedEntryRowLabel(entry, isSelected)}";
        }

        private string BuildOwnedEntryRowLabel(OwnedEntry entry, bool isSelected)
        {
            string title = BuildOwnedEntryRowTitle(entry);
            string status = BuildOwnedEntryRowStatus(entry, isSelected);
            return string.IsNullOrWhiteSpace(status) ? title : $"{title} — {status}";
        }

        private string BuildOwnedEntryRowTitle(OwnedEntry entry)
        {
            if (entry.Kind == OwnedKind.Land)
            {
                TownPlot plot = GetPlot(entry.PlotId);
                return plot != null ? $"Plot {plot.id:000}" : entry.Label;
            }

            return string.IsNullOrWhiteSpace(entry.Label) ? "Property" : entry.Label;
        }

        private string BuildOwnedEntryRowStatus(OwnedEntry entry, bool isSelected)
        {
            if (entry.Kind == OwnedKind.Store)
            {
                return BuildOwnedStoreRowStatus();
            }

            if (entry.Kind == OwnedKind.Renting)
            {
                return BuildOwnedRentingRowStatus();
            }

            TownPlot plot = GetPlot(entry.PlotId);
            PlacedBuilding building = GetBuilding(entry.BuildingId);
            BusinessInstanceState business = GetBusinessForBuilding(entry.BuildingId);
            if (entry.Kind == OwnedKind.Land && plot != null)
            {
                return BuildOwnedLandRowStatus(plot, entry, isSelected);
            }

            if (business != null)
            {
                return BuildManagedBusinessRowStatus(business);
            }

            if (building != null && IsPlayerOwnedShellWithDefinition(building))
            {
                return BuildVacantShellRowStatus(building, entry, isSelected);
            }

            if (building != null && IsResidentialHolding(building))
            {
                return BuildResidentialHoldingRowStatus(building, entry, isSelected);
            }

            if (building != null)
            {
                return "Held property";
            }

            return string.Empty;
        }

        private string BuildOwnedEntryRowTooltip(OwnedEntry entry, bool isSelected)
        {
            string body = $"Show details and actions for {BuildOwnedEntryRowTitle(entry)}.";
            string status = BuildOwnedEntryRowStatus(entry, isSelected);
            if (!string.IsNullOrWhiteSpace(status))
            {
                body += $" Current read: {status}.";
            }

            if (isSelected)
            {
                if (TryGetCurrentFirstSessionPropertyCoaching(out string currentRead, out string nextAction, out string blocker, out _, out _))
                {
                    if (!string.IsNullOrWhiteSpace(currentRead))
                    {
                        body += $" {currentRead.Trim().TrimEnd('.')}.";
                    }

                    if (!string.IsNullOrWhiteSpace(blocker))
                    {
                        body += $" Blocked: {CompactReason(blocker)}.";
                    }

                    if (!string.IsNullOrWhiteSpace(nextAction))
                    {
                        body += $" Next: {nextAction.Trim().TrimEnd('.')}.";
                    }
                }

                return body.Trim();
            }

            PlacedBuilding building = GetBuilding(entry.BuildingId);
            body += entry.Kind switch
            {
                OwnedKind.Land => " Select the parcel to inspect buildability and choose the next development step.",
                OwnedKind.Store => " Select the store to inspect operations, staffing, and cash pressure.",
                OwnedKind.Renting => " Select renting to inspect vacancies, applicants, and boarding demand.",
                OwnedKind.Business => building != null && IsPlayerOwnedShellWithDefinition(building)
                    ? " Select the shell to inspect startup readiness and next steps."
                    : " Select the business to inspect operations and recommended action.",
                OwnedKind.Holding => building != null && IsResidentialHolding(building)
                    ? " Select the residence to inspect occupants and upgrade posture."
                    : " Select the property to inspect its current state.",
                _ => string.Empty
            };
            return body.Trim();
        }

        private string BuildOwnedStoreRowStatus()
        {
            BusinessRuntimeState runtime = storeRuntime != null ? storeRuntime.RuntimeState : null;
            if (runtime == null)
            {
                return "Store unavailable";
            }

            string state = BuildStoreMainProblem(runtime);
            return $"{state} | Staff {runtime.FilledWorkerCount}/{runtime.TargetWorkerCount}";
        }

        private string BuildOwnedRentingRowStatus()
        {
            PopulationState population = GetRefreshedPopulationForRenting();
            if (population == null)
            {
                return "Records unavailable";
            }

            int openBoardingBeds = Mathf.Max(0, population.boardingHouseCapacity - population.boardingHouseUsed);
            return $"Vacancies {population.playerOwnedRentalVacancyCount} | Applicants {population.rentalApplicantCount} | Open beds {openBoardingBeds}";
        }

        private string BuildOwnedLandRowStatus(TownPlot plot, OwnedEntry entry, bool isSelected)
        {
            OwnedPlotBuildabilityState buildability = FindBuildabilityState(plot.id);
            if (buildability == null)
            {
                return "Planning unavailable";
            }

            if (!buildability.playerOwned)
            {
                return "Not in portfolio";
            }

            if (!buildability.empty)
            {
                return "Occupied site";
            }

            if (!buildability.buildable)
            {
                return string.IsNullOrWhiteSpace(buildability.reason)
                    ? "Blocked site"
                    : $"Blocked — {CompactActionReason(buildability.reason)}";
            }

            if (isSelected && TryGetPropertyProjectActionState(entry, out PropertyProjectActionState selectedState))
            {
                return BuildPropertyActionRowStatus(selectedState);
            }

            bool hasHousePlan = acquisitionMarket != null && acquisitionMarket.GetHouseBuildOptionsForOwnedPlot(plot.id)?.Count > 0;
            bool hasBusinessPlan = TryGetAnyBusinessShellPlanForRow(plot.id, out _);
            if (!hasHousePlan && !hasBusinessPlan)
            {
                return "Empty parcel | No valid plans";
            }

            if (hasHousePlan && hasBusinessPlan)
            {
                return "Empty parcel | House or business";
            }

            return hasBusinessPlan ? "Empty parcel | Business-capable" : "Empty parcel | House-capable";
        }

        private string BuildVacantShellRowStatus(PlacedBuilding building, OwnedEntry entry, bool isSelected)
        {
            if (acquisitionMarket == null)
            {
                return "Startup unavailable";
            }

            if (isSelected && TryGetPropertyProjectActionState(entry, out PropertyProjectActionState selectedState))
            {
                return BuildPropertyActionRowStatus(selectedState);
            }

            if (!TryGetAnyBusinessActivationRowState(building.id, out BusinessActivationCandidateState candidate, out bool ready, out string reason))
            {
                return string.IsNullOrWhiteSpace(reason) ? "No valid start" : $"No valid start — {CompactActionReason(reason)}";
            }

            if (ready)
            {
                return $"Startup ready — {CompactSelectionLabel(candidate.displayName)}";
            }

            return string.IsNullOrWhiteSpace(reason)
                ? $"Start blocked — {CompactSelectionLabel(candidate.displayName)}"
                : $"Start blocked — {CompactActionReason(reason)}";
        }

        private string BuildResidentialHoldingRowStatus(PlacedBuilding building, OwnedEntry entry, bool isSelected)
        {
            HouseholdState household = GetResidentHousehold(building.id);
            if (household == null)
            {
                return "No resident household";
            }

            if (isSelected && TryGetPropertyProjectActionState(entry, out PropertyProjectActionState selectedState) && selectedState.ActionLabel == "Build Upgrade")
            {
                string actionStatus = BuildPropertyActionRowStatus(selectedState);
                return $"{household.householdName} | {actionStatus}";
            }

            int builtCount = HouseholdUpgradeCatalog.CountBuiltUpgrades(household);
            return builtCount >= HouseholdUpgradeCatalog.Count
                ? $"{household.householdName} | Fully improved"
                : $"{household.householdName} | Upgrade review";
        }

        private static string BuildManagedBusinessRowStatus(BusinessInstanceState business)
        {
            BusinessRuntimeState runtime = business != null ? business.RuntimeState : null;
            if (runtime == null)
            {
                return "Business unavailable";
            }

            string state = !string.IsNullOrWhiteSpace(runtime.LastWeeklyBlockedReason)
                ? "Blocked"
                : runtime.StockHealth01 < 0.45f
                    ? "Restock pressure"
                    : runtime.FilledWorkerCount < runtime.TargetWorkerCount
                        ? "Understaffed"
                        : "Operating";
            return $"{state} | Staff {runtime.FilledWorkerCount}/{runtime.TargetWorkerCount}";
        }

        private static string BuildPropertyActionRowStatus(PropertyProjectActionState state)
        {
            string selected = CompactSelectionLabel(state.SelectedLabel);
            if (state.IsComplete)
            {
                return string.IsNullOrWhiteSpace(selected)
                    ? $"{state.ActionLabel} complete"
                    : $"{selected} complete";
            }

            if (state.IsBlocked)
            {
                return string.IsNullOrWhiteSpace(selected)
                    ? $"{state.ActionLabel} blocked"
                    : $"{selected} blocked";
            }

            return string.IsNullOrWhiteSpace(selected)
                ? $"{state.ActionLabel} ready"
                : $"{selected} ready";
        }

        private bool TryGetAnyBusinessShellPlanForRow(int plotId, out string businessDisplayName)
        {
            businessDisplayName = string.Empty;
            if (acquisitionMarket == null)
            {
                return false;
            }

            int candidateCount = acquisitionMarket.GetBusinessDevelopmentCandidateCount();
            if (candidateCount <= 0)
            {
                return false;
            }

            int startIndex = acquisitionMarket.GetFirstEligibleBusinessDevelopmentCandidateIndex();
            if (startIndex < 0)
            {
                startIndex = 0;
            }

            for (int offset = 0; offset < candidateCount; offset++)
            {
                int index = WrapIndex(startIndex + offset, candidateCount);
                if (!acquisitionMarket.TryGetBusinessDevelopmentCandidate(index, out BusinessActivationCandidateState candidate) || candidate == null)
                {
                    continue;
                }

                IReadOnlyList<BuildingDefinition> shellOptions = acquisitionMarket.GetBusinessBuildOptionsForOwnedPlot(plotId, index);
                if (shellOptions != null && shellOptions.Count > 0)
                {
                    businessDisplayName = candidate.displayName ?? string.Empty;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetAnyBusinessActivationRowState(int buildingId, out BusinessActivationCandidateState candidate, out bool ready, out string reason)
        {
            candidate = null;
            ready = false;
            reason = string.Empty;
            if (acquisitionMarket == null)
            {
                reason = "Startup unavailable.";
                return false;
            }

            int candidateCount = acquisitionMarket.GetBusinessActivationCandidateCount(buildingId);
            if (candidateCount <= 0)
            {
                reason = "No supported business starts.";
                return false;
            }

            int startIndex = acquisitionMarket.GetFirstEligibleBusinessActivationCandidateIndex(buildingId);
            if (startIndex < 0)
            {
                startIndex = 0;
            }

            string firstReason = string.Empty;
            for (int offset = 0; offset < candidateCount; offset++)
            {
                int index = WrapIndex(startIndex + offset, candidateCount);
                if (!acquisitionMarket.TryGetBusinessActivationCandidate(buildingId, index, out candidate) || candidate == null)
                {
                    continue;
                }

                if (!candidate.eligible)
                {
                    if (string.IsNullOrWhiteSpace(firstReason))
                    {
                        firstReason = string.IsNullOrWhiteSpace(candidate.reason)
                            ? $"{candidate.displayName} cannot start at this site."
                            : candidate.reason;
                    }
                    continue;
                }

                if (!acquisitionMarket.TryGetBusinessActivationQuote(buildingId, index, out ConstructionInputQuote quote, out string quoteMessage) || quote == null)
                {
                    if (string.IsNullOrWhiteSpace(firstReason))
                    {
                        firstReason = string.IsNullOrWhiteSpace(quoteMessage) ? "Startup quote unavailable." : quoteMessage;
                    }
                    continue;
                }

                if (quote.CanProceed)
                {
                    ready = true;
                    reason = string.Empty;
                    return true;
                }

                if (string.IsNullOrWhiteSpace(firstReason))
                {
                    firstReason = quote.BuildMissingSummary();
                }
            }

            reason = firstReason;
            return candidate != null;
        }

        private void ClearSpawnedRows()
        {
            for (int i = 0; i < spawnedRows.Count; i++)
            {
                if (spawnedRows[i] != null)
                {
                    Destroy(spawnedRows[i]);
                }
            }

            spawnedRows.Clear();
        }

        private void ClearFinanceRows()
        {
            for (int i = 0; i < spawnedFinanceRows.Count; i++)
            {
                if (spawnedFinanceRows[i] != null)
                {
                    Destroy(spawnedFinanceRows[i]);
                }
            }

            spawnedFinanceRows.Clear();
            financeBusinessButtons.Clear();
            financeBusinessSignature = string.Empty;
        }

        private void SelectOwnedEntry(int index)
        {
            selectedOwnedIndex = Mathf.Clamp(index, 0, Mathf.Max(0, ownedEntries.Count - 1));
            selectedBusinessActivationBuildingId = -1;
            currentTab = ManagementPanelTab.Properties;
            FocusSelectedProperty();
            Refresh();
        }

        private void FocusSelectedProperty()
        {
            if (ownedEntries.Count == 0 || strategyCamera == null || townWorld == null || townWorld.Grid == null)
            {
                panelStatus = "No strategy camera or generated town grid available to focus.";
                return;
            }

            OwnedEntry selected = ownedEntries[Mathf.Clamp(selectedOwnedIndex, 0, ownedEntries.Count - 1)];
            if (!TryGetWorldCenter(selected, out Vector3 center))
            {
                panelStatus = "Selected property has no valid world center yet.";
                return;
            }

            strategyCamera.SetFocus(center);
            panelStatus = $"Focused {selected.Label}.";
        }

        private bool TryGetWorldCenter(OwnedEntry entry, out Vector3 center)
        {
            PlacedBuilding building = GetBuilding(entry.BuildingId);
            if (building != null)
            {
                center = RectCenter(building.footprint);
                return true;
            }

            TownPlot plot = GetPlot(entry.PlotId);
            if (plot != null)
            {
                center = RectCenter(plot.bounds);
                return true;
            }

            center = default;
            return false;
        }

        private bool TryGetListingWorldCenter(AcquisitionListing listing, out Vector3 center)
        {
            if (listing != null && listing.buildingId >= 0)
            {
                PlacedBuilding building = GetBuilding(listing.buildingId);
                if (building != null)
                {
                    center = RectCenter(building.footprint);
                    return true;
                }
            }

            if (listing != null && listing.plotId >= 0)
            {
                TownPlot plot = GetPlot(listing.plotId);
                if (plot != null)
                {
                    center = RectCenter(plot.bounds);
                    return true;
                }
            }

            center = default;
            return false;
        }

        private Vector3 RectCenter(GridRect rect)
        {
            Vector3 min = townWorld.Grid.CoordToWorldCenter(new GridCoord(rect.xMin, rect.zMin));
            Vector3 max = townWorld.Grid.CoordToWorldCenter(new GridCoord(rect.xMaxInclusive, rect.zMaxInclusive));
            return (min + max) * 0.5f;
        }

        private bool IsListedPlot(int plotId)
        {
            if (acquisitionMarket == null)
            {
                return false;
            }

            for (int i = 0; i < acquisitionMarket.LandListings.Count; i++)
            {
                if (acquisitionMarket.LandListings[i].plotId == plotId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsListedBusiness(int buildingId)
        {
            if (acquisitionMarket == null)
            {
                return false;
            }

            for (int i = 0; i < acquisitionMarket.BusinessListings.Count; i++)
            {
                if (acquisitionMarket.BusinessListings[i].buildingId == buildingId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetSelectedOwnedEntry(out OwnedEntry entry)
        {
            if (ownedEntries.Count == 0)
            {
                entry = default;
                return false;
            }

            selectedOwnedIndex = Mathf.Clamp(selectedOwnedIndex, 0, ownedEntries.Count - 1);
            entry = ownedEntries[selectedOwnedIndex];
            return true;
        }

        // First-session guidance also reads acquisition coaching from the live panel contract
        // so the HUD stays aligned with the same selected lead and action desk the player sees.
        public bool TryGetCurrentFirstSessionAcquisitionCoaching(
            out string currentRead,
            out string nextAction,
            out string blocker,
            out string selectedActionLabel)
        {
            currentRead = string.Empty;
            nextAction = string.Empty;
            blocker = string.Empty;
            selectedActionLabel = string.Empty;

            if (!visible || currentTab != ManagementPanelTab.Acquisitions || acquisitionMarket == null)
            {
                return false;
            }

            if (acquisitionSection != AcquisitionMarketSection.Land)
            {
                currentRead = "Acquisitions open, but the market is not on Land.";
                blocker = "Switch the market to Land for the first expansion parcel.";
                nextAction = blocker;
                return true;
            }

            int listingCount = acquisitionMarket.LandListings != null ? acquisitionMarket.LandListings.Count : 0;
            if (listingCount <= 0)
            {
                currentRead = "Land market open, but no live parcel is listed yet.";
                blocker = "Stay on Land and wait for a live land listing.";
                nextAction = blocker;
                return true;
            }

            selectedActionLabel = NormalizeAcquisitionActionLabel(acquisitionMarket.GetSelectedAcquisitionActionLabel(acquisitionSection));
            if (!acquisitionMarket.TryGetSelectedListing(acquisitionSection, out _))
            {
                currentRead = "Land market open, but no parcel is selected.";
                blocker = "Use the listing controls to choose a live land lead.";
                nextAction = blocker;
                return true;
            }

            currentRead = BuildSelectedAcquisitionCurrentRead(selectedActionLabel);
            nextAction = BuildAcquisitionActionGuidance(selectedActionLabel);
            return true;
        }

        // First-session guidance reads from the same selected-property contract as the panel.
        // This avoids HUD text drifting away from the property detail/action state.
        public bool TryGetCurrentFirstSessionPropertyCoaching(
            out string currentRead,
            out string nextAction,
            out string blocker,
            out bool readyForShell,
            out bool readyForStartup)
        {
            currentRead = string.Empty;
            nextAction = string.Empty;
            blocker = string.Empty;
            readyForShell = false;
            readyForStartup = false;

            if (!TryGetSelectedOwnedEntry(out OwnedEntry selected))
            {
                return false;
            }

            if (selected.Kind == OwnedKind.Land)
            {
                if (selectedOwnedLandDevelopmentIntent != OwnedLandDevelopmentIntent.Business)
                {
                    currentRead = "Owned plot selected, but it is still set to House intent.";
                    blocker = "Switch the plot to Business for the first expansion objective.";
                    nextAction = blocker;
                    return true;
                }

                if (TryGetSelectedBusinessConstructionBlockedReason(selected.PlotId, out string reason))
                {
                    currentRead = "Owned plot selected, but shell construction is blocked.";
                    blocker = FirstSessionGuidanceText.BuildConstructionBlockerNextStep(reason);
                    nextAction = blocker;
                    return true;
                }

                currentRead = "Owned plot selected and ready for shell construction.";
                nextAction = "Press Build Shell, or cycle the business and shell.";
                readyForShell = true;
                return true;
            }

            if (selected.Kind == OwnedKind.Business && IsStartableOwnedShell(selected))
            {
                if (TryGetSelectedStartupBlockedReason(selected.BuildingId, out string reason))
                {
                    currentRead = "Vacant shell selected, but startup is blocked.";
                    blocker = FirstSessionGuidanceText.BuildConstructionBlockerNextStep(reason);
                    nextAction = blocker;
                    return true;
                }

                currentRead = "Vacant shell selected and ready for startup.";
                nextAction = "Press Start Business, or cycle the business type.";
                readyForStartup = true;
                return true;
            }

            if (selected.Kind == OwnedKind.Business)
            {
                currentRead = "Operating business selected.";
                nextAction = "Review staffing, cash, and operating pressure before the next expansion move.";
                return true;
            }

            if (selected.Kind == OwnedKind.Holding)
            {
                currentRead = "Residence selected.";
                nextAction = "Inspect the site, then return to the expansion parcel when you are ready to build.";
                return true;
            }

            if (selected.Kind == OwnedKind.Store)
            {
                currentRead = "General Store selected.";
                nextAction = "Use the store to stabilize cash, staffing, and sales before expanding.";
                return true;
            }

            if (selected.Kind == OwnedKind.Renting)
            {
                currentRead = "Rental income entry selected.";
                nextAction = "Review rental cash flow, then return to the expansion parcel when you are ready.";
                return true;
            }

            return false;
        }

        public bool TryGetCurrentFirstSessionBlockerGuidance(out string message)
        {
            message = string.Empty;
            return TryGetCurrentFirstSessionPropertyCoaching(out _, out _, out message, out _, out _)
                && !string.IsNullOrWhiteSpace(message);
        }

        private void SelectOwnedBuildingIfPresent(int buildingId)
        {
            for (int i = 0; i < ownedEntries.Count; i++)
            {
                if (ownedEntries[i].BuildingId == buildingId)
                {
                    selectedOwnedIndex = i;
                    selectedBusinessActivationBuildingId = buildingId;
                    return;
                }
            }
        }

        private void SelectOwnedPlotIfPresent(int plotId)
        {
            for (int i = 0; i < ownedEntries.Count; i++)
            {
                if (ownedEntries[i].PlotId == plotId)
                {
                    selectedOwnedIndex = i;
                    selectedBusinessActivationBuildingId = ownedEntries[i].BuildingId;
                    return;
                }
            }
        }

        private OwnershipWorkflowController EnsureWorkflowController()
        {
            AutoWireSources();
            return workflowController;
        }

        private AcquisitionSellerMeetingController EnsureSellerMeetingController()
        {
            AutoWireSources();
            return sellerMeetingController;
        }

        private int GetBuildOptionCount(int plotId)
        {
            OwnedPlotBuildabilityState buildability = FindBuildabilityState(plotId);
            return buildability != null && buildability.buildOptions != null ? buildability.buildOptions.Count : 0;
        }

        private int GetHouseBuildOptionCount(int plotId)
        {
            IReadOnlyList<BuildingDefinition> options = acquisitionMarket != null
                ? acquisitionMarket.GetHouseBuildOptionsForOwnedPlot(plotId)
                : null;
            return options != null ? options.Count : 0;
        }

        private int GetBusinessDevelopmentCandidateCount()
        {
            return acquisitionMarket != null ? acquisitionMarket.GetBusinessDevelopmentCandidateCount() : 0;
        }

        private int GetBusinessBuildOptionCount(int plotId)
        {
            IReadOnlyList<BuildingDefinition> options = acquisitionMarket != null
                ? acquisitionMarket.GetBusinessBuildOptionsForOwnedPlot(plotId, selectedBusinessActivationIndex)
                : null;
            return options != null ? options.Count : 0;
        }

        private void EnsureOwnedLandBusinessSelection(int plotId)
        {
            int candidateCount = GetBusinessDevelopmentCandidateCount();
            if (selectedBusinessDevelopmentPlotId != plotId)
            {
                int firstEligibleIndex = acquisitionMarket != null
                    ? acquisitionMarket.GetFirstEligibleBusinessDevelopmentCandidateIndex()
                    : -1;
                selectedBusinessActivationIndex = firstEligibleIndex >= 0
                    ? firstEligibleIndex
                    : WrapIndex(selectedBusinessActivationIndex, candidateCount);
                selectedBusinessBuildOptionIndex = 0;
                selectedBusinessDevelopmentPlotId = plotId;
            }
            else
            {
                selectedBusinessActivationIndex = WrapIndex(selectedBusinessActivationIndex, candidateCount);
            }

            selectedBusinessActivationBuildingId = -1;
        }

        private void EnsureBusinessActivationSelection(int buildingId)
        {
            if (acquisitionMarket == null || selectedBusinessActivationBuildingId == buildingId)
            {
                return;
            }

            int firstEligibleIndex = acquisitionMarket.GetFirstEligibleBusinessActivationCandidateIndex(buildingId);
            selectedBusinessActivationIndex = firstEligibleIndex >= 0 ? firstEligibleIndex : 0;
            selectedBusinessActivationBuildingId = buildingId;
        }

        private int GetBusinessActivationCandidateCount(int buildingId)
        {
            return acquisitionMarket != null ? acquisitionMarket.GetBusinessActivationCandidateCount(buildingId) : 0;
        }

        private string BuildConstructionInputAvailabilityText()
        {
            return acquisitionMarket != null ? acquisitionMarket.BuildConstructionInputAvailabilityText() : string.Empty;
        }

        private ExpansionReadinessService CreateExpansionReadinessService()
        {
            return new ExpansionReadinessService(
                acquisitionMarket,
                playerPortfolio,
                playerDebtManager,
                storeRuntime,
                sharedBusinessRuntime);
        }

        private string BuildExpansionCashLine()
        {
            return CreateExpansionReadinessService().BuildExpansionCashText();
        }

        private static void AppendExpansionReadiness(StringBuilder builder, ExpansionReadinessResult result)
        {
            if (builder == null || result == null)
            {
                return;
            }

            builder.AppendLine();
            builder.Append(result.BuildSummaryText());
        }

        private int GetHouseholdUpgradeOptionCount()
        {
            return acquisitionMarket != null
                ? acquisitionMarket.GetHouseholdUpgradeOptionCount()
                : HouseholdUpgradeCatalog.Count;
        }

        private bool TryGetSelectedHouseholdUpgradeDefinition(out HouseholdUpgradeDefinition definition)
        {
            if (acquisitionMarket != null && acquisitionMarket.TryGetHouseholdUpgradeDefinition(selectedHouseholdUpgradeIndex, out definition))
            {
                return definition != null;
            }

            definition = HouseholdUpgradeCatalog.GetByIndex(selectedHouseholdUpgradeIndex);
            return definition != null;
        }

        private bool IsSelectedHouseholdUpgradeBuilt(int buildingId)
        {
            HouseholdState household = GetResidentHousehold(buildingId);
            return household != null
                && TryGetSelectedHouseholdUpgradeDefinition(out HouseholdUpgradeDefinition definition)
                && definition != null
                && HouseholdUpgradeCatalog.HasBuiltUpgrade(household, definition.Kind);
        }

        private bool TryGetSelectedHouseholdUpgradeBlockedReason(int buildingId, out string reason)
        {
            reason = string.Empty;
            if (acquisitionMarket == null)
            {
                reason = "Acquisitions unavailable.";
                return true;
            }

            if (!TryGetSelectedHouseholdUpgradeDefinition(out HouseholdUpgradeDefinition definition) || definition == null)
            {
                reason = "No household upgrade is selected.";
                return true;
            }

            if (IsSelectedHouseholdUpgradeBuilt(buildingId))
            {
                reason = $"{definition.DisplayName} is already built.";
                return true;
            }

            if (!acquisitionMarket.TryGetHouseholdUpgradeQuote(buildingId, selectedHouseholdUpgradeIndex, out ConstructionInputQuote quote, out string quoteMessage))
            {
                reason = string.IsNullOrWhiteSpace(quoteMessage) ? "Household upgrade quote unavailable." : quoteMessage;
                return true;
            }

            if (quote == null)
            {
                reason = "Household upgrade quote unavailable.";
                return true;
            }

            if (!quote.CanProceed)
            {
                reason = quote.BuildMissingSummary();
                return true;
            }

            return false;
        }

        private HouseholdState GetResidentHousehold(int buildingId)
        {
            populationManager ??= FindAnyObjectByType<PopulationManager>();
            return populationManager != null ? populationManager.GetHouseholdByHomeBuildingId(buildingId) : null;
        }

        private bool IsSelectedActivationCandidateEligible(int buildingId)
        {
            return acquisitionMarket != null
                && acquisitionMarket.TryGetBusinessActivationCandidate(buildingId, selectedBusinessActivationIndex, out BusinessActivationCandidateState candidate)
                && candidate != null
                && candidate.eligible;
        }

        private bool TryGetSelectedHouseConstructionBlockedReason(int plotId, out string reason)
        {
            reason = string.Empty;
            if (acquisitionMarket == null)
            {
                reason = "Acquisitions unavailable.";
                return true;
            }

            int optionCount = GetHouseBuildOptionCount(plotId);
            selectedBuildOptionIndex = WrapIndex(selectedBuildOptionIndex, optionCount);
            if (optionCount <= 0)
            {
                reason = "No house-oriented shell plan physically fits this plot.";
                return true;
            }

            if (!acquisitionMarket.TryGetHouseConstructionQuoteForOwnedPlotOption(plotId, selectedBuildOptionIndex, out ConstructionInputQuote quote, out string quoteMessage))
            {
                reason = string.IsNullOrWhiteSpace(quoteMessage) ? "House construction quote unavailable." : quoteMessage;
                return true;
            }

            if (quote == null)
            {
                reason = "House construction quote unavailable.";
                return true;
            }

            if (!quote.CanProceed)
            {
                reason = quote.BuildMissingSummary();
                return true;
            }

            return false;
        }

        private bool TryGetSelectedBusinessConstructionBlockedReason(int plotId, out string reason)
        {
            reason = string.Empty;
            if (acquisitionMarket == null)
            {
                reason = "Acquisitions unavailable.";
                return true;
            }

            EnsureOwnedLandBusinessSelection(plotId);
            int candidateCount = GetBusinessDevelopmentCandidateCount();
            selectedBusinessActivationIndex = WrapIndex(selectedBusinessActivationIndex, candidateCount);
            if (candidateCount <= 0)
            {
                reason = "No supported business starts.";
                return true;
            }

            if (!acquisitionMarket.TryGetBusinessDevelopmentCandidate(selectedBusinessActivationIndex, out BusinessActivationCandidateState candidate) || candidate == null)
            {
                reason = "No business type is selected.";
                return true;
            }

            if (!candidate.eligible)
            {
                reason = string.IsNullOrWhiteSpace(candidate.reason)
                    ? $"{candidate.displayName} is not available."
                    : candidate.reason;
                return true;
            }

            int shellCount = GetBusinessBuildOptionCount(plotId);
            selectedBusinessBuildOptionIndex = WrapIndex(selectedBusinessBuildOptionIndex, shellCount);
            if (shellCount <= 0)
            {
                reason = $"No shell plan physically fits this plot for {candidate.displayName}.";
                return true;
            }

            if (!acquisitionMarket.TryGetBusinessConstructionQuoteForOwnedPlotOption(plotId, selectedBusinessActivationIndex, selectedBusinessBuildOptionIndex, out ConstructionInputQuote quote, out string quoteMessage))
            {
                reason = string.IsNullOrWhiteSpace(quoteMessage) ? "Business shell construction quote unavailable." : quoteMessage;
                return true;
            }

            if (quote == null)
            {
                reason = "Business shell construction quote unavailable.";
                return true;
            }

            if (!quote.CanProceed)
            {
                reason = quote.BuildMissingSummary();
                return true;
            }

            return false;
        }

        private bool IsSelectedConstructionQuoteAvailable(int plotId)
        {
            return acquisitionMarket != null
                && acquisitionMarket.TryGetConstructionQuoteForOwnedPlotOption(plotId, selectedBuildOptionIndex, out ConstructionInputQuote quote, out _)
                && quote != null
                && quote.CanProceed;
        }

        private bool IsSelectedActivationQuoteAvailable(int buildingId)
        {
            return acquisitionMarket != null
                && acquisitionMarket.TryGetBusinessActivationQuote(buildingId, selectedBusinessActivationIndex, out ConstructionInputQuote quote, out _)
                && quote != null
                && quote.CanProceed;
        }

        private BusinessInstanceState GetBusinessForBuilding(int buildingId)
        {
            return sharedBusinessRuntime != null ? sharedBusinessRuntime.FindByBuildingId(buildingId) : null;
        }

        private OwnedKind GetOwnedBuildingKind(PlacedBuilding building)
        {
            if (building == null)
            {
                return OwnedKind.Holding;
            }

            return GetBusinessForBuilding(building.id) != null || IsPlayerOwnedShellWithDefinition(building)
                ? OwnedKind.Business
                : OwnedKind.Holding;
        }

        private string GetOwnedBusinessLabel(PlacedBuilding building)
        {
            if (building == null)
            {
                return "Unassigned Property";
            }

            BusinessInstanceState business = GetBusinessForBuilding(building.id);
            if (business != null)
            {
                return business.RuntimeDisplayName;
            }

            if (IsPlayerOwnedShellWithDefinition(building))
            {
                return $"Vacant Building {building.id:000}";
            }

            return IsResidentialHolding(building) ? $"Home {building.id:000}" : $"Property {building.id:000}";
        }

        private string BuildNonStoreOperationsText(OwnedEntry entry)
        {
            BusinessInstanceState business = GetBusinessForBuilding(entry.BuildingId);
            if (business != null)
            {
                return sharedBusinessRuntime != null
                ? BuildBusinessOperationsWithPriority(business)
                : "Business unavailable.";
            }

            return IsStartableOwnedShell(entry)
                ? BuildBusinessActivationText(entry.BuildingId)
                : BuildResidentialHoldingText(entry.BuildingId);
        }

        private string BuildBusinessOperationsWithPriority(BusinessInstanceState business)
        {
            if (business == null || business.RuntimeState == null)
            {
                return "Business status unavailable.";
            }

            BusinessRuntimeState runtime = business.RuntimeState;
            StringBuilder builder = new();
            builder.AppendLine("<b>Business Status</b>");
            builder.AppendLine($"Condition: {ColorizeStatus(BuildBusinessSummaryStatus(business))}");
            builder.AppendLine($"Pressure: {BuildBusinessMainProblem(business)}");
            builder.AppendLine($"Manager Read: {BuildBusinessRecommendedAction(business)}");
            builder.AppendLine($"Operations: Cash {FormatMoney(runtime.CurrentCashCents)} | Staff {runtime.FilledWorkerCount}/{runtime.TargetWorkerCount} | Efficiency {business.OperatingEfficiency01:P0}");
            if (sharedBusinessRuntime != null)
            {
                string captureLine = sharedBusinessRuntime.BuildBusinessLocalMarketCaptureLine(business);
                if (!string.IsNullOrWhiteSpace(captureLine))
                {
                    builder.AppendLine(captureLine);
                }
            }

            if (sharedBusinessRuntime != null)
            {
                builder.AppendLine();
                builder.AppendLine("<b>Town Commerce</b>");
                builder.AppendLine(sharedBusinessRuntime.BuildTownCommerceHeadline());
                builder.AppendLine(sharedBusinessRuntime.BuildTownCommerceLedgerSummary());
                builder.AppendLine(sharedBusinessRuntime.BuildTownDecisionClimateHeadline());
                builder.AppendLine(sharedBusinessRuntime.BuildTownProcessClimateSummary());
            }

            builder.AppendLine();
            builder.AppendLine("<b>Management</b>");
            if (business.BusinessType == BusinessType.BoardingHouse && sharedBusinessRuntime != null)
            {
                builder.AppendLine("Rental & Boarding");
                builder.AppendLine(sharedBusinessRuntime.BuildBoardingHouseOccupancyLine(business));
                builder.AppendLine(BuildBoardingHouseDemandLine());
                builder.AppendLine();
            }

            builder.Append(sharedBusinessRuntime.BuildManagementText(business, selectedWorkerCandidateIndex));
            return builder.ToString();
        }

        private string BuildBusinessMainProblem(BusinessInstanceState business)
        {
            if (business == null || business.RuntimeState == null)
            {
                return "Business records unavailable.";
            }

            BusinessRuntimeState runtime = business.RuntimeState;
            BusinessPortfolioSummary summary = BuildBusinessPortfolioSummary(business);
            if (summary.ContinuityStatus == BusinessContinuityStatus.MissedPayroll)
            {
                return "Payroll was missed and business liabilities increased.";
            }

            if (summary.ContinuityStatus == BusinessContinuityStatus.CashBelowSurvivalReserve)
            {
                return summary.ContinuitySummary;
            }

            if (summary.ContinuityStatus == BusinessContinuityStatus.Blocked)
            {
                return CompactReason(business.LastWeeklyBlockedReason);
            }

            if (summary.ContinuityStatus == BusinessContinuityStatus.MissingRequiredRoles
                || summary.ContinuityStatus == BusinessContinuityStatus.OwnerOperatorFallback
                || summary.ContinuityStatus == BusinessContinuityStatus.Understaffed
                || summary.ContinuityStatus == BusinessContinuityStatus.Pressured)
            {
                return summary.RoleCoverage.BuildSummary();
            }

            if (runtime.CurrentCashCents <= 0)
            {
                return "Business cash is empty.";
            }

            if (business.OperatingEfficiency01 < 0.5f)
            {
                return "Operating efficiency is low.";
            }

            if (business.Capacity != null
                && !string.IsNullOrWhiteSpace(business.Capacity.BottleneckDisplayName)
                && !string.Equals(business.Capacity.BottleneckDisplayName, "none", StringComparison.OrdinalIgnoreCase))
            {
                return $"{business.Capacity.BottleneckDisplayName} is the current bottleneck.";
            }

            return "No urgent issue.";
        }

        private string BuildBusinessRecommendedAction(BusinessInstanceState business)
        {
            if (business == null || business.RuntimeState == null)
            {
                return "Refresh business records.";
            }

            BusinessRuntimeState runtime = business.RuntimeState;
            BusinessPortfolioSummary summary = BuildBusinessPortfolioSummary(business);
            if (summary.ContinuityStatus == BusinessContinuityStatus.CashBelowSurvivalReserve
                || summary.ContinuityStatus == BusinessContinuityStatus.MissedPayroll)
            {
                return "Add liquid cash to steady the business before the next payroll or operating step.";
            }

            if (summary.ContinuityStatus == BusinessContinuityStatus.MissingRequiredRoles
                || summary.ContinuityStatus == BusinessContinuityStatus.OwnerOperatorFallback
                || summary.ContinuityStatus == BusinessContinuityStatus.Understaffed
                || summary.ContinuityStatus == BusinessContinuityStatus.Pressured)
            {
                return "Pick a worker with Prev/Next Worker, then Hire.";
            }

            if (runtime.CurrentCashCents <= 0)
            {
                return "Add liquid cash to steady the store before the next reorder or payroll step.";
            }

            if (business.OperatingEfficiency01 < 0.5f)
            {
                return "Check staffing and bottlenecks before speeding up.";
            }

            if (business.Capacity != null
                && !string.IsNullOrWhiteSpace(business.Capacity.BottleneckDisplayName)
                && !string.Equals(business.Capacity.BottleneckDisplayName, "none", StringComparison.OrdinalIgnoreCase))
            {
                return "Use Control/Policy, then watch inventory and throughput.";
            }

            return "Keep operating, or adjust Control/Policy for the next week.";
        }

        private string BuildResidentialHoldingText(int buildingId)
        {
            PlacedBuilding building = GetBuilding(buildingId);
            string display = building != null && building.definition != null
                ? building.definition.DisplayName
                : $"Building {buildingId:000}";

            StringBuilder builder = new();
            builder.AppendLine("Residence");
            builder.AppendLine($"Site: {display}, Building {buildingId:000}");

            HouseholdState household = GetResidentHousehold(buildingId);
            AppendResidentialRentalLines(builder, buildingId, household);
            if (household == null)
            {
                builder.AppendLine("No resident household is assigned yet; household upgrades are unavailable.");
                return builder.ToString();
            }

            int capacity = HouseholdUpgradeCatalog.GetFoodReserveCapacityUnits(household);
            int builtCount = HouseholdUpgradeCatalog.CountBuiltUpgrades(household);
            builder.AppendLine($"{household.householdName} | Reserves {household.foodReserveUnits}/{capacity} | Upgrades {builtCount}/{HouseholdUpgradeCatalog.Count}");
            builder.AppendLine($"Last week: Reserve {FormatSignedUnits(household.lastWeeklyReserveDeltaUnits)} | Income {FormatMoney(household.lastWeeklyUpgradeIncomeCents)} | Upkeep {FormatMoney(household.lastWeeklyUpgradeUpkeepCents)}");
            builder.AppendLine(BuildBuiltHouseholdUpgradeLine(household));

            int optionCount = GetHouseholdUpgradeOptionCount();
            selectedHouseholdUpgradeIndex = WrapIndex(selectedHouseholdUpgradeIndex, optionCount);
            if (optionCount <= 0 || !TryGetSelectedHouseholdUpgradeDefinition(out HouseholdUpgradeDefinition definition) || definition == null)
            {
                builder.AppendLine("No household upgrades available.");
                AppendSelectedPropertyActionRead(
                    builder,
                    "Build Upgrade",
                    "no upgrade is available for this holding.",
                    "Select another holding or leave the house as-is.");
                return builder.ToString();
            }

            bool alreadyBuilt = HouseholdUpgradeCatalog.HasBuiltUpgrade(household, definition.Kind);
            builder.AppendLine($"Upgrade {selectedHouseholdUpgradeIndex + 1}/{optionCount}: {definition.DisplayName}");
            builder.AppendLine(definition.ResilienceSummary);
            builder.AppendLine(BuildHouseholdUpgradeEffectLine(definition));
            if (alreadyBuilt)
            {
                AppendSelectedPropertyActionRead(
                    builder,
                    "Build Upgrade",
                    "already built.",
                    "Cycle to another upgrade, or leave the house as-is.");
                return builder.ToString();
            }

            string quoteMessage = acquisitionMarket == null ? "Acquisitions unavailable." : string.Empty;
            if (acquisitionMarket != null && acquisitionMarket.TryGetHouseholdUpgradeQuote(buildingId, selectedHouseholdUpgradeIndex, out ConstructionInputQuote quote, out quoteMessage))
            {
                AppendConstructionQuote(builder, quote);
            }
            else
            {
                builder.AppendLine("Cost unavailable | Blocked");
                builder.AppendLine($"Missing: {CompactReason(quoteMessage)}");
                AppendBlockerNextStep(builder, quoteMessage);
            }

            AppendSelectedPropertyActionRead(
                builder,
                "Build Upgrade",
                "ready to build.",
                "Press Build Upgrade, or cycle to another improvement.");
            return builder.ToString();
        }

        private string BuildRentingAndLodgingSummary(bool includePreviewLists = true)
        {
            PopulationState population = GetRefreshedPopulationForRenting();
            if (population == null)
            {
                return "Renting & Boarding\nPopulation records unavailable.";
            }

            population.EnsureSettlementMetricsInitialized();
            int openBoardingHouseBeds = Mathf.Max(0, population.boardingHouseCapacity - population.boardingHouseUsed);
            int householdRoomCapacity = Mathf.Max(0, population.boardingCapacity - population.boardingHouseCapacity);
            int householdRoomUsed = Mathf.Max(0, population.boardingUsed - population.boardingHouseUsed);
            int rentableProperties = population.rentalProperties != null ? population.rentalProperties.Count : 0;

            StringBuilder builder = new();
            builder.AppendLine("Rental & Boarding");
            builder.AppendLine(populationManager != null ? populationManager.BuildTownSettlementHeadline() : $"Town settlement | Boarding {population.boardingUsed}/{population.boardingCapacity} | Vacancies {population.rentalVacancyCount} | Applicants {population.rentalApplicantCount}");
            builder.AppendLine($"Boarding House beds {population.boardingHouseUsed}/{population.boardingHouseCapacity} | Open beds {openBoardingHouseBeds} | Household boarder rooms {householdRoomUsed}/{householdRoomCapacity}");
            builder.AppendLine($"Rentable properties {rentableProperties} | Vacancies {population.rentalVacancyCount} ({population.playerOwnedRentalVacancyCount} player-owned) | Applicants {population.rentalApplicantCount}");
            builder.AppendLine($"Absorption: boarding {population.boardingUsed}, renting {population.renterHouseholdCount}, kin {population.kinPlacementCount}, transient {population.transientPersonCount}");
            if (!string.IsNullOrWhiteSpace(population.lastRentalSummary))
            {
                builder.AppendLine(population.lastRentalSummary);
            }

            builder.AppendLine(BuildRentingPressureHint(population));
            opportunityPressureRuntime ??= FindAnyObjectByType<OpportunityPressureRuntimeManager>();
            string rentingDeskSummary = opportunityPressureRuntime != null
                ? opportunityPressureRuntime.BuildDeskSummary("Renting & Boarding", 2)
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(rentingDeskSummary))
            {
                builder.AppendLine(rentingDeskSummary);
            }
            if (includePreviewLists)
            {
                builder.AppendLine();
                AppendRentalPropertyOverviewLines(builder, 3);
                builder.AppendLine();
                AppendRentalApplicantLines(builder, 3);
            }

            return builder.ToString();
        }

        private PopulationState GetRefreshedPopulationForRenting()
        {
            populationManager ??= FindAnyObjectByType<PopulationManager>();
            if (populationManager == null)
            {
                return null;
            }

            populationManager.RefreshSettlementMetrics();
            return populationManager.State;
        }

        private string BuildRentingAndBoardingDetail()
        {
            PopulationState population = GetRefreshedPopulationForRenting();
            if (population == null)
            {
                return "Renting & Boarding\nPopulation records unavailable.";
            }

            StringBuilder builder = new();
            builder.Append(BuildRentingAndLodgingSummary(false));
            builder.AppendLine();
            builder.AppendLine();
            AppendBoardingHouseBusinessLines(builder);
            builder.AppendLine();
            AppendRentalPropertyOverviewLines(builder, 6);
            return builder.ToString();
        }

        private string BuildRentingAndBoardingOperationsText()
        {
            PopulationState population = GetRefreshedPopulationForRenting();
            if (population == null)
            {
                return "Applicant records unavailable.";
            }

            StringBuilder builder = new();
            builder.AppendLine("Applicants, Vacancies & Pressure");
            builder.AppendLine(populationManager != null ? populationManager.BuildTownSettlementHeadline() : $"Waiting applicants {population.rentalApplicantCount} | Stronger housing need {population.strongerHousingNeedCount} | Rental pressure {population.rentalPressure01:P0}");
            builder.AppendLine($"Open vacancies {population.rentalVacancyCount} ({population.playerOwnedRentalVacancyCount} player-owned) | Transients {population.transientPersonCount} | Renter households {population.renterHouseholdCount}");
            builder.AppendLine(BuildRentingPressureHint(population));
            opportunityPressureRuntime ??= FindAnyObjectByType<OpportunityPressureRuntimeManager>();
            string rentingDeskSummary = opportunityPressureRuntime != null
                ? opportunityPressureRuntime.BuildDeskSummary("Renting & Boarding", 2)
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(rentingDeskSummary))
            {
                builder.AppendLine(rentingDeskSummary);
            }
            builder.AppendLine();
            AppendRentalApplicantLines(builder, 6);
            return builder.ToString();
        }

        private string BuildRentingAndBoardingListingsText()
        {
            PopulationState population = GetRefreshedPopulationForRenting();
            if (population == null)
            {
                return "Rentable property records unavailable.";
            }

            StringBuilder builder = new();
            AppendRentalPropertyOverviewLines(builder, 10);
            return builder.ToString();
        }

        private string BuildRentingPressureHint(PopulationState population)
        {
            if (population == null)
            {
                return "Pressure: settlement records unavailable.";
            }

            int openStaffSlots = CountBoardingHouseOpenStaffSlots();
            if (population.rentalApplicantCount > 0 && population.rentalVacancyCount <= 0)
            {
                return "Pressure: eligible applicants are waiting because there are no rentable vacancies.";
            }

            if (population.boardingHouseCapacity <= 0 && population.boardingUsed > 0)
            {
                return "Pressure: household boarder rooms are carrying lodging; a Boarding House would reduce pressure.";
            }

            if (population.boardingHouseCapacity > 0
                && population.boardingHouseUsed >= population.boardingHouseCapacity
                && openStaffSlots > 0)
            {
                return "Pressure: Boarding House beds are full and open staff slots can limit service.";
            }

            if (population.transientPersonCount > 0
                && population.boardingUsed >= Mathf.Max(1, population.boardingCapacity))
            {
                return "Pressure: transient residents need more rooms, rentable homes, or lodging capacity.";
            }

            if (population.strongerHousingNeedCount > population.rentalVacancyCount)
            {
                return "Pressure: more stable housing is needed for boarders and transient workers.";
            }

            return "Pressure: vacancies and lodging are currently manageable.";
        }

        private int CountBoardingHouseOpenStaffSlots()
        {
            if (sharedBusinessRuntime == null || sharedBusinessRuntime.Businesses == null)
            {
                return 0;
            }

            int openSlots = 0;
            for (int i = 0; i < sharedBusinessRuntime.Businesses.Count; i++)
            {
                BusinessInstanceState business = sharedBusinessRuntime.Businesses[i];
                if (business == null || business.BusinessType != BusinessType.BoardingHouse || business.RuntimeState == null)
                {
                    continue;
                }

                openSlots += Mathf.Max(0, business.RuntimeState.TargetWorkerCount - business.RuntimeState.FilledWorkerCount);
            }

            return openSlots;
        }

        private string BuildBoardingHouseDemandLine()
        {
            PopulationState population = GetRefreshedPopulationForRenting();
            if (population == null)
            {
                return "Town demand: population records unavailable.";
            }

            population.EnsureSettlementMetricsInitialized();
            if (populationManager != null)
            {
                return populationManager.BuildTownSettlementHeadline();
            }

            return $"Town settlement: applicants {population.rentalApplicantCount} | vacancies {population.rentalVacancyCount} | transients {population.transientPersonCount} | stronger housing need {population.strongerHousingNeedCount}";
        }

        private void AppendBoardingHouseBusinessLines(StringBuilder builder)
        {
            if (builder == null)
            {
                return;
            }

            builder.AppendLine("Boarding House Businesses");
            if (sharedBusinessRuntime == null || sharedBusinessRuntime.Businesses == null)
            {
                builder.AppendLine("No business runtime records available.");
                return;
            }

            int count = 0;
            for (int i = 0; i < sharedBusinessRuntime.Businesses.Count; i++)
            {
                BusinessInstanceState business = sharedBusinessRuntime.Businesses[i];
                if (business == null || business.BusinessType != BusinessType.BoardingHouse)
                {
                    continue;
                }

                count++;
                string cash = business.RuntimeState != null ? FormatMoney(business.RuntimeState.CurrentCashCents) : "$0.00";
                string staff = business.RuntimeState != null ? $"{business.RuntimeState.FilledWorkerCount}/{business.RuntimeState.TargetWorkerCount}" : "0/0";
                string occupancy = sharedBusinessRuntime.BuildBoardingHouseOccupancyLine(business);
                builder.AppendLine($"{business.RuntimeDisplayName} | Operator {GetOwnerDisplayName(business)} | Staff {staff} | Cash {cash}");
                builder.AppendLine(occupancy);
            }

            if (count == 0)
            {
                builder.AppendLine("No active Boarding House business is operating right now.");
            }
        }

        private void AppendRentalPropertyOverviewLines(StringBuilder builder, int maxLines)
        {
            if (builder == null)
            {
                return;
            }

            PopulationState population = GetRefreshedPopulationForRenting();
            builder.AppendLine("Rentable Properties");
            if (population == null || population.rentalProperties == null || population.rentalProperties.Count == 0)
            {
                builder.AppendLine("No rental-capable properties are recorded.");
                return;
            }

            int limit = Mathf.Clamp(maxLines, 1, 20);
            for (int i = 0; i < population.rentalProperties.Count && i < limit; i++)
            {
                RentalPropertyState property = population.rentalProperties[i];
                if (property == null)
                {
                    continue;
                }

                property.Sanitize();
                string owner = property.playerOwned ? "Player-owned" : "Town";
                string state = property.vacantHouseholdSlots > 0 ? "Vacant rentable" : "Occupied";
                builder.AppendLine($"{property.displayName} #{property.buildingId:000} | {state} | {property.occupancySummary} | {owner}");
            }

            if (population.rentalProperties.Count > limit)
            {
                builder.AppendLine($"+ {population.rentalProperties.Count - limit} more rental-capable properties");
            }
        }

        private void AppendRentalApplicantLines(StringBuilder builder, int maxLines)
        {
            if (builder == null)
            {
                return;
            }

            PopulationState population = GetRefreshedPopulationForRenting();
            builder.AppendLine("Applicants");
            if (population == null || population.rentalApplicants == null || population.rentalApplicants.Count == 0)
            {
                builder.AppendLine("No eligible rental applicants are waiting.");
                return;
            }

            int limit = Mathf.Clamp(maxLines, 1, 20);
            for (int i = 0; i < population.rentalApplicants.Count && i < limit; i++)
            {
                RentalApplicantState applicant = population.rentalApplicants[i];
                if (applicant == null)
                {
                    continue;
                }

                applicant.Sanitize();
                builder.AppendLine($"{applicant.displayName} | {NewcomerSettlementEvaluator.GetProfileDisplayName(applicant.arrivalProfile)} | Savings {FormatMoney(applicant.savingsCents)} | Score {applicant.score01:P0} | {applicant.status}");
            }

            if (population.rentalApplicants.Count > limit)
            {
                builder.AppendLine($"+ {population.rentalApplicants.Count - limit} more applicants");
            }
        }

        private void AppendResidentialRentalLines(StringBuilder builder, int buildingId, HouseholdState household)
        {
            if (builder == null)
            {
                return;
            }

            builder.AppendLine("Rental & Boarding");
            if (TryFindRentalPropertyState(buildingId, out RentalPropertyState property) && property != null)
            {
                string owner = property.playerOwned ? "Player-owned" : "Town";
                builder.AppendLine($"{owner} rentable property | {property.occupancySummary}");
                if (property.vacantHouseholdSlots > 0)
                {
                    builder.AppendLine("Vacant rooms can accept eligible rental applicants during weekly settlement.");
                }
            }
            else
            {
                PlacedBuilding building = GetBuilding(buildingId);
                bool residential = building != null && building.definition != null && building.definition.CanHostHouseholds;
                builder.AppendLine(residential
                    ? "Residential property is not currently listed as a rentable vacancy."
                    : "No rentable residential capacity recorded.");
            }

            if (household != null)
            {
                household.EnsureSettlementStateInitialized();
                int boarders = household.boarderPersonIds != null ? household.boarderPersonIds.Count : 0;
                string arrangement = household.isRenterHousehold
                    ? "renter household"
                    : NewcomerSettlementEvaluator.GetArrangementDisplayName(household.settlementArrangement);
                builder.AppendLine($"Occupancy: {arrangement} | Boarders {boarders}/{Mathf.Max(0, household.boardingCapacity)}");
            }
            else
            {
                builder.AppendLine("Occupancy: vacant.");
            }

            PopulationState population = populationManager != null ? populationManager.State : null;
            if (population != null)
            {
                population.EnsureSettlementMetricsInitialized();
                builder.AppendLine($"Town demand: applicants {population.rentalApplicantCount} | vacancies {population.rentalVacancyCount} | transient {population.transientPersonCount}");
            }
        }

        private string BuildResidentialRentalSummarySegment(int buildingId)
        {
            HouseholdState household = GetResidentHousehold(buildingId);
            if (household != null)
            {
                household.EnsureSettlementStateInitialized();
                int boarders = household.boarderPersonIds != null ? household.boarderPersonIds.Count : 0;
                if (household.isRenterHousehold || household.settlementArrangement == SettlementArrangement.Renting)
                {
                    return " | Renter household";
                }

                if (boarders > 0)
                {
                    return " | Hosts boarders";
                }

                if (household.settlementArrangement == SettlementArrangement.StableHousehold)
                {
                    return " | Stable household";
                }

                return $" | {NewcomerSettlementEvaluator.GetArrangementDisplayName(household.settlementArrangement)}";
            }

            if (TryFindRentalPropertyState(buildingId, out RentalPropertyState property) && property != null)
            {
                return property.vacantHouseholdSlots > 0
                    ? " | Vacant rentable"
                    : $" | Occupied {property.occupiedHouseholds}/{Mathf.Max(1, property.residentHouseholdCapacity)}";
            }

            return string.Empty;
        }

        private bool TryFindRentalPropertyState(int buildingId, out RentalPropertyState property)
        {
            property = null;
            PopulationState population = populationManager != null ? populationManager.State : null;
            if (population == null || population.rentalProperties == null)
            {
                return false;
            }

            for (int i = 0; i < population.rentalProperties.Count; i++)
            {
                RentalPropertyState candidate = population.rentalProperties[i];
                if (candidate != null && candidate.buildingId == buildingId)
                {
                    candidate.Sanitize();
                    property = candidate;
                    return true;
                }
            }

            return false;
        }

        private static string BuildBuiltHouseholdUpgradeLine(HouseholdState household)
        {
            if (household == null || HouseholdUpgradeCatalog.CountBuiltUpgrades(household) <= 0)
            {
                return "Built upgrades: none";
            }

            List<string> labels = new();
            household.EnsureHouseholdUpgradesInitialized();
            for (int i = 0; i < household.upgrades.Count; i++)
            {
                HouseholdUpgradeState upgrade = household.upgrades[i];
                if (upgrade == null || !upgrade.built)
                {
                    continue;
                }

                HouseholdUpgradeDefinition definition = HouseholdUpgradeCatalog.Get(upgrade.kind);
                labels.Add(definition != null ? definition.DisplayName : upgrade.kind.ToString());
            }

            return labels.Count == 0 ? "Built upgrades: none" : $"Built upgrades: {string.Join(", ", labels)}";
        }

        private static string BuildHouseholdUpgradeEffectLine(HouseholdUpgradeDefinition definition)
        {
            if (definition == null)
            {
                return "Effect unavailable.";
            }

            List<string> effects = new();
            if (definition.WeeklyReserveProductionUnits > 0)
            {
                effects.Add($"+{definition.WeeklyReserveProductionUnits} reserve/wk");
            }

            if (definition.FoodReserveCapacityBonus > 0)
            {
                effects.Add($"+{definition.FoodReserveCapacityBonus} reserve capacity");
            }

            if (definition.WeeklyIncomeCents > 0)
            {
                effects.Add($"+{FormatMoney(definition.WeeklyIncomeCents)}/wk income");
            }

            if (definition.WeeklyUpkeepCents > 0)
            {
                effects.Add($"{FormatMoney(definition.WeeklyUpkeepCents)}/wk upkeep");
            }

            if (definition.HousingSlotBonus > 0)
            {
                effects.Add($"+{definition.HousingSlotBonus} housing slot");
            }

            if (definition.DailyFoodNeedBonus > 0 || definition.DailyGeneralGoodsNeedBonus > 0)
            {
                effects.Add($"+{definition.DailyFoodNeedBonus} food, +{definition.DailyGeneralGoodsNeedBonus} goods demand/day");
            }

            return effects.Count == 0 ? "Effect: resilience only" : $"Effect: {string.Join(" | ", effects)}";
        }

        private string BuildBusinessActivationText(int buildingId)
        {
            StringBuilder builder = new();
            if (acquisitionMarket == null)
            {
                builder.AppendLine("Startup unavailable.");
                return builder.ToString();
            }

            if (!IsPlayerOwnedShellWithDefinition(buildingId))
            {
                builder.AppendLine("Startup unavailable.");
                builder.AppendLine("Missing: Owned shell is missing a usable building definition.");
                return builder.ToString();
            }

            EnsureBusinessActivationSelection(buildingId);
            int candidateCount = GetBusinessActivationCandidateCount(buildingId);
            selectedBusinessActivationIndex = WrapIndex(selectedBusinessActivationIndex, candidateCount);
            if (candidateCount <= 0 || !acquisitionMarket.TryGetBusinessActivationCandidate(buildingId, selectedBusinessActivationIndex, out BusinessActivationCandidateState candidate) || candidate == null)
            {
                builder.AppendLine("No supported business starts.");
                AppendSelectedPropertyActionRead(
                    builder,
                    "Start Business",
                    "no supported business start is selected.",
                    "Cycle the business type or inspect another shell.");
                return builder.ToString();
            }

            builder.AppendLine($"Start {selectedBusinessActivationIndex + 1}/{candidateCount}: {candidate.displayName}");
            AppendFitSummary(builder, "Startup fit", candidate.fitScore01, candidate.costMultiplier, candidate.fitWarningSummary);

            bool quoteAvailable = acquisitionMarket.TryGetBusinessActivationQuote(buildingId, selectedBusinessActivationIndex, out ConstructionInputQuote quote, out string quoteMessage);
            if (quoteAvailable)
            {
                AppendConstructionQuote(builder, quote);
            }
            else
            {
                builder.AppendLine($"Cost {FormatMoney(candidate.startupCostCents)} | Blocked");
                builder.AppendLine($"Missing: {CompactReason(quoteMessage)}");
                AppendBlockerNextStep(builder, quoteMessage);
            }

            if (!quoteAvailable && !candidate.eligible && !string.IsNullOrWhiteSpace(candidate.reason))
            {
                builder.AppendLine($"Blocked: {CompactReason(candidate.reason)}");
                AppendBlockerNextStep(builder, candidate.reason);
            }

            AppendExpansionReadiness(
                builder,
                CreateExpansionReadinessService().BuildForVacantShell(buildingId, selectedBusinessActivationIndex));
            AppendSelectedPropertyActionRead(
                builder,
                "Start Business",
                "ready to open.",
                "Press Start Business, or cycle the business type.");
            return builder.ToString();
        }

        private static void AppendFitSummary(StringBuilder builder, string label, float fitScore01, float costMultiplier, string warningSummary)
        {
            if (builder == null)
            {
                return;
            }

            string resolvedLabel = string.IsNullOrWhiteSpace(label) ? "Fit" : label;
            string fitText = $"{resolvedLabel} {Mathf.RoundToInt(Mathf.Clamp01(fitScore01) * 100f)}%";
            if (costMultiplier > 1.01f)
            {
                fitText += $" | Cost x{costMultiplier:0.00}";
            }

            if (!string.IsNullOrWhiteSpace(warningSummary))
            {
                fitText += $" | Warning: {CompactReason(warningSummary)}";
            }
            else
            {
                fitText += " | Recommended";
            }

            builder.AppendLine(fitText);
        }

        private void AppendConstructionQuote(StringBuilder builder, ConstructionInputQuote quote)
        {
            if (builder == null || quote == null)
            {
                return;
            }

            builder.AppendLine($"Cost {FormatMoney(quote.CashCostCents)} | {(quote.CanProceed ? "Ready" : "Blocked")}");
            if (!quote.CanProceed)
            {
                string missingSummary = quote.BuildMissingSummary();
                builder.AppendLine($"Missing: {CompactReason(missingSummary)}");
                AppendBlockerNextStep(builder, missingSummary);
            }

            if (quote.resources != null && quote.resources.Count > 0)
            {
                List<string> resources = new();
                for (int i = 0; i < quote.resources.Count; i++)
                {
                    ConstructionResourceQuoteLine line = quote.resources[i];
                    if (line != null && line.requiredUnits > 0)
                    {
                        resources.Add($"{line.displayName} {line.requiredUnits}/{line.availableUnits}");
                    }
                }

                if (resources.Count > 0)
                {
                    builder.AppendLine($"Inputs: {string.Join(" | ", resources)}");
                }
            }
        }

        private static void AppendBlockerNextStep(StringBuilder builder, string missingSummary)
        {
            if (builder == null)
            {
                return;
            }

            builder.AppendLine(FirstSessionGuidanceText.BuildConstructionBlockerNextStep(missingSummary));
        }

        private bool IsStartableOwnedShell(OwnedEntry entry)
        {
            return entry.Kind == OwnedKind.Business
                && GetBusinessForBuilding(entry.BuildingId) == null
                && IsPlayerOwnedShellWithDefinition(entry.BuildingId);
        }

        private bool IsPlayerOwnedShellWithDefinition(int buildingId)
        {
            return IsPlayerOwnedShellWithDefinition(GetBuilding(buildingId));
        }

        private static bool IsPlayerOwnedShellWithDefinition(PlacedBuilding building)
        {
            return building != null && building.playerOwned && building.definition != null;
        }

        private bool IsResidentialHolding(int buildingId)
        {
            return IsResidentialHolding(GetBuilding(buildingId));
        }

        private static bool IsResidentialHolding(PlacedBuilding building)
        {
            return building != null
                && building.definition != null
                && building.definition.CanHostHouseholds
                && !building.definition.CanHostWorkplace;
        }

        private bool TryGetSelectedStartupBlockedReason(int buildingId, out string reason)
        {
            reason = string.Empty;
            if (acquisitionMarket == null)
            {
                reason = "Startup unavailable.";
                return true;
            }

            if (!IsPlayerOwnedShellWithDefinition(buildingId))
            {
                reason = "Owned shell is missing a usable building definition.";
                return true;
            }

            EnsureBusinessActivationSelection(buildingId);
            int candidateCount = GetBusinessActivationCandidateCount(buildingId);
            selectedBusinessActivationIndex = WrapIndex(selectedBusinessActivationIndex, candidateCount);
            if (candidateCount <= 0)
            {
                reason = "No supported business starts.";
                return true;
            }

            if (!acquisitionMarket.TryGetBusinessActivationCandidate(buildingId, selectedBusinessActivationIndex, out BusinessActivationCandidateState candidate) || candidate == null)
            {
                reason = "No business activation candidate is selected.";
                return true;
            }

            if (!candidate.eligible)
            {
                reason = string.IsNullOrWhiteSpace(candidate.reason)
                    ? $"{candidate.displayName} cannot start at this site."
                    : candidate.reason;
                return true;
            }

            if (!acquisitionMarket.TryGetBusinessActivationQuote(buildingId, selectedBusinessActivationIndex, out ConstructionInputQuote quote, out string quoteMessage))
            {
                reason = string.IsNullOrWhiteSpace(quoteMessage) ? "Startup quote unavailable." : quoteMessage;
                return true;
            }

            if (quote == null)
            {
                reason = "Startup quote unavailable.";
                return true;
            }

            if (!quote.CanProceed)
            {
                reason = quote.BuildMissingSummary();
                return true;
            }

            return false;
        }

        private string BuildCompactLastAcquisition()
        {
            if (acquisitionMarket == null || string.IsNullOrWhiteSpace(acquisitionMarket.LastPurchaseSummary))
            {
                return "None";
            }

            string summary = acquisitionMarket.LastPurchaseSummary.Trim();
            if (summary.StartsWith("No acquisitions", StringComparison.OrdinalIgnoreCase))
            {
                return "None";
            }

            int inputsIndex = summary.IndexOf(" Inputs:", StringComparison.OrdinalIgnoreCase);
            if (inputsIndex >= 0)
            {
                summary = summary.Substring(0, inputsIndex).TrimEnd();
            }

            int secondSentenceIndex = summary.IndexOf(". ", StringComparison.Ordinal);
            if (secondSentenceIndex >= 0)
            {
                summary = summary.Substring(0, secondSentenceIndex + 1);
            }

            return summary;
        }

        private int GetPassiveTownBusinessCount()
        {
            if (sharedBusinessRuntime == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < sharedBusinessRuntime.Businesses.Count; i++)
            {
                BusinessInstanceState business = sharedBusinessRuntime.Businesses[i];
                if (business == null || business.BusinessType == BusinessType.GeneralStore)
                {
                    continue;
                }

                if (business.Owner != null && business.Owner.OwnerKind == BusinessOwnerKind.Player)
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private static string GetOwnerDisplayName(BusinessInstanceState business)
        {
            return business != null && business.Owner != null ? business.Owner.DisplayName : "Town";
        }

        private int GetOwnedBusinessCount()
        {
            int playerStoreCount = storeRuntime != null ? 1 : 0;
            int acquiredBusinessCount = acquisitionMarket != null ? acquisitionMarket.OwnedBusinessCount : 0;
            return playerStoreCount + acquiredBusinessCount;
        }

        private TownPlot GetPlot(int plotId)
        {
            return townWorld != null && plotId >= 0 && plotId < townWorld.Plots.Count ? townWorld.Plots[plotId] : null;
        }

        private PlacedBuilding GetBuilding(int buildingId)
        {
            return townWorld != null && buildingId >= 0 && buildingId < townWorld.Buildings.Count ? townWorld.Buildings[buildingId] : null;
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }

        private static void SetButtonLabel(Button button, string label)
        {
            TMP_Text text = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
            if (text != null)
            {
                text.text = label;
            }
        }

        private static void SetButtonVisible(Button button, bool visible)
        {
            if (button != null)
            {
                button.gameObject.SetActive(visible);
            }
        }

        private static void SetButtonInteractable(Button button, bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }

        private static void SetInputPlaceholder(TMP_InputField input, string placeholder)
        {
            if (input != null && input.placeholder is TMP_Text placeholderText)
            {
                placeholderText.text = placeholder ?? string.Empty;
            }
        }

        private static void SetInputTextIfNotFocused(TMP_InputField input, string value)
        {
            if (input != null && !input.isFocused)
            {
                input.SetTextWithoutNotify(value ?? string.Empty);
            }
        }

        private static string FormatId(int id)
        {
            return id >= 0 ? id.ToString("000") : "None";
        }

        private static string FormatMoney(int cents)
        {
            return "$" + (cents / 100f).ToString("N2");
        }

        private static string FormatContributionPercent(float value01)
        {
            return $"{Mathf.RoundToInt(Mathf.Clamp01(value01) * 100f)}%";
        }

        private static string FormatSignedMoney(int cents)
        {
            string sign = cents >= 0 ? "+" : "-";
            return sign + FormatMoney(Mathf.Abs(cents));
        }

        private static string FormatSignedMoneyColored(int cents)
        {
            string color = cents > 0 ? "#8FCB8F" : cents < 0 ? "#D98282" : "#D6D0BD";
            return $"<color={color}>{FormatSignedMoney(cents)}</color>";
        }

        private static LLFeedbackKind ResolveMoneyFeedbackKind(int cents)
        {
            int absolute = Mathf.Abs(cents);
            if (absolute >= 10000)
            {
                return LLFeedbackKind.HeavyCash;
            }

            if (absolute >= 1000)
            {
                return LLFeedbackKind.MediumCash;
            }

            return LLFeedbackKind.SmallCash;
        }

        private static void AppendNetRow(StringBuilder builder, string label, int cents)
        {
            builder.Append(label.PadRight(10));
            builder.Append(FormatSignedMoneyColored(cents));
            builder.AppendLine();
        }

        private static string ColorizeStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return string.Empty;
            }

            string color = status switch
            {
                "Stable" => "#8FCB8F",
                "Blocked" => "#D98282",
                "Below reserve" => "#D98282",
                "Understaffed" => "#D8B35A",
                "Needs restock" => "#D8B35A",
                "Thin stock" => "#D8B35A",
                _ => "#D6D0BD"
            };
            return $"<color={color}>{status}</color>";
        }

        private static string FormatSignedUnits(int units)
        {
            string sign = units >= 0 ? "+" : "-";
            return sign + Mathf.Abs(units);
        }

        private static string CompactReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return "not eligible";
            }

            string trimmed = reason.Trim();
            return trimmed.Length <= 140 ? trimmed : trimmed.Substring(0, 137) + "...";
        }

        private static string CompactActionReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return "blocked";
            }

            string trimmed = reason.Trim();
            return trimmed.Length <= 36 ? trimmed : trimmed.Substring(0, 33) + "...";
        }

        private static int WrapIndex(int index, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            return index < 0 ? count - 1 : index % count;
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Property";
            }

            StringBuilder builder = new();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            return builder.ToString();
        }
    }
}
