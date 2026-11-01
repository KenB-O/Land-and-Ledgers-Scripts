using System;
using System.Collections.Generic;
using LandLedgers.Population;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Doctor
{
    /// <summary>D1A: which treatment work the outcome describes (mirrors DoctorTreatmentCatalog task ids).</summary>
    public enum DoctorProcedureKind
    {
        OfficeVisit = 0,
        DispenseRemedy = 1,
        MinorProcedure = 2,
        HouseCall = 3,
    }

    /// <summary>
    /// D1A: honest treatment outcome. Canon §13.3B: "Treatment can reduce
    /// absence, complications or death risk where historically plausible, but
    /// it never guarantees recovery and should not erase the underlying
    /// severity." NoBenefit is always possible; severity steps down at most
    /// one band, never straight to cured — recovery runs through the existing
    /// health-tick model, which stays authoritative.
    /// </summary>
    public enum DoctorOutcomeTier
    {
        Untreated = 0,
        NoBenefit = 1,
        PartialBenefit = 2,
        FullBenefit = 3,
    }

    /// <summary>D1A: input to the outcome engine.</summary>
    [Serializable]
    public sealed class DoctorOutcomeInput
    {
        public DoctorProcedureKind Procedure = DoctorProcedureKind.OfficeVisit;
        public HealthConditionKind ConditionKind = HealthConditionKind.Illness;
        public HealthConditionSeverity ConditionSeverity = HealthConditionSeverity.Mild;

        /// <summary>True when the procedure actually had its supplies (remedy dose, dressing) to work with.</summary>
        public bool SuppliesPresent = true;

        public DoctorOutcomeInput() { }
    }

    /// <summary>D1A: one resolved outcome — absence reduction, possible one-step severity downgrade, trust effect.</summary>
    [Serializable]
    public sealed class DoctorTreatmentOutcome
    {
        public DoctorOutcomeTier Tier = DoctorOutcomeTier.Untreated;
        public int DaysReduced;
        public bool SeverityDowngraded;
        public string Summary = string.Empty;

        /// <summary>Trust ledger delta for this outcome (calibration; applied by the caller via DoctorReputationLedger).</summary>
        public int TrustDelta;

        public DoctorTreatmentOutcome() { }
    }

    /// <summary>
    /// D1A: the treatment-outcome engine — Canon §13.3B depth the width pass
    /// left as a placeholder string. Pure data in, resolved outcome out; the
    /// caller applies it to the health state via <see cref="ApplyOutcome"/>
    /// (which routes through the existing PersonHealthState authority — never
    /// re-implemented here) and records it on treatment records. Deterministic
    /// given the caller's RNG. All bands are calibration (Canon Part XV).
    /// </summary>
    public static class DoctorTreatmentOutcomes
    {
        private struct OutcomeBand
        {
            public int MinDays;
            public int MaxDays;
            public float DowngradeChance;
            public float NoBenefitChance;
            public string Note;
        }

        private static OutcomeBand Band(DoctorOutcomeInput input, out bool wrongTool)
        {
            wrongTool = false;
            bool serious = input.ConditionSeverity == HealthConditionSeverity.Serious;
            bool injury = input.ConditionKind == HealthConditionKind.Injury;

            // Minor procedures are wound work: full value on injury, exam-only on illness.
            if (input.Procedure == DoctorProcedureKind.MinorProcedure && !injury)
            {
                wrongTool = true;
                return new OutcomeBand
                {
                    MinDays = 1, MaxDays = 2, DowngradeChance = 0f, NoBenefitChance = 0.20f,
                    Note = "minor procedure on illness: exam only, no wound to treat",
                };
            }

            if (input.Procedure == DoctorProcedureKind.MinorProcedure) // injury
            {
                return serious
                    ? new OutcomeBand { MinDays = 2, MaxDays = 4, DowngradeChance = 0.50f, NoBenefitChance = 0.10f, Note = "wound dressing / splinting of a serious injury" }
                    : new OutcomeBand { MinDays = 2, MaxDays = 4, DowngradeChance = 0f, NoBenefitChance = 0.10f, Note = "wound dressing of a mild injury" };
            }

            if (input.Procedure == DoctorProcedureKind.DispenseRemedy)
            {
                return injury
                    ? new OutcomeBand { MinDays = 0, MaxDays = 1, DowngradeChance = 0f, NoBenefitChance = 0.40f, Note = "remedy on injury: pain relief at best" }
                    : new OutcomeBand { MinDays = 1, MaxDays = 2, DowngradeChance = 0f, NoBenefitChance = 0.15f, Note = "remedy course for illness" };
            }

            if (input.Procedure == DoctorProcedureKind.HouseCall)
            {
                // Bedside care of the severe cases the office cannot take.
                if (serious && injury)
                    return new OutcomeBand { MinDays = 2, MaxDays = 4, DowngradeChance = 0.40f, NoBenefitChance = 0.15f, Note = "bedside care of a serious injury" };
                if (serious)
                    return new OutcomeBand { MinDays = 2, MaxDays = 4, DowngradeChance = 0.35f, NoBenefitChance = 0.15f, Note = "bedside care of a serious illness" };
                return new OutcomeBand { MinDays = 1, MaxDays = 3, DowngradeChance = 0f, NoBenefitChance = 0.15f, Note = "bedside care of a mild case" };
            }

            // OfficeVisit.
            if (injury)
            {
                return serious
                    ? new OutcomeBand { MinDays = 1, MaxDays = 2, DowngradeChance = 0.15f, NoBenefitChance = 0.30f, Note = "office care of a serious injury" }
                    : new OutcomeBand { MinDays = 1, MaxDays = 3, DowngradeChance = 0f, NoBenefitChance = 0.15f, Note = "office care of a mild injury" };
            }

            return serious
                ? new OutcomeBand { MinDays = 1, MaxDays = 3, DowngradeChance = 0.25f, NoBenefitChance = 0.20f, Note = "office care of a serious illness" }
                : new OutcomeBand { MinDays = 2, MaxDays = 4, DowngradeChance = 0f, NoBenefitChance = 0.10f, Note = "office care of a mild illness" };
        }

        /// <summary>Resolves one treatment outcome. Deterministic given rng; rng may not be null.</summary>
        public static DoctorTreatmentOutcome ResolveOutcome(
            DoctorOutcomeInput input, DoctorPractitionerSkill skill, System.Random rng)
        {
            var outcome = new DoctorTreatmentOutcome();
            if (input == null || rng == null)
            {
                outcome.Summary = "no outcome resolved (missing input)";
                return outcome;
            }

            // No supplies, no procedure: the attempt is recorded honestly, not faked.
            if (!input.SuppliesPresent && input.Procedure != DoctorProcedureKind.OfficeVisit)
            {
                outcome.Tier = DoctorOutcomeTier.Untreated;
                outcome.Summary = "untreated: supplies unavailable";
                outcome.TrustDelta = -2;
                return outcome;
            }

            OutcomeBand band = Band(input, out bool wrongTool);

            float noBenefitChance = band.NoBenefitChance;
            float downgradeChance = band.DowngradeChance;
            int maxDays = band.MaxDays;
            switch (skill)
            {
                case DoctorPractitionerSkill.Basic:
                    noBenefitChance += 0.10f;
                    downgradeChance -= 0.10f;
                    break;
                case DoctorPractitionerSkill.Experienced:
                    noBenefitChance -= 0.05f;
                    downgradeChance += 0.10f;
                    maxDays += 1;
                    break;
            }

            noBenefitChance = Mathf.Clamp01(noBenefitChance);
            downgradeChance = Mathf.Clamp01(downgradeChance);

            if (rng.NextDouble() < noBenefitChance)
            {
                outcome.Tier = DoctorOutcomeTier.NoBenefit;
                outcome.Summary = $"treated ({band.Note}): no benefit observed — recovery not guaranteed";
                outcome.TrustDelta = -1;
                return outcome;
            }

            outcome.DaysReduced = rng.Next(band.MinDays, maxDays + 1);
            bool canDowngrade = input.ConditionSeverity == HealthConditionSeverity.Serious && downgradeChance > 0f;
            outcome.SeverityDowngraded = canDowngrade && rng.NextDouble() < downgradeChance;

            bool full = outcome.DaysReduced >= maxDays && (!canDowngrade || outcome.SeverityDowngraded);
            outcome.Tier = full ? DoctorOutcomeTier.FullBenefit : DoctorOutcomeTier.PartialBenefit;
            outcome.TrustDelta = full ? 2 : 1;
            outcome.Summary = $"treated ({band.Note}): {(full ? "full" : "partial")} benefit — " +
                $"{outcome.DaysReduced} day(s) eased" +
                (outcome.SeverityDowngraded ? ", severity stepped down" : "") +
                (wrongTool ? " (wrong tool for the ailment)" : "") +
                " — recovery not guaranteed";
            return outcome;
        }

        /// <summary>
        /// Applies a resolved outcome to a health state through the existing
        /// PersonHealthState authority. Severity steps down at most one band
        /// (Serious -&gt; Mild); it is never erased outright by treatment.
        /// </summary>
        public static void ApplyOutcome(
            PersonHealthState health,
            DoctorTreatmentOutcome outcome,
            int absoluteDayIndex,
            string summaryPrefix)
        {
            if (health == null || outcome == null || !health.HasActiveCondition) return;
            if (outcome.Tier == DoctorOutcomeTier.Untreated || outcome.Tier == DoctorOutcomeTier.NoBenefit)
            {
                health.lastTreatedDayIndex = absoluteDayIndex;
                health.lastTreatmentSummary = string.IsNullOrWhiteSpace(summaryPrefix)
                    ? outcome.Summary
                    : $"{summaryPrefix}: {outcome.Summary}";
                return;
            }

            HealthConditionSeverity resolved = HealthConditionSeverity.None;
            if (outcome.SeverityDowngraded && health.severity == HealthConditionSeverity.Serious)
            {
                resolved = HealthConditionSeverity.Mild;
            }

            string summary = string.IsNullOrWhiteSpace(summaryPrefix)
                ? outcome.Summary
                : $"{summaryPrefix}: {outcome.Summary}";
            health.ApplyTreatment(absoluteDayIndex, outcome.DaysReduced, summary, resolved);
        }
    }
}
