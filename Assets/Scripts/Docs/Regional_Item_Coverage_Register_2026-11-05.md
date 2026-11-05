# 13 - Item coverage register (definitive, code + assets) — 2026-11-05

CORRECTED 2026-11-05 ~09:20 EST: the first version of this register wrongly
claimed the economy `.asset` files live outside the repo. They do NOT: the
repo holds 65 `.asset` files, including every economy definition below. Only
Unity scenes (`.unity`, 0 in repo) and prefabs are absent. This version is
grounded in the assets actually read, and the wrong claims are struck here
rather than silently edited away.

Scope: repo code (`Assets/Scripts`) PLUS the economy assets read in full:
`GeneralStoreBusinessDefinition.asset` (27 items), `BakeryProfile.asset`,
`FuelDealerProfile.asset`, `OffMapAidBusinesses/MillbrookGrainCo.asset`,
`DevelopmentTownConfiguration.asset`. Nothing below invents a price, weight,
or package size. "Unauthored" means no code or asset in the repo states it.

## A. General Store items (27) — the asset is the authority

Store economics: starting cash 45000c, weekly reorder reserve 8000c, default
markup 1.4. Category markups: staple_food 1.45, meat 1.35, clothing 1.35,
household_goods 1.45, tools_hardware 1.4, medicine_remedies 1.55.
Prices below are baseline / wholesale / local band (cents); stock is
starting units, reorder target, lot size.

| id | unit | baseline / wholesale / band | stock / target / lot | producer tags | consumer tags |
|---|---|---|---|---|---|
| flour | 10 lb sack | 35 / 18 / 28–45 | 48 / 60 / 12 | mill, freight | household, bakery |
| bread | loaf | 5 / 2 / 4–8 | 24 / 30 / 6 | bakery, household | household |
| potatoes | sack | 25 / 12 / 18–35 | 30 / 40 / 10 | farm, freight | household |
| salt | small bag | 10 / — / 8–15 | 36 / 48 / 12 | freight | household, butcher |
| sugar | lb | 8 / — / 6–12 | 50 / 60 / 10 | freight | household, saloon |
| coffee | lb | 25 / — / 18–35 | 40 / 50 / 10 | freight | household, saloon |
| tea | lb | 60 / — / 45–80 | 12 / 18 / 6 | freight | household |
| beef | lb | 12 / 6 / 9–18 | 36 / 48 / 12 | ranch, butcher | household, saloon |
| bacon | lb | 15 / — / 12–22 | 32 / 40 / 8 | butcher, freight | household, saloon |
| cheese | lb | 15 / — / 12–22 | 24 / 32 / 8 | dairy, freight | household, saloon |
| basic_cloth | yard | 12 / — / 9–18 | 40 / 50 / 10 | freight | household, tailor |
| flannel | yard | 35 / — / 28–50 | 20 / 28 / 7 | freight | household, tailor |
| workwear | set | 150 / — / 120–210 | 10 / 14 / 4 | freight, tailor | household, worker |
| boots | pair | 250 / — / 200–350 | 8 / 12 / 4 | freight | household, worker |
| blanket | each | 125 / — / 90–180 | 10 / 14 / 4 | freight | household |
| hat | each | 75 / — / 50–125 | 8 / 12 / 4 (coreStock 0) | freight | household, worker |
| soap | bar | 5 / 2 / 4–8 | 48 / 60 / 12 | freight, household | household |
| candles | bundle | 10 / 4 / 8–15 | 36 / 48 / 12 | freight | household |
| lamp_oil | quart | 20 / 10 / 15–30 | 24 / 36 / 12 | freight | household, business |
| lantern | each | 100 / — / 75–150 | 6 / 10 / 2 | freight, tinsmith | household, business |
| thread | spool | 5 / — / 4–8 | 30 / 40 / 10 | freight | household, tailor |
| buttons | card | 5 / — / 3–8 | 20 / 30 / 10 | freight | household, tailor |
| nails_hardware_lot | lot | 25 / 12 / 18–40 | 30 / 40 / 10 | freight, blacksmith | household, builder, farm |
| hand_tool | each | 100 / — / 75–160 | 8 / 12 / 4 | freight, blacksmith | household, worker, farm |
| farm_tool | each | 250 / — / 180–400 | 5 / 8 / 2 | freight, blacksmith | farm |
| patent_remedy | bottle | 50 / — / 35–85 | 12 / 16 / 4 | freight, doctor | household |
| medicine_bottle | bottle | 75 / — / 50–125 | 8 / 12 / 4 | freight, doctor | household |

