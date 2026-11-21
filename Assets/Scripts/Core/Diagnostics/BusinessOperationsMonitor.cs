// Land & Ledgers — Business Operations Monitor core (Tool 04, Lane 5).
//
// Live per-business state — owner, employees, worker locations, queue,
// counter status, inventory, orders, cash, production blockers with
// specific causes — for the General Store, bakery, fuel dealer and the
// other businesses, so the owner can see a business's health without
// reading source.
//
// This class OBSERVES the authoritative systems (SharedBusinessRuntimeManager,
// GeneralStoreRuntimeManager, BakeryShopRuntime, FuelDealerRuntime,
// WageSettlementService, CustomerQueue, GeneralStoreServiceClerkPolicy) and
// never becomes a second ledger: it holds no balances, moves no cash, writes
// no stock, assigns no work, and changes no routes. Every method is pure
// over caller-supplied plain snapshot records: tests feed fixture records;
// the Editor window feeds live snapshots mapped from the real runtimes.
//
// Surface, do not decide:
//   - D-04 (player-owned bakery can never bake) is an OWNER DECISION
//     (plan section 9g). The monitor surfaces the bake-block with its
//     cause; it does not resolve it.
//   - D-18 (fuel-dealer knowledge seeding) is Lane 9e's repair. The
//     monitor surfaces the gap; it does not implement the seeding.
//   - The NPC store weekly settlement is FIXED (real payroll pass in
//     SharedBusinessRuntimeManager, D-10); daily sales settlement now runs
//     via Lane 9b's NPC cadence (DriveNpcDailySalesCadence /
//     BeginDailySalesCadence) — the monitor shows BOTH cadences and their
//     states, it does not re-settle either one.
//
// Evidence taxonomy on every finding in this file: source-reviewed only.
// Nothing here is Unity-compiled or live-verified; the Roslyn harness is
// the only verification this pass can perform (see HARNESS_NOTES.md).
// No run exists on this HEAD.

using System;
using System.Collections.Generic;
using System.Linq;

namespace LandLedgers.Diagnostics
{
    /// <summary>Health of one business as observed, never as judged.</summary>
    public enum BusinessHealthKind
    {
        /// <summary>No runtime state is bound — nothing to observe.</summary>
        Unknown = 0,
        /// <summary>No blocker with a cause is observed.</summary>
        Healthy = 1,
        /// <summary>At least one blocker with a named cause is observed.</summary>
        Blocked = 2,
        /// <summary>A blocker whose resolution is an owner decision (D-04).</summary>
        OwnerDecision = 3,
    }

    /// <summary>Daily sales settlement state for one business.</summary>
    public enum DailySalesCadenceState
    {
        Unknown = 0,
        /// <summary>No daily cadence has been observed to run.</summary>
        NotRun = 1,
        /// <summary>
        /// Lane 9b NPC cadence: DriveNpcDailySalesCadence resets the day on
        /// the development NPC store runtime (BeginDailySalesCadence). The
        /// legacy ResolveDailySales path is player-store gated and does not
        /// run for this store.
        /// </summary>
        NpcDailyReset = 2,
        /// <summary>
        /// Player store: ResolveDailySales ran through the dedicated
        /// GeneralStoreRuntimeManager path.
        /// </summary>
        PlayerLegacySettled = 3,
        /// <summary>
        /// Legacy daily sales early-returns with no player store bound
        /// (D-09) and no NPC cadence covers the business.
        /// </summary>
        LegacyDeadNoPlayerStore = 4,
    }

    /// <summary>Weekly payroll settlement state for one business.</summary>
    public enum WeeklyPayrollCadenceState
    {
        Unknown = 0,
        /// <summary>No weekly payroll has been observed to settle.</summary>
        NotRun = 1,
        /// <summary>Settled by the shared weekly pass (D-10 NPC store and all non-GeneralStore types).</summary>
        SettledViaSharedManager = 2,
        /// <summary>Settled by the dedicated GeneralStoreRuntimeManager weekly path (player store).</summary>
        SettledViaDedicatedManager = 3,
    }

    /// <summary>One worker slot as observed on a business runtime.</summary>
    public sealed class BusinessWorkerSnapshot
    {
        public string SlotId = string.Empty;
        public string DisplayName = string.Empty;
        public bool Filled;
        public string WorkerDisplayName = string.Empty;
        public int WeeklyWageCents;
        public bool PaidActive;
        public bool SuspendedForMissedPayroll;
        public bool RequiredForOpening;
    }

