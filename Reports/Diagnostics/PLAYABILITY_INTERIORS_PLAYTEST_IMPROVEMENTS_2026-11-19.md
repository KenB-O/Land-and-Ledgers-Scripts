# Playability, Interiors and Playtest Improvements

Date: 2026-11-19

## Checkpoint

- Branch: `codex/player-economic-participation-2026-11-18`
- Base HEAD reviewed: `8537f52fb7dea263fa3ba62da6cbd4d6b5aefbf8`
- Unity: 6000.4.2f1
- Unity state at inspection: Edit Mode, Main Scene open. Read-only UI inspection showed the legacy dock tab still titled `60-Day Playtest` before the newly added `OnEnable` title update could be loaded. The visible Console buffer showed 349 messages, 131 warnings and 0 errors; this is retained buffer state, not a new run. No scene save, compilation, test run or PlayMode session was started in this pass.
- Working tree already contained extensive owner-local changes, including the Playtest runner, Main Scene, DevelopmentTownConfiguration, prefabs, terrain, fonts, asset packs and unrelated economic source. Those files were preserved; no broad staging or commit was performed.

## Implemented

### Playtest tool

- Existing EditorWindow keeps its class, source path and `.meta` GUID while the menu and window title read `Playtest`.
- Duration controls expose 5, 10, 15, 30, 60 and 90 days plus a positive custom value. The selected count is used for the configured stop day and progress.
- Run report and run-index headings are generic; historical evidence and protocol file names remain unchanged for compatibility.
- Missing day-boundary snapshots are recorded explicitly and invalidate calendar completion. A scripting-domain reload during a running session invalidates the evidence, pauses simulation and exits Play Mode.
- Added duration, snapshot-gap and domain-reload regression tests. These are prepared but unexecuted.

### Notice Board overlay

- Removed the `OpportunityPressureRuntimeManager` pushes into the HUD alert strip. Housing, rent and other opportunity calculations, notices, snapshots and `BuildNoticeBoardText` remain available to their existing read models and diagnostics.
- Added a regression that the read model remains available without populating the playable HUD alert strip. Test is unexecuted.

### Camera

- Increased the authored close-pan sensitivity in `DefaultStrategyCameraSettings.asset` from 0.08 to 0.30; wide-scale response is unchanged.
- Added an Inspector-tunable 6-unit maximum wheel zoom step per input event. Both wheel paths are bounded, reducing large-distance jumps while retaining smooth target-distance interpolation.
- Added a configurable maximum outward camera-arm recovery speed (24 m/s by default). Collision retraction remains immediate, while outward extension is capped during runtime so a retained distant zoom target cannot snap the camera outward when an obstruction clears. Edit-time and explicit immediate rig placement still snap as designed.
- Added focused tests for close pan response, bounded wheel input, immediate collision retraction and capped outward recovery. Tests are unexecuted.
- Camera obstruction still uses its configured collision mask. The reported under-floor trap requires live inspection of camera focus, floor collider and layer interaction; it is not claimed fixed without that evidence.

### Prop placement and interior navigation

- Household and business prop presentation now aligns the rendered mesh bounds bottom to the configured building-local floor elevation after applying prefab-authored scale, visual multiplier and rotation. This addresses differing prefab pivots without applying a global scale.
- Added a bounds-versus-pivot floor-alignment test; unexecuted.
- General Store shelves were moved from building-local X 4.7 to 5.5, keeping their existing Z offsets, rotations, scale, floor index and interaction anchors.
- Asset inspection confirms the configured General Store Empty Handling Crate references the real `Assets/Environment/Props/Crate04.prefab`, with a visible mesh, a roughly 1.22 × 0.70 × 0.40 m root box collider and a separate roof mesh child. Its scenario entry is an empty handling fixture with no inventory authority; rendered-bounds placement grounds its visual to the floor. No goods or functional stock are created by its mesh.
- The General Store counter references the local `Wooden Kitchen Counter` prefab, whose root has scale 1, a mesh renderer, and no collider; its FBX is the 46 KB binary model named `Wooden_Kitchen_Counter_low`. The prefab's serialized root Y=0.882 is overwritten by the business initializer's building-local configured position (0.8) and then corrected using rendered bounds. The scale multiplier remains neutral to preserve the owner's model calibration. The shelves are configured at building-local X=5.5. The FBX vertex bounds and final visual scale still require Unity's loaded mesh/Inspector and in-game view; I did not guess another scale or Y offset.
- The existing world-visual rebuild callback rehydrates owned household furnishing presentations and their nonserialized runtime activity anchors after a town rebuild. The Nov. 19 run showed those anchors disappearing after rebuild; the recovery hook is present in the current local source but remains unverified in a fresh PlayMode run. Existing interior routes use the building's `main-entrance` and registered furnishing activity anchors, and the initializer reports route failures. The requested male/female entry and exit observations remain unverified in live gameplay.
- Added an EditMode regression that starts from an owned bed binding with its presentation/activity anchor absent, invokes `RestorePresentations`, verifies the bed anchor has a route from the main entrance, then invokes restoration again and checks there is no duplicate presentation, asset or placement binding. It is prepared but unexecuted.
- Source review of the Playtest runtime gate confirms it rejects a configured living-resident, household, or business count mismatch before the run begins (for example, 98 versus 14 residents). The count check is present but has not been exercised in Unity during this pass.
- The counter's visual scale and the floorboard camera issue require actual Editor/PlayMode inspection; no unsupported scale multiplier or universal Y=1.2 adjustment was applied.
- Static inspection of the current `House Natural Wood.prefab` shows an authored ground-floor plane at local Y=0.8 with a walkable polygon approximately X=-3.8..3.8, Z=-4.15..1.45. Its main entrance indoor point is (2.815, 0.833, -0.317), outdoor approach (2.815, 0.833, 3.683), and `Anchor_FrontDoor` at (2.815, 0.833, 1.683), so the authored crossing lies between those endpoints. The prefab contains six colliders; its GameObjects are layer 0 (Default). The loaded camera settings use all layers for close-pivot rays and obstruction casts. The consolidated house mesh collider therefore cannot be excluded as a floor-only layer without also changing wall collision. This makes floor obstruction a credible source-level risk, but only runtime hit/camera-position evidence can confirm the reported trap and justify a safe filter.