Package facts now VERIFIED from the asset (not assumed): flour IS a
"10 lb sack"; lamp oil IS a "quart" (confirming the Slice 10 basis);
potatoes are a "sack" with NO weight stated; salt is a "small bag" with
NO weight stated. The two unknown weights stay unknown.

Household-catalog mismatch (open question, not a claim): the asset's 27
items and the household catalog's 26 overlap on only 9 ids (flour, bread,
potatoes, salt, sugar, coffee, tea, soap, lamp_oil). Fifteen household
items have NO store definition (preserved_food, preserved_meat, fuel_wood,
clothing_textiles, cookware, bedding, tableware, cigarettes, tobacco,
whiskey, book, jewelry, perfume, chocolate, silk_cloth) — the store does
not sell them. Eighteen asset items have NO household definition (beef,
bacon, cheese, basic_cloth, flannel, workwear, boots, blanket, hat,
candles, lantern, thread, buttons, nails_hardware_lot, hand_tool,
farm_tool, patent_remedy, medicine_bottle) — how a household receives one
as inventory is a representative-item mapping question for Unity
verification, not something this audit decides.

## B. Bakery and fuel profiles (assets read)

- `BakeryProfile.asset`: `bakery_flour_sack` sack 58c; `bakery_fuel_bundle`
  bundle 28c; `bread_batch` batch 36c. Recipe evidence (code):
  12 lb flour + 1 notions -> 24 loaves; 6 lb -> 48 rolls; 4 lb -> 12 pies;
  4 firings/oven/day.
- `FuelDealerProfile.asset`: `fuel_wood_bundle` bundle 28c (band 15–48,
  stock 24, target 72, producer fuel_dealer); `fuel_offcut_bundle` bundle
  22c (band 12–40, stock 12, target 32, producer sawmill). Offcuts as
  bakery fuel remains undecided (SKU map: Kennedy/Codex decision).
- Bakery code items: `bakery-flour` (lb), `bakery-fuelwood` (unit),
  `bake.bread-loaf` / `bake.rolls` / `bake.pie`, `bakery-notions`
  (yeast/soda/salt/lard, 1/batch). Rolls/pies have no store path
  (deliberately unmapped). Restaurant code items: `restaurant-meat` (lb),
  `restaurant-bread` (loaf), `restaurant-produce` (units),
  `restaurant-dairy` (units), `restaurant-fuelwood`.

## C. Farm kinds (code-side; not item ids)

Crops: wheat, corn, oats (Red Fife, Scotch Oats, Yellow Dent, Timothy).
Harvest lots `grain`/`sheaves`/`hay`/`corn-ears`; threshing collapses all
to undifferentiated `grain` (variety lost). Mill kinds `flour`/`feed`/
`bran`/`meal` (kind strings, not ids); bran has no recorded consumer;
meal has no buyer. Produce is open-ended `produce_<slug>`, no fixed list.
Dairy: milk (creamery buys at 3c/unit), butter, cheese — none has a
household/store item id or retail path. Wool (lb, no buyer), hides ->
leather (no buyer), meat carcass fractions (households get only
`preserved_meat`). Eggs: comment-only. Seed: CropKind + variety, abstract
`u`, no item id or price. Lumber: logs -> lumber units (grades
clear/merchantable/cull), no retail item id.

## D. Craft, industrial, medicine, construction (code-side)

