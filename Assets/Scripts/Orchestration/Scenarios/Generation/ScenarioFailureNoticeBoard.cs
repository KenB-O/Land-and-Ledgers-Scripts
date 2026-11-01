using System;
using System.Collections.Generic;

namespace LandLedgers.Orchestration.Scenarios.Generation
{
    /// <summary>
    /// Pass XI WS5 (player-facing, A-13): the player-visible record of
    /// scenario failures. <see cref="ScenarioRunStateEvents.ScenarioFailed"/>
    /// has zero production subscribers, so a failure was announced to
    /// nobody; the panel layer can read this board instead of inventing
    /// a new notification system. Recording is deduplicated by
    /// scenario+goal: two subscribers both recording the same failure
    /// (the plan's double-subscription case) produce exactly one notice.
    /// The board is an instance, not a static, so tests and sessions
    /// cannot leak notices into each other; a bootstrap owns one and
    /// subscribes it to ScenarioFailed.
    /// </summary>
    public sealed class ScenarioFailureNoticeBoard
    {
        public sealed class Notice
        {
            public string ScenarioId = string.Empty;
            public string GoalId = string.Empty;
            public int DeadlineDayIndex = -1;
        }

        private readonly List<Notice> notices = new List<Notice>();

        public IReadOnlyList<Notice> Notices => notices;

        /// <summary>Records a failure notice. Returns false when the same
        /// scenario+goal was already recorded, or when either id is empty.</summary>
        public bool Record(string scenarioId, string goalId, int deadlineDayIndex)
        {
            if (string.IsNullOrEmpty(scenarioId) || string.IsNullOrEmpty(goalId)) return false;
            foreach (Notice existing in notices)
            {
                if (string.Equals(existing.ScenarioId, scenarioId, StringComparison.Ordinal)
                    && string.Equals(existing.GoalId, goalId, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            notices.Add(new Notice
            {
                ScenarioId = scenarioId,
                GoalId = goalId,
                DeadlineDayIndex = deadlineDayIndex,
            });
            return true;
        }

        public void Clear()
        {
            notices.Clear();
        }
    }
}
