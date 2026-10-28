using System;
using System.Collections.Generic;

namespace LandLedgers.Reporting
{
    /// <summary>
    /// Pass XII Worker D (LL-PLAY-005, PASS11-006): the source contract for
    /// the scenario HUD. This is a pure DTO plus a pure builder — it takes
    /// authoritative state as primitive inputs and never reads the world
    /// itself, so the panel, the puppet-master read path, and tests all
    /// agree. It is populated from the scenario headline snapshot
    /// equivalents by the Generation-side adapter
    /// (ScenarioHudSnapshotBuilder), never by inventing state here.
    ///
    /// Semantics pinned by this model (A-14 UNCHANGED):
    /// - A latched goal stays complete even if its predicate later turns
    ///   false; an unlatched goal past its deadline stays failed.
    /// - Failure is sticky: once the headline reports a failure, the
    ///   banner and the failed row stay visible for the rest of the run.
    /// - Sandbox (no active scenario) renders NO objectives, NO deadlines,
    ///   and NO failure state — a stale failure board from a previous run
    ///   must never paint a failure onto a clean Sandbox session, so
    ///   notices are dropped when there is no active scenario.
    /// - Deadline text uses the same boundary as
    ///   ScenarioObjectiveService.Evaluate: a goal fails only when the
    ///   current day is PAST its deadline, so the deadline day itself is
    ///   "Due today", never "Overdue".
    ///
    /// Codex wiring (NOT done by Muse): bind one HUD panel to this model
    /// (see Docs/Pass12_PlayerExperience_Contracts.md). The live scene
    /// panel remains unwired; this model alone does not fix the HUD.
    /// </summary>
    [Serializable]
    public sealed class ScenarioHudReadModel
    {
        /// <summary>One objective row for the HUD panel.</summary>
        [Serializable]
        public sealed class GoalRow
        {
            public string GoalId = string.Empty;
            public string Text = string.Empty;
            /// <summary>Deadline day; -1 (or int.MaxValue on input) means no deadline.</summary>
            public int DeadlineDayIndex = -1;
            public string DeadlineText = string.Empty;
            public bool IsLatched;
            /// <summary>True only for the headline's failed goal.</summary>
            public bool IsFailed;
            /// <summary>Unlatched and past its deadline on the current day.</summary>
            public bool IsOverdue;
        }

        /// <summary>One surfaced failure notice (from the failure board).</summary>
        [Serializable]
        public sealed class FailureNoticeRow
        {
            public string ScenarioId = string.Empty;
            public string GoalId = string.Empty;
            public int DeadlineDayIndex = -1;
            public string DisplayText = string.Empty;
        }

        /// <summary>Input goal for <see cref="Build"/> (panel-agnostic).</summary>
        public sealed class GoalInput
        {
            public string GoalId = string.Empty;
            public string Text = string.Empty;
            public int DeadlineDayIndex = -1;
        }

        /// <summary>Input failure notice for <see cref="Build"/> (failure-board row).</summary>
        public sealed class NoticeInput
        {
            public string ScenarioId = string.Empty;
            public string GoalId = string.Empty;
            public int DeadlineDayIndex = -1;
        }

        public string ScenarioId = string.Empty;
        public string Title = string.Empty;
        public bool HasActiveScenario;
        /// <summary>True exactly when <see cref="HasActiveScenario"/> is false.</summary>
        public bool IsSandbox;
        public int CurrentDayIndex;
        public List<GoalRow> Goals = new List<GoalRow>();
        public int CompletedGoals;
        public int TotalGoals;
        /// <summary>"No scenario active", "In progress", "Complete", or "Failed".</summary>
        public string StatusText = string.Empty;
        /// <summary>"2 of 5 objectives complete", or "No scenario active".</summary>
        public string CompletionText = string.Empty;
        /// <summary>Empty when the run has not failed.</summary>
        public string FailureBanner = string.Empty;
        public List<FailureNoticeRow> FailureNotices = new List<FailureNoticeRow>();
        /// <summary>
        /// Sandbox-only guidance for the panel: what to show instead of
        /// objectives. Empty when a scenario is active.
        /// </summary>
        public string EmptyStateText = string.Empty;

        /// <summary>
        /// Deadline text relative to the current day. The boundary matches
        /// Evaluate exactly; -1 and the int.MaxValue sentinel both mean
        /// no deadline.
        /// </summary>
        public static string FormatDeadline(int deadlineDayIndex, int currentDayIndex)
        {
            if (deadlineDayIndex < 0 || deadlineDayIndex == int.MaxValue) return "No deadline";
            int remaining = deadlineDayIndex - currentDayIndex;
            if (remaining == 0) return "Due today";
            if (remaining > 0) return remaining == 1 ? "Due in 1 day" : "Due in " + remaining + " days";
            int overdue = -remaining;
            return overdue == 1 ? "Overdue by 1 day" : "Overdue by " + overdue + " days";
        }

