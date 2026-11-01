# Pass VIII Worker 9 — Water and Survival Verdict

Date: 2026-10-13. Worker: Muse Pass VIII, Worker 9 (Scenario, Persistence, Water).
Scope: brief sections 13/14. Governing: Canonical Project Bible Rev XXXI,
Technical Implementation Bible Rev XIV; proposals 001-105 approved, 106-145 not.

## Verdict

**Defer the policy-dependent portion. No drinking-water implementation is
made in this pass.** The governing Canon establishes that water is real but
does not determine the gameplay rules a genuine implementation needs, and
the authorization rule for this task forbids inventing the missing
physiological, legal, and calibration numbers. What follows is the evidence,
the authority inventory, the precise open policy questions with options, and
the exact Canon decisions that would unblock implementation.

## 1. Canon evidence

All references below are to the v2.1 2026-10-08 docs package
(`02_Latest_Consolidated_Bibles_v0.3`, Consolidated Canon v0.3 Provisional).

**Water is real in Canon (established doctrine):**

- Household needs: "Food, heating fuel, water, clothing, medicines, schooling
  and lodging consume actual resources" — water is a named household need,
  and "a household can endure, downshift purchases, negotiate arrears, move,
  seek work or use kin support as actual circumstances permit."
- Housing: "Space provides actual shelter, sleep capacity, privacy, warmth,
  cooking, storage, water, sanitation, safety and access. Suitability is
  evaluated for a particular household in a particular season at a price it
  can afford."
- Technology baseline: "established hand, animal, water, wind and steam
  technologies already exist" in 1870 — wells and water handling are
  period-plausible, not inventions.
- Animals: "Working animals need feed, water, rest, appropriate
  tack/harness, handling, treatment and ownership identity."
- Disasters: "Drought, grasshoppers, hail, blizzard, long winters, livestock
  disease, market shifts and new machinery alter actual yields, survival,
  reserves, routes, prices, finance and replacement needs."
- Businesses: "lodging uses real accommodation capacity, cleaning, heating,
  water, laundry and staff time"; saloons sell drink; meals consume real
  ingredients and food work.

**Water is NOT determined in Canon (the gaps):**

- No per-person daily drinking-water requirement (quantity, or variation by
  age, labor, season, climate).
- No health consequences of deprivation (stages of dehydration, work-capacity
  effects, disease from bad water, mortality). Canon is explicit elsewhere
  that exact biological curves are not universal Canon numbers (cf. the
  poultry-fertility passage); no water numbers exist at all.
- No water access or rights rules: who may draw from which source, well
  ownership, riparian claims, drought priority. This is exactly proposal 136,
  "Water availability vs water rights", which sits in the **not-approved
  106-145 batch** as PENDING with the instruction: "Do not import a genuinely
  new mechanic into Canon until explicitly approved."
- No drought calibration for household/commercial supply (drought exists as
  an agricultural disaster only).

## 2. Existing authority inventory (verified on disk)

| Concept | Authority | State |
|---|---|---|
| Horse hydration | `HorseCareDriver` water01 scalar -> `HorseAuthority.ApplyCare` | Scalar only; nothing physical |
| Regional water geography | `RegionalParcelAuthority.WaterAccess01`, `WaterRiskHolding` use | Geography/risk only |
| Weather and drought episodes | NX-2C route conditions, `AgriculturalRisk` drought episodes | Agricultural scope |
| Business water references | Boarding/restaurant/hotel kitchen+fuel, barber bath hot-water gate | Flags/gates, no quantities |
| Infrastructure capability flags | capability registry entries | Flags only |
| Household daily needs | `DailyNeedsService` (meals), `HouseholdNeedRegistry` | Meals implemented; water absent |
| Person health | `PopulationHealthState`, conditions with severity/duration | No water-driven conditions |

Missing (PASS7-002, unchanged): physical water goods, wells, hauling,
household reserves, person hydration, livestock consumption, commercial
water demand, sanitation, firefighting.

## 3. Why the existing authorities are not sufficient

