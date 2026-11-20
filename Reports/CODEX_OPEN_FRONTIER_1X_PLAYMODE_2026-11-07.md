# Open Frontier 1× PlayMode Review — 2026-11-07

## A. Test configuration

- Project: Land & Ledgers, Unity 6000.4.2f1 HDRP.
- Branch/HEAD at test: `integration/muse-regional-economy-2026-11-05` / `7fad646e52c2bd93464651130ef2c4b3772bfc49`.
- Scenario: Open Frontier. Main Scene serialized `bootScenarioId: open-frontier`; the fresh-start log says `Startup intent: NewGame`.
- Seed: world `485455302` (randomly generated; requested comparison seed `738424587` was not forced). Market `1474896984`.
- Speed: 1× selected in the Game HUD throughout the observed run.
- Initial HUD time: Monday, March 1, 1897, 19:54. The HUD was last captured Tuesday, March 9, 1897, 16:31. This is more than seven complete in-game days. The test continued after that screenshot and the day-10 needs summary was logged before stopping; a final HUD timestamp was not captured.
- `SimulationTimeSettings.realSecondsPerGameDayAt1x` is authored as 60 seconds. Wall-clock duration was approximately 9m50s from the first visible fresh-start runtime messages through stopping PlayMode. The observed date progression was broadly consistent with that setting.
- Unity Console after stopping: 202 informational entries, 35 warnings, 0 errors. The test-only log contains no `error CS` lines or runtime exception strings after the fresh `NewGame` marker.
- No test suite was run. No gameplay code, prices, seed, configuration, terrain, prefab, or speed setting was changed for this test.

## B. Project integrity and scene recovery

The Editor initially had a “Scene(s) Have Been Modified” prompt with `Main Scene*` and `Comparison` loaded. I saved **Main Scene only** from its own Hierarchy context menu, then unloaded the clean `Comparison` scene without saving it. This preserved the authored Main Scene and did not save Comparison over it. After PlayMode the Editor returned to Main Scene Edit Mode; the scene root had no dirty marker. No Git operation was performed.

The saved scene still has the `Rural Landscape Dressing` hierarchy. A read-only parse of its serialized prefab-instance parent relationships counted **412** child prefab instances: 41 under each of Grove 1–4, 46 under each of Grove 5–8, and 64 under Meadow Edge Clumps. Scene and prefab settings retain `generateOnStart: 0`, regional terrain refresh off, `buildOnStart: 0`, and `allowEditorTerrainRebuild: 0`. The NewGame log confirms `UsePreAuthoredTerrain`, validates `main-authored-terrain-v1`, and says runtime regional terrain generation was skipped. Thus the 412 authored objects are persisted and runtime terrain regeneration was not invoked. I did not independently count active runtime renderers during PlayMode, so runtime duplicate absence is not claimed as directly measured.

Disk space at preflight was approximately 9.36 GiB free on C: and 196.3 GiB on F:. No disk-full error appeared. Main Scene is a local untracked asset; it was saved intentionally as requested. Existing local work was not staged, reset, cleaned, committed, or pushed.

**Compilation caveat:** Unity entered PlayMode and ran the simulation. The final Console showed 0 errors, and the fresh-run log segment contains no C# compiler errors or runtime exceptions. However, the full Editor log contains an earlier compiler diagnostic block for the untracked editor utility `Assets/EnvironmentDevelopment/Editor/RuralEnvironmentAuthoring.cs`: CS0103 references to `_protectedBounds` (lines 59 and 64), `_roadMap` (70), `RoadStrength` and `Disturbance` (96), and CS0128 for duplicate `settings` (128). Those diagnostics precede this fresh-start marker; I did not force a clean recompile or change that file. A clean full-project compilation is therefore **not verified**, despite successful PlayMode execution.

## C. Visual and animation observations

The saved GameView capture at Tuesday, March 9, 16:31 shows several small timber buildings, broad bright ochre roads, and mostly open ground from the current elevated camera. Roads are legible but visually dominant and more saturated than the adjacent ground. The camera did not frame pedestrians, horse A0, or the wagon closely enough to judge anatomy, scale, foot sliding, turning, stopping, or driver-seat alignment. No animation claim is upgraded to visually verified.

At night (observed around 21:07 and 01:54), the GameView became nearly black with only faint road shapes and the cursor light visible. Daylight returned by the later daytime observation. The day/night transition is active, but night-time readability is a clear visual concern. Dawn/dusk quality was not observed in enough detail to assess.

The Edit Mode Scene view showed the authored tree clusters. During the runtime view, the camera remained near the settlement center and did not show the rural groves. No floating vegetation, road obstruction, or runtime vegetation duplication was observed in the limited view; those conditions were not exhaustively traversed.

