# Economy Recovery Iteration 06

Date: 2026-11-15
Project: F:\Land & Ledgers
Unity: 6000.4.2f1

## Simulation

- Main Scene Small-town PlayMode run completed once at configured 250x (15,000 simulated seconds/real second).
- Start: 26,111.29 simulated seconds. End: 5,210,319.54.
- Duration: 60.00241 simulated days in 340.56 real seconds.
- 9 managed residents / 9 visible agents; zero route failures, zero capped rollover/short-tick frames, zero pending game time.
- 1,428 captured runtime entries: 914 warnings, 0 errors.
- Simulation paused at target; Editor returned to Edit Mode. Main Scene is clean and was not saved.

## Confirmed freight issue and repair

The prior same-day freight window search was insufficient. On day 7 the opening Millbrook flour shipment was already physically arrived, but the 08:00 freight drive ran before Ethan P7's scheduled reservations were released. The driver reported no five-minute window for the rest of that day and did not retry when later short-tick task progress freed time. This persisted throughout the run.

The new repair adds an arrived-unload-only retry after autonomous work progression on each simulated short tick. It uses the current day/minute, existing TaskAuthority and PersonScheduleTracker, and only starts unloading after reserving a legitimate free window. It does not advance wagon transit, perform unload work, move cargo, or settle freight. A focused regression verifies reserved refusal, same-day retry after reservation release, and no duplicate unload task.

## Economic evidence

- Glen's Bakery paid Millbrook Grain Co. 48 cents for one 10 lb flour lot (Millbrook flour 460 to 450 lb; supplier cash 0 to 48 cents). The accepted freight quote is 125 cents (25-cent base plus 100 miles at 1 cent/mile). Freight payment remains unsettled because unloading/receipt did not complete. The lot remains in wagon custody; Bakery received none of it. Bakery production later remained blocked at 10 lb on hand against the 12 lb recipe requirement. No duplicate payment or receipt was observed.
- The opening wagon staged at the authored operator-home plot, picked up the lot at Millbrook on day 4, returned on day 5 and the shipment reached Arrived at Bakery on day 6. This confirms staging did not require a commercial building/premises. The remaining blocked transition was unloading, not road departure or transit.
- Bakery emitted 10 distinct 24-loaf batch records and 10 matching wholesale transfers at 96 cents each (960 cents total). No duplicate batch IDs were observed. Later batches stopped when flour was insufficient; no output was logged without inputs.
- Livery B3 completed four physical General Store purchases of 7, 5, 5 and 3 horse-feed sacks at 15 cents each. Total 20 sacks / 300 cents: B3 debits matched B0 receipts, and all four lots were received after return. The authored conversion is 2 feed units per sack; 20 daily feed draws consumed exactly 40 feed units. The finite 20-sack order was exhausted after day 21; the day-1 and day-22 horse-risk warnings correspond to stock being unavailable before the first receipt and after depletion. No free feed was added.
- A real General Store procurement received 40u of potatoes from Ashford General Mercantile on day 37 at 13c/u: Store cash 40,117 to 39,597c and supplier ledger 21,430 to 21,950c, matching the 520c receipt. Household bread/potato/meat shortages and supplier refusals also remained visible; no unsupported inventory or cash was injected. A 12u tools_hardware delivery remained WAITING for receiving capacity and was not paid/booked. Felling, log hauling, sawmill production and sawmill-to-yard transfer each reported “no Sawmill business” in this Small-town runtime; this is missing scenario business data, not proof of a broken production chain.

## Warning review

Runtime warning categories: Incident 458; TaskDuration 341; SimulationDrivers 84; BusinessFeed 8; StoreRestock 7; FreightAgreement 3; AnimalCare 2; Felling, LogHaul, OffMapJourney, SawmillProduction and SawmillTransfer 1 each; six unclassified town-world validation warnings. TaskDuration lines refer to distinct task records and are emitted once per task under the existing diagnostics; they were not suppressed. Household and feed scarcity warnings were retained.

The Editor Console has one current error: the existing `Assets/Environment/Buildings/Building Animations/Single Story Business Building White Plaster Animation 1.anim` failed to load as corrupt/newer-version serialized data. The PlayMode capture contained zero errors. One transient Pipeline disposal error and two initial compile diagnostics from the new test's missing namespace also remain in historic Console records; the namespace was fixed, Unity recompiled successfully, and all fresh tests passed.

## Source and validation

Files changed in this repair pass:

- `Assets/Scripts/Domains/Economy/Businesses/Freight/FreightProductionDriver.cs`
- `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs`
- `Assets/Scripts/Tests/Editor/Economy/FreightProductionDriverTests.cs`

Validation: Unity compilation successful; `FreightProductionDriverTests` 12/12, `FreightScheduleReservationTests` 10/10, and `SimulationDriversSceneBindingTests` 4/4 passed; targeted `git diff --check` clean. No second PlayMode run was performed after the repair.

## Remaining verification

The next permitted PlayMode run should confirm the Arrived Millbrook shipment begins unloading after Ethan's same-day feed/work reservations release; completes the physical receiving gate; credits the exact 10 lb lot to Bakery once; and settles the accepted 125-cent freight charge exactly once. Also verify the carrier's later feed need after finite stockout when a legitimate supplier exists. Do not seed additional feed to manufacture recovery.

Evidence files in this directory: full runtime JSONL, 60 daily checkpoints, preflight and post-run Editor logs, separate preflight Console capture and error snapshots, simulation summary, and economic log analysis.

