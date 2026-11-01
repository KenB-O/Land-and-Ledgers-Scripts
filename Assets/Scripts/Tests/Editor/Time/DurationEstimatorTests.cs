using System;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Time
{
    /// <summary>
    /// Pass IV (Worker 1): method duration estimators compose physical factors —
    /// a 20lb flour sack and an industrial oven must differ; crew helps only within
    /// physical plausibility; the universal estimator selects by definition id and
    /// falls back to base-minutes behavior otherwise.
    /// </summary>
    [TestFixture]
    public sealed class DurationEstimatorTests
    {
        private static TaskDurationInputs HandlingInputs(
            float massKg, float bulk01, float distanceMeters, int crewSize, int itemCount = 1)
        {
            return new TaskDurationInputs
            {
                DefinitionId = "fr.unload-freight",
                ItemCount = itemCount,
                MassKgPerItem = massKg,
                Bulk01 = bulk01,
                DistanceMeters = distanceMeters,
                CrewSize = crewSize,
            };
        }

        [Test]
        public void Handling_FlourSackVsIndustrialOven_DifferStrongly()
        {
            var estimator = new HandlingDurationEstimator();

            // 20lb flour sack: 9.07kg, compact.
            DurationEstimate sack = estimator.Estimate(HandlingInputs(9.07f, 0.1f, 10f, 1));
            // Industrial oven: heavy, bulky, four-person crew.
            DurationEstimate oven = estimator.Estimate(HandlingInputs(180f, 0.9f, 10f, 4));

            Assert.Greater(sack.GameSeconds, 0f);
            Assert.Greater(oven.GameSeconds, sack.GameSeconds * 3f,
                $"oven ({oven.GameSeconds:F0}s) must take far longer than a flour sack ({sack.GameSeconds:F0}s)");
        }

        [Test]
        public void Handling_CrewCappedByPhysicalPlausibility()
        {
            var estimator = new HandlingDurationEstimator();

            DurationEstimate twoCrew = estimator.Estimate(HandlingInputs(9.07f, 0.1f, 10f, 2));
            DurationEstimate eightCrew = estimator.Estimate(HandlingInputs(9.07f, 0.1f, 10f, 8));

            Assert.AreEqual(twoCrew.GameSeconds, eightCrew.GameSeconds, 0.001f,
                "a flour sack needs at most two people; extra crew must not help");
        }

        [Test]
        public void Handling_HeavyLoad_UsesLargerCrew()
        {
            var estimator = new HandlingDurationEstimator();

            DurationEstimate twoCrew = estimator.Estimate(HandlingInputs(180f, 0.9f, 10f, 2));
            DurationEstimate sixCrew = estimator.Estimate(HandlingInputs(180f, 0.9f, 10f, 6));

            Assert.Greater(twoCrew.GameSeconds, sixCrew.GameSeconds,
                "a heavy/bulky load benefits from a larger crew (up to the plausibility cap)");
        }

        [Test]
        public void Handling_MoreDistance_TakesLonger()
        {
            var estimator = new HandlingDurationEstimator();

            DurationEstimate near = estimator.Estimate(HandlingInputs(9.07f, 0.1f, 10f, 1));
            DurationEstimate far = estimator.Estimate(HandlingInputs(9.07f, 0.1f, 40f, 1));

            Assert.Greater(far.GameSeconds, near.GameSeconds);
        }

        [Test]
        public void Handling_WorseAccess_TakesLonger()
        {
            var estimator = new HandlingDurationEstimator();
            var easy = HandlingInputs(9.07f, 0.1f, 10f, 1);
            var hard = HandlingInputs(9.07f, 0.1f, 10f, 1);
            hard.AccessDifficulty01 = 0.9f;

            Assert.Greater(estimator.Estimate(hard).GameSeconds, estimator.Estimate(easy).GameSeconds);
        }

        [Test]
        public void Travel_DoubleDistance_DoubleTime()
        {
            var estimator = new TravelDurationEstimator();
            var near = new TaskDurationInputs { DefinitionId = "travel", DistanceMeters = 1000f };
            var far = new TaskDurationInputs { DefinitionId = "travel", DistanceMeters = 2000f };

            DurationEstimate nearEstimate = estimator.Estimate(near);
            DurationEstimate farEstimate = estimator.Estimate(far);

            Assert.Greater(nearEstimate.GameSeconds, 0f);
            Assert.AreEqual(2f, farEstimate.GameSeconds / nearEstimate.GameSeconds, 0.01f);
        }

        [Test]
        public void Travel_WorseAccess_Slower()
        {
            var estimator = new TravelDurationEstimator();
            var easy = new TaskDurationInputs { DefinitionId = "travel", DistanceMeters = 1000f };
            var hard = new TaskDurationInputs { DefinitionId = "travel", DistanceMeters = 1000f, AccessDifficulty01 = 1f };

            Assert.Greater(estimator.Estimate(hard).GameSeconds, estimator.Estimate(easy).GameSeconds);
        }

        [Test]
        public void Universal_SelectsMethodEstimatorByDefinitionId()
        {
            var universal = new UniversalTaskDurationEstimator(
                new ITaskDurationEstimatorV2[]
                {
                    new HandlingDurationEstimator(new[] { "fr.unload-freight" }),
                    new TravelDurationEstimator(new[] { "travel" }),
                },
                inputs => DurationEstimate.FromGameSeconds(
                    Math.Max(1, inputs.BaseMinutes) * 60f, "test-fallback"));

            DurationEstimate handling = universal.Estimate(HandlingInputs(9.07f, 0.1f, 10f, 1));
            StringAssert.StartsWith("handling:", handling.Basis);

            DurationEstimate travel = universal.Estimate(
                new TaskDurationInputs { DefinitionId = "travel", DistanceMeters = 500f });
            StringAssert.StartsWith("travel:", travel.Basis);

            DurationEstimate fallback = universal.Estimate(
                new TaskDurationInputs { DefinitionId = "test.sweep", BaseMinutes = 10 });
            Assert.AreEqual("test-fallback", fallback.Basis);
            Assert.AreEqual(600f, fallback.GameSeconds, 0.001f);
        }

        [Test]
        public void Universal_FirstMatchingEstimatorWins()
        {
            var universal = new UniversalTaskDurationEstimator(
                new ITaskDurationEstimatorV2[]
                {
                    new HandlingDurationEstimator(new[] { "shared.id" }),
                    new TravelDurationEstimator(new[] { "shared.id" }),
                },
                inputs => DurationEstimate.FromGameSeconds(60f, "test-fallback"));

            DurationEstimate estimate = universal.Estimate(
                new TaskDurationInputs { DefinitionId = "shared.id", DistanceMeters = 10f });
            StringAssert.StartsWith("handling:", estimate.Basis);
        }

        [Test]
        public void Bridge_FallbackPreservesBaseMinutesThroughAuthority()
        {
            var authority = new TaskAuthority();
            string reason;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition("test.sweep", "Sweep", 10), out reason), reason);

            var universal = new UniversalTaskDurationEstimator(
                Array.Empty<ITaskDurationEstimatorV2>(),
                inputs => DurationEstimate.FromGameSeconds(
                    Math.Max(1, inputs.BaseMinutes) * 60f, "test-fallback"));
            authority.SetDurationEstimatorV2(universal);

            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            WorkTask task = authority.CreateTask("test.sweep", EntityId.For(EntityKind.Business, 1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, EntityId.For(EntityKind.Person, 1), budgets, out reason), reason);

            Assert.AreEqual(10, task.PlannedMinutes,
                "with no method estimator owning the id, the base-minutes fallback must be preserved");
        }

        [Test]
        public void Bridge_LegacySeamStillWorks()
        {
            var authority = new TaskAuthority();
            string reason;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition("test.sweep", "Sweep", 10), out reason), reason);

            // The pre-Pass IV seam is untouched: a V1 estimator installs directly.
            authority.SetDurationEstimator(new DefaultTaskDurationEstimator());

            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            WorkTask task = authority.CreateTask("test.sweep", EntityId.For(EntityKind.Business, 1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, EntityId.For(EntityKind.Person, 1), budgets, out reason), reason);
            Assert.AreEqual(10, task.PlannedMinutes);
        }
    }
}
