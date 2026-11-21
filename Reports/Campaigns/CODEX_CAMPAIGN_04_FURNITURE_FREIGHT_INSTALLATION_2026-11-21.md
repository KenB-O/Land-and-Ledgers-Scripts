# Codex Campaign 04 — Furniture, Freight, Production Persistence and Installation

Date: 2026-11-21  
Branch: `codex/muse-unity-integration-2026-11-21`  
Starting HEAD: `b822455a645506b21a3b0f72a1bc5edaf813df64`  
Unity verification: **not run**. Unity 6000.4.2f1 is open on the owner's separate `F:\Land & Ledgers` checkout, with its import workers active. This pass did not open another Editor or interrupt the owner's session.

## Source repairs in this pass

- `FurnishingInstallTaskDriver` now emits one durable-order placement-blocker diagnostic per unchanged reason instead of repeating the same incident on each daily sweep. The layout is still re-evaluated, so adding a legitimate authored placement allows recovery. Once a slot is valid, the stale blocker clears before the real install task is minted. The item remains in household custody until the task actually completes.
- Strengthened `Lane9fProductionRestoreTests.RestoreProcessesForBusiness_RestoresAndAdvancesToCompletion` to round-trip through `BusinessInstanceState.CaptureSaveDto`, Unity JSON serialization, and `BusinessInstanceState.FromSaveDto`. It now checks process identity, current stage and remaining work, consumed input identifiers and provenance lots, and real remaining material quantities before task rebinding and completion. The existing manual field-copy fixture could pass even if the production save DTO omitted a field.
- Added a furnishing regression for repeated blocked sweeps and recovery after a valid authored placement appears. It asserts the diagnostic is emitted once, no install task is minted while blocked, the furniture stays in custody, and a later valid placement mints a task without installing the asset as a side effect.

## Existing lifecycle verified by source inspection

- **Production persistence:** carpentry uses `ProductionProcessState` in the business's `GenericBusinessConfiguration`; `BusinessInstanceState.CaptureSaveDto` persists that configuration, including process ID, phase, remaining phase minutes, assigned person, reserved equipment/workspace, consumed input markers and lot provenance, completion, and `OutputCreated`. Business runtime restore rebinds the process. The material debit occurs at start and `ConsumeInputsOnce` prevents re-consumption. The output authority marks output created only after its callback succeeds. The integration path already has `Lane9fProductionRestoreTests`; the strengthened DTO-boundary case is unexecuted.
- **Staged furniture delivery:** the Main Scene `SimulationDrivers` call explicitly constructs `FurnitureStorePurchaseDriver` with staged delivery enabled and passes the durable Pass 9 order list plus the existing `FreightProductionDriver`. The constructor default is also staged; the legacy synchronous path requires an explicit `false`. G3 has a default-driver regression proving it refuses rather than silently falling back to synchronous delivery.
- **Stable order and custody identity:** Pass 9 stores purchase ID, shipment ID, household/building/vendor identity, line SKU, source lot, units, price, line stage, asset IDs, cancellation state, and installation blocker. Freight service state separately stores shipment status and physical cargo location. Household furnishing registers own received asset custody. Receipt reconstructs the runtime asset reference from durable asset IDs and the register instead of persisting duplicate asset objects in the order.
- **Task-backed installation:** delivered assets remain at `Received`; `FurnishingInstallTaskDriver` mints one `furnishing.install` task per eligible uninstalled asset, associates it with stable purchase/asset IDs, checks household-member availability and home arrival, books task work through `TaskAuthority`, and only then calls the authored-placement binding. Failed installation leaves custody intact; duplicate task minting and duplicate installation are guarded. Existing `Lane9fFurnishingInstallTests` cover task restoration, failed task recovery, blocked placement, and exactly-once order advancement.
- **Geometry-aware placement:** current `FurnitureInstallBinding` and `FurniturePlacementContext` use authored placement components and placement scoring. The existing placement tests cover clearances and scored candidate selection. No scene was edited in this pass.
- **Cancellation:** unpaid cancellation is supported without moving goods or money. Paid/shipped orders are explicitly rejected by the unpaid-cancellation path. Failed delivery records a terminal reason and does not create a household asset or invent a refund. This preserves payment and cargo records but does not resolve post-payment cargo disposition.
- **DEF-046:** the original `DoubleTimeline_ExistsButHasTwoProductionConsumers` assertion remains unchanged (expected three, current source may report seven). It was not altered or attributed to this campaign.

