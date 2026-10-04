using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Doctor;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1A: treatment outcomes resolve per procedure with honest uncertainty —
    /// Canon §13.3B: treatment can reduce absence but never guarantees recovery
    /// and never erases severity outright.
    /// </summary>
    [TestFixture]
    public sealed class DoctorTreatmentOutcomesTests
    {
        private static DoctorOutcomeInput Input(
            DoctorProcedureKind procedure,
            HealthConditionKind kind,
            HealthConditionSeverity severity)
        {
            return new DoctorOutcomeInput
            {
                Procedure = procedure,
                ConditionKind = kind,
                ConditionSeverity = severity,
                SuppliesPresent = true,
            };
        }

        [Test]
        public void ResolveOutcome_NullInputOrRng_NeverThrows()
        {
            var outcome = DoctorTreatmentOutcomes.ResolveOutcome(null, DoctorPractitionerSkill.Competent, new Random(1));
            Assert.AreEqual(DoctorOutcomeTier.Untreated, outcome.Tier);

            outcome = DoctorTreatmentOutcomes.ResolveOutcome(
                Input(DoctorProcedureKind.OfficeVisit, HealthConditionKind.Illness, HealthConditionSeverity.Mild),
                DoctorPractitionerSkill.Competent, null);
            Assert.AreEqual(DoctorOutcomeTier.Untreated, outcome.Tier);
        }

        [Test]
        public void ResolveOutcome_NoSupplies_NonOfficeProcedure_Untreated()
        {
            var input = Input(DoctorProcedureKind.HouseCall, HealthConditionKind.Illness, HealthConditionSeverity.Serious);
            input.SuppliesPresent = false;

            var outcome = DoctorTreatmentOutcomes.ResolveOutcome(input, DoctorPractitionerSkill.Experienced, new Random(2));

            Assert.AreEqual(DoctorOutcomeTier.Untreated, outcome.Tier);
            Assert.AreEqual(-2, outcome.TrustDelta);
            Assert.AreEqual(0, outcome.DaysReduced);
        }

        [Test]
        public void ResolveOutcome_DeterministicGivenSeed()
        {
            var input = Input(DoctorProcedureKind.HouseCall, HealthConditionKind.Injury, HealthConditionSeverity.Serious);

            var first = DoctorTreatmentOutcomes.ResolveOutcome(input, DoctorPractitionerSkill.Competent, new Random(42));
            var second = DoctorTreatmentOutcomes.ResolveOutcome(input, DoctorPractitionerSkill.Competent, new Random(42));

            Assert.AreEqual(first.Tier, second.Tier);
            Assert.AreEqual(first.DaysReduced, second.DaysReduced);
            Assert.AreEqual(first.SeverityDowngraded, second.SeverityDowngraded);
        }

        [Test]
        public void ResolveOutcome_UncertaintyRemains_BenefitAndNoBenefitBothOccur()
        {
            var input = Input(DoctorProcedureKind.OfficeVisit, HealthConditionKind.Illness, HealthConditionSeverity.Serious);
            var rng = new Random(7);
            bool sawBenefit = false;
            bool sawNoBenefit = false;

            for (int i = 0; i < 500; i++)
            {
                var outcome = DoctorTreatmentOutcomes.ResolveOutcome(input, DoctorPractitionerSkill.Competent, rng);
                if (outcome.Tier == DoctorOutcomeTier.NoBenefit) sawNoBenefit = true;
                if (outcome.Tier == DoctorOutcomeTier.PartialBenefit || outcome.Tier == DoctorOutcomeTier.FullBenefit) sawBenefit = true;
                Assert.IsNotEmpty(outcome.Summary);
            }

            Assert.IsTrue(sawBenefit, "Treatment must sometimes help (Canon §13.3B).");
            Assert.IsTrue(sawNoBenefit, "Treatment must never guarantee recovery (Canon §13.3B).");
        }

        [Test]
        public void ResolveOutcome_DaysReduced_StaysWithinBand()
        {
            var input = Input(DoctorProcedureKind.MinorProcedure, HealthConditionKind.Injury, HealthConditionSeverity.Serious);
            var rng = new Random(11);

            for (int i = 0; i < 300; i++)
            {
                var outcome = DoctorTreatmentOutcomes.ResolveOutcome(input, DoctorPractitionerSkill.Competent, rng);
                if (outcome.Tier == DoctorOutcomeTier.NoBenefit)
                {
                    Assert.AreEqual(0, outcome.DaysReduced);
                }
                else
                {
                    Assert.GreaterOrEqual(outcome.DaysReduced, 2);
                    Assert.LessOrEqual(outcome.DaysReduced, 4);
                }
            }
        }

        [Test]
        public void ResolveOutcome_ExperiencedPractitioner_BeatsBasicOnAverage()
        {
            var input = Input(DoctorProcedureKind.OfficeVisit, HealthConditionKind.Illness, HealthConditionSeverity.Mild);

            double basicMean = MeanDays(input, DoctorPractitionerSkill.Basic, 2000, 101);
            double experiencedMean = MeanDays(input, DoctorPractitionerSkill.Experienced, 2000, 101);

            Assert.Greater(experiencedMean, basicMean,
                $"Skill must shift odds, not erase uncertainty (basic {basicMean:F2} vs experienced {experiencedMean:F2}).");
        }

        private static double MeanDays(DoctorOutcomeInput input, DoctorPractitionerSkill skill, int draws, int seed)
        {
            var rng = new Random(seed);
            double total = 0;
            for (int i = 0; i < draws; i++)
            {
                total += DoctorTreatmentOutcomes.ResolveOutcome(input, skill, rng).DaysReduced;
            }

            return total / draws;
        }

        [Test]
        public void ResolveOutcome_MinorProcedureOnIllness_WrongTool_Noted()
        {
            var input = Input(DoctorProcedureKind.MinorProcedure, HealthConditionKind.Illness, HealthConditionSeverity.Mild);
            var rng = new Random(13);
            bool sawNote = false;

            for (int i = 0; i < 100; i++)
            {
                var outcome = DoctorTreatmentOutcomes.ResolveOutcome(input, DoctorPractitionerSkill.Competent, rng);
                if (outcome.Summary.Contains("wrong tool")) sawNote = true;
            }

            Assert.IsTrue(sawNote, "Wound work on an illness must say so honestly.");
        }

        [Test]
        public void ApplyOutcome_PartialBenefit_ReducesRemainingDays()
        {
            var health = new PersonHealthState();
            health.ApplyCondition(HealthConditionKind.Illness, HealthConditionSeverity.Mild, 10, "test case");

            var outcome = new DoctorTreatmentOutcome
            {
                Tier = DoctorOutcomeTier.PartialBenefit,
                DaysReduced = 3,
                Summary = "treated: partial benefit",
            };

            DoctorTreatmentOutcomes.ApplyOutcome(health, outcome, 400, "house call");

            Assert.AreEqual(7, health.remainingDays);
            Assert.IsTrue(health.HasActiveCondition, "Treatment must not erase the condition outright.");
            Assert.AreEqual(400, health.lastTreatedDayIndex);
            StringAssert.Contains("house call", health.lastTreatmentSummary);
        }

        [Test]
        public void ApplyOutcome_SeverityDowngrade_StepsDownOneBandOnly()
        {
            var health = new PersonHealthState();
            health.ApplyCondition(HealthConditionKind.Injury, HealthConditionSeverity.Serious, 12, "test case");

            var outcome = new DoctorTreatmentOutcome
            {
                Tier = DoctorOutcomeTier.FullBenefit,
                DaysReduced = 2,
                SeverityDowngraded = true,
                Summary = "treated: full benefit",
            };

            DoctorTreatmentOutcomes.ApplyOutcome(health, outcome, 401, null);

            Assert.AreEqual(HealthConditionSeverity.Mild, health.severity,
                "Canon §13.3B: treatment must not erase the underlying severity — one step down, never to cured.");
            Assert.IsTrue(health.HasActiveCondition);
        }

        [Test]
        public void ApplyOutcome_NoBenefit_KeepsDays_RecordsAttempt()
        {
            var health = new PersonHealthState();
            health.ApplyCondition(HealthConditionKind.Illness, HealthConditionSeverity.Mild, 6, "test case");

            var outcome = new DoctorTreatmentOutcome
            {
                Tier = DoctorOutcomeTier.NoBenefit,
                Summary = "treated: no benefit observed",
            };

            DoctorTreatmentOutcomes.ApplyOutcome(health, outcome, 402, null);

            Assert.AreEqual(6, health.remainingDays);
            Assert.AreEqual(402, health.lastTreatedDayIndex, "The attempt is still recorded.");
        }
    }
}
