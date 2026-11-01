using LandLedgers.Orchestration.Scenarios.Generation;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>Pass IX review R-08: the scenario objective completion
    /// latch. A dated qualifier met by its deadline is complete for the
    /// rest of the run — later predicate changes (the asset was sold, the
    /// balance was spent) cannot retroactively fail a goal that was met
    /// in time. A qualifier never met still fails the run exactly once.</summary>
    [TestFixture]
    public sealed class Pass9ReviewFixScenarioLatchTests
    {
        private const string GoalId = "ash-creek-saloon-ownership";
        private const int DeadlineDay = 100;

        [TearDown]
        public void TearDown()
        {
            ScenarioRunStateEvents.ClearSubscribers();
        }

        [Test]
        public void MetBeforeDeadline_ThenPredicateTurnsFalse_RunNeverFails()
        {
            int eventCount = 0;
            ScenarioRunStateEvents.ScenarioFailed += (_, _) => eventCount++;
            var service = new ScenarioObjectiveService();
            service.BeginScenario(ScenarioObjectiveService.FirstLedgerScenarioId);
            bool ownsSaloon = true;
            service.RegisterDatedQualifier(GoalId, DeadlineDay, () => ownsSaloon);

            Assert.IsFalse(service.Evaluate(50), "met, on time — no failure");
            Assert.Contains(GoalId, service.State.LatchedCompleteGoalIds,
                "meeting the qualifier latches it complete in the run state");

            ownsSaloon = false; // the asset is later sold — the goal was still met by its deadline
            Assert.IsFalse(service.Evaluate(DeadlineDay + 1));
            Assert.IsFalse(service.Evaluate(DeadlineDay + 500));
            Assert.IsFalse(service.IsFailed, "a latched goal can never fail the run afterwards");
            Assert.AreEqual(0, eventCount);
            Assert.AreEqual(0, service.State.FailedGoalIds.Count);
        }

        [Test]
        public void Latch_PersistsAcrossSaveLoad()
        {
            var service = new ScenarioObjectiveService();
            service.BeginScenario(ScenarioObjectiveService.FirstLedgerScenarioId);
            service.RegisterDatedQualifier(GoalId, DeadlineDay, () => true);
            Assert.IsFalse(service.Evaluate(DeadlineDay));

            ScenarioRunState saved = service.CaptureRunState();
            Assert.Contains(GoalId, saved.LatchedCompleteGoalIds, "the latch is part of the persisted run state");

            var restored = new ScenarioObjectiveService();
            restored.LoadRunState(saved);
            restored.RegisterDatedQualifier(GoalId, DeadlineDay, () => false);
            Assert.IsFalse(restored.Evaluate(DeadlineDay + 30),
                "after a reload the latched goal still cannot fail");
            Assert.IsFalse(restored.IsFailed);
        }

        [Test]
        public void NeverTrue_FailsOncePastDeadline_ExactlyOnce()
        {
            int eventCount = 0;
            ScenarioRunStateEvents.ScenarioFailed += (_, _) => eventCount++;
            var service = new ScenarioObjectiveService();
            service.BeginScenario(ScenarioObjectiveService.FirstLedgerScenarioId);
            service.RegisterDatedQualifier(GoalId, DeadlineDay, () => false);

            Assert.IsFalse(service.Evaluate(DeadlineDay), "the deadline day itself still belongs to the qualifier");
            Assert.IsFalse(service.IsFailed);
            Assert.AreEqual(0, service.State.LatchedCompleteGoalIds.Count);

            Assert.IsTrue(service.Evaluate(DeadlineDay + 1), "unlatched and past deadline: the run fails");
            Assert.IsTrue(service.IsFailed);
            Assert.AreEqual(1, eventCount);
            Assert.AreEqual(1, service.State.FailedGoalIds.Count);

            Assert.IsFalse(service.Evaluate(DeadlineDay + 30), "failure is recorded exactly once");
            Assert.AreEqual(1, eventCount);
            Assert.AreEqual(1, service.State.FailedGoalIds.Count);
        }
    }
}
