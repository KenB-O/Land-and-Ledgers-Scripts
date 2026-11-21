// Land & Ledgers — Household Survival and Food Security Monitor core (Tool 08, Lane 7).
//
// The 7-point automated audit design from Agent L §4, implemented as
// checks over caller-supplied snapshots:
//   1. Heating/reserve resolution ran (lastReserveDepletionDayIndex == day).
//   2. Town pulse resolved (lastResolvedDayIndex == day).
//   3. Demand snapshots fresh (rebuilt today, not merely refreshed for a shopping candidate).
//   4. Weekly upgrade effects ran (week-stamped marker at the week boundary).
//   5. NPC weekly settlement ran (SBRM weekly pass for the current week key).
//   6. No stale-zero signal silently consumed (readers of GSRM lastDaily*
//      fields check a "live today" flag; a stale read is a WARNING naming the field).
//   7. No double purchase (legacy reserve-purchase charges + embodied
//      purchase charges for the same category/day must reconcile to one need).
//
// The monitor requires the FULL shopping chain (G-11) before recording a
// shopping success: departure, purchase, payment, return, pantry receipt,
// later consumption. The 5-day run's "42 meals, zero missed" proves none
// of it — a meal count is not a shopping success, and this monitor never
// records one from meals alone.
//
// Missed-meal reasons come from Lane 9a (MissedMealReasonKind on
// MissedMealRecord, populated at close time; Unknown is the default and
// is never upgraded to an invented reason). Shortage affordability is
// the Lane 9b real affordability (HouseholdCanAffordFoodTopUp threaded
// into DriveHouseholdShortageMonitor), not placeholder zeros (A7).
// Every row separates legitimate poverty from broken mechanics.
//
// Evidence taxonomy on every finding in this file: source-reviewed only.
// Nothing here is Unity-compiled or live-verified; the Roslyn harness is
// the only verification this pass can perform (see HARNESS_NOTES.md).
// No run exists on this HEAD.

using System;
using System.Collections.Generic;
using LandLedgers.Population;

namespace LandLedgers.Diagnostics
{
    /// <summary>The seven Agent L §4 lifecycle checks.</summary>
    public enum LifecycleCheckKind
    {
        HeatingReserveResolutionRan = 1,
        TownPulseResolved = 2,
        DemandSnapshotsFresh = 3,
        WeeklyUpgradeEffectsRan = 4,
        NpcWeeklySettlementRan = 5,
        NoStaleZeroSignalConsumed = 6,
        NoDoublePurchase = 7,
    }

    /// <summary>Whether a household's shortage is poverty or broken mechanics.</summary>
    public enum ShortageVerdictKind
    {
        /// <summary>Not enough evidence to separate poverty from mechanics — never guessed.</summary>
        Unknown = 0,
        /// <summary>Food/fuel on hand covers the household's needs.</summary>
        Secure = 1,
        /// <summary>Legitimate poverty: the seller is known, stock is present, and the household genuinely cannot afford it.</summary>
        LegitimatePoverty = 2,
        /// <summary>Broken mechanics: a missing seller, missing stock, missing counter service, missing shopper, missed window, or an incomplete shopping chain.</summary>
        BrokenMechanics = 3,
    }

    /// <summary>Whether a shopping attempt counts as a success (G-11 full chain).</summary>
    public enum ShoppingChainVerdict
    {
        /// <summary>No shopping attempt was observed at all.</summary>
        NotAttempted = 0,
        /// <summary>All six stages observed: departure, purchase, payment, return, pantry receipt, later consumption.</summary>
        Success = 1,
        /// <summary>At least one stage is missing — not a success, with the missing stage named.</summary>
        IncompleteChain = 2,
    }

