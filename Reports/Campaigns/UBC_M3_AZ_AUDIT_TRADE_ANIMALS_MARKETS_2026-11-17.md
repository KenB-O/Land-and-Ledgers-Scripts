# UBC M3 — A-Z Audit, Third Slice: Trade and Import, Animal Services, NPC Decision and Market Layer

Date: 2026-11-17. Base: integration branch `muse/universal-business-construction-2026-11-17` at `9957be0a` (Merge D2 blacksmith construction hardware). Worktree `/home/hatch/ll-ubc-m3`. Harness baseline in this tree: **5087 passed, 2 failed, 5089 total** — the 2 failures are the E2×D2 integration break repaired here as M3-R1 (see below). After the repair the suite is green (see Verification).

M1 covered population, household, business formation, retail, tenancy, boarding, housing. M2 covered manufacturing, finance, contracts, construction completion, occupancy. This slice audits the remaining domains: (1) trade and import routes, off-map supply, freight contracts, the import authorities D3/F1 use; (2) animal services, vet and farrier production drivers, D1/S4 territory; (3) the NPC decision and market layer, price discovery, demand signals, the entrepreneurship observation/investigation/formation chain B2 is wiring.

Method, as in M1/M2: every finding gives file+method, caller/callee, expected vs observed, a reproduction, and a root cause. "Zero production callers/constructions" claims were verified with fully-qualified-aware greps (`new .*<Type>`), because the freight driver is constructed fully-qualified in `SimulationDrivers.cs` and an unqualified grep misses that shape.

## Headline

