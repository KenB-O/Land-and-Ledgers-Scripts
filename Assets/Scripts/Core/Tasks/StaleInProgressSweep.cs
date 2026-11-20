// Plan 05 (2026-11-13, Slice 05): stale-InProgress sweep decision.
//
// Pure decision seam for SimulationDrivers.AdvanceAutonomousWork (the
// per-tick autonomous driver). An InProgress task whose reservation
// window has expired accrues zero scheduled minutes forever while
// holding its worker InProgress, and a zero-remaining InProgress task
// bricks its worker the same way. The sweep rescues the expired-window
// case in place with a fresh window (the F2 catch-up transplant from
// BeginScheduledBusinessPersonTask), completes the zero-remaining case
// honestly, and parks anything it cannot re-reserve with a diagnostic
// instead of completing or deleting it.
//
// Why this seam exists: SimulationDrivers.cs is harness-excluded, so
// the per-task decision lives here where the Roslyn harness can pin
// it, following the BusinessTaskStartWindow precedent. This helper
// takes no budget store, no tracker, no authority — it cannot commit,
// release, reserve, or complete anything by construction; all side
// effects stay in SimulationDrivers. The rescue deliberately does NOT
// use interrupt + re-queue: interrupt leaves the stale schedule
// reservation in place (InterruptTask releases resource reservations
// only), and the start funnel refuses a Queued task that still holds
// one. In-place re-derive never enters that state, holds the worker
// throughout (no double-occupancy window while the ResumeTask guard
// from Plan 04 is unlanded), and needs no Interruptible check.
//
// Decision order is load-bearing: freight skip, then arrival-driven
// travel skip, then zero-remaining completion before any staleness
// check (a 0-remaining task must complete even with a valid window,
// because re-deriving [now, now+0] would accrue Min(0, overlap) = 0
// and leave it bricked), then staleness, then day-fit deferral.
using System;
using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;

namespace LandLedgers.Tasks
{
    public enum StaleInProgressDecision
    {
        /// <summary>The task's window is still valid (or starts later today): accrue normally, untouched.</summary>
        UntouchedHealthy,

        /// <summary>The window expired; release it and re-derive [now, now+remaining] in place.</summary>
        RescueReDerive,

        /// <summary>The window expired but the remainder does not fit today: leave in place, retry tomorrow.</summary>
        DeferNoFitToday,

        /// <summary>All minutes are already on the record: complete honestly through the existing path.</summary>
        CompleteZeroRemaining,

        /// <summary>Not this sweep's task (freight, arrival-driven travel): leave entirely alone.</summary>
        Skip
    }

    public readonly struct StaleInProgressVerdict
    {
        public readonly StaleInProgressDecision Decision;

        /// <summary>RescueReDerive only: the activity to reserve the fresh window under.</summary>
        public readonly PersonActivityKind ActivityToReserve;

        /// <summary>Diagnostic string for the caller's log line.</summary>
        public readonly string Reason;

        public StaleInProgressVerdict(
            StaleInProgressDecision decision,
            PersonActivityKind activityToReserve,
            string reason)
        {
            Decision = decision;
            ActivityToReserve = activityToReserve;
            Reason = reason ?? string.Empty;
        }
    }

    public static class StaleInProgressSweep
    {
        public static StaleInProgressVerdict Decide(
            WorkTask task,
            IReadOnlyList<PersonActivityReservation> reservations,
            int dayIndex,
            int minuteOfDay,
            string taskReservationLabel,
            bool isArrivalDrivenTravel)
        {
            // Preserve the point-in-time seam for callers without an elapsed
            // interval. The live driver supplies the full interval below so
            // in-tick work is not discarded by an end-boundary check.
            return Decide(task, reservations, dayIndex,
                Math.Max(0, minuteOfDay - 1), minuteOfDay,
                taskReservationLabel, isArrivalDrivenTravel);
        }