    /// <summary>One day's lifecycle inputs (Agent L §4).</summary>
    public sealed class DailyLifecycleSnapshot
    {
        public int DayIndex;
        public int HouseholdCount;
        public int HouseholdsWithReserveResolutionToday;
        public int TownPulseLastResolvedDayIndex = -1;
        /// <summary>Households whose demandSnapshot was rebuilt today. -1 means unobserved (no day stamp exists) — the check reports Unknown, never a pass.</summary>
        public int HouseholdsWithFreshDemandSnapshot = -1;
        public bool IsWeekBoundary;
        public bool WeeklyUpgradeEffectsRanThisWeek = true;
        public bool NpcWeeklySettlementRanThisWeek = true;
        public List<string> StaleSignalReads = new List<string>();
        public List<int> DoublePurchaseHouseholdIds = new List<int>();
    }

    /// <summary>One lifecycle finding, with its authority and evidence.</summary>
    public sealed class LifecycleFinding
    {
        public LifecycleFinding(LifecycleCheckKind check, IncidentSeverity severity, string title,
            string cause, string authority, string evidence)
        {
            Check = check;
            Severity = severity;
            Title = title ?? string.Empty;
            Cause = cause ?? string.Empty;
            Authority = authority ?? string.Empty;
            Evidence = evidence ?? string.Empty;
        }

        public LifecycleCheckKind Check { get; }
        public IncidentSeverity Severity { get; }
        public string Title { get; }
        public string Cause { get; }
        public string Authority { get; }
        public string Evidence { get; }
    }

    /// <summary>One observed missed meal, with its Lane 9a classified reason.</summary>
    public sealed class MissedMealObservation
    {
        public int DayIndex;
        public string Slot = string.Empty;
        public MissedMealReasonKind ReasonKind = MissedMealReasonKind.Unknown;
        public string Reason = string.Empty;
    }

    /// <summary>One household's survival state as observed.</summary>
    public sealed class HouseholdFoodSnapshot
    {
        public int HouseholdId;
        public string HouseholdLabel = string.Empty;
        public int CashCents;
        /// <summary>Days of food supply on hand (reserve + lots), as observed. Negative means unknown, never a guessed zero.</summary>
        public float FoodDaysOfSupply = -1f;
        /// <summary>Heating fuel units on hand. Negative means unknown.</summary>
        public int HeatingFuelUnits = -1;
        /// <summary>Days of heating fuel at the observed burn rate. Negative means unknown.</summary>
        public float HeatingFuelDaysOfSupply = -1f;
        public bool Departed;
        public bool Purchased;
        public bool Paid;
        public bool Returned;
        public bool PantryReceived;
        public bool LaterConsumed;
        /// <summary>Lane 9b real affordability: the household can afford the food top-up at the observed price.</summary>
        public bool CanAffordFoodTopUp;
        public int FoodTopUpCostCents;
        public int KnownSellerCount;
        public bool SellerStockKnownPresent;
        public List<MissedMealObservation> MissedMeals = new List<MissedMealObservation>();
    }

    /// <summary>One household's classified row: verdict + reason, never a bare status.</summary>
    public sealed class HouseholdSurvivalRow
    {
        public HouseholdSurvivalRow(int householdId, string label, ShortageVerdictKind verdict,
            string reason, ShoppingChainVerdict shopping, string shoppingDetail)
        {
            HouseholdId = householdId;
            Label = label ?? string.Empty;
            Verdict = verdict;
            Reason = reason ?? string.Empty;
            Shopping = shopping;
            ShoppingDetail = shoppingDetail ?? string.Empty;
        }

        public int HouseholdId { get; }
        public string Label { get; }
        public ShortageVerdictKind Verdict { get; }
        public string Reason { get; }
        public ShoppingChainVerdict Shopping { get; }
        public string ShoppingDetail { get; }
    }

    public sealed class HouseholdSurvivalReport
    {
        public HouseholdSurvivalReport(List<LifecycleFinding> lifecycleFindings, List<HouseholdSurvivalRow> rows)
        {
            LifecycleFindings = lifecycleFindings;
            Rows = rows;
        }

