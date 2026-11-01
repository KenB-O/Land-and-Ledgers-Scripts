using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Phase C (Real People): structured diagnostic event kinds for the
    /// autonomous shopping loop (§26 observability, source-level only).
    /// Append-only.
    /// </summary>
    public enum ShoppingEventKind
    {
        ShopperAssignment = 0,
        SellerDiscovered = 1,
        Decision = 2,
        RejectedAlternative = 3,
        JourneyLeg = 4,
        RetailPurchase = 5,
        Settlement = 6,
        Custody = 7,
        Delivery = 8,
        Failure = 9,
        Reschedule = 10,
        Receipt = 11,
    }

    /// <summary>
    /// Phase C: one structured diagnostic event from the shopping loop. A
    /// future UI answers "what is this Person doing, why, where are they
    /// going, what do they own, how did they decide?" from these events.
    /// </summary>
    [Serializable]
    public sealed class ShoppingDiagnosticEvent
    {
        public int EventSequence;
        public int DayIndex;
        public ShoppingEventKind Kind;
        public int HouseholdId = -1;
        public int PersonId = -1;
        public int NeedSequence = -1;
        public string Summary = string.Empty;
        public string Details = string.Empty;
    }

    /// <summary>
    /// Phase C: a candidate seller the shopper considered and rejected, with
    /// the honest reason. Observability for §26: rejected alternatives are
    /// recorded, never silently dropped.
    /// </summary>
    [Serializable]
    public sealed class RejectedShoppingAlternative
    {
        public string SupplierName = string.Empty;
        public string SupplierBusinessId = string.Empty;
        public string ItemId = string.Empty;
        public int UnitPriceCents;
        public int StockUnits;
        public int TravelMinutesOneWay;
        public string Reason = string.Empty;
    }

    /// <summary>
    /// Phase C: where purchased goods physically are. Goods enter legitimate
    /// custody at purchase (in the shopper's hands, on their wagon, or under
    /// an arranged delivery) — NEVER in the household inventory until physical
    /// receipt (no teleporting goods).
    /// </summary>
    public enum GoodsCustodyState
    {
        InHand = 0,
        Wagon = 1,
        ArrangedDelivery = 2,
        ReceivedAtHousehold = 3,
    }

    /// <summary>
    /// Phase C: one batch of purchased goods in transit custody. Carries its
    /// provenance from the retail transaction to household receipt.
    /// </summary>
    [Serializable]
    public sealed class CarriedGoodsBatch
    {
        public string BatchId = string.Empty;
        public int HouseholdId = -1;
        public int HolderPersonId = -1;
        public string ItemId = string.Empty;
        public int Units;
        public int UnitPriceCents;
        public string ReceiptId = string.Empty;
        public string Provenance = string.Empty;
        public GoodsCustodyState Custody;
        public int PurchaseDayIndex;
    }

    /// <summary>
    /// Phase C: what a store actually offers for one of its stock categories.
    /// The STORE declares which real items its category units represent — this
    /// is the store's own stocking knowledge (e.g. this store's "staple_food"
    /// category is flour at 1 unit = 1 lb), replacing the Phase B transitional
    /// category→item approximation in the shopping path.
    /// </summary>
    [Serializable]
    public sealed class StoreCategoryOffer
    {
        public string CategoryId = string.Empty;
        public List<string> OfferedItemIds = new List<string>();
        /// <summary>Store units consumed per item unit sold (whole units; ≥1).</summary>
        public int StoreUnitsPerItemUnit = 1;
    }

    /// <summary>
    /// Phase C: how a person legitimately learned about a seller. Knowledge is
    /// per-person — no omniscient seller lookup. A town resident knows the
    /// suppliers in their own town; anything else needs a discovery record.
    /// </summary>
    [Serializable]
    public sealed class KnownSupplierRecord
    {
        public int PersonId = -1;
        public string SupplierBusinessId = string.Empty;
        public string HowLearned = string.Empty;
        public int LearnedDayIndex;
    }

    /// <summary>Phase C: per-person legitimate seller knowledge.</summary>
    public sealed class ShopperSellerKnowledge
    {
        private readonly List<KnownSupplierRecord> records = new List<KnownSupplierRecord>();

        public IReadOnlyList<KnownSupplierRecord> Records => records;

        public void LearnSupplier(int personId, string supplierBusinessId, string howLearned, int dayIndex)
        {
            if (personId < 0 || string.IsNullOrWhiteSpace(supplierBusinessId))
            {
                return;
            }

            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].PersonId == personId &&
                    string.Equals(records[i].SupplierBusinessId, supplierBusinessId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            records.Add(new KnownSupplierRecord
            {
                PersonId = personId,
                SupplierBusinessId = supplierBusinessId,
                HowLearned = howLearned ?? string.Empty,
                LearnedDayIndex = Math.Max(0, dayIndex),
            });
        }

        /// <summary>
        /// Seeds legitimate town knowledge: a resident knows every registered
        /// supplier whose journey location is in their town. townOf maps a
        /// supplier to its town id; the person's town comes from homeTownOf.
        /// </summary>
        public void SeedTownKnowledge(
            int personId,
            string personTownId,
            ISupplierDirectory directory,
            IEnumerable<IGoodsSupplier> allSuppliers,
            Func<IGoodsSupplier, string> townOf,
            int dayIndex,
            string howLearned = "lives in the same town")
        {
            if (personId < 0 || string.IsNullOrWhiteSpace(personTownId) || allSuppliers == null)
            {
                return;
            }

            foreach (IGoodsSupplier supplier in allSuppliers)
            {
                if (supplier == null)
                {
                    continue;
                }

                string supplierTown = townOf != null ? townOf(supplier) : string.Empty;
                if (string.Equals(supplierTown, personTownId, StringComparison.OrdinalIgnoreCase))
                {
                    LearnSupplier(personId, supplier.SupplierBusinessId, howLearned, dayIndex);
                }
            }
        }

        public bool Knows(int personId, string supplierBusinessId)
        {
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].PersonId == personId &&
                    string.Equals(records[i].SupplierBusinessId, supplierBusinessId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public List<IGoodsSupplier> KnownSuppliers(int personId, IEnumerable<IGoodsSupplier> allSuppliers)
        {
            var result = new List<IGoodsSupplier>();
            if (allSuppliers == null)
            {
                return result;
            }

            foreach (IGoodsSupplier supplier in allSuppliers)
            {
                if (supplier != null && Knows(personId, supplier.SupplierBusinessId))
                {
                    result.Add(supplier);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Phase C: a store's real trading hours. Configurable per scenario —
    /// never a universal constant. DayOfWeekIndex follows the simulation
    /// clock convention (0 = Monday .. 5 = Saturday, 6 = Sunday).
    /// </summary>
    [Serializable]
    public sealed class StoreTradingHours
    {
        public List<int> OpenDayOfWeekIndices = new List<int> { 0, 1, 2, 3, 4, 5 };
        public int OpenMinuteOfDay = 480;
        public int CloseMinuteOfDay = 1080;

        public static StoreTradingHours Default => new StoreTradingHours();

        /// <summary>Returns null when open; otherwise the honest reason it is closed.</summary>
        public string CheckOpen(int absoluteDayIndex, int minuteOfDay)
        {
            int dayOfWeek = ((absoluteDayIndex % 7) + 7) % 7;
            if (OpenDayOfWeekIndices == null || !OpenDayOfWeekIndices.Contains(dayOfWeek))
            {
                return $"closed: day-of-week index {dayOfWeek} is not a trading day";
            }

            if (minuteOfDay < OpenMinuteOfDay)
            {
                return $"closed: opens at minute {OpenMinuteOfDay}, now {minuteOfDay}";
            }

            if (minuteOfDay >= CloseMinuteOfDay)
            {
                return $"closed: closed at minute {CloseMinuteOfDay}, now {minuteOfDay}";
            }

            return null;
        }
    }

    /// <summary>
    /// Phase C: a store's real trading capability — open AND staffed. A store
    /// that is closed or unstaffed cannot trade; the shopper waits,
    /// reschedules, or pursues another legitimate option (Canon 13.4).
    /// </summary>
    public sealed class StoreTradingCapability
    {
        public StoreTradingHours Hours { get; set; } = StoreTradingHours.Default;

        /// <summary>
        /// Refreshed by the store: true when staffed enough to serve customers.
        /// </summary>
        public bool Staffed { get; set; } = true;

        /// <summary>Returns null when the store can trade now; otherwise the reason.</summary>
        public string CheckTradable(int absoluteDayIndex, int minuteOfDay)
        {
            string hoursReason = Hours != null ? Hours.CheckOpen(absoluteDayIndex, minuteOfDay) : null;
            if (hoursReason != null)
            {
                return hoursReason;
            }

            if (!Staffed)
            {
                return "closed: store is unstaffed (no clerk to serve customers)";
            }

            return null;
        }
    }

    /// <summary>Phase C: how the retail purchase settles. No informal debt — ever.</summary>
    public enum ShoppingSettlementMode
    {
        CashFromLedger = 0,
        TradeCreditObligation = 1,
    }

    /// <summary>Phase C: the money side of one retail purchase.</summary>
    [Serializable]
    public sealed class ShoppingSettlement
    {
        public ShoppingSettlementMode Mode;
        public int AmountCents;
        public string ObligationId = string.Empty;
        public string Terms = string.Empty;
    }

    /// <summary>
    /// Phase C: result of one shopping trip through the 17-step loop.
    /// Carries the conservation evidence: buyer outflow == store cash in +
    /// store receivable, every cent.
    /// </summary>
    [Serializable]
    public sealed class ShoppingTripResult
    {
        public int NeedSequence = -1;
        public int HouseholdId = -1;
        public int ShopperPersonId = -1;
        public bool Success;
        public bool Rescheduled;
        public string FailureReason = string.Empty;
        public string ItemId = string.Empty;
        public int UnitsAcquired;
        public int UnitsStillNeeded;
        public int BuyerOutflowCents;
        public int StoreCashInCents;
        public int StoreReceivableCents;
        public string Counterparty = string.Empty;
        public string ReceiptId = string.Empty;
        public string ObligationId = string.Empty;
        public int TravelMinutesTotal;
        public List<RejectedShoppingAlternative> RejectedAlternatives = new List<RejectedShoppingAlternative>();
        public List<ShoppingDiagnosticEvent> Events = new List<ShoppingDiagnosticEvent>();
    }

    /// <summary>
    /// Phase C: a supplier that resolves real items (not just categories).
    /// The shopping loop requires item resolution — a supplier that cannot say
    /// which real items it sells cannot fill an item-level purchasing need
    /// (this is what retires the Phase B transitional category→item mapping:
    /// resolution is the STORE's declared knowledge, never a global guess).
    /// </summary>
    public interface IItemResolvingSupplier : IGoodsSupplier
    {
        bool OffersItem(string itemId);
        string CategoryForItem(string itemId);
        int ItemStockUnits(string itemId);
        int ItemPricePerUnitCents(string itemId);
        /// <summary>
        /// Real trading capability: open and staffed. Null = can trade now.
        /// </summary>
        string CheckTradingCapability(int absoluteDayIndex, int minuteOfDay);
        /// <summary>
        /// Sells real item units: decrements real store stock and posts the
        /// business side (cash or receivable) through the store's own cash
        /// authority. Returns units actually sold (0 = none available).
        /// </summary>
        int SellItem(string itemId, int requestedUnits, int dayIndex, ShoppingSettlement settlement, List<string> diagnostics);
        string LastReceiptId { get; }
    }

    /// <summary>
    /// Phase C: a storefront that can arrange delivery of purchased goods.
    /// Freight is real (journey minutes, real fee) and unaffordable freight
    /// fails the candidate — never silently absorbed.
    /// </summary>
    public interface IDeliveryOfferingSupplier
    {
        bool OffersDelivery { get; }
        int DeliveryFeeCents(string itemId, int units);
        int DeliveryLeadMinutes(string fromLocationId, string toLocationId);
    }

    /// <summary>
    /// Phase C: a storefront that can sell on documented trade credit. Credit
    /// is a real FinancialObligation (TradeCredit kind) — never informal.
    /// </summary>
    public interface IOffersTradeCredit
    {
        bool OffersTradeCredit { get; }
        int TradeCreditLimitCents { get; }
    }

    /// <summary>Phase C: per-trip options for the shopping loop.</summary>
    public sealed class ShoppingLoopOptions
    {
        public Func<int, string> PersonLocationId;
        public Action<int, string> SetPersonLocation;
        public Func<int, string> PersonTownId;
        /// <summary>Minute of day the trip starts (default 600 = 10:00).</summary>
        public int DepartureMinuteOfDay = 600;
        /// <summary>Minutes the shopper spends inside the store (default 15).</summary>
        public int InStoreMinutes = 15;
        /// <summary>Override: a specific person shops; otherwise the policy chooses.</summary>
        public int ShopperPersonIdOverride = -1;
        public bool AllowTradeCredit = true;
        public bool AllowDelivery = true;
        /// <summary>Units a person can carry home on foot (default 50).</summary>
        public int CarryCapacityUnits = 50;
        public List<string> Diagnostics;
    }
}
