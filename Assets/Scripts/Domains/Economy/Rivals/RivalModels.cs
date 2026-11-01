using System;
using System.Collections.Generic;
using LandLedgers.Economy.Valuation;
using UnityEngine;

namespace LandLedgers.Economy.Rivals
{
    public enum RivalOpportunityKind
    {
        Unknown = 0,
        Land = 1,
        BusinessAcquisition = 2,
        BusinessExpansion = 3,
        MarketPosition = 4
    }

    public enum RivalDecisionAction
    {
        Ignore = 0,
        Defer = 1,
        Watch = 2,
        Bid = 3
    }

    public enum RivalProposalReadiness
    {
        None = 0,
        SpeculativeSignal = 1,
        ActionableInterest = 2,
        ExecutableOffer = 3
    }

    public enum RivalImportanceTier
    {
        BackgroundOwner = 0,
        LocalCompetitor = 1,
        ActiveRival = 2,
        MajorRival = 3
    }

    public enum RivalScaleDriver
    {
        None = 0,
        Capital = 1,
        Holdings = 2,
        Operations = 3,
        Land = 4,
        Balanced = 5
    }

    public enum MarketPressureReasonCode
    {
        None = 0,
        RivalLandDemand = 1,
        RivalBusinessAcquisitionDemand = 2,
        RivalExpansionDemand = 3,
        RivalBidCompetition = 4,
        MajorRivalOverlap = 5
    }

    [Serializable]
    public struct RivalOwnerTraits
    {
        [Range(0f, 1f)]
        public float aggression01;

        [Range(0f, 1f)]
        public float decisionQuality01;

        [Range(0f, 1f)]
        public float riskTolerance01;

        [Range(0f, 1f)]
        public float liquidityDiscipline01;

        [Range(0f, 1f)]
        public float patience01;

        [Range(0f, 1f)]
        public float expansionBias01;

        public static RivalOwnerTraits Balanced => new RivalOwnerTraits
        {
            aggression01 = 0.5f,
            decisionQuality01 = 0.5f,
            riskTolerance01 = 0.5f,
            liquidityDiscipline01 = 0.5f,
            patience01 = 0.5f,
            expansionBias01 = 0.5f
        };

        public RivalOwnerTraits Sanitized()
        {
            RivalOwnerTraits sanitized = this;
            sanitized.aggression01 = Mathf.Clamp01(sanitized.aggression01);
            sanitized.decisionQuality01 = Mathf.Clamp01(sanitized.decisionQuality01);
            sanitized.riskTolerance01 = Mathf.Clamp01(sanitized.riskTolerance01);
            sanitized.liquidityDiscipline01 = Mathf.Clamp01(sanitized.liquidityDiscipline01);
            sanitized.patience01 = Mathf.Clamp01(sanitized.patience01);
            sanitized.expansionBias01 = Mathf.Clamp01(sanitized.expansionBias01);
            return sanitized;
        }
    }

    [Serializable]
    public sealed class RivalOwnerDefinition : ScriptableObject
    {
        public string ownerId;
        public string displayName;
        public string familyOrCompanyLabel;
        public int startingCapitalCents = 25000;
        public RivalOwnerTraits initialTraits = RivalOwnerTraits.Balanced;
        public List<string> sectorPreferences = new List<string>();
        public List<string> expansionPreferences = new List<string>();
    }

    [Serializable]
    public sealed class RivalOwnerRuntimeState
    {
        public string ownerId;
        public int capitalCents;
        public int reservedCapitalCents;
        public List<int> ownedPlotIds = new List<int>();
        public List<int> ownedBuildingIds = new List<int>();
        public List<string> ownedBusinessIds = new List<string>();
        public float reputation01 = 0.5f;
        public float localTrust01 = 0.5f;
        public float priorDealReliability01 = 0.5f;
        public float communityFit01 = 0.5f;
        public List<string> recentDecisionHistory = new List<string>();

