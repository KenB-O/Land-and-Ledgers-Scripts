# Muse Unity Verification Backlog — 2026-11-21

## Verification boundary

Integration source checkpoint assessed: `codex/muse-unity-integration-2026-11-21` at `878ed06e18247c92c5a00cf5abd041e47187ed5b` (Muse source ancestor `0fc97de07465df7364316b4f318c6f172ef6a5ef`). This record itself may be committed after that source checkpoint. Project version is Unity `6000.4.2f1`.

**Unity compile and tests are NOT RUN on the Muse integration source.** The only connected Editor is open on the owner's separate `F:\Land & Ledgers` checkout, branch `codex/player-economic-participation-2026-11-18`, HEAD `8537f52fb7dea263fa3ba62da6cbd4d6b5aefbf8`. It reports stopped/not-compiling and a clean active Main Scene, but loaded owner assets are marked dirty in memory. The integration worktree has no `Library` cache and would cold-import. Available physical memory at the last check was about 3.6 GB. The owner checkout has 2,667 status entries and 4 unpublished commits; it remains untouched. Do not open the integration project or close the owner Editor until a safe memory/asset checkpoint is confirmed.

The Unity CLI self-update to 1.0.1 did not complete because other Unity processes were running; the installed CLI remains 1.0.0-beta.10. Do not repeat the update attempt while Unity is open.

The observed live Editor Console (127 current warnings, 0 current errors) belongs to the Codex checkout, not the Muse source. It is not a Muse compilation result.

## Focused Editor test plan — all status NOT RUN

Run only after the integration worktree is safely open in Unity and current assemblies finish compiling. First confirm the named tests are present in Unity Test Runner. There is one PlayMode `.asmdef`; no separate Editor-test `.asmdef` appears in the Assets tree, so discoverability of the `Tests/Editor` fixtures must be confirmed by the actual runner.

1. `FreightServiceAgreementTests` — especially agreement terms, retries, settlement, cargo linkage, cancellation, and `SaveLoadPreservesEveryStatus`.
2. F7-2 pins in `SawmillToYardTransferDriverTests`:
   - `F7_ProductionTerms_WholesalePriceIs32Cents`
   - `F7_TransferAtProductionTerms_MovesWholeLotOnce_Settles32PerUnit` (40 units must settle 1,280 cents exactly once)
   - `F7_WeeklyCapQuantity_TransfersInFull` (80-unit boundary; currently source-verified only)
   - `S1_SaveRoundTrip_PreservesPlannedJob_FinishesThroughFreshDriver` (saved-transfer recovery remains outstanding)
3. `FurnitureStorePurchaseG3Tests.DefaultDriver_RefusesRatherThanInstallingSynchronously` plus staged delivery, delayed delivery, failed/cancelled delivery, no-placement custody, duplicate prevention, and both `Pass9SaveContinue_*` cases.
4. `DomesticLaborDriverTests.MissingFirewoodAnchor_WithStoveFallback_DoesNotParkTask` and `MissingFirewoodAnchor_ParkedTaskResumesWhenStoveFallbackAppears`.
5. Full `ExteriorTemperatureTests` and `HouseholdThermalModelTests` fixtures.
6. `HouseholdReserveInventoryTests`, including heating debit, fuel purchases, reserve conservation, and persistence.
7. `FuelAndStorageSyncG4Tests`, `HouseholdFuelCookingDriverB3Tests`, and `HouseholdStoveRegistryTests`.
8. Newly added regression `HouseholdReserveInventoryTests.HeatingAndThermalStateRoundTripsThroughPopulationSave` covers the new heat-retention/stove/fuel, home-area/storey, strain, pressure, use, and shortfall fields. **Source-added; execution pending.**
9. Incident, A-to-Z, business, diagnostic integration and relevant movement tests, after compile and test-discovery check.

Do not execute or reinterpret these two known pre-existing spillover failures until the owner resolves the decision request:

- `CrossSystemInterplayRuntimeTests.TownPulseUnmetHardwareDemandSpillsIntoBlacksmith` expects 3 units sold/transferred; current no-buyer rule produces 0.
- `CrossSystemInterplayRuntimeTests.TownPulseMeatDemandCanSpillIntoButcher` expects 2 units; current no-buyer rule produces 0.

Preserve Muse's historical Roslyn failure unchanged: `DEF-046 DoubleTimeline_ExistsButHasTwoProductionConsumers`, expected 3, observed 7. It predates Muse, is not a Unity result, and its assertion must not be weakened. A fresh source-only count in this integration checkout currently finds six production `.cs` files referencing `PreciseAbsoluteGameSeconds`: `TimeManager`, `AgentMover`, `WorldInteractionController`, `PopulationPathingDirector`, `SimulationDrivers`, and the Editor `NpcActivityMonitorWindow`. This differs from the archived Roslyn result of seven and is not a test execution. The test still asserts three; investigate the intended consumer contract in a current Unity run without weakening the historical assertion.

## Editor diagnostics — source inventory only

The following menu registrations were found under `Assets/Scripts/Editor/Diagnostics`; runtime usability and compilation remain unverified:

- `Land & Ledgers/Diagnostics/A-to-Z Audit` — `AtoZAuditWindow` / `AtoZAudit`
- `.../Business Operations Monitor` — `BusinessOperationsMonitorWindow` / `BusinessOperationsMonitor`
- `.../Economy Integrity Auditor` — `EconomyIntegrityAuditorWindow` / `EconomyIntegrityAudit`
- `.../Household Survival and Food Security Monitor` — `HouseholdSurvivalMonitorWindow` / `HouseholdSurvivalMonitor`
- `.../Incident Investigator` — `IncidentInvestigatorWindow` / `IncidentInvestigator`
- `.../NPC Activity Monitor` — `NpcActivityMonitorWindow` / `NpcActivityTraceRecorder` (existing window modified by Muse)
- `.../Regression Comparator` — `RegressionComparatorWindow` / `RegressionComparator`, `RunArtifactReader`
- `.../Save-Continue State Comparator` — `SaveContinueStateComparatorWindow` / `SaveContinueStateComparator`
- `.../Scenario Lab` — `ScenarioLabWindow` / `SurvivalAcceptanceScenario`, run artifacts
- `.../Simulation Health Control Center` — `SimulationHealthControlCenterWindow` / `SimulationHealthControlCenter`
- `.../Simulation Performance and Memory Monitor` — `SimulationPerformanceMonitorWindow` / `SimulationPerformanceMonitor`
- `.../Supply Chain Dependency Explorer` — `SupplyChainDependencyExplorerWindow` / `SupplyChainDependencyExplorer`
- `.../Task Lifecycle Inspector` — `TaskLifecycleInspectorWindow` / `TaskLifecycleInspector`
- `.../World Accessibility and Interaction Auditor` — `WorldAccessibilityAuditorWindow` / `WorldAccessibilityAuditor`

Most new windows have matching core diagnostic classes in `Assets/Scripts/Core/Diagnostics`. They are under Unity's predefined script folders; no new Editor assembly definition was introduced. Verify every menu opens after a successful Unity assembly reload, especially Incident Investigator, A-to-Z Audit, Scenario Lab, and Health Control Center.

## First live test gate — NOT RUN

After successful compile and focused tests, use a fresh Main Scene run at seed 1886, 10x, 5–10 simulated days. Record exact branch/SHA, full Console, daily meals, shopping, sales/payment/receipt, fuel and heating, worker activity, freight, task liveness, and Save initialization. Do not run the 60-day campaign until this startup and short-run gate passes. No PlayMode behavior is currently claimed.
