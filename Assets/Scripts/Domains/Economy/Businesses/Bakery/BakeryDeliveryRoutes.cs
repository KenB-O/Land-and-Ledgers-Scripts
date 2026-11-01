using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Bakery
{
    /// <summary>
    /// D1D: the status of a bakery delivery run. Bread cannot teleport
    /// (Tech X §9.3): staged runs depart, travel their transit days, and
    /// arrive — the buyer's pantry updates only on arrival (or on buyer
    /// pickup, when the bakery has no wagon to send).
    /// </summary>
    public enum BakeryDeliveryRunStatus
    {
        Unspecified = 0,
        /// <summary>Loaded and waiting to depart.</summary>
        Staged = 1,
        /// <summary>On the road; arrives <see cref="BakeryDeliveryRun.ArrivalDayIndex"/>.</summary>
        InTransit = 2,
        /// <summary>Delivered to the buyer (or picked up by the buyer).</summary>
        Delivered = 3,
        /// <summary>No bakery wagon/team — the buyer collects the load themselves.</summary>
        AwaitingBuyerPickup = 4,
        Cancelled = 5,
    }

    /// <summary>
    /// D1D: one cargo line of a delivery run — loaves taken from one seller
    /// bread lot. Carries everything the buyer's pantry intake needs
    /// (baked day, condition at load, provenance), so the receiving adapter
    /// can rebuild the lot snapshot at arrival without touching the bakery's
    /// shelf twice.
    /// </summary>
    [Serializable]
    public sealed class BakeryDeliveryCargoLine
    {
        public string SellerLotId = string.Empty;
        public string ProductId = string.Empty;
        public int Units;
        public BakeryGoodsCondition ConditionAtLoad = BakeryGoodsCondition.Unspecified;
        public int BakedDayIndex;
        public List<string> ProvenanceLines = new List<string>();

        public BakeryDeliveryCargoLine() { }

        /// <summary>
        /// Ages the cargo to the arrival day. Bread stales in transit exactly
        /// as on the shelf: baked day → fresh, next day → day-old, after that
        /// → stale (never delivered — waste instead).
        /// </summary>
        public BakeryGoodsCondition ConditionOnArrival(int arrivalDayIndex)
        {
            int age = arrivalDayIndex - BakedDayIndex;
            return age <= 0 ? BakeryGoodsCondition.Fresh
                : age == 1 ? BakeryGoodsCondition.DayOld
                : BakeryGoodsCondition.Stale;
        }

        /// <summary>
        /// Rebuilds a bread-lot snapshot for the buyer's pantry intake (e.g.
        /// BoardingHouseFoodSupply.ReceiveBakeryBreadLot), aged to the arrival
        /// day. The units already left the bakery's shelf at staging — this
        /// snapshot is the transfer record, not a second take.
        /// </summary>
        public BakeryBreadLot ToBreadLotSnapshot(int arrivalDayIndex)
        {
            var lot = new BakeryBreadLot
            {
                ProductId = ProductId,
                BakedDayIndex = BakedDayIndex,
                UnitCount = Math.Max(0, Units),
                Condition = ConditionOnArrival(arrivalDayIndex),
            };
            if (ProvenanceLines != null) lot.InputProvenance.AddRange(ProvenanceLines);
            return lot;
        }
    }

    /// <summary>
    /// D1D: one delivery run — a wholesale delivery moving from the bakery to
    /// the buyer's door (Canon §7.3E: "Delivery can be offered when labor and
    /// transport exist, creating a service advantage at a real operating
    /// cost"). The run names the wagon and draft team (canon expansion
    /// equipment: the delivery wagon), the driver labor minutes, and the haul
    /// cost in cents — the caller settles those as ordinary ledger outflows.
    /// </summary>
    [Serializable]
    public sealed class BakeryDeliveryRun
    {
        public string RunId = string.Empty;
        public string WholesaleDeliveryId = string.Empty;
        public string BuyerBusinessId = string.Empty;
        public BusinessType BuyerType;
        public string ProductId = string.Empty;
        public BakeryDeliveryRunStatus Status = BakeryDeliveryRunStatus.Unspecified;

        public int DepartDayIndex = -1;
        public int TransitDays;
        public int ArrivalDayIndex = -1;

        public List<BakeryDeliveryCargoLine> CargoLines = new List<BakeryDeliveryCargoLine>();

        /// <summary>Named wagon asset working the run (empty when buyer-pickup).</summary>
        public string WagonId = string.Empty;

        /// <summary>Named draft team working the run (empty when buyer-pickup).</summary>
        public string TeamId = string.Empty;

        /// <summary>Driver labor minutes for the run (the real operating cost, Canon §7.3E).</summary>
        public int DriverLaborMinutes;

        /// <summary>Haul cost in cents — wagon wear and team feed (calibration; the caller settles it).</summary>
        public int HaulCostCents;

        public LocalRecurringOrderHaulingResponsibility Hauling = LocalRecurringOrderHaulingResponsibility.Seller;

        public BakeryDeliveryRun() { }

        public int TotalUnits
        {
            get
            {
                int total = 0;
                if (CargoLines != null)
                {
                    foreach (var line in CargoLines)
                    {
                        if (line != null) total += Math.Max(0, line.Units);
                    }
                }

                return total;
            }
        }
    }

    /// <summary>
    /// D1D: the bakery's hauling capability — the terms a delivery run is
    /// staged under. Canon §7.3E offers delivery "when labor and transport
    /// exist"; without a wagon and team the bakery cannot deliver and the run
    /// waits for buyer pickup instead (never teleported, never faked).
    /// Distances, transit days, driver minutes and haul rates are calibration
    /// (Canon Part XV), settable per buyer.
    ///
    /// Not persisted: terms are caller-provided operating policy at staging
    /// time (like the oven definition passed to WorkDay). The staged
    /// <see cref="BakeryDeliveryRun"/> persists the resolved miles, transit
    /// days, driver minutes and haul cost as plain values.
    /// </summary>
    public sealed class BakeryHaulingTerms
    {
        /// <summary>Named delivery wagon asset (canon expansion equipment). Empty = no wagon.</summary>
        public string WagonId = string.Empty;

        /// <summary>Named draft team. Empty = no team.</summary>
        public string TeamId = string.Empty;

        /// <summary>Miles from the bakery to each buyer business id. Absent = 1 (in town).</summary>
        public Dictionary<string, int> MilesToBuyer = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Transit days to each buyer business id. Absent = 0 (same-day, in town).</summary>
        public Dictionary<string, int> TransitDaysToBuyer = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>TUNING: driver labor minutes per mile.</summary>
        public int DriverMinutesPerMile = 20;

        /// <summary>TUNING: haul cost in cents per mile (wagon wear, team feed).</summary>
        public int HaulCostCentsPerMile = 5;

        public BakeryHaulingTerms() { }

        public bool CanDeliver => !string.IsNullOrWhiteSpace(WagonId) && !string.IsNullOrWhiteSpace(TeamId);

        public int MilesFor(string buyerBusinessId)
        {
            if (!string.IsNullOrEmpty(buyerBusinessId)
                && MilesToBuyer != null
                && MilesToBuyer.TryGetValue(buyerBusinessId, out int miles))
            {
                return Math.Max(0, miles);
            }

            return 1;
        }

        public int TransitDaysFor(string buyerBusinessId)
        {
            if (!string.IsNullOrEmpty(buyerBusinessId)
                && TransitDaysToBuyer != null
                && TransitDaysToBuyer.TryGetValue(buyerBusinessId, out int days))
            {
                return Math.Max(0, days);
            }

            return 0;
        }
    }
}
