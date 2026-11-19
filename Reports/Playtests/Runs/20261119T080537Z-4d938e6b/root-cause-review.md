# Root-cause review — 20261119T080537Z-4d938e6b

## Run identity and validity

- Branch/SHA: `codex/player-economic-participation-2026-11-18` / `4e12150202cccbc332fe57151156531ad2fd007c`.
- Unity 6000.4.2f1; `Assets/Main Scene.unity`; Millbrook New Game; seed 1886; selected runner speed 250x.
- Wall interval: 2026-11-19 08:05:37Z–08:09:05Z (3m28s). Day index 0 through 30; 30/30 complete. March 1–31, 1870.
- Startup gate passed: 14 residents, 8 households, 7 businesses; 98 person records are a different registry count, not 98 active residents.
- Runner result `CALENDAR_COMPLETE_REQUIRES_REVIEW`, explicitly not an economic pass.
- 1,822 console events: 1,162 logs, 660 warnings, 0 errors, 0 exceptions. Warning families: 24; family counts in `warning-category-accounting.csv` sum to all 660 warning events. Original ordered `console-events.jsonl` and `console-full.txt` are preserved.
- No gameplay or source changes were made during this cycle. Unity returned an `Application.ExitPlayMode` “Hold on” dialog after runner completion; the Editor process remained responsive, but Edit Mode was not visually confirmed before the analysis stopped.

## Economy outcome and previous-cycle comparison

- Meals: 826 eaten, 194 missed; final ten days 246 eaten, 80 missed. This exactly matches the prior completed 30-day run `20261119T075703Z-d9a59138` (826/194; final ten 246/80).
- Daily meals begin at 28 eaten/0 missed; by the final day, 26 eaten/8 missed. On each of days 21–30, eight meals were missed.
- Direct meal-path purchases: 0 and 0 cents on all 30 daily rows. Physical Phase-C food activity exists for some households in the charge-reconciliation events, but there is no sustained H1 purchase; see below.
- H1 bread trip `trip-2-77` remained `Outbound` from day 1 through day 30. H2 also had a planned/patient-expired bread trip at day 30. Daily active shopping trips total 131 trip-days; the runner does not expose authoritative route position/outcome per day.
- Household cash remained high (H1 ended 39,675c; H2 39,637c), while meal resolution reported zero received meal-sized food in those households on missed-meal days. This is evidence of access/route/stock failure, not proof of poverty. The complete household table remains in `entity-snapshots.jsonl`.
- Food sellers repeatedly rejected bread for exhausted lots; the General Store also refused trade on the authored closed day. Those are legitimate stock/hour constraints individually, but repeated failed purchasing despite ample opening cash and an unchanged multi-day outbound trip is an actionable integration failure.
- Comparison: previous run 1,163 logs/650 warnings/0 errors; this run 1,162 logs/660 warnings/0 errors. Warning count rose by 10 (+1.5%); meals did not improve. Variance in warning count does not establish regression or repair.
- Other real activity captured: 30 bakery batches were baked and transferred through the existing wholesale account to the General Store; Phase-C household food charge reconciliation recorded 616 units and 3,407 cents across 29 household/day entries. This is transaction evidence, but does not show which SKU was consumed at every meal or eliminate double-charge risk.
- Forestry/sawmill: logs were hauled and 37 sawmill jobs completed. The lumberyard made two 10-unit retail sales to Don Millward (400c each); later mill-to-yard transfers were refused because the yard had 29c against 256c due for an 8-unit lot. This may be a genuine cash constraint, and the mill retained stock.
- Freight/feed: two Millbrook flour lots were physically picked up with the existing horse/wagon, but no receiving delivery for either was recorded. Three separate camp-provision shipments were delivered to the sawmill. Feed replenishment delivered 5-sack orders on days 21 and 26; A0 still had two nutrition-risk warnings during uncovered periods.
- Furniture: the workshop sold one cabinet to H3 for 325c; installation was refused because no matching placement was free. There were 87 furniture-install refusal log messages overall. Purchase/custody succeeded for that cabinet, but installation and capability use did not.
- No business-closure or bankruptcy event was found in the run logs. This does not prove all seven businesses were economically sound; final snapshots show the lumberyard had 29c and the General Store held 483 mixed retail units.
- The 331 `SimulationDrivers` warnings partition into 182 domestic-labor failures, 34 meal warnings, 30 carpentry-supply refusals, 15 sleep movement warnings, 13 camp-supply refusals, 12 autonomous-labor diagnostics, 7 work-assignment conflicts, 4 due-event failures, 30 daily household-needs summaries and 4 labor-market week settlements. Domestic warning payloads include overlapping mentions of 117 missing firewood-storage locations, 68 invalid porch approach points, 63 route-not-reached refusals, 28 installed-furniture obstructions and 13 scheduling failures. These subcause mention counts overlap because each Console warning can aggregate multiple diagnostics.
- The 37 `TaskDuration` events comprise 5 completed-overrun reports, 11 open-overrun reports (10 for T305), 16 daily summaries and 5 eviction summaries. The completed-overrun reports still show planned work credit equal to actual work; the repeated T305 entries show an unresolved open task after interruption.

