using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.GeneralStore
{
    /// <summary>
    /// Pass XL phase 4 (finding F05): the quote-to-order bridge between
    /// OffMapSupplierCatalog.QuoteAll and
    /// OffMapNamedSkuProcurementService.PlaceOrder. Until this pass the
    /// two were unconnected: quotes had no production caller, and orders
    /// took raw costs, so a quoted price could never be acted on in code.
    ///
    /// Validation, in order, before any order exists:
    ///   - The quote must name a SKU, a supplier, a positive quantity,
    ///     and non-negative goods/freight, and its delivered total must
    ///     recompute exactly as goods + freight. A tampered total is
    ///     refused: the order must cost exactly what was quoted.
    ///   - Goods must divide evenly by quantity. PlaceOrder takes a unit
    ///     goods cost and recomputes goods as units x unit cost; a
    ///     non-divisible goods total would silently lose cents, so it
    ///     is refused instead of rounded.
    ///   - SelfCollect quotes are refused. They carry RequiresJourney,
    ///     and no journey/travel execution exists for a procurement
    ///     pickup — ordering one as a market order would teleport goods
    ///     (finding F38). SellerFreight and FreightBusiness quotes are
    ///     accepted; their freight is passed through as the order's
    ///     seller-paid freight surcharge.
    ///   - A FreightBusiness quote does NOT create or commission a
    ///     freight business. The freight-business commission is priced
    ///     into the quote's freight cents and passed through as a
    ///     surcharge only. Creating a freight business is a separate,
    ///     Kennedy-scoped feature, never implied by ordering.
    ///
    /// The supplier itself is validated by PlaceOrder against the
    /// ExternalMarketAuthority (unknown counterparty = refused, no order,
    /// no obligation, no stock). This bridge adds no price, rate, or
    /// supplier of its own: every cent comes from the quote, and every
    /// quote comes from a catalog the caller populated — the production
    /// catalog is empty until Codex/Kennedy register real suppliers
    /// (finding F37), and all catalog examples remain test parameters.
    /// Plain C#, no Unity dependency, Roslyn-testable.
    /// </summary>
    public static class OffMapQuoteToOrder
    {
        public static NamedSkuOrder PlaceOrderFromQuote(
            OffMapFreightQuote quote,
            OffMapNamedSkuProcurementService procurement,
            int dayIndex,
            NamedSkuPaymentMode paymentMode,
            ITradeCreditPort tradeCredit,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (quote == null)
            {
                diagnostics.Add("OffMapQuoteToOrder: null quote; no order placed.");
                return null;
            }
            if (procurement == null)
            {
                diagnostics.Add("OffMapQuoteToOrder: null procurement service; no order placed.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(quote.SkuId) || string.IsNullOrWhiteSpace(quote.SupplierId) || quote.QuantityUnits <= 0)
            {
                diagnostics.Add("OffMapQuoteToOrder: quote needs a SKU, a supplier, and a positive quantity; no order placed.");
                return null;
            }
            if (quote.GoodsCents < 0 || quote.FreightCents < 0)
            {
                diagnostics.Add("OffMapQuoteToOrder: quote carries negative goods or freight; no order placed.");
                return null;
            }
            if (quote.DeliveredTotalCents != quote.GoodsCents + quote.FreightCents)
            {
                diagnostics.Add(
                    $"OffMapQuoteToOrder: quote total {quote.DeliveredTotalCents}c does not recompute as " +
                    $"goods {quote.GoodsCents}c + freight {quote.FreightCents}c; refused, no order placed.");
                return null;
            }
            if (quote.FreightOption == FreightOptionKind.SelfCollect)
            {
                diagnostics.Add("OffMapQuoteToOrder: SelfCollect quotes require a journey (player/NPC travel to the supplier); no pickup journey is wired, so the quote is refused rather than teleported. Choose SellerFreight or FreightBusiness.");
                return null;
            }
            if (quote.GoodsCents % quote.QuantityUnits != 0)
            {
                diagnostics.Add(
                    $"OffMapQuoteToOrder: goods {quote.GoodsCents}c do not divide evenly by {quote.QuantityUnits} units; " +
                    "refused rather than rounding cents away. Re-quote with a divisible goods total.");
                return null;
            }
            int unitGoodsCostCents = quote.GoodsCents / quote.QuantityUnits;
            NamedSkuOrder order = procurement.PlaceOrder(
                quote.SkuId, quote.QuantityUnits, unitGoodsCostCents, quote.FreightCents,
                quote.SupplierId, dayIndex, paymentMode, tradeCredit);
            if (order == null)
            {
                diagnostics.Add("OffMapQuoteToOrder: procurement service refused the quoted order (see its diagnostics); no order, no obligation, no stock.");
                return null;
            }
            if (order.TotalCostCents != quote.DeliveredTotalCents)
            {
                diagnostics.Add(
                    $"OffMapQuoteToOrder: placed order total {order.TotalCostCents}c differs from quoted total {quote.DeliveredTotalCents}c; " +
                    "this must never happen — the order stands as placed by the service, and the mismatch is diagnosed here.");
            }
            else
            {
                diagnostics.Add(
                    $"OffMapQuoteToOrder: order {order.OrderId} placed from quote — {quote.QuantityUnits}u '{quote.SkuId}' " +
                    $"from '{quote.SupplierId}', goods {quote.GoodsCents}c + freight {quote.FreightCents}c = {order.TotalCostCents}c.");
            }
            return order;
        }
    }
}
