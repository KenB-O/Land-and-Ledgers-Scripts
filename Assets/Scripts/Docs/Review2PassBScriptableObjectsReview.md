# Review 2 Pass B — ScriptableObjects

Base: `muse/review2-passA-general` at
`40c31331eb21308d9a7f965aa44af7d9c3fe7bfc`. Branch for this pass:
`muse/review2-passB-scriptableobjects`, parent exactly the Pass A tip.
No merge, no force-push, no history rewrite. `main`,
`integration/pass12-unity-readiness`, Pass 11 (`34ee209e`), and Pass A
(`40c31331`) are untouched by this pass.

Context, stated plainly: the second read-only review found NO
post-Pass 11 Codex work published on GitHub (local commit `f505faa3`
is not accessible from any published ref), so this pass works from
the Pass A tip, not from any Codex snapshot. Nothing here reviews,
uses, or assumes `f505faa3`. The repository is still public at this
pass; visibility, collaborators, and permissions are unchanged.

Scope rule for this pass: ScriptableObjects only, plus this doc.
No C# production behaviour change was made. No `.asset` was deleted.
No nine-person fixture was authored. No canon, price, rate, weight,
frequency, duration, cap, or semantics change was made, except for
the display-string normalization in change 3 below (a UI label only;
`buildingId`, footprint, prefab, and suitability are unchanged).
Muse "done/verified" here means branch-pushed plus the Git-level
checks in section 4 — NOT Unity compilation, Test Runner, PlayMode,
or scene wiring, none of which is claimed. `.asset` changes are not
verified by Roslyn (Roslyn cannot load Unity assets); the final
Unity import GUID check remains Codex/Unity AI work.

Worktree note: the first clone attempt in `/tmp` failed because
`/tmp` was full (512M tmpfs, 0 available), which broke checkout.
The worktree was moved to workspace scratch and cloned clean at
the Pass A tip (status 0 dirty files). The scratch worktree is not
part of the delivered branch and is removed after push.

## 1. What was changed — exactly three fixes

### Fix 1 — BlacksmithProfile: dangling `repair_service` removed

- File:
  `Assets/Resources/Core/Economy/BusinessProfiles/BlacksmithProfile.asset`
- Old: `serviceCategoryIds: [repair_service]`
- New: `serviceCategoryIds: []`
- Evidence: the asset defines only two categories, `metal_inputs`
  and `tools_hardware`. `repair_service` appears nowhere else in
  code, assets, or docs (searched all of `Assets/Scripts` and all
  `.asset` files). `SharedBusinessRuntimeManager.IsServiceCategory`
  only compares the list against a category id passed in, and
  category ids passed in come from defined categories, so the
  label could never match. Removing it is behaviour-identical,
  not a semantics change. The blacksmith's actual service output
  is `tools_hardware` (output category), which is untouched.

### Fix 2 — General Store: dangling `freight_goods` removed, both assets

- Files (both edited identically, so the pair stays identical):
  `Assets/Resources/Core/Economy/BusinessProfiles/GeneralStoreProfile.asset`
  and `Assets/Core/Economy/GeneralStoreBusinessDefinition.asset`
- Old (both): `inputCategoryIds: [freight_goods]`
- New (both): `inputCategoryIds: []`
- Evidence: neither asset defines a `freight_goods` category
  (defined: `staple_food`, `meat`, `clothing`, `household_goods`,
  `tools_hardware`, `medicine_remedies`), and `freight_goods`
  appears nowhere else in code, assets, or docs. Weekly operation
  in `SharedBusinessRuntimeManager` looks up
  `GetCategoryStock("freight_goods")`, gets null, and continues,
  so the label never procured or consumed anything. Removing it
  is behaviour-identical. This is NOT a decision that the store's
  inputs should be its six stock categories: putting those six
  into `inputCategoryIds` would change weekly procurement, so it
  was deliberately not done. If Codex wants the store to consume
  inputs through this path, that is a Pass C / binding decision,
  recorded in section 5.
- After this fix, the profile and the definition are still
  identical after stripping `m_Script`, `m_EditorClassIdentifier`,
  and `m_Name` (verified by scripted diff). The dual-authority
  question itself is NOT resolved by this fix — see section 5.

