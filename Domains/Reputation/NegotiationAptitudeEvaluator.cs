using System;
using UnityEngine;

namespace LandLedgers.Reputation
{
    [Serializable]
    public readonly struct NegotiationAptitudeResult
    {
        public NegotiationAptitudeResult(
            float informationQuality01,
            float offerCraft01,
            float relationshipHandling01,
            float groundedCheckModifier01,
            float offerCredibility01 = -1f,
            float sellerRelationshipSignal01 = -1f)
        {
            InformationQuality01 = Mathf.Clamp01(informationQuality01);
            OfferCraft01 = Mathf.Clamp01(offerCraft01);
            RelationshipHandling01 = Mathf.Clamp01(relationshipHandling01);
            OfferCredibility01 = offerCredibility01 >= 0f
                ? Mathf.Clamp01(offerCredibility01)
                : BuildOfferCredibility01(InformationQuality01, OfferCraft01, RelationshipHandling01, 0f);
            SellerRelationshipSignal01 = sellerRelationshipSignal01 >= 0f
                ? Mathf.Clamp01(sellerRelationshipSignal01)
                : RelationshipHandling01;
            DealLeverageModifier = Mathf.Clamp(groundedCheckModifier01, -0.12f, 0.14f);
        }

        public float InformationQuality01 { get; }
        public float OfferCraft01 { get; }
        public float RelationshipHandling01 { get; }
        public float DealLeverageModifier { get; }
        public float OfferCredibility01 { get; }
        public float SellerRelationshipSignal01 { get; }

        [Obsolete("Use DealLeverageModifier. Negotiation aptitude is soft deal leverage, not a pass/fail dialogue check.")]
        public float GroundedCheckModifier01 => DealLeverageModifier;

        internal static float BuildOfferCredibility01(float informationQuality01, float offerCraft01, float relationshipHandling01, float badFaith01)
        {
            return Mathf.Clamp01(
                Mathf.Clamp01(offerCraft01) * 0.42f
                + Mathf.Clamp01(informationQuality01) * 0.28f
                + Mathf.Clamp01(relationshipHandling01) * 0.22f
                + (1f - Mathf.Clamp01(badFaith01)) * 0.08f);
        }
    }

    public sealed class NegotiationAptitudeEvaluator
    {
        public NegotiationAptitudeResult Evaluate(NegotiationAptitudeState state)
        {
            if (state == null)
            {
                return new NegotiationAptitudeResult(0.25f, 0.25f, 0.25f, 0f, 0.25f, 0.25f);
            }

            state.Clamp();

            float experience = SaturatingProgress(state.negotiationExperiencePoints, 160f);
            float completed = SaturatingProgress(state.completedNegotiations, 24f);
            float difficult = SaturatingProgress(state.difficultNegotiationsCompleted, 12f);
            float cleanCounters = SaturatingProgress(state.cleanCountersAccepted, 12f);
            float structuredOffers = SaturatingProgress(state.wellStructuredOffers, 18f);
            float badFaith = SaturatingProgress(state.badFaithAttempts, 10f);

            float informationQuality = Mathf.Clamp01(0.25f + experience * 0.32f + difficult * 0.23f + completed * 0.12f - badFaith * 0.08f);
            float offerCraft = Mathf.Clamp01(0.25f + experience * 0.22f + structuredOffers * 0.28f + cleanCounters * 0.2f - badFaith * 0.06f);
            float relationshipHandling = Mathf.Clamp01(0.25f + completed * 0.18f + cleanCounters * 0.16f + structuredOffers * 0.14f - badFaith * 0.2f);
            float offerCredibility = NegotiationAptitudeResult.BuildOfferCredibility01(informationQuality, offerCraft, relationshipHandling, badFaith);
            float sellerRelationshipSignal = Mathf.Clamp01(relationshipHandling * 0.78f + cleanCounters * 0.14f + (1f - badFaith) * 0.08f);

            // Soft leverage for diligence quality, offer clarity, and term framing. This is intentionally not a dialogue gate.
            float dealLeverage = Mathf.Clamp(
                informationQuality * 0.045f
                + offerCraft * 0.052f
                + relationshipHandling * 0.028f
                + offerCredibility * 0.035f
                - badFaith * 0.1f
                - 0.055f,
                -0.12f,
                0.14f);

            return new NegotiationAptitudeResult(
                informationQuality,
                offerCraft,
                relationshipHandling,
                dealLeverage,
                offerCredibility,
                sellerRelationshipSignal);
        }

        private static float SaturatingProgress(float value, float scale)
        {
            if (scale <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(Mathf.Max(0f, value) / (Mathf.Max(0f, value) + scale));
        }
    }
}
