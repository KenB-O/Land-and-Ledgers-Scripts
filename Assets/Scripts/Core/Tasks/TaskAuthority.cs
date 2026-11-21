using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using UnityEngine;
using LandLedgers.Persistence;
using LandLedgers.Primitives;
using LandLedgers.Economy.Equipment;
using LandLedgers.Time;

namespace LandLedgers.Tasks
{
    /// <summary>
    /// TTS-2: estimates a task's planned minutes for a specific worker. The default
    /// implementation uses the definition's base minutes; TTS-3 swaps in a
    /// skill-based estimator (higher skill → fewer minutes, floored at 1).
    /// </summary>
    public interface ITaskDurationEstimator
    {
        int EstimateMinutes(TaskDefinition definition, EntityId workerId);

        /// <summary>
        /// Scales an instance-specific base duration (e.g. quantity-driven freight
        /// time) through the same curve. Additive: existing estimators keep working.
        /// </summary>
        int EstimateMinutesForBase(int baseMinutes, string skillId, EntityId workerId);
    }

    /// <summary>Default estimator: unskilled timing straight from the definition.</summary>
    public sealed class DefaultTaskDurationEstimator : ITaskDurationEstimator
    {
        public int EstimateMinutes(TaskDefinition definition, EntityId workerId)
        {
            if (definition == null)
            {
                return 1;
            }

            return EstimateMinutesForBase(definition.BaseMinutes, definition.RequiredSkillId, workerId);
        }

        public int EstimateMinutesForBase(int baseMinutes, string skillId, EntityId workerId)
        {
            return Math.Max(1, baseMinutes);
        }
    }

    /// <summary>
    /// TTS-2: the universal task authority. Every action in the game is a task:
    /// businesses, households, farms and the player all create tasks here, and
    /// workers (Persons, via the PKG-2 work relationships) execute them.
    ///
    /// Workload-based labor (Tech X §6.2): business activity generates task demand
    /// into per-owner queues; workers supply compatible availability. One worker may
    /// cover several task families; when simultaneous demand exceeds capacity, tasks
    /// WAIT in the queue — the authority never invents workers.
    ///
    /// Task assignment books minutes against the worker's TTS-1 work-time budget, so
    /// labor is a real constraint, not a slot count.
    /// </summary>
    [Serializable]
    public sealed partial class TaskAuthority
    {
        [SerializeField]
        private List<TaskDefinition> definitions = new List<TaskDefinition>();

        [SerializeField]
        private List<WorkTask> tasks = new List<WorkTask>();

        private readonly Dictionary<string, TaskDefinition> definitionLookup = new Dictionary<string, TaskDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<EntityId, WorkTask> taskLookup = new Dictionary<EntityId, WorkTask>();
        private readonly Dictionary<string, WorkTask> taskLookupByIdString = new Dictionary<string, WorkTask>(StringComparer.Ordinal);
        private readonly Dictionary<EntityId, List<WorkTask>> queueByOwner = new Dictionary<EntityId, List<WorkTask>>();

        private EntityIdRegistry idRegistry = new EntityIdRegistry();

        /// <summary>
        /// P3 (F6 production wiring): the live registry, so the bootstrapper
        /// can pass it to the LoggingOperationRuntime constructor. Mirrors
        /// AnimalRegistry.IdRegistry — the narrowest accessor; never construct
        /// a second registry for production use.
        /// </summary>
        internal EntityIdRegistry IdRegistry => idRegistry;
        private ITaskDurationEstimator durationEstimator = new DefaultTaskDurationEstimator();

        /// <summary>NX-1A: optional execution gate (Canon 4.1). Null = no enforcement.</summary>
        private ITaskExecutionGate executionGate;

        /// <summary>
        /// Pass VIII Worker 7: optional worker-availability seam. When installed,
        /// AssignTask and StartTask refuse workers the predicate marks
        /// unavailable (the production wiring marks the deceased: a dead person
        /// is never offered work, at task assignment or at start, no matter
        /// which caller forgot to check). Null = no enforcement (legacy).
        /// </summary>
        public Func<EntityId, bool> IsWorkerUnavailable { get; set; }

        /// <summary>
        /// Wiring pass (J-S3): optional completion hook, invoked once per completed
        /// task after it is marked terminal. The hub sets it to the skill resolver
        /// (SkillService.ApplyExperienceFromTask) so direct completions — the
        /// RecordWork auto-complete and the direct CompleteTask callers — grant
        /// experience the way the causal path already does. Null by default: a
        /// standalone authority completes tasks with no hook. The authority never
        /// references the skill system directly (no Core → Orchestration inversion);
        /// the hook is a plain Action set by the coordinator. The causal path
        /// (CompleteTaskWithCausalOutcome) passes applyCompletionHook: false
        /// because ResolveCausalOutcome already applied experience for that episode
        /// — the ledger is EntryId-idempotent per call, so a second application
        /// would credit the episode twice. Same shape as IsWorkerUnavailable.
        /// </summary>
        public Action<WorkTask, TaskOutcome, int> CompletionHook { get; set; }

