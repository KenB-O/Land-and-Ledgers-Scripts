// Plan 05 W0 (Obj 48/47): the pure weekly-reorder decision seam for the
// General Store. The weekly path (GeneralStoreRuntimeManager.
// ResolveCostedPendingReorders, harness-excluded) and the daily chain
// (StoreRestockService, driven daily) compute their quantities
// independently: the weekly step reads PendingReorderUnits, which the daily
// chain never decrements, so the same need can be ordered twice (Obj 47).
// This helper is the single place both sides meet:
//   - OutstandingOrderedUnits: what the daily chain already has ordered or
//     in transit for a category (open orders + unreceived deliveries).
//   - WeeklyAllocationUnits: how many units the weekly step allocates for a
//     category: max(0, pending - outstanding) — the Obj 47 shared
//     outstanding-order check (W1). W0 pinned the pre-fix behaviour
//     (pending, outstanding ignored).
//   - TryChooseNamedSupplier: the cheapest registered supplier that can ship
//     the units at a priceable delivered cost, mirroring the daily chain's
//     choice (cheapest by delivered cost when a delivered-cost function is
//     supplied, goods quote otherwise; zero stock / zero quote / unpriceable
//     suppliers are skipped, never silently compared on goods alone).
// No Unity types; harness-testable. The manager delegates thinly.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.GeneralStore
{
    public static class StoreWeeklyReorderPlanner
    {
        /// <summary>
        /// W0: units the daily restock chain already has committed for a
        /// category — open orders (Ordered stage, UnitsOrdered) plus
        /// in-transit deliveries not yet received (delivery Units). An
        /// InTransit order is counted once, through its delivery, never
        /// through both the order and the delivery. Received deliveries and
        /// cancelled orders count zero.
        /// </summary>
        public static int OutstandingOrderedUnits(StoreRestockService service, string categoryId)
        {
            if (service == null || string.IsNullOrWhiteSpace(categoryId))
            {
                return 0;
            }

            int outstanding = 0;
            IReadOnlyList<StoreProcurementOrder> orders = service.Orders;
            if (orders != null)
            {
                for (int i = 0; i < orders.Count; i++)
                {
                    StoreProcurementOrder order = orders[i];
                    if (order == null) continue;
                    if (!string.Equals(order.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (order.Stage == StoreProcurementStage.Ordered)
                    {
                        outstanding += Math.Max(0, order.UnitsOrdered);
                    }
                }
            }

            IReadOnlyList<StoreDelivery> deliveries = service.Deliveries;
            if (deliveries != null)
            {
                for (int i = 0; i < deliveries.Count; i++)
                {
                    StoreDelivery delivery = deliveries[i];
                    if (delivery == null) continue;
                    if (delivery.Received) continue;
                    if (!string.Equals(delivery.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase)) continue;
                    outstanding += Math.Max(0, delivery.Units);
                }
            }

            return Math.Max(0, outstanding);
        }

        /// <summary>
        /// W1 (Obj 47): the weekly allocation is the pending quantity MINUS
        /// what the daily chain already ordered or has in transit for the
        /// same category, floored at zero. With pending 50 and 30 already
        /// ordered daily, this returns 20 — the town orders 50 total for a
        /// need of 50, never 80. (W0 pinned the pre-fix behaviour, which
        /// returned pending and ignored the outstanding quantity.)
        /// </summary>
        public static int WeeklyAllocationUnits(int pendingUnits, int outstandingUnits)
        {
            return Math.Max(0, pendingUnits - Math.Max(0, outstandingUnits));
        }

        /// <summary>
        /// Picks the named supplier for a weekly reorder: the cheapest by
        /// delivered cost per unit (goods + solo freight via the supplied
        /// function, mirroring the daily chain's Slice 15 choice) among
        /// suppliers with stock on hand and a positive quote. With no
        /// delivered-cost function the comparison runs on the goods quote.
        /// Returns false when no registered supplier can ship the category
        /// — the caller must refuse loudly, never conjure. The no-loss
        /// gate (cost vs selling price) is the daily chain's, not the
        /// weekly path's: weekly affordability is the survival budget in
        /// ResolveCostedPendingReorders, and this chooser does not invent
        /// one.
        /// </summary>
        public static bool TryChooseNamedSupplier(
            IReadOnlyList<IUpstreamGoodsSupplier> suppliers,
            string categoryId,
            int unitsNeeded,
            Func<IUpstreamGoodsSupplier, string, string, int, int> deliveredCostPerUnitCents,
            out IUpstreamGoodsSupplier supplier,
            out int goodsUnitCostCents,
            out int deliveredCostCentsPerUnit)
        {
            supplier = null;
            goodsUnitCostCents = 0;
            deliveredCostCentsPerUnit = 0;
            if (suppliers == null || string.IsNullOrWhiteSpace(categoryId) || unitsNeeded <= 0)
            {
                return false;
            }

            IUpstreamGoodsSupplier best = null;
            int bestComparisonCost = int.MaxValue;
            int bestGoods = 0;
            int bestDelivered = 0;
            for (int i = 0; i < suppliers.Count; i++)
            {
                IUpstreamGoodsSupplier candidate = suppliers[i];
                if (candidate == null) continue;
                if (Math.Max(0, candidate.AvailableUnits(categoryId)) <= 0) continue;
                int quote = Math.Max(0, candidate.QuoteUnitCostCents(categoryId));
                if (quote <= 0) continue;

                int comparisonCost = quote;
                int delivered = 0;
                if (deliveredCostPerUnitCents != null)
                {
                    int orderSize = Math.Max(1, Math.Min(unitsNeeded, Math.Max(0, candidate.AvailableUnits(categoryId))));
                    delivered = Math.Max(0, deliveredCostPerUnitCents(candidate, categoryId, string.Empty, orderSize));
                    if (delivered <= 0) continue;
                    comparisonCost = delivered;
                }

                if (comparisonCost < bestComparisonCost)
                {
                    bestComparisonCost = comparisonCost;
                    best = candidate;
                    bestGoods = quote;
                    bestDelivered = delivered;
                }
            }

            if (best == null)
            {
                return false;
            }

            supplier = best;
            goodsUnitCostCents = bestGoods;
            deliveredCostCentsPerUnit = bestDelivered;
            return true;
        }

        /// <summary>
        /// W1: the shipment terms for one named-supplier weekly order. The
        /// buyer pays the authored landed cost per unit (unchanged from the
        /// anonymous path — plan §3); the seller is owed the goods quote per
        /// unit. SellerRevenueCents is the total the named supplier earns on
        /// this order, recorded exactly once per order — the shipment
        /// carries it as sellerUnitRevenueCents so the delivery branch can
        /// credit it through CreditSellerRevenue when the source resolves
        /// to a town business. For an off-map regional supplier there is no
        /// town seller cash account (the registry authors OpeningCashCents
        /// only; the adapter holds no live cash ledger), so the revenue is
        /// off-map by construction and the receipt logs it as such — the
        /// seller-side conservation for a regional supplier is the Ship()
        /// stock decrement, pinned by the receipt tests.
        /// </summary>
        public sealed class WeeklyReorderShipmentTerms
        {
            public string SupplierId = string.Empty;
            public string SupplierName = string.Empty;
            public string CategoryId = string.Empty;
            public int Units;
            public int BuyerUnitCostCents;
            public int GoodsUnitCostCents;
            public int DeliveredCostCentsPerUnit;
            public int SellerRevenueCents;
        }

        public static WeeklyReorderShipmentTerms BuildShipmentTerms(
            IUpstreamGoodsSupplier supplier,
            string categoryId,
            int units,
            int goodsUnitCostCents,
            int buyerUnitCostCents,
            int deliveredCostCentsPerUnit)
        {
            if (supplier == null || string.IsNullOrWhiteSpace(categoryId) || units <= 0)
            {
                return null;
            }

            int goods = Math.Max(0, goodsUnitCostCents);
            return new WeeklyReorderShipmentTerms
            {
                SupplierId = supplier.SupplierId ?? string.Empty,
                SupplierName = supplier.SupplierName ?? string.Empty,
                CategoryId = categoryId,
                Units = units,
                BuyerUnitCostCents = Math.Max(0, buyerUnitCostCents),
                GoodsUnitCostCents = goods,
                DeliveredCostCentsPerUnit = Math.Max(0, deliveredCostCentsPerUnit),
                SellerRevenueCents = units * goods,
            };
        }

        /// <summary>W1: the loud refusal when no registered supplier can cover a category.</summary>
        public static string UncoveredCategoryReason(string categoryId, int registeredSupplierCount)
        {
            string category = string.IsNullOrWhiteSpace(categoryId) ? "unknown category" : categoryId.Trim();
            return $"no registered supplier can ship '{category}' " +
                $"({Math.Max(0, registeredSupplierCount)} suppliers registered) — " +
                $"weekly reorder refused for '{category}'; no anonymous off-map shipment was created.";
        }

        /// <summary>Receives already transported off-map cargo only when the accepted quantity can be paid in full.</summary>
        public static int ReceivePaidOffMapShipment(
            BusinessRuntimeState storeState, string categoryId, int units, int unitCostCents,
            out int spendCents, out string refusal)
        {
            spendCents = 0;
            refusal = string.Empty;
            CategoryStockState stock = storeState?.GetCategoryStock(categoryId);
            if (stock == null || units <= 0 || unitCostCents <= 0)
            {
                refusal = "missing destination, cargo, or positive purchase cost";
                return 0;
            }

            int capacity = stock.TargetStockUnits > 0
                ? Math.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits) : units;
            int receivable = Math.Min(units, capacity);
            long cost = (long)receivable * unitCostCents;
            if (receivable <= 0 || cost > int.MaxValue || !storeState.TrySpendCents((int)cost))
            {
                refusal = "destination full or insufficient cash to settle the delivery in full";
                return 0;
            }

            int accepted = storeState.ApplyDeliveredShipment(categoryId, receivable);
            spendCents = accepted * unitCostCents;
            if (accepted < receivable) storeState.AddCashCents((receivable - accepted) * unitCostCents);
            return accepted;
        }

        /// <summary>W1: the outcome of receiving one named-supplier weekly shipment.</summary>
        public sealed class NamedSupplierReceiptResult
        {
            public int AcceptedUnits;
            public int ShippedUnits;
            public int BuyerSpendCents;
            /// <summary>Units the shipment still owes (shipped short of the requested units).</summary>
            public int ShortfallUnits;
            public string StatusLine = string.Empty;
            public string RefusalReason = string.Empty;
        }

        /// <summary>
        /// W1: receives one named-supplier weekly shipment against real
        /// supplier stock and real store cash. The supplier ships at receipt
        /// (not at order time — the daily chain's shape: Ship() then receive
        /// in the same step), so a shipment that is blocked or never
        /// delivered costs the supplier nothing and the pending quantity
        /// re-queues through the existing failure path. The ship quantity is
        /// capped at the store's receivable capacity, so shipped units can
        /// never strand against a full shelf. When the supplier ships fewer
        /// units than the shipment carried, the shortfall is NOT re-queued
        /// to pending: the shipment keeps owing it (the delivery branch
        /// marks Partial and re-delivers remainingQuantityUnits on the next
        /// pass), so re-queuing would double-order. Buyer spend is accepted
        /// × buyer unit cost (the authored landed cost); the seller revenue
        /// is carried on the shipment terms, not spent here. Returns
        /// accepted units (0 + RefusalReason on refusal).
        /// </summary>
        public static NamedSupplierReceiptResult ReceiveNamedSupplierShipment(
            IUpstreamGoodsSupplier supplier,
            BusinessRuntimeState storeState,
            string categoryId,
            int units,
            int buyerUnitCostCents,
            int dayIndex,
            List<string> diagnostics)
        {
            var result = new NamedSupplierReceiptResult();
            if (supplier == null)
            {
                result.RefusalReason = $"no named supplier for '{categoryId}' — receipt refused.";
                result.StatusLine = result.RefusalReason;
                return result;
            }

            if (storeState == null || string.IsNullOrWhiteSpace(categoryId) || units <= 0)
            {
                result.RefusalReason = "named-supplier receipt refused: missing store state, category, or units.";
                result.StatusLine = result.RefusalReason;
                return result;
            }

            CategoryStockState stock = storeState.GetCategoryStock(categoryId);
            if (stock == null)
            {
                result.RefusalReason = $"named-supplier receipt refused: destination category missing: {categoryId}.";
                result.StatusLine = result.RefusalReason;
                return result;
            }

            if (buyerUnitCostCents <= 0)
            {
                result.RefusalReason = $"named-supplier receipt refused: no positive buyer cost is recorded for '{categoryId}'; no goods or cash moved.";
                result.StatusLine = result.RefusalReason;
                return result;
            }

            int receivable = stock.TargetStockUnits > 0
                ? Math.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits)
                : Math.Max(0, units);
            if (receivable <= 0)
            {
                result.RefusalReason = $"named-supplier receipt refused: category full: {categoryId}.";
                result.StatusLine = result.RefusalReason;
                return result;
            }

            int toShip = Math.Min(Math.Max(0, units), receivable);
            int maxShippable = Math.Min(toShip, Math.Max(0, supplier.AvailableUnits(categoryId)));
            if (maxShippable <= 0)
            {
                result.RefusalReason = $"named-supplier receipt refused: '{supplier.SupplierName}' has no '{categoryId}' stock to ship.";
                result.StatusLine = result.RefusalReason;
                return result;
            }

            long requiredCash = (long)maxShippable * buyerUnitCostCents;
            if (storeState.CurrentCashCents < requiredCash)
            {
                result.RefusalReason = $"named-supplier receipt deferred: store has {storeState.CurrentCashCents}c, " +
                    $"but the available {maxShippable}u '{categoryId}' delivery requires {requiredCash}c; no supplier stock, inventory, or cash moved.";
                result.StatusLine = result.RefusalReason;
                if (diagnostics != null) diagnostics.Add(result.RefusalReason);
                return result;
            }

            // Capture the goods quote BEFORE shipping: the quote names the
            // cheapest in-stock item, and shipping can exhaust it.
            int goodsQuoteCents = Math.Max(0, supplier.QuoteUnitCostCents(categoryId));
            int shipped = Math.Max(0, supplier.Ship(categoryId, maxShippable, Math.Max(0, dayIndex), diagnostics ?? new List<string>()));
            result.ShippedUnits = shipped;
            if (shipped <= 0)
            {
                result.RefusalReason = $"named-supplier receipt refused: '{supplier.SupplierName}' shipped nothing for '{categoryId}'.";
                result.StatusLine = result.RefusalReason;
                return result;
            }

            int spend = checked(shipped * buyerUnitCostCents);
            // Cash was preflighted before the irreversible supplier decrement.
            // TrySpend remains the final guard; unlike SpendCents it cannot
            // silently accept a partial payment.
            if (!storeState.TrySpendCents(spend))
            {
                result.RefusalReason = $"named-supplier receipt refused: buyer cash changed before settling {spend}c; no destination inventory was accepted.";
                result.StatusLine = result.RefusalReason;
                if (diagnostics != null) diagnostics.Add(result.RefusalReason);
                return result;
            }

            int accepted = Math.Max(0, storeState.ApplyDeliveredShipment(categoryId, shipped));
            result.AcceptedUnits = accepted;
            result.BuyerSpendCents = spend;
            if (accepted < shipped)
            {
                int unacceptedSpend = (shipped - accepted) * buyerUnitCostCents;
                storeState.AddCashCents(unacceptedSpend);
                result.BuyerSpendCents -= unacceptedSpend;
            }

            result.ShortfallUnits = Math.Max(0, units - shipped);
            result.StatusLine = $"Received {accepted}u {categoryId} from {supplier.SupplierName} " +
                $"for {spend}c (seller revenue {accepted * goodsQuoteCents}c off-map)" +
                (result.ShortfallUnits > 0 ? $"; shortfall of {result.ShortfallUnits}u still owed by the shipment." : ".");
            return result;
        }
    }
}
