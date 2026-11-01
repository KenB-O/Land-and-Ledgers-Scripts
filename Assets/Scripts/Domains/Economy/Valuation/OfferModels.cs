using System;
using System.Collections.Generic;
using LandLedgers.Economy.DealTerms;
using UnityEngine;

namespace LandLedgers.Economy.Valuation
{
    public enum SellerMotive
    {
        Holding = 0,
        TestingMarket = 1,
        Retirement = 2,
        Relocation = 3,
        DebtPressure = 4,
        FinancialDistress = 5,
        EstateSale = 6,
        StrategicHoldout = 7,
        OwnerOperatorAttachment = 8,
        Liquidating = 9
    }

    public enum BuyerKind
    {
        Unknown = 0,
        Player = 1,
        LocalOperator = 2,
        OutsideInvestor = 3,
        Rival = 4,
        Civic = 5
    }

    public enum OfferDecision
    {
        Reject = 0,
        Counter = 1,
        Accept = 2
    }

    public enum OfferEvaluationReasonCode
    {
        None = 0,
        PriceMeetsThreshold = 1,
        CleanTerms = 2,
        TrustedBuyer = 3,
        StrongBuyerFit = 4,
        SellerUnderPressure = 5,
        ExpandedTermsHelpful = 6,
        TooLow = 100,
        BuyerDistrusted = 101,
        TermsTooRisky = 102,
        NotReadyToSell = 103,
        BetterUsePotential = 104,
        SellerAttached = 105,
        FinancingContingencyConcern = 106,
        InventoryExcludedConcern = 107,
        DelayedPossessionConcern = 108,
        RetainedLandLeaseConcern = 109,
        ExpandedSellerFinancingConcern = 110,
        BuyerClosingCertaintyConcern = 111,
        BuyerContinuityConcern = 112,
        BuyerProfileThinConcern = 113,
        BuyerStrategicThreatConcern = 114,
        CounterPriceRecommended = 200
    }

    [Serializable]
    public struct SellerProfile
    {
        public string sellerId;
        public SellerMotive motive;
        public float pressure01;
        public float urgency01;
        public float debtPressure01;
        public float cashNeed01;
        public float attachment01;
        public float relocationNeed01;
        public float reputationSensitivity01;
        public float buyerPreferenceSensitivity01;
        public bool listedForSale;
        public float minimumAcceptableRatio;
        public float counterofferFlexibility01;

        public bool HasExplicitPressure => pressure01 >= 0f;
        public bool HasExplicitUrgency => urgency01 >= 0f;
        public bool HasExplicitDebtPressure => debtPressure01 >= 0f;
        public bool HasExplicitCashNeed => cashNeed01 >= 0f;
        public bool HasExplicitAttachment => attachment01 >= 0f;
        public bool HasExplicitRelocationNeed => relocationNeed01 >= 0f;
        public bool HasExplicitReputationSensitivity => reputationSensitivity01 >= 0f;
        public bool HasExplicitBuyerPreferenceSensitivity => buyerPreferenceSensitivity01 >= 0f;
        public bool HasExplicitMinimumAcceptableRatio => minimumAcceptableRatio > 0f;
        public bool HasExplicitCounterofferFlexibility => counterofferFlexibility01 >= 0f;

        public SellerProfile Sanitized()
        {
            SellerProfile sanitized = this;
            sanitized.sellerId = sellerId ?? string.Empty;
            // Negative 01 fields mean "unknown / not authored yet" so the evaluators
            // can fall back to cautious midpoint defaults instead of treating absence as
            // the same thing as an explicit hard zero.
            sanitized.pressure01 = ClampOptional01(pressure01);
            sanitized.urgency01 = ClampOptional01(urgency01);
            sanitized.debtPressure01 = ClampOptional01(debtPressure01);
            sanitized.cashNeed01 = ClampOptional01(cashNeed01);
            sanitized.attachment01 = ClampOptional01(attachment01);
            sanitized.relocationNeed01 = ClampOptional01(relocationNeed01);
            sanitized.reputationSensitivity01 = ClampOptional01(reputationSensitivity01);
            sanitized.buyerPreferenceSensitivity01 = ClampOptional01(buyerPreferenceSensitivity01);
            sanitized.counterofferFlexibility01 = ClampOptional01(counterofferFlexibility01);
            sanitized.minimumAcceptableRatio = minimumAcceptableRatio <= 0f ? 0f : Mathf.Max(0.01f, minimumAcceptableRatio);
            return sanitized;
        }

