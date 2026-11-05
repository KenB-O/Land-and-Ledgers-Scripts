# 13 - Item coverage register (definitive, code-side) — 2026-11-05

Scope: everything defined in the repo's CODE (`Assets/Scripts`). Serialized
Unity `.asset` files live outside the repo and are NOT audited here; the
exact files needed are listed in section K. Nothing below invents a price,
weight, or package size. "Unauthored" means no code or asset in the repo
states it.

Method: HouseholdItemCatalog + GeneralStoreItemTiers + Review2ItemSkuMap +
BakeryBreadCatalog read directly; the rest by a cited read-only sweep
(`13a-catalog-sweep.md`, every claim file:line). Two corrections to the
sweep: the "2 feed units per sack" figure IS in production code
(`FirstLedgerSliceBootstrapper.cs` registers `FeedUnitsPerStoreUnit`
2/1 for the horse-feed SKU); and the Millbrook fixture is a finite
20-sack horse-feed order at 15c/sack, not a general grain supply.

Columns: **id** (stable string) | **type** | **unit** | **package / conversion**
| **retail** (household can buy it at the General Store) | **wholesale** (business-to-business path exists) |
**produced by** | **consumed by** | **regional source today** | **price reference** | **gaps**.

## A. General Store retail items (26) — HouseholdItemCatalog + tiers

All 26 have a household definition (unit below) and a tier. Retail prices
live ONLY in `GeneralStoreBusinessDefinition.asset` (outside repo) — no
code-side price exists for any of them. All are retail-eligible by
definition; whether the store actually offers one depends on the asset
having an `ItemDefinition` with a positive price AND the unit matching
(or converting via Slice 10).

| id | unit | package / conversion | produced by | consumed by | source today |
|---|---|---|---|---|---|
| flour | lb | store sells 10 lb sack; package shape, NOT convertible at retail (Slice 10) | grain mill (toll/merchant); Millbrook `bakery_flour_sack` -> `bakery-flour` | bakery batches (12 lb -> 24 loaves); household meals | mill (local); Millbrook flour adapter (bakery flour only) |
| bread | loaf | identity loaf->loaf | bakery `bake.bread-loaf` | households | bakery via wholesale driver |
| potatoes | lb | store sells sacks; sack weight unauthored; NOT convertible | farm produce (`produce_<slug>`, no fixed list) | household meals | none in code |
| preserved_food | jar | identity | household preservation (no runtime found in sweep) | households | none in code |
| preserved_meat | lb | identity | butcher carcass fractions | households | butcher (carcass -> meat fraction; no fresh-meat retail id) |
| fuel_wood | bundle | identity bundle->bundle (dealer `fuel_wood_bundle`, 28c) | timber/fuel dealer profile | household heating; bakery fuel (via caller factor) | FuelDealerProfile.asset (outside repo) |
| soap | bar | identity | none in code | households | none in code |
| salt | lb | store sells small bags; bag weight unauthored; NOT convertible | none in code | households; bakery notions (as ingredient, not this SKU) | none in code |
| sugar | lb | identity | none in code | households | none in code |
| coffee | lb | identity | none in code (import implied, not modeled) | households | none in code |
| tea | lb | identity | none in code (import implied, not modeled) | households | none in code |
| candle | each | identity | none in code | households | none in code |
| lamp_oil | gal | store sells quarts; 4 qt = 1 gal (Slice 10, factor 4) | none in code | households | none in code |
| cookware | each | identity | none in code | households | none in code |
| bedding | set | identity | none in code (tailor makes garments, not bedding) | households | none in code |
| tool_hand | each | identity | none in code (forge makes nails/repairs, not hand tools as SKUs) | households | none in code |
| tableware | set | identity | none in code | households | none in code |
| clothing_textiles | yd | identity | none in code (tailor buys `tailor-cloth`, no producer) | households | none in code |
| cigarettes | pack | identity | none in code | households | none in code |
| tobacco | lb | identity | none in code | households | none in code |
| whiskey | bottle | identity | none in code | households | none in code |
| book | each | identity | none in code | households | none in code |
| jewelry | each | identity | none in code | households | none in code |
| perfume | bottle | identity | none in code | households | none in code |
| chocolate | bar | identity | none in code | households | none in code |
| silk_cloth | yd | identity | none in code (import implied, not modeled) | households | none in code |

