using System;
using UnityEngine;

namespace LandLedgers.Progression
{
    [Serializable]
    public readonly struct OwnershipAptitudeResult
    {
        public OwnershipAptitudeResult(
            float acquisitionAptitude01,
            float operationsAptitude01,
            float peopleReputationAptitude01,
            float offerEstimateModifier01,
            float dueDiligenceReadModifier01,
            float hiringReadModifier01,
            float managerOversightModifier01)
        {
            AcquisitionAptitude01 = Mathf.Clamp01(acquisitionAptitude01);
            OperationsAptitude01 = Mathf.Clamp01(operationsAptitude01);
            PeopleReputationAptitude01 = Mathf.Clamp01(peopleReputationAptitude01);
            OfferEstimateModifier01 = Mathf.Clamp(offerEstimateModifier01, 0f, OwnershipAptitudeEvaluator.MaxOfferEstimateModifier01);
            DueDiligenceReadModifier01 = Mathf.Clamp(dueDiligenceReadModifier01, 0f, OwnershipAptitudeEvaluator.MaxDueDiligenceReadModifier01);
            HiringReadModifier01 = Mathf.Clamp(hiringReadModifier01, 0f, OwnershipAptitudeEvaluator.MaxHiringReadModifier01);
            ManagerOversightModifier01 = Mathf.Clamp(managerOversightModifier01, 0f, OwnershipAptitudeEvaluator.MaxManagerOversightModifier01);
        }

        public float AcquisitionAptitude01 { get; }
        public float OperationsAptitude01 { get; }
        public float PeopleReputationAptitude01 { get; }
        public float OfferEstimateModifier01 { get; }
        public float DueDiligenceReadModifier01 { get; }
        public float HiringReadModifier01 { get; }
        public float ManagerOversightModifier01 { get; }
    }

    [Serializable]
    public readonly struct OwnershipAptitudeSummary
    {
        public OwnershipAptitudeSummary(
            OwnershipAptitudeResult result,
            int acquisitionExperiencePoints,
            int operationsExperiencePoints,
            int peopleReputationExperiencePoints,
            int landPurchasedCount,
            int businessPurchasedCount,
            int parcelsDevelopedCount,
            int businessesOpenedCount,
            int turnaroundsCompletedCount,
            int ownershipMilestoneCount,
            int totalRecordedOwnershipEvents,
            int currentOwnedHoldings,
            int highestOwnershipMilestoneThresholdReached,
            bool hasNextOwnershipMilestone,
            int nextOwnershipMilestoneThreshold,
            int holdingsRemainingToNextMilestone)
            : this(
                result,
                acquisitionExperiencePoints,
                operationsExperiencePoints,
                peopleReputationExperiencePoints,
                landPurchasedCount,
                businessPurchasedCount,
                parcelsDevelopedCount,
                businessesOpenedCount,
                turnaroundsCompletedCount,
                ownershipMilestoneCount,
                totalRecordedOwnershipEvents,
                currentOwnedHoldings,
                highestOwnershipMilestoneThresholdReached,
                hasNextOwnershipMilestone,
                nextOwnershipMilestoneThreshold,
                holdingsRemainingToNextMilestone,
                0,
                false)
        {
        }

        public OwnershipAptitudeSummary(
            OwnershipAptitudeResult result,
            int acquisitionExperiencePoints,
            int operationsExperiencePoints,
            int peopleReputationExperiencePoints,
            int landPurchasedCount,
            int businessPurchasedCount,
            int parcelsDevelopedCount,
            int businessesOpenedCount,
            int turnaroundsCompletedCount,
            int ownershipMilestoneCount,
            int totalRecordedOwnershipEvents,
            int currentOwnedHoldings,
            int highestOwnershipMilestoneThresholdReached,
            bool hasNextOwnershipMilestone,
            int nextOwnershipMilestoneThreshold,
            int holdingsRemainingToNextMilestone,
            int pendingOwnershipMilestoneAwards,
            bool hasPendingOwnershipMilestoneAwards)
        {
            Result = result;
            AcquisitionExperiencePoints = Mathf.Clamp(acquisitionExperiencePoints, 0, OwnershipAptitudeState.MaxCategoryExperiencePoints);
            OperationsExperiencePoints = Mathf.Clamp(operationsExperiencePoints, 0, OwnershipAptitudeState.MaxCategoryExperiencePoints);
            PeopleReputationExperiencePoints = Mathf.Clamp(peopleReputationExperiencePoints, 0, OwnershipAptitudeState.MaxCategoryExperiencePoints);
            LandPurchasedCount = Mathf.Max(0, landPurchasedCount);
            BusinessPurchasedCount = Mathf.Max(0, businessPurchasedCount);
            ParcelsDevelopedCount = Mathf.Max(0, parcelsDevelopedCount);
            BusinessesOpenedCount = Mathf.Max(0, businessesOpenedCount);
            TurnaroundsCompletedCount = Mathf.Max(0, turnaroundsCompletedCount);
            OwnershipMilestoneCount = Mathf.Max(0, ownershipMilestoneCount);
            TotalRecordedOwnershipEvents = Mathf.Max(0, totalRecordedOwnershipEvents);
            CurrentOwnedHoldings = Mathf.Max(0, currentOwnedHoldings);
            HighestOwnershipMilestoneThresholdReached = Mathf.Max(0, highestOwnershipMilestoneThresholdReached);
            HasNextOwnershipMilestone = hasNextOwnershipMilestone;
            NextOwnershipMilestoneThreshold = Mathf.Max(0, nextOwnershipMilestoneThreshold);
            HoldingsRemainingToNextMilestone = Mathf.Max(0, holdingsRemainingToNextMilestone);
            PendingOwnershipMilestoneAwards = Mathf.Max(0, pendingOwnershipMilestoneAwards);
            HasPendingOwnershipMilestoneAwards = hasPendingOwnershipMilestoneAwards || PendingOwnershipMilestoneAwards > 0;
        }

        public OwnershipAptitudeResult Result { get; }
        public int AcquisitionExperiencePoints { get; }
        public int OperationsExperiencePoints { get; }
        public int PeopleReputationExperiencePoints { get; }
        public int LandPurchasedCount { get; }
        public int BusinessPurchasedCount { get; }
        public int ParcelsDevelopedCount { get; }
        public int BusinessesOpenedCount { get; }
        public int TurnaroundsCompletedCount { get; }
        public int OwnershipMilestoneCount { get; }
        public int TotalRecordedOwnershipEvents { get; }
        public int CurrentOwnedHoldings { get; }
        public int HighestOwnershipMilestoneThresholdReached { get; }
        public bool HasNextOwnershipMilestone { get; }
        public int NextOwnershipMilestoneThreshold { get; }
        public int HoldingsRemainingToNextMilestone { get; }
        public int PendingOwnershipMilestoneAwards { get; }
        public bool HasPendingOwnershipMilestoneAwards { get; }
        public bool HasAnyOwnershipProgress => TotalRecordedOwnershipEvents > 0 || CurrentOwnedHoldings > 0;
    }

