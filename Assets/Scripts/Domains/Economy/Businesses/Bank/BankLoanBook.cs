using System;
using System.Collections.Generic;
using UnityEngine;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Economy.Financing;

namespace LandLedgers.Economy.Bank
{
    /// <summary>
    /// D3B: the kind of earning asset on the bank's books. Every kind traces
    /// to a registered T2A instrument — the book never holds an asset the
    /// registry does not know (upstream provenance, FVS doctrine).
    /// </summary>
    public enum BankLoanAssetKind
    {
        Unspecified = 0,
        /// <summary>A borrower note the bank holds (loan it made).</summary>
        BorrowerNote = 1,
        /// <summary>Third-party commercial paper bought below face (W7C).</summary>
        DiscountedPaper = 2,
        /// <summary>A mortgage deed naming the bank as lender.</summary>
        MortgageDeed = 3,
        /// <summary>
        /// A called endorser guaranty: the maker defaulted and the endorser
        /// now owes the bank (D3B customer-discount recourse).
        /// </summary>
        RecourseReceivable = 4,
    }

    /// <summary>D3B: lifecycle of a loan-book asset.</summary>
    public enum BankLoanAssetStatus
    {
        Unspecified = 0,
        Current = 1,
        Collected = 2,
        ChargedOff = 3,
    }

    /// <summary>
    /// D3B: one earning asset on the bank's books. BookValueCents is what the
    /// asset stands at on the balance sheet: face for borrower notes and
    /// mortgages, PAID COST for discounted paper (the discount is profit only
    /// when collected), called amount for recourse receivables.
    /// </summary>
    [Serializable]
    public sealed class BankLoanBookEntry
    {
        public EntityId InstrumentId = EntityId.Invalid;
        public BankLoanAssetKind AssetKind = BankLoanAssetKind.Unspecified;
        public string DebtorName = string.Empty;
        public int FaceCents;
        public int BookValueCents;
        public int RegisteredDayIndex;
        public BankLoanAssetStatus Status = BankLoanAssetStatus.Current;
        /// <summary>
        /// Canon §16.5: a loan to the owner or another controlled business is
        /// still a real loan — the connected relationship is identified, not
        /// judged. The operating layer marks it; policy lives elsewhere.
        /// </summary>
        public bool ConnectedBorrower;
        public string SourceNote = string.Empty;

        public BankLoanBookEntry() { }
    }

    /// <summary>
    /// D3B: the period-style balance-sheet view (Canon §16.4). Assets are the
    /// bank's real holdings; liabilities are what it owes; equity is paid
    /// capital + surplus + undistributed profits, where undistributed profits
    /// are the DERIVED residual (assets minus liabilities minus paid capital
    /// minus surplus) — never a separately tracked number that could drift
    /// from the books. Balances() is the §18.15 completeness check: a bank
    /// that cannot balance its own sheet is telling on itself.
    /// </summary>
    [Serializable]
    public sealed class BankBalanceSheet
    {
        public int VaultCashCents;
        public int CorrespondentBalancesCents;
        public int BorrowerNotesCents;
        public int DiscountedPaperCents;
        public int MortgageDeedsCents;
        public int RecourseReceivablesCents;
        public int DepositsOwedCents;
        public int NotesOutstandingCents;
        public int DraftsOutstandingCents;
        public int CollectionObligationsCents;
        public int PaidCapitalCents;
        public int SurplusCents;
        public int UndistributedProfitsCents;

        public int TotalAssetsCents() =>
            VaultCashCents + CorrespondentBalancesCents + BorrowerNotesCents +
            DiscountedPaperCents + MortgageDeedsCents + RecourseReceivablesCents;

        public int TotalLiabilitiesCents() =>
            DepositsOwedCents + NotesOutstandingCents + DraftsOutstandingCents +
            CollectionObligationsCents;

        public int TotalEquityCents() =>
            PaidCapitalCents + SurplusCents + UndistributedProfitsCents;

