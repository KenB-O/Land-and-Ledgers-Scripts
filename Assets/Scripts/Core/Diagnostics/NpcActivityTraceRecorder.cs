#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using LandLedgers.Tasks;
using LandLedgers.Primitives;
using LandLedgers.Population;
using LandLedgers.World.Journeys;

namespace LandLedgers.Diagnostics
{
    public enum NpcTraceLevel { Basic, Detailed, Deep }

    [Serializable]
    public sealed class NpcActivityTraceEvent
    {
        public int schemaVersion = 1;
        public long sequence;
        public string sessionId;
        public string eventType;
        public string transition;
        public double simulatedSeconds = -1;
        public int dayIndex = -1;
        public string personId;
        public int personNumericId = -1;
        public int householdId = -1;
        public string taskId;
        public string taskOwnerId;
        public string taskDefinitionId;
        public string taskStatus;
        public string taskOutcome;
        public string taskPriority;
        public string operationId;
        public string shoppingTripId;
        public string target;
        public string activity;
        public string movementStatus;
        public float positionX;
        public float positionY;
        public float positionZ;
        public bool hasWorldPosition;
        public float distanceRemainingMeters;
        public float estimatedArrivalSeconds;
        public int destinationBuildingId = -1;
        public int routeGeneration = -1;
        public int routeWaypointIndex = -1;
        public int routeWaypointCount;
        public string routeIdentity;
        public string routeMode;
        public string scheduleState;
        public string activityLocationId;
        public string moverState;
        public bool moverVisible;
        public bool routeActive;
        public float destinationX;
        public float destinationY;
        public float destinationZ;
        public bool hasRouteDestination;
        public string reservationId;
        public int workedMinutes = -1;
        public int plannedMinutes = -1;
        public int remainingMinutes = -1;
        public int retryCount;
        public string reason;
        public string selectionReason;
        public string recoveryState;
        public string detail;
        /// <summary>
        /// Lane 1 (M-R14) — the causal class of a refusal/interrupt carried as
        /// a structured field (the <see cref="RefusalCauseClass"/> name, or
        /// "Unclassified"). The recorder is the capture layer: it records the
        /// tag the refusing authority supplied; classification lives in the
        /// incident core above it.
        /// </summary>
        public string refusalCauseClass;
    }

    [Serializable]
    public sealed class NpcActivityTraceAnomaly
    {
        public string anomalyId;
        public string category;
        public string taskId;
        public string personId;
        public int householdId = -1;
        public double startedAtSeconds;
        public double detectedAtSeconds;
        public double lastProgressSeconds;
        public string reason;
        public string supportingState;
        public string recoveryState;
    }

    [Serializable]
    public sealed class NpcActivityTraceManifest
    {
        public int schemaVersion = 1;
        public string sessionId;
        public string sourceBranch;
        public string sourceRevision;
        public string scenario;
        public int seed = -1;
        public string selectedSpeed;
        public string traceLevel;
        public double samplingIntervalSimulatedSeconds;
        public double startedAtSimulatedSeconds;
        public int retainedEvents;
        public int anomalyCount;
        public int droppedSessionEvents;
        public int droppedHistoryEvents;
        public int droppedAnomalies;
    }

    /// <summary>Bounded, observational task trace. It never calls back into task/gameplay authorities.</summary>
    public sealed class NpcActivityTraceRecorder : ITaskTimingObserver
    {
        private readonly Func<float> clock;
        private readonly Func<int, int> householdForPerson;
        private readonly List<NpcActivityTraceEvent> history = new List<NpcActivityTraceEvent>();
        private readonly List<NpcActivityTraceEvent> sessionEvents = new List<NpcActivityTraceEvent>();
        private readonly List<NpcActivityTraceAnomaly> anomalies = new List<NpcActivityTraceAnomaly>();
        private readonly HashSet<long> sessionEventSequences = new HashSet<long>();
        private readonly Dictionary<string, WorkTask> knownTasks = new Dictionary<string, WorkTask>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> lastObservedSeconds = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> firstObservedSeconds = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> inProgressStartedAtSeconds = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<int, double> latestTaskDirectedArrivalSeconds = new Dictionary<int, double>();
        private readonly Dictionary<int, string> taskDirectedRouteIdentityByPerson = new Dictionary<int, string>();
        private readonly Dictionary<int, WorkTask> taskDirectedRouteTaskByPerson = new Dictionary<int, WorkTask>();
        private readonly HashSet<string> openAtLastSweep = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> terminalObserved = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> emittedAnomalyKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<int> selectedPeople = new HashSet<int>();
        private readonly HashSet<int> selectedHouseholds = new HashSet<int>();
        private readonly Dictionary<string, MovementProgressState> movementProgress = new Dictionary<string, MovementProgressState>(StringComparer.Ordinal);
        private readonly HashSet<string> emittedRouteAnomalyKeys = new HashSet<string>(StringComparer.Ordinal);
        private long sequence;
        private long sessionDroppedThroughSequence;
        private int historyLimit = 5000;
        private int retentionLimit = 20000;
        private int anomalyLimit = 2000;
        private int droppedHistoryEvents;
        private int droppedSessionEvents;
        private int droppedAnomalies;
        private int currentDay = -1;
        private double nextDeepSampleSeconds;
        private double sampleIntervalSeconds = 300;
        private double prehistoryDurationSeconds = -1;

        public NpcActivityTraceRecorder(Func<float> clock, Func<int, int> householdForPerson)
        {
            this.clock = clock;
            this.householdForPerson = householdForPerson;
        }

