# Unwired Systems Registry

Status: active. Owner: whoever touches one of these systems next.

## Why this registry exists

The codebase contains several fully-implemented subsystems that have no
production callers. The generic `SharedBusinessRuntimeManager` path is the
live authority for day-to-day business simulation; the systems listed below
are detailed models that nothing in the live tick invokes. That is a
deliberate, documented state, not an oversight, and it must stay visible:
silently wiring one of these into production (or silently leaving a newly
added unwired system off this list) would change simulation behavior without
review.

This document is the single source of truth. It is machine-checked by
`Tests/Editor/Integration/UnwiredSystemsRegistryTests.cs`, which parses the
registry table below and fails the build if reality drifts from it:

- If a registered-unwired type GAINS a production caller (someone wires it
  into the live tick, a driver, or another production system), the test
  fails. Either complete the wiring deliberately and remove the entry, or
  revert the reference.
- If one of the known candidate types has NO production caller but is NOT
  registered here, the test fails. Register it with its story, or wire it.

## What counts as a production caller

A production caller is a reference to the registered type name from a `.cs`
file that is none of the following:

- the type's own source file,
- any file under `Tests/`,
- any file inside the type's owning directory (intra-feature helpers such
  as `HotelTravelerDemand` operating on `HotelShopRuntime` are part of the
  same unwired feature, not live wiring),
- a file named in the entry's allowlist (references that are fixtures,
  documentation-adjacent, or intra-subsystem by design).

Comments are stripped before matching, so prose mentions do not count.

## How to run the check

The test resolves the repository root from the `LAND_LEDGERS_REPO`
environment variable and falls back to the default checkout path
`~/workspace/land-and-ledgers/repo`. If the registry document cannot be
found, the test fails loudly rather than passing silently.

## Registry

| Type | Source file | Owning directory | Allowlisted refs |
|---|---|---|---|
| BarberShopRuntime | Domains/Economy/Businesses/Barber/BarberShopRuntime.cs | Domains/Economy/Businesses/Barber | (none) |
| DoctorPracticeRuntime | Domains/Economy/Businesses/Doctor/DoctorPracticeRuntime.cs | Domains/Economy/Businesses/Doctor | (none) |
| GrainElevatorShopRuntime | Domains/Economy/Businesses/GrainElevator/GrainElevatorShopRuntime.cs | Domains/Economy/Businesses/GrainElevator | (none) |
| GrainMillShopRuntime | Domains/Economy/Businesses/GrainMill/GrainMillShopRuntime.cs | Domains/Economy/Businesses/GrainMill | (none) |
| HotelShopRuntime | Domains/Economy/Businesses/Hotel/HotelShopRuntime.cs | Domains/Economy/Businesses/Hotel | (none) |
| LumberYardShopRuntime | Domains/Economy/Businesses/LumberYard/LumberYardShopRuntime.cs | Domains/Economy/Businesses/LumberYard | (none) |
| NewspaperRuntime | Domains/Economy/Businesses/Newspaper/NewspaperRuntime.cs | Domains/Economy/Businesses/Newspaper | (none) |
| RestaurantShopRuntime | Domains/Economy/Businesses/Restaurant/RestaurantShopRuntime.cs | Domains/Economy/Businesses/Restaurant | (none) |
| SaloonRuntime | Domains/Economy/Businesses/Saloon/SaloonRuntime.cs | Domains/Economy/Businesses/Saloon | (none) |
| SawmillShopRuntime | Domains/Economy/Businesses/Sawmill/SawmillShopRuntime.cs | Domains/Economy/Businesses/Sawmill | Orchestration/Player/ThreeBusinessSlice.cs |
| TailorShopRuntime | Domains/Economy/Businesses/Tailor/TailorShopRuntime.cs | Domains/Economy/Businesses/Tailor | (none) |
| BankRuntime | Domains/Economy/Businesses/Bank/BankRuntime.cs | Domains/Economy/Businesses/Bank | (none) |
| StagedProductionProcess | Domains/Economy/Generic/ProductionStageMachine.cs | Domains/Economy/Generic | Domains/Economy/Businesses/Bakery/BakeryStagedProductionChain.cs; Domains/Economy/Businesses/Blacksmith/ForgeRepairChain.cs |
| ProductionDueEventDriver | Domains/Economy/Generic/ProductionStageMachine.cs | Domains/Economy/Generic | Domains/Economy/Businesses/Bakery/BakeryStagedProductionChain.cs; Domains/Economy/Businesses/Blacksmith/ForgeRepairChain.cs |
| BakeryStagedProductionChain | Domains/Economy/Businesses/Bakery/BakeryStagedProductionChain.cs | Domains/Economy/Businesses/Bakery | (none) |
| ForgeRepairChain | Domains/Economy/Businesses/Blacksmith/ForgeRepairChain.cs | Domains/Economy/Businesses/Blacksmith | (none) |
| StoreRestockService | Domains/Economy/Businesses/GeneralStore/StoreProcurementChain.cs | Domains/Economy/Businesses/GeneralStore | Orchestration/HouseholdSlice/HouseholdVerticalSlicePhaseH.cs |
| RealDemandSource | Domains/Economy/Core/RealDemandSource.cs | Domains/Economy/Core | (none) |
| HouseholdMoveService | Domains/World/Property/HouseholdMoveService.cs | Domains/World/Property | (none) |
| ScenarioFailureNoticeBoard | Orchestration/Scenarios/Generation/ScenarioFailureNoticeBoard.cs | Orchestration/Scenarios/Generation | (none) |
| ButcherRuntime | Domains/Economy/Businesses/Butcher/ButcherRuntime.cs | Domains/Economy/Businesses/Butcher | Orchestration/Systems/SimulationSystemsHub.cs; Domains/Economy/Businesses/GeneralStore/StoreButchery.cs; Domains/Economy/Farming/FarmLivestock.cs; Domains/Economy/Farming/Integration/CullFlow.cs |
| BlacksmithRuntime | Domains/Economy/Businesses/Blacksmith/BlacksmithRuntime.cs | Domains/Economy/Businesses/Blacksmith | Domains/Economy/Businesses/Wheelwright/WagonRepairEconomy.cs |
| TanneryRuntime | Domains/Economy/Businesses/Tannery/TanneryRuntime.cs | Domains/Economy/Businesses/Tannery | (none) |
| PanelModalCoordinator | Presentation/UI/PanelModalCoordinator.cs | Presentation/UI | (none) |
| ResourceObservationReadModel | ReadModels/Civic/ResourceObservationReadModel.cs | ReadModels/Civic | (none) |
| SettlementCausalExplanation | ReadModels/Causal/SettlementCausalExplanation.cs | ReadModels/Causal | (none) |
| PersonInspectionReadModel | ReadModels/Person/PersonInspectionReadModel.cs | ReadModels/Person | (none) |
| PhysicalItemSpec | Domains/Economy/Storage/PhysicalItemSpec.cs | Domains/Economy/Storage | (none) |
| PersonInventoryReadModel | Domains/Population/PersonInventoryReadModel.cs | Domains/Population | (none) |
| HandOccupancyAuthority | Domains/Population/HandOccupancy.cs | Domains/Population | (none) |
| HandOccupancy | Domains/Population/HandOccupancy.cs | Domains/Population | Domains/Economy/Equipment/EquipmentTaskGate.cs |
| HandCustodyContext | Domains/Population/HandOccupancy.cs | Domains/Population | (none) |
| HandOperationResult | Domains/Population/HandOccupancy.cs | Domains/Population | (none) |
| WornContainer | Domains/Population/WornContainer.cs | Domains/Population | (none) |
| WornContainerEntry | Domains/Population/WornContainer.cs | Domains/Population | (none) |
| WornContainerSnapshot | Domains/Population/WornContainer.cs | Domains/Population | (none) |
| WornContainerOperationResult | Domains/Population/WornContainer.cs | Domains/Population | (none) |
| HandheldContainer | Domains/Population/HandheldContainer.cs | Domains/Population | (none) |
| HandheldContainerSnapshot | Domains/Population/HandheldContainer.cs | Domains/Population | (none) |
| HandheldContainerOperationResult | Domains/Population/HandheldContainer.cs | Domains/Population | (none) |
| DemandObservation | Domains/Economy/Logistics/DemandObservation.cs | Domains/Economy/Logistics | (none) |
| ReserveProjection | Domains/Economy/Logistics/ReserveProjection.cs | Domains/Economy/Logistics | (none) |
| SupplierComparison | Domains/Economy/Logistics/SupplierComparison.cs | Domains/Economy/Logistics | (none) |
| ReplenishmentPlan | Domains/Economy/Logistics/ReplenishmentPlan.cs | Domains/Economy/Logistics | (none) |
| ProductionPlan | Domains/Economy/Logistics/ProductionPlan.cs | Domains/Economy/Logistics | (none) |

