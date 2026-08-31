#if UNITY_EDITOR
using LandLedgers.MVP;
using LandLedgers.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LandLedgers.UI.Editor
{
    public static class ManagementPanelPrefabRepairUtility
    {
        private const string PrefabPath = "Assets/Core/UI/UI Canvas.prefab";
        private const string MainScenePath = "Assets/Main Scene.unity";
        private const string UiCanvasTag = "UI Canvas";
        private static readonly Vector2 CashTransferOverlaySize = new(680f, 330f);
        private static readonly Vector2 CashTransferOverlayOffset = new(24f, 72f);

        [MenuItem("Land & Ledgers/UI/Repair Persistent Management Panel")]
        public static void RepairManagementPanelPrefabAndScene()
        {
            RepairManagementPanelPrefab();
            RepairMainSceneInstance();
        }

        public static void RepairManagementPanelPrefab()
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                LandLedgersTypography.EnsureAllFontAssets();
                LandLedgersTypography.ConfigureTmpSettings();
                TMP_FontAsset managementFont = LandLedgersTypography.GetFont(LandLedgersTypography.FontRole.BitterSemiBold);
                RectTransform canvas = prefabRoot.GetComponent<RectTransform>();
                prefabRoot.name = "UI Canvas";
                SetTagIfAvailable(prefabRoot, UiCanvasTag);
                Transform hudRoot = prefabRoot.transform.Find("HUD_Root");
                if (hudRoot != null)
                {
                    hudRoot.gameObject.SetActive(true);
                }

                RectTransform mainPanel = EnsureRect(prefabRoot.transform, "Main Panel");
                mainPanel.gameObject.SetActive(true);
                ConfigurePanelRect(mainPanel);
                ConfigurePanelImage(mainPanel);
                ConfigureVerticalLayout(mainPanel, 10f, new RectOffset(14, 14, 12, 14));

                RectTransform header = EnsureRect(mainPanel, "Panel_Header");
                header.SetAsFirstSibling();
                ConfigureVerticalLayout(header, 2f, new RectOffset(0, 0, 0, 0));
                ConfigureLayoutElement(header, -1f, 60f, 0f, 0f);

                TMP_Text title = EnsureText(header, "Panel_Title", "Management", 22f, FontStyles.Bold);
                ConfigureTextLayout(title, -1f, 32f, 1f, 0f);
                TMP_Text hint = EnsureText(header, "Panel_HintText", "C: close", 15f, FontStyles.Normal);
                ConfigureTextLayout(hint, -1f, 22f, 1f, 0f);

                Transform oldTitle = mainPanel.Find("Panel_Title");
                if (oldTitle != null && oldTitle.parent == mainPanel)
                {
                    oldTitle.SetParent(header, false);
                    oldTitle.SetAsFirstSibling();
                    if (title != null && title.gameObject != oldTitle.gameObject)
                    {
                        Object.DestroyImmediate(title.gameObject);
                    }
                    title = oldTitle.GetComponent<TMP_Text>() ?? title;
                    ConfigureText(title, "Management", 22f, FontStyles.Bold);
                }

                RectTransform tabBar = EnsureRect(mainPanel, "Management Tab Bar");
                tabBar.SetSiblingIndex(1);
                ConfigureHorizontalLayout(tabBar, 8f, new RectOffset(0, 0, 0, 0));
                ConfigureLayoutElement(tabBar, -1f, 44f, 0f, 0f);

                Button propertiesTab = EnsureButton(tabBar, "Tab_Properties", "Properties", 140f, 40f, 15.5f);
                Button financesTab = EnsureButton(tabBar, "Tab_Finances", "Finances", 140f, 40f, 15.5f);
                Button acquisitionsTab = EnsureButton(tabBar, "Tab_Acquisitions", "Acquisitions", 160f, 40f, 15.5f);
                Button governmentTab = EnsureButton(tabBar, "Tab_Government", "Civic", 140f, 40f, 15.5f);
                Button resourcesTab = EnsureButton(tabBar, "Tab_Resources", "Resources", 140f, 40f, 15.5f);
                AdoptLegacyTab(tabBar, "Properties Tab Panel", propertiesTab.transform);
                AdoptLegacyTab(tabBar, "Finances Tab Panel", financesTab.transform);
                AdoptLegacyTab(tabBar, "Finaces Tab Panel", financesTab.transform);
                AdoptLegacyTab(tabBar, "Acquisitions Tab Panel", acquisitionsTab.transform);

                RectTransform contentRoot = EnsureRect(mainPanel, "Content Root");
                contentRoot.SetSiblingIndex(2);
                ConfigureHorizontalLayout(contentRoot, 0f, new RectOffset(0, 0, 0, 0));
                ConfigureLayoutElement(contentRoot, -1f, -1f, 1f, 1f);

                RectTransform propertiesContent = EnsureRect(contentRoot, "Properties Content");
                ConfigureHorizontalLayout(propertiesContent, 12f, new RectOffset(0, 0, 0, 0));
                ConfigureLayoutElement(propertiesContent, -1f, -1f, 1f, 1f);

                RectTransform propertyList = EnsureRect(propertiesContent, "Property List Column");
                ConfigureVerticalLayout(propertyList, 8f, new RectOffset(10, 10, 10, 10));
                ConfigurePanelImage(propertyList, new Color(0.06f, 0.07f, 0.06f, 0.82f));
                ConfigureLayoutElement(propertyList, 260f, -1f, 0f, 1f);
                TMP_Text propertyListTitle = EnsureText(propertyList, "PropertyList_Title", "Properties", 16f, FontStyles.Bold);
                ConfigureTextLayout(propertyListTitle, -1f, 26f, 1f, 0f);

                RectTransform propertyScroll = EnsureRect(propertyList, "PropertyList_Scroll");
                DestroyDirectChildrenNamed(propertyList, "PropertyList_Content");
                ConfigureLayoutElement(propertyScroll, -1f, -1f, 1f, 1f);
                Image propertyScrollImage = propertyScroll.GetComponent<Image>() ?? propertyScroll.gameObject.AddComponent<Image>();
                propertyScrollImage.color = new Color(0.02f, 0.025f, 0.02f, 0.35f);
                propertyScrollImage.raycastTarget = true;
                ScrollRect scrollRect = propertyScroll.GetComponent<ScrollRect>() ?? propertyScroll.gameObject.AddComponent<ScrollRect>();
                RectTransform propertyViewport = EnsureRect(propertyScroll, "PropertyList_Viewport");
                propertyViewport.anchorMin = Vector2.zero;
                propertyViewport.anchorMax = Vector2.one;
                propertyViewport.offsetMin = Vector2.zero;
                propertyViewport.offsetMax = Vector2.zero;
                _ = propertyViewport.GetComponent<RectMask2D>() ?? propertyViewport.gameObject.AddComponent<RectMask2D>();
                RectTransform propertyListContent = EnsureRect(propertyViewport, "PropertyList_Content");
                propertyListContent.anchorMin = new Vector2(0f, 1f);
                propertyListContent.anchorMax = new Vector2(1f, 1f);
                propertyListContent.pivot = new Vector2(0.5f, 1f);
                propertyListContent.anchoredPosition = Vector2.zero;
                propertyListContent.sizeDelta = new Vector2(0f, 0f);
                ConfigureVerticalLayout(propertyListContent, 6f, new RectOffset(4, 4, 4, 4));
                ContentSizeFitter fitter = propertyListContent.GetComponent<ContentSizeFitter>() ?? propertyListContent.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                scrollRect.viewport = propertyViewport;
                scrollRect.content = propertyListContent;
                ConfigureScrollRectInput(scrollRect);
                NormalizeScrollContentRaycasts(propertyListContent);

                Button propertyRowTemplate = EnsureButton(propertyListContent, "PropertyRow_Template", "Property", 224f, 38f, 15f);
                propertyRowTemplate.gameObject.SetActive(false);

                RectTransform detailPanel = EnsureRect(propertiesContent, "Property Detail Panel");
                ConfigureVerticalLayout(detailPanel, 10f, new RectOffset(12, 12, 10, 12));
                ConfigurePanelImage(detailPanel, new Color(0.075f, 0.085f, 0.075f, 0.82f));
                ConfigureLayoutElement(detailPanel, -1f, -1f, 1f, 1f);
                TMP_Text detailTitle = EnsureText(detailPanel, "PropertyDetail_Title", "Select a Property", 20f, FontStyles.Bold);
                ConfigureTextLayout(detailTitle, -1f, 32f, 1f, 0f);
                TMP_Text detailSummary = EnsureText(detailPanel, "PropertyDetail_Summary", "Property details load here once available.", 15f, FontStyles.Normal);
                ConfigureTextLayout(detailSummary, -1f, 30f, 1f, 0f);

                RectTransform propertyBodyScroll = EnsureRect(detailPanel, "PropertyDetail_BodyScroll");
                RectTransform propertyBodyContent = ConfigureScrollArea(propertyBodyScroll, "PropertyDetail_BodyViewport", "PropertyDetail_BodyContent");
                ConfigureLayoutElement(propertyBodyScroll, -1f, -1f, 1f, 1f);
                LayoutElement propertyBodyLayout = propertyBodyScroll.GetComponent<LayoutElement>() ?? propertyBodyScroll.gameObject.AddComponent<LayoutElement>();
                propertyBodyLayout.minHeight = 260f;

                MoveDirectChildIfNeeded(detailPanel, propertyBodyContent, "PropertyDetail_StoreSections");
                RectTransform storeSections = EnsureRect(propertyBodyContent, "PropertyDetail_StoreSections");
                ConfigureVerticalLayout(storeSections, 6f, new RectOffset(0, 0, 0, 0));
                ConfigureLayoutElement(storeSections, -1f, -1f, 1f, 0f);
                TMP_Text rentingOverview = EnsureSectionText(storeSections, "RentingOverviewText", "Renting & Occupancy");
                RectTransform storeUpperRow = FindRect(storeSections, "PropertyDetail_StoreUpperRow");
                if (storeUpperRow != null)
                {
                    MoveDirectChildIfNeeded(storeUpperRow, storeSections, "StoreOverviewText");
                    MoveDirectChildIfNeeded(storeUpperRow, storeSections, "StoreFinanceText");
                    RetireStoreUpperRow(storeUpperRow);
                }
                TMP_Text overview = EnsureSectionText(storeSections, "StoreOverviewText", "Overview");
                TMP_Text finance = EnsureSectionText(storeSections, "StoreFinanceText", "Store Ledger");
                TMP_Text staffing = EnsureSectionText(storeSections, "StoreStaffingText", "Staffing");
                TMP_Text stock = EnsureSectionText(storeSections, "StoreStockText", "Stock");
                rentingOverview.transform.SetSiblingIndex(0);
                overview.transform.SetSiblingIndex(1);
                finance.transform.SetSiblingIndex(2);
                staffing.transform.SetSiblingIndex(3);
                stock.transform.SetSiblingIndex(4);
                ConfigureScrollableText(rentingOverview);
                ConfigureScrollableText(overview);
                ConfigureScrollableText(finance);
                ConfigureScrollableText(staffing);
                ConfigureScrollableText(stock);
                ConfigureTextLayout(overview, -1f, -1f, 1f, 0f);
                LayoutElement overviewLayout = overview.GetComponent<LayoutElement>() ?? overview.gameObject.AddComponent<LayoutElement>();
                overviewLayout.minWidth = -1f;
                overviewLayout.minHeight = 88f;
                overview.alignment = TextAlignmentOptions.TopLeft;
                ConfigureTextLayout(finance, -1f, -1f, 1f, 0f);
                LayoutElement financeLayout = finance.GetComponent<LayoutElement>() ?? finance.gameObject.AddComponent<LayoutElement>();
                financeLayout.minWidth = -1f;
                financeLayout.minHeight = 118f;
                finance.alignment = TextAlignmentOptions.TopLeft;
                finance.textWrappingMode = TextWrappingModes.NoWrap;
                finance.overflowMode = TextOverflowModes.Overflow;
                ConfigureTextLayout(staffing, -1f, -1f, 1f, 0f);
                LayoutElement staffingLayout = staffing.GetComponent<LayoutElement>() ?? staffing.gameObject.AddComponent<LayoutElement>();
                staffingLayout.minHeight = 88f;
                ConfigureTextLayout(stock, -1f, -1f, 1f, 0f);
                LayoutElement stockLayout = stock.GetComponent<LayoutElement>() ?? stock.gameObject.AddComponent<LayoutElement>();
                stockLayout.minHeight = 108f;
                CashTransferRefs cashTransfer = EnsureBusinessCashTransferBlock(ResolveBusinessCashTransferParent(prefabRoot.transform), storeSections);
                ApplyBusinessTypography(detailTitle, detailSummary, overview, finance, staffing, stock);
                ApplyBusinessTypographyToCashTransfer(cashTransfer);

                RectTransform propertyActions = EnsureRect(detailPanel, "PropertyDetail_Actions");
                ConfigureHorizontalLayout(propertyActions, 8f, new RectOffset(0, 0, 0, 0));
                ConfigureLayoutElement(propertyActions, -1f, 44f, 0f, 0f);
                Button focusButton = EnsureButton(propertyActions, "FocusPropertyButton", "Inspect Site", 112f, 38f);
                Button decreaseMarginButton = EnsureButton(propertyActions, "DecreaseMarginButton", "Price -", 86f, 38f, 14f);
                Button increaseMarginButton = EnsureButton(propertyActions, "IncreaseMarginButton", "Price +", 86f, 38f, 14f);
                Button prevWorkerButton = EnsureButton(propertyActions, "PrevWorkerButton", "Prev Worker", 126f, 38f);
                Button nextWorkerButton = EnsureButton(propertyActions, "NextWorkerButton", "Next Worker", 126f, 38f);
                Button assignWorkerButton = EnsureButton(propertyActions, "AssignWorkerButton", "Hire", 104f, 38f);
                Button buildProjectButton = EnsureButton(propertyActions, "BuildProjectButton", "Build", 112f, 38f);
                focusButton.transform.SetSiblingIndex(0);
                decreaseMarginButton.transform.SetSiblingIndex(1);
                increaseMarginButton.transform.SetSiblingIndex(2);
                nextWorkerButton.transform.SetSiblingIndex(3);
                prevWorkerButton.transform.SetSiblingIndex(4);
                assignWorkerButton.transform.SetSiblingIndex(5);
                buildProjectButton.transform.SetSiblingIndex(6);
                buildProjectButton.interactable = false;
                buildProjectButton.gameObject.SetActive(false);

                RectTransform financesContent = EnsureRect(contentRoot, "Finances Content");
                ConfigureVerticalLayout(financesContent, 8f, new RectOffset(12, 12, 10, 12));
                ConfigurePanelImage(financesContent, new Color(0.075f, 0.085f, 0.075f, 0.82f));
                ConfigureLayoutElement(financesContent, -1f, -1f, 1f, 1f);

                RectTransform financesScroll = EnsureRect(financesContent, "Finances_Scroll");
                RectTransform financesScrollContent = ConfigureScrollArea(financesScroll, "Finances_Viewport", "Finances_ScrollContent");
                ConfigureLayoutElement(financesScroll, -1f, -1f, 1f, 1f);
                RectTransform financesTopRow = EnsureRect(financesScrollContent, "Finances_TopRow");
                ConfigureHorizontalLayout(financesTopRow, 12f, new RectOffset(0, 0, 0, 0));
                ConfigureLayoutElement(financesTopRow, -1f, -1f, 1f, 0f);
                TMP_Text financesSummary = EnsureSectionPanel(financesTopRow, "FinancesPortfolio_Section", "Portfolio Ledger", "FinancesSummaryText", "Portfolio body loads at runtime.", 132f);
                TMP_Text financesDebtPressure = EnsureSectionPanel(financesTopRow, "FinancesDebtPressure_Section", "Debt / Lender Pressure", "FinancesDebtPressureText", "Debt pressure loads at runtime.", 132f);

                RectTransform financesMiddleRow = EnsureRect(financesScrollContent, "Finances_MiddleRow");
                ConfigureHorizontalLayout(financesMiddleRow, 12f, new RectOffset(0, 0, 0, 0));
                ConfigureLayoutElement(financesMiddleRow, -1f, -1f, 1f, 0f);
                TMP_Text financesDistribution = EnsureSectionPanel(financesMiddleRow, "FinancesDistribution_Section", "Owner Draw / Transfers", "FinancesDistributionText", "Distribution body loads at runtime.", 112f);
                TMP_Text financesAffordability = EnsureSectionPanel(financesMiddleRow, "FinancesAffordability_Section", "Commitment Capacity", "FinancesAffordabilityText", "Commitment body loads at runtime.", 112f);

                RectTransform financesBottomRow = EnsureRect(financesScrollContent, "Finances_BottomRow");
                ConfigureHorizontalLayout(financesBottomRow, 12f, new RectOffset(0, 0, 0, 0));
                ConfigureLayoutElement(financesBottomRow, -1f, -1f, 1f, 0f);
                RectTransform businessBreakdownSection = EnsureRect(financesBottomRow, "FinancesBusinessBreakdown_Section");
                ConfigureVerticalLayout(businessBreakdownSection, 6f, new RectOffset(10, 10, 8, 10));
                ConfigurePanelImage(businessBreakdownSection, new Color(0.06f, 0.07f, 0.06f, 0.82f));
                ConfigureLayoutElement(businessBreakdownSection, -1f, 272f, 1f, 0f);
                TMP_Text businessBreakdownTitle = EnsureText(businessBreakdownSection, "FinancesBusinessBreakdown_HeaderText", "Business Breakdown", 16f, FontStyles.Bold);
                ConfigureTextLayout(businessBreakdownTitle, -1f, 24f, 1f, 0f);
                RectTransform businessBreakdown = EnsureRect(businessBreakdownSection, "BusinessBreakdown_Content");
                ConfigureVerticalLayout(businessBreakdown, 6f, new RectOffset(0, 0, 0, 0));
                ConfigureLayoutElement(businessBreakdown, -1f, -1f, 1f, 0f);
                Button financeBusinessRowTemplate = EnsureButton(businessBreakdown, "FinanceBusinessRow_Template", "Business finance row", 860f, 58f, 13f);
                financeBusinessRowTemplate.gameObject.SetActive(false);

                RectTransform bankLoanRoot = EnsureRect(financesBottomRow, "BankLoan_Root");
                ConfigureVerticalLayout(bankLoanRoot, 5f, new RectOffset(10, 10, 8, 10));
                ConfigurePanelImage(bankLoanRoot, new Color(0.07f, 0.08f, 0.07f, 0.9f));
                ConfigureLayoutElement(bankLoanRoot, -1f, 312f, 1f, 0f);
                TMP_Text bankLoanTitle = EnsureText(bankLoanRoot, "BankLoan_TitleText", "Bank Loan Desk", 18f, FontStyles.Bold);
                TMP_Text bankLoanHelp = EnsureText(bankLoanRoot, "BankLoan_HelpText", "Set the request, check payment pressure, then send it to the bank.", 13f, FontStyles.Normal);
                TMP_Text bankLoanRequestedAmount = EnsureText(bankLoanRoot, "BankLoan_RequestedAmountText", "Requested amount: unavailable", 14f, FontStyles.Bold);
                TMP_Text bankLoanEstimatedPayment = EnsureText(bankLoanRoot, "BankLoan_EstimatedPaymentText", "Estimated payment: unavailable", 13f, FontStyles.Normal);
                TMP_Text bankLoanPaymentSchedule = EnsureText(bankLoanRoot, "BankLoan_PaymentScheduleText", "Payment schedule: unavailable", 13f, FontStyles.Normal);
                TMP_Text bankLoanEstimatedTotal = EnsureText(bankLoanRoot, "BankLoan_EstimatedTotalText", "Estimated total owed: unavailable", 13f, FontStyles.Normal);
                TMP_Text bankLoanLenderStanding = EnsureText(bankLoanRoot, "BankLoan_LenderStandingText", "Lender standing: unavailable", 13f, FontStyles.Normal);
                RectTransform bankLoanAmountRow = EnsureRect(bankLoanRoot, "BankLoan_AmountButtonRow");
                ConfigureHorizontalLayout(bankLoanAmountRow, 6f, new RectOffset(0, 0, 0, 0));
                ConfigureLayoutElement(bankLoanAmountRow, -1f, 38f, 0f, 0f);
                Button loanIncreaseOne = EnsureButton(bankLoanAmountRow, "LoanAmount_Plus1Button", "+1", 58f, 34f, 13f);
                Button loanIncreaseTen = EnsureButton(bankLoanAmountRow, "LoanAmount_Plus10Button", "+10", 62f, 34f, 13f);
                Button loanIncreaseHundred = EnsureButton(bankLoanAmountRow, "LoanAmount_Plus100Button", "+100", 70f, 34f, 13f);
                Button loanDecreaseOne = EnsureButton(bankLoanAmountRow, "LoanAmount_Minus1Button", "-1", 58f, 34f, 13f);
                Button loanDecreaseTen = EnsureButton(bankLoanAmountRow, "LoanAmount_Minus10Button", "-10", 62f, 34f, 13f);
                Button loanDecreaseHundred = EnsureButton(bankLoanAmountRow, "LoanAmount_Minus100Button", "-100", 70f, 34f, 13f);
                Button loanWorkflow = EnsureButton(bankLoanRoot, "LoanWorkflowButton", "Open Loan Application", 190f, 34f, 13f);
                Button loanSubmit = EnsureButton(bankLoanRoot, "BankLoan_SubmitButton", "Submit Application", 170f, 34f, 13f);
                TMP_Text bankLoanStatus = EnsureText(bankLoanRoot, "BankLoan_StatusText", "Bank loan records unavailable.", 13f, FontStyles.Normal);
                TMP_Text bankLoanActiveLoan = EnsureText(bankLoanRoot, "BankLoan_ActiveLoanText", "Active loan: none.", 12f, FontStyles.Normal);
                ConfigureScrollableText(bankLoanTitle);
                ConfigureScrollableText(bankLoanHelp);
                ConfigureScrollableText(bankLoanRequestedAmount);
                ConfigureScrollableText(bankLoanEstimatedPayment);
                ConfigureScrollableText(bankLoanPaymentSchedule);
                ConfigureScrollableText(bankLoanEstimatedTotal);
                ConfigureScrollableText(bankLoanLenderStanding);
                ConfigureScrollableText(bankLoanStatus);
                ConfigureScrollableText(bankLoanActiveLoan);
                ConfigureTextLayout(bankLoanTitle, -1f, 28f, 1f, 0f);
                ConfigureTextLayout(bankLoanHelp, -1f, 34f, 1f, 0f);
                ConfigureTextLayout(bankLoanRequestedAmount, -1f, 24f, 1f, 0f);
                ConfigureTextLayout(bankLoanEstimatedPayment, -1f, 22f, 1f, 0f);
                ConfigureTextLayout(bankLoanPaymentSchedule, -1f, 22f, 1f, 0f);
                ConfigureTextLayout(bankLoanEstimatedTotal, -1f, 22f, 1f, 0f);
                ConfigureTextLayout(bankLoanLenderStanding, -1f, 24f, 1f, 0f);
                ConfigureTextLayout(bankLoanStatus, -1f, 42f, 1f, 0f);
                ConfigureTextLayout(bankLoanActiveLoan, -1f, 36f, 1f, 0f);

                RectTransform acquisitionsContent = EnsureRect(contentRoot, "Acquisitions Content");
                ConfigureVerticalLayout(acquisitionsContent, 8f, new RectOffset(12, 12, 10, 12));
                ConfigurePanelImage(acquisitionsContent, new Color(0.075f, 0.085f, 0.075f, 0.82f));
                ConfigureLayoutElement(acquisitionsContent, -1f, -1f, 1f, 1f);
                RectTransform acquisitionBar = EnsureRect(acquisitionsContent, "Acquisition Section Bar");
                ConfigureHorizontalLayout(acquisitionBar, 8f, new RectOffset(0, 0, 0, 0));
                ConfigureLayoutElement(acquisitionBar, -1f, 44f, 0f, 0f);
                Button landButton = EnsureButton(acquisitionBar, "Acquisition_LandButton", "Land", 118f, 38f);
                Button businessesButton = EnsureButton(acquisitionBar, "Acquisition_BusinessesButton", "Businesses", 146f, 38f);

                RectTransform acquisitionBody = EnsureRect(acquisitionsContent, "AcquisitionBody");
                ConfigureVerticalLayout(acquisitionBody, 8f, new RectOffset(0, 0, 0, 0));
                ConfigureLayoutElement(acquisitionBody, -1f, -1f, 1f, 1f);
                RectTransform acquisitionTopRow = EnsureRect(acquisitionBody, "Acquisition_TopRow");
                ConfigureHorizontalLayout(acquisitionTopRow, 12f, new RectOffset(0, 0, 0, 0));
                ConfigureLayoutElement(acquisitionTopRow, -1f, -1f, 1f, 0f);
                TMP_Text forSale = EnsureSectionPanel(acquisitionTopRow, "AcquisitionForSale_Section", "Active Leads", "AcquisitionForSale_BodyText", "Active leads load at runtime.", 132f);
                TMP_Text offMarket = EnsureSectionPanel(acquisitionTopRow, "AcquisitionOffMarket_Section", "Quiet / Off-Market Leads", "AcquisitionOffMarket_BodyText", "Quiet leads load at runtime.", 132f);
                RectTransform acquisitionBottomRow = EnsureRect(acquisitionBody, "Acquisition_BottomRow");
                ConfigureHorizontalLayout(acquisitionBottomRow, 12f, new RectOffset(0, 0, 0, 0));
                ConfigureLayoutElement(acquisitionBottomRow, -1f, -1f, 1f, 0f);
                TMP_Text selectedLead = EnsureSectionPanel(acquisitionBottomRow, "AcquisitionSelectedLead_Section", "Selected Lead / Readiness", "AcquisitionSelectedLead_BodyText", "Selected lead body loads at runtime.", 150f);
                TMP_Text history = EnsureSectionPanel(acquisitionBottomRow, "AcquisitionHistory_Section", "Process Ledger / Next Action", "AcquisitionHistory_BodyText", "Process ledger loads at runtime.", 150f);

                RectTransform acquisitionActions = EnsureRect(acquisitionsContent, "AcquisitionActions");
                ConfigureHorizontalLayout(acquisitionActions, 8f, new RectOffset(0, 0, 0, 0));
                ConfigureLayoutElement(acquisitionActions, -1f, 44f, 0f, 0f);
                if (acquisitionActions.GetSiblingIndex() != acquisitionsContent.childCount - 1)
                {
                    acquisitionActions.SetAsLastSibling();
                }

                SetSiblingIndexIfNeeded(acquisitionBody, Mathf.Max(0, acquisitionActions.GetSiblingIndex() - 1));
                Button acquisitionWorkflow = EnsureButton(acquisitionActions, "AcquisitionWorkflowButton", "Open Formal Acquisition", 214f, 40f, 14f);
                Button buyListing = EnsureButton(acquisitionActions, "BuyListingButton", "Advance Process", 166f, 40f, 14f);
                Button acquisitionFocus = EnsureButton(acquisitionActions, "AcquisitionFocusButton", "Inspect Site", 126f, 38f);
                Button previousListing = EnsureButton(acquisitionActions, "PreviousListingButton", "Previous Lead", 132f, 38f);
                Button nextListing = EnsureButton(acquisitionActions, "NextListingButton", "Next Lead", 116f, 38f);
                SetSiblingIndexIfNeeded(acquisitionWorkflow.transform, 0);
                SetSiblingIndexIfNeeded(buyListing.transform, 1);
                SetSiblingIndexIfNeeded(acquisitionFocus.transform, 2);
                SetSiblingIndexIfNeeded(previousListing.transform, 3);
                SetSiblingIndexIfNeeded(nextListing.transform, 4);

                RectTransform governmentContent = EnsureRect(contentRoot, "Government Content");
                DestroyDirectChildrenNamed(governmentContent, "Acquisition Section Bar");
                DestroyDirectChildrenNamed(governmentContent, "AcquisitionBody");
                DestroyDirectChildrenNamed(governmentContent, "AcquisitionActions");
                DestroyDirectChildrenNamed(governmentContent, "Government Hidden Section Bar");
                DestroyDirectChildrenNamed(governmentContent, "Government_TitleText");
                DestroyDirectChildrenNamed(governmentContent, "Government_SummaryText");
                DestroyDirectChildrenNamed(governmentContent, "Government_EffectsText");
                ConfigureVerticalLayout(governmentContent, 8f, new RectOffset(12, 12, 10, 12));
                ConfigurePanelImage(governmentContent, new Color(0.075f, 0.085f, 0.075f, 0.82f));
                ConfigureLayoutElement(governmentContent, -1f, -1f, 1f, 1f);

                RectTransform governmentBody = EnsureRect(governmentContent, "GovernmentBody");
                ConfigureVerticalLayout(governmentBody, 8f, new RectOffset(0, 0, 0, 0));
                ConfigureLayoutElement(governmentBody, -1f, -1f, 1f, 1f);
                TMP_Text governmentTitle = EnsureText(governmentBody, "Government_TitleText", "Civic", 20f, FontStyles.Bold);
                ConfigureTextLayout(governmentTitle, -1f, 32f, 1f, 0f);
                TMP_Text governmentSummary = EnsureSectionPanel(governmentBody, "GovernmentSummary_Section", "Town Status", "GovernmentSummary_BodyText", "Town status loads at runtime.", 104f);
                TMP_Text governmentEffects = EnsureSectionPanel(governmentBody, "GovernmentEffects_Section", "Civic Effects / Pressure", "GovernmentEffects_BodyText", "Civic effects load at runtime.", 104f);
                TMP_Text governmentOwnerImplications = EnsureSectionPanel(governmentBody, "GovernmentOwnerImplications_Section", "Owner Implications", "GovernmentOwnerImplications_BodyText", "Owner implications load at runtime.", 118f);
                RectTransform governmentActions = EnsureRect(governmentContent, "GovernmentActions");
                ConfigureHorizontalLayout(governmentActions, 8f, new RectOffset(0, 0, 0, 0));
                ConfigureLayoutElement(governmentActions, -1f, 44f, 0f, 0f);
                Button governmentFocus = EnsureButton(governmentActions, "GovernmentFocusButton", "View Civic Site", 136f, 38f);

                RectTransform resourcesContent = EnsureRect(contentRoot, "Resources Content");
                ConfigureVerticalLayout(resourcesContent, 8f, new RectOffset(12, 12, 10, 12));
                ConfigurePanelImage(resourcesContent, new Color(0.075f, 0.085f, 0.075f, 0.82f));
                ConfigureLayoutElement(resourcesContent, -1f, -1f, 1f, 1f);
                RectTransform resourcesBody = EnsureRect(resourcesContent, "ResourcesBody");
                ConfigureVerticalLayout(resourcesBody, 8f, new RectOffset(0, 0, 0, 0));
                ConfigureLayoutElement(resourcesBody, -1f, -1f, 1f, 1f);
                TMP_Text resourcesSummary = EnsureSectionPanel(resourcesBody, "ResourcesSummary_Section", "Regional Opportunity", "ResourcesSummaryText", "Resource summary loads at runtime.", 118f);
                TMP_Text resourcesDistricts = EnsureSectionPanel(resourcesBody, "ResourcesDistricts_Section", "District Ledger", "ResourcesDistrictListText", "District list loads at runtime.", 176f);
                TMP_Text resourcesDetail = EnsureSectionPanel(resourcesBody, "ResourcesDetail_Section", "Proto-Site Detail", "ResourcesDetailText", "Proto-site detail loads at runtime.", 156f);
                ConfigureDashboardCard(resourcesSummary, 118f);
                ConfigureDashboardCard(resourcesDistricts, 176f);
                ConfigureDashboardCard(resourcesDetail, 156f);

                RectTransform templates = EnsureRect(mainPanel, "Templates");
                templates.gameObject.SetActive(false);
                Button buttonRow = EnsureButton(templates, "ButtonRow_Template", "Button Row", 180f, 38f);
                TMP_Text textRow = EnsureText(templates, "TextRow_Template", "Text Row", 15f, FontStyles.Normal);
                buttonRow.gameObject.SetActive(false);
                textRow.gameObject.SetActive(false);

                TMP_Text fallback = EnsureText(mainPanel, "RuntimePanelText", string.Empty, 15f, FontStyles.Normal);
                fallback.gameObject.SetActive(false);

                ManagementPanelView view = mainPanel.GetComponent<ManagementPanelView>() ?? mainPanel.gameObject.AddComponent<ManagementPanelView>();
                SerializedObject serializedView = new(view);
                SetObject(serializedView, "panelRoot", mainPanel);
                SetObject(serializedView, "titleText", title);
                SetObject(serializedView, "hintText", hint);
                SetObject(serializedView, "propertiesTabButton", propertiesTab);
                SetObject(serializedView, "financesTabButton", financesTab);
                SetObject(serializedView, "acquisitionsTabButton", acquisitionsTab);
                SetObject(serializedView, "governmentTabButton", governmentTab);
                SetObject(serializedView, "resourcesTabButton", resourcesTab);
                SetObject(serializedView, "propertiesContentRoot", propertiesContent);
                SetObject(serializedView, "financesContentRoot", financesContent);
                SetObject(serializedView, "acquisitionsContentRoot", acquisitionsContent);
                SetObject(serializedView, "governmentContentRoot", governmentContent);
                SetObject(serializedView, "resourcesContentRoot", resourcesContent);
                SetObject(serializedView, "propertyListTitleText", propertyListTitle);
                SetObject(serializedView, "propertyListContent", propertyListContent);
                SetObject(serializedView, "propertyRowTemplate", propertyRowTemplate);
                SetObject(serializedView, "propertyDetailTitleText", detailTitle);
                SetObject(serializedView, "propertyDetailSummaryText", detailSummary);
                SetObject(serializedView, "managementFont", managementFont);
                SetObject(serializedView, "propertyDetailBodyScrollRoot", propertyBodyScroll);
                SetObject(serializedView, "propertyDetailBodyContent", propertyBodyContent);
                SetObject(serializedView, "rentingOverviewText", rentingOverview);
                SetObject(serializedView, "storeOverviewText", overview);
                SetObject(serializedView, "storeFinanceText", finance);
                SetObject(serializedView, "storeStaffingText", staffing);
                SetObject(serializedView, "storeStockText", stock);
                SetObject(serializedView, "cashTransferRoot", cashTransfer.Root);
                SetObject(serializedView, "cashTransferTitleText", cashTransfer.Title);
                SetObject(serializedView, "cashTransferHelpText", cashTransfer.Help);
                SetObject(serializedView, "cashTransferThresholdText", cashTransfer.Threshold);
                SetObject(serializedView, "cashTransferStatusText", cashTransfer.Status);
                SetObject(serializedView, "cashTransferDepositOneButton", cashTransfer.DepositOne);
                SetObject(serializedView, "cashTransferDepositTenButton", cashTransfer.DepositTen);
                SetObject(serializedView, "cashTransferDepositHundredButton", cashTransfer.DepositHundred);
                SetObject(serializedView, "cashTransferDepositInput", cashTransfer.DepositInput);
                SetObject(serializedView, "cashTransferDepositExactButton", cashTransfer.DepositExact);
                SetObject(serializedView, "cashTransferWithdrawOneButton", cashTransfer.WithdrawOne);
                SetObject(serializedView, "cashTransferWithdrawTenButton", cashTransfer.WithdrawTen);
                SetObject(serializedView, "cashTransferWithdrawHundredButton", cashTransfer.WithdrawHundred);
                SetObject(serializedView, "cashTransferWithdrawInput", cashTransfer.WithdrawInput);
                SetObject(serializedView, "cashTransferWithdrawExactButton", cashTransfer.WithdrawExact);
                SetObject(serializedView, "cashTransferAutoToggleButton", cashTransfer.AutoToggle);
                SetObject(serializedView, "cashTransferLowerThresholdInput", cashTransfer.LowerThresholdInput);
                SetObject(serializedView, "cashTransferUpperThresholdInput", cashTransfer.UpperThresholdInput);
                SetObject(serializedView, "cashTransferApplyReserveButton", cashTransfer.ApplyReserve);
                SetObject(serializedView, "cashTransferLowerMinusTenButton", cashTransfer.LowerMinusTen);
                SetObject(serializedView, "cashTransferLowerPlusTenButton", cashTransfer.LowerPlusTen);
                SetObject(serializedView, "cashTransferUpperMinusTenButton", cashTransfer.UpperMinusTen);
                SetObject(serializedView, "cashTransferUpperPlusTenButton", cashTransfer.UpperPlusTen);
                SetObject(serializedView, "focusPropertyButton", focusButton);
                SetObject(serializedView, "decreaseMarginButton", decreaseMarginButton);
                SetObject(serializedView, "increaseMarginButton", increaseMarginButton);
                SetObject(serializedView, "previousWorkerButton", prevWorkerButton);
                SetObject(serializedView, "nextWorkerButton", nextWorkerButton);
                SetObject(serializedView, "assignWorkerButton", assignWorkerButton);
                SetObject(serializedView, "buildProjectButton", buildProjectButton);
                SetObject(serializedView, "financesScrollRoot", financesScroll);
                SetObject(serializedView, "financesScrollContent", financesScrollContent);
                SetObject(serializedView, "financesSummaryText", financesSummary);
                SetObject(serializedView, "financesDebtPressureText", financesDebtPressure);
                SetObject(serializedView, "financesDistributionText", financesDistribution);
                SetObject(serializedView, "financesAffordabilityText", financesAffordability);
                SetObject(serializedView, "businessBreakdownContent", businessBreakdown);
                SetObject(serializedView, "financeBusinessRowTemplate", financeBusinessRowTemplate);
                SetObject(serializedView, "bankLoanRoot", bankLoanRoot);
                SetObject(serializedView, "bankLoanTitleText", bankLoanTitle);
                SetObject(serializedView, "bankLoanHelpText", bankLoanHelp);
                SetObject(serializedView, "bankLoanRequestedAmountText", bankLoanRequestedAmount);
                SetObject(serializedView, "bankLoanEstimatedPaymentText", bankLoanEstimatedPayment);
                SetObject(serializedView, "bankLoanPaymentScheduleText", bankLoanPaymentSchedule);
                SetObject(serializedView, "bankLoanEstimatedTotalText", bankLoanEstimatedTotal);
                SetObject(serializedView, "bankLoanLenderStandingText", bankLoanLenderStanding);
                SetObject(serializedView, "bankLoanStatusText", bankLoanStatus);
                SetObject(serializedView, "bankLoanActiveLoanText", bankLoanActiveLoan);
                SetObject(serializedView, "loanWorkflowButton", loanWorkflow);
                SetObject(serializedView, "loanIncreaseOneButton", loanIncreaseOne);
                SetObject(serializedView, "loanIncreaseTenButton", loanIncreaseTen);
                SetObject(serializedView, "loanIncreaseHundredButton", loanIncreaseHundred);
                SetObject(serializedView, "loanDecreaseOneButton", loanDecreaseOne);
                SetObject(serializedView, "loanDecreaseTenButton", loanDecreaseTen);
                SetObject(serializedView, "loanDecreaseHundredButton", loanDecreaseHundred);
                SetObject(serializedView, "loanSubmitButton", loanSubmit);
                SetObject(serializedView, "acquisitionForSaleSectionRoot", forSale.transform.parent);
                SetObject(serializedView, "acquisitionOffMarketSectionRoot", offMarket.transform.parent);
                SetObject(serializedView, "acquisitionSelectedLeadSectionRoot", selectedLead.transform.parent);
                SetObject(serializedView, "acquisitionHistorySectionRoot", history.transform.parent);
                SetObject(serializedView, "acquisitionLandButton", landButton);
                SetObject(serializedView, "acquisitionBusinessesButton", businessesButton);
                SetObject(serializedView, "acquisitionForSaleText", forSale);
                SetObject(serializedView, "acquisitionOffMarketText", offMarket);
                SetObject(serializedView, "acquisitionSelectedLeadText", selectedLead);
                SetObject(serializedView, "acquisitionHistoryText", history);
                SetObject(serializedView, "acquisitionFocusButton", acquisitionFocus);
                SetObject(serializedView, "acquisitionWorkflowButton", acquisitionWorkflow);
                SetObject(serializedView, "previousListingButton", previousListing);
                SetObject(serializedView, "nextListingButton", nextListing);
                SetObject(serializedView, "buyListingButton", buyListing);
                SetObject(serializedView, "governmentSummarySectionRoot", governmentSummary.transform.parent);
                SetObject(serializedView, "governmentEffectsSectionRoot", governmentEffects.transform.parent);
                SetObject(serializedView, "governmentOwnerImplicationsSectionRoot", governmentOwnerImplications.transform.parent);
                SetObject(serializedView, "governmentTitleText", governmentTitle);
                SetObject(serializedView, "governmentSummaryText", governmentSummary);
                SetObject(serializedView, "governmentEffectsText", governmentEffects);
                SetObject(serializedView, "governmentOwnerImplicationsText", governmentOwnerImplications);
                SetObject(serializedView, "governmentFocusButton", governmentFocus);
                SetObject(serializedView, "resourcesSummaryText", resourcesSummary);
                SetObject(serializedView, "resourcesDistrictListText", resourcesDistricts);
                SetObject(serializedView, "resourcesDetailText", resourcesDetail);
                SetObject(serializedView, "runtimeFallbackText", fallback);
                serializedView.ApplyModifiedPropertiesWithoutUndo();

                financesContent.gameObject.SetActive(false);
                acquisitionsContent.gameObject.SetActive(false);
                governmentContent.gameObject.SetActive(false);
                resourcesContent.gameObject.SetActive(false);
                propertiesContent.gameObject.SetActive(true);
                mainPanel.gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            AssetDatabase.SaveAssets();
        }

        public static void RepairMainSceneInstance()
        {
            Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"UI Canvas prefab not found at {PrefabPath}.");
                return;
            }

            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);
            GameObject authoritativeRoot = ChooseAuthoritativeCanvasRoot(canvases, prefab);
            if (authoritativeRoot == null)
            {
                authoritativeRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            }

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas candidateCanvas = canvases[i];
                if (candidateCanvas == null)
                {
                    continue;
                }

                GameObject candidateRoot = candidateCanvas.gameObject;
                if (candidateRoot != authoritativeRoot && IsUiCanvasCandidate(candidateRoot, prefab))
                {
                    Object.DestroyImmediate(candidateRoot);
                }
            }

            if (PrefabUtility.GetCorrespondingObjectFromOriginalSource(authoritativeRoot) != null)
            {
                PrefabUtility.RevertPrefabInstance(authoritativeRoot, InteractionMode.AutomatedAction);
            }

            authoritativeRoot.name = "UI Canvas";
            SetTagIfAvailable(authoritativeRoot, UiCanvasTag);
            Canvas canvas = authoritativeRoot.GetComponent<Canvas>();
            Transform hudRoot = authoritativeRoot.transform.Find("HUD_Root");
            if (hudRoot != null)
            {
                hudRoot.gameObject.SetActive(true);
            }

            Transform mainPanel = authoritativeRoot.transform.Find("Main Panel");
            if (mainPanel != null)
            {
                mainPanel.gameObject.SetActive(false);
            }

            ManagementPanelView view = Object.FindAnyObjectByType<ManagementPanelView>(FindObjectsInactive.Include);
            GeneralStorePanelController controller = Object.FindAnyObjectByType<GeneralStorePanelController>(FindObjectsInactive.Include);
            if (controller != null && view != null)
            {
                SerializedObject serializedController = new(controller);
                SetObject(serializedController, "canvas", canvas != null ? canvas : view.GetComponentInParent<Canvas>());
                SetObject(serializedController, "view", view);
                serializedController.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(controller);
            }

            if (view != null)
            {
                EditorUtility.SetDirty(view);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static GameObject ChooseAuthoritativeCanvasRoot(Canvas[] canvases, GameObject prefab)
        {
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas != null && IsUiCanvasCandidate(canvas.gameObject, prefab))
                {
                    return canvas.gameObject;
                }
            }

            return null;
        }

        private static bool IsUiCanvasCandidate(GameObject root, GameObject prefab)
        {
            if (root == null)
            {
                return false;
            }

            if (root.name == "UI Canvas" || root.name == "LandLedgers_HUDCanvas" || root.name == "LandLedgers_HUD Canvas")
            {
                return true;
            }

            if (root.CompareTag(UiCanvasTag))
            {
                return true;
            }

            return PrefabUtility.GetCorrespondingObjectFromOriginalSource(root) == prefab;
        }

        private static void SetTagIfAvailable(GameObject target, string tag)
        {
            if (target == null)
            {
                return;
            }

            try
            {
                target.tag = tag;
            }
            catch (UnityException)
            {
                Debug.LogWarning($"Tag '{tag}' is not defined. Add it in Project Settings > Tags before relying on tag fallback.", target);
            }
        }

        private static RectTransform EnsureRect(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null && existing is RectTransform rectTransform)
            {
                return rectTransform;
            }

            GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform));
            if (existing == null)
            {
                go.transform.SetParent(parent, false);
            }

            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect == null)
            {
                Debug.LogError($"{go.name} is not a UI object. Delete it and rerun the repair utility.");
            }

            return rect;
        }

        private static RectTransform FindRect(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return parent.Find(name) as RectTransform;
        }


        private static TMP_Text EnsureText(Transform parent, string name, string value, float fontSize, FontStyles style)
        {
            RectTransform rect = EnsureRect(parent, name);
            TMP_Text text = rect.GetComponent<TMP_Text>() ?? rect.gameObject.AddComponent<TextMeshProUGUI>();
            ConfigureText(text, value, fontSize, style);
            return text;
        }

        private static TMP_Text EnsureSectionText(Transform parent, string name, string fallback)
        {
            TMP_Text text = EnsureText(parent, name, fallback, 15f, FontStyles.Normal);
            ConfigureTextLayout(text, 220f, -1f, 1f, 1f);
            return text;
        }

        private static TMP_Text EnsureSectionPanel(Transform parent, string panelName, string title, string bodyName, string fallback, float minHeight)
        {
            RectTransform root = EnsureRect(parent, panelName);
            ConfigureVerticalLayout(root, 6f, new RectOffset(10, 10, 8, 10));
            ConfigurePanelImage(root, new Color(0.06f, 0.07f, 0.06f, 0.82f));
            ConfigureLayoutElement(root, -1f, minHeight, 1f, 0f);

            TMP_Text header = EnsureText(root, $"{panelName}_HeaderText", title, 16f, FontStyles.Bold);
            ConfigureTextLayout(header, -1f, 24f, 1f, 0f);

            TMP_Text body = EnsureText(root, bodyName, fallback, 14f, FontStyles.Normal);
            ConfigureScrollableText(body);
            ConfigureTextLayout(body, -1f, -1f, 1f, 1f);
            return body;
        }

        private static RectTransform ConfigureScrollArea(RectTransform scrollRoot, string viewportName, string contentName)
        {
            Image scrollImage = scrollRoot.GetComponent<Image>() ?? scrollRoot.gameObject.AddComponent<Image>();
            scrollImage.color = new Color(0.02f, 0.025f, 0.02f, 0.18f);
            scrollImage.raycastTarget = true;

            ScrollRect scrollRect = scrollRoot.GetComponent<ScrollRect>() ?? scrollRoot.gameObject.AddComponent<ScrollRect>();
            RectTransform verticalScrollbar = EnsureVerticalScrollbar(scrollRoot);
            RectTransform viewport = EnsureRect(scrollRoot, viewportName);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = verticalScrollbar != null ? new Vector2(-15f, 0f) : Vector2.zero;
            Image viewportImage = viewport.GetComponent<Image>() ?? viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
            viewportImage.raycastTarget = true;
            _ = viewport.GetComponent<RectMask2D>() ?? viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = EnsureRect(viewport, contentName);
            bool contentAlreadyExisted = content.childCount > 0 || content.GetComponent<VerticalLayoutGroup>() != null || content.GetComponent<ContentSizeFitter>() != null;
            Vector2 preservedContentPosition = content.anchoredPosition;
            Vector2 preservedContentSize = content.sizeDelta;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            if (!contentAlreadyExisted)
            {
                content.anchoredPosition = Vector2.zero;
            }

            content.sizeDelta = contentAlreadyExisted
                ? new Vector2(0f, preservedContentSize.y)
                : Vector2.zero;
            Image contentImage = content.GetComponent<Image>() ?? content.gameObject.AddComponent<Image>();
            contentImage.color = new Color(0f, 0f, 0f, 0.01f);
            contentImage.raycastTarget = true;
            ConfigureVerticalLayout(content, 6f, new RectOffset(6, 6, 4, 12));
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
            if (contentAlreadyExisted)
            {
                content.anchoredPosition = preservedContentPosition;
            }

            return content;
        }

        private static RectTransform EnsureVerticalScrollbar(RectTransform scrollRoot)
        {
            if (scrollRoot == null)
            {
                return null;
            }

            RectTransform scrollbarRect = FindRect(scrollRoot, "VerticalScrollbar") ?? FindRect(scrollRoot, "Scrollbar Vertical");
            if (scrollbarRect == null)
            {
                GameObject scrollbarObject = new("VerticalScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
                scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
                scrollbarRect.SetParent(scrollRoot, false);

                RectTransform slidingArea = EnsureRect(scrollbarRect, "Sliding Area");
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

            if (scrollbar.handleRect == null)
            {
                RectTransform slidingArea = FindRect(scrollbarRect, "Sliding Area") ?? EnsureRect(scrollbarRect, "Sliding Area");
                RectTransform handle = FindRect(slidingArea, "Handle");
                if (handle == null)
                {
                    GameObject handleObject = new("Handle", typeof(RectTransform), typeof(Image));
                    handle = handleObject.GetComponent<RectTransform>();
                    handle.SetParent(slidingArea, false);
                    handle.anchorMin = Vector2.zero;
                    handle.anchorMax = Vector2.one;
                    handle.offsetMin = Vector2.zero;
                    handle.offsetMax = Vector2.zero;
                }

                scrollbar.handleRect = handle;
            }

            if (scrollbar.handleRect != null)
            {
                Image handleImage = scrollbar.handleRect.GetComponent<Image>() ?? scrollbar.handleRect.gameObject.AddComponent<Image>();
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

        private static void NormalizeScrollContentRaycasts(RectTransform content)
        {
            if (content == null)
            {
                return;
            }

            TMP_Text[] texts = content.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null)
                {
                    texts[i].raycastTarget = false;
                }
            }
        }

        private static RectTransform ResolveBusinessCashTransferParent(Transform root)
        {
            // Keep prefab-repair ownership aligned with the runtime view: when the
            // property-detail store stack exists, the cash-transfer block belongs in that
            // scrollable lane rather than falling back to the broader body content.
            if (FindDeepChild(root, "PropertyDetail_BodyContent") is RectTransform propertyBodyRect)
            {
                return propertyBodyRect;
            }

            RectTransform hudRoot = root.Find("HUD_Root") as RectTransform;
            if (hudRoot != null)
            {
                return hudRoot;
            }

            return root as RectTransform;
        }

        private static CashTransferRefs EnsureBusinessCashTransferBlock(RectTransform parent, Transform legacyParent)
        {
            Transform legacy = legacyParent != null ? legacyParent.Find("BusinessCashTransfer_Root") : null;
            if (legacy != null && legacy.parent != parent)
            {
                legacy.SetParent(parent, false);
            }

            RectTransform root = EnsureRect(parent, "BusinessCashTransfer_Root");
            ConfigureCashTransferOverlayRoot(root);

            CashTransferRefs refs = new()
            {
                Root = root,
                Title = EnsureText(root, "BusinessCashTransfer_TitleText", "Business Cash Transfers", 16f, FontStyles.Bold),
                Help = EnsureText(root, "BusinessCashTransfer_HelpText", "Store Cash Position", 12f, FontStyles.Normal),
                Threshold = EnsureText(root, "BusinessCashTransfer_ThresholdText", "Thresholds unavailable.", 12f, FontStyles.Normal)
            };
            ConfigureCashTransferText(refs.Title, 24f);
            ConfigureCashTransferText(refs.Help, 24f);
            ConfigureCashTransferText(refs.Threshold, 24f);

            TMP_Text transferLabel = EnsureText(root, "BusinessCashTransfer_TransferGroupLabel", "Manual Transfer", 12f, FontStyles.Bold);
            ConfigureCashTransferText(transferLabel, 20f);
            RectTransform exactRow = EnsureRect(root, "BusinessCashTransfer_ExactTransferRow");
            ConfigureHorizontalLayout(exactRow, 6f, new RectOffset(0, 0, 0, 0));
            ConfigureLayoutElement(exactRow, -1f, 38f, 1f, 0f);
            refs.DepositInput = EnsureCashTransferInput(exactRow, "BusinessCashTransfer_DepositInput", "Exact Deposit", 150f);
            refs.DepositExact = EnsureButton(exactRow, "BusinessCashTransfer_DepositExactButton", "Deposit Exact", 118f, 34f, 12f);
            refs.WithdrawInput = EnsureCashTransferInput(exactRow, "BusinessCashTransfer_WithdrawInput", "Exact Withdrawal", 160f);
            refs.WithdrawExact = EnsureButton(exactRow, "BusinessCashTransfer_WithdrawExactButton", "Withdraw Exact", 126f, 34f, 12f);

            RectTransform amountRow = EnsureRect(root, "BusinessCashTransfer_AmountButtonRow");
            ConfigureHorizontalLayout(amountRow, 6f, new RectOffset(0, 0, 0, 0));
            ConfigureLayoutElement(amountRow, -1f, 38f, 1f, 0f);
            refs.DepositOne = EnsureButton(amountRow, "BusinessCashTransfer_Deposit1Button", "Deposit $1", 94f, 34f, 12f);
            refs.DepositTen = EnsureButton(amountRow, "BusinessCashTransfer_Deposit10Button", "$10", 58f, 34f, 12f);
            refs.DepositHundred = EnsureButton(amountRow, "BusinessCashTransfer_Deposit100Button", "$100", 66f, 34f, 12f);
            refs.WithdrawOne = EnsureButton(amountRow, "BusinessCashTransfer_Withdraw1Button", "Withdraw $1", 102f, 34f, 12f);
            refs.WithdrawTen = EnsureButton(amountRow, "BusinessCashTransfer_Withdraw10Button", "$10", 58f, 34f, 12f);
            refs.WithdrawHundred = EnsureButton(amountRow, "BusinessCashTransfer_Withdraw100Button", "$100", 66f, 34f, 12f);

            TMP_Text autoLabel = EnsureText(root, "BusinessCashTransfer_AutoGroupLabel", "Reserve Automation", 12f, FontStyles.Bold);
            ConfigureCashTransferText(autoLabel, 20f);
            RectTransform thresholdRow = EnsureRect(root, "BusinessCashTransfer_ThresholdInputRow");
            ConfigureHorizontalLayout(thresholdRow, 6f, new RectOffset(0, 0, 0, 0));
            ConfigureLayoutElement(thresholdRow, -1f, 38f, 1f, 0f);
            refs.AutoToggle = EnsureButton(thresholdRow, "BusinessCashTransfer_AutoToggleButton", "Auto Reserve On", 134f, 34f, 12f);
            refs.LowerThresholdInput = EnsureCashTransferInput(thresholdRow, "BusinessCashTransfer_LowerThresholdInput", "Refill Below", 135f);
            refs.UpperThresholdInput = EnsureCashTransferInput(thresholdRow, "BusinessCashTransfer_UpperThresholdInput", "Sweep Above", 135f);
            refs.ApplyReserve = EnsureButton(thresholdRow, "BusinessCashTransfer_ApplyReserveButton", "Apply Reserve Rules", 150f, 34f, 12f);

            RectTransform autoRow = EnsureRect(root, "BusinessCashTransfer_AutoButtonRow");
            ConfigureHorizontalLayout(autoRow, 6f, new RectOffset(0, 0, 0, 0));
            ConfigureLayoutElement(autoRow, -1f, 38f, 1f, 0f);
            refs.LowerMinusTen = EnsureButton(autoRow, "BusinessCashTransfer_LowerMinus10Button", "Refill -$10", 96f, 34f, 12f);
            refs.LowerPlusTen = EnsureButton(autoRow, "BusinessCashTransfer_LowerPlus10Button", "Refill +$10", 96f, 34f, 12f);
            refs.UpperMinusTen = EnsureButton(autoRow, "BusinessCashTransfer_UpperMinus10Button", "Sweep -$10", 96f, 34f, 12f);
            refs.UpperPlusTen = EnsureButton(autoRow, "BusinessCashTransfer_UpperPlus10Button", "Sweep +$10", 96f, 34f, 12f);

            refs.Status = EnsureText(root, "BusinessCashTransfer_StatusText", "No cash transfers yet.", 12f, FontStyles.Normal);
            ConfigureCashTransferText(refs.Status, 28f);
            return refs;
        }

        private static void ConfigureCashTransferOverlayRoot(RectTransform root)
        {
            bool insidePropertyScroll = root.parent != null
                && (root.parent.name == "PropertyDetail_BodyContent" || root.parent.name == "PropertyDetail_StoreSections");
            if (insidePropertyScroll)
            {
                root.anchorMin = new Vector2(0f, 1f);
                root.anchorMax = new Vector2(1f, 1f);
                root.pivot = new Vector2(0.5f, 1f);
                root.anchoredPosition = Vector2.zero;
                root.sizeDelta = Vector2.zero;
                root.SetAsLastSibling();

                ConfigurePanelImage(root, new Color(0f, 0f, 0f, 0.01f));
                ConfigureVerticalLayout(root, 6f, new RectOffset(0, 0, 8, 14));
                LayoutElement layout = root.GetComponent<LayoutElement>() ?? root.gameObject.AddComponent<LayoutElement>();
                layout.ignoreLayout = false;
                layout.minWidth = -1f;
                layout.preferredWidth = -1f;
                layout.minHeight = 344f;
                layout.preferredHeight = 364f;
                layout.flexibleWidth = 1f;
                layout.flexibleHeight = 0f;
                return;
            }

            root.anchorMin = new Vector2(0f, 0f);
            root.anchorMax = new Vector2(0f, 0f);
            root.pivot = new Vector2(0f, 0f);
            root.anchoredPosition = CashTransferOverlayOffset;
            root.sizeDelta = CashTransferOverlaySize;
            root.SetAsLastSibling();

            ConfigurePanelImage(root, new Color(0.07f, 0.08f, 0.07f, 0.94f));
            ConfigureVerticalLayout(root, 6f, new RectOffset(10, 10, 8, 10));
            LayoutElement overlayLayout = root.GetComponent<LayoutElement>() ?? root.gameObject.AddComponent<LayoutElement>();
            overlayLayout.ignoreLayout = true;
            overlayLayout.minWidth = CashTransferOverlaySize.x;
            overlayLayout.minHeight = CashTransferOverlaySize.y;
            overlayLayout.preferredWidth = CashTransferOverlaySize.x;
            overlayLayout.preferredHeight = CashTransferOverlaySize.y;
            overlayLayout.flexibleWidth = 0f;
            overlayLayout.flexibleHeight = 0f;
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

        private static void ConfigureCashTransferText(TMP_Text text, float minHeight)
        {
            ConfigureScrollableText(text);
            LayoutElement layout = text.GetComponent<LayoutElement>() ?? text.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = minHeight;
            layout.preferredHeight = minHeight;
            layout.flexibleHeight = 0f;
        }

        private static TMP_InputField EnsureCashTransferInput(Transform parent, string name, string placeholder, float preferredWidth)
        {
            RectTransform rect = EnsureRect(parent, name);
            Image image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.09f, 0.085f, 0.07f, 0.96f);
            image.raycastTarget = true;

            TMP_InputField input = rect.GetComponent<TMP_InputField>() ?? rect.gameObject.AddComponent<TMP_InputField>();
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.DecimalNumber;
            input.characterValidation = TMP_InputField.CharacterValidation.Decimal;
            input.keyboardType = TouchScreenKeyboardType.DecimalPad;
            input.characterLimit = 16;
            input.targetGraphic = image;

            TMP_Text placeholderText = EnsureInputText(rect, "Placeholder", placeholder, new Color(0.62f, 0.58f, 0.49f, 1f));
            TMP_Text valueText = EnsureInputText(rect, "Text", string.Empty, new Color(0.94f, 0.91f, 0.82f, 1f));
            LandLedgersTypography.ApplyRole(placeholderText, LandLedgersTypography.TextRole.SectionHeader);
            LandLedgersTypography.ApplyRole(valueText, LandLedgersTypography.TextRole.SectionHeader);
            input.placeholder = placeholderText;
            input.textComponent = valueText;
            ConfigureLayoutElement(rect, preferredWidth, 34f, 0f, 0f);
            return input;
        }

        private static TMP_Text EnsureInputText(Transform parent, string name, string value, Color color)
        {
            TMP_Text text = EnsureText(parent, name, value, 12f, FontStyles.Normal);
            text.color = color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            RectTransform rect = text.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(10f, 4f);
            rect.offsetMax = new Vector2(-10f, -4f);
            return text;
        }

        private static Button EnsureButton(Transform parent, string name, string label, float preferredWidth, float preferredHeight, float labelFontSize = 15f)
        {
            RectTransform rect = EnsureRect(parent, name);
            Image image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.18f, 0.18f, 0.14f, 0.96f);
            image.raycastTarget = true;
            Button button = rect.GetComponent<Button>() ?? rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ConfigureLayoutElement(rect, preferredWidth, preferredHeight, 0f, 0f);

            TMP_Text text = rect.GetComponentInChildren<TMP_Text>(true);
            if (text == null)
            {
                text = EnsureText(rect, "Label", label, labelFontSize, FontStyles.Bold);
            }

            ConfigureText(text, label, labelFontSize, FontStyles.Bold);
            LandLedgersTypography.ApplyRole(text, LandLedgersTypography.TextRole.ButtonLabel);
            text.alignment = TextAlignmentOptions.Center;
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(4f, 0f);
            textRect.offsetMax = new Vector2(-4f, 0f);
            return button;
        }

        private static Transform FindDeepChild(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }

                Transform nested = FindDeepChild(child, childName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static void AdoptLegacyTab(Transform tabBar, string legacyName, Transform target)
        {
            Transform legacy = tabBar.Find(legacyName);
            if (legacy == null || legacy == target)
            {
                return;
            }

            Object.DestroyImmediate(legacy.gameObject);
        }

        private static void DestroyDirectChildrenNamed(Transform parent, string childName)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName)
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        private static void MoveDirectChildIfNeeded(Transform oldParent, Transform newParent, string childName)
        {
            if (oldParent == null || newParent == null || string.IsNullOrWhiteSpace(childName))
            {
                return;
            }

            Transform child = oldParent.Find(childName);
            if (child != null && child.parent != newParent)
            {
                child.SetParent(newParent, false);
            }
        }

        private static void ConfigurePanelRect(RectTransform panel)
        {
            panel.anchorMin = new Vector2(1f, 1f);
            panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(1f, 1f);
            panel.anchoredPosition = new Vector2(-20f, -123f);
            panel.sizeDelta = new Vector2(1019.9216f, 937.61f);
        }

        private static void ConfigurePanelImage(Component component)
        {
            ConfigurePanelImage(component, new Color(0.07f, 0.08f, 0.07f, 0.92f));
        }

        private static void ConfigurePanelImage(Component component, Color color)
        {
            Image image = component.GetComponent<Image>() ?? component.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
        }

        private static void ConfigureText(TMP_Text text, string value, float fontSize, FontStyles style)
        {
            text.text = value;
            text.fontSize = fontSize;
            LandLedgersTypography.ApplyRole(text, ResolveRoleForText(text, style));
            text.color = new Color(0.94f, 0.91f, 0.82f, 1f);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
        }

        private static void ConfigureScrollableText(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            LayoutElement layout = text.GetComponent<LayoutElement>() ?? text.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = -1f;
            layout.flexibleWidth = 1f;
            layout.flexibleHeight = 0f;
        }

        private static void ConfigureDashboardCard(TMP_Text text, float minHeight)
        {
            if (text == null)
            {
                return;
            }

            LayoutElement layout = text.GetComponent<LayoutElement>() ?? text.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = minHeight;
            layout.preferredHeight = -1f;
            layout.flexibleWidth = 1f;
            layout.flexibleHeight = 0f;
        }

        private static void ConfigureTextLayout(TMP_Text text, float preferredWidth, float preferredHeight, float flexibleWidth, float flexibleHeight)
        {
            RectTransform rect = text.GetComponent<RectTransform>();
            ConfigureLayoutElement(rect, preferredWidth, preferredHeight, flexibleWidth, flexibleHeight);
        }

        private static void ConfigureHorizontalLayout(Component component, float spacing, RectOffset padding)
        {
            VerticalLayoutGroup oldVertical = component.GetComponent<VerticalLayoutGroup>();
            if (oldVertical != null)
            {
                Object.DestroyImmediate(oldVertical);
            }

            HorizontalLayoutGroup layout = component.GetComponent<HorizontalLayoutGroup>() ?? component.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
        }

        private static void ConfigureVerticalLayout(Component component, float spacing, RectOffset padding)
        {
            HorizontalLayoutGroup oldHorizontal = component.GetComponent<HorizontalLayoutGroup>();
            if (oldHorizontal != null)
            {
                Object.DestroyImmediate(oldHorizontal);
            }

            VerticalLayoutGroup layout = component.GetComponent<VerticalLayoutGroup>() ?? component.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        private static void ConfigureLayoutElement(Component component, float preferredWidth, float preferredHeight, float flexibleWidth, float flexibleHeight)
        {
            LayoutElement element = component.GetComponent<LayoutElement>() ?? component.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = preferredWidth;
            element.preferredHeight = preferredHeight;
            element.flexibleWidth = flexibleWidth;
            element.flexibleHeight = flexibleHeight;
        }

        private static void ApplyBusinessTypography(params TMP_Text[] texts)
        {
            if (texts == null)
            {
                return;
            }

            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null)
                {
                    LandLedgersTypography.ApplyRole(texts[i], ResolveRoleForText(texts[i], texts[i].fontStyle));
                }
            }
        }

        private static void ApplyBusinessTypographyToCashTransfer(CashTransferRefs refs)
        {
            if (refs.Root == null)
            {
                return;
            }

            TMP_Text[] texts = refs.Root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                LandLedgersTypography.ApplyRole(texts[i], LandLedgersTypography.TextRole.SectionHeader);
            }
        }

        private static LandLedgersTypography.TextRole ResolveRoleForText(TMP_Text text, FontStyles style)
        {
            if (text == null)
            {
                return LandLedgersTypography.TextRole.DenseBody;
            }

            string name = text.name;
            if (name.Contains("Title", System.StringComparison.OrdinalIgnoreCase))
            {
                return LandLedgersTypography.TextRole.PanelHeader;
            }

            if (name.Contains("Hint", System.StringComparison.OrdinalIgnoreCase)
                || name.Contains("Help", System.StringComparison.OrdinalIgnoreCase)
                || name.Contains("Summary", System.StringComparison.OrdinalIgnoreCase))
            {
                return LandLedgersTypography.TextRole.HelperText;
            }

            if (name.Contains("Finance", System.StringComparison.OrdinalIgnoreCase)
                || name.Contains("Ledger", System.StringComparison.OrdinalIgnoreCase)
                || name.Contains("Acquisition", System.StringComparison.OrdinalIgnoreCase)
                || name.Contains("Stock", System.StringComparison.OrdinalIgnoreCase)
                || name.Contains("Staffing", System.StringComparison.OrdinalIgnoreCase))
            {
                return LandLedgersTypography.TextRole.DenseBody;
            }

            return style == FontStyles.Bold ? LandLedgersTypography.TextRole.SectionHeader : LandLedgersTypography.TextRole.DenseBody;
        }

        private static void SetObject(SerializedObject serializedObject, string propertyName, Object value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private struct CashTransferRefs
        {
            public RectTransform Root;
            public TMP_Text Title;
            public TMP_Text Help;
            public TMP_Text Threshold;
            public TMP_Text Status;
            public Button DepositOne;
            public Button DepositTen;
            public Button DepositHundred;
            public TMP_InputField DepositInput;
            public Button DepositExact;
            public Button WithdrawOne;
            public Button WithdrawTen;
            public Button WithdrawHundred;
            public TMP_InputField WithdrawInput;
            public Button WithdrawExact;
            public Button AutoToggle;
            public TMP_InputField LowerThresholdInput;
            public TMP_InputField UpperThresholdInput;
            public Button ApplyReserve;
            public Button LowerMinusTen;
            public Button LowerPlusTen;
            public Button UpperMinusTen;
            public Button UpperPlusTen;
        }
    }
}
#endif
