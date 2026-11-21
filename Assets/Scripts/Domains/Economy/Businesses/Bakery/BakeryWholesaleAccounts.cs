using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Logistics;
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
    /// The bakery owner's enabled sales channels. This controls whether new
    /// retail sales and new wholesale agreements may be initiated; it does
    /// not cancel or erase a wholesale agreement already accepted.
    /// </summary>
    public enum BakerySalesChannelPolicy
    {
        Unspecified = 0,
        Retail = 1,
        Wholesale = 2,
        RetailAndWholesale = 3,
    }

    /// <summary>
    /// Lane 10a (H-3): one recorded sales-channel decision (Canon §7.3E/§7.5).
    /// The choice machinery (policy + account set) already persisted; the
    /// decision record is what was missing — who chose, on what day, from
    /// what to what, why, and on which observed inputs. Every policy change
    /// and every account add/pause writes one entry; the history round-trips
    /// with the runtime save DTO (additive; old saves load empty). The
    /// Incident Investigator and the Business Operations Monitor read this
    /// list instead of a bare policy value. Input fields are -1 when the
    /// decision was taken without that input measured.
    /// </summary>
    [Serializable]
    public sealed class BakeryChannelDecision
    {
        /// <summary>Decider for a scenario-authored decision (config or opening).</summary>
        public const string ScenarioDecider = "scenario";

        /// <summary>Decider when no owner person is bound and no scenario context was supplied.</summary>
        public const string UnspecifiedDecider = "unspecified";

        /// <summary>Kind: the initial policy resolved once at opening.</summary>
        public const string InitialPolicyKind = "InitialPolicy";

        /// <summary>Kind: the standing policy recorded on first observation when no prior decision is on record (legacy save or unconfigured opening); the policy itself is unchanged.</summary>
        public const string StandingPolicyKind = "StandingPolicy";

        /// <summary>Kind: a policy change (morning re-evaluation or an explicit set).</summary>
        public const string PolicyChangeKind = "PolicyChange";

        /// <summary>Kind: a wholesale account added (explicit config entry or a recorded opt-in).</summary>
        public const string AccountAddedKind = "AccountAdded";

        /// <summary>Kind: a wholesale account paused (IsActive = false) by a recorded decision — a lawful transition, never a silent wipe.</summary>
        public const string AccountPausedKind = "AccountPaused";

        /// <summary>Kind: a paused wholesale account resumed.</summary>
        public const string AccountResumedKind = "AccountResumed";

        /// <summary>Kind: initial scenario-authored enabled sales channels.</summary>
        public const string InitialSalesChannelsKind = "InitialSalesChannels";

        /// <summary>Kind: an authorized owner/business decision changed enabled sales channels.</summary>
        public const string SalesChannelsChangeKind = "SalesChannelsChange";

        public int DayIndex = -1;
        public string Decider = string.Empty;
        public string Kind = string.Empty;
        public string OldValue = string.Empty;
        public string NewValue = string.Empty;
        public string Reason = string.Empty;
        public int RecordedRetailAverageUnitsPerDay = -1;
        public int OpenHouseholdBreadNeedUnits = -1;
        public int DueWholesaleUnits = -1;
        public int RetailPriceCentsPerUnit = -1;
        public int WholesalePriceCentsPerUnit = -1;

        public BakeryChannelDecision() { }

        /// <summary>One inspectable line for the Business Operations Monitor / Incident Investigator.</summary>
        public string Summary()
        {
            return $"day {DayIndex}: {Kind} ({Decider}): {OldValue} -> {NewValue} — {Reason}";
        }
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

        /// <summary>
        /// Priority spoilage integration: the condition state of the
        /// units taken from each seller lot, parallel to
        /// <see cref="SellerLotIds"/> and <see cref="UnitsByLot"/>.
        /// A default (empty-LotId) entry means that lot carried no
        /// state — the list stays parallel even for stateless lots.
        /// Only <see cref="BakeryShopRuntime.FulfillWholesale"/> fills
        /// this list; <see cref="BakeryShopRuntime.RecordWholesaleTransfer"/>
        /// (the store path) leaves it empty, because the Review2
        /// driver takes shelf units directly and its result carries
        /// only a BakeryGoodsCondition, not a state (blocked seam b).
        /// </summary>
        public List<PerishableLotState.SaveDto> PerishableConditionsByLot = new List<PerishableLotState.SaveDto>();

        /// <summary>Flour/notions/fuel provenance lines baked into the shipped loaves.</summary>
        public List<string> ProvenanceLines = new List<string>();

        /// <summary>True when the account's full order was covered; false on shortfall (loud, never faked).</summary>
        public bool FullyCovered = true;

        public BakeryWholesaleDeliveryRecord() { }

        public int TotalCents => Math.Max(0, Units) * Math.Max(0, PricePerUnitCents);
    }
}
