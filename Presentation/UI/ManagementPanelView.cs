using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LandLedgers.UI
{
    public enum ManagementPanelTab
    {
        Properties = 0,
        Finances = 1,
        Acquisitions = 2,
        Government = 3,
        Resources = 4
    }

    [DisallowMultipleComponent]
    public sealed class ManagementPanelView : MonoBehaviour
    {
        private static readonly Vector2 CashTransferOverlaySize = new(680f, 330f);
        private static readonly Vector2 CashTransferOverlayOffset = new(24f, 72f);
        [Header("Shell")]
        [SerializeField] private RectTransform panelRoot;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text hintText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_FontAsset managementFont;
        [SerializeField] private Button propertiesTabButton;
        [SerializeField] private Button financesTabButton;
        [SerializeField] private Button acquisitionsTabButton;
        [SerializeField] private Button governmentTabButton;
        [SerializeField] private Button resourcesTabButton;
        [SerializeField] private Button closeButton;

        [Header("Tabs")]
        [SerializeField] private RectTransform propertiesContentRoot;
        [SerializeField] private RectTransform financesContentRoot;
        [SerializeField] private RectTransform acquisitionsContentRoot;
        [SerializeField] private RectTransform governmentContentRoot;
        [SerializeField] private RectTransform resourcesContentRoot;

        [Header("Properties")]
        [SerializeField] private TMP_Text propertyListTitleText;
        [SerializeField] private RectTransform propertyListContent;
        [SerializeField] private Button propertyRowTemplate;
        [SerializeField] private TMP_Text propertyDetailTitleText;
        [SerializeField] private TMP_Text propertyDetailSummaryText;
        [SerializeField] private RectTransform propertyDetailBodyScrollRoot;
        [SerializeField] private RectTransform propertyDetailBodyContent;
        [SerializeField] private TMP_Text rentingOverviewText;
        [SerializeField] private TMP_Text storeOverviewText;
        [SerializeField] private TMP_Text storeFinanceText;
        [SerializeField] private TMP_Text storeStaffingText;
        [SerializeField] private TMP_Text storeStockText;
        [SerializeField] private Button focusPropertyButton;
        [SerializeField] private Button decreaseMarginButton;
        [SerializeField] private Button increaseMarginButton;
        [SerializeField] private Button previousWorkerButton;
        [SerializeField] private Button nextWorkerButton;
        [SerializeField] private Button assignWorkerButton;
        [SerializeField] private Button buildProjectButton;
        private string lastPropertyDetailLayoutSignature;

        [Header("Finances")]
        [SerializeField] private RectTransform financesScrollRoot;
        [SerializeField] private RectTransform financesScrollContent;
        [SerializeField] private TMP_Text financesSummaryText;
        [SerializeField] private TMP_Text financesDebtPressureText;
        [SerializeField] private TMP_Text financesDistributionText;
        [SerializeField] private TMP_Text financesAffordabilityText;
        [SerializeField] private RectTransform businessBreakdownContent;
        [SerializeField] private Button financeBusinessRowTemplate;
        [SerializeField] private RectTransform cashTransferRoot;
        [SerializeField] private TMP_Text cashTransferTitleText;
        [SerializeField] private TMP_Text cashTransferHelpText;
        [SerializeField] private TMP_Text cashTransferThresholdText;
        [SerializeField] private TMP_Text cashTransferStatusText;
        [SerializeField] private Button cashTransferDepositOneButton;
        [SerializeField] private Button cashTransferDepositTenButton;
        [SerializeField] private Button cashTransferDepositHundredButton;
        [SerializeField] private TMP_InputField cashTransferDepositInput;
        [SerializeField] private Button cashTransferDepositExactButton;
        [SerializeField] private Button cashTransferWithdrawOneButton;
        [SerializeField] private Button cashTransferWithdrawTenButton;
        [SerializeField] private Button cashTransferWithdrawHundredButton;
        [SerializeField] private TMP_InputField cashTransferWithdrawInput;
        [SerializeField] private Button cashTransferWithdrawExactButton;
        [SerializeField] private Button cashTransferAutoToggleButton;
        [SerializeField] private TMP_InputField cashTransferLowerThresholdInput;
        [SerializeField] private TMP_InputField cashTransferUpperThresholdInput;
        [SerializeField] private Button cashTransferApplyReserveButton;
        [SerializeField] private Button cashTransferLowerMinusTenButton;
        [SerializeField] private Button cashTransferLowerPlusTenButton;
        [SerializeField] private Button cashTransferUpperMinusTenButton;
        [SerializeField] private Button cashTransferUpperPlusTenButton;
        [SerializeField] private RectTransform bankLoanRoot;
        [SerializeField] private TMP_Text bankLoanTitleText;
        [SerializeField] private TMP_Text bankLoanHelpText;
        [SerializeField] private TMP_Text bankLoanRequestedAmountText;
        [SerializeField] private TMP_Text bankLoanEstimatedPaymentText;
        [SerializeField] private TMP_Text bankLoanPaymentScheduleText;
        [SerializeField] private TMP_Text bankLoanEstimatedTotalText;
        [SerializeField] private TMP_Text bankLoanLenderStandingText;
        [SerializeField] private TMP_Text bankLoanStatusText;
        [SerializeField] private TMP_Text bankLoanActiveLoanText;
        [SerializeField] private Button loanWorkflowButton;
        [SerializeField] private Button loanIncreaseOneButton;
        [SerializeField] private Button loanIncreaseTenButton;
        [SerializeField] private Button loanIncreaseHundredButton;
        [SerializeField] private Button loanDecreaseOneButton;
        [SerializeField] private Button loanDecreaseTenButton;
        [SerializeField] private Button loanDecreaseHundredButton;
        [SerializeField] private Button loanSubmitButton;

        [Header("Acquisitions")]
        [SerializeField] private Button acquisitionLandButton;
        [SerializeField] private Button acquisitionBusinessesButton;
        [SerializeField] private RectTransform acquisitionForSaleSectionRoot;
        [SerializeField] private RectTransform acquisitionOffMarketSectionRoot;
        [SerializeField] private RectTransform acquisitionSelectedLeadSectionRoot;
        [SerializeField] private RectTransform acquisitionHistorySectionRoot;
        [SerializeField] private TMP_Text acquisitionForSaleText;
        [SerializeField] private TMP_Text acquisitionOffMarketText;
        [SerializeField] private TMP_Text acquisitionSelectedLeadText;
        [SerializeField] private TMP_Text acquisitionHistoryText;
        [SerializeField] private Button acquisitionWorkflowButton;
        [SerializeField] private Button acquisitionFocusButton;
        [SerializeField] private Button previousListingButton;
        [SerializeField] private Button nextListingButton;
        [SerializeField] private Button buyListingButton;

        [Header("Government")]
        [SerializeField] private RectTransform governmentSummarySectionRoot;
        [SerializeField] private RectTransform governmentEffectsSectionRoot;
        [SerializeField] private RectTransform governmentOwnerImplicationsSectionRoot;
        [SerializeField] private TMP_Text governmentTitleText;
        [SerializeField] private TMP_Text governmentSummaryText;
        [SerializeField] private TMP_Text governmentEffectsText;
        [SerializeField] private TMP_Text governmentOwnerImplicationsText;
        [SerializeField] private Button governmentFocusButton;

        [Header("Resources")]
        [SerializeField] private TMP_Text resourcesSummaryText;
        [SerializeField] private TMP_Text resourcesDistrictListText;
        [SerializeField] private TMP_Text resourcesDetailText;

        [Header("Fallback")]
        [SerializeField] private TMP_Text runtimeFallbackText;

        [Header("Selection Visuals")]
        [SerializeField] private Color unselectedButtonColor = new(0.15f, 0.16f, 0.14f, 0.94f);
        [SerializeField] private Color selectedButtonColor = new(0.68f, 0.52f, 0.27f, 1f);
        [SerializeField] private Color unselectedTextColor = new(0.82f, 0.84f, 0.78f, 1f);
        [SerializeField] private Color selectedTextColor = new(1f, 0.96f, 0.84f, 1f);
        [SerializeField] private Color statusTextColor = new(1f, 0.91f, 0.66f, 1f);

        public RectTransform PanelRoot => panelRoot != null ? panelRoot : transform as RectTransform;
        public TMP_Text TitleText => titleText;
        public TMP_Text HintText => hintText;
        public TMP_Text StatusText => statusText;
        public TMP_FontAsset ManagementFont => managementFont;
        public Button PropertiesTabButton => propertiesTabButton;
        public Button FinancesTabButton => financesTabButton;
        public Button AcquisitionsTabButton => acquisitionsTabButton;
        public Button GovernmentTabButton => governmentTabButton;
        public Button ResourcesTabButton => resourcesTabButton;
        public Button CloseButton => closeButton;
        public RectTransform PropertiesContentRoot => propertiesContentRoot;
        public RectTransform FinancesContentRoot => financesContentRoot;
        public RectTransform AcquisitionsContentRoot => acquisitionsContentRoot;
        public RectTransform GovernmentContentRoot => governmentContentRoot;
        public RectTransform ResourcesContentRoot => resourcesContentRoot;
        public TMP_Text PropertyListTitleText => propertyListTitleText;
        public RectTransform PropertyListContent => propertyListContent;
        public Button PropertyRowTemplate => propertyRowTemplate;
        public TMP_Text PropertyDetailTitleText => propertyDetailTitleText;
        public TMP_Text PropertyDetailSummaryText => propertyDetailSummaryText;
        public RectTransform PropertyDetailBodyScrollRoot => propertyDetailBodyScrollRoot;
        public RectTransform PropertyDetailBodyContent => propertyDetailBodyContent;
        public TMP_Text RentingOverviewText => rentingOverviewText;
        public TMP_Text StoreOverviewText => storeOverviewText;
        public TMP_Text StoreFinanceText => storeFinanceText;
        public TMP_Text StoreStaffingText => storeStaffingText;
        public TMP_Text StoreStockText => storeStockText;
        public Button FocusPropertyButton => focusPropertyButton;
        public Button DecreaseMarginButton => decreaseMarginButton;
        public Button IncreaseMarginButton => increaseMarginButton;
        public Button PreviousWorkerButton => previousWorkerButton;
        public Button NextWorkerButton => nextWorkerButton;
        public Button AssignWorkerButton => assignWorkerButton;
        public Button BuildProjectButton => buildProjectButton;
        public RectTransform FinancesScrollContent => financesScrollContent;
        public TMP_Text FinancesSummaryText => financesSummaryText;
        public TMP_Text FinancesDebtPressureText => financesDebtPressureText;
        public TMP_Text FinancesDistributionText => financesDistributionText;
        public TMP_Text FinancesAffordabilityText => financesAffordabilityText;
        public RectTransform BusinessBreakdownContent => businessBreakdownContent;
        public Button FinanceBusinessRowTemplate => financeBusinessRowTemplate;
        public RectTransform CashTransferRoot => cashTransferRoot;
        public TMP_Text CashTransferTitleText => cashTransferTitleText;
        public TMP_Text CashTransferHelpText => cashTransferHelpText;
        public TMP_Text CashTransferThresholdText => cashTransferThresholdText;
        public TMP_Text CashTransferStatusText => cashTransferStatusText;
        public Button CashTransferDepositOneButton => cashTransferDepositOneButton;
        public Button CashTransferDepositTenButton => cashTransferDepositTenButton;
        public Button CashTransferDepositHundredButton => cashTransferDepositHundredButton;
        public TMP_InputField CashTransferDepositInput => cashTransferDepositInput;
        public Button CashTransferDepositExactButton => cashTransferDepositExactButton;
        public Button CashTransferWithdrawOneButton => cashTransferWithdrawOneButton;
        public Button CashTransferWithdrawTenButton => cashTransferWithdrawTenButton;
        public Button CashTransferWithdrawHundredButton => cashTransferWithdrawHundredButton;
        public TMP_InputField CashTransferWithdrawInput => cashTransferWithdrawInput;
        public Button CashTransferWithdrawExactButton => cashTransferWithdrawExactButton;
        public Button CashTransferAutoToggleButton => cashTransferAutoToggleButton;
        public TMP_InputField CashTransferLowerThresholdInput => cashTransferLowerThresholdInput;
        public TMP_InputField CashTransferUpperThresholdInput => cashTransferUpperThresholdInput;
        public Button CashTransferApplyReserveButton => cashTransferApplyReserveButton;
        public Button CashTransferLowerMinusTenButton => cashTransferLowerMinusTenButton;
        public Button CashTransferLowerPlusTenButton => cashTransferLowerPlusTenButton;
        public Button CashTransferUpperMinusTenButton => cashTransferUpperMinusTenButton;
        public Button CashTransferUpperPlusTenButton => cashTransferUpperPlusTenButton;
        public RectTransform BankLoanRoot => bankLoanRoot;
        public TMP_Text BankLoanTitleText => bankLoanTitleText;
        public TMP_Text BankLoanHelpText => bankLoanHelpText;
        public TMP_Text BankLoanRequestedAmountText => bankLoanRequestedAmountText;
        public TMP_Text BankLoanEstimatedPaymentText => bankLoanEstimatedPaymentText;
        public TMP_Text BankLoanPaymentScheduleText => bankLoanPaymentScheduleText;
        public TMP_Text BankLoanEstimatedTotalText => bankLoanEstimatedTotalText;
        public TMP_Text BankLoanLenderStandingText => bankLoanLenderStandingText;
        public TMP_Text BankLoanStatusText => bankLoanStatusText;
        public TMP_Text BankLoanActiveLoanText => bankLoanActiveLoanText;
        public Button LoanWorkflowButton => loanWorkflowButton;
        public Button LoanIncreaseOneButton => loanIncreaseOneButton;
        public Button LoanIncreaseTenButton => loanIncreaseTenButton;
        public Button LoanIncreaseHundredButton => loanIncreaseHundredButton;
        public Button LoanDecreaseOneButton => loanDecreaseOneButton;
        public Button LoanDecreaseTenButton => loanDecreaseTenButton;
        public Button LoanDecreaseHundredButton => loanDecreaseHundredButton;
        public Button LoanSubmitButton => loanSubmitButton;
        public Button AcquisitionLandButton => acquisitionLandButton;
        public Button AcquisitionBusinessesButton => acquisitionBusinessesButton;
        public RectTransform AcquisitionForSaleSectionRoot => acquisitionForSaleSectionRoot;
        public RectTransform AcquisitionOffMarketSectionRoot => acquisitionOffMarketSectionRoot;
        public RectTransform AcquisitionSelectedLeadSectionRoot => acquisitionSelectedLeadSectionRoot;
        public RectTransform AcquisitionHistorySectionRoot => acquisitionHistorySectionRoot;
        public TMP_Text AcquisitionForSaleText => acquisitionForSaleText;
        public TMP_Text AcquisitionOffMarketText => acquisitionOffMarketText;
        public TMP_Text AcquisitionSelectedLeadText => acquisitionSelectedLeadText;
        public TMP_Text AcquisitionHistoryText => acquisitionHistoryText;
        public Button AcquisitionWorkflowButton => acquisitionWorkflowButton;
        public Button AcquisitionFocusButton => acquisitionFocusButton;
        public Button PreviousListingButton => previousListingButton;
        public Button NextListingButton => nextListingButton;
        public Button BuyListingButton => buyListingButton;
        public RectTransform GovernmentSummarySectionRoot => governmentSummarySectionRoot;
        public RectTransform GovernmentEffectsSectionRoot => governmentEffectsSectionRoot;
        public RectTransform GovernmentOwnerImplicationsSectionRoot => governmentOwnerImplicationsSectionRoot;
        public TMP_Text GovernmentTitleText => governmentTitleText;
        public TMP_Text GovernmentSummaryText => governmentSummaryText;
        public TMP_Text GovernmentEffectsText => governmentEffectsText;
        public TMP_Text GovernmentOwnerImplicationsText => governmentOwnerImplicationsText;
        public Button GovernmentFocusButton => governmentFocusButton;
        public TMP_Text ResourcesSummaryText => resourcesSummaryText;
        public TMP_Text ResourcesDistrictListText => resourcesDistrictListText;
        public TMP_Text ResourcesDetailText => resourcesDetailText;
        public TMP_Text RuntimeFallbackText => runtimeFallbackText;

        public bool BindFromChildren()
        {
            Transform root = transform;
            // The management shell is also exercised by editor/startup smoke checks.
            // Repair only missing presentation controls; runtime authorities remain
            // untouched and the authored scene/prefab remains the source of layout truth.
            bool canRepairAuthoredReferences = true;
            ResolveManagementFont();
            panelRoot = panelRoot != null ? panelRoot : transform as RectTransform;
            titleText = titleText != null ? titleText : FindText(root, "Panel_Title");
            hintText = hintText != null ? hintText : FindText(root, "Panel_HintText");
            statusText = statusText != null ? statusText : FindText(root, "Panel_StatusText");
            propertiesTabButton = propertiesTabButton != null ? propertiesTabButton : FindButton(root, "Tab_Properties");
            propertiesTabButton = propertiesTabButton != null ? propertiesTabButton : FindButton(root, "Properties Tab Panel");
            financesTabButton = financesTabButton != null ? financesTabButton : FindButton(root, "Tab_Finances");
            financesTabButton = financesTabButton != null ? financesTabButton : FindButton(root, "Finances Tab Panel");
            financesTabButton = financesTabButton != null ? financesTabButton : FindButton(root, "Finaces Tab Panel");
            acquisitionsTabButton = acquisitionsTabButton != null ? acquisitionsTabButton : FindButton(root, "Tab_Acquisitions");
            acquisitionsTabButton = acquisitionsTabButton != null ? acquisitionsTabButton : FindButton(root, "Acquisitions Tab Panel");
            governmentTabButton = governmentTabButton != null ? governmentTabButton : FindButton(root, "Tab_Government");
            resourcesTabButton = resourcesTabButton != null ? resourcesTabButton : FindButton(root, "Tab_Resources");
            closeButton = closeButton != null ? closeButton : FindButton(root, "Tab_Close");
            closeButton = closeButton != null ? closeButton : FindButton(root, "CloseButton");

            propertiesContentRoot = propertiesContentRoot != null ? propertiesContentRoot : FindRect(root, "Properties Content");
            financesContentRoot = financesContentRoot != null ? financesContentRoot : FindRect(root, "Finances Content");
            acquisitionsContentRoot = acquisitionsContentRoot != null ? acquisitionsContentRoot : FindRect(root, "Acquisitions Content");
            governmentContentRoot = governmentContentRoot != null ? governmentContentRoot : FindRect(root, "Government Content");
            resourcesContentRoot = resourcesContentRoot != null ? resourcesContentRoot : FindRect(root, "Resources Content");
            propertyListTitleText = propertyListTitleText != null ? propertyListTitleText : FindText(root, "PropertyList_Title");
            propertyListContent = propertyListContent != null ? propertyListContent : FindRect(root, "PropertyList_Content");
            propertyRowTemplate = propertyRowTemplate != null ? propertyRowTemplate : FindButton(root, "PropertyRow_Template");
            propertyDetailTitleText = propertyDetailTitleText != null ? propertyDetailTitleText : FindText(root, "PropertyDetail_Title");
            propertyDetailSummaryText = propertyDetailSummaryText != null ? propertyDetailSummaryText : FindText(root, "PropertyDetail_Summary");
            propertyDetailBodyScrollRoot = propertyDetailBodyScrollRoot != null ? propertyDetailBodyScrollRoot : FindRect(root, "PropertyDetail_BodyScroll");
            propertyDetailBodyContent = propertyDetailBodyContent != null ? propertyDetailBodyContent : FindRect(root, "PropertyDetail_BodyContent");
            rentingOverviewText = rentingOverviewText != null ? rentingOverviewText : FindText(root, "RentingOverviewText");
            storeOverviewText = storeOverviewText != null ? storeOverviewText : FindText(root, "StoreOverviewText");
            storeFinanceText = storeFinanceText != null ? storeFinanceText : FindText(root, "StoreFinanceText");
            storeStaffingText = storeStaffingText != null ? storeStaffingText : FindText(root, "StoreStaffingText");
            storeStockText = storeStockText != null ? storeStockText : FindText(root, "StoreStockText");
            focusPropertyButton = focusPropertyButton != null ? focusPropertyButton : FindButton(root, "FocusPropertyButton");
            decreaseMarginButton = decreaseMarginButton != null ? decreaseMarginButton : FindButton(root, "DecreaseMarginButton");
            increaseMarginButton = increaseMarginButton != null ? increaseMarginButton : FindButton(root, "IncreaseMarginButton");
            previousWorkerButton = previousWorkerButton != null ? previousWorkerButton : FindButton(root, "PrevWorkerButton");
            nextWorkerButton = nextWorkerButton != null ? nextWorkerButton : FindButton(root, "NextWorkerButton");
            assignWorkerButton = assignWorkerButton != null ? assignWorkerButton : FindButton(root, "AssignWorkerButton");
            buildProjectButton = buildProjectButton != null ? buildProjectButton : FindButton(root, "BuildProjectButton");
            EnsureMarginActionButtons(canRepairAuthoredReferences);
            EnsureCloseButton(canRepairAuthoredReferences);
            EnsureResourcesControls(canRepairAuthoredReferences);
            EnsurePropertyDetailScrollBody(canRepairAuthoredReferences);
            financesScrollRoot = financesScrollRoot != null ? financesScrollRoot : FindRect(root, "Finances_Scroll");
            financesScrollContent = financesScrollContent != null ? financesScrollContent : FindRect(root, "Finances_ScrollContent");
            financesSummaryText = financesSummaryText != null ? financesSummaryText : FindText(root, "FinancesSummaryText");
            financesDebtPressureText = financesDebtPressureText != null ? financesDebtPressureText : FindText(root, "FinancesDebtPressureText");
            financesDistributionText = financesDistributionText != null ? financesDistributionText : FindText(root, "FinancesDistributionText");
            financesAffordabilityText = financesAffordabilityText != null ? financesAffordabilityText : FindText(root, "FinancesAffordabilityText");
            businessBreakdownContent = businessBreakdownContent != null ? businessBreakdownContent : FindRect(root, "BusinessBreakdown_Content");
            financeBusinessRowTemplate = financeBusinessRowTemplate != null ? financeBusinessRowTemplate : FindButton(root, "FinanceBusinessRow_Template");
            EnsureFinanceDashboardControls(canRepairAuthoredReferences);
            EnsureBusinessCashTransferControls(canRepairAuthoredReferences);
            EnsureBankLoanControls(canRepairAuthoredReferences);
            acquisitionLandButton = acquisitionLandButton != null ? acquisitionLandButton : FindButton(root, "Acquisition_LandButton");
            acquisitionBusinessesButton = acquisitionBusinessesButton != null ? acquisitionBusinessesButton : FindButton(root, "Acquisition_BusinessesButton");
            acquisitionForSaleSectionRoot = acquisitionForSaleSectionRoot != null ? acquisitionForSaleSectionRoot : FindRect(root, "AcquisitionForSale_Section");
            acquisitionOffMarketSectionRoot = acquisitionOffMarketSectionRoot != null ? acquisitionOffMarketSectionRoot : FindRect(root, "AcquisitionOffMarket_Section");
            acquisitionSelectedLeadSectionRoot = acquisitionSelectedLeadSectionRoot != null ? acquisitionSelectedLeadSectionRoot : FindRect(root, "AcquisitionSelectedLead_Section");
            acquisitionHistorySectionRoot = acquisitionHistorySectionRoot != null ? acquisitionHistorySectionRoot : FindRect(root, "AcquisitionHistory_Section");
            acquisitionForSaleText = acquisitionForSaleText != null ? acquisitionForSaleText : FindText(root, "AcquisitionForSaleText");
            acquisitionForSaleText = acquisitionForSaleText != null ? acquisitionForSaleText : FindText(root, "AcquisitionForSale_BodyText");
            acquisitionOffMarketText = acquisitionOffMarketText != null ? acquisitionOffMarketText : FindText(root, "AcquisitionOffMarketText");
            acquisitionOffMarketText = acquisitionOffMarketText != null ? acquisitionOffMarketText : FindText(root, "AcquisitionOffMarket_BodyText");
            acquisitionSelectedLeadText = acquisitionSelectedLeadText != null ? acquisitionSelectedLeadText : FindText(root, "AcquisitionSelectedLeadText");
            acquisitionSelectedLeadText = acquisitionSelectedLeadText != null ? acquisitionSelectedLeadText : FindText(root, "AcquisitionSelectedLead_BodyText");
            acquisitionHistoryText = acquisitionHistoryText != null ? acquisitionHistoryText : FindText(root, "AcquisitionHistoryText");
            acquisitionHistoryText = acquisitionHistoryText != null ? acquisitionHistoryText : FindText(root, "AcquisitionHistory_BodyText");
            acquisitionWorkflowButton = acquisitionWorkflowButton != null ? acquisitionWorkflowButton : FindButton(root, "AcquisitionWorkflowButton");
            acquisitionFocusButton = acquisitionFocusButton != null ? acquisitionFocusButton : FindButton(root, "AcquisitionFocusButton");
            previousListingButton = previousListingButton != null ? previousListingButton : FindButton(root, "PreviousListingButton");
            nextListingButton = nextListingButton != null ? nextListingButton : FindButton(root, "NextListingButton");
            buyListingButton = buyListingButton != null ? buyListingButton : FindButton(root, "BuyListingButton");
            EnsureAcquisitionDashboardControls(canRepairAuthoredReferences);
            EnsureAcquisitionActionButtons(canRepairAuthoredReferences);
            ConfigureDashboardCard(acquisitionForSaleText, 132f);
            ConfigureDashboardCard(acquisitionOffMarketText, 150f);
            ConfigureDashboardCard(acquisitionSelectedLeadText, 150f);
            ConfigureDashboardCard(acquisitionHistoryText, 118f);
            governmentSummarySectionRoot = governmentSummarySectionRoot != null ? governmentSummarySectionRoot : FindRect(root, "GovernmentSummary_Section");
            governmentEffectsSectionRoot = governmentEffectsSectionRoot != null ? governmentEffectsSectionRoot : FindRect(root, "GovernmentEffects_Section");
            governmentOwnerImplicationsSectionRoot = governmentOwnerImplicationsSectionRoot != null ? governmentOwnerImplicationsSectionRoot : FindRect(root, "GovernmentOwnerImplications_Section");
            governmentTitleText = governmentTitleText != null ? governmentTitleText : FindText(root, "Government_TitleText");
            governmentSummaryText = governmentSummaryText != null ? governmentSummaryText : FindText(root, "Government_SummaryText");
            governmentSummaryText = governmentSummaryText != null ? governmentSummaryText : FindText(root, "GovernmentSummary_BodyText");
            governmentEffectsText = governmentEffectsText != null ? governmentEffectsText : FindText(root, "Government_EffectsText");
            governmentEffectsText = governmentEffectsText != null ? governmentEffectsText : FindText(root, "GovernmentEffects_BodyText");
            governmentOwnerImplicationsText = governmentOwnerImplicationsText != null ? governmentOwnerImplicationsText : FindText(root, "GovernmentOwnerImplicationsText");
            governmentOwnerImplicationsText = governmentOwnerImplicationsText != null ? governmentOwnerImplicationsText : FindText(root, "GovernmentOwnerImplications_BodyText");
            governmentFocusButton = governmentFocusButton != null ? governmentFocusButton : FindButton(root, "GovernmentFocusButton");
            EnsureGovernmentDashboardControls(canRepairAuthoredReferences);
            ConfigureDashboardCard(governmentSummaryText, 104f);
            ConfigureDashboardCard(governmentEffectsText, 154f);
            ConfigureDashboardCard(governmentOwnerImplicationsText, 118f);
            resourcesSummaryText = resourcesSummaryText != null ? resourcesSummaryText : FindText(root, "ResourcesSummaryText");
            resourcesDistrictListText = resourcesDistrictListText != null ? resourcesDistrictListText : FindText(root, "ResourcesDistrictListText");
            resourcesDetailText = resourcesDetailText != null ? resourcesDetailText : FindText(root, "ResourcesDetailText");
            runtimeFallbackText = runtimeFallbackText != null ? runtimeFallbackText : FindText(root, "RuntimePanelText");
            EnsureStatusText(canRepairAuthoredReferences);
            if (Application.isPlaying)
            {
                ConfigureStaticTooltips();
            }

            ApplyManagementFontToBusinessSurfaces();
            ConfigureStoreBusinessSectionLayout();
            return HasRequiredReferences();
        }

        public void ApplyManagementFontToBusinessSurfaces()
        {
            ApplyTypographyRoles();
            if (cashTransferRoot != null)
            {
                TMP_Text[] texts = cashTransferRoot.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < texts.Length; i++)
                {
                    LandLedgersTypography.ApplyRole(texts[i], LandLedgersTypography.TextRole.SectionHeader);
                }
            }
        }

        private void ApplyTypographyRoles()
        {
            LandLedgersTypography.ApplyRole(titleText, LandLedgersTypography.TextRole.ScreenTitle);
            LandLedgersTypography.ApplyRole(hintText, LandLedgersTypography.TextRole.HelperText);
            LandLedgersTypography.ApplyRole(statusText, LandLedgersTypography.TextRole.Badge);
            LandLedgersTypography.ApplyRole(propertyListTitleText, LandLedgersTypography.TextRole.SectionHeader);
            LandLedgersTypography.ApplyRole(propertyDetailTitleText, LandLedgersTypography.TextRole.PanelHeader);
            LandLedgersTypography.ApplyRole(propertyDetailSummaryText, LandLedgersTypography.TextRole.DenseBody);
            LandLedgersTypography.ApplyRole(rentingOverviewText, LandLedgersTypography.TextRole.DenseBody);
            LandLedgersTypography.ApplyRole(storeOverviewText, LandLedgersTypography.TextRole.DenseBody);
            LandLedgersTypography.ApplyRole(storeFinanceText, LandLedgersTypography.TextRole.DenseBody);
            LandLedgersTypography.ApplyRole(storeStaffingText, LandLedgersTypography.TextRole.DenseBody);
            LandLedgersTypography.ApplyRole(storeStockText, LandLedgersTypography.TextRole.DenseBody);
            LandLedgersTypography.ApplyRole(financesSummaryText, LandLedgersTypography.TextRole.DenseBody);
            LandLedgersTypography.ApplyRole(financesDebtPressureText, LandLedgersTypography.TextRole.DenseBody);
            LandLedgersTypography.ApplyRole(financesDistributionText, LandLedgersTypography.TextRole.DenseBody);
            LandLedgersTypography.ApplyRole(financesAffordabilityText, LandLedgersTypography.TextRole.DenseBody);
            LandLedgersTypography.ApplyRole(acquisitionForSaleText, LandLedgersTypography.TextRole.DenseBody);
            LandLedgersTypography.ApplyRole(acquisitionOffMarketText, LandLedgersTypography.TextRole.DenseBody);
            LandLedgersTypography.ApplyRole(acquisitionSelectedLeadText, LandLedgersTypography.TextRole.DenseBody);
            LandLedgersTypography.ApplyRole(acquisitionHistoryText, LandLedgersTypography.TextRole.LogBody);
            LandLedgersTypography.ApplyRole(governmentTitleText, LandLedgersTypography.TextRole.SectionHeader);
            LandLedgersTypography.ApplyRole(governmentSummaryText, LandLedgersTypography.TextRole.DenseBody);
            LandLedgersTypography.ApplyRole(governmentEffectsText, LandLedgersTypography.TextRole.DenseBody);
            LandLedgersTypography.ApplyRole(governmentOwnerImplicationsText, LandLedgersTypography.TextRole.DenseBody);
            LandLedgersTypography.ApplyRole(resourcesSummaryText, LandLedgersTypography.TextRole.DenseBody);
            LandLedgersTypography.ApplyRole(resourcesDistrictListText, LandLedgersTypography.TextRole.DenseBody);
            LandLedgersTypography.ApplyRole(resourcesDetailText, LandLedgersTypography.TextRole.DenseBody);
            LandLedgersTypography.ApplyRole(runtimeFallbackText, LandLedgersTypography.TextRole.HelperText);

            ApplyButtonTypography(propertiesTabButton, LandLedgersTypography.TextRole.TabLabel);
            ApplyButtonTypography(financesTabButton, LandLedgersTypography.TextRole.TabLabel);
            ApplyButtonTypography(acquisitionsTabButton, LandLedgersTypography.TextRole.TabLabel);
            ApplyButtonTypography(governmentTabButton, LandLedgersTypography.TextRole.TabLabel);
            ApplyButtonTypography(resourcesTabButton, LandLedgersTypography.TextRole.TabLabel);
            ApplyButtonTypography(closeButton, LandLedgersTypography.TextRole.TabLabel);
            ApplyButtonTypography(acquisitionLandButton, LandLedgersTypography.TextRole.TabLabel);
            ApplyButtonTypography(acquisitionBusinessesButton, LandLedgersTypography.TextRole.TabLabel);
            ApplyButtonTypography(focusPropertyButton, LandLedgersTypography.TextRole.ButtonLabel);
            ApplyButtonTypography(decreaseMarginButton, LandLedgersTypography.TextRole.ButtonLabel);
            ApplyButtonTypography(increaseMarginButton, LandLedgersTypography.TextRole.ButtonLabel);
            ApplyButtonTypography(previousWorkerButton, LandLedgersTypography.TextRole.ButtonLabel);
            ApplyButtonTypography(nextWorkerButton, LandLedgersTypography.TextRole.ButtonLabel);
            ApplyButtonTypography(assignWorkerButton, LandLedgersTypography.TextRole.ButtonLabel);
            ApplyButtonTypography(buildProjectButton, LandLedgersTypography.TextRole.ButtonLabel);
            ApplyButtonTypography(acquisitionWorkflowButton, LandLedgersTypography.TextRole.ButtonLabel);
            ApplyButtonTypography(acquisitionFocusButton, LandLedgersTypography.TextRole.ButtonLabel);
            ApplyButtonTypography(previousListingButton, LandLedgersTypography.TextRole.ButtonLabel);
            ApplyButtonTypography(nextListingButton, LandLedgersTypography.TextRole.ButtonLabel);
            ApplyButtonTypography(buyListingButton, LandLedgersTypography.TextRole.ButtonLabel);
            ApplyButtonTypography(governmentFocusButton, LandLedgersTypography.TextRole.ButtonLabel);
            ApplyButtonTypography(financeBusinessRowTemplate, LandLedgersTypography.TextRole.DenseBody);
        }

        public bool HasRequiredReferences()
        {
            return PanelRoot != null
                && titleText != null
                && hintText != null
                && propertiesTabButton != null
                && financesTabButton != null
                && acquisitionsTabButton != null
                && governmentTabButton != null
                && resourcesTabButton != null
                && closeButton != null
                && propertiesContentRoot != null
                && financesContentRoot != null
                && acquisitionsContentRoot != null
                && governmentContentRoot != null
                && resourcesContentRoot != null
                && propertyListTitleText != null
                && propertyListContent != null
                && propertyRowTemplate != null
                && propertyDetailTitleText != null
                && propertyDetailSummaryText != null
                && rentingOverviewText != null
                && storeOverviewText != null
                && storeFinanceText != null
                && storeStaffingText != null
                && storeStockText != null
                && focusPropertyButton != null
                && decreaseMarginButton != null
                && increaseMarginButton != null
                && previousWorkerButton != null
                && nextWorkerButton != null
                && assignWorkerButton != null
                && buildProjectButton != null
                && financesSummaryText != null
                && businessBreakdownContent != null
                && acquisitionLandButton != null
                && acquisitionBusinessesButton != null
                && acquisitionForSaleText != null
                && acquisitionOffMarketText != null
                && acquisitionSelectedLeadText != null
                && acquisitionHistoryText != null
                && acquisitionFocusButton != null
                && previousListingButton != null
                && nextListingButton != null
                && buyListingButton != null
                && governmentTitleText != null
                && governmentSummaryText != null
                && governmentEffectsText != null
                && governmentOwnerImplicationsText != null
                && resourcesSummaryText != null
                && resourcesDistrictListText != null
                && resourcesDetailText != null;
        }

        private void ConfigureStaticTooltips()
        {
            UITooltipRegistry.Attach(propertiesTabButton, "Businesses", "Review owned businesses, staffing, cash, and operating actions.");
            UITooltipRegistry.Attach(financesTabButton, "Finances", "Review liquid cash, business cash, transfers, lender pressure, and formal loan applications.");
            UITooltipRegistry.Attach(acquisitionsTabButton, "Acquisitions", "Review active leads, scouting reads, funding posture, and move serious deals into formal process.");
            UITooltipRegistry.Attach(governmentTabButton, "Civic", "Review town status, civic effects, and owner-facing public pressure.");
            UITooltipRegistry.Attach(resourcesTabButton, "Resources", "Review mineral districts, proto-sites, and future remote-industry pressure.");
            UITooltipRegistry.Attach(closeButton, "Close", "Close the management desk.");

            UITooltipRegistry.Attach(focusPropertyButton, "Inspect Site", "Move the camera to the selected property or business.");
            UITooltipRegistry.Attach(decreaseMarginButton, "Previous Option", "Adjust the selected property's current option.");
            UITooltipRegistry.Attach(increaseMarginButton, "Next Option", "Adjust the selected property's current option.");
            UITooltipRegistry.Attach(previousWorkerButton, "Previous Worker", "Move to the previous worker candidate or assigned staff member.");
            UITooltipRegistry.Attach(nextWorkerButton, "Next Worker", "Move to the next worker candidate or assigned staff member.");
            UITooltipRegistry.Attach(assignWorkerButton, "Staff Action", "Hire or dismiss the selected worker when the current property supports staffing.");
            UITooltipRegistry.Attach(buildProjectButton, "Property Action", "Build, start, or manage the selected property action.");

            UITooltipRegistry.Attach(cashTransferDepositOneButton, "Deposit $1", "Move $1.00 from Liquid Cash into the selected business cash account.");
            UITooltipRegistry.Attach(cashTransferDepositTenButton, "Deposit $10", "Move $10.00 from Liquid Cash into the selected business cash account.");
            UITooltipRegistry.Attach(cashTransferDepositHundredButton, "Deposit $100", "Move $100.00 from Liquid Cash into the selected business cash account.");
            UITooltipRegistry.Attach(cashTransferWithdrawOneButton, "Withdraw $1", "Move $1.00 from business cash back to Liquid Cash.");
            UITooltipRegistry.Attach(cashTransferWithdrawTenButton, "Withdraw $10", "Move $10.00 from business cash back to Liquid Cash.");
            UITooltipRegistry.Attach(cashTransferWithdrawHundredButton, "Withdraw $100", "Move $100.00 from business cash back to Liquid Cash.");
            UITooltipRegistry.Attach(cashTransferAutoToggleButton, "Auto Reserve", "Turn automatic refill-below and sweep-above business cash rules on or off.");
            UITooltipRegistry.Attach(cashTransferLowerMinusTenButton, "Refill Below Down", "Lower the cash level that triggers an owner-cash refill by $10.00.");
            UITooltipRegistry.Attach(cashTransferLowerPlusTenButton, "Refill Below Up", "Raise the cash level that triggers an owner-cash refill by $10.00.");
            UITooltipRegistry.Attach(cashTransferUpperMinusTenButton, "Sweep Above Down", "Lower the cash level that sweeps surplus business cash by $10.00.");
            UITooltipRegistry.Attach(cashTransferUpperPlusTenButton, "Sweep Above Up", "Raise the cash level that sweeps surplus business cash by $10.00.");

            UITooltipRegistry.Attach(loanIncreaseOneButton, "Add $1", "Increase the requested bank loan by $1.00.");
            UITooltipRegistry.Attach(loanIncreaseTenButton, "Add $10", "Increase the requested bank loan by $10.00.");
            UITooltipRegistry.Attach(loanIncreaseHundredButton, "Add $100", "Increase the requested bank loan by $100.00.");
            UITooltipRegistry.Attach(loanDecreaseOneButton, "Remove $1", "Decrease the requested bank loan by $1.00.");
            UITooltipRegistry.Attach(loanDecreaseTenButton, "Remove $10", "Decrease the requested bank loan by $10.00.");
            UITooltipRegistry.Attach(loanDecreaseHundredButton, "Remove $100", "Decrease the requested bank loan by $100.00.");
            UITooltipRegistry.Attach(loanSubmitButton, "Submit Loan Application", "Send the current loan draft into formal lender review, readiness screening, and commitment testing.");
            UITooltipRegistry.Attach(loanWorkflowButton, "Open Loan Application", "Open the formal loan review workflow.");

            UITooltipRegistry.Attach(acquisitionLandButton, "Land Listings", "Show parcels available for purchase.");
            UITooltipRegistry.Attach(acquisitionBusinessesButton, "Business Listings", "Show operating businesses available for acquisition.");
            UITooltipRegistry.Attach(acquisitionWorkflowButton, "Open Formal Acquisition", "Open the selected lead in the full acquisition workflow.");
            UITooltipRegistry.Attach(acquisitionFocusButton, "Inspect Site", "Move the camera to the selected lead when it has a town location.");
            UITooltipRegistry.Attach(previousListingButton, "Previous Lead", "Select the previous lead in the active acquisition section.");
            UITooltipRegistry.Attach(nextListingButton, "Next Lead", "Select the next lead in the active acquisition section.");
            UITooltipRegistry.Attach(buyListingButton, "Advance Process", "Advance the selected lead through inquiry, earnest, diligence, terms, and closing.");
            UITooltipRegistry.Attach(governmentFocusButton, "View Civic Center", "Move the camera to the civic center or relevant government location.");
        }

        public void ShowTab(ManagementPanelTab tab)
        {
            SetRootActive(propertiesContentRoot, tab == ManagementPanelTab.Properties);
            SetRootActive(financesContentRoot, tab == ManagementPanelTab.Finances);
            SetRootActive(acquisitionsContentRoot, tab == ManagementPanelTab.Acquisitions);
            SetRootActive(governmentContentRoot, tab == ManagementPanelTab.Government);
            SetRootActive(resourcesContentRoot, tab == ManagementPanelTab.Resources);
            SetActiveTab(tab);
        }

        public void SetStatusText(string message)
        {
            TMP_Text label = statusText != null || Application.isPlaying ? EnsureStatusText(Application.isPlaying) : statusText;
            if (label == null)
            {
                return;
            }

            string trimmed = string.IsNullOrWhiteSpace(message) ? "Ready." : message.Trim();
            label.text = $"Process Status: {trimmed}";
            label.color = statusTextColor;
            label.gameObject.SetActive(true);
        }

        public void SetActiveTab(ManagementPanelTab tab)
        {
            SetButtonSelected(propertiesTabButton, tab == ManagementPanelTab.Properties);
            SetButtonSelected(financesTabButton, tab == ManagementPanelTab.Finances);
            SetButtonSelected(acquisitionsTabButton, tab == ManagementPanelTab.Acquisitions);
            SetButtonSelected(governmentTabButton, tab == ManagementPanelTab.Government);
            SetButtonSelected(resourcesTabButton, tab == ManagementPanelTab.Resources);
        }

        public void SetAcquisitionSectionActive(bool landActive)
        {
            SetButtonSelected(acquisitionLandButton, landActive);
            SetButtonSelected(acquisitionBusinessesButton, !landActive);
        }

        public void SetButtonSelected(Button button, bool selected)
        {
            ApplySelectionVisual(button, selected);
        }

        public void SetVisible(bool visible)
        {
            RectTransform root = PanelRoot;
            if (root != null)
            {
                root.gameObject.SetActive(visible);
            }
        }

        private void OnValidate()
        {
            // OnValidate can run during Unity's internal consistency checks, including while
            // play mode is entering. Keep it lookup-only: do not call BindFromChildren() here,
            // because BindFromChildren can create/parent UI objects and Unity rejects that work
            // during Awake, CheckConsistency, and OnValidate.
            panelRoot = panelRoot != null ? panelRoot : transform as RectTransform;
            titleText = titleText != null ? titleText : FindText(transform, "Panel_Title");
            hintText = hintText != null ? hintText : FindText(transform, "Panel_HintText");
            statusText = statusText != null ? statusText : FindText(transform, "Panel_StatusText");
        }

        private TMP_Text EnsureStatusText(bool canCreate)
        {
            Transform root = transform;
            statusText = statusText != null ? statusText : FindText(root, "Panel_StatusText");
            if (statusText != null)
            {
                ConfigureStatusText();
                return statusText;
            }

            if (!canCreate)
            {
                return null;
            }

            RectTransform parent = hintText != null
                ? hintText.transform.parent as RectTransform
                : titleText != null
                ? titleText.transform.parent as RectTransform
                : PanelRoot;
            if (parent == null)
            {
                return null;
            }

            statusText = CreateText("Panel_StatusText", parent, "Process Status: Ready for review.", 14f, FontStyles.Bold);
            ConfigureStatusText();
            return statusText;
        }

        private void ConfigureStatusText()
        {
            if (statusText == null)
            {
                return;
            }

            statusText.fontSize = 14f;
            statusText.fontStyle = FontStyles.Bold;
            statusText.alignment = TextAlignmentOptions.Left;
            statusText.color = statusTextColor;
            statusText.textWrappingMode = TextWrappingModes.Normal;

            LayoutElement layout = statusText.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = statusText.gameObject.AddComponent<LayoutElement>();
            }

            layout.minHeight = 26f;
            layout.preferredHeight = 32f;
            layout.flexibleWidth = 1f;
        }

        private void ApplySelectionVisual(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            if (button.targetGraphic is Graphic background)
            {
                Color nextColor = selected ? selectedButtonColor : unselectedButtonColor;
                if (background.color != nextColor)
                {
                    background.color = nextColor;
                }
            }
            else if (button.TryGetComponent(out Graphic graphic))
            {
                Color nextColor = selected ? selectedButtonColor : unselectedButtonColor;
                if (graphic.color != nextColor)
                {
                    graphic.color = nextColor;
                }
            }

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label == null)
            {
                return;
            }

            Color nextTextColor = selected ? selectedTextColor : unselectedTextColor;
            FontStyles nextStyle = selected ? FontStyles.Bold : FontStyles.Normal;
            if (label.color != nextTextColor)
            {
                label.color = nextTextColor;
            }

            if (label.fontStyle != nextStyle)
            {
                label.fontStyle = nextStyle;
            }
        }

        public void EnsureCloseButton(bool canCreate)
        {
            Transform parent = closeButton != null
                ? closeButton.transform.parent
                : governmentTabButton != null
                ? governmentTabButton.transform.parent
                : propertiesTabButton != null
                ? propertiesTabButton.transform.parent
                : null;
            Button template = governmentTabButton != null ? governmentTabButton : propertiesTabButton;
            if (parent == null || template == null)
            {
                return;
            }

            if (closeButton == null && canCreate)
            {
                closeButton = Instantiate(template, parent);
                closeButton.name = "Tab_Close";
            }

            if (closeButton == null)
            {
                return;
            }

            EnsureCloseSpacer(parent, closeButton.transform, canCreate);
            ConfigureActionButton(closeButton, "Close", 86f);
            ApplyButtonTypography(closeButton, LandLedgersTypography.TextRole.TabLabel);
            closeButton.gameObject.SetActive(true);
            closeButton.transform.SetAsLastSibling();
        }

        public void EnsureResourcesControls(bool canCreate)
        {
            Transform tabParent = governmentTabButton != null
                ? governmentTabButton.transform.parent
                : propertiesTabButton != null ? propertiesTabButton.transform.parent : null;
            Button tabTemplate = governmentTabButton != null ? governmentTabButton : propertiesTabButton;
            if (resourcesTabButton == null && canCreate && tabParent != null && tabTemplate != null)
            {
                resourcesTabButton = Instantiate(tabTemplate, tabParent);
                resourcesTabButton.name = "Tab_Resources";
            }

            if (resourcesTabButton != null)
            {
                ConfigureActionButton(resourcesTabButton, "Resources", 140f);
                ApplyButtonTypography(resourcesTabButton, LandLedgersTypography.TextRole.TabLabel);
                resourcesTabButton.gameObject.SetActive(true);
            }

            RectTransform parent = governmentContentRoot != null
                ? governmentContentRoot.parent as RectTransform
                : acquisitionsContentRoot != null ? acquisitionsContentRoot.parent as RectTransform : null;
            if (resourcesContentRoot == null && canCreate && parent != null)
            {
                resourcesContentRoot = CreateRect("Resources Content", parent);
            }

            if (resourcesContentRoot == null)
            {
                return;
            }

            ConfigureSectionRoot(resourcesContentRoot, 160f);
            Image image = resourcesContentRoot.GetComponent<Image>() ?? resourcesContentRoot.gameObject.AddComponent<Image>();
            image.color = new Color(0.075f, 0.085f, 0.075f, 0.82f);
            image.raycastTarget = true;

            LayoutElement element = resourcesContentRoot.GetComponent<LayoutElement>() ?? resourcesContentRoot.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = -1f;
            element.flexibleWidth = 1f;
            element.flexibleHeight = 1f;

            resourcesSummaryText = EnsureSectionBody(
                resourcesContentRoot,
                "ResourcesSummary_Section",
                "Regional Summary",
                resourcesSummaryText,
                "ResourcesSummaryText",
                "Regional resource summary loads at runtime.",
                92f,
                canCreate);
            resourcesDistrictListText = EnsureSectionBody(
                resourcesContentRoot,
                "ResourcesDistrictList_Section",
                "Districts",
                resourcesDistrictListText,
                "ResourcesDistrictListText",
                "Mineral district list loads at runtime.",
                136f,
                canCreate);
            resourcesDetailText = EnsureSectionBody(
                resourcesContentRoot,
                "ResourcesDetail_Section",
                "Site Detail",
                resourcesDetailText,
                "ResourcesDetailText",
                "Future prospecting, claims, camps, and industrial detail will anchor here.",
                136f,
                canCreate);
        }

        private static void EnsureCloseSpacer(Transform parent, Transform closeTransform, bool canCreate)
        {
            if (parent == null || closeTransform == null || parent.GetComponent<HorizontalLayoutGroup>() == null)
            {
                return;
            }

            Transform spacer = FindChild(parent, "Tab_CloseSpacer");
            if (spacer == null)
            {
                if (!canCreate)
                {
                    return;
                }

                GameObject spacerObject = new("Tab_CloseSpacer", typeof(RectTransform), typeof(LayoutElement));
                spacerObject.transform.SetParent(parent, false);
                spacer = spacerObject.transform;
            }

            LayoutElement spacerLayout = spacer.GetComponent<LayoutElement>() ?? spacer.gameObject.AddComponent<LayoutElement>();
            spacerLayout.minWidth = 8f;
            spacerLayout.preferredWidth = 8f;
            spacerLayout.flexibleWidth = 1f;
            spacerLayout.ignoreLayout = false;
            spacer.SetSiblingIndex(Mathf.Max(0, closeTransform.GetSiblingIndex()));
        }

        private void EnsureMarginActionButtons(bool canCreate)
        {
            Transform parent = focusPropertyButton != null ? focusPropertyButton.transform.parent : null;
            Button template = focusPropertyButton != null ? focusPropertyButton : assignWorkerButton;
            if (parent == null || template == null)
            {
                return;
            }

            if (decreaseMarginButton == null && canCreate)
            {
                decreaseMarginButton = Instantiate(template, parent);
                decreaseMarginButton.name = "DecreaseMarginButton";
            }

            if (increaseMarginButton == null && canCreate)
            {
                increaseMarginButton = Instantiate(template, parent);
                increaseMarginButton.name = "IncreaseMarginButton";
            }

            if (decreaseMarginButton == null || increaseMarginButton == null)
            {
                return;
            }

            ConfigureActionButton(decreaseMarginButton, "- Margin", 86f);
            ConfigureActionButton(increaseMarginButton, "+ Margin", 86f);

            if (!canCreate)
            {
                return;
            }

            focusPropertyButton.transform.SetSiblingIndex(0);
            decreaseMarginButton.transform.SetSiblingIndex(1);
            increaseMarginButton.transform.SetSiblingIndex(2);
            nextWorkerButton?.transform.SetSiblingIndex(3);
            previousWorkerButton?.transform.SetSiblingIndex(4);
            assignWorkerButton?.transform.SetSiblingIndex(5);
            buildProjectButton?.transform.SetSiblingIndex(6);
        }

        private void EnsureAcquisitionActionButtons(bool canCreate)
        {
            Transform parent = buyListingButton != null ? buyListingButton.transform.parent : null;
            if (parent == null)
            {
                return;
            }

            ConfigureAcquisitionActionsRoot(parent as RectTransform);

            if (acquisitionFocusButton == null && canCreate)
            {
                acquisitionFocusButton = Instantiate(buyListingButton, parent);
                acquisitionFocusButton.name = "AcquisitionFocusButton";
            }

            if (acquisitionWorkflowButton == null && canCreate)
            {
                acquisitionWorkflowButton = Instantiate(buyListingButton, parent);
                acquisitionWorkflowButton.name = "AcquisitionWorkflowButton";
            }

            if (acquisitionFocusButton == null)
            {
                return;
            }

            ConfigureActionButton(acquisitionWorkflowButton, "Open Formal Acquisition", 214f, true);
            ConfigureActionButton(buyListingButton, "Advance Process", 166f, true);
            ConfigureActionButton(acquisitionFocusButton, "Inspect Site", 126f);
            ConfigureActionButton(previousListingButton, "Previous Lead", 132f);
            ConfigureActionButton(nextListingButton, "Next Lead", 116f);

            if (!canCreate)
            {
                return;
            }

            SetSiblingIndexIfNeeded(acquisitionWorkflowButton?.transform, 0);
            SetSiblingIndexIfNeeded(buyListingButton.transform, 1);
            SetSiblingIndexIfNeeded(acquisitionFocusButton.transform, 2);
            SetSiblingIndexIfNeeded(previousListingButton.transform, 3);
            SetSiblingIndexIfNeeded(nextListingButton.transform, 4);
        }

        public void EnsurePropertyDetailScrollBody(bool canCreate)
        {
            RectTransform detailPanel = propertyDetailTitleText != null
                ? propertyDetailTitleText.transform.parent as RectTransform
                : propertyDetailSummaryText != null
                ? propertyDetailSummaryText.transform.parent as RectTransform
                : null;
            if (detailPanel == null)
            {
                return;
            }

            propertyDetailBodyScrollRoot = propertyDetailBodyScrollRoot != null ? propertyDetailBodyScrollRoot : FindRect(detailPanel, "PropertyDetail_BodyScroll");
            propertyDetailBodyContent = propertyDetailBodyContent != null ? propertyDetailBodyContent : FindRect(detailPanel, "PropertyDetail_BodyContent");
            if (propertyDetailBodyScrollRoot == null)
            {
                if (!canCreate)
                {
                    return;
                }

                propertyDetailBodyScrollRoot = CreateRect("PropertyDetail_BodyScroll", detailPanel);
                RectTransform actionRoot = focusPropertyButton != null ? focusPropertyButton.transform.parent as RectTransform : null;
                int actionIndex = actionRoot != null && actionRoot.parent == detailPanel
                    ? actionRoot.GetSiblingIndex()
                    : detailPanel.childCount - 1;
                propertyDetailBodyScrollRoot.SetSiblingIndex(Mathf.Max(0, actionIndex));
            }

            ConfigureScrollRoot(propertyDetailBodyScrollRoot, ref propertyDetailBodyContent, "PropertyDetail_BodyViewport", "PropertyDetail_BodyContent", canCreate);
            RectTransform storeSections = storeOverviewText != null ? storeOverviewText.transform.parent as RectTransform : null;
            if (storeSections != null && storeSections.name == "PropertyDetail_StoreUpperRow")
            {
                storeSections = storeSections.parent as RectTransform;
            }

            storeSections ??= rentingOverviewText != null ? rentingOverviewText.transform.parent as RectTransform : null;
            if (storeSections == null && propertyDetailBodyContent != null && canCreate)
            {
                storeSections = CreateRect("PropertyDetail_StoreSections", propertyDetailBodyContent);
                VerticalLayoutGroup layout = storeSections.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.childAlignment = TextAnchor.UpperLeft;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
                layout.spacing = 8f;
                LayoutElement element = storeSections.gameObject.AddComponent<LayoutElement>();
                element.preferredHeight = -1f;
                element.flexibleWidth = 1f;
                element.flexibleHeight = 0f;
            }

            if (storeSections != null
                && propertyDetailBodyContent != null
                && storeSections != propertyDetailBodyContent
                && storeSections.parent != propertyDetailBodyContent
                && storeSections.name == "PropertyDetail_StoreSections")
            {
                storeSections.SetParent(propertyDetailBodyContent, false);
                storeSections.SetAsFirstSibling();
            }

            if (rentingOverviewText == null && storeSections != null && canCreate)
            {
                rentingOverviewText = CreateText("RentingOverviewText", storeSections, "Renting & Occupancy", 14f, FontStyles.Normal);
            }

            if (storeSections != null)
            {
                RectTransform upperRow = FindRect(storeSections, "PropertyDetail_StoreUpperRow");
                if (upperRow != null)
                {
                    storeOverviewText ??= FindText(upperRow, "StoreOverviewText");
                    storeFinanceText ??= FindText(upperRow, "StoreFinanceText");
                    RetireStoreUpperRow(upperRow);
                }

                if (storeOverviewText != null && storeOverviewText.transform.parent != storeSections)
                {
                    storeOverviewText.transform.SetParent(storeSections, false);
                }

                if (storeFinanceText == null && canCreate)
                {
                    storeFinanceText = CreateText("StoreFinanceText", storeSections, "Store ledger loads at runtime.", 14f, FontStyles.Normal);
                }
                else if (storeFinanceText.transform.parent != storeSections)
                {
                    storeFinanceText.transform.SetParent(storeSections, false);
                }
            }

            if (rentingOverviewText != null)
            {
                rentingOverviewText.transform.SetSiblingIndex(0);
            }

            ConfigureFinanceText(rentingOverviewText);
            ConfigureFinanceText(storeOverviewText);
            ConfigureFinanceText(storeFinanceText);
            ConfigureFinanceText(storeStaffingText);
            ConfigureFinanceText(storeStockText);
            RefreshPropertyDetailLayout();
            InstallPropertyDetailScrollRelays();
        }

        public void EnsureFinanceDashboardControls(bool canCreate)
        {
            RectTransform parent = financesContentRoot;
            if (parent == null)
            {
                return;
            }

            financesScrollRoot = financesScrollRoot != null ? financesScrollRoot : FindRect(parent, "Finances_Scroll");
            financesScrollContent = financesScrollContent != null ? financesScrollContent : FindRect(parent, "Finances_ScrollContent");
            if (financesScrollRoot == null)
            {
                if (!canCreate)
                {
                    return;
                }

                financesScrollRoot = CreateRect("Finances_Scroll", parent);
                financesScrollRoot.SetAsFirstSibling();
            }

            ConfigureScrollRoot(financesScrollRoot, ref financesScrollContent, "Finances_Viewport", "Finances_ScrollContent", canCreate);
            if (financesScrollContent == null)
            {
                return;
            }

            RectTransform topRow = EnsureDashboardRow(financesScrollContent, "Finances_TopRow", canCreate);
            RectTransform middleRow = EnsureDashboardRow(financesScrollContent, "Finances_MiddleRow", canCreate);
            RectTransform bottomRow = EnsureDashboardRow(financesScrollContent, "Finances_BottomRow", canCreate);

            if (topRow != null)
            {
                financesSummaryText = EnsureSectionBody(topRow, "FinancesPortfolio_Section", "Portfolio Ledger", financesSummaryText, "FinancesSummaryText", "Portfolio body loads at runtime.", 132f, canCreate);
                financesDebtPressureText = EnsureSectionBody(topRow, "FinancesDebtPressure_Section", "Debt / Lender Pressure", financesDebtPressureText, "FinancesDebtPressureText", "Debt pressure loads at runtime.", 132f, canCreate);
            }

            if (middleRow != null)
            {
                financesDistributionText = EnsureSectionBody(middleRow, "FinancesDistribution_Section", "Owner Draw / Transfers", financesDistributionText, "FinancesDistributionText", "Distribution body loads at runtime.", 112f, canCreate);
                financesAffordabilityText = EnsureSectionBody(middleRow, "FinancesAffordability_Section", "Commitment Capacity", financesAffordabilityText, "FinancesAffordabilityText", "Commitment body loads at runtime.", 112f, canCreate);
            }

            RectTransform breakdownSection = bottomRow != null ? FindRect(bottomRow, "FinancesBusinessBreakdown_Section") : null;
            if (breakdownSection == null && bottomRow != null && canCreate)
            {
                breakdownSection = CreateRect("FinancesBusinessBreakdown_Section", bottomRow);
                ConfigureSectionRoot(breakdownSection, 272f);
                CreateText("FinancesBusinessBreakdown_Section_HeaderText", breakdownSection, "Business Breakdown", 16f, FontStyles.Bold);
            }

            if (breakdownSection != null)
            {
                ConfigureSectionRoot(breakdownSection, 272f);
                if (businessBreakdownContent == null)
                {
                    businessBreakdownContent = FindRect(breakdownSection, "BusinessBreakdown_Content");
                }
                if (businessBreakdownContent == null && canCreate)
                {
                    businessBreakdownContent = CreateRect("BusinessBreakdown_Content", breakdownSection);
                }

                if (businessBreakdownContent != null && businessBreakdownContent.parent != breakdownSection)
                {
                    businessBreakdownContent.SetParent(breakdownSection, false);
                }

                if (businessBreakdownContent != null)
                {
                    businessBreakdownContent.SetAsLastSibling();
                    VerticalLayoutGroup breakdownLayout = businessBreakdownContent.GetComponent<VerticalLayoutGroup>() ?? businessBreakdownContent.gameObject.AddComponent<VerticalLayoutGroup>();
                    breakdownLayout.childAlignment = TextAnchor.UpperLeft;
                    breakdownLayout.childControlWidth = true;
                    breakdownLayout.childControlHeight = true;
                    breakdownLayout.childForceExpandWidth = true;
                    breakdownLayout.childForceExpandHeight = false;
                    breakdownLayout.spacing = 5f;
                    LayoutElement breakdownElement = businessBreakdownContent.GetComponent<LayoutElement>() ?? businessBreakdownContent.gameObject.AddComponent<LayoutElement>();
                    breakdownElement.preferredHeight = -1f;
                    breakdownElement.flexibleWidth = 1f;
                    breakdownElement.flexibleHeight = 0f;
                }
            }

            financeBusinessRowTemplate = financeBusinessRowTemplate != null ? financeBusinessRowTemplate : FindButton(financesScrollContent, "FinanceBusinessRow_Template");
            if (financeBusinessRowTemplate == null && businessBreakdownContent != null && canCreate)
            {
                financeBusinessRowTemplate = CreateLoanButton("FinanceBusinessRow_Template", businessBreakdownContent, "Business finance row", 860f);
                financeBusinessRowTemplate.gameObject.SetActive(false);
            }

            if (financeBusinessRowTemplate != null)
            {
                ConfigureFinanceRowButton(financeBusinessRowTemplate, "Business finance row");
                financeBusinessRowTemplate.gameObject.SetActive(false);
            }
        }

        private void EnsureAcquisitionDashboardControls(bool canCreate)
        {
            RectTransform parent = acquisitionsContentRoot;
            if (parent == null)
            {
                return;
            }

            RectTransform body = FindRect(parent, "AcquisitionBody");
            RectTransform topRow = body != null ? FindRect(body, "Acquisition_TopRow") : null;
            RectTransform bottomRow = body != null ? FindRect(body, "Acquisition_BottomRow") : null;
            if (canCreate)
            {
                body ??= CreateRect("AcquisitionBody", parent);
                ConfigureDashboardColumn(body, 8f);
                topRow ??= EnsureDashboardRow(body, "Acquisition_TopRow", true);
                bottomRow ??= EnsureDashboardRow(body, "Acquisition_BottomRow", true);
                RectTransform actions = FindRect(parent, "AcquisitionActions");
                if (actions != null)
                {
                    if (actions.GetSiblingIndex() != parent.childCount - 1)
                    {
                        actions.SetAsLastSibling();
                    }

                    int targetBodyIndex = Mathf.Max(0, actions.GetSiblingIndex() - 1);
                    SetSiblingIndexIfNeeded(body, targetBodyIndex);
                }
                else
                {
                    if (body.GetSiblingIndex() != parent.childCount - 1)
                    {
                        body.SetAsLastSibling();
                    }
                }
            }

            RectTransform activeParent = topRow != null ? topRow : parent;
            RectTransform quietParent = topRow != null ? topRow : parent;
            RectTransform selectedParent = bottomRow != null ? bottomRow : parent;
            RectTransform historyParent = bottomRow != null ? bottomRow : parent;

            acquisitionForSaleText = EnsureSectionBody(
                activeParent,
                "AcquisitionForSale_Section",
                "Active Leads",
                acquisitionForSaleText,
                "AcquisitionForSale_BodyText",
                "Active leads load at runtime.",
                132f,
                canCreate);
            acquisitionOffMarketText = EnsureSectionBody(
                quietParent,
                "AcquisitionOffMarket_Section",
                "Quiet / Off-Market Leads",
                acquisitionOffMarketText,
                "AcquisitionOffMarket_BodyText",
                "Quiet leads load at runtime.",
                150f,
                canCreate);
            acquisitionSelectedLeadText = EnsureSectionBody(
                selectedParent,
                "AcquisitionSelectedLead_Section",
                "Selected Lead / Readiness",
                acquisitionSelectedLeadText,
                "AcquisitionSelectedLead_BodyText",
                "Selected lead details load at runtime.",
                150f,
                canCreate);
            acquisitionHistoryText = EnsureSectionBody(
                historyParent,
                "AcquisitionHistory_Section",
                "Process Ledger / Next Action",
                acquisitionHistoryText,
                "AcquisitionHistory_BodyText",
                "Acquisition history loads at runtime.",
                118f,
                canCreate);

            acquisitionForSaleSectionRoot = acquisitionForSaleText != null
                ? acquisitionForSaleText.transform.parent as RectTransform
                : acquisitionForSaleSectionRoot;
            acquisitionOffMarketSectionRoot = acquisitionOffMarketText != null
                ? acquisitionOffMarketText.transform.parent as RectTransform
                : acquisitionOffMarketSectionRoot;
            acquisitionSelectedLeadSectionRoot = acquisitionSelectedLeadText != null
                ? acquisitionSelectedLeadText.transform.parent as RectTransform
                : acquisitionSelectedLeadSectionRoot;
            acquisitionHistorySectionRoot = acquisitionHistoryText != null
                ? acquisitionHistoryText.transform.parent as RectTransform
                : acquisitionHistorySectionRoot;
        }

        private static void ConfigureAcquisitionActionsRoot(RectTransform actions)
        {
            if (actions == null)
            {
                return;
            }

            HorizontalLayoutGroup layout = actions.GetComponent<HorizontalLayoutGroup>() ?? actions.gameObject.AddComponent<HorizontalLayoutGroup>();
            SetHorizontalLayout(layout, TextAnchor.UpperLeft, 8f, false, false, new RectOffset(0, 0, 0, 0));

            LayoutElement element = actions.GetComponent<LayoutElement>() ?? actions.gameObject.AddComponent<LayoutElement>();
            SetLayoutElement(element, -1f, 44f, 1f, 0f);
        }

        private static void SetSiblingIndexIfNeeded(Transform transform, int index)
        {
            if (transform == null || transform.parent == null)
            {
                return;
            }

            int clamped = Mathf.Clamp(index, 0, transform.parent.childCount - 1);
            if (transform.GetSiblingIndex() != clamped)
            {
                transform.SetSiblingIndex(clamped);
            }
        }

        private static void SetHorizontalLayout(
            HorizontalLayoutGroup layout,
            TextAnchor alignment,
            float spacing,
            bool forceExpandWidth,
            bool forceExpandHeight,
            RectOffset padding)
        {
            if (layout == null)
            {
                return;
            }

            layout.childAlignment = alignment;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = forceExpandWidth;
            layout.childForceExpandHeight = forceExpandHeight;
            layout.spacing = spacing;
            layout.padding = padding;
        }

        private static void SetLayoutElement(LayoutElement element, float preferredWidth, float preferredHeight, float flexibleWidth, float flexibleHeight)
        {
            if (element == null)
            {
                return;
            }

            element.preferredWidth = preferredWidth;
            element.preferredHeight = preferredHeight;
            element.flexibleWidth = flexibleWidth;
            element.flexibleHeight = flexibleHeight;
        }

        private static void ConfigureDashboardColumn(RectTransform column, float spacing)
        {
            if (column == null)
            {
                return;
            }

            VerticalLayoutGroup layout = column.GetComponent<VerticalLayoutGroup>() ?? column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = spacing;
            layout.padding = new RectOffset(0, 0, 0, 0);

            LayoutElement element = column.GetComponent<LayoutElement>() ?? column.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = -1f;
            element.flexibleWidth = 1f;
            element.flexibleHeight = 1f;
        }

        private void EnsureGovernmentDashboardControls(bool canCreate)
        {
            RectTransform parent = governmentContentRoot;
            if (parent == null)
            {
                return;
            }

            governmentSummaryText = EnsureSectionBody(
                parent,
                "GovernmentSummary_Section",
                "Town Status",
                governmentSummaryText,
                "GovernmentSummary_BodyText",
                "Town status loads at runtime.",
                104f,
                canCreate);
            governmentEffectsText = EnsureSectionBody(
                parent,
                "GovernmentEffects_Section",
                "Civic Effects / Pressure",
                governmentEffectsText,
                "GovernmentEffects_BodyText",
                "Civic effects load at runtime.",
                154f,
                canCreate);
            governmentOwnerImplicationsText = EnsureSectionBody(
                parent,
                "GovernmentOwnerImplications_Section",
                "Owner Implications",
                governmentOwnerImplicationsText,
                "GovernmentOwnerImplications_BodyText",
                "Owner implications load at runtime.",
                118f,
                canCreate);

            governmentSummarySectionRoot = governmentSummaryText != null
                ? governmentSummaryText.transform.parent as RectTransform
                : governmentSummarySectionRoot;
            governmentEffectsSectionRoot = governmentEffectsText != null
                ? governmentEffectsText.transform.parent as RectTransform
                : governmentEffectsSectionRoot;
            governmentOwnerImplicationsSectionRoot = governmentOwnerImplicationsText != null
                ? governmentOwnerImplicationsText.transform.parent as RectTransform
                : governmentOwnerImplicationsSectionRoot;
        }

        public void EnsureBusinessCashTransferControls(bool canCreate)
        {
            ResolveManagementFont();
            Transform root = transform;
            cashTransferRoot = cashTransferRoot != null ? cashTransferRoot : FindRect(root, "BusinessCashTransfer_Root");
            cashTransferTitleText = cashTransferTitleText != null ? cashTransferTitleText : FindText(root, "BusinessCashTransfer_TitleText");
            cashTransferHelpText = cashTransferHelpText != null ? cashTransferHelpText : FindText(root, "BusinessCashTransfer_HelpText");
            cashTransferThresholdText = cashTransferThresholdText != null ? cashTransferThresholdText : FindText(root, "BusinessCashTransfer_ThresholdText");
            cashTransferStatusText = cashTransferStatusText != null ? cashTransferStatusText : FindText(root, "BusinessCashTransfer_StatusText");
            cashTransferDepositOneButton = cashTransferDepositOneButton != null ? cashTransferDepositOneButton : FindButton(root, "BusinessCashTransfer_Deposit1Button");
            cashTransferDepositTenButton = cashTransferDepositTenButton != null ? cashTransferDepositTenButton : FindButton(root, "BusinessCashTransfer_Deposit10Button");
            cashTransferDepositHundredButton = cashTransferDepositHundredButton != null ? cashTransferDepositHundredButton : FindButton(root, "BusinessCashTransfer_Deposit100Button");
            cashTransferDepositInput = cashTransferDepositInput != null ? cashTransferDepositInput : FindInput(root, "BusinessCashTransfer_DepositInput");
            cashTransferDepositExactButton = cashTransferDepositExactButton != null ? cashTransferDepositExactButton : FindButton(root, "BusinessCashTransfer_DepositExactButton");
            cashTransferWithdrawOneButton = cashTransferWithdrawOneButton != null ? cashTransferWithdrawOneButton : FindButton(root, "BusinessCashTransfer_Withdraw1Button");
            cashTransferWithdrawTenButton = cashTransferWithdrawTenButton != null ? cashTransferWithdrawTenButton : FindButton(root, "BusinessCashTransfer_Withdraw10Button");
            cashTransferWithdrawHundredButton = cashTransferWithdrawHundredButton != null ? cashTransferWithdrawHundredButton : FindButton(root, "BusinessCashTransfer_Withdraw100Button");
            cashTransferWithdrawInput = cashTransferWithdrawInput != null ? cashTransferWithdrawInput : FindInput(root, "BusinessCashTransfer_WithdrawInput");
            cashTransferWithdrawExactButton = cashTransferWithdrawExactButton != null ? cashTransferWithdrawExactButton : FindButton(root, "BusinessCashTransfer_WithdrawExactButton");
            cashTransferAutoToggleButton = cashTransferAutoToggleButton != null ? cashTransferAutoToggleButton : FindButton(root, "BusinessCashTransfer_AutoToggleButton");
            cashTransferLowerThresholdInput = cashTransferLowerThresholdInput != null ? cashTransferLowerThresholdInput : FindInput(root, "BusinessCashTransfer_LowerThresholdInput");
            cashTransferUpperThresholdInput = cashTransferUpperThresholdInput != null ? cashTransferUpperThresholdInput : FindInput(root, "BusinessCashTransfer_UpperThresholdInput");
            cashTransferApplyReserveButton = cashTransferApplyReserveButton != null ? cashTransferApplyReserveButton : FindButton(root, "BusinessCashTransfer_ApplyReserveButton");
            cashTransferLowerMinusTenButton = cashTransferLowerMinusTenButton != null ? cashTransferLowerMinusTenButton : FindButton(root, "BusinessCashTransfer_LowerMinus10Button");
            cashTransferLowerPlusTenButton = cashTransferLowerPlusTenButton != null ? cashTransferLowerPlusTenButton : FindButton(root, "BusinessCashTransfer_LowerPlus10Button");
            cashTransferUpperMinusTenButton = cashTransferUpperMinusTenButton != null ? cashTransferUpperMinusTenButton : FindButton(root, "BusinessCashTransfer_UpperMinus10Button");
            cashTransferUpperPlusTenButton = cashTransferUpperPlusTenButton != null ? cashTransferUpperPlusTenButton : FindButton(root, "BusinessCashTransfer_UpperPlus10Button");

            RectTransform parent = ResolveBusinessCashTransferParent();
            if (cashTransferRoot != null)
            {
                PlaceBusinessCashTransferRoot(parent);
            }

            if (!canCreate || cashTransferRoot != null || parent == null)
            {
                ConfigureBusinessCashTransferControls();
                return;
            }

            cashTransferRoot = CreateRect("BusinessCashTransfer_Root", parent);
            VerticalLayoutGroup rootLayout = cashTransferRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            rootLayout.childAlignment = TextAnchor.UpperLeft;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;
            rootLayout.spacing = 6f;
            LayoutElement rootElement = cashTransferRoot.gameObject.AddComponent<LayoutElement>();
            rootElement.minHeight = CashTransferOverlaySize.y;
            rootElement.preferredHeight = CashTransferOverlaySize.y;
            rootElement.flexibleWidth = 1f;
            PlaceBusinessCashTransferRoot(parent);

            cashTransferTitleText = CreateText("BusinessCashTransfer_TitleText", cashTransferRoot, "Business Cash Transfers", 16f, FontStyles.Bold);
            cashTransferHelpText = CreateText("BusinessCashTransfer_HelpText", cashTransferRoot, "Business Cash Position", 12f, FontStyles.Normal);
            cashTransferThresholdText = CreateText("BusinessCashTransfer_ThresholdText", cashTransferRoot, "Thresholds unavailable.", 12f, FontStyles.Normal);

            CreateText("BusinessCashTransfer_TransferGroupLabel", cashTransferRoot, "Manual Transfer", 12f, FontStyles.Bold);
            RectTransform exactRow = CreateButtonRow("BusinessCashTransfer_ExactTransferRow", cashTransferRoot);
            cashTransferDepositInput = CreateCashTransferInput("BusinessCashTransfer_DepositInput", exactRow, "Exact Deposit", 150f);
            cashTransferDepositExactButton = CreateLoanButton("BusinessCashTransfer_DepositExactButton", exactRow, "Deposit Exact", 118f);
            cashTransferWithdrawInput = CreateCashTransferInput("BusinessCashTransfer_WithdrawInput", exactRow, "Exact Withdrawal", 160f);
            cashTransferWithdrawExactButton = CreateLoanButton("BusinessCashTransfer_WithdrawExactButton", exactRow, "Withdraw Exact", 126f);

            RectTransform amountRow = CreateButtonRow("BusinessCashTransfer_AmountButtonRow", cashTransferRoot);
            cashTransferDepositOneButton = CreateLoanButton("BusinessCashTransfer_Deposit1Button", amountRow, "Deposit $1", 94f);
            cashTransferDepositTenButton = CreateLoanButton("BusinessCashTransfer_Deposit10Button", amountRow, "$10", 58f);
            cashTransferDepositHundredButton = CreateLoanButton("BusinessCashTransfer_Deposit100Button", amountRow, "$100", 66f);
            cashTransferWithdrawOneButton = CreateLoanButton("BusinessCashTransfer_Withdraw1Button", amountRow, "Withdraw $1", 102f);
            cashTransferWithdrawTenButton = CreateLoanButton("BusinessCashTransfer_Withdraw10Button", amountRow, "$10", 58f);
            cashTransferWithdrawHundredButton = CreateLoanButton("BusinessCashTransfer_Withdraw100Button", amountRow, "$100", 66f);

            CreateText("BusinessCashTransfer_AutoGroupLabel", cashTransferRoot, "Reserve Automation", 12f, FontStyles.Bold);
            RectTransform thresholdRow = CreateButtonRow("BusinessCashTransfer_ThresholdInputRow", cashTransferRoot);
            cashTransferAutoToggleButton = CreateLoanButton("BusinessCashTransfer_AutoToggleButton", thresholdRow, "Auto Reserve On", 134f);
            cashTransferLowerThresholdInput = CreateCashTransferInput("BusinessCashTransfer_LowerThresholdInput", thresholdRow, "Refill Below", 135f);
            cashTransferUpperThresholdInput = CreateCashTransferInput("BusinessCashTransfer_UpperThresholdInput", thresholdRow, "Sweep Above", 135f);
            cashTransferApplyReserveButton = CreateLoanButton("BusinessCashTransfer_ApplyReserveButton", thresholdRow, "Apply Reserve Rules", 150f);

            RectTransform autoRow = CreateButtonRow("BusinessCashTransfer_AutoButtonRow", cashTransferRoot);
            cashTransferLowerMinusTenButton = CreateLoanButton("BusinessCashTransfer_LowerMinus10Button", autoRow, "Refill -$10", 96f);
            cashTransferLowerPlusTenButton = CreateLoanButton("BusinessCashTransfer_LowerPlus10Button", autoRow, "Refill +$10", 96f);
            cashTransferUpperMinusTenButton = CreateLoanButton("BusinessCashTransfer_UpperMinus10Button", autoRow, "Sweep -$10", 96f);
            cashTransferUpperPlusTenButton = CreateLoanButton("BusinessCashTransfer_UpperPlus10Button", autoRow, "Sweep +$10", 96f);

            cashTransferStatusText = CreateText("BusinessCashTransfer_StatusText", cashTransferRoot, "No cash transfers yet.", 12f, FontStyles.Normal);
            ConfigureBusinessCashTransferControls();
        }

        private RectTransform ResolveBusinessCashTransferParent()
        {
            if (propertyDetailBodyContent != null)
            {
                return propertyDetailBodyContent;
            }

            Canvas canvas = GetComponentInParent<Canvas>(true);
            Transform searchRoot = canvas != null ? canvas.transform : transform.root;
            RectTransform hudRoot = FindRect(searchRoot, "HUD_Root");
            if (hudRoot != null)
            {
                return hudRoot;
            }

            RectTransform canvasRect = canvas != null ? canvas.GetComponent<RectTransform>() : null;
            if (canvasRect != null)
            {
                return canvasRect;
            }

            return transform as RectTransform;
        }

        private void PlaceBusinessCashTransferRoot(RectTransform parent)
        {
            if (cashTransferRoot == null || parent == null)
            {
                return;
            }

            if (cashTransferRoot.parent != parent)
            {
                cashTransferRoot.SetParent(parent, false);
            }

            bool insidePropertyScroll = propertyDetailBodyContent != null && parent == propertyDetailBodyContent;
            LayoutElement layout = cashTransferRoot.GetComponent<LayoutElement>() ?? cashTransferRoot.gameObject.AddComponent<LayoutElement>();
            if (insidePropertyScroll)
            {
                cashTransferRoot.anchorMin = new Vector2(0f, 1f);
                cashTransferRoot.anchorMax = new Vector2(1f, 1f);
                cashTransferRoot.pivot = new Vector2(0.5f, 1f);
                cashTransferRoot.anchoredPosition = Vector2.zero;
                cashTransferRoot.sizeDelta = Vector2.zero;
                layout.ignoreLayout = false;
                layout.minWidth = -1f;
                layout.preferredWidth = -1f;
                layout.minHeight = 344f;
                layout.preferredHeight = 364f;
                layout.flexibleWidth = 1f;
                layout.flexibleHeight = 0f;
                cashTransferRoot.SetAsLastSibling();
                return;
            }

            cashTransferRoot.anchorMin = new Vector2(0f, 0f);
            cashTransferRoot.anchorMax = new Vector2(0f, 0f);
            cashTransferRoot.pivot = new Vector2(0f, 0f);
            cashTransferRoot.anchoredPosition = CashTransferOverlayOffset;
            cashTransferRoot.sizeDelta = CashTransferOverlaySize;
            layout.ignoreLayout = true;
            layout.minWidth = CashTransferOverlaySize.x;
            layout.preferredWidth = CashTransferOverlaySize.x;
            layout.minHeight = CashTransferOverlaySize.y;
            layout.preferredHeight = CashTransferOverlaySize.y;
            layout.flexibleWidth = 0f;
            layout.flexibleHeight = 0f;
            cashTransferRoot.SetAsLastSibling();
        }

        private void ConfigureBusinessCashTransferControls()
        {
            ConfigureCashTransferRootLayout();
            ApplyManagementFont(cashTransferTitleText);
            ApplyManagementFont(cashTransferHelpText);
            ApplyManagementFont(cashTransferThresholdText);
            ApplyManagementFont(cashTransferStatusText);
            ConfigureLoanButton(cashTransferDepositOneButton, "Deposit $1", 94f);
            ConfigureLoanButton(cashTransferDepositTenButton, "$10", 58f);
            ConfigureLoanButton(cashTransferDepositHundredButton, "$100", 66f);
            ConfigureCashTransferInput(cashTransferDepositInput, "Exact Deposit", 150f);
            ConfigureLoanButton(cashTransferDepositExactButton, "Deposit Exact", 118f);
            ConfigureLoanButton(cashTransferWithdrawOneButton, "Withdraw $1", 102f);
            ConfigureLoanButton(cashTransferWithdrawTenButton, "$10", 58f);
            ConfigureLoanButton(cashTransferWithdrawHundredButton, "$100", 66f);
            ConfigureCashTransferInput(cashTransferWithdrawInput, "Exact Withdrawal", 160f);
            ConfigureLoanButton(cashTransferWithdrawExactButton, "Withdraw Exact", 126f);
            ConfigureLoanButton(cashTransferAutoToggleButton, "Auto Reserve On", 134f);
            ConfigureCashTransferInput(cashTransferLowerThresholdInput, "Refill Below", 135f);
            ConfigureCashTransferInput(cashTransferUpperThresholdInput, "Sweep Above", 135f);
            ConfigureLoanButton(cashTransferApplyReserveButton, "Apply Reserve Rules", 150f);
            ConfigureLoanButton(cashTransferLowerMinusTenButton, "Refill -$10", 96f);
            ConfigureLoanButton(cashTransferLowerPlusTenButton, "Refill +$10", 96f);
            ConfigureLoanButton(cashTransferUpperMinusTenButton, "Sweep -$10", 96f);
            ConfigureLoanButton(cashTransferUpperPlusTenButton, "Sweep +$10", 96f);
            ApplyManagementFontToCashTransferButtons();
        }

        private void ConfigureStoreBusinessSectionLayout()
        {
            RectTransform storeSections = propertyDetailBodyContent != null
                ? propertyDetailBodyContent.Find("PropertyDetail_StoreSections") as RectTransform
                : null;
            RectTransform upperRow = storeSections != null
                ? storeSections.Find("PropertyDetail_StoreUpperRow") as RectTransform
                : null;

            if (storeSections != null)
            {
                MoveToParent(storeOverviewText, storeSections);
                MoveToParent(storeFinanceText, storeSections);
                MoveToParent(storeStaffingText, storeSections);
                MoveToParent(storeStockText, storeSections);

                VerticalLayoutGroup sectionLayout = storeSections.GetComponent<VerticalLayoutGroup>() ?? storeSections.gameObject.AddComponent<VerticalLayoutGroup>();
                sectionLayout.spacing = 8f;
                sectionLayout.padding = new RectOffset(0, 0, 0, 0);
                sectionLayout.childAlignment = TextAnchor.UpperLeft;
                sectionLayout.childControlWidth = true;
                sectionLayout.childControlHeight = true;
                sectionLayout.childForceExpandWidth = true;
                sectionLayout.childForceExpandHeight = false;
            }

            if (upperRow != null)
            {
                RetireStoreUpperRow(upperRow);
            }

            ConfigureStoreSectionText(storeOverviewText, -1f, 88f, 1f, TextAlignmentOptions.TopLeft);
            ConfigureStoreSectionText(storeFinanceText, -1f, 118f, 1f, TextAlignmentOptions.TopLeft);
            ConfigureStoreSectionText(storeStaffingText, -1f, 88f, 1f, TextAlignmentOptions.TopLeft);
            ConfigureStoreSectionText(storeStockText, -1f, 108f, 1f, TextAlignmentOptions.TopLeft);
        }

        private static void RetireStoreUpperRow(RectTransform upperRow)
        {
            if (upperRow == null)
            {
                return;
            }

            HorizontalLayoutGroup horizontal = upperRow.GetComponent<HorizontalLayoutGroup>();
            if (horizontal != null)
            {
                horizontal.enabled = false;
            }

            LayoutElement layout = upperRow.GetComponent<LayoutElement>() ?? upperRow.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            layout.minHeight = 0f;
            layout.preferredHeight = 0f;
            layout.flexibleHeight = 0f;
            upperRow.gameObject.SetActive(false);
        }

        private static void ConfigureStoreSectionText(TMP_Text text, float minWidth, float minHeight, float flexibleWidth, TextAlignmentOptions alignment)
        {
            if (text == null)
            {
                return;
            }

            text.alignment = alignment;
            bool pricingTable = IsPricingMarginTableText(text);
            text.textWrappingMode = pricingTable ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            LayoutElement layout = text.GetComponent<LayoutElement>() ?? text.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = minWidth;
            layout.minHeight = CalculateReadableTextHeight(text, minWidth, pricingTable ? Mathf.Max(minHeight, 140f) : minHeight);
            layout.preferredWidth = -1f;
            layout.preferredHeight = -1f;
            layout.flexibleWidth = flexibleWidth;
            layout.flexibleHeight = 0f;
        }

        private static bool IsPricingMarginTableText(TMP_Text text)
        {
            return text != null
                && string.Equals(text.name, "StoreFinanceText", System.StringComparison.Ordinal)
                && !string.IsNullOrEmpty(text.text)
                && text.text.Contains("Pricing & Margin");
        }

        private static float CalculateReadableTextHeight(TMP_Text text, float minWidth, float baseMinHeight)
        {
            if (text == null || string.IsNullOrEmpty(text.text))
            {
                return Mathf.Max(0f, baseMinHeight);
            }

            RectTransform rect = text.rectTransform;
            float width = rect != null && rect.rect.width > 32f
                ? rect.rect.width
                : minWidth > 32f
                ? minWidth
                : 520f;
            Vector2 preferred = text.GetPreferredValues(text.text, width, 0f);
            return Mathf.Max(0f, baseMinHeight, preferred.y + 8f);
        }

        public void RefreshPropertyDetailLayout()
        {
            string layoutSignature = BuildPropertyDetailLayoutSignature();
            if (string.Equals(lastPropertyDetailLayoutSignature, layoutSignature, System.StringComparison.Ordinal))
            {
                InstallPropertyDetailScrollRelays();
                return;
            }

            lastPropertyDetailLayoutSignature = layoutSignature;
            ScrollRect propertyScrollRect = propertyDetailBodyScrollRoot != null ? propertyDetailBodyScrollRoot.GetComponent<ScrollRect>() : null;
            RectTransform propertyContent = propertyScrollRect != null ? propertyScrollRect.content : propertyDetailBodyContent;
            Vector2 preservedContentPosition = propertyContent != null ? propertyContent.anchoredPosition : Vector2.zero;
            float preservedVerticalPosition = propertyScrollRect != null ? propertyScrollRect.verticalNormalizedPosition : 1f;
            ConfigureStoreBusinessSectionLayout();
            UpdatePropertyDetailSectionVisibility();

            Canvas.ForceUpdateCanvases();
            if (propertyDetailBodyContent != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(propertyDetailBodyContent);
            }

            if (propertyDetailBodyScrollRoot != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(propertyDetailBodyScrollRoot);
            }

            if (propertyScrollRect != null)
            {
                propertyScrollRect.verticalNormalizedPosition = preservedVerticalPosition;
            }

            if (propertyContent != null)
            {
                propertyContent.anchoredPosition = preservedContentPosition;
            }

            InstallPropertyDetailScrollRelays();
        }

        private void InstallPropertyDetailScrollRelays()
        {
            ScrollRect scrollRect = propertyDetailBodyScrollRoot != null ? propertyDetailBodyScrollRoot.GetComponent<ScrollRect>() : null;
            if (scrollRect == null)
            {
                return;
            }

            if (scrollRect.viewport != null)
            {
                ScrollRectInputRelay.Install(scrollRect.viewport, scrollRect);
            }

            InstallScrollInputRelays(propertyDetailBodyContent, scrollRect);
        }

        private string BuildPropertyDetailLayoutSignature()
        {
            return string.Concat(
                rentingOverviewText != null ? rentingOverviewText.text : string.Empty,
                "\u001f",
                storeOverviewText != null ? storeOverviewText.text : string.Empty,
                "\u001f",
                storeFinanceText != null ? storeFinanceText.text : string.Empty,
                "\u001f",
                storeStaffingText != null ? storeStaffingText.text : string.Empty,
                "\u001f",
                storeStockText != null ? storeStockText.text : string.Empty,
                "\u001f",
                cashTransferRoot != null && cashTransferRoot.parent == propertyDetailBodyContent ? cashTransferRoot.gameObject.activeSelf.ToString() : "False",
                "\u001f",
                bankLoanRoot != null && bankLoanRoot.parent == propertyDetailBodyContent ? bankLoanRoot.gameObject.activeSelf.ToString() : "False");
        }

        private void UpdatePropertyDetailSectionVisibility()
        {
            RectTransform storeSections = propertyDetailBodyContent != null
                ? propertyDetailBodyContent.Find("PropertyDetail_StoreSections") as RectTransform
                : null;
            RectTransform upperRow = storeSections != null
                ? storeSections.Find("PropertyDetail_StoreUpperRow") as RectTransform
                : null;

            bool showRenting = HasVisibleContent(rentingOverviewText);
            bool showOverview = HasVisibleContent(storeOverviewText);
            bool showFinance = HasVisibleContent(storeFinanceText);
            bool showStaffing = HasVisibleContent(storeStaffingText);
            bool showStock = HasVisibleContent(storeStockText);

            SetPropertySectionVisible(rentingOverviewText, showRenting);
            SetPropertySectionVisible(storeOverviewText, showOverview);
            SetPropertySectionVisible(storeFinanceText, showFinance);
            SetPropertySectionVisible(storeStaffingText, showStaffing);
            SetPropertySectionVisible(storeStockText, showStock);

            if (upperRow != null)
            {
                RetireStoreUpperRow(upperRow);
            }

            if (rentingOverviewText != null && rentingOverviewText.transform.parent == storeSections)
            {
                rentingOverviewText.transform.SetSiblingIndex(0);
            }

            if (storeOverviewText != null && storeOverviewText.transform.parent == storeSections)
            {
                storeOverviewText.transform.SetSiblingIndex(showRenting ? 1 : 0);
            }

            if (storeFinanceText != null && storeFinanceText.transform.parent == storeSections)
            {
                storeFinanceText.transform.SetSiblingIndex((showRenting ? 1 : 0) + (showOverview ? 1 : 0));
            }

            if (storeStaffingText != null && storeStaffingText.transform.parent == storeSections)
            {
                storeStaffingText.transform.SetSiblingIndex((showRenting ? 1 : 0) + (showOverview ? 1 : 0) + (showFinance ? 1 : 0));
            }

            if (storeStockText != null && storeStockText.transform.parent == storeSections)
            {
                storeStockText.transform.SetSiblingIndex((showRenting ? 1 : 0) + (showOverview ? 1 : 0) + (showFinance ? 1 : 0) + (showStaffing ? 1 : 0));
            }
        }

        private static bool HasVisibleContent(TMP_Text text)
        {
            return text != null && !string.IsNullOrWhiteSpace(text.text);
        }

        private static void SetPropertySectionVisible(TMP_Text text, bool visible)
        {
            if (text == null)
            {
                return;
            }

            text.gameObject.SetActive(visible);
            LayoutElement layout = text.GetComponent<LayoutElement>() ?? text.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = !visible;
        }

        private void ConfigureCashTransferRootLayout()
        {
            if (cashTransferRoot == null)
            {
                return;
            }

            VerticalLayoutGroup rootLayout = cashTransferRoot.GetComponent<VerticalLayoutGroup>() ?? cashTransferRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            rootLayout.childAlignment = TextAnchor.UpperLeft;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;
            rootLayout.spacing = 6f;

            LayoutElement rootElement = cashTransferRoot.GetComponent<LayoutElement>() ?? cashTransferRoot.gameObject.AddComponent<LayoutElement>();
            bool insidePropertyScroll = propertyDetailBodyContent != null && cashTransferRoot.parent == propertyDetailBodyContent;
            rootLayout.padding = insidePropertyScroll ? new RectOffset(0, 0, 8, 14) : new RectOffset(10, 10, 8, 10);

            Image rootImage = cashTransferRoot.GetComponent<Image>() ?? cashTransferRoot.gameObject.AddComponent<Image>();
            rootImage.color = insidePropertyScroll
                ? new Color(0f, 0f, 0f, 0.01f)
                : new Color(0.07f, 0.08f, 0.07f, 0.94f);
            rootImage.raycastTarget = true;

            rootElement.ignoreLayout = !insidePropertyScroll;
            rootElement.minWidth = insidePropertyScroll ? -1f : CashTransferOverlaySize.x;
            rootElement.minHeight = insidePropertyScroll ? 344f : CashTransferOverlaySize.y;
            rootElement.preferredWidth = insidePropertyScroll ? -1f : CashTransferOverlaySize.x;
            rootElement.preferredHeight = insidePropertyScroll ? 364f : CashTransferOverlaySize.y;
            rootElement.flexibleWidth = insidePropertyScroll ? 1f : 0f;
            rootElement.flexibleHeight = 0f;

            if (insidePropertyScroll)
            {
                cashTransferRoot.anchorMin = new Vector2(0f, 1f);
                cashTransferRoot.anchorMax = new Vector2(1f, 1f);
                cashTransferRoot.pivot = new Vector2(0.5f, 1f);
                cashTransferRoot.anchoredPosition = Vector2.zero;
                cashTransferRoot.sizeDelta = Vector2.zero;
                ScrollRect scrollRect = propertyDetailBodyScrollRoot != null ? propertyDetailBodyScrollRoot.GetComponent<ScrollRect>() : null;
                InstallScrollInputRelays(propertyDetailBodyContent, scrollRect);
                InstallScrollInputRelays(cashTransferRoot, scrollRect);
            }
            else
            {
                cashTransferRoot.anchorMin = new Vector2(0f, 0f);
                cashTransferRoot.anchorMax = new Vector2(0f, 0f);
                cashTransferRoot.pivot = new Vector2(0f, 0f);
                cashTransferRoot.anchoredPosition = CashTransferOverlayOffset;
                cashTransferRoot.sizeDelta = CashTransferOverlaySize;
            }

            ConfigureTransferText(cashTransferTitleText, 16f, FontStyles.Bold, 24f);
            ConfigureTransferText(cashTransferHelpText, 12f, FontStyles.Normal, 54f);
            ConfigureTransferText(cashTransferThresholdText, 12f, FontStyles.Normal, 54f);
            ConfigureTransferText(cashTransferStatusText, 12f, FontStyles.Normal, 30f);
        }

        private void ConfigureTransferText(TMP_Text text, float fontSize, FontStyles style, float minHeight)
        {
            if (text == null)
            {
                return;
            }

            ApplyManagementFont(text);
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;

            LayoutElement layout = text.GetComponent<LayoutElement>() ?? text.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = minHeight;
            layout.preferredHeight = -1f;
            layout.flexibleWidth = 1f;
            layout.flexibleHeight = 0f;
        }

        private void ApplyManagementFont(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            LandLedgersTypography.ApplyRole(text, IsLikelyHeader(text) ? LandLedgersTypography.TextRole.SectionHeader : LandLedgersTypography.TextRole.DenseBody);
        }

        private void ResolveManagementFont()
        {
            managementFont = LandLedgersTypography.GetFont(LandLedgersTypography.FontRole.BitterSemiBold);
        }

        private static void ApplyButtonTypography(Button button, LandLedgersTypography.TextRole role)
        {
            if (button == null)
            {
                return;
            }

            LandLedgersTypography.ApplyRole(button.GetComponentInChildren<TMP_Text>(true), role);
        }

        private static bool IsLikelyHeader(TMP_Text text)
        {
            if (text == null)
            {
                return false;
            }

            string name = text.name;
            return name.Contains("Title", System.StringComparison.OrdinalIgnoreCase)
                || name.Contains("Header", System.StringComparison.OrdinalIgnoreCase)
                || name.Contains("Label", System.StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyManagementFontToCashTransferButtons()
        {
            ApplyManagementFont(cashTransferDepositOneButton != null ? cashTransferDepositOneButton.GetComponentInChildren<TMP_Text>(true) : null);
            ApplyManagementFont(cashTransferDepositTenButton != null ? cashTransferDepositTenButton.GetComponentInChildren<TMP_Text>(true) : null);
            ApplyManagementFont(cashTransferDepositHundredButton != null ? cashTransferDepositHundredButton.GetComponentInChildren<TMP_Text>(true) : null);
            ApplyManagementFont(cashTransferDepositExactButton != null ? cashTransferDepositExactButton.GetComponentInChildren<TMP_Text>(true) : null);
            ApplyManagementFont(cashTransferWithdrawOneButton != null ? cashTransferWithdrawOneButton.GetComponentInChildren<TMP_Text>(true) : null);
            ApplyManagementFont(cashTransferWithdrawTenButton != null ? cashTransferWithdrawTenButton.GetComponentInChildren<TMP_Text>(true) : null);
            ApplyManagementFont(cashTransferWithdrawHundredButton != null ? cashTransferWithdrawHundredButton.GetComponentInChildren<TMP_Text>(true) : null);
            ApplyManagementFont(cashTransferWithdrawExactButton != null ? cashTransferWithdrawExactButton.GetComponentInChildren<TMP_Text>(true) : null);
            ApplyManagementFont(cashTransferAutoToggleButton != null ? cashTransferAutoToggleButton.GetComponentInChildren<TMP_Text>(true) : null);
            ApplyManagementFont(cashTransferApplyReserveButton != null ? cashTransferApplyReserveButton.GetComponentInChildren<TMP_Text>(true) : null);
            ApplyManagementFont(cashTransferLowerMinusTenButton != null ? cashTransferLowerMinusTenButton.GetComponentInChildren<TMP_Text>(true) : null);
            ApplyManagementFont(cashTransferLowerPlusTenButton != null ? cashTransferLowerPlusTenButton.GetComponentInChildren<TMP_Text>(true) : null);
            ApplyManagementFont(cashTransferUpperMinusTenButton != null ? cashTransferUpperMinusTenButton.GetComponentInChildren<TMP_Text>(true) : null);
            ApplyManagementFont(cashTransferUpperPlusTenButton != null ? cashTransferUpperPlusTenButton.GetComponentInChildren<TMP_Text>(true) : null);
        }

        public void EnsureBankLoanControls(bool canCreate)
        {
            Transform root = transform;
            bankLoanRoot = bankLoanRoot != null ? bankLoanRoot : FindRect(root, "BankLoan_Root");
            bankLoanTitleText = bankLoanTitleText != null ? bankLoanTitleText : FindText(root, "BankLoan_TitleText");
            bankLoanHelpText = bankLoanHelpText != null ? bankLoanHelpText : FindText(root, "BankLoan_HelpText");
            bankLoanRequestedAmountText = bankLoanRequestedAmountText != null ? bankLoanRequestedAmountText : FindText(root, "BankLoan_RequestedAmountText");
            bankLoanEstimatedPaymentText = bankLoanEstimatedPaymentText != null ? bankLoanEstimatedPaymentText : FindText(root, "BankLoan_EstimatedPaymentText");
            bankLoanPaymentScheduleText = bankLoanPaymentScheduleText != null ? bankLoanPaymentScheduleText : FindText(root, "BankLoan_PaymentScheduleText");
            bankLoanEstimatedTotalText = bankLoanEstimatedTotalText != null ? bankLoanEstimatedTotalText : FindText(root, "BankLoan_EstimatedTotalText");
            bankLoanLenderStandingText = bankLoanLenderStandingText != null ? bankLoanLenderStandingText : FindText(root, "BankLoan_LenderStandingText");
            bankLoanStatusText = bankLoanStatusText != null ? bankLoanStatusText : FindText(root, "BankLoan_StatusText");
            bankLoanActiveLoanText = bankLoanActiveLoanText != null ? bankLoanActiveLoanText : FindText(root, "BankLoan_ActiveLoanText");
            loanIncreaseOneButton = loanIncreaseOneButton != null ? loanIncreaseOneButton : FindButton(root, "LoanAmount_Plus1Button");
            loanIncreaseTenButton = loanIncreaseTenButton != null ? loanIncreaseTenButton : FindButton(root, "LoanAmount_Plus10Button");
            loanIncreaseHundredButton = loanIncreaseHundredButton != null ? loanIncreaseHundredButton : FindButton(root, "LoanAmount_Plus100Button");
            loanDecreaseOneButton = loanDecreaseOneButton != null ? loanDecreaseOneButton : FindButton(root, "LoanAmount_Minus1Button");
            loanDecreaseTenButton = loanDecreaseTenButton != null ? loanDecreaseTenButton : FindButton(root, "LoanAmount_Minus10Button");
            loanDecreaseHundredButton = loanDecreaseHundredButton != null ? loanDecreaseHundredButton : FindButton(root, "LoanAmount_Minus100Button");
            loanSubmitButton = loanSubmitButton != null ? loanSubmitButton : FindButton(root, "BankLoan_SubmitButton");
            loanWorkflowButton = loanWorkflowButton != null ? loanWorkflowButton : FindButton(root, "LoanWorkflowButton");

            RectTransform financeBottomRow = financesScrollContent != null ? FindRect(financesScrollContent, "Finances_BottomRow") : null;
            RectTransform parent = financeBottomRow != null ? financeBottomRow : (financesScrollContent != null ? financesScrollContent : financesContentRoot);
            if (bankLoanRoot != null && parent != null && bankLoanRoot.parent != parent)
            {
                bankLoanRoot.SetParent(parent, false);
                bankLoanRoot.SetAsLastSibling();
            }

            if (!canCreate || bankLoanRoot != null || parent == null)
            {
                ConfigureBankLoanControls();
                return;
            }

            bankLoanRoot = CreateRect("BankLoan_Root", parent);
            VerticalLayoutGroup rootLayout = bankLoanRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            rootLayout.childAlignment = TextAnchor.UpperLeft;
            rootLayout.childControlWidth = true;
            rootLayout.childControlHeight = true;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;
            rootLayout.spacing = 5f;
            LayoutElement rootElement = bankLoanRoot.gameObject.AddComponent<LayoutElement>();
            rootElement.minHeight = 300f;
            rootElement.preferredHeight = 340f;
            rootElement.flexibleWidth = 1f;

            bankLoanTitleText = CreateText("BankLoan_TitleText", bankLoanRoot, "Loan Application", 18f, FontStyles.Bold);
            bankLoanHelpText = CreateText("BankLoan_HelpText", bankLoanRoot, "Review the amount, expected payment, and lender view before you submit the application.", 13f, FontStyles.Normal);
            bankLoanRequestedAmountText = CreateText("BankLoan_RequestedAmountText", bankLoanRoot, "Requested amount: $100.00", 14f, FontStyles.Bold);
            bankLoanEstimatedPaymentText = CreateText("BankLoan_EstimatedPaymentText", bankLoanRoot, "Estimated payment: $0.00 weekly", 13f, FontStyles.Normal);
            bankLoanPaymentScheduleText = CreateText("BankLoan_PaymentScheduleText", bankLoanRoot, "Payment schedule: weekly", 13f, FontStyles.Normal);
            bankLoanEstimatedTotalText = CreateText("BankLoan_EstimatedTotalText", bankLoanRoot, "Estimated total owed: $0.00", 13f, FontStyles.Normal);
            bankLoanLenderStandingText = CreateText("BankLoan_LenderStandingText", bankLoanRoot, "Lender standing: 0%", 13f, FontStyles.Normal);

            RectTransform amountRow = CreateRect("BankLoan_AmountButtonRow", bankLoanRoot);
            HorizontalLayoutGroup amountLayout = amountRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            amountLayout.childAlignment = TextAnchor.MiddleLeft;
            amountLayout.childControlWidth = true;
            amountLayout.childControlHeight = true;
            amountLayout.childForceExpandWidth = false;
            amountLayout.childForceExpandHeight = false;
            amountLayout.spacing = 6f;
            amountRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;

            loanIncreaseOneButton = CreateLoanButton("LoanAmount_Plus1Button", amountRow, "+1");
            loanIncreaseTenButton = CreateLoanButton("LoanAmount_Plus10Button", amountRow, "+10");
            loanIncreaseHundredButton = CreateLoanButton("LoanAmount_Plus100Button", amountRow, "+100");
            loanDecreaseOneButton = CreateLoanButton("LoanAmount_Minus1Button", amountRow, "-1");
            loanDecreaseTenButton = CreateLoanButton("LoanAmount_Minus10Button", amountRow, "-10");
            loanDecreaseHundredButton = CreateLoanButton("LoanAmount_Minus100Button", amountRow, "-100");

            loanWorkflowButton = CreateLoanButton("LoanWorkflowButton", bankLoanRoot, "Open Loan Application", 190f);
            loanSubmitButton = CreateLoanButton("BankLoan_SubmitButton", bankLoanRoot, "Submit Application", 170f);
            bankLoanStatusText = CreateText("BankLoan_StatusText", bankLoanRoot, "No bank loan activity yet.", 13f, FontStyles.Normal);
            bankLoanActiveLoanText = CreateText("BankLoan_ActiveLoanText", bankLoanRoot, "Active loan: none", 12f, FontStyles.Normal);
            ConfigureBankLoanControls();
        }

        private RectTransform CreateButtonRow(string objectName, RectTransform parent)
        {
            RectTransform row = CreateRect(objectName, parent);
            HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = 5f;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;
            return row;
        }

        private TMP_InputField CreateCashTransferInput(string objectName, RectTransform parent, string placeholder, float preferredWidth)
        {
            RectTransform rect = CreateRect(objectName, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.09f, 0.085f, 0.07f, 0.96f);
            image.raycastTarget = true;

            TMP_InputField input = rect.gameObject.AddComponent<TMP_InputField>();
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.DecimalNumber;
            input.characterValidation = TMP_InputField.CharacterValidation.Decimal;
            input.keyboardType = TouchScreenKeyboardType.DecimalPad;
            input.targetGraphic = image;

            TMP_Text placeholderText = CreateInputText("Placeholder", rect, placeholder, new Color(0.62f, 0.58f, 0.49f, 1f));
            TMP_Text valueText = CreateInputText("Text", rect, string.Empty, new Color(0.94f, 0.91f, 0.82f, 1f));
            input.placeholder = placeholderText;
            input.textComponent = valueText;
            ConfigureCashTransferInput(input, placeholder, preferredWidth);
            return input;
        }

        private TMP_Text CreateInputText(string objectName, RectTransform parent, string textValue, Color color)
        {
            RectTransform rect = CreateRect(objectName, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(8f, 3f);
            rect.offsetMax = new Vector2(-8f, -3f);
            TMP_Text text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            CopyTextStyle(financesSummaryText, text);
            ApplyManagementFont(text);
            text.text = textValue;
            text.fontSize = 12f;
            text.fontStyle = FontStyles.Normal;
            text.color = color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        private void ConfigureCashTransferInput(TMP_InputField input, string placeholder, float preferredWidth)
        {
            if (input == null)
            {
                return;
            }

            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.DecimalNumber;
            input.characterValidation = TMP_InputField.CharacterValidation.Decimal;
            input.keyboardType = TouchScreenKeyboardType.DecimalPad;
            input.characterLimit = 16;

            if (input.placeholder is TMP_Text placeholderText)
            {
                LandLedgersTypography.ApplyRole(placeholderText, LandLedgersTypography.TextRole.SectionHeader);
                placeholderText.text = placeholder;
                placeholderText.fontSize = 12f;
                placeholderText.color = new Color(0.62f, 0.58f, 0.49f, 1f);
                ConfigureInputTextRect(placeholderText.rectTransform);
            }

            if (input.textComponent != null)
            {
                LandLedgersTypography.ApplyRole(input.textComponent, LandLedgersTypography.TextRole.SectionHeader);
                input.textComponent.fontSize = 12f;
                input.textComponent.color = new Color(0.94f, 0.91f, 0.82f, 1f);
                ConfigureInputTextRect(input.textComponent.rectTransform);
            }

            LayoutElement layout = input.GetComponent<LayoutElement>() ?? input.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = preferredWidth;
            layout.preferredHeight = 34f;
            layout.minHeight = 34f;
        }

        private static void ConfigureInputTextRect(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(10f, 4f);
            rect.offsetMax = new Vector2(-10f, -4f);
        }

        private void ConfigureBankLoanControls()
        {
            ConfigureLoanButton(loanIncreaseOneButton, "+1", 58f);
            ConfigureLoanButton(loanIncreaseTenButton, "+10", 62f);
            ConfigureLoanButton(loanIncreaseHundredButton, "+100", 70f);
            ConfigureLoanButton(loanDecreaseOneButton, "-1", 58f);
            ConfigureLoanButton(loanDecreaseTenButton, "-10", 62f);
            ConfigureLoanButton(loanDecreaseHundredButton, "-100", 70f);
            ConfigureLoanButton(loanWorkflowButton, "Open Loan Application", 190f);
            ConfigureLoanButton(loanSubmitButton, "Submit Application", 170f);
            ConfigureBankLoanRootLayout();
            ConfigureLoanText(bankLoanTitleText, 28f, FontStyles.Bold);
            ConfigureLoanText(bankLoanHelpText, 34f, FontStyles.Normal);
            ConfigureLoanText(bankLoanRequestedAmountText, 24f, FontStyles.Bold);
            ConfigureLoanText(bankLoanEstimatedPaymentText, 22f, FontStyles.Normal);
            ConfigureLoanText(bankLoanPaymentScheduleText, 22f, FontStyles.Normal);
            ConfigureLoanText(bankLoanEstimatedTotalText, 22f, FontStyles.Normal);
            ConfigureLoanText(bankLoanLenderStandingText, 24f, FontStyles.Normal);
            ConfigureLoanText(bankLoanStatusText, 42f, FontStyles.Normal);
            ConfigureLoanText(bankLoanActiveLoanText, 36f, FontStyles.Normal);
        }

        public void ConfigureFinanceRowButton(Button button, string label)
        {
            if (button == null)
            {
                return;
            }

            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
                text.alignment = TextAlignmentOptions.Left;
                text.textWrappingMode = TextWrappingModes.Normal;
                text.overflowMode = TextOverflowModes.Overflow;
                RectTransform textRect = text.GetComponent<RectTransform>();
                if (textRect != null)
                {
                    textRect.offsetMin = new Vector2(8f, 3f);
                    textRect.offsetMax = new Vector2(-8f, -3f);
                }
            }

            LayoutElement layout = button.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = button.gameObject.AddComponent<LayoutElement>();
            }

            layout.preferredWidth = -1f;
            layout.preferredHeight = 58f;
            layout.minHeight = 48f;
            layout.flexibleWidth = 1f;
            layout.flexibleHeight = 0f;
        }

        private static void ConfigureActionButton(Button button, string label, float preferredWidth, bool primary = false)
        {
            if (button == null)
            {
                return;
            }

            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
                text.fontStyle = FontStyles.Bold;
                text.fontSize = primary ? 14f : 13f;
            }

            LayoutElement layout = button.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.preferredWidth = preferredWidth;
                layout.preferredHeight = primary ? 40f : 38f;
                layout.minHeight = primary ? 38f : 34f;
            }

            Image image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = primary
                    ? new Color(0.24f, 0.18f, 0.09f, 0.98f)
                    : new Color(0.14f, 0.15f, 0.13f, 0.96f);
            }
        }

        private Button CreateLoanButton(string objectName, RectTransform parent, string label, float preferredWidth = 66f)
        {
            RectTransform rect = CreateRect(objectName, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.17f, 0.15f, 0.12f, 0.95f);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ConfigureLoanButton(button, label, preferredWidth);

            RectTransform labelRect = CreateRect("Label", rect);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            TMP_Text text = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
            CopyTextStyle(financesSummaryText, text);
            text.text = label;
            text.fontSize = 13f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            ApplyManagementFont(text);
            return button;
        }

        private static void ConfigureLoanButton(Button button, string label, float preferredWidth)
        {
            if (button == null)
            {
                return;
            }

            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
            }

            LayoutElement layout = button.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = button.gameObject.AddComponent<LayoutElement>();
            }

            layout.preferredWidth = preferredWidth;
            layout.preferredHeight = 34f;
            layout.minHeight = 34f;
        }

        private void ConfigureBankLoanRootLayout()
        {
            if (bankLoanRoot == null)
            {
                return;
            }

            Image image = bankLoanRoot.GetComponent<Image>() ?? bankLoanRoot.gameObject.AddComponent<Image>();
            image.color = new Color(0.07f, 0.08f, 0.07f, 0.9f);
            image.raycastTarget = true;

            VerticalLayoutGroup layout = bankLoanRoot.GetComponent<VerticalLayoutGroup>() ?? bankLoanRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(10, 10, 8, 10);
            layout.spacing = 4f;

            LayoutElement element = bankLoanRoot.GetComponent<LayoutElement>() ?? bankLoanRoot.gameObject.AddComponent<LayoutElement>();
            element.minHeight = 278f;
            element.preferredHeight = 312f;
            element.flexibleWidth = 1f;
            element.flexibleHeight = 0f;
        }

        private void ConfigureLoanText(TMP_Text text, float preferredHeight, FontStyles style)
        {
            if (text == null)
            {
                return;
            }

            ApplyManagementFont(text);
            text.fontStyle = style;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;

            LayoutElement layout = text.GetComponent<LayoutElement>() ?? text.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = preferredHeight;
            layout.minHeight = Mathf.Min(preferredHeight, 22f);
            layout.flexibleWidth = 1f;
            layout.flexibleHeight = 0f;
        }

        private TMP_Text CreateText(string objectName, RectTransform parent, string content, float fontSize, FontStyles fontStyle)
        {
            RectTransform rect = CreateRect(objectName, parent);
            TMP_Text text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            CopyTextStyle(financesSummaryText, text);
            ApplyManagementFont(text);
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = TextAlignmentOptions.Left;
            text.textWrappingMode = TextWrappingModes.Normal;
            LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1f;
            layout.preferredHeight = -1f;
            layout.minHeight = fontSize >= 18f ? 28f : 24f;
            return text;
        }

        private static void ConfigureFinanceText(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            text.textWrappingMode = IsPricingMarginTableText(text) ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            LayoutElement layout = text.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = text.gameObject.AddComponent<LayoutElement>();
            }

            layout.preferredHeight = -1f;
            layout.flexibleWidth = 1f;
            layout.flexibleHeight = 0f;
        }

        private static void ConfigureDashboardCard(TMP_Text text, float minHeight)
        {
            if (text == null || text.gameObject == null)
            {
                return;
            }

            LayoutElement layout = text.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = text.gameObject.AddComponent<LayoutElement>();
            }

            if (layout == null)
            {
                return;
            }

            if (!Mathf.Approximately(layout.minHeight, minHeight))
            {
                layout.minHeight = minHeight;
            }

            if (!Mathf.Approximately(layout.preferredHeight, -1f))
            {
                layout.preferredHeight = -1f;
            }

            if (!Mathf.Approximately(layout.flexibleWidth, 1f))
            {
                layout.flexibleWidth = 1f;
            }

            if (!Mathf.Approximately(layout.flexibleHeight, 0f))
            {
                layout.flexibleHeight = 0f;
            }
        }

        private RectTransform EnsureDashboardRow(RectTransform parent, string rowName, bool canCreate)
        {
            if (parent == null)
            {
                return null;
            }

            RectTransform row = FindRect(parent, rowName);
            if (row == null)
            {
                if (!canCreate)
                {
                    return null;
                }

                row = CreateRect(rowName, parent);
            }

            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>() ?? row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 8f;

            LayoutElement element = row.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = -1f;
            element.flexibleWidth = 1f;
            element.flexibleHeight = 0f;

            return row;
        }

        private TMP_Text EnsureSectionBody(
            RectTransform parent,
            string sectionName,
            string header,
            TMP_Text existingText,
            string bodyName,
            string placeholder,
            float minHeight,
            bool canCreate)
        {
            if (parent == null)
            {
                return existingText;
            }

            RectTransform section = FindRect(parent, sectionName);
            if (section == null)
            {
                if (!canCreate)
                {
                    return existingText;
                }

                section = CreateRect(sectionName, parent);
            }

            ConfigureSectionRoot(section, minHeight);
            TMP_Text headerText = FindText(section, $"{sectionName}_HeaderText");
            if (headerText == null && canCreate)
            {
                headerText = CreateText($"{sectionName}_HeaderText", section, header, 16f, FontStyles.Bold);
            }
            else if (headerText != null)
            {
                headerText.text = header;
            }

            TMP_Text body = existingText != null ? existingText : FindText(section, bodyName);
            if (body == null && canCreate)
            {
                body = CreateText(bodyName, section, placeholder, 14f, FontStyles.Normal);
            }

            if (body != null)
            {
                MoveToParent(body, section);
                body.transform.SetAsLastSibling();
                ConfigureFinanceText(body);
                ConfigureDashboardCard(body, minHeight);
            }

            return body;
        }

        private static void ConfigureSectionRoot(RectTransform section, float minHeight)
        {
            if (section == null)
            {
                return;
            }

            VerticalLayoutGroup layout = section.GetComponent<VerticalLayoutGroup>() ?? section.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 4f;
            layout.padding = new RectOffset(8, 8, 8, 8);

            LayoutElement element = section.GetComponent<LayoutElement>() ?? section.gameObject.AddComponent<LayoutElement>();
            element.minHeight = minHeight;
            element.preferredHeight = -1f;
            element.flexibleWidth = 1f;
            element.flexibleHeight = 0f;
        }

        private static void MoveToParent(Component component, RectTransform parent)
        {
            if (component == null || parent == null || component.transform.parent == parent)
            {
                return;
            }

            component.transform.SetParent(parent, false);
        }

        private void ConfigureScrollRoot(RectTransform scrollRoot, ref RectTransform content, string viewportName, string contentName, bool canCreate)
        {
            if (scrollRoot == null)
            {
                return;
            }

            LayoutElement rootElement = scrollRoot.GetComponent<LayoutElement>() ?? scrollRoot.gameObject.AddComponent<LayoutElement>();
            rootElement.minHeight = 260f;
            rootElement.preferredHeight = -1f;
            rootElement.flexibleWidth = 1f;
            rootElement.flexibleHeight = 1f;

            Image scrollImage = scrollRoot.GetComponent<Image>() ?? scrollRoot.gameObject.AddComponent<Image>();
            scrollImage.color = new Color(0.02f, 0.025f, 0.02f, 0.18f);
            scrollImage.raycastTarget = true;

            ScrollRect scrollRect = scrollRoot.GetComponent<ScrollRect>() ?? scrollRoot.gameObject.AddComponent<ScrollRect>();
            RectTransform viewport = FindRect(scrollRoot, viewportName);
            if (viewport == null)
            {
                if (!canCreate)
                {
                    return;
                }

                viewport = CreateRect(viewportName, scrollRoot);
            }

            RectTransform verticalScrollbar = EnsureVerticalScrollbar(scrollRoot, canCreate);

            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = verticalScrollbar != null ? new Vector2(-15f, 0f) : Vector2.zero;
            Image viewportImage = viewport.GetComponent<Image>() ?? viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
            viewportImage.raycastTarget = true;
            _ = viewport.GetComponent<RectMask2D>() ?? viewport.gameObject.AddComponent<RectMask2D>();

            bool contentWasCreated = false;
            bool contentWasReparented = false;
            content = content != null ? content : FindRect(viewport, contentName);
            if (content == null)
            {
                if (!canCreate)
                {
                    return;
                }

                content = CreateRect(contentName, viewport);
                contentWasCreated = true;
            }

            if (content.parent != viewport)
            {
                content.SetParent(viewport, false);
                contentWasReparented = true;
            }

            bool preserveScrollState = !contentWasCreated && !contentWasReparented;
            Vector2 preservedContentPosition = content.anchoredPosition;
            Vector2 preservedContentSize = content.sizeDelta;
            float preservedVerticalPosition = scrollRect.verticalNormalizedPosition;

            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            if (contentWasCreated || contentWasReparented)
            {
                content.anchoredPosition = Vector2.zero;
            }

            content.sizeDelta = preserveScrollState
                ? new Vector2(0f, preservedContentSize.y)
                : Vector2.zero;
            Image contentImage = content.GetComponent<Image>() ?? content.gameObject.AddComponent<Image>();
            contentImage.color = new Color(0f, 0f, 0f, 0.01f);
            contentImage.raycastTarget = true;
            VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 6f;
            layout.padding = new RectOffset(6, 6, 4, 12);
            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            scrollRect.verticalScrollbar = verticalScrollbar != null ? verticalScrollbar.GetComponent<Scrollbar>() : null;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scrollRect.verticalScrollbarSpacing = 0f;
            ConfigureScrollRectInput(scrollRect);
            ScrollRectInputRelay.Install(viewport, scrollRect);
            InstallScrollInputRelays(content, scrollRect);
            NormalizeScrollContentRaycasts(content);

            if (preserveScrollState)
            {
                scrollRect.verticalNormalizedPosition = preservedVerticalPosition;
                content.anchoredPosition = preservedContentPosition;
            }
        }

        private static RectTransform EnsureVerticalScrollbar(RectTransform scrollRoot, bool canCreate)
        {
            if (scrollRoot == null)
            {
                return null;
            }

            RectTransform scrollbarRect = FindRect(scrollRoot, "VerticalScrollbar");
            if (scrollbarRect == null)
            {
                scrollbarRect = FindRect(scrollRoot, "Scrollbar Vertical");
            }

            if (scrollbarRect == null)
            {
                if (!canCreate)
                {
                    return null;
                }

                GameObject scrollbarObject = new("VerticalScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
                scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
                scrollbarRect.SetParent(scrollRoot, false);

                RectTransform slidingArea = CreateRect("Sliding Area", scrollbarRect);
                slidingArea.anchorMin = Vector2.zero;
                slidingArea.anchorMax = Vector2.one;
                slidingArea.offsetMin = new Vector2(2f, 2f);
                slidingArea.offsetMax = new Vector2(-2f, -2f);

                GameObject handleObject = new("Handle", typeof(RectTransform), typeof(Image));
                RectTransform handle = handleObject.GetComponent<RectTransform>();
                handle.SetParent(slidingArea, false);
                handle.anchorMin = Vector2.zero;
                handle.anchorMax = Vector2.one;
                handle.offsetMin = Vector2.zero;
                handle.offsetMax = Vector2.zero;

                Image handleImage = handleObject.GetComponent<Image>();
                handleImage.color = new Color(0.58f, 0.52f, 0.40f, 0.78f);
                handleImage.raycastTarget = true;

                Scrollbar createdScrollbar = scrollbarObject.GetComponent<Scrollbar>();
                createdScrollbar.handleRect = handle;
                createdScrollbar.direction = Scrollbar.Direction.BottomToTop;
                createdScrollbar.transition = Selectable.Transition.ColorTint;
            }

            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 0.5f);
            scrollbarRect.sizeDelta = new Vector2(12f, 0f);
            scrollbarRect.anchoredPosition = Vector2.zero;

            Image background = scrollbarRect.GetComponent<Image>() ?? scrollbarRect.gameObject.AddComponent<Image>();
            background.color = new Color(0.11f, 0.095f, 0.065f, 0.72f);
            background.raycastTarget = true;

            Scrollbar scrollbar = scrollbarRect.GetComponent<Scrollbar>() ?? scrollbarRect.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.interactable = true;

            RectTransform handleRect = scrollbar.handleRect;
            if (handleRect == null)
            {
                RectTransform slidingArea = FindRect(scrollbarRect, "Sliding Area") ?? CreateRect("Sliding Area", scrollbarRect);
                handleRect = FindRect(slidingArea, "Handle");
                if (handleRect == null && canCreate)
                {
                    GameObject handleObject = new("Handle", typeof(RectTransform), typeof(Image));
                    handleRect = handleObject.GetComponent<RectTransform>();
                    handleRect.SetParent(slidingArea, false);
                }

                scrollbar.handleRect = handleRect;
            }

            if (handleRect != null)
            {
                Image handleImage = handleRect.GetComponent<Image>() ?? handleRect.gameObject.AddComponent<Image>();
                handleImage.color = new Color(0.58f, 0.52f, 0.40f, 0.78f);
                handleImage.raycastTarget = true;
            }

            return scrollbarRect;
        }

        private static void ConfigureScrollRectInput(ScrollRect scrollRect)
        {
            if (scrollRect == null)
            {
                return;
            }

            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = true;
            scrollRect.elasticity = 0f;
            scrollRect.scrollSensitivity = 34f;
        }

        private static void NormalizeScrollContentRaycasts(RectTransform content)
        {
            if (content == null)
            {
                return;
            }

            TMP_Text[] texts = content.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                texts[i].raycastTarget = false;
            }
        }

        private static void InstallScrollInputRelays(RectTransform content, ScrollRect scrollRect)
        {
            if (content == null || scrollRect == null)
            {
                return;
            }

            ScrollRectInputRelay.Install(content, scrollRect);

            LayoutGroup[] layoutGroups = content.GetComponentsInChildren<LayoutGroup>(true);
            for (int i = 0; i < layoutGroups.Length; i++)
            {
                ScrollRectInputRelay.Install(layoutGroups[i], scrollRect);
            }

            Image[] images = content.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i].raycastTarget)
                {
                    ScrollRectInputRelay.Install(images[i], scrollRect);
                }
            }

            Selectable[] selectables = content.GetComponentsInChildren<Selectable>(true);
            for (int i = 0; i < selectables.Length; i++)
            {
                ScrollRectInputRelay.Install(selectables[i], scrollRect);
            }
        }

        private static RectTransform CreateRect(string objectName, RectTransform parent)
        {
            GameObject obj = new(objectName, typeof(RectTransform));
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = Vector2.zero;
            return rect;
        }

        private static void CopyTextStyle(TMP_Text source, TMP_Text target)
        {
            if (target == null || source == null)
            {
                return;
            }

            target.font = source.font;
            target.fontSharedMaterial = source.fontSharedMaterial;
            target.color = source.color;
        }

        private static void SetRootActive(Component root, bool active)
        {
            if (root != null)
            {
                root.gameObject.SetActive(active);
            }
        }

        private static RectTransform FindRect(Transform root, string childName)
        {
            Transform child = FindChild(root, childName);
            return child != null ? child as RectTransform : null;
        }

        private static Button FindButton(Transform root, string childName)
        {
            Transform child = FindChild(root, childName);
            return child != null ? child.GetComponent<Button>() : null;
        }

        private static TMP_Text FindText(Transform root, string childName)
        {
            Transform child = FindChild(root, childName);
            return child != null ? child.GetComponent<TMP_Text>() : null;
        }

        private static TMP_InputField FindInput(Transform root, string childName)
        {
            Transform child = FindChild(root, childName);
            return child != null ? child.GetComponent<TMP_InputField>() : null;
        }

        private static Transform FindChild(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == childName)
                {
                    return children[i];
                }
            }

            return null;
        }
    }
}
