# Pass VI — Living Settlement / Playtest Backlog

## Pass XXII Main Scene presentation checkpoint (2026-10-30)

The actual Main Scene was opened in Unity 6000.4.2f1 and exercised in Play Mode. A fresh First Ledger start displayed the scenario objective HUD with the authoritative eight objectives, the $15,000 owner-equity target, and `0 of 8 required goals complete`; the HUD remained visible after the real `ScenarioDirector` activation. The observed clock was January 1, 1886, despite the scenario asset's 1870 start date. No calendar jump was applied because `TimeManager` clamps dates before its 1880-03-01 epoch; save-compatible correction remains open.

The Main Scene's referenced `DefaultTownGenerationSettings 1.asset` now disables regional debug markers, plot outlines, resource-district/proto-site overlays, agricultural detail props, and procedural forest dressing. The underlying resource/parcel/terrain data was preserved. Roads and buildings remain visible. The change is backed up before and after at `F:\Land & Ledgers_Pass22_Backups\20261030_pre_world_generation_settings` and `F:\Land & Ledgers_Pass22_Backups\20261030_post_presentation_settings`; the post-edit asset hash is `6E2FA579BBF2EAE40244EE2FDC8D22BDAD714BD079CB8CD926596561A049F796`, and the existing `.meta` GUID is unchanged. This does not gate the separate deterministic `BuildAgriculturalWorkingSiteVisuals` path.

The same Play Mode session showed two visible Person actors moving on the road, but no stable IDs or day/night continuity were inspected. The Console showed zero errors and 15 warnings; at least two warnings identify negative scale on a BoxCollider under a mirrored Doctor House window hierarchy. The warning set was not fully triaged. No new NUnit run or 30-day simulation completed: the Editor reached about 5.6 GB working set and the machine had under 300 MB physical memory free. See `Pass22_MainScene_Presentation_and_Playmode.md`. This is not a full playtest or conservation proof.

## Pass XVIII environment note (2026-10-29)

Unity 6000.4.2f1 script compilation remains unverified after repeated Bee/ILPP stalls, including in disposable trivial-script projects. A disposable Unity 6000.2.8f1 project compiled and passed one fresh EditMode test, while its PlayMode invocation discovered zero tests. No LL-PLAY item is marked fixed by this environment investigation. See the Pass XVIII reports for evidence and the required next gates.

Status recorded from the Pass VI source audit. Visual statuses remain unclaimed
until the Unity Editor can be inspected graphically.

