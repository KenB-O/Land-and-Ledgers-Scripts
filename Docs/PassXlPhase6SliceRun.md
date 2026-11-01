# Pass XL Phase 6 — Composed Six-Person Slice Run

Base: `muse/passxl-slice-phase5` at `ad3cd19fb9a273fd791de5cb7e213f1549b25fe9`.
Pass 6 is the first of three separate sequential vertical-slice passes (6, 7, 8).
It does NOT redo passes 1-5, and it does NOT start passes 7 or 8.

## What this pass is

Passes 1-5 proved isolated pieces: procure flour, sell it, deliver it to a
household, settle a newcomer, post a letter, order from a quote. No composed
run proved the six-person slice actually works end to end over time. This
pass builds that run in Roslyn, using ONLY existing phase 1-5 pieces and
existing production code where it exists, with the run orchestrator in
`Orchestration/Player/SixPersonSliceRun.cs` and the run tests in
`Tests/Editor/Population/PassXlPhase6SliceRunTests.cs`.

The orchestrator is a Roslyn slice-run orchestrator, NOT a production
driver and NOT a Main Scene tick driver. It calls the steps explicitly, in
order. Codex must still wire a real daily tick driver in Main Scene.

## Run parameters (all test parameters, never canon)

- Start day 0. Market supplier: test counterparty `test-grain`
  ("Test Grain Co."), 200u flour supply, transit 3 days. NOT a production
  supplier.
- Order day 0: 100u flour at 10c goods + 250c seller-paid freight surcharge
  = 1250c total, CashOnArrival. The 250c-on-1000c pattern is the phase 1
  test parameter, not a canon freight rate.
- Business cash input: 5000c. Household cash input: 10000c per household.
  Both are explicit run inputs simulating prior earnings, booked as named
  `OtherDocumented` inflows with source reference
  `passxl-phase6-run-input`. The fixture itself gives the three businesses
  0 starting capital and no granted inventory or cash, so this run does
  NOT claim the store or households earned their opening cash inside the
  slice. Work and pay inside the slice is a seam below, and is pass 7's
  most important missing leg if a payroll path can be wired safely.
- Sale: 40u flour at 20c = 800c, to the Baldwin-Ooms household (H1),
  acting person Kennedy (P001, `person:1`), on arrival day.
- Meals: 3 meal days on the RESTORED inventory, starting on arrival day,
  default meal policy, Baldwin-Ooms members Kennedy (1) and Emma (2).

## Roster (fixture labels, not production ids)

- Kennedy Baldwin-Ooms (35) and Emma Baldwin-Ooms (32), household 1,
  Baldwin-Ooms General Store.
- Glen Pierce (31) and Cassidy Pierce (29), household 2, Pierce Bakery.
- Jacob Boniecki (34) and Grace Boniecki (30), household 3,
  Boniecki Fuel Dealer.
- Person numbers 1-6 and household numbers 1-3 follow the fixture's person
  order. Production `PopulationState` ids are NOT proved by this run:
  `PopulationGenerator` is Unity-bound and is not run here, so Codex must
  confirm fixture ids match production ids before claiming this run is the
  Main Scene slice.

## Run actuals (real IDs and actuals, never fake zeros)

- Order: `sku-po-general-store-1-0-0`, receipt `EXT-P-*`, arrival day 3.
  Before arrival: 0 lots, 0 sellable flour. Duplicate receipt on day 3:
  0 new lots, still exactly 1 store lot.
- Store lot: `lot-EXT-P-*`, 100u flour on arrival.
- Market after order: supply 200u -> 100u remaining, outflow 1250c.
- Business cash: 5000c start -> 3750c after procurement (1250c paid on
  arrival) -> 4550c after sale (+800c). No double charge.
- Household cash (Baldwin-Ooms): 10000c start -> 9200c after sale (-800c).
  Meals move no cash.
