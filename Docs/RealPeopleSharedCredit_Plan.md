# Real People, Households & Shared Credit — Phase A Authority Audit Plan

Branch: `muse/real-people-shared-credit` @ `7b1ca29`. Governing: Canon XXXI + Tech XIV.
Proposals 001-105 approved; 106-145 never canon. No calibration figures promoted.

## 1. Authority map

### REAL and working (single truth each)
- **Financial obligations**: `FinancialObligationAuthority` (`Domains/Economy/Financing/`) — loans,
  payables, notes, trade credit, facilities, security interests, guaranties, assumptions,
  refinancing. Balances live here only. Save/load wired via `SimulationSystemsHub`.
- **Credit instruments**: `CreditRegistry` — promissory notes, seller-finance notes, mortgages,
  liens, guaranties as an instrument index; balances via the shared authority. Contingent/called
  guaranty exposure computed, not stored twice.
- **Credit workflow**: `CreditOfferWorkflow` — request/evaluate/counter/accept/collect; cash moves
  between sandboxed `CreditCashAccount`s. `CreditParticipantService` owns no balances.
- **Bank loans (player)**: `PlayerDebtManager` — flow wrapper only; `LoanContract` is a read surface
  over the shared obligation (re-read after every payment).
- **Business liabilities**: `BusinessLiabilityLedger` — borrows/payables into the shared authority;
  local `BalanceCents` re-read from the authority after each mutation ("never a second balance writer").
- **Private lending**: `PrivateLenderFunds` (finite commitments), `BankLoanBook`, `NoteDesk`
  (issues promissory notes + guaranties through `CreditRegistry`), `ForeclosureService`
  (deficiency notes), `PropertyTax` (tax liens).
- **Household cash ledger**: `HouseholdLedger` + `HouseholdLedgerRegistry` — append-only,
  provenance-gated (Canon 13.2: no fiat top-ups; every inflow names a source class), balance
  derived from entries, never stored.
- **Membership**: `HouseholdMembershipRegistry` is the sole membership authority (PKG-8 retired the
  `HouseholdState.memberIds` dual-write; the list is a derived reverse index).
- **Person truth**: `PersonState`/`PopulationManager`; labor availability derived
  (`PopulationHealthState.GetLaborAvailability01`). `PersonArchive` is a genuine departed-person archive.
- **Bank cash**: `BankRuntime` wraps `BankDepositLedger`; two-track (ledger = accounting truth,
  `SpecieLot` vault = physical truth) with `CheckVaultReconciliation` exposing gaps by design.
- **Business cash**: `BusinessRuntimeState.currentCashCents` — all mutations inside that class.
- **Owner cash**: `PlayerPortfolioManager` only.
- **Housing**: `HousingAuthority` (`Domains/World/Property/`) — buildings, spaces, occupancies,
  functional assignments, property agreements, save DTO.
- **Construction**: `ConstructionContract`/`ConstructionContractBook` + `ConstructionProgressBillingService`
  + `ConstructionBillingCashPort`. No Project/WorkPackage execution authority (only a mention in
  `AcquisitionMarketManager`).
- **Save/load**: `SimulationSystemsHub` holds the singletons (obligations, instruments, household
  ledgers) with Capture/Load DTOs; `SaveLoadManager` orchestrates.