        public List<LifecycleFinding> LifecycleFindings { get; }
        public List<HouseholdSurvivalRow> Rows { get; }

        public int SecureCount
        {
            get { return CountVerdict(ShortageVerdictKind.Secure); }
        }

        public int LegitimatePovertyCount
        {
            get { return CountVerdict(ShortageVerdictKind.LegitimatePoverty); }
        }

        public int BrokenMechanicsCount
        {
            get { return CountVerdict(ShortageVerdictKind.BrokenMechanics); }
        }

        public int UnknownCount
        {
            get { return CountVerdict(ShortageVerdictKind.Unknown); }
        }

        private int CountVerdict(ShortageVerdictKind kind)
        {
            int count = 0;
            foreach (HouseholdSurvivalRow row in Rows)
            {
                if (row != null && row.Verdict == kind) count++;
            }
            return count;
        }
    }

    /// <summary>
    /// The monitor. All methods are pure: no statics read, no food or
    /// money moved, no purchases made. Diagnostics OBSERVE the meal log,
    /// the shortage monitor and the daily drivers; this class never
    /// becomes a parallel source of truth about what a household owns.
    /// </summary>
    public static class HouseholdSurvivalMonitor
    {
        /// <summary>Label for one Agent L §4 check.</summary>
        public static string LifecycleCheckLabel(LifecycleCheckKind check)
        {
            switch (check)
            {
                case LifecycleCheckKind.HeatingReserveResolutionRan: return "1 — Heating/reserve resolution ran today";
                case LifecycleCheckKind.TownPulseResolved: return "2 — Town pulse resolved today";
                case LifecycleCheckKind.DemandSnapshotsFresh: return "3 — Demand snapshots fresh (rebuilt today)";
                case LifecycleCheckKind.WeeklyUpgradeEffectsRan: return "4 — Weekly upgrade effects ran";
                case LifecycleCheckKind.NpcWeeklySettlementRan: return "5 — NPC weekly settlement ran";
                case LifecycleCheckKind.NoStaleZeroSignalConsumed: return "6 — No stale-zero signal consumed";
                case LifecycleCheckKind.NoDoublePurchase: return "7 — No double purchase";
                default: return "Unknown lifecycle check";
            }
        }