        public bool Monitoring { get; private set; }
        public bool Recording { get; private set; }
        public bool AutoStallDetection { get; set; } = true;
        public bool AutoSelectAnomalies { get; set; } = true;
        public NpcTraceLevel Level { get; private set; } = NpcTraceLevel.Basic;
        public string SessionId { get; private set; } = string.Empty;
        public string SourceBranch { get; set; } = "NOT_CAPTURED";
        public string SourceRevision { get; set; } = "NOT_CAPTURED";
        public string Scenario { get; set; } = "NOT_CAPTURED";
        public int Seed { get; set; } = -1;
        public string SelectedSpeed { get; set; } = "NOT_CAPTURED";
        public string SelectionReason { get; private set; } = "developer selection";
        public int HistoryLimit { get => historyLimit; set { historyLimit = Math.Max(1, value); Trim(history, historyLimit, ref droppedHistoryEvents); } }
        public int RetentionLimit { get => retentionLimit; set { retentionLimit = Math.Max(1, value); Trim(sessionEvents, retentionLimit, ref droppedSessionEvents); } }
        public int AnomalyLimit { get => anomalyLimit; set { anomalyLimit = Math.Max(1, value); Trim(anomalies, anomalyLimit, ref droppedAnomalies); } }
        public int DroppedHistoryEvents => droppedHistoryEvents;
        public int DroppedSessionEvents => droppedSessionEvents;
        public int DroppedAnomalies => droppedAnomalies;
        public double SampleIntervalSeconds { get => sampleIntervalSeconds; set => sampleIntervalSeconds = Math.Max(1d, value); }
        public double PrehistoryDurationSeconds { get => prehistoryDurationSeconds; set => prehistoryDurationSeconds = value < 0 ? -1 : value; }
        public IReadOnlyList<NpcActivityTraceEvent> Events => sessionEvents;
        public IReadOnlyList<NpcActivityTraceAnomaly> Anomalies => anomalies;
        public IReadOnlyCollection<int> SelectedPeople => selectedPeople;
        public IReadOnlyCollection<int> SelectedHouseholds => selectedHouseholds;

        public void BeginMonitoring()
        {
            Monitoring = true;
        }

        public void SetSelection(IEnumerable<int> people, IEnumerable<int> households, string reason = "developer selection")
        {
            selectedPeople.Clear();
            selectedHouseholds.Clear();
            if (people != null) foreach (int id in people) if (id >= 0) selectedPeople.Add(id);
            if (households != null) foreach (int id in households) if (id >= 0) selectedHouseholds.Add(id);
            SelectionReason = string.IsNullOrWhiteSpace(reason) ? "developer selection" : reason;
        }

        public void StartRecording(NpcTraceLevel level, bool includePrehistory = true)
        {
            Monitoring = true;
            Recording = true;
            Level = level;
            SessionId = Guid.NewGuid().ToString("N");
            sessionEvents.Clear();
            sessionEventSequences.Clear();
            sessionDroppedThroughSequence = 0;
            anomalies.Clear();
            emittedAnomalyKeys.Clear();
            droppedSessionEvents = 0;
            droppedAnomalies = 0;
            currentDay = -1;
            nextDeepSampleSeconds = Now();
            if (includePrehistory)
            {
                double now = Now();
                for (int i = 0; i < history.Count; i++)
                    if (Matches(history[i]) && (prehistoryDurationSeconds < 0 || now < 0 || now - history[i].simulatedSeconds <= prehistoryDurationSeconds))
                        AppendSession(CloneForSession(history[i]));
            }
            RecordMarker("session-start", "Trace recording started", -1);
        }

        public void StopRecording(string reason = "developer stopped recording")
        {
            if (!Recording) return;
            RecordMarker("session-stop", reason, currentDay);
            Recording = false;
        }

        public void PauseRecording(bool paused)
        {
            if (!Monitoring) return;
            RecordMarker(paused ? "recording-paused" : "recording-resumed", "", currentDay);
            if (paused) Recording = false;
            else Recording = true;
        }

        public void SetCurrentDay(int dayIndex) { currentDay = dayIndex; }

        public void OnTransition(WorkTask task, string transitionKind, float nowSeconds, int nowDay)
        {
            if (!Monitoring || task == null) return;
            string taskId = task.TaskId.ToString();
            knownTasks[taskId] = task;
            double now = nowSeconds >= 0 ? nowSeconds : Now();
            lastObservedSeconds[taskId] = now;
            if (!firstObservedSeconds.ContainsKey(taskId)) firstObservedSeconds[taskId] = now;
            NpcActivityTraceEvent item = FromTask(task, transitionKind, now, nowDay);
            item.eventType = "task-transition";
            AppendObserved(item);
            if (Recording && Matches(item)) PromoteTaskHistory(taskId);
            if (task.Status == TaskStatus.InProgress && !inProgressStartedAtSeconds.ContainsKey(taskId))
                inProgressStartedAtSeconds[taskId] = now;
            if (task.Status == TaskStatus.Complete || task.Status == TaskStatus.Failed || task.Status == TaskStatus.Cancelled)
            {
                terminalObserved.Add(taskId);
                if (task.Status == TaskStatus.Complete && task.Outcome == TaskOutcome.Success && task.MinutesRemaining > 0)
                {
                    if (string.Equals(task.DefinitionId, JourneyTravel.TravelTaskId, StringComparison.Ordinal))
                    {
                        // Travel work is evidenced by physical arrival rather
                        // than credited labor minutes. At Detailed/Deep levels,
                        // require a task-directed route arrival after this task
                        // began; otherwise expose success without movement.
                        if (Level >= NpcTraceLevel.Detailed
                            && !HasTaskDirectedArrivalDuringTask(task, taskId, now))
                        {
                            AddAnomaly("travel-task-succeeded-without-arrival", task, now, lastObservedSeconds[taskId],
                                "Travel task succeeded with planned minutes remaining and no task-directed physical arrival was observed.", item.taskStatus);
                        }
                    }
                    else
                    {
                        AddAnomaly("success-before-planned-work", task, now, lastObservedSeconds[taskId],
                            "Task succeeded with planned minutes still remaining.", item.taskStatus);
                    }
                }
                inProgressStartedAtSeconds.Remove(taskId);
            }
        }

