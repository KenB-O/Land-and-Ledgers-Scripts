# Pass XL Phase 8: Three-Business Slice Economy

Branch: `muse/passxl-slice-phase8`, chained from `muse/passxl-slice-phase7`
at `835548e164acbe280e2aec44befc6f84fa3f8220`. No main merge, no
integration push, no force-push, no history rewrite. Roslyn harness only;
nothing here claims Unity compilation, Test Runner, PlayMode, scene,
prefab, or Inspector wiring.

All money, price, wage, draw, freight, and quantity numbers in this
document and in the phase 8 tests are caller-supplied test parameters
only, never canon, unless a number is explicitly identified as an
existing production default (e.g. the bakery price schedule, which is
production code, not a test input).

## 1. What the three-business run proved

File: `Orchestration/Player/ThreeBusinessSlice.cs`.
Test: `Tests/Editor/Population/PassXlPhase8ThreeBusinessTests.cs`,
`Run_EndToEnd_ThreeBusinessesProduceSellAndFeed_AllHouseholdsEat_CashReconciles`.

The run extends the phase 6 / phase 7 composed runs. Same six people,
three households, three businesses. Parameters (test only): store seed
5000c, bakery seed 2000c, fuel seed 1000c, reserves 1000c / 1000c / 500c,
order 100u flour at 10c plus 250c seller-paid freight = 1250c, market
supply 200u, transit 3 days, arrival day 3.

### Fuel Dealer (Jacob Boniecki) — PRODUCED AND SOLD

- Stand `stand-slice-1`, white pine, 10 logs, landing `landing-1`, timber
  rights granted to `boniecki-fuel-dealer-1` by `town-land-office`.
- Jacob felled 4 logs, hauled them landing-1 to mill-1-yard (2 miles,
  river road), mill sawed 4 logs, byproduct fuel 8 units (4 logs x 2
  fuel units per log for white pine), transferred as 1 lot to the
  `boniecki-fuel-dealer-1` intake. Fuel produced: 8 units.
- Sold 2u to the bakery at 5c = 10c, and 4u to the Baldwin-Ooms household
  at 10c = 40c. Intake remaining at end of run: 2 units.
- The sale bridge is new in this pass (`SellFuelToBakery`,
  `SellFuelToHousehold`): FIFO deduction in place from
  `FuelYardByproductIntake` lots, because the intake has NO dispense API;
  a lot at 0 units is never re-sold because `TotalFuelWoodUnitsFor`
  counts `Max(0, units)`. Any failed leg restores intake units and cash,
  mirroring the phase 2 atomic pattern.

### Bakery (Glen Pierce) — PRODUCED AND SOLD

- Fed 12u store flour at 20c = 240c into `BakeryFlourStock` via the new
  paid inter-business sale `FeedBakeryFromStore` (see section 2).
- Notions: 1 unit supplied as a LABELLED RUN INPUT (see section 4, seam
  N1). A bread batch needs 1 notion unit as well as 12 lb flour, and the
  slice has no notions supply.
- Oven installed (bakehouse: oven-chamber-1, kneading-table-1,
  proofing-rack-1, bake-peels-1). Planned and worked 1 bread-loaf batch
  on day 3: 12 lb flour, 1 notion, yield 24 loaves, fuel stock 2 to 1.
  Loaves baked: 24. This is the real `BakeryShopRuntime` path
  (`PlanBatch` / `WorkDay` / `BreadShelf` / `SellBread`), not category
  stock labelled as bread, not fixed-ratio weekly output.
- Sold 6 loaves each to Pierce, Boniecki, and Baldwin-Ooms households at
  the fresh price 8c = 48c each (production price schedule default).
  Shelf remaining: 6 loaves. Bakery cash at end of day 3: 1594c
  (2000 - 10 fuel - 240 flour feed - 300 Glen draw + 48 + 48 + 48 bread
  sales); the day 4 draw of 100c brings the final to 1494c. Full
  reconciliation is in section 5.

### General Store (Kennedy Baldwin-Ooms) — SOLD AS BEFORE, PLUS FEED

- Store order arrived day 3, CashOnArrival, store cash 5000 to 3750.
- Kennedy day-draw 1000, store to 2750, Baldwin-Ooms household to 1000.
- Flour sale 40u at 20c = 800c to Baldwin-Ooms, store to 3550,
  household to 200, store lot to 60.
- Bakery feed 12u at 20c = 240c, store to 3790, store lot to 48.
- Day 4 draws of 100c each bring the finals to section 5.

### What each household ate (day 3 meals, default policy)

`HouseholdMealService.ServeHouseholdDay` for Baldwin-Ooms (persons 1, 2),
Pierce (persons 3, 4), Boniecki (persons 5, 6). Each household served
more than 0 meals, total missed meals 0. Meals are proved from real
inventory lots: Baldwin-Ooms from store flour plus bakery bread,
Pierce and Boniecki from bakery bread (plus their own holdings where
present). Flour eaten directly is still flour, not bread, wherever the
meal policy picks it; bread meals are bread only where bread lots were
actually bought.

### Controls that held in the run

- Founder wage registration for Glen at the bakery with `isOwner` true
  refused (founders are operators, never wage employees, TECH 4.1).
- Reserve-breaking Glen draw of 100000 refused, moving nothing.
- Flour over-sale of sellable + 1 refused, moving nothing.
- Bread over-sale of remaining + 1 refused, shelf restored.
- Fuel over-sale of intake remaining + 1 refused, moving nothing.
- Slice gate survives: save/restore via the phase 2 coordinator with
  `SuppressMigrantArrivals` true restores true, consumption count
  matches, restored lot `RemainingUnits` equals the live lot.

## 2. Bakery flour feed — DONE (paid inter-business sale, not a gift)

New file section: `ThreeBusinessSlice.FeedBakeryFromStore`.

- Store consumption goes through the NEW `ConsumeLots` API on
  `OffMapNamedSkuProcurementService`
  (`Domains/Economy/Businesses/GeneralStore/OffMapNamedSkuProcurementService.cs`):
  FIFO, refuses on shortfall moving nothing, records a per-lot split,
  moves no cash (caller settles cash separately). `UndoConsumption`
  restores exactly one recorded consumption or refuses an unknown
  record. This is the ONLY non-sale consumption path, shared by the
  bakery feed and by credit sales (section 7), so one lot unit can never
  feed both a household sale and bakery stock.
- The bakery pays the store at a caller-supplied price per store unit;
  flour lands via `BakeryFlourSupply.ReceiveImportArrival` with the store
  order supplier as origin and the consumption id as import order id.
- Unit semantics are a caller parameter, `lbPerStoreUnit`; the composed
  run uses 1. Flour unit semantics (1 store lot unit = how many lb of
  bakery flour) is still a Kennedy decision (section 9, D-P8-1).
- Test: `FeedBakeryFromStore_Shortfall_MovesNothing` — feeding 11u from a
  10u lot refuses, sellable stays 10, consumptions 0, bakery cash
  unchanged, store cash at procurement cost only.

## 3. Staged-to-sellable bakery transfer — NOT DONE, NOT FAKED

Decision, with reasons (also in the `ThreeBusinessSlice` file header):

