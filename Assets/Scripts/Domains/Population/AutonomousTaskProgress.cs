using System.Collections.Generic;
using LandLedgers.Tasks;
using LandLedgers.World.Journeys;

namespace LandLedgers.Population
{
    /// <summary>
    /// Computes the part of an autonomous task's plan covered by its real
    /// reserved person-time window. Physical-arrival travel tasks are handled
    /// separately by their navigation authority; route-time travel tasks use
    /// the Travel reservation that was created for their JourneyModel estimate.
    /// </summary>
    public static class AutonomousTaskProgress
    {
        public static int ScheduledMinutes(
            WorkTask task,
            IReadOnlyList<PersonActivityReservation> reservations,
            int dayIndex,
            int tickStartMinute,
            int tickEndMinute,
            string taskReservationLabel)
        {
            if (task == null || task.Status != TaskStatus.InProgress || !task.HasAssignee
                || reservations == null || string.IsNullOrEmpty(taskReservationLabel)
                || tickEndMinute <= tickStartMinute)
            {
                return 0;
            }

            bool routeTimeTravel = task.DefinitionId == JourneyTravel.TravelTaskId;
            int scheduledMinutes = 0;
            for (int i = 0; i < reservations.Count; i++)
            {
                PersonActivityReservation reservation = reservations[i];
                if (reservation == null || reservation.Released || reservation.DayIndex != dayIndex
                    || reservation.PersonId != task.AssigneeId.Id
                    || !string.Equals(reservation.Label, taskReservationLabel, System.StringComparison.Ordinal))
                {
                    continue;
                }

                bool eligibleActivity = routeTimeTravel
                    ? reservation.Activity == PersonActivityKind.Travel
                    : reservation.Activity == PersonActivityKind.Work
                        || reservation.Activity == PersonActivityKind.Shopping;
                if (!eligibleActivity) continue;

                int overlapStart = System.Math.Max(tickStartMinute, reservation.StartMinuteOfDay);
                int overlapEnd = System.Math.Min(tickEndMinute, reservation.EndMinuteOfDay);
                scheduledMinutes = System.Math.Max(scheduledMinutes, overlapEnd - overlapStart);
            }

            return System.Math.Min(task.MinutesRemaining, System.Math.Max(0, scheduledMinutes));
        }
    }
}
