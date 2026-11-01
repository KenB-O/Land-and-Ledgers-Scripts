using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Builds ordinary supplier orders from an offer. It handles package
    /// multiples and basket-level minimums without creating stock or a shipment.
    /// </summary>
    public static class GenericProcurementPlanner
    {
        public static bool TryBuildOrder(SupplierOffer offer,
            IReadOnlyDictionary<string, float> requestedQuantities,
            out SupplierPurchaseOrder order,
            out string reason)
        {
            order = null;
            reason = string.Empty;
            if (offer == null || string.IsNullOrWhiteSpace(offer.SupplierId))
            {
                reason = "Supplier offer is missing.";
                return false;
            }

            var result = new SupplierPurchaseOrder
            {
                OrderId = string.IsNullOrWhiteSpace(offer.OfferId) ? Guid.NewGuid().ToString("N") : $"order:{offer.OfferId}",
                SupplierId = offer.SupplierId,
                MinimumQuantity = Mathf.Max(0, offer.MinimumOrderQuantity),
                MinimumOrderValueCents = Mathf.Max(0, offer.MinimumOrderValueCents),
            };

            foreach (SupplierOfferLine line in offer.Lines ?? new List<SupplierOfferLine>())
            {
                if (line == null || requestedQuantities == null
                    || !requestedQuantities.TryGetValue(line.ProductId, out float requested)
                    || requested <= 0f) continue;
                if (line.PackageQuantity <= 0f)
                {
                    reason = $"Offer line '{line.ProductId}' has no valid package quantity.";
                    return false;
                }
                if (requested > line.AvailableQuantity + 0.001f)
                {
                    reason = $"Requested '{line.ProductId}' exceeds supplier availability.";
                    return false;
                }
                float packages = Mathf.Ceil(requested / line.PackageQuantity);
                float quantity = packages * line.PackageQuantity;
                if (quantity + 0.001f < line.MinimumQuantity)
                    quantity = Mathf.Ceil(line.MinimumQuantity / line.PackageQuantity) * line.PackageQuantity;
                if (quantity > line.AvailableQuantity + 0.001f)
                {
                    reason = $"Minimum package quantity for '{line.ProductId}' exceeds availability.";
                    return false;
                }
                int unitPrice = line.GetUnitPriceCents(quantity);
                result.Lines.Add(new SupplierOrderLine
                {
                    ProductId = line.ProductId,
                    Quantity = quantity,
                    PackageQuantity = line.PackageQuantity,
                    UnitPriceCents = unitPrice,
                });
                result.FreightCents += Mathf.Max(0, line.FreightCents);
                result.HandlingCents += Mathf.Max(0, line.HandlingCents);
                result.MinimumQuantity = Mathf.Max(result.MinimumQuantity, line.MinimumQuantity);
                result.MinimumOrderValueCents = Mathf.Max(result.MinimumOrderValueCents, line.MinimumOrderValueCents);
            }

            if (result.Lines.Count == 0)
            {
                reason = "Supplier order has no requested lines.";
                return false;
            }
            if (!result.MeetsMinimums)
            {
                reason = "Supplier order does not meet its basket minimums or package multiples.";
                return false;
            }
            order = result;
            return true;
        }
    }
}
