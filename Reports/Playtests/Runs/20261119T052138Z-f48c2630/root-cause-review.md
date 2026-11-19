# Root-cause review — 20261119T052138Z-f48c2630

## Run identity

- Unity 6000.4.2f1; branch codex/player-economic-participation-2026-11-18; source SHA 7180a2f94ea1949146b502f274b8503107c7d404.
- New Game, Assets/Main Scene.unity, millbrook-development-town, seed 1886, configured 250x, 30 completed days (absolute day 0–30).
- Wall interval: 2026-11-19 05:21:38.912Z–05:27:10.243Z. Runner startup gate passed. Editor returned to Edit Mode.
- Run snapshot records a dirty tree with 29 tracked modified files. Those local changes were present during the run. Untracked files were not enumerated by the runner.
- Complete raw stream: console-full.txt and console-events.jsonl. Daily and final state: daily-summary.csv, entity-snapshots.jsonl, final-state.json.

## Outcomes

| Measure | Result |
|---|---:|
| Meals eaten / missed | 826 / 194 |
| Last 10 days eaten / missed | 246 / 80 |
| Console messages | 1,787 |
| Warnings / errors / exceptions | 687 / 1 / 0 |
| Expected / observed residents | 14 / 14 |
| Person records / households / businesses | 98 / 8 / 7 |
| Household cash, day 1 to day 30 | 320,000c → 315,018c |
| Business cash, day 1 to day 30 | 159,552c → 141,425c |
| Household inventory units, day 1 to day 30 | 148 → 234 |
| Retail inventory units, day 1 to day 30 | 172 → 492 |

The 98 personRecords field is not the residents count. The runner captured 14 actual residents against 14 expected; 98 by itself is not evidence of a population mismatch.

H1's Kennedy/P1 bread trip trip-2-77 is Outbound on day 3 and remains Outbound, without a failure reason, through day 30. H1 has 40,000c, no meal-sized recognized food stock at resolution, and missed meals. This is a confirmed liveness failure. The run did not capture enough physical route state to identify whether the route remained active, stalled, or was displaced. No route code correction was made on speculation.

The sawmill completed one production job on each of 26 observed production days. Logs identify real two-log deliveries, worker P13 completing jobs when P11 could not reserve time, and lubrication consumption. This is runtime evidence for logging-to-sawmill production, not downstream lumber sales or profitability.

Horse-feed replenishment is evidenced: four paid receipts of 20, 7, 5 and 5 sacks (37 total) from Millbrook Grain Co. through named lots and orders. The capture does not establish H1 receiving food; logs contain repeated stockouts and failed trips.

No Blacksmith owner was authored in the shared runtime; associated smith iron import and hardware transfer were skipped. This is a scenario capability/configuration absence, not a demonstrated runtime regression.

## Complete warning accounting

All 687 warning occurrences are accounted for by the logger/category below. warning-signature-accounting.csv provides 270 normalized first-line signatures, counts, first sequence and simulation context. This is a grouping aid; identical text does not prove a shared root cause. The raw event stream is unmodified.

| Category | Occurrences |
|---|---:|
| SimulationDrivers | 326 |
| Incident | 106 |
| FoodEconomy | 97 |
| LogHaul | 39 |
| TaskDuration | 35 |
| Uncategorized | 32 |
| FreightDispatch | 14 |
| BusinessFeed | 10 |
| FreightAgreement | 5 |
| ShoppingTrip | 5 |
| StoreRestock | 3 |
| OffMapJourney | 2 |
| AnimalCare | 2 |
| ScenarioDirector | 1 |
| HardwareImport | 1 |
| Felling | 1 |
| SawmillProduction | 1 |
| StaleInProgress | 1 |
| WorldGenesis | 1 |
| BlacksmithRuntime | 1 |
| SawmillTransfer | 1 |
| GeneralStore | 1 |
| HardwareTransfer | 1 |
| FreightPickup | 1 |
| **TOTAL** | **687** |

## Root-cause assessment

### Confirmed actionable gameplay failures

