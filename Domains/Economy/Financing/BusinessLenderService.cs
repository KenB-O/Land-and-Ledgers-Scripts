using System;
using System.Collections.Generic;
using LandLedgers.Economy.Labor;
using LandLedgers.Primitives;
using LandLedgers.ReadModels.Valuation;
using LandLedgers.Reputation;
using UnityEngine;

namespace LandLedgers.Economy.Financing
{
    /// <summary>
    /// Pass VIII (6.3): a business's real cash seen through its
    /// <see cref="BusinessRuntimeState"/>. Outflows refuse when the till
    /// cannot cover them (no silent clamping); inflows credit the till.
    /// This is the borrower's leg of the lending cycle; the bank's leg is
    /// the <see cref="BankDepositLedger"/> owned by
    /// <see cref="BusinessLenderService"/>.
    /// </summary>
    public sealed class BusinessCashStore : IRealCashStore
    {
        private readonly BusinessInstanceState business;

        public BusinessCashStore(BusinessInstanceState business)
        {
            this.business = business;
        }

        public string OwnerName => business != null ? business.InstanceId : string.Empty;

        public int ReadBalanceCents() =>
            business != null && business.RuntimeState != null
                ? business.RuntimeState.CurrentCashCents
                : 0;

        public string DebitCents(int dayIndex, int amountCents, string purpose, string counterparty)
        {
            if (business == null || business.RuntimeState == null)
                return "BusinessCashStore: the business has no cash ledger.";
            if (amountCents <= 0) return "BusinessCashStore: debit amount must be positive.";
            if (business.RuntimeState.CurrentCashCents < amountCents)
                return $"BusinessCashStore [{business.InstanceId}]: insufficient funds — " +
                    $"{amountCents}c requested, {business.RuntimeState.CurrentCashCents}c held. Nothing moved.";
            business.RuntimeState.AdjustCashCents(-amountCents, recordNetDelta: true);
            return null;
        }

        public string CreditCents(int dayIndex, int amountCents, string sourceClass,
            string sourceReference, string reason, string counterparty)
        {
            if (business == null || business.RuntimeState == null)
                return "BusinessCashStore: the business has no cash ledger.";
            if (amountCents <= 0) return "BusinessCashStore: credit amount must be positive.";
            business.RuntimeState.AdjustCashCents(amountCents, recordNetDelta: true);
            return null;
        }
    }

    /// <summary>
    /// Pass VIII (6.3): one arranged business loan as the lender tracks it.
    /// The debt itself lives only in <see cref="FinancialObligationAuthority"/>;
    /// this record is the lender's servicing bookkeeping (schedule, arrears,
    /// collateral, guaranty) and persists through save/load without ever
    /// re-creating the obligation.
    /// </summary>
    [Serializable]
    public sealed class BusinessLoanRecord
    {
        public string LoanKey = string.Empty;
        public string ObligationId = string.Empty;
        public string Borrower = string.Empty;
        public int PrincipalCents;
        public int IssuedDayIndex;
        public int AnnualRateBps;
        public int InstallmentPrincipalCents;
        public int PaymentIntervalDays;
        public int TermDays;
        public int NextPaymentDayIndex;
        public int LastServicedDayIndex = -1;
        public int ConsecutiveMissedPayments;
        public int LastMissedDayIndex = -1;
        public string CollateralId = string.Empty;
        public int CollateralAppraisedCents;
        public string SecurityInterestId = string.Empty;
        public string GuarantyId = string.Empty;
        public string GuarantorName = string.Empty;
        public int TotalCollectedCents;
        public string Status = "Active";
        public int DefaultConsequencesDayIndex = -1;
        public int SettledDayIndex = -1;

        public BusinessLoanRecord() { }
    }

    /// <summary>
    /// Pass VIII (6.3): the lender's memory of a borrower — failed files,
    /// caution and cooling-off. Feeds the next underwriting pass so a
    /// refused or defaulted borrower cannot immediately re-borrow clean.
    /// </summary>
    [Serializable]
    public sealed class BorrowerLenderMemory
    {
        public string Borrower = string.Empty;
        public int FailedFinancingCount;
        public float Caution01;
        public int CooldownUntilDayIndex = -1;

        public BorrowerLenderMemory() { }
    }

    /// <summary>
    /// Pass VIII (6.3): collateral the bank has taken in enforcement. The
    /// asset is the bank's now — it can never be pledged again, and its
    /// appraised value is the ceiling of the recovery it produced.
    /// </summary>
    [Serializable]
    public sealed class SeizedCollateralRecord
    {
        public string CollateralId = string.Empty;
        public string ObligationId = string.Empty;
        public string SecurityInterestId = string.Empty;
        public int AppraisedCents;
        public int SeizedDayIndex;

        public SeizedCollateralRecord() { }
    }

