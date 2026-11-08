using System;

namespace LandLedgers.Tasks
{
    /// <summary>
    /// Priority 4 Slice 1 — timing-observer + clock seam on TaskAuthority
    /// (plan 04-plan-task-duration-diagnostics.md §5 Slice 1).
    ///
    /// Additive only: both seams are null by default, no existing method signature
    /// changes, no scheduling behaviour changes, no <see cref="WorkTask"/> field
    /// is added. When no observer is installed, the clock is never read — the
    /// notification helper returns before consulting it. When an observer is
    /// installed, each notification passes the caller's existing
    /// <c>currentDayIndex</c> plus the clock reading (or -1 when no clock is
    /// installed); call sites that carry no day pass -1 and the service falls
    /// back to the record's last known day. The service never derives a day
    /// from seconds (plan §1.1).
    ///
    /// Notifications are fire-and-forget reads, issued only on success, after the
    /// transition has been applied: CreateTaskInternal, AssignTask, StartTask,
    /// RecordWork / RecordWorkProgress (progress notifications), CompleteTask,
    /// InterruptTask, CancelTask, PauseTask, ResumeTask,
    /// MarkWaitingOnPassiveProcess, MarkWaitingOnDependency, MarkResourcesReserved,
    /// MarkReworkRequired. A notification that throws propagates — the observer is
    /// diagnostic code under test, and silently swallowing its failure would hide
    /// a broken diagnostic; the service itself never calls back into this authority.
    ///
    /// Re-entrancy: while a notification is in flight, nested notifications are
    /// refused and counted on <see cref="TimingNotificationReentrancyRefusals"/>
    /// (never executed), so an observer that calls back into TaskAuthority cannot
    /// recurse the hook.
    /// </summary>
    public sealed partial class TaskAuthority
    {
        private ITaskTimingObserver timingObserver;
        private Func<float> timingClock;
        private bool timingNotificationInProgress;
        private int timingNotificationReentrancyRefusals;

        /// <summary>Installs the timing observer. Null uninstalls it (default: no observer).</summary>
        public void SetTimingObserver(ITaskTimingObserver observer)
        {
            timingObserver = observer;
        }

        /// <summary>
        /// Installs the clock supplier: a Func returning absolute game seconds on the
        /// authoritative simulation timeline (production: TimeManager.AbsoluteGameSeconds).
        /// Null restores day-only precision (default: no clock).
        /// </summary>
        public void SetTimingClock(Func<float> nowSeconds)
        {
            timingClock = nowSeconds;
        }

        /// <summary>True while a timing observer is installed.</summary>
        public bool HasTimingObserver
        {
            get { return timingObserver != null; }
        }

        /// <summary>Nested timing notifications refused by the re-entrancy guard so far.</summary>
        public int TimingNotificationReentrancyRefusals
        {
            get { return timingNotificationReentrancyRefusals; }
        }

        private void NotifyTimingObserver(WorkTask task, string transitionKind, int currentDayIndex)
        {
            // No observer: no clock read, no work at all — behaviour is identical
            // to the pre-hook authority.
            if (timingObserver == null || task == null)
            {
                return;
            }

            if (timingNotificationInProgress)
            {
                timingNotificationReentrancyRefusals++;
                return;
            }

            float nowSeconds = timingClock != null ? timingClock() : -1f;
            timingNotificationInProgress = true;
            try
            {
                timingObserver.OnTransition(task, transitionKind, nowSeconds, currentDayIndex);
            }
            finally
            {
                timingNotificationInProgress = false;
            }
        }
    }
}
