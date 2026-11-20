# Economy recovery iteration 02 — 2026-11-15

## Playtest completed before the repair batch

- One real Small-town Main Scene PlayMode run completed: 60.00064 simulated days at 250x (15,000 simulated seconds per real second), 342.93 real seconds.
- Game time: 23,809.24s to 5,207,864.16s. The run started March 1, 1870 and ended April 30, 1870.
- Nine residents and nine visible agents remained registered. No route failures, backlog, or capped short-tick/rollover frames were recorded.
- The monitor captured 1,465 log records: 575 informational logs, 890 warnings, and 0 errors. `console-playmode.jsonl` is the complete captured runtime log; `daily-checkpoints.csv` and `simulation-summary.txt` contain state checkpoints.
- FoodEconomy recorded 1,611 meals and zero missed meals during 60 daily resolutions.
- Unity was stopped out of Play Mode. As required, PlayMode was not entered again after the source repairs.

## Confirmed defects repaired

1. **Freight did not require a commercial building, but code made it a prerequisite.** Both exit-route planning and opening-transport placement called a commercial-premises authorization gate. Removed that policy. Freight now uses a real carrier-assigned building when one resolves, otherwise the carrier operator's actual home building as a physical location reference. This is location lookup only; the building does not authorize the shipment. Authored road access, parcel clearance, terrain, hitch clearance, vehicle footprint and physical destination checks remain in force. No goods, position, ownership or payment are invented.
2. **A previously accepted shipment could keep an empty route forever.** Registration attempted route planning once. After a refusal, daily departure retries never rebuilt the missing route. The departure path now retries route planning for that same shipment from its current physical staging location before attempting movement. It preserves the accepted agreement, lot custody, payment state and retryability; it does not mark departure or delivery successful.
3. **An autonomous task could be rescued before its just-started reservation accrued.** The stale-task sweep runs before current short-tick work accrual. It treated an activity reservation ending exactly on the tick boundary as expired, then the newly re-derived window overlapped the next daily plan. The boundary is now healthy for that tick and becomes stale on the next tick if work remains.
4. **A freight dispatch test depended on save-load leaving the operator away from home.** Loading normalized that starting state, so the test failed before exercising its handler. The fixture now explicitly establishes its required away-from-home precondition; no production rule was weakened.

## Economic findings

- The actual run had accepted a Millbrook flour shipment for a 10 lb lot, with a 125-cent quoted freight charge. The lot remained in freight custody; the Bakery correctly did not receive or consume it before physical delivery.
- The Bakery later purchased one real 10 lb flour sack locally for 29 cents. Its 12 lb batch requirement left a real 2 lb shortage; the batch remained blocked. No free stock or payment was created.
- During the captured run, the accepted freight route was refused because the code required assigned commercial premises. After the fix, that authorization rule is removed and existing null-route shipments are eligible for route replanning on a later departure attempt. Actual wagon departure, Millbrook pickup, physical return, Bakery receiving and settlement were not re-observed in this iteration because its instructions prohibit a second PlayMode run.
- The captured run also logged one departure refusal naming an unavailable physical destination trigger or coupled transport presentation. The former premise check could prevent opening transport from being presented at all; the new physical-location fallback addresses that setup path. The precise live presentation subcondition was not captured, so runtime departure still needs validation after this batch. This is not a commercial-authorization requirement.
- Household shopping, meal resolution and supplier stockouts remained governed by actual inventory, money and receipt authorities.

## Warning categories in the one PlayMode window

| Category | Count | Treatment |
| --- | ---: | --- |
| Incident | 446 | Bounded state-change reports, largely real household shortages/trip conditions; retained |
| TaskDuration | 326 | Successful terminal tasks, often with coarse short-tick duration accounting; not changed in this batch |
| SimulationDrivers | 83 | Daily need/refusal summaries; retained |
| BusinessFeed | 8 | Genuine feed offer/availability outcomes; retained |
| StoreRestock | 7 | Stock/capacity conditions; retained |
| Uncategorized | 6 | One unbacked Store SKU and five world-validation summaries for existing land-offer/building-anchor authoring |
| FreightAgreement | 4 | Includes the fixed premise-policy route refusal and other agreement events |
| StaleInProgress | 3 | Repeated same-boundary task stall; the boundary defect is fixed |
| AnimalCare | 2 | Actual finite feed shortage |
| Felling, LogHaul, SawmillProduction, SawmillTransfer | 1 each | No corresponding operating sawmill resource chain in this scenario |
| OffMapJourney | 1 | Coupled departure was unavailable in the pre-repair run; see above |

No warnings were globally suppressed. The old warning records remain in the captured evidence; the repairs change future behavior.

## Files changed

- `Assets/Scripts/Domains/World/Pathing/FreightStagingCellSelector.cs`
- `Assets/Scripts/Domains/Economy/Logistics/LogisticsRuntimeManager.cs`
- `Assets/Scripts/Domains/Economy/Transport/OpeningTransportPresentationController.cs`
- `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs`
- `Assets/Scripts/Core/Tasks/StaleInProgressSweep.cs`
- `Assets/Scripts/Tests/Editor/World/FreightStagingCellSelectorTests.cs`
- `Assets/Scripts/Tests/Editor/Tasks/StaleInProgressSweepTests.cs`
- `Assets/Scripts/Tests/Editor/Economy/FreightDispatchActivationIntegrationTests.cs`
- `Reports/EconomyRecovery_Iteration_2026-11-15_02/findings.md`

## Verification after repairs

- Unity 6000.4.2f1 fresh script recompile: completed, `failed=false`, zero reported compiler errors. Editor ready, not compiling, Play Mode stopped.
- `StaleInProgressSweepTests`: 24/24 passed.
- `FreightStagingCellSelectorTests`: 9/9 passed.
- `FreightDispatchActivationIntegrationTests`: 5/5 passed.
- `FreightDispatchStagingTests`: 7/7 passed.
- `HouseholdShoppingLoopTests`: 30/30 passed.
- `git diff --check` reported no whitespace errors in the modified source/test files.
- No post-repair PlayMode verification was performed, to obey the one-simulation limit.

## Still requiring gameplay verification

The remaining work is not to reintroduce commercial premises as an authorization. On the next allowed PlayMode run, confirm the existing flour shipment obtains its physical route, the opening team appears at a feasible physical location, the wagon departs, reaches the registered Millbrook trigger, picks up the identified lot, returns and delivers it exactly once to the Bakery. If the opening presentation still refuses, use its specific startup reason (not commercial permission) to repair the actual failed physical prerequisite. The Bakery's 2 lb residual shortage remains legitimate unless additional stock is actually procured.
