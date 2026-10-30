using System;
using System.Collections.Generic;
using System.Text;

namespace LandLedgers.Orchestration.Scenarios.Generation
{
    /// <summary>
    /// Pass XI WS5 (player-facing): the Unity-free text a scenario panel
    /// shows for dated objectives. The StartPanel previously rendered only
    /// the director's goal list — every goal marked [X] or [ ], no deadline, and a
    /// failed run indistinguishable from an unfinished one. This builder
    /// is the single place that turns a <see cref="ScenarioHeadlineSnapshot"/>
    /// plus the latched-goal set into panel lines, so the panel, the
    /// puppet-master read path, and tests all agree. It never invents
    /// state: deadlines come from the headline, completion from the
    /// latched set, failure from the snapshot's failed fields.
    /// </summary>
    public static class ScenarioObjectivePresentation
    {
        /// <summary>One goal row for <see cref="BuildObjectiveLines"/>.</summary>
        public sealed class GoalRow
        {
            public string GoalId = string.Empty;
            public string Text = string.Empty;
            /// <summary>Deadline day; -1 (or int.MaxValue) means no deadline.</summary>
            public int DeadlineDayIndex = -1;
        }

        /// <summary>
        /// Deadline text relative to the current day. The boundary matches
        /// <see cref="ScenarioObjectiveService.Evaluate"/> exactly: a goal
        /// fails only when the current day is PAST its deadline, so on
        /// the deadline day itself the text is "Due today", never
        /// "Overdue". -1 and the int.MaxValue sentinel both mean no
        /// deadline.
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
        /// Builds the panel text: one line per goal ([X] when latched,
        /// otherwise [ ], each with its deadline text when it has one),
        /// plus a failure banner naming the failed goal and its deadline
        /// when the headline reports a failure. A failed run can never
        /// again render as merely unfinished.
        /// </summary>
        public static string BuildObjectiveLines(
            ScenarioHeadlineSnapshot headline,
            IReadOnlyList<GoalRow> goals,
            IReadOnlyList<string> latchedGoalIds,
            int currentDayIndex)
        {
            var lines = new StringBuilder("SCENARIO OBJECTIVES\n");
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
                foreach (GoalRow goal in goals)
                {
                    if (goal == null) continue;
                    string goalId = goal.GoalId ?? string.Empty;
                    // Keep the HUD glyphs inside the bundled TMP font's
                    // supported ASCII range; the previous check/square symbols
                    // rendered as replacement boxes in the actual Main Scene.
                    lines.Append(latched.Contains(goalId) ? "[X] " : "[ ] ");
                    lines.Append(string.IsNullOrWhiteSpace(goal.Text) ? goalId : goal.Text);
                    if (goal.DeadlineDayIndex >= 0 && goal.DeadlineDayIndex != int.MaxValue)
                    {
                        lines.Append(" — ");
                        lines.Append(FormatDeadline(goal.DeadlineDayIndex, currentDayIndex));
                    }
                    lines.Append('\n');
                }
            }
            if (headline != null && headline.IsFailed)
            {
                string failedGoal = headline.FailedGoalId ?? string.Empty;
                if (string.IsNullOrEmpty(failedGoal) && headline.FailedCount > 0)
                {
                    lines.Append("SCENARIO FAILED.\n");
                }
                else if (!string.IsNullOrEmpty(failedGoal))
                {
                    lines.Append("SCENARIO FAILED — goal '");
                    lines.Append(failedGoal);
                    lines.Append("' missed its deadline");
                    if (headline.FailedDeadlineDayIndex >= 0)
                    {
                        lines.Append(" (day ");
                        lines.Append(headline.FailedDeadlineDayIndex);
                        lines.Append(')');
                    }
                    lines.Append(".\n");
                }
            }
            return lines.ToString();
        }
    }
}
