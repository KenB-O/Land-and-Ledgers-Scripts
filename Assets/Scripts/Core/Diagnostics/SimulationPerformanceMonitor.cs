// Land & Ledgers — Simulation Performance and Memory Monitor core (Tool 09, Lane 7).
//
// Tick cost, memory pressure — with the same honesty rules as the rest
// of the pass: report what was measured, never claim a Unity profile
// run here. No run exists on this HEAD; the only measured throughput in
// evidence is the retained 60-day run's DEF-061 lines (250x requested
// 15,000 game-s/real-s vs achieved 2,712–32,573, wildly variable).
//
// Throughput is computed from the runner's own fields
// (Playtest60DayRunCoordinator.RunSession: wallStartUtc, wallEndUtc,
// completedDays, targetDays, selectedMultiplier): simulated days per
// minute, wall seconds per simulated day, requested vs achieved
// game-seconds per real second. Requested rate is multiplier × 60
// game-s/real-s (1x = 60: one game minute per real second; 250x =
// 15,000, per the DEF-061 lines). The achieved/ requested ratio is
// reported; the "step down one speed preset" suggestion is a diagnostic
// heuristic (documented as such), not a Canon rule. The live-clock
// theory is REFUTED (Agent G R13: no DateTime.Now / UtcNow /
// realtimeSinceStartup anywhere in production Assets/Scripts), so
// DEF-061 needs re-scoping, not a clock fix — this monitor re-scopes
// it as measured throughput, and never blames a clock.
//
// Memory is measured, never equated: GC.GetTotalMemory is the MANAGED
// HEAP, Process working set is the PROCESS WORKING SET — neither is
// physical RAM, and this file never claims process private memory =
// physical RAM. The binding constraint is RAM per the Nov 19 audits
// (free RAM 0.25–3.8 GB, Editor up to ~20 GB private), not error 1455.
// The monitor reports heap and working-set levels, growth between
// samples, and growth per simulated day — and says plainly when growth
// is unmeasured, instead of inventing a pressure verdict.
//
// Live achieved speed also has a readout already: TimeManager computes
// LastAchievedGameSecondsPerRealSecond every frame and
// AchievedSpeedDiagnostics (DEF-061) supplies the pending-queue warning
// (more than one full game day pending warns once per day). This monitor
// reuses that warning decision; it does not invent a second one.
//
// Evidence taxonomy on every finding in this file: source-reviewed only.
// Nothing here is Unity-compiled, profiled, or live-verified; the
// Roslyn harness is the only verification this pass can perform (see
// HARNESS_NOTES.md). No run exists on this HEAD.

using System;
using System.Collections.Generic;
using System.Globalization;
using LandLedgers.Time;

namespace LandLedgers.Diagnostics
{
    /// <summary>One run-session's throughput, computed from the runner's own fields.</summary>
    public sealed class RunThroughput
    {
        public bool Valid;
        public string InvalidReason = string.Empty;
        public double WallSeconds;
        public int CompletedDays;
        public int TargetDays;
        public float SelectedMultiplier;
        public double RequestedGameSecondsPerRealSecond;
        public double AchievedGameSecondsPerRealSecond;
        public double AchievedRatio;
        public double SimulatedDaysPerMinute;
        public double WallSecondsPerSimulatedDay;
        public string SuggestedAction = string.Empty;
    }

    /// <summary>One memory sample. Both numbers are labelled by what they ARE; neither is physical RAM.</summary>
    public sealed class MemorySample
    {
        /// <summary>Wall-clock timestamp of the sample (ISO-8601), or empty when unknown.</summary>
        public string TimestampUtc = string.Empty;
        /// <summary>Simulated day index at the sample, or -1 when unknown.</summary>
        public int DayIndex = -1;
        /// <summary>GC.GetTotalMemory(false): the MANAGED HEAP in bytes. Not process memory, not physical RAM.</summary>
        public long ManagedHeapBytes = -1;
        /// <summary>Process working set in bytes. Not private memory, not physical RAM.</summary>
        public long WorkingSetBytes = -1;
    }

