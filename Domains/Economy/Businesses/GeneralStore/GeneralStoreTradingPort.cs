using System;
using System.Collections.Generic;
using LandLedgers.Economy.Core;
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
    public sealed class GeneralStoreTradingPort : IGeneralStoreTradingPort
    {
        private readonly GeneralStoreRuntimeManager manager;
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

        public string StoreBusinessId => manager.CurrentBusiness != null ? manager.CurrentBusiness.InstanceId : "general-store";
        public string StoreName => manager.CurrentBusiness != null ? manager.CurrentBusiness.RuntimeDisplayName : "General Store";
        public string LocationId => manager.StoreBuildingId >= 0 ? $"building:{manager.StoreBuildingId}" : "general-store";
        public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => offers;
        public StoreTradingCapability TradingCapability => capability;
        public int CurrentCashCents => manager.CurrentCashCents;
        public bool OffersDelivery => false;

        /// <summary>
        /// Trade credit policy for shoppers. Configured per store; default off
        /// (cash only) until the scenario/store opts in.
        /// </summary>
        public bool OffersTradeCredit { get; set; } = false;
        public int TradeCreditLimitCents { get; set; } = 0;

        public int CategoryStockUnits(string categoryId)
        {
            BusinessRuntimeState state = manager.RuntimeState;
            CategoryStockState stock = state != null ? state.GetCategoryStock(categoryId) : null;
            return stock != null ? stock.CurrentStockUnits : 0;
        }

        public int CategoryTargetStockUnits(string categoryId)
        {
            BusinessRuntimeState state = manager.RuntimeState;
            CategoryStockState stock = state != null ? state.GetCategoryStock(categoryId) : null;
            return stock != null ? stock.TargetStockUnits : 0;
        }

        public int CategoryPricePerUnitCents(string categoryId)
        {
            return Math.Max(0, manager.GetAverageCategorySellingPriceCents(categoryId));
        }

        public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
        {
            consumedUnits = 0;
            BusinessRuntimeState state = manager.RuntimeState;
            return state != null && state.TryConsumeCategoryStockUnits(categoryId, units, out consumedUnits);
        }

        public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
        {
            BusinessRuntimeState state = manager.RuntimeState;
            if (state != null)
            {
                state.RecordGenericRetailSettlement(categoryId, unitsSold, revenueCents);
            }
        }

        public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId)
        {
            BusinessRuntimeState state = manager.RuntimeState;
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
            BusinessRuntimeState state = manager.RuntimeState;
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
            BusinessRuntimeState state = manager.RuntimeState;
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
            BusinessRuntimeState state = manager.RuntimeState;
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
