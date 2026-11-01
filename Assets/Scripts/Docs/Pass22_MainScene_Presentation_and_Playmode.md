# Pass XXII — Main Scene presentation checkpoint

Date: 2026-10-30  
Project: Land & Ledgers, Unity 6000.4.2f1  
Scripts branch/HEAD at verification: `integration/pass12-unity-readiness` / `c4b41dba6c7c70de3f972b6dcc902190d03745d8`

## Scope

This checkpoint records a targeted Main Scene correction made through Unity's Inspector to the existing `TownGenerationSettings` asset referenced by the Main Scene's `TownWorld` instance. No resource, parcel, mineral, TerrainData, or scene object data was deleted. The asset's `.meta` file was not changed.

## Authored setting changes

The following were disabled in `Assets/Core/World/DefaultTownGenerationSettings 1.asset`:

- All regional debug/inspection markers, while keeping `syncRegionalDebugOverlaySettings` enabled so the runtime overlay follows the asset.
- `showPlots`, which creates developer plot-perimeter visuals.
- `showResourceDistricts` and `showResourceProtoSites`, which create colored resource-area and remote-site visuals. `showResourceSuitabilityOverlay` was already disabled.
- `generateDetailProps`, which generated scattered agricultural-yard support props.
- `generateForestEnvironmentDressing`, which enabled procedurally scattered forest dressing.

The changes preserve the underlying regional, parcel, mineral, forestry and agriculture data. Roads and buildings remain enabled. The separate deterministic working-site dressing path (`BuildAgriculturalWorkingSiteVisuals`) is not gated by these settings and remains a known visual-only representation; it was not changed in this checkpoint.

## Unity observation

The correct Unity 6000.4.2f1 Editor opened `Assets/Main Scene.unity`. In Play Mode, a fresh First Ledger start displayed the scenario start panel, then the visible objective panel with the authoritative eight goals, including the $15,000 owner-equity target. The HUD reported `IN PROGRESS — 0 of 8 required goals complete`. The player-facing cash/net-worth HUD remained visible. The HUD was observed after the real `ScenarioDirector` activation log.

In the observed run, the clock displayed January 1, 1886 at New Game and continued to January 2, 1886. This conflicts with the First Ledger `StartingWorld` date of 1870 and its start-panel narrative. `TimeManager` stores non-negative absolute days relative to an 1880-03-01 anchor; applying 1870 through the ordinary date jump clamps it to the anchor. No date mutation was made because correcting this safely requires a calendar/save-compatibility decision, not a presentation-only jump.

The Unity Console showed no errors at the time inspected and 15 warnings during the continuing session; warnings were not fully triaged. The test runner was not executed in this checkpoint. The Editor process reached roughly 5.6 GB working set with under 300 MB physical memory free, so further suite execution was deferred to protect the active project/session. This is a live visual Play Mode observation, not an automated test or 30-day simulation.

## Preservation

Before editing, the asset and `.meta` were copied to:

`F:\Land & Ledgers_Pass22_Backups\20261030_pre_world_generation_settings`

After editing, both were copied to:

`F:\Land & Ledgers_Pass22_Backups\20261030_post_presentation_settings`

The post-edit asset SHA-256 matches its backup: `6E2FA579BBF2EAE40244EE2FDC8D22BDAD714BD079CB8CD926596561A049F796`. The `.meta` SHA-256 remains `FE25BA8C6E5A77F8135A8EA3BB1F96AF957B976F95F0EC1F39B59BF4EA81DF71`.

## Remaining work from the read-only review

- Reopen the project with adequate memory and run the current full/targeted EditMode and PlayMode suites.
- Verify settings effects after a fresh world regeneration; the current screenshot showed no regional colored debug rectangles, but was not a before/after automated comparison.
- Add a clear authored gate for visual-only agricultural site dressing, then bind actual animal/production visuals to authoritative identities before presenting them as live simulation.
- Resolve the First Ledger start-date/calendar epoch inconsistency with explicit migration and save-compatibility tests.
- Continue the identified journey-location, rent, animal-feed, birthday, Person lifecycle, ownership/formation, production, conservation and 30-day Main Scene work; none of those are declared fixed by this checkpoint.

