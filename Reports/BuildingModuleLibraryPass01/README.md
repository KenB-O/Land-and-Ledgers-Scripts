# Building Materials — Pass 1 audit

## Outcome

The library is now indexed by a central Unity catalog at `Assets/Environment/Building Materials/Modules/BuildingMaterialsModuleCatalog.asset`. The catalog exposes functional category paths, source-family identity, descriptive display names, unique family-qualified IDs, material variants, measured renderer bounds, recommended floor axis adapters, and conservative doorway-collider diagnostics. The assembly authoring window now filters the catalog by category and source family.

The 1,172 ready module prefabs remain at their existing `Auto Extracted/<family>/Prefabs` paths. This is intentional: `LLModularBuildingPiece.outputPrefabPath`, paired LOD paths, extractor regeneration, prefab references, and existing consumers are path-sensitive. Moving these as a bulk operation without migrating those authorities risks duplicate generated prefabs or broken references. The task allows unsafe moves to remain in place and be exposed through the catalog. The central catalog is the canonical browsing/index layer; this pass did not copy the 1,172 prefabs into duplicate category folders.

The extractor's previous “clean generated output first” behavior deleted the entire generated tree. It now refreshes matching assets in place and skips destructive deletion, preserving prefab GUIDs and LOD dependencies. The catalog is rebuilt after full or single-family extraction.

## Inventory reconciliation

The directory contains 3,534 prefabs and one catalog asset:

- 1,172 ready-to-use normalized modules under `Auto Extracted/<family>/Prefabs`.
- 2,344 generated source/LOD prefabs under `Auto Extracted/<family>/Sources`.
- 18 additional source/LOD prefabs under the top-level `Sources` tree.
- Total source/LOD prefabs: 2,362. Thus `1,172 + 2,362 = 3,534`; the earlier “18 exceptions” are included in the 2,362, not additional.
- The 12 original model sources are FBX files in `Assets/Asset Packs/Western/Models/Buildings`, outside `Building Materials`; this library contains no standalone FBX files.
- The non-meta file types inside the directory are 3,534 `.prefab` files and one `.asset` catalog.

Ready-module families:

| Source family | Ready modules |
|---|---:|
| Church | 123 |
| Doctor | 82 |
| GeneralStore | 112 |
| Hanger | 5 |
| House01 | 85 |
| House02 | 112 |
| House03_bank | 33 |
| House04 | 60 |
| House05 | 106 |
| House06 | 77 |
| Saloon | 311 |
| Sheriff | 66 |

Catalog virtual categories after geometry-aware classification:

| Category | Count |
|---|---:|
| Floors | 123 |
| Foundations | 0 |
| Walls/Solid | 520 |
| Walls/Doorways | 36 |
| Walls/Windows | 63 |
| Walls/Corners | 0 recognized by current name + proportion rule |
| Doors | 13 |
| Windows | 4 |
| Roofs | 39 |
| Stairs | 38 |
| Balconies | 66 |
| Details | 180 |
| Other | 81 |
| Other/Wall-Labeled Geometry | 13 |

The audit found zero missing ready-module metadata, zero unresolved LOD0/LOD1 references, zero `outputPrefabPath` mismatches, zero null catalog references, and zero duplicate catalog asset references. All 1,172 ready module roots have local scale `(1,1,1)`, all declare a 2 m grid cell, and no missing scripts were found. Most have one collider (1,114); the remainder have multiple colliders.

## Identity, duplicates, and variants

Catalog IDs use `source-family/module-name/material-name`; if a genuinely repeated tuple remains, a deterministic suffix is assigned after asset-GUID-ordered discovery. Legacy `pieceId` and `sourceKey` values remain aliases. Alias resolution refuses to guess when the alias has more than one catalog match.

There are 87 groups of colliding legacy `pieceId` strings. These are identifier collisions, not confirmed duplicate prefabs. A conservative ready-module signature pass compared shared mesh asset identity, hierarchy transforms, assigned materials, collider type/bounds/state, and root scale. It found zero repeated signatures. This does not prove vertex-level geometric equivalence across distinct mesh assets, so nothing was deleted or merged. Eleven `(source family, source key)` groups contain multiple material names and remain separate, browseable variants.

## Geometry and orientation findings

`Floor 4 X 4 B 003 4x4 Wood Wall 2` from House01 measures about `2.0 × 2.0 × 0.4 m` in its current source orientation, while its metadata reports a 2 m cell and 1×1 occupancy. This confirms that “4x4” is not its measured Unity-meter footprint. Its thin axis is Z; the catalog recommends an X-axis quarter-turn of 3 to orient that surface horizontally. The module is not rescaled, and its 1×1 grid metadata remains unchanged.

Source families include both Y-up and Z-up mesh orientations. Wall classification therefore uses orientation-independent planar dimensions instead of assuming Y is the wall height. This moved 404 valid wall-labeled modules out of the false “Other” grouping in the earlier draft. Category labels do not rotate or approve geometry for assembly; the assembly adapter and compatibility validation remain responsible for placement.

Door/window classification is not treated as proof of traversal. The House01 `Wall 2 A 02 Door C 001` is listed as a doorway candidate, but an enabled collider intersects the nominal opening envelope and the catalog marks it blocked. A clear envelope is still only a candidate; traversal requires a route test and authoring data. In total, 112 door/window-named modules need geometry/collider review. No doorway was automatically certified as traversable.

The full machine-readable inventory is `inventory.csv`; additional review lists include `duplicate-piece-ids.txt`, `same-mesh-material-collider-signature-groups.txt`, `door-window-review.txt`, `missing-lod-paths.txt`, `path-mismatches.txt`, and `original-source-models.txt`.

## Use in Unity

- Run **Land & Ledgers → Buildings → Build Building Materials Catalog** to refresh the central index.
- Open **Land & Ledgers → Buildings → Modular Assembly Authoring** to filter ready modules by catalog category and source family.
- Catalog rows show family, category, module name, grid footprint, and material variant. Selecting a floor applies the measured orientation recommendation; compatibility validation rejects mismatched family/cell size/nested-scale combinations.
- The assembly authoring window includes a House01 example-generation action, but this pass did not run it or claim an example prefab was created. The current door-named House01 candidate is collider-blocked, and the generator intentionally refuses that candidate. Main Scene was not used or modified for a demonstration.

## Validation

- Unity 6000.4.2f1 Editor compilation completed with `failed=false`, no compiler errors, and Editor state ready.
- 15 focused EditMode cases passed across `LLBuildingModuleCatalogTests` and `LLModularBuildingAssemblyTests`, including catalog completeness/LOD references, floor bounds/orientation, Z-up wall classification, ambiguous aliases, doorway collider blocking, assembly family/scale compatibility, storey elevations, axis adaptation, and four grid-snapping cases.
- The Test Runner lists 6,639 EditMode tests in the current Editor; the full suite was not run.
- No PlayMode run was needed for this metadata/catalog pass. No visual route/traversal claim is made.
- Editor remained in Edit Mode. Main Scene and authored building prefabs were not edited by this pass.

## Preserved limitations

The 1,172 modules are cataloged but not physically moved into category folders. Asset/import scale normalization is preserved rather than rewritten. Corner modules, foundations, and final door/window classifications need explicit authoring or more geometry review where not recognized. The tested doorway candidate remains blocked; this pass does not implement runtime building traversal or construction economy behavior.
