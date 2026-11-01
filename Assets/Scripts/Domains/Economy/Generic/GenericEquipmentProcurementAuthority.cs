using System;
using LandLedgers.Economy.Blacksmith;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Orchestrates productive-Asset acquisition without spawning equipment.
    /// Each callback is an existing procurement/logistics authority: the generic
    /// layer records lifecycle state only after that authority succeeds.
    /// </summary>
    public static class GenericEquipmentProcurementAuthority
    {
        public static bool TryAdvanceToOperational(
            EquipmentProcurementState state,
            Func<SupplierPurchaseOrder, bool> placeOrder,
            Func<SupplierPurchaseOrder, bool> settleTerms,
            Func<string, bool> dispatchShipment,
            Func<string, EquipmentAsset> receiveAsset,
            string businessId,
            string locationId,
            int installationDay,
            out string reason,
            Action<EquipmentAsset> registerReceivedAsset = null)
        {
            reason = string.Empty;
            if (state == null || state.Order == null)
            {
                reason = "Equipment procurement requires a real purchase order.";
                return false;
            }
            if (state.Stage == EquipmentProcurementStage.Installed) return true;
            if (state.Stage == EquipmentProcurementStage.Quoted)
            {
                if (placeOrder == null || !placeOrder(state.Order) || !state.MarkOrdered())
                {
                    reason = "Supplier order was not accepted.";
                    return false;
                }
            }
            if (state.Stage == EquipmentProcurementStage.Ordered)
            {
                if (settleTerms == null || !settleTerms(state.Order) || !state.MarkPaidOrFinanced())
                {
                    reason = "Purchase terms were not settled.";
                    return false;
                }
            }
            string shipmentId = string.IsNullOrWhiteSpace(state.ShipmentId)
                ? state.Order.OrderId + ":shipment"
                : state.ShipmentId;
            if (state.Stage == EquipmentProcurementStage.PaidOrFinanced)
            {
                if (dispatchShipment == null || !dispatchShipment(shipmentId) || !state.MarkShipped(shipmentId))
                {
                    reason = "Equipment shipment was not dispatched.";
                    return false;
                }
            }
            if (state.Stage == EquipmentProcurementStage.Shipped)
            {
                EquipmentAsset asset = receiveAsset?.Invoke(shipmentId);
                if (asset == null || !state.Receive(asset, businessId, locationId))
                {
                    reason = "Equipment was not received into the Business asset authority.";
                    return false;
                }
                registerReceivedAsset?.Invoke(asset);
            }
            if (state.Stage == EquipmentProcurementStage.Received && !state.Install(installationDay))
            {
                reason = "Equipment setup/install could not be completed.";
                return false;
            }
            return state.Stage == EquipmentProcurementStage.Installed;
        }
    }
}