- `BakeryStagedProductionChain` is a separate nine-step production
  method (receive, prepare, proof, heat-oven, load, bake, unload, cool,
  store). Its shelf lots carry a string `LotId` (`bread-<batch>`); its
  batches run through `BakeryChainContext`, `TaskAuthority`,
  `WorkTimeBudgetStore`, `IDueEventScheduler`,
  `IUnifiedReservationAuthority`, and the game clock.
- `BakeryShopRuntime` has NO public Receive or Add API for finished
  bread, and the staged chain has NO delivered-mark set, so hand-copying
  staged lots into shop lots would create a second custody record for
  the same bread and could transfer one staged lot twice.
- A safe transfer needs, in Codex / production work: a
  `ReceiveTransferredBreadLot` API on `BakeryShopRuntime` with a
  transferred-staged-lot-id set in its save DTO, a staged-lot
  consumption mark on the chain side, a decision on baked-day versus
  transfer-day aging (the shop lot ages from `BakedDayIndex`; a transfer
  a day later would sell day-old bread as fresh unless the transfer
  carries the baked day through), and a Main Scene owner and driver for
  the chain.
- Until then the slice bakery IS the shop runtime: it produces real
  bread with real flour provenance, real fuel, and real prices, and the
  staged chain stays unused by the slice. Whether the staged chain or
  the shop runtime is the slice bakery path is a Kennedy decision
  (section 9, D-P8-2).

## 4. Seams where the three-business run stops

- N1 NOTIONS SUPPLY: bread baking is proved GIVEN notions on hand.
  The General Store stocks flour only (single-SKU guard intact) and no
  miller or notions-import path exists for the bakery. The run supplies
  notions as an explicit labelled run input through
  `BakeryFlourSupply.ReceiveImportArrival` (import order id
  `passxl-phase8-notions-input`, origin "off-map notions supplier",
  test parameter, not a registered supplier). A notions procurement path
  is a remaining gap for pass 9 or Codex.
- N2 FUEL SALE BRIDGE: the intake has no dispense API, so the bridge
  deducts FIFO in place. A production `Dispense` API on
  `FuelYardByproductIntake` would be cleaner; the in-place deduction is
  safe (0-unit lots never re-sell) but is bridge code, not a production
  API. Codex task.
- N3 EQUIPMENT GATES NOT PROVED: the fuel chain runs with gate null —
  no axe, wagon, or saw-line possession proved. A production driver must
  supply real gates. The sawmill is an upstream supplier outside the
  six-person fixture; its lumber stays at the mill and is not sold in
  this run.
- N4 BUSINESS SEEDS STILL INPUTS: store, bakery, and fuel dealer start
  with caller-supplied owner equity, not slice earnings. A 0-capital
  business cannot order, buy fuel, or sell before it has cash. Whether
  a business seed is still needed for the first cycle is a Kennedy
  decision (section 9, D-P8-4); removing it needs either a production
  owner-equity injection path or a TradeCredit first order plus a
  household-credit path, both undecided.
- N5 UNCHANGED FROM PHASES 6-7: daily/weekly tick driver
  (`SimulationDrivers`, `FirstLedgerSliceBootstrapper`), residence
  assignment (`HousingResidence` runtime), postal office registration
  (`PostalService.RegisterOffice`), production supplier registration
  (`OffMapSupplierCatalog.RegisterSupplier`), Main Scene wiring, and
  fixture-to-production id confirmation are all Codex-only.

## 5. Cash reconciliation (composed run, test parameters only)

Step order in the test: fuel chain day 0; store order and arrival day 3;
Kennedy day-draw 1000; Baldwin-Ooms flour sale 800; bakery feed 240;
Glen day-draw 300; Jacob day-draw 400; bakery bake day 3; bread sales
48 x 3; household fuel sale 40; meals day 3; day 4 draws 100 each.

Finals asserted in the test:

- Store: 3690c = 5000 - 1250 (procurement) - 1000 (Kennedy draw 1)
  + 800 (flour sale) + 240 (bakery feed) - 100 (Kennedy day 4 draw).
- Bakery: 1494c = 2000 - 10 (fuel) - 240 (flour feed) - 300 (Glen draw)
  + 48 + 48 + 48 (bread sales) - 100 (Glen day 4 draw).
- Fuel: 550c = 1000 + 10 (bakery fuel sale) - 400 (Jacob draw)
  + 40 (household fuel sale) - 100 (Jacob day 4 draw).
- Baldwin-Ooms household: 212c = 0 + 1000 (draw) - 800 (flour sale)
  - 48 (bread) - 40 (fuel) + 100 (day 4 draw).
- Pierce household: 352c = 0 + 300 (draw) - 48 (bread) + 100 (day 4 draw).
- Boniecki household: 452c = 0 + 400 (draw) - 48 (bread) + 100 (day 4 draw).
- Store lot remaining 48u; fuel intake remaining 2u; bakery fuel
  remaining 1u; shelf loaves remaining 6; fuel produced 8u; loaves
  baked 24; meals missed 0.

No double lot, double charge, or double sale in any step; every refusal
in the controls moved nothing.

## 6. Kennedy addition 1: daily wage, day-to-day owner draw, credit

Reported separately, as requested. All numbers are test parameters only.

### 6a. Daily wage — DONE

- There is no daily-wage authority and no daily-wage terms type in the
  codebase: `CompensationTerms` carries a weekly wage only
  (`Domains/Economy/Core/EmploymentRelationship.cs`, `FromWeeklyWage`,
  `AgreedWeeklyWageCents`). `SliceWorkAndPay.PayDailyWage`
  (`Orchestration/Player/SliceWorkAndPay.cs`) therefore uses the SAME
  existing path as every other wage:
  `WageSettlementService.SettlePartialPeriod` via
  `BusinessRuntimeState.PayAgreedWage`
  (`Domains/Economy/Labor/WageSettlementService.cs`,
  `Domains/Economy/Core/BusinessRuntimeState.cs`). No second payroll
  authority was invented.
- The daily amount is caller-supplied: a daily rate the business agreed,
  or a prorated slice of the weekly wage. The period label is
  `day-<dayIndex>`, which also gives the existing exactly-once
  manual-payment record its day key: paying the same day twice is
  refused by `PayAgreedWage` itself.
- The attendance gate lives in the bridge, NOT in `SettlePartialPeriod`
  (which does not check attendance): `PayDailyWage` refuses a day with
  no worked attendance before calling it, returning
  `refused-no-attendance`, moving nothing, accruing nothing.
- Shortfall uses the same treatment as weekly and partial pay:
  employment suspended (`EmployerPaymentDefault`), wage accrues as an
  operating liability (`AccruedLiabilityCents`), no cash created.
- A daily-paid week still nets against weekly payroll through the
  existing `ManualCashPaidThisWeek` netting in `BusinessRuntimeState`,
  so daily pay plus a weekly run cannot pay the same week twice. This
  closes the pass 7 open item (combined manual-plus-weekly netting was
  a Codex/driver test, not claimed, in pass 7; it is claimed here).