| ID | Issue | Current status | Evidence / next check |
|---|---|---|---|
| LL-PLAY-001 | Incorrect starting ownership / ghost Businesses | PARTIAL | Root cause found: serialized identityless `BusinessInstanceState` carried Player owner and entered the shared registry; startup rejects identityless placeholders. A second ghost arose in presentation: `GeneralStorePanelController` counted any coordinator as an owned store even when `CurrentBusiness` was null. Projection/count now require a real Player-owned `GeneralStore` state. Fresh EditMode regressions pass 2/2 for zero phantom row and one real store row. Alternate scenario/Continue/New Game matrix and live ownership ledger verification remain. |
| LL-PLAY-002 | Excessive PlayMode-generated scene roots | Not tested | Requires Edit Mode vs Play Mode hierarchy inspection. |
| LL-PLAY-003 | Duplicate Civic Foundation | PARTIAL | One `CivicFoundationManager` is now authored on Main Scene `Game Systems`; the bootstrap resolves the existing component through its shared `FindOrCreate` boundary. The scene YAML has one such script reference and the saved scene is backed up. An expanded Play Mode hierarchy census has not yet confirmed the runtime count. See Pass XXI continuation record. |
| LL-PLAY-004 | Obsolete tutorial guidance | PARTIAL | Fresh First Ledger after Start showed no universal “hire first store worker” strip and authentic scenario objectives instead. Legacy guidance was visible in a separate stale/test-runner view; confirm all routes and restored saves. |
| LL-PLAY-005 | Missing scenario HUD | PARTIAL | Main Scene serializes an authored objective panel, label, viewport and toggle under the existing Canvas. First Ledger PlayMode coverage asserts the active title, authentic $15,000 goal, authored visibility and no duplicate fallback panel. Runtime output showed the old checkmark glyph was missing from the bundled TMP font; objective state now uses ASCII `[X]` / `[ ]` markers, with focused presentation tests 8/8 and a scenario-start PlayMode test 1/1 passing without the font warning. The latest whole startup class was 11/12 because the independent General Store sale test received no customer. Sandbox, failed/victory, scene reload and save/load visual cases remain unverified; no new screenshot was exported. |
| LL-PLAY-006 | Blacksmith management panel layout | Not tested | Visual Editor check required. |
| LL-PLAY-007 | Seller meeting modal behavior | Not tested | UI interaction check required. |
| LL-PLAY-008 | Repetitive acquisition buttons | Not tested | UI interaction check required. |
| LL-PLAY-009 | Missing sale-discovery provenance | Not tested | Read-model/UI inspection required. |
| LL-PLAY-010 | Rigid construction progression | Partially implemented | WorkPackages and construction tasks exist; live presentation needs verification. |
| LL-PLAY-011 | Business labels without real operations | Partially implemented | Generic runtime exists; audit each exposed BusinessType in Main Scene. |
| LL-PLAY-012 | Artificial staffing limits | Source covered; runtime pending | Labor demand is workload-driven in source; verify live report. |
| LL-PLAY-013 | Implausible profits / unclear periods | Not tested | Requires live event-backed report comparison. |
| LL-PLAY-014 | Civic tab language | Not tested | Requires current UI state inspection. |
| LL-PLAY-015 | Resource and district displays | Not tested | Requires current UI state inspection. |
| LL-PLAY-016 | Flat terrain | PARTIAL | Pass XXII hid the resource/region overlays, plot outlines, agricultural detail props and procedural forest dressing in the Main Scene's actual referenced generation settings, preserving underlying world data. A fresh Play Mode view showed the colored resource rectangles absent in the sampled opening view. Ground composition remains flat/unfinished, building-footprint treatment is not implemented, and the independent working-site dressing path remains active. |
| LL-PLAY-017 | Calendar weekday mismatch | Source covered; runtime pending | Gregorian tests passed previously; verify displayed Main Scene date/weekday. |
| LL-PLAY-018 | General Store startup/notices | PARTIAL | The observed fresh route had no player holding; legacy weekly settlement now safely reports no player store. Full notice classification and duplicate UI projection remain unverified. |
| LL-PLAY-019 | MainMusicPlayer lifecycle | Not tested | Requires Edit/Play hierarchy and console inspection. |

## Runtime dependency audit

| Dependency | Source status | Production evidence still required |
|---|---|---|
| Boarding-house registration / housing search | Adapter and hub persistence exist; normal bootstrap caller not found in source audit | Live boarding business must create rooms, register with `SimulationDrivers`, and participate in housing search. |
| NPC construction material sources | Household adapter exists; world supplier registration not found in source audit | NPC project must resolve real supplier inventory and custody. |
| Bank deposit projection | `BankCreditParticipant.RefreshDepositsFrom` exists | Bank runtime must refresh immediately before live underwriting. |
| Duplicate wage/debt/activity authorities | Compatibility systems remain in repository | Confirm actual writers in Main Scene and preserve one authoritative mutation path. |
| Main Scene 30-day proof | Controlled fixtures previously passed | Ordinary New Game path still requires runtime trace and reconciliation. |

Visual validation is currently pending because the available environment is
headless; no screenshot or visual-fix claim is made by this file.

## Pass XV update — 2026-10-29

The opening farm defect was traced to typed premises selection: the formation
context used first-fit buildings without requiring an agricultural site role.
Pass XV added a typed production-context selector and live field registration.
The old four-week First Ledger Victory expectation was corrected to honest
progress against the unchanged $15,000 threshold. Post-fix Unity validation was
incomplete because the Unity/Bee compiler runner stalled before NUnit output;
no visual backlog item is marked fixed from this pass.

