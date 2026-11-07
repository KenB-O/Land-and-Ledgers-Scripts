# Codex to Muse: Economy Handoff — 2026-11-07

## Publication baseline

- Repository: `KenB-O/Land-and-Ledgers-Scripts` (`F:\Land & Ledgers`).
- Branch: `integration/muse-regional-economy-2026-11-05`.
- Muse integration HEAD before this handoff: `7fad646e52c2bd93464651130ef2c4b3772bfc49`.
- Fetched remote predecessor: `f0fd624683f8d527f72ada9f1df8877cdca90003`.
- The remote is an ancestor of local HEAD. Seventeen commits are ahead; the 17 imported Muse commits are included in the intended publication ancestry. This document is the only additional file intended for publication.
- Extensive unrelated dirty and untracked Unity work exists, including scene/prefab, terrain, animation, audio, materials, fonts and asset-pack content. It was not staged or altered. The November 7 raw reports/logs are local evidence and are not included in this narrow publication.

## Evidence boundary

This handoff records the Unity evidence in `Reports/CODEX_OPEN_FRONTIER_1X_PLAYMODE_2026-11-07.md` and its captured session segment `Reports/CODEX_OPEN_FRONTIER_1X_PLAYMODE_2026-11-07.log`. The complete Editor log is `Reports/CODEX_OPEN_FRONTIER_1X_EDITOR_FULL_2026-11-07.log`. These files remain local and should be consulted for full records. The report says a fresh Open Frontier session ran at 1x for about 9m50s, over seven complete days, with world seed `485455302`, market seed `1474896984`, and a day-10 needs summary. Console at stop: 202 informational entries, 35 warnings, 0 errors. No test suite was run for that session. This is PlayMode evidence, not a claim that all tests or a clean compilation passed.

## P0 — Household shopping and food consumption

### Observed result

Nine residents and five households were generated. From day 3 through day 10 there were **0 household purchases, 4 meals eaten, and 140 missed meals**. Day summaries show 2 eaten/16 missed on days 3 and 4, then 0/18 on days 5–10. Several households retained 40,000c while missing meals, so cash alone does not explain the failure.

Representative captured diagnostics:

> `No viable seller for 'preserved_meat': no legitimately known seller offers this item. Need stays open.`
>
> `Unavailable shopper: No available shopper: P1: schedule conflict during the trip window. P2: schedule conflict during the trip window. Needs stay open.`

On the Sunday attempt, the store's bread offer was rejected by the arrival-time trading check because the General Store was closed on day-of-week index 6. This establishes a particular timing conflict, not that the store is always closed. The log records no successful household counter settlement, household food-lot receipt, or consumption from a purchased lot. Treat seller/offer and shopper-window failures as separately observed blockers; do not claim either is the sole root cause without tracing the complete errand lifecycle.

### Source path to continue

- `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs`: daily needs and household errand orchestration (the 1x report identifies the `DriveDailyNeeds`/`HouseholdErrandCoordinator.ExecutePlan` area around lines 7669–7689 in that captured source revision).
- `Assets/Scripts/Domains/Population/DailyNeedsService.cs`: shortage/need generation and meal result accounting.
- `Assets/Scripts/Domains/Population/HouseholdErrandCoordinator.cs`: `ExecutePlan` and shopper selection handoff.
- `Assets/Scripts/Domains/Population/PersonScheduleTracker.cs`: `TrySelectShopper`; the observed refusal is `No available shopper` around line 522.
- `Assets/Scripts/Domains/Population/HouseholdShoppingLoop.cs`: seller discovery, route and arrival trading validation around lines 562–598; `ArriveAtStoreAndSettle`; `SettleAtCounter`; `ReceiveSettledGoods` and failure reporting.
- `Assets/Scripts/Domains/Population/ShoppingTripDriver.cs`: trip movement/arrival and settlement caller.

First repair target: instrument and make executable the transition from open food need → eligible adult and feasible trip window → known live seller/offer at arrival → paid settlement → household custody receipt → food inventory decrement at meal consumption. Preserve schedules, store hours, item identity, stock, cash and custody rules; no synthetic purchases. Add a focused test proving each state change and money conservation, then run a short Open Frontier PlayMode reproduction.

The latest report counted repeated diagnostics (62 seller-failure strings and 54 unavailable-shopper strings in the captured segment); these are repeated/aggregated entries, not unique households.

## P0 — Freight cargo, Bakery receipt and exactly-once settlement

### Observed result

