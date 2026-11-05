using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.FirstLedger;
using LandLedgers.Population;

namespace LandLedgers.Economy.Businesses.GeneralStore
{
    /// <summary>
    /// Phase C: production <see cref="IGeneralStoreTradingPort"/> over the
    /// real <see cref="GeneralStoreRuntimeManager"/>. Thin by design — every
    /// mutation goes through the manager's / BusinessRuntimeState's own
    /// authorities (stock, cash, settlement). Scene wiring constructs this
    /// and registers the adapter in the SupplierDirectory (Codex integrates).
    /// </summary>
    public sealed class GeneralStoreTradingPort : IItemLevelGeneralStoreTradingPort
    {
        private readonly GeneralStoreRuntimeManager manager;
        private readonly BusinessInstanceState business;
        private readonly List<GeneralStoreCategoryOffer> offers;
        private readonly StoreTradingCapability capability;

        public GeneralStoreTradingPort(
            GeneralStoreRuntimeManager manager,
            List<GeneralStoreCategoryOffer> categoryOffers,
            StoreTradingCapability tradingCapability)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
            this.offers = categoryOffers ?? new List<GeneralStoreCategoryOffer>();
            this.capability = tradingCapability ?? new StoreTradingCapability();
        }

        public GeneralStoreTradingPort(GeneralStoreRuntimeManager manager, BusinessInstanceState business,
            List<GeneralStoreCategoryOffer> categoryOffers, StoreTradingCapability tradingCapability)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
            this.business = business ?? throw new ArgumentNullException(nameof(business));
            this.offers = categoryOffers ?? new List<GeneralStoreCategoryOffer>();
            this.capability = tradingCapability ?? new StoreTradingCapability();
        }

        private BusinessInstanceState ActiveBusiness => business ?? manager.CurrentBusiness;
        private BusinessRuntimeState ActiveState => ActiveBusiness?.RuntimeState ?? manager.RuntimeState;
        public string StoreBusinessId => ActiveBusiness != null ? ActiveBusiness.InstanceId : "general-store";
        public string StoreName => ActiveBusiness != null ? ActiveBusiness.RuntimeDisplayName : "General Store";
        public string LocationId => ActiveBusiness != null && ActiveBusiness.AssignedBuildingId >= 0
            ? $"building:{ActiveBusiness.AssignedBuildingId}"
            : manager.StoreBuildingId >= 0 ? $"building:{manager.StoreBuildingId}" : "general-store";
        public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => offers;
        public StoreTradingCapability TradingCapability
        {
            get
            {
                // The scene registers this port before a player has formed a
                // store. Keep the view of staffing live so the same registered
                // supplier becomes trade-capable only after a real store and
                // clerk exist; registration itself never opens the business.
                if (manager != null && capability != null)
                {
                    capability.Staffed = business != null
                        ? business.RuntimeState != null && business.RuntimeState.FilledWorkerCount > 0
                        : manager.CurrentBusiness != null && manager.FilledWorkerSlotCount > 0;
                }

                return capability;
            }
        }
        public int CurrentCashCents => ActiveState != null ? ActiveState.CurrentCashCents : manager.CurrentCashCents;
        public bool OffersDelivery => false;

        /// <summary>
        /// Trade credit policy for shoppers. Configured per store; default off
        /// (cash only) until the scenario/store opts in.
        /// </summary>
        public bool OffersTradeCredit { get; set; } = false;
        public int TradeCreditLimitCents { get; set; } = 0;

        public int CategoryStockUnits(string categoryId)
        {
            BusinessRuntimeState state = ActiveState;
            CategoryStockState stock = state != null ? state.GetCategoryStock(categoryId) : null;
            return stock != null ? stock.CurrentStockUnits : 0;
        }

        public int CategoryTargetStockUnits(string categoryId)
        {
            BusinessRuntimeState state = ActiveState;
            CategoryStockState stock = state != null ? state.GetCategoryStock(categoryId) : null;
            return stock != null ? stock.TargetStockUnits : 0;
        }

        public int CategoryPricePerUnitCents(string categoryId)
        {
            return Math.Max(0, manager.GetAverageCategorySellingPriceCents(categoryId));
        }

        private GeneralStoreCategoryOffer FindOffer(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId) || offers == null) return null;
            for (int i = 0; i < offers.Count; i++)
            {
                GeneralStoreCategoryOffer offer = offers[i];
                if (offer?.OfferedItemIds == null) continue;
                for (int j = 0; j < offer.OfferedItemIds.Count; j++)
                    if (string.Equals(offer.OfferedItemIds[j], itemId, StringComparison.OrdinalIgnoreCase)) return offer;
            }
            return null;
        }

        public bool UsesItemLevelInventory(string itemId) => FindOffer(itemId)?.UsesItemLevelInventory == true;

        public int ItemStockUnits(string itemId)
        {
            GeneralStoreCategoryOffer offer = FindOffer(itemId);
            if (offer == null) return 0;
            // Slice 10: the item-level path used to report raw retail stock
            // (store units) as household units. When the offer carries a
            // conversion factor, stock is counted in household units, the
            // same as the category path already does.
            return offer.UsesItemLevelInventory
                ? (ActiveState?.GetRetailItemStockUnits(itemId, offer.CategoryId) ?? 0) / Math.Max(1, offer.StoreUnitsPerItemUnit)
                : CategoryStockUnits(offer.CategoryId) / Math.Max(1, offer.StoreUnitsPerItemUnit);
        }

        public int ItemPricePerUnitCents(string itemId)
        {
            GeneralStoreCategoryOffer offer = FindOffer(itemId);
            if (offer == null) return 0;
            // Slice 10: the item-level price is per STORE unit; the loop
            // prices per HOUSEHOLD unit, so the offer factor multiplies it,
            // the same as the category path already does. Factor 1 is a no-op.
            int storeUnitPrice = offer.UsesItemLevelInventory
                ? offer.ItemPriceOverrideCents > 0 ? offer.ItemPriceOverrideCents : manager.GetItemSellingPriceCents(itemId)
                : CategoryPricePerUnitCents(offer.CategoryId);
            return storeUnitPrice * Math.Max(1, offer.StoreUnitsPerItemUnit);
        }

        public bool TryConsumeItemStockUnits(string itemId, int units, out int consumedUnits)
        {
            consumedUnits = 0;
            GeneralStoreCategoryOffer offer = FindOffer(itemId);
            if (offer == null || !offer.UsesItemLevelInventory || ActiveState == null) return false;
            // Slice 10: `units` are household units; the retail lots hold
            // store units, so the factor converts before the all-or-nothing
            // lot consumption. What comes back is household units sold.
            int factor = Math.Max(1, offer.StoreUnitsPerItemUnit);
            if (!ActiveState.TryConsumeRetailItemStockUnits(itemId, offer.CategoryId, units * factor, out int consumedStoreUnits))
                return false;
            consumedUnits = consumedStoreUnits / factor;
            return consumedUnits > 0;
        }

        public void RecordItemCashSettlement(string categoryId, int unitsSold, int revenueCents)
        {
            ActiveState?.RecordRetailItemSettlement(categoryId, unitsSold, revenueCents);
        }

        public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
        {
            consumedUnits = 0;
            for (int i = 0; i < offers.Count; i++)
                if (offers[i] != null && offers[i].UsesItemLevelInventory
                    && string.Equals(offers[i].CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
                    return false;
            BusinessRuntimeState state = ActiveState;
            return state != null && state.TryConsumeCategoryStockUnits(categoryId, units, out consumedUnits);
        }

        public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
        {
            BusinessRuntimeState state = ActiveState;
            if (state != null)
            {
                state.RecordGenericRetailSettlement(categoryId, unitsSold, revenueCents);
            }
        }

        public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId)
        {
            BusinessRuntimeState state = ActiveState;
            if (state == null)
            {
                return;
            }

            // The shared FinancialObligationAuthority owns the receivable
            // (single truth); the store's cash is untouched on a credit sale.
            // Stock custody already moved via TryConsumeStoreUnits (which
            // refreshes stock health itself).
        }

        public string SpendCash(int amountCents, string purpose)
        {
            BusinessRuntimeState state = ActiveState;
            if (state == null)
            {
                return "Store runtime unavailable.";
            }

            int spend = Math.Max(0, amountCents);
            if (spend <= 0)
            {
                return null;
            }

            if (state.CurrentCashCents < spend)
            {
                return $"Store cannot afford {spend}c for {purpose}: cash is {state.CurrentCashCents}c.";
            }

            state.SpendCents(spend);
            return null;
        }

        public string ReceiveStock(string categoryId, int units, string provenanceLabel)
        {
            BusinessRuntimeState state = ActiveState;
            if (state == null)
            {
                return "Store runtime unavailable.";
            }

            if (units <= 0)
            {
                return "Nothing to receive.";
            }

            int accepted = state.AddCategoryStockUnits(categoryId, units);
            if (accepted < units)
            {
                return $"Store accepted {accepted}/{units}u {categoryId} ({provenanceLabel}): category full.";
            }

            return null;
        }

        public int CategoryReceivableUnits(string categoryId)
        {
            BusinessRuntimeState state = ActiveState;
            CategoryStockState stock = state != null ? state.GetCategoryStock(categoryId) : null;
            if (stock == null)
            {
                return 0;
            }

            return stock.TargetStockUnits > 0
                ? Math.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits)
                : int.MaxValue;
        }

        public int DeliveryFeeCents(string itemId, int units)
        {
            return 0;
        }
    }
}
