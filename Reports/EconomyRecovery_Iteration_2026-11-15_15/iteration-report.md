# Economy Recovery Iteration 15 — 2026-11-15

## Simulation

- One PlayMode session was run on the real Small-town Main Scene using Unity 6000.4.2f1. The Pipeline connection dropped during its first monitor request and recovered without restarting Unity. The monitor attached after recovery; its measured window then covered the full 60 days.
- Speed250x measured 250× Normal = 15,000 simulated seconds per real second. Monitor start 28,120.31s; end 5,212,139.19s; 60.00022 simulated days in 340.11 real seconds.
- The monitor paused at target and Play Mode was exited. At completion: 9/9 residents managed and visible; no route failure, capped rollover/short-tick frames, or pending simulation time.
- Fresh gameplay callback capture: 1,568 entries (522 logs, 1,046 warnings, 0 errors). Checkpoints were recorded daily; full evidence is in this folder.

## Economy and comparison with iteration 14

The warning counts and key economic outcomes exactly matched iteration 14; no new gameplay defect was established.

- Bakery: 40 batches × 24 loaves = 960 loaves; 40 wholesale transfers × 96¢ = 3,840¢. Production stopped when its 40-unit opening fuel was exhausted. D-2 conversion and D-7 import execution remain unresolved, and no output was fabricated after fuel ran out.
- Flour: 120 lb opening stock + one 10 lb Millbrook lot received on day 9 + 35 locally purchased 10 lb sacks supplied the 480 lb used by production (12 lb/batch). The driver made one later actual local purchase, on day 41, of two more sacks for 58¢; that 20 lb remains unconsumed inventory. This is repeatable behavior, but the run still does not establish that buying reserve flour during a fuel outage violates an approved procurement rule. No policy change was made.
- Millbrook lot `aidlot-millbrook_grain_co:opening-flour-b1`: Bakery paid 48¢; supplier stock fell 460→450 lb and supplier cash rose 0→48¢. It was picked up and physically delivered; the lot was credited once to the Bakery, and the accepted 125¢ freight fee was charged to the payer and credited to the carrier.
- Horse feed: four real purchases moved 20 sacks for 300¢ total at 15¢ each. Later feed stockouts were legitimate scarcity.
- Food: 60 daily resolution events served five households, with 1,611 meals eaten and 0 missed. Day 1 recorded 18 meals during the partial opening day; each following daily record was 27 meals with no misses.

## Warnings and errors

Runtime warnings (1,046), unchanged from iteration 14:

- Incident 498 (95 open, 290 escalated, 113 cause-changed; 55 resolutions are informational logs)
- TaskDuration 406
- SimulationDrivers 110
- BusinessFeed 9; StoreRestock 7; FreightAgreement 3; AnimalCare 2
- One each: OffMapJourney, Felling, LogHaul, SawmillProduction, SawmillTransfer; Other 6

The OffMapJourney warning remained an intermediate walk to the valid staging point; the shipment later completed. The day-60 task-duration summary reported no open overruns. Genuine supplier stockouts and unavailable offers remain visible.

The runtime capture recorded zero application errors. A failed Pipeline request during reconnection logged an HTTP response disposal error before the monitor attached; this was a tooling error, not a game transaction error. The fresh Unity log slice contains repeated Unity Licensing entitlement HTTP 404 notices and 53 negative-scale BoxCollider warnings from reference objects. There were no C# compiler errors. Unity's current Console still reports two Editor errors, while the captured error entry corresponds to the Pipeline request error; these are separate from the zero-error gameplay capture.

## Repair and verification

No gameplay source change was justified. Iteration 14's unresolved flour-purchase policy remains uncertain; D-2/D-7 require owner decisions. No source, scene, prefab, or authored asset was saved or modified by this iteration.

Fresh compilation check: `compilationFailed=false`, no compiler errors; Unity is ready in Edit Mode. Focused EditMode suites passed 132/132 (0 failed, 0 skipped): CoupledTransportMovementController 7; FreightProductionDriver 12; HouseholdShoppingLoop 30; ShoppingTripDriver 53; OffMapJourneyAuthority 15; BakeryDriverFuelWiring 3; FuelDealerSupplyTransfer 5; BakeryFuelBlockerDiagnostics 7.

## Next iteration targets

1. Resolve whether post-fuel flour procurement is an accepted reserve policy or should be gated by actionable fuel availability; do not infer a rule from this one purchase.
2. Continue checking that task-duration warnings distinguish late open work from completed work under coarse ticks.
3. Watch for changes in household stockouts, feed orders, freight receipt, and incident causes.
4. Keep D-2 and D-7 blocked until authorized fuel rules are supplied.

## Evidence and workspace

Evidence: `simulation-summary.txt`, `console-playmode.jsonl`, `daily-checkpoints.csv`, `editor-run-and-posttest.log`, `editor-log-offset.txt`, `preflight-console-errors.json`, and `test-results.txt`.

The repository remains detached at `1b09c0ca4356e2eaddd3492a4e94f0a51f223a06` with extensive pre-existing dirty and untracked work. No reset, cleanup, commit, push, or merge occurred. The previously dirty Main Scene and prefab edits were preserved.
