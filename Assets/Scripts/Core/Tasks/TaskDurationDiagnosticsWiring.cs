using System;

namespace LandLedgers.Tasks
{
    /// <summary>
    /// Priority 4 Slice 4 — the harness-reachable half of the driver wiring
    /// (plan 04-plan-task-duration-diagnostics.md §5 Slice 4).
    ///
    /// The hub (SimulationSystemsHub, harness-excluded) calls
    /// <see cref="Install"/> with the production clock
    /// (<c>() =&gt; timeManager.AbsoluteGameSeconds</c>) and the day drive
    /// (SimulationDrivers.OnDayChanged, harness-excluded) calls
    /// <see cref="TaskDurationDiagnostics.Evaluate"/> once per day on the
    /// returned service. Those call sites are source-reviewed only; this
    /// helper is the piece the harness CAN prove: installing the observer
    /// + clock on an authority, with the log sink defaulting to
    /// <see cref="UnityEngine.Debug.LogWarning"/> (production) and settable
    /// for tests.
    ///
    /// Install is pure wiring: it changes no scheduling behaviour (the
    /// observer only reads transitions, Slice 1) and adds no log emission
    /// beyond what Evaluate already does (Slice 3). A null authority
    /// installs nothing (returns null); a null clock installs the observer
    /// in day-only precision (Slice 1 degraded mode, explicit, never
    /// silent). Each call installs a fresh service; the hub guards
    /// idempotency (install once, reuse the service).
    /// </summary>
    public static class TaskDurationDiagnosticsWiring
    {
        public static TaskDurationDiagnostics Install(
            TaskAuthority authority, Func<float> nowSeconds, Action<string> logSink = null)
        {
            if (authority == null)
            {
                return null;
            }

            var diagnostics = new TaskDurationDiagnostics();
            if (logSink != null)
            {
                diagnostics.LogSink = logSink;
            }

            authority.SetTimingObserver(diagnostics);
            authority.SetTimingClock(nowSeconds);
            return diagnostics;
        }
    }
}