Retail reality check: of the 26, only flour, bread, fuel_wood, and lamp_oil
have ANY production or supply path in code. The other 22 are definitions
waiting on a supplier. That is the P3 coverage problem in one line: the
store's catalog is 26 wide and its supply is 4 deep.

## B. Bakery and restaurant code items

| id | unit | package / conversion | produced by | consumed by | price |
|---|---|---|---|---|---|
| bakery-flour | lb | store sack -> lb via caller factor (SKU map entry 1); bakery-profile sack -> lb via own caller factor (entry 3) | mill; Millbrook adapter | bakery batches | store sack 35c; profile sack 58c (asset bases) |
| bakery-fuelwood | unit | bundle -> unit via caller factor (entries 10, 11) | fuel dealer bundles | bakery oven | bundle 28c (asset basis) |
| bake.bread-loaf | loaf | identity with store bread | bakery (12 lb flour + 1 notions -> 24 loaves; 4 firings/oven/day) | store wholesale; households | wholesale 2c / retail 5c (caller evidence, not canon) |
| bake.rolls | roll | no store path (deliberately unmapped) | bakery (6 lb -> 48 rolls) | (bakery adapter only) | none |
| bake.pie | pie | no store path (deliberately unmapped) | bakery (4 lb -> 12 pies) | (bakery adapter only) | none |
| bakery-notions | per batch | no store equivalent (deliberately unmapped) | procured via Series2NotionsSupply | bakery batches (1/batch) | none |
| bread_batch | batch | batch -> loaf via caller factor LoavesPerBreadBatch (24) | BakeryProfile.asset | bakery | 36c (asset basis) |
| bakery_flour_sack | sack | sack -> lb, own caller factor | BakeryProfile.asset | bakery | 58c (asset basis) |
| bakery_fuel_bundle | bundle | bundle -> fuel units, caller factor | BakeryProfile.asset | bakery | 28c (asset basis) |
| fuel_wood_bundle | bundle | bundle -> fuel units, own caller factor | FuelDealerProfile.asset | bakery | 28c (asset basis) |
| fuel_offcut_bundle | bundle | NOT mappable to bakery fuel (Kennedy/Codex decision pending) | FuelDealerProfile.asset | (undecided) | 22c (asset basis) |
| restaurant-bread | loaf | direct from bake.bread-loaf | bakery | restaurant meals | none |
| restaurant-meat | lb | — | (no producer in code) | restaurant meals | none |
| restaurant-produce | units | — | farm produce | restaurant meals | none |
| restaurant-dairy | units | — | dairy chain (no item id) | restaurant meals | none |
| restaurant-fuelwood | — | — | fuel dealer | restaurant | none |

## C. Farm kinds (not item ids — product-kind strings and lot kinds)

| kind | unit | produced by | consumed by | gaps |
|---|---|---|---|---|
| grain (threshed, undifferentiated) | — | threshing sheaves (wheat/oats/corn all -> `grain`; variety lost) | mill; `sell_grain_bulk`; feed | no item id; wheat/oats/corn indistinguishable after threshing |
| sheaves / hay / corn-ears | — | harvest | threshing / animal feed | lot kinds, not SKUs |
| flour / feed / bran / meal (mill kinds) | — | GrainMillShopRuntime (toll/merchant) | bakery (flour); animals (feed) | kind strings, not item ids; bran has no recorded consumer; meal has no buyer |
| produce_<slug> | — | FarmProduceLot (open-ended kinds) | households (via produce category); restaurant | no fixed list, no units, no prices |
| milk | units | DairyChain (daily yield per cow) | creamery (buys at 3c/unit); cheese/butter chains | NO household/store item id; no retail path |
| butter | — | churn-butter (10 milk -> butter + 5 buttermilk) | — | no item id; no retail path |
| cheese | — | milk + rennet + vat/press, cured over days | creamery (at scale) | no item id; green cheese never saleable |
| wool | lb | sheep shearing | — | produced, NO buyer in code |
| hides | lb | butcher carcass fractions | tannery -> leather | leather has NO buyer in code |
| meat (carcass fractions) | — | butcher (meat + hide + tallow + waste sum to 1.0; hogs -> lard, sheep -> mutton + sheepskin) | households as `preserved_meat` only | no fresh-meat retail item id |
| eggs | — | mentioned in a comment only | — | no item id, lot kind, or producer |
| seed (CropKind + variety) | abstract `u` | seed merchant / own holdback / import | planting | no item id; no price in code |
| lumber (logs -> lumber units) | units | timber -> sawmill -> lumber yard (grades clear/merchantable/cull) | construction contracts | no retail item id |

