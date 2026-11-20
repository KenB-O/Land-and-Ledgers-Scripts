# Building Materials Asset Audit

Generated from the live Unity AssetDatabase on 2026-11-14. Every prefab under `Assets/Environment/Building Materials` is listed in `BuildingMaterialsAssetInventory.csv`, including untagged prefabs, paired source/LOD references, renderer bounds, collider types, mesh names, material names and available module metadata.

## Inventory

- Total prefab assets: 3,534
- Normalized ready modules in `Auto Extracted/*/Prefabs`: 1,172
- Paired source/LOD references: 2,362
- Ready modules with `LLModularBuildingPiece`: 1,172
- Prefabs without module metadata: 18
- No standalone FBX, mesh, material or texture assets are stored in this directory; normalized prefab meshes and materials reference assets elsewhere in `Assets/Asset Packs/Western/Models/Buildings`.
- The 12 referenced source building FBX imports (Church, Doctor, GeneralStore, Hanger, House01, House02, House03_bank, House04, House05, House06, Saloon and Sheriff) currently use `globalScale: 1`, `useFileUnits: 1`, `useFileScale: 1`, and `meshCompression: 0` in their importer metadata. Their common import scale does not normalize their authored geometry into a common building size. No source import settings were changed.

## Ready-module source families

- Church: 123
- Doctor: 82
- GeneralStore: 112
- Hanger: 5
- House01: 85
- House02: 112
- House03_bank: 33
- House04: 60
- House05: 106
- House06: 77
- Saloon: 311
- Sheriff: 66

## Ready-module categories

- Detail: 180
- Door: 13
- Floor: 123
- Other: 81
- Porch: 66
- Roof: 39
- Stair: 38
- Wall: 628
- Window: 4

## Geometry and compatibility findings

- Normalized module roots are generally unit scale and the catalog `cellSizeMeters` is 2 m, but geometry is not uniformly oriented. The House01 floor source mesh has 4×4×0.25 m source bounds and a 0.5 child visual scale, yielding a 2×2×0.125 m panel in local X/Y with thickness on Z. It requires a 90° X-axis adapter to lie on a Unity X/Z floor.
- Several wall families are already Unity Y-up (2×2×~0.13 m panels); some doorway/window panels are authored with vertical extent on local Z and thin local Y, so those require their own axis adapter.
- The ready House01 wall-with-door panel has a 2×2×0.174 m visual bounds and a single full-panel BoxCollider. Its door leaf is about 0.73×1.42 m. It cannot be treated as a valid standard adult entrance without changing collision and using a better-sized doorway.
- House04 `Door Frame Doors` has bounds approximately 1.11×0.21×2.00 m, a separate approximately 0.99×0.20×1.94 m door leaf, and separate frame/leaf MeshColliders. Rotating the module 90° about X makes it upright, but the leaf/collision state and actual aperture must be explicitly validated.
- The 13 normalized `Door` modules were inspected as a set. Church `Door Frame 003 Doors` is the strongest doorway candidate: its frame collider is open at center, but its two `Wall45` leaf colliders overlap across the centerline when closed. A bounded `PhysicsScene.Raycast` sweep through the assembled doorway hit both leaves at x=0; it is not a clear traversable opening in its supplied state. Several other door-category items are windows or closed door panels. A runtime/editor door state that moves or disables only the leaf collision, with a matching visible open state, is still needed before any candidate can be accepted for navigation.
- Although all ready pieces claim a 2 m cell, some have very different actual spans, short trim dimensions, source-axis differences and full-panel generated colliders. Category plus cell size is not sufficient compatibility evidence.
- Ready modules contain paired LOD0/LOD1 presentation roots and a collision root. Their source meshes/materials remain shared external references. Keep source prefabs untouched; use the extracted module wrapper and per-placement axis/position adapters.

## Complete building scale comparison

- Blue Wood and Large Building both have root scale 0.75. Unity-loaded full renderer bounds in prefab-root local coordinates are Blue Wood 12.59×8.16×12.27 m and Large Building 10.17×5.34×14.14 m, including attachments/roof details; approximate displayed extents at the shared root scale are 9.44×6.12×9.20 m and 7.63×4.01×10.61 m. Different source/nested geometry drives the difference; the root scale alone does not.
- These are whole-renderer extents, not lawful footprints or clear interior bounds. Do not globally rescale. Measure footprints and entrances independently and use per-instance compatible adapters.

