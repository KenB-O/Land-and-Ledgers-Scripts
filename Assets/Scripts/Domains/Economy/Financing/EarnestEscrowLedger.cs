// DEF-069 (Decision 12): the earnest escrow ledger.
//
// Earnest money was treated as a spend (buyer cash out, a number
// remembered on the deal) instead of a custody transfer. The forfeited
// portion of a failed deal was credited to nobody, a deal failing before
// earnest was committed could refund money that was never paid, and
// business earnest was impossible because no escrow record existed.
//
// This ledger is the single book for earnest: a distinct, traceable,
// stakeholder-named liability ledger, separate from ordinary spendable
// revenue and separate from the FinancialObligationAuthority (which is
// a debt ledger, not an escrow book). Cash moves through the existing
// cash writer (PlayerPortfolioManager); the ledger is the book, never
// the mover. Every record is conserved: the released + refunded +
// applied portions always sum to either 0 (Held/Disputed, full amount
// held) or exactly the committed amount (terminal states). No method
// ever deletes a record or reduces an amount. A refusal changes nothing.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Financing
{
    /// <summary>Terminal states are AppliedToPrice, ReleasedToSeller, RefundedToBuyer and SplitSettlement. Held and Disputed both hold the full amount.</summary>
    public enum EarnestEscrowState
    {
        Held = 0,
        Disputed = 1,
        AppliedToPrice = 2,
        ReleasedToSeller = 3,
        RefundedToBuyer = 4,
        SplitSettlement = 5
    }

    /// <summary>
    /// One earnest deposit: the buyer, the seller, the deal, the amount,
    /// the terms recorded at commit, and the settlement. History is the
    /// audit trail — every transition appends a day-stamped line.
    /// </summary>
    [Serializable]
    public sealed class EarnestEscrowRecord
    {
        public string EscrowId = string.Empty;
        public string ListingId = string.Empty;
        public string DealTitle = string.Empty;
        public string BuyerLabel = string.Empty;
        public string SellerLabel = string.Empty;
        public string KindLabel = string.Empty;
        public int AmountCents;
        public int CommittedDayIndex = -1;
        public string ForfeitTermsSummary = string.Empty;
        public EarnestEscrowState State = EarnestEscrowState.Held;
        public int ReleasedToSellerCents;
        public int RefundedToBuyerCents;
        public int AppliedToPriceCents;
        public int SettlementDayIndex = -1;
        public string SettlementReason = string.Empty;
        public List<string> History = new List<string>();
    }

    [Serializable]
    public sealed class EarnestEscrowSaveDto
    {
        public List<EarnestEscrowRecord> Records = new List<EarnestEscrowRecord>();
        public int Sequence;
    }

    /// <summary>
    /// The earnest escrow book. One open escrow per listing; every
    /// settlement enforces forfeited + refunded == committed (or
    /// seller + buyer == committed on a dispute resolution) and refuses
    /// otherwise, so money can never be created, destroyed, or double
    /// settled through this ledger. Released-to-seller funds are recorded
    /// as a claim owed to the named seller and held in the ledger until
    /// a seller-ledger path exists to settle them into (DEF-069 F2);
    /// they are never swept into any treasury balance.
    /// </summary>
    public sealed class EarnestEscrowLedger
    {
        public List<EarnestEscrowRecord> Records = new List<EarnestEscrowRecord>();

        private int sequence;

        public int TotalCommittedCents
        {
            get
            {
                int total = 0;
                foreach (EarnestEscrowRecord record in Records)
                {
                    if (record != null) total += Math.Max(0, record.AmountCents);
                }

                return total;
            }
        }

        public int TotalHeldCents => TotalInState(EarnestEscrowState.Held);

        public int TotalDisputedCents => TotalInState(EarnestEscrowState.Disputed);

        public int TotalReleasedToSellerCents
        {
            get
            {
                int total = 0;
                foreach (EarnestEscrowRecord record in Records)
                {
                    if (record != null) total += Math.Max(0, record.ReleasedToSellerCents);
                }

                return total;
            }
        }

        public int TotalRefundedToBuyerCents
        {
            get
            {
                int total = 0;
                foreach (EarnestEscrowRecord record in Records)
                {
                    if (record != null) total += Math.Max(0, record.RefundedToBuyerCents);
                }

                return total;
            }
        }

        public int TotalAppliedToPriceCents
        {
            get
            {
                int total = 0;
                foreach (EarnestEscrowRecord record in Records)
                {
                    if (record != null) total += Math.Max(0, record.AppliedToPriceCents);
                }

                return total;
            }
        }

        private int TotalInState(EarnestEscrowState state)
        {
            int total = 0;
            foreach (EarnestEscrowRecord record in Records)
            {
                if (record != null && record.State == state) total += Math.Max(0, record.AmountCents);
            }

            return total;
        }

        /// <summary>An escrow is open while it holds funds: Held or Disputed.</summary>
        public static bool IsOpen(EarnestEscrowState state)
        {
            return state == EarnestEscrowState.Held || state == EarnestEscrowState.Disputed;
        }

        public EarnestEscrowRecord Find(string escrowId)
        {
            if (string.IsNullOrWhiteSpace(escrowId)) return null;
            foreach (EarnestEscrowRecord record in Records)
            {
                if (record != null && string.Equals(record.EscrowId, escrowId, StringComparison.Ordinal))
                {
                    return record;
                }
            }

            return null;
        }

        public EarnestEscrowRecord FindOpenForListing(string listingId)
        {
            if (string.IsNullOrWhiteSpace(listingId)) return null;
            foreach (EarnestEscrowRecord record in Records)
            {
                if (record != null && IsOpen(record.State)
                    && string.Equals(record.ListingId, listingId, StringComparison.Ordinal))
                {
                    return record;
                }
            }

            return null;
        }

        /// <summary>
        /// Records a committed earnest deposit. Refuses a non-positive
        /// amount and a second open escrow for the same listing. The
        /// caller has already moved the cash out of spendable buyer cash;
        /// a refusal here means the caller must refund it immediately.
        /// </summary>
        public EarnestEscrowRecord RecordCommit(
            string listingId,
            string dealTitle,
            string buyerLabel,
            string sellerLabel,
            string kindLabel,
            int amountCents,
            int dayIndex,
            string forfeitTermsSummary,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (string.IsNullOrWhiteSpace(listingId))
            {
                diag.Add("EarnestEscrowLedger.RecordCommit: listing id required — no escrow recorded.");
                return null;
            }

            if (amountCents <= 0)
            {
                diag.Add($"EarnestEscrowLedger.RecordCommit: amount {amountCents}c is not positive — no escrow recorded for '{listingId}'.");
                return null;
            }

            if (FindOpenForListing(listingId) != null)
            {
                diag.Add($"EarnestEscrowLedger.RecordCommit: listing '{listingId}' already has an open escrow — refusing a second one.");
                return null;
            }

            sequence++;
            var record = new EarnestEscrowRecord
            {
                EscrowId = "escrow-" + sequence,
                ListingId = listingId,
                DealTitle = dealTitle ?? string.Empty,
                BuyerLabel = string.IsNullOrWhiteSpace(buyerLabel) ? "Player" : buyerLabel,
                SellerLabel = string.IsNullOrWhiteSpace(sellerLabel) ? "Town" : sellerLabel,
                KindLabel = kindLabel ?? string.Empty,
                AmountCents = amountCents,
                CommittedDayIndex = dayIndex,
                ForfeitTermsSummary = forfeitTermsSummary ?? string.Empty,
                State = EarnestEscrowState.Held,
            };
            record.History.Add($"day {dayIndex}: committed {amountCents}c from {record.BuyerLabel} for '{record.DealTitle}' (seller {record.SellerLabel}). Terms: {record.ForfeitTermsSummary}");
            Records.Add(record);
            diag.Add($"EarnestEscrowLedger: recorded {record.EscrowId} — {amountCents}c held for '{listingId}' (seller {record.SellerLabel}).");
            return record;
        }

        /// <summary>
        /// The deal closed: the earnest is part of the price. Held →
        /// AppliedToPrice for the full amount. Refuses any other state.
        /// </summary>
        public string ApplyToPrice(string escrowId, int dayIndex, string reason, List<string> diag)
        {
            diag = diag ?? new List<string>();
            EarnestEscrowRecord record = Find(escrowId);
            if (record == null)
            {
                return $"EarnestEscrowLedger.ApplyToPrice: escrow '{escrowId}' is unknown.";
            }

            if (record.State != EarnestEscrowState.Held)
            {
                return $"EarnestEscrowLedger.ApplyToPrice: escrow '{escrowId}' is {record.State}, not Held — refusing.";
            }

            record.AppliedToPriceCents = Math.Max(0, record.AmountCents);
            record.State = EarnestEscrowState.AppliedToPrice;
            record.SettlementDayIndex = dayIndex;
            record.SettlementReason = reason ?? string.Empty;
            record.History.Add($"day {dayIndex}: applied {record.AppliedToPriceCents}c to the closing price. {record.SettlementReason}");
            diag.Add($"EarnestEscrowLedger: {escrowId} applied {record.AppliedToPriceCents}c to the price.");
            return null;
        }

        /// <summary>
        /// The deal failed: settle the held earnest between the seller
        /// (forfeited) and the buyer (refunded). Held → ReleasedToSeller,
        /// RefundedToBuyer, or SplitSettlement. Enforces forfeited +
        /// refunded == committed; refuses any mismatch and any non-Held
        /// state (a Disputed escrow settles only through ResolveDispute,
        /// so a dispute can never be bypassed by the failure path).
        /// </summary>
        public string SettleFailure(string escrowId, int forfeitedCents, int refundableCents, int dayIndex, string reason, List<string> diag)
        {
            diag = diag ?? new List<string>();
            EarnestEscrowRecord record = Find(escrowId);
            if (record == null)
            {
                return $"EarnestEscrowLedger.SettleFailure: escrow '{escrowId}' is unknown.";
            }

            return Settle(record, forfeitedCents, refundableCents, dayIndex, reason, diag, "SettleFailure");
        }

        /// <summary>
        /// Marks a held escrow disputed: the funds are preserved in the
        /// ledger, no release and no refund, until the dispute is
        /// resolved through ResolveDispute. Refuses any other state.
        /// </summary>
        public string Dispute(string escrowId, int dayIndex, string reason, List<string> diag)
        {
            diag = diag ?? new List<string>();
            EarnestEscrowRecord record = Find(escrowId);
            if (record == null)
            {
                return $"EarnestEscrowLedger.Dispute: escrow '{escrowId}' is unknown.";
            }

            if (record.State != EarnestEscrowState.Held)
            {
                return $"EarnestEscrowLedger.Dispute: escrow '{escrowId}' is {record.State}, not Held — refusing.";
            }

            record.State = EarnestEscrowState.Disputed;
            record.SettlementReason = reason ?? string.Empty;
            record.History.Add($"day {dayIndex}: disputed — {record.AmountCents}c preserved in escrow. {record.SettlementReason}");
            diag.Add($"EarnestEscrowLedger: {escrowId} disputed; {record.AmountCents}c preserved.");
            return null;
        }

        /// <summary>
        /// Resolves a disputed escrow through the legal or negotiated
        /// process: the resolution decides the split, the ledger enforces
        /// seller + buyer == committed and records the outcome. Disputed →
        /// ReleasedToSeller, RefundedToBuyer, or SplitSettlement.
        /// </summary>
        public string ResolveDispute(string escrowId, int sellerCents, int buyerCents, int dayIndex, string resolution, List<string> diag)
        {
            diag = diag ?? new List<string>();
            EarnestEscrowRecord record = Find(escrowId);
            if (record == null)
            {
                return $"EarnestEscrowLedger.ResolveDispute: escrow '{escrowId}' is unknown.";
            }

            if (record.State != EarnestEscrowState.Disputed)
            {
                return $"EarnestEscrowLedger.ResolveDispute: escrow '{escrowId}' is {record.State}, not Disputed — refusing.";
            }

            return Settle(record, sellerCents, buyerCents, dayIndex, resolution, diag, "ResolveDispute");
        }

        private string Settle(EarnestEscrowRecord record, int sellerCents, int buyerCents, int dayIndex, string reason, List<string> diag, string operation)
        {
            if (record.State != EarnestEscrowState.Held && !(operation == "ResolveDispute" && record.State == EarnestEscrowState.Disputed))
            {
                return $"EarnestEscrowLedger.{operation}: escrow '{record.EscrowId}' is {record.State} — refusing.";
            }

            if (sellerCents < 0 || buyerCents < 0)
            {
                return $"EarnestEscrowLedger.{operation}: escrow '{record.EscrowId}' refuses negative portions (seller {sellerCents}c, buyer {buyerCents}c).";
            }

            if (sellerCents + buyerCents != Math.Max(0, record.AmountCents))
            {
                return $"EarnestEscrowLedger.{operation}: escrow '{record.EscrowId}' refuses a split that does not conserve the deposit (seller {sellerCents}c + buyer {buyerCents}c != {record.AmountCents}c held).";
            }

            record.ReleasedToSellerCents = sellerCents;
            record.RefundedToBuyerCents = buyerCents;
            record.State = sellerCents > 0 && buyerCents > 0
                ? EarnestEscrowState.SplitSettlement
                : sellerCents > 0
                    ? EarnestEscrowState.ReleasedToSeller
                    : EarnestEscrowState.RefundedToBuyer;
            record.SettlementDayIndex = dayIndex;
            record.SettlementReason = reason ?? string.Empty;
            record.History.Add($"day {dayIndex}: settled {operation} — {sellerCents}c released to {record.SellerLabel}, {buyerCents}c refunded to {record.BuyerLabel}. {record.SettlementReason}");
            diag.Add($"EarnestEscrowLedger: {record.EscrowId} settled — {sellerCents}c to {record.SellerLabel}, {buyerCents}c to {record.BuyerLabel}.");
            return null;
        }

        public EarnestEscrowSaveDto CaptureSaveDto()
        {
            var dto = new EarnestEscrowSaveDto { Sequence = sequence };
            foreach (EarnestEscrowRecord record in Records)
            {
                if (record != null) dto.Records.Add(record);
            }

            return dto;
        }

        /// <summary>
        /// Clear-then-load and idempotent. Duplicate ids are skipped, old
        /// saves (null DTO) start empty, and the sequence continues past
        /// the highest restored id so a later commit never re-mints one.
        /// </summary>
        public void LoadFromSaveDto(EarnestEscrowSaveDto dto, List<string> diag)
        {
            diag = diag ?? new List<string>();
            Records.Clear();
            if (dto == null || dto.Records == null)
            {
                sequence = 0;
                return;
            }

            foreach (EarnestEscrowRecord record in dto.Records)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.EscrowId)) continue;
                if (Find(record.EscrowId) != null)
                {
                    diag.Add($"EarnestEscrowLedger.LoadFromSaveDto: duplicate escrow '{record.EscrowId}' skipped.");
                    continue;
                }

                Records.Add(record);
            }

            int maxSuffix = -1;
            foreach (EarnestEscrowRecord record in Records)
            {
                maxSuffix = Math.Max(maxSuffix, TrailingNumber(record.EscrowId));
            }

            // RecordCommit pre-increments the sequence before minting, so
            // the restored sequence is the LAST USED value, not one past
            // it: the next commit increments to maxSuffix + 1. (Setting it
            // to maxSuffix + 1 here would skip an id — the T11 failure.)
            sequence = Math.Max(dto.Sequence, maxSuffix);
        }

        /// <summary>Numeric suffix after the last '-' in an id, or -1 when there is none.</summary>
        private static int TrailingNumber(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            int dash = id.LastIndexOf('-');
            if (dash < 0 || dash == id.Length - 1) return -1;
            int value;
            return int.TryParse(id.Substring(dash + 1), out value) ? value : -1;
        }
    }
}
