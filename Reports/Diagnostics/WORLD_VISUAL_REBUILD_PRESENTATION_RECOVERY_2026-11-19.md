# Runtime presentation recovery after town visual rebuilds

## Confirmed cause

In Main Scene run `20261119T082642Z-117e519b`, the town rebuilt its visuals on day 21 (Console event 1303) and day 28 (event 1712). The day-21 stack trace shows `AcquisitionMarketManager.ResolvePassiveDevelopmentInventory` calling `TownWorldController.TrySurfaceExpansionPlotsAfterLandPurchase`, which calls `BuildVisuals`. `BuildVisuals` clears generated visual children and recreates the building instances. Household furniture and its nonserialized activity locations are attached to those instances. Immediately after the day-21 rebuild (event 1305), residents could no longer resolve dining-chair activity IDs; later sleep and stove routes had the same failure. The run had seeded 34 furnishing assets, so this was runtime presentation loss after startup, not missing opening ownership.

## Source-only repair

- `TownWorldController` now raises `WorldVisualsRebuilt` after a complete visual rebuild.
- `FirstLedgerSliceBootstrapper` subscribes to that lifecycle event and recreates household and configured business presentations from their existing ownership/equipment authorities. The subscription is released on disable/destroy and re-established on enable/restore.
- Household restore now rebinds the recreated placement to the original binding ID, owner, acquisition source, building location and existing asset. It does not create a second asset or installation binding.
- Added a regression for runtime rebuild notification and a regression for placement identity/ownership/provenance preservation.

## Verification status

Source review confirms the observed call chain and the recovery path. `git diff --check` passes. The new Unity EditMode tests and compilation have **not** run. The latest environment reading was 3.82 GiB free physical RAM and 4.51 GiB free system commit; the active Unity 6000.4.2f1 Editor was responsive in Edit Mode. Compilation was deferred because this machine has a known Windows error 1455 during IL post-processing, and the current commit headroom remains limited. The captured 30-day run is pre-fix and does not verify this repair.

## Changed files

- `Assets/Scripts/Domains/World/TownWorldController.cs`
- `Assets/Scripts/Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs`
- `Assets/Scripts/Domains/Population/HouseholdFurnishingRegister.cs`
- `Assets/Scripts/Orchestration/Bootstrap/DevelopmentTownFurnishingInitializer.cs`
- `Assets/Scripts/Tests/Editor/World/TownWorldExpansionTests.cs`
- `Assets/Scripts/Tests/Editor/Population/FurnishingWiringB8Tests.cs`

No scenes, prefabs, ScriptableObjects or other owner-local assets were changed.