        /// <summary>Assets must equal liabilities + equity — always, by construction.</summary>
        public bool Balances() => TotalAssetsCents() == TotalLiabilitiesCents() + TotalEquityCents();

        /// <summary>Canon §18.8/§18.9: can the bank meet demand claims from cash it holds?</summary>
        public int DemandClaimsCents() => DepositsOwedCents + NotesOutstandingCents;

        public int LiquidityGapCents() => VaultCashCents - DemandClaimsCents();
    }

    /// <summary>
    /// D3B: the bank's earning-asset register and balance-sheet consolidation
    /// (Canon §16.4, §18.8). The book WRAPS the BankRuntime and the T2A/NX-3B
    /// instruments — it never bypasses them:
    ///
    /// - Every asset entry names the registered instrument it came from; the
    ///   registry stays the authority on the debt itself.
    /// - The book moves NO cash for asset flows: cash moves happen in the
    ///   methods that own them (NoteDesk, TellerOperations, CorrespondentAccount,
    ///   CollectionsDesk); the book only records and retires assets, and the
    ///   equity residual absorbs every gain and loss honestly. The one
    ///   exception is DeclareOwnerDistribution — an explicit equity action
    ///   the book owns, moving cash out through the wrapped ledger.
    /// - Undistributed profits are derived, not tracked: there is no second
    ///   profit number to drift. Fee income, discount profits, shortages and
    ///   charge-offs all flow through the residual automatically.
    ///
    /// HARD CONSTRAINT (NX-3B): the bank creates NO money. Registering an
    /// asset never creates the cash behind it — the cash had to arrive through
    /// a real inflow first, and every registration is refused when the paper
    /// is not on the registry naming this bank.
    /// </summary>
    public sealed class BankLoanBook
    {
        private readonly BankRuntime bank;
        private readonly CreditRegistry credit;

        private readonly List<BankLoanBookEntry> entries = new List<BankLoanBookEntry>();

        private NoteDesk noteDesk;
        private readonly List<BankCorrespondentAccount> correspondents = new List<BankCorrespondentAccount>();
        private BankCollectionsDesk collectionsDesk;

        private int paidCapitalCents;
        private bool paidCapitalRecorded;
        private string paidCapitalSource = string.Empty;
        private int surplusCents;

        public BankRuntime Bank => bank;
        public CreditRegistry Credit => credit;
        public IReadOnlyList<BankLoanBookEntry> Entries => entries;
        public int PaidCapitalCents => paidCapitalCents;
        public int SurplusCents => surplusCents;
        public int SharedReceivablesCents => credit?.FinancialAuthority?.TotalReceivableFor(bank?.BusinessName ?? string.Empty) ?? 0;

        public BankLoanBook(BankRuntime bank, CreditRegistry credit)
        {
            this.bank = bank;
            this.credit = credit;
        }

        public void AttachNoteDesk(NoteDesk desk) { noteDesk = desk; }

        public void AttachCorrespondent(BankCorrespondentAccount account)
        {
            if (account == null) return;
            foreach (BankCorrespondentAccount existing in correspondents)
                if (string.Equals(existing.AccountId, account.AccountId, StringComparison.Ordinal))
                    return;
            correspondents.Add(account);
        }

        public void AttachCollectionsDesk(BankCollectionsDesk desk) { collectionsDesk = desk; }

        // ---------- capital & surplus (Canon §16.4) ----------

        /// <summary>
        /// Records the bank's paid-in capital — once. Canon §16.1: paid-in
        /// capital must be real contributed value. The amount must match cash
        /// that actually entered (the operating layer pairs this with
        /// RecordOpeningCapital); the book records the equity half.
        /// </summary>
        public string RecordPaidCapital(int amountCents, string sourceNote, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (paidCapitalRecorded)
                return "BankLoanBook.RecordPaidCapital: paid capital is already recorded — capital is contributed once, not re-declared.";
            if (amountCents <= 0)
                return "BankLoanBook.RecordPaidCapital: paid capital must be positive.";
            if (string.IsNullOrWhiteSpace(sourceNote))
                return "BankLoanBook.RecordPaidCapital: the capital source must be named — capital is never conjured (Canon §16.1).";
            paidCapitalCents = amountCents;
            paidCapitalRecorded = true;
            paidCapitalSource = sourceNote;
            diagnostics.Add($"BankLoanBook [{bank.BusinessName}]: paid-in capital {amountCents}c recorded ({sourceNote}).");
            return null;
        }