- Tests (non-slice test employee person 99, business
  `general-store-test`, weekly wage 350c, all test parameters):
  `DailyWage_AttendanceGated_PaysOnce_AndNoAttendancePaysNothing` (day 0
  worked, daily 50c paid, business 5000 to 4950, household to 50; repeat
  day 0 refused; day 1 no attendance refused, liability 0; day 2 worked
  paid, business to 4900, household to 100);
  `DailyWage_Shortfall_AccruesLiabilityAndSuspends_NoCashCreated`
  (business cash 30, daily 50 worked, paid-arrears, business stays 30,
  household 0, liability 50, employment Suspended);
  `DailyWage_ThenWeeklyPayroll_NetsManualCash_NoDoublePayForTheWeek`
  (daily 50 on day 0, then weekly settlement week 0: business to 4650,
  household to 350 — weekly 350 nets the 50 already paid, 300 more, not
  350 more).
- Kennedy decision flagged, not guessed (D-P8-5): daily wage rate basis
  — a separate agreed daily rate, or a prorated slice of the weekly
  wage — since `CompensationTerms` carries weekly only.

### 6b. Day-to-day owner draw — DONE

- `SliceWorkAndPay.DrawDaily` is `DrawOwnerEarnings` under its
  day-to-day name: the pass 7 draw was read as a cycle draw, and Kennedy
  asked for the day-to-day form to be explicit. The owner may take money
  out on any day he needs it, in any amount, subject to the same reserve
  cap on every single draw, and may equally take nothing on a day.
  Taking nothing is a real choice, not a sweep default: business cash
  stays in the business, and the next day's ordering power is exactly
  the cash left behind.
- Business cash and household cash stay separate throughout: a draw is
  the only movement between them, it moves real cash business to
  household, and a reserve-breaking draw moves nothing.
  `DrawOwnerEarnings` itself was NOT changed: it refuses a non-positive
  amount, refuses when business cash minus amount is below the
  caller-supplied reserve, refuses when the household ledger rejects the
  inflow, moves nothing in any refusal case; on success business cash
  falls and household cash rises by exactly the draw, and records the
  draw on the runtime via `RecordWeeklyOwnerDistribution`.
- The reserve rule itself is still caller-supplied here; the production
  rule lives in `PlayerPortfolioManager.ResolveOwnerDistribution`
  (checkpoint, grace, survival reserve, growth divisor) and is a Kennedy
  decision, not something this bridge invents (D-P8-6).
- Test: `DayToDayDraw_SuccessiveDays_ReserveCapEveryDay_AndLeavingCashInPreservesOrderingPower`
  (business 2000, household 0; day 0 draw 300 reserve 1000 succeeds,
  business 1700, household 300; day 1 draw 300 succeeds, business 1400,
  household 600; day 2 draw 500 reserve 1000 refused, would leave 900,
  business and household unchanged; the leave-in assertion is
  arithmetic: 1400 left in the business still covers a 1250c order).
  The composed three-business run also exercises day draws for all
  three founders (Kennedy 1000 on arrival day, Glen 300, Jacob 400, plus
  100 each on day 4).
- Owner draw is NOT a wage; founders are operators, never wage
  employees (TECH 4.1, unchanged).

### 6c. Credit, as a business tool — DONE for store flour, DOCUMENTED limits

New file: `Orchestration/Player/SliceBusinessCredit.cs`.

- Policy class `BusinessCreditPolicy`: `OfferCredit`,
  `PerSaleLimitCents`, `PerCustomerOutstandingLimitCents`,
  `TotalOutstandingLimitCents`, `NetDays` (default 30),
  `BlockNewCreditWhenPastDue` (default true), and a `CashOnly` factory
  returning `OfferCredit` false and all limits 0.
- No new credit authority: `SellOnCredit` uses the existing
  `TradeCreditAuthorityPort` over `FinancialObligationAuthority`
  (debtor `household:<id>`, creditor business id, principal full price,
  terms net `<NetDays>`, purpose credit sale, agreement id
  `sku-credit-sale-<business>-<day>-<consumption id>`, source event
  consumption id, kind `TradeCredit`;
  `Domains/Economy/Financing/FinancialObligationAuthority.cs`: `Create`,
  `ApplyPayment`, `TotalOutstandingFor`, `TotalReceivableFor`,
  `MarkDue`, `MarkDelinquent`, `MarkDefaulted`). Aging uses the existing
  `TradeCreditBook` (`Domains/Economy/Financing/TradeCreditAging.cs`,
  `RegisterTerms`, `EvaluateDay`; stages Current, ReminderIssued at 1
  day past due, FormalDemand at 31, InCollection at 61, Defaulted at
  121).
- Ordering is consume, then obligate, then deliver, for the reasons in
  the file header: consumption before obligation, or a refused
  consumption would leave a debt for goods never taken; obligation
  before delivery, or delivered goods would have no debt behind them.
  Delivery failure after both is near-impossible (SKU pre-validated
  against `HouseholdItemCatalog`, units positive, source label
  non-blank), but if it happens the consumption is undone and the
  diagnostic says the obligation needs manual reconciliation, because
  no obligation-void API exists to erase it cleanly.
- Business cash does NOT rise on a credit sale — no cash has arrived;
  it rises only on repayment via `RepayCredit`, which credits business
  cash, records a household outflow, then calls `ApplyPayment`, and
  reverses both cash legs if `ApplyPayment` fails, with pre-checks that
  amount is positive, obligation exists and is not settled, amount does
  not exceed outstanding, and household balance covers it.
- Non-payment handling is exactly the existing book escalation. There
  is no forgiveness, write-off, or void API on
  `FinancialObligationAuthority`, so a defaulted debt stays outstanding
  until paid; whether the slice wants a write-off or forgiveness rule is
  a Kennedy decision (D-P8-7). This bridge does not invent one by
  deleting obligations.
- Configuration location, Codex / production task: this policy is a
  plain C# object handed to the bridge per sale. In production it
  belongs on the business, not in code: a per-business credit policy
  record, a ScriptableObject or a business-definition field, alongside
  the General Store business definition, set by the owner in the Main
  Scene or business UI, read by the sales driver before it offers
  credit, and persisted with the business. No such asset or field
  exists yet; that is the missing configuration link, and until it
  exists the policy must be supplied by the caller (tests do) and must
  never be defaulted silently to credit on.
- Default credit policy for the slice is a Kennedy decision, flagged,
  not guessed (D-P8-7): offer credit yes/no, per-sale limit,
  per-customer limit, total limit, net days, block-new-credit-when-past-due,
  and whether a write-off/forgiveness rule should exist.
- NOT built: credit sales for bakery bread and dealer fuel are not
  bridged in this pass. The policy class is reusable for them, but their
  cash bridges in `ThreeBusinessSlice` have no credit leg yet, and a
  credit leg for them needs the same consume-then-obligate-then-deliver
  ordering proved here for store flour. A household credit path for the
  store's OTHER goods does not exist beyond this bridge: phase 1
  household sales remain cash-only unless the caller explicitly routes
  through `SellOnCredit` with a policy that offers credit.
