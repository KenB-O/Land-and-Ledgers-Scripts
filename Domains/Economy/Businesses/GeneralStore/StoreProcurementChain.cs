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

    /// <summary>Phase C: one real supplier order from the store to upstream.</summary>
    [Serializable]
    public sealed class StoreProcurementOrder
    {
        public string OrderId = string.Empty;
        public string StoreBusinessId = string.Empty;
        public string CategoryId = string.Empty;
        public int UnitsOrdered;
        public int UnitCostCents;
        public string UpstreamSupplierId = string.Empty;
        public string UpstreamSupplierName = string.Empty;
        public int OrderDayIndex;
        public int ExpectedArrivalDayIndex;
        public StoreProcurementStage Stage;
        public string Provenance = string.Empty;
        public string CancelReason = string.Empty;
    }

    /// <summary>Phase C: one delivery in transit — goods exist but are NOT store stock yet.</summary>
    [Serializable]
    public sealed class StoreDelivery
    {
        public string DeliveryId = string.Empty;
        public string OrderId = string.Empty;
        public string CategoryId = string.Empty;
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

        public IReadOnlyList<StoreProcurementOrder> Orders => orders;
        public IReadOnlyList<StoreDelivery> Deliveries => deliveries;
        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Daily review: for each stocked category below the reorder
        /// threshold with no open order, place a real order with the best
        /// upstream supplier. Never duplicates an open order per category.
        /// </summary>
        public void EvaluateRestock(
            IGeneralStoreTradingPort store,
            List<IUpstreamGoodsSupplier> upstreamSuppliers,
            int dayIndex,
            float reorderThreshold01)
        {
            if (store == null || store.CategoryOffers == null)
            {
                return;
            }

            for (int i = 0; i < store.CategoryOffers.Count; i++)
            {
                GeneralStoreCategoryOffer offer = store.CategoryOffers[i];
                if (offer == null || string.IsNullOrWhiteSpace(offer.CategoryId))
                {
                    continue;
                }

                string categoryId = offer.CategoryId;
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

                if (HasOpenOrder(store.StoreBusinessId, categoryId))
                {
                    continue;
                }

                int missing = Math.Max(0, target - current);
                if (missing <= 0)
                {
                    continue;
                }

                IUpstreamGoodsSupplier best = ChooseSupplier(upstreamSuppliers, categoryId, missing, dayIndex);
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

                int leadDays = Math.Max(0, best.LeadTimeDays(categoryId));
                var order = new StoreProcurementOrder
                {
                    OrderId = $"po-{store.StoreBusinessId}-{dayIndex}-{nextOrderSequence++}",
                    StoreBusinessId = store.StoreBusinessId,
                    CategoryId = categoryId,
                    UnitsOrdered = orderUnits,
                    UnitCostCents = unitCost,
                    UpstreamSupplierId = best.SupplierId,
                    UpstreamSupplierName = best.SupplierName,
                    OrderDayIndex = Math.Max(0, dayIndex),
                    ExpectedArrivalDayIndex = Math.Max(0, dayIndex) + leadDays,
                    Stage = StoreProcurementStage.Ordered,
                    Provenance = $"order {orderUnits}u {categoryId} from {best.SupplierName} @ {unitCost}c/u",
                };
                order.Provenance = $"order {order.OrderId}: " + order.Provenance;
                orders.Add(order);
                diagnostics.Add($"Day {dayIndex}: {order.Provenance} (arrives ~day {order.ExpectedArrivalDayIndex}).");
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

                int shipped = Math.Max(0, supplier.Ship(order.CategoryId, order.UnitsOrdered, dayIndex, outDiagnostics));
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
                int unitCost = order != null ? order.UnitCostCents : 0;
                int receivable = Math.Max(0, store.CategoryReceivableUnits(delivery.CategoryId));
                int unitsToReceive = Math.Min(delivery.Units, receivable);
                if (unitsToReceive <= 0)
                {
                    outDiagnostics.Add($"Day {dayIndex}: {delivery.DeliveryId} WAITING — no receiving capacity for '{delivery.CategoryId}'. No stock booked.");
                    continue;
                }

                int cost = unitsToReceive * Math.Max(0, unitCost);
                string payProblem = store.SpendCash(cost, $"restock {unitsToReceive}u {delivery.CategoryId} ({delivery.DeliveryId})");
                if (payProblem != null)
                {
                    outDiagnostics.Add($"Day {dayIndex}: {delivery.DeliveryId} WAITING — {payProblem} No stock booked without payment.");
                    continue;
                }

                string provenance = $"{delivery.Provenance} → received day {dayIndex} for {cost}c";
                string receiveProblem = store.ReceiveStock(delivery.CategoryId, unitsToReceive, provenance);
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
                    (delivery.Units > 0 ? $" {delivery.Units}u still at the dock." : string.Empty));
            }
        }

        private bool HasOpenOrder(string storeBusinessId, string categoryId)
        {
            for (int i = 0; i < orders.Count; i++)
            {
                StoreProcurementOrder order = orders[i];
                if (order != null &&
                    string.Equals(order.StoreBusinessId, storeBusinessId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(order.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase) &&
                    (order.Stage == StoreProcurementStage.Ordered || order.Stage == StoreProcurementStage.InTransit))
                {
                    return true;
                }
            }

            return false;
        }

        private static IUpstreamGoodsSupplier ChooseSupplier(
            List<IUpstreamGoodsSupplier> suppliers, string categoryId, int unitsNeeded, int dayIndex)
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

                if (quote < bestCost)
                {
                    bestCost = quote;
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
    }
}
