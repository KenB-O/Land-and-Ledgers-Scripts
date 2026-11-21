# Codex to Muse Handoff — 2026-11-20

## Published checkpoint

- Repository: `KenB-O/Land-and-Ledgers-Scripts`
- Handoff branch: `codex/muse-handoff-2026-11-20`
- Implementation checkpoint: `49e9c82fd5b6b4c01be5a482ab6e8c53e83a5a9c`
- Branch URL: <https://github.com/KenB-O/Land-and-Ledgers-Scripts/tree/codex/muse-handoff-2026-11-20>
- Development baseline: `codex/player-economic-participation-2026-11-18`, local HEAD `8537f52fb7dea263fa3ba62da6cbd4d6b5aefbf8`, based on remote development SHA `7180a2f94ea1949146b502f274b8503107c7d404` plus four local commits. Those four commit identities and histories are ancestors of the published implementation checkpoint. No main merge or force push was performed.

This is a development handoff, not a claim that the gameplay work is complete. Published source changes include completed, partial, and unverified work. Use the current branch head as the authoritative checkpoint after cloning.

## Included project state

The handoff branch contains the four unpublished development commits and the current publishable working-tree changes: gameplay source, editor diagnostics and Playtest utilities, regression sources, project configuration, relevant documentation and reports, and the modified authored scene/prefab/configuration/font/terrain metadata selected for the current project state. Required Unity `.meta` files were included for selected assets. The authored Main Scene dependency manifest contains 305 project paths; validation found no missing `.meta` entry among those manifest paths. No files were deleted in the checkpoint.

The source checkpoint includes work touching, among other areas, tasks and liveness, household schedules/meals/shopping, movement/pathing, bakery and store supply, freight and fuel dealer operations, sawmill/yard transfers, furniture purchase/delivery state, Save/Continue, camera/UI, and Playtest diagnostics. Consult the actual diffs and the persistent checklist before extending these systems; do not infer that an area is verified from its presence in source.

## Diagnostics and investigation entry points

- Persistent warning register: `Reports/Diagnostics/CONSOLE_INVESTIGATION_CHECKLIST_2026-11-19.md`.
- Local evidence/exclusion inventory: `Reports/Diagnostics/MUSE_LOCAL_EVIDENCE_INVENTORY_2026-11-20.csv`.
- Authored Main Scene dependency manifest: `Reports/Diagnostics/MUSE_ASSET_DEPENDENCY_MANIFEST_2026-11-20.csv`.
- Playtest Editor window: `Assets/Scripts/Editor/Playtests/Playtest60DayRunnerWindow.cs`; visible window/menu label is Playtest, under `Land & Ledgers/Diagnostics/Playtest`.
- NPC Activity Monitor: `Assets/Scripts/Editor/Diagnostics/NpcActivityMonitorWindow.cs`, menu `Land & Ledgers/Diagnostics/NPC Activity Monitor`.
- Runtime activity trace recorder: `Assets/Scripts/Core/Diagnostics/NpcActivityTraceRecorder.cs`.
- Playtest protocol/schema/index: `Reports/Playtests/PLAYTEST_60_DAY_PROTOCOL.md`, `Reports/Playtests/PLAYTEST_DIAGNOSTIC_SCHEMA.md`, `Reports/Playtests/PLAYTEST_RUN_INDEX.md`.
- Full evidence for the latest completed five-day run is in `Reports/Playtests/Runs/20261120T205228Z-13c30187/`; the latest retained 60-day run is in `Reports/Playtests/Runs/20261120T192311Z-343a8cd2/`.

## Current verification state and outstanding work

The checklist is authoritative for signature-by-signature history. Its original baseline has 354 signatures and 645 warning occurrences; the recorded status tally is 287 OPEN, 58 IMPLEMENTED/UNVERIFIED, 1 INVESTIGATING, 2 BLOCKED, and 6 EXPLAINED/LEGITIMATE. Run-local occurrences are append-only and must not overwrite those historical counts.

The latest completed Main Scene run before the publication-only work is `20261120T205228Z-13c30187`: Unity 6000.4.2f1, development-town Main Scene, New Game, seed 1886, 10x, target/completed 5 days, source SHA `8537f52fb7dea263fa3ba62da6cbd4d6b5aefbf8` with a dirty working tree. It recorded 328 Console events (291 logs, 37 warnings, zero errors/exceptions). It is `CALENDAR_COMPLETE_REQUIRES_REVIEW`, not an economic pass. The startup gate reported 14 residents, 98 person records, 8 households, and 7 businesses. It recorded 42 meals eaten and zero missed per captured day, but seven shopping trips were only planned at the ending boundary; the run did not prove physical shopping, counter service, payment, return, pantry receipt, or later food consumption. Do not call household food security solved. A separate retained 60-day 250x run completed its calendar with 6,317 warnings and zero errors; economic health was not evaluated. The higher warning count is not a verified improvement over the historical 30-day baseline.

Important open or unverified items:

