# Economy recovery iteration 03 — 2026-11-15

## One PlayMode run

- Unity 6000.4.2f1, actual Main Scene Small-town session.
- 60.00012 simulated days at 250x (15,000 simulated seconds/real second), 342.04 real seconds; started at 25,575.79s, ended at 5,209,586.21s (March 1–April 30, 1870).
- 9/9 managed residents and 9/9 visible agents throughout the final checkpoint.
- Zero route failures, zero pending game seconds, zero capped rollover or short-tick frames. Simulation paused at target; Unity was returned to Edit Mode.
- Capture: 1,459 log entries, 886 warnings, 0 errors during the monitored interval. Full JSONL, daily checkpoints, summary, and filtered freight events are in this directory.

## What the run confirmed

- Prior removal of the commercial-premises authorization gate held: the Millbrook shipment received a real carrier agreement and planned exit route. No commercial premise was required to authorize it.
- The paid order was 48c for one 10 lb sack; Bakery cash changed 35,000→34,952c and Millbrook stock 460→450 lb, with 48c credited to Millbrook. The lot remained paid and awaiting physical pickup.
- Glen (P3) physically arrived at Ethan's (P7) meeting point. A 125c quote was accepted and shipment `freight-shipment:freight-request:millbrook_grain_co:opening-flour-b1` was registered. Payment and cargo transfer remained correctly pending delivery.
- Departure still failed because the opening horse/wagon views were not created: startup validation rejected every candidate because the body footprint crossed the operator-home parcel edge. That is a physical clearance failure, not an ownership or commercial authorization requirement.
- Freight operator Ethan made real B0 feed purchases and returned actual sacks to B3. Four purchase and four receipt events were recorded in the captured window; example: 7 sacks at 15c, B3 20,000→19,895c, B0 50,227→50,332c, then B3 received the 7 real sacks.
- Bakery production and wholesale settlement operated for five logged batches before flour scarcity blocked later batches. The 10 lb paid Millbrook lot was not consumed or received early. Households retained open shortages where no known purchasable offer existed.
- Previous stale-in-progress sweep warnings (3 in iteration 02) did not recur. The previously repaired missing-route retry allowed the shipment to reach a planned route; it could not depart without the coupled presentation.

## Repair applied after the run

`OpeningTransportPresentationController` no longer requires horse, wagon, hitch fixture, and turning clearance to remain within one parcel. The checks now require town-grid bounds, dry/known traversable terrain under the authored maximum slope, no road/building/blocked/access-anchor overlap under the parked footprints, valid custom-navigation paths, traversable non-road turn clearance beside road-route cells, and no physical collider overlap. Parcel ownership no longer doubles as a vehicle staging authorization rule. Existing road route and cargo authorities are unchanged.

Added `FreightStagingCellSelector.IsClearVehicleGroundCellAnywhere` and focused tests that accept clear non-parcel ground while rejecting roads, buildings, blocked anchors, unusable terrain, steep slopes, and out-of-bounds cells.

This source repair compiled and passed focused EditMode tests. It was not re-entered into PlayMode because this iteration allows only one simulation; wagon placement and Millbrook pickup therefore remain to be confirmed on the next authorized run.

## Warning review

| Category | Count | Interpretation |
| --- | ---: | --- |
| Incident | 446 | Bounded household shortages and trip states; repeated weekly escalation remained visible. |
| TaskDuration | 326 | Distinct terminal/open task duration diagnostics; retained, not globally muted. |
| SimulationDrivers | 83 | Daily need and refusal summaries. |
| BusinessFeed | 8 | Real feed availability/task-window outcomes. |
| StoreRestock | 7 | Legitimate supplier profitability, stock, or receiving-capacity conditions. |
| Uncategorized | 6 | One SKU without backing item lots and five world-validation summaries for land offers/building anchors. |
| FreightAgreement | 3 | Transient occupied/time-window scheduling conflicts; meeting later completed. |
| AnimalCare | 2 | Two real finite-feed shortage incidents before replenishment. |
| Felling, LogHaul, OffMapJourney, SawmillProduction, SawmillTransfer | 1 each | Felling/sawmill chain lacks live scenario resources; OffMapJourney is the wagon-presentation defect fixed in source. |

No warning category was suppressed in this pass. No source logging change was made. The opening transport placement error is outside the monitor's 1,459-entry capture window but remains in Unity Console history. The recurring `Single Story Business Building White Plaster Animation 1.anim` deserialization error also remains; this is an existing binary animation asset and was not rewritten during an economy recovery pass. One additional Editor Console error was generated by an invalid Unity pipeline command (`time` is not an exposed command); it was a tooling invocation, not a gameplay failure.

## Verification after repair

- Unity script recompile: completed, `failed=false`, no compiler errors; Editor returned ready/stopped.
- EditMode tests: `FreightStagingCellSelectorTests` 10/10, `FreightDispatchActivationIntegrationTests` 5/5, `TransportPairPlacementTests` 6/6, `FreightOffMapDepartureTests` 7/7. Total 28/28 passed.
- `git diff --check` passed for the three scoped files. No PlayMode run occurred after the repair.

## Files touched by this repair batch

- `Assets/Scripts/Domains/Economy/Transport/OpeningTransportPresentationController.cs`
- `Assets/Scripts/Domains/World/Pathing/FreightStagingCellSelector.cs`
- `Assets/Scripts/Tests/Editor/World/FreightStagingCellSelectorTests.cs`

These files contained earlier local work before this iteration; the changes above are additive and that work was preserved.

