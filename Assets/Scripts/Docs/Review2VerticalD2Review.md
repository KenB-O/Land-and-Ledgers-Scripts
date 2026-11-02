# Review 2 Vertical Slice Pass D2 — Owner-Addable General Store Horse Feed

Pass D2 is the second vertical-slice pass after D1. Its first item
implements Kennedy's Nov 2, 2026 decision: Kennedy can sell horse
feed at the General Store; by default it is NOT an item the store
sells, but it can be added in, as any player should be able to add
items to sell. Before D2, `SimulationDrivers.ResolveHorseFeedStock`
returned null for every General Store-owned horse (farm and livery
were the only fed branches), so a store horse degraded even when
the store held feed-shaped stock.

## What changed

Production code, all caller-parameterised, no existing branch
touched except by appending a third one:

- `Assets/Scripts/Domains/Economy/Core/Review2StoreHorseFeed.cs`
  (plus its `.meta`, fresh GUID, never copied). `Review2StoreFeedItem`
  is the owner-add record (business instance id, category id, item
  id, display name, unit label, store units per feed unit).
  `AddFeedItem` validates, creates the category entry at 0 current
  stock only if absent, never creates stock, never sets a price,
  is idempotent on an identical re-add, and refuses a different
  item for the same store or a conversion of 0 or less (0 means
  "not supplied", never a silent 1:1). `FeedStockForBusiness`
  returns null with a diagnostic when no item was added;
  `FeedStockForHorse` applies only to business-owned horses.
- `Assets/Scripts/Domains/Economy/Farming/Integration/StoreHorseFeedStockAdapter.cs`
  (plus its `.meta`, fresh GUID). An `IHorseFeedStock` over a store
  runtime category: stock is category units divided by the
  caller-supplied conversion, and consumption is all-or-nothing
  at store-unit level — a shortfall takes 0 and leaves store
  units in place, never rounded up.
- `Assets/Scripts/Domains/Population/Review2HouseholdHorseFeed.cs`
  (plus its `.meta`, fresh GUID). The person-owned horse path:
  an `IHorseFeedStock` over a household inventory, consuming
  through `HouseholdInventory.Consume` (all-or-nothing, oldest
  lots first) at a caller-supplied household-units-per-feed-unit
  conversion, with the day index as a constructor parameter so
  the driver must construct it per day.
- `Assets/Scripts/Domains/Population/HouseholdInventory.cs`,
  additive only: `HouseholdItemCatalog.Get` now also checks a
  runtime dictionary, and `RegisterRuntimeItem` registers a
  caller-supplied definition, refuses to shadow a built-in id,
  refuses overwriting with a different definition, and is
  idempotent on an identical re-registration. Built-in items
  still win, and unknown items are still rejected by
  `HouseholdInventory.AddLot` and therefore by the shopping loop.
- `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs`,
  additive only: a `Review2StoreFeedRegistry` field,
  `RegisterStoreFeedItem` (the owner operation; refuses
  non-General-Store businesses and missing runtimes), and a
  third branch in `ResolveHorseFeedStock` after farm and livery
  that returns `Review2StoreHorseFeed.FeedStockForBusiness`
  when the store has an added feed item. Farm and livery
  branches are byte-identical; a store with no added item
  still falls through to null with CareDay's diagnostic.

## What the tests prove (Roslyn only, 8 new tests)

- A store horse with the added item is fed and does not
  degrade: 20 store units at 2 per feed unit = 10 feed units;
  the idle ration (ceil 1.2 = 2) draws exactly 4 store units.
- A store without the added item still resolves null with a
  diagnostic, and the horse degrades — the pre-D2 behavior,
  not phantom feed.
- `AddFeedItem` creates no stock, refuses a zero conversion
  and a conflicting item, and is idempotent on re-add.
- Partial store units (3 units against a 4-unit ration) take
  nothing and stay for the next day, never rounded up.
- A working ration (ceil 2.5 = 3) draws 6 store units, a
  same-day repeat draws nothing, and day 2 draws again.
- The store resolver returns null for a person-owned horse:
  the person path is the household adapter, not the store
  resolver.
- A person-owned horse buys feed through the normal shopping
  loop (runtime-registered household item, store port offer,
  HouseholdLedger cash, store cash port): 6 sacks at a
  caller-supplied 9c = 54c, buyer outflow equals store cash in,
  live pool 50 -> 44, inventory gains 6 lots, then CareDay
  draws 2 units (idle ration at 1 household unit per feed
  unit) and the horse does not degrade.

No feed item id, price, or unit is canon: every test passes
its own parameters, and no `.asset`, profile, or definition
was changed, so by default the store still sells no feed.

## Deliberately left open (Codex / later passes)

- Persistence: runtime item registrations and feed
  registrations are in-memory only in this pass and are NOT
  saved. Codex must persist them or re-register on load, or a
  restored world falls back to null/unknown-item with
  diagnostics, never phantom feed or a faked sale.
- Feed-need origination: the person-horse test constructs its
  shopping need by the caller. Who opens a feed need, at what
  quantity, and when, is a driver decision not proved here.
- The `SimulationDrivers` branch itself is not Roslyn-tested:
  the driver is a MonoBehaviour and is excluded from the
  harness, as in D1. The domain resolver it calls IS tested;
  Unity verification of the driver branch (registration call
  site, per-day household adapter construction, CareDay
  wiring) remains Codex/Unity AI work.
- The driver must supply the person-owned horse's household
  inventory to `Review2HouseholdHorseFeed`; no
  person-to-household mapping is guessed in D2.
- No fixture, profile-vs-definition authority resolution,
  price/rate, or other D2-forbidden decision is made here.

## Verification

- Main harness (fresh `harness-D2.csproj`, retargeted from D1
  plus the 4 new files): 115 passed, 0 failed (D1: 107; +8 D2).
- Phase H harness (fresh `harness-D2-phaseh.csproj`):
  86 passed, 3 failed — exactly the 3 known baseline
  failures (insufficient-funds trace, hardship missed-meals,
  restock chain 59 vs 60), unchanged by D2.
- New GUIDs checked via grep: each appears in exactly one
  `.meta`, none collides with any tracked GUID.
- No Unity compilation, Test Runner, PlayMode, scene wiring,
  or final Unity import GUID claim is made by this pass.
