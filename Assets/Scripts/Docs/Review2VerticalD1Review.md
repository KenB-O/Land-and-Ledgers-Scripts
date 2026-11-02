# Review 2 Vertical Slice Pass D1 — Bakery to Store to Household Meal

Pass D1 is the first vertical-slice pass after Pass C. It chains, on
production driver/adapter classes only, the leg the second read-only
review found unwired: bakery shelf bread -> store wholesale intake ->
cash settlement -> store shelf -> live store category pool -> a
household shopping-loop purchase -> lot meal -> nutrition. Before D1
the bakery adapter sold direct (Pass 10) and the Pass 3 wholesale
catalog held shelf stock (Pass 7), but no caller connected the two,
so bread never reached a household through the store.

## What changed

New driver, production code, no existing adapter semantics changed:

- `Assets/Scripts/Domains/Economy/Ordering/Review2BakeryToStoreWholesaleDriver.cs`
  (plus its `.meta`, fresh GUID, never copied from another file).
  Two steps, both caller-parameterised (prices, quantities, day
  indices are test parameters, never canon):
  - `TransferToShelf`: oldest sellable bakery shelf lot ->
    `Pass3WholesaleIntake.Receive` into BACK -> `Series2WholesaleSettlement.SettleCash`
    -> `Series2WholesaleShelfMove.MoveToShelf`, returning the Pass 2
    task descriptor `gs.move-back-to-shelf` at 2 minutes per lot.
    Task issuance itself remains Codex work through `TaskAuthority`;
    the driver only names the task the move must be issued as.
    Pre-checks before any mutation: map translation, shelf
    availability, baked-day age (transfer at most 1 day past baked
    day, the Pass 3 rule), payer balance, retail price positive and
    different from wholesale cost. Settlement failure after intake is
    a named recoverable state: intake stands in BACK unsettled, no
    shelf or live movement, the lot cannot be re-intaken (duplicate
    guard); the caller settles the returned record, then calls
    `MoveSettledLotToShelf`. Freight charges are NOT settled here:
    if a carrier carried the leg, freight revenue settles separately
    via `Series2CarrierSettlement` on delivery acceptance, and this
    driver is the carrier leg's `ReceiveShipment` callback body.
  - `StockShelfToLive`: Pass 3 shelf units -> live store category
    pool via `IGeneralStoreTradingPort`. Shelf units are consumed
    only after a fully accepted `ReceiveStock`, and are restored if
    stocking refuses. A capped or full live pool refuses the whole
    quantity, never a partial stocking.

One additive production method, no other Pass 3 behaviour changed:

- `Assets/Scripts/Domains/Economy/Ordering/Pass3OrderingModel.cs`:
  `Pass3ProductLine.ConsumeShelfUnits` (additive only; `MoveToShelf`
  and `MoveToBack` untouched). Refuses bad quantities and short
  shelf stock, moving nothing on refusal. This is what keeps the
  Pass 3 shelf and the live pool from double-counting the same
  loaves: units leave the shelf exactly when they enter the pool.

Tests (Roslyn only, production classes as the system under test):

- `Assets/Scripts/Tests/Editor/Economy/Review2VerticalD1Tests.cs`
  (plus its `.meta`, fresh GUID). 9 tests. The live store port in
  these tests is a test seam over the same `BusinessRuntimeState`
  the wholesale cash port uses, so wholesale cash and retail cash
  reconcile in ONE runtime; it is not a second cash authority. The
  household is a generic test person, never named Ethan/Tia/Shane,
  and never presented as the slice fixture.

## A-to-Z trace, as run in the harness, with exact reconciliation

Caller parameters: bake day 2, transfer day 2, 24 loaves per batch,
wholesale 2c, retail 5c, live category `staple_food`, household
funds 10000c, store starting cash 5000c, bakery starting cash 2000c.

1. Bake: 1 batch on day 2, 24 loaves, bakery shelf 24, condition
   Fresh (baked day 2, transferred day 2).
2. Wholesale transfer: bakery shelf 24 -> 0; store pays 48c
   (24 x 2c): store 5000 -> 4952, bakery 2000 -> 2048; Pass 3 shelf
   24 at retail 5c, BACK 0.
3. Stock live: Pass 3 shelf 24 -> 0; live `staple_food` pool 0 -> 24;
   no cash moves on stocking.
4. Shopping loop: shortage monitor opens a real bread need, 14 units
   (empty household, 2/day, 7-day threshold); seller knowledge and
   the supplier directory route to the store adapter; the loop buys
   14 at 5c: household 10000 -> 9930, store 4952 -> 5022, live pool
   24 -> 10, household inventory 14, need Fulfilled, buyer outflow
   70c equals business cash in 70c exactly.