    /// <summary>
    /// Pass VIII (6.3): the live production business lender. A formal town
    /// bank as a real credit participant implementing
    /// <see cref="IManagerBusinessLender"/>, closing the full lending cycle:
    /// financial need, information, lender availability, underwriting,
    /// agreement, actual disbursement, debt, interest and repayment, and
    /// consequences.
    ///
    /// Cash truth: the bank lends from its <see cref="BankDepositLedger"/>
    /// (chartered capital plus deposits, minus a reserve floor) — never what
    /// it does not hold. Debt truth: exactly one
    /// <see cref="FinancialObligationAuthority"/> record per loan; this
    /// service owns no debt balances. Underwriting runs the shared
    /// <see cref="WorkingCapitalUnderwriting"/> on real projections (the
    /// valuation read model), real exposure (the obligation authority) and
    /// real collateral — and may refuse. Disbursement debits the bank's real
    /// cash before the obligation exists, so no unfunded proceeds can occur.
    /// Servicing (<see cref="ProcessDay"/>) accrues real interest, collects
    /// scheduled payments from the borrower's real till, and on default
    /// calls established guaranties and enforces pledged collateral through
    /// the authority's legitimate rights.
    /// </summary>
    public sealed class BusinessLenderService : IManagerBusinessLender
    {
        // TUNING: charter capital is a calibration figure, documented at the
        // chartering call. One-time, finite, persisted — never topped up silently.
        public const int DefaultCharterCapitalCents = 2500000;
        // TUNING: 12 monthly installments over a 365-day working-capital term.
        public const int LoanTermDays = 365;
        public const int PaymentIntervalDays = 30;
        public const int InstallmentsPerLoan = 12;
        // TUNING: missed-payment ladder — delinquent after the grace, defaulted
        // after four consecutive misses (matches the player debt lane).
        public const int MissedPaymentGraceDays = 7;
        public const int DefaultMissedPaymentCount = 4;
        // TUNING: loans at or above this size require the owner's guaranty.
        public const int GuarantyRequiredAboveCents = 20000;

        private string lenderName = "Frontier Town Bank";
        private readonly CreditLenderPolicy policy = CreditLenderPolicy.ForFormalBank();
        private readonly BankDepositLedger depositLedger;
        private bool chartered;
        private int charterCapitalCents;
        private string charterSource = string.Empty;

        private readonly Dictionary<string, BusinessLoanRecord> loans =
            new Dictionary<string, BusinessLoanRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, BorrowerLenderMemory> borrowerMemory =
            new Dictionary<string, BorrowerLenderMemory>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> arrangedKeys =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<SeizedCollateralRecord> seizedCollateral = new List<SeizedCollateralRecord>();
        private readonly List<string> diagnostics = new List<string>();

        // Bound at composition time; never serialized.
        private EntityIdRegistry ids;
        private FinancialObligationAuthority authority;
        private CreditOfferWorkflow workflow;
        private CreditEventLog events;
        private EnterpriseValuationReadModel valuation;
        private Func<string, BusinessInstanceState> findBusiness;

        public BusinessLenderService() : this("Frontier Town Bank") { }

        public BusinessLenderService(string lenderName)
        {
            this.lenderName = string.IsNullOrWhiteSpace(lenderName) ? "Frontier Town Bank" : lenderName;
            depositLedger = new BankDepositLedger(this.lenderName);
        }

        public string LenderName => lenderName ?? string.Empty;
        public CreditLenderPolicy Policy => policy;
        public IReadOnlyList<string> Diagnostics => diagnostics;
        public bool IsChartered => chartered;
        public int CashOnHandCents => depositLedger != null ? depositLedger.CashOnHandCents : 0;

        public void BindHubServices(EntityIdRegistry ids, FinancialObligationAuthority authority,
            CreditOfferWorkflow workflow, CreditEventLog events, EnterpriseValuationReadModel valuation)
        {
            this.ids = ids;
            this.authority = authority;
            this.workflow = workflow;
            this.events = events;
            this.valuation = valuation;
        }

        public void BindBusinessLookup(Func<string, BusinessInstanceState> findBusiness)
        {
            this.findBusiness = findBusiness;
        }

        /// <summary>
        /// One-time capitalization of the town bank: named-source money,
        /// never conjured. Idempotent — a second call is a no-op, never a
        /// top-up, so capital cannot drift.
        /// </summary>
        public string EnsureCharteredCapital(int capitalCents, string capitalSource, int dayIndex,
            List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (chartered)
            {
                diag.Add($"BusinessLenderService [{lenderName}]: already chartered — no top-up.");
                return null;
            }
            if (capitalCents <= 0) return "BusinessLenderService: charter capital must be positive.";
            if (string.IsNullOrWhiteSpace(capitalSource))
                return "BusinessLenderService: the charter capital source must be named — capital is never conjured.";
            string problem = depositLedger.RecordCapitalInflow(capitalCents, capitalSource, diag);
            if (problem != null) return problem;
            chartered = true;
            charterCapitalCents = capitalCents;
            charterSource = capitalSource;
            diag.Add($"BusinessLenderService [{lenderName}]: chartered with {capitalCents}c ({capitalSource}) on day {dayIndex}.");
            return null;
        }

        public int ReserveFloorCents() =>
            (int)(Math.Max(0, depositLedger.DepositsOwedCents()) * Mathf.Clamp01(policy.ReserveRequirement01));

        public int LendableCents() =>
            Math.Max(0, depositLedger.CashOnHandCents - ReserveFloorCents());

        // ================= the lending cycle =================

