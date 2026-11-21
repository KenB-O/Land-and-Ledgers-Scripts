// Land & Ledgers — Scenario Lab survival acceptance test (Lane 8, Tool 10,
// Agent N §2).
//
// Agent N's household survival end-to-end acceptance test as a runnable
// scenario definition with exact assertions — not a stub. The definition
// carries the default short-run configuration (10x, 15 days, seed 1886), the eight
// stages with their assertions and evidence sources, and the pass bars
// P-1–P-8 (all PROPOSED until the manager confirms them, N-14).
//
// The lab CANNOT execute a live run here (no Unity in this environment), and
// it says so: with no run evidence every stage is BLOCKED and the overall
// verdict is NO LIVE RUN. With run evidence (from the runner's artifacts,
// read by RunArtifactReader) the evaluator asserts each stage exactly as
// Agent N specified — including the S7 caveat that State == Done alone does
// NOT prove receipt (Done is set even when the batch is still in custody;
// the test requires result.Success && UnitsAcquired > 0 AND the receive
// line AND the lot deposit together).
//
// The 15-day test without the Save/Continue midpoint is still valid and is
// built first; the midpoint (save day 7, fresh-scene load, resume to day 15)
// needs F-S5 holder setup and F-S7 runner Continue mode first, and is
// recorded as BLOCKED until they land — never silently skipped.

using System;
using System.Collections.Generic;

namespace LandLedgers.Diagnostics
{
    /// <summary>Verdict of one survival stage or pass bar.</summary>
    public enum SurvivalVerdict
    {
        /// <summary>The stage's evidence exists and its assertion holds.</summary>
        Pass = 0,

        /// <summary>The stage's evidence exists and its assertion fails.</summary>
        Fail = 1,

        /// <summary>Cannot be evaluated: no live run, or a named prior repair is missing.</summary>
        Blocked = 2,
    }

    /// <summary>One of Agent N's eight survival stages, with its exact assertion.</summary>
    public sealed class SurvivalStageSpec
    {
        public SurvivalStageSpec(string stageId, string title, string assertion, string evidenceSource, string requiredPriorRepair)
        {
            StageId = stageId ?? string.Empty;
            Title = title ?? string.Empty;
            Assertion = assertion ?? string.Empty;
            EvidenceSource = evidenceSource ?? string.Empty;
            RequiredPriorRepair = requiredPriorRepair ?? string.Empty;
        }

        /// <summary>Stage id ("S1".."S8").</summary>
        public string StageId { get; }

        /// <summary>Stage title.</summary>
        public string Title { get; }

        /// <summary>The exact assertion, as Agent N specified it.</summary>
        public string Assertion { get; }

        /// <summary>Where the evidence lives (file:line / run artifact).</summary>
        public string EvidenceSource { get; }

        /// <summary>Prior repair the stage needs first, or empty when ready to implement from verified source.</summary>
        public string RequiredPriorRepair { get; }

        /// <summary>True when the stage is ready to implement from verified source (no prior repair named).</summary>
        public bool ReadyFromVerifiedSource
        {
            get { return RequiredPriorRepair.Length == 0; }
        }
    }

    /// <summary>One proposed pass bar P-1–P-8 (Agent N §2.4). All are PROPOSED until the manager confirms them (N-14).</summary>
    public sealed class SurvivalPassBar
    {
        public SurvivalPassBar(string barId, string text, string grounding)
        {
            BarId = barId ?? string.Empty;
            Text = text ?? string.Empty;
            Grounding = grounding ?? string.Empty;
        }

        /// <summary>Bar id ("P-1".."P-8").</summary>
        public string BarId { get; }

        /// <summary>The bar, in words.</summary>
        public string Text { get; }

        /// <summary>What the threshold is grounded on.</summary>
        public string Grounding { get; }

        /// <summary>True: proposed, manager to confirm (N-14). The lab encodes the proposed bars and flags them PROPOSED in the report.</summary>
        public bool IsProposed
        {
            get { return true; }
        }
    }

    /// <summary>One named scenario in the lab catalog (§10 list). The survival scenario carries the full eight-stage spec; the others carry their fixed configuration and one-line acceptance assertion.</summary>
    public sealed class LabScenarioDefinition
    {
        public LabScenarioDefinition(string id, string title, int seed, string speed, int targetDays, string acceptance, string requiredPriorRepair)
        {
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
            Seed = seed;
            Speed = speed ?? string.Empty;
            TargetDays = targetDays;
            Acceptance = acceptance ?? string.Empty;
            RequiredPriorRepair = requiredPriorRepair ?? string.Empty;
        }

        /// <summary>Stable scenario id.</summary>
        public string Id { get; }

        /// <summary>Scenario title.</summary>
        public string Title { get; }

        /// <summary>Deterministic seed (1886 for every scenario in this lab).</summary>
        public int Seed { get; }

        /// <summary>Recommended short physical-observation speed preset.</summary>
        public string Speed { get; }

        /// <summary>Target days for the run.</summary>
        public int TargetDays { get; }

        /// <summary>The acceptance assertion, in words.</summary>
        public string Acceptance { get; }

        /// <summary>Prior repair the scenario needs first, or empty.</summary>
        public string RequiredPriorRepair { get; }
    }

    /// <summary>
    /// Run evidence for the survival acceptance test. Plain data: the runner
    /// (or RunArtifactReader, from a run directory) fills what it measured
    /// and leaves the rest at its NOT_CAPTURED sentinel. A stage with no
    /// evidence is a failed or blocked stage, never a passed one. Unknown
    /// counts are -1 (NOT_CAPTURED), never zero, never a guess.
    /// </summary>
    public sealed class SurvivalRunEvidence
    {
        /// <summary>True when a live run produced this evidence. False = no run exists on this HEAD.</summary>
        public bool HasLiveRun;

        /// <summary>Run id (e.g. 20261120T192311Z-343a8cd2), empty when no run.</summary>
        public string RunId = string.Empty;

        /// <summary>Branch the run executed on, empty when unknown.</summary>
        public string Branch = string.Empty;

        /// <summary>Full commit SHA the run executed on, empty when unknown.</summary>
        public string CommitSha = string.Empty;

        /// <summary>Seed the run used (1886 for the acceptance test).</summary>
        public int Seed = -1;

        /// <summary>Exact speed name recorded for the evaluated run; retained rather than replaced with a default.</summary>
        public string Speed = string.Empty;

        /// <summary>Target days (15 for the acceptance test).</summary>
        public int TargetDays = -1;

        /// <summary>Scenario ID recorded in metadata.json.</summary>
        public string Scenario = string.Empty;

        /// <summary>Startup mode recorded in metadata.json.</summary>
        public string StartupMode = string.Empty;

