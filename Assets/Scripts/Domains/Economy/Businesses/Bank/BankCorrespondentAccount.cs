using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Bank
{
    /// <summary>D3B: lifecycle of a bank draft sold over the counter.</summary>
    public enum BankDraftStatus
    {
        Unspecified = 0,
        Outstanding = 1,
        Cleared = 2,
    }

    /// <summary>
    /// D3B: one bank draft — the bank's written order on its correspondent,
    /// sold to a customer who needs to pay someone out of town (Canon §16.6:
    /// buying/selling exchange, handling drafts/remittances). The buyer pays
    /// face plus the stated fee in cash; the bank owes the face through the
    /// correspondent until the draft clears.
    /// </summary>
    [Serializable]
    public sealed class BankDraft
    {
        public string DraftId = string.Empty;
        public string BuyerName = string.Empty;
        public string PayeeName = string.Empty;
        public int FaceCents;
        public int FeeCents;
        public int SoldDayIndex;
        public BankDraftStatus Status = BankDraftStatus.Outstanding;

        public BankDraft() { }
    }

    /// <summary>
    /// D3B: the bank's account with an outside (correspondent) bank
    /// (Canon §18.11, §16.6). The balance is the bank's OWN money held
    /// outside town — an asset on the balance sheet. It supports settlement,
    /// remittance and collection work:
    ///
    /// - RemitToCorrespondent: vault cash sent out becomes outside balance.
    /// - DrawOnCorrespondent: outside balance called home as vault cash.
    /// - SellDraft / ClearDraft: drafts sold over the counter are drawn on
    ///   this balance; a draft the balance cannot cover does NOT clear —
    ///   the liability stands and the shortfall is loud.
    /// - ReceiveCollectionSettlement: out-of-town collection proceeds land
    ///   here, not in the vault (the paper was presented elsewhere).
    ///
    /// HARD CONSTRAINT (NX-3B): no overdrafts, ever. Every draw, draft
    /// clearance and remittance is refused when the balance does not cover
    /// it — the bank cannot spend outside money it does not have, and the
    /// correspondent is not a money printer.
    /// </summary>
    public sealed class BankCorrespondentAccount
    {
        private string accountId = string.Empty;
        private string correspondentName = string.Empty;
        private string accountReference = string.Empty;
        private int openedDayIndex;

        private int balanceCents;
        private readonly List<BankDraft> drafts = new List<BankDraft>();
        private int nextDraftNumber = 1;

        public string AccountId => accountId ?? string.Empty;
        public string CorrespondentName => correspondentName ?? string.Empty;
        public string AccountReference => accountReference ?? string.Empty;
        public int BalanceCents => balanceCents;
        public IReadOnlyList<BankDraft> Drafts => drafts;

        public BankCorrespondentAccount() { }

        public BankCorrespondentAccount(string accountId, string correspondentName,
            string accountReference, int openedDayIndex)
        {
            this.accountId = accountId ?? string.Empty;
            this.correspondentName = correspondentName ?? string.Empty;
            this.accountReference = accountReference ?? string.Empty;
            this.openedDayIndex = Math.Max(0, openedDayIndex);
        }

        /// <summary>Face value of drafts sold but not yet cleared — a liability.</summary>
        public int DraftsOutstandingCents()
        {
            int total = 0;
            foreach (BankDraft draft in drafts)
                if (draft.Status == BankDraftStatus.Outstanding)
                    total += draft.FaceCents;
            return total;
        }

        /// <summary>
        /// What the bank can actually call home: the balance less drafts
        /// already sold against it. Drafts are spoken for — drawing against
        /// them would double-spend the same outside money.
        /// </summary>
        public int AvailableToDrawCents() => balanceCents - DraftsOutstandingCents();

        /// <summary>
        /// Sends vault cash out to the correspondent: cash out of the ledger,
        /// balance up by the same amount. An asset transfer, not income — the
        /// money was already the bank's. The operating layer pairs the specie
        /// actually sent (guarded, shipped, or handed to the express agent).
        /// </summary>
        public string RemitToCorrespondent(int amountCents, BankRuntime bank,
            int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (bank == null) return "BankCorrespondentAccount.RemitToCorrespondent: no bank.";
            if (amountCents <= 0)
                return "BankCorrespondentAccount.RemitToCorrespondent: the remittance must be positive.";
            string refused = bank.Ledger.RecordOperatingOutflow(amountCents,
                $"remittance to correspondent '{correspondentName}', day {dayIndex}", diagnostics);
            if (refused != null) return refused;
            balanceCents += amountCents;
            diagnostics.Add($"BankCorrespondentAccount [{correspondentName}]: +{amountCents}c remitted — outside balance now {balanceCents}c ({DraftsOutstandingCents()}c spoken for by drafts).");
            return null;
        }

        /// <summary>
        /// Calls outside money home: balance down, ledger cash up. Refused
        /// when the available balance does not cover it — never faked. The
        /// operating layer pairs the specie lot that physically arrives.
        /// </summary>
        public string DrawOnCorrespondent(int amountCents, BankRuntime bank,
            int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (bank == null) return "BankCorrespondentAccount.DrawOnCorrespondent: no bank.";
            if (amountCents <= 0)
                return "BankCorrespondentAccount.DrawOnCorrespondent: the draw must be positive.";
            if (amountCents > AvailableToDrawCents())
                return $"BankCorrespondentAccount.DrawOnCorrespondent: '{correspondentName}' holds {AvailableToDrawCents()}c available " +
                    $"({balanceCents}c balance less {DraftsOutstandingCents()}c in outstanding drafts) — {amountCents}c refused. No overdrafts.";
            balanceCents -= amountCents;
            string refused = bank.Ledger.RecordOperatingInflow(amountCents,
                $"drawn on correspondent '{correspondentName}', day {dayIndex}", diagnostics);
            if (refused != null)
            {
                balanceCents += amountCents;
                return refused;
            }
            diagnostics.Add($"BankCorrespondentAccount [{correspondentName}]: -{amountCents}c drawn home — outside balance now {balanceCents}c.");
            return null;
        }

        /// <summary>
        /// Out-of-town collection proceeds arrive through the correspondent
        /// (D3B collections desk). The money was the customer's paper paying
        /// off elsewhere — it lands as outside balance, never as conjured
        /// vault cash.
        /// </summary>
        public string ReceiveCollectionSettlement(int amountCents, string reference,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (amountCents <= 0)
                return "BankCorrespondentAccount.ReceiveCollectionSettlement: the settlement must be positive.";
            if (string.IsNullOrWhiteSpace(reference))
                return "BankCorrespondentAccount.ReceiveCollectionSettlement: the settlement must name its paper.";
            balanceCents += amountCents;
            diagnostics.Add($"BankCorrespondentAccount [{correspondentName}]: +{amountCents}c collection settlement ({reference}) — outside balance now {balanceCents}c.");
            return null;
        }

        /// <summary>
        /// Sells a draft over the counter (Canon §16.6: drafts/remittances).
        /// The buyer pays face + the stated fee in cash; the fee is terms as
        /// written, never invented. The bank now owes the face through the
        /// correspondent — a real liability the moment it is sold.
        /// </summary>
        public string SellDraft(string buyerName, string payeeName, int faceCents,
            int feeCents, string feeTerms, BankRuntime bank, int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (bank == null) return "BankCorrespondentAccount.SellDraft: no bank.";
            if (string.IsNullOrWhiteSpace(buyerName))
                return "BankCorrespondentAccount.SellDraft: the buyer must be named.";
            if (string.IsNullOrWhiteSpace(payeeName))
                return "BankCorrespondentAccount.SellDraft: the payee must be named — a draft is payable to someone.";
            if (faceCents <= 0)
                return "BankCorrespondentAccount.SellDraft: the draft face must be positive.";
            if (feeCents < 0)
                return "BankCorrespondentAccount.SellDraft: the fee cannot be negative.";

            string refused = bank.Ledger.RecordOperatingInflow(faceCents + feeCents,
                $"draft sale to '{buyerName}' payable to '{payeeName}', day {dayIndex}", diagnostics);
            if (refused != null) return refused;

            var draft = new BankDraft
            {
                DraftId = $"DRAFT-{accountId}-{nextDraftNumber++:D4}",
                BuyerName = buyerName,
                PayeeName = payeeName,
                FaceCents = faceCents,
                FeeCents = feeCents,
                SoldDayIndex = dayIndex,
                Status = BankDraftStatus.Outstanding,
            };
            drafts.Add(draft);
            diagnostics.Add($"BankCorrespondentAccount [{correspondentName}]: draft '{draft.DraftId}' sold — {faceCents}c to '{payeeName}' for '{buyerName}'" +
                (feeCents > 0 ? $" (fee {feeCents}c{(string.IsNullOrWhiteSpace(feeTerms) ? "" : ": " + feeTerms)})" : " (no fee)") +
                $" — drafts outstanding now {DraftsOutstandingCents()}c.");
            return null;
        }

        /// <summary>
        /// The correspondent honors a sold draft: balance down, liability
        /// retired. Refused when the outside balance cannot cover it — the
        /// draft stays outstanding and the shortfall is loud, never papered
        /// over. The bank must remit more funds first.
        /// </summary>
        public string ClearDraft(string draftId, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            BankDraft draft = null;
            foreach (BankDraft candidate in drafts)
                if (string.Equals(candidate.DraftId, draftId, StringComparison.Ordinal))
                { draft = candidate; break; }
            if (draft == null)
                return $"BankCorrespondentAccount.ClearDraft: no draft '{draftId}' on this account.";
            if (draft.Status != BankDraftStatus.Outstanding)
                return $"BankCorrespondentAccount.ClearDraft: draft '{draftId}' is {draft.Status} — already resolved.";
            if (draft.FaceCents > balanceCents)
            {
                diagnostics.Add($"BankCorrespondentAccount [{correspondentName}]: DRAFT SHORT — '{draftId}' needs {draft.FaceCents}c but the outside balance is {balanceCents}c. The draft stands; remit funds first.");
                return $"BankCorrespondentAccount.ClearDraft: '{draftId}' cannot clear — outside balance {balanceCents}c against {draft.FaceCents}c. Refused, not faked.";
            }
            balanceCents -= draft.FaceCents;
            draft.Status = BankDraftStatus.Cleared;
            diagnostics.Add($"BankCorrespondentAccount [{correspondentName}]: draft '{draftId}' cleared — {draft.FaceCents}c paid through the correspondent (day {dayIndex}). Balance now {balanceCents}c.");
            return null;
        }

        // ---------- save DTO (inside the owning runtime class) ----------

        [Serializable]
        public sealed class BankCorrespondentAccountSaveDto
        {
            public string AccountId = string.Empty;
            public string CorrespondentName = string.Empty;
            public string AccountReference = string.Empty;
            public int OpenedDayIndex;
            public int BalanceCents;
            public int NextDraftNumber = 1;
            public List<BankDraft> Drafts = new List<BankDraft>();
        }

        public BankCorrespondentAccountSaveDto ToSaveDto()
        {
            return new BankCorrespondentAccountSaveDto
            {
                AccountId = accountId,
                CorrespondentName = correspondentName,
                AccountReference = accountReference,
                OpenedDayIndex = Math.Max(0, openedDayIndex),
                BalanceCents = balanceCents,
                NextDraftNumber = Math.Max(1, nextDraftNumber),
                Drafts = new List<BankDraft>(drafts),
            };
        }

        public void LoadFromSaveDto(BankCorrespondentAccountSaveDto dto)
        {
            drafts.Clear();
            if (dto == null) return;
            accountId = dto.AccountId ?? string.Empty;
            correspondentName = dto.CorrespondentName ?? string.Empty;
            accountReference = dto.AccountReference ?? string.Empty;
            openedDayIndex = Math.Max(0, dto.OpenedDayIndex);
            balanceCents = Math.Max(0, dto.BalanceCents);
            nextDraftNumber = Math.Max(1, dto.NextDraftNumber);
            if (dto.Drafts != null)
                foreach (BankDraft draft in dto.Drafts)
                    if (draft != null && !string.IsNullOrWhiteSpace(draft.DraftId))
                        drafts.Add(draft);
        }
    }
}