        /// <summary>
        /// The full cycle for one manager-authorized borrowing: need,
        /// information, availability, underwriting, agreement, disbursement.
        /// Returns the single created <see cref="FinancialObligation"/>, or
        /// null with a refusal reason — never an unfunded promise. The loan
        /// proceeds reach the borrower's till through the existing delegation
        /// path (the caller credits the business after this returns).
        /// </summary>
        public FinancialObligation TryArrangeLoan(EntityIdRegistry ids,
            FinancialObligationAuthority obligations, string debtorBusinessId,
            int amountCents, int dayIndex, List<string> diagnostics, out string refusalReason)
        {
            diagnostics = diagnostics ?? new List<string>();
            refusalReason = string.Empty;

            // Readiness: the service must be bound and capitalized.
            if (ids == null || obligations == null)
                return Refuse(diagnostics, out refusalReason,
                    "the lending service is not bound to the obligation authority.");
            if (!chartered)
                return Refuse(diagnostics, out refusalReason,
                    $"[{lenderName}] is not chartered — no lending capital exists yet.");

            // Financial need.
            if (amountCents <= 0)
                return Refuse(diagnostics, out refusalReason, "the borrowing amount must be positive.");
            if (string.IsNullOrWhiteSpace(debtorBusinessId))
                return Refuse(diagnostics, out refusalReason, "the borrowing business must be named.");

            // Exactly-once: a second identical approval cannot duplicate the debt.
            string loanKey = debtorBusinessId + "|" + amountCents + "|" + dayIndex;
            if (arrangedKeys.TryGetValue(loanKey, out string existingObligationId))
                return Refuse(diagnostics, out refusalReason,
                    $"identical loan already arranged today (obligation '{existingObligationId}') — refusing a duplicate.");

            // The borrower must be a real business with a real till.
            BusinessInstanceState business = findBusiness != null ? findBusiness(debtorBusinessId) : null;
            if (business == null)
                return Refuse(diagnostics, out refusalReason,
                    $"unknown business '{debtorBusinessId}' — the bank lends to real businesses only.");
            if (business.RuntimeState == null)
                return Refuse(diagnostics, out refusalReason,
                    $"business '{debtorBusinessId}' has no cash ledger — proceeds could not land.");

            // Information: real projections, real exposure, real collateral.
            BorrowerFinancialInformation info = BuildBorrowerInformation(business, debtorBusinessId, obligations);

            // Lender availability: the bank must really hold the funds.
            int reserveFloor = ReserveFloorCents();
            int lendable = LendableCents();
            if (amountCents > lendable)
            {
                RecordRefusalMemory(debtorBusinessId, dayIndex);
                return Refuse(diagnostics, out refusalReason,
                    $"[{lenderName}] holds {depositLedger.CashOnHandCents}c with a {reserveFloor}c reserve floor — " +
                    $"only {lendable}c is lendable. The bank cannot lend what it does not have.");
            }
            int singleBorrowerCap = (int)(lendable * Mathf.Clamp01(policy.MaxSingleBorrowerShare01));
            if (amountCents > singleBorrowerCap)
            {
                RecordRefusalMemory(debtorBusinessId, dayIndex);
                return Refuse(diagnostics, out refusalReason,
                    $"[{lenderName}] caps one borrower at {singleBorrowerCap}c of lendable funds — concentration limit.");
            }

            // Underwriting: real projections, exposure and collateral — may refuse.
            BankLoanUnderwritingResult underwriting = WorkingCapitalUnderwriting.EvaluateWorkingCapitalApplication(
                amountCents,
                info.CashCents,
                info.OwnedAssetValueCents,
                info.WeeklyNetCashFlowCents,
                info.ExistingWeeklyDebtServiceCents,
                info.LenderTrust01,
                info.FailedFinancingCount,
                info.DefaultCount,
                info.HasActiveBankLoan,
                LenderProfile.DefaultLocalBank,
                dayIndex,
                info.CooldownUntilDayIndex,
                info.Caution01);
            if (underwriting == null || underwriting.decision != FinancingDecision.Approved)
            {
                RecordRefusalMemory(debtorBusinessId, dayIndex);
                events?.Record(dayIndex, CreditEventKind.LenderRefused, debtorBusinessId, lenderName,
                    amountCents,
                    $"bank underwriting refused: {underwriting?.declineReason ?? "evaluation failed"}.",
                    "", "");
                return Refuse(diagnostics, out refusalReason,
                    $"[{lenderName}] underwriting refused: {underwriting?.declineReason ?? "the file does not fit bank policy"}.");
            }

            // The documentary trail: a filed request and disclosed evidence.
            if (workflow != null)
            {
                CreditRequest request = workflow.SubmitRequest(ids, debtorBusinessId, lenderName,
                    amountCents, "working-capital", LoanTermDays, dayIndex, "business operating cash flow");
                if (request != null)
                {
                    workflow.DiscloseEvidence(ids, request, "valuation-read-model",
                        $"maintainable weekly {info.WeeklyNetCashFlowCents}c; transferable assets {info.OwnedAssetValueCents}c; till {info.CashCents}c.",
                        "", dayIndex);
                    workflow.DiscloseEvidence(ids, request, "obligation-authority",
                        $"total outstanding {info.TotalOutstandingCents}c across live obligations; {info.DefaultCount} defaulted; " +
                        $"existing weekly debt service {info.ExistingWeeklyDebtServiceCents}c.",
                        "", dayIndex);
                }
            }

            // Agreement terms: the underwritten rate floored at the bank's policy minimum.
            int rateBps = Math.Max(policy.MinimumRateBps, underwriting.term.annualInterestRateBps);
            int installment = Math.Max(1, amountCents / InstallmentsPerLoan);

            // Disbursement FIRST, through the bank's real cash: a refusal here
            // aborts before any debt exists — no unfunded proceeds, ever.
            string loanRef = $"loan:{debtorBusinessId}:d{dayIndex}:{amountCents}c";
            string disburseProblem = depositLedger.DisburseLoan(amountCents, loanRef, diagnostics);
            if (disburseProblem != null)
            {
                RecordRefusalMemory(debtorBusinessId, dayIndex);
                return Refuse(diagnostics, out refusalReason, disburseProblem);
            }

            // The single debt record.
            FinancialObligation obligation = obligations.CreateWithTerms(ids, FinancialObligationKind.Loan,
                debtorBusinessId, lenderName, amountCents, dayIndex,
                $"working-capital loan, {rateBps}bps, {InstallmentsPerLoan} monthly installments of {installment}c",
                "manager-authorized working capital",
                new FinancialPaymentTerms
                {
                    AnnualInterestRateBps = rateBps,
                    Structure = FinancialPaymentStructure.PrincipalInstallments,
                    PaymentIntervalDays = PaymentIntervalDays,
                    InstallmentPrincipalCents = installment,
                    AllowsEarlyPayoff = true,
                });
            if (obligation == null)
            {
                // Unreachable after validation, but the cash must never be left hanging.
                depositLedger.ReceiveLoanPayment(amountCents, loanRef + ":refund", diagnostics);
                return Refuse(diagnostics, out refusalReason,
                    "the obligation authority refused the loan record — disbursed cash restored, nothing created.");
            }
            obligation.MaturityDayIndex = dayIndex + LoanTermDays;

            // Secured obligation: the bank files its SENIOR lien on the
            // borrower's transferable assets (institutional power private
            // lenders do not have).
            string securityId = string.Empty;
            string collateralId = string.Empty;
            int collateralAppraised = 0;
            if (policy.CanFileSeniorLien && info.OwnedAssetValueCents > 0)
            {
                collateralId = "business-transferable-assets:" + debtorBusinessId;
                collateralAppraised = info.OwnedAssetValueCents;
                SecurityInterestRecord security = obligations.AttachSecurity(ids, obligation.ObligationId,
                    lenderName, debtorBusinessId, new[] { collateralId }, dayIndex,
                    1, false);
                if (security != null)
                {
                    securityId = security.SecurityInterestId;
                    diagnostics.Add($"BusinessLenderService [{lenderName}]: filed SENIOR lien on '{collateralId}' " +
                        $"appraised {collateralAppraised}c (priority 1, no junior interests).");
                }
            }

            // Guaranty where established: significant loans need the owner's name behind them.
            string guarantyId = string.Empty;
            string guarantorName = string.Empty;
            if (amountCents >= GuarantyRequiredAboveCents)
            {
                guarantorName = OwnerGuarantorName(business);
                if (!string.IsNullOrWhiteSpace(guarantorName))
                {
                    GuarantyRecord guaranty = obligations.AddGuaranty(ids, obligation.ObligationId,
                        lenderName, debtorBusinessId, guarantorName, obligation.TotalOutstandingCents);
                    if (guaranty != null)
                    {
                        guarantyId = guaranty.GuarantyId;
                        diagnostics.Add($"BusinessLenderService [{lenderName}]: owner guaranty by '{guarantorName}' " +
                            $"up to {obligation.TotalOutstandingCents}c.");
                    }
                }
            }

            arrangedKeys[loanKey] = obligation.ObligationId;
            loans[obligation.ObligationId] = new BusinessLoanRecord
            {
                LoanKey = loanKey,
                ObligationId = obligation.ObligationId,
                Borrower = debtorBusinessId,
                PrincipalCents = amountCents,
                IssuedDayIndex = dayIndex,
                AnnualRateBps = rateBps,
                InstallmentPrincipalCents = installment,
                PaymentIntervalDays = PaymentIntervalDays,
                TermDays = LoanTermDays,
                NextPaymentDayIndex = dayIndex + PaymentIntervalDays,
                CollateralId = collateralId,
                CollateralAppraisedCents = collateralAppraised,
                SecurityInterestId = securityId,
                GuarantyId = guarantyId,
                GuarantorName = guarantorName,
                Status = "Active",
            };
            events?.Record(dayIndex, CreditEventKind.Settlement, debtorBusinessId, lenderName,
                amountCents,
                $"bank advanced {amountCents}c in real vault cash at {rateBps}bps; obligation '{obligation.ObligationId}'. " +
                $"Reserve floor {reserveFloor}c held; {depositLedger.CashOnHandCents}c cash on hand remains.",
                obligation.ObligationId, "");
            diagnostics.Add($"BusinessLenderService [{lenderName}]: lent {amountCents}c to '{debtorBusinessId}' " +
                $"(obligation {obligation.ObligationId}) — vault now {depositLedger.CashOnHandCents}c.");
            return obligation;
        }

