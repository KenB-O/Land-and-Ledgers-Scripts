using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;

namespace LandLedgers.Economy.BreadDrivers
{
    /// <summary>
    /// Series 2 Pass 6 (bread drivers): the caller-supplied off-map
    /// counterparty for a flour or notions order. Every field is
    /// caller-supplied: no vendor, price, freight rate, transit time,
    /// or supply figure is invented here or treated as canon. All
    /// figures in tests are test parameters.
    /// </summary>
    public sealed class Series2CounterpartyRegistration
    {
        public string SupplierId = string.Empty;
        public string SupplierName = string.Empty;
        public string SkuId = string.Empty;
        public int UnitGoodsCostCents;
        public int FreightSurchargeCents;
        public int InitialBudgetCents;
        public int TransitDays = 1;
        public Dictionary<string, int> SupplyUnits = new Dictionary<string, int>();

        /// <summary>
        /// Registers the counterparty with the market authority.
        /// Returns null on success, or a refusal diagnostic. Re-registering
        /// the same id is a market no-op (see ExternalMarketAuthority).
        /// </summary>
        public string RegisterInto(ExternalMarketAuthority market, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (market == null) return "Series2CounterpartyRegistration: no market authority supplied.";
            if (string.IsNullOrWhiteSpace(SupplierId) || string.IsNullOrWhiteSpace(SkuId))
                return "Series2CounterpartyRegistration: supplier id and SKU are required; no counterparty registered.";
            if (UnitGoodsCostCents < 0 || FreightSurchargeCents < 0)
                return "Series2CounterpartyRegistration: negative goods or freight cost refused.";
            var counterparty = new ExternalCounterparty
            {
                CounterpartyId = SupplierId,
                DisplayName = string.IsNullOrWhiteSpace(SupplierName) ? SupplierId : SupplierName,
                InitialBudgetCents = Math.Max(0, InitialBudgetCents),
                TransitDays = Math.Max(1, TransitDays),
            };
            if (SupplyUnits != null)
            {
                foreach (var entry in SupplyUnits) counterparty.SupplyUnits[entry.Key] = entry.Value;
            }
            if (!counterparty.SupplyUnits.ContainsKey(SkuId))
                counterparty.SupplyUnits[SkuId] = 0;
            string problem = market.RegisterCounterparty(counterparty);
            if (problem != null) return $"Series2CounterpartyRegistration: market refused '{SupplierId}' — {problem}";
            diag.Add($"Series2CounterpartyRegistration: '{SupplierId}' ({counterparty.DisplayName}) registered for SKU '{SkuId}' at {UnitGoodsCostCents}c goods + {FreightSurchargeCents}c seller-paid freight per order, transit {counterparty.TransitDays} day(s).");
            return null;
        }
    }

    /// <summary>
    /// Builds the single order document this pass accepts: a legacy
    /// OffMapFreightQuote. The flour/notions driver NEVER accepts a
    /// Pass 3 supplier-order draft directly: Pass 3 drafts are the
    /// player-UI path (see the review doc), and letting a draft reach
    /// PlaceOrder without quote validation would be a second order
    /// path to the same order. A Pass 3 draft reaches this driver only
    /// if the player UI first converts it into a validated quote, an
    /// explicit conversion step Codex owns, not a silent one.
    /// </summary>
    public static class Series2ProcurementQuoteBuilder
    {
        public static OffMapFreightQuote Build(
            Series2CounterpartyRegistration registration, int units,
            FreightOptionKind freightOption, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (registration == null) { diag.Add("Series2ProcurementQuoteBuilder: no registration; no quote built."); return null; }
            if (units <= 0) { diag.Add("Series2ProcurementQuoteBuilder: positive units required; no quote built."); return null; }
            if (freightOption != FreightOptionKind.SellerFreight && freightOption != FreightOptionKind.FreightBusiness)
            {
                diag.Add($"Series2ProcurementQuoteBuilder: freight option '{freightOption}' refused — only SellerFreight or FreightBusiness reach an order (SelfCollect needs a journey that does not exist; Unspecified names no arrangement).");
                return null;
            }
            int goods = units * registration.UnitGoodsCostCents;
            int total = goods + registration.FreightSurchargeCents;
            var quote = new OffMapFreightQuote
            {
                SupplierId = registration.SupplierId,
                SupplierName = registration.SupplierName,
                SkuId = registration.SkuId,
                QuantityUnits = units,
                FreightOption = freightOption,
                GoodsCents = goods,
                FreightCents = registration.FreightSurchargeCents,
                DeliveredTotalCents = total,
                DeliveredUnitPriceCents = units > 0 ? total / units : 0,
                RequiresJourney = false,
            };
            if (total % units != 0)
                diag.Add($"Series2ProcurementQuoteBuilder: delivered total {total}c over {units}u leaves {total % units}c remainder on the lot total, not per unit (goods divide evenly; freight does not have to).");
            return quote;
        }
    }

