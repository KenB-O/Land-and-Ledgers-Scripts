# Economy Recovery Iteration 09 — 2026-11-15

## PlayMode run

- One real `Assets/Main Scene.unity` PlayMode session, Small-town, **60.00082 simulated days**, speed `Speed250x` / multiplier 250. The configured rate measured **15,000 simulated seconds per real second**.
- Clock: `27,223.18` to `5,211,294.27` game seconds; elapsed real time 340.92 seconds.
- 9/9 managed residents and 9/9 visible agents. No capped rollover frames, capped short-tick frames, or pending simulation time.
- Runtime capture: `console-playmode.jsonl`; day checkpoints: `daily-checkpoints.csv`; run summary: `simulation-summary.txt`. `preflight-console.json` and `editor-playmode.log` are separate, so stale Editor messages are not counted as runtime failures.
- Runtime errors: **0**. Runtime warnings: **1,025**. Unity was returned to Edit Mode after the one run.

## Economy findings

- **Paid Millbrook flour shipment did not reach the Bakery.** Glen P3 paid Millbrook Grain Co. 48c for its actual 10 lb lot (`460→450 lb` supplier flour; `0→48c` supplier cash). The accepted freight quote is 125c. Ethan P7 completed preparation/dispatch, the wagon staged via `operator-home-location-plot` and returned from Millbrook on day 5. The shipment arrived at Bakery B1 on day 6. Its receiving gate repeatedly pinned at 1/3 and 2/3, then delayed/retried; at day 60 it was still in wagon custody. No Bakery receipt or 125c freight settlement occurred. No duplicate charge or inventory credit was observed. The staging path worked without commercial premises, consistent with the owner's clarification that freight can be loaded from any physically valid location.
- **Confirmed route ownership gap repaired locally:** schedule routing protected task-directed travel only while `AgentMover.IsRouteActive`. A receiver could reach the Bakery between freight checks, then a home/work schedule could replace the arrived destination before the freight authority consumed it. A freight-specific destination claim now persists through the arrival observation; it is released when physical receipt is confirmed, or cleared when a route has ended unsuccessfully before retry. This is a source-confirmed race consistent with the observed repeated arrival pins, but the captured run did not record the exact transform/destination transition. The live post-repair shipment outcome is therefore **not verified** in this iteration.
- The route callback also discarded the doorway fallback's detailed progress reason. It now preserves route state, actor/target positions, remaining route distance and ETA in the existing bounded freight diagnostics, so a future run can distinguish active progress from a stalled route.
- **Bakery production and wholesale:** 34 logged batches produced 24 loaves each (816 loaves total); 34 corresponding wholesale lot settlements/transfers of 96c each (3,264c aggregate) were recorded. The batch and transfer counts match; no duplicate production/wholesale event was found in the captured logs. Actual recipe input checks remained enforced. The separate 10 lb Millbrook purchase was not used as Bakery stock because its freight receipt never occurred.
- **Meals:** all 60 daily household meal-resolution entries reported 5 households served; aggregate 1,611 meals eaten and 0 missed by that resolver. This does not prove all household shopping needs were fulfilled.
- **Feed:** the Store received 20 actual horse-feed sacks for 300c on day 1; horse-feed draws were recorded. The run later recorded no upstream quote for `animal_feed` and an at-risk horse warning when registered feed stock was unavailable. No feed was fabricated. Replenishment remains a genuine supply limitation unless a supported seller/offer is found.
- Bread stockouts caused some household shopping attempts to return empty-handed with needs still open. These are recorded as real stockouts, not completed purchases. Several other household needs had no known purchasable offer. Store restock refused items where no profitable supplier offer or receiving capacity existed; these outcomes did not create stock.

## Console review

Warnings grouped by leading category:

| Category | Count | Assessment |
|---|---:|---|
| Incident | 492 | Persistent household need incidents and cause changes; many reflect genuine unavailable offers or repeated plan transitions. |
| TaskDuration | 395 | Mostly completed historical travel/shopping tasks reported as overrun (often a 15-minute active tick against a 1-minute expected duration); no open overrun remained in the day-60 summary. |
| SimulationDrivers | 104 | Daily household need and shopping stockout/refusal summaries; real needs remain open. |
| StoreRestock | 10 | Unprofitable/unavailable upstream offers and a tools-hardware delivery waiting for receiving capacity. |
| BusinessFeed | 8 | Livery feed task windows and stock availability; not all feed needs could be fulfilled. |
| FreightAgreement | 3 | Contact/task window and freight agreement lifecycle messages. |
| AnimalCare | 2 | Horse at-risk notices when registered source stock was unavailable. |
| Other | 11 | Five town validation summaries and one each for SKU availability, OffMapJourney, Felling, LogHaul, SawmillProduction, and SawmillTransfer. |

No warning class was suppressed. The Editor log slice also contains Unity's existing repeated failure to load `Assets/Environment/Buildings/Building Animations/Single Story Business Building White Plaster Animation 1.anim`; this is not a runtime error from the captured simulation and was not modified.

## Repair and verification

Files changed by this iteration:

- `Assets/Scripts/Domains/Population/PopulationPathingDirector.cs`
- `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs`
- `Assets/Scripts/Tests/Editor/Population/PopulationPathingProfessionRhythmTests.cs`

The freight receiver now owns its destination through the arrival check; ordinary scheduled routing resumes after the handoff releases that claim. The route-failure path clears an ended claim before retrying from the actor's current position. Door-fallback progress details are retained rather than replaced with a generic “physically travelling” reason.

Validation after the repair:

- Unity script recompile: **completed**, `failed=false`, zero compiler errors.
- EditMode tests: `PopulationPathingProfessionRhythmTests` **31/31**, `FreightStalledShipmentRecoveryTests` **7/7**, `FreightShipmentServiceTests` **22/22**, `FreightProductionDriverTests` **12/12**, `ShoppingTripDriverTests` **53/53**, `HouseholdShoppingLoopTests` **30/30**. Total **155 passed, 0 failed**.
- Targeted `git diff --check`: passed for the three changed source/test files.
- Final Editor status: ready, not compiling, Play Mode stopped. No second PlayMode session was started.

## Remaining issues / next verification

1. **Millbrook receipt still unverified after the new route-claim repair.** The day-60 run preceded the edit, as required by the one-run rule. Next iteration should inspect P7's authoritative destination, route state, remaining distance/ETA, and route claim while the same receiving task is pending; then verify one lot receipt and 125c settlement exactly once.
2. The 1,025 warning volume remains material. Household scarcity/stockout and task-duration diagnostics dominate; this pass did not suppress them or change their policy.
3. The White Plaster animation asset load error remains an Editor-side issue.

The pre-existing worktree was preserved; no scene/prefab changes, commits, pushes, merges or resets were made. Unity is in Edit Mode.

## Workspace/evidence state

- Repository remained at detached `HEAD 1b09c0ca`; the extensive existing dirty worktree was left intact.
- Run instrumentation: `AgentScripts/CaptureEconomy60DayIteration09.cs` (outside `Assets`, used only for this bounded capture).
- Evidence files: `Reports/EconomyRecovery_Iteration_2026-11-15_09/console-playmode.jsonl`, `daily-checkpoints.csv`, `simulation-summary.txt`, `editor-playmode.log`, `preflight-console.json`, and this report.
