using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Bank
{
    /// <summary>W7C: one bank note the desk issued — the liability half.</summary>
    [Serializable]
    public sealed class IssuedBankNote
    {
        public EntityId InstrumentId = EntityId.Invalid;
        public int FaceCents;
        public string HolderName = string.Empty;
        public int IssuedDayIndex;
        public bool Redeemed;

        public IssuedBankNote() { }
    }

    /// <summary>
    /// W7C: third-party commercial paper the bank bought at the discount
    /// window — the asset half. The bank paid PaidCents for FaceCents of
    /// maker promise; the difference is profit only if collected in full.
    ///
    /// D3B: customer paper discounted over the counter carries endorser
    /// recourse (period practice) — the customer who endorsed the paper to
    /// the bank answers for it if the maker defaults, through a registered
    /// T2A guaranty (GuarantyInstrumentId). Desk-selected purchases have no
    /// recourse: the bank bought the risk as priced.
    /// </summary>
    [Serializable]
    public sealed class DiscountedPaper
    {
        public EntityId InstrumentId = EntityId.Invalid;
        public string MakerName = string.Empty;
        public int FaceCents;
        public int PaidCents;
        public int DiscountedDayIndex;
        public bool Collected;
        /// <summary>D3B: the customer who endorsed this paper to the bank (empty when desk-selected).</summary>
        public string CustomerEndorserName = string.Empty;
        /// <summary>D3B: true when the endorser remains liable on maker default.</summary>
        public bool HasRecourse;
        /// <summary>D3B: registry key of the endorser's guaranty (empty when no recourse).</summary>
        public string GuarantyInstrumentId = string.Empty;

        public DiscountedPaper() { }
    }

    /// <summary>
    /// W7C: the note issuance desk, per Canon banking. The desk WRAPS the
    /// T2A/NX-3B instruments (CreditRegistry promissory notes, the
    /// BankDepositLedger, vault specie lots) — it never bypasses them:
    ///
    /// - IssueNotesForLoan: the borrower signs a loan note TO the bank (the
    ///   asset half, registered); the bank pays out its OWN bank notes
    ///   (the liability half, registered as bearer notes payable on demand
    ///   in specie). No specie moves — the notes ARE the disbursement.
    /// - IssueNotesForSpecie: specie in, bank notes out — a demand liability
    ///   in bearer-paper form instead of an account.
    /// - RedeemNotes: the holder presents notes; the bank pays specie from
    ///   the vault and retires the instruments. Refused when uncovered —
    ///   never faked (Canon §18.10).
    /// - DiscountNote / CollectDiscountedNote: the discount window buys
    ///   third-party commercial paper below face, paying cash the bank
    ///   actually holds (finite capital — DisburseLoan refuses otherwise).
    ///
    /// HARD CONSTRAINT (NX-3B): the bank creates NO money. Issuance gate
    /// (W7C calibration — canon sets no backing ratio, recorded fork):
    /// notes outstanding + new issue must not exceed vault specie at
    /// issuance. Every note is a demand claim the bank could meet from the
    /// safe that day.
    /// </summary>
    public sealed class NoteDesk
    {
        public const string BearerPayeeName = "bearer";

        private readonly BankRuntime bank;
        private readonly CreditRegistry credit;
        private readonly EntityIdRegistry ids;

        private readonly List<IssuedBankNote> issuedNotes = new List<IssuedBankNote>();
        private readonly List<DiscountedPaper> discountedPaper = new List<DiscountedPaper>();

        public BankRuntime Bank => bank;
        public CreditRegistry Credit => credit;
        public IReadOnlyList<IssuedBankNote> IssuedNotes => issuedNotes;
        public IReadOnlyList<DiscountedPaper> DiscountedPaper => discountedPaper;

        public NoteDesk(BankRuntime bank, CreditRegistry credit, EntityIdRegistry ids)
        {
            this.bank = bank;
            this.credit = credit;
            this.ids = ids;
        }

        public static string BankNoteTerms() => "bank note payable to bearer on demand in specie";

        /// <summary>Face value of unredeemed bank notes — a demand liability.</summary>
        public int NotesOutstandingCents()
        {
            int total = 0;
            foreach (IssuedBankNote note in issuedNotes)
                if (!note.Redeemed) total += note.FaceCents;
            return total;
        }

        private string DeskReady(List<string> diagnostics)
        {
            if (bank == null || credit == null || ids == null)
                return "NoteDesk: the desk needs a bank, a credit registry, and an id registry.";
            return null;
        }

        /// <summary>
        /// The conservative issuance gate: outstanding notes plus the new
        /// issue must be covered by vault specie. Calibration (canon is
        /// silent on the backing ratio) — recorded fork: partial-reserve
        /// note issue is NOT implemented.
        /// </summary>
        private string CheckBacking(int additionalCents, List<string> diagnostics)
        {
            int outstanding = NotesOutstandingCents();
            int specie = bank.VaultSpecieTotalCents();
            if (outstanding + additionalCents > specie)
            {
                diagnostics.Add($"NoteDesk [{bank.BusinessName}]: ISSUANCE REFUSED — {outstanding}c outstanding + {additionalCents}c new exceeds {specie}c vault specie. Notes are issued against real reserves, never conjured.");
                return $"NoteDesk: cannot issue {additionalCents}c — only {specie - outstanding}c of specie-backed note capacity remains.";
            }
            return null;
        }

        /// <summary>
        /// Issues bank notes as a loan disbursement. Two registered halves:
        /// the borrower's loan note to the bank (asset) and the bank's bearer
        /// notes (liability). The notes are the disbursement — no specie
        /// moves, and the gate keeps every note specie-backed.
        ///
        /// D3B: when a loan book is supplied, the borrower's loan note is
        /// registered on it as an earning asset (balance-sheet consolidation,
        /// Canon §16.4). Existing callers are unaffected.
        /// </summary>
        public string IssueNotesForLoan(string borrowerName, int faceAmountCents,
            string loanTerms, int dayIndex, List<string> diagnostics,
            BankLoanBook loanBook = null)
        {
            diagnostics = diagnostics ?? new List<string>();
            string ready = DeskReady(diagnostics);
            if (ready != null) return ready;
            if (string.IsNullOrWhiteSpace(borrowerName))
                return "NoteDesk.IssueNotesForLoan: the borrower must be named — no anonymous debtors.";
            if (faceAmountCents <= 0)
                return "NoteDesk.IssueNotesForLoan: the note face must be positive.";
            string backing = CheckBacking(faceAmountCents, diagnostics);
            if (backing != null) return backing;

            PromissoryNote loanNote = credit.IssuePromissoryNote(ids, borrowerName, bank.BusinessName,
                faceAmountCents,
                string.IsNullOrWhiteSpace(loanTerms) ? "demand loan" : loanTerms,
                dayIndex, null, diagnostics);
            if (loanNote == null)
                return "NoteDesk.IssueNotesForLoan: the loan note could not be registered — no half-issued loans.";

            PromissoryNote bankNote = credit.IssuePromissoryNote(ids, bank.BusinessName, BearerPayeeName,
                faceAmountCents, BankNoteTerms(), dayIndex, null, diagnostics);
            if (bankNote == null)
            {
                diagnostics.Add($"NoteDesk [{bank.BusinessName}]: DIVERGENCE — loan note '{loanNote.InstrumentId}' stands with no matching bank notes. Resolve by hand; nothing was conjured or erased.");
                return "NoteDesk.IssueNotesForLoan: the bank notes could not be registered — the loan note stands unmatched (see diagnostics).";
            }
            bankNote.HolderName = borrowerName;

            issuedNotes.Add(new IssuedBankNote
            {
                InstrumentId = bankNote.InstrumentId,
                FaceCents = faceAmountCents,
                HolderName = borrowerName,
                IssuedDayIndex = dayIndex,
            });
            if (loanBook != null)
            {
                string bookRefusal = loanBook.RegisterBorrowerNote(loanNote.InstrumentId,
                    borrowerName, faceAmountCents, dayIndex, false, diagnostics);
                if (bookRefusal != null)
                    diagnostics.Add($"NoteDesk [{bank.BusinessName}]: BOOKING GAP — the loan note stands but is not on the loan book: {bookRefusal}");
            }
            diagnostics.Add($"NoteDesk [{bank.BusinessName}]: issued {faceAmountCents}c in bank notes to '{borrowerName}' against loan note '{loanNote.InstrumentId}' — outstanding now {NotesOutstandingCents()}c on {bank.VaultSpecieTotalCents()}c vault specie.");
            return null;
        }

        /// <summary>
        /// Issues bank notes against a specie deposit: specie in (real lot),
        /// bearer notes out. A demand liability in paper form instead of an
        /// account entry.
        /// </summary>
        public string IssueNotesForSpecie(string holderName, SpecieLot specieLot,
            int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            string ready = DeskReady(diagnostics);
            if (ready != null) return ready;
            if (string.IsNullOrWhiteSpace(holderName))
                return "NoteDesk.IssueNotesForSpecie: the note holder must be named.";
            if (specieLot == null || specieLot.AmountCents <= 0)
                return "NoteDesk.IssueNotesForSpecie: real specie is required — notes are issued against reserves, not promises.";
            string backing = CheckBacking(specieLot.AmountCents, diagnostics);
            if (backing != null) return backing;

            string lotRefusal = bank.ReceiveVaultLot(specieLot, dayIndex, diagnostics);
            if (lotRefusal != null) return lotRefusal;
            string inflowRefusal = bank.Ledger.RecordNoteBackedSpecieInflow(
                specieLot.AmountCents, $"notes-{dayIndex}-{holderName}", diagnostics);
            if (inflowRefusal != null)
            {
                bank.VoidVaultLot(specieLot.LotId, diagnostics);
                return inflowRefusal;
            }

            PromissoryNote bankNote = credit.IssuePromissoryNote(ids, bank.BusinessName, BearerPayeeName,
                specieLot.AmountCents, BankNoteTerms(), dayIndex, null, diagnostics);
            if (bankNote == null)
            {
                bank.VoidVaultLot(specieLot.LotId, diagnostics);
                bank.Ledger.RecordNoteRedemptionOutflow(specieLot.AmountCents,
                    $"notes-{dayIndex}-{holderName} (voided)", diagnostics);
                return "NoteDesk.IssueNotesForSpecie: the notes could not be registered — the specie was handed back.";
            }
            bankNote.HolderName = holderName;

            issuedNotes.Add(new IssuedBankNote
            {
                InstrumentId = bankNote.InstrumentId,
                FaceCents = specieLot.AmountCents,
                HolderName = holderName,
                IssuedDayIndex = dayIndex,
            });
            diagnostics.Add($"NoteDesk [{bank.BusinessName}]: issued {specieLot.AmountCents}c in bank notes to '{holderName}' against specie received — outstanding now {NotesOutstandingCents()}c.");
            return null;
        }

        /// <summary>
        /// Redemption on demand: the holder presents whole bank notes; the
        /// bank pays specie from the vault and retires the instruments.
        /// Refused when the notes are not outstanding or the vault cannot
        /// cover — never faked (Canon §18.10). Returns the specie lots paid,
        /// or null on refusal (reason in diagnostics).
        /// </summary>
        public List<SpecieLot> RedeemNotes(string holderName, int amountCents,
            int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            string ready = DeskReady(diagnostics);
            if (ready != null)
            {
                diagnostics.Add(ready);
                return null;
            }
            if (amountCents <= 0)
            {
                diagnostics.Add("NoteDesk.RedeemNotes: the redemption amount must be positive.");
                return null;
            }
            if (NotesOutstandingCents() < amountCents)
            {
                diagnostics.Add($"NoteDesk [{bank.BusinessName}]: REDEMPTION REFUSED — {amountCents}c presented but only {NotesOutstandingCents()}c of this bank's notes are outstanding.");
                return null;
            }

            // Whole notes only, oldest first — bearer paper is fungible but
            // instruments are not divisible.
            var redeemable = new List<IssuedBankNote>();
            int covered = 0;
            foreach (IssuedBankNote note in issuedNotes)
            {
                if (note.Redeemed) continue;
                if (covered + note.FaceCents > amountCents) break;
                redeemable.Add(note);
                covered += note.FaceCents;
                if (covered == amountCents) break;
            }
            if (covered != amountCents)
            {
                int smallest = int.MaxValue;
                foreach (IssuedBankNote note in issuedNotes)
                    if (!note.Redeemed && note.FaceCents < smallest) smallest = note.FaceCents;
                diagnostics.Add($"NoteDesk [{bank.BusinessName}]: REDEMPTION REFUSED — {amountCents}c cannot be made from whole outstanding notes (smallest outstanding: {smallest}c). Present whole notes.");
                return null;
            }

            if (bank.VaultSpecieTotalCents() < amountCents)
            {
                diagnostics.Add($"NoteDesk [{bank.BusinessName}]: REDEMPTION REFUSED — the vault holds {bank.VaultSpecieTotalCents()}c against {amountCents}c presented. Refused, not faked (Canon §18.10).");
                return null;
            }

            string outflowRefusal = bank.Ledger.RecordNoteRedemptionOutflow(
                amountCents, $"redemption-{dayIndex}-{holderName}", diagnostics);
            if (outflowRefusal != null) return null;

            List<SpecieLot> paid = bank.DrawVaultLots(amountCents, diagnostics);
            if (paid == null)
            {
                bank.Ledger.RecordNoteBackedSpecieInflow(amountCents,
                    $"redemption-{dayIndex}-{holderName} (voided)", diagnostics);
                diagnostics.Add($"NoteDesk [{bank.BusinessName}]: LEDGER/VAULT DIVERGENCE — redemption approved but the vault cannot produce {amountCents}c. The ledger outflow was reversed; count the vault now.");
                return null;
            }

            foreach (IssuedBankNote note in redeemable)
            {
                note.Redeemed = true;
                credit.SatisfyInstrument(note.InstrumentId.ToString(), diagnostics);
            }
            diagnostics.Add($"NoteDesk [{bank.BusinessName}]: redeemed {amountCents}c in {redeemable.Count} notes for '{holderName}' — paid in specie, notes retired. Outstanding now {NotesOutstandingCents()}c.");
            return paid;
        }

        /// <summary>
        /// The discount window: the bank buys a third-party promissory note
        /// below face. Finite capital — the purchase moves ledger cash AND
        /// vault specie together, and DisburseLoan refuses what the bank does
        /// not hold. The bank's own paper is never discounted (that is
        /// issuance, a different flow).
        ///
        /// D3B: when a loan book is supplied, the paper is registered on it
        /// at paid cost. Existing callers are unaffected.
        /// </summary>
        public string DiscountNote(EntityId instrumentId, int priceCents,
            int dayIndex, List<string> diagnostics, BankLoanBook loanBook = null)
        {
            diagnostics = diagnostics ?? new List<string>();
            string ready = DeskReady(diagnostics);
            if (ready != null) return ready;
            if (!credit.TryGetPromissoryNote(instrumentId, out PromissoryNote note) || note == null)
                return $"NoteDesk.DiscountNote: no promissory note '{instrumentId}' on the registry — the desk discounts real paper, not rumors.";
            if (note.Status != CreditInstrumentStatus.Active)
                return $"NoteDesk.DiscountNote: note '{instrumentId}' is {note.Status} — only live paper is discounted.";
            if (string.Equals(note.MakerName, bank.BusinessName, StringComparison.Ordinal))
                return $"NoteDesk.DiscountNote: note '{instrumentId}' is the bank's own paper — the desk discounts third-party paper, not its own notes.";
            if (priceCents <= 0)
                return "NoteDesk.DiscountNote: the price must be positive.";
            if (priceCents > note.PrincipalCents)
                return $"NoteDesk.DiscountNote: {priceCents}c for {note.PrincipalCents}c face is a premium, not a discount — refused.";
            foreach (DiscountedPaper paper in discountedPaper)
                if (!paper.Collected && paper.InstrumentId.Equals(instrumentId))
                    return $"NoteDesk.DiscountNote: note '{instrumentId}' is already on the bank's books.";

            string refused = bank.Ledger.DisburseLoan(priceCents, $"discount-{instrumentId}", diagnostics);
            if (refused != null) return refused;
            List<SpecieLot> drawn = bank.DrawVaultLots(priceCents, diagnostics);
            if (drawn == null)
            {
                bank.Ledger.ReceiveLoanPayment(priceCents, $"discount-{instrumentId} (voided)", diagnostics);
                diagnostics.Add($"NoteDesk [{bank.BusinessName}]: the vault could not produce {priceCents}c — the ledger disbursement was reversed, the discount is refused.");
                return $"NoteDesk.DiscountNote: the vault cannot produce {priceCents}c — refused.";
            }

            note.HolderName = bank.BusinessName;
            var paper = new DiscountedPaper
            {
                InstrumentId = instrumentId,
                MakerName = note.MakerName,
                FaceCents = note.PrincipalCents,
                PaidCents = priceCents,
                DiscountedDayIndex = dayIndex,
            };
            discountedPaper.Add(paper);
            if (loanBook != null)
            {
                string bookRefusal = loanBook.RegisterDiscountedPaper(paper, diagnostics);
                if (bookRefusal != null)
                    diagnostics.Add($"NoteDesk [{bank.BusinessName}]: BOOKING GAP — discounted paper stands but is not on the loan book: {bookRefusal}");
            }
            diagnostics.Add($"NoteDesk [{bank.BusinessName}]: discounted '{instrumentId}' ({note.MakerName} → {note.PayeeName}, face {note.PrincipalCents}c) for {priceCents}c — {note.PrincipalCents - priceCents}c discount profit if collected in full.");
            return null;
        }

        /// <summary>
        /// Collects discounted paper at full face when the maker pays, in
        /// specie. The discount profit realizes as cash (equity up, no new
        /// liability) — the honest end of the discount window.
        ///
        /// D3B: when a loan book is supplied, the asset retires from the
        /// books. Existing callers are unaffected.
        /// </summary>
        public string CollectDiscountedNote(EntityId instrumentId, int dayIndex,
            List<string> diagnostics, BankLoanBook loanBook = null)
        {
            diagnostics = diagnostics ?? new List<string>();
            string ready = DeskReady(diagnostics);
            if (ready != null) return ready;
            DiscountedPaper paper = null;
            foreach (DiscountedPaper candidate in discountedPaper)
                if (!candidate.Collected && candidate.InstrumentId.Equals(instrumentId))
                { paper = candidate; break; }
            if (paper == null)
                return $"NoteDesk.CollectDiscountedNote: note '{instrumentId}' is not on the bank's books.";
            if (!credit.TryGetPromissoryNote(instrumentId, out PromissoryNote note) || note == null)
                return $"NoteDesk.CollectDiscountedNote: note '{instrumentId}' is not on the registry.";
            if (note.Status != CreditInstrumentStatus.Active)
                return $"NoteDesk.CollectDiscountedNote: note '{instrumentId}' is {note.Status}.";

            var lot = new SpecieLot
            {
                Kind = VaultSpecieKind.GoldCoin,
                KindName = "specie",
                AmountCents = paper.FaceCents,
                SourceName = $"collection on {instrumentId} ({paper.MakerName})",
            };
            string lotRefusal = bank.ReceiveVaultLot(lot, dayIndex, diagnostics);
            if (lotRefusal != null) return lotRefusal;
            string payRefusal = bank.Ledger.ReceiveLoanPayment(paper.FaceCents, $"collection-{instrumentId}", diagnostics);
            if (payRefusal != null)
            {
                bank.VoidVaultLot(lot.LotId, diagnostics);
                return payRefusal;
            }

            credit.SatisfyInstrument(instrumentId.ToString(), diagnostics);
            paper.Collected = true;
            if (loanBook != null)
            {
                string bookRefusal = loanBook.MarkAssetCollected(instrumentId, diagnostics);
                if (bookRefusal != null)
                    diagnostics.Add($"NoteDesk [{bank.BusinessName}]: BOOKING GAP — paper collected but the book entry did not retire: {bookRefusal}");
            }
            diagnostics.Add($"NoteDesk [{bank.BusinessName}]: collected {paper.FaceCents}c on '{instrumentId}' — discount profit {paper.FaceCents - paper.PaidCents}c realized in cash.");
            return null;
        }

        /// <summary>
        /// D3B: the customer-facing discount window. A customer endorses
        /// their note receivable to the bank and walks away with cash today
        /// at the stated price — the classic "discounting commercial paper."
        /// Period practice, and the honest version of it: the ENDORSER
        /// remains liable. A T2A guaranty is registered (guarantor = customer,
        /// creditor = bank, debtor = maker, exposure = face), so a maker
        /// default calls the endorser through RecordDiscountDefault instead
        /// of vanishing into a write-off. Finite capital — the bank pays cash
        /// it holds, exactly like the desk-selected DiscountNote.
        ///
        /// The maker discounting their OWN note is refused here — that is a
        /// loan application, a different flow with its own underwriting.
        /// </summary>
        public string DiscountCustomerPaper(string customerName, EntityId instrumentId,
            int priceCents, string terms, int dayIndex, BankLoanBook loanBook,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            string ready = DeskReady(diagnostics);
            if (ready != null) return ready;
            if (string.IsNullOrWhiteSpace(customerName))
                return "NoteDesk.DiscountCustomerPaper: the customer must be named — the bank discounts paper for someone.";
            if (!credit.TryGetPromissoryNote(instrumentId, out PromissoryNote note) || note == null)
                return $"NoteDesk.DiscountCustomerPaper: no promissory note '{instrumentId}' on the registry — the desk discounts real paper, not rumors.";
            if (note.Status != CreditInstrumentStatus.Active)
                return $"NoteDesk.DiscountCustomerPaper: note '{instrumentId}' is {note.Status} — only live paper is discounted.";
            if (string.Equals(note.MakerName, customerName, StringComparison.Ordinal))
                return $"NoteDesk.DiscountCustomerPaper: '{customerName}' is the maker of '{instrumentId}' — a maker discounting their own note is borrowing, not discounting. Route to a loan.";
            if (string.Equals(note.MakerName, bank.BusinessName, StringComparison.Ordinal))
                return $"NoteDesk.DiscountCustomerPaper: note '{instrumentId}' is the bank's own paper — the desk discounts third-party paper, not its own notes.";
            if (priceCents <= 0)
                return "NoteDesk.DiscountCustomerPaper: the price must be positive.";
            if (priceCents > note.PrincipalCents)
                return $"NoteDesk.DiscountCustomerPaper: {priceCents}c for {note.PrincipalCents}c face is a premium, not a discount — refused.";
            foreach (DiscountedPaper existing in discountedPaper)
                if (!existing.Collected && existing.InstrumentId.Equals(instrumentId))
                    return $"NoteDesk.DiscountCustomerPaper: note '{instrumentId}' is already on the bank's books.";

            string refused = bank.Ledger.DisburseLoan(priceCents, $"customer-discount-{instrumentId}", diagnostics);
            if (refused != null) return refused;
            List<SpecieLot> drawn = bank.DrawVaultLots(priceCents, diagnostics);
            if (drawn == null)
            {
                bank.Ledger.ReceiveLoanPayment(priceCents, $"customer-discount-{instrumentId} (voided)", diagnostics);
                diagnostics.Add($"NoteDesk [{bank.BusinessName}]: the vault could not produce {priceCents}c — the ledger disbursement was reversed, the discount is refused.");
                return $"NoteDesk.DiscountCustomerPaper: the vault cannot produce {priceCents}c — refused.";
            }

            GuarantyAgreement guaranty = credit.IssueGuaranty(ids, customerName, bank.BusinessName,
                note.MakerName, instrumentId.ToString(), note.PrincipalCents,
                "endorser recourse on discount" + (string.IsNullOrWhiteSpace(terms) ? "" : ": " + terms),
                dayIndex, diagnostics);
            if (guaranty == null)
            {
                foreach (SpecieLot lot in drawn)
                    bank.ReceiveVaultLot(lot, dayIndex, diagnostics);
                bank.Ledger.ReceiveLoanPayment(priceCents, $"customer-discount-{instrumentId} (voided)", diagnostics);
                return $"NoteDesk.DiscountCustomerPaper: the endorser guaranty could not be registered — the discount is unwound, no recourse, no deal.";
            }

            note.HolderName = bank.BusinessName;
            var paper = new DiscountedPaper
            {
                InstrumentId = instrumentId,
                MakerName = note.MakerName,
                FaceCents = note.PrincipalCents,
                PaidCents = priceCents,
                DiscountedDayIndex = dayIndex,
                CustomerEndorserName = customerName,
                HasRecourse = true,
                GuarantyInstrumentId = guaranty.InstrumentId.ToString(),
            };
            discountedPaper.Add(paper);
            if (loanBook != null)
            {
                string bookRefusal = loanBook.RegisterDiscountedPaper(paper, diagnostics);
                if (bookRefusal != null)
                    diagnostics.Add($"NoteDesk [{bank.BusinessName}]: BOOKING GAP — discounted paper stands but is not on the loan book: {bookRefusal}");
            }
            diagnostics.Add($"NoteDesk [{bank.BusinessName}]: discounted customer paper '{instrumentId}' ({note.MakerName}, face {note.PrincipalCents}c) for '{customerName}' at {priceCents}c — endorser recourse registered ('{guaranty.InstrumentId}', exposure {note.PrincipalCents}c).");
            return null;
        }

        /// <summary>
        /// D3B: the maker of customer-discounted paper defaulted. The
        /// endorser's guaranty is called through the registry (Active →
        /// Called — the obligation becomes real, never silently dropped),
        /// and the loan book converts the paper to a recourse receivable
        /// against the endorser. Paper bought with no recourse cannot come
        /// here — charge it off through the loan book instead.
        /// </summary>
        public string RecordDiscountDefault(EntityId instrumentId, int defaultedAmountCents,
            int dayIndex, BankLoanBook loanBook, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            string ready = DeskReady(diagnostics);
            if (ready != null) return ready;
            DiscountedPaper paper = null;
            foreach (DiscountedPaper candidate in discountedPaper)
                if (!candidate.Collected && candidate.InstrumentId.Equals(instrumentId))
                { paper = candidate; break; }
            if (paper == null)
                return $"NoteDesk.RecordDiscountDefault: note '{instrumentId}' is not on the bank's books.";
            if (!paper.HasRecourse || string.IsNullOrWhiteSpace(paper.CustomerEndorserName))
                return $"NoteDesk.RecordDiscountDefault: note '{instrumentId}' was bought with no endorser recourse — the bank owns the loss. Charge it off through the loan book.";

            int called = Math.Min(paper.FaceCents, Math.Max(0, defaultedAmountCents));
            credit.RecordDefault(instrumentId.ToString(), defaultedAmountCents, dayIndex,
                null, null, null, diagnostics);
            if (loanBook != null)
            {
                string bookRefusal = loanBook.RecordDiscountDefault(instrumentId, called,
                    paper.CustomerEndorserName, dayIndex, diagnostics);
                if (bookRefusal != null)
                    diagnostics.Add($"NoteDesk [{bank.BusinessName}]: BOOKING GAP — guaranty called but the book entry did not convert: {bookRefusal}");
            }
            diagnostics.Add($"NoteDesk [{bank.BusinessName}]: '{instrumentId}' defaulted — endorser '{paper.CustomerEndorserName}' answers {called}c (guaranty '{paper.GuarantyInstrumentId}' called).");
            return null;
        }

        // ---------- save DTO (inside the owning runtime class) ----------

        [Serializable]
        public sealed class NoteDeskSaveDto
        {
            public List<IssuedBankNote> IssuedNotes = new List<IssuedBankNote>();
            public List<DiscountedPaper> DiscountedPaper = new List<DiscountedPaper>();
        }

        public NoteDeskSaveDto ToSaveDto()
        {
            return new NoteDeskSaveDto
            {
                IssuedNotes = new List<IssuedBankNote>(issuedNotes),
                DiscountedPaper = new List<DiscountedPaper>(discountedPaper),
            };
        }

        public void LoadFromSaveDto(NoteDeskSaveDto dto)
        {
            issuedNotes.Clear();
            discountedPaper.Clear();
            if (dto == null) return;
            foreach (IssuedBankNote note in dto.IssuedNotes)
                if (note != null && note.InstrumentId.IsValid && note.FaceCents > 0)
                    issuedNotes.Add(note);
            foreach (DiscountedPaper paper in dto.DiscountedPaper)
                if (paper != null && paper.InstrumentId.IsValid && paper.FaceCents > 0)
                    discountedPaper.Add(paper);
        }
    }
}
