# Targeted Codex stabilization and playtest — 2026-11-16

## Result

**VERIFIED:** the production household shopping path completed a real bread purchase, cash settlement, physical return and household receipt. The intended blocker was the timing of DEF-067's daily diagnostic: it was emitted from the daily-needs drive before that day's physical shopping settlements. It therefore produced misleading empty/incomplete lines. The diagnostic now reports the previous, completed day at the next day boundary. No purchase, supply, navigation or cash authority was replaced.

Unity 6000.4.2f1 compiled the changes successfully. The post-change Small-town/New Game run reached day index 19 (date 1870-03-20), about 19.6 simulated days. Speed was changed from 1x to 250x after startup; this exceeded the requested 10–14 day observation window. PlayMode was stopped and Unity is in Edit Mode. Main Scene was not saved.

## Root cause and changes

The Phase C household purchase is settled during the day, after `DriveDailyNeeds` begins. The old same-day summary was consequently not a complete daily charge record. This was a diagnostics integration defect, not evidence that the actual retail transaction was absent. Live gameplay confirmed the transaction pipeline works when a valid offer, stock, shopper and route are present.

Changed:

- `Assets/Scripts/Domains/Population/HouseholdFoodChargeReconciliation.cs` — added a once-per-completed-day reporter using the existing reconciliation authority.
- `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs` — publishes the previous day's reconciliation from `OnDayChanged`; removed the premature same-day publication from `DriveDailyNeeds`.
- `Assets/Scripts/Tests/Editor/Population/HouseholdFoodChargeReconciliationTests.cs` — tests completed-day publication, zero-activity lines, duplicate suppression and charge formatting.

## Live shopping and food evidence

The fresh runtime completed bakery, wholesale and household activity. Its daily bakery diagnostics recorded 19 batches of 24 loaves (456 loaves) and 19 wholesale settlements at 96 cents each ($18.24 total), with traceable bakery lots. Store stock later ran out for some requested goods; those attempts were refused without a sale and the needs remained open. The store received new wholesale bread and household purchases resumed when stock was available. This is consistent with finite retail inventory and demand; it is not proof that every household need was fulfilled.

One actual purchase recorded in the live `ShoppingTripResult` and household ledger:

- Shopper Ethan Boniecki (P7), household H4; seller Kennedy Baldwin-Ooms General Store.
- 42 loaves at 5 cents each; buyer debit 210 cents and matching store receipt 210 cents.
- Retail receipt `retail-v2:general_store_000:1:0`; received lot 3 (`authority:household-inventory/lot:3`).
- The trip recorded seller arrival, authorized settlement, goods entering shopper custody, return and 42-unit household receipt. H4's ledger shows the 210-cent debit with a 39,790-cent balance afterward; a separate 38-cent staple purchase leaves 39,752 cents. The reconciliation line totals H4's day-1 staple purchases as 44 units / 248 cents.
- H4 later consumed bread from household inventory through the meal log. The live meal log contained 504 legitimate meal records by the day-19 snapshot; recorded bread consumption across households totaled 384 units. Daily food summaries reported five households served, 27 meals eaten and zero missed on the observed completed days. These are runtime observations, not proof that food security is sustainable indefinitely.

## DEF-067

The completed-day reconciliation lines were emitted in PlayMode for completed days 0–18, including zero-activity days. Every reported household/day had `legacy=0u/0c`; Phase C charges appeared on actual purchase days. There were no `OVERLAP` flags in the observed output. `MULTI-ITEM` appeared on day 1 for H4 and H3 (bread and potatoes) and day 4 for H2 and H3 (bread and flour). Those flags identify multiple staple SKUs in one household/day; they do not by themselves indicate duplicate charging. H4's two day-1 ledger entries match the distinct purchases (210 cents and 38 cents), with no legacy charge recorded. **DEF-067 remains open:** this bounded observation is not a complete audit of every purchase source, all five households for every day, or a save/reload boundary.

## Verification

Unity compilation: complete; `compilationFailed=false`, `compiling=false`.

Focused EditMode results after the source change:

| Suite | Result |
|---|---:|
| HouseholdFoodChargeReconciliationTests | 9/9 passed |
| HouseholdShoppingLoopTests | 30/30 passed |
| HouseholdShoppingEndToEndTests | 8/8 passed |
| GeneralStoreBreadPurchaseIntegrationTests | 9/9 passed |
| ShoppingTripDriverTests | 56/56 passed |
| CampaignCCustomerPatiencePersistenceTests | 11/11 passed |
| **Total** | **123/123 passed** |

No broad suite was run; the previously reported 162 failures were outside this targeted pass and were not reclassified.

The PlayMode run was a real Small-town New Game, approximately 19.6 simulated days. It verified completed-day reconciliation output, real bread purchase/settlement/receipt, subsequent inventory-backed consumption, bakery production/wholesale and legitimate stockout refusals. A separate forced stockout-and-replenishment acceptance sequence, Save/Continue during outbound or paid return, and comparative Normal-versus-Extreme-250x run were **NOT TESTED**. The run began at 1x during startup, then used 250x; the observed shopping and day-rollover events progressed at accelerated speed.

## Console and remaining issues

The retained Unity Pipeline export is `TARGETED_CODEX_STABILIZATION_PLAYTEST_CONSOLE_2026-11-16.json` (883 entries, full message and stack-trace fields, 1,427,931 bytes). Its retained buffer counts are 467 logs, 416 warnings and 0 errors. At the same time, the Editor ground-truth status reported 448 logs, 319 warnings and 2 errors. The counters disagree, and the two ground-truth errors were not present in the exported entry buffer. Compilation succeeded. The discrepancy and unidentified errors are **INCONCLUSIVE**, not treated as zero errors.

Prominent warnings in the retained buffer included 53 negative-scale collider warnings, 42 genuine seller stockout/refusal messages, domestic stove/cleaning/fuel-route deferrals, 15 animal-feed capacity refusals, and 6 freight scheduling conflicts. These are reported, not suppressed or fixed in this shopping/diagnostic pass. Exact message entries and stack traces are retained in the JSON export.

No scene, prefab, font, terrain or asset was changed by this pass. Existing unrelated owner modifications remain unstaged and untouched.

## Publication

The source, regression test, report and console export were published to the existing development branch `codex/resumed-wave-unity-integration-2026-11-16`. The exact commit and remote SHA are reported in the final handoff after remote verification.
