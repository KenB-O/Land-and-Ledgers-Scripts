# Land & Ledgers — Dependency Rules and Architectural Boundaries

This document defines the intended dependency flow and boundary rules across the codebase. Agents must respect these constraints when making modifications.

---

## 1. Intended Layer Responsibilities & Flow

```
┌────────────────────────────────────────────────────────┐
│                   Presentation Layer                   │
│   (Presentation/UI, Presentation/Camera, Audio, Views) │
└──────────────────────────┬─────────────────────────────┘
                           │ reads projections / dispatches commands
                           ▼
┌────────────────────────────────────────────────────────┐
│                   ReadModels Layer                     │
│         (ReadModels/Reporting, Ledgers, Summaries)     │
└──────────────────────────▲─────────────────────────────┘
                           │ derives from
┌──────────────────────────┴─────────────────────────────┐
│                   Orchestration Layer                  │
│             (Bootstrapping, Guidance, Slices)          │
└──────────────────────────┬─────────────────────────────┘
                           │ coordinates
                           ▼
┌────────────────────────────────────────────────────────┐
│                     Domains Layer                      │
│     (Economy, World, Population, Civic, Reputation)    │
└──────────────────────────┬─────────────────────────────┘
                           │ uses foundational primitives
                           ▼
┌────────────────────────────────────────────────────────┐
│                      Core Layer                        │
│               (Core/Time, Tick Contexts)               │
└────────────────────────────────────────────────────────┘
```
*(Infrastructure/Persistence orthogonally serializes and restores Domain state)*

### Layer Rules:
1. **Presentation Layer**:
   - May observe domain state, read ReadModels, and invoke approved domain command methods.
   - **Must NEVER own simulation truth**, hold business logic, or calculate economic ledger balances.
   - **Feature-Specific Presentation**: Lives alongside the owning feature in its domain (e.g., `Domains/Economy/Businesses/GeneralStore/Presentation/`, `Domains/Economy/Acquisitions/Presentation/`).
   - **Generic Presentation**: Truly shared UI components, HUD scaffolding, cameras, and audio live under `Presentation/`.

2. **ReadModels Layer**:
   - Derives analytical summaries, weekly ledger reports, and immutable projection snapshots.
   - **Must NEVER mutate domain simulation state** or act as an authority for business logic.

3. **Orchestration Layer**:
   - Sequences domain initialization, boots scene graphs, and manages player tutorial guidance.
   - **Must NOT duplicate domain-owned state** or bypass domain validation rules.

4. **Domains Layer**:
   - Owns and encapsulates simulation rules, entity lifecycles, and domain truth for its area.
   - Feature-specific business logic stays strictly within its feature directory (e.g., General Store logic in `Domains/Economy/Businesses/GeneralStore/`).
   - Physical pathfinding belongs under `Domains/World/Pathing/`.
   - Economic shipment and freight execution belongs under `Domains/Economy/Logistics/`.

5. **Infrastructure / Persistence Layer**:
   - Serializes domain state to disk and reconstructs state during load routines.
   - Resides under `Infrastructure/Persistence/`.
   - **Must NOT redefine domain simulation behaviors** or introduce alternate business calculations.

6. **Core Layer**:
   - Contains only foundational, cross-domain primitives (e.g., `Core/Time`).
   - **Avoid Generic Junk Drawers**: Do not introduce broad `Common/`, `Shared/`, or `Misc/` folders containing disconnected utility scripts.

7. **Tests**:
   - Automated tests retain domain identity and reside under `Tests/Editor/<Domain>`.

8. **Editor Tooling**:
   - Editor utilities, custom inspectors, and prefab composing tools must live in an `Editor/` folder nested within their owning feature or domain.

---

## 2. Scope Boundaries of Current Migration
The physical migration to the AI-native directory structure was strictly an organizational relocation. It intentionally did **NOT**:
- Alter existing C# namespaces.
- Introduce Assembly Definition Files (`.asmdef`).
- Introduce an event-bus or messaging infrastructure.
- Decompose or split giant monolithic authority files.
- Resolve legacy circular dependency cycles.
- Rename serialized types or fields.
- Redesign or rebalance gameplay systems.

*Giant-file decomposition and modularization are scheduled for later dedicated workstreams.*

---

## 3. Known Dependency Debt
A prior structural audit identified multiple tight couplings and cyclical relationships across existing managers. Be aware of these existing debts:

1. **`Economy <-> World`**:
   - `SharedBusinessRuntimeManager` and `AcquisitionMarketManager` directly query and hold references to `TownWorldController`, parcel plots, and tile grid data.
2. **`Economy <-> Population`**:
   - `SharedBusinessRuntimeManager`, `WorkerRoleFitEvaluator`, and `OpportunityPressureRuntimeManager` directly inspect `PopulationManager` arrays, household reserves, and demographic traits.
3. **`Economy <-> former first-slice / Orchestration`**:
   - Domain managers maintain back-references to `FirstLedgerSliceBootstrapper` or expect initialization orchestration directly in `Awake`/`Start`.
4. **`Persistence <-> Domain Simulations`**:
   - `SaveLoadManager` directly sets internal fields on domain managers during state restore rather than utilizing decoupled memento interfaces.
5. **`Simulation -> UI Direct Invocations`**:
   - Several simulation managers directly trigger visual feedback (e.g., `PlayerPortfolioManager` invoking `LandLedgers.UI.LLFeedbackService`).
6. **`Valuation <-> DealTerms`**:
   - Deal term evaluators and offer calculation engines cross-reference models bidirectionally.
7. **`Economy <-> Financing`**:
   - Debt repayment loops in `PlayerDebtManager` directly modify cash accounts in `PlayerPortfolioManager` and business records.

### Policy on Existing Dependency Debt:
> [!IMPORTANT]
> The items above are **documented legacy debt**. They are **NOT permission** to perform unprompted refactoring, rewrite systems, or introduce new abstractions during routine feature work. Work within existing contracts unless an explicit refactoring task is assigned.

---

## 4. Assembly Definition Files (`.asmdef`) Status

**NO ASMDEFS YET.**

Because circular references currently exist across domain boundaries (such as `Economy <-> World` and `Economy <-> Population`), introducing Assembly Definition Files at this stage would cause compilation failures. Assembly definition boundaries are deferred until dependency cycles are untangled in a future architecture workstream.
