# Review 2 Final Verification Cleanup Pass — Pull-Ready Handoff

This is the LAST Muse pass in the Review 2 pipeline. There is no next Muse
pass. The branch this document sits on is the pull target.

- **Pull target branch:** `muse/review2-final-cleanup`
- **Parent tip:** `065ceb2a0f4aae2cef51a007d8c91165db345b0c`
  (`muse/review2-second-scriptableobjects`)
- **This pass, scope:** verification, cleanup, and handoff ONLY. No new
  features. No production behaviour change. The only files added by this
  pass are this document and its `.meta`. No cleanup fix was needed: every
  verification below passed, every headline claim spot-checked matched the
  code at this tip, and no stale TODO or wrong doc claim was found that
  required a correction. That negative result is itself reported here so it
  is not mistaken for a skipped check.

## 1. The complete chain — pull ONLY the final branch

Every link below was verified by the parent via GitHub `ls-remote` before
and after this pass, and by ancestry checks in this pass (each tip is a
descendant of the previous tip). Main and integration are unchanged at
every step of the chain.

| Order | Branch | Tip SHA | Parent |
|---|---|---|---|
| Base | `muse/series2-pass11-final-review` (Pass 11) | `34ee209e27c56c75ba7fd813f04f91aa9d4ef0b2` | Pass 10 `220583ba` |
| 1 | `muse/review2-passA-general` (Pass A) | `40c31331eb21308d9a7f965aa44af7d9c3fe7bfc` | Pass 11 |
| 2 | `muse/review2-passB-scriptableobjects` (Pass B) | `84d1d1cd4cde5f8d336497887fbf99cdafbfd037` | Pass A |
| 3 | `muse/review2-passC-scripts` (Pass C) | `b0fa28bc61b2ba905765663e4694f408e1be4feb` | Pass B |
| 4 | `muse/review2-vertical-D1` (D1) | `4da60eac9de45d8542bd43f60c961c142a3305f6` | Pass C |
| 5 | `muse/review2-vertical-D2` (D2) | `3c8f97a921476afc5619b2ae6f56f2acd6f6aae7` | D1 |
| 6 | `muse/review2-vertical-D3` (D3) | `851bbcf3ec6d48ca454e2b4e2bf4865ab800f0cf` | D2 |
| 7 | `muse/review2-second-scripts` (second scripts) | `83e6344ef2c1cfd52365f7e105014385752048b7` | D3 |
| 8 | `muse/review2-second-scriptableobjects` (second SO) | `065ceb2a0f4aae2cef51a007d8c91165db345b0c` | second scripts |
| 9 | `muse/review2-final-cleanup` (THIS BRANCH) | (parent-verified on push; see pull instructions) | second SO |

Per-link diff sizes (shortstat, parent tip to branch tip):

- Pass 11 to Pass A: 5 files, +639.
- Pass A to Pass B: 7 files, +604/−5.
- Pass B to Pass C: 11 files, +1,227.
- Pass C to D1: 7 files, +1,132 (code commit plus the horse-feed
  decision-record doc commit).
- D1 to D2: 12 files, +1,112/−1.
- D2 to D3: 10 files, +1,438/−2.
- D3 to second scripts: 10 files, +1,201/−1.
- Second scripts to second SO: 8 files, +615/−1.
- Second SO to final cleanup: 2 files (this document + its `.meta`).

## 2. What each pass did — one paragraph each

- **Pass 11 (Series 2 final review):** the Series 1 + Series 2 pull target
  before Review 2. Seven files, +657/−7, no `.asset`, bootstrapper, or
  scene changes, no canon or price or rate changes. Its substantive fix was
  carrying the Pass 10 save section in `SaveLoadManager` — Pass 10 had
  shipped coordinator overloads but no `SaveLoadManager` property, so the
  production save path could never carry it — plus two review-doc claim
  corrections. Codex still must assign the live state before first
  save/load, plus Unity compilation. Agent-reported Roslyn at the time:
  full Series 2 set 74 passed, 0 failed.
- **Pass A (general):** decision-free test pins and one doc clarification,
  no production behaviour change, no `.asset` touched, no fixture authored.
  Eight new tests pinning documented caveats both ways: retail
  WARNING-only cash (a refusing cash port leaves a bakery sale and a fuel
  dispense standing with stock moved and no cash posted, diagnosed; through
  the production cash port the same sales post exactly, bakery 5 × 8c and
  fuel 5 × 28c, test parameters); bakery `DayIndex` gating precisely (the
  adapter ignores its `absoluteDayIndex` argument and refuses only while
  its own `DayIndex` is unset — day equality is NOT enforced); dealer fuel
  partial supply (a refused or under-delivering off-map callback leaves
  dealer units already in the bakery store — fuel not lost, but NOT a clean
  rollback, so a production driver must pre-check first or explicitly
  accept partial-store state).
