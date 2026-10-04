using EntityId = LandLedgers.Primitives.EntityId;

using System;
using UnityEngine;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;

namespace LandLedgers.Orchestration.Player
{
    /// <summary>
    /// TTS-4: script-side authority for the player. The player is a Person in the
    /// player household (HF-3 DeclarePlayerHousehold) and performs tasks through
    /// the SAME <see cref="TaskAuthority"/> as NPCs — no separate player logic.
    ///
    /// The director issues movement/task intents, routes movement so travel takes
    /// time like everything else, and books player minutes against the same
    /// TTS-1 work-time budget as NPCs. It works headless (avatar == null, e.g. in
    /// tests); the Unity side supplies an <see cref="IPlayerAvatar"/> that the
    /// director notifies of each intent.
    ///
    /// Travel timing is currently a flat per-route minute estimate booked against
    /// the budget. When the location/journey model lands, intents feed it instead —
    /// the director API does not change.
    /// </summary>
    [Serializable]
    public sealed class PlayerDirector
    {
        [SerializeField]
        private EntityId playerPersonId;

        [SerializeField]
        private string currentLocationId;

        [SerializeField]
        private EntityId currentTaskId;

        [SerializeField]
        private bool travelling;

        [SerializeField]
        private string travelDestination;

        [SerializeField]
        private int travelMinutesTotal = 1;

        [SerializeField]
        private int travelMinutesElapsed;

        public EntityId PlayerPersonId => playerPersonId;
        public string CurrentLocationId => currentLocationId;
        public bool IsTravelling => travelling;
        public EntityId CurrentTaskId => currentTaskId;
        public bool HasTask => currentTaskId.Kind != EntityKind.Unspecified;
        public bool IsBusy => travelling || HasTask;
        public int TravelMinutesRemaining => travelling ? Math.Max(0, travelMinutesTotal - travelMinutesElapsed) : 0;

        public PlayerDirector()
        {
        }

        public PlayerDirector(EntityId playerPersonId, string startingLocationId)
        {
            if (playerPersonId.Kind != EntityKind.Person)
            {
                throw new ArgumentException("PlayerDirector drives a Person.", nameof(playerPersonId));
            }

            this.playerPersonId = playerPersonId;
            currentLocationId = startingLocationId ?? string.Empty;
        }

        /// <summary>
        /// Issues a movement intent: the player travels from place to place, taking
        /// real minutes booked against their work-time budget. Fails with a reason
        /// when the player is busy or the budget cannot cover the trip.
        /// </summary>
        public bool TryIssueMoveIntent(string destinationLocationId, int travelMinutes, WorkTimeBudgetStore budgets, IPlayerAvatar avatar, out string rejectionReason)
        {
            rejectionReason = null;
            if (string.IsNullOrEmpty(destinationLocationId))
            {
                rejectionReason = "Move intent requires a destination.";
                return false;
            }

            if (IsBusy)
            {
                rejectionReason = "Player is busy (" + DescribeActivity() + "); cancel first.";
                return false;
            }

            int wholeMinutes = WorkTimeMath.ClampToWholeMinutes(travelMinutes);
            string budgetReason;
            if (budgets != null && !budgets.TryCommitTask(playerPersonId, wholeMinutes, out budgetReason))
            {
                rejectionReason = "Cannot travel: " + budgetReason;
                return false;
            }

            travelling = true;
            travelDestination = destinationLocationId;
            travelMinutesTotal = wholeMinutes;
            travelMinutesElapsed = 0;

            if (avatar != null)
            {
                avatar.ExecuteMove(destinationLocationId, wholeMinutes);
            }

            return true;
        }

        /// <summary>
        /// Advances travel as the clock runs. Call from the game loop with the whole
        /// minutes elapsed since the last call. Arrival updates the logical location.
        /// </summary>
        public void RecordMovementProgress(int minutes, WorkTimeBudgetStore budgets)
        {
            if (!travelling)
            {
                return;
            }

            int wholeMinutes = WorkTimeMath.ClampToWholeMinutes(minutes);
            travelMinutesElapsed += wholeMinutes;
            if (budgets != null)
            {
                budgets.GetOrCreate(playerPersonId).RecordWorked(wholeMinutes);
            }

            if (travelMinutesElapsed >= travelMinutesTotal)
            {
                travelling = false;
                currentLocationId = travelDestination;
                travelDestination = null;
                travelMinutesElapsed = 0;
                travelMinutesTotal = 1;
            }
        }

        /// <summary>
        /// Issues a task intent: assigns the player to a queued task and starts it,
        /// through the shared task authority. Budget booking follows the normal
        /// assignment rules — the player is never exempt.
        /// </summary>
        public bool TryIssueTaskIntent(EntityId taskId, TaskAuthority authority, WorkTimeBudgetStore budgets, int currentDayIndex, IPlayerAvatar avatar, out string rejectionReason)
        {
            rejectionReason = null;
            if (authority == null)
            {
                rejectionReason = "Task authority is required.";
                return false;
            }

            if (IsBusy)
            {
                rejectionReason = "Player is busy (" + DescribeActivity() + "); cancel first.";
                return false;
            }

            if (!authority.AssignTask(taskId, playerPersonId, budgets, out rejectionReason))
            {
                return false;
            }

            if (!authority.StartTask(taskId, currentDayIndex, out rejectionReason))
            {
                authority.CancelTask(taskId, budgets);
                return false;
            }

            currentTaskId = taskId;
            if (avatar != null)
            {
                avatar.ExecuteTask(taskId);
            }

            return true;
        }

        /// <summary>
        /// Cancels whatever the player is doing. A task is interrupted through the
        /// authority (work preserved, remainder released); travel stops with its
        /// unspent minutes released back to the budget.
        /// </summary>
        public bool CancelCurrent(TaskAuthority authority, WorkTimeBudgetStore budgets, IPlayerAvatar avatar)
        {
            bool didAnything = false;

            if (HasTask && authority != null)
            {
                string ignored;
                WorkTask task = authority.FindTask(currentTaskId);
                if (task != null && task.IsOpen)
                {
                    authority.InterruptTask(currentTaskId, budgets, out ignored);
                }

                currentTaskId = default(EntityId);
                didAnything = true;
            }

            if (travelling)
            {
                if (budgets != null)
                {
                    budgets.GetOrCreate(playerPersonId).ReleaseCommitment(TravelMinutesRemaining);
                }

                travelling = false;
                travelDestination = null;
                travelMinutesElapsed = 0;
                travelMinutesTotal = 1;
                didAnything = true;
            }

            if (didAnything && avatar != null)
            {
                avatar.ExecuteCancel();
            }

            return didAnything;
        }

        /// <summary>Budget minutes the player still has available today.</summary>
        public int BudgetMinutesRemaining(WorkTimeBudgetStore budgets)
        {
            if (budgets == null)
            {
                return 0;
            }

            return budgets.GetOrCreate(playerPersonId).MinutesRemaining;
        }

        private string DescribeActivity()
        {
            if (travelling)
            {
                return "travelling to " + travelDestination;
            }

            if (HasTask)
            {
                return "working task " + currentTaskId;
            }

            return "idle";
        }
    }
}