No live break in the trade/import money path: the flour, notions, and lumber-yard supply routes are genuinely wired and running. The two live systems that look wired but are dormant are **ImportService** (a whole parallel import authority with zero production use, while ten production files' doc comments still claim it is the reorder path) and **the entire animal-services domain** (vet, farrier, herd health, medicine supply, disease service — fully implemented, never driven, and the live simulation has no animal-health demand model at all). The market layer has no price authority and no driver that ever reprices anything. The freight contract book is unused but non-blocking by design. The top confirmed break in this tree was not in the audited domains at all: the E2×D2 interaction left the only 2 failing tests in the suite, repaired here as M3-R1.

## Findings

### F-M3-1 — ImportService is a dead parallel import authority; the live route is the standing-order procurement chain

- **Files:** `Assets/Scripts/Domains/Economy/Trade/ImportService.cs` (197 lines), `Assets/Scripts/Domains/Economy/Trade/ImportCatalog.cs` (113 lines).
- **Caller/callee:** `ImportService.PlaceOrder` / `ReceiveDueImports` have zero production callers. Zero production constructions of `ImportService` (verified including fully-qualified form). Zero references in `SimulationSystemsHub.cs` and zero in `Series2Pass9Save.cs` — it is not hub-owned and not saved. Its only constructions are in `Tests/Editor/Economy/ImportOrderTests.cs` (3 constructions).
- **Expected:** one import authority, used by the supply routes that need off-map goods.
- **Observed:** two import systems exist. The live one is the standing-order procurement chain: `Series2FlourProcurementDriver` → `OffMapNamedSkuProcurementService` → `ExternalMarketAuthority`, with `OffMapQuoteToOrder.PlaceOrderFromQuote` called by `Series2ProcurementDriver.cs:148` and `Series2NotionsSupply.cs:61`; `BakeryWholesaleDailyDriver.cs:502` places the Millbrook flour reorder; `SimulationDrivers.cs:7289` resolves shippable lots; `LumberYardSupply.cs:44` calls `ImportCatalog.Get`. `ImportService` itself is never touched by any of that.
- **Doc drift (the sharp edge):** 14 production files reference `ImportService` in doc comments only. Eight state plainly that a commodity "reorders through ImportService": `BakeryOvenFuel.cs`, `BarberConsumableLots.cs`, `BoardingHouseFoodLots.cs`, `DoctorMedicineLots.cs`, `HotelConsumableLots.cs`, `RestaurantFoodLots.cs`, `RestaurantKitchenFuel.cs`, `TailorClothLots.cs`. Six more name it (`BakeryFlourLots.cs`, `BarberConsumablePolicy.cs`, `DoctorMedicinePolicy.cs`, `HotelFoodLots.cs`, `RestaurantResupply.cs`, `TailorClothReorderPolicy.cs`). Any reader or lane following those comments wires to a dead authority.
- **Reproduction:** `grep -rn "new .*ImportService" --include='*.cs' Assets/Scripts | grep -v /Tests/` returns nothing. Run a live flour reorder and trace it: it goes through `Series2FlourProcurementDriver`, never `ImportService.PlaceOrder`.
- **Root cause:** two import systems were authored in parallel; the procurement-driver chain won in production and `ImportService` stayed as the test-only exemplar. D3 is about to make it live for nails/iron (`ImportService.PlaceOrderForBusiness`), which resolves the split for those two goods only — the eight doc comments stay wrong for everything else.
- **Disposition:** recorded, not repaired in this slice. The repair is a decision, not a code fix: either delete `ImportService` once D3's consumers are the only users and correct the eight comments to name the live chain, or declare it the single import authority and migrate the standing-order chain onto it. Do not silently pick one. The eight doc comments should be corrected whichever way Kennedy rules.

### F-M3-2 — Animal services are fully implemented and entirely undriven; the live simulation has no animal-health demand model

- **Files:** `Assets/Scripts/Domains/Economy/AnimalServices/VetServices.cs` (`VetService`, `FarrierService`), `VetHerdHealth.cs` (`VetHerdVisitService`, `VetHerdVisitSchedule`), `VetMedicineSupply.cs` (`VetMedicineSupply`), `LivestockDiseaseService` (hub-owned).
- **Caller/callee:** zero production constructions of `VetService`, `FarrierService`, `VetHerdHealth`, `VetMedicineSupply` (verified including fully-qualified form). Zero production callers of `VetService.CallOut` (`VetServices.cs:167`), `FarrierService.FitShoes` (`:358`), legacy `FarrierService.ShoeHorse` (`:290`), or `VetHerdVisitService.ConductVisit` (`VetHerdHealth.cs:141`). No `DriveVet` or `DriveFarrier` exists in `SimulationDrivers.cs`. No shoe-wear or hoof-condition decay driver exists anywhere; `HoofNeed` is referenced only inside `VetServices.cs`. `LivestockDiseaseService` is hub-owned but never ticked — its only production references are the hub lazy property (`SimulationSystemsHub.cs:1493` and `:1596`). S4 already registered `VetService` and `FarrierService` as unwired in `Docs/UnwiredSystems.md`.
- **Expected:** horses get shod (D1 forged individual shoes for exactly this), sick animals get treated, herd health gets visited.
- **Observed:** D1's farrier fitting is reachable only from tests (`HorseshoeFittingTests.cs`). The blacksmith forges shoes and D3 delivers hardware, but no production code ever fits a shoe to a hoof, treats an animal, runs a herd visit, or lets an animal fall ill. An animal with a hoof need stays unshod forever; a sick animal is never seen.
- **Reproduction:** in a live run, create a horse with a hoof need and wait — no driver ever picks it up. `grep -rn "new .*FarrierService" --include='*.cs' Assets/Scripts | grep -v /Tests/` returns nothing; same for `VetService`, `VetHerdHealth`, `VetMedicineSupply`.
- **Root cause:** the animal-services domain was authored as a complete library with no production driver ever wired, and the live simulation has no animal-health demand model at all — there is nothing for a driver to respond to. Wiring a driver alone would change nothing; the demand side (shoe wear, illness, herd-visit scheduling) does not exist.
- **Disposition:** recorded, not repaired. A repair here is a feature, not a fix: it needs an owner decision on whether animals should need vet/farrier care in the live simulation at all, and if so a demand model first (shoe wear, disease incidence, visit scheduling), then a driver. `VetHerdVisitService` already carries a save DTO (`CaptureSaveDto` / `LoadFromSaveDto`, `VetHerdHealth.cs:269`/`278`), so persistence is ready when a driver exists.
- **Related note, out of slice:** `DoctorPracticeRuntime` (human medicine) is also never constructed in production — the same dormant shape as the animal services. Not audited here; flagged for a later slice.

### F-M3-3 — Off-map supplier catalog is empty in production; the general-store quote path is wired but starved

- **Files:** `Assets/Scripts/Domains/Economy/Businesses/GeneralStore/OffMapSupplierCatalog.cs` (`QuoteAll`, `RegisterSupplier` at `:91`), `OffMapQuoteToOrder.cs` (`PlaceOrderFromQuote`).
- **Caller/callee:** `QuoteAll` has zero production callers. `RegisterSupplier` has zero production callers. The quote path itself is live — `OffMapQuoteToOrder.PlaceOrderFromQuote` is called by `Series2ProcurementDriver.cs:148` and `Series2NotionsSupply.cs:61` — but it can only quote suppliers registered in the catalog, and in production none ever are.
- **Expected:** the general store (and other buyers) can get quotes from real off-map suppliers.
- **Observed:** the production catalog is empty until someone registers real suppliers. The `OffMapQuoteToOrder` doc already records this as finding F37.
- **Reproduction:** a general-store restock asks the catalog for a quote and finds no suppliers.
- **Root cause:** no supplier-registration authority runs in production. Registering real suppliers (Ashford General Mercantile exists as an F5 supplier for furniture; Millbrook Grain Company for flour) is a data/owner task, not a code defect — the code to quote and order works.
- **Disposition:** recorded, not repaired. Do not invent suppliers. Kennedy/Codex register the real ones; the code path is ready.

### F-M3-4 — No NPC pricing decisions: no price authority, no driver ever reprices anything (B1-7 confirmed)

- **Files:** no `PriceAuthority`, `MarketPrice`, `DemandSignal`, or `PriceDiscovery` class exists. `SetPriceSchedule` exists for bakery and restaurant with zero production callers. `GeneralStoreRuntimeManager.cs` (`GetCategoryMarkupMultiplier`, `CalculateEffectiveCategoryMarkupMultiplier`, `storeMarginAdjustment`).
- **Caller/callee:** `storeMarginAdjustment` has zero setters outside `GeneralStoreRuntimeManager.cs` — no driver ever moves a margin. `CollectRealDemand` exists in `SharedBusinessRuntimeManager.cs:679`, but `weeklyDemandIntents` are consumed only in a summary (`:1158-1159`) and in procurement `ResolveDesiredUnits` (`:5426`). Demand is collected; it never feeds a pricing decision.
- **Expected:** prices respond to supply and demand, or at least some NPC revisits a price.
- **Observed:** General Store pricing is cost-plus category markup plus a `supplyDemandPressure` term, all from config. Nothing in production ever changes a price after opening. Run 60 days and log every price change: only opening prices, never adjusted.
- **Reproduction:** grep for a production caller of any price-setting method on a live business — none exists.
- **Root cause:** pricing was authored as config plus a manual schedule setter, with no decision driver. This is B1 finding B1-7, confirmed here from the market side.
- **Disposition:** recorded, not repaired. Repricing is a policy decision (what rule, what bounds, who may move a price), not a bug fix.

### F-M3-5 — Freight agreement book is unused, but non-blocking by design

- **Files:** `Assets/Scripts/Domains/Economy/Businesses/Freight/FreightServiceAgreements.cs` (`FreightServiceAgreementBook`), `FreightProductionDriver.cs` (agreement check at `:236-244`).
- **Caller/callee:** zero production code creates an agreement — no `new FreightServiceAgreement`, `AddAgreement`, or `RegisterAgreement` outside the book itself. The driver is live in production (`SimulationDrivers.FreightProduction` lazy property at `:6962`, `DriveFreightCompanies` at `:7010`, `FreightShipmentService` underneath) and reads the book via `FindAgreement` / `MarkFulfilled`.
- **Expected:** freight runs under agreed contracts with an enforced agreed price.
- **Observed:** the agreement check is guarded by `if (!string.IsNullOrWhiteSpace(freightServiceAgreementId))`, so every shipment that names no agreement id skips the check entirely. Freight runs; no agreed price is ever enforced against a shipment.
- **Reproduction:** any live freight shipment carries no agreement id, so the agreement check never engages.
- **Root cause:** no agreement-creation flow was ever built — no negotiation-to-agreement step in production (`DriveMillbrookFreightNegotiation` exists for the Millbrook path but creates no agreements). The guard makes the missing feature silent rather than blocking, which is the honest shape for an unbuilt feature.
- **Disposition:** recorded, not repaired. Creating agreements needs a negotiation/acceptance flow; that is a lane-sized feature, not an audit repair.

### F-M3-6 — Generic supplier-comparison utility is dormant; no decision driver consumes it

- **Files:** `Assets/Scripts/Domains/Economy/Logistics/SupplierComparison.cs:710` (`SupplierComparison`).
- **Caller/callee:** zero production callers of any `SupplierComparison` member. The only live comparison is the narrow `BakeryFlourSupplierComparison` (flour only).
- **Expected:** buyers compare suppliers on price/terms before ordering.
- **Observed:** the generic comparator exists and is never used; the live buyers that do compare (flour) use a bespoke comparator.
- **Root cause:** comparison utilities were authored without a decision driver to call them — same pattern as F-M3-4.
- **Disposition:** recorded. Folds into the pricing/decision-driver question (F-M3-4).

### F-M3-7 — Entrepreneurship chain: investigation walks away at the finder gate (B1-1, owned by B2)

- **Files:** `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs:4232` (`DriveEntrepreneurshipWeek`), comment at `:4248`.
- **Status:** `PremisesFinder` and `EquipmentFinder` remain unbound by design; every production investigation walks away and `FormBusiness` is unreachable. B1 recorded this as its top break (B1-1); B2 is implementing the commercial-lease authority and market wiring now. Not re-audited in depth here; recorded so the chain's state is on this slice's record. Do not touch B2's work.

## Verified live in this slice (no finding)

- Bakery/flour reorder chain: `BakeryWholesaleDailyDriver.cs:502` `PlaceReorder`, `SimulationDrivers.cs:7289` `FindShippableLot`, Millbrook flour at 48c/sack.
- Notions supply and the general procurement driver (`Series2NotionsSupply`, `Series2ProcurementDriver`).
- Lumber-yard supply path (`LumberYardSupply.cs:44` `ImportCatalog.Get`).
- Freight companies driver: `SimulationDrivers.FreightProduction` lazy property (`:6962`, fully-qualified construction — an unqualified grep misses it), `DriveFreightCompanies` (`:7010`), `FreightShipmentService`, per-business runtime registration (R-04 payer resolution).
- Weekly NPC business lifecycle evaluation: `DriveNpcBusinessLifecycleWeek` (`:4220`), driver constructed at `:4277` — B1's 8-week close path is live.
- Demand collection (`CollectRealDemand`, `SharedBusinessRuntimeManager.cs:679`) and the store pricing config path.
- Entrepreneurship observation and investigation reach the finder gate, then walk away (F-M3-7 / B2).

## Repair implemented — M3-R1: E2×D2 integration break in the construction hardware end-to-end tests

**The break.** Baseline in this tree (integration tip `9957be0a`): **5087 passed, 2 failed, 5089 total**. The only 2 failures were both in `Tests/Editor/Economy/ConstructionHardwareEndToEndTests.cs` (D2's Scenario 4 tests):

- `Scenario4_SmithHardware_BuildsSettlerCabinEndToEnd`
- `Scenario4_EmptyYardHardware_RoofRefuses_NoDoorWithoutHardware`

Both failed identically: `DrivePackageToComplete` committed a fixed 360 minutes once and then asserted `AdvancePackage` returns null (complete). E2 recalibrated settler-cabin phase labour to 1200 / 2100 / 2100 person-minutes, so the package was nowhere near complete: `AdvancePackage` refused with "package 'wpkg-cproj-0-1' has 600 of 1200 labor minutes — work is real." The D2 tests were written against the old 120-minute phases; the E2/D2 merge kept E2's labour values in the presets but nothing updated the test's labour-driving helper. The merged line was red because of this interaction alone.

**The repair (test-only, one file).** `Tests/Editor/Economy/ConstructionHardwareEndToEndTests.cs`, `Chain.DrivePackageToComplete`:

- Loop `CommitLabor` + `WorkCommittedLabor` one worker-day (600 minutes, the E2 day budget) at a time until `package.LaborMinutesCommitted >= package.LaborMinutesRequired`, exactly as `CommitLabor`'s own doc directs ("The caller re-commits on later days until the package is fully worked; partial work persists on the package").
- The day is a shared `ref int` cursor across the three packages in a test, so the worker is never double-booked: sills take days 11–12, walls 13–16, roof 17–20 in the positive test. `AdvancePackage` is called on the last worked day.
- Both test methods now carry one `int day = 11` cursor; the roof-procurement and `CompleteProject` calls in the negative test use the cursor day.
- No production code touched. No D2 assertion weakened: the 26,005c end-to-end money pin, the 13,600c negative-test pin, the 9-hardware-unit pins, the roof-refusal pins, and the provenance pins are all preserved verbatim. E2's labour values untouched. `ProductionDesignPresets.cs` and `NpcConstructionDesignsDef011Tests.cs` untouched, as instructed.

**Verification.** Full harness run in this tree after the repair: **5089 passed, 0 failed, 5089 total** (logs `/home/hatch/ll-ubc-m3-build1.log`, `/home/hatch/ll-ubc-m3-run1.log`). FAIL-set delta vs this tree's baseline: exactly the 2 failures removed, zero new failures. The two Scenario 4 tests pass.

## Open owner questions (recorded, not decided)

Carried from earlier slices, untouched by this slice:

- **F-M2-1** — construction progress billing never settles in production; needs an owner decision on stage acceptance.
- **F-M2-8** — no inspection/acceptance stage anywhere in construction; possibly by design.
- **I1 F2** — off-map members eat from the larder; options (a)–(d) are Kennedy's call.

New from this slice:

- **Animal care in the live simulation** — should animals ever need vet/farrier care? There is no demand model (no shoe wear, no illness), so wiring a driver alone would do nothing. Needs a design decision before any lane builds it.
- **The single import authority** — `ImportService` vs the standing-order procurement chain. D3 makes `ImportService` live for nails/iron; decide whether it becomes the single authority (migrate the chain) or stays a two-good authority (correct the eight doc comments to name the live chain).
- **NPC repricing** — B1-7 / F-M3-4. What rule, what bounds, who may move a price. A policy decision, not a repair.
- **Freight agreements** — F-M3-5. Whether freight should run under agreed contracts, and what the negotiation/acceptance flow is.

## Files changed

- `Assets/Scripts/Tests/Editor/Economy/ConstructionHardwareEndToEndTests.cs` (M3-R1 repair, test-only).
- `Reports/Campaigns/UBC_M3_AZ_AUDIT_TRADE_ANIMALS_MARKETS_2026-11-17.md` (this report; repo-root reports carry no `.meta`, per E1/D1 precedent).

No production source changed. No new `.cs` files, so no new `.meta` files were needed. Nothing pushed, nothing merged to main.
