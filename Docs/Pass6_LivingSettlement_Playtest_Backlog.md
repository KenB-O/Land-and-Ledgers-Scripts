# Pass VI — Living Settlement / Playtest Backlog

## Pass XVIII environment note (2026-10-29)

Unity 6000.4.2f1 script compilation remains unverified after repeated Bee/ILPP stalls, including in disposable trivial-script projects. A disposable Unity 6000.2.8f1 project compiled and passed one fresh EditMode test, while its PlayMode invocation discovered zero tests. No LL-PLAY item is marked fixed by this environment investigation. See the Pass XVIII reports for evidence and the required next gates.

Status recorded from the Pass VI source audit. Visual statuses remain unclaimed
until the Unity Editor can be inspected graphically.

| ID | Issue | Current status | Evidence / next check |
|---|---|---|---|
| LL-PLAY-001 | Incorrect starting ownership / ghost Businesses | PARTIAL | Root cause found: serialized identityless BusinessInstanceState carried Player owner and entered the shared registry. Startup now rejects identityless placeholders; fresh First Ledger Play Mode showed zero player holdings and an EditMode regression passed. Alternate scenario/Continue matrix and duplicate projection still need checks. |
| LL-PLAY-002 | Excessive PlayMode-generated scene roots | Not tested | Requires Edit Mode vs Play Mode hierarchy inspection. |
| LL-PLAY-003 | Duplicate Civic Foundation | Not tested | Requires Main Scene runtime inspection and authority-count trace. |
| LL-PLAY-004 | Obsolete tutorial guidance | PARTIAL | Fresh First Ledger after Start showed no universal “hire first store worker” strip and authentic scenario objectives instead. Legacy guidance was visible in a separate stale/test-runner view; confirm all routes and restored saves. |
| LL-PLAY-005 | Missing scenario HUD | PARTIAL | Main Scene now serializes the authored UI Canvas into ScenarioStartPanelController; live First Ledger showed the 0/8 objectives and authentic $15,000 equity goal in a readable collapsible panel. Runtime panel contents are still generated dynamically; Sandbox, failed/victory states, reload and save/load visual cases remain unverified. |
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
| LL-PLAY-016 | Flat terrain | Not tested | Visual/world-generation assessment deferred until Editor access. |
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
| LL-PLAY-020 | Daily needs do not have journey locations | OPEN | Main Scene log: `DailyNeedsService skipped: no journey locations registered`; connect valid location authority and test actual needs. |
| LL-PLAY-021 | Animal feeding and rent authorities are missing in runtime | OPEN | Live log reports horse feeding shortfall and rent collection skipped for missing authorities; trace actual scene registration and custody. |
| LL-PLAY-022 | Opening-day birthday events report unchanged ages | PARTIAL | Root cause: generated/migrated birthdays were constrained to dates before the opening day, concentrating Jan-1 birthdays. Generation now distributes dates across the year while preserving completed age; opening-day and leap-year regressions added. Final leap-year test result pending the current Unity run. |

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