        /// <summary>
        /// Canon §16.4: transfers of earnings into surplus are explicit.
        /// Profit is not automatically distributable owner cash — moving it
        /// to surplus takes it out of the distributable residual on purpose.
        /// Refuses what the residual does not hold.
        /// </summary>
        public string TransferToSurplus(int amountCents, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (amountCents <= 0)
                return "BankLoanBook.TransferToSurplus: the transfer must be positive.";
            int residual = UndistributedProfitsCents();
            if (amountCents > residual)
                return $"BankLoanBook.TransferToSurplus: undistributed profits are {residual}c — cannot capitalize {amountCents}c of earnings the bank has not made.";
            surplusCents += amountCents;
            diagnostics.Add($"BankLoanBook [{bank.BusinessName}]: {amountCents}c of earnings transferred to surplus (surplus now {surplusCents}c).");
            return null;
        }

        /// <summary>
        /// The owner takes profits out. Refused beyond the undistributed
        /// residual — the owner cannot distribute capital or surplus as if
        /// they were earnings. Cash leaves through the ledger; the operating
        /// layer pairs the vault specie lots handed over.
        /// </summary>
        public string DeclareOwnerDistribution(int amountCents, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (bank == null) return "BankLoanBook.DeclareOwnerDistribution: no bank.";
            if (amountCents <= 0)
                return "BankLoanBook.DeclareOwnerDistribution: the distribution must be positive.";
            int residual = UndistributedProfitsCents();
            if (amountCents > residual)
                return $"BankLoanBook.DeclareOwnerDistribution: undistributed profits are {residual}c — {amountCents}c would eat capital or surplus. Refused.";
            string refused = bank.Ledger.RecordOperatingOutflow(amountCents,
                $"owner distribution, day {dayIndex}", diagnostics);
            if (refused != null) return refused;
            diagnostics.Add($"BankLoanBook [{bank.BusinessName}]: owner distribution {amountCents}c declared — cash out, residual now {UndistributedProfitsCents()}c.");
            return null;
        }

        // ---------- asset registration (upstream provenance) ----------

        private BankLoanBookEntry FindEntry(EntityId instrumentId)
        {
            foreach (BankLoanBookEntry entry in entries)
                if (entry.InstrumentId.Equals(instrumentId))
                    return entry;
            return null;
        }

        private string Ready(List<string> diagnostics)
        {
            if (bank == null || credit == null)
                return "BankLoanBook: the book needs a bank and a credit registry.";
            return null;
        }