- Tests: `Credit_CashOnlyPolicy_RefusesCredit_AndCashSaleStillWorks`
  (CashOnly policy: credit sale 10u at 20c refused, diagnostic contains
  "does not offer credit", sellable stays 100, obligations 0; then a
  cash sale of the same goods succeeds, sellable 90 — cash-only is a
  valid, tested path);
  `Credit_SaleCreatesObligation_NotCash_LimitsEnforced_RepaymentSettles_PastDueBlocksNewCredit`
  (policy offer true, per-sale 500, per-customer 800, total 1000, net
  30, block-when-past-due true: sale 1, 20u at 20c = 400c, succeeds,
  business cash stays 3750, outstanding for household:1 is 400,
  inventory flour 20; over-sale-limit 30u = 600c refused; sale 2, 20u =
  400c, succeeds, outstanding 800; over-customer-limit 10u = 200c
  refused; household funded 1000, repayment of sale 1 (400c, day 1)
  succeeds, outstanding 400, store cash 4150, household balance 600;
  `AgeCredit` to day 31, sale 2 due day 30, book stage ReminderIssued,
  new credit sale day 32 refused, diagnostic contains "past-due";
  repayment of sale 2 (400c, day 32) succeeds, outstanding 0, new
  credit sale 10u at 20c on day 32 succeeds).

## 7. Kennedy addition 2: freight and horses

Reported separately, as requested. All numbers are test parameters only.

### 7a. Freight — PROVED IN ROSLYN, with driver and route seams

Audit (all in `repo-passxl8`, code read, not assumed):

- `FreightProductionDriver`
  (`Domains/Economy/Businesses/Freight/FreightProductionDriver.cs`):
  `EnsureCompany`, `FindCompany`, `CreateShipment`, `SetDispatchPlan`,
  `AdvanceTick`, `CaptureSaveDto`, `RestoreSaveDto`. `CreateShipment`
  goes through `FreightShipmentService.CreateShipment` and the carrier
  `FreightCompanyRuntime.TryAcceptShipment`, records a `DispatchPlan`
  with real driver plus animals, refuses a phantom wagon, over-capacity
  cargo, or an exclusivity conflict. `AdvanceTick` steps Planned to
  Loading to dispatched InTransit to Arrived to Unloading to Completed;
  Delayed shipments are left exactly as they are, cargo in legitimate
  wagon custody, no revenue; settlement collects revenue only for
  completed-and-accepted shipments, exactly once, via the runtime
  collected flag. Revenue collection: each shipment's named payer is
  resolved to its runtime and debited before the carrier is credited;
  an unresolvable payer leaves the charge uncollected — no legacy
  unverified fallback, no phantom carrier credit. Dispatch without a
  dispatch plan waits; capacity is never invented.
- `FreightShipmentService`
  (`Domains/Economy/Logistics/FreightShipmentService.cs`): `RegisterWagon`,
  `CreateShipment`, `BeginLoading`, `WorkLoading`, `Dispatch`,
  `AdvanceTransit`, `BeginUnloading`, `WorkUnloading`. A wagon must be a
  registered `EquipmentAsset` with Kind containing wagon
  (`EquipmentAsset.IsWagon`,
  `Domains/Economy/Businesses/Blacksmith/EquipmentAsset.cs`), otherwise
  `CreateShipment` refuses a phantom wagon or a non-wagon asset.
  Capacity is checked by mass and volume against
  `PayloadMassCapacityKg` and `CargoVolumeCapacityM3`. Exclusivity: a
  wagon, driver, or animal already bound to a live shipment cannot be
  booked twice, and when `hooks.Reservations` is wired, dispatch
  atomically reserves wagon, driver, and draft animals through the
  unified reservation authority, and completion releases the hold. When
  `hooks.ReceiveShipment` is null, an unloaded shipment is held as
  Delayed rather than falsely marked delivered.
- `FreightResourcePool`
  (`Domains/Economy/Businesses/Freight/FreightResourcePool.cs`):
  `FreightWagon` with `WagonId`, capacity, condition, status Available /
  OnRoute / InRepair / Retired, attached `EquipmentAsset` as the single
  condition truth after reconciliation; `DraftTeamReservation` with
  wagon, draft animals, driver, purpose. `TryReserveTeam` requires
  capacity, reserves exactly 2 draft animals and 1 driver through
  `DraftPowerService`, the ONE draft-power authority (Tech X section
  3.8), and reserves the wagon instance through the unified authority,
  atomically with rollback on any failure. `ReleaseTeam` releases
  authority holds, asset flag, and legacy list, returns the wagon to
  Available with wear (default 0.02) or `ReleaseTeamAfterTrip` with
  per-mile wear from `EquipmentConditionProfiles` when a profile
  exists. A wagon in repair cannot haul. The pool's `DraftPower`
  property creates a default `DraftPowerService` if none is attached;
  the hub attaches its own persisted service on boot and after load.
- `DraftPowerService`
  (`Domains/Economy/DraftPower/DraftPowerService.cs`):
  `TryReserveAnimals`, `TryReserveDriver`, `ReleaseAnimals`,
  `ReleaseDriver`, `IsAnimalReserved`, `IsDriverReserved`, `HoldCount`,
  `CaptureSaveDto` / `LoadFromSaveDto` (DTO in
  `Infrastructure/Persistence/SystemsSaveDtos.cs`). Reservations are
  all-or-none with rollback, never double-booked; each animal gets its
  own reservation so release is exact. Default constructor uses a
  private transient `EquipmentReservationService`; production constructor
  takes the hub-owned shared authority, which is persisted.
  `NowGameSeconds` defaults to 0.
- `FreightCompanyRuntime`
  (`Domains/Economy/Businesses/Freight/FreightCompanyRuntime.cs`):
  multi-unit enterprise (Canon section 3.6), freight-hauling plus
  wagon-repair under ONE ledger sharing ONE physical pool; internal
  repair is a cost allocation, never revenue to itself; revenue category
  `freight_service`; tasks `fr.load-freight`, `fr.drive-route`,
  `fr.unload-freight`, `fr.repair-wagon` (repair task needs a
  wheelwright-kit). Shipments honor MR-P001 conservation: cargo moves
  only through the shipment's own load, transit, unload stages; blocked
  shipments hold zero cargo and are never completed by fiat.
  `FreightGoodsClass` (Fragile, Perishable) is defined in
  `Domains/Economy/Businesses/GeneralStore/GeneralStoreTaskCatalog.cs`.
- LiveryFreight as a business type: `BusinessType.LiveryFreight` = 9
  (`Domains/Economy/Core/BusinessDefinitions.cs`), profile
  `livery_freight`, archetype `LogisticsMovement`, recurring-order
  template `crop_food_to_livery_upkeep` (CropFarm crop_food to
  LiveryFreight `livery_feed_upkeep`, 6, in `LocalRecurringOrderModels.cs`).
  There is no LiveryFreight per-instance business package in the same
  sense as the FuelDealer gap; the freight company runtime and
  production driver are the per-instance freight path, and the archetype
  local-market outlet is the old generic path, not to be restored
  without a Kennedy decision.
