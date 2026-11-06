# Muse Next Development Pass — Integrated Nov 6 Evidence

## Baseline

Develop from branch `integration/muse-regional-economy-2026-11-05`, initially at `055ed1c325954989e69d28a3eae52e1d89779302`, plus Muse's 48 commits through `8afd67887cb89fec30e2eb86575364f13e592ea7` and Codex Unity compilation repair `8403ea7123750a10ab49ebb15d7055ce07854e0f`. Use the final published integration SHA supplied by Kennedy after the accompanying handoff commit is pushed. Do not merge into main, force push, rewrite history, or overwrite Kennedy's local scenes, prefabs, ScriptableObjects, UI, fonts, music, or other authored files. Check Git status before editing and stage only task-owned files.

Unity 6000.4.2f1 HDRP. Local Unity-only asset `Assets/Core/Population/DevelopmentTownConfiguration.asset` already has Bakery equipment IDs `oven-chamber-1`, `kneading-table-1`, `proofing-rack-1`, `bake-peels-1`, `baker-hand-kit`; preserve its GUID and values. The Nov 6 source compile was repaired, but focused Unity tests were not executed because the Test Runner utility was off-screen and the CLI bridge package was absent.

## Verified integrated run

A fresh First Ledger NewGame (Open Frontier was not selectable) began 1870-01-01/day 0 and ran at the highest visible speed, 100x, for about 2m18, reaching day 226 (~Aug 15). No seed was logged. Full raw capture: `NOV_06_PLAYMODE_CONSOLE.log`; analysis: `NOV_06_PLAYMODE_ANALYSIS.md`.

The first freight shipment completed its logged state lifecycle: request, agreement, 125c quote, T34 hitch preparation, T35 dispatch at absolute simulation time 315594s, coupled 82-waypoint route to the map exit, Millbrook pickup day 4, return/delivery progression, lot credited once and 125c collected/posted on day 8. However, two warnings report `No director or driver person resolved for the rider; the pair moves without Ethan.` The brief run did not visually confirm seated rider alignment. Treat this as the highest-risk integration defect before claiming people-plus-vehicle travel.

The Bakery did not bake: day 1 refused on 0 notions (one-unit order for day 15), while fuel remained 0 with D-2 factor unset and no live yard/dealer source (D-7 design unresolved). Later Glen had 150 minutes at 25% capacity, below the 180-minute batch. Wholesale fallback planned/covered a batch but no shelf lot existed to transfer. Households completed only the first day’s identified purchase (6 bread at 5c and 2 lb flour at 29c, balanced cash/receipts); bread dropped 18→12, flour to zero, day 2 bread was out, no subsequent purchases, and day 226 showed 0 eaten / 18 missed / 0 purchases. The raw capture has 4,991 warnings, zero errors and zero exceptions: 4,000 food meal misses; 961 SimulationDrivers (504 routine due-event logs, 225 domestic-labor refusal logs, 226 daily summaries, 6 autonomous-labor); 17 TownWorld build timings; 8 feed; 2 animal care; 2 coupled transport; 1 freight agreement.

B3 feed handling reported a custody-ledger refusal for a receipt with duplicate/missing transaction ID after purchase had settled. A0 nutrition fell to 0.85. Preserve cash and inventory authorities; do not create feed.

## Ranked objectives

### 1. Repair and test rider identity in coupled freight movement

Inspect `CoupledTransportMovementController.StartLeg/Update`, `OpeningTransportPresentationController` dispatch-to-movement caller, `WagonRiderPlacement`, `PopulationPathingDirector`, `AgentMover`, and save re-derivation. Resolve Ethan from the accepted shipment/operator identity, ensure the mover's actual person/root is passed, board once at the configured DriverSeat, keep him seated while the coupled horse/wagon follow the authored path, and dismount safely on return. Do not guess seat offsets or change Kennedy's prefab transforms. Add focused integration tests for identity resolution, duplicate start, rider placement/re-derivation and failure refusal. In Unity, visually confirm Ethan, A0 and wagon move together, preserve world scale/rotation and return without duplicates.

### 2. Resolve legitimate Bakery fuel and operational work without policy invention

No fuel was delivered because `fuelDealerIntakeUnitsPerBakeryUnit` is unset and D-7 import execution is undecided. Present the exact choices for Kennedy: D-2 unit conversion and D-7 ordering/payment/persistence. Do not place purchase orders before those decisions. Meanwhile verify existing notions arrival/receipt idempotency and trace why day-15 order does or does not become usable stock. Implement a real baker person-time booking seam only if compatible with `WorkTimeBudgetStore` and existing task authority; never treat a 150-minute capacity as enough for a 180-minute batch. Add tests for refusal, arrival, stock consumption and retry semantics.

### 3. Reconcile freight journey / shipment callback timeline