## Pass XVI update — 2026-10-29

The Unity runner recovery attempt regenerated the Bee cache but still stalled
during IL post-processing of package assemblies before NUnit discovery. The
prior cache was preserved, and no gameplay or scene changes were made. The
Pass XV typed agricultural-premises correction remains Unity-unverified.
The Main Scene thirty-day proof, crop Tasks, growing-season harvest, freight
from farm output and day-15 continuation remain open.

## Pass XVII update — 2026-10-29

Disposable controls isolated the runner failure beyond the game project: a
clean Unity 6000.4.2f1 project without user scripts exits normally, while one
trivial script reproduces the Bee second-stage stall. The primary project was
not modified. The agricultural correction remains Unity-unverified pending an
environment-level Unity/Bee repair or owner-approved engine comparison.

## Pass XIX update — 2026-10-29

A complete authored-project checkpoint is stored outside the project at
`F:\Land & Ledgers_Pass19_Backups\20261029_authored_project_checkpoint`; the
Main Scene remained byte-identical and no scene/prefab/ProjectSettings assets
were edited. Live Main Scene inspection reproduced the blank holdings panel
and the legacy Open Management guidance. One fresh New Game route showed no
player-held Business; this narrows the ghost-store defect but does not clear
alternate starts or duplicate representations.

The legacy guidance is now disabled by the Main Scene bootstrap, and the
scenario controller has a startup/late-activation objective-panel path. The
scenario panel was not visible in the inspected Play Mode view, so LL-PLAY-005
remains open and LL-PLAY-004 is partial. The weekly General Store no-player
null path has a focused passing EditMode regression. Broader suites still have
12 failures total (5 General Store and 7 orchestration); none were relaxed.
The current source compiled in the open graphical Editor, but a fresh final-tree
NUnit run did not complete. Main Scene daily-needs journey locations, horse
feeding, rent collection, and opening-date birthday diagnostics are also open
runtime findings; see `Pass19_Runtime_Defect_and_Integration_Results.md`.