        private static float ClampOptional01(float value)
        {
            return value < 0f ? -1f : Mathf.Clamp01(value);
        }
    }

    [Serializable]
    public struct SellerPressureResult
    {
        public SellerPressureResult(
            float pressure01,
            float willingnessToSell01,
            float reservationValueMultiplier,
            float relationshipSensitivity01,
            float counterofferFlexibility01,
            ScorecardRow scorecardRow,
            IReadOnlyList<OfferEvaluationReasonCode> reasonCodes)
        {
            Pressure01 = Mathf.Clamp01(pressure01);
            WillingnessToSell01 = Mathf.Clamp01(willingnessToSell01);
            ReservationValueMultiplier = Mathf.Clamp(reservationValueMultiplier, 0.5f, 1.75f);
            RelationshipSensitivity01 = Mathf.Clamp01(relationshipSensitivity01);
            CounterofferFlexibility01 = Mathf.Clamp01(counterofferFlexibility01);
            ScorecardRow = scorecardRow;
            ReasonCodes = reasonCodes ?? Array.Empty<OfferEvaluationReasonCode>();
        }

        public float Pressure01 { get; }
        public float WillingnessToSell01 { get; }
        public float ReservationValueMultiplier { get; }
        public float RelationshipSensitivity01 { get; }
        public float CounterofferFlexibility01 { get; }
        public ScorecardRow ScorecardRow { get; }
        public IReadOnlyList<OfferEvaluationReasonCode> ReasonCodes { get; }
    }

    [Serializable]
    public struct BuyerOfferProfile
    {
        public string buyerId;
        public BuyerKind buyerKind;
        public float reputation01;
        public float localTrust01;
        public float priorDealReliability01;
        public float strategicThreat01;
        public float communityFit01;
        public float cashCertainty01;
        public float relationshipWithSeller01;

        public bool HasExplicitReputation => reputation01 >= 0f;
        public bool HasExplicitLocalTrust => localTrust01 >= 0f;
        public bool HasExplicitPriorDealReliability => priorDealReliability01 >= 0f;
        public bool HasExplicitStrategicThreat => strategicThreat01 >= 0f;
        public bool HasExplicitCommunityFit => communityFit01 >= 0f;
        public bool HasExplicitCashCertainty => cashCertainty01 >= 0f;
        public bool HasExplicitRelationshipWithSeller => relationshipWithSeller01 >= 0f;

        public BuyerOfferProfile Sanitized()
        {
            BuyerOfferProfile sanitized = this;
            sanitized.buyerId = buyerId ?? string.Empty;
            sanitized.reputation01 = ClampOptional01(reputation01);
            sanitized.localTrust01 = ClampOptional01(localTrust01);
            sanitized.priorDealReliability01 = ClampOptional01(priorDealReliability01);
            sanitized.strategicThreat01 = ClampOptional01(strategicThreat01);
            sanitized.communityFit01 = ClampOptional01(communityFit01);
            sanitized.cashCertainty01 = ClampOptional01(cashCertainty01);
            sanitized.relationshipWithSeller01 = ClampOptional01(relationshipWithSeller01);
            return sanitized;
        }

        private static float ClampOptional01(float value)
        {
            return value < 0f ? -1f : Mathf.Clamp01(value);
        }
    }


    public enum BuyerFitBand
    {
        VeryWeak = 0,
        Cautious = 1,
        Workable = 2,
        Strong = 3
    }

