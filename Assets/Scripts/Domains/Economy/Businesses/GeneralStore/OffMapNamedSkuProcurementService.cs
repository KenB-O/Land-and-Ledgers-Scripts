using System;
using System.Collections.Generic;
using LandLedgers.Economy.Core;
using LandLedgers.Population;

namespace LandLedgers.Economy.Businesses.GeneralStore
{
    /// <summary>
    /// Pass XL vertical slice, one SKU only: off-map procurement for a named
    /// General Store SKU, end to end, through existing authorities where they
    /// exist and with every gap named where they do not.
    ///
    /// CHOSEN SKU AND WHY IT IS SAFE
    /// The slice SKU is "flour" (HouseholdItemCatalog.FlourId). The authored
    /// staple_food category pools flour, bread, potatoes, salt, sugar, coffee
    /// and tea, so GeneralStoreRuntimeManager.BuildHouseholdTradingPort()
    /// REFUSES staple_food as a single-item offer (see
    /// GeneralStoreItemOfferIdentityTests). Flour therefore cannot be sold
    /// from category stock in this slice, and this service never reads or
    /// writes category stock at all. That is the point: there is no second
    /// truth for flour, because the category aggregate is never labelled
    /// flour, and the lot ledger below is never merged into it.
    ///
    /// WHY THE LOT LEDGER IS STANDALONE (READ THIS BEFORE MOVING IT)
    /// GenericBusinessConfiguration.Inventory models lots, but a General Store
    /// is on the LegacyArchetype production path (ProductionPathRegistry:
    /// GeneralStore -&gt; LegacyArchetype, inventory truth = category stock).
    /// Attaching flour lots to a BusinessInstanceState.GenericConfiguration
    /// would let ProductionPathRegistry.ConsolidateInventory move them into
    /// category stock on the next retail sale, destroying flour identity
    /// exactly as the single-SKU guard forbids. So this service owns flour
    /// lots in its OWN ledger, never attached to a BusinessInstanceState,
    /// never passed through ConsolidateInventory, and never passed through
    /// GenericRetailSaleAuthority (which routes a General Store through
    /// category stock). Codex must NOT attach this ledger to the store's
    /// GenericConfiguration until Kennedy decides the production-path
    /// migration for the General Store. That decision is flagged in the
    /// final report; it is not guessed here.
    ///
    /// AUTHORITIES, IN ORDER
    ///   - Order / supply / transit / boundary: ExternalMarketAuthority.
    ///     It never touches local stock or cash; the caller settles locally
    ///     with the receipt id as provenance. Its price is a caller-supplied
    ///     TOTAL, not a price list, and it has no freight field at all.
    ///   - Business cash: IBusinessCashPort, supplied by the caller. In
    ///     production this maps to BusinessRuntimeState (TrySpendCents for
    ///     debit, AddCashCents for credit, CurrentCashCents for balance);
    ///     this service never writes business cash itself except through
    ///     that port, so BusinessRuntimeState remains the business cash
    ///     writer. In Roslyn tests the port is a fake.
    ///   - Household cash: the real HouseholdLedger, the one household cash
    ///     writer. RecordOutflow refuses insufficient funds and missing
    ///     purpose, so a household that cannot pay leaves the need unmet.
    ///   - Trade credit (procurement only): ITradeCreditPort, supplied by
    ///     the caller. The real adapter is TradeCreditAuthorityPort, which
    ///     wraps FinancialObligationAuthority, the one debt authority.
    ///     Household purchases in this service are CASH ONLY; a household
    ///     trade-credit purchase is not implemented here and must not be
    ///     faked by recording an obligation without a sale.
    ///
    /// FREIGHT SURCHARGE (KENNEDY DECISION, NOT CANON)
    /// Kennedy's rule for this slice: the business orders off-map, does not
    /// send its own wagon, and hires the seller to pay for freight, as an
    /// explicit surcharge that makes off-map more expensive than on-map.
    /// No freight rate exists in canon or in ExternalMarketAuthority, so
    /// the surcharge is a caller-supplied parameter, FreightSurchargeCents
    /// per order, logged separately from goods cost on every order, sale
    /// diagnostic, and receipt. Total order cost = goods cost + freight
    /// surcharge, and that total is what ExternalMarketAuthority books as
    /// the boundary outflow, so the seller is paid the freight, not given
    /// free freight. On-map unit price = goods unit price (no freight);
    /// off-map delivered unit price = total / units. Tests use explicit
    /// parameters labelled as test parameters, never as canon rates.
    /// Kennedy must approve a production freight rate before any driver
    /// wires this service with a fixed rate.
    ///
    /// PAYMENT MODES (PROCUREMENT)
    ///   - CashOnArrival: no cash moves at order time. On arrival the
    ///     business cash port must pay the total; if it cannot, the order
    ///     stays in WaitingPayment, no lot is created, and a later
    ///     ReceiveArrivals call retries payment for that order. The
    ///     ExternalMarketAuthority trade is already Fulfilled at that
    ///     point, so the retry is driven from this service's own order
    ///     state, not from re-calling ResolveArrivals.
    ///   - TradeCredit: obligation terms (debtor, creditor, principal,
    ///     purpose, agreement = order id, source event = receipt id) are
    ///     recorded on the order at acceptance, and the real obligation is
    ///     created through ITradeCreditPort before any lot is created on
    ///     arrival. If the port cannot create it, no lot is created.
    ///     ExternalMarketAuthority has no cancel/return API, so a trade
    ///     whose obligation creation fails after purchase leaves supply
    ///     deducted in the market authority; that is diagnosed loudly and
    ///     must be reconciled manually, never hidden by deleting the trade.
    ///
    /// IDS: every order, receipt, lot, sale, and household lot has a stable
    /// string id derived from business id, day, sequence, or receipt id.
    /// No Guid-random ids are used on this path. A second ReceiveArrivals
    /// call for the same receipt creates no second lot (the order is
    /// already Received, and the lot id is receipt-derived).
    ///
    /// PERSISTENCE: ExportState / ImportState carry orders, lots, sales,
    /// received receipt ids, delivered sale ids, and both sequences.
    /// Import is idempotent: importing the same order, lot, or sale
    /// twice does not duplicate it. Since Pass XL phase 2 this state IS
    /// wired into saves: PassXlSliceSaveDto.procurement carries it and
    /// PassXlSliceSaveCoordinator captures/restores it through
    /// SaveLoadManager (clear-then-load, so loading a second save
    /// cannot leak the first save's state). What is still Codex-only
    /// is registering the production service instance with
    /// SaveLoadManager.PassXlProcurementService before the first save
    /// (phase 4 finding F19): without that registration, capture and
    /// restore see a null service and procurement persists empty, with
    /// a diagnostic. Old saves therefore do not break: a store without
    /// this service simply has no off-map lots.
    ///
    /// HOUSEHOLD RECEIPT, STATED PLAINLY: SellToHousehold alone returns
    /// a NamedSkuSale whose HouseholdLotId and provenance chain are the
    /// carriage/receipt record and does NOT write HouseholdInventory
    /// lots. Since Pass XL phase 2 the delivery write exists on this
    /// service: DeliverSaleToInventory books one already-settled sale
    /// into HouseholdInventory exactly once (delivered-sale guard), and
    /// SellAndDeliverToInventory is the single atomic path (sale + cash
    /// + inventory lot, with RollbackSale if AddLot rejects). Do not
    /// claim household inventory was updated from SellToHousehold
    /// alone; use the phase 2 delivery path.
    ///
    /// Plain C# with no Unity dependency, so the whole path is testable in
    /// the Roslyn harness. No Unity, scene, prefab, or Inspector wiring is
    /// claimed here.
    /// </summary>
    public enum NamedSkuOrderStage
    {
        Ordered = 0,
        WaitingPayment = 1,
        WaitingObligation = 2,
        Received = 3,
        Cancelled = 4,
    }