| ID | Issue | Current status | Evidence / next check |
|---|---|---|---|
| LL-PLAY-020 | Daily needs do not have journey locations | PARTIAL | Main Scene daily driver now builds the hub JourneyModel from connected TownGrid roads and reciprocal building/plot frontage. Fresh PlayMode verified real building/home destinations and the daily service no longer skips for an empty model. That day still recorded missed meals and zero purchases, so a legitimate purchase/seller-availability case remains unverified. See `Pass21_MainScene_JourneyNetwork_Continuation.md` and fresh XML under `F:\Land & Ledgers_Pass21_Backups\journey-network-continuation_20261030_011226\reports`. |
| LL-PLAY-021 | Rent settlement and animal-feed supply are not demonstrated in the Main Scene | PARTIAL | Source trace shows the daily driver calls the authoritative tenancy settlement path with Housing, household ledgers, payee directory, obligation authority and IDs; no actual occupied tenancy/payment was demonstrated in the ordinary opening, so settlement and arrears behavior remain unverified. `BoardingRentCollectionTests` passes 8/8 in the fresh full EditMode run. Feed shortfalls are consistent with finite feed inventory; `FeedMarket.BuyFeed` exists, but an authorized procurement path is not connected to the ordinary Main Scene driver. Do not label these as missing authorities or hide legitimate shortages. Positive/negative Main Scene runtime proofs remain open. |
| LL-PLAY-022 | Opening-day birthday events report unchanged ages / repeated same-date dispatch | PARTIAL | The age diagnostic now distinguishes a birthday observed from an age change. A fresh Main Scene run had exposed 64 repeated same-date dispatches per affected Person because due events fired on the final pre-rollover short ticks and were handled against the wrong day index. The driver now derives each due event's date from its absolute timestamp; the instrumented 30-day Main Scene PlayMode test passed with at most one dispatch per Person/date, and `BirthdayChronologyTests` passed 9/9. Full-year Main Scene aging, leap-day save/load and longer-horizon persistence remain unverified. See `Pass22_MainScene_Presentation_and_Playmode.md`. |
| LL-PLAY-027 | Recovered construction project remains blocked by historical weekly diagnostic | PARTIAL | Confirmed self-lock: `IsProjectLikelyStartReady` treated `LastWeeklyBlockedReason` as a current gate after present materials and builder capacity were restored. The production predicate now evaluates current readiness; a new recovery assertion passes in fresh Unity Test Runner output. `ConstructionInputSupportTests` remains 49/94 passing, so the wider class still needs failure-cluster review. See `Pass21_Continuation_RealHouseholdAndCommerce.md`. |
| LL-PLAY-028 | Fresh New Game parcel authority retains a pre-generation/null TownGrid | VERIFIED FIXED | `SimulationDrivers` discovers TownWorld on Awake/rebind, and `FirstLedgerSliceBootstrapper` rebinds after the actual New Game grid is generated. The final Main Scene PlayMode suite asserts `SimulationSystemsHub.Parcels.Grid` is the same instance as the generated TownWorld grid; 11/11 pass. See `Pass21_Continuation_RealHouseholdAndCommerce.md` and `pass21-playable-scenario-final-tree-20261030.xml`. |
| LL-PLAY-029 | Household membership history and legacy runtime projections diverge across lifecycle/save-load | PARTIAL | Population saves now retain authoritative active/ended membership records; old saves still migrate from legacy copies. Lifecycle/save/load project registry state into legacy fields still consumed by older runtime paths. Transfer, death, deep-copy, repeated restore, legacy fallback and exhausted ID cursor are covered by `HouseholdMembershipAuthorityTests` 10/10. Other population-generation/newcomer mutation paths that still write legacy fields require a complete authority migration audit. See `Pass21_Continuation_RealHouseholdAndCommerce.md` and `pass21-membership-authority-final-20261030.xml`. |
| LL-PLAY-023 | Main Scene household shopping loop is not bound to authoritative membership/supply | PARTIAL | Production daily drive binds `PopulationManager.Memberships`, rebinds after restore, and bases guidance on stocked/open/reachable item suppliers. A follow-up fix makes the already-registered General Store port resolve authored offers and live stock/staffing after legitimate formation; fresh PlayMode regression passes. The ordinary Main Scene still has no observed positive autonomous purchase in the fresh opening, so commerce/settlement remains unproven. See `Pass21_MainScene_JourneyNetwork_Continuation.md` and fresh reports under `F:\Land & Ledgers_Pass21_Backups\journey-network-continuation_20261030_011226\reports`. |
| LL-PLAY-025 | ScenarioDirector used a parallel household lifecycle and membership authority | VERIFIED FIXED | `FirstLedgerSliceBootstrapper.ConfigureScenarioAuthorities()` now binds the director to the exact `PopulationManager.Lifecycle` instance; it no longer creates parallel membership/kinship registries. A fresh real Main Scene PlayMode regression asserts reference identity; `PlayableScenarioStartupPlayModeTests` passed 11/11. See `Pass21_MainScene_JourneyNetwork_Continuation.md` and `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-population-lifecycle-main-scene-playmode.xml`. |
| LL-PLAY-026 | First Ledger start does not resolve its declared player-household founder | VERIFIED FIXED | When the authored founder-name list is empty, scenario startup now resolves a real generated founder through the injected production resolver. `HouseholdLifecycle` adopts the founder’s existing household and refuses conflicting membership merges. Fresh Main Scene PlayMode asserts that scenario activation creates no duplicate household, assigns exactly one First Ledger player household, and retains actual residents. See `Pass21_Continuation_RealHouseholdAndCommerce.md` and `pass21-main-scene-focused-post-funding-20261030.xml` (11/11 passed). |
| LL-PLAY-027 | Generated workplace assignments are not represented as formal employment relationships | OPEN | Fresh Main Scene 30-day PlayMode snapshots showed 50 Persons with `workplaceBuildingId >= 0` while the hub's authoritative `EmploymentRelationshipRegistry` had 0 records; the task archive ended with only 1 complete task. `PopulationGenerator.AssignProfessionsAndWorkplaces` assigns profession, workplace and wage mirrors from generic building candidates without a formal employer Business identity. Do not convert these mirrors into payroll until the real employer/Business mapping and wage authority are established. See `Pass22_MainScene_Presentation_and_Playmode.md`. |
| LL-PLAY-028 | Main Scene task queue accumulates with little completed work over 30 days | OPEN | The fresh Main Scene run had 55 persisted Tasks after 30 days (1 complete, 54 open). This is direct runtime evidence of a low completion ratio, but the test has not yet classified task definitions, assignees, blockers or required labor, so root cause is still open. Extend the snapshot by task definition/status/assignee and trace the production caller before changing task rules. See `Pass22_MainScene_Presentation_and_Playmode.md`. |
| LL-PLAY-024 | First-session scenario entry allows the clock to run before Start and uses a mismatched authored/runtime start date | PARTIAL | `ScenarioStartPanelController` now pauses `TimeManager` while the fresh scenario-choice panel is open and resumes only after a successful scenario switch. Fresh Main Scene PlayMode regression verifies the clock remains fixed during the choice and resumes on First Ledger start. Live GUI showed `FirstLedger.asset` authored for 1870 while the Main Scene clock initialized to 1886; the start-date/world-bootstrap authority mismatch remains open. See `Pass21_MainScene_JourneyNetwork_Continuation.md`. |