    public enum BuyerFitLeadSignal
    {
        None = 0,
        Trust = 1,
        ClosingCertainty = 2,
        Continuity = 3
    }

    public enum BuyerFitPrimaryConcern
    {
        None = 0,
        LowTrust = 1,
        LowClosingCertainty = 2,
        WeakContinuity = 3,
        StrategicThreat = 4,
        ThinProfile = 5
    }

    [Serializable]
    public struct BuyerSellerFitResult
    {
        public BuyerSellerFitResult(float score01, IReadOnlyList<OfferEvaluationReasonCode> reasonCodes)
            : this(
                score01,
                score01,
                score01,
                score01,
                0f,
                0f,
                false,
                ResolveBand(score01),
                BuyerFitLeadSignal.None,
                BuyerFitPrimaryConcern.None,
                ResolveFallbackSummary(ResolveBand(score01)),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                reasonCodes)
        {
        }

        public BuyerSellerFitResult(
            float score01,
            float trustScore01,
            float certaintyScore01,
            float continuityScore01,
            float threatPenalty01,
            float inputCoverage01,
            IReadOnlyList<OfferEvaluationReasonCode> reasonCodes)
            : this(
                score01,
                trustScore01,
                certaintyScore01,
                continuityScore01,
                threatPenalty01,
                inputCoverage01,
                true,
                ResolveBand(score01),
                BuyerFitLeadSignal.None,
                BuyerFitPrimaryConcern.None,
                ResolveFallbackSummary(ResolveBand(score01)),
                ResolveFallbackSignalSummary(trustScore01),
                ResolveFallbackSignalSummary(certaintyScore01),
                ResolveFallbackSignalSummary(continuityScore01),
                ResolveFallbackThreatSummary(threatPenalty01),
                reasonCodes)
        {
        }

        public BuyerSellerFitResult(
            float score01,
            float trustScore01,
            float certaintyScore01,
            float continuityScore01,
            float threatPenalty01,
            float inputCoverage01,
            BuyerFitBand fitBand,
            BuyerFitLeadSignal primarySupportSignal,
            BuyerFitPrimaryConcern primaryConcern,
            string summary,
            string trustSummary,
            string certaintySummary,
            string continuitySummary,
            string threatSummary,
            IReadOnlyList<OfferEvaluationReasonCode> reasonCodes)
            : this(
                score01,
                trustScore01,
                certaintyScore01,
                continuityScore01,
                threatPenalty01,
                inputCoverage01,
                true,
                fitBand,
                primarySupportSignal,
                primaryConcern,
                summary,
                trustSummary,
                certaintySummary,
                continuitySummary,
                threatSummary,
                reasonCodes)
        {
        }

        private BuyerSellerFitResult(
            float score01,
            float trustScore01,
            float certaintyScore01,
            float continuityScore01,
            float threatPenalty01,
            float inputCoverage01,
            bool hasDetailedSignals,
            BuyerFitBand fitBand,
            BuyerFitLeadSignal primarySupportSignal,
            BuyerFitPrimaryConcern primaryConcern,
            string summary,
            string trustSummary,
            string certaintySummary,
            string continuitySummary,
            string threatSummary,
            IReadOnlyList<OfferEvaluationReasonCode> reasonCodes)
        {
            Score01 = Mathf.Clamp01(score01);
            TrustScore01 = Mathf.Clamp01(trustScore01);
            CertaintyScore01 = Mathf.Clamp01(certaintyScore01);
            ContinuityScore01 = Mathf.Clamp01(continuityScore01);
            ThreatPenalty01 = Mathf.Clamp01(threatPenalty01);
            InputCoverage01 = Mathf.Clamp01(inputCoverage01);
            HasDetailedSignals = hasDetailedSignals;
            FitBand = fitBand;
            PrimarySupportSignal = primarySupportSignal;
            PrimaryConcern = primaryConcern;
            Summary = summary ?? string.Empty;
            TrustSummary = trustSummary ?? string.Empty;
            CertaintySummary = certaintySummary ?? string.Empty;
            ContinuitySummary = continuitySummary ?? string.Empty;
            ThreatSummary = threatSummary ?? string.Empty;
            ReasonCodes = reasonCodes ?? Array.Empty<OfferEvaluationReasonCode>();
        }

