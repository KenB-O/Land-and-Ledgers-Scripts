namespace LandLedgers.Economy
{
    /// <summary>Arrival/receiving adapter for generic goods. The transport system
    /// owns movement; this authority performs the single idempotent inventory write
    /// after the caller confirms physical arrival.</summary>
    public static class GenericShipmentReceivingAuthority
    {
        public static bool TryReceive(BusinessInstanceState business, string shipmentId,
            string productId, float quantity, int landedUnitCostCents,
            GenericProductDefinition definition, int dayIndex, out string reason)
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
            return true;
        }
    }
}