        /// <summary>
        /// Records an assign/start refusal for a task. The optional
        /// <paramref name="causeClass"/> tags the refusal with its causal
        /// class (M-R14) as a structured field; when the caller does not know
        /// it, the tag stays Unclassified and the reason string is the only
        /// evidence. A refusal is not progress: it never moves last-progress.
        /// </summary>
        public void RecordRefusal(string taskId, string method, string reason, int day,
            RefusalCauseClass causeClass = RefusalCauseClass.Unclassified)
        {
            if (!Monitoring || Level < NpcTraceLevel.Detailed) return;
            knownTasks.TryGetValue(taskId ?? string.Empty, out WorkTask task);
            NpcActivityTraceEvent item = task != null ? FromTask(task, method, Now(), day) :
                new NpcActivityTraceEvent { taskId = taskId ?? string.Empty, transition = method, simulatedSeconds = Now(), dayIndex = day };
            item.eventType = "task-refusal";
            item.reason = reason ?? string.Empty;
            // Merge of Lane 1 + Lane 2 (M-R14): an explicit class supplied by
            // the refusing authority wins; otherwise classify the reason
            // string with the core classifier. (Lane 2's Investigator keeps
            // its own DestinationBlockerKind classification internally; the
            // two enums share the M-R14 classes 1-6 by design.)
            item.refusalCauseClass = (causeClass != RefusalCauseClass.Unclassified
                ? causeClass
                : RefusalCauseClassifier.Classify(reason)).ToString();
            AppendObserved(item);
        }

        public void RecordNoWorker(string taskId, string reason, int day)
        {
            if (!Monitoring || Level < NpcTraceLevel.Detailed) return;
            knownTasks.TryGetValue(taskId ?? string.Empty, out WorkTask task);
            NpcActivityTraceEvent item = task != null ? FromTask(task, "no-worker", Now(), day) :
                new NpcActivityTraceEvent { taskId = taskId ?? string.Empty, transition = "no-worker", simulatedSeconds = Now(), dayIndex = day };
            item.eventType = "worker-unavailable";
            item.reason = reason ?? string.Empty;
            AppendObserved(item);
        }

        /// <summary>Receives an authoritative event emitted by HouseholdShoppingLoop.</summary>
        public void RecordShoppingEvent(ShoppingDiagnosticEvent shoppingEvent)
        {
            if (!Monitoring || shoppingEvent == null) return;
            string tripId = shoppingEvent.TripId ?? string.Empty;
            string operationId = !string.IsNullOrEmpty(tripId) ? tripId :
                "shopping:H" + shoppingEvent.HouseholdId + ":need:" + shoppingEvent.NeedSequence;
            var item = new NpcActivityTraceEvent
            {
                eventType = "shopping",
                transition = shoppingEvent.Kind.ToString(),
                simulatedSeconds = Now(),
                dayIndex = shoppingEvent.DayIndex,
                personId = shoppingEvent.PersonId >= 0 ? "P" + shoppingEvent.PersonId : string.Empty,
                personNumericId = shoppingEvent.PersonId,
                householdId = shoppingEvent.HouseholdId,
                operationId = operationId,
                shoppingTripId = tripId,
                target = shoppingEvent.Summary ?? string.Empty,
                detail = Level >= NpcTraceLevel.Detailed ? shoppingEvent.Details ?? string.Empty : string.Empty,
                selectionReason = SelectionReason
            };
            AppendObserved(item);
        }

