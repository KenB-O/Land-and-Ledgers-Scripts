# Strategy camera close and distant control — Codex pass 1

Date: 2026-11-17 (America/Toronto)

Project: `F:\Land & Ledgers`

Unity: `6000.4.2f1`

Branch: `codex/house-interiors-functional-furniture-2026-11-17`

## Findings and changes

The camera's configured near limit was 8 units. Close-view pivoting only ran when mouse rotation began, and it refused to rebase while the smoothed rig root lagged behind its target focus. The camera arm had no obstruction sweep. Terrain-follow could also adjust the focus while the camera was at a close indoor distance.

Changes:

- Lowered the default settings asset's minimum zoom to 1.5 units and close pan sensitivity to 0.08. `closeControlStartDistance` (16), `standardControlDistance` (40), close rotation sensitivity, pitch curve, wheel curve, and zoom smoothing remain as authored.
- Added opt-in-by-default close view-focus adoption when wheel zoom crosses into the close range. Focus adoption now returns a result, permits the smoothed pivot to lag, and keeps the existing view ray and bounds checks.
- Disabled terrain-follow height correction at close indoor distances so it cannot pull an indoor focus toward outside ground.
- Added an Inspector-configurable sphere sweep for the camera arm, including layer mask, radius, padding and a focus-side ignore offset. It shortens the rendered arm without changing the requested zoom target; the arm eases back through the existing zoom smoothing after an obstruction clears.
- Removed the per-frame FOV write from the controller. The loaded Main Scene camera was 70 degrees while the shared settings asset is 45 degrees. PlayMode checks preserved the scene's 70-degree value. No near-clip setting or setup utility was changed or invoked.

## Verification

Unity compiled the updated source with `compilationFailed=false` and no compile errors from these camera files. Focused `StrategyCameraZoomSensitivityTests`: **6 passed, 0 failed**. Coverage includes near-range zoom configuration, distance-adaptive pan/rotation, wheel curve continuity, obstruction stop/recovery, and preserving an authored 70-degree camera FOV.

Actual PlayMode inspection used the loaded Main Scene's game camera and existing development town:

- Captured the bed, stove and table in household 6's home; captured the stove, table and chair in household 2's differently oriented home.
- Queued a mouse-wheel input while aimed at the second home's stove. Target zoom changed from 3.2 to 2.681 units; focus remained on that furnishing and camera FOV remained 70 degrees.
- Queued W and E controls in close range. The focus and yaw changed through the existing game camera input path. Input hold duration was not calibrated, so this confirms operation, not a units-per-second benchmark.
- Viewed the town at 100 units, then returned the view to the first home's table at 2.8 units. The return view remained inside the same home.
- Unity was returned to Edit Mode. `Assets/Main Scene.unity` reported `isDirty=false`; it was not saved.

Screenshots (all captured from the actual Game view during PlayMode):

- [`camera-house-006-table.png`](../Playtests/Runs/20261118-house-interiors-furniture/camera-house-006-table.png)
- [`camera-house-006-stove.png`](../Playtests/Runs/20261118-house-interiors-furniture/camera-house-006-stove.png)
- [`camera-house-006-bed.png`](../Playtests/Runs/20261118-house-interiors-furniture/camera-house-006-bed.png)
- [`camera-house-009-stove.png`](../Playtests/Runs/20261118-house-interiors-furniture/camera-house-009-stove.png)
- [`camera-town-overview.png`](../Playtests/Runs/20261118-house-interiors-furniture/camera-town-overview.png)
- [`camera-house-006-return.png`](../Playtests/Runs/20261118-house-interiors-furniture/camera-house-006-return.png)

## Limits and diagnostics

The PlayMode checks placed the game camera at measured runtime furnishing positions with the controller API, and used queued Input System events for the wheel, W and E checks. This exercised the existing controller and render path, but was not an unassisted owner mouse/keyboard session. The screenshots show the town's current dark lighting; lighting was outside this pass.

The general Unity Console retains errors unrelated to camera behavior, including the local `Single Story Business Building White Plaster Animation 1.anim` load error and Unity Pipeline HTTP response errors during Editor stalls. The final Editor reported compilation successful and no camera-test failures. No unrelated asset or console issue was changed.

## Files

- `Assets/Scripts/Presentation/Camera/StrategyCameraController.cs`
- `Assets/Scripts/Presentation/Camera/StrategyCameraSettings.cs`
- `Assets/Core/Camera/DefaultStrategyCameraSettings.asset`
- `Assets/Scripts/Tests/Editor/Camera/StrategyCameraZoomSensitivityTests.cs`
- The six screenshots linked above.

No Main Scene, camera setup utility, owner prefab or unrelated asset was changed.
