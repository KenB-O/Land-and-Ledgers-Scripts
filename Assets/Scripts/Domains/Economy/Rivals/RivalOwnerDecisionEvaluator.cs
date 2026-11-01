using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Rivals
{
    public sealed class RivalOwnerDecisionEvaluator
    {
        private const float MinimumMeaningfulScore01 = 0.24f;
        private const float ExecutableBidCashCoverageThreshold01 = 0.85f;
        private const float WatchCashCoverageThreshold01 = 0.55f;
        private const float BaselineCredibleBuyerThreshold01 = 0.45f;
        private const float StrongMonitoringCredibilityThreshold01 = 0.48f;
        private const float UnpricedWatchThresholdBonus01 = 0.08f;
        private const float UnpricedWatchMonitoringThreshold01 = 0.58f;
        private const float UnpricedDeferMonitoringThreshold01 = 0.42f;
        private const float SubWatchPricedNearWatchMargin01 = 0.07f;
        private const float PricedDeferMonitoringThreshold01 = 0.5f;
        private const float PricedDeferPartialCashThreshold01 = 0.28f;

        public IReadOnlyList<RivalDecisionProposal> Evaluate(
            RivalOwnerRuntimeState owner,
            RivalOwnerTraits traits,
            RivalOwnerScaleSnapshot scale,
            IReadOnlyList<RivalOpportunitySignal> opportunities)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            RivalOwnerTraits sanitizedTraits = traits.Sanitized();
            List<RivalDecisionProposal> proposals = new List<RivalDecisionProposal>();
            if (opportunities == null)
            {
                return proposals;
            }

            for (int i = 0; i < opportunities.Count; i++)
            {
                proposals.Add(EvaluateOpportunity(owner, sanitizedTraits, scale, opportunities[i]));
            }

            proposals.Sort(CompareProposals);
            return proposals;
        }

        private static RivalDecisionProposal EvaluateOpportunity(
            RivalOwnerRuntimeState owner,
            RivalOwnerTraits traits,
            RivalOwnerScaleSnapshot scale,
            RivalOpportunitySignal opportunity)
        {
            RivalOpportunitySignal sanitizedOpportunity = RivalEvaluationUtility.SanitizeOpportunity(opportunity);
            if (!sanitizedOpportunity.IsMeaningfullyFormed)
            {
                return BuildInsufficientSignalProposal(owner.ownerId, sanitizedOpportunity);
            }

            int referencePrice = GetReferencePrice(sanitizedOpportunity);
            bool hasPricingBasis = sanitizedOpportunity.HasPricingBasis;
            float valueRatio = GetValueRatio(sanitizedOpportunity);
            float fit = sanitizedOpportunity.strategicFit01;
            float urgency = sanitizedOpportunity.urgency01;
            float pressure = sanitizedOpportunity.marketPressure01;
            float expansionBias = GetOpportunityBias(sanitizedOpportunity.kind, traits);
            float ownerCredibility = RivalEvaluationUtility.GetOwnerCredibility01(owner);
            float opportunityScore = Mathf.Clamp01(
                sanitizedOpportunity.score01 > 0f
                    ? sanitizedOpportunity.score01
                    : valueRatio * 0.27f
                    + fit * 0.24f
                    + urgency * 0.14f
                    + pressure * 0.12f
                    + ownerCredibility * 0.13f
                    + expansionBias * 0.06f
                    + scale.ThreatToPlayer01 * 0.04f);

            float qualityNoise = DeterministicNoise01(owner.ownerId, sanitizedOpportunity.opportunityId) - 0.5f;
            float uncertaintyAmplitude = Mathf.Lerp(0.24f, 0.03f, traits.decisionQuality01);
            float perceivedScore = Mathf.Clamp01(opportunityScore + qualityNoise * uncertaintyAmplitude);

            float reserveRatio = Mathf.Lerp(0.07f, 0.34f, traits.liquidityDiscipline01) * Mathf.Lerp(1f, 0.7f, traits.riskTolerance01);
            int reserveCents = Mathf.RoundToInt(Mathf.Max(0, owner.capitalCents) * reserveRatio);
            int protectedCapital = Mathf.Max(owner.reservedCapitalCents, reserveCents);
            int availableCapital = Mathf.Max(0, owner.capitalCents - protectedCapital);
            float cashCoverage = hasPricingBasis ? Mathf.Clamp01(availableCapital / (float)Mathf.Max(1, referencePrice)) : 0f;

            float urgencyPremium = Mathf.Lerp(0f, 0.08f, urgency);
            float rivalryPremium = Mathf.Lerp(0f, 0.06f, scale.ThreatToPlayer01);
            float aggressionPremium = Mathf.Lerp(0.01f, 0.15f, traits.aggression01);
            float pricingDisciplinePenalty = Mathf.Lerp(0.03f, 0f, traits.liquidityDiscipline01);
            float bidPremium = aggressionPremium + urgencyPremium + rivalryPremium - pricingDisciplinePenalty;
            int desiredBid = hasPricingBasis ? Mathf.RoundToInt(referencePrice * (1f + Mathf.Max(0f, bidPremium))) : 0;
            int maxBid = hasPricingBasis ? Mathf.Min(availableCapital, Mathf.Max(0, desiredBid)) : 0;

            float bidThreshold = Mathf.Clamp01(
                Mathf.Lerp(0.74f, 0.46f, traits.aggression01)
                + Mathf.Lerp(0.08f, -0.05f, traits.riskTolerance01)
                + Mathf.Lerp(0.05f, -0.05f, ownerCredibility)
                + Mathf.Lerp(0.03f, -0.04f, expansionBias));
            float watchThreshold = Mathf.Clamp01(bidThreshold * Mathf.Lerp(0.8f, 0.68f, urgency));
            float monitoringStrength = GetMonitoringStrength01(fit, urgency, pressure);
            RivalDecisionAction action = GetAction(perceivedScore, cashCoverage, ownerCredibility, bidThreshold, watchThreshold, hasPricingBasis, monitoringStrength);

            List<string> reasons = BuildReasons(sanitizedOpportunity, fit, valueRatio, urgency, pressure, cashCoverage, ownerCredibility, scale, action, perceivedScore, watchThreshold, hasPricingBasis, monitoringStrength);
            return new RivalDecisionProposal(owner.ownerId, sanitizedOpportunity, action, perceivedScore, maxBid, cashCoverage, reasons);
        }

        private static int CompareProposals(RivalDecisionProposal left, RivalDecisionProposal right)
        {
            // Readiness sorts before raw score so actionable priced competition stays above speculative rumor-level signals.
            int readinessCompare = right.Readiness.CompareTo(left.Readiness);
            if (readinessCompare != 0)
            {
                return readinessCompare;
            }

            int scoreCompare = right.Score01.CompareTo(left.Score01);
            if (scoreCompare != 0)
            {
                return scoreCompare;
            }

            int pricingCompare = right.HasPricingBasis.CompareTo(left.HasPricingBasis);
            if (pricingCompare != 0)
            {
                return pricingCompare;
            }

            int actionCompare = right.Action.CompareTo(left.Action);
            if (actionCompare != 0)
            {
                return actionCompare;
            }

            int bidCompare = right.MaxBidCents.CompareTo(left.MaxBidCents);
            if (bidCompare != 0)
            {
                return bidCompare;
            }

            int opportunityCompare = string.CompareOrdinal(left.Opportunity.opportunityId ?? string.Empty, right.Opportunity.opportunityId ?? string.Empty);
            if (opportunityCompare != 0)
            {
                return opportunityCompare;
            }

            return string.CompareOrdinal(left.OwnerId ?? string.Empty, right.OwnerId ?? string.Empty);
        }

        private static List<string> BuildReasons(
            RivalOpportunitySignal opportunity,
            float fit,
            float valueRatio,
            float urgency,
            float pressure,
            float cashCoverage,
            float ownerCredibility,
            RivalOwnerScaleSnapshot scale,
            RivalDecisionAction action,
            float score,
            float watchThreshold,
            bool hasPricingBasis,
            float monitoringStrength)
        {
            List<string> reasons = new List<string>();
            if (fit >= 0.7f)
            {
                reasons.Add("strategic_fit");
            }

            if (valueRatio >= 0.6f)
            {
                reasons.Add("value_gap");
            }

            if (urgency >= 0.65f)
            {
                reasons.Add("urgent_window");
            }

            if (pressure >= 0.6f)
            {
                reasons.Add("market_pressure");
            }

            if (!hasPricingBasis)
            {
                reasons.Add("unpriced_signal");
                if (action == RivalDecisionAction.Watch)
                {
                    reasons.Add("price_discovery_needed");
                }
                else if (monitoringStrength >= 0.6f)
                {
                    reasons.Add("early_signal_only");
                }
            }
            else if (cashCoverage < 1f)
            {
                reasons.Add("capital_limited");
                if (cashCoverage <= 0f && action == RivalDecisionAction.Defer)
                {
                    reasons.Add("awaiting_financing");
                }
                else if (cashCoverage < ExecutableBidCashCoverageThreshold01 && action == RivalDecisionAction.Watch)
                {
                    reasons.Add("preparing_bid");
                }
            }

            if (ownerCredibility >= 0.65f)
            {
                reasons.Add("credible_buyer");
            }

            if (scale.ThreatToPlayer01 >= 0.5f)
            {
                reasons.Add("scale_threat");
            }

            if (action == RivalDecisionAction.Watch)
            {
                reasons.Add(ownerCredibility >= StrongMonitoringCredibilityThreshold01 ? "credible_monitoring_interest" : "monitoring_only");
            }
            else if (action == RivalDecisionAction.Defer)
            {
                if (!hasPricingBasis)
                {
                    reasons.Add("awaiting_clarity");
                }
                else if (monitoringStrength >= PricedDeferMonitoringThreshold01)
                {
                    reasons.Add("watching_priced_window");
                }
                else if (IsNearWatchThreshold(score, watchThreshold))
                {
                    reasons.Add("near_watch_window");
                }
                else if (ownerCredibility >= BaselineCredibleBuyerThreshold01 && cashCoverage >= PricedDeferPartialCashThreshold01)
                {
                    reasons.Add("credible_partial_financing");
                }
                else
                {
                    reasons.Add("thin_live_interest");
                }
            }
            else if (action == RivalDecisionAction.Ignore)
            {
                reasons.Add(hasPricingBasis && score >= MinimumMeaningfulScore01 ? "thin_priced_signal" : "below_action_threshold");
            }

            if (opportunity.kind == RivalOpportunityKind.BusinessExpansion || opportunity.kind == RivalOpportunityKind.MarketPosition)
            {
                reasons.Add("growth_lane");
            }

            return reasons;
        }

        private static RivalDecisionAction GetAction(
            float score,
            float cashCoverage,
            float ownerCredibility,
            float bidThreshold,
            float watchThreshold,
            bool hasPricingBasis,
            float monitoringStrength)
        {
            if (score < MinimumMeaningfulScore01)
            {
                return RivalDecisionAction.Ignore;
            }

            if (!hasPricingBasis)
            {
                // Unpriced opportunities can still create credible monitoring pressure, but should require stronger
                // non-price signal than a priced deal before surfacing as watch-level interest.
                float unpricedWatchThreshold = Mathf.Clamp01(watchThreshold + UnpricedWatchThresholdBonus01);
                if (score >= unpricedWatchThreshold && monitoringStrength >= UnpricedWatchMonitoringThreshold01)
                {
                    return ownerCredibility >= StrongMonitoringCredibilityThreshold01 ? RivalDecisionAction.Watch : RivalDecisionAction.Defer;
                }

                return monitoringStrength >= UnpricedDeferMonitoringThreshold01 ? RivalDecisionAction.Defer : RivalDecisionAction.Ignore;
            }

            if (score >= bidThreshold && cashCoverage >= ExecutableBidCashCoverageThreshold01 && ownerCredibility >= BaselineCredibleBuyerThreshold01)
            {
                return RivalDecisionAction.Bid;
            }

            if (cashCoverage <= 0f)
            {
                // Fully unfunded priced interest should not disappear, but it should stay below watch-level urgency.
                return score >= bidThreshold && ownerCredibility >= StrongMonitoringCredibilityThreshold01
                    ? RivalDecisionAction.Defer
                    : RivalDecisionAction.Ignore;
            }

            if (score >= watchThreshold)
            {
                if (cashCoverage >= WatchCashCoverageThreshold01 && ownerCredibility >= BaselineCredibleBuyerThreshold01)
                {
                    return RivalDecisionAction.Watch;
                }

                return RivalDecisionAction.Defer;
            }

            // Priced sub-watch interest should remain live only when it has a readable business reason.
            return ShouldKeepSubWatchPricedInterest(score, cashCoverage, ownerCredibility, watchThreshold, monitoringStrength)
                ? RivalDecisionAction.Defer
                : RivalDecisionAction.Ignore;
        }

        private static bool ShouldKeepSubWatchPricedInterest(float score, float cashCoverage, float ownerCredibility, float watchThreshold, float monitoringStrength)
        {
            return IsNearWatchThreshold(score, watchThreshold)
                || monitoringStrength >= PricedDeferMonitoringThreshold01
                || (ownerCredibility >= BaselineCredibleBuyerThreshold01 && cashCoverage >= PricedDeferPartialCashThreshold01);
        }

        private static bool IsNearWatchThreshold(float score, float watchThreshold)
        {
            return score >= Mathf.Max(MinimumMeaningfulScore01, watchThreshold - SubWatchPricedNearWatchMargin01);
        }

        private static float GetMonitoringStrength01(float fit, float urgency, float pressure)
        {
            return Mathf.Clamp01(fit * 0.45f + pressure * 0.3f + urgency * 0.25f);
        }

        private static RivalDecisionProposal BuildInsufficientSignalProposal(string ownerId, RivalOpportunitySignal opportunity)
        {
            List<string> reasons = new List<string>(2) { "insufficient_signal" };
            if (opportunity.kind == RivalOpportunityKind.Unknown)
            {
                reasons.Add("unknown_opportunity");
            }

            return new RivalDecisionProposal(ownerId, opportunity, RivalDecisionAction.Ignore, 0f, 0, 0f, reasons);
        }

        private static int GetReferencePrice(RivalOpportunitySignal opportunity)
        {
            int askingPrice = Mathf.Max(0, opportunity.askingPriceCents);
            int estimatedValue = Mathf.Max(0, opportunity.estimatedValueCents);
            if (askingPrice > 0)
            {
                return askingPrice;
            }

            return estimatedValue > 0 ? estimatedValue : 0;
        }

        private static float GetValueRatio(RivalOpportunitySignal opportunity)
        {
            int askingPrice = Mathf.Max(0, opportunity.askingPriceCents);
            int estimatedValue = Mathf.Max(0, opportunity.estimatedValueCents);
            if (askingPrice <= 0 || estimatedValue <= 0)
            {
                return 0.55f;
            }

            return Mathf.Clamp01((estimatedValue - askingPrice) / (float)Mathf.Max(1, estimatedValue) + 0.5f);
        }

        private static float GetOpportunityBias(RivalOpportunityKind kind, RivalOwnerTraits traits)
        {
            switch (kind)
            {
                case RivalOpportunityKind.BusinessExpansion:
                case RivalOpportunityKind.MarketPosition:
                    return Mathf.Lerp(0.35f, 1f, traits.expansionBias01);
                case RivalOpportunityKind.BusinessAcquisition:
                    return Mathf.Lerp(0.45f, 0.9f, traits.expansionBias01);
                case RivalOpportunityKind.Land:
                    return Mathf.Lerp(0.5f, 0.75f, traits.patience01);
                default:
                    return 0.5f;
            }
        }

        private static float DeterministicNoise01(string ownerId, string opportunityId)
        {
            unchecked
            {
                uint hash = 2166136261u;
                AppendStableHash(ref hash, ownerId);
                hash ^= 0x9e3779b9u;
                hash *= 16777619u;
                AppendStableHash(ref hash, opportunityId);
                return (hash & 0x00ffffffu) / 16777215f;
            }
        }

        private static void AppendStableHash(ref uint hash, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                hash ^= 0xffu;
                hash *= 16777619u;
                return;
            }

            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619u;
            }
        }
    }
}
