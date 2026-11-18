# Physical business interiors — Codex Pass 4

## Status

**Portable source foundation compiled and focused EditMode tests passed. The owner's local development-town configuration now contains 13 explicit fixture records, but the live PlayMode visual and interaction acceptance checks are blocked and unverified.** The local configuration and local-only prop references are intentionally excluded from the published checkpoint.

## Findings

The complete `Assets/Environment/Props` audit covered 95 prefab assets (62 tracked, 33 local-only). No audited prefab carries a Land & Ledgers gameplay behaviour; the props are physical models and do not self-register as storage, workstations, service counters or inventory. Crates, barrels and package meshes do not imply contents or saleable stock.

Useful real models include the local-only owner-adjusted `Shelf.prefab`, `Item Shelf.prefab`, `Stove.prefab` and `Wooden Kitchen Counter.prefab`, plus tracked barrels and crates. The tracked `Cooking Stove.prefab` and `Kitchen Counter.prefab` are cube geometry and were rejected as storefront substitutes. The available name search found no dedicated woodworking bench/tools, commercial bakery oven, sawmill machine or lumber-handling machine. This is a library limitation, not a reason to pretend a visual prop is operating equipment.

The live New Game survey found business/premise mismatches that block furnishing some target operations safely:

| Operation | Runtime observation | Result |
|---|---|---|
| General Store | `general_store_000`, Building 0 | Assigned business premises. |
| Bakery | `bakery_001`, Building 1 | Assigned premises; runtime equipment already includes oven chamber, kneading table, proofing rack and bake peels. |
| Fuel Dealer | `fuel_dealer_002`, Building 2 | Assigned business premises. |
| Freight | `livery_freight_003`, no assigned building | No storefront is required by this pass. |
| Sawmill | sawmill business, no assigned building; runtime equipment exists | No authorized premise was available for physical placement. |
| Lumberyard | `lumber_yard_005`, Building 3 | Assigned business premises. |
| Carpentry workshop | `biz_Generic_006`, Building 13 | Runtime had bound the owner's residential home as workshop premises. No commercial fixtures were placed there. |

Buildings 0–3 share the authored White Wood storefront shell and geometry-derived interior floor. This confirms actual business premises exist for the four operations above, but does not prove a usable worker/customer route or that a fixture fits without obstruction.

## Portable source implementation

- `DevelopmentTownConfiguration.BusinessOpeningConfiguration.interiorFixtures` defines stable fixture identity, kind, prefab, building-local transform/scale/pivot/floor, optional obstruction footprint and optional typed approach point.
- `DevelopmentBusinessInteriorInitializer` applies only explicitly authored finite opening fixtures. It registers an `EquipmentAsset` with the owning business through `BusinessEquipmentRegister`, refuses duplicate identities and wrong ownership/kind on restore, checks a live assigned building and walkable floor point, and instantiates the physical prefab under its `BuildingFootprintAuthority`.
- `BusinessInteriorFixturePresentation` stores presentation identity and unregisters only its own optional runtime approach point on destruction.
- `FirstLedgerSliceBootstrapper` invokes the initializer for New Game and rebuilds presentations from existing equipment identities on Continue. Continue does not mint a missing asset.
- The code does not add merchandise stock, business cash, workstation capability, storage capacity or a service transaction. An approach marker only creates a discoverable physical activity location; the existing work/service authorities still have to bind and authorize the activity.
- The live survey found that configured `general-store`, `fuel-dealer` and `lumber-yard` operation IDs did not resolve runtime profiles named `general_store`, `fuel_dealer` and `lumber_yard`. The resolver now treats punctuation differences as equivalent while retaining exact normalized identity matching and ambiguity refusal. The Bakery already matched and is the only business for which this run created presentations.

## Local authoring performed

Using Unity's `AssetDatabase`/Editor APIs (not raw YAML), the owner-local `Assets/Core/Population/DevelopmentTownConfiguration.asset` was saved with 13 fixture records: General Store 4, Bakery 3, Fuel Dealer 3 and Lumberyard 3. Unity read-back validated the asset and reported 14 people, 8 households, 7 businesses, asset GUID `f5820dc040b04928a91079ae0c36b461`. The authored config references local-only shelf, stove and counter props alongside tracked empty crates/barrels. These records do not create merchandise stock. The configuration was not staged because it already contains owner edits and its prefab dependencies are local-only; the original asset is preserved locally.

