// Slice F2 (2026-11-06, Objectives 18 + 45): business-task start-window
// decision.
//
// Pure decision seam for SimulationDrivers.BeginScheduledBusinessPersonTask
// (the shared funnel through which every business-person task — hitch,
// dispatch, feed, meeting, staging travel — starts). ShortTick callbacks
// execute at the interval end, so a reservation that has expired by that
// boundary must be caught up and re-reserved before StartTask. Overlap with
// an earlier part of the interval is not enough. This decision is kept here
// so the Roslyn harness can pin it (SimulationDrivers.cs is harness-excluded).
//
// Why this seam exists: ShortTick windows are 15 game-minutes wide
// (SimulationTimeSettings.shortTickIntervalGameMinutes) and every tick
// fired in one frame reports the same TimeOfDay01 (TimeManager.
// ProcessShortTicks does not advance the clock inside its burst), so at
// 100x — one game day per 0.6 real seconds, ~40 game-minutes per frame —
// consecutive evaluated tick windows are ~40 minutes apart with ~25-minute
// gaps between them. A short reservation window (10-minute hitch, 1-minute
// dispatch) that falls in a gap is never overlapped: the task stays
// Assigned, the day-scoped reservation pins its start minute in the past
// for the rest of the day (TryStartBusinessPersonTask's alreadyScheduled
// branch never re-derives it), and the next day runs the same lottery.
// That is the verified Nov 6 console mechanism: T44 assigned day 6,
// started day 16; T168 created day 16, started day 19. At 1x the tick
// windows tile the day contiguously, so the defect does not reproduce
// there; it appears once the per-frame advance exceeds the 15-minute tick
// width (from ~50x up).
//
// The decision has three outcomes: Wait (the tick boundary precedes the
// reservation), Start (the reservation remains live at the callback
// boundary), CatchUp (the reservation has ended by the callback boundary —
// start late from now, re-reserving the actual work window).
// No budget, duration, threshold, identity, fatigue, or time-conservation
// rule is weakened by any outcome: a catch-up start still consumes the
// task's committed budget minutes against a freshly reserved, exclusive
// window, and AdvanceAutonomousWork only accrues progress inside that
// window.
namespace LandLedgers.Tasks
{
    public enum BusinessTaskStartDecision
    {
        /// <summary>The reservation window has not been reached yet.</summary>
        Wait,

        /// <summary>The tick window overlaps the reservation window.</summary>
        Start,

        /// <summary>
        /// The reservation window has fully passed without a start. The
        /// caller re-reserves from now and starts late rather than leaving
        /// the task Assigned forever.
        /// </summary>
        CatchUp
    }

    public static class BusinessTaskStartWindow
    {
        /// <summary>
        /// Decide what a ShortTick should do with an Assigned task's
        /// reservation window. All minutes are minutes-of-day; callbacks
        /// execute at tickEndMinute after the [tickStartMinute,
        /// tickEndMinute] interval has elapsed. Only a reservation still
        /// live at that boundary can be started directly.
        /// </summary>
        public static BusinessTaskStartDecision Resolve(
            int reservationStartMinute,
            int reservationEndMinute,
            int tickStartMinute,
            int tickEndMinute)
        {
            // The reservation start is inclusive. A callback exactly at
            // that boundary can begin the task without using unreserved
            // time; only a callback strictly before it must wait.
            if (tickEndMinute < reservationStartMinute)
            {
                return BusinessTaskStartDecision.Wait;
            }

            // If the reservation ended anywhere within the interval,
            // including exactly at tickEndMinute, starting now would begin
            // outside the reserved time. Catch up from the actual boundary.
            if (tickEndMinute >= reservationEndMinute)
            {
                return BusinessTaskStartDecision.CatchUp;
            }

            return BusinessTaskStartDecision.Start;
        }
    }

    /// <summary>
    /// Classifies a task-start refusal caused by another active task. These
    /// refusals preserve exclusive person occupancy and are routinely
    /// recoverable when the current task releases the person; callers should
    /// report them as deferred work rather than as a failed operation.
    /// </summary>
    public static class BusinessTaskStartRefusal
    {
        public static bool IsTemporaryOccupancyConflict(string refusal)
        {
            return !string.IsNullOrWhiteSpace(refusal)
                && refusal.IndexOf(" is already occupied by task ", System.StringComparison.Ordinal) >= 0;
        }
    }
}
