# Muse cloud handoff — Pass XL continuation

**Project:** Land & Ledgers  
**Repository:** `KenB-O/Land-and-Ledgers-Scripts`  
**Branch:** `integration/pass12-unity-readiness`  
**Unity target:** 6000.4.2f1 HDRP  
**Handoff date:** 2026-11-01  
**Starting source tip before this checkpoint commit:** `3a1a6078218f2eec74e0af63724ba74f752a91f9`

## Copy/paste task for Muse

Continue the unfinished Pass XL work from the latest fetched `integration/pass12-unity-readiness` tip. This Git repository is the nested `Assets/Scripts` repository. Fetch first, inspect the checkpoint commit and all current source/tests, and preserve its history. Do not merge to `main`, force-push, reset, or assume the cloud has access to the local Unity Editor, `Assets/Main Scene.unity`, the complete authored project, local saves, or the XML files listed below. Those local-only results are evidence supplied for context, not artifacts hosted in this repository.

This handoff is for substantial implementation, not another broad audit. Choose one narrow, high-value end-to-end economy path, implement it through existing authorities, add focused regression tests, and commit the work on the integration branch (or a clearly named child branch if required by the cloud workflow). Report exact files, APIs, assumptions, and tests. Do not claim Unity scene wiring or PlayMode success from C# tests alone.

### Current verified state and honest limitations

Local Unity 6000.4.2f1 runs completed before this handoff:

- The opt-in Main Scene six-person fixture test passed 1/1. It verified six distinct named Person records and visible actor bindings, three Households, three Businesses (General Store, Bakery, Fuel Dealer), three houses, three commercial buildings, and the Town Hall. The fixture toggle was temporary and the authored scene was restored byte-for-byte. This verifies startup identity/visual binding only.
- The opt-in six-person authoritative-clock run passed 1/1 for 30 days. It did **not** demonstrate functioning autonomous commerce: at opening and closing, all three Businesses had 0 cash, 0 stock, 0 Tasks, and the three Households held 750 cents total. By day 30 it logged 0 meals eaten, 12 missed, and 0 purchases; the horse had no feed stock registered for its owner. Treat this as a failed economic acceptance outcome despite the passing calendar test.
- The ordinary First Ledger 30-day test passed 1/1 as a clock/driver horizon. It also had no meaningful Business stock/sales or completed Tasks by closing; household needs exhausted initial provisions and purchases did not start.
- A fresh positive General Store sale integration test failed 0/1 because the real store has no item-level inventory acquired through a connected procurement path.
- EditMode: Bakery staged production chain 10/10; freight production driver 7/7; daily needs 6/6; household shopping loop 22/22; Bakery/Fuel Dealer unauthorized weekly conversion guard 1/1.
- The entire 96-test `ConstructionInputSupportTests` class did not complete before the 1,800-second Unity timeout. It has no test verdict/XML and must not be reported as passed or failed.
- An interim source change blocks Bakery and Fuel Dealer from the old generic weekly category converter. Keep this containment unless and until a real Task-backed, input-backed production path is connected. Do not restore fixed-ratio weekly output.

