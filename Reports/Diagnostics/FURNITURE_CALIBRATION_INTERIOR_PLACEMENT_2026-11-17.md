# Furniture Calibration and Interior Placement

## Scope and preservation

- Unity: `6000.4.2f1`.
- Source baseline: `codex/house-interiors-functional-furniture-2026-11-17` at `4a35f53bc72213f302bac5b032099eb50baf5a68`.
- Work branch: `codex/furniture-calibration-interior-placement-2026-11-17`.
- The manually adjusted prop prefabs under `Assets/Environment/Props` are owner-local and untracked. They were inspected and measured, but not modified, copied, or staged.
- The owner-local `DevelopmentTownConfiguration.asset` was changed only through Unity `SerializedObject` and `AssetDatabase` APIs. It references those local-only props and remains excluded from the source checkpoint.
- Main Scene, building, terrain, font, and third-party asset changes were not staged or saved by this pass. At final verification the Editor was in Edit Mode and `Assets/Main Scene.unity` was clean.

## Findings and implementation

1. `DevelopmentTownFurnishingInitializer.CreatePresentation` replaced each instantiated prefab's root scale with the configuration's `localScale`. The local configuration used `(1,1,1)`, so the manually authored Chair and Table root scales `(0.76347,0.76347,0.76347)` and Stove scale `(0.75,0.75,0.75)` were lost. The serialized field now migrates as a neutral `visualScaleMultiplier` using `FormerlySerializedAs`; it multiplies the prefab-authored root scale once, preserving current owner calibration.
2. Furniture positions used configuration Y values around `1.35–1.505`, while the authored interior floor is at local Y `0.8`. A pre-fix PlayMode capture showed floating furniture. Floor-aligned furnishings now use the selected `BuildingFootprintAuthority` floor elevation. The owner-local configuration's 64 floor-bound variant entries were normalized to local Y `0.8` using Unity serialization.
3. The local Chair mesh's visual front points along prefab-local `-Z`; its prior `+Z` assumption pointed through the backrest. Per-chair calibration now records `-Z`, and dining chairs orient toward the nearest configured table. Interaction direction remains tableward.
4. The H1B stove yaw is `180°` per the owner's observation; other stove rotations remain unchanged.
5. The Bed mesh headboard is at local `-X`. The H1B east bed and all other configured paired east beds (`h1-a`, `h2-b`, `h4-a`, `h4-b`, `h7-a`, `h7-b`) now yaw `180°` while their west beds remain at their authored yaw, aligning the pair foot-to-foot. These are per-instance configuration changes; the shared bed prefab was untouched.
6. Calibration data now supports visual scale multiplier, visual front axis, pivot offset, floor alignment, and an optional local X/Z navigation-footprint override. Renderer bounds remain the default obstruction source. `BuildingEquipmentPlacement` applies a configured footprint only when explicitly enabled.

Unity measurements from the loaded local prefabs: Chair rendered bounds approximately `0.63 x 1.06 x 0.59`, Table `1.19 x 0.84 x 1.19`, Bed `2.47 x 1.37 x 1.28`. These values describe the current local assets, not universal dimensions.

## Verification

### Compilation and EditMode tests

Unity compilation completed without errors. The final targeted Editor run passed **121 tests, 0 failed**:

| Suite | Passed | Failed |
|---|---:|---:|
| `FurnishingPresentationCalibrationTests` | 5 | 0 |
| `DevelopmentTownConfigurationTests` | 6 | 0 |
| `FurnishingWiringB8Tests` | 8 | 0 |
| `HouseholdFurnishingRegisterTests` | 25 | 0 |
| `FurnishingDecisionsB6Tests` | 38 | 0 |
| `DiningPlaceRegistryTests` | 20 | 0 |
| `InteriorFloorAuthoringTests` | 19 | 0 |

### PlayMode inspection

PlayMode screenshots after the floor-height and scale correction were captured and remain locally available at:

- `F:\Land & Ledgers\Reports\Playtests\Runs\20261118-furniture-calibration\home-006-grounded.png` — table, chairs, stove, and interior floor.
- `F:\Land & Ledgers\Reports\Playtests\Runs\20261118-furniture-calibration\home-008-grounded.png` — a second home's table, chairs, stove, and doorway.
- `F:\Land & Ledgers\Reports\Playtests\Runs\20261118-furniture-calibration\home-011-grounded.png` — a more modest third home's room and table.

The captures show three distinct house instances with visibly grounded furniture. The screenshots are excluded from Git because they depict owner-local, untracked third-party prop assets. Runtime inspection confirmed prefab-authored scales were retained; the H1B stove yaw was 180°; chair approach routes succeeded in homes 6, 8, and 9; chair front-to-table dot product was `1.00`; bed and stove activity routes succeeded in those homes. Rendered furniture bottoms matched the floor within approximately `0.00005` units. Tables themselves do not expose activity-location IDs, so the direct route query correctly reports no table activity point.

**Not verified:** an NPC physically completing a sleep, cooking/heating, or seated meal activity. Route reachability is not proof of task execution. The screenshots are dark due to the existing scene lighting; lighting was outside this pass. No fresh full diagnostic run was done to classify dining/furniture blocking-point warnings.

The final Unity state is Edit Mode, no compilation or tests running, and the Main Scene is not dirty. No Main Scene save was performed.

## Publication boundary

The source, tests, and report are the intended checkpoint. The owner-local configuration, local-only prop prefabs, and screenshots remain uncommitted and unpushed because they depend on owner assets and contain broader local authoring. Their current in-project adjustments and screenshots remain available locally. No unrelated owner changes were staged.
