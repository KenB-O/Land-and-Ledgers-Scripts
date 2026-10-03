using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Financing
{
    /// <summary>NX-3B: demand (withdrawable anytime) vs term (locked to maturity).</summary>
    public enum DepositKind
    {
        Unspecified = 0,
        Demand = 1,
        Term = 2,
    }

    /// <summary>
    /// NX-3B: one deposit account. The depositor is always named — a person,
    /// business, or institution. Historical (1870s): demand deposits typically
    /// paid no interest; time deposits / certificates of deposit paid interest
    /// per stated terms. Rates are terms-as-written, never invented.
    /// </summary>
    [Serializable]
    public sealed class DepositAccount
    {
        public string AccountId = string.Empty;
        public string DepositorName = string.Empty;
        public DepositKind Kind = DepositKind.Unspecified;
        public int BalanceCents;
        public int OpenedDayIndex;
        public int TermDays;              // Term only
        public int MaturityDayIndex;      // Term only
        public int InterestRateBps;       // Term only: basis points per annum, as written
        public int InterestAccruedCents;  // owed but not yet credited

        public DepositAccount() { }
    }

    /// <summary>
    /// NX-3B: a bank's deposit book. Canon §18.8: "A player-owned bank must
    /// operate as a business with both assets and liabilities. Deposits are
    /// money the bank owes to depositors, not free owner cash."
    ///
    /// The ledger tracks two honest numbers: CashOnHand (liquid cash the bank
    /// actually holds) and DepositsOwed (what it owes depositors). Lending
    /// moves cash out while the liability stays — that gap IS the liquidity
    /// risk (Canon §18.9). A withdrawal the bank cannot cover is refused
    /// loudly, never faked: a bank run is "an extreme acceleration of a real
    /// liability, not a random event card" (Canon §18.10).
    ///
    /// Canon §18.8: "Do not reduce this to one Reserve slider that directly
    /// grants profit or safety" — there is no reserve ratio here, only the
    /// actual composition and timing of commitments.
    /// </summary>
    public sealed class BankDepositLedger
    {
        public string BankName = string.Empty;

        private readonly Dictionary<string, DepositAccount> accounts =
            new Dictionary<string, DepositAccount>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        /// <summary>Liquid cash the bank actually holds.</summary>
        public int CashOnHandCents { get; private set; }

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyCollection<DepositAccount> Accounts => accounts.Values;

        public BankDepositLedger() { }
        public BankDepositLedger(string bankName) { BankName = bankName ?? string.Empty; }

        /// <summary>What the bank owes its depositors — the liability side.</summary>
        public int DepositsOwedCents()
        {
            int total = 0;
            foreach (DepositAccount account in accounts.Values)
                total += account.BalanceCents + account.InterestAccruedCents;
            return total;
        }

        public DepositAccount OpenAccount(
            string accountId, string depositorName, DepositKind kind,
            int openingDepositCents, int dayIndex,
            int termDays = 0, int interestRateBps = 0, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(accountId))
            {
                diag.Add("BankDepositLedger.OpenAccount: an account id is required.");
                return null;
            }
            if (accounts.ContainsKey(accountId))
            {
                diag.Add($"BankDepositLedger.OpenAccount: account '{accountId}' already exists.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(depositorName))
            {
                diag.Add("BankDepositLedger.OpenAccount: the depositor must be named — no anonymous deposits.");
                return null;
            }
            if (kind == DepositKind.Unspecified)
            {
                diag.Add("BankDepositLedger.OpenAccount: demand or term must be stated.");
                return null;
            }
            if (openingDepositCents <= 0)
            {
                diag.Add("BankDepositLedger.OpenAccount: the opening deposit must be positive.");
                return null;
            }
            if (kind == DepositKind.Term && termDays <= 0)
            {
                diag.Add("BankDepositLedger.OpenAccount: a term deposit needs a real term.");
                return null;
            }
            var account = new DepositAccount
            {
                AccountId = accountId,
                DepositorName = depositorName,
                Kind = kind,
                BalanceCents = openingDepositCents,
                OpenedDayIndex = dayIndex,
                TermDays = termDays,
                MaturityDayIndex = dayIndex + termDays,
                InterestRateBps = interestRateBps,
            };
            accounts[accountId] = account;
            CashOnHandCents += openingDepositCents;
            diag.Add($"BankDepositLedger [{BankName}]: {kind} account '{accountId}' opened for '{depositorName}' — " +
                $"{openingDepositCents}c in, cash on hand now {CashOnHandCents}c.");
            return account;
        }

        /// <summary>A depositor adds funds — cash in, liability up.</summary>
        public string Deposit(string accountId, int amountCents, int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (!accounts.TryGetValue(accountId, out DepositAccount account))
                return $"BankDepositLedger.Deposit: unknown account '{accountId}'.";
            if (amountCents <= 0) return "BankDepositLedger.Deposit: the deposit must be positive.";
            account.BalanceCents += amountCents;
            CashOnHandCents += amountCents;
            diag.Add($"BankDepositLedger [{BankName}]: '{accountId}' +{amountCents}c (day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// A depositor withdraws. Demand: anytime, if the bank has the cash.
        /// Term: at/after maturity, or early with the forfeit stated in terms
        /// (calibration: full accrued interest forfeited). When the bank lacks
        /// the cash, the withdrawal is REFUSED — never faked (Canon §18.10).
        /// </summary>
        public string Withdraw(string accountId, int amountCents, int dayIndex,
            bool earlyTermWithdrawalAccepted, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (!accounts.TryGetValue(accountId, out DepositAccount account))
                return $"BankDepositLedger.Withdraw: unknown account '{accountId}'.";
            if (amountCents <= 0) return "BankDepositLedger.Withdraw: the withdrawal must be positive.";
            if (amountCents > account.BalanceCents)
                return $"BankDepositLedger.Withdraw: '{accountId}' holds {account.BalanceCents}c — cannot withdraw {amountCents}c.";
            if (account.Kind == DepositKind.Term && dayIndex < account.MaturityDayIndex && !earlyTermWithdrawalAccepted)
                return $"BankDepositLedger.Withdraw: term account '{accountId}' matures day {account.MaturityDayIndex} — early withdrawal refused.";
            if (amountCents > CashOnHandCents)
            {
                diag.Add($"BankDepositLedger [{BankName}]: WITHDRAWAL REFUSED — '{account.DepositorName}' asked for {amountCents}c " +
                    $"but the bank holds {CashOnHandCents}c cash. A run is an acceleration of a real liability (Canon §18.10).");
                return $"BankDepositLedger.Withdraw: the bank cannot cover {amountCents}c (cash on hand {CashOnHandCents}c) — refused, not faked.";
            }
            if (account.Kind == DepositKind.Term && dayIndex < account.MaturityDayIndex)
            {
                diag.Add($"BankDepositLedger [{BankName}]: early withdrawal from '{accountId}' — accrued interest {account.InterestAccruedCents}c forfeited per terms.");
                account.InterestAccruedCents = 0;
            }
            account.BalanceCents -= amountCents;
            CashOnHandCents -= amountCents;
            diag.Add($"BankDepositLedger [{BankName}]: '{accountId}' -{amountCents}c (day {dayIndex}) — cash on hand now {CashOnHandCents}c.");
            return null;
        }

        /// <summary>
        /// The bank lends from its cash — cash out, liability unchanged. This
        /// is the honest version of the §18.9 maturity-mismatch decision: every
        /// loan deepens the gap between cash and deposits owed.
        /// </summary>
        public string DisburseLoan(int amountCents, string loanReference, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (amountCents <= 0) return "BankDepositLedger.DisburseLoan: the loan must be positive.";
            if (string.IsNullOrWhiteSpace(loanReference))
                return "BankDepositLedger.DisburseLoan: the loan must be named.";
            if (amountCents > CashOnHandCents)
                return $"BankDepositLedger.DisburseLoan: '{loanReference}' needs {amountCents}c but the bank holds {CashOnHandCents}c — the bank cannot lend what it does not have.";
            CashOnHandCents -= amountCents;
            diag.Add($"BankDepositLedger [{BankName}]: disbursed {amountCents}c for '{loanReference}' — cash on hand now {CashOnHandCents}c against {DepositsOwedCents()}c owed.");
            return null;
        }

        /// <summary>Loan repayment received — cash back in.</summary>
        public string ReceiveLoanPayment(int amountCents, string loanReference, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (amountCents <= 0) return "BankDepositLedger.ReceiveLoanPayment: the payment must be positive.";
            CashOnHandCents += amountCents;
            diag.Add($"BankDepositLedger [{BankName}]: received {amountCents}c on '{loanReference}' — cash on hand now {CashOnHandCents}c.");
            return null;
        }

        /// <summary>
        /// Accrues one day of interest on term deposits — owed, not yet paid.
        /// Simple interest on the recorded rate; the accrual increases what the
        /// bank owes (Canon §18.8: deposits are liabilities).
        /// </summary>
        public void AccrueInterestDay(List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            foreach (DepositAccount account in accounts.Values)
            {
                if (account.Kind != DepositKind.Term || account.InterestRateBps <= 0) continue;
                int daily = (int)Math.Round(account.BalanceCents * (account.InterestRateBps / 10000.0) / 365.0);
                if (daily > 0)
                {
                    account.InterestAccruedCents += daily;
                    diag.Add($"BankDepositLedger [{BankName}]: '{account.AccountId}' accrued {daily}c interest (owed: {account.InterestAccruedCents}c).");
                }
            }
        }
    }
}