        /// <summary>
        /// NX-1A: installs the equipment execution gate. Canon 4.1 CANON LOCK —
        /// a task is executable only when the full combination exists (capable
        /// person + required tool/equipment + workspace + materials + motive
        /// power + authority + time). The gate is consulted in StartTask.
        /// </summary>
        public void SetExecutionGate(ITaskExecutionGate gate)
        {
            executionGate = gate;
        }

        /// <summary>
        /// Campaign C (DEF-049): retention floor for terminal-task pruning, in days.
        /// A technical housekeeping constant, not an economic policy: same-tick and
        /// same-day readers (production phase completion checks, due-event handlers,
        /// diagnostics reconciliation) must never observe a task vanish on its
        /// completion day. Seven days exceeds every same-day or next-day read window
        /// found in the reader audit (see PruneTerminalTasks), while bounding save
        /// growth to roughly a week of terminal tasks instead of all history.
        /// </summary>
        public const int TerminalTaskRetentionDays = 7;

        /// <summary>
        /// Campaign C (DEF-049): optional external-reference seam, same shape as
        /// IsWorkerUnavailable. When installed, the daily terminal-task prune asks
        /// it whether a terminal task is still referenced by a live process outside
        /// this authority (live shopping trips hold leg/service task ids that
        /// save/load rehydration requires to resolve). Null = no external references
        /// known. The predicate must be cheap and must never mutate state; it is
        /// consulted once per terminal task per daily prune.
        /// </summary>
        public Func<EntityId, bool> IsTaskExternallyReferenced { get; set; }

        /// <summary>Swaps the duration estimator (TTS-3 installs the skill-based one).</summary>
        public void SetDurationEstimator(ITaskDurationEstimator estimator)
        {
            durationEstimator = estimator ?? new DefaultTaskDurationEstimator();
        }

        /// <summary>Registers an authored task definition. Duplicate ids are rejected deterministically.</summary>
        public bool RegisterDefinition(TaskDefinition definition, out string rejectionReason)
        {
            rejectionReason = null;
            if (definition == null || string.IsNullOrEmpty(definition.DefinitionId))
            {
                rejectionReason = "TaskDefinition requires a non-empty DefinitionId.";
                return false;
            }

            RebuildLookupsIfNeeded();
            if (definitionLookup.ContainsKey(definition.DefinitionId))
            {
                rejectionReason = "Duplicate TaskDefinitionId: " + definition.DefinitionId;
                return false;
            }

            definitions.Add(definition);
            definitionLookup[definition.DefinitionId] = definition;
            return true;
        }

        public TaskDefinition GetDefinition(string definitionId)
        {
            RebuildLookupsIfNeeded();
            TaskDefinition definition;
            return definitionLookup.TryGetValue(definitionId ?? string.Empty, out definition) ? definition : null;
        }

        /// <summary>
        /// Creates a queued task from a registered definition. Business-specific
        /// runtime truth (customer, target, terms) is supplied here — never in the definition.
        /// </summary>
        public WorkTask CreateTask(string definitionId, EntityId ownerId, int currentDayIndex, string customerRef = null, string termsRef = null)
        {
            return CreateTaskInternal(definitionId, ownerId, currentDayIndex, -1, customerRef, termsRef);
        }

        /// <summary>
        /// Creates a queued task with an instance-specific planned-minutes base
        /// (e.g. freight unload time driven by shipment quantity). The base still
        /// flows through the skill estimator at assignment, so skill scales it.
        /// </summary>
        public WorkTask CreateTaskWithPlannedMinutes(string definitionId, EntityId ownerId, int currentDayIndex, int plannedMinutesBase, string customerRef = null, string termsRef = null)
        {
            return CreateTaskInternal(definitionId, ownerId, currentDayIndex, plannedMinutesBase, customerRef, termsRef);
        }

