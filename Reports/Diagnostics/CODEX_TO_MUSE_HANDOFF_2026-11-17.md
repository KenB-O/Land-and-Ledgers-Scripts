# Codex to Muse handoff — 2026-11-17

**Handoff status: NOT_READY_FOR_MUSE**

The first narrow startup-recovery checkpoint is published, but the active Unity-loaded development-town configuration still fails validation and prevents a valid PlayMode run. Do not infer successful economy initialization from generated buildings.

## A. Source state

- Branch: `codex/expanded-unity-verification-2026-11-16`
- Published recovery checkpoint: `a81f9264d4bd04b2fdb6cbe51e2a0468af68a034` (`Fail closed on invalid development town startup`). Remote `origin` was queried directly with `git ls-remote`; it returned the same SHA.
- Parent Muse integration: `muse/expanded-integration-2026-11-16` at `cbd8e2ce1b901919f908f54a9e23318bc5189734`, previously published and verified.
- Owner-local modified scene/prefab/font/terrain metadata and large local asset additions remain outside the commit. They were not staged. The Main Scene remains dirty and unsaved.
- This document and the playtest utility/protocol are not yet part of the published checkpoint until their follow-up commit is verified.

## B. Unity status

- Editor: Unity `6000.4.2f1`, project `F:\Land & Ledgers`; original Editor was left running, currently responsive in Edit Mode, not compiling, not in Play Mode. Main Scene remains dirty; it was not saved, reloaded or discarded.
- Latest source compile completed without errors (`failed=false`, `errors=[]`).
- Focused EditMode: configuration validation 2/2 passed; startup generation-gate 1/1 passed. `DevelopmentTownConfigurationTests`: 2 passed / 3 failed because Unity's loaded asset omitted serialized entries present on disk (details below).
- Prior full suite evidence from the expanded integration review: 8071 total, 7906 passed, 165 failed, 0 skipped. The 165 are **unclassified**, not presumed baseline failures. The older Muse Roslyn count is not Unity Test Runner evidence.
- No new PlayMode run or 60-day simulation was performed in this recovery. The owner-provided short-playtest log summary had no attached raw export path. Previous full Console export is at `F:\Land & Ledgers Verification Evidence\Unity-Editor-full-console-expanded-integration-2026-11-16.log` (133,176,037 bytes; SHA-256 `B687565F0C8BBC38124970E1AF23375F63EB0CAB9A4980B7EDA8E07E46D70EC8`).
- The Unity AI Assistant relay/bus connection failure is classified as tooling/transport evidence, not a gameplay defect. It is separate from the confirmed bootstrap failure.

## C. Startup root cause and recovery

Owner-provided short-playtest error:

> `[WorldStartup] Development town startup stopped: Development businesses need unique keys, an operation ID, a configured owner, and non-negative opening cash.`

Unity's live `AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>("Assets/Core/Population/DevelopmentTownConfiguration.asset")` returned 10 people, 6 households and 7 businesses. The on-disk/HEAD YAML contains 15 people, 8 households and 7 businesses. Live people omitted `enzo` and `don-millward`; the corresponding serialized `sawmill` and `carpentry-workshop` business owner keys therefore failed referential validation. Disk YAML was not changed in Git. Loaded crew count also read 1 where serialized asset declares 3.

The exact stale-read/cache cause is **not confirmed**. Non-destructive AssetDatabase refresh/reimport and scene reload/reopen did not resolve it. The current Codex source repair adds per-business field diagnostics and a terminal failed-startup state; it also makes TownWorldController refuse visual shell generation after fatal bootstrap failure. This prevents misleading partial gameplay; it does not repair the loaded asset state. The premises-free freight operator is excluded from the count of physical business buildings instead of weakening configuration validation.

Regression evidence: invalid/valid configuration tests 2/2; shell-generation policy tests 1/1. Existing authored asset read-back tests still fail 3/5 with the stale values above. No owner name, operation, cash or inventory was fabricated. No new residents were added.

## D. 60-day testing infrastructure

- Protocol: `Reports/Playtests/PLAYTEST_60_DAY_PROTOCOL.md`
- Run index: `Reports/Playtests/PLAYTEST_RUN_INDEX.md`
- Diagnostic schema: `Reports/Playtests/PLAYTEST_DIAGNOSTIC_SCHEMA.md`
- Editor utility: `Assets/Scripts/Editor/Playtests/Playtest60DayRunnerWindow.cs`, menu `Land & Ledgers > Diagnostics > 60-Day Playtest Runner`.
- The runner uses the real TimeManager/day event, requires deterministic existing town seed settings, gates startup before counting day 1, writes raw timestamped Unity logs plus JSONL, CSV, entity snapshots, final metadata/report, and marks unavailable data as `NOT_CAPTURED`.
- No 60-day run has been completed. No run ID or console checksum exists. Run artifacts are not invented.

