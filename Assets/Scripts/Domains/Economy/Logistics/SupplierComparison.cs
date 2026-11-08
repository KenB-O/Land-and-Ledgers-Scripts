// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice P5 — supplier and procurement intelligence (directive
// P5). A read-only, planning-layer comparison of supplier options and the
// replenishment plan that connects a Slice 6 recommended reserve to the
// best option. Nothing here orders, nothing reserves stock, nothing moves
// money or goods, no price changes, no invented wholesale contract, no
// free inventory, no freight bypass.
//
// WHERE EVERY FIGURE COMES FROM (no figure is invented):
//   - Unit price: the supplier's own authority — Millbrook via
//     OffMapAidBusinessRuntime.GetOrderPriceCents (48c per sack in the
//     authored table), the catalog via OffMapSupplierCatalog.QuoteAll
//     (GoodsCents for the quoted quantity), the named-SKU route via
//     NamedSkuOrder.UnitGoodsCostCents. A supplier that quotes no price
//     for the item gives an Unknown price, never a guessed one.
//   - Freight: the actual freight authority computation as it exists in
//     source — either a settled LogisticsShipmentState.FreightChargeCents
//     (the chain settles 125c exactly once for a 16-sack haul), or an
//     OffMapFreightQuote.FreightCents from the catalog (distance miles
//     times a caller-supplied cents-per-mile rate; the catalog's own doc
//     says the rate is caller-supplied, never a canon rate), or a
//     NamedSkuOrder.FreightSurchargeCents. The 25c dispatch and 1c-per-
//     mile figures are EXCLUDED as Canon (EXC-07, EXC-08) and are never
//     recomputed here: the 125c in the chain fixture is the quote's own
//     TotalPriceCents as recorded, read back from the shipment.
//   - Quantity available: Known only where an authority states it (a
//     named-SKU order's own UnitsOrdered). Millbrook quotes a price per
//     sack; the fillable quantity is decided at order time by
//     TryPlaceOrder (OffMapAidBusinessRuntime.cs), which refuses when
//     stock is short — no pre-order quantity quote exists, so the option
//     carries Unknown availability and the plan says so. The catalog
//     likewise quotes a price for a requested quantity and carries no
//     stock figure, so its availability is Unknown too.
//   - Arrival: the caller's lead time (no authored lead time exists
//     anywhere in v3.1) plus the as-of day, or a named-SKU order's own
//     ExpectedArrivalDayIndex. Observed F3 history can only flag that
//     the average recorded transit exceeds the lead time; it never
//     replaces the lead time.
//   - Reliability: the F3 custody log, per supplier authority id
//     ("millbrook:<id>" / "named-sku:<id>"): committed freight
//     shipments, received shipments, on-time arrivals (transit within
//     the caller-supplied lead time), average transit. No history means
//     Unknown reliability, never assumed reliable. Lateness cannot be
//     judged without a lead time, and the derivation says so.
//   - Storage fit: the caller supplies availability in the same units
//     as the option quantity, computed from the F2 BusinessStorage leg
//     (Total minus Occupied). This type compares numbers; it does not
//     convert units and it does not reference the F2 types (the F2
//     registry row stays honest — see the file footer).
//   - Expected spoilage: the Slice 5 rule as the Slice 6 reserve uses
//     it — a lot past its useful life (remaining life Known and zero or
//     less) or in an Unsafe safety state counts as spoiled; Suspect and
//     Stale lots are counted, not spoiled, because no authored rule
//     discounts them. The loss in cents is spoiled units times the
//     recorded revenue per unit; with no recorded revenue the loss is
//     Unknown.
//   - Available cash: the business's own cash records, supplied by the
//     caller (for example BusinessRuntimeState.CurrentCashCents). This
//     type reads no cash authority itself.
//   - Revenue: the actually recorded retail price per unit, or a revenue
//     per order unit derived from recorded figures with the derivation
//     named in RevenueBasis (for example 100c per flour sack: the
//     recorded bake converts 12 lb to 24 loaves, so a 10 lb sack bakes
//     20 loaves, at the recorded 5c loaf price). No 6c or 8c bread price
//     is used (EXC-05).
//
// RANKING RULE, stated once so any reader can check it: comparable
// options (expected profit Known) rank by expected profit, highest
// first — a cheaper unit price that loses money after freight and
// spoilage ranks below a dearer option that profits. Ties break by
// reliability (on-time ratio, else received ratio, else no history
// last), then earlier expected arrival, then lower freight. Options
// whose profit is Unknown are listed after the ranked ones, unranked,
// with the missing input named — incomparability is per axis: an
// Unknown lead time makes the arrival axis Unknown (the option still
// ranks on profit), Unknown reliability makes the reliability axis
// Unknown, and an Unknown profit input (price, freight, revenue, or
// quantity) makes the profit axis Unknown. Unknown spoilage is
// EXCLUDED from the profit (like the Slice 6 Unknown-exclusion rule)
// and flagged ProfitExcludesUnmeasuredSpoilage, so the profit is
// explicitly "profit before unmeasured spoilage", never a silent zero.
// Consolidation is considered only where quoted: an unquoted
// consolidation is never assumed, and no authority quotes a
// consolidation discount, so consolidation changes no figure.
// A SelfCollect catalog quote is never selected by a plan: its freight
// is 0c in the quote but a journey is required and the quote books
// none, so no arrival can be computed.
//
// UNWIRED BY DESIGN: plain data plus pure functions. No flow consults
// this type, no driver is activated, no shopping loop, Bakery driver,
// or live flow is touched. This file references PerishableLotState,
// CustodyTransferLog, DemandObservation-adjacent ReserveProjection
// types (LeadTimeDays, ReserveQuantity) only from inside its own
// owning directory (Domains/Economy/Logistics), so no earlier
// UnwiredSystems.md row is voided; it references the F1/F2 types
// nowhere (the F1 registry and the F2 evaluator rows stay), and it
// references no person-inventory type (the Slice 3 row stays).

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;