        /// <summary>Days actually completed (-1 = NOT_CAPTURED).</summary>
        public int DaysCompleted = -1;

        /// <summary>Meals eaten per absolute day, index 0 = day 1. Empty when not captured.</summary>
        public List<int> MealsEatenPerDay = new List<int>();

        /// <summary>Meals missed per absolute day, index 0 = day 1. Empty when not captured.</summary>
        public List<int> MealsMissedPerDay = new List<int>();

        /// <summary>Household lot units per absolute day, index 0 = day 1. Empty when not captured.</summary>
        public List<int> HouseholdLotsPerDay = new List<int>();

        /// <summary>[FoodEconomy] purchase settled lines in the run (-1 = NOT_CAPTURED).</summary>
        public int SettleLineCount = -1;

        /// <summary>[FoodEconomy] purchase received lines in the run (-1 = NOT_CAPTURED).</summary>
        public int ReceiveLineCount = -1;

        /// <summary>Receipt ids appearing in BOTH a settle line and a receive line (-1 = NOT_CAPTURED).</summary>
        public int ReceiptIdsMatched = -1;

        /// <summary>Receipt ids settled twice (double settlement) (-1 = NOT_CAPTURED).</summary>
        public int DoubleSettlements = -1;

        /// <summary>Shopping trips ABORTED with "No physical route reaches seller location" (-1 = NOT_CAPTURED).</summary>
        public int TripsAbortedNoRoute = -1;

        /// <summary>"Counter service expired" warnings in the run (-1 = NOT_CAPTURED).</summary>
        public int CounterServiceExpiries = -1;

        /// <summary>Trips stuck in Planned for more than 24 game hours (-1 = NOT_CAPTURED).</summary>
        public int TripsStuckPlannedOver24h = -1;

        /// <summary>Trips that reached Done with UnitsAcquired == 0 (-1 = NOT_CAPTURED). Done alone does NOT prove receipt (S7 caveat).</summary>
        public int TripsDoneWithZeroUnits = -1;

        /// <summary>Meals served from generic staple_food_unspecified (-1 = NOT_CAPTURED; must be 0).</summary>
        public int MealsFromGenericStaple = -1;

        /// <summary>Missed meals where the larder held a full serving at the slot time (-1 = NOT_CAPTURED; must be 0; needs A5).</summary>
        public int LarderFullMissedMeals = -1;

        /// <summary>Households whose cash fell below zero (-1 = NOT_CAPTURED; must be 0).</summary>
        public int NegativeCashHouseholds = -1;

        /// <summary>Longest run of consecutive days with 0 meals eaten for any one household (-1 = NOT_CAPTURED; must be under 3).</summary>
        public int MaxConsecutiveZeroMealDays = -1;

        /// <summary>Minimum completed purchase-to-receipt trips per household per 3-day window while a need was open (-1 = NOT_CAPTURED; must be >= 1).</summary>
        public int MinTripsPerHouseholdPer3Days = -1;

        /// <summary>Needs opened within 1 day for households below 7 days of cover (null = NOT_CAPTURED).</summary>
        public bool? NeedsOpenedWithin1Day;

        /// <summary>True when the A7 placeholder-zero blocker applies (monitor fed zeros, affordability branch cannot fire) — S1 then records the need but cannot assert the affordability classification.</summary>
        public bool Stage1BlockedByA7;

        /// <summary>Shoppers chosen within the same day for every open need, availability-first (null = NOT_CAPTURED).</summary>
        public bool? ShoppersChosenSameDay;

        /// <summary>Every planned trip left Planned within 24 game hours of planning (null = NOT_CAPTURED).</summary>
        public bool? TripsLeftPlannedWithin24h;

        /// <summary>Every trip that reached AtStore was served via a completed TendCustomer, never via the compatibility direct-settlement path (null = NOT_CAPTURED).</summary>
        public bool? ServedViaTendCustomer;

        /// <summary>Every settled purchase has UnitsSettled > 0 and the trip's Batch.ReceiptId equals the settle line's receipt (null = NOT_CAPTURED).</summary>
        public bool? SettlementReceiptsMatch;

        /// <summary>Every received lot was deposited with the purchased item id, units and seller source label, and the need was decremented by exactly UnitsAcquired (null = NOT_CAPTURED).</summary>
        public bool? LotDepositedAndNeedDecremented;

        /// <summary>True when LotIdentityResolver is wired in production so a meal can be traced to its received lot (A6, Lane 9a: the resolver is constructed in production, assigned to the meal service, and consulted at meal time). The Scenario Lab sets this from a source check on the evaluated tree; a run on another tree is reported against this tree's wiring. False means S8 can only assert meals eaten from any identified lot of that item.</summary>
        public bool LotTracingAvailable;

        /// <summary>True when a Save/Continue midpoint (save day 7, fresh-scene load, resume to day 15) was executed in this run.</summary>
        public bool ContinueMidpointRun;
    }

    /// <summary>Verdict of one stage or bar, with the reason in words.</summary>
    public sealed class SurvivalStageVerdict
    {
        public SurvivalStageVerdict(SurvivalVerdict verdict, string reason)
        {
            Verdict = verdict;
            Reason = reason ?? string.Empty;
        }

        /// <summary>Pass / Fail / Blocked.</summary>
        public SurvivalVerdict Verdict { get; }

        /// <summary>Why: the evidence, the failing assertion, or the named blocker.</summary>
        public string Reason { get; }

        /// <summary>True when the stage passed.</summary>
        public bool Passed
        {
            get { return Verdict == SurvivalVerdict.Pass; }
        }
    }

    /// <summary>Overall verdict of the survival acceptance test.</summary>
    public enum SurvivalOverallVerdict
    {
        /// <summary>All stages and bars pass, and the proposed bars have owner approval.</summary>
        Accepted = 0,

        /// <summary>At least one stage or bar fails. The report names the first failing stage or bar.</summary>
        Failed = 1,

        /// <summary>Cannot be evaluated: no live run, or a named prior repair is missing.</summary>
        NoLiveRun = 2,

        /// <summary>Evidence meets all proposed bars, but their acceptance criteria are not owner-approved.</summary>
        Provisional = 3,
    }

    /// <summary>The evaluated survival acceptance test: per-stage and per-bar verdicts and the overall verdict.</summary>
    public sealed class SurvivalAcceptanceResult
    {
        public SurvivalAcceptanceResult(
            SurvivalOverallVerdict overall, string runId,
            Dictionary<string, SurvivalStageVerdict> stageVerdicts,
            Dictionary<string, SurvivalStageVerdict> barVerdicts,
            string firstFailure,
            string configurationSummary = "",
            string acceptanceNote = "")
        {
            Overall = overall;
            RunId = runId ?? string.Empty;
            StageVerdicts = stageVerdicts ?? new Dictionary<string, SurvivalStageVerdict>();
            BarVerdicts = barVerdicts ?? new Dictionary<string, SurvivalStageVerdict>();
            FirstFailure = firstFailure ?? string.Empty;
            ConfigurationSummary = configurationSummary ?? string.Empty;
            AcceptanceNote = acceptanceNote ?? string.Empty;
        }

