# Series 2 Pass 8 — Freight Carrier + Fuel Chain Review

Base: `muse/series2-pass7-selling` at `9594e9707ada1ad14494d76f3eb3ac6acbed0a7b`
(Pass 7 tip; parent Pass 6 `7dfebac9`). Branch: `muse/series2-pass8-freight-fuel`.
Roslyn harness only. No Unity compilation, Test Runner, PlayMode, or
scene wiring is claimed anywhere in this pass.

## 1. What was built

New folder `Domains/Economy/FreightFuel/` (folder `.meta` + four
code files, all additions, no existing file edited):

- `Series2FuelDealer.cs` — the dealer-owned fuel callable Pass 7
  deliberately did not create. It owns the real
  `FuelYardByproductIntake`: sawmill byproduct receipt with
  provenance, stock, FIFO dispense via `TryDispenseUnits`
  (shortfall dispenses nothing, emptied lots never re-sold), and
  `SupplyFuelToBakery` (one bakery lot per dispense line, so
  provenance survives lot by lot). Custody only: it settles NO
  cash, because a retail sale already has a sale path (Pass 7
  adapter) and a carrier leg already has a settlement path
  (below); settling cash here too would sell the same fuel
  twice. Sawmill receipt pays the sawmill NOTHING and diagnoses
  that in plain text — the byproduct price, if any, is an open
  Kennedy decision, not a zero price smuggled in.
- `Series2DealerBakeryFuelSource.cs` — the
  `ISeries2BakeryFuelSource` implementation Pass 6's interface
  was waiting for, swapped in WITHOUT editing any Pass 6 file.
  The Pass 6 in-stock source stays as the fallback for a bakery
  that already holds fuel; it is NOT the production source.
  Dealer stock first, then an optional caller-supplied off-map
  import callback (named order + origin, via
  `BakeryFuelSupply.ReceiveImportArrival`). Without a covering
  source it refuses and the Pass 6 driver refuses the bake.
  Intake-to-bakery conversion is a caller parameter
  (`IntakeUnitsPerBakeryUnit`, >= 1), carried across dispense
  lines with remainder-forwarding so no intake unit is lost to
  rounding; it is a test parameter, NEVER a decided game rate,
  and must not be read as canonising Pass 7's adapter default
  (1 intake unit = 1 household unit, a different conversion).
  Dealer-only shortfall refuses BEFORE dispensing, so a refused
  supply leaves both stores untouched.
- `Series2Carrier.cs` — the carrier contract, the shipment
  bridge, slice legs, and settlement:
  - `ISeries2Carrier` + `Series2FreightProductionCarrier`, a
    thin bridge over the real `FreightProductionDriver`
    (detailed shipments only; see section 3).
  - `Series2CarrierQuoteResolver`: a FreightBusiness quote
    (Pass 6 legacy `OffMapFreightQuote`) or order request
    (Pass 3 `Pass3OrderRequest`) with NO registered carrier is
    REFUSED — it must not keep behaving silently like
    SellerFreight, the Pass 6 gap this pass closes. With a
    carrier it becomes a leg charged at exactly the quote /
    allocated freight figure the caller already priced; this
    pass never recomputes a freight rate. SellerFreight and
    SelfCollect paths are untouched.
  - `Series2SliceLegs`: off-map arrival -> store/bakery,
    store -> bakery, bakery -> store wholesale, dealer ->
    bakery, dealer -> household. Every field is caller-
    supplied generic business/route input — NOT named Ethan
    shipments, because fixture inputs are blocked on Kennedy
    inputs and are never invented here. A leg moves SHIPMENT
    STATE, not item stock: stock moves only in the caller-
    wired `ReceiveShipment` callback (Pass 6 feed, Pass 3
    wholesale intake into BACK storage only, Pass 8 dealer
    supply, Pass 8 reserve link). A wholesale leg's callback
    must intake into BACK storage only (Pass 3 rule); the
    carrier itself never puts bread on a shelf.
  - `Series2CarrierSettlement`: freight revenue settles ONLY
    on full delivery acceptance (Completed AND
    `deliveryApplied`). Delayed/Partial shipments settle
    NOTHING and keep cargo in wagon custody. Cash is atomic
    across caller-supplied `IBusinessCashPort` ports (payer
    balance checked, debit, carrier credit, refund on credit
    failure), exactly like Pass 7 wholesale settlement.
    Cross-path guard, both directions: it refuses a shipment
    whose `freightRevenueCollected` flag is already set
    (driver collection already paid it) and sets that flag
    when it settles, so a later driver tick cannot collect
    twice. HouseholdLedger is never used for freight charges:
    they are business-to-business cash, not household cash.
- `Series2HouseholdFuelReserveLink.cs` — bought household fuel
  -> `fuel_wood` reserve, with the bundle-to-reserve
  conversion as a caller parameter (`ReserveUnitsPerHouseholdUnit`),
  never a decided rate and never a canonisation of Pass 7's
  adapter default. It buys nothing, dispenses nothing, and
  touches no cash: the adapter (shopping loop) or carrier leg
  already moved the fuel and the money; this only mirrors a
  delivered quantity into the reserve the heating evaluator
  reads. No reserve after initialisation = loud refusal, never
  an invented reserve.
