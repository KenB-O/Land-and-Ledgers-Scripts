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
//     category. W0 pins the CURRENT behaviour (pending, outstanding
//     ignored); W1 flips it to max(0, pending - outstanding).
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
        /// W0 CHARACTERIZATION — current behaviour, pinned before W1 changes
        /// it: the weekly allocation is the pending quantity, and the
        /// daily-chain outstanding quantity is ignored. With pending 50 and
        /// 30 already ordered daily, this returns 50, so the town orders 80
        /// for a need of 50. W1 flips this to
        /// max(0, pending - outstanding). Do not "fix" this method without
        /// flipping StoreWeeklyReorderCustodyTests with it.
        /// </summary>
        public static int WeeklyAllocationUnits(int pendingUnits, int outstandingUnits)
        {
            return Math.Max(0, pendingUnits);
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
    }
}
