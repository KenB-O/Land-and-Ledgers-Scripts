// Series 2 Pass 7 (selling): the bakery as a real supplier in the
// SupplierDirectory, over the real BakeryShopRuntime shelf. Plain C#
// except for the runtime type itself (which the Roslyn harness already
// compiles via its Unity shims); no MonoBehaviour, no scene code.
//
// Item identity, stated plainly: households buy household item "bread"
// (HouseholdItemCatalog.BreadId). The shop runtime's product is
// "bake.bread-loaf" (BakeryBreadCatalog.BreadLoafId). This adapter is
// the one place that mapping lives. Rolls and pies are NOT offered:
// the household catalog has no roll or pie item, so offering them
// would invent a household item. A roll/pie household item is a
// Kennedy/catalog decision, not something this adapter guesses.
//
// Pricing and the shopping loop's uniform-price assumption, stated
// plainly because it is the subtle part: the loop plans a sale at
// ItemPricePerUnitCents and settles the household at that price, while
// the bakery prices each lot by condition (fresh full price, day-old
// discounted, stale never sold). If a sale mixed fresh and day-old
// lots, household outflow and bakery inflow would disagree by the
// discount — a conservation break. This adapter therefore sells in
// condition-homogeneous batches only: ItemPricePerUnitCents reports
// the price of the OLDEST sellable lot (FIFO order: day-old sells
// before fresh, per SellBread), and SellItem caps the sale to the
// total units of that oldest baked day, so every unit sold in one
// call shares one condition and one price. A larger request is a
// partial fill (the loop's partial-fill refund path handles the
// remainder honestly); it is never topped up from a second condition
// at a second price inside one call.
//
// Day awareness: ItemPricePerUnitCents has no day parameter, so the
// adapter carries a settable DayIndex. The production day driver MUST
// set it to the simulation day before any shopping runs; until it is
// set (DayIndex < 0), CheckTradingCapability refuses trading rather
// than selling at a stale day's price. SellItem uses its own dayIndex
// argument for the actual sale and re-derives the condition price at
// that day; if that price disagrees with the quoted price, the sale
// is refused (0 units, stock untouched by the cap logic — SellBread
// is only called once price agreement is proven), so the loop's
// sold-zero refund path, not a mismatch, is what the household sees.
// In normal operation (day set at day start) quote and sale prices
// agree and no refusal occurs.
//
// Cash: business cash moves only through the caller-supplied
// IBusinessCashPort, which production maps to BusinessRuntimeState
// (the same port shape Pass 6 used). Household cash is the loop's
// HouseholdLedger leg, not this adapter's. Trade credit, when the
// caller enables it, is a real FinancialObligationAuthority
// obligation created by the loop before SellItem is called; this
// adapter then posts NO cash for a credit sale (the receivable is
// the loop's obligation), exactly as the store adapter does. Credit
// is off by default and capped by a caller-supplied limit, never
// invented: enabling it without a limit refuses credit sales.
using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Logistics;
using LandLedgers.Population;

namespace LandLedgers.Economy.Businesses.Bakery
{
    /// <summary>
    /// Series 2 Pass 7: bakery supplier adapter over a real shop runtime.
    /// RegisterIn is the one registration call Codex makes from the
    /// bootstrapper (this pass does NOT edit the bootstrapper).
    /// </summary>
    public sealed class Series2BakerySupplierAdapter : IItemResolvingSupplier, IOffersTradeCredit, ILotConditionSupplier
    {
        private readonly BakeryShopRuntime bakery;
        private readonly IBusinessCashPort cash;
        private readonly Func<string> tradingCheck;
        private readonly List<string> log = new List<string>();

        /// <summary>
        /// Priority spoilage integration: the condition state of the
        /// units the most recent <see cref="SellItem"/> call sold.
        /// Reset on every SellItem call, including refusals, so a stale
        /// state never rides a later sale.
        /// </summary>
        private PerishableLotState.SaveDto lastSaleCondition;
        private bool hasLastSaleCondition;
        private string lastReceiptId = string.Empty;
        private int receiptSequence;

