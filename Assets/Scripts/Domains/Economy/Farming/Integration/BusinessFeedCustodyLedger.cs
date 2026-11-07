using System.Collections.Generic;
using LandLedgers.Economy;

namespace LandLedgers.Economy.Farming.Integration
{
    /// <summary>
    /// W4 (F-1) — runtime-side custody ledger for purchased feed batches
    /// whose return-leg receipt was refused. A batch is held here keyed by
    /// its TransactionId from the refusal until the next feed drive retries
    /// the receipt. The batch's cash already moved at purchase and the
    /// store's stock was already consumed, so losing the batch would lose
    /// paid-for goods: the ledger is what makes the "retained" log line in
    /// the return-stage handler true.
    ///
    /// The ledger is deliberately runtime-side, not serialized. The durable
    /// seam for trip data is the WorkTask TermsRef (the trip JSON rides on
    /// the task through save/load), and a refused receipt has no live task
    /// left to ride on — persisting the batch durably would need a
    /// save-format change, which this slice does not make. A save/load
    /// between refusal and retry drops the held batch; the need stays open
    /// (businessFeedNeedsOpen is runtime-side too), so the next drive buys
    /// fresh feed. That gap is recorded in the checkpoint, not hidden.
    ///
    /// A retry reserves storage capacity for the batch first, mirroring
    /// the pre-purchase reservation in DriveBusinessHorseFeedProcurement:
    /// a capacity refusal at receipt means the stock/capacity balance
    /// moved after purchase, and the retry repairs it rather than
    /// re-refusing on the same cause. The retry never purchases and never
    /// touches cash — the batch is the same paid-for batch, so cash moves
    /// once (at purchase) by construction.
    /// </summary>
    public sealed class BusinessFeedCustodyLedger
    {
        private readonly Dictionary<string, BusinessFeedPurchase.Batch> pending =
            new Dictionary<string, BusinessFeedPurchase.Batch>(System.StringComparer.Ordinal);

        public int PendingCount => pending.Count;

        /// <summary>True when a batch for this buyer is held awaiting retry.</summary>
        public bool HasPendingForBuyer(string buyerBusinessId)
        {
            if (string.IsNullOrWhiteSpace(buyerBusinessId)) return false;
            foreach (BusinessFeedPurchase.Batch batch in pending.Values)
                if (batch != null && string.Equals(batch.BuyerBusinessId, buyerBusinessId, System.StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>
        /// Holds a refused batch keyed by its TransactionId. Returns false
        /// when the batch carries no transaction id (nothing to key it by)
        /// or the same transaction is already held — the held batch is the
        /// one that gets retried, never a second copy.
        /// </summary>
        public bool TryHold(BusinessFeedPurchase.Batch batch)
        {
            if (batch == null || !batch.HasValidCustodyIdentity) return false;
            if (pending.ContainsKey(batch.TransactionId)) return false;
            pending[batch.TransactionId] = batch;
            return true;
        }

        /// <summary>Peeks the held batch for a buyer without removing it.</summary>
        public bool TryPeekForBuyer(string buyerBusinessId, out BusinessFeedPurchase.Batch batch)
        {
            batch = null;
            if (string.IsNullOrWhiteSpace(buyerBusinessId)) return false;
            foreach (BusinessFeedPurchase.Batch held in pending.Values)
            {
                if (held != null && string.Equals(held.BuyerBusinessId, buyerBusinessId, System.StringComparison.Ordinal))
                {
                    batch = held;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Retries the held receipt for one buyer: reserves storage capacity
        /// for the batch first, then calls ReceiveAtBusiness. On success —
        /// including an idempotent duplicate receipt, which ReceiveAtBusiness
        /// reports as Succeeded with Duplicate — the batch leaves the ledger.
        /// On refusal it stays held for the next day's feed drive. Returns
        /// false (with a refusal naming the missing batch) when no batch is
        /// held for the buyer.
        /// </summary>
        public bool TryRetryForBuyer(string buyerBusinessId, BusinessRuntimeState destination,
            out BusinessFeedPurchase.Result result)
        {
            result = new BusinessFeedPurchase.Result();
            if (!TryPeekForBuyer(buyerBusinessId, out BusinessFeedPurchase.Batch batch))
            {
                result.Refusal = $"No pending custody batch is held for buyer '{buyerBusinessId}'.";
                return false;
            }
            result.Batch = batch;
            if (destination == null)
            {
                result.Refusal = $"Pending custody batch '{batch.TransactionId}' cannot be retried: no destination business.";
                return true;
            }

            CategoryStockState category = destination.GetCategoryStock(batch.CategoryId);
            int storageNeeded = (category?.CurrentStockUnits ?? 0) + batch.Units;
            if ((category?.TargetStockUnits ?? 0) < storageNeeded)
                destination.EnsureCategoryStorageCapacity(batch.CategoryId, storageNeeded);

            result = BusinessFeedPurchase.ReceiveAtBusiness(batch, destination);
            if (result.Succeeded) pending.Remove(batch.TransactionId);
            return true;
        }
    }
}
