using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using UnityEngine;
using LandLedgers.Economy.Equipment;
using LandLedgers.Primitives;
using LandLedgers.Time;

namespace LandLedgers.Tasks
{
    /// <summary>
    /// Pass IV (Living World Foundation): task execution lifecycle on TaskAuthority.
    ///
    /// Enforces TaskLifecyclePolicy on every state transition — anything the policy
    /// table does not allow is refused loudly (false + reason), never silently
    /// applied. New states (Paused, WaitingOnPassiveProcess, WaitingOnDependency,
    /// ResourcesReserved, ReworkRequired) are traversed only through these methods.
    ///
    /// Optional seams (null = current behavior):
    /// - Reservation authority: when set, StartTask reserves atomically via
    ///   TryReserveAll (needs a request builder) and stores the handle id on the
    ///   task; cancellation / interruption / completion release via ReleaseAllForTask.
    /// - Due-event scheduler: when set, terminal transitions cancel the task's
    ///   scheduled due events (no orphaned completions after save/load).
    /// - Ambient work-time budget store: when set, entering
    ///   WaitingOnPassiveProcess releases the worker's remaining commitment (the
    ///   worker is free for other work per the policy) and ResumeTask re-commits
    ///   it; a resume the worker cannot afford is refused loudly.
    ///
    /// NOTE on layering: referencing IUnifiedReservationAuthority
    /// (Domains/Economy/Equipment) from Core/Tasks is a sanctioned Pass IV seam —
    /// the foundation contracts assign this setter to Worker 1 explicitly.
    ///
    /// Canon: XXXI Part III §§3.1-3.7. Technical: XIV Part II §§2.1-2.8.
    /// </summary>
    public sealed partial class TaskAuthority
    {
        /// <summary>Pass IV: optional unified reservation authority. Null = no reservation enforcement.</summary>
        private IUnifiedReservationAuthority reservationAuthority;

        /// <summary>Pass IV: optional due-event scheduler. Null = no due-event linkage.</summary>
        private IDueEventScheduler dueEventScheduler;

        /// <summary>
        /// Pass IV: builds the atomic reservation request for a task about to start.
        /// Null = StartTask does not auto-reserve (use MarkResourcesReserved).
        /// </summary>
        private Func<WorkTask, TaskDefinition, ReservationRequest> reservationRequestBuilder;

        /// <summary>
        /// Pass IV: ambient budget store for worker-commitment truth across the new
        /// states. Null = lifecycle methods do not touch budgets.
        /// </summary>
        private WorkTimeBudgetStore ambientBudgetStore;

        /// <summary>Pass IV: installs the unified reservation authority (Worker 2's implementation).</summary>
        public void SetReservationAuthority(IUnifiedReservationAuthority authority)
        {
            reservationAuthority = authority;
        }

        /// <summary>Pass IV: the installed reservation authority, or null.</summary>
        public IUnifiedReservationAuthority ReservationAuthority => reservationAuthority;

        /// <summary>Pass IV: installs the due-event scheduler (hub-owned).</summary>
        public void SetDueEventScheduler(IDueEventScheduler scheduler)
        {
            dueEventScheduler = scheduler;
        }

        /// <summary>Pass IV: the installed due-event scheduler, or null.</summary>
        public IDueEventScheduler DueEventScheduler => dueEventScheduler;

        /// <summary>
        /// Pass IV: installs the builder StartTask uses to reserve resources atomically
        /// when a reservation authority is set. The request's OwnerTaskId must be the
        /// task's EntityId string so ReleaseAllForTask(task.TaskId.ToString()) matches.
        /// </summary>
        public void SetReservationRequestBuilder(Func<WorkTask, TaskDefinition, ReservationRequest> builder)
        {
            reservationRequestBuilder = builder;
        }

        /// <summary>
        /// Pass IV: wires an ambient budget store so the new lifecycle states keep
        /// worker commitments truthful (passive-process entry releases, resume
        /// re-commits). Null restores the no-budget-touch behavior.
        /// </summary>
        public void SetWorkTimeBudgetStore(WorkTimeBudgetStore budgetStore)
        {
            ambientBudgetStore = budgetStore;
        }

