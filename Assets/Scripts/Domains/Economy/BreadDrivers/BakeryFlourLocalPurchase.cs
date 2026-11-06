using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.BreadDrivers
{
    /// <summary>Caller inputs for one whole-sack local flour purchase.</summary>
    public sealed class BakeryFlourLocalPurchaseInputs
    {
        public BusinessRuntimeState StoreRuntime;
        public BusinessRuntimeState BakeryRuntime;
        public BakeryShopRuntime Bakery;
        public IBusinessIdentityCashPort BakeryCashPort;
        public IBusinessIdentityCashPort StoreCashPort;
        public EntityIdRegistry IdRegistry;
        public string BakeryBusinessId = string.Empty;
        public int UnitPriceCents;
        public int SacksRequested;
        public int DayIndex;
        public string TransactionId = string.Empty;
    }

    /// <summary>Outcome of one whole-sack local flour purchase.</summary>
    public sealed class BakeryFlourLocalPurchaseResult
    {
        public bool Succeeded;
        public bool Duplicate;
        public string Refusal = string.Empty;
        public int SacksPurchased;
        public int PoundsReceived;
        public int TotalCostCents;
        public string BakeryFlourLotId = string.Empty;
        public List<string> SourceLotIds = new List<string>();
    }

    /// <summary>
    /// Plan 02 (2026-11-06, slice 2A): a whole-sack B2B flour purchase from
    /// the General Store's identified retail lots, at the live retail price.
    /// Plain C#, no Unity dependency. It does NOT go through the trading port
    /// (the flour offer is dropped for households by design) and does NOT use
    /// the procurement service (no flour lots there in production). It mirrors
    /// BusinessFeedPurchase's checks (identity, lot proof, debit-then-consume,
    /// deterministic transaction id, duplicate-safe) but operates on retail
    /// lots directly, in the same consume → pay → store cash → bin order
    /// Series2StoreToBakeryFeed.FeedToBin uses, with its rollback shape.
    ///
    /// Order: validate + prove lots → idempotency check → pre-validate the bin
    /// lot (no mutation) → debit bakery cash → consume store lots → credit
    /// the store once via RecordRetailItemSettlement → record the bin lot.
    /// The store settlement is recorded before the bin lot (FeedToBin shape:
    /// store cash moves before the bin receive), so a bin refusal can reverse
    /// it through the store cash port; the plan's §6 step 2(d) order is
    /// bin-then-settlement, which cannot reverse a settlement that has not
    /// happened yet — this order is the minimum fix for that, and the net
    /// effect is identical on success (exactly one store credit).
    /// Consumption itself cannot be undone through the public lot API, so a
    /// bin refusal after consumption leaves the lots consumed-but-unpaid and
    /// says so loudly in diagnostics; the bin-lot pre-validation is what
    /// makes that path near-impossible.
    /// </summary>
    public static class BakeryFlourLocalPurchase
    {
        public const string FlourItemId = "flour";
        public const string FlourCategoryId = "staple_food";
        public const string FlourUnitLabel = "10 lb sack";
        public const int LbPerSack = 10;

        /// <summary>Deterministic transaction id: never a Guid, duplicate ids on a day refuse rather than double-buy.</summary>
        public static string BuildTransactionId(string bakeryBusinessId, int dayIndex, int sequence) =>
            $"bakery-flour-purchase:{bakeryBusinessId}:{dayIndex}:{sequence}";

        /// <summary>Next purchase sequence for a bakery on a day: existing purchase lots that day + 1.</summary>
        public static int NextSequenceForDay(BakeryShopRuntime bakery, string bakeryBusinessId, int dayIndex)
        {
            if (bakery?.FlourStock == null) return 1;
            string prefix = $"bakery-flour-purchase:{bakeryBusinessId}:{dayIndex}:";
            int count = 0;
            foreach (var lot in bakery.FlourStock.Lots)
            {
                if (lot != null && !string.IsNullOrWhiteSpace(lot.ImportOrderId)
                    && lot.ImportOrderId.StartsWith(prefix, StringComparison.Ordinal))
                    count++;
            }
            return count + 1;
        }

        public static BakeryFlourLocalPurchaseResult PurchaseSacks(
            BakeryFlourLocalPurchaseInputs inputs, List<string> diag)
        {
            diag = diag ?? new List<string>();
            var result = new BakeryFlourLocalPurchaseResult();

            if (inputs == null || inputs.StoreRuntime == null || inputs.BakeryRuntime == null
                || inputs.Bakery == null || inputs.BakeryCashPort == null || inputs.StoreCashPort == null
                || inputs.IdRegistry == null)
            {
                result.Refusal = "BakeryFlourLocalPurchase: store runtime, bakery runtime, bakery, both cash ports, and the id registry are required.";
                diag.Add(result.Refusal);
                return result;
            }
            if (inputs.SacksRequested <= 0)
            {
                result.Refusal = "BakeryFlourLocalPurchase: a positive sack count is required — whole sacks only, never a partial sack.";
                diag.Add(result.Refusal);
                return result;
            }
            if (inputs.UnitPriceCents <= 0)
            {
                result.Refusal = "BakeryFlourLocalPurchase: a positive unit price is required — a zero or negative price refuses, no free flour.";
                diag.Add(result.Refusal);
                return result;
            }
            if (string.IsNullOrWhiteSpace(inputs.TransactionId))
            {
                result.Refusal = "BakeryFlourLocalPurchase: a deterministic transaction id is required.";
                diag.Add(result.Refusal);
                return result;
            }
            string storeBusinessId = inputs.StoreRuntime.BusinessId;
            string bakeryBusinessId = string.IsNullOrWhiteSpace(inputs.BakeryBusinessId)
                ? inputs.BakeryRuntime.BusinessId : inputs.BakeryBusinessId;
            if (string.IsNullOrWhiteSpace(storeBusinessId) || string.IsNullOrWhiteSpace(bakeryBusinessId)
                || !string.Equals(inputs.BakeryCashPort.BusinessId, bakeryBusinessId, StringComparison.Ordinal)
                || !string.Equals(inputs.StoreCashPort.BusinessId, storeBusinessId, StringComparison.Ordinal)
                || string.Equals(storeBusinessId, bakeryBusinessId, StringComparison.Ordinal))
            {
                result.Refusal = "BakeryFlourLocalPurchase: seller, buyer, and cash-port identities do not match distinct businesses.";
                diag.Add(result.Refusal);
                return result;
            }

            // (a) Prove the sacks from identified retail lots before anything moves.
            var sourceLotIds = new List<string>();
            var sourceProvenance = new List<string>();
            int provable = 0;
            foreach (var lot in inputs.StoreRuntime.RetailItemLots)
            {
                if (provable >= inputs.SacksRequested) break;
                if (lot == null
                    || !string.Equals(lot.ItemId, FlourItemId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.CategoryId, FlourCategoryId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.UnitLabel, FlourUnitLabel, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.OwnerBusinessId, storeBusinessId, StringComparison.Ordinal)
                    || !string.Equals(lot.CustodianBusinessId, storeBusinessId, StringComparison.Ordinal)) continue;
                int take = Math.Min(inputs.SacksRequested - provable, lot.AvailableUnits);
                if (take <= 0) continue;
                sourceLotIds.Add(lot.LotId);
                sourceProvenance.Add(lot.Provenance);
                provable += take;
            }
            if (provable < inputs.SacksRequested)
            {
                result.Refusal = $"BakeryFlourLocalPurchase: store retail lots prove {provable} '{FlourUnitLabel}' sack(s) of flour, {inputs.SacksRequested} requested — shortfall {inputs.SacksRequested - provable} sack(s); nothing moved.";
                diag.Add(result.Refusal);
                return result;
            }

            // (e) Idempotency: the same transaction id already recorded in the bin buys nothing twice.
            foreach (var lot in inputs.Bakery.FlourStock.Lots)
            {
                if (lot != null && string.Equals(lot.ImportOrderId, inputs.TransactionId, StringComparison.Ordinal))
                {
                    result.Succeeded = true;
                    result.Duplicate = true;
                    result.BakeryFlourLotId = lot.LotId.ToString();
                    result.SourceLotIds.AddRange(sourceLotIds);
                    diag.Add($"BakeryFlourLocalPurchase: transaction '{inputs.TransactionId}' already recorded as bin lot {result.BakeryFlourLotId} — duplicate, 0 new sacks, cash moved once.");
                    return result;
                }
            }

            // (b) Pre-validate the bin lot (content preconditions only, no mutation):
            // ReceiveLot refuses a lot with no name, no positive units, or no
            // miller and no import order/origin. The lot below names the import
            // order (this transaction) and the General Store origin, so it passes;
            // if it ever would not, refuse before any cash or stock moves.
            string originName = "General Store " + storeBusinessId;
            string supplierNote = $"B2B counter purchase from General Store {storeBusinessId}; " +
                $"{inputs.SacksRequested} sack(s) ('{FlourUnitLabel}') at {inputs.UnitPriceCents}c; " +
                $"source retail lots: {string.Join(", ", sourceLotIds)}";
            if (string.IsNullOrWhiteSpace(BakeryBreadCatalog.FlourItemId)
                || inputs.SacksRequested * LbPerSack <= 0
                || string.IsNullOrWhiteSpace(inputs.TransactionId)
                || string.IsNullOrWhiteSpace(originName))
            {
                result.Refusal = "BakeryFlourLocalPurchase: the bin lot for this purchase would be refused (missing name, units, order, or origin) — refused before any cash or stock moves.";
                diag.Add(result.Refusal);
                return result;
            }

            // (c) Debit bakery cash, then consume store lots; either failure refunds / moves nothing.
            int totalCents = checked(inputs.SacksRequested * inputs.UnitPriceCents);
            string debitRefusal = inputs.BakeryCashPort.TryDebit(totalCents,
                $"flour purchase {inputs.SacksRequested} sack(s) from {storeBusinessId} ({inputs.TransactionId})");
            if (debitRefusal != null)
            {
                result.Refusal = $"BakeryFlourLocalPurchase: bakery payment refused: {debitRefusal}; nothing moved.";
                diag.Add(result.Refusal);
                return result;
            }
            if (!inputs.StoreRuntime.TryConsumeRetailItemStockUnits(
                    FlourItemId, FlourCategoryId, inputs.SacksRequested, out int consumed)
                || consumed != inputs.SacksRequested)
            {
                string refundRefusal = inputs.BakeryCashPort.Credit(totalCents,
                    $"refund refused flour purchase {inputs.TransactionId}");
                result.Refusal = $"BakeryFlourLocalPurchase: store stock changed before checkout ({consumed}/{inputs.SacksRequested} consumed). " +
                    (refundRefusal == null ? "Bakery debit reversed." : $"Bakery refund failed: {refundRefusal}");
                diag.Add(result.Refusal);
                return result;
            }

            // Store cash, once, through the single cash writer — before the bin
            // receive, so a bin refusal can reverse it (FeedToBin shape).
            inputs.StoreRuntime.RecordRetailItemSettlement(FlourCategoryId, inputs.SacksRequested, totalCents);

            // (d) Record the bin lot with full provenance.
            string binRefusal = BakeryFlourSupply.ReceiveImportArrival(
                inputs.Bakery.FlourStock, BakeryBreadCatalog.FlourItemId,
                inputs.SacksRequested * LbPerSack, inputs.TransactionId, originName,
                inputs.DayIndex, inputs.IdRegistry, diag);
            if (binRefusal != null)
            {
                // Rollback (FeedToBin shape): reverse the store credit through
                // the store cash port, credit the bakery back, and say loudly
                // that the consumed lots are consumed-but-unpaid — consumption
                // cannot be undone through the public lot API.
                string storeRefundRefusal = inputs.StoreCashPort.TryDebit(totalCents,
                    $"rollback: bin refused flour purchase {inputs.TransactionId}");
                if (storeRefundRefusal != null)
                    diag.Add($"BakeryFlourLocalPurchase ROLLBACK WARNING: store cash could not return the flour price ({storeRefundRefusal}); cash and stock now disagree and must be reconciled manually.");
                string bakeryRefundRefusal = inputs.BakeryCashPort.Credit(totalCents,
                    $"rollback: bin refused flour purchase {inputs.TransactionId}");
                if (bakeryRefundRefusal != null)
                    diag.Add($"BakeryFlourLocalPurchase ROLLBACK WARNING: bakery cash could not be credited back ({bakeryRefundRefusal}).");
                result.Refusal = $"BakeryFlourLocalPurchase: bakery bin refused the flour ({binRefusal}); cash reversed, but the {inputs.SacksRequested} consumed store sack(s) from lots {string.Join(", ", sourceLotIds)} are consumed-but-unpaid and must be reconciled manually.";
                diag.Add(result.Refusal);
                return result;
            }

            // ReceiveImportArrival takes no supplier-note parameter; the note
            // naming the source retail lots is applied to the recorded lot
            // immediately after receipt, before any other step reads it.
            BakeryFlourLot recorded = null;
            foreach (var lot in inputs.Bakery.FlourStock.Lots)
            {
                if (lot != null && string.Equals(lot.ImportOrderId, inputs.TransactionId, StringComparison.Ordinal))
                {
                    recorded = lot;
                    break;
                }
            }
            if (recorded != null)
            {
                recorded.SupplierNote = supplierNote;
                result.BakeryFlourLotId = recorded.LotId.ToString();
            }

            result.Succeeded = true;
            result.SacksPurchased = inputs.SacksRequested;
            result.PoundsReceived = inputs.SacksRequested * LbPerSack;
            result.TotalCostCents = totalCents;
            result.SourceLotIds.AddRange(sourceLotIds);
            diag.Add($"BakeryFlourLocalPurchase: {inputs.SacksRequested} sack(s) of flour ({result.PoundsReceived} lb) from General Store {storeBusinessId} " +
                $"at {inputs.UnitPriceCents}c/sack for {totalCents}c (transaction {inputs.TransactionId}); " +
                $"source retail lots: {string.Join(", ", sourceLotIds)}; bin lot {result.BakeryFlourLotId}.");
            return result;
        }
    }
}
