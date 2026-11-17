# UBC Lane I1 — Household Shopping and Consumption Chain Audit

- Date: 2026-11-17
- Lane: I1, workstream I (household shopping and consumption chain)
- Base: `muse/universal-business-construction-2026-11-17` at `f9e43292` (E1, S2, D1, A1 merged)
- Scope: the full household chain. A household needs food, identifies a supplier
  through legitimate knowledge, a person purchases, the merchant has stock and
  staff, a real transaction happens, money moves, goods enter household custody,
  food is prepared and consumed, and needs and inventory are affected.
- Method: read on disk. Every verdict below cites the file and method that
  decides it. Nothing here rests on a planning document. Where a link can only
  be settled live (Unity pathing, scene state), the verdict says so and names
  the Codex check instead of claiming it.

## Headline

The chain is wired end to end and honest at every step: no consumption without
legitimate inventory, no purchase credited for an attempted trip, no meal
credited merely because a driver executed. One confirmed break dominates the
rest: **I1-M1, the shortage monitor sizes every meal-priority item's need for
the full 7-day target independently, while meals consume one item per serving,
so one household demands roughly six times the food it eats and strips the
store for every other household.** The repair is in
`HouseholdShortageMonitor.Evaluate` (priority-ordered allocation of the
shortfall, with a purchasability-gated fallback), with regression tests in
`Tests/Editor/Population/HouseholdShortageAllocationTests.cs`. The remaining
findings are observations, doc mismatches, or Unity-verification items, not
breaks.

## Verdicts

### 1. The store is registered as a real supplier — HOLDS

`GeneralStoreSupplierAdapter.RegisterIn`
(`Domains/Economy/Businesses/GeneralStore/GeneralStoreSupplierAdapter.cs:157`)
registers the live store in the `SupplierDirectory`. Production callers are
`FirstLedgerSliceBootstrapper.cs:669` (New Game, NPC port built from the
development store) and `:2501` (`EnsureNotionsProcurementForRestore`, which
re-registers after a Continue restore rebuilds the business instances), plus
`SimulationDrivers.cs:10120`. The adapter sells only what the store holds
(`SellItem`, `:300`) and posts through the store cash authority.

### 2. Households legitimately know the store — HOLDS

`SimulationDrivers.SeedLocalGeneralStoreKnowledge`
(`Orchestration/Systems/SimulationDrivers.cs:3278` instance, `:3317` static)
teaches every on-map resident the local General Store, idempotently, and is
called from `BuildHouseholdShoppingLoop` (`:3074`) before any shopping runs.
Eligibility (`IsEligibleLocalGeneralStoreSupplier`, `:3297`) admits a real
business id outright and admits the `"general-store"` fallback id only when it
has a real `building:` location, closing the overnight 2026-11-04
"registered, stocked, staffed, and open, but never known" hole. Knowledge is
per-person (`ShopperSellerKnowledge.LearnSupplier` /
`Knows`, `Domains/Population/HouseholdShopping.cs`).

### 3. The store is staffed by a real clerk — HOLDS

The store definition asset
(`Assets/Core/Economy/GeneralStoreBusinessDefinition.asset:41`) defines two
worker slots, `clerk_helper` (Store Clerk, required for opening) and
`stock_helper`. The development roster is built from the configuration
(`FirstLedgerSliceBootstrapper.BuildDevelopmentOpeningRoster`, `:2010`) and
formed through `SharedBusinessRuntimeManager.EnsureOpeningBusinesses`
(`:8965`), whose formation callback calls
`result.Business.EnsureOwnerOperatorStaffing(result.Business.Owner)` (`:9000`).
`BusinessRuntimeState.TryAssignOwnerOperator` (`:3905`) puts the NPC owner
(Kennedy Baldwin-Ooms) into the first open slot; `WorkerSlotState.Assign`
(`:760`) sets `paidActive`, so `FilledWorkerCount` (`:1515`) is 1 and the
clerk resolver `GeneralStoreRuntimeManager.TryGetServiceClerkPersonId`
(`:475`) parses the `owner:{personId}` slot. `StoreTradingCapability`
(`HouseholdShopping.cs:292`) therefore reports staffed, and
`CheckTradingCapability` returns null during hours.

