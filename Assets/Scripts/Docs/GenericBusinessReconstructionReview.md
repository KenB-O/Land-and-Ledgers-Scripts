# Generic Business Reconstruction Review and Unity AI Handoff

Pass 1 of the new series. Base: `integration/pass12-unity-readiness` at
`273b11312b80d38d59fadc93c72f9aebb3295758` (the migrated repository
root: `Assets/Scripts`, `Assets/Core`, `Assets/Resources`).
Branch for this pass: `muse/generic-business-reconstruction` (SHA and
push confirmation are in the pass report, not in this file, because
they change at push time).

Read this file together with the governing docs: Documentation v3.0,
Canonical Project Bible Revision XXXI and Technical Implementation
Bible Revision XIV, both dated 2026-10-07. Proposals 001-105 are
approved; 106-145 are pending and were NOT promoted here.

## 0. What this pass is, and what it is not

This pass is source-level generic-business reconstruction ONLY. It does
NOT start the later passes (task system implementation, economy
ordering UI, player depth, town growth). It does NOT claim Unity
compilation, Unity Test Runner, PlayMode, scene wiring, prefab
wiring, or Inspector values: no Unity run happened in this pass, and
none is claimed. Everything verified here was verified in a small
Roslyn harness (see section 13). Unity AI owns first-compilation
busy work (console errors, namespaces, random Unity issues). Codex
owns hard problem solving and final integration. Neither prompt is
written here; ChatGPT (manager) writes those from the parent's
summary of this pass.

The governing principle throughout: a business's permitted operations
derive from its real capabilities, resources, rights, and activities,
NOT from an arbitrary BusinessType label. One business keeps one
legal/economic principal, one ledger, and one owner, while tracking
distinct operations as capabilities.

## 1. Baseline reconciliation: integration 273b113 vs old phase-10 branch

Method: compared the old phase-10 branch tip
(`muse/passxl-slice-phase10` at `6f5893b`) against integration
`273b113` file by file. Result: `273b113` is a direct child of
`6f5893b`. All 3,499 old phase-10 Scripts files are present in the
new base under `Assets/Scripts/`, and all 108 files changed between
the old integration checkpoint and phase 10 match phase-10 content
in the new base. Slice work is therefore PRESENT in the new base,
not missing, plus the new `Assets/Core` and `Assets/Resources`
assets (including `GeneralStoreBusinessDefinition.asset`,
`SixPersonDevelopmentFixture.asset`, building definitions, a wagon
prefab, UI canvas, fonts, and music).

Present in 273b113 (verified by file presence and content match):
- Pass XL phases 1-10 code: player-as-NPC flag, off-map named-SKU
  flour procurement with standalone lot ledger, slice save section,
  family-tree builder, immigration conditions, recruitment
  conversation, supplier catalog, through-traveler evaluation,
  arrival settlement and driver, town reputation model,
  outmigration, six-person composed runs (store, work-and-pay,
  three-business), phase review docs (Docs/PassXlPhase4Findings.md,
  PassXlPhase5Review.md, PassXlPhase6SliceRun.md,
  PassXlPhase7WorkAndPay.md, PassXlPhase8ThreeBusiness.md,
  PassXlPhase9Review.md, PassXlPhase10CodexPrep.md).
- Six-person slice gate: `SuppressMigrantArrivals` /
  `AllowNewArrivals` still suppress arrivals for the slice. Nothing
  in this pass turns immigration, traveler arrivals, or outmigration
  on for the slice.
- Generic systems this pass builds on: `GenericBusinessEconomy.cs`
  (ProductionMethodDefinition, GenericBusinessConfiguration,
  GenericRetailConfiguration, ProductionStageMachine),
  `GenericVerticalCatalog.cs`, `GenericProductionExecution.cs`,
  `GenericProductionRuntime.cs`, `GenericEquipmentProcurementAuthority.cs`,
  `BusinessCapabilities.cs` (capability registry, AddOperation),
  `RetailServiceDesk.cs`.

Missing from the new base (left out of the migration commit, NOT
invented here):
- Main Scene, Packages, ProjectSettings, environment assets, and any
  untracked local files from the PC project. If a file you expect
  (a scene, a prefab, a ProjectSettings value) is not under
  `Assets/Scripts`, `Assets/Core`, or `Assets/Resources` in this
  branch, it is missing from this base and must be supplied from
  the PC project by Codex, not reconstructed from memory.
- Some `Assets/Core` / `Assets/Resources` references may point to
  those excluded assets. That is a migration caveat, not a finding
  of this pass.

