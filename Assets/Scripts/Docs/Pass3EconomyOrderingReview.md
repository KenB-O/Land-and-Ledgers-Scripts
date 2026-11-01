# Pass 3 — Economy Ordering: Review and Handoff

Pass 3 of the new series. Base: `muse/pass2-task-system` at
`b947a78382328d0cbb4a914380e41a76e5bf92e5` (Pass 2, task system).
Branch for this pass: `muse/pass3-economy-ordering` (SHA and push
confirmation are in the pass report, not in this file, because they
change at push time).

Governing docs: Documentation v3.0, Canonical Project Bible Revision
XXXI and Technical Implementation Bible Revision XIV, both dated
2026-10-07. Proposals 001-105 are approved; 106-145 are pending and
were NOT promoted here.

## 0. What this pass is, and what it is not

Pass 3 ONLY: economy ordering, source-level. It does NOT start
player depth or town growth / immigration (Kennedy's order is task
(done, Pass 2), then economy (this pass), then player, then town).
It does NOT implement housing heating alternatives: a read-only
heating audit was still running in parallel when this pass was
briefed, its results are not in yet, so heating alternatives are
noted as PENDING below, and no fuel type, stove type, or heating
consumption rate is guessed anywhere in this pass.

It does NOT claim Unity compilation, Unity Test Runner, PlayMode,
scene wiring, prefab wiring, or Inspector values: no Unity run
happened in this pass, and none is claimed. Everything verified
here was verified in a small Roslyn harness (section 10). Unity AI
owns first-compilation busy work. Codex owns hard problem solving
and final integration. Neither prompt is written here; ChatGPT
(manager) writes those from the parent's summary of this pass.

It does NOT repeat Pass 1 or Pass 2 work, and it does NOT rewrite
functioning systems unnecessarily. Existing procurement, freight,
and retail systems are reused, not replaced; see section 2.

## 1. Surviving decisions (recorded, not guessed as canon)

- **Player ordering is never automatic.** Pass3OrderingSession and
  Pass3OrderDraft both refuse Automatic + Player, and Automatic +
  EmployeeDelegate without delegation permission. Employee delegation
  of purchasing/reordering is allowed; player ordering must be a
  deliberate player action. This is a guard, not a preference: an
  automatic player order is refused, never silently downgraded.
- **Shelf vs back are different places.** Stock lands in back on
  intake; it reaches the shelf only through a shelf/back move,
  which Pass 2 made a task (gs.move-back-to-shelf, 2 min/lot).
  This pass records the move and enforces shelf/back capacity;
  it does not perform person-time and does not claim a driver
  issued the move task.
- **Margin and price are the same action.** Setting a margin
  percent, setting a margin amount, and setting a price all end in
  one price in cents, rounded to the nearest cent. Pass 2 decided
  price/margin setting is an instant decision, not a task; this
  pass honours that and does not double-count it as labour.
- **Minimums are refusals, never silent round-ups.** An order below
  a supplier's minimum amount, quantity, or poundage is refused
  with every violated minimum named. No goods are added and no
  extra charge is made to reach a minimum.
- **Grouping inclines, never hard-locks.** A multi-supplier order
  gets a consolidation note suggesting a single supplier; it is
  NOT refused. Plausible groupings (tomatoes/potatoes/carrots;
  cigars/cigarettes; shirts/pants and possibly shoes, since a
  shirt/pants maker totally could make boots; crackers vs
  boots/shoes as different suppliers) are supplier data, not code
  rules, and no category is hard-locked to a supplier.
- **Fuel input is generic.** The Fuel Dealer off-map input SKU is
  caller-supplied and never defaulted. "Firewood" is Kennedy's
  guess for the dealer's output, not a decided input, and no file
  in this pass hardcodes logs, timber, slabs, or cordwood as the
  input. Conversion rate, prices, and quantities are test/author
  parameters, never canon.
- **Formation rule preserved (Pass 1).** A newly formed business
  begins with zero magically generated inventory/cash; adding a
  catalog line here creates zero stock. Existing scenario
  businesses may begin with documented endowments with identity
  and provenance; that endowment is not created by this pass, and
  no test seed amount in this pass is canon.

## 2. What already existed, and what this pass reused