### 4. The store holds real, priced, identified stock — HOLDS

Opening inventory is seeded as identified retail lots, not a pooled number:
`ScenarioOpeningStockEndowment.TryApply`
(`Domains/Economy/Businesses/GeneralStore/GeneralStoreRuntimeManager.cs:6369`)
calls `BusinessRuntimeState.TryAddRetailItemLot` (`:4058`) per configured SKU
(bread 24-48, flour 8-16, potatoes 8-16, sugar, coffee, tea, salt, beef,
bacon, cheese, plus household goods). `TryAddRetailItemLot` also increments
the category pool and refuses when `CurrentStockUnits + quantity` would exceed
`TargetStockUnits`. The `staple_food` target is 306 (sum of the authored
reorder targets: flour 60, bread 30, potatoes 40, salt 48, sugar 60, coffee
50, tea 18), so the 68-136 opening units leave ample headroom and the bakery
delivery cannot be capacity-blocked. Prices are cost-plus with a floor
(`GetItemSellingPriceCents`, `:854`): bread baselines at 5c against the 4c
wholesale cost, so the transfer retail guard passes.

### 5. Bread and staples are replenished through real supply — HOLDS

Bread: the configuration authors a bakery wholesale agreement
(`DevelopmentTownConfiguration.asset`, bakery `wholesaleAgreements`: 24
loaves/day at 4c, first delivery day 0). `DriveBakeryWholesalePlanning`
(`SimulationDrivers.cs:11656`) drives `BakeryWholesaleDailyDriver.RunDay`,
which retries prior-day shelf stock (`:694`), transfers
(`Review2BakeryToStoreWholesaleDriver.TransferToShelf`, `:755`) with the
`bake.bread-loaf` to `bread` translation, and stocks into live identified
lots (`StockShelfToLive`, `:778`). Non-bread staples: the NPC restock driver
is registered with twelve regional supplier adapters
(`RegionalSupplierSet.CreateAll`,
`Domains/World/Regional/RegionalSupplierAdapter.cs:417`), and
`RegionalBusinessRegistry.All` offers flour, potatoes, salt, sugar, coffee
(Ashford-only), tea (Redfield-only), beef, bacon, and cheese. Bread comes
from the home bakery, not a regional town, as the registry documents.

### 6. Needs open from real shortages — HOLDS, with the M1 exception

`DriveHouseholdShortageMonitor` (`SimulationDrivers.cs:13335`) evaluates
every household daily against lot inventory and expected daily use
(members x serving units x meals per day). `HouseholdShortageMonitor.Evaluate`
(`Domains/Population/HouseholdShortage.cs:89`) opens a real
`HouseholdPurchasingNeed` (one open need per household+item) and never erases
a shortage for lack of a seller: `KnownSellerRecognitionPredicate` (`:138`)
only annotates recognition, and a need for an unrecognized item stays open
and observable. Needs are necessities (`IsNecessity = true`,
`HouseholdShortage.cs:29`), so no spending-policy cap defers food.

### 7. Trips are physical, and only a confirmed arrival settles — HOLDS

`DriveEmbodiedShopping` (`SimulationDrivers.cs:12775`) plans one real-time
trip per open need via `ShoppingTripDriver.PlanForHousehold`
(`Domains/Population/ShoppingTripDriver.cs:329`). `AdvanceTick` (`:398`)
departs trips at their window, `HandleLegDue` (`:563`) completes legs on due
events, and `ArriveNow` (`:719`) settles only after
`HasPhysicallyArrivedAtShoppingLocation` confirms the person is at the store
building; otherwise it schedules an arrival retry. The loop's `PlanTrip`
(`Domains/Population/HouseholdShoppingLoop.cs:2244`) chooses the shopper by
availability (never sex), reserves real person-time windows, and evaluates
candidates on stock, price, route, and trading capability
(`EvaluateCandidates`, `:3425`), rejecting an unstaffed or closed store
honestly. Employee service (`ArriveAtStoreAndSettle`, `:2656`) queues the
shopper and starts a `TendCustomer` task for a filled clerk; with no clerk
the customer settles as a no-service sale (rule (g)), never waits forever.

