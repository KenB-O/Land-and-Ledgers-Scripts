# November 5 Muse Integration Stress Test

## Baseline and verification boundary

- Starting published integration baseline: `f60a21dc4869b27da27b7a1ad6bd038c70f28706` on `integration/muse-regional-economy-2026-11-05`.
- Muse bundle: 108,201 bytes; `git bundle verify` passed with the expected baseline prerequisite. The fetched branch contains all 13 commits and ends at `1c74ebd307452b5dc99659b5c30543bd5a0befe0`.
- That Muse HEAD was integrated by fast-forward. A Unity compilation defect was repaired before PlayMode.
- Unity 6000.4.2f1 HDRP Editor successfully compiled after the repair. An independent `dotnet build Assembly-CSharp.csproj --no-restore` also completed with 0 errors and 48 warnings; this is not a substitute for Unity validation.
- The fresh run was actually started from the Game view's **Start First Ledger** option. It used First Ledger, not Open Frontier. The authored fixed town still initialized: 9 residents, 5 households, 4 businesses.
- Highest available in-game speed was 100x; 250x was not available. The run progressed from 1870-01-01 (day index 0) through day index 335 (approximately 1870-12-02), about 335 simulated days. Wall time was approximately 3 minutes, slightly longer than the requested two minutes. This was a bounded test, not a repeatable benchmark.
- The actual PlayMode Console showed 0 errors and 882 warning groups at stop; its information counter was `999+`. The unfiltered Editor.log segment contains 7,740 warning calls, 1,355 ordinary `Debug.Log` calls, zero `Debug.LogError` calls and zero `Debug.LogException` calls. The raw counts include repeated records that Unity's Console groups. No warning was suppressed.
- The full raw segment is `COMPLETE_STRESS_TEST_CONSOLE.log` (14,704,575 bytes). The Editor was returned to Main Scene Edit Mode; the pre-existing dirty scene was not saved.

## Ranked findings

### P0 — Household food supply collapses after opening bread is sold

**Observed.** The live General Store began with 24 bread loaves (not 17). On day 1, four households each legitimately bought 6 loaves at 5c per loaf. Each settlement debited 30c from the household, credited 30c to the store, reduced stock by 6, and recorded a receipt. Stock moved 24 → 18 → 12 → 6 → 0. From day 2 forward, bread purchases were refused as out of stock. The day 335 summary reports 0 eaten, 18 missed meals, 0 purchases. There were 5,966 `[FoodEconomy] meal missed` warnings in the raw run; most later records say household inventory was empty. Across days 2–335 the household shopping summaries repeatedly say no viable seller for needs such as preserved meat, meat, preserved food, potatoes, flour, staple food and fuel wood. Cash was still present in several households, so this is not explained by universal insolvency.

**Bread producer boundary.** `SimulationDrivers.DriveBakeryWholesalePlanning` is called on day changes and creates/attaches a legitimate in-stock fuel source. It returns before batch planning when `BakeryShopRuntime.WholesaleAccounts` is null/empty. The PlayMode log contains no `[BakeryProduction]` events. Bakery oven components and the baker hand kit were endowed from the authored development configuration, and Millbrook flour procurement works, but the bakery has no authored wholesale account that establishes a product, quantity, buyer, terms or production demand. Do not invent a daily batch policy. The next legitimate step is to author/approve the demand/wholesale terms, then verify fuel, flour, labor, production and real wholesale transfer.

**Smallest repair.** First connect a legitimate Bakery-to-store wholesale account or other already-approved demand source, with owner-authorized product, quantity, timing and price. Then diagnose the first refusal from the existing bakery production guards. Separately inspect household need identifiers and eligible seller offers; do not grant free or synthetic stock.

Representative evidence: `[SimulationDrivers] daily needs day 335: 0 eaten, 18 missed, 0 purchases (0c).` and the day 2 shopping diagnostics report General Store bread out of stock and no known sellers for other requested items.

### P1 — Freight exit route cannot be staged; shipment never dispatches

**Observed.** Glen's request and physical business visit occurred. A one-time agreement was accepted, with a 50-mile one-way route, 100 chargeable round-trip miles, 25c flat fee + 100c mileage = 125c payable after delivery. Exactly one shipment was registered for `aidlot-millbrook_grain_co:opening-flour-b1`; it remained planned/on dispatch hold. No pickup, cargo transfer, delivery or carrier payment occurred. Regional journey locations were registered, but that alone did not make the carrier's local route stageable.

Exact warning: `[FreightAgreement] Exit route unplanned for shipment 'freight-shipment:freight-request:opening-flour-b1': TryPlanFreightExitRoute: road access could not be resolved for business 'livery_freight_003' — no placed building or plot road-access cell, so the freight exit route has no staging cell.` This occurred once in the captured run.

**Root cause.** `LogisticsRuntimeManager.TryPlanFreightExitRoute` requires an authored/placed business building or plot road-access cell for the carrier staging point. The runtime freight business has neither in this configuration, so `SimulationDrivers` cannot produce an exit route and retains the shipment hold. The horse/wagon never depart; no off-map journey was entered.

**Smallest repair.** Provide a valid authored road access/staging reference for B3 using the existing parcel/building authority, or resolve the staging cell through the already-authored freight staging fixture if that is the established design. Verify route plan → dispatch authorization → coupled transport departure. Do not teleport or mark pickup/delivery.

### P1 — Hitch preparation T49 is assigned but no completion is observed

**Observed.** The shipment's preparation task T49 was created and equipment reservation was reported. Its first scheduling attempt was refused because P7 was resting at minutes 480–510; it was later assigned. Through day 335 there is no hitch completion event, no dispatch T50 event and no equipment departure. On day 17 domestic labor reported P7 already working on `task:T49|opening-wagon-hitch-preparation`, while repeated days re-adopt domestic tasks and report work conflicts.

