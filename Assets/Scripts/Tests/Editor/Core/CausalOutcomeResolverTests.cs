using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Core
{
    /// <summary>
    /// Pass III Worker D: causal task-outcome resolution.
    /// Canon: Tech XIV Part II §2.4 (no flat SkillMultiplier, no hard gates);
    /// XXXI §3.3 (competence changes execution — novice work is possible, not
    /// equally effective); XXXI §3.1 CANON LOCK (no hard skill gates).
    /// </summary>
    [TestFixture]
    public sealed class CausalOutcomeResolverTests
    {
        private static CausalOutcomeInputs BaseInputs()
        {
            return new CausalOutcomeInputs
            {
                BaseMinutes = 60,
                Capability01 = 0.5,
                ExperienceRelevance01 = 0.5,
                Difficulty01 = 0.5,
                Supervised = false,
                EquipmentCondition01 = 1.0,
                Fatigue01 = 0.0,
                TimePressure01 = 0.0,
                InstructionQuality01 = 0.5,
            };
        }

        [Test]
        public void Novice_TakesLonger_AndRisksMore_ThanExpert_OnSameTask()
        {
            var resolver = new CausalTaskOutcomeResolver();

            CausalOutcomeInputs novice = BaseInputs();
            novice.Capability01 = 0.10;
            novice.ExperienceRelevance01 = 0.0;

            CausalOutcomeInputs expert = BaseInputs();
            expert.Capability01 = 0.90;
            expert.ExperienceRelevance01 = 0.9;

            CausalOutcomeResult noviceResult = resolver.Resolve(novice);
            CausalOutcomeResult expertResult = resolver.Resolve(expert);

            // Duration differs: novice slower, expert quicker — but never a
            // flat multiplier, and never below half the base.
            Assert.Greater(noviceResult.DurationMinutes, 60.0);
            Assert.Less(expertResult.DurationMinutes, 60.0);
            Assert.GreaterOrEqual(expertResult.DurationMinutes, 30.0);

            // Quality differs: novice below ordinary, expert above.
            Assert.Less(noviceResult.QualityDelta, 0.0);
            Assert.Greater(expertResult.QualityDelta, 0.0);

            // Waste and failure risk differ.
            Assert.Greater(noviceResult.WasteFactor, expertResult.WasteFactor);
            Assert.Greater(noviceResult.FailureRisk01, expertResult.FailureRisk01);
            Assert.Greater(noviceResult.ReworkMinutes, expertResult.ReworkMinutes);
        }

        [Test]
        public void NoHardGates_NoviceCanStillComplete()
        {
            var resolver = new CausalTaskOutcomeResolver();

            // A complete beginner on a hard task: slow, wasteful, risky —
            // but the resolver never refuses (XXXI §3.1: no hard gates).
            CausalOutcomeInputs inputs = BaseInputs();
            inputs.Capability01 = 0.0;
            inputs.ExperienceRelevance01 = 0.0;
            inputs.Difficulty01 = 1.0;

            CausalOutcomeResult result = resolver.Resolve(inputs);
            Assert.Greater(result.DurationMinutes, 0.0);
            Assert.IsTrue(result.DurationMinutes <= 60.0 * 3.0);
            Assert.IsTrue(result.FailureRisk01 <= 0.95);
        }

        [Test]
        public void Supervision_Fatigue_AndEquipment_MoveOutcomesCausally()
        {
            var resolver = new CausalTaskOutcomeResolver();

            CausalOutcomeInputs supervised = BaseInputs();
            supervised.Capability01 = 0.3;
            supervised.ExperienceRelevance01 = 0.2;
            supervised.Supervised = true;

            CausalOutcomeInputs alone = BaseInputs();
            alone.Capability01 = 0.3;
            alone.ExperienceRelevance01 = 0.2;

            Assert.Less(resolver.Resolve(supervised).FailureRisk01,
                resolver.Resolve(alone).FailureRisk01);

            CausalOutcomeInputs tired = BaseInputs();
            tired.Fatigue01 = 0.8;
            CausalOutcomeResult tiredResult = resolver.Resolve(tired);
            CausalOutcomeResult freshResult = resolver.Resolve(BaseInputs());
            Assert.Greater(tiredResult.DurationMinutes, freshResult.DurationMinutes);
            Assert.Less(tiredResult.QualityDelta, freshResult.QualityDelta);

            CausalOutcomeInputs brokenTools = BaseInputs();
            brokenTools.EquipmentCondition01 = 0.2;
            Assert.Greater(resolver.Resolve(brokenTools).DurationMinutes,
                resolver.Resolve(BaseInputs()).DurationMinutes);
        }

        [Test]
        public void OutcomeDetermination_IsDeterministic_AndVariesByWorker()
        {
            var resolver = new CausalTaskOutcomeResolver();

            CausalOutcomeInputs noviceInputs = BaseInputs();
            noviceInputs.Capability01 = 0.05;
            noviceInputs.ExperienceRelevance01 = 0.0;
            noviceInputs.Difficulty01 = 0.8;
            CausalOutcomeResult noviceResult = resolver.Resolve(noviceInputs);

            CausalOutcomeInputs expertInputs = BaseInputs();
            expertInputs.Capability01 = 0.95;
            expertInputs.ExperienceRelevance01 = 0.9;
            expertInputs.Difficulty01 = 0.8;
            CausalOutcomeResult expertResult = resolver.Resolve(expertInputs);

            // Deterministic: same inputs always give the same outcome.
            TaskOutcome first = TaskOutcomeDetermination.DetermineOutcome(
                noviceResult, "task-1", "p1", 10);
            TaskOutcome second = TaskOutcomeDetermination.DetermineOutcome(
                noviceResult, "task-1", "p1", 10);
            Assert.AreEqual(first, second);

            // Across many deterministic draws, the novice fails or varies more
            // often than the expert on the same hard task.
            int noviceBad = 0, expertBad = 0;
            for (int i = 0; i < 50; i++)
            {
                TaskOutcome n = TaskOutcomeDetermination.DetermineOutcome(
                    noviceResult, "task-" + i, "novice", 10);
                TaskOutcome e = TaskOutcomeDetermination.DetermineOutcome(
                    expertResult, "task-" + i, "expert", 10);
                if (n == TaskOutcome.RecoverableFailure || n == TaskOutcome.SeriousFailure ||
                    n == TaskOutcome.AcceptableWithVariation) noviceBad++;
                if (e == TaskOutcome.RecoverableFailure || e == TaskOutcome.SeriousFailure ||
                    e == TaskOutcome.AcceptableWithVariation) expertBad++;
            }
            Assert.Greater(noviceBad, expertBad);
            Assert.AreEqual(0, expertBad); // expert on this task: reliable
        }

        [Test]
        public void SkillService_ResolveTaskCompletion_VariesOutcome_AndRecordsExperience()
        {
            var skills = new SkillService();
            skills.RegisterStarterSet();

            EntityId noviceId = EntityId.For(EntityKind.Person, 50);
            EntityId expertId = EntityId.For(EntityKind.Person, 51);

            // Give the expert real capability through recorded episodes.
            for (int day = 1; day <= 60; day++)
            {
                skills.RecordTaskExperience(expertId, new ExperienceFormationInputs
                {
                    TaskCapabilityId = SkillIds.Stocking,
                    Difficulty01 = 0.7,
                    Novelty01 = 0.9,
                    Supervised = false,
                    Outcome = "Success",
                    MinutesSpent = 480,
                    Day = day,
                });
            }

            int noviceBad = 0, expertBad = 0;
            for (int i = 0; i < 30; i++)
            {
                CausalOutcomeInputs nInputs = skills.BuildCausalInputs(noviceId, SkillIds.Stocking, 120);
                nInputs.Difficulty01 = 0.75;
                TaskOutcome nOut = skills.ResolveTaskCompletion(
                    noviceId, SkillIds.Stocking, nInputs,
                    difficulty01: 0.75, novelty01: 0.6, supervised: false,
                    day: 100, taskIdForDeterminism: "shelf-" + i);
                if (nOut != TaskOutcome.Success) noviceBad++;

                CausalOutcomeInputs eInputs = skills.BuildCausalInputs(expertId, SkillIds.Stocking, 120);
                eInputs.Difficulty01 = 0.75;
                TaskOutcome eOut = skills.ResolveTaskCompletion(
                    expertId, SkillIds.Stocking, eInputs,
                    difficulty01: 0.75, novelty01: 0.1, supervised: false,
                    day: 100, taskIdForDeterminism: "shelf-" + i);
                if (eOut != TaskOutcome.Success) expertBad++;
            }

            // The novice varies/fails sometimes; the expert is steady.
            Assert.Greater(noviceBad, 0);
            Assert.Greater(noviceBad, expertBad);
            // Both recorded real experience episodes.
            Assert.Greater(skills.Experience.Count, 0);
        }
    }
}
