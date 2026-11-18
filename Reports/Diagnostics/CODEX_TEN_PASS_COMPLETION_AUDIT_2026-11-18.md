# Codex Ten-Pass Completion Audit and Recovery — 2026-11-18

## Executive result

**All ten passes are not complete.** Passes 1 and 2 meet their scoped acceptance evidence. Pass 3 verified the freight transaction but not the improved close-view pedestrian presentation. Passes 4–10 remain partial. No broad feature rewrite was attempted. This iteration repaired a local business-fixture placement, reran its focused Unity Editor suite, and later verified a successful PlayMode startup plus all 13 runtime fixture presentations. Visual fit and NPC use remain unverified.

## Repository and workspace

- Project: `F:\Land & Ledgers`; Unity `6000.4.2f1`.
- Branch at audit start: `codex/player-economic-participation-2026-11-18`.
- Published starting HEAD: `2c229d3efb6552e793735280742d379a07b2459e` (matched `origin` before this report).
- The authoritative owner-local `DevelopmentTownConfiguration.asset` remains modified along with Main Scene, Natural Wood house prefab, terrain metadata, fonts, and a large set of untracked assets and diagnostics. None were staged or discarded.
- A backup of the configuration exists outside `Assets`: `Temp/DevelopmentTownConfiguration.before-fixture-floor-repair-2026-11-18.asset.bak`, SHA-256 `6177226C5353EE3BA81A260AD82C6E3A14540D75A8E9D425F954881DD50713B7`.

## Ten-pass evidence

| Pass | Status | Checkpoint and evidence | Remaining acceptance gap |
|---|---|---|---|
| 1 — Strategy camera | **COMPLETE** | `4a35f53bc72213f302bac5b032099eb50baf5a68`; Unity compile clean; `StrategyCameraZoomSensitivityTests` 6/6; real Main Scene PlayMode checks for close inspection, zoom/pan/rotation and authored FOV preservation. | Automated input was used; this was not an unassisted owner mouse/keyboard session. |
| 2 — Furniture calibration | **COMPLETE** | `3d73d4f73953deb3d4b3e76384b8747bf195057c`; Unity compile clean; 121 focused EditMode tests passed; three-house placement/orientation screenshots after floor/scale correction. | Physical NPC use was outside this pass and remains Pass 6 work. |
| 3 — Movement and freight | **PARTIAL** | `a9436c9fa653b6d61a3078e0c953b0dfc8accaa5`; 41 focused test executions passed. PlayMode delivered the Millbrook flour lot to bakery inventory, debited 125¢ from bakery, credited 125¢ to carrier, released the team and reused the wagon for three camp deliveries. | Freight is verified; close-view evidence for the pedestrian turning/steering improvement is absent. |
| 4 — Business interiors | **PARTIAL** | `b1c256b2aa85b34cc1658c4cb2c4d1d646911cd7`, follow-up `e3e6c01837e888e2a0ccf6bbc8963a6063e8c72d`. Unity startup reached `Game slice ready` (14 buildings; log says 98 generated people; 1 NPC General Store; no player-owned General Store). The first fixture run rejected Fuel Dealer barrel 02 as overlapping barrel 01. This iteration moved barrel 02 from local `(4.8, 0.8, -3.8)` to `(3.4, 0.8, -3.8)` using Unity `SerializedObject`; `DevelopmentBusinessInteriorInitializerTests` passed 7/7 afterward. | The recovered post-adjustment startup reached `Game slice ready`; a runtime query found all 13 fixtures under the four configured business identities. No fixture placement rejection appeared in that startup. The captured Game view was aerial and does not verify interior fit or reachability. The local config is not published. |
| 5 — Furniture commerce | **PARTIAL** | `740f43e0a9d6f3288772d8b8a01b4181a278b806`; `FurnitureStorePurchaseG3Tests` 28/28, `StoreRestockServiceTests` 18/18, named procurement regression 1/1; Unity compilation succeeded in that pass. | No real household purchase, physical wagon delivery, installation, or resulting capability change verified. The production furniture delivery driver still performs loading/transit/unloading synchronously. |
| 6 — Household life | **PARTIAL** | `547b532d909eeb38013653235df0f006bb1f524c`; focused suites 54/54. Actual PlayMode ran about three simulated days at 10×; completed bed-use history was recorded for residents in all eight households. | Cooking/fuel consumption and physical seated dining were not verified; unsupported chair movement and open meal-prep tasks remain. |
| 7 — Forestry and lumber | **PARTIAL** | `559833d7e44b1128211b865528e1eaf9c0b2e863`; Unity compilation clean; 135 focused tests passed across sawmill, task budget, forestry, transfer, settlement, and lumber buyer suites. | No live logging-to-lumber-to-yard-to-purchaser chain; PlayMode startup stalled during reload. |
| 8 — Construction | **PARTIAL** | `62578ff5836f2eb481928e62c227426c45444238`; NPC construction suites 21/21 and 20/20. Player timer-only shell construction was changed to fail closed before spending cash/materials or reporting completion. | No complete physical construction project. Player queue is explicitly disconnected from committed labor/material custody and physical housing registration. One queued-shell suite was blocked by the White Plaster animation asset load failure. |
| 9 — Blacksmith and maintenance | **PARTIAL** | `3e6d64ccacac8ae05621a0a4f4c8bac4938c93af`; source changes cover skill registration and failed-repair conservation, with `BlacksmithTests` and task-catalog regressions added. | No verified live smith workshop, sourced forging inputs, product sale, repair, or individual horseshoe replacement. Exact final test-run totals were not present in the available handoff evidence. |
| 10 — Player economy | **PARTIAL** | `2c229d3efb6552e793735280742d379a07b2459e`; 15 acquisition-continuity tests and 1 player/NPC mode test passed. Acquisition now checks retail-runtime readiness before transfer; developer control switching avoids duplicating Kennedy. | No live property/business acquisition, supply order, hiring, or subsequent NPC recognition verified. |

