# Land & Ledgers — Authority Map

This document establishes the authoritative owning scripts for core simulation and gameplay concepts as they physically exist in the codebase today.

Do not assume a future planned system exists until it is verified on disk. If a concept is planned but has no implementing class, it is marked **NOT YET IMPLEMENTED**.

---

## 1. Monolithic / Giant Authority Files
The following files represent major centralized authorities with high code volume. Exercise extreme care when making targeted modifications to these scripts:

| Script Name | Location | Line Count | Notes |
| :--- | :--- | :--- | :--- |
| **`TownWorldController.cs`** | `Domains/World/` | 12,500 | Spatial grid, terrain, plot subdivision, visual generation, selection. |
| **`AcquisitionMarketManager.cs`** | `Domains/Economy/Acquisitions/` | 12,278 | Due diligence, parcel/business listings, negotiation, closing. |
| **`SharedBusinessRuntimeManager.cs`** | `Domains/Economy/Core/` | 8,332 | Passive/active business simulation, payroll, inventory, commodity flow. |
| *(partial) `...BusinessReadouts.cs`* | `Domains/Economy/Core/` | 1,123 | Partial class: financial snapshot calculations for business readouts. |
| *(partial) `...ArchetypeRuntimes.cs`*| `Domains/Economy/Core/` | 300 | Partial class: archetype-specific business execution routines. |
| **`GeneralStoreRuntimeManager.cs`** | `Domains/Economy/Businesses/GeneralStore/` | 4,752 | Player retail store shelves, customer shopping checkout, retail stocking. |
| **`PlayerDebtManager.cs`** | `Domains/Economy/Financing/` | 2,732 | Bank loan issuance, payment schedules, default handling, debt ledger. |
| **`BusinessRuntimeState.cs`** | `Domains/Economy/Core/` | 2,209 | Core state container for active/passive business instances. |
| **`SaveLoadManager.cs`** | `Infrastructure/Persistence/` | 2,146 | Central save/load state orchestrator and DTO mapping. |
| **`PopulationManager.cs`** | `Domains/Population/` | 1,964 | Demographic simulation, citizen lifecycles, household state stepping. |
| **`OpportunityPressureRuntimeManager.cs`** | `Domains/Economy/Markets/` | 1,877 | Economic supply/demand imbalance tracking and opportunity flags. |
| **`PlayerPortfolioManager.cs`** | `Domains/Economy/Finance/` | 1,815 | Player treasury cash, business dividend distributions, personal ledger. |
| **`LogisticsRuntimeManager.cs`** | `Domains/Economy/Logistics/` | 1,706 | Freight transport dispatch, carrier capacity, trade route execution. |
| **`BusinessInstanceState.cs`** | `Domains/Economy/Core/` | 1,204 | Serializable runtime state for an individual business establishment. |

---

## 2. Concept Authority Mapping

### Time / Calendar Authority
- **CONCEPT**: Simulation clock, game calendar date, game speed scaling, and tick stepping.
- **CURRENT AUTHORITY**: `TimeManager`
- **LOCATION**: `Core/Time/TimeManager.cs`
- **NOTES / KNOWN LIMITATIONS**: Drives all time-based simulation via event broadcasts and tick contexts. Fixed timestep and calendar conversions (`SimulationDate`) are centralized here.

---

### World / Town Authority
- **CONCEPT**: Town tile grid, parcel/lot subdivision, plot boundary registration, terrain mesh generation, and object selection.
- **CURRENT AUTHORITY**: `TownWorldController`
- **LOCATION**: `Domains/World/TownWorldController.cs`
- **NOTES / KNOWN LIMITATIONS**: Monolithic god class (~12.5k lines). Blends procedural grid generation, building placement, anchor markers, and direct visual rendering logic.

---

