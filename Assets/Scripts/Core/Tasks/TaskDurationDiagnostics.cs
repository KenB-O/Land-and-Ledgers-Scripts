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
    /// </summary>
    public interface ITaskTimingObserver
    {
        void OnTransition(WorkTask task, string transitionKind, float nowSeconds, int nowDay);
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
    /// Priority 4 Slice 1 — the task-duration diagnostic service (plan §5 Slice 1).
    /// Holds the timing records (bounded, §4), applies observed transitions, and
    /// rebuilds records from the authority after a load (§2.5).
    ///
    /// Slice 1 scope only: observation. No classification (Slice 2), no warnings
    /// (Slice 3), no driver wiring (Slice 4). The service is read-only with
    /// respect to every economic authority: it holds no ledger, no obligation, no
    /// inventory, and no custody, and it never calls a state-changing method on
    /// <see cref="TaskAuthority"/> (or anything else).
    ///
    /// Accumulation rule: on a status transition, the clock time elapsed since the
    /// previous transition is attributed to the bucket of the state being left
    /// (Queued → queue, Assigned/ResourcesReserved → assigned-waiting, InProgress
    /// → active, Paused/Waiting* → blocked by the reason recorded at entry).
    /// Progress notifications (RecordWork / RecordWorkProgress) change no state, so
    /// they update worked minutes and last-progress only — they never close or
    /// reopen a bucket, and they never overwrite the last transition.
    /// </summary>
    public sealed class TaskDurationDiagnostics : ITaskTimingObserver
    {
        /// <summary>DIAGNOSTIC PARAMETER — not Canon, not an authored rate (§4.2). Default bound on tracked records.</summary>
        public const int DefaultMaxTrackedRecords = 512;

        private int maxTrackedRecords = DefaultMaxTrackedRecords;
        private readonly Dictionary<string, TaskTimingRecord> records = new Dictionary<string, TaskTimingRecord>(StringComparer.Ordinal);
        private readonly List<string> recordOrder = new List<string>();

        /// <summary>DIAGNOSTIC PARAMETER — not Canon, not an authored rate (§4.2). Bounded record store (Slice 3 enforces the eviction rule).</summary>
        public int MaxTrackedRecords
        {
            get { return maxTrackedRecords; }
            set { maxTrackedRecords = Math.Max(1, value); }
        }

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

            if (records.Count >= maxTrackedRecords)
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