Do NOT re-apply old Scripts-only patches: there is nothing to
re-apply. The old paths no longer describe the repo; every file
reference in this pass uses the new `Assets/Scripts/...` paths.

## 2. Preservation and source review (section 1)

Legitimate authorities (do not rewrite):
- `HouseholdLedger`: the ONE cash writer.
- `FinancialObligationAuthority`: the ONE debt authority.
- `ExternalMarketAuthority` + `OffMapNamedSkuProcurementService`:
  off-map procurement with named lots and FIFO sale.
- `TaskAuthority`: the single task authority (see the task-system
  audits; this pass does not change it).
- `ProductionMethodDefinition` / `ProductionStageMachine`: the ONE
  recipe/production authority. This pass EXTENDS it additively
  (section 7); it does not create a second recipe authority.
- `BusinessCapabilityRegistry` + `BusinessExpansion.TryAddOperation`:
  capability registration (never a permission grant) and adding an
  operation to the same business/ledger.
- `GenericEquipmentProcurementAuthority`: equipment acquisition
  lifecycle (Quoted to Operational), recording state only after
  the underlying authority succeeds.
- `BusinessCreationAuthority` / `CreateBusinessIntent`: formation
  workflow (BIZ-1). The deprecated passive-seed path behind
  `useLegacyPassiveSeed` is obsolete and stays off by default.

Adapters (keep until their callers migrate):
- `StoreProcurementChain` alongside
  `OffMapNamedSkuProcurementService`: two procurement authorities
  with no shared double-ordering guard (open decision F26, carried
  forward, NOT resolved here).
- `LogisticsRuntimeManager` timer-driven shipments alongside the
  task-based `FreightShipmentService` / `FreightProductionDriver`
  (open decision D1 / P9-14, carried forward).
- `BusinessProfileDefinition.CreateFallback`: code fallback profile
  for business types without an authored ScriptableObject. Keep
  until all business types have authored profiles.

Duplicate responsibility (documented, not deleted in this pass):
- Bakery has TWO production models: the staged chain
  (`BakeryStagedProductionChain`, nine steps) and the shop runtime
  (`BakeryShopRuntime`, 840-minute daily labour pool). Picking ONE
  model is a Kennedy decision carried forward; this pass adds
  recipes to the shared method system, which either model can use,
  but does not pick the winner.
- The shared-runtime constants in section 12 are the throughput
  example of the same pattern: legacy values still read by legacy
  callers, replacement path added here, deletion deferred to a
  migration pass.

## 3. Generic business architecture (section 2)

New plain-C# core (dependency-free, System only, under
`Assets/Scripts/Domains/Economy/GenericBusiness/`):
- `BusinessCapabilityKind.cs`: player-facing kinds (Sell Goods, Make
  Products, Offer Services, Transport Goods or People, Provide
  Lodging, Perform Construction) with display names and suggested
  registry ids. Kinds are labels, never permission grants.
- `GenericBusinessCapabilitySet.cs`: one business's enabled kinds,
  retail product ids, enabled production method ids, and enabled
  service ids, plus `CheckProductionFeasibility` (equipment,
  workspace, premises right, authority, on-hand inputs, qualified
  person). Adding a capability never duplicates ownership, assets,
  money, or accounting: the business identity and ledger are
  untouched by this class.
- `GenericBusinessUnityAdapter.cs`: the mapping from every plain
  record in this pass onto the existing Unity system, class by
  class, field by field, including which fields are ADDITIVE and
  which mappings lose precision (legacy whole-minute phases vs
  seconds-native recipes). Unity AI should implement exactly this
  mapping.

Worked example from the prompt: Ken's Emporium starts as retail
(sell meat and clothing), then `TryEnableService("barber-haircut")`
adds barber services to the SAME capability set (tested: retail
kind appears once, service list has one entry, second enable is a
no-op). It cannot actually cut hair until feasibility passes:
barber chair, period-appropriate cutting tools, barber-shop
workspace, sufficient usable space, and a qualified person with
the barbering skill. Later it may enable a food-production method
the same way, once it has premises, equipment, people, materials,
and legal authority.

## 4. Business templates (section 3)

`GenericBusinessTemplates.cs`: General Store, Bakery, Fuel Dealer,
Barber. Each template suggests a catalog, equipment, production
methods, workspaces, staffing roles (suggestions only), and a
procurement policy. Applying a template creates NO cash, inventory,
equipment, or workers: `Apply` returns an `AppliedTemplate` listing
what to acquire and what to enable. Staffing suggestions are never
hiring targets; hiring need comes from task backlog (see the
task-system audits). Template data is authoring data, not canon.

