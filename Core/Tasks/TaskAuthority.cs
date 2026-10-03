using System;
using System.Collections.Generic;
using UnityEngine;
using LandLedgers.Primitives;
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
    public sealed class TaskAuthority
    {
        [SerializeField]
        private List<TaskDefinition> definitions = new List<TaskDefinition>();

        [SerializeField]
        private List<WorkTask> tasks = new List<WorkTask>();

        private readonly Dictionary<string, TaskDefinition> definitionLookup = new Dictionary<string, TaskDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<EntityId, WorkTask> taskLookup = new Dictionary<EntityId, WorkTask>();
        private readonly Dictionary<EntityId, List<WorkTask>> queueByOwner = new Dictionary<EntityId, List<WorkTask>>();

        private EntityIdRegistry idRegistry = new EntityIdRegistry();
        private ITaskDurationEstimator durationEstimator = new DefaultTaskDurationEstimator();

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
            GetOrCreateQueue(ownerId).Add(task);
            return task;
        }

        /// <summary>
        /// Assigns a queued task to a Person, booking skill-scaled minutes against
        /// their work-time budget. Returns false with a reason when the task is not
        /// assignable or the worker has no budget left — the task stays queued.
        /// </summary>
        public bool AssignTask(EntityId taskId, EntityId workerId, WorkTimeBudgetStore budgetStore, out string rejectionReason)
        {
            rejectionReason = null;
            WorkTask task = FindTask(taskId);
            if (task == null)
            {
                rejectionReason = "Unknown task: " + taskId;
                return false;
            }

            if (workerId.Kind != EntityKind.Person)
            {
                rejectionReason = "Tasks assign to Persons; got " + workerId.Kind + ".";
                return false;
            }

            if (task.Status != TaskStatus.Queued)
            {
                rejectionReason = "Task " + taskId + " is " + task.Status + ", not Queued.";
                return false;
            }

            if (!PrerequisitesComplete(task))
            {
                rejectionReason = "Task " + taskId + " has incomplete prerequisites.";
                return false;
            }

            TaskDefinition definition = GetDefinition(task.DefinitionId);
            int plannedMinutes;
            if (task.HasPlannedMinutesOverride)
            {
                plannedMinutes = durationEstimator.EstimateMinutesForBase(
                    task.PlannedMinutes,
                    definition != null ? definition.RequiredSkillId : null,
                    workerId);
            }
            else
            {
                plannedMinutes = durationEstimator.EstimateMinutes(definition, workerId);
            }

            task.SetPlannedMinutes(plannedMinutes);

            string budgetReason;
            if (budgetStore != null && !budgetStore.TryCommitTask(workerId, plannedMinutes, out budgetReason))
            {
                rejectionReason = "Cannot assign " + taskId + ": " + budgetReason;
                return false;
            }

            task.AssignTo(workerId);
            return true;
        }

        /// <summary>Assigned -&gt; InProgress. Work actually begins.</summary>
        public bool StartTask(EntityId taskId, int currentDayIndex, out string rejectionReason)
        {
            rejectionReason = null;
            WorkTask task = FindTask(taskId);
            if (task == null)
            {
                rejectionReason = "Unknown task: " + taskId;
                return false;
            }

            if (task.Status != TaskStatus.Assigned)
            {
                rejectionReason = "Task " + taskId + " is " + task.Status + ", not Assigned.";
                return false;
            }

            task.MarkStarted(currentDayIndex);
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

            return true;
        }

        /// <summary>Completes a task early (or records a failure). Unused commitment is released.</summary>
        public bool CompleteTask(EntityId taskId, TaskOutcome outcome, int currentDayIndex, WorkTimeBudgetStore budgetStore)
        {
            WorkTask task = FindTask(taskId);
            if (task == null || !task.IsOpen)
            {
                return false;
            }

            ReleaseUnusedCommitment(task, budgetStore);
            if (outcome == TaskOutcome.RecoverableFailure || outcome == TaskOutcome.SeriousFailure)
            {
                task.MarkFailed(outcome, currentDayIndex);
            }
            else
            {
                task.MarkComplete(outcome, currentDayIndex);
            }

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

            ReleaseUnusedCommitment(task, budgetStore);
            task.MarkInterrupted();
            task.Requeue();
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
            task.MarkCancelled();
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

        public WorkTask FindTask(EntityId taskId)
        {
            RebuildLookupsIfNeeded();
            WorkTask task;
            return taskLookup.TryGetValue(taskId, out task) ? task : null;
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
            if (definitionLookup.Count == definitions.Count && taskLookup.Count == tasks.Count)
            {
                return;
            }

            definitionLookup.Clear();
            foreach (TaskDefinition definition in definitions)
            {
                definitionLookup[definition.DefinitionId] = definition;
            }

            taskLookup.Clear();
            queueByOwner.Clear();
            foreach (WorkTask task in tasks)
            {
                taskLookup[task.TaskId] = task;
                GetOrCreateQueue(task.OwnerId).Add(task);
            }
        }
    }
}
