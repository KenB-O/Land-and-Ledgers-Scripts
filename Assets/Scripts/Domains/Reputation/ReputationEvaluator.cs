using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Reputation
{
    [System.Serializable]
    public readonly struct BusinessReputationPropagationContext
    {
        public BusinessReputationPropagationContext(
            float visibleFailureSeverity01,
            int consecutiveVisibleFailureDays = 0,
            bool recentlyAcquired = false,
            float correctiveAction01 = 0f,
            float portfolioScale01 = 0f,
            float publicVisibility01 = 1f,
            float inheritedBaggageProtection01 = 0f)
        {
            VisibleFailureSeverity01 = Mathf.Clamp01(visibleFailureSeverity01);
            ConsecutiveVisibleFailureDays = Mathf.Max(0, consecutiveVisibleFailureDays);
            RecentlyAcquired = recentlyAcquired;
            CorrectiveAction01 = Mathf.Clamp01(correctiveAction01);
            PortfolioScale01 = Mathf.Clamp01(portfolioScale01);
            PublicVisibility01 = Mathf.Clamp01(publicVisibility01);
            InheritedBaggageProtection01 = Mathf.Clamp01(inheritedBaggageProtection01);
        }

        public float VisibleFailureSeverity01 { get; }
        public int ConsecutiveVisibleFailureDays { get; }
        public bool RecentlyAcquired { get; }
        public float CorrectiveAction01 { get; }
        public float PortfolioScale01 { get; }
        public float PublicVisibility01 { get; }
        public float InheritedBaggageProtection01 { get; }

        public static BusinessReputationPropagationContext FromLegacy(float visibleFailureSeverity01)
        {
            return new BusinessReputationPropagationContext(visibleFailureSeverity01);
        }
    }

    public sealed class ReputationEvaluator
    {
        private const float DealTrustWeight = 0.3f;
        private const float LenderTrustWeight = 0.25f;
        private const float SupplierTrustWeight = 0.15f;
        private const float LocalSocialTrustWeight = 0.2f;
        private const float OperationalReliabilityWeight = 0.1f;

        public float EvaluateHeadline01(PlayerReputationState state)
        {
            if (state == null)
            {
                return 0f;
            }

            return Mathf.Clamp01(
                Mathf.Clamp01(state.dealTrust01) * DealTrustWeight
                + Mathf.Clamp01(state.lenderTrust01) * LenderTrustWeight
                + Mathf.Clamp01(state.supplierTrust01) * SupplierTrustWeight
                + Mathf.Clamp01(state.localSocialTrust01) * LocalSocialTrustWeight
                + Mathf.Clamp01(state.operationalReliability01) * OperationalReliabilityWeight);
        }

        public int EvaluateHeadline100(PlayerReputationState state)
        {
            return Mathf.RoundToInt(EvaluateHeadline01(state) * 100f);
        }

        public ReputationInfluenceProfile BuildInfluenceProfile(PlayerReputationState reputationState, NegotiationAptitudeState aptitudeState)
        {
            PlayerReputationState reputation = reputationState ?? new PlayerReputationState();
            NegotiationAptitudeResult aptitude = new NegotiationAptitudeEvaluator().Evaluate(aptitudeState);

            return new ReputationInfluenceProfile(
                EvaluateHeadline01(reputation),
                reputation.dealTrust01,
                reputation.lenderTrust01,
                reputation.supplierTrust01,
                reputation.localSocialTrust01,
                reputation.operationalReliability01,
                aptitude.InformationQuality01,
                aptitude.OfferCraft01,
                aptitude.RelationshipHandling01,
                aptitude.DealLeverageModifier,
                aptitude.OfferCredibility01,
                aptitude.SellerRelationshipSignal01,
                EvaluateEmploymentTrust01(reputation));
        }

        public float EvaluateEmploymentTrust01(PlayerReputationState state)
        {
            PlayerReputationState reputation = state ?? new PlayerReputationState();
            reputation.Clamp();
            return ReputationInfluenceProfile.BuildDerivedEmploymentTrust01(
                reputation.dealTrust01,
                reputation.supplierTrust01,
                reputation.localSocialTrust01,
                reputation.operationalReliability01);
        }

        public int EvaluateEmploymentTrust100(PlayerReputationState state)
        {
            return Mathf.RoundToInt(EvaluateEmploymentTrust01(state) * 100f);
        }

        public ReputationChangeResult ApplyBusinessReputationPropagation(
            PlayerReputationState ownerState,
            BusinessReputationState businessState,
            float visibleFailureSeverity01)
        {
            return ApplyBusinessReputationPropagation(
                ownerState,
                businessState,
                BusinessReputationPropagationContext.FromLegacy(visibleFailureSeverity01));
        }

        public ReputationChangeResult ApplyBusinessReputationPropagation(
            PlayerReputationState ownerState,
            BusinessReputationState businessState,
            BusinessReputationPropagationContext context)
        {
            PlayerReputationState target = ownerState ?? new PlayerReputationState();
            target.Clamp();
            PlayerReputationState before = target.Clone();
            float headlineBefore = EvaluateHeadline01(before);

            BusinessReputationState business = businessState != null ? businessState.Clone() : new BusinessReputationState();
            business.Clamp();
            float headline = new BusinessReputationEvaluator().EvaluateHeadline01(business);
            float failure = Mathf.Clamp01((0.55f - headline) / 0.55f);
            float stockoutPressure = business.GetStockoutPressure01(null);
            float visibility = Mathf.Clamp01(context.VisibleFailureSeverity01 * context.PublicVisibility01);
            float visibleFailure = failure * visibility;
            float durationPressure = Mathf.Clamp01(context.ConsecutiveVisibleFailureDays / 28f);
            float inheritedProtection = Mathf.Clamp01(context.InheritedBaggageProtection01 + (context.RecentlyAcquired ? 0.55f : 0f));
            float correctiveRelief = Mathf.Clamp01(context.CorrectiveAction01);

            float operationalConcern = Mathf.Clamp01(
                visibleFailure * 0.45f
                + (1f - business.stockReliability01) * 0.17f
                + (1f - business.serviceExperience01) * 0.13f
                + (1f - business.conditionPresentationTrust01) * 0.1f
                + stockoutPressure * 0.15f);

            // Inherited baggage should survive the purchase, but owner blame should build mainly through visible duration and neglect.
            float ownerAccountability = BuildOwnerAccountability01(
                operationalConcern,
                durationPressure,
                context.PortfolioScale01,
                inheritedProtection,
                correctiveRelief);

            List<ReputationChangeReasonCode> reasons = new List<ReputationChangeReasonCode>();
            if (ownerAccountability > 0.002f)
            {
                target.Add(ReputationSubcategory.OperationalReliability, -0.03f * ownerAccountability);
                target.Add(ReputationSubcategory.LocalSocialTrust, -0.02f * ownerAccountability);
                if (stockoutPressure >= 0.45f || visibleFailure >= 0.45f || durationPressure >= 0.55f)
                {
                    target.Add(ReputationSubcategory.SupplierTrust, -0.007f * ownerAccountability);
                }

                reasons.Add(ReputationChangeReasonCode.LingeringOperationalDisruption);
            }
            else if (CanCreditOperationalRecovery(headline, stockoutPressure, context))
            {
                float recovery = Mathf.Clamp01((headline - 0.68f) / 0.32f);
                float creditedRecovery = Mathf.Clamp01(recovery * (0.65f + correctiveRelief * 0.35f));
                target.Add(ReputationSubcategory.OperationalReliability, 0.012f * creditedRecovery);
                target.Add(ReputationSubcategory.LocalSocialTrust, 0.008f * creditedRecovery);
                reasons.Add(ReputationChangeReasonCode.OperationalRecovery);
            }

            target.Clamp();
            PlayerReputationState after = target.Clone();
            return new ReputationChangeResult(
                headlineBefore,
                EvaluateHeadline01(after),
                before,
                after,
                BuildDeltas(before, after),
                reasons);
        }

        private static float BuildOwnerAccountability01(
            float operationalConcern01,
            float durationPressure01,
            float portfolioScale01,
            float inheritedProtection01,
            float correctiveAction01)
        {
            float concern = Mathf.Clamp01(operationalConcern01);
            if (concern <= 0f)
            {
                return 0f;
            }

            float duration = Mathf.Clamp01(durationPressure01);
            float scale = Mathf.Clamp01(portfolioScale01);
            float inheritedRelief = Mathf.Clamp01(inheritedProtection01) * Mathf.Clamp01(1f - duration * 0.75f);
            float correctiveRelief = Mathf.Clamp01(correctiveAction01) * 0.55f;
            float accountabilityMultiplier = Mathf.Clamp01(0.35f + duration * 0.45f + scale * 0.2f);

            return Mathf.Clamp01(concern * accountabilityMultiplier * (1f - inheritedRelief) * (1f - correctiveRelief));
        }

        private static bool CanCreditOperationalRecovery(float businessHeadline01, float stockoutPressure01, BusinessReputationPropagationContext context)
        {
            if (context.VisibleFailureSeverity01 > 0.05f || context.ConsecutiveVisibleFailureDays > 0)
            {
                return false;
            }

            return businessHeadline01 >= 0.68f && stockoutPressure01 <= 0.05f;
        }

        private static IReadOnlyList<ReputationSubcategoryDelta> BuildDeltas(PlayerReputationState before, PlayerReputationState after)
        {
            List<ReputationSubcategoryDelta> deltas = new List<ReputationSubcategoryDelta>();
            AddDelta(deltas, before, after, ReputationSubcategory.DealTrust);
            AddDelta(deltas, before, after, ReputationSubcategory.LenderTrust);
            AddDelta(deltas, before, after, ReputationSubcategory.SupplierTrust);
            AddDelta(deltas, before, after, ReputationSubcategory.LocalSocialTrust);
            AddDelta(deltas, before, after, ReputationSubcategory.OperationalReliability);
            return deltas;
        }

        private static void AddDelta(List<ReputationSubcategoryDelta> deltas, PlayerReputationState before, PlayerReputationState after, ReputationSubcategory subcategory)
        {
            float beforeValue = before.Get(subcategory);
            float afterValue = after.Get(subcategory);
            if (!Mathf.Approximately(beforeValue, afterValue))
            {
                deltas.Add(new ReputationSubcategoryDelta(subcategory, beforeValue, afterValue));
            }
        }
    }
}