        public int AvailableCapitalCents => Mathf.Max(0, capitalCents - reservedCapitalCents);
    }

    [Serializable]
    public struct RivalScaleInputs
    {
        public int rivalCapitalCents;
        public int rivalHoldingValueCents;
        public int rivalBusinessCount;
        public int rivalLandCount;
        public int playerCapitalCents;
        public int playerHoldingValueCents;
        public int playerBusinessCount;
        public int playerLandCount;
        public int baselineTownCapitalCents;
        public int baselineHoldingValueCents;
        public int baselineTownBusinessCount;
        public int baselineTownLandCount;
        public float opportunityOverlap01;
    }

    [Serializable]
    public readonly struct RivalOwnerScaleSnapshot
    {
        private const float MaterialScaleAxisThreshold01 = 0.25f;
        private const float FocusedScaleAdvantageThreshold01 = 0.55f;
        private const float DominantScaleTieMargin01 = 0.07f;

        public RivalOwnerScaleSnapshot(
            string ownerId,
            int capitalValueCents,
            int holdingsValueCents,
            int businessCount,
            int landCount,
            float capitalScale,
            float holdingScale,
            float operatingScale,
            float totalScaleScore01,
            float threatToPlayer01,
            RivalImportanceTier importanceTier)
            : this(
                ownerId,
                capitalValueCents,
                holdingsValueCents,
                businessCount,
                landCount,
                capitalScale,
                holdingScale,
                operatingScale,
                0f,
                totalScaleScore01,
                threatToPlayer01,
                importanceTier)
        {
        }

        public RivalOwnerScaleSnapshot(
            string ownerId,
            int capitalValueCents,
            int holdingsValueCents,
            int businessCount,
            int landCount,
            float capitalScale,
            float holdingScale,
            float operatingScale,
            float landScale,
            float totalScaleScore01,
            float threatToPlayer01,
            RivalImportanceTier importanceTier)
        {
            OwnerId = ownerId ?? string.Empty;
            CapitalValueCents = Mathf.Max(0, capitalValueCents);
            HoldingsValueCents = Mathf.Max(0, holdingsValueCents);
            BusinessCount = Mathf.Max(0, businessCount);
            LandCount = Mathf.Max(0, landCount);
            CapitalScale = Mathf.Clamp01(capitalScale);
            HoldingScale = Mathf.Clamp01(holdingScale);
            OperatingScale = Mathf.Clamp01(operatingScale);
            LandScale = Mathf.Clamp01(landScale);
            TotalScaleScore01 = Mathf.Clamp01(totalScaleScore01);
            ThreatToPlayer01 = Mathf.Clamp01(threatToPlayer01);
            ImportanceTier = importanceTier;
        }

        public string OwnerId { get; }
        public int CapitalValueCents { get; }
        public int HoldingsValueCents { get; }
        public int BusinessCount { get; }
        public int LandCount { get; }
        public float CapitalScale { get; }
        public float HoldingScale { get; }
        public float OperatingScale { get; }
        public float LandScale { get; }
        public float TotalScaleScore01 { get; }
        public float ThreatToPlayer01 { get; }
        public RivalImportanceTier ImportanceTier { get; }
        public bool IsMaterialCompetitor => ImportanceTier >= RivalImportanceTier.LocalCompetitor;
        public bool IsActiveCompetitiveThreat => ImportanceTier >= RivalImportanceTier.ActiveRival || ThreatToPlayer01 >= 0.5f;
        public float DominantScaleScore01 => Mathf.Max(Mathf.Max(CapitalScale, HoldingScale), Mathf.Max(OperatingScale, LandScale));
        public int MaterialScaleAxisCount
        {
            get
            {
                int count = 0;
                if (CapitalScale >= MaterialScaleAxisThreshold01)
                {
                    count++;
                }

                if (HoldingScale >= MaterialScaleAxisThreshold01)
                {
                    count++;
                }

                if (OperatingScale >= MaterialScaleAxisThreshold01)
                {
                    count++;
                }

                if (LandScale >= MaterialScaleAxisThreshold01)
                {
                    count++;
                }

                return count;
            }
        }

        public RivalScaleDriver DominantScaleDriver
        {
            get
            {
                float dominant = DominantScaleScore01;
                if (dominant <= 0f)
                {
                    return RivalScaleDriver.None;
                }

                int tiedAxes = 0;
                RivalScaleDriver driver = RivalScaleDriver.None;
                ConsiderDriver(CapitalScale, dominant, RivalScaleDriver.Capital, ref tiedAxes, ref driver);
                ConsiderDriver(HoldingScale, dominant, RivalScaleDriver.Holdings, ref tiedAxes, ref driver);
                ConsiderDriver(OperatingScale, dominant, RivalScaleDriver.Operations, ref tiedAxes, ref driver);
                ConsiderDriver(LandScale, dominant, RivalScaleDriver.Land, ref tiedAxes, ref driver);
                return tiedAxes > 1 ? RivalScaleDriver.Balanced : driver;
            }
        }

        public bool IsBroadScaleRival => MaterialScaleAxisCount >= 3 && IsMaterialCompetitor;
        public bool HasFocusedScaleAdvantage => MaterialScaleAxisCount == 1 && DominantScaleScore01 >= FocusedScaleAdvantageThreshold01;

        private static void ConsiderDriver(float value, float dominant, RivalScaleDriver candidate, ref int tiedAxes, ref RivalScaleDriver driver)
        {
            if (dominant - value > DominantScaleTieMargin01)
            {
                return;
            }

            tiedAxes++;
            if (driver == RivalScaleDriver.None)
            {
                driver = candidate;
            }
        }
    }

