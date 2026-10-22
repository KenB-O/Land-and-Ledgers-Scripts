using LandLedgers.Primitives;

namespace LandLedgers.Economy
{
    /// <summary>Arrival/receiving adapter for generic goods. The transport system
    /// owns movement; this authority performs the single idempotent inventory write
    /// after the caller confirms physical arrival.</summary>
    public static class GenericShipmentReceivingAuthority
    {
        public static bool TryReceive(BusinessInstanceState business, string shipmentId,
            string productId, float quantity, int landedUnitCostCents,
            GenericProductDefinition definition, int dayIndex, out string reason,
            SharedCategoryStockLedger provenanceLedger = null,
            EntityIdRegistry provenanceIds = null)
        {
            reason = string.Empty;
            if (business == null)
            {
                reason = "Receiving requires a destination Business.";
                return false;
            }
            if (!business.GenericConfiguration.TryReceiveInventoryShipment(shipmentId, productId,
                quantity, landedUnitCostCents, definition, $"shipment:{shipmentId}", dayIndex))
            {
                reason = "Shipment was already received or has invalid cargo.";
                return false;
            }
            if (provenanceLedger != null)
            {
                // Pass IX WS6 (IDENT-001): arrival is a provenance boundary —
                // the arriving goods consume their ExternalSupply record
                // (opened at dispatch, or opened-and-consumed here when the
                // arrival is the first sight of them). Freight itself carries
                // no lot ids; provenance rides this ledger at origin/destination.
                provenanceLedger.RecordArrival(
                    $"shipment:{shipmentId}", productId, quantity, business.InstanceId, dayIndex, provenanceIds);
            }
            return true;
        }
    }
}
