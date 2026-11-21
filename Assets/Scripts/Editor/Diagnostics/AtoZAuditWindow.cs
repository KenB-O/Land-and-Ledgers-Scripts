// Land & Ledgers — A-to-Z automated audit window (Lane 8, §20).
//
// Editor window for the AtoZAudit core. It OBSERVES: it reads source files
// and config text from disk, runs the pure audit checks, and renders
// findings in the §22 status vocabulary with exact evidence. Findings can
// be emitted to Lane 1's incident core (SimulationIncident / IncidentReport)
// — never a parallel register. It edits nothing. Unity-bound, so it is
// verified with ParseCheck only — it never joins the Roslyn harness closure.
//
// Menu: Land & Ledgers/Diagnostics/A-to-Z Audit

using System.Collections.Generic;
using System.IO;
using LandLedgers.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Diagnostics
{
    public sealed class AtoZAuditWindow : EditorWindow
    {
        [MenuItem("Land & Ledgers/Diagnostics/A-to-Z Audit")]
        public static void Open()
        {
            var window = GetWindow<AtoZAuditWindow>("A-to-Z Audit");
            window.Show();
        }

        private Vector2 scroll;
        private AtoZAuditReport report;
        private string status = "Not run yet. Press Run Audit.";

        private void OnGUI()
        {
            EditorGUILayout.LabelField("A-to-Z Automated Audit (§20)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "The manual A–N checks as automated, re-runnable checks. Each finding carries a §22 status "
                + "(VERIFIED FIXED / REGRESSION VERIFIED / IMPLEMENTED UNVERIFIED / SUSPECTED FIX / UNRESOLVED / "
                + "BLOCKED / NOT REPRODUCED / EXPLAINED LEGITIMATE / NEW REGRESSION) and cites exact evidence "
                + "(file:line, test id, run id). Checks whose evidence is sufficient are recorded, not re-run. "
                + "Run checks are BLOCKED until a live run exists — no run exists on this HEAD.",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Run Audit", GUILayout.Height(28)))
            {
                RunAudit();
            }
            if (report != null && GUILayout.Button("Emit findings to incident core", GUILayout.Height(28)))
            {
                EmitToIncidentCore();
            }
            if (report != null && GUILayout.Button("Copy markdown", GUILayout.Height(28)))
            {
                EditorGUIUtility.systemCopyBuffer = report.ToMarkdown();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status: " + status, EditorStyles.wordWrappedLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll);

            if (report != null)
            {
                EditorGUILayout.LabelField(
                    "Findings: " + report.Findings.Count
                    + " | VERIFIED FIXED: " + report.CountWithStatus(AtoZFindingStatus.VerifiedFixed)
                    + " | IMPLEMENTED UNVERIFIED: " + report.CountWithStatus(AtoZFindingStatus.ImplementedUnverified)
                    + " | SUSPECTED FIX: " + report.CountWithStatus(AtoZFindingStatus.SuspectedFix)
                    + " | UNRESOLVED: " + report.CountWithStatus(AtoZFindingStatus.Unresolved)
                    + " | BLOCKED: " + report.CountWithStatus(AtoZFindingStatus.Blocked)
                    + " | Live run still owed: " + report.LiveRunOwedCount,
                    EditorStyles.boldLabel);
                EditorGUILayout.Space();

                foreach (AtoZFinding finding in report.Findings)
                {
                    EditorGUILayout.LabelField("[" + finding.Definition.Id + "] "
                        + AtoZFindingStatusText.Label(finding.Status)
                        + " — " + finding.Definition.Title, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Detail: " + finding.Detail, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("Evidence: " + finding.Evidence, EditorStyles.wordWrappedLabel);
                    if (finding.LiveRunStillOwed)
                    {
                        EditorGUILayout.LabelField("Live-run confirmation still owed.", EditorStyles.wordWrappedLabel);
                    }
                    EditorGUILayout.Space();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void RunAudit()
        {
            string repoRoot = RepoRoot();
            var evidence = new AtoZSourceEvidence
            {
                ConfigAssetText = ReadFile(repoRoot, "Assets/Core/Population/DevelopmentTownConfiguration.asset"),
                WholesaleAccountsText = ReadFile(repoRoot, "Assets/Scripts/Domains/Economy/Equipment/ScenarioOpeningWholesaleAccounts.cs"),
                ByproductSettlementText = ReadFile(repoRoot, "Assets/Scripts/Domains/Economy/Core/FuelDealerByproductSettlement.cs"),
                SawmillTransferText = ReadFile(repoRoot, "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs"),
                HouseholdMealsText = ReadFile(repoRoot, "Assets/Scripts/Domains/Population/HouseholdMeals.cs"),
            };
            report = AtoZAuditRunner.Evaluate(evidence);
            status = "Audit complete: " + report.Findings.Count + " findings, "
                + report.CountWithStatus(AtoZFindingStatus.Unresolved) + " unresolved, "
                + report.CountWithStatus(AtoZFindingStatus.Blocked) + " blocked (no live run).";
        }

        private void EmitToIncidentCore()
        {
            if (report == null)
            {
                return;
            }

            int emitted = 0;
            foreach (IncidentReport incident in report.ToIncidentReports())
            {
                try
                {
                    IncidentDiagnostics.SharedAuthority.Emit(incident);
                    emitted++;
                }
                catch
                {
                    // Diagnostics must never break the tool. A failed emission is lost, not retried.
                }
            }
            status = "Emitted " + emitted + " of " + report.Findings.Count + " findings to the incident core.";
        }

        private static string RepoRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        private static string ReadFile(string repoRoot, string relativePath)
        {
            string fullPath = Path.Combine(repoRoot, relativePath);
            return File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
        }
    }
}
