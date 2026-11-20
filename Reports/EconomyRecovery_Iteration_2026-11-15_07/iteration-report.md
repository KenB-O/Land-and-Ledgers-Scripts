LAND & LEDGERS — ECONOMY RECOVERY ITERATION 07
Date: 2026-11-15

PLAYTEST
- Actual Unity Editor 6000.4.2f1, Main Scene, Small-town runtime.
- One bounded simulation completed: 60.00096 simulated days, 250x (15,000 simulated seconds/real second), 339.43 real seconds.
- Start/end AbsoluteGameSeconds: 25,560.36 → 5,209,642.96.
- Nine residents managed and visible at completion. Zero capped rollover or short-tick frames; zero pending simulation time.
- Captured 1,582 runtime entries: 557 logs, 1,025 warnings, 0 runtime errors. Full stream and day checkpoints are in console-playmode.jsonl and daily-checkpoints.csv.
- Unity returned to Edit Mode before source changes; no second PlayMode run was made.

CONFIRMED FREIGHT DEFECT AND REPAIR
- Shipment freight-shipment:freight-request:millbrook_grain_co:opening-flour-b1 reached bakery_001 on day 6. Its unload began after the day-6 short-tick retry.
- P7’s freight receiving journey could be replaced by a later explicit business-feed route. TryCarryFreightToActivity returned success for any already-active movement and marked the destination while leaving the actual route unchanged. TrySendAgent also reset the receiving route mode to Scheduled. TryStartExplicitRoute could then retarget an active task/scheduled route to a different building.
- Runtime evidence: receiving remained pinned on days 7–35; P7 later performed business-feed travel while the paid cargo remained in wagon custody. The shipment did ultimately complete on day 36. The receiving lot was credited once to bakery_001, the 125c freight charge was collected from the payer before carrier credit, and the agreement fulfilled. This is delayed successful receipt, not acceptable delivery reliability.
- Repair: task-owned receiving routes retain TaskDirected ownership after route start; explicit routes refuse to replace a conflicting active task/scheduled route; freight carrying does not claim an unrelated active journey as progress. Only ambient or unowned movement can yield to an authorized freight receiving task. The task can retry after the protected route resolves; no route is teleported or falsely arrived.
- The specific repair is source- and EditMode-verified only. Per the one-playtest limit, it was not tested in PlayMode after the repair.

ECONOMIC OBSERVATIONS
- Glen P3’s Millbrook order: Bakery cash 35,000→34,952c; supplier stock 460→450 lb; Millbrook cash 0→48c for the identified 10 lb lot aidlot-millbrook_grain_co:opening-flour-b1.
- The accepted freight charge was 125c. At day 36, receipt and payer-first freight settlement occurred once; the 10 lb lot entered Bakery inventory once. Runtime did not record exact post-settlement cash balances in the captured message, so none are inferred here.
- Bakery production resumed after receiving the lot. By day 60, 34 batches of 24 loaves each had been baked and wholesaled; recorded wholesale revenue was 3,264c. Bakery logs recorded 28 local 10 lb flour purchases totaling 812c. This confirms continued production and sales records, not net profitability.
- No sawmill business existed in the live scenario; forestry, log hauling, sawmill production, and yard transfer correctly skipped for missing scenario business data.
- Store stockouts, open household needs, animal-feed shortage, and tools_hardware receiving-capacity waits remained visible; no stock or cash was fabricated.

WARNINGS
- Warning categories during this run: 492 Incident (open needs / changing causes), 395 TaskDuration (terminal overrun diagnostic records), 104 SimulationDrivers, 10 StoreRestock, 8 BusinessFeed, 3 FreightAgreement, 2 AnimalCare, and 1 each Felling, LogHaul, OffMapJourney, SawmillProduction, SawmillTransfer; 6 unprefixed warnings.
- Previous 60-day run: 914 warnings (458 Incident, 341 TaskDuration, 84 SimulationDrivers). This run increased by 111 warnings; the largest growth reflects additional open-need incident updates and completed task-duration records. No warning suppression was added. Food stockouts, unavailable sellers and scenario omissions are not treated as successful activity.
- After compilation/tests, the Console showed a repeated asset-load error for the pre-existing binary White Plaster animation file (“File may be corrupted or was serialized with a newer version of Unity”). It is unrelated to this economy repair and was left unchanged to protect authored assets.

CHANGES AND VERIFICATION
- Runtime source touched: Assets/Scripts/Domains/Population/PopulationPathingDirector.cs.
- Regression coverage touched: Assets/Scripts/Tests/Editor/Population/PopulationPathingProfessionRhythmTests.cs.
- Unity script recompile: completed; failed=false, errors=[], compilationFailed=false.
- PopulationPathingProfessionRhythmTests: 27/27 passed.
- FreightProductionDriverTests: 12/12 passed.
- Console runtime errors in the captured 60-day run: 0. Current compile/test Console contains the pre-existing White Plaster animation load error above.
- Play Mode remains stopped; Editor remains responsive and in Edit Mode.
- No commits, pushes, merges, or resets.
