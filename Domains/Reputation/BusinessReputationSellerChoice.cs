using System;
using UnityEngine;

namespace LandLedgers.Reputation
{
    [Serializable]
    public readonly struct BusinessReputationSellerChoiceInput
    {
        public BusinessReputationSellerChoiceInput(
            BusinessReputationState reputation,
            float categoryStockHealth01,
            int availableUnits,
            int unitPriceCents,
            int referenceUnitPriceCents,
            float runtimeReliability01,
            float operatingEfficiency01,
            string displayName,
            string instanceId,
            int candidateIndex,
            string categoryId = null,
            float openAvailability01 = 1f,
            float distanceConvenience01 = 0.5f,
            float directRememberedPreference01 = 0.5f,
            float ownerReputationTieBreaker01 = 0.5f)
        {
            Reputation = reputation;
            CategoryStockHealth01 = Mathf.Clamp01(categoryStockHealth01);
            AvailableUnits = Mathf.Max(0, availableUnits);
            UnitPriceCents = Mathf.Max(0, unitPriceCents);
            ReferenceUnitPriceCents = Mathf.Max(0, referenceUnitPriceCents);
            RuntimeReliability01 = Mathf.Clamp01(runtimeReliability01);
            OperatingEfficiency01 = Mathf.Clamp01(operatingEfficiency01);
            DisplayName = displayName ?? string.Empty;
            InstanceId = instanceId ?? string.Empty;
            CandidateIndex = Mathf.Max(0, candidateIndex);
            CategoryId = BusinessReputationState.NormalizeStockoutCategoryId(categoryId);
            OpenAvailability01 = Mathf.Clamp01(openAvailability01);
            DistanceConvenience01 = Mathf.Clamp01(distanceConvenience01);
            DirectRememberedPreference01 = Mathf.Clamp01(directRememberedPreference01);
            OwnerReputationTieBreaker01 = Mathf.Clamp01(ownerReputationTieBreaker01);
        }

        public BusinessReputationState Reputation { get; }
        public float CategoryStockHealth01 { get; }
        public int AvailableUnits { get; }
        public int UnitPriceCents { get; }
        public int ReferenceUnitPriceCents { get; }
        public float RuntimeReliability01 { get; }
        public float OperatingEfficiency01 { get; }
        public string DisplayName { get; }
        public string InstanceId { get; }
        public int CandidateIndex { get; }
        public string CategoryId { get; }
        public float OpenAvailability01 { get; }
        public float DistanceConvenience01 { get; }
        public float DirectRememberedPreference01 { get; }
        public float OwnerReputationTieBreaker01 { get; }
    }