## Notes per entry

- The fifteen `*Runtime` entries are the detailed per-business models
  (roughly 32k lines combined, plus the three Pass XII additions).
  Each ships with its own feature helpers in the same directory. None
  is invoked by `SimulationDrivers`, `SharedBusinessRuntimeManager`,
  or any other production system; the three Pass XII additions are
  additionally never instantiated (see their change-log note).
  `BankRuntime` is the thirteenth: it was missing from this registry
  until Pass XI WS1, because `Domains/Economy/Businesses/Bank` sits
  outside the Roslyn harness subset, so neither registry test could see
  the type at all (the assembly scan only enumerates the compiled
  harness assembly, and the hand-audited candidate list did not name
  it). It is registered here honestly as UNWIRED with its true status:
  zero production references outside its own directory, no bank ledger
  registered as a CreditCash store, no live deposit refresh before
  underwriting. It is NOT wired merely to clear this entry; bank
  binding is WS2 work under a scene-confirmation gate.
- `StagedProductionProcess` and `ProductionDueEventDriver` are the generic
  staged-production machinery. Their only production-tree references are the
  two chains below, which are themselves unwired, so the references are
  intra-subsystem and allowlisted.
- `BakeryStagedProductionChain` and `ForgeRepairChain` are the concrete
  staged-production chains. Covered by editor tests only.
- Pass XI WS2 update to the BankRuntime note above: the binding
  contract is now delivered (BankLedgerBindingRegistry: one ledger per
  bank, fail-loud on a second, live reads, no cached balance, no
  balances created; hub-owned lazy property;
  SimulationDrivers.BindBankLedger binding point), but it is
  deliberately NOT wired into underwriting, because the lender ledger
  and BankRuntime ledgers are disjoint and binding both would
  double-count. Player-bank scene binding remains outstanding under
  gate G4 and must never be described as wired. BankRuntime itself
  remains UNWIRED.
- `StoreRestockService` is instantiated only by the Phase H vertical-slice
  test fixture (`HouseholdVerticalSlicePhaseH.cs`), which is allowlisted.
  No production restock path calls it.
