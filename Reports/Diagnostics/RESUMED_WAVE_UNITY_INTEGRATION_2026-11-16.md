# Resumed Wave Unity Integration and Verification

Date: 2026-11-16\
Unity: 6000.4.2f1 HDRP\
Verification branch: `codex/resumed-wave-unity-integration-2026-11-16`\
Integrated Muse commit: `556990678ca29ca91a501ad623def88f0d97de17`

## Integration and preservation

The supplied bundle was downloaded and verified against SHA256 `a9ef628906c5fbb7521686b3320497ff96abdf7780fe29a5e69622eb2a7842a5`. It contains merge commit `556990678ca29ca91a501ad623def88f0d97de17`, with the requested parents `07c67dcd178a9b6e11046847f26e7f3f38f98fd8` and `2d755c6e8eb8ef1c26c0ba949b3ceb896430ba55`; baseline `7f4acaa722aa7f42adffd2386f4f5ddd80eb19a5` is an ancestor. The integration branch was pushed without force and its remote SHA was verified before local verification began.

Owner changes present before integration remain dirty and untouched: `Assets/Main Scene.unity`, `Assets/Environment/Buildings/House Natural Wood.prefab`, nine regional terrain metadata files and five TMP SDF font assets. The Main Scene remained dirty throughout; it was not saved. Untracked files were not staged or removed.

## Compilation and tests

Unity's first compile exposed a real C# error in `AcquisitionMarketManager.cs`: `finalPriceCents` was out of scope where the code used the in-scope `finalPrice`. That reference was corrected and an escrow-close assertion was added. Unity 6.4 also exposed ambiguous `Object` and `EntityId` names in two newly integrated test files; Unity-specific aliases were added. A fresh forced Unity compile then completed with zero errors.

The full Unity EditMode run completed: **7,390 total; 7,228 passed; 162 failed; 0 skipped**. The full failure set was not compared against a Unity baseline, so it is not labelled pre-existing. Known high-count failure classes included TownWorldFootprintSaveLoad (12), AcquisitionVpFlow (8), obsolete 300-second DayLength expectations (7), BusinessCashTransfer (6), ConstructionInputSupport (6), GeneralStoreEconomyRepair (5), ProfessionWaveRuntime (5), and HouseholdReserveInventory (5). Other failures were distributed among smaller suites. `AcquisitionVpFlowTests` had 31 pass and 8 failures; the land-close test failed at `TryPurchaseSelected` before reaching the new escrow assertions. The TownHall foundation tests also logged an animation import/read error for `Single Story Business Building White Plaster Animation 1.anim`. These failures remain unresolved; the harness's 19 failures are not treated as equivalent Unity results.

Integration-focused classes passed in that full run:

| Suite | Result |
|---|---:|
| GeneralStoreRuntimeManagerTests | 12/12 |
| BakerySuppliesTests | 31/31 |
| BakeryNotionsDailySupplyTests | 12/12 |
| FuelIntakeSavePrecreationTests | 5/5 |
| FreightPricingDef016Tests | 20/20 |
| EarnestEscrowDef069Tests | 12/12 |
| FreightCancellationDef071Tests | 8/8 |
| FreightFailedShipmentDef070Tests | 5/5 |
| ConstructionFreightPlanFactoryTests | 16/16 |
| ConstructionProgressBillingTests | 22/22 |
| NpcConstructionCommissioningDef011Tests | 19/19 |
| TimberRegrowthDef072Tests | 11/11 |
| HouseholdFoodChargeReconciliationTests | 8/8 |
| ShoppingTripDriverTests | 56/56 |

The PlayMode review then found the DEF-067 daily reconciliation line was added to a transient diagnostics list but filtered from Console by the publishing path. `HouseholdShoppingLoop.IsRoutineSummaryDiagnostic` now recognizes that line, and its regression test covers the classifier. After that small patch, Unity recompiled successfully and focused tests passed: **HouseholdShoppingLoopTests 30/30; HouseholdFoodChargeReconciliationTests 8/8**. The new log routing has not yet been exercised in a subsequent gameplay run.

## Live simulation

One live New Game run was made in the real Main Scene. It started on March 1, 1870 and ended on April 3, 1870, about 34 calendar days, at the UI's **Extreme 250x Normal** preset (`5.76 simulated seconds/day`, 24-minute Normal day). It was left in Edit Mode. Startup selected `Fixed Testing World / Default QA Town`, seed 1886, not an Open Frontier preset. Startup logs record 9 residents, 5 households, 13 parcels, and 11 generated buildings. The enabled production runtimes included the bakery, fuel dealer, freight company, sawmill and lumber yard, plus the General Store.

### Economic evidence

