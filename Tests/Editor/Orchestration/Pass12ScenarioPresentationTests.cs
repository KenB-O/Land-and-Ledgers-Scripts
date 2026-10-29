using System.Collections.Generic;
using LandLedgers.Orchestration.Scenarios.Generation;
using LandLedgers.Reporting;
using NUnit.Framework;

namespace LandLedgers.Editor.Orchestration
{
    /// <summary>
    /// Pass XII Worker D: scenario HUD read model + guidance gate.
    /// Reproduction-first: the pre-pass defects were (1) no HUD model
    /// at all binding identity/objectives/deadlines/failure/Sandbox
    /// from authoritative state, and (2) first-session guidance that
    /// ignored the active scenario. A-14 semantics are NOT changed
    /// here: expired objectives stay failed, latched goals stay
    /// complete — both pinned, not relaxed.
    /// </summary>
    [TestFixture]
    public sealed class Pass12ScenarioPresentationTests
    {
        [Test]
        public void HudModel_ActiveScenarioShowsIdentityProgressAndDeadlines()
        {
            ScenarioHudReadModel model = ScenarioHudReadModel.Build(
                "saloon-circuit", "Saloon Circuit", 10,
                new List<ScenarioHudReadModel.GoalInput>
                {
                    new ScenarioHudReadModel.GoalInput { GoalId = "g-done", Text = "Done goal", DeadlineDayIndex = -1 },
                    new ScenarioHudReadModel.GoalInput { GoalId = "g-open", Text = "Open goal", DeadlineDayIndex = 12 },
                },
                new List<string> { "g-done" },
                false, string.Empty, -1, null);
            Assert.IsTrue(model.HasActiveScenario);
            Assert.IsFalse(model.IsSandbox);
            Assert.AreEqual("Saloon Circuit", model.Title);
            Assert.AreEqual("In progress", model.StatusText);
            Assert.AreEqual("1 of 2 objectives complete", model.CompletionText);
            Assert.AreEqual(string.Empty, model.FailureBanner);
            Assert.AreEqual(string.Empty, model.EmptyStateText);
            Assert.AreEqual("Due in 2 days", model.Goals[1].DeadlineText);
            Assert.IsTrue(model.Goals[0].IsLatched);
            Assert.IsFalse(model.Goals[1].IsLatched);
        }

        [Test]
        public void HudModel_DeadlineDayIsDueTodayNeverOverdue()
        {
            Assert.AreEqual("Due today", ScenarioHudReadModel.FormatDeadline(10, 10));
            Assert.AreEqual("Overdue by 1 day", ScenarioHudReadModel.FormatDeadline(10, 11));
            Assert.AreEqual("No deadline", ScenarioHudReadModel.FormatDeadline(int.MaxValue, 11));
        }

        [Test]
        public void HudModel_SandboxShowsNoObjectivesAndNoFailure()
        {
            ScenarioHudReadModel model = ScenarioHudReadModel.Build(
                string.Empty, string.Empty, 30, null, null, false, string.Empty, -1,
                new List<ScenarioHudReadModel.NoticeInput>
                {
                    // A stale board from a previous run must never paint
                    // failure onto a clean Sandbox session.
                    new ScenarioHudReadModel.NoticeInput { ScenarioId = "saloon-circuit", GoalId = "g-old", DeadlineDayIndex = 5 },
                });
            Assert.IsTrue(model.IsSandbox);
            Assert.IsFalse(model.HasActiveScenario);
            Assert.AreEqual(0, model.Goals.Count);
            Assert.AreEqual(string.Empty, model.FailureBanner);
            Assert.AreEqual(0, model.FailureNotices.Count);
            Assert.AreEqual("No scenario active", model.StatusText);
            Assert.IsTrue(model.EmptyStateText.Contains("Sandbox"), model.EmptyStateText);
        }