    [Serializable]
    public struct RivalOpportunitySignal
    {
        public string opportunityId;
        public RivalOpportunityKind kind;
        public string categoryId;
        public int estimatedValueCents;
        public int askingPriceCents;
        public float marketPressure01;
        public float strategicFit01;
        public float urgency01;
        public float score01;

        public bool HasPricingBasis => estimatedValueCents > 0 || askingPriceCents > 0;
        public bool HasScoringBasis => score01 > 0f || strategicFit01 > 0f || marketPressure01 > 0f || urgency01 > 0f;
        public bool IsMeaningfullyFormed => HasPricingBasis || HasScoringBasis;
    }

    [Serializable]
    public readonly struct RivalDecisionProposal
    {
        public RivalDecisionProposal(
            string ownerId,
            RivalOpportunitySignal opportunity,
            RivalDecisionAction action,
            float score01,
            int maxBidCents,
            float cashCoverage01,
            IReadOnlyList<string> reasonCodes)
        {
            OwnerId = ownerId ?? string.Empty;
            Opportunity = RivalEvaluationUtility.SanitizeOpportunity(opportunity);
            Action = action;
            Score01 = Mathf.Clamp01(score01);
            MaxBidCents = Mathf.Max(0, maxBidCents);
            CashCoverage01 = Mathf.Clamp01(cashCoverage01);
            ReasonCodes = RivalEvaluationUtility.NormalizeReasonCodes(reasonCodes);
        }

        public string OwnerId { get; }
        public RivalOpportunitySignal Opportunity { get; }
        public RivalDecisionAction Action { get; }
        public float Score01 { get; }
        public int MaxBidCents { get; }
        public float CashCoverage01 { get; }
        public IReadOnlyList<string> ReasonCodes { get; }
        public bool IsActionable => Action >= RivalDecisionAction.Watch;
        public bool IsCommittedAction => Action == RivalDecisionAction.Bid;
        public bool HasPricingBasis => Opportunity.HasPricingBasis;

        // Defer still represents live rival interest even when it is not yet a watch/bid level threat.
        public bool HasLiveCompetitiveInterest => Action >= RivalDecisionAction.Defer && Opportunity.IsMeaningfullyFormed;

        public bool IsSpeculativeSignal => !IsActionable && HasLiveCompetitiveInterest;

        // Executable offer pressure should only exist once the rival is both priced and actually committing.
        public bool SupportsExecutableOffer => IsCommittedAction && HasPricingBasis && MaxBidCents > 0 && CashCoverage01 > 0f;

        // Readiness keeps UI/log ordering grounded: executable offers first, then actionable interest, then live but uncommitted leads.
        public RivalProposalReadiness Readiness => SupportsExecutableOffer
            ? RivalProposalReadiness.ExecutableOffer
            : IsActionable
                ? RivalProposalReadiness.ActionableInterest
                : IsSpeculativeSignal
                    ? RivalProposalReadiness.SpeculativeSignal
                    : RivalProposalReadiness.None;

        public string PrimaryReasonCode => ReasonCodes.Count > 0 ? ReasonCodes[0] : string.Empty;
    }