        private WorkTask CreateTaskInternal(string definitionId, EntityId ownerId, int currentDayIndex, int plannedMinutesOverride, string customerRef, string termsRef)
        {
            TaskDefinition definition = GetDefinition(definitionId);
            if (definition == null)
            {
                throw new ArgumentException("Unknown TaskDefinitionId: " + definitionId, nameof(definitionId));
            }

            var task = new WorkTask(idRegistry.Allocate(EntityKind.WorkTask), definitionId, ownerId, currentDayIndex);
            task.ApplyDefinitionSnapshot(definition);
            if (plannedMinutesOverride > 0)
            {
                task.SetPlannedMinutesOverride(plannedMinutesOverride);
            }

            task.SetCustomer(customerRef, termsRef);

            RebuildLookupsIfNeeded();
            tasks.Add(task);
            taskLookup[task.TaskId] = task;
            taskLookupByIdString[task.TaskId.ToString()] = task;
            GetOrCreateQueue(ownerId).Add(task);
            NotifyTimingObserver(task, "CreateTask", currentDayIndex);
            return task;
        }

        /// <summary>
        /// Assigns a queued task to a Person, booking skill-scaled minutes against
        /// their work-time budget. Returns false with a reason when the task is not
        /// assignable or the worker has no budget left — the task stays queued.
        /// </summary>
        public bool AssignTask(EntityId taskId, EntityId workerId, WorkTimeBudgetStore budgetStore, out string rejectionReason)
        {
            return AssignTask(taskId, workerId, budgetStore, -1, out rejectionReason);
        }

        /// <summary>Assigns a task while explicitly advancing the shared work-day budget.</summary>
        public bool AssignTask(EntityId taskId, EntityId workerId, WorkTimeBudgetStore budgetStore,
            int currentDayIndex, out string rejectionReason)
        {
            rejectionReason = null;
            WorkTask task = FindTask(taskId);
            if (task == null)
            {
                return RefuseTask(taskId, "AssignTask", "Unknown task: " + taskId, currentDayIndex, out rejectionReason);
            }

            if (workerId.Kind != EntityKind.Person)
            {
                return RefuseTask(taskId, "AssignTask", "Tasks assign to Persons; got " + workerId.Kind + ".", currentDayIndex, out rejectionReason);
            }

            // Pass VIII Worker 7: the dead are never offered work. The seam is
            // consulted here so every caller is covered, including callers
            // that never learned to check.
            if (IsWorkerUnavailable != null)
            {
                bool unavailable;
                try
                {
                    unavailable = IsWorkerUnavailable(workerId);
                }
                catch (Exception ex)
                {
                    return RefuseTask(taskId, "AssignTask", "Cannot assign " + taskId + ": worker-availability check failed (" +
                        ex.GetType().Name + ") — refused rather than assigned blind.", currentDayIndex, out rejectionReason);
                }
                if (unavailable)
                {
                    return RefuseTask(taskId, "AssignTask", "Cannot assign " + taskId + ": " + workerId +
                        " is unavailable for work (worker-availability seam).", currentDayIndex, out rejectionReason);
                }
            }

            if (task.Status != TaskStatus.Queued)
            {
                return RefuseTask(taskId, "AssignTask", "Task " + taskId + " is " + task.Status + ", not Queued.", currentDayIndex, out rejectionReason);
            }

            if (!PrerequisitesComplete(task))
            {
                return RefuseTask(taskId, "AssignTask", "Task " + taskId + " has incomplete prerequisites.", currentDayIndex, out rejectionReason);
            }

            TaskDefinition definition = GetDefinition(task.DefinitionId);
            int plannedMinutes = EstimatePlannedMinutes(task, definition, workerId);

            task.SetPlannedMinutes(plannedMinutes);

            // Commit the remaining work, not the full estimate. A fresh
            // task has no worked minutes, so MinutesRemaining equals
            // plannedMinutes and the fresh path is unchanged. A re-queued
            // interrupted task keeps its minutesWorked, so committing the
            // full estimate would re-book minutes already worked
            // (over-commit equals MinutesWorked per re-assignment).
            int commitMinutes = task.MinutesRemaining;

            string budgetReason;
            if (budgetStore != null && currentDayIndex >= 0)
            {
                budgetStore.EnsureDay(currentDayIndex);
            }
            if (budgetStore != null && !budgetStore.TryCommitTask(workerId, commitMinutes, out budgetReason))
            {
                return RefuseTask(taskId, "AssignTask", "Cannot assign " + taskId + ": " + budgetReason, currentDayIndex, out rejectionReason);
            }

            task.AssignTo(workerId);
            NotifyTimingObserver(task, "AssignTask", currentDayIndex);
            return true;
        }

        /// <summary>
        /// Human-readable word for a blocking task status, used in occupancy
        /// refusals so the diagnostic names the state plainly ("in-progress")
        /// instead of the raw enum name ("InProgress"). Any future blocking
        /// state falls back to its enum name.
        /// </summary>
        private static string OccupancyStatusWord(TaskStatus status)
        {
            switch (status)
            {
                case TaskStatus.InProgress: return "in-progress";
                case TaskStatus.Paused: return "paused";
                case TaskStatus.WaitingOnDependency: return "waiting on dependency";
                case TaskStatus.ResourcesReserved: return "resources reserved";
                default: return status.ToString();
            }
        }

