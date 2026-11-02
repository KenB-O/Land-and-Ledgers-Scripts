# Review 2 Second ScriptableObjects Pass — Review

Second ScriptableObjects pass: trace A to Z through the vertical slice
with real, meaningful changes to descriptive objects. Base: second
scripts tip `83e6344ef2c1cfd52365f7e105014385752048b7`
(`muse/review2-second-scripts`). This branch:
`muse/review2-second-scriptableobjects`.

What this pass claims, in one sentence: the one misleading asset name
left from Pass B is renamed with its GUID unchanged, the headline
asset/runtime disagreement — a runtime-added store feed item is
feedable to store horses but unsellable to person-owned horses
through the production port — is proved from code and assets and
made observable by a diagnostic-only helper with tests, and every
other slice asset was re-checked and left alone for a stated reason.
What this pass does NOT claim: Unity compilation, Test Runner,
PlayMode, scene wiring, or the final Unity import GUID check. New
GUIDs are checked via text search (mutually distinct, once each in
the tracked tree after commit); Unity import remains the final GUID
check, and is Codex work.

## 1. What changed — exactly two changes

### Change 1 — Town variant renamed to a distinguishing name

- Old files: `Assets/Core/World/DefaultTownGenerationSettings 1.asset`
  (+ its `.meta`, GUID `3c5986b4d8fe3e74189dbfcbddf4b6a8`)
- New files: `Assets/Core/World/RegionalFoundationMineralTownGenerationSettings.asset`
  (+ its `.meta`, same GUID, moved with `git mv`, not regenerated)
- Old `m_Name`: `DefaultTownGenerationSettings 1`
- New `m_Name`: `RegionalFoundationMineralTownGenerationSettings`
- Unchanged, deliberately: every other field, including seed
  2147483647, grid 220x200, regional foundation fields, mineral
  districts (2 each of coal/iron/gold/silver), detail props off,
  forest dressing off, debug overlays off, and the
  `schoolhousePhysicalDefinition` field the base asset leaves
  unset.
- Evidence the rename is safe: no code loads this asset by path
  (editor utility and tests load only the base asset by path,
  `Assets/Core/World/DefaultTownGenerationSettings.asset`), no
  code references its GUID (searched all of `Assets` for the GUID
  and the old filename: only the asset itself, its `.meta`, and
  historical docs name it), and scene/prefab references are
  GUID-based, which is unchanged — so the rename is safe even if
  the PC-local Main Scene references it. The new name describes
  the authored content (regional foundation + mineral districts),
  the two additions that distinguish it from the base asset; it
  is not "Default" plus Unity's duplicate suffix any more, which
  is exactly the distinguishing name Pass B said it needed if
  kept. Historical docs (Pass B review, Pass 22 doc, playtest
  backlog) still name the old filename; they are historical
  records, not loaders, and were not rewritten.
- Not done: the variant was NOT deleted. It is a distinct
  variant, not a duplicate, and its scene loading (if any) is via
  a Main Scene Inspector reference (PC-local, BLOCKED from Git).

### Change 2 — Diagnostic-only helper for the feed offer gap

- New: `Assets/Scripts/Domains/Economy/Core/Review2StoreOfferGapCheck.cs`
  (+ `.meta`, GUID `2c68470ab8fa4ea7a9ce4ccce2238186`)
- New tests: `Assets/Scripts/Tests/Editor/Economy/Review2SecondScriptableObjectsTests.cs`
  (+ `.meta`, GUID `b65aadbfe9dd47828ec56ff0ff51c7bd`), 5 tests.
- This is the minimal script change this pass allows itself, and
  it changes NO behaviour: the helper only returns diagnostics.
  It exists because the headline gap (section 2) cannot be fixed
  by any asset edit without either adding a default feed item
  (forbidden — feed is player-addable, never default) or choosing
  which authority wins (forbidden — see section 3). Making the
  gap observable, with a test proving each branch, is the
  decision-free change; the production fix is a Kennedy/Codex
  design decision, recorded in section 6.

## 2. Headline bottleneck — feed registration vs production offers

Traced precisely, with file evidence. Both sides were read, not
assumed:

- Registration side (D2, unchanged here):
  `Review2StoreHorseFeed.AddFeedItem`
  (`Assets/Scripts/Domains/Economy/Core/Review2StoreHorseFeed.cs`)
  creates only a runtime category entry on `BusinessRuntimeState`
  (`EnsureCategoryStock`, 0 stock unless it already exists) and a
  registry entry. It adds no definition category, no definition
  item, no profile entry, and no price. Prices live at the store's
  own offer, and the helper deliberately sets none.