The local fixture positions and approach points have **not** been visually inspected or confirmed clear. The configured bakery stove visual is not evidence that the bakery runtime's actual oven model has a matching physical presentation. The equipment register / workstation / service binding must be checked before treating any fixture as functional.

## Verification

- Unity `6000.4.2f1` Editor source compilation: passed; `compilationFailed=false`, no compiler errors before the live run.
- `DevelopmentBusinessInteriorInitializerTests`: 2 passed, 0 failed.
- `DevelopmentTownConfigurationTests`: 6 passed, 0 failed.
- Unity Editor PlayMode status before test: stopped (Edit Mode).
- A bounded New Game PlayMode attempt was started after authoring. The Editor then became unresponsive during domain reload/asset scan; the Unity Pipeline connection stopped. `Editor.log` ended in the asset import/revision phase and reported `Failed to load 'F:/Land & Ledgers/Assets/Environment/Buildings/Building Animations/Single Story Business Building White Plaster Animation 1.anim'. File may be corrupted or was serialized with a newer version of Unity.` No recovery restart was attempted.
- The Editor recovered without a restart and the actual PlayMode survey confirmed `manager=True`, `hub=True`, `world=True`, 14 buildings and all seven configured runtime business identities. Before the resolver correction, only the Bakery's two new fixture presentations appeared (its pre-existing oven asset plus the configured work surface/crate produced six registered equipment assets total). General Store, Fuel Dealer and Lumberyard registers remained empty, confirming the separator-mismatch defect. The sawmill had no assigned building, and the carpentry business was assigned to the owner's home (Building 13); neither received furnishings. Freight had no building, as expected.
- After fixing profile matching, Unity compiled the new code successfully (`Tundra build success`, 56.48 seconds, `compilationFailed=false`, zero errors). The Editor then began an assembly reload and the Pipeline disconnected again. The five new profile-normalization regression cases could not be rerun after the fix. No second PlayMode was launched.
- No three-interior screenshots or customer/worker approach test were obtained. The pre-change distant town view is not acceptance evidence. The last confirmed Editor state before the final code reload was Edit Mode; the Editor later became nonresponsive during reload, so its final mode could not be queried.
- `git diff --check` passed for the reviewed source changes.

## Follow-up requirements

1. When Unity is responsive, re-run `DevelopmentBusinessInteriorInitializerTests`, confirm all four configured businesses now resolve, and inspect the `.anim` import warning before any restart; do not overwrite the owner asset.
2. Inspect the four authored premises and all 13 fixture transforms in Scene/Game views; adjust only through authoring data, preserving the owner's building/prefab changes.
3. Verify physical path reachability to the service counter and relevant work points, then connect those points only to existing service/workstation authorities.
4. Assign a legitimate commercial workshop premise before placing carpentry equipment. Resolve sawmill premises separately; do not use a residential home or an unassigned shell as if it were authorized.
5. Add approved physical models for dedicated bakery and sawmill/woodworking machinery if the owner wants those operations visibly represented; current runtime equipment records do not make the available household props equivalent machinery.

## Files in the portable checkpoint

- `Assets/Scripts/Domains/Population/DevelopmentTownConfiguration.cs`
- `Assets/Scripts/Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs`
- `Assets/Scripts/Orchestration/Bootstrap/DevelopmentBusinessInteriorInitializer.cs` and `.meta`
- `Assets/Scripts/Orchestration/Bootstrap/BusinessInteriorFixturePresentation.cs` and `.meta`
- `Assets/Scripts/Tests/Editor/Economy/DevelopmentBusinessInteriorInitializerTests.cs` and `.meta`
- This report.

The owner's Main Scene, local configuration asset, local prop prefabs, asset packs, terrain/font changes and generated audit files are not part of the portable checkpoint.
