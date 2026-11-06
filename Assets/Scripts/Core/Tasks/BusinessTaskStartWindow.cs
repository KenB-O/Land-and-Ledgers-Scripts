// Slice F2 (2026-11-06, Objectives 18 + 45): business-task start-window
// decision.
//
// Pure decision seam for SimulationDrivers.BeginScheduledBusinessPersonTask
// (the shared funnel through which every business-person task — hitch,
// dispatch, feed, meeting, staging travel — starts). The funnel starts a
// task only when the current ShortTick window overlaps the task's
// reservation window; that overlap rule is the whole decision, and it is
// kept here so the Roslyn harness can pin it (SimulationDrivers.cs is
// harness-excluded).
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
// The decision has three outcomes: Wait (window not reached yet),
// Start (tick window overlaps the reservation window), CatchUp (the
// reservation window has fully passed without a start — start late from
// now, re-reserving the actual work window, rather than never starting).
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
        /// reservation window. All minutes are minutes-of-day; the tick
        /// window is [tickStartMinute, tickEndMinute] (15 game-minutes wide
        /// at the authored tick interval) and the reservation window is
        /// [reservationStartMinute, reservationEndMinute].
        /// </summary>
        public static BusinessTaskStartDecision Resolve(
            int reservationStartMinute,
            int reservationEndMinute,
            int tickStartMinute,
            int tickEndMinute)
        {
            // Window not reached yet: wait for a later tick.
            if (tickEndMinute <= reservationStartMinute)
            {
                return BusinessTaskStartDecision.Wait;
            }

            // Window fully passed without a start: the tick windows jumped
            // clean over it (the 100x stall). Catch up — start late from
            // now with a re-reserved window — rather than waiting out the
            // day on a reservation whose window is already in the past.
            if (tickStartMinute >= reservationEndMinute)
            {
                return BusinessTaskStartDecision.CatchUp;
            }

            return BusinessTaskStartDecision.Start;
        }
    }
}
