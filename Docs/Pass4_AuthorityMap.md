# Pass IV Authority Map — The Living World Foundation

Date: 2026-10-12 · Baseline: `a38c6aa` (integration/muse-real-lives-land-livelihoods).
Synthesized from three read-only code audits (task/activity/time, production/economy, equipment/skills/persistence).
The repository is the truth; this map records what the code actually does.

## 1. Task, Activity, Person-Time, Scheduling, Clock

| Authority | Location | Role |
|---|---|---|
| `TimeManager` | `Core/Time/TimeManager.cs` | Authoritative 24h clock. ShortTick/DayChanged/WeekChanged events, speed scaling, save/load. KNOWN DEFECTS: `ProcessShortTicks` caps at 64 ticks/frame and discards overflow seconds; `CollapseRemainingDayRollovers` emits one DayChanged for N crossed days (daily drivers run once; intermediate payroll weeks skipped, no catch-up). |
| `TaskAuthority` | `Core/Tasks/TaskAuthority.cs` | THE universal task authority (single mutator of WorkTask state). Create/assign/start/record-work/complete/interrupt/cancel; prerequisites; estimator + execution-gate seams. `RecordWork` auto-completes Success; `RecordWorkProgress` (a38c6aa) assigns no outcome. |
| `WorkTask` / `TaskDefinition` | `Core/Tasks/TaskModels.cs` | TaskInstance (`WorkTask`: EntityId, owner, assignee, planned/worked minutes, status, outcome, prerequisites) and authored definition data. TaskStatus now 12 states (Pass IV appends 5). |
| `ITaskExecutionGate` | `Core/Tasks/TaskExecutionGate.cs` | Canon 4.1 start-time gate (person + tool + workspace + materials + motive power + authority + time). Interface only. |
| `WorkTimeBudget` / `WorkTimeBudgetStore` | `Core/Time/WorkTimeBudget.cs` | Per-person daily work minutes (default 600), commit/worked/release accounting, nutrition capacity multiplier, day rollover. |
| `PersonScheduleTracker` | `Domains/Population/PersonScheduleTracker.cs` | Simultaneity guard: per-day minute windows, overlap refused loudly. PersonActivityKind now 14 values (Pass IV appends 5). Sleeping/Eating/Travel previously had zero call sites. |
| `WorkScheduler` | `Domains/Economy/Labor/WorkScheduler.cs` | Stateless pure planner (Pass II). Never invents workers. |
| `SimulationDrivers` | `Orchestration/Systems/SimulationDrivers.cs` | Thin drivers: OnDayChanged (budgets, generic production 1440-min lump, labor market, autonomous labor), OnShortTick (`AdvanceAutonomousWork` per-tick progress inside Work windows), OnWeekChanged (payroll, Generic only). |
| `PersonState` | `Domains/Population/PersonState.cs` | Canonical person record (`int id`, never rewritten). EntityId bridge is implicit (`EntityId.For`). |
| Person location | Fragmented | `TownGrid`/`TownCell`/`GridCoord` (2m substrate), `ParcelService`, `PersonState` building refs, `JourneyModel`/`JourneyTravel`, `PopulationPathingDirector` + `AgentMover`. `WorkTask.locationId` is an unwired string placeholder. |

Duplicate "what is the person doing": `PersonScheduleTracker` vs `PersonState.scheduleState` (ambient) vs `PlayerDirector.travelling`/`currentTaskId`.
Duplicate person-time accounting: budget minutes vs tracker windows vs `WorkTask.minutesWorked` vs `WorkPackage.LaborMinutesCommitted`.
Three task-timing regimes: per-tick real-time (autonomous), daily 1440-min lump (generic production), instant single-call (domestic/shopping).

## 2. Production, Inventory, Freight, Employment, Household, Finance