- `RealDemandSource` (Pass IX WS6, A3-09) is registered honestly as
  UNWIRED, deliberately. It collects fully-provenanced `PurchaseIntent`s
  from real household needs and business input needs, but its intents have
  no executor in the TownWorld purchase path — `TownWorldController`
  resolves demand itself when purchases happen. Wiring the source in
  without an executor behind it would fake execution (intents collected,
  nothing bought), so it stays a collection-side system until the
  purchase path grows a real intent executor. Its only production-tree
  references are inside its owning directory (the demand contracts and a
  mention in `SharedBusinessRuntimeManager`). Pass XI WS2 update: the
  wire-or-retire decision (gate G1, PASS9-008) is still open. WS2
  delivered a decision memo with four options (remain a read model,
  become an input to the existing purchase executor, replace a specific
  older demand-discovery path, or not yet necessary), recommending the
  intent-executor option only if demand collection should affect play,
  otherwise leaving it unwired. Central design fact: intents vs
  embodied shopping consuming the same needs exactly once is a
  double-buying hazard that any executor design must solve. Live
  purchase already happens through embodied shopping and the general
  store sale authority; nothing was wired in Pass XI.
- `ScenarioObjectiveService` was registered here from Pass IX WS6 until
  Pass XI WS1, when it was WIRED and its entry removed: the director
  start hook now calls `BeginScenario` for the started scenario, the
  drivers day change calls `Evaluate`, the scenario bootstraps register
  dated qualifiers from live evaluator state and rebuild them after
  load from the persisted registrations, and the three goal stores
  reconcile to one union. It is no longer unwired, so it no longer
  belongs in this table; its wiring is covered by the Pass XI WS1
  scenario start tests.
- `ScenarioFailureNoticeBoard` (Pass XI WS5) is the Unity-free,
  exactly-once scenario-failure notice surface: one notice per
  scenario-plus-goal even with multiple subscribers. It has no
  production caller yet: the StartPanel reads director state only and
  holds no `ScenarioObjectiveService` reference, so wiring the board
  into the live panel is scene/reference work for a Unity session
  (gate G4), not a safe blind edit. It is registered here honestly as
  UNWIRED rather than hidden in the scan baseline, because unlike the
  Saloon types it has no production caller in any file, inside or
  outside the harness subset. Covered by editor tests only.
- `PhysicalItemSpec` (Priority 9 Slice F1, 2026-11-08) is the physical
  specification of one unit of an item (mass, volume, dimensions; data
  flags only). It is registered here honestly as UNWIRED by design, not
  oversight: the Slice F1 brief is data-only — the registry is seeded,
  nothing reads it yet, and no flow may change until a later slice wires
  a reader. Its sibling types need no row: `PhysicalMeasure` and
  `PhysicalDimensions` are used by the spec itself, and
  `PhysicalItemSpecRegistry` is a static class (scan-exempt). Remove this
  row the day a production reader outside `Domains/Economy/Storage`
  references the spec (Slice 2 capacity queries are the first expected
  reader); do not wire it merely to clear the entry.
- `PersonInventoryReadModel` (Priority 9 Slice F3 / Priority B,
  2026-11-08) is the read-only personal and player inventory query
  layer (carried batches, household lots owned by a person, equipment
  grants held, carried load, remaining carry units, carried-container
  contents). It is registered here honestly as UNWIRED by design, not
  oversight: the Slice 3 brief wires nothing into live flows — the read
  model is constructed in tests against the real authorities (shopping
  loop custody, household inventory registry, equipment grant resolver)
  and no flow calls it yet. Its sibling types need no row:
  `CarriedItem`, `OwnedHouseholdLot`, `HeldEquipment` and
  `CarriedContainerContents` are nested inside the read model and
  `PersonCarryRemaining` is a nested struct (the scan enumerates
  concrete public top-level classes only), and `PersonItemCondition` is
  an enum. Remove this row the day a production flow outside
  `Domains/Population` calls the read model; do not wire it merely to
  clear the entry. Priority V1 wires the read model into the shopping
  loop itself (`HouseholdShoppingLoop.PersonInventory`, deposit
  verification through the read model) — but the loop sits in the same
  owning directory, so under the scan's caller rule (same-directory
  references do not count) that is not a production caller and this row
  stays. The read model reads the F1/F2 types only through
  `PhysicalLoad` (a struct, scan-exempt) and references no other
  registered-unwired type by name, so the `PhysicalItemSpec` row above
  stays accurate and stays in place. (The `PhysicalCapacityEvaluator`
  row the Slice 3 note named here was removed by Priority V1 — see the
  change log — so it no longer appears above.)
- `HandOccupancyAuthority` with `HandOccupancy`, `HandCustodyContext`,
  and `HandOperationResult` (physical hands campaign P1a, 2026-11-08)
  is the per-person hand authority core: two hand slots, left and
  right, each holding at most one LotKey plus its BatchId, with
  one-handed allocate, two-handed allocate, release, and a
  hand-to-container transfer refused until P2 supplies a container
  type. Hand requirement is a caller-supplied parameter, refused when
  none is supplied; nothing is derived from mass, name, or category.
  The four types are registered here honestly as UNWIRED by design, not
  oversight: the P1a brief wires nothing into live flows — the
  authority is constructed in tests only and no flow calls it yet. Its
  sibling types need no row: `HandSlot` and `HandRequirement` are enums
  (the scan enumerates concrete public top-level classes only). Remove
  these rows the day a production flow outside `Domains/Population`
  calls the authority (P1c task gating and the P4 vertical slice are
  the first expected callers); do not wire it merely to clear the
  entries. The authority records `CustodyTransferLog` entries with the
  new `hand-pickup` and `hand-release` causes only when a log and a
  custody context are both supplied, but that reference is to the log,
  not from any production flow to the authority, so these rows stay.