## Pass XXII continuation — Unity CLI regression and birthday diagnostic — 2026-10-30

The Unity 6000.4.2f1 CLI completed fresh tests against the current source tree after the birthday logging change:

- `LandLedgers.EditorTests.Population.BirthdayChronologyTests`: 8/8 passed, 0 failed, 0 skipped. XML: `F:\Land & Ledgers_Pass22_Backups\pass22-reports\pass22-birthday-chronology-editmode.xml` (SHA-256 `04F1C102B6858DBF4BB27B78059B3FA5E40B84F421BD2066344A6A647972A8F4`). The added regression confirms that a due opening-day event may preserve the already-derived age, reports “birthday observed; age remains …”, and leaves exactly one next birthday scheduled. This corrects misleading diagnostics; it does not establish a full-year Main Scene birthday run.
- `LandLedgers.PlayMode.PlayableScenarioStartupPlayModeTests`: 11/11 passed, 0 failed, 0 skipped. XML: `F:\Land & Ledgers_Pass22_Backups\pass22-reports\pass22-playable-startup-full.xml` (SHA-256 `3E69FDB68EBE2DA286ED91D940BA491F7164FF13D68C343E887B42E4765F4DE9`). This includes Main Scene start, winter field registration, journey network, honest First Ledger progress, seller-provenance coverage, and save/load cases; it is not RUN-004.
- The isolated `GeneralStoreDailySaleKeepsRealHouseholdAndPersonProvenance` invocation failed once because no household selected the newly formed player store. Its emitted daily ledger showed household reserve spending at other local sellers and off-map, while player-store sales remained zero. The same test passed within the subsequent complete 11-test startup class. This did not reproduce in the complete class, so the isolated failure is not yet classified as a production sale defect. The test should be made deterministic without bypassing seller choice or fabricating demand before treating it as a stable positive-sale proof.

The birthday change is in `Assets/Scripts/Domains/Population/BirthdayService.cs` with regression coverage in `Assets/Scripts/Tests/Editor/Population/BirthdayChronologyTests.cs`. No scene or prefab files were changed in this continuation. The nested Scripts checkout was compiled by the fresh EditMode and PlayMode invocations above.

## Pass XXII runtime verification and birthday boundary repair — 2026-10-30

The current source tree adds a production-order birthday regression and a real Main Scene 30-day simulation check.