## Complete warning occurrence accounting

`warning-category-accounting.csv` gives exact count, exact-message signature count, representative message, and first source frame for every family. The counts below are mutually exclusive and sum to 660.

| Family | Count | Evidence-based classification |
|---|---:|---|
| SimulationDrivers | 331 | Mixed family. Includes genuine domestic activity route failures (missing firewood storage, unreachable porch/stove, chair obstruction), shopping stock/availability failures and ordinary shortfalls. Requires sub-message/root analysis; do not treat all as one defect. Aggregated in `SimulationDrivers.cs:15116` and daily needs at `:8893`. |
| Incident | 106 | Recurrent household needs opened or changed as purchase attempts stock out, sellers close, shoppers have no window, or patience expires. The incident ledger is doing its job, but repeated same-need reopen/change churn points to purchasing/route recovery defects. `SimulationDrivers.cs:9000`. |
| FoodEconomy | 97 | Real misses and empty-larder condition. On missed days, no received meal-sized stock; warning is evidence, not noise. H1/H2 have cash but no settled receipt. `SimulationDrivers.cs:8893`. |
| TaskDuration | 37 | 5 completed-overrun warnings are delayed tick settlement while labor credit equals plan; 1 queued no-worker task; 1 domestic meal-prep task T305 remained Queued after interruption with 0/61 work minutes and repeated as an open overrun for 10 days. T305 was interrupted on day 19 and never resolved through day 30: actionable task-liveness/requeue defect or unsupported geometry needs diagnosis. `TaskDurationDiagnostics.cs:564`. |
| Uncategorized | 18 | Includes repeated world-validation/access-anchor warnings and achieved-speed lag. Separate authoring warnings from simulation-speed/runner warning; do not claim a single root. First frame in CSV. |
| LogHaul | 17 | Teamster P10 had 14–26 of 600 daily minutes when a 34-minute trip was required; refused correctly and left logs at landing. Capacity refusal is real; repeated same-day dispatch attempts should defer until a valid window. `SimulationDrivers.cs:3054`. |
| FreightDispatch | 14 | Two Millbrook flour shipments repeatedly “remain prepared” because accepted agreement/hold/status did not match; later pickup warnings show neither physically reached the off-map supplier. This is a real dispatch/hold or agreement-state integration blocker, not delivered freight. |
| BusinessFeed | 10 | Ethan’s outbound feed task repeatedly had no available window; one was off-map. Deferral preserves need, but the owner/wagon schedule cannot supply feed in those windows. `SimulationDrivers.cs:9262`. |
| FreightAgreement | 5 | Glenn’s contact travel refused because P3 had no remaining schedule window. Valid refusal but supplier orders cannot progress without rescheduling; source `SimulationDrivers.cs:8592`. |
| ShoppingTrip | 5 | Trips aborted because a resident was already carrying freight to another receiving building. Legitimate movement ownership conflict; shopping must wait/replan after freight, and must not strand its need. `SimulationDrivers.cs:15080`. |
| StoreRestock | 3 | No profitable upstream salt offer. Economically valid refusal; no evidence that creating stock would be correct. `GeneralStoreRuntimeManager.cs:2801`. |
| OffMapJourney | 3 | Loaded wagon journeys waited to begin physical return because assigned team/route/position was not ready. Real stranded return state; investigate physical freight recovery. |
| AnimalCare | 2 | Horse A0 nutrition fell 1.00→0.85 because no feed was available. Real supply failure; no free feed. |
| FreightPickup | 2 | Flour shipments still pending physical arrival at `ashford-millbrook_grain_co`. Confirms quote/preparation did not become pickup/delivery. |
| HardwareImport | 1 | No authored Blacksmith business, so import skipped; scenario limitation, not a broken transaction. `SimulationDrivers.cs:3036`. |
| SawmillProduction | 1 | No lots delivered and no open jobs at that sample; alone does not prove inactivity. Check output lots/jobs and completed production records. |
| SawmillTransfer | 1 | No untransferred mill-yard lots/open jobs at sample; no conclusion of broken transfer from this warning alone. |
| ScenarioDirector | 1 | Existing ghost development-household declaration (`GHOST-DEF-006`); should be reviewed as known scenario diagnostic, not resident count. |
| GeneralStore | 1 | Serialized placeholder lacking stable business identity discarded; likely safe duplicate cleanup, but compare startup config before downgrading. |
| StaleInProgress | 1 | Expired travel task could not reserve fresh window because P7 already had legitimate management duty; expected conflict with liveness risk if it repeats. |
| Felling | 1 | Camp refused because provisions (0) were below 2 required. Legitimate stop; no auto-order is appropriate absent authority. |
| BlacksmithRuntime | 1 | No authored Blacksmith business/owner. Scenario capability absent, not an accidental runtime loss proven by this run. |
| HardwareTransfer | 1 | Same missing Blacksmith premise/business; transfer skipped. |
| WorldGenesis | 1 | Sawmill has no fixed premises. Owner design says not every business requires commercial premises; warning should be evaluated against real sawmill equipment/worksite, not silenced blindly. |

