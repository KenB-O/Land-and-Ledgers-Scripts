using System;
using UnityEngine;

namespace LandLedgers.Population
{
    public enum WorkerTraitKind
    {
        Reliability = 0,
        Care = 1,
        SocialSkill = 2,
        Honesty = 3,
        LearningSpeed = 4,
        Durability = 5
    }

    [Serializable]
    public sealed class WorkerHiddenTraits
    {
        public bool initialized;
        public int reliability;
        public int care;
        public int socialSkill;
        public int honesty;
        public int learningSpeed;
        public int durability;

        public bool IsInitialized => initialized;

        public int GetValue(WorkerTraitKind trait)
        {
            return trait switch
            {
                WorkerTraitKind.Reliability => ClampTrait(reliability),
                WorkerTraitKind.Care => ClampTrait(care),
                WorkerTraitKind.SocialSkill => ClampTrait(socialSkill),
                WorkerTraitKind.Honesty => ClampTrait(honesty),
                WorkerTraitKind.LearningSpeed => ClampTrait(learningSpeed),
                WorkerTraitKind.Durability => ClampTrait(durability),
                _ => 0
            };
        }

        public WorkerHiddenTraits Clone()
        {
            return new WorkerHiddenTraits
            {
                initialized = initialized,
                reliability = ClampTrait(reliability),
                care = ClampTrait(care),
                socialSkill = ClampTrait(socialSkill),
                honesty = ClampTrait(honesty),
                learningSpeed = ClampTrait(learningSpeed),
                durability = ClampTrait(durability)
            };
        }

        public static WorkerHiddenTraits Create(
            int reliability,
            int care,
            int socialSkill,
            int honesty,
            int learningSpeed,
            int durability)
        {
            return new WorkerHiddenTraits
            {
                initialized = true,
                reliability = ClampTrait(reliability),
                care = ClampTrait(care),
                socialSkill = ClampTrait(socialSkill),
                honesty = ClampTrait(honesty),
                learningSpeed = ClampTrait(learningSpeed),
                durability = ClampTrait(durability)
            };
        }

        public static WorkerHiddenTraits Generate(System.Random random, AgeBand ageBand, LaborAccessLevel laborAccess)
        {
            random ??= new System.Random(0);
            int maturityBonus = laborAccess switch
            {
                LaborAccessLevel.None => -10,
                LaborAccessLevel.HouseholdHelper => -6,
                LaborAccessLevel.JuniorLowTrust => -2,
                LaborAccessLevel.YoungWorker => 2,
                LaborAccessLevel.FullLaborMarket => 6,
                _ => 0
            };

            int physicalBonus = ageBand switch
            {
                AgeBand.Child0To9 => -12,
                AgeBand.Helper10To12 => -8,
                AgeBand.JuniorWorker13To15 => -3,
                AgeBand.YoungWorker16To17 => 2,
                AgeBand.Adult18Plus => 8,
                _ => 0
            };

            return Create(
                RollTrait(random, maturityBonus),
                RollTrait(random, maturityBonus / 2),
                RollTrait(random, 0),
                RollTrait(random, maturityBonus),
                RollTrait(random, ageBand == AgeBand.Adult18Plus ? -1 : 4),
                RollTrait(random, physicalBonus));
        }

        public static int ClampTrait(int value)
        {
            return Mathf.Clamp(value, 0, 100);
        }

        private static int RollTrait(System.Random random, int modifier)
        {
            int value = random.Next(35, 86) + modifier;
            if (random.NextDouble() < 0.08d)
            {
                value = random.Next(20, 96) + modifier;
            }

            return ClampTrait(value);
        }
    }

    [Serializable]
    public sealed class WorkerVisibleProfile
    {
        public bool initialized;
        public int generalSkill;
        public int wageExpectationCents;
        public string roleHistorySummary;

        public bool IsInitialized => initialized;

        public WorkerVisibleProfile Clone()
        {
            return new WorkerVisibleProfile
            {
                initialized = initialized,
                generalSkill = WorkerHiddenTraits.ClampTrait(generalSkill),
                wageExpectationCents = Mathf.Max(0, wageExpectationCents),
                roleHistorySummary = roleHistorySummary ?? string.Empty
            };
        }

        public void RecordCurrentRole(string roleName, int wageCents)
        {
            initialized = true;
            roleHistorySummary = string.IsNullOrWhiteSpace(roleName)
                ? "Currently working"
                : $"Currently working as {roleName}";
            wageExpectationCents = Mathf.Max(wageExpectationCents, Mathf.Max(0, wageCents));
        }

