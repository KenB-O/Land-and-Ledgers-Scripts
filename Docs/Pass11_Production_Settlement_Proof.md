# Pass XI — Production Settlement Proof (WS4)

## Scope statement (read first)

This document reports a **30-day production-driven settlement proof** for
Land & Ledgers, run in the Roslyn source harness
(`Tests/Editor/Integration/Pass11Ws4SettlementProof.cs`, test classes
`Pass11Ws4SettlementProofTests`, `Pass11Ws4SubsystemProbeTests`,
`Pass11Ws4MidTaskReservationTests`).

**This is NOT the Unity RUN-004 thirty-day proof.** RUN-004 stays open
until it is performed in Unity (compilation, Test Runner, PlayMode). Every
result below is a Roslyn harness result obtained by driving the actual
production authorities outside Unity. No Unity compilation, Test Runner,
or PlayMode success is claimed anywhere in this document.

The scenario uses the real production bootstrap, systems, drivers, and
authorities wherever they can be instantiated outside Unity. There is no
alternative simplified economy: all mutable state lives in production
authorities (`HouseholdLedgerRegistry`, `FinancialObligationAuthority`,
`EmploymentRelationshipRegistry`, `BusinessRuntimeState`,
`BusinessLenderService` + `BankDepositLedger`, `ExternalMarketAuthority`,
`HousingAuthority` + `TenancyPaymentService`,
`NpcBusinessLifecycleService`, `PropertyTaxService` +
`CivicTreasuryService`, `FreightShipmentService`,
`EquipmentReservationService`, `MoneyOrderService` (+ `PostalService`),
`NpcConstructionDriver`, `NpcBusinessFormationService`, `MineAssayResult`).
The scenario class only orchestrates day phases, applies dated beats,
and reconciles. Where a subsystem could not be exercised because its
production path is missing from the harness, that blocker is named
plainly below — never silently substituted with a fixture.

No production code was modified for this proof. The only file added is
the proof test file above. No test was deleted, no assertion was
weakened, and no new failure was reclassified as historical.

## Duration, population, businesses

- **Duration:** days 0–29 (30 days), 13 phases per day (production,
  freight advance, retail, Friday-gated payroll, bank + postal advance,
  dated beats, construction drive, tax advance, objective evaluation).
- **Population:** 5 persons / 5 households.
  H1/P1 miller, H2/P2 baker, H3/P3 farmer, H4/P4 landlord/investor,
  H5/P5 miner.
- **Businesses:** `mill-1`, `bakery-1`, `farm-1`, `store-1`, `mine-1`.
  Goods: `grain`, `flour`, `bread`, `ore`.
- **Prices (cents):** grain 35, mill flour 55, import flour 75, bread
  wholesale 75 → 90 on day 22, bread retail 100 → 120 on day 25, ore 20.
- **Wages (cents/week):** mill 600, bakery 500, farm 450, mine 450.
  Household rent 800 per 15 days, boarding 300 per 15 days, bakery
  commercial rent 1000 weekly on Tuesdays.
- **Opening cash:** mill 15000, bakery 8000, farm 10000, store 20000,
  mine 500; H1 3500, H2 4000, H3 5000, H4 80000, H5 1500; bank charter
  2,500,000 from "charter investors"; lumber-yard purse 0; treasury 0.