### Regional World Authority
- **CONCEPT**: Macro-level territory boundaries, regional resource deposits, and macro parcel registry.
- **CURRENT AUTHORITY**: `RegionalWorldFoundation` & `RegionalParcelAuthority`
- **LOCATION**: `Domains/World/Regional/RegionalWorldFoundation.cs`, `Domains/World/Regional/RegionalParcelAuthority.cs`
- **NOTES / KNOWN LIMITATIONS**: Provides regional context outside the immediate town boundary; works in tandem with `RegionalResourceScaffold.cs`.

---

### Pathing Authority
- **CONCEPT**: Spatial pathfinding, grid connectivity, road network navigation, and agent movement.
- **CURRENT AUTHORITY**: `PathingManager`
- **LOCATION**: `Domains/World/Pathing/PathingManager.cs`
- **NOTES / KNOWN LIMITATIONS**: Owns A* path calculations and coordinates with `AgentMover.cs` and `LogisticsRoutePlanner.cs`. Pure spatial navigation without economic logic.

---

### Population Authority
- **CONCEPT**: Town population counts, household formation, person state, worker traits, and settlement planning.
- **CURRENT AUTHORITY**: `PopulationManager`
- **LOCATION**: `Domains/Population/PopulationManager.cs`
- **NOTES / KNOWN LIMITATIONS**: Manages demographic state (`PopulationState`), household reserve tracking, and immigrant settlement (`NewcomerSettlementPlanner.cs`). Direct coupling to `TownWorldController` for household residence anchors.

---

### Business Runtime Authority
- **CONCEPT**: Core business simulation, operating expense deduction, commodity production, employee staffing, and business solvency.
- **CURRENT AUTHORITY**: `SharedBusinessRuntimeManager` (supplemented by `BusinessRuntimeState`)
- **LOCATION**: `Domains/Economy/Core/SharedBusinessRuntimeManager.cs`
- **NOTES / KNOWN LIMITATIONS**: Monolithic manager (~8.3k lines). Handles both passive AI businesses and player enterprise operations. Tightly coupled with world parcels, market pressure, and population labor.

---

### General Store Authority
- **CONCEPT**: Specific gameplay simulation for the player-managed retail General Store (shelf slots, stocking, local customer transactions).
- **CURRENT AUTHORITY**: `GeneralStoreRuntimeManager`
- **LOCATION**: `Domains/Economy/Businesses/GeneralStore/GeneralStoreRuntimeManager.cs`
- **NOTES / KNOWN LIMITATIONS**: Large dedicated manager (~4.8k lines) governing granular retail mechanics distinct from generic wholesale businesses.

---

### Owner / Portfolio Cash Authority
- **CONCEPT**: Player's personal liquid funds, owner equity, dividend withdrawals, and capital injection into businesses.
- **CURRENT AUTHORITY**: `PlayerPortfolioManager`
- **LOCATION**: `Domains/Economy/Finance/PlayerPortfolioManager.cs`
- **NOTES / KNOWN LIMITATIONS**: Manages owner distributions and tracks cash balances. Currently holds direct references to UI feedback notifications.

---

### Debt / Financing Authority
- **CONCEPT**: Bank credit facilities, loan amortization schedules, interest accrual, debt servicing, and debt restructuring.
- **CURRENT AUTHORITY**: `PlayerDebtManager`
- **LOCATION**: `Domains/Economy/Financing/PlayerDebtManager.cs`
- **NOTES / KNOWN LIMITATIONS**: Manages loan contracts (`BankLoanModels.cs`) and weekly debt payments. Contains dedicated reflection and integration hooks for portfolio deductions.

---

### Acquisition Authority
- **CONCEPT**: Commercial property listings, business acquisitions, property diligence, valuation bidding, and ownership closing.
- **CURRENT AUTHORITY**: `AcquisitionMarketManager`
- **LOCATION**: `Domains/Economy/Acquisitions/AcquisitionMarketManager.cs`
- **NOTES / KNOWN LIMITATIONS**: Giant authority (~12.3k lines). Integrates deal terms (`Domains/Economy/DealTerms/`), rival bidding (`Domains/Economy/Rivals/`), and valuation algorithms (`Domains/Economy/Valuation/`).

---

