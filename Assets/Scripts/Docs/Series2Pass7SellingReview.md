# Series 2 Pass 7 — Selling Review

Base: `muse/series2-pass6-bread-drivers` at `7dfebac9ee85403482ef8adf4e2ee6c581b20814`
(Series 2 Pass 6). Branch: `muse/series2-pass7-selling`.
Roslyn harness only. No Unity compilation, Test Runner, PlayMode, or
scene wiring is claimed anywhere in this pass.

## 1. What was built

- `Domains/Economy/Businesses/GeneralStore/Series2TradingPortBuilder.cs`
  — per-item offers for multi-item categories, validated through
  Pass3PerItemOffer against a transient validation-only catalog
  (built, checked, discarded; it creates no stock and sets no price).
  Staple_food now yields seven one-item offers (flour, bread,
  potatoes, salt, sugar, coffee, tea). Items that are not household
  items are never offered and never offered under a representative
  id (beef/bacon/cheese are NOT preserved_meat; cloth/workwear/etc.
  are NOT clothing_textiles).
- `Domains/Economy/Businesses/GeneralStore/GeneralStoreRuntimeManager.cs`
  (edited, targeted) — `BuildHouseholdTradingPort` now uses the
  builder for multi-item categories. The single-item branch is
  UNCHANGED code: a category with exactly one authored item keeps
  the legacy mapping check and the legacy single offer shape.
  `TryResolveSingleItemForCategory` is kept for that branch.
- `Domains/Economy/Businesses/Bakery/Series2BakerySupplierAdapter.cs`
  — bakery as an `IItemResolvingSupplier` over the real
  `BakeryShopRuntime` shelf. Households buy household item "bread",
  mapped here (and only here) to shop product "bake.bread-loaf".
  Rolls/pies are NOT offered (no household item for them). Sales are
  condition-homogeneous only: the quoted price is the oldest
  sellable lot's price, and a sale caps to that oldest baked day's
  units, so fresh and day-old lots never mix at two prices inside
  one loop settlement. DayIndex must be set by the day driver
  before shopping; until it is set, trading refuses rather than
  pricing at a stale day. Credit is off by default, capped when
  enabled, and checked BEFORE any shelf stock moves.
- `Domains/Economy/Businesses/FuelDealer/Series2FuelDealerSupplierAdapter.cs`
  (new FuelDealer folder + folder `.meta`) — fuel dealer as an
  `IItemResolvingSupplier` over the real `FuelYardByproductIntake`
  (Pass 2 dispense API: FIFO with provenance, shortfall dispenses
  NOTHING, emptied lots removed). Households buy "fuel_wood".
  Price per unit is caller-supplied; intake-units-per-household-unit
  defaults to 1 and is a caller parameter, NOT a decided conversion.
  This adapter does NOT create a Fuel Dealer runtime (Pass 8 owns
  that); it is the selling surface over intake lots that exist.
- `Domains/Economy/Ordering/Series2WholesaleSettlement.cs` —
  settlement for one Pass 3 wholesale record, plus the back-to-shelf
  move. Cash: payer debited, payee credited, same amount, through
  business cash ports; insufficient payer cash refuses with nothing
  moved; a payee credit failure refunds the payer debit; settling
  an already-Settled record refuses (no double payment). Credit:
  no cash moves; the record settles only when the trade-credit port
  returns a real obligation id. Shelf move: Pass 3 catalog
  back -> shelf, capacity-checked, returned with the Pass 2 task
  descriptor (gs.move-back-to-shelf, 2 min/lot) that Codex issues
  through TaskAuthority — this pass performs the stock move, not
  the person-time. A retail-price guard refuses shelf sale while a
  line still prices at its wholesale cost; the owner must set a
  retail price/margin first (Pass 3 instant decision).
- `Tests/Editor/Economy/GeneralStoreItemOfferIdentityTests.cs`
  (edited) — the Unity port test's invariant is restated for the
  per-item world (see section 4). It is NOT run in this pass
  (Unity Test Runner is Codex work); the Roslyn tests below are.
