# House interiors and functional furniture — Codex pass 2

Date: 2026-11-18

Project: `F:\Land & Ledgers`

Unity: `6000.4.2f1`

Branch: `codex/house-interiors-functional-furniture-2026-11-17`

Base checkpoint: `94d1e3365455c6f9c582761acab93af705706d37`

## Result

The source path for finite, household-owned opening furnishings is implemented and compiled. The local development-town configuration has deterministic furnishing variants for its eight configured homes, using real bed, stove, table and chair prefabs. The configuration read-back and the focused functional-authority regressions passed. The required live interior inspection, screenshots and proof of actual NPC use remain **not verified**: the existing Unity Editor became unresponsive while refreshing the large local asset collection as Play Mode was requested. It was not restarted.

## Implementation

- `DevelopmentTownConfiguration.HouseholdDefinition` now owns weighted opening-furnishing variants. Selection hashes the scenario seed, configuration ID and stable household key; it does not reroll each frame or depend on roster ordering.
- `DevelopmentTownFurnishingInitializer.SeedNewGame` resolves each configured household to its existing home, creates stable `EquipmentAsset` identities with household ownership and `ScenarioEndowment` provenance, instantiates only the selected finite plan, installs through `HouseholdFurnishingRegister`, associates each object with that building instance and rebuilds derived sleep/stove/dining registries. Failures do not grant a successful capability.
- Continue rebuilds presentations from the saved furnishing register and placement bindings. It does not grant the opening assets again. The initializer intentionally does not guess presentations for purchased/manufactured equipment absent from the configured plan.
- Runtime furniture approach points are instance-specific and nonserialized. `BuildingFootprintAuthority` registers, updates and removes them with the presentation. The existing custom interior pathing/floor checks remain authoritative.
- Household live inspection summaries now derive whether a cooking stove exists from the physical stove registry where available. A household without an installed stove is no longer described as having one based solely on a legacy summary tier. Stove capability and fuel rules remain with existing authorities.
- The owner-authored `House Natural Wood.prefab` and `Main Scene` were not edited by this pass. Furniture is created as a child of the generated building instance at runtime.

## Local development-town furnishing authoring

The local `Assets/Core/Population/DevelopmentTownConfiguration.asset` now contains weighted plans for all eight current households. The plans use stable equipment IDs and distinct variants/placements. Six homes receive stove capability in every variant; two remain without a stove and sparsely furnished. Beds are assigned to every household. Tables/chairs appear in most of the established homes, with variation in the modest households. This does not add residents or buildings.

The chosen visual prefabs include the tracked Bed and local-only real Stove, Table and Chair prefabs under `Assets/Environment/Props`. Local-only prefab references are not publishable with the source checkpoint, and the asset pack is not staged. The tracked `Cooking Stove` and cabinet models inspected in this checkout are simple placeholder geometry; they were not substituted for the actual local models. Cabinets and shelves are not endowed in this pass because their storage binding/capacity integration is not established by the current equipment install path. No storage capability is inferred from a mesh.

The configuration asset change is therefore intentionally excluded from the Git checkpoint: publishing it without the referenced local-only prefabs would leave missing Unity object references in a clean checkout. It remains available in the authoritative local project and should be treated as owner-local authoring. To make the whole scene reproducible elsewhere, add appropriately licensed/tracked prefab assets or have the owner select approved equivalents, then publish the configuration and validate its references.

Finite scenario-opening furniture uses the explicitly authored `ScenarioEndowment` source and stable item identities; no daily grants or cash transactions are created. Actual startup installation still requires the configured prefab, a valid building floor, a walkable furniture point, and a valid interaction point where one is required.

## Verification evidence

Unity Editor reported version `6000.4.2f1`. The final script recompile completed with `failed=false`, `compilationFailed=false`, and no errors.

Focused Unity EditMode results:

| Suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| `DevelopmentTownConfigurationTests` | 6 | 0 | 0 |
| `HouseholdFurnishingRegisterTests` | 25 | 0 | 0 |
| `InteriorFloorAuthoringTests` | 18 | 0 | 0 |
| `SleepingPlaceRegistryTests` | 19 | 0 | 0 |
| `HouseholdStoveRegistryTests` | 22 | 0 | 0 |
| `DiningPlaceRegistryTests` | 20 | 0 | 0 |
| **Total** | **110** | **0** | **0** |

The added regressions cover repeatable scenario/household variant selection and runtime activity-point registration, movement and removal. Existing suites cover installation ownership, duplicate/refused installs, persistence/idempotence, physical placement and navigation obstructions, sleep capability, stove removal/fuel and dining seating/fallbacks.

### PlayMode and visuals

**Not verified.** The open scene was `Assets/Main Scene.unity`; it was not dirty in the Editor at the pre-test query. The `editor.playmode start` request was accepted, but the Editor then became unresponsive during the asset refresh/import path. Unity's window stopped responding and the pipeline was unavailable intermittently; the process remained alive. The Editor log showed asset categorization/import and post-processing, including an `Untracked` scan, before the stall. I did not force-restart, save, or close the project. No screenshots or claims of furniture visible in PlayMode, successful startup seeding, physical NPC access or actual use are provided.

Return the existing Editor to Edit Mode only after it responds; if Unity eventually entered Play Mode despite the stalled response, stop Play Mode from that existing Editor before resuming authoring. Do not discard or save the owner scene as part of recovery.

## Remaining limitations

1. Live visual differences among three houses and real sleep, cooking and dining use still need PlayMode verification.
2. Cabinet/shelf storage capability and real storage binding are not created here; visible assets alone would not establish capacity or inventory ownership.
3. Purchased/manufactured furnishing delivery-to-install presentation reconstruction is not part of this opening-endowment initializer.
4. Local-only model dependencies prevent the eight-house visual plans from being a portable Git-only scene until approved assets are available to the receiving checkout.
5. No screenshots were captured because Unity did not complete PlayMode entry.

## Changed source and tests

- `Assets/Scripts/Domains/Population/DevelopmentTownConfiguration.cs`
- `Assets/Scripts/Domains/Population/HouseholdState.cs`
- `Assets/Scripts/Domains/Population/PopulationManager.cs`
- `Assets/Scripts/Domains/World/Buildings/BuildingFootprintAuthority.cs`
- `Assets/Scripts/Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs`
- `Assets/Scripts/Orchestration/Bootstrap/DevelopmentTownFurnishingInitializer.cs`
- `Assets/Scripts/Orchestration/Bootstrap/DevelopmentTownFurnishingInitializer.cs.meta`
- `Assets/Scripts/Tests/Editor/Population/DevelopmentTownConfigurationTests.cs`
- `Assets/Scripts/Tests/Editor/World/InteriorFloorAuthoringTests.cs`

Excluded from publication: local `DevelopmentTownConfiguration.asset` authoring due its references to local-only furniture prefabs; owner-modified `House Natural Wood.prefab`, Main Scene, terrain metadata and fonts; all local asset packs and generated/unrelated untracked diagnostics.