Runtime logs confirm that Ethan/P7 boarded the wagon’s `DriverSeat` with `pose=sitting`, the coupled journey started, and P7 dismounted at staging on return. They also state the horse, wagon, and Ethan remained active at the physical regional destination. This verifies runtime identity/transition logging, **not** visual seating, mesh substitution, scale inheritance, horse alignment, or animation quality. No duplicate-man appearance was visually assessed.

Open Frontier started without a First Ledger introduction, confirmation, objective acknowledgement, or campaign popup. The HUD showed cash/net worth, date, town pulse, and speed controls. First Ledger’s continued availability was not tested during this assignment.

## D. Household economy — primary failure

The NewGame created 9 residents in 5 households. The General Store’s actual opening inventory log for this seed records 19 bread loaves, 4 ten-pound flour sacks, 8 potato sacks, 8 lb sugar, 5 lb coffee, 2 lb tea, and 8 small salt bags.

The daily-needs summaries for days 3–10 were:

| Day | Meals eaten | Meals missed | Household purchases |
|---|---:|---:|---:|
| 3 | 2 | 16 | 0 (0c) |
| 4 | 2 | 16 | 0 (0c) |
| 5 | 0 | 18 | 0 (0c) |
| 6 | 0 | 18 | 0 (0c) |
| 7 | 0 | 18 | 0 (0c) |
| 8 | 0 | 18 | 0 (0c) |
| 9 | 0 | 18 | 0 (0c) |
| 10 | 0 | 18 | 0 (0c) |

Across these eight summaries: **4 meals eaten, 140 missed, zero household purchases**. The missed-meal diagnostics report zero food in household inventory. Cash was not uniformly the constraint: for example, households H2–H4 logged 40,000c while missing meals.

The first observable failure occurs before a completed shopping transaction:

1. Household needs remain open and the daily shopping/errand code attempts to plan them.
2. `HouseholdShoppingLoop` rejects candidate sellers when there is no recognized offer for the requested item (`FindRoute`/arrival trading checks are also applied; `HouseholdShoppingLoop.cs:562–598`). Runtime reasons include `preserved_meat`, `meat_unspecified`, `preserved_food`, `potatoes`, `staple_food_unspecified`, and `fuel_wood` having no legitimately known seller offer.
3. The errand planner also logs unavailable shoppers because both household members have schedule conflicts in the trip window. The coordinator’s execution path is `SimulationDrivers` around `:7669–7689`; `PersonScheduleTracker` reports `No available shopper` at `:522`.
4. On the Sunday attempt, bread itself was rejected at supplier trading validation: the General Store was closed for day-of-week index 6 (`HouseholdShoppingLoop.cs:570–574`). This is a specific closure, not proof the store is closed every day.
5. No successful seller visit, cash debit/credit, household lot receipt, or consumption-from-purchased-stock was logged.

The log contains repeated diagnostics (62 seller-failure strings and 54 unavailable-shopper strings in the captured segment; these include repeated/aggregated messages and are not unique households). The first repair should instrument and fix the needs-to-eligible-shopper/seller-to-trip transition, then prove a real purchase, receipt, and food decrement. Do not interpret cash alone as proof that a valid seller or shopping window exists.

## E. Bakery and General Store

The startup log confirms one configured oven installed with `oven-chamber-1`, `kneading-table-1`, `proofing-rack-1`, and `bake-peels-1`, plus `baker-hand-kit`. It booked the existing demand-derived wholesale fallback for 24 loaves/day at 2c each. The Bakery had a finite 40-unit opening fuel endowment and 20 lb flour available for a one-batch plan; later planning diagnostics report 30 lb flour. Fuel was not the first observed production blocker in this run.

No batch baked. Each daily production cycle through day 10 refused its one-batch precheck because the Bakery had **0 notions on hand and needed 1**. The 24-unit planned batch remained counted as “already covered in the pipeline,” so the daily driver did not re-plan; the production log repeatedly says the batch stays planned. The wholesale driver found no sellable bread shelf lot, transferred no bread, moved no cash, and kept the account due.

The notions driver logged one order each day with a 14-day quoted transit, but runtime procurement messages route the `bakery-notions` order through `general_store_000`/the store procurement chain rather than showing a usable Bakery-owned receipt. No notions arrival or Bakery receipt occurred during the run. This ownership/recipient mismatch is the first Bakery-chain break; the unresolved batch reservation is a second recovery blocker. Exact logs and all stack traces are in the session log.

No household bought bread. Therefore the opening bread stock was not depleted by household sales in the tested summaries, and no Bakery-to-Store replenishment occurred. I did not read final live business inventory through a runtime Inspector, so no exact ending stock is asserted.