### Logistics / Shipment Authority
- **CONCEPT**: Freight hauling contracts, recurring supply orders, transport vehicle dispatch, and carrier loading/unloading.
- **CURRENT AUTHORITY**: `LogisticsRuntimeManager` & `LocalRecurringOrderManager`
- **LOCATION**: `Domains/Economy/Logistics/LogisticsRuntimeManager.cs`, `Domains/Economy/Logistics/LocalRecurringOrderManager.cs`
- **NOTES / KNOWN LIMITATIONS**: Owns economic shipment state, dispatch queues, and supplier relationships (`SupplierTrustEvaluator.cs`). Relies on `Domains/World/Pathing` for vehicle movement. Known defect: P0 physical shipment/inventory conservation bug is currently targeted for later work.

---

### Market Opportunity / Pressure Authority
- **CONCEPT**: Local market demand curves, commodity deficits, service shortages, household consumer preferences, and town economic pulse.
- **CURRENT AUTHORITY**: `OpportunityPressureRuntimeManager` & `TownPulseRuntimeManager`
- **LOCATION**: `Domains/Economy/Markets/OpportunityPressureRuntimeManager.cs`, `Domains/Economy/Markets/TownPulseRuntimeManager.cs`
- **NOTES / KNOWN LIMITATIONS**: Evaluates consumer needs (`HouseholdAffinityEvaluator.cs`) and flags market opportunities to drive rival investments and player recommendations.

---

### Persistence / Save-Load Authority
- **CONCEPT**: Disk serialization, save game slot management, DTO conversions, and scene reference reconstruction.
- **CURRENT AUTHORITY**: `SaveLoadManager`
- **LOCATION**: `Infrastructure/Persistence/SaveLoadManager.cs`
- **NOTES / KNOWN LIMITATIONS**: Centralized JSON-based save coordinator (~2.1k lines). Directly accesses and restores internal state across all major domain managers.

---

### Reporting / Read Models Authority
- **CONCEPT**: Financial ledgers, period-based profit/loss summaries, portfolio analytics, and balance sheet reporting.
- **CURRENT AUTHORITY**: `BusinessReportBuilder` & `PortfolioReportBuilder`
- **LOCATION**: `ReadModels/Reporting/BusinessReportBuilder.cs`, `ReadModels/Reporting/PortfolioReportBuilder.cs`
- **NOTES / KNOWN LIMITATIONS**: Pure read-only builders. Derive report summaries from state snapshots; do not mutate simulation state.

---

### UI / HUD Presentation Authority
- **CONCEPT**: Screen-space canvas management, top HUD bar, time controls display, alert feeds, and management panel host.
- **CURRENT AUTHORITY**: `LandLedgersHUDController` & `ManagementPanelView`
- **LOCATION**: `Presentation/UI/LandLedgersHUDController.cs`, `Presentation/UI/ManagementPanelView.cs`
- **NOTES / KNOWN LIMITATIONS**: Top-level UI coordinators. Feature-specific views (e.g., General Store shelf view, Acquisition dialogs) delegate through their own domain presentation controllers.

---

### Absent / Future Authorities
The following concepts are documented in game design or technical bibles but are **NOT YET IMPLEMENTED** in current production code:
- **Commercial Bank Corporate Entity**: Currently handled procedurally through `PlayerDebtManager`; no autonomous bank AI or bank balance sheet simulation exists.
- **Inter-Town Rail & Maritime Freight**: Inter-town logistics is currently handled via abstract off-map approach vectors in `LogisticsRuntimeManager`; full physical rail/maritime freight networks are NOT YET IMPLEMENTED.
- **Municipal Taxation & Zoning Authority**: `CivicFoundationManager` tracks town hall definitions, but active tax collection engines and dynamic municipal zoning are NOT YET IMPLEMENTED.
- **Decoupled Event Bus Infrastructure**: No centralized event broker or message bus exists; current systems use direct C# events, Unity events, or direct manager invocations.
- **Assembly Definition Architecture (`.asmdef`)**: NOT YET IMPLEMENTED. The codebase compiles into default `Assembly-CSharp.dll`.
