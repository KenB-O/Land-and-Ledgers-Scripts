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

        /// <summary>
        /// Pass IX WS4 (A4-17): binary min-heap by (dueGameSeconds, sequence),
        /// replacing the sorted list whose PopDue drained with RemoveAt(0)
        /// (O(k·n) per cohort) and whose Schedule/Cancel shifted O(n) entries.
        /// Heap push/pop are O(log n). Replacement and cancellation are lazy:
        /// the superseded heap entry becomes stale (byId no longer maps its id
        /// to that exact object) and is discarded when it reaches the top, so
        /// the observable pop order — (dueGameSeconds, sequence) over live
        /// events — is exactly the pre-index order.
        /// </summary>
        private readonly List<DueEvent> heap = new List<DueEvent>();

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
            // Replace-by-id: overwriting byId orphans any previous heap entry
            // for this id (it becomes stale and is skipped lazily).
            var due = new DueEvent(id, kind, dueGameSeconds, payloadRef, nextSequence++);
            byId[id] = due;
            HeapPush(due);
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

            var due = new DueEvent(eventId, kind, dueGameSeconds, payloadRef, nextSequence++);
            byId[eventId] = due;
            HeapPush(due);
            return null;
        }

        /// <summary>Cancels a scheduled event. No-op when the id is unknown.</summary>
        public void Cancel(string eventId)
        {
            if (string.IsNullOrEmpty(eventId))
            {
                return;
            }

            // Lazy removal: the heap entry for this id is now stale and is
            // discarded when it reaches the heap top.
            byId.Remove(eventId);
        }

        /// <summary>
        /// Pops and returns every event with dueGameSeconds &lt;= currentGameSeconds,
        /// ordered by (dueGameSeconds, sequence). Removed from the schedule.
        /// </summary>
        public List<DueEvent> PopDue(float currentGameSeconds)
        {
            var due = new List<DueEvent>();
            DueEvent next;
            while ((next = LiveTop()) != null && next.DueGameSeconds <= currentGameSeconds)
            {
                HeapPopTop();
                byId.Remove(next.EventId);
                due.Add(next);
            }

            return due;
        }

        /// <summary>Peeks the next due event without removing it (null when empty).</summary>
        public DueEvent PeekNext()
        {
            return LiveTop();
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
            heap.Clear();
            nextSequence = 1;
        }

        /// <summary>Pass IV: captures the schedule for the save pipeline.</summary>
        public DueEventSchedulerSaveDto CaptureSaveDto()
        {
            // The DTO lists live events in (dueGameSeconds, sequence) order —
            // exactly what the pre-index sorted list held. The heap itself is
            // unordered, so the capture sorts the live set.
            var live = new List<DueEvent>(byId.Values);
            live.Sort(CompareEvents);
            return new DueEventSchedulerSaveDto
            {
                events = live,
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
            heap.Clear();
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

                // A repeated id replaces the earlier entry (its heap entry
                // becomes stale), matching the pre-index clear-then-load.
                byId[due.EventId] = due;
                HeapPush(due);
                nextSequence = Math.Max(nextSequence, due.Sequence + 1);
            }

            nextSequence = Math.Max(nextSequence, Math.Max(1, dto.nextSequence));
        }

        /// <summary>An entry is live iff byId still maps its id to this exact object.</summary>
        private bool IsLive(DueEvent candidate)
        {
            DueEvent current;
            return candidate != null
                && byId.TryGetValue(candidate.EventId, out current)
                && ReferenceEquals(current, candidate);
        }

        /// <summary>Discards stale entries at the heap top; returns the live top or null.</summary>
        private DueEvent LiveTop()
        {
            while (heap.Count > 0)
            {
                DueEvent top = heap[0];
                if (IsLive(top))
                {
                    return top;
                }

                HeapPopTop();
            }

            return null;
        }

        private void HeapPush(DueEvent due)
        {
            heap.Add(due);
            int index = heap.Count - 1;
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (CompareEvents(heap[parent], heap[index]) <= 0)
                {
                    break;
                }

                DueEvent swap = heap[parent];
                heap[parent] = heap[index];
                heap[index] = swap;
                index = parent;
            }
        }

        /// <summary>Removes the heap top (live or stale). Heap must be non-empty.</summary>
        private void HeapPopTop()
        {
            int lastIndex = heap.Count - 1;
            DueEvent last = heap[lastIndex];
            heap.RemoveAt(lastIndex);
            if (lastIndex == 0)
            {
                return;
            }

            heap[0] = last;
            int index = 0;
            while (true)
            {
                int left = index * 2 + 1;
                int right = left + 1;
                int smallest = index;
                if (left < heap.Count && CompareEvents(heap[left], heap[smallest]) < 0)
                {
                    smallest = left;
                }

                if (right < heap.Count && CompareEvents(heap[right], heap[smallest]) < 0)
                {
                    smallest = right;
                }

                if (smallest == index)
                {
                    break;
                }

                DueEvent swap = heap[index];
                heap[index] = heap[smallest];
                heap[smallest] = swap;
                index = smallest;
            }
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