    public sealed class OwnershipAptitudeEvaluator
    {
        public const float MaxOfferEstimateModifier01 = 0.03f;
        public const float MaxDueDiligenceReadModifier01 = 0.035f;
        public const float MaxHiringReadModifier01 = 0.035f;
        public const float MaxManagerOversightModifier01 = 0.025f;

        public const float AcquisitionAptitudeSaturationScale = 150f;
        public const float OperationsAptitudeSaturationScale = 150f;
        public const float PeopleReputationAptitudeSaturationScale = 140f;
        public const float DueDiligenceAcquisitionWeight = 0.65f;
        public const float DueDiligenceOperationsWeight = 0.35f;
        public const float HiringReadPeopleWeight = 0.75f;
        public const float HiringReadOperationsWeight = 0.25f;
        public const float ManagerOversightOperationsWeight = 0.7f;
        public const float ManagerOversightPeopleWeight = 0.3f;

        public OwnershipAptitudeResult Evaluate(OwnershipAptitudeState state)
        {
            if (state == null)
            {
                return new OwnershipAptitudeResult(0f, 0f, 0f, 0f, 0f, 0f, 0f);
            }

            float acquisition = SaturatingProgress(state.GetExperiencePoints(OwnershipAptitudeCategory.Acquisition), AcquisitionAptitudeSaturationScale);
            float operations = SaturatingProgress(state.GetExperiencePoints(OwnershipAptitudeCategory.Operations), OperationsAptitudeSaturationScale);
            float people = SaturatingProgress(state.GetExperiencePoints(OwnershipAptitudeCategory.PeopleReputation), PeopleReputationAptitudeSaturationScale);

            float offerEstimate = acquisition * MaxOfferEstimateModifier01;
            float dueDiligence = WeightedClamped(acquisition, DueDiligenceAcquisitionWeight, operations, DueDiligenceOperationsWeight) * MaxDueDiligenceReadModifier01;
            float hiringRead = WeightedClamped(people, HiringReadPeopleWeight, operations, HiringReadOperationsWeight) * MaxHiringReadModifier01;
            float managerOversight = WeightedClamped(operations, ManagerOversightOperationsWeight, people, ManagerOversightPeopleWeight) * MaxManagerOversightModifier01;

            return new OwnershipAptitudeResult(
                acquisition,
                operations,
                people,
                offerEstimate,
                dueDiligence,
                hiringRead,
                managerOversight);
        }

