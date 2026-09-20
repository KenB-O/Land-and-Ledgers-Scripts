using System;
using System.Collections.Generic;

namespace LandLedgers.Economy
{
    public enum AcquisitionConversationOptionKind
    {
        Information = 0,
        Funding = 1,
        SellerPosition = 2,
        DealFollowUp = 3,
        Diligence = 4,
        ClosingRisk = 5
    }

    public enum AcquisitionSellerPosture
    {
        Unknown = 0,
        Open = 1,
        Guarded = 2,
        Pressured = 3,
        Formal = 4
    }

    public enum AcquisitionConversationProcessStatus
    {
        Upcoming = 0,
        Current = 1,
        Completed = 2
    }

    [Serializable]
    public sealed class AcquisitionConversationProcessStep
    {
        public string Label { get; set; } = string.Empty;
        public AcquisitionConversationProcessStatus Status { get; set; }
        public string Summary { get; set; } = string.Empty;
    }

    [Serializable]
    public sealed class AcquisitionConversationOption
    {
        public string OptionId { get; set; } = string.Empty;
        public AcquisitionConversationOptionKind Kind { get; set; }
        public string Label { get; set; } = string.Empty;
        public string ShortcutLabel { get; set; } = string.Empty;
        public string IntentText { get; set; } = string.Empty;
        public string BuyerLine { get; set; } = string.Empty;
        public string SellerReplyText { get; set; } = string.Empty;
        public string OutcomeTag { get; set; } = string.Empty;
        public string ResponseText { get; set; } = string.Empty;
    }

    [Serializable]
    public sealed class AcquisitionConversationState
    {
        public string ListingId { get; set; } = string.Empty;
        public AcquisitionListingKind Kind { get; set; }
        public string Title { get; set; } = string.Empty;
        public string SellerDisplayName { get; set; } = string.Empty;
        public AcquisitionDealStage Stage { get; set; } = AcquisitionDealStage.None;
        public AcquisitionDealStage FailedFromStage { get; set; } = AcquisitionDealStage.None;
        public string StageLabel { get; set; } = string.Empty;
        public string FailureBreakpointLabel { get; set; } = string.Empty;
        public string FormalActionLabel { get; set; } = string.Empty;
        public bool CanAdvanceDeal { get; set; }
        public AcquisitionSellerPosture SellerPosture { get; set; } = AcquisitionSellerPosture.Unknown;
        public string SellerMotiveLabel { get; set; } = string.Empty;
        public string SellerPressureLabel { get; set; } = string.Empty;
        public string BuyerSeriousnessLabel { get; set; } = string.Empty;
        public string OpeningLine { get; set; } = string.Empty;
        public string SellerLine { get; set; } = string.Empty;
        public string ProcessSummary { get; set; } = string.Empty;
        public string DialoguePrompt { get; set; } = string.Empty;
        public string PostureSummary { get; set; } = string.Empty;
        public string ReadinessSummary { get; set; } = string.Empty;
        public string LeadQualitySummary { get; set; } = string.Empty;
        public string ProofOfFundsSummary { get; set; } = string.Empty;
        public string DiligenceSummary { get; set; } = string.Empty;
        public string ClosingRiskSummary { get; set; } = string.Empty;
        public string TimingSummary { get; set; } = string.Empty;
        public string NextStepSummary { get; set; } = string.Empty;
        public string DecisionSummary { get; set; } = string.Empty;
        public string ChecklistSummary { get; set; } = string.Empty;
        public string LeadAccessSummary { get; set; } = string.Empty;
        public string FollowUpSummary { get; set; } = string.Empty;
        public List<AcquisitionConversationProcessStep> ProcessSteps { get; } = new();
        public List<AcquisitionConversationOption> Options { get; } = new();
    }
}