    [Serializable]
    public readonly struct RivalBidPressureResult
    {
        public RivalBidPressureResult(BuyerOfferProfile buyerProfile, AcquisitionOfferTerms offerTerms, float bidPressure01)
        {
            BuyerProfile = buyerProfile;
            OfferTerms = offerTerms;
            BidPressure01 = Mathf.Clamp01(bidPressure01);
        }

        public BuyerOfferProfile BuyerProfile { get; }
        public AcquisitionOfferTerms OfferTerms { get; }
        public float BidPressure01 { get; }
        public bool HasSeriousPressure => RivalEvaluationUtility.IsSeriousBidPressure(BidPressure01);
        public bool HasExecutableOffer => OfferTerms.offerPriceCents > 0 && BidPressure01 > 0f;
        // Indicative-only should mean there is real non-executable rival heat, not just an identified rival buyer profile.
        public bool IsIndicativeOnly => !HasExecutableOffer && BidPressure01 > 0f && !string.IsNullOrEmpty(BuyerProfile.buyerId);
    }

    [Serializable]
    public readonly struct MarketPressureSnapshot
    {
        // Legacy overload preserved for older callers that only tracked actionable counts before
        // this lane separated live demand pressure from watch/bid-level actionable interest.
        public MarketPressureSnapshot(
            string marketId,
            RivalOpportunityKind kind,
            string categoryId,
            float landDemandPressure01,
            float businessAcquisitionPressure01,
            float expansionDemandPressure01,
            float localCompetitionPressure01,
            float bidCompetitionPressure01,
            int actionableProposalCount,
            int committedBidCount,
            int relevantRivalCount,
            IReadOnlyList<MarketPressureReasonCode> reasonCodes)
            : this(
                marketId,
                kind,
                categoryId,
                landDemandPressure01,
                businessAcquisitionPressure01,
                expansionDemandPressure01,
                localCompetitionPressure01,
                bidCompetitionPressure01,
                actionableProposalCount,
                0,
                actionableProposalCount,
                committedBidCount,
                relevantRivalCount,
                reasonCodes)
        {
        }

        public MarketPressureSnapshot(
            string marketId,
            RivalOpportunityKind kind,
            string categoryId,
            float landDemandPressure01,
            float businessAcquisitionPressure01,
            float expansionDemandPressure01,
            float localCompetitionPressure01,
            float bidCompetitionPressure01,
            int pressureContributingProposalCount,
            int actionableProposalCount,
            int committedBidCount,
            int relevantRivalCount,
            IReadOnlyList<MarketPressureReasonCode> reasonCodes)
            : this(
                marketId,
                kind,
                categoryId,
                landDemandPressure01,
                businessAcquisitionPressure01,
                expansionDemandPressure01,
                localCompetitionPressure01,
                bidCompetitionPressure01,
                pressureContributingProposalCount,
                Mathf.Max(0, pressureContributingProposalCount - actionableProposalCount),
                actionableProposalCount,
                committedBidCount,
                relevantRivalCount,
                reasonCodes)
        {
        }

        public MarketPressureSnapshot(
            string marketId,
            RivalOpportunityKind kind,
            string categoryId,
            float landDemandPressure01,
            float businessAcquisitionPressure01,
            float expansionDemandPressure01,
            float localCompetitionPressure01,
            float bidCompetitionPressure01,
            int pressureContributingProposalCount,
            int speculativeProposalCount,
            int actionableProposalCount,
            int committedBidCount,
            int relevantRivalCount,
            IReadOnlyList<MarketPressureReasonCode> reasonCodes)
        {
            MarketId = marketId ?? string.Empty;
            Kind = kind;
            CategoryId = categoryId ?? string.Empty;
            LandDemandPressure01 = Mathf.Clamp01(landDemandPressure01);
            BusinessAcquisitionPressure01 = Mathf.Clamp01(businessAcquisitionPressure01);
            ExpansionDemandPressure01 = Mathf.Clamp01(expansionDemandPressure01);
            LocalCompetitionPressure01 = Mathf.Clamp01(localCompetitionPressure01);
            BidCompetitionPressure01 = Mathf.Clamp01(bidCompetitionPressure01);
            PressureContributingProposalCount = Mathf.Max(0, pressureContributingProposalCount);
            SpeculativeProposalCount = Mathf.Clamp(speculativeProposalCount, 0, PressureContributingProposalCount);
            ActionableProposalCount = Mathf.Clamp(actionableProposalCount, 0, PressureContributingProposalCount);
            CommittedBidCount = Mathf.Clamp(committedBidCount, 0, ActionableProposalCount);
            RelevantRivalCount = Mathf.Max(0, relevantRivalCount);
            ReasonCodes = RivalEvaluationUtility.NormalizeReasonCodes(reasonCodes);
        }

        public string MarketId { get; }
        public RivalOpportunityKind Kind { get; }
        public string CategoryId { get; }
        public float LandDemandPressure01 { get; }
        public float BusinessAcquisitionPressure01 { get; }
        public float ExpansionDemandPressure01 { get; }
        public float LocalCompetitionPressure01 { get; }
        public float BidCompetitionPressure01 { get; }

        // Counts every non-ignored rival proposal that still contributes demand pressure, including defer-level leads.
        public int PressureContributingProposalCount { get; }

        // Speculative proposals are live defer-level signals: useful for market texture, weaker than watch/bid pressure.
        public int SpeculativeProposalCount { get; }

        // Actionable proposals are watch/bid level interest that can be surfaced more aggressively in notices/UI.
        public int ActionableProposalCount { get; }

        public int CommittedBidCount { get; }
        public int RelevantRivalCount { get; }
        public IReadOnlyList<MarketPressureReasonCode> ReasonCodes { get; }

        // PeakPressure01 is intended as the safest compact inspection read for UI and logs.
        public float PeakPressure01 => Mathf.Max(
            Mathf.Max(LandDemandPressure01, BusinessAcquisitionPressure01),
            Mathf.Max(ExpansionDemandPressure01, Mathf.Max(LocalCompetitionPressure01, BidCompetitionPressure01)));

        public bool HasMeaningfulCompetition => RivalEvaluationUtility.IsMaterialPressure(LocalCompetitionPressure01) || RivalEvaluationUtility.IsMaterialPressure(BidCompetitionPressure01);
        public bool HasBidCompetition => CommittedBidCount > 0 && RivalEvaluationUtility.IsMaterialPressure(BidCompetitionPressure01);
        public bool HasActionableDemand => ActionableProposalCount > 0 && RivalEvaluationUtility.IsMaterialPressure(PeakPressure01);
        public bool HasSpeculativeDemand => SpeculativeProposalCount > 0 && RivalEvaluationUtility.IsMaterialPressure(PeakPressure01);
        public bool HasOnlySpeculativeDemand => HasSpeculativeDemand && ActionableProposalCount <= 0;
        public bool HasLiveCompetitiveInterest => PressureContributingProposalCount > 0;
        public bool HasMarketRelevantRivals => RelevantRivalCount > 0;
        public MarketPressureReasonCode PrimaryReasonCode => ReasonCodes.Count > 0 ? ReasonCodes[0] : MarketPressureReasonCode.None;
    }