### DUPLICATED — two writers, one truth (found this audit)
1. **Household cash** (CONFIRMED, primary finding). `HouseholdState.spendingMoneyCents` (raw int,
   mutated directly) vs `HouseholdLedger` (entry-derived balance):
   - Writers to `spendingMoneyCents`: `GeneralStoreRuntimeManager` Saturday shopping loop (lines ~795,
     ~2314), `SharedBusinessRuntimeManager` doctor services (~line 1999), and
     `DepositWeeklyHouseholdIncome` (~line 2541) which adds `max(fallback, weeklyIncomeSnapshot*100)`
     weekly — fiat replenishment in tension with Canon 13.2.
   - Writers to `HouseholdLedger`: blacksmith/wheelwright repair orders, mine assay, horse trade,
     farm chains, `MineLaborRegister` wage payments, `EmbodiedPurchaseExecutor` ("if the household
     cannot pay, nothing moves").
   - No bridge exists between the two wallets. A household can spend the same economic capacity twice.
2. **Shopping execution** (same root cause): General Store loop pays from `spendingMoneyCents`;
   `EmbodiedPurchaseExecutor` pays from the ledger. Canon 13.4 mandates the embodied
   ProcurementNeed -> acting Person -> supplier -> Transaction chain.
3. **Wage crediting asymmetry**: general payroll (`BusinessRuntimeState.ResolveWeeklyPayrollFromEmployments`,
   28 business types) deducts business cash and credits NOBODY — wage money vanishes. Only
   `MineLaborRegister` credits the worker household (via `RecordWagePayment`). The General Store
   weekly deposit papers over this with a snapshot-based fiat top-up.

### MISSING (foundations exist, runtime unwired)
- Payroll -> household wage crediting for non-mine businesses (Phase B core).
- Rent collection: `BoardingHouseBoarderRegister.RentDueWeekly/Monthly` computes dues but never
  collects from household cash (Phase D).
- `IssueSellerFinanceNote` has no production callers (test only); acquisitions don't issue seller notes (Phase E).
- Trade-credit runtime per Tech 6.4 (invoice terms, arrears, collection stage) — only an obligation kind exists (Phase E).
- `CallGuaranty`/`SettleGuarantyPayment`/`ForeclosureService` unexercised in production flows (Phase E).
- `CreditCashAccount` not wired to real participant cash (Phase E).

### Already-fixed duplicates (no action)
- `CreateBlockedShipment` phantom cargo: FIXED at base (see verdict below).
- `HouseholdState.memberIds` dual-write: FIXED (PKG-8).

## 2. CreateBlockedShipment verdict — ALREADY FIXED, no change made
- Fix commits in base: `c0d88e8` "Implement MR-P001 shipment conservation gate",
  `521fe8c` "T1D: Blocked-shipment invariant audit".
- Current code (`LogisticsRuntimeManager.CreateBlockedShipment`, ~line 1361):
  `remainingQuantityUnits = 0`, `sourceCommittedAtSchedule = false`, `loadApplied = false`,
  `state = LogisticsShipmentStatus.Failed` — a blocked pre-load shipment owns zero cargo and can
  never advance into delivery/settlement.
- Legacy-save repair in `LogisticsModels.FromSaveDto` (~line 293): deterministic repair zeroes cargo
  for provably-never-loaded shipments; ambiguous malformed shipments are quarantined to Failed.
- Regression coverage at base: `Tests/Editor/Economy/LogisticsConservationGateTests.cs`, 16 tests,
  incl. save/load round-trip and legacy-malformed repair. The 7 `CreateBlockedShipment` call paths
  are covered by `AllCreateBlockedShipmentCallPaths_Covered`.
- Honest-verification note: these tests need Unity's `AssetDatabase` (editor-only), so they cannot run
  in the Roslyn harness on this VM; the verdict rests on code + commit evidence as the task allows.

## 3. First Ledger 2/2 vs 10/10 verdict — NO COVERAGE LOST
- At base the First Ledger suite is **15 tests, unchanged since Oct 3-4**: `FirstLedgerGameStateTests` (4,
  added `1fbf9ab` "4 EditMode tests"), `FirstLedgerGoalEvaluatorTests` (6, added `6166931`
  "6 new EditMode tests"), `FirstLedgerStakeTests` (5, added `a74f70b`). No deletions or renames
  anywhere in history.
- "10/10" maps to the Oct 3-4 combined targeted run: 6 (GoalEvaluator) + 4 (GameState) = 10, matching
  both commit messages. "2/2" matches no First Ledger fixture in the repo (no file has 2 tests);
  it was likely a partial/subset run. It does not indicate lost coverage.
- It does not matter for execution: the 15 tests are intact; Phase H re-runs them in Unity.

## 4. Honest verification done in Phase A
- Built a Roslyn `csc.dll` + reflection harness (UnityEngine/NUnit shims) on this VM.
- `FinancialObligationAuthorityTests`: **28/28 PASS** — real compile, real run. Confirms the shared
  obligation authority and its duplicate-writer guards (one obligation owns balance+history,
  liability ledger projects shared balance, guaranty call creates exposure without cloning principal).
- Full-Unity runs remain Codex/Luna's job; nothing here is claimed as a Unity result.

## 5. Phased build plan (B-I) with concrete integration points

**Phase B — person/household consumption.** Make `HouseholdLedger` the single household-cash truth
(Canon 13.2/13.4). Integration points: `BusinessRuntimeState.ResolveWeeklyPayrollFromEmployments`
(credit worker households via `RecordWagePayment` through `EmploymentRelationshipRegistry`);
`SimulationSystemsHub.HouseholdLedgers`; retire or reframe `DepositWeeklyHouseholdIncome` with
explicit provenance. Tests: wage payment crediting, no-fiat-topup rejection.

**Phase C — shopping/retail loop (priority end-to-end proof).** One execution path: register the
General Store as an `IGoodsSupplier` in `SupplierDirectory`; household plans via
`HouseholdConsumptionPlanner`, execution via `EmbodiedPurchaseExecutor` against the ledger;
retire `spendingMoneyCents` as a payment wallet. Integration points: `GeneralStoreRuntimeManager`
(stock as `StockUnits`, price as `PricePerUnitCents`), `EmbodiedPurchaseExecutor`, `HouseholdLedger`.

**Phase D — housing.** Collect rent: `BoardingHouseBoarderRegister.RentDueWeekly/Monthly` ->
`HouseholdLedger.RecordOutflow` (tenant) -> boarding-house `BusinessRuntimeState` cash.
Integration points: `HousingAuthority` occupancies, `HouseholdLedgerRegistry`, business cash.

**Phase E — shared credit completion.** Wire the unexercised foundations: seller-note issuance in
acquisitions (`IssueSellerFinanceNote` callers), trade-credit runtime (Tech 6.4: terms, arrears,
collection stage), guaranty call flow (`CallGuaranty` from delinquency), foreclosure execution,
`CreditCashAccount` to real participant cash. Integration points: `FinancialObligationAuthority`,
`CreditRegistry`, `CreditOfferWorkflow`, `CreditParticipantService.ChooseDelinquencyResponse`.

**Phase F — NPC property/construction.** Add the missing Project/WorkPackage execution authority on
top of `ConstructionContractBook`: contract -> parcel + labor + materials + progress billing.
Integration points: `ConstructionProgressBillingService`, `HousingAuthority`, logistics materials.

**Phase G — NPC business formation.** NPC financing path: `BusinessCreation` + `CreditParticipantService`
(private/NPC borrowing through the shared authority). Integration points: `PrivateLenderFunds`,
`CreditOfferWorkflow`, `FinancialObligationAuthority`.

**Phase H — vertical slice fixture.** Extend `Orchestration/HouseholdSlice/HouseholdVerticalSlice`
(existing 7 tests) with the unified household economy end-to-end.

**Phase I — integration/handoff + final report.** Full-suite Unity run (Codex/Luna), save/load
round-trips for every touched authority, handoff notes.

## 6. Needs Kennedy's decision
None of the Phase A findings are genuine forks: the household-cash unification direction is
canon-settled (Canon 13.2 provenance + 13.4 embodied execution both point at `HouseholdLedger`;
`spendingMoneyCents` is the legacy duplicate). The migration is broad (payroll x28 business types,
shopping loop, services), so Phase B should confirm the direction with him before starting — a
courtesy check, not a design fork. Out of scope for this pass: Ghost Town lock, mortality/
succession triggers, property-dispute sources (Oct 4 scenario-design forks).