    /// <summary>Summary over memory samples — levels and growth, never a pressure verdict from thin air.</summary>
    public sealed class MemorySummary
    {
        public MemorySummary()
        {
        }

        public int SampleCount;
        public bool HasManagedHeap;
        public long ManagedHeapFirstBytes = -1;
        public long ManagedHeapLastBytes = -1;
        public long ManagedHeapMinBytes = -1;
        public long ManagedHeapMaxBytes = -1;
        public bool HasWorkingSet;
        public long WorkingSetFirstBytes = -1;
        public long WorkingSetLastBytes = -1;
        public long WorkingSetMinBytes = -1;
        public long WorkingSetMaxBytes = -1;
        public long ManagedHeapGrowthBytes;
        public long WorkingSetGrowthBytes;
        /// <summary>Growth per simulated day, or -1 when the day span is unknown. A number, not a verdict.</summary>
        public double ManagedHeapGrowthBytesPerDay = -1;
        public double WorkingSetGrowthBytesPerDay = -1;
        public string Assessment = string.Empty;
    }

    /// <summary>
    /// The monitor. All methods are pure over caller-supplied values:
    /// no process inspected, no clock read, no memory allocated to
    /// measure itself. The window supplies the samples; this class only
    /// computes and labels them honestly.
    /// </summary>
    public static class SimulationPerformanceMonitor
    {
        /// <summary>Game seconds per real second at 1x: one game minute per real second (DEF-061: 250x = 15,000).</summary>
        public const double GameSecondsPerRealSecondAt1x = 60.0;

        /// <summary>Game seconds in one simulated day (TimeManager.GameSecondsPerDay).</summary>
        public const double GameSecondsPerDay = 86400.0;

        /// <summary>
        /// Diagnostic heuristic, not a Canon rule: an achieved rate at or
        /// above 90% of the requested rate counts as holding the preset.
        /// Below it, the suggestion is to step down one speed preset and
        /// re-measure. The plan prefers 250x or the next lower stable
        /// preset for long tests.
        /// </summary>
        public const double HoldPresetRatio = 0.9;

        /// <summary>Format bytes as MB with one decimal, for display.</summary>
        public static string FormatMegabytes(long bytes)
        {
            if (bytes < 0)
                return "unknown";
            return (bytes / 1048576.0).ToString("F1", CultureInfo.InvariantCulture) + " MB";
        }

        /// <summary>
        /// Throughput from a run session's own fields. Invalid when the
        /// wall times do not parse, the end is not after the start, or
        /// no day completed — the monitor reports the invalidity, never
        /// a fabricated rate.
        /// </summary>
        public static RunThroughput ComputeThroughput(
            string wallStartUtc, string wallEndUtc,
            int completedDays, int targetDays, float selectedMultiplier)
        {
            var result = new RunThroughput
            {
                CompletedDays = completedDays,
                TargetDays = targetDays,
                SelectedMultiplier = selectedMultiplier,
            };

            DateTime start;
            DateTime end;
            if (!DateTime.TryParse(wallStartUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out start)
                || !DateTime.TryParse(wallEndUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out end))
            {
                result.Valid = false;
                result.InvalidReason = "Wall start/end did not parse as ISO-8601 UTC (runner RunSession.wallStartUtc / wallEndUtc). No rate computed.";
                return result;
            }

            double wallSeconds = (end - start).TotalSeconds;
            if (wallSeconds <= 0)
            {
                result.Valid = false;
                result.InvalidReason = "Wall end is not after wall start (" + wallSeconds + "s). No rate computed.";
                return result;
            }
            if (completedDays <= 0)
            {
                result.Valid = false;
                result.InvalidReason = "No day completed (completedDays = " + completedDays + "). No rate computed.";
                return result;
            }

            result.Valid = true;
            result.WallSeconds = wallSeconds;
            result.RequestedGameSecondsPerRealSecond = Math.Max(0.0, (double)selectedMultiplier) * GameSecondsPerRealSecondAt1x;
            double gameSeconds = completedDays * GameSecondsPerDay;
            result.AchievedGameSecondsPerRealSecond = gameSeconds / wallSeconds;
            result.AchievedRatio = result.RequestedGameSecondsPerRealSecond > 0
                ? result.AchievedGameSecondsPerRealSecond / result.RequestedGameSecondsPerRealSecond
                : 0.0;
            result.SimulatedDaysPerMinute = completedDays / (wallSeconds / 60.0);
            result.WallSecondsPerSimulatedDay = wallSeconds / completedDays;
            result.SuggestedAction = result.AchievedRatio >= HoldPresetRatio
                ? "Hold: achieved rate is at least 90% of the requested rate at " + selectedMultiplier + "x."
                : "Step down one speed preset and re-measure: achieved " + result.AchievedGameSecondsPerRealSecond.ToString("F0", CultureInfo.InvariantCulture)
                    + " of requested " + result.RequestedGameSecondsPerRealSecond.ToString("F0", CultureInfo.InvariantCulture)
                    + " game-s/real-s (" + (result.AchievedRatio * 100.0).ToString("F0", CultureInfo.InvariantCulture)
                    + "%). Prefer 250x or the next lower stable preset for long tests.";
            return result;
        }

