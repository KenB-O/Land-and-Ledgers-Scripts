using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;

namespace LandLedgers.Economy.Businesses.Sawmill
{
    /// <summary>Caller inputs for one lamp-oil lubrication purchase.</summary>
    public sealed class SawmillLubricationPurchaseInputs
    {
        public BusinessRuntimeState StoreRuntime;
        public SawmillShopRuntime MillRuntime;
        public LandLedgers.Economy.Equipment.SupportContext MillSupport;
        public IBusinessIdentityCashPort MillCashPort;
        public IBusinessIdentityCashPort StoreCashPort;
        public string MillBusinessId = string.Empty;
        public string StoreBusinessId = string.Empty;
        public int UnitPriceCents;
        public int QuartsRequested;
        public int DayIndex;
        public string TransactionId = string.Empty;
    }

    /// <summary>Outcome of one lamp-oil lubrication purchase.</summary>
    public sealed class SawmillLubricationPurchaseResult
    {
        public bool Succeeded;
        public bool Duplicate;
        public string Refusal = string.Empty;
        public int QuartsPurchased;
        public int TotalCostCents;
        public List<string> SourceLotIds = new List<string>();
    }

    /// <summary>
    /// C-5 (expanded campaign, Phase 1 forestry): the mill buys lamp oil
    /// from the General Store's identified retail lots at the store's LIVE
    /// selling price, for its lubrication store (1 quart = 1 lubrication
    /// unit). This is the BakeryFlourLocalPurchase shape, minus the bin lot:
    /// the mill's "bin" is an integer store, so the receipt leg is
    /// SawmillLubrication.AddUnits and the duplicate-safe record is the
    /// runtime's applied-transaction-ids list. Supplier named: General Store.
    /// Price never hardcoded — the production caller reads
    /// GetItemSellingPriceCents("lamp_oil") from the store manager; a zero
    /// price (store definition missing, item unknown) refuses. Lamp oil
    /// (kerosene/coal oil) on a saw blade to prevent pitch buildup is
    /// historically real practice, and it is the only purchasable oil in the
    /// economy: no tallow/lard/machine-oil SKU exists anywhere, so no tallow
    /// path is invented. Freight is NOT built here — the store is in-town and
    /// C-7 (camp supply line) owns the mill-buys-from-store freight pattern;
    /// this is the receipt + settlement leg with the price read live.
    /// </summary>
    public static class SawmillLubricationPurchase
    {
        public const string LampOilItemId = "lamp_oil";
        public const string LampOilCategoryId = "household_goods";
        public const string LampOilUnitLabel = "quart";