### Fix 3 — Misnamed civic building renamed to LargeLivery

- Old files:
  `Assets/Core/World/Buildings/SingleStoryCivicNaturalWood 1.asset`
  (+ its `.meta`, GUID `c90825624f0f83a48a71fd5920fb6cf8`)
- New files: `Assets/Core/World/Buildings/LargeLivery.asset`
  (+ its `.meta`, same GUID, moved with `git mv`, not regenerated)
- Old `m_Name`: `SingleStoryCivicNaturalWood 1`
- New `m_Name`: `LargeLivery`
- Old `displayName`: `large_livery`
- New `displayName`: `Large Livery`
- Unchanged, deliberately: `buildingId: large_livery`,
  footprint 2x2, storey 1, `primaryUse: 2` (business),
  `workplaceEligible: 0`, suitability rows, and
  `visualPrefab` GUID `8fa3f8d1d3a7846439daaf94aba64e1a`.
- Evidence: the old name said "civic" and " 1" (Unity's duplicate
  suffix), but the content was never a civic duplicate: the base
  `SingleStoryCivicNaturalWood` has `buildingId:
  single_story_civic_natural_wood`, `primaryUse: 3` (civic), and
  suitability type 7, while this asset has `buildingId:
  large_livery`, `primaryUse: 2`, and suitability type 9
  (LiveryFreight) instead of 7. No code loads this asset by path
  or by name (building catalogs are loaded by type search in the
  buildings folder and by `buildingId`), and scene/prefab
  references are by GUID, which is unchanged, so the rename is
  GUID-safe and logic-safe. `displayName` is a UI label only; no
  code or test matches it.
- Flagged, NOT changed: this shell still cannot host a workplace
  (`workplaceEligible: 0`, and every suitability row is 0,
  including type 9 LiveryFreight), despite its id and business
  primary use. Whether a large livery should host livery work is
  a gameplay decision for Pass C / Codex, not a naming fix, so
  it was left as authored and is listed in section 5.

No other `.asset` was edited. No `.meta` was regenerated. No new
`.asset` was created, so no new GUID was needed for assets (the
only new GUID in this pass is this doc's own `.meta`, below).

## 2. What was checked and left alone, and why

- General Store dual authority (profile vs definition): LEFT, by
  instruction. The two assets are byte-identical after stripping
  script/name/class (verified), carry the same
  `startingCashCents: 45000`, reserve 8000c, markup 1.4, site
  requirements, categories, and all 27 items with identical
  prices, stocks, and reorder targets. The bootstrapper uses the
  definition (`storeDefinition` field) while the shared runtime
  loads profiles via `Resources.LoadAll("Core/Economy/BusinessProfiles")`.
  Deleting or overwriting either one would pick a canonical
  asset by stealth, so neither was touched. The question — which
  asset wins, and how the new $250 fixture target and
  player-fund reconciliation apply exactly once — stays open for
  Kennedy/Codex (section 5). `FirstLedger.asset` separately holds
  player starting cash 250000c; that is a third cash figure, in
  the scenario layer, also left alone.
- New fixture targets ($250/$175/$200/$100 businesses;
  $65/$75/$60/$125 households; player funds preserved): NOT in
  any asset and NOT applied here. Profile cash values remain the
  old test parameters (General Store $450, Bakery $150, Fuel
  Dealer $130, LiveryFreight $150, plus all other profiles in
  section 3). Applying fixture targets by editing a profile
  would change every generated world using that profile, so it
  must come as fixture inputs/overrides, not as profile edits.
- SixPersonDevelopmentFixture and its validator: UNTOUCHED.
  Still Kennedy 35, Emma 32, Glen 31, Cassidy Pierce 29, Jacob 34,
  Grace 30; households Baldwin-Ooms, Pierce, Boniecki; ordinals
  0, 1, 2. `Validate()` still requires exactly 6 persons, 3
  households, 2 per household. Tannis, Ethan, Tia, and Shane are
  still absent from fixture data, and no nine-person fixture was
  authored — Kennedy's fixture inputs (ages, Tia's role, Shane's
  house/tenure/cash in cents, freight business name/instance id,
  horses/wagons, starting stock, slice start season/day, freight
  payers, seed rule) are still incomplete.
