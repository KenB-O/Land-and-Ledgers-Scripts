# Pass XIX — Runtime Defect and Integration Results

Date: 2026-10-29
Starting commit: `76344869838c2c31ae672f13f9fa3ef738ea4bb6`

## Verified source corrections

### General Store weekly callback without a player store

`GeneralStoreRuntimeManager.ResolveWeeklyPayrollAndReorder()` could continue into the legacy settlement path after initialization even when no player General Store was formed and its runtime state was absent. The guard now returns with a clear no-player-store status before settlement. Regression `WeeklySettlementIsSafeBeforePlayerStoreFormation` passed as a focused EditMode run (fresh result: one test passed).

A 30-test General Store fixture run then reported 25 passed and 5 failed. Failures were reserve buffer expectation (44,000 vs at least 48,000), two wholesale-price expectations (106 vs at least 108), low-cash essential triage (0 rather than positive), and reorder budget (0 vs 5,000). These were not weakened. Their relationship to the null-state fix has not been established; investigate before calling the suite green.

### Legacy guidance versus scenario authority

The live Main Scene showed the obsolete generic `Open Management` strip, and the old step sequence can progress to “Hire your first store worker” even when no player store exists. `FirstLedgerSliceBootstrapper` now disables this legacy strip after configuration. `ScenarioGuidanceGate` also suppresses first-session guidance whenever an authoritative ScenarioDirector exists; legacy fail-open behavior remains only for scenes without that authority. A gate regression test covers Sandbox, First Ledger, other scenarios, and legacy no-authority behavior.

### Active scenario panel startup

`ScenarioStartPanelController` now builds its objective panel when an active scenario is already present at Start, and when activation occurs later. It renders the scenario title, failed/in-progress state, required goal completion count, and non-optional goals from `ScenarioRuntimeState`. The live Game view did not show a scenario panel after this change. Therefore this source repair is compiled but its Unity wiring/visibility is unverified and remains partial; deadlines, detailed progress read models, victory/failure presentation, and responsive visual review are not complete.

## Unity validation evidence

The live Unity Editor refreshed and compiled the current scripts at 19:08:58 local time; no new C# compiler error was observed in the completion log. Existing warnings include deprecated `FindObjectsByType(...FindObjectsSortMode...)` calls in the bootstrapper.

Fresh Unity Test Runner artifacts in `F:\Land & Ledgers_Pass19_Backups\pass19-reports`:

- `pass19-generalstore-null-guard-editmode.xml`: the focused no-store weekly settlement regression passed (1/1).
- `pass19-generalstore-suite.xml`: 25/30 passed; 5 failures listed above.
- `pass19-orchestration-suite.xml`: 145/152 passed; 7 failures: insufficient-funds hardship recovery; missed-meal unaffordability read; procurement chain ending at 59 vs expected 60; travel hub condition wiring; postal journey arrival (Posted rather than Arrived); and two Pass9 scenario start-date assertions.
- `pass19-editor-orchestration-suite.xml`: 62/62 passed at 19:02 UTC, before the final adjustment that suppresses First Ledger guidance under an authoritative director. It is not a final-tree result.

The graphical Test Runner discovered 4,222 tests. A post-change NUnit run of the final source was not completed. The `unity` CLI could not address the open Editor because the project has no Unity Pipeline connection; the graceful `unity close` attempt did not close it, and no force termination was used.

## Asset and hierarchy safety

No authored Unity assets were saved or changed. Main Scene hash matches the protected baseline. The project was left out of Play Mode. No persistent manager hierarchy correction, scene component authoring, or prefab change was made.

## Remaining high-priority work

1. Rerun focused gate and General Store tests against the final source.
2. Wire and visually verify the authoritative scenario objective HUD in Main Scene.
3. Reconcile the 12 fixture failures without weakening assertions.
4. Trace the Main Scene daily needs, animal feeding, rent authority, and opening-day birthday behavior.
5. Reproduce Person disappearance with stable IDs and runtime/visual counts.
6. Establish genuine Person/Household persistence, real movement/Task evidence, and an actual interrupted save/load run.
7. Complete farm Task lifecycle and a genuine 30-day Main Scene trace.
8. Investigate actual sources of resource overlays, procedural trees, and farm props before changing them.

This report documents a partial repair pass; it does not assert gameplay readiness.
