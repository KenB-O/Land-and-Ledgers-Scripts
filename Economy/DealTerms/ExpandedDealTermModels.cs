using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.DealTerms
{
    public enum InventoryTransferMode { Unspecified = 0, Included = 1, Excluded = 2, Partial = 3 }
    public enum DelayedPossessionSource { None = 0, SellerRequested = 1, BuyerRequested = 2, Mutual = 3, OperationalConstraint = 4 }
    public enum SellerFinancingPaymentCadence { Weekly = 0, Monthly = 1 }
    public enum SellerFinancingPerformance { None = 0, Honored = 1, Missed = 2 }

    public enum ExpandedDealTermReasonCode
    {
        None = 0,
        InventoryIncluded = 1,
        InventoryExcluded = 2,
        PartialInventoryTransfer = 3,
        DelayedPossession = 4,
        SellerPossessionBenefit = 5,
        RetainedLandLease = 6,
        BusinessOnlyTransfer = 7,
        SellerFinancingRequested = 8,
        SellerFinancingOffered = 9,
        SellerFinancingReducesBuyerBurden = 10,
        SellerFinancingAddsSellerRisk = 11,
        CleanExpandedTerms = 12,
        BuyerBurdenRaised = 100,
        RiskRaised = 101,
        SellerCashNeedConflict = 102,
        InventoryRestartBurden = 103,
        DelayedPossessionBurden = 104,
        RetainedLandControlRisk = 105,
        DocumentationWeak = 106,
        LongPossessionDelay = 107,
        ShortLeaseTerm = 108,
        WeakRenewalSecurity = 109,
        HeavySellerControl = 110,
        BalloonPaymentRisk = 111,
        InventoryScopeUnclear = 112,
        PossessionWindowUnclear = 113,
        LeaseEconomicsUnclear = 114,
        FinancingStructureUnclear = 115
    }

    public enum ExpandedDealInspectionFocus { None = 0, Documentation = 1, Financing = 2, Inventory = 3, Possession = 4, LandControl = 5, GeneralRisk = 6 }
    public enum ExpandedDealSeverityBand { Low = 0, Moderate = 1, High = 2, Severe = 3 }
    public enum ExpandedDealReadinessClass { Straightforward = 0, ReviewClosely = 1, ClosingFragile = 2 }

    [Flags]
    public enum ExpandedDealTagFlags
    {
        None = 0,
        StandardTransfer = 1 << 0,
        SpecialStructure = 1 << 1,
        RoutineDiligence = 1 << 2,
        EnhancedDiligence = 1 << 3,
        ClosingCare = 1 << 4,
        FinancingReview = 1 << 5,
        PossessionReview = 1 << 6,
        LandControlReview = 1 << 7,
        InventoryReview = 1 << 8,
        DocumentationReview = 1 << 9,
        ClarificationReview = 1 << 10
    }

    [Flags]
    public enum ExpandedDealWorkflowStepFlags
    {
        None = 0,
        Financing = 1 << 0,
        LandControl = 1 << 1,
        PossessionTiming = 1 << 2,
        InventoryHandoff = 1 << 3,
        Documentation = 1 << 4,
        ClosingCare = 1 << 5,
        GeneralReview = 1 << 6
    }

    [Serializable]
    public struct SellerFinancingTerms
    {
        public bool requested;
        public bool offered;
        public int principalCents;
        public int downPaymentCents;
        public int annualInterestRateBps;
        public int termMonths;
        public int amortizationMonths;
        public int balloonPaymentCents;
        public SellerFinancingPaymentCadence paymentCadence;
        public float sellerRiskTolerance01;

        public bool HasAuthoredFinancingShape => principalCents > 0 || downPaymentCents > 0 || annualInterestRateBps > 0 || termMonths > 0 || amortizationMonths > 0 || balloonPaymentCents > 0;
        public bool HasExplicitPrincipalShape => principalCents > 0 || downPaymentCents > 0;
        public bool HasTerms => requested || offered || HasAuthoredFinancingShape;
        public bool UsesStructuredSellerFinancing => requested || offered;
        public bool NeedsOfferedStructureClarification => offered && !HasExplicitPrincipalShape;
        public bool NeedsStructureClarification => (HasAuthoredFinancingShape && !UsesStructuredSellerFinancing) || NeedsOfferedStructureClarification;

        public SellerFinancingTerms Sanitized(int purchasePriceCents = 0)
        {
            int price = Mathf.Max(0, purchasePriceCents);
            SellerFinancingTerms sanitized = this;
            sanitized.principalCents = Mathf.Max(0, sanitized.principalCents);
            sanitized.downPaymentCents = Mathf.Max(0, sanitized.downPaymentCents);
            sanitized.annualInterestRateBps = Mathf.Max(0, sanitized.annualInterestRateBps);
            sanitized.termMonths = Mathf.Max(0, sanitized.termMonths);
            sanitized.amortizationMonths = Mathf.Max(0, sanitized.amortizationMonths);
            sanitized.balloonPaymentCents = Mathf.Max(0, sanitized.balloonPaymentCents);
            sanitized.sellerRiskTolerance01 = Mathf.Clamp01(sanitized.sellerRiskTolerance01);

            if (price > 0)
            {
                sanitized.downPaymentCents = Mathf.Clamp(sanitized.downPaymentCents, 0, price);
                int maxPrincipalCents = Mathf.Max(0, price - sanitized.downPaymentCents);
                sanitized.principalCents = Mathf.Clamp(sanitized.principalCents, 0, maxPrincipalCents);
                sanitized.balloonPaymentCents = Mathf.Clamp(sanitized.balloonPaymentCents, 0, sanitized.principalCents > 0 ? sanitized.principalCents : maxPrincipalCents);
            }

            if (sanitized.offered && sanitized.HasExplicitPrincipalShape)
            {
                if (sanitized.termMonths <= 0) sanitized.termMonths = 60;
                if (sanitized.amortizationMonths <= 0) sanitized.amortizationMonths = sanitized.termMonths;
            }

            return sanitized;
        }

        public int GetFinancedPrincipalCents(int purchasePriceCents)
        {
            SellerFinancingTerms sanitized = Sanitized(purchasePriceCents);
            if (!sanitized.offered || sanitized.NeedsOfferedStructureClarification) return 0;
            if (sanitized.principalCents > 0) return Mathf.Min(sanitized.principalCents, Mathf.Max(0, purchasePriceCents));
            return Mathf.Max(0, Mathf.Max(0, purchasePriceCents) - sanitized.downPaymentCents);
        }
    }

    [Serializable]
    public struct InventoryTransferTerms
    {
        public InventoryTransferMode transferMode;
        public int estimatedInventoryValueCents;
        public float operatingContinuity01;
        public float restockBurden01;
        public float verificationRisk01;

        public bool HasAuthoredHandoffShape => estimatedInventoryValueCents > 0 || operatingContinuity01 > 0f || restockBurden01 > 0f || verificationRisk01 > 0f;
        public bool HasTerms => transferMode != InventoryTransferMode.Unspecified || HasAuthoredHandoffShape;
        public bool IncludesAnyInventory => transferMode == InventoryTransferMode.Included || transferMode == InventoryTransferMode.Partial;
        public bool ExcludesInventory => transferMode == InventoryTransferMode.Excluded;
        public bool NeedsStructureClarification => transferMode == InventoryTransferMode.Unspecified && HasAuthoredHandoffShape;
        public bool NeedsScopeClarification => NeedsStructureClarification || (transferMode == InventoryTransferMode.Partial && estimatedInventoryValueCents <= 0);

        public InventoryTransferTerms Sanitized()
        {
            InventoryTransferTerms sanitized = this;
            sanitized.estimatedInventoryValueCents = Mathf.Max(0, sanitized.estimatedInventoryValueCents);
            sanitized.operatingContinuity01 = Mathf.Clamp01(sanitized.operatingContinuity01);
            sanitized.restockBurden01 = Mathf.Clamp01(sanitized.restockBurden01);
            sanitized.verificationRisk01 = Mathf.Clamp01(sanitized.verificationRisk01);
            if (sanitized.transferMode == InventoryTransferMode.Excluded && sanitized.restockBurden01 <= 0f) sanitized.restockBurden01 = 0.5f;
            if (sanitized.transferMode == InventoryTransferMode.Partial)
            {
                if (sanitized.operatingContinuity01 <= 0f) sanitized.operatingContinuity01 = 0.35f;
                if (sanitized.restockBurden01 <= 0f) sanitized.restockBurden01 = 0.35f;
                if (sanitized.verificationRisk01 <= 0f) sanitized.verificationRisk01 = 0.2f;
            }
            else if (sanitized.IncludesAnyInventory && sanitized.operatingContinuity01 <= 0f)
            {
                sanitized.operatingContinuity01 = 0.5f;
            }
            return sanitized;
        }
    }

    [Serializable]
    public struct DelayedPossessionTerms
    {
        public bool delayed;
        public int minimumDelayDays;
        public int maximumDelayDays;
        public DelayedPossessionSource source;
        public float sellerContinuityNeed01;

        public bool HasAuthoredPossessionShape => minimumDelayDays > 0 || maximumDelayDays > 0 || source != DelayedPossessionSource.None || sellerContinuityNeed01 > 0f;
        public bool HasTerms => delayed || HasAuthoredPossessionShape;
        public bool NeedsStructureClarification => HasAuthoredPossessionShape && !delayed;
        public bool NeedsWindowClarification => delayed && maximumDelayDays <= 0;
        public int AverageDelayDays => Mathf.RoundToInt((Mathf.Max(0, minimumDelayDays) + Mathf.Max(Mathf.Max(0, minimumDelayDays), maximumDelayDays)) * 0.5f);

        public DelayedPossessionTerms Sanitized()
        {
            DelayedPossessionTerms sanitized = this;
            sanitized.minimumDelayDays = Mathf.Clamp(sanitized.minimumDelayDays, 0, 365);
            sanitized.maximumDelayDays = Mathf.Clamp(Mathf.Max(sanitized.minimumDelayDays, sanitized.maximumDelayDays), 0, 365);
            sanitized.sellerContinuityNeed01 = Mathf.Clamp01(sanitized.sellerContinuityNeed01);
            if (sanitized.HasAuthoredPossessionShape) sanitized.delayed = true;
            if (sanitized.delayed && sanitized.source == DelayedPossessionSource.None) sanitized.source = DelayedPossessionSource.Mutual;
            return sanitized;
        }
    }

    [Serializable]
    public struct RetainedLandLeaseTerms
    {
        public bool retainedLandLease;
        public bool businessOnlyTransfer;
        public int weeklyLeasePaymentCents;
        public int leaseTermMonths;
        public int estimatedRetainedLandValueCents;
        public float renewalSecurity01;
        public float sellerControlRights01;
        public float purchaseOptionValue01;

        public bool HasAuthoredControlShape => weeklyLeasePaymentCents > 0 || leaseTermMonths > 0 || estimatedRetainedLandValueCents > 0 || renewalSecurity01 > 0f || sellerControlRights01 > 0f || purchaseOptionValue01 > 0f;
        public bool HasTerms => retainedLandLease || businessOnlyTransfer || HasAuthoredControlShape;
        public bool NeedsStructureClarification => HasAuthoredControlShape && !retainedLandLease && !businessOnlyTransfer;
        public bool NeedsOccupancyEconomicsClarification => businessOnlyTransfer && !retainedLandLease && weeklyLeasePaymentCents <= 0 && leaseTermMonths <= 0 && purchaseOptionValue01 <= 0f;
        public bool NeedsLeaseEconomicsClarification => retainedLandLease && weeklyLeasePaymentCents <= 0 && purchaseOptionValue01 <= 0f;

        public RetainedLandLeaseTerms Sanitized()
        {
            RetainedLandLeaseTerms sanitized = this;
            sanitized.weeklyLeasePaymentCents = Mathf.Max(0, sanitized.weeklyLeasePaymentCents);
            sanitized.leaseTermMonths = Mathf.Max(0, sanitized.leaseTermMonths);
            sanitized.estimatedRetainedLandValueCents = Mathf.Max(0, sanitized.estimatedRetainedLandValueCents);
            sanitized.renewalSecurity01 = Mathf.Clamp01(sanitized.renewalSecurity01);
            sanitized.sellerControlRights01 = Mathf.Clamp01(sanitized.sellerControlRights01);
            sanitized.purchaseOptionValue01 = Mathf.Clamp01(sanitized.purchaseOptionValue01);
            if ((sanitized.weeklyLeasePaymentCents > 0 || sanitized.estimatedRetainedLandValueCents > 0) && !sanitized.businessOnlyTransfer) sanitized.retainedLandLease = true;
            if (sanitized.retainedLandLease)
            {
                sanitized.businessOnlyTransfer = true;
                if (sanitized.leaseTermMonths <= 0) sanitized.leaseTermMonths = 36;
            }
            return sanitized;
        }
    }

    [Serializable]
    public struct ExpandedDealTerms
    {
        public SellerFinancingTerms sellerFinancing;
        public InventoryTransferTerms inventoryTransfer;
        public DelayedPossessionTerms delayedPossession;
        public RetainedLandLeaseTerms retainedLandLease;
        public float documentationQuality01;

        public bool HasAnyTerms => sellerFinancing.HasTerms || inventoryTransfer.HasTerms || delayedPossession.HasTerms || retainedLandLease.HasTerms;
        public bool NeedsFinancingStructureClarification => sellerFinancing.NeedsStructureClarification;
        public bool NeedsInventoryScopeClarification => inventoryTransfer.NeedsScopeClarification;
        public bool NeedsPossessionWindowClarification => delayedPossession.NeedsWindowClarification || delayedPossession.NeedsStructureClarification;
        public bool NeedsLeaseEconomicsClarification => retainedLandLease.NeedsStructureClarification || retainedLandLease.NeedsLeaseEconomicsClarification || retainedLandLease.NeedsOccupancyEconomicsClarification;
        public bool NeedsClarification => NeedsFinancingStructureClarification || NeedsInventoryScopeClarification || NeedsPossessionWindowClarification || NeedsLeaseEconomicsClarification;
        public bool HasExplicitWeakDocumentation => documentationQuality01 > 0f && documentationQuality01 < 0.65f;
        public bool NeedsDocumentationReview => HasExplicitWeakDocumentation;
        public bool NeedsClarificationReview => NeedsClarification;
        public bool NeedsFinancingReview => sellerFinancing.UsesStructuredSellerFinancing || sellerFinancing.NeedsStructureClarification;
        public bool NeedsOperationalHandoffReview => delayedPossession.delayed || delayedPossession.NeedsStructureClarification || inventoryTransfer.transferMode == InventoryTransferMode.Excluded || inventoryTransfer.transferMode == InventoryTransferMode.Partial || inventoryTransfer.NeedsStructureClarification;
        public bool NeedsLandControlReview => retainedLandLease.retainedLandLease || retainedLandLease.businessOnlyTransfer || retainedLandLease.NeedsStructureClarification;
        public bool IsSpecialStructure => NeedsFinancingReview || NeedsOperationalHandoffReview || NeedsLandControlReview;

        public ExpandedDealTagFlags TagFlags => GetTagFlags();
        public ExpandedDealWorkflowStepFlags WorkflowStepFlags => BuildWorkflowStepFlags(TagFlags);
        public string TagSummary => BuildTagSummary(TagFlags);
        public string WorkflowStepSummary => BuildWorkflowStepSummary(WorkflowStepFlags);
        public string StructureSummary => IsSpecialStructure ? BuildStructureSummary() : "standard transfer";
        public string ClarificationSummary => BuildClarificationSummary();
        public string ReviewSummary => BuildReviewSummary();
        public string WorkflowReasonSummary => BuildWorkflowReasonSummary();
        public string AuthoringWarningSummary => BuildAuthoringWarningSummary();
        public string InspectionSummaryBlock => BuildInspectionSummaryBlock();

        public ExpandedDealTerms Sanitized(int purchasePriceCents = 0)
        {
            ExpandedDealTerms sanitized = this;
            sanitized.sellerFinancing = sanitized.sellerFinancing.Sanitized(purchasePriceCents);
            sanitized.inventoryTransfer = sanitized.inventoryTransfer.Sanitized();
            sanitized.delayedPossession = sanitized.delayedPossession.Sanitized();
            sanitized.retainedLandLease = sanitized.retainedLandLease.Sanitized();
            sanitized.documentationQuality01 = Mathf.Clamp01(sanitized.documentationQuality01);
            if (sanitized.HasAnyTerms && sanitized.documentationQuality01 <= 0f)
            {
                int complexity = 0;
                if (sanitized.NeedsOperationalHandoffReview) complexity++;
                if (sanitized.NeedsLandControlReview) complexity++;
                if (sanitized.NeedsFinancingReview) complexity++;
                if (sanitized.NeedsClarificationReview) complexity++;
                sanitized.documentationQuality01 = complexity switch { 0 => 0.8f, 1 => 0.76f, 2 => 0.72f, _ => 0.68f };
            }
            return sanitized;
        }

        private ExpandedDealTagFlags GetTagFlags()
        {
            ExpandedDealTagFlags flags = IsSpecialStructure ? ExpandedDealTagFlags.SpecialStructure : ExpandedDealTagFlags.StandardTransfer;
            if (NeedsFinancingReview) flags |= ExpandedDealTagFlags.FinancingReview;
            if (delayedPossession.delayed || delayedPossession.NeedsStructureClarification) flags |= ExpandedDealTagFlags.PossessionReview;
            if (NeedsLandControlReview) flags |= ExpandedDealTagFlags.LandControlReview;
            if (inventoryTransfer.transferMode == InventoryTransferMode.Excluded || inventoryTransfer.transferMode == InventoryTransferMode.Partial || inventoryTransfer.NeedsStructureClarification) flags |= ExpandedDealTagFlags.InventoryReview;
            if (NeedsDocumentationReview) flags |= ExpandedDealTagFlags.DocumentationReview;
            if (NeedsClarificationReview) flags |= ExpandedDealTagFlags.ClarificationReview;
            return flags;
        }

        private string BuildStructureSummary()
        {
            List<string> parts = new List<string>(6);
            AddIf(parts, sellerFinancing.UsesStructuredSellerFinancing, "seller financing");
            AddIf(parts, sellerFinancing.NeedsStructureClarification, "seller-financing terms unclear");
            AddIf(parts, delayedPossession.delayed, "delayed possession");
            AddIf(parts, retainedLandLease.retainedLandLease, "retained-land lease");
            AddIf(parts, retainedLandLease.businessOnlyTransfer && !retainedLandLease.retainedLandLease, "business-only transfer");
            AddIf(parts, retainedLandLease.NeedsStructureClarification, "land-control terms unclear");
            AddIf(parts, inventoryTransfer.transferMode == InventoryTransferMode.Excluded, "inventory excluded");
            AddIf(parts, inventoryTransfer.transferMode == InventoryTransferMode.Partial, "partial inventory transfer");
            AddIf(parts, inventoryTransfer.NeedsStructureClarification, "inventory handoff unclear");
            return parts.Count > 0 ? string.Join(", ", parts) : "standard transfer";
        }

        private string BuildReviewSummary()
        {
            List<string> parts = new List<string>(5);
            AddIf(parts, NeedsFinancingReview, "financing review");
            AddIf(parts, NeedsOperationalHandoffReview, "operational handoff review");
            AddIf(parts, NeedsLandControlReview, "land-control review");
            AddIf(parts, NeedsClarification, "term clarification");
            AddIf(parts, HasExplicitWeakDocumentation, "documentation review");
            return parts.Count > 0 ? string.Join(", ", parts) : "routine diligence";
        }

        private string BuildClarificationSummary()
        {
            List<string> parts = new List<string>(4);
            AddIf(parts, NeedsFinancingStructureClarification, "clarify seller-financing structure");
            AddIf(parts, NeedsInventoryScopeClarification, "clarify inventory handoff scope");
            AddIf(parts, NeedsPossessionWindowClarification, "clarify possession window");
            AddIf(parts, NeedsLeaseEconomicsClarification, "clarify retained-land or occupancy economics");
            return parts.Count > 0 ? string.Join(", ", parts) : "none";
        }

        private string BuildWorkflowReasonSummary()
        {
            List<string> parts = new List<string>(5);
            AddIf(parts, NeedsFinancingReview, "financing terms affect closeability");
            AddIf(parts, NeedsLandControlReview, "land control affects operating continuity");
            AddIf(parts, delayedPossession.delayed || delayedPossession.NeedsStructureClarification, "possession timing affects handoff");
            AddIf(parts, inventoryTransfer.transferMode != InventoryTransferMode.Unspecified || inventoryTransfer.NeedsStructureClarification, "inventory handoff affects opening condition");
            AddIf(parts, NeedsDocumentationReview || NeedsClarificationReview, "paperwork or terms need clarification");
            return parts.Count > 0 ? string.Join("; ", parts) : "routine diligence only";
        }

        private string BuildAuthoringWarningSummary()
        {
            List<string> parts = new List<string>(5);
            AddIf(parts, sellerFinancing.NeedsStructureClarification, "seller-financing fields are under-specified");
            AddIf(parts, inventoryTransfer.NeedsStructureClarification, "inventory metrics are authored without a transfer mode");
            AddIf(parts, delayedPossession.NeedsWindowClarification, "delayed possession lacks a maximum window");
            AddIf(parts, retainedLandLease.NeedsStructureClarification, "land-control fields are authored without a retained-land or business-only structure");
            AddIf(parts, retainedLandLease.NeedsOccupancyEconomicsClarification, "business-only transfer lacks occupancy economics");
            return parts.Count > 0 ? string.Join("; ", parts) : "none";
        }

        private string BuildInspectionSummaryBlock()
        {
            List<string> lines = new List<string>(6)
            {
                $"Structure: {StructureSummary}",
                $"Review: {ReviewSummary}",
                $"Workflow steps: {WorkflowStepSummary}",
                $"Why: {WorkflowReasonSummary}"
            };
            if (NeedsClarification) lines.Add($"Clarify: {ClarificationSummary}");
            if (AuthoringWarningSummary != "none") lines.Add($"Authoring warnings: {AuthoringWarningSummary}");
            lines.Add($"Tags: {TagSummary}");
            return string.Join("\n", lines);
        }

        public static ExpandedDealWorkflowStepFlags BuildWorkflowStepFlags(ExpandedDealTagFlags flags)
        {
            ExpandedDealWorkflowStepFlags steps = ExpandedDealWorkflowStepFlags.None;
            if ((flags & ExpandedDealTagFlags.FinancingReview) != 0) steps |= ExpandedDealWorkflowStepFlags.Financing;
            if ((flags & ExpandedDealTagFlags.LandControlReview) != 0) steps |= ExpandedDealWorkflowStepFlags.LandControl;
            if ((flags & ExpandedDealTagFlags.PossessionReview) != 0) steps |= ExpandedDealWorkflowStepFlags.PossessionTiming;
            if ((flags & ExpandedDealTagFlags.InventoryReview) != 0) steps |= ExpandedDealWorkflowStepFlags.InventoryHandoff;
            if ((flags & ExpandedDealTagFlags.DocumentationReview) != 0 || (flags & ExpandedDealTagFlags.ClarificationReview) != 0) steps |= ExpandedDealWorkflowStepFlags.Documentation;
            if ((flags & ExpandedDealTagFlags.ClosingCare) != 0) steps |= ExpandedDealWorkflowStepFlags.ClosingCare;
            if (steps == ExpandedDealWorkflowStepFlags.None && (flags & ExpandedDealTagFlags.EnhancedDiligence) != 0) steps |= ExpandedDealWorkflowStepFlags.GeneralReview;
            return steps;
        }

        public static string BuildWorkflowStepSummary(ExpandedDealWorkflowStepFlags steps)
        {
            List<string> labels = new List<string>(7);
            AddIf(labels, (steps & ExpandedDealWorkflowStepFlags.Financing) != 0, "financing");
            AddIf(labels, (steps & ExpandedDealWorkflowStepFlags.LandControl) != 0, "land control");
            AddIf(labels, (steps & ExpandedDealWorkflowStepFlags.PossessionTiming) != 0, "possession timing");
            AddIf(labels, (steps & ExpandedDealWorkflowStepFlags.InventoryHandoff) != 0, "inventory handoff");
            AddIf(labels, (steps & ExpandedDealWorkflowStepFlags.Documentation) != 0, "documentation / clarification");
            AddIf(labels, (steps & ExpandedDealWorkflowStepFlags.ClosingCare) != 0, "closing care");
            AddIf(labels, (steps & ExpandedDealWorkflowStepFlags.GeneralReview) != 0, "general review");
            return labels.Count > 0 ? string.Join(", ", labels) : "none";
        }

        private static string BuildTagSummary(ExpandedDealTagFlags flags)
        {
            List<string> labels = new List<string>(8);
            AddIf(labels, (flags & ExpandedDealTagFlags.StandardTransfer) != 0, "standard transfer");
            AddIf(labels, (flags & ExpandedDealTagFlags.SpecialStructure) != 0, "special structure");
            AddIf(labels, (flags & ExpandedDealTagFlags.DocumentationReview) != 0, "documentation review");
            AddIf(labels, (flags & ExpandedDealTagFlags.ClarificationReview) != 0, "clarification review");
            AddIf(labels, (flags & ExpandedDealTagFlags.FinancingReview) != 0, "financing review");
            AddIf(labels, (flags & ExpandedDealTagFlags.PossessionReview) != 0, "possession review");
            AddIf(labels, (flags & ExpandedDealTagFlags.LandControlReview) != 0, "land-control review");
            AddIf(labels, (flags & ExpandedDealTagFlags.InventoryReview) != 0, "inventory review");
            return labels.Count > 0 ? string.Join(", ", labels) : "none";
        }

        private static void AddIf(List<string> parts, bool condition, string value)
        {
            if (condition && !string.IsNullOrEmpty(value) && !parts.Contains(value)) parts.Add(value);
        }
    }

    [Serializable]
    public struct ExpandedDealTermEvaluationContext
    {
        public int purchasePriceCents;
        public int estimatedAssetValueCents;
        public int buyerAvailableCashCents;
        public float sellerPressure01;
        public float sellerCashNeed01;
        public float sellerAttachment01;
        public float sellerRelationshipSensitivity01;
        public float buyerReputation01;
        public float buyerCashCertainty01;

        public ExpandedDealTermEvaluationContext Sanitized()
        {
            ExpandedDealTermEvaluationContext sanitized = this;
            sanitized.purchasePriceCents = Mathf.Max(0, sanitized.purchasePriceCents);
            sanitized.estimatedAssetValueCents = Mathf.Max(0, sanitized.estimatedAssetValueCents);
            sanitized.buyerAvailableCashCents = Mathf.Max(0, sanitized.buyerAvailableCashCents);
            sanitized.sellerPressure01 = Mathf.Clamp01(sanitized.sellerPressure01);
            sanitized.sellerCashNeed01 = Mathf.Clamp01(sanitized.sellerCashNeed01);
            sanitized.sellerAttachment01 = Mathf.Clamp01(sanitized.sellerAttachment01);
            sanitized.sellerRelationshipSensitivity01 = Mathf.Clamp01(sanitized.sellerRelationshipSensitivity01);
            sanitized.buyerReputation01 = Mathf.Clamp01(sanitized.buyerReputation01);
            sanitized.buyerCashCertainty01 = Mathf.Clamp01(sanitized.buyerCashCertainty01);
            return sanitized;
        }
    }

    [Serializable]
    public sealed class ExpandedDealTermEvaluationResult
    {
        public ExpandedDealTermEvaluationResult(float dealAttractiveness01, float risk01, float sellerWillingnessDelta, float buyerBurden01, int effectiveValueDeltaCents, int thirdPartyFinancingNeedCents, IReadOnlyList<ExpandedDealTermReasonCode> reasonCodes)
        {
            DealAttractiveness01 = Mathf.Clamp01(dealAttractiveness01);
            Risk01 = Mathf.Clamp01(risk01);
            SellerWillingnessDelta = Mathf.Clamp(sellerWillingnessDelta, -0.5f, 0.5f);
            BuyerBurden01 = Mathf.Clamp01(buyerBurden01);
            EffectiveValueDeltaCents = effectiveValueDeltaCents;
            ThirdPartyFinancingNeedCents = Mathf.Max(0, thirdPartyFinancingNeedCents);
            ReasonCodes = reasonCodes ?? Array.Empty<ExpandedDealTermReasonCode>();
        }

        public float DealAttractiveness01 { get; }
        public float Risk01 { get; }
        public float SellerWillingnessDelta { get; }
        public float BuyerBurden01 { get; }
        public int EffectiveValueDeltaCents { get; }
        public int ThirdPartyFinancingNeedCents { get; }
        public IReadOnlyList<ExpandedDealTermReasonCode> ReasonCodes { get; }

        public ExpandedDealTermReasonCode PrimaryConcernCode => GetPrimaryConcernCode(ReasonCodes);
        public ExpandedDealInspectionFocus PrimaryInspectionFocus => GetPrimaryInspectionFocus(PrimaryConcernCode);
        public ExpandedDealSeverityBand RiskBand => GetSeverityBand(Risk01);
        public ExpandedDealSeverityBand BuyerBurdenBand => GetSeverityBand(BuyerBurden01);
        public bool NeedsEnhancedDiligence => Risk01 >= 0.35f || IsEnhancedDiligenceConcern(PrimaryConcernCode);
        public bool NeedsClosingCare => RiskBand >= ExpandedDealSeverityBand.High || BuyerBurdenBand >= ExpandedDealSeverityBand.High;
        public ExpandedDealReadinessClass ReadinessClass => NeedsClosingCare ? ExpandedDealReadinessClass.ClosingFragile : NeedsEnhancedDiligence ? ExpandedDealReadinessClass.ReviewClosely : ExpandedDealReadinessClass.Straightforward;
        public ExpandedDealTagFlags TagFlags => GetTagFlags();
        public ExpandedDealWorkflowStepFlags WorkflowStepFlags => ExpandedDealTerms.BuildWorkflowStepFlags(TagFlags);
        public bool RequiresStructuredReviewPath => (TagFlags & (ExpandedDealTagFlags.DocumentationReview | ExpandedDealTagFlags.ClarificationReview | ExpandedDealTagFlags.FinancingReview | ExpandedDealTagFlags.PossessionReview | ExpandedDealTagFlags.LandControlReview | ExpandedDealTagFlags.InventoryReview)) != 0;
        public bool NeedsFormalWorkflow => NeedsClosingCare || RequiresStructuredReviewPath;
        public float CloseabilityCashPressure01 => Mathf.Clamp01(BuyerBurden01 + (ThirdPartyFinancingNeedCents > 0 ? 0.1f : 0f));
        public float CloseabilityStructurePressure01 => BuildStructureCloseabilityPressure01();
        public float CloseabilityDocumentationPressure01 => HasAnyReason(ExpandedDealTermReasonCode.DocumentationWeak) ? Mathf.Clamp01(0.35f + Risk01 * 0.5f) : 0f;
        public float CloseabilityHandoffPressure01 => BuildHandoffCloseabilityPressure01();
        public float CloseabilityOverallPressure01 => Mathf.Clamp01(Mathf.Max(Mathf.Max(CloseabilityCashPressure01, CloseabilityStructurePressure01), Mathf.Max(CloseabilityDocumentationPressure01, CloseabilityHandoffPressure01)));
        public string CloseabilityBurdenSummary => $"cash {GetSeverityLabel(CloseabilityCashPressure01)}, structure {GetSeverityLabel(CloseabilityStructurePressure01)}, documentation {GetSeverityLabel(CloseabilityDocumentationPressure01)}, handoff {GetSeverityLabel(CloseabilityHandoffPressure01)}";
        public string TagSummary => BuildTagSummary(TagFlags);
        public string WorkflowStepSummary => ExpandedDealTerms.BuildWorkflowStepSummary(WorkflowStepFlags);
        public string InspectionHeadline => PrimaryConcernCode == ExpandedDealTermReasonCode.None ? (NeedsEnhancedDiligence ? "Elevated diligence recommended" : "Clean structure") : $"{GetReasonLabel(PrimaryConcernCode)} ({GetInspectionFocusLabel(PrimaryInspectionFocus)})";
        public string DiligenceSummary => NeedsEnhancedDiligence && NeedsClosingCare ? "Enhanced diligence and closing-care review" : NeedsEnhancedDiligence ? "Enhanced diligence" : "Routine diligence";
        public string FragilitySummary => ReadinessClass switch { ExpandedDealReadinessClass.Straightforward => "Straightforward structure", ExpandedDealReadinessClass.ReviewClosely => "Review closely before commit", ExpandedDealReadinessClass.ClosingFragile => "Closing-fragile structure", _ => "Unknown readiness" };
        public string ReviewFirstSummary => BuildReviewFirstSummary();
        public string WorkflowRecommendation => NeedsClosingCare ? "Use formal workflow before commit" : (TagFlags & ExpandedDealTagFlags.ClarificationReview) != 0 ? "Use formal workflow and clarify terms" : RequiresStructuredReviewPath ? "Use formal workflow review" : NeedsEnhancedDiligence ? "Tab review acceptable; flag enhanced diligence" : "Tab-safe overview";
        public string InspectionSummaryBlock => string.Join("\n", new[] { $"Headline: {InspectionHeadline}", $"Diligence: {DiligenceSummary}", $"Readiness: {FragilitySummary}", $"Workflow: {WorkflowRecommendation}", $"Workflow steps: {WorkflowStepSummary}", $"Review first: {ReviewFirstSummary}", $"Closeability: {CloseabilityBurdenSummary}", $"Tags: {TagSummary}" });

        private ExpandedDealTagFlags GetTagFlags()
        {
            ExpandedDealTagFlags flags = HasSpecialStructureConcern(ReasonCodes) ? ExpandedDealTagFlags.SpecialStructure : ExpandedDealTagFlags.StandardTransfer;
            flags |= NeedsEnhancedDiligence ? ExpandedDealTagFlags.EnhancedDiligence : ExpandedDealTagFlags.RoutineDiligence;
            if (NeedsClosingCare) flags |= ExpandedDealTagFlags.ClosingCare;
            if (HasAnyReason(ExpandedDealTermReasonCode.DocumentationWeak)) flags |= ExpandedDealTagFlags.DocumentationReview;
            if (HasAnyReason(ExpandedDealTermReasonCode.InventoryScopeUnclear, ExpandedDealTermReasonCode.PossessionWindowUnclear, ExpandedDealTermReasonCode.LeaseEconomicsUnclear, ExpandedDealTermReasonCode.FinancingStructureUnclear)) flags |= ExpandedDealTagFlags.ClarificationReview;
            if (HasAnyReason(ExpandedDealTermReasonCode.SellerFinancingRequested, ExpandedDealTermReasonCode.SellerFinancingOffered, ExpandedDealTermReasonCode.SellerCashNeedConflict, ExpandedDealTermReasonCode.BalloonPaymentRisk, ExpandedDealTermReasonCode.FinancingStructureUnclear)) flags |= ExpandedDealTagFlags.FinancingReview;
            if (HasAnyReason(ExpandedDealTermReasonCode.DelayedPossession, ExpandedDealTermReasonCode.DelayedPossessionBurden, ExpandedDealTermReasonCode.PossessionWindowUnclear, ExpandedDealTermReasonCode.LongPossessionDelay, ExpandedDealTermReasonCode.SellerPossessionBenefit)) flags |= ExpandedDealTagFlags.PossessionReview;
            if (HasAnyReason(ExpandedDealTermReasonCode.RetainedLandLease, ExpandedDealTermReasonCode.BusinessOnlyTransfer, ExpandedDealTermReasonCode.RetainedLandControlRisk, ExpandedDealTermReasonCode.LeaseEconomicsUnclear, ExpandedDealTermReasonCode.ShortLeaseTerm, ExpandedDealTermReasonCode.WeakRenewalSecurity, ExpandedDealTermReasonCode.HeavySellerControl)) flags |= ExpandedDealTagFlags.LandControlReview;
            if (HasAnyReason(ExpandedDealTermReasonCode.InventoryIncluded, ExpandedDealTermReasonCode.InventoryExcluded, ExpandedDealTermReasonCode.PartialInventoryTransfer, ExpandedDealTermReasonCode.InventoryRestartBurden, ExpandedDealTermReasonCode.InventoryScopeUnclear)) flags |= ExpandedDealTagFlags.InventoryReview;
            return flags;
        }

        private float BuildStructureCloseabilityPressure01()
        {
            float pressure = 0f;
            if ((TagFlags & ExpandedDealTagFlags.FinancingReview) != 0) pressure += 0.18f;
            if ((TagFlags & ExpandedDealTagFlags.LandControlReview) != 0) pressure += 0.18f;
            if ((TagFlags & ExpandedDealTagFlags.ClarificationReview) != 0) pressure += 0.16f;
            if (NeedsClosingCare) pressure += 0.12f;
            return Mathf.Clamp01(pressure + Risk01 * 0.35f);
        }

        private float BuildHandoffCloseabilityPressure01()
        {
            float pressure = 0f;
            if ((TagFlags & ExpandedDealTagFlags.PossessionReview) != 0) pressure += 0.2f;
            if ((TagFlags & ExpandedDealTagFlags.InventoryReview) != 0) pressure += 0.16f;
            return Mathf.Clamp01(pressure + BuyerBurden01 * 0.25f);
        }

        private string BuildReviewFirstSummary()
        {
            if (PrimaryConcernCode == ExpandedDealTermReasonCode.None) return "No special review focus";
            return PrimaryInspectionFocus switch
            {
                ExpandedDealInspectionFocus.Documentation => "Review paperwork quality first",
                ExpandedDealInspectionFocus.Financing => "Review financing terms first",
                ExpandedDealInspectionFocus.Inventory => "Review inventory handoff first",
                ExpandedDealInspectionFocus.Possession => "Review possession timing first",
                ExpandedDealInspectionFocus.LandControl => "Review land-control terms first",
                _ => "Review overall deal risk first"
            };
        }

        private bool HasReason(ExpandedDealTermReasonCode reasonCode)
        {
            for (int i = 0; i < ReasonCodes.Count; i++) if (ReasonCodes[i] == reasonCode) return true;
            return false;
        }

        private bool HasAnyReason(params ExpandedDealTermReasonCode[] reasonCodes)
        {
            if (reasonCodes == null) return false;
            for (int i = 0; i < reasonCodes.Length; i++) if (HasReason(reasonCodes[i])) return true;
            return false;
        }

        private static bool HasSpecialStructureConcern(IReadOnlyList<ExpandedDealTermReasonCode> reasonCodes)
        {
            if (reasonCodes == null) return false;
            for (int i = 0; i < reasonCodes.Count; i++)
            {
                switch (reasonCodes[i])
                {
                    case ExpandedDealTermReasonCode.RetainedLandLease:
                    case ExpandedDealTermReasonCode.BusinessOnlyTransfer:
                    case ExpandedDealTermReasonCode.DelayedPossession:
                    case ExpandedDealTermReasonCode.InventoryExcluded:
                    case ExpandedDealTermReasonCode.PartialInventoryTransfer:
                    case ExpandedDealTermReasonCode.SellerFinancingRequested:
                    case ExpandedDealTermReasonCode.SellerFinancingOffered:
                    case ExpandedDealTermReasonCode.InventoryScopeUnclear:
                    case ExpandedDealTermReasonCode.PossessionWindowUnclear:
                    case ExpandedDealTermReasonCode.LeaseEconomicsUnclear:
                    case ExpandedDealTermReasonCode.FinancingStructureUnclear:
                        return true;
                }
            }
            return false;
        }

        private static ExpandedDealTermReasonCode GetPrimaryConcernCode(IReadOnlyList<ExpandedDealTermReasonCode> reasonCodes)
        {
            if (reasonCodes == null || reasonCodes.Count == 0) return ExpandedDealTermReasonCode.None;
            ExpandedDealTermReasonCode[] priority =
            {
                ExpandedDealTermReasonCode.DocumentationWeak,
                ExpandedDealTermReasonCode.FinancingStructureUnclear,
                ExpandedDealTermReasonCode.InventoryScopeUnclear,
                ExpandedDealTermReasonCode.PossessionWindowUnclear,
                ExpandedDealTermReasonCode.LeaseEconomicsUnclear,
                ExpandedDealTermReasonCode.BalloonPaymentRisk,
                ExpandedDealTermReasonCode.ShortLeaseTerm,
                ExpandedDealTermReasonCode.WeakRenewalSecurity,
                ExpandedDealTermReasonCode.HeavySellerControl,
                ExpandedDealTermReasonCode.LongPossessionDelay,
                ExpandedDealTermReasonCode.RetainedLandControlRisk,
                ExpandedDealTermReasonCode.InventoryRestartBurden,
                ExpandedDealTermReasonCode.DelayedPossessionBurden,
                ExpandedDealTermReasonCode.SellerCashNeedConflict,
                ExpandedDealTermReasonCode.BuyerBurdenRaised,
                ExpandedDealTermReasonCode.RiskRaised
            };
            for (int i = 0; i < priority.Length; i++)
            {
                for (int j = 0; j < reasonCodes.Count; j++)
                {
                    if (reasonCodes[j] == priority[i]) return priority[i];
                }
            }
            return reasonCodes[0];
        }

        private static ExpandedDealInspectionFocus GetPrimaryInspectionFocus(ExpandedDealTermReasonCode reasonCode)
        {
            return reasonCode switch
            {
                ExpandedDealTermReasonCode.DocumentationWeak => ExpandedDealInspectionFocus.Documentation,
                ExpandedDealTermReasonCode.FinancingStructureUnclear => ExpandedDealInspectionFocus.Financing,
                ExpandedDealTermReasonCode.BalloonPaymentRisk => ExpandedDealInspectionFocus.Financing,
                ExpandedDealTermReasonCode.SellerFinancingRequested => ExpandedDealInspectionFocus.Financing,
                ExpandedDealTermReasonCode.SellerCashNeedConflict => ExpandedDealInspectionFocus.Financing,
                ExpandedDealTermReasonCode.InventoryScopeUnclear => ExpandedDealInspectionFocus.Inventory,
                ExpandedDealTermReasonCode.InventoryRestartBurden => ExpandedDealInspectionFocus.Inventory,
                ExpandedDealTermReasonCode.PartialInventoryTransfer => ExpandedDealInspectionFocus.Inventory,
                ExpandedDealTermReasonCode.InventoryExcluded => ExpandedDealInspectionFocus.Inventory,
                ExpandedDealTermReasonCode.PossessionWindowUnclear => ExpandedDealInspectionFocus.Possession,
                ExpandedDealTermReasonCode.LongPossessionDelay => ExpandedDealInspectionFocus.Possession,
                ExpandedDealTermReasonCode.DelayedPossessionBurden => ExpandedDealInspectionFocus.Possession,
                ExpandedDealTermReasonCode.DelayedPossession => ExpandedDealInspectionFocus.Possession,
                ExpandedDealTermReasonCode.LeaseEconomicsUnclear => ExpandedDealInspectionFocus.LandControl,
                ExpandedDealTermReasonCode.ShortLeaseTerm => ExpandedDealInspectionFocus.LandControl,
                ExpandedDealTermReasonCode.WeakRenewalSecurity => ExpandedDealInspectionFocus.LandControl,
                ExpandedDealTermReasonCode.HeavySellerControl => ExpandedDealInspectionFocus.LandControl,
                ExpandedDealTermReasonCode.RetainedLandControlRisk => ExpandedDealInspectionFocus.LandControl,
                ExpandedDealTermReasonCode.RetainedLandLease => ExpandedDealInspectionFocus.LandControl,
                ExpandedDealTermReasonCode.BusinessOnlyTransfer => ExpandedDealInspectionFocus.LandControl,
                _ => ExpandedDealInspectionFocus.GeneralRisk
            };
        }

        private static string GetReasonLabel(ExpandedDealTermReasonCode reasonCode)
        {
            return reasonCode switch
            {
                ExpandedDealTermReasonCode.None => "No primary concern",
                ExpandedDealTermReasonCode.FinancingStructureUnclear => "Seller financing unclear",
                ExpandedDealTermReasonCode.InventoryScopeUnclear => "Inventory scope unclear",
                ExpandedDealTermReasonCode.PossessionWindowUnclear => "Possession window unclear",
                ExpandedDealTermReasonCode.LeaseEconomicsUnclear => "Lease economics unclear",
                ExpandedDealTermReasonCode.DocumentationWeak => "Documentation weak",
                ExpandedDealTermReasonCode.BalloonPaymentRisk => "Balloon payment risk",
                ExpandedDealTermReasonCode.ShortLeaseTerm => "Short lease term",
                ExpandedDealTermReasonCode.WeakRenewalSecurity => "Weak renewal security",
                ExpandedDealTermReasonCode.HeavySellerControl => "Heavy seller control",
                ExpandedDealTermReasonCode.LongPossessionDelay => "Long possession delay",
                ExpandedDealTermReasonCode.RetainedLandControlRisk => "Retained-land control risk",
                ExpandedDealTermReasonCode.InventoryRestartBurden => "Inventory restart burden",
                ExpandedDealTermReasonCode.DelayedPossessionBurden => "Delayed-possession burden",
                ExpandedDealTermReasonCode.SellerCashNeedConflict => "Seller cash-need conflict",
                ExpandedDealTermReasonCode.BusinessOnlyTransfer => "Business-only transfer",
                ExpandedDealTermReasonCode.RetainedLandLease => "Retained-land lease",
                ExpandedDealTermReasonCode.SellerFinancingRequested => "Seller financing requested",
                ExpandedDealTermReasonCode.SellerFinancingOffered => "Seller financing offered",
                ExpandedDealTermReasonCode.InventoryExcluded => "Inventory excluded",
                ExpandedDealTermReasonCode.PartialInventoryTransfer => "Partial inventory transfer",
                ExpandedDealTermReasonCode.DelayedPossession => "Delayed possession",
                ExpandedDealTermReasonCode.InventoryIncluded => "Inventory included",
                _ => "Deal concern"
            };
        }

        private static string GetInspectionFocusLabel(ExpandedDealInspectionFocus inspectionFocus)
        {
            return inspectionFocus switch
            {
                ExpandedDealInspectionFocus.Documentation => "Documentation",
                ExpandedDealInspectionFocus.Financing => "Financing",
                ExpandedDealInspectionFocus.Inventory => "Inventory",
                ExpandedDealInspectionFocus.Possession => "Possession",
                ExpandedDealInspectionFocus.LandControl => "Land control",
                ExpandedDealInspectionFocus.GeneralRisk => "General risk",
                _ => "No focus"
            };
        }

        private static ExpandedDealSeverityBand GetSeverityBand(float value01)
        {
            float clamped = Mathf.Clamp01(value01);
            if (clamped >= 0.75f) return ExpandedDealSeverityBand.Severe;
            if (clamped >= 0.5f) return ExpandedDealSeverityBand.High;
            if (clamped >= 0.25f) return ExpandedDealSeverityBand.Moderate;
            return ExpandedDealSeverityBand.Low;
        }

        private static string GetSeverityLabel(float value01)
        {
            return GetSeverityBand(value01) switch
            {
                ExpandedDealSeverityBand.Severe => "severe",
                ExpandedDealSeverityBand.High => "high",
                ExpandedDealSeverityBand.Moderate => "moderate",
                _ => "low"
            };
        }

        private static bool IsEnhancedDiligenceConcern(ExpandedDealTermReasonCode reasonCode)
        {
            return reasonCode == ExpandedDealTermReasonCode.DocumentationWeak
                || reasonCode == ExpandedDealTermReasonCode.FinancingStructureUnclear
                || reasonCode == ExpandedDealTermReasonCode.InventoryScopeUnclear
                || reasonCode == ExpandedDealTermReasonCode.PossessionWindowUnclear
                || reasonCode == ExpandedDealTermReasonCode.LeaseEconomicsUnclear
                || reasonCode == ExpandedDealTermReasonCode.BalloonPaymentRisk
                || reasonCode == ExpandedDealTermReasonCode.ShortLeaseTerm
                || reasonCode == ExpandedDealTermReasonCode.WeakRenewalSecurity
                || reasonCode == ExpandedDealTermReasonCode.HeavySellerControl
                || reasonCode == ExpandedDealTermReasonCode.LongPossessionDelay
                || reasonCode == ExpandedDealTermReasonCode.RetainedLandControlRisk
                || reasonCode == ExpandedDealTermReasonCode.InventoryRestartBurden
                || reasonCode == ExpandedDealTermReasonCode.DelayedPossessionBurden
                || reasonCode == ExpandedDealTermReasonCode.BusinessOnlyTransfer;
        }

        private static string BuildTagSummary(ExpandedDealTagFlags flags)
        {
            List<string> labels = new List<string>(8);
            AddIf(labels, (flags & ExpandedDealTagFlags.StandardTransfer) != 0, "standard transfer");
            AddIf(labels, (flags & ExpandedDealTagFlags.SpecialStructure) != 0, "special structure");
            AddIf(labels, (flags & ExpandedDealTagFlags.RoutineDiligence) != 0, "routine diligence");
            AddIf(labels, (flags & ExpandedDealTagFlags.EnhancedDiligence) != 0, "enhanced diligence");
            AddIf(labels, (flags & ExpandedDealTagFlags.ClosingCare) != 0, "closing care");
            AddIf(labels, (flags & ExpandedDealTagFlags.DocumentationReview) != 0, "documentation review");
            AddIf(labels, (flags & ExpandedDealTagFlags.ClarificationReview) != 0, "clarification review");
            AddIf(labels, (flags & ExpandedDealTagFlags.FinancingReview) != 0, "financing review");
            AddIf(labels, (flags & ExpandedDealTagFlags.PossessionReview) != 0, "possession review");
            AddIf(labels, (flags & ExpandedDealTagFlags.LandControlReview) != 0, "land-control review");
            AddIf(labels, (flags & ExpandedDealTagFlags.InventoryReview) != 0, "inventory review");
            return labels.Count > 0 ? string.Join(", ", labels) : "none";
        }

        private static void AddIf(List<string> parts, bool condition, string value)
        {
            if (condition && !string.IsNullOrEmpty(value) && !parts.Contains(value)) parts.Add(value);
        }
    }
}