- `PerishableLotState` (Priority E Slice 5, 2026-11-08) is the
  perishable condition of one lot: separate physical-condition,
  food-safety, remaining-life, handling-history, and perceived-vs-known
  dimensions (Canon XXXI 4.5/4.6, Technical XIV 4.1/4.2); deterministic
  simulation-time deterioration applying only the authored bakery
  AgeToDay rule; spoilage accounting that removes nothing from any live
  authority. The row for it was removed by the Priority spoilage
  integration slice — see the change log — so it no longer appears
  above. Its sibling types need no row:
  `PerishablePhysicalCondition`, `PerishableSafetyState`, and
  `PerishableDisposition` are enums and `UsefulLifeEstimate`,
  `PerishableHandlingEvent`, `SpoilageRecord`, and the nested
  `SaveDto` are structs (the scan enumerates concrete public top-level
  classes only). The `PhysicalItemSpec`,
  `PersonInventoryReadModel`, and `LotIdentityResolver` rows above stay:
  the state reads no registered-unwired type by name (it consults no F1
  spec parameter — the spec carries no aging parameter — and names
  `BakeryBreadCatalog` only for the authored bread rule).
- `DemandObservation` (Priority F Slice 6, 2026-11-08) is the
  business-level demand observation book: completed sales, observed
  demand, stockout unmet demand, and partial fills recorded separately,
  with daily/weekly/monthly history, day-of-week pattern, and trend from
  recorded history only; the same unresolved need is counted once, not
  once per retry. It is registered here honestly as UNWIRED by design,
  not oversight: the Slice 6 brief instruments no live flow — the book
  is fed by explicit Record calls in tests and by derivation from the F3
  custody log, and no flow calls it yet. Its sibling types need no row:
  `DemandAttemptRecord`, `DailyDemandPoint`, `DemandHistoryPoint`, and
  `DemandTrend` are structs and `DemandTrendDirection` is an enum (the
  scan enumerates concrete public top-level classes only). Remove this
  row the day a production flow feeds or consults the book; do not wire
  it merely to clear the entry. The `PhysicalItemSpec`,
  `PhysicalCapacityEvaluator`, `PersonInventoryReadModel`,
  `LotIdentityResolver`, and `PerishableLotState` rows above stay: the
  book reads the F3 log only and references no other registered-unwired
  type by name.
- `ReserveProjection` (Priority F Slice 6, 2026-11-08) is the adaptive
  reserve recommendation: recomputed from recent demand, uncertainty,
  usable stock, forecast spoilage, commitments, incoming deliveries,
  production capacity, lead time, and available storage, with no fixed
  reserve value and no automatic procurement — a recommendation only. It
  is registered here honestly as UNWIRED by design, not oversight: the
  Slice 6 brief wires nothing into live flows — the projection is
  computed in tests from supplied inputs and no flow calls it yet. Its
  sibling types need no row: `ReserveInputs`, `ReserveQuantity`,
  `LeadTimeDays`, and `UsableStockAssessment` are structs and
  `ReserveConfidence` is an enum (the scan enumerates concrete public
  top-level classes only). Remove this row the day a production flow
  consults a projection; do not wire it merely to clear the entry. The
  `PhysicalItemSpec`, `PhysicalCapacityEvaluator`,
  `PersonInventoryReadModel`, `LotIdentityResolver`,
  `PerishableLotState`, and `DemandObservation` rows above stay: the
  projection reads `PerishableLotState` only from inside its own
  owning directory (same-directory references are not production
  callers), and `DemandObservation` only through the caller-supplied
  `ReserveInputs` bridge in tests.
- `SupplierComparison` (Priority P5, 2026-11-08) is the read-only
  supplier comparison: options quoted by the authorities that actually
  hold them (Millbrook table, catalog quotes, named-SKU orders, settled
  shipment charges), ranked by expected profit — never by unit price
  alone — with per-option reasoning and per-axis Unknown handling
  (profit, arrival, reliability, storage, consolidation). It is
  registered here honestly as UNWIRED by design, not oversight: the
  P5 brief wires nothing into live flows — the comparison is computed
  in tests from supplied options and no flow calls it yet. Its sibling
  types need no row: `CentsAmount`, `SupplierStorageFit`,
  `SupplierReliability`, `SpoilageForecast`, `SupplierOption`,
  `RankedSupplierOption`, and `SupplierComparisonResult` are structs
  (the scan enumerates concrete public top-level classes only).
  Remove this row the day a production flow consults a comparison; do
  not wire it merely to clear the entry. The `PhysicalItemSpec`,
  `PhysicalCapacityEvaluator`, `PersonInventoryReadModel`,
  `LotIdentityResolver`, `PerishableLotState`, `DemandObservation`,
  and `ReserveProjection` rows above stay: the comparison reads the
  F3 log and the Slice 5 states only from inside its own owning
  directory, reuses the Slice 6 `LeadTimeDays` and `ReserveQuantity`
  structs from the same directory, and references the F1/F2 types
  nowhere.
- `ReplenishmentPlan` (Priority P5, 2026-11-08) is the replenishment
  plan that connects a Slice 6 recommended reserve to the best
  supplier option for the shortfall: it selects the best affordable
  comparable option, states expected cost, arrival, and profit, and
  orders nothing. It is registered here honestly as UNWIRED by design,
  not oversight: the P5 brief wires nothing into live flows — the plan
  is computed in tests from a supplied projection and comparison and
  no flow calls it yet. Remove this row the day a production flow
  consults a plan; do not wire it merely to clear the entry. The
  `PhysicalItemSpec`, `PhysicalCapacityEvaluator`,
  `PersonInventoryReadModel`, `LotIdentityResolver`,
  `PerishableLotState`, `DemandObservation`, `ReserveProjection`, and
  `SupplierComparison` rows above stay: the plan reads the projection
  and the comparison result only, both from inside its own owning
  directory.