        public void RecordFormerRole(string roleName, int wageCents)
        {
            initialized = true;
            roleHistorySummary = string.IsNullOrWhiteSpace(roleName)
                ? "Prior work history"
                : $"Former {roleName}";
            wageExpectationCents = Mathf.Max(wageExpectationCents, Mathf.Max(0, wageCents));
        }

        public static WorkerVisibleProfile Create(
            int generalSkill,
            int wageExpectationCents,
            string roleHistorySummary)
        {
            return new WorkerVisibleProfile
            {
                initialized = true,
                generalSkill = WorkerHiddenTraits.ClampTrait(generalSkill),
                wageExpectationCents = Mathf.Max(0, wageExpectationCents),
                roleHistorySummary = string.IsNullOrWhiteSpace(roleHistorySummary)
                    ? "No paid work history"
                    : roleHistorySummary
            };
        }

        public static WorkerVisibleProfile Generate(
            WorkerHiddenTraits hiddenTraits,
            int age,
            AgeBand ageBand,
            LaborAccessLevel laborAccess,
            WageSnapshot wage,
            string professionName,
            System.Random random)
        {
            random ??= new System.Random(0);
            hiddenTraits ??= WorkerHiddenTraits.Generate(random, ageBand, laborAccess);

            int accessBase = laborAccess switch
            {
                LaborAccessLevel.None => 4,
                LaborAccessLevel.HouseholdHelper => 12,
                LaborAccessLevel.JuniorLowTrust => 24,
                LaborAccessLevel.YoungWorker => 36,
                LaborAccessLevel.FullLaborMarket => 48,
                _ => 10
            };

            int ageBonus = Mathf.Clamp(age - 14, 0, 22);
            int hiddenAverage = (
                hiddenTraits.GetValue(WorkerTraitKind.Reliability)
                + hiddenTraits.GetValue(WorkerTraitKind.Care)
                + hiddenTraits.GetValue(WorkerTraitKind.LearningSpeed)) / 3;
            int skill = accessBase + ageBonus + Mathf.RoundToInt(hiddenAverage * 0.25f) + random.Next(-6, 7);
            int wageCents = ResolveStartingWageExpectationCents(wage, laborAccess, skill);

            return Create(skill, wageCents, ResolveRoleHistory(professionName, laborAccess));
        }

        private static int ResolveStartingWageExpectationCents(WageSnapshot wage, LaborAccessLevel laborAccess, int generalSkill)
        {
            if (wage.weeklyWage > 0)
            {
                return Mathf.Max(0, wage.weeklyWage * 100);
            }

            int baseline = laborAccess switch
            {
                LaborAccessLevel.JuniorLowTrust => 700,
                LaborAccessLevel.YoungWorker => 1000,
                LaborAccessLevel.FullLaborMarket => 1400,
                _ => 0
            };

            if (baseline <= 0)
            {
                return 0;
            }

            return Mathf.Max(0, baseline + (WorkerHiddenTraits.ClampTrait(generalSkill) - 50) * 8);
        }

        private static string ResolveRoleHistory(string professionName, LaborAccessLevel laborAccess)
        {
            if (string.IsNullOrWhiteSpace(professionName)
                || professionName.IndexOf("No assigned", StringComparison.OrdinalIgnoreCase) >= 0
                || professionName.IndexOf("dependent", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "No paid work history";
            }

            if (laborAccess == LaborAccessLevel.HouseholdHelper)
            {
                return "Household helper";
            }

            return $"Worked as {professionName}";
        }
    }

    [Serializable]
    public sealed class WorkerTraitVisibility
    {
        public bool initialized;
        public bool roleFitHintVisible = true;
        public int reliabilityConfidence;
        public int careConfidence;
        public int socialSkillConfidence;
        public int honestyConfidence;
        public int learningSpeedConfidence;
        public int durabilityConfidence;

        public bool IsInitialized => initialized;

        public WorkerTraitVisibility Clone()
        {
            return new WorkerTraitVisibility
            {
                initialized = initialized,
                roleFitHintVisible = roleFitHintVisible,
                reliabilityConfidence = WorkerHiddenTraits.ClampTrait(reliabilityConfidence),
                careConfidence = WorkerHiddenTraits.ClampTrait(careConfidence),
                socialSkillConfidence = WorkerHiddenTraits.ClampTrait(socialSkillConfidence),
                honestyConfidence = WorkerHiddenTraits.ClampTrait(honestyConfidence),
                learningSpeedConfidence = WorkerHiddenTraits.ClampTrait(learningSpeedConfidence),
                durabilityConfidence = WorkerHiddenTraits.ClampTrait(durabilityConfidence)
            };
        }

