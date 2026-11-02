using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.BreadDrivers
{
    /// <summary>
    /// Series 2 Pass 6 (bread drivers): notions supply for the bakery.
    /// Every bread batch needs 1 notion unit (BakeryBreadCatalog
    /// StandardNotionsPerBatch), and before this pass notions had no
    /// supply at all. Notions ride the SAME named-SKU procurement
    /// service as flour (the store's service), so the single
    /// PassXlProcurementService save slot covers them: the store orders
    /// a caller-supplied notions SKU off-map, receives it, and feeds it
    /// to the bakery bin as "bakery-notions" through the same
    /// consume-then-pay-then-bin path as flour, with the same rollback.
    /// The notions vendor, SKU, price, and store-unit-to-bin-unit
    /// conversion are ALL caller-supplied, never invented as canon: a
    /// notions store unit is whatever the caller's supplier sells, and
    /// NotionsUnitsPerStoreUnit states what it becomes in the bin.
    /// A bakery-owned notions service (bakery orders notions directly,
    /// no store middleman) is a Pass 9 decision, not built here; if
    /// Kennedy chooses it, this class's order/arrival calls move to
    /// that service and the feed becomes a within-business transfer
    /// with no inter-business cash — the bin item id does not change.
    /// Cash here is inter-business (bakery pays store), through
    /// BusinessRuntimeState only; no debt is created by this class.
    /// </summary>
    public sealed class Series2NotionsSupply
    {
        private readonly Series2FlourProcurementDriver procurement;

        public Series2NotionsSupply(Series2FlourProcurementDriver procurement)
        {
            this.procurement = procurement;
        }

        public Series2FlourProcurementDriver Procurement => procurement;

        /// <summary>
        /// Orders notions off-map through the legacy quote path (the
        /// one order path this pass uses; Pass 3 drafts are not accepted).
        /// Returns the order, or null when the quote or service refuses.
        /// </summary>
        public NamedSkuOrder PlaceNotionsOrder(
            Series2CounterpartyRegistration registration, int units, int dayIndex,
            NamedSkuPaymentMode paymentMode, ITradeCreditPort tradeCredit, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (procurement == null)
            {
                diag.Add("Series2NotionsSupply: no procurement driver; no notions order placed.");
                return null;
            }
            OffMapFreightQuote quote = Series2ProcurementQuoteBuilder.Build(
                registration, units, FreightOptionKind.SellerFreight, diag);
            if (quote == null) return null;
            return procurement.PlaceOrderFromQuote(quote, dayIndex, paymentMode, tradeCredit, diag);
        }

        public List<NamedSkuLot> ReceiveArrivals(int dayIndex, ITradeCreditPort tradeCredit, List<string> diag)
        {
            if (procurement == null) return new List<NamedSkuLot>();
            return procurement.ReceiveArrivals(dayIndex, tradeCredit, diag);
        }

        /// <summary>
        /// Feeds notions from store lots into the bakery bin as
        /// "bakery-notions". Returns null on success, or a refusal
        /// diagnostic with full rollback (consumption undone, cash
        /// restored). NotionsUnitsPerStoreUnit must be positive and is
        /// stated in diagnostics on every feed, so a caller who feeds
        /// sacks and a caller who feeds single units cannot silently
        /// produce the same bin count.
        /// </summary>
        public string FeedNotionsToBakery(
            OffMapNamedSkuProcurementService storeService,
            BusinessRuntimeState storeRuntime,
            BakeryShopRuntime bakery, BusinessRuntimeState bakeryRuntime,
            EntityIdRegistry ids,
            string notionsSkuId, int storeUnits, int notionsUnitsPerStoreUnit,
            int unitPriceCents, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (notionsUnitsPerStoreUnit <= 0)
                return "Series2NotionsSupply: notions units per store unit must be positive; feed refused rather than guessing the conversion.";
            int binUnits = storeUnits * notionsUnitsPerStoreUnit;
            diag.Add($"Series2NotionsSupply: 1 notions store unit = {notionsUnitsPerStoreUnit} bin unit(s); feeding {storeUnits}u '{notionsSkuId}' as {binUnits}u '{BakeryBreadCatalog.NotionsItemId}'.");
            return Series2StoreToBakeryFeed.FeedToBin(
                storeService, storeRuntime, bakery, bakeryRuntime, ids,
                notionsSkuId, storeUnits, BakeryBreadCatalog.NotionsItemId,
                binUnits, "bakery-notions-intake", unitPriceCents, dayIndex, diag);
        }
    }
}