- Sale: `sku-sale-general-store-1-3-0`, 40u flour, 800c, delivered as
  household inventory lot (lot id assigned by `HouseholdInventory`,
  carriage record is the sale's `HouseholdLotId`).
- Store lot after sale: 100u - 40u = 60u remaining, 60u sellable.
  A follow-up 61u sale is refused, moving nothing: no new sale, no cash
  movement, no lot change (proven in tests).
- Save/restore (phase 2 coordinator): slice DTO captured with
  `suppressMigrantArrivals = true`, `treatPlayerAsNpc = false`, then
  restored into a fresh service and a fresh inventory rebuilt from saved
  lots (the phase 2 pattern). Restored counts: 1 order, 1 lot, 1 sale,
  1 delivered mark. Re-delivery of the same sale returns
  `AlreadyDelivered` with exactly 1 inventory lot, adding nothing.
- Meals, on the restored inventory, default policy:
  - Day 3 (arrival day): staple days of supply 40 / (2 members x 3
    expected meals x ~2u) = 3.33, at/above the 3-day midday threshold,
    so midday is served: 6 meals, 12 lb flour eaten, 28 lb left.
  - Day 4: 28 lb, 2.33 days, below threshold, morning + evening only:
    4 meals, 8 lb eaten, 20 lb left.
  - Day 5: 20 lb, 1.67 days, morning + evening only: 4 meals, 8 lb
    eaten, 12 lb left.
  - Total: 14 meals eaten, 0 missed, 28 lb flour consumed, 12 lb left.
- Pierce and Boniecki households: no flour bought in this run, so no
  meals are proven for them. Their meal leg stops at "no supply".

## What the run PROVED

- Stock moves, with identity intact, from off-map order to store lot to
  household inventory lot, through the phase 1/2 paths, without
  duplication on duplicate receipt, on save/restore, or on re-delivery.
- Cash moves once, in both directions, store <-> household, with the
  business cash port and the real `HouseholdLedger` as the only writers
  on this path.
- Meals are actually eaten from real restored lots, not from abstract
  reserve units, and the midday circumstance rule fires exactly as the
  policy defines it (served day 3, withheld days 4-5).
- The six-person slice gate survives save/restore in this run
  (`suppressMigrantArrivals` restored true, NPC flag restored false).

## What the run did NOT prove (read this before claiming a working slice)

- **Flour meals, not bread meals.** Flour is 5th in meal priority at
  2 lb per serving, so the run's meals are flour eaten directly from
  household lots. It does NOT prove cooking, baking, or bread production.
  In a real slice, flour should mostly become bread via the Pierce
  Bakery, which is exactly the missing leg below. Do not describe this
  run as "the town fed itself on bread".
- **No earnings inside the slice.** Opening cash is a run input (see
  parameters). The run does not prove a household or a business can earn
  its next 800c inside the slice.
- **No Main Scene behaviour.** No scene, prefab, Inspector, tick driver,
  journey, or PlayMode step is proved here. The orchestrator calls steps
  explicitly; a real driver must still call them on schedule.
- **No Bakery, Fuel, or other-household economy.** See seams.
- **Meal-count denominator note (not changed):**
  `DailyNeedsService.MealsPerPersonPerDay` is 2, while the default
  `MealSchedulingPolicy` expects 3 meals per day (2 substantial + midday
  when supply allows). This run follows the policy exactly, so it serves
  up to 3 meals per person per day when supply allows. Whether the slice
  should be 2 or 3 meals per person per day is a Kennedy / playtest-feel
  decision, not a bug this pass silently "fixed".

## Seams where the run stopped (exact driver/file names for Codex)

1. Bakery production — `Domains/Economy/Businesses/Bakery/BakeryStagedProductionChain.cs`
   and `BakeryShopRuntime`: no Main Scene owner/driver, no
   BreadShelf-to-SKU transfer, no flour feed (phase 4 F16/F17). No bread
   produced, Pierce household eats no bakery bread in this run.
2. Fuel Dealer production — no Task to tree depletion to wood lot to
   sale path. No fuel produced or sold in this run.
3. Work and pay — no Roslyn-usable wage payroll for General Store /
   Bakery / Fuel Dealer work in this run. In production, wages must come
   via `EmploymentRelationship` payroll; the driver that pays slice
   workers is the single biggest missing leg for pass 7.
4. Daily tick driver — `Orchestration/Systems/SimulationDrivers.cs` /
   `FirstLedgerSliceBootstrapper`: no production driver calls procurement
   arrivals, sale, delivery, or meals on a schedule in Roslyn. The run
   orchestrator is not that driver.
5. Residence assignment — `HousingResidence` runtime: meals use a
   `HouseholdState` id and member list directly, not a residence record.
6. Postal office — `PostalService.RegisterOffice`: no recruitment letter
   is posted in this run; office registration remains Codex-only
   (phase 4 F04).
7. Supplier registration — `OffMapSupplierCatalog.RegisterSupplier` /
   production supplier data: the run supplier is a test counterparty
   (phase 4 F37).
8. Business cash — production maps the run cash port to
   `BusinessRuntimeState` and must register the service with
   `SaveLoadManager.PassXlProcurementService` before the first save
   (phase 4 F19). Business cash persistence is via that runtime's own
   save path, NOT via the procurement DTO; the run sets the restored
   cash port to the after-sale balance explicitly and says so.
9. Household ledger/inventory persistence — the run rebuilds the
   restored inventory from saved lots (phase 2 pattern). Production
   household persistence is via `HouseholdInventoryState` /
   `HouseholdLedgerState` DTOs, not via the slice DTO; SaveLoadManager
   wiring for those DTOs is not proved here.
10. Population generation — `PopulationGenerator` /
    `SixPersonDevelopmentFixture` (ScriptableObject, Unity-only) is not
    run here; fixture-to-production id mapping is Codex-only (see roster).

## Composed-path bugs found

None that required a production fix. The procurement, sale, delivery,
save/restore, and meal paths composed exactly as phases 1-5 claimed:
no double lot, double charge, double delivery, or double person was
found, and the follow-up refusal test proves over-stock sales move
nothing. The only composed-path observations are the flour-meal caveat
and the meal-count denominator note above, both left unchanged as
design decisions, not silently "fixed".

## Tests (Roslyn only, never Unity)

- `PassXlPhase6SliceRunTests.Run_EndToEnd_FlourToMeals_WithSaveRestore_NoDuplication`
- `PassXlPhase6SliceRunTests.Run_Seams_AreNamed_AndOtherHouseholdsEatNothing`
- `PassXlPhase6SliceRunTests.Run_SecondSaleBeyondRemainingStock_Refused_NothingMoves`
- `PassXlPhase6SliceRunTests.Run_ZeroMealDays_FlourRemains_AndCashStillReconciles`
- Harness: `harness-passxl-phase6.csproj` (outside the repo, copied from
  the phase 5 harness, retargeted to `repo-passxl6`, plus the run class
  and the new test file). Full-set result is in the phase 6 report, not
  here, to avoid quoting a number twice from different runs.

## Codex handoff (from this pass only)

- Confirm fixture-to-production person/household ids in Main Scene
  (roster section above) before calling this run "the slice".
- Wire a daily tick driver that, in order, calls procurement arrivals,
  household shopping/sale/delivery, and meal serving on schedule; the
  run orchestrator is the Roslyn reference for that order, not the driver.
- Close the work-and-pay leg first for pass 7: a payroll path that pays
  slice workers via `EmploymentRelationship` so opening cash is no longer
  a run input.
- Then close Bakery (Receive API + scene owner + flour feed) and Fuel
  (Task to wood lot to sale) before claiming a three-business economy.
- Register the production procurement service, production suppliers, and
  the General Store postal office, as in seams 6-8.

## Must NOT claim from this pass alone

Scene wiring, prefab wiring, Inspector values, PlayMode success, Main
Scene visual behaviour, a working three-business economy, slice earnings,
bread production, or full-regression status beyond the Roslyn full-set
result in the phase 6 report. No production freight rate, supplier price,
or meal-count rule is canon here; all run numbers are test parameters.
