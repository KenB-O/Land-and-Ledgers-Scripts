// Lane 7 (Muse Pass, Nov 20 2026) — Simulation Performance and Memory
// Monitor window (Tool 09). Menu: Land & Ledgers/Diagnostics/Simulation Performance and Memory Monitor.
//
// Tick cost, memory pressure — with the same honesty rules as the rest
// of the pass: report what was measured, never claim a Unity profile
// run here. Throughput comes from the runner's own fields (wall start/
// end, completed days, selected multiplier); memory is the managed heap
// (GC.GetTotalMemory) and the process working set — neither is physical
// RAM, and this window never claims otherwise. RAM is the binding
// constraint per the Nov 19 audits (free RAM 0.25–3.8 GB, Editor up to
// ~20 GB private), not error 1455.
//
// Verification: this file is UnityEditor-bound and CANNOT join the Roslyn
// harness closure. It is verified with ParseCheck only (must print
// PARSE CLEAN). The window cannot be opened without Unity here; the
// computation logic lives in SimulationPerformanceMonitor (Core, harness
// tested). No live run exists on this HEAD.
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Diagnostics;
using LandLedgers.Diagnostics;
using LandLedgers.Time;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Diagnostics
{
    public sealed class SimulationPerformanceMonitorWindow : EditorWindow
    {
        private readonly List<MemorySample> samples = new List<MemorySample>();
        private string wallStartUtc = "2026-11-20T19:23:11Z";
        private string wallEndUtc = "2026-11-20T20:23:11Z";
        private string completedDaysText = "60";
        private string targetDaysText = "60";
        private string multiplierText = "250";
        private RunThroughput lastThroughput;
        private Vector2 scroll;
        private string status = "No samples recorded yet.";

        [MenuItem("Land & Ledgers/Diagnostics/Simulation Performance and Memory Monitor")]
        public static void Open()
        {
            SimulationPerformanceMonitorWindow window = GetWindow<SimulationPerformanceMonitorWindow>("Simulation Performance and Memory Monitor");
            window.minSize = new Vector2(680, 560);
            window.Show();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Simulation Performance and Memory Monitor", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox("Tick cost and memory pressure, measured — never a Unity profile claim. Throughput is computed from the runner's own wall times and completed days; memory is the managed heap and the process working set. Neither memory number is physical RAM.", MessageType.Info);

            DrawLiveClock();
            DrawMemorySampling();
            DrawRunSessionThroughput();
            DrawKnownFacts();
            EditorGUILayout.EndScrollView();
        }

        private void DrawLiveClock()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Live clock (TimeManager)", EditorStyles.boldLabel);
            TimeManager time = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
            if (time == null)
            {
                EditorGUILayout.LabelField("No live TimeManager: enter Play Mode in the Main Scene.");
            }
            else
            {
                EditorGUILayout.LabelField(time.DescribeAchievedSpeed(), EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Last achieved: " + time.LastAchievedGameSecondsPerRealSecond.ToString("F0")
                    + " game-s/real-s; pending queue: " + time.PendingGameSeconds.ToString("F0") + " game-s.", EditorStyles.wordWrappedLabel);
                bool warn = SimulationPerformanceMonitor.ShouldWarnPendingQueue(
                    time.PendingGameSeconds, 86400f);
                EditorGUILayout.LabelField("Pending-queue warning (DEF-061, more than one full game day pending): "
                    + (warn ? "WARN — the clock is more than a day behind the requested schedule." : "not tripped."),
                    EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawMemorySampling()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Memory samples (" + samples.Count + ")", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Managed heap = GC.GetTotalMemory(false). Working set = Process working set. Neither is physical RAM.", EditorStyles.wordWrappedLabel);
            if (GUILayout.Button("Record sample now"))
            {
                TimeManager time = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
                long heapBytes;
                try
                {
                    heapBytes = System.GC.GetTotalMemory(false);
                }
                catch (System.Exception)
                {
                    heapBytes = -1;
                }
                long workingSetBytes;
                try
                {
                    workingSetBytes = Process.GetCurrentProcess().WorkingSet64;
                }
                catch (System.Exception)
                {
                    workingSetBytes = -1;
                }
                samples.Add(new MemorySample
                {
                    TimestampUtc = System.DateTime.UtcNow.ToString("O"),
                    DayIndex = time != null ? time.CurrentAbsoluteDayIndex : -1,
                    ManagedHeapBytes = heapBytes,
                    WorkingSetBytes = workingSetBytes,
                });
                status = "Recorded sample " + samples.Count + " (heap "
                    + SimulationPerformanceMonitor.FormatMegabytes(heapBytes) + ", working set "
                    + SimulationPerformanceMonitor.FormatMegabytes(workingSetBytes) + ").";
            }
            EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);

            if (samples.Count > 0)
            {
                MemorySummary summary = SimulationPerformanceMonitor.SummarizeMemory(samples);
                EditorGUILayout.LabelField("Managed heap: " + SimulationPerformanceMonitor.FormatMegabytes(summary.ManagedHeapFirstBytes)
                    + " → " + SimulationPerformanceMonitor.FormatMegabytes(summary.ManagedHeapLastBytes)
                    + " (min " + SimulationPerformanceMonitor.FormatMegabytes(summary.ManagedHeapMinBytes)
                    + ", max " + SimulationPerformanceMonitor.FormatMegabytes(summary.ManagedHeapMaxBytes) + ")", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Working set: " + SimulationPerformanceMonitor.FormatMegabytes(summary.WorkingSetFirstBytes)
                    + " → " + SimulationPerformanceMonitor.FormatMegabytes(summary.WorkingSetLastBytes)
                    + " (min " + SimulationPerformanceMonitor.FormatMegabytes(summary.WorkingSetMinBytes)
                    + ", max " + SimulationPerformanceMonitor.FormatMegabytes(summary.WorkingSetMaxBytes) + ")", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(summary.Assessment, EditorStyles.wordWrappedLabel);
                if (GUILayout.Button("Clear samples"))
                {
                    samples.Clear();
                    status = "Samples cleared.";
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawRunSessionThroughput()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Run-session throughput (runner fields)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Paste a run's wall start/end (ISO-8601 UTC, from the runner's RunSession) with its completed days and selected multiplier. The 60-day runner records no achieved-throughput fields of its own — this computes them.", EditorStyles.wordWrappedLabel);
            wallStartUtc = EditorGUILayout.TextField("Wall start UTC", wallStartUtc);
            wallEndUtc = EditorGUILayout.TextField("Wall end UTC", wallEndUtc);
            completedDaysText = EditorGUILayout.TextField("Completed days", completedDaysText);
            targetDaysText = EditorGUILayout.TextField("Target days", targetDaysText);
            multiplierText = EditorGUILayout.TextField("Selected multiplier", multiplierText);
            if (GUILayout.Button("Compute throughput"))
            {
                int completedDays;
                int targetDays;
                float multiplier;
                if (!int.TryParse(completedDaysText, out completedDays)
                    || !int.TryParse(targetDaysText, out targetDays)
                    || !float.TryParse(multiplierText, out multiplier))
                {
                    lastThroughput = null;
                    status = "Throughput inputs did not parse: completed/target days must be integers, multiplier a number.";
                }
                else
                {
                    lastThroughput = SimulationPerformanceMonitor.ComputeThroughput(
                        wallStartUtc, wallEndUtc, completedDays, targetDays, multiplier);
                    status = lastThroughput.Valid
                        ? "Throughput computed."
                        : "Throughput invalid: " + lastThroughput.InvalidReason;
                }
            }
            if (lastThroughput != null && lastThroughput.Valid)
            {
                EditorGUILayout.LabelField("Achieved: " + lastThroughput.AchievedGameSecondsPerRealSecond.ToString("F0")
                    + " of requested " + lastThroughput.RequestedGameSecondsPerRealSecond.ToString("F0")
                    + " game-s/real-s (" + (lastThroughput.AchievedRatio * 100.0).ToString("F0") + "%)", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Simulated days/minute: " + lastThroughput.SimulatedDaysPerMinute.ToString("F2")
                    + " | wall seconds/simulated day: " + lastThroughput.WallSecondsPerSimulatedDay.ToString("F1"), EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(lastThroughput.SuggestedAction, EditorStyles.wordWrappedLabel);
            }
            else if (lastThroughput != null)
            {
                EditorGUILayout.LabelField("Invalid: " + lastThroughput.InvalidReason, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawKnownFacts()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Known facts (source-reviewed)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(SimulationPerformanceMonitor.RetainedRunThroughputReference(), EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("G R13 — the live-clock theory is REFUTED: no DateTime.Now / UtcNow / realtimeSinceStartup anywhere in production Assets/Scripts. DEF-061 needs re-scoping as measured throughput, not a clock fix.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("B-8 — 250x tick-driver sampling: verify before tuning any timeout policy.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("RAM is the binding constraint per the Nov 19 audits (free RAM 0.25–3.8 GB, Editor up to ~20 GB private), not error 1455.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("No run exists on this HEAD — no throughput or memory numbers here are measured from a live run until Codex runs one.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
        }
    }
}
#endif
