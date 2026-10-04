using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Wheelwright
{
    /// <summary>
    /// D3A: the shop's customer account book. Canon §7.2E: "Repair trades can
    /// use the shared commercial-credit system. Farms, ranches, freight
    /// companies and mines may hold running accounts where historically
    /// appropriate rather than paying cash for every shoeing or small repair."
    /// This is BOOK credit (a running tab), not a formal credit instrument —
    /// it follows the LumberYardContractorCredit pattern (D2C), not the
    /// CreditRegistry's promissory notes: posture is a shop policy choice and
    /// terms are per-account data, never canon constants.
    ///
    /// Operator flow: complete the repair/build with a null customer ledger
    /// (revenue still books in the runtime), then ChargeToAccount for the
    /// agreed price. RecordAccountPayment clears the tab when cash arrives.
    /// </summary>
    public enum WheelwrightCreditPosture
    {
        Strict = 0,          // cash on completion only; account charges refused loudly
        AccountOnRequest = 1,
        Generous = 2,        // account freely offered; +trust, +bad-debt exposure
    }

    /// <summary>D3A: settlement terms for one customer account — per-account DATA, never canon.</summary>
    [Serializable]
    public sealed class WheelwrightSettlementTerms
    {
        /// <summary>Days from charge to due. Positive; data, not canon.</summary>
        public int TermsDays = 30;
        /// <summary>Credit limit in cents for account charges. Data, not canon.</summary>
        public int CreditLimitCents;

        public WheelwrightSettlementTerms() { }

        public WheelwrightSettlementTerms(int termsDays, int creditLimitCents)
        {
            TermsDays = Math.Max(1, termsDays);
            CreditLimitCents = Math.Max(0, creditLimitCents);
        }
    }

    /// <summary>
    /// D3A: one customer account — a NAMED farm, freight company, ranch or
    /// mine the shop does repeat work for. Anonymous accounts are refused;
    /// the debtor is always named.
    /// </summary>
    [Serializable]
    public sealed class WheelwrightCustomerAccount
    {
        public string AccountId = string.Empty;          // ACCT-0001
        public string CustomerBusinessId = string.Empty; // the named debtor
        public string CustomerLabel = string.Empty;
        public WheelwrightCreditPosture Posture = WheelwrightCreditPosture.Strict;
        public WheelwrightSettlementTerms Terms = new WheelwrightSettlementTerms();
        public int OpenedDayIndex;
        public int BalanceCents;                          // what the customer OWES the shop
        public int LastChargeDayIndex = -1;

        public WheelwrightCustomerAccount() { }
    }

    /// <summary>D3A: the account book — open, charge, pay, and find the overdue tabs.</summary>
    public sealed class WheelwrightAccountBook
    {
        private readonly Dictionary<string, WheelwrightCustomerAccount> accounts =
            new Dictionary<string, WheelwrightCustomerAccount>(StringComparer.Ordinal);
        private int nextNumber = 1;

        public IReadOnlyDictionary<string, WheelwrightCustomerAccount> Accounts => accounts;

        public WheelwrightCustomerAccount FindByCustomer(string customerBusinessId)
        {
            if (string.IsNullOrWhiteSpace(customerBusinessId)) return null;
            accounts.TryGetValue(customerBusinessId, out WheelwrightCustomerAccount account);
            return account;
        }

        public WheelwrightCustomerAccount OpenAccount(string customerBusinessId, string customerLabel,
            WheelwrightCreditPosture posture, WheelwrightSettlementTerms terms,
            int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (string.IsNullOrWhiteSpace(customerBusinessId))
            {
                diagnostics.Add("WheelwrightAccountBook: customer business id is required — no anonymous accounts.");
                return null;
            }
            if (accounts.ContainsKey(customerBusinessId))
            {
                diagnostics.Add($"WheelwrightAccountBook: {customerBusinessId} already has an account.");
                return accounts[customerBusinessId];
            }
            var account = new WheelwrightCustomerAccount
            {
                AccountId = $"ACCT-{nextNumber++:D4}",
                CustomerBusinessId = customerBusinessId,
                CustomerLabel = string.IsNullOrWhiteSpace(customerLabel) ? customerBusinessId : customerLabel,
                Posture = posture,
                Terms = terms ?? new WheelwrightSettlementTerms(),
                OpenedDayIndex = dayIndex,
            };
            accounts[customerBusinessId] = account;
            diagnostics.Add(
                $"WheelwrightAccountBook: opened {account.AccountId} for {account.CustomerLabel} " +
                $"({posture}, {account.Terms.TermsDays}d terms, {account.Terms.CreditLimitCents}c limit).");
            return account;
        }

        /// <summary>
        /// Puts a completed job's price on the customer's tab. Refuses loudly:
        /// unknown customer, Strict posture, or a charge that would breach the
        /// credit limit — no silent over-extension (Canon §7.2E: reserved
        /// capacity and agreed pricing are negotiated, not assumed).
        /// </summary>
        public string ChargeToAccount(string customerBusinessId, int amountCents,
            string memo, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            WheelwrightCustomerAccount account = FindByCustomer(customerBusinessId);
            if (account == null)
                return $"WheelwrightAccountBook: no account for '{customerBusinessId}' — open one first.";
            if (amountCents <= 0)
                return "WheelwrightAccountBook: charge amount must be positive.";
            if (account.Posture == WheelwrightCreditPosture.Strict)
                return $"WheelwrightAccountBook: {account.CustomerLabel} is Strict — cash on completion, no tab.";
            if (account.BalanceCents + amountCents > account.Terms.CreditLimitCents)
                return $"WheelwrightAccountBook: {amountCents}c would breach {account.CustomerLabel}'s " +
                       $"{account.Terms.CreditLimitCents}c limit (owes {account.BalanceCents}c) — refused.";
            account.BalanceCents += amountCents;
            account.LastChargeDayIndex = dayIndex;
            diagnostics.Add(
                $"WheelwrightAccountBook: charged {amountCents}c to {account.CustomerLabel} " +
                $"({memo}) — tab now {account.BalanceCents}c.");
            return null;
        }

        /// <summary>Cash against the tab. Overpayment is refused — the shop is not a bank.</summary>
        public string RecordAccountPayment(string customerBusinessId, int amountCents,
            int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            WheelwrightCustomerAccount account = FindByCustomer(customerBusinessId);
            if (account == null)
                return $"WheelwrightAccountBook: no account for '{customerBusinessId}'.";
            if (amountCents <= 0)
                return "WheelwrightAccountBook: payment amount must be positive.";
            if (amountCents > account.BalanceCents)
                return $"WheelwrightAccountBook: {amountCents}c overpays {account.CustomerLabel}'s " +
                       $"{account.BalanceCents}c tab — refused.";
            account.BalanceCents -= amountCents;
            diagnostics.Add(
                $"WheelwrightAccountBook: {account.CustomerLabel} paid {amountCents}c (day {dayIndex}) — " +
                $"tab now {account.BalanceCents}c.");
            return null;
        }

        /// <summary>Tabs with an unpaid balance past their terms — the operator's collection list.</summary>
        public List<WheelwrightCustomerAccount> OverdueAccounts(int asOfDayIndex)
        {
            var overdue = new List<WheelwrightCustomerAccount>();
            foreach (var kv in accounts)
            {
                WheelwrightCustomerAccount a = kv.Value;
                if (a.BalanceCents <= 0) continue;
                if (a.LastChargeDayIndex < 0) continue;
                if (asOfDayIndex - a.LastChargeDayIndex > Math.Max(1, a.Terms.TermsDays))
                    overdue.Add(a);
            }
            return overdue;
        }

        /// <summary>Every cent customers owe the shop right now.</summary>
        public int TotalReceivablesCents()
        {
            int total = 0;
            foreach (var kv in accounts)
                total += Math.Max(0, kv.Value.BalanceCents);
            return total;
        }

        // ---------- save DTO (inside the owning book class) ----------

        [Serializable]
        public sealed class AccountBookDto
        {
            public List<WheelwrightCustomerAccount> Accounts = new List<WheelwrightCustomerAccount>();
            public int NextAccountNumber = 1;
        }

        public AccountBookDto ToSaveDto()
        {
            var dto = new AccountBookDto { NextAccountNumber = nextNumber };
            foreach (var kv in accounts)
                dto.Accounts.Add(kv.Value);
            return dto;
        }

        public void LoadFromSaveDto(AccountBookDto dto)
        {
            accounts.Clear();
            if (dto == null) { nextNumber = 1; return; }
            foreach (WheelwrightCustomerAccount a in dto.Accounts)
            {
                if (a == null || string.IsNullOrWhiteSpace(a.CustomerBusinessId)) continue;
                accounts[a.CustomerBusinessId] = a;
            }
            nextNumber = Math.Max(1, dto.NextAccountNumber);
        }
    }
}