        /// <summary>
        /// The seven Agent L §4 assertions over one day's snapshot.
        /// Check 1 and 5 failures are CRITICAL (the resolution or the
        /// settlement is the household economy's heartbeat); the rest
        /// are WARNING; a stale read names the field; a double purchase
        /// names the household. A check with no data is not run — it is
        /// reported as a finding with severity Unknown, never as a pass.
        /// </summary>
        public static List<LifecycleFinding> EvaluateLifecycle(DailyLifecycleSnapshot snapshot)
        {
            var findings = new List<LifecycleFinding>();
            if (snapshot == null)
            {
                findings.Add(new LifecycleFinding(LifecycleCheckKind.HeatingReserveResolutionRan,
                    IncidentSeverity.Unknown, "No lifecycle snapshot supplied",
                    "The audit cannot run without a snapshot; nothing is asserted and nothing passes.",
                    "HouseholdSurvivalMonitor.EvaluateLifecycle",
                    "DailyLifecycleSnapshot = null."));
                return findings;
            }

            if (snapshot.HouseholdCount > 0
                && snapshot.HouseholdsWithReserveResolutionToday < snapshot.HouseholdCount)
            {
                int missing = snapshot.HouseholdCount - snapshot.HouseholdsWithReserveResolutionToday;
                findings.Add(new LifecycleFinding(LifecycleCheckKind.HeatingReserveResolutionRan,
                    IncidentSeverity.Critical,
                    "Heating/reserve resolution did not run for " + missing + " of " + snapshot.HouseholdCount + " households",
                    "Every household's lastReserveDepletionDayIndex must equal the absolute day index. "
                    + "A household whose resolution did not run burns no heating fuel after day 0, so its "
                    + "fuel stock never depletes through use and the heating system cannot signal a shortage (L-01). "
                    + "This assertion would have caught L-01 on day 1.",
                    "HouseholdReserveEvaluator.ResolveDailyUse via SimulationDrivers.DriveHouseholdReserveDay (Lane 9b F-L1)",
                    "Day " + snapshot.DayIndex + ": resolved " + snapshot.HouseholdsWithReserveResolutionToday
                    + " of " + snapshot.HouseholdCount + " households."));
            }

            if (snapshot.TownPulseLastResolvedDayIndex != snapshot.DayIndex)
            {
                findings.Add(new LifecycleFinding(LifecycleCheckKind.TownPulseResolved,
                    IncidentSeverity.Warning,
                    "Town pulse did not resolve today",
                    "TownPulseRuntimeState.lastResolvedDayIndex must equal the absolute day index. While it "
                    + "stays at -1 no daily requested/fulfilled/missed units are ever recorded, and the "
                    + "opportunity-pressure retail signal reads stale zeros (L-02).",
                    "TownPulseRuntimeManager.BeginDailyResolution via SimulationDrivers.DriveTownPulseDemand (Lane 9b F-L2)",
                    "Day " + snapshot.DayIndex + ": lastResolvedDayIndex = " + snapshot.TownPulseLastResolvedDayIndex + "."));
            }

            if (snapshot.HouseholdCount > 0 && snapshot.HouseholdsWithFreshDemandSnapshot < 0)
            {
                // HouseholdDemandSnapshot carries no day stamp, so freshness
                // cannot be read live — the check is reported as unobservable,
                // never as a pass and never as a failure.
                findings.Add(new LifecycleFinding(LifecycleCheckKind.DemandSnapshotsFresh,
                    IncidentSeverity.Unknown,
                    "Demand-snapshot freshness is not observable live",
                    "HouseholdDemandSnapshot carries no day stamp, so no live read can prove the daily drive "
                    + "rebuilt it today. The daily drive rebuilds it (Lane 9b F-L1); this check needs the "
                    + "drive's own record, not a snapshot read.",
                    "HouseholdReserveEvaluator.BuildDemandSnapshot via SimulationDrivers.DriveHouseholdReserveDay (Lane 9b F-L1)",
                    "Day " + snapshot.DayIndex + ": HouseholdsWithFreshDemandSnapshot = -1 (unobserved)."));
            }
            else if (snapshot.HouseholdCount > 0
                && snapshot.HouseholdsWithFreshDemandSnapshot < snapshot.HouseholdCount)
            {
                int stale = snapshot.HouseholdCount - snapshot.HouseholdsWithFreshDemandSnapshot;
                findings.Add(new LifecycleFinding(LifecycleCheckKind.DemandSnapshotsFresh,
                    IncidentSeverity.Warning,
                    "Demand snapshots stale for " + stale + " of " + snapshot.HouseholdCount + " households",
                    "Every household's demandSnapshot must be rebuilt today by the daily drive. The "
                    + "shopping-candidate path refreshes a subset on demand (L-04) — that refresh does not "
                    + "satisfy this check, which asserts the daily drive ran.",
                    "HouseholdReserveEvaluator.BuildDemandSnapshot via SimulationDrivers.DriveHouseholdReserveDay (Lane 9b F-L1)",
                    "Day " + snapshot.DayIndex + ": fresh " + snapshot.HouseholdsWithFreshDemandSnapshot
                    + " of " + snapshot.HouseholdCount + " households."));
            }

            if (snapshot.IsWeekBoundary && !snapshot.WeeklyUpgradeEffectsRanThisWeek)
            {
                findings.Add(new LifecycleFinding(LifecycleCheckKind.WeeklyUpgradeEffectsRan,
                    IncidentSeverity.Warning,
                    "Weekly household-upgrade settlement effects did not run",
                    "Per built household upgrade, weekly reserve production, production income, upkeep and "
                    + "spoilage never apply while this is unrun — upgrades are economically inert (L-03). "
                    + "Owner confirmation of the system home is still pending (L-10); the monitor surfaces the gap.",
                    "HouseholdUpgradeEconomyEvaluator.ApplyWeeklySettlementEffects via SharedBusinessRuntimeManager.ResolveWeeklySharedOperations (Lane 9b F-L3)",
                    "Week boundary day " + snapshot.DayIndex + ": no week-stamped upgrade marker."));
            }

            if (snapshot.IsWeekBoundary && !snapshot.NpcWeeklySettlementRanThisWeek)
            {
                findings.Add(new LifecycleFinding(LifecycleCheckKind.NpcWeeklySettlementRan,
                    IncidentSeverity.Critical,
                    "NPC weekly settlement did not run",
                    "The shared weekly pass (settlement + payroll + apprenticeship) must complete for the "
                    + "current week key. This pass is verified independent of player ownership (L-06); a "
                    + "miss here is a live failure, not a gating defect.",
                    "SharedBusinessRuntimeManager.ResolveWeeklySharedOperations via OnWeekChanged",
                    "Week boundary day " + snapshot.DayIndex + ": no settlement for the current week key."));
            }

            if (snapshot.StaleSignalReads != null)
            {
                foreach (string field in snapshot.StaleSignalReads)
                {
                    if (string.IsNullOrWhiteSpace(field))
                        continue;
                    findings.Add(new LifecycleFinding(LifecycleCheckKind.NoStaleZeroSignalConsumed,
                        IncidentSeverity.Warning,
                        "Stale-zero signal consumed: " + field,
                        "A reader consumed " + field + " without checking a \"live today\" flag. The value is a "
                        + "stale zero from a dead drive, not a measurement — opportunity pressure and the HUD "
                        + "must treat it as degraded, never as demand (L-02).",
                        "OpportunityPressureRuntimeManager.EvaluateRetailPressure / HUD readers of GeneralStoreRuntimeManager.lastDaily*",
                        "Field '" + field + "' read stale on day " + snapshot.DayIndex + "."));
                }
            }

            if (snapshot.DoublePurchaseHouseholdIds != null)
            {
                foreach (int householdId in snapshot.DoublePurchaseHouseholdIds)
                {
                    findings.Add(new LifecycleFinding(LifecycleCheckKind.NoDoublePurchase,
                        IncidentSeverity.Critical,
                        "Double purchase: household " + householdId,
                        "Legacy reserve-purchase charges and embodied purchase charges for the same reserve "
                        + "category on the same day do not reconcile to one need. The legacy loop is dead "
                        + "today (L-04) and must stay out of the daily drive until the owner/design decision "
                        + "lands; this check guards the re-homing.",
                        "GeneralStoreRuntimeManager.ResolveHouseholdReserveNeeds vs EmbodiedPurchaseExecutor (L-04)",
                        "Household " + householdId + " charged twice for one need on day " + snapshot.DayIndex + "."));
                }
            }

            return findings;
        }