    /// <summary>
    /// Series 2 Pass 6 (bread drivers): scheduled flour/notions ordering
    /// and arrival on the named-SKU procurement service. Ordering goes
    /// through the ONE chosen path — the legacy OffMapQuoteToOrder
    /// bridge, which validates the quote (total recomputes, goods
    /// divide evenly, no SelfCollect teleport) before PlaceOrder.
    /// Stock is created only by ReceiveArrivals, after transit and
    /// after payment/obligation settlement, exactly as the service
    /// already guarantees. This driver never sets any save slot and
    /// never wires a bootstrapper; see Series2ProcurementSaveRegistration
    /// for the one line Codex must call.
    /// </summary>
    public sealed class Series2FlourProcurementDriver
    {
        private readonly OffMapNamedSkuProcurementService service;

        public Series2FlourProcurementDriver(OffMapNamedSkuProcurementService service)
        {
            this.service = service;
        }

        public OffMapNamedSkuProcurementService Service => service;

        public int SellableUnits(string skuId)
        {
            return service != null ? service.SellableUnits(skuId) : 0;
        }

        /// <summary>
        /// Places one order from a validated quote. Returns the order,
        /// or null with diagnostics when the quote or the service refuses.
        /// No cash moves here in either payment mode; no stock is created
        /// here. Call ReceiveArrivals on or after the expected arrival day.
        /// </summary>
        public NamedSkuOrder PlaceOrderFromQuote(
            OffMapFreightQuote quote, int dayIndex,
            NamedSkuPaymentMode paymentMode, ITradeCreditPort tradeCredit, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (service == null)
            {
                diag.Add("Series2FlourProcurementDriver: no procurement service; no order placed.");
                return null;
            }
            NamedSkuOrder order = OffMapQuoteToOrder.PlaceOrderFromQuote(
                quote, service, dayIndex, paymentMode, tradeCredit, diag);
            if (order != null)
                diag.Add($"Series2FlourProcurementDriver: order {order.OrderId} placed ({order.UnitsOrdered}u '{order.SkuId}', total {order.TotalCostCents}c, arrives day {order.ExpectedArrivalDayIndex}).");
            return order;
        }

        /// <summary>
        /// Receives arrivals due on or before <paramref name="dayIndex"/>.
        /// Idempotent: a receipt already received creates no second lot,
        /// and a payment-waiting order is retried from service state.
        /// Returns newly created lots (possibly empty, never null).
        /// </summary>
        public List<NamedSkuLot> ReceiveArrivals(int dayIndex, ITradeCreditPort tradeCredit, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (service == null)
            {
                diag.Add("Series2FlourProcurementDriver: no procurement service; nothing received.");
                return new List<NamedSkuLot>();
            }
            List<NamedSkuLot> created = service.ReceiveArrivals(dayIndex, tradeCredit);
            diag.Add($"Series2FlourProcurementDriver: ReceiveArrivals day {dayIndex} created {created.Count} lot(s) for business '{service.BusinessId}'.");
            return created;
        }
    }

    /// <summary>
    /// Series 2 Pass 6 (bread drivers): the named setter target for the
    /// flour/notions procurement service, so Codex can assign
    /// SaveLoadManager.PassXlProcurementService in ONE line. This object
    /// never assigns anything itself and is never called by any driver:
    /// the assignment happens only when Codex calls Apply, from
    /// production wiring, with the production SaveLoadManager in hand.
    /// The one line Codex writes is:
    ///   registration.Apply(s => saveLoadManager.PassXlProcurementService = s);
    /// Until that line runs, saves capture procurement as empty with a
    /// diagnostic (finding F19, still open after this pass), and old
    /// saves do not break. Save adoption for driver state beyond the
    /// service itself (planned days, fuel source state) is Pass 9 scope.
    /// </summary>
    public sealed class Series2ProcurementSaveRegistration
    {
        public OffMapNamedSkuProcurementService Service { get; }

        /// <summary>The exact production property this service must be assigned to.</summary>
        public string TargetProperty => "PassXlProcurementService";

        public Series2ProcurementSaveRegistration(OffMapNamedSkuProcurementService service)
        {
            Service = service;
        }

        /// <summary>
        /// Returns the assignment line as text for Codex to copy, e.g.
        /// "saveLoadManager.PassXlProcurementService = flourDriver.Service;".
        /// Text only; nothing is assigned by calling this.
        /// </summary>
        public string AssignmentLine(string saveLoadManagerVariable, string serviceExpression)
        {
            return $"{saveLoadManagerVariable}.{TargetProperty} = {serviceExpression};";
        }

        /// <summary>Codex-only: performs the one assignment, once, from production wiring.</summary>
        public void Apply(Action<OffMapNamedSkuProcurementService> setter)
        {
            setter?.Invoke(Service);
        }
    }
}