        /// <summary>Returns the same skill-scaled duration AssignTask will commit for a worker.</summary>
        public int EstimateMinutesForWorker(string definitionId, EntityId workerId)
        {
            TaskDefinition definition = GetDefinition(definitionId);
            return Math.Max(1, durationEstimator.EstimateMinutes(definition, workerId));
        }

        /// <summary>Returns the remaining skill-scaled duration for an existing task and worker.</summary>
        public int EstimateRemainingMinutesForWorker(EntityId taskId, EntityId workerId)
        {
            WorkTask task = FindTask(taskId);
            if (task == null) return 1;
            TaskDefinition definition = GetDefinition(task.DefinitionId);
            return Math.Max(1, EstimatePlannedMinutes(task, definition, workerId));
        }

        /// <summary>
        /// Assigned -&gt; InProgress (or ResourcesReserved -&gt; InProgress). Work actually begins.
        ///
        /// Pass IV: when a reservation authority AND a request builder are installed, an
        /// Assigned task reserves its resources atomically via TryReserveAll before work
        /// begins (Assigned -&gt; ResourcesReserved -&gt; InProgress); a refused reservation
        /// refuses the start loudly. Without the seams, behavior is unchanged.
        /// </summary>
        public bool StartTask(EntityId taskId, int currentDayIndex, out string rejectionReason)
        {
            rejectionReason = null;
            WorkTask task = FindTask(taskId);
            if (task == null)
            {
                return RefuseTask(taskId, "StartTask", "Unknown task: " + taskId, currentDayIndex, out rejectionReason);
            }

            bool fromAssigned = task.Status == TaskStatus.Assigned;
            bool fromReserved = task.Status == TaskStatus.ResourcesReserved;
            if (!fromAssigned && !fromReserved)
            {
                return RefuseTask(taskId, "StartTask", "Task " + taskId + " is " + task.Status + ", not Assigned or ResourcesReserved.", currentDayIndex, out rejectionReason);
            }

            if (task.HasAssignee)
            {
                foreach (WorkTask activeTask in tasks)
                {
                    if (activeTask == null || ReferenceEquals(activeTask, task) || !activeTask.HasAssignee
                        || !TaskLifecyclePolicy.BlocksConcurrentTaskStart(activeTask.Status)
                        || activeTask.AssigneeId != task.AssigneeId) continue;
                    return RefuseTask(taskId, "StartTask", "Cannot start task " + taskId + ": " + task.AssigneeId
                        + " is already occupied by task " + activeTask.TaskId + " (" + OccupancyStatusWord(activeTask.Status) + ").", currentDayIndex, out rejectionReason);
                }
            }

            // Pass VIII Worker 7: a worker who became unavailable between
            // assignment and start (death, above all) must not start work.
            if (IsWorkerUnavailable != null && task.HasAssignee)
            {
                bool unavailable;
                try
                {
                    unavailable = IsWorkerUnavailable(task.AssigneeId);
                }
                catch (Exception ex)
                {
                    return RefuseTask(taskId, "StartTask", "Task " + taskId + ": worker-availability check failed (" +
                        ex.GetType().Name + ") — refused rather than started blind.", currentDayIndex, out rejectionReason);
                }
                if (unavailable)
                {
                    return RefuseTask(taskId, "StartTask", "Task " + taskId + ": " + task.AssigneeId +
                        " is unavailable for work (worker-availability seam) — start refused.", currentDayIndex, out rejectionReason);
                }
            }

            // NX-1A: Canon 4.1 — work begins only when the full combination
            // exists. The gate names the missing requirement loudly; a
            // refusal here is never a silent debuff (Tech X §3.9).
            if (executionGate != null)
            {
                string gateRefusal = executionGate.CheckStart(task, GetDefinition(task.DefinitionId), currentDayIndex);
                if (gateRefusal != null)
                {
                    return RefuseTask(taskId, "StartTask", gateRefusal, currentDayIndex, out rejectionReason);
                }
            }

            // Pass IV: atomic resource reservation before work begins.
            if (fromAssigned && reservationAuthority != null && reservationRequestBuilder != null)
            {
                ReservationRequest request;
                try
                {
                    request = reservationRequestBuilder(task, GetDefinition(task.DefinitionId));
                }
                catch (Exception builderFailure)
                {
                    return RefuseTask(taskId, "StartTask", "Task " + taskId + ": reservation request builder failed: " + builderFailure.Message, currentDayIndex, out rejectionReason);
                }

                if (request == null)
                {
                    return RefuseTask(taskId, "StartTask", "Task " + taskId + ": reservation request builder returned no request.", currentDayIndex, out rejectionReason);
                }

                string reservationRefusal = reservationAuthority.TryReserveAll(request, out ReservationHandle handle);
                if (reservationRefusal != null)
                {
                    return RefuseTask(taskId, "StartTask", "Task " + taskId + ": resource reservation refused: " + reservationRefusal, currentDayIndex, out rejectionReason);
                }

                task.SetReservationHandleId(handle != null ? handle.ReservationId : string.Empty);
                task.MarkResourcesReserved();
            }

            if (!TaskLifecyclePolicy.CanTransition(task.Status, TaskStatus.InProgress))
            {
                return RefuseTask(taskId, "StartTask", "Task " + taskId + ": " + task.Status + " -> InProgress is not a legal lifecycle transition.", currentDayIndex, out rejectionReason);
            }

            task.MarkStarted(currentDayIndex);
            NotifyTimingObserver(task, "StartTask", currentDayIndex);
            return true;
        }