- `ProductionPlan` (Priority P6, 2026-11-08) is the production
  planning foundation that connects a projected finished-goods demand
  to required batches through the recorded Bakery recipe, then to
  recipe inputs, usable stock, commitments, confirmed incoming
  supplies, expected spoilage, and cash and capacity constraints. It
  recommends batches and orders nothing. It is registered here
  honestly as UNWIRED by design, not oversight: the P6 brief wires
  nothing into live flows — the plan is computed in tests from
  supplied inputs and no flow calls it yet. Remove this row the day a
  production flow consults a plan; do not wire it merely to clear the
  entry. The `PhysicalItemSpec`, `PersonInventoryReadModel`,
  `DemandObservation`, `ReserveProjection`, `SupplierComparison`, and
  `ReplenishmentPlan` rows above stay: the plan reads the Slice 5
  states, the Slice 6 observation and projection types, and the P5
  supplier option only from inside its own owning directory, and
  references the F1/F2 types nowhere. (`PhysicalCapacityEvaluator`,
  `LotIdentityResolver`, and `PerishableLotState` rows were removed by
  V1 and the spoilage integration; earlier notes that list them among
  the rows that "stay" describe the pre-integration state.)
- `HouseholdMoveService` (Pass VIII Worker 2; registered by the Pass IX
  review fixes, R-13) executes a household move through the real
  authorities — validated before anything changes, old occupancies ended,
  new occupancies created under a recorded agreement, no partial moves.
  It has no production caller: production occupancy changes go through
  `HousingAuthority` directly (acquisitions, tenancies, estate and
  absence paths), never through this service's all-or-nothing move
  operation. Covered by editor tests only. It also remains in the
  assembly scan's feature-internal baseline, which is inert duplication
  (the scan accounts a type by caller, registry, or baseline — any one
  suffices). Pass XI WS2 re-confirmation: the service stays caller-free
  BY DECISION (3 tests prove its atomic guarantee; no approved gameplay
  path requires whole-household atomicity; acquisition closing
  occupancy does not state relocation vs second property, so routing
  it without a relocation flag would change its semantics).

## Audited production-reachable systems (NOT registry entries)

The Pass IX audit re-checked a set of systems that look similar to the
registered entries but ARE reached from production call paths. They were
never registered here and must not be added as historical entries; this
note records their audited status so the question does not reopen:

- `MineProductionCycle` and its production chain — driven via
  `MineProductionDriver` and referenced by `SharedBusinessRuntimeManager`.
- `FreightCompanyRuntime` — referenced by `SimulationDrivers`,
  `PostalService`, `DeliveryService`, and the logistics models.
- `FarmCycleDriver` — referenced by `SharedBusinessRuntimeManager`.
- `EstateLifecycleDriver` — driven from `SimulationDrivers`.
- `DomesticLaborDriver` — driven from `SimulationDrivers` and owned by
  `SimulationSystemsHub`.
- `DailyActivityPlanner` — driven from `SimulationDrivers` and the
  population services.
- `ConversationRunner` — run by `DailySocialDrive`, which
  `SimulationDrivers` invokes.

## Change log

- 2026-10-13: registry created (Pass VIII self-directed fixes, G-09/G-10/G-12).
  Seventeen entries. No rewiring, no deletions.
- Pass IX review fix (R-08): `ScenarioObjectiveService` registered —
  hub-owned and persisted, evaluation source-complete with the
  completion latch, production start pending the scene bootstrap hook
  (declared Unity handoff, deliberately not invented here).
- Pass IX review fix (R-13): `HouseholdMoveService` registered — fully
  implemented move execution with no production caller (verified by
  source search: no references outside its own file in the production
  tree).
- Pass XI WS5: `ScenarioFailureNoticeBoard` registered as unwired —
  exactly-once failure-notice surface with no production caller yet;
  live-panel wiring is Unity-session scene work (gate G4).
- Pass XII Worker F: `ButcherRuntime`, `BlacksmithRuntime` and
  `TanneryRuntime` are registered here after Worker B's referral.
  None of the three is instantiated anywhere in production:
  `ButcherRuntime`'s hub plumbing (`SimulationSystemsHub` list,
  `GetOrCreateButcherRuntime`, save/load rehydration) is never
  driven — `GetOrCreateButcherRuntime` has no production caller, so
  the list is always empty and rehydration can never populate it;
  the three parameter-passing helpers (`StoreButchery`,
  `FarmLivestockMarket.ExecuteSale`, `CullFlow.CullAndSellCow`) are
  themselves never called in production, so no instance ever flows
  into them. `BlacksmithRuntime`'s only non-test reference is
  `WagonRepairEconomy.FindAvailableIronwork`, and that helper is
  itself never called in production. `TanneryRuntime` has no
  production reference at all (only a comment mention, which the
  registry test strips). The allowlists above cover plumbing
  references only, NOT wiring: a future real wiring must remove
  the allowlisted file from the row, and reviewers must check
  `GetOrCreateButcherRuntime` callers directly, because drivers
  iterate the hub's `ButcherRuntimes` property (not the type name)
  and the type-name test cannot see that wiring. Butcher itself is
  LIVE via the archetype path and Blacksmith is LIVE via the
  archetype path; only these detailed runtime classes are unwired.
  None of the three files is in the Roslyn harness subset, so the
  assembly scan cannot see them — this file-based registry test is
  their only machine check, which is why they are also added to the
  registry test's candidate list.
- Priority 8 Slice 3 (2026-11-08): `SawmillShopRuntime` stays registered
  as UNWIRED in the live tick — no driver, bootstrapper, or daily flow
  constructs it. Its only production-tree reference is the test-driven
  scripted `ThreeBusinessSlice` run (`ThreeBusinessSliceRun.Execute`,
  invoked only by tests), which constructs one slice-local mill and runs
  sawing through it as a demo; that file is allowlisted in the row above
  as fixture-adjacent scaffolding, NOT as wiring. If the sawmill is ever
  constructed by a live driver or the bootstrapper, remove the allowlist
  and the row together.
