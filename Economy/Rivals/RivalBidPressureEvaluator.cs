using System;
using LandLedgers.Economy.Valuation;
using UnityEngine;

namespace LandLedgers.Economy.Rivals
{
    public sealed class RivalBidPressureEvaluator
    {
        private const float ExecutableBidCashCoverageThreshold01 = 0.85f;
        private const float WatchCashCoverageThreshold01 = 0.55f;
        private const float PricedWatchIndicativeCap01 = 0.7f;
        private const float PricedDeferIndicativeCap01 = 0.52f;
        private const float PricedUnfundedDeferIndicativeCap01 = 0.42f;
        private const float UnpricedWatchIndicativeCap01 = 0.44f;
        private const float UnpricedDeferIndicativeCap01 = 0.26f;

        public RivalBidPressureResult Evaluate(
            RivalOwnerRuntimeState owner,
            RivalOwnerTraits traits,
            RivalOwnerScaleSnapshot scale,
            RivalDecisionProposal proposal)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (proposal.SupportsExecutableOffer)
            {
                return BuildExecutableOfferResult(owner, traits, scale, proposal);
            }

            return BuildIndicativeOnlyResult(owner, traits, scale, proposal);
        }

        private static RivalBidPressureResult BuildExecutableOfferResult(
            RivalOwnerRuntimeState owner,
            RivalOwnerTraits traits,
            RivalOwnerScaleSnapshot scale,
            RivalDecisionProposal proposal)
        {
            RivalOwnerTraits sanitizedTraits = traits.Sanitized();
            float cashCertainty = Mathf.Clamp01(proposal.CashCoverage01);
            float ownerCredibility = RivalEvaluationUtility.GetOwnerCredibility01(owner);
            float urgency = Mathf.Clamp01(proposal.Opportunity.urgency01);
            float score = Mathf.Clamp01(proposal.Score01);
            float closingSpeed = Mathf.Clamp01(0.24f + ownerCredibility * 0.28f + sanitizedTraits.aggression01 * 0.18f + urgency * 0.18f + cashCertainty * 0.12f);

            int offerPrice = Mathf.Max(0, proposal.MaxBidCents);
            BuyerOfferProfile buyer = BuildBuyerProfile(owner, scale, cashCertainty);

            AcquisitionOfferTerms terms = new AcquisitionOfferTerms
            {
                offerPriceCents = offerPrice,
                earnestMoneyCents = Mathf.RoundToInt(offerPrice * Mathf.Clamp01(Mathf.Lerp(0.05f, 0.13f, cashCertainty) + Mathf.Lerp(0f, 0.03f, urgency) + Mathf.Lerp(0f, 0.02f, ownerCredibility))),
                closingSpeed01 = closingSpeed,
                contingencyBurden01 = Mathf.Clamp01(0.26f - cashCertainty * 0.12f - ownerCredibility * 0.07f + Mathf.Lerp(0.04f, -0.03f, sanitizedTraits.riskTolerance01)),
                inspectionStrictness01 = Mathf.Clamp01(0.12f + (1f - sanitizedTraits.decisionQuality01) * 0.08f + (1f - cashCertainty) * 0.08f + Mathf.Lerp(0.05f, -0.02f, urgency)),
                sellerFinancingRequested = false,
                nonPriceConcessions01 = Mathf.Clamp01(0.12f + sanitizedTraits.aggression01 * 0.16f + ownerCredibility * 0.12f + urgency * 0.1f)
            };

            float bidPressure = Mathf.Clamp01(
                score * 0.28f
                + cashCertainty * 0.22f
                + ownerCredibility * 0.2f
                + Mathf.Clamp01(scale.ThreatToPlayer01) * 0.16f
                + urgency * 0.08f
                + sanitizedTraits.aggression01 * 0.06f);
            return new RivalBidPressureResult(buyer, terms, bidPressure);
        }

