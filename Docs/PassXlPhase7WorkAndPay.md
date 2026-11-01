# Pass XL Phase 7 — Work and Pay Inside the Slice

Base: `muse/passxl-slice-phase6` at `401d3c77f04722e594d4ae7c7295674a0673c257`.
Pass 7 is the second of three separate sequential vertical-slice passes
(6, 7, 8). It does NOT redo passes 1-6, and it does NOT start pass 8.

## The question this pass answers

Pass 6 proved flour to meals with save/restore, but opening cash was an
explicit run input simulating prior earnings, booked as OtherDocumented
inflows. The fixture itself is 0 capital. Pass 7 asks: can the slice
earn its own cash inside the slice, so earnings stop being a run input?

Short answer, honestly split in two:

1. **Employee wages: yes, through the existing payroll path, proved
   in Roslyn.** But the six-person slice has no employees to pay.
2. **Founder earnings: yes, but NOT as wages.** The three founders are
   operators, and operator income is profit/equity draw, never payroll.
   The composed run proves a General Store owner draw funding two
   purchases from a household that starts at 0. The first cycle's
   business seed is still an input, and is labelled as one.

## Payroll audit (existing path, no second authority invented)

- **Authority:** `EmploymentRelationship` is the sole wage record
  (PKG-6), with agreed weekly wage terms living on the relationship
  only (`Domains/Economy/Core/EmploymentRelationship.cs`).
- **Settlement:** `WageSettlementService`
  (`Domains/Economy/Labor/WageSettlementService.cs`) guards and
  records; it never touches cash itself. Weekly payroll delegates to
  `BusinessRuntimeState.ResolveWeeklyPayroll`; partial periods pay
  through the same cash path via `BusinessRuntimeState.PayAgreedWage`.
- **Cash:** business cash moves only through `BusinessRuntimeState`
  (single-writer spend/credit); household cash moves only through
  `HouseholdLedger.RecordWagePayment` (source WageEmployment,
  employment id as provenance). `MineLaborRegister.SettleShiftWages`
  is a mine-specific shift path with the same household-ledger
  pattern; it is NOT used here, because the slice has no mine and no
  shift roster, and inventing a mine payroll for a general store
  would be a second path in disguise.
- **Attendance is the work proof:** `BusinessRuntimeState` pays a
  weekly wage only for weeks with caller-supplied attendance
  (`WeeklyAttendanceProvider`). No attendance means no pay and no
  liability, not a paid week. A throwing attendance lookup is
  treated as absent, never as paid.
- **Shortfall is arrears, not a fake payment:** when business cash
  cannot cover a wage, the employment is suspended
  (`EmployerPaymentDefault`) and the wage accrues as an operating
  liability (`AccruedLiabilityCents` plus a named payable), exactly
  as the existing path defines it. No cash is created to cover it.
- **Exactly-once:** `WageSettlementService` settled-week markers
  make a repeat weekly settlement return "already-settled" with no
  movement, and `PayAgreedWage` manual-payment records make a repeat
  partial period for the same employment/day/label return "refused".
  Weekly payroll also nets manual cash already paid that week, so a
  partial payment plus a full weekly payment cannot pay a week twice
  (that netting is existing behaviour, exercised by the partial test
  only in the sense that the manual record exists; the combined
  manual-plus-weekly netting case is a Codex/driver test, not
  claimed here).

## Owner vs employee (the distinction this pass exists to enforce)

- TECH 4.1 taxonomy (`Core/Primitives/WorkRelationship.cs`):
  `FarmOrBusinessOperator` compensation is "profit/equity draw,
  never payroll"; `FamilyEnterpriseLabor` likewise creates no wage
  employment or payroll entries. `EmploymentRelationshipProjector`
  skips `owner:` slots for the same reason.
- Therefore: Kennedy (General Store), Glen (Bakery), and Jacob (Fuel
  Dealer) are operators. `SliceWorkAndPay.RegisterEmployee` refuses
  any attempt to register a founder as a wage employee of his own
  business, creating no record and no wage. `RecordOwnerLabour`
  records days worked with `WageCents` always 0.
- Owner earnings use `HouseholdIncomeSource.OwnerDraw` (business id
  as source reference, reason naming profit/equity draw, never a
  wage). The bridge `DrawOwnerEarnings` moves caller-supplied cents
  from `BusinessRuntimeState` cash to a household ledger, capped by
  a caller-supplied reserve that stays in the business, and records
  the draw on the runtime (`RecordWeeklyOwnerDistribution`) for
  readout. It is NOT the full `PlayerPortfolioManager`
  checkpoint/grace/reserve rule: that manager is Unity-wired and
  computes its own reserve and growth rules, and this pass does not
  reimplement or replace it. The bridge draw amount and reserve are
  test parameters, never a canon draw rule.