        public static StaleInProgressVerdict Decide(
            WorkTask task,
            IReadOnlyList<PersonActivityReservation> reservations,
            int dayIndex,
            int tickStartMinute,
            int tickEndMinute,
            string taskReservationLabel,
            bool isArrivalDrivenTravel)
        {
            if (task == null)
            {
                return new StaleInProgressVerdict(
                    StaleInProgressDecision.Skip, PersonActivityKind.Work, "no task to sweep");
            }

            // 1. Freight is Agent 2's (QA-01): freight load/unload tasks are
            // owned by literal business id 3, never reserve schedule windows,
            // and accrue via WorkLoading/WorkUnloading, not
            // AdvanceAutonomousWork. Never touch them here, even if one ever
            // entered the autonomous iteration set. The predicate lives in
            // FreightHandlingTaskGuard (shared with the AdvanceAutonomousWork
            // accrual skip and the TryStartBusinessPersonTask start refusal)
            // so the three guards can never rot apart; it has two
            // independent clauses (owner, then definition id) — either
            // clause alone skips the task.
            if (FreightHandlingTaskGuard.IsFreightHandlingTask(task))
            {
                return new StaleInProgressVerdict(
                    StaleInProgressDecision.Skip, PersonActivityKind.Work,
                    "freight task '" + task.DefinitionId + "' is outside the autonomous sweep");
            }

            // Plan 07 (Slice 07): staged production labour tasks are
            // owned by the stage machine, not the autonomous sweep. A
            // staged task sits InProgress with NO schedule-window
            // reservation, so the sweep would either RescueReDerive it
            // (creating a window that makes AdvanceAutonomousWork accrue
            // it alongside the chain drive: double-counted person-time)
            // or CompleteZeroRemaining it the moment its MinutesRemaining
            // hits 0 (the tending path records minutes without checking
            // completion, so a carried task can reach 0 while the stage
            // machine still owns it — and the stage machine would then
            // refuse every further WorkActiveStage, bricking the batch).
            // The stage machine completes staged tasks itself at stage
            // completion. Literals only, no dependency on the chain
            // classes from this Core/Tasks helper (slice 06 precedent).
            if (task.DefinitionId != null
                && task.DefinitionId.StartsWith("generic-production:", StringComparison.Ordinal))
            {
                return new StaleInProgressVerdict(
                    StaleInProgressDecision.Skip, PersonActivityKind.Work,
                    "staged production task '" + task.DefinitionId + "' is owned by the stage machine, outside the autonomous sweep");
            }

            // Arrival-driven travel completes on actor arrival via
            // ResolveArrivalDrivenTravelTasks, not on its window.
            if (isArrivalDrivenTravel)
            {
                return new StaleInProgressVerdict(
                    StaleInProgressDecision.Skip, PersonActivityKind.Work,
                    "arrival-driven travel task completes on arrival, not on its window");
            }

            // 2. Zero remaining first, before any staleness check: the work
            // is fully on the record (MinutesWorked >= PlannedMinutes), so
            // this is completing with real work, not without it.
            if (task.MinutesRemaining <= 0)
            {
                return new StaleInProgressVerdict(
                    StaleInProgressDecision.CompleteZeroRemaining, PersonActivityKind.Work,
                    "task '" + task.TaskId + "' has 0 minutes remaining; work is fully recorded");
            }

            // 3. Staleness is evaluated against the interval about to be
            // accrued, not merely its end. A reservation can end before
            // tickEnd while still covering real work earlier in the tick.
            // Releasing it at the end boundary would discard that overlap on
            // every accelerated tick and keep the task permanently open.
            // Released reservations are invisible, same as no window.
            PersonActivityReservation stale = null;
            if (reservations != null)
            {
                for (int i = 0; i < reservations.Count; i++)
                {
                    PersonActivityReservation reservation = reservations[i];
                    if (reservation == null || reservation.Released) continue;
                    if (reservation.DayIndex != dayIndex) continue;
                    if (reservation.PersonId != task.AssigneeId.Id) continue;
                    if (!string.Equals(reservation.Label, taskReservationLabel, StringComparison.Ordinal)) continue;
                    if (reservation.EndMinuteOfDay > tickStartMinute)
                    {
                        return new StaleInProgressVerdict(
                            StaleInProgressDecision.UntouchedHealthy, PersonActivityKind.Work,
                            "task '" + task.TaskId + "' holds a live window ending at "
                            + reservation.EndMinuteOfDay);
                    }

                    // Keep the most recently ended stale window: its
                    // activity is the best guide for the fresh window.
                    if (stale == null || reservation.EndMinuteOfDay > stale.EndMinuteOfDay)
                    {
                        stale = reservation;
                    }
                }
            }

            // 4. Day-fit, exactly as the F2 catch-up does: if the remainder
            // does not fit before midnight, defer and retry tomorrow at a
            // small tickEndMinute. The stale window stays in place, harmless
            // — it ends before now, so it can only collide with windows
            // starting in the past.
            int remaining = Math.Max(1, task.MinutesRemaining);
            if (tickEndMinute + remaining > 1440)
            {
                return new StaleInProgressVerdict(
                    StaleInProgressDecision.DeferNoFitToday, PersonActivityKind.Work,
                    "task '" + task.TaskId + "' needs " + remaining
                    + " minutes but only " + (1440 - tickEndMinute) + " remain today");
            }

            // 5. Rescue in place. Activity is the stale reservation's when
            // one exists (the F2 transplant uses active.Activity); when no
            // matching reservation exists at all, fall back to the start-path
            // call sites: Travel for journey travel, Shopping for feed-trip
            // and freight-meeting customer refs, Work otherwise.
            PersonActivityKind activity = stale != null
                ? stale.Activity
                : FallbackActivity(task);
            return new StaleInProgressVerdict(
                StaleInProgressDecision.RescueReDerive, activity,
                "task '" + task.TaskId + "' window expired; re-derive [" + tickEndMinute
                + ", " + (tickEndMinute + remaining) + "] as " + activity);
        }

        private static PersonActivityKind FallbackActivity(WorkTask task)
        {
            // JourneyTravel.TravelTaskId ("travel"); start sites reserve
            // Travel for it (SimulationDrivers :3766, :5635, :5805).
            if (task.DefinitionId == JourneyTravel.TravelTaskId)
            {
                return PersonActivityKind.Travel;
            }

            // BusinessFeedTaskPrefix "business-feed-trip:" (start site
            // :4400) and FreightMeetingTaskPrefix "freight-meeting:"
            // (start site :4700) reserve Shopping.
            if (!string.IsNullOrEmpty(task.CustomerRef)
                && (task.CustomerRef.StartsWith("business-feed-trip:", StringComparison.Ordinal)
                    || task.CustomerRef.StartsWith("freight-meeting:", StringComparison.Ordinal)))
            {
                return PersonActivityKind.Shopping;
            }

            // Hitch/dispatch start site (:5763) reserves Work.
            return PersonActivityKind.Work;
        }
    }

    /// <summary>
    /// A completion due event is a wake-up/backstop, not evidence that work
    /// occurred. Only minutes recorded by the task authority may authorize
    /// the due-event completion path.
    /// </summary>
    public static class TaskCompletionDuePolicy
    {
        public static bool CanCompleteFromDueEvent(WorkTask task)
        {
            return task != null
                && task.Status == TaskStatus.InProgress
                && task.MinutesRemaining <= 0;
        }
    }
}