## Pass XX update — 2026-10-29

- Fresh Unity Editor Test Runner baseline on the pre-leap-fix source: EditMode 4,224 total / 4,010 passed / 214 failed / 0 skipped. PlayMode 10 total / 9 passed / 1 failed / 0 skipped. Artifacts: `F:\Land & Ledgers_Pass20_Backups\pass20-reports\pass20-editmode-20261029.xml` and `pass20-playmode-20261029.xml`.
- The PlayMode failure is `ProductionWeeklyPassRegistersFarmFieldAndRespectsWinter`: the ordinary authored opening world has no CropFarm Business. No synthetic farm was introduced. Winter field registration therefore remains unverified in Main Scene.
- Actual Main Scene Canvas binding was repaired and the First Ledger objective HUD was visibly demonstrated after scenario start. LL-PLAY-005 is partial pending broader scenario/save/reload cases and scene-authored panel elements.
- An identityless serialized General Store placeholder was rejected before ownership registration. Fresh First Ledger showed no player Business; broader scenario/continue cases remain open.
- Demographic birth-date generation was changed to distribute birthdays through the calendar year while preserving planned age. Leap-year validation was added; the final current-tree regression passed, as recorded in the Pass XX report.
- LL-PLAY-020 journey destinations, LL-PLAY-021 rent/animal authorities, Person lifecycle, farming Tasks/freight, and the RUN-004 30-day economic reconciliation remain OPEN. No claim of full simulation or playtest readiness is made.
Final Pass XX regression update: the current-source full EditMode run completed 4,225 tests (4,011 passed, 214 failed, 0 skipped). The identityless General Store, January birthday-distribution and leap-year birthday regressions passed. Fresh XML and failure CSV are stored under `F:\Land & Ledgers_Pass20_Backups\pass20-reports`. The full suite remains red; failures were not suppressed. A final-source PlayMode rerun completed 9/10; `ProductionWeeklyPassRegistersFarmFieldAndRespectsWinter` failed because the authored opening world has no CropFarm Business.

## Pass XXI update — 2026-10-30

