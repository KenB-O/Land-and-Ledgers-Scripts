# Series 2 Pass 10 — Town Gates + Final Acceptance Harness (review)

Branch: `muse/series2-pass10-harness`, based only on Pass 9
(`muse/series2-pass9-player-save`). LAST editing pass of
Series 2. Roslyn harness only: no Unity compilation, no Test
Runner, no PlayMode, no scene wiring is claimed anywhere in
this pass. Unity verification is Codex/Unity AI work.

## What was built

### A. Town gates, decision-gated only

- NEW `Domains/Population/TownGrowth/Series2SliceTownGates.cs`:
  the slice gate switches as one named object, defaulting to
  current live behaviour: slice arrivals SUPPRESSED, departure
  gate UNDECIDED (departures stay live on the legacy roll),
  legacy roll NOT retired. This class invents no second gate:
  it applies its values to the existing Pass 5 inputs
  (`Pass5TownInputs.SliceSuppressArrivals`,
  `Pass5TownInputs.DepartureGateDecision`) and never calls
  `Pass5TownDriverModel.WithLegacyRollRetired` itself.
- Activation is Kennedy's one line at the driver call site
  (`DepartureGateDecision = ActivateGate`, and, only with a
  separate Kennedy decision, `RetireLegacyRoll = true`).
  `Validate()` refuses a retirement request without a
  departure decision, so a half-applied activation cannot
  pass as a decision. Nothing is applied silently.
- Legacy departures are never rewritten: the harness asserts
  `Pass5DepartureModel.PreserveLegacyDeparture` stays
  `LegacyUntracked` with an empty destination, and the new
  channel refuses an untracked destination.
- Slice-gate assertions: arrivals suppressed even when town
  conditions pass (and allowed when suppression is off,
  proving suppression is the switch); no newcomer settles
  with no housing, no terms, and no explicit
  unemployed-with-plan flag (`Pass5SettlementReadinessModel`
  refuses; the plan flag stays pending, never invented
  employment).

### B. Pass 5 save adoption (one additive field, Pass 9 pattern)

- NEW `Infrastructure/Persistence/Series2Pass10Save.cs`:
  `Series2Pass10SaveDto` + `Series2Pass10SaveState`
  (Capture deep copy, Restore clear-then-load, idempotent:
  restoring twice yields the same lists, never doubles).
  Adopted, all pre-existing Pass 5 model records, nothing
  invented: `Pass5LeadSaveRecord` (FromLead/ToLead),
  `Pass5TravelerVisitRecord` (visitor-only; a visit never
  settles a resident), and `Pass5ChainRecord` — caller-
  supplied chain state only, since Pass 5 has no chain save
  record in any DTO.
- Snapshots: no Pass 5 town-evaluation save record existed
  anywhere in the repo (searched: `Pass5TownEvaluation`
  appears only in the TownGrowth model files), so Pass 10
  adds the shape only: `Series2Pass10TownSnapshotDto`
  mirrors `Pass5TownEvaluation` field for field, carries
  caller-supplied values verbatim, and is null when no
  snapshot is on file. No snapshot value is invented,
  estimated, or defaulted into a real-looking score.
- `PassXlSliceSaveDto` gains ONE additive field,
  `series2Pass10` (null on old saves, which restore as
  empty Pass 10 state, diagnosed). No existing field
  renamed, removed, or reordered. The Pass 9 section is
  untouched.
- `PassXlSliceSaveCoordinator` gains Capture/Restore
  overloads carrying the Pass 10 section when a state is
  supplied; the old overloads are unchanged in behaviour.
  A present section with no state supplied is diagnosed,
  never silently dropped.
- SaveLoadManager wiring for the Pass 10 state is NOT in
  this pass (coordinator overloads only, per the brief):
  Codex must set the Pass 10 state on SaveLoadManager
  before loading, exactly as for Pass 9. Do not claim the
  production save path as Unity-verified: the Pass 9
  SaveLoadManager edit is likewise still Codex/Unity AI
  verification work, not Roslyn-proven Unity behaviour.

### C. 9-person-shape acceptance harness (production drivers only)

- NEW `Tests/Editor/Economy/Series2Pass10HarnessTests.cs`
  (21 tests, all passing in the Roslyn harness). The system
  under test is the Pass 6-9 driver/binding/save classes
  only; `ThreeBusinessSlice` / `SixPersonSliceRun`
  orchestrators are never used. Pass 6-9 suites still pass
  (8 + 16 + 12 + 15); the full Series 2 harness set passes
  72/72.
