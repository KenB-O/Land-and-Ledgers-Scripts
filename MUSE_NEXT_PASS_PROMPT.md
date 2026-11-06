# Land & Ledgers — Muse Follow-up: Make Food and Freight Executable

**Studio:** Deadscripts Interactive
**Unity:** 6000.4.2f1 HDRP
**Source baseline:** `integration/muse-regional-economy-2026-11-05`
**Pinned integration checkpoint:** `1d297672fbd47aeb9c7526befc0bc4cb2dba3e03`. Verify the published branch HEAD at handoff; this checkpoint contains the source repair, Bakery opening-equipment configuration hunk, and stress analysis. The Nov 5 food/freight branch ancestry includes Muse HEAD `1c74ebd307452b5dc99659b5c30543bd5a0befe0` (13 commits after published `f60a21dc4869b27da27b7a1ad6bd038c70f28706`).

## Mission

Continue from this integrated source and turn the demonstrated food and freight foundations into executable, causally valid gameplay. Start by verifying the current branch/HEAD and reading `STRESS_TEST_ANALYSIS.md` plus `COMPLETE_STRESS_TEST_CONSOLE.log`. Do not repeat the long stress test before fixing its first failures. Implement legitimate repairs in coherent commits, run focused tests after each slice, and proceed to the next independent objective when one requires owner policy. Never report source-only functionality as Unity-verified.

The reference PlayMode run selected **First Ledger**, started at 1870-01-01, reached day index 335 (about 335 simulated days) at 100x in about three real minutes, and returned to Edit Mode. Unity Console showed 0 errors and 882 warning groups at stop; the raw capture has 7,740 warning calls and 1,355 informational calls. The fresh town had nine residents, five households and four businesses. Opening bread was 24 loaves, all four households bought six each on day one, then no further bread was available. Day 335 reported 0 eaten, 18 missed meals and 0 purchases. This is the evidence baseline, not a required date-by-date deterministic replay.

## Non-negotiable authority and preservation rules

- Preserve business/household/supplier cash separation; inventory ownership, lot identity and provenance; person-time and equipment reservations; shipment/contract identities; idempotency; and save owners.
- Do not synthesize goods, meals, purchases, deliveries, route completion or carrier payments.
- Do not choose an unapproved bakery recipe quantity, daily bake policy, fuel supply, household demand rate, package weight, freight rate or subsidy. If a policy is missing, implement data-driven support and report the exact minimum decision needed, then continue independent work.
- Do not treat an estimated freight cost as a carrier payment. Do not treat an auto-received regional order as a physical wagon trip.
- Keep custom navigation; no NavMesh. Preserve Kennedy's Main Scene, configuration, UI, horse/wagon prefabs, materials, fonts and music. Never reset, clean, force checkout or stage unrelated local changes.
- Do not run the complete suite or an uncontrolled multi-month simulation. Use focused tests, bounded PlayMode milestones, and inspect the actual Console.

## Priority 0 — Establish the source baseline and compile

1. Verify the published integration branch and expected HEAD against GitHub before editing. Preserve any local changes.
2. Compile in the actual Unity Editor. The previously imported Muse changes initially failed in `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs` due to `LogisticsRoutePlan` and `WorkstationComponentView` namespace references; the integration repair should already fix these. Confirm there is no regression.
3. Confirm the direct development configuration still creates 9 residents, 5 households and 4 businesses and that Bakery opening equipment remains the existing `oven-chamber-1`, `kneading-table-1`, `proofing-rack-1`, `bake-peels-1`, and `baker-hand-kit` data. Do not replace GUIDs or overwrite Kennedy's other serialized asset edits.

Relevant files: `SimulationDrivers.cs`, `DevelopmentTownConfiguration.asset`, Bakery runtime/save registration, and the existing focused Editor tests.

## Priority 1 — Make the bakery-to-store bread chain legitimate

The current day-change path calls `SimulationDrivers.DriveBakeryWholesalePlanning`, but it returns without planning when `BakeryShopRuntime.WholesaleAccounts` is null/empty. The live run had no `[BakeryProduction]` event despite configured oven components and baker kit. Millbrook's flour purchase worked and created its paid lot; that is not proof the flour reached Bakery inventory or that bread was baked.

1. Trace the actual Bakery account creation/authoring path, production plan, fuel authority, flour units, equipment checks, labor/time guard, finished-goods stock, wholesale pricing/settlement, and General Store receipt.
2. Identify whether a valid Bakery-to-General Store account already exists but is not initialized. Repair the actual missing caller/configuration if so.
3. If product, batch quantity, production cadence, wholesale terms or fuel source require owner policy, do not invent it. Add/use an authorable configuration that captures those choices, then report the precise missing decision. Continue testing safe refusal behavior and independent store replenishment work.
4. On an authorized setup, test one full real cycle: valid inputs decrease; person-time/equipment are reserved; production advances under actual task rules; bread enters Bakery stock; wholesale buyer pays; Bakery cash increases; Bakery stock decreases; B0 stock increases under a unique lot/receipt. Repeated daily ticks must not duplicate production, cash or inventory.
5. Use focused tests for missing account, missing fuel/input/kit/oven, successful production, wholesale transfer, and idempotent repeated driver calls.

