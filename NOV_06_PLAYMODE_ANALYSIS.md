# Nov 6 PlayMode Analysis

## Test envelope

Fresh First Ledger NewGame; 100x (highest visible option); start 1870-01-01 / day 0; about 2m18 real time; stopped after day 226 (~1870-08-15). No seed was logged. Raw capture: `NOV_06_PLAYMODE_CONSOLE.log`. Unity Editor compilation before PlayMode: zero errors. Runtime capture: 4,991 warnings, zero errors/exceptions. The Console displayed 387 warning groups because identical messages were collapsed; raw warning logger records are counted here.

## Ranked findings

### P1 — Bakery cannot produce/store bread

Evidence: day 1 `Series2BakeryProductionDriver pre-check refused: notions 0u on hand, need 1u for 1 batch(es)`; `BakeryFuelBlocker` reports fuel 0, D-2 factor unset, no live fuel-yard intake, no dealer source, D-7 unresolved. A legitimate order for one notion was placed for day 15, but fuel remained unavailable. The fallback wholesale account plans/covers a batch, but there is no sellable `bake.bread-loaf` shelf lot and transfer is refused. Final state continues to have no planned production. Later Glen's budget reaches 150m at 25% capacity against a 180m batch.

Source: `SimulationDrivers.DriveBakeryWholesalePlanning`; `BakeryWholesaleDailyDriver`; `BakeryNotionsDailySupply`; `BakeryFuelBlockerDiagnostics`; development config D-2/D-7 fields. Most dependencies are owner decisions, not implementation defects: decide fuel route/conversion and wholesale quantity/cadence/pricing; then wire real business-cash procurement and work booking without treating in-transit stock as on-hand.

### P1 — Household retail supply ends after opening purchases

Evidence: H1 day 1 settles 6 bread for 30c and 2 lb flour for 58c; identified purchase receipts logged. Bread stock falls 18→12; flour to zero. Day 2 bread is out of stock, and other needs report no viable seller for preserved meat/food, potatoes, staple food, fuel wood. No later household purchase was recorded; final day summary `0 eaten, 18 missed, 0 purchases`. There are 4,000 meal-miss warnings (person/slot across 226 days). This is an observed system-level shortage, not evidence that every requested merchandise item should be sold. Bread's first missing link is actual bakery production (fuel + notions + labor), while multiple other needs lack matching eligible catalog, package definitions, or suppliers.

Source: `SimulationDrivers` household needs call chain, `GeneralStoreRuntimeManager` / `GeneralStoreTradingPort`, `DailyNeedsService`, household food inventory. Trace each item through identifier, offers, package conversion, settlement and custody; do not invent demand or pack sizes.

### P1 — Coupled trip moves without Ethan

Evidence: two warning entries say `No director or driver person resolved for the rider; the pair moves without Ethan.` Despite this, coupled transport started an 82-waypoint 2.2m/s route from `(14,47)` to exit `(47,95)` and logged exit arrival. The shipment proceeded through Millbrook pickup and return/delivery settlement. Thus source/runtime movement of the vehicle pair and the freight state chain ran, but the rider was not associated. The brief PlayMode session did not visually inspect the moving scene closely; mounted/seat/animation alignment remains unverified.

Source: `CoupledTransportMovementController.StartLeg` and the caller/identity passed from `OpeningTransportPresentationController`; inspect `WagonRiderPlacement`, `DriverSeat`, and existing presentation controller wiring. Resolve the assigned operator from the accepted dispatch/shipment identity before movement. Keep one entity identity and don't infer successful boarding from task completion.

### P1 — Freight journeys conflict with return/manifest callback

Evidence: day 4 pickup confirms the original Millbrook lot, one sack / 10 lb, wagon identity and off-map journey ID. Day 5 the `OffMapJourneyAuthority` says the wagon/person returned with “no cargo callback supplied, purchase manifest receipt remains on the record”; day 6 shipment service reports Bakery arrival; day 8 it credits the lot once and collects/posts exactly 125c once. This looks like two related journey/settlement paths with different completion timing. The first shipment completed in this run, but the callback/manifest seam could duplicate or orphan cargo under retry/save/load or second haul.

