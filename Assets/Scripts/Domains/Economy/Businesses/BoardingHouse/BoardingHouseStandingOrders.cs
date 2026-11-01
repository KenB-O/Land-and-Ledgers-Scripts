using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.BoardingHouse
{
    /// <summary>
    /// D1F: one buyer-side standing supply agreement for the boarding-house
    /// pantry. Canon §8.1B: "Supply agreements with butchers, stores, farms
    /// or owned businesses can stabilize service through real procurement."
    /// The order names the supplier business and the SELLER-side agreement
    /// it is placed against (e.g. the D1D bakery's wholesale account id for
    /// bread). Due-date evaluation only — the agreement never auto-fulfills;
    /// the caller places real orders and receives real lots with provenance.
    /// Mirrors <see cref="LandLedgers.Economy.Businesses.Restaurant.RestaurantStandingOrder"/> (D1E).
    /// </summary>
    [Serializable]
    public sealed class BoardingHouseStandingOrder
    {
        public string OrderId = string.Empty;

        /// <summary>boardinghouse-meat / boardinghouse-bread / boardinghouse-produce / boardinghouse-dairy.</summary>
        public string FoodName = string.Empty;

        /// <summary>The named supplier business (butcher, bakery, farm, dairy).</summary>
        public string SupplierBusinessId = string.Empty;

        /// <summary>Supplier kind: "butcher", "bakery", "farm", "dairy", "import".</summary>
        public string SupplierKind = string.Empty;

        /// <summary>The seller-side agreement this order is placed against (e.g. the bakery's wholesale account id).</summary>
        public string SupplierAccountId = string.Empty;

        /// <summary>Units per delivery (lb, loaves, produce units, dairy units).</summary>
        public int UnitsPerDelivery;

        /// <summary>Days between deliveries (1 = daily).</summary>
        public int CadenceDays = 1;

        /// <summary>The next day a delivery is due.</summary>
        public int NextDueDayIndex;

        /// <summary>Negotiated price per unit, in cents (calibration — the agreed terms).</summary>
        public int PricePerUnitCents;

        public bool IsActive = true;

        public BoardingHouseStandingOrder() { }

        public bool IsDue(int dayIndex)
        {
            return IsActive && UnitsPerDelivery > 0 && CadenceDays > 0 && dayIndex >= NextDueDayIndex;
        }
    }

    /// <summary>
    /// D1F: the pantry's reorder policy — threshold signals per food kind.
    /// The bootstrap endowment is one-time and lots never auto-replenish
    /// (upstream-provenance doctrine), so the house needs a visible signal
    /// before the pantry runs dry. Signals produce DATA; the caller places
    /// real orders (standing orders above, or import orders). Mirrors the
    /// D1E restaurant resupply policy. All values are TUNING (Canon Part XV),
    /// settable per house.
    /// </summary>
    [Serializable]
    public sealed class BoardingHouseResupplyPolicy
    {
        /// <summary>TUNING: meat lb at/below which a reorder signal fires.</summary>
        public int MeatThresholdUnits = 12;

        /// <summary>TUNING: suggested meat lb on the reorder.</summary>
        public int MeatOrderUnits = 36;

        /// <summary>TUNING: bread loaves at/below which a reorder signal fires.</summary>
        public int BreadThresholdUnits = 10;

        /// <summary>TUNING: suggested bread loaves on the reorder.</summary>
        public int BreadOrderUnits = 30;

        /// <summary>TUNING: produce units at/below which a reorder signal fires.</summary>
        public int ProduceThresholdUnits = 8;

        /// <summary>TUNING: suggested produce units on the reorder.</summary>
        public int ProduceOrderUnits = 24;

        /// <summary>TUNING: dairy units at/below which a reorder signal fires.</summary>
        public int DairyThresholdUnits = 8;

        /// <summary>TUNING: suggested dairy units on the reorder.</summary>
        public int DairyOrderUnits = 24;

        public BoardingHouseResupplyPolicy() { }
    }

    /// <summary>D1F: one reorder signal — a fact that the pantry is at or below its threshold, never an order.</summary>
    [Serializable]
    public sealed class BoardingHouseResupplySignal
    {
        public string FoodName = string.Empty;
        public int UnitsOnHand;
        public int ThresholdUnits;
        public int SuggestedOrderUnits;
        public int DayIndex;

        public BoardingHouseResupplySignal() { }
    }

    /// <summary>
    /// D1F: the house's standing-order book plus resupply-signal evaluation.
    /// Orders are buyer-side facts; evaluation reports what is DUE (the
    /// caller places the real order) and what the pantry is SHORT of (the
    /// caller reorders). Nothing here moves goods or money — money and
    /// goods move only through ledger authorities and real lot receipts.
    /// </summary>
    public sealed class BoardingHouseStandingOrders
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<BoardingHouseStandingOrder> orders = new List<BoardingHouseStandingOrder>();
        private readonly BoardingHouseResupplyPolicy resupplyPolicy = new BoardingHouseResupplyPolicy();
        private int orderSequence = 1;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<BoardingHouseStandingOrder> Orders => orders;
        public BoardingHouseResupplyPolicy ResupplyPolicy => resupplyPolicy;

        /// <summary>
        /// Records a standing supply agreement. The supplier account id
        /// should name the seller-side agreement (e.g. the bakery's wholesale
        /// account) so the order is traceable to real supply. Returns the
        /// order id, or null plus a loud refusal.
        /// </summary>
        public string AddStandingOrder(string foodName, string supplierBusinessId, string supplierKind,
            string supplierAccountId, int unitsPerDelivery, int cadenceDays, int nextDueDayIndex,
            int pricePerUnitCents, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(foodName))
                return "BoardingHouseStandingOrders.AddStandingOrder: an order needs a food name.";
            if (string.IsNullOrWhiteSpace(supplierBusinessId))
                return "BoardingHouseStandingOrders.AddStandingOrder: an order needs a real supplier business — anonymous supply is not ordered.";
            if (unitsPerDelivery <= 0)
                return "BoardingHouseStandingOrders.AddStandingOrder: an order needs a positive delivery size.";
            if (cadenceDays <= 0)
                return "BoardingHouseStandingOrders.AddStandingOrder: an order needs a positive cadence.";
            if (nextDueDayIndex < 0)
                return "BoardingHouseStandingOrders.AddStandingOrder: an order needs a real first due day.";
            if (pricePerUnitCents < 0)
                return "BoardingHouseStandingOrders.AddStandingOrder: the agreed price cannot be negative.";

            var order = new BoardingHouseStandingOrder
            {
                OrderId = $"bh-order-{orderSequence++}",
                FoodName = foodName,
                SupplierBusinessId = supplierBusinessId,
                SupplierKind = supplierKind ?? string.Empty,
                SupplierAccountId = supplierAccountId ?? string.Empty,
                UnitsPerDelivery = unitsPerDelivery,
                CadenceDays = cadenceDays,
                NextDueDayIndex = nextDueDayIndex,
                PricePerUnitCents = pricePerUnitCents,
                IsActive = true,
            };
            orders.Add(order);
            diag.Add($"BoardingHouseStandingOrders: standing order {order.OrderId} recorded — {unitsPerDelivery} × {foodName} " +
                $"every {cadenceDays}d from {supplierBusinessId} (account '{order.SupplierAccountId}') at {pricePerUnitCents}¢/unit, first due day {nextDueDayIndex}.");
            return order.OrderId;
        }

        /// <summary>Deactivates a standing order (history kept). Returns the refusal, or null.</summary>
        public string CancelStandingOrder(string orderId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            for (int i = 0; i < orders.Count; i++)
            {
                BoardingHouseStandingOrder order = orders[i];
                if (order != null && string.Equals(order.OrderId, orderId, StringComparison.OrdinalIgnoreCase) && order.IsActive)
                {
                    order.IsActive = false;
                    diag.Add($"BoardingHouseStandingOrders: order {orderId} cancelled (day {dayIndex}) — history kept.");
                    return null;
                }
            }
            return $"BoardingHouseStandingOrders.CancelStandingOrder: no active order '{orderId}'.";
        }

        /// <summary>
        /// Orders due on the given day. DUE IS NOT FULFILLED: the caller
        /// places the real order against the supplier and receives real
        /// lots. Due orders advance their NextDueDayIndex by cadence so the
        /// book stays honest even when nobody acts on them.
        /// </summary>
        public List<BoardingHouseStandingOrder> EvaluateDueOrders(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var due = new List<BoardingHouseStandingOrder>();
            foreach (BoardingHouseStandingOrder order in orders)
            {
                if (order == null || !order.IsDue(dayIndex)) continue;
                due.Add(order);
                order.NextDueDayIndex = dayIndex + Math.Max(1, order.CadenceDays);
                diag.Add($"BoardingHouseStandingOrders: order {order.OrderId} is DUE (day {dayIndex}) — {order.UnitsPerDelivery} × " +
                    $"{order.FoodName} from {order.SupplierBusinessId}; place the real order, receive real lots. Next due day {order.NextDueDayIndex}.");
            }
            return due;
        }

        /// <summary>
        /// Threshold signals for the pantry: facts that stock is at/below
        /// threshold, never orders. The caller reorders through real supply.
        /// </summary>
        public List<BoardingHouseResupplySignal> EvaluateResupplySignals(BoardingHouseFoodStock foodStock, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var signals = new List<BoardingHouseResupplySignal>();
            if (foodStock == null)
            {
                diag.Add("BoardingHouseStandingOrders: no pantry — no resupply signals.");
                return signals;
            }

            CheckThreshold(foodStock, BoardingHouseFoodSupply.MeatMaterialId,
                resupplyPolicy.MeatThresholdUnits, resupplyPolicy.MeatOrderUnits, dayIndex, signals, diag);
            CheckThreshold(foodStock, BoardingHouseFoodSupply.BreadMaterialId,
                resupplyPolicy.BreadThresholdUnits, resupplyPolicy.BreadOrderUnits, dayIndex, signals, diag);
            CheckThreshold(foodStock, BoardingHouseFoodSupply.ProduceMaterialId,
                resupplyPolicy.ProduceThresholdUnits, resupplyPolicy.ProduceOrderUnits, dayIndex, signals, diag);
            CheckThreshold(foodStock, BoardingHouseFoodSupply.DairyMaterialId,
                resupplyPolicy.DairyThresholdUnits, resupplyPolicy.DairyOrderUnits, dayIndex, signals, diag);
            return signals;
        }

        private static void CheckThreshold(BoardingHouseFoodStock foodStock, string foodName, int threshold, int orderUnits,
            int dayIndex, List<BoardingHouseResupplySignal> signals, List<string> diag)
        {
            int onHand = foodStock.UnitsOnHand(foodName);
            if (onHand <= Math.Max(0, threshold))
            {
                signals.Add(new BoardingHouseResupplySignal
                {
                    FoodName = foodName,
                    UnitsOnHand = onHand,
                    ThresholdUnits = Math.Max(0, threshold),
                    SuggestedOrderUnits = Math.Max(0, orderUnits),
                    DayIndex = dayIndex,
                });
                diag.Add($"BoardingHouseStandingOrders: RESUPPLY SIGNAL — {foodName} at {onHand} (threshold {threshold}); " +
                    $"suggested reorder {orderUnits}. Signal only — place a real order.");
            }
        }

        #region Save / Load
        [Serializable]
        public sealed class BoardingHouseStandingOrdersSaveDto
        {
            public int OrderSequence = 1;
            public List<BoardingHouseStandingOrder> Orders = new List<BoardingHouseStandingOrder>();
            public BoardingHouseResupplyPolicy ResupplyPolicy = new BoardingHouseResupplyPolicy();
        }

        public BoardingHouseStandingOrdersSaveDto CaptureSaveDto()
        {
            var dto = new BoardingHouseStandingOrdersSaveDto
            {
                OrderSequence = Math.Max(1, orderSequence),
                ResupplyPolicy = resupplyPolicy,
            };
            foreach (BoardingHouseStandingOrder order in orders)
            {
                if (order != null) dto.Orders.Add(order);
            }
            return dto;
        }

        public void LoadFromSaveDto(BoardingHouseStandingOrdersSaveDto dto)
        {
            orders.Clear();
            orderSequence = 1;
            if (dto == null) return;
            orderSequence = Math.Max(1, dto.OrderSequence);
            if (dto.ResupplyPolicy != null)
            {
                resupplyPolicy.MeatThresholdUnits = dto.ResupplyPolicy.MeatThresholdUnits;
                resupplyPolicy.MeatOrderUnits = dto.ResupplyPolicy.MeatOrderUnits;
                resupplyPolicy.BreadThresholdUnits = dto.ResupplyPolicy.BreadThresholdUnits;
                resupplyPolicy.BreadOrderUnits = dto.ResupplyPolicy.BreadOrderUnits;
                resupplyPolicy.ProduceThresholdUnits = dto.ResupplyPolicy.ProduceThresholdUnits;
                resupplyPolicy.ProduceOrderUnits = dto.ResupplyPolicy.ProduceOrderUnits;
                resupplyPolicy.DairyThresholdUnits = dto.ResupplyPolicy.DairyThresholdUnits;
                resupplyPolicy.DairyOrderUnits = dto.ResupplyPolicy.DairyOrderUnits;
            }
            if (dto.Orders == null) return;
            foreach (BoardingHouseStandingOrder order in dto.Orders)
            {
                if (order == null || string.IsNullOrWhiteSpace(order.OrderId)) continue;
                orders.Add(order);
            }
        }
        #endregion
    }
}