## D. Craft, industrial, medicine, construction

| id / kind | unit | produced by | consumed by | gaps |
|---|---|---|---|---|
| leather (tannery lots) | lb | hides + tanbark, 90-day batch | — | no item id; NO buyer (no saddler/harness-maker) |
| coal / iron / gold / silver (MineralResourceKind) | oz/ton (Au/Ag), pct-fe (Fe) | mine (no sale path in code) | forge (iron + coal, dispensed exactly once) | NO ore/coal/metal item id; no smelter/refinery; forge inputs have NO source in code |
| nails (forge run count) | count | forge nail runs | construction/hardware (as counts) | NOT a SKU; no nail item id |
| medicine-dose / wound-dressing | dose | doctor stock `medicine-stock` | doctor treatments (1 per dispense) | no producer/supplier; no price |
| construction-lumber / construction-glass | — | lumber yard / glass (no producer in code) | construction contracts; freight | freight ids only |
| ConstructionMaterialKind: Lumber, Hardware, Masonry, Paint, Glass, Roofing, Other | — | — | construction | kinds, not SKUs; hardware lots hold nails/hinges/latches/strap iron |
| saw / hammer | — | — | construction crew tool requirements | example ids only, not items |

## E. Feed and fuel SKUs

| id | unit | package / conversion | produced by | consumed by | price |
|---|---|---|---|---|---|
| store_horse_feed_sack | sack | 2 feed units per sack (production code: bootstrapper registers FeedUnitsPerStoreUnit 2/1) | Millbrook fixture (finite 20-sack order) | draft animals via HorseFeedStockAdapter | 15c/sack |
| bakery_flour_sack | sack | sack -> `bakery-flour` lb on receipt (Millbrook adapter) | Millbrook Grain Co. | bakery | (asset) |
| bakery_fuel_bundle / fuel_wood_bundle | bundle | bundle -> bakery fuel units, caller factor | BakeryProfile / FuelDealerProfile assets | bakery oven | 28c each (asset basis) |

## F. Service and equipment items (made to order, not stocked SKUs)

| id | unit | produced by | consumed by | gaps |
|---|---|---|---|---|
| tail.shirt / tail.trousers / tail.coat / tail.dress; tail.mend (service) | — | tailor, from `tailor-cloth` (yards) + `tailor-notions` | customers, made to order | `tailor-cloth` has NO producer in code |
| barber-soap / barber-linen | — | barber consumables | barber services | no supplier in code |
| wagon-wheel / wagon-axle | — | wheelwright spares | wagon repair | no supplier in code |
| ToolKitCatalog sets (mason-kit, tailor-hand-kit, barber-kit, doctor-bag, wheelwright-kit, stable-grooming-kit) | — | durable equipment sets | workers | NOT tradeable SKUs |

## G. Orphans — named in code, no definition anywhere

Generic-merchandise template suggestions ONLY (`GenericBusinessEconomy.cs:666-672`,
`TryApply` grants no goods): beef, bacon, cheese, basic_cloth, flannel,
workwear, boots, blanket, hat, candles, lantern, thread, buttons,
nails_hardware_lot, hand_tool, farm_tool, patent_remedy, medicine_bottle.
Smithy template suggestions ONLY (`:688-691`): 8d-cut-nails, hinges,
straps, brackets, hooks, simple-latches, bolts, pins. None has a unit,
price, producer, or supplier anywhere in code. `beef` also appears only as
a Ranch profile description; `cheese`/`meat` as substring checks in
delivery routing. These are NOT items. They must not be stocked, priced,
or sold until a real definition exists.

## H. Category ids in use (no master list in code)

Household catalog: `staple_food`, `meat`, `household_goods`, `clothing`,
`luxury_goods`, `fuel_wood`. Store/retail adds `medicine_remedies`,
`tools_hardware` (weekly reorder priority list, survival-ordered).
`animal_feed` (horse feed). `lumber` (acquisition default).
`produce_general` / `produce_<slug>` (open-ended). `freight_service`
(freight revenue, NOT goods). Categories are authored in
`GeneralStoreBusinessDefinition.asset` (outside repo); `ItemCategoryDefinition`
defines only the shape (markup default 1.15).

