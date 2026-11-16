# Building and equipment groundwork — 2026-11-16

## Outcome

Added the smallest source-level link needed for independently placed furniture/equipment to participate in a particular building's physical interior navigation. The building prefab remains the structural template. A placed object can remain a separate scene/runtime object and explicitly reference one `BuildingFootprintAuthority`; it blocks only that building's walkable floor. Existing child-of-building placements continue to work.

This association describes physical placement only. It does not grant ownership, create inventory, or imply acquisition. No prefab, scene, asset, terrain, or save schema was modified. The owner's dirty `House Natural Wood.prefab` and `Main Scene.unity` were preserved and left unsaved.

## Existing behavior and finding

`BuildingEquipmentPlacement` already stores placement identity, definition, floor, and physical obstruction metadata. Its comments and fields make economic ownership separate. Before this change, its building was resolved only with `GetComponentInParent<BuildingFootprintAuthority>()`, and the building's navigation query only gathered descendants. The provided authoring tool creates an independent scene object (not a prefab-asset child) but parents that object under the scene building instance. A separately rooted scene/runtime object could not be associated with a building.

No serialized `BuildingEquipmentPlacement` references were found in the current scene/prefab YAML scan. No code path was found that turns nested building prefab contents into economic equipment ownership. Accordingly, the owner's removed Natural Wood prefab children were left untouched; they were not restored or reclassified.

## Existing furniture and equipment prefab inventory

The following relevant prefabs were inspected in `Assets/Environment/Props`. Prefab YAML inspection found no attached project gameplay `MonoBehaviour` scripts on the listed assets. Their existing components are transforms, mesh filters/renderers and, where noted, colliders, LOD groups, an Animator, or nested prefab references. A visible stove, shelf, cabinet, crate, or package does not by itself register a workstation, usable capacity, stock, owner, or economic item.

| Prefab path | Repository status at inventory | Existing serialized component summary | Readiness and next integration |
|---|---|---|---|
| `Assets/Environment/Props/Bed.prefab` | tracked | Mesh, renderer, box collider, LODGroup | Physical bed asset; needs instance placement and bed/sleep activity binding. |
| `Assets/Environment/Props/Stove.prefab` | local untracked prefab and `.meta` | Mesh, renderer, Animator | Candidate stove visual; requires collider/obstruction review and legitimate stove/fuel/workstation definition. |
| `Assets/Environment/Props/Cooking Stove.prefab` | tracked | Mesh and renderer | Visual placement; no collider or gameplay adapter found in prefab YAML. Needs physical obstruction and stove/workstation wiring. |
| `Assets/Environment/Props/Cabinet Small.prefab` | tracked | Mesh and renderer | Cabinet visual; needs explicit storage-zone/category/capacity association if used for storage. |
| `Assets/Environment/Props/Cabinet Large.prefab` | tracked | Mesh and renderer | Same; capacity must come from authorized configuration, not model-size assumptions. |
| `Assets/Environment/Props/Wall Mounted Cabinet.prefab` | local untracked prefab and `.meta` | Mesh and renderer | Requires wall/placement validation and storage configuration; do not treat its hanging visual as floor storage. |
| `Assets/Environment/Props/Closet.prefab` | local untracked prefab and `.meta` | Mesh, renderer, box collider, LODGroup, nested prefab | Physical obstruction exists; storage contents/capacity and clothing access remain unwired. |
| `Assets/Environment/Props/Shelf.prefab` | local untracked prefab and `.meta` | Mesh and renderer | Candidate display/storage fixture; requires authorized capacity/category or display allocation and navigation obstruction placement. |
| `Assets/Environment/Props/Item Shelf.prefab` | local untracked prefab and `.meta` | Mesh and renderer | Same as Shelf; package visuals must not add inventory. |
| `Assets/Environment/Props/Table.prefab` | local untracked prefab and `.meta` | Mesh, renderer, capsule collider, LODGroup, nested prefab | Physical table candidate; needs placement and activity-specific workstation binding where applicable. |
| `Assets/Environment/Props/SmallTable.prefab` | local untracked prefab and `.meta` | Mesh, renderer, box collider, LODGroup, nested prefab | Physical table candidate; no gameplay function is attached. |
| `Assets/Environment/Props/Low Wooden Table.prefab` | local untracked prefab and `.meta` | Mesh and renderer | Visual placement candidate; needs obstruction configuration if used in a navigable room. |
| `Assets/Environment/Props/Chair.prefab` | local untracked prefab and `.meta` | Mesh, renderer, box collider, LODGroup | Physical seating asset; requires a specific service/activity binding to become a workstation. |
| `Assets/Environment/Props/SmallChair.prefab` | local untracked prefab and `.meta` | Mesh, renderer, box collider, LODGroup, nested prefab | Same as Chair. |
| `Assets/Environment/Props/Teachers Room Chair.prefab` | local untracked prefab and `.meta` | Mesh and renderer | Visual seating candidate; collider and functional assignment need configuration. |
| `Assets/Environment/Props/Kitchen Counter.prefab` | tracked | Mesh and renderer | Suitable visual work surface; needs station type, reachable interaction side(s), and obstruction configuration. |
| `Assets/Environment/Props/Wooden Kitchen Counter.prefab` | local untracked prefab and `.meta` | Mesh and renderer | Same; preserve as local asset and do not stage automatically. |
| `Assets/Environment/Props/Bar.prefab` | local untracked prefab and `.meta` | Mesh, renderer, box collider, nested prefab | Candidate service counter; collider is physical only. Needs typed station metadata and checked employee/customer approach positions. |
| `Assets/Environment/Props/Crate With Packages.prefab` | tracked | Nested prefab instance and transform | Crate/package presentation only; package visuals are not goods. Configure separate real storage only if an authorized storage zone is associated. |
| `Assets/Environment/Props/Crate01.prefab`–`Crate05.prefab` | tracked | Mesh, renderer, box collider, LODGroup | Physical crate variants; category/capacity must be explicitly configured. |
| `Assets/Environment/Props/Barrel.prefab` | local untracked prefab and `.meta` | Mesh, renderer, capsule collider, LODGroup, nested prefab | Physical container candidate; needs permitted commodity/category and capacity configuration. |
| `Assets/Environment/Props/Barrel01.prefab`–`Barrel03.prefab` | tracked | Mesh, renderer, capsule collider, LODGroup | Physical barrel variants; contents are controlled by existing inventory/storage authority, never by appearance. |