namespace LandLedgers.Economy.Logistics
{
    /// <summary>
    /// An amount of money in cents that is Known or Unknown. Unknown is
    /// the honest answer where no authority supplies a figure — never a
    /// stored zero. A negative amount is a genuine figure (an expected
    /// profit may be a loss); only the absence of a figure is Unknown.
    /// </summary>
    public struct CentsAmount
    {
        private readonly bool isKnown;
        private readonly int cents;

        public CentsAmount(bool known, int cents)
        {
            isKnown = known;
            this.cents = known ? cents : 0;
        }

        public bool IsKnown => isKnown;
        public int Cents => IsKnown ? cents : 0;

        public static CentsAmount Unknown => new CentsAmount(false, 0);
        public static CentsAmount Known(int cents) => new CentsAmount(true, cents);
    }

    /// <summary>
    /// Whether one option's quantity fits the available storage, as
    /// computed by the caller from the F2 BusinessStorage leg (Total
    /// minus Occupied) in the same units as the option quantity. Unknown
    /// where the availability or the quantity is Unknown — never an
    /// assumed fit.
    /// </summary>
    public struct SupplierStorageFit
    {
        public SupplierStorageFit(bool isKnown, bool fits, ReserveQuantity shortfallUnits, string reason)
        {
            IsKnown = isKnown;
            Fits = isKnown && fits;
            ShortfallUnits = shortfallUnits;
            Reason = reason ?? string.Empty;
        }

        public bool IsKnown { get; }
        public bool Fits { get; }
        public ReserveQuantity ShortfallUnits { get; }
        public string Reason { get; }

        public static SupplierStorageFit Unknown(string reason) =>
            new SupplierStorageFit(false, false, ReserveQuantity.Unknown,
                string.IsNullOrWhiteSpace(reason) ? "storage fit unknown — no storage check was supplied." : reason);

        /// <summary>
        /// Compares a quantity against available storage, both in the
        /// same units (the caller keeps units coherent; this type never
        /// converts units — no authored conversion exists). The need is
        /// reported, not silently capped: a shortfall names its size.
        /// </summary>
        public static SupplierStorageFit FromAvailableStorage(
            ReserveQuantity availableUnits, ReserveQuantity quantityUnits)
        {
            if (!availableUnits.IsKnown)
            {
                return Unknown("storage fit unknown — available storage is unknown, so the fit cannot be checked.");
            }
            if (!quantityUnits.IsKnown)
            {
                return Unknown("storage fit unknown — the option quantity is unknown, so the fit cannot be checked.");
            }
            if (quantityUnits.Units <= availableUnits.Units)
            {
                return new SupplierStorageFit(true, true, ReserveQuantity.Known(0),
                    $"storage fits: {quantityUnits.Units} unit(s) against {availableUnits.Units} available.");
            }
            int shortfall = quantityUnits.Units - availableUnits.Units;
            return new SupplierStorageFit(true, false, ReserveQuantity.Known(shortfall),
                $"storage does not fit: {quantityUnits.Units} unit(s) against {availableUnits.Units} available — shortfall {shortfall} unit(s), reported not capped.");
        }
    }

    /// <summary>
    /// One supplier's delivery reliability, derived from the F3 custody
    /// log's freight records for that supplier's lot authority. Unknown
    /// where the log holds no freight history for the supplier — never
    /// assumed reliable. On-time means a dated completed transit within
    /// the caller-supplied lead time; without a lead time, lateness
    /// cannot be judged and OnTimeShipments is Unknown.
    /// </summary>
    public struct SupplierReliability
    {
        public SupplierReliability(bool isKnown, int committedShipments, int receivedShipments,
            ReserveQuantity onTimeShipments, LeadTimeDays averageTransitDays, string reason)
        {
            IsKnown = isKnown;
            CommittedShipments = committedShipments;
            ReceivedShipments = receivedShipments;
            OnTimeShipments = onTimeShipments;
            AverageTransitDays = averageTransitDays;
            Reason = reason ?? string.Empty;
        }

        public bool IsKnown { get; }
        public int CommittedShipments { get; }
        public int ReceivedShipments { get; }
        public ReserveQuantity OnTimeShipments { get; }
        public LeadTimeDays AverageTransitDays { get; }
        public string Reason { get; }

        public static SupplierReliability Unknown => new SupplierReliability(false, 0, 0,
            ReserveQuantity.Unknown, LeadTimeDays.Unknown,
            "reliability unknown — no freight history was supplied for this supplier, so none is assumed.");

        /// <summary>Share of committed shipments received, or NaN with no history.</summary>
        public double ReceivedRatio => CommittedShipments > 0
            ? (double)ReceivedShipments / CommittedShipments : double.NaN;

        /// <summary>Share of received shipments on time, or NaN where lateness cannot be judged.</summary>
        public double OnTimeRatio => OnTimeShipments.IsKnown && ReceivedShipments > 0
            ? (double)OnTimeShipments.Units / ReceivedShipments : double.NaN;

