# Economy Playtest and Recovery Iteration

Date: 2026-11-14/15 (local Editor log timestamps are UTC)
Project: F:\Land & Ledgers

## PlayMode run

- One Small-town run completed: 60.00092 simulated days, DayIndex 0 to approximately 60.
- TimeManager was set to 250x: 15,000 simulated seconds per real second; configured normal day is 86,400 game seconds. Expected 60-day elapsed real time is 345.6 seconds.
- Captured Play Console span: 00:45:53–00:52:29 UTC (6m36s including setup/observation). The monitor's `RealSeconds=0.12` field is an instrumentation error; use TimeManager conversion and Console timestamps instead.
- 9/9 managed and visible residents at start and end; no route failure in the monitor's final movement status; no capped rollover/short-tick backlog.
- Raw console capture is retained in `console-startup-delta.json`, `console-live-chunk-01.json` through `console-live-chunk-04.json`, `console-during-play.jsonl`, and `unity-editor-full.log`. `console-before.json` is the pre-run snapshot. Unity was exited from Play Mode after the target was reached.

## Console findings

The de-duplicated Play capture has 1,614 entries: 631 logs, 982 warnings and 1 error.

- The one error is the opening freight wagon refusal: `livery_freight_003` has no assigned commercial premises; residential staging is correctly rejected. This is a real, unresolved configuration/land-rights blocker. No unauthorized staging fallback was added.
- Twenty-five warning entries were false positives: successful `[V1] capacity consulted at settle` `Fits` results contain the word `Unknown`, which the broad due-event classifier mistook for failure. The classifier now excludes this informational record; a focused regression test covers both the success and real-failure cases. No Play rerun was made, per instruction.
- 53 capacity-at-settlement records appeared in the captured log. The log does not provide a complete per-trip money/lot/return reconciliation, so this is not represented as 53 completed household shopping loops.
- 446 warning lines were household supply incidents: 89 opened, 42 cause changes, 315 seven-day escalations, 49 resolved. Repeated needs include seller stockouts/no matching SKU or unavailable shopping windows. These remain open where the economy has no valid offer; no goods or money were created.
- 326 TaskDuration warnings concern distinct task records; examples show 1-minute travel tasks resolving on a 15-minute activity tick. These were retained as diagnostics, not suppressed: whether this tick-granularity overrun causes consequential schedule delay remains a follow-up verification item.
- 53 BoxCollider negative-scale warnings point to mirrored objects under `Prefab Environment` building/prop reference hierarchies. They are editor reference objects and no gameplay collision failure was established; owner-authored assets were left untouched.
- Other warnings include Store restock stockouts/receiving-capacity waits, repeated P7 time-window refusals, a legitimate opening shipment staging failure, and two horse-feed risk events after the authorized feed stock was exhausted. No warning was globally suppressed.

## Economic observations

- FoodEconomy emitted 60 daily resolutions: 1,611 meals eaten and zero missed across the recorded days (day 1 was partial at 18 meals; later days report 27/day for five households).
- The Store placed and received finite purchased goods with supplier lots and paid receipts; captured `[StoreReceipt]` records include actual stock and cash transitions. Store purchases, household buys, bakery wholesale and suppliers all remained within existing authorities.
- Bakery produced five Series2 batches (24 loaves each) and settled five wholesale lots to the Store at 96c each (480c total recorded revenue). The separate staged Bakery chain also completed five 24-loaf batches from real transferred inputs. Those are two live production paths drawing from the same opening input stocks; this run observed their separate outputs but did not establish that the parallel throughput is intended. Bread aging/write-off and the repeated stale wholesale lot show this needs a focused production-authority review; no speculative gate was added here.
- Bakery production became flour-blocked after the opening 120 lb was consumed. A 10 lb paid flour lot remained in the freight pipeline through day 60; the opening carrier shipment did not depart because it lacked authorized commercial staging premises. The 10 lb did not satisfy the 12 lb batch recipe, so production and the due wholesale account remained blocked. This is a real dependency failure, not fabricated scarcity.
- The General Store purchased 20 feed sacks from a finite external offer; Ethan physically returned 7 sacks to B3 and the business recorded the receipt. Later feed shortages led to two observed horse-risk warnings, while subsequent supplier/Store feed offers were genuinely unavailable.
- Opening setup also reported no formed Sawmill business, so forestry, hauling and sawmill production drivers correctly skipped. No production or sale was claimed for those systems.

## Repair and regression validation

Changed this iteration:

- `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs` — do not classify successful shopping capacity evidence as a due-event failure warning.
- `Assets/Scripts/Tests/Editor/Orchestration/SimulationDriversSceneBindingTests.cs` — regression test keeps successful capacity evidence informational and real aborted/failed transitions warning-worthy.

Unity recompile completed with `failed=false`, `compilationFailed=false`, zero compiler errors. Focused EditMode results:

- `SimulationDriversSceneBindingTests`: 3 passed, 0 failed.
- `ShoppingTripDriverTests`: 53 passed, 0 failed.
- `HouseholdShoppingLoopTests`: 30 passed, 0 failed.

No PlayMode test was started after the 60-day run. Editor status after tests: ready, not compiling, Play Mode stopped (Edit Mode). No scene was saved by this iteration.

## Preserved prior work / remaining findings

The modular-building assignment remains untouched. Its doorway traversal is still unverified and nine building tests remain pending, as previously reported. The prefab/scene working tree contains pre-existing owner and development changes; this iteration changed only the two source files listed above and did not reset or clean anything.

Remaining gameplay blockers include authorized freight staging premises/route, the stalled flour freight receipt, feed availability after finite stock exhaustion, Store stockouts for household goods, task-duration overrun follow-up, and the live dual Bakery production-path question. No further PlayMode run was performed.