        // ================= servicing: interest, repayment, consequences =================

        /// <summary>
        /// Daily servicing for every arranged loan: interest accrues as a
        /// real obligation, scheduled payments are collected from the
        /// borrower's real till, missed payments climb the delinquency
        /// ladder, and default triggers the documented consequences —
        /// guaranty calls and collateral enforcement through the authority's
        /// legitimate rights. Idempotent per day.
        /// </summary>
        public void ProcessDay(int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (authority == null)
            {
                diagnostics.Add("BusinessLenderService: no obligation authority bound — servicing skipped.");
                return;
            }
            // Snapshot: consequences create new obligations (guaranty calls).
            var records = new List<BusinessLoanRecord>(loans.Values);
            foreach (BusinessLoanRecord record in records)
            {
                if (record != null) ServiceLoan(record, dayIndex, diagnostics);
            }
        }

        private void ServiceLoan(BusinessLoanRecord record, int dayIndex, List<string> diagnostics)
        {
            FinancialObligation obligation = authority.Find(record.ObligationId);
            if (obligation == null)
            {
                diagnostics.Add($"BusinessLenderService: obligation '{record.ObligationId}' is gone — servicing skipped, loan kept for the record.");
                return;
            }
            if (obligation.Settled)
            {
                FinalizeSettledLoan(record, obligation, dayIndex, diagnostics);
                return;
            }
            // Interest accrues as a real obligation; the authority guards
            // double accrual via LastInterestAccrualDayIndex.
            authority.AccrueInterestThroughDay(record.ObligationId, dayIndex);

            if (dayIndex < record.NextPaymentDayIndex) return;
            if (dayIndex <= record.LastServicedDayIndex) return; // idempotent per day
            record.LastServicedDayIndex = dayIndex;

            int due = Math.Min(
                record.InstallmentPrincipalCents
                    + Math.Max(0, obligation.AccruedInterestCents)
                    + Math.Max(0, obligation.FeesCents),
                obligation.TotalOutstandingCents);
            if (due <= 0)
            {
                record.NextPaymentDayIndex = dayIndex + record.PaymentIntervalDays;
                return;
            }

            BusinessInstanceState business = findBusiness != null ? findBusiness(record.Borrower) : null;
            int borrowerCash = business != null && business.RuntimeState != null
                ? business.RuntimeState.CurrentCashCents
                : 0;
            if (business == null || business.RuntimeState == null || borrowerCash < due)
            {
                RecordMissedPayment(record, obligation, due, borrowerCash, dayIndex, diagnostics);
                return;
            }

            // Collect: the authority payment first (validated, so it cannot
            // fail after cash moves), then the real cash legs.
            FinancialPaymentRecord payment = authority.ApplyPayment(record.ObligationId, due, dayIndex,
                record.Borrower, lenderName, $"loan-service:{record.ObligationId}:{dayIndex}");
            if (payment == null)
            {
                diagnostics.Add($"BusinessLenderService: INCONSISTENCY — authority refused a validated {due}c collection " +
                    $"on '{record.ObligationId}'. No cash moved.");
                return;
            }
            business.RuntimeState.AdjustCashCents(-payment.AmountCents, recordNetDelta: true);
            depositLedger.ReceiveLoanPayment(payment.AmountCents, $"loan:{record.ObligationId}", diagnostics);
            record.TotalCollectedCents += payment.AmountCents;
            record.ConsecutiveMissedPayments = 0;
            record.NextPaymentDayIndex = dayIndex + record.PaymentIntervalDays;
            events?.Record(dayIndex, CreditEventKind.PaymentCollected, record.Borrower, lenderName,
                payment.AmountCents,
                $"scheduled loan payment {payment.AmountCents}c collected " +
                $"(principal {payment.PrincipalCents}c, interest {payment.InterestCents}c, fees {payment.FeesCents}c); " +
                $"obligation '{record.ObligationId}' now {obligation.TotalOutstandingCents}c outstanding.",
                record.ObligationId, "");
            diagnostics.Add($"BusinessLenderService: collected {payment.AmountCents}c on '{record.ObligationId}' — " +
                $"{obligation.TotalOutstandingCents}c outstanding.");
        }