- Offer side (production, unchanged here):
  `GeneralStoreRuntimeManager.BuildHouseholdTradingPort`
  (`Assets/Scripts/Domains/Economy/Businesses/GeneralStore/GeneralStoreRuntimeManager.cs:372`)
  iterates ONLY `storeDefinition.Categories` (line 381) and, per
  category, ONLY the definition's authored items (line 417),
  falling back to the category's own item list only when the
  definition's item table is empty for that category. It never
  iterates runtime category stock. The port's per-category price
  comes from `GetAverageCategorySellingPriceCents`, which averages
  authored definition items for the category and falls back to
  `minimumCategoryUnitPriceCents` (line 164: 5c) when there are
  none.
- Consequence, stated exactly:
  1. A store-owned horse CAN draw feed from a runtime-added
     category: the D2 adapter reads runtime category stock
     directly, and D2 proved it (20 store units, idle ration
     draws exactly 4, no degradation).
  2. A person-owned horse CANNOT buy that same feed through the
     production port if the feed item is a new category or a new
     item id in an authored category, because the builder emits
     no offer for it. D2's person-leg test does NOT prove
     otherwise: it used a test-seam `RuntimeStorePort`
     (`Review2VerticalD2Tests.cs:63`) whose `OfferedItemIds`
     were set directly to the feed item (line 346), bypassing
     `BuildHouseholdTradingPort` entirely.
  3. If the owner instead registers feed under an EXISTING
     authored definition category AND item id (for example
     `staple_food` / `flour`), the item IS offered — but feed
     draws and household sales then share that category's single
     aggregate pool in `BusinessRuntimeState`, so a household
     buying the item and a store horse drawing feed consume the
     same stock. That is a conflation, not a clean feed offer.
  4. Selling a runtime-only feed item at the 5c floor by default
     would silently canonise a price, so no fix in this pass
     does that. The production fix needs a runtime-offer path
     plus an owner-set price field, and where that price lives
     (registration, offer, or profile override) is the design
     decision in section 6.
- The helper (`Review2StoreOfferGapCheck.CheckFeedOffer`) returns
  exactly one diagnostic per registration: GAP when the category
  is not a definition category, GAP when the item is not an
  offered item of its definition category, SHARED POOL when it is
  an authored definition item, and nothing when there is no
  registration. Its caller contract is stated in its header:
  the caller must pass the EFFECTIVE offer item list per
  category, exactly as `BuildHouseholdTradingPort` would emit it.
  Proved by 5 new tests: new-category feed is a gap, new-item
  in an authored category is a gap, authored-item feed is a
  shared-pool warning (not a gap), no registration gives no
  diagnostics, and a missing category id gives a diagnostic
  without claiming a gap.

## 3. General Store dual authority — still identical, still unresolved

- Re-verified at this tip by scripted diff: `GeneralStoreProfile.asset`
  and `GeneralStoreBusinessDefinition.asset` are still identical
  after stripping `m_Script`, `m_EditorClassIdentifier`, and
  `m_Name` — same business id/type, same economy
  (startingCashCents 45000, reserve 8000c, markup 1.4), same six
  categories, same 27 items with identical prices, stocks, and
  reorder targets, same worker slots, same site requirements
  (minimum 5x5 cells, frontage 5, road frontage required).
- The two authorities still reach different consumers, and that
  is why a future divergence would matter, not a present one:
  the bootstrapper's `storeDefinition` is a serialized Inspector
  field (`FirstLedgerSliceBootstrapper.cs:124`, wired at line
  1073 into `storeRuntime.Configure`), so the live store
  manager's offers, prices, site suitability, and weekly reorder
  come from whichever asset the Main Scene Inspector points to;
  the shared runtime loads profiles via
  `Resources.LoadAll("Core/Economy/BusinessProfiles")`
  (`SharedBusinessRuntimeManager.cs:9634`), which loads the
  Resources profile, NOT the Core definition (the definition
  lives outside Resources). Today there is no disagreement to
  resolve, because the files agree; choosing a winner by
  deleting or overwriting one would still pick a canonical asset
  by stealth, so neither was touched. The choice, and how the
  $250 fixture target and player-fund reconciliation apply
  exactly once, remain Kennedy/Codex decisions.
- The feed gap in section 2 is NOT a profile/definition
  disagreement: both assets agree, and both lack a feed item —
  correctly, because feed is player-addable, not default. The
  gap is between authored definition data and runtime
  registration, which no profile/definition choice fixes.

## 4. Slice assets traced, checked, and left alone

Every `.asset` the vertical slice actually loads was traced to its
consumer. Values below are test parameters, never Canon, and
never the new fixture targets.

- General Store: definition (Inspector, live manager) + profile
  (Resources, shared runtime). See section 3. No feed item in
  either, correctly. No change.
