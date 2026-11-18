# NPC Pathing, Road Movement and Freight Recovery — 18 November 2026

## Result

- Project: `F:\Land & Ledgers`, Unity `6000.4.2f1`.
- Branch: `codex/npc-pathing-road-freight-recovery-2026-11-18`.
- Starting checkpoint: `3d73d4f73953deb3d4b3e76384b8747bf195057c`.
- PlayMode scenario: Open Frontier, New Game, seed `1886`; displayed opening date Tuesday, March 1, 1870.
- The run progressed through at least day 37, using 250x for freight progression and Observe at inspection points. It was not a 60-day run.
- Unity compilation after the source changes completed with `compilationFailed=false` and no compiler errors. The Editor became stuck at “Hold on” during domain reload afterward and the Unity test pipeline disconnected; it was left alone and not force-restarted.

## Changes

`AgentMover` now turns its visible model toward the current route heading at a configurable maximum yaw rate, using real elapsed time for rotation. Authoritative location and travel distance still advance through the existing simulation clock; no separate position smoothing or random wandering was added.

Grid routes retain their pathfinder-selected cells, including road and footpath costs. Between validated route cells, moderate 20–150 degree turns are rounded with a short cubic curve whose control radius is a configurable fraction of a grid cell. Straight runs and near-reversals remain unchanged. The curve stays within the already validated corner cell. Door/entrance selection and route ownership were not changed in this pass.

The `DefaultPathingSettings` asset now sets pedestrian yaw to 240 degrees per real second (previous serialized value: 720) and corner rounding to 0.18 grid cells. Both are editable in the Pathing Settings Inspector; set rounding to zero to restore square grid turns. Unity serialization also wrote previously omitted fields at their existing code-default values; their behavior did not change.

## Freight PlayMode evidence

**VERIFIED — Millbrook flour shipment completed through runtime authorities.** Shipment `freight-shipment:freight-request:millbrook_grain_co:opening-flour-b1` used operator Ethan Boniecki (P7), horse A0, and `opening-freight-wagon`.

1. The bakery paid Millbrook 48 cents for one genuine 10 lb flour lot (`aidlot-millbrook_grain_co:opening-flour-b1`). The opening log records Bakery cash 35,000 → 34,952 cents and Millbrook cash 0 → 48 cents.
2. The shipment was authorized and dispatched using the existing freight path. The wagon physically travelled to Millbrook and back; the log records pickup of that lot at Millbrook on day 5, physical return to the town gateway, continuation along the authored road route, and return to the wagon staging point.
3. On day 8, the receiving pin reported an active route with 108 m remaining. This was a delayed intermediate state, not a permanent stall in this run. On day 9, the shipment credited the identified lot once to `bakery_001`.
4. The delivery settlement debited the bakery 125 cents, then posted 125 cents to `livery_freight_003` with shipment provenance. The freight agreement was fulfilled and dispatch released horse A0 and the wagon.
5. The same wagon subsequently completed three separate logging-camp supply deliveries (`campsup-0`, `campsup-1`, `campsup-2`), with 3, 2, and 2 units accepted at the sawmill. This verifies legitimate wagon reuse after the flour shipment.

Several camp-supply attempts made while the flour wagon was still bound were refused and refunded before departure. Those refusals prevented double-booking; they were not lost or free shipments.

**PARTIALLY VERIFIED — NPC movement quality.** The PlayMode run exercised real operator and wagon movement, off-map travel, gateway return, road return, staging, and delivery. The available screenshot is an aerial game view and does not resolve individual footstep turns. The new pedestrian curve/yaw presentation has passing EditMode coverage, but its visual appearance in a close camera view remains unverified. A close-view walking screenshot was not captured.

Captured Game view for scene context: `Reports/Playtests/Runs/20261118T0455Z-npc-pathing-freight-pass3/gameview-aerial.png` (SHA-256 `A0BEE1E4566DCA8732806D42BB1E540A8F3AFD871749A3B0F2B438C7F91F8AA4`). It is not evidence of the new turning presentation.

## Focused Unity EditMode tests

All executed suites passed:

| Suite | Passed | Failed |
| --- | ---: | ---: |
| `AgentMoverTests` | 19 | 0 |
| `FreightStalledShipmentRecoveryTests` | 7 | 0 |
| `MillbrookFlourFreightCustodyTests` | 10 | 0 |
| `FreightDispatchActivationIntegrationTests` | 5 | 0 |
| **Executions** | **41** | **0** |

These were separate EditMode runs, not 41 unique tests across one invocation. The movement suite includes right-angle curve bounds/endpoints, straight and reversing routes, plus existing clock-driven movement and arrival coverage.

## Console artifacts and limitations

Run artifacts: `Reports/Playtests/Runs/20261118T0455Z-npc-pathing-freight-pass3/`.

- `console-capture.zip` packages the exact unmodified text log, JSONL events, and metadata for GitHub. SHA-256: `58C7A05F2627180561D1364E79184CF48C0D2CAAAD5C75B082D2CF7603B65BC9`.
- `console-full.txt` preserves the captured original messages and stack traces in readable form. SHA-256: `F83EEDCABBB64AE2E5E78375AA69F5319DFB0820949D4660F552369BDA291333`.
- `console-events.jsonl` preserves sequence, UTC timestamp, level, original message, and stack trace. SHA-256: `B8FF2C04E562EA7908CF204120FE11CF9E374F1933BEEF1867B1CB070F5611F4`.
- `metadata.txt` records scenario, seed, approximate run start and evidence summary.
- `gameview-aerial.png` records the town Game view; it is not a close-up movement capture.
- The original logs contain trailing whitespace/newlines because stack traces are preserved verbatim; the packaged raw files were not normalized.
- The capture contains 2,000 ordered entries, sequence 44834–46833: 1,128 logs, 871 warnings, and 1 error. Unity's nearby retained counters reported 848 logs, 492 warnings, and 4 errors. These counters cover different windows and must not be combined or interpreted as a full lifetime Console total.
- The one captured error was a Unity AI Assistant relay/bus validation timeout after 19 attempts; it is an Editor tooling failure, not a game transaction error. The available raw capture begins at 04:41:27Z and ends at 04:55:24Z; it is the full captured window, not a claim of the entire Editor session.
- The capture includes unrelated startup and economy warnings; this pass did not suppress or repair them.

The earlier report `FREIGHT_PHYSICAL_DELIVERY_RECOVERY_2026-11-17.md` described a prior failed attempt. This run is a distinct successful transaction and does not erase that historical failure. The day-8 108 m receiving pin shows that retries can take time; verify longer-duration stalls and failed/cancelled recovery separately if they recur.

## Files in this checkpoint

- `Assets/Scripts/Domains/World/Pathing/AgentMover.cs`
- `Assets/Scripts/Domains/World/Pathing/PathingSettings.cs`
- `Assets/Scripts/Tests/Editor/Pathing/AgentMoverTests.cs`
- `Assets/Core/Pathing/DefaultPathingSettings.asset`
- This report and the run evidence files listed above.

Owner-local Main Scene, development-town configuration, Natural Wood prefab, terrain metadata, fonts, and asset-pack changes were excluded from this checkpoint.
