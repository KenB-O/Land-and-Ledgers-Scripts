// Lane 7 (Muse Pass, Nov 20 2026) — World Accessibility and Interaction
// Auditor window (Tool 06). Menu: Land & Ledgers/Diagnostics/World Accessibility and Interaction Auditor.
//
// The three destination regimes from Agent M §2 are the framing:
// A physical arrival at the item, B arrival at the building only,
// C timers with no destination. The data source is the Lane 9c startup
// validation (B-7 / M-R13): this window RE-RUNS the same public
// resolver (BuildingFootprintAuthority.TryFindReachableActivityLocation)
// per building and surfaces the gaps — it does not re-implement the
// validation and never fabricates coordinates or aliases the porch
// point as Firewood. Every gap carries its Lane 1 refusal class.
//
// Verification: this file is UnityEditor-bound and CANNOT join the Roslyn
// harness closure. It is verified with ParseCheck only (must print
// PARSE CLEAN). The window cannot be opened without Unity here; the
// classification logic it displays lives in WorldAccessibilityAuditor
// (Core, harness tested). No live run exists on this HEAD.
#if UNITY_EDITOR
using System.Collections.Generic;
using LandLedgers.Diagnostics;
using LandLedgers.Economy;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using LandLedgers.World;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Diagnostics
{
    public sealed class WorldAccessibilityAuditorWindow : EditorWindow
    {
        private string taskFamilyQuery = "domestic.meal-prep";
        private Vector2 scroll;
        private string status = "No live validation yet.";

        [MenuItem("Land & Ledgers/Diagnostics/World Accessibility and Interaction Auditor")]
        public static void Open()
        {
            WorldAccessibilityAuditorWindow window = GetWindow<WorldAccessibilityAuditorWindow>("World Accessibility Auditor");
            window.minSize = new Vector2(680, 560);
            window.Show();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("World Accessibility and Interaction Auditor", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox("Surfaces the Lane 9c startup validation (B-7 / M-R13) per home and per business: Stove, Cleaning, Firewood and Dining per home; Counter and Receiving per business — each authored and reachable from main-entrance. It observes the live footprint authorities; it authors no points and fabricates no coordinates.", MessageType.Info);

            TownWorldController townWorld = UnityEngine.Object.FindFirstObjectByType<TownWorldController>();
            PopulationManager population = UnityEngine.Object.FindFirstObjectByType<PopulationManager>();
            SharedBusinessRuntimeManager shared = UnityEngine.Object.FindFirstObjectByType<SharedBusinessRuntimeManager>();

            if (townWorld == null || townWorld.Buildings == null)
            {
                EditorGUILayout.HelpBox("No live world: enter Play Mode in the Main Scene to connect to TownWorldController. The config inventory and regime table below do not need a live world.", MessageType.Warning);
                DrawConfigInventory();
                DrawRegimeTable();
                DrawTaskFamilyClassifier();
                EditorGUILayout.EndScrollView();
                return;
            }

            List<AccessPointCheck> checks = BuildChecks(townWorld, population, shared);
            WorldAccessibilityReport report = WorldAccessibilityAuditor.BuildReport(checks);
            status = "Live validation: " + checks.Count + " point checks across " + townWorld.Buildings.Count
                + " buildings; " + report.Gaps.Count + " gaps (" + report.MissingCount + " missing, "
                + report.UnreachableCount + " unreachable).";
            EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);

            DrawGaps(report);
            DrawConfigInventory();
            DrawRegimeTable();
            DrawTaskFamilyClassifier();
            DrawKnownGaps();
            EditorGUILayout.EndScrollView();
        }

        private void DrawGaps(WorldAccessibilityReport report)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Access gaps (" + report.Gaps.Count + ")", EditorStyles.boldLabel);
            if (report.Gaps.Count == 0)
            {
                EditorGUILayout.LabelField("No gap found in the checks run. A clean result here means the checked points are authored and reachable — it does not prove every task can arrive (Regime C tasks have no destination at all).");
            }
            else
            {
                foreach (AccessGap gap in report.Gaps)
                {
                    EditorGUILayout.LabelField("[" + gap.Id + "] " + gap.BuildingLabel + " — " + gap.PointKind
                        + " (" + gap.Kind + ", refusal class " + gap.RefusalClass + ")", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(gap.Detail, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("Authority: " + gap.Authority, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("Evidence: " + gap.Evidence, EditorStyles.wordWrappedLabel);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawConfigInventory()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Config inventory (Agent B / M-13, re-verified)", EditorStyles.boldLabel);
            foreach (ConfigInventoryFact fact in WorldAccessibilityAuditor.KnownConfigInventory())
            {
                EditorGUILayout.LabelField(fact.PointKind + ": " + fact.AuthoredCount + " authored — " + fact.Note,
                    EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawRegimeTable()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("The three destination regimes (Agent M §2)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("A — physical arrival at the item: domestic tasks, sleep, dining. Must arrive at the authored activity point before work accrues.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("B — arrival at the building only: business work, travel, freight receiving. Only when an on-site Work reservation overlaps AND the task has no CustomerRef.", EditorStyles.wordWrappedLabel);
            foreach (string hole in WorldAccessibilityAuditor.BuildingGateHoles)
            {
                EditorGUILayout.LabelField("  Gate hole: " + hole, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.LabelField("C — timers, no destination: service, production, freight-handling, construction, farming, service-business tasks. Minutes accrue wherever the worker is.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawTaskFamilyClassifier()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Task-family regime lookup (Agent M §3)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            taskFamilyQuery = EditorGUILayout.TextField(taskFamilyQuery);
            if (GUILayout.Button("Classify", GUILayout.Width(80)))
            {
                // Classification runs on Repaint via the stored query below.
            }
            EditorGUILayout.EndHorizontal();
            TaskFamilyRegime regime = WorldAccessibilityAuditor.ClassifyTaskFamily(taskFamilyQuery);
            EditorGUILayout.LabelField(WorldAccessibilityAuditor.RegimeLabel(regime.Regime), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(regime.DestinationExpectation, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Evidence: " + regime.Evidence, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawKnownGaps()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Known gaps this auditor surfaces (not decides)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("B-03 — zero Firewood-type locations anywhere; domestic.fuel-handling leg 0 can never run.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("B-01/B-02 — meal prep routes to literal \"stove\", chores to literal \"cleaning\" / \"porch-dropoff\"; the type-based resolver (TryFindReachableActivityLocation) is wired by Lane 9c — this window re-runs it, it does not rewrite it.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("M-04 — domestic.animal-care has no resolver branch (returns Unsupported) yet is scheduled daily when a household owns animals.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("M-07/M-08 — the bakery and fuel dealer have no counter point; the only Counter-type point is general-store-counter.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("M-15 — the \"receiving\" exact-id lookup fails on every building; the authored front-door fallback is the universal path.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("No run exists on this HEAD — live behaviour is unverified until Codex runs the 5-10 day 10x run.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Re-run the Lane 9c startup validation over the live world,
        /// using the same public resolver and the same point lists:
        /// Stove/Cleaning/Firewood/Dining per home, Counter/Receiving
        /// per business. Firewood needs presence only (exterior pile).
        /// </summary>
        private static List<AccessPointCheck> BuildChecks(
            TownWorldController townWorld, PopulationManager population, SharedBusinessRuntimeManager shared)
        {
            var checks = new List<AccessPointCheck>();
            if (townWorld.Buildings == null)
                return checks;

            foreach (PlacedBuilding building in townWorld.Buildings)
            {
                if (building == null || building.runtimeFootprintAuthority == null)
                    continue;
                BuildingFootprintAuthority authority = building.runtimeFootprintAuthority;

                string businessKey = BusinessKeyForBuilding(shared, building.id);
                HouseholdState household = population != null
                    ? population.GetHouseholdByHomeBuildingId(building.id)
                    : null;

                if (!string.IsNullOrEmpty(businessKey))
                {
                    string label = "Business '" + businessKey + "' (building " + building.id + ")";
                    foreach (string kind in WorldAccessibilityAuditor.BusinessPointKinds)
                    {
                        checks.Add(BuildCheck(authority, building.id, label, kind, true, -1, businessKey));
                    }
                }
                else if (household != null)
                {
                    string label = "Home 'household " + household.id + "' (household " + household.id
                        + ", building " + building.id + ")";
                    foreach (string kind in WorldAccessibilityAuditor.HomePointKinds)
                    {
                        bool requiresRoute = !string.Equals(kind, "Firewood", System.StringComparison.Ordinal);
                        checks.Add(BuildCheck(authority, building.id, label, kind, requiresRoute, household.id, string.Empty));
                    }
                }
                else
                {
                    // A building that is neither a business nor a home: check the home list anyway
                    // so an unassigned building's gaps are visible, labelled by building id.
                    string label = "Building " + building.id + " (no household or business bound)";
                    foreach (string kind in WorldAccessibilityAuditor.HomePointKinds)
                    {
                        bool requiresRoute = !string.Equals(kind, "Firewood", System.StringComparison.Ordinal);
                        checks.Add(BuildCheck(authority, building.id, label, kind, requiresRoute, -1, string.Empty));
                    }
                }
            }
            return checks;
        }

        private static string BusinessKeyForBuilding(SharedBusinessRuntimeManager shared, int buildingId)
        {
            if (shared == null || shared.Businesses == null)
                return string.Empty;
            foreach (BusinessInstanceState business in shared.Businesses)
            {
                if (business != null && business.AssignedBuildingId == buildingId)
                    return business.InstanceId;
            }
            return string.Empty;
        }

        /// <summary>
        /// One check via the public resolver — the same computation
        /// DevelopmentBusinessInteriorInitializer.BuildAccessCheck
        /// performs (interior types through TryFindReachableActivityLocation;
        /// exterior types by presence scan). The window cannot call that
        /// internal method from the Editor assembly, so it repeats the
        /// public calls, not the decision: the gap decision lives in
        /// WorldAccessibilityAuditor.
        /// </summary>
        private static AccessPointCheck BuildCheck(
            BuildingFootprintAuthority authority, int buildingId, string label,
            string kind, bool requiresInteriorRoute, int householdId, string businessKey)
        {
            var check = new AccessPointCheck
            {
                BuildingLabel = label,
                BuildingId = buildingId,
                HouseholdId = householdId,
                BusinessKey = businessKey,
                PointKind = kind,
                RequiresInteriorRoute = requiresInteriorRoute,
            };
            if (!System.Enum.TryParse<BuildingActivityLocationType>(kind, out BuildingActivityLocationType type))
                return check;

            if (requiresInteriorRoute)
            {
                if (authority.TryFindReachableActivityLocation(type, "main-entrance", buildingId,
                        out ResolvedBuildingActivityLocation resolved,
                        out List<Vector3> waypoints, out string reason))
                {
                    check.Found = true;
                    check.LocationId = resolved.Id;
                    check.ReachableFromMainEntrance = true;
                    return check;
                }
                check.Reason = reason;
            }

            if (authority.ActivityLocations != null)
            {
                foreach (BuildingActivityLocationAuthoring candidate in authority.ActivityLocations)
                {
                    if (candidate != null && candidate.type == type)
                    {
                        check.Found = true;
                        check.LocationId = candidate.id;
                        break;
                    }
                }
            }
            return check;
        }
    }
}
#endif