        /// <summary>
        /// Records minutes worked (whole minutes, TTS-1 quantum). When worked minutes
        /// reach the plan, the task auto-completes with Success.
        /// </summary>
        public bool RecordWork(EntityId taskId, int minutes, WorkTimeBudgetStore budgetStore, int currentDayIndex, out string rejectionReason)
        {
            rejectionReason = null;
            WorkTask task = FindTask(taskId);
            if (task == null)
            {
                rejectionReason = "Unknown task: " + taskId;
                return false;
            }

            if (task.Status != TaskStatus.InProgress)
            {
                rejectionReason = "Task " + taskId + " is " + task.Status + ", not InProgress.";
                return false;
            }

            int wholeMinutes = WorkTimeMath.ClampToWholeMinutes(minutes);
            task.AddWorkedMinutes(wholeMinutes);
            if (task.HasAssignee && budgetStore != null)
            {
                budgetStore.GetOrCreate(task.AssigneeId).RecordWorked(wholeMinutes);
            }

            if (task.MinutesRemaining <= 0)
            {
                CompleteTask(taskId, TaskOutcome.Success, currentDayIndex, budgetStore);
            }

            NotifyTimingObserver(task, "RecordWork", currentDayIndex);
            return true;
        }

        /// <summary>Records progress without assigning a default outcome.</summary>
        public bool RecordWorkProgress(EntityId taskId, int minutes, WorkTimeBudgetStore budgetStore,
            int currentDayIndex, out bool reachedCompletion, out string rejectionReason)
        {
            reachedCompletion = false;
            rejectionReason = null;
            WorkTask task = FindTask(taskId);
            if (task == null) { rejectionReason = "Unknown task: " + taskId; return false; }
            if (task.Status != TaskStatus.InProgress)
            {
                rejectionReason = "Task " + taskId + " is " + task.Status + ", not InProgress.";
                return false;
            }
            int wholeMinutes = WorkTimeMath.ClampToWholeMinutes(minutes);
            task.AddWorkedMinutes(wholeMinutes);
            if (task.HasAssignee && budgetStore != null)
                budgetStore.GetOrCreate(task.AssigneeId).RecordWorked(wholeMinutes);
            reachedCompletion = task.MinutesRemaining <= 0;
            NotifyTimingObserver(task, "RecordWorkProgress", currentDayIndex);
            return true;
        }

        /// <summary>
        /// Completes a task early (or records a failure). Unused commitment is released.
        ///
        /// Pass IV: transitions from the new lifecycle states are validated against
        /// TaskLifecyclePolicy (legacy states keep their existing behavior); terminal
        /// transitions release reservations and cancel the task's due events.
        ///
        /// Wiring pass (J-S3): after the task is marked terminal, the CompletionHook
        /// runs once (unless applyCompletionHook is false). The hook's caller owns
        /// idempotency: the causal path passes false because ResolveCausalOutcome
        /// already applied experience for the episode.
        /// </summary>
        public bool CompleteTask(EntityId taskId, TaskOutcome outcome, int currentDayIndex, WorkTimeBudgetStore budgetStore, bool applyCompletionHook = true)
        {
            WorkTask task = FindTask(taskId);
            if (task == null || !task.IsOpen)
            {
                return false;
            }

            TaskStatus target = outcome == TaskOutcome.RecoverableFailure || outcome == TaskOutcome.SeriousFailure
                ? TaskStatus.Failed
                : TaskStatus.Complete;
            if (IsPass4LifecycleState(task.Status) && !TaskLifecyclePolicy.CanTransition(task.Status, target))
            {
                Debug.LogWarning("[TaskAuthority] CompleteTask refused: Task " + taskId + ": " +
                    task.Status + " -> " + target + " is not a legal lifecycle transition.");
                return false;
            }

            ReleaseUnusedCommitment(task, budgetStore);
            ReleaseTaskResources(task);
            if (outcome == TaskOutcome.RecoverableFailure || outcome == TaskOutcome.SeriousFailure)
            {
                task.MarkFailed(outcome, currentDayIndex);
            }
            else
            {
                task.MarkComplete(outcome, currentDayIndex);
            }

            if (applyCompletionHook && CompletionHook != null)
            {
                CompletionHook(task, outcome, currentDayIndex);
            }

            NotifyTimingObserver(task, "CompleteTask", currentDayIndex);
            return true;
        }

