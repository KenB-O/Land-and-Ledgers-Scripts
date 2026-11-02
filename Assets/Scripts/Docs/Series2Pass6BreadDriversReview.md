# Series 2 Pass 6 — Bread Drivers Review

Base: `muse/final-reconciliation` at `509dce62fa59172569a403f861c3e90b420b0245`
(Series 1 final tip). Branch: `muse/series2-pass6-bread-drivers`.
Roslyn harness only. No Unity compilation, Test Runner, PlayMode, or scene
wiring is claimed anywhere in this pass.

## 1. What was built

New folder `Assets/Scripts/Domains/Economy/BreadDrivers/` (domain logic,
per folder ownership), five plain-C# files, no scene code, no bootstrapper
wiring. Codex hooks these classes up from the production day driver; this
pass wires nothing itself.

- `Series2BakeryProductionDriver.cs` — schedule-gated calls to the real
  `BakeryShopRuntime.PlanBatch` + `WorkDay`. Pre-checks before any bake:
  flour, notions, fuel (through the interface below), a ready oven, and a
  usable baker's hand kit. Any failure refuses loudly with batches still
  planned; `WorkDay` is never called on a failed pre-check. Schedule gate:
  a day earlier than the planned day refuses (nothing bakes early); a day
  with nothing planned refuses (WorkDay is never a generic daily tick);
  a later day runs as a diagnosed catch-up. Labour is reported, never
  enforced: planned prep + bake minutes vs the shop's `BakerMinutesPerDay`
  (a code value, not canon) and vs any caller-supplied daily budget; an
  over-budget day is diagnosed and still runs, because the budget owner
  is WorkDay, not this driver. Shop runtime is the surviving production
  model (Pass 2 decision). Staged-chain lots reach the shelf ONLY through
  the existing `Pass2BakeryStagedTransfer` bridge (baked-day aging), which
  Codex calls directly; this pass deliberately adds no wrapper and no
  second transfer path.
- `Series2BakeryFuelSource.cs` — `ISeries2BakeryFuelSource`: fuel on hand
  plus `SupplyTo` into the shop's fuel store. The Pass 6 implementation
  (`Series2InStockBakeryFuelSource`) only verifies fuel already in stock.
  Pass 8 implements the same interface with a dealer/off-map source and
  swaps it in without editing Pass 6. No fuel is built here.
- `Series2ProcurementDriver.cs` — counterparty registration
  (`Series2CounterpartyRegistration`: supplier, SKU, unit goods cost,
  freight surcharge, transit, supply — every field caller-supplied, never
  invented as canon), a quote builder, the flour/notions order driver
  (`Series2FlourProcurementDriver`: scheduled `PlaceOrder` +
  `ReceiveArrivals` on `OffMapNamedSkuProcurementService`), and the named
  save setter (`Series2ProcurementSaveRegistration`). The one line Codex
  must call is
  `registration.Apply(s => saveLoadManager.PassXlProcurementService = s);`
  — this pass never calls it and never assigns the slot itself, so F19
  (production procurement registration) is still open after this pass.
- `Series2StoreToBakeryFeed.cs` — the production store-to-bakery feed
  over real runtimes, with the same rollback semantics as the
  `ThreeBusinessSlice` orchestrator bridge (consume store lots, bakery
  cash check with consumption undone on failure, store cash, bakery bin;
  on bin refusal, consumption undone and both cash movements restored).
  The orchestrator stays as a regression reference only. Flour-unit
  conversion is a caller-supplied parameter (`Series2FlourUnitConversion`):
  `LbPerStoreUnit` is the number applied, `StoreUnitDescription` names
  what the caller believes a store unit is, and an optional
  `ExpectedLbPerNamedUnit` produces a loud WARNING (not a silent fix, not
  a refusal) when description and number disagree. This makes the audit's
  finding explicit and testable: the old run used 1 lb per store unit
  while the store profile describes a 10 lb sack, and neither number is
  canon until Kennedy decides.
- `Series2NotionsSupply.cs` — notions order/arrival/intake through the
  SAME store procurement service as flour (so the single
  `PassXlProcurementService` save slot covers notions), feeding the
  bakery bin as `bakery-notions` through the same consume-pay-bin path
  with the same rollback. The notions vendor, SKU, price, and
  store-unit-to-bin-unit conversion are all caller-supplied. A
  bakery-owned notions service (no store middleman, no inter-business
  cash) is a Pass 9 decision, not built here.

## 2. The one order path (and the other path, documented)

CHOSEN for this driver: the legacy `OffMapQuoteToOrder` bridge. Every
flour/notions order goes through a validated `OffMapFreightQuote` (total
recomputes as goods + freight, goods divide evenly, SelfCollect refused
because no pickup journey exists, Unspecified refused because it names
no arrangement) before `PlaceOrder`. DOCUMENTED AS THE PLAYER-UI PATH,
NOT WIRED HERE: the Pass 3 supplier-order model (`Pass3OrderDraft`).
This driver accepts no Pass 3 draft; a draft reaches an order only if
the player UI first converts it into a validated quote, an explicit
conversion step Codex owns. Wiring both to the same order would be a
double-ordering path, so it is never done silently.