        public void RecordMovement(int personId, int dayIndex, double seconds, string transition, string status,
            string destination, float remainingDistance, string routeId = null, string reason = null,
            int destinationBuildingId = -1, int routeGeneration = -1, string routeMode = null,
            string scheduleState = null, string moverState = null, Vector3? routeDestination = null,
            string activityLocationId = null, Vector3? worldPosition = null,
            bool moverVisible = false, bool routeActive = false, WorkTask associatedTask = null,
            string shoppingTripId = null)
        {
            if (!Monitoring || Level < NpcTraceLevel.Detailed) return;
            bool taskDirected = string.Equals(routeMode, "TaskDirected", StringComparison.OrdinalIgnoreCase);
            string routeIdentity = routeId ?? string.Empty;
            if (string.Equals(transition, "started", StringComparison.OrdinalIgnoreCase))
            {
                taskDirectedRouteIdentityByPerson.Remove(personId);
                taskDirectedRouteTaskByPerson.Remove(personId);
                if (taskDirected && !string.IsNullOrWhiteSpace(routeIdentity) && associatedTask != null)
                {
                    taskDirectedRouteIdentityByPerson[personId] = routeIdentity;
                    taskDirectedRouteTaskByPerson[personId] = associatedTask;
                }
            }
            else if (associatedTask == null && taskDirected
                && taskDirectedRouteIdentityByPerson.TryGetValue(personId, out string ownedRouteIdentity)
                && string.Equals(ownedRouteIdentity, routeIdentity, StringComparison.Ordinal)
                && taskDirectedRouteTaskByPerson.TryGetValue(personId, out WorkTask routeTask))
            {
                // Keep the originating task attached even if it reaches a terminal
                // state before the mover's arrival callback is recorded.
                associatedTask = routeTask;
            }
            var item = new NpcActivityTraceEvent
            {
                eventType = "movement",
                transition = transition ?? string.Empty,
                simulatedSeconds = seconds,
                dayIndex = dayIndex,
                personId = "P" + personId,
                personNumericId = personId,
                householdId = Household(personId),
                target = destination ?? string.Empty,
                detail = "status=" + (status ?? string.Empty) + "; remainingMeters=" + remainingDistance.ToString("0.##") + "; route=" + (routeId ?? string.Empty),
                reason = reason ?? string.Empty,
                destinationBuildingId = destinationBuildingId,
                routeGeneration = routeGeneration,
                routeIdentity = routeId ?? string.Empty,
                routeMode = routeMode ?? string.Empty,
                scheduleState = scheduleState ?? string.Empty,
                activityLocationId = activityLocationId ?? string.Empty,
                moverState = moverState ?? string.Empty,
                movementStatus = status ?? string.Empty,
                distanceRemainingMeters = remainingDistance,
                hasWorldPosition = worldPosition.HasValue,
                positionX = worldPosition.HasValue ? worldPosition.Value.x : 0f,
                positionY = worldPosition.HasValue ? worldPosition.Value.y : 0f,
                positionZ = worldPosition.HasValue ? worldPosition.Value.z : 0f,
                moverVisible = moverVisible,
                routeActive = routeActive,
                hasRouteDestination = routeDestination.HasValue,
                destinationX = routeDestination.HasValue ? routeDestination.Value.x : 0f,
                destinationY = routeDestination.HasValue ? routeDestination.Value.y : 0f,
                destinationZ = routeDestination.HasValue ? routeDestination.Value.z : 0f,
                selectionReason = SelectionReason
            };
            if (!string.IsNullOrWhiteSpace(shoppingTripId))
            {
                item.shoppingTripId = shoppingTripId;
                if (string.IsNullOrWhiteSpace(item.operationId)) item.operationId = shoppingTripId;
            }
            if (associatedTask != null)
            {
                NpcActivityTraceEvent taskContext = FromTask(associatedTask, transition, seconds, dayIndex);
                item.taskId = taskContext.taskId;
                item.taskOwnerId = taskContext.taskOwnerId;
                item.taskDefinitionId = taskContext.taskDefinitionId;
                item.taskStatus = taskContext.taskStatus;
                item.taskOutcome = taskContext.taskOutcome;
                item.taskPriority = taskContext.taskPriority;
                item.operationId = taskContext.operationId;
                item.reservationId = taskContext.reservationId;
                item.workedMinutes = taskContext.workedMinutes;
                item.plannedMinutes = taskContext.plannedMinutes;
                item.remainingMinutes = taskContext.remainingMinutes;
                item.retryCount = taskContext.retryCount;
            }
            if (string.Equals(transition, "started", StringComparison.OrdinalIgnoreCase))
                latestTaskDirectedArrivalSeconds.Remove(personId);
            else if (string.Equals(transition, "arrived", StringComparison.OrdinalIgnoreCase) && taskDirected)
            {
                latestTaskDirectedArrivalSeconds[personId] = seconds;
                taskDirectedRouteIdentityByPerson.Remove(personId);
                taskDirectedRouteTaskByPerson.Remove(personId);
            }
            else if (string.Equals(transition, "refused", StringComparison.OrdinalIgnoreCase))
            {
                taskDirectedRouteIdentityByPerson.Remove(personId);
                taskDirectedRouteTaskByPerson.Remove(personId);
            }
            AppendObserved(item);
        }

        public void RecordPersonSnapshot(int personId, int householdId, int dayIndex, double seconds,
            string activity, int destinationBuildingId, Vector3? worldPosition, string movementStatus,
            float remainingDistance, float estimatedArrival, string taskSummary,
            string routeIdentity = null, int routeGeneration = -1, int routeWaypointIndex = -1,
            int routeWaypointCount = 0, Vector3? routeDestination = null, string routeMode = null,
            string scheduleState = null, string activityLocationId = null, bool moverVisible = false,
            bool routeActive = false, string moverState = null)
        {
            if (!Monitoring || Level < NpcTraceLevel.Detailed) return;
            bool includePhysicalPosition = Level == NpcTraceLevel.Deep;
            var item = new NpcActivityTraceEvent
            {
                eventType = includePhysicalPosition ? "person-snapshot" : "movement-snapshot",
                transition = "simulated-time-sample",
                simulatedSeconds = seconds,
                dayIndex = dayIndex,
                personId = "P" + personId,
                personNumericId = personId,
                householdId = householdId,
                activity = activity ?? string.Empty,
                destinationBuildingId = destinationBuildingId,
                hasWorldPosition = includePhysicalPosition && worldPosition.HasValue,
                positionX = includePhysicalPosition && worldPosition.HasValue ? worldPosition.Value.x : 0f,
                positionY = includePhysicalPosition && worldPosition.HasValue ? worldPosition.Value.y : 0f,
                positionZ = includePhysicalPosition && worldPosition.HasValue ? worldPosition.Value.z : 0f,
                movementStatus = movementStatus ?? string.Empty,
                distanceRemainingMeters = remainingDistance,
                estimatedArrivalSeconds = estimatedArrival,
                routeIdentity = routeIdentity ?? string.Empty,
                routeGeneration = routeGeneration,
                routeWaypointIndex = routeWaypointIndex,
                routeWaypointCount = routeWaypointCount,
                routeMode = routeMode ?? string.Empty,
                scheduleState = scheduleState ?? string.Empty,
                activityLocationId = activityLocationId ?? string.Empty,
                moverVisible = moverVisible,
                routeActive = routeActive,
                moverState = moverState ?? string.Empty,
                hasRouteDestination = routeActive && routeDestination.HasValue,
                destinationX = routeActive && routeDestination.HasValue ? routeDestination.Value.x : 0f,
                destinationY = routeActive && routeDestination.HasValue ? routeDestination.Value.y : 0f,
                destinationZ = routeActive && routeDestination.HasValue ? routeDestination.Value.z : 0f,
                detail = taskSummary ?? string.Empty,
                selectionReason = SelectionReason
            };
            AppendObserved(item);
            ObserveMovementProgress(item);
        }

        public void RecordMarker(string eventType, string detail, int dayIndex)
        {
            if (!Monitoring) return;
            var item = new NpcActivityTraceEvent { eventType = eventType, detail = detail ?? string.Empty, simulatedSeconds = Now(), dayIndex = dayIndex, sessionId = SessionId };
            AppendObserved(item);
        }