- Tests (pattern mirrored from the existing Unity test
  `Tests/Editor/Economy/FreightProductionDriverTests.cs`):
  `WagonTeamPool_ReserveDoubleReserveRefused_ReleaseFreesWagonAndAnimals`
  (registry horses A and B, pool wagon `wagon-1` capacity 600;
  `TryReserveTeam` capacity 100, animals A and B, driver person 1,
  purpose test haul, succeeds, wagon OnRoute; second reserve with the
  same animals and driver fails; `ReleaseTeam` returns wagon to
  Available, `DraftPower.IsAnimalReserved` for horse A false; third
  reserve succeeds);
  `Freight_RealRegistryHorsesAsTeam_ShipmentDeliversOnce_AndChargeSettlesOnce`
  (`TaskAuthority`, `WorkTimeBudgetStore` with `EnsureDay 42`, hooks
  with Tasks, Budgets, DayIndex 42, Diagnostics, `ReceiveShipment`
  callback adding to recipient stock keyed by destination business id;
  wagon asset `wagon-1`, Kind `farm-wagon`, Condition01 0.9,
  PayloadMassCapacityKg 1000, CargoVolumeCapacityM3 4; registry horses
  A and B as team; handler person 701, driver person 702; carrier
  `biz-freight-1`, payer `biz-store-1` with 100000c; shipment `ship-1`,
  source mill-1, destination store-1, 50 units, MassKgPerUnit 2,
  VolumeM3PerUnit 0.01, charge 500, status Planned; `AdvanceTick` 3600
  seconds with handler, guard 60, until Completed; DeliveredQuantityUnits
  50, recipient stock store-1 50, carrier cash 500, payer cash 99500;
  further 10 ticks settle nothing twice, carrier cash unchanged; second
  shipment `ship-2` on wagon-1, 10 units, charge 100, succeeds — a
  completed shipment frees its wagon).
- SEAMS for freight: `AdvanceTick` needs `TaskAuthority` plus
  `WorkTimeBudgetStore` hooks; a production driver must supply them on a
  schedule (`SimulationDrivers` or a freight driver). A `FreightRoute`
  object is NOT exercised in the pass 8 test (source to destination
  with a wagon asset only); whether a `FreightRoute` object is required
  for a shipment, or source-to-destination with a wagon asset is
  sufficient, is a Kennedy decision (D-P8-8).

### 7b. Horses — PROVED IN ROSLYN, with a production feed-resolution seam

Audit:

- `AnimalRegistry` (`Domains/Animals/AnimalRegistry.cs`): `RegisterAnimal`
  takes species, breed, sex, ownerKind, ownerId, dayIndex, reason,
  allocates a permanent `AnimalId` (`EntityKind.Animal`) via
  `EntityIdRegistry`, appends an `AnimalOwnershipRecord`; history is
  append-only; transfers close the open record and append a new one;
  past records are never rewritten. `AnimalState` fields include
  `AnimalId`, `Species`, `OwnerKind`, `OwnerId`, `HousingId`,
  `PhysicalLocationId`, `CustodianKind`, `CustodianId`,
  `Nutrition01` (default 1), `Fatigue01`, `LastCareDayIndex` (default
  -1), `HorseCapabilities`, `BiologicalState`, `CommercialStatus`,
  `IsActive`.
- `HorseAuthority` (`Domains/Animals/HorseFoundation.cs`): `Configure`
  takes horse, `HorseUseCapabilities`, locationId, housingId, dayIndex;
  refuses None capabilities and a missing housing id for a working
  horse; sets capabilities, location, housing, and `LastCareDayIndex`
  to the configuration day. `SetCustody`, `SetHousing`,
  `TryReserveUse`, `ReleaseUse`, `MeetsWorkCondition`, `IsWorkReady`,
  `RecordInjury`, `ComputeWorkCost`, `ApplyCare`, `ApplyWork`.
  `HorseUseCapabilities` is Flags: None 0, Saddle 1, Draft 2,
  GeneralPurpose (Saddle or Draft). `HorseHousingRegister`: `AddHousing`
  with id and capacity, `Assign`, `Release`, `Occupied`, `Capacity`,
  `CaptureSaveDto` / `LoadFromSaveDto`.
- `HorseCareDriver` (`Domains/Animals/HorseCareDriver.cs`): `CareDay`
  with registry, feed stock or feed-stock-for-horse function, isWorking
  function, dayIndex, diagnostics; returns the number of horses that
  went unfed. Daily feed units: idle 1.2, working 2.5, both calibration,
  not canon, matching `FeedLoop` daily rates. Need in whole units is
  Ceiling of the rate, minimum 1: idle need 2 units, working need 3
  units. A fully fed horse recovers through
  `HorseAuthority.ApplyCare` (water 1 if it holds a named housing
  arrangement else 0.5, rest 1 if idle else 0.4). An underfed horse
  degrades proportionally to shortfall: nutrition loss 0.15 times
  shortfall, fatigue gain 0.10 times shortfall, no care applied. A horse
  with a null feed stock degrades by the full 0.15 nutrition and 0.10
  fatigue. Idempotency: if `horse.LastCareDayIndex >= dayIndex`, the
  horse is skipped, so replayed daily events, duplicate driver
  registration, or restoring and reprocessing the same date must not
  consume a second ration or apply a second condition change.
  `LastCareDayIndex` is persisted with the animal state, so it is the
  idempotency marker.
- Feed resolution, production, `SimulationDrivers.DriveHorseCareDay`
  and `ResolveHorseFeedStock`
  (`Orchestration/Systems/SimulationDrivers.cs`, around lines 2311-2355
  at investigation time): `DriveHorseCareDay` returns early unless hub,
  `hub.Animals`, `hub.FarmSlice`, and `hub.DraftPower` are all present.
  It calls `HorseCareDriver.CareDay` with `ResolveHorseFeedStock` and
  isWorking as `hub.DraftPower.IsAnimalReserved` for the horse
  `AnimalId`, so a horse holding a draft reservation eats the working
  ration — draft power has honest upkeep. `ResolveHorseFeedStock`
  returns: a `HorseFeedStockAdapter` over `hub.FarmSlice.Feed` when
  `OwnerKind` is Business and `OwnerId` starts with `farm:`; a
  `BusinessHorseFeedStockAdapter` over the owner's
  `BusinessRuntimeState` `livery_feed_upkeep` category stock when
  `OwnerKind` is Business and
  `SharedBusinessRuntimeManager.TryFindByInstanceId` finds the owner
  and the owner `BusinessType` is LiveryFreight and
  `GetCategoryStock` for `livery_feed_upkeep` is not null; and null
  otherwise. That otherwise is the open question, exactly as Kennedy
  stated: person-owned horses, household-owned horses, business-owned
  horses that are not farm or LiveryFreight, and LiveryFreight businesses
  with no `livery_feed_upkeep` category stock object at all, all resolve
  to null and go through the unfed path, degrading even when feed
  actually exists elsewhere for that owner.
- Feed adapters (`Domains/Economy/Farming/Integration/HorseFeedStockAdapter.cs`):
  `HorseFeedStockAdapter` wraps `FeedLoop`; `FeedStockUnits` and
  `TakeFeedUnits` return only what the stock actually holds — feed is
  never free. `BusinessHorseFeedStockAdapter` wraps a
  `BusinessRuntimeState`, `FeedCategoryId` `livery_feed_upkeep`,
  `TakeFeedUnits` uses `TryConsumeCategoryStockUnits` and returns
  consumed or 0.