- Bread chain: flour order/arrival, store-to-bakery feed
  with a caller-supplied 10 lb sack conversion (asserted,
  not defaulted), notions feed, driver plan/bake with
  flour/notions/fuel pre-checks and loud refusals, shelf,
  adapter sale — business cash, household cash, stock, and
  meals inputs reconcile exactly (bakery 1950c + 80c for
  10 fresh loaves at 8c; store ends at 4550c).
- Fuel chain: dealer intake lots, FIFO dispense with
  provenance through `TryDispenseUnits` (shortfall
  dispenses nothing, emptied lots never re-sold), dealer
  lot into the bakery fuel store, a driver-fired batch,
  and separately dealer stock to a household `fuel_wood`
  reserve (caller price 28c, caller conversion factor 2)
  with cash settled through the production port
  (`Series2BusinessRuntimeCashPort`).
- Freight legs: all five slice legs through the Pass 8
  carrier/freight driver (off-map arrival, store-bakery,
  bakery-store wholesale, dealer-bakery, dealer-household),
  each completing through the acceptance callback exactly
  once, each settling exactly once (second settlement
  refused), and all five surviving a mid-run save/restore
  with shipments, cargo, `deliveryApplied`, and
  `freightRevenueCollected` intact and no duplication.
  The household-leg freight payer is a caller parameter
  in the harness; the dealer-borne vs priced-into-retail
  split is still undecided and neither is asserted.
- Mid-run save/restore: Pass 9 state (sequence, missed
  report, duty roster, auto-care, bakery plan, fuel lots,
  freight shipments) plus Pass 10 state (leads, visits,
  chains) restored twice into fresh instances with no
  duplication anywhere. Driver re-supply obligations are
  asserted as required and diagnosed: fuel-source
  rewiring (off-map delegate flagged, never silently
  restored), seller re-supply (a restored AtSeller state
  with no seller fails safely, no free meal), and roster
  re-issue through the Pass 9 binding.
- Documented restore drops, tested as behaviour, not bugs:
  a saved current task that is not an unfinished
  in-progress roster task is dropped (diagnosed, no
  phantom task); saved travel is dropped when a task is
  current (diagnosed, player stays at the saved location,
  never teleported); an out-of-range auto-care state
  restores as Inactive (diagnosed).
- DayIndex: bakery `PlannedDayIndex` restores verbatim
  (Pass 9); the adapter `DayIndex` is deliberately NOT
  restored — a fresh adapter refuses trading until the day
  driver sets it at day start. Early-day runs refuse;
  late-day runs bake as diagnosed catch-up. No
  day-equality is asserted, because the code does not
  enforce it. Pre-check gating of flour, notions, fuel,
  oven, and kit is asserted loudly.
- Missed work visible: missed-work reports via the Pass 2
  API only, console rendering working in the harness;
  on-screen UI/NPC display is Codex work reading that API.
  Idle is standing by, never missed work or hiring
  pressure. Offloaded tasks are never missed work.
- Shane-shaped buying, positive controls: a generic
  single-person test household with no food/fuel and cash
  > 0, driven ONLY by shortage monitor → shopping loop →
  adapter purchase (no direct stock injection, no scripted
  purchase call — bypassing the loop/adapter means no
  purchase and a failing test). Bread: 14 loaves, 112c,
  buyer outflow equals business cash in, inventory gains
  exactly the purchased units, need Fulfilled. Fuel: 7
  units at caller price 28c, 196c through the production
  port. These are SHAPE tests, not Shane Bryan, and not
  the slice fixture.
- Shane-shaped buying, negative controls (both): no cash
  → need stays Open, no purchase, no debt, no free stock;
  cash but no adapter registered → need stays Open, no
  purchase. Both diagnosed, never silently satisfied.
- Shane-shaped hiring, positive control: sustained
  unfinished-work evidence only — three days of
  `LaborDemandAssessor` pressure over a caller-supplied
  threshold (3 days, 200 unmet minutes) — then an effort
  → `HireFromInquiry` → employment with caller-supplied
  terms (5000c/week, Permanent). Negative controls: a
  single missed task and a below-threshold day open
  nothing; all tasks done opens no effort. No headcount
  quota or target is asserted anywhere.
- The missing Pass 3 double-restore assertion is added:
  Pass 3 drafts, placed requests, wholesale settlements,
  and the delivered set now restore twice without
  doubling, and the delivered set still refuses a
  re-intake after the second restore. The "every test
  restores twice" claim may now be repeated for Pass
  2/3/4/6/8.
