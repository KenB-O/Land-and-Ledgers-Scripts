# Final Unity Integration Verification — 2026-11-15

## Tested revision and environment

- Integration revision: `muse/final-integration-2026-11-15`, `b83f136a096261decd8c676fc634ca984ab4209d`.
- Unity: 6000.4.2f1; project: `F:\Land & Ledgers`.
- The final integration was checked out on `codex/final-integration-verification-2026-11-15` after ancestry checks. No merge to `main` or push occurred as part of verification.
- One actual Main Scene PlayMode run advanced about 54 simulated days at Extreme (250x Normal, 5.76 simulated seconds per game day). It exceeded the requested ~30-day interval. Unity was returned to Edit Mode; Main Scene was not saved.

## Compilation and tests

- Fresh Unity script compilation completed with no errors after test-source compatibility corrections.
- Campaign C EditMode selection: 316/316 passed.
- Campaign B EditMode selection: 206/215 passed; 9 failed. The failures were three path-format assertions, five cash-clamp logging expectations, and one production-caller assertion. These remain real failures in this Unity run; they are not classified as pre-existing.
- No PlayMode test assembly was run.

## Runtime observations

- The actual scenario HUD showed Open Frontier on March 1, 1870. The speed menu confirmed Extreme at 250x Normal.
- All eight active regional terrain objects had enabled Terrain and TerrainCollider components, matching non-null TerrainData references, and 1024 x 80 x 1024 dimensions. The active Main Scene terrain references resolve to assets under `Assets/Asset Packs/EnvironmentDevelopment/RegionalTerrain3x3`.
- Horse A0 was fed on all 54 daily records; reported nutrition remained 1.00. A legitimate five-sack feed purchase settled once with matching business cash and stock changes, and the feed was received by the livery. Some 20-sack feed receipts were refused because storage capacity would be exceeded.
- Daily food reports recorded 27 meals eaten and zero missed across five households per day (1,458 reported meals total). Lot-level inventory reconciliation was not captured.
- A physical opening freight wagon return and shipment arrival at the Bakery were recorded. A later unloading attempt failed on a long route and the shipment was delayed. Completed flour receipt at the Bakery was not established.
- No active household shopping trip was available at the inspection checkpoint. Save/Continue during an active trip was not tested.
- DEF-067 is inconclusive: the run did not provide corresponding legacy and Phase C household purchase/receipt records for a day-by-day charge comparison. Missing logs alone do not establish that duplicate charges did not occur.

## Errors and unresolved issues

- Full Console export: `C:\Users\kenne\AppData\Local\Temp\LL-final-integration-console-full.json` (1,508 entries, no dropped entries): 741 logs, 766 warnings, 1 error.
- The error reports that `Single Story Business Building White Plaster Animation 1.anim` may be corrupted or serialized by a newer Unity version.
- Bakery coverage reported 40 fuel units while its production pre-check reported zero available and left the batch planned. This gating discrepancy remains unresolved.
- `Single Story Business Building White Wood.prefab` is absent from this checkout, although recovery scenes retain references to its former GUID. No prefab was restored or modified.

## Scope and local preservation

This checkpoint includes this report and reviewed test-source compatibility edits. Five locally modified TMP font SDF assets, nine regenerated root terrain `.meta` files, 106,674 untracked files, and other unrelated local data were excluded and left untouched. The terrain `.meta` GUID changes were generated during Unity import; the tracked Main Scene references the original GUIDs currently resolved by local Asset Pack copies. Publishing the new GUIDs without remapping the scene would break those references in a clean checkout, so the metadata edits remain local. No gameplay code was changed. Unity remains in Edit Mode.