        /// <summary>
        /// Interrupts a task: work done so far is preserved, the assignment and its
        /// remaining commitment are released, and the task returns to Queued for
        /// reassignment (Tech II §2.5 preemption). Non-interruptible tasks refuse.
        /// </summary>
        public bool InterruptTask(EntityId taskId, WorkTimeBudgetStore budgetStore, out string rejectionReason)
        {
            rejectionReason = null;
            WorkTask task = FindTask(taskId);
            if (task == null)
            {
                rejectionReason = "Unknown task: " + taskId;
                return false;
            }

            if (!task.Interruptible)
            {
                rejectionReason = "Task " + taskId + " is not interruptible.";
                return false;
            }

            if (!task.IsOpen)
            {
                rejectionReason = "Task " + taskId + " is " + task.Status + ".";
                return false;
            }

            // Pass IV: interruption from the new lifecycle states follows the policy
            // table (Paused -> Interrupted is legal; the waiting/reserved states are
            // not). Legacy states keep their existing behavior.
            if (IsPass4LifecycleState(task.Status)
                && !TaskLifecyclePolicy.CanTransition(task.Status, TaskStatus.Interrupted))
            {
                rejectionReason = "Task " + taskId + ": " + task.Status +
                    " cannot be interrupted (TaskLifecyclePolicy).";
                return false;
            }

            ReleaseUnusedCommitment(task, budgetStore);
            ReleaseTaskResources(task);
            task.MarkInterrupted();
            task.Requeue();
            NotifyTimingObserver(task, "InterruptTask", -1);
            return true;
        }

        public bool CancelTask(EntityId taskId, WorkTimeBudgetStore budgetStore)
        {
            WorkTask task = FindTask(taskId);
            if (task == null || !task.IsOpen)
            {
                return false;
            }

            ReleaseUnusedCommitment(task, budgetStore);
            ReleaseTaskResources(task);
            task.MarkCancelled();
            NotifyTimingObserver(task, "CancelTask", -1);
            return true;
        }

        /// <summary>
        /// Next queued task for an owner, ordered by priority then creation order,
        /// skipping tasks whose prerequisites are incomplete. Null when the queue is
        /// empty or everything is blocked — callers wait; workers are never invented.
        /// </summary>
        public WorkTask NextQueuedTask(EntityId ownerId)
        {
            List<WorkTask> queue = GetOrCreateQueue(ownerId);
            WorkTask best = null;
            foreach (WorkTask task in queue)
            {
                if (task.Status != TaskStatus.Queued || !PrerequisitesComplete(task))
                {
                    continue;
                }

                if (best == null
                    || task.Priority > best.Priority
                    || (task.Priority == best.Priority && IsCreatedBefore(task, best)))
                {
                    best = task;
                }
            }

            return best;
        }

        /// <summary>Open (queued/assigned/in-progress) tasks for an owner.</summary>
        public List<WorkTask> GetOpenTasks(EntityId ownerId)
        {
            var open = new List<WorkTask>();
            foreach (WorkTask task in GetOrCreateQueue(ownerId))
            {
                if (task.IsOpen)
                {
                    open.Add(task);
                }
            }

            return open;
        }

        /// <summary>
        /// Finds an already-started task that prevents a worker from starting
        /// another task, regardless of which business or household owns it.
        /// PersonScheduleTracker reservations cover planned time windows; this
        /// query covers actual task occupancy that may not yet have a matching
        /// reservation.
        /// </summary>
        public WorkTask FindBlockingTaskForAssignee(EntityId workerId)
        {
            if (!workerId.IsValid) return null;
            foreach (WorkTask task in tasks)
            {
                if (task == null || !task.HasAssignee || task.AssigneeId != workerId
                    || !TaskLifecyclePolicy.BlocksConcurrentTaskStart(task.Status))
                    continue;
                return task;
            }

            return null;
        }

        public WorkTask FindTask(EntityId taskId)
        {
            RebuildLookupsIfNeeded();
            WorkTask task;
            return taskLookup.TryGetValue(taskId, out task) ? task : null;
        }