Leather (tannery, 90-day batch): no item id, no buyer. Coal/iron/gold/
silver (MineralResourceKind): assayed oz/ton or pct-fe; NO ore/coal/metal
item id or sale path; no smelter/refinery; forge consumes iron + coal with
NO source in code. Nails: forge run counts, not a SKU. Doctor:
`medicine-dose` / `wound-dressing` (1 per dispense), stock
`medicine-stock`; no producer/supplier, no price. Construction kinds
Lumber/Hardware/Masonry/Paint/Glass/Roofing; freight ids
`construction-lumber`, `construction-glass`; hardware lots hold nails,
hinges, latches, strap iron. Tool ids `saw`, `hammer` appear only as crew
tool examples.

## E. Feed SKUs

| id | unit | package / conversion | produced by | consumed by | price |
|---|---|---|---|---|---|
| store_horse_feed_sack | sack | 2 feed units per sack (bootstrapper registers FeedUnitsPerStoreUnit 2/1) | Millbrook fixture (finite 20-sack order) | draft animals via HorseFeedStockAdapter | 15c/sack |
| bakery_flour_sack | sack | sack -> `bakery-flour` lb on receipt (Millbrook adapter); Millbrook order unit "10 lb sack", 10 inventory units + 18 bakery units per order | Millbrook Grain Co. | bakery | Millbrook order price 48c/sack; BakeryProfile 58c/sack |

## F. Service and equipment items (made to order, not stocked SKUs)

Tailor garments `tail.shirt` / `tail.trousers` / `tail.coat` / `tail.dress`,
service `tail.mend`; materials `tailor-cloth` (yards, NO producer in
code) + `tailor-notions`. Barber consumables `barber-soap`, `barber-linen`.
Wheelwright spares `wagon-wheel`, `wagon-axle`. ToolKitCatalog sets
(mason-kit, tailor-hand-kit, barber-kit, doctor-bag, wheelwright-kit,
stable-grooming-kit) are durable equipment, NOT tradeable SKUs.

## G. Orphans — CORRECTED

The first version listed 25 template ids as orphans. Eighteen of them ARE
defined in the store asset (section A): beef, bacon, cheese, basic_cloth,
flannel, workwear, boots, blanket, hat, candles, lantern, thread, buttons,
nails_hardware_lot, hand_tool, farm_tool, patent_remedy, medicine_bottle.
They are real store items with units, prices, and stock. What remains
truly orphaned (named in code, no definition anywhere): the smithy
template ids `8d-cut-nails`, `hinges`, `straps`, `brackets`, `hooks`,
`simple-latches`, `bolts`, `pins` — suggestions only, no unit, price,
producer, or supplier. `beef` also appears as a Ranch profile
description; `cheese`/`meat` as substring checks in delivery routing.

## H. Category ids in use

Store asset (authoritative): `staple_food`, `meat`, `clothing`,
`household_goods`, `tools_hardware`, `medicine_remedies`. Household
catalog adds `luxury_goods`, `fuel_wood` (as categories). Also in code:
`animal_feed` (horse feed), `lumber` (acquisition default),
`produce_general` / `produce_<slug>` (open-ended), `freight_service`
(revenue, NOT goods). `ItemCategoryDefinition` defines only the shape
(markup default 1.15).

## I. Existing suppliers (what can actually supply the store today)

- **Millbrook Grain Co.** (`millbrook_grain_co`, `MillbrookGrainCo.asset`,
  FULLY READ): `PickupRouteMilesFromTown: 50`. Inventory (lb): flour
  sacks 230 (order unit "10 lb sack", 10 inventory units + 18 bakery units
  per order, 48c/order); wheat grain 512; sugar 36; yeast 23 (last three:
  no order unit, price 0 — not orderable as-is). Production weekly
  (min/max, target reserve): flour sacks 40–80, reserve 460; wheat grain
  64–128, reserve 1024; sugar 4–10, reserve 72; yeast 3–7, reserve 46.
  Plus the finite 20-sack horse-feed order at 15c/sack (scenario
  endowment, exactly once) and the flour freight custody adapter.
  Millbrook is a grain company at 50 miles — the brief's Ashford distance
  — not a general supplier.
