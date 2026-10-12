using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Tasks
{
    /// <summary>
    /// Pass IV (Living World Foundation): extended task lifecycle policy.
    ///
    /// The serialized state enum remains TaskStatus (TaskModels.cs) — its values are
    /// NEVER renumbered. Pass IV appends five states there (Paused, WaitingOnPassiveProcess,
    /// WaitingOnDependency, ResourcesReserved, ReworkRequired). This class documents the
    /// allowed transitions and the meaning of each state so all workers implement one
    /// lifecycle, not four.
    ///
    /// Canon: XXXI Part III §§3.1-3.7 (universal production, task execution).
    /// Technical: XIV Part II §§2.1-2.8 (task outcome resolution, reservations).
    /// </summary>
    public static class TaskLifecyclePolicy
    {
        /// <summary>
        /// Allowed transitions. Anything not listed here is refused loudly by callers.
        /// Terminal states (Complete, Failed, Cancelled) have no outbound transitions;
        /// ReworkRequired is terminal for THIS instance but its follow-up linkage spawns
        /// the corrective task (see WorkTask.FollowUpTaskIds).
        /// </summary>
        public static bool CanTransition(TaskStatus from, TaskStatus to)
        {
            switch (from)
            {
                case TaskStatus.Queued:
                    return to == TaskStatus.Assigned || to == TaskStatus.Cancelled;
                case TaskStatus.Assigned:
                    return to == TaskStatus.ResourcesReserved
                        || to == TaskStatus.InProgress
                        || to == TaskStatus.Queued
                        || to == TaskStatus.Cancelled;
                case TaskStatus.ResourcesReserved:
                    return to == TaskStatus.InProgress
                        || to == TaskStatus.Assigned
                        || to == TaskStatus.Cancelled;
                case TaskStatus.InProgress:
                    return to == TaskStatus.Paused
                        || to == TaskStatus.WaitingOnPassiveProcess
                        || to == TaskStatus.WaitingOnDependency
                        || to == TaskStatus.Complete
                        || to == TaskStatus.Failed
                        || to == TaskStatus.Interrupted
                        || to == TaskStatus.Cancelled;
                case TaskStatus.Paused:
                    return to == TaskStatus.InProgress
                        || to == TaskStatus.Interrupted
                        || to == TaskStatus.Cancelled;
                case TaskStatus.WaitingOnPassiveProcess:
                    return to == TaskStatus.InProgress
                        || to == TaskStatus.Failed
                        || to == TaskStatus.Cancelled;
                case TaskStatus.WaitingOnDependency:
                    return to == TaskStatus.InProgress
                        || to == TaskStatus.Queued
                        || to == TaskStatus.Cancelled;
                case TaskStatus.Interrupted:
                    return to == TaskStatus.Queued;
                case TaskStatus.Failed:
                    return to == TaskStatus.ReworkRequired;
                default:
                    return false;
            }
        }

        /// <summary>
        /// States in which the assigned worker's person-time is still committed.
        /// WaitingOnPassiveProcess releases the WORKER but not the equipment/workspace/materials.
        /// </summary>
        public static bool HoldsWorkerCommitment(TaskStatus status)
        {
            switch (status)
            {
                case TaskStatus.Assigned:
                case TaskStatus.ResourcesReserved:
                case TaskStatus.InProgress:
                case TaskStatus.Paused:
                case TaskStatus.WaitingOnDependency:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// States in which equipment/workspace/material reservations are still held.
        /// </summary>
        public static bool HoldsResourceReservations(TaskStatus status)
        {
            switch (status)
            {
                case TaskStatus.ResourcesReserved:
                case TaskStatus.InProgress:
                case TaskStatus.Paused:
                case TaskStatus.WaitingOnPassiveProcess:
                case TaskStatus.WaitingOnDependency:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Human-readable meaning of each lifecycle state, for read models and diagnostics.</summary>
        public static string Describe(TaskStatus status)
        {
            switch (status)
            {
                case TaskStatus.Queued: return "Waiting for a worker and prerequisites.";
                case TaskStatus.Assigned: return "Worker assigned; work-time budget committed.";
                case TaskStatus.ResourcesReserved: return "Equipment, workspace and materials reserved; work not yet started.";
                case TaskStatus.InProgress: return "Active work in progress.";
                case TaskStatus.Paused: return "Work suspended; assignment and reservations held.";
                case TaskStatus.WaitingOnPassiveProcess: return "Passive process running; worker released, equipment/materials still occupied.";
                case TaskStatus.WaitingOnDependency: return "Blocked on another task, delivery, or person.";
                case TaskStatus.Complete: return "Finished; outcome recorded.";
                case TaskStatus.Interrupted: return "Assignment released; work preserved for reassignment.";
                case TaskStatus.Failed: return "Terminal failure; see outcome.";
                case TaskStatus.ReworkRequired: return "Failed work needs a corrective task; see follow-ups.";
                case TaskStatus.Cancelled: return "Cancelled; consumed materials stay consumed, unused reservations released.";
                default: return status.ToString();
            }
        }
    }

    /// <summary>
    /// Pass IV: task identity and lineage conventions. Every consequential task carries
    /// stable identity (WorkTask.TaskId, EntityKind.WorkTask). Related records reference
    /// that id — never a second generated key for the same task.
    ///
    /// Deterministic ids where the domain needs them (e.g. "task-" + 32 hex for fixtures);
    /// runtime ids come from EntityIdRegistry. Never invent identifiers Kennedy or a tool
    /// did not supply.
    /// </summary>
    public static class TaskIdentityConventions
    {
        public const string TaskIdPrefix = "task-";

        /// <summary>Reservation owner key for a task: the task EntityId rendered as string.</summary>
        public static string ReservationOwnerKey(string taskEntityIdString)
        {
            return "task:" + (taskEntityIdString ?? string.Empty);
        }
    }
}