        /// <summary>Overall verdict. Accepted only when every stage and every bar passes.</summary>
        public SurvivalOverallVerdict Overall { get; }

        /// <summary>Run id evaluated, empty when no run exists.</summary>
        public string RunId { get; }

        /// <summary>Stage id ("S1".."S8") -> verdict.</summary>
        public Dictionary<string, SurvivalStageVerdict> StageVerdicts { get; }

        /// <summary>Bar id ("P-1".."P-8") -> verdict.</summary>
        public Dictionary<string, SurvivalStageVerdict> BarVerdicts { get; }

        /// <summary>First failing stage or bar, in words; empty when Accepted.</summary>
        public string FirstFailure { get; }

        /// <summary>Run configuration as recorded in its artifacts; never replaced with scenario defaults.</summary>
        public string ConfigurationSummary { get; }

        /// <summary>Qualification attached to a result that cannot be called accepted.</summary>
        public string AcceptanceNote { get; }

        /// <summary>True only when the test is Accepted.</summary>
        public bool Accepted
        {
            get { return Overall == SurvivalOverallVerdict.Accepted; }
        }

        /// <summary>True when all scored checks pass but owner approval of proposed bars is pending.</summary>
        public bool Provisional
        {
            get { return Overall == SurvivalOverallVerdict.Provisional; }
        }

        /// <summary>Verdict for one stage id; Blocked when the id is unknown.</summary>
        public SurvivalStageVerdict StageVerdict(string stageId)
        {
            SurvivalStageVerdict verdict;
            if (stageId != null && StageVerdicts.TryGetValue(stageId, out verdict))
            {
                return verdict;
            }
            return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Unknown stage id '" + stageId + "'.");
        }

        /// <summary>Verdict for one bar id; Blocked when the id is unknown.</summary>
        public SurvivalStageVerdict BarVerdict(string barId)
        {
            SurvivalStageVerdict verdict;
            if (barId != null && BarVerdicts.TryGetValue(barId, out verdict))
            {
                return verdict;
            }
            return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Unknown bar id '" + barId + "'.");
        }
    }

    /// <summary>
    /// Lane 8 (Tool 10, Agent N §2) — the Scenario Lab's survival acceptance
    /// scenario: default short-run configuration, eight stages with exact assertions,
    /// pass bars P-1–P-8, and the evaluator that asserts a run's evidence
    /// against them. The lab is a real, wired definition: the evaluator runs
    /// against RunArtifactReader output, and the Editor window drives it
    /// against a run directory. It cannot execute a live run here (no Unity);
    /// with no run evidence it reports NO LIVE RUN, never a pass.
    /// </summary>
    public static class SurvivalAcceptanceScenario
    {
        // ---- Default short-run configuration; longer completed runs may supply the same survival window. ----

        /// <summary>Deterministic TownGenerationSettings seed.</summary>
        public const int Seed = 1886;

        /// <summary>Run length in days: covers exhaustion (days 8–10) plus 5 post-exhaustion days.</summary>
        public const int TargetDays = 15;

        /// <summary>Recommended short physical-observation preset; longer runs retain and report their actual speed.</summary>
        public const string Speed = "10x";

        /// <summary>Startup mode: New Game, real bootstrap.</summary>
        public const string StartupMode = "NewGame";

        /// <summary>Scenario id.</summary>
        public const string ScenarioId = "millbrook-development-town";

        /// <summary>
        /// These survival bars are still labeled PROPOSED in the approved
        /// handoff. Keep passing run evidence provisional until an owner
        /// decision is recorded and this constant is deliberately updated.
        /// </summary>
        public const bool PassBarsOwnerApproved = false;

        /// <summary>Scene.</summary>
        public const string ScenePath = "Assets/Main Scene.unity";

        /// <summary>Expected counts: exact match, not minimum (14 residents, 8 households, 7 businesses).</summary>
        public const int ExpectedResidents = 14;

        /// <summary>Expected households (exact).</summary>
        public const int ExpectedHouseholds = 8;

        /// <summary>Expected businesses (exact).</summary>
        public const int ExpectedBusinesses = 7;

        /// <summary>Approved day length in game seconds.</summary>
        public const int DayLengthSeconds = 86400;

        /// <summary>Save/Continue midpoint: save at end of this absolute day, Continue in a fresh scene, resume to day 15.</summary>
        public const int SaveDayIndex = 7;

        /// <summary>Meal slots per day (14 residents x 3).</summary>
        public const int MealSlotsPerDay = 42;

        /// <summary>Meal clock minutes: 07:30 / 12:00 / 18:30, flex 60 min (DailyActivityPlanner.DefaultMealMinutes = {450, 720, 1110}).</summary>
        public static readonly int[] MealMinutes = { 450, 720, 1110 };

        /// <summary>Days 11–15 bar: average meals eaten per day (P-2, PROPOSED).</summary>
        public const int Days11To15MinimumAverageMeals = 38;

        // ---- The eight stages (Agent N §2.3) ----

