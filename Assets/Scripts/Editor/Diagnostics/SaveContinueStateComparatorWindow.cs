// Lane 7 (Muse Pass, Nov 20 2026) — Save/Continue State Comparator
// window (Tool 07). Menu: Land & Ledgers/Diagnostics/Save-Continue State Comparator.
//
// Compare a save's state against the live Continue state — what was
// lost, duplicated, or changed. Capture a pre-save snapshot, save and
// Continue, capture the post-restore snapshot, and compare. Only
// meaningful changes are defects; legitimate restoration
// transformations are defined by the existing authorities (Lane 9a
// staple_food flagging; Lane 9f presentation rebind). A Continue that
// loses an in-progress batch or duplicates a settled lot is a defect
// the comparator must catch, not a transformation it accepts.
//
// Snapshots are held in memory for this window session only. The
// capture covers the fields with safe public live accessors; the rest
// of the Agent F §5 table is shown as expectations, not as captured
// data — the window never claims a field it did not capture.
//
// Verification: this file is UnityEditor-bound and CANNOT join the Roslyn
// harness closure. It is verified with ParseCheck only (must print
// PARSE CLEAN). The window cannot be opened without Unity here; the
// comparison logic lives in SaveContinueStateComparator (Core, harness
// tested). No live run exists on this HEAD — no Continue round-trip has
// been observed.
#if UNITY_EDITOR
using System.Collections.Generic;
using LandLedgers.Diagnostics;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.FuelDealer;
using LandLedgers.Economy.GeneralStore;
using LandLedgers.FirstLedger;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Persistence;
using LandLedgers.Population;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Diagnostics
{
    public sealed class SaveContinueStateComparatorWindow : EditorWindow
    {
        private List<PersistedField> preSaveSnapshot;
        private List<PersistedField> postRestoreSnapshot;
        private SaveContinueComparisonReport lastReport;
        private Vector2 scroll;
        private string status = "No snapshots captured yet.";

        [MenuItem("Land & Ledgers/Diagnostics/Save-Continue State Comparator")]
        public static void Open()
        {
            SaveContinueStateComparatorWindow window = GetWindow<SaveContinueStateComparatorWindow>("Save-Continue State Comparator");
            window.minSize = new Vector2(680, 560);
            window.Show();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Save/Continue State Comparator", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox("Capture a pre-save snapshot, save and Continue, capture the post-restore snapshot, then compare. Only meaningful changes are defects. Snapshots live in memory for this window session only.", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Capture pre-save snapshot"))
            {
                preSaveSnapshot = CaptureSnapshot("pre-save");
                status = "Pre-save snapshot captured (" + preSaveSnapshot.Count + " fields). Now save and Continue, then capture the post-restore snapshot.";
            }
            if (GUILayout.Button("Capture post-restore snapshot"))
            {
                postRestoreSnapshot = CaptureSnapshot("post-restore");
                status = "Post-restore snapshot captured (" + postRestoreSnapshot.Count + " fields). Compare when both are captured.";
            }
            if (GUILayout.Button("Compare"))
            {
                if (preSaveSnapshot == null || postRestoreSnapshot == null)
                {
                    status = "Cannot compare: capture both snapshots first (pre-save, then post-restore).";
                }
                else
                {
                    lastReport = SaveContinueStateComparator.Compare(preSaveSnapshot, postRestoreSnapshot);
                    status = "Compared " + lastReport.Comparisons.Count + " fields: " + lastReport.DefectCount
                        + " defect(s), " + lastReport.RefusedLoudlyCount + " refused loudly, "
                        + lastReport.LegitimateTransformationCount + " legitimate transformation(s).";
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);

            DrawReport();
            DrawPersistenceTable();
            DrawKnownGaps();
            EditorGUILayout.EndScrollView();
        }

        private void DrawReport()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Comparison", EditorStyles.boldLabel);
            if (lastReport == null)
            {
                EditorGUILayout.LabelField("No comparison yet. Capture both snapshots and press Compare.");
                EditorGUILayout.LabelField("Pre-save: " + (preSaveSnapshot == null ? "not captured" : preSaveSnapshot.Count + " fields")
                    + " | Post-restore: " + (postRestoreSnapshot == null ? "not captured" : postRestoreSnapshot.Count + " fields"));
            }
            else if (lastReport.Comparisons.Count == 0)
            {
                EditorGUILayout.LabelField("Both snapshots are empty — nothing was captured to compare.");
            }
            else
            {
                foreach (StateFieldComparison comparison in lastReport.Comparisons)
                {
                    EditorGUILayout.LabelField(comparison.System + " / " + comparison.Field + " — "
                        + SaveContinueStateComparator.OutcomeLabel(comparison.Outcome), EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(comparison.Detail, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("Authority: " + comparison.Authority, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("Evidence: " + comparison.Evidence, EditorStyles.wordWrappedLabel);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawPersistenceTable()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Persistence expectations (Agent F §5, DTO-verified)", EditorStyles.boldLabel);
            foreach (PersistenceExpectation expectation in SaveContinueStateComparator.KnownPersistence())
            {
                EditorGUILayout.LabelField((expectation.Persisted ? "[persisted] " : "[NOT persisted] ")
                    + expectation.System + " / " + expectation.Field, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("  " + expectation.Authority + " — " + expectation.Note, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawKnownGaps()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Known gaps this comparator catches (not decides)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("F R-2 — ActiveProcesses had no save path pre-9f: an in-flight furniture batch and its consumed inputs vanished on Continue. Lane 9f repaired the restore path (restore or refuse loudly); this comparator still catches any loss.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("F R-3 — purchased/manufactured furnishings with no configured dev-town item have no presentation after Continue (counted and diagnosed, never silently absent). No kind-to-prefab mapping exists anywhere in source.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("F R-4 — storage re-bind pinned by Lane 9f tests (StorageFixtures 0 -> 1 after RefreshInventory + RestoreBindings).", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("C-5 / yard queue / carpentry F4 records — still unread in places; the comparator flags what it captures and names what it does not.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("No run exists on this HEAD — no Continue round-trip has been observed. The runner has no Continue round-trip mode yet (Lane 8).", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Capture the fields with safe public live accessors. Fields
        /// without one are NOT captured and NOT claimed — the
        /// persistence table above names them as expectations instead.
        /// </summary>
        private static List<PersistedField> CaptureSnapshot(string side)
        {
            var fields = new List<PersistedField>();
            SaveLoadManager save = UnityEngine.Object.FindFirstObjectByType<SaveLoadManager>();
            SharedBusinessRuntimeManager shared = UnityEngine.Object.FindFirstObjectByType<SharedBusinessRuntimeManager>();
            GeneralStoreRuntimeManager storeManager = UnityEngine.Object.FindFirstObjectByType<GeneralStoreRuntimeManager>();

            if (save != null && save.Series2Pass9State != null)
            {
                Series2Pass9SaveState pass9 = save.Series2Pass9State;
                fields.Add(new PersistedField
                {
                    System = "Furniture",
                    Field = "Purchase orders",
                    ValueSummary = pass9.FurniturePurchaseOrders.Count + " order(s)",
                    Count = pass9.FurniturePurchaseOrders.Count,
                    Present = true,
                });
                int bindingCount = 0;
                foreach (HouseholdFurnishingRegister register in pass9.HouseholdFurnishingRegisters)
                {
                    if (register != null && register.Bindings != null)
                        bindingCount += register.Bindings.Count;
                }
                fields.Add(new PersistedField
                {
                    System = "Furniture",
                    Field = "Household furnishing registers",
                    ValueSummary = pass9.HouseholdFurnishingRegisters.Count + " register(s), " + bindingCount + " binding(s)",
                    Count = bindingCount,
                    Present = true,
                });
                fields.Add(new PersistedField
                {
                    System = "Household",
                    Field = "Stove fuel stores",
                    ValueSummary = pass9.HouseholdStoveFuelStores.Count + " store(s)",
                    Count = pass9.HouseholdStoveFuelStores.Count,
                    Present = true,
                });

                BakeryShopRuntime bakery = pass9.BakeryRuntime;
                if (bakery != null)
                {
                    int activeBatches = 0;
                    if (bakery.Batches != null)
                    {
                        foreach (BakeryDoughBatch batch in bakery.Batches)
                        {
                            if (batch != null && batch.IsActive) activeBatches++;
                        }
                    }
                    fields.Add(new PersistedField
                    {
                        System = "Production",
                        Field = "Bakery batches (in-progress)",
                        ValueSummary = activeBatches + " active batch(es) of " + (bakery.Batches != null ? bakery.Batches.Count : 0) + " total",
                        Count = activeBatches,
                        Present = true,
                    });
                }
            }

            if (shared != null && shared.Businesses != null)
            {
                int activeProcesses = 0;
                foreach (BusinessInstanceState business in shared.Businesses)
                {
                    if (business == null || business.GenericConfiguration == null) continue;
                    activeProcesses += business.GenericConfiguration.ActiveProcesses.Count;
                }
                fields.Add(new PersistedField
                {
                    System = "Production",
                    Field = "ActiveProcesses (in-flight generic production)",
                    ValueSummary = activeProcesses + " active process(es) across all businesses",
                    Count = activeProcesses,
                    Present = true,
                });

                if (storeManager != null)
                {
                    foreach (BusinessInstanceState business in shared.Businesses)
                    {
                        if (business == null) continue;
                        CustomerQueue queue = storeManager.CustomerQueueForBusiness(business);
                        if (queue == null) continue;
                        fields.Add(new PersistedField
                        {
                            System = "Business",
                            Field = "Customer queue (" + business.InstanceId + ")",
                            ValueSummary = queue.WaitingCount + " waiting",
                            Count = queue.WaitingCount,
                            Present = true,
                        });
                    }
                }
            }

            return fields;
        }
    }
}
#endif