## 3. Roslyn acceptance (production classes, not orchestrators)

Test file `Assets/Scripts/Tests/Editor/Economy/Series2Pass6BreadDriversTests.cs`.
Result: **8 passed, 0 failed** (Roslyn harness
`harness-pass6.csproj`, old Phase 8 domain subset mapped onto the new
root layout plus all original harness shims, plus one harness-only shim
for the staged-lot field shape, which no test exercises).

- Flour to bake to shelf, cash reconciled: store 5000c, bakery 2000c
  (test parameters). Flour order 20u at 10c + 250c freight = 450c on
  arrival; notions order 5u at 10c = 50c. Feed 2 flour units at 10 lb
  per unit = 20 lb for 40c; feed 1 notions unit = 1 notion for 10c.
  One batch baked day 2: 24 loaves, Fresh, baked day 2. Final cash store
  4550c, bakery 1950c; flour left 8 lb, notions 0, fuel 2 minus 1 firing
  = 1. Baking itself moves no cash.
- Feed with insufficient bakery cash moves nothing (sellable units and
  cash unchanged).
- Conversion is explicit: 2 store units at 1 lb = 2 lb in the bin, not
  20; a "10 lb sack" description with a 1 lb applied number warns loudly.
- No fuel: `RunScheduledDay` returns -1, shelf empty, batch stays Planned.
- Early day: planned day 5, run day 4 returns -1, nothing baked.
- Tampered quote total (999c vs 450c) places no order.
- Zero notions conversion refuses, store notions untouched.
- Save registration names `PassXlProcurementService` and hands Codex the
  exact service instance.

## 4. Deliberately NOT built (Pass 6 scope held)

- No `FirstLedgerSliceBootstrapper` edit, no `SimulationDrivers` daily
  tick change, no `GeneralStoreRuntimeManager` trading-port change, no
  `.asset` change, no Main Scene / prefab / Inspector change.
- No fuel production, no dealer adapter, no off-map fuel input (Pass 8).
- No supplier adapters for household shopping, no trading-port switch,
  no wholesale settlement or shelf-move (Pass 7).
- No player duty binding, no save adoption beyond naming the slot
  (Pass 9). Driver state (planned day, planned batch ids) is NOT in any
  save DTO after this pass; a reload loses the plan, and Codex must
  re-plan or adopt the state in Pass 9. Stated, not hidden.
- No seconds or sub-minute tasks; all durations are the existing minute
  values from `BakeryBreadCatalog`. No price, rate, duration, or
  conversion is canon; every figure in code comments and tests is a
  test parameter or a caller-supplied value.
- No `WorkTimeBudget` change: the cap is reported against, never
  changed, truncated, or deleted. The 1-minute floor and 600-minute cap
  remain code conventions, not Kennedy decisions.
- No Unity-only test file: the drivers are plain C# and fully
  Roslyn-testable. Unity compilation, Test Runner, PlayMode, and scene
  wiring remain Unity AI / Codex work, NOT RUN here.

## 5. What Codex must hook up (in order)

1. Call `PlanBatches` then `RunScheduledDay` from the production bakery
   day driver, passing the real oven definition, component finder, and
   hand-kit state; pass the fuel source Pass 8 supplies (until then,
   the in-stock source, which means fuel must already be in the store).
2. Call the save registration line (section 1) with the production
   `SaveLoadManager` before the first save, or procurement persists
   empty (F19, still open).
3. Register real flour and notions counterparties (supplier ids, prices,
   freight) — the production catalog is empty until Kennedy/Codex
   register them; this pass registers nothing in production.
4. Decide the flour-unit conversion (1 lb vs 10 lb sack vs something
   else) before any driver runs in production; the driver applies
   whatever the caller passes, and a wrong caller value bakes wrong
   quantities loudly but does not self-correct.
5. Call the Pass 2 staged-transfer bridge directly for staged lots;
   do not build a second transfer path.
6. In Pass 9, adopt driver state (planned day/batch ids) into save/load
   or document it as transient and re-plan on load.

## 6. Open Kennedy decisions touched by this pass (unchanged, none guessed)

Flour-unit semantics (store SKU/sack vs bakery lb); flour and notions
prices and freight rates; whether notions ride the store service
forever or move to a bakery-owned service; bakery aging (baked-day
default stands; transfer-day remains the open alternative, in the one
place the bridge owns it). Slice expansion noted during this pass
(freight household, Shane Bryan) does not change these drivers: bread
drivers do not depend on the carrier, and Shane's buying path is Pass 7
selling scope, queued for the post-Pass 6 audit.