- `Tests/Editor/Economy/Series2Pass7SellingTests.cs` — Roslyn
  acceptance, production classes as the system under test (bakery
  shelf from a real Pass 6 bake, fuel from real intake lots with
  provenance, store stock through the adapter's real consume path).
  Result: **16 passed, 0 failed** (harness-pass7). Pass 6's suite
  re-run in the same harness: **8 passed, 0 failed** (no regression).

## 2. Corrections and caveats, stated plainly

- BRIEF CORRECTION (wholesale cash authority): the Pass 7 brief said
  wholesale cash moves "ONLY through HouseholdLedger". That is
  wrong for business-to-business cash, and this pass does not do
  it: HouseholdLedger records HOUSEHOLD cash only (one ledger per
  household, inflows requiring a household income source). A
  store-to-bakery payment booked into a household ledger would
  invent a household party to a business payment. Business cash
  therefore moves through IBusinessCashPort (production:
  BusinessRuntimeState, the same port Pass 6 used), household cash
  through HouseholdLedger (in the shopping loop, which already does
  that), and debt only through FinancialObligationAuthority. No
  cash moves anywhere else in this pass.
- SHARED-POOL CAVEAT (store per-item offers): BusinessRuntimeState
  holds ONE aggregate count per category, not one count per item.
  Every per-item offer in a category draws from the same pool.
  Per-item stock counts therefore overstate each item taken alone,
  but total units actually sold across all items in a category can
  never exceed the pool, because every sale consumes pool units.
  Item-level stock is a save-shape migration and a Kennedy/Codex
  decision, NOT claimed here. No offer in this pass may be read as
  item-level stock.
- BAKERY DAY CAVEAT: the bakery adapter's quoted price depends on
  the driver-set DayIndex. Codex must set it at day start before
  any shopping; the adapter refuses trading until it is set, and
  refuses a sale whose sale-day price disagrees with its quote.
  In normal operation (day set correctly) quote and sale agree.
  REVIEW 2 PASS A CLARIFICATION (no code change): `CheckTradingCapability`
  ignores its `absoluteDayIndex` argument — it refuses only while
  the adapter's own `DayIndex` is unset (plus any caller trading
  check). Day equality is NOT enforced by the adapter; only a
  price disagreement refuses a sale. A stale DayIndex left set
  from a previous day therefore does not, by itself, stop
  trading. This is pinned by `Review2PassAGeneralTests`
  (`BakeryAdapter_CheckTradingCapability_IgnoresDayArgument_RefusesOnlyWhenUnset`
  and `BakeryAdapter_SaleDayPriceGuard_RefusesOnPriceDifference_StockIntact`);
  setting DayIndex at every day start remains a Codex binding
  obligation, and a day-equality guard, if wanted, is a later
  decision, not something this pass adds silently.
- WHOLESALE PERSISTENCE CAVEAT: intake duplicates are guarded per
  instance and double settlement per record, but delivered lot ids
  are NOT yet persisted across saves. Pass 9 must adopt that set
  before this path runs in production, or a reload could re-intake
  a lot. Stated, not hidden.
- FUEL CONVERSION CAVEAT: 1 intake unit = 1 household fuel_wood
  unit is the adapter's default parameter, not a decided game rate.
  The bundle-to-reserve conversion is a Pass 8 decision.

## 3. Deliberately NOT built (Pass 7 scope held)

- No `FirstLedgerSliceBootstrapper` edit: adapter registration in
  `SupplierDirectory` is Codex's hook-up (RegisterIn calls exist
  and are tested, but nothing registers them in production here).
- No `.asset` change. No Main Scene / prefab / Inspector change.
- No Pass 6 driver change: Pass 6 classes are consumed (bake flow
  in tests), never rewritten.
- No freight carrier code (Pass 8), no fixture authoring (BLOCKED
  on Kennedy inputs — tests use generic single-person inputs and
  never invent Ethan/Tia/Shane names, ages, cash amounts, business
  names, horses, or wagons), no player binding, no save adoption
  beyond the wholesale guards above, no town gates, no seconds or
  sub-minute work, no WorkTimeBudget change, no retail-price canon
  (all prices in tests are caller parameters).
- No shopping-loop change: the loop is consumed through its
  existing `IItemResolvingSupplier` contract. The loop's
  uniform-price settlement is why the bakery adapter sells
  condition-homogeneous batches only (section 1).

## 4. Legacy test update, and why it is safe

The old Unity port test asserted staple_food is NEVER offered and
that every offer's category holds exactly one authored item. That
assertion was the guard against selling a 7-item aggregate as
"flour". Pass 7 removes the skip but keeps the guard's intent in
per-item form: every offer still carries exactly one item, every
offered item is an authored item in its category AND a real
household item, and no multi-item category is offered under a
representative id. The updated test asserts exactly that, plus
that staple_food offers all seven items and that meat/clothing
stay un-offered. Single-item categories keep the legacy branch
code unchanged, and the Roslyn single-item adapter test proves the
adapter behaviour for a one-offer store is unchanged. Unity Test
Runner confirmation of the updated test is Codex work, NOT RUN.

## 5. What Codex must hook up (in order)

1. Register the bakery and fuel adapters in `SupplierDirectory`
   from the bootstrapper (`Series2BakerySupplierAdapter.RegisterIn`,
   `Series2FuelDealerSupplierAdapter.RegisterIn`), with real
   bakery/fuel cash ports (BusinessRuntimeState), real trading
   checks (open/staffed), and — for the bakery — the day driver
   setting `DayIndex` at day start.
2. Confirm the updated Unity port test passes in Test Runner and
   that a household shopping-loop purchase of flour and of bread
   as distinct items completes in Main Scene (the Roslyn tests
   prove the adapter/builder logic, not the scene wiring).
3. Wire wholesale settlement + shelf-move calls into the bakery
   delivery flow (delivered lot -> intake -> settle -> retail price
   decision -> back-to-shelf task via TaskAuthority -> shelf sale),
   and adopt delivered lot ids into save/load (Pass 9) before this
   path runs in production.
4. Map dealer and bakery retail prices from the Pass 3 margin
   model (caller parameters in this pass) — no price in this pass
   is canon.

## 6. Open Kennedy decisions touched by this pass (unchanged, none guessed)

Store category shape is now PARTIALLY decided in code (per-item
offers over the shared pool) but item-level stock is still open;
flour unit semantics, wholesale-vs-retail prices, fuel
bundle-to-reserve conversion, dealer vs mine coal retail, and all
prices/rates remain test parameters or caller inputs, never canon.
