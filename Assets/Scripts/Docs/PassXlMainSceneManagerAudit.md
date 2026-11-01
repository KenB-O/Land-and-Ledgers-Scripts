# Pass XL Main Scene Manager Audit

Scope: full tree at `muse/passxl-slice-phase2` tip `e7da22`, re-checked at
`muse/passxl-slice-phase3` base. Method: scripted inventory of every
`.cs` file outside `Planning Sources`, classification of every
`MonoBehaviour` class, cross-check against `FirstLedgerSliceBootstrapper`
serialized fields, `PersistentRuntimeHierarchyAuthoring.AuthorMainScene`,
`ScenarioHudSceneAuthoring`, `TransportPrefabAuthoring`,
`PersonInspectionPanelAuthoring`, `FindAnyObjectByType` / `FindUnique`
callers, and production (non-test) references.

Counts, verified by script, not by hand:
- 1,251 `.cs` files outside `Planning Sources`.
- 61 `MonoBehaviour` classes.
- 12 `ScriptableObject` classes (definitions/assets, not scene managers).
- Everything else is plain C#: domain state, drivers, services, registries,
  authorities, read models, and tests. A file named `*Manager.cs` or
  `*Runtime.cs` is NOT automatically a GameObject. Most of them are
  hub-owned plain C# and must NOT be given a GameObject.

## The one rule that explains most "missing" scripts

Three GameObject managers own almost all plain-C# simulation:

- `SimulationSystemsHub` owns `GenericProductionRuntimeRegistry`,
  `HouseholdDecisionDriver`, `HouseholdAutonomyDriver`,
  `DomesticLaborDriver`, `NpcEntrepreneurshipDriver`, `ButcherRuntime`
  instances, `BoardingHouseShopRuntime` instances, and the task / work-time
  authorities. It does NOT need a GameObject per driver.
- `SimulationDrivers` owns `NpcBusinessLifecycleDriver`,
  `EstateLifecycleDriver`, `NpcConstructionDriver`,
  `FreightProductionDriver`, `PlayerTaskProgressDriver`, and binds
  `SharedBusinessRuntimeManager`.
- `SharedBusinessRuntimeManager` (itself a GameObject, see below) is the
  runtime home for business state, including `BakeryShopRuntime`,
  `BakeryStagedProductionChain`, fuel, lumber, mine, and other shop
  runtimes, which are plain C# and are created per business instance.

