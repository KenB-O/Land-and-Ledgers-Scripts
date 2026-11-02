# Pass XL Development Checkpoint — 2026-11-02

## Workspace preservation

- Project: `F:\Land & Ledgers`
- Scripts branch: `muse/series2-pass11-final-review`
- HEAD: `34ee209e27c56c75ba7fd813f04f91aa9d4ef0b2`
- The working tree is substantially dirty, including existing local source and authored Unity assets. No branch switch, reset, clean, stash, commit, or push was performed.
- Main Scene was returned to Edit Mode after observation. No runtime state was saved to the authored scene.
- Existing backup folders remain intact. Targeted pre-edit copies for the DayNight controller and HUD controller are under `F:\Land & Ledgers_PassXX_Backups\20261102_daynight_exposure_smoothing` and `F:\Land & Ledgers_PassXX_Backups\20261102_camera_hint_accessor`.

## Changes and validation

- `Presentation/World/DayNightVisualController.cs`: HDRP exposure now follows the controller's smoothed night level. This avoids an immediate exposure jump when simulation speed changes at night; darkness remains presentation-only.
- `Presentation/UI/LandLedgersHUDController.cs`: exposed the effective control hint for diagnostics/tests and made the HUD use `Raise R | Lower F` so stale serialized hint text does not keep the reversed instruction.
- Unity 6000.4.2f1 imported and compiled the current source. `Assembly-CSharp.dll` was refreshed during this session.
- Fresh focused Test Runner results recorded in this session: `StrategyCameraPitchBindingTests` 6/6 passed and `DayNightVisualControllerTests` 7/7 passed. The final focused total is 13 passed, 0 failed. An XML export path was not captured.
- Earlier focused checks in the ongoing development session: `SixPersonDevelopmentFixtureTests` 4/4, `GeneralStoreBreadPurchaseIntegrationTests` 1/1, `GeneralStoreItemOfferIdentityTests` 5/5, and `ExpandedSliceFixtureTests` 4/4 passed. These were not rerun after the two presentation-only edits.

## Main Scene observations

- Fresh First Ledger started on the authoritative date 1870-01-01. The objective HUD appeared on the left, and the obsolete universal store-worker guidance was absent.
- The Main Scene management panel still exposes the older `Businesses`, `Finances`, `Acquisitions`, `Civic`, and `Resources` tabs. The requested `Businesses`, `New Business`, `Town`, `Acquisitions`, and `Dialogue` reorganization has not been authored in the prefab.
- The day-3 runtime log reported H2 and H3 each missed four meals; household errands failed for bread, preserved meat, unspecified meat, preserved food, potatoes, flour, and unspecified staple food because no viable seller offered the requested items. The message does not yet distinguish lack of seller knowledge from zero item stock or another offer refusal.
- Domestic Tasks were scheduled and completed on earlier simulated days, including household fuel handling, chores, and meal preparation. This does not establish that those meal Tasks consumed food or improved nutrition.
- Horse A0 remained unfed on days 1–3 because no feed stock was registered for owner `general_store_060`; its condition-degradation path ran.
- Current run ended in Edit Mode. The Editor Console showed zero errors at stop; warnings were present. No fresh 30-day run was conducted.
- Direct keyboard validation of raising/lowering the camera was inconclusive: the Windows CUA can send discrete key presses but cannot hold a key, while the camera movement path is continuous. The setting/read-model tests passed, and the HUD help text is correct.

## Current status and next executable work

1. Inspect the live B60 item lots, store operating capability, and household seller-knowledge records at startup and when the first household need is refused. The current refusal text is insufficient to identify which condition caused the Main Scene failure.
2. Re-run the bread purchase/receipt/consumption path in the real Main Scene and save fresh Unity Test Runner results with an XML export.
3. Do not invent feed SKUs, unit conversions, opening cash, animal nutrition values, or Ethan/Tia/Shane identity fields. A0's current owner has no actual resolvable feed lot.
4. Bakery production still has no verified Main Scene production-day caller and has unresolved input-unit/Notions data.
5. The nine-person/four-Business fixture, detailed freight, Shane recruitment bridge, and five-tab prefab overhaul remain incomplete.

This checkpoint records current evidence and incomplete work; it does not claim the game is playtest-ready or that the food economy has been repaired.

## Follow-up — household shopping refusal diagnostics

- `Domains/Population/HouseholdShoppingLoop.cs`: shopping refusals now distinguish “no seller is known” from “a known seller exists but no item offer is purchasable.” Batch diagnostics also record the earliest observed refusal category among missing item identity, out-of-stock inventory, missing positive price, blocked route, and seller trading restrictions. This is diagnostic only; it does not alter purchase authority or grant stock/cash.
- `Tests/Editor/Population/HouseholdShoppingLoopTests.cs`: added negative cases for a known but unstocked seller. These assert the need stays open and that no cash, stock, or revenue is created.
- Unity imported and compiled this patch successfully; current runtime/editor assemblies refreshed and Console showed 0 errors (57 warnings, primarily obsolete API warnings).
- Fresh execution of the newly added shopping-loop tests is **pending**. The graphical Test Runner enumerated 4,600 EditMode tests, but its search control did not accept text input via the available UI automation, so no suite was selected or run. No full-suite run was substituted.
- Focused diff check for the shopping-loop files reported no whitespace errors. No scene/prefab save, branch operation, commit, or push occurred.
