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

## Pass XXI continuation — current findings

- Current local branch is `muse/series2-pass11-final-review`, HEAD `f505faa35f86056396a0b3d6188fa83d20e4a737` (one local commit ahead of its remote parent), with substantial remaining source and Unity asset changes. No reset, clean, checkout, commit, or push was performed during this continuation.
- Unity 6000.4.2f1 has the original Main Scene open in Edit Mode. The previous PlayMode run was stopped; transient runtime state was not saved. The active editor is the Land & Ledgers project. Other Unity MCP processes target Galactic Bounty and are not being used.
- F: currently has about 17.7 GiB free. No new backup folder was created.
- The last captured Main Scene runtime result was 0 meals eaten, 12 missed, 0 purchases, 0 cents spent. Every logged household staple need failed at the seller-knowledge stage: “no legitimately known seller offers this item.” This proves the first refusal in that run; it does not prove downstream affordability or purchase settlement.
- Source inspection confirms `GeneralStoreRuntimeManager.BuildHouseholdTradingPort(BusinessInstanceState)` builds item-level offers from exact declared stock, matching units and positive item prices. `HouseholdMealService` consumes actual bread lots and `DailyNeedsService.ExecuteLotBasedMeals` applies the resulting meal count to nutrition. These are source capabilities, not Main Scene verification.
- A lifecycle edge exists in `SimulationDrivers.BuildHouseholdShoppingLoop`: local General Store knowledge is seeded only in the new-loop branch. If suppliers are registered after a loop already exists, the existing-loop branch only rebinds population authorities and does not seed the newly available local seller. The prior runtime failure is consistent with this edge but has not yet been causally confirmed.
- Focused source implementation work is underway across separate agent scopes for the existing food-discovery path, Bakery/horse-care daily integration, the expanded population fixture, kinship persistence, Business establishment/readiness, storage zones, and the authored management UI. No Unity scene/prefab save has been made during this continuation. The UI specialist is not controlling the live Editor.
- The food transaction and meal path remains the critical integration target: confirm the local-store knowledge repair in current source, compile in Unity, run affected focused tests, then play the actual New Game through purchase, ledger/stock transfer, household receipt, meal consumption, nutrition and save/load. Continue to downstream refusal diagnosis if any step still fails.

## Pass XXI continuation — Main Scene recheck and item-recognition repair

- Removed a startup-order defect that allowed the management UI’s read-only refresh to initialize the default Business roster before the First Ledger bootstrapper. The latest fresh Main Scene run initialized the intended three NPC Businesses (Baldwin-Ooms General Store, Pierce Bakery, Boniecki Fuel Dealer), six Persons and three Households. The default ten-Business roster no longer preempts the slice bootstrap in this observed run.
- Unity 6000.4.2f1 imported the latest source and reported a successful Tundra build (15.80 seconds; 9 items updated, 938 evaluated). The affected editor tests were not rerun after the newest supplier-recognition patch.
- The fresh First Ledger PlayMode run started on 1870-01-01, Day 0. B60 (`general_store_000`) sold and delivered 19 actual bread units to H1 on Day 1. This is runtime evidence of a real store offer and household receipt; this log did not expose the exact cash debit/credit amounts, so those remain unverified from this run.
- After the sale, later shopping diagnostics correctly named the known General Store as out of stock for bread. Previously, zero stock caused the item to be filtered out of the known-seller lookup and misreported as “no legitimately known seller.” `IItemCatalogSupplier.RecognizesItem()` now separates SKU identity from current stock; purchasable offers still require positive actual stock and price. The fresh runtime showed the intended stockout diagnostic and no regenerated bread.
- H1 continued receiving food for several days after Day 1; by Day 7 its larder was empty and missed-meal diagnostics began. H2 and H3 had empty larders and missed meals from Day 3. The run reached Day 18 before PlayMode was stopped. No complete cash reconciliation or save/load cycle was performed.
- The runtime also confirmed unresolved independent blockers: no local offers for preserved meat/unspecified meat/preserved food/potatoes/flour/staple-food SKU aliases; A0 repeatedly had no feed stock registered for owner `general_store_000`; Bakery production is still not connected to a Main Scene day caller; household meal-preparation Tasks can complete, but those completions do not by themselves prove ingredients were consumed or meals were produced.
- Added item-recognition and final-unit negative assertions to `Tests/Editor/Economy/GeneralStoreItemOfferIdentityTests.cs`. They compiled, but the graphical Test Runner’s search field could not be reliably focused, so no new NUnit run is claimed. Prior pass results remain prior evidence only.
- PlayMode is stopped. Unity remains on the original Main Scene in Edit Mode; no transient runtime state was saved to the scene. No commit, push, checkout, reset, clean, or new backup folder was created.

## Follow-up — household shopping refusal diagnostics