        public float Score01 { get; }
        public float TrustScore01 { get; }
        public float CertaintyScore01 { get; }
        public float ContinuityScore01 { get; }
        public float ThreatPenalty01 { get; }
        public float InputCoverage01 { get; }
        // Compatibility path: older call sites may only know the blended fit score. Keep that
        // path working without pretending we have a real trust/certainty breakdown.
        public bool HasDetailedSignals { get; }
        public BuyerFitBand FitBand { get; }
        public BuyerFitLeadSignal PrimarySupportSignal { get; }
        public BuyerFitPrimaryConcern PrimaryConcern { get; }
        public bool UsesProvisionalInputs => HasDetailedSignals && InputCoverage01 < 0.999f;
        public string Summary { get; }
        public string TrustSummary { get; }
        public string CertaintySummary { get; }
        public string ContinuitySummary { get; }
        public string ThreatSummary { get; }
        public IReadOnlyList<OfferEvaluationReasonCode> ReasonCodes { get; }


        // Preserve readable output for older call sites that still use the compact constructors.
        // The richer evaluator path will overwrite these with more specific trust/certainty text.
        private static string ResolveFallbackSummary(BuyerFitBand fitBand)
        {
            return fitBand switch
            {
                BuyerFitBand.Strong => "strong buyer fit",
                BuyerFitBand.Workable => "workable buyer fit",
                BuyerFitBand.Cautious => "cautious buyer fit",
                _ => "weak buyer fit"
            };
        }

        private static string ResolveFallbackSignalSummary(float score01)
        {
            float clamped = Mathf.Clamp01(score01);
            if (clamped >= 0.72f)
            {
                return "strong";
            }

            if (clamped >= 0.5f)
            {
                return "workable";
            }

            if (clamped >= 0.32f)
            {
                return "limited";
            }

            return "weak";
        }

        private static string ResolveFallbackThreatSummary(float penalty01)
        {
            float clamped = Mathf.Clamp01(penalty01);
            if (clamped >= 0.42f)
            {
                return "high";
            }

            if (clamped >= 0.24f)
            {
                return "present";
            }

            return "low";
        }

        private static BuyerFitBand ResolveBand(float score01)
        {
            float clamped = Mathf.Clamp01(score01);
            if (clamped >= 0.72f)
            {
                return BuyerFitBand.Strong;
            }

            if (clamped >= 0.5f)
            {
                return BuyerFitBand.Workable;
            }

            if (clamped >= 0.32f)
            {
                return BuyerFitBand.Cautious;
            }

            return BuyerFitBand.VeryWeak;
        }
    }

    [Serializable]
    public struct AcquisitionOfferTerms
    {
        public int offerPriceCents;
        public int earnestMoneyCents;
        public float closingSpeed01;
        public float contingencyBurden01;
        public float inspectionStrictness01;
        public bool sellerFinancingRequested;
        public float nonPriceConcessions01;
        public ExpandedDealTerms expandedDealTerms;

        public bool HasExplicitClosingSpeed => closingSpeed01 >= 0f;
        public bool HasExplicitContingencyBurden => contingencyBurden01 >= 0f;
        public bool HasExplicitInspectionStrictness => inspectionStrictness01 >= 0f;
        public bool HasExplicitNonPriceConcessions => nonPriceConcessions01 >= 0f;

        public AcquisitionOfferTerms Sanitized()
        {
            AcquisitionOfferTerms sanitized = this;
            sanitized.offerPriceCents = Mathf.Max(0, offerPriceCents);
            sanitized.earnestMoneyCents = Mathf.Max(0, earnestMoneyCents);
            sanitized.closingSpeed01 = ClampOptional01(closingSpeed01);
            sanitized.contingencyBurden01 = ClampOptional01(contingencyBurden01);
            sanitized.inspectionStrictness01 = ClampOptional01(inspectionStrictness01);
            sanitized.nonPriceConcessions01 = ClampOptional01(nonPriceConcessions01);
            return sanitized;
        }

