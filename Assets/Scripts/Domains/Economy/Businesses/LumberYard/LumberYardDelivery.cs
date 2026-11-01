using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.LumberYard
{
    /// <summary>
    /// D2C: the yard's delivery pricing policy — DATA, not canon. Canon §6.5
    /// says lumber can go direct to the building site with "physical
    /// production, loading, travel, unloading and ledger attribution" still
    /// occurring, and Canon Part VI §6.2 says a lumber contract can obligate
    /// the seller to deliver and the buyer to provide unloading — but neither
    /// fixes rates. Every field here is calibration (Canon Part XV), set by
    /// the scenario/economy authority, never invented per-sale.
    /// </summary>
    [Serializable]
    public sealed class LumberYardDeliveryPolicy
    {
        /// <summary>Charge per lumber unit per mile, cents. Calibration.</summary>
        public int PerUnitMileCents;
        /// <summary>Flat charge per delivery order, cents. Calibration.</summary>
        public int FlatChargeCents;
        /// <summary>
        /// Reduction applied when the buyer provides unloading at the site
        /// (Canon Part VI §6.2: "the buyer to provide unloading"). Calibration.
        /// </summary>
        public int BuyerUnloadDiscountCents;

        public LumberYardDeliveryPolicy() { }

        public int ComputeChargeCents(int lumberUnits, int miles, bool buyerProvidesUnloading)
        {
            int units = Math.Max(0, lumberUnits);
            int distance = Math.Max(0, miles);
            int charge = Math.Max(0, FlatChargeCents)
                + units * distance * Math.Max(0, PerUnitMileCents);
            if (buyerProvidesUnloading)
                charge = Math.Max(0, charge - Math.Max(0, BuyerUnloadDiscountCents));
            return charge;
        }
    }

    /// <summary>
    /// D2C: one yard delivery — the lumber contract's delivery obligation
    /// (Canon Part VI §6.2: "A lumber contract can obligate the seller to
    /// deliver"). This is the OBLIGATION, not the freight execution:
    /// wagon dispatch, team capacity and route execution remain the
    /// LogisticsRuntimeManager's authority (DEPENDENCY_RULES: economic
    /// shipment and freight execution belongs under Domains/Economy/
    /// Logistics). The yard names the carrier (its own teamster crew or a
    /// named freight business) and carries the agreed delivery charge for the
    /// ledger to post — money moves only through ledger authorities, never
    /// here. Actual loading/travel/unloading is acknowledged as real work the
    /// logistics layer performs; the yard does not pretend it happened.
    /// </summary>
    [Serializable]
    public sealed class LumberYardDeliveryOrder
    {
        public EntityId OrderId = EntityId.Invalid; // EntityKind.Contract — a delivery obligation
        public EntityId SaleId = EntityId.Invalid;  // the yard sale this delivery fulfills
        public string ProjectId = string.Empty;     // named building site / project
        public string DestinationLabel = string.Empty;
        public int LumberUnits;
        public int Miles;
        public int ChargeCents;                     // agreed delivery charge, for the ledger to post
        public bool YardDelivers;                   // false = buyer arranges pickup/haulage
        public string CarrierLabel = string.Empty;  // named when YardDelivers (own team or freight business)
        public bool BuyerProvidesUnloading;         // Canon VI §6.2: the buyer may provide unloading
        public int ScheduledDayIndex;
        public int DeliveredDayIndex = -1;

        public LumberYardDeliveryOrder() { }

        public bool IsDelivered => DeliveredDayIndex >= 0;
    }

    /// <summary>
    /// D2C: the yard's delivery register — delivery obligations linked to
    /// real sales. A delivery is refused loudly when the sale is unknown
    /// (deliveries fulfill real sales, never thin air), when the destination
    /// project is unnamed, or when the yard undertakes delivery without
    /// naming its carrier (own teamster crew or a named freight business).
    /// </summary>
    public sealed class LumberYardDeliveryRegister
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<LumberYardDeliveryOrder> orders = new List<LumberYardDeliveryOrder>();
        private readonly string yardBusinessId;

        public LumberYardDeliveryRegister(string yardBusinessId)
        {
            this.yardBusinessId = yardBusinessId ?? string.Empty;
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<LumberYardDeliveryOrder> Orders => orders;
        public string YardBusinessId => yardBusinessId;

        /// <summary>
        /// Schedules a delivery against a real, already-recorded yard sale.
        /// The sale's BuyerLabel must name the project (construction sales do);
        /// retail sales can deliver to a named destination too. Returns the
        /// refusal, or null on success (order appended to Orders).
        /// </summary>
        public string ScheduleDelivery(
            LumberYardSaleRecord sale,
            string destinationLabel,
            int miles,
            bool yardDelivers,
            string carrierLabel,
            bool buyerProvidesUnloading,
            LumberYardDeliveryPolicy policy,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (sale == null)
                return "LumberYardDeliveryRegister: no sale — deliveries fulfill real sales, never thin air.";
            if (string.IsNullOrWhiteSpace(destinationLabel))
                return "LumberYardDeliveryRegister: the destination must be named — anonymous deliveries refused.";
            if (yardDelivers && string.IsNullOrWhiteSpace(carrierLabel))
                return "LumberYardDeliveryRegister: the yard undertakes delivery — name the carrier (own teamster crew or a named freight business).";
            if (policy == null)
                return "LumberYardDeliveryRegister: no delivery policy — charges are data, not guesses.";
            if (idRegistry == null)
                return "LumberYardDeliveryRegister: no EntityIdRegistry — delivery refused.";

            int lumberUnits = sale.UnitsSoldFor(ConstructionResourceKind.Lumber);
            var order = new LumberYardDeliveryOrder
            {
                OrderId = idRegistry.Allocate(EntityKind.Contract),
                SaleId = sale.SaleId,
                ProjectId = string.IsNullOrWhiteSpace(sale.BuyerLabel) ? destinationLabel.Trim() : sale.BuyerLabel,
                DestinationLabel = destinationLabel.Trim(),
                LumberUnits = lumberUnits,
                Miles = Math.Max(0, miles),
                ChargeCents = policy.ComputeChargeCents(lumberUnits, miles, buyerProvidesUnloading),
                YardDelivers = yardDelivers,
                CarrierLabel = yardDelivers ? carrierLabel.Trim() : string.Empty,
                BuyerProvidesUnloading = buyerProvidesUnloading,
                ScheduledDayIndex = dayIndex,
            };
            orders.Add(order);
            diag.Add($"LumberYardDeliveryRegister ({yardBusinessId}): delivery {order.OrderId} scheduled for sale {sale.SaleId} — "
                + $"{lumberUnits} lumber units to '{order.DestinationLabel}' ({order.Miles} mi), charge {order.ChargeCents}c, "
                + (yardDelivers
                    ? $"yard delivers via '{order.CarrierLabel}'{(buyerProvidesUnloading ? ", buyer provides unloading" : ", yard unloads")}"
                    : "buyer arranges haulage")
                + ". Freight execution belongs to Logistics; the charge is carried for the ledger to post.");
            return null;
        }

        /// <summary>
        /// Marks a delivery as completed on the given day. This records the
        /// yard's acknowledgement that the obligation was met — the physical
        /// loading/travel/unloading happened under the logistics layer.
        /// Returns the refusal, or null.
        /// </summary>
        public string MarkDelivered(EntityId orderId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            LumberYardDeliveryOrder order = null;
            foreach (var o in orders)
            {
                if (o != null && o.OrderId.Equals(orderId)) { order = o; break; }
            }
            if (order == null)
                return $"LumberYardDeliveryRegister: no delivery {orderId} — completion refused.";
            if (order.IsDelivered)
                return $"LumberYardDeliveryRegister: delivery {orderId} already marked delivered (day {order.DeliveredDayIndex}).";

            order.DeliveredDayIndex = dayIndex;
            diag.Add($"LumberYardDeliveryRegister ({yardBusinessId}): delivery {orderId} marked delivered (day {dayIndex}).");
            return null;
        }

        /// <summary>D2C save contract: lives inside the owning register class.</summary>
        [Serializable]
        public sealed class LumberYardDeliveryRegisterSaveDto
        {
            public List<LumberYardDeliveryOrder> Orders = new List<LumberYardDeliveryOrder>();
        }

        public LumberYardDeliveryRegisterSaveDto CaptureSaveDto()
        {
            var dto = new LumberYardDeliveryRegisterSaveDto();
            foreach (var o in orders)
            {
                if (o == null) continue;
                dto.Orders.Add(new LumberYardDeliveryOrder
                {
                    OrderId = o.OrderId,
                    SaleId = o.SaleId,
                    ProjectId = o.ProjectId,
                    DestinationLabel = o.DestinationLabel,
                    LumberUnits = o.LumberUnits,
                    Miles = o.Miles,
                    ChargeCents = o.ChargeCents,
                    YardDelivers = o.YardDelivers,
                    CarrierLabel = o.CarrierLabel,
                    BuyerProvidesUnloading = o.BuyerProvidesUnloading,
                    ScheduledDayIndex = o.ScheduledDayIndex,
                    DeliveredDayIndex = o.DeliveredDayIndex,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(LumberYardDeliveryRegisterSaveDto dto)
        {
            orders.Clear();
            if (dto == null || dto.Orders == null) return;
            foreach (var o in dto.Orders)
            {
                if (o == null) continue;
                orders.Add(o);
            }
        }
    }
}