### 8. Settlement is conserved, every cent — HOLDS

`SettleAtCounter` (`HouseholdShoppingLoop.cs:1741`) checks stock at the
counter, debits the household ledger first (`RecordOutflow`) or creates a
real trade-credit obligation (never informal debt), then calls
`SellItem`, which consumes real store lots. A zero sale refunds the cash leg
and cancels both obligations; a partial fill refunds the undelivered goods
value (cash) or reduces the obligation principal (credit, DEF-043). The
delivery fee, when charged, posts to the carrier till, never the store.
`ReceiveSettledGoods` (`:1004`) deposits the goods into household lots only
on physical receipt (carried home or delivered) and mirrors the receipt into
the reserve; the need is fulfilled only by units actually received.

### 9. Meals consume real lots and record missed meals honestly — HOLDS

Production wires lot inventories and the meal log into the daily drive
(`SimulationDrivers.cs:7634`), so `DailyNeedsService.ExecuteDay`
(`Domains/Population/DailyNeedsService.cs:132`) runs the lot-based path.
`HouseholdMealService.ServeHouseholdDay`
(`Domains/Population/HouseholdMeals.cs:325`) serves each member in priority
order (bread, beef, bacon, cheese, preserved meat, unspecified meat,
preserved food, potatoes, flour, unspecified staple food), consumes real lots
through `HouseholdInventory.Consume`, and records a missed-meal hardship
record with the reason when no lot covers a serving. The meal log is lazily
instantiated and never null (`SimulationSystemsHub.cs:156`). `MealTickDriver`
records no meals at all (verified by search: no `RecordMeal` call in
`Domains/Population/MealTickDriver.cs`): a meal is time-reserved, never
credited by a driver executing. `HouseholdInventory.AddLot` (`:313`) refuses
unknown items, non-positive quantities, and sourceless lots, so no synthetic
stock enters a larder.

### 10. Starting food is real and one-time — HOLDS

Households open with 40,000c each (configuration `openingCashCents`) and with
reserves filled to target by population generation
(`PopulationGenerator.cs:230`, `:254`, `:441` calling
`EnsureHouseholdReservesInitialized(true)`). The first lot-based meal drive
converts those reserves into lots exactly once
(`HouseholdInventoryReserveBridge.SeedLotsFromReserves`, guarded by the
`SeededFromReserves` flag), and later receipts and consumption mirror once
each way, so no food is duplicated. The overnight "0 meals eaten" symptom was
recorded under a failed startup; under a valid startup the day-0 larder is
seeded and meals can be served before the first shopping trip lands.

### 11. Charges reconcile without double-counting — HOLDS

`HouseholdFoodChargeReconciliation`
(`Domains/Population/HouseholdFoodChargeReconciliation.cs`) records only
posted charges (cash outflow or live obligation), never voided or failed
settlements, and flags the two known overlaps (`BothPathsChargedSameWant`,
`PhaseCChargedMultipleItemsSameWant`) for the Unity evidence run. The loop
is the only live charging path when bound (the legacy executor is null in
that case), so no purchase is charged twice.

### 12. Save and Continue restore the chain — HOLDS at code level

The supplier is re-registered after a restore
(`FirstLedgerSliceBootstrapper.cs:2501`); trip custody, needs, lots, and the
meal log are save-persisted (`ShoppingTripDriver.CaptureSaveDto`,
`HouseholdNeedRegistry.ExportState`, `HouseholdInventory` state,
`SimulationSystemsHub.ExportMealLog`); restored trips rehydrate through
`ShoppingRehydrateGuard` / `RehydrateAfterLoad`. Live Continue behaviour is
a Codex Unity check, not a harness claim.