    /// <summary>One category's stock as observed on a business runtime.</summary>
    public sealed class BusinessStockSnapshot
    {
        public string CategoryId = string.Empty;
        public int CurrentUnits;
        public int TargetUnits;
        public int LastDailyUnitsSold;
        public int MissingTargetUnits => Math.Max(0, TargetUnits - CurrentUnits);
    }

    /// <summary>Bakery-specific observed state (BakeryShopRuntime + staged chain gate inputs).</summary>
    public sealed class BakeryOperationsDetail
    {
        /// <summary>Owner person id, or -1 when no owner/worker is bound (D-04).</summary>
        public int OwnerPersonId = -1;
        public string SupplyProtectionPolicy = string.Empty;
        public int WholesaleAccountCount;
        public int WholesaleUnitsDueToday;
        public int RetailUnitsAvailableForSale;
        public int RetailUnitsNetOfDueWholesale;
        public int FuelUnits;
        public int FlourUnits;
        public int NotionsUnits;
        public int RetailUnitsSoldToday;
        public int RetailRevenueTodayCents;
        public int WasteUnitsToday;
        /// <summary>Config value: 0 means the paid dealer source returns null (E-14).</summary>
        public int FuelDealerIntakeUnitsPerBakeryUnit;
        public bool HasActiveConventionalBatch;
        public bool HasActiveStagedBatch;
    }

    /// <summary>General Store-specific observed state (counter, queue, cadence).</summary>
    public sealed class StoreOperationsDetail
    {
        /// <summary>Resolved service clerk person id, or -1 when none resolves.</summary>
        public int ClerkPersonId = -1;
        public int ClerkCandidateCount;
        public int QueueWaitingCount;
        public int QueueMaxTolerated;
        public DailySalesCadenceState DailySalesState = DailySalesCadenceState.Unknown;
        public string DailySalesSummary = string.Empty;
        /// <summary>Units short of authored reorder targets across categories (the restock need signal).</summary>
        public int RestockMissingUnits;
        public bool PhysicalCounterServiceRequired;
    }

    /// <summary>Fuel dealer-specific observed state (FuelDealerRuntime).</summary>
    public sealed class FuelDealerOperationsDetail
    {
        public int StockIntakeUnits;
        public int TargetStockIntakeUnits;
        public int ReorderPointIntakeUnits;
        public int OutstandingOrderCount;
        public int ExpectedIncomingUnits;
        /// <summary>Configured bundle price (average category transfer price); 0 or less means no registration (D-17).</summary>
        public int RetailPriceCentsPerBundle;
        /// <summary>False until Lane 9e seeds dealer knowledge (D-18).</summary>
        public bool SellerKnowledgeSeeded;
        public int RetailSalesRecorded;
    }

    /// <summary>One business as observed. Common fields come from BusinessInstanceState + BusinessRuntimeState.</summary>
    public sealed class BusinessSnapshot
    {
        public string Key = string.Empty;
        public string DisplayName = string.Empty;
        public string BusinessTypeName = string.Empty;
        public string OwnerKind = string.Empty;
        public string OwnerDisplayName = string.Empty;
        public int BuildingId = -1;
        public bool HasRuntimeState;
        public int CashCents;
        public int OpeningCashCents;
        public int LastDailyRevenueCents;
        public int LastDailyNetCents;
        public int LastDailyUnitsSold;
        public List<BusinessWorkerSnapshot> WorkerSlots = new List<BusinessWorkerSnapshot>();
        public List<BusinessStockSnapshot> CategoryStock = new List<BusinessStockSnapshot>();
        public string LastWeeklyBlockedReason = string.Empty;
        public int LastWeeklyCashAfterCents;
        public int FilledWorkerCount;

        public BakeryOperationsDetail Bakery;
        public StoreOperationsDetail Store;
        public FuelDealerOperationsDetail FuelDealer;
    }