        public Series2BakerySupplierAdapter(
            BakeryShopRuntime bakery,
            IBusinessCashPort cash,
            string businessId,
            string businessName,
            string locationId,
            Func<string> tradingCheck = null)
        {
            this.bakery = bakery ?? throw new ArgumentNullException(nameof(bakery));
            this.cash = cash ?? throw new ArgumentNullException(nameof(cash));
            SupplierBusinessId = businessId ?? string.Empty;
            SupplierName = businessName ?? string.Empty;
            LocationId = locationId ?? string.Empty;
            this.tradingCheck = tradingCheck;
        }

        public IReadOnlyList<string> Log => log;

        /// <summary>Simulation day the driver has set for pricing. -1 = unset: trading refused.</summary>
        public int DayIndex { get; set; } = -1;

        /// <summary>Trade credit offered by this bakery. Off by default; a caller limit is required to use it.</summary>
        public bool OffersTradeCreditValue { get; set; } = false;

        /// <summary>Trade-credit cap in cents. 0 means no credit sale is accepted even when enabled.</summary>
        public int TradeCreditLimitCentsValue { get; set; } = 0;

        public string SupplierBusinessId { get; }
        public string SupplierName { get; }
        public string LocationId { get; }
        public string LastReceiptId => lastReceiptId;

        public static void RegisterIn(SupplierDirectory directory, Series2BakerySupplierAdapter adapter)
        {
            if (directory == null || adapter == null) return;
            directory.Register(adapter);
        }

        // ---- IGoodsSupplier (category surface: bakery_goods, Phase H precedent) ----

        public bool HasCategory(string categoryId)
        {
            return string.Equals(categoryId, "bakery_goods", StringComparison.OrdinalIgnoreCase);
        }

        public int StockUnits(string categoryId)
        {
            return HasCategory(categoryId) ? ItemStockUnits(HouseholdItemCatalog.BreadId) : 0;
        }

        public int PricePerUnitCents(string categoryId)
        {
            return HasCategory(categoryId) ? ItemPricePerUnitCents(HouseholdItemCatalog.BreadId) : 0;
        }

        public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics)
        {
            if (!HasCategory(categoryId)) return 0;
            var settlement = new ShoppingSettlement { Mode = ShoppingSettlementMode.CashFromLedger };
            return SellItem(HouseholdItemCatalog.BreadId, requestedUnits, dayIndex, settlement, diagnostics);
        }

        // ---- IItemResolvingSupplier (item surface: household "bread" only) ----

        public bool OffersItem(string itemId)
        {
            return string.Equals(itemId, HouseholdItemCatalog.BreadId, StringComparison.OrdinalIgnoreCase);
        }

        public string CategoryForItem(string itemId)
        {
            return OffersItem(itemId) ? "bakery_goods" : string.Empty;
        }

        public int ItemStockUnits(string itemId)
        {
            if (!OffersItem(itemId) || bakery == null) return 0;
            // Sellability is judged at the adapter day, not from the lot's
            // stored Condition field, which SellBread only refreshes when
            // it runs (AgeShelf). A lot baked two days ago must not count
            // as sellable just because nobody aged it yet.
            int day = DayIndex >= 0 ? DayIndex : 0;
            int total = 0;
            foreach (BakeryBreadLot lot in bakery.BreadShelf)
            {
                if (lot != null && lot.UnitCount > 0 && ConditionAtDay(lot, day) != BakeryGoodsCondition.Stale
                    && string.Equals(lot.ProductId, BakeryBreadCatalog.BreadLoafId, StringComparison.Ordinal))
                    total += Math.Max(0, lot.UnitCount);
            }
            return total;
        }

        public int ItemPricePerUnitCents(string itemId)
        {
            if (!OffersItem(itemId) || DayIndex < 0) return 0;
            BakeryBreadLot oldest = OldestSellableLot(DayIndex);
            if (oldest == null) return 0;
            return PriceAtDay(oldest, DayIndex);
        }

        public string CheckTradingCapability(int absoluteDayIndex, int minuteOfDay)
        {
            if (DayIndex < 0)
                return "bakery trading day is not set — the day driver must set the adapter day before shopping, or bread would price at a stale day";
            if (tradingCheck != null)
                return tradingCheck();
            return null;
        }