    [Serializable]
    public readonly struct BusinessReputationSellerChoiceScore
    {
        public BusinessReputationSellerChoiceScore(
            float score01,
            float reputationHeadline01,
            float categoryStockHealth01,
            float valueFairness01,
            float operatingReliability01,
            int availableUnits,
            int unitPriceCents,
            string displayName,
            string instanceId,
            int candidateIndex,
            bool eligible,
            float categoryTrust01 = 0.5f,
            float stockoutMemoryPressure01 = 0f,
            float openAvailability01 = 1f,
            float distanceConvenience01 = 0.5f,
            float directRememberedPreference01 = 0.5f,
            float ownerReputationTieBreaker01 = 0.5f)
        {
            Score01 = Mathf.Clamp01(score01);
            ReputationHeadline01 = Mathf.Clamp01(reputationHeadline01);
            CategoryStockHealth01 = Mathf.Clamp01(categoryStockHealth01);
            ValueFairness01 = Mathf.Clamp01(valueFairness01);
            OperatingReliability01 = Mathf.Clamp01(operatingReliability01);
            AvailableUnits = Mathf.Max(0, availableUnits);
            UnitPriceCents = Mathf.Max(0, unitPriceCents);
            DisplayName = displayName ?? string.Empty;
            InstanceId = instanceId ?? string.Empty;
            CandidateIndex = Mathf.Max(0, candidateIndex);
            Eligible = eligible;
            CategoryTrust01 = Mathf.Clamp01(categoryTrust01);
            StockoutMemoryPressure01 = Mathf.Clamp01(stockoutMemoryPressure01);
            OpenAvailability01 = Mathf.Clamp01(openAvailability01);
            DistanceConvenience01 = Mathf.Clamp01(distanceConvenience01);
            DirectRememberedPreference01 = Mathf.Clamp01(directRememberedPreference01);
            OwnerReputationTieBreaker01 = Mathf.Clamp01(ownerReputationTieBreaker01);
        }

        public float Score01 { get; }
        public int Score100 => Mathf.RoundToInt(Score01 * 100f);
        public float ReputationHeadline01 { get; }
        public float CategoryStockHealth01 { get; }
        public float ValueFairness01 { get; }
        public float OperatingReliability01 { get; }
        public float CategoryTrust01 { get; }
        public int CategoryTrust100 => Mathf.RoundToInt(CategoryTrust01 * 100f);
        public float StockoutMemoryPressure01 { get; }
        public int StockoutMemoryPressure100 => Mathf.RoundToInt(StockoutMemoryPressure01 * 100f);
        public float OpenAvailability01 { get; }
        public int OpenAvailability100 => Mathf.RoundToInt(OpenAvailability01 * 100f);
        public float DistanceConvenience01 { get; }
        public int DistanceConvenience100 => Mathf.RoundToInt(DistanceConvenience01 * 100f);
        public float DirectRememberedPreference01 { get; }
        public int DirectRememberedPreference100 => Mathf.RoundToInt(DirectRememberedPreference01 * 100f);
        public float OwnerReputationTieBreaker01 { get; }
        public int OwnerReputationTieBreaker100 => Mathf.RoundToInt(OwnerReputationTieBreaker01 * 100f);
        public int AvailableUnits { get; }
        public int UnitPriceCents { get; }
        public string DisplayName { get; }
        public string InstanceId { get; }
        public int CandidateIndex { get; }
        public bool Eligible { get; }
    }

    public sealed class BusinessReputationSellerChoice
    {
        private const float ReputationWeight = 0.25f;
        private const float StockHealthWeight = 0.19f;
        private const float ValueFairnessWeight = 0.12f;
        private const float OperatingReliabilityWeight = 0.13f;
        private const float CategoryTrustWeight = 0.12f;
        private const float OpenAvailabilityWeight = 0.08f;
        private const float DistanceConvenienceWeight = 0.06f;
        private const float RememberedPreferenceWeight = 0.04f;
        private const float OwnerReputationTieBreakerWeight = 0.01f;
        private const float StockoutMemoryPenaltyWeight = 0.3f;

        private readonly BusinessReputationEvaluator evaluator;

        public BusinessReputationSellerChoice()
            : this(new BusinessReputationEvaluator())
        {
        }

        public BusinessReputationSellerChoice(BusinessReputationEvaluator evaluator)
        {
            this.evaluator = evaluator ?? new BusinessReputationEvaluator();
        }