    /// <summary>One production blocker WITH its specific cause. A blocker without a cause is never emitted.</summary>
    public sealed class BusinessBlocker
    {
        public BusinessBlocker(
            string id, string businessKey, string title, string cause,
            string authority, string evidence, IncidentSeverity severity, bool isOwnerDecision)
        {
            Id = id;
            BusinessKey = businessKey ?? string.Empty;
            Title = title ?? string.Empty;
            Cause = cause ?? string.Empty;
            Authority = authority ?? string.Empty;
            Evidence = evidence ?? string.Empty;
            Severity = severity;
            IsOwnerDecision = isOwnerDecision;
        }

        public string Id { get; }
        public string BusinessKey { get; }
        public string Title { get; }
        public string Cause { get; }
        public string Authority { get; }
        public string Evidence { get; }
        public IncidentSeverity Severity { get; }
        public bool IsOwnerDecision { get; }
    }

    /// <summary>Informational surfacing: a fact the owner should see that is not a blocker.</summary>
    public sealed class BusinessNote
    {
        public BusinessNote(string id, string businessKey, string text, string authority)
        {
            Id = id;
            BusinessKey = businessKey ?? string.Empty;
            Text = text ?? string.Empty;
            Authority = authority ?? string.Empty;
        }

        public string Id { get; }
        public string BusinessKey { get; }
        public string Text { get; }
        public string Authority { get; }
    }

    /// <summary>Both settlement cadences for one business, with their states (D-09/D-10 + Lane 9b).</summary>
    public sealed class BusinessSettlementCadence
    {
        public string BusinessKey = string.Empty;
        public DailySalesCadenceState DailyState = DailySalesCadenceState.Unknown;
        public string DailyEvidence = string.Empty;
        public WeeklyPayrollCadenceState WeeklyState = WeeklyPayrollCadenceState.Unknown;
        public string WeeklyEvidence = string.Empty;
        public int WeeklySettledWeekIndex = -1;
    }

    /// <summary>One expected dev-town business, from DevelopmentTownConfiguration.asset (config-grounded).</summary>
    public sealed class ExpectedBusiness
    {
        public ExpectedBusiness(string key, string displayName, string ownerPersonKey, int openingCashCents)
        {
            Key = key ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            OwnerPersonKey = ownerPersonKey ?? string.Empty;
            OpeningCashCents = openingCashCents;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public string OwnerPersonKey { get; }
        public int OpeningCashCents { get; }
    }

    public sealed class BusinessOperationsReport
    {
        public BusinessOperationsReport(
            List<BusinessSnapshot> snapshots,
            List<BusinessBlocker> blockers,
            List<BusinessNote> notes,
            List<BusinessSettlementCadence> cadences,
            List<string> missingBusinessKeys)
        {
            Snapshots = snapshots;
            Blockers = blockers;
            Notes = notes;
            Cadences = cadences;
            MissingBusinessKeys = missingBusinessKeys;
        }

        public List<BusinessSnapshot> Snapshots { get; }
        public List<BusinessBlocker> Blockers { get; }
        public List<BusinessNote> Notes { get; }
        public List<BusinessSettlementCadence> Cadences { get; }
        public List<string> MissingBusinessKeys { get; }

        public int HealthyCount => Snapshots.Count(s => BusinessOperationsMonitor.ClassifyHealth(s, BlockersFor(s.Key)) == BusinessHealthKind.Healthy);
        public int BlockedCount => Snapshots.Count(s => BusinessOperationsMonitor.ClassifyHealth(s, BlockersFor(s.Key)) == BusinessHealthKind.Blocked);
        public int UnknownCount => Snapshots.Count(s => BusinessOperationsMonitor.ClassifyHealth(s, BlockersFor(s.Key)) == BusinessHealthKind.Unknown);
        public int OwnerDecisionCount => Snapshots.Count(s => BusinessOperationsMonitor.ClassifyHealth(s, BlockersFor(s.Key)) == BusinessHealthKind.OwnerDecision);

        private List<BusinessBlocker> BlockersFor(string businessKey)
        {
            var result = new List<BusinessBlocker>();
            foreach (BusinessBlocker blocker in Blockers)
            {
                if (string.Equals(blocker.BusinessKey, businessKey, StringComparison.Ordinal))
                    result.Add(blocker);
            }
            return result;
        }
    }

