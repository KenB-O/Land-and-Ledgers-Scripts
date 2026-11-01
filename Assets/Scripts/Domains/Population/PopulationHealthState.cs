using System;
using UnityEngine;

namespace LandLedgers.Population
{
    public enum HealthConditionKind
    {
        None = 0,
        Illness = 1,
        Injury = 2
    }

    public enum HealthConditionSeverity
    {
        None = 0,
        Mild = 1,
        Serious = 2
    }

    public enum HealthWorkImpactBand
    {
        Full = 0,
        Reduced = 1,
        Limited = 2,
        Absent = 3
    }

    [Serializable]
    public sealed class PersonHealthState
    {
        public HealthConditionKind conditionKind = HealthConditionKind.None;
        public HealthConditionSeverity severity = HealthConditionSeverity.None;
        public int remainingDays;
        public int laborCapacityPercent = 100;
        public int lastTreatedDayIndex = -1;
        public string lastTreatmentSummary = string.Empty;

        public bool HasActiveCondition =>
            conditionKind != HealthConditionKind.None
            && severity != HealthConditionSeverity.None
            && remainingDays > 0;

        public void EnsureInitialized()
        {
            remainingDays = Mathf.Max(0, remainingDays);
            laborCapacityPercent = Mathf.Clamp(laborCapacityPercent, 0, 100);
            if (!HasActiveCondition)
            {
                conditionKind = HealthConditionKind.None;
                severity = HealthConditionSeverity.None;
                remainingDays = 0;
                laborCapacityPercent = 100;
            }
            else
            {
                laborCapacityPercent = PopulationHealthEvaluator.GetLaborCapacityPercent(conditionKind, severity);
            }
        }

        public void ApplyCondition(
            HealthConditionKind kind,
            HealthConditionSeverity newSeverity,
            int days,
            string summary)
        {
            conditionKind = kind;
            severity = newSeverity;
            remainingDays = Mathf.Max(0, days);
            laborCapacityPercent = PopulationHealthEvaluator.GetLaborCapacityPercent(conditionKind, severity);
            lastTreatedDayIndex = -1;
            lastTreatmentSummary = summary ?? string.Empty;
            EnsureInitialized();
        }

        public void TickNaturalRecovery()
        {
            if (!HasActiveCondition)
            {
                EnsureInitialized();
                return;
            }

            remainingDays = Mathf.Max(0, remainingDays - 1);
            if (remainingDays <= 0)
            {
                conditionKind = HealthConditionKind.None;
                severity = HealthConditionSeverity.None;
                laborCapacityPercent = 100;
                lastTreatmentSummary = "Recovered naturally.";
                return;
            }

            laborCapacityPercent = PopulationHealthEvaluator.GetLaborCapacityPercent(conditionKind, severity);
        }

        public void ApplyTreatment(
            int absoluteDayIndex,
            int daysReduced,
            string summary,
            HealthConditionSeverity resolvedSeverity = HealthConditionSeverity.None)
        {
            if (!HasActiveCondition)
            {
                EnsureInitialized();
                return;
            }

            lastTreatedDayIndex = absoluteDayIndex;
            lastTreatmentSummary = summary ?? string.Empty;
            if (resolvedSeverity != HealthConditionSeverity.None)
            {
                severity = resolvedSeverity;
            }

            remainingDays = Mathf.Max(0, remainingDays - Mathf.Max(0, daysReduced));
            if (remainingDays <= 0)
            {
                conditionKind = HealthConditionKind.None;
                severity = HealthConditionSeverity.None;
                laborCapacityPercent = 100;
                return;
            }

            EnsureInitialized();
        }

        public string BuildConditionSummary()
        {
            if (!HasActiveCondition)
            {
                return "Healthy.";
            }

            string kindLabel = conditionKind == HealthConditionKind.Injury ? "Injury" : "Illness";
            string severityLabel = severity == HealthConditionSeverity.Serious ? "Serious" : "Mild";
            string workImpact = PopulationHealthEvaluator.GetWorkImpactLabel(
                PopulationHealthEvaluator.GetWorkImpactBand(this, laborCapacityPercent));
            return $"{severityLabel} {kindLabel} | Work {workImpact} | Labor {laborCapacityPercent}% | {remainingDays} day(s) remaining.";
        }

