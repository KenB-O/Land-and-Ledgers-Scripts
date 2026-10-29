# Pass XIX — Initial Unity and Git Audit

Date: 2026-10-29

## Source and project protection

- Unity project: `F:\Land & Ledgers`.
- Unity Editor reported in the live window: 6000.4.2f1, build 7a4c1aeef971.
- Nested Scripts branch: `integration/pass12-unity-readiness`.
- Starting local and remote integration HEAD: `76344869838c2c31ae672f13f9fa3ef738ea4bb6`.
- Remote `main`: `7b1ca29cb13e570c04870c75a7a62f5ddc625f1d`.
- No remote advancement was found before edits. No main-branch operation was performed.
- A complete authored-project checkpoint was verified outside the Unity project at `F:\Land & Ledgers_Pass19_Backups\20261029_authored_project_checkpoint`. It includes Assets (including nested Scripts Git metadata), Packages, ProjectSettings, UserSettings and AGENTS.md. Regenerated Library, Temp and Logs were excluded.
- The checkpoint contains 21,388 Asset files. Main Scene and package/version representative hashes were verified. The authored `Assets/Main Scene.unity` hash remained `62391374B89BD3FC8C2FB6E2016198D9B446F6A8279C924BC87355A5954CE525` after this work.
- No scene, prefab, ScriptableObject, terrain, package or ProjectSettings asset was modified.

## Editor and loaded assemblies

The graphical Unity 6000.4.2f1 Editor was open on Main Scene. A source refresh completed and rewrote `Library/ScriptAssemblies/Assembly-CSharp.dll` at 2026-10-29 19:08:58 local time (6,357,504 bytes). The Editor reached its normal editor UI and Play Mode could be entered. The successful prior Test Runner discovery count was 4,222 tests.

The local `unity` CLI does not detect this Editor because the Pipeline package is not installed. Its `unity close` command reported `Closed=false` and did not affect the live Editor. No second Unity process was started for tests. This avoids competing with the open project, but the final revised test set was not executed through NUnit.

## Current owner-observed reproduction

The actual Main Scene was entered in Play Mode. A fresh world displayed $2,500 player liquid cash and no player property in the management holdings list. A prior Main Scene smoke run recorded 96 authoritative Persons and the General Store runtime's explicit “No player General Store has been formed yet” state. This disproves an unconditional player-store grant in that particular New Game route; alternate scenario, Continue, duplicate projection and UI paths remain unverified.

A later live view showed the legacy “Open Management” guidance while the scenario presentation was not visible. The latest compiled correction retires that legacy strip from the Main Scene bootstrap. A subsequent runtime view showed the strip absent, but the intended scenario objective panel still did not appear. Thus LL-PLAY-004 is only partially resolved, and LL-PLAY-005 remains open.

The rendered Main Scene is materially sparse: broad orange road surfaces, large parcel outlines, simple buildings and extensive open ground. This is direct visual inspection. Resource squares, random tree creation, farm decoration and nighttime Person disappearance were not conclusively isolated during this pass.

## Main Scene runtime observations

The earlier 37 simulated-day smoke used the existing 100x control and reached approximately February 7 from January 1. It was a live smoke, not a formal RUN-004 with reconciled ledgers. Runtime diagnostics reported missing journey locations for daily needs, horse feeding shortfalls, and skipped rent collection because required authorities were missing. The log also recorded hundreds of birthday events at the opening date (e.g. P0 turned 35 while already 35), which merits a separate lifecycle/date audit. These are open defects, not passing economic validation.

## Scope limits

This pass did not complete the requested identity, population, animal, item-provenance, terrain, scenery, business operations, farming, freight, save/load, or thirty-day economic audits. No claim of overall playtest readiness is made.
