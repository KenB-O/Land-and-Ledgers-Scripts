using System;
using UnityEngine;

namespace LandLedgers.Reputation
{
    [Serializable]
    public readonly struct ReputationInfluenceProfile
    {
        public ReputationInfluenceProfile(
            float headlineReputation01,
            float dealTrust01,
            float lenderTrust01,
            float supplierTrust01,
            float localSocialTrust01,
            float operationalReliability01,
            float negotiationInformationQuality01,
            float negotiationOfferCraft01,
            float negotiationRelationshipHandling01,
            float negotiationDealLeverageModifier = 0f,
            float negotiationOfferCredibility01 = -1f,
            float negotiationSellerRelationshipSignal01 = -1f,
            float employmentTrust01 = -1f)
        {
            HeadlineReputation01 = Mathf.Clamp01(headlineReputation01);
            DealTrust01 = Mathf.Clamp01(dealTrust01);
            LenderTrust01 = Mathf.Clamp01(lenderTrust01);
            SupplierTrust01 = Mathf.Clamp01(supplierTrust01);
            LocalSocialTrust01 = Mathf.Clamp01(localSocialTrust01);
            OperationalReliability01 = Mathf.Clamp01(operationalReliability01);
            NegotiationInformationQuality01 = Mathf.Clamp01(negotiationInformationQuality01);
            NegotiationOfferCraft01 = Mathf.Clamp01(negotiationOfferCraft01);
            NegotiationRelationshipHandling01 = Mathf.Clamp01(negotiationRelationshipHandling01);
            NegotiationDealLeverageModifier = Mathf.Clamp(negotiationDealLeverageModifier, -0.12f, 0.14f);
            NegotiationOfferCredibility01 = negotiationOfferCredibility01 >= 0f
                ? Mathf.Clamp01(negotiationOfferCredibility01)
                : Mathf.Clamp01(NegotiationOfferCraft01 * 0.45f + NegotiationInformationQuality01 * 0.32f + NegotiationRelationshipHandling01 * 0.23f);
            NegotiationSellerRelationshipSignal01 = negotiationSellerRelationshipSignal01 >= 0f
                ? Mathf.Clamp01(negotiationSellerRelationshipSignal01)
                : NegotiationRelationshipHandling01;
            EmploymentTrust01 = employmentTrust01 >= 0f
                ? Mathf.Clamp01(employmentTrust01)
                : BuildDerivedEmploymentTrust01(DealTrust01, SupplierTrust01, LocalSocialTrust01, OperationalReliability01);
        }

        public float HeadlineReputation01 { get; }
        public int HeadlineReputation100 => Mathf.RoundToInt(HeadlineReputation01 * 100f);
        public float DealTrust01 { get; }
        public float LenderTrust01 { get; }
        public float SupplierTrust01 { get; }
        public float LocalSocialTrust01 { get; }
        public float OperationalReliability01 { get; }
        public float NegotiationInformationQuality01 { get; }
        public float NegotiationOfferCraft01 { get; }
        public float NegotiationRelationshipHandling01 { get; }
        public float NegotiationDealLeverageModifier { get; }
        public float NegotiationOfferCredibility01 { get; }
        public float NegotiationSellerRelationshipSignal01 { get; }
        public float EmploymentTrust01 { get; }
        public int EmploymentTrust100 => Mathf.RoundToInt(EmploymentTrust01 * 100f);

        public static float BuildDerivedEmploymentTrust01(
            float dealTrust01,
            float supplierTrust01,
            float localSocialTrust01,
            float operationalReliability01)
        {
            return Mathf.Clamp01(
                Mathf.Clamp01(localSocialTrust01) * 0.48f
                + Mathf.Clamp01(operationalReliability01) * 0.34f
                + Mathf.Clamp01(dealTrust01) * 0.12f
                + Mathf.Clamp01(supplierTrust01) * 0.06f);
        }
    }
}
