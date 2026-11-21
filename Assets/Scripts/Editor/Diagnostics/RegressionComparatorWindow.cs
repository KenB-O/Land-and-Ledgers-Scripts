// Land & Ledgers — Regression Comparator window (Lane 8, Tool 11, Agent N
// §3.8).
//
// Editor window for the RegressionComparator core. It reads two run
// directories' own artifacts, runs the comparability gate, and reports
// per-signature deltas. The gate is binding: a refused pair reports no
// deltas, and a comparison against the retained 60-day run
// (20261120T192311Z-343a8cd2, commit 8537f52f dirty tree) is labelled
// BASELINE SHAPE ONLY, never before/after. A disappeared signature without
// a repair attribution is UNEXPLAINED — a reduction in warning count alone
// is NOT a repair. Unity-bound, so it is verified with ParseCheck only —
// it never joins the Roslyn harness closure.
//
// Menu: Land & Ledgers/Diagnostics/Regression Comparator

using System.Collections.Generic;
using System.IO;
using LandLedgers.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Diagnostics
{
    public sealed class RegressionComparatorWindow : EditorWindow
    {
        [MenuItem("Land & Ledgers/Diagnostics/Regression Comparator")]
        public static void Open()
        {
            var window = GetWindow<RegressionComparatorWindow>("Regression Comparator");
            window.Show();
        }

        private Vector2 scroll;
        private string baselineDirectory = "Reports/Playtests/Runs/20261120T192311Z-343a8cd2";
        private string candidateDirectory = string.Empty;
        private bool speedDifferenceRecordedAsVariable;
        private bool sourceChangeIsSubject;
        private string repairAttributions = string.Empty;
        private RunComparisonResult result;
        private string status = "Not compared yet. Pick two run directories and press Compare.";

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Regression Comparator (Tool 11)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Compares two runs' structured outputs (warnings/errors by (signature, destination), meals, shopping, "
                + "production, freight, inventories, transactions). The comparability gate refuses pairs that differ in "
                + "seed, scenario, startup mode, target days, unrecorded speed, or dirty-vs-clean source state. "
                + "The retained 60-day run is BASELINE SHAPE ONLY, never a same-source comparator.",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space();
            baselineDirectory = EditorGUILayout.TextField("Baseline run directory", baselineDirectory);
            candidateDirectory = EditorGUILayout.TextField("Candidate run directory", candidateDirectory);
            speedDifferenceRecordedAsVariable = EditorGUILayout.Toggle("Speed difference recorded as a variable (A F5 caveat)", speedDifferenceRecordedAsVariable);
            sourceChangeIsSubject = EditorGUILayout.Toggle("Source-state difference is the explicit subject", sourceChangeIsSubject);
            EditorGUILayout.LabelField("Repair attributions, one per line: signature || destination || register entry || file:line", EditorStyles.wordWrappedLabel);
            repairAttributions = EditorGUILayout.TextArea(repairAttributions, GUILayout.Height(60));

            if (GUILayout.Button("Compare", GUILayout.Height(28)))
            {
                CompareRuns();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status: " + status, EditorStyles.wordWrappedLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll);

            if (result != null)
            {
                EditorGUILayout.LabelField("Comparability: " + result.Comparability, EditorStyles.boldLabel);
                foreach (string note in result.GateNotes)
                {
                    EditorGUILayout.LabelField("- " + note, EditorStyles.wordWrappedLabel);
                }
                if (result.Comparability != Comparability.Refused)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Outcomes: meals eaten " + result.MealsEatenDelta
                        + "; meals missed " + result.MealsMissedDelta
                        + "; warnings " + result.WarningDelta + "; errors " + result.ErrorDelta
                        + "; settle lines " + result.SettleLineDelta + "; receive lines " + result.ReceiveLineDelta
                        + "; trips aborted for no route " + result.TripsAbortedDelta + ".", EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("Disappeared signatures without repair attribution (UNEXPLAINED): " + result.UnexplainedDisappearances
                        + ". New signatures (NEW REGRESSION candidates): " + result.NewSignatures + ".", EditorStyles.wordWrappedLabel);
                    EditorGUILayout.Space();
                    foreach (SignatureDelta delta in result.SignatureDeltas)
                    {
                        if (delta.Delta == 0)
                        {
                            continue;
                        }
                        string line = "[" + delta.Signature + "] @ '" + delta.Destination + "': "
                            + delta.BaselineCount + " -> " + delta.CandidateCount + " (" + delta.Delta + ")";
                        if (delta.DisappearedUnexplained)
                        {
                            line += " — UNEXPLAINED.";
                        }
                        else if (delta.Disappeared && delta.Attribution != null)
                        {
                            line += " — removed by " + delta.Attribution.RegisterEntry + " (" + delta.Attribution.FileLine + ").";
                        }
                        else if (delta.IsNew)
                        {
                            line += " — NEW REGRESSION candidate.";
                        }
                        EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
                    }
                    if (GUILayout.Button("Copy comparison markdown"))
                    {
                        EditorGUIUtility.systemCopyBuffer = result.ToMarkdown();
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void CompareRuns()
        {
            string repoRoot = Directory.GetParent(Application.dataPath).FullName;
            string baselineDir = Path.Combine(repoRoot, baselineDirectory);
            string candidateDir = Path.Combine(repoRoot, candidateDirectory);
            if (!Directory.Exists(baselineDir))
            {
                status = "Baseline run directory not found: " + baselineDir;
                return;
            }
            if (!Directory.Exists(candidateDir))
            {
                status = "Candidate run directory not found: " + candidateDir;
                return;
            }

            RunSummary baseline = ReadRun(baselineDir);
            RunSummary candidate = ReadRun(candidateDir);
            result = RegressionComparator.Compare(
                baseline, candidate,
                speedDifferenceRecordedAsVariable, sourceChangeIsSubject,
                ParseAttributions(repairAttributions));
            status = "Compared " + baseline.RunId + " against " + candidate.RunId + ": " + result.Comparability + ".";
        }

        private static RunSummary ReadRun(string runDir)
        {
            string metadataText = ReadOptional(Path.Combine(runDir, "metadata.json"));
            string csvText = ReadOptional(Path.Combine(runDir, "daily-summary.csv"));
            string jsonlText = ReadOptional(Path.Combine(runDir, "console-events.jsonl"));
            RunIdentity identity = RunArtifactReader.ParseMetadataJson(metadataText);
            List<DailySummaryRow> rows = RunArtifactReader.ParseDailySummaryCsv(csvText);
            ConsoleEventSummary console = RunArtifactReader.ParseConsoleEventsJsonl(jsonlText);
            RunSummary summary = RunArtifactReader.BuildRunSummary(identity.RunId, identity, rows, console, -1, -1);
            RunArtifactReader.ApplyMetadataToSummary(summary, metadataText);
            return summary;
        }

        private static List<RepairAttribution> ParseAttributions(string text)
        {
            var attributions = new List<RepairAttribution>();
            if (string.IsNullOrEmpty(text))
            {
                return attributions;
            }

            string[] lines = text.Split(new[] { '\n' }, System.StringSplitOptions.None);
            foreach (string line in lines)
            {
                if (line.Trim().Length == 0)
                {
                    continue;
                }
                string[] parts = line.Split(new[] { "||" }, System.StringSplitOptions.None);
                if (parts.Length < 4)
                {
                    continue;
                }
                attributions.Add(new RepairAttribution(
                    parts[0].Trim(), parts[1].Trim(), parts[2].Trim(), parts[3].Trim()));
            }
            return attributions;
        }

        private static string ReadOptional(string fullPath)
        {
            return File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
        }
    }
}