- **Pass B (ScriptableObjects):** no C# production file changed, no asset
  deleted, no new asset created. Removed two dangling category labels
  (Blacksmith `repair_service`; General Store `freight_goods` in BOTH the
  profile and the definition — NOT replaced with stock categories, which
  would change weekly procurement); renamed the misnamed civic asset to
  `LargeLivery.asset` via `git mv` with its `.meta` GUID unchanged; and
  wrote a full 63-asset inventory recording every asset's path, backing
  type, GUID, runtime consumer where known, purpose, key fields,
  production-loading status or Main Scene/Inspector BLOCKED status, and
  why each was left alone. The renamed LargeLivery shell still cannot host
  a workplace (`workplaceEligible: 0`) — hosting is a gameplay decision.
- **Pass C (scripts):** a caller-parameterised item/SKU map
  (`Review2ItemSkuMap`) across Store, BakeryCode, BakeryProfile, FuelProfile,
  Household, and RestaurantCode spaces — no id renamed, no price merged, no
  conversion canonised, existing adapters NOT rewired; packaging factors
  default to 0 = not supplied = refusal with a diagnostic naming the
  factor, never a silent 1. The General Store weekly input path was
  addressed explicitly: empty `inputCategoryIds` after Pass B is correct
  today (the store restocks via its weekly payroll-and-reorder path plus
  local-supply transfers, not the weekly converter). The ONE production
  change was the weekly converter guard in
  `SharedBusinessRuntimeManager.ResolveBusinessOperation`: empty input
  category + non-empty output category now blocks with "no input category:
  weekly conversion cannot mint output from nothing", producing 0 and
  returning before any stock is credited — production-identical today (the
  store never reaches the converter) but closing a latent mint of up to 36
  staple_food units/week. Plus a pure-diagnostic driver-binding checklist
  (10 ordered binding steps) and 16 new Roslyn tests, plus a Unity-only
  converter test for Codex Test Runner work.
- **D1 (vertical slice):** a production driver chaining the oldest sellable
  bakery shelf lot through wholesale intake into BACK, wholesale settlement,
  and the shelf move, plus a stock-to-live move, with one additive
  production method (`Pass3ProductLine.ConsumeShelfUnits`) that stops the
  Pass 3 shelf and the live pool double-counting the same loaves. The A-to-Z
  trace as run in the harness, with exact reconciliation (caller parameters,
  never canon: bake day 2, transfer day 2, 24 loaves/batch, wholesale 2c,
  retail 5c, household 10,000c, store 5,000c, bakery 2,000c): bake 1 batch
  day 2 to shelf 24; wholesale transfer to shelf 0 with store 5,000 → 4,952
  and bakery 2,000 → 2,048 (24 × 2c = 48c); stock live to pool 24 with no cash
  move; shopping loop buys 14 at 5c (household 10,000 → 9,930, store
  4,952 → 5,022, pool 24 → 10, buyer outflow 70c = business cash in 70c);
  morning + evening meals served 2, missed 0, inventory 14 → 12; nutrition
  0.70 → 1.00. Refusal modes tested: stale lot, unmapped rolls, retail
  price equal to wholesale cost, payer cash below wholesale, settlement
  failure after intake (stands in BACK, recoverable), capped live pool
  (refuses whole, never partial). Kennedy's horse-feed decision arrived
  after D1's code was committed, so D1 recorded it verbatim in its review
  doc and handed implementation to D2 — it did not rework the pass.
- **D2 (vertical slice):** the now-decided horse-feed source, implemented.
  Feed is a player-addable General Store item: by default the store sells
  no feed; an owner adds it via an operation. `AddFeedItem` creates the
  category entry at 0 current stock only if absent, never creates stock,
  never sets a price, is idempotent on identical re-add, refuses a different
  item for the same store, and refuses a conversion of 0 or less (0 means
  "not supplied", never a silent 1:1). Consumption is all-or-nothing at
  store-unit level. Driver wiring adds a store feed registry, a register
  operation refusing non-General-Store businesses and missing runtimes, and
  a third branch in `ResolveHorseFeedStock` after farm and livery; farm and
  livery branches are untouched, and a store with no added item still falls
  through to null with a diagnostic. The person-owned leg runs over
  household inventory (oldest lots first, caller-supplied conversion) with
  `HouseholdInventory` edited additively only (runtime-item registration
  refusing to shadow built-in ids). All feed ids, prices, and conversions
  are caller-supplied test parameters, never canon. Eight new tests,
  including the full person leg: 6 sacks bought through the normal shopping
  loop at a caller-supplied 9c (54c, buyer outflow = store cash in, live
  pool 50 → 44), then CareDay draws 2 units and the horse does not degrade.
