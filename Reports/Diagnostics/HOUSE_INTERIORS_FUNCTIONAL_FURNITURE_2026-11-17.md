# House interiors and functional furniture — Codex pass 2

Date: 2026-11-18

Project: `F:\Land & Ledgers`

Unity: `6000.4.2f1`

Branch: `codex/house-interiors-functional-furniture-2026-11-17`

Base checkpoint: `94d1e3365455c6f9c582761acab93af705706d37`

## Result

The finite furnishing plan compiles and initializes in the live development town. A later PlayMode check completed after an earlier attempt had stalled: the game slice initialized with 8 households, and the furnishing initializer reported 34 assets installed (seed 9217). Runtime authorities showed one furnishing binding per asset, six usable stoves, six dining tables, and two deliberately sparse/stove-free homes. A screenshot confirms an actual furnished interior (table and chairs), but three-house visual comparison and real NPC sleeping/cooking/dining use remain **unverified**. The editor has been returned to Edit Mode.

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
| `InteriorFloorAuthoringTests` | 19 | 0 | 0 |
| `SleepingPlaceRegistryTests` | 19 | 0 | 0 |
| `HouseholdStoveRegistryTests` | 22 | 0 | 0 |
| `DiningPlaceRegistryTests` | 20 | 0 | 0 |
| **Total** | **111** | **0** | **0** |

The added regressions cover repeatable scenario/household variant selection and runtime activity-point registration, movement and removal. The final regression also confirms an activity point may ignore its own furniture obstruction while still respecting other obstacles. Existing suites cover installation ownership, duplicate/refused installs, persistence/idempotence, physical placement and navigation obstructions, sleep capability, stove removal/fuel and dining seating/fallbacks.

### PlayMode startup and runtime registrations

**Verified:** A subsequent New Game ran in the actual `Assets/Main Scene.unity` project in Unity `6000.4.2f1`. Runtime reported `initialized=true`, `failed=false`, `Game slice ready`, 98 people, 8 households and 14 buildings. The opening-furnishing log reported `households=8, assets installed=34, seed=9217`.

Household furnishing register and derived capability counts from the live runtime:

| Household | Home ID | Assets / bindings | Stoves | Tables | Seats |
|---|---:|---:|---:|---:|---:|
| H1 | 6 | 6 / 6 | 1 | 1 | 2 |
| H2 | 7 | 5 / 5 | 1 | 1 | 2 |
| H3 | 8 | 5 / 5 | 1 | 1 | 2 |
| H4 | 9 | 6 / 6 | 1 | 1 | 2 |
| H5 | 10 | 1 / 1 | 0 | 0 | 0 |
| H6 | 11 | 4 / 4 | 1 | 1 | 1 |
| H7 | 12 | 6 / 6 | 1 | 1 | 2 |
| H8 | 13 | 1 / 1 | 0 | 0 | 0 |

The earlier failed attempt had found a genuine interaction-point issue: dining tables referenced a legacy `dining` location that was absent, and stove interaction points collided with their own furniture obstruction. The configuration was corrected locally using Unity serialization; runtime activity points now ignore only their owning furniture placement. The final startup log had no household furnishing installation errors. An unrelated WorldGenesis reconciliation warning remained.

**Visual evidence:** [household-1-settled.png](../Playtests/Runs/20261118-house-interiors-furniture/household-1-settled.png) shows an actual table-and-chair interior in PlayMode. This is one house only; its dark lighting limits presentation quality. The requested three distinct interior screenshots were not completed.

**Not verified:** NPC physical traversal into the furnished rooms and actual sleeping, cooking, heating, or seated dining. Registry counts prove capability registration, not actual use. No character-use success is claimed.

The Editor was responsive and explicitly stopped from Play Mode. Final status: ready, not compiling, `playMode=stopped`, Unity `6000.4.2f1`. It was left in Edit Mode. The Main Scene and structural house prefab were not saved by this pass.

## Remaining limitations

1. Live visual comparison among three houses and real NPC sleep, cooking and dining use still need PlayMode verification.
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

## Final source checkpoint

The follow-up interaction-point fix and its regression are included in the second checkpoint on `codex/house-interiors-functional-furniture-2026-11-17`. See the final handoff for the exact pushed SHA. The local configuration authoring remains excluded for the portability reason above.
