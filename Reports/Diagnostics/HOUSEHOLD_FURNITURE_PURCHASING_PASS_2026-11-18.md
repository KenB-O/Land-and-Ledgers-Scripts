# Household Furniture Purchasing Pass 5 — 2026-11-18

## Scope and result

This pass connected the General Store's existing paid, finite supplier-restock authority to the first-wave wooden furniture products already described by its retail definition and the regional supplier registry. It does not seed inventory or change retail prices. This is a source integration step, not verification that a household completed purchase, freight, installation, or use in live gameplay.

## Confirmed source/runtime findings

- The loaded General Store definition supplies positive retail prices and positive reorder targets for the eight first-wave products: bed, table, chair, small/large cabinet, shelf, crate, and bedroll. The Editor runtime read-back on 2026-11-18 returned prices of 294c, 221c, 123c, 319c, 417c, 78c, 73c, and 59c respectively; authored reorder targets were 4/4/6/4/4/6/6/6.
- The regional business registry already authorizes finite Ashford supply and wholesale prices for those eight products. There is no configured cooking-stove offer or price. Stove purchase is therefore refused rather than assigned a guessed price or invented source.
- The development-town General Store opening inventory has no furniture lots. At the observed New Game startup its retail runtime started with zero furniture stock. The household-purchase path correctly needs a named available retail lot and cannot purchase from zero stock.
- The current production `FurnitureStorePurchaseDriver.TryPurchaseForHousehold` calls `FurniturePurchaseService.TryDeliver`, which loops loading, dispatch, full-duration transit and unloading synchronously from the daily driver before installation. This does not provide an observable, independently time-progressing live wagon delivery. It remains a confirmed blocker to the requested physical delivery verification.
- The carpentry sale path transfers title and household custody directly. The household daily driver can then install such workshop pieces from custody. The configured carpentry business currently lacks a confirmed commercial workshop premise, so this pass does not claim a physical Don Millward workshop sale or delivery.
- Cabinet/shelf installation maps to `StorageZone`, but the purchase installation handler is not wired to `FurnishingStorageRegistry.TryBindFixture` with an authoritative capacity definition. No storage capacity was invented in this pass.

## Change made

`GeneralStoreRuntimeManager` now adds an item-level furniture procurement offer only when the authored General Store definition has the item's positive selling price and reorder target and the regional category map recognizes it as furniture. The normal `StoreRestockService` then remains responsible for finite supplier stock, named lots, purchase cost, and receipt. Offer construction itself creates no stock.

A Unity regression test reads the actual General Store definition, checks all eligible first-wave SKUs become named offers, and asserts offer construction does not create retail lots.

## Verification

- Unity 6000.4.2f1 source recompilation: completed, `failed=false`, zero compiler errors.
- `FurnitureStorePurchaseG3Tests`: 28 passed, 0 failed.
- `StoreRestockServiceTests`: 18 passed, 0 failed.
- New `AuthoredFurniturePricesAndTargetsCreateNamedProcurementOffers_WithoutSeedingStock`: 1 passed, 0 failed.
- Live Editor asset read-back confirmed wood item prices and reorder targets above; stove price returned 0.
- A pre-change PlayMode New Game initialized the authored town, but no completed simulated day or furniture transaction was observed. A post-change PlayMode attempt triggered a prolonged Unity reload/initialization and the Editor GUI stopped responding; no post-change gameplay result is claimed. No force-restart was attempted.

## Remaining work before Pass 5 can be called complete

1. Make a paid furniture purchase remain staged across ordinary simulation ticks; use the existing freight production/task/route authorities rather than synchronously consuming the whole trip in one day-driver call.
2. Verify the household's payment, seller receipt, named-lot debit, shipment custody, real wagon journey, arrival and installation from actual live state.
3. Connect Don Millward's produced furniture through a legitimate commercial premise and physical delivery/collection path; do not sell/install it directly from a remote register.
4. Add authoritative storage capacity metadata and bind installed cabinets/shelves through `FurnishingStorageRegistry` only when such capacity is defined.
5. Obtain an authorized stove supplier/price before testing stove purchase. Until then a stove purchase must remain unavailable.
6. Repeat a short PlayMode check after Unity responsiveness is restored. Preserve both initially sparse homes and current household cash; no extra free funds or goods were granted.