- OffMapSupplierCatalog: supplier choice, distance, and freight
  quotes (SelfCollect / SellerFreight / FreightBusiness), sorted
  by delivered total. Reused as the quote concept; this pass adds
  a multi-product draft on top of it, it does not replace it.
- OffMapNamedSkuProcurementService + ExternalMarketAuthority:
  the ONE procurement path for off-map orders (one SKU per
  procurement order, freight as an explicit surcharge, CashOnArrival
  and TradeCredit payment modes, duplicate-receipt protection).
  Pass3OrderRequest is the validated request a caller maps onto
  that service, line by line, with allocated freight as the
  surcharge on that line. No second procurement authority exists
  in this pass.
- Pass 2 task catalogs: gs.order-from-supplier (the labour of
  ordering), gs.move-shelf-to-back / gs.move-back-to-shelf
  (2 min/lot), gs.choose-supplier and gs.set-price-margin
  (instant decisions). This pass's model is consistent with those
  decisions and does not re-decide them.
- Pass 2 bakery transfer (ReceiveTransferredBreadLot, baked-day
  aging, delivered-mark set) and fuel dispense API
  (FuelYardByproductIntake.TryDispenseUnits, FIFO, shortfall
  refuses whole). Wholesale intake in this pass uses the same
  baked-day aging rule and the same duplicate-refusal principle.
- Pass 1 equipment purchase / procurement authorities and the
  GenericBusiness templates (General Store, Bakery, Fuel Dealer,
  Barber). Fuel Dealer input ordering and horse/wagon pairing
  below are built to be fed through those authorities, not around
  them.

## 3. Player ordering model (Pass3OrderingModel.cs)

- Pass3ProductLine: ProductId, DisplayName, CostCents, PriceCents,
  ShelfUnits, BackUnits, ShelfCapacityUnits, BackCapacityUnits
  (0 = no explicit cap set by the caller, never an invented cap).
  MarkupTotalCents = price - cost; MarkupPercent on cost, zero
  when cost is zero or less because a percent on no cost is
  undefined, not zero margin.
- SetPrice / SetMarginPercent / SetMarginAmount: one action, one
  price, nearest-cent rounding. Negative prices refused; margin
  percent refused when cost is zero or less.
- MoveToShelf / MoveToBack: refuse bad quantities, insufficient
  source stock, or a capacity breach, moving nothing. Shelf
  capacity is enforced here; back capacity is enforced the same
  way when a caller sets one.
- Pass3StoreCatalog: AddProduct (zero stock, duplicate refused),
  RemoveProduct (refused while any shelf or back stock remains,
  so removal can never silently destroy stock), Find, Lines.
- Pass3OrderingSession: BusinessId, Actor (Player /
  EmployeeDelegate), IsAutomatic, DelegationAllowed, and
  ValidateAutomatic() implementing the player-never-automatic
  guard above.

## 4. Supplier orders (Pass3SupplierOrder.cs)

- Pass3SupplierOffer: SupplierId, Name, DistanceMiles, IsPickupOnly,
  MinimumAmountCents, MinimumQuantityUnits, MinimumPoundageLb,
  and per-SKU offers (SkuId, UnitPriceCents, UnitWeightLb).
  Minimums of 0 mean that minimum is not set.
- Pass3OrderDraft: one supplier, one or more lines, one freight
  option, freight cents-per-mile, freight-business commission,
  actor, automatic flag, delegation flag.
  - AddLine: at the supplier's current offer price; refuses
    unknown SKUs, non-positive quantities, and duplicate SKU lines
    (combine deliberately, do not silently merge).
  - Freight: SellerFreight = distance x cents-per-mile, explicit
    surcharge; SelfCollect = 0, RequiresJourney true, status
    AwaitingCollection, never delivered by this model;
    FreightBusiness = seller freight + caller commission.
    Pickup-only suppliers refuse any non-SelfCollect option.
  - Validate: supplier, lines, freight option, pickup-only rule,
    automatic guard, and all three minimums, every failure named.
  - Place: validated requests only, one per line, freight
    allocated proportionally to goods with integer rounding
    absorbed by the last line, so allocations sum EXACTLY to the
    draft freight total.
- Reorder vs new order: ReorderFrom copies a previous order's
  supplier, SKUs, and quantities, then RE-QUOTES every line at
  current offer prices. A price change is visible; a line the
  supplier no longer offers blocks the reorder entirely. A new
  order starts from a fresh supplier choice, not from a previous
  order's prices.
