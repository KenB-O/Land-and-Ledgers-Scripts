using System.Collections.Generic;
using System.Linq;
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

        // DEF-013 (Decision 9): the equity thresholds are a scenario target
        // from the asset's equityTarget tunables, not a universal rule.

        [Test]
        public void ResolveEquityThresholds_AllTunablesWin()
        {
            int[] resolved = FirstLedgerScenario.ResolveEquityThresholds(
                new TunableValue("equityTarget1Cents", 400000),
                new TunableValue("equityTarget2Cents", 900000),
                new TunableValue("equityTarget3Cents", 1200000));

            Assert.AreEqual(400000, resolved[0]);
            Assert.AreEqual(900000, resolved[1]);
            Assert.AreEqual(1200000, resolved[2]);
        }

        [Test]
        public void ResolveEquityThresholds_MissingTunablesFallBack()
        {
            int[] resolved = FirstLedgerScenario.ResolveEquityThresholds(
                null,
                new TunableValue("equityTarget2Cents", 900000),
                null);

            Assert.AreEqual(500000, resolved[0]);
            Assert.AreEqual(900000, resolved[1]);
            Assert.AreEqual(1500000, resolved[2]);
        }

        [Test]
        public void ResolveEquityThresholds_NonPositiveOrWrongKindFallsBack()
        {
            int[] resolved = FirstLedgerScenario.ResolveEquityThresholds(
                new TunableValue("equityTarget1Cents", 0),
                new TunableValue("equityTarget2Cents", -5),
                new TunableValue("equityTarget3Cents", 1.5f));

            Assert.AreEqual(500000, resolved[0]);
            Assert.AreEqual(1000000, resolved[1]);
            Assert.AreEqual(1500000, resolved[2]);
        }

        [Test]
        public void Evaluate_CustomThresholds_FireAtTheCustomValues()
        {
            var evaluator = new FirstLedgerGoalEvaluator(400000, 900000, 1200000);
            var state = new FakeState
            {
                PlayerOwnedBusinessCount = 1,
                ActivePlayerEmployeeCount = 1,
                PlayerOwnerEquityCents = 399999
            };

            evaluator.Evaluate(state);
            Assert.AreEqual("equity-5000", evaluator.Progress().nextGoalId);

            state.PlayerOwnerEquityCents = 400000;
            List<string> newly = evaluator.Evaluate(state);
            Assert.Contains("equity-5000", newly);
        }

        [Test]
        public void ConfigureEquityThresholds_NoOpAfterAnEquityGoalCompletes()
        {
            var evaluator = new FirstLedgerGoalEvaluator();
            var state = new FakeState
            {
                PlayerOwnedBusinessCount = 1,
                ActivePlayerEmployeeCount = 1,
                PlayerOwnerEquityCents = 500000
            };

            List<string> newly = evaluator.Evaluate(state);
            Assert.Contains("equity-5000", newly);

            // Thresholds must not move mid-game, even when reconfigured.
            evaluator.ConfigureEquityThresholds(9000000, 9000000, 9000000);
            Assert.AreEqual(3, evaluator.Progress().completed);
            Assert.IsTrue(evaluator.CompletedGoalIds.Contains("equity-5000"));
            Assert.AreEqual(0, evaluator.Evaluate(state).Count);
        }
    }
}