        public static SawmillLubricationPurchaseResult PurchaseQuarts(
            SawmillLubricationPurchaseInputs inputs, List<string> diag)
        {
            diag = diag ?? new List<string>();
            var result = new SawmillLubricationPurchaseResult();

            if (inputs == null || inputs.StoreRuntime == null || inputs.MillRuntime == null
                || inputs.MillSupport == null || inputs.MillCashPort == null || inputs.StoreCashPort == null)
            {
                result.Refusal = "SawmillLubricationPurchase: store runtime, mill runtime, mill support context, and both cash ports are required.";
                diag.Add(result.Refusal);
                return result;
            }
            if (inputs.QuartsRequested <= 0)
            {
                result.Refusal = "SawmillLubricationPurchase: a positive quart count is required — whole quarts only, never a partial quart.";
                diag.Add(result.Refusal);
                return result;
            }
            if (inputs.UnitPriceCents <= 0)
            {
                result.Refusal = "SawmillLubricationPurchase: a positive unit price is required — a zero or negative price refuses, no free lamp oil.";
                diag.Add(result.Refusal);
                return result;
            }
            if (string.IsNullOrWhiteSpace(inputs.TransactionId))
            {
                result.Refusal = "SawmillLubricationPurchase: a deterministic transaction id is required.";
                diag.Add(result.Refusal);
                return result;
            }
            string storeBusinessId = inputs.StoreRuntime.BusinessId;
            string millBusinessId = string.IsNullOrWhiteSpace(inputs.MillBusinessId)
                ? inputs.MillRuntime.BusinessInstanceId : inputs.MillBusinessId;
            if (string.IsNullOrWhiteSpace(storeBusinessId) || string.IsNullOrWhiteSpace(millBusinessId)
                || !string.Equals(inputs.MillCashPort.BusinessId, millBusinessId, StringComparison.Ordinal)
                || !string.Equals(inputs.StoreCashPort.BusinessId, storeBusinessId, StringComparison.Ordinal)
                || string.Equals(storeBusinessId, millBusinessId, StringComparison.Ordinal))
            {
                result.Refusal = "SawmillLubricationPurchase: seller, buyer, and cash-port identities do not match distinct businesses.";
                diag.Add(result.Refusal);
                return result;
            }

            // (a) Idempotency FIRST, before the lot proof: the same transaction
            // id already recorded on the mill buys nothing twice. The proof
            // below would fail on a genuine retry — the first purchase
            // consumed the lots it proved — so a retry must report the
            // recorded outcome, not a shortfall. (The flour purchase proves
            // first and only passes its duplicate test because the store
            // retains enough stock; this ordering is the stronger one.)
            if (inputs.MillRuntime.HasAppliedLubricationPurchase(inputs.TransactionId))
            {
                result.Succeeded = true;
                result.Duplicate = true;
                diag.Add($"SawmillLubricationPurchase: transaction '{inputs.TransactionId}' already recorded — duplicate, 0 new quarts, cash moved once.");
                return result;
            }

            // (b) Prove the quarts from identified retail lots before anything moves.
            var sourceLotIds = new List<string>();
            var sourceProvenance = new List<string>();
            int provable = 0;
            foreach (var lot in inputs.StoreRuntime.RetailItemLots)
            {
                if (provable >= inputs.QuartsRequested) break;
                if (lot == null
                    || !string.Equals(lot.ItemId, LampOilItemId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.CategoryId, LampOilCategoryId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.UnitLabel, LampOilUnitLabel, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.OwnerBusinessId, storeBusinessId, StringComparison.Ordinal)
                    || !string.Equals(lot.CustodianBusinessId, storeBusinessId, StringComparison.Ordinal)) continue;
                int take = Math.Min(inputs.QuartsRequested - provable, lot.AvailableUnits);
                if (take <= 0) continue;
                sourceLotIds.Add(lot.LotId);
                sourceProvenance.Add(lot.Provenance);
                provable += take;
            }
            if (provable < inputs.QuartsRequested)
            {
                result.Refusal = $"SawmillLubricationPurchase: store retail lots prove {provable} '{LampOilUnitLabel}' quart(s) of lamp oil, {inputs.QuartsRequested} requested — shortfall {inputs.QuartsRequested - provable} quart(s); nothing moved.";
                diag.Add(result.Refusal);
                return result;
            }

            // (c) The two lubrication stores must agree before any cash moves
            // (the receipt leg refuses on disagreement — check it here, while
            // a refusal is still free).
            int onHand = 0;
            inputs.MillSupport.ConsumableUnits.TryGetValue(SawmillLubrication.LubricationKey, out onHand);
            if (inputs.MillRuntime.MillLubricationUnits != onHand)
            {
                result.Refusal = $"SawmillLubricationPurchase: lubrication stores disagree — runtime {inputs.MillRuntime.MillLubricationUnits} unit(s), support context {onHand} unit(s); nothing moved, resync from the runtime before restocking.";
                diag.Add(result.Refusal);
                return result;
            }

            // (d) Debit mill cash, then consume store lots; either failure refunds / moves nothing.
            int totalCents = checked(inputs.QuartsRequested * inputs.UnitPriceCents);
            string debitRefusal = inputs.MillCashPort.TryDebit(totalCents,
                $"lamp oil purchase {inputs.QuartsRequested} quart(s) from {storeBusinessId} ({inputs.TransactionId})");
            if (debitRefusal != null)
            {
                result.Refusal = $"SawmillLubricationPurchase: mill payment refused: {debitRefusal}; nothing moved.";
                diag.Add(result.Refusal);
                return result;
            }
            if (!inputs.StoreRuntime.TryConsumeRetailItemStockUnits(
                    LampOilItemId, LampOilCategoryId, inputs.QuartsRequested, out int consumed, retailSale: true)
                || consumed != inputs.QuartsRequested)
            {
                string refundRefusal = inputs.MillCashPort.Credit(totalCents,
                    $"refund refused lamp oil purchase {inputs.TransactionId}");
                result.Refusal = $"SawmillLubricationPurchase: store stock changed before checkout ({consumed}/{inputs.QuartsRequested} consumed). " +
                    (refundRefusal == null ? "Mill debit reversed." : $"Mill refund failed: {refundRefusal}");
                diag.Add(result.Refusal);
                return result;
            }

            // Store cash, once, through the single cash writer — before the
            // receipt, so a receipt refusal can reverse it (FeedToBin shape).
            inputs.StoreRuntime.RecordRetailItemSettlement(LampOilCategoryId, inputs.QuartsRequested, totalCents);

            // (e) Receive the quarts into the lubrication store.
            string receiptRefusal = SawmillLubrication.AddUnits(
                inputs.MillRuntime, inputs.MillSupport, inputs.QuartsRequested, diag);
            if (receiptRefusal != null)
            {
                // Rollback (flour shape): reverse the store credit through
                // the store cash port, credit the mill back, and say loudly
                // that the consumed lots are consumed-but-unpaid — consumption
                // cannot be undone through the public lot API. This should
                // never fire: step (b) already proved the stores agree.
                string storeRefundRefusal = inputs.StoreCashPort.TryDebit(totalCents,
                    $"rollback: receipt refused lamp oil purchase {inputs.TransactionId}");
                if (storeRefundRefusal != null)
                    diag.Add($"SawmillLubricationPurchase ROLLBACK WARNING: store cash could not return the lamp oil price ({storeRefundRefusal}); cash and stock now disagree and must be reconciled manually.");
                string millRefundRefusal = inputs.MillCashPort.Credit(totalCents,
                    $"rollback: receipt refused lamp oil purchase {inputs.TransactionId}");
                if (millRefundRefusal != null)
                    diag.Add($"SawmillLubricationPurchase ROLLBACK WARNING: mill cash could not be credited back ({millRefundRefusal}).");
                result.Refusal = $"SawmillLubricationPurchase: lubrication store refused the quarts ({receiptRefusal}); cash reversed, but the {inputs.QuartsRequested} consumed store quart(s) from lots {string.Join(", ", sourceLotIds)} are consumed-but-unpaid and must be reconciled manually.";
                diag.Add(result.Refusal);
                return result;
            }

            // (f) Record the transaction id so a retry buys nothing twice.
            inputs.MillRuntime.RecordLubricationPurchaseTransaction(inputs.TransactionId);
            result.Succeeded = true;
            result.QuartsPurchased = inputs.QuartsRequested;
            result.TotalCostCents = totalCents;
            result.SourceLotIds.AddRange(sourceLotIds);
            diag.Add($"SawmillLubricationPurchase: bought {inputs.QuartsRequested} quart(s) of lamp oil from General Store {storeBusinessId} at {inputs.UnitPriceCents}c/quart ({totalCents}c total) — mill lubrication store now {inputs.MillRuntime.MillLubricationUnits} unit(s).");
            return result;
        }
    }
}