        /// <summary>
        /// Pass IV: Assigned -&gt; ResourcesReserved. Records a reservation the caller
        /// already granted via IUnifiedReservationAuthority.TryReserveAll. When a
        /// reservation authority is installed, an empty reservation id is refused —
        /// reserve first, then mark.
        /// </summary>
        public bool MarkResourcesReserved(EntityId taskId, string reservationId, string reason, out string rejectionReason)
        {
            rejectionReason = null;
            WorkTask task = FindTask(taskId);
            if (!TryTransitionTo(task, TaskStatus.ResourcesReserved, "mark resources reserved", out rejectionReason))
            {
                return false;
            }

            if (reservationAuthority != null && string.IsNullOrEmpty(reservationId))
            {
                rejectionReason = "Task " + taskId + ": a reservation authority is installed — supply the granted reservation id (reserve via IUnifiedReservationAuthority.TryReserveAll first).";
                return false;
            }

            task.SetReservationHandleId(reservationId);
            task.MarkResourcesReserved();
            return true;
        }

        /// <summary>
        /// Pass IV: InProgress -&gt; Paused. Work is suspended; the assignment and all
        /// reservations are HELD (per TaskLifecyclePolicy.HoldsWorkerCommitment), so the
        /// budget commitment is not released. budgetStore is accepted for API symmetry
        /// with the other lifecycle methods.
        /// </summary>
        public bool PauseTask(EntityId taskId, WorkTimeBudgetStore budgetStore, string reason, out string rejectionReason)
        {
            rejectionReason = null;
            WorkTask task = FindTask(taskId);
            if (!TryTransitionTo(task, TaskStatus.Paused, "pause", out rejectionReason))
            {
                return false;
            }

            task.MarkPaused();
            return true;
        }

        /// <summary>
        /// Pass IV: Paused / WaitingOnPassiveProcess / WaitingOnDependency -&gt; InProgress.
        /// When the ambient budget store is wired and the task was released for a
        /// passive process, the remaining commitment is re-booked; a resume the worker
        /// cannot afford is refused loudly (the worker genuinely has no time left).
        /// </summary>
        public bool ResumeTask(EntityId taskId, string reason, out string rejectionReason)
        {
            rejectionReason = null;
            WorkTask task = FindTask(taskId);
            if (!TryTransitionTo(task, TaskStatus.InProgress, "resume", out rejectionReason))
            {
                return false;
            }

            bool releasedForPassiveProcess = task.Status == TaskStatus.WaitingOnPassiveProcess;
            if (releasedForPassiveProcess && ambientBudgetStore != null && task.HasAssignee && task.MinutesRemaining > 0)
            {
                string budgetReason;
                if (!ambientBudgetStore.TryCommitTask(task.AssigneeId, task.MinutesRemaining, out budgetReason))
                {
                    rejectionReason = "Task " + taskId + ": cannot resume — " + budgetReason;
                    return false;
                }
            }

            task.MarkResumed();
            task.SetDueEventId(string.Empty);
            task.SetDependencyDescription(string.Empty);
            return true;
        }

        /// <summary>
        /// Pass IV: InProgress -&gt; WaitingOnPassiveProcess. The worker is released for
        /// other work (remaining budget commitment freed when the ambient store is
        /// wired); equipment, workspace and material reservations stay held until the
        /// due event fires or the deadline is missed. The due event id is recorded for
        /// diagnostics and correlation.
        /// </summary>
        public bool MarkWaitingOnPassiveProcess(EntityId taskId, string dueEventId, string reason, out string rejectionReason)
        {
            rejectionReason = null;
            WorkTask task = FindTask(taskId);
            if (!TryTransitionTo(task, TaskStatus.WaitingOnPassiveProcess, "wait on passive process", out rejectionReason))
            {
                return false;
            }

            if (ambientBudgetStore != null && task.HasAssignee && task.MinutesRemaining > 0)
            {
                ambientBudgetStore.GetOrCreate(task.AssigneeId).ReleaseCommitment(task.MinutesRemaining);
            }

            task.SetDueEventId(dueEventId);
            task.MarkWaitingOnPassiveProcess();
            return true;
        }

        /// <summary>
        /// Pass IV: InProgress -&gt; WaitingOnDependency. The assignment and reservations
        /// are held; the worker's budget commitment stays booked (they are still the
        /// worker on this task).
        /// </summary>
        public bool MarkWaitingOnDependency(EntityId taskId, string dependencyDescription, string reason, out string rejectionReason)
        {
            rejectionReason = null;
            WorkTask task = FindTask(taskId);
            if (!TryTransitionTo(task, TaskStatus.WaitingOnDependency, "wait on dependency", out rejectionReason))
            {
                return false;
            }

            task.SetDependencyDescription(dependencyDescription);
            task.MarkWaitingOnDependency();
            return true;
        }