- Horse trade and rental exist but are not proved in pass 8:
  `HorseRentalService`, `HorseRentalRecord`, `HorseTrade`, `HorseSale`
  (`Domains/Economy/DraftPower/HorseRental.cs` and `HorseTrade.cs`),
  and `HorseWorldView`, a MonoBehaviour
  (`Domains/Economy/Transport/TransportWorldViews.cs`, Unity-only, not
  in Roslyn).
- Tests:
  `Horse_OwnedFedCared_IdempotentSameDay_WorkingRationWhenReserved_UnfedDegrades`
  (registry with `EntityIdRegistry`, horse person-owned `person:1`,
  `Configure` with Draft capability, location `stable-1-yard`, housing
  `stable-1`, day 0; stub feed stock in the test file implementing
  `IHorseFeedStock` with a Units field; `CareDay` day 1 idle, unfed 0,
  stock 100 to 98, idle ration Ceiling 1.2 = 2, `LastCareDayIndex` 1;
  repeat `CareDay` day 1, unfed 0, stock stays 98; `DraftPowerService`
  reserve horse for plow, 1 animal, eligible always true, reserved true,
  `IsAnimalReserved` true, `CareDay` day 2 working, unfed 0, stock 98 to
  95, working ration Ceiling 2.5 = 3, release animals,
  `IsAnimalReserved` false; empty stock `CareDay` day 3, unfed 1,
  nutrition falls, fatigue rises);
  `Horse_PersonOwned_ProductionResolverReturnsNull_DegradesDespiteFeedElsewhere`
  (same registry and horse, household stock 50 units, local resolver
  returns the stock only for Business owners with `OwnerId` starting
  `farm:`, null otherwise, mirroring `SimulationDrivers.ResolveHorseFeedStock`;
  `CareDay` day 1, unfed 1, household stock stays 50, never consulted,
  nutrition falls. This test reproduces the rule; it does not prove the
  production method itself, because `SimulationDrivers` is Unity-bound
  and is not compiled in the harness. The report cites
  `SimulationDrivers.cs`, not a production-code run.)
- SEAM for horses: production feed resolution for person-owned horses
  returns null (above). Horse rental, trade, and world views are
  Unity-only or unproved. A horse is therefore NOT proved to avoid
  degrading unfed when feed actually exists for a person owner — that is
  the one part of Kennedy's "without degrading unfed when feed exists"
  test that FAILS at the production seam, and it is a Kennedy decision
  (D-P8-9): where person-owned feed comes from (household feed stock,
  household inventory feed item, LiveryFreight stabling, or another
  rule).

### 7c. D1 — LogisticsTransportPresentationController, re-audited

Phase 3 discrepancy D1 is UNCHANGED and now sharper:

- `LogisticsTransportPresentationController`
  (`Domains/Economy/Logistics/LogisticsTransportPresentationController.cs`)
  is still a MonoBehaviour, `DisallowMultipleComponent`,
  `DefaultExecutionOrder` 266, serialized `LogisticsRuntimeManager`
  `logisticsRuntime`, GameObject `preferredTransportPrefab`, float
  `wagonHeightOffset` 0.05, bool `presentCompletedShipments`, bool
  `presentFailedShipments`, string `lastPresentationSummary`,
  `lastVisibleShipmentReadout`, `lastPressureReadout`. `Awake` and
  `OnValidate` call `AutoWire`; `OnValidate` in the Editor loads
  `preferredTransportPrefab` from `Assets/Travel/Wagon Cargo Horse.prefab`
  if null; `LateUpdate` calls `AutoWire`; if `logisticsRuntime` is null
  it clears visuals and reports logistics runtime unavailable, otherwise
  it reads `logisticsRuntime.CaptureTownPressureSnapshot` and
  `logisticsRuntime.Shipments` and spawns wagon visuals.
- A grep for `LogisticsTransportPresentationController` across Domains,
  Orchestration, and Presentation, excluding its own file, found 0
  production references, unchanged from phase 3.
- Sharper finding in pass 8: `LogisticsRuntimeManager`
  (`Domains/Economy/Logistics/LogisticsRuntimeManager.cs`) is a separate
  MonoBehaviour with its own `Shipments` list (line 42 at investigation
  time) and its own `AdvanceShipments`, and it contains NO reference to
  `FreightProductionDriver`, `FreightShipmentService`, or
  `FreightCompanyRuntime`. The controller therefore visualises the
  `LogisticsRuntimeManager`'s own shipment set, NOT the
  `FreightProductionDriver`'s company shipments. Even if placed in Main
  Scene, it would not visualise driver shipments unless the two shipment
  sets are unified or the controller is re-pointed.
- D1 remains a Codex and Kennedy decision (D-P8-10): place versus
  retire, now with that extra unification question. The controller is
  Unity-only and is NOT compiled or run in the Roslyn harness, so this
  is a code audit finding, not a Roslyn proof.

### 7d. Starting horse and wagon for Kennedy — PROPOSAL ONLY, NOT CANON

NOT implemented, NOT in the fixture, NOT decided. Kennedy suggested
starting Kennedy off with a horse and wagon or something; this section
evaluates it as a slice-start option.

What it would change:

- Self-collect freight becomes possible: SelfCollect quotes carry
  `RequiresJourney` true and currently no pickup travel execution exists
  for the store (phase 4 F38), and a person-owned horse plus wagon plus
  a driver (Kennedy himself) would supply the team for a SelfCollect
  pickup.
- A freight business becomes startable: `FreightProductionDriver.EnsureCompany`
  plus a wagon and team pool is the start path, and Kennedy already has
  a team, rather than needing to acquire one first.
- Horse feed becomes a daily cost: idle 1.2 units per day, working 2.5
  units per day (calibration, not canon), drawn from a real finite
  stock, with degradation on shortfall.
- Horse care becomes a daily task: `HorseCareDriver.CareDay` on a
  schedule, idempotent by `LastCareDayIndex`, with a working ration when
  the horse holds a draft reservation.

What the fixture currently gives:

- `SixPersonDevelopmentFixture`
  (`Domains/Population/SixPersonDevelopmentFixture.cs`) is a
  ScriptableObject with only a `profileId`, a `PersonDefinition` array
  of exactly 6 people, and a `HouseholdDefinition` array of exactly 3
  households, validated to exactly 6 persons and 3 households, each
  household exactly 2 members, unique `homeBuildingOrdinal`. It has NO
  starting assets, NO animals, NO wagons, NO feed, NO business capital
  fields at all.
- `OpeningBusinessRoster.SixPersonDevelopment` assigns 0 starting
  capital, no granted inventory or cash.
- A horse plus wagon would therefore be a granted asset, breaking the
  0 granted rule, and needs Kennedy's explicit approval before it
  becomes fixture canon.

How it would be represented (proposal):