    internal static class RivalEvaluationUtility
    {
        // Shared surfacing thresholds live here so model helpers and evaluators do not drift apart over time.
        public const float MaterialPressureThreshold01 = 0.25f;
        public const float SeriousBidPressureThreshold01 = 0.5f;

        public static float GetOwnerCredibility01(RivalOwnerRuntimeState owner)
        {
            if (owner == null)
            {
                return 0f;
            }

            float dealReliability = Mathf.Clamp01(owner.priorDealReliability01);
            float reputation = Mathf.Clamp01(owner.reputation01);
            float localTrust = Mathf.Clamp01(owner.localTrust01);
            float communityFit = Mathf.Clamp01(owner.communityFit01);
            return Mathf.Clamp01(dealReliability * 0.36f + reputation * 0.28f + localTrust * 0.22f + communityFit * 0.14f);
        }

        public static bool IsMaterialPressure(float value)
        {
            return Mathf.Clamp01(value) >= MaterialPressureThreshold01;
        }

        public static bool IsSeriousBidPressure(float value)
        {
            return Mathf.Clamp01(value) >= SeriousBidPressureThreshold01;
        }

        public static RivalOpportunitySignal SanitizeOpportunity(RivalOpportunitySignal opportunity)
        {
            opportunity.opportunityId = opportunity.opportunityId ?? string.Empty;
            opportunity.categoryId = opportunity.categoryId ?? string.Empty;
            opportunity.estimatedValueCents = Mathf.Max(0, opportunity.estimatedValueCents);
            opportunity.askingPriceCents = Mathf.Max(0, opportunity.askingPriceCents);
            opportunity.marketPressure01 = Sanitize01(opportunity.marketPressure01);
            opportunity.strategicFit01 = Sanitize01(opportunity.strategicFit01);
            opportunity.urgency01 = Sanitize01(opportunity.urgency01);
            opportunity.score01 = Sanitize01(opportunity.score01);
            return opportunity;
        }