- Phase H regression: CONFIRMED UNCHANGED, not fixed,
  not claimed fixed. `HouseholdVerticalSlicePhaseHTests`
  runs 12 passed, 3 failed with the same three failures
  and messages as baseline: `EndToEndTrace_InsufficientFunds_FailsThenRecovers`
  (Expected true), `Secondary_Hardship_MissedMeals_NoInvisibleReplenishment`
  (Expected string containing 'unaffordable', got ''),
  `Secondary_Restock_ProcurementChainRefillsThenSaleSucceeds`
  (Expected 60, got 59). The `ConstructionInputSupportTests`
  1,800-second Unity timeout remains a Codex diagnosis
  item, not a Pass 10 claim.
- SaveLoadManager null freight hooks: shipments, cargo,
  `deliveryApplied`, and `freightRevenueCollected`
  round-trip in the DTO itself, and reservation rebinding
  is skipped with null hooks (task bindings re-resolve by
  definition id on the next tick). This pass does NOT
  claim the null-hooks path rebinds reservations.

## What was deliberately NOT built

- The NAMED expanded fixture (Ethan B., Tia B., Shane
  Bryan) is BLOCKED, not built. No full surnames, ages,
  business name, houses, horses/wagons, or Shane cash
  amount have arrived, and inventing any of them would
  violate the no-fake-persons rule. The harness runs on
  expanded-shape test inputs only, clearly labelled as
  shape tests, never named Ethan/Tia/Shane and never
  presented as the slice fixture. `SixPersonDevelopmentFixture`
  and its `.asset` are untouched (its Validate requires
  exactly 6 persons, 3 households, 2 each).
- No new `ExpandedSliceFixture`: only if Kennedy inputs
  arrive in a later brief.
- No legacy departure destinations rewritten; no friend
  graph touched (no friend edges generated without
  `SharedHistoryEvidence`); no bootstrapper/Main Scene/
  prefab/Inspector values touched; no Kennedy decision
  applied; no item-level stock migration
  (`BusinessRuntimeState` stays per-category, not
  per-item); no Pass 6-9 internals edited (public
  callables only, plus the additive Pass 10 save plumbing);
  no `.asset` file touched; no price, rate, weight,
  frequency, or duration fixed as canon (all remain
  caller/test parameters).
- No Unity compilation, Test Runner, PlayMode, or scene
  wiring claimed. New-file GUIDs are fresh and each
  appears exactly once in the repo; a full repo-wide
  duplicate scan was not run (prior audits note it stalls
  on the large repo) — Unity import remains the final
  GUID check.

## Inputs still required before ANY fixture authoring

Ethan full surname (confirm whether "B." is Boniecki),
age, freight business name, house/building, horses/wagons
counts, starting cash/stock, whether he starts with
wagons/horses or must buy them, freight charge/pricing
model (or confirm it stays a caller parameter); Tia full
name, age, role, household relationship to Ethan; Shane
age, house/building id and tenure, exact starting cash
amount, anything besides cash he starts with; general
slice inputs: seed for the new households/businesses,
which freight legs run first, who pays freight on each
leg (including the household leg).

## What Codex must hook up (Unity side, not claimed here)

1. SaveLoadManager: set the Pass 10 state before loading
   (and the Pass 9 state, per Pass 9), exactly as for
   procurement; verify the Pass 9 SaveLoadManager edit in
   Unity — it is not Roslyn-proven Unity behaviour.
2. Day driver: set each adapter `DayIndex` at day start
   (bakery adapter refuses trading until set); re-issue
   the duty roster, re-supply sellers/lodging for resumed
   auto-care, and rewire the dealer fuel source from
   `RestoredFuelSourceParams` (including the off-map
   receipt delegate) after every load.
3. On-screen UI/NPC display of missed work, reading the
   Pass 2 missed-work API (console rendering already
   works in the harness).
4. The named expanded fixture as `.asset`/scene work
   once Kennedy inputs arrive — never from invented data.
5. Town-gate activation wiring, ONLY on Kennedy's one
   line: `DepartureGateDecision = ActivateGate`, and
   legacy-roll retirement only with his separate decision.
   Until then, arrivals stay suppressed in the slice and
   departures stay live on the legacy roll.
6. `ConstructionInputSupportTests` Unity timeout diagnosis
   (1,800 seconds, pre-existing).
7. Unity compilation, Test Runner (EditMode), PlayMode,
   and scene wiring for everything in this pass, plus the
   Unity import GUID check.
