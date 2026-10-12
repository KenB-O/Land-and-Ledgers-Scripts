# Universal Tasks, Equipment, and Activities — Codex Handoff

Pass IV implementation for Land & Ledgers. Branch: `muse/universal-tasks-equipment-activities`.
Foundation: `b3e25c1`. This handoff: coordinator fixes at `f7a7601`.

## 1. What Was Built

Four workers implemented the Living World Foundation on top of shared contracts
committed at `b3e25c1`:

- **Worker 1 (task execution and time)**: Task lifecycle enforcement in TaskAuthority,
  universal duration estimators, DueEventScheduler with hub registration, short-tick
  overflow deferral in TimeManager, per-day collapse processing, due-event drain,
  player-task progress driver.
- **Worker 2 (equipment and reservations)**: IUnifiedReservationAuthority on
  EquipmentReservationService (persisted), IEquipmentCapacityService with all six
  capacity models (Exclusive, Container, Workstation, Throughput, PassiveProcess,
  Transport), EquipmentTaskGate installed in production drivers, instance-level
  equipment checks, wagon reconciliation via adapter, DraftPowerService hub-owned
  and persisted, per-kind condition profiles.
- **Worker 3 (production and industries)**: ProductionStageMachine with slices A-E:
  bakery 9-step chain, retail service desk, farm plow, forge repair, freight shipment.
  Construction labor routed through TaskAuthority with budget.
- **Worker 4 (household, autonomy, sim)**: Daily activity planner (sleep, meals,
  travel, rest), DomesticLaborService live driver, shopping through real time,
  autonomous work generation (Phase O), Phase Q read models, Phase P coarse-sim rules,
  Slice F, 14-day deterministic simulation, performance measurements.

## 2. Shared Contracts (at b3e25c1)

- `Core/Tasks/TaskLifecycleContracts.cs`: TaskLifecyclePolicy transition table.
  TaskStatus appended: Paused=7, WaitingOnPassiveProcess=8, WaitingOnDependency=9,
  ResourcesReserved=10, ReworkRequired=11. Append-only; never renumber.
- `Core/Time/DurationContracts.cs`: Seconds-precise durations, whole-minute booking
  quantum, ITaskDurationEstimatorV2.
- `Core/Time/DueEventContracts.cs`: IDueEventScheduler with stable event ids.
- `Domains/Economy/Equipment/ReservationContracts.cs`: IUnifiedReservationAuthority,
  one key space: asset:, kit:, workstation:, person:, lot:, workspace:, animal:, motive:.
- `Domains/Economy/Equipment/EquipmentCapacityContracts.cs`: Six capacity models.
- `Domains/Economy/Generic/ProductionStageContracts.cs`: ProductionStageKind,
  MissedDeadlineConsequence, stage definitions and state.
- `Domains/Population/PersonActivityContracts.cs`: PersonActivityPolicy.
  PersonActivityKind appended: Resting=9, JobSeeking=10, Socializing=11,
  Managing=12, FamilyWork=13.

## 3. Integration Points for Codex

### TaskAuthority (Worker 1)
- `MarkWaitingOnPassiveProcess(taskId, dueEventId, reason, out rejectionReason)`
- `ResumeTask(taskId, reason, out rejectionReason)`
- `MarkReworkRequired(taskId, correctiveTaskId, reason, out rejectionReason)`
- These are the canonical methods. ProductionStageMachine.ProductionTaskHandoffDriver
  calls them directly (coordinator removed the reflection seam at af25101).

### Reservations (Worker 2)
- `EquipmentReservationService` implements `IUnifiedReservationAuthority`.
- `IEquipmentCapacityService` handles all six models.
- `EquipmentTaskGate` is installed in production drivers; it checks instance-level
  availability, not just kind-level.

### Production (Worker 3)
- `ProductionStageMachine` drives staged processes with due events.
- Slices: BakeryStagedProductionChain, RetailServiceDesk, PlowWorkService,
  ForgeRepairChain, FreightShipmentService.
- Handoff driver methods now take `dueEventId` explicitly:
  `TryEnterPassiveWait(authority, taskId, budgets, dayIndex, dueEventId, out reason)`
  `TryResumeForTending(authority, taskId, tender, budgets, dayIndex, out reason)`

### Household and Sim (Worker 4)
- Daily planner, DomesticLaborService, autonomous work generation.
- 14-day sim conserves cash exactly (147600c). Timing-stress identical hashes
  at 60s, 600s, 3600s, 6000s steps.

## 4. Coordinator Fixes (f7a7601)

Worker 3's code did not compile. Fixed:

- `ProductionStageMachine`: internal callers updated for new handoff signatures.
- `ForgeRepairChain.LoadFromSaveDto`: `BuildRepairMethod()` (nonexistent) replaced
  with `BuildSixStepMethod(order)` after initializer.
- `ForgeRepairChainTests`: `AdvanceRepair` (nonexistent) replaced with `WorkOrder`.
- `BakeryStagedProductionChainTests`: updated EMULATED fallback expectation; the
  fallback was removed, direct Worker 1 API is used.