        public string BuildTreatmentReadSummary()
        {
            if (!HasActiveCondition)
            {
                return "No treatment needed.";
            }

            string lastTreatment = lastTreatedDayIndex >= 0
                ? $"Last treated on day {lastTreatedDayIndex}."
                : "No treatment recorded yet.";
            return $"{BuildConditionSummary()} {lastTreatment}";
        }

        public PersonHealthState Clone()
        {
            return new PersonHealthState
            {
                conditionKind = conditionKind,
                severity = severity,
                remainingDays = remainingDays,
                laborCapacityPercent = laborCapacityPercent,
                lastTreatedDayIndex = lastTreatedDayIndex,
                lastTreatmentSummary = lastTreatmentSummary ?? string.Empty
            };
        }
    }

    [Serializable]
    public sealed class PopulationHealthDailySnapshot
    {
        public int activeCases;
        public int newCases;
        public int treatedCases;
        public int untreatedCases;
        public int laborLossCents;
        public int medicalSpendCents;
        public int doctorRevenueCents;
        public string summary = "No health resolution yet.";

        public string BuildDailySummary()
        {
            string details = $"Cases {activeCases} | New {newCases} | Treated {treatedCases} | Untreated {untreatedCases}";
            string costs = $"Labor Loss ${laborLossCents / 100f:N2} | Medical Spend ${medicalSpendCents / 100f:N2} | Doctor Revenue ${doctorRevenueCents / 100f:N2}";
            return string.IsNullOrWhiteSpace(summary) ? details + "\n" + costs : summary + "\n" + details + "\n" + costs;
        }

        public string BuildPressureHeadline()
        {
            if (activeCases <= 0 && untreatedCases <= 0)
            {
                return "Town health pressure is quiet.";
            }

            return untreatedCases > 0
                ? $"Health pressure rising: {untreatedCases} untreated case(s), {activeCases} active case(s)."
                : $"Health pressure active: {activeCases} active case(s), all treated today.";
        }

        public PopulationHealthDailySnapshot Clone()
        {
            return new PopulationHealthDailySnapshot
            {
                activeCases = Mathf.Max(0, activeCases),
                newCases = Mathf.Max(0, newCases),
                treatedCases = Mathf.Max(0, treatedCases),
                untreatedCases = Mathf.Max(0, untreatedCases),
                laborLossCents = Mathf.Max(0, laborLossCents),
                medicalSpendCents = Mathf.Max(0, medicalSpendCents),
                doctorRevenueCents = Mathf.Max(0, doctorRevenueCents),
                summary = summary ?? string.Empty
            };
        }
    }

    public static class PopulationHealthEvaluator
    {
        public const int MildIllnessDurationDays = 6;
        public const int SeriousIllnessDurationDays = 12;
        public const int MildInjuryDurationDays = 8;
        public const int SeriousInjuryDurationDays = 18;
        private const int BaseIllnessRiskPerTenThousand = 150;
        private const int BaseInjuryRiskPerTenThousand = 40;

        public static PersonHealthState EnsureHealthInitialized(PersonState person)
        {
            if (person == null)
            {
                return null;
            }

            person.health ??= new PersonHealthState();
            person.health.EnsureInitialized();
            return person.health;
        }

        public static bool HasActiveCondition(PersonState person)
        {
            PersonHealthState health = EnsureHealthInitialized(person);
            return health != null && health.HasActiveCondition;
        }

        public static float GetLaborAvailability01(PersonState person)
        {
            PersonHealthState health = EnsureHealthInitialized(person);
            if (health == null || !health.HasActiveCondition)
            {
                return 1f;
            }

            return Mathf.Clamp01(health.laborCapacityPercent / 100f);
        }

        public static float GetLaborAvailability01(
            PersonState person,
            HouseholdState household,
            PopulationState population)
        {
            int capacityPercent = GetEffectiveLaborCapacityPercent(person, household, population);
            return Mathf.Clamp01(capacityPercent / 100f);
        }

        public static int GetLaborCapacityPercent(
            HealthConditionKind conditionKind,
            HealthConditionSeverity severity)
        {
            return conditionKind switch
            {
                HealthConditionKind.Illness => severity == HealthConditionSeverity.Serious ? 35 : 70,
                HealthConditionKind.Injury => severity == HealthConditionSeverity.Serious ? 15 : 55,
                _ => 100
            };
        }

