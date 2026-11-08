using System;
using LandLedgers.Economy;
using LandLedgers.Economy.Logistics;

namespace LandLedgers.Economy.Businesses.GeneralStore
{
    /// <summary>
    /// Moves one already-purchased named supplier lot into a business retail
    /// inventory. Supplier payment occurs once at ReceiveArrivals; this
    /// operation only transfers custody and safely restores source lots when
    /// the destination refuses the receipt.
    /// </summary>
    public static class PurchasedRetailLotReceipt
    {
        public sealed class Result
        {
            public bool Received;
            public bool Duplicate;
            public int Units;
            public string RetailLotId = string.Empty;
            public string ConsumptionId = string.Empty;
            public string Refusal = string.Empty;
            public bool SourceRestored;
        }

        /// <param name="log">Priority 9 Slice F3: the custody transfer log
        /// the receipt is recorded into, when supplied. Null (the default)
        /// means no recording — today behaviour. The log is an audit trail
        /// only: no flow reads it, and a logging failure never fails the
        /// receipt.</param>
        public static Result Transfer(
            OffMapNamedSkuProcurementService source,
            BusinessRuntimeState destination,
            NamedSkuLot supplierLot,
            string categoryId,
            string unitLabel,
            int dayIndex,
            CustodyTransferLog log = null)
        {
            var result = new Result();
            if (source == null || destination == null || supplierLot == null)
            {
                result.Refusal = "Purchased retail receipt needs source procurement, destination business, and supplier lot.";
                return result;
            }

            result.RetailLotId = "retail-feed-" + supplierLot.LotId;
            if (destination.FindRetailItemLot(result.RetailLotId) != null)
            {
                result.Duplicate = true;
                result.Refusal = $"Retail lot '{result.RetailLotId}' already exists; duplicate receipt refused.";
                return result;
            }

            int transferUnits = supplierLot.RemainingUnits;
            result.Units = transferUnits;
            if (transferUnits <= 0)
            {
                result.Refusal = $"Supplier lot '{supplierLot.LotId}' has no remaining units to receive.";
                return result;
            }

            int sellable = source.SellableUnits(supplierLot.SkuId);
            if (sellable < transferUnits)
            {
                result.Refusal = $"Supplier lot '{supplierLot.LotId}' has {transferUnits} remaining units, " +
                    $"but source custody has only {sellable} sellable '{supplierLot.SkuId}' units.";
                return result;
            }

            NamedSkuLotConsumptionRecord consumption = source.ConsumeLots(
                supplierLot.SkuId, transferUnits, "General Store item-lot receiving", dayIndex);
            if (consumption == null)
            {
                result.Refusal = $"Source refused consumption of {transferUnits} '{supplierLot.SkuId}' units.";
                return result;
            }
            result.ConsumptionId = consumption.ConsumptionId;
            if (consumption.Units != transferUnits)
            {
                result.Refusal = $"Source consumed {consumption.Units}/{transferUnits} units.";
                result.SourceRestored = source.UndoConsumption(consumption) == null;
                return result;
            }

            // Priority 9 Slice F4: the retail lot's own canonical
            // provenance key is appended to its provenance (free text kept
            // verbatim, appended once) so the lot's identity survives in
            // its own provenance field.
            string retailProvenance = LotIdentity.AppendProvenanceKey(
                supplierLot.Provenance + " -> retail receipt " + consumption.ConsumptionId +
                "; consumed source provenance: " + consumption.Provenance,
                CustodyTransferLog.BusinessAuthority(destination.BusinessId),
                result.RetailLotId);
            string refusal = destination.TryAddRetailItemLot(
                result.RetailLotId,
                supplierLot.SkuId,
                categoryId,
                unitLabel,
                transferUnits,
                destination.BusinessId,
                destination.BusinessId,
                retailProvenance,
                dayIndex);
            if (refusal != null)
            {
                result.Refusal = refusal;
                result.SourceRestored = source.UndoConsumption(consumption) == null;
                if (!result.SourceRestored)
                    result.Refusal += " Source restoration failed; manual reconciliation is required.";
                return result;
            }

            result.Received = true;
            // Priority 9 Slice F3: custody recording only, after the
            // receipt succeeds. The supplier is resolved from the lot's
            // order; when the order cannot be found the custodian names
            // the gap instead of inventing a supplier. A logging failure
            // never fails the receipt.
            if (log != null)
            {
                string supplierId = "unknown";
                if (source.Orders != null)
                {
                    foreach (NamedSkuOrder order in source.Orders)
                    {
                        if (order != null && string.Equals(order.OrderId, supplierLot.OrderId, StringComparison.Ordinal)
                            && !string.IsNullOrWhiteSpace(order.SupplierId))
                        {
                            supplierId = order.SupplierId;
                            break;
                        }
                    }
                }
                log.Record(new CustodyTransferRecord(
                    dayIndex, supplierLot.SkuId, transferUnits,
                    new LotKey(CustodyTransferLog.NamedSkuAuthority(source.BusinessId), supplierLot.LotId),
                    new LotKey(CustodyTransferLog.BusinessAuthority(destination.BusinessId), result.RetailLotId),
                    CustodyTransferLog.SupplierCustodian(supplierId),
                    CustodyTransferLog.BusinessCustodian(destination.BusinessId),
                    result.RetailLotId, CustodyTransferLog.CauseRetailReceipt), out _);
            }
            return result;
        }
    }
}