- **D3 (vertical slice):** save adoption for D2's in-memory-only
  registrations, in one additive section only, clear-then-load, idempotent.
  A mid-run test proves a store feed item and a runtime item survive restore
  and still feed (store horse 20 → 16 units on the idle ration; person horse
  6 → 4 inventory units); an old save without the section restores empty
  with a diagnostic, never phantom feed; invalid entries are skipped with
  diagnostics; conflicting catalog definitions are diagnosed with the
  existing definition winning. Persisted values are exactly the
  caller-supplied registration values, verbatim. Plus fuel, freight, and
  hiring traces: fuel 20 = 6 to bakery + 4 sold at caller 28c (+112c exactly)
  + 10 remaining; freight carrier leg 10 units / 250c accepted exactly once
  with a restored settlement refusing the same leg via the shipment's
  `freightRevenueCollected` flag; hiring proved to stop at manager intent
  (intent alone creates 0 efforts and 0 employment relationships; an
  explicitly opened handbill effort at 150c still hires nobody — no named
  person hired, no fake employment).
- **Second scripts:** A-to-Z bottleneck tracing with real changes. Freight:
  the detailed path and the Series 2 carrier operate on the SAME
  `LogisticsShipmentState` object, and both mechanisms check the same
  `freightRevenueCollected` flag — proved in both orders on one completed
  shipment (250c, payer 1,000c → 750c, carrier 0c → 250c, the other mechanism
  posting 0 or refused); the timer-driven `LogisticsRuntimeManager` path
  holds its own shipment list and must never be wired to the same slice
  legs, and cross-instance copies are NOT guarded (the guard lives on the
  object), so detailed-only wiring with one driver instance per carrier
  business remains a Codex wiring rule. D3 save wiring: a one-line-style
  bootstrap bind (`Review2SecondScriptsSaveWiring.BindDriverRegistry`)
  joining the driver's live feed registry to the D3 save state by shared
  instance, idempotent on re-bind, with diagnostics — but the bind call site
  is MonoBehaviour code and is NOT Roslyn-tested, and no production code
  assigns `Series2Pass9State`, `Series2Pass10State`, or
  `Review2VerticalD3SaveState` anywhere (verified by grep), so those
  assignments remain Codex work. Partial-fill attribution: the shopping
  result object now carries the reason a need stays open after a partial
  fill. Hiring: full chain with negative controls at each step. Catalog
  conflict: metadata only, never feeding math. Phase H's 3 failures were
  investigated and identified as a policy fork, NOT fixed here (see
  section 4).
- **Second ScriptableObjects:** exactly two changes. The one misleading
  asset name left from Pass B was renamed with its GUID unchanged: the
  " 1" town variant (`DefaultTownGenerationSettings 1.asset`, GUID
  `3c5986b4d8fe3e74189dbfcbddf4b6a8`) became
  `RegionalFoundationMineralTownGenerationSettings.asset` via `git mv`,
  every other field unchanged (seed 2147483647, grid 220×200, regional
  foundation fields, mineral districts) — a distinct variant, NOT deleted,
  and no code loads it by path or references its GUID. And a diagnostic-only
  helper (`Review2StoreOfferGapCheck`) with 5 tests making the headline
  asset/runtime disagreement observable: a runtime-added store feed item is
  feedable to store horses but unsellable to person-owned horses through
  the production port, because the production port emits offers only for
  profile/definition categories and a runtime-added item has no offer and
  no owner-set price home. That production fix is a Kennedy/Codex design
  decision (see section 4); no asset edit could fix it without either
  adding a default feed item (forbidden — feed is player-addable, never
  default) or choosing which authority wins (forbidden). Every other slice
  asset was re-checked and left alone for a stated reason, including the
  still-identical General Store profile and definition. A follow-up commit
  corrected GUID wording in this pass's own review doc; that wording
  correction is the only doc correction in the second SO pass, and this
  final pass found no further doc claim requiring correction.
- **Final cleanup (this pass):** re-ran everything from freshly built
  binaries at this tip (section 3), re-verified the chain, GUIDs, category
  labels, fixture, and asset inventory (section 5), and wrote this handoff.
  No production change, no asset change, no test change, no decision taken.

## 3. Verification at this tip — freshly built binaries, Roslyn only

Harness projects were retargeted from the second-SO harness to this
worktree (`harness-FC.csproj`, `harness-FC-phaseh.csproj`, in the
roslyn-harness folder, outside the repo) and built with the local .NET 8
SDK. Both builds: 0 errors (warnings only, pre-existing unused-field and
unused-variable warnings). No test was weakened, edited, or removed to
reach these counts.

### Main harness — RESULT: 136 passed, 0 failed

| Suite | Passed |
|---|---|
| Series 2 Pass 6 bread drivers | 8 |
| Series 2 Pass 7 selling | 16 |
| Series 2 Pass 8 freight/fuel | 12 |
| Series 2 Pass 9 player/save | 15 |
| Series 2 Pass 10 harness | 21 |
| Series 2 Pass 11 final review | 2 |
| Series 2 subtotal | 74 |
| Review 2 Pass A general | 8 |
| Review 2 Pass C scripts | 16 |
| Review 2 vertical D1 | 9 |
| Review 2 vertical D2 | 8 |
| Review 2 vertical D3 | 8 |
| Review 2 second scripts | 8 |
| Review 2 second ScriptableObjects | 5 |
| Review 2 subtotal | 62 |
| **Total** | **136** |