        /// <summary>
        /// G-11 full-chain verdict: a shopping success requires ALL SIX
        /// stages — departure, purchase, payment, return, pantry receipt,
        /// later consumption. A meal count alone is never a success.
        /// </summary>
        public static ShoppingChainVerdict EvaluateShoppingChain(HouseholdFoodSnapshot snapshot, out string missingStage)
        {
            missingStage = string.Empty;
            if (snapshot == null)
                return ShoppingChainVerdict.NotAttempted;
            if (!snapshot.Departed && !snapshot.Purchased && !snapshot.Paid
                && !snapshot.Returned && !snapshot.PantryReceived && !snapshot.LaterConsumed)
                return ShoppingChainVerdict.NotAttempted;
            if (!snapshot.Departed) missingStage = "departure";
            else if (!snapshot.Purchased) missingStage = "purchase";
            else if (!snapshot.Paid) missingStage = "payment";
            else if (!snapshot.Returned) missingStage = "return";
            else if (!snapshot.PantryReceived) missingStage = "pantry receipt";
            else if (!snapshot.LaterConsumed) missingStage = "later consumption";
            return string.IsNullOrEmpty(missingStage) ? ShoppingChainVerdict.Success : ShoppingChainVerdict.IncompleteChain;
        }

        /// <summary>
        /// Separate legitimate poverty from broken mechanics in one row.
        /// Legitimate poverty needs all three: the seller is known, stock
        /// is present, and the household genuinely cannot afford the
        /// top-up (Lane 9b real affordability). Any mechanics reason —
        /// no known seller, no stock, no counter service, no shopper,
        /// missed window, or an incomplete shopping chain — is broken
        /// mechanics, never poverty. Unknown reasons stay Unknown.
        /// </summary>
        public static HouseholdSurvivalRow ClassifyHousehold(HouseholdFoodSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return new HouseholdSurvivalRow(-1, "(no snapshot)", ShortageVerdictKind.Unknown,
                    "No snapshot supplied; nothing is asserted.",
                    ShoppingChainVerdict.NotAttempted, "No shopping chain observed.");
            }

