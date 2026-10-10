using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Financing
{
    /// <summary>Phase E (E3): what kind of lender this is.</summary>
    public enum LenderInstitutionKind
    {
        PrivateIndividual = 0,
        FormalBank = 1,
    }

    /// <summary>
    /// Phase E (E3): how a lender differs. Formal banks and private lenders
    /// use the SAME <see cref="FinancialObligationAuthority"/>; they differ
    /// through actual liquidity, institutional/legal powers, capital,
    /// relationships, information, underwriting policy, funding, reserve
    /// requirements, concentration limits and willingness — all modeled here,
    /// never as a second debt ledger.
    /// </summary>
    [Serializable]
    public sealed class CreditLenderPolicy
    {
        public LenderInstitutionKind Kind = LenderInstitutionKind.PrivateIndividual;
        /// <summary>Rate floor in bps. TUNING.</summary>
        public int MinimumRateBps = 600;
        /// <summary>Longest term this lender writes. TUNING.</summary>
        public int MaximumTermDays = 1825;
        /// <summary>Banks only: share of deposits that must stay in the vault. TUNING.</summary>
        public float ReserveRequirement01 = 0f;
        /// <summary>Max share of lendable funds to one borrower. TUNING.</summary>
        public float MaxSingleBorrowerShare01 = 0.25f;
        /// <summary>Institutional power: files first-priority liens. Banks only.</summary>
        public bool CanFileSeniorLien;
        /// <summary>Institutional power: discounts third-party paper (NoteDesk). Banks only.</summary>
        public bool CanDiscountPaper;
        /// <summary>Private lenders forbear for people they know; banks follow the book.</summary>
        public bool RelationshipForbearance;
        /// <summary>Disclosed evidence records required before an offer. TUNING.</summary>
        public int MinimumEvidenceRecords;

        public CreditLenderPolicy() { }

        /// <summary>
        /// A private individual lends their OWN finite capital (no deposit
        /// taking, no money creation), decides relationally, and writes
        /// shorter, higher-rate paper.
        /// </summary>
        public static CreditLenderPolicy ForPrivateIndividual()
        {
            return new CreditLenderPolicy
            {
                Kind = LenderInstitutionKind.PrivateIndividual,
                MinimumRateBps = 800,
                MaximumTermDays = 1095,
                ReserveRequirement01 = 0f,
                MaxSingleBorrowerShare01 = 0.50f,
                CanFileSeniorLien = false,
                CanDiscountPaper = false,
                RelationshipForbearance = true,
                MinimumEvidenceRecords = 0,
            };
        }

        /// <summary>
        /// A formal bank lends deposits subject to a reserve floor, files
        /// senior liens, discounts paper, and underwrites to a stricter
        /// evidence standard.
        /// </summary>
        public static CreditLenderPolicy ForFormalBank()
        {
            return new CreditLenderPolicy
            {
                Kind = LenderInstitutionKind.FormalBank,
                MinimumRateBps = 600,
                MaximumTermDays = 3650,
                ReserveRequirement01 = 0.15f,
                MaxSingleBorrowerShare01 = 0.10f,
                CanFileSeniorLien = true,
                CanDiscountPaper = true,
                RelationshipForbearance = false,
                MinimumEvidenceRecords = 2,
            };
        }
    }

    /// <summary>
    /// Phase E (E3): a private lender as a real credit participant. Every
    /// advance is gated twice — by the purse (actual cash) and by
    /// <see cref="PrivateLenderFunds"/> (finite committed capital). A private
    /// lender CANNOT lend what they do not have: a refused commitment aborts
    /// the offer before any cash moves, and an undrawn facility is neither
    /// spendable cash nor existing debt.
    /// </summary>
    public sealed class PrivateLenderCreditParticipant
    {
        public string LenderName = string.Empty;
        public PrivateLenderFunds Funds;
        public PurseCashStore Purse;
        public CreditLenderPolicy Policy = CreditLenderPolicy.ForPrivateIndividual();

        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        public PrivateLenderCreditParticipant() { }

        public PrivateLenderCreditParticipant(string lenderName, PrivateLenderFunds funds,
            PurseCashStore purse, CreditLenderPolicy policy = null)
        {
            LenderName = lenderName ?? string.Empty;
            Funds = funds;
            Purse = purse;
            Policy = policy ?? CreditLenderPolicy.ForPrivateIndividual();
        }

        public int LendableCents()
        {
            if (Funds == null || Purse == null) return 0;
            return Math.Max(0, Math.Min(Purse.ReadBalanceCents(), Funds.AvailableCents()));
        }

        /// <summary>Evidence-limited evaluation: only disclosed records count, never a universal score.</summary>
        public CreditOffer EvaluateOffer(LandLedgers.Primitives.EntityIdRegistry ids,
            CreditOfferWorkflow workflow, FinancialObligationAuthority authority,
            CreditRequest request, int dayIndex, CreditEventLog events, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (ids == null || workflow == null || request == null)
            {
                diag.Add($"PrivateLender [{LenderName}]: cannot evaluate — missing request machinery.");
                return null;
            }
            int disclosed = 0;
            foreach (CreditEvidenceRecord record in workflow.Evidence)
                if (record != null && record.DisclosedToLender
                    && string.Equals(record.Borrower, request.Borrower, StringComparison.Ordinal))
                    disclosed++;
            if (disclosed < Policy.MinimumEvidenceRecords)
            {
                diag.Add($"PrivateLender [{LenderName}]: refused '{request.Borrower}' — {disclosed} disclosed evidence record(s), policy needs {Policy.MinimumEvidenceRecords}.");
                events?.Record(dayIndex, CreditEventKind.LenderRefused, request.Borrower, LenderName,
                    request.RequestedAmountCents,
                    $"private lender refused: insufficient disclosed evidence ({disclosed}/{Policy.MinimumEvidenceRecords}).",
                    "", request.RequestId);
                return null;
            }
            CreditOffer offer = workflow.Evaluate(ids, request, authority, LendableCents(), dayIndex);
            if (offer != null && offer.Status == CreditOfferStatus.Proposed)
            {
                offer.AnnualInterestRateBps = Math.Max(offer.AnnualInterestRateBps, Policy.MinimumRateBps);
                offer.TermDays = Math.Min(offer.TermDays, Policy.MaximumTermDays);
                diag.Add($"PrivateLender [{LenderName}]: offered {offer.OfferedAmountCents}c to '{request.Borrower}' " +
                    $"at {offer.AnnualInterestRateBps}bps/{offer.TermDays}d (lendable {LendableCents()}c).");
                events?.Record(dayIndex, CreditEventKind.OfferMade, request.Borrower, LenderName,
                    offer.OfferedAmountCents,
                    $"private lender offered {offer.OfferedAmountCents}c at {offer.AnnualInterestRateBps}bps, {offer.TermDays} days.",
                    "", request.RequestId);
            }
            else
            {
                diag.Add($"PrivateLender [{LenderName}]: no offer for '{request.Borrower}' — {offer?.DecisionReason ?? "evaluation failed"}.");
                events?.Record(dayIndex, CreditEventKind.OfferRefused, request.Borrower, LenderName,
                    request.RequestedAmountCents,
                    $"private lender refused: {offer?.DecisionReason ?? "evaluation failed"}.",
                    "", request.RequestId);
            }
            return offer;
        }

        /// <summary>
        /// Accepts an offer as the lender: commits finite funds FIRST (a
        /// refusal aborts before cash moves), then advances real cash through
        /// the bridge. On any failure after the commitment, the commitment is
        /// released — capital and cash never drift apart.
        /// </summary>
        public FinancialObligation AcceptOffer(LandLedgers.Primitives.EntityIdRegistry ids,
            CreditOfferWorkflow workflow, FinancialObligationAuthority authority,
            CreditCashBridge cash, string offerId, string borrowerOwner,
            int dayIndex, CreditEventLog events, List<string> diag)
        {
            diag = diag ?? diagnostics;
            CreditOffer offer = workflow?.FindOffer(offerId);
            if (offer == null)
            {
                diag.Add($"PrivateLender [{LenderName}]: unknown offer '{offerId}'.");
                return null;
            }
            if (Funds == null || Purse == null)
            {
                diag.Add($"PrivateLender [{LenderName}]: no funds or purse wired — cannot advance.");
                events?.Record(dayIndex, CreditEventKind.DecisionFailed, offer.Borrower, LenderName,
                    offer.OfferedAmountCents, "private lender accept failed: funds or purse not wired.",
                    "", offerId);
                return null;
            }
            string commitmentId = "commit-" + offerId;
            string refusal = Funds.CommitFunds(commitmentId, offerId, offer.OfferedAmountCents, dayIndex, diag);
            if (refusal != null)
            {
                diag.Add($"PrivateLender [{LenderName}]: CANNOT advance {offer.OfferedAmountCents}c — {refusal}");
                events?.Record(dayIndex, CreditEventKind.LenderRefused, offer.Borrower, LenderName,
                    offer.OfferedAmountCents,
                    $"private lender cannot advance: {refusal}",
                    "", offerId);
                return null;
            }

            FinancialObligation obligation = null;
            CreditCashAccount lenderCash = cash != null ? cash.OpenWindow(LenderName, diag) : null;
            CreditCashAccount borrowerCash = cash != null ? cash.OpenWindow(borrowerOwner, diag) : null;
            if (lenderCash != null && borrowerCash != null)
            {
                // The purse is the lender's real store: bind the window to it.
                obligation = workflow.Accept(ids, offerId, authority, lenderCash, borrowerCash, dayIndex);
            }
            if (obligation == null)
            {
                Funds.ReleaseCommitment(offerId, diag);
                if (cash != null) { cash.DiscardWindow(lenderCash); cash.DiscardWindow(borrowerCash); }
                diag.Add($"PrivateLender [{LenderName}]: workflow accept failed — commitment released, nothing moved.");
                events?.Record(dayIndex, CreditEventKind.DecisionFailed, offer.Borrower, LenderName,
                    offer.OfferedAmountCents, "private lender accept failed at the workflow; commitment released.",
                    "", offerId);
                return null;
            }
            string lenderProblem = cash.CommitWindow(lenderCash, dayIndex,
                $"loan advance to '{offer.Borrower}'", offer.Borrower, diag);
            string borrowerProblem = cash.CommitWindow(borrowerCash, dayIndex,
                $"loan proceeds from '{LenderName}'", LenderName, diag);
            if (lenderProblem != null || borrowerProblem != null)
            {
                Funds.ReleaseCommitment(offerId, diag);
                diag.Add($"PrivateLender [{LenderName}]: INCONSISTENCY — cash commit refused after obligation creation; commitment released, manual reconciliation required.");
                return null;
            }
            // The bridge already moved the purse cash (the purse is this
            // lender's registered real store): capital commitment and cash
            // stay in lockstep with no second debit.
            diag.Add($"PrivateLender [{LenderName}]: advanced {offer.OfferedAmountCents}c to '{offer.Borrower}' — " +
                $"funds committed ({Funds.AvailableCents()}c still free), purse now {Purse.ReadBalanceCents()}c.");
            events?.Record(dayIndex, CreditEventKind.Settlement, offer.Borrower, LenderName,
                offer.OfferedAmountCents,
                $"private lender advanced {offer.OfferedAmountCents}c in real cash; obligation '{obligation.ObligationId}'.",
                obligation.ObligationId, offerId);
            return obligation;
        }

        /// <summary>
        /// Releases the capital commitment when the loan is satisfied. The
        /// repayment cash itself moves through the bridge (borrower store →
        /// lender purse); this only retires the commitment so capital and
        /// cash never drift apart.
        /// </summary>
        public string RecordRepayment(string obligationId, string instrumentId,
            bool loanSatisfied, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (Funds == null) return "PrivateLender: no funds wired.";
            if (loanSatisfied && !string.IsNullOrWhiteSpace(instrumentId))
            {
                string problem = Funds.ReleaseCommitment(instrumentId, diag);
                if (problem == null)
                    diag.Add($"PrivateLender [{LenderName}]: loan satisfied — commitment released, {Funds.AvailableCents()}c free.");
                return problem;
            }
            return null;
        }
    }

    /// <summary>
    /// Phase E (E3): a formal bank as a credit participant. Lends deposits
    /// subject to a reserve floor (policy), files senior liens (institutional
    /// power), and underwrites to a stricter evidence standard. The vault cash
    /// here is the bank's real specie position; the deposit ledger that backs
    /// <see cref="DepositsOwedCents"/> is owned by the bank runtime (Codex
    /// integration), so the reserve math stays honest about what it knows.
    /// </summary>
    public sealed class BankCreditParticipant
    {
        public string BankName = string.Empty;
        public PurseCashStore Vault;
        public int DepositsOwedCents;
        public CreditLenderPolicy Policy = CreditLenderPolicy.ForFormalBank();

        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        public BankCreditParticipant() { }

        public BankCreditParticipant(string bankName, PurseCashStore vault, int depositsOwedCents,
            CreditLenderPolicy policy = null)
        {
            BankName = bankName ?? string.Empty;
            Vault = vault;
            DepositsOwedCents = Math.Max(0, depositsOwedCents);
            Policy = policy ?? CreditLenderPolicy.ForFormalBank();
        }

        public int ReserveFloorCents() =>
            (int)(Math.Max(0, DepositsOwedCents) * Math.Clamp(Policy.ReserveRequirement01, 0f, 1f));

        public int LendableCents() =>
            Vault == null ? 0 : Math.Max(0, Vault.ReadBalanceCents() - ReserveFloorCents());

        public CreditOffer EvaluateOffer(LandLedgers.Primitives.EntityIdRegistry ids,
            CreditOfferWorkflow workflow, FinancialObligationAuthority authority,
            CreditRequest request, int dayIndex, CreditEventLog events, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (ids == null || workflow == null || request == null)
            {
                diag.Add($"Bank [{BankName}]: cannot evaluate — missing request machinery.");
                return null;
            }
            int disclosed = 0;
            foreach (CreditEvidenceRecord record in workflow.Evidence)
                if (record != null && record.DisclosedToLender
                    && string.Equals(record.Borrower, request.Borrower, StringComparison.Ordinal))
                    disclosed++;
            if (disclosed < Policy.MinimumEvidenceRecords)
            {
                diag.Add($"Bank [{BankName}]: refused '{request.Borrower}' — {disclosed} disclosed evidence record(s), policy needs {Policy.MinimumEvidenceRecords}.");
                events?.Record(dayIndex, CreditEventKind.LenderRefused, request.Borrower, BankName,
                    request.RequestedAmountCents,
                    $"bank refused: insufficient disclosed evidence ({disclosed}/{Policy.MinimumEvidenceRecords}).",
                    "", request.RequestId);
                return null;
            }
            int lendable = LendableCents();
            if (request.RequestedAmountCents > lendable)
            {
                diag.Add($"Bank [{BankName}]: refused '{request.Borrower}' — {request.RequestedAmountCents}c requested, " +
                    $"only {lendable}c lendable above the {ReserveFloorCents()}c reserve floor.");
                events?.Record(dayIndex, CreditEventKind.LenderRefused, request.Borrower, BankName,
                    request.RequestedAmountCents,
                    $"bank refused: request exceeds lendable cash above the reserve floor.",
                    "", request.RequestId);
                return null;
            }
            CreditOffer offer = workflow.Evaluate(ids, request, authority, lendable, dayIndex);
            if (offer != null && offer.Status == CreditOfferStatus.Proposed)
            {
                offer.AnnualInterestRateBps = Math.Max(offer.AnnualInterestRateBps, Policy.MinimumRateBps);
                offer.TermDays = Math.Min(offer.TermDays, Policy.MaximumTermDays);
                diag.Add($"Bank [{BankName}]: offered {offer.OfferedAmountCents}c to '{request.Borrower}' " +
                    $"at {offer.AnnualInterestRateBps}bps/{offer.TermDays}d (reserve floor {ReserveFloorCents()}c held).");
                events?.Record(dayIndex, CreditEventKind.OfferMade, request.Borrower, BankName,
                    offer.OfferedAmountCents,
                    $"bank offered {offer.OfferedAmountCents}c at {offer.AnnualInterestRateBps}bps, {offer.TermDays} days.",
                    "", request.RequestId);
            }
            return offer;
        }

        /// <summary>
        /// Accepts as the bank: re-checks the reserve floor, advances real
        /// vault cash, and files a SENIOR lien when collateral is named
        /// (institutional power private lenders do not have).
        /// </summary>
        public FinancialObligation AcceptOffer(LandLedgers.Primitives.EntityIdRegistry ids,
            CreditOfferWorkflow workflow, FinancialObligationAuthority authority,
            CreditCashBridge cash, string offerId, string borrowerOwner,
            int dayIndex, CreditEventLog events, List<string> diag)
        {
            diag = diag ?? diagnostics;
            CreditOffer offer = workflow?.FindOffer(offerId);
            if (offer == null)
            {
                diag.Add($"Bank [{BankName}]: unknown offer '{offerId}'.");
                return null;
            }
            if (Vault == null || offer.OfferedAmountCents > LendableCents())
            {
                diag.Add($"Bank [{BankName}]: cannot advance {offer.OfferedAmountCents}c — reserve floor {ReserveFloorCents()}c, lendable {LendableCents()}c.");
                events?.Record(dayIndex, CreditEventKind.LenderRefused, offer.Borrower, BankName,
                    offer.OfferedAmountCents, "bank cannot advance: reserve floor breach.",
                    "", offerId);
                return null;
            }
            CreditCashAccount vaultCash = cash != null ? cash.OpenWindow(BankName, diag) : null;
            CreditCashAccount borrowerCash = cash != null ? cash.OpenWindow(borrowerOwner, diag) : null;
            FinancialObligation obligation = null;
            if (vaultCash != null && borrowerCash != null)
                obligation = workflow.Accept(ids, offerId, authority, vaultCash, borrowerCash, dayIndex);
            if (obligation == null)
            {
                if (cash != null) { cash.DiscardWindow(vaultCash); cash.DiscardWindow(borrowerCash); }
                diag.Add($"Bank [{BankName}]: workflow accept failed — nothing moved.");
                events?.Record(dayIndex, CreditEventKind.DecisionFailed, offer.Borrower, BankName,
                    offer.OfferedAmountCents, "bank accept failed at the workflow.", "", offerId);
                return null;
            }
            if (cash.CommitWindow(vaultCash, dayIndex, $"loan advance to '{offer.Borrower}'", offer.Borrower, diag) != null
                || cash.CommitWindow(borrowerCash, dayIndex, $"loan proceeds from '{BankName}'", BankName, diag) != null)
            {
                diag.Add($"Bank [{BankName}]: INCONSISTENCY — cash commit refused after obligation creation; manual reconciliation required.");
                return null;
            }
            // The bridge already moved the vault cash (the vault is this
            // bank's registered real store) — no second debit.
            if (Policy.CanFileSeniorLien && !string.IsNullOrWhiteSpace(offer.SecurityRequired))
            {
                SecurityInterestRecord security = authority.AttachSecurity(ids, obligation.ObligationId,
                    BankName, offer.Borrower, new[] { offer.SecurityRequired }, dayIndex, 1, false);
                diag.Add(security != null
                    ? $"Bank [{BankName}]: filed SENIOR lien on '{offer.SecurityRequired}' (priority 1, no junior interests permitted)."
                    : $"Bank [{BankName}]: senior lien filing on '{offer.SecurityRequired}' was refused.");
            }
            diag.Add($"Bank [{BankName}]: advanced {offer.OfferedAmountCents}c to '{offer.Borrower}' — vault now {Vault.ReadBalanceCents()}c, reserve floor {ReserveFloorCents()}c.");
            events?.Record(dayIndex, CreditEventKind.Settlement, offer.Borrower, BankName,
                offer.OfferedAmountCents,
                $"bank advanced {offer.OfferedAmountCents}c in real vault cash; obligation '{obligation.ObligationId}'.",
                obligation.ObligationId, offerId);
            return obligation;
        }
    }
}
