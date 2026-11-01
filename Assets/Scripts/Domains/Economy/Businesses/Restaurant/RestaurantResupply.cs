using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.Restaurant
{
    /// <summary>
    /// D1E: one standing ingredient order — the BUYER side of Canon §8.1B
    /// ("supply agreements with butchers, stores, farms or owned businesses
    /// can stabilize service through real procurement") and Canon §7.3E
    /// (repeat buyers create account demand). This is the restaurant's
    /// procurement POLICY — what it wants, from whom, how often, at what
    /// agreed price. It is not fulfillment: the seller (bakery wholesale
    /// account, butcher, farm, dairy, or an import order) still moves real
    /// goods, and money still moves only through ledger authorities.
    ///
    /// Integration (D1D): a bread standing order names the bakery's
    /// <c>BakeryWholesaleAccount.AccountId</c> in <see cref="SupplierAccountId"/>
    /// and BuyerBusinessId/BuyerType match the account's buyer fields; the
    /// bakery's FulfillWholesale ships, and the pantry intake uses the
    /// existing buyer adapter (RestaurantFoodSupply.ReceiveBakeryBreadLot).
    /// </summary>
    [Serializable]
    public sealed class RestaurantStandingOrder
    {
        public string OrderId = string.Empty;

        /// <summary>restaurant-meat / restaurant-bread / restaurant-produce / restaurant-dairy.</summary>
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

        public RestaurantStandingOrder() { }

        public bool IsDue(int dayIndex)
        {
            return IsActive && UnitsPerDelivery > 0 && CadenceDays > 0 && dayIndex >= NextDueDayIndex;
        }
    }

    /// <summary>
    /// D1E: the pantry's reorder policy — threshold signals per food kind.
    /// The bootstrap endowment is one-time and lots never auto-replenish
    /// (upstream-provenance doctrine), so the house needs a visible signal
    /// before the pantry runs dry. Signals produce DATA; the caller places
    /// real orders (standing orders above, or ImportService). Mirrors the D1C
    /// tailor and D1B barber reorder patterns. All values are calibration
    /// (Canon Part XV), settable per shop.
    /// </summary>
    [Serializable]
    public sealed class RestaurantResupplyPolicy
    {
        /// <summary>TUNING: meat lb at/below which a reorder signal fires.</summary>
        public int MeatThresholdUnits = 10;

        /// <summary>TUNING: suggested meat lb on the reorder.</summary>
        public int MeatOrderUnits = 30;

        /// <summary>TUNING: bread loaves at/below which a reorder signal fires.</summary>
        public int BreadThresholdUnits = 8;

        /// <summary>TUNING: suggested bread loaves on the reorder.</summary>
        public int BreadOrderUnits = 24;

        /// <summary>TUNING: produce units at/below which a reorder signal fires.</summary>
        public int ProduceThresholdUnits = 10;

        /// <summary>TUNING: suggested produce units on the reorder.</summary>
        public int ProduceOrderUnits = 30;

        /// <summary>TUNING: dairy units at/below which a reorder signal fires.</summary>
        public int DairyThresholdUnits = 6;

        /// <summary>TUNING: suggested dairy units on the reorder.</summary>
        public int DairyOrderUnits = 16;

        public RestaurantResupplyPolicy() { }
    }

    /// <summary>D1E: one pantry reorder signal — a food kind at or below its threshold.</summary>
    [Serializable]
    public sealed class RestaurantResupplySignal
    {
        public string FoodName = string.Empty;
        public int UnitsOnHand;
        public int ThresholdUnits;
        public int SuggestedOrderUnits;
        public int DayIndex;

        /// <summary>Days a real import order takes to arrive (restaurant supply registers 14 transit days).</summary>
        public int LeadTimeDays;

        public string Reason = string.Empty;

        public RestaurantResupplySignal() { }
    }

    /// <summary>D1E: evaluates pantry reorder signals and standing-order due
    /// dates. Signals never order — ordering is the caller's real standing
    /// order or import order; the pantry is never silently replenished.</summary>
    public static class RestaurantResupplyEvaluator
    {
        /// <summary>TUNING: transit days for a real food import order (RestaurantFoodSupply: 14 days via railhead).</summary>
        public const int ImportLeadTimeDays = 14;

        /// <summary>
        /// Returns a signal for each food kind at or below its threshold.
        /// Empty list = the pantry is fine.
        /// </summary>
        public static List<RestaurantResupplySignal> EvaluateSignals(
            RestaurantFoodStock stock,
            RestaurantResupplyPolicy policy,
            int dayIndex,
            List<string> diagnostics)
        {
            var signals = new List<RestaurantResupplySignal>();
            diagnostics = diagnostics ?? new List<string>();
            policy = policy ?? new RestaurantResupplyPolicy();
            if (stock == null)
            {
                diagnostics.Add("RestaurantResupplyEvaluator: no pantry — resupply not evaluated.");
                return signals;
            }

            CheckFood(stock, RestaurantMealCatalog.MeatItemId,
                policy.MeatThresholdUnits, policy.MeatOrderUnits, dayIndex, signals, diagnostics);
            CheckFood(stock, RestaurantMealCatalog.BreadItemId,
                policy.BreadThresholdUnits, policy.BreadOrderUnits, dayIndex, signals, diagnostics);
            CheckFood(stock, RestaurantMealCatalog.ProduceItemId,
                policy.ProduceThresholdUnits, policy.ProduceOrderUnits, dayIndex, signals, diagnostics);
            CheckFood(stock, RestaurantMealCatalog.DairyItemId,
                policy.DairyThresholdUnits, policy.DairyOrderUnits, dayIndex, signals, diagnostics);

            if (signals.Count == 0)
            {
                diagnostics.Add("RestaurantResupplyEvaluator: pantry above reorder thresholds — no signal.");
            }

            return signals;
        }

        private static void CheckFood(
            RestaurantFoodStock stock,
            string foodName,
            int threshold,
            int orderQty,
            int dayIndex,
            List<RestaurantResupplySignal> signals,
            List<string> diagnostics)
        {
            int onHand = stock.UnitsOnHand(foodName);
            if (onHand <= Math.Max(0, threshold))
            {
                signals.Add(new RestaurantResupplySignal
                {
                    FoodName = foodName,
                    UnitsOnHand = onHand,
                    ThresholdUnits = Math.Max(0, threshold),
                    SuggestedOrderUnits = Math.Max(1, orderQty),
                    DayIndex = dayIndex,
                    LeadTimeDays = ImportLeadTimeDays,
                    Reason = $"Pantry '{foodName}' at {onHand} units (threshold {Math.Max(0, threshold)}). " +
                        "Place a real standing order or import order — the pantry never self-replenishes.",
                });
                diagnostics.Add($"RestaurantResupplyEvaluator: reorder signal — '{foodName}' at {onHand} units (threshold {Math.Max(0, threshold)}).");
            }
        }

        /// <summary>
        /// Returns the standing orders due on the day. Due orders do NOT
        /// auto-advance: the caller places the real order (seller-side
        /// fulfillment, ledger settlement) and then calls
        /// <see cref="MarkOrderPlaced"/> to move the next due date.
        /// </summary>
        public static List<RestaurantStandingOrder> DueStandingOrders(
            List<RestaurantStandingOrder> orders,
            int dayIndex,
            List<string> diagnostics)
        {
            var due = new List<RestaurantStandingOrder>();
            diagnostics = diagnostics ?? new List<string>();
            if (orders == null) return due;

            foreach (var order in orders)
            {
                if (order != null && order.IsDue(dayIndex)) due.Add(order);
            }

            if (due.Count > 0)
            {
                diagnostics.Add($"RestaurantResupplyEvaluator: {due.Count} standing order(s) due on day {dayIndex} — place real orders; nothing auto-fulfills.");
            }

            return due;
        }

        /// <summary>
        /// Advances a standing order's next due date after the caller placed
        /// the real order. The goods still have to arrive through the
        /// seller's real fulfillment — this only moves the schedule.
        /// </summary>
        public static string MarkOrderPlaced(RestaurantStandingOrder order, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (order == null) return "RestaurantResupplyEvaluator: no standing order to advance.";
            order.NextDueDayIndex = dayIndex + Math.Max(1, order.CadenceDays);
            diagnostics.Add($"RestaurantResupplyEvaluator: standing order '{order.OrderId}' placed — next due day {order.NextDueDayIndex}.");
            return null;
        }
    }
}