- Furniture delivery is no longer intended to be a synchronous purchase-to-install shortcut in current source. Paid orders are recorded before shipment setup, existing freight shipment state is adopted by the saved carrier driver, and delivered pieces remain in household custody. However, order/task lifecycle regressions are pending execution, and an installation outcome resolver still does not link a completed `furnishing.install` TaskAuthority task to the order. No live completed installation is established. The 60-day evidence recorded 456 placement refusals where no matching authored placement was free; goods remained in household custody.
- Household shopping and food receipt remain unverified end-to-end. The five-day run planned seven trips but did not demonstrate departures or purchases. Food source availability, seller queue/service, physical carriage, home receipt, and a later meal consuming the purchased item still need a short integrated Main Scene proof.
- Firewood handling is blocked by a missing authored Firewood activity point in affected houses. Do not alias a porch point or invent placement coordinates; use valid authored household/storage geometry and verify physical reach/use.
- Fuel Dealer customer-service queue and physically present clerk logic have historical focused coverage (`CampaignCCustomerPatiencePersistenceTests`, 15/15), but live retail service is not verified. The latest run had only 4–5 byproduct intake units against a 10-unit bundle threshold, so no full retail bundle was available. Do not invent fuel.
- The current saved lumber-transfer regression is compiled but awaiting execution per the Assignment 04 verification backlog. A historical transfer suite passed 27/27 on an earlier checkpoint; this does not replace execution against the designated current source. Preserve the current pending status and avoid needless changes to that transfer implementation.
- The five-day run recorded a settled 24-loaf bakery wholesale lot for 96c entering the General Store receiving catalog, but shared `staple_food` receiving capacity was exhausted and the lot waited as backstock for a later stocking retry. This is an observed capacity constraint, not proof of household retail availability.
- Several focused suites passed historically, including `CampaignBFuelDealerRuntimeTests` (40/40); those results do not establish that the entire published tree was rebuilt or that all changes in this checkpoint are compiled. The latest task/furniture work and full handoff tree have not been Unity-compiled or PlayMode-verified as part of this publication.
- The latest formal run reported a startup population distinction of 14 residents versus 98 person records. The gate passed but population interpretation/integrity remains a checklist concern; do not equate person-record count with resident count.

The checklist and run records identify additional signatures, exact evidence, affected entities, and next actions. Reconcile every warning against new complete Console evidence; do not suppress meaningful warnings or close entries based only on absence from an unrelated run.

## Unity and Git integrity notes

- Unity version in evidence: `6000.4.2f1`.
- Recent Editor memory pressure led to stopping a prior run at about 434 MB free physical memory. Later memory snapshots were not treated as sufficient grounds for a long run. This publication operation did not start Unity, compile, run tests, or run a Playtest; the Editor and owner-local assets were left untouched.
- The original local Git object database has known damaged loose objects: `02b68f94f209bbfb72aacd92fb216ccdcc56c0a7` (inflate data stream error), `07faef747aa193e0996410a3bc524f8bb6982f68` (bad header), and `bc9c646edf1b1ec757106d66b862282fc959f0af` (empty/missing blob). It was not repaired or altered. Publication was prepared in an isolated clean clone; the four local commit patches were applied and their resulting trees checked against the original commit objects before the exact original commit IDs were restored as ancestors.
- No credentials were copied into the branch. Machine-local Editor logs containing token-bearing content were not published.

## Local-only items intentionally not uploaded

The full per-path inventory and reasons are in `MUSE_LOCAL_EVIDENCE_INVENTORY_2026-11-20.csv`. In summary, excluded but preserved in the owner project are Unity `Library`/Temp-style generated data, a recovery Library tree (85,293 files, about 15.7 GB), root-level build/test logs and XML (502 files, about 5.3 GB), `.codex` and `.dotnet*` machine state, verification `bin/obj` outputs and `TestResults`, unused recovery scenes, and disposable historical raw run captures. The latest complete 60-day and five-day evidence runs and the checklist were included. Unreferenced parts of the local Asset Packs tree (about 20.9 GB beyond the selected Main Scene dependency set) and other unreferenced owner-local assets (about 237.8 MB) remain local; the dependency manifest records the paths selected for this handoff. `Assets/Audits/MasterScale/Snapshots.asset` (about 121 MB) was not uploaded because it exceeds GitHub's ordinary per-file limit and is outside the Main Scene dependency closure. No owner-local content was deleted or overwritten. See the inventory for precise paths, counts, and bytes.

## Instructions for Muse

Clone the dedicated handoff branch, not `main` or the older remote development pointer:

```powershell
git clone --branch codex/muse-handoff-2026-11-20 --single-branch https://github.com/KenB-O/Land-and-Ledgers-Scripts.git
git rev-parse HEAD
```

Confirm the clone's `HEAD` equals the published branch SHA supplied with this report. That SHA is the exact base for an isolated Muse worktree/branch. Read `AGENTS.md`, the governing Canon/Technical documents, this handoff, and the persistent checklist before work. Preserve the published checkpoint and keep Muse changes isolated; do not base the pass on `main`.