        private void RecordMissedPayment(BusinessLoanRecord record, FinancialObligation obligation,
            int dueCents, int borrowerCashCents, int dayIndex, List<string> diagnostics)
        {
            record.ConsecutiveMissedPayments++;
            record.LastMissedDayIndex = dayIndex;
            record.NextPaymentDayIndex = dayIndex + record.PaymentIntervalDays;
            BorrowerLenderMemory memory = GetBorrowerMemory(record.Borrower);
            memory.Caution01 = Math.Min(1f, memory.Caution01 + 0.2f);
            if (dayIndex > record.NextPaymentDayIndex - record.PaymentIntervalDays + MissedPaymentGraceDays
                || record.ConsecutiveMissedPayments >= 2)
                authority.MarkDelinquent(record.ObligationId);
            events?.Record(dayIndex, CreditEventKind.PaymentMissed, record.Borrower, lenderName,
                dueCents,
                $"scheduled payment of {dueCents}c missed — borrower holds {borrowerCashCents}c " +
                $"({record.ConsecutiveMissedPayments} consecutive).",
                record.ObligationId, "");
            diagnostics.Add($"BusinessLenderService: MISSED payment {dueCents}c on '{record.ObligationId}' " +
                $"({record.ConsecutiveMissedPayments} consecutive) — borrower holds {borrowerCashCents}c.");
            if (record.ConsecutiveMissedPayments >= DefaultMissedPaymentCount)
                ApplyDefaultConsequences(record, obligation, dayIndex, diagnostics);
        }

