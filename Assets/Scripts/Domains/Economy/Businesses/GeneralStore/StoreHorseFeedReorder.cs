using System;
using System.Collections.Generic;
using LandLedgers.Economy.Core;
using LandLedgers.Population;

namespace LandLedgers.Economy.Businesses.GeneralStore
{
    /// <summary>Caller inputs for one day of General Store horse-feed reorder.</summary>
    public sealed class StoreHorseFeedReorderInputs
    {
        public OffMapNamedSkuProcurementService Service;
        public string SupplierId = string.Empty;
        public string SkuId = string.Empty;
        public int UnitPriceCents;
        /// <summary>Whole sacks per standing lot. Production uses the authored opening lot size.</summary>
        public int LotSacks;
        /// <summary>Retail sacks physically on the store shelf right now (not in-transit lots).</summary>
        public int StoreSacksOnHand;
        public int DayIndex;
    }

    /// <summary>Outcome of one day of horse-feed reorder.</summary>
    public sealed class StoreHorseFeedReorderResult
    {
        public bool Ordered;
        public NamedSkuOrder Order;
        public string Refusal = string.Empty;
    }

    /// <summary>
    /// Part B maintenance (2026-11-15): the General Store's standing
    /// horse-feed reorder — the replenishment the opening feed order
    /// never had. The opening order (FirstLedgerSliceBootstrapper
    /// .ConfigurePlaytestHorseFeed) buys 20 sacks of
    /// "store_horse_feed_sack" from Millbrook Grain Co. at 15c/sack,
    /// cash on arrival, 1-day transit, out of a finite external-market
    /// supply of exactly 20 sacks. Nothing ever reordered it: the
    /// external market had no supply-restock API at all, and no driver
    /// placed a second order. The store therefore sells its last sack
    /// and the freight horse (A0) starves — 20 sacks are 40 feed units,
    /// about 16 working days at 2.5 units/day — and the whole freight
    /// vertical slice dies with it.
    ///
    /// This planner closes that loop on the opening order's own terms,
    /// nothing new invented: same supplier, same SKU, same 15c/sack,
    /// same 20-sack lot, same cash-on-arrival payment, same 1-day
    /// transit. Each day it counts what the store already covers —
    /// shelf sacks + sellable procurement lots + unreceived orders for
    /// the SKU — and orders one fresh lot only when coverage is below
    /// one full lot and no feed order is already outstanding. The
    /// market supply for the lot is restocked first, as the modeled
    /// event ExternalMarketAuthority.RestockCounterpartySupply, with
    /// the reason naming this standing reorder; the restock creates no
    /// local stock and moves no cash, and the order itself still pays
    /// on arrival through the service. A refused restock or order
    /// orders nothing and moves nothing. Plain C#, no Unity
    /// dependency, Roslyn-testable. The daily driver calls this from
    /// DriveStoreFeedProcurement; the Unity-side wiring is Codex
    /// verification work.
    /// </summary>
    public static class StoreHorseFeedReorder
    {
        /// <summary>Authored opening feed supplier (ConfigurePlaytestHorseFeed).</summary>
        public const string MillbrookSupplierId = "millbrook_grain_co";

        /// <summary>Authored opening feed SKU.</summary>
        public const string FeedSkuId = "store_horse_feed_sack";

        /// <summary>Authored opening feed price, cents per sack.</summary>
        public const int FeedPriceCentsPerSack = 15;

        /// <summary>Authored opening feed lot, sacks per order.</summary>
        public const int OpeningLotSacks = 20;

        /// <summary>Authored feed category (dev ConfigurePlaytestHorseFeed).</summary>
        public const string FeedCategoryId = "animal_feed";

        /// <summary>Authored feed display name (dev ConfigurePlaytestHorseFeed).</summary>
        public const string FeedDisplayName = "Horse Feed";

        /// <summary>Authored feed unit label (dev ConfigurePlaytestHorseFeed).</summary>
        public const string FeedUnitLabel = "sack";

        /// <summary>Authored feed conversion: one sack is two feed units.</summary>
        public const int FeedUnitsPerSack = 2;

        /// <summary>Authored feed transit: one day from Millbrook.</summary>
        public const int FeedTransitDays = 1;

        /// <summary>Authored Millbrook display name (dev ConfigurePlaytestHorseFeed).</summary>
        public const string MillbrookDisplayName = "Millbrook Grain Co.";

        /// <summary>
        /// Campaign B (DEF-008): the Millbrook feed counterparty on the
        /// authored opening terms — the same counterparty the dev-only
        /// ConfigurePlaytestHorseFeed builds inline. Both build flavors
        /// call this builder so the terms cannot drift between them.
        /// </summary>
        public static ExternalCounterparty BuildMillbrookCounterparty()
        {
            return new ExternalCounterparty
            {
                CounterpartyId = MillbrookSupplierId,
                DisplayName = MillbrookDisplayName,
                TransitDays = FeedTransitDays,
                InitialBudgetCents = 0,
                SupplyUnits = new Dictionary<string, int> { [FeedSkuId] = OpeningLotSacks },
            };
        }