- Priority 8 Slice 3 (2026-11-08): `BakeryShopRuntime` was removed from
  the table above, not allowlisted: it is wired into the live tick by the
  campaign's own deliberate Bakery work. The bootstrapper owns the single
  live instance, binds its owner, builds the `Series2BakeryProductionDriver`
  on it, and registers its Pass 9 state with `SaveLoadManager`
  (`FirstLedgerSliceBootstrapper.cs:1990-2019`); `SimulationDrivers`
  drives daily wholesale planning from it and builds the Millbrook flour
  custody adapter on it (`SimulationDrivers.cs:3146`, `:7081`);
  `Series2Pass9Save` captures and restores it; the BreadDrivers
  (`Series2BakeryProductionDriver`, `BakeryWholesaleDailyDriver`,
  `BakeryNotionsDailySupply`, `BakeryFlourLocalPurchase`,
  `Series2BakeryFuelSource`, `Series2NotionsSupply`,
  `Series2StoreToBakeryFeed`, `Series2DealerBakeryFuelSource`,
  `Review2BakeryToStoreWholesaleDriver`,
  `MillbrookFlourFreightCustodyAdapter`, `ScenarioOpeningEquipmentEndowment`,
  `ScenarioOpeningWholesaleAccounts`) all operate on it. All 16 production
  call sites were verified in source in Priority 8 Slice 3; none was
  accidental. Its removal resolves R-06 together with the
  `SettlementCivicReadModel` removal above and the `SawmillShopRuntime`
  allowlist.
- Pass XII Worker D: the four remaining panel-contract types above are
  source-complete read models / panel contracts (scenario HUD is NOT
  among them: `ScenarioHudReadModel` HAS a production caller in
  `ScenarioHudSnapshotBuilder`, in another directory, so it is
  accounted for by caller, not by this table; the static contracts
  `AcquisitionDealReadModel`, `StartupNoticeGate`,
  `ScenarioGuidanceGate`, and `ScenarioHudSnapshotBuilder` are
  scan-exempt as abstract+sealed in IL). The four registered types
  have no production caller until Codex binds them in the scene
  (modal coordinator, Resources tab, causal surface,
  person inspection). Registering them here is accounting for
  scene-binding-pending contracts, NOT a claim that they are
  dormant runtimes or broken systems; do not remove a row until a
  real production caller outside its owning directory exists.
  The fifth, `SettlementCivicReadModel`, was removed in Priority 8
  Slice 3 (2026-11-08): `GeneralStorePanelController.UpdateGovernment()`
  now builds it in both branches for the live Civic tab (Plan 05 W-11,
  verified by `Pass12PlayerExperienceReadModelTests.Civic_BuilderMatchesPanelText`),
  so it is wired, not scene-binding-pending.
- Pass XI WS2: BankRuntime note updated for the delivered binding
  contract (deliberately not wired into underwriting; scene binding
  outstanding under G4); HouseholdMoveService re-confirmed caller-free
  by decision; RealDemandSource note updated for the decision memo
  (gate G1 still open). No entry was closed merely because a test
  invoked the class.
- Pass IX (WS6): `RealDemandSource` registered as unwired (A3-09, see its
  note — unwired by decision, not oversight). Production-reachable audit
  note added for the mine chain, freight, farm, estate, domestic-labor,
  daily-activity, and conversation systems; no historical entries added
  for them and all seventeen still-unwired entries remain intact. The
  registry test also gained an assembly-scan enumeration (see
  `Pass9RegistryAssemblyScanTests`) that sweeps the compiled assembly for
  further unwired concrete types, with a small allowlist of
  source-only-by-design types (PassengerManifest, PersonMoneyAccounts,
  ScenarioCoordinator, ScenarioWorldBuilder, RealDemandSource,
  BusinessValuationService).
- Priority 9 Slice F1 (2026-11-08): `PhysicalItemSpec` registered as
  UNWIRED by design — data-only spec registry, nothing reads it yet;
  remove the row when a production reader outside
  `Domains/Economy/Storage` is wired (Slice 2 capacity queries first).
- Priority 9 Slice F2 (2026-11-08): `PhysicalCapacityEvaluator`
  registered as UNWIRED by design — delegating capacity evaluator, no
  flow calls it yet; remove the row when a production flow calls it.
  The `PhysicalItemSpec` row stays: the evaluator reads the registry
  only from inside `Domains/Economy/Storage`, so it is not a production
  reader outside the spec's directory under the scan's caller rule.
- Priority 9 Slice F3 / Priority B (2026-11-08):
  `PersonInventoryReadModel` registered as UNWIRED by design — read-only
  personal/player inventory query layer, constructed in tests against
  the real authorities, no flow calls it yet; remove the row when a
  production flow calls it. The `PhysicalItemSpec` and
  `PhysicalCapacityEvaluator` rows stay: the read model reads the F1/F2
  types only through `PhysicalLoad` (a struct) and references no other
  registered-unwired type by name.
- Priority 9 Slice F4 / Priority D (2026-11-08):
  `LotIdentityResolver` registered as UNWIRED by design — read-only
  lot identity resolver, constructed in tests against the real
  authorities, no flow calls it yet; remove the row when a
  production flow calls it. The `PhysicalItemSpec`,
  `PhysicalCapacityEvaluator`, and `PersonInventoryReadModel` rows
  stay: the resolver reads the F1/F2 types only through
  `PhysicalLoad` (a struct) and references no other
  registered-unwired type by name. `LotIdentity` needs no row: it
  has production callers outside its owning directory (the three
  provenance-append sites).
- Priority E Slice 5 (2026-11-08):
  `PerishableLotState` registered as UNWIRED by design — perishable
  lot condition foundation, constructed in tests alongside the real
  authorities, no flow calls it yet and no authority DTO carries it.
  The `PhysicalItemSpec`, `PhysicalCapacityEvaluator`,
  `PersonInventoryReadModel`, and `LotIdentityResolver` rows stay:
  the state reads no registered-unwired type by name.
