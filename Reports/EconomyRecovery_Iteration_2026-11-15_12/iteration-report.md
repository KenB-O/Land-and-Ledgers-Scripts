# Economy Recovery Iteration 12 — 2026-11-15

## Run

- Unity 6000.4.2f1, actual Main Scene / Small-town runtime.
- One bounded PlayMode run completed 60.00250 simulated days (23,571.63s to 5,207,787.83s) in 282.78 real seconds at configured 250x (15,000 simulated seconds/real second). The run paused at target and Unity was returned to Edit Mode.
- 9/9 residents were managed and visible. No movement route failures were captured; no capped rollover/short-tick frames and no pending simulation seconds at the end.
- Runtime callback capture: 1,577 entries, 1,052 warnings, 0 errors. The Editor Console's retained buffer later showed 3 current errors and 88 warnings, including repeated asset-import errors and the earlier CLI invocation error. These post-run Editor errors are not counted as gameplay errors in the captured PlayMode run.

## Economic observations

- Bakery: 40 distinct batches × 24 loaves = 960 loaves; 40 wholesale settlements × 96¢ = 3,840¢. No duplicate-output/revenue evidence. Production stopped on day 41 when actual fuel ran out; logs say no fuel and no baking. Remaining flour at that point was 12 lb after 35 purchased sacks (350 lb) plus 142 lb initial, less 40 × 12 lb recipes (480 lb). Flour and notions inputs reconciled. Fuel shortage is a real supply-chain limitation, not a reason to synthesize fuel.
- Millbrook flour shipment completed physically: the 10 lb lot purchased for 48¢ was moved from its legitimate pickup through the wagon's physical return and received once by the Bakery on day 10. The accepted freight charge was 125¢; Bakery debit and carrier receipt reconciled. Loading/staging used a physically valid roadside/operator-home plot at (14,47); this run did not require commercial staging premises.
- Livery feed purchases were four real transactions (7, 5, 5 and 3 sacks, 20 total at 15¢ each; 300¢), with actual Store stock consumed and goods entering business custody on return. Feed was consumed at 2/day; later stockout and declining horse nutrition were genuine scarcity.
- FoodEconomy resolved 60 daily cycles for five households; after the opening day it recorded 27 meals/day and zero missed meals.
- Household shopping still reports genuine shortages/no known seller and real Store stockouts. Failed purchases did not settle or create goods.  No free stock or funds were introduced.

## Repairs made after the one PlayMode run

- Fixed false freight return-overdue warnings: the daily drive no longer classifies a return as overdue on its estimated due day before that day's pickup/return can occur, and it avoids warning while the physical route or driver journey is active.
- Fixed retry handling for an already active coupled physical return: the controller now recognizes the same journey's active return before applying the near-supplier precondition. This prevented legitimate returning wagons from being reported as unable to begin their return.
- Added focused coverage for active physical-return identity/driver state.
- Reconciled `TownWorldExpansionTests` with the current regional-opportunity readout wording rather than obsolete “Parcel authority” text.

The freight warning repair was validated by EditMode tests only; the one-run limit means it was not revalidated in a second PlayMode session.

## Console review

Captured warning categories were led by bounded incidents (498), task-duration diagnostics (406), household unmet-need/no-seller and due-event stockout summaries (110), with smaller business-feed, restock, freight, animal-care and world-validation groups. No StartNotWalkable or route-refusal flood appeared. The 406 completed-overrun records were terminal task-duration diagnostics, not evidence of retained reservations. Genuine supply refusals remain visible. No global warning suppression was added.

The Unity Editor also repeatedly failed to deserialize the tracked `Assets/Environment/Buildings/Building Animations/Single Story Business Building White Plaster Animation 1.anim` (“may be corrupted or was serialized with a newer version”). This recurred outside gameplay during Editor/test activity and remains unresolved; no authored animation asset was overwritten. A Unity Pipeline command typo and one Pipeline `/api/exec` response error were tooling errors, not runtime game errors.

## Validation and final state

- Fresh Unity script compilation: completed, zero errors.
- EditMode tests after the repair batch: `CoupledTransportMovementControllerTests` 7/7; `TownWorldExpansionTests` 9/9; `FreightOffMapDepartureTests` 7/7; `OffMapJourneyAuthorityTests` 15/15; `FreightProductionDriverTests` 12/12. All completed; no PlayMode test was rerun.
- Final Editor status: ready, not compiling, domain reload complete, Play Mode stopped; most recent Test Runner suite completed 12/12.
- Scene was left in Edit Mode and was not saved or reset. No commits, pushes or merges were made.
- Working tree contains extensive pre-existing user changes and generated/untracked artifacts; they were preserved. This iteration added a capture utility under `AgentScripts/` and evidence under this report directory.

## Evidence files

- `simulation-summary.txt`
- `console-playmode.jsonl` (complete callback capture)
- `editor-playmode.log` (bounded Editor log slice)
- `daily-checkpoints.csv`
- `preflight-console-errors.txt`

## Remaining issues

1. The White Plaster animation clip import/serialization error remains a confirmed Editor issue; repair requires a known-good source or a safe asset recovery path.
2. Bakery fuel sourcing and livery feed replenishment eventually exhaust legitimate supply; no authorized replenishment chain was demonstrated for the Bakery fuel.
3. Household food shortages/no-seller conditions remain where current real supplier stock is unavailable.
4. Freight return-warning repair needs verification in a future permitted PlayMode run.
