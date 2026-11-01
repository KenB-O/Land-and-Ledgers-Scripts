using System;
using System.Collections.Generic;
using LandLedgers.Population;

namespace LandLedgers.Economy.Businesses.GeneralStore
{
    /// <summary>
    /// Phase C: the store's own declaration of what one of its stock
    /// categories actually represents as real household items. This is
    /// store-owned stocking knowledge (e.g. THIS store's "staple_food"
    /// category is flour, 1 unit = 1 lb) — it replaces the Phase B
    /// transitional global category→item approximation in the shopping path.
    /// </summary>
    [Serializable]
    public sealed class GeneralStoreCategoryOffer
    {
        public string CategoryId = string.Empty;
        public List<string> OfferedItemIds = new List<string>();
        /// <summary>Store category units consumed per item unit sold (≥1).</summary>
        public int StoreUnitsPerItemUnit = 1;
    }

    /// <summary>
    /// Phase C: Unity-free seam over the general store runtime so the
    /// supplier adapter (and its tests) never touch the MonoBehaviour.
    /// Production wiring wraps <see cref="GeneralStoreRuntimeManager"/>.
    /// </summary>
    public interface IGeneralStoreTradingPort
    {
        string StoreBusinessId { get; }
        string StoreName { get; }
        string LocationId { get; }
        IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers { get; }
        int CategoryStockUnits(string categoryId);
        int CategoryPricePerUnitCents(string categoryId);
        StoreTradingCapability TradingCapability { get; }
        int CurrentCashCents { get; }
        bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits);
        void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents);
        void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId);
        /// <summary>C5: spends business cash on procurement; null = paid, else the reason.</summary>
        string SpendCash(int amountCents, string purpose);
        /// <summary>C5: receives real stock with provenance; null = accepted, else the reason.</summary>
        string ReceiveStock(string categoryId, int units, string provenanceLabel);
        /// <summary>C5: how many more units the category can take right now (capacity).</summary>
        int CategoryReceivableUnits(string categoryId);
        int CategoryTargetStockUnits(string categoryId);
        bool OffersDelivery { get; }
        int DeliveryFeeCents(string itemId, int units);
        /// <summary>C1.11: documented trade credit offered to shoppers.</summary>
        bool OffersTradeCredit { get; }
        int TradeCreditLimitCents { get; }
    }

    /// <summary>
    /// Phase C: registers the REAL general store as an <see cref="IGoodsSupplier"/>
    /// in the <see cref="SupplierDirectory"/> so the embodied shopping path is
    /// the one path (no parallel fake store). The adapter sells only what the
    /// store actually holds (finite stock), only when the store can really
    /// trade (open + staffed), and posts the business side of every sale
    /// through the store's real cash authority.
    ///
    /// Item resolution is store-declared (CategoryOffers): the store knows
    /// what its categories represent. No global category→item guesswork.
    /// </summary>
    public sealed class GeneralStoreSupplierAdapter : IItemResolvingSupplier, IDeliveryOfferingSupplier, IOffersTradeCredit
    {
        private readonly IGeneralStoreTradingPort port;
        private readonly List<string> log = new List<string>();
        private string lastReceiptId = string.Empty;
        private int receiptSequence;

        public GeneralStoreSupplierAdapter(IGeneralStoreTradingPort port)
        {
            this.port = port ?? throw new ArgumentNullException(nameof(port));
        }

        public IReadOnlyList<string> Log => log;

        public string SupplierBusinessId => port.StoreBusinessId;
        public string SupplierName => port.StoreName;
        public string LocationId => port.LocationId;
        public string LastReceiptId => lastReceiptId;

        /// <summary>C5: the one registration — the real general store joins the directory.</summary>
        public static void RegisterIn(SupplierDirectory directory, IGeneralStoreTradingPort storePort)
        {
            if (directory == null || storePort == null)
            {
                return;
            }

            directory.Register(new GeneralStoreSupplierAdapter(storePort));
        }

        // ---- IGoodsSupplier (T1A contract, category surface) ----

        public bool HasCategory(string categoryId)
        {
            return FindOffer(categoryId) != null;
        }

        public int StockUnits(string categoryId)
        {
            return Math.Max(0, port.CategoryStockUnits(categoryId));
        }

        public int PricePerUnitCents(string categoryId)
        {
            return Math.Max(0, port.CategoryPricePerUnitCents(categoryId));
        }

        public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics)
        {
            GeneralStoreCategoryOffer offer = FindOffer(categoryId);
            if (offer == null || requestedUnits <= 0)
            {
                return 0;
            }

            if (!port.TryConsumeStoreUnits(categoryId, requestedUnits, out int consumed) || consumed <= 0)
            {
                diagnostics?.Add($"'{SupplierName}' has no '{categoryId}' to sell — no sale faked.");
                return 0;
            }

            int revenue = consumed * PricePerUnitCents(categoryId);
            port.RecordCashSettlement(categoryId, consumed, revenue);
            lastReceiptId = $"rcpt-{SupplierBusinessId}-{dayIndex}-{receiptSequence++}";
            log.Add($"Day {dayIndex}: sold {consumed}u {categoryId} for {revenue}c ({lastReceiptId}).");
            return consumed;
        }

        // ---- IItemResolvingSupplier (Phase C item surface) ----

        public bool OffersItem(string itemId)
        {
            return !string.IsNullOrWhiteSpace(CategoryForItem(itemId));
        }

        public string CategoryForItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId) || port.CategoryOffers == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < port.CategoryOffers.Count; i++)
            {
                GeneralStoreCategoryOffer offer = port.CategoryOffers[i];
                if (offer == null || offer.OfferedItemIds == null)
                {
                    continue;
                }

                for (int j = 0; j < offer.OfferedItemIds.Count; j++)
                {
                    if (string.Equals(offer.OfferedItemIds[j], itemId, StringComparison.OrdinalIgnoreCase))
                    {
                        return offer.CategoryId ?? string.Empty;
                    }
                }
            }

            return string.Empty;
        }

        public int ItemStockUnits(string itemId)
        {
            string categoryId = CategoryForItem(itemId);
            if (string.IsNullOrEmpty(categoryId))
            {
                return 0;
            }

            int perItem = StoreUnitsPerItem(categoryId);
            return StockUnits(categoryId) / Math.Max(1, perItem);
        }

        public int ItemPricePerUnitCents(string itemId)
        {
            string categoryId = CategoryForItem(itemId);
            if (string.IsNullOrEmpty(categoryId))
            {
                return 0;
            }

            return PricePerUnitCents(categoryId) * Math.Max(1, StoreUnitsPerItem(categoryId));
        }

        public string CheckTradingCapability(int absoluteDayIndex, int minuteOfDay)
        {
            StoreTradingCapability capability = port.TradingCapability;
            if (capability == null)
            {
                return "unknown trading capability — the store cannot prove it can trade";
            }

            return capability.CheckTradable(absoluteDayIndex, minuteOfDay);
        }

        /// <summary>
        /// The actual retail transaction: real store units leave, the business
        /// side posts through the store's cash authority (cash in, or a real
        /// credit obligation recorded as the receivable). Returns units sold.
        /// </summary>
        public int SellItem(
            string itemId,
            int requestedUnits,
            int dayIndex,
            ShoppingSettlement settlement,
            List<string> diagnostics)
        {
            string categoryId = CategoryForItem(itemId);
            if (string.IsNullOrEmpty(categoryId) || requestedUnits <= 0)
            {
                return 0;
            }

            int storeUnitsNeeded = requestedUnits * Math.Max(1, StoreUnitsPerItem(categoryId));
            if (!port.TryConsumeStoreUnits(categoryId, storeUnitsNeeded, out int consumedStoreUnits) || consumedStoreUnits <= 0)
            {
                diagnostics?.Add($"'{SupplierName}' cannot fill {requestedUnits}u {itemId}: real stock exhausted — no sale faked.");
                return 0;
            }

            int unitsSold = consumedStoreUnits / Math.Max(1, StoreUnitsPerItem(categoryId));
            int unitPrice = ItemPricePerUnitCents(itemId);
            int revenue = unitsSold * unitPrice;

            if (settlement != null && settlement.Mode == ShoppingSettlementMode.TradeCreditObligation)
            {
                port.RecordCreditSettlement(categoryId, consumedStoreUnits, revenue, settlement.ObligationId ?? string.Empty);
                log.Add($"Day {dayIndex}: sold {unitsSold}u {itemId} on trade credit {settlement.ObligationId} ({revenue}c receivable).");
            }
            else
            {
                port.RecordCashSettlement(categoryId, consumedStoreUnits, revenue);
                log.Add($"Day {dayIndex}: sold {unitsSold}u {itemId} for {revenue}c cash.");
            }

            lastReceiptId = $"rcpt-{SupplierBusinessId}-{dayIndex}-{receiptSequence++}";
            diagnostics?.Add($"'{SupplierName}' sold {unitsSold}u {itemId} ({lastReceiptId}).");
            return unitsSold;
        }

        // ---- IDeliveryOfferingSupplier ----

        public bool OffersDelivery => port.OffersDelivery;

        public int DeliveryFeeCents(string itemId, int units)
        {
            return Math.Max(0, port.DeliveryFeeCents(itemId, units));
        }

        public int DeliveryLeadMinutes(string fromLocationId, string toLocationId)
        {
            // The adapter does not guess freight time; the loop routes it
            // through the JourneyModel. This seam exists for future wagon
            // scheduling — it reports "unknown" honestly.
            return -1;
        }

        // ---- IOffersTradeCredit ----

        public bool OffersTradeCredit => port.OffersTradeCredit;

        public int TradeCreditLimitCents => Math.Max(0, port.TradeCreditLimitCents);

        private GeneralStoreCategoryOffer FindOffer(string categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId) || port.CategoryOffers == null)
            {
                return null;
            }

            for (int i = 0; i < port.CategoryOffers.Count; i++)
            {
                GeneralStoreCategoryOffer offer = port.CategoryOffers[i];
                if (offer != null && string.Equals(offer.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
                {
                    return offer;
                }
            }

            return null;
        }

        private int StoreUnitsPerItem(string categoryId)
        {
            GeneralStoreCategoryOffer offer = FindOffer(categoryId);
            return offer != null ? Math.Max(1, offer.StoreUnitsPerItemUnit) : 1;
        }
    }
}