Fresh local XML files, which are **not in this repository**, are under:
`C:\Users\kenne\Documents\LandLedgers_PassXL_Recovery_20261031\Resume_20261101\`

Relevant filenames: `SixPersonVisuals_AfterGuard.xml`, `SixPerson30Day_AfterGuard.xml`, `MainScene30Days_AfterProductionGuard.xml`, `GeneralStoreSale_Current.xml`, `BakeryStagedProductionChain_Final.xml`, `FreightProductionDriver_Final.xml`, `DailyNeedsService_Final.xml`, `HouseholdShoppingLoop_Final.xml`, and `BakeryFuelGuard_Final.xml`.

### Governing design references

The v3.0 `Planning Sources` package is in this Scripts repository after the checkpoint. Canonical Bible XXXI and Technical Implementation Bible XIV are the governing originals. Consolidated v0.3 documents are navigation aids, not replacements. First Vein playtest material is conceptual evidence, not proof of runtime behavior.

Technical XIV expressly models external ordering as discovery/evidence → order and credit terms → payment/remittance → supplier fulfillment → freight/shipment → local delivery → receipt/inventory. An order must not immediately add stock. Canon XXXI distinguishes owner capital, intercompany advance, and bank loan; scenario starting assets must be explicitly defined. Credit shifts timing and risk and must be a real obligation, not free cash.

### Recommended first implementation slice: named retail goods through a real shipment and sale

The pooled category stock in `BusinessRuntimeState` cannot be treated as an individual SKU. The General Store currently exposes an item offer only when an authored category maps to exactly one SKU; this refusal is correct for pooled multi-SKU categories. Do not weaken that guard or label aggregate `staple_food` stock as flour.

Existing candidate authorities discovered locally:

- `ExternalMarketAuthority` has named counterparties, finite supply, prices, transit days, external purchase receipts, arrival resolution, and boundary cash reconciliation. Its caller must settle local cash/liability and deliver stock at arrival.
- `GenericInventoryLot` and `GenericBusinessConfiguration.Inventory` model product-specific stock and provenance, but General Stores still route through legacy category stock. Switching a path flag alone risks two concurrent stock authorities.
- `ManagerProcureExecutor` uses real buyer/supplier settlements and records shipment state, but currently does not capture source-lot allocations on supplier consumption.
- `HouseholdShoppingLoop` settles a purchase and records physical carriage/receipt. Seller source-lot ancestry is not carried through to the Household lot.
- `GenericRetailSaleAuthority` has rollback concerns: restoring quantity through a generic add may lose original lot identity/provenance.

Build a single SKU slice that answers these questions with tests:

1. Which authority owns sellable SKU quantity for a General Store?
2. What named supplier and finite stock fund the offer?
3. What real local cash payment or explicit trade-credit obligation is recorded at order acceptance?
4. How are transit, freight, receipt, and duplicate/rejected delivery handled idempotently?
5. Which source lot allocations are consumed, and how is ancestry preserved through store stock, sale, household carriage, and final receipt?
6. Can household and seller settlement fail atomically without duplicating cash, goods, or lots?
7. What persists across save/load?

Start with one item and one Business, not a new generalized inventory architecture. Preserve existing category systems for unaffected Businesses and prevent double-counting while migrating this path.

### Next dependent slices (do not fake them)

- Bakery: the existing `BakeryStagedProductionChain` passes isolated tests and uses `TaskAuthority`, work budgets, due events, reservations, input lots, and staged output. It has no Main Scene owner/driver, no active-save DTO wiring, and no transfer from `BreadShelf` to the Business SKU inventory. Do not advance its Tasks through a second labor path. Begin a batch only from received, provenance-bearing flour/notions/fuel lots, then transfer actual output once.
- Fuel Dealer: no complete Business-bound Task → finite identified tree depletion → tools/rights → wood lot → inventory/sale path is connected. Never restore the old fixed `slabs_offcuts` ratio or weekly quota as a substitute.
- Households: make actual affordable purchases from real offers, physical receipts, and meals that consume household stock. Preserve unmet needs when no stock, money, or valid route exists.
- Animal care: register the proper owner-specific feed source; distinguish missing registration from actual shortage. Do not grant free feed.
- Save/load: include every newly owned order, shipment, lot allocation, active production stage, and obligation in the existing persistence authorities. Add duplicate-restore tests.

### Scope, safety, and reporting

- Keep the six-person profile opt-in for development; do not replace ordinary population/scenario settings.
- Do not lower First Ledger's equity goals, inject money/stock/meals, fake output, fabricate demand, auto-complete Tasks, or create instant deliveries to make tests pass.
- Do not make an Editor/scene claim without access to the complete Unity project and an actual Unity run. This cloud checkout alone cannot verify serialized Main Scene wiring.
- Preserve backward-compatible save loading and current IDs/GUIDs.
- Run the cheapest relevant tests while iterating. On final source, report tests that actually completed, failures, timeouts, and untested scene/runtime behavior separately.
- Commit only the work you performed and can explain; leave `main` untouched. The local tree had substantial changes when this checkpoint was prepared, so use the pushed checkpoint as the base and do not rewrite it.

**Acceptance for the first slice:** one paid or explicitly credited, named-supplier shipment of one real SKU arrives after modeled transit, creates one provenance-backed store lot exactly once, a funded household buys it through the real shopping authority, cash/obligation and lot quantities reconcile, and a save/restore replay duplicates neither shipment, sale, nor goods. If that cannot be completed safely from available authorities, commit a focused investigation/test that proves the precise missing dependency—do not report it as functional.