        /// <summary>
        /// Registers a borrower note the bank holds as an earning asset at
        /// face. The registry must show the note active with this bank as
        /// payee or holder — the book adopts real paper, never assertions.
        /// </summary>
        public string RegisterBorrowerNote(EntityId instrumentId, string debtorName,
            int faceCents, int dayIndex, bool connectedBorrower, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            string ready = Ready(diagnostics);
            if (ready != null) return ready;
            if (FindEntry(instrumentId) != null)
                return $"BankLoanBook.RegisterBorrowerNote: '{instrumentId}' is already on the books.";
            if (!credit.TryGetPromissoryNote(instrumentId, out PromissoryNote note) || note == null)
                return $"BankLoanBook.RegisterBorrowerNote: no promissory note '{instrumentId}' on the registry.";
            if (note.Status != CreditInstrumentStatus.Active)
                return $"BankLoanBook.RegisterBorrowerNote: note '{instrumentId}' is {note.Status} — only live paper becomes an asset.";
            if (!string.Equals(note.PayeeName, bank.BusinessName, StringComparison.Ordinal) &&
                !string.Equals(note.HolderName, bank.BusinessName, StringComparison.Ordinal))
                return $"BankLoanBook.RegisterBorrowerNote: note '{instrumentId}' names neither payee nor holder as this bank — the book holds its own paper.";
            if (faceCents <= 0 || faceCents != note.PrincipalCents)
                return $"BankLoanBook.RegisterBorrowerNote: face must equal the registered principal {note.PrincipalCents}c — the registry is the authority.";
            if (credit.FinancialAuthority != null && string.IsNullOrWhiteSpace(note.ObligationId))
                return $"BankLoanBook.RegisterBorrowerNote: note '{instrumentId}' has no shared financial obligation.";

            entries.Add(new BankLoanBookEntry
            {
                InstrumentId = instrumentId,
                AssetKind = BankLoanAssetKind.BorrowerNote,
                DebtorName = string.IsNullOrWhiteSpace(debtorName) ? note.MakerName : debtorName,
                FaceCents = note.PrincipalCents,
                BookValueCents = note.PrincipalCents,
                RegisteredDayIndex = dayIndex,
                Status = BankLoanAssetStatus.Current,
                ConnectedBorrower = connectedBorrower,
                SourceNote = $"borrower note '{instrumentId}' ({note.MakerName} → {note.PayeeName})",
            });
            diagnostics.Add($"BankLoanBook [{bank.BusinessName}]: borrower note '{instrumentId}' on the books at {note.PrincipalCents}c face" +
                (connectedBorrower ? " — CONNECTED borrower, identified per Canon §16.5." : "."));
            return null;
        }

        /// <summary>
        /// Adopts discounted paper from the note desk at PAID COST — the asset
        /// half of the discount window. The discount is profit only when the
        /// paper is collected (then the residual shows it).
        /// </summary>
        public string RegisterDiscountedPaper(DiscountedPaper paper, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            string ready = Ready(diagnostics);
            if (ready != null) return ready;
            if (paper == null)
                return "BankLoanBook.RegisterDiscountedPaper: no paper supplied.";
            if (FindEntry(paper.InstrumentId) != null)
                return $"BankLoanBook.RegisterDiscountedPaper: '{paper.InstrumentId}' is already on the books.";
            entries.Add(new BankLoanBookEntry
            {
                InstrumentId = paper.InstrumentId,
                AssetKind = BankLoanAssetKind.DiscountedPaper,
                DebtorName = paper.MakerName,
                FaceCents = paper.FaceCents,
                BookValueCents = paper.PaidCents,
                RegisteredDayIndex = paper.DiscountedDayIndex,
                Status = BankLoanAssetStatus.Current,
                SourceNote = $"discounted paper '{paper.InstrumentId}' (paid {paper.PaidCents}c for {paper.FaceCents}c face" +
                    (paper.HasRecourse ? $", endorser {paper.CustomerEndorserName}" : ", no recourse") + ")",
            });
            diagnostics.Add($"BankLoanBook [{bank.BusinessName}]: discounted paper '{paper.InstrumentId}' on the books at {paper.PaidCents}c cost.");
            return null;
        }