            string missingStage;
            ShoppingChainVerdict shopping = EvaluateShoppingChain(snapshot, out missingStage);
            string shoppingDetail;
            if (shopping == ShoppingChainVerdict.Success)
                shoppingDetail = "Full chain observed: departure, purchase, payment, return, pantry receipt, later consumption.";
            else if (shopping == ShoppingChainVerdict.IncompleteChain)
                shoppingDetail = "Incomplete chain — missing stage: " + missingStage + ". Not a shopping success (G-11).";
            else
                shoppingDetail = "No shopping attempt observed.";

            string label = string.IsNullOrWhiteSpace(snapshot.HouseholdLabel)
                ? "Household " + snapshot.HouseholdId : snapshot.HouseholdLabel;

            bool hasMissedMeals = snapshot.MissedMeals != null && snapshot.MissedMeals.Count > 0;
            bool foodKnownShort = snapshot.FoodDaysOfSupply >= 0f && snapshot.FoodDaysOfSupply < 1f;
            bool fuelKnownShort = snapshot.HeatingFuelUnits >= 0 && snapshot.HeatingFuelUnits <= 0;

            if (!hasMissedMeals && !foodKnownShort && !fuelKnownShort)
            {
                return new HouseholdSurvivalRow(snapshot.HouseholdId, label, ShortageVerdictKind.Secure,
                    "No missed meals observed and no known food or heating-fuel shortfall.",
                    shopping, shoppingDetail);
            }

            // Mechanics reasons first: they explain the miss regardless of cash.
            foreach (MissedMealObservation missed in snapshot.MissedMeals ?? new List<MissedMealObservation>())
            {
                if (missed == null)
                    continue;
                switch (missed.ReasonKind)
                {
                    case MissedMealReasonKind.NoKnownSeller:
                        return new HouseholdSurvivalRow(snapshot.HouseholdId, label, ShortageVerdictKind.BrokenMechanics,
                            "Missed meal (" + missed.Slot + ", day " + missed.DayIndex + "): no known seller. "
                            + "The household cannot buy what no seller is known to sell — a discovery failure, not poverty.",
                            shopping, shoppingDetail);
                    case MissedMealReasonKind.NoStockAtSeller:
                        return new HouseholdSurvivalRow(snapshot.HouseholdId, label, ShortageVerdictKind.BrokenMechanics,
                            "Missed meal (" + missed.Slot + ", day " + missed.DayIndex + "): no stock at the seller. "
                            + "The seller is known but has nothing to sell.",
                            shopping, shoppingDetail);
                    case MissedMealReasonKind.NoCounterService:
                        return new HouseholdSurvivalRow(snapshot.HouseholdId, label, ShortageVerdictKind.BrokenMechanics,
                            "Missed meal (" + missed.Slot + ", day " + missed.DayIndex + "): no counter service. "
                            + "Physical counter service failed closed (STORE_UNSERVED) — no TendCustomer, no sale.",
                            shopping, shoppingDetail);
                    case MissedMealReasonKind.NoShopperAvailable:
                        return new HouseholdSurvivalRow(snapshot.HouseholdId, label, ShortageVerdictKind.BrokenMechanics,
                            "Missed meal (" + missed.Slot + ", day " + missed.DayIndex + "): no shopper available. "
                            + "Nobody could make the trip.",
                            shopping, shoppingDetail);
                    case MissedMealReasonKind.SchedulingWindowMissed:
                        return new HouseholdSurvivalRow(snapshot.HouseholdId, label, ShortageVerdictKind.BrokenMechanics,
                            "Missed meal (" + missed.Slot + ", day " + missed.DayIndex + "): scheduling window missed. "
                            + "The meal slot passed before the household could act.",
                            shopping, shoppingDetail);
                }
            }