- The employee wage tests therefore use a clearly labelled non-slice
  test employee (person 99/98) and a non-slice test business
  (`general-store-test`), to prove the mechanism. They do NOT claim
  a slice resident was paid, and no slice resident's employment
  record was created in this pass.

## What pass 7 proved (Roslyn only, never Unity)

Bridge and composed run: `Orchestration/Player/SliceWorkAndPay.cs`.
Tests: `Tests/Editor/Population/PassXlPhase7WorkAndPayTests.cs`
(11 tests). Harness: `harness-passxl-phase7.csproj` (outside the
repo, copied from phase 6, retargeted to `repo-passxl7`, plus
`WageSettlementService.cs`, the bridge, and the new test file).

Employee wage path (non-slice test employee, test parameters only):

- Weekly payroll pays once: business 5000c -> 4680c, household
  0c -> 320c, one PayrollEvent entry Paid, and a repeat settlement
  of the same week returns "already-settled" with no movement.
- No attendance earns nothing and accrues nothing; the employment
  stays Active.
- Insufficient cash accrues liability and suspends: business stays
  at 200c, household stays at 0c, liability 320c, employment
  Suspended with reason EmployerPaymentDefault.
- Partial period pays a prorated wage once: 400c weekly wage,
  2 of 7 days, prorated to 115c via the existing
  `WageSettlementService.ProrateWeeklyWage`, business 1000c ->
  885c, household 0c -> 115c, and a repeat of the same period
  label/day is refused with no movement.
- Unknown employment, wrong-business employment, and dead/off-map
  (unavailable) workers are all refused with no cash movement.
- The procurement cash port (`BusinessRuntimeCashPort`) is the SAME
  cash as payroll: an over-debit is refused, a 1250c procurement
  debit and an 800c sale credit land on the runtime balance exactly.

Composed earn-then-spend run (General Store only, all numbers are
test parameters, never canon):

- Household starts at 0c. Business starts with 5000c owner equity
  (labelled seed, NOT wages, NOT slice earnings).
- Kennedy owner labour recorded: 7 days, 0c wage.
- Draw 1: 1000c, reserve 1000c kept. Business 5000c -> 4000c,
  household 0c -> 1000c.
- Procurement: 100u flour at 10c + 250c freight = 1250c,
  CashOnArrival, transit 3 days, test supplier `test-grain`.
  Business 4000c -> 2750c on arrival.
- Sale 1: 40u at 20c = 800c, atomic sell-and-deliver to the
  Baldwin-Ooms household. Business 2750c -> 3550c, household
  1000c -> 200c.
- Draw 2: 500c, reserve kept. Business 3550c -> 3050c, household
  200c -> 700c.
- Sale 2: 20u at 20c = 400c, from the remaining store lot.
  Business 3050c -> 3450c, household 700c -> 300c.
  Store lot remaining: 100u - 40u - 20u = 40u.
- Meals: 1 meal day on the household inventory after sale 2 serves
  meals (>0) and consumes flour (inventory below 60u), with no cash
  moved by meals.
- Reconciliation is exact at every step: business final 3450c =
  5000 - 1000 - 1250 + 800 - 500 + 400; household final 300c =
  0 + 1000 - 800 + 500 - 400. No double pay, no pay for unworked
  time, no pay when the business cannot afford it (that case is the
  arrears test, not the composed run).
- A control run with draw 2 = 0 returns null with a diagnostic
  instead of inventing cash: the household holds only 200c after
  sale 1 and cannot fund a second purchase without draw 2.

## What pass 7 did NOT prove (read this before claiming a working slice economy)

- **No Bakery or Fuel Dealer earnings.** Glen and Jacob draw
  nothing in this pass: no Bakery production/sale and no Fuel
  production/sale is proved (phase 4 F16/F17, Fuel task-to-wood-
  lot path). Do not describe this as "the three founders all earn".
- **No slice-resident wages.** As above, the employee tests are a
  mechanism proof with a non-slice employee. A slice household
  earning wages requires a real hire (a non-founder person, a real
  employment record, real attendance) in a later pass or in Main
  Scene, not a founder reclassified as an employee.
- **No first-cycle self-sufficiency.** The business seed (5000c
  owner equity) is still an input. A 0-capital store cannot order
  on CashOnArrival, and a 0-cash household cannot buy before it
  has drawn or earned. What pass 7 removes is the HOUSEHOLD run
  input (phase 6 gave the household 10000c directly); the second
  purchase is funded by cycle-1 earnings, not by a fresh input.
  Removing the business seed as well would require either a real
  owner-equity injection path in production, or a TradeCredit
  first order plus a household credit path that does not currently
  exist (phase 1 household sales are cash-only) — both are
  decisions/drivers, not things this pass fakes.
- **No Main Scene behaviour.** No scene, prefab, Inspector, tick
  driver, journey, or PlayMode step is proved here. The bridge
  calls payroll explicitly; a real driver must call it weekly.
