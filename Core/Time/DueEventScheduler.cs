using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Time
{
    /// <summary>
    /// Pass IV (Worker 1): deterministic due-event scheduler.
    ///
    /// Events are ordered by (dueGameSeconds, sequence) so concurrent events process
    /// deterministically; PopDue returns every event due up to the current clock time
    /// in that order. Event ids are stable ("due-" + kind + "-" + payloadRef, matching
    /// DueEventIdConventions) — scheduling the same id twice REPLACES the event
    /// (idempotent reschedule, never duplicates); cancelling a missing id is a no-op.
    ///
    /// Due times are absolute game seconds on the TimeManager timeline; this class
    /// owns no clock. Plain C# — fully deterministic and testable.
    ///
    /// Canon: XXXI Part III §§3.1-3.7. Technical: XIV Part II §§2.1-2.8.
    /// </summary>
    [Serializable]
    public sealed class DueEventScheduler : IDueEventScheduler
    {
        private readonly Dictionary<string, DueEvent> byId =
            new Dictionary<string, DueEvent>(StringComparer.Ordinal);

        /// <summary>All scheduled events, kept sorted by (dueGameSeconds, sequence).</summary>
        private readonly List<DueEvent> ordered = new List<DueEvent>();

        private long nextSequence = 1;

        public int ScheduledCount => byId.Count;

        /// <summary>
        /// Schedules an event. Returns null on success, or a refusal reason.
        /// Scheduling an existing eventId replaces it (idempotent).
        /// </summary>
        public string Schedule(DueEventKind kind, float dueGameSeconds, string payloadRef, out string eventId)
        {
            eventId = null;
            if (float.IsNaN(dueGameSeconds) || float.IsInfinity(dueGameSeconds) || dueGameSeconds < 0f)
            {
                return "dueGameSeconds must be finite and >= 0 (got " + dueGameSeconds + ").";
            }

            string id = "due-" + kind + "-" + (payloadRef ?? string.Empty);
            DueEvent existing;
            if (byId.TryGetValue(id, out existing))
            {
                ordered.Remove(existing);
            }

            var due = new DueEvent(id, kind, dueGameSeconds, payloadRef, nextSequence++);
            byId[id] = due;
            InsertOrdered(due);
            eventId = id;
            return null;
        }

        /// <summary>
        /// Pass V P0-2: schedules under a caller-chosen stable id. Same
        /// validation and replace semantics as <see cref="Schedule"/>.
        /// </summary>
        public string ScheduleWithEventId(string eventId, DueEventKind kind, float dueGameSeconds, string payloadRef)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                return "eventId must be non-empty.";
            }

            if (float.IsNaN(dueGameSeconds) || float.IsInfinity(dueGameSeconds) || dueGameSeconds < 0f)
            {
                return "dueGameSeconds must be finite and >= 0 (got " + dueGameSeconds + ").";
            }

            DueEvent existing;
            if (byId.TryGetValue(eventId, out existing))
            {
                ordered.Remove(existing);
            }

            var due = new DueEvent(eventId, kind, dueGameSeconds, payloadRef, nextSequence++);
            byId[eventId] = due;
            InsertOrdered(due);
            return null;
        }

        /// <summary>Cancels a scheduled event. No-op when the id is unknown.</summary>
        public void Cancel(string eventId)
        {
            if (string.IsNullOrEmpty(eventId))
            {
                return;
            }

            DueEvent existing;
            if (byId.TryGetValue(eventId, out existing))
            {
                byId.Remove(eventId);
                ordered.Remove(existing);
            }
        }

        /// <summary>
        /// Pops and returns every event with dueGameSeconds &lt;= currentGameSeconds,
        /// ordered by (dueGameSeconds, sequence). Removed from the schedule.
        /// </summary>
        public List<DueEvent> PopDue(float currentGameSeconds)
        {
            var due = new List<DueEvent>();
            while (ordered.Count > 0 && ordered[0].DueGameSeconds <= currentGameSeconds)
            {
                DueEvent next = ordered[0];
                ordered.RemoveAt(0);
                byId.Remove(next.EventId);
                due.Add(next);
            }

            return due;
        }

        /// <summary>Peeks the next due event without removing it (null when empty).</summary>
        public DueEvent PeekNext()
        {
            return ordered.Count > 0 ? ordered[0] : null;
        }

        /// <summary>Cancels every event whose payloadRef matches (task/process cancelled).</summary>
        public void CancelForPayload(string payloadRef)
        {
            if (payloadRef == null)
            {
                return;
            }

            var ids = new List<string>();
            foreach (KeyValuePair<string, DueEvent> entry in byId)
            {
                if (string.Equals(entry.Value.PayloadRef, payloadRef, StringComparison.Ordinal))
                {
                    ids.Add(entry.Key);
                }
            }

            foreach (string id in ids)
            {
                Cancel(id);
            }
        }

        /// <summary>
        /// G-05: purges every scheduled event (New Game / Restart). Sequence
        /// restarts at 1, matching <see cref="LoadFromSaveDto"/> clear-then-load.
        /// </summary>
        public void Clear()
        {
            byId.Clear();
            ordered.Clear();
            nextSequence = 1;
        }

        /// <summary>Pass IV: captures the schedule for the save pipeline.</summary>
        public DueEventSchedulerSaveDto CaptureSaveDto()
        {
            return new DueEventSchedulerSaveDto
            {
                events = new List<DueEvent>(ordered),
                nextSequence = nextSequence,
            };
        }

        /// <summary>
        /// Pass IV: restores the schedule (clear-then-load; idempotent). The sequence
        /// cursor is restored past every loaded event so ids never repeat.
        /// </summary>
        public void LoadFromSaveDto(DueEventSchedulerSaveDto dto)
        {
            byId.Clear();
            ordered.Clear();
            nextSequence = 1;
            if (dto == null || dto.events == null)
            {
                return;
            }

            foreach (DueEvent due in dto.events)
            {
                if (due == null || string.IsNullOrEmpty(due.EventId))
                {
                    continue;
                }

                DueEvent previous;
                if (byId.TryGetValue(due.EventId, out previous))
                {
                    ordered.Remove(previous);
                }

                byId[due.EventId] = due;
                InsertOrdered(due);
                nextSequence = Math.Max(nextSequence, due.Sequence + 1);
            }

            nextSequence = Math.Max(nextSequence, Math.Max(1, dto.nextSequence));
        }

        private void InsertOrdered(DueEvent due)
        {
            int lo = 0;
            int hi = ordered.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (CompareEvents(ordered[mid], due) < 0)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            ordered.Insert(lo, due);
        }

        private static int CompareEvents(DueEvent left, DueEvent right)
        {
            int byDue = left.DueGameSeconds.CompareTo(right.DueGameSeconds);
            if (byDue != 0)
            {
                return byDue;
            }

            return left.Sequence.CompareTo(right.Sequence);
        }
    }

    /// <summary>Pass IV (Worker 1): persisted due-event schedule. Owned by DueEventScheduler.</summary>
    [Serializable]
    public sealed class DueEventSchedulerSaveDto
    {
        public List<DueEvent> events = new List<DueEvent>();
        public long nextSequence = 1;
    }
}