        /// <summary>
        /// Default consequences, applied exactly once: the obligation is
        /// marked defaulted, every established guaranty is called (a real new
        /// obligation on the guarantor — the covered principal is not
        /// duplicated), and pledged collateral is enforced through the senior
        /// lien — the bank takes the asset at its appraised value and the
        /// recovery reduces the debt. Any shortfall stays live as a
        /// deficiency on the same obligation.
        /// </summary>
        private void ApplyDefaultConsequences(BusinessLoanRecord record, FinancialObligation obligation,
            int dayIndex, List<string> diagnostics)
        {
            if (record.DefaultConsequencesDayIndex >= 0) return;
            record.DefaultConsequencesDayIndex = dayIndex;
            record.Status = "Defaulted";
            authority.MarkDefaulted(record.ObligationId);
            BorrowerLenderMemory memory = GetBorrowerMemory(record.Borrower);
            memory.FailedFinancingCount++;
            memory.Caution01 = 1f;
            memory.CooldownUntilDayIndex = dayIndex + 30;
            events?.Record(dayIndex, CreditEventKind.DelinquencyMarked, record.Borrower, lenderName,
                obligation.TotalOutstandingCents,
                $"obligation '{record.ObligationId}' DEFAULTED after {record.ConsecutiveMissedPayments} missed payments " +
                $"({obligation.TotalOutstandingCents}c outstanding).",
                record.ObligationId, "");
            diagnostics.Add($"BusinessLenderService: '{record.ObligationId}' DEFAULTED — {obligation.TotalOutstandingCents}c outstanding.");

            // Guaranty where established: the call is a real new obligation.
            if (ids != null && !string.IsNullOrWhiteSpace(record.GuarantyId))
            {
                FinancialObligation called = authority.CallGuaranty(ids, record.GuarantyId,
                    obligation.TotalOutstandingCents, dayIndex);
                if (called != null)
                {
                    events?.Record(dayIndex, CreditEventKind.GuarantyCalled, record.GuarantorName, lenderName,
                        called.TotalOutstandingCents,
                        $"guaranty '{record.GuarantyId}' called: '{record.GuarantorName}' now owes {called.TotalOutstandingCents}c " +
                        $"for '{record.Borrower}'s default (obligation '{called.ObligationId}').",
                        called.ObligationId, record.GuarantyId);
                    diagnostics.Add($"BusinessLenderService: guaranty '{record.GuarantyId}' CALLED — " +
                        $"'{record.GuarantorName}' owes {called.TotalOutstandingCents}c.");
                }
                else
                {
                    diagnostics.Add($"BusinessLenderService: guaranty '{record.GuarantyId}' call was refused by the authority.");
                }
            }

            // Collateral enforcement through the senior lien: the bank takes
            // the pledged asset at its appraised value (satisfaction in kind)
            // and the recovery reduces the debt. The asset is seized — it can
            // never be pledged again.
            if (!string.IsNullOrWhiteSpace(record.SecurityInterestId) && record.CollateralAppraisedCents > 0)
            {
                SecurityInterestRecord security = FindSecurity(record.SecurityInterestId);
                if (security != null && !security.Released && string.IsNullOrWhiteSpace(security.EnforcementState))
                {
                    security.EnforcementState = $"Enforced:{dayIndex}";
                    int recovery = Math.Min(record.CollateralAppraisedCents, obligation.TotalOutstandingCents);
                    if (recovery > 0)
                    {
                        FinancialPaymentRecord recoveryPayment = authority.ApplyPayment(record.ObligationId,
                            recovery, dayIndex, record.Borrower, lenderName,
                            $"collateral-enforcement:{record.SecurityInterestId}");
                        if (recoveryPayment != null)
                        {
                            seizedCollateral.Add(new SeizedCollateralRecord
                            {
                                CollateralId = record.CollateralId,
                                ObligationId = record.ObligationId,
                                SecurityInterestId = record.SecurityInterestId,
                                AppraisedCents = record.CollateralAppraisedCents,
                                SeizedDayIndex = dayIndex,
                            });
                            events?.Record(dayIndex, CreditEventKind.PaymentCollected, record.Borrower, lenderName,
                                recoveryPayment.AmountCents,
                                $"collateral '{record.CollateralId}' ENFORCED under the senior lien — " +
                                $"bank takes the asset at {record.CollateralAppraisedCents}c appraised; " +
                                $"{recoveryPayment.AmountCents}c recovered against '{record.ObligationId}'. " +
                                $"Remaining {obligation.TotalOutstandingCents}c stays live as deficiency.",
                                record.ObligationId, record.SecurityInterestId);
                            diagnostics.Add($"BusinessLenderService: ENFORCED '{record.CollateralId}' — " +
                                $"recovered {recoveryPayment.AmountCents}c; {obligation.TotalOutstandingCents}c deficiency remains.");
                        }
                    }
                }
            }
        }

        private void FinalizeSettledLoan(BusinessLoanRecord record, FinancialObligation obligation,
            int dayIndex, List<string> diagnostics)
        {
            if (string.Equals(record.Status, "Settled", StringComparison.Ordinal)) return;
            record.Status = "Settled";
            record.SettledDayIndex = dayIndex;
            if (!string.IsNullOrWhiteSpace(record.SecurityInterestId))
                authority.ReleaseSecurity(record.SecurityInterestId, "loan satisfied");
            if (!string.IsNullOrWhiteSpace(record.GuarantyId))
            {
                foreach (GuarantyRecord guaranty in authority.Guaranties)
                {
                    if (guaranty != null && string.Equals(guaranty.GuarantyId, record.GuarantyId, StringComparison.Ordinal)
                        && !guaranty.Called)
                    {
                        guaranty.Released = true;
                        break;
                    }
                }
            }
            events?.Record(dayIndex, CreditEventKind.PaymentCollected, record.Borrower, lenderName,
                0,
                $"loan '{record.ObligationId}' satisfied in full — {record.TotalCollectedCents}c collected over its life; " +
                "security released, guaranty released.",
                record.ObligationId, "");
            diagnostics.Add($"BusinessLenderService: '{record.ObligationId}' SETTLED — security and guaranty released.");
        }

