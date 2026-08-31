using System;
using LandLedgers.Population;
using UnityEngine;

namespace LandLedgers.Economy
{
    public readonly struct WorkerRoleFitResult
    {
        public WorkerRoleFitResult(float score01, string label, string roleDisplayName, string summary, string strongestReason)
        {
            Score01 = Mathf.Clamp01(score01);
            Label = string.IsNullOrWhiteSpace(label) ? "Risky" : label;
            RoleDisplayName = string.IsNullOrWhiteSpace(roleDisplayName) ? "Worker" : roleDisplayName;
            Summary = summary ?? string.Empty;
            StrongestReason = strongestReason ?? string.Empty;
        }

        public float Score01 { get; }
        public string Label { get; }
        public string RoleDisplayName { get; }
        public string Summary { get; }
        public string StrongestReason { get; }
    }

    public static class WorkerRoleFitEvaluator
    {
        public static WorkerRoleFitResult Evaluate(PersonState person, BusinessType businessType, WorkerSlotState slot)
        {
            return Evaluate(
                person,
                businessType,
                slot != null ? slot.SlotId : string.Empty,
                slot != null ? slot.SlotDisplayName : string.Empty);
        }

        public static WorkerRoleFitResult Evaluate(PersonState person, BusinessType businessType, string slotId, string slotDisplayName)
        {
            string roleDisplayName = string.IsNullOrWhiteSpace(slotDisplayName) ? "Worker" : slotDisplayName;
            if (person == null)
            {
                return BuildResult(0f, roleDisplayName, string.Empty);
            }

            person.EnsureWorkerTraitsInitialized();
            WorkerHiddenTraits hidden = person.hiddenWorkerTraits;
            WorkerVisibleProfile visible = person.visibleWorkerProfile;
            WorkerRoleProfile profile = ResolveProfile(businessType, slotId, roleDisplayName);

            float totalWeight = 0f;
            float weightedScore = 0f;
            TraitWeight strongest = default;
            float strongestContribution = -1f;

            for (int i = 0; i < profile.weights.Length; i++)
            {
                TraitWeight weight = profile.weights[i];
                float traitScore = hidden.GetValue(weight.trait) / 100f;
                float contribution = traitScore * weight.weight;
                weightedScore += contribution;
                totalWeight += weight.weight;
                if (contribution > strongestContribution)
                {
                    strongestContribution = contribution;
                    strongest = weight;
                }
            }

            if (profile.generalSkillWeight > 0f)
            {
                weightedScore += WorkerHiddenTraits.ClampTrait(visible.generalSkill) / 100f * profile.generalSkillWeight;
                totalWeight += profile.generalSkillWeight;
            }

            float score = totalWeight > 0f ? weightedScore / totalWeight : 0f;
            string reason = strongestContribution >= 0f ? strongest.reason : string.Empty;
            return BuildResult(score, profile.roleDisplayName, reason);
        }

        public static string BuildCandidateHint(PersonState person, BusinessType businessType, WorkerSlotState slot)
        {
            WorkerRoleFitResult fit = Evaluate(person, businessType, slot);
            return $"Role fit: {fit.Label} - suited for {fit.RoleDisplayName}";
        }

        public static int CompareCandidates(PersonState left, PersonState right, BusinessType businessType, WorkerSlotState slot)
        {
            float leftScore = NewcomerSettlementEvaluator.BuildLaborSortScore(left, Evaluate(left, businessType, slot).Score01);
            float rightScore = NewcomerSettlementEvaluator.BuildLaborSortScore(right, Evaluate(right, businessType, slot).Score01);
            int fit = rightScore.CompareTo(leftScore);
            if (fit != 0)
            {
                return fit;
            }

            int access = right.laborAccessLevel.CompareTo(left.laborAccessLevel);
            if (access != 0)
            {
                return access;
            }

            int skill = GetVisibleSkill(right).CompareTo(GetVisibleSkill(left));
            if (skill != 0)
            {
                return skill;
            }

            int age = right.age.CompareTo(left.age);
            return age != 0 ? age : string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal);
        }

        private static WorkerRoleFitResult BuildResult(float score01, string roleDisplayName, string strongestReason)
        {
            string label = score01 >= 0.7f ? "Strong" : score01 >= 0.5f ? "Fair" : "Risky";
            string summary = string.IsNullOrWhiteSpace(strongestReason)
                ? $"{label} fit for {roleDisplayName}."
                : $"{label} fit for {roleDisplayName}; strongest signal is {strongestReason}.";
            return new WorkerRoleFitResult(score01, label, roleDisplayName, summary, strongestReason);
        }