        private static float ClampOptional01(float value)
        {
            return value < 0f ? -1f : Mathf.Clamp01(value);
        }
    }

    [Serializable]
    public sealed class OfferEvaluationResult
    {
        public OfferEvaluationResult(
            OfferDecision decision,
            int pressureAdjustedThresholdCents,
            int counterofferPriceCents,
            float sellerValueScore01,
            float priceStrength01,
            float buyerFit01,
            float termsFit01,
            IReadOnlyList<OfferEvaluationReasonCode> reasonCodes)
            : this(
                decision,
                pressureAdjustedThresholdCents,
                counterofferPriceCents,
                sellerValueScore01,
                priceStrength01,
                buyerFit01,
                termsFit01,
                ResolveBuyerFitBand(buyerFit01),
                BuyerFitLeadSignal.None,
                BuyerFitPrimaryConcern.None,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                reasonCodes)
        {
        }

        public OfferEvaluationResult(
            OfferDecision decision,
            int pressureAdjustedThresholdCents,
            int counterofferPriceCents,
            float sellerValueScore01,
            float priceStrength01,
            float buyerFit01,
            float termsFit01,
            BuyerFitBand buyerFitBand,
            BuyerFitLeadSignal buyerPrimarySupportSignal,
            BuyerFitPrimaryConcern buyerPrimaryConcern,
            string buyerSummary,
            string pricingSummary,
            string decisionSummary,
            IReadOnlyList<OfferEvaluationReasonCode> reasonCodes)
            : this(
                decision,
                pressureAdjustedThresholdCents,
                counterofferPriceCents,
                sellerValueScore01,
                priceStrength01,
                buyerFit01,
                termsFit01,
                buyerFitBand,
                buyerPrimarySupportSignal,
                buyerPrimaryConcern,
                buyerSummary,
                pricingSummary,
                string.Empty,
                decisionSummary,
                reasonCodes)
        {
        }

        public OfferEvaluationResult(
            OfferDecision decision,
            int pressureAdjustedThresholdCents,
            int counterofferPriceCents,
            float sellerValueScore01,
            float priceStrength01,
            float buyerFit01,
            float termsFit01,
            BuyerFitBand buyerFitBand,
            BuyerFitLeadSignal buyerPrimarySupportSignal,
            BuyerFitPrimaryConcern buyerPrimaryConcern,
            string buyerSummary,
            string pricingSummary,
            string termsSummary,
            string decisionSummary,
            IReadOnlyList<OfferEvaluationReasonCode> reasonCodes)
        {
            Decision = decision;
            PressureAdjustedThresholdCents = Mathf.Max(1, pressureAdjustedThresholdCents);
            CounterofferPriceCents = Mathf.Max(0, counterofferPriceCents);
            SellerValueScore01 = Mathf.Clamp01(sellerValueScore01);
            PriceStrength01 = Mathf.Max(0f, priceStrength01);
            BuyerFit01 = Mathf.Clamp01(buyerFit01);
            TermsFit01 = Mathf.Clamp01(termsFit01);
            BuyerFitBand = buyerFitBand;
            BuyerPrimarySupportSignal = buyerPrimarySupportSignal;
            BuyerPrimaryConcern = buyerPrimaryConcern;
            IReadOnlyList<OfferEvaluationReasonCode> safeReasonCodes = reasonCodes ?? Array.Empty<OfferEvaluationReasonCode>();
            // Compatibility path: older constructors/callers may not provide the richer
            // workflow summaries yet. Backfill short honest defaults so downstream surfaces do
            // not end up with blank buyer/pricing/terms/decision text.
            BuyerSummary = string.IsNullOrWhiteSpace(buyerSummary)
                ? ResolveBuyerSummary(buyerFitBand, buyerPrimarySupportSignal, buyerPrimaryConcern)
                : buyerSummary;
            PricingSummary = string.IsNullOrWhiteSpace(pricingSummary)
                ? ResolvePricingSummary(decision, priceStrength01, pressureAdjustedThresholdCents, counterofferPriceCents)
                : pricingSummary;
            TermsSummary = string.IsNullOrWhiteSpace(termsSummary)
                ? ResolveTermsSummary(decision, termsFit01, safeReasonCodes)
                : termsSummary;
            DecisionSummary = string.IsNullOrWhiteSpace(decisionSummary)
                ? ResolveDecisionSummary(decision, buyerPrimaryConcern, termsFit01, priceStrength01)
                : decisionSummary;
            ReasonCodes = safeReasonCodes;
        }