    /// <summary>
    /// The monitor. All methods are pure: no statics read, no cash moved,
    /// no stock written, no work assigned. Diagnostics OBSERVE authoritative
    /// state; this class never becomes a parallel source of truth.
    /// </summary>
    public static class BusinessOperationsMonitor
    {
        /// <summary>
        /// The 7 instantiated dev-town businesses, from
        /// DevelopmentTownConfiguration.asset (businesses: general-store,
        /// bakery, fuel-dealer, freight-company, sawmill, lumber-yard,
        /// carpentry-workshop) with their config owners and opening cash.
        /// Config total 162,000c (D-36: day-1 runs record 159,552c, a
        /// 2,448c shortfall with no attributed ledger — still open).
        /// </summary>
        public static IReadOnlyList<ExpectedBusiness> ExpectedBusinesses()
        {
            return new List<ExpectedBusiness>
            {
                new ExpectedBusiness("general-store", "General Store", "kennedy-baldwin-ooms", 50000),
                new ExpectedBusiness("bakery", "Bakery", "glen-pierce", 35000),
                new ExpectedBusiness("fuel-dealer", "Fuel Dealer", "jacob-boniecki", 20000),
                new ExpectedBusiness("freight-company", "Livery and Freight", "ethan-boniecki", 20000),
                new ExpectedBusiness("sawmill", "Sawmill", "enzo-kwindt", 15000),
                new ExpectedBusiness("lumber-yard", "Lumber Yard", "grace-boniecki", 12000),
                new ExpectedBusiness("carpentry-workshop", "Carpentry Workshop", "don-millward", 10000),
            };
        }