    public enum NamedSkuPaymentMode
    {
        CashOnArrival = 0,
        TradeCredit = 1,
    }

    /// <summary>Business cash port. Production maps to BusinessRuntimeState.</summary>
    public interface IBusinessCashPort
    {
        int BalanceCents { get; }

        /// <summary>Returns null on success, or a diagnostic on failure, moving nothing.</summary>
        string TryDebit(int amountCents, string reason);

        /// <summary>Returns null on success, or a diagnostic on failure, moving nothing.</summary>
        string Credit(int amountCents, string reason);
    }

    /// <summary>
    /// A business cash port whose runtime owner is known. Use this where
    /// transaction records name payer/payee Businesses and the money ports
    /// must be proven to belong to those same parties.
    /// </summary>
    public interface IBusinessIdentityCashPort : IBusinessCashPort
    {
        string BusinessId { get; }
    }

    /// <summary>Trade-credit port. Production maps to FinancialObligationAuthority.</summary>
    public interface ITradeCreditPort
    {
        /// <summary>Returns the obligation id, or null on failure.</summary>
        string CreateTradeCredit(
            string debtor, string creditor, int principalCents, int dayIndex,
            string terms, string purpose, string agreementId, string sourceEconomicEventId);
    }

    /// <summary>Real trade-credit adapter over the one debt authority.</summary>
    public sealed class TradeCreditAuthorityPort : ITradeCreditPort
    {
        private readonly LandLedgers.Economy.Financing.FinancialObligationAuthority authority;
        private readonly LandLedgers.Primitives.EntityIdRegistry ids;

        public TradeCreditAuthorityPort(
            LandLedgers.Economy.Financing.FinancialObligationAuthority authority,
            LandLedgers.Primitives.EntityIdRegistry ids)
        {
            this.authority = authority;
            this.ids = ids;
        }

        public string CreateTradeCredit(
            string debtor, string creditor, int principalCents, int dayIndex,
            string terms, string purpose, string agreementId, string sourceEconomicEventId)
        {
            if (authority == null || ids == null)
            {
                return null;
            }

            LandLedgers.Economy.Financing.FinancialObligation obligation = authority.Create(
                ids,
                LandLedgers.Economy.Financing.FinancialObligationKind.TradeCredit,
                debtor, creditor, principalCents, dayIndex, terms, purpose,
                agreementId, -1, sourceEconomicEventId);
            return obligation != null ? obligation.ObligationId : null;
        }
    }

    [Serializable]
    public sealed class NamedSkuOrder
    {
        public string OrderId = string.Empty;
        public string BusinessId = string.Empty;
        public string SkuId = string.Empty;
        public int UnitsOrdered;
        public int UnitGoodsCostCents;
        public int FreightSurchargeCents;
        public int TotalCostCents;
        public string SupplierId = string.Empty;
        public string SupplierName = string.Empty;
        public int OrderDayIndex;
        public int ExpectedArrivalDayIndex;
        public string ReceiptId = string.Empty;
        public NamedSkuOrderStage Stage;
        public NamedSkuPaymentMode PaymentMode;
        public string ObligationId = string.Empty;
        public string ObligationTerms = string.Empty;
        public string Provenance = string.Empty;
        public string CancelReason = string.Empty;
    }

    [Serializable]
    public sealed class NamedSkuLot
    {
        public string LotId = string.Empty;
        public string SkuId = string.Empty;
        public string BusinessId = string.Empty;
        public string OrderId = string.Empty;
        public string ReceiptId = string.Empty;
        public int QuantityUnits;
        public int RemainingUnits;
        public int TotalCostCents;
        public int UnitCostCents;
        public int CreatedDayIndex;
        public string Provenance = string.Empty;
    }

    /// <summary>One store lot consumption, with units, so a sale can be rolled back exactly.</summary>
    [Serializable]
    public sealed class NamedSkuLotConsumption
    {
        public string LotId = string.Empty;
        public int Units;
    }

    /// <summary>
    /// Phase 8: one recorded non-sale consumption of store-lot units
    /// (bakery feed, credit sale). The per-lot split is the rollback
    /// record: without it, consumed units could not be restored exactly.
    /// </summary>
    [Serializable]
    public sealed class NamedSkuLotConsumptionRecord
    {
        public string ConsumptionId = string.Empty;
        public string BusinessId = string.Empty;
        public string SkuId = string.Empty;
        public int Units;
        public string Purpose = string.Empty;
        public int DayIndex;
        public List<NamedSkuLotConsumption> ConsumedLotUnits = new List<NamedSkuLotConsumption>();
        public string Provenance = string.Empty;
    }