Status is for this local checkout. Local untracked entries exist in the owner's project but will not reach Muse through the published branch unless separately reviewed and authorized. This pass does not add or modify any listed prefab or `.meta` file. Other commercial source/model libraries exist outside this curated props list; they were not treated as installed gameplay prefabs or inventoried exhaustively.

### Structural pieces versus independently placed equipment

Building shells, floors, walls, doorway/window frames, roofs, and permanently integrated architectural elements belong to the structural building prefab/design. A bed, stove, cabinet, shelf, table, chair, counter fixture, barrel, or crate is an independently placed furnishing/equipment instance. It should have a stable placement identity and floor/local transform, and be associated with one building instance through `BuildingEquipmentPlacement`. Reusable structural prefabs must not carry instance-specific furnishing ownership or automatically furnish every building.

### Reusable authorities and missing connection

- **Physical placement/navigation:** `BuildingEquipmentPlacement` plus `BuildingFootprintAuthority`. The Editor workflow creates a scene object for the selected building instance; detached objects can explicitly call `AssociateWithBuilding(authority)`. This affects physical obstruction only.
- **Business ownership:** `SimulationSystemsHub.EquipmentRegisterFor(businessInstanceId)` and `BusinessEquipmentRegister` are the business-owned equipment authority. Legitimate purchase/acquisition or scenario endowment must register the item. A scene prefab does not establish title.
- **Task capability/workstation:** `WorkstationCatalog`, `WorkstationDefinition`/`WorkstationInstance`, `EquipmentTaskGate`, and runtime workstation checks describe component requirements and gate work. Existing examples include bakery oven, sawmill saw-line, wheelwright, barber chair, and printing press. No inventoried visual prefab is automatically resolved to these records.
- **Physical stock/storage:** `PhysicalStorageZone` and physical capacity contracts integrate with the existing lot/inventory authority. Configure fixture category/capacity only through that authority; do not create a second ledger or count package meshes.
- **Interaction/access:** building/interior route and typed activity-location authorities must supply physically reachable employee/customer positions. A counter mesh or collider alone is not a service station; work still reserves time through the existing task/scheduler authority.
- **Construction tools:** household-held tools and `EquipmentReservationService` used by `ConstructionProjectExecutor` are temporary project resources, not permanent furnishings.

**Readiness summary:** the tracked beds, cooking stove, cabinets, counters, crate variants, and barrels exist as visual/physical assets; some have colliders, others need obstruction setup. The inspected prefab YAML contains no attached gameplay adapter. Each requires instance placement and explicit ownership, workstation, storage, or activity binding as appropriate. Untracked furnishings are available only in this local tree and should be reviewed by provenance before publication.

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
