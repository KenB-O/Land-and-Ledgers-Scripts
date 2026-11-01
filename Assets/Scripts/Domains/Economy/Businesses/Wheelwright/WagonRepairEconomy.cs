using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Freight;
using UnityEngine;

namespace LandLedgers.Economy.Wheelwright
{
    /// <summary>
    /// D3A: repair quality tiers (Canon §6.5C: "Repair quality can distinguish
    /// proper, temporary and improvised work where economically meaningful").
    /// A field repair may restore a disabled wagon enough to return to town
    /// without making it equivalent to shop work (Canon §7.2C) — temporary and
    /// improvised repairs restore partial condition and record a known defect
    /// requiring proper shop follow-up.
    /// </summary>
    public enum WheelwrightRepairQuality
    {
        Proper = 0,      // full shop restoration
        Temporary = 1,   // interim fix to a parameterized condition; follow-up required
        Improvised = 2,  // field-expedient fix with whatever is at hand; lower restoration
    }

    /// <summary>
    /// W6b: the wagon repair economy — pricing (parts + labor), turnaround
    /// times, queue prioritization rules, and the demand links: draft-power
    /// wagon condition deteriorates with use → generates repair demand; freight
    /// operators are the customers; blacksmith ironwork is the upstream input.
    ///
    /// Canon ground: Tech X §6.4 (shared work-order authority — the queue lives
    /// in W6a), Tech X §6.3 (wear inputs: use, load, environment, age, storage,
    /// operating practice, unresolved defects), Canon §3.6 (a wagon in repair
    /// cannot haul). Every rate, price, and threshold below is CALIBRATION
    /// (Canon Part XV) — tuning, not canon.
    /// </summary>

    /// <summary>
    /// Pricing policy: parts at cost + markup, labor at the shop rate.
    /// The agreed price is still negotiated per order (RepairQueue.AgreeTerms —
    /// pricing freedom, Canon §7.2D); this produces the shop's ASK.
    /// </summary>
    public static class WheelwrightPricing
    {
        /// <summary>Calibration: shop labor rate. Tuning, not canon.</summary>
        public const int DefaultShopRateCentsPerHour = 25;

        /// <summary>Calibration: parts markup over material cost. Tuning, not canon.</summary>
        public const float DefaultPartsMarkup = 0.20f;

        /// <summary>Labor is billed by the started hour (calibration).</summary>
        public static int PricePartsPlusLabor(
            int materialUnits, int pricePerUnitCents, int laborMinutes,
            int shopRateCentsPerHour = DefaultShopRateCentsPerHour,
            float partsMarkup = DefaultPartsMarkup)
        {
            int parts = (int)Math.Round(
                Math.Max(0, materialUnits) * Math.Max(0, pricePerUnitCents) * (1f + Math.Max(0f, partsMarkup)));
            int hours = (int)Math.Ceiling(Math.Max(0, laborMinutes) / 60.0);
            int labor = hours * Math.Max(0, shopRateCentsPerHour);
            return parts + labor;
        }

        /// <summary>Builds the shop's ask from a diagnosed work order's own fields.</summary>
        public static int QuoteFromOrder(
            RepairWorkOrder order, int pricePerUnitCents,
            int shopRateCentsPerHour = DefaultShopRateCentsPerHour,
            float partsMarkup = DefaultPartsMarkup)
        {
            if (order == null) return 0;
            return PricePartsPlusLabor(order.MaterialUnitsNeeded, pricePerUnitCents,
                order.EstimatedLaborMinutes, shopRateCentsPerHour, partsMarkup);
        }
    }

    /// <summary>
    /// Turnaround estimation: the customer's wagon is done when the labor ahead
    /// of it in the queue plus its own labor fits the shop's daily capacity.
    /// Calibration (Canon Part XV): the shop's real bottleneck is wheelwright
    /// labor-hours per day, not bays.
    /// </summary>
    public static class TurnaroundEstimator
    {
        /// <summary>Calibration: one wheelwright's effective bench day. Tuning, not canon.</summary>
        public const int DefaultShopLaborMinutesPerDay = 480;

        /// <summary>
        /// Estimated completion day index for an order already in the queue.
        /// Counts only OPEN orders strictly ahead in queue position (FIFO within
        /// urgency — RepairQueue), plus the order's own labor.
        /// </summary>
        public static int EstimateCompletionDay(
            RepairWorkOrder order, RepairQueue queue, int currentDayIndex,
            int shopLaborMinutesPerDay = DefaultShopLaborMinutesPerDay)
        {
            if (order == null || queue == null) return currentDayIndex;
            int daily = Math.Max(1, shopLaborMinutesPerDay);
            int aheadMinutes = 0;
            foreach (RepairWorkOrder o in queue.Orders)
            {
                if (ReferenceEquals(o, order)) continue;
                if (!IsOpen(o)) continue;
                if (o.QueuePosition < order.QueuePosition)
                    aheadMinutes += Math.Max(0, o.EstimatedLaborMinutes);
            }
            int daysBeforeStart = (int)Math.Ceiling(aheadMinutes / (double)daily);
            int ownDays = Math.Max(1, (int)Math.Ceiling(Math.Max(0, order.EstimatedLaborMinutes) / (double)daily));
            return currentDayIndex + daysBeforeStart + ownDays - 1;
        }