## Recommended convention

- Keep 1 Unity unit = 1 m. Use the existing 2 m extracted-module cell as the coarse placement lattice, with measured half-cell/offset placement for openings and irregular footprints. Do not snap all asset bounds blindly.
- Keep storey elevations explicit per assembly (for example, legacy floor elevations are not interchangeable with Blue Wood’s authored 0.8 m floor level).
- Allow per-module quarter-turn axis adapters, then validate transformed render/collider bounds, surface orientation, opening clearance, footprint containment and connection faces before saving an assembly. Reject mixed source families by default until an explicit compatibility mapping exists.

## Implemented authoring foundation and limits

- A catalog builder indexes only normalized `Prefabs` modules; source/LOD reference prefabs are excluded from assembly choices.
- Assembly metadata represents footprint cells, explicit storey elevations, source family, placements, module IDs and per-placement floor/rotation. The editor works in an isolated preview scene and writes a new prefab under `Building Materials/Examples`; it does not modify Main Scene.
- Generated assemblies attach one `BuildingFootprintAuthority` and configure geometry-derived `InteriorFloorAuthoring` per floor. Existing floor triangles, door pairs and equipment obstruction remain the navigation authority. A stair piece alone does not create a vertical path link.
- No modular example prefab has been saved yet. A usable example requires a floor module with an axis adapter and doorway whose leaf/collider leaves a measured clear path; current candidate geometry is not yet verified as a complete traversable assembly. The House01 automatic example is intentionally guarded against this mismatch. The isolated Church-door ray test also found centerline leaf collisions, so saving it as an accessible example would falsely imply a usable entrance.

## Source collections

- `Auto Extracted/*/Prefabs`: ready normalized module prefabs.
- `Auto Extracted/*/Sources`: paired LOD/source references, not assembly pieces.
- `/Sources`: 18 source-family reference prefabs.
- The directory’s top-level `Prefabs` folder contains no additional prefab assets.

## Current complete-building dimensions at authored root scales

The following were measured by loading each prefab with Unity `PrefabUtility.LoadPrefabContents` and summing transformed renderer local bounds through nested transforms. “Full render extent” includes roof/porch/decorative attachments; “footprint” is the building authority corner rectangle. These are distinct measurements.

| Prefab | Root scale | Full render extent (W×H×D m) | Authority footprint (W×D m) | Nested non-unit transforms |
|---|---:|---:|---:|---:|
| House Natural Wood | 0.75 | 6.97×6.11×7.37 | 7.27×7.10 | 39 |
| Single Story Business Building White Plaster | 0.75 | 6.41×4.98×7.08 | 6.80×7.63 | 22 |
| Single Story Business Building Blue Wood | 0.75 | 9.44×6.12×9.21 | 9.88×9.70 | 16 |
| Double Story Business Building Green Wood | 0.75 | 6.44×7.89×10.41 | 6.92×10.82 | 39 |
| Double Story Business Building Natural Wood | 0.75 | 6.71×9.15×12.36 | 7.66×13.06 | 39 |
| Double Story Business Building Red Wood | 0.75 | 12.41×8.80×12.52 | 13.23×12.96 | 65 |
| Large Building | 0.75 | 7.63×4.00×10.61 | 10.65×13.24 | 388 |
| Townhall | 0.75 | 15.11×7.73×17.50 | 18.59×19.11 | 45 |

All eight currently share a 0.75 root scale, but have different nested geometry, footprints and attachment envelopes. The Blue Wood and Large Building comparison therefore does not support a blanket scale correction: Blue Wood renders wider/taller, while Large Building has a larger claimed footprint and depth. The very high nested transform count on Large Building also makes root-scale-only diagnosis inadequate.

The author-provided scale references remain 1 Unity unit = 1 m, adult man 1.71 m, adult woman 1.59 m, Blue Wood entrance about 2.10 m high × 1.07 m clear width, and Blue Wood floor elevation 0.8 m. Those Blue Wood measurements are reference points, not values inferred for House01/House04 modules. The old common door assets measured in the existing scale audit (roughly 1.42 m leaf height) are incompatible with full-height adult access without a better module or a measured adapter.
