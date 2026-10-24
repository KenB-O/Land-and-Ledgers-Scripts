using System;
using System.Collections.Generic;
using LandLedgers.Orchestration.Scenarios.Generation;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>
    /// Pass XI WS1: the scenario start contract at the service level.
    /// A scenario start begins objective tracking (BeginScenario), a
    /// drivers daily hook drives Evaluate with the day index, dated
    /// qualifiers persist across save and load and rebuild from live
    /// predicates, and the three goal stores reconcile to one union
    /// without ever un-completing a goal. The MonoBehaviour wiring
    /// (director hook, bootstrap registration, drivers day hook) sits
    /// outside the harness subset and is verified by the variant build
    /// plus Unity scene checks, never claimed from these tests.
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws1ScenarioStartTests
    {
        [TearDown]
        public void TearDown()
        {
            ScenarioRunStateEvents.ClearSubscribers();
        }

        [Test]
        public void StartContract_DailyEvaluate_LatchesMetGoal_AndFailsUnmetPastDeadline()
        {
            var service = new ScenarioObjectiveService();
            service.BeginScenario(ScenarioObjectiveService.FirstLedgerScenarioId);
            bool met = false;
            service.RegisterDatedQualifier("goal-met", 100, () => met);
            service.RegisterDatedQualifier("goal-never", 100, () => false);

            // Daily driving before either deadline: nothing latches, nothing fails.
            for (int day = 0; day <= 100; day++)
            {
                Assert.IsFalse(service.Evaluate(day), "no failure on or before the deadline");
            }
            Assert.IsFalse(service.IsFailed);

            // Day 101: one daily pass latches the met goal and fails the
            // run on the unmet goal, in qualifier order.
            met = true;
            Assert.IsTrue(service.Evaluate(101), "the unmet goal fails past its deadline");
            Assert.Contains("goal-met", service.State.LatchedCompleteGoalIds);
            Assert.IsTrue(service.IsFailed);
            Assert.Contains("goal-never", service.State.FailedGoalIds);
            Assert.IsFalse(service.Evaluate(200), "failure is recorded exactly once");
        }

        [Test]
        public void QualifierRegistrations_PersistAcrossSaveLoad_AndRebuildRestoresEvaluation()
        {
            var service = new ScenarioObjectiveService();
            service.BeginScenario("saloon-circuit");
            service.RegisterDatedQualifier("ash-creek-ownership", 365, () => true);
            service.RegisterDatedQualifier("prairie-crossing-ownership", 730, () => false);

            ScenarioRunState saved = service.CaptureRunState();
            Assert.AreEqual(2, saved.QualifierRegistrations.Count, "registrations persist, predicates do not");
            Assert.AreEqual("saloon-circuit:ash-creek-ownership", saved.QualifierRegistrations[0].PredicateKey);

            var restored = new ScenarioObjectiveService();
            restored.LoadRunState(saved);
            // Before rebuild the live qualifier list is empty, so Evaluate
            // is a no-op even past a deadline. Rebuild closes that gap.
            Assert.IsFalse(restored.Evaluate(800), "no live qualifiers yet, no silent failure either");
            Assert.IsFalse(restored.IsFailed);

            List<string> unrebuilt = restored.RebuildQualifiers(new Dictionary<string, Func<bool>>
            {
                { "ash-creek-ownership", () => true },
                { "prairie-crossing-ownership", () => false },
            });
            Assert.AreEqual(0, unrebuilt.Count);
            Assert.IsFalse(restored.Evaluate(366), "rebuilt met goal latches");
            Assert.Contains("ash-creek-ownership", restored.State.LatchedCompleteGoalIds);
            Assert.IsTrue(restored.Evaluate(731), "rebuilt unmet goal fails past its deadline");
        }

        [Test]
        public void RebuildQualifiers_ReportsUnrebuiltGoals_InsteadOfSilentlyIgnoringThem()
        {
            var service = new ScenarioObjectiveService();
            service.BeginScenario("saloon-circuit");
            service.RegisterDatedQualifier("ash-creek-ownership", 365, () => false);
            service.RegisterDatedQualifier("prairie-crossing-ownership", 730, () => false);

            var restored = new ScenarioObjectiveService();
            restored.LoadRunState(service.CaptureRunState());
            List<string> unrebuilt = restored.RebuildQualifiers(new Dictionary<string, Func<bool>>
            {
                { "ash-creek-ownership", () => false },
            });
            Assert.AreEqual(1, unrebuilt.Count);
            Assert.Contains("prairie-crossing-ownership", unrebuilt);
            Assert.IsFalse(restored.Evaluate(365),
                "the rebuilt goal holds to its deadline, and the unrebuilt goal fails nothing on its own");
            Assert.IsFalse(restored.IsFailed);
            Assert.AreEqual(0, restored.State.FailedGoalIds.Count,
                "no failure may be recorded for a goal with no live predicate");
        }

        [Test]
        public void EnsureQualifierRegistrations_OldSave_AdoptsDefinitions_AndPersistedWins()
        {
            // An old save carries no registrations at all.
            var oldState = new ScenarioRunState { ScenarioId = "saloon-circuit" };
            oldState.QualifierRegistrations = new List<ScenarioQualifierRegistration>();
            var service = new ScenarioObjectiveService();
            service.LoadRunState(oldState);

            var definitions = new List<ScenarioQualifierRegistration>
            {
                new ScenarioQualifierRegistration { GoalId = "ash-creek-ownership", DeadlineDayIndex = 365, PredicateKey = "saloon-circuit:ash-creek-ownership" },
            };
            Assert.IsTrue(service.EnsureQualifierRegistrations(definitions), "old save adopts fresh definitions");
            Assert.AreEqual(1, service.QualifierRegistrations.Count);

            var movedDefinitions = new List<ScenarioQualifierRegistration>
            {
                new ScenarioQualifierRegistration { GoalId = "ash-creek-ownership", DeadlineDayIndex = 999, PredicateKey = "saloon-circuit:ash-creek-ownership" },
            };
            Assert.IsFalse(service.EnsureQualifierRegistrations(movedDefinitions),
                "persisted registrations win over later definitions");
            Assert.AreEqual(365, service.QualifierRegistrations[0].DeadlineDayIndex);
        }

        [Test]
        public void HeadlineDeadline_FallsBackToPersistedRegistration_AfterLoad()
        {
            var service = new ScenarioObjectiveService();
            service.BeginScenario("saloon-circuit");
            service.RegisterDatedQualifier("ash-creek-ownership", 365, () => false);
            service.RegisterDatedQualifier("crown-rank-one", int.MaxValue, () => false);

            var restored = new ScenarioObjectiveService();
            restored.LoadRunState(service.CaptureRunState());
            ScenarioHeadlineSnapshot snapshot = restored.BuildHeadlineSnapshot(
                "Saloon Circuit", 0, 9, "ash-creek-ownership");
            Assert.AreEqual(365, snapshot.DeadlineDayIndex,
                "deadline must come from the persisted registration, not -1");

            ScenarioHeadlineSnapshot noDeadline = restored.BuildHeadlineSnapshot(
                "Saloon Circuit", 0, 9, "crown-rank-one");
            Assert.AreEqual(-1, noDeadline.DeadlineDayIndex, "a goal with no deadline still reports -1");
        }

        [Test]
        public void RegisterDatedQualifier_ReRegistration_UpdatesPersistedDeadline_WithoutDuplicating()
        {
            var service = new ScenarioObjectiveService();
            service.BeginScenario("saloon-circuit");
            service.RegisterDatedQualifier("ash-creek-ownership", 365, () => false);
            service.RegisterDatedQualifier("ash-creek-ownership", 400, () => false);
            Assert.AreEqual(1, service.QualifierRegistrations.Count);
            Assert.AreEqual(400, service.QualifierRegistrations[0].DeadlineDayIndex);
        }

        [Test]
        public void MergeLatchedGoals_NeverRemoves_AndNeverUnfails()
        {
            var service = new ScenarioObjectiveService();
            service.BeginScenario("saloon-circuit");
            service.RegisterDatedQualifier("goal-never", 10, () => false);
            Assert.IsTrue(service.Evaluate(11), "run fails first");
            Assert.IsTrue(service.IsFailed);

            Assert.AreEqual(1, service.MergeLatchedGoals(new[] { "goal-from-director" }));
            Assert.AreEqual(0, service.MergeLatchedGoals(new[] { "goal-from-director" }), "merge dedupes");
            Assert.Contains("goal-from-director", service.State.LatchedCompleteGoalIds);
            Assert.IsTrue(service.IsFailed, "merging latched goals never un-fails the run");
            Assert.Contains("goal-never", service.State.FailedGoalIds, "failed set untouched by merge");
        }

        [Test]
        public void Reconcile_UnionsAllThreeStores_AndReportsEveryDisagreement()
        {
            ScenarioGoalStoreReconciler.Reconciliation result =
                ScenarioGoalStoreReconciler.Reconcile(
                    new[] { "goal-a" },
                    new[] { "goal-a", "goal-b" },
                    new[] { "goal-b", "goal-c" });
            Assert.AreEqual(new[] { "goal-a", "goal-b", "goal-c" }, result.CompletedGoals.ToArray(),
                "union in director, evaluator, hub order");
            Assert.IsTrue(result.Diagnostics.Count >= 3, "each store that lacked a goal is named");
            foreach (string diagnostic in result.Diagnostics)
            {
                Assert.IsTrue(diagnostic.Contains("goal-"), diagnostic);
            }
        }

        [Test]
        public void Reconcile_NeverUncompletes_GoalCompletedInOnlyOneStore()
        {
            ScenarioGoalStoreReconciler.Reconciliation result =
                ScenarioGoalStoreReconciler.Reconcile(
                    new string[0], new string[0], new[] { "goal-only-in-hub" });
            Assert.Contains("goal-only-in-hub", result.CompletedGoals);
            ScenarioGoalStoreReconciler.Reconciliation empty =
                ScenarioGoalStoreReconciler.Reconcile(null, null, null);
            Assert.AreEqual(0, empty.CompletedGoals.Count);
            Assert.AreEqual(0, empty.Diagnostics.Count);
        }
    }
}