        /// <summary>
        /// Registers a mortgage deed naming the bank as lender, at principal.
        /// </summary>
        public string RegisterMortgageDeed(EntityId instrumentId, int dayIndex,
            bool connectedBorrower, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            string ready = Ready(diagnostics);
            if (ready != null) return ready;
            if (FindEntry(instrumentId) != null)
                return $"BankLoanBook.RegisterMortgageDeed: '{instrumentId}' is already on the books.";
            MortgageDeed found = null;
            foreach (MortgageDeed m in credit.CaptureSaveDto().Mortgages)
                if (m != null && m.InstrumentId.Equals(instrumentId)) { found = m; break; }
            if (found == null)
                return $"BankLoanBook.RegisterMortgageDeed: no mortgage '{instrumentId}' on the registry.";
            if (found.Status != CreditInstrumentStatus.Active)
                return $"BankLoanBook.RegisterMortgageDeed: mortgage '{instrumentId}' is {found.Status}.";
            if (!string.Equals(found.LenderName, bank.BusinessName, StringComparison.Ordinal))
                return $"BankLoanBook.RegisterMortgageDeed: mortgage '{instrumentId}' names lender '{found.LenderName}' — not this bank.";
            if (credit.FinancialAuthority != null && string.IsNullOrWhiteSpace(found.ObligationId))
                return $"BankLoanBook.RegisterMortgageDeed: mortgage '{instrumentId}' has no shared financial obligation.";

            entries.Add(new BankLoanBookEntry
            {
                InstrumentId = instrumentId,
                AssetKind = BankLoanAssetKind.MortgageDeed,
                DebtorName = found.BorrowerName,
                FaceCents = found.PrincipalCents,
                BookValueCents = found.PrincipalCents,
                RegisteredDayIndex = dayIndex,
                Status = BankLoanAssetStatus.Current,
                ConnectedBorrower = connectedBorrower,
                SourceNote = $"mortgage '{instrumentId}' ({found.BorrowerName}, {found.PropertyDescription})",
            });
            diagnostics.Add($"BankLoanBook [{bank.BusinessName}]: mortgage '{instrumentId}' on the books at {found.PrincipalCents}c.");
            return null;
        }

        /// <summary>
        /// The paper paid off in full — the asset retires. Cash already moved
        /// through the owning flow (NoteDesk collection, teller receipt); the
        /// book only retires the entry, and the residual shows the gain.
        /// </summary>
        public string MarkAssetCollected(EntityId instrumentId, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            BankLoanBookEntry entry = FindEntry(instrumentId);
            if (entry == null)
                return $"BankLoanBook.MarkAssetCollected: '{instrumentId}' is not on the books.";
            if (entry.Status != BankLoanAssetStatus.Current)
                return $"BankLoanBook.MarkAssetCollected: '{instrumentId}' is {entry.Status} — already resolved.";
            entry.Status = BankLoanAssetStatus.Collected;
            if (credit.TryGetPromissoryNote(instrumentId, out PromissoryNote note) && note != null
                && credit.FinancialAuthority != null && !string.IsNullOrWhiteSpace(note.ObligationId))
            {
                credit.ApplyInstrumentPayment(instrumentId.ToString(),
                    credit.FinancialAuthority.Find(note.ObligationId)?.TotalOutstandingCents ?? 0,
                    entry.RegisteredDayIndex, note.MakerName, note.PayeeName, diagnostics);
            }
            diagnostics.Add($"BankLoanBook [{bank.BusinessName}]: '{instrumentId}' collected — asset retired from the books.");
            return null;
        }

        /// <summary>
        /// D3B customer-discount recourse: the maker defaulted, the endorser's
        /// guaranty was called for calledAmountCents, and the bank now holds a
        /// receivable against the ENDORSER. The paper converts to a recourse
        /// receivable at the called amount; any shortfall against cost books
        /// through the residual immediately (the loss is real now).
        /// </summary>
        public string RecordDiscountDefault(EntityId instrumentId, int calledAmountCents,
            string endorserName, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            BankLoanBookEntry entry = FindEntry(instrumentId);
            if (entry == null)
                return $"BankLoanBook.RecordDiscountDefault: '{instrumentId}' is not on the books.";
            if (entry.Status != BankLoanAssetStatus.Current)
                return $"BankLoanBook.RecordDiscountDefault: '{instrumentId}' is {entry.Status} — already resolved.";
            if (entry.AssetKind != BankLoanAssetKind.DiscountedPaper)
                return $"BankLoanBook.RecordDiscountDefault: '{instrumentId}' is {entry.AssetKind} — recourse conversion applies to discounted paper.";
            if (string.IsNullOrWhiteSpace(endorserName))
                return "BankLoanBook.RecordDiscountDefault: the answering endorser must be named.";
            if (calledAmountCents < 0)
                return "BankLoanBook.RecordDiscountDefault: the called amount cannot be negative.";

            int shortfall = Math.Max(0, entry.BookValueCents - calledAmountCents);
            entry.AssetKind = BankLoanAssetKind.RecourseReceivable;
            entry.DebtorName = endorserName;
            entry.BookValueCents = calledAmountCents;
            entry.SourceNote += $" → maker defaulted day {dayIndex}; endorser {endorserName} answers {calledAmountCents}c";
            diagnostics.Add($"BankLoanBook [{bank.BusinessName}]: '{instrumentId}' defaulted — recourse receivable {calledAmountCents}c against endorser '{endorserName}'" +
                (shortfall > 0 ? $"; {shortfall}c shortfall against cost is a real loss (see residual)." : "."));
            return null;
        }

