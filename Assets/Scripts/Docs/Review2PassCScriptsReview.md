# Review 2 Pass C - Scripts Review

Branch: `muse/review2-passC-scripts`, based only on Pass B tip
`84d1d1cd4cde5f8d336497887fbf99cdafbfd037`. Single commit for the
pass. This pass covers scripts only. It does NOT claim Unity
compilation, Unity Test Runner results, PlayMode verification, scene
wiring, or a final Unity import GUID check. New `.meta` GUIDs were
checked only via repository text search: each new GUID appears
exactly once, and the four are mutually distinct.

## What changed

### 1. Item/SKU map - new, caller-parameterised, no canon

`Assets/Scripts/Domains/Economy/Core/Review2ItemSkuMap.cs`
(plus `.meta`, GUID `d6def01c1b0643d4af08f4c9089ed3fb`)

Maps between the spaces the game already uses, without renaming
any item id and without merging any price:

- Store `flour` (sack) <-> BakeryCode `bakery-flour` (lb):
  Packaging, caller factor `LbPerStoreFlourSack`. Forward is
  multiply; reverse is divide with remainder, never rounded up.
- Store `bread` <-> BakeryCode `bake.bread-loaf`: Direct,
  loaf-to-loaf (basis: the Pass 7 adapter's loaf pricing).
- BakeryCode `bake.bread-loaf` <-> RestaurantCode `restaurant-bread`:
  Direct.
- Household `bread` <-> Store `bread`: Identity (same item).
- Household `flour` (lb) -> Store `flour` (sack): Packaging via
  `LbPerStoreFlourSack`. This is deliberately NOT identity:
  an identity mapping would silently turn 1 lb into 1 sack.
- Household `fuel_wood` -> FuelProfile `fuel_wood_bundle`: Direct.
- BakeryProfile `bakery_flour_sack` -> BakeryCode `bakery-flour`:
  Packaging via `LbPerBakeryFlourSack`.
- BakeryProfile `bakery_fuel_bundle` -> BakeryCode `bakery-fuelwood`:
  Packaging via `BakeryFuelUnitsPerBakeryFuelBundle`.
- FuelProfile `fuel_wood_bundle` -> BakeryCode `bakery-fuelwood`:
  Packaging via its OWN factor
  `BakeryFuelUnitsPerFuelWoodBundle`. The two bundle SKUs are
  different items (bakery bundle 28c vs fuel offcut bundle 22c in
  the profile data), so they must never share one factor.
- BakeryProfile `bread_batch` -> BakeryCode `bake.bread-loaf`:
  Packaging via `LoavesPerBreadBatch`.

Rules enforced by the map itself:

- A packaging factor that is not supplied, zero, or negative is a
  refusal with a diagnostic naming the factor. It is never
  silently treated as 1.
- Negative quantities refuse. A null entry refuses.
- Unmapped ids return null with a diagnostic, never a guess.
  Deliberately unmapped: BakeryCode `bakery-notions` (no store
  item exists; supply runs through `Series2NotionsSupply`),
  `bake.rolls` and `bake.pie` (the store offers only loaf),
  FuelProfile `fuel_offcut_bundle` -> bakery fuel (oven use of
  offcuts is undecided, and the offcut bundle price differs
  from the fuel bundle price).

No conversion factor in this file or in the tests is canon. The
test values (10 lb per sack, 24 loaves per batch, 2 bakery fuel
units per bundle) are parameters supplied by the test caller to
prove the mechanism, and the tests say so. Existing adapters were
NOT rewired to use this map: the Pass 7 selling adapters, the
Pass 8 dealer source, and the weekly converters keep their
current behaviour, so no existing test changes meaning. The map
is available for Codex to call when binding drivers; it is not
a second authority, because nothing else reads it yet.

### 2. General Store weekly input path - addressed, not restored

Pass B removed the dangling `freight_goods` label from the
General Store's `inputCategoryIds`, leaving it empty in both the
profile and the definition. Pass C does NOT restore
`freight_goods` and does NOT invent a replacement procurement
category, and it does NOT put the store's six stock categories
into `inputCategoryIds` (that would change weekly procurement,
a binding decision, not a fix).

Evidence that weekly procurement does not need
`inputCategoryIds` today (all in `SharedBusinessRuntimeManager`
and `GeneralStoreRuntimeManager`):

- The General Store appears in neither
  `GoodsTransformerRuntimeTypes` nor `ServiceRuntimeTypes`
  (`SharedBusinessRuntimeManager.ArchetypeRuntimes.cs`), so the
  shared weekly converter never runs for it in production.
- It is excluded from `WeeklySettlementBusinessTypes`.
- Its restock is
  `GeneralStoreRuntimeManager.ResolveWeeklyPayrollAndReorder`:
  per-category pending reorder units, costed, delivered by
  off-map inbound shipment (stock arrives on delivery, not
  minted), plus local-supply transfers. That path does not read
  `inputCategoryIds`.

So the empty list is correct today. But it was a latent mint:
a staffed store run through `ResolveBusinessOperation` directly
would have credited planned throughput units of the first output
category (`staple_food`, baseline 36) with no input consumed and
no cash spent. Pass C closes that in the converter only
(see section 3). The store's real restock path is untouched.

### 3. Weekly converter guard - the one production change

`Assets/Scripts/Domains/Economy/Core/SharedBusinessRuntimeManager.cs`
(+37 lines, no other production file changed)

In `ResolveBusinessOperation`, after the Service path returns
and before `ProcureInputsForWeeklyOperation`: if the input
category is empty and the output category is non-empty, the
operation is blocked with reason
"no input category: weekly conversion cannot mint output from
nothing", produces 0 units, and returns before any stock is
credited. This is production-identical today (the General
Store never reaches the converter, see section 2) and closes
the latent mint above.

Scope of the guard, deliberately narrow:

- It applies to the converter path only. Service businesses
  return earlier, so Builder service revenue (empty input and
  empty output, paid by households) is unaffected.
- It does not apply to the Mine, which has its own special
  operation, or to LumberYard, which has a real lumber input.
- Only the General Store among Retail/Producer/Converter
  profiles is empty-input plus non-empty-output, so only it
  would ever hit the guard today.

### 4. Driver-binding checklist - diagnostics only

`Assets/Scripts/Domains/Economy/Core/Review2DriverBindingChecklist.cs`
(plus `.meta`, GUID `f0845b7a9ab84088ae47ec463201654c`)

Pure helpers, no behaviour change, no scene change, no new
authority. Ten ordered binding steps, in code so Codex cannot
drift from a doc: register bakery and fuel supplier adapters;
set the bakery adapter DayIndex at EVERY day start (the
adapter ignores its day argument, so a driver that only sets
it once will silently age stock wrong); run the Pass 6 bakery
pre-check before any Pass 8 dealer fuel supply, or explicitly
accept partial-store state on a refused off-map supply; route
cash through `Series2BusinessRuntimeCashPort`; check the
Pass 9 production port before trusting retail cash warnings;
bind the mine's special operation; wire fuel reserve links;
add a store-side horse feed link ONLY after Kennedy chooses a
feed source; run the binding checks on a debug route; then
drive a full production day end to end.

`CheckBakeryAdapterDay` refuses only while the adapter DayIndex
is unset; a day mismatch is a warning, because day equality is
NOT enforced by the adapter and Pass C does not invent that
enforcement. `CheckFuelSupplyOrder` refuses unless pre-check
first OR partial-store state is explicitly accepted, mirroring
the Pass A dealer partial-supply pin. `CheckSaveStateAssignments`
lists every missing save assignment (Pass 9, Pass 10, and
procurement F19) instead of failing on the first.

No DayIndex-setting helper was added: the adapter already
exposes its own DayIndex, and a second setter would be a
second authority. No pre-check-first fuel supply helper was
added: the checklist plus the existing pre-check is the
binding surface, and a helper that reordered supply would
change refused-supply behaviour, which Pass A pins.

### 5. Tests

- `Assets/Scripts/Tests/Editor/Economy/Review2PassCScriptsTests.cs`
  (plus `.meta`, GUID `d94d07723c814bb18a8754fc2ac95d0b`):
  16 Roslyn tests for the map and the checklist, cross-checked
  against the literal ids of `BakeryBreadCatalog`,
  `HouseholdItemCatalog`, and `RestaurantMealCatalog`
  (including a test that fails if a catalog constant drifts
  from the map). One test initially failed on its own wording
  ("Pass 6 pre-check" vs the checklist's "Pass 6 bakery
  pre-check"); the assertion was corrected, no production
  code changed for it.
- `Assets/Scripts/Tests/Editor/Economy/Review2PassCWeeklyConverterTests.cs`
  (plus `.meta`, GUID `fd4ac55ae2ee4df0b274883a13dfc496`):
  Unity-only test, NOT in the Roslyn harness. It follows the
  `ProfessionWaveRuntimeTests` pattern (GameObject +
  `SharedBusinessRuntimeManager`, staffed General Store via
  `Resources.LoadAll` profiles, staple_food set to a known
  value, `ResolveBusinessOperation` by reflection) and asserts
  stock unchanged, throughput 0, and the blocked reason
  containing "no input category". Codex/Unity AI must run it
  in the Unity Test Runner; Pass C does not claim it passes.

## What was checked and left alone

- Pass A pins, unchanged: retail WARNING-only cash (adapters
  post cash after stock moves; the Pass 9 production port
  `Series2BusinessRuntimeCashPort` makes the WARNING defensive
  dead code); bakery DayIndex gating (adapter ignores its day
  argument, refuses only while its own DayIndex is unset, day
  equality NOT enforced); dealer fuel partial supply (a refused
  off-map callback leaves dealer units already in the bakery
  store - not a clean rollback). No Pass A test was changed.
- Existing Unity test
  `ProfessionWaveRuntimeTests.BakeryRequiresFuelAsSecondaryInput`
  looks inconsistent with the bakery weekly-converter guard
  (the guard returns early with the task-driver reason, not a
  fuel shortage). It was NOT changed: it is a Unity-side
  expectation, and whether it needs updating is a Codex/Unity
  decision once the guard runs in Unity. Flagged here, not
  silently "fixed".
- General Store dual authority, NOT resolved: profile and
  definition remain identical after stripping script/name/
  class ($450/8000c/1.4); the bootstrapper uses the
  definition, the runtime loads profiles. Neither was deleted
  or overwritten. This is a Codex/Kennedy decision.
- `ResolveHorseFeedStock` still returns null for General Store
  and person-owned horses. No feed source was chosen (Kennedy
  has NOT chosen one), and null is NOT pinned as expected
  behaviour. It stays documented as a gap; the checklist
  step 8 names it explicitly.
- Six-person fixture and validator: untouched. No Ethan/Tia/
  Shane/Tannis data was authored, and no nine-person fixture
  was created (inputs still incomplete).
- No `.asset` file was created, deleted, or edited. No Main
  Scene, prefab, or ProjectSettings change. No repository
  visibility or permission change. No serialized field was
  renamed. One cash writer per domain and one debt authority
  are preserved; no synthetic inventory, fake person,
  fake job completion, or duplicate task authority was added.

## Decision-gated items left for Kennedy/Codex

Everything from the Pass C brief that is a decision, not a
fix, remains open: flour/fuel conversions as canon (the map
parameters are test parameters only), item-level stock
migration, player-fund reconciliation, mortality and
work-eligibility thresholds, freight payer/commission, seconds
granularity, meal/sleep durations, departure gate, retiring
the 4% arrival roll, double-ordering guard, friend graph vs
family-only, and all prices/rates/weights/frequencies as canon.
The horse feed source is the only one with a code placeholder
waiting on it (`ResolveHorseFeedStock`, checklist step 8).

## Verification (Roslyn harness only)

Local .NET 8 SDK, freshly built binaries for this tip:

- Main harness (`harness-passC.csproj`, Pass C files plus
  `RestaurantMealCatalog.cs` added to the Pass A set):
  **98 passed, 0 failed** - 74 Series 2, 8 Pass A re-runs,
  16 new Pass C.
- Phase H harness (`harness-passC-phaseh.csproj`, freshly
  built): 86 passed, **3 failed, all the baseline Phase H
  failures unchanged** (`EndToEndTrace_InsufficientFunds_
  FailsThenRecovers`; `Secondary_Hardship_MissedMeals_
  NoInvisibleReplenishment`; `Secondary_Restock_
  ProcurementChainRefillsThenSaleSucceeds`, off by one:
  expected 60, got 59). No new Phase H failure appeared.
- `ConstructionInputSupportTests` was NOT re-run (per brief;
  its 1,800-second Unity timeout is a Codex diagnosis item).
- The Unity-only converter test was NOT run (no Unity here);
  it is Codex/Unity Test Runner work.

## What still needs doing

- Vertical slice passes, then the second scripts pass, then
  the second ScriptableObjects pass, then the final
  verification cleanup pass, then pull. None started here.
- Codex/Unity: run `Review2PassCWeeklyConverterTests` in the
  Test Runner; decide on the `BakeryRequiresFuelAsSecondaryInput`
  expectation against the new guard; decide the General Store
  dual-authority question; choose a horse feed source.
- Kennedy: every decision-gated item listed above, starting
  with the horse feed source.