        /// <summary>The eight stage specifications, in order.</summary>
        public static readonly List<SurvivalStageSpec> StageSpecs = new List<SurvivalStageSpec>
        {
            new SurvivalStageSpec("S1", "Need opens",
                "For every household whose identified food lots fall below 7 days of cover, an open need exists within 1 day, with ReasonSummary recording days of cover and threshold.",
                "HouseholdNeedRegistry; SimulationDrivers.cs:15523 DriveHouseholdShortageMonitor line",
                "A7 — the monitor is fed placeholder zeros (upcomingObligationCents: 0, expectedIncomeCents: 0, CanProduce = false, HasCash = balance > 0), so the affordability branch cannot fire."),
            new SurvivalStageSpec("S2", "Shopper chosen availability-first",
                "For every open need, a shopper is chosen within the same day; the choice records the shopper person id and the availability reason; no choice is made by sex or by an assumed home position.",
                "ShoppingTripResult (ShoppingEventKind.ShopperAssignment, HouseholdShoppingLoop.cs:784); HouseholdShoppingLoop.TripHistory (:187)",
                string.Empty),
            new SurvivalStageSpec("S3", "Trip planned and departs",
                "Every open need with a chosen shopper has a PlannedShoppingTrip with a departure minute; the trip leaves Planned state within 24 game hours of planning.",
                "ShoppingTripDriver.ActiveTrips; daily snapshot activeShoppingTrips (Playtest60DayRunnerWindow.cs:1200-1215)",
                "B-01/B-03 — outbound route and seller-location authoring (the same StartNotWalkable family)."),
            new SurvivalStageSpec("S4", "Physical outbound travel arrives",
                "Zero trips ABORTED with 'No physical route reaches seller location' in the 15-day window; zero trips exceeding the 24-hour liveness deadline; every trip that departs reaches AtStore.",
                "[ShoppingTrip] ABORTED lines in console-events.jsonl; ShoppingTripDriver.cs:60-68 deadline fields",
                "Seller-location / walkable-floor authoring; destinations enumerated per (signature, destination)."),
            new SurvivalStageSpec("S5", "Counter service completes",
                "Zero 'counter service expired' warnings in the 15-day window; every trip that reaches AtStore reaches AwaitingService and then InStore via a completed TendCustomer — never via the compatibility direct-settlement path.",
                "[ShoppingTrip] counter service expired lines; TendCustomer task completion in TaskAuthority",
                string.Empty),
            new SurvivalStageSpec("S6", "Settlement posts exactly once, with a receipt",
                "For every completed purchase, exactly one '[FoodEconomy] purchase settled' line exists with household debit == seller cash credit + carrier cash credit (or the matching receivable figures for credit mode); UnitsSettled > 0; the trip's Batch.ReceiptId equals the receipt in the line.",
                "HouseholdShoppingLoop.cs:2103 settle line; console-events.jsonl",
                string.Empty),
            new SurvivalStageSpec("S7", "Physical return and receipt into lots",
                "For every settled purchase, exactly one '[FoodEconomy] purchase received' line exists with the SAME receipt id as S6; the trip reaches TripState.Done with result.Success == true and result.UnitsAcquired > 0; the household inventory gains a lot with the purchased item id, units and seller source label; the need's UnitsNeeded is decremented by exactly UnitsAcquired. State == Done alone does NOT prove receipt: Done is set at HouseholdShoppingLoop.cs:3246 even when the batch is still in custody.",
                "HouseholdShoppingLoop.cs:1113 receive line; TripHistory (ShoppingEventKind.Receipt, :3233); entity-snapshots.jsonl household-lot entries",
                string.Empty),
            new SurvivalStageSpec("S8", "The received food is eaten",
                "Within 2 days of a receipt, at least one meal is served from the received lot; over days 11–15 the run averages >= 38 of 42 meal slots eaten per day; zero missed meals where the larder held a full serving at the slot time.",
                "hub.MealLog.GetMeals / GetMissedMeals (Playtest60DayRunnerWindow.cs:1146-1162); LastMealLotIdentities when the resolver is wired",
                "A6 for lot tracing; A5 for the missed-with-food assertion."),
        };

        // ---- Pass bars P-1–P-8 (Agent N §2.4 — all PROPOSED, N-14) ----

        /// <summary>The eight pass bars, all PROPOSED until the manager confirms them.</summary>
        public static readonly List<SurvivalPassBar> PassBars = new List<SurvivalPassBar>
        {
            new SurvivalPassBar("P-1", "Days 1–7: 42/42 slots eaten every day, 0 missed.",
                "Baseline achieves this on starting supplies; it is the control period."),
            new SurvivalPassBar("P-2", "Days 11–15 average: >= 38/42 slots eaten per day (>= 90%).",
                "42 slots/day is the configured demand (14 residents x 3); the 10% allowance covers genuine cash shortage and the two stove-less households' cold-food friction (B-04)."),
            new SurvivalPassBar("P-3", "Zero missed meals where the larder held a full serving at the slot time.",
                "A missed meal with food present is a defect by the assignment's definition, never hardship. Needs A5."),
            new SurvivalPassBar("P-4", "No household goes 3 consecutive days with 0 meals eaten.",
                "W §17 escalation rule (3 consecutive days -> CRITICAL)."),
            new SurvivalPassBar("P-5", ">= 1 completed purchase-to-receipt trip (S6+S7) per household per 3-day window while a need is open.",
                "The loop's own cadence: DriveEmbodiedShopping plans daily; a need open 3 days with no completed trip is a stuck loop."),
            new SurvivalPassBar("P-6", "Zero trips stuck in Planned > 24 game hours; zero ABORTED trips for 'no physical route'; zero 'counter service expired'.",
                "The 24-hour liveness bound (B-14) and 20-minute patience are the system's own limits."),
            new SurvivalPassBar("P-7", "Household lot units non-decreasing trend over days 11–15 (replenishment keeps pace with consumption).",
                "Baseline shows lots frozen at 80 while 42 meals/day are missed; a sustaining loop must reverse that."),
            new SurvivalPassBar("P-8", "No household's cash falls below zero; no purchase settles for more than the household's balance (or its authorised credit).",
                "Conservation (E C-01/C-02)."),
        };

        // ---- Scenario catalog (§10 list) ----

