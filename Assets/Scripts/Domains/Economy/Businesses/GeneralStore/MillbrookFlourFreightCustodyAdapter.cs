using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy;
using LandLedgers.Economy.Logistics;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.GeneralStore
{
    /// <summary>
    /// Bridges a paid Millbrook order lot into the Bakery's pound-based flour
    /// bin when the detailed freight authority commits pickup and receipt.
    /// The order's package count is the freight unit; its recorded inventory
    /// pounds are the bakery stock unit. The separate bakery-unit quotation is
    /// preserved in provenance and is never treated as pounds.
    /// </summary>
    public sealed class MillbrookFlourFreightCustodyAdapter
    {
        public const string FlourSkuId = "bakery_flour_sack";

        private readonly OffMapAidBusinessRuntime supplier;
        private readonly BakeryShopRuntime bakery;
        private readonly EntityIdRegistry lotIds;

        public MillbrookFlourFreightCustodyAdapter(
            OffMapAidBusinessRuntime supplier,
            BakeryShopRuntime bakery,
            EntityIdRegistry lotIds)
        {
            this.supplier = supplier;
            this.bakery = bakery;
            this.lotIds = lotIds;
        }

        public bool CommitSourceLot(LogisticsShipmentState shipment, int packageUnits, out string reason)
        {
            reason = string.Empty;
            OffMapAidCargoLot lot = ResolveLot(shipment, packageUnits, out reason);
            if (lot == null) return false;
            if (lot.Received)
            {
                reason = "Millbrook lot has already been received by its destination.";
                return false;
            }

            reason = supplier.CommitFreightPickup(lot.LotId, shipment.ShipmentId, packageUnits);
            if (reason != null) return false;
            reason = $"Millbrook lot '{lot.LotId}' committed once to shipment '{shipment.ShipmentId}' " +
                $"({packageUnits} sack(s), {lot.InventoryUnits} {lot.InventoryUnit}).";
            return true;
        }

        public bool ReceiveCargoLot(LogisticsShipmentState shipment, int packageUnits, int dayIndex, out string reason)
        {
            reason = string.Empty;
            OffMapAidCargoLot lot = ResolveLot(shipment, packageUnits, out reason);
            if (lot == null) return false;
            if (!lot.SourceCommitted || !string.Equals(lot.ShipmentId, shipment.ShipmentId, StringComparison.Ordinal))
            {
                reason = $"Millbrook lot '{lot.LotId}' is not in this shipment's wagon custody.";
                return false;
            }
            if (lot.InventoryUnits <= 0 || !string.Equals(lot.InventoryUnit, "lb", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Millbrook lot '{lot.LotId}' has no valid pound quantity for Bakery flour stock.";
                return false;
            }

            BakeryFlourLot existing = FindBakeryReceipt(lot.LotId);
            if (existing != null)
            {
                if (existing.Units != lot.InventoryUnits || !string.Equals(existing.FlourName,
                        BakeryBreadCatalog.FlourItemId, StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"Bakery already has a conflicting receipt for Millbrook lot '{lot.LotId}'.";
                    return false;
                }
                reason = supplier.ReceiveAtBakery(lot.LotId, shipment.ShipmentId,
                    bakery.BusinessInstanceId, out _);
                if (reason != null) return false;
                reason = $"Millbrook lot '{lot.LotId}' receipt was already present in Bakery stock; duplicate receipt accepted idempotently.";
                return true;
            }

            // Validate the source-side idempotency key before adding the
            // destination lot. ReceiveAtBakery is deterministic after these
            // checks and the destination is updated in this same synchronous
            // callback, so no daily tick can observe an intermediate state.
            if (lot.Received && (!string.Equals(lot.DestinationBusinessId, bakery.BusinessInstanceId, StringComparison.Ordinal)
                || !string.Equals(lot.ShipmentId, shipment.ShipmentId, StringComparison.Ordinal)))
            {
                reason = $"Millbrook lot '{lot.LotId}' was already delivered to a different shipment or business.";
                return false;
            }

            var flourLot = new BakeryFlourLot
            {
                LotId = lotIds != null ? lotIds.Allocate(EntityKind.Lot) : EntityId.Invalid,
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = lot.InventoryUnits,
                AcquiredDayIndex = Math.Max(0, dayIndex),
                ImportOrderId = lot.LotId,
                OriginName = lot.SupplierBusinessId,
                SupplierNote = $"Millbrook paid order {lot.OrderId}; source lot {lot.LotId}; " +
                    $"{lot.PackageUnits} sack(s), {lot.InventoryUnits} {lot.InventoryUnit}; " +
                    $"supplier's separate bakery-unit figure={lot.BakeryUnits}."
            };
            string stockRefusal = bakery.FlourStock.ReceiveLot(flourLot, new List<string>());
            if (stockRefusal != null)
            {
                reason = stockRefusal;
                return false;
            }

            reason = supplier.ReceiveAtBakery(lot.LotId, shipment.ShipmentId,
                bakery.BusinessInstanceId, out int supplierBakeryUnits);
            if (reason != null)
            {
                // No supported removal/compensation operation exists on
                // BakeryFlourStock. This branch should be unreachable after
                // prevalidation; fail visibly rather than minting or silently
                // duplicating another lot.
                reason = "Millbrook source receipt refused after the Bakery lot was added; investigate custody reconciliation: " + reason;
                return false;
            }

            reason = $"Received Millbrook lot '{lot.LotId}' as {flourLot.Units} lb Bakery flour " +
                $"(supplier records {supplierBakeryUnits} bakery units separately).";
            return true;
        }

        private OffMapAidCargoLot ResolveLot(LogisticsShipmentState shipment, int packageUnits, out string reason)
        {
            reason = string.Empty;
            if (supplier == null || bakery == null || shipment == null || lotIds == null)
            {
                reason = "Millbrook freight custody needs the supplier, Bakery, shared lot-id authority, and shipment.";
                return null;
            }
            if (!string.Equals(shipment.sourceBusinessInstanceId, supplier.BusinessId, StringComparison.Ordinal)
                || !string.Equals(shipment.destinationBusinessInstanceId, bakery.BusinessInstanceId, StringComparison.Ordinal))
            {
                reason = "Shipment source or destination does not match the Millbrook-to-Bakery order.";
                return null;
            }
            if (packageUnits <= 0 || shipment.PlannedQuantityUnits != packageUnits
                || !string.Equals(shipment.cargoSkuId, FlourSkuId, StringComparison.Ordinal))
            {
                reason = "Shipment package quantity or flour SKU does not match the paid Millbrook lot.";
                return null;
            }

            foreach (OffMapAidCargoLot candidate in supplier.Lots)
            {
                if (candidate != null && string.Equals(candidate.LotId, shipment.CargoLotId, StringComparison.Ordinal))
                    return candidate;
            }
            reason = $"Millbrook paid lot '{shipment.CargoLotId}' does not exist.";
            return null;
        }

        private BakeryFlourLot FindBakeryReceipt(string sourceLotId)
        {
            foreach (BakeryFlourLot lot in bakery.FlourStock.Lots)
            {
                if (lot != null && string.Equals(lot.ImportOrderId, sourceLotId, StringComparison.Ordinal))
                    return lot;
            }
            return null;
        }
    }
}
