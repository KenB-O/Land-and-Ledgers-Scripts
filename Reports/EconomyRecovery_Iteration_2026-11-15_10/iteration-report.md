# Economy Recovery Iteration 10 — 2026-11-15

## Result

One real Small-town PlayMode simulation completed 60.00028 simulated days at the configured 250x speed. Unity is left in Edit Mode. No gameplay source was changed: review of the observed blocks found real resource scarcity and no safe existing source from which to manufacture supply. The run and logs are preserved in this folder.

## Run evidence

- Unity: 6000.4.2f1; `Assets/Main Scene.unity`.
- Start: 23,701.13 absolute game seconds; finish: 5,207,725.54.
- Elapsed real time: 321.59 seconds; configured progression observed at 15,000 simulated seconds/real second.
- 9/9 managed residents and 9/9 visible agents at checkpoints.
- 0 capped rollover frames, 0 capped short-tick frames, 0 pending game seconds at finish.
- PlayMode callbacks: 1,049 warnings, 0 errors, 523 logs. The callback dump is `console-playmode.jsonl`; per-day measurements are in `daily-checkpoints.csv`; the monitor’s summary is in `simulation-summary.txt`.
- Final Editor state: ready, not compiling, not in PlayMode; Main Scene clean (`isDirty=false`). The Editor log slice is separate in `editor-playmode.log`.

## Previous repairs rechecked

The paid Millbrook flour freight completed end to end. Glen P3 purchased the real 10 lb lot for 48 cents (Bakery cash 35,000c to 34,952c; Millbrook cash 0c to 48c; Millbrook stock 460 lb to 450 lb). Freight quoted at 125c, charged only after delivery; the Bakery paid and received the lot once, and the freight business received the corresponding proceeds. Staging used the physically valid `operator-home-location-plot`; no commercial premises authorization was needed. P7 and horse A0 physically hauled it to Bakery B1. Its actual receiving point was reached on day 7, and day 9 settlement/receipt completed. At day 8 the authoritative route was still active with 108m remaining and a 34.9-second route estimate; it completed without duplicate payment or receipt. This confirms the earlier freight-route/receiving repair in live gameplay.

## Economy findings

- Bakery: 40 batches × 24 loaves = 960 loaves produced by day 40; 40 matching wholesale settlements of 96c each (3,840c total). No evidence of duplicate batches or revenue. Production then stopped because Bakery fuel stock reached zero. Days 41–60 retained the same due batch and correctly refused to bake without fuel; flour (20 lb) and notions (1 unit) remained. The log identifies the first failing input as fuel.
- Fuel is a real supply-chain limitation in this scenario, not a safe code-only repair: the live logs say no formed Sawmill was present, so no byproduct could reach the Fuel Dealer intake. The dealer-to-Bakery paid source also requires existing intake stock, a configured dealer price, and an authored `FuelDealerIntakeUnitsPerBakeryUnit` conversion. No alternate fuelwood seller offer or lawful stock source appeared. I did not add a dealer, fuel, conversion, cash, price, or free delivery.
- Household shopping: real stockouts repeatedly refused purchases; no payment or goods were created. There were 147 failed bread shopping attempts during the run, alongside shortages of other products and no-known-seller cases. Open needs remained diagnosable. These reflect available store stock and supplier limits; the run did not demonstrate a duplicate settlement, inventory creation, or a route failure as the cause.
- Horse feed: the opening supply was a finite 20-sack external-market purchase at 15c/sack (300c total), received by the Store and then physically bought by the livery in four trips totaling 20 sacks/300c. The livery’s feed package is two feed units, matching the horse’s two-unit daily consumption; this opening quantity therefore covered roughly 20 days. The stock and seller then ran out. No producer or continuing feed source was present, so this is genuine scarcity rather than grounds to create feed.
- Meals: the existing FoodEconomy resolver recorded 1,611 meals consumed and 0 missed across the run. This verifies consumption resolution, not that every household had a successful shopping/restock cycle.
- No systematic route starvation was observed in the sampled population checkpoints; the 60-day run retained all nine visible agents. No payment/receipt duplication was identified in the captured freight, wholesale, shopping, or feed flows.

## Console analysis

Application PlayMode warnings grouped by prefix: Incident 498; TaskDuration 406; SimulationDrivers 110; BusinessFeed 9; StoreRestock 7; Other 6; FreightAgreement 3; OffMapJourney 3; AnimalCare 2; and one each for Felling, FreightPickup, LogHaul, SawmillProduction, and SawmillTransfer.

Incident/SimulationDrivers warnings mostly document open unmet household needs and genuine product stockouts/no offer. TaskDuration warnings are one-per-terminal-task records (`completed-overrun`), not 406 simultaneously stuck tasks; the daily TaskDuration summaries reported zero open overruns. The records include completed or cancelled tasks and short placeholder estimates (for example, travel estimated at 1 minute with a 15-minute active scheduling quantum). This warrants calibration of task-duration diagnostic inputs, but the run does not establish lost task progression and I made no speculative change to the diagnostics. StoreRestock warnings report no profitable upstream offer and unchanged stock. The Bakery fuel refusals after stock depletion are daily ordinary logs, not warning-level errors.

The Editor log has two Unity licensing entitlement API 404 notices and native extension lookup messages; they are not application PlayMode callback errors. Startup town validation reported zero errors and 15 warnings, separate from the simulation callback totals. Full log evidence is preserved so environment/editor messages are not conflated with gameplay output.

## Validation after the run

- Unity recompile request: `up_to_date`; no new compilation required and no source was changed.
- Focused EditMode suites executed serially after PlayMode: PopulationPathingProfessionRhythmTests 31/31; FreightStalledShipmentRecoveryTests 7/7; FreightShipmentServiceTests 22/22; FreightProductionDriverTests 12/12; ShoppingTripDriverTests 53/53; HouseholdShoppingLoopTests 30/30. Total: 155 passed, 0 failed.
- One preliminary Test Runner request used an unsupported filter type and was rejected before test execution; the corrected suite requests then ran. This is not counted as a test failure.
- No second PlayMode run was performed.

## Remaining confirmed constraints

The Bakery cannot continue production after the real fuel reserve is exhausted without an authorized fuel source and configured unit conversion. Household bread and horse-feed demand can exceed the finite scenario inventory/supply. Resolving these requires actual scenario supply decisions (formed/operating source businesses, valid products, prices, stock, and conversion/transport), not a runtime patch that mints goods. Completed-task duration diagnostics also merit a focused follow-up if their observed timing quanta are unintended; this run did not prove a gameplay stall.

## Files

No production files changed. Iteration instrumentation and evidence are retained under `AgentScripts/CaptureEconomy60DayIteration10.cs`, `AgentScripts/InspectIteration10EditorState.cs`, and this `Reports/EconomyRecovery_Iteration_2026-11-15_10/` folder. These are local untracked additions and were not staged or committed.