- Pass XXI source work is on the existing integration branch from `bc29a1ebafe2bb5eff6bbbf1f157639a1be6ae2d`. Commits `7870baab`, `e47ac52e`, and `6fbda31c` are local and unpublished; the final source and records are backed by `F:\Land & Ledgers_Pass21_Backups\pass21-source-final-20261030-r2.bundle`. The Main Scene, prefabs, ProjectSettings and GUIDs were not edited in this pass.
- `LL-PLAY-001` remains PARTIAL: the existing fresh-start correction is preserved; Pass XXI additionally prevents a generic shop selector from consuming agricultural plots and makes rollback restore the mirrored sawmill construction stock. The full scenario/Continue/duplicate-list matrix has not been completed.
- `LL-PLAY-005` remains PARTIAL: the active First Ledger HUD is visible in the Main Scene screenshot under `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-main-scene-first-ledger-hud-fit-20261030.png`. Its eight objectives include the actual equity requirement. This pass only adjusted runtime panel geometry; objective rows remain runtime-generated, and Sandbox, failure, victory, reload and save/load visual cases remain unverified.
- Person death day is now carried through the population save DTO, with the legacy default remaining `-1`; a regression covers current and older DTO data. This does not establish complete birth/death events, visual actor identity or the day/night lifecycle.
- Current GUI Test Runner observation: EditMode 4,226 total / 4,027 passed / 199 failed / 0 skipped; PlayMode 10 total / 10 passed / 0 failed / 0 skipped. The current PlayMode XML is `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-playmode-current-final-20261030.xml`. The current full EditMode result was observed in the GUI, but its export operation did not produce a fresh XML; `pass21-editmode-full-current-20261030.xml` is an earlier run (4,030/196) and must not be represented as the current-tree artifact.
- LL-PLAY-020 (daily-needs destinations), LL-PLAY-021 (rent and animal feeding authorities), crop-task lifecycle/freight, and RUN-004 30-day economic reconciliation remain OPEN. The successful 10-test PlayMode suite does not cover or prove these missing Main Scene integrations.

### Pass XXI final-tree verification — 2026-10-30

- Final Unity 6000.4.2f1 batch results supersede the preliminary GUI counts: full EditMode 4,226 total / 4,031 passed / 195 failed / 0 skipped; full available PlayMode 10 total / 9 passed / 1 failed / 0 skipped. The full EditMode XML and final PlayMode XML are preserved under `F:\Land & Ledgers_Pass21_Backups\pass21-reports`; exact hashes and the repeat test are in `Docs/Pass21_TestRunner_Results.md`.
- The added Sawmill site-role regression passes 1/1 and is also passing in the full EditMode XML. A Sawmill now requires its typed `SawmillYard` site even if the generic building shell is broadly suitable. This fixes site eligibility; it does not verify complete Sawmill operations.
- The one final PlayMode failure, `GeneralStoreDailySaleKeepsRealHouseholdAndPersonProvenance`, reproduced in an isolated repeat. Its one-day fixture does not prove an affordable unmet household need or that the player store is selected over competing sellers. Keep the result open; do not manufacture demand or count a UI-only sale.
- `LL-PLAY-001` remains PARTIAL; `LL-PLAY-005` remains PARTIAL. `LL-PLAY-020` and `LL-PLAY-021` remain OPEN. No 30-day economic reconciliation, day-15 continuation, full person lifecycle, rent/animal authority proof, seasonal crop Task chain or farm freight proof was completed. No visual issue is closed solely from source/test evidence.

Pass XX final-source PlayMode result: 10 tests, 9 passed, 1 failed, 0 skipped. XML: `F:\Land & Ledgers_Pass20_Backups\pass20-reports\pass20-playmode-final-20261029.xml`. The failure is the authored opening world missing a real CropFarm Business; no synthetic farm was added. The first run's PlayMode result is retained separately.

## Pass XXI continuation — real journey network and shopping regression — 2026-10-30