Arithmetic check: D3 tip 123 + second scripts 8 + second SO 5 = 136. No
failures. A separate shopping-loop test harness does not exist in the
final-cleanup harness project file list (the shopping-loop production code
is compiled in, but its legacy test fixture is not listed); shopping-loop
behaviour at this tip is therefore evidenced by the D1/D2 traces and the
Phase H proofs, not by a standalone shopping-loop suite run — do not quote
one.

### Phase H harness — RESULT: 94 passed, 3 failed (baseline, unchanged)

Phase H harness composition at this tip: Series 2 Pass 6 (8), Pass 7 (16),
Pass 8 (12), Pass 9 (15), Pass 10 (21), Pass 11 (2), second scripts (8),
plus HouseholdVerticalSlicePhaseH (12 passed, 3 failed) = 94 passed, 3
failed. The 3 failures are exactly the pre-existing baseline failures,
unchanged by every pass in this chain, and now understood as a policy fork
(section 4), never as new failures:

1. `EndToEndTrace_InsufficientFunds_FailsThenRecovers` — Expected true.
2. `Secondary_Hardship_MissedMeals_NoInvisibleReplenishment` — Expected
   string containing 'unaffordable', got ''.
3. `Secondary_Restock_ProcurementChainRefillsThenSaleSucceeds` — Expected
   60, got 59.

`ConstructionInputSupportTests` was NOT re-run, per standing instruction:
it timed out at 1,800 seconds in Unity at Pass XL with no verdict, and must
be narrowed first by Codex, never re-run unchanged.

## 4. Open decisions — in blocking order, none resolved here

1. **Phase H policy fork (Kennedy / development manager decision).** Root
   cause proved by the second scripts pass: `HouseholdShoppingLoop`
   deliberately downsizes a cash-short need — when the ledger covers at
   least one unit's price but not the full need, the household buys what it
   can afford (a smaller real trip, never debt) and the trip reports
   Success = true with the need left open. The 3 Phase H tests encode the
   opposite policy (cash-short household buys nothing; trip fails with
   "unaffordable"; balance and stock unchanged). No Canon text mandates
   all-or-nothing needs, and the downsizing branch is blessed by two
   passing unit tests. Resolutions, with costs: (a) keep downsizing,
   recommended on cost — update the 3 Phase H tests to downsizing reality
   (attempt buys 1u for 120c, balance 80c, flour 1u, stock 49u, need open
   with 9 remaining, PartialFillReason containing "affordability";
   recovery buys 9u for 1080c; Hardship asserts the downsizing attribution;
   Restock asserts StockBefore 59 with the cascade note), no production
   change; or (b) all-or-nothing needs — remove or gate the downsizing
   branch so a cash-short need fails with "unaffordable" and buys nothing,
   then update the partial-carry expectation that blesses partial fills.
   Resolution (b) changes live shopping behaviour for every cash-short
   household and needs Kennedy's explicit decision. Until one is picked,
   Phase H stays at 3 failures — reported as the fork, never as fixed, and
   never fixed by weakening tests or removing downsizing in this pass.
2. **Production feed-offer fix (Codex, Kennedy design decision).** A
   runtime-offer path in or alongside `BuildHouseholdTradingPort` plus an
   owner-set price for runtime-added items, with a decision on where that
   price lives (registration, offer, or profile override). Until then, the
   second-SO gap diagnostic is the honest signal, and the D2 person-leg
   proof must not be quoted as a production-port proof. Also verify the
   store-owned leg in-game: registration call site, per-day household
   adapter construction, and CareDay wiring after a load.
3. **General Store dual authority (Kennedy decision).** Profile and
   definition are still identical after stripping script/name/class
   (both `startingCashCents: 45000`, markup 1.4, reserve 8000c, same
   categories and starting stocks flour 48 / bread 24), and
   `FirstLedger.asset` separately holds `startingCashCents: 250000`, a
   third cash figure with an undocumented relationship to business opening
   cash. Confirm which asset the Main Scene's store manager points to
   before deleting either, and decide how the fixture targets (General
   Store $250) and preserved player funds reconcile exactly once — never by
   double-counting. The definition's site requirements (minimum 5×5 cells,
   frontage 5, buildable 25, road frontage required) also tension with
   generic `NoDedicatedPremises` formation; precedence is undecided.
4. **Production binding (Codex, in order in section 6).** The one-line
   save-wiring bind, Pass 9/10/D3 state assignments, Pass 6 driver
   invocation, adapter registration, daily DayIndex, F19 procurement
   registration, and wholesale intake → settle → retail-price decision →
   back-to-shelf task → shelf sale wiring. Nothing in this chain binds any
   of it in production; all of it is callables, models, and tests.
