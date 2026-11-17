# Expanded Campaign Unity Integration Verification — 2026-11-16

## Executive status

**Partial integration verified; live gameplay acceptance is blocked by the active DevelopmentTownConfiguration asset read-back.** The imported Muse source compiles in Unity 6000.4.2f1. The final EditMode run completed. A PlayMode startup was attempted against the real Small-town scene and halted before town generation; objectives A–I therefore have no live gameplay proof. The Muse branch was published and confirmed at GitHub before Unity verification. Codex fixes and this report are to be pushed on the dedicated verification branch.

## Git integration

- Baseline: `codex/building-equipment-groundwork-2026-11-16` at `494c75fa36ff541f4c487cc98dd5ba0b29b4de0c`.
- Verified bundle: `land-ledgers-expanded-integration-2026-11-16.bundle`, 858,219 bytes, SHA-256 `f9af97fa72455af16fa0c9dad2922c571d849cd6aff970862fcc54dfdb16f2f9`; `git bundle verify` passed and required the exact baseline.
- Imported Muse branch: `muse/expanded-integration-2026-11-16`, HEAD `cbd8e2ce1b901919f908f54a9e23318bc5189734`. It has the expected two-parent amalgamation and 48 package commits plus two integration-support commits in the history.
- The imported Muse branch was pushed to `origin` before long Unity testing; direct remote ref verification matched the full SHA above.
- Working branch: `codex/expanded-unity-verification-2026-11-16`, based on the imported Muse commit. No merge to `main`, force push, reset, clean, or stash was used.
- Local owner edits and the approximately 106,690 local/untracked entries were not staged. No path overlap was found between the Muse import and the tracked owner-local modifications. Natural Wood prefab cleanup, Main Scene, terrain metadata, font assets, and local asset packs remain excluded from publication.

## Unity compilation and tests

- Unity: **6000.4.2f1**. The active Editor reports ready, compilation idle, Play Mode stopped.
- Fresh script recompile: **completed, no compiler errors** (`failed=false`, `compilationFailed=false`). Initial integration compile blockers were `EntityId` name collisions introduced by Unity 6's `UnityEngine.EntityId` and an incorrectly cased `Contents` DTO member; narrowly corrected.
- Final full EditMode run: **8,071 total; 7,906 passed; 165 failed; 0 skipped; 0 inconclusive**, 475.92 seconds.
- Previous full run on the same branch: 8,071 / 7,896 passed / 175 failed. Name-by-name comparison found **10 prior failures no longer failing and no newly failing test names**. This comparison is against the immediately prior run, not a historical Unity baseline.
- Historical resumed-wave Unity result was 7,390 / 7,228 / 162 failures, but its exact failure identities were not retained. Those 162 remain **unclassified**; they cannot be called pre-existing based on totals alone.
- Muse Roslyn result (4,996 passed / 20 failed) remains source-harness evidence only and was not reproduced in this Unity run.

Focused final checks:

| Suite | Result |
|---|---:|
| `FurnitureStorePurchaseG3Tests` | 28/28 passed |
| `WorkstationTests` | 7/7 passed |
| `HouseholdFuelCookingDriverB3Tests` | 16/16 passed |
| `SleepTickDriverTests` | 10/10 passed |
| `SleepingPlaceRegistryTests` | 19/19 passed |
| Route-backed sawmill-to-yard shipment custody regression | 1/1 passed |
| `DayLength1440SecondsTests` | 10/10 passed |
| `DevelopmentTownConfigurationTests` | 2 passed / 3 failed |

The 1,440-second day is the approved value. Seven old assertions expected 300 seconds and contradicted the owner's ruling; those tests were renamed and updated. Production time behavior was not changed.

### Remaining Unity test failures

