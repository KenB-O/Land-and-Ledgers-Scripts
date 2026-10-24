using System;
using System.Collections.Generic;
using LandLedgers.Orchestration.Scenarios.Generation;
using LandLedgers.Orchestration.Scenarios.SaloonCircuit;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>
    /// Pass XI WS6 first-gate seam tests (Roslyn-harness feasible).
    ///
    /// The bootstrap MonoBehaviours themselves cannot compile in the
    /// Roslyn harness (SaloonCircuitBootstrap pulls SimulationSystemsHub
    /// / SharedBusinessRuntimeManager, which the harness subset excludes,
    /// and the shim lacks FindAnyObjectByType); their behaviour is
    /// covered by Pass11Ws6FirstGateBootstrapTests in Tests/Editor/
    /// Scenarios, which runs in the Unity Test Runner. These tests prove
    /// the pure data flow those bootstrap methods now drive, at the
    /// seams the harness can build:
    /// - M1: a Saloon load sequence with NO First Ledger bootstrap in
    ///   the loop — dto goals + hub latched list + evaluator restore +
    ///   qualifier rebuild — keeps the saved Saloon goals.
    /// - M2: the late-registration primitive (Ensure + Rebuild) that a
    ///   late-awakening bootstrap's Awake now calls: it restores live
    ///   qualifiers after a start it missed, and repeating it never
    ///   duplicates registrations or latched goals.
    /// - M4: RestoreHistoricalGoals never resurrects a lapsed
    ///   final-state goal into predicateHolding or the saved list,
    ///   never un-completes a historical goal, and still-holding finals
    ///   re-derive on the next Evaluate.
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws6FirstGateSeamTests
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

        private static List<ScenarioQualifierRegistration> SaloonDefinitions(ScenarioObjectiveService service)
        {
            var definitions = new List<ScenarioQualifierRegistration>();
            foreach (string goalId in SaloonCircuitScenario.GoalIdsInOrder)
            {
                int deadline = SaloonCircuitScenario.DeadlineDayByGoal.TryGetValue(goalId, out int day)
                    ? day : int.MaxValue;
                definitions.Add(new ScenarioQualifierRegistration
                {
                    GoalId = goalId,
                    DeadlineDayIndex = deadline,
                    PredicateKey = service.PredicateKeyFor(goalId),
                });
            }
            return definitions;
        }

        private static Dictionary<string, Func<bool>> LivePredicates(SaloonCircuitGoalEvaluator evaluator)
        {
            var predicates = new Dictionary<string, Func<bool>>();
            foreach (string goalId in SaloonCircuitScenario.GoalIdsInOrder)
            {
                string captured = goalId;
                predicates[captured] = () => evaluator.GoalHolds(captured);
            }
            return predicates;
        }

        [Test]
        public void SaloonLoad_WithoutFirstLedgerBootstrap_HistoricalGoalsSurvive_LapsedFinalDoesNotResurrect()
        {
            // The saved dto (as the Saloon bootstrap's self-sufficient
            // restore now reads it, with no First Ledger bootstrap
            // anywhere in the loop): one genuine historical milestone
            // plus a final-state goal that held at save time but has
            // since lapsed. The hub latched list still holds the lapsed
            // final (sticky by design, A-14 unchanged).
            var savedGoals = new List<string> { "ash-creek-ownership", "crown-rank-one" };
            var evaluator = new SaloonCircuitGoalEvaluator();
            var objectives = new ScenarioObjectiveService();
            objectives.BeginScenario(SaloonCircuitScenario.ScenarioId);
            objectives.MergeLatchedGoals(new List<string> { "crown-rank-one" });
            Assert.IsTrue(objectives.EnsureQualifierRegistrations(SaloonDefinitions(objectives)));

            // Bootstrap restore step 1 (M4): historical goals only.
            evaluator.RestoreHistoricalGoals(savedGoals);
            // Bootstrap reconcile step (M4): union of director dto,
            // evaluator list, and hub latched list, restored the same way.
            ScenarioGoalStoreReconciler.Reconciliation reconciliation =
                ScenarioGoalStoreReconciler.Reconcile(
                    savedGoals,
                    evaluator.CompletedGoalIds(),
                    objectives.State.LatchedCompleteGoalIds);
            evaluator.RestoreHistoricalGoals(reconciliation.CompletedGoals);

            Assert.IsTrue(evaluator.HistoricalCompleted.Contains("ash-creek-ownership"),
                "The saved Saloon milestone survives the load.");
            Assert.IsFalse(evaluator.PredicatesHolding.Contains("crown-rank-one"),
                "A lapsed final-state goal must not resurrect into predicateHolding from the file or the hub latched list.");
            Assert.IsFalse(evaluator.CompletedGoalIds().Contains("crown-rank-one"),
                "The saved list (CompletedGoalIds) must not permanently claim a goal the live state does not hold.");

            // Bootstrap restore step 2 (M1/M2): qualifier rebuild from
            // live evaluator state, as the self-sufficient restore and
            // a late-awakening bootstrap both do.
            List<string> unrebuilt = objectives.RebuildQualifiers(LivePredicates(evaluator));
            Assert.AreEqual(0, unrebuilt.Count, "Every Saloon qualifier rebuilds from live goal state.");
            objectives.Evaluate(0);
            Assert.IsTrue(objectives.State.LatchedCompleteGoalIds.Contains("ash-creek-ownership"),
                "The rebuilt qualifier latches the genuinely completed milestone.");
            // The hub latched list itself stays sticky by design (A-14,
            // deliberately unchanged): it still holds crown-rank-one
            // from the seed. What M4 fixed is the evaluator and the
            // saved list above — they no longer resurrect it.
        }

        [Test]
        public void LateRebuild_IsIdempotent_AndKeepsEvaluating()
        {
            // A bootstrap that missed ScenarioStarted registers late by
            // calling Ensure + Rebuild; a second bootstrap (or a later
            // ScenarioStarted) repeating it must not duplicate anything.
            var evaluator = new SaloonCircuitGoalEvaluator();
            evaluator.RestoreHistoricalGoals(new List<string> { "ash-creek-ownership" });
            var objectives = new ScenarioObjectiveService();
            objectives.BeginScenario(SaloonCircuitScenario.ScenarioId);

            Assert.IsTrue(objectives.EnsureQualifierRegistrations(SaloonDefinitions(objectives)),
                "First registration adopts the definitions.");
            Assert.AreEqual(0, objectives.RebuildQualifiers(LivePredicates(evaluator)).Count);

            // The late-awakening repeat (and a subsequent start-event repeat).
            Assert.IsFalse(objectives.EnsureQualifierRegistrations(SaloonDefinitions(objectives)),
                "Persisted registrations win over fresh definitions on repeat.");
            Assert.AreEqual(0, objectives.RebuildQualifiers(LivePredicates(evaluator)).Count);
            Assert.AreEqual(SaloonCircuitScenario.GoalIdsInOrder.Length,
                objectives.QualifierRegistrations.Count,
                "Repeating registration never duplicates a qualifier registration.");

            objectives.Evaluate(0);
            Assert.AreEqual(1, objectives.State.LatchedCompleteGoalIds.Count,
                "Exactly one goal latches: no duplicate latch entries.");
            Assert.IsTrue(objectives.State.LatchedCompleteGoalIds.Contains("ash-creek-ownership"));
        }

        [Test]
        public void LapsedFinal_EvaluateAfterRestore_StaysAbsent_WhileHistoricalPersists()
        {
            var evaluator = new SaloonCircuitGoalEvaluator();
            evaluator.RestoreHistoricalGoals(new List<string>
            {
                "ash-creek-ownership", "crown-rank-one", "unknown-goal-id",
            });
            Assert.IsTrue(evaluator.HistoricalCompleted.Contains("ash-creek-ownership"));
            Assert.IsFalse(evaluator.PredicatesHolding.Contains("crown-rank-one"),
                "RestoreHistoricalGoals never files a final-state id into predicateHolding.");
            Assert.IsFalse(evaluator.CompletedGoalIds().Contains("unknown-goal-id"),
                "Unknown ids are ignored.");

            // Live state where the historical predicate no longer holds
            // (sale record gone) and the final does not hold either.
            var state = new FakeState { Day = 50, DebtCents = 5000, RankOne = false };
            evaluator.Evaluate(state);
            Assert.IsTrue(evaluator.HistoricalCompleted.Contains("ash-creek-ownership"),
                "Historical milestones persist after legitimate execution even when the predicate no longer holds.");
            Assert.IsFalse(evaluator.PredicatesHolding.Contains("crown-rank-one"));
            Assert.IsFalse(evaluator.IsScenarioComplete);
        }

        [Test]
        public void StillHoldingFinal_ReDerivesOnNextEvaluate()
        {
            var evaluator = new SaloonCircuitGoalEvaluator();
            evaluator.RestoreHistoricalGoals(new List<string> { "ash-creek-ownership", "crown-rank-one" });
            Assert.IsFalse(evaluator.PredicatesHolding.Contains("crown-rank-one"),
                "Not holding yet: restore alone never claims a final-state goal.");

            var state = new FakeState { Day = 50, DebtCents = 0, RankOne = true };
            state.Ownership[SaloonCircuitScenario.AshCreekSaloonId] = 1f;
            state.Ownership[SaloonCircuitScenario.PrairieCrossingSaloonId] = 1f;
            state.Ownership[SaloonCircuitScenario.CrownHouseSaloonId] = 1f;
            evaluator.Evaluate(state);
            Assert.IsTrue(evaluator.PredicatesHolding.Contains("crown-rank-one"),
                "A final-state goal that genuinely holds live re-derives on the next tick.");
            Assert.IsTrue(evaluator.IsScenarioComplete);
        }
    }
}
