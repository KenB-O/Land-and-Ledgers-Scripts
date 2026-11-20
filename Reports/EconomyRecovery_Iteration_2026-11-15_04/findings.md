# Economy Recovery Iteration 04

## PlayMode run

- Unity 6000.4.2f1, actual Main Scene / Small-town run; one PlayMode session only.
- Simulated interval: 28,920.94 to 5,213,096.40 game seconds = 60.00203 days at 250x. Real elapsed time: 342.18 seconds. Stopped back in Edit Mode.
- Population: 9/9 managed residents and 9/9 visible agents. No route failures, pending game seconds, capped rollover frames, or capped short-tick frames in the monitor summary.
- Captured monitor window: 1,460 log entries, 886 warnings, 0 errors. Full captured Console stream is preserved in `console-playmode.jsonl`; daily state snapshots are in `daily-checkpoints.csv`.

## Confirmed behavior and defects

- The previous freight change that removed commercial-premises authorization did not by itself make opening transport presentable. Startup candidates for livery business `livery_freight_003` (building 7) all failed physical placement because horse footprints crossed the town-grid boundary or their sampled horse position was outside dry terrain. This was a geometry/grid-edge failure, not a property-right requirement.
- A paid Millbrook flour shipment remained undelivered. Glen P3 / Bakery `bakery_001` paid Millbrook Grain Co. 48c for one 10 lb lot (`aidlot-millbrook_grain_co:opening-flour-b1`): Bakery cash 35,000→34,952c; Millbrook flour 460→450 lb; Millbrook cash 0→48c. On day 2 the carrier agreement accepted a 125c quote and shipment `freight-shipment:freight-request:millbrook_grain_co:opening-flour-b1` was planned. At 214,200 game seconds, departure was refused because the physical destination trigger or coupled presentation was unavailable. No pickup, freight payment, delivery, or destination receipt occurred. The paid lot remains real and outstanding; no goods or money were fabricated.
- Real unrelated activity did progress: Bakery completed 5 batches and 5 wholesale settlements. Ethan P7 completed four B3 feed purchases and receipts. One recorded example: five sacks at 15c each moved B3 cash 18,391→18,316c and B0 cash 39,327→39,402c; stock 13→8 then the household/business receipt raised B3 stock 1→6, with lot and transaction IDs logged. This example is one of four, not the total count.
- The captured warning categories match the prior iteration's counts, so this run shows no reduction or increase in warning volume: Incident 446; TaskDuration 326; SimulationDrivers 83; BusinessFeed 8; StoreRestock 7; Uncategorized 6; FreightAgreement 3; AnimalCare 2; Felling, LogHaul, OffMapJourney, SawmillProduction, and SawmillTransfer 1 each (886 total). Genuine shortages/deferrals and repeated categories remain; this pass did not suppress warnings.

## Repair batch after the run

- `Assets/Scripts/Domains/Economy/Transport/OpeningTransportPresentationController.cs`: supplements authored hitch candidates with up to 48 nearest-first roadside ground candidates. Each is still validated against sampled terrain, horse/wagon physical footprints and obstacles, hitch-fixture cell, custom navigation routes and wagon turning clearance. No commercial-property authorization was added and no invalid route is accepted. Refusal diagnostics now report up to 12 candidate examples plus an omitted count.
- `Assets/Scripts/Domains/World/Pathing/FreightStagingCellSelector.cs`: added deterministic nearest-first roadside-ground candidate collection. It requires actual adjacent road access but does not require a parcel or ownership permission.
- `Assets/Scripts/Tests/Editor/World/FreightStagingCellSelectorTests.cs`: added tests for bounded nearest-first parcel-independent candidates and rejection of ground without adjacent road.

This batch addresses the observed town-grid-edge staging failure in source, but the repair was made after the sole PlayMode run. It is therefore not yet verified in live gameplay and cannot be claimed to have enabled wagon presentation or resolved the Millbrook destination/presentation refusal. The latter remains the first unpassed freight transition.

## Verification

- Fresh Unity script recompile: completed, `failed=false`, zero compiler errors.
- Focused EditMode tests after the final source edit: `FreightStagingCellSelectorTests` 12/12 passed.
- Related focused suites in the same repair batch: `FreightDispatchActivationIntegrationTests` 5/5, `FreightOffMapDepartureTests` 7/7, `TransportPairPlacementTests` 6/6. Total 30/30.
- `git diff --check`: passed.
- Unity status: ready, not compiling, Play Mode stopped; left in Edit Mode.

## Remaining defects and limits

- Retest the roadside fallback in a later PlayMode iteration. Confirm startup transport presentation, then verify actual pickup and delivery for the already-paid Millbrook lot. Do not charge again or recreate the lot.
- The departure gate's observed message conflates missing destination trigger and unavailable coupled presentation. The run log proves the refusal, but does not distinguish which side was unavailable. Diagnose that specifically when retesting.
- Unity Console history still contains repeated load errors for `Assets/Environment/Buildings/Building Animations/Single Story Business Building White Plaster Animation 1.anim` (file reported corrupted or serialized by a newer Unity version). The simulation monitor captured no run-time errors; the Unity Editor history did record these asset-load errors. The authored animation was not altered in this economy pass.
- Warning totals remain high and unchanged from the prior iteration. The categories are preserved for targeted follow-up; this pass did not conceal genuine incidents or broadly silence warnings.
- No second PlayMode session was run, per the one-simulation instruction.