        /// <summary>
        /// Pass IV: Failed -&gt; ReworkRequired. Terminal for this instance; the
        /// corrective task id is linked via FollowUpTaskIds. A specified corrective id
        /// must reference an existing task (no dangling links); an invalid/empty id
        /// means the corrective task will be created and linked later.
        /// </summary>
        public bool MarkReworkRequired(EntityId taskId, EntityId correctiveTaskId, string reason, out string rejectionReason)
        {
            rejectionReason = null;
            WorkTask task = FindTask(taskId);
            if (!TryTransitionTo(task, TaskStatus.ReworkRequired, "mark rework required", out rejectionReason))
            {
                return false;
            }

            if (correctiveTaskId.IsValid && FindTask(correctiveTaskId) == null)
            {
                rejectionReason = "Task " + taskId + ": corrective task " + correctiveTaskId + " does not exist.";
                return false;
            }

            task.AddFollowUpTask(correctiveTaskId);
            task.MarkReworkRequired();
            ReleaseTaskResources(task);
            return true;
        }

        /// <summary>
        /// Pass IV: releases every reservation owned by the task through the installed
        /// authority and cancels its scheduled due events. Idempotent; safe when no
        /// authority or scheduler is installed.
        /// </summary>
        public void ReleaseTaskReservations(EntityId taskId)
        {
            ReleaseTaskResources(FindTask(taskId));
        }

        /// <summary>Pass IV: finds a task by its EntityId string form (due-event payloads).</summary>
        public WorkTask FindTaskByIdString(string taskIdString)
        {
            RebuildLookupsIfNeeded();
            if (string.IsNullOrEmpty(taskIdString))
            {
                return null;
            }

            WorkTask task;
            return taskLookupByIdString.TryGetValue(taskIdString, out task) ? task : null;
        }

        /// <summary>Pass IV: every non-terminal task, for drivers that must not skip states.</summary>
        public List<WorkTask> GetAllOpenTasks()
        {
            RebuildLookupsIfNeeded();
            var open = new List<WorkTask>(tasks.Count);
            foreach (WorkTask task in tasks)
            {
                if (task != null && task.IsOpen)
                {
                    open.Add(task);
                }
            }

            return open;
        }

        /// <summary>
        /// Pass IV: sets per-instance physical inputs for method duration estimators
        /// (mass, distance, crew...). Used by domain code that knows the real inputs
        /// (e.g. freight quantity) before assignment runs the estimate.
        /// </summary>
        public bool SetTaskDurationInputs(EntityId taskId, TaskDurationInputs inputs, out string rejectionReason)
        {
            rejectionReason = null;
            WorkTask task = FindTask(taskId);
            if (task == null)
            {
                rejectionReason = "Unknown task: " + taskId;
                return false;
            }

            task.SetDurationInputsOverride(inputs);
            return true;
        }

        /// <summary>
        /// Pass IV: validates a transition against TaskLifecyclePolicy, refusing loudly.
        /// Task is re-fetched by the caller; a null task is an unknown-id refusal.
        /// </summary>
        private bool TryTransitionTo(WorkTask task, TaskStatus to, string action, out string rejectionReason)
        {
            rejectionReason = null;
            if (task == null)
            {
                rejectionReason = "Unknown task.";
                return false;
            }

            if (!TaskLifecyclePolicy.CanTransition(task.Status, to))
            {
                rejectionReason = "Task " + task.TaskId + ": cannot " + action + " — " +
                    task.Status + " -> " + to + " is not a legal lifecycle transition (TaskLifecyclePolicy).";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Pass IV: releases a task's reservations and cancels its due events. Called
        /// on every terminal transition when the seams are installed. Idempotent.
        /// </summary>
        private void ReleaseTaskResources(WorkTask task)
        {
            if (task == null)
            {
                return;
            }

            if (reservationAuthority != null)
            {
                reservationAuthority.ReleaseAllForTask(task.TaskId.ToString());
            }

            task.SetReservationHandleId(string.Empty);

            if (dueEventScheduler != null)
            {
                dueEventScheduler.CancelForPayload(task.TaskId.ToString());
            }

            task.SetDueEventId(string.Empty);
        }

        /// <summary>Pass IV: true for the five appended lifecycle states (never renumbered).</summary>
        private static bool IsPass4LifecycleState(TaskStatus status)
        {
            return status >= TaskStatus.Paused;
        }
    }
}