        public OwnershipAptitudeSummary BuildSummary(OwnershipAptitudeState state, int ownedLandCount, int ownedBusinessCount)
        {
            OwnershipAptitudeResult result = Evaluate(state);
            int currentOwnedHoldings = OwnershipAptitudeState.CalculateOwnedHoldings(ownedLandCount, ownedBusinessCount);

            if (state == null)
            {
                bool hasNext = TryGetNextMilestoneForNullState(currentOwnedHoldings, out int nextThreshold, out int remaining);
                int pending = CountMilestonesAtOrBelow(currentOwnedHoldings);
                return new OwnershipAptitudeSummary(
                    result,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    currentOwnedHoldings,
                    0,
                    hasNext,
                    nextThreshold,
                    remaining,
                    pending,
                    pending > 0);
            }

            // This is a readout surface for UI/tests. The state remains the only progression authority.
            bool hasNextMilestone = state.TryGetNextOwnershipMilestoneProgress(
                ownedLandCount,
                ownedBusinessCount,
                out currentOwnedHoldings,
                out int nextMilestoneThreshold,
                out int holdingsRemainingToNextMilestone);
            int pendingMilestones = state.GetPendingOwnershipMilestoneAwardCount(ownedLandCount, ownedBusinessCount);

            return new OwnershipAptitudeSummary(
                result,
                state.GetExperiencePoints(OwnershipAptitudeCategory.Acquisition),
                state.GetExperiencePoints(OwnershipAptitudeCategory.Operations),
                state.GetExperiencePoints(OwnershipAptitudeCategory.PeopleReputation),
                state.GetSourceCount(OwnershipAptitudeSource.LandPurchased),
                state.GetSourceCount(OwnershipAptitudeSource.BusinessPurchased),
                state.GetSourceCount(OwnershipAptitudeSource.ParcelDeveloped),
                state.GetSourceCount(OwnershipAptitudeSource.BusinessOpened),
                state.GetSourceCount(OwnershipAptitudeSource.TurnaroundCompleted),
                state.GetReachedOwnershipMilestoneCount(),
                state.GetTotalRecordedOwnershipEvents(),
                currentOwnedHoldings,
                state.GetHighestOwnershipMilestoneThresholdReached(),
                hasNextMilestone,
                nextMilestoneThreshold,
                holdingsRemainingToNextMilestone,
                pendingMilestones,
                pendingMilestones > 0);
        }

        private static float SaturatingProgress(float value, float scale)
        {
            if (scale <= 0f)
            {
                return 0f;
            }

            float clamped = Mathf.Max(0f, value);
            return Mathf.Clamp01(clamped / (clamped + scale));
        }

        private static float WeightedClamped(float firstValue, float firstWeight, float secondValue, float secondWeight)
        {
            return Mathf.Clamp01(firstValue * Mathf.Max(0f, firstWeight) + secondValue * Mathf.Max(0f, secondWeight));
        }

        private static bool TryGetNextMilestoneForNullState(int currentOwnedHoldings, out int nextMilestoneThreshold, out int holdingsRemaining)
        {
            for (int i = 0; i < OwnershipAptitudeState.OwnershipMilestoneThresholdCount; i++)
            {
                int threshold = OwnershipAptitudeState.GetOwnershipMilestoneThreshold(i);
                if (threshold <= 0)
                {
                    continue;
                }

                nextMilestoneThreshold = threshold;
                holdingsRemaining = Mathf.Max(0, threshold - currentOwnedHoldings);
                return true;
            }

            nextMilestoneThreshold = 0;
            holdingsRemaining = 0;
            return false;
        }

        private static int CountMilestonesAtOrBelow(int currentOwnedHoldings)
        {
            int count = 0;
            for (int i = 0; i < OwnershipAptitudeState.OwnershipMilestoneThresholdCount; i++)
            {
                int threshold = OwnershipAptitudeState.GetOwnershipMilestoneThreshold(i);
                if (threshold > 0 && currentOwnedHoldings >= threshold)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