        /// <summary>All lab scenarios: the survival acceptance test plus the §10 targeted scenarios. Every scenario records seed 1886 and its source revision; none executes a live run here.</summary>
        public static readonly List<LabScenarioDefinition> ScenarioCatalog = new List<LabScenarioDefinition>
        {
            new LabScenarioDefinition("survival-acceptance", "Household survival end-to-end acceptance test (Agent N §2)", 1886, "10x", 15,
                "All eight stages S1–S8 and all eight bars P-1–P-8 pass. The test FAILS if any bar fails; there is no partial pass.",
                "S1 needs A7; S3/S4 need B-01/B-03 seller-location authoring; S8 needs A6/A5; the midpoint needs F-S5/F-S7."),
            new LabScenarioDefinition("food-runout", "Food runout: starting supplies exhaust, replenishment must sustain", 1886, "10x", 15,
                "Days 11–15 average >= 38/42 meals eaten; household lots non-decreasing over days 11–15.",
                "Same prior repairs as the survival acceptance test."),
            new LabScenarioDefinition("unavailable-clerk", "Unavailable clerk: no physically present clerk during shopping hours", 1886, "10x", 15,
                "Counter-service expiries are counted and named; a trip is never recorded served without a completed TendCustomer.",
                string.Empty),
            new LabScenarioDefinition("waiting-customer", "Waiting customer: finite patience window is honoured", 1886, "10x", 15,
                "Zero customers served after their patience window expired; expiries carry the clerk-presence cause.",
                string.Empty),
            new LabScenarioDefinition("insufficient-supplier-stock", "Insufficient supplier stock: seller short, order ends terminal with age and overdue recognition", 1886, "10x", 15,
                "No order ends silently complete when short; every outstanding order carries an age and an overdue recognition.",
                "E R-11 terminal state + age (Lane 9d)."),
            new LabScenarioDefinition("blocked-activity-point", "Blocked activity point: stove / Firewood / chair unreachable", 1886, "10x", 15,
                "Route refusals are grouped by (signature, destination); the refusal names the missing point and the household.",
                "Firewood authoring (B-FIREWOOD); stove-less households Shane Bryan (id 5) and Millward (id 8)."),
            new LabScenarioDefinition("movement-conflicts", "Movement conflicts: two actors, one destination", 1886, "10x", 15,
                "Conflicts resolve as occupied/reserved (class 3) with the holder named, never as a silent stall.",
                string.Empty),
            new LabScenarioDefinition("unaffordable-lumber-transfer", "Unaffordable lumber transfer: yard cannot pay 32c/unit", 1886, "10x", 15,
                "The transfer is refused before custody moves; no lumber moves unpaid.",
                string.Empty),
            new LabScenarioDefinition("lost-settlement-authority", "Lost settlement authority: byproduct settlement fails after custody moved", 1886, "10x", 15,
                "A settlement failure after custody moved logs loudly and the books are reconciled, never silently re-paid (SF-7).",
                string.Empty),
            new LabScenarioDefinition("delayed-freight", "Delayed freight: shipment overdue, cargo preserved", 1886, "10x", 15,
                "Cargo and owner/custodian are preserved; recovery, alternative transport, storage or delivery is attempted; liability follows the agreement.",
                "E R-07 DEF-050/F6-2/F-02 recovery set (Lane 9d)."),
            new LabScenarioDefinition("furniture-placement", "Furniture placement: paid piece installs with a presentation", 1886, "10x", 15,
                "A paid furniture piece installs (furnishing.install outcome wired) and keeps its visual presentation after Continue.",
                "M-R10 furnishing.install outcome wiring (Lane 9f); F-F9 presentation restore."),
            new LabScenarioDefinition("saved-unfinished-order", "Saved unfinished order: in-progress production survives Save/Continue", 1886, "10x", 15,
                "Pre-save vs post-restore equality holds for the unfinished order; consumed inputs are neither lost nor duplicated.",
                "F-S4 production persistence (Lane 9f)."),
            new LabScenarioDefinition("interrupted-production", "Interrupted production: batch interrupted mid-run resumes or terminates loudly", 1886, "10x", 15,
                "An interrupted batch is resumed, re-queued with a named cause, or terminally disposed with its inputs accounted for — never silently dropped.",
                string.Empty),
            new LabScenarioDefinition("stalled-worker", "Stalled worker: no-progress task is named with its blocking cause", 1886, "10x", 15,
                "Zero tasks with 0 worked minutes after 72 simulated hours without a named blocking cause.",
                string.Empty),
            new LabScenarioDefinition("save-continue-during-service", "Save/Continue during service: in-flight trip rehydrates, no duplicate settlement", 1886, "10x", 15,
                "A trip Outbound at save time is Outbound or further along after load, never back to Planned with its legs lost; no receipt id settles twice across the boundary.",
                "F-S5 holder setup; F-S7 runner Continue mode."),
        };

        /// <summary>
        /// Evaluates run evidence against the eight stages and eight bars.
        /// With no live run every stage and bar is BLOCKED and the overall
        /// verdict is NoLiveRun. The test FAILS if any stage or bar fails;
        /// there is no partial pass.
        /// </summary>
        public static SurvivalAcceptanceResult Evaluate(SurvivalRunEvidence evidence)
        {
            if (evidence == null)
            {
                throw new ArgumentNullException("evidence");
            }

            var stages = new Dictionary<string, SurvivalStageVerdict>();
            var bars = new Dictionary<string, SurvivalStageVerdict>();

            if (!evidence.HasLiveRun)
            {
                const string reason = "No live run: no run exists on this HEAD, and the lab cannot execute a live run here (no Unity). The scenario definition and its assertions are wired; the run must come from the Editor/Playtest runner.";
                foreach (SurvivalStageSpec spec in StageSpecs)
                {
                    stages[spec.StageId] = new SurvivalStageVerdict(SurvivalVerdict.Blocked, reason);
                }
                foreach (SurvivalPassBar bar in PassBars)
                {
                    bars[bar.BarId] = new SurvivalStageVerdict(SurvivalVerdict.Blocked, reason);
                }
                return new SurvivalAcceptanceResult(SurvivalOverallVerdict.NoLiveRun, string.Empty, stages, bars,
                    "No live run: the acceptance test cannot be evaluated.");
            }

            // The default catalog entry describes the recommended short
            // observation run. A longer campaign can also supply the
            // acceptance window, provided its identity and New Game setup
            // match and at least the required 15 days actually completed.
            // Speed and full duration remain evidence; they are not replaced
            // with the catalog defaults or grounds for discarding a 250x run.
            if (evidence.Seed != Seed
                || !string.Equals(evidence.Scenario, ScenarioId, StringComparison.Ordinal)
                || !string.Equals(evidence.StartupMode, StartupMode, StringComparison.Ordinal)
                || !IsSupportedSpeed(evidence.Speed)
                || evidence.TargetDays < TargetDays
                || evidence.DaysCompleted < TargetDays)
            {
                string reason = "Run cannot supply the survival window (requires scenario '" + ScenarioId
                    + "', startup '" + StartupMode + "', seed " + Seed + ", a supported recorded speed, and at least "
                    + TargetDays + " completed days): scenario='" + (evidence.Scenario ?? string.Empty)
                    + "', startup='" + (evidence.StartupMode ?? string.Empty) + "', seed=" + evidence.Seed
                    + ", speed='" + (evidence.Speed ?? string.Empty) + "', targetDays=" + evidence.TargetDays
                    + ", completedDays=" + evidence.DaysCompleted + ".";
                foreach (SurvivalStageSpec spec in StageSpecs)
                {
                    stages[spec.StageId] = new SurvivalStageVerdict(SurvivalVerdict.Blocked, reason);
                }
                foreach (SurvivalPassBar bar in PassBars)
                {
                    bars[bar.BarId] = new SurvivalStageVerdict(SurvivalVerdict.Blocked, reason);
                }
                return new SurvivalAcceptanceResult(SurvivalOverallVerdict.NoLiveRun, evidence.RunId, stages, bars,
                    reason, ConfigurationSummary(evidence));
            }

            stages["S1"] = EvaluateStage1(evidence);
            stages["S2"] = EvaluateStage2(evidence);
            stages["S3"] = EvaluateStage3(evidence);
            stages["S4"] = EvaluateStage4(evidence);
            stages["S5"] = EvaluateStage5(evidence);
            stages["S6"] = EvaluateStage6(evidence);
            stages["S7"] = EvaluateStage7(evidence);
            stages["S8"] = EvaluateStage8(evidence);

            bars["P-1"] = EvaluateBarP1(evidence);
            bars["P-2"] = EvaluateBarP2(evidence);
            bars["P-3"] = EvaluateBarP3(evidence);
            bars["P-4"] = EvaluateBarP4(evidence);
            bars["P-5"] = EvaluateBarP5(evidence);
            bars["P-6"] = EvaluateBarP6(evidence);
            bars["P-7"] = EvaluateBarP7(evidence);
            bars["P-8"] = EvaluateBarP8(evidence);

            string firstFailure = string.Empty;
            foreach (SurvivalStageSpec spec in StageSpecs)
            {
                if (stages[spec.StageId].Verdict == SurvivalVerdict.Fail)
                {
                    firstFailure = "Stage " + spec.StageId + " failed: " + stages[spec.StageId].Reason;
                    break;
                }
            }
            if (firstFailure.Length == 0)
            {
                foreach (SurvivalPassBar bar in PassBars)
                {
                    if (bars[bar.BarId].Verdict == SurvivalVerdict.Fail)
                    {
                        firstFailure = "Bar " + bar.BarId + " failed: " + bars[bar.BarId].Reason;
                        break;
                    }
                }
            }

            // A stage or bar still BLOCKED (missing seam) means the test
            // cannot be accepted yet — but a FAILED stage is the verdict
            // that matters first: fail loudly and specifically.
            bool anyFail = firstFailure.Length > 0;
            bool anyBlocked = false;
            foreach (SurvivalStageVerdict verdict in stages.Values)
            {
                if (verdict.Verdict == SurvivalVerdict.Blocked) { anyBlocked = true; }
            }
            foreach (SurvivalStageVerdict verdict in bars.Values)
            {
                if (verdict.Verdict == SurvivalVerdict.Blocked) { anyBlocked = true; }
            }

            SurvivalOverallVerdict overall;
            if (anyFail)
            {
                overall = SurvivalOverallVerdict.Failed;
            }
            else if (anyBlocked)
            {
                overall = SurvivalOverallVerdict.NoLiveRun;
                if (firstFailure.Length == 0)
                {
                    firstFailure = "One or more stages or bars are BLOCKED by a missing seam or prior repair; the test cannot be accepted yet.";
                }
            }
            else
            {
                overall = PassBarsOwnerApproved
                    ? SurvivalOverallVerdict.Accepted
                    : SurvivalOverallVerdict.Provisional;
            }

            string acceptanceNote = overall == SurvivalOverallVerdict.Provisional
                ? "All measured stages and proposed bars pass, but P-1 through P-8 remain owner-unapproved; this is provisional evidence, not an acceptance claim."
                : string.Empty;
            return new SurvivalAcceptanceResult(overall, evidence.RunId, stages, bars,
                firstFailure, ConfigurationSummary(evidence), acceptanceNote);
        }