| Authority | Location | Role |
|---|---|---|
| `GenericProductionRuntime` / `GenericProductionAuthority` / `GenericProductionPolicyExecutor` / `GenericProductionOutputAuthority` | `Domains/Economy/Generic/` | New production path. `ProductionProcessState`: phase machine, `ConsumeInputsOnce` (exactly-once with rollback), `OutputCreated` flag (no output before completion). `GenericProductionTaskBridge`: active phases → TaskAuthority tasks. Passive phases advance unattended by design. |
| `ProductionMethodDefinition` / `ProductionPhaseDefinition` / `ProductionProcessState` | `Domains/Economy/Generic/GenericBusinessEconomy.cs` | Recipe, phases (`ElapsedMinutes`, `RequiresPerson`), process state. |
| `GenericInventoryPosition` / `GenericInventoryLot` | `Domains/Economy/Generic/GenericBusinessEconomy.cs` | Business inventory: quantity, reserved, available, average cost, optional lots (`lot-{GUID:N}`), FIFO. |
| `HouseholdInventory` / `HouseholdItemLot` | `Domains/Population/HouseholdInventory.cs` | Household lots with holds (`HoldForCommitment`/`FulfillCommitment`/`ReleaseHold`), traceable consumption records. |
| `LogisticsRuntimeManager` / `LogisticsShipmentState` | `Domains/Economy/Logistics/` | Shipment authority. `CreateBlockedShipment` FIXED (MR-P001): blocked shipments own zero cargo; save-load normalization quarantines ambiguous records. `FailShipmentAfterLoad` returns cargo to source or quarantines — never vaporized. |
| `JourneyModel` / `JourneyTravel` | `Domains/World/Journeys/` | Route plans, travel-time estimates, blizzard closures. |
| `EmploymentRelationship` | `Domains/Economy/Core/EmploymentRelationship.cs` | Sole employment + compensation record. Carries no ownership/rights fields. |
| `EmploymentNegotiationService` | `Domains/Economy/Labor/` | Offers with provenance; explicit deny-list blocks ownership/residential grants. |
| `WageSettlementService` | `Domains/Economy/Labor/WageSettlementService.cs` | Idempotent weekly payroll (Generic only). Settled-week markers; never double-posts. |
| `MineLaborRegister` | `Domains/Economy/Businesses/Mine/MineLaborRegister.cs` | LEGACY second wage path (per-shift). Test fixed in c6a676a. |
| `HouseholdLedger` | `Domains/Population/HouseholdLedger.cs` | THE one cash writer. Rejects sourceless inflows (Canon 13.2) and over-balance outflows. Legacy `spendingMoneyCents` fallbacks still exist in unwired paths. |
| `DomesticLaborService` | `Domains/Population/DomesticLaborService.cs` | 4 domestic task families on TaskAuthority. NO LIVE DRIVER (tests only). |
| `HouseholdShoppingLoop` / `EmbodiedPurchaseExecutor` | `Domains/Population/` | Need→purchase loop with real ledger flows; embodied travel. |
| `TenancyPaymentService` | `Domains/World/Property/TenancyPaymentService.cs` | Rent/board loop, period-keyed idempotent, arrears → real Payable. |
| `RentCollectionService` | `Domains/Economy/Businesses/BoardingHouse/` | LEGACY rent loop, no idempotency key. |
| `FinancialObligationAuthority` | `Domains/Economy/Financing/` | THE one debt authority. Owns obligation lifecycle; deliberately moves no cash. |
| `PlayerDebtManager` | `Domains/Economy/Financing/PlayerDebtManager.cs` | Player loans; keeps own LoanContract book bridged via obligationId (dual-truth, mirrored repayments). |

Duplicate inventory truths per business: `BusinessRuntimeState` category stock vs `GenericConfiguration` generic positions.
`WorkTask.reservedMaterials` populated but never read/enforced; planner passes `reservedInput: _ => 0f`.

## 3. Equipment, Skills, Save/Load, Hub

