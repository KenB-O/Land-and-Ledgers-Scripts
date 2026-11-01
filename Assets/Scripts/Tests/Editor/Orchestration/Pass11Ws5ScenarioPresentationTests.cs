using System.Collections.Generic;
using LandLedgers.Orchestration.Scenarios.Generation;
using NUnit.Framework;

namespace LandLedgers.Editor.Orchestration
{
    /// <summary>
    /// Pass XI WS5 (Part A): scenario headline deadlines, failure
    /// visibility, and the presentation builder. A-14 (late-latch
    /// semantics) is deliberately NOT covered here: Evaluate is
    /// unchanged by WS5 and its lenient behaviour stays pinned by the
    /// existing Pass9ReviewFixScenarioLatchTests; see the WS5 report.
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws5ScenarioPresentationTests
    {
        [Test]
        public void Headline_LiveNoDeadlineSentinelReportsMinusOne()
        {
            var service = new ScenarioObjectiveService();
            service.BeginScenario("the-first-ledger");
            // Every First Ledger goal registers int.MaxValue (see
            // FirstLedgerBootstrap.RegisterObjectiveQualifiers). The
            // in-session headline used to report 2147483647.
            service.RegisterDatedQualifier("goal-a", int.MaxValue, () => false);
            ScenarioHeadlineSnapshot headline = service.BuildHeadlineSnapshot("Title", 0, 1, "goal-a");
            Assert.AreEqual(-1, headline.DeadlineDayIndex);
        }

        [Test]
        public void Headline_FiniteDeadlineReportedOnLiveAndPostLoadPaths()
        {
            var service = new ScenarioObjectiveService();
            service.BeginScenario("saloon-circuit");
            service.RegisterDatedQualifier("goal-b", 42, () => false);
            Assert.AreEqual(42, service.BuildHeadlineSnapshot("Title", 0, 1, "goal-b").DeadlineDayIndex);

            // Post-load: live qualifiers are empty until the bootstrap
            // rebuilds them; the persisted registration must still win.
            var reloaded = new ScenarioObjectiveService();
            reloaded.LoadRunState(service.CaptureRunState());
            Assert.AreEqual(42, reloaded.BuildHeadlineSnapshot("Title", 0, 1, "goal-b").DeadlineDayIndex);
        }

        [Test]
        public void Headline_FailureNamesGoalAndDeadline()
        {
            var service = new ScenarioObjectiveService();
            service.BeginScenario("saloon-circuit");
            service.RegisterDatedQualifier("goal-deadline", 10, () => false);
            Assert.IsTrue(service.Evaluate(11));
            ScenarioHeadlineSnapshot headline = service.BuildHeadlineSnapshot("Title", 0, 1, "goal-deadline");
            Assert.IsTrue(headline.IsFailed);
            Assert.AreEqual(1, headline.FailedCount);
            Assert.AreEqual("goal-deadline", headline.FailedGoalId);
            Assert.AreEqual(10, headline.FailedDeadlineDayIndex);
        }

        [Test]
        public void Headline_NotFailedCarriesNoFailureIdentity()
        {
            var service = new ScenarioObjectiveService();
            service.BeginScenario("saloon-circuit");
            service.RegisterDatedQualifier("goal-ok", 10, () => false);
            service.Evaluate(5);
            ScenarioHeadlineSnapshot headline = service.BuildHeadlineSnapshot("Title", 0, 1, "goal-ok");
            Assert.IsFalse(headline.IsFailed);
            Assert.AreEqual(string.Empty, headline.FailedGoalId);
            Assert.AreEqual(-1, headline.FailedDeadlineDayIndex);
        }

        [Test]
        public void FormatDeadline_BoundariesMatchEvaluate()
        {
            Assert.AreEqual("No deadline", ScenarioObjectivePresentation.FormatDeadline(-1, 10));
            Assert.AreEqual("No deadline", ScenarioObjectivePresentation.FormatDeadline(int.MaxValue, 10));
            // Evaluate fails only when dayIndex > deadline, so the
            // deadline day itself is "Due today", never "Overdue".
            Assert.AreEqual("Due today", ScenarioObjectivePresentation.FormatDeadline(10, 10));
            Assert.AreEqual("Due in 1 day", ScenarioObjectivePresentation.FormatDeadline(11, 10));
            Assert.AreEqual("Due in 3 days", ScenarioObjectivePresentation.FormatDeadline(13, 10));
            Assert.AreEqual("Overdue by 1 day", ScenarioObjectivePresentation.FormatDeadline(9, 10));
            Assert.AreEqual("Overdue by 3 days", ScenarioObjectivePresentation.FormatDeadline(7, 10));
        }

        [Test]
        public void BuildObjectiveLines_ShowsDeadlinesLatchedAndFailureBanner()
        {
            var headline = new ScenarioHeadlineSnapshot
            {
                ScenarioId = "saloon-circuit",
                Title = "Saloon Circuit",
                IsFailed = true,
                FailedCount = 1,
                FailedGoalId = "goal-deadline",
                FailedDeadlineDayIndex = 10,
            };
            var goals = new List<ScenarioObjectivePresentation.GoalRow>
            {
                new ScenarioObjectivePresentation.GoalRow { GoalId = "goal-done", Text = "Done goal", DeadlineDayIndex = -1 },
                new ScenarioObjectivePresentation.GoalRow { GoalId = "goal-deadline", Text = "Timed goal", DeadlineDayIndex = 10 },
                new ScenarioObjectivePresentation.GoalRow { GoalId = "goal-open", Text = "Open goal", DeadlineDayIndex = 12 },
            };
            string text = ScenarioObjectivePresentation.BuildObjectiveLines(
                headline, goals, new List<string> { "goal-done" }, 10);
            Assert.IsTrue(text.Contains("[X] Done goal"), text);
            Assert.IsTrue(text.Contains("[ ] Timed goal — Due today"), text);
            Assert.IsTrue(text.Contains("[ ] Open goal — Due in 2 days"), text);
            Assert.IsFalse(text.Contains("✓") || text.Contains("□"), text);
            Assert.IsTrue(text.Contains("SCENARIO FAILED — goal 'goal-deadline' missed its deadline (day 10)."), text);
        }

        [Test]
        public void BuildObjectiveLines_UnfailedRunHasNoFailureBanner()
        {
            var headline = new ScenarioHeadlineSnapshot { ScenarioId = "x", IsFailed = false };
            string text = ScenarioObjectivePresentation.BuildObjectiveLines(
                headline,
                new List<ScenarioObjectivePresentation.GoalRow>
                {
                    new ScenarioObjectivePresentation.GoalRow { GoalId = "g", Text = "Goal", DeadlineDayIndex = int.MaxValue },
                },
                new List<string>(),
                3);
            Assert.IsFalse(text.Contains("FAILED"), text);
            Assert.IsFalse(text.Contains("No deadline"), text);
        }

        [Test]
        public void FailureNoticeBoard_TwoSubscribersProduceOneNotice()
        {
            var board = new ScenarioFailureNoticeBoard();
            // Two subscribers (e.g. bootstrap + panel) both recording the
            // same failure must not produce two player-visible notices.
            Assert.IsTrue(board.Record("saloon-circuit", "goal-deadline", 10));
            Assert.IsFalse(board.Record("saloon-circuit", "goal-deadline", 10));
            Assert.AreEqual(1, board.Notices.Count);
            Assert.AreEqual("saloon-circuit", board.Notices[0].ScenarioId);
            Assert.AreEqual("goal-deadline", board.Notices[0].GoalId);
            Assert.AreEqual(10, board.Notices[0].DeadlineDayIndex);

            Assert.IsTrue(board.Record("saloon-circuit", "goal-other", 20));
            Assert.AreEqual(2, board.Notices.Count);
            Assert.IsFalse(board.Record(string.Empty, "goal-other", 20));
            Assert.IsFalse(board.Record("saloon-circuit", string.Empty, 20));
            board.Clear();
            Assert.AreEqual(0, board.Notices.Count);
        }
    }
}
