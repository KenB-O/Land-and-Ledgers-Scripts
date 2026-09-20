using LandLedgers.Economy;
using LandLedgers.MVP;
using LandLedgers.UI;
using NUnit.Framework;
using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LandLedgers.EditorTests.UI
{
    public sealed class OwnershipWorkflowUiRepairTests
    {
        [Test]
        public void WorkflowOverlayKeepsProcessNotesScrollable()
        {
            GameObject canvasObject = new("Workflow UI Test Canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                OwnershipWorkflowView view = OwnershipWorkflowView.GetOrCreate(canvas);

                RectTransform notesPanel = FindRect(view.transform, "Workflow_NotesPanel");
                ScrollRect scroll = notesPanel.GetComponent<ScrollRect>();
                RectTransform content = FindRect(view.transform, "Workflow_NotesContent");

                Assert.NotNull(scroll);
                Assert.AreSame(content, scroll.content);
                Assert.IsFalse(scroll.horizontal);
                Assert.IsTrue(scroll.vertical);
                Assert.AreEqual(ScrollRect.MovementType.Clamped, scroll.movementType);
                Assert.GreaterOrEqual(scroll.scrollSensitivity, 24f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        [Test]
        public void WorkflowActionAreaKeepsFooterButtonsReachable()
        {
            GameObject canvasObject = new("Workflow Action Row Test Canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                OwnershipWorkflowView view = OwnershipWorkflowView.GetOrCreate(canvas);

                RectTransform actionRoot = FindRect(view.transform, "Workflow_Actions");
                VerticalLayoutGroup actionLayout = actionRoot.GetComponent<VerticalLayoutGroup>();
                LayoutElement actionElement = actionRoot.GetComponent<LayoutElement>();

                Assert.NotNull(actionLayout);
                Assert.GreaterOrEqual(actionRoot.childCount, 3);
                Assert.GreaterOrEqual(actionElement.preferredHeight, 138f);
                Assert.NotNull(view.PrimaryButton);
                Assert.NotNull(view.CloseButton);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        [Test]
        public void WorkflowTextDoesNotStealMouseWheelInput()
        {
            GameObject canvasObject = new("Workflow Raycast Test Canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                OwnershipWorkflowView view = OwnershipWorkflowView.GetOrCreate(canvas);
                TMP_Text[] textChildren = view.GetComponentsInChildren<TMP_Text>(true);

                Assert.IsNotEmpty(textChildren);
                for (int i = 0; i < textChildren.Length; i++)
                {
                    Assert.IsFalse(textChildren[i].raycastTarget, textChildren[i].name);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        [Test]
        public void ManagementAcquisitionsDashboardCreatesFormalFourSectionLayout()
        {
            GameObject viewObject = new("Management Acquisition Dashboard Test", typeof(RectTransform));
            try
            {
                ManagementPanelView view = viewObject.AddComponent<ManagementPanelView>();
                RectTransform acquisitionsRoot = CreateRect("Acquisitions Content", viewObject.transform);
                SetPrivateField(view, "acquisitionsContentRoot", acquisitionsRoot);

                RectTransform actions = CreateRect("AcquisitionActions", acquisitionsRoot);
                HorizontalLayoutGroup actionLayout = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
                actionLayout.childControlWidth = true;
                actionLayout.childControlHeight = true;
                Button buyButton = CreateButton("BuyListingButton", actions, "Advance Process");
                Button nextButton = CreateButton("NextListingButton", actions, "Next Lead");
                Button previousButton = CreateButton("PreviousListingButton", actions, "Previous Lead");
                SetPrivateField(view, "buyListingButton", buyButton);
                SetPrivateField(view, "nextListingButton", nextButton);
                SetPrivateField(view, "previousListingButton", previousButton);

                InvokePrivate(view, "EnsureAcquisitionDashboardControls", true);
                InvokePrivate(view, "EnsureAcquisitionActionButtons", true);

                RectTransform body = FindRect(view.transform, "AcquisitionBody");
                RectTransform topRow = FindRect(view.transform, "Acquisition_TopRow");
                RectTransform bottomRow = FindRect(view.transform, "Acquisition_BottomRow");
                TMP_Text activeHeader = FindText(view.transform, "AcquisitionForSale_Section_HeaderText");
                TMP_Text quietHeader = FindText(view.transform, "AcquisitionOffMarket_Section_HeaderText");
                TMP_Text selectedHeader = FindText(view.transform, "AcquisitionSelectedLead_Section_HeaderText");
                TMP_Text ledgerHeader = FindText(view.transform, "AcquisitionHistory_Section_HeaderText");

                Assert.NotNull(body);
                Assert.NotNull(topRow);
                Assert.NotNull(bottomRow);
                Assert.AreEqual("Active Leads", activeHeader.text);
                Assert.AreEqual("Quiet / Off-Market Leads", quietHeader.text);
                Assert.AreEqual("Selected Lead / Readiness", selectedHeader.text);
                Assert.AreEqual("Process Ledger / Next Action", ledgerHeader.text);
                Assert.AreSame(topRow, view.AcquisitionForSaleText.transform.parent.parent);
                Assert.AreSame(topRow, view.AcquisitionOffMarketText.transform.parent.parent);
                Assert.AreSame(bottomRow, view.AcquisitionSelectedLeadText.transform.parent.parent);
                Assert.AreSame(bottomRow, view.AcquisitionHistoryText.transform.parent.parent);
                Assert.Less(body.GetSiblingIndex(), actions.GetSiblingIndex());
                Assert.AreEqual(acquisitionsRoot.childCount - 1, actions.GetSiblingIndex());
                Assert.AreEqual("AcquisitionWorkflowButton", actions.GetChild(0).name);
                Assert.AreEqual("BuyListingButton", actions.GetChild(1).name);

                int bodyIndex = body.GetSiblingIndex();
                int actionsIndex = actions.GetSiblingIndex();
                int workflowIndex = actions.GetChild(0).GetSiblingIndex();
                int buyIndex = actions.GetChild(1).GetSiblingIndex();
                LayoutElement actionElement = actions.GetComponent<LayoutElement>();

                InvokePrivate(view, "EnsureAcquisitionDashboardControls", true);
                InvokePrivate(view, "EnsureAcquisitionActionButtons", true);

                Assert.AreEqual(bodyIndex, body.GetSiblingIndex());
                Assert.AreEqual(actionsIndex, actions.GetSiblingIndex());
                Assert.AreEqual(workflowIndex, actions.GetChild(0).GetSiblingIndex());
                Assert.AreEqual(buyIndex, actions.GetChild(1).GetSiblingIndex());
                Assert.AreEqual(44f, actionElement.preferredHeight);
                Assert.AreEqual(0f, actionElement.flexibleHeight);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void ManagementPropertyDetailCreatesUsableVerticalScrollbarAndWheelTargets()
        {
            GameObject viewObject = new("Management Property Scroll Test", typeof(RectTransform));
            try
            {
                ManagementPanelView view = viewObject.AddComponent<ManagementPanelView>();
                RectTransform detailPanel = CreateRect("PropertyDetail_Panel", viewObject.transform);
                TMP_Text title = CreateText("PropertyDetail_Title", detailPanel, "Player General Store");
                TMP_Text summary = CreateText("PropertyDetail_Summary", detailPanel, "General Store summary.");
                SetPrivateField(view, "propertyDetailTitleText", title);
                SetPrivateField(view, "propertyDetailSummaryText", summary);

                view.EnsurePropertyDetailScrollBody(true);

                RectTransform storeSections = FindRect(view.PropertyDetailBodyContent, "PropertyDetail_StoreSections");
                TMP_Text overview = CreateText("StoreOverviewText", storeSections, "Store status.");
                TMP_Text finance = FindText(storeSections, "StoreFinanceText");
                TMP_Text staffing = CreateText("StoreStaffingText", storeSections, "Staffing.");
                TMP_Text stock = CreateText("StoreStockText", storeSections, "Stock.");
                SetPrivateField(view, "storeOverviewText", overview);
                SetPrivateField(view, "storeFinanceText", finance);
                SetPrivateField(view, "storeStaffingText", staffing);
                SetPrivateField(view, "storeStockText", stock);

                finance.text = string.Join("\n", new[]
                {
                    "<b>Pricing & Margin</b>",
                    "Measure           | Current     | Read",
                    "------------------+-------------+--------------------------",
                    "Price Adjustment  | +10%        | manual shelf price move",
                    "Avg Sell          | $1.25       | shelf receipts",
                    "Avg Cost          | $0.82       | landed goods cost",
                    "Gross / Unit      | $0.43       | before payroll",
                    "Gross Margin      | 34%         | on selling price",
                    "Markup Over Cost  | 52%         | over landed cost"
                });
                staffing.text = string.Join("\n", new string[18].Select((_, i) => $"Worker and counter detail line {i + 1}"));
                stock.text = string.Join("\n", new string[18].Select((_, i) => $"Stock and reorder detail line {i + 1}"));

                InvokePrivate(view, "EnsureBusinessCashTransferControls", true);
                view.RefreshPropertyDetailLayout();

                RectTransform scrollRoot = FindRect(view.transform, "PropertyDetail_BodyScroll");
                ScrollRect scrollRect = scrollRoot.GetComponent<ScrollRect>();
                RectTransform scrollbarRect = FindRect(scrollRoot, "VerticalScrollbar");
                Image viewportImage = scrollRect.viewport.GetComponent<Image>();
                Image contentImage = scrollRect.content.GetComponent<Image>();
                RectTransform upperRow = FindRect(storeSections, "PropertyDetail_StoreUpperRow");
                RectTransform cashTransfer = FindRect(view.PropertyDetailBodyContent, "BusinessCashTransfer_Root");
                LayoutElement cashLayout = cashTransfer.GetComponent<LayoutElement>();
                Image cashImage = cashTransfer.GetComponent<Image>();

                Assert.NotNull(scrollRect.verticalScrollbar);
                Assert.AreSame(scrollRect.verticalScrollbar.transform, scrollbarRect);
                Assert.AreEqual(ScrollRect.ScrollbarVisibility.Permanent, scrollRect.verticalScrollbarVisibility);
                Assert.IsTrue(scrollRect.vertical);
                Assert.IsFalse(scrollRect.horizontal);
                Assert.NotNull(viewportImage);
                Assert.IsTrue(viewportImage.raycastTarget);
                Assert.NotNull(contentImage);
                Assert.IsTrue(contentImage.raycastTarget);
                Assert.NotNull(scrollRect.viewport.GetComponent<ScrollRectInputRelay>());
                Assert.NotNull(scrollRect.content.GetComponent<ScrollRectInputRelay>());
                Assert.NotNull(storeSections.GetComponent<ScrollRectInputRelay>());
                Assert.IsTrue(upperRow == null || upperRow.GetComponent<ScrollRectInputRelay>() != null);
                Assert.IsTrue(upperRow == null || upperRow.GetComponent<LayoutElement>().ignoreLayout || !upperRow.gameObject.activeSelf);
                Assert.AreSame(storeSections, overview.transform.parent);
                Assert.AreSame(storeSections, finance.transform.parent);
                Assert.AreSame(storeSections, staffing.transform.parent);
                Assert.AreSame(storeSections, stock.transform.parent);
                Assert.Less(overview.transform.GetSiblingIndex(), finance.transform.GetSiblingIndex());
                Assert.Less(finance.transform.GetSiblingIndex(), staffing.transform.GetSiblingIndex());
                Assert.Less(staffing.transform.GetSiblingIndex(), stock.transform.GetSiblingIndex());
                Assert.NotNull(cashTransfer);
                Assert.AreSame(view.PropertyDetailBodyContent, cashTransfer.parent);
                Assert.AreEqual(view.PropertyDetailBodyContent.childCount - 1, cashTransfer.GetSiblingIndex());
                Assert.IsFalse(cashLayout.ignoreLayout);
                Assert.GreaterOrEqual(cashLayout.minHeight, 344f);
                Assert.GreaterOrEqual(cashLayout.preferredHeight, 364f);
                Assert.IsTrue(cashImage == null || cashImage.color.a <= 0.05f);
                Assert.Greater(stock.GetComponent<LayoutElement>().minHeight, 108f);
                Assert.Greater(staffing.GetComponent<LayoutElement>().minHeight, 88f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void ManagementPropertyDetailScrollSetupPreservesExistingUserScrollOffset()
        {
            GameObject viewObject = new("Management Property Scroll Persistence Test", typeof(RectTransform));
            try
            {
                ManagementPanelView view = viewObject.AddComponent<ManagementPanelView>();
                RectTransform detailPanel = CreateRect("PropertyDetail_Panel", viewObject.transform);
                TMP_Text title = CreateText("PropertyDetail_Title", detailPanel, "Player General Store");
                TMP_Text summary = CreateText("PropertyDetail_Summary", detailPanel, "General Store summary.");
                SetPrivateField(view, "propertyDetailTitleText", title);
                SetPrivateField(view, "propertyDetailSummaryText", summary);

                view.EnsurePropertyDetailScrollBody(true);
                RectTransform scrollRoot = FindRect(view.transform, "PropertyDetail_BodyScroll");
                ScrollRect scrollRect = scrollRoot.GetComponent<ScrollRect>();
                RectTransform content = scrollRect.content;
                content.sizeDelta = new Vector2(content.sizeDelta.x, 900f);
                content.anchoredPosition = new Vector2(0f, 280f);
                scrollRect.verticalNormalizedPosition = 0.35f;

                view.EnsurePropertyDetailScrollBody(true);

                Assert.AreEqual(900f, content.sizeDelta.y);
                Assert.AreEqual(280f, content.anchoredPosition.y);
                Assert.AreEqual(0.35f, scrollRect.verticalNormalizedPosition, 0.001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void ManagementStoreFinanceTextPreservesPricingMarginTablePresentation()
        {
            GameObject viewObject = new("Management Store Finance Table Test", typeof(RectTransform));
            try
            {
                ManagementPanelView view = viewObject.AddComponent<ManagementPanelView>();
                RectTransform detailPanel = CreateRect("PropertyDetail_Panel", viewObject.transform);
                TMP_Text title = CreateText("PropertyDetail_Title", detailPanel, "Player General Store");
                TMP_Text summary = CreateText("PropertyDetail_Summary", detailPanel, "General Store summary.");
                SetPrivateField(view, "propertyDetailTitleText", title);
                SetPrivateField(view, "propertyDetailSummaryText", summary);

                view.EnsurePropertyDetailScrollBody(true);

                RectTransform storeSections = FindRect(view.PropertyDetailBodyContent, "PropertyDetail_StoreSections");
                TMP_Text overview = CreateText("StoreOverviewText", storeSections, "Store status.");
                TMP_Text finance = FindText(storeSections, "StoreFinanceText");
                SetPrivateField(view, "storeOverviewText", overview);
                SetPrivateField(view, "storeFinanceText", finance);

                finance.text = string.Join("\n", new[]
                {
                    "<b>Pricing & Margin</b>",
                    "<mspace=0.62em>Measure           | Current     | Read</mspace>",
                    "<mspace=0.62em>------------------+-------------+--------------------------</mspace>",
                    "<mspace=0.62em>Price Adjustment  | <color=#8FCB88>+10%</color>        | manual shelf price move</mspace>",
                    "<mspace=0.62em>Gross Margin      | <color=#8FCB88>34%</color>         | on selling price</mspace>"
                });

                view.RefreshPropertyDetailLayout();

                LayoutElement financeLayout = finance.GetComponent<LayoutElement>();
                Assert.AreEqual(TextWrappingModes.NoWrap, finance.textWrappingMode);
                Assert.AreEqual(TextOverflowModes.Overflow, finance.overflowMode);
                StringAssert.Contains("<mspace=0.62em>Measure", finance.text);
                StringAssert.Contains("| Current", finance.text);
                StringAssert.Contains("Gross Margin", finance.text);
                Assert.GreaterOrEqual(financeLayout.minHeight, 140f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }


        [Test]
        public void SellerMeetingViewRendersWithoutDialogueOptionsAndKeepsShellUsable()
        {
            GameObject canvasObject = new("Seller Meeting Empty Dialogue Test Canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                AcquisitionSellerMeetingView view = AcquisitionSellerMeetingView.GetOrCreate(canvas);
                AcquisitionConversationState state = new()
                {
                    Title = "Town Edge Holding",
                    Stage = AcquisitionDealStage.Inquiry,
                    StageLabel = "Inquiry",
                    FormalActionLabel = "Continue Review",
                    CanAdvanceDeal = true
                };

                view.Render(state);

                Assert.NotNull(view.PrimaryActionButton);
                Assert.NotNull(view.CloseButton);
                Assert.NotNull(view.ResponseRoot);
                Assert.AreEqual("Town Edge Holding", view.TitleText.text);
                Assert.AreEqual("Continue Review", view.ProcessActionText.text);
                Assert.NotNull(FindRect(view.transform, "SellerMeeting_ProcessRail"));
                Assert.LessOrEqual(view.ResponseButtons.Count, 4);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        [Test]
        public void SellerMeetingViewCreatesRequiredShellControls()
        {
            GameObject canvasObject = new("Seller Meeting UI Test Canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                AcquisitionSellerMeetingView view = AcquisitionSellerMeetingView.GetOrCreate(canvas);

                Assert.NotNull(view.TitleText);
                Assert.NotNull(view.StageText);
                Assert.NotNull(view.SellerIdentityText);
                Assert.NotNull(view.SelectedFileText);
                Assert.NotNull(view.SellerLineText);
                Assert.NotNull(view.PostureText);
                Assert.NotNull(view.ResponseRoot);
                Assert.NotNull(view.ProcessActionText);
                Assert.NotNull(view.DialoguePromptText);
                Assert.NotNull(view.PrimaryActionButton);
                Assert.NotNull(view.CloseButton);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        [Test]
        public void SellerMeetingViewCapsDialogueChoicesAtFourAndUsesGroupedRows()
        {
            GameObject canvasObject = new("Seller Meeting Dialogue Test Canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                AcquisitionSellerMeetingView view = AcquisitionSellerMeetingView.GetOrCreate(canvas);
                AcquisitionConversationState state = new()
                {
                    Title = "Main Street Parcel",
                    StageLabel = "Inquiry",
                    FormalActionLabel = "Commit Earnest",
                    CanAdvanceDeal = true
                };

                for (int i = 0; i < 5; i++)
                {
                    state.Options.Add(new AcquisitionConversationOption { Label = $"Choice {i + 1}" });
                }

                view.Render(state);

                Assert.AreEqual(4, view.ResponseButtons.Count);
                Assert.GreaterOrEqual(view.ResponseRoot.childCount, 2);
                Assert.NotNull(FindRect(view.transform, "SellerMeeting_ProcessRail"));
                Assert.NotNull(FindRect(view.ResponseRoot, "SellerMeeting_ResponseRow_0"));
                Assert.NotNull(FindRect(view.ResponseRoot, "SellerMeeting_ResponseRow_1"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        [Test]
        public void SellerMeetingProcessRailBuildsFiveTransactionSteps()
        {
            GameObject canvasObject = new("Seller Meeting Rail Test Canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                AcquisitionSellerMeetingView view = AcquisitionSellerMeetingView.GetOrCreate(canvas);
                AcquisitionConversationState state = new()
                {
                    Title = "Town Edge Holding",
                    Stage = AcquisitionDealStage.EarnestCommitted,
                    StageLabel = "Earnest committed",
                    FormalActionLabel = "Run Diligence",
                    CanAdvanceDeal = true
                };

                state.ProcessSteps.Add(new AcquisitionConversationProcessStep { Label = "Inquiry", Status = AcquisitionConversationProcessStatus.Completed });
                state.ProcessSteps.Add(new AcquisitionConversationProcessStep { Label = "Earnest", Status = AcquisitionConversationProcessStatus.Current });
                state.ProcessSteps.Add(new AcquisitionConversationProcessStep { Label = "Diligence", Status = AcquisitionConversationProcessStatus.Upcoming });
                state.ProcessSteps.Add(new AcquisitionConversationProcessStep { Label = "Terms", Status = AcquisitionConversationProcessStatus.Upcoming });
                state.ProcessSteps.Add(new AcquisitionConversationProcessStep { Label = "Close", Status = AcquisitionConversationProcessStatus.Upcoming });

                view.Render(state);

                RectTransform rail = FindRect(view.transform, "SellerMeeting_ProcessRail");
                Assert.AreEqual(5, rail.childCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        [Test]
        public void SellerMeetingTextDoesNotStealMouseInput()
        {
            GameObject canvasObject = new("Seller Meeting Raycast Test Canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                AcquisitionSellerMeetingView view = AcquisitionSellerMeetingView.GetOrCreate(canvas);
                TMP_Text[] textChildren = view.GetComponentsInChildren<TMP_Text>(true);

                Assert.IsNotEmpty(textChildren);
                for (int i = 0; i < textChildren.Length; i++)
                {
                    Assert.IsFalse(textChildren[i].raycastTarget, textChildren[i].name);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }


        [Test]
        public void ArchivedWorkflowSnapshotDisplaysClosedStateWithoutLiveLead()
        {
            GameObject canvasObject = new("Workflow Archived Close Test Canvas", typeof(RectTransform), typeof(Canvas));
            GameObject controllerObject = new("Ownership Workflow Controller Test", typeof(RectTransform));
            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                OwnershipWorkflowView view = OwnershipWorkflowView.GetOrCreate(canvas);
                OwnershipWorkflowController controller = controllerObject.AddComponent<OwnershipWorkflowController>();
                SetPrivateField(controller, "canvas", canvas);
                SetPrivateField(controller, "view", view);
                SetWorkflowSnapshot(
                    controller,
                    listingId: "business_001",
                    title: "Closed Tannery",
                    actionLabel: "Closed",
                    leadSummary: "Lead: Tannery parcel with staffing baggage.",
                    readinessSummary: "Funding: settled. Commitment Read: buyer already closed.",
                    checklistSummary: "Checklist: move into staffing and cash stabilization.",
                    processSummary: "Process ledger: terms accepted and recorded.");

                InvokePrivate(controller, "RefreshAcquisition");

                Assert.AreEqual("Closed Tannery", view.TitleText.text);
                AssertContainsAny(view.StageText.text, "Acquisition closed", "Deal closed");
                StringAssert.Contains("Status", view.SummaryText.text);
                StringAssert.Contains("stabilization", view.SummaryText.text.ToLowerInvariant());
                AssertContainsAny(view.PostureText.text, "Recorded action", "Last action");
                AssertContainsAny(view.LedgerText.text, "Continuity", "Status");
                Assert.AreEqual("Deal Closed", GetButtonLabel(view.PrimaryButton));
                Assert.AreEqual("Lead Inactive", GetButtonLabel(view.WatchButton));
                Assert.IsFalse(view.PrimaryButton.interactable);
                Assert.IsFalse(view.WatchButton.interactable);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        [Test]
        public void ArchivedWorkflowSnapshotDisplaysNegotiationStateAndRecordedPosture()
        {
            GameObject canvasObject = new("Workflow Archived Negotiation Test Canvas", typeof(RectTransform), typeof(Canvas));
            GameObject controllerObject = new("Ownership Workflow Negotiation Controller Test", typeof(RectTransform));
            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                OwnershipWorkflowView view = OwnershipWorkflowView.GetOrCreate(canvas);
                OwnershipWorkflowController controller = controllerObject.AddComponent<OwnershipWorkflowController>();
                SetPrivateField(controller, "canvas", canvas);
                SetPrivateField(controller, "view", view);
                SetWorkflowSnapshot(
                    controller,
                    listingId: "land_014",
                    title: "Main Street Corner Lot",
                    actionLabel: "Agree Terms",
                    leadSummary: "Lead: quiet frontage with local seller pressure.",
                    readinessSummary: "Funding: cash thin. Commitment Read: active. Deadline: this week.",
                    checklistSummary: "Checklist: settle price before rival pressure rises.",
                    processSummary: "Process posture: diligence complete and terms open.");

                InvokePrivate(controller, "RefreshAcquisition");

                Assert.AreEqual("Main Street Corner Lot", view.TitleText.text);
                AssertContainsAny(view.StageText.text, "terms were last being negotiated", "Terms were taking shape");
                AssertContainsAny(view.SummaryText.text, "Last move", "Last next move");
                AssertContainsAny(view.PostureText.text, "Recorded action", "Last action");
                AssertContainsAny(view.PostureText.text, "Last seriousness", "Seriousness");
                AssertContainsAny(view.LedgerText.text, "Continuity", "Process", "Status");
                AssertContainsAny(GetButtonLabel(view.PrimaryButton), "Lead Inactive", "Lead Unavailable");
                Assert.IsFalse(view.PrimaryButton.interactable);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        [Test]
        public void OpenInspectionClearsArchivedWorkflowSnapshotSoOldLeadDoesNotBleedIntoInspection()
        {
            GameObject canvasObject = new("Workflow Inspection Reset Test Canvas", typeof(RectTransform), typeof(Canvas));
            GameObject controllerObject = new("Ownership Workflow Inspection Controller Test", typeof(RectTransform));
            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                OwnershipWorkflowView view = OwnershipWorkflowView.GetOrCreate(canvas);
                OwnershipWorkflowController controller = controllerObject.AddComponent<OwnershipWorkflowController>();
                controller.Configure(canvas, null, null, null, null);
                SetPrivateField(controller, "view", view);
                SetWorkflowSnapshot(
                    controller,
                    listingId: "land_021",
                    title: "Old Snapshot",
                    actionLabel: "Run Diligence",
                    leadSummary: "Lead: should disappear on inspection reset.",
                    readinessSummary: "Funding: thin.",
                    checklistSummary: "Checklist: old data.",
                    processSummary: "Process: old data.");

                controller.OpenInspection(-1, -1);

                Assert.AreEqual("Property Inspection", view.TitleText.text);
                StringAssert.Contains("Inspection only", view.StageText.text);
                StringAssert.Contains("No acquisition lead is selected.", view.SummaryText.text);
                Assert.AreEqual("No Seller Meeting", GetButtonLabel(view.PrimaryButton));
                Assert.AreEqual("Inspection Only", GetButtonLabel(view.WatchButton));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        private static RectTransform FindRect(Transform root, string name)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == name)
                {
                    return children[i] as RectTransform;
                }
            }

            Assert.Fail($"Missing UI child '{name}'.");
            return null;
        }

        private static TMP_Text FindText(Transform root, string name)
        {
            TMP_Text[] children = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == name)
                {
                    return children[i];
                }
            }

            Assert.Fail($"Missing text '{name}'.");
            return null;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.transform as RectTransform;
        }

        private static Button CreateButton(string name, Transform parent, string label)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Button button = go.GetComponent<Button>();
            GameObject labelObject = new("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(go.transform, false);
            TMP_Text text = labelObject.GetComponent<TMP_Text>();
            text.text = label;
            return button;
        }

        private static TMP_Text CreateText(string name, Transform parent, string value)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            TMP_Text text = go.GetComponent<TMP_Text>();
            text.text = value;
            text.fontSize = 14f;
            text.textWrappingMode = TextWrappingModes.Normal;
            return text;
        }


        // Archived workflow continuity changed shape during this lane: one controller snapshot lives in a
        // nested struct, while an earlier implementation stored the same data as flat private fields.
        // The UI regression tests should survive either representation as long as the archived workflow
        // experience remains intact.
        private static void SetWorkflowSnapshot(
            OwnershipWorkflowController controller,
            string listingId,
            string title,
            string actionLabel,
            string leadSummary,
            string readinessSummary,
            string checklistSummary,
            string processSummary)
        {
            Assert.NotNull(controller);

            System.Type controllerType = typeof(OwnershipWorkflowController);
            System.Type snapshotType = controllerType.GetNestedType("WorkflowSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
            if (snapshotType != null && controllerType.GetField("lastWorkflowSnapshot", BindingFlags.Instance | BindingFlags.NonPublic) != null)
            {
                object snapshot = Activator.CreateInstance(snapshotType);
                SetField(snapshotType, snapshot, "ListingId", listingId);
                SetField(snapshotType, snapshot, "Title", title);
                SetField(snapshotType, snapshot, "ActionLabel", actionLabel);
                SetField(snapshotType, snapshot, "LeadSummary", leadSummary);
                SetField(snapshotType, snapshot, "ReadinessSummary", readinessSummary);
                SetField(snapshotType, snapshot, "ChecklistSummary", checklistSummary);
                SetField(snapshotType, snapshot, "ProcessSummary", processSummary);
                SetPrivateField(controller, "lastWorkflowSnapshot", snapshot);
                return;
            }

            FieldInfo flatListingField = controllerType.GetField("workflowSnapshotListingId", BindingFlags.Instance | BindingFlags.NonPublic);
            if (flatListingField != null)
            {
                flatListingField.SetValue(controller, listingId);
                SetPrivateField(controller, "workflowSnapshotTitle", title);
                SetPrivateField(controller, "workflowSnapshotActionLabel", actionLabel);
                SetPrivateField(controller, "workflowSnapshotLeadSummary", leadSummary);
                SetPrivateField(controller, "workflowSnapshotReadinessSummary", readinessSummary);
                SetPrivateField(controller, "workflowSnapshotChecklistSummary", checklistSummary);
                SetPrivateField(controller, "workflowSnapshotProcessNotesSummary", processSummary);
                SetPrivateField(controller, "currentListingId", listingId);
                SetPrivateField(controller, "currentListing", CreateArchivedListing(listingId, title));
                SetPrivateField(controller, "currentListingSelectedInMarket", false);
                SetPrivateField(controller, "workflowContinuityStatus", "This lead is no longer active in the acquisition market. Inspect the site record and continue through Properties, or reopen a live lead from Acquisitions.");

                if (controllerType.GetField("acquisitionMarket", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(controller) == null)
                {
                    AcquisitionMarketManager market = controller.gameObject.AddComponent<AcquisitionMarketManager>();
                    SetPrivateField(controller, "acquisitionMarket", market);
                }

                return;
            }

            Assert.Fail("OwnershipWorkflowController does not expose a recognized archived workflow snapshot shape.");
        }

        private static object CreateArchivedListing(string listingId, string title)
        {
            object listing = Activator.CreateInstance(typeof(AcquisitionListing), true);
            SetFieldOrProperty(typeof(AcquisitionListing), listing, "listingId", listingId);
            SetFieldOrProperty(typeof(AcquisitionListing), listing, "title", title);
            SetFieldOrProperty(typeof(AcquisitionListing), listing, "plotId", -1);
            SetFieldOrProperty(typeof(AcquisitionListing), listing, "buildingId", -1);
            return listing;
        }

        private static void SetField(System.Type type, object target, string fieldName, object value)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(field, $"Missing field '{fieldName}'.");
            field.SetValue(target, value);
        }

        private static void SetFieldOrProperty(System.Type type, object target, string memberName, object value)
        {
            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                property.SetValue(target, value);
                return;
            }

            Assert.Fail($"Missing field or property '{memberName}'.");
        }

        private static void AssertContainsAny(string actual, params string[] expectedFragments)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(actual), "Expected non-empty text.");
            for (int i = 0; i < expectedFragments.Length; i++)
            {
                if (actual.IndexOf(expectedFragments[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return;
                }
            }

            Assert.Fail($"Expected one of [{string.Join(", ", expectedFragments)}] inside '{actual}'.");
        }

        private static string GetButtonLabel(Button button)
        {
            Assert.NotNull(button);
            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            Assert.NotNull(text, $"Button '{button.name}' is missing a text label.");
            return text.text;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"Missing private field '{fieldName}'.");
            field.SetValue(target, value);
        }

        private static void InvokePrivate(object target, string methodName, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method, $"Missing private method '{methodName}'.");
            method.Invoke(target, args);
        }
    }
}
