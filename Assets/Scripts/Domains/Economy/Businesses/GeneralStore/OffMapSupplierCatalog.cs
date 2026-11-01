using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.GeneralStore
{
    /// <summary>
    /// Pass XL phase 3, item 4: off-map suppliers and freight quotes.
    ///
    /// With only three on-map businesses, most General Store stock must
    /// be ordered off-map. This catalog is the supplier side of the
    /// Phase 1/2 OffMapNamedSkuProcurementService, which already handles
    /// one SKU end to end through ExternalMarketAuthority but has no
    /// supplier choice, no distance, and no freight option comparison.
    ///
    /// A supplier is: an id, a name, a distance in miles (caller-supplied,
    /// test examples use 100/200/250/300 miles — those distances are
    /// examples, not canon), the SKUs it offers, and a goods unit price
    /// in cents (caller-supplied, NOT a price list and NOT canon).
    /// Two suppliers may offer the same SKU at different distances and
    /// prices — e.g. cigarettes from a 100-mile supplier and a 300-mile
    /// supplier — and the quote comparison is what makes that choice
    /// meaningful, not a hidden cheapest-supplier rule.
    ///
    /// Freight, per Kennedy's design, rises with distance and is a
    /// caller-supplied rate, never a canon rate:
    ///   - SelfCollect: freight 0 in the quote, but RequiresJourney =
    ///     true — someone (e.g. the player) must actually travel to
    ///     collect it; the quote does not book a journey.
    ///   - SellerFreight: freight = distance miles x cents-per-mile,
    ///     paid to the seller as an explicit surcharge (the Phase 1/2
    ///     pattern, where the order total booked through
    ///     ExternalMarketAuthority includes the surcharge).
    ///   - FreightBusiness: seller-freight cost + a caller-supplied
    ///     commission. This quotes a freight business; it does NOT
    ///     create one. Starting a freight business is a business-creation
    ///     action through FreightProductionDriver.EnsureCompany, with a
    ///     wagon/team pool — see the audit doc, section D4. If no
    ///     freight business exists, a FreightBusiness quote is still a
    ///     quote, not a booking.
    ///
    /// Quotes are sorted by delivered total. This catalog creates no
    /// stock, no cash movement, and no order: since Pass XL phase 4
    /// the quote-to-order step is OffMapQuoteToOrder.PlaceOrderFromQuote,
    /// which validates the quote (totals recompute, divisible goods)
    /// and places the exact order through
    /// OffMapNamedSkuProcurementService, refusing SelfCollect quotes
    /// (no pickup journey exists) and, since phase 5, Unspecified
    /// freight options. Plain C#, no Unity dependency.
    /// </summary>
    public sealed class OffMapSupplier
    {
        public string SupplierId = string.Empty;
        public string SupplierName = string.Empty;
        public int DistanceMiles;
        public List<string> OfferedSkuIds = new List<string>();
        public int GoodsUnitPriceCents;
    }

    public enum FreightOptionKind
    {
        Unspecified = 0,
        SelfCollect = 1,
        SellerFreight = 2,
        FreightBusiness = 3,
    }

    public sealed class OffMapFreightQuote
    {
        public string SupplierId = string.Empty;
        public string SupplierName = string.Empty;
        public int DistanceMiles;
        public string SkuId = string.Empty;
        public int QuantityUnits;
        public FreightOptionKind FreightOption;
        public int GoodsCents;
        public int FreightCents;
        public int DeliveredTotalCents;
        public int DeliveredUnitPriceCents;
        public bool RequiresJourney; // SelfCollect only
    }

    public sealed class OffMapSupplierCatalog
    {
        private readonly Dictionary<string, OffMapSupplier> suppliers =
            new Dictionary<string, OffMapSupplier>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public int SupplierCount => suppliers.Count;

        public bool RegisterSupplier(OffMapSupplier supplier)
        {
            if (supplier == null || string.IsNullOrWhiteSpace(supplier.SupplierId))
            {
                diagnostics.Add("OffMapSupplierCatalog: supplier id required.");
                return false;
            }
            if (supplier.DistanceMiles < 0 || supplier.GoodsUnitPriceCents < 0)
            {
                diagnostics.Add($"OffMapSupplierCatalog: supplier '{supplier.SupplierId}' has a negative distance or price.");
                return false;
            }
            if (supplier.OfferedSkuIds == null || supplier.OfferedSkuIds.Count == 0)
            {
                diagnostics.Add($"OffMapSupplierCatalog: supplier '{supplier.SupplierId}' offers no SKUs.");
                return false;
            }
            if (suppliers.ContainsKey(supplier.SupplierId))
            {
                diagnostics.Add($"OffMapSupplierCatalog: supplier '{supplier.SupplierId}' already registered; duplicate ignored.");
                return false;
            }
            suppliers[supplier.SupplierId] = supplier;
            return true;
        }

        public List<OffMapSupplier> SuppliersForSku(string skuId)
        {
            var result = new List<OffMapSupplier>();
            if (string.IsNullOrWhiteSpace(skuId)) return result;
            foreach (OffMapSupplier supplier in suppliers.Values)
            {
                if (supplier.OfferedSkuIds != null && supplier.OfferedSkuIds.Contains(skuId))
                    result.Add(supplier);
            }
            result.Sort((a, b) => a.DistanceMiles.CompareTo(b.DistanceMiles));
            return result;
        }

        public List<OffMapFreightQuote> QuoteAll(
            string skuId, int quantityUnits, int freightCentsPerMile, int freightBusinessCommissionCents)
        {
            var quotes = new List<OffMapFreightQuote>();
            if (string.IsNullOrWhiteSpace(skuId) || quantityUnits <= 0)
            {
                diagnostics.Add("OffMapSupplierCatalog: quote needs a SKU and a positive quantity.");
                return quotes;
            }
            foreach (OffMapSupplier supplier in SuppliersForSku(skuId))
            {
                int goods = supplier.GoodsUnitPriceCents * quantityUnits;
                int sellerFreight = Math.Max(0, supplier.DistanceMiles) * Math.Max(0, freightCentsPerMile);
                quotes.Add(MakeQuote(supplier, skuId, quantityUnits, FreightOptionKind.SelfCollect, goods, 0, requiresJourney: true));
                quotes.Add(MakeQuote(supplier, skuId, quantityUnits, FreightOptionKind.SellerFreight, goods, sellerFreight, requiresJourney: false));
                quotes.Add(MakeQuote(supplier, skuId, quantityUnits, FreightOptionKind.FreightBusiness, goods,
                    sellerFreight + Math.Max(0, freightBusinessCommissionCents), requiresJourney: false));
            }
            quotes.Sort((a, b) =>
            {
                int byTotal = a.DeliveredTotalCents.CompareTo(b.DeliveredTotalCents);
                return byTotal != 0 ? byTotal : a.DistanceMiles.CompareTo(b.DistanceMiles);
            });
            return quotes;
        }

        private static OffMapFreightQuote MakeQuote(
            OffMapSupplier supplier, string skuId, int quantityUnits,
            FreightOptionKind option, int goods, int freight, bool requiresJourney)
        {
            int total = goods + freight;
            return new OffMapFreightQuote
            {
                SupplierId = supplier.SupplierId,
                SupplierName = supplier.SupplierName,
                DistanceMiles = supplier.DistanceMiles,
                SkuId = skuId,
                QuantityUnits = quantityUnits,
                FreightOption = option,
                GoodsCents = goods,
                FreightCents = freight,
                DeliveredTotalCents = total,
                DeliveredUnitPriceCents = total / Math.Max(1, quantityUnits),
                RequiresJourney = requiresJourney,
            };
        }
    }
}