- Priority F Slice 6 (2026-11-08):
  `DemandObservation` registered as UNWIRED by design — demand
  observation book, fed by explicit Record calls in tests and by
  derivation from the F3 custody log, no live flow instrumented and no
  flow calls it yet. `ReserveProjection` registered as UNWIRED by
  design — adaptive reserve recommendation, computed in tests from
  supplied inputs, no flow calls it yet. The `PhysicalItemSpec`,
  `PhysicalCapacityEvaluator`, `PersonInventoryReadModel`,
  `LotIdentityResolver`, and `PerishableLotState` rows stay: the book
  reads the F3 log only, and the projection reads `PerishableLotState`
  only from inside its own owning directory.
- Priority P5 (2026-11-08):
  `SupplierComparison` registered as UNWIRED by design — read-only
  supplier comparison, ranked by expected profit with per-option
  reasoning, computed in tests from authority-quoted options, no flow
  calls it yet. `ReplenishmentPlan` registered as UNWIRED by design —
  reserve-to-supplier plan stating expected cost, arrival, and profit,
  computed in tests, orders nothing, no flow calls it yet. The
  `PhysicalItemSpec`, `PhysicalCapacityEvaluator`,
  `PersonInventoryReadModel`, `LotIdentityResolver`,
  `PerishableLotState`, `DemandObservation`, and `ReserveProjection`
  rows stay: the comparison and the plan read the F3 log, the Slice 5
  states, and the Slice 6 types only from inside their own owning
  directory, and reference the F1/F2 types nowhere.
- Priority 8 Slice 3 (2026-11-08, overnight economy repair campaign):
  `BakeryShopRuntime` removed (wired into the live tick — 16 production
  call sites verified in source, none accidental; see the Slice 3 note
  above); `SettlementCivicReadModel` removed (wired into the live Civic
  tab — `GeneralStorePanelController.UpdateGovernment()` builds it in both
  branches; see the Pass XII Worker D note above); `SawmillShopRuntime`
  stays registered, with `Orchestration/Player/ThreeBusinessSlice.cs`
  allowlisted as fixture-adjacent scripted-slice scaffolding (see the
  Slice 3 note above). The 145 unaccounted types from R-05 were classified
  the same day in `Pass9RegistryAssemblyScanTests` (144 feature-internal
  baseline, 1 source-only-by-design: `Series2FuelDealerSupplierAdapter`,
  whose `RegisterIn` is the Codex bootstrapper registration call per its
  own doc comment); no type was entered in this registry on the basis of
  the plan alone, and no standalone unwired system was found among the 145.
- Priority V1 (2026-11-08): `PhysicalCapacityEvaluator` and
  `LotIdentityResolver` removed from the table above — both are wired
  into production by the inventory-and-custody vertical slice. The
  shopping loop consults the evaluator at settle time
  (`HouseholdShoppingLoop.SettleAtCounter`, OnFootPerson leg with the
  loop's live carry cap supplied via
  `PhysicalCapacityEvaluationContext.OnFootCarryCapUnits`; the
  cap-aware branch answers on units only, mass and volume stay
  Unknown) and resolves each deposited lot through the resolver
  (`HouseholdShoppingLoop.RecordDepositedLotIdentity`, all three
  receipt paths), and the meal service resolves each consumed lot
  through the resolver (`HouseholdMealService.TryServeFromInventory`,
  `LastMealLotIdentities`). Both callers sit outside the types'
  owning directories (`Domains/Population` vs
  `Domains/Economy/Storage` and `Domains/Economy/Logistics`), so the
  registry test and the assembly scan both count them as production
  callers. The `PhysicalItemSpec` row stays: the loop reaches the
  spec only through the evaluator, which reads the registry from
  inside `Domains/Economy/Storage`, so no production reader outside
  the spec's owning directory exists yet. The
  `PersonInventoryReadModel` row stays: the loop owns a read model
  (`HouseholdShoppingLoop.PersonInventory`) and verifies deposits
  through it, but the loop sits in the same owning directory
  (`Domains/Population`), so under the caller rule that is not a
  production caller — the row is removed the day a caller outside
  `Domains/Population` uses the read model. `PerishableLotState`,
  `DemandObservation`, `ReserveProjection`, `SupplierComparison`, and
  `ReplenishmentPlan` stay: V1 wires none of them (no Slice 5 state
  is attached to any authority; the meal and loop paths consult no
  demand, reserve, or supplier type). Earlier notes that list the
  evaluator and resolver rows among the rows that "stay" describe the
  pre-V1 state and are superseded by this entry.
- Priority spoilage integration (2026-11-08): `PerishableLotState`
  removed from the table above — the Slice 5 condition state is now
  carried by real production authorities through real transfers. The
  bakery attaches a Fresh state at bake (`BakeryShopRuntime.BakeBatch`)
  and ages it by the authored AgeToDay rule (`AgeShelf`); Millbrook
  attaches an Unknown state at order time
  (`OffMapAidBusinessRuntime.TryPlaceOrder`) and the freight adapter
  re-attaches it to the flour lot at receipt
  (`MillbrookFlourFreightCustodyAdapter.ReceiveCargoLot`); the bakery
  adapter carries the taken part's state through the sale
  (`Series2BakerySupplierAdapter`, via the new `ILotConditionSupplier`
  surface, read by `HouseholdShoppingLoop.SettleAtCounter`) and the
  loop deposits it onto the household lot (all three receipt paths);
  every lot record carries the state through its own DTO pair
  (bread shelf, flour bin, cargo lots, custody batches, driver DTO,
  household registry). The callers sit outside the type's owning
  directory (`Domains/Economy/Logistics` vs `Domains/Economy/Businesses`
  and `Domains/Population`), so the registry test and the assembly scan
  both count them as production callers. `DemandObservation`,
  `ReserveProjection`, `SupplierComparison`, and `ReplenishmentPlan`
  stay: this slice wires none of them. Earlier notes that list the
  `PerishableLotState` row among the rows that "stay" describe the
  pre-spoilage-integration state and are superseded by this entry.
  Blocked seams recorded in the slice checkpoint, not wired here: the
  General Store category pool is anonymous (a pool purchase carries no
  lot condition); the Review2 wholesale driver takes shelf units
  directly, so its delivery record carries provenance but no states;
  the retail lot DTO lives in harness-excluded `SaveDtos.cs` whose
  harness copy is a stale verbatim extraction; the freight shipment
  carries no condition field (the adapter re-reads the source lot at
  receive instead); directly seeded bin lots carry no state.