## 5. Adding capabilities to existing businesses (section 4)

`GenericBusinessCapabilitySet` provides the Add to Business
operation:
- `TryAddCapability(kind)`: idempotent, returns false only for
  Unspecified.
- `TryExpandRetailCatalog(productIds)`: expands the catalog in
  place; existing ids are kept, new ids are added, and no second
  retail operation is created. Expanding with no new products
  does NOT enable retail (fixed during testing: an empty expansion
  previously enabled the Sell Goods kind, which wrongly gave the
  barber template a retail capability).
- `TryEnableProductionMethod(methodId)` and
  `TryEnableService(serviceId)`: idempotent, one entry per id.
- `CheckProductionFeasibility(recipe, input, reasons)`: every
  requirement must be met by caller-supplied real state, or the
  method is refused with named reasons.

Unity AI API surface: bind the UI's "Add to Business" action to
these methods (via the adapter mapping onto
`BusinessInstanceState.AddCapability` and
`GenericBusinessConfiguration.AddProductionMethod` /
`GenericRetailConfiguration.AddOrReplaceLine`). The UI must show
the feasibility reasons when a capability is enabled but not yet
performable.

## 6. SharedBusinessRuntimeManager audit (section 5)

Full table with classifications is in code:
`GenericBusinessUnityAdapter.cs` is the mapping;
`BusinessInspectorFields.cs` (`BusinessInspectorFields.Audit()`)
is the audit result. Summary:

- `useLegacyPassiveSeed` (default false, deprecated): (5)
  obsolete development artifact. Keep for backward compatibility
  until legacy callers migrate, then remove in a migration pass.
  A business becomes operating only through recorded commerce.
- `minimumHireLaborAccess` (default YoungWorker): (3) legal /
  historical rule. KEEP. A floor on who may be hired, not a
  hiring target and not a quota.
- `MineState` (deposit, stage, safety, last cut, richness,
  confidence, camp pressure; returns mineState only for Mine
  businesses, else null): (2) specialized capability. KEEP the
  support, HIDE the clutter: conditional Inspector grouping so
  mining state shows only for Mine businesses, or a
  capability-specific component in a Unity pass. Non-mine
  businesses already see null, so the data is not duplicated;
  the clutter is presentation only.
- Staffing readouts (`FilledWorkerCount / TargetWorkerCount`):
  (6) duplicated authority as a decision input. Display of slot
  fill only, never a hiring target. Hiring need comes from task
  backlog.
- `baselineWeeklyThroughputUnits` (default 12),
  `baselineDailyServiceCapacity` (default 0) on
  `BusinessProfileDefinition`: (4) scenario configuration /
  template calibration, not a hard cap. See section 12.
- `AllowedPlotZones` / `allowedPlotZones` (default Business,
  MixedUse) with `Meets` as a hard gate: (5) obsolete
  development artifact as a GATE. The serialized field stays
  (existing assets reference it); new callers must use
  `SiteEligibility.Evaluate` (section 10). Note the legacy gate
  already softens MixedUse plots (a MixedUse plot matches any
  recommendation) and skips Agricultural zones in the comparison;
  the remaining hard-gate problem is a non-MixedUse,
  non-matching zone returning false.
- Output-per-input and outlet maxima constants: (6) duplicated
  authority, legacy calibration. See section 12.
- `operatingLedger` / operating-only-through-recorded-commerce:
  (1) legitimate universal business concern. KEEP.
- `workerSlots` / RequiredForOpening gating: (4) scenario
  configuration. Re-express in task terms in a later pass
  (opening tasks with no worker, not a slot count). A bare
  minimum (a store cannot open with nobody to tend it) is
  defensible, but it is not a hiring target.

Nothing was deleted and no serialized field was renamed in this
audit. Deletions are migration tasks for Codex, after callers
move.

## 7. Starting stock and business formation (section 6)

`GenericBusinessFormation.cs` keeps the two cases apart, exactly
as the owner directed:
- NEW business: starts at zero inventory and zero cash, then
  applies ONLY caller-supplied legitimate contributions. A
  contribution with no source and no provenance is REFUSED, not
  silently dropped: that is the magical-inventory path this gate
  exists to block.
- EXISTING scenario business: may begin with documented starting
  assets, but only as a scenario endowment: endowment id,
  scenario id, and provenance required, and every item needs an
  item id, a lot/asset id, provenance, and a positive quantity.
  Undocumented items are refused.

This is NOT the old six-person slice "zero starting capital" rule
(rejected by Kennedy as canon, Nov 1, 2026). Slice seeds are
configurable test inputs / scenario endowments, variable between
runs. This formation gate applies to new formation in general;
scenario endowments remain the documented exception, never a rule
that every business starts at zero in every scenario, and never
canon. No test seed amount in this pass is canon.

