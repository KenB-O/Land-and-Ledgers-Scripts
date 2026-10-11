# Codex Handoff — Autonomous Labor, Employment & Business Management

**Branch:** `muse/autonomous-labor-employment` (from `3152171de43e867f2b316dcf30837e36201d3d5c`; do NOT merge into main).
**Mode:** code-only simulation. No scenes, prefabs, terrain, or UI layouts were modified.
**Test status:** Roslyn harness (`roslyn-harness/harness-l.csproj`): **357 passed, 1 failed** —
the single failure is the pre-existing `MineLaborRegisterTests.SettleShiftWages_PaysBusinessOutflowAndWorkerInflow`
(documented since `7b1ca29`, unrelated, untouched). Unity Test Runner / PlayMode were
NOT run (Unity unavailable in this environment).

## 1. New services (all plain C#, hub-owned — attach nothing new to scenes)

All in `Domains/Economy/Labor/`, namespace `LandLedgers.Economy.Labor`:

| Service | Hub property | Save DTO field |
|---|---|---|
| `ContactGraphService` | `hub.ContactGraph` | `dto.contactGraph` |
| `EmploymentNegotiationService` | `hub.EmploymentNegotiation` | `dto.employmentNegotiation` |
| `LaborDemandAssessor` | `hub.LaborDemand` | — (stateless, read-only) |
| `WorkScheduler` | `hub.WorkScheduling` | — (stateless) |
| `WageSettlementService` | `hub.WageSettlement` | `dto.wageSettlement` |
| `ManagerBrain` (+ `StandingInstructionRegister`) | `hub.Managers` | `dto.managerBrain` (instructions + activity log) |
| `LaborMigrationLinker` | `hub.MigrationLinker` | — (stateless evaluation) |
| `DelegationService` (orphan rescue — existed, was never registered) | `hub.Delegation` | `dto.delegation` |

Shared contracts: `Domains/Economy/Labor/LaborContracts.cs` (DTOs/enums only).
`BusinessRuntimeState` gained two ADDITIVE members only: `LastPayrollSettledWeekIndex`
and `PayAgreedWage(employmentId, amountCents, periodLabel, dayIndex, diagnostics)`.
Nothing else in existing files was modified.

## 2. Already wired (no Codex action needed)

- `SimulationSystemsHub`: all 8 services registered (lazy properties) + `CaptureSaveDto` /
  `LoadFromSaveDto` lines. `SystemsSaveDtos`: 5 new fields.
- `SimulationDrivers.OnDayChanged`: sets `hub.LaborDayIndex` and calls
  `DriveLaborMarketDay` → `hub.EmploymentNegotiation.AdvanceDay(day, hub.Employments, diag)`
  (offer expiries + start-day activation).
- `SimulationDrivers.OnWeekChanged`: calls `DriveLaborMarketWeek` →
  `hub.WageSettlement.SettleWeeklyPayroll(business.RuntimeState, registry, weekIndex, day, diag)`
  per business (settled-week marker ⇒ never double-posts).

## 3. Codex wiring still needed (per-business cycles)

Per business with a delegated manager, each simulated day, assemble and call:

```csharp
// 1. Demand (map real workload into the DTO):
var inputs = new LaborDemandInputs { BusinessInstanceId = id, DayIndex = day };
//   OpenTaskMinutesByFamily from TaskAuthority.GetOpenTasks(businessEntityId)
//   WaitingCustomers / RecentBalkCount from the business CustomerQueue
//   RequiresPersonPhaseMinutes from active ProductionProcessStates
//   ActiveEmployeeCount / AvailableEmployeeMinutes from employments + WorkTimeBudgetStore
LaborDemandReport demand = hub.LaborDemand.Assess(inputs, diag);

// 2. Manager cycle:
var cycle = new ManagerCycleInputs {
    BusinessInstanceId = id, ManagerPersonId = managerPid,
    CashCents = business.RuntimeState.CurrentCashCents,
    DemandReport = demand,
    Instructions = standingInstructionsForBusiness, // StandingInstructionRegister
    DelegatedScopes = hub.Delegation scopes for (id, managerPid), // scope name strings
    DayIndex = day, JudgmentSeed = worldSeed ^ day, JudgmentQuality01 = 0.8f,
    PurchaseLimitCents = ..., MaxBorrowingCents = ownerMaxBorrowing,
};
ManagerDecisionResult r = hub.Managers.DailyCycle(cycle, diag);
// r.Escalated / r.EscalationReason -> surface to the owner UI; never auto-execute.
// ManagerActivityLog (hub.Managers.ActivityLog.Records) is the UI timeline source.

// 3. Work scheduling (map TaskAuthority + budgets + schedule tracker into the DTO):
var sched = new SchedulerInputs { BusinessInstanceId = id, DayIndex = day };
//   OpenWork from TaskAuthority.GetOpenTasks; Workers from active employments
//   with remaining WorkTimeBudgetStore minutes and PersonScheduleTracker windows
WorkAssignmentReport report = hub.WorkScheduling.AssignDailyWork(sched, diag);
// Apply: TaskAuthority.AssignTask + StartTask per assignment; PersonScheduleTracker.TryReserve
// the work window; WorkTimeBudgetStore.TryCommitTask the minutes. UnassignedTaskIds stay queued.
```

Owner hook: set `hub.IsBusinessOwner = (biz, pid) => /* business registry owner check */`.
When unset, offers require HireStaff delegation (the honest default).

## 4. Adapters

None new. Wage/recruitment cash uses the standing single writers:
`BusinessRuntimeState.AddCashCents/AdjustCashCents` and
`HouseholdLedger.RecordInflow/RecordOutflow/RecordWagePayment`. Reuse the
`INpcBusinessCashPort` / `IRentCashSink` patterns where NPC businesses are involved.

## 5. Previous handoff items — not regressed

`Docs/RealPeopleSharedCredit_CodexHandoff.md` §§2,3,5,6,7 are untouched by this
pass: no prefab records, adapters, driver calls, save-wiring, or manual tests
from that handoff were modified. This pass ADDS manual tests T11–T14 below.

## 6. Manual Unity tests to run (extend handoff §7)

- **T11 — wage settlement idempotency.** Employ a worker (weekly wage), run to the
  weekly payroll boundary twice (or save/load across it). EXPECT: exactly one
  wage debit on the business and one `RecordWagePayment` inflow on the household;
  the second boundary is a no-op (`LastPayrollSettledWeekIndex` guard).
- **T12 — manager unattended week.** Delegate a manager (HireStaff + AssignTasks
  scopes, one OwnerLocked instruction e.g. max borrowing 0). Run 7+ days with no
  owner input. EXPECT: staffing/scheduling/procurement activity in
  `ManagerActivityLog`; over-limit or reserved acts appear as escalations, never
  executed; no passive income (business cash moves only via real transactions).
- **T13 — recruitment letter round-trip.** Open a handbill/newspaper effort, run
  days. EXPECT: inquiries arrive only for contact-graph-linked persons after real
  postal travel time; strangers never appear; offer → counter → accept lands a
  PendingStart relationship that activates on its start day.
- **T14 — slice economy smoke.** Drive the `LaborToHouseholdSliceTests` world
  shape in-scene for 14 days (declining household → hire → work → payroll).
  EXPECT: every week's cash reconciles (business + household + obligation deltas
  sum to zero up to recorded external trade); no exceptions.

## 7. Read models for UI (source-complete)

- `ManagerActivityLog.Records` — manager identity, business, observed conditions,
  decision, evidence, authority used, resulting reference, outcome, escalations.
- `WageSettlementService` `PayrollEvent`s — per-employment paid/unpaid read models.
- `LaborDemandReport` — per-family unmet minutes, coverage gap, hiring-pressure evidence.
- `WorkAssignmentReport` / `EmploymentOffer` (with `OfferState`, `HowHeard` provenance).