- Priority P6 (2026-11-08):
  `ProductionPlan` registered as UNWIRED by design — production
  planning foundation, computed in tests from supplied inputs
  (projected demand, recorded Bakery recipe, stock, commitments,
  confirmed incoming, spoilage, cash, capacity, lead times),
  recommends batches, orders nothing, no flow calls it yet. The
  `PhysicalItemSpec`, `PersonInventoryReadModel`,
  `DemandObservation`, `ReserveProjection`, `SupplierComparison`,
  and `ReplenishmentPlan` rows stay: the plan reads the Slice 5
  states, the Slice 6 types, and the P5 option only from inside its
  own owning directory, and references the F1/F2 types nowhere.
- Physical hands campaign P1a (2026-11-08):
  `HandOccupancyAuthority`, `HandOccupancy`, `HandCustodyContext`,
  and `HandOperationResult` registered as UNWIRED by design — per-person
  hand authority core, no production caller yet. The
  `PhysicalItemSpec`, `PersonInventoryReadModel`,
  `DemandObservation`, `ReserveProjection`, `SupplierComparison`,
  `ReplenishmentPlan`, and `ProductionPlan` rows stay.
- Physical hands campaign P1b (2026-11-08):
  hand occupancy read view (`HandOccupancyView` and `HandSlotView`,
  nested inside `PersonInventoryReadModel`, which already has a
  registry row, so no new row is needed) plus last-refusal
  diagnostics on the authority and the four hand causes moved from
  `HandOccupancyAuthority` onto `CustodyTransferLog`. Category B:
  no production caller passes the new hand supplier yet, so the view
  is reachable only from tests until P4 wires it.
- Physical hands campaign P1c (2026-11-08):
  hand gate in task start (`EquipmentTaskGate` reads the new
  `TaskDefinition.HandRequirementSpec` string at `CheckStart`, after
  the equipment check, through the optional `HandOccupancySupplier`;
  freight definitions are excluded outright). Category B: no
  production caller supplies hand state and no definition carries a
  hand requirement yet, so the gate is installed but dormant until
  P4 wires the supplier and P5 authors a requirement. Check-only:
  starting, completing, interrupting, or cancelling a task never
  changes hand state, so no release hook lands here. The gate's
  optional `HandOccupancy` reference is allowlisted on that type's
  registry row above: the supplier stays null in production, so the
  type is still unwired and the row stays.
- Physical hands campaign P2a (2026-11-08):
  one worn container kind, a satchel (`WornContainer` with
  `WornContainerKind`, `WornContainerCapacity`, `WornContainerEntry`,
  `WornContainerSnapshot`, and `WornContainerOperationResult`, all in
  `Domains/Population/WornContainer.cs`): caller-supplied capacity on
  four axes, Wear and Remove recording the wearer, Place and Take
  holding lot references only, hand-to-container and container-to-hand
  transfer on `HandOccupancyAuthority`, fit through the new
  `WornSatchel` leg of `PhysicalCapacityEvaluator`, container contents
  in the read model through an optional container supplier, and the
  `ContainerCustodian` helper plus `ContainerAuthority` on
  `CustodyTransferLog`. Category B: no production caller owns
  containers yet, so the container is reachable only from tests until
  P4 wires it. The four class types get registry rows above; the
  `WornContainerKind` enum and the `WornContainerCapacity` struct get
  no rows, because the registry test resolves a row only through a
  `class` declaration and both would fail that check as written, and
  the assembly scan only sweeps classes, so neither needs a row. The
  evaluator's `WornContainerCapacity` reference needs no allowlist
  for the same reason: it is a struct, not a registered class.
- Physical hands campaign P2b (2026-11-08):
  one handheld container kind, a pail (`HandheldContainer` with
  `HandheldContainerKind`, `HandheldContainerSnapshot`, and
  `HandheldContainerOperationResult`, all in
  `Domains/Population/HandheldContainer.cs`, reusing the P2a
  `WornContainerCapacity` capacity struct and `WornContainerEntry`
  entry class): held as its own key in a hand slot, PickUp and SetDown
  recording the holder and the held hand, Place and Take mirroring the
  satchel with the held-by check replacing the worn-by check, pick up
  and set down and move-between-hands and hand-to-held-container and
  held-container-to-hand transfer on `HandOccupancyAuthority`, fit
  through the new `HandheldPail` leg of `PhysicalCapacityEvaluator`,
  held-container contents in the read model through an optional
  held-container supplier, and the three container causes
  (`container-pickup`, `container-setdown`, `container-move`) on
  `CustodyTransferLog`. Category B: no production caller owns
  containers yet, so the container is reachable only from tests until
  P4 wires it. The three class types get registry rows above; the
  `HandheldContainerKind` enum gets no row, because the registry test
  resolves a row only through a `class` declaration and it would fail
  that check as written, and the assembly scan only sweeps classes, so
  it needs no row. `HeldContainerView` and `HeldContainerContents`
  are nested inside `PersonInventoryReadModel`, which already has a
  registry row, so no new row is needed for them.