        public int SellItem(
            string itemId, int requestedUnits, int dayIndex, ShoppingSettlement settlement, List<string> diagnostics)
        {
            // Priority spoilage integration: every SellItem call starts
            // with no last-sale state — a refusal or an empty sale must
            // never leave a previous sale's state behind for the loop
            // to read.
            hasLastSaleCondition = false;
            lastSaleCondition = default;
            if (!OffersItem(itemId) || requestedUnits <= 0 || bakery == null)
                return 0;
            string tradingProblem = CheckTradingCapability(dayIndex, 0);
            if (tradingProblem != null)
            {
                diagnostics?.Add($"'{SupplierName}' cannot trade: {tradingProblem}");
                return 0;
            }

            // Condition-homogeneous cap: only the oldest baked day's units.
            BakeryBreadLot oldest = OldestSellableLot(dayIndex);
            if (oldest == null)
            {
                diagnostics?.Add($"'{SupplierName}' has no sellable bread — no sale faked.");
                return 0;
            }
            int oldestDayTotal = 0;
            foreach (BakeryBreadLot lot in bakery.BreadShelf)
            {
                if (lot != null && lot.UnitCount > 0 && lot.BakedDayIndex == oldest.BakedDayIndex
                    && ConditionAtDay(lot, dayIndex) != BakeryGoodsCondition.Stale
                    && string.Equals(lot.ProductId, BakeryBreadCatalog.BreadLoafId, StringComparison.Ordinal))
                    oldestDayTotal += Math.Max(0, lot.UnitCount);
            }
            int units = Math.Min(requestedUnits, oldestDayTotal);
            if (units <= 0) return 0;
            int unitPrice = PriceAtDay(oldest, dayIndex);
            if (unitPrice <= 0)
            {
                diagnostics?.Add($"'{SupplierName}' has no legitimate bread price — free goods are not commerce.");
                return 0;
            }

            // Price agreement guard: if the sale-day price differs from the
            // quoted price, refuse rather than post a mismatched sale.
            // In normal operation (day set at day start) they agree.
            if (dayIndex != DayIndex)
            {
                int quoted = ItemPricePerUnitCents(itemId);
                if (quoted != unitPrice)
                {
                    diagnostics?.Add($"'{SupplierName}' price moved since quote ({quoted}c quoted, {unitPrice}c at sale day) — sale refused, re-evaluate.");
                    return 0;
                }
            }

            // Credit eligibility is checked BEFORE any shelf stock moves:
            // a credit sale this bakery does not offer must refuse, never
            // sell first and warn afterwards.
            if (settlement != null && settlement.Mode == ShoppingSettlementMode.TradeCreditObligation)
            {
                if (!OffersTradeCreditValue || TradeCreditLimitCentsValue <= 0 || units * unitPrice > TradeCreditLimitCentsValue)
                {
                    diagnostics?.Add($"'{SupplierName}' does not offer trade credit covering {units * unitPrice}c — sale refused, stock untouched.");
                    return 0;
                }
            }

            List<BakerySaleRecord> records = bakery.SellBread(BakeryBreadCatalog.BreadLoafId, units, dayIndex, diagnostics);
            int sold = 0;
            int revenue = 0;
            foreach (BakerySaleRecord record in records)
            {
                if (record == null) continue;
                sold += Math.Max(0, record.Units);
                revenue += Math.Max(0, record.TotalCents);
            }
            if (sold <= 0) return 0;

            // Priority spoilage integration: the taken part's condition
            // state rides the sale to the buyer. All records share one
            // baked day (the homogeneous cap above), so one condition.
            // The state is the first record's taken part, unit-synced
            // to the total sold when several same-day lots contributed
            // (same day, same condition — the per-lot breakdown stays on
            // the sale records and the F3 log). A stateless shelf (lots
            // baked before this slice attached states) sells exactly as
            // before, with no state.
            PerishableLotState.SaveDto firstTaken = default;
            bool anyTaken = false;
            int takenUnits = 0;
            foreach (BakerySaleRecord record in records)
            {
                if (record == null || !record.HasPerishableCondition) continue;
                if (!anyTaken)
                {
                    firstTaken = record.PerishableCondition;
                    anyTaken = true;
                }
                takenUnits += Math.Max(0, record.Units);
            }
            if (anyTaken && takenUnits > 0)
            {
                PerishableLotState combined = PerishableLotState.FromSaveDto(firstTaken);
                if (combined.Units != takenUnits) combined = combined.WithUnits(takenUnits);
                lastSaleCondition = combined.CaptureSaveDto();
                hasLastSaleCondition = true;
            }

            // Every record must share the one quoted price; otherwise the
            // loop's uniform-price settlement cannot match this sale.
            // The cap above makes this unreachable in practice; if it
            // ever fires, it is logged loudly and the sale still stands
            // at its actual revenue, because shelf stock already moved
            // and silently unwinding it would be a second error.
            foreach (BakerySaleRecord record in records)
            {
                if (record != null && record.PricePerUnitCents != unitPrice)
                    diagnostics?.Add($"'{SupplierName}' WARNING: mixed-condition sale record at {record.PricePerUnitCents}c vs {unitPrice}c — loop settlement assumes uniform price; investigate.");
            }

            if (settlement != null && settlement.Mode == ShoppingSettlementMode.TradeCreditObligation)
            {
                log.Add($"Day {dayIndex}: sold {sold}u bread on trade credit {settlement.ObligationId} ({revenue}c receivable, cash untouched).");
            }
            else
            {
                string problem = cash.Credit(revenue, $"bakery retail sale {sold}u bread");
                if (problem != null)
                    diagnostics?.Add($"'{SupplierName}' WARNING: bakery cash port refused {revenue}c credit ({problem}) — shelf sale already moved; investigate.");
                log.Add($"Day {dayIndex}: sold {sold}u bread for {revenue}c cash.");
            }

            lastReceiptId = $"rcpt-{SupplierBusinessId}-{dayIndex}-{receiptSequence++}";
            diagnostics?.Add($"'{SupplierName}' sold {sold}u bread ({lastReceiptId}).");
            return sold;
        }

