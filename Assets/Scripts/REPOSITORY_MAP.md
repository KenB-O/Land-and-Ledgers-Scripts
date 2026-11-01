# Land & Ledgers — Repository Map

This map describes the actual physical layout of production scripts and tests across `Assets/Scripts/`.

---

## 1. Top-Level Structure Overview

- **`Core/`**: Universal game-loop primitives and fundamental cross-cutting concerns (e.g., simulation time, calendar date).
- **`Domains/`**: Simulation state, business rules, and domain-specific logic partitioned by game subdomain.
- **`Infrastructure/`**: Low-level platform systems, disk I/O, file serialization, and save/load persistence.
- **`Orchestration/`**: Higher-level coordinator services that bootstrap the game, sequence domain startup, or guide the player.
- **`Presentation/`**: View layer, user interaction, cameras, feedback overlays, and generic UI components.
- **`ReadModels/`**: Derived, immutable projections, reporting engines, and analytical ledger builders.
- **`Tests/`**: Editor test suites validating domain mechanics, regressions, and integrations.

---

## 2. Core Subtrees

### `Core/Time/`
- **Responsibility**: Authoritative simulation clock, game date tracking, time-scale stepping, and tick context.
- **Representative Files**:
  - `TimeManager.cs`: Primary MonoBehaviour controlling the tick loop and time scaling.
  - `SimulationDate.cs`: Struct representing calendar day, month, year, and tick conversions.
  - `SimulationTickContext.cs`: Execution context passed down during simulation steps.
  - `SimulationSpeed.cs`, `SimulationTimeSettings.cs`: Playback rate presets and configurations.
- **Caveats**: Central time driver; many domains rely on its tick events or date structures directly.

---

## 3. Domains Subtrees

### `Domains/Economy/`
Houses all commercial, financial, market, and business systems.

- **`Core/`**:
  - **Responsibility**: Foundational business entities, runtime instances, shared business logic, and employment role evaluation.
  - **Representative Files**: `SharedBusinessRuntimeManager.cs` (and partials `.ArchetypeRuntimes.cs`, `.BusinessReadouts.cs`), `BusinessRuntimeState.cs`, `BusinessInstanceState.cs`, `BusinessProfileDefinition.cs`, `BusinessDefinitions.cs`, `BusinessDelegationPolicy.cs`, `BusinessTransferAgreementModels.cs`, `RetailDefinitions.cs`, `WorkerRoleFitEvaluator.cs`.
  - **Caveats**: `SharedBusinessRuntimeManager.cs` is a massive monolithic authority (~8.3k lines) that manages passive and active business lifecycle.
- **`Finance/`**:
  - **Responsibility**: Player portfolio cash, personal treasury distributions, and owner equity tracking.
  - **Representative Files**: `PlayerPortfolioManager.cs`.
- **`Financing/`**:
  - **Responsibility**: Commercial bank loans, credit limits, interest calculations, debt repayment schedules, and loan default penalties.
  - **Representative Files**: `PlayerDebtManager.cs`, `BankLoanModels.cs`, `FinancingEvaluator.cs`, `FinancingFailureEvaluator.cs`, `PaymentScheduleBuilder.cs`.
- **`Acquisitions/`**:
  - **Responsibility**: Buying/selling parcels and businesses, valuation negotiation, due diligence, and seller interactions.
  - **Representative Files**: `AcquisitionMarketManager.cs`, `ExpansionReadinessService.cs`, `AcquisitionConversationModels.cs`.
  - **Presentation**: `Acquisitions/Presentation/` (`AcquisitionConversationPresenter.cs`, `AcquisitionSellerMeetingView.cs`, `OwnershipWorkflowView.cs`, `PropertyPreviewCameraRig.cs`).
  - **Workflows**: `Acquisitions/Workflows/` (`AcquisitionSellerMeetingController.cs`, `OwnershipWorkflowController.cs`).
  - **Caveats**: `AcquisitionMarketManager.cs` is a giant authority file (~12.3k lines).
- **`Markets/`**:
  - **Responsibility**: Supply/demand pressure, household consumer affinity, neighborhood market signals, and town economic pulse.
  - **Representative Files**: `OpportunityPressureRuntimeManager.cs`, `TownPulseRuntimeManager.cs`, `HouseholdAffinityEvaluator.cs`, `HouseholdAffinityModels.cs`.
- **`Logistics/`**:
  - **Responsibility**: Freight transportation execution, trade route dispatch, recurring merchant orders, and supplier trust evaluation.
  - **Representative Files**: `LogisticsRuntimeManager.cs`, `LocalRecurringOrderManager.cs`, `LocalRecurringOrderModels.cs`, `LogisticsModels.cs`, `SupplierTrustEvaluator.cs`, `SupplierTrustModels.cs`, `LogisticsTransportPresentationController.cs`, `LogisticsTransportVisualController.cs`.
  - **Caveats**: Economic shipment tracking is handled here; physical vehicle navigation interfaces with `Domains/World/Pathing`.