        /// <summary>Expected business keys with no live snapshot — a business that is not visible at all.</summary>
        public static List<string> VerifyExpectedBusinesses(IEnumerable<string> liveBusinessKeys)
        {
            var live = new HashSet<string>(liveBusinessKeys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var missing = new List<string>();
            foreach (ExpectedBusiness expected in ExpectedBusinesses())
            {
                if (!live.Contains(expected.Key))
                    missing.Add(expected.Key);
            }
            return missing;
        }

        /// <summary>Display label for a daily sales cadence state.</summary>
        public static string DailyCadenceLabel(DailySalesCadenceState state)
        {
            switch (state)
            {
                case DailySalesCadenceState.NpcDailyReset: return "NPC daily cadence (Lane 9b reset)";
                case DailySalesCadenceState.PlayerLegacySettled: return "Player legacy daily settlement";
                case DailySalesCadenceState.LegacyDeadNoPlayerStore: return "Legacy daily sales dead (no player store)";
                case DailySalesCadenceState.NotRun: return "Daily cadence not run";
                default: return "Daily cadence unknown";
            }
        }

        /// <summary>Display label for a weekly payroll cadence state.</summary>
        public static string WeeklyCadenceLabel(WeeklyPayrollCadenceState state)
        {
            switch (state)
            {
                case WeeklyPayrollCadenceState.SettledViaSharedManager: return "Weekly payroll settled (shared manager)";
                case WeeklyPayrollCadenceState.SettledViaDedicatedManager: return "Weekly payroll settled (dedicated manager)";
                case WeeklyPayrollCadenceState.NotRun: return "Weekly payroll not run";
                default: return "Weekly payroll unknown";
            }
        }

        /// <summary>
        /// Health from the business's own blockers. A business with no
        /// runtime state is Unknown, never Healthy: there is nothing to
        /// observe. An owner-decision blocker (D-04) outranks Blocked so
        /// the owner sees at once that the decision is theirs.
        /// </summary>
        public static BusinessHealthKind ClassifyHealth(BusinessSnapshot snapshot, IReadOnlyList<BusinessBlocker> blockers)
        {
            if (snapshot == null || !snapshot.HasRuntimeState)
                return BusinessHealthKind.Unknown;
            if (blockers != null)
            {
                foreach (BusinessBlocker blocker in blockers)
                {
                    if (blocker != null && blocker.IsOwnerDecision)
                        return BusinessHealthKind.OwnerDecision;
                }
                foreach (BusinessBlocker blocker in blockers)
                {
                    if (blocker != null && (blocker.Severity == IncidentSeverity.Warning || blocker.Severity == IncidentSeverity.Critical))
                        return BusinessHealthKind.Blocked;
                }
            }
            return BusinessHealthKind.Healthy;
        }

        // --- D-04 / bakery -----------------------------------------------------

        /// <summary>
        /// Bakery blockers, each with its specific cause (never a bare
        /// "blocked"). D-04 is surfaced as an OWNER DECISION, never
        /// resolved here.
        /// </summary>
        public static void ClassifyBakery(
            BakeryOperationsDetail detail, string businessKey,
            List<BusinessBlocker> blockers, List<BusinessNote> notes)
        {
            if (detail == null || blockers == null)
                return;
            string key = businessKey ?? string.Empty;

            if (detail.OwnerPersonId < 0)
            {
                blockers.Add(new BusinessBlocker(
                    "D-04", key,
                    "Bakery cannot bake: no worker binding",
                    "The owner has no person id, so BakeryShopRuntime.BindOwnerPerson refuses the bind "
                    + "and the fail-closed bake gate refuses every bake with zero worker minutes "
                    + "(SimulationDrivers bakery wholesale-planning region: 'no worker time is available; "
                    + "baking is refused until an assigned worker resolves'). A player-owned bakery can "
                    + "never bake. OWNER DECISION owed (plan section 9g) — the monitor surfaces the block; "
                    + "it does not resolve it.",
                    "BakeryShopRuntime.BindOwnerPerson / SimulationDrivers.DriveBakeryWholesalePlanning",
                    "OwnerPersonId = -1 (no owner/worker bound).",
                    IncidentSeverity.Warning, true));
            }

            if (detail.FuelUnits <= 0)
            {
                string cause;
                if (detail.FuelDealerIntakeUnitsPerBakeryUnit == 0)
                {
                    cause = "The paid dealer source returns null because the configured "
                        + "fuelDealerIntakeUnitsPerBakeryUnit is 0 (E-14 — read from the fuel-dealer block by "
                        + "DevelopmentTownConfiguration.FuelDealerIntakeUnitsPerBakeryUnit): the bakery runs on "
                        + "its 40u opening stock only, and the day-41 fuel death follows from that config "
                        + "value, not from missing code.";
                }
                else if (detail.FuelDealerIntakeUnitsPerBakeryUnit < 0)
                {
                    cause = "The fuel store is empty; the configured fuelDealerIntakeUnitsPerBakeryUnit was "
                        + "not observed in this snapshot, so whether the paid dealer source can build is "
                        + "unknown — check DevelopmentTownConfiguration.asset (E-14).";
                }
                else
                {
                    cause = "The fuel store is empty and no paid dealer source is supplying it, although the "
                        + "configured fuelDealerIntakeUnitsPerBakeryUnit is "
                        + detail.FuelDealerIntakeUnitsPerBakeryUnit + " (the paid source builds when a priced "
                        + "dealer exists — check the dealer row for stock and price).";
                }
                blockers.Add(new BusinessBlocker(
                    "E-14", key,
                    "Bakery has no fuel",
                    cause,
                    "BakeryShopRuntime.FuelStock / SimulationDrivers.TryBuildPaidBakeryFuelSource",
                    "FuelUnits = 0; fuelDealerIntakeUnitsPerBakeryUnit = " + detail.FuelDealerIntakeUnitsPerBakeryUnit + ".",
                    IncidentSeverity.Warning, false));
            }

            if (detail.FlourUnits <= 0)
            {
                blockers.Add(new BusinessBlocker(
                    "E-14", key,
                    "Bakery has no flour",
                    "The flour bin is empty: no batch can be planned or baked. Flour is procured through "
                    + "the bakery flour procurement driver (Millbrook/Redfield via the store); an empty bin "
                    + "with no order in flight is a supply failure, not a demand failure.",
                    "BakeryShopRuntime.FlourStock",
                    "FlourUnits = 0.",
                    IncidentSeverity.Warning, false));
            }

            if (detail.NotionsUnits <= 0)
            {
                blockers.Add(new BusinessBlocker(
                    "D-07", key,
                    "Bakery has no notions",
                    "The notions bin is empty: batches that need notions cannot be prepped. Notions need is "
                    + "the physical shortfall after bin stock and today's paid feed (D-07).",
                    "BakeryShopRuntime (notions dispense)",
                    "NotionsUnits = 0.",
                    IncidentSeverity.Warning, false));
            }

            bool wholesaleFirst = string.Equals(detail.SupplyProtectionPolicy, "ProtectWholesaleContracts", StringComparison.Ordinal);
            if (detail.WholesaleUnitsDueToday > 0 && wholesaleFirst && detail.RetailUnitsAvailableForSale <= 0)
            {
                blockers.Add(new BusinessBlocker(
                    "D-02", key,
                    "Retail counter starved by wholesale-protection policy",
                    "A due wholesale account takes every sellable loaf first under ProtectWholesaleContracts "
                    + "(BakeryShopRuntime.RetailAllowanceFor), so the adapter correctly reports 0 retail units "
                    + "and households see no bread. Lane 9a changed the code default to ProtectRetailCounter — "
                    + "this fires only while the live policy is ProtectWholesaleContracts.",
                    "BakeryShopRuntime.RetailAllowanceFor / Series2BakerySupplierAdapter.ItemStockUnits",
                    "WholesaleUnitsDueToday = " + detail.WholesaleUnitsDueToday
                    + "; RetailUnitsAvailableForSale = 0; policy = " + detail.SupplyProtectionPolicy + ".",
                    IncidentSeverity.Warning, false));
            }

            if (detail.WholesaleAccountCount > 0 && notes != null)
            {
                notes.Add(new BusinessNote(
                    "D-03", key,
                    "Staged chain nets due-today wholesale instead of vetoing (Lane 9a): a standing wholesale "
                    + "account no longer blocks staged retail batches; only the " + detail.WholesaleUnitsDueToday
                    + "u due today are netted out of retail stock (RetailUnitsNetOfDueWholesale = "
                    + detail.RetailUnitsNetOfDueWholesale + "u).",
                    "SimulationDrivers.ShouldBeginStagedBakeryBatch / StagedBakeryBatchRefusalReason"));
            }
        }

        // --- D-18 / fuel dealer --------------------------------------------------

        /// <summary>Fuel dealer blockers, each with its specific cause.</summary>
        public static void ClassifyFuelDealer(
            FuelDealerOperationsDetail detail, string businessKey,
            List<BusinessBlocker> blockers)
        {
            if (detail == null || blockers == null)
                return;
            string key = businessKey ?? string.Empty;

            if (!detail.SellerKnowledgeSeeded)
            {
                blockers.Add(new BusinessBlocker(
                    "D-18", key,
                    "Fuel dealer has no live customers reachable",
                    "SimulationDrivers seeds only SeedLocalGeneralStoreKnowledge and SeedLocalBakeryKnowledge; "
                    + "no SeedLocalFuelDealerKnowledge exists anywhere in Assets/Scripts, so households cannot "
                    + "discover the registered dealer as a fuel_wood seller. The dealer is registered, stocked, "
                    + "staffed and queue-equipped (D-17, D-21) but never known. Seeding is Lane 9e's repair, "
                    + "not this lane's — surfaced here, not implemented here.",
                    "SimulationDrivers (seller-knowledge seeding)",
                    "SellerKnowledgeSeeded = false; retail sales recorded = " + detail.RetailSalesRecorded + ".",
                    IncidentSeverity.Warning, false));
            }

            if (detail.RetailPriceCentsPerBundle <= 0)
            {
                blockers.Add(new BusinessBlocker(
                    "D-17", key,
                    "Fuel dealer has no configured retail price",
                    "The configured price is the average category transfer price for fuel_wood; at 0 or below "
                    + "the adapter is not registered and no retail sale can be priced (D-17: price <= 0 warns "
                    + "once, no registration).",
                    "SimulationDrivers dealer registration / SharedBusinessRuntimeManager.GetConfiguredCategorySalePriceCents",
                    "RetailPriceCentsPerBundle = " + detail.RetailPriceCentsPerBundle + ".",
                    IncidentSeverity.Warning, false));
            }

            if (detail.StockIntakeUnits <= 0 && detail.OutstandingOrderCount <= 0 && detail.ExpectedIncomingUnits <= 0)
            {
                blockers.Add(new BusinessBlocker(
                    "D-19", key,
                    "Fuel dealer has no fuel stock and no order placed",
                    "Stock is 0, no order is outstanding, and nothing is expected incoming. The reorder point "
                    + "is " + detail.ReorderPointIntakeUnits + "u against a " + detail.TargetStockIntakeUnits
                    + "u target; the procurement assessment (FuelDealerRuntime) counts expected incoming "
                    + "before ordering, so a placed order would already show here.",
                    "FuelDealerRuntime (procurement assessment)",
                    "StockIntakeUnits = 0; OutstandingOrderCount = 0; ExpectedIncomingUnits = 0.",
                    IncidentSeverity.Warning, false));
            }
            else if (detail.StockIntakeUnits < detail.ReorderPointIntakeUnits && detail.OutstandingOrderCount <= 0)
            {
                blockers.Add(new BusinessBlocker(
                    "D-19", key,
                    "Fuel dealer below reorder point with no order placed",
                    "Stock (" + detail.StockIntakeUnits + "u) is below the reorder point ("
                    + detail.ReorderPointIntakeUnits + "u) and no order is outstanding. Procurement should "
                    + "have placed one unless no vendor exists (the 'no vendor is conjured' refusal).",
                    "FuelDealerRuntime (procurement assessment)",
                    "StockIntakeUnits = " + detail.StockIntakeUnits
                    + "; ReorderPointIntakeUnits = " + detail.ReorderPointIntakeUnits
                    + "; OutstandingOrderCount = 0.",
                    IncidentSeverity.Warning, false));
            }
        }

        // --- D-09 / D-15 / D-16 / store --------------------------------------------

        /// <summary>General Store blockers and cadence notes, each with its specific cause.</summary>
        public static void ClassifyStore(
            StoreOperationsDetail detail, string businessKey,
            List<BusinessBlocker> blockers, List<BusinessNote> notes)
        {
            if (detail == null || blockers == null)
                return;
            string key = businessKey ?? string.Empty;

            if (detail.ClerkPersonId < 0 && detail.ClerkCandidateCount <= 0)
            {
                blockers.Add(new BusinessBlocker(
                    "D-15", key,
                    "No service clerk resolves for the counter",
                    "The GeneralStoreServiceClerkPolicy candidate list is empty: no paid clerk is assigned "
                    + "and the owner has no person id to fall back to as owner-operator. Physical counter "
                    + "service fails closed (STORE_UNSERVED) — no TendCustomer, no sale (D-16).",
                    "GeneralStoreServiceClerkPolicy.CandidatePersonIds / GeneralStoreRuntimeManager.TryGetServiceClerkPersonId",
                    "ClerkPersonId = -1; ClerkCandidateCount = 0.",
                    IncidentSeverity.Warning, false));
            }

            if (detail.QueueMaxTolerated > 0 && detail.QueueWaitingCount >= detail.QueueMaxTolerated)
            {
                blockers.Add(new BusinessBlocker(
                    "D-16", key,
                    "Counter queue is full",
                    "Waiting customers (" + detail.QueueWaitingCount + ") have reached the tolerated maximum ("
                    + detail.QueueMaxTolerated + "); further arrivals get STORE_UNSERVED and no sale settles "
                    + "until the queue drains.",
                    "CustomerQueue (MaxToleratedQueueLength)",
                    "QueueWaitingCount = " + detail.QueueWaitingCount
                    + "; QueueMaxTolerated = " + detail.QueueMaxTolerated + ".",
                    IncidentSeverity.Warning, false));
            }

            if (detail.DailySalesState == DailySalesCadenceState.LegacyDeadNoPlayerStore && notes != null)
            {
                notes.Add(new BusinessNote(
                    "D-09", key,
                    "Daily legacy sales settlement (ResolveDailySales) early-returns with no player store bound "
                    + "(D-09); the live daily cadence for this NPC store is the Lane 9b reset "
                    + "(DriveNpcDailySalesCadence / BeginDailySalesCadence), not the legacy loop.",
                    "GeneralStoreRuntimeManager.ResolveDailySales / DriveNpcDailySalesCadence"));
            }
        }

        // --- General (all businesses) ------------------------------------------------

        /// <summary>
        /// Blockers every business can show: no runtime state, the weekly
        /// blocked reason verbatim (never a bare "blocked"), a worker
        /// suspended for missed payroll. Cash at zero is NOT a blocker by
        /// itself: the yard ending a run at 29c is a legitimate economic
        /// stoppage (D-25), and the monitor must not cry wolf over it.
        /// Required-for-opening slots are a staffing-need flag, not a
        /// causal gate (WorkerSlotState), so an unfilled one is a note.
        /// </summary>
        public static void ClassifyGeneral(
            BusinessSnapshot snapshot,
            List<BusinessBlocker> blockers, List<BusinessNote> notes)
        {
            if (snapshot == null || blockers == null)
                return;
            string key = snapshot.Key ?? string.Empty;

            if (!snapshot.HasRuntimeState)
            {
                blockers.Add(new BusinessBlocker(
                    "BIZ-STATE", key,
                    "No runtime state bound",
                    "The business instance exists but no BusinessRuntimeState is bound, so there is no cash, "
                    + "stock, staffing or settlement state to observe. Nothing can be said about this "
                    + "business's health until a runtime is bound.",
                    "BusinessInstanceState.RuntimeState",
                    "HasRuntimeState = false.",
                    IncidentSeverity.Unknown, false));
                return;
            }

            string weeklyBlocked = snapshot.LastWeeklyBlockedReason ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(weeklyBlocked)
                && !string.Equals(weeklyBlocked.Trim(), "None", StringComparison.OrdinalIgnoreCase))
            {
                blockers.Add(new BusinessBlocker(
                    "BIZ-WEEKLY", key,
                    "Weekly settlement blocked",
                    weeklyBlocked.Trim(),
                    "SharedBusinessRuntimeManager weekly pass / BusinessRuntimeState.LastWeeklyBlockedReason",
                    "LastWeeklyBlockedReason = '" + weeklyBlocked.Trim() + "'.",
                    IncidentSeverity.Warning, false));
            }

            if (snapshot.WorkerSlots != null)
            {
                foreach (BusinessWorkerSnapshot slot in snapshot.WorkerSlots)
                {
                    if (slot == null)
                        continue;
                    if (slot.SuspendedForMissedPayroll)
                    {
                        blockers.Add(new BusinessBlocker(
                            "BIZ-PAYROLL", key,
                            "Worker suspended for missed payroll: " + slot.DisplayName,
                            "The slot's worker (" + slot.WorkerDisplayName + ") is suspended because payroll did "
                            + "not settle; the worker stays suspended until paid (WageSettlementService).",
                            "WageSettlementService / BusinessRuntimeState.WorkerSlots",
                            "Slot " + slot.SlotId + " suspendedForMissedPayroll; wage "
                            + slot.WeeklyWageCents + "c/week.",
                            IncidentSeverity.Warning, false));
                    }
                    else if (slot.RequiredForOpening && !slot.Filled && notes != null)
                    {
                        notes.Add(new BusinessNote(
                            "BIZ-STAFF", key,
                            "Required-for-opening slot '" + slot.DisplayName + "' is unfilled. This is a "
                            + "staffing-need flag, not a causal gate: whether the business may operate is "
                            + "decided from actual worker coverage, not from slot definitions.",
                            "BusinessRuntimeState.WorkerSlotState"));
                    }
                }
            }
        }