        public static bool IsOpen(RepairWorkOrder order)
        {
            // D4K (additive): Declined orders leave the active queue too —
            // the customer said no; the order is closed, not pending.
            return order != null
                && order.Status != RepairOrderStatus.Complete
                && order.Status != RepairOrderStatus.Paid
                && order.Status != RepairOrderStatus.Declined;
        }
    }

    /// <summary>
    /// Queue prioritization policy for the wheelwright's shop. The queue itself
    /// orders emergency > urgent > routine, FIFO within urgency (W6a /
    /// RepairQueue). This policy adds the OPERATOR's rules on top — documented
    /// defaults the shop owner can change, not canon:
    /// 1. A freight wagon sitting InRepair blocks hauling (Canon §3.6) — flag it
    ///    so the operator sees the revenue cost of letting it wait.
    /// 2. Orders open past their patience window escalate one urgency step
    ///    (calibration: 7 days urgent, 14 days routine).
    /// </summary>
    public static class WheelwrightRepairPolicy
    {
        /// <summary>Calibration: days before a routine order escalates. Tuning, not canon.</summary>
        public const int RoutineEscalationDays = 14;

        /// <summary>Calibration: days before an urgent order escalates. Tuning, not canon.</summary>
        public const int UrgentEscalationDays = 7;

        /// <summary>
        /// Flags open orders whose wagons are freight wagons currently InRepair —
        /// every day they wait is a day the freight company cannot haul them.
        /// Returns the flagged work-order ids.
        /// </summary>
        public static List<string> FlagHaulingBlockedOrders(
            RepairQueue queue, FreightResourcePool freightPool)
        {
            var flagged = new List<string>();
            if (queue == null || freightPool == null) return flagged;
            foreach (RepairWorkOrder order in queue.Orders)
            {
                if (!TurnaroundEstimator.IsOpen(order)) continue;
                if (string.IsNullOrWhiteSpace(order.AssetId)) continue;
                FreightWagon wagon = freightPool.GetWagon(order.AssetId);
                if (wagon != null && wagon.Status == FreightWagonStatus.InRepair)
                    flagged.Add(order.WorkOrderId);
            }
            return flagged;
        }

        /// <summary>
        /// Escalates open orders past their patience window one urgency step.
        /// Returns the ids of escalated orders. Emergency is the ceiling.
        /// </summary>
        public static List<string> EscalateOverdueOrders(
            RepairQueue queue, int currentDayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var escalated = new List<string>();
            if (queue == null) return escalated;
            foreach (RepairWorkOrder order in queue.Orders)
            {
                if (!TurnaroundEstimator.IsOpen(order)) continue;
                int ageDays = currentDayIndex - order.OpenedDayIndex;
                int window = order.Urgency == RepairUrgency.Urgent ? UrgentEscalationDays
                    : order.Urgency == RepairUrgency.Routine ? RoutineEscalationDays
                    : int.MaxValue;
                if (ageDays >= window && order.Urgency < RepairUrgency.Emergency)
                {
                    order.Urgency = (RepairUrgency)((int)order.Urgency + 1);
                    escalated.Add(order.WorkOrderId);
                    diagnostics.Add(
                        $"WheelwrightRepairPolicy: {order.WorkOrderId} escalated to {order.Urgency} after {ageDays} days open.");
                }
            }
            return escalated;
        }
    }

    /// <summary>
    /// Draft-power wear model for wagons. Tech X §6.3: wear inputs include use,
    /// load, environment, age, storage, operating practice and unresolved
    /// defects — the rates below are CALIBRATION (Canon Part XV), the input
    /// SHAPE is canon. Route-around note: FreightResourcePool.ReleaseTeam
    /// already applies a flat wear per haul; this model is the richer
    /// alternative for callers with real trip inputs — it does not change the
    /// existing flat-wear behavior.
    /// </summary>
    public static class WagonWearModel
    {
        /// <summary>Calibration: base wear per mile at rated load on a good road.</summary>
        public const float BaseWearPerMile01 = 0.0004f;

        /// <summary>Calibration: cap on wear from a single trip.</summary>
        public const float MaxTripWear01 = 0.25f;