        private static int GetVisibleSkill(PersonState person)
        {
            person?.EnsureWorkerTraitsInitialized();
            return person != null && person.visibleWorkerProfile != null
                ? WorkerHiddenTraits.ClampTrait(person.visibleWorkerProfile.generalSkill)
                : 0;
        }

        private static WorkerRoleProfile ResolveProfile(BusinessType businessType, string slotId, string slotDisplayName)
        {
            string normalizedSlot = (slotId ?? string.Empty).Trim().ToLowerInvariant();
            string roleDisplayName = string.IsNullOrWhiteSpace(slotDisplayName) ? "Worker" : slotDisplayName;

            if (businessType == BusinessType.GeneralStore)
            {
                if (normalizedSlot.Contains("clerk"))
                {
                    return new WorkerRoleProfile(roleDisplayName, 0.08f,
                        new TraitWeight(WorkerTraitKind.SocialSkill, 0.42f, "customer manner"),
                        new TraitWeight(WorkerTraitKind.Reliability, 0.33f, "steady attendance"),
                        new TraitWeight(WorkerTraitKind.Honesty, 0.17f, "trustworthy counter work"));
                }

                return new WorkerRoleProfile(roleDisplayName, 0.08f,
                    new TraitWeight(WorkerTraitKind.Reliability, 0.38f, "steady attendance"),
                    new TraitWeight(WorkerTraitKind.Durability, 0.29f, "stockroom stamina"),
                    new TraitWeight(WorkerTraitKind.Care, 0.25f, "careful handling"));
            }

            if (businessType == BusinessType.Butcher)
            {
                if (normalizedSlot.Contains("counter"))
                {
                    return new WorkerRoleProfile(roleDisplayName, 0.05f,
                        new TraitWeight(WorkerTraitKind.Care, 0.35f, "careful handling"),
                        new TraitWeight(WorkerTraitKind.Reliability, 0.30f, "steady attendance"),
                        new TraitWeight(WorkerTraitKind.SocialSkill, 0.20f, "customer manner"),
                        new TraitWeight(WorkerTraitKind.Honesty, 0.10f, "trustworthy counter work"));
                }

                return new WorkerRoleProfile(roleDisplayName, 0.05f,
                    new TraitWeight(WorkerTraitKind.Care, 0.43f, "careful handling"),
                    new TraitWeight(WorkerTraitKind.Reliability, 0.32f, "steady attendance"),
                    new TraitWeight(WorkerTraitKind.Durability, 0.15f, "physical work"),
                    new TraitWeight(WorkerTraitKind.LearningSpeed, 0.05f, "trainability"));
            }

            if (businessType == BusinessType.Blacksmith)
            {
                return new WorkerRoleProfile(roleDisplayName, 0.05f,
                    new TraitWeight(WorkerTraitKind.Durability, 0.38f, "forge stamina"),
                    new TraitWeight(WorkerTraitKind.Reliability, 0.29f, "steady attendance"),
                    new TraitWeight(WorkerTraitKind.LearningSpeed, 0.23f, "trainability"),
                    new TraitWeight(WorkerTraitKind.Care, 0.05f, "careful handling"));
            }

            if (businessType == BusinessType.Doctor && normalizedSlot.Contains("assistant"))
            {
                return new WorkerRoleProfile(roleDisplayName, 0.05f,
                    new TraitWeight(WorkerTraitKind.Care, 0.43f, "patient care"),
                    new TraitWeight(WorkerTraitKind.Reliability, 0.32f, "steady attendance"),
                    new TraitWeight(WorkerTraitKind.SocialSkill, 0.10f, "bedside manner"),
                    new TraitWeight(WorkerTraitKind.LearningSpeed, 0.10f, "trainability"));
            }

            if (businessType == BusinessType.Ranch || businessType == BusinessType.CropFarm)
            {
                return new WorkerRoleProfile(roleDisplayName, 0.05f,
                    new TraitWeight(WorkerTraitKind.Durability, 0.34f, "outdoor stamina"),
                    new TraitWeight(WorkerTraitKind.Reliability, 0.34f, "steady attendance"),
                    new TraitWeight(WorkerTraitKind.Care, 0.19f, "careful handling"),
                    new TraitWeight(WorkerTraitKind.LearningSpeed, 0.08f, "trainability"));
            }

            if (businessType == BusinessType.Sawmill)
            {
                if (normalizedSlot.Contains("sawyer"))
                {
                    return new WorkerRoleProfile(roleDisplayName, 0.05f,
                        new TraitWeight(WorkerTraitKind.Care, 0.34f, "careful mill work"),
                        new TraitWeight(WorkerTraitKind.Reliability, 0.29f, "steady attendance"),
                        new TraitWeight(WorkerTraitKind.Durability, 0.20f, "mill stamina"),
                        new TraitWeight(WorkerTraitKind.LearningSpeed, 0.12f, "trainability"));
                }

                if (normalizedSlot.Contains("logger"))
                {
                    return new WorkerRoleProfile(roleDisplayName, 0.05f,
                        new TraitWeight(WorkerTraitKind.Durability, 0.40f, "timber crew stamina"),
                        new TraitWeight(WorkerTraitKind.Reliability, 0.31f, "steady attendance"),
                        new TraitWeight(WorkerTraitKind.Care, 0.14f, "careful cutting"),
                        new TraitWeight(WorkerTraitKind.LearningSpeed, 0.10f, "trainability"));
                }

                if (normalizedSlot.Contains("teamster") || normalizedSlot.Contains("yard"))
                {
                    return new WorkerRoleProfile(roleDisplayName, 0.05f,
                        new TraitWeight(WorkerTraitKind.Reliability, 0.36f, "steady hauling"),
                        new TraitWeight(WorkerTraitKind.Care, 0.24f, "careful handling"),
                        new TraitWeight(WorkerTraitKind.Durability, 0.20f, "yard stamina"),
                        new TraitWeight(WorkerTraitKind.Honesty, 0.15f, "trustworthy hauling"));
                }

                if (normalizedSlot.Contains("foreman"))
                {
                    return new WorkerRoleProfile(roleDisplayName, 0.08f,
                        new TraitWeight(WorkerTraitKind.Reliability, 0.31f, "steady supervision"),
                        new TraitWeight(WorkerTraitKind.SocialSkill, 0.25f, "crew direction"),
                        new TraitWeight(WorkerTraitKind.Honesty, 0.19f, "trustworthy oversight"),
                        new TraitWeight(WorkerTraitKind.Care, 0.17f, "careful scheduling"));
                }

                return new WorkerRoleProfile(roleDisplayName, 0.05f,
                    new TraitWeight(WorkerTraitKind.Durability, 0.32f, "mill-yard stamina"),
                    new TraitWeight(WorkerTraitKind.Reliability, 0.32f, "steady attendance"),
                    new TraitWeight(WorkerTraitKind.Care, 0.18f, "careful handling"),
                    new TraitWeight(WorkerTraitKind.LearningSpeed, 0.13f, "trainability"));
            }

            if (businessType == BusinessType.LumberYard)
            {
                if (normalizedSlot.Contains("manager") || normalizedSlot.Contains("clerk") || normalizedSlot.Contains("bookkeeper"))
                {
                    return new WorkerRoleProfile(roleDisplayName, 0.06f,
                        new TraitWeight(WorkerTraitKind.Honesty, 0.31f, "stock records"),
                        new TraitWeight(WorkerTraitKind.Reliability, 0.29f, "steady attendance"),
                        new TraitWeight(WorkerTraitKind.Care, 0.22f, "careful ordering"),
                        new TraitWeight(WorkerTraitKind.SocialSkill, 0.12f, "buyer handling"));
                }

                if (normalizedSlot.Contains("teamster") || normalizedSlot.Contains("delivery") || normalizedSlot.Contains("yard"))
                {
                    return new WorkerRoleProfile(roleDisplayName, 0.05f,
                        new TraitWeight(WorkerTraitKind.Reliability, 0.34f, "steady delivery work"),
                        new TraitWeight(WorkerTraitKind.Durability, 0.27f, "yard stamina"),
                        new TraitWeight(WorkerTraitKind.Care, 0.20f, "careful lumber handling"),
                        new TraitWeight(WorkerTraitKind.Honesty, 0.14f, "trustworthy stock movement"));
                }

                return new WorkerRoleProfile(roleDisplayName, 0.05f,
                    new TraitWeight(WorkerTraitKind.Reliability, 0.33f, "steady attendance"),
                    new TraitWeight(WorkerTraitKind.Care, 0.28f, "careful lumber handling"),
                    new TraitWeight(WorkerTraitKind.Durability, 0.20f, "yard stamina"),
                    new TraitWeight(WorkerTraitKind.Honesty, 0.14f, "trustworthy handling"));
            }

            if (businessType == BusinessType.BoardingHouse)
            {
                if (normalizedSlot.Contains("keeper") || normalizedSlot.Contains("manager"))
                {
                    return new WorkerRoleProfile(roleDisplayName, 0.07f,
                        new TraitWeight(WorkerTraitKind.Honesty, 0.30f, "guest accounts"),
                        new TraitWeight(WorkerTraitKind.Reliability, 0.30f, "steady house supervision"),
                        new TraitWeight(WorkerTraitKind.SocialSkill, 0.22f, "boarder handling"),
                        new TraitWeight(WorkerTraitKind.Care, 0.13f, "room upkeep"));
                }

                return new WorkerRoleProfile(roleDisplayName, 0.05f,
                    new TraitWeight(WorkerTraitKind.Care, 0.34f, "clean rooms"),
                    new TraitWeight(WorkerTraitKind.Reliability, 0.30f, "steady chores"),
                    new TraitWeight(WorkerTraitKind.SocialSkill, 0.18f, "guest handling"),
                    new TraitWeight(WorkerTraitKind.Durability, 0.10f, "house work stamina"));
            }

            if (businessType == BusinessType.LiveryFreight)
            {
                return new WorkerRoleProfile(roleDisplayName, 0.05f,
                    new TraitWeight(WorkerTraitKind.Reliability, 0.35f, "steady hauling"),
                    new TraitWeight(WorkerTraitKind.Care, 0.24f, "careful animal and freight handling"),
                    new TraitWeight(WorkerTraitKind.Durability, 0.22f, "yard and road stamina"),
                    new TraitWeight(WorkerTraitKind.Honesty, 0.14f, "trustworthy freight handling"));
            }

            if (businessType == BusinessType.Builder || businessType == BusinessType.Wheelwright)
            {
                return new WorkerRoleProfile(roleDisplayName, 0.05f,
                    new TraitWeight(WorkerTraitKind.Care, 0.32f, "accurate repair work"),
                    new TraitWeight(WorkerTraitKind.Reliability, 0.30f, "steady job completion"),
                    new TraitWeight(WorkerTraitKind.Durability, 0.18f, "shop and site stamina"),
                    new TraitWeight(WorkerTraitKind.LearningSpeed, 0.15f, "trainability"));
            }

            if (businessType == BusinessType.FuelDealer || businessType == BusinessType.GrainMill || businessType == BusinessType.Bakery)
            {
                return new WorkerRoleProfile(roleDisplayName, 0.05f,
                    new TraitWeight(WorkerTraitKind.Reliability, 0.34f, "steady production rhythm"),
                    new TraitWeight(WorkerTraitKind.Care, 0.28f, "careful handling"),
                    new TraitWeight(WorkerTraitKind.Durability, 0.20f, "work stamina"),
                    new TraitWeight(WorkerTraitKind.Honesty, 0.10f, "trustworthy accounts"));
            }

            if (businessType == BusinessType.Tailor || businessType == BusinessType.Barber)
            {
                return new WorkerRoleProfile(roleDisplayName, 0.06f,
                    new TraitWeight(WorkerTraitKind.Care, 0.38f, "careful customer work"),
                    new TraitWeight(WorkerTraitKind.SocialSkill, 0.24f, "customer manner"),
                    new TraitWeight(WorkerTraitKind.Reliability, 0.23f, "steady appointments"),
                    new TraitWeight(WorkerTraitKind.Honesty, 0.09f, "trustworthy counter work"));
            }

            if (businessType == BusinessType.Saloon)
            {
                return new WorkerRoleProfile(roleDisplayName, 0.06f,
                    new TraitWeight(WorkerTraitKind.SocialSkill, 0.34f, "customer handling"),
                    new TraitWeight(WorkerTraitKind.Reliability, 0.28f, "steady service"),
                    new TraitWeight(WorkerTraitKind.Honesty, 0.18f, "cash and tab trust"),
                    new TraitWeight(WorkerTraitKind.Care, 0.14f, "orderly service"));
            }

            return new WorkerRoleProfile(roleDisplayName, 0.15f,
                new TraitWeight(WorkerTraitKind.Reliability, 0.35f, "steady attendance"),
                new TraitWeight(WorkerTraitKind.LearningSpeed, 0.20f, "trainability"),
                new TraitWeight(WorkerTraitKind.Honesty, 0.15f, "trustworthy work"),
                new TraitWeight(WorkerTraitKind.Care, 0.15f, "careful handling"));
        }