The fresh 1x run created Glen's one-sack Millbrook request and accepted Ethan's freight agreement. Quote: 50 miles one way, 100 chargeable miles, 125c payable after delivery. On day 2, Ethan/P7 boarded `DriverSeat` and the coupled horse/wagon departed. The session recorded physical arrival at the destination identity `ashford-millbrook_grain_co`, return initiation, gateway return, and staging/disembark by day 4. Runtime evidence confirms physical travel and return progression, but not close-up visual quality.

The same run's shipment registration diagnostic explicitly reported `status=Planned`, `dispatch hold=True`, and **no pickup, cargo transfer, or freight payment**. No supplier-lot custody transfer into the vehicle, Bakery receipt, or 125c settlement was confirmed. Thus the first missing economic transition after physical destination arrival is an authoritative, shipment-bound supplier lot pickup/cargo custody commit; without that, no receiver receipt or authorized post-delivery settlement follows. Do not conflate physical trigger arrival, a return journey, or a planned shipment with cargo delivery.

### Source path to continue

- `Assets/Scripts/Domains/Economy/Businesses/Freight/FreightProductionDriver.cs`: shipment/dispatch state, `AuthorizeShipmentDispatch`, and tick advancement.
- `Assets/Scripts/Domains/Economy/Logistics/FreightShipmentService.cs`: shipment lifecycle, pickup/delivery hooks and payment gate.
- `Assets/Scripts/Domains/World/Journeys/OffMapJourneyAuthority.cs`: persistent physical journey and arrival confirmation state.
- `Assets/Scripts/Domains/Economy/Businesses/GeneralStore/OffMapAidBusinessRuntime.cs`: Millbrook stock-backed pickup and cargo receipt identity checks (`TryPickup`/receipt methods in the current source).
- `Assets/Scripts/Domains/Economy/Businesses/GeneralStore/MillbrookFlourFreightCustodyAdapter.cs`: supplier lot custody integration.
- `Assets/Scripts/Domains/Economy/BreadDrivers/Series2StoreToBakeryFeed.cs`: receiving-bin custody path where applicable.
- `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs` and `OpeningTransportPresentationController.cs`: runtime caller and physical arrival/presentation bridge.

The Unity report's observed state is that travel arrival is not invoking or completing the source-lot custody transition in this run. Trace stable agreement, shipment, journey, supplier lot and destination identities through one authority, then commit pickup once, carry the identified lot, receive it at Bakery once, and settle the already-agreed 125c only at its authorized delivery event. Add retry/save-restore and duplicate-tick assertions. Test a second haul only after the first lifecycle is genuinely completed.

Historical comparison, not current-run proof: `NOV_06_MUSE_INTEGRATION_REPORT.md` describes an earlier 100x run with one pickup, Bakery receipt and 125c settlement. `NOV_06_PLAYMODE_ANALYSIS.md` describes a different earlier run where pickup/return and later settlement were logged, with a callback/manifest timing seam. The Nov 7 1x run did not reproduce a completed transaction; resolve the discrepancy by following current runtime state, not by assuming success from the older run.

## P1 — Bakery notions coverage and ownership

In the Nov 7 1x run, the Bakery's daily production precheck refused because it had 0 notions and needed 1; the one-batch production plan remained counted as covered. The notions quote specified finite supply, 10c/unit and a nominal 14-day lead. The test reached the day-10 summary, so the absence of a due arrival before day 14 is not by itself evidence of a failed supplier. However, daily diagnostics showed orders placed through `general_store_000` and did not show a Bakery-owned usable receipt. At the tested stop there was no notions arrival, bake, or wholesale transfer. Verify pending-order de-duplication, recipient identity, due-date advancement, Store-to-Bakery custody receipt and whether a refused batch reservation is recoverable after the input becomes available.

Relevant source: `BakeryNotionsDailySupply.cs`, `BakeryWholesaleDailyDriver.cs`, `SimulationDrivers.cs`, `Series2StoreToBakeryFeed.cs`, `BakeryShopRuntime.cs`, and the supplier/procurement receipt authority. Preserve legitimate finite stock and cash settlement. Do not classify the 1x pre-day-14 absence as a defect without an arrival-due test.

Historical counter-evidence: `NOV_06_MUSE_INTEGRATION_REPORT.md` records an earlier 100x run where the Store-to-Bakery feed transferred one notion for 10c on day 15, six 24-loaf batches produced 144 loaves, and six 24-loaf wholesale transfers settled at 2c/loaf. That proves the source/runtime path worked in that earlier test, not that it ran in the latest 1x test or will repeat indefinitely. The same report notes no successful later batch after day 27 through day 174.

