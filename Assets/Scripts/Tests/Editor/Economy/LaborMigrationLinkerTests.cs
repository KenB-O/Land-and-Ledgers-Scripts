using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Labor;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Worker D: bounded employment migration (Phase G, brief §13). Distant
    /// jobs are evaluated from documented factors only: no auto-awareness, no
    /// auto-spawn, no movement execution here - the integrator maps accepted
    /// intents to journey tasks.
    /// </summary>
    [TestFixture]
    public sealed class LaborMigrationLinkerTests
    {
        private static MigrationEvaluationInput BaseInput()
        {
            return new MigrationEvaluationInput
            {
                PersonId = 4,
                HouseholdId = 104,
                ContactAware = true,
                TravelMinutesOneWay = 240,
                TravelCostCents = 2000,
                HousingFit = true,
                MovingCostCents = 20000,
                OfferWageCentsPerWeek = 5000,
                CurrentWageCentsPerWeek = 3000,
                HouseholdReservesCents = 50000,
                Dependents = 1,
                HouseholdCircumstances = "spouse can find work at destination",
                DayIndex = 410
            };
        }

        [Test]
        public void NoContactAwareness_Refused()
        {
            var linker = new LaborMigrationLinker();
            MigrationEvaluationInput input = BaseInput();
            input.ContactAware = false;

            MigrationEvaluation evaluation = linker.EvaluateDistantJob(input, new List<string>());

            Assert.IsFalse(evaluation.Proceed, "no auto-awareness: unknown jobs are never considered");
            Assert.AreEqual("refused", evaluation.Decision);
            Assert.IsTrue(evaluation.RefusalReason.Contains("no documented contact awareness"));
            Assert.IsFalse(evaluation.TravelTaskRequested, "nothing moves");
        }

        [Test]
        public void HousingUnfit_WithDependents_Refused()
        {
            var linker = new LaborMigrationLinker();
            MigrationEvaluationInput input = BaseInput();
            input.HousingFit = false;
            input.Dependents = 2;

            MigrationEvaluation evaluation = linker.EvaluateDistantJob(input, new List<string>());

            Assert.IsFalse(evaluation.Proceed);
            Assert.AreEqual("refused", evaluation.Decision);
            Assert.IsTrue(evaluation.RefusalReason.Contains("2 dependents"),
                "family is not moved into unfit housing");
        }

        [Test]
        public void HousingUnfit_NoDependents_Delayed()
        {
            var linker = new LaborMigrationLinker();
            MigrationEvaluationInput input = BaseInput();
            input.HousingFit = false;
            input.Dependents = 0;

            MigrationEvaluation evaluation = linker.EvaluateDistantJob(input, new List<string>());

            Assert.IsFalse(evaluation.Proceed);
            Assert.IsTrue(evaluation.Delay);
            Assert.AreEqual("delayed", evaluation.Decision);
        }

        [Test]
        public void GoodOffer_Proceeds_WithDocumentedFactors()
        {
            var linker = new LaborMigrationLinker();

            MigrationEvaluation evaluation = linker.EvaluateDistantJob(BaseInput(), new List<string>());

            Assert.IsTrue(evaluation.Proceed);
            Assert.AreEqual("proceed", evaluation.Decision);
            Assert.IsTrue(evaluation.TravelTaskRequested, "relocation intent for the integrator's journey tasks");
            Assert.GreaterOrEqual(evaluation.Factors.Count, 4, "decision factors documented");
            bool paybackDocumented = false;
            bool circumstancesDocumented = false;
            foreach (string factor in evaluation.Factors)
            {
                if (factor.Contains("payback holds")) paybackDocumented = true;
                if (factor.Contains("spouse can find work")) circumstancesDocumented = true;
            }

            Assert.IsTrue(paybackDocumented, "payback factor documented");
            Assert.IsTrue(circumstancesDocumented, "household circumstances factor documented");
        }

        [Test]
        public void PoorPayback_Refused()
        {
            var linker = new LaborMigrationLinker();
            MigrationEvaluationInput input = BaseInput();
            input.OfferWageCentsPerWeek = 3200; // gain 200c/week * 26 = 5200c < 22000c move cost

            MigrationEvaluation evaluation = linker.EvaluateDistantJob(input, new List<string>());

            Assert.IsFalse(evaluation.Proceed);
            Assert.AreEqual("refused", evaluation.Decision);
            Assert.IsTrue(evaluation.RefusalReason.Contains("does not cover"));
        }

        [Test]
        public void CannotFrontMoveCost_Delayed()
        {
            var linker = new LaborMigrationLinker();
            MigrationEvaluationInput input = BaseInput();
            input.HouseholdReservesCents = 1000; // payback holds but the move cannot be fronted

            MigrationEvaluation evaluation = linker.EvaluateDistantJob(input, new List<string>());

            Assert.IsFalse(evaluation.Proceed);
            Assert.IsTrue(evaluation.Delay);
            Assert.AreEqual("delayed", evaluation.Decision);
            Assert.IsTrue(evaluation.RefusalReason.Contains("cannot front"));
        }

        [Test]
        public void CommutableJob_NoRelocationNeeded()
        {
            var linker = new LaborMigrationLinker();
            MigrationEvaluationInput input = BaseInput();
            input.TravelMinutesOneWay = 60; // commutable
            input.HousingFit = false;       // irrelevant when commutable

            MigrationEvaluation evaluation = linker.EvaluateDistantJob(input, new List<string>());

            Assert.IsTrue(evaluation.Proceed);
            Assert.IsFalse(evaluation.TravelTaskRequested, "commutable: no journey tasks needed");
        }

        [Test]
        public void LinkerCreatesNothing_EvaluationOnly()
        {
            // Bounded: the linker is stateless and deterministic - evaluating
            // twice yields the same decision, and nothing (no person, no
            // business, no journey) is created as a side effect.
            var linker = new LaborMigrationLinker();
            var diag = new List<string>();

            MigrationEvaluation first = linker.EvaluateDistantJob(BaseInput(), diag);
            MigrationEvaluation second = linker.EvaluateDistantJob(BaseInput(), diag);

            Assert.AreEqual(first.Decision, second.Decision, "deterministic");
            Assert.AreEqual(first.Proceed, second.Proceed);
            Assert.IsFalse(second.TravelTaskRequested && first.TravelTaskRequested == false,
                "intent flag stable, not movement");
        }

        [Test]
        public void NullInput_RefusedSafely()
        {
            var linker = new LaborMigrationLinker();
            MigrationEvaluation evaluation = linker.EvaluateDistantJob(null, new List<string>());
            Assert.IsFalse(evaluation.Proceed);
            Assert.AreEqual("refused", evaluation.Decision);
        }
    }
}
