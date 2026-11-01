# Phase I — Authority-Ownership Reconciliation (I2) + Save/Load No-Duplication Verification (I3)

Branch: `muse/real-people-shared-credit` @ `fad195e`. Date: Oct 10, 2026.

## I2 — Authority-ownership reconciliation

For every asset/obligation class touched or created in Phases B–H, exactly one
writer/owner was confirmed by code inspection. The Phase A duplicate-writer
list is fully closed:

| Asset / obligation | Single owner | Evidence |
|---|---|---|
| Household cash | `HouseholdLedger` via `HouseholdLedgerRegistry` | `DepositWeeklyHouseholdIncome` is GONE (no references repo-wide). `spendingMoneyCents` writes remain only as (a) spawn-time endowment (`NewcomerSettlementPlanner`, `PopulationGenerator`) which `HouseholdLegacyCashMigrator` moves into the ledger and zeroes, and (b) no-registry fallback branches (`GeneralStoreRuntimeManager.ChargeHouseholdCents`, doctor services) that execute ONLY when the ledger registry is absent — mutually exclusive with the ledger path, never a second concurrent writer. |
| Wage crediting | `BusinessRuntimeState.ResolveWeeklyPayroll` → `HouseholdLedger.RecordWagePayment` for all business types; `MineLaborRegister.SettleShiftWages` → same method | The Phase A asymmetry (payroll credited nobody) is closed; mine and general payroll share the one crediting path with employment provenance. |
| Obligations | `FinancialObligationAuthority` | `BusinessLiabilityLedger.BalanceCents` is a re-read projection (`financialAuthority.Find(...)?.TotalOutstandingCents`); the single `= 0` write is the conversion bookkeeping after the authority converts the payable. Rent arrears are real obligations (`obligations.Create(...)`); `RentArrearsRecord` is a workflow view holding the obligation id, never a balance. |
| Credit instruments | `CreditRegistry` (index; balances via the authority) | Unchanged; Phase E tests green. |
| Credit cash | `IRealCashStore` implementations, mutated only via `CreditCashBridge.CommitWindow` deltas | `CreditCashAccount` is an explicit sandbox/window (snapshot → delta → commit through the real store). `HouseholdCashStore` delegates to `ledger.RecordOutflow/RecordInflow` — the ledger stays the single writer. `PurseCashStore` is a named purse with a sourced opening balance (never conjured). |
| Business cash | `BusinessRuntimeState` (`AddCashCents` et al., all mutations inside the class) | Rent collection reaches it via the `IRentCashSink` port (production adapter is Codex's job — see handoff). |
| Bank cash | `BankDepositLedger` via `BankRuntime` | Two-track ledger/vault unchanged. |
| Housing/occupancy | `HousingAuthority` | Unchanged. |
| Household inventory | `HouseholdInventory` (all lot mutations inside the class) | Legacy `HouseholdReserveState` is seeded once into lots by the idempotent `HouseholdInventoryReserveBridge.SeedLotsFromReserves` — a migration path, not a second writer. |
| Meals | `HouseholdMealLogRegistry` (`RecordMeal`/`RecordMissedMeal`/`RecordPreparation` the only writers) | No external writers found. |
| Person time | `PersonScheduleTracker` (`TryReserve`/`Release` the only mutators) | Consumers (construction, investigation, shopping) all go through it; double-booking is refused loudly. |
| Person truth / membership | `PersonState`/`PopulationManager`, `HouseholdMembershipRegistry` | Unchanged. |
| Construction execution | `ConstructionProjectExecutor` (projects/packages), `ConstructionContractBook` (money) | Money and materials never share a writer: the contract book records obligations; the executor consumes real lots via `IConstructionMaterialSource` and commits person-time. |

## I3 — Save/load no-duplication verification

### Hub-wired systems (persisted today)

- Obligations, credit offers, instruments, liabilities — via
  `SimulationSystemsHub.CaptureSaveDto`/`LoadFromSaveDto` (round-trip tests pass).
- Household ledgers, inventories, meal log, purchasing needs — via
  `SaveLoadManager` → hub `Export*/Import*` into the population save section;
  the legacy-cash migrator runs AFTER ledger import (idempotent — see below).

### Migrator idempotency — verified by test

`PhaseBWageCreditingTests.LegacyMigration_MovesBalanceOnce_Idempotent` (passes in
the I1 sweep): `MigrateAll` run twice books one entry; the field is zeroed after
booking so the second pass (or a reloaded migrated save) books nothing. Negative
legacy balances are quarantined, not booked.

### Load methods duplicate nothing — verified by inspection

Every new `LoadFromSaveDto` clears before loading (or uses keyed upsert with a
duplicate-skip diagnostic): `NpcFormationAssetRegister`, `NpcBusinessLifeRecord`,
`NpcOpportunityObservationLog`, `NpcBusinessEventLog`, `TradeCreditBook`
(keyed by obligation id), `PersonScheduleTracker`, `RentCollectionService`
(keyed arrears), `NpcBusinessInvestigation`, `NpcPropertyLife`,
`NpcConstructionCommissioner`, `ConstructionProjectExecutor` (hash-set dedupe).
Round-trip tests with explicit no-duplication assertions pass for: arrears,
construction projects/packages, commissioned-build records, observation log,
event log, business life records, mine roster, liabilities, obligations.

### NOT hub-wired — Codex's job (complete list)

The Phase E worker deliberately left hub wiring to Codex. The following have
`CaptureSaveDto`/`LoadFromSaveDto` ready but are referenced NOWHERE in
`SimulationSystemsHub` or `SaveLoadManager` — their runtime state does not
persist across saves until Codex wires them:

1. `PersonScheduleTracker` (`PersonScheduleTrackerSaveDto`)
2. `RentCollectionService` (`RentCollectionServiceSaveDto` — arrears + notices)
3. `NpcBusinessFormation` (`NpcFormationAssetRegisterSaveDto`)
4. `NpcBusinessLifecycle` (`NpcBusinessLifeRecordSaveDto`)
5. `NpcBusinessInvestigation` (`NpcBusinessInvestigationSaveDto`)
6. `NpcOpportunityObservation` (`NpcOpportunityObservationLogSaveDto`)
7. `NpcBusinessEventLog` (`NpcBusinessEventLogSaveDto`)
8. `NpcPropertyLife` (`NpcPropertyLifeSaveDto`)
9. `ConstructionProjectExecutor` (`ConstructionProjectSaveDto`)
10. `NpcConstructionCommissioner` (`NpcConstructionCommissionerSaveDto`)
11. `TradeCreditBook` (`TradeCreditBookSaveDto`)
12. `CreditEventLog` (`CreditEventLogSaveDto`)

Stateless by design (no DTO needed, nothing to wire): `NpcPropertyDecisionEngine`
(decisions are re-derived from current state each run) and
`SellerFinanceClosingService` (holds only diagnostics; the issued note persists
via `CreditRegistry`/`FinancialObligationAuthority`, which ARE wired).

Consequence until wired: NPC observations, investigations, formations, life
records, property life, construction projects, trade-credit aging state, rent
arrears/notices, person-time reservations, and the credit event log reset on
save/load. The money and obligation legs (the conserved quantities) survive;
the workflow state does not.