        public OfferDecision Decision { get; }
        public bool Accepted => Decision == OfferDecision.Accept;
        public bool Countered => Decision == OfferDecision.Counter;
        public bool Rejected => Decision == OfferDecision.Reject;
        public int PressureAdjustedThresholdCents { get; }
        public int CounterofferPriceCents { get; }
        public float SellerValueScore01 { get; }
        public float PriceStrength01 { get; }
        public float BuyerFit01 { get; }
        public float TermsFit01 { get; }
        public BuyerFitBand BuyerFitBand { get; }
        public BuyerFitLeadSignal BuyerPrimarySupportSignal { get; }
        public BuyerFitPrimaryConcern BuyerPrimaryConcern { get; }
        public string BuyerSummary { get; }
        public string PricingSummary { get; }
        public string TermsSummary { get; }
        public string DecisionSummary { get; }
        public IReadOnlyList<OfferEvaluationReasonCode> ReasonCodes { get; }

        private static BuyerFitBand ResolveBuyerFitBand(float buyerFit01)
        {
            float clamped = Mathf.Clamp01(buyerFit01);
            if (clamped >= 0.72f)
            {
                return BuyerFitBand.Strong;
            }

            if (clamped >= 0.5f)
            {
                return BuyerFitBand.Workable;
            }

            if (clamped >= 0.32f)
            {
                return BuyerFitBand.Cautious;
            }

            return BuyerFitBand.VeryWeak;
        }

        private static string ResolveBuyerSummary(
            BuyerFitBand buyerFitBand,
            BuyerFitLeadSignal buyerPrimarySupportSignal,
            BuyerFitPrimaryConcern buyerPrimaryConcern)
        {
            if (buyerPrimaryConcern != BuyerFitPrimaryConcern.None)
            {
                return buyerPrimaryConcern switch
                {
                    BuyerFitPrimaryConcern.LowTrust => "buyer fit is held back by low trust",
                    BuyerFitPrimaryConcern.LowClosingCertainty => "buyer fit is held back by thin closing certainty",
                    BuyerFitPrimaryConcern.WeakContinuity => "buyer fit is held back by continuity concerns",
                    BuyerFitPrimaryConcern.StrategicThreat => "buyer fit is held back by strategic threat concerns",
                    BuyerFitPrimaryConcern.ThinProfile => "buyer fit remains provisional because the profile is thin",
                    _ => string.Empty
                };
            }

            if (buyerPrimarySupportSignal != BuyerFitLeadSignal.None)
            {
                return buyerPrimarySupportSignal switch
                {
                    BuyerFitLeadSignal.Trust => "buyer fit is mainly supported by trust",
                    BuyerFitLeadSignal.ClosingCertainty => "buyer fit is mainly supported by closing certainty",
                    BuyerFitLeadSignal.Continuity => "buyer fit is mainly supported by continuity fit",
                    _ => string.Empty
                };
            }

            return buyerFitBand switch
            {
                BuyerFitBand.Strong => "strong buyer fit",
                BuyerFitBand.Workable => "workable buyer fit",
                BuyerFitBand.Cautious => "cautious buyer fit",
                _ => "weak buyer fit"
            };
        }

