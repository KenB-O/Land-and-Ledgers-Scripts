// Land & Ledgers — Scenario Lab window (Lane 8, Tool 10, Agent N §2).
//
// Editor window for the Scenario Lab core. It shows the survival acceptance
// scenario definition (fixed config, eight stages, pass bars P-1–P-8, the
// §10 scenario catalog) and evaluates a run directory's own artifacts
// (daily-summary.csv, console-events.jsonl, metadata.json) against the
// eight stages — then builds the six-section run acceptance report and can
// write run-acceptance-report.md / run-acceptance.json into the run
// directory, as Agent N §3.1 specifies.
//
// The lab cannot execute a live run from this window (no Unity run is
// driven here): it evaluates artifacts. With no run evidence every stage
// is BLOCKED and the verdict is NO LIVE RUN, never a pass. Unity-bound, so
// it is verified with ParseCheck only — it never joins the Roslyn harness
// closure.
//
// Menu: Land & Ledgers/Diagnostics/Scenario Lab

using System.Collections.Generic;
using System.IO;
using LandLedgers.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Diagnostics
{
    public sealed class ScenarioLabWindow : EditorWindow
    {
        [MenuItem("Land & Ledgers/Diagnostics/Scenario Lab")]
        public static void Open()
        {
            var window = GetWindow<ScenarioLabWindow>("Scenario Lab");
            window.Show();
        }

        private Vector2 scroll;
        private int selectedScenario;
        private string runDirectory = "Reports/Playtests/Runs/20261120T192311Z-343a8cd2";
        private bool continueMidpointRun;
        private SurvivalAcceptanceResult survivalResult;
        private RunAcceptanceReport acceptanceReport;
        private RunSummary runSummary;
        private string status = "No run evaluated yet. Point the lab at a run directory and press Evaluate run artifacts.";

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Scenario Lab (Tool 10) — household survival acceptance test", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Fixed configuration: seed 1886, 15 days, 10x, New Game, 14 residents / 8 households / 7 businesses. "
                + "The lab evaluates a run's own artifacts against the eight stages (S1–S8) and the pass bars (P-1–P-8, all PROPOSED). "
                + "It cannot execute a live run here — with no run evidence every stage is BLOCKED, never passed.",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space();
            string[] scenarioNames = ScenarioNames();
            selectedScenario = EditorGUILayout.Popup("Scenario", selectedScenario, scenarioNames);
            LabScenarioDefinition scenario = SurvivalAcceptanceScenario.ScenarioCatalog[selectedScenario];
            EditorGUILayout.LabelField("Acceptance: " + scenario.Acceptance, EditorStyles.wordWrappedLabel);
            if (scenario.RequiredPriorRepair.Length > 0)
            {
                EditorGUILayout.LabelField("Needs prior repair: " + scenario.RequiredPriorRepair, EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Eight stages", EditorStyles.boldLabel);
            foreach (SurvivalStageSpec spec in SurvivalAcceptanceScenario.StageSpecs)
            {
                string verdict = survivalResult == null
                    ? string.Empty
                    : " — " + survivalResult.StageVerdict(spec.StageId).Verdict + ": " + survivalResult.StageVerdict(spec.StageId).Reason;
                EditorGUILayout.LabelField(spec.StageId + " " + spec.Title + verdict, EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.Space();
            runDirectory = EditorGUILayout.TextField("Run directory (relative to repo root)", runDirectory);
            continueMidpointRun = EditorGUILayout.Toggle("Run included a Save/Continue midpoint (save day 7)", continueMidpointRun);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Evaluate run artifacts", GUILayout.Height(28)))
            {
                EvaluateRun();
            }
            if (acceptanceReport != null && GUILayout.Button("Write acceptance report into run directory", GUILayout.Height(28)))
            {
                WriteAcceptanceReport();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status: " + status, EditorStyles.wordWrappedLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll);

            if (survivalResult != null)
            {
                EditorGUILayout.LabelField("Survival verdict: " + survivalResult.Overall, EditorStyles.boldLabel);
                if (survivalResult.FirstFailure.Length > 0)
                {
                    EditorGUILayout.LabelField("First failure: " + survivalResult.FirstFailure, EditorStyles.wordWrappedLabel);
                }
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Pass bars P-1–P-8 (all PROPOSED — manager to confirm, N-14)", EditorStyles.boldLabel);
                foreach (SurvivalPassBar bar in SurvivalAcceptanceScenario.PassBars)
                {
                    SurvivalStageVerdict verdict = survivalResult.BarVerdict(bar.BarId);
                    EditorGUILayout.LabelField(bar.BarId + " " + verdict.Verdict + ": " + verdict.Reason, EditorStyles.wordWrappedLabel);
                }
            }

            if (acceptanceReport != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Six-section acceptance report: " + acceptanceReport.Overall, EditorStyles.boldLabel);
                foreach (RunAcceptanceSection section in acceptanceReport.Sections)
                {
                    EditorGUILayout.LabelField(section.Title + " — " + RunAcceptanceReport.VerdictLabel(section.Verdict), EditorStyles.boldLabel);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private static string[] ScenarioNames()
        {
            var names = new List<string>();
            foreach (LabScenarioDefinition scenario in SurvivalAcceptanceScenario.ScenarioCatalog)
            {
                names.Add(scenario.Id + " — " + scenario.Title);
            }
            return names.ToArray();
        }

        private void EvaluateRun()
        {
            string repoRoot = Directory.GetParent(Application.dataPath).FullName;
            string runDir = Path.Combine(repoRoot, runDirectory);
            if (!Directory.Exists(runDir))
            {
                status = "Run directory not found: " + runDir;
                return;
            }

            string metadataText = ReadOptional(Path.Combine(runDir, "metadata.json"));
            string csvText = ReadOptional(Path.Combine(runDir, "daily-summary.csv"));
            string jsonlText = ReadOptional(Path.Combine(runDir, "console-events.jsonl"));

            RunIdentity identity = RunArtifactReader.ParseMetadataJson(metadataText);
            List<DailySummaryRow> rows = RunArtifactReader.ParseDailySummaryCsv(csvText);
            ConsoleEventSummary console = RunArtifactReader.ParseConsoleEventsJsonl(jsonlText);

            runSummary = RunArtifactReader.BuildRunSummary(identity.RunId, identity, rows, console, -1, -1);
            RunArtifactReader.ApplyMetadataToSummary(runSummary, metadataText);

            SurvivalRunEvidence evidence = RunArtifactReader.BuildSurvivalEvidence(identity.RunId, identity, rows, console);
            // The artifact reader only fills what the artifacts measure; the
            // acceptance configuration is fixed, so a run on another
            // configuration is reported BLOCKED by the evaluator, not passed.
            survivalResult = SurvivalAcceptanceScenario.Evaluate(evidence);
            acceptanceReport = RunAcceptanceReportBuilder.Build(runSummary, survivalResult, continueMidpointRun);

            status = "Evaluated run " + identity.RunId + ": survival " + survivalResult.Overall
                + ", acceptance " + acceptanceReport.Overall + ".";
        }

        private void WriteAcceptanceReport()
        {
            if (acceptanceReport == null)
            {
                return;
            }

            string repoRoot = Directory.GetParent(Application.dataPath).FullName;
            string runDir = Path.Combine(repoRoot, runDirectory);
            File.WriteAllText(Path.Combine(runDir, "run-acceptance-report.md"), acceptanceReport.ToMarkdown());
            File.WriteAllText(Path.Combine(runDir, "run-acceptance.json"), acceptanceReport.ToJson());
            status = "Wrote run-acceptance-report.md and run-acceptance.json into " + runDir + ".";
            AssetDatabase.Refresh();
        }

        private static string ReadOptional(string fullPath)
        {
            return File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
        }
    }
}
