// Lane 7 (Muse Pass, Nov 20 2026) — Supply Chain Dependency Explorer
// window (Tool 05). Menu: Land & Ledgers/Diagnostics/Supply Chain Dependency Explorer.
//
// Follow a good from source to shelf through the dependency graph —
// flour, fuel, timber, bread — naming each authority and each break.
// The trace stops at the FIRST supported blocker. A clean trace means
// "no supported blocker found", never "the chain flows".
//
// Verification: this file is UnityEditor-bound and CANNOT join the Roslyn
// harness closure. It is verified with ParseCheck only (must print
// PARSE CLEAN). The window cannot be opened without Unity here; the
// chain definitions and trace logic live in SupplyChainDependencyExplorer
// (Core, harness tested). No live run exists on this HEAD.
#if UNITY_EDITOR
using System.Collections.Generic;
using LandLedgers.Diagnostics;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.FuelDealer;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Persistence;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Diagnostics
{
    public sealed class SupplyChainDependencyExplorerWindow : EditorWindow
    {
        private string selectedGood = "bread";
        private Vector2 scroll;
        private string status = "No live trace yet.";

        [MenuItem("Land & Ledgers/Diagnostics/Supply Chain Dependency Explorer")]
        public static void Open()
        {
            SupplyChainDependencyExplorerWindow window = GetWindow<SupplyChainDependencyExplorerWindow>("Supply Chain Dependency Explorer");
            window.minSize = new Vector2(680, 560);
            window.Show();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Supply Chain Dependency Explorer", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox("Follow a good from source to shelf — flour, fuel, timber, bread — naming each authority and each break. The trace stops at the first supported blocker. Nodes with no live observation are Unknown, never blockers.", MessageType.Info);

            SaveLoadManager save = UnityEngine.Object.FindFirstObjectByType<SaveLoadManager>();

            EditorGUILayout.BeginHorizontal();
            foreach (string good in SupplyChainDependencyExplorer.Goods)
            {
                bool selected = string.Equals(good, selectedGood, System.StringComparison.OrdinalIgnoreCase);
                if (GUILayout.Toggle(selected, SupplyChainDependencyExplorer.GoodLabel(good), "Button"))
                    selectedGood = good;
            }
            EditorGUILayout.EndHorizontal();

            List<SupplyNodeObservation> observations = BuildObservations(selectedGood, save);
            SupplyChainTrace trace = SupplyChainDependencyExplorer.Trace(selectedGood, observations);
            status = "Trace for " + SupplyChainDependencyExplorer.GoodLabel(selectedGood) + ": "
                + (trace.Clean ? "no supported blocker found." : "blocked at '" + trace.BlockerNode.StageLabel + "'.");
            EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);

            DrawTrace(trace);
            DrawChain(selectedGood, observations);
            DrawKnownBreaks(selectedGood);
            EditorGUILayout.EndScrollView();
        }

        private void DrawTrace(SupplyChainTrace trace)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("First supported blocker", EditorStyles.boldLabel);
            if (trace.Clean)
            {
                EditorGUILayout.LabelField(trace.Cause, EditorStyles.wordWrappedLabel);
            }
            else
            {
                EditorGUILayout.LabelField("[" + trace.BlockerNode.Id + "] " + trace.BlockerNode.StageLabel
                    + " — " + trace.Severity, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Cause: " + trace.Cause, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Authority: " + trace.Authority, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Evidence: " + trace.Evidence, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawChain(string goodId, List<SupplyNodeObservation> observations)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Chain — source to shelf", EditorStyles.boldLabel);
            var observed = new Dictionary<string, SupplyNodeObservation>(System.StringComparer.OrdinalIgnoreCase);
            foreach (SupplyNodeObservation observation in observations)
            {
                if (observation != null && !string.IsNullOrWhiteSpace(observation.NodeId))
                    observed[observation.NodeId] = observation;
            }
            foreach (SupplyChainNode node in SupplyChainDependencyExplorer.KnownChain(goodId))
            {
                observed.TryGetValue(node.Id, out SupplyNodeObservation observation);
                string state = observation == null ? "Unknown (no live observation)" : observation.State.ToString()
                    + (observation.StockUnits >= 0 ? " (" + observation.StockUnits + "u)" : "");
                EditorGUILayout.LabelField(node.StageLabel + " — " + state, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Authority: " + node.Authority, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Evidence: " + node.Evidence, EditorStyles.wordWrappedLabel);
                if (!string.IsNullOrWhiteSpace(node.KnownBreak))
                    EditorGUILayout.LabelField("Known break: " + node.KnownBreak, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawKnownBreaks(string goodId)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Known breaks on this chain (source-reviewed)", EditorStyles.boldLabel);
            bool any = false;
            foreach (SupplyChainNode node in SupplyChainDependencyExplorer.KnownChain(goodId))
            {
                if (string.IsNullOrWhiteSpace(node.KnownBreak)) continue;
                any = true;
                EditorGUILayout.LabelField(node.StageLabel + ": " + node.KnownBreak, EditorStyles.wordWrappedLabel);
            }
            if (!any)
                EditorGUILayout.LabelField("No break is recorded on this chain's legs in the current evidence.");
            EditorGUILayout.LabelField("No run exists on this HEAD — live behaviour is unverified until Codex runs the 5-10 day 10x run.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Live observations from the Pass 9 runtimes (the same sources
        /// the Business Operations Monitor reads). Nodes with no live
        /// source stay Unknown: felling, log haul, sawmill, yard
        /// transfer and carpentry procurement have no safe public
        /// live accessor in this window, so they are not guessed.
        /// </summary>
        private static List<SupplyNodeObservation> BuildObservations(string goodId, SaveLoadManager save)
        {
            var observations = new List<SupplyNodeObservation>();
            if (save == null || save.Series2Pass9State == null)
                return observations;

            Series2Pass9SaveState pass9 = save.Series2Pass9State;
            BakeryShopRuntime bakery = pass9.BakeryRuntime;
            FuelDealerRuntime dealer = pass9.FuelDealerRuntime;

            if (bakery != null)
            {
                int flourUnits = 0;
                int notionsUnits = 0;
                if (bakery.FlourStock != null && bakery.FlourStock.Lots != null)
                {
                    foreach (BakeryFlourLot lot in bakery.FlourStock.Lots)
                    {
                        if (lot == null) continue;
                        if (string.Equals(lot.FlourName, BakeryBreadCatalog.NotionsItemId, System.StringComparison.Ordinal))
                            notionsUnits += System.Math.Max(0, lot.Units);
                        else
                            flourUnits += System.Math.Max(0, lot.Units);
                    }
                }
                int fuelUnits = 0;
                if (bakery.FuelStock != null && bakery.FuelStock.Lots != null)
                {
                    foreach (BakeryFuelLot lot in bakery.FuelStock.Lots)
                    {
                        if (lot != null) fuelUnits += System.Math.Max(0, lot.Units);
                    }
                }
                int activeBatches = 0;
                if (bakery.Batches != null)
                {
                    foreach (BakeryDoughBatch batch in bakery.Batches)
                    {
                        if (batch != null && batch.IsActive) activeBatches++;
                    }
                }

                if (goodId == "flour")
                {
                    observations.Add(new SupplyNodeObservation
                    {
                        NodeId = "flour-bakery-bin",
                        State = flourUnits > 0 ? SupplyNodeState.Present : SupplyNodeState.Empty,
                        StockUnits = flourUnits,
                    });
                }
                if (goodId == "bread")
                {
                    observations.Add(new SupplyNodeObservation
                    {
                        NodeId = "bread-flour",
                        State = flourUnits > 0 ? SupplyNodeState.Present : SupplyNodeState.Empty,
                        StockUnits = flourUnits,
                    });
                    observations.Add(new SupplyNodeObservation
                    {
                        NodeId = "bread-fuel",
                        State = fuelUnits > 0 ? SupplyNodeState.Present : SupplyNodeState.Empty,
                        StockUnits = fuelUnits,
                    });
                    observations.Add(new SupplyNodeObservation
                    {
                        NodeId = "bread-notions",
                        State = notionsUnits > 0 ? SupplyNodeState.Present : SupplyNodeState.Empty,
                        StockUnits = notionsUnits,
                    });
                    observations.Add(new SupplyNodeObservation
                    {
                        NodeId = "bread-staged-batches",
                        State = activeBatches > 0 ? SupplyNodeState.Present : SupplyNodeState.Unknown,
                        StockUnits = activeBatches,
                        Note = activeBatches > 0 ? activeBatches + " active batch(es)." : "No active batch observed (not a blocker by itself).",
                    });
                }
                if (goodId == "fuel")
                {
                    observations.Add(new SupplyNodeObservation
                    {
                        NodeId = "fuel-bakery-source",
                        State = fuelUnits > 0 ? SupplyNodeState.Present : SupplyNodeState.Empty,
                        StockUnits = fuelUnits,
                    });
                }
            }

            if (dealer != null && (goodId == "fuel"))
            {
                observations.Add(new SupplyNodeObservation
                {
                    NodeId = "fuel-dealer-stock",
                    State = dealer.StockIntakeUnits > 0 ? SupplyNodeState.Present : SupplyNodeState.Empty,
                    StockUnits = dealer.StockIntakeUnits,
                    Note = "D-18: dealer stock is observable, but households cannot discover the dealer without seller-knowledge seeding (Lane 9e).",
                });
            }

            return observations;
        }
    }
}
#endif
