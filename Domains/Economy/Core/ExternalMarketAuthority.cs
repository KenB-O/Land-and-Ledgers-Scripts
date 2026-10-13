using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Core
{
    /// <summary>
    /// Pass VII Workstream A: an identifiable external trading counterparty.
    /// External counterparties are real economic actors with BOUNDED budgets and
    /// FINITE supply — never infinite demand, never infinite goods. Budgets only
    /// replenish through an explicit <see cref="ExternalMarketAuthority.RestockCounterparty"/>
    /// call (a modeled event), never by fiat.
    /// </summary>
    [Serializable]
    public sealed class ExternalCounterparty
    {
        public string CounterpartyId = string.Empty;
        public string DisplayName = string.Empty;
        public int InitialBudgetCents;
        public int BudgetCents;
        /// <summary>Goods this counterparty can sell to us: goods id -> units available.</summary>
        public Dictionary<string, int> SupplyUnits = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Modeled fulfillment delay in days: external shipments are never instantaneous.</summary>
        public int TransitDays = 3;
    }

    /// <summary>
    /// Pass VII Workstream A: a recorded external trade with full provenance.
    /// The receipt id is the permanent provenance reference linking the local
    /// settlement (stock movement, cash movement) to the external counterparty.
    /// </summary>
    [Serializable]
    public sealed class ExternalTradeRecord
    {
        public string ReceiptId = string.Empty;
        public int DayIndex;
        /// <summary>"sale" = local sells to external (cash flows IN); "purchase" = local buys from external (cash flows OUT).</summary>
        public string Direction = string.Empty;
        public string CounterpartyId = string.Empty;
        public string CounterpartyDisplayName = string.Empty;
        public string GoodsId = string.Empty;
        public int Quantity;
        public int PriceCents;
        public string Provenance = string.Empty;
        /// <summary>For purchases: the day the shipment arrives (dayIndex + transit). Sales settle immediately.</summary>
        public int FulfilledDayIndex;
        public bool Fulfilled;
    }

    /// <summary>
    /// Pass VII Workstream A: boundary accounting for external trade.
    ///
    /// Every inflow names its external source. Local cash is NOT conserved
    /// across the boundary: cash arriving from an external sale is new money in
    /// the local economy, and it is accounted explicitly in the boundary
    /// reconciliation (<see cref="GetBoundaryReconciliation"/>), never silently
    /// merged.
    ///
    /// This authority owns ONLY the boundary side: counterparty budgets, finite
    /// counterparty supply, in-transit shipments, trade receipts, and the
    /// inflow/outflow books. It never touches local business stock or cash —
    /// the caller performs the local settlement through the production
    /// authorities (BusinessRuntimeState) using the returned receipt id as
    /// provenance. That separation is what keeps the boundary explicit.
    /// </summary>
    public sealed class ExternalMarketAuthority : IExternalMarketAuthority
    {
        private readonly Dictionary<string, ExternalCounterparty> counterparties =
            new Dictionary<string, ExternalCounterparty>(StringComparer.Ordinal);
        private readonly Dictionary<string, ExternalTradeRecord> tradesByReceipt =
            new Dictionary<string, ExternalTradeRecord>(StringComparer.Ordinal);
        private readonly List<ExternalTradeRecord> trades = new List<ExternalTradeRecord>();
        private readonly List<string> diagnostics = new List<string>();
        private int nextSequence;
        private int totalInflowCents;
        private int totalOutflowCents;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<ExternalTradeRecord> Trades => trades;
        public int TotalInflowCents => totalInflowCents;
        public int TotalOutflowCents => totalOutflowCents;

        /// <summary>Registers an external counterparty. Re-registering the same id is a no-op.</summary>
        public string RegisterCounterparty(ExternalCounterparty counterparty)
        {
            if (counterparty == null || string.IsNullOrWhiteSpace(counterparty.CounterpartyId))
            {
                return "Rejected: counterparty needs an id.";
            }

            if (counterparties.ContainsKey(counterparty.CounterpartyId))
            {
                return null;
            }

            counterparty.BudgetCents = Mathf.Max(0, counterparty.InitialBudgetCents);
            counterparties[counterparty.CounterpartyId] = counterparty;
            return null;
        }

        public ExternalCounterparty GetCounterparty(string counterpartyId)
        {
            return !string.IsNullOrWhiteSpace(counterpartyId) && counterparties.TryGetValue(counterpartyId, out ExternalCounterparty c)
                ? c
                : null;
        }

        /// <summary>
        /// A local seller sells goods TO the external counterparty: cash flows
        /// IN across the boundary. The counterparty pays from its BOUNDED
        /// budget; a counterparty that cannot pay produces no sale.
        /// Returns the receipt id, or null/empty when rejected (diagnostic logged).
        /// The caller settles the local side (stock, cash) with the receipt as provenance.
        /// </summary>
        public string RecordExternalSale(
            string externalCounterpartyId, string goodsId, int quantity,
            int priceCents, List<string> diagnostics)
        {
            List<string> log = diagnostics ?? this.diagnostics;
            ExternalCounterparty counterparty = GetCounterparty(externalCounterpartyId);
            if (counterparty == null)
            {
                log.Add($"RecordExternalSale rejected: unknown external counterparty '{externalCounterpartyId}'.");
                return null;
            }

            if (quantity <= 0 || priceCents <= 0 || string.IsNullOrWhiteSpace(goodsId))
            {
                log.Add("RecordExternalSale rejected: quantity and price must be positive and goods named.");
                return null;
            }

            if (counterparty.BudgetCents < priceCents)
            {
                log.Add(
                    $"RecordExternalSale rejected: {counterparty.DisplayName} cannot pay {priceCents}c " +
                    $"for {quantity}x '{goodsId}' (bounded budget {counterparty.BudgetCents}c). No sale.");
                return null;
            }

            counterparty.BudgetCents -= priceCents;
            var record = new ExternalTradeRecord
            {
                ReceiptId = $"EXT-S-{nextSequence++:0000}",
                DayIndex = CurrentDay(),
                Direction = "sale",
                CounterpartyId = counterparty.CounterpartyId,
                CounterpartyDisplayName = counterparty.DisplayName,
                GoodsId = goodsId,
                Quantity = quantity,
                PriceCents = priceCents,
                Provenance = $"external buyer {counterparty.CounterpartyId} ({counterparty.DisplayName}) " +
                             $"purchased {quantity}x '{goodsId}' for {priceCents}c from bounded budget; " +
                             $"budget remaining {counterparty.BudgetCents}c",
                FulfilledDayIndex = CurrentDay(),
                Fulfilled = true,
            };
            AddTrade(record);
            totalInflowCents += priceCents;
            log.Add($"RecordExternalSale {record.ReceiptId}: {quantity}x '{goodsId}' -> {counterparty.DisplayName} for {priceCents}c (inflow).");
            return record.ReceiptId;
        }

        /// <summary>
        /// A local buyer purchases goods FROM the external counterparty: cash
        /// flows OUT across the boundary. The counterparty sells from FINITE
        /// supply, and fulfillment is modeled with a transit delay — never
        /// instantaneous. Returns the receipt id, or null/empty when rejected.
        /// The caller pays the local cash and credits local stock on arrival
        /// (see <see cref="ResolveArrivals"/>).
        /// </summary>
        public string RecordExternalPurchase(
            string externalCounterpartyId, string goodsId, int quantity,
            int priceCents, List<string> diagnostics)
        {
            List<string> log = diagnostics ?? this.diagnostics;
            ExternalCounterparty counterparty = GetCounterparty(externalCounterpartyId);
            if (counterparty == null)
            {
                log.Add($"RecordExternalPurchase rejected: unknown external counterparty '{externalCounterpartyId}'.");
                return null;
            }

            if (quantity <= 0 || priceCents <= 0 || string.IsNullOrWhiteSpace(goodsId))
            {
                log.Add("RecordExternalPurchase rejected: quantity and price must be positive and goods named.");
                return null;
            }

            int available = 0;
            if (counterparty.SupplyUnits != null)
            {
                counterparty.SupplyUnits.TryGetValue(goodsId, out available);
            }

            if (available < quantity)
            {
                log.Add(
                    $"RecordExternalPurchase rejected: {counterparty.DisplayName} holds {available}x '{goodsId}', " +
                    $"cannot supply {quantity}x. External supply is finite. No purchase.");
                return null;
            }

            counterparty.SupplyUnits[goodsId] = available - quantity;
            int day = CurrentDay();
            int transit = Mathf.Max(1, counterparty.TransitDays);
            var record = new ExternalTradeRecord
            {
                ReceiptId = $"EXT-P-{nextSequence++:0000}",
                DayIndex = day,
                Direction = "purchase",
                CounterpartyId = counterparty.CounterpartyId,
                CounterpartyDisplayName = counterparty.DisplayName,
                GoodsId = goodsId,
                Quantity = quantity,
                PriceCents = priceCents,
                Provenance = $"external seller {counterparty.CounterpartyId} ({counterparty.DisplayName}) " +
                             $"supplying {quantity}x '{goodsId}' for {priceCents}c; " +
                             $"in transit {transit}d, arrives day {day + transit}",
                FulfilledDayIndex = day + transit,
                Fulfilled = false,
            };
            AddTrade(record);
            totalOutflowCents += priceCents;
            log.Add($"RecordExternalPurchase {record.ReceiptId}: {quantity}x '{goodsId}' <- {counterparty.DisplayName} for {priceCents}c (outflow), arrives day {record.FulfilledDayIndex}.");
            return record.ReceiptId;
        }

        /// <summary>
        /// Marks in-transit external purchases arrived on or before dayIndex.
        /// Returns the newly arrived records; the caller credits local stock.
        /// </summary>
        public List<ExternalTradeRecord> ResolveArrivals(int dayIndex)
        {
            var arrived = new List<ExternalTradeRecord>();
            for (int i = 0; i < trades.Count; i++)
            {
                ExternalTradeRecord trade = trades[i];
                if (trade != null && !trade.Fulfilled && trade.Direction == "purchase" && trade.FulfilledDayIndex <= dayIndex)
                {
                    trade.Fulfilled = true;
                    arrived.Add(trade);
                    diagnostics.Add($"ResolveArrivals: {trade.ReceiptId} ({trade.Quantity}x '{trade.GoodsId}') arrived day {dayIndex}.");
                }
            }

            return arrived;
        }

        /// <summary>
        /// A modeled event that replenishes a counterparty budget (e.g. a new
        /// buying season). Explicit and logged — budgets never refill by fiat.
        /// </summary>
        public void RestockCounterparty(string counterpartyId, int additionalBudgetCents, string reason, List<string> diagnostics)
        {
            List<string> log = diagnostics ?? this.diagnostics;
            ExternalCounterparty counterparty = GetCounterparty(counterpartyId);
            if (counterparty == null)
            {
                log.Add($"RestockCounterparty rejected: unknown counterparty '{counterpartyId}'.");
                return;
            }

            counterparty.BudgetCents = Mathf.Max(0, counterparty.BudgetCents + Mathf.Max(0, additionalBudgetCents));
            log.Add($"RestockCounterparty: {counterparty.DisplayName} budget +{additionalBudgetCents}c ({reason}); now {counterparty.BudgetCents}c.");
        }

        /// <summary>
        /// Boundary reconciliation: explicit accounting of cash that crossed the
        /// boundary. Inflows are new local money (NOT conserved from local
        /// cash); outflows are local money that left. Net is the boundary's
        /// contribution to local cash, reconcilable against the sum of
        /// receipt-attributed local settlements.
        /// </summary>
        public (int inflowCents, int outflowCents, int netCents) GetBoundaryReconciliation()
        {
            return (totalInflowCents, totalOutflowCents, totalInflowCents - totalOutflowCents);
        }

        private void AddTrade(ExternalTradeRecord record)
        {
            tradesByReceipt[record.ReceiptId] = record;
            trades.Add(record);
        }

        private int currentDay = -1;

        /// <summary>Sets the day index stamped on new trade records.</summary>
        public void SetDayIndex(int dayIndex)
        {
            currentDay = dayIndex;
        }

        private int CurrentDay()
        {
            return Mathf.Max(0, currentDay);
        }

        #region Save / load (additive, idempotent)

        /// <summary>Exports full authority state for save. Import is idempotent by receipt id.</summary>
        public ExternalMarketAuthoritySaveDto ExportState()
        {
            var dto = new ExternalMarketAuthoritySaveDto
            {
                nextSequence = nextSequence,
                totalInflowCents = totalInflowCents,
                totalOutflowCents = totalOutflowCents,
            };
            foreach (KeyValuePair<string, ExternalCounterparty> entry in counterparties)
            {
                ExternalCounterparty c = entry.Value;
                var supply = new List<ExternalCounterpartySupplyDto>();
                if (c.SupplyUnits != null)
                {
                    foreach (KeyValuePair<string, int> s in c.SupplyUnits)
                    {
                        supply.Add(new ExternalCounterpartySupplyDto { goodsId = s.Key, units = s.Value });
                    }
                }

                dto.counterparties.Add(new ExternalCounterpartySaveDto
                {
                    counterpartyId = c.CounterpartyId,
                    displayName = c.DisplayName,
                    initialBudgetCents = c.InitialBudgetCents,
                    budgetCents = c.BudgetCents,
                    transitDays = c.TransitDays,
                    supply = supply,
                });
            }

            foreach (ExternalTradeRecord trade in trades)
            {
                dto.trades.Add(new ExternalTradeRecordSaveDto
                {
                    receiptId = trade.ReceiptId,
                    dayIndex = trade.DayIndex,
                    direction = trade.Direction,
                    counterpartyId = trade.CounterpartyId,
                    counterpartyDisplayName = trade.CounterpartyDisplayName,
                    goodsId = trade.GoodsId,
                    quantity = trade.Quantity,
                    priceCents = trade.PriceCents,
                    provenance = trade.Provenance,
                    fulfilledDayIndex = trade.FulfilledDayIndex,
                    fulfilled = trade.Fulfilled,
                });
            }

            return dto;
        }

        /// <summary>
        /// Restores authority state. Idempotent: re-importing an already-loaded
        /// receipt does not duplicate it, and totals are recomputed from the
        /// imported records so a double import cannot double-count.
        /// </summary>
        public void ImportState(ExternalMarketAuthoritySaveDto dto)
        {
            if (dto == null)
            {
                return;
            }

            counterparties.Clear();
            if (dto.counterparties != null)
            {
                foreach (ExternalCounterpartySaveDto c in dto.counterparties)
                {
                    if (c == null || string.IsNullOrWhiteSpace(c.counterpartyId))
                    {
                        continue;
                    }

                    var counterparty = new ExternalCounterparty
                    {
                        CounterpartyId = c.counterpartyId,
                        DisplayName = c.displayName ?? string.Empty,
                        InitialBudgetCents = Mathf.Max(0, c.initialBudgetCents),
                        BudgetCents = Mathf.Max(0, c.budgetCents),
                        TransitDays = Mathf.Max(1, c.transitDays),
                    };
                    if (c.supply != null)
                    {
                        foreach (ExternalCounterpartySupplyDto s in c.supply)
                        {
                            if (s != null && !string.IsNullOrWhiteSpace(s.goodsId))
                            {
                                counterparty.SupplyUnits[s.goodsId] = Mathf.Max(0, s.units);
                            }
                        }
                    }

                    counterparties[counterparty.CounterpartyId] = counterparty;
                }
            }

            tradesByReceipt.Clear();
            trades.Clear();
            nextSequence = 0;
            if (dto.trades != null)
            {
                foreach (ExternalTradeRecordSaveDto t in dto.trades)
                {
                    if (t == null || string.IsNullOrWhiteSpace(t.receiptId) || tradesByReceipt.ContainsKey(t.receiptId))
                    {
                        continue;
                    }

                    var record = new ExternalTradeRecord
                    {
                        ReceiptId = t.receiptId,
                        DayIndex = Mathf.Max(0, t.dayIndex),
                        Direction = t.direction ?? string.Empty,
                        CounterpartyId = t.counterpartyId ?? string.Empty,
                        CounterpartyDisplayName = t.counterpartyDisplayName ?? string.Empty,
                        GoodsId = t.goodsId ?? string.Empty,
                        Quantity = Mathf.Max(0, t.quantity),
                        PriceCents = Mathf.Max(0, t.priceCents),
                        Provenance = t.provenance ?? string.Empty,
                        FulfilledDayIndex = Mathf.Max(0, t.fulfilledDayIndex),
                        Fulfilled = t.fulfilled,
                    };
                    tradesByReceipt[record.ReceiptId] = record;
                    trades.Add(record);
                    nextSequence = Mathf.Max(nextSequence, ParseSequence(record.ReceiptId) + 1);
                }
            }

            // Totals recomputed from records: a repeated import cannot double-count.
            totalInflowCents = 0;
            totalOutflowCents = 0;
            for (int i = 0; i < trades.Count; i++)
            {
                if (trades[i].Direction == "sale")
                {
                    totalInflowCents += trades[i].PriceCents;
                }
                else if (trades[i].Direction == "purchase")
                {
                    totalOutflowCents += trades[i].PriceCents;
                }
            }
        }

        private static int ParseSequence(string receiptId)
        {
            if (string.IsNullOrWhiteSpace(receiptId))
            {
                return 0;
            }

            int dash = receiptId.LastIndexOf('-');
            if (dash < 0 || dash + 1 >= receiptId.Length)
            {
                return 0;
            }

            return int.TryParse(receiptId.Substring(dash + 1), out int seq) ? seq : 0;
        }

        #endregion
    }

    #region Save DTOs (owning runtime class hosts its DTO methods per repo convention)

    [Serializable]
    public sealed class ExternalCounterpartySupplyDto
    {
        public string goodsId = string.Empty;
        public int units;
    }

    [Serializable]
    public sealed class ExternalCounterpartySaveDto
    {
        public string counterpartyId = string.Empty;
        public string displayName = string.Empty;
        public int initialBudgetCents;
        public int budgetCents;
        public int transitDays;
        public List<ExternalCounterpartySupplyDto> supply = new List<ExternalCounterpartySupplyDto>();
    }

    [Serializable]
    public sealed class ExternalTradeRecordSaveDto
    {
        public string receiptId = string.Empty;
        public int dayIndex;
        public string direction = string.Empty;
        public string counterpartyId = string.Empty;
        public string counterpartyDisplayName = string.Empty;
        public string goodsId = string.Empty;
        public int quantity;
        public int priceCents;
        public string provenance = string.Empty;
        public int fulfilledDayIndex;
        public bool fulfilled;
    }

    [Serializable]
    public sealed class ExternalMarketAuthoritySaveDto
    {
        public int nextSequence;
        public int totalInflowCents;
        public int totalOutflowCents;
        public List<ExternalCounterpartySaveDto> counterparties = new List<ExternalCounterpartySaveDto>();
        public List<ExternalTradeRecordSaveDto> trades = new List<ExternalTradeRecordSaveDto>();
    }

    #endregion
}
