using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.Hotel
{
    /// <summary>
    /// D2A: one buyer-side standing supply agreement for the hotel's dining
    /// room pantry. Canon §8.1B: "Supply agreements with butchers, stores,
    /// farms or owned businesses can stabilize service through real
    /// procurement." The order names the supplier business and the
    /// SELLER-side agreement it is placed against (e.g. a bakery's
    /// wholesale account id for bread). Due-date evaluation only — the
    /// agreement never auto-fulfills; the caller places real orders and
    /// receives real lots with provenance. Mirrors the boarding-house
    /// standing orders (D1F).
    /// </summary>
    [Serializable]
    public sealed class HotelStandingOrder
    {
        public string OrderId = string.Empty;

        /// <summary>hotel-meat / hotel-bread / hotel-produce / hotel-dairy.</summary>
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

        public HotelStandingOrder() { }

        public bool IsDue(int dayIndex)
        {
            return IsActive && UnitsPerDelivery > 0 && CadenceDays > 0 && dayIndex >= NextDueDayIndex;
        }
    }

    /// <summary>
    /// D2A: the dining room's reorder policy — threshold signals per food
    /// kind. The bootstrap pantry endowment is one-time and lots never
    /// auto-replenish (upstream-provenance doctrine), so the hotel needs a
    /// visible signal before the pantry runs dry. Signals produce DATA;
    /// the caller places real orders (standing orders above, or import
    /// orders). All values are TUNING (Canon Part XV), settable per
    /// hotel. Mirrors the boarding-house resupply policy (D1F).
    /// </summary>
    [Serializable]
    public sealed class HotelResupplyPolicy
    {
        /// <summary>TUNING: meat lb at/below which a reorder signal fires.</summary>
        public int MeatThresholdUnits = 16;

        /// <summary>TUNING: suggested meat lb on the reorder.</summary>
        public int MeatOrderUnits = 48;

        /// <summary>TUNING: bread loaves at/below which a reorder signal fires.</summary>
        public int BreadThresholdUnits = 14;

        /// <summary>TUNING: suggested bread loaves on the reorder.</summary>
        public int BreadOrderUnits = 40;

        /// <summary>TUNING: produce units at/below which a reorder signal fires.</summary>
        public int ProduceThresholdUnits = 12;

        /// <summary>TUNING: suggested produce units on the reorder.</summary>
        public int ProduceOrderUnits = 32;

        /// <summary>TUNING: dairy units at/below which a reorder signal fires.</summary>
        public int DairyThresholdUnits = 10;

        /// <summary>TUNING: suggested dairy units on the reorder.</summary>
        public int DairyOrderUnits = 28;

        public HotelResupplyPolicy() { }
    }

    /// <summary>
    /// D2A: one reorder signal — the pantry for a food kind is at or below
    /// its threshold and a real order is due. This is DATA for the
    /// caller, not an automatic order (upstream-provenance doctrine: the
    /// caller buys from real suppliers).
    /// </summary>
    [Serializable]
    public sealed class HotelReorderSignal
    {
        public string FoodName = string.Empty;
        public int UnitsOnHand;
        public int SuggestedOrderUnits;
        public int DayIndex;

        public HotelReorderSignal() { }
    }

    /// <summary>
    /// D2A: the hotel's standing-order book plus its resupply policy. The
    /// book holds buyer-side agreements; the policy turns pantry levels
    /// into reorder signals. Neither fulfills anything — fulfillment is
    /// real lots received through the food stock with provenance.
    /// </summary>
    public sealed class HotelStandingOrders
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<HotelStandingOrder> orders = new List<HotelStandingOrder>();
        private readonly HotelResupplyPolicy resupplyPolicy = new HotelResupplyPolicy();
        private int orderSequence = 1;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<HotelStandingOrder> Orders => orders;
        public HotelResupplyPolicy ResupplyPolicy => resupplyPolicy;

        /// <summary>
        /// Places a standing supply agreement with a real supplier.
        /// Returns the order id, or null plus a loud refusal.
        /// </summary>
        public string PlaceOrder(string foodName, string supplierBusinessId, string supplierKind,
            string supplierAccountId, int unitsPerDelivery, int cadenceDays, int nextDueDayIndex,
            int pricePerUnitCents, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(foodName))
                return "HotelStandingOrders.PlaceOrder: the order must name a food.";
            if (string.IsNullOrWhiteSpace(supplierBusinessId))
                return "HotelStandingOrders.PlaceOrder: the order must name a real supplier business.";
            if (unitsPerDelivery <= 0)
                return "HotelStandingOrders.PlaceOrder: the order needs positive units per delivery.";
            if (cadenceDays <= 0)
                return "HotelStandingOrders.PlaceOrder: the order needs a positive cadence in days.";
            if (pricePerUnitCents < 0)
                return "HotelStandingOrders.PlaceOrder: the price cannot be negative.";

            var order = new HotelStandingOrder
            {
                OrderId = $"htl-so-{orderSequence++}",
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
            diag.Add($"HotelStandingOrders: {order.OrderId} — {unitsPerDelivery} × {foodName} every {cadenceDays}d " +
                $"from '{supplierBusinessId}' at {pricePerUnitCents}¢/unit, first due day {nextDueDayIndex}.");
            return order.OrderId;
        }

        /// <summary>Cancels a standing order (history stays on the books).</summary>
        public string CancelOrder(string orderId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            foreach (HotelStandingOrder order in orders)
            {
                if (order != null && string.Equals(order.OrderId, orderId, StringComparison.OrdinalIgnoreCase))
                {
                    order.IsActive = false;
                    diag.Add($"HotelStandingOrders: {orderId} cancelled (day {dayIndex}).");
                    return null;
                }
            }

            return $"HotelStandingOrders.CancelOrder: no standing order '{orderId}'.";
        }

        /// <summary>All active orders due on the given day — the caller's shopping list, never an auto-order.</summary>
        public List<HotelStandingOrder> OrdersDue(int dayIndex)
        {
            var due = new List<HotelStandingOrder>();
            foreach (HotelStandingOrder order in orders)
            {
                if (order != null && order.IsDue(dayIndex)) due.Add(order);
            }

            return due;
        }

        /// <summary>
        /// Advances an order's next-due day after the caller received a real
        /// delivery (the caller tells the book the delivery happened — the
        /// book never assumes it).
        /// </summary>
        public string MarkDelivered(string orderId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            foreach (HotelStandingOrder order in orders)
            {
                if (order != null && string.Equals(order.OrderId, orderId, StringComparison.OrdinalIgnoreCase))
                {
                    order.NextDueDayIndex = Math.Max(dayIndex + 1, order.NextDueDayIndex) + Math.Max(1, order.CadenceDays);
                    diag.Add($"HotelStandingOrders: {orderId} delivery recorded (day {dayIndex}) — next due day {order.NextDueDayIndex}.");
                    return null;
                }
            }

            return $"HotelStandingOrders.MarkDelivered: no standing order '{orderId}'.";
        }

        /// <summary>
        /// Evaluates the pantry against the resupply policy and emits
        /// reorder signals — DATA for the caller, never orders.
        /// </summary>
        public List<HotelReorderSignal> EvaluateSignals(HotelFoodStock foodStock, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var signals = new List<HotelReorderSignal>();
            if (foodStock == null)
            {
                diag.Add("HotelStandingOrders: no pantry offered — no reorder signals evaluated.");
                return signals;
            }

            CheckSignal(foodStock, HotelFoodSupply.MeatMaterialId,
                resupplyPolicy.MeatThresholdUnits, resupplyPolicy.MeatOrderUnits, dayIndex, signals);
            CheckSignal(foodStock, HotelFoodSupply.BreadMaterialId,
                resupplyPolicy.BreadThresholdUnits, resupplyPolicy.BreadOrderUnits, dayIndex, signals);
            CheckSignal(foodStock, HotelFoodSupply.ProduceMaterialId,
                resupplyPolicy.ProduceThresholdUnits, resupplyPolicy.ProduceOrderUnits, dayIndex, signals);
            CheckSignal(foodStock, HotelFoodSupply.DairyMaterialId,
                resupplyPolicy.DairyThresholdUnits, resupplyPolicy.DairyOrderUnits, dayIndex, signals);

            if (signals.Count == 0)
                diag.Add($"HotelStandingOrders: pantry above all reorder thresholds (day {dayIndex}).");
            else
                diag.Add($"HotelStandingOrders: {signals.Count} reorder signal(s) (day {dayIndex}) — the caller places real orders.");
            return signals;
        }

        private static void CheckSignal(HotelFoodStock foodStock, string foodName, int threshold, int orderUnits,
            int dayIndex, List<HotelReorderSignal> signals)
        {
            int onHand = foodStock.UnitsOnHand(foodName);
            if (onHand <= Math.Max(0, threshold))
            {
                signals.Add(new HotelReorderSignal
                {
                    FoodName = foodName,
                    UnitsOnHand = onHand,
                    SuggestedOrderUnits = Math.Max(1, orderUnits),
                    DayIndex = dayIndex,
                });
            }
        }

        #region Save / Load
        [Serializable]
        public sealed class HotelStandingOrdersSaveDto
        {
            public int OrderSequence = 1;
            public List<HotelStandingOrder> Orders = new List<HotelStandingOrder>();
            public HotelResupplyPolicy ResupplyPolicy = new HotelResupplyPolicy();
        }

        public HotelStandingOrdersSaveDto CaptureSaveDto()
        {
            var dto = new HotelStandingOrdersSaveDto
            {
                OrderSequence = Math.Max(1, orderSequence),
                ResupplyPolicy = new HotelResupplyPolicy
                {
                    MeatThresholdUnits = resupplyPolicy.MeatThresholdUnits,
                    MeatOrderUnits = resupplyPolicy.MeatOrderUnits,
                    BreadThresholdUnits = resupplyPolicy.BreadThresholdUnits,
                    BreadOrderUnits = resupplyPolicy.BreadOrderUnits,
                    ProduceThresholdUnits = resupplyPolicy.ProduceThresholdUnits,
                    ProduceOrderUnits = resupplyPolicy.ProduceOrderUnits,
                    DairyThresholdUnits = resupplyPolicy.DairyThresholdUnits,
                    DairyOrderUnits = resupplyPolicy.DairyOrderUnits,
                },
            };
            foreach (HotelStandingOrder order in orders)
            {
                if (order != null) dto.Orders.Add(order);
            }

            return dto;
        }

        public void LoadFromSaveDto(HotelStandingOrdersSaveDto dto)
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
            foreach (HotelStandingOrder order in dto.Orders)
            {
                if (order == null || string.IsNullOrWhiteSpace(order.OrderId)) continue;
                orders.Add(order);
            }
        }
        #endregion
    }
}