- **`Valuation/`**:
  - **Responsibility**: Pricing calculations, buyer-seller fit models, parcel appraisals, and acquisition offers.
  - **Representative Files**: `OfferEvaluationEngine.cs`, `ParcelValuationEvaluator.cs`, `SellerPressureEvaluator.cs`, `BuyerSellerFitEvaluator.cs`, `ValuationModels.cs`, `OfferModels.cs`.
- **`DealTerms/`**:
  - **Responsibility**: Non-price deal term evaluation, contingencies, earnouts, and transaction modifiers.
  - **Representative Files**: `ExpandedDealTermEvaluator.cs`, `ExpandedDealTermAdapters.cs`, `ExpandedDealTermModels.cs`.
- **`Rivals/`**:
  - **Responsibility**: Competitive AI bids, rival merchant scale evaluation, and market expansion pressure.
  - **Representative Files**: `MarketPressureEvaluator.cs`, `RivalBidPressureEvaluator.cs`, `RivalOwnerDecisionEvaluator.cs`, `RivalOwnerScaleEvaluator.cs`, `RivalModels.cs`.
- **`Businesses/GeneralStore/`**:
  - **Responsibility**: Specific runtime simulation, shelf inventory, and consumer transactions for the player's flagship General Store.
  - **Representative Files**: `GeneralStoreRuntimeManager.cs`, `GeneralStoreBusinessDefinition.cs`, `Presentation/GeneralStorePanelController.cs`.
  - **Caveats**: High line-count runtime manager (~4.8k lines). Feature-specific UI lives in its `Presentation/` subfolder.
- **`Businesses/Mine/`**:
  - **Responsibility**: Mine extraction definitions, shaft tiers, and commodity models.
  - **Representative Files**: `MineRuntimeModels.cs`.
- **`Businesses/Construction/`**:
  - **Responsibility**: Construction support nodes, material supply staging, and contractor inputs.
  - **Representative Files**: `ConstructionSupportNodeDefinition.cs`.

### `Domains/World/`
Houses all spatial, grid, terrain, procedural generation, and navigation systems.

- **Central World**:
  - **Responsibility**: Grid cell management, parcel subdivision, terrain procedural initialization, and world interaction hit-testing.
  - **Representative Files**: `TownWorldController.cs`, `TownGrid.cs`, `TownPlot.cs`, `TownCell.cs`, `TownSiteSelector.cs`, `TownGenerationSettings.cs`, `WorldInteractionController.cs`, `WorldSeedAuthority.cs`, `WorldSurfaceProvider.cs`, `PreAuthoredTerrainWorldProfile.cs`.
  - **Caveats**: `TownWorldController.cs` is the single largest monolithic authority in the project (~12.5k lines).
- **`Buildings/`**:
  - **Responsibility**: Modular building definitions, footprint authority, exterior markers, and placement logic.
  - **Representative Files**: `BuildingDefinition.cs`, `BuildingFootprintAuthority.cs`, `PlacedBuilding.cs`, `LLModularBuildingPiece.cs`, `BuildingAnchorMarker.cs`, `BuildingDoorAnimator.cs`, `BuildingExteriorPropAuthoring.cs`.
  - **Editor**: `Buildings/Editor/` (`LLBuildingAutoComposer.cs`, `LLModularBuildingPieceFactoryWindow.cs`).
- **`Regional/`**:
  - **Responsibility**: Macro-scale regional world foundation, regional resource nodes, parcel authority, and regional map overlays.
  - **Representative Files**: `RegionalWorldFoundation.cs`, `RegionalParcelAuthority.cs`, `RegionalResourceScaffold.cs`, `RegionalTerrainTileView.cs`, `RegionalWorldDebugOverlay.cs`.
- **`Pathing/`**:
  - **Responsibility**: A* grid pathfinding, road-network routing, waypoint traversal, and agent motion execution.
  - **Representative Files**: `PathingManager.cs`, `AgentMover.cs`, `LogisticsRoutePlanner.cs`, `PathingSettings.cs`, `PathTypes.cs`, `LogisticsRouteTypes.cs`.
  - **Caveats**: Pure physical pathfinding. Interfaced by Population movers and Logistics transports.

