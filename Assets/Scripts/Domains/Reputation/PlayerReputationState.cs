using System;
using UnityEngine;

namespace LandLedgers.Reputation
{
    [Serializable]
    public sealed class PlayerReputationState
    {
        public float dealTrust01 = 0.5f;
        public float lenderTrust01 = 0.5f;
        public float supplierTrust01 = 0.5f;
        public float localSocialTrust01 = 0.5f;
        public float operationalReliability01 = 0.5f;

        public PlayerReputationState()
        {
        }

        public PlayerReputationState(
            float dealTrust01,
            float lenderTrust01,
            float supplierTrust01,
            float localSocialTrust01,
            float operationalReliability01)
        {
            this.dealTrust01 = Mathf.Clamp01(dealTrust01);
            this.lenderTrust01 = Mathf.Clamp01(lenderTrust01);
            this.supplierTrust01 = Mathf.Clamp01(supplierTrust01);
            this.localSocialTrust01 = Mathf.Clamp01(localSocialTrust01);
            this.operationalReliability01 = Mathf.Clamp01(operationalReliability01);
        }

        public PlayerReputationState Clone()
        {
            return new PlayerReputationState(
                dealTrust01,
                lenderTrust01,
                supplierTrust01,
                localSocialTrust01,
                operationalReliability01);
        }

        public void Clamp()
        {
            dealTrust01 = Mathf.Clamp01(dealTrust01);
            lenderTrust01 = Mathf.Clamp01(lenderTrust01);
            supplierTrust01 = Mathf.Clamp01(supplierTrust01);
            localSocialTrust01 = Mathf.Clamp01(localSocialTrust01);
            operationalReliability01 = Mathf.Clamp01(operationalReliability01);
        }

        public float Get(ReputationSubcategory subcategory)
        {
            return subcategory switch
            {
                ReputationSubcategory.DealTrust => dealTrust01,
                ReputationSubcategory.LenderTrust => lenderTrust01,
                ReputationSubcategory.SupplierTrust => supplierTrust01,
                ReputationSubcategory.LocalSocialTrust => localSocialTrust01,
                ReputationSubcategory.OperationalReliability => operationalReliability01,
                _ => 0f
            };
        }

        public void Add(ReputationSubcategory subcategory, float delta)
        {
            switch (subcategory)
            {
                case ReputationSubcategory.DealTrust:
                    dealTrust01 = Mathf.Clamp01(dealTrust01 + delta);
                    break;
                case ReputationSubcategory.LenderTrust:
                    lenderTrust01 = Mathf.Clamp01(lenderTrust01 + delta);
                    break;
                case ReputationSubcategory.SupplierTrust:
                    supplierTrust01 = Mathf.Clamp01(supplierTrust01 + delta);
                    break;
                case ReputationSubcategory.LocalSocialTrust:
                    localSocialTrust01 = Mathf.Clamp01(localSocialTrust01 + delta);
                    break;
                case ReputationSubcategory.OperationalReliability:
                    operationalReliability01 = Mathf.Clamp01(operationalReliability01 + delta);
                    break;
            }
        }
    }
}