    [Serializable]
    public sealed class NamedSkuSale
    {
        public string SaleId = string.Empty;
        public string BusinessId = string.Empty;
        public int HouseholdId;
        public int ActingPersonId;
        public string BuyerPrincipal = string.Empty;
        public string SkuId = string.Empty;
        public int Units;
        public int UnitSalePriceCents;
        public int TotalPriceCents;
        public List<string> ConsumedLotIds = new List<string>();
        /// <summary>
        /// Phase 2, additive: per-lot units matching ConsumedLotIds, in the
        /// same order. Sales written before Phase 2 carry an empty list;
        /// rollback of such a sale cannot restore per-lot splits exactly
        /// and is therefore refused rather than guessed (see RollbackSale).
        /// </summary>
        public List<NamedSkuLotConsumption> ConsumedLotUnits = new List<NamedSkuLotConsumption>();
        public string HouseholdLotId = string.Empty;
        public int DayIndex;
        public string Provenance = string.Empty;
    }

    [Serializable]
    public sealed class NamedSkuProcurementSaveDto
    {
        public string BusinessId = string.Empty;
        public List<NamedSkuOrder> Orders = new List<NamedSkuOrder>();
        public List<NamedSkuLot> Lots = new List<NamedSkuLot>();
        public List<NamedSkuSale> Sales = new List<NamedSkuSale>();
        public List<string> ReceivedReceiptIds = new List<string>();
        /// <summary>
        /// Phase 2, additive: SaleIds whose household inventory delivery
        /// has already happened. Delivering the same sale twice must never
        /// double-add inventory; this set is the persisted guard. Old
        /// saves carry an empty list, so a sale from an old save is
        /// treated as not yet delivered by this path.
        /// </summary>
        public List<string> DeliveredSaleIds = new List<string>();
        /// <summary>Phase 8, additive: non-sale lot consumptions. Old saves carry an empty list.</summary>
        public List<NamedSkuLotConsumptionRecord> LotConsumptions = new List<NamedSkuLotConsumptionRecord>();
        public int NextOrderSequence;
        public int NextSaleSequence;
        public int NextConsumptionSequence;
    }

    public sealed class OffMapNamedSkuProcurementService
    {
        private readonly string businessId;
        private readonly ExternalMarketAuthority market;
        private readonly IBusinessCashPort businessCash;
        private readonly List<NamedSkuOrder> orders = new List<NamedSkuOrder>();
        private readonly List<NamedSkuLot> lots = new List<NamedSkuLot>();
        private readonly List<NamedSkuSale> sales = new List<NamedSkuSale>();
        private readonly List<string> receivedReceiptIds = new List<string>();
        private readonly List<string> deliveredSaleIds = new List<string>();
        private readonly List<NamedSkuLotConsumptionRecord> productionConsumptions = new List<NamedSkuLotConsumptionRecord>();
        private readonly List<string> diagnostics = new List<string>();
        private int nextOrderSequence;
        private int nextSaleSequence;
        private int nextConsumptionSequence;

        public OffMapNamedSkuProcurementService(
            string businessId, ExternalMarketAuthority market, IBusinessCashPort businessCash)
        {
            this.businessId = string.IsNullOrWhiteSpace(businessId) ? string.Empty : businessId;
            this.market = market;
            this.businessCash = businessCash;
        }

        public string BusinessId => businessId;
        public IReadOnlyList<NamedSkuOrder> Orders => orders;
        public IReadOnlyList<NamedSkuLot> Lots => lots;
        public IReadOnlyList<NamedSkuSale> Sales => sales;
        public IReadOnlyList<string> ReceivedReceiptIds => receivedReceiptIds;
        public IReadOnlyList<string> DeliveredSaleIds => deliveredSaleIds;
        public IReadOnlyList<NamedSkuLotConsumptionRecord> LotConsumptions => productionConsumptions;
        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>True when this sale's household inventory delivery has already happened.</summary>
        public bool IsSaleDelivered(string saleId)
        {
            return !string.IsNullOrWhiteSpace(saleId) && deliveredSaleIds.Contains(saleId);
        }

        /// <summary>
        /// Phase 2 clear-then-load support: empties orders, lots, sales,
        /// receipts, delivered marks, and both sequences. Callers restoring
        /// a save must call this before ImportState, so a second save does
        /// not leak the first save's state. ImportState alone remains
        /// merge-idempotent for duplicate imports.
        /// </summary>
        public void ClearState()
        {
            orders.Clear();
            lots.Clear();
            sales.Clear();
            receivedReceiptIds.Clear();
            deliveredSaleIds.Clear();
            productionConsumptions.Clear();
            diagnostics.Clear();
            nextOrderSequence = 0;
            nextSaleSequence = 0;
            nextConsumptionSequence = 0;
        }

        /// <summary>
        /// Phase 8: consumes store-lot units for a named non-household-sale
        /// purpose (bakery feed, credit sale) through this service's own
        /// FIFO lot ledger, so one lot unit can never feed both a household
        /// sale and production, and can never be sold twice. This is the
        /// ONLY non-sale consumption path: it moves no cash (the caller
        /// settles cash separately, e.g. an inter-business sale), it
        /// refuses on shortfall moving nothing, and it records the
        /// per-lot split so UndoConsumption can restore it exactly.
        /// Returns null on refusal, with a diagnostic recorded.
        /// </summary>
        public NamedSkuLotConsumptionRecord ConsumeLots(
            string skuId, int units, string purpose, int dayIndex)
        {
            if (string.IsNullOrWhiteSpace(skuId) || units <= 0
                || string.IsNullOrWhiteSpace(purpose))
            {
                diagnostics.Add("ConsumeLots rejected: a SKU, positive units, and a named purpose are required.");
                return null;
            }

            if (SellableUnits(skuId) < units)
            {
                diagnostics.Add($"ConsumeLots rejected: sellable '{skuId}' is {SellableUnits(skuId)}u, requested {units}u for '{purpose}'; no partial consumption, nothing moved.");
                return null;
            }

            var consumed = new List<NamedSkuLotConsumption>();
            int remaining = units;
            for (int i = 0; i < lots.Count && remaining > 0; i++)
            {
                NamedSkuLot lot = lots[i];
                if (lot == null || !string.Equals(lot.SkuId, skuId, StringComparison.OrdinalIgnoreCase)
                    || lot.RemainingUnits <= 0)
                {
                    continue;
                }

                int take = Math.Min(lot.RemainingUnits, remaining);
                lot.RemainingUnits -= take;
                consumed.Add(new NamedSkuLotConsumption { LotId = lot.LotId, Units = take });
                remaining -= take;
            }

            if (remaining > 0)
            {
                for (int i = 0; i < consumed.Count; i++)
                {
                    for (int j = 0; j < lots.Count; j++)
                    {
                        if (lots[j] != null && string.Equals(lots[j].LotId, consumed[i].LotId, StringComparison.Ordinal))
                        {
                            lots[j].RemainingUnits += consumed[i].Units;
                            break;
                        }
                    }
                }

                diagnostics.Add("ConsumeLots rolled back: lot consumption failed after validation; quantities restored.");
                return null;
            }

            var record = new NamedSkuLotConsumptionRecord
            {
                ConsumptionId = $"sku-consume-{businessId}-{Math.Max(0, dayIndex)}-{nextConsumptionSequence++}",
                BusinessId = businessId,
                SkuId = skuId,
                Units = units,
                Purpose = purpose,
                DayIndex = Math.Max(0, dayIndex),
                ConsumedLotUnits = consumed,
                Provenance = $"consumption {purpose}: {units}u {skuId} from store lots " +
                    $"[{string.Join(", ", consumed.ConvertAll(c => c.LotId + ":" + c.Units + "u"))}]; " +
                    "these units are no longer sellable and can never also feed a household sale.",
            };
            productionConsumptions.Add(record);
            diagnostics.Add(record.Provenance);
            return record;
        }

