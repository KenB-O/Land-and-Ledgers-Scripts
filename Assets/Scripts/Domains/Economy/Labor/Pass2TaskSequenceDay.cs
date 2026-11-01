using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// Pass 2 (task system): a work day as a SEQUENCE of tasks, not a
    /// fixed 10-hour block. A person shows up and does tasks of varying
    /// durations — a half-hour task, a five-minute task, an hour task, a
    /// meal, another task — until the day's work is done or cannot be
    /// done. This class is a planning view over a day's tasks: it sums
    /// their minutes, lists what was not completed, and reports the
    /// total honestly. It does NOT replace WorkTimeBudget, and it does
    /// NOT remove the existing 600-minute default cap: whether the cap
    /// survives, is raised, or is replaced by pure task sequencing is a
    /// Kennedy decision, and until it is made, this sequence view and
    /// the budget coexist. A driver that enforces only this sequence
    /// while the budget still caps would be lying about one of them.
    ///
    /// Life tasks (eat, sleep) belong in the same sequence in principle:
    /// a meal is a task in the day's order, not a separate clock. In
    /// this pass the sequence accepts life task ids as entries; the
    /// driver decision about whether meal consumption stays
    /// day-boundary or becomes task-driven is documented, not assumed.
    /// </summary>
    public sealed class Pass2TaskSequenceDay
    {
        public sealed class SequenceEntry
        {
            public string TaskDefinitionId = string.Empty;
            public int PlannedMinutes;
            public bool Completed;
            public string MissedReason = string.Empty;

            public SequenceEntry() { }
        }

        private readonly List<SequenceEntry> entries = new List<SequenceEntry>();

        public IReadOnlyList<SequenceEntry> Entries => entries;

        public void AddTask(string taskDefinitionId, int plannedMinutes)
        {
            entries.Add(new SequenceEntry
            {
                TaskDefinitionId = taskDefinitionId ?? string.Empty,
                PlannedMinutes = Math.Max(1, plannedMinutes),
                Completed = false,
            });
        }

        public void MarkCompleted(string taskDefinitionId)
        {
            foreach (var entry in entries)
            {
                if (string.Equals(entry.TaskDefinitionId, taskDefinitionId, StringComparison.Ordinal) && !entry.Completed)
                {
                    entry.Completed = true;
                    entry.MissedReason = string.Empty;
                    return;
                }
            }
        }

        public void MarkMissed(string taskDefinitionId, string reason)
        {
            foreach (var entry in entries)
            {
                if (string.Equals(entry.TaskDefinitionId, taskDefinitionId, StringComparison.Ordinal) && !entry.Completed)
                {
                    entry.MissedReason = reason ?? "Not completed.";
                    return;
                }
            }
        }

        public int TotalPlannedMinutes()
        {
            int total = 0;
            foreach (var entry in entries) total += Math.Max(1, entry.PlannedMinutes);
            return total;
        }

        public int CompletedMinutes()
        {
            int total = 0;
            foreach (var entry in entries)
            {
                if (entry.Completed) total += Math.Max(1, entry.PlannedMinutes);
            }
            return total;
        }

        public List<SequenceEntry> MissedEntries()
        {
            var missed = new List<SequenceEntry>();
            foreach (var entry in entries)
            {
                if (!entry.Completed) missed.Add(entry);
            }
            return missed;
        }
    }
}