- A new StartingAssets section on the fixture, or on
  `OpeningBusinessRoster` if Kennedy prefers code over asset, naming one
  horse (breed and sex as caller data), owner Person Kennedy (person 1),
  housing id for a stable, and one wagon asset (`WagonId`, capacity,
  condition), plus an opening feed stock with units and a feed source —
  all ID'd: horse `AnimalId` from `EntityIdRegistry`
  (`EntityKind.Animal`), wagon `AssetId`, feed stock id, saved and
  restored through `AnimalRegistry` save, `FreightShipmentService`
  wagon registration, and the feed stock save path, with no duplication,
  because the animal registry, wagon registration, and feed stock are
  each single-truth stores, and a second bootstrapper run must not
  re-register the same horse or wagon.
- Bootstrapper wiring required: `FirstLedgerSliceBootstrapper`, after
  `GeneratePopulationSnapshot` and before the first save (the same
  wiring point as the family-tree call site), must register the horse in
  the `AnimalRegistry`, configure it with `HorseAuthority.Configure`,
  register the wagon with `FreightShipmentService.RegisterWagon` and the
  `FreightResourcePool`, and seed the feed stock — all Codex and Editor
  work, not Roslyn.

Costs (all test parameters, not canon): daily feed as above, daily care
task time, wagon wear on trips (`ReleaseTeam` default 0.02 or per-mile
wear from `EquipmentConditionProfiles`), and the unresolved feed
question becomes load-bearing on day 1: a person-owned horse currently
degrades unfed under the production resolver, so the proposal is NOT
viable until Kennedy decides the person-owned feed source, or the horse
is owned by a LiveryFreight business or farm instead, which changes who
Kennedy is in the fiction.

Pros: self-collect becomes testable, freight becomes testable from day
1, horse care and feed become real slice costs. Cons: granted asset
breaks the 0 capital fiction, feed source is undecided, and a wagon
without a freight business or a route is just an asset, not an economy.

Kennedy decisions flagged, not guessed (D-P8-11): approve or reject the
granted horse and wagon; person-owned versus business-owned horse; feed
source for a person-owned horse; opening feed units; wagon capacity and
condition; and whether the fixture or `OpeningBusinessRoster` carries
starting assets.

## 8. Per-item status

- Three-business composed run (produce, sell, eat, reconcile): DONE
  (section 1, section 5).
- Bakery flour feed with single-custody consumption: DONE (section 2).
- Staged-to-sellable bakery transfer: NOT DONE, NOT FAKED (section 3).
- Fuel Dealer Task-to-tree-to-wood-lot-to-sale path: DONE in Roslyn
  (stand, rights, felling, haul, sawing, byproduct transfer, sale
  bridge), with seams N2 (no intake dispense API) and N3 (gates null).
- Pierce and Boniecki households buying and eating: DONE (bread bought
  and eaten day 3; fuel bought by Baldwin-Ooms).
- Glen and Jacob businesses selling something they produced: DONE
  (bread 18 loaves, fuel 6 units).
- Founder operator / wage distinction preserved: DONE (controls in
  section 1; pass 7 rules unchanged).
- Slice gate intact: DONE (section 1 controls; no work in this pass
  turns immigration, traveler arrivals, or outmigration on).
- Daily wage: DONE (section 6a).
- Day-to-day owner draw: DONE (section 6b).
- Credit options (offer, limits, terms, non-payment): DONE for store
  flour; DOCUMENTED limits for bread/fuel credit legs, policy asset,
  and write-off rule (section 6c).
- Freight working (shipment, exclusivity, team, delivery once, charge
  once): DONE in Roslyn, with driver-tick and route seams (section 7a).
- Horses working (owned, fed, cared, idempotent, hitched, freight):
  DONE in Roslyn except the production person-owned feed seam
  (section 7b). The "no unfed degradation when feed exists" condition
  is NOT met for person-owned horses under the production resolver.
- D1 controller audit: DONE as an audit; placement/unification is a
  Codex + Kennedy decision (section 7c).
- Starting horse and wagon: PROPOSAL ONLY (section 7d).

## 9. Design forks needing Kennedy's decision (nothing guessed)

- D-P8-1 Flour unit semantics: 1 store lot unit = how many lb of bakery
  flour (composed run uses 1, `lbPerStoreUnit` parameter).
- D-P8-2 Bakery path: staged chain with a safe transfer API, or shop
  runtime as the slice bakery (section 3); plus transfer ownership and
  timing, baked-day versus transfer-day aging.
- D-P8-3 Notions supply: where bakery notions come from (store SKU,
  miller path, import path, or another source).
- D-P8-4 Business seed for the first cycle: still needed (section 4,
  N4), or replaced by a production owner-equity injection path or a
  TradeCredit first order plus household-credit path.
- D-P8-5 Daily wage rate basis: separate agreed daily rate, or prorated
  slice of the weekly wage.
- D-P8-6 Production owner-draw reserve rule:
  `PlayerPortfolioManager.ResolveOwnerDistribution` (checkpoint, grace,
  survival reserve, growth divisor) versus the bridge caller-supplied
  reserve.
- D-P8-7 Default credit policy for the slice: offer credit yes/no,
  per-sale limit, per-customer limit, total limit, net days,
  block-new-credit-when-past-due, and whether a write-off/forgiveness
  rule should exist (none exists in code).
- D-P8-8 Freight route representation: `FreightRoute` object required,
  or source-to-destination with a wagon asset sufficient.
- D-P8-9 Person-owned horse feed source: household feed stock,
  household inventory feed item, LiveryFreight stabling, or another
  rule; without it a person-owned horse degrades unfed under the
  production resolver.
- D-P8-10 D1: place `LogisticsTransportPresentationController` in Main
  Scene or retire it, plus shipment-set unification with
  `FreightProductionDriver` companies.
- D-P8-11 Starting horse and wagon: approve or reject, person-owned
  versus business-owned, opening feed units, wagon capacity and
  condition, fixture versus `OpeningBusinessRoster` for starting assets.
- Carried from earlier phases, unchanged: slice-departure activation
  (F09/R09), retire old 4% arrival roll (F10/F35), unify departure paths
  with destinations (F11/F12), double-ordering guard (F26), friend graph
  (F36), experience formation rate (F34), all prices/rates/weights/
  frequencies, meal-count denominator (2/day in `DailyNeedsService`
  versus up to 3/day under the default meal policy), and the phase 5
  employment-id format (R05) before the driver ever runs in production.

## 10. Per-gap owner: Codex Editor task, Unity-only test, or Kennedy decision

- Staged transfer API (`BakeryShopRuntime.ReceiveTransferredBreadLot`,
  staged-lot consumption mark, baked-day aging, Main Scene owner/driver):
  Codex Editor task + Kennedy decision D-P8-2.
- Notions procurement path: Codex task + Kennedy decision D-P8-3.
- Fuel intake dispense API (`FuelYardByproductIntake`): Codex task.
- Production equipment gates for felling/haul/sawing: Codex driver task.
- Freight driver tick wiring (`TaskAuthority` + `WorkTimeBudgetStore`
  hooks on a schedule): Codex driver task.
- FreightRoute representation: Kennedy decision D-P8-8.
- Person-owned horse feed source + `SimulationDrivers.ResolveHorseFeedStock`
  change: Kennedy decision D-P8-9, then Codex task.
- Starting horse/wagon fixture + bootstrapper wiring: Kennedy decision
  D-P8-11, then Codex Editor task.
- D1 controller placement + shipment-set unification: Codex Editor task
  + Kennedy decision D-P8-10.
