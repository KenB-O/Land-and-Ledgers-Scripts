// Lane 5 (Muse Pass, Nov 20 2026) — Business Operations Monitor
// window (Tool 04). Menu: Land & Ledgers/Diagnostics/Business Operations Monitor.
//
// Live per-business state — owner, employees, worker locations, queue,
// counter status, inventory, orders, cash, production blockers with
// specific causes — for the General Store, bakery, fuel dealer and the
// other businesses, so the owner can see a business's health without
// reading source. Observation only: it assigns no work, changes no
// routes, and moves no goods, money, food, transport or labour.
//
// This window IS the business inspector Lane 2 flagged as MISSING: the
// Incident Investigator's business jump-to opens this window pre-targeted
// at the incident's business (OpenForBusiness), showing that business's
// runtime state and its authority source.
//
// Verification: this file is UnityEditor-bound and CANNOT join the Roslyn
// harness closure. It is verified with ParseCheck only (must print
// PARSE CLEAN). The window cannot be opened without Unity here; the
// classification logic it displays lives in BusinessOperationsMonitor
// (Core, harness tested). No live run exists on this HEAD.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using LandLedgers.Diagnostics;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.FuelDealer;
using LandLedgers.Economy.GeneralStore;
using LandLedgers.Economy.Labor;
using LandLedgers.FirstLedger;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Time;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Diagnostics
{
    public sealed class BusinessOperationsMonitorWindow : EditorWindow
    {
        private string selectedBusinessKey = "general-store";
        private Vector2 scroll;
        private string status = "No live snapshot yet.";

        [MenuItem("Land & Ledgers/Diagnostics/Business Operations Monitor")]
        public static void Open()
        {
            BusinessOperationsMonitorWindow window = GetWindow<BusinessOperationsMonitorWindow>("Business Operations Monitor");
            window.minSize = new Vector2(680, 560);
            window.Show();
        }

        /// <summary>
        /// The business inspector entry point (Lane 2 jump-to target):
        /// open the monitor pre-targeted at one business key
        /// (general-store, bakery, fuel-dealer, freight-company, sawmill,
        /// lumber-yard, carpentry-workshop).
        /// </summary>
        public static void OpenForBusiness(string businessKey)
        {
            BusinessOperationsMonitorWindow window = GetWindow<BusinessOperationsMonitorWindow>("Business Operations Monitor");
            window.minSize = new Vector2(680, 560);
            if (!string.IsNullOrWhiteSpace(businessKey))
                window.selectedBusinessKey = businessKey.Trim();
            window.Show();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Business Operations Monitor", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox("Observes the authoritative business runtimes. It never assigns work, changes routes, or moves goods, money, food, transport or labour. Every blocker shown carries its specific cause — never a bare \"blocked\".", MessageType.Info);

            SharedBusinessRuntimeManager shared = UnityEngine.Object.FindFirstObjectByType<SharedBusinessRuntimeManager>();
            GeneralStoreRuntimeManager storeManager = UnityEngine.Object.FindFirstObjectByType<GeneralStoreRuntimeManager>();
            SaveLoadManager save = UnityEngine.Object.FindFirstObjectByType<SaveLoadManager>();
            SimulationSystemsHub hub = UnityEngine.Object.FindFirstObjectByType<SimulationSystemsHub>();
            TimeManager time = UnityEngine.Object.FindFirstObjectByType<TimeManager>();

            if (shared == null)
            {
                EditorGUILayout.HelpBox("No live runtime: enter Play Mode in the Main Scene to connect to SharedBusinessRuntimeManager. The expected-business list below is config-grounded and does not need a live runtime.", MessageType.Warning);
                DrawExpectedBusinesses(null);
                DrawKnownGaps();
                EditorGUILayout.EndScrollView();
                return;
            }

            int dayIndex = time != null ? time.CurrentAbsoluteDayIndex : -1;
            List<BusinessSnapshot> snapshots = BuildSnapshots(shared, storeManager, save, hub, dayIndex);
            List<BusinessSettlementCadence> cadences = BuildCadences(shared, storeManager, hub, snapshots);
            BusinessOperationsReport report = BusinessOperationsMonitor.BuildReport(snapshots, cadences);
            status = "Live snapshot: " + snapshots.Count + " businesses observed (day " + dayIndex + "); "
                + report.HealthyCount + " healthy, " + report.BlockedCount + " blocked, "
                + report.OwnerDecisionCount + " owner-decision, " + report.UnknownCount + " unknown.";

            EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);
            DrawOverview(report);
            DrawBusinessSelector(report);
            DrawSelectedBusiness(report, shared);
            DrawExpectedBusinesses(report);
            DrawKnownGaps();
            EditorGUILayout.EndScrollView();
        }

        private void DrawOverview(BusinessOperationsReport report)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("All businesses", EditorStyles.boldLabel);
            foreach (BusinessSnapshot snapshot in report.Snapshots)
            {
                BusinessHealthKind health = BusinessOperationsMonitor.ClassifyHealth(snapshot, BlockersFor(report, snapshot.Key));
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(snapshot.DisplayName + " (" + snapshot.BusinessTypeName + ")", GUILayout.Width(260));
                EditorGUILayout.LabelField(health.ToString(), GUILayout.Width(110));
                EditorGUILayout.LabelField(snapshot.OwnerDisplayName + " — " + snapshot.CashCents + "c", GUILayout.Width(220));
                if (GUILayout.Button("Inspect", GUILayout.Width(70)))
                    selectedBusinessKey = snapshot.Key;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawBusinessSelector(BusinessOperationsReport report)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Inspect one business", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            foreach (BusinessSnapshot snapshot in report.Snapshots)
            {
                bool selected = string.Equals(snapshot.Key, selectedBusinessKey, StringComparison.OrdinalIgnoreCase);
                if (GUILayout.Toggle(selected, snapshot.DisplayName, "Button"))
                    selectedBusinessKey = snapshot.Key;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawSelectedBusiness(BusinessOperationsReport report, SharedBusinessRuntimeManager shared)
        {
            BusinessSnapshot snapshot = null;
            foreach (BusinessSnapshot candidate in report.Snapshots)
            {
                if (string.Equals(candidate.Key, selectedBusinessKey, StringComparison.OrdinalIgnoreCase))
                {
                    snapshot = candidate;
                    break;
                }
            }
            if (snapshot == null)
            {
                EditorGUILayout.HelpBox("No business selected. Pick one above, or jump here from the Incident Investigator.", MessageType.None);
                return;
            }

            BusinessHealthKind health = BusinessOperationsMonitor.ClassifyHealth(snapshot, BlockersFor(report, snapshot.Key));
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(snapshot.DisplayName + " — " + health, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Owner", snapshot.OwnerDisplayName + " (" + snapshot.OwnerKind + ")");
            EditorGUILayout.LabelField("Type", snapshot.BusinessTypeName + " | Building " + snapshot.BuildingId);
            EditorGUILayout.LabelField("Cash", snapshot.CashCents + "c (opening " + snapshot.OpeningCashCents + "c)");
            EditorGUILayout.LabelField("Last day", "revenue " + snapshot.LastDailyRevenueCents + "c, net " + snapshot.LastDailyNetCents
                + "c, units sold " + snapshot.LastDailyUnitsSold);
            EditorGUILayout.LabelField("Last week", "cash after " + snapshot.LastWeeklyCashAfterCents + "c");
            EditorGUILayout.EndVertical();

            BusinessSettlementCadence cadence = CadenceFor(report, snapshot.Key);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Settlement cadences", EditorStyles.boldLabel);
            if (cadence != null)
            {
                EditorGUILayout.LabelField("Daily sales", BusinessOperationsMonitor.DailyCadenceLabel(cadence.DailyState)
                    + (string.IsNullOrWhiteSpace(cadence.DailyEvidence) ? "" : " — " + cadence.DailyEvidence), EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Weekly payroll", BusinessOperationsMonitor.WeeklyCadenceLabel(cadence.WeeklyState)
                    + (string.IsNullOrWhiteSpace(cadence.WeeklyEvidence) ? "" : " — " + cadence.WeeklyEvidence), EditorStyles.wordWrappedLabel);
            }
            else
            {
                EditorGUILayout.LabelField("No cadence observed for this business.");
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Workers (" + snapshot.FilledWorkerCount + " filled)", EditorStyles.boldLabel);
            if (snapshot.WorkerSlots.Count == 0)
            {
                EditorGUILayout.LabelField("No worker slots defined on the runtime.");
            }
            else
            {
                foreach (BusinessWorkerSnapshot slot in snapshot.WorkerSlots)
                {
                    string line = slot.DisplayName + ": "
                        + (slot.Filled ? slot.WorkerDisplayName + " (" + slot.WeeklyWageCents + "c/week"
                            + (slot.PaidActive ? ", paid active" : ", not paid active") + ")" : "unfilled")
                        + (slot.SuspendedForMissedPayroll ? " — SUSPENDED for missed payroll" : "")
                        + (slot.RequiredForOpening ? " (required for opening)" : "");
                    EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Inventory", EditorStyles.boldLabel);
            if (snapshot.CategoryStock.Count == 0)
            {
                EditorGUILayout.LabelField("No category stock on the runtime.");
            }
            else
            {
                foreach (BusinessStockSnapshot stock in snapshot.CategoryStock)
                {
                    EditorGUILayout.LabelField(stock.CategoryId + ": " + stock.CurrentUnits + "u / target "
                        + stock.TargetUnits + "u (sold " + stock.LastDailyUnitsSold + "u last day"
                        + (stock.MissingTargetUnits > 0 ? ", short " + stock.MissingTargetUnits + "u" : "") + ")");
                }
            }
            if (snapshot.Bakery != null)
            {
                BakeryOperationsDetail bakery = snapshot.Bakery;
                EditorGUILayout.LabelField("Bakery: fuel " + bakery.FuelUnits + "u, flour " + bakery.FlourUnits
                    + " lb, notions " + bakery.NotionsUnits + "u", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Bakery: retail sold today " + bakery.RetailUnitsSoldToday + "u for "
                    + bakery.RetailRevenueTodayCents + "c; waste " + bakery.WasteUnitsToday + "u", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Bakery: wholesale accounts " + bakery.WholesaleAccountCount
                    + ", due today " + bakery.WholesaleUnitsDueToday + "u, retail available "
                    + bakery.RetailUnitsAvailableForSale + "u (net of due wholesale "
                    + bakery.RetailUnitsNetOfDueWholesale + "u), policy " + bakery.SupplyProtectionPolicy,
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Bakery: latest channel decision — " + bakery.LatestChannelDecision,
                    EditorStyles.wordWrappedLabel);
            }
            if (snapshot.FuelDealer != null)
            {
                FuelDealerOperationsDetail dealer = snapshot.FuelDealer;
                EditorGUILayout.LabelField("Fuel dealer: stock " + dealer.StockIntakeUnits + "u / target "
                    + dealer.TargetStockIntakeUnits + "u, reorder at " + dealer.ReorderPointIntakeUnits + "u",
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Fuel dealer: outstanding orders " + dealer.OutstandingOrderCount
                    + ", expected incoming " + dealer.ExpectedIncomingUnits + "u, retail price "
                    + dealer.RetailPriceCentsPerBundle + "c/bundle, retail sales recorded "
                    + dealer.RetailSalesRecorded, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndVertical();

            if (snapshot.Store != null)
            {
                StoreOperationsDetail store = snapshot.Store;
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("Counter", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Clerk: " + (store.ClerkPersonId >= 0 ? "person " + store.ClerkPersonId : "none resolves")
                    + " (" + store.ClerkCandidateCount + " candidates)", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Queue: " + store.QueueWaitingCount + " waiting / max "
                    + store.QueueMaxTolerated + (store.PhysicalCounterServiceRequired ? " (physical counter service required — no TendCustomer, no sale)" : ""),
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Restock need: " + store.RestockMissingUnits + "u short of reorder targets", EditorStyles.wordWrappedLabel);
                if (!string.IsNullOrWhiteSpace(store.DailySalesSummary))
                    EditorGUILayout.LabelField(store.DailySalesSummary, EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndVertical();
            }

            List<BusinessBlocker> blockers = BlockersFor(report, snapshot.Key);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Production blockers (" + blockers.Count + ")", EditorStyles.boldLabel);
            if (blockers.Count == 0)
            {
                EditorGUILayout.LabelField("No blocker with a cause is observed for this business.");
            }
            else
            {
                foreach (BusinessBlocker blocker in blockers)
                {
                    EditorGUILayout.LabelField("[" + blocker.Id + "] " + blocker.Title
                        + (blocker.IsOwnerDecision ? " — OWNER DECISION" : ""), EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Cause: " + blocker.Cause, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("Authority: " + blocker.Authority, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("Evidence: " + blocker.Evidence, EditorStyles.wordWrappedLabel);
                }
            }
            EditorGUILayout.EndVertical();

            List<BusinessNote> notes = NotesFor(report, snapshot.Key);
            if (notes.Count > 0)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("Notes", EditorStyles.boldLabel);
                foreach (BusinessNote note in notes)
                {
                    EditorGUILayout.LabelField("[" + note.Id + "] " + note.Text, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.LabelField("Authority: " + note.Authority, EditorStyles.wordWrappedLabel);
                }
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Authority source (shared inspection summary)", EditorStyles.boldLabel);
            string summary = shared != null ? FindBusinessInstance(shared, snapshot.Key) != null
                ? shared.BuildBusinessInspectionSummary(FindBusinessInstance(shared, snapshot.Key))
                : string.Empty : string.Empty;
            EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(summary) ? "(no inspection summary)" : summary,
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Runtime authority: SharedBusinessRuntimeManager.Businesses / BusinessRuntimeState",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawExpectedBusinesses(BusinessOperationsReport report)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Expected businesses (config)", EditorStyles.boldLabel);
            foreach (ExpectedBusiness expected in BusinessOperationsMonitor.ExpectedBusinesses())
            {
                bool missing = report != null && report.MissingBusinessKeys.Contains(expected.Key);
                EditorGUILayout.LabelField(expected.DisplayName + " (" + expected.Key + ") — owner "
                    + expected.OwnerPersonKey + ", opening " + expected.OpeningCashCents + "c"
                    + (missing ? " — NOT OBSERVED (no live snapshot)" : ""), EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawKnownGaps()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Known gaps this monitor surfaces (not decides)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("D-04 — a player-owned bakery can never bake (no owner person id; fail-closed bake gate). Owner decision owed (plan section 9g).", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("D-18 — the fuel dealer is never seeded into household seller knowledge, so it has no live customers. Seeding is Lane 9e's repair.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("D-09/D-10 — daily legacy sales settlement early-returns without a player store; the live daily cadence for the NPC store is the Lane 9b reset, and weekly payroll runs through the shared manager.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("E-14 correction — the bakery block carries fuelDealerIntakeUnitsPerBakeryUnit: 0, but the code reads the fuel-dealer block's 4 (DevelopmentTownConfiguration.FuelDealerIntakeUnitsPerBakeryUnit), so the paid dealer source is not null on that ground at this HEAD; the monitor shows the live value in the bakery row.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("No run exists on this HEAD — live behaviour is unverified until Codex runs the 5-10 day 10x run.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
        }

        private static List<BusinessBlocker> BlockersFor(BusinessOperationsReport report, string businessKey)
        {
            var result = new List<BusinessBlocker>();
            if (report == null) return result;
            foreach (BusinessBlocker blocker in report.Blockers)
            {
                if (string.Equals(blocker.BusinessKey, businessKey, StringComparison.OrdinalIgnoreCase))
                    result.Add(blocker);
            }
            return result;
        }

        private static List<BusinessNote> NotesFor(BusinessOperationsReport report, string businessKey)
        {
            var result = new List<BusinessNote>();
            if (report == null) return result;
            foreach (BusinessNote note in report.Notes)
            {
                if (string.Equals(note.BusinessKey, businessKey, StringComparison.OrdinalIgnoreCase))
                    result.Add(note);
            }
            return result;
        }

        private static BusinessSettlementCadence CadenceFor(BusinessOperationsReport report, string businessKey)
        {
            if (report == null) return null;
            foreach (BusinessSettlementCadence cadence in report.Cadences)
            {
                if (string.Equals(cadence.BusinessKey, businessKey, StringComparison.OrdinalIgnoreCase))
                    return cadence;
            }
            return null;
        }

        private static BusinessInstanceState FindBusinessInstance(SharedBusinessRuntimeManager shared, string key)
        {
            if (shared == null || shared.Businesses == null) return null;
            foreach (BusinessInstanceState business in shared.Businesses)
            {
                if (business == null) continue;
                if (string.Equals(business.InstanceId, key, StringComparison.OrdinalIgnoreCase))
                    return business;
                if (string.Equals(business.ProfileId, key, StringComparison.OrdinalIgnoreCase))
                    return business;
            }
            return null;
        }

        private static List<BusinessSnapshot> BuildSnapshots(
            SharedBusinessRuntimeManager shared,
            GeneralStoreRuntimeManager storeManager,
            SaveLoadManager save,
            SimulationSystemsHub hub,
            int dayIndex)
        {
            var snapshots = new List<BusinessSnapshot>();
            if (shared == null || shared.Businesses == null)
                return snapshots;

            Series2Pass9SaveState pass9 = save != null ? save.Series2Pass9State : null;
            BakeryShopRuntime bakeryRuntime = pass9 != null ? pass9.BakeryRuntime : null;
            FuelDealerRuntime dealerRuntime = pass9 != null ? pass9.FuelDealerRuntime : null;

            foreach (BusinessInstanceState business in shared.Businesses)
            {
                if (business == null)
                    continue;
                var snapshot = new BusinessSnapshot
                {
                    Key = business.InstanceId,
                    DisplayName = business.RuntimeDisplayName,
                    BusinessTypeName = business.BusinessType.ToString(),
                    OwnerKind = business.Owner != null ? business.Owner.OwnerKind.ToString() : "Unknown",
                    OwnerDisplayName = business.Owner != null ? business.Owner.DisplayName : "(no owner)",
                    BuildingId = business.AssignedBuildingId,
                    HasRuntimeState = business.RuntimeState != null,
                    OpeningCashCents = OpeningCashFor(business.InstanceId),
                };

                BusinessRuntimeState runtime = business.RuntimeState;
                if (runtime != null)
                {
                    snapshot.CashCents = runtime.CurrentCashCents;
                    snapshot.LastDailyRevenueCents = runtime.LastDailyRevenueCents;
                    snapshot.LastDailyNetCents = runtime.LastDailyNetCents;
                    snapshot.LastWeeklyCashAfterCents = runtime.LastWeeklyCashAfterCents;
                    snapshot.LastWeeklyBlockedReason = runtime.LastWeeklyBlockedReason;
                    snapshot.FilledWorkerCount = runtime.FilledWorkerCount;
                    int unitsSold = 0;
                    foreach (CategoryStockState stock in runtime.CategoryStock)
                    {
                        if (stock == null) continue;
                        unitsSold += stock.LastDailyUnitsSold;
                        snapshot.CategoryStock.Add(new BusinessStockSnapshot
                        {
                            CategoryId = stock.CategoryId,
                            CurrentUnits = stock.CurrentStockUnits,
                            TargetUnits = stock.TargetStockUnits,
                            LastDailyUnitsSold = stock.LastDailyUnitsSold,
                        });
                    }
                    snapshot.LastDailyUnitsSold = unitsSold;
                    foreach (WorkerSlotState slot in runtime.WorkerSlots)
                    {
                        if (slot == null) continue;
                        snapshot.WorkerSlots.Add(new BusinessWorkerSnapshot
                        {
                            SlotId = slot.SlotId,
                            DisplayName = slot.SlotDisplayName,
                            Filled = slot.IsFilled,
                            WorkerDisplayName = slot.AssignedWorkerDisplayName,
                            WeeklyWageCents = slot.WeeklyWageCents,
                            PaidActive = slot.IsPaidActive,
                            SuspendedForMissedPayroll = slot.SuspendedForMissedPayroll,
                            RequiredForOpening = slot.RequiredForOpening,
                        });
                    }
                }

                if (business.BusinessType == BusinessType.Bakery && bakeryRuntime != null
                    && string.Equals(bakeryRuntime.BusinessInstanceId, business.InstanceId, StringComparison.Ordinal))
                {
                    snapshot.Bakery = BuildBakeryDetail(bakeryRuntime, dayIndex, ResolveFuelIntakeUnitsPerBakeryUnit(pass9));
                }
                if (business.BusinessType == BusinessType.GeneralStore && storeManager != null)
                {
                    snapshot.Store = BuildStoreDetail(storeManager, business, hub);
                }
                if (business.BusinessType == BusinessType.FuelDealer && dealerRuntime != null
                    && string.Equals(dealerRuntime.DealerBusinessId, business.InstanceId, StringComparison.Ordinal))
                {
                    snapshot.FuelDealer = BuildFuelDealerDetail(shared, business, dealerRuntime);
                }

                snapshots.Add(snapshot);
            }
            return snapshots;
        }

        private static int OpeningCashFor(string businessKey)
        {
            foreach (ExpectedBusiness expected in BusinessOperationsMonitor.ExpectedBusinesses())
            {
                if (string.Equals(expected.Key, businessKey, StringComparison.OrdinalIgnoreCase))
                    return expected.OpeningCashCents;
            }
            return 0;
        }

        /// <summary>
        /// The configured fuelDealerIntakeUnitsPerBakeryUnit as the runtime
        /// reads it: the live paid fuel source first, else the config asset
        /// property (DevelopmentTownConfiguration reads the FUEL-DEALER
        /// block's value — 4 at this HEAD — not the bakery block's 0, so
        /// E-14's "config value 0" reading does not describe the live
        /// value). -1 when neither is observable.
        /// </summary>
        private static int ResolveFuelIntakeUnitsPerBakeryUnit(Series2Pass9SaveState pass9)
        {
            if (pass9 != null && pass9.FuelSource != null)
                return pass9.FuelSource.IntakeUnitsPerBakeryUnit;
            DevelopmentTownConfiguration config = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(
                "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            if (config != null)
                return config.FuelDealerIntakeUnitsPerBakeryUnit;
            return -1;
        }

        private static BakeryOperationsDetail BuildBakeryDetail(BakeryShopRuntime bakery, int dayIndex, int fuelIntakeUnitsPerBakeryUnit)
        {
            var detail = new BakeryOperationsDetail
            {
                OwnerPersonId = bakery.OwnerPersonId,
                SupplyProtectionPolicy = bakery.SupplyProtection.ToString(),
                // Lane 10a (H-3): the channel history is inspectable from
                // the monitor — the latest recorded decision, not a bare
                // policy value. "none on record" is honest for a legacy
                // save whose history predates the decision record.
                LatestChannelDecision = bakery.LatestChannelDecision != null
                    ? bakery.LatestChannelDecision.Summary()
                    : "none on record",
                WholesaleAccountCount = bakery.WholesaleAccounts != null ? bakery.WholesaleAccounts.Count : 0,
                FuelDealerIntakeUnitsPerBakeryUnit = fuelIntakeUnitsPerBakeryUnit,
            };
            int day = dayIndex >= 0 ? dayIndex : 0;
            detail.WholesaleUnitsDueToday = bakery.WholesaleUnitsDueToday(BakeryBreadCatalog.BreadLoafId, day);
            detail.RetailUnitsAvailableForSale = bakery.RetailUnitsAvailableForSale(BakeryBreadCatalog.BreadLoafId, day);
            detail.RetailUnitsNetOfDueWholesale = bakery.RetailUnitsNetOfDueWholesale(BakeryBreadCatalog.BreadLoafId, day);

            int fuelUnits = 0;
            if (bakery.FuelStock != null && bakery.FuelStock.Lots != null)
            {
                foreach (BakeryFuelLot lot in bakery.FuelStock.Lots)
                {
                    if (lot != null) fuelUnits += Math.Max(0, lot.Units);
                }
            }
            detail.FuelUnits = fuelUnits;

            int flourUnits = 0;
            int notionsUnits = 0;
            if (bakery.FlourStock != null && bakery.FlourStock.Lots != null)
            {
                foreach (BakeryFlourLot lot in bakery.FlourStock.Lots)
                {
                    if (lot == null) continue;
                    if (string.Equals(lot.FlourName, BakeryBreadCatalog.NotionsItemId, StringComparison.Ordinal))
                        notionsUnits += Math.Max(0, lot.Units);
                    else
                        flourUnits += Math.Max(0, lot.Units);
                }
            }
            detail.FlourUnits = flourUnits;
            detail.NotionsUnits = notionsUnits;

            int soldToday = 0;
            int revenueToday = 0;
            if (bakery.Sales != null)
            {
                foreach (BakerySaleRecord sale in bakery.Sales)
                {
                    if (sale == null || sale.DayIndex != day) continue;
                    soldToday += Math.Max(0, sale.Units);
                    revenueToday += Math.Max(0, sale.Units) * Math.Max(0, sale.PricePerUnitCents);
                }
            }
            detail.RetailUnitsSoldToday = soldToday;
            detail.RetailRevenueTodayCents = revenueToday;

            int wasteToday = 0;
            if (bakery.Waste != null)
            {
                foreach (BakeryWasteRecord waste in bakery.Waste)
                {
                    if (waste == null || waste.WastedDayIndex != day) continue;
                    wasteToday += Math.Max(0, waste.Units);
                }
            }
            detail.WasteUnitsToday = wasteToday;

            bool hasConventional = false;
            if (bakery.Batches != null)
            {
                foreach (BakeryDoughBatch batch in bakery.Batches)
                {
                    if (batch != null && batch.IsActive) { hasConventional = true; break; }
                }
            }
            detail.HasActiveConventionalBatch = hasConventional;
            detail.HasActiveStagedBatch = false;
            return detail;
        }

        private static StoreOperationsDetail BuildStoreDetail(
            GeneralStoreRuntimeManager storeManager, BusinessInstanceState business, SimulationSystemsHub hub)
        {
            var detail = new StoreOperationsDetail();
            BusinessRuntimeState runtime = business.RuntimeState;
            int clerkId = -1;
            if (runtime != null && storeManager.TryGetServiceClerkPersonId(runtime, out int resolved))
                clerkId = resolved;
            detail.ClerkPersonId = clerkId;
            detail.ClerkCandidateCount = clerkId >= 0 ? 1 : 0;

            CustomerQueue queue = storeManager.CustomerQueueForBusiness(business);
            if (queue != null)
            {
                detail.QueueWaitingCount = queue.WaitingCount;
                detail.QueueMaxTolerated = CustomerQueue.MaxToleratedQueueLength;
            }

            HouseholdShoppingLoop loop = hub != null ? hub.ShoppingLoop : null;
            detail.PhysicalCounterServiceRequired = loop != null && loop.RequirePhysicalCounterService;

            int missing = 0;
            if (runtime != null)
            {
                foreach (CategoryStockState stock in runtime.CategoryStock)
                {
                    if (stock == null) continue;
                    missing += stock.MissingTargetUnits;
                }
            }
            detail.RestockMissingUnits = missing;

            if (BusinessInstanceState.IsDevelopmentNpcGeneralStore(business))
            {
                detail.DailySalesState = DailySalesCadenceState.NpcDailyReset;
                detail.DailySalesSummary = "Daily: Lane 9b NPC cadence resets the day on this runtime "
                    + "(DriveNpcDailySalesCadence / BeginDailySalesCadence). Legacy ResolveDailySales "
                    + "early-returns without a player store (D-09).";
            }
            else if (storeManager.CurrentBusiness == business)
            {
                detail.DailySalesState = DailySalesCadenceState.PlayerLegacySettled;
                detail.DailySalesSummary = "Daily: player store — ResolveDailySales runs through the dedicated manager path.";
            }
            else
            {
                detail.DailySalesState = DailySalesCadenceState.Unknown;
                detail.DailySalesSummary = "Daily: no daily sales cadence is observed for this store.";
            }
            return detail;
        }

        /// <summary>Wiring pass (N-2): live D-18 value — true when any resident knows
        /// this dealer business (Lane 9e seeding, read through SimulationDrivers).
        /// No drivers object in the scene means unknown, never seeded.</summary>
        private static bool IsFuelDealerKnown(string businessInstanceId)
        {
            SimulationDrivers drivers = Object.FindAnyObjectByType<SimulationDrivers>();
            return drivers != null && drivers.AnyHouseholdKnowsSeller(businessInstanceId);
        }

        private static FuelDealerOperationsDetail BuildFuelDealerDetail(
            SharedBusinessRuntimeManager shared, BusinessInstanceState business, FuelDealerRuntime dealer)
        {
            var detail = new FuelDealerOperationsDetail
            {
                StockIntakeUnits = dealer.StockIntakeUnits,
                TargetStockIntakeUnits = dealer.TargetStockIntakeUnits,
                ReorderPointIntakeUnits = dealer.ReorderPointIntakeUnits,
                RetailPriceCentsPerBundle = shared.GetConfiguredCategorySalePriceCents(business, "fuel_wood"),
                // D-18 (wiring pass, N-2): the live seeded value from SimulationDrivers —
                // Lane 9e seeds dealer knowledge on every registration pass, so a dealer
                // no resident knows is a real blocker and a known dealer clears it.
                SellerKnowledgeSeeded = IsFuelDealerKnown(business.InstanceId),
            };

            int outstanding = 0;
            int expectedIncoming = 0;
            if (dealer.Orders != null)
            {
                foreach (FuelDealerOrder order in dealer.Orders)
                {
                    if (order == null || order.Status != FuelDealerOrderStatus.Placed) continue;
                    outstanding++;
                    expectedIncoming += (int)Math.Floor(Math.Max(0, order.InputUnits) * order.ConversionRateFuelPerInputUnit);
                }
            }
            detail.OutstandingOrderCount = outstanding;
            detail.ExpectedIncomingUnits = expectedIncoming;

            int retailSales = 0;
            if (dealer.CashBook != null)
            {
                foreach (FuelDealerCashBookEntry entry in dealer.CashBook)
                {
                    if (entry != null && entry.Kind == FuelDealerCashBookKind.RetailSale)
                        retailSales++;
                }
            }
            detail.RetailSalesRecorded = retailSales;
            return detail;
        }

        private static List<BusinessSettlementCadence> BuildCadences(
            SharedBusinessRuntimeManager shared,
            GeneralStoreRuntimeManager storeManager,
            SimulationSystemsHub hub,
            List<BusinessSnapshot> snapshots)
        {
            var cadences = new List<BusinessSettlementCadence>();
            WageSettlementService wages = hub != null ? hub.WageSettlement : null;
            foreach (BusinessSnapshot snapshot in snapshots)
            {
                if (snapshot == null) continue;
                var cadence = new BusinessSettlementCadence { BusinessKey = snapshot.Key };

                if (snapshot.Store != null)
                {
                    cadence.DailyState = snapshot.Store.DailySalesState;
                    cadence.DailyEvidence = snapshot.Store.DailySalesSummary;
                }
                else
                {
                    cadence.DailyState = DailySalesCadenceState.Unknown;
                    cadence.DailyEvidence = "No daily sales cadence applies to this business type.";
                }

                int settledWeek = wages != null ? wages.SettledWeekIndex(snapshot.Key) : -1;
                cadence.WeeklySettledWeekIndex = settledWeek;
                if (settledWeek >= 0)
                {
                    bool npcStore = BusinessInstanceState.IsDevelopmentNpcGeneralStore(
                        FindBusinessInstance(shared, snapshot.Key));
                    bool playerStore = storeManager != null && storeManager.CurrentBusiness != null
                        && string.Equals(storeManager.CurrentBusiness.InstanceId, snapshot.Key, StringComparison.OrdinalIgnoreCase);
                    if (playerStore)
                    {
                        cadence.WeeklyState = WeeklyPayrollCadenceState.SettledViaDedicatedManager;
                        cadence.WeeklyEvidence = "Settled week " + settledWeek
                            + " via GeneralStoreRuntimeManager.ResolveWeeklyPayrollAndReorder.";
                    }
                    else
                    {
                        cadence.WeeklyState = WeeklyPayrollCadenceState.SettledViaSharedManager;
                        cadence.WeeklyEvidence = "Settled week " + settledWeek
                            + " via SharedBusinessRuntimeManager.ResolveWeeklySharedOperations"
                            + (npcStore ? " (NPC store weekly payroll pass, D-10)." : ".");
                    }
                }
                else
                {
                    cadence.WeeklyState = WeeklyPayrollCadenceState.NotRun;
                    cadence.WeeklyEvidence = "No settled week is recorded for this business (WageSettlementService).";
                }

                cadences.Add(cadence);
            }
            return cadences;
        }
    }
}
#endif