## Finding I1-M1 — BREAKS: the monitor demands one full diet per food item

`HouseholdShortageMonitor.EvaluateItem` (`HouseholdShortage.cs:210-267`)
sizes each item's need independently:

```text
unitsNeeded = ceil(max(0, targetDays - daysCovered) * dailyUseUnits)
```

where `daysCovered` is that item's own days of supply only. But
`HouseholdMealService.TryServeFromInventory` (`HouseholdMeals.cs:406`) eats
one item per serving, first available in priority order. So the needs for
different foods are substitutes, not additions, and the monitor treats them
as additions.

Reproduction (live playtest policy: Low 3 days, Target 7, Fulfilled 7; the
2-member Ethan-Tia household; 3 meals a day; serving units bread 1, beef 1,
flour 2, potatoes 2 — `HouseholdItemCatalog`, `HouseholdInventory.cs:70-72,
109`):

- Empty larder, daily use bread 6, beef 6, flour 12, potatoes 12.
- Observed before the repair: bread need 42, beef need 42, flour need 84,
  potato need 84. In servings that is 42 + 42 + 42 + 42 = 168 servings
  demanded for a household that eats 42 servings a week.
- The full purchasable set is six items (bread, beef, bacon, cheese,
  potatoes, flour), so the household demands about six times the food it
  eats, every week, from a store whose whole opening staple stock is 68-136
  units. Eight households doing this strip the store on day 1-2, the weekly
  regional restock and the 24-loaf daily bakery delivery are stripped in
  turn, and later households find empty shelves. DEF-067 already flags the
  symptom (`PhaseCChargedMultipleItemsSameWant`, mechanism M1) but the cause
  was unrepaired.

Expected: the household's total edible cover (all foods, in priority order)
is what must reach 7 days. One item's need covers only the shortfall that
remains after higher-priority foods, held or planned.

### Repair (I1-M1)

`HouseholdShortageMonitor.Evaluate` now takes two optional parameters
(existing callers compile unchanged):

- `mealPriorityOrder`: the meal-item priority list. When supplied, food
  needs are allocated in that order against one shared cover total. When
  null, evaluation is exactly today's per-item behaviour, so existing tests
  and the single-item 42-loaf pin are untouched.
- `isPurchasableNow`: whether the item can actually be bought today
  (recognized by a known seller and currently stocked). A higher-priority
  need counts toward the shared cover only when it is purchasable; an
  unpurchasable need stays open as an observable shortage but does not block
  lower-priority foods, preserving the stockout fallback. When null, a need
  counts as cover (the optimistic assumption).

The shared cover is `totalHeldDays` (every priority food already in the
larder, because meals eat any priority food, first available in priority
order) plus `plannedDays` (days higher-priority needs will add when bought).
A top food therefore never demands a full target on top of a larder already
stocked with a lower-priority food: 7 days of beef held means no bread need
opens at all. Fuel is evaluated outside the food pass and is never counted
in food cover, and food never counts toward fuel. The production driver
(`DriveHouseholdShortageMonitor`) passes `policy.MealItemPriority` and a
purchasability predicate built the same way as the existing recognition
predicate (known seller plus `ItemStockUnits(itemId) > 0`).

Regression tests: `Tests/Editor/Population/HouseholdShortageAllocationTests.cs`
- empty larder, bread and beef both purchasable: bread need 42, no beef need.
- bread unpurchasable: bread need 42 stays open, beef need 42 opens (fallback).
- 3 days of bread held: bread need 24, no beef need.
- 7 days of bread held: bread need fulfilled, no beef need.
- 7 days of beef held: no bread need opens, no beef need opens.
- priority order respected: beef first means beef need 42, no bread need.
- chained fallback: bread unpurchasable, beef fills the cover, no flour need.
- no priority order passed: today's per-item behaviour is preserved.

## Other findings

### F1 — NPC port docstring overclaims (doc fix, no behaviour change)