        public bool SampleDeep(TaskAuthority authority, double simulatedSeconds, int dayIndex,
            Func<int, string> movementReadout = null, Func<string, string> taskRecoveryReadout = null)
        {
            if (!Monitoring || simulatedSeconds < nextDeepSampleSeconds) return false;
            nextDeepSampleSeconds = simulatedSeconds + sampleIntervalSeconds;
            if (authority == null) return false;
            List<WorkTask> tasks = authority.GetAllOpenTasks();
            var currentOpen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < tasks.Count; i++)
            {
                WorkTask task = tasks[i];
                if (task == null) continue;
                knownTasks[task.TaskId.ToString()] = task;
                string id = task.TaskId.ToString();
                double now = simulatedSeconds;
                if (!firstObservedSeconds.ContainsKey(id)) firstObservedSeconds[id] = now;
                double last = lastObservedSeconds.TryGetValue(id, out double lastSeen) ? lastSeen : firstObservedSeconds[id];
                if (Level == NpcTraceLevel.Deep)
                {
                    NpcActivityTraceEvent item = FromTask(task, "deep-snapshot", now, dayIndex);
                    item.eventType = "task-snapshot";
                    if (task.HasAssignee && movementReadout != null)
                        item.detail = movementReadout(task.AssigneeId.Id) ?? string.Empty;
                    if (taskRecoveryReadout != null)
                        item.recoveryState = taskRecoveryReadout(id) ?? string.Empty;
                    AppendObserved(item);
                }
                currentOpen.Add(id);
                if (AutoStallDetection) DetectStall(task, now, last,
                    taskRecoveryReadout != null ? taskRecoveryReadout(id) : string.Empty);
            }

            var missing = new List<string>();
            foreach (string id in openAtLastSweep)
                if (!currentOpen.Contains(id) && authority.FindTask(id) == null && !terminalObserved.Contains(id)) missing.Add(id);
            for (int i = 0; i < missing.Count; i++)
                AddAnomaly("task-disappeared-without-terminal", knownTasks.TryGetValue(missing[i], out WorkTask old) ? old : null,
                    simulatedSeconds, lastObservedSeconds.TryGetValue(missing[i], out double progress) ? progress : simulatedSeconds,
                    "A previously observed open task is absent from TaskAuthority without a terminal event.", missing[i]);
            openAtLastSweep.Clear();
            foreach (string id in currentOpen) openAtLastSweep.Add(id);
            return true;
        }

        public void RecordAuthorityRestored(int dayIndex)
        {
            RecordMarker("authority-reattached", "TaskAuthority reattached after Save/Continue; durable ids are re-resolved.", dayIndex);
            openAtLastSweep.Clear();
            terminalObserved.Clear();
        }

        /// <summary>
        /// Lane 2: bounded pre-incident capture for one task. Returns the
        /// retained history rows for the task inside [now - windowSeconds,
        /// now], oldest first, capped at 500 rows so an investigation can
        /// never pull an unbounded slice of the trace. The recorder's
        /// retention limits and drop counters still bound the underlying
        /// history; this method reports only what survived.
        /// </summary>
        public IReadOnlyList<NpcActivityTraceEvent> CapturePreIncident(string taskId, double windowSeconds, double nowSeconds)
        {
            var result = new List<NpcActivityTraceEvent>();
            if (string.IsNullOrWhiteSpace(taskId)) return result;
            double window = windowSeconds < 0 ? -1 : windowSeconds;
            for (int i = 0; i < history.Count; i++)
            {
                NpcActivityTraceEvent item = history[i];
                if (item == null || !string.Equals(item.taskId, taskId, StringComparison.Ordinal)) continue;
                if (window >= 0 && item.simulatedSeconds >= 0 && nowSeconds >= 0 && nowSeconds - item.simulatedSeconds > window) continue;
                if (item.simulatedSeconds >= 0 && nowSeconds >= 0 && item.simulatedSeconds > nowSeconds) continue;
                result.Add(item);
                if (result.Count >= 500) break;
            }
            return result;
        }

        /// <summary>
        /// Lane 2: investigate one task from its retained trace evidence.
        /// Maps the recorder's events onto the investigator's plain input
        /// and returns the classified finding (authority, causal timeline,
        /// earliest confirmed blocker, circumstance severity). Observation
        /// only: assigns no work and changes no simulation state.
        /// </summary>
        public IncidentFinding InvestigateTask(string taskId, double nowSeconds)
        {
            if (string.IsNullOrWhiteSpace(taskId)) return IncidentInvestigator.InvestigateTaskStall(null, nowSeconds);
            IReadOnlyList<NpcActivityTraceEvent> captured = CapturePreIncident(taskId, -1, nowSeconds);
            var evidence = new List<IncidentEvidenceEvent>(captured.Count);
            for (int i = 0; i < captured.Count; i++) evidence.Add(ToEvidence(captured[i]));
            if (evidence.Count == 0)
            {
                // No retained rows: still return a finding so the window can
                // show UNKNOWN with the missing evidence named, instead of
                // an empty panel.
                evidence.Add(new IncidentEvidenceEvent { TaskId = taskId, SimulatedSeconds = -1 });
            }
            return IncidentInvestigator.InvestigateTaskStall(evidence, nowSeconds);
        }