        // ---- ILotConditionSupplier (Priority spoilage integration) ----

        /// <summary>
        /// The condition state of the units the most recent
        /// <see cref="SellItem"/> call sold, or false when that sale
        /// carried no state (refusal, empty sale, or a stateless lot).
        /// The state is the taken part's own state (split at the sale),
        /// in sale units; the loop transfers it to the shopper's
        /// custody and the batch carries it to deposit.
        /// </summary>
        public bool TryGetLastSaleCondition(out PerishableLotState.SaveDto condition)
        {
            condition = lastSaleCondition;
            return hasLastSaleCondition;
        }

        // ---- IOffersTradeCredit ----

        public bool OffersTradeCredit => OffersTradeCreditValue;
        public int TradeCreditLimitCents => Math.Max(0, TradeCreditLimitCentsValue);

        private BakeryBreadLot OldestSellableLot(int dayIndex)
        {
            BakeryBreadLot oldest = null;
            foreach (BakeryBreadLot lot in bakery.BreadShelf)
            {
                if (lot == null || lot.UnitCount <= 0) continue;
                if (!string.Equals(lot.ProductId, BakeryBreadCatalog.BreadLoafId, StringComparison.Ordinal)) continue;
                if (ConditionAtDay(lot, dayIndex) == BakeryGoodsCondition.Stale) continue;
                if (oldest == null || lot.BakedDayIndex < oldest.BakedDayIndex)
                    oldest = lot;
            }
            return oldest;
        }

        private static BakeryGoodsCondition ConditionAtDay(BakeryBreadLot lot, int dayIndex)
        {
            int age = dayIndex - lot.BakedDayIndex;
            return age <= 0 ? BakeryGoodsCondition.Fresh
                : age == 1 ? BakeryGoodsCondition.DayOld
                : BakeryGoodsCondition.Stale;
        }

        private int PriceAtDay(BakeryBreadLot lot, int dayIndex)
        {
            switch (ConditionAtDay(lot, dayIndex))
            {
                case BakeryGoodsCondition.Fresh:
                    return bakery.Prices != null ? bakery.Prices.BreadLoafFreshCents : 0;
                case BakeryGoodsCondition.DayOld:
                    return BakeryBreadCatalog.GetDayOldPriceCents(BakeryBreadCatalog.BreadLoafId, bakery.Prices);
                default:
                    return 0;
            }
        }
    }
}
