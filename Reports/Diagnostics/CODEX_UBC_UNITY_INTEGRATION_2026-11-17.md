# Codex Unity Integration and 60-Day Playtest — 2026-11-17

## Status

Muse source integration is verified at `8cb914eaa7facbf882b40a27b4aae3cdd007198e`. Unity compiled the scoped source fixes, and focused EditMode suites ran. Two complete 60-day real PlayMode runs completed at 250x: a pre-fix run exposed a sawmill workstation-owner-ID mismatch, and a post-fix run verified the corrected lookup reached task assignment. The post-fix run then showed a separate labor-time refusal that still prevented lumber production. A third attempt, run ID `20261117T220225Z-e0f61aea`, is the post-fix run; its completion metadata was initially misread during handoff and is corrected below.

## Source and integration

- Muse branch: `muse/universal-business-construction-2026-11-17`
- Muse tip: `8cb914eaa7facbf882b40a27b4aae3cdd007198e`
- Bundle: `ll-ubc-integration-2026-11-17.bundle`, 619,020 bytes
- Bundle SHA-256: `4ef5738665ff9fbb69cf4004b005a1857a345005f0eb3b94b0e1220e6db56bc4`
- `git bundle verify` passed; bundle ref and ancestry were verified from the supplied foundation (`a1c20ad9e8e8320cec68fb476616e88daa9457ce` is an ancestor; 69 commits are reachable after it). The Muse tip is a parent of this Codex integration branch.
- Unity project: `F:\Land & Ledgers`; Unity `6000.4.2f1`.
- No scene, prefab, furniture, terrain, font, `.meta`, or other owner-authored asset was edited or saved.

## Unity compilation and tests

Compilation completed with zero C# errors after the final source change.

Focused EditMode test evidence:

| Suite | Passed | Failed | Result |
|---|---:|---:|---|
| `UBCJ1ManufacturingNetworkTests` | 4 | 0 | PASS |
| `NpcConstructionProcurementAdapterTests` | 8 | 0 | PASS |
| `PhaseGNpcBusinessLifeTests` | 8 | 0 | PASS |
| `OpeningEconomyDef013Tests` | 13 | 0 | PASS |
| `UbcA1AcquisitionContinuityTests` | 10 | 4 | FAIL |
| `DevelopmentTownPopulationGenerationTests` | 7 | 0 | PASS |
| `SawmillWorkstationEstablishmentTests` after the Codex fix | 14 | 0 | PASS |
| **Total across these runs** | **64** | **4** | **4 unresolved test failures** |

The four A1 failures were: two listing/purchase tests expected a General Store sale listing but received null; one fail-loud test produced an unanticipated `Debug.LogError` in the test; one acquisition continuity test triggered a Unity error loading `Assets/Environment/Buildings/Building Animations/Single Story Business Building White Plaster Animation 1.anim` (“may be corrupted or serialized with a newer version”). The animation asset was not changed. These are Unity-observed failures; no reproducible baseline was available to call them pre-existing. The full 8,288-test Editor suite was not run.

## PlayMode runs

### Completed run — pre-sawmill fix

- Run ID: `20261117T214151Z-ac7b3530`
- Scene/scenario: `Assets/Main Scene.unity` / `millbrook-development-town`
- Startup: New Game; seed 1886; 250x; 86,400 game seconds per simulated day
- Calendar: Year 1870, Month 3, Week 1, Day 1 through Year 1870, Month 4, Week 9, Day 30
- Duration: 7m 10s wall time; 60/60 completed days
- Startup gate: PASS — bootstrap, town, population, households, businesses, hubs, drivers, and required Pass 9 holders were present.
- Runtime counts: 98 residents, 8 households, 7 businesses. Configuration listed 14 authored people; the runner checks population is at least that number, not equal. This difference was observed but not independently explained.
- Console: 3,054 logs, 1,237 warnings, 0 errors, 0 exceptions.

Full, unfiltered artifacts are in `Reports/Playtests/Runs/20261117T214151Z-ac7b3530/`:

- `console-full.txt` — 4,559,989 bytes; SHA-256 `101D3791E74F3703D7D6396EBB79AEBC28BFEEA01343C0FBE40597C6B2B2C7D3`
- `console-events.jsonl` — 4,821,231 bytes; SHA-256 `EE828CE73A006CE386DF3DF766111C078423631AE9495129380A5EF8E6B7D4B9`
- `daily-summary.csv` — 22,503 bytes; SHA-256 `F84287C7F8705CF704C5E3C5375377F147AA82430E4CC89F9FE7A9CC441E251D`
- `entity-snapshots.jsonl` — 3,027,886 bytes; SHA-256 `CC246419A9B4F6FA5A19128AFB359F4C4990F4816F8E184A478ECD9B7691C71D`
- `metadata.json`, `final-state.json`, and `playtest-report.md`

The log and state hashes above were computed from the completed run artifacts.

