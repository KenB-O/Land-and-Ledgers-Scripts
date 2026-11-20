# Economy recovery iteration — 2026-11-15

## Run

- Actual Main Scene / Small-town PlayMode, one run only.
- Start: game time 29,320.71 s; 1870-03-01; 250x = 15,000 simulated s / real s.
- End: 5,213,375.83 s; 60.00064 simulated days; 342.13 measured real seconds; 1870-04-30.
- 9/9 managed residents and 9/9 visible agents at checkpoints; no route failure, capped rollover/short-tick frames, or pending game-time backlog recorded.
- PlayMode log monitor: 1,463 entries, 575 Log, 888 Warning, 0 Error. There were 60 meal-resolution records: 1,611 meals eaten, 0 missed.
- Unity exited Play Mode and is in Edit Mode. No second PlayMode run was started.

## Prior repair verification

The prior iteration's due-event classifier repair remains in source. This run had zero false due-event warnings for successful `capacity consulted at settle` evidence (none were captured). The direct regression passed again: `DueEventFailureClassifier_DoesNotPromoteSuccessfulCapacityEvidenceToWarning`.

## Findings

No new C# exception, null reference, missing-reference runtime failure, `StartNotWalkable` or movement-route failure was captured during the 60-day window. Ordinary households and meal resolution continued. The captured window included actual supplier deliveries and receipts, 5 cash-settled Bakery wholesale lots in the first days, and repeated paid feed purchases/physical receipts for the livery; those records are within their respective authorities.

Confirmed persistent blockers:

- **Freight staging:** the Millbrook flour request and shipment were created and priced, but the wagon could not plan an exit because `livery_freight_003` has no authorized commercial premises. No pickup, delivery, or payment occurred. This is an ownership/staging prerequisite, not a safe code fallback; residential property remains correctly rejected.
- **Bakery flour:** a paid 10 lb lot remained in freight custody without a scheduled arrival throughout the run; the Bakery reported it needs physical receipt and stopped the relevant production/wholesale path. The 10 lb lot is below the recipe's 12 lb flour requirement. No inventory was credited early.
- **Store and household shortages:** the finite supplier system reported repeated no-offer/no-stock cases (bread, meat, cheese, flour and other goods) and receiving-capacity waits. Shopping failures remained unpaid and open. These are genuine availability/capacity conditions; no goods or cash were manufactured.
- **Animal feed:** real feed sacks were bought and received by B3; stock later ran out and the horse-risk warnings reflected the real shortage.
- **TaskDuration:** 326 warning records were emitted, mostly `completed-overrun` for terminal tasks (e.g. 1-minute travel recorded as active for a 15-minute tick, or tasks already complete by the next daily diagnostic). These are not open stuck tasks; daily summaries reported zero open overruns. They do flag coarse observation/short-tick timing and remain useful evidence, so this pass did not suppress them or alter simulation timing.
- **Incident warnings:** 446 entries (89 open, 42 cause changed, 315 escalated, 49 resolved) describe continuing household shortage/trip conditions. They are bounded daily incident reporting, not console spam without state changes.
- **World validation:** validation runs reported zero errors but repeated legacy/fallback service-anchor and land-offer warnings when weekly expansion rebuilt visuals. Owner building and scene authoring were left intact.
- **Bakery production paths:** the run still showed the existing Series2 and staged production paths using shared opening inputs. This is a confirmed unresolved architecture question; no safe authority choice was available in this pass, so neither path was disabled.

Unity's retained preflight Console also contains older entries from before the run: a compile error in `LLModularBuildingAssemblyEditor.cs` (`Vector3Int * float`), later superseded by a successful current compilation; an opening-wagon staging error; and repeated failed loads of `Single Story Business Building White Plaster Animation 1.anim`. They are not counted as errors in the captured PlayMode window. The animation asset was not rewritten during this economy pass.

## Validation

- Current Unity compile status: completed, `failed=false`, `compilationFailed=false`, zero compiler errors.
- `TaskDurationDiagnosticsTests`: 48/48 passed.
- `SimulationDriversSceneBindingTests`: 3/3 passed.
- `ShoppingTripDriverTests`: 53/53 passed.
- `HouseholdShoppingLoopTests`: 30/30 passed.
- Total focused Editor tests executed this iteration: 134 passed, 0 failed.
- No source or gameplay-data repair was justified from the new run without altering authorized property, inventory or task-time rules. No gameplay files were changed in this iteration.

Raw evidence is retained alongside this report: `simulation-summary.txt`, `daily-checkpoints.csv`, `console-playmode.jsonl`, and `console-unity-delta.json`.
