using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy
{
    public sealed class HouseholdAffinityEvaluator
    {
        private const float NeutralMemory = 0.5f;
        private const float DailyDriftStep = 0.001f;
        private const float MaxSingleDriftStep = 0.08f;
        private const float PositiveSignalThreshold = 0.62f;
        private const float NegativeSignalThreshold = 0.38f;
        private const float GeneralStorePurchaseHabitBonus = 0.012f;
        private const float GeneralStoreDriftMultiplier = 0.82f;

        public HouseholdAffinityChangeProposal ProposeChange(HouseholdBusinessAffinityState state, HouseholdAffinityEvent affinityEvent)
        {
            HouseholdBusinessAffinityState current = GetState(state);
            HouseholdAffinityEvent sanitizedEvent = affinityEvent.Sanitized();
            List<HouseholdAffinityReasonCode> reasons = new List<HouseholdAffinityReasonCode>();

            float repeated = current.repeatedUse01;
            float price = current.priceFairnessMemory01;
            float stock = current.stockConsistencyMemory01;
            float trustQuality = current.trustQualityMemory01;
            bool generalStore = IsGeneralStore(current, sanitizedEvent);
            ApplyDrift(current, sanitizedEvent.absoluteDayIndex, generalStore, ref repeated, ref price, ref stock, ref trustQuality, reasons);

            if (sanitizedEvent.completedPurchase)
            {
                repeated += 0.045f + Mathf.Clamp01(current.purchaseCount / 20f) * 0.025f;
                if (generalStore)
                {
                    repeated += GeneralStorePurchaseHabitBonus;
                }
                AddDistinct(reasons, HouseholdAffinityReasonCode.PurchaseCompleted);
                if (current.visitCount > 0 || current.purchaseCount > 0)
                {
                    AddDistinct(reasons, HouseholdAffinityReasonCode.RepeatedUse);
                }
            }
            else
            {
                repeated -= 0.035f;
                AddDistinct(reasons, HouseholdAffinityReasonCode.VisitWithoutPurchase);
            }

            price += (sanitizedEvent.priceFairness01 - price) * 0.32f;
            stock += (sanitizedEvent.stockConsistency01 - stock) * 0.32f;
            trustQuality += (sanitizedEvent.trustQuality01 - trustQuality) * 0.28f;
            AddSignalReasons(reasons, sanitizedEvent);

            return BuildProposal(
                current,
                ResolveHouseholdId(current, sanitizedEvent),
                ResolveBusinessId(current, sanitizedEvent),
                sanitizedEvent.completedPurchase,
                repeated,
                price,
                stock,
                trustQuality,
                reasons);
        }

        public HouseholdAffinityChangeProposal ProposeDrift(HouseholdBusinessAffinityState state, int absoluteDayIndex)
        {
            HouseholdBusinessAffinityState current = GetState(state);
            List<HouseholdAffinityReasonCode> reasons = new List<HouseholdAffinityReasonCode>();
            float repeated = current.repeatedUse01;
            float price = current.priceFairnessMemory01;
            float stock = current.stockConsistencyMemory01;
            float trustQuality = current.trustQualityMemory01;
            ApplyDrift(current, absoluteDayIndex, IsGeneralStore(current), ref repeated, ref price, ref stock, ref trustQuality, reasons);

            return BuildProposal(
                current,
                current.householdId,
                current.businessId,
                false,
                repeated,
                price,
                stock,
                trustQuality,
                reasons);
        }

        public HouseholdBusinessAffinityState BuildUpdatedState(HouseholdBusinessAffinityState state, HouseholdAffinityEvent affinityEvent)
        {
            HouseholdBusinessAffinityState updated = GetState(state);
            HouseholdAffinityEvent sanitizedEvent = affinityEvent.Sanitized();
            HouseholdAffinityChangeProposal proposal = ProposeChange(updated, sanitizedEvent);

            updated.householdId = proposal.HouseholdId;
            updated.businessId = proposal.BusinessId;
            if (sanitizedEvent.hasBusinessType)
            {
                updated.hasBusinessType = true;
                updated.businessType = sanitizedEvent.businessType;
            }

            updated.visitCount++;
            if (sanitizedEvent.completedPurchase)
            {
                updated.purchaseCount++;
                updated.totalSpendCents += sanitizedEvent.spendCents;
            }

            updated.lastVisitDayIndex = sanitizedEvent.absoluteDayIndex >= 0
                ? sanitizedEvent.absoluteDayIndex
                : updated.lastVisitDayIndex;
            updated.repeatedUse01 = proposal.ProjectedRepeatedUse01;
            updated.priceFairnessMemory01 = proposal.ProjectedPriceFairness01;
            updated.stockConsistencyMemory01 = proposal.ProjectedStockConsistency01;
            updated.trustQualityMemory01 = proposal.ProjectedTrustQuality01;
            updated.cachedAffinity01 = proposal.ProjectedAffinity01;
            updated.lastTrend = proposal.Trend;
            updated.lastReasonCodes = new List<HouseholdAffinityReasonCode>(proposal.ReasonCodes);
            return updated;
        }

        public HouseholdAffinityScoreSummary Summarize(HouseholdBusinessAffinityState state)
        {
            HouseholdBusinessAffinityState current = GetState(state);
            float affinity = CalculateAffinity(
                current.repeatedUse01,
                current.priceFairnessMemory01,
                current.stockConsistencyMemory01,
                current.trustQualityMemory01);
            float preferenceWeight = CalculatePreferenceWeight(affinity);
            List<HouseholdAffinityReasonCode> reasons = new List<HouseholdAffinityReasonCode>();
            if (affinity >= PositiveSignalThreshold)
            {
                AddDistinct(reasons, HouseholdAffinityReasonCode.PreferenceStrength);
            }
            else if (affinity <= NegativeSignalThreshold)
            {
                AddDistinct(reasons, HouseholdAffinityReasonCode.PreferenceWeakness);
            }

            if (current.repeatedUse01 >= PositiveSignalThreshold)
            {
                AddDistinct(reasons, HouseholdAffinityReasonCode.RepeatedUse);
            }

            AddComponentSummaryReason(
                reasons,
                current.priceFairnessMemory01,
                HouseholdAffinityReasonCode.FairPrice,
                HouseholdAffinityReasonCode.HighPrice);
            AddComponentSummaryReason(
                reasons,
                current.stockConsistencyMemory01,
                HouseholdAffinityReasonCode.ConsistentStock,
                HouseholdAffinityReasonCode.Stockout);
            AddComponentSummaryReason(
                reasons,
                current.trustQualityMemory01,
                HouseholdAffinityReasonCode.TrustedQuality,
                HouseholdAffinityReasonCode.PoorQuality);

            return new HouseholdAffinityScoreSummary(
                current.householdId,
                current.businessId,
                current.hasBusinessType,
                current.businessType,
                affinity,
                preferenceWeight,
                current.repeatedUse01,
                current.priceFairnessMemory01,
                current.stockConsistencyMemory01,
                current.trustQualityMemory01,
                current.lastTrend,
                reasons);
        }

        private static HouseholdBusinessAffinityState GetState(HouseholdBusinessAffinityState state)
        {
            return state != null
                ? state.SanitizedCopy()
                : new HouseholdBusinessAffinityState().SanitizedCopy();
        }

        private static HouseholdAffinityChangeProposal BuildProposal(
            HouseholdBusinessAffinityState current,
            int householdId,
            string businessId,
            bool completedPurchase,
            float repeated,
            float price,
            float stock,
            float trustQuality,
            IReadOnlyList<HouseholdAffinityReasonCode> reasons)
        {
            float projectedRepeated = Mathf.Clamp01(repeated);
            float projectedPrice = Mathf.Clamp01(price);
            float projectedStock = Mathf.Clamp01(stock);
            float projectedTrustQuality = Mathf.Clamp01(trustQuality);
            float currentAffinity = CalculateAffinity(
                current.repeatedUse01,
                current.priceFairnessMemory01,
                current.stockConsistencyMemory01,
                current.trustQualityMemory01);
            float projectedAffinity = CalculateAffinity(projectedRepeated, projectedPrice, projectedStock, projectedTrustQuality);
            float affinityDelta = projectedAffinity - currentAffinity;
            RelationshipTrendDirection trend = EvaluateTrend(affinityDelta);

            return new HouseholdAffinityChangeProposal(
                householdId,
                businessId,
                completedPurchase,
                projectedRepeated - current.repeatedUse01,
                projectedPrice - current.priceFairnessMemory01,
                projectedStock - current.stockConsistencyMemory01,
                projectedTrustQuality - current.trustQualityMemory01,
                affinityDelta,
                projectedRepeated,
                projectedPrice,
                projectedStock,
                projectedTrustQuality,
                projectedAffinity,
                trend,
                reasons);
        }

        private static void ApplyDrift(
            HouseholdBusinessAffinityState current,
            int absoluteDayIndex,
            bool generalStore,
            ref float repeated,
            ref float price,
            ref float stock,
            ref float trustQuality,
            List<HouseholdAffinityReasonCode> reasons)
        {
            if (current.lastVisitDayIndex < 0 || absoluteDayIndex < 0 || absoluteDayIndex <= current.lastVisitDayIndex)
            {
                return;
            }

            int daysSinceVisit = absoluteDayIndex - current.lastVisitDayIndex;
            float driftStep = Mathf.Min(MaxSingleDriftStep, daysSinceVisit * DailyDriftStep) * (generalStore ? GeneralStoreDriftMultiplier : 1f);
            float beforeRepeated = repeated;
            float beforePrice = price;
            float beforeStock = stock;
            float beforeTrustQuality = trustQuality;
            repeated = Mathf.MoveTowards(repeated, NeutralMemory, driftStep);
            price = Mathf.MoveTowards(price, NeutralMemory, driftStep);
            stock = Mathf.MoveTowards(stock, NeutralMemory, driftStep);
            trustQuality = Mathf.MoveTowards(trustQuality, NeutralMemory, driftStep);

            if (!Mathf.Approximately(beforeRepeated, repeated)
                || !Mathf.Approximately(beforePrice, price)
                || !Mathf.Approximately(beforeStock, stock)
                || !Mathf.Approximately(beforeTrustQuality, trustQuality))
            {
                AddDistinct(reasons, HouseholdAffinityReasonCode.AffinityDrift);
            }
        }

        private static float CalculateAffinity(float repeated, float price, float stock, float trustQuality)
        {
            return Mathf.Clamp01(
                Mathf.Clamp01(repeated) * 0.28f
                + Mathf.Clamp01(price) * 0.24f
                + Mathf.Clamp01(stock) * 0.24f
                + Mathf.Clamp01(trustQuality) * 0.24f);
        }

        private static float CalculatePreferenceWeight(float affinity)
        {
            return Mathf.Clamp(0.5f + (Mathf.Clamp01(affinity) - 0.5f) * 0.75f, 0.15f, 0.85f);
        }

        private static RelationshipTrendDirection EvaluateTrend(float affinityDelta)
        {
            if (affinityDelta >= 0.005f)
            {
                return RelationshipTrendDirection.Improving;
            }

            if (affinityDelta <= -0.005f)
            {
                return RelationshipTrendDirection.Declining;
            }

            return RelationshipTrendDirection.Stable;
        }

        private static int ResolveHouseholdId(HouseholdBusinessAffinityState current, HouseholdAffinityEvent affinityEvent)
        {
            return affinityEvent.householdId >= 0 ? affinityEvent.householdId : current.householdId;
        }

        private static string ResolveBusinessId(HouseholdBusinessAffinityState current, HouseholdAffinityEvent affinityEvent)
        {
            return string.IsNullOrWhiteSpace(affinityEvent.businessId) ? current.businessId : affinityEvent.businessId;
        }

        private static bool IsGeneralStore(HouseholdBusinessAffinityState current)
        {
            return current != null && current.hasBusinessType && current.businessType == BusinessType.GeneralStore;
        }

        private static bool IsGeneralStore(HouseholdBusinessAffinityState current, HouseholdAffinityEvent affinityEvent)
        {
            if (affinityEvent.hasBusinessType)
            {
                return affinityEvent.businessType == BusinessType.GeneralStore;
            }

            return IsGeneralStore(current);
        }

        private static void AddSignalReasons(List<HouseholdAffinityReasonCode> reasons, HouseholdAffinityEvent affinityEvent)
        {
            AddSignalReason(
                reasons,
                affinityEvent.priceFairness01,
                HouseholdAffinityReasonCode.FairPrice,
                HouseholdAffinityReasonCode.HighPrice);
            AddSignalReason(
                reasons,
                affinityEvent.stockConsistency01,
                HouseholdAffinityReasonCode.ConsistentStock,
                HouseholdAffinityReasonCode.Stockout);
            AddSignalReason(
                reasons,
                affinityEvent.trustQuality01,
                HouseholdAffinityReasonCode.TrustedQuality,
                HouseholdAffinityReasonCode.PoorQuality);
        }

        private static void AddSignalReason(
            List<HouseholdAffinityReasonCode> reasons,
            float signal01,
            HouseholdAffinityReasonCode positive,
            HouseholdAffinityReasonCode negative)
        {
            if (signal01 >= PositiveSignalThreshold)
            {
                AddDistinct(reasons, positive);
            }
            else if (signal01 <= NegativeSignalThreshold)
            {
                AddDistinct(reasons, negative);
            }
        }

        private static void AddComponentSummaryReason(
            List<HouseholdAffinityReasonCode> reasons,
            float component01,
            HouseholdAffinityReasonCode positive,
            HouseholdAffinityReasonCode negative)
        {
            if (component01 >= PositiveSignalThreshold)
            {
                AddDistinct(reasons, positive);
            }
            else if (component01 <= NegativeSignalThreshold)
            {
                AddDistinct(reasons, negative);
            }
        }

        private static void AddDistinct(List<HouseholdAffinityReasonCode> reasons, HouseholdAffinityReasonCode reason)
        {
            if (reason != HouseholdAffinityReasonCode.None && !reasons.Contains(reason))
            {
                reasons.Add(reason);
            }
        }
    }
}