A meaningful drinking-water implementation is a supply-and-consumption
chain: source -> haul -> store -> drink/cook/water-livestock, with shortage
consequences. The existing authorities support the *edges* (geography
scalars, animal scalars, drought episodes, meal execution) but determine
none of the chain's rules:

1. **Requirement rate.** How much does a person drink per day? A child vs a
   harvest hand? Summer vs winter? Any number here is physiological policy
   Canon has not set. A chain without a requirement is decoration.
2. **Consequences.** What happens when water is missing — reduced work
   capacity (like missed meals via `WorkCapacityMultiplier`), illness, death?
   At what deprivation level, after how long? Canon sets none, and this task
   explicitly forbids silently inventing precise mortality or disease rates.
3. **Access rules.** May a household draw from a neighbor's well, a creek, a
   town pump? Who owns a well? Proposal 136 (water rights) is pending and
   not approved — building access rules now would pre-empt Canon.
4. **No partial credit.** A "supply chain" that produces water nobody needs
   is dead inventory (violates the no-synthetic-inventory discipline); a
   consumption loop with invented rates is exactly the superficial isolated
   thirst meter this task forbids.

Therefore: no bounded implementation is feasible without Canon policy.
Implementing now would mean inventing the rules, not implementing Canon.

## 4. Options, documented precisely, for when policy lands

**Option A — Water access as a housing-suitability input (closest to Canon).**
Canon already says housing suitability evaluates water for a particular
household in a particular season. Implementation would score dwellings on
water access (own well / shared well / carry distance / none) and feed the
existing housing search/selection. Needs Canon: what counts as access, and
how it weighs against price and other suitability factors.

**Option B — Physical water good with a hauling chain.**
Well (building improvement or parcel feature) -> water-barrel good ->
household storage, hauled by person-time or wagon through the existing
logistics/freight authorities, consumed by drinking/cooking/livestock.
Needs Canon: quantities per use, container/weight handling, contamination or
spoilage rules, hauling labor norms.

**Option C — Person-level hydration with health effects.**
Daily requirement per person, tracked like nutrition (`p.nutrition`
pattern), missed water biting work capacity, prolonged deprivation causing
illness/death through the existing condition/mortality authorities. Needs
Canon: the full physiological table (requirement by age/activity/season,
deprivation stages, disease vectors, mortality) — the largest policy ask,
and the one this task explicitly blocks on.

**Option D — Livestock and commercial water demand.**
Per-head animal consumption through `AnimalRegistry` (replacing the scalar),
per-business demand (boarding kitchens, baths, laundries, livestock-heavy
operations) through the business runtimes. Needs Canon: per-head and
per-business rates, and drought curtailment priority.

Recommended sequencing when unblocked: A, then B, then D, then C — each
layer is playable and honest without the next, and C must never precede its
physiological policy.

## 5. Exact unblock criteria

Implementation may begin when Canon (not a worker) determines:

1. Daily drinking-water requirement per person, with stated variation
   (age, labor intensity, season) or an explicit "flat rate" ruling.
2. Health consequences of deprivation: stages, work-capacity effects,
   illness and mortality rules — or an explicit ruling that deprivation has
   no lethal/health effects in the simulation's scope.
3. Water access/rights doctrine (proposal 136 or its Canon successor):
   who may draw from which sources under what tenure.
4. Drought effects on household and commercial supply (beyond agriculture).
5. Whether water quality/contamination is in scope.

## 6. Other survival needs discovered

- **Sanitation** is named alongside water in the housing passage and has no
  authority at all; it shares water's deferral (it is arguably downstream of
  the water decision).
- **Heating fuel** exists (`BoardingHouseFuel`, `HotelFuel`,
  `RestaurantKitchenFuel`, fuel dealers). **Food** exists (meals, nutrition,
  reserves). **Clothing, medicines, schooling** exist as need-registry
  categories. No other survival need was found both absent and
  Canon-determined; none are implemented here.

## 7. Recommendation

Keep PASS7-002 deferred. Do not implement Options A-D until the section 5
decisions are recorded in Canon. The deferred register entry is updated to
point here. No code in this pass touches water mechanics.