- **Bakery and wholesale:** 36 batch events produced 24 loaves each (864 loaves recorded as production) and 36 wholesale lot settlements at 96c each. Recorded bakery-to-store settlement total was **3,456c**. Production consumed one 12 lb flour allocation, one notions unit and one fuel unit per batch. Bakery fuel began at 40 units and was down to 4 after day 36; the Fuel Dealer registered zero yard intake and zero saleable fuel, so continued production is at risk after the remaining opening supply is consumed.
- **Flour and freight:** The opening 10 lb Millbrook flour lot was bought for 48c, reducing bakery cash 35,000c to 34,952c and Millbrook stock 460 lb to 450 lb while crediting Millbrook 48c. Two later reorder records were for 2 sacks/20 lb at 96c and 1 sack/10 lb at 48c. All three shipment lots were ultimately credited once to bakery inventory (four sacks/40 lb total). Each of the three carrier agreements collected 125c after delivery and logged the matching 125c carrier revenue, for **375c** total freight settlement. The third shipment's receiving route was delayed and pinned on days 21–23; it resumed and was received on day 26. No duplicate receipt is evident in the logged lot identities.
- **Local bakery flour procurement:** Daily records report 44 sacks/440 lb bought locally for 1,276c over the run. Those purchases are separate from the 40 lb Millbrook cargo. Opening flour was 120 lb; after logged production and those purchases plus freight receipts, the remaining 168 lb is consistent with 600 lb in less 432 lb consumed. This is a derived reconciliation from logged quantities, not a separately captured end-of-run inventory snapshot.
- **Horse feed:** Ethan P7 made 7 physical business purchases at the General Store totalling 37 sacks at 15c each (**555c**); the buyer and store cash deltas matched each transaction, and the return logs credit the real purchased sacks to B3. Horse A0 has 34 daily feeding records drawing 2 feed units each (68 units); nutrition recovered from 0.85 to 1.00 and stayed at 1.00 on successful feeding days. There were 2 at-risk/no-feed warnings. Two 20-sack supplier-lot receipts were refused repeatedly because animal-feed capacity was exceeded (32 refusal warnings total); each refusal reports the source lot restored, so no phantom receipt is indicated.
- **Household meals:** The live daily resolver reported 5 households served, 18 meals eaten on day 1, then 27 meals/day on days 2–36, and zero missed: **963 reported meals**. These are DailyNeedsService meal-resolution results. The Console did not include meal-lot consumption records or a saved end inventory snapshot, so item-by-item inventory reconciliation was not established.
- **Household food shopping:** The fresh log contained no `[FoodEconomy] purchase settled`, no `purchase received`, no daily shopping summary and no charge-reconciliation line. There were 36 due-event failure summaries and repeated stockout refusals for bread, beef, bacon and cheese; the aggregate capture mentions 52 distinct trip IDs. No household food debit, seller credit or household receipt could be verified. This means a successful household retail transaction and counter-service flow were **not demonstrated**; it does not establish whether absent retail settlement was caused solely by scarcity or by a service/stock-authority mismatch.
- **Sawmill, forestry, lumber and construction:** Startup explicitly logged no configured sawmill equipment; daily transfer records show no sawmill byproducts. No lumber production or sale was verified. The live construction evaluator scanned all five households each day but queued zero requests because all five had “no evidenced need.” No live construction commission, material procurement, labor progression, earnest-money transaction or occupancy transition occurred.
- **Shelf prices and business cadence:** The Main Scene's serialized `logBusinessCadenceDebug` was already enabled (`1`) and was not changed. This run still provided no legacy/Phase-C household charge comparison, and it produced no household food purchase entries; therefore DEF-067 overlap and multi-item status are **inconclusive**, not a clean result.

## Checklist status

| Requirement | Result | Evidence / limitation |
|---|---|---|
| Fresh New Game begins in 1870 | **VERIFIED** | HUD showed March 1, 1870; startup intent was NewGame. Startup was Default QA Town, not Open Frontier. |
| 30-day economic run | **VERIFIED** | One run advanced about 34 calendar days at 250x; daily production, meals, feed and freight events were observed. |
| A. DEF-009 intake and lots through save/continue/re-save/reload and old save | **NOT VERIFIED** | `FuelIntakeSavePrecreationTests` passed 5/5, but no live save/load cycle was performed. Live Dealer intake and saleable fuel were zero. |
| B. DEF-067 daily charge reconciliation across households | **FAILED / PARTIAL** | No reconciliation lines were emitted by this run due to the filtered publishing path. The source routing was fixed and EditMode tested afterward; no second gameplay run was used. No overlap conclusion is justified. |
| C. Shopping Save/Continue states | **NOT TESTED** | No dedicated save slot or mid-outbound, mid-return, paid-recovery, AwaitingService or InStore restoration exercise was performed. |
| D. DEF-011 commissioning through procurement, freight, labor, occupancy and persistence | **NOT TESTED** | No need was evidenced and no commissioning request was created in this scenario. Negative refusal fixtures passed in EditMode; no live positive case. |
| Physical household purchase and employee counter service | **FAILED to demonstrate** | No household purchase settlement or receipt was recorded. Stockout failures occurred; service gate was not proven. |
| Flour freight physical delivery and settlement | **VERIFIED** | Three shipments delivered 4 sacks/40 lb; receipts and 3 × 125c carrier settlements are logged. |
| Horse feed procurement and consumption | **VERIFIED with shortages** | Seven paid purchases / receipts and 34 feeding records; two at-risk events and repeated capacity-refused supplier receipts. |

