# Outer woodland and regional ground pass — 2026-11-09

## Work completed

Added a persistent, deterministic woodland layer on the eight existing outer terrain tiles in Main Scene. The authoring pass created **128 canopy tree instances** and **120 forest-floor/edge instances** (saplings, shrubs, ferns, grass, logs, and small rocks): 16 canopy and 15 understory/edge instances per tile. It reuses existing project prefabs, places irregular two-copse groups using per-tile fixed seeds, samples the terrain for placement height, rejects steep placements, and disables decorative colliders on these instances so they do not change navigation or collision behavior.

The outer-tile ground painting was rerun after including the new beech trees in its canopy-proximity sampling. The existing rural palette paints meadow, disturbed soil, leaf litter, damp earth, and exposed stones using broad noise, terrain height/slope, and distance from vegetation. This only updated the eight outer TerrainData alphamaps. It did not modify heightmaps, the center terrain, terrain colliders, road logic, startup settings, or simulation bounds.

## Layout and data verification

The Unity Editor's read-only terrain audit found nine distinct terrains. All are 1024 × 80 × 1024 metres with 257 heightmap and 256 alphamap resolution. Their positions form the intended 3×3 layout: center at (-512, -4, -512), cardinal neighbors at ±512, and corner neighbors at ±1536/512 as appropriate. The center remains `Pre-Authored Terrain` and retains its existing TerrainData asset and terrain-layer palette.

The audit sampled each of the 12 shared edges at 65 positions. Every reported mean and maximum height difference was 0 metres. Neighbor references were reciprocal in the expected cardinal directions. The center TerrainData remains `Assets/Core/World/PreAuthoredTerrain/MainAuthoredTerrainData.asset`; outer tile TerrainData remains under `Assets/EnvironmentDevelopment/RegionalTerrain3x3/`.

## Coverage capture

`OUTER_WOODLAND_COVERAGE_2026-11-09.md` is a read-only per-tile inventory. Its counts are **Renderer components**, not prefab-instance counts: 48–79 renderer components per outer tile after the woodland addition. The counts vary with the child renderers inside the reused prefabs. The vegetation root is `AuthoredWorldContent/Outer Regional Woodlots`, with per-tile `Canopy` and `Forest Floor and Edge` groups.

## Validation and limits

- Unity 6000.4.2f1 Editor was in Main Scene, Edit Mode. The scene hierarchy showed the persistent outer woodlot root and its instances.
- A close Scene view inspection showed clustered beech canopies and understory on an outer tile. These assets are persistent scene instances and therefore visible in Edit Mode.
- The scene was saved. A short fresh NewGame was started and exited safely; the startup completed at 1×, and the authored woodlot root remained in the runtime hierarchy. The gameplay camera stayed town-centered, so distant woodlots were **not** visually confirmed from the in-game camera.
- The latest script compilation completed with no C# errors. Unity Console still showed two Package Manager `ScriptableSingleton already exists` errors and 36 warnings, including existing compiler warnings. These are not terrain validation successes and were not repaired in this pass.
- No Unity Test Runner tests were run. No long PlayMode terrain/performance test was run. Runtime frame cost, far-camera readability, and night readability remain unmeasured.
- Authored center and outer height ranges, dimensions, neighbor links, road layout and simulation bounds were not changed by this woodland/paint pass.

## Files changed by this pass

- `Assets/EnvironmentDevelopment/Editor/OuterWoodlandAuthoring.cs` — deterministic persistent outer woodland authoring and read-only coverage audit.
- `Assets/EnvironmentDevelopment/Editor/OuterWoodlandAuthoring.cs.meta` — new script metadata, GUID `d22c2dc076184161b440ce91eaa5410e`.
- `Assets/EnvironmentDevelopment/Editor/RegionalTerrainAuditAndPaint.cs` — include authored outer beech trees when computing leaf-litter paint influence; qualify `System.IO.Directory.CreateDirectory` to avoid the project's `LandLedgers.Directory` namespace collision.
- `Assets/Main Scene.unity` — saved persistent outer woodland instances and organizational root.
- `Assets/EnvironmentDevelopment/RegionalTerrain3x3/RegionalTerrain_*.asset` — outer alphamaps repainted by the existing ground-painting utility; no height data changed.
- `Reports/Diagnostics/OUTER_WOODLAND_PASS_2026-11-09.md` — this implementation record.
- `Reports/Diagnostics/OUTER_WOODLAND_COVERAGE_2026-11-09.md` — read-only coverage audit.
- `Reports/Diagnostics/OUTER_TERRAIN_AUDIT_2026-11-09.md` — read-only layout, dimensions, neighbors, and seam-height audit.

## Recovery checkpoint

Before this woodland pass, a byte-for-byte external checkpoint was created at `C:\Users\kenne\Documents\LandLedgersTerrainCheckpoint_2026-11-09\Before-outer-woodland-pass`. It contains the then-current Main Scene, scene metadata, and the eight outer TerrainData assets and metadata. The checkpoint is outside the Unity project. No Git cleanup, reset, broad staging, commit, or push was performed.

## Remaining visual work

The added clusters improve the outer countryside at close range, while the town-centered runtime camera does not presently show enough of the outer tiles to assess their full visual impact in normal play. A later visual pass should inspect the regional landscape from a suitable overview camera, confirm distance/LOD behavior and performance, and evaluate night readability. Any further density increase should be based on that camera and performance evidence.