## Validation and remaining work

- `git diff --check` passed for the directly edited implementation/test/docs files. Git emitted only line-ending normalization notices.
- Unity compilation and EditMode tests were not run. Latest process/resource check found one responsive Editor (PID 23852), two other Unity processes, and a live but nearly idle `Unity.ILPP.Runner` (CPU increased about 0.3 seconds over an 8-second observation). Physical RAM availability was 2,953 MiB; Windows committed 53.62/57.93 GiB (about 4.02 GiB headroom, 92.5% committed). A subsequent check showed 49.96/53.96 GiB committed (about 4.00 GiB headroom, 92% committed). `Editor.log` was 174,519,919 bytes and had not changed since 08:03 local time; its tail ended after scene backup loading and licensing messages, not an active compile/test. The last directly observed UI state was Edit Mode, but Play/Edit state was not re-read this turn. Given prior Windows error 1455 and current commit pressure, compilation/domain reload is still not safe to start.
- No actual camera, object-placement or NPC interior-navigation observation was made. Unity was left in Edit Mode and Main Scene was not saved.
- New code and tests remain local and uncommitted because the runner, configuration, and several implementation files already contained owner-local diffs that cannot safely be mixed into a commit without review. No publication was attempted.

### Follow-up source and environment check

- The Unity window remains open and responsive on Main Scene. This follow-up did not establish Play Mode state from the Editor toolbar and did not alter the Editor.
- Current machine sample: 16,238 MiB physical RAM; 2,773 MiB free; 51,501 MiB committed of a 55,250 MiB limit (about 93.2% committed). The existing Editor process had about 1.79 GiB working set. Given the prior IL post-processor error 1455, no compile, domain reload, test run or PlayMode entry was attempted.
- Source review does not confirm the historical hidden-mover hypothesis as a root cause: `PopulationPathingDirector` calls `ShowAtCurrentLocation` when it creates a resident actor (around line 678), and scheduled resident routes are started with `hideWhenArrived: false` (around line 2413). Domestic activities use the building-interior route authority and report unsupported route starts; movement APIs refuse to route a hidden mover. This makes a specific runtime refusal trace necessary before changing visibility policy. The source has a public `TryShowPersonAtCurrentLocation`, but ordinary domestic route code does not call it.
- Static source and prefab checks still cannot show that the authored indoor route is traversable in the loaded Main Scene. The ground-floor polygon and doorway chain appear internally consistent in `House Natural Wood.prefab`; runtime collision, grid registration, movement and task execution remain Unity-verification items.
- Latest `git diff --check` passed for the targeted implementation, tests and report. Working tree remains broadly dirty, with owner-local assets and unrelated source preserved; no files were staged, committed or pushed.

## First Unity verification

1. When memory headroom is healthy and asset import has settled, compile once in the existing Editor.
2. Run `FurnishingPresentationCalibrationTests`, `StrategyCameraZoomSensitivityTests`, `OpportunityPressureNoticeTests`, `Playtest60DayRunnerTests` and `Playtest60DayRecoveryTests`.
3. In Main Scene, open the Playtest window and verify each preset plus a custom positive duration; verify `metadata.json`, progress and exact day stop for a short run, then confirm missing snapshots/domain reload produce `INVALID`.
4. In a short New Game, check the top-right HUD, General Store shelf/counter/crate floor placement, and at least two male and two female residents entering, reaching a furniture anchor, and leaving their own houses.
5. Inspect close camera pan, zoom increments, an interior floor/wall collision, and return to town overview. Leave the scene unsaved unless the owner asks otherwise.
