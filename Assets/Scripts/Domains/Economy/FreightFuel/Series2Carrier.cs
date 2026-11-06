// Series 2 Pass 8 (freight + fuel chain): the carrier contract that
// Pass 6 FreightBusiness quotes and Pass 3 FreightBusiness orders
// can call, the shipment-request bridge over the detailed freight
// driver, slice leg definitions, and delivery-acceptance settlement.
//
// HONESTY, stated first: a carrier shipment moves SHIPMENT STATE,
// not item stock. The detailed service (FreightShipmentService /
// FreightProductionDriver) tracks load -> transit -> unload ->
// acceptance, and the recipient takes custody only through the
// caller-wired ReceiveShipment callback in FreightShipmentHooks.
// A store-to-bakery leg therefore does NOT by itself put flour in
// the bakery, and a bakery-to-store wholesale leg does NOT by
// itself put bread in BACK storage: Codex wires those callbacks to
// the Pass 6 feed, the Pass 3 wholesale intake (BACK only), the
// Pass 8 dealer supply, and the household reserve link. Any test
// here that "accepts" a leg records acceptance through a callback,
// exactly as the production driver tests do — it never claims a
// stock move the callback did not perform.
//
// CARRIER-LESS FREIGHTBUSINESS, the Pass 6 gap this pass closes:
// Pass 6's quote builder accepted FreightBusiness quotes whose
// freight cents behaved exactly like SellerFreight, because no
// carrier existed to commission. From this pass, a FreightBusiness
// quote/order with NO registered carrier is REFUSED by the resolvers
// below — it must not keep behaving silently like SellerFreight.
// With a registered carrier it becomes a real leg request, charged
// at the quote/request freight figure the caller already priced
// (commission included in that figure where the caller priced it);
// this pass never recomputes a freight rate and never invents one.
//
// SURVIVING SHIPMENT SET (slice decision, documented, not a
// deletion): DETAILED shipments — FreightShipmentService +
// FreightProductionDriver — survive for the slice. The timer-driven
// LogisticsRuntimeManager (MonoBehaviour, category-stock bulk path)
// is the NON-SLICE path: it is not edited, not deleted, and must
// not be run on a slice leg. Cross-system double-booking is NOT
// prevented by either system (exclusivity is enforced within the
// detailed service only), so slice discipline — detailed only for
// slice legs — is a Codex wiring rule, stated here, not a guard
// this pass can enforce from inside one system.
//
// TICK REQUIREMENT, stated explicitly: FreightProductionDriver.Tick
// (AdvanceTick) requires hooks.Tasks (TaskAuthority) AND
// hooks.Budgets (WorkTimeBudgetStore). This bridge checks that
// requirement BEFORE advancing and returns 0 with a diagnostic
// when it is not met — shipments simply do not advance, rather
// than advancing on a timer with no handler, no budget, and no
// person-time. A valid handler id is additionally required for
// loading/unloading steps (the driver diagnoses waiting shipments
// loudly when the handler is invalid; in-transit shipments still
// advance and revenue still collects in that case, per the driver).
//
// SETTLEMENT: freight revenue settles ONLY on full delivery
// acceptance (shipment Completed AND deliveryApplied). Delayed and
// Partial shipments keep their cargo in wagon custody and settle
// NOTHING. Settlement here is atomic across the two business cash
// ports (payer debit, carrier credit, refund on credit failure),
// exactly like the Pass 7 wholesale settlement. It must never be
// run on a shipment the production driver's own collection already
// settled: this settlement object tracks settled ids per instance
// and refuses a second settlement, and Codex must choose ONE
// settlement path per leg (driver collection on the tick, or this
// callable) — but the cross-path guard is the shipment's own
// freightRevenueCollected flag (see Series2CarrierSettlement):
// this settlement refuses a flag-set shipment and sets the flag
// when it settles, so a later driver tick cannot collect twice.
// Codex should still choose ONE settlement path per leg as the
// normal route; save adoption of the per-instance settled set is
// Pass 9 work, stated, not claimed.
using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Ordering;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.FreightFuel
{
    /// <summary>One caller-parameterised carrier leg. No fixture, person, horse, or wagon is invented here.</summary>
    public sealed class Series2CarrierLeg
    {
        public string ShipmentId = string.Empty;
        public string CarrierBusinessId = string.Empty;
        public string SourceBusinessId = string.Empty;
        public string DestinationBusinessId = string.Empty;
        /// <summary>Household id for a dealer-to-household leg, or -1 for a business destination.</summary>
        public int DestinationHouseholdId = -1;
        /// <summary>Generic cargo kind, e.g. "flour", "fuel", "bread-wholesale", "fuel-household". Never an item id claim.</summary>
        public string CargoKind = string.Empty;
        public int QuantityUnits;
        public int FreightChargeCents;
        public string FreightPayerBusinessId = string.Empty;
        public string WagonAssetId = string.Empty;
        public float CargoMassKgPerUnit = 1f;
        public float CargoVolumeM3PerUnit = 0.01f;
        public EntityId DriverId = EntityId.Invalid;
        public List<EntityId> AnimalIds = new List<EntityId>();
    }

    /// <summary>Result of a carrier leg request: the shipment, or a refusal with no shipment created.</summary>
    public sealed class Series2CarrierLegResult
    {
        public LogisticsShipmentState Shipment;
        public string Refusal = string.Empty;
    }

    /// <summary>Result of a FreightBusiness quote/order resolution: a leg request, or a refusal, or "not a carrier quote".</summary>
    public sealed class Series2CarrierQuoteResult
    {
        public Series2CarrierLeg Leg;
        /// <summary>Non-empty when the quote/order names FreightBusiness but no carrier can take it. Null/empty + null Leg means "not a FreightBusiness quote at all".</summary>
        public string Refusal = string.Empty;
        public bool IsFreightBusiness;
    }

    /// <summary>Series 2 Pass 8: the carrier contract.</summary>
    public interface ISeries2Carrier
    {
        string CarrierBusinessId { get; }
        Series2CarrierLegResult RequestLeg(Series2CarrierLeg leg, FreightShipmentHooks hooks, List<string> diag);
        int AdvanceTick(float gameSeconds, FreightShipmentHooks hooks, EntityId handlerId, List<string> diag);
    }

    /// <summary>Series 2 Pass 8: the surviving shipment set for the slice, documented.</summary>
    public static class Series2FreightSurvival
    {
        public const string SurvivingSet = "Detailed: FreightShipmentService + FreightProductionDriver (task-based, handler-gated, acceptance-gated).";
        public const string NonSlicePath = "Timer-driven: LogisticsRuntimeManager (MonoBehaviour, category-stock bulk path) — not edited, not deleted, never run on a slice leg.";
        public const string CrossSystemGap = "Cross-system double-booking is NOT prevented by either system; slice legs go through the detailed driver only, as a Codex wiring rule.";

        /// <summary>Returns null when the driver tick requirement is met, or the reason shipments must not advance.</summary>
        public static string CheckTickRequirements(FreightShipmentHooks hooks)
        {
            if (hooks == null) return "Series2FreightSurvival: no shipment hooks supplied — shipments must not advance.";
            if (hooks.Tasks == null || hooks.Budgets == null)
                return "Series2FreightSurvival: driver tick needs TaskAuthority + WorkTimeBudgetStore in hooks — shipments not advanced, never advanced on a timer alone.";
            return null;
        }
    }

    /// <summary>Series 2 Pass 8: FreightBusiness quote/order resolution — re-route to a carrier, or refuse, never silently SellerFreight.</summary>
    public static class Series2CarrierQuoteResolver
    {
        /// <summary>
        /// Resolves a Pass 6 legacy OffMapFreightQuote. Non-FreightBusiness
        /// quotes return IsFreightBusiness=false with no leg and no
        /// refusal (the seller-freight path is unchanged and untouched).
        /// A FreightBusiness quote with no registered carrier is
        /// REFUSED. With a carrier, the leg charges exactly the quote's
        /// freight cents (commission already priced in by the caller).
        /// </summary>
        public static Series2CarrierQuoteResult ResolveOffMapQuote(
            OffMapFreightQuote quote, string carrierBusinessId, bool carrierRegistered,
            string destinationBusinessId, string shipmentId, string wagonAssetId, List<string> diag)
        {
            var result = new Series2CarrierQuoteResult();
            diag = diag ?? new List<string>();
            if (quote == null || quote.FreightOption != FreightOptionKind.FreightBusiness)
                return result;
            result.IsFreightBusiness = true;
            if (!carrierRegistered || string.IsNullOrWhiteSpace(carrierBusinessId))
            {
                result.Refusal = "Series2CarrierQuoteResolver: FreightBusiness quote refused — no freight carrier is registered for the slice; " +
                    "it must not fall back to behaving like SellerFreight. Register a carrier, or re-quote as SellerFreight deliberately.";
                diag.Add(result.Refusal);
                return result;
            }
            result.Leg = new Series2CarrierLeg
            {
                ShipmentId = shipmentId ?? string.Empty,
                CarrierBusinessId = carrierBusinessId,
                SourceBusinessId = quote.SupplierId ?? string.Empty,
                DestinationBusinessId = destinationBusinessId ?? string.Empty,
                CargoKind = quote.SkuId ?? string.Empty,
                QuantityUnits = quote.QuantityUnits,
                FreightChargeCents = Math.Max(0, quote.FreightCents),
                FreightPayerBusinessId = destinationBusinessId ?? string.Empty,
                WagonAssetId = wagonAssetId ?? string.Empty,
            };
            diag.Add($"Series2CarrierQuoteResolver: FreightBusiness quote re-routed to carrier '{carrierBusinessId}' as leg '{result.Leg.ShipmentId}' ({result.Leg.QuantityUnits}u, freight {result.Leg.FreightChargeCents}c).");
            return result;
        }

        /// <summary>
        /// Resolves a Pass 3 placed order request. Same rules as the
        /// legacy quote: non-FreightBusiness requests are not carrier
        /// legs; a FreightBusiness request with no carrier is refused;
        /// with a carrier it becomes a leg charged at the request's
        /// allocated freight (which already includes any commission
        /// the draft priced). SelfCollect requests are never carrier
        /// legs (they are AwaitingCollection by definition).
        /// </summary>
        public static Series2CarrierQuoteResult ResolvePass3Request(
            Pass3OrderRequest request, string carrierBusinessId, bool carrierRegistered,
            string destinationBusinessId, string shipmentId, string wagonAssetId, List<string> diag)
        {
            var result = new Series2CarrierQuoteResult();
            diag = diag ?? new List<string>();
            if (request == null || request.FreightOption != Pass3FreightOption.FreightBusiness)
                return result;
            result.IsFreightBusiness = true;
            if (!carrierRegistered || string.IsNullOrWhiteSpace(carrierBusinessId))
            {
                result.Refusal = "Series2CarrierQuoteResolver: FreightBusiness order request refused — no freight carrier is registered for the slice; " +
                    "it must not fall back to behaving like SellerFreight. Register a carrier, or place as SellerFreight deliberately.";
                diag.Add(result.Refusal);
                return result;
            }
            result.Leg = new Series2CarrierLeg
            {
                ShipmentId = shipmentId ?? string.Empty,
                CarrierBusinessId = carrierBusinessId,
                SourceBusinessId = request.SupplierId ?? string.Empty,
                DestinationBusinessId = destinationBusinessId ?? string.Empty,
                CargoKind = request.SkuId ?? string.Empty,
                QuantityUnits = request.QuantityUnits,
                FreightChargeCents = Math.Max(0, request.FreightAllocatedCents),
                FreightPayerBusinessId = destinationBusinessId ?? string.Empty,
                WagonAssetId = wagonAssetId ?? string.Empty,
            };
            diag.Add($"Series2CarrierQuoteResolver: FreightBusiness order request re-routed to carrier '{carrierBusinessId}' as leg '{result.Leg.ShipmentId}' ({result.Leg.QuantityUnits}u, freight {result.Leg.FreightChargeCents}c).");
            return result;
        }
    }

    /// <summary>
    /// Series 2 Pass 8: slice leg definitions. Every field is caller-
    /// supplied (generic business/route inputs, NOT named Ethan
    /// shipments — fixture inputs are blocked on Kennedy inputs and
    /// are never invented here). Stock movement is the receiving
    /// callback's job (see the file header); a leg definition alone
    /// moves no stock, and a wholesale leg's callback must intake
    /// into BACK storage only (Pass 3 rule).
    /// </summary>
    public static class Series2SliceLegs
    {
        private static Series2CarrierLeg Base(
            string shipmentId, string carrierId, string source, string destination,
            string cargoKind, int quantity, int chargeCents, string payer, string wagon)
        {
            return new Series2CarrierLeg
            {
                ShipmentId = shipmentId ?? string.Empty,
                CarrierBusinessId = carrierId ?? string.Empty,
                SourceBusinessId = source ?? string.Empty,
                DestinationBusinessId = destination ?? string.Empty,
                CargoKind = cargoKind ?? string.Empty,
                QuantityUnits = quantity,
                FreightChargeCents = Math.Max(0, chargeCents),
                FreightPayerBusinessId = payer ?? string.Empty,
                WagonAssetId = wagon ?? string.Empty,
            };
        }

        /// <summary>Off-map arrival into a store or bakery. Receiving callback: the Pass 6 procurement arrival.</summary>
        public static Series2CarrierLeg OffMapArrivalToBusiness(
            string shipmentId, string carrierId, string supplierId, string destinationBusinessId,
            string skuId, int quantity, int chargeCents, string wagon)
        {
            return Base(shipmentId, carrierId, supplierId, destinationBusinessId, skuId, quantity, chargeCents, destinationBusinessId, wagon);
        }

        /// <summary>Store to bakery. Receiving callback: the Pass 6 store-to-bakery feed (with its flour-unit conversion parameter).</summary>
        public static Series2CarrierLeg StoreToBakery(
            string shipmentId, string carrierId, string storeBusinessId, string bakeryBusinessId,
            int quantity, int chargeCents, string wagon)
        {
            return Base(shipmentId, carrierId, storeBusinessId, bakeryBusinessId, "flour", quantity, chargeCents, bakeryBusinessId, wagon);
        }

        /// <summary>Bakery to store wholesale. Receiving callback: the Pass 3 wholesale intake into BACK storage only, then Pass 7 settlement + shelf move.</summary>
        public static Series2CarrierLeg BakeryToStoreWholesale(
            string shipmentId, string carrierId, string bakeryBusinessId, string storeBusinessId,
            int quantity, int chargeCents, string wagon)
        {
            return Base(shipmentId, carrierId, bakeryBusinessId, storeBusinessId, "bread-wholesale", quantity, chargeCents, storeBusinessId, wagon);
        }

        /// <summary>Fuel dealer to bakery. Receiving callback: the Pass 8 dealer supply into the bakery fuel store.</summary>
        public static Series2CarrierLeg DealerToBakery(
            string shipmentId, string carrierId, string dealerBusinessId, string bakeryBusinessId,
            int quantity, int chargeCents, string wagon)
        {
            return Base(shipmentId, carrierId, dealerBusinessId, bakeryBusinessId, "fuel", quantity, chargeCents, bakeryBusinessId, wagon);
        }

        /// <summary>Fuel dealer to household. Receiving callback: the Pass 8 household reserve link (bundle-to-reserve conversion as a caller parameter).</summary>
        public static Series2CarrierLeg DealerToHousehold(
            string shipmentId, string carrierId, string dealerBusinessId, int householdId,
            int quantity, int chargeCents, string wagon)
        {
            var leg = Base(shipmentId, carrierId, dealerBusinessId, string.Empty, "fuel-household", quantity, chargeCents, dealerBusinessId, wagon);
            leg.DestinationHouseholdId = householdId;
            return leg;
        }
    }

    /// <summary>Series 2 Pass 8: the detailed-driver carrier bridge.</summary>
    public sealed class Series2FreightProductionCarrier : ISeries2Carrier
    {
        private readonly FreightProductionDriver driver;

        public Series2FreightProductionCarrier(FreightProductionDriver driver, string carrierBusinessId)
        {
            this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
            CarrierBusinessId = carrierBusinessId ?? string.Empty;
        }

        public string CarrierBusinessId { get; }

        public FreightProductionDriver Driver => driver;

        public void RegisterWagon(EquipmentAsset wagon)
        {
            if (wagon != null) driver.Service.RegisterWagon(wagon);
        }

        public void RegisterBusinessRuntime(string businessInstanceId, BusinessRuntimeState runtime)
        {
            driver.RegisterBusinessRuntime(businessInstanceId, runtime);
        }

        public Series2CarrierLegResult RequestLeg(Series2CarrierLeg leg, FreightShipmentHooks hooks, List<string> diag)
        {
            var result = new Series2CarrierLegResult();
            diag = diag ?? new List<string>();
            if (leg == null || string.IsNullOrWhiteSpace(leg.ShipmentId) || leg.QuantityUnits <= 0)
            {
                result.Refusal = "Series2FreightProductionCarrier: a leg needs a shipment id and a positive quantity; no shipment created.";
                diag.Add(result.Refusal);
                return result;
            }
            if (!string.Equals(leg.CarrierBusinessId, CarrierBusinessId, StringComparison.Ordinal))
            {
                result.Refusal = $"Series2FreightProductionCarrier: leg '{leg.ShipmentId}' names carrier '{leg.CarrierBusinessId}', this carrier is '{CarrierBusinessId}'; refused, never silently re-carried.";
                diag.Add(result.Refusal);
                return result;
            }
            string destination = leg.DestinationHouseholdId >= 0
                ? $"household-{leg.DestinationHouseholdId}"
                : leg.DestinationBusinessId;
            LogisticsShipmentState shipment = driver.CreateShipment(
                CarrierBusinessId, leg.ShipmentId, leg.SourceBusinessId, destination,
                leg.QuantityUnits,
                new FreightCargoSpec { MassKgPerUnit = leg.CargoMassKgPerUnit, VolumeM3PerUnit = leg.CargoVolumeM3PerUnit },
                leg.WagonAssetId, hooks,
                leg.DriverId, leg.AnimalIds,
                leg.FreightChargeCents, leg.FreightPayerBusinessId,
                diag, out string reason);
            if (shipment == null)
            {
                result.Refusal = $"Series2FreightProductionCarrier: leg '{leg.ShipmentId}' refused — {reason}";
                diag.Add(result.Refusal);
                return result;
            }
            if (shipment.IsPreLoadBlocked)
            {
                result.Refusal = $"Series2FreightProductionCarrier: leg '{leg.ShipmentId}' blocked pre-load — {shipment.BlockedReason}; it owns zero cargo.";
                diag.Add(result.Refusal);
                return result;
            }
            result.Shipment = shipment;
            diag.Add($"Series2FreightProductionCarrier: leg '{leg.ShipmentId}' accepted by carrier '{CarrierBusinessId}' ({leg.QuantityUnits}u '{leg.CargoKind}', freight {leg.FreightChargeCents}c, payer '{leg.FreightPayerBusinessId}').");
            return result;
        }

        public int AdvanceTick(float gameSeconds, FreightShipmentHooks hooks, EntityId handlerId, List<string> diag)
        {
            diag = diag ?? new List<string>();
            string requirement = Series2FreightSurvival.CheckTickRequirements(hooks);
            if (requirement != null)
            {
                diag.Add(requirement);
                return 0;
            }
            int advanced = driver.AdvanceTick(gameSeconds, hooks, handlerId, diag);
            diag.Add($"Series2FreightProductionCarrier: tick advanced {advanced} shipment(s) for carrier '{CarrierBusinessId}' (detailed set only; timer-driven manager never advanced here).");
            return advanced;
        }
    }

    /// <summary>Result of a carrier settlement attempt.</summary>
    public sealed class Series2CarrierSettlementResult
    {
        public bool Settled;
        public string Refusal = string.Empty;
    }

    /// <summary>
    /// Series 2 Pass 8: freight settlement on full delivery acceptance
    /// only, atomic across business cash ports. A shipment that is
    /// not Completed with deliveryApplied (Delayed, Partial, or any
    /// earlier state) settles NOTHING and keeps its cargo in wagon
    /// custody. Business cash only: payer and carrier ports are
    /// caller-supplied IBusinessCashPort ports (production: a
    /// BusinessRuntimeState port — see the review doc on which
    /// in-tree port Codex may reuse, and that it is not yet a
    /// production-driver path). HouseholdLedger is never used here:
    /// freight charges are business-to-business cash, not household
    /// cash. Cross-path guard, both directions: this settlement
    /// refuses a shipment whose freightRevenueCollected flag is
    /// already set (driver collection already paid it), and after
    /// a successful settlement it sets that flag, so a later driver
    /// tick scan cannot collect the same charge again. The per-
    /// instance settled set is an additional guard; persisting it
    /// across saves is Pass 9 work, stated, not claimed.
    /// </summary>
    public sealed class Series2CarrierSettlement
    {
        private readonly HashSet<string> settledShipmentIds = new HashSet<string>(StringComparer.Ordinal);

        public bool AlreadySettled(string shipmentId)
        {
            return !string.IsNullOrWhiteSpace(shipmentId) && settledShipmentIds.Contains(shipmentId);
        }

        /// <summary>
        /// Series 2 Pass 9, additive only: rebuild the per-instance
        /// settled set from restored shipments, clear-then-load.
        /// DECISION (Pass 9, stated plainly): the shipment's own
        /// freightRevenueCollected flag — which IS part of the
        /// shipment save DTO — is the settlement authority across
        /// saves, not this in-memory set. A second persisted set
        /// could disagree with the flag, and then no reader would
        /// know which one to trust, so no second set is persisted
        /// at all. Restore rebuilds this set from the flags so the
        /// in-process guard matches the persisted truth, and
        /// SettleOnAcceptance still refuses a flagged shipment
        /// even if this set were empty — a restored settlement
        /// can therefore never double-collect. Restoring twice
        /// yields the same set (idempotent).
        /// </summary>
        public void RestoreFromShipments(IEnumerable<LogisticsShipmentState> shipments)
        {
            settledShipmentIds.Clear();
            if (shipments == null) return;
            foreach (LogisticsShipmentState shipment in shipments)
            {
                if (shipment == null || string.IsNullOrWhiteSpace(shipment.ShipmentId)) continue;
                if (shipment.FreightRevenueCollected) settledShipmentIds.Add(shipment.ShipmentId);
            }
        }

        public Series2CarrierSettlementResult SettleOnAcceptance(
            LogisticsShipmentState shipment, IBusinessCashPort payer, IBusinessCashPort carrier, List<string> diag)
        {
            var result = new Series2CarrierSettlementResult();
            diag = diag ?? new List<string>();
            if (shipment == null)
            {
                result.Refusal = "Series2CarrierSettlement: no shipment; nothing settled.";
                diag.Add(result.Refusal);
                return result;
            }
            if (shipment.Status != LogisticsShipmentStatus.Completed || !shipment.deliveryApplied)
            {
                result.Refusal = $"Series2CarrierSettlement: shipment '{shipment.ShipmentId}' is {shipment.Status} (deliveryApplied={shipment.deliveryApplied}) — " +
                    "not fully accepted, so nothing settles and cargo stays in wagon custody.";
                diag.Add(result.Refusal);
                return result;
            }
            if (AlreadySettled(shipment.ShipmentId) || shipment.FreightRevenueCollected)
            {
                result.Refusal = $"Series2CarrierSettlement: shipment '{shipment.ShipmentId}' is already settled/collected — double settlement refused.";
                diag.Add(result.Refusal);
                return result;
            }
            if (payer == null || carrier == null)
            {
                result.Refusal = $"Series2CarrierSettlement: shipment '{shipment.ShipmentId}' needs both a payer and a carrier cash port; nothing settled.";
                diag.Add(result.Refusal);
                return result;
            }
            int charge = shipment.FreightChargeCents;
            if (charge <= 0)
            {
                settledShipmentIds.Add(shipment.ShipmentId);
                shipment.freightRevenueCollected = true;
                result.Settled = true;
                diag.Add($"Series2CarrierSettlement: shipment '{shipment.ShipmentId}' accepted with a 0c freight charge; marked settled, no cash moved.");
                return result;
            }
            if (payer.BalanceCents < charge)
            {
                result.Refusal = $"Series2CarrierSettlement: payer holds {payer.BalanceCents}c, freight charge is {charge}c for shipment '{shipment.ShipmentId}' — nothing moved.";
                diag.Add(result.Refusal);
                return result;
            }
            string debitProblem = payer.TryDebit(charge, $"freight charge shipment {shipment.ShipmentId}");
            if (debitProblem != null)
            {
                result.Refusal = $"Series2CarrierSettlement: payer debit failed ({debitProblem}) — nothing settled.";
                diag.Add(result.Refusal);
                return result;
            }
            string creditProblem = carrier.Credit(charge, $"freight charge shipment {shipment.ShipmentId}");
            if (creditProblem != null)
            {
                payer.Credit(charge, $"freight charge refund shipment {shipment.ShipmentId}");
                result.Refusal = $"Series2CarrierSettlement: carrier credit failed ({creditProblem}) — payer refunded, nothing settled.";
                diag.Add(result.Refusal);
                return result;
            }
            settledShipmentIds.Add(shipment.ShipmentId);
            shipment.freightRevenueCollected = true;
            result.Settled = true;
            diag.Add($"Series2CarrierSettlement: shipment '{shipment.ShipmentId}' settled: {charge}c from payer to carrier.");
            return result;
        }
    }
}