- Item-ID split: LEFT, documented, not renamed. Store space:
  `flour` (10 lb sack, 35c) / `bread` (loaf, 5c). Bakery profile
  space: `bakery_flour_sack` (sack, 58c) / `bakery_fuel_bundle`
  (bundle, 28c) / `bread_batch` (batch, 36c). Bakery code space:
  `bakery-flour` (lb) / `bakery-notions` / `bake.bread-loaf`.
  Fuel dealer profile: `fuel_wood_bundle` (bundle, 28c) /
  `fuel_offcut_bundle` (bundle, 22c). Restaurant code:
  `restaurant-bread`. The 28c bundle price converges across the
  bakery and fuel-dealer profiles, which is evidence the bundles
  intend the same good, but renaming any profile item id would
  change generic-runtime offers, category stock, and prices, so
  it is a mapping decision for Pass C / the item-library work,
  not a silent asset edit. No production-recipe, equipment,
  merchandise-beyond-definitions, supplier, household-inventory,
  animal/feed, or storage asset exists; recipes live in C#
  catalogs, which this pass does not change.
- Font duplicates (16 files, 8 names x 2 locations): LEFT, and
  they are NOT identical duplicates. The Resources copies are
  the live ones: `LandLedgersTypography.GetFont` loads
  `Resources.Load("Core/UI/Fonts/<name>")` at runtime and
  `AssetDatabase.LoadAssetAtPath("Assets/Resources/Core/UI/Fonts/<name>.asset")`
  in the editor, and the UI Canvas prefab references Resources
  font GUIDs (Bitter Bold, Bitter SemiBold, Source Sans 3 Bold,
  Regular, SemiBold). The `Assets/Core/UI/Fonts` copies have
  different content sizes (e.g. Source Sans 3 Regular 13.6 MB
  under Core vs 24.4 MB under Resources) and are referenced by
  no code path and no prefab in Git. That does NOT prove them
  unused: the Main Scene is PC-local and could reference them.
  They go on the Codex scrap list in section 5 with a
  pre-delete Main Scene GUID search check. They were not deleted
  here.
- " 1" town variant
  (`DefaultTownGenerationSettings 1.asset`): LEFT. It is a
  distinct variant, not a duplicate: seed 2147483647 (vs 1886),
  regional-foundation fields, mineral districts (2 each of
  coal/iron/gold/silver), detail props and forest dressing off,
  debug overlays off, and a `schoolhousePhysicalDefinition`
  field the base asset leaves unset. No code loads it by path
  (code loads the base asset by path) and no code references its
  meta GUID, so its loading, if any, is via a Main Scene
  Inspector reference (PC-local, BLOCKED). It was not renamed
  or deleted.
- Compact variants (`CompactPlaytestTownGenerationSettings`,
  `CompactPlaytestPopulationSettings`): LEFT. Distinct playtest
  variants, not duplicates: town 96x96 (vs 220x200), seed
  919477915, sawmill property off, regional foundation on;
  population 8-10 households / 22-30 residents / arrival rate
  0.05 (vs class defaults 14-22 / 55-95 / 0.28 used by the
  Default asset, which omits those fields). The Default
  population asset also has an empty `m_EditorClassIdentifier`
  (as do the Time, Camera, and Pathing assets); Unity resolves
  by script GUID, so that is a cosmetic inconsistency, not a
  defect, and was not edited.
- Building prefab references: LEFT, not "fixed". Every building
  definition's `visualPrefab` GUID, and 72 of 84 unique GUIDs in
  each town-generation settings asset (crop/food prefabs and
  similar), have no `.meta` in Git. Those are PC-local prefabs
  excluded from the repository-root migration, not invalid
  references in the Unity project. Clearing them would break
  the PC project. Their resolution is BLOCKED from GitHub and
  is a Unity-import / Main Scene check for Codex, not a Git fix.
  By contrast, the Town Hall definition's
  `physicalBuildingDefinition` DOES resolve in Git, to
  `SingleStoryCivicNaturalWood` — verified, not a dangling ref.
