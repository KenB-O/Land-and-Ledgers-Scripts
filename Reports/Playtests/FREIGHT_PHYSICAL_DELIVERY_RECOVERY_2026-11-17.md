# Freight and Physical Delivery Recovery — 17 November 2026

## Scope and source state

- Unity project: `F:\Land & Ledgers`, Unity `6000.4.2f1`.
- Development branch: `codex/freight-physical-delivery-recovery-2026-11-17`.
- Starting and current source baseline: `4615119d24592a47cb38341fdaebfecc0eaba4b3`.
- The owner-authored Main Scene, prefabs, terrain, fonts, and local asset packs were not changed by this pass.
- Unity ended in Edit Mode. No scene was saved.

## Findings

The opening-flour shipment exposed two distinct physical movement failures in different PlayMode observations. They must not be conflated:

1. The preserved 60-day run `20261117T220225Z-e0f61aea` completed its clock interval, but did **not** complete this shipment. Its raw Console records show agreement acceptance and dispatch authorization for `freight-shipment:freight-request:millbrook_grain_co:opening-flour-b1`, followed by T109 completing while Ethan was still away from the wagon staging point. The shipment remained `Planned`, cargo stayed in its prior custody, and no pickup, cargo transfer, delivery, or freight payment occurred. The same planned shipment then exclusively bound `opening-freight-wagon`; subsequent logging-camp freight requests were correctly refused to prevent double-booking. Relevant evidence: `Reports/Playtests/Runs/20261117T220225Z-e0f61aea/console-full.txt` entries 2925–2999, 3172, and repeated wagon-binding refusals thereafter. This run's overall 60-day calendar status is PASS; its freight objective is FAILED.

2. A later focused live attempt did physically stage, board, travel to Millbrook, return, and reach the bakery premises. The final receiving route then remained active at the same 108 m distance and ETA across successive day pins. At day 30 the shipment was abandoned after three receiving-gate pins; one unit was quarantined in wagon custody. This is a failed delivery, not a successful receipt. The contemporaneous Unity `Editor.log` was observed at `C:\Users\kenne\AppData\Local\Unity\Editor\Editor.log`; the relevant event sequence is around lines 1108207–1119140. The 48¢ flour purchase is recorded as paid to Millbrook. The accepted freight quote was 125¢, payable after successful delivery. The evidence does not show that the freight charge settled, nor does it establish a refund or physical return of the quarantined lot.

The live failure therefore includes a staging/dispatch handoff problem and a separate freight-receiving route stall. The source change in this pass addresses route ownership interference and staging cleanup, but the later observation still showed pending staging, and the receiving-route failure remains unresolved. A successful complete shipment and reuse of the same wagon have **not** been verified.

Source inspection also found a concrete route-ownership bypass: household domestic activities call `BeginGridRoute` directly inside `PopulationPathingDirector.TryReachDomesticActivity`, bypassing the common `TrySendAgent` ownership guard. A cooking or chores route could therefore replace the active freight staging/receiving route. The final patch defers those domestic routes while freight owns the person's movement. This is covered by the focused pathing regression, but a final PlayMode run could not confirm the observed shipment now completes.

## Changes in this pass

- `WagonRiderPlacement` now selects a reachable, walkable approach cell within the seat boarding tolerance and verifies that an active route actually ends at the assigned approach cell.
- `PopulationPathingDirector` retains the assigned wagon-seat approach as movement ownership, prevents direct/scheduled unrelated route requests from replacing an active freight receiving route, and releases the approach when the dispatch operation terminates.
- `OpeningTransportPresentationController` exposes staging reason diagnostics and explicit approach release.
- `SimulationDrivers` records specific staging refusals and releases the staging approach for failed, cancelled, or interrupted dispatch tasks.
- Focused regressions cover reachable approach selection, unrelated active routes, and route deferral for staging and receiving claims.
- Domestic activity route startup now respects freight staging and receiving ownership even though it uses a direct route-start path.

These changes improve ownership and diagnostics; they do **not** establish that the shipment now completes. The latest bounded live attempt remained pending during staging, and the receiving route issue still needs a targeted investigation.

## Verification

Unity recompile reported `up_to_date` with no script compilation required. Unity Editor status: ready, `playMode=stopped` (Edit Mode), Unity `6000.4.2f1`.

Focused EditMode results on the current source:

| Suite | Passed | Failed |
|---|---:|---:|
| `PopulationPathingProfessionRhythmTests` | 31 | 0 |
| `WagonRiderPlacementTests` | 11 | 0 |
| `FreightDispatchActivationIntegrationTests` | 5 | 0 |
| `FreightStalledShipmentRecoveryTests` | 7 | 0 |
| `FreightShipmentServiceTests` | 22 | 0 |
| `FreightOffMapDepartureTests` | 7 | 0 |
| `FreightDispatch` filter | 18 | 0 |
| **Total test executions** | **101** | **0** |

The 18-test dispatch filter overlaps the five dispatch-activation tests; 101 is the sum of suite executions, not a unique test count.

The preserved 60-day raw Console export is under `Reports/Playtests/Runs/20261117T220225Z-e0f61aea/` and includes `console-full.txt`, `console-events.jsonl`, daily summaries, entity snapshots, metadata, and final state. `console-full.txt` SHA-256: `AA581F6F75FBFD4030F3B0CA76BB9CD0AD73E484FC324F3CE019A3FE5D8E5905`. Its recorded totals are 3040 logs, 1223 warnings, 0 errors, and 0 exceptions. These totals cover the whole simulation and are not freight-only counts. The later focused run has no separate complete exported Console package; its relevant records remain in the local Unity `Editor.log` path above.

Two earlier PlayMode startups made the Editor temporarily unresponsive during scene initialization and later recovered without a forced restart. After the final route-ownership regression was added and compiled, one more bounded PlayMode start entered startup callbacks but never advanced beyond frame 1 (`playerLoopTicking=false`). Pipeline status/stop requests initially timed out. The stop command later returned `Exited play mode`; a subsequent Editor status confirmed `playMode=stopped`, `compiling=false`, and Edit Mode. No force restart was attempted. No post-fix complete freight shipment was observed.

## Remaining required work

1. Determine why a valid staging approach continues to report pending or loses movement progress under the production tick, even when a direct staging readiness probe can succeed.
2. Determine why the return journey's receiving route remains pinned at 108 m with an active route and unchanged ETA, and ensure failed routes can retry or enter a recoverable delayed state without abandoning cargo prematurely.
3. Verify the exact payment/refund state and cargo custody after failed delivery; do not infer settlement from the accepted quote.
4. Complete one live shipment through supplier pickup, wagon custody, town return, physical receiving, buyer inventory receipt, exactly-once freight settlement, and release of Ethan, A0, and the wagon. Then verify a subsequent legitimate shipment reuses that wagon.

## Publication status

Source and focused tests are prepared for a scoped checkpoint on `codex/freight-physical-delivery-recovery-2026-11-17`. Publication status is recorded in the Git handoff; the branch must not be represented as published until the commit and remote SHA are verified.