- `BirthdayChronologyTests`: 9/9 passed, 0 failed, 0 skipped. XML: `F:\Land & Ledgers_Pass22_Backups\pass22-reports\pass22-birthday-chronology-final-tree.xml`; SHA-256 `6469C5D2BD95CE28FB61DD1BD74703836D2DF18CBC15A4F6CECA0A074416D171`.
- `MainSceneFirstLedgerRunsThirtyDaysThroughAuthoritativeDrivers`: 1/1 passed, 0 failed, 0 skipped. XML: `F:\Land & Ledgers_Pass22_Backups\pass22-reports\pass22-main-scene-30-day-birthday-instrumented.xml`; SHA-256 `E69AA7DA8B80CAA5EFB9E2CC96AC96DB587FD16EB2204580F5204FF96A1D991D`.
- The integration run advanced Main Scene from day 2132 to 2162 through the production TimeManager and ordinary daily/weekly callbacks. It held at 98 unique Persons, 28→31 Households and 10 Businesses. Person IDs and Household IDs were unique at both snapshots; no First Ledger Victory was forced.
- Opening/closing diagnostic totals: Business runtime cash 217,000c→324,666c; household-ledger cash 122,259c→113,565c; Business category stock 1,321u→1,141u. These partial totals are not a conservation proof: the trace does not include all external counterparties, item lots, obligations, bank/civic balances or all transaction journals.
- The run recorded 55 persisted Tasks (1 Complete, 54 open) and 50 generated workplace assignments while the formal EmploymentRelationship registry contained 0 records. This is a confirmed source-to-runtime employment representation gap requiring a separate authority-mapping repair; the generated workplace mirror is not evidence of formal wages or payroll.
- Horse A0 received 0/2 feed units on each of the 30 days, and the condition degraded. This is a real opening-stock shortage; it is not evidence that the feeding diagnostic is false. No free feed was added.
- The earlier runtime trace recorded 64 repeated same-date birthday dispatches per affected Person. The cause was day-boundary short ticks being drained before calendar rollover, while the driver passed the prior/current day index to date-sensitive handlers. `SimulationDrivers.ProcessDueEvents` now derives the handling day from each due event’s absolute timestamp. The daily demographic revalidation begins on the following day after midnight events were dispatched. The fresh instrumented run observed birthday events without any Person/date dispatched more than once; its in-test assertion passed. Birthday ages and event state still need a longer save/load and leap-year Main Scene run.
- Full `PlayableScenarioStartupPlayModeTests` against the current birthday/runtime changes: 11/12 passed, 1 failed, 0 skipped. XML: `F:\Land & Ledgers_Pass22_Backups\pass22-reports\pass22-playable-startup-final-tree.xml`; SHA-256 `32CF285AAD245D0E94B5EA874C96B0BBD7FE84B815A41666B4F16BE784036253`. The single failure is `GeneralStoreDailySaleKeepsRealHouseholdAndPersonProvenance`: the stocked store received zero household customers in that generated world. Run logs show existing reserve demand and other local/off-map supplier activity, but no player-store sale. This has passed in a prior complete class and failed both isolated and full-class runs on other world seeds; it is not a reliable positive-sale proof. Do not force demand or bypass seller selection.
- The same PlayMode output exposed the authored HUD's `✓` completion marker was missing from the bundled LiberationSans SDF and rendered as replacement squares. `ScenarioObjectivePresentation` now uses ASCII `[X]` / `[ ]` status markers. Focused presentation EditMode tests passed 8/8 (XML `F:\Land & Ledgers_Pass22_Backups\pass22-reports\pass22-scenario-presentation-ascii.xml`, SHA-256 `A1D706A80363E104230F9EDC4809E44FB3E0846CC9DC85D9636C0FDF03CC696F`). The final strengthened scenario-start PlayMode test passed 1/1 with zero checkmark font warnings (XML `F:\Land & Ledgers_Pass22_Backups\pass22-reports\pass22-scenario-hud-ascii-final.xml`, SHA-256 `29D548D0B8B4F2A25844A507177D1BD8FCE2E1AE1DD05EE666B70BD604CD68C7`).
- This is automated PlayMode execution of the real Main Scene, not a manually observed interactive play session. It does not validate day/night visual actor persistence, complete economic conservation, rent settlement, animal feed procurement, farming Tasks, or freight.

The source changes are limited to `Domains/Population/PopulationManager.cs`, `Orchestration/Systems/SimulationDrivers.cs`, `Tests/Editor/Population/BirthdayChronologyTests.cs`, and `Tests/PlayMode/PlayableScenarioStartupPlayModeTests.cs`. No Unity scene, prefab, ScriptableObject or `.meta` asset was modified in this continuation.

## Pass XXII continuation — contradictory shipment restore quarantine — 2026-10-30

The v2.2 First Vein handoff's `CreateBlockedShipment(...)` conservation warning was traced through production refusal callers and save/load. Normal pre-load refusals already create terminal failed records with zero cargo; caller-side stock reservation/rollback is handled separately. A narrower legacy-restore edge remained: `LogisticsShipmentState.FromSaveDto` treated `loadApplied == false` plus any blocked reason as proof cargo had never left the source, even when `sourceCommittedAtSchedule == true`. That contradictory record can represent cargo whose source stock was already removed, so zeroing its shipment quantity could erase physical custody on restore.

The migration now zeroes cargo for a non-legacy blocked save only when both source-commit and load markers are false. A non-legacy record with committed source stock, no load marker, a blocked reason, positive cargo, and no prior delivery is quarantined in `Failed` state with its cargo and contradictory markers preserved. The known historical `blocked:` records still receive the targeted phantom-cargo repair. This keeps the shipment non-deliverable while retaining physical units for investigation.

Fresh Unity 6000.4.2f1 EditMode result on the modified tree: `LogisticsConservationGateTests` 17/17 passed, 0 failed, 0 skipped. This includes the new `ContradictoryCommittedShipmentSave_PreservesCargoInFailedCustody` regression and all prior zero-cargo blocked, caller rollback, retry, legacy repair, delivery refusal, and post-load recovery cases. NUnit XML: `F:\Land & Ledgers_Pass22_Backups\pass22-reports\pass22-logistics-conservation-final-tree.xml`; SHA-256 `E1D3A2A060A5120D769758A5E3A70458512201EAC2475B31F9929271D2172688`.