        public BusinessReputationSellerChoiceScore Evaluate(BusinessReputationSellerChoiceInput input)
        {
            bool eligible = input.AvailableUnits > 0
                && input.UnitPriceCents > 0
                && input.OperatingEfficiency01 > 0f
                && input.OpenAvailability01 > 0f;
            float reputation = evaluator.EvaluateHeadline01(input.Reputation);
            float valueFairness = CalculateValueFairness01(input.UnitPriceCents, input.ReferenceUnitPriceCents);
            float operatingReliability = Mathf.Clamp01(input.RuntimeReliability01 * 0.6f + input.OperatingEfficiency01 * 0.4f);
            float stockoutPenalty = input.Reputation != null ? input.Reputation.GetStockoutPressure01(input.CategoryId) : 0f;
            float categoryTrust = input.Reputation != null
                ? evaluator.EvaluateLocalTrust01(input.Reputation, input.CategoryId, input.CategoryStockHealth01)
                : Mathf.Clamp01(input.CategoryStockHealth01 * 0.45f
                    + operatingReliability * 0.25f
                    + valueFairness * 0.2f
                    + reputation * 0.1f
                    - stockoutPenalty * 0.5f);
            float score = eligible
                ? reputation * ReputationWeight
                    + input.CategoryStockHealth01 * StockHealthWeight
                    + valueFairness * ValueFairnessWeight
                    + operatingReliability * OperatingReliabilityWeight
                    + categoryTrust * CategoryTrustWeight
                    + input.OpenAvailability01 * OpenAvailabilityWeight
                    + input.DistanceConvenience01 * DistanceConvenienceWeight
                    + input.DirectRememberedPreference01 * RememberedPreferenceWeight
                    + input.OwnerReputationTieBreaker01 * OwnerReputationTieBreakerWeight
                    - stockoutPenalty * StockoutMemoryPenaltyWeight
                : 0f;

            return new BusinessReputationSellerChoiceScore(
                score,
                reputation,
                input.CategoryStockHealth01,
                valueFairness,
                operatingReliability,
                input.AvailableUnits,
                input.UnitPriceCents,
                input.DisplayName,
                input.InstanceId,
                input.CandidateIndex,
                eligible,
                categoryTrust,
                stockoutPenalty,
                input.OpenAvailability01,
                input.DistanceConvenience01,
                input.DirectRememberedPreference01,
                input.OwnerReputationTieBreaker01);
        }

        public static float CalculateValueFairness01(int unitPriceCents, int referenceUnitPriceCents)
        {
            int price = Mathf.Max(0, unitPriceCents);
            int reference = Mathf.Max(0, referenceUnitPriceCents);
            if (price <= 0 || reference <= 0)
            {
                return 0.5f;
            }

            float ratio = price / (float)reference;
            if (ratio <= 0.75f)
            {
                return 1f;
            }

            if (ratio <= 1f)
            {
                return Mathf.Lerp(1f, 0.72f, (ratio - 0.75f) / 0.25f);
            }

            if (ratio <= 1.5f)
            {
                return Mathf.Lerp(0.72f, 0.25f, (ratio - 1f) / 0.5f);
            }

            return Mathf.Clamp01(0.25f - (ratio - 1.5f) * 0.2f);
        }

        public static int Compare(BusinessReputationSellerChoiceScore left, BusinessReputationSellerChoiceScore right)
        {
            if (left.Eligible != right.Eligible)
            {
                return right.Eligible.CompareTo(left.Eligible);
            }

            int leftScore = Mathf.RoundToInt(left.Score01 * 10000f);
            int rightScore = Mathf.RoundToInt(right.Score01 * 10000f);
            int scoreCompare = rightScore.CompareTo(leftScore);
            if (scoreCompare != 0)
            {
                return scoreCompare;
            }

            int priceCompare = left.UnitPriceCents.CompareTo(right.UnitPriceCents);
            if (priceCompare != 0)
            {
                return priceCompare;
            }

            int convenienceCompare = Mathf.RoundToInt(right.DistanceConvenience01 * 10000f)
                .CompareTo(Mathf.RoundToInt(left.DistanceConvenience01 * 10000f));
            if (convenienceCompare != 0)
            {
                return convenienceCompare;
            }

            int preferenceCompare = Mathf.RoundToInt(right.DirectRememberedPreference01 * 10000f)
                .CompareTo(Mathf.RoundToInt(left.DirectRememberedPreference01 * 10000f));
            if (preferenceCompare != 0)
            {
                return preferenceCompare;
            }

            int displayCompare = string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
            if (displayCompare != 0)
            {
                return displayCompare;
            }

            int instanceCompare = string.Compare(left.InstanceId, right.InstanceId, StringComparison.OrdinalIgnoreCase);
            if (instanceCompare != 0)
            {
                return instanceCompare;
            }

            return left.CandidateIndex.CompareTo(right.CandidateIndex);
        }
    }
}