- **Opening goods:** farm grain 300, mill grain 220, mill flour 60,
  bakery flour 40, bakery bread 20, store bread 30. Yard: fieldstone
  1000, lumber 650. External counterparty `kansas-city` ("Kansas City
  grain house"), budget 100000, transit 3 days, flour supply 200.
- **External boundary (legitimate off-map counterparties, explicit):**
  `kansas-city` for grain export and flour import; the postal system
  (PMG float, see postal section) for money orders. Nothing else leaves
  or enters the town ledger.

## Dated beats (day : event)

1 formation validation (valid intent passes, provenance-free asset
refused) · 2 mill loan 12000 approved · 3 H3 bank deposit 1000 ·
5 freight ship-1 farm→mill, 60 grain · 6 wagon contention probe (live
ship-1) · 7 money-order issue, H1 → H2, 2500 + 25 fee · 8 cabin-1
commission + freight ship-2 farm→mill, 40 grain · 9 ship-2 delay
("bridge washed out") + unaffordable purchase + money-order redemption ·
10 harvest +400 grain · 11 bakery loan 25000 refused + ship-2 resume ·
12 empty supplier (500 flour refused, finite supply 200) + ship-2
delivers · 13 overweight shipment (1000 units) blocked pre-load, zero
cargo · 14 household rents settle · 16 declining demand (bread per
household per day 2 → 1) · 17 flour import 30 at 75c, arrives day 20 ·
18 mine assay (0.02 oz/ton vs 0.10 break-even) + mine payroll suspends ·
20 mine closure + tax levy · 22 wholesale price raise + cabin-2
commission · 24 H3 bank withdrawal 1000 · 25 retail price raise + mill
tax payment · 26 farm grain export 100 at 35c · 27 bakery oven repair
500 to H3 · 29 household rents settle.

## Production, employment, financial movement, deliveries, inventory

- **Production:** milled 300 grain → 240 flour + 60 bran (Mondays, 100
  grain → 80 flour + 20 bran waste; skipped when grain short, days 21
  and 28). Baked 100 bread (5 Monday batches of 20). Farm labor +200
  grain; harvest +400 grain. Mine dug 30 ore, sold 30.
- **Employment:** mill, bakery, farm employments Active at day 30.
  Mine employment Suspended day 18 (payroll cash-short: mine held 200
  before the Friday payroll, so the 450 wage posted a real FOA Payable
  instead of paying cash the mine did not hold), then Ended day 20 by
  the orderly closure (staff released = 1). No person holds more than
  one active employment at day 30.
- **Financial movement (cash legs, all through production paths):**
  payroll Fridays days 4, 11, 18, 25; household rents days 14, 29;
  bakery commercial rent Tuesdays; wholesale flour 20/week mill→bakery
  (days 0, 7, 14, 21; the day-28 purchase was refused — the bakery held
  850c against the 1100c price — and the bakery baked from stock on
  hand instead); wholesale bread 20/week bakery→store; retail bread to
  households; mill loan 12000 day 2; H3 deposit 1000 day 3, withdrawal
  1000 day 24; oven repair 500 day 27; mill tax 900 day 25.
- **Deliveries:** freight delivered 100 units (ship-1 60 on day 7,
  ship-2 40 on day 12 after the day-9 delay and day-11 resume). Cargo
  in transit at day 30: 0. Farm stock deducted exactly once per
  shipment, at load completion.
- **Inventory at day 30:** grain 720 (farm 700 + mill 20), flour 270
  (mill 220 + bakery 50), bread 32 (bakery 20 + store 12), ore 0.
  Yard: fieldstone 920 (1000 − 40 − 40), lumber 50 (650 − 600).
- **Ending cash:** mill 28100, bakery 1650, farm 11700, store 24160,
  mine 0; households H1 0, H2 4260, H3 3260, H4 59295, H5 0; bank vault
  2488000; lumber-yard purse 28000; civic treasury 900.
- **Construction:** cabin-1 (parcel-9, H4) completed day 12 — both
  packages complete, exactly 1 P4 occupancy in its building. Cabin-2
  (parcel-10, H4) commissioned day 22; foundation complete, framing
  stalled (50 lumber on hand vs 600 required). Extra drives on days
  30–32 change neither the state hash nor any cash balance.
  Construction was paid from real stores only (H4 → yard purse 28000:
  2000 + 24000 + 2000); no free construction.

## Adverse outcomes (genuine failure, no injected resources)

| Condition | Observed outcome |
|---|---|
| Unaffordable purchase | H5's 50-loaf purchase refused by the ledger; store stock unchanged |
| Unaffordable payroll | Mine day-18 payroll suspended; FOA Payable created, no cash paid |
| Empty supplier | 500 flour refused (supply 200); no cash moved |
| Delayed shipment | Ship-2 delayed day 9, resumed day 11, delivered day 12 |
| Rejected shipment | 1000-unit shipment blocked pre-load day 13, zero cargo; load refused |
| Uneconomic mine | Assay 0.02 vs 0.10 break-even; mine closed orderly day 20 |
| Material-short construction | Cabin-2 framing stalled at 50/600 lumber; no partial consumption |
| Refused bank application | Bakery loan 25000 refused day 11; no debt created |
| Unpaid rent | H1 rent arrears 340 (day 14) + 225 (day 29); H5 boarding arrears 100 — all real FOA Payables |
| Declining demand | Bread per household per day 2 → 1 from day 16; retailed 118 |
| Worker departure | Mine worker's employment Ended day 20 via closure |
| Passed scenario deadline | Bread-120 objective FAILED at its day-25 deadline and stayed failed: supply by day 25 was at most 110 loaves (opening 30 + wholesale 20 on days 0, 7, 14, 21), so 110 < 120 was guaranteed by stock, not by tuning. The failure was recorded, not extended, and did not retroactively complete |

Refusals logged in the run (7): formation provenance refusal, blocked
shipment load refusal, wagon direct-reserve refusal (see below),
second money-order redemption refusal, unaffordable purchase refusal,
bakery loan refusal, empty-supplier refusal. No diagnostic line
contains "unexpectedly" or "(finding)" — every defensive branch stayed
untriggered.

**Wagon contention, observed:** while ship-1 was live, the freight
service's `FindExclusivityConflict` for `wagon-1` was non-null
(asserted), and a direct `Reservations.Reserve("asset:wagon-1", …)`
was refused naming the freight holder — the freight hold is enforced
at both the service level and the reservation-authority level
(capacity defaults to exclusive 1).

**Scenario progression (own objective ladder):** freight-delivered
(60 units) by day 10 — met; cabin-complete by day 20 — met (day 12);
mill-tax-paid by day 25 — met; bread-120 by day 25 — FAILED at
deadline (above). The saloon-specific `SaloonCircuitGoalEvaluator` was
not driven: see "Not exercised" below.

## Conservation results (all exact, no rounding tolerance)

- **Cash:** initial 2647500 + boundary inflow 4100 (grain export 3500
  + ore sales 600) − boundary outflow 2275 (flour import 2250 +
  postal fee 25) + liquidation 0 = **2649325 expected = 2649325
  actual**. Modeled scope: 5 business tills + 5 household ledgers +
  bank vault + lumber-yard purse + civic treasury.
- **Goods:** grain 520 + 400 harvested + 200 labor − 300 milled −
  100 exported = 720 ✓ (freight is internal). Flour 100 + 240 milled
  + 30 imported − 100 baked = 270 ✓. Bread 50 + 100 baked − 118
  retailed = 32 ✓. Ore dug 30 = sold 30, ending 0 ✓. Bran 60 = milled
  / 5 ✓.
- **Debts:** the obligation authority's `Reconcile()` returns empty;
  every obligation names a debtor and a creditor; 5 obligations,
  12996 outstanding: mill loan 12081, H1 rent arrears 340 + 225, H5
  boarding arrears 100, mine payroll payable 250 (450 − 200 paid at
  closure). Mill loan outstanding ≥ 12000; no bakery loan exists.
  Bank deposits owed 0; bank vault 2488000 = 2500000 charter − 12000
  loan (no installment fell due in-window; 81c interest accrued on
  the obligation only).
- **Treasury and tax:** mill parcel 900c paid in full day 25 into
  `CivicTreasuryService` (`TaxReceived`); treasury balance 900.
  Landlord parcel 1200c unpaid → Delinquent from day 26 with daily
  interest, ending owed 1204. No unexplained `OtherCredit`.
- **Postal books (reconciled separately — transit store, not town
  cash):** po-a funds 2500 + po-b funds 2500 = 5000 (5000 PMG-authorized
  reserve + 2500 issued − 2500 paid); fees collected 25; claims ≥ 1;
  order status Paid; second redemption refused. The sender's 2525
  outflow left town cash at issue and 2500 returned to the payee's
  ledger at redemption; only the 25 fee left town across the boundary.
- **Ownership/custody:** freight custody callbacks credit the
  destination runtime exactly once per delivery; construction occupancy
  exactly 1 P4 in cabin-1's building; mine closure residual and
  creditor payment land in modeled ledgers (see creditor-bridge note
  below); no phantom money or goods anywhere in the run.

## Save / restore: exactly-once continuation (all passing)

Each continuation test captures a snapshot at the midpoint, discards
the pre-save instance (as a real load discards the pre-save session),
restores into fresh authorities, and continues to day 30; the restored
run's deterministic state hash must equal the uninterrupted run's hash
(cash, stocks, liabilities, obligations, employments, shipments,
projects, taxes, counters, postal funds, reservations — diagnostics
excluded, as logging is not state).

| Midpoint | Restore point | Result |
|---|---|---|
| Mid-shipment | (day 7, phase 0): ship-1 InTransit, loaded | hash equal |
| Mid-transaction | (day 8, phase 0): money order Issued, advice not yet received | hash equal |
| Mid-employment | (day 4, phase 5): Friday, after mill payroll, before bakery payroll | hash equal |
| Mid-production | (day 21, phase 1): Monday, after mill grind, before bakery production | hash equal |
| Mid-tax-obligation | (day 21, phase 0): mill assessment Assessed, paid 0 | hash equal |
| Mid-objective | (day 16, phase 0): end of day 15 — freight met, bread neither met nor failed | hash equal |
| Double-restore | (day 8, phase 0), restored twice | hashes equal before continuing either instance; continued run hash equal |
| Mid-Task / reservation (freight) | 60-unit shipment mid-load (1 minute worked, source not yet deducted) | source deducted exactly once; recipient custody exactly 60; `VerifyConservation` clean |
| Mid-Task / reservation (equipment) | Wagon reserve/refuse/release round-trip through the DTO | double-booking refuses naming the holder; reservation survives load; release frees; re-grant succeeds |
| Mid-batch (bakery staged chain) | 24-unit batch mid-chain, save/load into a fresh chain over the same authorities | batch completes; bread out > 0 |
| Farm driver | Plant/tend/harvest with seed provenance, save/load mid-cycle | production exactly once; re-drive consumes 0; save/load consumes 0 |

**Determinism:** two fresh full runs produce identical state hashes.

## Production save/load findings (reported, not hidden)

AMENDMENT (Pass XI follow-up fix, after WS4 delivery): both gaps below
are now FIXED in production (fix commit 9465fdd, proof-compensation
removal commit 82648b6, merged into muse/continuous-development).
The proof restore compensation described below has been REMOVED, and
all 21 proof tests still pass, so the proof now exercises the
production fix rather than compensating for its absence. The original
finding text is preserved below for the record, with the fix noted on
each item. The proof's restore path originally compensated for two
fields that production save DTOs did not persist. Both compensations
were exact for this scenario (the compensated values were fully
determined by restored state), but a real Unity load would have hit
both gaps, so they were recorded here for the coordinator rather than
fixed silently in the proof:

1. **Construction id sequence** (`ConstructionProjectExecutor.sequence`,
   `Domains/Economy/Businesses/Construction/NpcHomeConstruction.cs`):
   `ConstructionProjectSaveDto` carries projects but not the executor's
   sequence, so a fresh executor restarts at 0 and the next commission
   reuses an existing project id (observed: cabin-2 minted as `cproj-0`
   after restore, colliding with cabin-1). The proof restore recomputes
   the sequence as one past the largest numeric suffix among all
   restored project and package ids — exact because ids are never
   deleted. FIXED: `ConstructionProjectSaveDto` now carries additive
   `ExecutorSequence` (default -1 = absent); restore takes
   max(persisted, recomputed floor), so old saves without the field
   still load without collision. Covered by
   `Pass11Ws4SaveLoadFixTests` (uniqueness, old-save, zeroed-field,
   persisted-ahead cases).
2. **Freight dispatch-authority map** (`FreightShipmentService.dispatchAuthorities`,
   `Domains/Economy/Logistics/FreightShipmentService.cs`):
   `FreightServiceSaveDto` restores shipments and re-tracks them but
   does not repopulate the private map from shipment id to reservation
   authority, so a restored in-transit shipment completes without
   releasing its wagon reservation and the next shipment refuses on a
   stale hold (observed: ship-2 never created after a mid-shipment
   restore; lingering reservation = 1). The proof restore rebinds the
   map for every restored shipment that was already dispatched and is
   not yet terminal. FIXED: `FreightServiceSaveDto` now carries
   additive `ShipmentIdsWithDispatchAuthorities`; restore rebinds the
   map for transport-bound live shipments (empty list on old saves
   falls back to shipment state), release stays idempotent, and
   double restore only overwrites the same map key. Covered by
   `Pass11Ws4SaveLoadFixTests` (restore-then-complete release,
   double-restore release, old-save release, fresh-run control, later
   shipment not refused, recipient credited exactly once).

**Creditor-bridge wiring note (scenario-side, not a production
defect):** `CloseOrderly` pays open obligations from the closing
business's till and credits the creditor through the lifecycle
context's creditor bridge, flagging explicitly when no store is
registered. The mine payroll payable names its creditor `person:5`
(the employee), so the scenario registers the employee's household
ledger as the `person:5` store in both the live and restored bridges.
Without that registration the 200c closure payment left the mine till
and landed nowhere modeled (observed as a 200c cash gap before the
registration was added); with it, cash conserves exactly and the
payment lands in H5's ledger.

## Subsystems not exercised, with blockers

- **Saloon Circuit goal ladder** (`SaloonCircuitGoalEvaluator` +
  `ISaloonCircuitGameState`): not driven. It is saloon-specific
  (ownership, qualifying saloon sales, Crown House rank, scenario
  debt) and the WS4 town has no saloon businesses. The evaluator
  itself is covered by WS1/WS6 tests. Scenario progression in WS4 is
  proven via its own objective ladder plus lifecycle events.
- **Boarding-house business runtime:** not driven as a business. Its
  tests are excluded from the harness subset, so boarding is exercised
  as tenancy occupancy + boarding payment terms, not as a boarding
  business.
- **Unity-side bootstrap** (`SimulationSystemsHub`, scene/service
  bindings, `ScenarioUnityWiring`): cannot be instantiated in the
  Roslyn harness. The proof drives the production authorities directly,
  which is the documented harness scope.
- **Full business formation execution** (`NpcBusinessCreationAdapter.TryCreateBusiness`):
  not executed. The adapter file is not in the harness subset, so the
  formation probe drives production `Validate` with a stub port
  (`Validate` only null-checks the port and never invokes it); the
  valid intent validates with zero problems and the provenance-free
  intent is refused. Adding
  `Domains/Economy/Creation/NpcBusinessCreationAdapter.cs` to the
  harness subset would allow a real-port probe; it is not required for
  any result reported here.
- **Player tax driver in the main loop:** civic taxation in the main
  loop uses `PropertyTaxService` + `CivicTreasuryService` directly.
  `PlayerPropertyTaxDriver` is exercised only in the once-per-year
  probe (levy once per tax year across `ResolveWeek(7/14/21)`), and
  assessment status is never asserted for the driver, because the
  known P5 test-side defect concerns the Assessed-vs-Delinquent
  expectation (see baseline failures below).
- **Saloon / ScenarioModels content:** not driven (no saloon
  businesses in the WS4 town).

## Verification

- Focused proof tests: **21 passed, 0 failed**
  (`Pass11Ws4*`).
- Full harness on this branch (csproj regenerated from the worktree,
  grep-verified to include `Pass11Ws4SettlementProof`, 0 build
  errors, isolated output paths): **1374 passed, 3 failed**.
- Baseline on the branch base was 1353 passed, 3 failed; the delta is
  exactly the 21 new proof tests, with zero regressions.
- The 3 failures are the known, deliberately-failing baseline
  failures (documented test-side defects; their assertions were not
  touched):
  - `MoneyOrderServiceTests.IssueDuplicate_ReplacesLostOrderFree`
  - `MoneyOrderServiceTests.RunSettlementCycle_NetsAndSettlesInFull`
  - `P5PropertyLegalJourneyTests.Driver_LeviesAnnualAssessmentOncePerTaxYear`
- **Harness-subset additions required at merge: none.** The proof file
  lives under `Tests/Editor/Integration/`, which the shared
  `sources.txt` already includes as a directory. The only follow-up
  (optional, not required) is the formation-adapter file noted above.
- **Production defects fixed in this work: none** (no production file
  was edited). The two save/load gaps above are reported as findings
  for the coordinator, with exact files and the exact compensation
  used in the proof restore path. AMENDMENT: this bullet was true at
  WS4 delivery and is superseded by the follow-up fix above (commits
  9465fdd and 82648b6); the gaps are now fixed in production and the
  compensation is removed.
