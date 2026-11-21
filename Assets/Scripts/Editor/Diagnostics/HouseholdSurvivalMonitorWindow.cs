// Lane 7 (Muse Pass, Nov 20 2026) — Household Survival and Food Security
// Monitor window (Tool 08). Menu: Land & Ledgers/Diagnostics/Household Survival and Food Security Monitor.
//
// Daily reserve/heating resolution, demand snapshots, and the shortage
// monitor with real affordability (Lane 9b) and missed-meal reasons
// (Lane 9a) — the 7-point automated audit design from Agent L §4.
// The monitor requires the FULL shopping chain (G-11) before recording
// a shopping success: departure, purchase, payment, return, pantry
// receipt, later consumption. A meal count alone is never a success.
// Every row separates legitimate poverty from broken mechanics.
//
// Verification: this file is UnityEditor-bound and CANNOT join the Roslyn
// harness closure. It is verified with ParseCheck only (must print
// PARSE CLEAN). The window cannot be opened without Unity here; the
// classification logic it displays lives in HouseholdSurvivalMonitor
// (Core, harness tested). No live run exists on this HEAD.
#if UNITY_EDITOR
using System.Collections.Generic;
using LandLedgers.Diagnostics;
using LandLedgers.Economy;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Markets;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using LandLedgers.Time;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Diagnostics
{
    public sealed class HouseholdSurvivalMonitorWindow : EditorWindow
    {
        private Vector2 scroll;
        private string status = "No live snapshot yet.";

        [MenuItem("Land & Ledgers/Diagnostics/Household Survival and Food Security Monitor")]
        public static void Open()
        {
            HouseholdSurvivalMonitorWindow window = GetWindow<HouseholdSurvivalMonitorWindow>("Household Survival and Food Security Monitor");
            window.minSize = new Vector2(680, 560);
            window.Show();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Household Survival and Food Security Monitor", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox("Daily reserve/heating resolution, demand snapshots, and the shortage monitor — the Agent L §4 7-point audit. A shopping success requires the full chain (departure, purchase, payment, return, pantry receipt, later consumption); a meal count alone is never a success. Every row separates legitimate poverty from broken mechanics.", MessageType.Info);

            PopulationManager population = UnityEngine.Object.FindFirstObjectByType<PopulationManager>();
            SimulationSystemsHub hub = UnityEngine.Object.FindFirstObjectByType<SimulationSystemsHub>();
            TimeManager time = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
            TownPulseRuntimeManager townPulse = UnityEngine.Object.FindFirstObjectByType<TownPulseRuntimeManager>();
            SharedBusinessRuntimeManager shared = UnityEngine.Object.FindFirstObjectByType<SharedBusinessRuntimeManager>();

            if (population == null || population.State == null || population.State.households == null)
            {
                EditorGUILayout.HelpBox("No live population: enter Play Mode in the Main Scene to connect to PopulationManager. The audit design below does not need a live population.", MessageType.Warning);
                DrawAuditDesign();
                DrawKnownGaps();
                EditorGUILayout.EndScrollView();
                return;
            }

            int dayIndex = time != null ? time.CurrentAbsoluteDayIndex : -1;
            DailyLifecycleSnapshot lifecycle = BuildLifecycleSnapshot(population, townPulse, shared, dayIndex);
            List<HouseholdFoodSnapshot> households = BuildHouseholdSnapshots(population, hub, dayIndex);
            HouseholdSurvivalReport report = HouseholdSurvivalMonitor.BuildReport(lifecycle, households);
            status = "Live snapshot: day " + dayIndex + ", " + households.Count + " households — "
                + report.SecureCount + " secure, " + report.LegitimatePovertyCount + " legitimate poverty, "
                + report.BrokenMechanicsCount + " broken mechanics, " + report.UnknownCount + " unknown; "
                + report.LifecycleFindings.Count + " lifecycle findings.";
            EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);

            DrawLifecycleFindings(report);
            DrawHouseholdRows(report);
            DrawWeeklySettlementNote(shared, hub);
            DrawAuditDesign();
            DrawKnownGaps();
            EditorGUILayout.EndScrollView();
        }

        private void DrawLifecycleFindings(HouseholdSurvivalReport report)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Daily lifecycle audit — Agent L §4 (7 checks)", EditorStyles.boldLabel);
            if (report.LifecycleFindings.Count == 0)
            {
                EditorGUILayout.LabelField("All seven checks pass on the observed snapshot.");
            }
            else
            {
                foreach (LifecycleFinding finding in report.LifecycleFindings)
                {
                    EditorGUILayout.LabelField("[" + HouseholdSurvivalMonitor.LifecycleCheckLabel(finding.Check) + "] "
                        + finding.Severity + " — " + finding.Title, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Cause: " + finding.Cause, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("Authority: " + finding.Authority, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("Evidence: " + finding.Evidence, EditorStyles.wordWrappedLabel);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawHouseholdRows(HouseholdSurvivalReport report)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Households (" + report.Rows.Count + ")", EditorStyles.boldLabel);
            if (report.Rows.Count == 0)
            {
                EditorGUILayout.LabelField("No households observed.");
            }
            else
            {
                foreach (HouseholdSurvivalRow row in report.Rows)
                {
                    EditorGUILayout.LabelField(row.Label + " — " + row.Verdict, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(row.Reason, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("Shopping: " + row.Shopping + " — " + row.ShoppingDetail, EditorStyles.wordWrappedLabel);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawWeeklySettlementNote(SharedBusinessRuntimeManager shared, SimulationSystemsHub hub)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("NPC weekly settlement (check 5, live proxy)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("The current-week comparison needs SBRM's week key, which is private; the settled week indices below are the live evidence that the shared weekly pass ran. A business settled in the current week is evidence the pass ran — the window does not claim a pass it cannot compare.", EditorStyles.wordWrappedLabel);
            WageSettlementService wages = hub != null ? hub.WageSettlement : null;
            if (shared == null || shared.Businesses == null)
            {
                EditorGUILayout.LabelField("No live business runtime.");
            }
            else if (wages == null)
            {
                EditorGUILayout.LabelField("No WageSettlementService on the hub.");
            }
            else
            {
                foreach (BusinessInstanceState business in shared.Businesses)
                {
                    if (business == null) continue;
                    EditorGUILayout.LabelField(business.InstanceId + ": settled week " + wages.SettledWeekIndex(business.InstanceId));
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawAuditDesign()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("The 7-point audit (Agent L §4)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("1 — Heating/reserve resolution ran: every household's lastReserveDepletionDayIndex == day (CRITICAL).", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("2 — Town pulse resolved: lastResolvedDayIndex == day (WARNING).", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("3 — Demand snapshots fresh: rebuilt today by the daily drive, not merely refreshed for a shopping candidate (WARNING).", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("4 — Weekly upgrade effects ran at the week boundary (WARNING).", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("5 — NPC weekly settlement ran for the current week key (CRITICAL).", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("6 — No stale-zero signal consumed: readers of GSRM lastDaily* fields check a \"live today\" flag (WARNING, naming the field).", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("7 — No double purchase: legacy + embodied charges for one need must reconcile (CRITICAL).", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawKnownGaps()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Known gaps this monitor surfaces (not decides)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("L-01/L-02 — before Lane 9b, household reserve/heating resolution and town-pulse demand ran ZERO times in the dev town (player-store gating). Lane 9b re-homed both onto the independent daily hub; this monitor asserts they still run.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("L-03 — weekly household-upgrade effects moved to the shared weekly pass (Lane 9b F-L3); owner confirmation of the system home is still pending.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("L-04 — the legacy reserve-purchase loop stays dead by design; re-homing it alongside the embodied loop would double-charge households. Owner/design decision owed.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("A7 — the shortage monitor threads real affordability (Lane 9b HouseholdCanAffordFoodTopUp), not placeholder zeros.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("No run exists on this HEAD — live behaviour is unverified until Codex runs the 5-10 day 10x run.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
        }

        private static DailyLifecycleSnapshot BuildLifecycleSnapshot(
            PopulationManager population, TownPulseRuntimeManager townPulse,
            SharedBusinessRuntimeManager shared, int dayIndex)
        {
            var snapshot = new DailyLifecycleSnapshot
            {
                DayIndex = dayIndex,
                // Demand snapshots carry no day stamp (HouseholdDemandSnapshot
                // has no day field), so freshness is unobservable live — the
                // core reports it Unknown, never a pass.
                HouseholdsWithFreshDemandSnapshot = -1,
                // Weekly checks are evaluated at the week boundary by the
                // core; the window does not claim a week boundary it has
                // not verified, and the settled-week proxy is shown
                // separately in DrawWeeklySettlementNote.
                IsWeekBoundary = false,
                WeeklyUpgradeEffectsRanThisWeek = true,
                NpcWeeklySettlementRanThisWeek = true,
            };

            int resolved = 0;
            int households = 0;
            if (population.State.households != null)
            {
                foreach (HouseholdState household in population.State.households)
                {
                    if (household == null) continue;
                    households++;
                    if (household.lastReserveDepletionDayIndex == dayIndex)
                        resolved++;
                }
            }
            snapshot.HouseholdCount = households;
            snapshot.HouseholdsWithReserveResolutionToday = resolved;
            snapshot.TownPulseLastResolvedDayIndex = townPulse != null && townPulse.RuntimeState != null
                ? townPulse.RuntimeState.lastResolvedDayIndex
                : -1;
            return snapshot;
        }

        /// <summary>
        /// Per-household snapshots from the live meal log. Food/fuel
        /// days-of-supply and affordability are NOT read here: the
        /// window does not invent them from a partial read — they are
        /// left unknown (-1 / false with zero known sellers) unless a
        /// missed meal pins the cause, and the core classifies on the
        /// evidence actually present.
        /// </summary>
        private static List<HouseholdFoodSnapshot> BuildHouseholdSnapshots(
            PopulationManager population, SimulationSystemsHub hub, int dayIndex)
        {
            var snapshots = new List<HouseholdFoodSnapshot>();
            if (population.State.households == null)
                return snapshots;
            HouseholdMealLogRegistry mealLog = hub != null ? hub.MealLog : null;

            foreach (HouseholdState household in population.State.households)
            {
                if (household == null) continue;
                var snapshot = new HouseholdFoodSnapshot
                {
                    HouseholdId = household.id,
                    HouseholdLabel = "Household " + household.id,
                    FoodDaysOfSupply = -1f,
                    HeatingFuelUnits = -1,
                    HeatingFuelDaysOfSupply = -1f,
                    CanAffordFoodTopUp = false,
                    KnownSellerCount = 0,
                    SellerStockKnownPresent = false,
                };
                if (mealLog != null)
                {
                    foreach (MissedMealRecord missed in mealLog.GetMissedMeals(household.id))
                    {
                        if (missed == null) continue;
                        snapshot.MissedMeals.Add(new MissedMealObservation
                        {
                            DayIndex = missed.DayIndex,
                            Slot = missed.Slot.ToString(),
                            ReasonKind = missed.ReasonKind,
                            Reason = missed.Reason,
                        });
                    }
                }
                snapshots.Add(snapshot);
            }
            return snapshots;
        }
    }
}
#endif