- `Tests/Editor/Economy/Series2Pass8FreightFuelTests.cs` —
  Roslyn acceptance, production classes as the system under
  test: dealer FIFO/shortfall/orphan refusal, sawmill
  no-payment diagnosis, dealer -> bakery fuel store with
  provenance, conversion-factor rounding, off-map fallback
  and unwired refusal, adapter sale + reserve link (revenue
  and reserve both move), carrier-less FreightBusiness
  refusal vs SellerFreight untouched, Pass 3 re-route at
  allocated freight, carrier leg acceptance -> settlement
  exactly once, Delayed leg -> no settlement + custody held,
  settlement refund on carrier credit failure, tick
  requirement (no TaskAuthority + budgets = 0 advanced, with
  a diagnostic), wrong-carrier refusal, slice leg shapes.
  Result: **12 passed, 0 failed** in harness-pass8, with
  Pass 6 (8) and Pass 7 (16) re-run green: **36 passed,
  0 failed total**.

## 2. Decisions made in this pass, stated plainly

- SURVIVING SHIPMENT SET (slice): DETAILED shipments
  (`FreightShipmentService` + `FreightProductionDriver`) survive
  for the slice. The timer-driven `LogisticsRuntimeManager`
  (MonoBehaviour, category-stock bulk path) is the NON-SLICE
  path: not edited, not deleted, never run on a slice leg.
  This is a documented decision, not a deletion — Codex must
  not wire both systems to the same shipment.
- TICK REQUIREMENT: `FreightProductionDriver.AdvanceTick`
  needs `hooks.Tasks` (TaskAuthority) AND `hooks.Budgets`
  (`WorkTimeBudgetStore`); the bridge checks that BEFORE
  advancing and returns 0 with a diagnostic when it is not
  met. A valid handler id is additionally required for
  loading/unloading steps (the driver diagnoses waiting
  shipments loudly; in-transit shipments still advance and
  revenue still collects with an invalid handler, per the
  driver — that behaviour is the driver's, not this pass's).
- PRODUCTION CASH PORT: this pass accepts a caller-supplied
  `IBusinessCashPort` everywhere and assumes NO production
  port. Nuance, stated honestly: the only in-tree
  `BusinessRuntimeState` port is `SliceWorkAndPay.
  BusinessRuntimeCashPort`, which lives in the orchestrator
  path (`Orchestration/Player`), not in a production driver
  path — Codex must place a production port (it may reuse
  that pattern) before any of this runs in production. Pass 7
  tests used a test-only port for the same reason.
- PASS 7 ADAPTER CASH GAP, NOT SILENTLY FIXED: Pass 7
  adapters post revenue AFTER stock moves; if `Credit`
  fails they log a WARNING and the sale stands. This pass
  does NOT rewrite Pass 7 adapters (brief scope). The
  production cash port must therefore be effectively
  infallible (a `BusinessRuntimeState` port's `Credit` via
  `AddCashCents` cannot fail for a non-negative amount), or
  a later pass must add a pre-check/reversal to the adapters.
  Until then, adapter sales are stock-first, cash-second —
  Codex must not read them as atomic.
- BAKERY DAYINDEX, NOT CHANGED: `DayIndex` must be set at
  every day start by Codex (Pass 7 caveat, unchanged). A
  day-equality guard is a Pass 9/10 consideration, not a
  Pass 8 edit, and no Pass 8 code depends on a day value it
  did not receive as a parameter.
- ITEM-LEVEL STOCK, NOT ASSUMED: `BusinessRuntimeState` is
  per-category, not per-item. No Pass 8 code records a sale
  against an item the pool did not supply, and no Pass 8
  test reads a per-item stock count as truth. Item-level
  stock migration remains a save-shape / Kennedy-Codex
  decision (Pass 7 caveat, unchanged).

## 3. Deliberately NOT built (Pass 8 scope held)

- No Pass 6 driver change, no Pass 7 adapter/builder/
  settlement change: Pass 6 and Pass 7 classes are consumed,
  never rewritten. (Pass 7's adapter cash pattern above is
  documented, not fixed, per the brief.)
- No bootstrapper registration: carrier registration,
  adapter registration, `SaveLoadManager.
  PassXlProcurementService` assignment, and day-driver
  hook-up are Codex's wiring, not this pass.
- No `.asset` change. No Main Scene / prefab / Inspector
  change. No fixture authoring: NO Ethan/Tia/Shane names,
  ages, cash amounts, business names, horses, wagons, or
  house details are invented anywhere in this pass — all
  test inputs are generic business/route ids.
- No heating rates, coal, stove devices, or heating-vs-food
  priority. The reserve link credits fuel; what heating does
  with it is existing evaluator behaviour, unchanged.
- No town gates, no seconds or sub-minute work, no
  WorkTimeBudget cap change, no prices/rates as canon (all
  test parameters or caller inputs), no item-level stock
  migration, no `ThreeBusinessSlice` in-place bridge change
  (left for its orchestrator tests).
