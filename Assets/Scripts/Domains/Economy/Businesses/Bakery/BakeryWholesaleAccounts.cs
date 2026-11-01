using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Bakery
{
    /// <summary>
    /// D1D: how fresh wholesale bread must be. Canon §7.3E is silent on
    /// freshness terms for supply agreements — this is a genuine fork, so the
    /// requirement is an explicit per-account term, never a global guess.
    /// </summary>
    public enum BakeryWholesaleFreshness
    {
        Unspecified = 0,
        /// <summary>Only bread baked today ships to this buyer.</summary>
        FreshOnly = 1,
        /// <summary>Fresh first, then day-old if the order still needs units.</summary>
        FreshOrDayOld = 2,
    }

    /// <summary>
    /// D1D: who scarce supply protects. Canon §7.3E: "The player can choose
    /// whether scarce supply protects contract customers, household trade,
    /// internal portfolio buyers or the highest-margin outlet." The choice is
    /// the owner's policy on the runtime — settable, never hardcoded. The
    /// default is marked TUNING (canon gives no default).
    /// </summary>
    public enum BakerySupplyProtectionPolicy
    {
        Unspecified = 0,
        /// <summary>Due wholesale accounts are filled first; the retail counter gets the remainder.</summary>
        ProtectWholesaleContracts = 1,
        /// <summary>The retail counter sells freely; wholesale accounts get the remainder.</summary>
        ProtectRetailCounter = 2,
        /// <summary>
        /// Shelf stock splits proportionally between due wholesale demand and
        /// the retail reference demand
        /// (<see cref="BakeryBreadCatalog.RetailReferenceDemandUnits"/>).
        /// </summary>
        ProportionalShare = 3,
    }

    /// <summary>
    /// D1D: one wholesale supply agreement — a standing account with a repeat
    /// buyer (boarding house, hotel, restaurant, general store, saloon, mine
    /// or logging camp — Canon §7.3E). The account names the buyer business,
    /// the product, the quantity per delivery, the delivery cadence, the
    /// negotiated wholesale price, and the freshness term. Active accounts
    /// due for delivery drive automatic batch planning (throughput visibility,
    /// Canon §7.3E) via
    /// <see cref="BakeryShopRuntime.PlanWholesaleBatches"/>.
    /// </summary>
    [Serializable]
    public sealed class BakeryWholesaleAccount
    {
        public string AccountId = string.Empty;

        /// <summary>The buying business instance (e.g. "boardinghouse-biz-1").</summary>
        public string BuyerBusinessId = string.Empty;

        /// <summary>The buyer's business type (BoardingHouse, Hotel, Restaurant, ...).</summary>
        public BusinessType BuyerType;

        public string ProductId = string.Empty; // bake.bread-loaf / bake.rolls / bake.pie

        /// <summary>Loaves/rolls/pies per delivery.</summary>
        public int UnitsPerDelivery;

        /// <summary>Days between deliveries (1 = daily).</summary>
        public int DeliveryCadenceDays = 1;

        /// <summary>The next day a delivery is due.</summary>
        public int NextDeliveryDayIndex;

        /// <summary>Negotiated wholesale price per unit, in cents (calibration — the agreed terms).</summary>
        public int PricePerUnitCents;

        /// <summary>
        /// Freshness term. Default FreshOrDayOld is TUNING (canon-silent fork) —
        /// set FreshOnly for buyers who insist on bread baked the same day.
        /// </summary>
        public BakeryWholesaleFreshness Freshness = BakeryWholesaleFreshness.FreshOrDayOld;

        public bool IsActive = true;

        public BakeryWholesaleAccount() { }
    }

    /// <summary>
    /// D1D: one fulfilled wholesale delivery — loaves taken from the shelf
    /// against an account. The record carries the seller lot ids and their
    /// provenance so the buyer's pantry intake (e.g.
    /// BoardingHouseFoodSupply.ReceiveBakeryBreadLot) keeps the full upstream
    /// chain. Money moves only through ledger authorities; this record is the
    /// goods side.
    /// </summary>
    [Serializable]
    public sealed class BakeryWholesaleDeliveryRecord
    {
        public string DeliveryId = string.Empty;
        public string AccountId = string.Empty;
        public string BuyerBusinessId = string.Empty;
        public BusinessType BuyerType;
        public string ProductId = string.Empty;
        public int Units;
        public int PricePerUnitCents;
        public int DayIndex;

        /// <summary>Seller bread-lot ids the units were taken from (parallel to UnitsByLot).</summary>
        public List<string> SellerLotIds = new List<string>();

        /// <summary>Units taken per seller lot, parallel to <see cref="SellerLotIds"/>.</summary>
        public List<int> UnitsByLot = new List<int>();

        /// <summary>Condition of each seller lot at load, parallel to <see cref="SellerLotIds"/>.</summary>
        public List<BakeryGoodsCondition> ConditionByLot = new List<BakeryGoodsCondition>();

        /// <summary>Baked day of each seller lot, parallel to <see cref="SellerLotIds"/>.</summary>
        public List<int> BakedDayByLot = new List<int>();

        /// <summary>Flour/notions/fuel provenance lines baked into the shipped loaves.</summary>
        public List<string> ProvenanceLines = new List<string>();

        /// <summary>True when the account's full order was covered; false on shortfall (loud, never faked).</summary>
        public bool FullyCovered = true;

        public BakeryWholesaleDeliveryRecord() { }

        public int TotalCents => Math.Max(0, Units) * Math.Max(0, PricePerUnitCents);
    }
}