        public static WorkerTraitVisibility FirstPassDefault()
        {
            return new WorkerTraitVisibility
            {
                initialized = true,
                roleFitHintVisible = true,
                reliabilityConfidence = 20,
                careConfidence = 20,
                socialSkillConfidence = 20,
                honestyConfidence = 10,
                learningSpeedConfidence = 15,
                durabilityConfidence = 20
            };
        }
    }

    [Serializable]
    public sealed class WorkerApprenticeshipState
    {
        public bool initialized;
        public ApprenticeshipStage stage = ApprenticeshipStage.Helper;
        public int experiencePoints;
        public int totalSupportedWeeksWorked;
        public int consecutiveWeeksInCurrentAssignment;
        public int currentBusinessType = -1;
        public int currentBuildingId = -1;
        public string currentSlotId = string.Empty;

        public bool IsInitialized => initialized;
        public ApprenticeshipStage Stage => stage;
        public int ExperiencePoints => Mathf.Max(0, experiencePoints);
        public int TotalSupportedWeeksWorked => Mathf.Max(0, totalSupportedWeeksWorked);
        public int ConsecutiveWeeksInCurrentAssignment => Mathf.Max(0, consecutiveWeeksInCurrentAssignment);
        public int CurrentBusinessType => currentBusinessType;
        public int CurrentBuildingId => currentBuildingId;
        public string CurrentSlotId => currentSlotId ?? string.Empty;

        public WorkerApprenticeshipState Clone()
        {
            WorkerApprenticeshipState clone = new()
            {
                initialized = initialized,
                stage = NormalizeStage(stage),
                experiencePoints = Mathf.Max(0, experiencePoints),
                totalSupportedWeeksWorked = Mathf.Max(0, totalSupportedWeeksWorked),
                consecutiveWeeksInCurrentAssignment = Mathf.Max(0, consecutiveWeeksInCurrentAssignment),
                currentBusinessType = currentBusinessType,
                currentBuildingId = currentBuildingId,
                currentSlotId = currentSlotId ?? string.Empty
            };
            clone.EnsureInitialized();
            return clone;
        }

        public void EnsureInitialized()
        {
            initialized = true;
            stage = NormalizeStage(stage);
            experiencePoints = Mathf.Max(0, experiencePoints);
            totalSupportedWeeksWorked = Mathf.Max(0, totalSupportedWeeksWorked);
            consecutiveWeeksInCurrentAssignment = Mathf.Max(0, consecutiveWeeksInCurrentAssignment);
            currentSlotId ??= string.Empty;
        }

        public void RecordAssignment(int businessType, int buildingId, string slotId)
        {
            EnsureInitialized();
            if (!IsSameAssignment(businessType, buildingId, slotId))
            {
                consecutiveWeeksInCurrentAssignment = 0;
            }

            currentBusinessType = businessType;
            currentBuildingId = buildingId;
            currentSlotId = slotId ?? string.Empty;
        }

        public void RecordPaidWeek(int gainedExperience, int businessType, int buildingId, string slotId, ApprenticeshipStage resolvedStage)
        {
            EnsureInitialized();
            bool sameAssignment = IsSameAssignment(businessType, buildingId, slotId);
            currentBusinessType = businessType;
            currentBuildingId = buildingId;
            currentSlotId = slotId ?? string.Empty;
            consecutiveWeeksInCurrentAssignment = sameAssignment
                ? Mathf.Max(0, consecutiveWeeksInCurrentAssignment) + 1
                : 1;
            totalSupportedWeeksWorked = Mathf.Max(0, totalSupportedWeeksWorked) + 1;
            experiencePoints = Mathf.Max(0, experiencePoints + Mathf.Max(0, gainedExperience));
            stage = NormalizeStage(resolvedStage);
        }

        public void ClearCurrentAssignment()
        {
            EnsureInitialized();
            currentBusinessType = -1;
            currentBuildingId = -1;
            currentSlotId = string.Empty;
            consecutiveWeeksInCurrentAssignment = 0;
        }

        public bool IsSameAssignment(int businessType, int buildingId, string slotId)
        {
            return currentBusinessType == businessType
                && currentBuildingId == buildingId
                && string.Equals(CurrentSlotId, slotId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        public static WorkerApprenticeshipState FirstPassDefault()
        {
            WorkerApprenticeshipState state = new();
            state.EnsureInitialized();
            return state;
        }

        private static ApprenticeshipStage NormalizeStage(ApprenticeshipStage value)
        {
            return value switch
            {
                ApprenticeshipStage.Apprentice => ApprenticeshipStage.Apprentice,
                ApprenticeshipStage.Worker => ApprenticeshipStage.Worker,
                _ => ApprenticeshipStage.Helper
            };
        }
    }
}