        public static int GetDurationDays(
            HealthConditionKind conditionKind,
            HealthConditionSeverity severity)
        {
            if (conditionKind == HealthConditionKind.Illness)
            {
                return severity == HealthConditionSeverity.Serious
                    ? SeriousIllnessDurationDays
                    : MildIllnessDurationDays;
            }

            if (conditionKind == HealthConditionKind.Injury)
            {
                return severity == HealthConditionSeverity.Serious
                    ? SeriousInjuryDurationDays
                    : MildInjuryDurationDays;
            }

            return 0;
        }

        public static int GetEffectiveWeeklyWageDollars(PersonState person)
        {
            if (person == null)
            {
                return 0;
            }

            int baseWage = Mathf.Max(0, person.wage.weeklyWage);
            return Mathf.RoundToInt(baseWage * GetLaborAvailability01(person));
        }

        public static int GetEffectiveWeeklyWageDollars(
            PersonState person,
            HouseholdState household,
            PopulationState population)
        {
            if (person == null)
            {
                return 0;
            }

            int baseWage = Mathf.Max(0, person.wage.weeklyWage);
            return Mathf.RoundToInt(baseWage * GetLaborAvailability01(person, household, population));
        }

        public static int GetWeeklyLaborLossCents(PersonState person)
        {
            if (person == null)
            {
                return 0;
            }

            int baseWage = Mathf.Max(0, person.wage.weeklyWage);
            int effectiveWage = GetEffectiveWeeklyWageDollars(person);
            return Mathf.Max(0, baseWage - effectiveWage) * 100;
        }

        public static int GetWeeklyLaborLossCents(
            PersonState person,
            HouseholdState household,
            PopulationState population)
        {
            if (person == null)
            {
                return 0;
            }

            int baseWage = Mathf.Max(0, person.wage.weeklyWage);
            int effectiveWage = GetEffectiveWeeklyWageDollars(person, household, population);
            return Mathf.Max(0, baseWage - effectiveWage) * 100;
        }

        public static bool IsLaborMarketWorker(PersonState person)
        {
            return person != null
                && person.workplaceBuildingId >= 0
                && person.laborAccessLevel >= LaborAccessLevel.YoungWorker
                && person.wage.weeklyWage > 0;
        }

        public static int GetEffectiveLaborCapacityPercent(
            PersonState person,
            HouseholdState household,
            PopulationState population)
        {
            PersonHealthState health = EnsureHealthInitialized(person);
            int directCapacity = health != null && health.HasActiveCondition
                ? Mathf.Clamp(health.laborCapacityPercent, 0, 100)
                : 100;

            if (household == null || population == null)
            {
                return directCapacity;
            }

            int continuityLossPercent = GetHouseholdContinuityLossPercent(household, population, person != null ? person.id : -1);
            return Mathf.Clamp(Mathf.RoundToInt(directCapacity * (1f - continuityLossPercent / 100f)), 0, 100);
        }

        public static int GetHouseholdContinuityLossPercent(
            HouseholdState household,
            PopulationState population,
            int excludingPersonId = -1)
        {
            if (household == null || population == null || household.memberIds == null)
            {
                return 0;
            }

            float penalty01 = 0f;
            for (int i = 0; i < household.memberIds.Count; i++)
            {
                int memberId = household.memberIds[i];
                if (memberId == excludingPersonId)
                {
                    continue;
                }

                PersonState person = population.GetPerson(memberId);
                PersonHealthState health = EnsureHealthInitialized(person);
                if (health == null || !health.HasActiveCondition)
                {
                    continue;
                }

                penalty01 += health.severity == HealthConditionSeverity.Serious ? 0.14f : 0.05f;
                penalty01 += health.conditionKind == HealthConditionKind.Illness ? 0.04f : 0.02f;

                HealthWorkImpactBand impactBand = GetWorkImpactBand(health, health.laborCapacityPercent);
                if (impactBand == HealthWorkImpactBand.Absent)
                {
                    penalty01 += 0.08f;
                }
                else if (impactBand == HealthWorkImpactBand.Limited)
                {
                    penalty01 += 0.04f;
                }
            }

            return Mathf.RoundToInt(Mathf.Clamp01(penalty01) * 100f);
        }