        /// <summary>
        /// Full report over the observed snapshots and cadences. Runs every
        /// classifier, attaches each cadence to its business, and verifies
        /// the 7 expected businesses are all visible.
        /// </summary>
        public static BusinessOperationsReport BuildReport(
            IEnumerable<BusinessSnapshot> snapshots,
            IEnumerable<BusinessSettlementCadence> cadences)
        {
            var snapshotList = snapshots != null
                ? new List<BusinessSnapshot>(snapshots)
                : new List<BusinessSnapshot>();
            var cadenceList = cadences != null
                ? new List<BusinessSettlementCadence>(cadences)
                : new List<BusinessSettlementCadence>();
            var blockers = new List<BusinessBlocker>();
            var notes = new List<BusinessNote>();

            var liveKeys = new List<string>();
            foreach (BusinessSnapshot snapshot in snapshotList)
            {
                if (snapshot == null)
                    continue;
                liveKeys.Add(snapshot.Key ?? string.Empty);
                ClassifyGeneral(snapshot, blockers, notes);
                if (snapshot.Bakery != null)
                    ClassifyBakery(snapshot.Bakery, snapshot.Key, blockers, notes);
                if (snapshot.Store != null)
                    ClassifyStore(snapshot.Store, snapshot.Key, blockers, notes);
                if (snapshot.FuelDealer != null)
                    ClassifyFuelDealer(snapshot.FuelDealer, snapshot.Key, blockers);
            }

            List<string> missing = VerifyExpectedBusinesses(liveKeys);
            return new BusinessOperationsReport(snapshotList, blockers, notes, cadenceList, missing);
        }
    }
}
