# Pass IV Foundation Contracts — Muse Pass IV (The Living World Foundation)

Branch: `muse/universal-tasks-equipment-activities` · Baseline: `a38c6aa` (integration/muse-real-lives-land-livelihoods)
Date: 2026-10-12 · Status: foundation commit; implementation workstreams build on this.

## Contract files (this commit)

| File | Namespace | Contents |
|---|---|---|
| `Core/Tasks/TaskLifecycleContracts.cs` | `LandLedgers.Tasks` | `TaskLifecyclePolicy` (transition table, commitment rules), `TaskIdentityConventions` |
| `Core/Time/DurationContracts.cs` | `LandLedgers.Time` | `DurationConventions` (seconds precise, minutes booking quantum), `TaskDurationInputs`, `DurationEstimate`, `ITaskDurationEstimatorV2`, `FallbackDurationEstimator` |
| `Core/Time/DueEventContracts.cs` | `LandLedgers.Time` | `DueEventKind`, `DueEvent`, `IDueEventScheduler`, `DueEventIdConventions` |
| `Domains/Economy/Equipment/ReservationContracts.cs` | `LandLedgers.Economy.Equipment` | `ReservationDomain`, `ReservationKeyConventions`, `ReservationItem`, `ReservationRequest`, `ReservationHandle`, `IUnifiedReservationAuthority` |
| `Domains/Economy/Equipment/EquipmentCapacityContracts.cs` | `LandLedgers.Economy.Equipment` | `EquipmentCapacityModel` (6 models), `CapacityEnvelope`, `EquipmentCapacityDescriptor`, `EquipmentConditionProfile`, `IEquipmentCapacityService` |
| `Domains/Economy/Equipment/EquipmentCapacityContracts.cs` | `LandLedgers.Economy.Equipment` | (same file) |
| `Domains/Economy/Generic/ProductionStageContracts.cs` | `LandLedgers.Economy.Generic` | `ProductionStageKind`, `MissedDeadlineConsequence`, `ProductionStageDefinition`, `ProductionStageState` |
| `Domains/Population/PersonActivityContracts.cs` | `LandLedgers.Population` | `PersonActivityPolicy` (interruptibility, budget consumption, descriptions) |

Pure data + interfaces + policy tables. No logic, no Unity dependencies beyond attributes, no scene references.

Enum extensions in this commit (append-only, never renumbered):
- `TaskStatus`: `Paused = 7`, `WaitingOnPassiveProcess = 8`, `WaitingOnDependency = 9`, `ResourcesReserved = 10`, `ReworkRequired = 11`
- `PersonActivityKind`: `Resting = 9`, `JobSeeking = 10`, `Socializing = 11`, `Managing = 12`, `FamilyWork = 13`

## ID and key conventions

- Task ids: existing `EntityId` (kind `WorkTask`); reservation owner key `"task:" + taskIdString`.
- Reservation resource keys: `asset:{id}`, `kit:{id}`, `workstation:{id}`, `person:{entityId}`, `lot:{id}`, `workspace:{id}`, `animal:{id}`, `motive:{sourceId}` — one key space.
- Due-event ids: `"due-" + kind + "-" + payloadRef` (stable across save/load; rescheduling replaces, never duplicates).
- Deterministic ids where the domain needs them; never invent identifiers Kennedy or a tool did not supply.

## Authority table (from the 2026-10-12 audits — do not re-litigate)

| Fact | Authority | Notes |
|---|---|---|
| Task lifecycle state | `TaskAuthority` + `TaskLifecyclePolicy` | Single mutator of WorkTask state; new states in policy table |
| Task duration estimate | `ITaskDurationEstimatorV2` (new) | Composable per-method; fallback preserves base-minutes behavior |
| Due events / deadlines | `IDueEventScheduler` (new) | Absolute game seconds on the TimeManager timeline; no second clock |
| All reservations | `IUnifiedReservationAuthority` (new) | Promotes EquipmentReservationService key-space; persists |
| Equipment capacity semantics | `IEquipmentCapacityService` (new) | 6 models; ownership never implies unlimited capacity |
| Production stages | `ProductionStageDefinition/State` (new) | Extends existing ProductionPhaseDefinition/ProductionProcessState |
| Person activity simultaneity | `PersonScheduleTracker` (existing) | New activity kinds appended; sleep/eat/travel now really reserved |
| Cash movement | `HouseholdLedger` (existing single writer) | Unchanged |
| Debt truth | `FinancialObligationAuthority` (existing) | Unchanged; PlayerDebtManager bridge untouched |
| Capability/experience | `SkillService` + stores (Pass III) | Unchanged; formation inputs to be derived from real context |