        [Test]
        public void HudAdapter_ExpiredObjectiveStaysFailedAndLatchedStaysComplete()
        {
            var service = new ScenarioObjectiveService();
            service.BeginScenario("saloon-circuit");
            service.RegisterDatedQualifier("goal-done", 50, () => true);
            service.RegisterDatedQualifier("goal-late", 10, () => false);
            Assert.IsFalse(service.Evaluate(5));
            Assert.IsTrue(service.State.LatchedCompleteGoalIds.Contains("goal-done"));
            Assert.IsTrue(service.Evaluate(11));
            Assert.IsTrue(service.IsFailed);
            // A later evaluation must not un-fail the run (A-14 sticky failure).
            Assert.IsFalse(service.Evaluate(20));
            Assert.IsTrue(service.IsFailed);

            var board = new ScenarioFailureNoticeBoard();
            Assert.IsTrue(board.Record("saloon-circuit", "goal-late", 10));
            ScenarioHudReadModel model = ScenarioHudSnapshotBuilder.Build(
                service, "Saloon Circuit",
                new List<ScenarioObjectivePresentation.GoalRow>
                {
                    new ScenarioObjectivePresentation.GoalRow { GoalId = "goal-done", Text = "Done goal", DeadlineDayIndex = 50 },
                    new ScenarioObjectivePresentation.GoalRow { GoalId = "goal-late", Text = "Timed goal", DeadlineDayIndex = 10 },
                },
                20, board);
            Assert.AreEqual("Failed", model.StatusText);
            Assert.IsTrue(model.FailureBanner.Contains("goal-late"), model.FailureBanner);
            Assert.IsTrue(model.Goals[0].IsLatched);
            Assert.IsFalse(model.Goals[0].IsFailed);
            Assert.IsTrue(model.Goals[1].IsFailed);
            Assert.IsTrue(model.Goals[1].IsOverdue);
            Assert.AreEqual(1, model.FailureNotices.Count);
            Assert.IsTrue(model.FailureNotices[0].DisplayText.Contains("goal-late"), model.FailureNotices[0].DisplayText);
        }

        [Test]
        public void HudAdapter_LatchedGoalSurvivesItsDeadline()
        {
            var service = new ScenarioObjectiveService();
            service.BeginScenario("saloon-circuit");
            service.RegisterDatedQualifier("goal-early", 10, () => true);
            Assert.IsFalse(service.Evaluate(5));
            Assert.IsFalse(service.Evaluate(11));
            Assert.IsFalse(service.IsFailed);
            ScenarioHudReadModel model = ScenarioHudSnapshotBuilder.Build(
                service, "Saloon Circuit",
                new List<ScenarioObjectivePresentation.GoalRow>
                {
                    new ScenarioObjectivePresentation.GoalRow { GoalId = "goal-early", Text = "Early goal", DeadlineDayIndex = 10 },
                },
                11, null);
            Assert.IsTrue(model.Goals[0].IsLatched);
            Assert.IsFalse(model.Goals[0].IsOverdue);
            Assert.IsFalse(model.Goals[0].IsFailed);
            Assert.AreEqual("Complete", model.StatusText);
        }

        [Test]
        public void HudAdapter_NoServiceIsSandbox()
        {
            ScenarioHudReadModel model = ScenarioHudSnapshotBuilder.Build(null, "Title", null, 0, null);
            Assert.IsTrue(model.IsSandbox);
            Assert.AreEqual(0, model.FailureNotices.Count);
        }

        [Test]
        public void GuidanceGate_FirstLedgerShownOthersSuppressedLegacyFailOpen()
        {
            Assert.IsTrue(ScenarioGuidanceGate.ShouldShowFirstSessionGuidance(string.Empty));
            Assert.IsTrue(ScenarioGuidanceGate.ShouldShowFirstSessionGuidance(null));
            Assert.IsTrue(ScenarioGuidanceGate.ShouldShowFirstSessionGuidance("the-first-ledger"));
            Assert.IsFalse(ScenarioGuidanceGate.ShouldShowFirstSessionGuidance("saloon-circuit"));
            Assert.IsFalse(ScenarioGuidanceGate.ShouldShowFirstSessionGuidance("the-first-ledger-2"));
            Assert.AreEqual(string.Empty, ScenarioGuidanceGate.SuppressionReason("the-first-ledger"));
            Assert.IsTrue(ScenarioGuidanceGate.SuppressionReason("saloon-circuit").Contains("saloon-circuit"));
        }

        [Test]
        public void FailureBoard_DedupStillHoldsThroughHud()
        {
            var board = new ScenarioFailureNoticeBoard();
            Assert.IsTrue(board.Record("s", "g", 3));
            Assert.IsFalse(board.Record("s", "g", 3));
            Assert.AreEqual(1, board.Notices.Count);
        }
    }
}