        public static int GetInjuryRiskPerTenThousand(PersonState person)
        {
            if (!IsLaborMarketWorker(person))
            {
                return 0;
            }

            person?.EnsureWorkerTraitsInitialized();
            float exposure = GetProfessionInjuryExposure01(person);
            float durabilityFactor = person != null && person.hiddenWorkerTraits != null
                ? Mathf.Lerp(1.25f, 0.75f, person.hiddenWorkerTraits.GetValue(WorkerTraitKind.Durability) / 100f)
                : 1f;
            return Mathf.Clamp(Mathf.RoundToInt(BaseInjuryRiskPerTenThousand * exposure * durabilityFactor), 0, 10000);
        }

        public static int GetIllnessRiskPerTenThousand(PersonState person, HouseholdState household)
        {
            int risk = BaseIllnessRiskPerTenThousand;
            if (household != null)
            {
                risk += Mathf.RoundToInt(Mathf.Clamp01(household.crowdingPressure01) * 200f);
                risk += Mathf.RoundToInt(Mathf.Clamp01(household.boarderOverloadPressure01) * 120f);
                risk += Mathf.RoundToInt(Mathf.Clamp01(household.lastHeatingPressure01) * 60f);
            }

            return Mathf.Clamp(risk, 0, 10000);
        }

        public static string BuildConditionSummary(PersonHealthState health)
        {
            if (health == null || !health.HasActiveCondition)
            {
                return "healthy";
            }

            string severityLabel = health.severity == HealthConditionSeverity.Serious ? "serious" : "mild";
            string kindLabel = health.conditionKind == HealthConditionKind.Injury ? "injury" : "illness";
            string impact = GetWorkImpactLabel(GetWorkImpactBand(health, health.laborCapacityPercent)).ToLowerInvariant();
            return $"{severityLabel} {kindLabel}, work {impact}, {health.remainingDays} day(s) remaining";
        }

        public static HealthWorkImpactBand GetWorkImpactBand(PersonState person)
        {
            PersonHealthState health = EnsureHealthInitialized(person);
            int capacityPercent = health != null ? health.laborCapacityPercent : 100;
            return GetWorkImpactBand(health, capacityPercent);
        }

        public static HealthWorkImpactBand GetWorkImpactBand(PersonHealthState health, int effectiveLaborCapacityPercent)
        {
            if (health == null || !health.HasActiveCondition)
            {
                return HealthWorkImpactBand.Full;
            }

            int capacity = Mathf.Clamp(effectiveLaborCapacityPercent, 0, 100);
            if (capacity <= 15)
            {
                return HealthWorkImpactBand.Absent;
            }

            if (capacity <= 50)
            {
                return HealthWorkImpactBand.Limited;
            }

            return capacity < 85
                ? HealthWorkImpactBand.Reduced
                : HealthWorkImpactBand.Full;
        }

        public static string GetWorkImpactLabel(HealthWorkImpactBand band)
        {
            return band switch
            {
                HealthWorkImpactBand.Absent => "Absent",
                HealthWorkImpactBand.Limited => "Limited",
                HealthWorkImpactBand.Reduced => "Reduced",
                _ => "Full"
            };
        }

        private static float GetProfessionInjuryExposure01(PersonState person)
        {
            string combined = string.Concat(
                person != null ? person.professionId : string.Empty,
                " ",
                person != null ? person.professionName : string.Empty).ToLowerInvariant();

            if (combined.Contains("sawmill")
                || combined.Contains("logger")
                || combined.Contains("lumber")
                || combined.Contains("blacksmith")
                || combined.Contains("builder")
                || combined.Contains("wheelwright")
                || combined.Contains("ranch")
                || combined.Contains("farm")
                || combined.Contains("teamster")
                || combined.Contains("freight")
                || combined.Contains("fuel"))
            {
                return 2.4f;
            }

            if (combined.Contains("butcher")
                || combined.Contains("bakery")
                || combined.Contains("grain")
                || combined.Contains("mill"))
            {
                return 1.5f;
            }

            if (combined.Contains("doctor")
                || combined.Contains("tailor")
                || combined.Contains("barber")
                || combined.Contains("clerk")
                || combined.Contains("bookkeeper"))
            {
                return 0.45f;
            }

            return 1f;
        }
    }
}
