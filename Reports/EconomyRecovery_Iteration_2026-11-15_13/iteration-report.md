# Economy Recovery Iteration 13 — 2026-11-15

## Simulation

- One PlayMode entry was made on the real Main Scene / Small-town runtime. Unity's Pipeline connection dropped during the initial domain reload and the Editor was briefly unresponsive. It recovered automatically on port 7802; no restart or scene reset was used. The 60-day measurement began only after the monitor attached, so the unmonitored startup interval is not counted.
- Unity 6000.4.2f1, configured `Speed250x` = 15,000 simulated seconds per real second.
- Start 33,881.74 game seconds; end 5,217,922.75 game seconds; 60.00047 days in 345.43 real seconds.
- The monitor paused at the target, then Play Mode was exited. 9/9 residents remained managed and visible. No route failure, capped rollover/short-tick frame, or pending simulation time was recorded.
- Runtime callback capture: 1,568 entries, 1,046 warnings, 0 errors. The final Unity Console ground truth after EditMode testing was 0 errors, 1,104 warnings, 586 logs, with compilationFailed=false.

## Economy

- Bakery produced 40 batches × 24 loaves = 960 loaves and settled 40 wholesale transfers × 96¢ = 3,840¢. No duplicated production/output or revenue was found. It stopped after 40 days because its finite 40-unit opening fuel was exhausted. The log explicitly reports D-2 conversion unset and D-7 import execution absent; production correctly baked nothing afterward.
- Flour reconciled: 120 lb opening bin + 10 lb Millbrook lot + 35 locally purchased 10 lb sacks = 480 lb, consumed as 40 × 12 lb batches. Payments were 48¢ for the Millbrook lot and 1,015¢ for local sacks (1,063¢ total flour outlay). Notions and wholesale events also tracked one-for-one with the batches.
- Millbrook lot `aidlot-millbrook_grain_co:opening-flour-b1`: Bakery paid 48¢; supplier stock 460→450 lb and cash 0→48¢. It was physically picked up on day 4, returned with the wagon to the valid roadside staging point (14,47) on day 5, reached the Bakery receiving route, and was credited once on day 9. Bakery paid the accepted 125¢ freight charge and the carrier received 125¢ after payer debit verification. No commercial premises were required or used.
- Livery/feed: four real purchases of 7, 5, 5 and 3 sacks at 15¢ = 20 sacks / 300¢. General Store feed stock fell 20→0 across the transactions; each actual return credited business custody once. The Store later had no feed stock, so the remaining feed shortage and animal-condition decline were real scarcity.
- FoodEconomy logged all 60 daily resolutions for five households; days 10, 20, 30, 40, 50 and 60 each showed 27 meals eaten and 0 missed. Household shopping continued to leave needs open on genuine seller stockouts/no-known-seller outcomes; failures did not debit money or create goods.

## Previous repairs and current findings

- The prior freight-return repair is supported by this run: the shipment completed, and the earlier premature overdue-return warning pattern did not recur. Of six prior `[OffMapJourney]` warnings, five disappeared; the one remaining warning says the shipment is pending while the driver walks to the staging point. That is a real intermediate state, not a false overdue report.
- Runtime warning categories: incident warnings 498 (95 open, 290 escalated, 113 cause-changed), plus 55 resolved incident log events; task-duration 406; SimulationDrivers 110; BusinessFeed 9; StoreRestock 7; TownWorldValidation 5; FreightAgreement 3; AnimalCare 2; one each for Felling, LogHaul, OffMapJourney, SawmillProduction, SawmillTransfer and other. Warnings remain grouped/bounded; no per-frame warning flood or route-refusal flood was found.
- Task start refusals where a person was already occupied respected the one-person-one-activity guard. Shopping failures were actual counter stockouts or no viable seller/window; the need stayed open and no transaction settled. Freight and feed tasks later progressed where a window became available. No permanent reservation stall was evidenced in this run.
- Editor log slice recorded 106 negative-scale BoxCollider warnings from objects under the temporary `Prefab Environment/Building Prefab References` hierarchy and 10 Unity entitlement 404 notices. No compiler error or new animation import failure appeared in this run slice. These Editor messages are separate from the 1,046 runtime callback warnings. The preflight Console snapshot contained older tooling errors and the known White Plaster `.anim` load errors; they were not produced by this run, and final Editor Console ground truth was clean of errors.

## Repair decision

No gameplay source change was justified by this run. The repeatable freight concern is now resolved by actual shipment receipt and payer/carrier reconciliation. The main production blocker needs owner-authorized rules, not a code guess: `DevelopmentTownConfiguration.fuelDealerIntakeUnitsPerBakeryUnit` is explicitly zero/unset (D-2), and D-7 fuel-import execution is not defined. Setting a conversion, inventing fuel, or changing business balances would violate the existing authority. The remaining feed stockout likewise had no real Store supply to buy.

## Validation

All tests below ran in EditMode after the single PlayMode run; none were concurrent and no PlayMode test was repeated.

- `CoupledTransportMovementControllerTests`: 7/7
- `FreightProductionDriverTests`: 12/12
- `HouseholdShoppingLoopTests`: 30/30
- `ShoppingTripDriverTests`: 53/53
- `OffMapJourneyAuthorityTests`: 15/15
- `BakeryDriverFuelWiringTests`: 3/3
- `FuelDealerSupplyTransferTests`: 5/5
- `BakeryFuelBlockerDiagnosticsTests`: 7/7
- Total focused tests: 132/132 passed, 0 failed/skipped.
- Unity Editor final state: ready, Edit Mode, not compiling; `script recompile_status` completed with `failed=false`, `errors=[]`, `compilationFailed=false`.

## Files and handoff

No gameplay source or authored Unity asset was changed in this iteration. Added only `AgentScripts/CaptureEconomy60DayIteration13.cs` (external capture utility) and this evidence folder. The Main Scene was not saved. Existing local modifications and untracked files were preserved; no commit, push, merge, reset or cleanup was performed. Repository remains detached at `1b09c0ca4356e2eaddd3492a4e94f0a51f223a06` with extensive pre-existing dirty/untracked work.

Evidence: `simulation-summary.txt`, `console-playmode.jsonl`, `daily-checkpoints.csv`, `editor-run-and-posttest.log`, `editor-log-offset.txt`, and `preflight-console-errors.json`.

## Next permitted iteration should verify

1. Whether the one remaining staging-walk warning transitions to physical departure without false overdue reporting.
2. Bakery fuel procurement only after Kennedy supplies the D-2 conversion and D-7 import decision; until then the correct outcome is blocked production at stockout.
3. Household shopping attempts against current real Store stock and the remaining unmet needs, distinguishing physical stockout from supplier discovery.
4. Any change to 500x event ordering, cash, inventory or task reservations; this run was at 250x as requested.

