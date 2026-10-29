using System.Collections.Generic;
using LandLedgers.Reporting;

namespace LandLedgers.Orchestration.Scenarios.Generation
{
    /// <summary>
    /// Pass XII Worker D (LL-PLAY-005, PASS11-006): the single adapter that
    /// turns authoritative scenario state into the HUD read model. It reads
    /// the headline from <see cref="ScenarioObjectiveService"/>, the
    /// latched set from the persisted run state, and the failure notices
    /// from <see cref="ScenarioFailureNoticeBoard"/> — and nothing else.
    /// It never re-evaluates goal predicates and never invents objectives:
    /// goal texts come from the caller (the scenario director/preset),
    /// because the objective service holds qualifiers, not display text.
    ///
    /// Codex wiring (NOT done by Muse): call this adapter once per HUD
    /// refresh from the scene's scenario service + notice board, and bind
    /// the returned model to the scenario HUD panel field-by-field per
    /// Docs/Pass12_PlayerExperience_Contracts.md. The live scene panel
    /// remains unwired.
    /// </summary>
    public static class ScenarioHudSnapshotBuilder
    {
        /// <summary>
        /// Builds the HUD model for the service's current run. When the
        /// service has no scenario, the result is the Sandbox model
        /// (no objectives, no failure state), regardless of board content.
        /// </summary>
        public static ScenarioHudReadModel Build(
            ScenarioObjectiveService service,
            string title,
            IReadOnlyList<ScenarioObjectivePresentation.GoalRow> goals,
            int currentDayIndex,
            ScenarioFailureNoticeBoard board)
        {
            if (service == null)
            {
                return ScenarioHudReadModel.Build(
                    string.Empty, title, currentDayIndex,
                    null, null, false, string.Empty, -1, null);
            }

            ScenarioRunState state = service.State;
            var latched = state != null && state.LatchedCompleteGoalIds != null
                ? state.LatchedCompleteGoalIds
                : new List<string>();

            var inputs = new List<ScenarioHudReadModel.GoalInput>();
            if (goals != null)
            {
                foreach (ScenarioObjectivePresentation.GoalRow goal in goals)
                {
                    if (goal == null) continue;
                    inputs.Add(new ScenarioHudReadModel.GoalInput
                    {
                        GoalId = goal.GoalId ?? string.Empty,
                        Text = goal.Text ?? string.Empty,
                        DeadlineDayIndex = goal.DeadlineDayIndex,
                    });
                }
            }

            int completed = 0;
            foreach (ScenarioHudReadModel.GoalInput input in inputs)
            {
                if (latched.Contains(input.GoalId)) completed++;
            }

            string nextGoalId = string.Empty;
            foreach (ScenarioHudReadModel.GoalInput input in inputs)
            {
                if (!latched.Contains(input.GoalId))
                {
                    nextGoalId = input.GoalId;
                    break;
                }
            }

            ScenarioHeadlineSnapshot headline = service.BuildHeadlineSnapshot(
                title, completed, inputs.Count, nextGoalId);

            var notices = new List<ScenarioHudReadModel.NoticeInput>();
            if (board != null)
            {
                foreach (ScenarioFailureNoticeBoard.Notice notice in board.Notices)
                {
                    if (notice == null) continue;
                    notices.Add(new ScenarioHudReadModel.NoticeInput
                    {
                        ScenarioId = notice.ScenarioId,
                        GoalId = notice.GoalId,
                        DeadlineDayIndex = notice.DeadlineDayIndex,
                    });
                }
            }

            return ScenarioHudReadModel.Build(
                headline.ScenarioId, headline.Title, currentDayIndex,
                inputs, latched, headline.IsFailed,
                headline.FailedGoalId, headline.FailedDeadlineDayIndex, notices);
        }
    }
}
