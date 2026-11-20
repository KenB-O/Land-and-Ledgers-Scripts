# Outer Terrain Relief Pass — 2026-11-09

One-time deterministic broad-relief authoring pass for the eight existing outer TerrainData assets. The authored town-center TerrainData and Main Scene transforms are not modified.

The added field combines world-coordinate Perlin variation at approximately 625 m and 250 m scales. The theoretical maximum contribution is ±4 m, while the sampled Perlin range is expected to produce gentler relief near ±2 m. A 160 m smooth fade reaches zero on every tile edge, preserving all shared edge samples and producing a gradual transition near the town tile. The pass creates no sharp peaks, changes no tile dimensions, collider bounds, terrain positions, scene objects, road logic, or simulation/pathfinding bounds.

| Tile | Before elevation range (m) | After elevation range (m) | Max absolute change (m) | Sampled peak slope after (degrees) |
|---|---:|---:|---:|---:|
| Regional Terrain E | 12.713 | 14.542 | 2.45 | 5.169 |
| Regional Terrain N | 13.653 | 12.764 | 1.977 | 4.663 |
| Regional Terrain NE | 11.905 | 11.917 | 1.934 | 4.346 |
| Regional Terrain NW | 11.295 | 11.331 | 2.288 | 3.676 |
| Regional Terrain S | 14.149 | 14.525 | 1.978 | 4.033 |
| Regional Terrain SE | 11.715 | 11.971 | 1.98 | 4.036 |
| Regional Terrain SW | 13.529 | 14.21 | 1.823 | 5.103 |
| Regional Terrain W | 12.921 | 13.382 | 2.452 | 4.622 |

## Verification

- The Editor applied relief to the eight outer tiles only. The center TerrainData was excluded from this operation. It is already locally modified relative to the repository baseline and was preserved; this pass did not restore or overwrite it.
- Main Scene was byte-for-byte unchanged during the relief pass (SHA256 `802DCBB7804312C4AE51863EBEF1ACBC4FE060141B86F2194FAA7121933F5F0F`), as confirmed against the pre-pass copy at `C:\Users\kenne\Documents\LandLedgersTerrainCheckpoint_2026-11-09\Before-gentle-relief-pass\Main Scene.unity`.
- After relief and ground repaint, the Unity terrain audit found nine 1024 × 80 × 1024 m tiles at their existing positions, with the existing reciprocal neighbor links. All 12 adjacent shared edges matched at mean 0 m and maximum 0 m difference across 65 samples per edge. See `Reports/Diagnostics/OUTER_TERRAIN_AUDIT_2026-11-09.md`.
- The existing slope-aware rural ground palette was repainted on the eight outer tiles after relief. This changes their alphamaps, not their dimensions or positions. Center palette authoring was not part of the outer-paint operation.
- Existing terrain colliders remained enabled and attached to the same TerrainData. The Main Scene, startup profile, authored town, and simulation/pathfinding bounds were not edited by this pass.
- The new Editor authoring utility imported and its menu was available, confirming Unity compiled that editor script. No C# compilation errors were observed. The Console still contains two Unity Package Manager `ScriptableSingleton already exists` errors and existing warnings; these were not caused or repaired here.
- No PlayMode runtime, frame-time, or terrain-streaming/performance test was run. Visual review was limited to the Unity Editor scene view; confirm from player-height camera views before treating runtime presentation as verified.

## Recovery checkpoint

A byte-for-byte safety copy of Main Scene, its `.meta`, and the eight outer TerrainData assets and `.meta` files was saved outside the project before authoring at:

`C:\Users\kenne\Documents\LandLedgersTerrainCheckpoint_2026-11-09\Before-gentle-relief-pass`

The checkpoint includes `SHA256SUMS.txt`. It remains a recovery copy and was not restored over the project.