        /// <summary>Compatibility lookup for persisted domain task identifiers.</summary>
        public WorkTask FindTask(string taskId)
        {
            if (string.IsNullOrWhiteSpace(taskId)) return null;
            return taskLookupByIdString.TryGetValue(taskId, out WorkTask task) ? task : null;
        }

        /// <summary>
        /// Rebinds a persisted subsystem process to its existing open task after
        /// save/load. This is intentionally a read-only lookup; it does not create
        /// a second task or alter task state.
        /// </summary>
        public WorkTask FindOpenTask(string definitionId, EntityId ownerId, EntityId assigneeId)
        {
            for (int i = 0; i < tasks.Count; i++)
            {
                WorkTask task = tasks[i];
                if (task == null || task.Status == TaskStatus.Complete
                    || !string.Equals(task.DefinitionId, definitionId ?? string.Empty, StringComparison.Ordinal)
                    || task.OwnerId != ownerId) continue;
                if (assigneeId.IsValid && task.HasAssignee && task.AssigneeId != assigneeId) continue;
                return task;
            }
            return null;
        }

        private bool PrerequisitesComplete(WorkTask task)
        {
            foreach (EntityId prereqId in task.PrerequisiteTaskIds)
            {
                WorkTask prereq = FindTask(prereqId);
                if (prereq == null || prereq.Status != TaskStatus.Complete)
                {
                    return false;
                }
            }

            return true;
        }

        private void ReleaseUnusedCommitment(WorkTask task, WorkTimeBudgetStore budgetStore)
        {
            if (task.HasAssignee && budgetStore != null && task.MinutesRemaining > 0)
            {
                budgetStore.GetOrCreate(task.AssigneeId).ReleaseCommitment(task.MinutesRemaining);
            }
        }

        private bool IsCreatedBefore(WorkTask left, WorkTask right)
        {
            // Queues are append-ordered, so index order is creation order.
            List<WorkTask> queue = GetOrCreateQueue(left.OwnerId);
            return queue.IndexOf(left) < queue.IndexOf(right);
        }

        private List<WorkTask> GetOrCreateQueue(EntityId ownerId)
        {
            List<WorkTask> queue;
            if (!queueByOwner.TryGetValue(ownerId, out queue))
            {
                queue = new List<WorkTask>();
                queueByOwner[ownerId] = queue;
            }

            return queue;
        }

        private void RebuildLookupsIfNeeded()
        {
            if (definitionLookup.Count == definitions.Count
                && taskLookup.Count == tasks.Count
                && taskLookupByIdString.Count == tasks.Count)
            {
                return;
            }

            definitionLookup.Clear();
            foreach (TaskDefinition definition in definitions)
            {
                definitionLookup[definition.DefinitionId] = definition;
            }

            taskLookup.Clear();
            taskLookupByIdString.Clear();
            queueByOwner.Clear();
            foreach (WorkTask task in tasks)
            {
                taskLookup[task.TaskId] = task;
                taskLookupByIdString[task.TaskId.ToString()] = task;
                GetOrCreateQueue(task.OwnerId).Add(task);
            }
        }

        /// <summary>
        /// CLN-1: captures task state for the save pipeline. Definitions and work tasks;
        /// lookups rebuild on load. The task ID cursor persists via HF-1 entityIdCursors.
        /// </summary>
        public TaskAuthoritySaveDto CaptureSaveDto()
        {
            return new TaskAuthoritySaveDto
            {
                definitions = new List<TaskDefinition>(definitions),
                tasks = new List<WorkTask>(tasks),
            };
        }

        /// <summary>CLN-1: restores task state from the save pipeline.</summary>
        public void LoadFromSaveDto(TaskAuthoritySaveDto dto)
        {
            definitions.Clear();
            tasks.Clear();
            // Clear-then-load: drop every lookup so a coincidentally equal count can
            // never leave stale entries behind (idempotent restore).
            definitionLookup.Clear();
            taskLookup.Clear();
            taskLookupByIdString.Clear();
            queueByOwner.Clear();
            if (dto == null)
            {
                return;
            }

            // Pass XII Worker F (copy-on-load, E-DEFECT-02 round 2):
            // definitions and tasks are mutated in place throughout
            // their lifecycle, so the load must clone them — storing
            // the DTO's objects meant a double restore reloaded
            // already-started, half-worked tasks instead of queued ones.
            if (dto.definitions != null)
            {
                foreach (TaskDefinition d in dto.definitions)
                {
                    if (d != null) definitions.Add(d.Copy());
                }
            }

            if (dto.tasks != null)
            {
                foreach (WorkTask t in dto.tasks)
                {
                    if (t == null) continue;
                    tasks.Add(t.Copy());
                    // This authority owns its own allocator; hub cursor restoration
                    // cannot advance it. Never mint an ID already present in the save.
                    if (t.TaskId.IsValid && t.TaskId.Kind == EntityKind.WorkTask)
                        idRegistry.SeedKind(EntityKind.WorkTask, checked(t.TaskId.Id + 1));
                }
            }

            RebuildLookupsIfNeeded();
        }

