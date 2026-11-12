using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.GeneralStore
{
    /// <summary>
    /// Phase C: upstream provenance for store restocking. Append-only.
    /// </summary>
    public enum StoreProcurementStage
    {
        Ordered = 0,
        InTransit = 1,
        Received = 2,
        Cancelled = 3,
    }

    /// <summary>
    /// Phase C: one real upstream goods supplier for store restocking —
    /// an off-map wholesaler, a local producer, or a player/NPC producer.
    /// Finite stock: a supplier cannot ship what it does not hold.
    /// </summary>
    public interface IUpstreamGoodsSupplier
    {
        string SupplierId { get; }
        string SupplierName { get; }
        int QuoteUnitCostCents(string categoryId);
        int LeadTimeDays(string categoryId);
        int AvailableUnits(string categoryId);
        /// <summary>Hands over real goods; returns units actually shipped.</summary>
        int Ship(string categoryId, int units, int dayIndex, List<string> diagnostics);
    }

    /// <summary>
    /// Slice 18: optional exact-item extension for upstream suppliers.
    /// A category-only supplier stays source compatible; a supplier that
    /// can quote, count, and ship a NAMED item implements this so the
    /// chain can choose the cheapest deliverable item inside a category
    /// (flour from Millbrook vs Redfield, tea at its own price) instead
    /// of the category's cheapest item standing in for all of them.
    /// </summary>
    public interface IItemLevelUpstreamGoodsSupplier : IUpstreamGoodsSupplier
    {
        /// <summary>Items this supplier offers in the category with stock on hand.</summary>
        IReadOnlyList<string> OfferedItemsInCategory(string categoryId);
        /// <summary>Wholesale cents per unit for a named item; 0 when not offered or out of stock.</summary>
        int QuoteItemUnitCostCents(string itemId);
        /// <summary>Units of a named item on hand.</summary>
        int AvailableItemUnits(string itemId);
        /// <summary>Ships a named item from real stock; returns units actually shipped.</summary>
        int ShipItem(string itemId, int units, int dayIndex, List<string> diagnostics);
    }

    /// <summary>Phase C: one real supplier order from the store to upstream.</summary>
    [Serializable]
    public sealed class StoreProcurementOrder
    {
        public string OrderId = string.Empty;
        public string StoreBusinessId = string.Empty;
        public string CategoryId = string.Empty;
        /// <summary>
        /// Slice 18: the named item this order buys, when the chain chose
        /// item-level. Empty = a category order (the pre-Slice-18 shape):
        /// the supplier ships its cheapest item in the category.
        /// </summary>
        public string ItemId = string.Empty;
        public int UnitsOrdered;
        public int UnitCostCents;
        /// <summary>
        /// Slice 15: delivered cents per unit (goods + solo freight) when the
        /// order was chosen on delivered cost; 0 when it was chosen on the
        /// goods quote alone. The supplier is paid UnitCostCents; freight is
        /// a separate carrier settlement. New field: 0 on old saves.
        /// </summary>
        public int DeliveredCostCentsPerUnit;
        public string UpstreamSupplierId = string.Empty;
        public string UpstreamSupplierName = string.Empty;
        public int OrderDayIndex;
        public int ExpectedArrivalDayIndex;
        public StoreProcurementStage Stage;
        public string Provenance = string.Empty;
        public string CancelReason = string.Empty;
        /// <summary>
        /// Overnight 2026-11-04: orders placed the same day with the same
        /// supplier share one shipment group, so the freight layer can move
        /// them as one load. Grouping never creates an order: every grouped
        /// order independently passed the threshold, availability, and
        /// no-loss checks below. Empty when the supplier has only one order
        /// that day. Operational only; not part of the save shape yet.
        /// </summary>
        public string ShipmentGroupId = string.Empty;
    }

    /// <summary>Phase C: one delivery in transit — goods exist but are NOT store stock yet.</summary>
    [Serializable]
    public sealed class StoreDelivery
    {
        public string DeliveryId = string.Empty;
        public string OrderId = string.Empty;
        public string CategoryId = string.Empty;
        /// <summary>Slice 18: the named item this delivery carries, when the order was item-level. Empty for category deliveries.</summary>
        public string ItemId = string.Empty;
        public int Units;
        public int ShippedDayIndex;
        public int TransitMinutes;
        public bool Arrived;
        public bool Received;
        public string Provenance = string.Empty;
    }

    /// <summary>
    /// Phase C: the store restock chain. Household purchases deplete REAL
    /// store inventory; when stock runs low this service triggers REAL
    /// procurement: supplier order → delivery → real freight → receiving.
    /// Goods, ownership, custody, time, money, obligations and provenance are
    /// preserved through the whole chain:
    ///   - order names the upstream supplier and quotes a real unit cost;
    ///   - goods ship from the supplier's REAL stock (finite);
    ///   - the delivery is in-transit custody, not store stock;
    ///   - receiving pays real store cash and books real stock with the full
    ///     provenance chain (order → delivery → received);
    ///   - an unaffordable delivery WAITS (recorded, retried) — it is never
    ///     silently received and never conjures stock.
    /// The store seeks the cheapest quoted supplier with available units, and
    /// may delay or cancel when no legitimate supply exists.
    /// </summary>
    public sealed class StoreRestockService
    {
        private readonly List<StoreProcurementOrder> orders = new List<StoreProcurementOrder>();
        private readonly List<StoreDelivery> deliveries = new List<StoreDelivery>();
        private readonly List<string> diagnostics = new List<string>();
        private int nextOrderSequence;
        private int nextDeliverySequence;
        private int nextShipmentGroupSequence;

        public IReadOnlyList<StoreProcurementOrder> Orders => orders;
        public IReadOnlyList<StoreDelivery> Deliveries => deliveries;
        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Daily review: for each stocked category below the reorder
        /// threshold with no open order, place a real order with the best
        /// upstream supplier. Never duplicates an open order per category.
        /// Overnight 2026-11-04: an order is placed only when it can
        /// plausibly profit — the cheapest quote must sit below the
        /// store's selling price for the category, since transport and
        /// handling on top of a break-even quote would already lose
        /// money. Orders placed the same day with the same supplier are
        /// grouped into one shipment group for a single freight movement;
        /// grouping never invents demand, it only tags orders that each
        /// independently earned their place.
        /// </summary>
        public void EvaluateRestock(
            IGeneralStoreTradingPort store,
            List<IUpstreamGoodsSupplier> upstreamSuppliers,
            int dayIndex,
            float reorderThreshold01,
            Func<IUpstreamGoodsSupplier, string, string, int, int> deliveredCostPerUnitCents = null)
        {
            if (store == null || store.CategoryOffers == null)
            {
                return;
            }

            // When a store has identified SKU lots, its authored item targets
            // must drive replenishment. Category totals can remain healthy
            // because another SKU (for example sugar) is abundant while
            // bread or flour is already sold out. Keep legacy category-only
            // behavior for ports without this optional production contract.
            var itemManagedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IItemLevelRestockTradingPort itemRestock = store as IItemLevelRestockTradingPort;
            if (itemRestock != null && itemRestock.CategoryOffers != null)
            {
                for (int offerIndex = 0; offerIndex < itemRestock.CategoryOffers.Count; offerIndex++)
                {
                    GeneralStoreCategoryOffer offer = itemRestock.CategoryOffers[offerIndex];
                    if (offer == null || !offer.UsesItemLevelInventory || offer.OfferedItemIds == null
                        || string.IsNullOrWhiteSpace(offer.CategoryId)) continue;

                    for (int itemIndex = 0; itemIndex < offer.OfferedItemIds.Count; itemIndex++)
                    {
                        string itemId = offer.OfferedItemIds[itemIndex];
                        int target = Math.Max(0, itemRestock.ItemReorderTargetUnits(itemId));
                        if (target <= 0) continue;
                        itemManagedCategories.Add(offer.CategoryId);

                        int current = Math.Max(0, itemRestock.ItemRestockStockUnits(itemId));
                        float itemHealth = current / (float)target;
                        if (itemHealth > Math.Max(0f, Math.Min(1f, reorderThreshold01))) continue;
                        if (HasOpenOrder(store.StoreBusinessId, offer.CategoryId, itemId)) continue;

                        int missing = Math.Max(0, target - current);
                        if (missing <= 0) continue;
                        TryPlaceSpecificItemLevelOrder(store, itemRestock, upstreamSuppliers,
                            offer.CategoryId, itemId, missing, dayIndex, deliveredCostPerUnitCents);
                    }
                }
            }

            for (int i = 0; i < store.CategoryOffers.Count; i++)
            {
                GeneralStoreCategoryOffer offer = store.CategoryOffers[i];
                if (offer == null || string.IsNullOrWhiteSpace(offer.CategoryId))
                {
                    continue;
                }

                string categoryId = offer.CategoryId;
                if (itemManagedCategories.Contains(categoryId)) continue;
                int target = Math.Max(0, store.CategoryTargetStockUnits(categoryId));
                int current = Math.Max(0, store.CategoryStockUnits(categoryId));
                if (target <= 0)
                {
                    continue;
                }

                float health = current / (float)target;
                if (health > Math.Max(0f, Math.Min(1f, reorderThreshold01)))
                {
                    continue;
                }

                if (HasOpenOrder(store.StoreBusinessId, categoryId, null))
                {
                    continue;
                }

                int missing = Math.Max(0, target - current);
                if (missing <= 0)
                {
                    continue;
                }

                // Slice 18: item-level choice first (needs an item-level
                // port and item-level suppliers). It picks the cheapest
                // deliverable (supplier, item) pair that passes ITS OWN
                // item price — so tea and coffee, whose delivered costs
                // sit above the category average but below their own
                // prices, become orderable, and the flour crossover
                // (near vs far by order size) becomes visible to the
                // chain. When no item pair passes, the category path
                // below runs unchanged.
                if (TryPlaceItemLevelOrder(store, upstreamSuppliers, categoryId, missing, dayIndex, deliveredCostPerUnitCents))
                {
                    continue;
                }

                IUpstreamGoodsSupplier best = ChooseSupplier(upstreamSuppliers, categoryId, missing, dayIndex, deliveredCostPerUnitCents);
                if (best == null)
                {
                    diagnostics.Add($"Day {dayIndex}: no upstream supplier can quote '{categoryId}' — restock delayed, not faked.");
                    continue;
                }

                int unitCost = Math.Max(0, best.QuoteUnitCostCents(categoryId));
                int available = Math.Max(0, best.AvailableUnits(categoryId));
                int orderUnits = Math.Min(missing, available);
                if (orderUnits <= 0)
                {
                    diagnostics.Add($"Day {dayIndex}: '{best.SupplierName}' has no '{categoryId}' to ship — restock delayed.");
                    continue;
                }

                // Slice 15: when a delivered-cost function is supplied, the
                // choice AND the no-loss check both run on delivered cost
                // (goods + solo freight), because freight on top of a
                // break-even goods quote already loses money. The order
                // still records the goods cost the supplier is paid.
                int deliveredCost = 0;
                if (deliveredCostPerUnitCents != null)
                    deliveredCost = Math.Max(0, deliveredCostPerUnitCents(best, categoryId, string.Empty, orderUnits));
                int costForNoLossCheck = deliveredCostPerUnitCents != null ? deliveredCost : unitCost;

                int sellingPrice = Math.Max(0, store.CategoryPricePerUnitCents(categoryId));
                if (sellingPrice <= 0 || costForNoLossCheck >= sellingPrice)
                {
                    diagnostics.Add($"Day {dayIndex}: no restock for '{categoryId}' — cheapest {(deliveredCostPerUnitCents != null ? "delivered" : "")} cost {costForNoLossCheck}c meets or exceeds " +
                        $"the selling price {sellingPrice}c, so the sale would not profit. Order withheld, not placed at a loss.");
                    continue;
                }

                int leadDays = Math.Max(0, best.LeadTimeDays(categoryId));
                var order = BuildOrder(store, best, categoryId, string.Empty, orderUnits, unitCost, deliveredCost, leadDays, dayIndex);
                orders.Add(order);
                diagnostics.Add($"Day {dayIndex}: {order.Provenance} (arrives ~day {order.ExpectedArrivalDayIndex}).");
            }

            ConsolidateTodayShipments(store.StoreBusinessId, dayIndex);
        }

        private void TryPlaceSpecificItemLevelOrder(
            IGeneralStoreTradingPort store,
            IItemLevelRestockTradingPort itemPort,
            List<IUpstreamGoodsSupplier> suppliers,
            string categoryId,
            string itemId,
            int missing,
            int dayIndex,
            Func<IUpstreamGoodsSupplier, string, string, int, int> deliveredCostPerUnitCents)
        {
            if (suppliers == null || missing <= 0) return;

            IItemLevelUpstreamGoodsSupplier bestSupplier = null;
            int bestGoodsCost = 0;
            int bestDeliveredCost = 0;
            int bestOrderUnits = 0;
            int bestComparableCost = int.MaxValue;
            int sellingPrice = Math.Max(0, itemPort.ItemRetailPricePerStoreUnitCents(itemId));
            if (sellingPrice <= 0) return;

            for (int i = 0; i < suppliers.Count; i++)
            {
                IItemLevelUpstreamGoodsSupplier supplier = suppliers[i] as IItemLevelUpstreamGoodsSupplier;
                if (supplier == null) continue;
                IReadOnlyList<string> offeredItems = supplier.OfferedItemsInCategory(categoryId);
                bool offersItem = false;
                if (offeredItems != null)
                    for (int j = 0; j < offeredItems.Count; j++)
                        if (string.Equals(offeredItems[j], itemId, StringComparison.OrdinalIgnoreCase))
                        {
                            offersItem = true;
                            break;
                        }
                if (!offersItem) continue;

                int available = Math.Max(0, supplier.AvailableItemUnits(itemId));
                int goodsCost = Math.Max(0, supplier.QuoteItemUnitCostCents(itemId));
                if (available <= 0 || goodsCost <= 0) continue;
                int lotSize = Math.Max(1, itemPort.ItemReorderLotSize(itemId));
                int requested = (int)Math.Min(int.MaxValue, ((long)missing + lotSize - 1L) / lotSize * lotSize);
                int units = Math.Min(requested, available);
                int deliveredCost = deliveredCostPerUnitCents != null
                    ? Math.Max(0, deliveredCostPerUnitCents(supplier, categoryId, itemId, units))
                    : goodsCost;
                if (deliveredCost >= sellingPrice) continue;
                if (deliveredCost < bestComparableCost
                    || (deliveredCost == bestComparableCost && bestSupplier != null
                        && string.CompareOrdinal(supplier.SupplierId, bestSupplier.SupplierId) < 0))
                {
                    bestSupplier = supplier;
                    bestGoodsCost = goodsCost;
                    bestDeliveredCost = deliveredCost;
                    bestOrderUnits = units;
                    bestComparableCost = deliveredCost;
                }
            }

            if (bestSupplier == null)
            {
                diagnostics.Add($"Day {dayIndex}: no profitable upstream offer can replenish '{itemId}' for '{categoryId}' — item stock remains unchanged.");
                return;
            }

            int leadDays = Math.Max(0, bestSupplier.LeadTimeDays(categoryId));
            StoreProcurementOrder order = BuildOrder(store, bestSupplier, categoryId, itemId,
                bestOrderUnits, bestGoodsCost, bestDeliveredCost, leadDays, dayIndex);
            orders.Add(order);
            diagnostics.Add($"Day {dayIndex}: {order.Provenance} (item target {itemPort.ItemReorderTargetUnits(itemId)} store unit(s), on hand {itemPort.ItemRestockStockUnits(itemId)}; arrives ~day {order.ExpectedArrivalDayIndex}).");
        }

        /// <summary>
        /// Slice 18: item-level ordering inside a category need. Scans
        /// every (item-level supplier, offered item) pair in the category,
        /// keeps the pairs whose cost sits below the ITEM's own selling
        /// price, and places one order for the cheapest such pair. The
        /// need remains the category shortfall for legacy ports; identified
        /// production ports use their authored per-item targets above.
        /// Returns false when no item pair passes the no-loss check — the
        /// caller then runs the category path.
        /// </summary>
        private bool TryPlaceItemLevelOrder(
            IGeneralStoreTradingPort store,
            List<IUpstreamGoodsSupplier> upstreamSuppliers,
            string categoryId,
            int missing,
            int dayIndex,
            Func<IUpstreamGoodsSupplier, string, string, int, int> deliveredCostPerUnitCents)
        {
            var itemPort = store as IItemLevelGeneralStoreTradingPort;
            if (itemPort == null || upstreamSuppliers == null)
            {
                return false;
            }

            IUpstreamGoodsSupplier bestSupplier = null;
            string bestItemId = null;
            int bestGoods = 0;
            int bestDelivered = 0;
            int bestUnits = 0;
            int bestCostForCheck = int.MaxValue;

            for (int s = 0; s < upstreamSuppliers.Count; s++)
            {
                var itemSupplier = upstreamSuppliers[s] as IItemLevelUpstreamGoodsSupplier;
                if (itemSupplier == null) continue;
                IReadOnlyList<string> items = itemSupplier.OfferedItemsInCategory(categoryId);
                if (items == null) continue;
                for (int j = 0; j < items.Count; j++)
                {
                    string itemId = items[j];
                    if (string.IsNullOrWhiteSpace(itemId)) continue;
                    int available = Math.Max(0, itemSupplier.AvailableItemUnits(itemId));
                    if (available <= 0) continue;
                    int orderUnits = Math.Min(missing, available);
                    if (orderUnits <= 0) continue;
                    int goods = Math.Max(0, itemSupplier.QuoteItemUnitCostCents(itemId));
                    if (goods <= 0) continue;
                    int delivered = 0;
                    if (deliveredCostPerUnitCents != null)
                        delivered = Math.Max(0, deliveredCostPerUnitCents(itemSupplier, categoryId, itemId, orderUnits));
                    int costForCheck = deliveredCostPerUnitCents != null ? delivered : goods;
                    int sellingPrice = Math.Max(0, itemPort.ItemPricePerUnitCents(itemId));
                    if (sellingPrice <= 0 || costForCheck >= sellingPrice) continue;
                    if (costForCheck < bestCostForCheck
                        || (costForCheck == bestCostForCheck && bestSupplier != null
                            && (string.CompareOrdinal(itemSupplier.SupplierId, bestSupplier.SupplierId) < 0
                                || (string.Equals(itemSupplier.SupplierId, bestSupplier.SupplierId, StringComparison.Ordinal)
                                    && string.CompareOrdinal(itemId, bestItemId) < 0))))
                    {
                        bestSupplier = itemSupplier;
                        bestItemId = itemId;
                        bestGoods = goods;
                        bestDelivered = delivered;
                        bestUnits = orderUnits;
                        bestCostForCheck = costForCheck;
                    }
                }
            }

            if (bestSupplier == null)
            {
                return false;
            }

            int leadDays = Math.Max(0, bestSupplier.LeadTimeDays(categoryId));
            StoreProcurementOrder order = BuildOrder(store, bestSupplier, categoryId, bestItemId, bestUnits, bestGoods, bestDelivered, leadDays, dayIndex);
            orders.Add(order);
            diagnostics.Add($"Day {dayIndex}: {order.Provenance} (arrives ~day {order.ExpectedArrivalDayIndex}).");
            return true;
        }

        private StoreProcurementOrder BuildOrder(
            IGeneralStoreTradingPort store,
            IUpstreamGoodsSupplier supplier,
            string categoryId,
            string itemId,
            int orderUnits,
            int unitCost,
            int deliveredCost,
            int leadDays,
            int dayIndex)
        {
            string what = string.IsNullOrWhiteSpace(itemId) ? categoryId : itemId;
            var order = new StoreProcurementOrder
            {
                OrderId = $"po-{store.StoreBusinessId}-{dayIndex}-{nextOrderSequence++}",
                StoreBusinessId = store.StoreBusinessId,
                CategoryId = categoryId,
                ItemId = itemId ?? string.Empty,
                UnitsOrdered = orderUnits,
                UnitCostCents = unitCost,
                DeliveredCostCentsPerUnit = deliveredCost,
                UpstreamSupplierId = supplier.SupplierId,
                UpstreamSupplierName = supplier.SupplierName,
                OrderDayIndex = Math.Max(0, dayIndex),
                ExpectedArrivalDayIndex = Math.Max(0, dayIndex) + leadDays,
                Stage = StoreProcurementStage.Ordered,
                Provenance = $"order {orderUnits}u {what} from {supplier.SupplierName} @ {unitCost}c/u",
            };
            order.Provenance = $"order {order.OrderId}: " + order.Provenance;
            return order;
        }

        /// <summary>
        /// Tags today's orders from any supplier with two or more orders
        /// into one shipment group each, so one freight movement can carry
        /// them. Orders already grouped (a repeated same-day review) keep
        /// their group; singleton orders stay ungrouped.
        /// </summary>
        private void ConsolidateTodayShipments(string storeBusinessId, int dayIndex)
        {
            var bySupplier = new Dictionary<string, List<StoreProcurementOrder>>(StringComparer.OrdinalIgnoreCase);
            foreach (StoreProcurementOrder order in orders)
            {
                if (order == null || order.Stage != StoreProcurementStage.Ordered
                    || order.OrderDayIndex != Math.Max(0, dayIndex)
                    || !string.Equals(order.StoreBusinessId, storeBusinessId, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(order.UpstreamSupplierId))
                {
                    continue;
                }

                if (!bySupplier.TryGetValue(order.UpstreamSupplierId, out List<StoreProcurementOrder> group))
                {
                    group = new List<StoreProcurementOrder>();
                    bySupplier[order.UpstreamSupplierId] = group;
                }

                group.Add(order);
            }

            foreach (KeyValuePair<string, List<StoreProcurementOrder>> pair in bySupplier)
            {
                if (pair.Value.Count < 2) continue;
                string groupId = null;
                foreach (StoreProcurementOrder order in pair.Value)
                {
                    if (!string.IsNullOrWhiteSpace(order.ShipmentGroupId))
                    {
                        groupId = order.ShipmentGroupId;
                        break;
                    }
                }

                if (groupId == null)
                {
                    groupId = $"ship-{storeBusinessId}-{Math.Max(0, dayIndex)}-{pair.Key}-{nextShipmentGroupSequence++}";
                    diagnostics.Add($"Day {dayIndex}: {pair.Value.Count} orders from '{pair.Value[0].UpstreamSupplierName}' " +
                        $"consolidated into shipment {groupId} for one freight movement.");
                }

                foreach (StoreProcurementOrder order in pair.Value)
                {
                    if (string.IsNullOrWhiteSpace(order.ShipmentGroupId))
                    {
                        order.ShipmentGroupId = groupId;
                    }
                }
            }
        }

        /// <summary>
        /// Advances the chain one day: due orders ship from real upstream
        /// stock into in-transit deliveries; arrived deliveries are received
        /// (real cash paid, real stock booked with provenance). Unaffordable
        /// receipts wait honestly — no invisible replenishment.
        /// </summary>
        public void AdvanceDay(
            IGeneralStoreTradingPort store,
            List<IUpstreamGoodsSupplier> upstreamSuppliers,
            int dayIndex,
            List<string> outDiagnostics)
        {
            outDiagnostics ??= diagnostics;
            if (store == null)
            {
                return;
            }

            // 1. Due orders → ship → in-transit deliveries.
            for (int i = 0; i < orders.Count; i++)
            {
                StoreProcurementOrder order = orders[i];
                if (order == null || order.Stage != StoreProcurementStage.Ordered)
                {
                    continue;
                }

                if (order.ExpectedArrivalDayIndex > dayIndex)
                {
                    continue;
                }

                IUpstreamGoodsSupplier supplier = FindSupplier(upstreamSuppliers, order.UpstreamSupplierId);
                if (supplier == null)
                {
                    order.Stage = StoreProcurementStage.Cancelled;
                    order.CancelReason = $"upstream supplier '{order.UpstreamSupplierId}' gone";
                    outDiagnostics.Add($"Day {dayIndex}: {order.OrderId} cancelled — {order.CancelReason}.");
                    continue;
                }

                // Slice 18: an item-level order ships the NAMED item from
                // real stock; a category order ships the supplier's
                // cheapest items in the category, as before.
                int shipped;
                if (!string.IsNullOrWhiteSpace(order.ItemId)
                    && supplier is IItemLevelUpstreamGoodsSupplier itemSupplier)
                {
                    shipped = Math.Max(0, itemSupplier.ShipItem(order.ItemId, order.UnitsOrdered, dayIndex, outDiagnostics));
                }
                else
                {
                    shipped = Math.Max(0, supplier.Ship(order.CategoryId, order.UnitsOrdered, dayIndex, outDiagnostics));
                }
                if (shipped <= 0)
                {
                    order.Stage = StoreProcurementStage.Cancelled;
                    order.CancelReason = $"upstream '{supplier.SupplierName}' shipped nothing (stock exhausted)";
                    outDiagnostics.Add($"Day {dayIndex}: {order.OrderId} cancelled — {order.CancelReason}. No stock conjured.");
                    continue;
                }

                order.Stage = StoreProcurementStage.InTransit;
                var delivery = new StoreDelivery
                {
                    DeliveryId = $"dlv-{order.OrderId}-{nextDeliverySequence++}",
                    OrderId = order.OrderId,
                    CategoryId = order.CategoryId,
                    ItemId = order.ItemId ?? string.Empty,
                    Units = shipped,
                    ShippedDayIndex = Math.Max(0, dayIndex),
                    TransitMinutes = 0,
                    Arrived = true,
                    Provenance = $"{order.Provenance} → delivery shipped day {dayIndex}",
                };
                delivery.Provenance = $"delivery {delivery.DeliveryId}: " + delivery.Provenance;
                deliveries.Add(delivery);
                outDiagnostics.Add($"Day {dayIndex}: {delivery.Provenance}.");
            }

            // 2. Arrived deliveries → receive (pay + stock). Only what the
            // category can take AND the store can afford moves; the rest waits
            // at the dock honestly — never conjured, never unpaid-for.
            for (int i = 0; i < deliveries.Count; i++)
            {
                StoreDelivery delivery = deliveries[i];
                if (delivery == null || !delivery.Arrived || delivery.Received)
                {
                    continue;
                }

                StoreProcurementOrder order = FindOrder(delivery.OrderId);
                // Slice 24: defense in depth for the restore check above — a
                // delivery with no live order never books stock, even if it
                // reaches memory through another path. (A missing order would
                // otherwise price at 0c below and SpendCash(0) would succeed.)
                if (order == null || order.Stage != StoreProcurementStage.InTransit)
                {
                    outDiagnostics.Add($"Day {dayIndex}: {delivery.DeliveryId} WAITING — no live order for '{delivery.CategoryId}' (order '{delivery.OrderId}' " +
                        (order == null ? "missing" : $"is {order.Stage}") + "). No stock booked.");
                    continue;
                }
                int unitCost = order.UnitCostCents;
                int receivable = Math.Max(0, store.CategoryReceivableUnits(delivery.CategoryId));
                int unitsToReceive = Math.Min(delivery.Units, receivable);
                if (unitsToReceive <= 0)
                {
                    outDiagnostics.Add($"Day {dayIndex}: {delivery.DeliveryId} WAITING — no receiving capacity for '{delivery.CategoryId}'. No stock booked.");
                    continue;
                }

                bool identifiedItemDelivery = !string.IsNullOrWhiteSpace(delivery.ItemId)
                    && store is IItemLevelRestockTradingPort
                    && store is IIdentifiedRetailStockReceivingPort;
                string itemUnitLabel = identifiedItemDelivery
                    ? ((IItemLevelRestockTradingPort)store).ItemUnitLabel(delivery.ItemId)
                    : string.Empty;
                if (identifiedItemDelivery && string.IsNullOrWhiteSpace(itemUnitLabel))
                {
                    outDiagnostics.Add($"Day {dayIndex}: {delivery.DeliveryId} WAITING — no authored unit label for '{delivery.ItemId}'. No stock booked or paid.");
                    continue;
                }

                long fullCost = (long)unitsToReceive * Math.Max(0, unitCost);
                if (fullCost > int.MaxValue || !(store is IPaidStoreReceivingPort paidReceiver))
                {
                    outDiagnostics.Add($"Day {dayIndex}: {delivery.DeliveryId} WAITING — cost exceeds supported cents or the store has no atomic paid-receipt port. No cash or stock moved.");
                    continue;
                }
                int cost = (int)fullCost;
                string provenance = $"{delivery.Provenance} → received day {dayIndex} for {cost}c";
                int receivedBefore = Math.Max(0, order.UnitsOrdered - delivery.Units);
                string lotId = $"store-procurement:{delivery.DeliveryId}:{receivedBefore}";
                int cashBeforeReceipt = store.CurrentCashCents;
                int stockBeforeReceipt = store.CategoryStockUnits(delivery.CategoryId);
                string receiveProblem = paidReceiver.ReceivePaidStock(lotId,
                    identifiedItemDelivery ? delivery.ItemId : string.Empty,
                    delivery.CategoryId, itemUnitLabel, unitsToReceive, cost, provenance, dayIndex);
                if (receiveProblem != null)
                {
                    outDiagnostics.Add($"Day {dayIndex}: {delivery.DeliveryId} receive issue — {receiveProblem}");
                    continue;
                }

                delivery.Units -= unitsToReceive;
                if (delivery.Units <= 0)
                {
                    delivery.Received = true;
                    if (order != null)
                    {
                        order.Stage = StoreProcurementStage.Received;
                    }
                }

                outDiagnostics.Add($"Day {dayIndex}: received {unitsToReceive}u {delivery.CategoryId} ({provenance})." +
                    $" [StoreReceipt] business='{store.StoreBusinessId}' item='{delivery.ItemId}' lot='{lotId}' " +
                    $"paid={cost}c cash={cashBeforeReceipt}->{store.CurrentCashCents}c stock={stockBeforeReceipt}->{store.CategoryStockUnits(delivery.CategoryId)}u." +
                    (delivery.Units > 0 ? $" {delivery.Units}u still at the dock." : string.Empty));
            }
        }

        private bool HasOpenOrder(string storeBusinessId, string categoryId, string itemId)
        {
            for (int i = 0; i < orders.Count; i++)
            {
                StoreProcurementOrder order = orders[i];
                if (order != null &&
                    string.Equals(order.StoreBusinessId, storeBusinessId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(order.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(order.ItemId)
                        || string.Equals(order.ItemId, itemId, StringComparison.OrdinalIgnoreCase)) &&
                    (order.Stage == StoreProcurementStage.Ordered || order.Stage == StoreProcurementStage.InTransit))
                {
                    return true;
                }
            }

            return false;
        }

        private static IUpstreamGoodsSupplier ChooseSupplier(
            List<IUpstreamGoodsSupplier> suppliers, string categoryId, int unitsNeeded, int dayIndex,
            Func<IUpstreamGoodsSupplier, string, string, int, int> deliveredCostPerUnitCents = null)
        {
            IUpstreamGoodsSupplier best = null;
            int bestCost = int.MaxValue;
            if (suppliers == null)
            {
                return null;
            }

            for (int i = 0; i < suppliers.Count; i++)
            {
                IUpstreamGoodsSupplier supplier = suppliers[i];
                if (supplier == null)
                {
                    continue;
                }

                int available = Math.Max(0, supplier.AvailableUnits(categoryId));
                if (available <= 0)
                {
                    continue;
                }

                int quote = Math.Max(0, supplier.QuoteUnitCostCents(categoryId));
                if (quote <= 0)
                {
                    continue;
                }

                // Slice 15: when a delivered-cost function is supplied, the
                // comparison runs on delivered cost per unit for the order
                // size that would actually ship (min of needed and
                // available), not the goods quote. A supplier whose
                // delivered cost cannot be priced (0) is skipped, never
                // silently compared on goods alone.
                int comparisonCost = quote;
                if (deliveredCostPerUnitCents != null)
                {
                    int orderSize = Math.Max(1, Math.Min(unitsNeeded, available));
                    int delivered = Math.Max(0, deliveredCostPerUnitCents(
                        supplier, categoryId, string.Empty, orderSize));
                    if (delivered <= 0) continue;
                    comparisonCost = delivered;
                }

                if (comparisonCost < bestCost)
                {
                    bestCost = comparisonCost;
                    best = supplier;
                }
            }

            return best;
        }

        private static IUpstreamGoodsSupplier FindSupplier(List<IUpstreamGoodsSupplier> suppliers, string supplierId)
        {
            if (suppliers == null || string.IsNullOrWhiteSpace(supplierId))
            {
                return null;
            }

            for (int i = 0; i < suppliers.Count; i++)
            {
                if (suppliers[i] != null &&
                    string.Equals(suppliers[i].SupplierId, supplierId, StringComparison.OrdinalIgnoreCase))
                {
                    return suppliers[i];
                }
            }

            return null;
        }

        private StoreProcurementOrder FindOrder(string orderId)
        {
            for (int i = 0; i < orders.Count; i++)
            {
                if (orders[i] != null && string.Equals(orders[i].OrderId, orderId, StringComparison.Ordinal))
                {
                    return orders[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Slice 17: captures this service's half of the regional save
        /// state — open orders, in-transit deliveries, and the sequences —
        /// as deep copies into the DTO. The supplier stocks are captured
        /// by the adapters; the manager assembles the whole DTO.
        /// </summary>
        public void CaptureInto(LandLedgers.World.Regional.RegionalSupplySaveDto dto)
        {
            if (dto == null) return;
            dto.Orders = new List<StoreProcurementOrder>();
            for (int i = 0; i < orders.Count; i++)
                if (orders[i] != null) dto.Orders.Add(CopyOrder(orders[i]));
            dto.Deliveries = new List<StoreDelivery>();
            for (int i = 0; i < deliveries.Count; i++)
                if (deliveries[i] != null) dto.Deliveries.Add(CopyDelivery(deliveries[i]));
            dto.NextOrderSequence = nextOrderSequence;
            dto.NextDeliverySequence = nextDeliverySequence;
            dto.NextShipmentGroupSequence = nextShipmentGroupSequence;
        }

        /// <summary>
        /// Slice 17: clear-then-load for the service's orders, deliveries,
        /// and sequences. Invalid entries (missing ids, non-positive units,
        /// negative costs, undefined stages) are skipped with a diagnostic;
        /// sequences restore verbatim (they are next-values, so skipped
        /// entries can never collide with a future id). Idempotent: a
        /// second restore of the same DTO reproduces the same state.
        /// </summary>
        public void RestoreFrom(LandLedgers.World.Regional.RegionalSupplySaveDto dto, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            orders.Clear();
            deliveries.Clear();
            nextOrderSequence = 0;
            nextDeliverySequence = 0;
            nextShipmentGroupSequence = 0;
            if (dto == null) return;

            if (dto.Orders != null)
            {
                foreach (StoreProcurementOrder order in dto.Orders)
                {
                    if (!IsRestorableOrder(order))
                    {
                        diagnostics.Add("StoreRestockService: skipped an unrestorable saved order (missing id, non-positive units, negative cost, or unknown stage).");
                        continue;
                    }
                    orders.Add(CopyOrder(order));
                }
            }
            if (dto.Deliveries != null)
            {
                foreach (StoreDelivery delivery in dto.Deliveries)
                {
                    if (!IsRestorableDelivery(delivery))
                    {
                        diagnostics.Add("StoreRestockService: skipped an unrestorable saved delivery (missing ids or non-positive units).");
                        continue;
                    }
                    // Slice 24: a live delivery is a claim on a shipped order. A
                    // settled delivery (Received) is history and loads as-is; a live
                    // one loads only when its order exists and is InTransit — the
                    // only stage a live delivery legitimately pairs with. Anything
                    // else (missing order, Ordered/Cancelled/Received order) is an
                    // inconsistent save: skipping it keeps AdvanceDay from receiving
                    // stock for free (missing order prices at 0c) or twice (an order
                    // already Received). Skipped deliveries are diagnosed, never
                    // silently dropped.
                    if (!delivery.Received)
                    {
                        StoreProcurementOrder order = FindOrder(delivery.OrderId);
                        if (order == null)
                        {
                            diagnostics.Add($"StoreRestockService: skipped saved delivery '{delivery.DeliveryId}' — its order '{delivery.OrderId}' is missing.");
                            continue;
                        }
                        if (order.Stage != StoreProcurementStage.InTransit)
                        {
                            diagnostics.Add($"StoreRestockService: skipped saved delivery '{delivery.DeliveryId}' — its order '{delivery.OrderId}' is {order.Stage}, not InTransit.");
                            continue;
                        }
                    }
                    deliveries.Add(CopyDelivery(delivery));
                }
            }
            nextOrderSequence = Math.Max(0, dto.NextOrderSequence);
            nextDeliverySequence = Math.Max(0, dto.NextDeliverySequence);
            nextShipmentGroupSequence = Math.Max(0, dto.NextShipmentGroupSequence);
        }

        private static bool IsRestorableOrder(StoreProcurementOrder order)
        {
            if (order == null) return false;
            if (string.IsNullOrWhiteSpace(order.OrderId)) return false;
            if (string.IsNullOrWhiteSpace(order.StoreBusinessId)) return false;
            if (string.IsNullOrWhiteSpace(order.CategoryId)) return false;
            if (order.UnitsOrdered <= 0) return false;
            if (order.UnitCostCents < 0 || order.DeliveredCostCentsPerUnit < 0) return false;
            if (!Enum.IsDefined(typeof(StoreProcurementStage), order.Stage)) return false;
            return true;
        }

        private static bool IsRestorableDelivery(StoreDelivery delivery)
        {
            if (delivery == null) return false;
            if (string.IsNullOrWhiteSpace(delivery.DeliveryId)) return false;
            if (string.IsNullOrWhiteSpace(delivery.OrderId)) return false;
            if (string.IsNullOrWhiteSpace(delivery.CategoryId)) return false;
            // Units is the units still to receive: zero is legitimate for
            // a fully received delivery (Received = true). Negative is not.
            if (delivery.Units < 0) return false;
            return true;
        }

        private static StoreProcurementOrder CopyOrder(StoreProcurementOrder source)
        {
            return new StoreProcurementOrder
            {
                OrderId = source.OrderId ?? string.Empty,
                StoreBusinessId = source.StoreBusinessId ?? string.Empty,
                CategoryId = source.CategoryId ?? string.Empty,
                ItemId = source.ItemId ?? string.Empty,
                UnitsOrdered = source.UnitsOrdered,
                UnitCostCents = source.UnitCostCents,
                DeliveredCostCentsPerUnit = source.DeliveredCostCentsPerUnit,
                UpstreamSupplierId = source.UpstreamSupplierId ?? string.Empty,
                UpstreamSupplierName = source.UpstreamSupplierName ?? string.Empty,
                OrderDayIndex = source.OrderDayIndex,
                ExpectedArrivalDayIndex = source.ExpectedArrivalDayIndex,
                Stage = source.Stage,
                Provenance = source.Provenance ?? string.Empty,
                CancelReason = source.CancelReason ?? string.Empty,
                ShipmentGroupId = source.ShipmentGroupId ?? string.Empty,
            };
        }

        private static StoreDelivery CopyDelivery(StoreDelivery source)
        {
            return new StoreDelivery
            {
                DeliveryId = source.DeliveryId ?? string.Empty,
                OrderId = source.OrderId ?? string.Empty,
                CategoryId = source.CategoryId ?? string.Empty,
                ItemId = source.ItemId ?? string.Empty,
                Units = source.Units,
                ShippedDayIndex = source.ShippedDayIndex,
                TransitMinutes = source.TransitMinutes,
                Arrived = source.Arrived,
                Received = source.Received,
                Provenance = source.Provenance ?? string.Empty,
            };
        }
    }
}