        /// <summary>
        /// The retained 60-day run's DEF-061 reference numbers, for
        /// comparison: 250x requested 15,000 game-s/real-s, achieved
        /// 2,712–32,573, wildly variable. Not a measurement of this run.
        /// </summary>
        public static string RetainedRunThroughputReference()
        {
            return "Retained 60-day run (20261120T192311Z-343a8cd2): 250x requested 15,000 game-s/real-s; "
                + "achieved 2,712–32,573 game-s/real-s across the run — wildly variable, not a stable rate. "
                + "DEF-061 needs re-scoping as measured throughput, not a clock fix (G R13: no live-clock reads in production).";
        }

        /// <summary>
        /// DEF-061 pending-queue warning, reusing AchievedSpeedDiagnostics:
        /// warn when the pending queue holds more than one full game day.
        /// </summary>
        public static bool ShouldWarnPendingQueue(double pendingGameSeconds, float gameSecondsPerDay)
        {
            return AchievedSpeedDiagnostics.PendingExceedsWarningThreshold(pendingGameSeconds, gameSecondsPerDay);
        }

        /// <summary>
        /// Summary over memory samples. Growth is reported as a number;
        /// the assessment says what the numbers do and do not mean. RAM
        /// is the binding constraint per the Nov 19 audits (free RAM
        /// 0.25–3.8 GB, Editor up to ~20 GB private), not error 1455 —
        /// but a rising heap in a short sample is a signal to watch
        /// across a longer run, not a diagnosis by itself.
        /// </summary>
        public static MemorySummary SummarizeMemory(IReadOnlyList<MemorySample> samples)
        {
            var summary = new MemorySummary();
            if (samples == null || samples.Count == 0)
            {
                summary.Assessment = "No memory samples recorded. Nothing is measured — no pressure claim is made.";
                return summary;
            }

            summary.SampleCount = samples.Count;
            long heapMin = long.MaxValue;
            long heapMax = long.MinValue;
            long heapFirst = -1;
            long heapLast = -1;
            long wsMin = long.MaxValue;
            long wsMax = long.MinValue;
            long wsFirst = -1;
            long wsLast = -1;
            int firstDay = -1;
            int lastDay = -1;

            foreach (MemorySample sample in samples)
            {
                if (sample == null)
                    continue;
                if (sample.ManagedHeapBytes >= 0)
                {
                    summary.HasManagedHeap = true;
                    if (heapFirst < 0) heapFirst = sample.ManagedHeapBytes;
                    heapLast = sample.ManagedHeapBytes;
                    if (sample.ManagedHeapBytes < heapMin) heapMin = sample.ManagedHeapBytes;
                    if (sample.ManagedHeapBytes > heapMax) heapMax = sample.ManagedHeapBytes;
                }
                if (sample.WorkingSetBytes >= 0)
                {
                    summary.HasWorkingSet = true;
                    if (wsFirst < 0) wsFirst = sample.WorkingSetBytes;
                    wsLast = sample.WorkingSetBytes;
                    if (sample.WorkingSetBytes < wsMin) wsMin = sample.WorkingSetBytes;
                    if (sample.WorkingSetBytes > wsMax) wsMax = sample.WorkingSetBytes;
                }
                if (sample.DayIndex >= 0)
                {
                    if (firstDay < 0) firstDay = sample.DayIndex;
                    lastDay = sample.DayIndex;
                }
            }

            if (summary.HasManagedHeap)
            {
                summary.ManagedHeapFirstBytes = heapFirst;
                summary.ManagedHeapLastBytes = heapLast;
                summary.ManagedHeapMinBytes = heapMin;
                summary.ManagedHeapMaxBytes = heapMax;
                summary.ManagedHeapGrowthBytes = heapLast - heapFirst;
            }
            if (summary.HasWorkingSet)
            {
                summary.WorkingSetFirstBytes = wsFirst;
                summary.WorkingSetLastBytes = wsLast;
                summary.WorkingSetMinBytes = wsMin;
                summary.WorkingSetMaxBytes = wsMax;
                summary.WorkingSetGrowthBytes = wsLast - wsFirst;
            }

            int daySpan = (firstDay >= 0 && lastDay >= firstDay) ? (lastDay - firstDay) : -1;
            if (daySpan > 0)
            {
                if (summary.HasManagedHeap)
                    summary.ManagedHeapGrowthBytesPerDay = (double)summary.ManagedHeapGrowthBytes / daySpan;
                if (summary.HasWorkingSet)
                    summary.WorkingSetGrowthBytesPerDay = (double)summary.WorkingSetGrowthBytes / daySpan;
            }

            if (summary.SampleCount == 1)
            {
                summary.Assessment = "One sample: a level, not a trend. Growth is unmeasured — record more samples across simulated days before judging memory pressure.";
            }
            else if (!summary.HasManagedHeap && !summary.HasWorkingSet)
            {
                summary.Assessment = "Samples recorded but no heap or working-set value was present. Nothing is measured — no pressure claim is made.";
            }
            else
            {
                string heapPart = summary.HasManagedHeap
                    ? "Managed heap " + FormatMegabytes(summary.ManagedHeapFirstBytes) + " → " + FormatMegabytes(summary.ManagedHeapLastBytes)
                        + " (growth " + FormatMegabytes(summary.ManagedHeapGrowthBytes)
                        + (summary.ManagedHeapGrowthBytesPerDay >= 0 ? ", " + FormatMegabytes((long)summary.ManagedHeapGrowthBytesPerDay) + "/day" : ", per-day growth unknown")
                        + ")"
                    : "Managed heap unmeasured";
                string wsPart = summary.HasWorkingSet
                    ? "working set " + FormatMegabytes(summary.WorkingSetFirstBytes) + " → " + FormatMegabytes(summary.WorkingSetLastBytes)
                        + " (growth " + FormatMegabytes(summary.WorkingSetGrowthBytes)
                        + (summary.WorkingSetGrowthBytesPerDay >= 0 ? ", " + FormatMegabytes((long)summary.WorkingSetGrowthBytesPerDay) + "/day" : ", per-day growth unknown")
                        + ")"
                    : "working set unmeasured";
                summary.Assessment = heapPart + "; " + wsPart + ". Neither number is physical RAM. "
                    + "RAM is the binding constraint per the Nov 19 audits (free RAM 0.25–3.8 GB, Editor up to ~20 GB private), not error 1455 — "
                    + "a rising heap across a long run is a signal to watch, not a diagnosis by itself.";
            }
            return summary;
        }
    }
}