        // ================= information: real projections, exposure, standing =================

        private sealed class BorrowerFinancialInformation
        {
            public int CashCents;
            public int WeeklyNetCashFlowCents;
            public int OwnedAssetValueCents;
            public int ExistingWeeklyDebtServiceCents;
            public int TotalOutstandingCents;
            public float LenderTrust01;
            public int FailedFinancingCount;
            public int DefaultCount;
            public bool HasActiveBankLoan;
            public float Caution01;
            public int CooldownUntilDayIndex = -1;
        }

        private BorrowerFinancialInformation BuildBorrowerInformation(BusinessInstanceState business,
            string debtorBusinessId, FinancialObligationAuthority obligations)
        {
            var info = new BorrowerFinancialInformation
            {
                CashCents = business.RuntimeState.CurrentCashCents,
            };
            ValuationEvidence evidence = valuation != null ? valuation.GetEvidence(business.InstanceId) : null;
            info.OwnedAssetValueCents = evidence != null ? evidence.TransferableAssetValueCents : 0;
            info.WeeklyNetCashFlowCents = ProjectWeeklyNetCents(business, evidence);

            int weeklyService = 0;
            int totalOutstanding = 0;
            int defaultCount = 0;
            bool hasActiveBankLoan = false;
            foreach (FinancialObligation obligation in obligations.Obligations)
            {
                if (obligation == null || !string.Equals(obligation.Debtor, debtorBusinessId, StringComparison.Ordinal))
                    continue;
                if (obligation.Status == FinancialObligationStatus.Defaulted) defaultCount++;
                if (obligation.Settled) continue;
                totalOutstanding += obligation.TotalOutstandingCents;
                if (string.Equals(obligation.Creditor, lenderName, StringComparison.Ordinal)
                    && obligation.Kind == FinancialObligationKind.Loan)
                    hasActiveBankLoan = true;
                FinancialPaymentTerms terms = obligation.PaymentTerms;
                if (terms != null && terms.InstallmentPrincipalCents > 0 && terms.PaymentIntervalDays > 0)
                    weeklyService += terms.InstallmentPrincipalCents * 7 / Math.Max(1, terms.PaymentIntervalDays);
            }
            info.ExistingWeeklyDebtServiceCents = weeklyService;
            info.TotalOutstandingCents = totalOutstanding;
            info.DefaultCount = defaultCount;
            info.HasActiveBankLoan = hasActiveBankLoan;

            BorrowerLenderMemory memory = GetBorrowerMemory(debtorBusinessId);
            info.FailedFinancingCount = memory.FailedFinancingCount;
            info.Caution01 = memory.Caution01;
            info.CooldownUntilDayIndex = memory.CooldownUntilDayIndex;
            info.LenderTrust01 = DeriveLenderTrust(business, memory, defaultCount);
            return info;
        }

        /// <summary>
        /// Current financial projection: the maintainable weekly operating
        /// profit from the valuation read model (trailing window, the same
        /// basis the valuation itself uses), falling back to the last
        /// realized weekly cash delta when no valuation evidence exists.
        /// </summary>
        private static int ProjectWeeklyNetCents(BusinessInstanceState business, ValuationEvidence evidence)
        {
            IReadOnlyList<int> profits = evidence != null ? evidence.WeeklyOperatingProfitCents : null;
            if (profits != null && profits.Count > 0)
            {
                int window = Math.Min(12, profits.Count);
                long sum = 0;
                for (int i = 0; i < window; i++) sum += profits[profits.Count - window + i];
                return (int)(sum / Math.Max(1, window));
            }
            return business.LastWeeklyCashAfterCents - business.LastWeeklyCashBeforeCents;
        }

        private static float DeriveLenderTrust(BusinessInstanceState business, BorrowerLenderMemory memory,
            int defaultCount)
        {
            BusinessReputationState reputation = business.BusinessReputation;
            float average = 0.5f;
            if (reputation != null)
            {
                average = (reputation.stockReliability01 + reputation.valueFairness01
                    + reputation.serviceExperience01 + reputation.conditionPresentationTrust01
                    + reputation.productTradeConfidence01) / 5f;
            }
            // Lender policy (TUNING): standing starts from the business's real
            // operating reputation and is reduced by real failure history.
            return Mathf.Clamp01(0.30f + 0.45f * average
                - 0.08f * Math.Max(0, memory.FailedFinancingCount)
                - 0.12f * Math.Max(0, defaultCount));
        }

        private static string OwnerGuarantorName(BusinessInstanceState business)
        {
            BusinessOwnerIdentity owner = business != null ? business.Owner : null;
            if (owner == null) return string.Empty;
            if (owner.OwnerKind == BusinessOwnerKind.Player) return "Player";
            if (owner.OwnerKind == BusinessOwnerKind.Npc && owner.PersonId >= 0
                && !string.IsNullOrWhiteSpace(owner.DisplayName))
                return owner.DisplayName;
            return string.Empty;
        }

