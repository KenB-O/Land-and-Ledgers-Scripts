using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.UI;
using LandLedgers.World;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LandLedgers.FirstLedger
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(325)]
    public sealed class OwnershipWorkflowController : MonoBehaviour
    {
        [SerializeField] private Canvas canvas;
        [SerializeField] private OwnershipWorkflowView view;
        [SerializeField] private TownWorldController townWorld;
        [SerializeField] private AcquisitionMarketManager acquisitionMarket;
        [SerializeField] private PlayerDebtManager playerDebtManager;
        [SerializeField] private AcquisitionSellerMeetingController sellerMeetingController;

        private readonly PropertyPreviewCameraRig previewRig = new();
        private AcquisitionListing currentListing;
        private AcquisitionMarketSection currentSection;
        private int currentPlotId = -1;
        private int currentBuildingId = -1;
        private string currentListingId = string.Empty;
        private bool currentListingSelectedInMarket;
        private string workflowContinuityStatus = string.Empty;
        private WorkflowSnapshot lastWorkflowSnapshot;
        private bool loanMode;
        private bool listenersBound;
        private int previewRequestVersion;
        private AcquisitionSellerMeetingController boundSellerMeetingController;

        private struct WorkflowSnapshot
        {
            public string ListingId;
            public string Title;
            public string ActionLabel;
            public string LeadSummary;
            public string ReadinessSummary;
            public string ChecklistSummary;
            public string ProcessSummary;

            public bool HasContent => !string.IsNullOrWhiteSpace(ListingId) || !string.IsNullOrWhiteSpace(Title);
        }

        private void Awake()
        {
            AutoWire();
            EnsureView();
            BindListeners();
            BindSellerMeetingEvents();
        }

        private void OnDestroy()
        {
            UnbindSellerMeetingEvents();
            previewRig.Dispose();
        }

        private void Update()
        {
            if (view != null
                && view.Root != null
                && view.Root.gameObject.activeInHierarchy
                && FirstLedgerKeyboardInput.WasPressedThisFrame(Key.Escape))
            {
                Close();
            }
        }

        public void Configure(
            Canvas newCanvas,
            TownWorldController newTownWorld,
            AcquisitionMarketManager newAcquisitionMarket,
            PlayerDebtManager newPlayerDebtManager,
            AcquisitionSellerMeetingController newSellerMeetingController = null)
        {
            canvas = newCanvas != null ? newCanvas : canvas;
            townWorld = newTownWorld != null ? newTownWorld : townWorld;
            acquisitionMarket = newAcquisitionMarket != null ? newAcquisitionMarket : acquisitionMarket;
            playerDebtManager = newPlayerDebtManager != null ? newPlayerDebtManager : playerDebtManager;
            sellerMeetingController = newSellerMeetingController != null ? newSellerMeetingController : sellerMeetingController;
            EnsureView();
            BindListeners();
            sellerMeetingController?.Configure(canvas, acquisitionMarket);
            BindSellerMeetingEvents();
        }

        public void OpenLoanWorkflow()
        {
            AutoWire();
            EnsureView();
            loanMode = true;
            currentListing = null;
            currentListingId = string.Empty;
            currentListingSelectedInMarket = false;
            workflowContinuityStatus = string.Empty;
            ClearWorkflowSnapshot();
            currentPlotId = -1;
            currentBuildingId = -1;
            RefreshLoan();
            view?.SetVisible(true);
        }

        public void OpenAcquisitionWorkflow(AcquisitionMarketSection section)
        {
            AutoWire();
            if (acquisitionMarket == null || !acquisitionMarket.TryGetSelectedListing(section, out AcquisitionListing listing))
            {
                OpenInspection(-1, -1);
                return;
            }

            OpenAcquisitionWorkflow(listing, section);
        }

        public void OpenAcquisitionWorkflow(AcquisitionListing listing, AcquisitionMarketSection section)
        {
            AutoWire();
            EnsureView();
            loanMode = false;
            currentListing = listing;
            currentListingId = listing != null ? listing.listingId : string.Empty;
            currentSection = section;
            currentPlotId = listing != null ? listing.plotId : -1;
            currentBuildingId = listing != null ? listing.buildingId : -1;
            ClearWorkflowSnapshot();
            SyncWorkflowLeadSelection();
            RefreshAcquisition();
            view?.SetVisible(true);
        }

        public void OpenInspection(int plotId, int buildingId)
        {
            AutoWire();
            EnsureView();
            loanMode = false;
            currentListing = null;
            currentListingId = string.Empty;
            currentListingSelectedInMarket = false;
            workflowContinuityStatus = string.Empty;
            ClearWorkflowSnapshot();
            currentPlotId = plotId;
            currentBuildingId = buildingId;
            if (acquisitionMarket != null)
            {
                if (buildingId >= 0 && acquisitionMarket.TrySelectListingByBuildingId(buildingId, out AcquisitionMarketSection buildingSection, out AcquisitionListing buildingListing))
                {
                    currentSection = buildingSection;
                    currentListing = buildingListing;
                    currentListingId = buildingListing != null ? buildingListing.listingId : string.Empty;
                }
                else if (plotId >= 0 && acquisitionMarket.TrySelectListingByPlotId(plotId, out AcquisitionMarketSection plotSection, out AcquisitionListing plotListing))
                {
                    currentSection = plotSection;
                    currentListing = plotListing;
                    currentListingId = plotListing != null ? plotListing.listingId : string.Empty;
                }
            }

            RefreshAcquisition();
            view?.SetVisible(true);
        }

        public void Close()
        {
            view?.SetVisible(false);
            LLFeedbackService.Play(LLFeedbackKind.UIBack, "ownership workflow close", canvas);
        }

        private void BindListeners()
        {
            if (listenersBound || view == null)
            {
                return;
            }

            view.CloseButton?.onClick.AddListener(Close);
            view.WatchButton?.onClick.AddListener(ToggleWatch);
            view.ConditionButton?.onClick.AddListener(() => Reveal(AcquisitionDiligenceClueKind.VisibleCondition));
            view.StaffingButton?.onClick.AddListener(() => Reveal(AcquisitionDiligenceClueKind.Staffing));
            view.SupplierButton?.onClick.AddListener(() => Reveal(AcquisitionDiligenceClueKind.SupplierWeakness));
            view.DemandButton?.onClick.AddListener(() => Reveal(AcquisitionDiligenceClueKind.DemandFit));
            view.ValueButton?.onClick.AddListener(() => Reveal(AcquisitionDiligenceClueKind.RoughValue));
            view.ReliabilityButton?.onClick.AddListener(() => Reveal(AcquisitionDiligenceClueKind.Reliability));
            view.SeriousnessButton?.onClick.AddListener(CycleSeriousness);
            view.StanceButton?.onClick.AddListener(CycleStance);
            view.PrimaryButton?.onClick.AddListener(PrimaryAction);
            view.LoanMinusHundredButton?.onClick.AddListener(() => AdjustLoan(-100));
            view.LoanMinusTenButton?.onClick.AddListener(() => AdjustLoan(-10));
            view.LoanPlusTenButton?.onClick.AddListener(() => AdjustLoan(10));
            view.LoanPlusHundredButton?.onClick.AddListener(() => AdjustLoan(100));
            listenersBound = true;
        }

        private void ToggleWatch()
        {
            if (!EnsureCurrentLeadIsActionable("watch mark toggle"))
            {
                return;
            }

            acquisitionMarket.TryToggleWatchListing(currentListing.listingId, out _);
            LLFeedbackService.Play(LLFeedbackKind.UIConfirm, "acquisition watch mark toggle", canvas);
            RefreshAcquisition();
        }

        private void Reveal(AcquisitionDiligenceClueKind kind)
        {
            if (!EnsureCurrentLeadIsActionable($"quick diligence {kind}"))
            {
                return;
            }

            acquisitionMarket.TryRevealQuickDiligenceClue(currentListing.listingId, kind, out _);
            LLFeedbackService.Play(LLFeedbackKind.UIConfirm, $"quick diligence {kind} reveal", canvas);
            RefreshAcquisition();
        }

        private void CycleSeriousness()
        {
            if (!EnsureCurrentLeadIsActionable("seriousness selection"))
            {
                return;
            }

            acquisitionMarket.TryCycleListingSeriousness(currentListing.listingId, out _);
            LLFeedbackService.Play(LLFeedbackKind.UISelect, "acquisition seriousness selection", canvas);
            RefreshAcquisition();
        }

        private void CycleStance()
        {
            if (!EnsureCurrentLeadIsActionable("integration stance selection"))
            {
                return;
            }

            acquisitionMarket.TryCycleListingIntegrationStance(currentListing.listingId, out _);
            LLFeedbackService.Play(LLFeedbackKind.UISelect, "acquisition integration stance selection", canvas);
            RefreshAcquisition();
        }

        private void PrimaryAction()
        {
            if (loanMode)
            {
                playerDebtManager?.SubmitApplication(out _);
                LLFeedbackService.Play(LLFeedbackKind.StampApproval, "formal loan workflow submit", canvas);
                RefreshLoan();
                return;
            }

            if (!EnsureCurrentLeadIsActionable("seller meeting entry"))
            {
                return;
            }

            EnsureSellerMeetingController()?.OpenForListing(currentListing.listingId);
            LLFeedbackService.Play(LLFeedbackKind.UIPanel, "seller meeting open from formal workflow", canvas);
            RefreshAcquisition();
        }

        private bool EnsureCurrentLeadIsActionable(string actionContext)
        {
            if (acquisitionMarket != null && currentListing != null && SyncWorkflowLeadSelection())
            {
                return true;
            }

            LLFeedbackService.Play(LLFeedbackKind.UIBack, $"acquisition {actionContext} blocked by unavailable lead", canvas);
            RefreshAcquisition();
            return false;
        }

        private void AdjustLoan(int dollars)
        {
            playerDebtManager?.AdjustRequestedAmountDollars(dollars);
            LLFeedbackService.Play(LLFeedbackKind.UISelect, "formal loan amount adjust", canvas);
            RefreshLoan();
        }

        private void RefreshAcquisition()
        {
            if (view == null)
            {
                return;
            }

            bool hasLiveWorkflowLead = SyncWorkflowLeadSelection();

            if (!hasLiveWorkflowLead && lastWorkflowSnapshot.HasContent)
            {
                RefreshArchivedWorkflowSnapshot(lastWorkflowSnapshot);
                return;
            }

            view.SetLoanMode(false);
            SetText(view.TitleText, currentListing != null ? currentListing.title : "Property Inspection");

            string actionLabel = hasLiveWorkflowLead && acquisitionMarket != null
                ? NormalizeAcquisitionActionLabel(acquisitionMarket.GetSelectedAcquisitionActionLabel(currentSection))
                : "No Process";
            string meetingEntryLabel = BuildMeetingEntryLabel(currentListing != null, hasLiveWorkflowLead);

            if (hasLiveWorkflowLead)
            {
                CaptureWorkflowSnapshot(actionLabel);
            }

            SetText(view.StageText, BuildWorkflowStageText(actionLabel, currentListing != null, hasLiveWorkflowLead));
            SetText(view.SummaryText, BuildWorkflowLeadSummary());
            SetText(view.SiteText, BuildWorkflowInspectionSummary());
            SetText(view.PostureText, BuildAcquisitionPosture());
            SetText(view.LedgerText, BuildWorkflowNotes());

            SetButtonLabel(view.PrimaryButton, meetingEntryLabel);
            SetButtonLabel(view.CloseButton, "Back");
            SetButtonInteractable(view.PrimaryButton, hasLiveWorkflowLead);
            SetButtonInteractable(view.WatchButton, hasLiveWorkflowLead);
            SetButtonInteractable(view.ConditionButton, hasLiveWorkflowLead);
            SetButtonInteractable(view.StaffingButton, hasLiveWorkflowLead);
            SetButtonInteractable(view.SupplierButton, hasLiveWorkflowLead);
            SetButtonInteractable(view.DemandButton, hasLiveWorkflowLead);
            SetButtonInteractable(view.ValueButton, hasLiveWorkflowLead);
            SetButtonInteractable(view.ReliabilityButton, hasLiveWorkflowLead);
            SetButtonInteractable(view.SeriousnessButton, hasLiveWorkflowLead);
            SetButtonInteractable(view.StanceButton, hasLiveWorkflowLead);
            SetButtonLabel(view.WatchButton, hasLiveWorkflowLead && acquisitionMarket != null && acquisitionMarket.IsSelectedListingWatched(currentSection) ? "Unmark" : "Mark Later");
            UpdateDiligenceButtonLabels();

            QueueStablePreview(currentPlotId, currentBuildingId);
        }

        private void RefreshArchivedWorkflowSnapshot(WorkflowSnapshot snapshot)
        {
            view.SetLoanMode(false);
            string actionLabel = NormalizeAcquisitionActionLabel(snapshot.ActionLabel);
            bool closed = string.Equals(actionLabel, "Closed", StringComparison.OrdinalIgnoreCase);

            SetText(view.TitleText, string.IsNullOrWhiteSpace(snapshot.Title) ? "Archived Acquisition Lead" : snapshot.Title);
            SetText(view.StageText, BuildArchivedWorkflowStageText(actionLabel));
            SetText(view.SummaryText, BuildArchivedWorkflowSummary(snapshot));
            SetText(view.SiteText, string.IsNullOrWhiteSpace(snapshot.ChecklistSummary)
                ? "Archived checklist unavailable."
                : snapshot.ChecklistSummary);
            SetText(view.PostureText, BuildArchivedWorkflowPosture(snapshot));
            SetText(view.LedgerText, BuildArchivedWorkflowLedger(snapshot));

            SetButtonLabel(view.PrimaryButton, closed ? "Deal Closed" : "Lead Unavailable");
            SetButtonLabel(view.CloseButton, "Back");
            SetButtonLabel(view.WatchButton, "Lead Inactive");
            SetButtonInteractable(view.PrimaryButton, false);
            SetButtonInteractable(view.WatchButton, false);
            SetButtonInteractable(view.ConditionButton, false);
            SetButtonInteractable(view.StaffingButton, false);
            SetButtonInteractable(view.SupplierButton, false);
            SetButtonInteractable(view.DemandButton, false);
            SetButtonInteractable(view.ValueButton, false);
            SetButtonInteractable(view.ReliabilityButton, false);
            SetButtonInteractable(view.SeriousnessButton, false);
            SetButtonInteractable(view.StanceButton, false);
            UpdateDiligenceButtonLabels();
            QueueStablePreview(currentPlotId, currentBuildingId);
        }

        private void CaptureWorkflowSnapshot(string actionLabel)
        {
            if (currentListing == null || string.IsNullOrWhiteSpace(currentListingId))
            {
                ClearWorkflowSnapshot();
                return;
            }

            lastWorkflowSnapshot = new WorkflowSnapshot
            {
                ListingId = currentListingId,
                Title = currentListing.title,
                ActionLabel = actionLabel,
                LeadSummary = BuildWorkflowLeadSummary(),
                ReadinessSummary = BuildAcquisitionPosture(),
                ChecklistSummary = BuildWorkflowInspectionSummary(),
                ProcessSummary = BuildWorkflowNotes()
            };
        }

        private void ClearWorkflowSnapshot()
        {
            lastWorkflowSnapshot = default;
        }

        private void RefreshLoan()
        {
            if (view == null)
            {
                return;
            }

            view.SetLoanMode(true);
            SetText(view.TitleText, "Formal Loan Application");
            SetText(view.StageText, playerDebtManager != null && playerDebtManager.HasPendingApplication
                ? "Application pending bank review."
                : "Review the terms, then submit the draft.");
            SetText(view.SummaryText, BuildLoanSummary());
            SetText(view.SiteText, BuildLoanTermsSummary());
            SetText(view.PostureText, playerDebtManager != null ? playerDebtManager.BuildPanelStatusText() : "Loan records unavailable.");
            SetText(view.LedgerText, playerDebtManager != null ? playerDebtManager.BuildActiveLoanText() : string.Empty);

            SetButtonLabel(view.PrimaryButton, "Submit Review");
            SetButtonLabel(view.CloseButton, "Back");
            SetButtonInteractable(view.PrimaryButton, playerDebtManager != null && playerDebtManager.CanSubmitApplication);
            QueueStablePreview(-1, -1);
        }

        private void QueueStablePreview(int plotId, int buildingId)
        {
            int version = ++previewRequestVersion;
            previewRig.BeginPreview(townWorld, plotId, buildingId, view.PreviewImage, view.PreviewFallbackText);
            StartCoroutine(RenderStablePreviewWhenReady(version, plotId, buildingId));
        }

        private IEnumerator RenderStablePreviewWhenReady(int version, int plotId, int buildingId)
        {
            yield return null;

            const int maxAttempts = 4;
            for (int attempt = 0; attempt < maxAttempts && version == previewRequestVersion; attempt++)
            {
                if (previewRig.TryRenderStablePreview(townWorld, plotId, buildingId, view.PreviewImage, view.PreviewFallbackText))
                {
                    yield break;
                }

                yield return null;
            }
        }

        private string BuildWorkflowLeadSummary()
        {
            if (acquisitionMarket == null || currentListing == null)
            {
                return "No acquisition lead is selected.";
            }

            if (!currentListingSelectedInMarket)
            {
                return workflowContinuityStatus;
            }

            string leadRaw = StripLeadingHeading(acquisitionMarket.BuildSelectedLeadCompactSummary(currentSection), "Selected Lead");
            string readinessRaw = StripLeadingHeading(acquisitionMarket.BuildSelectedLeadReadinessSummary(currentSection), "Funding & Readiness");
            string actionLabel = NormalizeAcquisitionActionLabel(acquisitionMarket.GetSelectedAcquisitionActionLabel(currentSection));

            StringBuilder builder = new();
            AppendKeyLine(builder, "Lead", leadRaw, new[] { "Lead", "Use", "Seller", "Owner", "Price", "Asking", "Source", "Confidence", "Urgency", "Deadline" });
            AppendKeyLine(builder, "Source", leadRaw, new[] { "Source", "Confidence", "Urgency", "Exclusivity", "Deadline", "Rival" });
            AppendKeyLine(builder, "Terms", leadRaw, new[] { "Asking", "Price", "Value", "State", "Use", "Plot", "Building" });
            AppendDirectLine(builder, "Next move", BuildActionGuidance(actionLabel));
            AppendKeyLine(builder, "Readiness", readinessRaw, new[] { "Funding", "Readiness", "Commitment", "Runway", "Execution", "Liquidity" });
            AppendRemainderLines(builder, leadRaw, 2,
                "Lead", "Selected Lead", "Use", "Seller", "Owner", "Price", "Asking", "Source", "Confidence", "Urgency", "Deadline", "Exclusivity", "Rival", "Value", "State", "Plot", "Building");

            return builder.Length > 0
                ? builder.ToString().TrimEnd()
                : "No compact lead summary is available yet.";
        }

        private string BuildWorkflowInspectionSummary()
        {
            string raw = BuildInspectionSummary();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "No site summary is available.";
            }

            List<string> kept = new();
            string[] lines = raw.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = NormalizeWhitespace(lines[i]);
                if (string.IsNullOrWhiteSpace(line) || ShouldSkipInspectionLine(line))
                {
                    continue;
                }

                kept.Add(ClampLineLength(line, 120));
                if (kept.Count >= 6)
                {
                    break;
                }
            }

            if (kept.Count <= 0)
            {
                kept.Add("No compact site summary is available yet.");
            }

            return string.Join("\n", kept);
        }

        private static bool ShouldSkipInspectionLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return true;
            }

            string[] blockedPrefixes =
            {
                "Inspection",
                "Acquisition Lead",
                "Checklist:",
                "Watchlist:",
                "Pipeline:",
                "Liquidity:",
                "Process posture:",
                "Process ledger:",
                "Decision climate:",
                "Selected Lead:",
                "Owner Read",
                "Commitment Read",
                "Runway Read",
                "Execution Load",
                "Town commerce:",
                "Commerce Ledger",
                "Service pressure:",
                "Weekly review:",
                "Decision:"
            };

            for (int i = 0; i < blockedPrefixes.Length; i++)
            {
                if (line.StartsWith(blockedPrefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string BuildInspectionSummary()
        {
            if (townWorld == null)
            {
                return "Town records unavailable.";
            }

            if (currentBuildingId >= 0)
            {
                return townWorld.BuildBuildingInspectionSummary(currentBuildingId);
            }

            if (currentPlotId >= 0)
            {
                return townWorld.BuildPlotInspectionSummary(currentPlotId);
            }

            return "No property selected.";
        }

        private string BuildAcquisitionPosture()
        {
            if (acquisitionMarket == null)
            {
                return "Acquisition records unavailable.";
            }

            if (currentListing != null && !currentListingSelectedInMarket)
            {
                return workflowContinuityStatus;
            }

            if (currentListing == null)
            {
                string marketRaw = StripLeadingHeading(acquisitionMarket.BuildAcquisitionDashboardSnapshotSummary(currentSection), "Market Snapshot");
                StringBuilder marketBuilder = new();
                AppendKeyLine(marketBuilder, "Market", marketRaw, new[] { "For sale", "Market", "Pipeline", "Watchlist", "Liquidity", "Service pressure", "Town" });
                AppendRemainderLines(marketBuilder, marketRaw, 4,
                    "Market Snapshot", "Selected Lead", "Checklist", "Decision", "Last");
                return marketBuilder.Length > 0
                    ? marketBuilder.ToString().TrimEnd()
                    : "No market posture summary is available.";
            }

            string readinessRaw = StripLeadingHeading(acquisitionMarket.BuildSelectedLeadReadinessSummary(currentSection), "Funding & Readiness");
            string checklistRaw = StripLeadingHeading(acquisitionMarket.BuildSelectedLeadActionChecklist(currentSection), "Checklist");
            string actionLabel = NormalizeAcquisitionActionLabel(acquisitionMarket.GetSelectedAcquisitionActionLabel(currentSection));
            StringBuilder builder = new();
            AppendDirectLine(builder, "Action now", BuildActionGuidance(actionLabel));
            AppendKeyLine(builder, "Funding", readinessRaw, new[] { "Funding", "Liquidity", "Runway", "Cash" });
            AppendKeyLine(builder, "Seriousness", readinessRaw, new[] { "Commitment", "Readiness", "Execution", "Seriousness" });
            AppendKeyLine(builder, "Pressure", readinessRaw, new[] { "Urgency", "Deadline", "Rival", "Pressure", "Decision" });
            AppendRemainderLines(builder, checklistRaw, 2, "Checklist", "Selected Lead", "Funding & Readiness");
            AppendRemainderLines(builder, readinessRaw, 2,
                "Funding", "Liquidity", "Runway", "Cash", "Commitment", "Readiness", "Execution", "Seriousness", "Urgency", "Deadline", "Rival", "Pressure", "Decision");
            return builder.Length > 0
                ? builder.ToString().TrimEnd()
                : "No readiness summary is available yet.";
        }

        private string BuildWorkflowNotes()
        {
            if (loanMode)
            {
                return playerDebtManager != null ? playerDebtManager.BuildActiveLoanText() : "No active loan record is available.";
            }

            if (acquisitionMarket == null)
            {
                return "Acquisition records unavailable.";
            }

            if (currentListing != null && !currentListingSelectedInMarket)
            {
                return "The seller meeting or market desk moved this lead out of the active acquisition lane. Inspect the site, then continue through Properties or reopen a live lead from Acquisitions.";
            }

            string processRaw = StripLeadingHeading(acquisitionMarket.BuildAcquisitionCompactProcessNotesSummary(currentSection), "Process Notes");
            string checklistRaw = currentListing != null
                ? StripLeadingHeading(acquisitionMarket.BuildSelectedLeadActionChecklist(currentSection), "Checklist")
                : string.Empty;
            StringBuilder builder = new();
            AppendKeyLine(builder, "Process", processRaw, new[] { "Pipeline", "Watchlist", "Process posture", "Process ledger", "Decision climate", "Decision" });
            AppendRemainderLines(builder, checklistRaw, 3, "Checklist");
            AppendRemainderLines(builder, processRaw, 5, "Process Notes", "Pipeline", "Watchlist", "Process posture", "Process ledger", "Decision climate", "Decision");
            return builder.Length > 0
                ? builder.ToString().TrimEnd()
                : "No compact process notes are available.";
        }

        private static string BuildWorkflowStageText(string actionLabel, bool hasListing, bool hasLiveMarketLead)
        {
            if (!hasListing)
            {
                return "Inspection only - select a live lead to review readiness, price, and next action.";
            }

            if (!hasLiveMarketLead)
            {
                return "Lead no longer active - inspect the site record and continue through Properties or reopen a live acquisition lead.";
            }

            return actionLabel switch
            {
                "Open Inquiry" => "Lead spotted - enter the seller meeting to open a formal inquiry and test seller seriousness.",
                "Commit Earnest" => "Inquiry open - enter the seller meeting to commit earnest money and hold the lane.",
                "Run Diligence" => "Earnest committed - enter the seller meeting to authorize diligence before price drift or rival pressure.",
                "Agree Terms" => "Diligence complete - enter the seller meeting to lock terms while the lead is still warm.",
                "Close Acquisition" => "Terms agreed - enter the seller meeting to close and prepare the first integration move.",
                "Closed" => "Acquisition closed - move to stabilization, staffing, and early cash control.",
                _ => "Inspection active - review the lead and decide whether to commit capital."
            };
        }

        private static string BuildArchivedWorkflowStageText(string actionLabel)
        {
            return actionLabel switch
            {
                "Closed" => "Acquisition closed - move to stabilization, staffing, and early cash control.",
                "Agree Terms" => "Terms were taking shape when this lead left the active market.",
                "Close Acquisition" => "Closing was the recorded next step when this lead left the active market.",
                "Run Diligence" => "Diligence was the recorded next step when this lead left the active market.",
                "Commit Earnest" => "Earnest commitment was the recorded next step when this lead left the active market.",
                "Open Inquiry" => "Inquiry was the recorded next step when this lead left the active market.",
                _ => "Lead no longer active - inspect the site record and continue through Properties or reopen a live acquisition lead."
            };
        }

        private static string BuildArchivedWorkflowSummary(WorkflowSnapshot snapshot)
        {
            StringBuilder builder = new();
            AppendDirectLine(builder, "Status", "This acquisition lead is no longer active in the market.");
            AppendDirectLine(builder, "Last move", BuildActionGuidance(NormalizeAcquisitionActionLabel(snapshot.ActionLabel)));
            AppendArchivedLine(builder, snapshot.LeadSummary);
            AppendArchivedLine(builder, snapshot.ReadinessSummary);
            AppendArchivedLine(builder, snapshot.ChecklistSummary);
            return builder.ToString().TrimEnd();
        }

        private static string BuildArchivedWorkflowPosture(WorkflowSnapshot snapshot)
        {
            StringBuilder builder = new();
            AppendDirectLine(builder, "Recorded action", string.IsNullOrWhiteSpace(snapshot.ActionLabel) ? "Lead inactive" : snapshot.ActionLabel);
            AppendDirectLine(builder, "Last seriousness", "Use the archived notes as a record; reopen a live lead before changing posture.");
            AppendArchivedLine(builder, snapshot.ReadinessSummary);
            return builder.ToString().TrimEnd();
        }

        private static string BuildArchivedWorkflowLedger(WorkflowSnapshot snapshot)
        {
            StringBuilder builder = new();
            AppendDirectLine(builder, "Continuity", "Archived workflow snapshot retained after the live market lead closed or expired.");
            AppendArchivedLine(builder, snapshot.ProcessSummary);
            AppendArchivedLine(builder, snapshot.ChecklistSummary);
            return builder.ToString().TrimEnd();
        }

        private static void AppendArchivedLine(StringBuilder builder, string value)
        {
            if (builder == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string normalized = ClampLineLength(NormalizeDisplayLine(value), 118);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                builder.AppendLine(normalized);
            }
        }

        private static string BuildMeetingEntryLabel(bool hasListing, bool hasLiveMarketLead)
        {
            if (!hasListing)
            {
                return "No Seller Meeting";
            }

            return hasLiveMarketLead ? "Enter Seller Meeting" : "Lead Unavailable";
        }

        private static string NormalizeAcquisitionActionLabel(string label)
        {
            return string.Equals(label, "Close", StringComparison.OrdinalIgnoreCase)
                ? "Close Acquisition"
                : label;
        }

        private static string BuildActionGuidance(string actionLabel)
        {
            return actionLabel switch
            {
                "Open Inquiry" => "Test whether the seller will treat you as a serious buyer.",
                "Commit Earnest" => "Secure the window before you spend more time or diligence budget.",
                "Run Diligence" => "Check condition, staff, suppliers, demand, and value before agreeing terms.",
                "Agree Terms" => "Convert what you learned into a clean price and close structure.",
                "Close Acquisition" => "Be ready to absorb inherited problems the day the deal closes.",
                "Closed" => "Shift from deal-making to stabilization and operating control.",
                _ => "Review the lead, capital pressure, and fit before committing."
            };
        }

        private static void AppendKeyLine(StringBuilder builder, string label, string raw, string[] preferredPrefixes)
        {
            string line = FindFirstMatchingLine(raw, preferredPrefixes);
            if (!string.IsNullOrWhiteSpace(line))
            {
                AppendDirectLine(builder, label, line);
            }
        }

        private static void AppendDirectLine(StringBuilder builder, string label, string value)
        {
            if (builder == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string normalized = ClampLineLength(NormalizeDisplayLine(value), 118);
            if (!string.IsNullOrWhiteSpace(label) && LineMatchesPrefix(normalized, label))
            {
                builder.AppendLine(normalized);
                return;
            }

            builder.Append(label);
            builder.Append(": ");
            builder.AppendLine(normalized);
        }

        private static void AppendRemainderLines(StringBuilder builder, string raw, int maxLines, params string[] blockedPrefixes)
        {
            if (builder == null || maxLines <= 0)
            {
                return;
            }

            List<string> lines = CollectUsefulLines(raw, blockedPrefixes);
            int appended = 0;
            for (int i = 0; i < lines.Count && appended < maxLines; i++)
            {
                builder.AppendLine(ClampLineLength(lines[i], 118));
                appended++;
            }
        }

        private static string FindFirstMatchingLine(string raw, string[] prefixes)
        {
            if (prefixes == null || prefixes.Length <= 0)
            {
                return string.Empty;
            }

            List<string> lines = CollectUsefulLines(raw);
            for (int prefixIndex = 0; prefixIndex < prefixes.Length; prefixIndex++)
            {
                string prefix = prefixes[prefixIndex];
                for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                {
                    if (LineMatchesPrefix(lines[lineIndex], prefix))
                    {
                        return lines[lineIndex];
                    }
                }
            }

            return lines.Count > 0 ? lines[0] : string.Empty;
        }

        private static List<string> CollectUsefulLines(string raw, params string[] blockedPrefixes)
        {
            List<string> lines = new();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return lines;
            }

            string[] split = raw.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < split.Length; i++)
            {
                string line = NormalizeDisplayLine(split[i]);
                if (string.IsNullOrWhiteSpace(line) || IsBlockedDisplayLine(line, blockedPrefixes))
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

        private static bool IsBlockedDisplayLine(string line, string[] blockedPrefixes)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return true;
            }

            if (blockedPrefixes != null)
            {
                for (int i = 0; i < blockedPrefixes.Length; i++)
                {
                    if (LineMatchesPrefix(line, blockedPrefixes[i]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool LineMatchesPrefix(string line, string prefix)
        {
            return !string.IsNullOrWhiteSpace(line)
                && !string.IsNullOrWhiteSpace(prefix)
                && line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDisplayLine(string value)
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

        private void UpdateDiligenceButtonLabels()
        {
            SetDiligenceLabel(view.ConditionButton, AcquisitionDiligenceClueKind.VisibleCondition, "Condition");
            SetDiligenceLabel(view.StaffingButton, AcquisitionDiligenceClueKind.Staffing, "Staffing");
            SetDiligenceLabel(view.SupplierButton, AcquisitionDiligenceClueKind.SupplierWeakness, "Supplier");
            SetDiligenceLabel(view.DemandButton, AcquisitionDiligenceClueKind.DemandFit, "Demand");
            SetDiligenceLabel(view.ValueButton, AcquisitionDiligenceClueKind.RoughValue, "Value");
            SetDiligenceLabel(view.ReliabilityButton, AcquisitionDiligenceClueKind.Reliability, "Reliability");
        }

        private void SetDiligenceLabel(Button button, AcquisitionDiligenceClueKind kind, string label)
        {
            bool revealed = acquisitionMarket != null
                && currentListingSelectedInMarket
                && currentListing != null
                && acquisitionMarket.IsDiligenceClueRevealed(currentListing.listingId, kind);
            SetButtonLabel(button, revealed ? $"{label} Done" : label);
        }

        private string BuildLoanSummary()
        {
            if (playerDebtManager == null)
            {
                return "Loan records are unavailable.";
            }

            StringBuilder builder = new();
            builder.AppendLine(playerDebtManager.BuildRequestedAmountText());
            builder.AppendLine(playerDebtManager.BuildEstimatedPaymentText());
            builder.AppendLine(playerDebtManager.BuildPaymentScheduleText());
            return builder.ToString().TrimEnd();
        }

        private string BuildLoanTermsSummary()
        {
            if (playerDebtManager == null)
            {
                return "Use the controls below to build a draft once financing records are available.";
            }

            StringBuilder builder = new();
            builder.AppendLine(playerDebtManager.BuildEstimatedTotalOwedText());
            builder.AppendLine(playerDebtManager.BuildLenderStandingText());
            builder.AppendLine(playerDebtManager.HasPendingApplication
                ? "The current application is already pending bank review."
                : "Use the amount controls below, then submit the draft for review.");
            return builder.ToString().TrimEnd();
        }


        private void HandleSellerMeetingConversationStateChanged(string listingId)
        {
            if (loanMode || view == null || view.Root == null || !view.Root.gameObject.activeInHierarchy)
            {
                return;
            }

            // Cleared-state broadcasts are used when the meeting cannot open or loses its lead.
            // Refresh the formal workflow so it can fall back to its own continuity messaging.
            if (string.IsNullOrWhiteSpace(listingId))
            {
                RefreshAcquisition();
                return;
            }

            if (!string.IsNullOrWhiteSpace(currentListingId) && string.Equals(currentListingId, listingId, StringComparison.Ordinal))
            {
                RefreshAcquisition();
            }
        }

        private void BindSellerMeetingEvents()
        {
            if (boundSellerMeetingController == sellerMeetingController || sellerMeetingController == null)
            {
                return;
            }

            UnbindSellerMeetingEvents();
            boundSellerMeetingController = sellerMeetingController;
            boundSellerMeetingController.ConversationStateChanged += HandleSellerMeetingConversationStateChanged;
        }

        private void UnbindSellerMeetingEvents()
        {
            if (boundSellerMeetingController == null)
            {
                return;
            }

            boundSellerMeetingController.ConversationStateChanged -= HandleSellerMeetingConversationStateChanged;
            boundSellerMeetingController = null;
        }

        private bool SyncWorkflowLeadSelection()
        {
            currentListingSelectedInMarket = false;
            workflowContinuityStatus = string.Empty;

            if (loanMode || acquisitionMarket == null || currentListing == null)
            {
                return false;
            }

            AcquisitionListing replacementListing = null;
            bool foundReplacementForSite = false;

            // A formal workflow belongs to one acquisition file. Building/plot lookup is only a way
            // to re-select that same file after UI navigation; it must not silently adopt a relisted file.
            if (currentBuildingId >= 0
                && acquisitionMarket.TrySelectListingByBuildingId(currentBuildingId, out AcquisitionMarketSection buildingSection, out AcquisitionListing buildingListing)
                && buildingListing != null)
            {
                if (IsCurrentWorkflowListing(buildingListing))
                {
                    currentSection = buildingSection;
                    currentListing = buildingListing;
                    currentListingId = buildingListing.listingId ?? currentListingId;
                    currentListingSelectedInMarket = true;
                    return true;
                }

                replacementListing = buildingListing;
                foundReplacementForSite = true;
            }

            if (currentPlotId >= 0
                && acquisitionMarket.TrySelectListingByPlotId(currentPlotId, out AcquisitionMarketSection plotSection, out AcquisitionListing plotListing)
                && plotListing != null)
            {
                if (IsCurrentWorkflowListing(plotListing))
                {
                    currentSection = plotSection;
                    currentListing = plotListing;
                    currentListingId = plotListing.listingId ?? currentListingId;
                    currentListingSelectedInMarket = true;
                    return true;
                }

                replacementListing ??= plotListing;
                foundReplacementForSite = true;
            }

            workflowContinuityStatus = foundReplacementForSite
                ? $"The original acquisition file is no longer active. A different live file now exists for this site ({DescribeReplacementLead(replacementListing)}); reopen it from Acquisitions or Properties before taking formal action."
                : "This lead is no longer active in the acquisition market. Inspect the site record and continue through Properties, or reopen a live lead from Acquisitions.";
            return false;
        }

        private bool IsCurrentWorkflowListing(AcquisitionListing listing)
        {
            if (listing == null || string.IsNullOrWhiteSpace(currentListingId))
            {
                return false;
            }

            return string.Equals(listing.listingId, currentListingId, StringComparison.Ordinal);
        }

        private static string DescribeReplacementLead(AcquisitionListing listing)
        {
            if (listing == null)
            {
                return "new listing";
            }

            if (!string.IsNullOrWhiteSpace(listing.title))
            {
                return listing.title.Trim();
            }

            if (!string.IsNullOrWhiteSpace(listing.listingId))
            {
                return listing.listingId.Trim();
            }

            return "new listing";
        }

        private void EnsureView()
        {
            if (view != null)
            {
                return;
            }

            canvas = canvas != null ? canvas : FindAnyObjectByType<Canvas>();
            view = OwnershipWorkflowView.GetOrCreate(canvas);
        }

        private void AutoWire()
        {
            townWorld ??= FindAnyObjectByType<TownWorldController>();
            acquisitionMarket ??= FindAnyObjectByType<AcquisitionMarketManager>();
            playerDebtManager ??= FindAnyObjectByType<PlayerDebtManager>();
            sellerMeetingController ??= FindAnyObjectByType<AcquisitionSellerMeetingController>();
            canvas ??= FindAnyObjectByType<Canvas>();
            if (sellerMeetingController == null && Application.isPlaying)
            {
                GameObject sellerMeetingObject = new("Acquisition Seller Meeting Controller");
                sellerMeetingController = sellerMeetingObject.AddComponent<AcquisitionSellerMeetingController>();
            }

            sellerMeetingController?.Configure(canvas, acquisitionMarket);
            BindSellerMeetingEvents();
        }

        private AcquisitionSellerMeetingController EnsureSellerMeetingController()
        {
            AutoWire();
            return sellerMeetingController;
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

        private static string NormalizeWhitespace(string value)
        {
            return NormalizeDisplayLine(value);
        }

        private static string ClampLineLength(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength - 1).TrimEnd() + "…";
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
                text.text = label ?? string.Empty;
            }
        }

        private static void SetButtonInteractable(Button button, bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }
    }
}
