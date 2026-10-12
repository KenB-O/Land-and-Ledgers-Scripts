using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Time;

namespace LandLedgers.Tasks
{
    /// <summary>
    /// Pass IV (Worker 1): per-tick progress driver for player-issued tasks.
    ///
    /// PlayerDirector-issued tasks reach InProgress through the shared TaskAuthority
    /// but previously had no per-tick driver, so they stalled there forever. This
    /// driver extends the AdvanceAutonomousWork pattern: every InProgress task
    /// assigned to the player person advances in real time through
    /// RecordWorkProgress (budget accounting included — the player is never exempt).
    /// Tasks that reach completion resolve through the supplied outcome resolver;
    /// without one they rest at 0 remaining until a resolver is provided.
    ///
    /// Plain C# — no Unity dependencies; the SimulationDrivers wire the causal
    /// outcome resolver, mirroring the autonomous path.
    /// </summary>
    public sealed class PlayerTaskProgressDriver
    {
        private readonly TaskAuthority tasks;
        private readonly WorkTimeBudgetStore budgets;

        /// <summary>Resolves the outcome for a task that reached its plan. May be null.</summary>
        public delegate TaskOutcome OutcomeResolver(WorkTask task, int dayIndex);

        public PlayerTaskProgressDriver(TaskAuthority tasks, WorkTimeBudgetStore budgets)
        {
            this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
            this.budgets = budgets;
        }

        /// <summary>
        /// Advances every InProgress task assigned to the player person by wholeMinutes
        /// of real time. Returns the minutes actually applied. Diagnostics name any
        /// task that refused progress loudly instead of stalling silently.
        /// </summary>
        public int AdvancePlayerTasks(
            EntityId playerPersonId,
            int wholeMinutes,
            int dayIndex,
            OutcomeResolver resolveOutcome,
            List<string> diagnostics)
        {
            if (playerPersonId.Kind != EntityKind.Person || wholeMinutes <= 0)
            {
                return 0;
            }

            int applied = 0;
            foreach (WorkTask task in tasks.GetAllOpenTasks())
            {
                if (task == null || task.Status != TaskStatus.InProgress || !task.HasAssignee)
                {
                    continue;
                }

                if (task.AssigneeId != playerPersonId)
                {
                    continue;
                }

                string rejectionReason;
                bool reachedCompletion;
                if (!tasks.RecordWorkProgress(task.TaskId, wholeMinutes, budgets, dayIndex, out reachedCompletion, out rejectionReason))
                {
                    diagnostics?.Add("player task " + task.TaskId + ": progress refused (" + rejectionReason + ")");
                    continue;
                }

                applied += wholeMinutes;

                if (reachedCompletion)
                {
                    if (resolveOutcome != null)
                    {
                        TaskOutcome outcome = resolveOutcome(task, dayIndex);
                        if (!tasks.CompleteTask(task.TaskId, outcome, dayIndex, budgets))
                        {
                            diagnostics?.Add("player task " + task.TaskId + ": completion refused after reaching plan");
                        }
                    }
                    else
                    {
                        diagnostics?.Add("player task " + task.TaskId + ": reached plan with no outcome resolver — left InProgress");
                    }
                }
            }

            return applied;
        }
    }
}