## 8. Production recipes (section 7)

One recipe authority: `ProductionMethodDefinition`. This pass
adds ADDITIVE fields only (no renames, no removals) in
`Domains/Economy/Generic/GenericBusinessEconomy.cs`:
- `ByproductOutputs` (list of product requirements)
- `FuelOrEnergyInputs` (list of product requirements)
- `InputSubstitutions` (list of
  `ProductionInputSubstitutionDefinition`: original product,
  alternative products, alternative quantity)
- `RequiredFloorAreaSqM` (float, 0 = no floor-area requirement
  recorded, NOT "zero space")
- `WasteFraction01` (float, 0 to 1, 0 = no waste recorded)
- `FeasibilityConditions` (list of named conditions)

A legacy method that leaves every new field at its default
behaves exactly as before: one output, no byproducts, no
fuel/energy requirement, no substitutions, no floor-area gate,
zero waste, no extra conditions. That is tested in the Unity-only
integration tests (section 13).

`ProductionRecipeDetail.cs` is the plain-C# view of a method's
recipe: inputs and quantities, outputs and quantities, byproducts,
fuel/energy, substitutions, equipment, workspace, floor area,
waste fraction, skill, feasibility conditions, and phases
(duration in game SECONDS, RequiresPerson flag). Bakery presets
(`BakeryRecipePresets`) give three distinct methods from the
existing `BakeryBreadCatalog` specs (bread 12 lb flour to 24
loaves, rolls 6 lb to 48 rolls, pie 4 lb to 12 pies, notions per
batch, fuel per batch): the catalog's numbers are calibration
(Canon Part XV in the catalog's own comments), NOT canon, and are
repeated here only so the three products share one recipe system
instead of a single hard-coded bread product. The owner enables
whichever methods they want; enabling a method does not create
its inputs.

Category-level reporting is preserved: the profile's
`inputCategoryIds` / `outputCategoryIds` / `serviceCategoryIds`
are untouched. The recipe layer adds product-level specificity
underneath them.

Precision warning for Unity AI: legacy `ProductionPhaseDefinition`
uses whole minutes (`ElapsedMinutes`); recipe phases use game
seconds. Converting seconds to legacy minutes is
`ceil(seconds / 60)` and is lossy for sub-minute phases. Prefer
the seconds-native `ProductionStageMachine` path (which already
uses `DurationGameSeconds`) for sub-minute phases. The seconds
value in the recipe is precise; the legacy minute value is the
rounded one.

## 9. Physical floor-space capacity (section 8)

`PremisesCapacity.cs`: scalable, per-floor, caller-data driven.
- `BuildingFloor`: floor index, gross area (sqm), usable
  fraction, use. Usable area = gross x usable fraction.
- `SpaceReservation`: id, activity, floor, area (sqm),
  equipment id. Reserving space that exceeds free usable space
  on that floor is refused with a named reason. A 10 m x 20 m
  building is 200 sqm gross per floor; hundreds of chairs at, say,
  1 sqm each plus clearance simply do not fit, by arithmetic, not
  by an invented chair limit.
- Access/egress reserve and storage reserve are deducted from
  free space on floor 0 (ground floor) when supplied.
- Incompatible activities are caller-declared pairs only. With
  no declared pairs, activities are compatible: mixed use is
  allowed by default, and only real conflicts block.
- There is NO invented universal occupancy limit anywhere in
  this model. If no data is supplied for a factor, that factor
  imposes no constraint (it is unmodelled, not zero), and the
  model says so in its reasons instead of inventing a limit.

Units warning for Unity AI: this model works in SQUARE METRES
throughout. Existing premises code uses square feet in places
(`PremisesRequirement.MinimumAreaSqFt`). Convert (1 sqm =
10.7639 sqft) when mapping; do not mix units. `BuildingDefinition`
today has NO floor-by-floor usable area, equipment footprint,
workstation clearance, access/egress reserve, or storage area
fields (it has `storeyCount`, footprint cells, and
`primaryUse`). Unity AI must ADD those capacity fields to the
building/prefab authoring data (additive only) and map footprint
cells to sqm via the building's real dimensions.

Barber use case, verified in tests: chair (5 sqm in the test, a
test parameter) plus period-appropriate cutting tools plus
sufficient usable space plus a qualified person with the
barbering skill passes feasibility; any one missing refuses with
a named reason. No modern electric equipment is introduced: the
barber template's equipment ids are `barber-chair` and
`barber-cutting-tools` (hand tools), and its procurement policy
states period-appropriate tools only.

## 10. Acquisition and business formation (section 9)

`AcquisitionInterfaceModel.cs`:
- Tabs: `StartABusiness` (selected by default) and
  `PreExistingBusinesses`. There is NO Land tab in this
  interface, by design, because the owner asked for it removed
  from THIS interface.
- Land acquisition is NOT deleted: `LandAvailableViaPropertyParcelWorkflow`
  is true. Land stays accessible through the Property/Parcel
  workflow, which is a separate path. The property-rights system
  is untouched by this pass.
- `StartBusinessForm.Validate()`: a form with no premises right
  is refused, because a business must not operate from space it
  has no right to use. Valid rights: Owned, Leased, Rented,
  Shared, Mobile, or another valid arrangement. Starting a
  business does NOT universally require owning land.
- `StartBusinessForm.TryCreate(...)`: converts a valid form to a
  formation record (zero inventory, zero cash, template and
  capability choices applied as configuration, never as grants)
  and refuses an invalid form with named reasons.

Unity AI: bind the two tabs to this model, default to Start a
Business, and convert the form to a `CreateBusinessIntent` only
after `Validate()` passes (see the adapter mapping onto
`CreateBusinessFormModel` / `CreateBusinessPresenter`).

## 11. Plot-zone restrictions (section 10)

`SiteEligibility.cs` replaces the zone hard gate:
- Zone mismatch is a WARNING, never a refusal. A MixedUse plot
  matches any recommendation (matching the legacy gate's own
  MixedUse-plot behaviour); the reverse is not true (a
  recommendation that includes MixedUse does not make a
  Residential or Agricultural plot match; this was fixed during
  testing).
- Refusals are real constraints only, all caller-supplied:
  no premises right, unsatisfied lease terms, no access, physical
  unsuitability, unacceptable nuisance, unsatisfied safety,
  unsatisfied historically applicable legal requirements,
  unavailable infrastructure, and a missing required
  agricultural site role (a physical/legal constraint,
  preserved, not a zoning label).
- `ZoneAloneBlocksActivity()` returns false, by design.

This is NOT permission to violate historically meaningful
property or legal restrictions: those remain refusals above.

## 12. Procurement and equipment (section 11)

`EquipmentPurchase.cs`: a purchase path for physical equipment
and assets, built on the existing procurement pattern
(quote -> order -> pay -> deliver -> receive) without inventing
vendors, prices, or assets.
- `EquipmentPurchaseLine`: asset kind, asset id, vendor id and
  name, price (cents), freight (cents), freight option, delivery
  location, stage.
- `ValidateLine`: refuses a line with no vendor, no asset id, or
  a negative price/freight. Prices are caller-supplied vendor
  quotes; this class never invents a price.
- `TryAdvance`: forward only, one stage at a time, mirroring
  `GenericEquipmentProcurementAuthority`'s lifecycle (Quoted ->
  Ordered -> PaidOrFinanced -> Shipped -> Received -> Installed ->
  Operational). Reaching Operational records the stage; it does
  not perform ownership registration.
