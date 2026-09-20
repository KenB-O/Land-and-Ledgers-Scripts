using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy
{
    public enum HouseholdAffinityReasonCode
    {
        None = 0,
        PurchaseCompleted = 1,
        VisitWithoutPurchase = 2,
        RepeatedUse = 3,
        FairPrice = 20,
        HighPrice = 21,
        ConsistentStock = 40,
        Stockout = 41,
        TrustedQuality = 60,
        PoorQuality = 61,
        AffinityDrift = 80,
        PreferenceStrength = 100,
        PreferenceWeakness = 101
    }

    [Serializable]
    public sealed class HouseholdBusinessAffinityState
    {
        public int householdId = -1;
        public string businessId = string.Empty;
        public bool hasBusinessType;
        public BusinessType businessType;
        public int visitCount;
        public int purchaseCount;
        public int totalSpendCents;
        public int lastVisitDayIndex = -1;

        [Range(0f, 1f)]
        public float repeatedUse01 = 0.5f;

        [Range(0f, 1f)]
        public float priceFairnessMemory01 = 0.5f;

        [Range(0f, 1f)]
        public float stockConsistencyMemory01 = 0.5f;

        [Range(0f, 1f)]
        public float trustQualityMemory01 = 0.5f;

        [Range(0f, 1f)]
        public float cachedAffinity01 = 0.5f;

        public RelationshipTrendDirection lastTrend;
        public List<HouseholdAffinityReasonCode> lastReasonCodes = new();

        public HouseholdBusinessAffinityState SanitizedCopy()
        {
            return new HouseholdBusinessAffinityState
            {
                householdId = householdId,
                businessId = businessId ?? string.Empty,
                hasBusinessType = hasBusinessType,
                businessType = businessType,
                visitCount = Mathf.Max(0, visitCount),
                purchaseCount = Mathf.Max(0, purchaseCount),
                totalSpendCents = Mathf.Max(0, totalSpendCents),
                lastVisitDayIndex = lastVisitDayIndex,
                repeatedUse01 = Mathf.Clamp01(repeatedUse01),
                priceFairnessMemory01 = Mathf.Clamp01(priceFairnessMemory01),
                stockConsistencyMemory01 = Mathf.Clamp01(stockConsistencyMemory01),
                trustQualityMemory01 = Mathf.Clamp01(trustQualityMemory01),
                cachedAffinity01 = Mathf.Clamp01(cachedAffinity01),
                lastTrend = lastTrend,
                lastReasonCodes = lastReasonCodes != null
                    ? new List<HouseholdAffinityReasonCode>(lastReasonCodes)
                    : new List<HouseholdAffinityReasonCode>()
            };
        }
    }

    [Serializable]
    public struct HouseholdAffinityEvent
    {
        public int householdId;
        public string businessId;
        public bool hasBusinessType;
        public BusinessType businessType;
        public bool completedPurchase;
        public int spendCents;
        public float priceFairness01;
        public float stockConsistency01;
        public float trustQuality01;
        public int absoluteDayIndex;

        public HouseholdAffinityEvent Sanitized()
        {
            HouseholdAffinityEvent sanitized = this;
            sanitized.businessId ??= string.Empty;
            sanitized.spendCents = Mathf.Max(0, sanitized.spendCents);
            sanitized.priceFairness01 = Mathf.Clamp01(sanitized.priceFairness01);
            sanitized.stockConsistency01 = Mathf.Clamp01(sanitized.stockConsistency01);
            sanitized.trustQuality01 = Mathf.Clamp01(sanitized.trustQuality01);
            sanitized.absoluteDayIndex = Mathf.Max(-1, sanitized.absoluteDayIndex);
            return sanitized;
        }
    }

    [Serializable]
    public readonly struct HouseholdAffinityChangeProposal
    {
        public HouseholdAffinityChangeProposal(
            int householdId,
            string businessId,
            bool completedPurchase,
            float repeatedUseDelta01,
            float priceFairnessDelta01,
            float stockConsistencyDelta01,
            float trustQualityDelta01,
            float affinityDelta01,
            float projectedRepeatedUse01,
            float projectedPriceFairness01,
            float projectedStockConsistency01,
            float projectedTrustQuality01,
            float projectedAffinity01,
            RelationshipTrendDirection trend,
            IReadOnlyList<HouseholdAffinityReasonCode> reasonCodes)
        {
            HouseholdId = householdId;
            BusinessId = businessId ?? string.Empty;
            CompletedPurchase = completedPurchase;
            RepeatedUseDelta01 = Mathf.Clamp(repeatedUseDelta01, -1f, 1f);
            PriceFairnessDelta01 = Mathf.Clamp(priceFairnessDelta01, -1f, 1f);
            StockConsistencyDelta01 = Mathf.Clamp(stockConsistencyDelta01, -1f, 1f);
            TrustQualityDelta01 = Mathf.Clamp(trustQualityDelta01, -1f, 1f);
            AffinityDelta01 = Mathf.Clamp(affinityDelta01, -1f, 1f);
            ProjectedRepeatedUse01 = Mathf.Clamp01(projectedRepeatedUse01);
            ProjectedPriceFairness01 = Mathf.Clamp01(projectedPriceFairness01);
            ProjectedStockConsistency01 = Mathf.Clamp01(projectedStockConsistency01);
            ProjectedTrustQuality01 = Mathf.Clamp01(projectedTrustQuality01);
            ProjectedAffinity01 = Mathf.Clamp01(projectedAffinity01);
            Trend = trend;
            ReasonCodes = reasonCodes ?? Array.Empty<HouseholdAffinityReasonCode>();
        }

        public int HouseholdId { get; }
        public string BusinessId { get; }
        public bool CompletedPurchase { get; }
        public float RepeatedUseDelta01 { get; }
        public float PriceFairnessDelta01 { get; }
        public float StockConsistencyDelta01 { get; }
        public float TrustQualityDelta01 { get; }
        public float AffinityDelta01 { get; }
        public float ProjectedRepeatedUse01 { get; }
        public float ProjectedPriceFairness01 { get; }
        public float ProjectedStockConsistency01 { get; }
        public float ProjectedTrustQuality01 { get; }
        public float ProjectedAffinity01 { get; }
        public RelationshipTrendDirection Trend { get; }
        public IReadOnlyList<HouseholdAffinityReasonCode> ReasonCodes { get; }
    }

    [Serializable]
    public readonly struct HouseholdAffinityScoreSummary
    {
        public HouseholdAffinityScoreSummary(
            int householdId,
            string businessId,
            bool hasBusinessType,
            BusinessType businessType,
            float affinity01,
            float preferenceWeight01,
            float repeatedUse01,
            float priceFairness01,
            float stockConsistency01,
            float trustQuality01,
            RelationshipTrendDirection trend,
            IReadOnlyList<HouseholdAffinityReasonCode> reasonCodes)
        {
            HouseholdId = householdId;
            BusinessId = businessId ?? string.Empty;
            HasBusinessType = hasBusinessType;
            BusinessType = businessType;
            Affinity01 = Mathf.Clamp01(affinity01);
            PreferenceWeight01 = Mathf.Clamp01(preferenceWeight01);
            RepeatedUse01 = Mathf.Clamp01(repeatedUse01);
            PriceFairness01 = Mathf.Clamp01(priceFairness01);
            StockConsistency01 = Mathf.Clamp01(stockConsistency01);
            TrustQuality01 = Mathf.Clamp01(trustQuality01);
            Trend = trend;
            ReasonCodes = reasonCodes ?? Array.Empty<HouseholdAffinityReasonCode>();
        }

        public int HouseholdId { get; }
        public string BusinessId { get; }
        public bool HasBusinessType { get; }
        public BusinessType BusinessType { get; }
        public float Affinity01 { get; }
        public float PreferenceWeight01 { get; }
        public float RepeatedUse01 { get; }
        public float PriceFairness01 { get; }
        public float StockConsistency01 { get; }
        public float TrustQuality01 { get; }
        public RelationshipTrendDirection Trend { get; }
        public IReadOnlyList<HouseholdAffinityReasonCode> ReasonCodes { get; }
    }
}