- Pass3SupplierGrouping.ConsolidationNote: single-supplier vs
  multi-supplier guidance, never a refusal.
- Pass3PriceRandomiser.VariedPriceCents: deterministic on a seed
  string (same seed, same price), within +/- variance, never
  negative. Variance is a caller/test parameter, never canon.
  Randomisation affects quotes only; it never changes a placed
  order's price after placement.
- Off-map vs on-map: this model is location-agnostic. A supplier
  carries a distance; an on-map supplier is simply a supplier
  with distance 0 and seller-freight 0. No on-map supplier is
  faked into existence here, and no code path assumes off-map.

## 5. Fuel Dealer off-map input (Pass3FuelDealerInput.cs)

- Pass3FuelInputOrder.Create: dealer id, supplier, a GENERIC input
  SKU id supplied by the caller, quantity, freight option, and a
  caller conversion rate. An empty or missing input SKU is
  refused outright, because the raw input type is still open and
  must be supplied, never defaulted.
- Place: through the normal Pass3OrderDraft validation, so
  minimums, pickup-only, freight, and the automatic guard all
  apply to fuel input exactly as to any other order.
- Pass3FuelInputConversion.Convert: input units x conversion
  rate, floored, so fractional input never conjures a whole fuel
  unit. Refused unless the input order is actually RECEIVED:
  an unreceived order converts nothing. The output record
  (Pass3FuelOutputLot) carries supplier, order, input lot, input
  units, fuel units, and provenance, and is a PLAN, not stock:
  a driver must still move those units into a real fuel stock
  through the Pass 2 dispense/feed APIs and business cash ports
  before they can be sold or burned. No free assets are created
  by selecting a capability, and no equipment, horse, or wagon is
  granted here; those are bought through the Pass 1 equipment
  purchase / procurement authorities with real vendors, prices,
  payments, deliveries, and asset ids.
- Pass3TransportPair: HorseAssetId + WagonAssetId + OwnerBusinessId.
  Validate refuses a missing horse, a missing wagon, or the same
  id in both fields. PairDisplayId ("horse+wagon") is a display
  string only, never a merged asset id. Horse and wagon remain
  distinct owned assets even when operated together.

## 6. Bakery bread into General Store stock (Pass3WholesaleIntake.cs)

- Pass3WholesaleIntake.Receive: bakery lot id, product id, units,
  wholesale price, baked day, transfer day, store catalog.
  - Lands in BACK storage only, never straight onto the shelf.
    Shelf availability requires a Pass 2 back-to-shelf move.
  - Condition from baked day vs transfer day: same day = Fresh,
    next day = DayOld, more than one day past baked day = STALE,
    refused, not converted and not silently dropped. This matches
    Pass 2's staged-transfer aging rule.
  - Duplicate lot ids refused per intake instance, so re-running
    an intake can never double-count bread. Production persistence
    of delivered lot ids across saves (like the Pass XL
    delivered-sale set) is a Codex wiring task, listed in section 9.
  - A missing catalog line is created with cost = wholesale price
    and price initially = wholesale price; the owner must set a
    retail price/margin via an instant decision before any shelf
    sale. Wholesale price is NOT silently treated as retail price.
  - Cash is NOT moved here. The returned settlement record (store
    pays bakery, units x wholesale price, Settled = false) must be
    settled by a caller through the business cash ports. Until
    then, stock and cash are both visible and unsettled, which is
    honest; claiming cash moved here would invent a payment.
- Pass3PerItemOffer.OfferForItem: given a category's item list and
  a store catalog, returns exactly that item's line, or a refusal
  when the item is not in the category or has no catalog line.
  It never returns an aggregate quantity labelled as a single
  item, which is the exact error the single-item category guard
  was built to prevent. See section 8 for why this is a model,
  not yet a production fix.

## 7. Unity AI mapping (source-level spec, NOT Unity work)

- Pass3StoreCatalog -> store UI product list: each line is one
  row, showing cost, price, markup percent AND markup total,
  shelf units, back units, and shelf capacity. Add/remove product
  buttons map to AddProduct / RemoveProduct, with removal refused
  while stock remains (show the stock, do not hide it).