The General Store’s independent feed cycle did work. It ordered 20 feed sacks from Millbrook at 15c/sack for 300c (log says no category stock was seeded for free). Ethan/P7 later bought 7 sacks for 105c: B3 cash 20,000c→19,895c, Store cash 19,758c→19,863c, Store stock 20→13; the identified lot was received by B3 on return. A later 5-sack purchase settled 75c: B3 18,516c→18,441c, Store 19,863c→19,938c, Store stock 13→8, with receipt lot recorded. Horse A0 was fed two units per daily feed event and returned to nutrition 1.00. These are actual business-feed transactions, distinct from household shopping and from physical Millbrook freight delivery.

## F. Freight and physical transportation

Glen’s opening request for one 10 lb Millbrook flour lot was created. Ethan P7 met Glen; the in-person agreement quoted 50.0 miles one way, 100 chargeable miles, 125c total, payable after successful delivery. A shipment was accepted for `opening-freight-wagon`.

On day 2, the runtime log confirms P7 boarded at DriverSeat and the coupled team departed toward the Millbrook destination. The movement controller logged physical movement beyond the town boundary and a destination-specific physical arrival at `ashford-millbrook_grain_co`; it reports Ethan, horse, and wagon active at destination. The coupled physical return began, the journey returned through the gateway, and the team arrived at staging/disembarked by day 4. These logs show physical movement and return occurred, though no close visual view was captured.

**The freight transaction did not complete.** The shipment registration diagnostic explicitly says `status=Planned`, `dispatch hold=True`, and “no pickup, cargo transfer, or freight payment occurred.” There is no supplier lot pickup/receipt by the Bakery and no 125c settlement to Ethan. Arrival/return progression is not equivalent to cargo custody or delivery. One opening trip occurred; repeat-haul capability was not tested.

## G. Daily timeline (available evidence)

- **Start / day 1:** Fresh random-seed Open Frontier world; 9 residents/5 households; store opening stock and finite Bakery inputs initialized. Glen’s freight request was created. Bakery precheck failed on notions. Household shopping needs were already failing seller resolution.
- **Day 2:** Feed purchase by Ethan completed and was received by B3. Glen/Ethan met and accepted the 125c after-delivery freight agreement. Horse/wagon/driver boarded and departed for Millbrook.
- **Day 3:** Physical Millbrook destination trigger arrived; return was initiated. Daily needs: 2 eaten, 16 missed, 0 purchases. Bakery still had 0 notions.
- **Day 4:** Physical return/staging completed. Daily needs again 2/16/0. No freight cargo receipt or payment logged.
- **Day 5:** Daily needs 0/18/0. Production remained blocked.
- **Day 6 (Sunday):** Daily needs 0/18/0. Bread candidate was rejected because the Store was not trading on day index 6.
- **Days 7–10:** Each recorded 0 eaten, 18 missed, 0 purchases; Bakery precheck continued to refuse for 0 notions and planned demand remained covered. Feed purchasing/receipts continued. The day-10 summary was logged before stopping.

## H. Errors and warnings

- Final Unity Console counts after exiting PlayMode: 0 errors, 35 warnings, 202 informational entries.
- The fresh-run log segment contains 0 C# compiler errors, 0 `LogError` calls, and 0 exception strings after the NewGame marker.
- Repeated warning causes observed: no valid household seller for open needs; no available shopper in the scheduled trip window; domestic fuel-handling/chores cannot fit the candidates’ schedules; autonomous work assignments refused because P3/P7 were already resting; negative-scale BoxCollider warnings; and unrecognized/dropped General Store item offers from the startup catalog audit. These are not all one defect: seller/catalog mismatch and schedule fit block shopping; rest conflicts are person-time authority outcomes; negative-scale colliders are an authored transform/physics warning.
- The complete Editor log contains Unity AI Assistant token-refresh/conversation-network messages from before the gameplay run. It also contains the earlier `RuralEnvironmentAuthoring.cs` compiler diagnostic block described above. Those are kept separate from the zero runtime-error count for this NewGame.
- A Game HUD control overlay and no First Ledger campaign interruption appeared. No crash occurred.

## I. Ten highest-value findings