**Best-supported cause, still requiring a focused reproduction.** `SimulationDrivers.AdvanceAutonomousWork` is the production progress path and completion handler. The evidence suggests an active task reservation/availability interaction: hitching is not accumulating enough uninterrupted progress or its completion callback is not reached, while daily scheduling repeatedly encounters the retained task reservation. The log alone does not prove which branch is at fault. Inspect absolute task progress, task state, daily work budget and reservation windows before changing priorities.

**Smallest repair.** Add a narrow regression around the existing 10-minute task at a legitimate schedule opening; verify it reaches completed exactly once, releases/attaches equipment correctly, then queues dispatch. Preserve person-time authority.

### P1 — Domestic labor requeues and scheduling failures recur

**Observed.** The raw log has 335 `[SimulationDrivers] autonomous labor` warning summaries and 332 `domestic labor failures` summaries. A representative failure is `P1 has 49 of 150 daily work minutes remaining (capacity 25%); the task needs 61`. Day summaries also repeatedly say the same `domestic.meal-prep` task was re-adopted. A total of 1,738 warning calls were emitted under the SimulationDrivers logger, including 731 routine due-event completion summaries emitted at warning severity. These completions are not themselves failures; the severity and volume obscure real scheduling refusals.

**Repair direction.** Separate routine completion diagnostics from actual refusal warnings, then trace stale/re-adopted domestic tasks and the 61-minute task vs 49-minute available budget. Do not bypass work budgets or shorten work durations without authority.

### P2 — Regional restock is present in startup but no regional replenishment is demonstrated

**Observed.** Startup registered 20 authored Ashford/Redfield locations. The General Store manager's daily restock path is wired in source. However, this stress run produced no identifiable regional purchase order or regional goods receipt. It did produce a real initial external feed purchase: 20 sacks at 15c each (300c), followed by legitimate sales to B3. B0 feed stock later reached 3; later 5–7 sack orders were refused for insufficient stock. Thus the test proves one feed transaction and finite depletion, not functioning regional restocking.

**Repair direction.** Trace a day-by-day restock decision with explicit eligibility/threshold/cash/pending-order refusal reasons. Check whether regional suppliers' finite inventories, offered SKUs, customer demand and weekly legacy reorder share pending-order identity. Do not treat a cost estimate or planned delivery as physical freight or a paid carrier settlement.

### P2 — Household catalog/demand cannot reach much authored stock

The daily log repeatedly reports no legitimately known seller for requested identifiers. This indicates a mismatch between the active household demand identifiers/knowledge and actual store offers, in addition to eventual bread stock exhaustion. The stress trace does not justify offering every one of the General Store's products or inventing demand rates. Audit identifier mapping, household catalog membership, seller knowledge and offer eligibility, and document owner-policy gaps separately.

### P2 — Scenario startup still selects First Ledger

The UI started `first-ledger` from asset `FirstLedger`; the Console confirms `Scenario 'first-ledger' active`. The fixed town did initialize, and 20 regional locations were registered, but Open Frontier is not the selected/registered Main Scene start path. This is a scenario/scene authoring integration item, not evidence of failed population initialization.

### P3 — Horse-feed system partly works; no transport movement was exercised

The live store ordered 20 feed sacks for 300c, received the finite purchased lot, and B3 made three legitimate purchases (7, 5, 5 sacks) with store depletion and business cash debit. A0 was actually fed in 17 observed care events. Four `[AnimalCare]` at-risk warnings report zero available feed at a draw and nutrition reduction. This proves some live feed procurement/care, not robust long-term supply. No horse/wagon freight movement was observed because dispatch prerequisites did not reach route execution.

### Other warning sources

- Warning-call category counts from the captured log: FoodEconomy 5,966; SimulationDrivers 1,738; warning-detail records 20; BusinessFeed 5; AnimalCare 4; FreightAgreement 3; one each for a General Store omitted-offer warning, a runtime SKU not offered warning, GeneralStore placeholder warning and ScenarioDirector diagnostic. Total 7,740. The 20 warning-detail lines and the broad SimulationDrivers summaries include several distinct messages; do not treat category count as a count of distinct code defects.
- The one-time General Store placeholder warning says a serialized Business placeholder without stable identity was discarded. It did not prevent the store from initializing, but should be traced to its authoring source.
- One SKU warning says runtime SKU `store_horse_feed_sack` is not offered; feed procurement nevertheless used its existing business feed authority. Confirm whether this is expected separation or a missing retail offer, rather than indiscriminately exposing feed to households.
- Five mortality-suppression events were observed (old age and illness). No birth/pregnancy event or completed death was observed. Development mortality/fertility remained disabled; this is a finite sample, not a full regression proof.

## Unity source integration repair

`Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs` had four Unity compile errors introduced by incorrect namespaces in the Muse additions. The repair imports `LandLedgers.Pathing` for `LogisticsRoutePlan` and refers to `WorkstationComponentView` in `LandLedgers.Economy.Equipment` (the catalog remains under its existing `Workstations` namespace). Unity recompiled successfully afterward.

`Assets/Core/Population/DevelopmentTownConfiguration.asset` received a narrowly staged Bakery opening equipment entry (four existing oven component IDs and `baker-hand-kit`). The asset GUID is unchanged. The Editor log confirmed the equipment endowment. Existing local asset edits were not included in the source change.

## Validation not performed

No Unity Test Runner tests were executed. No save/load round-trip was tested. The stress run reached First Ledger, not Open Frontier. No actual regional reorder, Bakery bake, wholesale bread receipt, freight exit, Millbrook pickup, return delivery or freight settlement was observed. Those are open integration tasks, not successful outcomes.