- **Development town (3 failures):** the asset on disk has the current 15-person, 8-household, 7-business, 3-employee YAML, but Unity's `AssetDatabase.LoadAssetAtPath` and runtime bootstrap read an older/inconsistent object. Validation reports businesses missing unique keys/operation IDs/configured owners; tests read 10 people, 6 households and 1 employee. Targeted `ImportAsset`/refresh, scene reload, and a graceful Editor restart/reopen did not correct the read-back. No asset YAML or Main Scene was rewritten to force success.
- **Other current failures remain unresolved and are listed in the full JSON.** Notable groups include construction/Mine candidate expectations, two Millbrook flour tests expecting 220 where the test observes 450, and broad UI/economy/terrain/save-load failures. The exact failure names and messages are in the exported Test Runner JSON. No unrelated production policies were changed to chase them.
- The isolated freight test initially exposed the obsolete assertion that a route-backed recurring-order manifest could not exist. The final regression confirms actual committed cargo remains in shipment custody while the destination has no receipt or settlement. It passes in the final focused run.

## PlayMode startup and scenario blocker

**FAILED:** A real PlayMode start was attempted on the active Small-town scene. `FirstLedgerSliceBootstrapper.bootstrapStatus` returned:

`Development town startup stopped: Development businesses need unique keys, an operation ID, a configured owner, and non-negative opening cash.`

Startup stopped during development configuration validation, before population/business generation. The on-disk asset is clean in Git and its YAML contains the expanded campaign data, while Unity reads older values. Several safe non-destructive refresh/restart methods were attempted and did not fix the discrepancy. This is the confirmed immediate blocker; its underlying AssetDatabase/import/cache cause remains unconfirmed.

Consequently:

- PlayMode startup: **FAILED**.
- Objectives A–I (sleeping, domestic activity, furnished-home economics, furniture production/freight, forestry-to-lumber chain, NPC construction, property closing, player physical presence, conservation): **NOT TESTED** in live gameplay.
- G-3/G-4: focused EditMode coverage passed where listed, but the requested physical household/building/transport demonstrations are **NOT TESTED**.
- DEF-009, DEF-067 and DEF-011: **NOT TESTED** in this pass. No live Save/Continue, daily reconciliation, or positive NPC commission claim is made.
- No 30-day simulation was run because the actual scenario failed before it existed.

## Console and visual-state evidence

- Unity Pipeline Console ground truth sampled after the suite: **7 errors, 3,361 warnings, 2,304 logs**; compilation failure false. The retained Pipeline buffer held only 2,000 entries (5 errors, 1,824 warnings, 171 logs), so its counts do not equal the Editor's lifetime counters. The full Editor.log is retained as the authoritative textual record.
- The retained buffer's errors are test/negative-case diagnostics: missing ScenarioAsset, empty ScenarioId, and duplicate-authority checks for bootstrapper, PopulationManager, and TownWorldController. These occurred during tests; they are not evidence that the untouched Main Scene itself had duplicate authorities.
- Major warning clusters include `SendMessage ... during ... OnValidate` from UI creation in validation paths, repeated `new` construction warnings for a ScriptableObject test fixture, and the Unity AI Account API timeout. A legacy White Plaster Animation clip also produced an intermittent load error during one isolated fixture run; the final full suite and final freight regression completed, and the asset was not edited. These are captured in the raw exports; no warnings were globally suppressed.
- The Main Scene was not saved. After the full Test Runner, Unity displayed a dirty marker on Main Scene. The Editor remains out of Play Mode; no attempt was made to save, close, revert, or discard that in-memory state. The Main Scene file remains an owner-local modified file and was not staged.
- Visual inspection of the White Wood store, terrain tiles, furnishings, and runtime interactions was **NOT TESTED**; no prefab, scene, terrain, font, or commercial asset was edited by this pass.

## Codex changes on the verification branch