### Earlier runner attempts

- `20261117T212512Z-57cdf3de`: INCOMPLETE, 0/60. The Editor reloaded before `isPlaying` became true and the runner dropped the armed session.
- `20261117T213318Z-715bf690`: INVALID, 1 observed day. Startup gate passed, but the runner left its 30-second startup timeout active and later aborted.
- The runner fixes preserved the armed session during the transition and clear the startup-await flag as soon as the gate passes. The 60/60 run above verifies those fixes.

### Completed run — post-sawmill workstation fix

- Run ID: `20261117T220225Z-e0f61aea`
- Metadata status/result: COMPLETED/PASS; startup gate passed; authoritative clock completed 60/60 days, Year 1870, Month 3, Week 1, Day 1 through Year 1870, Month 4, Week 9, Day 30. Seed 1886, `Speed250x`, 86,400 game seconds/day. Runtime population 98, 8 households, 7 businesses.
- The Unity process remained responsive after the run. The runner marked completion and stopped PlayMode; no scene save was requested or performed.
- Console: 3,040 logs, 1,223 warnings, 0 errors, 0 exceptions. The earlier statement that this run stopped before startup was incorrect.
- The workstation-owner lookup regression is now passed in focused EditMode coverage and the live sawmill job advanced beyond the former “workstation not established” refusal. The job instead failed assignment because worker P11 had 0 of 600 daily work minutes available for an 81-minute task. No lumber was produced; the remaining labor/schedule gate requires Muse investigation.
- Full raw console and structured evidence are preserved in `Reports/Playtests/Runs/20261117T220225Z-e0f61aea/`. SHA256: `console-full.txt` `AA581F6F75FBFD4030F3B0CA76BB9CD0AD73E484FC324F3CE019A3FE5D8E5905`; `console-events.jsonl` `505028805ACBA03746A000D347F6B2FBBF0EA7B65638EBD78154758CEBB8B4CA`; `daily-summary.csv` `953FA7D925E76495E4A997DAECCFBC6E027BFE837E7CF04029251D8331187FBA`; `entity-snapshots.jsonl` `8E89A2D65A3D815207EBF42365FCD81FCBF0059BD01D3FE4DAEE9FCAAC09EC61`.

## Runtime findings from the completed 60-day run

### Household food and retail

- The daily `[FoodEconomy]` diagnostics covered all 60 days: 1,645 meals eaten and 289 missed. Eight households were included in daily meal-resolution records; early and middle days had shortages, while days 47–60 reported no missed meals.
- Food charge reconciliation totaled 1,038 Phase C units / 5,759¢ and zero legacy units / cents. No `OVERLAP` or `MULTI-ITEM` flags appeared. This is a clean observation window, not proof that DEF-067 is closed.
- Snapshot provenance contains 63 distinct bread retail-receipt lots, across 7 households, representing 985 purchased bread units. Each says the item was purchased from Kennedy Baldwin-Ooms General Store for cash and was carried home under a unique `retail-v2` receipt identity. This is positive evidence of actual purchase and inventory custody. The snapshots do not independently expose the matching per-receipt seller credit or exact cash amount, so full cash-to-lot reconciliation remains open.
- Repeated bread counter stockouts and no-window trip refusals also occurred. The system correctly logged failed stockout attempts as not sales and left needs open. H1 had no bread purchase receipt in the captured household-lot snapshots.
- Daily snapshot aggregate cash on day 60: households 316,591¢; businesses 127,310¢. These are end-state totals, not a complete transaction-conservation reconciliation.

### Bakery and supply

- 40 batches were logged as baked; each produced 24 loaves. The run recorded 40 wholesale cash settlements of 96¢ each and 40 corresponding 24-unit wholesale transfers: 960 loaves and 3,840¢ wholesale receipts in total.
- 41 local bakery-flour purchase records total 53 sacks / 530 lb / 1,270¢. Opening stock and replenishment were finite; output was not manufactured from missing inputs.
- The paid 10 lb Millbrook flour order (48¢) obtained a 125¢ freight quote and a dispatch task eventually completed. The assigned shipment repeatedly remained pending while the driver walked to the staging point; there is no arrival/receiving evidence. It must not be reported delivered.
- The shared opening freight wagon remained bound to that pending shipment. Logging-camp supply attempts consequently reported that they could not create a shipment because the wagon was already bound. This is a confirmed freight blockage requiring Muse follow-up.

### Horse feed, forestry, sawmill, and construction