        private BorrowerLenderMemory GetBorrowerMemory(string borrower)
        {
            if (!borrowerMemory.TryGetValue(borrower ?? string.Empty, out BorrowerLenderMemory memory))
            {
                memory = new BorrowerLenderMemory { Borrower = borrower ?? string.Empty };
                borrowerMemory[borrower ?? string.Empty] = memory;
            }
            return memory;
        }

        private void RecordRefusalMemory(string borrower, int dayIndex)
        {
            BorrowerLenderMemory memory = GetBorrowerMemory(borrower);
            memory.FailedFinancingCount++;
            memory.Caution01 = Math.Min(1f, memory.Caution01 + 0.25f);
            memory.CooldownUntilDayIndex = Math.Max(memory.CooldownUntilDayIndex, dayIndex + 7);
        }

        private SecurityInterestRecord FindSecurity(string securityInterestId)
        {
            if (authority == null || string.IsNullOrWhiteSpace(securityInterestId)) return null;
            foreach (SecurityInterestRecord security in authority.Securities)
                if (security != null && string.Equals(security.SecurityInterestId, securityInterestId, StringComparison.Ordinal))
                    return security;
            return null;
        }

        private static FinancialObligation Refuse(List<string> diagnostics, out string refusalReason, string reason)
        {
            refusalReason = reason ?? string.Empty;
            diagnostics.Add("BusinessLenderService: REFUSED — " + refusalReason);
            return null;
        }

        // ================= save / load (clear-then-load, idempotent) =================

        [Serializable]
        public sealed class ArrangedLoanKey
        {
            public string LoanKey = string.Empty;
            public string ObligationId = string.Empty;

            public ArrangedLoanKey() { }
        }

        [Serializable]
        public sealed class BusinessLenderServiceSaveDto
        {
            public string LenderName = string.Empty;
            public bool Chartered;
            public int CharterCapitalCents;
            public string CharterSource = string.Empty;
            public BankDepositLedger.BankDepositLedgerSaveDto DepositLedger = new BankDepositLedger.BankDepositLedgerSaveDto();
            public List<BusinessLoanRecord> Loans = new List<BusinessLoanRecord>();
            public List<BorrowerLenderMemory> BorrowerMemories = new List<BorrowerLenderMemory>();
            public List<ArrangedLoanKey> ArrangedKeys = new List<ArrangedLoanKey>();
            public List<SeizedCollateralRecord> SeizedCollateral = new List<SeizedCollateralRecord>();

            public BusinessLenderServiceSaveDto() { }
        }

        public BusinessLenderServiceSaveDto CaptureSaveDto()
        {
            var dto = new BusinessLenderServiceSaveDto
            {
                LenderName = lenderName,
                Chartered = chartered,
                CharterCapitalCents = charterCapitalCents,
                CharterSource = charterSource,
                DepositLedger = depositLedger.CaptureSaveDto(),
            };
            dto.Loans.AddRange(loans.Values);
            dto.BorrowerMemories.AddRange(borrowerMemory.Values);
            foreach (KeyValuePair<string, string> pair in arrangedKeys)
                dto.ArrangedKeys.Add(new ArrangedLoanKey { LoanKey = pair.Key, ObligationId = pair.Value });
            dto.SeizedCollateral.AddRange(seizedCollateral);
            return dto;
        }

        public void LoadFromSaveDto(BusinessLenderServiceSaveDto dto)
        {
            loans.Clear();
            borrowerMemory.Clear();
            arrangedKeys.Clear();
            seizedCollateral.Clear();
            diagnostics.Clear();
            chartered = false;
            charterCapitalCents = 0;
            charterSource = string.Empty;
            depositLedger.LoadFromSaveDto(null);
            if (dto == null) return;
            // The obligations themselves persist through the authority's own
            // save data — this restores only the lender's servicing books, so
            // debt and disbursement are never duplicated on load.
            lenderName = string.IsNullOrWhiteSpace(dto.LenderName) ? lenderName : dto.LenderName;
            chartered = dto.Chartered;
            charterCapitalCents = Math.Max(0, dto.CharterCapitalCents);
            charterSource = dto.CharterSource ?? string.Empty;
            depositLedger.LoadFromSaveDto(dto.DepositLedger);
            if (dto.Loans != null)
                foreach (BusinessLoanRecord record in dto.Loans)
                    if (record != null && !string.IsNullOrWhiteSpace(record.ObligationId)
                        && !loans.ContainsKey(record.ObligationId))
                        loans[record.ObligationId] = record;
            if (dto.BorrowerMemories != null)
                foreach (BorrowerLenderMemory memory in dto.BorrowerMemories)
                    if (memory != null && !string.IsNullOrWhiteSpace(memory.Borrower))
                        borrowerMemory[memory.Borrower] = memory;
            if (dto.ArrangedKeys != null)
                foreach (ArrangedLoanKey key in dto.ArrangedKeys)
                    if (key != null && !string.IsNullOrWhiteSpace(key.LoanKey)
                        && !arrangedKeys.ContainsKey(key.LoanKey))
                        arrangedKeys[key.LoanKey] = key.ObligationId ?? string.Empty;
            if (dto.SeizedCollateral != null)
                seizedCollateral.AddRange(dto.SeizedCollateral);
        }
    }
}
