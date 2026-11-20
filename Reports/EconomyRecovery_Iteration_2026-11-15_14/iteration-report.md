# Economy Recovery Iteration 14 — 2026-11-15

## Simulation

- One PlayMode session was run on the actual Small-town Main Scene with Unity 6000.4.2f1. No second PlayMode entry was made.
- Configured Speed250x was 250× Normal (15,000 simulated seconds per real second). Measured start 26,029.34s; end 5,210,081.02s; elapsed 331.20 real seconds; 60.00060 simulated days.
- The capture paused at the target, then Play Mode was exited. At the end: 9/9 residents managed and visible; no route failure, rollover or short-tick cap, and no unprocessed simulation time.
- Fresh runtime callback capture: 1,568 entries (522 logs, 1,046 warnings, 0 errors). Full callback output is in `console-playmode.jsonl`; ten-day activity checkpoints are in `daily-checkpoints.csv`.

## Economic findings

- Bakery: 40 authorized batches × 24 loaves = 960 loaves, with 40 wholesale transfers × 96¢ = 3,840¢. Production stopped when its 40-unit opening fuel was exhausted. The fuel blocker continued to state that D-2 conversion is unset and D-7 import execution is undefined; no output was made after fuel ran out.
- Flour conservation: 120 lb opening stock + one received 10 lb Millbrook lot + 35 locally purchased sacks (350 lb) supplied the 480 lb used by 40 batches (12 lb each). The log then shows one additional day-41 local purchase of two actual sacks (20 lb, 58¢) from real General Store stock after fuel exhaustion; no further post-stockout flour purchases occurred. This paid stock remains legitimate inventory, not phantom output. Whether procurement should pause while fuel is unavailable is not established by this run and remains a targeted follow-up, not a confirmed defect.
- Millbrook: Bakery paid 48¢ for lot `aidlot-millbrook_grain_co:opening-flour-b1`; supplier flour fell 460→450 lb and supplier cash rose 0→48¢. The lot was physically picked up by the opening wagon, moved through the validated roadside staging point, and received once. The accepted 125¢ freight amount was charged to the Bakery and credited to the carrier; no commercial-premises prerequisite was involved.
- Livery feed: four actual purchases moved 20 sacks for 300¢ total at 15¢/sack. General Store stock was depleted by those sales and the purchased feed was received into the livery's inventory. Subsequent feed unavailability was genuine scarcity.
- Food: all 60 daily resolution records show five households served, 27 meals eaten and 0 missed per day (1,620 meals total). No direct meal-path purchases were recorded.

## Warnings and errors

Runtime warning categories (1,046 total):

- Incident: 498 (95 open, 290 escalated, 113 cause-changed; 55 resolutions were informational logs)
- TaskDuration: 406
- SimulationDrivers: 110
- BusinessFeed: 9
- StoreRestock: 7
- FreightAgreement: 3
- AnimalCare: 2
- Felling, LogHaul, OffMapJourney, SawmillProduction, SawmillTransfer: 1 each
- Other: 6

The single OffMapJourney warning was the driver still walking to the legitimate staging point while shipment status remained pending; the later trip completed. This is an honest intermediate state. TaskDuration warnings are mainly completed tasks whose observed active span exceeds nominal duration under coarse accelerated ticks; the day-60 summary reported 0 open overruns. No repeated route-refusal flood or permanent route failure was captured.

The fresh runtime capture had zero errors. The Editor log slice contains two Unity Licensing entitlement HTTP 404 messages and a Pipeline disposed-object message during the recovered connection; these are not gameplay or compile failures. The post-run Console ground-truth counter showed two Editor errors, while the captured Console error buffer returned no entries. Older White Plaster animation load errors were present in the separate preflight snapshot but did not recur in this fresh log slice. The fresh Editor slice also contains 53 negative-scale BoxCollider warnings from reference objects; no authored scene change was made.

## Repair and validation

No source repair was justified: the economic and route transitions reconciled, shortages remained honest, and the remaining flour/fuel policy question has no approved rule to encode. No gameplay source, scene, prefab or asset was changed.

Unity compilation state: ready, not compiling, `compilationFailed=false`; script recompile status completed with no errors. Focused EditMode tests passed:

- CoupledTransportMovementControllerTests: 7/7
- FreightProductionDriverTests: 12/12
- HouseholdShoppingLoopTests: 30/30
- ShoppingTripDriverTests: 53/53
- OffMapJourneyAuthorityTests: 15/15
- BakeryDriverFuelWiringTests: 3/3
- FuelDealerSupplyTransferTests: 5/5
- BakeryFuelBlockerDiagnosticsTests: 7/7
- Total: 132/132 passed, 0 failed, 0 skipped.

## Next iteration targets

1. Check whether the day-41 flour stock purchase is intentional inventory policy during an unresolved fuel outage; any change needs an approved rule and a focused production/procurement regression.
2. Continue to distinguish completed-task duration diagnostics from genuinely overdue open reservations.
3. Observe recurring household stockout decisions, feed replenishment, and the remaining incident warnings against actual task/transaction outcomes.
4. Keep D-2 and D-7 blocked until the owner supplies their fuel conversion and import-execution decisions.

## Evidence and workspace

This iteration's evidence is stored in this folder: `simulation-summary.txt`, `console-playmode.jsonl`, `daily-checkpoints.csv`, `editor-run-and-posttest.log`, `editor-log-offset.txt`, `preflight-console-errors.json`, and `test-results.txt`.

The repository remains detached at `1b09c0ca4356e2eaddd3492a4e94f0a51f223a06` with extensive pre-existing changes and untracked local files. This iteration added evidence only; no Git operation or cleanup was performed. Unity is in Edit Mode.