        private static IncidentEvidenceEvent ToEvidence(NpcActivityTraceEvent item)
        {
            if (item == null) return new IncidentEvidenceEvent();
            return new IncidentEvidenceEvent
            {
                SimulatedSeconds = item.simulatedSeconds,
                DayIndex = item.dayIndex,
                EventType = item.eventType ?? string.Empty,
                Transition = item.transition ?? string.Empty,
                TaskId = item.taskId ?? string.Empty,
                TaskDefinitionId = item.taskDefinitionId ?? string.Empty,
                TaskStatus = item.taskStatus ?? string.Empty,
                TaskOwnerId = item.taskOwnerId ?? string.Empty,
                PersonId = item.personId ?? string.Empty,
                PersonNumericId = item.personNumericId,
                HouseholdId = item.householdId,
                Reason = item.reason ?? string.Empty,
                Detail = item.detail ?? string.Empty,
                RecoveryState = item.recoveryState ?? string.Empty,
                ActivityLocationId = item.activityLocationId ?? string.Empty,
                ReservationId = item.reservationId ?? string.Empty,
                WorkedMinutes = item.workedMinutes,
                RemainingMinutes = item.remainingMinutes,
                PlannedMinutes = item.plannedMinutes,
                InterruptionCount = item.retryCount,
            };
        }

        public void WriteJsonLines(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Export path is required.", nameof(path));
            using (var writer = new StreamWriter(path, false))
            {
                writer.WriteLine(JsonUtility.ToJson(new NpcActivityTraceManifest
                {
                    sessionId = SessionId,
                    sourceBranch = SourceBranch,
                    sourceRevision = SourceRevision,
                    scenario = Scenario,
                    seed = Seed,
                    selectedSpeed = SelectedSpeed,
                    traceLevel = Level.ToString(),
                    samplingIntervalSimulatedSeconds = sampleIntervalSeconds,
                    startedAtSimulatedSeconds = sessionEvents.Count > 0 ? sessionEvents[0].simulatedSeconds : -1,
                    retainedEvents = sessionEvents.Count,
                    anomalyCount = anomalies.Count,
                    droppedSessionEvents = droppedSessionEvents,
                    droppedHistoryEvents = droppedHistoryEvents,
                    droppedAnomalies = droppedAnomalies
                }));
                for (int i = 0; i < sessionEvents.Count; i++) writer.WriteLine(JsonUtility.ToJson(sessionEvents[i]));
            }
        }

        public void WriteAnomalyReport(string path)
        {
            using (var writer = new StreamWriter(path, false))
            {
                writer.WriteLine("NPC Activity Trace Anomalies " + SessionId);
                writer.WriteLine("Detected: " + anomalies.Count + "; dropped session events: " + droppedSessionEvents);
                for (int i = 0; i < anomalies.Count; i++)
                {
                    NpcActivityTraceAnomaly anomaly = anomalies[i];
                    writer.WriteLine(anomaly.anomalyId + " | " + anomaly.category + " | task=" + anomaly.taskId +
                        " person=" + anomaly.personId + " household=" + anomaly.householdId + " detected=" +
                        anomaly.detectedAtSeconds.ToString("0.###") + "s | " + anomaly.reason + " | " + anomaly.supportingState);
                }
            }
        }

        public void WriteReadableReport(string path)
        {
            using (var writer = new StreamWriter(path, false))
            {
                writer.WriteLine("NPC Activity Trace " + SessionId);
                writer.WriteLine("Events: " + sessionEvents.Count + "; anomalies: " + anomalies.Count +
                    "; dropped session/history: " + droppedSessionEvents + "/" + droppedHistoryEvents);
                for (int i = 0; i < sessionEvents.Count; i++)
                {
                    NpcActivityTraceEvent e = sessionEvents[i];
                    writer.WriteLine(e.simulatedSeconds.ToString("0.###") + "s day=" + e.dayIndex + " " + e.eventType +
                        " " + e.transition + " person=" + e.personId + " household=" + e.householdId + " task=" + e.taskId +
                        " status=" + e.taskStatus + (string.IsNullOrEmpty(e.reason) ? string.Empty : " reason=" + e.reason));
                }
                writer.WriteLine("Anomalies:");
                for (int i = 0; i < anomalies.Count; i++) writer.WriteLine(anomalies[i].anomalyId + " " + anomalies[i].category + " " + anomalies[i].reason);
            }
        }

        private bool HasTaskDirectedArrivalDuringTask(WorkTask task, string taskId, double now)
        {
            if (task == null || !task.HasAssignee || task.AssigneeId.Kind != EntityKind.Person
                || !inProgressStartedAtSeconds.TryGetValue(taskId, out double startedAt)
                || !latestTaskDirectedArrivalSeconds.TryGetValue(task.AssigneeId.Id, out double arrivedAt))
                return false;

            const double boundaryToleranceSeconds = 0.05d;
            return arrivedAt + boundaryToleranceSeconds >= startedAt
                && arrivedAt <= now + boundaryToleranceSeconds;
        }

        private void DetectStall(WorkTask task, double now, double lastProgress, string recoveryState)
        {
            double stale = Math.Max(0, now - lastProgress);
            double threshold = task.Status == TaskStatus.InProgress ? 6 * 3600d :
                task.Status == TaskStatus.Assigned || task.Status == TaskStatus.ResourcesReserved ? 12 * 3600d :
                task.Status == TaskStatus.WaitingOnDependency || task.Status == TaskStatus.WaitingOnPassiveProcess ? 48 * 3600d : 72 * 3600d;
            if (stale < threshold) return;
            bool hasRecovery = !string.IsNullOrWhiteSpace(recoveryState);
            AddAnomaly(hasRecovery ? "task-no-progress-with-recovery" : "task-no-progress", task, now, lastProgress,
                "Open " + task.Status + " task has had no recorded lifecycle/work progress for " + (stale / 3600d).ToString("0.0") + " simulated hours."
                    + (hasRecovery ? " Owning system recovery: " + recoveryState : string.Empty),
                "worked=" + task.MinutesWorked + "; remaining=" + task.MinutesRemaining + "; reservation=" + task.ReservationHandleId,
                recoveryState);
        }

