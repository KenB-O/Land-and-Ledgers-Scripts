# Economy Recovery Iteration 08 — 2026-11-15

## Simulation

- Ran one Small-town PlayMode session for 60.001 simulated days at the configured 250x multiplier. The measured rate was 15,000 simulated seconds per real second; elapsed real time was 328.74 seconds.
- Game time advanced from 24,202.34 to 5,208,293.61 seconds. There were 9 managed and 9 visible residents, no runtime errors, no capped rollover or short-tick frames, and no remaining pending game time.
- Unity was stopped after the run and is in Edit Mode. No second PlayMode session was started.
- The complete runtime console capture is `console-playmode.jsonl`; 10-day checkpoints are in `daily-checkpoints.csv`. `editor-playmode.log` contains the Unity Editor log from the saved pre-run byte offset. `preflight-console.txt` is kept separately from the run output.

## Confirmed gameplay findings

- **Millbrook flour freight did not complete delivery.** The bakery paid 48 cents for one 10 lb sack; Millbrook stock changed 460→450 lb and its cash changed 0→48 cents. The lot was picked up on day 5 and returned to town in wagon custody. The shipment reached `bakery_001` on day 7, but physical receiving pinned at 1/3 and 2/3, then failed and retried every four days. On day 60 it remained in wagon custody. There was no Bakery receipt, no credited flour lot, and no freight-fee settlement. The accepted 125-cent quote was not charged. No duplicate payment or inventory credit was observed.
- The Bakery completed 10 batches of 24 loaves and wholesale transfers through day 10. Thereafter it had 10 lb flour against a 12 lb batch requirement and correctly refused production. This is a real input shortage, made worse by the undelivered paid lot; the recipe requirement was not relaxed.
- Daily meal resolution ran for 60 days: 1,611 meals recorded, zero missed by that resolver, with 5 households served on each day. This does not imply all household shopping needs were fulfilled.
- Livery feed purchases remained real transactions. For example, Ethan P7 bought 7 sacks for 105 cents (livery cash 20,000→19,895; Store cash 50,227→50,332), returned with the purchased lots, and the livery received them. A later 5-sack purchase settled 75 cents and was also received.

## Repair made

The freight receiver's station is serialized on the Bakery prefab at local `(1.81, 0.946, 3.56)`, outside its authored floor polygon (whose Z maximum is `1.15`) but inside the building footprint. It is marked as a non-interior activity. The old receiver treated every non-interior point as an exterior grid destination without first requiring that its actual cell be a navigable exterior cell or falling back to the authorized doorway. The runtime cell was not recorded in the pre-repair run, so this geometry mismatch is confirmed while its exact cell classification during that run remains unverified. The callback also replaced detailed route refusal/progress information with the same generic “physically travelling” text.

The receiving path now:

- Checks a non-interior activity point against the active grid. A point outside the navigable exterior uses the existing authored-front-door route only when the destination has that authorized door; otherwise it fails honestly.
- Preserves the actor's current origin until the route has actually started. Previously the destination building was written before `TrySendAgent` chose the route's origin-building context.
- Requires the actor's recorded destination to match the receiver before confirming activity-point arrival.
- Retains bounded route/position details in the existing receiving reason so a future run can distinguish route progress from a repeated stale state.

No freight, property, inventory, cash, wagon, prefab, or scene data was fabricated or changed. Cargo custody, physical arrival, and shipment settlement authorities remain in place.

## Validation

- Fresh Unity script recompile: completed, `failed=false`, zero compiler errors.
- `PopulationPathingProfessionRhythmTests`: 29/29 passed, including blocked exterior receiving fallback and failed-route origin preservation.
- `FreightProductionDriverTests`: 12/12 passed.
- `FreightShipmentServiceTests`: 22/22 passed.
- `ShoppingTripDriverTests`: 53/53 passed.
- `HouseholdShoppingLoopTests`: 30/30 passed.
- `git diff --check`: passed for the three edited code/test files.
- Final Editor status: ready, not compiling, Play Mode stopped.

## Console and remaining verification

- The 60-day runtime produced 1,497 entries: 580 logs, 917 warnings, and 0 errors. Warning categories were Incident 458, TaskDuration 341, SimulationDrivers 84, BusinessFeed 8, StoreRestock 7, unprefixed 6, FreightAgreement 3, OffMapJourney 3, AnimalCare 2, and one each for Felling, FreightPickup, LogHaul, SawmillProduction, and SawmillTransfer. The run had fewer warnings than the prior recorded 1,025-warning iteration, but this variation does not establish that the earlier route repair worked. No warnings were globally suppressed in this pass.
- Post-test Editor Console reports one current error and 77 warnings. The repeated error is Unity's failure to load `Assets/Environment/Buildings/Building Animations/Single Story Business Building White Plaster Animation 1.anim` (“File may be corrupted or was serialized with a newer version”). The file is an existing, unmodified binary-looking asset; it is outside this freight repair and was preserved.
- The route-origin ordering defect and invalid exterior-station handling are repaired and EditMode-tested, but **the repaired path has not been exercised in PlayMode**, as this iteration forbids a second PlayMode entry. The next authorized 60-day iteration should verify that the same paid Millbrook lot reaches the authored Bakery doorway, is received exactly once, and that the 125-cent freight obligation settles exactly once. It should also verify that the recorded route detail changes from active travel to arrival rather than repeating the same position.

## Files changed by this iteration

- `Assets/Scripts/Domains/Population/PopulationPathingDirector.cs`
- `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs`
- `Assets/Scripts/Tests/Editor/Population/PopulationPathingProfessionRhythmTests.cs`
- Evidence captured under `Reports/EconomyRecovery_Iteration_2026-11-15_08/`

The existing worktree contains many unrelated authored and development modifications. They were preserved; no commits, pushes, merges, resets, or scene/prefab edits were made.