        /// <summary>
        /// Derives reliability from the F3 log: freight-commit records
        /// whose source or destination lot authority is the supplier's
        /// authority, joined to freight-receive records by the shipment
        /// idempotency key (CauseId). A commit with no matching receive
        /// counts as committed but not received — it may still be in
        /// transit, and the reason says so rather than calling it late.
        /// Dateless records (DayIndex -1, the delegate-path gap) cannot
        /// contribute a transit time and are named in the reason.
        /// </summary>
        public static SupplierReliability FromCustodyLog(
            CustodyTransferLog log, string supplierAuthorityId, LeadTimeDays expectedLeadTime)
        {
            if (log == null)
            {
                return new SupplierReliability(false, 0, 0, ReserveQuantity.Unknown, LeadTimeDays.Unknown,
                    "reliability unknown — no custody log was supplied, so no history can be read.");
            }
            if (string.IsNullOrWhiteSpace(supplierAuthorityId))
            {
                return new SupplierReliability(false, 0, 0, ReserveQuantity.Unknown, LeadTimeDays.Unknown,
                    "reliability unknown — no supplier authority id was supplied, so no history can be attributed.");
            }

            var commits = new List<CustodyTransferRecord>();
            foreach (CustodyTransferRecord record in log.Records)
            {
                if (record == null) continue;
                if (!string.Equals(record.CauseKind, CustodyTransferLog.CauseFreightCommit, StringComparison.Ordinal)) continue;
                if (string.Equals(record.SourceLotKey.AuthorityId, supplierAuthorityId, StringComparison.Ordinal)
                    || string.Equals(record.DestinationLotKey.AuthorityId, supplierAuthorityId, StringComparison.Ordinal))
                {
                    commits.Add(record);
                }
            }
            if (commits.Count == 0)
            {
                return new SupplierReliability(false, 0, 0, ReserveQuantity.Unknown, LeadTimeDays.Unknown,
                    $"reliability unknown — no freight history for authority '{supplierAuthorityId}' in the custody log, so none is assumed.");
            }

            int received = 0;
            int onTime = 0;
            int datedPairs = 0;
            int datelessPairs = 0;
            double transitSum = 0.0;
            foreach (CustodyTransferRecord commit in commits)
            {
                CustodyTransferRecord receive = log.LatestForCause(
                    CustodyTransferLog.CauseFreightReceive, commit.CauseId);
                if (receive == null) continue;
                received++;
                if (commit.DayIndex < 0 || receive.DayIndex < 0)
                {
                    datelessPairs++;
                    continue;
                }
                double transit = receive.DayIndex - commit.DayIndex;
                if (transit < 0.0) continue;
                datedPairs++;
                transitSum += transit;
                if (expectedLeadTime.IsKnown && transit <= expectedLeadTime.Days) onTime++;
            }

            LeadTimeDays averageTransit = datedPairs > 0
                ? LeadTimeDays.Known(transitSum / datedPairs) : LeadTimeDays.Unknown;
            ReserveQuantity onTimeCount = expectedLeadTime.IsKnown && datedPairs > 0
                ? ReserveQuantity.Known(onTime) : ReserveQuantity.Unknown;
            string reason = $"{committedShipmentsText(commits.Count)} committed, {received} received";
            reason += onTimeCount.IsKnown
                ? $", {onTime} on time against a {expectedLeadTime.Days}-day lead"
                : " — lateness cannot be judged (no lead time was supplied, or no dated completed transit exists)";
            reason += averageTransit.IsKnown
                ? $", average transit {averageTransit.Days:0.##} day(s)."
                : ", no dated completed transit, so no average transit can be computed.";
            if (datelessPairs > 0)
            {
                reason += $" {datelessPairs} received shipment(s) carry no day, so they contribute no transit time.";
            }
            if (received < commits.Count)
            {
                reason += $" {commits.Count - received} committed shipment(s) have no receive record — they may still be in transit, not late.";
            }
            return new SupplierReliability(true, commits.Count, received,
                onTimeCount, averageTransit, reason);
        }

        private static string committedShipmentsText(int count) =>
            count + " shipment(s)";
    }

    /// <summary>
    /// An expected spoilage forecast for a set of lots: how many units
    /// the Slice 5 rule counts as spoiled, and the loss in cents at the
    /// recorded revenue per unit. A struct, like the other small
    /// assessment records in this layer.
    /// </summary>
    public struct SpoilageForecast
    {
        public SpoilageForecast(ReserveQuantity spoiledUnits, CentsAmount lossCents, string reason)
        {
            SpoiledUnits = spoiledUnits;
            LossCents = lossCents;
            Reason = reason ?? string.Empty;
        }