This does not establish freight operation in the Main Scene or prove the entire economy conserved. Source/test changes are limited to `Domains/Economy/Logistics/LogisticsModels.cs` and `Tests/Editor/Economy/LogisticsConservationGateTests.cs`; no authored Unity scene, prefab, ProjectSettings, terrain or `.meta` asset changed.

## Pass XXII final-tree recheck — 2026-10-30

After the shipment restore change, the actual Main Scene RUN-004 PlayMode test was rerun against the same source tree: 1/1 passed, 0 failed, 0 skipped. Unity 6000.4.2f1, `PlayableScenarioStartupPlayModeTests.MainSceneFirstLedgerRunsThirtyDaysThroughAuthoritativeDrivers`. NUnit XML: `F:\Land & Ledgers_Pass22_Backups\pass22-reports\pass22-main-scene-30-day-final-tree.xml`; SHA-256 `9A98AE9881E21430FE8B31629446EBAA54BF901D69E7766B60ED0A56AD507319`.

The run advanced the authoritative calendar from day 2132 to 2162 (January 1 to January 31, 1886). Opening/closing snapshot: Persons 94/94 unique; Households 28→33; Businesses 10; Business runtime cash 217,000c→321,950c; household-ledger cash 145,704c→142,617c; Business category stock 1,321u→1,143u; Tasks 0→56 (1 Complete, 55 Open); generated workplace assignments 54; formal EmploymentRelationship registry 0. The First Ledger was not forced to Victory. These partial totals remain non-reconciled and are not a conservation proof.

Horse A0 received 0/2 feed units each day and degraded; the run did not create free feed. The startup still reports a same-age birthday observation on January 1, followed by dated age changes for other Persons later in the month; the chronological duplicate-dispatch assertion passed. The 1886 clock versus the authored First Ledger 1870 start remains unresolved. The task completion ratio and formal employment mapping remain open. This is automated Main Scene PlayMode, not a manual visual session or interrupted day-15 save/load run.

The most recent complete `PlayableScenarioStartupPlayModeTests` class report remains 11/12 with one failure in `GeneralStoreDailySaleKeepsRealHouseholdAndPersonProvenance`. Independent source tracing classifies the positive-sale assertion as nondeterministic setup: it directly calls the store's daily resolver on the same day the bootstrap already resolved household shopping, does not establish an outstanding affordable need, and does not ensure the player store wins the legitimate seller comparison. A separate limitation remains: the reported Person ID is inferred as the first eligible adult member, not supplied by a completed shopping journey. No demand, score, or person identity was fabricated to make the assertion pass. Keep the positive autonomous retail case open until the test follows a real later shopping opportunity and actor provenance is tied to the ordinary journey/purchase authority.

Final overnight handoff verification on the post-logistics source tree:

- Full `PlayableScenarioStartupPlayModeTests`: 12/12 passed, 0 failed, 0 skipped in Unity 6000.4.2f1. XML: `F:\Land & Ledgers_Pass22_Backups\pass22-reports\pass22-playable-startup-postlogistics.xml`; SHA-256 `D466AEADF4ABEBB4C14E630057747C4D4D15220D7F22902BE064E2AB6EDD79B1`.
- The 30-day Main Scene test independently passed 1/1 on this same tree; see the final-tree RUN-004 result above.
- The positive General Store sale assertion passed in this generated run. This does not clear the broader issue: prior seeds produced zero player-store sales, and `LastCustomerPersonId` still represents an inferred eligible adult rather than a Person verified as the actual journey actor. The suite pass proves one legitimate household reserve sale was processed by the production resolver under this generated state; it does not establish repeatable positive sales across seeds or an embodied shopping journey.
- Fresh EditMode `LogisticsConservationGateTests`: 17/17 passed on this same tree; see the shipment-restore section above.

Unity batch test processes exited normally and did not leave the project in Play Mode. The next handoff state is the authored Main Scene open in Edit Mode.