## I. Existing suppliers (what can actually supply the store today)

- **Millbrook Grain Co.** (`millbrook_grain_co`): ONE finite fixture — a
  20-sack horse-feed order at 15c/sack, transit 1 day, cash on arrival,
  recorded once as a scenario endowment (`FirstLedgerSliceBootstrapper.cs`,
  "small-prompt-2-horse-feed-order"). Plus the flour freight custody
  adapter (`bakery_flour_sack` -> `bakery-flour`). Millbrook is a grain
  company, not a general store supplier; it does not supply the other 24
  retail items.
- **Bakery** (local producer): bread loaves via the wholesale driver.
- **Creamery**: buys milk at 3c/unit (a demand outlet, not a store supplier).
- **Off-map catalogs**: `OffMapSupplierCatalog` and
  `OffMapNamedSkuProcurementService` are fully generic — caller-supplied
  SKUs/prices, no fixed list in code, no concrete supplier registered
  outside tests. `OffMapAidBusinessDefinition` (ScriptableObject) is the
  shape a real off-map supplier takes (inventory rows with order unit,
  conversions, price; production rows with weekly min/max/reserve) — the
  asset instance lives outside the repo.
- **Weekly off-map reorder**: sourceless by design (`OffMapSupplier`
  class); it is a fallback, not a supplier.

## J. Coverage assessment for P3 (what can be placed, what cannot)

Eligible merchandise = real tradeable goods with a definition (sections
A, B, E, plus F's made-to-order goods). Count: 26 retail + 16 bakery/
restaurant + 3 feed/fuel SKUs (one overlapping: `bakery_flour_sack`) = 44
distinct item ids, plus farm kinds (C) and craft kinds (D) that have no
item ids and therefore cannot be stocked as SKUs at all.

- **Has a source today (4 of 26 retail):** flour (mill/Millbrook),
  bread (bakery), fuel_wood (fuel dealer), lamp_oil (definition only —
  no supplier yet; convertible at retail since Slice 10).
- **Placeable in P3 without inventing data:** all 26 retail items CAN be
  stocked by a regional business that genuinely carries them, with prices
  from existing data or historical calibration and package definitions
  from the asset/register. The 22 currently unsourced retail items are
  placeable the moment a real Ashford/Redfield business lists them —
  that is the P3 build. Farm kinds (grain, produce, milk) can be
  supplied by farm/mill/dairy businesses as lots, but need item ids
  before they can be retail SKUs.
- **NOT placeable without new definitions (report as exceptions, never
  fabricate):** milk/butter/cheese as retail items (no item ids);
  fresh meat (no retail id — only `preserved_meat`); eggs (comment-only);
  iron/coal/ore (no item ids, no source); wool/leather as retail goods
  (produced, no buyer, no retail id); medicine doses (no producer);
  `tailor-cloth` (no producer); the 25 orphan template ids (section G);
  rolls/pies at the store (deliberately unmapped — bakery-only);
  fuel offcuts as bakery fuel (decision pending).
- **Package gaps that stay open:** flour sack weight is the asset's
  "10 lb sack" label (Slice 10 records it explicitly, never parsed);
  potato sack weight and salt bag weight are unauthored — a P3 business
  cannot honestly sell them by weight until the asset (or Kennedy)
  declares the package size.

## K. Missing .asset files (a complete Unity catalog audit needs these)

- `GeneralStoreBusinessDefinition.asset` — store `ItemDefinition`s
  (units, prices, categories); the ONLY authority for authored store
  units and retail prices.
- `BakeryProfile.asset` — `bakery_flour_sack` (58c), `bakery_fuel_bundle`
  (28c), `bread_batch` (36c).
- `FuelDealerProfile.asset` — `fuel_wood_bundle` (28c), `fuel_offcut_bundle` (22c).
- `OffMapAidBusinessDefinition` instance(s) — Millbrook inventory, order
  units, prices, production rows.
- `DevelopmentTownConfiguration` asset instance — authored businesses,
  cash targets, opening inventories (rows are `(itemId, minimumUnits,
  maximumUnits)`).
- Any `ItemDefinition` / price data for the orphan ids in section G, if
  they exist in Unity at all — none was found in code.
