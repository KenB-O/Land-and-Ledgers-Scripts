using System.Linq;
using System.Collections.Generic;
using LandLedgers.Orchestration.Scenarios.SaloonCircuit;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Scenarios
{
    /// <summary>
    /// T3B: Saloon Circuit goal evaluation. Canon Part XII locks the completion
    /// structure; Part XV locks objective semantics (historical latches,
    /// final-state predicates re-evaluate).
    /// </summary>
    [TestFixture]
    public sealed class SaloonCircuitGoalEvaluatorTests
    {
        private sealed class FakeState : ISaloonCircuitGameState
        {
            public readonly Dictionary<string, float> Ownership = new Dictionary<string, float>();
            public readonly HashSet<string> QualifyingSales = new HashSet<string>();
            public bool IntegratedSale;
            public readonly Dictionary<string, int> InternalProducts = new Dictionary<string, int>();
            public bool RankOne;
            public int DebtCents;
            public int Day;

            public float SaloonOwnershipShare(string saloonId)
                => Ownership.TryGetValue(saloonId, out float s) ? s : 0f;
            public bool HasQualifyingCustomerSale(string saloonId)
                => QualifyingSales.Contains(saloonId);
            public bool HasIntegratedSale() => IntegratedSale;
            public int DistinctInternallySuppliedProductsSold(string saloonId)
                => InternalProducts.TryGetValue(saloonId, out int n) ? n : 0;
            public bool IsCrownHouseRankOne => RankOne;
            public int ScenarioPrincipalDebtCents => DebtCents;
            public int CurrentDayIndex => Day;
        }

        [Test]
        public void HistoricalMilestone_LatchesAndPersists()
        {
            var evaluator = new SaloonCircuitGoalEvaluator();
            var state = new FakeState { Day = 100 };
            state.Ownership[SaloonCircuitScenario.AshCreekSaloonId] = 1f;
            state.QualifyingSales.Add(SaloonCircuitScenario.AshCreekSaloonId);

            var first = evaluator.Evaluate(state);
            Assert.Contains("ash-creek-ownership", first);

            // Sale record gone (e.g. archival) — the milestone persists.
            state.QualifyingSales.Clear();
            var second = evaluator.Evaluate(state);
            Assert.IsFalse(second.Contains("ash-creek-ownership"), "Historical milestones never re-fire.");
            Assert.IsTrue(evaluator.HistoricalCompleted.Contains("ash-creek-ownership"),
                "Historical milestones persist after legitimate execution (Canon Part XV).");
        }

        [Test]
        public void HistoricalMilestone_MissedDeadline_NeverCompletes()
        {
            var evaluator = new SaloonCircuitGoalEvaluator();
            var state = new FakeState { Day = 400 }; // past the 365 deadline
            state.Ownership[SaloonCircuitScenario.AshCreekSaloonId] = 1f;
            state.QualifyingSales.Add(SaloonCircuitScenario.AshCreekSaloonId);

            evaluator.Evaluate(state);
            Assert.IsFalse(evaluator.HistoricalCompleted.Contains("ash-creek-ownership"),
                "A sale after the authored deadline does not complete the milestone.");
        }

        [Test]
        public void FinalStatePredicate_ReevaluatesCurrentState()
        {
            var evaluator = new SaloonCircuitGoalEvaluator();
            var state = new FakeState { Day = 50, DebtCents = 0 };
            evaluator.Evaluate(state);
            Assert.IsTrue(evaluator.PredicatesHolding.Contains("debt-zero"));

            state.DebtCents = 5000; // new borrowing
            evaluator.Evaluate(state);
            Assert.IsFalse(evaluator.PredicatesHolding.Contains("debt-zero"),
                "Final-state predicates re-evaluate current state (Canon Part XV).");
            Assert.IsFalse(evaluator.IsScenarioComplete);
        }

        [Test]
        public void IntegratedSale_RequiresRealProvenanceSignal()
        {
            var evaluator = new SaloonCircuitGoalEvaluator();
            var state = new FakeState { Day = 50 };
            evaluator.Evaluate(state);
            Assert.IsFalse(evaluator.HistoricalCompleted.Contains("integrated-sale"));

            state.IntegratedSale = true; // the provenance query confirms a real chain
            var done = evaluator.Evaluate(state);
            Assert.Contains("integrated-sale", done);
        }

        [Test]
        public void CrownProducts_CountDistinct()
        {
            var evaluator = new SaloonCircuitGoalEvaluator();
            var state = new FakeState { Day = 50 };
            state.InternalProducts[SaloonCircuitScenario.CrownHouseSaloonId] = 3;
            var done = evaluator.Evaluate(state);
            Assert.Contains("crown-three-products", done);
            Assert.IsFalse(done.Contains("crown-five-products"));

            state.InternalProducts[SaloonCircuitScenario.CrownHouseSaloonId] = 5;
            done = evaluator.Evaluate(state);
            Assert.Contains("crown-five-products", done);
        }

        [Test]
        public void ScenarioComplete_RequiresAllThreePredicates()
        {
            var evaluator = new SaloonCircuitGoalEvaluator();
            var state = new FakeState { Day = 50, DebtCents = 0, RankOne = true };
            state.Ownership[SaloonCircuitScenario.AshCreekSaloonId] = 1f;
            state.Ownership[SaloonCircuitScenario.PrairieCrossingSaloonId] = 1f;
            state.Ownership[SaloonCircuitScenario.CrownHouseSaloonId] = 1f;
            evaluator.Evaluate(state);
            Assert.IsTrue(evaluator.IsScenarioComplete,
                "Rank #1 + all three owned + zero debt = scenario complete.");
        }

        [Test]
        public void Progress_ReportsNextGoal()
        {
            var evaluator = new SaloonCircuitGoalEvaluator();
            var state = new FakeState { Day = 50 };
            var (completed, total, next) = evaluator.Progress();
            Assert.AreEqual(0, completed);
            Assert.AreEqual(9, total);
            Assert.AreEqual("ash-creek-ownership", next);
        }
    }
}