Consequence: Bakery, Fuel Dealer, Butcher, and the off-map procurement
service (`OffMapNamedSkuProcurementService`, plain C#) have NO GameObject
and need none. Their missing link is driver wiring, not scene assignment.
Creating a GameObject for them would be a new, wrong authority, not a fix.
The same applies to `PassXlSliceSaveCoordinator`, `PlayerNpcPlaytestPolicy`,
`FamilyTreeBuilder`, `ImmigrationConditions`, `RecruitmentConversation`,
and `OffMapSupplierCatalog` in this pass: plain C#, driven by a hub or by
a production driver, never by their own GameObject.

The Pass XIII audit (`Docs/Pass13_MainScene_Manager_Audit.md`) is still
the last Editor-confirmed Main Scene census: one `SimulationSystemsHub`,
one `SimulationDrivers`, one `ScenarioDirector`,
one `FirstLedgerSliceBootstrapper`, one `Time Manager`,
one `PopulationManager`, plus authored UI/world roots. This audit does
NOT replace it, because Main Scene (`.unity`) is outside this repo and
cannot be confirmed from Git alone. Every placement below that is not
created by `PersistentRuntimeHierarchyAuthoring` is marked CONFIRM:
Codex must open Main Scene and confirm that GameObject actually exists.

## A. Main Scene persistent managers — Game Systems root

Placement is the `PersistentRuntimeHierarchyAuthoring` path unless noted.
`Game Systems` is the scene root; `Persistent Authorities` is a group
under it.

| Script | GameObject | Evidence |
|---|---|---|
| `TimeManager` | Game Systems > Time Manager | Pass XIII census; bootstrapper `timeManager` field; 40 production reference files |
| `SimulationSystemsHub` | Game Systems > SimulationSystemsHub | Pass XIII census; bootstrapper `systemsHub` field |
| `SimulationDrivers` | Game Systems > SimulationDrivers | Pass XIII census; `treatPlayerAsNpc` serialized field; 26 production reference files |
| `ScenarioDirector` | Game Systems > ScenarioDirector | Pass XIII census; bootstrapper assigns `PlayerDirector` to it |
| `FirstLedgerSliceBootstrapper` | Game Systems > FirstLedgerSliceBootstrapper | Authoring wires 21 serialized fields into it; NOT created by authoring, CONFIRM it exists once |
| `PopulationManager` | Game Systems > PopulationManager | Pass XIII census; bootstrapper `populationManager` field |
| `TownWorldController` | World root > TownWorldController | Bootstrapper `townWorld` field (FindUnique); 34 production reference files |
| `PathingManager` | Game Systems > PathingManager | Bootstrapper `pathingManager` field (FindUnique) |
| `PreAuthoredTerrainWorldProfile` | World > PreAuthoredTerrainWorldProfile | Bootstrapper field (FindUnique); NOT authoring-created, CONFIRM |
| `SaveLoadManager` | Game Systems > Persistent Authorities > Simulation and Persistence > Save Load Manager | Authoring creates it |
| `PopulationPathingDirector` | Game Systems > Persistent Authorities > Simulation and Persistence > Population Pathing Director | Authoring creates it; authoring adds `PopulationAgentsRoot` child under it |
| `FirstSessionGuidanceManager` | Game Systems > Persistent Authorities > Simulation and Persistence > First Session Guidance | Authoring creates it |
| `CivicFoundationManager` | Game Systems > Persistent Authorities > Economy and Civic > Civic Foundation | Authoring creates it; Pass XIII duplicate-instance fix (`FindOrCreate` incl. inactive) must still hold, CONFIRM only one live instance |
| `SharedBusinessRuntimeManager` | Game Systems > Persistent Authorities > Economy and Civic > Shared Business Runtime | Authoring creates it; 35 production reference files |
| `LogisticsRuntimeManager` | Game Systems > Persistent Authorities > Economy and Civic > Logistics Runtime | Authoring creates it |
| `TownPulseRuntimeManager` | Game Systems > Persistent Authorities > Economy and Civic > Town Pulse Runtime | Authoring creates it |
| `OpportunityPressureRuntimeManager` | Game Systems > Persistent Authorities > Economy and Civic > Opportunity Pressure Runtime | Authoring creates it |
| `PlayerDebtManager` | Game Systems > Persistent Authorities > Economy and Civic > Player Debt Manager | Authoring creates it |
| `PlayerPortfolioManager` | Game Systems > Persistent Authorities > Economy and Civic > Player Portfolio Manager | Authoring creates it |
| `WorldInteractionController` | Game Systems > Persistent Authorities > Interaction > World Interaction Controller | Authoring creates it; owns person inspection panel (click a person, see known info, relations) |
| `OwnershipWorkflowController` | Game Systems > Persistent Authorities > Interaction > Ownership Workflow Controller | Authoring creates it |
| `AcquisitionSellerMeetingController` | Game Systems > Persistent Authorities > Interaction > Acquisition Seller Meeting Controller | Authoring creates it |
| `GeneralStoreRuntimeManager` | Game Systems > General Store Runtime | Bootstrapper `storeRuntime` field (FindUnique); NOT authoring-created, CONFIRM |
| `AcquisitionMarketManager` | Game Systems > Acquisition Market | Bootstrapper `acquisitionMarket` field (FindUnique); NOT authoring-created, CONFIRM |
| `StrategyCameraController` | Main Camera rig > Strategy Camera Controller | Camera setup utility; bootstrapper-adjacent, CONFIRM camera assignment |
| `StrategyCameraBounds` | Main Camera rig > Strategy Camera Bounds | Referenced by StrategyCameraController |

## B. Main Scene UI — UI Canvas root

| Script | GameObject | Evidence |
|---|---|---|
| `LandLedgersHUDController` | UI Canvas > HUD Controller | Bootstrapper `hudController` field (FindUnique); authoring ensures control-hint, status strip, town-pulse alert texts |
| `GeneralStorePanelController` | UI Canvas > General Store / Management Panel | Bootstrapper `storePanel` field (FindUnique) |
| `ManagementPanelView` | UI Canvas > Management Panel View | Authoring requires this trio (controller/view/runtime) for business-creation UI |
| `BusinessCreationPanelController` | UI Canvas > Management Panel GameObject (same object as GeneralStorePanelController) | Authoring adds it to the management GameObject and calls `Configure(businesses, management, view)` |
| `ScenarioStartPanelController` | UI Canvas > Scenario Start Panel | Authoring invokes its `AuthorStartPanelForScene`; panel starts inactive |
| `OwnershipWorkflowView` | UI Canvas > Ownership Workflow View (hidden) | Authoring `GetOrCreate`, set invisible |
| `AcquisitionSellerMeetingView` | UI Canvas > Acquisition Seller Meeting View (hidden) | Authoring `GetOrCreate`, set invisible |
| `UITooltipController` | UI Canvas > Runtime_Tooltip (hidden) | Authoring creates it under the canvas |
| `LLMoneyFeedbackOverlay` | UI Canvas > LL_MoneyFeedbackOverlay | Authoring creates it under the canvas |
| `LLMainMusicPlayer` | Player rig > Runtime Music Audio Rig (Music Source A/B children) | Authoring finds the player, then adds the rig under it; NOT authoring-created itself, CONFIRM |
| `LLFeedbackService` | UI Canvas > Feedback Service (self-creating, see discrepancy D2) | `Instance` getter creates a PlayMode-only object if none exists; CONFIRM a persistent one |
| `UITooltipTarget` | Per UI element (any tooltip-bearing control) | Referenced by UITooltipController/registry; NOT a scene root |
| `ScrollRectInputRelay` | Per scrollable panel | Referenced by ScenarioStartPanelController and ManagementPanelView; NOT a scene root |

## C. Per-instance / prefab components — NOT Main Scene roots

These go on the thing they describe, created at runtime or by prefab
authoring. Putting one on a Main Scene root would be wrong.

| Script | Goes on | Evidence |
|---|---|---|
| `AgentMover` | Each visible agent / wagon agent instance | PopulationPathingDirector, Logistics presentation, TownPathingDemoController |
| `PersonVisualIdentityBinding` | Each visible person instance | PopulationPathingDirector |
| `BuildingAnchorMarker`, `BuildingDoorAnimator`, `BuildingFootprintAuthority`, `BuildingExteriorPropAuthoring` | Each placed building / building prefab | TownWorldController, BuildingDefinition |
| `GeneratedWorldContentMarker` | Generated world content instances | TownWorldController, PreAuthoredTerrainWorldProfile |
| `LLModularBuildingPiece` | Each modular building-piece prefab | Editor factory window adds it to piece prefabs; 0 runtime references is EXPECTED, it is prefab data, not limbo |
| `RegionalTerrainTileView` | Each regional terrain tile | TownWorldController, PreAuthoredTerrainSetupUtility |
| `WorldInspectableTarget` | Each inspectable world object | PopulationPathingDirector, TownWorldController, WorldInteractionController |
| `WorldPlacementMaskVolume` | Placement-mask volumes | PreAuthoredTerrainWorldProfile, WorldSurfaceProvider |
| `HorseWorldView`, `WagonWorldView`, `TransportAnimatorState`, `LogisticsTransportVisualController` | Transport prefabs (horse, wagon) | TransportPrefabAuthoring adds them to prefabs; VisualController is added by PresentationController per shipment, not by Main Scene |
| `StrategyCameraBounds` | See section A (camera rig) | — |

## D. Scenario, development, and demo — NOT First Ledger Main Scene

| Script | Correct home | Evidence |
|---|---|---|
| `FirstLedgerBootstrap` | LEGACY First Ledger bootstrap; do NOT also put it in Main Scene alongside `FirstLedgerSliceBootstrapper` | SaveLoadManager still references it for capture/restore, but the slice bootstrapper is the current First Ledger boot path. CONFIRM in Editor that Main Scene contains exactly one First Ledger bootstrapper, or the Pass XIII Civic duplicate pattern repeats |
| `SaloonCircuitBootstrap` | Saloon Circuit scenario scene only | SaveLoadManager `FindAnyObjectByType` returns null safely in First Ledger; a lingering instance across scenario loads is a bug, not a Main Scene assignment |
| `PuppetMasterPanel` | Development only; created on demand by `PuppetMasterWindow` editor tool | Editor window creates it if absent; NOT a Main Scene persistent manager |
| `ScenarioValidationHarness` | Game Systems > Persistent Authorities > Development > Scenario Validation Harness | Authoring DOES create it; intentional dev harness, confirm it is inactive/disabled for a shipping playtest build |
| `TownPathingDemoController` | Demo only; must NOT be a Main Scene persistent manager | 0 production references; `spawnDemoOnStart` defaults false. CONFIRM it is absent or disabled in Main Scene |
| `RegionalWorldDebugOverlay` | Debug only (F7 overlay) | 0 production references, 1 test reference. CONFIRM it is absent or disabled in a shipping Main Scene |

## E. Discrepancies and limbo candidates — Codex must check in the Editor

These are the only MonoBehaviour scripts with no Main Scene home in code.
"Limbo" here means: no authoring creates it, no bootstrapper references
it, and no production caller creates it. It does NOT mean the script is
dead; the Editor scene may already contain it, which only Codex can see.

### D1. LogisticsTransportPresentationController — GENUINE DISCREPANCY
- Real runtime presentation: `LateUpdate` reads
  `LogisticsRuntimeManager.Shipments`, spawns wagon visuals via
  `LogisticsTransportVisualController`, reports town pressure.
- Auto-wires `logisticsRuntime` on Awake/Validate, so it would work if
  placed, but: 0 production references, NOT in
  `PersistentRuntimeHierarchyAuthoring`, NOT a bootstrapper field, and
  no other code instantiates it.
- Required Codex action: EITHER add it to Main Scene at
  Game Systems > Persistent Authorities > Economy and Civic >
  Logistics Transport Presentation, with `logisticsRuntime` wired to the
  Logistics Runtime manager and `preferredTransportPrefab` set, OR
  record a Kennedy decision to retire it in favour of the
  LogisticsRuntimeManager pressure readout and delete the script in a
  later pass. Do NOT leave it silently unassigned, and do NOT delete it
  without that decision. Until Codex acts, wagon shipment visuals from
  this controller are not running in Main Scene, even though the
  controller code is complete.

### D2. LLFeedbackService — SELF-CREATING, CONFIRM PERSISTENCE
- `Instance` creates a PlayMode-only GameObject if none exists. That
  masks a missing persistent assignment: feedback works in PlayMode
  but the object is not saved in the scene.
- Required Codex action: confirm a persistent Feedback Service
  GameObject exists on UI Canvas (with AudioSource, as
  `[RequireComponent]` demands), or add one. Do not rely on the
  self-create path for a shipping scene.

### D3. Bootstrapper-referenced but NOT authoring-created — CONFIRM LIST
Authoring wires the bootstrapper by `FindUnique`, which silently does
nothing when the target is absent. Codex must confirm each of these
exists in Main Scene, because a re-authored scene could lose them
without any code error:
`TownWorldController`, `PathingManager`, `TimeManager`,
`PopulationManager`, `GeneralStoreRuntimeManager`,
`AcquisitionMarketManager`, `GeneralStorePanelController`,
`LandLedgersHUDController`, `SimulationSystemsHub`,
`PreAuthoredTerrainWorldProfile`, `LLMainMusicPlayer`.
If any is absent, re-run `Land & Ledgers/Author Persistent Main Scene
Hierarchy` and then re-check, do not hand-wire a duplicate.

### D4. Plain-C# systems that look like limbo but are NOT
Do NOT create GameObjects for any of these. Their Codex task, if any,
is driver wiring, stated in their own handoffs:
- `BakeryShopRuntime`, `BakeryStagedProductionChain` — no Main Scene
  owner/driver, no save wiring (Phase 2 finding, unchanged). Missing
  link is a driver, not a GameObject.
- `OffMapNamedSkuProcurementService` — production driver must set
  `SaveLoadManager.PassXlProcurementService` before saving, or
  procurement persists empty (Phase 2 finding).
- `FreightCompanyRuntime` / `FreightProductionDriver` — created by
  `SimulationDrivers` per freight business (`EnsureCompany`), not by a
  scene GameObject. Starting a freight business is a business-creation
  action, not a scene assignment.
- `ExternalMarketAuthority`, `GenericRetailSaleAuthority`,
  `HouseholdShoppingLoop`, `ManagerProcureExecutor` — authorities
  called by drivers/runtimes, not scene managers.
- All `*Driver` classes under `Domains/Population` and
  `Orchestration/Systems` except the two GameObject drivers in
  section A — hub-owned, see the rule at the top.

### D5. What is NOT limbo
- `LLModularBuildingPiece` (0 runtime references) is prefab data, see
  section C.
- `TownPathingDemoController` and `RegionalWorldDebugOverlay`
  (0 production references) are demo/debug, see section D.
- Editor-only authoring tools (`PersistentRuntimeHierarchyAuthoring`,
  `ScenarioHudSceneAuthoring`, `TransportPrefabAuthoring`,
  `PersonInspectionPanelAuthoring`, `LLModularBuildingPieceFactoryWindow`,
  `PuppetMasterWindow`, `ScenarioValidationWindow`) are EditMode tools,
  never scene managers. Their menu items are the authoring path, not
  GameObjects.

## F. ScriptableObject definitions — assets, not GameObjects
12 ScriptableObject classes (business/profile definitions, population
settings, scenario assets, etc.) live as `.asset` files outside this
repo. They are referenced by serialized fields (e.g.
`GeneralStoreBusinessDefinition` on the store runtime, population
settings on the bootstrapper), never attached to a GameObject as a
component. Codex: if a definition field is empty in Main Scene, that is
an asset-reference gap, not a missing manager.

## G. Copyable Codex handoff — do the same pass on your own work

```
Codex, do a full Main Scene manager pass on your own work, and check
it, do not just accept the Muse list above. Open Main Scene in the
Editor and, for every MonoBehaviour in the tree, confirm one of these:
it is on the GameObject named in Docs/PassXlMainSceneManagerAudit.md,
it is a per-instance or prefab component, it is scenario or dev only,
or it is a discrepancy you are reporting back. Pay special attention
to D1 LogisticsTransportPresentationController, add it at Game Systems
Persistent Authorities Economy and Civic Logistics Transport
Presentation with Logistics Runtime wired, or get a decision to retire
it, do not leave it unassigned. Confirm D2 LLFeedbackService is
persistent on UI Canvas, confirm the D3 FindUnique list all exist and
are wired into FirstLedgerSliceBootstrapper, confirm exactly one First
Ledger bootstrapper and exactly one Civic Foundation Manager, and
confirm TownPathingDemoController and RegionalWorldDebugOverlay are
absent or disabled. Then report back, for your own scripts and for any
Muse script you disagree with, the GameObject, the evidence you saw in
the scene, and every discrepancy you found. Do not claim scene wiring
from C# alone, and do not create GameObjects for plain C# drivers,
Bakery or Fuel runtimes, or the off-map procurement service, their
missing link is driver wiring, not a GameObject.
```

## H. Limits of this audit
- Scene contents, prefab contents, and Inspector values cannot be
  verified from this repo. Pass XIII remains the last Editor census.
- A script with a production reference is not proof it is in Main
  Scene; `FindAnyObjectByType` callers return null safely and then log
  or fall back, which is exactly how a missing manager hides.
- This audit covers MonoBehaviour placement only. Plain-C# driver
  wiring (who calls whom, in what order, on what tick) is a separate
  audit and is not claimed here.
