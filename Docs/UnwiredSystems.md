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
| BakeryShopRuntime | Domains/Economy/Businesses/Bakery/BakeryShopRuntime.cs | Domains/Economy/Businesses/Bakery | (none) |
| BarberShopRuntime | Domains/Economy/Businesses/Barber/BarberShopRuntime.cs | Domains/Economy/Businesses/Barber | (none) |
| DoctorPracticeRuntime | Domains/Economy/Businesses/Doctor/DoctorPracticeRuntime.cs | Domains/Economy/Businesses/Doctor | (none) |
| GrainElevatorShopRuntime | Domains/Economy/Businesses/GrainElevator/GrainElevatorShopRuntime.cs | Domains/Economy/Businesses/GrainElevator | (none) |
| GrainMillShopRuntime | Domains/Economy/Businesses/GrainMill/GrainMillShopRuntime.cs | Domains/Economy/Businesses/GrainMill | (none) |
| HotelShopRuntime | Domains/Economy/Businesses/Hotel/HotelShopRuntime.cs | Domains/Economy/Businesses/Hotel | (none) |
| LumberYardShopRuntime | Domains/Economy/Businesses/LumberYard/LumberYardShopRuntime.cs | Domains/Economy/Businesses/LumberYard | (none) |
| NewspaperRuntime | Domains/Economy/Businesses/Newspaper/NewspaperRuntime.cs | Domains/Economy/Businesses/Newspaper | (none) |
| RestaurantShopRuntime | Domains/Economy/Businesses/Restaurant/RestaurantShopRuntime.cs | Domains/Economy/Businesses/Restaurant | (none) |
| SaloonRuntime | Domains/Economy/Businesses/Saloon/SaloonRuntime.cs | Domains/Economy/Businesses/Saloon | (none) |
| SawmillShopRuntime | Domains/Economy/Businesses/Sawmill/SawmillShopRuntime.cs | Domains/Economy/Businesses/Sawmill | (none) |
| TailorShopRuntime | Domains/Economy/Businesses/Tailor/TailorShopRuntime.cs | Domains/Economy/Businesses/Tailor | (none) |
| BankRuntime | Domains/Economy/Businesses/Bank/BankRuntime.cs | Domains/Economy/Businesses/Bank | (none) |
| StagedProductionProcess | Domains/Economy/Generic/ProductionStageMachine.cs | Domains/Economy/Generic | Domains/Economy/Businesses/Bakery/BakeryStagedProductionChain.cs; Domains/Economy/Businesses/Blacksmith/ForgeRepairChain.cs |
| ProductionDueEventDriver | Domains/Economy/Generic/ProductionStageMachine.cs | Domains/Economy/Generic | Domains/Economy/Businesses/Bakery/BakeryStagedProductionChain.cs; Domains/Economy/Businesses/Blacksmith/ForgeRepairChain.cs |
| BakeryStagedProductionChain | Domains/Economy/Businesses/Bakery/BakeryStagedProductionChain.cs | Domains/Economy/Businesses/Bakery | (none) |
| ForgeRepairChain | Domains/Economy/Businesses/Blacksmith/ForgeRepairChain.cs | Domains/Economy/Businesses/Blacksmith | (none) |
| StoreRestockService | Domains/Economy/Businesses/GeneralStore/StoreProcurementChain.cs | Domains/Economy/Businesses/GeneralStore | Orchestration/HouseholdSlice/HouseholdVerticalSlicePhaseH.cs |
| RealDemandSource | Domains/Economy/Core/RealDemandSource.cs | Domains/Economy/Core | (none) |
| HouseholdMoveService | Domains/World/Property/HouseholdMoveService.cs | Domains/World/Property | (none) |

## Notes per entry

- The thirteen `*Runtime` entries are the detailed per-business models
  (roughly 32k lines combined). Each ships with its own feature helpers in
  the same directory. None is invoked by `SimulationDrivers`,
  `SharedBusinessRuntimeManager`, or any other production system.
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
  mention in `SharedBusinessRuntimeManager`).
- `ScenarioObjectiveService` was registered here from Pass IX WS6 until
  Pass XI WS1, when it was WIRED and its entry removed: the director
  start hook now calls `BeginScenario` for the started scenario, the
  drivers day change calls `Evaluate`, the scenario bootstraps register
  dated qualifiers from live evaluator state and rebuild them after
  load from the persisted registrations, and the three goal stores
  reconcile to one union. It is no longer unwired, so it no longer
  belongs in this table; its wiring is covered by the Pass XI WS1
  scenario start tests.
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
  suffices).

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