- `FreightShipmentServiceTests`: added missing `using LandLedgers.Economy;`.

## 5. Test Results

Roslyn harness: 682 passed, 4 failed.

Failures (all 4 pre-existing in the baseline at a38c6aa):
- `MoneyOrderServiceTests.IndorseOrder_AllowsSingleIndorsement` (pre-existing)
- `MoneyOrderServiceTests.IssueDuplicate_ReplacesLostOrderFree` (pre-existing)
- `MoneyOrderServiceTests.RunSettlementCycle_NetsAndSettlesInFull` (pre-existing)
- `P5PropertyLegalJourneyTests.Driver_LeviesAnnualAssessmentOncePerTaxYear` (pre-existing)

The 2 Worker 3 failures are FIXED (post-pass fix commits d2542d2 and 83854b7,
see section 6). Baseline at a38c6aa was 534 passed, 4 failed.

## 6. Known Issues for Codex

Post-pass fixes (both Worker 3 failures resolved; tests unmodified):

- Forge save/load (FIXED at d2542d2). Root cause: Worker 3's fixture expected
  `BeginRepair` to leave the order at the repair stage (3), but
  `StagedProductionProcess.Begin` only enters stage 0 — the DTO restore path
  itself was correct. Fix: `BeginRepair` now drives the synchronous intake
  stages (receive, inspect, prepare) to completion so the order rests at the
  repair stage; a parked stage (forge contention) does not fail begin.
  Additionally, `WorkOrder` could not advance the passive cooling stage, which
  the fixture's completion loop requires: new
  `StagedProductionProcess.AdvancePassiveByMinutes` advances passive process
  time by minutes and completes through the normal stage path (stable due-event
  id cancelled so it cannot fire stale); stages needing clock-driven
  tending/deadlines are refused. `ProductionStageState` gains a persisted
  `PassiveMinutesElapsed` field (additive). Success now returns a null reason
  (the fixture's begin-ok contract). Repeated restore is clear-then-load;
  no duplicated work, reservations, or inputs.
- Retail delivery false rejection (FIXED at 83854b7). Root cause: not a
  duplicate — `TryReceiveInventoryShipment("ship-77", ...)` was *blocked*.
  `TryAddInventory` → `Retail.TryOccupyProductSpace` → `GetOrCreatePool`
  minted a zero-allocation pool for the never-configured "shelf-a" pool, so
  `TryOccupy(10)` failed and the legitimate first delivery was refused as
  "duplicate or blocked". Fix: a capacity pool that was never explicitly
  configured now imposes no constraint; only an allocated pool constrains.
  Shipment-id idempotency (`receivedShipmentIds`) is untouched: a genuine
  duplicate delivery id is still refused, and re-booking the same shipment id
  is still refused exactly once.
- `RegisteredMailServiceTests`: 3 tests flaky (pass/fail varies by run order).
  Files untouched by Pass IV; pre-existing timing issue.
- Unpublished Codex checkpoint `0d7b8d9` was never pushed. It is a later
  integration dependency. Do not assume its code is present.

## 7. Economic Integrity

- One cash writer: HouseholdLedger. One debt authority: FinancialObligationAuthority.
- No synthetic inventory, no teleported goods, no fake persons.
- Person-time exclusive. NPCs and player use the same systems.
- Save/load is clear-then-load and idempotent.

## 8. Branch and SHAs

- Branch: `muse/universal-tasks-equipment-activities` (local only; no push credentials)
- Foundation: `b3e25c1`
- Worker 1 merge: `b6a779a` (tip `6c09a1a`)
- Worker 2 merge: `83f9840` (tip `b368cf3`)
- Coordinator reflection fix: `af25101`
- Worker 3 merge: (tip `ee98377`)
- Worker 4 merge: (tip `6048e7f`)
- Coordinator harness fixes: `f7a7601` (previous HEAD)
- Post-pass regression fixes: `d2542d2` (forge save/load), `83854b7` (retail
  delivery) — current HEAD

## 9. Files Changed by Coordinator

- `Domains/Economy/Generic/ProductionStageMachine.cs`
- `Domains/Economy/Businesses/Blacksmith/ForgeRepairChain.cs`
- `Tests/Editor/Economy/BakeryStagedProductionChainTests.cs`
- `Tests/Editor/Economy/ForgeRepairChainTests.cs`
- `Tests/Editor/Economy/FreightShipmentServiceTests.cs`

## 10. For Codex Integration

When integrating `0d7b8d9` or building on this branch:

- Do not reintroduce the reflection seam in ProductionTaskHandoffDriver.
- Do not rename the appended enum values (TaskStatus 7-11, PersonActivityKind 9-13).
- Keep save-DTO methods inside the owning runtime class.
- New .cs/.md files need .meta with fresh GUIDs.
- The `dueEventId` parameter on handoff methods is required; use
  `DueEventIdFor(DueEventKind.TendingDue)` or similar stable ids.