- Building suitability rows: LEFT. All suitability
  `businessType` values are valid enum values (0-27, 255).
  Buildings list only a subset of types, but
  `BuildingDefinition.IsSuitableForBusiness` falls back to
  `IsDefaultCommercialProfessionFit` for unlisted types, so a
  missing row is not a defect. Suitability `suitable: 0` rows
  (e.g. Ranch/CropFarm in business shells, all rows in the
  House and Civic shells) are authored exclusions, not errors.
- Calendar tension: LEFT, decision-gated. `FirstLedger.asset`
  starts 1870, `DefaultSimulationTimeSettings` starts 1886, and
  the prior review noted the 1870 start clamping to a
  1880-03-01 clock anchor. Changing either year would be a
  calendar decision, so neither was changed.
- `FirstLedger.asset` founder names: LEFT, deliberately empty.
  `founderDisplayNames: []` with an asset note saying to name
  household founders before bootstrapping is Kennedy's
  in-editor step. Inventing founder names here would violate
  the no-invention rule. Player starting cash 250000c,
  cash-band 25000-150000c, equity goals $5,000/$10,000/$15,000,
  and the "form first business, hire first hand" goals are
  unchanged.
- Pass A pins: UNTOUCHED. Retail WARNING-only cash, bakery
  DayIndex gating (argument ignored, refuses only while the
  adapter's own DayIndex is unset, day equality NOT enforced),
  and dealer fuel partial supply (refused off-map callback
  leaves dealer units already in the bakery store) are all
  behaviour questions for code, not assets, and no asset in
  this pass touches them. No Pass A test was changed.
- `DefaultSimulationTimeSettings` (7-day weeks, 4-week months,
  starting 1886, 60 real seconds per game day at 1x, 15-minute
  short ticks), `DefaultStrategyCameraSettings`, and
  `DefaultPathingSettings` (road 10, walkable 14, rough 18,
  pedestrian 3.2 m/s, diagonal off): read, no discrepancy
  against their backing classes found, left alone. Pathing
  settings reach code via a `PathingManager.Settings` Inspector
  field, so its scene loading is BLOCKED from GitHub.
- Terrain assets (`MainAuthoredTerrainData/Layer/Texture`):
  binary, not opened as text and not edited. Their presence and
  meta GUIDs are intact; content verification is a Unity
  import matter.

## 3. Full asset inventory (all 63)

Backing script GUIDs verified to resolve to a real `.cs.meta`
in Git for every scripted asset. Asset `.meta` GUIDs: no
duplicates across all 63 (checked by script). "Loaded?" means
a Git-visible consumer was found; "BLOCKED" means loading
depends on the PC-local Main Scene / Inspector and cannot be
verified from GitHub. All values below are test parameters or
fixture calibration unless stated otherwise — none is Canon.

### Settings and fixtures (Assets/Core)

- `Core/Camera/DefaultStrategyCameraSettings.asset` —
  `StrategyCameraSettings` (script
  `90a7d0b591fd45e1bbdd47eec2421d6a`). Consumer: editor
  setup utility only, in Git. Left alone.
- `Core/Civic/TownHallDefinition.asset` —
  `TownHallDefinition` (script
  `671a2f7d9c994ab6a2b5965aa8fd3a41`). holding
  `town_hall`, owner Town Council, physical building ref
  resolves to SingleStoryCivicNaturalWood. Consumers:
  CivicFoundationManager, TownWorldController. Left alone.
- `Core/Economy/GeneralStoreBusinessDefinition.asset` —
  `GeneralStoreBusinessDefinition` (script
  `61dc447ef0cb4e8499d4dc59698995b5`). See Fix 2 and the
  dual-authority note. Consumer: bootstrapper
  `storeDefinition` field (Inspector, BLOCKED for scene
  confirmation). Edited only for Fix 2.
- `Core/Pathing/DefaultPathingSettings.asset` —
  `PathingSettings` (script
  `e6202e55c6dd4b8aa14d3b861e8df3db`). Consumer:
  PathingManager Inspector field (BLOCKED). Left alone.
- `Core/Population/CompactPlaytestPopulationSettings.asset` —
  `PopulationGenerationSettings` (script
  `1bc426602f4ab6642bc0c81fd7ecc471`). 8-10 households,
  22-30 residents, arrival 0.05. Loading: BLOCKED (variant
  selection is scene/test side). Left alone.
- `Core/Population/DefaultPopulationGenerationSettings.asset` —
  same script. Omits household/resident/arrival fields, so
  class defaults apply (14-22, 55-95, 0.28). Left alone.
- `Core/Population/SixPersonDevelopmentFixture.asset` —
  `SixPersonDevelopmentFixture` (script
  `9353c1b04e3a6284db175ca0c5f7f467`, asset meta
  `3f2eef30e0ec49449933180f0a75a418`). Six persons,
  Cassidy Pierce included, Tannis absent. Consumer: opt-in
  bootstrapper reference only. UNTOUCHED.
- `Core/Time/DefaultSimulationTimeSettings.asset` —
  `SimulationTimeSettings` (script
  `43aac2e93e9cf2b44955f1981869aaab`). Consumer: editor
  time setup utility, in Git. Left alone (see calendar
  tension in section 2).

### Buildings (Assets/Core/World/Buildings)

All backed by `BuildingDefinition` (script
`48cb8c96d20f4281a4a8ec62941af0a8`). All footprints 2x2.
Catalog loaded by type search in the buildings folder and by
`buildingId`; visualPrefab GUIDs are PC-local (see section 2).

- `DoubleStoryBusinessGreenWood`, `DoubleStoryBusinessNaturalWood`,
  `DoubleStoryBusinessRedWood`: id per name, 2 storeys,
  workplace 1, residential mode 2, capacity 1, zone 1, use 2.
  Suitable: types 0, 1, 2, 5, 8. Left alone.
- `DoubleStorySaloonCornerNaturalWood`: 2 storeys, workplace
  1, mode 2, capacity 1, zone 1, use 2. Suitable: type 8 only
  among listed rows. Left alone.
- `HouseNaturalWood`: 1 storey, workplace 0, mode 1
  (standalone household), capacity 1, zone 2, use 1. Left alone.
- `SingleStoryBusinessGreenWood`, `GreyPlaster`,
  `NaturalWood`, `WhitePlaster`, `WhiteWood`: 1 storey,
  workplace 1, mode 0, capacity 0, zone 1, use 2. Suitable:
  types 0, 1, 2, 5. Left alone.
- `SingleStoryCivicNaturalWood`: 1 storey, workplace 0, mode
  0, capacity 0, zone 1, use 3 (civic). Suitable: none among
  listed rows (types 0-7 all 0). Referenced by Town Hall.
  Left alone.
- `LargeLivery` (renamed from `SingleStoryCivicNaturalWood 1`
  in Fix 3): 1 storey, workplace 0, mode 0, capacity 0, zone 1,
  use 2 (business). Suitability rows all 0 including type 9.
  See Fix 3 and the hosting flag in section 5.

### Town generation (Assets/Core/World)

All backed by `TownGenerationSettings` (script
`d79bbdbc401a4ad9be0110c11e93a167`). Code loads the base
asset by path in tests/editor utility; scene selection of the
other two is BLOCKED.

- `DefaultTownGenerationSettings.asset`: seed 1886, grid
  220x200, main street 150, sawmill property on, detail
  props and forest dressing on, debug overlays on. Left alone.
- `DefaultTownGenerationSettings 1.asset`: seed 2147483647,
  regional foundation and mineral districts on, detail
  props/forest dressing off, debug overlays off. Left alone
  (distinct variant, loading unknown).
- `CompactPlaytestTownGenerationSettings.asset`: seed
  919477915, grid 96x96, main street 40, sawmill property
  off, regional foundation on. Left alone.

### Terrain (Assets/Core/World/PreAuthoredTerrain)

- `MainAuthoredTerrainData.asset`,
  `MainAuthoredTerrainLayer.asset`,
  `MainAuthoredTerrainTexture.asset`: native Unity terrain
  types (no `m_Script` GUID, expected). Binary, not text-audited.
  Meta GUIDs intact. Left alone; Unity import matter.

### Business profiles (Assets/Resources/Core/Economy/BusinessProfiles)

All backed by `BusinessProfileDefinition` (script
`111cf2b7fdeb44baa372e61f8a3aa477`). All loaded at runtime
via `Resources.LoadAll("Core/Economy/BusinessProfiles")`
(shared runtime, acquisition manager, save resolver). Every
profile has exactly one `requiredForOpening: 1` slot except
Mine (2). `buildingId` is empty in every profile — consistent
with formation-before-premises at the profile layer, while
each profile still carries site requirements (see the
definition-vs-generic precedence question in section 5).
Cash/markup are test parameters, not Canon and not the new
fixture targets.

- `BakeryProfile` (type 13): cash 15000c, reserve 1500c,
  markup 1.15. Categories flour (`bakery_flour_sack`, sack,
  58c, stock 18), fuel_wood (`bakery_fuel_bundle`, bundle,
  28c, stock 6), bread (`bread_batch`, batch, 36c, stock 24).
  Worker: Baker 1400c required. Site 5x5, frontage 4.
  Left alone (item-id split, section 2).
- `BarberProfile` (type 16): cash 10000c, markup 1.0.
  Categories barber_consumables, barber_bath_service.
  Worker: Barber 1100c required. Left alone.
- `BlacksmithProfile` (type 1): cash 22000c, markup 1.28.
  Categories metal_inputs, tools_hardware. Worker: Smith
  1800c required. Edited only for Fix 1.
- `BoardingHouseProfile` (type 8): cash 13000c, markup 1.0.
  Categories boarding_lodging, fuel_wood, meat, staple_food.
  Worker: House Keeper 1250c required. Left alone.
- `BuilderProfile` (type 10): cash 15000c, markup 1.0.
  Categories builder_labor, carpentry_service, lumber,
  tools_hardware. Worker: Carpenter 1650c required.
  Left alone.
- `ButcherProfile` (type 2): cash 24000c, markup 1.25.
  Categories livestock_inputs, meat. Worker: Butcher 1600c
  required. Left alone.
- `CropFarmProfile` (type 4): cash 19000c, markup 1.15.
  Categories crop_food, seed_inputs. Worker: Farm Hand
  1300c required. Left alone.
- `DoctorProfile` (type 5): cash 17000c, markup 1.25.
  Categories medical_service, medicine_remedies. Worker:
  Physician 2200c required. Left alone.
- `FuelDealerProfile` (type 11): cash 13000c, reserve 1600c,
  markup 1.12. Categories slabs_offcuts
  (`fuel_offcut_bundle`, bundle, 22c, stock 12), fuel_wood
  (`fuel_wood_bundle`, bundle, 28c, stock 24). Worker: Fuel
  Yard Keeper 1200c required. Site 6x6, frontage 4.
  Left alone (item-id split, section 2).
- `GeneralStoreProfile` (type 0): cash 45000c, reserve 8000c,
  markup 1.4. Six categories, 27 items, identical to the
  definition (see section 2). Edited only for Fix 2.
- `GrainMillProfile` (type 12): cash 16000c, markup 1.12.
  Categories flour, grain. Worker: Miller 1500c required.
  Left alone.
- `LiveryFreightProfile` (type 9): cash 15000c, reserve
  1800c, markup 1.0. Categories livery_feed_upkeep
  (`livery_upkeep_lot`, lot, 135c, stock 18), freight_service
  (`freight_service_visit`, job, 200c, stock 0),
  hauling_capacity (`team_wagon_day`, day, 120c, stock 0),
  wheelwright_repairs (`livery_repair_lot`, lot, 220c,
  stock 2). Workers: Stable Boss 1450c required, Teamster,
  Hostler. Site 7x7, frontage 6. Left alone.
- `LumberYardProfile` (type 7): cash 19000c, markup 1.15.
  Category lumber. Worker: Yard Manager 1450c required.
  Left alone.
- `MineProfile` (type 18): cash 26000c, markup 1.08.
  Categories coal (`coal_ton`), gold_ore, iron_ore,
  mine_support_supplies, silver_ore. Two required slots
  (Mine Foreman and one other required slot — the only
  profile with two). Left alone.
- `RanchProfile` (type 3): cash 21000c, markup 1.15.
  Categories feed_inputs, livestock_inputs. Worker: Ranch
  Hand 1400c required. Left alone.
- `SaloonProfile` (type 15): cash 16000c, markup 1.1.
  Categories fuel_wood, meal_inputs, meal_service. Worker:
  Proprietor 1450c required. Left alone.
- `SawmillProfile` (type 6): cash 22000c, markup 1.15.
  Categories lumber, sawmill_logs, slabs_offcuts,
  standing_timber. Worker: Sawyer 1500c required.
  Left alone.
- `TailorProfile` (type 14): cash 12000c, markup 1.15.
  Categories cloth_notions, clothing, mending_service.
  Worker: Tailor 1250c required. Left alone.
- `WheelwrightProfile` (type 17): cash 13000c, markup 1.15.
  Categories repair_inputs, wheelwright_repairs. Worker:
  Wheelwright 1500c required. Left alone.

Note: types 19-27 and 255 (Tannery, PostOffice, Newspaper,
Restaurant, Hotel, Logging, GrainElevator, Bank, Lawyer,
Generic) have NO profile asset. That matches the prior
review's profile-less business types list and is left for
the author-or-rule-out decision in section 5.

### Construction support (Assets/Resources/Core/Economy/ConstructionSupportNodes)

- `LocalSawmillSupport.asset` —
  `ConstructionSupportNodeDefinition` (script
  `9fb78065283d4ebba87d60cd3777e8c2`). node
  `local_sawmill`, unit cost 85c, starting stock 240,
  standing timber 80, log stock 8, cut/saw capacity 10 logs
  per week each, lumber per log 4, storage 360, active.
  Loaded via Resources construction-support path. Left alone.

### Fonts (Assets/Core/UI/Fonts and Assets/Resources/Core/UI/Fonts)

All backed by TMP `TMP_FontAsset` (script
`71c1514a6bd24e1e882cebbe1904ce04`). Eight roles:
Bitter Regular, Medium, SemiBold, Bold, Black; Source Sans
3 Regular, SemiBold, Bold. Resources copies are live
(runtime `Resources.Load`, editor `AssetDatabase` path, UI
Canvas prefab references for Bold/SemiBold/Source Sans
roles). Core copies are referenced by no code path and no
prefab in Git — see the scrap list in section 5. All 16
left alone, meta GUIDs distinct.

### Scenario (Assets/Scripts/Orchestration/Scenarios/FirstLedger)

- `FirstLedger.asset` — `ScenarioAsset` (script
  `5b1df927f0fe407ba45810966afb291c`). scenario
  `first-ledger`, display "First Ledger", start 1870-01-01,
  55-95 residents (preferred 78), player starting cash
  250000c, cash band 25000-150000c, equity goals
  $5,000/$10,000/$15,000, founder names empty (Kennedy's
  in-editor step). Consumers: scenario director/service,
  preset library, validation harness. Left alone.

## 4. Verification (Git-level only — no Unity, no Roslyn run)

No production `.cs` file was changed in this pass, so no
Roslyn suite is affected by this diff and none was re-run
here; re-running the Pass A suites on a diff that touches
only `.asset` text would prove nothing, because Roslyn
cannot load Unity assets. The Pass A regression baseline
(Series 2 74 + Pass A 8, Phase H 12 passed / 3 failed with
the exact baseline failures) is therefore carried forward
unchanged, not re-claimed here. Pass C must re-run it.

Checks actually run in this pass (scripted, against the
worktree before commit):

- All 63 `.asset` files have a `.meta`; asset `.meta`
  GUIDs have zero duplicates.
- Every scripted asset's `m_Script` GUID resolves to a
  real `.cs.meta` in Git (12 distinct script GUIDs, one
  per backing type, shared correctly across assets of
  that type).
- Dangling-category check across all 20 profile/definition
  assets (owned/input/output/service lists vs defined
  categories): after Fixes 1-2, zero dangling entries.
- Profile-vs-definition identity: still identical after
  stripping script/name/class, after Fix 2 was applied
  to both files.
- LargeLivery: `.meta` GUID unchanged by the rename
  (`c90825624f0f83a48a71fd5920fb6cf8`), `buildingId`
  unchanged (`large_livery`), prefab GUID unchanged.
- New GUID check: this doc's `.meta` GUID
  `e8005b4decba4cf3946061115ba20561` was freshly
  generated with `python3 -c "import uuid;
  print(uuid.uuid4().hex)"`, never copied, and appears
  exactly once in the tracked tree at commit time
  (verified by `git grep` before commit).
- NOT verified, explicitly: Unity import (final GUID
  check), Unity Test Runner, PlayMode, Main Scene wiring,
  prefab wiring, and whether the Core font copies or the
  " 1" town variant are referenced from the PC-local
  Main Scene. Those are Codex/Unity AI work.

## 5. What still needs doing (in pass order)

- Pass C (scripts): the code-side counterparts of this
  pass's findings — item/SKU mapping (do not rename profile
  ids without a mapping decision), General Store weekly
  input path (whether `inputCategoryIds` should list stock
  categories, or stay empty now that the dangling label is
  gone), and the Pass A / review-2 decision-gated script
  work (horse feed source once Kennedy chooses it, driver
  binding support). A Pass C change that alters
  WARNING-only, DayIndex, or partial-supply behaviour must
  update Pass A tests explicitly and say why, never silently.