        public static IReadOnlyList<string> NormalizeReasonCodes(IReadOnlyList<string> reasonCodes)
        {
            if (reasonCodes == null || reasonCodes.Count == 0)
            {
                return Array.Empty<string>();
            }

            List<string> normalized = new List<string>(reasonCodes.Count);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < reasonCodes.Count; i++)
            {
                string reasonCode = reasonCodes[i];
                if (string.IsNullOrWhiteSpace(reasonCode))
                {
                    continue;
                }

                string trimmed = reasonCode.Trim();
                if (seen.Add(trimmed))
                {
                    normalized.Add(trimmed);
                }
            }

            return normalized.Count > 0 ? normalized.ToArray() : Array.Empty<string>();
        }

        public static IReadOnlyList<MarketPressureReasonCode> NormalizeReasonCodes(IReadOnlyList<MarketPressureReasonCode> reasonCodes)
        {
            if (reasonCodes == null || reasonCodes.Count == 0)
            {
                return Array.Empty<MarketPressureReasonCode>();
            }

            List<MarketPressureReasonCode> normalized = new List<MarketPressureReasonCode>(reasonCodes.Count);
            HashSet<MarketPressureReasonCode> seen = new HashSet<MarketPressureReasonCode>();
            for (int i = 0; i < reasonCodes.Count; i++)
            {
                MarketPressureReasonCode reasonCode = reasonCodes[i];
                if (reasonCode == MarketPressureReasonCode.None)
                {
                    continue;
                }

                if (seen.Add(reasonCode))
                {
                    normalized.Add(reasonCode);
                }
            }

            return normalized.Count > 0 ? normalized.ToArray() : Array.Empty<MarketPressureReasonCode>();
        }

        private static float Sanitize01(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);
        }
    }
}
