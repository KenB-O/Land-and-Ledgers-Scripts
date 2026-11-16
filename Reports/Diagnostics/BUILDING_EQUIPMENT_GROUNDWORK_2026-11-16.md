# Building and equipment groundwork — 2026-11-16

## Outcome

Added the smallest source-level link needed for independently placed furniture/equipment to participate in a particular building's physical interior navigation. The building prefab remains the structural template. A placed object can remain a separate scene/runtime object and explicitly reference one `BuildingFootprintAuthority`; it blocks only that building's walkable floor. Existing child-of-building placements continue to work.

This association describes physical placement only. It does not grant ownership, create inventory, or imply acquisition. No prefab, scene, asset, terrain, or save schema was modified. The owner's dirty `House Natural Wood.prefab` and `Main Scene.unity` were preserved and left unsaved.

## Existing behavior and finding

`BuildingEquipmentPlacement` already stores placement identity, definition, floor, and physical obstruction metadata. Its comments and fields make economic ownership separate. Before this change, its building was resolved only with `GetComponentInParent<BuildingFootprintAuthority>()`, and the building's navigation query only gathered descendants. The provided authoring tool creates an independent scene object (not a prefab-asset child) but parents that object under the scene building instance. A separately rooted scene/runtime object could not be associated with a building.

No serialized `BuildingEquipmentPlacement` references were found in the current scene/prefab YAML scan. No code path was found that turns nested building prefab contents into economic equipment ownership. Accordingly, the owner's removed Natural Wood prefab children were left untouched; they were not restored or reclassified.

## Implementation

- `Assets/Scripts/Domains/World/Buildings/BuildingEquipmentPlacement.cs`
  - Added an optional serialized `BuildingFootprintAuthority` scene-instance reference and public `AssociateWithBuilding` runtime API.
  - Retains parent discovery as a compatibility fallback.
  - Registers/unregisters with the selected building when enabled, reassigned, reparented, detached, or disabled; transform changes invalidate derived placement geometry.
- `Assets/Scripts/Domains/World/Buildings/BuildingFootprintAuthority.cs`
  - Keeps a nonserialized set of explicitly associated live placements and merges it with existing child placements when building the obstruction cache.
  - Updated authoring help to distinguish the scene object placement from the reusable prefab and clarify that the association grants no ownership or inventory.
- `Assets/Scripts/Tests/Editor/World/InteriorFloorAuthoringTests.cs`
  - Added tests for a detached placement affecting only its selected building, reassignment between buildings, detachment, and movement-driven cache refresh.

Existing `.meta` GUIDs are unchanged. The existing in-scene placement tool remains compatible.

## Relevant current authorities and Muse integration points

1. **Physical building / placement:** `BuildingFootprintAuthority` owns derived walkability and obstruction evaluation. `BuildingEquipmentPlacement` is the placement record. For equipment installed in a specific building, create/restore an actual scene/runtime object, set its floor and placement identity, then call `AssociateWithBuilding(targetAuthority)` (or assign its serialized `Building Authority`). Do not add the furnishing to the reusable structural prefab. On removal/replacement, detach, disable, or destroy that placement object; its obstruction registration is released.
2. **Economic equipment and ownership:** `SimulationSystemsHub.EquipmentRegisterFor(businessInstanceId)` returns the per-business `BusinessEquipmentRegister`, persisted through the existing Pass 9 equipment-register save seam. `EquipmentTaskGate` checks task-defined equipment support. Neither visual placement nor a building prefab should populate that economic register automatically. Muse should connect a legitimately acquired/owned item to its business, while separately establishing its physical placement when installed.
3. **Sawmill:** the active Main Scene bootstrap is configured through the referenced `DevelopmentTownConfiguration` asset (asset GUID `f5820dc040b04928a91079ae0c36b461`); it enables development-town configuration. The config defines sawmill and lumber-yard business slots. `FirstLedgerSliceBootstrapper.ConfigureSawmillProductionWiring` adopts the hub-owned mill runtime and shared equipment task gate, and registers the production driver, counters, custody ledger, and logging runtime. The scenario-authorized sawmill opening axe is added to the per-business equipment register by `ScenarioMillOpeningEquipmentEndowment`; this is not a physical scene machine placement. Blade-maintenance tasks require the `sawmill-saw-line` workstation code through the equipment gate. The present scene has no `BuildingEquipmentPlacement` records binding the business's work equipment to a physical building instance.
4. **Lumber yard:** the hub holds one `LumberYardShopRuntime` per business instance. Bootstrap binds `LumberYardConstructionPricePort`, registers the yard as a construction supplier, and wires the sawmill-to-yard transfer using those same runtime instances. The reviewed bootstrap does not create a yard `BusinessEquipmentRegister` or connect yard work equipment to a physical placement record. Do not infer that a prefab or business slot means the yard owns physical fixtures.
5. **NPC construction commissioning:** `NpcConstructionNeedEvaluator` and `NpcConstructionDriver` determine whether a real household/design/reason/funding/supplier/parcel/member set can commission work. `ConstructionProjectExecutor` checks and reserves required household-held tools with `EquipmentReservationService`, records package equipment holds, and releases them on completion/cancellation. These are construction tools, not building furnishings. A later integration should preserve the distinction between (a) acquired movable equipment, (b) installation/placement in a particular completed building, and (c) structural construction progress. This pass adds no commissioning or procurement behavior.

## Verification

Unity 6000.4.2f1: forced script recompile completed with `failed=false`, `compilationFailed=false`, and no compiler errors. Editor remained in Edit Mode.

| EditMode suite | Result |
|---|---:|
| InteriorFloorAuthoringTests | 15/15 passed |
| SawmillProductionDriverTests | 35/35 passed |
| NpcConstructionCommissioningDef011Tests | 19/19 passed |
| LumberYardConstructionBindingTests | 2/2 passed |
| **Total** | **71/71 passed** |

The new tests establish scene-instance association, isolation between two buildings, reassignment, detachment and transform-driven navigation refresh. No PlayMode inspection or long simulation was performed.

## Remaining scene and runtime work

- Do not restore the owner's removed Natural Wood prefab contents. Place legitimate fixtures only in individual building instances or through runtime ownership/acquisition systems.
- Existing prefab instances that need furniture must have separate placed objects with an explicit `Building Authority` reference or be placed under the intended scene building instance using the existing editor tool.
- Physical placement is not yet joined to item title/business inventory or to the Sawmill/Lumber Yard task capability records. Muse should use the placement API above and the existing `BusinessEquipmentRegister`, `EquipmentTaskGate`, production/task, and commissioning authorities; do not infer acquisition from a scene object.
- No commissioning, forestry, sawmill, lumber-yard, construction, persistence, or scene-authoring changes were made in this groundwork pass.

## Publication

Published from a new Codex branch based on `5789b93168bdc588f1e3afefbf304a7061322fab`. The exact branch and commit are provided in the final handoff after remote verification.