- `LL-PLAY-020` moved from OPEN to PARTIAL. A connected road/building graph is now generated in the actual Main Scene daily drive from TownWorld road cells and each building's valid anchor or matching validated plot frontage. The fresh scenario-startup PlayMode class passed 11/11; its new Main Scene case observed connected building routes and verified registered household home IDs. A one-day run still had missed meals and zero purchases, so embodied commerce is not claimed complete.
- The focused journey, P6 travel/comms, embodied purchase, shopping loop, and Main Scene startup suites passed 10/10, 10/10, 6/6, 21/21, and 11/11 respectively. Exact hashes and XML paths are in `Pass21_MainScene_JourneyNetwork_Continuation.md`.
- The household shopping test formerly named `ExecuteNeed_UnaffordableFreight_CandidateRejected` expected total rejection although the production policy explicitly allows an affordable carry-limit partial purchase. Its expectation now verifies the real 50-unit purchase, remaining 10-unit need, 6,000-cent ledger movement, and stock/inventory conservation. No production policy or goods were injected.

### Pass XXI continuation update — 2026-10-30

- The pre-registered General Store supplier adapter now follows a later legitimate player formation and staffing event. It advertises only authored offers before formation, exposes no stock and is not staffed until a real Business and clerk exist. `ProductionBusinessAuthorityFormsTheScenarioThreeBusinessRoute` passes with the pre-formation adapter retained through formation; this verifies the port lifecycle, not an autonomous Main Scene sale. `LL-PLAY-023` remains PARTIAL.
- `LL-PLAY-021` is PARTIAL, not “missing authorities”: the Main Scene daily driver invokes the existing tenancy settlement authority chain, but no ordinary-opening occupied tenancy/payment was runtime-demonstrated. Animal feed is finite and shortfalls can be genuine; an authorized Main Scene feed procurement path remains unconnected and positive/negative runtime behavior is unverified.
- Fresh full available PlayMode: 11/11 passed, 0 failed, 0 skipped. A broad EditMode name filter `GeneralStore` selected 60 tests and completed 41 passed / 19 failed / 0 skipped; the failures span pre-existing reserve, wholesale, reorder, construction-support, settlement-coverage and HUD expectations. Focused `HouseholdShoppingLoopTests` 22/22 and `JourneyModelTests` 10/10 passed. Evidence and hashes are recorded in `Pass21_MainScene_JourneyNetwork_Continuation.md`.
- Current source checkpoint: `49cc0f1b2d5a300ac2ba1371d257461fc2821ec3`. No Main Scene or other Unity authored assets changed. RUN-004, positive autonomous shopping in the ordinary opening, rent settlement, feed procurement, seasonal farm production, Person night-cycle persistence and visual inspection remain incomplete.

### Pass XXI continuation — household adoption and funded production paths — 2026-10-30

- `LL-PLAY-026` is VERIFIED FIXED on the current source: First Ledger scenario activation resolves and adopts a real generated household through the existing population lifecycle authority. It does not create a duplicate household or rewrite cross-household membership. The fresh Main Scene PlayMode startup suite passes 11/11.
- Feed and farm-construction purchases now reject invalid prices and unaffordable orders before finite supplier stock changes. Funded tests record owner contribution and purchase outflow; unfunded tests verify that no goods/materials move. `FarmIntegrationTests` passes 13/13; `HouseholdLifecycleTests` passes 10/10.
- The positive General Store sale-provenance PlayMode fixture now funds the formed store via the player portfolio, assigns premises/staff through production authorities, runs weekly procurement, checks actual staple stock, and then executes the sale check. This is production-path evidence within a PlayMode fixture, not proof of autonomous Main Scene retail. The suite passes 11/11.
- Fresh full EditMode on the same tree: 4,236 total, 4,044 passed, 192 failed, 0 skipped. The full XML is `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-editmode-full-latest-20261030.xml` (SHA-256 `7118FC459AF3FFB3FF088B119B171855E3EA70E6F470F4D13931ADA256B9394E`). Known General Store assertions remain unresolved; see `Pass21_Continuation_RealHouseholdAndCommerce.md`. The Main Scene, prefabs, terrain, and ProjectSettings remain unchanged.