        /// <summary>
        /// The asset is worthless — charged off. The book value leaves the
        /// balance sheet and the residual absorbs the loss. The registry
        /// instrument keeps its own lifecycle; this only ends the book entry.
        /// </summary>
        public string ChargeOffAsset(EntityId instrumentId, string reason, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            BankLoanBookEntry entry = FindEntry(instrumentId);
            if (entry == null)
                return $"BankLoanBook.ChargeOffAsset: '{instrumentId}' is not on the books.";
            if (entry.Status != BankLoanAssetStatus.Current)
                return $"BankLoanBook.ChargeOffAsset: '{instrumentId}' is {entry.Status} — already resolved.";
            if (string.IsNullOrWhiteSpace(reason))
                return "BankLoanBook.ChargeOffAsset: the charge-off reason must be stated.";
            entry.Status = BankLoanAssetStatus.ChargedOff;
            diagnostics.Add($"BankLoanBook [{bank.BusinessName}]: '{instrumentId}' charged off ({reason}) — {entry.BookValueCents}c leaves the books; the residual absorbs it.");
            return null;
        }

        /// <summary>Canon §16.5: identifies a connected borrower without judging the loan.</summary>
        public string MarkConnectedBorrower(EntityId instrumentId, bool connected, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            BankLoanBookEntry entry = FindEntry(instrumentId);
            if (entry == null)
                return $"BankLoanBook.MarkConnectedBorrower: '{instrumentId}' is not on the books.";
            entry.ConnectedBorrower = connected;
            diagnostics.Add($"BankLoanBook [{bank.BusinessName}]: '{instrumentId}' connected-borrower = {connected} (Canon §16.5 — identified, still a real loan).");
            return null;
        }

        // ---------- read model ----------

        public int LiveBookValueCents(BankLoanAssetKind kind)
        {
            int total = 0;
            foreach (BankLoanBookEntry entry in entries)
                if (entry.Status == BankLoanAssetStatus.Current && entry.AssetKind == kind)
                    total += entry.BookValueCents;
            return total;
        }

        public int LiveEarningAssetsCents() =>
            LiveBookValueCents(BankLoanAssetKind.BorrowerNote) +
            LiveBookValueCents(BankLoanAssetKind.DiscountedPaper) +
            LiveBookValueCents(BankLoanAssetKind.MortgageDeed) +
            LiveBookValueCents(BankLoanAssetKind.RecourseReceivable);

        /// <summary>
        /// The derived undistributed-profits residual: what the bank has
        /// earned (or lost) beyond paid capital and surplus, implied by every
        /// cash, asset and liability move on the books. Never tracked
        /// separately — it cannot drift.
        /// </summary>
        public int UndistributedProfitsCents()
        {
            if (bank == null) return 0;
            int assets = bank.Ledger.CashOnHandCents + LiveEarningAssetsCents();
            foreach (BankCorrespondentAccount c in correspondents)
                assets += Math.Max(0, c.BalanceCents);
            int liabilities = bank.Ledger.DepositsOwedCents();
            if (noteDesk != null) liabilities += noteDesk.NotesOutstandingCents();
            foreach (BankCorrespondentAccount c in correspondents)
                liabilities += c.DraftsOutstandingCents();
            if (collectionsDesk != null) liabilities += collectionsDesk.CollectionObligationsCents();
            return assets - liabilities - paidCapitalCents - surplusCents;
        }