- Credit policy asset / business-definition field: Codex task +
  Kennedy decision D-P8-7.
- Daily wage rate basis, owner-draw reserve rule: Kennedy decisions
  D-P8-5, D-P8-6 (production rule lives in
  `PlayerPortfolioManager.ResolveOwnerDistribution`, Unity-wired, not
  proved in Roslyn).
- Unchanged Codex items from phases 1-7: family-tree call site
  (`FirstLedgerSliceBootstrapper` after `GeneratePopulationSnapshot`,
  before first save), residence assignment, postal office registration,
  production supplier registration, `SaveLoadManager.PassXlProcurementService`
  registration before first save, daily tick driver, reputation
  aggregation, traveler generation, `SettleAndApply` for stays,
  `OutmigrationCloseout` plus F08 decisions, Bakery Receive API plus
  scene owner plus flour feed (feed bridge now exists in Roslyn; scene
  owner/driver still Codex), Fuel Task-to-wood-lot-to-sale path in Main
  Scene (Roslyn path now exists; scene/driver still Codex), multi-SKU
  batch ledger, D2, D3, playtest Inspector bools,
  `GeneralStoreBusinessDefinition` asset tier ids, fixture-to-production
  id confirmation.
- Unity-only tests still unrun: `PassXlPlayerAsNpcTests`,
  `SixPersonNoImmigrationTests`, six-person fixture test, six-person
  30-day test, `ConstructionInputSupportTests` 1800s timeout (still no
  verdict), dedicated Phase H failure diagnosis for the 3 pre-existing
  failures.

## 11. Roslyn harness notes (for the next pass)

Harness file (outside the repo, new for this pass):
`~/workspace/land_and_ledgers/roslyn-harness/harness-passxl-phase8.csproj`
(copied from the phase 7 harness; phase 7 harness untouched), plus
harness-only shim `roslyn-harness/Shims/Phase8SaveShim.cs`.

Shimmed, NOT production behaviour, documented so pass 9 does not
mistake them for proofs:

- `DraftPowerServiceSaveDto`, `DraftPowerHoldEntry`,
  `FreightResourcePoolSaveDto`, `TransportAssetRegistrySaveDto`: field
  shapes copied from `Infrastructure/Persistence/SystemsSaveDtos.cs`,
  which drags dozens of domains in and cannot be compiled in the
  harness. Save round-trips of draft power and freight pool state are
  therefore NOT proved in Roslyn.
- `MillLot`: only type the bakery flour bin needs from
  `GrainChain.cs`; the full grain chain drags crop, feed, and
  capability systems in. Field list mirrors production; milling itself
  is NOT proved in Roslyn.
- `EquipmentAvailability`: only type needed from
  `EquipmentRequirements.cs`, whose `EquipmentRequirementCodes` class is
  already stubbed in `MineSaveShim.cs` (including the real file would
  duplicate it). `EquipmentRequirements.ForHolder` is a shape-only stub
  and `CheckTaskRequirements` refuses loudly rather than silently
  passing; no pass 8 test calls it (all gates are null), and any future
  caller gets a loud refusal, not a fake pass. Equipment gates are
  therefore NOT proved in Roslyn anywhere in this pass.
- Real files added to the harness (not shims): `SkillService.cs` plus
  `CapabilityStores.cs`, `SkillModels.cs`, `CausalTaskOutcomeResolver.cs`,
  `TaskOutcomeContracts.cs`, `CapabilityContracts.cs`,
  `CapabilityEvidenceService.cs` (skill system, needed only because
  `BakeryBreadCatalog.RegisterSkills` takes a `SkillService`; the method
  is never called in tests); `ImportService.cs` + `ImportCatalog.cs`
  (Trade namespace used by the bakery flour bin); `EquipmentCustody.cs`,
  `MotivePower.cs`, `Supportability.cs`, `ToolKit.cs`,
  `EquipmentCapacityContracts.cs`, `EquipmentConditionProfiles.cs`
  (equipment types used by workstations and gates);
  `HorseTrade.cs` + `HorseRental.cs` (draft work unit dependency);
  `LoggingLogHaul.cs` (log haul type); `Core/Tasks/TaskModels.cs`
  (`TaskDefinition` for the gate stub signature).
- Test fix during this pass, recorded honestly: the shortfall and
  credit tests originally assumed a 0-day transit order arrives the same
  day. Production clamps transit to a minimum of 1 day
  (`ExternalMarketAuthority`, `Mathf.Max(1, counterparty.TransitDays)`),
  so a day-0 order arrives day 1. The tests were fixed to
  `TransitDays = 1` + `ReceiveArrivals(1)`; production behaviour was not
  changed to suit the tests.

## 12. What this pass did NOT prove

- No Unity compilation, Test Runner, PlayMode, scene, prefab, or
  Inspector result of any kind.
- No Main Scene behaviour, no tick driver running on a schedule, no
  journey, no residence, no postal, no production supplier.
- No Bakery staged chain output of any kind; no bread from the staged
  chain reached a shelf or a household.
- No fuel from the old generic converter path (`SharedBusinessRuntimeManager`
  FuelDealer archetype, `FuelDealerOutputUnitsPerInput` 5); that path
  stays unrestored.
- No slice-resident wage employment (pass 8 wage tests use a non-slice
  test employee, as in pass 7); no Bakery or Fuel Dealer owner-draw rule
  as canon (bridge caller-supplied reserves only); no combined
  daily-plus-weekly netting in production (proved in Roslyn only).
- No credit for bread or fuel; no write-off or forgiveness; no credit
  policy asset.
- No person-owned horse feed under the production resolver; no horse
  rental, trade, or world view; no `FreightRoute` object; no D1 visuals.
- No first-cycle self-sufficiency: business seeds are still inputs.
- Full-regression status beyond the Roslyn full-set result below is
  NOT claimed.

## 13. Verification (Roslyn harness, phase 8)

- New phase 8 tests: 12 passed, 0 failed
  (`PassXlPhase8ThreeBusinessTests` x8, `PassXlPhase8FreightAndHorsesTests` x4).
- Earlier phases still passing in the phase 8 harness: full set
  374 passed, 3 failed. The 3 failures are exactly the pre-existing
  Phase H failures, by name:
  `HouseholdVerticalSlicePhaseHTests.EndToEndTrace_InsufficientFunds_FailsThenRecovers`,
  `HouseholdVerticalSlicePhaseHTests.Secondary_Hardship_MissedMeals_NoInvisibleReplenishment`,
  `HouseholdVerticalSlicePhaseHTests.Secondary_Restock_ProcurementChainRefillsThenSaleSucceeds`.
- Phase 7 harness re-run as a baseline check in this pass: 362 passed,
  3 failed, same 3 failures by name. 374 - 362 = 12, exactly the new
  phase 8 tests. No regression.
- Slice gate re-verified in the composed run (section 1 controls).
- General Store single-SKU guard untouched; category stock never read
  or written by the slice; one cash writer (`HouseholdLedger` via
  `BusinessRuntimeState` spend/credit and ledger inflows) and one debt
  authority (`FinancialObligationAuthority`) throughout; no new direct
  cash mutation introduced by this pass.
