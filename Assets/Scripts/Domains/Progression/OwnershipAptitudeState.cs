using System;
using LandLedgers.Persistence;
using UnityEngine;

namespace LandLedgers.Progression
{
    public enum OwnershipAptitudeCategory
    {
        Acquisition = 0,
        Operations = 1,
        PeopleReputation = 2
    }

    public enum OwnershipAptitudeSource
    {
        LandPurchased = 0,
        BusinessPurchased = 1,
        ParcelDeveloped = 2,
        BusinessOpened = 3,
        TurnaroundCompleted = 4,
        OwnershipMilestone = 5
    }

    [Serializable]
    public sealed class OwnershipAptitudeState
    {
        public const int MaxCategoryExperiencePoints = 500;

        public const int LandPurchasedAcquisitionExperiencePoints = 10;
        public const int BusinessPurchasedAcquisitionExperiencePoints = 16;
        public const int ParcelDevelopedOperationsExperiencePoints = 12;
        public const int BusinessOpenedOperationsExperiencePoints = 14;
        public const int BusinessOpenedPeopleJudgmentExperiencePoints = 4;
        public const int TurnaroundCompletedOperationsExperiencePoints = 18;
        public const int TurnaroundCompletedPeopleJudgmentExperiencePoints = 10;
        public const int OwnershipMilestoneAcquisitionExperiencePoints = 4;
        public const int OwnershipMilestoneOperationsExperiencePoints = 4;
        public const int OwnershipMilestonePeopleJudgmentExperiencePoints = 8;

        private static readonly int[] OwnershipMilestoneThresholds =
        {
            1,
            3,
            5,
            8,
            12
        };

        public int acquisitionExperiencePoints;
        public int operationsExperiencePoints;

        // This is owner people-judgment aptitude, not the broader Owner Reputation or Business Reputation authority.
        public int peopleReputationExperiencePoints;

        public int landPurchasedCount;
        public int businessPurchasedCount;
        public int parcelsDevelopedCount;
        public int businessesOpenedCount;
        public int turnaroundsCompletedCount;
        public int ownershipMilestonesReached;
        public int highestOwnershipMilestoneCount;

        public static int OwnershipMilestoneThresholdCount => OwnershipMilestoneThresholds.Length;

        public int GetExperiencePoints(OwnershipAptitudeCategory category)
        {
            return category switch
            {
                OwnershipAptitudeCategory.Acquisition => Mathf.Clamp(acquisitionExperiencePoints, 0, MaxCategoryExperiencePoints),
                OwnershipAptitudeCategory.Operations => Mathf.Clamp(operationsExperiencePoints, 0, MaxCategoryExperiencePoints),
                OwnershipAptitudeCategory.PeopleReputation => Mathf.Clamp(peopleReputationExperiencePoints, 0, MaxCategoryExperiencePoints),
                _ => 0
            };
        }

        public int GetSourceCount(OwnershipAptitudeSource source)
        {
            return source switch
            {
                OwnershipAptitudeSource.LandPurchased => Mathf.Max(0, landPurchasedCount),
                OwnershipAptitudeSource.BusinessPurchased => Mathf.Max(0, businessPurchasedCount),
                OwnershipAptitudeSource.ParcelDeveloped => Mathf.Max(0, parcelsDevelopedCount),
                OwnershipAptitudeSource.BusinessOpened => Mathf.Max(0, businessesOpenedCount),
                OwnershipAptitudeSource.TurnaroundCompleted => Mathf.Max(0, turnaroundsCompletedCount),
                OwnershipAptitudeSource.OwnershipMilestone => Mathf.Max(0, ownershipMilestonesReached),
                _ => 0
            };
        }