        /// <summary>
        /// Canon §16.4: the period-style balance-sheet view. Assets =
        /// cash, correspondent balances, loans, discounted paper, mortgages,
        /// recourse receivables. Liabilities = deposits, notes outstanding,
        /// drafts outstanding, collection obligations. Equity = paid capital
        /// + surplus + derived undistributed profits. Solvency and liquidity
        /// stay separate readings on the same sheet (Canon §18.8).
        /// </summary>
        public BankBalanceSheet BuildBalanceSheet()
        {
            var sheet = new BankBalanceSheet();
            if (bank == null) return sheet;
            sheet.VaultCashCents = bank.Ledger.CashOnHandCents;
            foreach (BankCorrespondentAccount c in correspondents)
                sheet.CorrespondentBalancesCents += Math.Max(0, c.BalanceCents);
            sheet.BorrowerNotesCents = LiveBookValueCents(BankLoanAssetKind.BorrowerNote);
            sheet.DiscountedPaperCents = LiveBookValueCents(BankLoanAssetKind.DiscountedPaper);
            sheet.MortgageDeedsCents = LiveBookValueCents(BankLoanAssetKind.MortgageDeed);
            sheet.RecourseReceivablesCents = LiveBookValueCents(BankLoanAssetKind.RecourseReceivable);
            sheet.DepositsOwedCents = bank.Ledger.DepositsOwedCents();
            if (noteDesk != null) sheet.NotesOutstandingCents = noteDesk.NotesOutstandingCents();
            foreach (BankCorrespondentAccount c in correspondents)
                sheet.DraftsOutstandingCents += c.DraftsOutstandingCents();
            if (collectionsDesk != null) sheet.CollectionObligationsCents = collectionsDesk.CollectionObligationsCents();
            sheet.PaidCapitalCents = paidCapitalCents;
            sheet.SurplusCents = surplusCents;
            sheet.UndistributedProfitsCents = UndistributedProfitsCents();
            return sheet;
        }

        // ---------- save DTO (inside the owning runtime class) ----------

        [Serializable]
        public sealed class BankLoanBookSaveDto
        {
            public int PaidCapitalCents;
            public bool PaidCapitalRecorded;
            public string PaidCapitalSource = string.Empty;
            public int SurplusCents;
            public List<BankLoanBookEntry> Entries = new List<BankLoanBookEntry>();
        }

        public BankLoanBookSaveDto ToSaveDto()
        {
            return new BankLoanBookSaveDto
            {
                PaidCapitalCents = paidCapitalCents,
                PaidCapitalRecorded = paidCapitalRecorded,
                PaidCapitalSource = paidCapitalSource,
                SurplusCents = Math.Max(0, surplusCents),
                Entries = new List<BankLoanBookEntry>(entries),
            };
        }

        public void LoadFromSaveDto(BankLoanBookSaveDto dto)
        {
            entries.Clear();
            paidCapitalCents = 0;
            paidCapitalRecorded = false;
            paidCapitalSource = string.Empty;
            surplusCents = 0;
            if (dto == null) return;
            paidCapitalCents = Math.Max(0, dto.PaidCapitalCents);
            paidCapitalRecorded = dto.PaidCapitalRecorded;
            paidCapitalSource = dto.PaidCapitalSource ?? string.Empty;
            surplusCents = Math.Max(0, dto.SurplusCents);
            if (dto.Entries != null)
                foreach (BankLoanBookEntry entry in dto.Entries)
                    if (entry != null && entry.InstrumentId.IsValid)
                        entries.Add(entry);
        }
    }
}