        /// <summary>
        /// Campaign C (DEF-049): removes terminal tasks nothing reads anymore, so
        /// saves grow with live state instead of total tasks ever created
        /// (Check 10 F-15: CaptureSaveDto persisted every task ever created).
        ///
        /// A task is pruned only when ALL of these hold:
        /// - Status is Complete, Failed, or Cancelled. Interrupted re-queues, so
        ///   it is never terminal here; ReworkRequired keeps its corrective-task
        ///   link and is out of this pass's scope.
        /// - Its terminal day is at least TerminalTaskRetentionDays before
        ///   currentDayIndex. Complete/Failed record completedDayIndex.
        ///   Cancelled records no day (MarkCancelled takes no day), so Cancelled
        ///   tasks use createdDayIndex as the anchor; no production reader needs
        ///   a cancelled task after cancellation (every FindTask call site
        ///   null-handles), so the creation-day anchor is safe. Recording a
        ///   cancellation day is a follow-up, not a blocker.
        /// - No non-terminal task lists it as a prerequisite (PrerequisitesComplete
        ///   requires a prereq to resolve with Status Complete; no production code
        ///   writes prerequisites today, but the guard keeps the prune safe if one
        ///   ever does).
        /// - IsTaskExternallyReferenced, when installed, does not flag it (live
        ///   shopping trips: RehydrateAfterLoad aborts a trip whose leg/service
        ///   task id fails to resolve, and leg tasks complete mid-trip while the
        ///   trip is still live — so a completed leg task MUST survive while its
        ///   trip lives).
        ///
        /// Removal drops the task from the task list, both id lookups, and its
        /// owner's queue. Reservations and due events were already released by the
        /// terminal transition itself (ReleaseTaskResources), so the prune releases
        /// nothing and changes no scheduling, dispatch, or completion semantics.
        /// Returns the number of tasks removed. A negative currentDayIndex prunes
        /// nothing.
        /// </summary>
        public int PruneTerminalTasks(int currentDayIndex)
        {
            if (currentDayIndex < 0)
            {
                return 0;
            }

            // Prerequisite targets of open tasks must survive: a dependent that
            // can never see its prereq Complete can never start.
            var prereqTargets = new HashSet<EntityId>();
            foreach (WorkTask task in tasks)
            {
                if (task == null || !task.IsOpen)
                {
                    continue;
                }

                foreach (EntityId prereqId in task.PrerequisiteTaskIds)
                {
                    prereqTargets.Add(prereqId);
                }
            }

            int pruned = 0;
            // Backwards so removal never skips an entry.
            for (int i = tasks.Count - 1; i >= 0; i--)
            {
                WorkTask task = tasks[i];
                if (task == null)
                {
                    continue;
                }

                if (task.Status != TaskStatus.Complete
                    && task.Status != TaskStatus.Failed
                    && task.Status != TaskStatus.Cancelled)
                {
                    continue;
                }

                int terminalDay = task.CompletedDayIndex >= 0 ? task.CompletedDayIndex : task.CreatedDayIndex;
                if (currentDayIndex - terminalDay < TerminalTaskRetentionDays)
                {
                    continue;
                }

                if (prereqTargets.Contains(task.TaskId))
                {
                    continue;
                }

                if (IsTaskExternallyReferenced != null)
                {
                    bool referenced;
                    try
                    {
                        referenced = IsTaskExternallyReferenced(task.TaskId);
                    }
                    catch (Exception ex)
                    {
                        // Fail closed for this task: a broken predicate must never
                        // delete a task it cannot vouch for, and must not abort the
                        // sweep for the other tasks.
                        Debug.LogWarning("[TaskAuthority] DEF-049 prune: external-reference check failed for task "
                            + task.TaskId + " (" + ex.GetType().Name + ") — task kept.");
                        continue;
                    }

                    if (referenced)
                    {
                        continue;
                    }
                }

                tasks.RemoveAt(i);
                taskLookup.Remove(task.TaskId);
                taskLookupByIdString.Remove(task.TaskId.ToString());
                List<WorkTask> queue;
                if (queueByOwner.TryGetValue(task.OwnerId, out queue))
                {
                    queue.Remove(task);
                }

                pruned++;
            }

            return pruned;
        }
    }
}