| Authority | Location | Role |
|---|---|---|
| `EquipmentAsset` | `Domains/Economy/Businesses/Blacksmith/EquipmentAsset.cs` | Persistent equipment identity (plows, wagons, harnesses). Condition 0..1, Reserve/Release (refuses double-booking), wear, maintenance log, ownership chain. |
| `ToolKitInstance` / `ToolKitCatalog` | `Domains/Economy/Equipment/` | Hand-tool kits as one unit with single condition. |
| `BusinessEquipmentRegister` | `Domains/Economy/Equipment/` | Per-business asset/kit register; feeds valuation. No production holders, no save wiring. |
| `EquipmentReservationService` | `Domains/Economy/Equipment/` | EQP-1 unified key-space (`asset:`/`kit:`/`workstation:`). No double-booking, atomic ReserveAll. ONLY RentalFleet uses it. NO save DTO (transient). |
| `EquipmentRequirements` | `Domains/Economy/Equipment/` | Task equipment codes (`kit:`, `asset:`, `workstation:`, OR-alternatives). KIND-level check only — one plow satisfies N tasks. |
| `EquipmentTaskGate` | `Domains/Economy/Equipment/` | ITaskExecutionGate impl. NEVER installed on TaskAuthority in production (tests only). |
| `EquipmentCustody` | `Domains/Economy/Equipment/EquipmentCustody.cs` | Access grants (owned, employer-provided, rented, leased, borrowed...). Expired grants satisfy nothing. |
| `MotivePower` | `Domains/Economy/Equipment/MotivePower.cs` | Human/Horse/Mule/Ox/Water/Steam/Electric availability. |
| `WorkstationDefinition` / `BusinessWorkstations` | `Domains/Economy/Equipment/Workstation*.cs` | Readiness derived from component assets. No reservation observed. |
| `DraftPowerService` / `DraftWorkUnitService` | `Domains/Economy/DraftPower/` | THE animal/driver reservation authority (never double-books). NOT hub-owned, NOT persisted. |
| `TransportAssetRegistry` | `Domains/Economy/Transport/WagonFoundation.cs` | Wagon EquipmentAssets; trip wear formula. |
| `FreightResourcePool` / `FreightWagon` | `Domains/Economy/Businesses/Freight/` | DUPLICATE wagon model (own condition + reservedWagonIds). |
| `AnimalRegistry` / `HorseFoundation` | `Domains/Animals/` | Animal identity, fatigue meter (`Fatigue01`, eligibility ≤ 0.85). |
| `SkillService` + 4 stores + `ExperienceFormationService` + `CausalTaskOutcomeResolver` | `Core/Skills/` | Pass III: continuous capability 0..1, idempotent experience ledger, causal outcomes, 4-band evidence. a38c6aa wired `ResolveTaskCompletion` into the live driver. Driver hardcodes formation inputs (novelty/difficulty) — not yet derived. |
| `SimulationSystemsHub` | `Orchestration/Systems/SimulationSystemsHub.cs` | Scene home for ~40 authorities; lazy properties; save boundary (Capture/Load). |
| `SaveLoadManager` | `Infrastructure/Persistence/SaveLoadManager.cs` | Central orchestrator; clear-then-load everywhere. |

Duplicate wagons: `FreightWagon` vs `EquipmentAsset` wagons (independent condition + reservations, both persisted).
Split reservations: asset-level persists; service-level transient; draft transient.
Legacy skill signals: `WorkerVisibleProfile.generalSkill` (0-100, drives role fit) vs `PersonCapabilityStore`; `WorkerHiddenTraits.learningSpeed` vs `AptitudeStore`.

## 4. What Pass IV changes (and what it does not touch)

CHANGES: task lifecycle states, duration estimation, due-event scheduling, unified reservations, equipment capacity models, production stages, activity wiring, industry slices, autonomous generation, read models, 14-day sim.
DOES NOT TOUCH: `SharedBusinessRuntimeManager` internals, `PlayerDebtManager` bridge, title chain, `HouseholdLedger`/`FinancialObligationAuthority` cores, Pass III capability stores, Canon docs package, scenes/prefabs.