- `SameAsset`: horse and wagon are distinct owned assets even
  when operated together. Two lines are the same asset only when
  their asset ids match exactly.
- `SortByDeliveredTotal`: quotes sorted by price + freight,
  cheapest first (the `OffMapSupplierCatalog.QuoteAll` comparison
  rule, reused for equipment). Choosing a quote never pays for it.

What this class does NOT do, and Unity AI / Codex must wire:
ordering, payment, delivery, and ownership registration must go
through the existing authorities
(`OffMapNamedSkuProcurementService` / `ExternalMarketAuthority`
for the purchase, freight/transport authorities for delivery, and
the existing equipment/asset authorities for ownership). This
record never pays, delivers, or registers ownership by itself,
and never spawns a free asset because a capability was selected.
No vendor or supplier data is invented here: vendor ids and
prices in tests are test parameters only.

## 13. Throughput limits (section 12)

`ThroughputCapacity.cs`: capacity from real constraints only.
`Compute` returns the maximum whole outputs and the binding
constraint, where the binding constraint is the minimum of:
materials on hand / material per output, worker hours x outputs
per worker-hour, equipment hours x outputs per equipment-hour x
equipment condition, fuel/energy on hand / fuel per output,
transport capacity, and storage capacity. There are NO constants
for "units per week" or "logs per week" in this model: any such
number would be exactly the arbitrary limit this pass removes.
Caller-supplied rates (units per worker-hour, per
equipment-hour, fuel per unit) must come from the
method/equipment definition, not from this class.

