using System.Collections.Generic;
using LandLedgers.Orchestration.Scenarios;
using LandLedgers.Orchestration.Scenarios.FirstLedger;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>
    /// BIZ-6: the First Ledger goal ladder — live evaluation, ladder order,
    /// equity goals on owner equity (Canon §11.4).
    /// </summary>
    [TestFixture]
    public sealed class FirstLedgerGoalEvaluatorTests
    {
        private sealed class FakeState : IFirstLedgerGameState
        {
            public int PlayerOwnedBusinessCount { get; set; }
            public int ActivePlayerEmployeeCount { get; set; }
            public int PlayerOwnerEquityCents { get; set; }
        }

        [Test]
        public void Evaluate_CompletesGoalsInLadderOrder()
        {
            var evaluator = new FirstLedgerGoalEvaluator();
            var completed = new List<string>();
            evaluator.GoalCompleted += completed.Add;
            var state = new FakeState
            {
                PlayerOwnedBusinessCount = 3,
                ActivePlayerEmployeeCount = 3,
                PlayerOwnerEquityCents = 1500000,
            };

            List<string> newly = evaluator.Evaluate(state);

            Assert.AreEqual(8, newly.Count);
            Assert.AreEqual(FirstLedgerScenario.GoalIdsInOrder, newly);
            Assert.AreEqual(FirstLedgerScenario.GoalIdsInOrder, completed);
            Assert.IsTrue(evaluator.IsScenarioComplete);
        }

        [Test]
        public void Evaluate_EachGoalFiresOnce()
        {
            var evaluator = new FirstLedgerGoalEvaluator();
            var state = new FakeState { PlayerOwnedBusinessCount = 1 };

            Assert.AreEqual(1, evaluator.Evaluate(state).Count);
            Assert.AreEqual(0, evaluator.Evaluate(state).Count); // already done
        }

        [Test]
        public void Evaluate_EquityGoals_UseOwnerEquityThresholds()
        {
            var evaluator = new FirstLedgerGoalEvaluator();
            var state = new FakeState
            {
                PlayerOwnedBusinessCount = 1,
                ActivePlayerEmployeeCount = 1,
                PlayerOwnerEquityCents = 499999
            };

            evaluator.Evaluate(state);
            var progress = evaluator.Progress();
            Assert.AreEqual("equity-5000", progress.nextGoalId);

            state.PlayerOwnerEquityCents = 500000;
            List<string> newly = evaluator.Evaluate(state);
            Assert.Contains("equity-5000", newly);
        }

        [Test]
        public void Progress_ReportsNextGoal()
        {
            var evaluator = new FirstLedgerGoalEvaluator();
            var state = new FakeState
            {
                PlayerOwnedBusinessCount = 1,
                ActivePlayerEmployeeCount = 1,
            };

            evaluator.Evaluate(state);
            var (completed, total, nextGoalId) = evaluator.Progress();

            Assert.AreEqual(2, completed);
            Assert.AreEqual(8, total);
            Assert.AreEqual("equity-5000", nextGoalId);
        }

        [Test]
        public void ScenarioDefinition_HasEightGoalsAndFiveObjectives()
        {
            Assert.AreEqual(8, FirstLedgerScenario.BuildGoals().Count);
            Assert.AreEqual(5, FirstLedgerScenario.BuildObjectives().Count);
            Assert.AreEqual(8, FirstLedgerScenario.GoalIdsInOrder.Length);

            // Every objective links to a real goal.
            var goalIds = new HashSet<string>(FirstLedgerScenario.GoalIdsInOrder);
            foreach (ScenarioObjective objective in FirstLedgerScenario.BuildObjectives())
            {
                Assert.IsTrue(goalIds.Contains(objective.LinkedGoalId),
                    $"Objective '{objective.ObjectiveId}' links to unknown goal.");
            }
        }

        [Test]
        public void ScenarioDefinition_HasOpeningNarration()
        {
            // Canon Part I §1.1: every authored scenario opens with narration.
            Assert.IsFalse(string.IsNullOrWhiteSpace(FirstLedgerScenario.OpeningNarration));
        }
    }
}