        private readonly struct WorkerRoleProfile
        {
            public WorkerRoleProfile(string roleDisplayName, float generalSkillWeight, params TraitWeight[] weights)
            {
                this.roleDisplayName = string.IsNullOrWhiteSpace(roleDisplayName) ? "Worker" : roleDisplayName;
                this.generalSkillWeight = Mathf.Max(0f, generalSkillWeight);
                this.weights = weights ?? Array.Empty<TraitWeight>();
            }

            public readonly string roleDisplayName;
            public readonly float generalSkillWeight;
            public readonly TraitWeight[] weights;
        }

        private readonly struct TraitWeight
        {
            public TraitWeight(WorkerTraitKind trait, float weight, string reason)
            {
                this.trait = trait;
                this.weight = Mathf.Max(0f, weight);
                this.reason = reason ?? string.Empty;
            }

            public readonly WorkerTraitKind trait;
            public readonly float weight;
            public readonly string reason;
        }
    }

    public static class ApprenticeshipProgressionEvaluator
    {
        public const int HelperToApprenticeExperience = 60;
        public const int ApprenticeToWorkerExperience = 180;

        private const int BasePaidWeekExperience = 10;

        public static bool IsSupportedBusiness(BusinessType businessType)
        {
            return businessType == BusinessType.GeneralStore
                || businessType == BusinessType.Blacksmith
                || businessType == BusinessType.Butcher
                || businessType == BusinessType.Sawmill
                || businessType == BusinessType.LumberYard
                || businessType == BusinessType.BoardingHouse
                || businessType == BusinessType.LiveryFreight
                || businessType == BusinessType.Builder
                || businessType == BusinessType.FuelDealer
                || businessType == BusinessType.GrainMill
                || businessType == BusinessType.Bakery
                || businessType == BusinessType.Tailor
                || businessType == BusinessType.Saloon
                || businessType == BusinessType.Barber
                || businessType == BusinessType.Wheelwright;
        }

        public static int AdvancePaidWorkers(BusinessInstanceState business, PopulationState population)
        {
            if (business == null || business.RuntimeState == null || population == null || !IsSupportedBusiness(business.BusinessType))
            {
                return 0;
            }

            int advanced = 0;
            for (int i = 0; i < business.RuntimeState.WorkerSlots.Count; i++)
            {
                WorkerSlotState slot = business.RuntimeState.WorkerSlots[i];
                if (slot == null || !slot.IsFilled || !slot.IsPaidActive)
                {
                    continue;
                }

                if (!int.TryParse(slot.AssignedWorkerId, out int personId))
                {
                    continue;
                }

                PersonState person = population.GetPerson(personId);
                if (person == null || person.workplaceBuildingId != business.AssignedBuildingId)
                {
                    continue;
                }

                if (AdvancePaidWeek(person, business.BusinessType, business.AssignedBuildingId, slot) > 0)
                {
                    advanced++;
                }
            }

            return advanced;
        }

        public static int AdvancePaidWeek(PersonState person, BusinessType businessType, int buildingId, WorkerSlotState slot)
        {
            if (person == null || slot == null || !slot.IsPaidActive || !IsSupportedBusiness(businessType))
            {
                return 0;
            }

            person.EnsureWorkerTraitsInitialized();
            person.EnsureApprenticeshipInitialized();

            WorkerApprenticeshipState apprenticeship = person.apprenticeship;
            int gained = CalculateWeeklyExperience(person, businessType, buildingId, slot);
            int resolvedExperience = apprenticeship.ExperiencePoints + gained;
            ApprenticeshipStage resolvedStage = ResolveStage(resolvedExperience);
            apprenticeship.RecordPaidWeek(gained, (int)businessType, buildingId, slot.SlotId, resolvedStage);
            return gained;
        }

        public static void RecordAssignment(PersonState person, BusinessType businessType, int buildingId, WorkerSlotState slot)
        {
            if (person == null || slot == null || !IsSupportedBusiness(businessType))
            {
                return;
            }

            person.EnsureWorkerTraitsInitialized();
            person.EnsureApprenticeshipInitialized();
            person.apprenticeship.RecordAssignment((int)businessType, buildingId, slot.SlotId);
        }

        public static void ClearAssignment(PersonState person)
        {
            if (person == null)
            {
                return;
            }

            person.EnsureApprenticeshipInitialized();
            person.apprenticeship.ClearCurrentAssignment();
        }

        public static string BuildAssignedWorkerProgressSegment(PersonState person, BusinessType businessType)
        {
            if (person == null || !IsSupportedBusiness(businessType))
            {
                return string.Empty;
            }

            person.EnsureApprenticeshipInitialized();
            return " | " + BuildProgressText(person.apprenticeship);
        }

        public static string BuildCandidateReadinessHint(PersonState person, BusinessType businessType, WorkerSlotState slot)
        {
            if (person == null || slot == null || !IsSupportedBusiness(businessType))
            {
                return string.Empty;
            }

            person.EnsureWorkerTraitsInitialized();
            person.EnsureApprenticeshipInitialized();
            WorkerRoleFitResult fit = WorkerRoleFitEvaluator.Evaluate(person, businessType, slot);
            return $"{GetStageDisplayName(person.apprenticeship.Stage)} | {fit.Label.ToLowerInvariant()} apprenticeship candidate";
        }

        public static string BuildProgressText(WorkerApprenticeshipState apprenticeship)
        {
            if (apprenticeship == null)
            {
                return "Helper 0% to Apprentice";
            }

            apprenticeship.EnsureInitialized();
            if (apprenticeship.Stage == ApprenticeshipStage.Worker)
            {
                return "Worker stage";
            }

            if (apprenticeship.Stage == ApprenticeshipStage.Apprentice)
            {
                int percent = Mathf.RoundToInt(Mathf.Clamp01(
                    (apprenticeship.ExperiencePoints - HelperToApprenticeExperience)
                    / (float)(ApprenticeToWorkerExperience - HelperToApprenticeExperience)) * 100f);
                return $"Apprentice {percent}% to Worker";
            }

            int helperPercent = Mathf.RoundToInt(Mathf.Clamp01(
                apprenticeship.ExperiencePoints / (float)HelperToApprenticeExperience) * 100f);
            return $"Helper {helperPercent}% to Apprentice";
        }

        public static ApprenticeshipStage ResolveStage(int experiencePoints)
        {
            int xp = Mathf.Max(0, experiencePoints);
            if (xp >= ApprenticeToWorkerExperience)
            {
                return ApprenticeshipStage.Worker;
            }

            return xp >= HelperToApprenticeExperience
                ? ApprenticeshipStage.Apprentice
                : ApprenticeshipStage.Helper;
        }

        private static int CalculateWeeklyExperience(PersonState person, BusinessType businessType, int buildingId, WorkerSlotState slot)
        {
            WorkerRoleFitResult fit = WorkerRoleFitEvaluator.Evaluate(person, businessType, slot);
            WorkerHiddenTraits hidden = person.hiddenWorkerTraits;
            WorkerApprenticeshipState apprenticeship = person.apprenticeship;
            int continuityBonus = apprenticeship != null && apprenticeship.IsSameAssignment((int)businessType, buildingId, slot.SlotId)
                ? Mathf.Clamp(apprenticeship.ConsecutiveWeeksInCurrentAssignment, 0, 4)
                : 0;

            float weekly = BasePaidWeekExperience;
            weekly += fit.Score01 * 8f;
            weekly += hidden.GetValue(WorkerTraitKind.LearningSpeed) / 100f * 5f;
            weekly += hidden.GetValue(WorkerTraitKind.Reliability) / 100f * 3f;
            weekly += continuityBonus;
            return Mathf.Max(1, Mathf.RoundToInt(weekly));
        }

        private static string GetStageDisplayName(ApprenticeshipStage stage)
        {
            return stage switch
            {
                ApprenticeshipStage.Apprentice => "Apprentice",
                ApprenticeshipStage.Worker => "Worker",
                _ => "Helper"
            };
        }
    }
}