`BuildHouseholdTradingPort(BusinessInstanceState)`
(`GeneralStoreRuntimeManager.cs:623`) documents that items are included
"only when ... this Business has identified stock for that SKU", but the code
(`:683-733`) includes every validated item regardless of stock. Behaviour is
still honest: `OffersItem` requires stock above zero and `EvaluateCandidates`
rejects zero stock. The docstring should be corrected to match the code; no
code change is needed here.

### F2 — Off-map members still eat from the household larder (open, small)

`ServeHouseholdDay` serves every listed member with no location or off-map
check, so a person away on a multi-day freight trip still eats from home
stores. The minimal repair is not obvious: trail rations are not modelled,
and silently skipping the meal would misstate nutrition either way. Left
open for an owner decision; not repaired in this lane.

### F3 — `StartingHouseholdEconomy` has no production callers (dead seeder)

`Domains/Population/StartingHouseholdEconomy.cs` seeds coherent starting
economics (cash, pantry lots, debt) but nothing in production calls it; the
live start is reserves-at-target plus the one-time lot seeding (verdict 10).
Left in place; removing it is out of scope for this lane.

### F4 — The store is closed on Sundays, by design

`StoreTradingHours.Default` (`HouseholdShopping.cs:255`) opens Monday to
Saturday, 08:00-18:00. A trip planned for a Sunday fails at evaluation with
"closed: day-of-week index 6 is not a trading day" and the need stays open
for Monday. Expected behaviour, not a defect.

### F5 — NPC store lookup hardcodes owner person id 1 (brittle, works today)

`FindDevelopmentNpcGeneralStore`
(`FirstLedgerSliceBootstrapper.cs:1556`) requires
`business.Owner.PersonId == 1`. Kennedy Baldwin-Ooms resolves to 1 today
(first household member in configuration order), so the notions/feed
registration succeeds, but any roster reorder silently breaks bakery notions
supply and therefore bread. Worth a lookup by business key instead; not
repaired here because it is outside the household chain's own authorities.

### F6 — Physical arrival depends on Unity pathing (NOT UNITY VERIFIED)

`TryStartShoppingPhysicalRoute` /
`HasPhysicallyArrivedAtShoppingLocation`
(`SimulationDrivers.cs:13195`, `:13224`) delegate to
`PopulationPathingDirector`, which the Roslyn harness cannot exercise. The
code reads correctly (route start, arrival gate, retry, cancel), but whether
a shopper visibly reaches the store and the leg due fires can only be settled
by the Codex Unity checklist (startup, population/pathing, shopping/meals
sections).

### F7 — `HouseholdErrandCoordinator` is hub-held but never driven

`SimulationSystemsHub.cs:1364` exposes it, but no production caller uses it.
Dead path, not a break; the live errand path is the shopping loop plus the
trip driver.

## Harness evidence

- Baseline in the I1 worktree (before the repair): 5029 passed / 8 failed /
  5037 total; FAIL set byte-identical to
  `/home/hatch/ll-ubc-int-fails2.txt` (the 8 S3-owned failures). Log:
  `/home/hatch/ll-ubc-i1-run-baseline.log`.
- After the repair: 5037 passed / 8 failed / 5045 total (the 8 new
  allocation tests, all passing; the 42-loaf pin still passes). FAIL set
  byte-identical to `/home/hatch/ll-ubc-int-fails2.txt` — zero new failures.
  Logs: build `/home/hatch/ll-ubc-i1-build-repair2.log`, new-test subset
  `/home/hatch/ll-ubc-i1-run-allocation2.log`, full run
  `/home/hatch/ll-ubc-i1-run-repair2.log` (exit 1 only because the 8 known
  failures remain).

## What this audit does not claim

- No Unity verification: PlayMode startup, visible pathing, live shopping,
  and the 60-day run remain Codex checklist items.
- No claim about the overnight "0 meals / 0 trips / 0 charges" symptom
  beyond this: under a valid startup the chain is wired (verdicts 1-12), and
  the one confirmed break that would reproduce empty shelves and missed
  meals at scale is I1-M1, not a registration or staffing gap.
