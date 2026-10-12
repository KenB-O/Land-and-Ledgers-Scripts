using System;
using System.Collections.Generic;

namespace LandLedgers.Time
{
    /// <summary>
    /// Pass IV (Worker 4): a minimal, deterministic <see cref="IDueEventScheduler"/>
    /// implementation used by the household/autonomy drivers and the extended
    /// simulation fixtures. Worker 1 owns the production scheduler; this fallback
    /// exists so household domestic/shopping work can already be driven through
    /// real time in the harness without depending on Worker 1's branch.
    ///
    /// Semantics (per DueEventContracts):
    /// - Events ordered by (dueGameSeconds, sequence); sequence is a
    ///   monotonically increasing tie-break assigned at Schedule time, so the
    ///   same seed + same schedule order always yields the same pop order.
    /// - Scheduling an existing eventId REPLACES the event (idempotent).
    /// - Cancelling an unknown id is a no-op.
    /// - PopDue removes and returns every event with dueGameSeconds &lt;= now.
    /// - Save: DTO methods on this class (clear-then-load).
    /// </summary>
    public sealed class SimpleDueEventScheduler : IDueEventScheduler
    {
        private readonly List<DueEvent> events = new List<DueEvent>();
        private long nextSequence;

        public int ScheduledCount => events.Count;

        public string Schedule(DueEventKind kind, float dueGameSeconds, string payloadRef, out string eventId)
        {
            eventId = DueEventIdConventions.ForTask(payloadRef ?? string.Empty, kind);
            // Idempotent: replacing an existing event never duplicates.
            for (int i = events.Count - 1; i >= 0; i--)
            {
                if (string.Equals(events[i].EventId, eventId, StringComparison.Ordinal))
                {
                    events.RemoveAt(i);
                }
            }

            events.Add(new DueEvent(eventId, kind, dueGameSeconds, payloadRef ?? string.Empty, nextSequence++));
            return null;
        }

        public void Cancel(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                return;
            }

            for (int i = events.Count - 1; i >= 0; i--)
            {
                if (string.Equals(events[i].EventId, eventId, StringComparison.Ordinal))
                {
                    events.RemoveAt(i);
                }
            }
        }

        public List<DueEvent> PopDue(float currentGameSeconds)
        {
            var due = new List<DueEvent>();
            for (int i = events.Count - 1; i >= 0; i--)
            {
                if (events[i].DueGameSeconds <= currentGameSeconds)
                {
                    due.Add(events[i]);
                    events.RemoveAt(i);
                }
            }

            due.Sort((a, b) =>
            {
                int byTime = a.DueGameSeconds.CompareTo(b.DueGameSeconds);
                return byTime != 0 ? byTime : a.Sequence.CompareTo(b.Sequence);
            });
            return due;
        }

        public DueEvent PeekNext()
        {
            DueEvent best = null;
            foreach (DueEvent e in events)
            {
                if (best == null ||
                    e.DueGameSeconds < best.DueGameSeconds ||
                    (e.DueGameSeconds == best.DueGameSeconds && e.Sequence < best.Sequence))
                {
                    best = e;
                }
            }

            return best;
        }

        public void CancelForPayload(string payloadRef)
        {
            if (string.IsNullOrWhiteSpace(payloadRef))
            {
                return;
            }

            for (int i = events.Count - 1; i >= 0; i--)
            {
                if (string.Equals(events[i].PayloadRef, payloadRef, StringComparison.Ordinal))
                {
                    events.RemoveAt(i);
                }
            }
        }

        /// <summary>Save: captures the pending event queue (owning runtime class holds its DTO).</summary>
        public SimpleDueEventSchedulerSaveDto CaptureSaveDto()
        {
            return new SimpleDueEventSchedulerSaveDto
            {
                events = new List<DueEvent>(events),
                nextSequence = nextSequence,
            };
        }

        /// <summary>Save: clear-then-load. Restored events resume in due order.</summary>
        public void LoadFromSaveDto(SimpleDueEventSchedulerSaveDto dto)
        {
            events.Clear();
            nextSequence = 0;
            if (dto == null)
            {
                return;
            }

            if (dto.events != null)
            {
                events.AddRange(dto.events);
            }

            nextSequence = Math.Max(0, dto.nextSequence);
        }
    }

    /// <summary>Save data for the simple due-event scheduler.</summary>
    [Serializable]
    public sealed class SimpleDueEventSchedulerSaveDto
    {
        public List<DueEvent> events = new List<DueEvent>();
        public long nextSequence;
    }
}
