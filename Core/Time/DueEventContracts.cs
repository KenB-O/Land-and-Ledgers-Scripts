using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Time
{
    /// <summary>
    /// Pass IV (Living World Foundation): deterministic event-based time.
    ///
    /// The game runs at accelerated speeds (normal up to ~10x, dev testing to 100x).
    /// A single accelerated tick may cross task completions, passive-process completions,
    /// equipment deadlines, shift boundaries, resource releases, and inventory thresholds.
    /// These events must NOT be skipped.
    ///
    /// This contract defines the due-event scheduler: simulation systems register
    /// consequential future events with stable identities and absolute game-second due
    /// times; the driver pops all events due up to the current clock time IN DUE ORDER
    /// before advancing further. A deterministic fixture must produce equivalent
    /// consequential results in small steps, large steps, and at 10x/100x.
    ///
    /// Reuses the existing authoritative TimeManager clock. Does NOT create a competing
    /// world clock: due times are absolute game seconds on the TimeManager timeline.
    ///
    /// Canon: XXXI Part III §§3.1-3.7. Technical: XIV Part II §§2.1-2.8.
    /// </summary>
    public enum DueEventKind
    {
        Unspecified = 0,
        /// <summary>An active task's planned work is due to complete.</summary>
        TaskCompletionDue = 1,
        /// <summary>A passive process (proofing, baking, drying) finished its elapsed time.</summary>
        PassiveProcessComplete = 2,
        /// <summary>A tending/unload intervention is due (baker must return).</summary>
        TendingDue = 3,
        /// <summary>A tending deadline was missed; consequences apply.</summary>
        TendingDeadlineMissed = 4,
        /// <summary>A reserved resource's window ends; release it.</summary>
        ResourceReleaseDue = 5,
        /// <summary>A work shift boundary was crossed.</summary>
        ShiftBoundary = 6,
        /// <summary>An inventory deterioration or threshold check is due.</summary>
        InventoryThresholdDue = 7,
        /// <summary>A journey/shipment stage is due to complete.</summary>
        JourneyStageDue = 8,
        /// <summary>A scheduled activity window starts or ends.</summary>
        ActivityWindowDue = 9,
        /// <summary>
        /// Pass V: a person's birthday is due. Payload convention is
        /// "birthday:{personId}" (see AgeAuthority.BirthdayEventId); the
        /// scheduler-level event id is "due-BirthdayDue-birthday:{personId}".
        /// Append-only addition; existing values untouched.
        /// </summary>
        BirthdayDue = 10,
    }

    /// <summary>Pass IV: one scheduled consequential event. Plain data; persisted.</summary>
    [Serializable]
    public sealed class DueEvent
    {
        [SerializeField]
        private string eventId = string.Empty;

        [SerializeField]
        private DueEventKind kind = DueEventKind.Unspecified;

        /// <summary>Absolute game seconds on the TimeManager timeline.</summary>
        [SerializeField]
        private float dueGameSeconds;

        /// <summary>Owning record: task EntityId string, process id, reservation id...</summary>
        [SerializeField]
        private string payloadRef = string.Empty;

        /// <summary>Deterministic tie-break sequence for events due at the same second.</summary>
        [SerializeField]
        private long sequence;

        public string EventId => eventId;
        public DueEventKind Kind => kind;
        public float DueGameSeconds => dueGameSeconds;
        public string PayloadRef => payloadRef;
        public long Sequence => sequence;

        public DueEvent()
        {
        }

        public DueEvent(string eventId, DueEventKind kind, float dueGameSeconds, string payloadRef, long sequence)
        {
            this.eventId = eventId ?? string.Empty;
            this.kind = kind;
            this.dueGameSeconds = dueGameSeconds;
            this.payloadRef = payloadRef ?? string.Empty;
            this.sequence = sequence;
        }
    }

    /// <summary>
    /// Pass IV: the due-event scheduler contract. Implementations keep events ordered by
    /// (dueGameSeconds, sequence) so concurrent events process deterministically.
    /// All mutating operations are idempotent-safe: scheduling the same eventId twice
    /// replaces (never duplicates); cancelling a missing id is a no-op.
    /// </summary>
    public interface IDueEventScheduler
    {
        /// <summary>
        /// Schedules an event. Returns null on success, or a refusal reason.
        /// Scheduling an existing eventId replaces it (idempotent).
        /// </summary>
        string Schedule(DueEventKind kind, float dueGameSeconds, string payloadRef, out string eventId);

        /// <summary>Cancels a scheduled event. No-op when the id is unknown.</summary>
        void Cancel(string eventId);

        /// <summary>
        /// Pops and returns every event with dueGameSeconds &lt;= currentGameSeconds,
        /// ordered by (dueGameSeconds, sequence). Removed from the schedule.
        /// </summary>
        List<DueEvent> PopDue(float currentGameSeconds);

        /// <summary>Peeks the next due event without removing it (null when empty).</summary>
        DueEvent PeekNext();

        /// <summary>Number of scheduled (not yet due) events.</summary>
        int ScheduledCount { get; }

        /// <summary>Cancels every event whose payloadRef matches (task/process cancelled).</summary>
        void CancelForPayload(string payloadRef);
    }

    /// <summary>
    /// Pass IV: deterministic event-id conventions. Stable across save/load so a
    /// restored game never double-schedules the same logical event.
    /// </summary>
    public static class DueEventIdConventions
    {
        public static string ForTask(string taskEntityIdString, DueEventKind kind)
        {
            return "due-" + kind + "-" + (taskEntityIdString ?? string.Empty);
        }

        public static string ForProcess(string processId, DueEventKind kind)
        {
            return "due-" + kind + "-proc-" + (processId ?? string.Empty);
        }

        public static string ForReservation(string reservationId)
        {
            return "due-ResourceReleaseDue-resv-" + (reservationId ?? string.Empty);
        }
    }
}