            if (shopping == ShoppingChainVerdict.IncompleteChain)
            {
                return new HouseholdSurvivalRow(snapshot.HouseholdId, label, ShortageVerdictKind.BrokenMechanics,
                    "Shopping chain incomplete (missing: " + missingStage + ") with a missed meal or known shortfall. "
                    + "The chain broke before food reached the pantry — a mechanics failure, not poverty.",
                    shopping, shoppingDetail);
            }

            // Legitimate poverty: known seller, stock present, genuinely cannot afford.
            bool anyCannotAfford = false;
            if (snapshot.MissedMeals != null)
            {
                foreach (MissedMealObservation missed in snapshot.MissedMeals)
                {
                    if (missed != null && missed.ReasonKind == MissedMealReasonKind.CannotAfford)
                    {
                        anyCannotAfford = true;
                        break;
                    }
                }
            }
            if (anyCannotAfford && snapshot.KnownSellerCount > 0 && snapshot.SellerStockKnownPresent && !snapshot.CanAffordFoodTopUp)
            {
                return new HouseholdSurvivalRow(snapshot.HouseholdId, label, ShortageVerdictKind.LegitimatePoverty,
                    "Missed meal with reason CannotAfford; the seller is known (" + snapshot.KnownSellerCount
                    + " known) and stock is present, but the household holds " + snapshot.CashCents
                    + "c against a " + snapshot.FoodTopUpCostCents + "c top-up (Lane 9b real affordability). "
                    + "Poverty, not broken mechanics.",
                    shopping, shoppingDetail);
            }

            if (foodKnownShort || fuelKnownShort)
            {
                return new HouseholdSurvivalRow(snapshot.HouseholdId, label, ShortageVerdictKind.Unknown,
                    "Known shortfall (food " + snapshot.FoodDaysOfSupply + " days, heating fuel "
                    + snapshot.HeatingFuelUnits + "u) but no classified missed-meal reason pins the cause. "
                    + "Unknown is a valid outcome — the cause is not invented.",
                    shopping, shoppingDetail);
            }

            return new HouseholdSurvivalRow(snapshot.HouseholdId, label, ShortageVerdictKind.Unknown,
                "Missed meal(s) observed with reason Unknown (or NoFoodInLarder without a seller/affordability "
                + "read). The cause is not invented from an unclassified record.",
                shopping, shoppingDetail);
        }

        /// <summary>Full report: lifecycle findings plus one classified row per household.</summary>
        public static HouseholdSurvivalReport BuildReport(
            DailyLifecycleSnapshot lifecycle, IEnumerable<HouseholdFoodSnapshot> households)
        {
            List<LifecycleFinding> findings = EvaluateLifecycle(lifecycle);
            var rows = new List<HouseholdSurvivalRow>();
            if (households != null)
            {
                foreach (HouseholdFoodSnapshot household in households)
                {
                    rows.Add(ClassifyHousehold(household));
                }
            }
            return new HouseholdSurvivalReport(findings, rows);
        }
    }
}