Potential source: `SimulationDrivers.DriveBakeryWholesalePlanning`; `Series2BakeryProductionDriver`; `BakeryShopRuntime`; Bakery wholesale account/contract authorities; `GeneralStoreRuntimeManager`; `GeneralStoreTradingPort`; `StoreProcurementChain`; existing Bakery-to-store wholesale and production tests.

## Priority 2 — Repair the observed household supply failure without fake demand

In the stress run, B0 began with 24 bread and sold all of it legitimately on day 1 (four 6-loaf purchases at 5c, 30c household debit and 30c business credit per receipt). On day 2 onward, bread was out of stock. The run emitted 5,966 missed-meal warnings; on day 335 the summary was 0 meals eaten, 18 missed, 0 purchases. The live demand also requested preserved meat, unspecified meat, preserved food, potatoes, flour, staple food and fuel wood, while the household system reported no legitimately known seller for those identifiers. Household cash was not uniformly zero.

1. Trace item IDs through household catalog/needs, known seller discovery, sale offers, package/unit conversion, purchase quantity, settlement, lot receipt and meal consumption.
2. Fix only demonstrated identity/registration/wiring defects. Do not expose all 27 store SKUs or create arbitrary household demand merely to increase sales. The run did show the food shortage remained open when no eligible stock/seller existed.
3. Confirm valid purchased lots satisfy the corresponding need and are consumed once, with no second cash charge at meal time.
4. Add focused transaction and refusal tests for real available stock, exhausted stock, unaffordable purchase, unsupported package conversion, household receipt, and one-time consumption.
5. If normal demand for sugar, coffee, tea, salt, soap, candles, clothing, hardware or medicine lacks approved rates/preferences, provide a precise list of missing authored definitions and continue with existing approved food needs.

Potential source: `DailyNeedsService`, household inventory/consumption, item catalogs and mappings, seller knowledge/offer authorities, `RetailUnitConversions`, `GeneralStoreTradingPort`, `GeneralStoreRuntimeManager.BuildHouseholdTradingPort`, transaction and receipt authorities.

## Priority 3 — Make restock decisions observable and avoid duplicate orders

The run registered 20 regional journey locations (Ashford/Redfield) and source code wires `GeneralStoreRuntimeManager` to `StoreRestockDriver`. However, the captured PlayMode run did not show a regional purchase order or regional receipt. It did show one legitimate finite feed order: 20 sacks at 15c/sack, 300c total; B0 stock was then sold to B3 and later 5–7 sack orders were refused when only 3 remained. This demonstrates finite feed trade, not regional replenishment.

1. Follow `OnDayChanged` → `DriveDailyRestock` → regional supplier initialization/production → restock decision → purchase order → delivery/receipt. Record actual refusal/no-order reason (below threshold, no eligible SKU, cash, pending order, no supply, owner policy) without repeated log spam.
2. Compare the daily regional restock driver with the weekly off-map reorder path. Ensure they share an authoritative outstanding-demand/order check so one shortage cannot be bought twice.
3. Preserve finite supplier stock, cash, order and lot identities. Do not mark an off-map shipment as physically carried unless a real freight authority executes it. If source-side automatic receipt is an explicit existing contract, keep the boundary clear and do not charge a fictional carrier.
4. Focused tests: no stock creation on repeated initialization; one order per shortage; supplier debit/credit and stock depletion; delivery exactly once; duplicate daily/weekly trigger coordination; old save without regional state; restore with open order.

Potential source: `StoreRestockDriver`, `StoreRestockService`, `GeneralStoreRuntimeManager`, `GeneralStoreSupplierAdapter`, `RegionalSupplierAdapter`, `RegionalTownRegistry`, `RegionalBusinessRegistry`, `RegionalDeliveredCost`, `SaveDtos`, `SaveLoadManager`, weekly off-map reorder driver.

## Priority 4 — Unblock shipment staging and complete the existing hitch task

The freight agreement was accepted at 125c (50 miles one way; 100 chargeable return miles; 25c flat + 1c/mile; payment after delivery). One shipment referenced the exact paid Millbrook lot. It stayed on dispatch hold; no cargo pickup, payment or delivery occurred.

Exact route refusal from the PlayMode capture:

> `[FreightAgreement] Exit route unplanned for shipment 'freight-shipment:freight-request:opening-flour-b1': TryPlanFreightExitRoute: road access could not be resolved for business 'livery_freight_003' — no placed building or plot road-access cell, so the freight exit route has no staging cell.`

