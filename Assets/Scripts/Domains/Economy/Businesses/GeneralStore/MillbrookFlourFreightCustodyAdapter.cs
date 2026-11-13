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
        private const float PoundsToKilograms = 0.45359237f;
        private const float FlourSackVolumeCubicMeters = 0.01f;

        /// <summary>
        /// Builds the freight service's per-package physical specification
        /// from one paid Millbrook lot. Inventory pounds are divided by the
        /// lot's sack count before conversion; the existing sack-volume
        /// factor is per sack and is multiplied by the shipment quantity only
        /// inside FreightShipmentService.
        /// </summary>
        public static FreightCargoSpec BuildFreightCargoSpec(OffMapAidCargoLot lot)
        {
            if (lot == null || lot.PackageUnits <= 0) return null;
            return new FreightCargoSpec
            {
                MassKgPerUnit = ((float)Math.Max(0, lot.InventoryUnits) / lot.PackageUnits) * PoundsToKilograms,
                VolumeM3PerUnit = FlourSackVolumeCubicMeters,
            };
        }

        private readonly OffMapAidBusinessRuntime supplier;
        private readonly BakeryShopRuntime bakery;
        private readonly EntityIdRegistry lotIds;

        /// <summary>
        /// Priority 9 Slice F3 (P9 plan section 4 Slice F3): the custody
        /// transfer log freight commits and receipts are recorded into,
        /// when set. Null (the default) means no recording — today
        /// behaviour. The log is an audit trail only: no flow reads it,
        /// and a logging failure never fails a commit or receipt.
        /// </summary>
        public CustodyTransferLog CustodyLog { get; }

        public MillbrookFlourFreightCustodyAdapter(
            OffMapAidBusinessRuntime supplier,
            BakeryShopRuntime bakery,
            EntityIdRegistry lotIds,
            CustodyTransferLog custodyLog = null)
        {
            this.supplier = supplier;
            this.bakery = bakery;
            this.lotIds = lotIds;
            CustodyLog = custodyLog;
        }

        /// <param name="dayIndex">The simulation day of the pickup, when the
        /// caller knows it. Defaults to UnknownDayIndex: the freight-commit
        /// delegate path carries no day, so the record names the gap instead
        /// of inventing one (the pickup drive passes its day explicitly).</param>
        public bool CommitSourceLot(LogisticsShipmentState shipment, int packageUnits, out string reason,
            int dayIndex = CustodyTransferLog.UnknownDayIndex)
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
            // Priority 9 Slice F3: custody recording only, after the commit
            // succeeds. The wagon holds the lot now; wagon custody is not a
            // lot, so the destination key is Empty. A logging failure never
            // fails the commit — a repeated commit for the same shipment is
            // idempotent in the supplier and refused as a duplicate by the
            // log, so the transfer is still recorded exactly once.
            if (CustodyLog != null)
            {
                string wagonId = shipment.physicalWagonAssetId;
                if (dayIndex < 0)
                {
                    CustodyLog.Note("custody: freight-commit for shipment '" + shipment.ShipmentId +
                        "' has no day at this site — the freight-commit delegate path carries no day; recorded as day -1 instead of inventing one.");
                }
                if (string.IsNullOrWhiteSpace(wagonId))
                {
                    CustodyLog.Note("custody: freight-commit for shipment '" + shipment.ShipmentId +
                        "' has no wagon asset id at this site; custodian recorded as wagon:unknown.");
                }
                CustodyLog.Record(new CustodyTransferRecord(
                    dayIndex, lot.SkuId, packageUnits,
                    new LotKey(CustodyTransferLog.MillbrookAuthority(supplier.BusinessId), lot.LotId),
                    LotKey.Empty,
                    CustodyTransferLog.SupplierCustodian(supplier.BusinessId),
                    CustodyTransferLog.WagonCustodian(string.IsNullOrWhiteSpace(wagonId) ? "unknown" : wagonId),
                    shipment.ShipmentId, CustodyTransferLog.CauseFreightCommit), out _);
            }
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
                // Priority spoilage integration: attach the condition
                // state to a lot that was received before this slice
                // attached one (attach-if-absent — never overwrite an
                // existing state, never double-attach on re-receipt).
                AttachConditionIfAbsent(existing, lot, dayIndex);
                // Priority 9 Slice F3: the duplicate receipt is idempotent
                // in the adapter, and the log refuses the second record for
                // the same CauseKind plus CauseId — the transfer is still
                // recorded exactly once.
                RecordFreightReceive(shipment, lot, existing.LotId, dayIndex);
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
            // Priority spoilage integration: the freight receive is a
            // transfer — the shipment itself carries no condition field
            // (blocked seam (d)), so the adapter re-reads the source
            // cargo lot and attaches the same Unknown condition to the
            // new flour lot, keyed to that lot, in bakery pounds,
            // anchored at the receive day. Flour has no authored aging
            // rule, so the state stays Unknown.
            AttachConditionIfAbsent(flourLot, lot, dayIndex);
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

            // Priority 9 Slice F3: custody recording only, after the receipt
            // succeeds. The receipt moves pounds (the Bakery stock unit),
            // not sacks (the freight unit). A logging failure never fails
            // the receipt.
            RecordFreightReceive(shipment, lot, flourLot.LotId, dayIndex);
            reason = $"Received Millbrook lot '{lot.LotId}' as {flourLot.Units} lb Bakery flour " +
                $"(supplier records {supplierBakeryUnits} bakery units separately).";
            return true;
        }

        /// <summary>
        /// Priority spoilage integration: attaches an Unknown condition
        /// state to a flour lot received from a Millbrook cargo lot,
        /// unless the lot already carries one. The state is keyed to
        /// the flour lot (not the cargo lot), names flour in bakery
        /// pounds, is anchored at the receive day, and names the source
        /// cargo lot in its provenance. Flour has no authored aging
        /// rule, so the condition is Unknown — explicit, never a
        /// guessed shelf life.
        /// </summary>
        private void AttachConditionIfAbsent(BakeryFlourLot flourLot, OffMapAidCargoLot sourceLot, int dayIndex)
        {
            if (flourLot == null || sourceLot == null || flourLot.HasPerishableCondition) return;
            flourLot.PerishableCondition = new PerishableLotState(
                new LotKey(CustodyTransferLog.BakeryAuthority(bakery.BusinessInstanceId), flourLot.LotId.ToString()),
                BakeryBreadCatalog.FlourItemId,
                flourLot.Units,
                Math.Max(0, dayIndex))
                .WithSource(sourceLot.LotId, sourceLot.SupplierBusinessId, "bakery:" + bakery.BusinessInstanceId)
                .CaptureSaveDto();
            flourLot.HasPerishableCondition = true;
        }

        /// <summary>
        /// Priority 9 Slice F3: one log-only freight-receive record. The
        /// wagon asset id is read from the shipment; when the shipment
        /// holds none, the gap is named in diagnostics instead of inventing
        /// an id.
        /// </summary>
        private void RecordFreightReceive(LogisticsShipmentState shipment, OffMapAidCargoLot lot, EntityId bakeryLotId, int dayIndex)
        {
            if (CustodyLog == null) return;
            string wagonId = shipment.physicalWagonAssetId;
            if (string.IsNullOrWhiteSpace(wagonId))
            {
                CustodyLog.Note("custody: freight-receive for shipment '" + shipment.ShipmentId +
                    "' has no wagon asset id at this site; custodian recorded as wagon:unknown.");
            }
            CustodyLog.Record(new CustodyTransferRecord(
                dayIndex, lot.SkuId, lot.InventoryUnits,
                new LotKey(CustodyTransferLog.MillbrookAuthority(supplier.BusinessId), lot.LotId),
                new LotKey(CustodyTransferLog.BakeryAuthority(bakery.BusinessInstanceId), bakeryLotId.ToString()),
                CustodyTransferLog.WagonCustodian(string.IsNullOrWhiteSpace(wagonId) ? "unknown" : wagonId),
                CustodyTransferLog.BakeryAuthority(bakery.BusinessInstanceId),
                shipment.ShipmentId, CustodyTransferLog.CauseFreightReceive), out _);
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