1. **Critical — Household food is not self-sustaining.** Zero purchases and 140 missed meals in days 3–10. Seller resolution and shopper availability fail before transaction/receipt/consumption.
2. **Critical — Bakery notions do not reach Bakery custody.** Daily orders go through the General Store procurement identity; Bakery remains at 0 notions.
3. **Critical — Failed/planned Bakery work blocks recovery.** The same 24-loaf pipeline reservation is counted as demand coverage after the input precheck refuses, so no replacement batch is planned.
4. **High — Freight arrival is not pickup/delivery.** The physical journey completes its destination/return legs without supplier lot custody or the authorized 125c post-delivery settlement.
5. **High — Shopper schedules prevent errand execution.** Logs report no adult free for the trip window. Preserve schedules, but provide/validate a legitimate shopping window.
6. **High — Household item identity/catalog coverage is incomplete.** Several open needs have no recognized seller; do not broaden the catalog indiscriminately.
7. **Medium — General Store hours reject Sunday bread purchase.** Observed arrival check rejects it as a non-trading day. Confirm intended business calendar and schedule shoppers accordingly.
8. **Medium — Fresh full compile remains unverified.** A stale `RuralEnvironmentAuthoring.cs` compiler block is in the full Editor log; the current source appears to have since added the referenced members. Run a fresh Unity compile before the next implementation pass.
9. **Medium — Night presentation is nearly black.** Daytime is readable; late-night streets/buildings were difficult to see in GameView.
10. **Unverified — Character animation/scale and runtime vegetation duplicates.** Scene contains authored dressing and logs show rider state transitions, but this camera/test did not provide close visual evidence for walking, seated alignment, collider clearance, or all active runtime environment instances.

## J. Recommended development plan

### Muse source development

1. **First repair: household errand completion.** Start from `HouseholdErrandCoordinator`, `PersonScheduleTracker`, `HouseholdShoppingLoop`, and the `SimulationDrivers` day-needs caller. Make eligibility, seller stock/open hours, route, scheduled trip, settlement, lot receipt, and consumption observable as distinct transitions. Add focused tests for a real bread purchase at an open time, exact cash/stock/household-lot changes, and subsequent consumption. Do not make synthetic purchases.
2. **Repair Bakery notions ownership and order lifecycle.** Ensure the procurement recipient is the Bakery and receipt enters its actual notions inventory. Add a focused save/load and ownership test. Avoid duplicate daily orders while an order is pending.
3. **Release blocked Bakery demand only after a failed batch is terminal.** Preserve in-progress batches; a refused precheck must not leave phantom pipeline coverage forever. Test retry once notions arrive, then verify real flour/notions/fuel consumption, labour, bread lot, wholesale receipt, and balanced settlement.
4. **Connect physical freight destination arrival to custody authorities.** At the assigned Millbrook trigger, purchase/load the agreed lot once; on Bakery receipt settle 125c once. Retain journey identity and save/load tests. Add a second genuine shipment after the first is fulfilled.
5. **Test Open Frontier day summaries at normal and accelerated speeds** after focused tests, preserving the one-authority scheduler and persistence rules.

### Codex Unity integration

1. Reopen the project, inspect the current dirty-state-safe branch, and resolve the `RuralEnvironmentAuthoring.cs` compiler diagnostics without discarding authored environment or scene work. Confirm a fresh full Unity compile before declaring it clean.
2. Inspect the live General Store trading-day Inspector/configuration and align errand arrival windows to authored hours; do not change the business policy without owner approval.
3. After source fixes, run focused Unity tests and a short real PlayMode purchase-to-consumption check; use the runtime Inspector or explicit diagnostics to confirm ending stock and lot custody.
4. Perform a close, camera-follow visual check of actual P7, A0, wagon, DriverSeat, walking/idle animation, and dismount. The current test did not visually validate those.
5. Recheck the environment in daylight and night once, confirming all 412 serialized instances are present, no runtime duplicates/road obstruction, and night exposure remains readable.

### Owner-authored Unity visual work

1. Review the saved daytime GameView capture and night readability. Decide whether the intense ochre road presentation and near-black night require adjustment.
2. Keep existing terrain and vegetation authoring intact until a camera-grounded visual pass identifies specific corrections; do not globally rescale characters/buildings from this overhead capture.

### Project safety and artifacts

- Main Scene is saved and open in Edit Mode; Comparison is unloaded and was not saved.
- No source-code edits, tests, commits, pushes, branch changes, resets, or cleanup were performed for this assignment.
- Existing dirty and untracked project files were preserved. `git status --porcelain --untracked-files=all` is very large because it expands nested untracked paths; I did not inspect, stage, or modify unrelated paths.
- Evidence files:
  - `Reports/CODEX_OPEN_FRONTIER_1X_PLAYMODE_2026-11-07.log` — current fresh-run session segment from NewGame through Editor exit.
  - `Reports/CODEX_OPEN_FRONTIER_1X_EDITOR_FULL_2026-11-07.log` — full Editor log, including pre-run Editor/AI Assistant and compiler diagnostics.
  - `Reports/CODEX_OPEN_FRONTIER_1X_GAMEVIEW.png` — full Editor screenshot with the daytime GameView, 1× button, HUD date, and Console.