- Price/margin editing -> one control: changing price, margin
  percent, or margin amount all call the same SetPrice path and
  update all three displays. This is an instant decision, not a
  task, and must not be shown as labour or booked as person-time.
- Shelf/back display -> two pools per product, with move buttons
  mapping to MoveToShelf / MoveToBack. A move button must request
  a Pass 2 move task from a driver; the model alone does not move
  a person, and the UI must not teleport stock without a task.
- Order screen -> "Place an order" is the core action, mapping to
  Pass3OrderDraft.Place. Reorder-from-same-supplier is a separate
  button mapping to ReorderFrom, showing re-quoted prices before
  confirmation. A new order starts from a fresh supplier choice.
  Supplier quotes must show goods, freight (as an explicit
  surcharge, with the freight option named), delivered total,
  and delivered unit price, sorted by delivered total, never by
  goods price alone. Pickup-only suppliers must show SelfCollect
  only, with the journey requirement visible.
- Minimums -> shown on the supplier/order screen, and enforced as
  refusals with the violated minimum named. The UI must never
  silently round an order up to a minimum.
- Automatic ordering -> must not be offered for the player at all.
  A delegate automatic-reorder toggle may be offered only where
  delegation is granted, mapping to DelegationAllowed.
- Fuel Dealer input screen -> same order screen, with the input
  SKU as a caller/author field, not a hardcoded product. The UI
  must not present "firewood" as the input type; it may present
  the dealer's OUTPUT as fuel, with the input type left to
  authoring until Kennedy decides it.
- Wholesale intake -> a driver/adapter call, not a UI button that
  conjures stock. The UI may show an unsettled settlement record
  (store owes bakery X) until a caller confirms payment.

## 8. Economy-side chain breaks: what this pass closed, and what it did not

CLOSED AS A MODEL (tested, but NOT wired into production drivers):
- Wholesale intake model for bakery bread into General Store
  back stock, with baked-day aging, duplicate refusal, and an
  unsettled settlement record (section 6).
- Per-item offer model for multi-item categories, so a category
  like staple_food can be offered item by item without mislabelling
  an aggregate as flour or bread (section 6).

NOT CLOSED, and NOT claimed as closed (Codex / driver / Unity work):
- Bakery WorkDay driver: no production driver calls PlanBatch /
  WorkDay. This pass does not author one.
- Flour order / arrival driver: no production driver calls
  PlaceOrder / ReceiveArrivals on schedule, and
  SaveLoadManager.PassXlProcurementService still has no production
  setter found. This pass does not author one.
- Store-to-bakery feed driver: the feed remains orchestrator-only
  (ThreeBusinessSlice.FeedBakeryFromStore). This pass does not
  author a production feed.
- Fuel production driver: fuel input ordering is modelled here,
  but no driver runs fell / haul / split / stack / dispense as a
  production chain, and no driver feeds converted fuel input into
  a real fuel stock. The Pass 2 dispense API exists; its driver
  does not.
- Supplier adapters in SupplierDirectory: this pass does NOT
  author a BakerySupplierAdapter or a FuelDealerSupplierAdapter.
  The only production supplier registration remains the General
  Store adapter. Households therefore still cannot buy bread from
  the bakery or fuel from the dealer through the shopping loop
  in production, and that fact is NOT hidden by the models above.
- Store staple_food shopping-loop block: Pass3PerItemOffer is a
  model only. GeneralStoreRuntimeManager.BuildHouseholdTradingPort
  was NOT edited in this pass, deliberately, to avoid breaking
  its legacy callers and tests. Until Codex wires a production
  adapter to the per-item model, the production shopping loop
  still skips staple_food, and store-bought flour/bread through
  that loop remains blocked.
- Daily tick driver, scene wiring, prefab wiring, Inspector
  values, and any Main Scene / ProjectSettings asset: none of
  these are in this pass, and none are claimed.

## 9. Codex / Unity AI only (do not treat as done)

- Production drivers: bakery WorkDay, flour order/arrival,
  store-to-bakery feed, fuel production chain, wholesale intake
  settlement + shelf moves, and supplier adapter registration
  (Bakery, Fuel Dealer) in SimulationDrivers / FirstLedgerSliceBootstrapper.
- Persistence: delivered wholesale lot ids across saves;
  PassXlProcurementService production registration (F19, still open).