Legacy constants NOT deleted in this pass (verified in the new
base, `SharedBusinessRuntimeManager.cs` lines 66-89):
`SharedOperatingCashFloorCents = 4500`; output units per input
(CropFarm 8, Ranch 2, Butcher 8, Blacksmith 4, FuelDealer 5,
GrainMill 4, Bakery 3); local outlet maxima (CropFarm 110, Ranch
4, Butcher 68, Blacksmith 34, FuelDealer 58, GrainMill 48, Bakery
62, Tailor 28, sawmill offcut local outlet 12); sawmill to
lumber yard weekly max 80. These are still read by legacy
callers, so deleting them now would break legacy businesses.
They are documented here as legacy calibration, and their
callers are a Codex migration task: move callers to
`ThroughputCapacity.Compute`, then retire the constants. Do NOT
replace them with another unexplained constant set. Fixed-ratio
weekly output is NOT restored anywhere in this pass.

## 14. Tests and migration (section 13)

Roslyn tests (small harness, direct DLL run, NOT Unity):
- File: `Tests/Editor/Economy/GenericBusinessReconstructionTests.cs`
  (21 tests, all passing at the end of this pass).
- Runner: small Roslyn harness
  (`roslyn-harness/harness-genbiz.csproj`: NUnit shim + Runner +
  the 12 new GenericBusiness files + the test file only).
  The full-repo Roslyn harness was NOT used for this pass,
  because the new files are dependency-free and the full repo
  does not compile under the harness shims; that is a harness
  limitation, not a code result.
- Result: 21 passed, 0 failed. Two failures were found and fixed
  during the pass, honestly: (a) a test-arithmetic error (free
  space 300 sqm, not 320 sqm, after reserving 20 sqm and 40 sqm
  from 360 sqm usable); (b) a real evaluator bug (a MixedUse
  entry in the recommended list wrongly matched a Residential
  plot; fixed so only a MixedUse PLOT matches broadly).
- Coverage maps to the prompt's test list: new business without
  invented stock; contribution without provenance refused;
  legitimate contribution recorded; existing business retains
  documented starting inventory; endowment without identity
  refused; retail without a template; catalog expansion without
  duplication; barber capability added to an existing retailer;
  multiple production recipes; insufficient equipment; insufficient
  floor space; mixed-use premises; multiple floors; legitimate
  leasing; no artificial zoning lock; valid supplier purchase
  (sorted by delivered total); horse/wagon asset identity;
  purchase without vendor refused; no duplicated business or
  financial authority; throughput bound by real constraints;
  templates never grant; acquisition defaults; start form
  refuses a missing premises right.

Unity-only tests (AUTHORED, NOT RUN, NOT PASSED):
- File: `Tests/Editor/Economy/GenericBusinessUnityIntegrationTests.cs`
  (4 tests). These reference UnityEngine and the full generic
  economy types and CANNOT run in the Roslyn harness. They must
  be run in Unity Test Runner (EditMode) by Codex / Unity AI.
  Until that run happens, report them as authored, not passed.
  They cover: barber capability keeps a single business
  identity; legacy production-method defaults unchanged;
  additive recipe fields round-trip; capability set matches the
  registry (including duplicate-id rejection).

Earlier tests: no earlier test was re-run in this pass, because
the small harness compiles only the new files. Full-regression
status is therefore NOT claimed. The 3 pre-existing Phase H
Roslyn failures and the `ConstructionInputSupportTests` Unity
timeout from earlier passes are unchanged by this pass (this
pass touches one production file additively and adds new files
only), but that is a reasoning claim, not a re-run result, and
Codex must confirm it in Unity.

Migration and compatibility:
- No legacy class was removed. No serialized class, field, or
  namespace was renamed. One production file was changed, and
  only additively: `ProductionMethodDefinition` gained six new
  fields with empty/zero defaults (section 8). Existing scenes
  and ScriptableObjects are unaffected by additive fields.
- Every new file has a `.meta` with a fresh GUID (see the pass
  report for the full list). The new folder
  `Domains/Economy/GenericBusiness` has its own folder `.meta`.
- The General Store single-SKU guard is NOT weakened and NOT
  replaced in this pass. Generic catalog work here adds product
  lines through the existing retail configuration; a tested
  generic guard replacing the single-SKU guard is a later task,
  and must be named as such if it happens.

## 15. Handoff to Unity AI (section 14)

Changed files (this branch, vs 273b113):
- MODIFIED (additive only):
  `Assets/Scripts/Domains/Economy/Generic/GenericBusinessEconomy.cs`
  (ProductionMethodDefinition + new
  ProductionInputSubstitutionDefinition).