        /// <summary>
        /// Phase 8: restores exactly one ConsumeLots record, for a failed
        /// downstream step (e.g. a refused inventory delivery). Returns
        /// null on success, or a diagnostic when the record is unknown,
        /// in which case nothing is restored by guesswork.
        /// </summary>
        public string UndoConsumption(NamedSkuLotConsumptionRecord record)
        {
            if (record == null)
            {
                return "UndoConsumption rejected: no consumption record supplied.";
            }

            bool found = false;
            for (int i = 0; i < productionConsumptions.Count; i++)
            {
                if (productionConsumptions[i] != null
                    && string.Equals(productionConsumptions[i].ConsumptionId, record.ConsumptionId, StringComparison.Ordinal))
                {
                    productionConsumptions.RemoveAt(i);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return $"UndoConsumption refused for '{record.ConsumptionId}': no such consumption recorded; nothing restored.";
            }

            if (record.ConsumedLotUnits != null)
            {
                for (int i = 0; i < record.ConsumedLotUnits.Count; i++)
                {
                    for (int j = 0; j < lots.Count; j++)
                    {
                        if (lots[j] != null && string.Equals(lots[j].LotId, record.ConsumedLotUnits[i].LotId, StringComparison.Ordinal))
                        {
                            lots[j].RemainingUnits += record.ConsumedLotUnits[i].Units;
                            break;
                        }
                    }
                }
            }

            diagnostics.Add($"UndoConsumption: consumption {record.ConsumptionId} ({record.Units}u {record.SkuId} for '{record.Purpose}') restored to store lots.");
            return null;
        }

        public int SellableUnits(string skuId)
        {
            int total = 0;
            for (int i = 0; i < lots.Count; i++)
            {
                if (lots[i] != null && string.Equals(lots[i].SkuId, skuId, StringComparison.OrdinalIgnoreCase))
                {
                    total += Math.Max(0, lots[i].RemainingUnits);
                }
            }

            return total;
        }

        public NamedSkuOrder FindOrder(string orderId)
        {
            for (int i = 0; i < orders.Count; i++)
            {
                if (orders[i] != null && string.Equals(orders[i].OrderId, orderId, StringComparison.Ordinal))
                {
                    return orders[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Places one off-map order. No cash moves here in either mode, and
        /// no stock is created here: stock is created only by ReceiveArrivals
        /// after transit and after payment/obligation settlement.
        /// Returns null on rejection, with a diagnostic recorded.
        /// </summary>
        public NamedSkuOrder PlaceOrder(
            string skuId, int units, int unitGoodsCostCents, int freightSurchargeCents,
            string supplierId, int dayIndex, NamedSkuPaymentMode paymentMode,
            ITradeCreditPort tradeCredit)
        {
            if (market == null)
            {
                diagnostics.Add("PlaceOrder rejected: no ExternalMarketAuthority supplied.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(businessId) || string.IsNullOrWhiteSpace(skuId)
                || units <= 0 || unitGoodsCostCents < 0 || freightSurchargeCents < 0
                || string.IsNullOrWhiteSpace(supplierId))
            {
                diagnostics.Add("PlaceOrder rejected: business, SKU, supplier, positive units, and non-negative costs are required.");
                return null;
            }

            int goodsCost = units * unitGoodsCostCents;
            int totalCost = goodsCost + freightSurchargeCents;
            if (totalCost <= 0)
            {
                diagnostics.Add("PlaceOrder rejected: total order cost must be positive (ExternalMarketAuthority requires a positive price).");
                return null;
            }

            if (paymentMode == NamedSkuPaymentMode.TradeCredit && tradeCredit == null)
            {
                diagnostics.Add("PlaceOrder rejected: TradeCredit mode requires a trade-credit port; no obligation is faked.");
                return null;
            }

            ExternalCounterparty supplier = market.GetCounterparty(supplierId);
            if (supplier == null)
            {
                diagnostics.Add($"PlaceOrder rejected: unknown external counterparty '{supplierId}'.");
                return null;
            }

            market.SetDayIndex(Math.Max(0, dayIndex));
            var marketDiag = new List<string>();
            string receiptId = market.RecordExternalPurchase(supplierId, skuId, units, totalCost, marketDiag);
            for (int i = 0; i < marketDiag.Count; i++)
            {
                diagnostics.Add(marketDiag[i]);
            }

            if (string.IsNullOrWhiteSpace(receiptId))
            {
                diagnostics.Add($"PlaceOrder rejected: ExternalMarketAuthority refused {units}u '{skuId}' from '{supplierId}' (finite supply). No order, no obligation, no stock.");
                return null;
            }

            int arrivalDay = dayIndex;
            for (int i = 0; i < market.Trades.Count; i++)
            {
                if (market.Trades[i] != null && string.Equals(market.Trades[i].ReceiptId, receiptId, StringComparison.Ordinal))
                {
                    arrivalDay = market.Trades[i].FulfilledDayIndex;
                    break;
                }
            }

            string orderId = $"sku-po-{businessId}-{Math.Max(0, dayIndex)}-{nextOrderSequence++}";
            string terms = paymentMode == NamedSkuPaymentMode.TradeCredit
                ? $"trade credit, net 30, for {units}u {skuId}, total {totalCost}c (goods {goodsCost}c + seller-paid freight {freightSurchargeCents}c)"
                : $"cash on arrival, total {totalCost}c (goods {goodsCost}c + seller-paid freight {freightSurchargeCents}c)";
            var order = new NamedSkuOrder
            {
                OrderId = orderId,
                BusinessId = businessId,
                SkuId = skuId,
                UnitsOrdered = units,
                UnitGoodsCostCents = unitGoodsCostCents,
                FreightSurchargeCents = freightSurchargeCents,
                TotalCostCents = totalCost,
                SupplierId = supplierId,
                SupplierName = supplier.DisplayName,
                OrderDayIndex = Math.Max(0, dayIndex),
                ExpectedArrivalDayIndex = arrivalDay,
                ReceiptId = receiptId,
                Stage = NamedSkuOrderStage.Ordered,
                PaymentMode = paymentMode,
                ObligationTerms = terms,
                Provenance = $"order {orderId}: {units}u {skuId} from {supplier.DisplayName} ({supplierId}) goods {goodsCost}c + seller-paid freight {freightSurchargeCents}c = {totalCost}c, receipt {receiptId}, arrives day {arrivalDay}; on-map unit price {unitGoodsCostCents}c, off-map delivered unit price {totalCost / units}c" +
                    (totalCost % units != 0 ? $" (remainder {totalCost % units}c on the lot total, not per unit)" : string.Empty),
            };
            orders.Add(order);
            diagnostics.Add(order.Provenance);
            return order;
        }

        /// <summary>
        /// Receives arrivals due on or before dayIndex. Idempotent: a receipt
        /// already received creates no second lot, and a payment-waiting
        /// order is retried from this service's own state (the market will
        /// not re-return it). Returns newly created lots.
        /// </summary>
        public List<NamedSkuLot> ReceiveArrivals(int dayIndex, ITradeCreditPort tradeCredit)
        {
            var created = new List<NamedSkuLot>();
            if (market == null)
            {
                diagnostics.Add("ReceiveArrivals rejected: no ExternalMarketAuthority supplied.");
                return created;
            }

            // Retry payment/obligation-waiting orders first: their market
            // trades are already Fulfilled, so ResolveArrivals will not
            // return them again.
            for (int i = 0; i < orders.Count; i++)
            {
                NamedSkuOrder order = orders[i];
                if (order == null
                    || (order.Stage != NamedSkuOrderStage.WaitingPayment
                        && order.Stage != NamedSkuOrderStage.WaitingObligation)
                    || order.ExpectedArrivalDayIndex > dayIndex)
                {
                    continue;
                }

                NamedSkuLot lot = TryCreateLot(order, dayIndex, tradeCredit);
                if (lot != null)
                {
                    created.Add(lot);
                }
            }

            List<ExternalTradeRecord> arrived = market.ResolveArrivals(Math.Max(0, dayIndex));
            for (int i = 0; i < arrived.Count; i++)
            {
                ExternalTradeRecord trade = arrived[i];
                if (trade == null || string.IsNullOrWhiteSpace(trade.ReceiptId))
                {
                    continue;
                }

                NamedSkuOrder order = null;
                for (int j = 0; j < orders.Count; j++)
                {
                    if (orders[j] != null && string.Equals(orders[j].ReceiptId, trade.ReceiptId, StringComparison.Ordinal))
                    {
                        order = orders[j];
                        break;
                    }
                }

                if (order == null)
                {
                    diagnostics.Add($"ReceiveArrivals: arrived trade {trade.ReceiptId} has no order in this service (business {businessId}); no lot created, no cash moved.");
                    continue;
                }

                if (order.Stage == NamedSkuOrderStage.Received || receivedReceiptIds.Contains(trade.ReceiptId))
                {
                    diagnostics.Add($"ReceiveArrivals: trade {trade.ReceiptId} already received; duplicate delivery ignored, no second lot.");
                    continue;
                }

                NamedSkuLot lot = TryCreateLot(order, dayIndex, tradeCredit);
                if (lot != null)
                {
                    created.Add(lot);
                }
            }

            return created;
        }

        private NamedSkuLot TryCreateLot(NamedSkuOrder order, int dayIndex, ITradeCreditPort tradeCredit)
        {
            if (order == null || receivedReceiptIds.Contains(order.ReceiptId))
            {
                return null;
            }

            if (order.PaymentMode == NamedSkuPaymentMode.CashOnArrival)
            {
                if (businessCash == null)
                {
                    order.Stage = NamedSkuOrderStage.WaitingPayment;
                    diagnostics.Add($"ReceiveArrivals: order {order.OrderId} WAITING - no business cash port supplied; no stock booked without payment.");
                    return null;
                }

                string payProblem = businessCash.TryDebit(order.TotalCostCents, $"off-map receipt {order.ReceiptId} ({order.UnitsOrdered}u {order.SkuId})");
                if (payProblem != null)
                {
                    order.Stage = NamedSkuOrderStage.WaitingPayment;
                    diagnostics.Add($"ReceiveArrivals: order {order.OrderId} WAITING - {payProblem} No stock booked without payment.");
                    return null;
                }
            }
            else
            {
                if (tradeCredit == null)
                {
                    order.Stage = NamedSkuOrderStage.WaitingObligation;
                    diagnostics.Add($"ReceiveArrivals: order {order.OrderId} WAITING - no trade-credit port supplied; no obligation, no stock.");
                    return null;
                }

                if (string.IsNullOrWhiteSpace(order.ObligationId))
                {
                    string obligationId = tradeCredit.CreateTradeCredit(
                        $"business:{businessId}", order.SupplierId, order.TotalCostCents,
                        Math.Max(0, dayIndex), order.ObligationTerms,
                        $"off-map procurement {order.OrderId} ({order.UnitsOrdered}u {order.SkuId})",
                        order.OrderId, order.ReceiptId);
                    if (string.IsNullOrWhiteSpace(obligationId))
                    {
                        order.Stage = NamedSkuOrderStage.WaitingObligation;
                        diagnostics.Add($"ReceiveArrivals: order {order.OrderId} WAITING - trade-credit obligation could not be created; no stock booked. " +
                            "Note: ExternalMarketAuthority has no cancel API, so its supply deduction for this trade is not reversible; reconcile manually.");
                        return null;
                    }

                    order.ObligationId = obligationId;
                }
            }

            string lotId = $"lot-{order.ReceiptId}";
            var lot = new NamedSkuLot
            {
                LotId = lotId,
                SkuId = order.SkuId,
                BusinessId = businessId,
                OrderId = order.OrderId,
                ReceiptId = order.ReceiptId,
                QuantityUnits = order.UnitsOrdered,
                RemainingUnits = order.UnitsOrdered,
                TotalCostCents = order.TotalCostCents,
                UnitCostCents = order.UnitsOrdered > 0 ? order.TotalCostCents / order.UnitsOrdered : 0,
                CreatedDayIndex = Math.Max(0, dayIndex),
                Provenance = $"{order.Provenance} -> lot {lotId} received day {dayIndex}, {order.UnitsOrdered}u {order.SkuId}, total {order.TotalCostCents}c" +
                    (order.PaymentMode == NamedSkuPaymentMode.TradeCredit ? $" via obligation {order.ObligationId}" : " cash on arrival") +
                    "; category stock untouched, GenericConfiguration untouched.",
            };
            lots.Add(lot);
            receivedReceiptIds.Add(order.ReceiptId);
            order.Stage = NamedSkuOrderStage.Received;
            diagnostics.Add(lot.Provenance);
            return lot;
        }

        /// <summary>
        /// Sells lot stock to a household for cash. Atomic by compensation:
        /// business credit is taken first; if lot consumption or household
        /// debit then fails, the business credit is reversed and lot
        /// quantities are restored exactly (lot objects are restored in
        /// place, so lot ids and provenance survive). Returns null on
        /// refusal, with a diagnostic recorded, moving nothing.
        /// Household purchases are cash only in this slice.
        /// </summary>
        public NamedSkuSale SellToHousehold(
            HouseholdLedger householdLedger, int householdId, int actingPersonId,
            string buyerPrincipal, string skuId, int units, int unitSalePriceCents, int dayIndex)
        {
            if (householdLedger == null || householdLedger.HouseholdId != householdId)
            {
                diagnostics.Add("SellToHousehold rejected: a HouseholdLedger for the buying household is required.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(buyerPrincipal) || actingPersonId < 0
                || string.IsNullOrWhiteSpace(skuId) || units <= 0 || unitSalePriceCents < 0)
            {
                diagnostics.Add("SellToHousehold rejected: a real buyer principal, acting person, SKU, positive units, and non-negative price are required.");
                return null;
            }

            if (SellableUnits(skuId) < units)
            {
                diagnostics.Add($"SellToHousehold rejected: sellable '{skuId}' is {SellableUnits(skuId)}u, requested {units}u; no partial sale, nothing moved.");
                return null;
            }

            int totalPrice = units * unitSalePriceCents;
            if (totalPrice > householdLedger.GetBalanceCents())
            {
                diagnostics.Add($"SellToHousehold rejected: household H{householdId} balance {householdLedger.GetBalanceCents()}c cannot pay {totalPrice}c for {units}u '{skuId}'; need stays unmet, nothing moved.");
                return null;
            }

            if (businessCash == null)
            {
                diagnostics.Add("SellToHousehold rejected: no business cash port supplied; sale cannot settle.");
                return null;
            }

            string creditProblem = businessCash.Credit(totalPrice, $"sale {units}u {skuId} to H{householdId}");
            if (creditProblem != null)
            {
                diagnostics.Add($"SellToHousehold rejected: business cash credit failed ({creditProblem}); nothing moved.");
                return null;
            }

            // Consume FIFO across lots for this SKU, capturing lot ids.
            var consumed = new List<(NamedSkuLot lot, int units)>();
            var consumedLotIds = new List<string>();
            int remaining = units;
            for (int i = 0; i < lots.Count && remaining > 0; i++)
            {
                NamedSkuLot lot = lots[i];
                if (lot == null || !string.Equals(lot.SkuId, skuId, StringComparison.OrdinalIgnoreCase)
                    || lot.RemainingUnits <= 0)
                {
                    continue;
                }

                int take = Math.Min(lot.RemainingUnits, remaining);
                lot.RemainingUnits -= take;
                consumed.Add((lot, take));
                consumedLotIds.Add(lot.LotId);
                remaining -= take;
            }

            if (remaining > 0)
            {
                // Defensive: validation above should have prevented this.
                // Restore exactly, then reverse the business credit.
                for (int i = 0; i < consumed.Count; i++)
                {
                    consumed[i].lot.RemainingUnits += consumed[i].units;
                }

                string reverseProblem = businessCash.TryDebit(totalPrice, "rollback: lot consumption failed after business credit");
                diagnostics.Add("SellToHousehold rolled back: lot consumption failed after business credit; quantities restored" +
                    (reverseProblem != null ? $" WARNING: business credit reversal failed ({reverseProblem}); reconcile manually." : "."));
                return null;
            }

            string outflowProblem = householdLedger.RecordOutflow(
                Math.Max(0, dayIndex), totalPrice,
                $"purchase {units}u {skuId} from {businessId}", businessId);
            if (outflowProblem != null)
            {
                for (int i = 0; i < consumed.Count; i++)
                {
                    consumed[i].lot.RemainingUnits += consumed[i].units;
                }

                string reverseProblem = businessCash.TryDebit(totalPrice, "rollback: household debit failed after business credit");
                diagnostics.Add("SellToHousehold rolled back: household debit failed after business credit; lot quantities restored" +
                    (reverseProblem != null ? $" WARNING: business credit reversal failed ({reverseProblem}); reconcile manually." : "."));
                return null;
            }

            string saleId = $"sku-sale-{businessId}-{Math.Max(0, dayIndex)}-{nextSaleSequence++}";
            string householdLotId = $"hh-lot-{saleId}";
            var consumedLotUnits = new List<NamedSkuLotConsumption>();
            for (int i = 0; i < consumed.Count; i++)
            {
                consumedLotUnits.Add(new NamedSkuLotConsumption { LotId = consumed[i].lot.LotId, Units = consumed[i].units });
            }

            var sale = new NamedSkuSale
            {
                SaleId = saleId,
                BusinessId = businessId,
                HouseholdId = householdId,
                ActingPersonId = actingPersonId,
                BuyerPrincipal = buyerPrincipal,
                SkuId = skuId,
                Units = units,
                UnitSalePriceCents = unitSalePriceCents,
                TotalPriceCents = totalPrice,
                ConsumedLotIds = consumedLotIds,
                ConsumedLotUnits = consumedLotUnits,
                HouseholdLotId = householdLotId,
                DayIndex = Math.Max(0, dayIndex),
                Provenance = $"sale {saleId}: H{householdId} bought {units}u {skuId} from {businessId} for {totalPrice}c " +
                    $"from store lots [{string.Join(", ", consumedLotIds)}] -> household lot {householdLotId}; " +
                    "household cash via HouseholdLedger, business cash via business cash port; " +
                    "household inventory write is NOT done by SellToHousehold alone (use SellAndDeliverToInventory).",
            };
            sales.Add(sale);
            diagnostics.Add(sale.Provenance);
            return sale;
        }

        /// <summary>Result of a household inventory delivery for one sale.</summary>
        public sealed class NamedSkuDeliveryResult
        {
            public bool Success;
            public bool AlreadyDelivered;
            public NamedSkuSale Sale;
            public int HouseholdInventoryLotId = -1;
            public string Diagnostic = string.Empty;
        }

        /// <summary>
        /// Phase 2: delivers one already-settled sale into household
        /// inventory, exactly once. If the sale is already in
        /// DeliveredSaleIds, this is a no-op success (AlreadyDelivered),
        /// never a second inventory lot. It does not move cash: cash was
        /// settled by SellToHousehold. Callers that need sale + delivery
        /// in one transaction must use SellAndDeliverToInventory, not
        /// SellToHousehold followed by a separate charge.
        /// </summary>
        public NamedSkuDeliveryResult DeliverSaleToInventory(NamedSkuSale sale, HouseholdInventory inventory)
        {
            var result = new NamedSkuDeliveryResult { Sale = sale };
            if (sale == null)
            {
                result.Diagnostic = "DeliverSaleToInventory rejected: no sale supplied.";
                diagnostics.Add(result.Diagnostic);
                return result;
            }

            if (inventory == null || inventory.HouseholdId != sale.HouseholdId)
            {
                result.Diagnostic = $"DeliverSaleToInventory rejected: a HouseholdInventory for H{sale.HouseholdId} is required.";
                diagnostics.Add(result.Diagnostic);
                return result;
            }

            if (IsSaleDelivered(sale.SaleId))
            {
                result.Success = true;
                result.AlreadyDelivered = true;
                result.Diagnostic = $"DeliverSaleToInventory: sale {sale.SaleId} already delivered; no second inventory lot created.";
                diagnostics.Add(result.Diagnostic);
                return result;
            }

            int beforeCount = inventory.Lots.Count;
            string sourceLabel = $"{sale.Provenance} -> delivered to household inventory";
            string addProblem = inventory.AddLot(
                sale.SkuId, sale.Units, sale.DayIndex, sourceLabel,
                qualityBand: string.Empty, sourceKind: "off-map-procurement-sale",
                ownerPersonId: sale.ActingPersonId);
            if (addProblem != null)
            {
                result.Diagnostic = $"DeliverSaleToInventory rejected: household inventory refused the lot ({addProblem}); nothing delivered.";
                diagnostics.Add(result.Diagnostic);
                return result;
            }

            // AddLot appends exactly one lot; that lot is the delivery.
            // Do not guess its id from a sequence: read it back.
            HouseholdItemLot delivered = null;
            for (int i = beforeCount; i < inventory.Lots.Count; i++)
            {
                delivered = inventory.Lots[i];
            }

            if (delivered == null)
            {
                result.Diagnostic = "DeliverSaleToInventory failed: inventory accepted the lot but no new lot is visible; delivery not marked.";
                diagnostics.Add(result.Diagnostic);
                return result;
            }

            deliveredSaleIds.Add(sale.SaleId);
            result.Success = true;
            result.HouseholdInventoryLotId = delivered.LotId;
            result.Diagnostic = $"DeliverSaleToInventory: sale {sale.SaleId} delivered as household lot {delivered.LotId} ({sale.Units}u {sale.SkuId}); sale HouseholdLotId {sale.HouseholdLotId} is the carriage record, inventory LotId is the household lot.";
            diagnostics.Add(result.Diagnostic);
            return result;
        }

        /// <summary>
        /// Phase 2: the single settlement path for a household purchase
        /// that must also land in household inventory. It calls
        /// SellToHousehold exactly once (one cash settlement, no second
        /// charge), then delivers to inventory. If AddLot rejects, the
        /// whole operation rolls back: store lots restored, business
        /// credit reversed, household outflow reversed, sale record
        /// removed, and no delivered mark. Returns null on failure.
        /// </summary>
        public NamedSkuDeliveryResult SellAndDeliverToInventory(
            HouseholdLedger householdLedger, HouseholdInventory inventory,
            int householdId, int actingPersonId, string buyerPrincipal,
            string skuId, int units, int unitSalePriceCents, int dayIndex)
        {
            var result = new NamedSkuDeliveryResult();
            NamedSkuSale sale = SellToHousehold(
                householdLedger, householdId, actingPersonId, buyerPrincipal,
                skuId, units, unitSalePriceCents, dayIndex);
            if (sale == null)
            {
                result.Diagnostic = "SellAndDeliverToInventory failed: sale was refused; nothing moved.";
                diagnostics.Add(result.Diagnostic);
                return result;
            }

            result.Sale = sale;
            NamedSkuDeliveryResult delivery = DeliverSaleToInventory(sale, inventory);
            if (delivery.Success)
            {
                delivery.Sale = sale;
                return delivery;
            }

            // AddLot (or inventory) refused after cash settled: roll back
            // the sale completely, so no partial goods or cash remain.
            string rollbackProblem = RollbackSale(sale, householdLedger);
            result.Diagnostic = $"SellAndDeliverToInventory rolled back: {delivery.Diagnostic}" +
                (rollbackProblem != null ? $" WARNING: rollback incomplete ({rollbackProblem}); reconcile manually." : " Sale record removed, lots and cash restored.");
            diagnostics.Add(result.Diagnostic);
            return result;
        }

        /// <summary>
        /// Phase 2 rollback for one sale created by SellToHousehold.
        /// Restores store lot quantities from ConsumedLotUnits, reverses
        /// the business credit, reverses the household outflow with a
        /// named SaleProceeds refund inflow, and removes the sale record.
        /// Returns null on full success, or a diagnostic if any leg
        /// could not be reversed. Sales written before Phase 2 carry no
        /// ConsumedLotUnits and are refused for rollback rather than
        /// restored by guesswork.
        /// </summary>
        public string RollbackSale(NamedSkuSale sale, HouseholdLedger householdLedger)
        {
            if (sale == null)
            {
                return "RollbackSale rejected: no sale supplied.";
            }

            if (sale.ConsumedLotUnits == null || sale.ConsumedLotUnits.Count == 0
                || sale.ConsumedLotUnits.Count != sale.ConsumedLotIds.Count)
            {
                return $"RollbackSale refused for sale {sale.SaleId}: per-lot consumption is missing, so lot quantities cannot be restored exactly; no partial rollback was attempted.";
            }

            for (int i = 0; i < sale.ConsumedLotUnits.Count; i++)
            {
                NamedSkuLotConsumption consumption = sale.ConsumedLotUnits[i];
                for (int j = 0; j < lots.Count; j++)
                {
                    if (lots[j] != null && string.Equals(lots[j].LotId, consumption.LotId, StringComparison.Ordinal))
                    {
                        lots[j].RemainingUnits += consumption.Units;
                        break;
                    }
                }
            }

            string problem = null;
            if (businessCash != null)
            {
                string reverseProblem = businessCash.TryDebit(
                    sale.TotalPriceCents, $"rollback: sale {sale.SaleId} reversed");
                if (reverseProblem != null)
                {
                    problem = $"business credit reversal failed ({reverseProblem})";
                }
            }
            else
            {
                problem = "no business cash port supplied for reversal";
            }

            if (householdLedger != null && householdLedger.HouseholdId == sale.HouseholdId)
            {
                string refundProblem = householdLedger.RecordInflow(
                    Math.Max(0, sale.DayIndex), sale.TotalPriceCents,
                    HouseholdIncomeSource.SaleProceeds, sale.SaleId,
                    $"refund: reversed sale {sale.SaleId} ({sale.Units}u {sale.SkuId})",
                    sale.BusinessId);
                if (refundProblem != null)
                {
                    problem = problem != null
                        ? problem + $"; household refund failed ({refundProblem})"
                        : $"household refund failed ({refundProblem})";
                }
            }
            else
            {
                problem = problem != null
                    ? problem + "; no matching household ledger supplied for refund"
                    : "no matching household ledger supplied for refund";
            }

            for (int i = 0; i < sales.Count; i++)
            {
                if (sales[i] != null && string.Equals(sales[i].SaleId, sale.SaleId, StringComparison.Ordinal))
                {
                    sales.RemoveAt(i);
                    break;
                }
            }

            deliveredSaleIds.Remove(sale.SaleId);
            diagnostics.Add($"RollbackSale: sale {sale.SaleId} reversed (lots restored, cash legs attempted)" +
                (problem != null ? $" WARNING: {problem}." : "."));
            return problem;
        }

        public NamedSkuProcurementSaveDto ExportState()
        {
            return new NamedSkuProcurementSaveDto
            {
                BusinessId = businessId,
                Orders = new List<NamedSkuOrder>(orders),
                Lots = new List<NamedSkuLot>(lots),
                Sales = new List<NamedSkuSale>(sales),
                ReceivedReceiptIds = new List<string>(receivedReceiptIds),
                DeliveredSaleIds = new List<string>(deliveredSaleIds),
                LotConsumptions = new List<NamedSkuLotConsumptionRecord>(productionConsumptions),
                NextOrderSequence = nextOrderSequence,
                NextSaleSequence = nextSaleSequence,
                NextConsumptionSequence = nextConsumptionSequence,
            };
        }

        /// <summary>Idempotent import: same order, lot, sale, or receipt twice does not duplicate it.</summary>
        public void ImportState(NamedSkuProcurementSaveDto dto)
        {
            if (dto == null)
            {
                return;
            }

            if (dto.Orders != null) for (int i = 0; i < dto.Orders.Count; i++)
            {
                if (dto.Orders[i] != null && FindOrder(dto.Orders[i].OrderId) == null)
                {
                    orders.Add(dto.Orders[i]);
                }
            }

            if (dto.Lots != null) for (int i = 0; i < dto.Lots.Count; i++)
            {
                NamedSkuLot lot = dto.Lots[i];
                if (lot == null)
                {
                    continue;
                }

                bool exists = false;
                for (int j = 0; j < lots.Count; j++)
                {
                    if (lots[j] != null && string.Equals(lots[j].LotId, lot.LotId, StringComparison.Ordinal))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    lots.Add(lot);
                }
            }

            if (dto.Sales != null) for (int i = 0; i < dto.Sales.Count; i++)
            {
                NamedSkuSale sale = dto.Sales[i];
                if (sale == null)
                {
                    continue;
                }

                bool exists = false;
                for (int j = 0; j < sales.Count; j++)
                {
                    if (sales[j] != null && string.Equals(sales[j].SaleId, sale.SaleId, StringComparison.Ordinal))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    sales.Add(sale);
                }
            }

            if (dto.ReceivedReceiptIds != null)
            {
                for (int i = 0; i < dto.ReceivedReceiptIds.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(dto.ReceivedReceiptIds[i]) && !receivedReceiptIds.Contains(dto.ReceivedReceiptIds[i]))
                    {
                        receivedReceiptIds.Add(dto.ReceivedReceiptIds[i]);
                    }
                }
            }

            if (dto.DeliveredSaleIds != null)
            {
                for (int i = 0; i < dto.DeliveredSaleIds.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(dto.DeliveredSaleIds[i]) && !deliveredSaleIds.Contains(dto.DeliveredSaleIds[i]))
                    {
                        deliveredSaleIds.Add(dto.DeliveredSaleIds[i]);
                    }
                }
            }

            if (dto.LotConsumptions != null) for (int i = 0; i < dto.LotConsumptions.Count; i++)
            {
                NamedSkuLotConsumptionRecord record = dto.LotConsumptions[i];
                if (record == null)
                {
                    continue;
                }

                bool exists = false;
                for (int j = 0; j < productionConsumptions.Count; j++)
                {
                    if (productionConsumptions[j] != null && string.Equals(productionConsumptions[j].ConsumptionId, record.ConsumptionId, StringComparison.Ordinal))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    productionConsumptions.Add(record);
                }
            }

            nextOrderSequence = Math.Max(nextOrderSequence, dto.NextOrderSequence);
            nextSaleSequence = Math.Max(nextSaleSequence, dto.NextSaleSequence);
            nextConsumptionSequence = Math.Max(nextConsumptionSequence, dto.NextConsumptionSequence);
        }
    }
}