## Tests and checks

- `git diff --check` for the three changed source/test files: **passed** (no whitespace errors reported).
- Unity compilation: **not executed**.
- Unity Editor tests: **not executed**. The modified regression cases and existing G3/freight suites are not counted as passing until run against current Unity assemblies.
- Main Scene purchase-to-install PlayMode lifecycle: **not executed**.
- No furniture, freight, or placement claim is marked live-verified.

Relevant tests to run in Unity:

- `Lane9fProductionRestoreTests.RestoreProcessesForBusiness_RestoresAndAdvancesToCompletion` (expanded actual business DTO/JSON round-trip)
- `Lane9fFurnishingInstallTests.InstallTask_UnchangedPlacementBlockerIsReportedOnceAndRecoversWhenAuthoredSlotAppears` (new)
- The full `Lane9fProductionRestoreTests` and `Lane9fFurnishingInstallTests` fixtures
- `FurnitureStorePurchaseG3Tests.DefaultDriver_RefusesRatherThanInstallingSynchronously`
- `FurnitureStorePurchaseG3Tests.StagedOrder_IsPaidOnce_AndWaitsForCarrierOverGameTime`
- `FurnitureStorePurchaseG3Tests.Pass9SaveContinue_RestoresOrderShipmentAndExactlyOncePayment`
- `FurnitureStorePurchaseG3Tests.Pass9SaveContinue_AfterReceiptRetainsFurnitureAndInstallationBlocker`
- `FurnitureStorePurchaseG3Tests.FailedDelivery_IsTerminalInFreightAndDoesNotCreditHouseholdOrRefundSilently`
- `FreightServiceAgreementTests` and the relevant `FreightShipmentServiceTests`

## Remaining defects and decisions

1. **Physical approach during installation remains unproven and incomplete at source level.** The current driver gates work on presence at the home building and a non-blocking household task, but does not route the worker to a furniture-specific approach point. `BuildingEquipmentPlacement` has physical position and orientation, while building activity routing uses authored activity anchors; no existing install route binds those authorities together. The task therefore has task-level movement/labor authority and placement validation, but a worker's approach to the actual item is not demonstrated. Implement the route only through the established mover and a valid reachable interaction point, then prove it in Main Scene. Do not infer this from the existing unit test's `hasArrivedAtHome` predicate.
2. **Paid failed/cancelled freight disposition needs an owner rule.** Current behavior retains the paid obligation and cargo in the freight authority without automatic refund, seizure, destruction, or return trip. The required ruling is: when a paid furniture shipment cannot be delivered or its order is cancelled after loading, who retains legal title/custody, who pays for return or storage, and under what condition (if any) is the buyer refunded? The existing behavior is conservative and preserves evidence until that rule is resolved.
3. **End-to-end Main Scene installation, actual worker availability, physical freight and Save/Continue are unverified** because no safe Unity execution was available.
4. Existing freight and installation test suites have source coverage, but this pass did not demonstrate live arrivals, cargo transfer, task work, or installed furniture in the authored world.

## Changed files

- `Assets/Scripts/Domains/Economy/GenericBusiness/FurnishingInstallTaskDriver.cs`
- `Assets/Scripts/Tests/Editor/Economy/Lane9fFurnishingInstallTests.cs`
- `Assets/Scripts/Tests/Editor/Economy/Lane9fProductionRestoreTests.cs`
- `Reports/Campaigns/CODEX_CAMPAIGN_04_FURNITURE_FREIGHT_INSTALLATION_2026-11-21.md`

No commit was created. No prior owner-local or Campaign 02/03 changes were reset or overwritten.