1. H1 bread trip liveness: ShoppingTripDriver and SimulationDrivers snapshots prove this trip did not leave Outbound for 28 days after departure. Relevant sources are Assets/Scripts/Domains/Population/ShoppingTripDriver.cs (AdvanceTick, HandleLegDue, ScheduleArrivalRetry) and Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs (physical route and route observation callbacks). Missing evidence is route generation, active/hidden state, remaining-distance samples and the task id at each snapshot. A runtime capture patch is the next safe step before changing retry behavior.
2. Domestic task routes fail repeatedly. Logs show stove routes not reaching activity points, porch points without a walkable and unobstructed approach, cleaning blocked by dining-chair footprints, and firewood handling with no authored home storage. Tasks are requeued without completion or work credit. These are distinct physical/configuration issues, not one scheduler defect. Check local household plans and prefab geometry in Unity.
3. Bed and dining activity anchors fail to resolve in multiple homes late in the run. SimulationDrivers.ResolveSleepMovement and meal movement resolve ActivityLocationId through each BuildingFootprintAuthority. Logs say IDs are not authored on the generated home. The opening configuration has registerRuntimeActivityLocation enabled and DevelopmentTownFurnishingInitializer.CreatePresentation should register them. The live discrepancy is confirmed; the run cannot say whether anchors were absent, lost or rebound. Avoid fallbacks that award use without reaching the actual furniture.
4. Meal access is poor despite substantial household cash: 194 missed meals, 80 in the final ten days. Most repeated H1 messages cite no meal-sized recognized inventory and 40,000c. Elsewhere, logs show seller stockouts, Sunday closure, unavailable shopper windows and open trips. Poverty alone is not a sufficient explanation. The stale H1 trip and finite stock/availability are confirmed contributors; full per-need transaction provenance is not captured in daily summaries.

### Expected constraints or missing scenario configuration

- Log-haul work budget: 14 warnings report P10 with 26/600 work minutes remaining for a 34-minute trip; remaining logs stay at the landing. The constraint is valid. If it repeats without later dispatch, investigate throughput rather than granting free labor.
- Logging camp supply could not use the sole wagon while it was bound to live flour shipments; the unstarted fare was refunded. This is finite-capacity contention with rollback, not duplicate dispatch.
- Store restock refusals and retail stockouts often represent finite stock or unprofitable supply. These logs do not prove every refusal is a defect.
- Firewood handling has no authored storage at affected homes. Review the authoritative local home plans; do not grant storage or fuel implicitly.
- Blacksmith and its imported hardware activity require an authored operator/business absent from this scenario.

### Diagnostic classification issues

- 16 successful LogHaul summaries report dispatch, travel, charged work time and the equipment gate, yet were emitted as warnings by SimulationDrivers.cs near line 1590. Changed the call to Debug.Log after this run. This severity correction is not compiled or tested and is not reflected in the 687 warnings.
- TaskDuration sometimes reports zero open overruns as a warning. The same family also contains genuine stuck domestic prep tasks with zero of 61 work minutes. Separate zero-overrun summaries from actionable overruns; do not downgrade the real failures.
- DEF-061 warnings report actual clock progress behind its target by multiple game days at observation points. The authoritative 30-day interval still completed. Treat these as throughput/performance evidence, not proof that 250x was sustained exactly; retain the messages.

## Unity error

One error at sequence 1 reports a load failure for Assets/Environment/Buildings/Building Animations/Single Story Business Building White Plaster Animation 1.anim: “File may be corrupted or was serialized with a newer version of Unity.” No stack trace is included. Startup and the run completed. This local asset was not changed.

## Post-run source change and verification

Changed the successful daily LogHaul status call from Debug.LogWarning to Debug.Log in Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs (near line 1590). This is a diagnostic severity correction, not a gameplay repair. It is uncompiled and unverified. Current system commit charge was approximately 49.11/50.93 GB (about 1.82 GB headroom) and free RAM approximately 2.38 GB. I did not launch another compile or test under that pressure. Unity remained responsive in Edit Mode. Existing unrelated changes in SimulationDrivers.cs were left intact.

## Next verification actions

1. Recover commit and memory headroom. Compile once, then run focused AgentMoverTests, ShoppingTripDriverTests, DomesticLaborDriverTests, SleepTickDriverTests and MealTickDriverG4Tests as applicable.
2. Capture H1 outbound task id, active route/route generation, hidden/visible state, remaining distance/time, last progress game time, trip age and failure reason. This is the missing evidence needed to fix trip-2-77 without teleportation or false completion.
3. In a short Main Scene run, inspect home bed/chair runtime activity IDs immediately after initialization and on the first failed night/meal. Compare them to furnishing register bindings and the BuildingFootprintAuthority runtime-location registry.
4. Check stove, door and furniture routes in two or three affected homes. Keep local geometry repair separate from task retries.
5. Trace H1 bread supplier candidate, actual lot availability, route, settlement and physical receipt; compare buyer/seller cash and stock before/after. Do not start another 30-day test until this short path works.
6. Treat absent Blacksmith activity as a scenario capability limitation pending owner direction. Inspect the White Plaster asset's provenance before modifying it.

## Verification status

- Verified in Unity: 30 calendar-day transitions, startup gate, New Game counts, meals, actual sawmill job completion, named paid horse-feed receipts, Console totals, and return to Edit Mode.
- Not verified: physical route outcomes, all household shopping/receipt transfers, downstream lumber sales, full conservation across Save/Continue, and the post-run LogHaul severity edit.
- No owner-local scene, prefab, configuration or asset was changed in this cycle. Pre-existing local changes remain untouched.