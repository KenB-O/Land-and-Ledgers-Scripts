# Pass VIII Inventory and Shipment Integrity

Phase 1 of Muse Pass VIII (absolute first priority): correction of PASS7-006, PASS7-007, and PASS7-008 at the production authorities, with a full-chain audit and invariant enforcement per Brief Section 5.

## Defects and root causes

### PASS7-006 — Retail capacity pool corruption (P1)
`GenericRetailConfiguration.ReleaseProductSpace` called `GetOrCreatePool`, which minted a zero-allocation pool on a store's first sale. `TryOccupyProductSpace` then refused every future receive against that zero pool while shipment advancement completed the shipments anyway, so goods vanished. A store that never configured shelf capacity silently lost every shipment after its first sale. (The brief's description was half-fixed already: `TryOccupyProductSpace` no longer minted, but `ReleaseProductSpace` still did, so the corruption survived.)

Fix: release now looks up the pool and does nothing when none exists. An unconfigured pool imposes no constraint; an explicitly configured pool still constrains and still frees room on release.

### PASS7-007 — Silent stock cap (P2)
`AddCategoryStockUnits` caps at the category target and returns accepted units, but several callers ignored the return and over-counted production or silently dropped goods. Every caller was audited: `GenericRetailSaleAuthority.TryExecuteSale` rollback now measures restored units and warns loudly on a capped restore; sawmill slab production records stored units, re-stores surplus after the outlet sale, and records remainder as disposed mill fuel; weekly operation output records a shortfall note; sawmill-to-yard lumber transfer and recurring-order fulfillment capture the seller re-absorb return and record stranded units; `StoreButchery` caps the wholesale request at receivable room before the butcher deducts, so the store never pays for meat it cannot hold. Callers already honoring the return (mine output, procurement, trading port, farm produce, production registry, logistics restore path) were left alone.

### PASS7-008 — Receive failure masked as Delivered (P2)
Three live instances:
1. `LogisticsShipmentState.AdvanceGameSeconds` auto-advanced Unloading to Completed on the clock, so any driver bypassing the delivery authority (notably `FreightCompanyRuntime`) marked goods delivered with no acceptance. Fix: Unloading now pins at stage end until a delivery authority resolves it; Completed always implies actual acceptance.
2. `FreightShipmentService.WorkUnloading` marked shipments delivered with only a diagnostic line and no inventory write. Fix: it now receives through a wired `ReceiveShipment` authority (`FreightShipmentHooks.ReceiveShipment` delegate). Full acceptance completes with real recipient custody; partial acceptance records exact accepted and remaining counts as Partial (retryable via `BeginUnloading`); refusal holds as Delayed with goods retained in wagon custody; a missing authority holds as Delayed rather than false-delivering.
3. `FromSaveDto` had an "ambiguous quarantine" repair that vaporized genuinely loaded cargo on reload. Analysis showed that branch was unreachable for phantom cargo (subsumed by the `provablyNeverLoaded` repair) and only ever fired on real cargo, so it was removed. Delayed and Failed shipments with real cargo now survive save/load intact.

Supporting changes: new persisted `deliveredQuantityUnits` on the shipment (all four manager delivery paths accumulate it); `VerifyConservation` rejects delivered claims disagreeing with actual acceptance; `FreightCompanyRuntime.CollectFreightCharge` now requires `deliveryApplied` so freight revenue settles only on truly delivered shipments. A harness-shim mirror of the DTO was updated to match.

## Full-chain audit

Seller stock → reservation → shipment creation → loading → departure → transport → arrival → destination acceptance → settlement → completed delivery, plus retries, rejected deliveries, partial fulfillment, blocked shipments, and save/load. The older `CreateBlockedShipment` duplication risk was rechecked: duplicate blocked creations each own zero cargo with distinct identities and deliver nothing.

## Invariants enforced (Brief Section 5)

Goods cannot disappear for insufficient receiving capacity. Goods cannot duplicate on failed shipment creation. A rejected shipment remains in legitimate custody. Partial receipts record exact accepted and remaining quantities. Delivered quantity equals actual accepted quantity. Shipment completion cannot falsely imply successful inventory acceptance. Financial settlement agrees with contractual delivery terms. A failed transfer cannot produce completed delivery twice. Capacity constraints cannot silently become zero through an unrelated release. Inventory readers cannot assume requested quantity equals actual mutation. Saved inventories remain valid (additive DTO change; legacy saves read the new field as false/zero). Retry and save/load are idempotent.

## Edge cases proved

Twenty-seven new production-authority tests (`Tests/Editor/Economy/ShipmentIntegrityPass8Tests.cs`), all passing: unconfigured retail capacity; zero legitimate available capacity; partially occupied capacity; full destination; shipment larger than available capacity; partial receipt; receiving after capacity changes; failed receipt followed by retry; multiple competing shipments; seller stock restoration after blocked shipment; destination stock reservation; goods awaiting unloading; purchase cancellation; seller/buyer closure during transport; save/load during unresolved delivery; repeated receipt callback; unavailable destination; external trade boundary receipt; blocked-shipment duplication.

## API changes for sibling systems

`FreightShipmentHooks` gained an optional `ReceiveShipment` delegate; when null, unloading holds as Delayed. `AdvanceGameSeconds` no longer auto-completes Unloading. `BeginUnloading` accepts Partial shipments with no open unload task for remainder retries. New persisted `DeliveredQuantityUnits`; `VerifyConservation` enforces it. `CollectFreightCharge` requires `deliveryApplied`. `ReleaseProductSpace` no longer mints pools. All other public signatures unchanged.

## Limitations

`LogisticsRuntimeManager`, `SharedBusinessRuntimeManager`, `LocalRecurringOrderManager`, `StoreButchery`, and `FreightCompanyRuntime` are excluded from the Roslyn harness (unshimmable scene dependencies); their changed paths were verified by review plus a scratch full-variant build with zero new errors. Their edits are minimal and additive against reviewed logic.