        /// <summary>
        /// Campaign B (DEF-008): the store feed registry item on the
        /// authored opening terms — the same item the dev-only
        /// ConfigurePlaytestHorseFeed builds inline. The retail price is
        /// the opening 15c/sack and one sack converts to two feed units.
        /// </summary>
        public static Review2StoreFeedItem BuildFeedRegistryItem(string storeBusinessInstanceId)
        {
            return new Review2StoreFeedItem
            {
                BusinessInstanceId = storeBusinessInstanceId ?? string.Empty,
                FeedCategoryId = FeedCategoryId,
                FeedItemId = FeedSkuId,
                DisplayName = FeedDisplayName,
                UnitLabel = FeedUnitLabel,
                RetailPriceCents = FeedPriceCentsPerSack,
                FeedUnitsPerStoreUnitNumerator = FeedUnitsPerSack,
                FeedUnitsPerStoreUnitDenominator = 1,
            };
        }

        /// <summary>
        /// Campaign B (DEF-008): the household catalog item for the feed
        /// SKU — the same definition the dev-only
        /// ConfigurePlaytestHorseFeed registers. Without it the trading
        /// port drops the feed offer even when stock is on the shelf.
        /// </summary>
        public static HouseholdItemDefinition BuildFeedCatalogItem()
        {
            return new HouseholdItemDefinition(FeedSkuId, FeedDisplayName, FeedUnitLabel, FeedCategoryId, 1, false);
        }

        public static StoreHorseFeedReorderResult RunDay(StoreHorseFeedReorderInputs inputs, List<string> diag)
        {
            diag = diag ?? new List<string>();
            var result = new StoreHorseFeedReorderResult();
            if (inputs == null || inputs.Service == null)
            {
                result.Refusal = "StoreHorseFeedReorder: no procurement service — no feed reordered.";
                diag.Add(result.Refusal);
                return result;
            }
            if (string.IsNullOrWhiteSpace(inputs.SupplierId) || string.IsNullOrWhiteSpace(inputs.SkuId)
                || inputs.UnitPriceCents <= 0 || inputs.LotSacks <= 0)
            {
                result.Refusal = "StoreHorseFeedReorder: supplier, SKU, positive price, and positive lot size are required — no feed reordered, no conversion guessed.";
                diag.Add(result.Refusal);
                return result;
            }

            int outstanding = OutstandingOrderSacks(inputs.Service, inputs.SkuId);
            int covered = Math.Max(0, inputs.StoreSacksOnHand)
                + Math.Max(0, inputs.Service.SellableUnits(inputs.SkuId))
                + outstanding;
            if (covered >= inputs.LotSacks)
            {
                diag.Add($"StoreHorseFeedReorder: feed covered — {covered} sack(s) on hand / sellable / on order for '{inputs.SkuId}'; no reorder.");
                return result;
            }
            if (outstanding > 0)
            {
                diag.Add($"StoreHorseFeedReorder: {outstanding} sack(s) of '{inputs.SkuId}' already on order; waiting for arrival, no duplicate order.");
                return result;
            }

            string restockReason = $"standing horse-feed reorder (opening terms: {inputs.LotSacks} sacks at {inputs.UnitPriceCents}c/sack from {inputs.SupplierId})";
            if (!inputs.Service.RestockSupplierSupply(inputs.SupplierId, inputs.SkuId, inputs.LotSacks, restockReason))
            {
                result.Refusal = $"StoreHorseFeedReorder: feed supply could not be restocked from '{inputs.SupplierId}' for '{inputs.SkuId}' — no order placed; see diagnostics.";
                diag.Add(result.Refusal);
                return result;
            }

            NamedSkuOrder order = inputs.Service.PlaceOrder(
                inputs.SkuId, inputs.LotSacks, inputs.UnitPriceCents, 0,
                inputs.SupplierId, Math.Max(0, inputs.DayIndex),
                NamedSkuPaymentMode.CashOnArrival, null);
            if (order == null)
            {
                result.Refusal = $"StoreHorseFeedReorder: feed order refused after supply restock — no order placed; the restocked supply stays at the supplier until ordered; see diagnostics.";
                diag.Add(result.Refusal);
                return result;
            }

            result.Ordered = true;
            result.Order = order;
            diag.Add($"StoreHorseFeedReorder: reordered {order.UnitsOrdered} sack(s) of '{inputs.SkuId}' from {inputs.SupplierId} at {inputs.UnitPriceCents}c/sack, total {order.TotalCostCents}c, arrives day {order.ExpectedArrivalDayIndex}; store held {inputs.StoreSacksOnHand} sack(s).");
            return result;
        }

        private static int OutstandingOrderSacks(OffMapNamedSkuProcurementService service, string skuId)
        {
            if (service?.Orders == null) return 0;
            int total = 0;
            foreach (NamedSkuOrder order in service.Orders)
            {
                if (order == null || !string.Equals(order.SkuId, skuId, StringComparison.Ordinal)
                    || order.Stage == NamedSkuOrderStage.Received
                    || order.Stage == NamedSkuOrderStage.Cancelled)
                    continue;
                total += Math.Max(0, order.UnitsOrdered);
            }
            return total;
        }
    }
}