## Console and runtime warnings

The complete fresh buffer export is `RESUMED_WAVE_UNITY_CONSOLE_2026-11-16.json` (2,452,668 bytes, SHA256 `F7E8C72605A0AA7FC82BD0E8F8193985E79D1939D8C9E80E5CA0923B213143B3`). It retains message text and stack traces. The captured buffer reported 1 error, 707 warnings and 732 logs (1,440 entries). At the same capture the Editor ground-truth counters reported 2 errors, 494 warnings and 682 logs; the counter discrepancy is preserved rather than normalized away.

The one captured error was a Unity Pipeline bridge request failure, `Cannot be changed after headers are sent`, during the initial domain reload. It is tooling/transport evidence, not a gameplay exception. Unity compilation itself completed with zero errors. Warning categories from the captured buffer:

| Category | Count | Main observed cause |
|---|---:|---|
| SimulationDrivers | 305 | 203 domestic labor failures, 36 due-event failure batches, 36 daily household-needs warnings, 17 autonomous-labor warnings, 13 work-before-shopping conflicts. |
| Incident | 251 | 59 opened, 32 cause changes, 160 escalations; persistent household supply/need incidents. |
| Negative-scale BoxCollider warnings | 53 | Temporary prefab-reference hierarchy objects have negative scale and BoxCollider extents are forced positive. |
| FeedSupply | 32 | Repeated 20-sack receipt refusals when animal-feed category capacity would be exceeded; source lots restored. |
| BusinessFeed | 18 | Feed purchasing/receiving refusals and related supply diagnostics. |
| TaskDuration | 14 | Task duration overruns. |
| FreightAgreement | 8 | Contact travel/meeting timing conflicts or retryable availability constraints. |
| StoreRestock | 7 | No profitable upstream offer for selected staple/meat goods. |
| StaleInProgress | 4 | Expired task windows could not be re-reserved due to existing person-time commitments. |
| AnimalCare | 2 | Horse feed unavailable, nutrition fell to 0.85. |

Other startup/authoring warnings included legacy service anchors that were not adjacent to road access and a below-target vacant land-offer reserve. These were not suppressed or edited during verification.

## Files changed locally for this checkpoint

- `Assets/Scripts/Domains/Economy/Acquisitions/AcquisitionMarketManager.cs` — compile fix for out-of-scope final-price identifier.
- `Assets/Scripts/Tests/Editor/Economy/AcquisitionVpFlowTests.cs` — escrow close assertions.
- `Assets/Scripts/Tests/Editor/Economy/ConstructionFreightPlanFactoryTests.cs` — Unity 6 `EntityId` alias.
- `Assets/Scripts/Tests/Editor/Economy/GeneralStoreRuntimeManagerTests.cs` — Unity 6 `Object` alias.
- `Assets/Scripts/Domains/Population/HouseholdShoppingLoop.cs` — publish the DEF-067 daily reconciliation line as routine information.
- `Assets/Scripts/Tests/Editor/Population/HouseholdShoppingLoopTests.cs` — regression assertion for reconciliation summary classification.
- `Reports/Diagnostics/RESUMED_WAVE_UNITY_CONSOLE_2026-11-16.json` — full fresh Console export.
- This report.

No scene, prefab, material, terrain asset or owner-authored configuration was modified by this verification pass. The dirty owner changes listed above remain excluded from the checkpoint commit.

## Remaining blockers and follow-up

1. Repeat a bounded runtime pass after the reconciliation-log fix to obtain all-household/day charges, meal inventory evidence and legacy-vs-Phase-C comparisons. The current run cannot decide DEF-067.
2. Run dedicated Save/Continue scenarios with a unique test slot for DEF-009 and each shopping state in Part C; do not overwrite owner saves.
3. Select a scenario with a real evidenced housing need before attempting the positive DEF-011 commissioning chain. Do not create a need or resources for test success.
4. Investigate why household shopping produced stockout failures and no purchase receipts while daily bakery wholesale stock was logged; identify whether timing/scarcity or the service/offer authority is responsible before changing policy.
5. Investigate the repeated domestic route failures, capped feed receipts and negative-scale BoxCollider warnings. These were recorded, not repaired or globally suppressed.
6. Review the 162 Unity EditMode failures against a reproducible baseline and recover the exact full failed-test list before classifying regressions. Do not treat the Muse Roslyn harness counts as Unity results.

Unity was left in Edit Mode. No Main Scene save, merge to `main`, reset, clean, stash or remote force operation was performed.