## Confirmed and suspected root causes

1. **Confirmed runtime economic failure:** households with substantial cash repeatedly lack received food; no direct meal-path purchases occurred; H1's same bread trip stayed Outbound for the entire run. The Console identifies stockout, closed-day and unavailable-shopper causes, while 5 ShoppingTrip warnings show freight route ownership can refuse household travel. The runner does not capture person positions or route success, so the exact H1 movement cause remains unknown. Do not label all 194 misses justified.
2. **Confirmed task-liveness defect:** T305 `domestic.meal-prep` was interrupted day 19, remained queued with zero work and no recorded blocker through day 30, and was warned repeatedly. The route/activity reason and retry policy must be checked before a source repair; current evidence proves stale work and poor recovery, not a specific geometry correction.
3. **Confirmed incomplete freight execution:** two flour shipments never demonstrated physical pickup; prepared-state warnings and physical-arrival-pending warnings persisted. The prior successful flour deliveries do not generalize to this run.
4. **Confirmed physical-route blockers:** domestic tasks were refused for missing authored firewood storage, porch approach constraints, unreachable stoves, and dining chairs blocking a cleaning point. Those are distinct route/configuration cases; scene/prefab-local authoring is involved and was not modified.
5. **Expected conditions, not automatic defects:** salt upstream offer was unprofitable; logging camp had no provisions; horse feed stock depleted; P10 lacked 34 minutes; Sunday was closed. These should remain honest refusals, while recurring demand planning and task ownership require investigation.
6. **Diagnostics gap:** per-day movement outcomes and freight/construction aggregates are `NOT_CAPTURED`; per-household `inventoryUnits` is also not present in the household snapshot schema (the `-1` default is not a negative inventory). Therefore path progress, all transaction balances and detailed freight completion cannot be proven from aggregate runner totals.

## Cycle work, verification, and limits

- Actual Main Scene New Game and 250x runner: verified 30/30 clock days; full logs and snapshots saved.
- Source compilation, EditMode tests, Save/Continue tests, code repairs and a post-repair playtest: not performed. No source changes were made. Immediately after the run, free physical RAM was 2.16 GB; later measurement was 2.79 GB free RAM, 51.94 GB commit limit, and 2.63 GB free commit. The C:\pagefile.sys was using 11.99 GB (12.79 GB peak). Unity used 3.41 GB working set / 27.34 GB private memory and responded to process checks. The UI had shown an `Application.ExitPlayMode` “Hold on” dialog; Edit Mode was not visually confirmed. No compile/test was attempted with only 2.63 GB free commit.
- No source commit or push made. Existing branch remains `codex/player-economic-participation-2026-11-18`, local HEAD `4e12150202cccbc332fe57151156531ad2fd007c`, one commit ahead of `origin` at observation. The 31 tracked modifications and large untracked owner-local asset set were preserved.
- No claim that warnings have been repaired. This run re-demonstrates unchanged food access and route/task failures; actionable counts are awaiting implementation and fresh verification.

## Recommended next cycle order

1. Once memory headroom is healthy, trace T305 and H1 `trip-2-77` through task authority, movement ownership and arrival callbacks; capture live person/route state and prove bounded cancellation/replan.
2. Fix the highest-confidence shared movement/task-liveness root cause only after identifying the exact authority that retained the route/task. Add regressions for interrupted domestic route and occupied freight route.
3. Verify one real household purchase/receipt/consumption with cash and inventory delta; continue multi-day test only after that completes.
4. Trace both flour shipments from prepared through agreement hold, dispatch, supplier arrival, pickup and return; preserve cargo and payment authority.
5. Resolve authored firewood/porch/stove/chair interactions in owner-local scene/prefab content only after backing up and visually confirming exact affected objects; do not overwrite Main Scene.
6. Re-run focused tests and short Main Scene, then 30-day runner; compare meals, transactions, task state and warning families, not calendar completion alone.
