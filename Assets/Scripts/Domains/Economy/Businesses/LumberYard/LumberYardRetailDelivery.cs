using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.LumberYard
{
    /// <summary>
    /// C-6 (expanded campaign 2026-11-16, shared with F): the yard side
    /// of a retail lumber sale — the delivery request. The buyer side (F)
    /// owns who orders: it creates the retail sale, settles the goods money
    /// through its own purchase flow (the F4 Pay pattern), measures the
    /// route, and enqueues the request. This request carries the measured
    /// route (miles, transit seconds, road-condition multiplier) — the
    /// driver validates positivity but never invents a route.
    /// </summary>
    [Serializable]
    public sealed class LumberYardRetailDeliveryRequest
    {
        public EntityId SaleId = EntityId.Invalid;
        public string DestinationLabel = string.Empty;
        public string DestinationBusinessInstanceId = string.Empty;
        public int Miles;
        public float TransitGameSeconds = 10000f;
        public float ConditionMultiplier;
        public bool YardDelivers = true;
        public string CarrierLabel = string.Empty;
        public bool BuyerProvidesUnloading;
        public string BuyerBusinessId = string.Empty;
        /// <summary>
        /// The buyer-side receiving authority: accepts the delivered units
        /// into the buyer's custody and returns the accepted count. The
        /// service requires it when the yard delivers — a shipment with no
        /// receiver is held Delayed, never falsely marked delivered
        /// (the PASS7-008 rule). Transient: the request queue is not
        /// persisted, so a delegate carried here lives for the session only.
        /// </summary>
        public ReceiveFreightShipmentHandler ReceiveShipment;

        public LumberYardRetailDeliveryRequest() { }
    }

    /// <summary>
    /// C-6: the runtime bindings one retail delivery executes against.
    /// The rate card is the freight business's configured card (never
    /// invented in code); the wagon is the production wagon; the crew are
    /// real person ids resolved by the production driver; the receiver is
    /// the buyer-side authority carried on the request.
    /// </summary>
    public sealed class LumberYardRetailDeliveryExecution
    {
        public DevelopmentTownConfiguration.FreightServiceRateCard RateCard;
        public EquipmentAsset Wagon;
        public IBusinessCashPort BuyerCash;
        public IBusinessCashPort CarrierCash;
        public EntityId LoaderId = EntityId.Invalid;
        public EntityId DriverId = EntityId.Invalid;
        public List<EntityId> DraftAnimalIds = new List<EntityId>();
        public EntityId UnloaderId = EntityId.Invalid;
        public TaskAuthority Tasks;
        public WorkTimeBudgetStore Budgets;
        public PersonScheduleTracker Schedules;
        public LumberYardRetailDeliveryExecution() { }
    }

    /// <summary>
    /// C-6 (expanded campaign 2026-11-16, shared with F): yard retail
    /// delivery execution. The yard's retail sale (TrySellLumberRetail)
    /// records the sale and withdraws real lots; the goods money is settled
    /// by the buyer-side flow that created the sale (the F4 Pay pattern).
    /// This service is the DELIVERY leg only:
    ///
    /// - it quotes the delivery through the DEF-016 cargo-aware calculator
    ///   (the freight business's configured rate card, the lumber cargo
    ///   spec, the production wagon's capacity, round-trip charging);
    /// - it schedules the delivery obligation through the yard's own
    ///   ScheduleDeliveryForSale, with the policy built from the quote so
    ///   the recorded order carries exactly the quoted charge for the
    ///   ledger — the delivery charge IS the DEF-016 quote, and no second
    ///   charge exists (the policy's per-unit-mile and discount fields stay
    ///   zero because the card prices per trip, not per unit-mile; a buyer
    ///   who provides unloading is recorded on the order but the wagon trip
    ///   costs what it costs);
    /// - it settles the delivery charge per trip, exactly once per trip
    ///   (buyer to carrier, paid before the trip runs — the construction
    ///   coordinator's pattern; a trip that fails before departing refunds
    ///   its fare in full, and only a departed trip's fare stands for
    ///   the attempt, so only trips that ran are paid for);
    /// - it moves the lumber through the real freight chain
    ///   (FreightShipmentService, one shipment per trip, conservation
    ///   verified, received at the destination through the caller-supplied
    ///   receiver) — never a free gift, never a phantom delivery.
    ///
    /// Construction sales are refused: construction freight runs through
    /// ConstructionMaterialFreightCoordinator, never here. Buyer-haulage
    /// (YardDelivers false) records the obligation with a zero delivery
    /// charge and runs no freight — the yard performs no delivery and
    /// charges no delivery fee; the lumber is the buyer's from the sale and
    /// pickup/completion is the buyer's act (MarkDeliveryComplete is the
    /// buyer side's call).
    ///
    /// Idempotency: one delivery order per sale (scanned from the yard's
    /// own DeliveryRegister); the order's per-trip fare record guards the
    /// single settlement; ShipmentIds on the order let a retry skip
    /// Completed trips. A retry finding a non-Completed, cargo-carrying
    /// shipment for a trip refuses loudly — the goods are accounted for in
    /// that shipment; reconcile, do not re-ship. A Planned or pre-load
    /// Failed shipment owns zero cargo and may be abandoned for a fresh
    /// shipment id.
    ///
    /// The service holds no save slot of its own: the delivery order (with
    /// its per-trip fare record and shipment ids) persists through the
    /// yard's own save DTO, and the shipment sequence is re-seeded from
    /// the freight service's tracked shipments (the QA-20 pattern), so a
    /// service rebuilt after a load never re-issues an id.
    /// </summary>
    public sealed class LumberYardRetailDelivery
    {
        private readonly LumberYardShopRuntime yard;
        private readonly FreightShipmentService freight;
        private readonly List<string> diagnostics;
        private int sequence;

        public LumberYardRetailDelivery(
            LumberYardShopRuntime yard,
            FreightShipmentService freight,
            List<string> diagnostics)
        {
            this.yard = yard;
            this.freight = freight;
            this.diagnostics = diagnostics ?? new List<string>();
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// C-6: the DEF-016 cargo-aware delivery quote for a retail sale's
        /// lumber load. Pure: no yard, no cash, no freight service needed.
        /// Refuses exactly as the calculator refuses (non-positive miles or
        /// units, no wagon capacity, negative rate card) — a refused quote
        /// is a refused delivery, never a guessed charge.
        /// </summary>
        public static bool TryQuoteDeliveryCharge(
            int lumberUnits,
            float miles,
            DevelopmentTownConfiguration.FreightServiceRateCard rateCard,
            EquipmentAsset wagon,
            float conditionMultiplier,
            out FreightServiceQuote quote,
            out string reason)
        {
            quote = null;
            reason = string.Empty;
            if (rateCard == null)
            {
                reason = "LumberYardRetailDelivery: no freight rate card — the delivery charge is quoted from the freight business's configured card, never invented.";
                return false;
            }
            if (wagon == null)
            {
                reason = "LumberYardRetailDelivery: no wagon — a delivery quote needs the production wagon's capacity.";
                return false;
            }
            return FreightServiceQuoteCalculator.TryCalculateCargo(
                miles,
                rateCard.baseServiceFeeCents,
                rateCard.ratePerMileCents,
                rateCard.chargeRoundTrip,
                lumberUnits,
                LumberCargoSpec.Spec,
                wagon.PayloadMassCapacityKg,
                wagon.CargoVolumeCapacityM3,
                conditionMultiplier,
                out quote,
                out reason);
        }

        /// <summary>
        /// C-6: executes one retail delivery end to end — quote, schedule,
        /// settle per trip, freight per trip, receive, mark delivered.
        /// Returns null on success (delivered, or already delivered — a
        /// no-op re-execution); a refusal otherwise, with the order
        /// recording how far the delivery got. A buyer who cannot pay the
        /// delivery charge waits loudly: the affordability pre-check runs
        /// before any cash moves or any freight is created.
        /// </summary>
        public string ExecuteRetailDelivery(
            LumberYardSaleRecord sale,
            LumberYardRetailDeliveryRequest request,
            LumberYardRetailDeliveryExecution execution,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (yard == null)
                return "LumberYardRetailDelivery: no yard runtime — delivery refused.";
            if (freight == null)
                return "LumberYardRetailDelivery: no FreightShipmentService — lumber moves through the real freight chain or not at all.";
            if (sale == null)
                return "LumberYardRetailDelivery: no sale — deliveries fulfill real sales, never thin air.";
            bool saleRecorded = false;
            foreach (LumberYardSaleRecord recorded in yard.SalesHistory)
            {
                if (ReferenceEquals(recorded, sale)) { saleRecorded = true; break; }
            }
            if (!saleRecorded)
                return "LumberYardRetailDelivery: the yard never recorded this sale — unknown sale refused.";
            if (sale.IsConstructionSale)
                return "LumberYardRetailDelivery: construction sales freight through ConstructionMaterialFreightCoordinator, never through retail delivery.";
            if (request == null)
                return "LumberYardRetailDelivery: no delivery request — destination, miles and buyer are required.";
            if (!request.SaleId.IsValid || !request.SaleId.Equals(sale.SaleId))
                return "LumberYardRetailDelivery: the request names a different sale than the record — refusing.";
            if (string.IsNullOrWhiteSpace(request.DestinationLabel))
                return "LumberYardRetailDelivery: the destination must be named — anonymous deliveries refused.";
            if (string.IsNullOrWhiteSpace(request.DestinationBusinessInstanceId))
                return "LumberYardRetailDelivery: the freight destination must be named — refusing.";
            if (execution == null)
                return "LumberYardRetailDelivery: no execution bindings — rate card, wagon, cash ports and crew are required.";

            int lumberUnits = sale.UnitsSoldFor(ConstructionResourceKind.Lumber);
            if (lumberUnits <= 0)
                return $"LumberYardRetailDelivery: sale '{sale.SaleId}' sold no lumber — nothing to deliver.";

            // Buyer-haulage: the yard performs no delivery and charges no
            // delivery fee. The obligation is recorded (the buyer takes the
            // lumber at the yard); pickup and completion are the buyer's act.
            if (!request.YardDelivers)
            {
                string haulageRefusal = yard.ScheduleDeliveryForSale(sale, request.DestinationLabel, request.Miles,
                    false, string.Empty, request.BuyerProvidesUnloading,
                    new LumberYardDeliveryPolicy(), dayIndex, diag);
                if (haulageRefusal != null)
                    return "LumberYardRetailDelivery: buyer-haulage obligation refused — " + haulageRefusal;
                diag.Add($"LumberYardRetailDelivery: sale '{sale.SaleId}' recorded for buyer haulage to "
                    + $"'{request.DestinationLabel}' — no yard freight, no delivery charge; the buyer collects.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(request.CarrierLabel))
                return "LumberYardRetailDelivery: the yard undertakes delivery — name the carrier (own teamster crew or a named freight business).";
            if (request.Miles <= 0)
                return "LumberYardRetailDelivery: a yard delivery needs a positive distance — the DEF-016 quote refuses zero miles.";
            if (execution.RateCard == null)
                return "LumberYardRetailDelivery: no freight rate card — the delivery charge is quoted from the freight business's configured card, never invented.";
            if (execution.Wagon == null)
                return "LumberYardRetailDelivery: no wagon — refusing phantom transport.";
            if (!execution.Wagon.IsWagon)
                return $"LumberYardRetailDelivery: asset '{execution.Wagon.AssetId}' is not a wagon (kind '{execution.Wagon.Kind}') — refusing.";
            if (execution.Wagon.PayloadMassCapacityKg <= 0f || execution.Wagon.CargoVolumeCapacityM3 <= 0f)
                return $"LumberYardRetailDelivery: wagon '{execution.Wagon.AssetId}' has no payload capacity ({execution.Wagon.PayloadMassCapacityKg} kg / {execution.Wagon.CargoVolumeCapacityM3} m3) — refusing.";
            if (execution.BuyerCash == null)
                return "LumberYardRetailDelivery: no buyer cash port — the delivery charge is settled from real cash, never on credit here.";
            if (execution.CarrierCash == null)
                return "LumberYardRetailDelivery: no carrier cash port — the delivery charge is credited to a real carrier, never dropped.";
            if (request.ReceiveShipment == null)
                return "LumberYardRetailDelivery: no receiving authority wired — a shipment with no receiver is held Delayed, never falsely marked delivered.";
            if (execution.Tasks == null || execution.Budgets == null)
                return "LumberYardRetailDelivery: no task authority or work-time budgets — the freight chain runs on real person time or not at all.";
            if (!execution.LoaderId.IsValid || !execution.DriverId.IsValid || !execution.UnloaderId.IsValid)
                return "LumberYardRetailDelivery: the freight crew is incomplete (loader, driver and unloader are all required) — refusing.";

            // Quote first: the charge is the DEF-016 quote, and the policy
            // passed to the register is built from it.
            if (!TryQuoteDeliveryCharge(lumberUnits, request.Miles, execution.RateCard,
                    execution.Wagon, request.ConditionMultiplier, out FreightServiceQuote quote, out string quoteRefusal))
                return "LumberYardRetailDelivery: no freight quote — " + quoteRefusal;

            // One delivery order per sale: reuse the recorded order on a
            // retry, never schedule a second one.
            LumberYardDeliveryOrder order = FindOrderForSale(sale);
            if (order != null && order.IsDelivered)
            {
                diag.Add($"LumberYardRetailDelivery: sale '{sale.SaleId}' is already delivered (order {order.OrderId}) — nothing to do.");
                return null;
            }
            if (order != null && order.ChargeCents != quote.TotalPriceCents)
                return $"LumberYardRetailDelivery: sale '{sale.SaleId}' already has a delivery order charging {order.ChargeCents}c, but the fresh quote is {quote.TotalPriceCents}c — the agreed charge changed; reconcile, do not re-quote silently.";
            if (order == null)
            {
                string scheduleRefusal = yard.ScheduleDeliveryForSale(sale, request.DestinationLabel, request.Miles,
                    true, request.CarrierLabel, request.BuyerProvidesUnloading,
                    LumberYardDeliveryPolicy.FromQuotedCharge(quote.TotalPriceCents), dayIndex, diag);
                if (scheduleRefusal != null)
                    return "LumberYardRetailDelivery: delivery obligation refused — " + scheduleRefusal;
                order = FindOrderForSale(sale);
                if (order == null)
                    return $"LumberYardRetailDelivery: the delivery obligation for sale '{sale.SaleId}' was scheduled but is not in the yard's register — refusing.";
            }

            // Affordability pre-check on the whole charge, before any cash
            // moves or any freight is created: a buyer who cannot pay the
            // delivery charge waits loudly, and the lumber stays at the yard.
            if (execution.BuyerCash.BalanceCents < quote.TotalPriceCents)
                return $"LumberYardRetailDelivery: the buyer holds {execution.BuyerCash.BalanceCents}c but owes {quote.TotalPriceCents}c for delivery of sale '{sale.SaleId}' — the lumber stays at the yard until the buyer can pay.";

            int trips = quote.TripCount;
            int perTripUnits = FreightCargoCapacity.MaxUnitsPerTrip(
                LumberCargoSpec.Spec, execution.Wagon.PayloadMassCapacityKg, execution.Wagon.CargoVolumeCapacityM3);
            if (trips <= 0 || perTripUnits <= 0)
                return "LumberYardRetailDelivery: the quoted load needs no trips, or the wagon cannot carry even one lumber unit — refusing.";

            freight.RegisterWagon(execution.Wagon);
            SeedSequenceFromService(freight);

            int deliveredUnits = 0;
            for (int trip = 0; trip < trips; trip++)
            {
                int tripUnits = Math.Min(perTripUnits, lumberUnits - deliveredUnits);
                if (tripUnits <= 0)
                    return $"LumberYardRetailDelivery: trip {trip + 1} of {trips} for sale '{sale.SaleId}' has no units left — refusing; the load split is inconsistent.";

                string tripRefusal = RunTrip(sale, request, execution, order, trip, tripUnits, quote, dayIndex, diag);
                if (tripRefusal != null)
                    return tripRefusal;
                deliveredUnits += tripUnits;
            }

            if (deliveredUnits != lumberUnits)
                return $"LumberYardRetailDelivery: sale '{sale.SaleId}' delivered {deliveredUnits}u of {lumberUnits}u — refusing; the load is incomplete.";

            string completeRefusal = yard.MarkDeliveryComplete(order.OrderId, dayIndex, diag);
            if (completeRefusal != null)
                return "LumberYardRetailDelivery: delivery completed but could not be marked delivered — " + completeRefusal;
            diag.Add($"LumberYardRetailDelivery: sale '{sale.SaleId}' delivered — {deliveredUnits}u to "
                + $"'{request.DestinationLabel}' in {trips} trip(s), delivery charge {quote.TotalPriceCents}c settled (order {order.OrderId}).");
            return null;
        }

        private LumberYardDeliveryOrder FindOrderForSale(LumberYardSaleRecord sale)
        {
            if (sale == null || yard == null || yard.DeliveryRegister == null) return null;
            foreach (LumberYardDeliveryOrder order in yard.DeliveryRegister.Orders)
            {
                if (order != null && order.SaleId.Equals(sale.SaleId)) return order;
            }
            return null;
        }

        /// <summary>
        /// QA-20: the service holds no save slot and is rebuilt per drive,
        /// so its sequence restarts at 0. Seed it from the freight
        /// service's own tracked shipments before minting, so a new id never
        /// collides with one the service already holds. Only the ydship-
        /// prefix is ours; other shipment ids are ignored. Never moves the
        /// sequence backward.
        /// </summary>
        private void SeedSequenceFromService(FreightShipmentService service)
        {
            if (service == null) return;
            foreach (LogisticsShipmentState existing in service.TrackedShipments)
            {
                if (existing == null) continue;
                string id = existing.ShipmentId;
                if (string.IsNullOrEmpty(id)
                    || !id.StartsWith("ydship-", StringComparison.Ordinal)) continue;
                if (int.TryParse(id.Substring("ydship-".Length), out int n) && n >= sequence)
                    sequence = n + 1;
            }
        }

        /// <summary>
        /// One trip of a retail delivery: pay the trip's fare, create the
        /// shipment, run the freight chain, verify conservation. The fare
        /// is paid BEFORE the trip runs (the construction coordinator's
        /// pattern, so a payment refusal leaves no shipment behind); a
        /// credit refusal refunds the buyer that trip's fare in full and
        /// the trip fails with nothing loaded. A trip whose shipment is
        /// already Completed is skipped; a trip whose shipment exists but
        /// carries cargo refuses loudly — the goods are accounted for in
        /// that shipment. A Planned or pre-load Failed shipment owns zero
        /// cargo: it is abandoned through FailAbandonedShipment (which
        /// releases the wagon hold — a Planned shipment is still live for
        /// exclusivity) and replaced with a fresh shipment id. A trip that
        /// fails before departing refunds its fare in full; a trip that
        /// fails after departing keeps its fare standing for the attempt.
        /// </summary>
        private string RunTrip(
            LumberYardSaleRecord sale,
            LumberYardRetailDeliveryRequest request,
            LumberYardRetailDeliveryExecution execution,
            LumberYardDeliveryOrder order,
            int tripIndex,
            int tripUnits,
            FreightServiceQuote quote,
            int dayIndex,
            List<string> diag)
        {
            string tripLabel = $"trip {tripIndex + 1} of {quote.TripCount} for sale '{sale.SaleId}'";
            int perTripCharge = quote.PerTripTotalCents;
            var hooks = new FreightShipmentHooks
            {
                Tasks = execution.Tasks,
                Budgets = execution.Budgets,
                Schedules = execution.Schedules,
                DayIndex = dayIndex,
                Diagnostics = diag,
                ReceiveShipment = request.ReceiveShipment,
            };

            string existingShipmentId = TripShipmentId(order, tripIndex);
            if (!string.IsNullOrEmpty(existingShipmentId))
            {
                LogisticsShipmentState existing = FindShipment(existingShipmentId);
                if (existing == null)
                    return $"LumberYardRetailDelivery: {tripLabel} names shipment '{existingShipmentId}' on the delivery order, but the freight service no longer tracks it — reconcile, do not re-ship.";
                if (existing.Status == LogisticsShipmentStatus.Completed)
                {
                    if (TripFarePaid(order, tripIndex) < 0)
                        return $"LumberYardRetailDelivery: {tripLabel} shipment '{existingShipmentId}' is Completed but its fare is unrecorded — the settlement record is inconsistent; reconcile, do not re-ship.";
                    diag.Add($"LumberYardRetailDelivery: {tripLabel} already delivered (shipment '{existingShipmentId}') — skipped.");
                    return null;
                }
                if (existing.Status == LogisticsShipmentStatus.Planned || existing.IsPreLoadBlocked)
                {
                    diag.Add($"LumberYardRetailDelivery: {tripLabel} shipment '{existingShipmentId}' is {existing.Status} with zero cargo — abandoning it for a fresh shipment.");
                    freight.FailAbandonedShipment(existing,
                        $"abandoned by yard retail delivery retry — {tripLabel} never departed", hooks);
                }
                else
                {
                    return $"LumberYardRetailDelivery: {tripLabel} shipment '{existingShipmentId}' is {existing.Status} — the goods are accounted for in that shipment; reconcile, do not re-ship.";
                }
            }

            // Pay this trip's fare before the trip runs. No shipment exists
            // yet, so a payment refusal leaves nothing behind to reconcile.
            string debitRefusal = execution.BuyerCash.TryDebit(perTripCharge,
                $"yard retail delivery {tripLabel} (sale '{sale.SaleId}')");
            if (debitRefusal != null)
                return $"LumberYardRetailDelivery: {tripLabel} delivery fare unpaid — {debitRefusal} ({TripFarePaidTotal(order)}c of {quote.TotalPriceCents}c already settled for this delivery).";
            string creditRefusal = execution.CarrierCash.Credit(perTripCharge,
                $"yard retail delivery {tripLabel} (sale '{sale.SaleId}')");
            if (creditRefusal != null)
            {
                string refundRefusal = execution.BuyerCash.Credit(perTripCharge,
                    $"yard retail delivery {tripLabel} refund (sale '{sale.SaleId}')");
                return $"LumberYardRetailDelivery: {tripLabel} carrier credit refused — {creditRefusal}. "
                    + (refundRefusal == null
                        ? "The buyer was refunded the trip fare in full."
                        : $"The buyer refund ALSO failed — {refundRefusal}; reconcile before retrying.");
            }
            SetTripFarePaid(order, tripIndex, perTripCharge);

            string shipmentId = $"ydship-{sequence++}";
            LogisticsShipmentState shipment = freight.CreateShipment(
                shipmentId, yard.BusinessInstanceId, request.DestinationBusinessInstanceId,
                tripUnits, LumberCargoSpec.Spec, execution.Wagon.AssetId, hooks, out string createReason);
            if (shipment == null)
                return RefundAndFail(order, tripIndex, perTripCharge,
                    $"LumberYardRetailDelivery: {tripLabel} shipment not created — {createReason}", execution, sale, tripLabel);
            if (shipment.Status == LogisticsShipmentStatus.Failed)
                return RefundAndFail(order, tripIndex, perTripCharge,
                    $"LumberYardRetailDelivery: {tripLabel} shipment '{shipmentId}' blocked pre-load ({shipment.BlockedReason}) — it owns zero cargo.",
                    execution, sale, tripLabel);
            SetTripShipmentId(order, tripIndex, shipmentId);

            string chainRefusal = RunShipmentChain(shipment, request, execution, dayIndex, diag);
            if (chainRefusal != null)
                return FailTripDisposition(order, tripIndex, perTripCharge, shipment,
                    $"LumberYardRetailDelivery: {tripLabel} failed in transit — {chainRefusal}",
                    execution, sale, tripLabel, hooks, diag);

            string verifyReason;
            if (!freight.VerifyConservation(shipment, tripUnits, out verifyReason))
                return FailTripDisposition(order, tripIndex, perTripCharge, shipment,
                    $"LumberYardRetailDelivery: conservation FAILED for '{shipmentId}' ({tripLabel}) — {verifyReason}. Goods do not reach the buyer.",
                    execution, sale, tripLabel, hooks, diag);
            diag.Add($"LumberYardRetailDelivery: {tripLabel} arrived at '{request.DestinationLabel}' ({tripUnits}u).");
            return null;
        }

        /// <summary>
        /// A trip that never departed (no shipment, or a shipment blocked
        /// pre-load): refund the fare in full and clear the fare record, so
        /// a retry pays exactly once. A refund failure is named loudly —
        /// the money is then in dispute, not silently kept.
        /// </summary>
        private string RefundAndFail(
            LumberYardDeliveryOrder order,
            int tripIndex,
            int perTripCharge,
            string failure,
            LumberYardRetailDeliveryExecution execution,
            LumberYardSaleRecord sale,
            string tripLabel)
        {
            string refundRefusal = execution.BuyerCash.Credit(perTripCharge,
                $"yard retail delivery {tripLabel} refund (sale '{sale.SaleId}')");
            if (refundRefusal == null)
            {
                SetTripFarePaid(order, tripIndex, -1);
                return failure + " The buyer was refunded the trip fare in full; the trip never departed.";
            }
            return failure + $" The buyer refund ALSO failed — {refundRefusal}; reconcile before retrying.";
        }

        /// <summary>
        /// A trip that failed after its shipment was created: abandon the
        /// shipment (releasing the wagon hold; goods stay quarantined on
        /// the failed shipment) and refund the fare only when the shipment
        /// never departed. A departed trip's fare stands for the attempt.
        /// </summary>
        private string FailTripDisposition(
            LumberYardDeliveryOrder order,
            int tripIndex,
            int perTripCharge,
            LogisticsShipmentState shipment,
            string failure,
            LumberYardRetailDeliveryExecution execution,
            LumberYardSaleRecord sale,
            string tripLabel,
            FreightShipmentHooks hooks,
            List<string> diag)
        {
            bool neverDeparted = shipment == null
                || shipment.Status == LogisticsShipmentStatus.Planned
                || shipment.Status == LogisticsShipmentStatus.Loading
                || shipment.Status == LogisticsShipmentStatus.Failed;
            string refundNote = string.Empty;
            if (neverDeparted && perTripCharge > 0)
            {
                string refundRefusal = execution.BuyerCash.Credit(perTripCharge,
                    $"yard retail delivery {tripLabel} refund (sale '{sale.SaleId}')");
                if (refundRefusal == null)
                {
                    SetTripFarePaid(order, tripIndex, -1);
                    refundNote = $" Freight refunded ({perTripCharge}c returned to the buyer).";
                }
                else
                {
                    refundNote = $" Freight refund FAILED — {refundRefusal} (the carrier keeps {perTripCharge}c; in dispute).";
                }
            }
            if (shipment != null)
            {
                freight.FailAbandonedShipment(shipment, failure, hooks);
                refundNote += shipment.loadApplied
                    ? $" {shipment.RemainingQuantityUnits}u quarantined on the failed shipment."
                    : " Goods stay with the yard.";
            }
            return failure + refundNote
                + $" ({TripFarePaidTotal(order)}c of {order.ChargeCents}c settled for this delivery; a departed trip's fare stands for the attempt).";
        }

        private string TripShipmentId(LumberYardDeliveryOrder order, int tripIndex)
        {
            if (order == null || order.ShipmentIds == null || tripIndex < 0 || tripIndex >= order.ShipmentIds.Count)
                return null;
            return order.ShipmentIds[tripIndex];
        }

        private void SetTripShipmentId(LumberYardDeliveryOrder order, int tripIndex, string shipmentId)
        {
            if (order == null) return;
            order.ShipmentIds = order.ShipmentIds ?? new List<string>();
            while (order.ShipmentIds.Count <= tripIndex) order.ShipmentIds.Add(string.Empty);
            order.ShipmentIds[tripIndex] = shipmentId ?? string.Empty;
        }

        private int TripFarePaid(LumberYardDeliveryOrder order, int tripIndex)
        {
            if (order == null || order.TripFaresPaidCents == null || tripIndex < 0 || tripIndex >= order.TripFaresPaidCents.Count)
                return -1;
            return order.TripFaresPaidCents[tripIndex];
        }

        private void SetTripFarePaid(LumberYardDeliveryOrder order, int tripIndex, int fareCents)
        {
            if (order == null) return;
            order.TripFaresPaidCents = order.TripFaresPaidCents ?? new List<int>();
            while (order.TripFaresPaidCents.Count <= tripIndex) order.TripFaresPaidCents.Add(-1);
            order.TripFaresPaidCents[tripIndex] = fareCents;
        }

        private int TripFarePaidTotal(LumberYardDeliveryOrder order)
        {
            if (order == null || order.TripFaresPaidCents == null) return 0;
            int total = 0;
            foreach (int fare in order.TripFaresPaidCents)
            {
                if (fare > 0) total += fare;
            }
            return total;
        }

        private LogisticsShipmentState FindShipment(string shipmentId)
        {
            if (freight == null || string.IsNullOrWhiteSpace(shipmentId)) return null;
            foreach (LogisticsShipmentState shipment in freight.TrackedShipments)
            {
                if (shipment != null && string.Equals(shipment.ShipmentId, shipmentId, StringComparison.Ordinal))
                    return shipment;
            }
            return null;
        }

        /// <summary>
        /// The construction coordinator's chain, verbatim in shape:
        /// load, dispatch, transit, unload, each step loud on refusal.
        /// The receiver accepts the units during unloading through the
        /// caller-supplied ReceiveShipment hook.
        /// </summary>
        private string RunShipmentChain(
            LogisticsShipmentState shipment,
            LumberYardRetailDeliveryRequest request,
            LumberYardRetailDeliveryExecution execution,
            int dayIndex,
            List<string> diag)
        {
            var hooks = new FreightShipmentHooks
            {
                Tasks = execution.Tasks,
                Budgets = execution.Budgets,
                Schedules = execution.Schedules,
                DayIndex = dayIndex,
                Diagnostics = diag,
                // The receiver lives on the request: the buyer-side
                // authority accepts the units during unloading.
                ReceiveShipment = request.ReceiveShipment,
            };
            string reason;
            if (!freight.BeginLoading(shipment, execution.LoaderId, hooks, out reason))
                return "loading refused — " + reason;
            int guard = 0;
            while (!shipment.loadApplied && guard++ < 100)
            {
                if (!freight.WorkLoading(shipment, 60, hooks, out reason))
                    return "loading failed — " + reason;
            }
            if (!shipment.loadApplied)
                return $"loading never completed for '{shipment.ShipmentId}' — refusing.";

            if (!freight.Dispatch(shipment, execution.DriverId, execution.DraftAnimalIds, hooks, out reason))
                return "dispatch refused — " + reason;
            freight.AdvanceTransit(shipment, Math.Max(1f, request.TransitGameSeconds), hooks);
            if (shipment.Status != LogisticsShipmentStatus.Arrived)
                return $"shipment '{shipment.ShipmentId}' is {shipment.Status} after transit — not arrived, refusing.";

            if (!freight.BeginUnloading(shipment, execution.UnloaderId, hooks, out reason))
                return "unloading refused — " + reason;
            guard = 0;
            while (shipment.Status != LogisticsShipmentStatus.Completed && guard++ < 100)
            {
                if (!freight.WorkUnloading(shipment, 60, hooks, out reason))
                    return "unloading failed — " + reason;
            }
            if (shipment.Status != LogisticsShipmentStatus.Completed)
                return $"unloading never completed for '{shipment.ShipmentId}' — refusing.";
            return null;
        }
    }
}