        /// <summary>
        /// Builds the HUD model. <paramref name="isFailed"/>,
        /// <paramref name="failedGoalId"/> and
        /// <paramref name="failedDeadlineDayIndex"/> come from the
        /// authoritative headline; <paramref name="latchedGoalIds"/> from
        /// the persisted run state. Nothing here re-evaluates predicates.
        /// </summary>
        public static ScenarioHudReadModel Build(
            string scenarioId,
            string title,
            int currentDayIndex,
            IReadOnlyList<GoalInput> goals,
            IReadOnlyList<string> latchedGoalIds,
            bool isFailed,
            string failedGoalId,
            int failedDeadlineDayIndex,
            IReadOnlyList<NoticeInput> notices)
        {
            var model = new ScenarioHudReadModel
            {
                ScenarioId = scenarioId ?? string.Empty,
                Title = title ?? string.Empty,
                CurrentDayIndex = currentDayIndex,
            };
            model.HasActiveScenario = !string.IsNullOrEmpty(model.ScenarioId);
            model.IsSandbox = !model.HasActiveScenario;

            if (!model.HasActiveScenario)
            {
                model.StatusText = "No scenario active";
                model.CompletionText = "No scenario active";
                model.EmptyStateText = "Sandbox — no scenario objectives. Free play: no deadlines, no failure state.";
                return model;
            }

            var latched = new HashSet<string>(StringComparer.Ordinal);
            if (latchedGoalIds != null)
            {
                foreach (string id in latchedGoalIds)
                {
                    if (!string.IsNullOrEmpty(id)) latched.Add(id);
                }
            }

            if (goals != null)
            {
                foreach (GoalInput goal in goals)
                {
                    if (goal == null) continue;
                    string goalId = goal.GoalId ?? string.Empty;
                    int deadline = goal.DeadlineDayIndex == int.MaxValue ? -1 : goal.DeadlineDayIndex;
                    bool isLatched = latched.Contains(goalId);
                    bool isFailedGoal = isFailed && string.Equals(goalId, failedGoalId ?? string.Empty, StringComparison.Ordinal);
                    model.Goals.Add(new GoalRow
                    {
                        GoalId = goalId,
                        Text = string.IsNullOrWhiteSpace(goal.Text) ? goalId : goal.Text,
                        DeadlineDayIndex = deadline,
                        DeadlineText = deadline < 0 ? string.Empty : FormatDeadline(deadline, currentDayIndex),
                        IsLatched = isLatched,
                        IsFailed = isFailedGoal,
                        IsOverdue = !isLatched && deadline >= 0 && currentDayIndex > deadline,
                    });
                }
            }

            model.TotalGoals = model.Goals.Count;
            int completed = 0;
            foreach (GoalRow row in model.Goals)
            {
                if (row.IsLatched) completed++;
            }
            model.CompletedGoals = completed;
            model.CompletionText = model.TotalGoals == 0
                ? "No objectives"
                : completed + " of " + model.TotalGoals + " objectives complete";
            model.StatusText = isFailed
                ? "Failed"
                : (model.TotalGoals > 0 && completed >= model.TotalGoals ? "Complete" : "In progress");

            if (isFailed)
            {
                string failedGoal = failedGoalId ?? string.Empty;
                if (string.IsNullOrEmpty(failedGoal))
                {
                    model.FailureBanner = "SCENARIO FAILED.";
                }
                else
                {
                    model.FailureBanner = "SCENARIO FAILED — goal '" + failedGoal + "' missed its deadline"
                        + (failedDeadlineDayIndex >= 0 ? " (day " + failedDeadlineDayIndex + ")" : string.Empty) + ".";
                }
            }

            if (notices != null)
            {
                foreach (NoticeInput notice in notices)
                {
                    if (notice == null) continue;
                    if (!string.Equals(notice.ScenarioId ?? string.Empty, model.ScenarioId, StringComparison.Ordinal)) continue;
                    if (string.IsNullOrEmpty(notice.GoalId)) continue;
                    model.FailureNotices.Add(new FailureNoticeRow
                    {
                        ScenarioId = notice.ScenarioId,
                        GoalId = notice.GoalId,
                        DeadlineDayIndex = notice.DeadlineDayIndex,
                        DisplayText = "Scenario failed: goal '" + notice.GoalId + "' missed its deadline"
                            + (notice.DeadlineDayIndex >= 0 ? " (day " + notice.DeadlineDayIndex + ")" : string.Empty) + ".",
                    });
                }
            }

            return model;
        }
    }
}