### Pass XXI continuation — scenario HUD and duplicate store projection — 2026-10-30

- The existing Main Scene now contains serialized scenario objective UI components. The controller reads the authoritative scenario state and presentation builder for live goals, status, completion and registered deadlines. The panel is collapsible. Manual GUI play showed the First Ledger start card, an active objective panel after selection, and working collapse/expand. Fresh `PlayableScenarioStartupPlayModeTests` passed 11/11 (0 failed, 0 skipped). Visual Sandbox, deadline failure, Victory, reload and save/load cases remain unverified.
- `GeneralStorePanelController` no longer counts or lists an unformed General Store merely because its coordinator component exists. It projects only a real, identified Player-owned General Store `BusinessInstanceState`, and prevents the same instance appearing through both store and shared-world projections. Three projection regressions pass (empty coordinator, identityless shell, one real store). The final 34-case class is 29/34; the five reserve, price-floor, cash-triage and reorder-buffer assertions remain unresolved. No economic policy was changed.
- Main Scene and `.meta` are backed up at `F:\Land & Ledgers_Pass21_Backups\20261030_scenario_hud_pre_authoring` and `F:\Land & Ledgers_Pass21_Backups\20261030_scenario_hud_authored`; the current saved scene hash matches the post-authoring copy and the `.meta` GUID is unchanged. Fresh XML, logs and hashes are recorded in `Pass21_Continuation_RealHouseholdAndCommerce.md`.
- `LL-PLAY-001` and `LL-PLAY-005` remain PARTIAL. Full New Game/Continue ownership matrix, full scene manager census, positive ordinary autonomous purchases, day/night Person identity, rent and animal supply, seasonal crop Tasks/freight and RUN-004 30-day reconciliation remain unproven.

### Pass XXI continuation — persistent membership and New Game parcel binding — 2026-10-30

- `LL-PLAY-028` is VERIFIED FIXED: after actual New Game town generation, the bootstrap rebinds the existing SimulationDrivers authority to the generated TownGrid. The current full available PlayMode suite passes 11/11, including a reference-equality assertion between TownWorld.Grid and SystemsHub.Parcels.Grid.
- `LL-PLAY-029` is PARTIAL: lifecycle/save/load now persists authoritative active and ended household membership records and projects active membership to compatibility fields still consumed by legacy systems. Fresh `HouseholdMembershipAuthorityTests` passes 10/10 and `NewcomerSettlementSaveLoadTests` 8/8. Several population/newcomer creation paths still mutate legacy membership fields directly and require a complete authority migration.
- Fresh final-tree full EditMode: 4,244 total / 4,052 passed / 192 failed / 0 skipped; XML `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-editmode-full-final-tree-20261030.xml`, SHA-256 `72F099FFD6C57732B6C17A8692C981277CFB1E353E5D7A5B985DCE91AB55D80E`. The 192 current failures are distributed across the documented construction, world save/load, acquisition, ownership UI, population, business transfer and other suites; no tests were removed.
- Fresh full available PlayMode: 11/11 passed / 0 failed / 0 skipped; XML `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-playmode-full-final-tree-20261030.xml`, SHA-256 `BB99B48C6F139FD41254BA7451EC5E94A6E3CCCEB20CD35C7B825C63E0CD9AB7`. The Main Scene scenario/HUD/fresh-grid checks pass; this does not establish RUN-004 or long-term financial/goods reconciliation.
- HUD screenshot inspected: `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-main-scene-first-ledger-hud-fit-20261030.png`. The scenario name, 0/8 progress, genuine goals and $15,000 equity goal are visible. Text is dense at the captured game-view scale; Sandbox/failure/Victory/save-load visuals remain unverified. Scene hashes remain `817F22A74FACDC20A2F0E2B36451AA1293571E11041484078823B25E952261B9` (scene) and `841B50C2925B4A0B459AF03D63E63F992FAFD72C547618DDCE9322A5E43BACE5` (`.meta`).