- NEW (12 files, each with a `.meta`):
  `Assets/Scripts/Domains/Economy/GenericBusiness/BusinessCapabilityKind.cs`
  `Assets/Scripts/Domains/Economy/GenericBusiness/GenericBusinessCapabilitySet.cs`
  `Assets/Scripts/Domains/Economy/GenericBusiness/GenericBusinessTemplates.cs`
  `Assets/Scripts/Domains/Economy/GenericBusiness/GenericBusinessFormation.cs`
  `Assets/Scripts/Domains/Economy/GenericBusiness/ProductionRecipeDetail.cs`
  `Assets/Scripts/Domains/Economy/GenericBusiness/PremisesCapacity.cs`
  `Assets/Scripts/Domains/Economy/GenericBusiness/SiteEligibility.cs`
  `Assets/Scripts/Domains/Economy/GenericBusiness/AcquisitionInterfaceModel.cs`
  `Assets/Scripts/Domains/Economy/GenericBusiness/EquipmentPurchase.cs`
  `Assets/Scripts/Domains/Economy/GenericBusiness/ThroughputCapacity.cs`
  `Assets/Scripts/Domains/Economy/GenericBusiness/BusinessInspectorFields.cs`
  `Assets/Scripts/Domains/Economy/GenericBusiness/GenericBusinessUnityAdapter.cs`
  plus the folder `.meta` for `GenericBusiness`.
- NEW tests (each with a `.meta`):
  `Assets/Scripts/Tests/Editor/Economy/GenericBusinessReconstructionTests.cs`
  (Roslyn, 21 passing)
  `Assets/Scripts/Tests/Editor/Economy/GenericBusinessUnityIntegrationTests.cs`
  (Unity-only, authored, not run)
- NEW doc (with a `.meta`): this file,
  `Assets/Scripts/Docs/GenericBusinessReconstructionReview.md`.

New/modified components:
- No new MonoBehaviour and no new ScriptableObject class was
  created in this pass. The plain-C# records above are the
  components; Unity AI creates the Inspector/UI binding for them
  (see below). Do not invent a MonoBehaviour for them without a
  scene owner: the existing managers (`SharedBusinessRuntimeManager`,
  `BusinessInstanceState`, `GenericBusinessConfiguration`) are the
  owners, via the adapter mapping.

Inspector fields (for Unity AI to add, additive only):
- On `ProductionMethodDefinition` (already added in code, needs
  Inspector exposure): ByproductOutputs, FuelOrEnergyInputs,
  InputSubstitutions, RequiredFloorAreaSqM, WasteFraction01,
  FeasibilityConditions.
- On building/prefab authoring data (NOT yet in code; Unity AI
  adds, additive only): per-floor usable area, equipment
  footprint, workstation clearance, access/egress reserve,
  storage reserve. Map footprint cells to sqm via real
  dimensions; never invent a universal occupancy limit.
- Inspector grouping (Unity Editor work, no serialized change):
  mining state under a Mine capability foldout shown only for
  Mine businesses; `minimumHireLaborAccess` kept visible as a
  labor safeguard; `useLegacyPassiveSeed` and
  `allowedPlotZones` marked legacy in tooltips/docs, NOT deleted.

Serialized migration requirements:
- NONE for existing assets. The only production change is
  additive fields with empty/zero defaults, which old assets
  load as empty/zero and old behaviour is preserved. Do NOT
  rename any serialized class, field, or namespace. Do NOT
  delete `allowedPlotZones`, `useLegacyPassiveSeed`, or the
  throughput constants until their callers migrate (Codex
  tasks below).

Scene references to update:
- NONE in this pass. No Main Scene change was made and none is
  required for source correctness. Unity AI owns scene and UI
  integration; Codex owns final integration. Do NOT begin a
  major Main Scene visual reorganization on the basis of this
  pass.

UI calls and expected behaviours (for Unity AI to bind):
- Acquisition interface: two tabs, Start a Business (default)
  and Pre-existing Businesses; no Land tab in this interface;
  land via Property/Parcel workflow. Start form validates
  (name, owner, premises right, at least one capability) before
  any business is created; on failure, show the named reasons.
- Add to Business: bind to `TryAddCapability` /
  `TryExpandRetailCatalog` / `TryEnableProductionMethod` /
  `TryEnableService`. When a capability is enabled but not yet
  performable, show the feasibility reasons (missing equipment,
  workspace, space, premises right, authority, inputs, qualified
  person).
