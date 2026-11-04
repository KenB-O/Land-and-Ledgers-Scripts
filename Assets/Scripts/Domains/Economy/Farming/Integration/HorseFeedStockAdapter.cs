using LandLedgers.Animals;
using LandLedgers.Economy;
using System;

namespace LandLedgers.Economy.Farming.Integration
{
    /// <summary>
    /// Pass VII Workstream B (P1-5): production adapter letting the
    /// animal-domain HorseCareDriver draw rations from the farm's real
    /// FeedLoop stock. Feed is finite — the driver only ever receives what
    /// the stock actually holds, so horse upkeep is a real cost against the
    /// farm's feed supply.
    /// </summary>
    public sealed class HorseFeedStockAdapter : IHorseFeedStock
    {
        private readonly FeedLoop feedLoop;

        public HorseFeedStockAdapter(FeedLoop feedLoop)
        {
            this.feedLoop = feedLoop;
        }

        public int FeedStockUnits => feedLoop != null ? feedLoop.FeedStockUnits : 0;

        public int TakeFeedUnits(int requestedUnits)
        {
            if (feedLoop == null) return 0;
            return feedLoop.TakeFeedUnits(requestedUnits);
        }
    }

    /// <summary>Finite feed/upkeep stock adapter for a horse-owning livery Business.</summary>
    public sealed class BusinessHorseFeedStockAdapter : IHorseFeedStock
    {
        private readonly BusinessRuntimeState business;
        private readonly string feedItemId;
        private readonly string feedCategoryId;
        private readonly string feedUnitLabel;
        private readonly int feedUnitsNumerator;
        private readonly int storeUnitsDenominator;
        private readonly Func<int> getUnusedFeedUnits;
        private readonly Action<int> setUnusedFeedUnits;

        /// <summary>
        /// Adapts one explicitly identified Business SKU to horse rations.
        /// The caller must supply the exact category, SKU and unit label; this
        /// adapter performs no packaging conversion. Each physical SKU unit is
        /// one feed unit only when its authored unit is already the care
        /// system's feed unit. Category balances alone are never feed stock.
        /// </summary>
        public BusinessHorseFeedStockAdapter(
            BusinessRuntimeState business,
            string feedItemId,
            string feedCategoryId,
            string feedUnitLabel)
            : this(business, feedItemId, feedCategoryId, feedUnitLabel, 1, 1, null, null)
        {
        }

        /// <summary>
        /// Adapts identified business-owned packages to care units. Conversion
        /// and partial-package carry are explicit caller inputs so a package
        /// cannot be silently treated as one ration. The carry callbacks let
        /// the scenario's save authority persist the remainder per animal.
        /// </summary>
        public BusinessHorseFeedStockAdapter(
            BusinessRuntimeState business,
            string feedItemId,
            string feedCategoryId,
            string feedUnitLabel,
            int feedUnitsPerPackageNumerator,
            int feedUnitsPerPackageDenominator,
            Func<int> getUnusedFeedUnits,
            Action<int> setUnusedFeedUnits)
        {
            this.business = business;
            this.feedItemId = feedItemId?.Trim() ?? string.Empty;
            this.feedCategoryId = feedCategoryId?.Trim() ?? string.Empty;
            this.feedUnitLabel = feedUnitLabel?.Trim() ?? string.Empty;
            feedUnitsNumerator = feedUnitsPerPackageNumerator;
            storeUnitsDenominator = feedUnitsPerPackageDenominator;
            this.getUnusedFeedUnits = getUnusedFeedUnits;
            this.setUnusedFeedUnits = setUnusedFeedUnits;
        }

        public int FeedStockUnits
        {
            get
            {
                if (!HasValidSource() || feedUnitsNumerator <= 0 || storeUnitsDenominator <= 0) return 0;
                int total = 0;
                foreach (BusinessRetailItemLotState lot in business.RetailItemLots)
                {
                    if (Matches(lot)) total += lot.AvailableUnits;
                }
                return Math.Max(0, getUnusedFeedUnits?.Invoke() ?? 0)
                    + total * feedUnitsNumerator / storeUnitsDenominator;
            }
        }

        public int TakeFeedUnits(int requestedUnits)
        {
            int requested = Math.Max(0, requestedUnits);
            int carried = Math.Max(0, getUnusedFeedUnits?.Invoke() ?? 0);
            if (requested <= 0 || feedUnitsNumerator <= 0 || storeUnitsDenominator <= 0
                || FeedStockUnits < requested) return 0;

            int deficit = Math.Max(0, requested - carried);
            int packagesNeeded = deficit == 0 ? 0
                : (deficit * storeUnitsDenominator + feedUnitsNumerator - 1) / feedUnitsNumerator;
            int deliveredByPackages = packagesNeeded * feedUnitsNumerator / storeUnitsDenominator;
            while (packagesNeeded > 0 && deliveredByPackages < deficit)
            {
                packagesNeeded++;
                deliveredByPackages = packagesNeeded * feedUnitsNumerator / storeUnitsDenominator;
            }

            if (packagesNeeded > 0
                && (!business.TryConsumeRetailItemStockUnits(feedItemId, feedCategoryId, packagesNeeded, out int consumed)
                    || consumed != packagesNeeded)) return 0;

            setUnusedFeedUnits?.Invoke(Math.Max(0, carried + deliveredByPackages - requested));
            return requested;
        }

        private bool HasValidSource()
        {
            if (business == null || string.IsNullOrWhiteSpace(business.BusinessId)
                || string.IsNullOrWhiteSpace(feedItemId) || string.IsNullOrWhiteSpace(feedCategoryId)
                || string.IsNullOrWhiteSpace(feedUnitLabel)) return false;

            // TryConsumeRetailItemStockUnits consumes by item/category. Refuse
            // mixed unit labels so it cannot consume a physically different
            // package that happens to share the same SKU/category pair.
            foreach (BusinessRetailItemLotState lot in business.RetailItemLots)
            {
                if (lot == null || !string.Equals(lot.ItemId, feedItemId, System.StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.CategoryId, feedCategoryId, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(lot.UnitLabel, feedUnitLabel, System.StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.OwnerBusinessId, business.BusinessId, System.StringComparison.Ordinal)
                    || !string.Equals(lot.CustodianBusinessId, business.BusinessId, System.StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private bool Matches(BusinessRetailItemLotState lot) => lot != null
            && string.Equals(lot.ItemId, feedItemId, System.StringComparison.OrdinalIgnoreCase)
            && string.Equals(lot.CategoryId, feedCategoryId, System.StringComparison.OrdinalIgnoreCase)
            && string.Equals(lot.UnitLabel, feedUnitLabel, System.StringComparison.OrdinalIgnoreCase)
            && string.Equals(lot.OwnerBusinessId, business.BusinessId, System.StringComparison.Ordinal)
            && string.Equals(lot.CustodianBusinessId, business.BusinessId, System.StringComparison.Ordinal);
    }
}
