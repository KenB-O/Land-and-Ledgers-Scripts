using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Reputation
{
    [Serializable]
    public readonly struct BusinessReputationObservation
    {
        public BusinessReputationObservation(
            int requestedUnits,
            int soldUnits,
            int unitPriceCents,
            int referenceUnitPriceCents,
            float stockHealthAfterSale01,
            float runtimeReliability01,
            float operatingEfficiency01,
            string categoryId = null,
            int absoluteDayIndex = -1,
            int weekKey = -1)
        {
            RequestedUnits = Mathf.Max(0, requestedUnits);
            SoldUnits = Mathf.Max(0, soldUnits);
            UnitPriceCents = Mathf.Max(0, unitPriceCents);
            ReferenceUnitPriceCents = Mathf.Max(0, referenceUnitPriceCents);
            StockHealthAfterSale01 = Mathf.Clamp01(stockHealthAfterSale01);
            RuntimeReliability01 = Mathf.Clamp01(runtimeReliability01);
            OperatingEfficiency01 = Mathf.Clamp01(operatingEfficiency01);
            CategoryId = BusinessReputationState.NormalizeStockoutCategoryId(categoryId);
            AbsoluteDayIndex = absoluteDayIndex;
            WeekKey = weekKey;
        }

        public int RequestedUnits { get; }
        public int SoldUnits { get; }
        public int UnitPriceCents { get; }
        public int ReferenceUnitPriceCents { get; }
        public float StockHealthAfterSale01 { get; }
        public float RuntimeReliability01 { get; }
        public float OperatingEfficiency01 { get; }
        public string CategoryId { get; }
        public int AbsoluteDayIndex { get; }
        public int WeekKey { get; }
        public float FillRate01 => RequestedUnits <= 0 ? 0f : Mathf.Clamp01(SoldUnits / (float)RequestedUnits);
    }


    public enum BusinessReputationSubcategory
    {
        StockReliability,
        ValueFairness,
        ServiceExperience,
        ConditionPresentationTrust,
        ProductTradeConfidence,
        StockoutPressure
    }

    public enum BusinessReputationChangeReasonCode
    {
        None,
        ObservationIgnored,
        FullyFilledSale,
        PartialStockout,
        SevereStockout,
        StockoutMemoryPressure,
        StockoutRecovery,
        FairPricing,
        OverpricingConcern,
        WeakOperatingReliability,
        StrongOperatingReliability,
        LowStockHealth,
        StrongStockHealth,
        ReputationImproved,
        ReputationDamaged
    }

    [Serializable]
    public readonly struct BusinessReputationSubcategoryDelta
    {
        public BusinessReputationSubcategoryDelta(BusinessReputationSubcategory subcategory, float before01, float after01)
        {
            this.subcategory = subcategory;
            this.before01 = Mathf.Clamp01(before01);
            this.after01 = Mathf.Clamp01(after01);
        }

        public readonly BusinessReputationSubcategory subcategory;
        public readonly float before01;
        public readonly float after01;
        public float Delta01 => after01 - before01;
        public int Delta100 => Mathf.RoundToInt(Delta01 * 100f);
        public bool IsImprovement => Delta01 > 0.0001f;
        public bool IsDamage => Delta01 < -0.0001f;
    }

    [Serializable]
    public sealed class BusinessReputationChangeResult
    {
        public BusinessReputationChangeResult(
            bool applied,
            string categoryId,
            int requestedUnits,
            int soldUnits,
            float stockoutPressureBefore01,
            float stockoutPressureAfter01,
            float headlineBefore01,
            float headlineAfter01,
            BusinessReputationState before,
            BusinessReputationState after,
            IReadOnlyList<BusinessReputationSubcategoryDelta> deltas,
            IReadOnlyList<BusinessReputationChangeReasonCode> reasonCodes)
        {
            Applied = applied;
            CategoryId = BusinessReputationState.NormalizeStockoutCategoryId(categoryId);
            RequestedUnits = Mathf.Max(0, requestedUnits);
            SoldUnits = Mathf.Max(0, soldUnits);
            StockoutPressureBefore01 = Mathf.Clamp01(stockoutPressureBefore01);
            StockoutPressureAfter01 = Mathf.Clamp01(stockoutPressureAfter01);
            HeadlineBefore01 = Mathf.Clamp01(headlineBefore01);
            HeadlineAfter01 = Mathf.Clamp01(headlineAfter01);
            Before = before ?? new BusinessReputationState();
            After = after ?? new BusinessReputationState();
            Deltas = deltas ?? Array.Empty<BusinessReputationSubcategoryDelta>();
            ReasonCodes = reasonCodes ?? Array.Empty<BusinessReputationChangeReasonCode>();
        }

        public bool Applied { get; }
        public string CategoryId { get; }
        public int RequestedUnits { get; }
        public int SoldUnits { get; }
        public float FillRate01 => RequestedUnits <= 0 ? 0f : Mathf.Clamp01(SoldUnits / (float)RequestedUnits);
        public int FillRate100 => Mathf.RoundToInt(FillRate01 * 100f);
        public float StockoutPressureBefore01 { get; }
        public float StockoutPressureAfter01 { get; }
        public float StockoutPressureDelta01 => StockoutPressureAfter01 - StockoutPressureBefore01;
        public int StockoutPressureAfter100 => Mathf.RoundToInt(StockoutPressureAfter01 * 100f);
        public float HeadlineBefore01 { get; }
        public float HeadlineAfter01 { get; }
        public float HeadlineDelta01 => HeadlineAfter01 - HeadlineBefore01;
        public int HeadlineBefore100 => Mathf.RoundToInt(HeadlineBefore01 * 100f);
        public int HeadlineAfter100 => Mathf.RoundToInt(HeadlineAfter01 * 100f);
        public int HeadlineDelta100 => Mathf.RoundToInt(HeadlineDelta01 * 100f);
        public bool HasChange => Mathf.Abs(HeadlineDelta01) > 0.0001f || Mathf.Abs(StockoutPressureDelta01) > 0.0001f || Deltas.Count > 0;
        public bool IsImprovement => HeadlineDelta01 > 0.0001f;
        public bool IsDamage => HeadlineDelta01 < -0.0001f;
        public BusinessReputationState Before { get; }
        public BusinessReputationState After { get; }
        public IReadOnlyList<BusinessReputationSubcategoryDelta> Deltas { get; }
        public IReadOnlyList<BusinessReputationChangeReasonCode> ReasonCodes { get; }
        public BusinessReputationChangeReasonCode PrimaryReasonCode => ReasonCodes.Count > 0 ? ReasonCodes[0] : BusinessReputationChangeReasonCode.None;
        public BusinessReputationSubcategoryDelta StrongestAbsoluteDelta => FindStrongestDelta(DeltaSelection.Absolute);
        public BusinessReputationSubcategoryDelta StrongestPositiveDelta => FindStrongestDelta(DeltaSelection.Positive);
        public BusinessReputationSubcategoryDelta StrongestNegativeDelta => FindStrongestDelta(DeltaSelection.Negative);

        public bool HasReason(BusinessReputationChangeReasonCode reasonCode)
        {
            for (int i = 0; i < ReasonCodes.Count; i++)
            {
                if (ReasonCodes[i] == reasonCode)
                {
                    return true;
                }
            }

            return false;
        }

        public float GetDelta01(BusinessReputationSubcategory subcategory)
        {
            return TryGetDelta(subcategory, out BusinessReputationSubcategoryDelta delta) ? delta.Delta01 : 0f;
        }

        public bool TryGetDelta(BusinessReputationSubcategory subcategory, out BusinessReputationSubcategoryDelta delta)
        {
            for (int i = 0; i < Deltas.Count; i++)
            {
                if (Deltas[i].subcategory == subcategory)
                {
                    delta = Deltas[i];
                    return true;
                }
            }

            delta = default;
            return false;
        }

        private BusinessReputationSubcategoryDelta FindStrongestDelta(DeltaSelection selection)
        {
            BusinessReputationSubcategoryDelta strongest = default;
            float strongestMagnitude = 0f;

            for (int i = 0; i < Deltas.Count; i++)
            {
                BusinessReputationSubcategoryDelta candidate = Deltas[i];
                float delta = candidate.Delta01;
                if (selection == DeltaSelection.Positive && delta <= 0f)
                {
                    continue;
                }

                if (selection == DeltaSelection.Negative && delta >= 0f)
                {
                    continue;
                }

                float magnitude = Mathf.Abs(delta);
                if (magnitude > strongestMagnitude)
                {
                    strongest = candidate;
                    strongestMagnitude = magnitude;
                }
            }

            return strongest;
        }

        private enum DeltaSelection
        {
            Absolute,
            Positive,
            Negative
        }
    }

    public sealed class BusinessReputationEvaluator
    {
        private const float StockReliabilityWeight = 0.25f;
        private const float ValueFairnessWeight = 0.2f;
        private const float ServiceExperienceWeight = 0.2f;
        private const float ConditionPresentationTrustWeight = 0.15f;
        private const float ProductTradeConfidenceWeight = 0.2f;
        private const float ObservationResponse = 0.18f;
        private const float StockoutPenaltyResponse = 0.08f;
        private const float StockoutRecoveryResponse = 0.035f;

        public float EvaluateHeadline01(BusinessReputationState state)
        {
            if (state == null)
            {
                return 0.5f;
            }

            return Mathf.Clamp01(
                Mathf.Clamp01(state.stockReliability01) * StockReliabilityWeight
                + Mathf.Clamp01(state.valueFairness01) * ValueFairnessWeight
                + Mathf.Clamp01(state.serviceExperience01) * ServiceExperienceWeight
                + Mathf.Clamp01(state.conditionPresentationTrust01) * ConditionPresentationTrustWeight
                + Mathf.Clamp01(state.productTradeConfidence01) * ProductTradeConfidenceWeight);
        }

        public int EvaluateHeadline100(BusinessReputationState state)
        {
            return Mathf.RoundToInt(EvaluateHeadline01(state) * 100f);
        }

        public float EvaluateLocalTrust01(BusinessReputationState state, string categoryId, float stockConsistency01 = -1f)
        {
            if (state == null)
            {
                return 0.5f;
            }

            float stockConsistency = stockConsistency01 < 0f
                ? Mathf.Clamp01(state.stockReliability01)
                : Mathf.Clamp01(stockConsistency01);
            float stockoutPressure = state.GetStockoutPressure01(categoryId);
            return Mathf.Clamp01(
                Mathf.Clamp01(state.serviceExperience01) * 0.27f
                + Mathf.Clamp01(state.valueFairness01) * 0.16f
                + Mathf.Clamp01(state.conditionPresentationTrust01) * 0.15f
                + Mathf.Clamp01(state.productTradeConfidence01) * 0.22f
                + stockConsistency * 0.2f
                - stockoutPressure * 0.34f);
        }

        public BusinessReputationState BuildInitialState(float stockHealth01, float runtimeReliability01, float operatingEfficiency01)
        {
            BusinessReputationState state = new BusinessReputationState();
            state.ApplyRuntimeBaseline(stockHealth01, runtimeReliability01, operatingEfficiency01);
            return state;
        }

        public void ApplyObservation(BusinessReputationState state, BusinessReputationObservation observation)
        {
            ApplyObservationWithResult(state, observation);
        }

        public BusinessReputationChangeResult ApplyObservationWithResult(BusinessReputationState state, BusinessReputationObservation observation)
        {
            if (state == null)
            {
                return new BusinessReputationChangeResult(
                    false,
                    observation.CategoryId,
                    observation.RequestedUnits,
                    observation.SoldUnits,
                    0f,
                    0f,
                    0.5f,
                    0.5f,
                    null,
                    null,
                    Array.Empty<BusinessReputationSubcategoryDelta>(),
                    new[] { BusinessReputationChangeReasonCode.ObservationIgnored });
            }

            state.Clamp();
            BusinessReputationState before = state.Clone();
            float headlineBefore = EvaluateHeadline01(before);
            float stockoutPressureBefore = before.GetStockoutPressure01(observation.CategoryId);

            float fillRate = observation.FillRate01;
            if (observation.RequestedUnits > 0 && observation.SoldUnits < observation.RequestedUnits)
            {
                float severity = Mathf.Clamp01((observation.RequestedUnits - observation.SoldUnits) / (float)observation.RequestedUnits);
                state.RecordStockout(observation.CategoryId, observation.AbsoluteDayIndex, observation.WeekKey, severity);
            }
            else if (observation.RequestedUnits > 0)
            {
                state.RecordStockoutRecovery(observation.CategoryId, fillRate);
            }

            float stockoutPressure = state.GetStockoutPressure01(observation.CategoryId);
            float valueFairness = BusinessReputationSellerChoice.CalculateValueFairness01(
                observation.UnitPriceCents,
                observation.ReferenceUnitPriceCents);
            float operatingReliability = Mathf.Clamp01(
                observation.RuntimeReliability01 * 0.6f
                + observation.OperatingEfficiency01 * 0.4f);
            float stockSignal = Mathf.Clamp01(observation.StockHealthAfterSale01 * 0.65f + fillRate * 0.35f - stockoutPressure * 0.35f);
            float serviceSignal = Mathf.Clamp01(fillRate * 0.6f + observation.OperatingEfficiency01 * 0.4f);
            float conditionSignal = Mathf.Clamp01(observation.RuntimeReliability01 * 0.7f + observation.OperatingEfficiency01 * 0.3f);
            float productSignal = Mathf.Clamp01(
                observation.StockHealthAfterSale01 * 0.3f
                + fillRate * 0.3f
                + valueFairness * 0.25f
                + operatingReliability * 0.15f
                - stockoutPressure * 0.25f);

            state.stockReliability01 = BlendToward(state.stockReliability01, stockSignal);
            state.valueFairness01 = BlendToward(state.valueFairness01, valueFairness);
            state.serviceExperience01 = BlendToward(state.serviceExperience01, serviceSignal);
            state.conditionPresentationTrust01 = BlendToward(state.conditionPresentationTrust01, conditionSignal);
            state.productTradeConfidence01 = BlendToward(state.productTradeConfidence01, productSignal);
            if (stockoutPressure > 0f)
            {
                state.stockReliability01 = Mathf.Clamp01(state.stockReliability01 - stockoutPressure * StockoutPenaltyResponse);
                state.productTradeConfidence01 = Mathf.Clamp01(state.productTradeConfidence01 - stockoutPressure * StockoutPenaltyResponse * 0.75f);
            }
            else if (fillRate >= 0.95f)
            {
                state.stockReliability01 = Mathf.Clamp01(state.stockReliability01 + StockoutRecoveryResponse);
                state.productTradeConfidence01 = Mathf.Clamp01(state.productTradeConfidence01 + StockoutRecoveryResponse * 0.5f);
            }

            state.initializedFromRuntime = true;
            state.Clamp();

            BusinessReputationState after = state.Clone();
            float headlineAfter = EvaluateHeadline01(after);
            float stockoutPressureAfter = after.GetStockoutPressure01(observation.CategoryId);
            List<BusinessReputationSubcategoryDelta> deltas = BuildDeltas(before, after, stockoutPressureBefore, stockoutPressureAfter);
            List<BusinessReputationChangeReasonCode> reasons = BuildReasonCodes(
                observation,
                fillRate,
                valueFairness,
                operatingReliability,
                stockoutPressureBefore,
                stockoutPressureAfter,
                headlineBefore,
                headlineAfter);

            return new BusinessReputationChangeResult(
                true,
                observation.CategoryId,
                observation.RequestedUnits,
                observation.SoldUnits,
                stockoutPressureBefore,
                stockoutPressureAfter,
                headlineBefore,
                headlineAfter,
                before,
                after,
                deltas,
                reasons);
        }

        private static List<BusinessReputationSubcategoryDelta> BuildDeltas(
            BusinessReputationState before,
            BusinessReputationState after,
            float stockoutPressureBefore01,
            float stockoutPressureAfter01)
        {
            List<BusinessReputationSubcategoryDelta> deltas = new List<BusinessReputationSubcategoryDelta>();
            AddDelta(deltas, BusinessReputationSubcategory.StockReliability, before.stockReliability01, after.stockReliability01);
            AddDelta(deltas, BusinessReputationSubcategory.ValueFairness, before.valueFairness01, after.valueFairness01);
            AddDelta(deltas, BusinessReputationSubcategory.ServiceExperience, before.serviceExperience01, after.serviceExperience01);
            AddDelta(deltas, BusinessReputationSubcategory.ConditionPresentationTrust, before.conditionPresentationTrust01, after.conditionPresentationTrust01);
            AddDelta(deltas, BusinessReputationSubcategory.ProductTradeConfidence, before.productTradeConfidence01, after.productTradeConfidence01);
            AddDelta(deltas, BusinessReputationSubcategory.StockoutPressure, stockoutPressureBefore01, stockoutPressureAfter01);
            return deltas;
        }

        private static void AddDelta(List<BusinessReputationSubcategoryDelta> deltas, BusinessReputationSubcategory subcategory, float before01, float after01)
        {
            if (!Mathf.Approximately(Mathf.Clamp01(before01), Mathf.Clamp01(after01)))
            {
                deltas.Add(new BusinessReputationSubcategoryDelta(subcategory, before01, after01));
            }
        }

        private static List<BusinessReputationChangeReasonCode> BuildReasonCodes(
            BusinessReputationObservation observation,
            float fillRate,
            float valueFairness01,
            float operatingReliability01,
            float stockoutPressureBefore01,
            float stockoutPressureAfter01,
            float headlineBefore01,
            float headlineAfter01)
        {
            List<BusinessReputationChangeReasonCode> reasons = new List<BusinessReputationChangeReasonCode>();
            if (observation.RequestedUnits > 0 && observation.SoldUnits >= observation.RequestedUnits)
            {
                reasons.Add(BusinessReputationChangeReasonCode.FullyFilledSale);
            }
            else if (observation.RequestedUnits > 0 && observation.SoldUnits < observation.RequestedUnits)
            {
                float missedShare = Mathf.Clamp01((observation.RequestedUnits - observation.SoldUnits) / (float)observation.RequestedUnits);
                reasons.Add(missedShare >= 0.5f
                    ? BusinessReputationChangeReasonCode.SevereStockout
                    : BusinessReputationChangeReasonCode.PartialStockout);
            }

            if (stockoutPressureAfter01 > stockoutPressureBefore01 + 0.0001f)
            {
                reasons.Add(BusinessReputationChangeReasonCode.StockoutMemoryPressure);
            }
            else if (stockoutPressureAfter01 < stockoutPressureBefore01 - 0.0001f)
            {
                reasons.Add(BusinessReputationChangeReasonCode.StockoutRecovery);
            }

            if (valueFairness01 >= 0.72f)
            {
                reasons.Add(BusinessReputationChangeReasonCode.FairPricing);
            }
            else if (valueFairness01 <= 0.38f)
            {
                reasons.Add(BusinessReputationChangeReasonCode.OverpricingConcern);
            }

            if (operatingReliability01 <= 0.35f)
            {
                reasons.Add(BusinessReputationChangeReasonCode.WeakOperatingReliability);
            }
            else if (operatingReliability01 >= 0.78f)
            {
                reasons.Add(BusinessReputationChangeReasonCode.StrongOperatingReliability);
            }

            if (observation.StockHealthAfterSale01 <= 0.25f)
            {
                reasons.Add(BusinessReputationChangeReasonCode.LowStockHealth);
            }
            else if (observation.StockHealthAfterSale01 >= 0.75f && fillRate >= 0.95f)
            {
                reasons.Add(BusinessReputationChangeReasonCode.StrongStockHealth);
            }

            if (headlineAfter01 > headlineBefore01 + 0.0001f)
            {
                reasons.Add(BusinessReputationChangeReasonCode.ReputationImproved);
            }
            else if (headlineAfter01 < headlineBefore01 - 0.0001f)
            {
                reasons.Add(BusinessReputationChangeReasonCode.ReputationDamaged);
            }

            return reasons;
        }

        private static float BlendToward(float current, float target)
        {
            return Mathf.Clamp01(current + (Mathf.Clamp01(target) - Mathf.Clamp01(current)) * ObservationResponse);
        }
    }
}