        private static RivalBidPressureResult BuildIndicativeOnlyResult(
            RivalOwnerRuntimeState owner,
            RivalOwnerTraits traits,
            RivalOwnerScaleSnapshot scale,
            RivalDecisionProposal proposal)
        {
            if (!proposal.HasLiveCompetitiveInterest || proposal.Readiness == RivalProposalReadiness.None)
            {
                BuyerOfferProfile emptyBuyer = BuildBuyerProfile(owner, scale, 0f);
                AcquisitionOfferTerms emptyTerms = new AcquisitionOfferTerms
                {
                    offerPriceCents = 0,
                    earnestMoneyCents = 0,
                    closingSpeed01 = 0f,
                    contingencyBurden01 = 0f,
                    inspectionStrictness01 = 0f,
                    sellerFinancingRequested = false,
                    nonPriceConcessions01 = 0f
                };
                return new RivalBidPressureResult(emptyBuyer, emptyTerms, 0f);
            }

            RivalOwnerTraits sanitizedTraits = traits.Sanitized();
            float ownerCredibility = RivalEvaluationUtility.GetOwnerCredibility01(owner);
            float score = Mathf.Clamp01(proposal.Score01);
            float urgency = Mathf.Clamp01(proposal.Opportunity.urgency01);
            float scaleThreat = Mathf.Clamp01(scale.ThreatToPlayer01);
            float cashCoverage = proposal.HasPricingBasis ? Mathf.Clamp01(proposal.CashCoverage01) : 0f;
            float financingReadiness = proposal.HasPricingBasis
                ? Mathf.Clamp01(cashCoverage / ExecutableBidCashCoverageThreshold01)
                : 0f;

            float actionSignal = GetIndicativeActionSignal01(proposal.Action);
            float pricingSignal = proposal.HasPricingBasis
                ? Mathf.Lerp(0.45f, 1f, financingReadiness)
                : 0.35f;
            float indicativeCap = GetIndicativePressureCap01(proposal, cashCoverage);
            float temperamentDrive = Mathf.Clamp01(
                sanitizedTraits.aggression01 * 0.24f
                + sanitizedTraits.riskTolerance01 * 0.18f
                + sanitizedTraits.expansionBias01 * 0.18f
                + (1f - sanitizedTraits.patience01) * 0.16f
                + (1f - sanitizedTraits.liquidityDiscipline01) * 0.14f
                + sanitizedTraits.decisionQuality01 * 0.1f);
            float disciplineBrake = proposal.HasPricingBasis
                ? Mathf.Lerp(1f, 0.72f, sanitizedTraits.liquidityDiscipline01 * (1f - financingReadiness))
                : Mathf.Lerp(1f, 0.82f, sanitizedTraits.patience01);

            // Temperament can shape non-executable rival heat, but it must never fabricate an offer price or terms.
            float rawPressure = Mathf.Clamp01(
                score * 0.3f
                + ownerCredibility * 0.16f
                + scaleThreat * 0.16f
                + urgency * 0.1f
                + actionSignal * 0.09f
                + pricingSignal * 0.07f
                + temperamentDrive * 0.12f);
            float bidPressure = Mathf.Clamp01(rawPressure * indicativeCap * disciplineBrake);

            BuyerOfferProfile buyer = BuildBuyerProfile(owner, scale, cashCoverage);
            AcquisitionOfferTerms terms = new AcquisitionOfferTerms
            {
                offerPriceCents = 0,
                earnestMoneyCents = 0,
                closingSpeed01 = 0f,
                contingencyBurden01 = 0f,
                inspectionStrictness01 = 0f,
                sellerFinancingRequested = false,
                nonPriceConcessions01 = 0f
            };
            return new RivalBidPressureResult(buyer, terms, bidPressure);
        }

        private static float GetIndicativeActionSignal01(RivalDecisionAction action)
        {
            switch (action)
            {
                case RivalDecisionAction.Watch:
                    return 1f;
                case RivalDecisionAction.Defer:
                    return 0.55f;
                default:
                    return 0f;
            }
        }

        private static float GetIndicativePressureCap01(RivalDecisionProposal proposal, float cashCoverage)
        {
            if (!proposal.HasPricingBasis)
            {
                return proposal.Action == RivalDecisionAction.Watch
                    ? UnpricedWatchIndicativeCap01
                    : UnpricedDeferIndicativeCap01;
            }

            if (proposal.Action == RivalDecisionAction.Watch)
            {
                return cashCoverage >= WatchCashCoverageThreshold01
                    ? PricedWatchIndicativeCap01
                    : PricedDeferIndicativeCap01;
            }

            return cashCoverage > 0f
                ? PricedDeferIndicativeCap01
                : PricedUnfundedDeferIndicativeCap01;
        }

        private static BuyerOfferProfile BuildBuyerProfile(RivalOwnerRuntimeState owner, RivalOwnerScaleSnapshot scale, float cashCertainty)
        {
            return new BuyerOfferProfile
            {
                buyerId = owner.ownerId,
                buyerKind = BuyerKind.Rival,
                reputation01 = Mathf.Clamp01(owner.reputation01),
                localTrust01 = Mathf.Clamp01(owner.localTrust01),
                priorDealReliability01 = Mathf.Clamp01(owner.priorDealReliability01),
                strategicThreat01 = Mathf.Clamp01(scale.ThreatToPlayer01),
                communityFit01 = Mathf.Clamp01(owner.communityFit01),
                cashCertainty01 = Mathf.Clamp01(cashCertainty),
                relationshipWithSeller01 = 0f
            };
        }
    }
}
