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
| StagedProductionProcess | Domains/Economy/Generic/ProductionStageMachine.cs | Domains/Economy/Generic | Domains/Economy/Businesses/Bakery/BakeryStagedProductionChain.cs; Domains/Economy/Businesses/Blacksmith/ForgeRepairChain.cs |
| ProductionDueEventDriver | Domains/Economy/Generic/ProductionStageMachine.cs | Domains/Economy/Generic | Domains/Economy/Businesses/Bakery/BakeryStagedProductionChain.cs; Domains/Economy/Businesses/Blacksmith/ForgeRepairChain.cs |
| BakeryStagedProductionChain | Domains/Economy/Businesses/Bakery/BakeryStagedProductionChain.cs | Domains/Economy/Businesses/Bakery | (none) |
| ForgeRepairChain | Domains/Economy/Businesses/Blacksmith/ForgeRepairChain.cs | Domains/Economy/Businesses/Blacksmith | (none) |
| StoreRestockService | Domains/Economy/Businesses/GeneralStore/StoreProcurementChain.cs | Domains/Economy/Businesses/GeneralStore | Orchestration/HouseholdSlice/HouseholdVerticalSlicePhaseH.cs |

## Notes per entry

- The twelve `*Runtime` entries are the detailed per-business models
  (roughly 32k lines combined). Each ships with its own feature helpers in
  the same directory. None is invoked by `SimulationDrivers`,
  `SharedBusinessRuntimeManager`, or any other production system.
- `StagedProductionProcess` and `ProductionDueEventDriver` are the generic
  staged-production machinery. Their only production-tree references are the
  two chains below, which are themselves unwired, so the references are
  intra-subsystem and allowlisted.
- `BakeryStagedProductionChain` and `ForgeRepairChain` are the concrete
  staged-production chains. Covered by editor tests only.
- `StoreRestockService` is instantiated only by the Phase H vertical-slice
  test fixture (`HouseholdVerticalSlicePhaseH.cs`), which is allowlisted.
  No production restock path calls it.

## Change log

- 2026-10-13: registry created (Pass VIII self-directed fixes, G-09/G-10/G-12).
  Seventeen entries. No rewiring, no deletions.
