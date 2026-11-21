// Land & Ledgers — Economy Integrity Auditor window (Tool 02, Lane 3).
//
// Editor window for the EconomyIntegrityAudit core. It OBSERVES: it reads
// source files and config text from disk, runs the pure audit checks, and
// renders findings. It holds no balances, moves no cash, writes no stock,
// and edits nothing. Unity-bound, so it is verified with ParseCheck only —
// it never joins the Roslyn harness closure.
//
// Menu: Land & Ledgers/Diagnostics/Economy Integrity Auditor

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LandLedgers.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Diagnostics
{
    public sealed class EconomyIntegrityAuditorWindow : EditorWindow
    {
        [MenuItem("Land & Ledgers/Diagnostics/Economy Integrity Auditor")]
        public static void Open()
        {
            var window = GetWindow<EconomyIntegrityAuditorWindow>("Economy Integrity Auditor");
            window.Show();
        }

        private Vector2 scroll;
        private EconomyIntegrityReport report;
        private CashWriterInventory writerInventory;
        private List<EconomyIntegrityFinding> findings = new List<EconomyIntegrityFinding>();
        private string status = "Not run yet. Press Run Audit.";
        private string configValues = string.Empty;

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Economy and Inventory Integrity Auditor (Tool 02)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Conservation checks over money, goods and ownership. Observes the authoritative "
                + "systems; never a second ledger, never moves cash.",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Run Audit (source scans + known cases)", GUILayout.Height(28)))
                RunAudit();
            if (GUILayout.Button("Run Known Cases Only", GUILayout.Height(28)))
                RunKnownCasesOnly();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status: " + status, EditorStyles.wordWrappedLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll);

            if (report != null)
            {
                EditorGUILayout.LabelField(
                    "Findings: " + report.Findings.Count
                    + " (" + report.CriticalCount + " critical, " + report.WarningCount + " warning)",
                    EditorStyles.boldLabel);
                foreach (EconomyIntegrityFinding finding in report.Findings)
                {
                    string header = "[" + finding.Severity + "] " + finding.Id + " — " + finding.Title
                        + " (" + finding.Status + ")";
                    EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Check: " + finding.Check + " | Authority: " + finding.Authority, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField(finding.Evidence, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.Space();
                }
            }
            else if (findings.Count > 0)
            {
                EditorGUILayout.LabelField("Findings: " + findings.Count, EditorStyles.boldLabel);
                foreach (EconomyIntegrityFinding finding in findings)
                {
                    EditorGUILayout.LabelField("[" + finding.Severity + "] " + finding.Id + " — " + finding.Title, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(finding.Evidence, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.Space();
                }
            }

            if (writerInventory != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(
                    "E-01 cash writer inventory: " + writerInventory.TotalCallSites
                    + " method-call sites across " + writerInventory.FileCount + " files",
                    EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Cash moves only through BusinessRuntimeState methods (AddCashCents, "
                    + "SetCurrentCashCents, AdjustCashCents, RecordWeeklyCashTransferIn/Out).",
                    EditorStyles.wordWrappedLabel);
                foreach (KeyValuePair<string, int> pair in writerInventory.WritersByFile)
                    EditorGUILayout.LabelField(pair.Value + "  " + pair.Key);
            }

            if (report != null && report.LotPoolMap != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("E-10 lot/pool map (verified at this HEAD)", EditorStyles.boldLabel);
                foreach (LotPoolMapEntry entry in report.LotPoolMap)
                {
                    EditorGUILayout.LabelField(entry.Subsystem + " — " + entry.StockForm + " [" + entry.Status + "]", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(entry.Evidence, EditorStyles.wordWrappedLabel);
                }
            }

            if (!string.IsNullOrEmpty(configValues))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Config values read (DevelopmentTownConfiguration.asset)", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(configValues, EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        private void RunKnownCasesOnly()
        {
            report = EconomyIntegrityAudit.AuditKnownCases();
            findings = report.Findings;
            writerInventory = null;
            configValues = string.Empty;
            status = "Known cases reproduced from the current tree's source-reviewed evidence (no run exists on this HEAD).";
        }

        private void RunAudit()
        {
            string repoRoot = RepoRoot();
            if (repoRoot == null)
            {
                status = "Could not locate the repo root (expected Assets/Scripts under Application.dataPath).";
                return;
            }

            var allFindings = new List<EconomyIntegrityFinding>();
            string scriptsRoot = Path.Combine(repoRoot, "Assets", "Scripts");

            List<EconomySourceTextFile> productionFiles = ReadProductionFiles(scriptsRoot, repoRoot);

            // E-01: enumerate the method-call writer list over the real tree.
            writerInventory = EconomyIntegrityAudit.AuditCashWriters(productionFiles, allFindings);

            // E-02: legacy settlement guards + clerk policy agreement.
            EconomySourceTextFile storeManager = FindFile(productionFiles, "GeneralStoreRuntimeManager.cs");
            if (storeManager != null)
                EconomyIntegrityAudit.VerifyLegacySalesEarlyReturn(storeManager.Text, allFindings);
            EconomySourceTextFile clerkPolicy = FindFile(productionFiles, "GeneralStoreServiceClerkPolicy.cs");
            EconomySourceTextFile drivers = FindFile(productionFiles, "SimulationDrivers.cs");
            if (clerkPolicy != null && drivers != null)
                EconomyIntegrityAudit.VerifyClerkPolicy(clerkPolicy.Text, drivers.Text, allFindings);

            // E-19: supplier ledger debit check.
            EconomySourceTextFile registry = FindFile(productionFiles, "RegionalBusinessRegistry.cs");
            if (registry != null)
                EconomyIntegrityAudit.VerifySupplierLedgerHasNoDebit(registry.Text, allFindings);

            // E-23: reservation coverage — retail files vs all production files.
            List<EconomySourceTextFile> retailFiles = productionFiles
                .Where(f => f.RelativePath.Contains("GeneralStore/")
                    || f.RelativePath.EndsWith("HouseholdShoppingLoop.cs", StringComparison.Ordinal)
                    || f.RelativePath.EndsWith("ShoppingTripDriver.cs", StringComparison.Ordinal))
                .ToList();
            EconomyIntegrityAudit.AuditReservationCoverage(retailFiles, productionFiles, allFindings);

            // E-18: shipment field list from the real LogisticsModels source.
            EconomySourceTextFile logistics = FindFile(productionFiles, "LogisticsModels.cs");
            if (logistics != null)
            {
                EconomyIntegrityAudit.VerifyShipmentFields(
                    ExtractShipmentStateFields(logistics.Text), allFindings);
            }

            // E-14 / E-20: config values read from the asset text.
            configValues = ReadConfigValues(repoRoot);

            // Known cases, reproduced from the current tree's verified evidence.
            EconomyIntegrityReport known = EconomyIntegrityAudit.AuditKnownCases();
            allFindings.AddRange(known.Findings);

            report = new EconomyIntegrityReport(allFindings, writerInventory, EconomyIntegrityAudit.BuildLotPoolMap());
            findings = allFindings;
            status = "Audit complete. Source-reviewed evidence only — no run exists on this HEAD, nothing here is Unity-compiled or live-verified.";
        }

        private static string RepoRoot()
        {
            // Application.dataPath is <repo>/Assets; the repo root is its parent.
            string assets = Application.dataPath;
            if (string.IsNullOrEmpty(assets))
                return null;
            string root = Directory.GetParent(assets)?.FullName;
            if (root == null || !Directory.Exists(Path.Combine(root, "Assets", "Scripts")))
                return null;
            return root;
        }

        private static List<EconomySourceTextFile> ReadProductionFiles(string scriptsRoot, string repoRoot)
        {
            var files = new List<EconomySourceTextFile>();
            if (!Directory.Exists(scriptsRoot))
                return files;
            foreach (string path in Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                string relative = path.Substring(repoRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (relative.Contains("/Tests/") || relative.Contains("\\Tests\\"))
                    continue;
                try
                {
                    files.Add(new EconomySourceTextFile(relative.Replace('\\', '/'), File.ReadAllText(path)));
                }
                catch (IOException)
                {
                    // Unreadable file: skip it and keep auditing the rest.
                }
                catch (UnauthorizedAccessException)
                {
                    // Same: never idle waiting for access to one file.
                }
            }

            return files;
        }

        private static EconomySourceTextFile FindFile(List<EconomySourceTextFile> files, string fileName)
        {
            foreach (EconomySourceTextFile file in files)
            {
                if (file.RelativePath.EndsWith("/" + fileName, StringComparison.Ordinal)
                    || file.RelativePath.EndsWith(fileName, StringComparison.Ordinal))
                    return file;
            }

            return null;
        }

        private static List<string> ExtractShipmentStateFields(string logisticsModelsSource)
        {
            // Crude but honest: the field names declared inside the
            // LogisticsShipmentState class block. The auditor only checks
            // whether an ETA / overdue / created-day field exists at all.
            var fields = new List<string>();
            string[] lines = logisticsModelsSource.Split('\n');
            bool inClass = false;
            int braceDepth = 0;
            foreach (string rawLine in lines)
            {
                string line = rawLine;
                if (!inClass)
                {
                    if (line.Contains("class LogisticsShipmentState"))
                        inClass = true;
                    continue;
                }

                foreach (char c in line)
                {
                    if (c == '{') braceDepth++;
                    if (c == '}') braceDepth--;
                }

                if (braceDepth <= 0 && line.Contains("}"))
                    break;

                string trimmed = line.Trim();
                if (trimmed.StartsWith("public ", StringComparison.Ordinal)
                    || trimmed.StartsWith("[SerializeField]", StringComparison.Ordinal))
                {
                    string[] tokens = trimmed.Split(new[] { ' ', '\t', ';', '=' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string token in tokens)
                    {
                        string name = token.Trim();
                        if (name.Length > 2 && char.IsLower(name[0]) && !name.Contains("(") && !name.Contains("<"))
                            fields.Add(name);
                    }
                }
            }

            return fields;
        }

        private static string ReadConfigValues(string repoRoot)
        {
            string assetPath = Path.Combine(repoRoot, "Assets", "Core", "Population", "DevelopmentTownConfiguration.asset");
            if (!File.Exists(assetPath))
                return "DevelopmentTownConfiguration.asset not found.";
            string[] lines;
            try
            {
                lines = File.ReadAllLines(assetPath);
            }
            catch (IOException)
            {
                return "DevelopmentTownConfiguration.asset unreadable.";
            }

            var fuel = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("fuelDealerIntakeUnitsPerBakeryUnit:"))
                    fuel.Add("line " + (i + 1) + ": " + lines[i].Trim());
            }

            return "fuelDealerIntakeUnitsPerBakeryUnit occurrences (E-14):\n" + string.Join("\n", fuel.ToArray())
                + "\nOpening cash is reported by the E-20 known-case finding (config governs: bakery 35,000c / dealer 20,000c).";
        }
    }
}