## Duplicate truths the workers resolve (not re-litigate)

1. Equipment gate never installed + kind-level check allows N tasks on one plow → Worker 2 installs gate, enforces instance reservation.
2. Two wagon models (`FreightWagon` vs `EquipmentAsset` wagons) → Worker 2 reconciles; freight uses the asset registry.
3. Transient reservations (`EquipmentReservationService`, `DraftPowerService`) lost on load → Worker 2 persists the unified authority.
4. Construction credits labor at reserve time, skips budget → Worker 3 routes construction labor through TaskAuthority + budget.
5. Generic production 1440-min daily lump → Worker 1/3 move to event-driven progression.
6. Domestic/shopping instant completion → Worker 4 drives them through real time.
7. `DomesticLaborService` has no live driver → Worker 4 wires it.
8. Player tasks stall (no progress driver) → Worker 1 extends AdvanceAutonomousWork or equivalent.
9. Sleep/eat/travel never reserved → Worker 4 reserves them.
10. Short-tick overflow discarded; day-collapse skips payroll weeks → Worker 1 fixes event processing.

## Worker ownership (merge order: 1, 2, 3, 4)

- **Worker 1 — task execution + time** (`muse/pass4-task-execution-time`): TaskLifecyclePolicy enforcement in TaskAuthority (new states, transitions), ITaskDurationEstimatorV2 universal estimator + first method estimators, IDueEventScheduler implementation + hub registration, fix short-tick overflow (defer, never discard) and day-collapse (process each crossed day), player-task progress driver, save/load for new task states and due events, tests.
- **Worker 2 — equipment + reservations** (`muse/pass4-equipment-reservations`): IUnifiedReservationAuthority implementation (extend EquipmentReservationService, persist it), IEquipmentCapacityService with all 6 capacity models, install EquipmentTaskGate on TaskAuthority in production drivers, reconcile FreightWagon vs EquipmentAsset wagons, persist DraftPowerService reservations (hub-own it), equipment condition profiles per kind, save/load, tests.
- **Worker 3 — production + industries** (`muse/pass4-production-industries`): ProductionStageDefinition/State runtime (stage machine on ProductionProcessState), active/passive/tending/deadline progression wired to due events, bakery slice (full 9-step chain), retail service work, farm/plow chain (draft unit + exclusive reservation), forge/repair chain (multi-stage, rework), freight handling chain (load/travel/unload, custody), construction through TaskAuthority, save/load mid-process, tests.
- **Worker 4 — household + autonomy + sim** (`muse/pass4-household-autonomy-sim`): PersonActivityKind wiring (sleep/eat/travel/rest reservations in daily planning), DomesticLaborService live driver, HouseholdShoppingLoop through real time, autonomous work generation from real needs, ManagerBrain work-allocation via the unified authority, Phase Q read models (person activity, business work, equipment), Phase P coarse-simulation rules, 14-day deterministic extended sim fixture, persistence section, performance measurements, tests.

## Protected — additive only, do not rewrite

Pass II labor glue: SimulationDrivers, hub orchestration, WageSettlementService, WorkScheduler, payroll drivers. Pass III: SkillService + stores, TenancyPaymentService, HouseholdDecisionService/Driver, DomesticLaborService (wire it, don't rewrite it), ParcelService, PropertyInterestStore. TaskAuthority core (extend, don't replace). HousingFoundation models. FinancialObligationAuthority, HouseholdLedger. SharedBusinessRuntimeManager (do not touch its internals; new work goes through the task authority).

## Environment (for every worker)

- Work in an ISOLATED git worktree (`git worktree add`). Never share a checkout. Run `git status` before every commit; only your files staged.
- Fresh GUID per new `.meta` — `python3 -c "import uuid; print(uuid.uuid4().hex)"` (no `uuidgen`). Folder metas use `folderAsset: yes`.
- C# SDK: `export DOTNET_ROOT=$HOME/dotnet; export PATH="$DOTNET_ROOT:$PATH"`.
- Harness: `~/workspace/land-and-ledgers/harness/` (being rebuilt; see its README). Add your new .cs files to the .csproj Compile Include list. Compile-check before pushing your branch.
- Tests: `Tests/Editor/<Domain>/`, deterministic, invariant-based and parameterized; substantial but not bloated. Never describe a test as passing unless compiled and executed. Never claim Unity validation.
- Canon: proposals 001–105 approved; 106–145 NOT approved. Cite document/revision/section for consequential decisions. Genuine forks → record and escalate, never guess.
- Economic integrity: one cash writer, one debt authority, no synthetic inventory, no teleported goods, no fake persons, person-time exclusive, NPCs and player share systems, save/load clear-then-load and idempotent.