- **No combined manual-plus-weekly netting proof.** The netting
  logic exists in `BusinessRuntimeState` and the manual record
  exists, but no test in this pass pays a partial period and then
  runs the same week's weekly payroll to prove the net. That
  combination is a follow-up test, not a claim made here.
- **No owner-draw rule as canon.** The bridge draw is caller-
  supplied amount plus reserve. The production draw rule lives in
  `PlayerPortfolioManager.ResolveOwnerDistribution` (checkpoint,
  grace, survival reserve, growth divisor) and is NOT proved or
  replaced here.

## Seams where the run stopped (exact driver/file names for Codex)

1. Daily/weekly payroll driver — `Orchestration/Systems/SimulationDrivers.cs`
   / `FirstLedgerSliceBootstrapper`: no production driver calls
   `WageSettlementService.SettleWeeklyPayroll` or the owner-draw
   bridge on schedule in Roslyn. The bridge is the reference, not
   the driver. The driver must also stamp `PayrollDayIndex` and
   supply attendance from real worked days, or weekly payroll
   falls back to legacy behaviour (attendance check skipped).
2. Bakery production — `Domains/Economy/Businesses/Bakery/BakeryStagedProductionChain.cs`
   and `BakeryShopRuntime`: no Main Scene owner/driver, no
   BreadShelf-to-SKU transfer, no flour feed (phase 4 F16/F17).
3. Fuel Dealer production — no Task to tree depletion to wood lot
   to sale path.
4. Production owner-draw rule — `Domains/Economy/Finance/PlayerPortfolioManager.cs`
   `ResolveOwnerDistribution`: Unity-wired, checkpoint/grace/
   reserve rules not proved in Roslyn; the bridge draw must not be
   mistaken for that rule in Main Scene.
5. Business cash persistence — production maps the runtime to its
   own save path and must register the procurement service with
   `SaveLoadManager.PassXlProcurementService` before the first save
   (phase 4 F19), as in phase 6.
6. Supplier registration — `OffMapSupplierCatalog.RegisterSupplier` /
   production supplier data: the run supplier is a test
   counterparty (phase 4 F37).
7. Postal office — `PostalService.RegisterOffice`: no recruitment
   letter is posted in this run (phase 4 F04).
8. Residence assignment — `HousingResidence` runtime: not used by
   this run, as in phase 6.

## Codex handoff (from this pass only)

- Wire a weekly payroll driver that, in order: stamps
  `PayrollDayIndex`, supplies attendance from real worked days,
  calls `WageSettlementService.SettleWeeklyPayroll` for each
  business, and reports arrears/suspensions loudly; do NOT pay
  founders as employees, and do NOT pay weeks with no attendance.
- Wire owner draws through the production
  `PlayerPortfolioManager` rule in Main Scene, not by copying the
  bridge's caller-supplied draw into production; the bridge proves
  the cash conservation (business down = household up), not the
  draw policy.
- Confirm fixture-to-production person/household ids before
  calling this run "the slice" (phase 6 roster note, unchanged).
- Close Bakery (Receive API + scene owner + flour feed) and Fuel
  (Task to wood lot to sale) before claiming a three-business
  economy or three-founder earnings.
- Decide, with Kennedy, the first-cycle funding question below
  before claiming the slice is self-funding from day 0.

## Design forks needing Kennedy's decision (nothing guessed, all test parameters only)

- Which payroll path pays slice workers: confirmed here as
  `WageSettlementService` + `BusinessRuntimeState` for wage
  employees, and owner draw for founders — but the production
  driver wiring and the production draw rule are still Codex work.
- Owner vs employee treatment: founders are operators, never wage
  employees (TECH 4.1). If Kennedy wants a founder to also draw a
  wage, that is a canon/taxonomy decision, not something this pass
  can grant by reclassifying them.
- Wage terms: all wages in this pass (320c weekly, 400c weekly,
  115c prorated) are test parameters only, never canon wages.
- Whether opening cash is still needed for the first cycle: yes,
  for the business seed, as proved above; the household input is
  removed, the business seed is not. Removing it needs either a
  production owner-equity injection path or a TradeCredit +
  household-credit path, both undecided.
- Whether a slice resident should ever be hired as a wage employee
  (e.g. Emma/Cassidy/Grace, or a future hire at larger town size):
  that changes the fixture roster and is a design decision, not
  assumed here.

## Must NOT claim from this pass alone

Scene wiring, prefab wiring, Inspector values, PlayMode success,
Main Scene visual behaviour, a working three-business economy,
slice-resident wages, founder wages, bread or fuel production, a
canon wage or draw rule, or full-regression status beyond the
Roslyn full-set result in the phase 7 report. No production
freight rate, supplier price, wage, or draw rule is canon here;
all run numbers are test parameters.