Inspect `OffMapJourneyAuthority`, `FreightProductionDriver`, `FreightShipmentService`, `SimulationDrivers`, purchase manifests, stable journey IDs and save DTOs. The run logged return with “no cargo callback supplied” on day 5, Bakery shipment arrival day 6, unload start day 7 and one lot + 125c settlement day 8. Prove custody and settlement remain exactly once across save/restore, retries and failed callbacks. Preserve payment-after-delivery. Do not fabricate pickup, arrival or money.

### 4. Fix business-feed receipt identity and retry

Trace `BusinessFeedCustodyLedger` and its producer/consumer adapters. The paid feed receipt was refused because its custody batch lacked a transaction ID; ensure the stable purchase transaction ID is retained and a retry records stock without repeating buyer debit/seller credit. Keep genuine store-shortage and non-trading-day refusals visible. Verify B3 feed procurement, A0 nutrition, inventory/cash conservation and save restore.

### 5. Make the food economy continue from real supply

Trace H1's real bread/flour purchase through identified retail lot, cash, household inventory and meal use. Day 2 had no bread or valid sellers for several needs; then no repeat purchases across the rest of the run. Separate the missing bakery-production dependency from unimplemented item demand/producer authorities. Do not create daily purchases, fake bread or free food. Verify one repeat cycle only after legitimate production and wholesale account terms exist.

### 6. Repair repeated freight gates before promising a second trip

Keep Muse's known unresolved gates visible:
- Gate A: `StoreWeeklyReorderPlanner.OutstandingOrderedUnits` does not count Millbrook lots and `BuildInputNeeds`/`ResolveDesiredUnits` can misaccount repeat orders.
- Gate B: `HandleCompletedFreightMeetingTask` can terminally refuse temporary carrier-busy state.
- Gate C: `DriveOpeningHitchPreparation` chooses only the first accepted agreement and no code marks it fulfilled.
Add focused tests for sequential requests and stable state, then verify a second genuinely funded shipment. Do not broaden to regional supplier architecture.

### 7. Focused test runner and 100x scheduler regression

The Nov 6 compile succeeded but zero Unity tests executed. Fix the test-runner access/tooling blocker through an existing in-process/supported method; don't install unrelated packages or launch another Editor against the same project. Run the specific tests for coupled motion/rider, BusinessTaskStartWindow, freight dispatch/lot custody, bakery planning/inputs/wholesale, feed custody, retail purchases and work-capacity. Test a high-speed time step that crosses a task window and confirm the same task catches up once without violating rest, budget or exclusive occupancy.

### 8. Open Frontier registration / default is still absent

The live scenario picker exposed First Ledger only. Inspect the authored ScenarioDirector and registered assets in Main Scene, preserve First Ledger, and make Open Frontier selectable if existing scenario architecture supports it. S-1 default selection remains a distinct decision; do not silently make it the default. Validate both modes in Editor.

### 9. Preserve and inspect regional ordering/settlement

Run a bounded one-week test of daily vs weekly procurement, supplier stock, delivered-cost estimates, actual payments, receipt custody, and duplicate demand. Do not interpret a modeled freight cost as a carrier payment or automatic arrival as physical freight. Keep the regional towns off-map and preserve JourneyModel routes.

### 10. Save/restore after lifecycle fixes

Exercise one meaningful mid-trip save, including operator/rider, horse/wagon identities, shipment, purchase manifest and custody; restore twice; verify no duplicate journey, cargo, inventory, settlement or hidden Ethan. Also cover pending notions/order state. Do not claim save verification based on pure DTO tests.

## Owner decisions: do not invent

- D-1: wholesale quantity, cadence and final B2B flour price.
- D-2 / D-7: bakery fuel route/conversion and import order/payer/persistence execution.
- D-5/R1: potato sack and salt bag quantities.
- D-6: meal eligibility, preserved food/meat identity, and legitimate suppliers/producers.
- Bakery batch policy/inputs and live baker time booking; do not create a daily bread faucet.
- 120-second day, 250x replacing 25x, time-scaled movement, First Ledger popup suppression, young-worker classification, full town config audit and bottleneck register remain unimplemented unless separately authorized.

## Required validation and deliverables

1. First inspect `git status` and latest published integration SHA; preserve local dirty assets. Keep work in a dedicated branch or current authorized integration branch without merging main.
2. Compile in the actual Unity Editor, record exact errors/warnings, and run focused tests with real pass/fail counts.
3. Start a fresh Main Scene NewGame. Record scenario, seed if available, date, selected multiplier and elapsed wall time. Use a bounded 100x run, then step the rider/departure at a lower speed for actual visual evidence.
4. Capture full Console logs and group repeated warning causes with counts and representatives.
5. Reconcile buyer debit, seller credit, lot/source custody, inventory quantities, workload, and identity transitions. Test at least one lifecycle save/restore after repairs.
6. Commit only verified source/test changes and intended reports; preserve Muse's 48 commits and local authoring; push normally and verify remote SHA. Never force push.
7. Report implemented vs compiled vs EditMode tested vs PlayMode verified vs visually confirmed. First safe stop point: rider and vehicle identity/path integration is visibly correct with no duplicate operation. Continue to later objectives only as time and validated prerequisites permit.