        public static float ComputeTripWear01(
            float tripMiles, float loadFraction01, float roadCondition01,
            bool hasUnresolvedDefects, float ageYears)
        {
            float wear = Math.Max(0f, tripMiles) * BaseWearPerMile01;
            wear *= 1f + Mathf.Clamp01(loadFraction01);                       // load
            wear *= 1f + (1f - Mathf.Clamp01(roadCondition01)) * 1.5f;        // environment
            if (hasUnresolvedDefects) wear *= 1.5f;                          // defects
            wear *= 1f + Math.Max(0f, ageYears) * 0.02f;                     // age
            return Mathf.Min(MaxTripWear01, wear);
        }
    }

    /// <summary>
    /// Blacksmith ironwork as input: finds finished wagon ironwork ("wagon-part"
    /// recipe assets) in a smithy's inventory so the wheelwright can receive
    /// them (WheelwrightRuntime.ReceiveIronworkPart — provenance kept). Read-only
    /// over the smith's stock; the handover itself is the wheelwright's receipt.
    /// </summary>
    public static class BlacksmithIronworkSupply
    {
        public static List<EquipmentAsset> FindAvailableIronwork(BlacksmithRuntime smithy)
        {
            var parts = new List<EquipmentAsset>();
            if (smithy == null || smithy.Assets == null) return parts;
            foreach (var kv in smithy.Assets)
            {
                EquipmentAsset part = kv.Value;
                if (part != null && string.Equals(part.Kind, "wagon-part", StringComparison.Ordinal))
                    parts.Add(part);
            }
            return parts;
        }
    }

    /// <summary>
    /// The repair-demand bridge: freight operators as customers. Scans a freight
    /// company's wagon pool for wagons worn below the repair threshold and opens
    /// wheelwright work orders for them — worn wagons generate real demand
    /// (Canon §7.2D queues exist because wear is real). The operator sends the
    /// wagon to repair through the pool (physical truth: a wagon in repair
    /// cannot haul, Canon §3.6). Never double-opens: wagons with an open order
    /// or already InRepair are skipped.
    /// </summary>
    public static class RepairDemandService
    {
        /// <summary>Calibration: below this condition a wagon needs the shop. Tuning, not canon.</summary>
        public const float DefaultRepairThreshold01 = 0.60f;

        /// <summary>Calibration: below this condition the need is an emergency. Tuning, not canon.</summary>
        public const float DefaultEmergencyThreshold01 = 0.25f;

        /// <summary>
        /// Scans the fleet; opens intake work orders at the wheelwright's shop for
        /// worn, available wagons and marks them InRepair in the pool. Returns
        /// the opened orders.
        /// </summary>
        public static List<RepairWorkOrder> ScanFleetForRepair(
            FreightResourcePool freightPool,
            WheelwrightRuntime shop,
            string customerBusinessId,
            string customerBusinessName,
            int dayIndex,
            List<string> diagnostics,
            float repairThreshold01 = DefaultRepairThreshold01,
            float emergencyThreshold01 = DefaultEmergencyThreshold01)
        {
            diagnostics = diagnostics ?? new List<string>();
            var opened = new List<RepairWorkOrder>();
            if (freightPool == null || shop == null)
            {
                diagnostics.Add("RepairDemandService: need a freight pool and a wheelwright shop.");
                return opened;
            }

            foreach (FreightWagon wagon in freightPool.Wagons)
            {
                if (wagon == null) continue;
                if (wagon.Condition01 >= repairThreshold01) continue;
                if (wagon.Status != FreightWagonStatus.Available) continue;
                if (HasOpenOrder(shop.Repairs, wagon.WagonId)) continue;

                RepairUrgency urgency = wagon.Condition01 < emergencyThreshold01
                    ? RepairUrgency.Emergency
                    : RepairUrgency.Routine;

                RepairWorkOrder order = shop.Repairs.Intake(
                    customerBusinessName, customerBusinessId,
                    wagon.WagonId, wagon.DisplayName,
                    $"wagon worn to {wagon.Condition01:P0} by hauling",
                    WheelwrightRuntime.WheelwrightCapabilityCode,
                    customerBusinessId, urgency, dayIndex, diagnostics);
                if (order == null) continue;

                if (!freightPool.TrySendToRepair(wagon.WagonId, diagnostics))
                {
                    diagnostics.Add($"RepairDemandService: {wagon.WagonId} could not enter repair — order {order.WorkOrderId} stays queued.");
                    continue;
                }
                opened.Add(order);
                diagnostics.Add(
                    $"RepairDemandService: {order.WorkOrderId} opened for {wagon.WagonId} ({urgency}) — wagon in repair, unavailable for hauling.");
            }
            return opened;
        }

        private static bool HasOpenOrder(RepairQueue queue, string assetId)
        {
            if (queue == null || string.IsNullOrWhiteSpace(assetId)) return false;
            foreach (RepairWorkOrder o in queue.Orders)
            {
                if (!TurnaroundEstimator.IsOpen(o)) continue;
                if (string.Equals(o.AssetId, assetId, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
