using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Tasks
{
    /// <summary>
    /// Precision of the clock readings held by a <see cref="TaskTimingRecord"/>.
    /// Full: every reading came from an installed clock (<see cref="Func{T, TResult}"/>
    /// returning absolute game seconds). DayOnly: no clock was installed, so times
    /// are day-start seconds derived from the task's day indexes (plan §2.4) —
    /// degraded mode is explicit, never silent.
    /// </summary>
    public enum TaskTimingClockPrecision
    {
        Full = 0,
        DayOnly = 1,
    }

    /// <summary>
    /// Priority 4 Slice 1 — the observer hook the diagnostic installs on
    /// <see cref="TaskAuthority"/> (plan 04-plan-task-duration-diagnostics.md §5 Slice 1).
    /// One method: the authority calls it at each existing transition site, after the
    /// transition has succeeded, passing the task, the transition kind, the clock
    /// reading (or -1 when no clock is installed), and the caller's day index
    /// (or -1 when the call site carries no day). The service derives the rest from
    /// the task; it never calls back into <see cref="TaskAuthority"/>.
    ///
    /// FIRE-AND-FORGET CONTRACT: an observer must not call any
    /// <see cref="TaskAuthority"/> method from inside <see cref="OnTransition"/>.
    /// The authority refuses nested timing notifications (counted on
    /// <see cref="TaskAuthority.TimingNotificationReentrancyRefusals"/>) so a
    /// re-entrant observer can never recurse the hook.
    ///
    /// Slice 2 adds <see cref="RecordRefusal"/>: callers that hold an assign/start
    /// refusal string feed it in (plan §3.2 L6). A refusal is not a transition and
    /// is never progress — it updates the refusal streak only (§2.1).
    /// </summary>
    public interface ITaskTimingObserver
    {
        void OnTransition(WorkTask task, string transitionKind, float nowSeconds, int nowDay);

        /// <summary>
        /// Records an assign/start refusal for a task (Slice 2, plan §3.2 L6).
        /// <paramref name="method"/> is the refusing authority method
        /// ("AssignTask" / "StartTask" / ...); <paramref name="reason"/> is the
        /// refusal string verbatim; <paramref name="day"/> is the day it was
        /// refused. A refusal is not progress: it never moves
        /// <see cref="TaskTimingRecord.LastProgressSeconds"/>.
        /// </summary>
        void RecordRefusal(string taskId, string method, string reason, int day);
    }

    /// <summary>
    /// Priority 4 Slice 1 — per-task timing sidecar (plan §2, §5 Slice 1).
    /// Keyed by task EntityId string — never a second generated key. This record is
    /// ephemeral diagnostic state: it is NOT persisted and no field is added to
    /// <see cref="WorkTask"/> (plan §1.3, §2.5). On load it is rebuilt from the
    /// task's day fields at day resolution, flagged
    /// <see cref="RebuiltAfterLoad"/>.
    ///
    /// Two quantities are kept side by side and never conflated (plan §2):
    /// worked minutes (<see cref="MinutesWorkedLastSeen"/>, whole minutes recorded
    /// by RecordWork / RecordWorkProgress — true work done) and clock seconds in
    /// state (the Accumulated* buckets — seconds the task spent in a status,
    /// including nights and Sundays).
    /// </summary>
    public sealed class TaskTimingRecord
    {
        /// <summary>Key used by <see cref="TaskDurationDiagnostics.AccumulatedBlockedSecondsByReason"/> for Paused time.</summary>
        public const string PausedBlockReasonKey = "Paused";

        /// <summary>Key used when a waiting state carries no reason at all.</summary>
        public const string NoReasonRecordedKey = "none recorded";

        public string TaskId { get; set; }
        public string DefinitionId { get; set; }
        public EntityId OwnerId { get; set; }
        public EntityId AssigneeId { get; set; }

        public float CreatedSeconds { get; set; }
        public int CreatedDay { get; set; }
        public float AssignedSeconds { get; set; }
        public int AssignedDay { get; set; }
        public float StartedSeconds { get; set; }
        public int StartedDay { get; set; }
        public float CompletedSeconds { get; set; }
        public int CompletedDay { get; set; }

        public string LastTransitionKind { get; set; }
        public float LastTransitionSeconds { get; set; }
        public int LastTransitionDay { get; set; }
        public float LastProgressSeconds { get; set; }

        /// <summary>Clock seconds accumulated while InProgress (M5b).</summary>
        public float AccumulatedActiveSeconds { get; set; }

        /// <summary>
        /// Blocked seconds split by reason class/text (M7): Paused time under
        /// "Paused", WaitingOnDependency time under the dependency description
        /// verbatim, WaitingOnPassiveProcess time under the due event id.
        /// </summary>
        public Dictionary<string, float> AccumulatedBlockedSecondsByReason { get; private set; }

        /// <summary>Total blocked seconds (sum of <see cref="AccumulatedBlockedSecondsByReason"/>).</summary>
        public float AccumulatedBlockedSeconds
        {
            get
            {
                float total = 0f;
                foreach (KeyValuePair<string, float> entry in AccumulatedBlockedSecondsByReason)
                {
                    total += entry.Value;
                }

                return total;
            }
        }

        /// <summary>Paused seconds (the "Paused" entry of the blocked-by-reason map).</summary>
        public float AccumulatedPausedSeconds
        {
            get
            {
                float value;
                return AccumulatedBlockedSecondsByReason.TryGetValue(PausedBlockReasonKey, out value) ? value : 0f;
            }
        }

        /// <summary>Clock seconds accumulated while Queued (M2).</summary>
        public float AccumulatedQueueSeconds { get; set; }

        /// <summary>Clock seconds accumulated while Assigned or ResourcesReserved.</summary>
        public float AccumulatedAssignedWaitingSeconds { get; set; }

        /// <summary>Travel seconds on linked leg tasks only (M6, §2.3). Zero in Slice 1: no linkage exists yet.</summary>
        public float AccumulatedTravelSeconds { get; set; }

        public int MinutesWorkedLastSeen { get; set; }
        public int PlannedMinutesLastSeen { get; set; }

        /// <summary>Reason text of the most recent blocked entry, verbatim (dependency description, due event id, or "Paused").</summary>
        public string LastBlockReason { get; set; }

        /// <summary>Most recent refusal fed in via RecordRefusal (Slice 2). Empty in Slice 1: no refusal feed exists yet.</summary>
        public string LastRefusal { get; set; }

        /// <summary>Authority method that produced <see cref="LastRefusal"/> ("AssignTask" / "StartTask" / ...). Slice 2.</summary>
        public string LastRefusalMethod { get; set; }

        /// <summary>Day <see cref="LastRefusal"/> was fed in (-1 = none). Slice 2.</summary>
        public int LastRefusalDay { get; set; }

        /// <summary>
        /// Consecutive days on which the same refusal reason was fed in (Slice 2,
        /// §3.3): incremented when a refusal for the next day carries the same reason
        /// text, reset to 1 on a new reason or a gap, unchanged by a same-day repeat.
        /// </summary>
        public int ConsecutiveRefusalDays { get; set; }

        /// <summary>Due event id captured on entering WaitingOnPassiveProcess (survives ResumeTask clearing it on the task).</summary>
        public string LastDueEventId { get; set; }

        public TaskTimingClockPrecision ClockPrecision { get; set; }
        public bool RebuiltAfterLoad { get; set; }
        public float FirstSeenSeconds { get; set; }

        // State-entry tracking the service uses to attribute elapsed clock time to
        // the bucket of the state being left. Internal: not part of the plan's
        // record fields, never persisted.
        internal TaskStatus CurrentStatus { get; set; }
        internal float StateEnteredSeconds { get; set; }
        internal int StateEnteredDay { get; set; }
        internal string CurrentBlockKey { get; set; }

        /// <summary>
        /// Closed state spells (Slice 2, internal bookkeeping, never persisted):
        /// every InProgress / Paused / Waiting spell the observer saw end, with its
        /// status, reason key (empty for active spells), and clock interval. Classify
        /// needs intervals, not just scalar bucket totals, to cap horizon-bounded
        /// explanations (L1 arrival day, L5 due time) and to overlap sleep windows
        /// with active time only (L3). The spell in progress is not listed here; it
        /// is reconstructed from <see cref="CurrentStatus"/> /
        /// <see cref="StateEnteredSeconds"/> at classification time. Rebuilt records
        /// (§2.5) carry no closed spans.
        /// </summary>
        internal List<TaskTimingSpan> ClosedSpans { get; private set; }

        public TaskTimingRecord()
        {
            TaskId = string.Empty;
            DefinitionId = string.Empty;
            OwnerId = default(EntityId);
            AssigneeId = default(EntityId);
            CreatedSeconds = -1f;
            CreatedDay = -1;
            AssignedSeconds = -1f;
            AssignedDay = -1;
            StartedSeconds = -1f;
            StartedDay = -1;
            CompletedSeconds = -1f;
            CompletedDay = -1;
            LastTransitionKind = string.Empty;
            LastTransitionSeconds = -1f;
            LastTransitionDay = -1;
            LastProgressSeconds = -1f;
            AccumulatedBlockedSecondsByReason = new Dictionary<string, float>(StringComparer.Ordinal);
            LastBlockReason = string.Empty;
            LastRefusal = string.Empty;
            LastRefusalMethod = string.Empty;
            LastRefusalDay = -1;
            ConsecutiveRefusalDays = 0;
            ClosedSpans = new List<TaskTimingSpan>();
            LastDueEventId = string.Empty;
            ClockPrecision = TaskTimingClockPrecision.DayOnly;
            RebuiltAfterLoad = false;
            FirstSeenSeconds = -1f;
            CurrentStatus = TaskStatus.Queued;
            StateEnteredSeconds = -1f;
            StateEnteredDay = -1;
            CurrentBlockKey = string.Empty;
        }
    }

    /// <summary>
    /// Priority 4 Slice 2 — one closed state spell on a <see cref="TaskTimingRecord"/>
    /// (plan §3). Internal bookkeeping only, never persisted. <see cref="Status"/> is
    /// the state the spell was spent in; <see cref="Key"/> is the blocked-reason key
    /// for Paused / Waiting spells (empty for active spells); the interval is
    /// [StartSeconds, EndSeconds] on the authoritative simulation clock.
    /// </summary>
    internal struct TaskTimingSpan
    {
        public TaskStatus Status;
        public string Key;
        public float StartSeconds;
        public float EndSeconds;
    }

    /// <summary>
    /// Priority 4 Slice 2 — the diagnostic parameters (plan §4.2). Every field is a
    /// DIAGNOSTIC PARAMETER — not Canon, not an authored rate, not a gameplay value.
    /// Changing one changes only how loudly the diagnostic speaks, never any
    /// scheduling behaviour. Defaults are the §4.2 values.
    /// </summary>
    public sealed class TaskDurationDiagnosticSettings
    {
        /// <summary>DIAGNOSTIC PARAMETER — not Canon, not an authored rate (§4.2). §3.1 P2 multiplier on expected active seconds.</summary>
        public float OverrunFactor { get; set; }

        /// <summary>DIAGNOSTIC PARAMETER — not Canon, not an authored rate (§4.2). §3.1 P2 additive grace, seconds (15 min).</summary>
        public float OverrunGraceSeconds { get; set; }

        /// <summary>DIAGNOSTIC PARAMETER — not Canon, not an authored rate (§4.2). §3.1 P1 no-progress grace, seconds (1 day).</summary>
        public float StallGraceSeconds { get; set; }

        /// <summary>DIAGNOSTIC PARAMETER — not Canon, not an authored rate (§4.2). §3.3 repeated-legitimate-explanation cap, days.</summary>
        public int EscalationDays { get; set; }

        /// <summary>DIAGNOSTIC PARAMETER — not Canon, not an authored rate (§4.2). At most this many warning lines per day across all tasks.</summary>
        public int MaxWarningsPerDay { get; set; }

        /// <summary>DIAGNOSTIC PARAMETER — not Canon, not an authored rate (§4.2). At most this many warnings per definition id per day.</summary>
        public int MaxWarningsPerDefinitionPerDay { get; set; }

        /// <summary>DIAGNOSTIC PARAMETER — not Canon, not an authored rate (§4.2). One warning per task per day.</summary>
        public int MaxWarningsPerTaskPerDay { get; set; }

        /// <summary>DIAGNOSTIC PARAMETER — not Canon, not an authored rate (§4.2). Bounded record store.</summary>
        public int MaxTrackedRecords { get; set; }

        /// <summary>DIAGNOSTIC PARAMETER — not Canon, not an authored rate (§4.2). Truncation for the evidence text in the warning line.</summary>
        public int MaxBlockReasonChars { get; set; }

        public TaskDurationDiagnosticSettings()
        {
            OverrunFactor = 2.0f;
            OverrunGraceSeconds = 900f;
            StallGraceSeconds = 86400f;
            EscalationDays = 3;
            MaxWarningsPerDay = 12;
            MaxWarningsPerDefinitionPerDay = 3;
            MaxWarningsPerTaskPerDay = 1;
            MaxTrackedRecords = 512;
            MaxBlockReasonChars = 120;
        }

        /// <summary>A fresh settings instance carrying the §4.2 defaults.</summary>
        public static TaskDurationDiagnosticSettings Defaults
        {
            get { return new TaskDurationDiagnosticSettings(); }
        }
    }

    /// <summary>
    /// Priority 4 Slice 2 — the classification verdict kinds (plan §3).
    /// Legitimate: the span is explained (or no predicate holds). Unexplained:
    /// P1 and P2 both hold (P2 alone for a terminal task). Escalated: the stall is
    /// explained but the same legitimate explanation has repeated past
    /// <see cref="TaskDurationDiagnosticSettings.EscalationDays"/> (§3.3) — the
    /// diagnostic warns with the repeated reason quoted, without reclassifying the
    /// stall as unexplained.
    /// </summary>
    public enum TaskTimingVerdictKind
    {
        Legitimate = 0,
        Unexplained = 1,
        Escalated = 2,
    }

    /// <summary>
    /// Priority 4 Slice 2 — the legitimate-delay taxonomy (plan §3.2). The class
    /// letters are diagnostic labels, not Canon. NoneRecorded means no §3.2 reason
    /// covers the span. L8 is defined but unavailable: no assignment path in source
    /// names "no eligible worker" as a refusal (verified by grep; plan §7 C-7), so
    /// no record ever classifies L8 in this slice.
    /// </summary>
    public enum TaskTimingReasonClass
    {
        NoneRecorded = 0,
        L1SupplierLeadTime = 1,
        L2Travel = 2,
        L3RestSleep = 3,
        L4WaitingOnDependency = 4,
        L5WaitingOnPassiveProcess = 5,
        L6BudgetScheduleRefusal = 6,
        L7ClosedDay = 7,
        L8NoWorkerExists = 8,
    }

    /// <summary>
    /// Priority 4 Slice 2 — one sleep/rest window for one person on one day, as
    /// returned by the schedule lookup (plan §3.2 L3). StartMinuteOfDay is the
    /// minute of day the window starts (0–1439); DurationMinutes is its length in
    /// whole minutes and may run past midnight (the evening part of a wrapping
    /// sleep is 1320 + 120). The lookup is keyed by (personId, dayIndex), so the
    /// day is the key, not a field here. Core/Tasks must not reference the
    /// Population schedule types (dependency direction, plan §5), so the service
    /// defines this small struct instead of reusing PersonActivityReservation.
    /// </summary>
    public struct TaskDurationSleepWindow
    {
        public int StartMinuteOfDay { get; set; }
        public int DurationMinutes { get; set; }
    }

    /// <summary>
    /// Priority 4 Slice 2 — the expected-vs-actual verdict for one task at one
    /// evaluation time (plan §5 Slice 2). Returned by
    /// <see cref="TaskDurationDiagnostics.Classify"/>; no log line is emitted in
    /// this slice. CoveredSeconds / UncoveredSeconds partition the task's open span
    /// (their sum is the span); LastProgressSeconds is the clock time of the last
    /// status advance or MinutesWorked increase the observer saw.
    /// </summary>
    public sealed class TaskTimingVerdict
    {
        public TaskTimingVerdictKind Verdict { get; set; }
        public TaskTimingReasonClass ReasonClass { get; set; }
        public float CoveredSeconds { get; set; }
        public float UncoveredSeconds { get; set; }
        public string EvidenceText { get; set; }
        public float LastProgressSeconds { get; set; }
        public float ExpectedActiveSeconds { get; set; }
        public int ExpectedMinutes { get; set; }
        public bool IsProvisional { get; set; }

        public TaskTimingVerdict()
        {
            Verdict = TaskTimingVerdictKind.Legitimate;
            ReasonClass = TaskTimingReasonClass.NoneRecorded;
            EvidenceText = string.Empty;
        }
    }

    /// <summary>
    /// Priority 4 Slice 1 — the task-duration diagnostic service (plan §5 Slice 1).
    /// Holds the timing records (bounded, §4), applies observed transitions, and
    /// rebuilds records from the authority after a load (§2.5).
    ///
    /// Slice 2 adds the expected-vs-actual comparison and the §3 classification
    /// (<see cref="Classify"/>), the diagnostic settings
    /// (<see cref="TaskDurationDiagnosticSettings"/>), the refusal feed
    /// (<see cref="RecordRefusal"/>), and the optional evidence lookups below.
    /// Still no warnings (Slice 3) and no driver wiring (Slice 4). The service is
    /// read-only with respect to every economic authority: it holds no ledger, no
    /// obligation, no inventory, and no custody, and it never calls a
    /// state-changing method on <see cref="TaskAuthority"/> (or anything else).
    ///
    /// Accumulation rule: on a status transition, the clock time elapsed since the
    /// previous transition is attributed to the bucket of the state being left
    /// (Queued → queue, Assigned/ResourcesReserved → assigned-waiting, InProgress
    /// → active, Paused/Waiting* → blocked by the reason recorded at entry).
    /// Progress notifications (RecordWork / RecordWorkProgress) change no state, so
    /// they update worked minutes and last-progress only — they never close or
    /// reopen a bucket, and they never overwrite the last transition.
    ///
    /// Evidence-lookup notes (plan §5 Slice 2 item 3, verified against source at
    /// implementation time): the supplier-arrival lookup is keyed by the blocked
    /// reason text (dependency description / order id as recorded verbatim). The
    /// due-time lookup is a plain Func keyed by due event id because neither
    /// <c>IDueEventScheduler</c> nor <c>SimpleDueEventScheduler</c> exposes a
    /// by-id read (Schedule / Cancel / PopDue / PeekNext only) — there is no
    /// scheduler method for Classify to call, so Slice 4 must feed due times from
    /// whatever source holds them, or L5 falls back to "due time unknown". The
    /// schedule lookup returns Sleep/Rest windows per (personId, dayIndex) as
    /// <see cref="TaskDurationSleepWindow"/> values; the real shape it mirrors is
    /// DailyActivityPlanner's two-window wrapping sleep (evening 1320+120, morning
    /// 0+420, both on the same day index). L8 is unavailable: no assignment path
    /// names "no eligible worker" (plan §7 C-7).
    /// </summary>
    public sealed class TaskDurationDiagnostics : ITaskTimingObserver
    {
        /// <summary>DIAGNOSTIC PARAMETER — not Canon, not an authored rate (§4.2). Default bound on tracked records.</summary>
        public const int DefaultMaxTrackedRecords = 512;

        private TaskDurationDiagnosticSettings settings = TaskDurationDiagnosticSettings.Defaults;
        private readonly Dictionary<string, TaskTimingRecord> records = new Dictionary<string, TaskTimingRecord>(StringComparer.Ordinal);
        private readonly List<string> recordOrder = new List<string>();

        public TaskDurationDiagnostics()
        {
        }

        /// <summary>
        /// The diagnostic parameters (plan §4.2). Never null: assigning null restores
        /// the defaults. Shared with <see cref="MaxTrackedRecords"/>.
        /// </summary>
        public TaskDurationDiagnosticSettings Settings
        {
            get { return settings; }
            set { settings = value ?? TaskDurationDiagnosticSettings.Defaults; }
        }

        /// <summary>DIAGNOSTIC PARAMETER — not Canon, not an authored rate (§4.2). Bounded record store (Slice 3 enforces the eviction rule). Delegates to <see cref="Settings"/>.</summary>
        public int MaxTrackedRecords
        {
            get { return settings != null ? settings.MaxTrackedRecords : DefaultMaxTrackedRecords; }
            set { if (settings != null) { settings.MaxTrackedRecords = Math.Max(1, value); } }
        }

        /// <summary>
        /// Optional evidence lookup (Slice 2, §3.2 L1): order/dependency id (the
        /// blocked reason text verbatim) → expected arrival day index, or null when
        /// unknown. Null by default (no lookup installed). The real source is wired
        /// in Slice 4, or recorded as absent.
        /// </summary>
        public Func<string, int?> SupplierArrivalLookup { get; set; }

        /// <summary>
        /// Optional evidence lookup (Slice 2, §3.2 L5): due event id →
        /// due absolute game seconds, or null when unknown. Null by default (no
        /// lookup installed). Read-only: the diagnostic never schedules or cancels
        /// due events. There is no by-id read on IDueEventScheduler to wire this
        /// from directly (see the class docstring); Slice 4 feeds it from whatever
        /// source holds the due times, or L5 falls back to "due time unknown".
        /// </summary>
        public Func<string, float?> DueEventDueTimeLookup { get; set; }

        /// <summary>
        /// Optional evidence lookup (Slice 2, §3.2 L3): (personId, dayIndex) →
        /// the person's Sleep/Rest windows for that day, or null when unknown.
        /// Null by default (no lookup installed). Windows are minute-of-day
        /// windows; the service converts them to absolute seconds with the same
        /// day-start convention Slice 1 uses (day × 86400 + minute × 60).
        /// </summary>
        public Func<int, int, List<TaskDurationSleepWindow>> ScheduleLookup { get; set; }

        /// <summary>Records evicted by the store bound so far.</summary>
        public int EvictedRecordCount { get; private set; }

        public int RecordCount
        {
            get { return records.Count; }
        }

        public TaskTimingRecord GetRecord(string taskId)
        {
            if (string.IsNullOrEmpty(taskId))
            {
                return null;
            }

            TaskTimingRecord record;
            return records.TryGetValue(taskId, out record) ? record : null;
        }

        public TaskTimingRecord GetRecord(EntityId taskId)
        {
            return GetRecord(taskId.ToString());
        }

        public List<TaskTimingRecord> GetAllRecords()
        {
            return new List<TaskTimingRecord>(records.Values);
        }

        public void OnTransition(WorkTask task, string transitionKind, float nowSeconds, int nowDay)
        {
            ApplyTransition(task, transitionKind, nowSeconds, nowDay);
        }

        /// <summary>
        /// Records an assign/start refusal for a task (Slice 2, §3.2 L6). The
        /// refusal string is fed in by the caller that holds it — the authority
        /// returns refusal reasons and stores them nowhere (G7). A refusal for a
        /// task the service has no record for is dropped, never invented: timing
        /// is only recorded for observed tasks. A refusal is not progress: it
        /// never moves LastProgressSeconds and never changes the last transition.
        /// The consecutive-day streak counts days on which the same reason text was
        /// fed in for the next day in a row; a same-day repeat leaves it alone and
        /// a new reason or a gap in days resets it to 1.
        /// </summary>
        public void RecordRefusal(string taskId, string method, string reason, int day)
        {
            if (string.IsNullOrEmpty(taskId) || string.IsNullOrEmpty(reason))
            {
                return;
            }

            TaskTimingRecord record;
            if (!records.TryGetValue(taskId, out record))
            {
                return;
            }

            string previousReason = record.LastRefusal;
            int previousDay = record.LastRefusalDay;
            record.LastRefusal = reason;
            record.LastRefusalMethod = method ?? string.Empty;
            if (day == previousDay)
            {
                // Same-day repeat: the streak is unchanged.
            }
            else if (day == previousDay + 1
                && string.Equals(reason, previousReason, StringComparison.Ordinal))
            {
                record.ConsecutiveRefusalDays++;
            }
            else
            {
                record.ConsecutiveRefusalDays = 1;
            }

            record.LastRefusalDay = day;
        }

        /// <summary>EntityId overload of <see cref="RecordRefusal(string, string, string, int)"/>.</summary>
        public void RecordRefusal(EntityId taskId, string method, string reason, int day)
        {
            RecordRefusal(taskId.ToString(), method, reason, day);
        }

        /// <summary>
        /// Slice 2 — the expected-vs-actual comparison and §3 classification for one
        /// record at one evaluation time (plan §3.1–§3.4). Returns null for a null
        /// record. Emits no log lines; Slice 3 turns verdicts into warnings.
        ///
        /// Expected active seconds are PlannedMinutes × 60 once assigned (M4); before
        /// assignment the definition BaseMinutes the record still carries is the
        /// provisional expectation (IsProvisional). Open spells are counted from the
        /// record's state-entry tracking, so a task evaluated days after its last
        /// transition is measured correctly without waiting for another transition.
        /// For a terminal record the span ends at completion (P1 does not apply —
        /// the task finished; P2 alone decides).
        ///
        /// UnexplainedSpanSeconds = openSpan − queueExplained − assignedExplained −
        /// activeExplained(L3) − legitimateBlocked − expectedTravelSeconds (0 in this
        /// slice: no travel linkage exists, §2.3). Each explained term covers only
        /// the bucket it belongs to, so the terms are disjoint and their sum never
        /// exceeds the open span; the remainder is clamped at 0. P1 is
        /// (spanEnd − LastProgressSeconds) &gt; StallGraceSeconds; P2 is
        /// uncovered &gt; (expectedActive × OverrunFactor + OverrunGraceSeconds).
        /// Escalation (§3.3) fires only for horizon-less explanations that repeat:
        /// an L6 refusal streak past EscalationDays, or an L4 named dependency held
        /// continuously past EscalationDays. Horizon-bounded waits (L1 arrival day,
        /// L5 due time) run to their horizon instead — a 14-day lead does not
        /// escalate on day 4 — and are unexplained only from the horizon on (§3.6).
        /// </summary>
        public TaskTimingVerdict Classify(TaskTimingRecord record, float nowSeconds, int nowDay)
        {
            if (record == null)
            {
                return null;
            }

            TaskDurationDiagnosticSettings diagnosticSettings = settings ?? TaskDurationDiagnosticSettings.Defaults;
            bool isTerminal = IsTerminalStatus(record.CurrentStatus);
            float spanEnd = isTerminal && record.CompletedSeconds >= 0f ? record.CompletedSeconds : nowSeconds;
            float openSpan = record.CreatedSeconds >= 0f ? Math.Max(0f, spanEnd - record.CreatedSeconds) : 0f;

            // Current open-spell portions (Slice 1 closes buckets only on
            // transitions, so the spell in progress must be counted here).
            float queueNow = 0f;
            float assignedNow = 0f;
            float activeNow = 0f;
            float blockedNow = 0f;
            if (record.StateEnteredSeconds >= 0f && spanEnd > record.StateEnteredSeconds)
            {
                float open = spanEnd - record.StateEnteredSeconds;
                switch (record.CurrentStatus)
                {
                    case TaskStatus.Queued:
                        queueNow = open;
                        break;
                    case TaskStatus.Assigned:
                    case TaskStatus.ResourcesReserved:
                        assignedNow = open;
                        break;
                    case TaskStatus.InProgress:
                        activeNow = open;
                        break;
                    case TaskStatus.Paused:
                    case TaskStatus.WaitingOnDependency:
                    case TaskStatus.WaitingOnPassiveProcess:
                        blockedNow = open;
                        break;
                }
            }

            float queueTotal = record.AccumulatedQueueSeconds + queueNow;
            float assignedTotal = record.AccumulatedAssignedWaitingSeconds + assignedNow;
            float activeTotal = record.AccumulatedActiveSeconds + activeNow;

            int expectedMinutes = Math.Max(1, record.PlannedMinutesLastSeen);
            bool isProvisional = record.AssignedSeconds < 0f;
            float expectedActiveSeconds = expectedMinutes * 60f;
            // Slice 2: no travel linkage exists (§2.3), so expected travel is 0 and
            // AccumulatedTravelSeconds is 0. The term is carried so Slice 3/4 can
            // fill it without changing this formula.
            float expectedTravelSeconds = 0f;

            // ---- Blocked time, classified per reason key (M7, §3.2) ----
            List<TaskTimingSpan> blockedSpans = CollectBlockedSpans(record, spanEnd, blockedNow);
            Dictionary<string, List<TaskTimingSpan>> spansByKey =
                new Dictionary<string, List<TaskTimingSpan>>(StringComparer.Ordinal);
            foreach (TaskTimingSpan span in blockedSpans)
            {
                List<TaskTimingSpan> list;
                if (!spansByKey.TryGetValue(span.Key, out list))
                {
                    list = new List<TaskTimingSpan>();
                    spansByKey.Add(span.Key, list);
                }

                list.Add(span);
            }

            float legitimateBlocked = 0f;
            TaskTimingReasonClass primaryClass = TaskTimingReasonClass.NoneRecorded;
            float primaryCovered = 0f;
            float primarySeconds = 0f;
            string primaryEvidence = "no reason recorded";
            foreach (KeyValuePair<string, List<TaskTimingSpan>> entry in spansByKey)
            {
                TaskTimingReasonClass keyClass;
                float keyCovered;
                string keyEvidence;
                float keySeconds = ClassifyBlockedKey(entry.Key, entry.Value, out keyClass, out keyCovered, out keyEvidence);
                legitimateBlocked += keyCovered;
                // Primary = the explanation covering the most seconds; ties break
                // toward the larger wait. A zero-coverage key with real seconds
                // (e.g. a due id with no known due time) still outranks "nothing".
                if (keyCovered > primaryCovered
                    || (keyCovered == primaryCovered && keySeconds > primarySeconds))
                {
                    primaryClass = keyClass;
                    primaryCovered = keyCovered;
                    primarySeconds = keySeconds;
                    primaryEvidence = keyEvidence;
                }
            }

            // ---- L6: a refusal legitimates waiting time on the refused day(s) only ----
            float queueExplained = 0f;
            float assignedExplained = 0f;
            if (!string.IsNullOrEmpty(record.LastRefusal) && record.ConsecutiveRefusalDays > 0)
            {
                float refusalBudget = record.ConsecutiveRefusalDays * 86400f;
                if (record.LastRefusalMethod == "StartTask")
                {
                    assignedExplained = Math.Min(assignedTotal, refusalBudget);
                }
                else
                {
                    queueExplained = Math.Min(queueTotal, refusalBudget);
                }

                float refusalCovered = queueExplained + assignedExplained;
                // The refusal is a standing explanation: it names the stall even
                // before it has covered any seconds (day 1 of the streak, when the
                // open span is still 0). It therefore outranks a NoneRecorded
                // primary with no seconds of its own, but never outranks a real
                // covering explanation, nor a NoneRecorded wait that has actual
                // unexplained seconds behind it (e.g. a long reason-less pause).
                bool refusalWins = refusalCovered > primaryCovered
                    || (refusalCovered == primaryCovered
                        && primaryClass == TaskTimingReasonClass.NoneRecorded
                        && primarySeconds <= 0f);
                if (refusalWins)
                {
                    primaryClass = TaskTimingReasonClass.L6BudgetScheduleRefusal;
                    primaryCovered = refusalCovered;
                    primarySeconds = refusalCovered;
                    primaryEvidence = record.LastRefusal;
                }
            }

            // ---- L3: sleep/rest windows cover only their own minutes, inside active time ----
            float sleepCovered = SleepCoveredSeconds(record, spanEnd, activeNow, activeTotal, nowDay);
            if (sleepCovered > primaryCovered)
            {
                primaryClass = TaskTimingReasonClass.L3RestSleep;
                primaryCovered = sleepCovered;
                primarySeconds = sleepCovered;
                primaryEvidence = "sleep/rest windows (L3)";
            }

            float covered = queueExplained + assignedExplained + sleepCovered + legitimateBlocked + expectedTravelSeconds;
            float uncovered = Math.Max(0f, openSpan - covered);

            float lastProgress = record.LastProgressSeconds >= 0f
                ? record.LastProgressSeconds
                : record.CreatedSeconds;
            bool p1 = !isTerminal && (spanEnd - lastProgress) > diagnosticSettings.StallGraceSeconds;
            bool p2 = uncovered > (expectedActiveSeconds * diagnosticSettings.OverrunFactor
                + diagnosticSettings.OverrunGraceSeconds);

            // ---- Escalation (§3.3): a legitimate reason that repeats is itself diagnostic ----
            bool escalates = false;
            TaskTimingReasonClass escalationClass = TaskTimingReasonClass.NoneRecorded;
            string escalationEvidence = string.Empty;
            if (p1)
            {
                if (primaryClass == TaskTimingReasonClass.L6BudgetScheduleRefusal
                    && record.ConsecutiveRefusalDays > diagnosticSettings.EscalationDays)
                {
                    escalates = true;
                    escalationClass = TaskTimingReasonClass.L6BudgetScheduleRefusal;
                    escalationEvidence = record.LastRefusal;
                }
                else if (primaryClass == TaskTimingReasonClass.L4WaitingOnDependency
                    && record.CurrentStatus == TaskStatus.WaitingOnDependency
                    && record.StateEnteredDay >= 0)
                {
                    int stallDays = nowDay >= record.StateEnteredDay
                        ? nowDay - record.StateEnteredDay + 1
                        : 1;
                    if (stallDays > diagnosticSettings.EscalationDays)
                    {
                        escalates = true;
                        escalationClass = TaskTimingReasonClass.L4WaitingOnDependency;
                        escalationEvidence = primaryEvidence;
                    }
                }
            }

            TaskTimingVerdictKind kind;
            if (escalates)
            {
                kind = TaskTimingVerdictKind.Escalated;
            }
            else if (isTerminal)
            {
                kind = p2 ? TaskTimingVerdictKind.Unexplained : TaskTimingVerdictKind.Legitimate;
            }
            else
            {
                kind = p1 && p2 ? TaskTimingVerdictKind.Unexplained : TaskTimingVerdictKind.Legitimate;
            }

            return new TaskTimingVerdict
            {
                Verdict = kind,
                ReasonClass = escalates ? escalationClass : primaryClass,
                CoveredSeconds = covered,
                UncoveredSeconds = uncovered,
                EvidenceText = escalates ? escalationEvidence : primaryEvidence,
                LastProgressSeconds = lastProgress,
                ExpectedActiveSeconds = expectedActiveSeconds,
                ExpectedMinutes = expectedMinutes,
                IsProvisional = isProvisional,
            };
        }

        /// <summary>Short label for a reason class ("L1" … "L8", "none recorded") for the Slice 3 warning line.</summary>
        public static string ReasonClassLabel(TaskTimingReasonClass reasonClass)
        {
            switch (reasonClass)
            {
                case TaskTimingReasonClass.L1SupplierLeadTime: return "L1";
                case TaskTimingReasonClass.L2Travel: return "L2";
                case TaskTimingReasonClass.L3RestSleep: return "L3";
                case TaskTimingReasonClass.L4WaitingOnDependency: return "L4";
                case TaskTimingReasonClass.L5WaitingOnPassiveProcess: return "L5";
                case TaskTimingReasonClass.L6BudgetScheduleRefusal: return "L6";
                case TaskTimingReasonClass.L7ClosedDay: return "L7";
                case TaskTimingReasonClass.L8NoWorkerExists: return "L8";
                default: return "none recorded";
            }
        }

        private List<TaskTimingSpan> CollectBlockedSpans(TaskTimingRecord record, float spanEnd, float blockedNow)
        {
            var blockedSpans = new List<TaskTimingSpan>();
            foreach (TaskTimingSpan span in record.ClosedSpans)
            {
                if (span.Status == TaskStatus.Paused
                    || span.Status == TaskStatus.WaitingOnDependency
                    || span.Status == TaskStatus.WaitingOnPassiveProcess)
                {
                    blockedSpans.Add(span);
                }
            }

            if (blockedNow > 0f)
            {
                blockedSpans.Add(new TaskTimingSpan
                {
                    Status = record.CurrentStatus,
                    Key = string.IsNullOrEmpty(record.CurrentBlockKey)
                        ? TaskTimingRecord.NoReasonRecordedKey
                        : record.CurrentBlockKey,
                    StartSeconds = record.StateEnteredSeconds,
                    EndSeconds = spanEnd,
                });
            }

            return blockedSpans;
        }

        /// <summary>
        /// Classifies one blocked reason key (§3.2). Returns the key's total seconds
        /// and, via out parameters, its class, its covered seconds, and its evidence
        /// text. Paused carries no recorded reason (PauseTask stores its reason
        /// string nowhere on the task), so it is never L4. A dependency description
        /// with a future arrival day from the lookup is L1, capped at the end of
        /// the arrival day; without a lookup it is L4, covering the whole wait. A
        /// due event id with a known due time is L5, capped at the due time; with
        /// no due time known it stays L5 with zero covered seconds and evidence
        /// saying so — the id names the wait, but an explanation with no horizon
        /// evidence is not an explanation.
        /// </summary>
        private float ClassifyBlockedKey(string key, List<TaskTimingSpan> spans,
            out TaskTimingReasonClass reasonClass, out float coveredSeconds, out string evidenceText)
        {
            float keySeconds = 0f;
            foreach (TaskTimingSpan span in spans)
            {
                float length = span.EndSeconds - span.StartSeconds;
                if (length > 0f)
                {
                    keySeconds += length;
                }
            }

            if (key == TaskTimingRecord.PausedBlockReasonKey)
            {
                reasonClass = TaskTimingReasonClass.NoneRecorded;
                coveredSeconds = 0f;
                evidenceText = "paused, no reason recorded";
                return keySeconds;
            }

            if (key == TaskTimingRecord.NoReasonRecordedKey)
            {
                reasonClass = TaskTimingReasonClass.NoneRecorded;
                coveredSeconds = 0f;
                evidenceText = "no reason recorded";
                return keySeconds;
            }

            TaskStatus keyStatus = spans.Count > 0 ? spans[0].Status : TaskStatus.Queued;
            if (keyStatus == TaskStatus.WaitingOnPassiveProcess)
            {
                reasonClass = TaskTimingReasonClass.L5WaitingOnPassiveProcess;
                float? due = DueEventDueTimeLookup != null ? DueEventDueTimeLookup(key) : null;
                if (due.HasValue)
                {
                    coveredSeconds = OverlapBefore(spans, due.Value);
                    evidenceText = "due " + key + ", due " + due.Value + "s";
                    if (coveredSeconds <= 0f)
                    {
                        evidenceText += " (due time already passed)";
                    }
                }
                else
                {
                    coveredSeconds = 0f;
                    evidenceText = "due " + key + ", due time unknown (no due-event lookup installed)";
                }

                return keySeconds;
            }

            // WaitingOnDependency (or a key whose status is unknown): a future
            // arrival day promotes the named wait to L1; otherwise it is L4.
            int? arrivalDay = SupplierArrivalLookup != null ? SupplierArrivalLookup(key) : null;
            if (arrivalDay.HasValue)
            {
                reasonClass = TaskTimingReasonClass.L1SupplierLeadTime;
                // L1 accrues through the end of the arrival day (§3.6: from the day
                // after the arrival day the wait is unexplained).
                float coverUntil = (arrivalDay.Value + 1) * 86400f;
                coveredSeconds = OverlapBefore(spans, coverUntil);
                evidenceText = key + " (arrives day " + arrivalDay.Value + ")";
                if (coveredSeconds < keySeconds)
                {
                    evidenceText += " (arrival day passed)";
                }

                return keySeconds;
            }

            reasonClass = TaskTimingReasonClass.L4WaitingOnDependency;
            coveredSeconds = keySeconds;
            evidenceText = key;
            return keySeconds;
        }

        /// <summary>Seconds of the given spans falling before (or at) a horizon. Exact for any number of spells.</summary>
        private static float OverlapBefore(List<TaskTimingSpan> spans, float coverUntilSeconds)
        {
            float total = 0f;
            foreach (TaskTimingSpan span in spans)
            {
                if (span.EndSeconds <= span.StartSeconds)
                {
                    continue;
                }

                float coveredEnd = Math.Min(span.EndSeconds, coverUntilSeconds);
                if (coveredEnd > span.StartSeconds)
                {
                    total += coveredEnd - span.StartSeconds;
                }
            }

            return total;
        }

        /// <summary>
        /// L3 (§3.2): sleep/rest minutes inside active time only. The overlap is
        /// computed span by span against the windows the schedule lookup returns
        /// for each day from the first start day to the evaluation day, so nights
        /// before the first start and nights spent Paused / Waiting are never
        /// counted, and a multi-spell task is measured exactly. Capped at the
        /// active total. Zero when no lookup is installed, the task never started,
        /// or the assignee is not a person.
        /// </summary>
        private float SleepCoveredSeconds(TaskTimingRecord record, float spanEnd, float activeNow, float activeTotal, int nowDay)
        {
            if (ScheduleLookup == null
                || record.AssigneeId.Kind != EntityKind.Person
                || record.AssigneeId.Id < 0
                || record.StartedSeconds < 0f)
            {
                return 0f;
            }

            var activeSpans = new List<TaskTimingSpan>();
            foreach (TaskTimingSpan span in record.ClosedSpans)
            {
                if (span.Status == TaskStatus.InProgress)
                {
                    activeSpans.Add(span);
                }
            }

            if (activeNow > 0f)
            {
                activeSpans.Add(new TaskTimingSpan
                {
                    Status = TaskStatus.InProgress,
                    Key = string.Empty,
                    StartSeconds = record.StateEnteredSeconds,
                    EndSeconds = spanEnd,
                });
            }

            if (activeSpans.Count == 0)
            {
                return 0f;
            }

            int firstDay = record.StartedDay >= 0 ? record.StartedDay : record.CreatedDay;
            if (firstDay < 0 || nowDay < firstDay || nowDay - firstDay > 3660)
            {
                return 0f;
            }

            float sleepCovered = 0f;
            for (int day = firstDay; day <= nowDay; day++)
            {
                List<TaskDurationSleepWindow> windows = ScheduleLookup(record.AssigneeId.Id, day);
                if (windows == null)
                {
                    continue;
                }

                foreach (TaskDurationSleepWindow window in windows)
                {
                    if (window.DurationMinutes <= 0
                        || window.StartMinuteOfDay < 0
                        || window.StartMinuteOfDay >= 1440)
                    {
                        continue;
                    }

                    float windowStart = day * 86400f + window.StartMinuteOfDay * 60f;
                    float windowEnd = windowStart + window.DurationMinutes * 60f;
                    foreach (TaskTimingSpan span in activeSpans)
                    {
                        float overlapStart = Math.Max(span.StartSeconds, windowStart);
                        float overlapEnd = Math.Min(span.EndSeconds, windowEnd);
                        if (overlapEnd > overlapStart)
                        {
                            sleepCovered += overlapEnd - overlapStart;
                        }
                    }
                }
            }

            return Math.Min(sleepCovered, activeTotal);
        }

        /// <summary>
        /// Rebuilds timing records for every open task after a load (§2.5).
        /// Records that already exist (observed before the load) are left alone.
        /// Rebuilt records are day-resolution only: CreatedSeconds is the created
        /// day's start, LastTransition is "rebuilt-after-load", and
        /// <see cref="TaskTimingRecord.RebuiltAfterLoad"/> is set. Intra-day
        /// precision is lost across a load; that is a stated limitation, not a defect.
        /// </summary>
        public void RebuildFromAuthority(TaskAuthority authority)
        {
            if (authority == null)
            {
                return;
            }

            foreach (WorkTask task in authority.GetAllOpenTasks())
            {
                if (task == null || records.ContainsKey(task.TaskId.ToString()))
                {
                    continue;
                }

                float dayStart = task.CreatedDayIndex >= 0 ? task.CreatedDayIndex * 86400f : 0f;
                var record = new TaskTimingRecord
                {
                    TaskId = task.TaskId.ToString(),
                    DefinitionId = task.DefinitionId,
                    OwnerId = task.OwnerId,
                    CreatedSeconds = dayStart,
                    CreatedDay = task.CreatedDayIndex,
                    FirstSeenSeconds = dayStart,
                    LastTransitionKind = "rebuilt-after-load",
                    LastTransitionSeconds = dayStart,
                    LastTransitionDay = task.CreatedDayIndex,
                    LastProgressSeconds = dayStart,
                    MinutesWorkedLastSeen = task.MinutesWorked,
                    PlannedMinutesLastSeen = task.PlannedMinutes,
                    ClockPrecision = TaskTimingClockPrecision.DayOnly,
                    RebuiltAfterLoad = true,
                    CurrentStatus = task.Status,
                    StateEnteredSeconds = dayStart,
                    StateEnteredDay = task.CreatedDayIndex,
                };
                if (task.HasAssignee)
                {
                    record.AssigneeId = task.AssigneeId;
                }

                if (task.StartedDayIndex >= 0)
                {
                    record.StartedSeconds = task.StartedDayIndex * 86400f;
                    record.StartedDay = task.StartedDayIndex;
                }

                if (task.Status == TaskStatus.WaitingOnDependency)
                {
                    record.LastBlockReason = task.DependencyDescription;
                }

                if (task.Status == TaskStatus.WaitingOnPassiveProcess)
                {
                    record.LastBlockReason = task.DueEventId;
                    record.LastDueEventId = task.DueEventId;
                }

                record.CurrentBlockKey = BlockKeyFor(task);
                AddRecord(record);
            }
        }

        private void ApplyTransition(WorkTask task, string transitionKind, float nowSeconds, int nowDay)
        {
            if (task == null || string.IsNullOrEmpty(transitionKind))
            {
                return;
            }

            string key = task.TaskId.ToString();
            TaskTimingRecord record;
            bool isNew = !records.TryGetValue(key, out record);

            bool clockPresent = nowSeconds >= 0f;
            int day = nowDay >= 0
                ? nowDay
                : (!isNew && record.LastTransitionDay >= 0 ? record.LastTransitionDay : task.CreatedDayIndex);
            float now = clockPresent ? nowSeconds : day * 86400f;

            if (isNew)
            {
                record = CreateRecord(task, now, day, clockPresent);
                AddRecord(record);
                // The record may have been dropped by the store bound (full of open
                // records); in that case there is nothing to apply the transition to.
                if (!records.TryGetValue(key, out record))
                {
                    return;
                }
            }

            if (clockPresent)
            {
                record.ClockPrecision = TaskTimingClockPrecision.Full;
            }

            // Progress notifications change no state: worked minutes + last progress
            // only. On a terminal task they sync worked/planned and nothing else —
            // the completion notification already closed the record.
            if (transitionKind == "RecordWork" || transitionKind == "RecordWorkProgress")
            {
                record.MinutesWorkedLastSeen = task.MinutesWorked;
                record.PlannedMinutesLastSeen = task.PlannedMinutes;
                if (!IsTerminalStatus(task.Status))
                {
                    record.LastProgressSeconds = now;
                }

                SyncIdentity(record, task);
                return;
            }

            // Attribute the elapsed time to the bucket of the state being left.
            Accrue(record, now);

            switch (transitionKind)
            {
                case "CreateTask":
                    record.CreatedSeconds = now;
                    record.CreatedDay = day;
                    record.FirstSeenSeconds = now;
                    break;
                case "AssignTask":
                    record.AssignedSeconds = now;
                    record.AssignedDay = day;
                    break;
                case "StartTask":
                    if (record.StartedSeconds < 0f)
                    {
                        record.StartedSeconds = now;
                        record.StartedDay = day;
                    }

                    break;
                case "CompleteTask":
                    record.CompletedSeconds = now;
                    record.CompletedDay = day;
                    break;
                case "MarkWaitingOnDependency":
                    record.LastBlockReason = task.DependencyDescription;
                    break;
                case "MarkWaitingOnPassiveProcess":
                    record.LastBlockReason = task.DueEventId;
                    record.LastDueEventId = task.DueEventId;
                    break;
                case "PauseTask":
                    record.LastBlockReason = TaskTimingRecord.PausedBlockReasonKey;
                    break;
            }

            record.LastTransitionKind = transitionKind;
            record.LastTransitionSeconds = now;
            record.LastTransitionDay = day;
            record.LastProgressSeconds = now;
            record.MinutesWorkedLastSeen = task.MinutesWorked;
            record.PlannedMinutesLastSeen = task.PlannedMinutes;
            SyncIdentity(record, task);

            // Enter the new state: the task was already mutated by the authority
            // before the notification, so task.Status is the state entered.
            record.CurrentStatus = task.Status;
            record.StateEnteredSeconds = now;
            record.StateEnteredDay = day;
            record.CurrentBlockKey = BlockKeyFor(task);
        }

        private TaskTimingRecord CreateRecord(WorkTask task, float now, int day, bool clockPresent)
        {
            float createdSeconds = task.CreatedDayIndex >= 0 ? task.CreatedDayIndex * 86400f : now;
            var record = new TaskTimingRecord
            {
                TaskId = task.TaskId.ToString(),
                DefinitionId = task.DefinitionId,
                OwnerId = task.OwnerId,
                CreatedSeconds = createdSeconds,
                CreatedDay = task.CreatedDayIndex,
                FirstSeenSeconds = now,
                LastProgressSeconds = createdSeconds,
                MinutesWorkedLastSeen = task.MinutesWorked,
                PlannedMinutesLastSeen = task.PlannedMinutes,
                ClockPrecision = clockPresent ? TaskTimingClockPrecision.Full : TaskTimingClockPrecision.DayOnly,
                CurrentStatus = TaskStatus.Queued,
                // State entry is the first-seen clock time, not the day-start
                // approximation of CreatedSeconds: time before the observer was
                // installed is unknown and must not be attributed to any bucket.
                // (For a record first seen at its own CreateTask, now IS creation.)
                StateEnteredSeconds = now,
                StateEnteredDay = day,
            };
            if (task.HasAssignee)
            {
                record.AssigneeId = task.AssigneeId;
            }

            return record;
        }

        private void AddRecord(TaskTimingRecord record)
        {
            if (record == null || records.ContainsKey(record.TaskId))
            {
                return;
            }

            if (records.Count >= MaxTrackedRecords)
            {
                // Evict terminal records first, oldest first; never evict an open
                // task to make room for another record. If the store holds only
                // open records, the new observation is dropped instead.
                string evictKey = null;
                foreach (string candidateKey in recordOrder)
                {
                    TaskTimingRecord candidate;
                    if (records.TryGetValue(candidateKey, out candidate) && IsTerminalStatus(candidate.CurrentStatus))
                    {
                        evictKey = candidateKey;
                        break;
                    }
                }

                if (evictKey == null)
                {
                    return;
                }

                records.Remove(evictKey);
                recordOrder.Remove(evictKey);
                EvictedRecordCount++;
            }

            records.Add(record.TaskId, record);
            recordOrder.Add(record.TaskId);
        }

        private void Accrue(TaskTimingRecord record, float now)
        {
            if (record.StateEnteredSeconds < 0f)
            {
                return;
            }

            float elapsed = now - record.StateEnteredSeconds;
            if (elapsed <= 0f)
            {
                return;
            }

            switch (record.CurrentStatus)
            {
                case TaskStatus.Queued:
                    record.AccumulatedQueueSeconds += elapsed;
                    break;
                case TaskStatus.Assigned:
                case TaskStatus.ResourcesReserved:
                    record.AccumulatedAssignedWaitingSeconds += elapsed;
                    break;
                case TaskStatus.InProgress:
                    record.AccumulatedActiveSeconds += elapsed;
                    break;
                case TaskStatus.Paused:
                    AddBlocked(record, TaskTimingRecord.PausedBlockReasonKey, elapsed);
                    break;
                case TaskStatus.WaitingOnDependency:
                case TaskStatus.WaitingOnPassiveProcess:
                    AddBlocked(record, record.CurrentBlockKey, elapsed);
                    break;
                default:
                    // Terminal states (Complete / Failed / Cancelled / ReworkRequired)
                    // and Interrupted (never observed: InterruptTask requeues at once)
                    // accumulate into no bucket.
                    break;
            }

            // Slice 2: close the spell as an interval for Classify. Only active and
            // blocked spells are needed (L3 overlap, L1/L5 horizon caps); queue and
            // assigned-waiting time is explained by refusals, not intervals.
            TaskStatus left = record.CurrentStatus;
            if (left == TaskStatus.InProgress
                || left == TaskStatus.Paused
                || left == TaskStatus.WaitingOnDependency
                || left == TaskStatus.WaitingOnPassiveProcess)
            {
                record.ClosedSpans.Add(new TaskTimingSpan
                {
                    Status = left,
                    Key = left == TaskStatus.InProgress
                        ? string.Empty
                        : (string.IsNullOrEmpty(record.CurrentBlockKey)
                            ? TaskTimingRecord.NoReasonRecordedKey
                            : record.CurrentBlockKey),
                    StartSeconds = record.StateEnteredSeconds,
                    EndSeconds = now,
                });
            }
        }

        private static void AddBlocked(TaskTimingRecord record, string reasonKey, float elapsed)
        {
            if (string.IsNullOrEmpty(reasonKey))
            {
                reasonKey = TaskTimingRecord.NoReasonRecordedKey;
            }

            float existing;
            record.AccumulatedBlockedSecondsByReason.TryGetValue(reasonKey, out existing);
            record.AccumulatedBlockedSecondsByReason[reasonKey] = existing + elapsed;
        }

        private static string BlockKeyFor(WorkTask task)
        {
            switch (task.Status)
            {
                case TaskStatus.Paused:
                    return TaskTimingRecord.PausedBlockReasonKey;
                case TaskStatus.WaitingOnDependency:
                    return string.IsNullOrEmpty(task.DependencyDescription)
                        ? TaskTimingRecord.NoReasonRecordedKey
                        : task.DependencyDescription;
                case TaskStatus.WaitingOnPassiveProcess:
                    return string.IsNullOrEmpty(task.DueEventId)
                        ? TaskTimingRecord.NoReasonRecordedKey
                        : task.DueEventId;
                default:
                    return string.Empty;
            }
        }

        private static void SyncIdentity(TaskTimingRecord record, WorkTask task)
        {
            record.DefinitionId = task.DefinitionId;
            record.OwnerId = task.OwnerId;
            // The assignee is cleared on interruption (Requeue); keep the last
            // known assignee so the record still names the worker.
            if (task.HasAssignee)
            {
                record.AssigneeId = task.AssigneeId;
            }
        }

        private static bool IsTerminalStatus(TaskStatus status)
        {
            return status == TaskStatus.Complete
                || status == TaskStatus.Failed
                || status == TaskStatus.Cancelled
                || status == TaskStatus.ReworkRequired;
        }
    }
}
