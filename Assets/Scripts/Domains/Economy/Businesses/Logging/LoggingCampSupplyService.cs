using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Logging
{
    /// <summary>
    /// C-7 (expanded campaign 2026-11-16): the camp's supply order — the request.
    /// The driver measures the route (the stand's authored miles to the mill —
    /// the store and the mill are both in town, so the out-of-town leg is the
    /// stand leg) and the live flour price; the service never invents a route
    /// or a price. The camp itself is the receiver: the ledger is the
    /// destination authority, so no caller-supplied receiver is carried here
    /// (unlike C-6, where the buyer side owns receiving).
    /// </summary>
    [Serializable]
    public sealed class LoggingCampSupplyRequest
    {
        public string StoreBusinessId = string.Empty;
        public string MillBusinessId = string.Empty;
        public float Miles;
        public float TransitGameSeconds = 10000f;
        public float ConditionMultiplier;
        public string CarrierLabel = string.Empty;

        public LoggingCampSupplyRequest() { }
    }

    /// <summary>
    /// C-7: the runtime bindings one camp supply order executes against.
    /// The store runtime is a per-drive binding (resolved by the production
    /// driver, like the cash ports); the camp itself is the service's fixed
    /// runtime (constructor). The rate card is the freight business's
    /// configured card (never invented in code); the wagon is the production
    /// wagon; the crew are real person ids resolved by the production driver.
    /// </summary>
    public sealed class LoggingCampSupplyExecution
    {
        public BusinessRuntimeState StoreRuntime;
        public DevelopmentTownConfiguration.FreightServiceRateCard RateCard;
        public EquipmentAsset Wagon;
        public IBusinessIdentityCashPort MillCash;
        public IBusinessIdentityCashPort CarrierCash;
        public EntityId LoaderId = EntityId.Invalid;
        public EntityId DriverId = EntityId.Invalid;
        public List<EntityId> DraftAnimalIds = new List<EntityId>();
        public EntityId UnloaderId = EntityId.Invalid;
        public TaskAuthority Tasks;
        public WorkTimeBudgetStore Budgets;
        public PersonScheduleTracker Schedules;
        public int UnitPriceCents;
        public LoggingCampSupplyExecution() { }
    }

    /// <summary>
    /// C-7 (expanded campaign 2026-11-16): the logging camp's ongoing supply
    /// line — the order leg C-1 left open. The C-1 cover (7 days) buys time;
    /// this service is what keeps the camp fed after it: the mill orders
    /// flour sacks from the General Store at the store's LIVE selling price,
    /// pays for the goods and the freight from mill cash, hauls the sacks to
    /// the camp through the real freight chain (FreightShipmentService, one
    /// shipment per trip, conservation verified), and records the delivery
    /// into the camp's supply ledger with the store named as supplier.
    ///
    /// The good is flour, the trail staple (Canon: the store "can
    /// simultaneously lead workwear, trail grocery staples and barely
    /// participate in remote-camp supply"): the store sells it by the 10 lb
    /// sack, the bakery already buys it by the sack (BakeryFlourLocalPurchase),
    /// and no "provisions" SKU exists anywhere — so none is invented. The
    /// conversion is 1 sack = 5 provision units (2 lb of flour per man-day of
    /// trail rations: bread, mush and gravy are the ration's bulk; the figure
    /// is provisional scenario calibration, Kennedy can correct it). The
    /// cargo spec is the sack itself: 10 lb = 4.5 kg, 0.01 m3 — 100 sacks per
    /// trip on the 450 kg production wagon (mass binds).
    ///
    /// Cover policy (provisional, documented): the camp holds 7 days of cover
    /// (the C-1 figure) and reorders when the balance covers fewer than 3
    /// days for the rostered crew; an order restores the full 7 days, in
    /// whole sacks (rounded up). The balance is the order state: a delivered
    /// order raises the balance above the threshold, so no duplicate order
    /// fires; a failed order leaves the balance low and the next day's drive
    /// re-orders the remainder. No persistent order record is kept — the
    /// ledger's own delivery list is the record.
    ///
    /// Payment order (deliberate deviation from the C-5 pay-all-upfront shape,
    /// because the goods here cross a freight leg that can fail): the freight
    /// fare is paid per trip BEFORE the trip's shipment is created (the
    /// construction coordinator's pattern, so a payment refusal leaves no
    /// shipment behind); the goods for that trip are paid AFTER the shipment
    /// exists but BEFORE the chain runs (mill debit, store lots consumed,
    /// store settlement once) — so a shipment-creation failure strands no
    /// paid-for flour. A trip that fails after its goods are paid strands
    /// that trip's flour on the failed shipment (the C-6 disposition:
    /// quarantined, recorded loudly, never re-shipped); the next day's order
    /// covers the shortfall with fresh flour. A trip that fails before
    /// departing reverses its fare in full — the carrier is debited the
    /// fare it was credited and the mill is credited back, because a
    /// one-legged refund (mill refunded, carrier keeping the fare) would
    /// create money; a departed trip's fare stands.
    ///
    /// The service holds no save slot of its own: the ledger deliveries
    /// persist through the camp's own save DTO, and the shipment sequence is
    /// re-seeded from the freight service's tracked shipments (the QA-20
    /// pattern), so a service rebuilt after a load never re-issues an id.
    /// </summary>
    public sealed class LoggingCampSupplyService
    {
        /// <summary>Flour, the trail staple: item, category and unit label mirror BakeryFlourLocalPurchase.</summary>
        public const string FlourItemId = "flour";
        public const string FlourCategoryId = "staple_food";
        public const string FlourUnitLabel = "10 lb sack";
        /// <summary>Provision units per sack of flour (provisional scenario calibration — see the class summary).</summary>
        public const int ProvisionUnitsPerSack = 5;
        /// <summary>Mass of one 10 lb sack of flour, kilograms.</summary>
        public const float FlourSackMassKg = 4.5f;
        /// <summary>Volume of one 10 lb sack of flour, cubic metres.</summary>
        public const float FlourSackVolumeM3 = 0.01f;
        /// <summary>Days of cover an order restores (the C-1 cover figure).</summary>
        public const int CoverTargetDays = 7;
        /// <summary>Order when the balance covers fewer than this many days for the rostered crew.</summary>
        public const int ReorderBelowDays = 3;

        private readonly LoggingCampSite camp;
        private readonly FreightShipmentService freight;
        private readonly List<string> diagnostics;
        private int sequence;

        public LoggingCampSupplyService(
            LoggingCampSite camp,
            FreightShipmentService freight,
            List<string> diagnostics)
        {
            this.camp = camp;
            this.freight = freight;
            this.diagnostics = diagnostics ?? new List<string>();
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// C-7: true when the camp needs an order — rostered crew present, camp
        /// not struck, and the balance covers fewer than ReorderBelowDays days.
        /// Pure: no cash, no freight, no store needed.
        /// </summary>
        public static bool ShouldOrder(LoggingCampSite camp)
        {
            if (camp == null || camp.IsStruck) return false;
            int headcount = camp.CrewHeadcount;
            if (headcount <= 0) return false;
            float ration = camp.Parameters != null ? Math.Max(0f, camp.Parameters.ProvisionsPerHeadPerDay) : 1f;
            if (ration <= 0f) return false;
            return camp.SupplyLedger.Balance < headcount * ration * ReorderBelowDays;
        }

        /// <summary>
        /// C-7: whole sacks needed to restore CoverTargetDays of cover for the
        /// rostered crew, rounded up (a partial sack is never ordered). Zero
        /// when the balance already covers the target, the camp is struck, or
        /// there is no crew to feed. Pure.
        /// </summary>
        public static int SacksToOrder(LoggingCampSite camp)
        {
            if (camp == null || camp.IsStruck) return 0;
            int headcount = camp.CrewHeadcount;
            if (headcount <= 0) return 0;
            float ration = camp.Parameters != null ? Math.Max(0f, camp.Parameters.ProvisionsPerHeadPerDay) : 1f;
            if (ration <= 0f) return 0;
            float target = headcount * ration * CoverTargetDays;
            float shortfall = target - camp.SupplyLedger.Balance;
            if (shortfall <= 0f) return 0;
            return (int)Math.Ceiling(shortfall / ProvisionUnitsPerSack);
        }

        /// <summary>
        /// C-7: the cargo spec for one sack of flour. Always a new instance:
        /// callers own their spec.
        /// </summary>
        public static FreightCargoSpec FlourSackSpec => new FreightCargoSpec
        {
            MassKgPerUnit = FlourSackMassKg,
            VolumeM3PerUnit = FlourSackVolumeM3,
        };

        /// <summary>
        /// C-7: the DEF-016 cargo-aware freight quote for a camp supply order's
        /// flour load. Pure: no camp, no cash, no freight service needed.
        /// Refuses exactly as the calculator refuses (non-positive miles or
        /// sacks, no wagon capacity, negative rate card) — a refused quote is
        /// a refused order, never a guessed charge.
        /// </summary>
        public static bool TryQuoteSupplyCharge(
            int sacks,
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
                reason = "LoggingCampSupplyService: no freight rate card — the delivery charge is quoted from the freight business's configured card, never invented.";
                return false;
            }
            if (wagon == null)
            {
                reason = "LoggingCampSupplyService: no wagon — a supply quote needs the production wagon's capacity.";
                return false;
            }
            return FreightServiceQuoteCalculator.TryCalculateCargo(
                miles,
                rateCard.baseServiceFeeCents,
                rateCard.ratePerMileCents,
                rateCard.chargeRoundTrip,
                sacks,
                FlourSackSpec,
                wagon.PayloadMassCapacityKg,
                wagon.CargoVolumeCapacityM3,
                conditionMultiplier,
                out quote,
                out reason);
        }

        /// <summary>
        /// C-7: executes one camp supply order end to end — prove the flour
        /// from identified store lots, quote the freight, check the mill can
        /// pay goods + freight, then per trip pay the fare, create the
        /// shipment, pay for that trip's sacks, run the freight chain, and
        /// record the delivery into the camp ledger. Returns null on success
        /// (delivered, or already supplied — a no-op re-execution); a refusal
        /// otherwise, naming how far the order got. An order with insufficient
        /// mill cash refuses before any cash moves or any freight is created.
        /// </summary>
        public string ExecuteSupplyOrder(
            LoggingCampSupplyRequest request,
            LoggingCampSupplyExecution execution,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (camp == null)
                return "LoggingCampSupplyService: no camp — supply refused.";
            if (freight == null)
                return "LoggingCampSupplyService: no FreightShipmentService — provisions move through the real freight chain or not at all.";
            if (request == null)
                return "LoggingCampSupplyService: no supply request — store, mill and miles are required.";
            if (execution == null)
                return "LoggingCampSupplyService: no execution bindings — store runtime, rate card, wagon, cash ports and crew are required.";
            if (camp.IsStruck)
                return "LoggingCampSupplyService: the camp is struck — no supply for a closed camp.";
            if (camp.CrewHeadcount <= 0)
                return "LoggingCampSupplyService: the camp has no rostered crew — nobody to feed.";
            if (!string.Equals(camp.BusinessInstanceId, request.MillBusinessId, StringComparison.Ordinal))
                return $"LoggingCampSupplyService: camp '{camp.CampId}' belongs to business '{camp.BusinessInstanceId}', not '{request.MillBusinessId}' — refusing an order for another business's camp.";
            if (string.IsNullOrWhiteSpace(request.StoreBusinessId))
                return "LoggingCampSupplyService: the supplying store must be named — anonymous suppliers refused.";
            if (string.IsNullOrWhiteSpace(request.MillBusinessId))
                return "LoggingCampSupplyService: the buying mill must be named — refusing.";
            if (string.Equals(request.StoreBusinessId, request.MillBusinessId, StringComparison.Ordinal))
                return "LoggingCampSupplyService: the store and the mill are the same business — a business cannot supply itself.";
            if (string.IsNullOrWhiteSpace(request.CarrierLabel))
                return "LoggingCampSupplyService: the carrier must be named — name the freight business carrying the flour.";

            // Already supplied: the balance gate is the idempotency. A repeat
            // call after a delivered order finds the balance at cover and
            // does nothing — it never buys twice.
            int sacks = SacksToOrder(camp);
            if (sacks <= 0)
            {
                diag.Add($"LoggingCampSupplyService: camp '{camp.CampId}' already holds {camp.SupplyLedger.Balance} provisions " +
                    $"({camp.CrewHeadcount} heads) — no order needed.");
                return null;
            }

            if (request.Miles <= 0)
                return "LoggingCampSupplyService: a camp supply needs a positive distance — the DEF-016 quote refuses zero miles.";
            if (execution.StoreRuntime == null)
                return "LoggingCampSupplyService: no store runtime — the flour is proved from real store lots, never thin air.";
            if (!string.Equals(execution.StoreRuntime.BusinessId, request.StoreBusinessId, StringComparison.Ordinal))
                return "LoggingCampSupplyService: the store runtime belongs to a different business than the request names — refusing.";
            if (execution.RateCard == null)
                return "LoggingCampSupplyService: no freight rate card — the delivery charge is quoted from the freight business's configured card, never invented.";
            if (execution.Wagon == null)
                return "LoggingCampSupplyService: no wagon — refusing phantom transport.";
            if (!execution.Wagon.IsWagon)
                return $"LoggingCampSupplyService: asset '{execution.Wagon.AssetId}' is not a wagon (kind '{execution.Wagon.Kind}') — refusing.";
            if (execution.Wagon.PayloadMassCapacityKg <= 0f || execution.Wagon.CargoVolumeCapacityM3 <= 0f)
                return $"LoggingCampSupplyService: wagon '{execution.Wagon.AssetId}' has no payload capacity ({execution.Wagon.PayloadMassCapacityKg} kg / {execution.Wagon.CargoVolumeCapacityM3} m3) — refusing.";
            if (execution.MillCash == null)
                return "LoggingCampSupplyService: no mill cash port — the order settles from real mill cash, never on credit here.";
            if (execution.CarrierCash == null)
                return "LoggingCampSupplyService: no carrier cash port — the delivery charge is credited to a real carrier, never dropped.";
            if (!string.Equals(execution.MillCash.BusinessId, request.MillBusinessId, StringComparison.Ordinal))
                return "LoggingCampSupplyService: seller, buyer, and cash-port identities do not match distinct businesses.";
            if (execution.UnitPriceCents <= 0)
                return "LoggingCampSupplyService: a positive flour price is required — a zero or negative price refuses, no free flour.";
            if (execution.Tasks == null || execution.Budgets == null)
                return "LoggingCampSupplyService: no task authority or work-time budgets — the freight chain runs on real person time or not at all.";
            if (!execution.LoaderId.IsValid || !execution.DriverId.IsValid || !execution.UnloaderId.IsValid)
                return "LoggingCampSupplyService: the freight crew is incomplete (loader, driver and unloader are all required) — refusing.";

            // Prove the sacks from identified store retail lots before
            // anything moves (the C-5 proof shape).
            var sourceLotIds = new List<string>();
            int provable = 0;
            foreach (var lot in execution.StoreRuntime.RetailItemLots)
            {
                if (provable >= sacks) break;
                if (lot == null
                    || !string.Equals(lot.ItemId, FlourItemId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.CategoryId, FlourCategoryId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.UnitLabel, FlourUnitLabel, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.OwnerBusinessId, request.StoreBusinessId, StringComparison.Ordinal)
                    || !string.Equals(lot.CustodianBusinessId, request.StoreBusinessId, StringComparison.Ordinal)) continue;
                int take = Math.Min(sacks - provable, lot.AvailableUnits);
                if (take <= 0) continue;
                sourceLotIds.Add(lot.LotId);
                provable += take;
            }
            if (provable < sacks)
            {
                return $"LoggingCampSupplyService: store retail lots prove {provable} sack(s) of flour, {sacks} requested — shortfall {sacks - provable} sack(s); nothing moved.";
            }

            // Quote the freight, then the affordability pre-check on goods +
            // freight together, before any cash moves or any freight is created.
            if (!TryQuoteSupplyCharge(sacks, request.Miles, execution.RateCard,
                    execution.Wagon, request.ConditionMultiplier, out FreightServiceQuote quote, out string quoteRefusal))
                return "LoggingCampSupplyService: no freight quote — " + quoteRefusal;
            int goodsTotalCents = checked(sacks * execution.UnitPriceCents);
            int freightTotalCents = quote.TotalPriceCents;
            if (execution.MillCash.BalanceCents < goodsTotalCents + freightTotalCents)
                return $"LoggingCampSupplyService: the mill holds {execution.MillCash.BalanceCents}c but owes {goodsTotalCents}c for flour + {freightTotalCents}c for freight ({goodsTotalCents + freightTotalCents}c) — nothing moved; the crew waits until the mill can pay.";

            int trips = quote.TripCount;
            int perTripSacks = FreightCargoCapacity.MaxUnitsPerTrip(
                FlourSackSpec, execution.Wagon.PayloadMassCapacityKg, execution.Wagon.CargoVolumeCapacityM3);
            if (trips <= 0 || perTripSacks <= 0)
                return "LoggingCampSupplyService: the quoted load needs no trips, or the wagon cannot carry even one sack of flour — refusing.";

            freight.RegisterWagon(execution.Wagon);
            SeedSequenceFromService(freight);

            var progress = new OrderProgress();
            int deliveredSacks = 0;
            for (int trip = 0; trip < trips; trip++)
            {
                int tripSacks = Math.Min(perTripSacks, sacks - deliveredSacks);
                if (tripSacks <= 0)
                    return $"LoggingCampSupplyService: trip {trip + 1} of {trips} has no sacks left — refusing; the load split is inconsistent " +
                        $"({progress.DeliveredSacks} of {sacks} sacks delivered).";

                string tripRefusal = RunTrip(request, execution, trip, tripSacks, quote, dayIndex, progress, diag);
                if (tripRefusal != null)
                    return tripRefusal;
                deliveredSacks += tripSacks;
            }

            if (deliveredSacks != sacks)
                return $"LoggingCampSupplyService: delivered {deliveredSacks} sack(s) of {sacks} — refusing; the load is incomplete.";
            diag.Add($"LoggingCampSupplyService: camp '{camp.CampId}' supplied — {deliveredSacks} sack(s) of flour " +
                $"({deliveredSacks * ProvisionUnitsPerSack} provisions) in {trips} trip(s), goods {goodsTotalCents}c + freight {freightTotalCents}c settled; " +
                $"ledger balance now {camp.SupplyLedger.Balance}.");
            return null;
        }

        /// <summary>
        /// C-7: the camp-side receiver — the freight chain calls this during
        /// unloading. Records the delivered sacks as provision units into the
        /// camp ledger with the store named as supplier (never anonymous),
        /// and returns the sacks accepted. A refusal returns 0 and the
        /// shipment is held Delayed, never falsely marked delivered (the
        /// PASS7-008 rule). Public so tests can pin the refusal path directly.
        /// </summary>
        public int ReceiveCampDelivery(LogisticsShipmentState shipment, int sacks, string storeBusinessId, int dayIndex, List<string> diag, out string reason)
        {
            reason = string.Empty;
            diag = diag ?? diagnostics;
            if (camp == null)
            {
                reason = "LoggingCampSupplyService: no camp to receive into.";
                return 0;
            }
            if (camp.IsStruck)
            {
                reason = $"LoggingCampSupplyService: camp '{camp.CampId}' is struck — a closed camp receives nothing.";
                return 0;
            }
            if (shipment == null || sacks <= 0)
            {
                reason = "LoggingCampSupplyService: no shipment or nothing to receive.";
                return 0;
            }
            if (string.IsNullOrWhiteSpace(storeBusinessId))
            {
                reason = "LoggingCampSupplyService: no supplier named — provisions never arrive anonymously.";
                return 0;
            }
            float units = sacks * ProvisionUnitsPerSack;
            string deliveryRefusal = camp.SupplyLedger.RecordDelivery(storeBusinessId, units, dayIndex,
                $"pack-train from the General Store: {sacks} sack(s) of flour ({units} provisions).", diag);
            if (deliveryRefusal != null)
            {
                reason = $"LoggingCampSupplyService: the camp ledger refused the delivery — {deliveryRefusal}";
                return 0;
            }
            return sacks;
        }

        /// <summary>Per-order progress within one Execute call (never kept across calls).</summary>
        private sealed class OrderProgress
        {
            public int DeliveredSacks;
            public int SettledFreightCents;
        }

        /// <summary>
        /// QA-20: the service holds no save slot and is rebuilt per drive,
        /// so its sequence restarts at 0. Seed it from the freight
        /// service's own tracked shipments before minting, so a new id never
        /// collides with one the service already holds. Only the campsup-
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
                    || !id.StartsWith("campsup-", StringComparison.Ordinal)) continue;
                if (int.TryParse(id.Substring("campsup-".Length), out int n) && n >= sequence)
                    sequence = n + 1;
            }
        }

        /// <summary>
        /// One trip of a camp supply order: pay the trip's fare, create the
        /// shipment, pay for that trip's sacks, run the freight chain, verify
        /// conservation. The fare is paid BEFORE the trip runs (the
        /// construction coordinator's pattern, so a payment refusal leaves no
        /// shipment behind); the goods are paid AFTER the shipment exists
        /// (so a shipment-creation failure strands no paid-for flour) but
        /// BEFORE the chain runs (the lots are consumed into the shipment's
        /// custody). A trip that fails before departing reverses its fare in
        /// full (carrier debited, mill credited); a trip that fails after
        /// departing keeps its fare standing for the attempt, and its flour
        /// stays quarantined on the failed shipment — recorded loudly, never re-shipped.
        /// </summary>
        private string RunTrip(
            LoggingCampSupplyRequest request,
            LoggingCampSupplyExecution execution,
            int tripIndex,
            int tripSacks,
            FreightServiceQuote quote,
            int dayIndex,
            OrderProgress progress,
            List<string> diag)
        {
            string tripLabel = $"trip {tripIndex + 1} of {quote.TripCount} for camp '{camp.CampId}'";
            int perTripCharge = quote.PerTripTotalCents;
            int goodsCents = checked(tripSacks * execution.UnitPriceCents);
            var hooks = new FreightShipmentHooks
            {
                Tasks = execution.Tasks,
                Budgets = execution.Budgets,
                Schedules = execution.Schedules,
                DayIndex = dayIndex,
                Diagnostics = diag,
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string receiveReason) =>
                    ReceiveCampDelivery(shipment, quantityUnits, request.StoreBusinessId, dayIndex, diag, out receiveReason),
            };

            // Pay this trip's fare before the trip runs. No shipment exists
            // yet, so a payment refusal leaves nothing behind to reconcile.
            string debitRefusal = execution.MillCash.TryDebit(perTripCharge,
                $"camp supply {tripLabel} (flour freight)");
            if (debitRefusal != null)
                return $"LoggingCampSupplyService: {tripLabel} delivery fare unpaid — {debitRefusal} ({progress.SettledFreightCents}c of {quote.TotalPriceCents}c already settled for this order).";
            string creditRefusal = execution.CarrierCash.Credit(perTripCharge,
                $"camp supply {tripLabel} (flour freight)");
            if (creditRefusal != null)
            {
                string refundRefusal = execution.MillCash.Credit(perTripCharge,
                    $"camp supply {tripLabel} refund");
                return $"LoggingCampSupplyService: {tripLabel} carrier credit refused — {creditRefusal}. "
                    + (refundRefusal == null
                        ? "The mill was refunded the trip fare in full."
                        : $"The mill refund ALSO failed — {refundRefusal}; reconcile before retrying.");
            }
            progress.SettledFreightCents += perTripCharge;

            string shipmentId = $"campsup-{sequence++}";
            LogisticsShipmentState shipment = freight.CreateShipment(
                shipmentId, request.StoreBusinessId, request.MillBusinessId,
                tripSacks, FlourSackSpec, execution.Wagon.AssetId, hooks, out string createReason);
            if (shipment == null)
                return RefundFareAndFail(progress, perTripCharge,
                    $"LoggingCampSupplyService: {tripLabel} shipment not created — {createReason}", execution, tripLabel);
            if (shipment.Status == LogisticsShipmentStatus.Failed)
                return RefundFareAndFail(progress, perTripCharge,
                    $"LoggingCampSupplyService: {tripLabel} shipment '{shipmentId}' blocked pre-load ({shipment.BlockedReason}) — it owns zero cargo.",
                    execution, tripLabel);

            // Pay for this trip's sacks now that the shipment exists: mill
            // debit, store lots consumed, store settlement once (the C-5
            // shape, per trip). A failure here refunds the fare and abandons
            // the empty shipment, so nothing is stranded.
            string goodsDebitRefusal = execution.MillCash.TryDebit(goodsCents,
                $"camp supply {tripLabel} flour ({tripSacks} sack(s))");
            if (goodsDebitRefusal != null)
            {
                freight.FailAbandonedShipment(shipment,
                    $"abandoned by camp supply — {tripLabel} goods unpaid", hooks);
                return RefundFareAndFail(progress, perTripCharge,
                    $"LoggingCampSupplyService: {tripLabel} flour payment refused — {goodsDebitRefusal}; nothing loaded.",
                    execution, tripLabel);
            }
            if (!execution.StoreRuntime.TryConsumeRetailItemStockUnits(
                    FlourItemId, FlourCategoryId, tripSacks, out int consumed, retailSale: true)
                || consumed != tripSacks)
            {
                string goodsRefundRefusal = execution.MillCash.Credit(goodsCents,
                    $"camp supply {tripLabel} flour refund");
                freight.FailAbandonedShipment(shipment,
                    $"abandoned by camp supply — {tripLabel} store stock changed before checkout", hooks);
                return RefundFareAndFail(progress, perTripCharge,
                    $"LoggingCampSupplyService: {tripLabel} store stock changed before checkout ({consumed}/{tripSacks} consumed). " +
                    (goodsRefundRefusal == null ? "Mill goods debit reversed." : $"Mill goods refund failed: {goodsRefundRefusal}"),
                    execution, tripLabel);
            }
            execution.StoreRuntime.RecordRetailItemSettlement(FlourCategoryId, tripSacks, goodsCents);

            string chainRefusal = RunShipmentChain(shipment, request, execution, dayIndex, diag);
            if (chainRefusal != null)
                return FailTripDisposition(progress, perTripCharge, shipment,
                    $"LoggingCampSupplyService: {tripLabel} failed in transit — {chainRefusal}",
                    execution, tripLabel, hooks, diag);

            string verifyReason;
            if (!freight.VerifyConservation(shipment, tripSacks, out verifyReason))
                return FailTripDisposition(progress, perTripCharge, shipment,
                    $"LoggingCampSupplyService: conservation FAILED for '{shipmentId}' ({tripLabel}) — {verifyReason}. Flour does not reach the camp.",
                    execution, tripLabel, hooks, diag);
            progress.DeliveredSacks += tripSacks;
            diag.Add($"LoggingCampSupplyService: {tripLabel} arrived at camp '{camp.CampId}' ({tripSacks} sack(s)).");
            return null;
        }

        /// <summary>
        /// A trip that never departed (no shipment, or a shipment blocked
        /// pre-load): reverse the fare in full, so a retry pays exactly
        /// once. A refund is two-legged — the carrier is debited the fare
        /// it was credited, and the mill is credited back. A one-legged
        /// refund would create money (the mill refunded AND the carrier
        /// keeping the fare), so both legs are required; a refusal on
        /// either leg is named loudly and nothing is half-moved.
        /// </summary>
        private string RefundFareAndFail(
            OrderProgress progress,
            int perTripCharge,
            string failure,
            LoggingCampSupplyExecution execution,
            string tripLabel)
        {
            string refundNote = RefundTripFare(progress, perTripCharge, execution, tripLabel, out string dispute);
            if (refundNote != null)
                return failure + refundNote + " The trip never departed.";
            return failure + dispute;
        }

        /// <summary>
        /// Reverses one trip's fare in full: the carrier is debited the
        /// fare it was credited, the mill is credited back, and the
        /// order's settled-freight total is reduced. Returns the note to
        /// append on success; on any refusal returns null and sets the
        /// dispute text, leaving the money where it is (never half-moved:
        /// if the mill credit fails after the carrier debit, the carrier
        /// is re-credited so no money is created or destroyed).
        /// </summary>
        private string RefundTripFare(
            OrderProgress progress,
            int perTripCharge,
            LoggingCampSupplyExecution execution,
            string tripLabel,
            out string dispute)
        {
            dispute = null;
            string carrierDebitRefusal = execution.CarrierCash.TryDebit(perTripCharge,
                $"camp supply {tripLabel} fare refund");
            if (carrierDebitRefusal != null)
            {
                dispute = $" The carrier fare-reversal debit ALSO failed — {carrierDebitRefusal}; " +
                    $"the carrier keeps {perTripCharge}c and the mill is short it; reconcile before retrying.";
                return null;
            }
            string millRefundRefusal = execution.MillCash.Credit(perTripCharge,
                $"camp supply {tripLabel} refund");
            if (millRefundRefusal != null)
            {
                string restoreRefusal = execution.CarrierCash.Credit(perTripCharge,
                    $"camp supply {tripLabel} refund restore");
                dispute = $" The mill refund ALSO failed — {millRefundRefusal}; " +
                    (restoreRefusal == null
                        ? $"the carrier was re-credited {perTripCharge}c, so no money was created or destroyed; reconcile before retrying."
                        : $"the carrier re-credit ALSO failed — {restoreRefusal}; reconcile before retrying.");
                return null;
            }
            progress.SettledFreightCents -= perTripCharge;
            return $" Freight refunded ({perTripCharge}c returned to the mill; the carrier's fare reversed).";
        }

        /// <summary>
        /// A trip that failed after its shipment was created: abandon the
        /// shipment (releasing the wagon hold; goods stay quarantined on
        /// the failed shipment) and refund the fare only when the shipment
        /// never departed. A departed trip's fare stands for the attempt.
        /// </summary>
        private string FailTripDisposition(
            OrderProgress progress,
            int perTripCharge,
            LogisticsShipmentState shipment,
            string failure,
            LoggingCampSupplyExecution execution,
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
                refundNote = RefundTripFare(progress, perTripCharge, execution, tripLabel, out string dispute) ?? dispute;
            }
            if (shipment != null)
            {
                freight.FailAbandonedShipment(shipment, failure, hooks);
                refundNote += shipment.loadApplied
                    ? $" {shipment.RemainingQuantityUnits}u quarantined on the failed shipment."
                    : " Goods stay with the store.";
            }
            return failure + refundNote
                + $" ({progress.SettledFreightCents}c freight settled for this order; a departed trip's fare stands for the attempt).";
        }

        /// <summary>
        /// The construction coordinator's chain, verbatim in shape:
        /// load, dispatch, transit, unload, each step loud on refusal.
        /// The receiver accepts the sacks into the camp ledger during
        /// unloading through the service's own ReceiveCampDelivery.
        /// </summary>
        private string RunShipmentChain(
            LogisticsShipmentState shipment,
            LoggingCampSupplyRequest request,
            LoggingCampSupplyExecution execution,
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
                ReceiveShipment = (LogisticsShipmentState state, int quantityUnits, out string receiveReason) =>
                    ReceiveCampDelivery(state, quantityUnits, request.StoreBusinessId, dayIndex, diag, out receiveReason),
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