        public ReserveQuantity SpoiledUnits { get; }
        public CentsAmount LossCents { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// One supplier's quote for one item, as its authority actually
    /// states it — every input named, every Unknown an honest Unknown.
    /// A struct: a small value record, like the other quote records in
    /// this layer. Quantities are in the item's order units (for
    /// example sacks); money is in cents.
    /// </summary>
    public struct SupplierOption
    {
        public SupplierOption(string supplierId, string itemId,
            ReserveQuantity quantityUnits, ReserveQuantity quantityAvailable,
            CentsAmount unitPriceCents, string unitPriceSource,
            CentsAmount freightCostCents, string freightSource,
            bool consolidatedShipmentQuoted, bool consolidatedShipment,
            LeadTimeDays leadTimeDays, ReserveQuantity quotedArrivalDay,
            SupplierReliability reliability, SupplierStorageFit storageFit,
            ReserveQuantity expectedSpoilageUnits, CentsAmount revenuePerUnitCents,
            string revenueBasis, CentsAmount availableCashCents, bool isSelfCollect)
        {
            SupplierId = supplierId ?? string.Empty;
            ItemId = itemId ?? string.Empty;
            QuantityUnits = quantityUnits;
            QuantityAvailable = quantityAvailable;
            UnitPriceCents = unitPriceCents;
            UnitPriceSource = unitPriceSource ?? string.Empty;
            FreightCostCents = freightCostCents;
            FreightSource = freightSource ?? string.Empty;
            ConsolidatedShipmentQuoted = consolidatedShipmentQuoted;
            ConsolidatedShipment = consolidatedShipment;
            LeadTimeDays = leadTimeDays;
            QuotedArrivalDay = quotedArrivalDay;
            Reliability = reliability;
            StorageFit = storageFit;
            ExpectedSpoilageUnits = expectedSpoilageUnits;
            RevenuePerUnitCents = revenuePerUnitCents;
            RevenueBasis = revenueBasis ?? string.Empty;
            AvailableCashCents = availableCashCents;
            IsSelfCollect = isSelfCollect;
        }

        public string SupplierId { get; }
        public string ItemId { get; }
        public ReserveQuantity QuantityUnits { get; }
        public ReserveQuantity QuantityAvailable { get; }
        public CentsAmount UnitPriceCents { get; }
        public string UnitPriceSource { get; }
        public CentsAmount FreightCostCents { get; }
        public string FreightSource { get; }
        public bool ConsolidatedShipmentQuoted { get; }
        public bool ConsolidatedShipment { get; }
        public LeadTimeDays LeadTimeDays { get; }
        public ReserveQuantity QuotedArrivalDay { get; }
        public SupplierReliability Reliability { get; }
        public SupplierStorageFit StorageFit { get; }
        public ReserveQuantity ExpectedSpoilageUnits { get; }
        public CentsAmount RevenuePerUnitCents { get; }
        public string RevenueBasis { get; }
        public CentsAmount AvailableCashCents { get; }
        public bool IsSelfCollect { get; }

        /// <summary>Goods cost at the quoted quantity, or Unknown where price or quantity is Unknown.</summary>
        public CentsAmount ExpectedGoodsCostCents => UnitPriceCents.IsKnown && QuantityUnits.IsKnown
            ? CentsAmount.Known(UnitPriceCents.Cents * QuantityUnits.Units) : CentsAmount.Unknown;

        /// <summary>Expected revenue at the quoted quantity, or Unknown where revenue or quantity is Unknown.</summary>
        public CentsAmount ExpectedRevenueCents => RevenuePerUnitCents.IsKnown && QuantityUnits.IsKnown
            ? CentsAmount.Known(RevenuePerUnitCents.Cents * QuantityUnits.Units) : CentsAmount.Unknown;

        /// <summary>Expected spoilage loss in cents, or Unknown where spoilage or revenue is Unknown.</summary>
        public CentsAmount ExpectedSpoilageLossCents => ExpectedSpoilageUnits.IsKnown && RevenuePerUnitCents.IsKnown
            ? CentsAmount.Known(ExpectedSpoilageUnits.Units * RevenuePerUnitCents.Cents) : CentsAmount.Unknown;

        /// <summary>Goods plus freight at the quoted quantity, or Unknown where either is Unknown.</summary>
        public CentsAmount ExpectedTotalCostCents => ExpectedGoodsCostCents.IsKnown && FreightCostCents.IsKnown
            ? CentsAmount.Known(ExpectedGoodsCostCents.Cents + FreightCostCents.Cents) : CentsAmount.Unknown;

        /// <summary>
        /// True when the profit below was computed without a spoilage
        /// forecast: Unknown spoilage is excluded, never silently counted
        /// as zero, so the profit is "profit before unmeasured spoilage".
        /// </summary>
        public bool ProfitExcludesUnmeasuredSpoilage => !ExpectedSpoilageUnits.IsKnown;

        /// <summary>
        /// Expected profit: revenue minus goods minus freight minus
        /// expected spoilage loss. Unknown where unit price, freight,
        /// revenue, or quantity is Unknown — the option is then
        /// incomparable on profit. Unknown spoilage is excluded from
        /// the figure (see ProfitExcludesUnmeasuredSpoilage).
        /// </summary>
        public CentsAmount ExpectedProfitCents
        {
            get
            {
                if (!ExpectedRevenueCents.IsKnown || !ExpectedGoodsCostCents.IsKnown
                    || !FreightCostCents.IsKnown)
                {
                    return CentsAmount.Unknown;
                }
                int spoilageLoss = ExpectedSpoilageLossCents.IsKnown ? ExpectedSpoilageLossCents.Cents : 0;
                return CentsAmount.Known(ExpectedRevenueCents.Cents - ExpectedGoodsCostCents.Cents
                    - FreightCostCents.Cents - spoilageLoss);
            }
        }

        /// <summary>
        /// The expected arrival day: a quoted arrival day when the
        /// authority states one (a named-SKU order), else the as-of day
        /// plus the caller-supplied lead time, else Unknown. Self-collect
        /// has no arrival: the quote books no journey. Never invented.
        /// </summary>
        public ReserveQuantity ExpectedArrivalDay(int asOfDay)
        {
            if (IsSelfCollect) return ReserveQuantity.Unknown;
            if (QuotedArrivalDay.IsKnown) return QuotedArrivalDay;
            if (LeadTimeDays.IsKnown && asOfDay >= 0)
            {
                return ReserveQuantity.Known(asOfDay + (int)Math.Ceiling(LeadTimeDays.Days));
            }
            return ReserveQuantity.Unknown;
        }

        /// <summary>Whether affordability can be judged (cost and cash both Known).</summary>
        public bool IsAffordableKnown => ExpectedTotalCostCents.IsKnown && AvailableCashCents.IsKnown;

        /// <summary>Whether the quoted order fits the available cash (false where affordability is Unknown).</summary>
        public bool IsAffordable => IsAffordableKnown
            && ExpectedTotalCostCents.Cents <= AvailableCashCents.Cents;

        /// <summary>
        /// Builds an option from the Millbrook authority: the unit price
        /// is the table's own GetOrderPriceCents quote. The freight
        /// charge, lead time, reliability, storage fit, spoilage
        /// forecast, revenue basis, and cash are caller-supplied — this
        /// type reads no other authority for them. Quantity available is
        /// Unknown: Millbrook quotes a price per sack and the fillable
        /// quantity is decided at order time by TryPlaceOrder, which
        /// refuses when stock is short; no pre-order quantity quote
        /// exists. A null runtime gives an Unknown price, never a
        /// guessed supplier or price.
        /// </summary>
        public static SupplierOption FromMillbrook(
            OffMapAidBusinessRuntime runtime, string itemId, ReserveQuantity quantityUnits,
            CentsAmount freightCostCents, string freightSource,
            LeadTimeDays leadTimeDays, SupplierReliability reliability, SupplierStorageFit storageFit,
            ReserveQuantity expectedSpoilageUnits, CentsAmount revenuePerUnitCents,
            string revenueBasis, CentsAmount availableCashCents)
        {
            string supplierId = runtime != null ? runtime.BusinessId : string.Empty;
            CentsAmount price;
            string priceSource;
            if (runtime == null)
            {
                price = CentsAmount.Unknown;
                priceSource = "no Millbrook runtime supplied — no price can be quoted.";
            }
            else
            {
                int quoted = runtime.GetOrderPriceCents(itemId);
                if (quoted <= 0)
                {
                    price = CentsAmount.Unknown;
                    priceSource = $"the Millbrook table quotes no price for '{itemId}'.";
                }
                else
                {
                    price = CentsAmount.Known(quoted);
                    priceSource = "Millbrook table (OffMapAidBusinessRuntime.GetOrderPriceCents).";
                }
            }
            return new SupplierOption(
                supplierId, itemId ?? string.Empty, quantityUnits, ReserveQuantity.Unknown,
                price, priceSource, freightCostCents,
                string.IsNullOrWhiteSpace(freightSource) ? "freight source not named." : freightSource,
                false, false, leadTimeDays, ReserveQuantity.Unknown,
                reliability, storageFit, expectedSpoilageUnits,
                revenuePerUnitCents, revenueBasis ?? string.Empty, availableCashCents, false);
        }

        /// <summary>
        /// Builds an option from a catalog quote: the unit price is the
        /// quote's goods total over its quantity, the freight is the
        /// quote's own freight figure (distance times the
        /// caller-supplied rate the catalog was asked with). Quantity
        /// available is Unknown — the catalog quotes a price for a
        /// requested quantity and carries no stock figure. Consolidation
        /// is not quoted by the catalog, so it is never assumed. A
        /// SelfCollect quote records IsSelfCollect: its freight is 0c in
        /// the quote, but a journey is required and the quote books none.
        /// </summary>
        public static SupplierOption FromCatalogQuote(
            OffMapFreightQuote quote, LeadTimeDays leadTimeDays, SupplierReliability reliability,
            SupplierStorageFit storageFit, ReserveQuantity expectedSpoilageUnits,
            CentsAmount revenuePerUnitCents, string revenueBasis, CentsAmount availableCashCents)
        {
            if (quote == null) throw new ArgumentNullException(nameof(quote), "a catalog quote is required; no quote, no option.");
            bool selfCollect = quote.FreightOption == FreightOptionKind.SelfCollect;
            CentsAmount price = quote.QuantityUnits > 0
                ? CentsAmount.Known(quote.GoodsCents / Math.Max(1, quote.QuantityUnits)) : CentsAmount.Unknown;
            string priceSource = $"catalog quote (OffMapSupplierCatalog.QuoteAll): goods {quote.GoodsCents}c for {quote.QuantityUnits} unit(s).";
            string freightSource = selfCollect
                ? "self-collect quote: freight 0c in the quote, but a journey is required and the quote books none."
                : $"catalog quote freight: {quote.DistanceMiles} miles at the caller-supplied rate ({quote.FreightCents}c).";
            return new SupplierOption(
                quote.SupplierId ?? string.Empty, quote.SkuId ?? string.Empty,
                ReserveQuantity.Known(Math.Max(0, quote.QuantityUnits)), ReserveQuantity.Unknown,
                price, priceSource, CentsAmount.Known(Math.Max(0, quote.FreightCents)), freightSource,
                false, false, leadTimeDays, ReserveQuantity.Unknown,
                reliability, storageFit, expectedSpoilageUnits,
                revenuePerUnitCents, revenueBasis ?? string.Empty, availableCashCents, selfCollect);
        }

        /// <summary>
        /// Builds an option from a named-SKU order: the unit price is
        /// the order's own goods price, the freight is the order's
        /// seller-paid surcharge, the quantity and the availability are
        /// the order's own quantity, and the arrival is the order's own
        /// expected arrival day. Consolidation is not quoted for a
        /// named-SKU order, so it is never assumed.
        /// </summary>
        public static SupplierOption FromNamedSkuOrder(
            NamedSkuOrder order, LeadTimeDays leadTimeDays, SupplierReliability reliability,
            SupplierStorageFit storageFit, ReserveQuantity expectedSpoilageUnits,
            CentsAmount revenuePerUnitCents, string revenueBasis, CentsAmount availableCashCents)
        {
            if (order == null) throw new ArgumentNullException(nameof(order), "a named-SKU order is required; no order, no option.");
            CentsAmount price = order.UnitGoodsCostCents > 0
                ? CentsAmount.Known(order.UnitGoodsCostCents) : CentsAmount.Unknown;
            string priceSource = price.IsKnown
                ? "named-SKU order (OffMapNamedSkuProcurementService): the order's own goods price."
                : $"the named-SKU order '{order.OrderId}' carries no goods price.";
            CentsAmount freight = order.FreightSurchargeCents >= 0
                ? CentsAmount.Known(order.FreightSurchargeCents) : CentsAmount.Unknown;
            string freightSource = freight.IsKnown
                ? "named-SKU order: the order's seller-paid freight surcharge."
                : $"the named-SKU order '{order.OrderId}' carries no freight surcharge.";
            ReserveQuantity arrival = order.ExpectedArrivalDayIndex >= 0
                ? ReserveQuantity.Known(order.ExpectedArrivalDayIndex) : ReserveQuantity.Unknown;
            return new SupplierOption(
                order.SupplierId ?? string.Empty, order.SkuId ?? string.Empty,
                ReserveQuantity.Known(Math.Max(0, order.UnitsOrdered)),
                ReserveQuantity.Known(Math.Max(0, order.UnitsOrdered)),
                price, priceSource, freight, freightSource,
                false, false, leadTimeDays, arrival,
                reliability, storageFit, expectedSpoilageUnits,
                revenuePerUnitCents, revenueBasis ?? string.Empty, availableCashCents, false);
        }

        /// <summary>
        /// Reads the freight figure from a shipment: the settled
        /// LogisticsShipmentState.FreightChargeCents. A null shipment
        /// gives Unknown; a zero charge is reported as a zero charge
        /// with the warning that the authority does not distinguish a
        /// free haul from an unset charge.
        /// </summary>
        public static CentsAmount FreightFromShipment(
            LogisticsShipmentState shipment, out string reason)
        {
            if (shipment == null)
            {
                reason = "freight unknown — no shipment was supplied, so no settled freight charge can be read.";
                return CentsAmount.Unknown;
            }
            int charge = shipment.FreightChargeCents;
            reason = charge > 0
                ? $"settled charge on shipment '{shipment.ShipmentId}' (LogisticsShipmentState.FreightChargeCents)."
                : $"shipment '{shipment.ShipmentId}' carries a zero freight charge (free or unset — the authority does not distinguish).";
            return CentsAmount.Known(charge);
        }

        /// <summary>
        /// Forecasts expected spoilage across a set of Slice 5 lot
        /// states, by the Slice 5 rule as the Slice 6 reserve uses it: a
        /// lot past its useful life (remaining life Known and zero or
        /// less) or in an Unsafe safety state counts as spoiled; every
        /// other lot counts as sellable. The loss in cents is spoiled
        /// units times the recorded revenue per unit; with no recorded
        /// revenue the units are still counted but the loss is Unknown.
        /// A null lot entry counts as zero. Pure: the states are never
        /// mutated.
        /// </summary>
        public static SpoilageForecast ExpectedSpoilageFromLots(
            IEnumerable<PerishableLotState> lots, CentsAmount revenuePerUnitCents, out string reason)
        {
            reason = string.Empty;
            if (lots == null)
            {
                reason = "no lots supplied — expected spoilage is unknown, not zero.";
                return new SpoilageForecast(ReserveQuantity.Unknown, CentsAmount.Unknown, reason);
            }
            int spoiled = 0;
            int pastLife = 0;
            int unsafeUnits = 0;
            foreach (PerishableLotState lot in lots)
            {
                if (lot == null) continue;
                if (lot.SafetyState == PerishableSafetyState.Unsafe)
                {
                    spoiled += lot.Units;
                    unsafeUnits += lot.Units;
                    continue;
                }
                if (lot.RemainingUsefulLife.IsKnown && lot.RemainingUsefulLife.Days <= 0.0)
                {
                    spoiled += lot.Units;
                    pastLife += lot.Units;
                }
            }
            CentsAmount loss = revenuePerUnitCents.IsKnown
                ? CentsAmount.Known(spoiled * revenuePerUnitCents.Cents) : CentsAmount.Unknown;
            reason = $"{spoiled} spoiled unit(s) expected: {pastLife} past its useful life, {unsafeUnits} unsafe. "
                + "Suspect and stale lots are counted, not spoiled — no authored rule discounts them. "
                + (loss.IsKnown
                    ? $"Loss {loss.Cents}c at {revenuePerUnitCents.Cents}c per unit of recorded revenue."
                    : "No recorded revenue per unit was supplied, so the spoilage loss in cents is unknown.");
            return new SpoilageForecast(ReserveQuantity.Known(spoiled), loss, reason);
        }
    }

    /// <summary>
    /// One option as ranked (or left unranked) by a comparison: its
    /// rank among comparable options (1-based; 0 means incomparable on
    /// profit and listed after the ranked ones), its expected profit,
    /// its expected arrival for the comparison's as-of day, and the
    /// reasoning that produced the rank — never a bare ranking.
    /// </summary>
    public struct RankedSupplierOption
    {
        public RankedSupplierOption(SupplierOption option, int rank, bool isComparableOnProfit,
            CentsAmount expectedProfitCents, ReserveQuantity expectedArrivalDay,
            IReadOnlyList<string> reasoning)
        {
            Option = option;
            Rank = rank;
            IsComparableOnProfit = isComparableOnProfit;
            ExpectedProfitCents = expectedProfitCents;
            ExpectedArrivalDay = expectedArrivalDay;
            Reasoning = reasoning ?? new List<string>().AsReadOnly();
        }

        public SupplierOption Option { get; }
        public int Rank { get; }
        public bool IsComparableOnProfit { get; }
        public CentsAmount ExpectedProfitCents { get; }
        public ReserveQuantity ExpectedArrivalDay { get; }
        public IReadOnlyList<string> Reasoning { get; }
    }

    /// <summary>
    /// The result of one comparison: the ranked options (comparable
    /// first, incomparable after, in input order) and the diagnostics
    /// (excluded options, empty input). A struct, like the other small
    /// result records in this layer.
    /// </summary>
    public struct SupplierComparisonResult
    {
        public SupplierComparisonResult(string itemId,
            IReadOnlyList<RankedSupplierOption> ranked, IReadOnlyList<string> diagnostics)
        {
            ItemId = itemId ?? string.Empty;
            Ranked = ranked ?? new List<RankedSupplierOption>().AsReadOnly();
            Diagnostics = diagnostics ?? new List<string>().AsReadOnly();
        }

        public string ItemId { get; }
        public IReadOnlyList<RankedSupplierOption> Ranked { get; }
        public IReadOnlyList<string> Diagnostics { get; }
    }

    /// <summary>
    /// Read-only supplier comparison. Compare ranks options for one
    /// item by the rule in the file header (expected profit first, then
    /// reliability, arrival, freight) and names the reasoning per
    /// option. Pure: the options are never mutated, and no authority is
    /// read or written.
    /// </summary>
    public sealed class SupplierComparison
    {
        private SupplierComparison()
        {
        }

        /// <summary>
        /// Ranks the options for one item as of one day. Options for
        /// another item, and options with no supplier id, are excluded
        /// and named in diagnostics. Options whose expected profit is
        /// Unknown are listed after the ranked ones with rank 0 and the
        /// missing input named.
        /// </summary>
        public static SupplierComparisonResult Compare(
            string itemId, IEnumerable<SupplierOption> options, int asOfDay)
        {
            var diagnostics = new List<string>();
            var ranked = new List<RankedSupplierOption>();
            var incomparable = new List<RankedSupplierOption>();
            if (options == null)
            {
                diagnostics.Add("no options supplied — nothing can be compared.");
                return new SupplierComparisonResult(itemId, ranked, diagnostics);
            }
            foreach (SupplierOption option in options)
            {
                if (!string.Equals(option.ItemId, itemId, StringComparison.Ordinal))
                {
                    diagnostics.Add($"option from supplier '{option.SupplierId}' is for item '{option.ItemId}' — excluded from the '{itemId}' comparison.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(option.SupplierId))
                {
                    diagnostics.Add("an option with no supplier id was excluded — a quote with no supplier cannot be acted on.");
                    continue;
                }
                List<string> reasoning = BuildReasoning(option, asOfDay);
                var entry = new RankedSupplierOption(option, 0, option.ExpectedProfitCents.IsKnown,
                    option.ExpectedProfitCents, option.ExpectedArrivalDay(asOfDay), reasoning);
                if (entry.IsComparableOnProfit)
                {
                    ranked.Add(entry);
                }
                else
                {
                    incomparable.Add(entry);
                }
            }
            ranked.Sort(CompareRanked);
            var ordered = new List<RankedSupplierOption>();
            for (int i = 0; i < ranked.Count; i++)
            {
                RankedSupplierOption entry = ranked[i];
                ordered.Add(new RankedSupplierOption(entry.Option, i + 1, true,
                    entry.ExpectedProfitCents, entry.ExpectedArrivalDay, entry.Reasoning));
            }
            ordered.AddRange(incomparable);
            return new SupplierComparisonResult(itemId, ordered, diagnostics);
        }

        private static int CompareRanked(RankedSupplierOption a, RankedSupplierOption b)
        {
            int byProfit = b.ExpectedProfitCents.Cents.CompareTo(a.ExpectedProfitCents.Cents);
            if (byProfit != 0) return byProfit;
            double scoreA = ReliabilityScore(a.Option);
            double scoreB = ReliabilityScore(b.Option);
            int byReliability = scoreB.CompareTo(scoreA);
            if (byReliability != 0) return byReliability;
            int arrivalA = a.ExpectedArrivalDay.IsKnown ? a.ExpectedArrivalDay.Units : int.MaxValue;
            int arrivalB = b.ExpectedArrivalDay.IsKnown ? b.ExpectedArrivalDay.Units : int.MaxValue;
            int byArrival = arrivalA.CompareTo(arrivalB);
            if (byArrival != 0) return byArrival;
            int freightA = a.Option.FreightCostCents.IsKnown ? a.Option.FreightCostCents.Cents : int.MaxValue;
            int freightB = b.Option.FreightCostCents.IsKnown ? b.Option.FreightCostCents.Cents : int.MaxValue;
            return freightA.CompareTo(freightB);
        }

        private static double ReliabilityScore(SupplierOption option)
        {
            SupplierReliability reliability = option.Reliability;
            if (!double.IsNaN(reliability.OnTimeRatio)) return reliability.OnTimeRatio;
            if (!double.IsNaN(reliability.ReceivedRatio)) return reliability.ReceivedRatio;
            return -1.0;
        }

        private static List<string> BuildReasoning(SupplierOption option, int asOfDay)
        {
            var reasoning = new List<string>();
            reasoning.Add($"supplier '{option.SupplierId}' quotes {(option.QuantityUnits.IsKnown ? option.QuantityUnits.Units.ToString() : "an unknown quantity of")} unit(s) of '{option.ItemId}'.");
            if (option.UnitPriceCents.IsKnown)
            {
                reasoning.Add($"unit price {option.UnitPriceCents.Cents}c per unit, quoted by {option.UnitPriceSource}");
            }
            else
            {
                reasoning.Add($"unit price unknown: {option.UnitPriceSource}");
            }
            if (option.ExpectedGoodsCostCents.IsKnown)
            {
                reasoning.Add($"goods cost: {option.QuantityUnits.Units} x {option.UnitPriceCents.Cents}c = {option.ExpectedGoodsCostCents.Cents}c.");
            }
            else
            {
                reasoning.Add("goods cost unknown — the unit price or the quantity is unknown.");
            }
            if (option.FreightCostCents.IsKnown)
            {
                reasoning.Add($"freight {option.FreightCostCents.Cents}c ({option.FreightSource})");
            }
            else
            {
                reasoning.Add($"freight unknown: {option.FreightSource}");
            }
            if (option.ExpectedSpoilageLossCents.IsKnown)
            {
                reasoning.Add($"expected spoilage loss: {option.ExpectedSpoilageUnits.Units} unit(s) x {option.RevenuePerUnitCents.Cents}c = {option.ExpectedSpoilageLossCents.Cents}c.");
            }
            else if (option.ExpectedSpoilageUnits.IsKnown)
            {
                reasoning.Add("expected spoilage loss unknown — the spoiled units are counted but no recorded revenue per unit was supplied.");
            }
            else
            {
                reasoning.Add("expected spoilage unknown — no spoilage forecast was supplied, so the profit below excludes unmeasured spoilage.");
            }
            if (option.ExpectedRevenueCents.IsKnown)
            {
                reasoning.Add($"expected revenue: {option.QuantityUnits.Units} x {option.RevenuePerUnitCents.Cents}c = {option.ExpectedRevenueCents.Cents}c ({option.RevenueBasis})");
            }
            else
            {
                reasoning.Add("expected revenue unknown — no recorded retail price per unit was supplied.");
            }
            if (option.ExpectedProfitCents.IsKnown)
            {
                int spoilageLoss = option.ExpectedSpoilageLossCents.IsKnown ? option.ExpectedSpoilageLossCents.Cents : 0;
                string profit = $"expected profit: {option.ExpectedRevenueCents.Cents}c revenue - {option.ExpectedGoodsCostCents.Cents}c goods - {option.FreightCostCents.Cents}c freight - {spoilageLoss}c spoilage = {option.ExpectedProfitCents.Cents}c.";
                if (option.ProfitExcludesUnmeasuredSpoilage)
                {
                    profit += " (profit before unmeasured spoilage — no spoilage forecast was supplied.)";
                }
                reasoning.Add(profit);
            }
            else
            {
                string missing = !option.UnitPriceCents.IsKnown ? "unit price"
                    : !option.FreightCostCents.IsKnown ? "freight"
                    : !option.RevenuePerUnitCents.IsKnown ? "revenue"
                    : !option.QuantityUnits.IsKnown ? "quantity"
                    : "a profit input";
                reasoning.Add($"expected profit unknown — the {missing} is unknown, so this option is incomparable on profit and is not ranked.");
            }
            if (option.IsAffordableKnown)
            {
                reasoning.Add(option.IsAffordable
                    ? $"affordable: {option.ExpectedTotalCostCents.Cents}c against {option.AvailableCashCents.Cents}c available cash."
                    : $"unaffordable: {option.ExpectedTotalCostCents.Cents}c exceeds {option.AvailableCashCents.Cents}c available cash.");
            }
            else
            {
                reasoning.Add("affordability unknown — the expected cost or the available cash is unknown.");
            }
            if (option.Reliability.IsKnown)
            {
                string reliability = $"reliability from the custody log: {option.Reliability.Reason}";
                reasoning.Add(reliability);
                if (option.Reliability.AverageTransitDays.IsKnown && option.LeadTimeDays.IsKnown
                    && option.Reliability.AverageTransitDays.Days > option.LeadTimeDays.Days)
                {
                    reasoning.Add($"recorded history suggests a later arrival: average transit {option.Reliability.AverageTransitDays.Days:0.##} day(s) exceeds the {option.LeadTimeDays.Days}-day lead time.");
                }
            }
            else
            {
                reasoning.Add("reliability unknown — no reliability history was supplied, so none is assumed.");
            }
            if (option.IsSelfCollect)
            {
                reasoning.Add("expected arrival unknown — self-collect requires a journey the quote does not book, so no arrival can be computed.");
            }
            else if (option.QuotedArrivalDay.IsKnown)
            {
                reasoning.Add($"expected arrival day {option.QuotedArrivalDay.Units} (quoted by the order).");
            }
            else if (option.ExpectedArrivalDay(asOfDay).IsKnown)
            {
                reasoning.Add($"expected arrival day {option.ExpectedArrivalDay(asOfDay).Units} (day {asOfDay} + {option.LeadTimeDays.Days}-day lead time).");
            }
            else
            {
                reasoning.Add("expected arrival unknown — no authored lead time was supplied, so no arrival can be computed.");
            }
            if (option.StorageFit.IsKnown)
            {
                reasoning.Add($"storage: {option.StorageFit.Reason}");
            }
            else
            {
                reasoning.Add($"storage unknown: {option.StorageFit.Reason}");
            }
            if (option.ConsolidatedShipmentQuoted)
            {
                reasoning.Add(option.ConsolidatedShipment
                    ? "consolidated shipment (quoted): this order travels with others in one freight movement."
                    : "not consolidated (quoted as a single shipment).");
            }
            else
            {
                reasoning.Add("consolidation not quoted — no consolidation is assumed, and consolidation changes no figure.");
            }
            return reasoning;
        }
    }
}