        private void AddAnomaly(string category, WorkTask task, double now, double progress, string reason, string state,
            string recoveryState = null)
        {
            string taskId = task == null ? string.Empty : task.TaskId.ToString();
            string key = category + "|" + taskId;
            if (!emittedAnomalyKeys.Add(key)) return;
            int person = task != null && task.HasAssignee ? task.AssigneeId.Id : -1;
            var anomaly = new NpcActivityTraceAnomaly
            {
                anomalyId = key,
                category = category,
                taskId = taskId,
                personId = person >= 0 ? "P" + person : string.Empty,
                householdId = person >= 0 ? Household(person) : -1,
                startedAtSeconds = firstObservedSeconds.TryGetValue(taskId, out double started) ? started : progress,
                detectedAtSeconds = now,
                lastProgressSeconds = progress,
                reason = reason ?? string.Empty,
                supportingState = state ?? string.Empty,
                recoveryState = recoveryState ?? string.Empty
            };
            anomalies.Add(anomaly);
            Trim(anomalies, anomalyLimit, ref droppedAnomalies);
            var item = new NpcActivityTraceEvent
            {
                eventType = "anomaly",
                transition = category,
                sessionId = SessionId,
                simulatedSeconds = now,
                dayIndex = currentDay,
                personId = anomaly.personId,
                personNumericId = person,
                householdId = anomaly.householdId,
                taskId = taskId,
                reason = anomaly.reason,
                recoveryState = anomaly.recoveryState,
                detail = anomaly.supportingState,
                // Merge of Lane 1 + Lane 2 (M-R14): tag the anomaly with its
                // causal class, classified from the reason + recovery strings.
                refusalCauseClass = RefusalCauseClassifier.Classify(
                    (anomaly.reason ?? string.Empty) + " " + (anomaly.recoveryState ?? string.Empty)).ToString(),
                selectionReason = "automatic anomaly selection: " + category
            };
            if (AutoSelectAnomalies && person >= 0)
            {
                selectedPeople.Add(person);
                SelectionReason = item.selectionReason;
                if (Level < NpcTraceLevel.Detailed) Level = NpcTraceLevel.Detailed;
                PromoteSelectedHistory();
                if (task != null) PromoteTaskHistory(taskId);
            }
            AppendObserved(item);
        }

        private void ObserveMovementProgress(NpcActivityTraceEvent item)
        {
            if (!AutoStallDetection || item == null || item.personNumericId < 0) return;
            string personKey = item.personNumericId.ToString();
            if (!item.routeActive || string.IsNullOrEmpty(item.routeIdentity))
            {
                movementProgress.Remove(personKey);
                return;
            }

            if (!movementProgress.TryGetValue(personKey, out MovementProgressState progress)
                || !string.Equals(progress.routeIdentity, item.routeIdentity, StringComparison.Ordinal))
            {
                movementProgress[personKey] = new MovementProgressState
                {
                    routeIdentity = item.routeIdentity,
                    startedAt = item.simulatedSeconds,
                    lastProgressAt = item.simulatedSeconds,
                    lastDistance = item.distanceRemainingMeters
                };
                return;
            }

            if (item.distanceRemainingMeters + 0.25f < progress.lastDistance)
            {
                progress.lastDistance = item.distanceRemainingMeters;
                progress.lastProgressAt = item.simulatedSeconds;
                progress.anomalyEmitted = false;
            }
            else if (!progress.anomalyEmitted && item.simulatedSeconds - progress.lastProgressAt >= 30 * 60d)
            {
                progress.anomalyEmitted = true;
                AddMovementAnomaly(item, progress,
                    "The same physical route remained active for 30 simulated minutes without measurable distance reduction.");
            }
        }

        private void AddMovementAnomaly(NpcActivityTraceEvent sample, MovementProgressState progress, string reason)
        {
            var anomaly = new NpcActivityTraceAnomaly
            {
                anomalyId = "route-no-progress|" + sample.routeIdentity,
                category = "route-no-progress",
                taskId = sample.taskId ?? string.Empty,
                personId = sample.personId ?? string.Empty,
                householdId = sample.householdId,
                startedAtSeconds = progress.startedAt,
                detectedAtSeconds = sample.simulatedSeconds,
                lastProgressSeconds = progress.lastProgressAt,
                reason = reason,
                supportingState = "route=" + sample.routeIdentity + "; status=" + sample.movementStatus
                    + "; mover=" + sample.moverState + "; remainingMeters=" + sample.distanceRemainingMeters.ToString("0.##")
                    + "; waypoint=" + sample.routeWaypointIndex + "/" + sample.routeWaypointCount
            };
            anomalies.Add(anomaly);
            Trim(anomalies, anomalyLimit, ref droppedAnomalies);
            var item = new NpcActivityTraceEvent
            {
                eventType = "anomaly",
                transition = anomaly.category,
                simulatedSeconds = anomaly.detectedAtSeconds,
                dayIndex = sample.dayIndex,
                personId = sample.personId,
                personNumericId = sample.personNumericId,
                householdId = sample.householdId,
                taskId = sample.taskId,
                operationId = sample.routeIdentity,
                reason = reason,
                detail = anomaly.supportingState,
                selectionReason = "automatic anomaly selection: " + anomaly.category
            };
            if (AutoSelectAnomalies)
            {
                selectedPeople.Add(sample.personNumericId);
                SelectionReason = item.selectionReason;
                if (Level < NpcTraceLevel.Detailed) Level = NpcTraceLevel.Detailed;
                PromoteSelectedHistory();
            }
            AppendObserved(item);
        }

        private sealed class MovementProgressState
        {
            public string routeIdentity;
            public double startedAt;
            public double lastProgressAt;
            public float lastDistance;
            public bool anomalyEmitted;
        }