### `Domains/Population/`
- **Responsibility**: Town demographic simulation, household finances/reserves, resident lifecycle, job assignments, newcomer immigration, and health.
- **Representative Files**: `PopulationManager.cs`, `PopulationState.cs`, `HouseholdState.cs`, `PersonState.cs`, `HouseholdReserves.cs`, `HouseholdUpgrades.cs`, `NewcomerSettlementPlanner.cs`, `PopulationPathingDirector.cs`, `ProfessionDefinition.cs`, `WorkerTraits.cs`.
- **Caveats**: `PopulationManager.cs` (~2.0k lines) manages generation and simulation stepping for all citizens.

### `Domains/Civic/`
- **Responsibility**: Town civic buildings (Town Hall, Schoolhouse), municipal policies, and public service infrastructure.
- **Representative Files**: `CivicFoundationManager.cs`, `TownHallDefinition.cs`, `TownHallModels.cs`, `SchoolhouseModels.cs`.

### `Domains/Reputation/`
- **Responsibility**: Player and business reputation standing, public trust tracking, negotiation aptitude, and reputation event processing.
- **Representative Files**: `PlayerReputationState.cs`, `BusinessReputationState.cs`, `ReputationEvaluator.cs`, `BusinessReputationEvaluator.cs`, `NegotiationAptitudeEvaluator.cs`, `ReputationEventApplier.cs`.

### `Domains/Progression/`
- **Responsibility**: Player ownership mastery, aptitude progression, and capability unlocking.
- **Representative Files**: `OwnershipAptitudeEvaluator.cs`, `OwnershipAptitudeState.cs`.

---

## 4. Orchestration Subtrees

### `Orchestration/Bootstrap/`
- **Responsibility**: Game entry point and startup coordinator across world generation, population seeding, business initialization, and UI binding.
- **Representative Files**: `FirstLedgerSliceBootstrapper.cs`..

### `Orchestration/Guidance/`
- **Responsibility**: Tutorial sequences, first-session player guidance milestones, and contextual hints.
- **Representative Files**: `FirstSessionGuidanceManager.cs`, `Presentation/FirstSessionGuidanceText.cs`.

---

## 5. Presentation Subtrees

### `Presentation/UI/`
- **Responsibility**: Top-level HUD bar, financial readouts, alert strips, floating money feedback, tooltip system, and management views.
- **Representative Files**: `LandLedgersHUDController.cs`, `ManagementPanelView.cs`, `LLFeedbackService.cs`, `LLMoneyFeedbackOverlay.cs`, `UITooltipController.cs`, `UITooltipRegistry.cs`, `LandLedgersTypography.cs`, `ScrollRectInputRelay.cs`.
- **Editor**: `Presentation/UI/Editor/` (`LandLedgersHUDSetupUtility.cs`, `ManagementPanelPrefabRepairUtility.cs`).

### `Presentation/Camera/`
- **Responsibility**: Strategy camera movement, pan/zoom bounds, orthographic/perspective framing, and input handling.
- **Representative Files**: `StrategyCameraController.cs`, `StrategyCameraBounds.cs`, `StrategyCameraSettings.cs`.

### `Presentation/Audio/`
- **Responsibility**: Background soundtrack playback and audio ambiance.
- **Representative Files**: `LLMainMusicPlayer.cs`.

---

## 6. Infrastructure Subtrees

### `Infrastructure/Persistence/`
- **Responsibility**: JSON serialization/deserialization, game state save/load orchestration, save slot schemas, and entity reference reconstruction.
- **Representative Files**: `SaveLoadManager.cs`, `SaveDtos.cs`, `RegionalWorldSaveDtos.cs`, `SaveReferenceResolver.cs`.
- **Caveats**: `SaveLoadManager.cs` (~2.1k lines) currently interacts directly with multiple domain managers during save/load cycles.

---

## 7. ReadModels Subtrees

### `ReadModels/Reporting/`
- **Responsibility**: Read-only business ledgers, period-based profit & loss summaries, portfolio financial health readouts, and analytical projections.
- **Representative Files**: `BusinessReportBuilder.cs`, `PortfolioReportBuilder.cs`, `ReportDtos.cs`, `ReportSnapshots.cs`, `LedgerEntry.cs`, `ReportPeriod.cs`.
- **Caveats**: Non-mutating read-only builders consumed by UI and management views.

---

## 8. Tests Subtree

### `Tests/Editor/`
- **Responsibility**: Automated NUnit/Unity test suites covering domain calculations, persistence roundtrips, and integration smoke tests.
- **Directory Layout**: Organized by owning domain:
  - `Civic/`
  - `DealTerms/`
  - `Economy/`
  - `Financing/`
  - `Pathing/`
  - `Population/`
  - `Progression/`
  - `Reputation/`
  - `Rivals/`
  - `Time/`
  - `UI/`
  - `Valuation/`
  - `World/`