5. Meal: morning + evening from the purchased bread (midday disabled
   by the caller, so the two substantial slots match the nutrition
   denominator of 2 meals/day; that is a test parameter, not a canon
   meal rule): 2 meals served, 0 missed, inventory 14 -> 12.
6. Nutrition: a person starting the day at 0.70 nutrition, fully fed
   (2 of 2 meals), recovers to 1.00 (0.70 + 0.30, capped).

Refusal modes, each moving nothing it must not move, all tested:

- Stale lot (baked day 2, transfer day 4): refused before intake;
  bakery shelf stays 24, both cash balances unchanged, no catalog
  line created.
- Unmapped product (`bake.rolls`): refused with "no store
  translation"; rolls and pies stay deliberately unmapped.
- Retail price equal to wholesale cost (2c = 2c): refused before
  intake, so no cash moves and no catalog line is created.
- Payer cash below the wholesale amount (10c against 48c): refused
  before intake.
- Settlement failure after intake: bread stands in BACK, unsettled
  (24 BACK, 0 shelf); bakery shelf taken and not undone; no cash
  moved; recovery settles the returned record (store 4952, bakery
  2048) and then moves the lot to shelf (BACK 0, shelf 24).
- Capped live pool (cap 10 against 24): stocking refuses the whole
  quantity, Pass 3 shelf stays 24, pool stays 0; raising the cap
  retries to success (shelf 0, pool 24).
- `ConsumeShelfUnits` refusals (short shelf, zero quantity) move
  nothing; an exact consume succeeds.

## What was checked and left alone

- Pass A pins honoured, not weakened: retail WARNING-only cash
  untouched; bakery `DayIndex` gating untouched (the bake driver
  here runs on a fixed day index, as Pass 10 did); dealer fuel
  partial supply untouched (no fuel leg is run in D1).
- Pass C untouched: `Review2ItemSkuMap` semantics unchanged; the
  driver only calls `TryTranslate` on it. The weekly converter
  guard and the driver-binding checklist are untouched.
- No adapter rewired: the bakery and General Store adapters keep
  their existing semantics. In particular the bakery adapter is
  still not registered in production (test-only) — the D1 driver
  bypasses the adapter by taking the bakery shelf lot directly,
  which is why the chain works in the harness while the production
  binding gap remains open below.
- No `.asset` changed, created, or deleted. No fixture authored.
  The six-person fixture and validator are untouched. No Main
  Scene, prefab, or ProjectSettings change. No repository
  visibility or permission change. No serialized type, field, or
  namespace renamed. No orchestrator used as the system under test.

## Left open — needs a Kennedy/Codex decision, never silently fixed

- Production registration of the bakery and fuel adapters, and the
  weekly converter deliberately disabled for Bakery/FuelDealer:
  D1 proves the chain on production classes in the harness, but a
  player in the Main Scene still has no registered bakery seller
  to buy from. Binding is Codex/Unity work.
- Task issuance for `gs.move-back-to-shelf` (2 min/lot): the driver
  returns the descriptor; a real task through `TaskAuthority`,
  with a real worker and real time, is not run here.
- Seller registration without guaranteed membership/knowledge
  binding in the Main Scene (plausible, bootstrapper comment):
  the harness test wires knowledge explicitly.
- Category-pool vs per-item offers: the live pool is still a
  category pool; the store sells "bread" as a `staple_food` offer
  at the category price, not as a per-item priced offer.
- General Store profile-vs-definition dual authority: untouched,
  not resolved, not silently picked.
- Horse feed source: untouched, no source chosen, null not pinned
  as expected.
- Employment registry gap, item-level stock migration, freight
  payer per leg, and all prices/rates as canon: untouched; D1's
  prices are caller parameters.
- The live store port in production is a MonoBehaviour manager
  wrapper, not constructible in Roslyn: the D1 stocking step is
  proved against the production interface over a real runtime
  state, and Codex must verify the same step against the real
  manager in Unity.

## Verification (Roslyn harness only)

- D1 suite: 9 passed, 0 failed.
- Full main harness at the D1 tip (Series 2 + Pass A + Pass C +
  D1): 107 passed, 0 failed (98 before D1, plus 9 D1).
- Phase H at the D1 tip, freshly built binary: 12 passed, 3 failed
  — the same 3 baseline failures as Pass C (insufficient-funds
  trace, hardship missed-meals, restock procurement chain),
  unchanged by D1, which adds no code path Phase H exercises.
- `ConstructionInputSupportTests` not re-run, per instructions.

## NOT claimed

Unity compilation, Test Runner, PlayMode, scene wiring, daily tick
driving, task issuance, Main Scene purchase/meal evidence, or final
Unity import GUID check. New GUIDs were checked via `git grep` to
be mutually distinct and each appearing once. D1 code changes the
game only when Kennedy/Codex pull, integrate, compile, wire
scenes/drivers, and verify in Unity.