- `Domains/Population/HouseholdShoppingLoop.cs`: shopping refusals now distinguish “no seller is known” from “a known seller exists but no item offer is purchasable.” Batch diagnostics also record the earliest observed refusal category among missing item identity, out-of-stock inventory, missing positive price, blocked route, and seller trading restrictions. This is diagnostic only; it does not alter purchase authority or grant stock/cash.
- `Tests/Editor/Population/HouseholdShoppingLoopTests.cs`: added negative cases for a known but unstocked seller. These assert the need stays open and that no cash, stock, or revenue is created.
- Unity imported and compiled this patch successfully; current runtime/editor assemblies refreshed and Console showed 0 errors (57 warnings, primarily obsolete API warnings).
- Fresh execution of the newly added shopping-loop tests is **pending**. The graphical Test Runner enumerated 4,600 EditMode tests, but its search control did not accept text input via the available UI automation, so no suite was selected or run. No full-suite run was substituted.
- Focused diff check for the shopping-loop files reported no whitespace errors. No scene/prefab save, branch operation, commit, or push occurred.

## Pass XXI implementation continuation — 2026-11-02

- Source work completed in this continuation: Business formation now separates legal entity establishment from premises availability; the management Canvas prefab has five principal tabs (Businesses, New Business, Town, Acquisitions, Dialogue); local General Store knowledge is seeded when a registered B60 adapter becomes available even if the shopping loop already exists; Business-owned horse feed accepts only exact physical SKU lots; ready bread consumption avoids an unrelated skipped midday slot counting as a missed nutritional meal; opt-in expanded population fixture, kinship extension, and physical storage-zone foundation were added. These are source/authored-asset changes; most are not yet Main Scene verified.
- Changed files include `Domains/Economy/Creation/BusinessCreation.cs`, `Domains/Economy/Creation/CreateBusinessFormModel.cs`, `Domains/Economy/Farming/Integration/HorseFeedStockAdapter.cs`, `Domains/Economy/Storage/PhysicalStorageZone.cs`, `Domains/Population/DailyNeedsService.cs`, `Domains/Population/ExpandedSliceFixture.cs`, `Domains/Population/HouseholdLifecycle.cs`, `Domains/Population/HouseholdMeals.cs`, `Domains/Population/PopulationGenerator.cs`, `Domains/Population/PopulationManager.cs`, `Infrastructure/Persistence/SaveDtos.cs`, `Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs`, `Orchestration/Systems/SimulationDrivers.cs`, `Presentation/UI/ManagementPanelView.cs`, relevant focused Editor tests, `Core/Population/ExpandedSliceFixture.asset`, and `Core/UI/UI Canvas.prefab`.
- `dotnet build Assembly-CSharp-Editor.csproj --no-restore -p:BuildProjectReferences=false --nologo -v:q` succeeded with 0 errors and 33 warnings. This is a generated-project build, not proof of Unity Editor compilation or gameplay.
- Fresh Unity EditMode focused run executed 93 cases: 91 passed, 2 failed, 0 skipped; source SHA at run: `f505faa35f86056396a0b3d6188fa83d20e4a737`. XML: `TestResults/PassXXI_Focused_EditMode.xml`. Failures: `PhysicalStorageZoneTests.BoundedDimensionRefusesWhenExistingPlacementCannotBeMeasured` (test expected unmeasured existing lot placement to be accepted despite bounded capacity; test correction prepared) and `ExpandedSliceFixtureTests.RuntimeAppend_CreatesNinePeopleFiveSeparateHomesAndExpectedKinship` (test incorrectly expected the player household ledger to be zero before expansion; assertion correction prepared). Neither correction has been rerun yet.
- The suite covered and passed `GeneralStoreBreadPurchaseIntegrationTests`, `DailyNeedsServiceTests`, `KinshipRegistryTests`, `BusinessCreationTests`, `CreateBusinessFormTests`, `Pass7AnimalAvailabilityTests`, `SimulationDriversSceneBindingTests`, `SliceIntegrationSmokeTests`, and the remaining expanded-fixture cases, apart from the exact failures above. It is not a whole-suite run.
- The Unity Test Runner's PlayMode tests temporarily unloaded and then re-opened `Assets/Main Scene.unity`. Main Scene asset was not saved. A brief live session displayed the First Ledger start card and confirmed the 1870-01-01 HUD. Start action was not activated because the Game View was partially occluded by a detached Console; no purchase or post-start scenario behavior is claimed from that attempt.
- Unity Editor window was accidentally closed while dismissing the detached Console. Unity's log records normal shutdown; no scene write is recorded. The same `F:\Land & Ledgers` project was relaunched without a second Editor instance. Verify its window, compilation status, Main Scene, and Edit/Play mode before continuing.
- No commit, push, branch change, reset, clean, or new backup folder was made. One temporary TestRunner API launcher and its `.meta` were removed after the run; the XML evidence remains. Several already-untracked Test Runner generated scene assets and local result files in the dirty project were left untouched.
- Outstanding immediate work: verify reopened Editor and Main Scene; rerun the two corrected tests; capture actual five-tab UI screenshots in PlayMode; perform a genuine First Ledger New Game with B60 discovery, offer, purchase, meal and save/load; keep horse feed shortage explicit; Bakery day-driver integration remains unverified/unwired; update the one progress ledger after the next meaningful verification.