1. `Assets/Scripts/Domains/Economy/Acquisitions/AcquisitionMarketManager.cs` — restore correct DTO initializer member casing (`Contents`).
2. `Assets/Scripts/Domains/Economy/GenericBusiness/FurnitureStorePurchase.cs` — disambiguate project `EntityId`.
3. `Assets/Scripts/Domains/World/Buildings/BuildingEquipmentPlacement.cs` — preserve the `furniture.unspecified` free-slot definition instead of deriving an incompatible type from a GameObject name.
4. `Assets/Scripts/Orchestration/Player/PlayerFurnishingPresenceDriver.cs` — disambiguate project `EntityId`.
5. `Assets/Scripts/Tests/Editor/Economy/CarpentryWorkshopDriverG1Tests.cs` — disambiguate project `EntityId`.
6. `Assets/Scripts/Tests/Editor/Economy/CarpentryWorkshopF1Part2Tests.cs` — disambiguate project `EntityId`.
7. `Assets/Scripts/Tests/Editor/Economy/ConstructionInputSupportTests.cs` — assert route-backed shipment custody and defer destination stock/settlement assertions until delivery.
8. `Assets/Scripts/Tests/Editor/Economy/MillOpeningEquipmentEndowmentTests.cs` — disambiguate project `EntityId`.
9. `Assets/Scripts/Tests/Editor/Economy/NpcAcquisitionContentsDef011Tests.cs` — disambiguate project `EntityId`.
10. `Assets/Scripts/Tests/Editor/Economy/OpeningEmploymentEndowmentTests.cs` — disambiguate project `EntityId`.
11. `Assets/Scripts/Tests/Editor/Economy/WorkstationTests.cs` — give the station its real water power source so the test isolates milling equipment readiness.
12. `Assets/Scripts/Tests/Editor/Population/HouseholdFuelCookingDriverB3Tests.cs` — disambiguate project `EntityId`.
13. `Assets/Scripts/Tests/Editor/Population/SleepTickDriverTests.cs` — disambiguate project `EntityId`.
14. `Assets/Scripts/Tests/Editor/Time/DayLength1440SecondsTests.cs` plus its original `.meta` GUID (renamed from `DayLength120SecondsTests`) — align stale 300-second assertions with the approved 1,440-second day.

No gameplay economy, Main Scene, prefab, or asset-pack changes were made by Codex.

## Deferred decisions and integration points

Owner decisions remain open and were not guessed: a priced hay source; carpenter wage calibration; F-D11-2 sleeping-capacity policy (A/B/C/D); F-D11-4 player-household scan; F-D11-7 progress-billing cash port; F-D11-8 no-third-construction-model documentation; C-6 never-departed regression; C-9 freight-failure goods recovery. Also open: NPC property acquisition production caller/value input, D-10 lodger driver call, property agreement/close drift verification, G-3 lot-level COGS, paid-furniture-install retry, secondhand sale production path, G-4 install-time storage binding/measurement, D-12 prerequisites, and construction-loan evaluator terms/profile inputs.

The generic BusinessType migration fork remains unchosen. No deferred feature queue was implemented.

## Evidence exports

Persistent evidence directory: `F:\Land & Ledgers Verification Evidence` (outside the Unity repository):

- `Unity-Editor-full-console-expanded-integration-2026-11-16.log` — 133,176,037 bytes, SHA-256 `B687565F0C8BBC38124970E1AF23375F63EB0CAB9A4980B7EDA8E07E46D70EC8`.
- `Unity-Console-retained-buffer-expanded-integration-2026-11-16.json` — 2,000 retained messages with stack traces and counter snapshot.
- `Unity-Console-status-expanded-integration-2026-11-16.json` — Editor counters and compilation state.
- `EditMode-TestRunner-expanded-integration-2026-11-16.json` — complete final Test Runner result payload.
- `EditMode-failures-expanded-integration-2026-11-16.json` — final failure list.

## Publication and follow-up

The Muse integration branch is already published and verified. Codex source/tests and this report are limited to explicit paths on `codex/expanded-unity-verification-2026-11-16`; owner-local modifications and untracked assets remain excluded. The final Codex commit and direct GitHub SHA check are recorded in the handoff.

**Recommended next step:** isolate and repair the DevelopmentTownConfiguration Unity asset read-back discrepancy without rewriting its authored YAML or touching Main Scene; then rerun startup validation and one bounded New Game PlayMode smoke test before attempting objectives A–I.
