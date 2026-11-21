// Lane 2 (Muse Pass, Nov 20 2026) — Simulation Incident Investigator
// window (Tool 01). Menu: Land & Ledgers/Diagnostics/Incident Investigator.
//
// Investigates a missing meal, stalled worker, incomplete delivery, failed
// purchase, missing inventory, or broken production cycle from the retained
// trace evidence, without reconstructing thousands of Console entries. It
// names the responsible authority and gives a defensible explanation
// whenever the evidence permits. Observation only: it assigns no work,
// changes no routes, and moves no goods, money, food, transport or labour.
//
// Verification: this file is UnityEditor-bound and CANNOT join the Roslyn
// harness closure. It is verified with ParseCheck only (must print
// PARSE CLEAN). The window cannot be opened without Unity here; the
// causal logic it displays lives in IncidentInvestigator (Core, harness
// tested) and the proving cases run on recorded run evidence, not a live
// run — no live run exists on this HEAD.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using LandLedgers.Diagnostics;
using LandLedgers.FirstLedger;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Persistence;
using LandLedgers.Time;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Diagnostics
{
    public sealed class IncidentInvestigatorWindow : EditorWindow
    {
        private string taskId = "";
        private IncidentFinding finding;
        private Vector2 scroll;
        private string status = "No investigation run yet.";

        [MenuItem("Land & Ledgers/Diagnostics/Incident Investigator")]
        public static void Open()
        {
            IncidentInvestigatorWindow window = GetWindow<IncidentInvestigatorWindow>("Incident Investigator");
            window.minSize = new Vector2(620, 520);
            window.Show();
        }

        /// <summary>Open the Investigator pre-targeted at one task (used by the NPC Activity Monitor's Investigate button).</summary>
        public static void OpenForTask(string taskIdToInvestigate)
        {
            IncidentInvestigatorWindow window = GetWindow<IncidentInvestigatorWindow>("Incident Investigator");
            window.minSize = new Vector2(620, 520);
            window.taskId = taskIdToInvestigate ?? string.Empty;
            window.Show();
            window.InvestigateLive();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Simulation Incident Investigator", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox("Observes the retained trace evidence. It never assigns work, changes routes, or alters simulation state. Severity comes from circumstances, never from elapsed time alone.", MessageType.Info);

            SimulationSystemsHub hub = UnityEngine.Object.FindFirstObjectByType<SimulationSystemsHub>();
            TimeManager time = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
            NpcActivityTraceRecorder trace = hub != null ? hub.ActivityTrace : null;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Investigate a task", EditorStyles.boldLabel);
            taskId = EditorGUILayout.TextField("Task ID (e.g. T9)", taskId);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(trace == null || string.IsNullOrWhiteSpace(taskId)))
            {
                if (GUILayout.Button("Investigate live trace")) InvestigateLive();
            }
            EditorGUILayout.EndHorizontal();
            if (trace == null)
                EditorGUILayout.HelpBox("No live trace: enter Play Mode in the Main Scene to connect to SimulationSystemsHub. The proving cases below run on recorded run evidence and do not need a live trace.", MessageType.Warning);
            else
                EditorGUILayout.LabelField("Trace", trace.Events.Count + " session events; dropped session/prehistory: " + trace.DroppedSessionEvents + "/" + trace.DroppedHistoryEvents + "; anomalies: " + trace.Anomalies.Count);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Historical task evidence", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Proving case 1: fuel-handling stall"))
            {
                IncidentDiagnostics.WireInvestigator();
                finding = IncidentInvestigator.InvestigateKnownFuelHandlingStall();
                status = "Proving case 1: interrupted domestic.fuel-handling tasks T9/T10/T170 (60-day) and T8/T9/T11 (5-day), retained-run record.";
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Live bakery wholesale accounts", EditorStyles.boldLabel);
            var bakery = EditorApplication.isPlaying
                ? UnityEngine.Object.FindFirstObjectByType<SaveLoadManager>()?.Series2Pass9State?.BakeryRuntime
                : null;
            if (bakery == null)
            {
                EditorGUILayout.LabelField("No live bakery authority connected.");
            }
            else
            {
                EditorGUILayout.LabelField(bakery.BusinessInstanceId,
                    $"{bakery.SalesChannels} / {bakery.SupplyProtection}");
                EditorGUILayout.LabelField($"Recorded accounts: {bakery.WholesaleAccounts.Count}");
                foreach (var account in bakery.WholesaleAccounts)
                {
                    if (account == null) continue;
                    EditorGUILayout.LabelField(account.AccountId,
                        $"{account.UnitsPerDelivery} {account.ProductId} at {account.PricePerUnitCents}c to " +
                        $"{account.BuyerBusinessId}, next due day {account.NextDeliveryDayIndex}",
                        EditorStyles.wordWrappedLabel);
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);
            if (finding != null) DrawFinding(finding, hub, time);
            EditorGUILayout.EndScrollView();
        }

        private void InvestigateLive()
        {
            IncidentDiagnostics.WireInvestigator();
            SimulationSystemsHub hub = UnityEngine.Object.FindFirstObjectByType<SimulationSystemsHub>();
            TimeManager time = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
            NpcActivityTraceRecorder trace = hub != null ? hub.ActivityTrace : null;
            if (trace == null)
            {
                status = "No live trace is connected.";
                finding = null;
                return;
            }
            // Lane 2: use the float timeline accessor here — the
            // double-precision timeline is pinned by
            // CampaignCFloat32TimelineTests to exactly three production
            // consumers, and this window must not become a fourth.
            double now = time != null ? time.AbsoluteGameSeconds : -1;
            finding = trace.InvestigateTask(taskId.Trim(), now);
            status = "Investigated task " + taskId.Trim() + " from the retained trace (bounded pre-incident capture, max 500 rows).";
        }

        private void DrawFinding(IncidentFinding item, SimulationSystemsHub hub, TimeManager time)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(item.Title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Incident", item.IncidentId);
            EditorGUILayout.LabelField("Severity", item.Severity + " — " + SeverityMeaning(item.Severity));
            EditorGUILayout.LabelField("Blocker class", item.BlockerKind.ToString());
            EditorGUILayout.LabelField("Responsible authority", item.ResponsibleAuthority, EditorStyles.wordWrappedLabel);
            if (!string.IsNullOrWhiteSpace(item.AuthoritySourcePath))
                EditorGUILayout.LabelField("Authority source", item.AuthoritySourcePath + (item.AuthoritySourceLine > 0 ? " :" + item.AuthoritySourceLine : ""));
            EditorGUILayout.LabelField("Earliest confirmed blocker",
                string.IsNullOrWhiteSpace(item.EarliestBlocker) ? "(none confirmed — see missing evidence)"
                    : item.EarliestBlocker + (item.EarliestBlockerSeconds >= 0 ? " (at " + item.EarliestBlockerSeconds.ToString("0.#") + "s)" : ""),
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Explanation", item.Explanation, EditorStyles.wordWrappedLabel);
            if (item.MissingEvidence != null && item.MissingEvidence.Count > 0)
                EditorGUILayout.HelpBox("Missing evidence:\n- " + string.Join("\n- ", item.MissingEvidence), MessageType.Warning);
            else
                EditorGUILayout.LabelField("Evidence", "Complete for this classification.");
            EditorGUILayout.LabelField("Source note", item.SourceNote, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Causal timeline", EditorStyles.boldLabel);
            if (item.Timeline == null || item.Timeline.Count == 0)
            {
                EditorGUILayout.LabelField("No timeline events retained for this incident.");
            }
            else
            {
                foreach (CausalTimelineEntry entry in item.Timeline)
                {
                    string prefix = entry.IsBlocker ? "[BLOCKER] " : "";
                    EditorGUILayout.LabelField(prefix + entry.SimulatedSeconds.ToString("0.#") + "s day " + entry.DayIndex + " " + entry.Label,
                        EditorStyles.wordWrappedLabel);
                    if (!string.IsNullOrWhiteSpace(entry.Detail))
                        EditorGUILayout.LabelField("  " + entry.Detail, EditorStyles.wordWrappedLabel);
                }
            }
            EditorGUILayout.EndVertical();

            DrawJumps(item, hub);
        }

        private void DrawJumps(IncidentFinding item, SimulationSystemsHub hub)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Jump to", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(item.PersonNumericId < 0))
            {
                if (GUILayout.Button("Jump to NPC")) FramePerson(item.PersonNumericId);
            }
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(item.TaskId)))
            {
                if (GUILayout.Button("Jump to task in NPC Monitor")) OpenMonitorFor(item);
            }
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(item.BusinessKey)))
            {
                if (GUILayout.Button("Jump to business")) OpenBusinessInspectorFor(item);
            }
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(item.AuthoritySourcePath)))
            {
                if (GUILayout.Button("Jump to authority source")) OpenAuthoritySource(item);
            }
            EditorGUILayout.EndHorizontal();
            // Jump-to status (verified in Lane 2, business completed in Lane 5):
            // - NPC: implemented (frames the person in the Scene view, same as the Monitor's Frame button).
            // - Task: implemented (opens the NPC Activity Monitor with the task's person/household selected).
            // - Business: implemented (Lane 5, Tool 04) — opens the Business Operations Monitor pre-targeted
            //   at the incident's business, showing that business's runtime state and its authority source.
            //   Disabled when the incident names no business, rather than guessing one.
            // - Source: implemented (opens the owning authority's file at the cited line, or pings the asset).
            if (string.IsNullOrWhiteSpace(item.BusinessKey))
                EditorGUILayout.HelpBox("Jump to business: not available — this incident names no business.", MessageType.None);
            else
                EditorGUILayout.LabelField("Jump to business: opens the Business Operations Monitor for " + item.BusinessKey + ".");
            EditorGUILayout.EndVertical();
        }

        private static string SeverityMeaning(IncidentSeverity severity)
        {
            switch (severity)
            {
                case IncidentSeverity.Information: return "legitimate wait or below the stall threshold — not an incident.";
                case IncidentSeverity.Warning: return "idle without a valid cause, or a defect blocks the work.";
                case IncidentSeverity.Critical: return "integrity violation — conservation or authority conflict.";
                default: return "insufficient evidence — the missing evidence is named above.";
            }
        }

        private static void FramePerson(int personId)
        {
            LandLedgers.FirstLedger.PopulationPathingDirector pathing =
                UnityEngine.Object.FindFirstObjectByType<LandLedgers.FirstLedger.PopulationPathingDirector>();
            if (pathing != null && pathing.TryGetPresentationTransform(personId, out Transform actor))
            {
                Selection.activeTransform = actor;
                EditorGUIUtility.PingObject(actor.gameObject);
                SceneView.lastActiveSceneView?.FrameSelected();
            }
        }

        private void OpenMonitorFor(IncidentFinding item)
        {
            // Hand the task's person/household to the Monitor via the same
            // SessionState keys the Monitor itself persists, then open it.
            if (item.PersonNumericId >= 0)
                SessionState.SetString("LandLedgers.NpcTrace.Persons", "P" + item.PersonNumericId);
            if (item.HouseholdId >= 0)
                SessionState.SetString("LandLedgers.NpcTrace.Households", "H" + item.HouseholdId);
            NpcActivityMonitorWindow.Open();
        }

        /// <summary>
        /// Lane 5 (Tool 04): open the Business Operations Monitor
        /// pre-targeted at the incident's business — the business
        /// inspector this jump-to was missing until Lane 5.
        /// </summary>
        private static void OpenBusinessInspectorFor(IncidentFinding item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.BusinessKey))
                return;
            BusinessOperationsMonitorWindow.OpenForBusiness(item.BusinessKey);
        }

        private static void OpenAuthoritySource(IncidentFinding item)
        {
            string path = item.AuthoritySourcePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && item.AuthoritySourceLine > 0)
                {
                    UnityEngine.Object script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                    if (script != null) AssetDatabase.OpenAsset(script, item.AuthoritySourceLine);
                    return;
                }
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset != null)
                {
                    EditorGUIUtility.PingObject(asset);
                    Selection.activeObject = asset;
                }
                else if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                    if (script != null) AssetDatabase.OpenAsset(script);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Incident Investigator could not open authority source " + path + ": " + ex.Message);
            }
        }
    }
}
#endif
