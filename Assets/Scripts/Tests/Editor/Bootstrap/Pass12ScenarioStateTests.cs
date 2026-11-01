using System;
using System.Collections.Generic;
using LandLedgers.Orchestration.Scenarios.Generation;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Bootstrap
{
    /// <summary>
    /// Pass XII Worker A (brief sections 4 and 9, task 3 and 6): the
    /// scenario-state load contract. A load must never reset a run:
    /// latched goals stay latched, failures stay failed, and qualifier
    /// registrations survive. An old save carrying no registrations at
    /// all still adopts fresh definitions and rebuilds without crashing.
    /// A fresh BeginScenario, by contrast, IS a reset — start semantics
    /// and load semantics must never be confused. A-14 late-latch
    /// semantics are deliberately untouched: a qualifier observed holding
    /// latches, and only an unlatched qualifier past its deadline fails.
    /// </summary>
    [TestFixture]
    public sealed class Pass12ScenarioStateTests
    {
        [TearDown]
        public void TearDown()
        {
            ScenarioRunStateEvents.ClearSubscribers();
        }

        private static ScenarioRunState BuildOldSaveWithoutRegistrations()
        {
            // A pre-Pass-XI save: a latched goal and a recorded failure,
            // but no qualifier registrations at all.
            return new ScenarioRunState
            {
                ScenarioId = "saloon-circuit",
                QualifierRegistrations = new List<ScenarioQualifierRegistration>(),
                LatchedCompleteGoalIds = new List<string> { "goal-already-met" },
                FailedScenarioIds = new List<string>(),
                FailedGoalIds = new List<string>(),
            };
        }

        [Test]
        public void OldSaveLoad_AdoptsDefinitions_Rebuilds_WithoutResettingLatchedGoals()
        {
            var service = new ScenarioObjectiveService();
            service.LoadRunState(BuildOldSaveWithoutRegistrations());

            var definitions = new List<ScenarioQualifierRegistration>
            {
                new ScenarioQualifierRegistration { GoalId = "goal-already-met", DeadlineDayIndex = 365, PredicateKey = "saloon-circuit:goal-already-met" },
                new ScenarioQualifierRegistration { GoalId = "goal-still-open", DeadlineDayIndex = 730, PredicateKey = "saloon-circuit:goal-still-open" },
            };
            Assert.IsTrue(service.EnsureQualifierRegistrations(definitions),
                "an old save with no registrations adopts fresh definitions");

            List<string> unrebuilt = service.RebuildQualifiers(new Dictionary<string, Func<bool>>
            {
                { "goal-already-met", () => false },
                { "goal-still-open", () => false },
            });
            Assert.AreEqual(0, unrebuilt.Count);

            Assert.IsFalse(service.Evaluate(700),
                "the latched goal must not fail even though its live predicate is now false and its deadline passed, " +
                "and the open goal must not fail before its own deadline");
            Assert.Contains("goal-already-met", service.State.LatchedCompleteGoalIds);
            Assert.IsFalse(service.IsFailed, "no failure may be invented for a latched goal");
            Assert.AreEqual(2, service.QualifierRegistrations.Count, "adopted registrations survive evaluation");

            Assert.IsTrue(service.Evaluate(800),
                "past the open goal's deadline the run does fail — on the open goal, not the latched one");
            Assert.Contains("goal-still-open", service.State.FailedGoalIds);
            Assert.IsFalse(service.State.FailedGoalIds.Contains("goal-already-met"),
                "failure attribution must never blame a latched goal");
        }

        [Test]
        public void FailedRunSurvivesLoad_AndFailureEventFiresExactlyOnce()
        {
            int failureEvents = 0;
            ScenarioRunStateEvents.ScenarioFailed += (scenarioId, goalId) => failureEvents++;

            var service = new ScenarioObjectiveService();
            service.BeginScenario("saloon-circuit");
            service.RegisterDatedQualifier("goal-never", 100, () => false);
            Assert.IsTrue(service.Evaluate(101), "the run fails past the deadline");
            Assert.AreEqual(1, failureEvents);

            ScenarioRunState saved = service.CaptureRunState();
            var restored = new ScenarioObjectiveService();
            restored.LoadRunState(saved);
            Assert.IsTrue(restored.IsFailed, "failure is sticky across load");
            Assert.Contains("goal-never", restored.State.FailedGoalIds);

            List<string> unrebuilt = restored.RebuildQualifiers(new Dictionary<string, Func<bool>>
            {
                { "goal-never", () => true },
            });
            Assert.AreEqual(0, unrebuilt.Count);
            Assert.IsFalse(restored.Evaluate(500),
                "a failed run never re-fails and never un-fails, even if the predicate later holds");
            Assert.AreEqual(1, failureEvents, "the failure event fires exactly once per failed scenario, not once per load");
            Assert.IsTrue(restored.IsFailed);
        }

        [Test]
        public void DoubleRestore_IsIdempotent_ForRunStateAndEvaluation()
        {
            var service = new ScenarioObjectiveService();
            service.BeginScenario("saloon-circuit");
            service.RegisterDatedQualifier("goal-met", 365, () => true);
            service.RegisterDatedQualifier("goal-open", 730, () => false);
            service.Evaluate(10);
            ScenarioRunState saved = service.CaptureRunState();

            var restored = new ScenarioObjectiveService();
            restored.LoadRunState(saved);
            ScenarioRunState afterFirst = restored.CaptureRunState();
            restored.LoadRunState(saved);
            ScenarioRunState afterSecond = restored.CaptureRunState();

            Assert.AreEqual(afterFirst.ScenarioId, afterSecond.ScenarioId);
            Assert.AreEqual(afterFirst.LatchedCompleteGoalIds.Count, afterSecond.LatchedCompleteGoalIds.Count);
            Assert.AreEqual(afterFirst.FailedGoalIds.Count, afterSecond.FailedGoalIds.Count);
            Assert.AreEqual(afterFirst.QualifierRegistrations.Count, afterSecond.QualifierRegistrations.Count);
            for (int i = 0; i < afterFirst.QualifierRegistrations.Count; i++)
            {
                Assert.AreEqual(afterFirst.QualifierRegistrations[i].GoalId, afterSecond.QualifierRegistrations[i].GoalId);
                Assert.AreEqual(afterFirst.QualifierRegistrations[i].DeadlineDayIndex, afterSecond.QualifierRegistrations[i].DeadlineDayIndex);
                Assert.AreEqual(afterFirst.QualifierRegistrations[i].PredicateKey, afterSecond.QualifierRegistrations[i].PredicateKey);
            }

            restored.RebuildQualifiers(new Dictionary<string, Func<bool>>
            {
                { "goal-met", () => true },
                { "goal-open", () => false },
            });
            Assert.IsFalse(restored.Evaluate(400), "double restore changes nothing about evaluation");
            Assert.Contains("goal-met", restored.State.LatchedCompleteGoalIds);
        }

        [Test]
        public void LoadRunState_NullAndEmptyState_NeverCrashes_EvaluateIsNoop()
        {
            var service = new ScenarioObjectiveService();
            service.LoadRunState(null);
            Assert.AreEqual(string.Empty, service.ScenarioId);
            Assert.IsFalse(service.Evaluate(9999));
            Assert.IsFalse(service.IsFailed);

            service.LoadRunState(new ScenarioRunState());
            Assert.IsFalse(service.Evaluate(9999));
            Assert.AreEqual(0, service.RebuildQualifiers(null).Count);
        }

        [Test]
        public void BeginScenario_AfterCompletedRun_ResetsToFreshRun()
        {
            var service = new ScenarioObjectiveService();
            service.BeginScenario("saloon-circuit");
            service.RegisterDatedQualifier("goal-met", 100, () => true);
            service.Evaluate(10);
            Assert.Contains("goal-met", service.State.LatchedCompleteGoalIds);

            service.BeginScenario("the-first-ledger");
            Assert.AreEqual("the-first-ledger", service.ScenarioId);
            Assert.AreEqual(0, service.State.LatchedCompleteGoalIds.Count,
                "a genuine new start resets the run; only a load preserves it");
            Assert.AreEqual(0, service.State.FailedGoalIds.Count);
            Assert.AreEqual(0, service.QualifierRegistrations.Count);
            Assert.IsFalse(service.IsFailed);
        }

        [Test]
        public void CaptureRunState_IsADeepCopy_MutatingItDoesNotCorruptLiveState()
        {
            var service = new ScenarioObjectiveService();
            service.BeginScenario("saloon-circuit");
            service.RegisterDatedQualifier("goal-a", 365, () => false);
            ScenarioRunState snapshot = service.CaptureRunState();

            snapshot.QualifierRegistrations[0].DeadlineDayIndex = 1;
            snapshot.LatchedCompleteGoalIds.Add("goal-forged");
            snapshot.FailedGoalIds.Add("goal-forged");

            Assert.AreEqual(365, service.QualifierRegistrations[0].DeadlineDayIndex);
            Assert.AreEqual(0, service.State.LatchedCompleteGoalIds.Count);
            Assert.AreEqual(0, service.State.FailedGoalIds.Count);
        }
    }
}