Source: `OffMapJourneyAuthority`, `FreightShipmentService`, `FreightProductionDriver` and `SimulationDrivers` freight advancement. Trace stable journey, lot custody and callbacks end-to-end before enabling repeat freight.

### P2 — Ethan hitch window is initially refused at rest boundary

Evidence: T34 was assigned to P7 but refused because P7 was Resting `[480-510]` and the work reservation asked `[480-490]`. It later started and dispatched (T35) in this same run; do not infer the total assignment-to-start delay from date-only output. `BusinessTaskStartWindow` catch-up mitigates skipped high-speed windows, but start timing must be instrumented with absolute time. Preserve PersonScheduleTracker and WorkTimeBudgetStore; add a focused high-speed regression around windows crossed by a 100x frame.

### P2 — B3 feed receipt custody identity fails

Evidence: 8 `BusinessFeed` warnings, including refusal after return: custody batch `business-feed:livery_freight_003:13` could not be retained because the transaction ID was duplicate or missing. Other warnings say B0 cannot offer 5–7 feed sacks at 15c with only 2 in stock; no cash/stock moved. A purchase attempt was also refused on a non-trading day. A0 then lost nutrition. The shortage is finite and legitimate; the missing/duplicate receipt transaction identity is a code defect.

Source: `BusinessFeedCustodyLedger`, feed procurement/receipt adapter, `SimulationDrivers` feed procurement. Preserve already-settled purchase identity and retry without buying again.

### P2 — Warning channel contains 504 routine completions

Raw warning-family counts: 4,000 FoodEconomy meal misses; 961 SimulationDrivers (504 `due events`, 225 domestic labor failures, 226 daily need summaries, 6 autonomous labor); 17 TownWorld visual-build timing; 8 BusinessFeed; 2 AnimalCare; 2 CoupledTransport; 1 FreightAgreement. 367 normalized warning-text signatures. Routine family-labor task completions are emitted as warnings, producing 504 warning records without representing failures. 225 domestic labor failures include actual budget refusals (e.g. 49/150 minutes remaining against a 61-minute meal-prep task) and should remain causal, but summary/duplicate adoption logs should not be warning severity. TownWorld timing is expensive (~330–400ms per visual rebuild) and repeated, but is performance evidence rather than a correctness exception.

Source: `SimulationDrivers.DispatchDueEvent`, domestic labor logging, `TownWorld` build timings. Fix severity/rate only where preserving real refusal visibility.

### P3 — Household demand and packaging remain incomplete

Day 2 has explicit refusals for potatoes, preserved foods/meat, staple food and fuel wood. Flour's authored 10 lb package did execute through actual trade, but this session did not verify every package class. Potato/salt package sizes and D-5/R1 remain unresolved. D-6 meal eligibility/preservation identity and household demand decisions remain open. Do not convert unknown units by assumption.

### P3 — Scenario/default and save/load not validated

Start UI offered First Ledger only; Open Frontier was not selectable. The active development config asset exists locally and already has Bakery equipment IDs, but this fresh run did not demonstrate Open Frontier. No meaningful save/restore was run. Repeat freight Gates A/B/C remain unresolved and no second haul was attempted.

## Successful evidence (state/log only)

- Bakery account fallback and planning called; no production.
- One flour purchase from Millbrook (48c) and one freight request/agreement/shipment.
- Glen physically arrived at Ethan's meeting location.
- T34 hitching assigned; A0 and wagon reserved; T35 dispatch authorized exactly once by log with absolute simulation time `315594s`.
- Coupled team traversed 82 path waypoints and reached the exit; two rider resolution warnings show Ethan was not in the operation.
- Millbrook lot pickup day 4, shipment return/delivery progressed, one lot receipt and 125c customer debit / carrier revenue post on day 8.
- First-day household bread/flour transactions show separate identified receipts and balanced seller/buyer cash deltas.
- No runtime errors or exceptions; eight mortality suppressions logged and no completed death seen.

## Next likely bottleneck

Fix operator identity handoff into coupled movement first, then validate visual rider/vehicle together. For food supply, the next blocking input is owner-authorized fuel route/conversion and legitimate production time/wholesale terms; notions alone will not unblock a bake.