        private void PromoteSelectedHistory()
        {
            double now = Now();
            for (int i = 0; i < history.Count; i++)
            {
                NpcActivityTraceEvent prior = history[i];
                if (Matches(prior) && (prehistoryDurationSeconds < 0 || now < 0 || now - prior.simulatedSeconds <= prehistoryDurationSeconds))
                    AppendSession(CloneForSession(prior));
            }
            sessionEvents.Sort((left, right) => left.sequence.CompareTo(right.sequence));
        }

        private NpcActivityTraceEvent FromTask(WorkTask task, string transition, double seconds, int day)
        {
            int person = task.HasAssignee ? task.AssigneeId.Id :
                task.OwnerId.Kind == EntityKind.Person ? task.OwnerId.Id : -1;
            return new NpcActivityTraceEvent
            {
                sessionId = SessionId,
                transition = transition ?? string.Empty,
                simulatedSeconds = seconds,
                dayIndex = day,
                personId = person >= 0 ? "P" + person : string.Empty,
                personNumericId = person,
                householdId = person >= 0 ? Household(person) : task.OwnerId.Kind == EntityKind.Household ? task.OwnerId.Id : -1,
                taskId = task.TaskId.ToString(),
                taskOwnerId = task.OwnerId.ToString(),
                taskDefinitionId = task.DefinitionId,
                taskStatus = task.Status.ToString(),
                taskOutcome = task.Outcome.ToString(),
                taskPriority = task.Priority.ToString(),
                operationId = !string.IsNullOrEmpty(task.CustomerRef) ? task.CustomerRef : task.TermsRef,
                target = task.TargetDescription,
                reservationId = task.ReservationHandleId,
                workedMinutes = task.MinutesWorked,
                plannedMinutes = task.PlannedMinutes,
                remainingMinutes = task.MinutesRemaining,
                retryCount = task.InterruptionCount,
                selectionReason = SelectionReason
            };
        }

        private void AppendObserved(NpcActivityTraceEvent item)
        {
            item.sequence = ++sequence;
            item.sessionId = SessionId;
            if (Monitoring)
            {
                history.Add(item);
                Trim(history, historyLimit, ref droppedHistoryEvents);
            }
            if (Recording && Matches(item)) AppendSession(CloneForSession(item));
        }

        private void AppendSession(NpcActivityTraceEvent item)
        {
            if (item == null || item.sequence <= sessionDroppedThroughSequence || !sessionEventSequences.Add(item.sequence)) return;
            item.sessionId = SessionId;
            sessionEvents.Add(item);
            while (sessionEvents.Count > retentionLimit)
            {
                sessionEventSequences.Remove(sessionEvents[0].sequence);
                sessionDroppedThroughSequence = Math.Max(sessionDroppedThroughSequence, sessionEvents[0].sequence);
                sessionEvents.RemoveAt(0);
                droppedSessionEvents++;
            }
        }

        private void PromoteTaskHistory(string taskId)
        {
            for (int i = 0; i < history.Count; i++)
                if (string.Equals(history[i].taskId, taskId, StringComparison.Ordinal))
                    AppendSession(CloneForSession(history[i]));
            sessionEvents.Sort((left, right) => left.sequence.CompareTo(right.sequence));
        }

        private bool Matches(NpcActivityTraceEvent item)
        {
            if (selectedPeople.Count == 0 && selectedHouseholds.Count == 0) return true;
            if (item.personNumericId >= 0 && selectedPeople.Contains(item.personNumericId)) return true;
            return item.householdId >= 0 && selectedHouseholds.Contains(item.householdId);
        }

        private int Household(int personId) => householdForPerson != null ? householdForPerson(personId) : -1;
        private double Now() => clock != null ? clock() : -1d;

        private static NpcActivityTraceEvent CloneForSession(NpcActivityTraceEvent source)
        {
            return new NpcActivityTraceEvent
            {
                schemaVersion = source.schemaVersion, sequence = source.sequence, sessionId = source.sessionId,
                eventType = source.eventType, transition = source.transition, simulatedSeconds = source.simulatedSeconds,
                dayIndex = source.dayIndex, personId = source.personId, personNumericId = source.personNumericId,
                householdId = source.householdId, taskId = source.taskId, taskOwnerId = source.taskOwnerId,
                taskDefinitionId = source.taskDefinitionId, taskStatus = source.taskStatus, taskOutcome = source.taskOutcome,
                taskPriority = source.taskPriority, operationId = source.operationId, shoppingTripId = source.shoppingTripId, target = source.target,
                routeGeneration = source.routeGeneration, routeWaypointIndex = source.routeWaypointIndex,
                routeWaypointCount = source.routeWaypointCount, routeIdentity = source.routeIdentity,
                routeMode = source.routeMode, scheduleState = source.scheduleState,
                activityLocationId = source.activityLocationId, moverState = source.moverState,
                moverVisible = source.moverVisible, routeActive = source.routeActive,
                destinationX = source.destinationX, destinationY = source.destinationY,
                destinationZ = source.destinationZ, hasRouteDestination = source.hasRouteDestination,
                activity = source.activity, movementStatus = source.movementStatus, positionX = source.positionX,
                positionY = source.positionY, positionZ = source.positionZ, hasWorldPosition = source.hasWorldPosition,
                distanceRemainingMeters = source.distanceRemainingMeters, estimatedArrivalSeconds = source.estimatedArrivalSeconds,
                destinationBuildingId = source.destinationBuildingId,
                reservationId = source.reservationId, workedMinutes = source.workedMinutes, plannedMinutes = source.plannedMinutes,
                remainingMinutes = source.remainingMinutes, retryCount = source.retryCount, reason = source.reason,
                refusalCauseClass = source.refusalCauseClass,
                selectionReason = source.selectionReason, recoveryState = source.recoveryState, detail = source.detail
            };
        }

        private static void Trim<T>(List<T> list, int limit, ref int dropped)
        {
            while (list.Count > limit) { list.RemoveAt(0); dropped++; }
        }

    }
}
#endif