`LogisticsRuntimeManager.TryPlanFreightExitRoute` requires valid local road-access/staging data. Resolve B3 staging using existing authored parcel/building/fixture authority; do not invent a new route or save unrelated scene edits. Test a valid and invalid staging cell and preserve the shipment hold on refusal.

The preparation task T49 was assigned after an initial rest-window refusal, but the 335-day run never logged hitch completion, dispatch creation/start, or departure. Later domestic labor reported P7 already working on `task:T49|opening-wagon-hitch-preparation`; day 17 also records a same-person task conflict. Investigate `AdvanceAutonomousWork`, task progress, schedule windows, reservation release and the one-time hitch completion callback with absolute simulation time. The log does not prove which branch is faulty, so instrument and reproduce before changing priority. Fix the minimum issue so a valid 10-minute opening completes once, releases/attaches the horse and wagon in the existing transport state, and creates/starts only the correct next task when authorized.

Potential source: `SimulationDrivers.AdvanceAutonomousWork`, hitch completion handler, `TaskAuthority`, `PersonScheduleTracker`, `WorkTimeBudgetStore`, `FreightProductionDriver`, `FreightServiceAgreements`, `HorseAuthority`, wagon/equipment reservation authority, `LogisticsRuntimeManager.TryPlanFreightExitRoute`, regional exit/staging authoring.

## Priority 5 — Implement coupled departure and genuine freight custody only through existing authorities

After route staging and task completion work, inspect the exact transition from authorized dispatch to movement. Connect the existing custom pathing/logistics/journey authority so Ethan, horse A0 and the wagon move together, preserve world scale/anchors, and reach an authored regional handoff. Keep identities and cargo custody stable across multiple workdays; long trips must remain active across days and allow legitimate rest. Do not invent speed, costs, lodging, camping, feed provisioning or arrival dates. Do not teleport, mark pickup at dispatch, or automatically settle the 125c before successful delivery.

Only proceed to off-map departure/pickup if the local staging and coupled movement authorities can prove actual position and custody transitions. Otherwise deliver a tested boundary and exact scene/config authoring requirements. Next bottlenecks after departure are persistent journey state, Millbrook lot pickup exactly once, return journey, bakery receipt/storage, and post-delivery freight settlement exactly once.

Potential source: `FreightProductionDriver`, `FreightShipmentService`, `LogisticsRuntimeManager`, `JourneyModel`, `PopulationPathingDirector`, `AgentMover`, transport presentation controller, freight custody adapter, shipment/agreement state, save owner/DTO.

## Priority 6 — Stabilize task diagnostics and mortality/scenario startup

The raw Console capture has 7,740 warning calls (5,966 FoodEconomy, 1,738 SimulationDrivers, 5 BusinessFeed, 4 AnimalCare, 3 FreightAgreement, plus 24 other warning-detail/registration records). The SimulationDrivers warnings include 731 routine due-event completion summaries at warning severity and 335 autonomous labor summaries; domestic labor failure summaries occurred 332 times. A representative real scheduling refusal says the worker had 49/150 daily minutes remaining (25% capacity) while the task required 61 minutes. Routine completion should be informational; real schedule refusal remains visible and transition-based. Deduplicate unchanged refusals without suppressing distinct causes.

The run logged five development mortality-suppression events (old age/illness), no observed birth/pregnancy, and no completed death. Keep mortality and fertility development switches off and preserve nonfatal hunger/illness. This was only a finite smoke test; add focused regression tests for suppressed death not opening an estate or removing a person.

Startup presented First Ledger; `[ScenarioDirector] Scenario 'first-ledger' active` and HUD requested 1870-01-01. Verify Open Frontier registration separately through the existing ScenarioDirector and preserve First Ledger. Do not create another scenario authority or procedural generation system.

## Required validation and commits

For each coherent slice:

1. Add/extend targeted Unity Editor tests using production authorities.
2. Compile in Unity; record actual errors/warnings.
3. Run only the targeted tests and provide actual counts/failures.
4. Run a bounded fresh PlayMode checkpoint only when its prerequisites are ready. Inspect Game/Scene movement and actual transaction, task, inventory, cash and custody states—not just Console messages.
5. Capture Console before/after and summarize exact distinct causes and counts.
6. Commit the coherent source slice to the supplied integration branch, preserving upstream Muse history and any local authored work. Never force push or stage unrelated files.

At minimum, the next PlayMode verification should prove one household purchase and meal from valid stock, one bakery production/wholesale cycle only if its terms are authored, one store replenishment decision with no duplicate order, and freight staging/hitching/dispatch state. Do not run another 335-day simulation to discover known blockers.

## Deliverable

Report implemented vs connected vs Unity-tested behavior. List files/methods changed, focused tests and outcomes, actual cash/inventory/custody reconciliation, remaining owner choices, and the next bottleneck. Do not claim physical freight delivery or sustainable food supply until observed through the real Unity runtime.