## P1 — Fresh-scene Continue persistence (Muse Q04)

A fresh-scene save/Continue scenario remains outstanding. Do not infer it from serializer classes or an in-session state restoration. Verify NewGame → save at a meaningful in-flight point → leave/reopen into a fresh Main Scene/editor session → Continue, retaining scenario/date, household needs and inventories, open orders/arrivals, Bakery plans, freight agreement/shipment/journey/cargo identity, and finances. Assert no duplicate seed stock, order, pickup, receipt or settlement. Existing discussion/reference: Muse Q04 and `Assets/Scripts/Docs/Pass13_SaveLoad_Compatibility.md`; include old-save compatibility only as the established matrix requires.

## P1 — Household reserve 12c discrepancy

The latest focused Unity EditMode result available to this handoff is **77 passed, 1 failed, 0 skipped (78 total)**. Failure: `HouseholdReserveInventoryTests.PartialFillReattemptsOnlyTheRemainingDeficit`. The assertion at test source around line 791 expected wallet `99856`; observed was `99844`, a 12c difference. Test comments link this to AUD-335 Q03/AUD-328. Root cause and intended accounting value remain unresolved; do not change the expected result or purchase logic until the goods and freight components are reconciled from the test setup and actual settlement path.

Relevant files: `Assets/Scripts/Tests/Editor/Population/HouseholdReserveInventoryTests.cs`, household reserve purchase/receipt path, and `HouseholdShoppingLoop` settlement. Re-run only this focused test after diagnosis and add a component-level assertion for goods charge, freight charge, buyer debit, seller credit, and partial-fill retry.

## Existing audit queue and decisions

Use the project's existing register and Muse queue rather than creating a project-wide audit: `Assets/Scripts/Docs/Overnight_2026-11-04_Script_Audit_Register.md` and the recorded AUD-266, AUD-328, AUD-334, AUD-335 and AUD-338 references in the relevant source/tests and prior handoffs. Confirm exact current queue ownership before modifying statuses.

Still-open economic design constraints include D-1 wholesale quantities/cadence/final terms; D-2 Bakery fuel intake conversion; D-7 fuel import execution; D-5/R1 potato and salt package weights; and D-6 household meal/food identities. The 1x test had finite 40-unit Bakery opening fuel but did not resolve sustainable replenishment. Do not silently promote provisional numbers to Canon, invent package sizes, or claim a supply policy is approved.

## Proof classification

- **Source present:** the cited source authorities and transitions exist; this alone does not demonstrate that production callers execute them.
- **Unity compilation:** the Nov 7 1x report flags an earlier `RuralEnvironmentAuthoring.cs` diagnostic block in the full Editor log and says it did not force a fresh full compile. A clean full-project compilation is therefore unverified for that run. The Nov 6 Unity report separately recorded a successful compile on its earlier revision.
- **EditMode:** 77/78 focused tests passed; one reserve-wallet assertion remains unresolved. These results are Unity EditMode, not Roslyn harness results.
- **PlayMode:** latest 1x run confirms household needs/failed errands, physical freight travel and return, finite Bakery state, and observed counters as stated above. It does not confirm a household purchase, meal from purchased stock, freight cargo receipt/payment, visual seating quality, or save/Continue.
- **Not tested in this assignment:** no new tests, compile, or PlayMode session were run to prepare this handoff.

## Ranked Muse work

1. **P0 — Make household shopping and food consumption executable.** Fix/diagnose the eligibility + schedule + seller/offer/arrival transitions. Prove paid stock decrement, household receipt and subsequent food decrement with focused tests and a short PlayMode loop.
2. **P0 — Close freight custody and settlement.** Bind physical destination arrival to one authorized pickup/cargo-lot commit, Bakery receipt, and exactly-once 125c post-delivery settlement; exercise retries and save/restore.
3. **P1 — Prove Bakery notions ordering, due arrival, recipient custody and batch recovery.** Run beyond the 14-day lead; avoid duplicate orders and distinguish a pending order from on-hand input.
4. **P1 — Complete Muse Q04 fresh-scene Continue.** Test meaningful in-flight economic/freight state with duplicate prevention.
5. **P1 — Resolve the 12c reserve test mismatch.** Reconcile the 144c expected versus 156c implied charge from the fixture before changing production behavior or assertion.

After those, compare the 100x Bakery evidence against a controlled repeat-production run, then validate sustained purchasing and freight repeatability. Keep the full local logs as evidence; this committed handoff is a concise map to them, not a replacement for their full contents.