- Templates: offer General Store, Bakery, Fuel Dealer, Barber
  as presets. Show the "to acquire" list (equipment, workspaces,
  floor area, staffing suggestions) as a shopping/build list,
  never as granted items.
- Production methods: offer the enabled methods for a business;
  show inputs, outputs, byproducts, fuel/energy, substitutions,
  waste, floor area, and feasibility conditions per method.
  Convert recipe seconds to legacy minutes with
  `ceil(seconds / 60)` only where the legacy path is used;
  prefer the seconds-native stage machine.
- Premises: show usable vs gross area per floor, reserved vs
  free space, and the binding constraint when a reservation is
  refused. Show sqm; convert to sqft only at display time if
  the UI uses sqft, with the 10.7639 factor.
- Equipment purchase: show vendor quotes sorted by delivered
  total (price + freight); horse and wagon as separate lines
  with separate asset ids, even when hitched together.

Prefab requirements (for Unity AI / prefab authoring):
- Barber: a chair, period-appropriate cutting tools (hand
  tools, NO modern electric equipment), and sufficient usable
  space, per the capacity model. Prefab authoring must supply
  the capacity fields (floor usable area, chair footprint,
  clearance) so the model has real data to compute from.
- General: any new equipment prefab must carry an asset id and
  a footprint, so purchases register ownership and capacity
  reservations have real areas. No prefab in this pass supplies
  those values; Unity AI must author them from real dimensions.

Existing adapters that must temporarily remain:
- `StoreProcurementChain` alongside
  `OffMapNamedSkuProcurementService` (double-ordering guard
  still open, F26).
- `LogisticsRuntimeManager` timer shipments alongside the
  task-based freight path (unification still open, D1/P9-14).
- `BusinessProfileDefinition.CreateFallback` until all business
  types have authored profiles.
- `BusinessSiteRequirementDefinition.Meets` (legacy zone gate)
  until its callers migrate to `SiteEligibility.Evaluate`.
- Legacy throughput constants until their callers migrate to
  `ThroughputCapacity.Compute`.
- Bakery staged chain AND shop runtime until Kennedy picks ONE
  model (carried decision, below).

Tests passed and failed:
- Roslyn small harness: 21 passed, 0 failed (final run).
- Unity-only integration tests: authored, NOT run, NOT passed.
- Full regression: NOT run, NOT claimed.
- Pre-existing failures carried forward (not re-run here):
  3 Phase H Roslyn failures; `ConstructionInputSupportTests`
  Unity timeout (Unity-only, Codex task).

Known blockers (with owner):
- UNITY AI: first compilation of the new files in Unity
  (namespaces, console errors, random Unity issues); Inspector
  exposure of the six new `ProductionMethodDefinition` fields;
  building capacity fields on prefab authoring data; Acquisition
  UI tabs and start-form binding; Add to Business UI binding;
  template preset UI; equipment-purchase UI binding to the
  existing procurement authorities.
- CODEX: Unity Test Runner for the 4 Unity-only integration
  tests and for legacy General Store operations (no regression
  claimed until run); migration of legacy callers off
  `Meets`, off the throughput constants, and off
  `useLegacyPassiveSeed`; migration of the bakery one-model
  choice once Kennedy decides; freight shipment-set unification;
  double-ordering guard (F26); confirmation that the 3 Phase H
  failures and the Construction timeout are unchanged.
- KENNEDY (decisions, nothing guessed): which Bakery model
  survives (staged chain vs shop runtime); whether price/margin
  changes are tasks or instant UI actions (carried from the
  task audits; ordering tasks in the economy pass depend on
  it); what counts as a legitimate leasing/shared/mobile
  premises arrangement in each edge case; what supplier/vendor
  data is legitimate (no vendors or prices invented here);
  what Unity AI fields/prefabs are required beyond the list
  above; all prices, rates, weights, and frequencies (every
  number in tests is a test parameter only).

Open decisions carried forward from earlier passes (NOT decided
here): slice-departure gate activation; retiring the old 4%
arrival roll; unifying departure paths with destinations;
double-ordering guard (F26); friend graph vs family-only
recruitment; experience formation rate; meal-count rule (2 vs
up to 3 per day); phase-5 employment-id format acceptance;
D1 place vs retire; person-owned horse feed source; starting
horse + wagon approval and ownership; credit default policy and
write-off rule; daily wage rate basis; production owner-draw
reserve rule; freight route representation; flour unit semantics;
notions supply; multi-SKU batch ledger; post-departure
household/property/debt handling; seconds small vs full option;
sleep/meal durations; idle behaviour and idle-as-schedule-activity
vs literal WorkTask; customer familiarity threshold/lapse/scope;
routine triggers.