        private static bool IsSupportedSpeed(string speed)
        {
            if (string.IsNullOrWhiteSpace(speed)) return false;
            string value = speed.Trim();
            if (value.StartsWith("Speed", StringComparison.Ordinal)) value = value.Substring("Speed".Length);
            return value == "1x" || value == "2x" || value == "4x" || value == "5x"
                || value == "6x" || value == "10x" || value == "25x" || value == "50x"
                || value == "100x" || value == "250x" || value == "500x"
                || value == "RealTime" || value == "Observe";
        }

        private static string ConfigurationSummary(SurvivalRunEvidence evidence)
        {
            if (evidence == null) return string.Empty;
            return "scenario='" + (evidence.Scenario ?? string.Empty)
                + "', startup='" + (evidence.StartupMode ?? string.Empty)
                + "', seed=" + evidence.Seed
                + ", speed='" + (evidence.Speed ?? string.Empty)
                + "', completed=" + evidence.DaysCompleted + "/" + evidence.TargetDays + " days";
        }

        private static SurvivalStageVerdict EvaluateStage1(SurvivalRunEvidence evidence)
        {
            if (evidence.Stage1BlockedByA7)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked,
                    "NEEDS A PRIOR REPAIR FIRST (A7): the monitor is fed placeholder zeros, so the affordability branch cannot fire. The test records the need but cannot assert the affordability classification.");
            }
            if (!evidence.NeedsOpenedWithin1Day.HasValue)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Need-opening evidence NOT_CAPTURED.");
            }
            return evidence.NeedsOpenedWithin1Day.Value
                ? new SurvivalStageVerdict(SurvivalVerdict.Pass, "Open needs exist within 1 day for every household below 7 days of cover.")
                : new SurvivalStageVerdict(SurvivalVerdict.Fail, "At least one household below 7 days of cover had no open need within 1 day.");
        }

        private static SurvivalStageVerdict EvaluateStage2(SurvivalRunEvidence evidence)
        {
            if (!evidence.ShoppersChosenSameDay.HasValue)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Shopper-choice evidence NOT_CAPTURED.");
            }
            return evidence.ShoppersChosenSameDay.Value
                ? new SurvivalStageVerdict(SurvivalVerdict.Pass, "A shopper was chosen within the same day for every open need, availability-first.")
                : new SurvivalStageVerdict(SurvivalVerdict.Fail, "At least one open need had no shopper chosen within the same day.");
        }

        private static SurvivalStageVerdict EvaluateStage3(SurvivalRunEvidence evidence)
        {
            if (!evidence.TripsLeftPlannedWithin24h.HasValue)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Trip-departure evidence NOT_CAPTURED.");
            }
            return evidence.TripsLeftPlannedWithin24h.Value
                ? new SurvivalStageVerdict(SurvivalVerdict.Pass, "Every planned trip left Planned within 24 game hours of planning.")
                : new SurvivalStageVerdict(SurvivalVerdict.Fail, "At least one trip sat in Planned for more than 24 game hours (baseline: 17–27 trips/day stuck in Planned with empty failure strings).");
        }

        private static SurvivalStageVerdict EvaluateStage4(SurvivalRunEvidence evidence)
        {
            if (evidence.TripsAbortedNoRoute < 0 || evidence.TripsStuckPlannedOver24h < 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Trip abort / liveness evidence NOT_CAPTURED.");
            }
            if (evidence.TripsAbortedNoRoute > 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Fail, evidence.TripsAbortedNoRoute + " trip(s) ABORTED with 'No physical route reaches seller location' (baseline: 850).");
            }
            if (evidence.TripsStuckPlannedOver24h > 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Fail, evidence.TripsStuckPlannedOver24h + " trip(s) exceeded the 24-hour liveness deadline.");
            }
            return new SurvivalStageVerdict(SurvivalVerdict.Pass, "Zero aborted trips and zero trips past the 24-hour liveness deadline; every departed trip reached AtStore.");
        }

        private static SurvivalStageVerdict EvaluateStage5(SurvivalRunEvidence evidence)
        {
            if (evidence.CounterServiceExpiries < 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Counter-service evidence NOT_CAPTURED.");
            }
            if (evidence.CounterServiceExpiries > 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Fail, evidence.CounterServiceExpiries + " 'counter service expired' warning(s) (baseline: 151).");
            }
            if (!evidence.ServedViaTendCustomer.HasValue)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Counter-service completion evidence NOT_CAPTURED; absence of expiry warnings does not prove a TendCustomer task completed.");
            }
            if (!evidence.ServedViaTendCustomer.Value)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Fail, "At least one trip was served via the compatibility direct-settlement path instead of a completed TendCustomer.");
            }
            return new SurvivalStageVerdict(SurvivalVerdict.Pass, "Zero counter-service expiries; service completed via TendCustomer.");
        }

        private static SurvivalStageVerdict EvaluateStage6(SurvivalRunEvidence evidence)
        {
            if (evidence.SettleLineCount < 0 || evidence.ReceiptIdsMatched < 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Settlement / receipt evidence NOT_CAPTURED.");
            }
            if (evidence.SettleLineCount == 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Fail, "Zero '[FoodEconomy] purchase settled' lines in the run (baseline: zero in both retained runs). The survival loop never completed a purchase.");
            }
            if (evidence.DoubleSettlements > 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Fail, evidence.DoubleSettlements + " receipt id(s) settled twice.");
            }
            if (!evidence.SettlementReceiptsMatch.HasValue)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Settlement amount and trip-to-receipt identity evidence NOT_CAPTURED.");
            }
            if (!evidence.SettlementReceiptsMatch.Value)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Fail, "At least one settled purchase has UnitsSettled == 0 or a Batch.ReceiptId that does not match the settle line.");
            }
            if (evidence.ReceiptIdsMatched != evidence.SettleLineCount)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Fail, "Only " + evidence.ReceiptIdsMatched + " of " + evidence.SettleLineCount + " settle lines have a matching receipt.");
            }
            return new SurvivalStageVerdict(SurvivalVerdict.Pass, evidence.SettleLineCount + " purchase(s) settled exactly once, each with a receipt.");
        }

        private static SurvivalStageVerdict EvaluateStage7(SurvivalRunEvidence evidence)
        {
            // The S7 caveat: State == Done alone does NOT prove receipt.
            if (evidence.ReceiveLineCount < 0 || evidence.ReceiptIdsMatched < 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Receipt evidence NOT_CAPTURED.");
            }
            if (evidence.TripsDoneWithZeroUnits > 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Fail, evidence.TripsDoneWithZeroUnits + " trip(s) reached Done with UnitsAcquired == 0. Done alone does not prove receipt (HouseholdShoppingLoop.cs:3246 sets Done even when the batch is still in custody).");
            }
            if (evidence.ReceiveLineCount == 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Fail, "Zero '[FoodEconomy] purchase received' lines in the run (baseline: zero in both retained runs).");
            }
            if (evidence.ReceiveLineCount != evidence.SettleLineCount)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Fail, "Receive lines (" + evidence.ReceiveLineCount + ") do not match settle lines (" + evidence.SettleLineCount + "); every settled purchase needs exactly one receive line with the same receipt id.");
            }
            if (!evidence.LotDepositedAndNeedDecremented.HasValue)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Household lot identity and need-decrement evidence NOT_CAPTURED.");
            }
            if (!evidence.LotDepositedAndNeedDecremented.Value)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Fail, "At least one received lot was not deposited with the purchased item id, units and seller source label, or the need was not decremented by exactly UnitsAcquired.");
            }
            return new SurvivalStageVerdict(SurvivalVerdict.Pass, evidence.ReceiveLineCount + " purchase(s) received with matching receipt ids and lot deposits.");
        }

        private static SurvivalStageVerdict EvaluateStage8(SurvivalRunEvidence evidence)
        {
            if (evidence.MealsEatenPerDay.Count == 0 || evidence.MealsMissedPerDay.Count == 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Meal evidence NOT_CAPTURED.");
            }
            if (evidence.MealsFromGenericStaple > 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Fail, evidence.MealsFromGenericStaple + " meal(s) served from generic staple_food_unspecified (A8: generic stock must not satisfy a meal).");
            }
            SurvivalStageVerdict bar = EvaluateBarP2(evidence);
            if (bar.Verdict == SurvivalVerdict.Fail)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Fail, "Days 11–15 average below 38/42 meals eaten per day: " + bar.Reason);
            }
            if (!evidence.LotTracingAvailable)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked,
                    "Aggregate meals pass, but lot tracing is unavailable for this run (A6: no LotIdentityResolver wiring was found for the evaluated tree), so no meal can be traced to its received lot. S8 can only assert meals eaten from any identified lot of that item.");
            }
            return new SurvivalStageVerdict(SurvivalVerdict.Pass, "Days 11–15 average >= 38/42 meals eaten; received food was eaten.");
        }

        private static double AverageMealsEaten(SurvivalRunEvidence evidence, int firstDayOneBased, int lastDayOneBased)
        {
            if (evidence.MealsEatenPerDay.Count < lastDayOneBased)
            {
                return -1;
            }
            int total = 0;
            for (int day = firstDayOneBased; day <= lastDayOneBased; day++)
            {
                total += evidence.MealsEatenPerDay[day - 1];
            }
            return (double)total / (lastDayOneBased - firstDayOneBased + 1);
        }

        private static SurvivalStageVerdict EvaluateBarP1(SurvivalRunEvidence evidence)
        {
            if (evidence.MealsEatenPerDay.Count < 7 || evidence.MealsMissedPerDay.Count < 7)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Days 1–7 meal evidence NOT_CAPTURED.");
            }
            for (int day = 0; day < 7; day++)
            {
                if (evidence.MealsEatenPerDay[day] != MealSlotsPerDay || evidence.MealsMissedPerDay[day] != 0)
                {
                    return new SurvivalStageVerdict(SurvivalVerdict.Fail, "Day " + (day + 1) + ": " + evidence.MealsEatenPerDay[day] + " eaten / " + evidence.MealsMissedPerDay[day] + " missed; control period requires 42/42, 0 missed.");
                }
            }
            return new SurvivalStageVerdict(SurvivalVerdict.Pass, "Days 1–7: 42/42 slots eaten every day, 0 missed (control period).");
        }

        private static SurvivalStageVerdict EvaluateBarP2(SurvivalRunEvidence evidence)
        {
            double average = AverageMealsEaten(evidence, 11, 15);
            if (average < 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Days 11–15 meal evidence NOT_CAPTURED.");
            }
            if (average < Days11To15MinimumAverageMeals)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Fail, "Days 11–15 average " + average.ToString("0.0") + "/42 meals eaten per day, below the 38/42 bar (baseline: 0/42 for 51 days).");
            }
            return new SurvivalStageVerdict(SurvivalVerdict.Pass, "Days 11–15 average " + average.ToString("0.0") + "/42 meals eaten per day.");
        }

        private static SurvivalStageVerdict EvaluateBarP3(SurvivalRunEvidence evidence)
        {
            if (evidence.LarderFullMissedMeals < 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Larder-full missed-meal evidence NOT_CAPTURED (needs A5 classification).");
            }
            return evidence.LarderFullMissedMeals == 0
                ? new SurvivalStageVerdict(SurvivalVerdict.Pass, "Zero missed meals where the larder held a full serving at the slot time.")
                : new SurvivalStageVerdict(SurvivalVerdict.Fail, evidence.LarderFullMissedMeals + " missed meal(s) where the larder held a full serving — a defect, never hardship.");
        }

        private static SurvivalStageVerdict EvaluateBarP4(SurvivalRunEvidence evidence)
        {
            if (evidence.MaxConsecutiveZeroMealDays < 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Per-household zero-meal streak evidence NOT_CAPTURED.");
            }
            return evidence.MaxConsecutiveZeroMealDays < 3
                ? new SurvivalStageVerdict(SurvivalVerdict.Pass, "No household went 3 consecutive days with 0 meals eaten (longest streak: " + evidence.MaxConsecutiveZeroMealDays + ").")
                : new SurvivalStageVerdict(SurvivalVerdict.Fail, "A household went " + evidence.MaxConsecutiveZeroMealDays + " consecutive days with 0 meals eaten (W §17: 3 days -> CRITICAL).");
        }

        private static SurvivalStageVerdict EvaluateBarP5(SurvivalRunEvidence evidence)
        {
            if (evidence.MinTripsPerHouseholdPer3Days < 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Per-household trip cadence evidence NOT_CAPTURED.");
            }
            return evidence.MinTripsPerHouseholdPer3Days >= 1
                ? new SurvivalStageVerdict(SurvivalVerdict.Pass, ">= 1 completed purchase-to-receipt trip per household per 3-day window while a need was open.")
                : new SurvivalStageVerdict(SurvivalVerdict.Fail, "A household with an open need completed 0 purchase-to-receipt trips in a 3-day window — a stuck loop.");
        }

        private static SurvivalStageVerdict EvaluateBarP6(SurvivalRunEvidence evidence)
        {
            if (evidence.TripsStuckPlannedOver24h < 0 || evidence.TripsAbortedNoRoute < 0 || evidence.CounterServiceExpiries < 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Trip liveness evidence NOT_CAPTURED.");
            }
            if (evidence.TripsStuckPlannedOver24h > 0 || evidence.TripsAbortedNoRoute > 0 || evidence.CounterServiceExpiries > 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Fail, "Stuck in Planned > 24h: " + evidence.TripsStuckPlannedOver24h
                    + "; ABORTED for no physical route: " + evidence.TripsAbortedNoRoute
                    + "; counter service expired: " + evidence.CounterServiceExpiries + ".");
            }
            return new SurvivalStageVerdict(SurvivalVerdict.Pass, "Zero stuck trips, zero aborts, zero counter-service expiries.");
        }

        private static SurvivalStageVerdict EvaluateBarP7(SurvivalRunEvidence evidence)
        {
            if (evidence.HouseholdLotsPerDay.Count < 15)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Household lot trajectory evidence NOT_CAPTURED.");
            }
            int day11 = evidence.HouseholdLotsPerDay[10];
            int day15 = evidence.HouseholdLotsPerDay[14];
            if (day15 < day11)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Fail, "Household lot units fell from " + day11 + " (day 11) to " + day15 + " (day 15); replenishment is not keeping pace with consumption (baseline: lots frozen at 80 while 42 meals/day were missed).");
            }
            return new SurvivalStageVerdict(SurvivalVerdict.Pass, "Household lot units non-decreasing over days 11–15 (" + day11 + " -> " + day15 + ").");
        }

        private static SurvivalStageVerdict EvaluateBarP8(SurvivalRunEvidence evidence)
        {
            if (evidence.NegativeCashHouseholds < 0 || evidence.DoubleSettlements < 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Blocked, "Cash / settlement conservation evidence NOT_CAPTURED.");
            }
            if (evidence.NegativeCashHouseholds > 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Fail, evidence.NegativeCashHouseholds + " household(s) with cash below zero.");
            }
            if (evidence.DoubleSettlements > 0)
            {
                return new SurvivalStageVerdict(SurvivalVerdict.Fail, evidence.DoubleSettlements + " receipt id(s) settled twice.");
            }
            return new SurvivalStageVerdict(SurvivalVerdict.Pass, "No household cash below zero; no purchase settled twice.");
        }

        /// <summary>Human-readable definition of the scenario: config, stages, bars, and the no-live-run notice.</summary>
        public static string DefinitionMarkdown()
        {
            var lines = new List<string>();
            lines.Add("# Scenario Lab — household survival acceptance test (Agent N §2)");
            lines.Add(string.Empty);
            lines.Add("Default short observation configuration: seed 1886, 15 days, 10x, New Game; longer artifacts for this scenario and seed can be evaluated using the first 15 completed days, with actual speed and duration preserved. "
                + "scene '" + ScenePath + "', scenario '" + ScenarioId + "', 14 residents / 8 households / 7 businesses, "
                + "day length " + DayLengthSeconds + " game seconds, Save/Continue midpoint at end of day " + SaveDayIndex + ".");
            lines.Add(string.Empty);
            lines.Add("The lab cannot execute a live run here (no Unity in this environment). With no run evidence every stage is BLOCKED.");
            lines.Add(string.Empty);
            lines.Add("## Eight stages");
            foreach (SurvivalStageSpec spec in StageSpecs)
            {
                lines.Add("- " + spec.StageId + " " + spec.Title + ": " + spec.Assertion);
                lines.Add("  Evidence: " + spec.EvidenceSource);
                if (!spec.ReadyFromVerifiedSource)
                {
                    lines.Add("  Needs prior repair: " + spec.RequiredPriorRepair);
                }
            }
            lines.Add(string.Empty);
            lines.Add("## Pass bars P-1–P-8 (all PROPOSED — manager to confirm, N-14)");
            foreach (SurvivalPassBar bar in PassBars)
            {
                lines.Add("- " + bar.BarId + " " + bar.Text + " Grounding: " + bar.Grounding);
            }
            lines.Add(string.Empty);
            lines.Add("The test FAILS if any stage or bar fails. There is no partial pass.");
            return string.Join("\n", lines.ToArray());
        }
    }
}