- The daily animal-care driver ran. The external market supplied real 20-sack feed lots to the General Store; livery feed purchases/consumption occurred. A0 had one recorded at-risk event where no registered feed was available and nutrition changed 1.00→0.85. Forty-four supplier-lot receipt attempts were refused by the authorized `animal_feed` capacity check, with source lots restored; no capacity overflow was accepted.
- Logging/felling and hauling activity occurred, including logged wagon trips. The sawmill opened with configured workers, machinery, support, and a station establishment message, but its production job was refused daily.
- One business task exposed the sawmill defect: `sawmill:L1` could not start because TaskAuthority supplied the hashed numeric business EntityId, while the workstation registry was keyed by the business instance ID. The registry resolver passed to the equipment gate looked up the wrong key.
- A narrow hub fix now maps hashed runtime business IDs back to their registered business instance, refuses ambiguous hash collisions, and does not create a misleading registry for unknown numeric IDs. The new test proves the hub resolves the same workstation for a real task-owner ID and fails closed for an unknown owner. The 14-test suite passes. In the post-fix PlayMode run, the task passed that workstation lookup and reached labour assignment, but was refused because P11 had zero scheduled work minutes; successful lumber production remains unverified and the labor-time blockage is a confirmed runtime issue.
- One generic business, `biz_Generic_006` / Don Millward Business, received a week-56 closure event after cost-cutting; log reports settled 0, assumed 0, liquidated 0, staff released 0. The lumberyard separately cut costs. No bankruptcy event was found. Investigate closure-state completeness and owner records; do not assume it is harmless.
- NPC construction was registered for the scenario, but no completed NPC construction progression was verified in this run.

## Warning/error observations

Top warning groups in the 1,237 captured warnings: SimulationDrivers 582; Incident 242; FoodEconomy 146; no tag 101; OffMapJourney 58; FeedSupply 44; BusinessFeed 21; LogHaul 16; TaskDuration 10; FreightAgreement 4; StoreRestock 3; and 10 singleton categories. The 53 negative-scale BoxCollider warnings are a subset of the untagged group. There were no captured runtime errors or exceptions.

Warnings include household meal shortages, repeated bread stockouts, the pending off-map shipment, a freight wagon held by that shipment, and accepted feed-capacity refusals. Do not suppress these messages to reduce counts.

## Codex source changes

- `Assets/Scripts/Editor/Playtests/Playtest60DayRunnerWindow.cs` — preserve armed sessions across Unity's play transition and stop startup timeout after a successful gate.
- `Assets/Scripts/Orchestration/Systems/SimulationSystemsHub.cs` — resolve numeric runtime task-owner IDs to stable business-keyed workstation registries; collision ambiguity fails closed.
- `Assets/Scripts/Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs` — restore independent hardware/glass restore diagnostic loops.
- `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs` — repair missing method brace/scope in the Blacksmith coal import registration bridge.
- `Assets/Scripts/Tests/Editor/Economy/UBC_A1_AcquisitionContinuityTests.cs` — use available public runtime lookup API.
- `Assets/Scripts/Tests/Editor/Economy/UBC_J1_ManufacturingNetworkTests.cs` — qualify ambiguous Unity 6 `EntityId` type.
- `Assets/Scripts/Tests/Editor/Economy/SawmillWorkstationEstablishmentTests.cs` — add the hashed business task-owner registry regression.
- `Assets/Scripts/Tests/Editor/Orchestration/SimulatedScenarioWorld.cs` — remove duplicate using.
- `Reports/Playtests/PLAYTEST_RUN_INDEX.md` and this report; completed and blocked run artifacts are separately identified.

No unrelated local assets were staged or modified. No gameplay balancing, scene/prefab authoring, save/load flow, or economic rules were changed.

## Follow-up priorities for Muse

1. Repair the shared-workstation ID handoff for any affected productive businesses and prove the sawmill job can start and complete once, with labor, inputs, blade state, output lots, and inventory custody conserved.
2. Repair freight staging/arrival for the paid Millbrook shipment. Avoid keeping the sole opening wagon indefinitely bound; preserve shipment custody and the paid obligation. Confirm camp-provision shipments can proceed legitimately afterward.
3. Trace the 289 missed meals and repeated bread stockouts by household/day. The run does prove 985 bread units reached seven household inventories; reconcile each receipt's household debit and store credit, then investigate H1's missing bread receipts. Keep DEF-067 open until transaction-level evidence reconciles.
4. Audit the generic business week-56 closure flow to establish whether liabilities, employees, assets, and ownership resolve correctly; record its real business identity and cause.
5. Inspect the four Unity A1 test failures and unreadable animation asset. No asset repair was attempted.
6. Repeat the 60-day run only after Unity recovers and the post-fix compile is stable. Use a fresh run ID; keep prior evidence immutable.

## Publication

This report and both completed raw runs are prepared for publication on the Codex development branch `codex/universal-business-construction-unity-integration-2026-11-17`, based on Muse tip `8cb914eaa7facbf882b40a27b4aae3cdd007198e`. Remote verification and the final commit SHA must be recorded after push. Do not merge to `main` or force-push.
Additional Muse iteration guidance: Reports/Diagnostics/CODEX_TO_MUSE_DEVELOPMENT_NOTES_2026-11-17.md.