- Adapter switch: GeneralStore trading port from single-item
  category guard to Pass3PerItemOffer, with regression tests for
  legacy General Store operations.
- Unity AI: first-compilation console check, namespace / Unity
  issues, and the UI exposure mapped in section 7. No UI, scene,
  or prefab work is in this branch.
- Unity Test Runner: the ordering model is plain C# and fully
  Roslyn-testable (section 10); there is deliberately NO
  Unity-only test file in this pass, because a Unity-only file
  that only re-asserts plain-C# model behaviour would be a fake
  Unity test. The Unity work to verify is UI binding and driver
  wiring, which must be tested in Unity by Codex / Unity AI, not
  by a Roslyn-only stub pretending to be a Unity test.

## 10. Tests and verification

- Roslyn harness (small harness, direct DLL run, NOT Unity):
  harness-pass3.csproj in ~/workspace/land_and_ledgers/roslyn-harness/
  (NUnit shim + UnityEngine shim + Runner + the 4 new Ordering
  files + the test file only). The full-repo Roslyn harness was
  NOT used for this pass, and no earlier test was re-run here.
- File: Tests/Editor/Economy/Pass3EconomyOrderingTests.cs
  (18 tests, all passing at the end of this pass).
- Coverage maps to the brief: margin percent/amount/price as one
  action with nearest-cent rounding; shelf/back separate pools
  with capacity and refusal; catalog add/duplicate/remove with
  stock; player automatic refused, delegate needs delegation;
  minimums (amount, quantity, poundage) refuse whole, never round
  up; pickup-only refuses seller/third-party freight; freight
  seller/self-collect/third-party with exact allocation sums;
  unknown SKU, duplicate line, empty draft refused; reorder
  same supplier re-quoted, blocked when SKU gone; multi-supplier
  grouping as guidance, not refusal; deterministic price
  randomiser within band, never negative; fuel input generic SKU,
  conversion refuses unreceived, converts received with
  provenance; horse/wagon distinct assets; wholesale intake
  back-only with unsettled settlement; day-old/stale/duplicate
  refused; per-item offer returns one item, never the aggregate.
- GUID check: all new .meta GUIDs (Ordering folder + 4 model
  files + test file) are fresh, and a combined check of tracked
  .meta GUIDs plus new .meta GUIDs found no duplicates.
- NOT verified here: Unity compilation, Unity Test Runner,
  PlayMode, scene wiring, prefab wiring, Inspector values, and
  any production driver behaviour. None of those are claimed.

## 11. Kennedy decisions (separated from code facts)

- Seconds: small vs full migration remains OPEN (Pass 2). Nothing
  in this pass depends on it, and nothing here guesses it.
- Fuel input type: logs, timber, slabs, cordwood, or other?
  Still open. The model takes a caller SKU and refuses an empty
  one; it does not answer the question.
- Fuel conversion rate: input units to fuel units. Caller/test
  parameter here, not canon, and not a production rate until
  Kennedy sets one.
- Heating alternatives: PENDING, read-only audit running in
  parallel, results not in yet. No fuel type, stove type, or
  heating consumption rate is decided or modelled here.
- Prices, freight rates, minimums, and randomisation variance:
  all caller/test parameters here, never canon. A production
  freight rate, supplier price list, and minimum schedule still
  need Kennedy's approval before any driver wires them fixed.
- Store category shape: single-item categories (asset split) vs
  per-item offers (this pass's model). The model exists; the
  production choice and the adapter switch are still open.
- Bakery model: shop runtime is the surviving model (Pass 2).
  Staged lots reach customers only through the Pass 2 transfer.
- First-cycle seed: profile starting cash/stock vs owner equity
  vs TradeCredit. Scene currently seeds none of it (Unity
  verification, Pass XL); the choice determines whether a fixed
  driver can start from an empty yard and empty bakery.
- Freight realism: abstract surcharge arrival (current procurement
  model, works in code) vs a real wagon journey for flour and
  fuel input. The surcharge version is modelled here; the wagon
  version is a later scope decision, not faked here.
- Wholesale price vs retail price: wholesale intake sets price
  initially to wholesale price, with an owner price/margin
  decision required before shelf sale. That decision is the
  owner's, not a default markup invented here.