- **Bakery** (local): bread loaves via the wholesale driver (2c wholesale
  / 5c retail caller evidence).
- **Creamery**: buys milk at 3c/unit (a demand outlet, not a store supplier).
- **Fuel dealer** (local, `FuelDealerProfile.asset`): fuel bundles.
- **Off-map catalogs**: `OffMapSupplierCatalog` /
  `OffMapNamedSkuProcurementService` are fully generic (caller-supplied
  SKUs/prices, no fixed list, no concrete supplier outside tests).
  `OffMapAidBusinessDefinition` is the shape a real off-map supplier takes.
- **Weekly off-map reorder**: sourceless by design; a fallback, not a supplier.
- **Freight rate card** (`DevelopmentTownConfiguration.asset`): base
  service fee 25c, 1c/mile, round trip charged. The "25c + 1c/mile" figures
  are the opening freight company's authored rates, not universal law.

## J. Coverage assessment for P3 (what can be placed, what cannot)

Eligible merchandise = real tradeable goods with a definition: the store's
27 items (all priced), bakery/restaurant code items, feed SKUs. The
definition gap is CLOSED for the store catalog — every one of the 27 has a
unit, a price band, starting stock, and producer tags. The gap is SUPPLY:
only flour (mill/Millbrook), bread (bakery), fuel_wood (fuel dealer), and
lamp_oil (convertible at retail since Slice 10, supplier still freight)
have any supply path in code. The other 23 store items are priced
definitions waiting on a supplier — that is the P3 build, and the asset's
producer tags say what kind of business each needs (ranch/butcher, dairy,
tailor, tinsmith, blacksmith, doctor, freight).

- **Placeable in P3 without inventing data:** all 27 store items, priced
  from the asset's bands (a regional price inside the band, non-identical
  across towns, is calibration, not invention). Farm kinds can be supplied
  as lots by farm/mill/dairy businesses but need item ids before they can
  be retail SKUs.
- **NOT placeable without new definitions (report as exceptions, never
  fabricate):** milk/butter/cheese as retail items (no item ids — note the
  asset DOES list `cheese` as a store item at 15c/lb from dairy; the gap
  is the dairy CHAIN has no item id minting it, not the store side);
  fresh meat as a distinct retail id (asset sells `beef`/`bacon` by the
  lb; the chain mints only carcass fractions); eggs (comment-only);
  iron/coal/ore (no item ids, no source); wool/leather as retail goods
  (produced, no buyer, no retail id); medicine doses as retail stock
  (doctor stock only); `tailor-cloth` (no producer); the 8 smithy
  template ids (section G); rolls/pies at the store (deliberately
  unmapped — bakery-only); fuel offcuts as bakery fuel (decision pending).
- **Package gaps that stay open:** potato sack weight and salt bag weight
  are unauthored in the asset too — a P3 business cannot honestly sell
  them by weight until the asset (or Kennedy) declares the package size.

## K. What is still missing (honest limits)

- Unity scenes (`.unity`, 0 in repo) and prefabs: no rendered town, no
  storefront, no business interiors can be audited or built from this
  repo. Codex owns scene/prefab integration.
- `DevelopmentTownConfiguration.asset` opening town: general-store
  (25000c), bakery (17500c), fuel-dealer (20000c), freight-company
  (10000c, rate card 25c + 1c/mile round trip); cash targets 20000c each;
  opening store inventory bread 12–24, flour 4–8 sacks, potatoes 4–8,
  sugar 5–10, coffee 3–6, tea 2–4, salt 4–8. Households: Baldwin-Ooms,
  Pierce, Boniecki, ethan-tia, Shane Bryan.
- The 18 asset items without household definitions (section A): how a
  household receives one is a representative-item mapping question for
  Unity verification.
- The store asset's `producerTags` are tags, not suppliers: `[freight]`
  on 20 of 27 items means "comes by freight from somewhere" — the
  "somewhere" is exactly what P3's Ashford/Redfield businesses supply.
