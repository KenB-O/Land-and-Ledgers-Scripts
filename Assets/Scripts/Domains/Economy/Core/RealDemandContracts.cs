using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Core
{
    /// <summary>
    /// Pass VII Workstream A: a legitimate purchase intent from a real buyer.
    /// Every ordinary business revenue credit must trace to one of these.
    /// No buyer, no sale. (Canon: OffScreenSimulationRules rule 5.)
    /// </summary>
    [Serializable]
    public sealed class PurchaseIntent
    {
        public string BuyerKind = string.Empty;      // "household" | "business" | "external"
        public string BuyerId = string.Empty;        // household id / business instance id / external counterparty id
        public string SellerBusinessInstanceId = string.Empty;
        public string GoodsOrServiceId = string.Empty;
        public int QuantityUnits;
        public int AgreedPriceCents;
        public string FulfillmentMethod = string.Empty; // "pickup" | "delivery" | "immediate"
        public string SettlementKind = string.Empty;    // "cash" | "credit" | "external"
        public string Provenance = string.Empty;        // need -> knowledge -> supplier chain
    }

    /// <summary>
    /// A source of real customer demand. Implementations read actual household
    /// needs, business input requirements, or modeled external markets.
    /// Never manufactures buyers.
    /// </summary>
    public interface IRealDemandSource
    {
        List<PurchaseIntent> CollectDemand(int dayIndex, List<string> diagnostics);
    }

    /// <summary>
    /// Boundary accounting for external trade. Every inflow names its external
    /// source. Local cash is NOT conserved across the boundary without it.
    /// External counterparties have bounded budgets and real demand; they are
    /// never infinite.
    /// </summary>
    public interface IExternalMarketAuthority
    {
        string RecordExternalSale(
            string externalCounterpartyId, string goodsId, int quantity,
            int priceCents, List<string> diagnostics);

        string RecordExternalPurchase(
            string externalCounterpartyId, string goodsId, int quantity,
            int priceCents, List<string> diagnostics);
    }
}