5. **Nine-person fixture inputs (Kennedy inputs, never invented).** Still
   ungiven: Ethan's age; Tia's age and role (works in the freight company,
   stays home, or another business — do not assume the stay-home pattern);
   freight business name / instance id (confirm LiveryFreight or a new
   name); house/building ids or descriptions for Ethan/Tia and for Shane's
   extra house, and its tenure and location; starting horses (count; horse
   and wagon remain distinct owned assets), starting wagons/carts (count,
   capacity), whether Ethan starts with wagons/horses or must buy them;
   starting stock for the freight business if any (profile 15000c + 18
   upkeep lots + 2 repair lots are test parameters, not a seed decision);
   Shane's age, anything besides cash (tools, stock, or nothing — Kennedy
   named only no food, no fuel), labour-access/skill starting point, and
   job-seeking details. Slice inputs still open: seed rule for the new
   households/businesses beyond the cash targets, which freight legs run
   first, who pays freight on each leg, and slice start season/day (matters
   for Shane's fuel need). Given and NOT re-asked: nine residents, five
   households, four businesses; Glen and Tannis Pierce (Tannis replaces
   Cassidy); Jake and Grace Boniecki; Ethan and Tia Boniecki, Ethan and
   Jake brothers, Ethan and Tia spouses; Shane Bryan unemployed with
   greater household savings; development-fixture cash targets Pierce $65,
   Jake/Grace $75, Ethan/Tia $60, Shane $125, player funds preserved and
   reconciled, General Store $250, Bakery $175, Fuel Dealer $200, Freight
   Company $100; NPC household members may access shared household funds;
   the player household needs a spending-permission setting (no such
   system exists — `HouseholdLedger.RecordOutflow` has no permission
   parameter).
6. **Feed-need origination.** Who opens a feed need, at what quantity — the
   D2 person-horse test constructs its need by the caller. Undecided.
7. **Horse mortality rule and work-eligibility threshold.** Prolonged-
   deprivation condition and death disposition (no arbitrary single-day
   threshold, per Kennedy's direction), and any nutrition/fatigue threshold
   for horse work. Undecided.
8. **Item-level stock migration.** Per-item offers over a shared
   per-category pool are code (`BusinessRuntimeState` holds one aggregate
   count per category, not per item); per-item stock counts overstate each
   item alone, though total sold across items can never exceed the pool.
   Item-level stock is a save-shape migration and a Kennedy/Codex decision.
9. **Flour/fuel unit semantics as canon, and item/SKU mapping as canon.**
   Store SKU units vs bakery lb, intake-to-bakery conversion factor, and
   fuel bundle-to-reserve conversion factor are all caller parameters in
   code and tests (Pass C map, Pass 7 default 1=1 is a parameter, NOT a
   rate). The conversion decision must be made before any production driver
   run. Notions supplier identity and price, and notions ownership (store
   service vs bakery-owned), are likewise open. Fuel Dealer off-map input
   identity (logs, slabs, cordwood, firewood, or other) is open. Sawmill
   byproduct price (if any) is open — Pass 8 receipt pays the sawmill
   nothing and diagnoses that plainly, which is not a zero price.
10. **Freight payer per leg and commission.** Household-leg payer defaults
    to the dealer in slice-leg code, but a household cannot pay freight
    through business ports, so dealer-borne vs priced into the retail fuel
    price must be decided before any harness asserts household-leg cash.
    Carrier-leg decisions are open: off-map leg split, store→bakery and
    bakery→store via carrier vs Pass 6 instant feed until the carrier
    bridge is wired, household fuel via shop-at-dealer vs dealer delivery,
    household purchases via shopper travel vs freight delivery, and
    commission (profile `freight_service_visit` 200c is a test parameter).
11. **LargeLivery hosting.** The renamed shell has `workplaceEligible: 0`
    with all suitability rows 0 including type 9 — it cannot host a
    workplace as authored. Hosting is a gameplay decision.
12. **Calendar start year.** FirstLedger 1870 vs TimeSettings 1886 vs 1880
    anchor vs scenario clamping. Undecided. Empty founder names in
    `FirstLedger.asset` are Kennedy's in-editor step.
13. **Profile-less business types.** Tannery 19, PostOffice 20, Newspaper
    21, Restaurant 22, Hotel 23, Logging 24, GrainElevator 25, Bank 26,
    Lawyer 27, plus Generic 255 — author or explicitly rule out.
14. **Seconds granularity; meal/sleep durations; work-time cap.**
    Seconds are representable in newer layers but not usable end-to-end as
    tasks in `TaskAuthority` (minute fields, drivers flooring sub-60-second
    ticks to zero progress). Meal code 30 min vs Kennedy example 15 min;
    sleep code 22:00–07:00 = 9 h vs Kennedy guess 6–8 h; life-task driver
    (day-boundary meal consumption vs Pass 2 life tasks, no double-eating;
    acting-adult gate for lot meals proved and unaddressed). Work-time cap
    600 min default vs pure task sequencing (Bakery 840, Farm 480) — all
    still Kennedy decisions.
15. **Departure gate; the 4% arrival roll; departure unification;
    double-ordering guard; friend graph.** Departure gate activation is a
    one-line decision, still open. Retiring the old 4% arrival roll
    (`MigrationDriver.MonthlyArrivalProbability = 0.04`) is open. Past
    departures must NEVER be rewritten with an invented destination.
    Double-ordering guard (F26) — no guard found, and whether it is required
    before the nine-person slice is an open manager question. No friend
    graph built; family-only vs friend graph is open.
16. **All prices, rates, weights, frequencies, and thresholds.** Every such
    value in every pass and audit is a test parameter or calibration, NEVER
    canon. This includes bread batch parameters, crew scaling, reputation
    weights, immigration thresholds, traveler frequency, letter reply
    rates, experience formation rate, roles and need-measurement thresholds
    (never a headcount quota), and heating (all open: coal as one item or
    three, unit weights/heat, Dealer sells coal vs Mine sells direct, house
    variation driver, bakery oven fuel same as household fuel — today NOT —
    heating vs food priority, starting stove/fireplace/nothing; current
    code heats with `fuel_wood` only, `HouseholdFuelType` has exactly one
    value, Wood, and no stove/fireplace/hearth/chimney exists as an owned
    object).

## 5. What this pass checked and left alone

- **Chain integrity:** every branch in section 1 exists at its expected SHA
  in GitHub `ls-remote`; each tip descends from the previous tip (ancestry
  checks passed for D3 → second scripts → second SO, and the full link
  list matches the parent's ls-remote verification). Protected branches
  (main, integration, Pass 11, Pass A, Pass B, Pass C, D1, D2, D3, second
  scripts, second SO) are unchanged by this pass — re-verified after push
  (section 7).
- **Headline claims spot-checked against code at this tip, all matched:**
  D1 bakery-to-meal reconciliation (D1 suite 9/9 in the fresh run, with the
  exact cash/stock figures in section 2); D2 feed legs (D2 suite 8/8;
  `Review2StoreHorseFeed`, `StoreHorseFeedStockAdapter`, and
  `Review2HouseholdHorseFeed` present with the all-or-nothing and
  zero-means-not-supplied rules intact); D3 save adoption (D3 suite 8/8;
  `Review2VerticalD3Save` section present, additive, with the old-save
  empty-with-diagnostic behaviour); second-scripts save wiring bridge
  (`Review2SecondScriptsSaveWiring.BindDriverRegistry`, idempotent,
  diagnostics, no production assignment of the D3/Pass 9/Pass 10 states —
  still a Codex step) and freight guard (same-object
  `freightRevenueCollected` flag checked by both settlement and driver
  scan, proved in both orders; timer path and cross-instance copies
  explicitly NOT guarded, as documented); second-SO town rename
  (`RegionalFoundationMineralTownGenerationSettings.asset`, GUID
  `3c5986b4d8fe3e74189dbfcbddf4b6a8` unchanged, no code loading it by
  path or referencing its GUID) and feed-offer gap diagnostic
  (`Review2StoreOfferGapCheck`, diagnostic only, no behaviour change).
- **GUIDs:** all 2,079 `.meta` files in the tree at this tip — zero
  duplicate GUIDs. All 27 new `.meta` files added across the Review 2
  chain (Pass A through second SO) have GUIDs that are mutually distinct
  and appear exactly once each in the tracked tree (verified via
  `git grep` per GUID). This pass's own new `.meta` GUID
  (`fd96a44ccc92489fa900e6827a5ad3f9`) was checked collision-free before
  commit. Unity import remains the final GUID check — NOT claimed here.
- **Category labels:** zero dangling category entries in the sense Pass B
  fixed them — General Store profile and definition both carry
  `inputCategoryIds: []` and `serviceCategoryIds: []`, Blacksmith carries
  `serviceCategoryIds: []`; remaining profile labels are their own defined
  categories (e.g. Barber `barber_consumables`, FuelDealer `slabs_offcuts`,
  LiveryFreight `livery_feed_upkeep` + `wheelwright_repairs`). No label
  was added or removed in this pass.
- **Assets:** 63 `.asset` files at Pass 11, 63 at this tip. Chain diff for
  `*.asset`: 3 modifications (GeneralStoreBusinessDefinition,
  BlacksmithProfile, GeneralStoreProfile — the Pass B label removals) and 2
  renames (SingleStoryCivicNaturalWood 1 → LargeLivery, Pass B;
  DefaultTownGenerationSettings 1 →
  RegionalFoundationMineralTownGenerationSettings, second SO). Zero
  deletions. No fixture data authored anywhere in the chain: the only
  Ethan/Tia/Shane mentions in the chain diff are "no fixture data"
  disclaimers in code comments.
- **Six-person fixture and validator:** `SixPersonDevelopmentFixture.asset`
  and `SixPersonDevelopmentFixture.cs` (which carries `Validate()`) are
  byte-identical between Pass 11 and this tip — `git diff` empty for both
  paths. The fixture still names Cassidy; no Tannis/Ethan/Tia/Shane data
  was authored. The nine-person fixture is Codex work after Kennedy's
  inputs (section 4, item 5), built as an extension, not a replacement,
  keeping the six-person fixture as regression.
- **Docs:** no TODO/FIXME markers in any Review 2 review doc; no stale TODO
  that is actually done was found; no doc claim in the chain was found to
  mismatch the code at this tip, so no correction beyond this document was
  made. Historical docs still naming old asset filenames (Pass B review,
  Pass 22 doc, playtest backlog) are historical records, not loaders, and
  were deliberately not rewritten.
- **Left alone, explicitly:** every open decision in section 4; Pass A
  pins, Pass C ItemSkuMap semantics and converter guard, D1 wholesale
  driver, D2/D3 feed and save behaviour (no test in the chain was edited by
  this pass); repository visibility and permissions (repo stays public in
  this pass — privatising is owner-only, see section 6); Main Scene,
  prefabs, and ProjectSettings (PC-local, not in Git); the Pass B scrap
  list (16 Core font copies — Resources copies are live, Core copies have
  no Git-visible consumer — and the renamed town variant's old name), which
  still awaits a Main Scene GUID search before any deletion, and which
  Kennedy has NOT approved for deletion.

## 6. Codex / Unity work — in order

1. **Pull ONLY this branch** (`muse/review2-final-cleanup`), verify the tip
   SHA against the parent's ls-remote report, confirm main and integration
   unchanged, and work from a clean worktree with a backup before any scene
   work. Do NOT pull Pass 11 or any intermediate branch as the working
   base — they are superseded links in the chain, not the pull target.
2. **Unity compilation** of ALL Series 1 + Series 2 + Review 2 files,
   including `SaveLoadManager.cs` (edited in Pass 11, D3, and second
   scripts wiring) and the second-SO diagnostic helper. Fix only proven
   compilation failures; do not rework behaviour to satisfy the compiler
   without reporting it.
3. **Unity import / final GUID check**, including the two renames
   (LargeLivery; RegionalFoundationMineralTownGenerationSettings) —
   references are GUID-based, but import must confirm no path-based break.
   Main Scene GUID search for the Pass B scrap list (16 Core font copies;
   renamed town variant's old name) before ANY deletion; delete `.asset`
   plus `.meta` together only if the search is empty, and only with
   Kennedy's approval for the scrap list.
4. **Production binding, in order:** (a) the one-line bootstrap bind
   (`Review2SecondScriptsSaveWiring.BindDriverRegistry` with the driver's
   live registry and the D3 state) plus assigning the D3 state (and,
   separately, the Pass 9 and Pass 10 states) to `SaveLoadManager` before
   save/load, with a save/load round trip proving a store feed registration
   survives in-game, and verification that the `SaveLoadManager` state
   object identity persists across save/load (else re-bind after load);
   (b) call Pass 6 `PlanBatches` → `RunScheduledDay` from the production
   day driver with the Pass 8 fuel source (not the Pass 6 in-stock source),
   running the Pass 6 pre-check before any Pass 8 fuel supply; (c) register
   real flour/notions counterparties (production catalog empty until then)
   and the one-line save registration so `SaveLoadManager.PassXlProcurementService`
   is set before the first save (F19, still open); (d) register the Pass 7
   bakery + fuel adapters in `SupplierDirectory` from the bootstrapper
   using the Pass 9 production `IBusinessCashPort`; (e) set the bakery
   adapter `DayIndex` at EVERY day start (Pass C's
   `CheckBakeryAdapterDay` diagnoses this); (f) wire wholesale intake →
   settle → retail-price decision → back-to-shelf task via `TaskAuthority`
   → shelf sale; (g) register the freight carrier and choose ONE settlement
   path per leg as the normal route, enforcing detailed-only freight for
   slice legs (one driver instance per carrier business; timer manager
   never on slice legs); (h) driver per-day household adapter construction
   and CareDay wiring after a load (a stale adapter across days would
   misdate consumption; a post-load CareDay wired to a fresh empty registry
   would silently unfed store horses); (i) manager recruitment per-day
   driver invocation and hub wiring (missed work → assessor → brain intent
   → driver effort → inquiry → employment, confirming no step auto-hires a
   person the player never approved).
5. **Production feed-offer fix** (section 4, item 2): runtime-offer path in
   or alongside `BuildHouseholdTradingPort` plus an owner-set price for
   runtime-added items, with the price-home decision. Until this lands, the
   D2 person-leg proof is a shopping-loop proof over a test port, not a
   production-port proof — do not quote it as one.
6. **Scene/prefab/Inspector wiring (Unity AI first check):** exactly one
   First Ledger bootstrapper and exactly one Civic Foundation Manager in
   Main Scene; manager audit assignments; demo/debug scripts
   absent/disabled; playtest Inspector bools wired to production drivers,
   not just models; dialogue UI/wording for Pass 5 recruitment; on-screen
   missed-work display from the Pass 2 API; scene confirmation that no
   scene caller already invokes `WorkDay`, `PlaceOrder`, or the adapters;
   `FirstLedger.asset` founder names (Kennedy's in-editor step).
7. **Test Runner (EditMode), suites run INDIVIDUALLY** — never one forced
   combined run (the earlier combined-run OOM lesson). Suites: Pass 1 Unity
   tests (4), Pass 2 bakery transfer (2) and idle (1), Pass 4 tests, Pass
   7's restated `GeneralStoreItemOfferIdentityTests`, six-person fixture
   test, six-person 30-day test, `PassXlPlayerAsNpcTests`,
   `SixPersonNoImmigrationTests`, Pass 10 and Pass 11 harness tests in
   Unity, Pass C's `Review2PassCWeeklyConverterTests`, and a decision on
   the `ProfessionWaveRuntimeTests.BakeryRequiresFuelAsSecondaryInput`
   expectation against the Pass C converter guard. Plus the Phase H
   disposition (section 4, item 1) and the `ConstructionInputSupportTests`
   timeout diagnosis (narrow first, never re-run unchanged).
8. **PlayMode full-chain proof in Main Scene** with named buyers/sellers
   and flow assertions (purchases, meals, bread units, fuel units, freight
   deliveries, wage payments), including settling whether the Pass 22
   zero-customer sale result is a blocker, a seed flake, or a known gap
   (must include a second seed if treated as must-fix evidence). What
   "done" means after Unity AI and Codex: a nine-person (once the fixture
   is authored) 30-day Main Scene run in which businesses have non-zero
   cash from real sales (not seed alone), households buy and eat bread and
   fuel from real sellers, the bakery bakes from real flour/fuel/notions,
   the Fuel Dealer dispenses real fuel, the store sells staple food item
   by item, the freight company carries slice legs with acceptance-gated
   settlement, Shane buys through real logic and is hired only on
   unfinished-work evidence, players and NPCs complete and miss tasks
   visibly, and newcomers (if any) arrive only through a driven channel
   with housing and terms or an explicit unemployed-with-plan flag. Until
   that run exists, all Muse passes are models, callables, and tests on
   branches, not a working in-game economy.
9. **ScriptableObject Codex items:** General Store canonical decision
   (section 4, item 3); LargeLivery hosting decision; population default vs
   compact decision; 9 profile-less business types (author or explicitly
   rule out); calendar start year; `FirstLedger.asset` founders
   (Kennedy's in-editor step).
10. **Privacy transition (owner-only, not started):** verify Muse's GitHub
    App installation selects `KenB-O/Land-and-Ledgers-Scripts` (Contents:
    Read) BEFORE any visibility flip; decide publish-before-privatize vs
    privatize-with-baseline; owner changes visibility in GitHub Settings →
    General → Danger Zone; then verify Muse still reads via the connector,
    future Codex pushes remain possible when explicitly authorized, and
    unauthenticated public access fails. Privatising must not delete local
    Unity files, discard commits, reset branches, prevent Codex working
    locally, give Muse write permission, or grant all connected agents
    access to every private repo. Private does not erase copies already
    downloaded (including review clones); forks count was 0 at the second
    review, a point-in-time fact.

## 7. Separate lines — NOT in this chain, do NOT silently merge

- **Codex local commit `f505faa3`** (`f505faa35f86056396a0b3d6188fa83d20e4a737`,
  locally on top of Pass 11 on branch `muse/series2-pass11-final-review`)
  is NOT in this chain and was NOT reviewed, rebased, or merged by any
  Review 2 pass, because it was never published to GitHub. When/if it is
  published (expected branch
  `codex/muse-review-food-settlement-f505faa3`), it needs a separate
  review/integration decision — never a silent merge into this line, and
  never a rebase of this line onto it without Kennedy's explicit
  integration decision.
- **Codex push retry is still blocked on housekeeping:** Kennedy must
  first revoke the credential `LandLedgersScriptsPush-2026-11-02` — its
  one-time token was visible in a captured settings screenshot, so it must
  never be reused — then create a fresh credential scoped to
  `KenB-O/Land-and-Ledgers-Scripts` only (Contents: Read and write, plus
  GitHub-required Metadata: Read-only) before any retry. He should also
  protect/delete that screenshot and not share it. This housekeeping does
  not block the Muse passes and does not block pulling this branch.
- **Repository visibility:** the repo is public as of the second review
  (unauthenticated `ls-remote` and clone succeed). Nothing in this pass
  changes visibility, collaborators, or permissions.

## 8. NOT claimed

Unity compilation, Test Runner, PlayMode, scene wiring, daily tick driving,
task issuance, Main Scene purchase/meal evidence, or final Unity import
GUID check. This pass's code changes the game only when Kennedy/Codex pull
this branch, integrate, compile, wire scenes/drivers, and verify in Unity.
Roslyn-harness results above are the full extent of this pass's
verification, and they are reported honestly: 136 passed / 0 failed on the
main harness, 94 passed / 3 baseline failures on Phase H, with the 3
failures understood as the Phase H policy fork in section 4, item 1.