- Bakery: `BakeryProfile.asset` only (Resources, type 13).
  Categories flour (`bakery_flour_sack`, sack, 58c, stock 18),
  fuel_wood (`bakery_fuel_bundle`, bundle, 28c, stock 6), bread
  (`bread_batch`, batch, 36c, stock 24). Worker Baker 1400c
  required. Site 5x5, frontage 4. The code-space ids
  (`bakery-flour`, `bake.bread-loaf`, `bakery-notions`) are
  mapped by Pass C's `Review2ItemSkuMap`, not by renaming any
  profile id here. No change.
- Fuel Dealer: `FuelDealerProfile.asset` only (type 11).
  Categories slabs_offcuts (`fuel_offcut_bundle`, bundle, 22c,
  stock 12), fuel_wood (`fuel_wood_bundle`, bundle, 28c,
  stock 24). Worker Fuel Yard Keeper 1200c required. Site 6x6,
  frontage 4. The 28c bundle price converges with the bakery
  fuel bundle, as Pass B noted, but merging them is a mapping
  decision, not an asset edit. No change.
- Freight (LiveryFreight): `LiveryFreightProfile.asset` only
  (type 9). Categories livery_feed_upkeep
  (`livery_upkeep_lot`, lot, 135c, stock 18), freight_service
  (`freight_service_visit`, job, 200c, stock 0), hauling_capacity
  (`team_wagon_day`, day, 120c, stock 0), wheelwright_repairs
  (`livery_repair_lot`, lot, 220c, stock 2). Workers Stable Boss
  1450c required, Teamster, Hostler. Site 7x7, frontage 6. The
  livery feed branch in `SimulationDrivers` reads exactly
  `livery_feed_upkeep` runtime stock, matching this profile.
  No change.
- Ranch feed, noted, NOT adopted: `RanchProfile.asset`
  (type 3) already authors `feed_inputs` / `feed_lot` (lot,
  120c, stock 24) and the Livery profile authors
  `livery_upkeep_lot` (135c). Neither is a store item, and
  neither was silently adopted as the store feed id, price, or
  unit — adopting one would canonise it. They are evidence
  that authored feed items exist elsewhere, not a store feed
  default. No change.
- Household inventory: no household-inventory asset exists;
  the household catalog lives in code (`HouseholdItemCatalog`),
  with D2's runtime registration for feed items. Creating an
  asset for it would invent a catalog shape, so none was
  created. No change.
- Horse feed: no animal/feed asset exists, correctly — feed is
  a runtime-added store item per Kennedy's decision, not a
  default asset entry. No feed item was added to any profile or
  definition. No change.
- Buildings and freight hosting: `LargeLivery.asset` still
  cannot host a workplace (`workplaceEligible: 0`, so
  `IsSuitableForBusiness` returns false for every type before
  any suitability row is consulted). Generic business shells
  (workplace 1) CAN host LiveryFreight via the default
  commercial fit fallback (`BuildingDefinition.cs:937-948`,
  which lists LiveryFreight among fallback types), so a freight
  company is placeable in a generic shell — just not in the
  livery-named shell. Flipping `workplaceEligible` on
  LargeLivery would be a gameplay decision, so it was left as
  authored. No change.
- All other profiles: re-checked with the same scripted
  category/item consistency check as Pass B (owned/input/
  output/service lists vs defined categories; item table
  category vs category item lists). Zero dangling entries
  across all 20 profile/definition assets, same as Pass B.
  Business types are unique across the 19 Resources profiles
  (0-18, each once). The Mine profile's two
  `requiredForOpening: 1` slots (foreman, pitman) are as
  authored in Pass B and are not a defect on this evidence.
  No change to any of them.

## 5. Verification

- Git-level checks (scripted, against the worktree before
  commit): all 63 `.asset` files have a `.meta`; asset `.meta`
  GUIDs have zero duplicates; every repo script GUID resolves
  to a real `.cs.meta` in Git (the single external GUID is the
  TextMeshPro package script GUID `71c1514a6bd24e1e882cebbe1904ce04`
  on the 16 font assets, which is a package asset, not a repo
  file, and is expected); zero dangling category entries across
  all 20 profile/definition assets after this pass's changes;
  profile-vs-definition identity still holds after stripping
  script/name/class; renamed town variant `.meta` GUID
  unchanged (`3c5986b4d8fe3e74189dbfcbddf4b6a8`), `buildingId`
  fields untouched (this asset has none), and no code loads it
  by path or references its GUID.