        private static string ResolvePricingSummary(
            OfferDecision decision,
            float priceStrength01,
            int pressureAdjustedThresholdCents,
            int counterofferPriceCents)
        {
            return decision switch
            {
                OfferDecision.Accept => priceStrength01 >= 1f
                    ? "offer clears the current seller threshold"
                    : "offer is treated as clear enough on current pricing posture",
                OfferDecision.Counter => counterofferPriceCents > 0
                    ? $"seller would likely counter above the current offer price toward {counterofferPriceCents} cents"
                    : "seller would likely counter because price still trails the current threshold",
                _ => priceStrength01 >= 1f
                    ? "price is not the main blocker on the current posture"
                    : $"offer trails the current seller threshold of {pressureAdjustedThresholdCents} cents"
            };
        }

        private static string ResolveTermsSummary(OfferDecision decision, float termsFit01, IReadOnlyList<OfferEvaluationReasonCode> reasonCodes)
        {
            if (ContainsReason(reasonCodes, OfferEvaluationReasonCode.RetainedLandLeaseConcern))
            {
                return "terms carry retained-land or lease-control risk";
            }

            if (ContainsReason(reasonCodes, OfferEvaluationReasonCode.DelayedPossessionConcern))
            {
                return "terms carry delayed-possession or transition risk";
            }

            if (ContainsReason(reasonCodes, OfferEvaluationReasonCode.InventoryExcludedConcern))
            {
                return "terms exclude inventory or add restart burden";
            }

            if (ContainsReason(reasonCodes, OfferEvaluationReasonCode.ExpandedSellerFinancingConcern)
                || ContainsReason(reasonCodes, OfferEvaluationReasonCode.FinancingContingencyConcern))
            {
                return "terms rely on seller financing or financing certainty that may burden the seller";
            }

            if (ContainsReason(reasonCodes, OfferEvaluationReasonCode.TermsTooRisky) || termsFit01 < 0.46f)
            {
                return "terms read as materially risky";
            }

            if (ContainsReason(reasonCodes, OfferEvaluationReasonCode.ExpandedTermsHelpful))
            {
                return "expanded terms improve the deal posture";
            }

            if (decision == OfferDecision.Counter)
            {
                return "terms look workable enough for a price counter";
            }

            return termsFit01 >= 0.68f
                ? "terms look clean and seller-friendly"
                : "terms look workable";
        }

        private static bool ContainsReason(IReadOnlyList<OfferEvaluationReasonCode> reasonCodes, OfferEvaluationReasonCode reason)
        {
            if (reasonCodes == null)
            {
                return false;
            }

            for (int i = 0; i < reasonCodes.Count; i++)
            {
                if (reasonCodes[i] == reason)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolveDecisionSummary(
            OfferDecision decision,
            BuyerFitPrimaryConcern buyerPrimaryConcern,
            float termsFit01,
            float priceStrength01)
        {
            switch (decision)
            {
                case OfferDecision.Accept:
                    return "seller would likely accept on the current overall posture";

                case OfferDecision.Counter:
                    return "seller would likely counter on price rather than reject outright";

                default:
                    return buyerPrimaryConcern switch
                    {
                        BuyerFitPrimaryConcern.LowTrust => "seller is likely to reject because buyer trust is too weak",
                        BuyerFitPrimaryConcern.LowClosingCertainty => "seller is likely to reject because closing certainty still looks thin",
                        BuyerFitPrimaryConcern.WeakContinuity => "seller is likely to reject because continuity confidence is limited",
                        BuyerFitPrimaryConcern.StrategicThreat => "seller is likely to reject because the buyer feels strategically threatening",
                        BuyerFitPrimaryConcern.ThinProfile => "seller is likely to reject because the buyer profile is still too thin",
                        _ => termsFit01 < 0.46f
                            ? "seller is likely to reject because the terms read as too risky"
                            : priceStrength01 < 1f
                                ? "seller is likely to reject because price trails the current threshold"
                                : "seller is likely to reject on the current overall posture"
                    };
            }
        }
    }
}
