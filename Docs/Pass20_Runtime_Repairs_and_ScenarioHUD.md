# Pass XX — Runtime Repairs and Scenario HUD

Date: 2026-10-29  
Unity: 6000.4.2f1  
Starting source: `integration/pass12-unity-readiness` at `407fccc6c41a348d2794b026949099f92cd54812`

## Work completed

### Identityless General Store placeholder

A serialized `BusinessInstanceState` with no `InstanceId` could still carry the legacy `Player` owner kind. The General Store manager previously exposed and handed that shell into the shared registry. `GeneralStoreRuntimeManager` now clears identityless startup placeholders and exposes `CurrentBusiness` only for a stable ID. `SharedBusinessRuntimeManager` independently refuses an identityless General Store argument. This is defensive validation at both the runtime shell and registry boundary; it does not create a store or grant ownership.

Regression: `FreshStartupDiscardsSerializedPlayerBusinessPlaceholderWithoutIdentity` passed in the first fresh EditMode run. A fresh First Ledger Main Scene run displayed $2,500 and no player-owned Business. Alternate scenario, Continue and duplicate projection paths remain unverified.

### Scenario HUD binding

The Main Scene's `ScenarioStartPanelController` had no serialized Canvas even though the authored `UI Canvas` exists. The scene now references that Canvas. Controller startup resolves the active overlay Canvas and reports a missing scenario authority/Canvas instead of silently creating a second `ScenarioDirector`. The Main Scene Play Mode run visibly showed the First Ledger objective panel after starting the scenario, with 0/8 goals complete and the authentic $15,000 owner-equity goal. The panel is collapsible and aligned at the top right.

The panel's content hierarchy is still created dynamically by the controller; the scene edit binds the existing authored Canvas but does not yet serialize each text element/objective row. Victory, failed-scenario, Sandbox, reload and save/load visual cases have not been captured. No screenshot file was saved; the UI was inspected live in the Unity Editor.

Main Scene asset hash after this serialized reference: `D70A01219AF7912F302008C2D74532F02EDD7406D4B611A2AC469792CC5C225D`. Original asset hash: `62391374B89BD3FC8C2FB6E2016198D9B446F6A8279C924BC87355A5954CE525`, preserved in `F:\Land & Ledgers_Pass20_Backups\checkpoint_20261029_192945`.

### Opening-day birthdays

The demographic planner had constrained generated/migrated birthdays to dates on or before the opening date. At a January 1 opening this concentrated the generated cohort on January 1. `DrawBirthDate` now selects throughout the year, shifts the birth year as needed to preserve the planned completed age, and clamps dates to valid Gregorian month lengths when leap-year differences require it. Regression coverage checks January-first distribution and age preservation, including missing-date migration, plus leap-year validity and age preservation.

The current-source full EditMode run passed the January-first regression and the added leap-year case. The actual opening-date lifecycle over a complete fresh population remains unverified. The live opening-date diagnostics that prompted the fix were observed previously; a complete fresh visual population lifecycle run remains outstanding.

## Test evidence

Fresh Unity Editor Test Runner, EditMode, first full run on the tree before the leap-year follow-up: 4,224 total, 4,010 passed, 214 failed, 0 skipped. Artifacts:

- `F:\Land & Ledgers_Pass20_Backups\pass20-reports\pass20-editmode-20261029.xml`
- `F:\Land & Ledgers_Pass20_Backups\pass20-reports\pass20-editmode-failures.csv`

The ghost-ownership and January-first birthday regressions passed. The run is not a green baseline; failures span multiple suites and require triage. The final current-tree full EditMode run completed with 4,011 passing and 214 failing tests (4,225 total, zero skipped); the final-source PlayMode rerun completed 9/10, with the same missing-authored-CropFarm failure.

Fresh Unity Editor Test Runner PlayMode: 10 total, 9 passed, 1 failed, 0 skipped. Artifact:

- `F:\Land & Ledgers_Pass20_Backups\pass20-reports\pass20-playmode-20261029.xml`

`ProductionWeeklyPassRegistersFarmFieldAndRespectsWinter` failed because the authored opening world contains no CropFarm Business. No test-only farm, field, harvest or shipment was injected. The other nine tests passed, including fresh scenario startup, honest four-week First Ledger progress, Business identity save round-trip and owned horse/wagon authority checks.

## Reproduced historical failures

The full EditMode run reproduced the known 5 General Store and 7 orchestration failures recorded in Pass XIX:

- General Store: reserve buffer 44,000 vs expected at least 48,000; wholesale price 106 vs expected at least 108 (two tests); low-cash essential triage returned 0; reorder budget returned 0 vs expected 5,000.
- Orchestration: insufficient-funds hardship recovery remained false; missed-meal unaffordability was not reported; procurement ended at 59 vs 60; travel-hub condition provider was null; postal journey remained Posted; two Pass9 start-date assertions failed.

These results are reported as observed test failures. Each still requires source-versus-contract analysis before changing production behavior or expectations. The full run also reported failures outside those twelve; see XML/CSV for exact cases.

## Active defects and scope limits

- Main Scene authored start date is inconsistent with First Ledger asset's 1870 start: live clock showed January 1886 / Week 305 while the intro content names 1870. Time initialization needs a source-level trace before correction.
- Daily needs have no registered journey locations in Main Scene; rent authorities are missing; animal feeding records shortfalls. These were not repaired here.
- The ordinary January opening has no CropFarm Business, so winter field registration could not be established in the Main Scene. The corresponding PlayMode assertion failed. Crop Tasks, harvest and freight remain unverified.
- No 30-day reconciled Main Scene simulation, day-15 continuation, stable Person ID/day-night study, or full runtime-manager census was completed.
- The Main Scene still shows broad orange road/parcel treatments and unfinished environmental presentation. No terrain, tree, deposit, animal prefab or farm-prop changes were made.
- The supplied v2.1 documentation ZIP was not found in the checked local project/user folders. Canon XXXI and Technical XIV were not independently inspected during this pass; no Canon rule was changed.

## Preservation

The project/scene checkpoint is `F:\Land & Ledgers_Pass20_Backups\checkpoint_20261029_192945`. Diagnostic NUnit results are under `F:\Land & Ledgers_Pass20_Backups\pass20-reports`. The UI prefab was not changed. Source stays on the existing integration branch; no history rewrite or main-branch update was performed.