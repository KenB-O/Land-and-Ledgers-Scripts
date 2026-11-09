# Economy Repair and PlayMode Loop — 2026-11-09

## Scope and preservation

Work was limited to one confirmed freight-negotiation retry defect and its focused verification. No terrain painting, terrain-data edits, scene edits, pricing changes, free inventory, forced task completion, or save-slot changes were made. Unity was returned to Main Scene in Edit Mode. Existing dirty assets and recovery work were left untouched.

Starting source revision: `8baa2473c72c365e15621f887b3e0cb0b2056cc8` on `integration/muse-regional-economy-2026-11-05`.

## Repair

`SimulationDrivers.DriveMillbrookFreightNegotiation` previously returned whenever `ContactTravelTaskId` referenced any open task. A failed scheduling attempt leaves that travel task open and queued; the request then kept that task ID and negotiation never retried the assignment. The driver now reuses a queued contact-travel task and retries assignment on later negotiation ticks. Assigned or in-progress tasks remain owned by their existing schedule. Completed or stale tasks clear the request reference and may be replaced. No cargo, delivery, payment, or settlement logic changed.

A focused regression test was added for queued-versus-assigned eligibility. Unity Test Runner filtering was inaccessible in this session, so that EditMode test was not executed. It remains a test artifact, not a passed test.

## Fresh PlayMode evidence

- Scenario: fresh Open Frontier NewGame.
- World seed: `1014635433`.
- Market seed: `311291151`.
- Speed: selected 100x; one configured 1x day is 300 seconds. The run reached simulation day 50. Wall time was roughly three minutes by Editor clock, not stopwatch-measured.
- Runtime stability: the simulation continued through day 50. The bounded log contains no C# exception or runtime exception pattern. Town and household daily processing continued beyond day 4; the previously reported disappearance was not reproduced.
- Raw bounded Editor log: `Reports/Diagnostics/OPEN_FRONTIER_FREIGHT_RETRY_DAY50_2026-11-09.log` (1,045,368 bytes). Keep local: it contains Unity/Editor environment details and was not added to the publication.

### Freight

The opening flour shipment completed its legitimate lifecycle:

1. Day 1 request was blocked once because P3 had no available work window.
2. The request later progressed; carrier agreement was accepted on day 2 for one identified 10 lb flour sack at 125c.
3. Ethan/A0/wagon preparation and dispatch tasks completed on days 3–4.
4. Physical route returned on day 5; shipment arrival was logged day 6; unloading began day 7.
5. On day 8 the named lot `aidlot-millbrook_grain_co:opening-flour-b1` was credited once to `bakery_001`; Bakery cash was debited and verified for 125c; `livery_freight_003` received 125c; the agreement was marked fulfilled. The draft horse/wagon team was released.

This verifies the first complete freight transaction in this run. A second shipment was not observed, so repeat freight remains unverified. This result is consistent with the queued contact retry repair, but the EditMode regression test was not run.

### Households and businesses

- Day 1: six purchases, 182c total; six purchase receipts were logged to household inventory. There were no later purchases in the bounded 50-day run because the General Store reported no sellable food offers/stock.
- The authoritative daily-need summaries show real consumption: 38 meals eaten and 106 missed slots across the logged days 3–10. The 54 per-person missed-meal warning records are not a count of meals eaten. Day-one purchases were followed by household inventory receipts; the daily summaries then show consumption declining (15 eaten/3 missed on day 3, 12/6 on day 4, 7/11 on day 5, 4/14 on day 6, then 0/18 on days 7–10). This proves the purchased food reached the meal-consumption authority and was depleted. Household purchases and daily-need summaries stop appearing after day 10, although shopping diagnostics continue through day 50; determine whether this is a normal zero-input/reporting condition or a daily-needs driver/lifecycle defect before claiming the later-period meal state.
- Bakery: zero completed batches and zero wholesale bread transfers in this run. A 24-loaf pipeline plan remained covered despite the batch not producing. Notions were at zero and baking prechecks refused during the observed early days; purchase orders quoted 14-day transit. The run did not show a successful notions receipt or production after the nominal lead time. Investigate recipient ownership and arrival processing before changing the 14-day policy.
- No price or recipe policy was changed. The observed standard batch economics remain 68c ingredient cost against 48c wholesale revenue; do not silently alter approved price terms to compensate.

## Compilation and validation

The first Unity compilation attempt in this session reported an Editor-only C# error in the untracked `Assets/EnvironmentDevelopment/Editor/RegionalTerrainAuditAndPaint.cs` (`Directory.CreateDirectory` resolving to `LandLedgers.Directory`). The current file content already uses `System.IO.Directory.CreateDirectory`. After triggering a fresh source refresh, the latest Unity log records successful Tundra builds and assembly reloads with no subsequent C# errors. The runtime `Assembly-CSharp.dll` compiled; the earlier failure was in the Editor assembly build. No EditMode or PlayMode test suite was executed; the 50-day run is the PlayMode evidence.

## Changed files for this development slice

- `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs`
- `Assets/Scripts/Tests/Editor/Economy/FreightDispatchActivationIntegrationTests.cs`
- This report

The raw PlayMode log remains local and is deliberately excluded from Git. No other authored or dirty files were staged.