        public int GetTotalRecordedOwnershipEvents()
        {
            long total = 0;
            total += Mathf.Max(0, landPurchasedCount);
            total += Mathf.Max(0, businessPurchasedCount);
            total += Mathf.Max(0, parcelsDevelopedCount);
            total += Mathf.Max(0, businessesOpenedCount);
            total += Mathf.Max(0, turnaroundsCompletedCount);
            total += Mathf.Max(0, ownershipMilestonesReached);
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        public static int GetOwnershipMilestoneThreshold(int index)
        {
            if (index < 0 || index >= OwnershipMilestoneThresholds.Length)
            {
                return 0;
            }

            return OwnershipMilestoneThresholds[index];
        }

        public static int GetMaxOwnershipMilestoneThreshold()
        {
            return OwnershipMilestoneThresholds.Length == 0 ? 0 : OwnershipMilestoneThresholds[OwnershipMilestoneThresholds.Length - 1];
        }

        public int GetReachedOwnershipMilestoneCount()
        {
            Sanitize();
            return Mathf.Clamp(ownershipMilestonesReached, 0, OwnershipMilestoneThresholds.Length);
        }

        public int GetHighestOwnershipMilestoneThresholdReached()
        {
            Sanitize();
            return highestOwnershipMilestoneCount;
        }

        public bool TryGetNextOwnershipMilestoneProgress(
            int ownedLandCount,
            int ownedBusinessCount,
            out int currentOwnedHoldings,
            out int nextMilestoneThreshold,
            out int holdingsRemaining)
        {
            Sanitize();
            currentOwnedHoldings = CalculateOwnedHoldings(ownedLandCount, ownedBusinessCount);
            holdingsRemaining = 0;

            for (int i = 0; i < OwnershipMilestoneThresholds.Length; i++)
            {
                int threshold = OwnershipMilestoneThresholds[i];
                if (threshold <= highestOwnershipMilestoneCount)
                {
                    continue;
                }

                nextMilestoneThreshold = threshold;
                holdingsRemaining = Mathf.Max(0, threshold - currentOwnedHoldings);
                return true;
            }

            nextMilestoneThreshold = 0;
            return false;
        }

        public int GetPendingOwnershipMilestoneAwardCount(int ownedLandCount, int ownedBusinessCount)
        {
            Sanitize();
            int totalOwnedHoldings = CalculateOwnedHoldings(ownedLandCount, ownedBusinessCount);
            int pending = 0;

            for (int i = 0; i < OwnershipMilestoneThresholds.Length; i++)
            {
                int threshold = OwnershipMilestoneThresholds[i];
                if (threshold > highestOwnershipMilestoneCount && totalOwnedHoldings >= threshold)
                {
                    pending++;
                }
            }

            return pending;
        }

        public bool HasPendingOwnershipMilestoneAward(int ownedLandCount, int ownedBusinessCount)
        {
            return GetPendingOwnershipMilestoneAwardCount(ownedLandCount, ownedBusinessCount) > 0;
        }

        public void RecordSource(OwnershipAptitudeSource source)
        {
            switch (source)
            {
                case OwnershipAptitudeSource.LandPurchased:
                    landPurchasedCount = IncrementCounter(landPurchasedCount);
                    AddExperience(OwnershipAptitudeCategory.Acquisition, LandPurchasedAcquisitionExperiencePoints);
                    break;

                case OwnershipAptitudeSource.BusinessPurchased:
                    businessPurchasedCount = IncrementCounter(businessPurchasedCount);
                    AddExperience(OwnershipAptitudeCategory.Acquisition, BusinessPurchasedAcquisitionExperiencePoints);
                    break;

                case OwnershipAptitudeSource.ParcelDeveloped:
                    parcelsDevelopedCount = IncrementCounter(parcelsDevelopedCount);
                    AddExperience(OwnershipAptitudeCategory.Operations, ParcelDevelopedOperationsExperiencePoints);
                    break;

                case OwnershipAptitudeSource.BusinessOpened:
                    businessesOpenedCount = IncrementCounter(businessesOpenedCount);
                    AddExperience(OwnershipAptitudeCategory.Operations, BusinessOpenedOperationsExperiencePoints);
                    AddExperience(OwnershipAptitudeCategory.PeopleReputation, BusinessOpenedPeopleJudgmentExperiencePoints);
                    break;

                case OwnershipAptitudeSource.TurnaroundCompleted:
                    turnaroundsCompletedCount = IncrementCounter(turnaroundsCompletedCount);
                    AddExperience(OwnershipAptitudeCategory.Operations, TurnaroundCompletedOperationsExperiencePoints);
                    AddExperience(OwnershipAptitudeCategory.PeopleReputation, TurnaroundCompletedPeopleJudgmentExperiencePoints);
                    break;

                case OwnershipAptitudeSource.OwnershipMilestone:
                    ownershipMilestonesReached = IncrementCounter(ownershipMilestonesReached);
                    AddExperience(OwnershipAptitudeCategory.Acquisition, OwnershipMilestoneAcquisitionExperiencePoints);
                    AddExperience(OwnershipAptitudeCategory.Operations, OwnershipMilestoneOperationsExperiencePoints);
                    AddExperience(OwnershipAptitudeCategory.PeopleReputation, OwnershipMilestonePeopleJudgmentExperiencePoints);
                    break;
            }

            Sanitize();
        }

        public int RecordOwnershipMilestones(int ownedLandCount, int ownedBusinessCount)
        {
            Sanitize();
            int totalOwnedHoldings = CalculateOwnedHoldings(ownedLandCount, ownedBusinessCount);
            int awarded = 0;
            for (int i = 0; i < OwnershipMilestoneThresholds.Length; i++)
            {
                int threshold = OwnershipMilestoneThresholds[i];
                if (threshold <= highestOwnershipMilestoneCount || totalOwnedHoldings < threshold)
                {
                    continue;
                }

                highestOwnershipMilestoneCount = threshold;
                RecordSource(OwnershipAptitudeSource.OwnershipMilestone);
                awarded++;
            }

            Sanitize();
            return awarded;
        }

        public OwnershipAptitudeSaveDto CaptureSaveDto()
        {
            Sanitize();
            return new OwnershipAptitudeSaveDto
            {
                acquisitionExperiencePoints = acquisitionExperiencePoints,
                operationsExperiencePoints = operationsExperiencePoints,
                peopleReputationExperiencePoints = peopleReputationExperiencePoints,
                landPurchasedCount = landPurchasedCount,
                businessPurchasedCount = businessPurchasedCount,
                parcelsDevelopedCount = parcelsDevelopedCount,
                businessesOpenedCount = businessesOpenedCount,
                turnaroundsCompletedCount = turnaroundsCompletedCount,
                ownershipMilestonesReached = ownershipMilestonesReached,
                highestOwnershipMilestoneCount = highestOwnershipMilestoneCount
            };
        }

        public void LoadFromSaveDto(OwnershipAptitudeSaveDto dto)
        {
            if (dto == null)
            {
                Clear();
                return;
            }

            acquisitionExperiencePoints = dto.acquisitionExperiencePoints;
            operationsExperiencePoints = dto.operationsExperiencePoints;
            peopleReputationExperiencePoints = dto.peopleReputationExperiencePoints;
            landPurchasedCount = dto.landPurchasedCount;
            businessPurchasedCount = dto.businessPurchasedCount;
            parcelsDevelopedCount = dto.parcelsDevelopedCount;
            businessesOpenedCount = dto.businessesOpenedCount;
            turnaroundsCompletedCount = dto.turnaroundsCompletedCount;
            ownershipMilestonesReached = dto.ownershipMilestonesReached;
            highestOwnershipMilestoneCount = dto.highestOwnershipMilestoneCount;
            Sanitize();
        }

        public static OwnershipAptitudeState FromSaveDto(OwnershipAptitudeSaveDto dto)
        {
            OwnershipAptitudeState state = new();
            state.LoadFromSaveDto(dto);
            return state;
        }

        public void Sanitize()
        {
            acquisitionExperiencePoints = Mathf.Clamp(acquisitionExperiencePoints, 0, MaxCategoryExperiencePoints);
            operationsExperiencePoints = Mathf.Clamp(operationsExperiencePoints, 0, MaxCategoryExperiencePoints);
            peopleReputationExperiencePoints = Mathf.Clamp(peopleReputationExperiencePoints, 0, MaxCategoryExperiencePoints);
            landPurchasedCount = Mathf.Max(0, landPurchasedCount);
            businessPurchasedCount = Mathf.Max(0, businessPurchasedCount);
            parcelsDevelopedCount = Mathf.Max(0, parcelsDevelopedCount);
            businessesOpenedCount = Mathf.Max(0, businessesOpenedCount);
            turnaroundsCompletedCount = Mathf.Max(0, turnaroundsCompletedCount);
            ownershipMilestonesReached = Mathf.Clamp(ownershipMilestonesReached, 0, OwnershipMilestoneThresholds.Length);
            highestOwnershipMilestoneCount = NormalizeMilestoneThreshold(highestOwnershipMilestoneCount);

            int thresholdsReachedByHighest = CountThresholdsAtOrBelow(highestOwnershipMilestoneCount);
            if (ownershipMilestonesReached > thresholdsReachedByHighest)
            {
                ownershipMilestonesReached = thresholdsReachedByHighest;
            }
            else if (ownershipMilestonesReached < thresholdsReachedByHighest)
            {
                highestOwnershipMilestoneCount = ownershipMilestonesReached <= 0
                    ? 0
                    : OwnershipMilestoneThresholds[ownershipMilestonesReached - 1];
            }
        }

        public static int CalculateOwnedHoldings(int ownedLandCount, int ownedBusinessCount)
        {
            long total = (long)Mathf.Max(0, ownedLandCount) + Mathf.Max(0, ownedBusinessCount);
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        private void Clear()
        {
            acquisitionExperiencePoints = 0;
            operationsExperiencePoints = 0;
            peopleReputationExperiencePoints = 0;
            landPurchasedCount = 0;
            businessPurchasedCount = 0;
            parcelsDevelopedCount = 0;
            businessesOpenedCount = 0;
            turnaroundsCompletedCount = 0;
            ownershipMilestonesReached = 0;
            highestOwnershipMilestoneCount = 0;
        }

        private void AddExperience(OwnershipAptitudeCategory category, int points)
        {
            int clampedPoints = Mathf.Max(0, points);
            switch (category)
            {
                case OwnershipAptitudeCategory.Acquisition:
                    acquisitionExperiencePoints = AddBounded(acquisitionExperiencePoints, clampedPoints);
                    break;

                case OwnershipAptitudeCategory.Operations:
                    operationsExperiencePoints = AddBounded(operationsExperiencePoints, clampedPoints);
                    break;

                case OwnershipAptitudeCategory.PeopleReputation:
                    peopleReputationExperiencePoints = AddBounded(peopleReputationExperiencePoints, clampedPoints);
                    break;
            }
        }

        private static int AddBounded(int current, int points)
        {
            long sum = (long)Mathf.Max(0, current) + Mathf.Max(0, points);
            return (int)Math.Min(MaxCategoryExperiencePoints, sum);
        }

        private static int IncrementCounter(int value)
        {
            return value == int.MaxValue ? int.MaxValue : Mathf.Max(0, value) + 1;
        }

        private static int NormalizeMilestoneThreshold(int threshold)
        {
            if (threshold <= 0 || OwnershipMilestoneThresholds.Length == 0)
            {
                return 0;
            }

            int normalized = 0;
            for (int i = 0; i < OwnershipMilestoneThresholds.Length; i++)
            {
                int candidate = OwnershipMilestoneThresholds[i];
                if (candidate > threshold)
                {
                    break;
                }

                normalized = candidate;
            }

            return normalized;
        }

        private static int CountThresholdsAtOrBelow(int threshold)
        {
            if (threshold <= 0)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < OwnershipMilestoneThresholds.Length; i++)
            {
                if (OwnershipMilestoneThresholds[i] <= threshold)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