- New GUIDs, freshly generated with
  `python3 -c "import uuid; print(uuid.uuid4().hex)"`, never
  copied: helper `2c68470ab8fa4ea7a9ce4ccce2238186`, tests
  `b65aadbfe9dd47828ec56ff0ff51c7bd`, this doc
  `008a5f0917aa4a62a1a9759d3afd070c` — each appears in exactly
  one `.meta` file, mutually distinct, with no other asset
  sharing the GUID (verified by `git grep` after commit; the
  doc's own mentions of the GUIDs are text references, not
  additional assets. Unity import remains the final GUID check).
- Roslyn only (this pass touched one `.cs` file, so suites
  were re-run): main harness, freshly built
  `harness-2SO.csproj` (second-scripts harness retargeted to
  this worktree, plus the helper and the new test file):
  **136 passed, 0 failed** (second scripts' 131 + 5 new; all
  5 new tests pass by name; Series 2, Pass A, Pass C, D1, D2,
  D3, and second-scripts suites all re-run inside that count,
  none weakened, no existing test changed). Phase H harness,
  freshly built for this tip (second-scripts Phase H harness
  retargeted, no new test file added to it): **94 passed, 3
  failed — exactly the 3 baseline failures, unchanged**
  (insufficient-funds trace expecting true; hardship
  missed-meals expecting 'unaffordable', got ''; restock chain
  expecting 60, got 59). Per the second-scripts finding, those
  three share one root cause — a policy fork between honest
  downsizing and all-or-nothing needs — which is a Kennedy /
  development-manager decision, NOT fixed here by weakening
  tests or removing downsizing. `ConstructionInputSupportTests`
  NOT re-run, per task.
- NOT claimed: Unity compilation (including the helper file
  and the renamed asset), Test Runner, PlayMode, scene wiring,
  final Unity import GUID check, or Main Scene reference
  confirmation for the renamed variant or the font scrap list.

## 6. What still needs doing

- Final verification cleanup pass, to get everything ready to
  be pulled. Pull target is the final cleanup branch, NOT this
  branch, NOT Pass 11, NOT any intermediate branch.
- Codex / Unity, in order of slice impact:
  1. Production feed-offer fix (the section 2 gap): a
     runtime-offer path in or alongside
     `BuildHouseholdTradingPort` plus an owner-set price for
     runtime-added items, with a decision on where that price
     lives (registration, offer, or profile override). Until
     then, the helper's GAP diagnostic is the honest signal,
     and the D2 person-leg proof must not be quoted as a
     production-port proof. Also verify the store-owned leg
     in-game: registration call site, per-day household adapter
     construction, and CareDay wiring after a load.
  2. Unity import of this branch (final GUID check, including
     the town-variant rename — references are GUID-based, but
     import must confirm no path-based break) and a Main
     Scene GUID search for the Pass B scrap list before any
     deletion: 16 Core font copies (Resources copies are live;
     Core copies have no Git-visible consumer) and the
     renamed town variant's old name in scene references.
  3. General Store canonical asset decision (profile vs
     definition, and how fixture targets and player-fund
     reconciliation apply exactly once); LargeLivery hosting
     decision (`workplaceEligible: 0`); profile-less business
     types 19-27 and 255 (author or explicitly rule out);
     calendar start year (FirstLedger 1870 vs TimeSettings
     1886 vs 1880 anchor); Phase H policy fork disposition
     (keep downsizing and update the 3 tests, or
     all-or-nothing needs per Kennedy's decision).
  4. All previously open Codex/Unity work carries forward
     unchanged: production binding (Pass 6 driver, adapter
     registration, daily DayIndex, F19, Pass 9/10 and D3 save
     assignments, second-scripts bootstrap bind), Test Runner
     suites run INDIVIDUALLY (never one forced combined run),
     PlayMode full-chain proof with flow assertions, and the
     nine-person fixture inputs (ages, Tia's role, houses,
     horses/wagons, freight business name/instance id, seed
     rule, freight payers) — none invented here, none applied
     to any profile.
- Kennedy decisions still open (unchanged by this pass):
  feed-offer price home and runtime-offer shape; General Store
  canonical asset; item-level stock migration; item/SKU mapping
  and flour/fuel unit semantics as canon; LargeLivery hosting;
  profile-less business types; calendar start year; horse
  mortality rule and work-eligibility threshold; feed-need
  origination; freight payer per leg and commission; seconds
  granularity; meal/sleep durations; departure gate; the 4%
  roll; double-ordering guard; friend graph; and all
  prices/rates/weights/frequencies as test parameters only.
  Fixture cash targets ($65/$75/$60/$125 households;
  $250/$175/$200/$100 businesses) were NOT applied to any
  profile in this pass — they are development-fixture inputs,
  not profile defaults, and editing a profile would change
  every generated world using it.