- No save adoption: the carrier settlement's per-instance
  settled set, dealer stock, and fuel-source state are NOT
  in any save DTO yet. That is Pass 9 work, stated, not
  claimed. The `freightRevenueCollected` flag on the
  shipment itself IS part of the shipment save DTO, which
  is why it is used as the cross-path guard.

## 4. Corrections and caveats, stated plainly

- BRIEF CORRECTION (production cash port): the brief said
  no production class implements `IBusinessCashPort` over
  `BusinessRuntimeState` yet. The nuance found in files is
  that `SliceWorkAndPay.BusinessRuntimeCashPort` does
  implement it over `BusinessRuntimeState` — but only in
  the orchestrator path, not as a production driver port.
  The brief's operative point stands (Codex must place a
  production port); the file-level fact is corrected here
  so Codex does not hunt for a port that does not exist in
  a driver path, nor ignore the orchestrator pattern it can
  reuse.
- PARTIAL-SUPPLY HONESTY: a dealer-only `SupplyTo` refuses
  BEFORE dispensing when dealer stock cannot cover the
  request (both stores untouched). With an off-map callback
  wired, a dealer portion may move before the callback
  refuses — that portion is real fuel in the bakery store,
  not lost, but the bake is still refused until the full
  request is covered; the Pass 6 driver pre-check (dealer
  stock via the interface) is the normal gate that prevents
  reaching this path with an uncovered request.
- CROSS-SYSTEM DOUBLE-BOOKING: exclusivity is enforced
  WITHIN the detailed service only. Nothing in this pass
  prevents Codex from also running a timer-manager shipment
  on the same wagon/goods. Slice discipline (detailed only)
  is a wiring rule, not a code guard — stated here so it is
  not mistaken for one.
- NEW GUIDS: folder + four code files + test file + this
  doc each carry a fresh GUID; per-GUID grep shows each
  GUID in exactly its own `.meta` file. A full repo-wide
  duplicate audit was not re-run (it stalls on the large
  repo, as in the post-Pass 6 audit); Unity import is the
  final GUID check, as before.
- ROSLYN ONLY: the harness compiles a curated subset with
  shims (UnityEngine, NUnit, persistence DTOs), exactly as
  Pass 6/7 did. A green harness is NOT Unity compilation,
  NOT Test Runner, NOT PlayMode, and NOT scene wiring.

## 5. What Codex must hook up (in order)

1. Place a production `IBusinessCashPort` over
   `BusinessRuntimeState` (reusing the orchestrator pattern
   if suitable) and wire it to the dealer, the adapters, and
   the carrier settlement. Confirm the Pass 7 adapter cash
   gap (section 2) is acceptable with that port, or send it
   back for a pre-check/reversal pass.
2. Register the freight carrier (generic business id, real
   wagon, real driver + animals) and choose ONE settlement
   path per leg as the normal route (driver collection on
   the tick, or `Series2CarrierSettlement`); the flag guard
   prevents double collection, but the normal route must
   still be one path, not both.
3. Wire slice leg receiving callbacks: off-map arrival ->
   Pass 6 procurement arrival; store -> bakery -> Pass 6
   feed; bakery -> store wholesale -> Pass 3 intake (BACK
   only) -> Pass 7 settlement -> retail price decision ->
   back-to-shelf task; dealer -> bakery -> Pass 8 dealer
   supply; dealer -> household -> Pass 8 reserve link.
   Wire the day driver to set `DayIndex` at day start and
   to call the Pass 6 bakery driver with the Pass 8 fuel
   source (not the Pass 6 in-stock source) for production
   bakes.
4. Decide the sawmill byproduct price (if any) and, if
   priced, settle sawmill payments through the business cash
   ports as a separate caller step — never by editing the
   dealer receipt to invent a price.
5. Adopt Pass 8 state into save/load (Pass 9): carrier
   settlement settled set, dealer intake lots (the intake
   already has a save DTO; it must be wired into a
   production save, not just captured), fuel-source state.
6. Confirm in Unity Test Runner that the Pass 8 tests'
   Unity-visible counterparts (none authored here — this
   pass is plain C#) and the Pass 6/7 Unity tests still
   pass, and run a Main Scene leg: dealer stock -> bakery
   fuel -> bake, and dealer stock -> household reserve,
   through the carrier, not through an orchestrator.

## 6. Open Kennedy decisions touched by this pass (unchanged, none guessed)

Sawmill byproduct price (if any); intake-to-bakery fuel
conversion factor; bundle-to-reserve conversion factor;
dealer vs mine coal retail; heating methods/rates/devices;
freight charges and commissions (all caller parameters);
first-cycle seed (including any freight starting
cash/wagons/horses — blocked on Kennedy inputs, not
invented); bakery aging (baked-day, unchanged); store
category shape (per-item offers over a shared pool,
unchanged); WorkTimeBudget cap vs task sequencing
(unchanged); seconds small vs full (unchanged); all
prices/rates/weights/frequencies (test parameters only).