## E. Exact owner-provided short-playtest evidence and open defects

1. **BLOCKER — invalid/stale loaded development configuration.** Reproduce by starting the intended development New Game; observed bootstrap validation error above. Expected: asset read-back yields the serialized configured owners and startup proceeds only after full validation. Evidence: loaded-object readout vs unchanged YAML; failing `DevelopmentTownConfigurationTests` 3 cases. Files: `DevelopmentTownConfiguration.asset` (serialized bytes unchanged), `DevelopmentTownConfiguration.cs`, `FirstLedgerSliceBootstrapper.cs`.
2. **Partial-world presentation.** Owner observed secondary world generation continue and create visual buildings after primary startup failure. Expected: no visual-only shell presented as a running scenario. Recovery guard is published; no fresh live PlayMode reproduction yet.
3. **Pass 9/runtime authority bindings.** Owner observed absent Pass 9 state, logging camp, stand registry, logging operation, sawmill/lumberyard holders, transfer driver, construction price port and wagon registration. These cascade from failed bootstrap or may include independent scene wiring. No post-recovery live run establishes which remain after valid initialization.
4. **Household routine absent in owner observation.** First nine logged dates: 0 households served meals, 0 meals eaten, 0 physical food-shopping trips, 0 food charges. Cannot attribute causally until startup and authority wiring pass.
5. **Business cash/status.** Ten businesses reported zero operating cash and STRUGGLING. This may reflect invalid/partial initialization; no separate legitimate opening-cash audit was completed in runtime.
6. **Shared business support.** `linkedSawmillSupportNode` reported unwired; independent status unknown after the bootstrap is corrected.
7. **Secondary diagnostics.** Two agricultural/livestock building-fit errors, 15 world warnings (access anchors/exterior props), negative-scale BoxCollider warnings. They need individual triage after startup. Do not suppress globally.
8. **Other open defect status.** DEF-009, DEF-067 and DEF-011 remain unverified in PlayMode in this recovery. The earlier 162 Unity EditMode failure set (later suite 165 failures) lacks a reproducible identity comparison and remains unclassified.

## F. Ranked Muse priorities

1. **P0: resolve live configuration asset read-back.** Compare YAML, AssetDatabase object, GUID/meta, importer state and serialized owner references. No destructive Library clearing while the owner project is open/dirty. Demonstrate authored 15/8/7 values in the live Unity object and a successful fail-closed startup. Relevant: `DevelopmentTownConfiguration.asset`, `DevelopmentTownConfiguration.cs`, `FirstLedgerSliceBootstrapper.cs`, existing authored configuration tests.
2. **P1: one valid New Game smoke test.** Confirm exact residents/households/business ownership and opening balances; then inspect `SimulationSystemsHub`, `SimulationDrivers`, `SaveLoadManager`, `Series2Pass9SaveState`, population/pathing and daily drivers. Gate every runtime authority against the actual initialized town.
3. **P1: Pass 9 holders and logistics initialization.** Reproduce logs for opening wagon, logging camp/stand/operation, sawmill/yard and transfer driver after P0; separate missing scene reference from bootstrap cascade. No synthetic business stock or cash.
4. **P1: real household routine.** Once a valid town is active, verify daily meal and shopping drivers, valid household inventories, routes, payments and receipts from authoritative records. The supplied 0/0/0/0 nine-day observation is concrete but came from failed startup context.
5. **P2: diagnose independent authored world errors.** Reproduce the two fit errors, access/exterior-prop warnings and negative-scale colliders; fix only validated placements/authoring causes.
6. **P2: classify 165 Editor test failures.** Capture identities and compare against an exact same-Unity/current-source baseline; do not call them pre-existing without that evidence.
7. After startup passes, execute the protocol's 60-day run and collect DEF-009/067/011 evidence.

## G. Handoff status

`NOT_READY_FOR_MUSE` until a live Unity-loaded configuration passes the gate and the follow-up source/docs commit has a verified remote SHA. The already published recovery checkpoint is available, but source publication alone is not proof of a valid playable economy. Owner-local commercial assets remain only in the authoritative local project and are not available to Muse through GitHub unless separately published under the owner's asset/licensing rules.