- Codex / Unity AI: Unity import of this branch (final GUID
  check, including the LargeLivery rename — references are
  GUID-based, but import must confirm no path-based break);
  Main Scene GUID search for the scrap list below before
  any deletion; prefab resolution for building visualPrefab
  GUIDs and town-generation prefab GUIDs; five-tab UI and
  Sketchbook mockup work (unchanged by this pass).
- Codex scrap list (DO NOT delete in Git on this evidence
  alone — each needs a Main Scene/prefab GUID search first,
  because Git cannot prove non-use):
  1. `Assets/Core/UI/Fonts/*` (8 files): no Git-visible
     consumer; Resources copies are live. Search Main Scene
     for each Core meta GUID (Bitter Black
     `ed0bb9ea2acf26d41af1965a4caa5976`, Bold
     `5c9a9c01b2801fa4b8f829a6cc0bd532`, Medium
     `51707ba0cca44ca4fbe91f1395cb10d8`, Regular
     `dc992076575d57242a4cfe4d41c245fb`, SemiBold
     `ad6620c77486ded4598057be8946c7d9`, Source Sans 3
     Bold `45c09d8d340e4524abd3bce5fac75ae6`, Regular
     `dbd2e12b72c8c6749b0f7f50bcb689ca`, SemiBold
     `5c18b3cf33124d84bbe8f25c9f268087`) before deleting
     any; delete `.asset` plus `.meta` together only if the
     search is empty.
  2. `Assets/Core/World/DefaultTownGenerationSettings 1.asset`:
     distinct variant, no Git-visible loader; confirm no
     Main Scene Inspector reference before renaming or
     deleting it. If kept, it needs a distinguishing name
     (it is not "Default" plus a duplicate).
  3. General Store canonical asset (profile vs definition):
     still unresolved. Do not delete either until Kennedy /
     Codex records which one wins and how fixture targets
     and player-fund reconciliation apply exactly once.
- Kennedy decisions still open (unchanged by this pass):
  nine-person fixture inputs; General Store canonical asset;
  item-level stock migration; item/SKU mapping and flour /
  fuel unit semantics as canon; LargeLivery hosting
  (should a `large_livery` shell host livery work, given
  `workplaceEligible: 0` and all suitability rows 0?);
  profile-less business types 19-27 and 255 (author or
  explicitly rule out); calendar start year (1870 scenario
  vs 1886 time settings vs 1880 anchor); horse feed source;
  freight payer per leg and commission; seconds granularity;
  and all prices/rates/weights/frequencies as test
  parameters only.
- Later passes: vertical slice passes, second scripts pass,
  second ScriptableObjects pass, and final verification
  cleanup pass, then pull. The final pull target is the
  final cleanup branch, not this branch.