## This iteration: fixture repair and Unity result

- The authoritative local config already placed all business fixtures at the authored floor elevation `Y=0.8`; the remaining live rejection was spatial overlap between Fuel Dealer barrel 02 and barrel 01.
- Changed only `fuel-dealer-barrel-02.localPosition.x`, `4.8 -> 3.4`, retaining its Y/Z, prefab, identity, floor, and all other configuration.
- EditMode: `DevelopmentBusinessInteriorInitializerTests` **7 passed, 0 failed** after the adjustment.
- The first PlayMode launch after the adjustment stalled during an asset refresh (`Untracked: 28.477 s`); the Editor later recovered naturally. That same New Game startup later reached `Game slice ready` (14 buildings; log reported 98 generated people; one NPC General Store). The opening settlement reconciliation reported zero errors and one warning (the sawmill had no fixed premises; Unity Editor log at `C:\Users\kenne\AppData\Local\Unity\Editor\Editor.log`, latest run lines 1838152–1838193). A read-only runtime query found all 13 fixture presentation markers under the intended bakery, fuel dealer, general store, and lumberyard identities; no fixture rejection appeared in that startup. The Game view capture at `Temp/business-fixtures-startup.png` is an aerial view and does not prove interior fit or use. The known White Plaster `.anim` load error still appears in the Editor log. The normal `editor.playmode stop` command returned `Exited play mode`; a later Editor status reported `playMode=stopped`, `compiling=false`, and `ready`. No force restart or scene save was attempted.
- The pre-correction run had the barrel-02 collision and sawmill-premise warning; the later post-correction run had the sawmill warning but no fixture-placement rejection.
- No C# source changed in this audit iteration; a fresh compile was not triggered. The Unity test assembly ran successfully. A full EditMode suite was not run here.

## Defects and next implementation order

1. **Unity/runtime follow-up:** inspect the configured interiors in Game/Scene view and verify worker/customer reachability for the 13 registered fixtures. Investigate the White Plaster `.anim` load failure independently; do not suppress it in fixture tests. The last normal stop returned `Exited play mode`, and a subsequent status confirmed the Editor ready in Edit Mode.
2. **Pass 4:** verify the 13 authored business fixtures visually and test employee/customer reachability. Current prefab audit found no gameplay behaviors on audited prop models; decorative fixtures must remain separate from storage, workstation, service, and stock authorities. Sawmill has no assigned premise; the carpentry business was associated with a residence in the earlier survey, so neither was furnished as commercial premises.
3. **Pass 5:** replace the synchronous furniture delivery call with a real existing freight lifecycle, then prove paid purchase, cargo custody, arrival, installation, and capability change. Stove supplier/pricing and storage-fixture binding remain unavailable.
4. **Pass 6:** only count sleep/meal/dining work after actual arrival; establish real fuel retrieval, stove consumption, and dining fallback. Current reports explicitly leave cooking and seated dining unverified.
5. **Passes 7–9:** build focused controlled runtime scenarios for the full log/lumber chain, construction, and blacksmith; do not infer production from registration or passing unit tests.
6. **Pass 8:** connect player commissioning to the existing construction executor, real labor, materials, tools, procurement, and housing completion before re-enabling player construction.
7. **Pass 10:** run one acquisition through the live authority and verify seller compensation, asset/obligation continuity, and NPC recognition.

## Known caveats

- The current New Game log's `Generated people=98` is a runtime startup observation, not proof that the authored resident roster contains 98 people. Reconcile the expected configuration, generated roster, and requested development-town size before treating this run as an acceptance scenario.
- A successful freight pass does not verify all movement presentation or every freight cancellation/recovery state.
- The historical Unity report of 162 EditMode failures remains **unclassified**; this iteration did not rerun the full suite or compare it with a reproducible baseline.
- Licensing entitlement 404s and the Unity AI Assistant relay error were previously observed as tooling messages; they are not counted here as gameplay defects without causal evidence.
- No changes in this report claim the entire ten-pass program is complete.

## Publication scope

This audit report is the only portable file prepared for publication from this iteration. The owner-local configuration asset (including substantial existing authoring and local-only prefab references), Main Scene, House Natural Wood prefab, terrain metadata, fonts, commercial/third-party assets, and generated/untracked diagnostics remain excluded and untouched by staging. The local configuration fix is not visible to Muse through Git; its exact path and change are recorded above.
