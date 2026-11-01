using System;
using System.Collections.Generic;
using LandLedgers.Population;
using UnityEngine;

namespace LandLedgers.Economy.Financing
{
    /// <summary>
    /// Phase E (E1): a real cash store behind a <see cref="CreditCashAccount"/>.
    /// Credit flows never invent money: every advance, payment and settlement
    /// debits one real store and credits another, cent for cent.
    /// </summary>
    public interface IRealCashStore
    {
        string OwnerName { get; }
        int ReadBalanceCents();
        /// <summary>Returns null on success, a refusal reason on failure.</summary>
        string DebitCents(int dayIndex, int amountCents, string purpose, string counterparty);
        /// <summary>Returns null on success, a refusal reason on failure.</summary>
        string CreditCents(int dayIndex, int amountCents, string sourceClass,
            string sourceReference, string reason, string counterparty);
    }

    /// <summary>
    /// Phase E: a household's real cash, seen through its
    /// <see cref="HouseholdLedger"/>. Outflows enforce the ledger's own
    /// insufficient-funds rule; inflows carry a real source class (Canon 13.2).
    /// </summary>
    public sealed class HouseholdCashStore : IRealCashStore
    {
        private readonly HouseholdLedger ledger;
        private readonly int householdId;

        public HouseholdCashStore(int householdId, HouseholdLedger ledger)
        {
            this.householdId = householdId;
            this.ledger = ledger;
        }

        public string OwnerName => "household:" + householdId;

        public int ReadBalanceCents() => ledger != null ? ledger.GetBalanceCents() : 0;

        public string DebitCents(int dayIndex, int amountCents, string purpose, string counterparty)
        {
            if (ledger == null) return "HouseholdCashStore: no ledger for household " + householdId + ".";
            return ledger.RecordOutflow(dayIndex, amountCents, purpose, counterparty ?? string.Empty);
        }

        public string CreditCents(int dayIndex, int amountCents, string sourceClass,
            string sourceReference, string reason, string counterparty)
        {
            if (ledger == null) return "HouseholdCashStore: no ledger for household " + householdId + ".";
            HouseholdIncomeSource source = HouseholdIncomeSource.OtherDocumented;
            if (!string.IsNullOrWhiteSpace(sourceClass)
                && Enum.TryParse(sourceClass, out HouseholdIncomeSource parsed)
                && parsed != HouseholdIncomeSource.Unspecified)
                source = parsed;
            return ledger.RecordInflow(dayIndex, amountCents, source,
                sourceReference ?? string.Empty, reason, counterparty ?? string.Empty);
        }
    }

    /// <summary>
    /// Phase E: a named person's own purse — the spendable cash of a private
    /// individual (lender or borrower). Seeded only from a named source and
    /// never negative: the purse refuses debits it cannot cover, so an
    /// undrawn commitment or facility can never become spendable cash.
    /// </summary>
    public sealed class PurseCashStore : IRealCashStore
    {
        private readonly string ownerName;
        private int balanceCents;
        private readonly List<string> history = new List<string>();

        public PurseCashStore(string ownerName, int openingBalanceCents, string openingSource)
        {
            this.ownerName = string.IsNullOrWhiteSpace(ownerName) ? "unnamed-purse" : ownerName;
            if (openingBalanceCents < 0)
                throw new ArgumentOutOfRangeException(nameof(openingBalanceCents));
            if (openingBalanceCents > 0 && string.IsNullOrWhiteSpace(openingSource))
                throw new ArgumentException("An opening purse balance must name its source — cash is never conjured.",
                    nameof(openingSource));
            balanceCents = openingBalanceCents;
            if (openingBalanceCents > 0)
                history.Add($"opened with {openingBalanceCents}c ({openingSource}).");
        }

        public string OwnerName => ownerName;
        public IReadOnlyList<string> History => history;

        public int ReadBalanceCents() => balanceCents;

        public string DebitCents(int dayIndex, int amountCents, string purpose, string counterparty)
        {
            if (amountCents <= 0) return "PurseCashStore: debit amount must be positive.";
            if (amountCents > balanceCents)
                return $"PurseCashStore [{ownerName}]: insufficient funds — {amountCents}c requested, {balanceCents}c held. Nothing moved.";
            balanceCents -= amountCents;
            history.Add($"day {dayIndex}: -{amountCents}c ({purpose}) to '{counterparty}'. Balance {balanceCents}c.");
            return null;
        }

        public string CreditCents(int dayIndex, int amountCents, string sourceClass,
            string sourceReference, string reason, string counterparty)
        {
            if (amountCents <= 0) return "PurseCashStore: credit amount must be positive.";
            balanceCents += amountCents;
            history.Add($"day {dayIndex}: +{amountCents}c [{sourceClass}] {reason} from '{counterparty}'. Balance {balanceCents}c.");
            return null;
        }
    }

    /// <summary>
    /// Phase E (E1): binds <see cref="CreditCashAccount"/> windows to real
    /// cash stores. The workflow still receives the account type it expects,
    /// but the balance is synchronized from the real store before each
    /// operation and the resulting delta is pushed back through the store's
    /// own debit/credit path — never a raw field write.
    /// </summary>
    public sealed class CreditCashBridge
    {
        private readonly Dictionary<string, IRealCashStore> stores =
            new Dictionary<string, IRealCashStore>(StringComparer.Ordinal);
        private readonly Dictionary<CreditCashAccount, BoundWindow> openWindows =
            new Dictionary<CreditCashAccount, BoundWindow>();

        private sealed class BoundWindow
        {
            public IRealCashStore Store;
            public int StartBalanceCents;
        }

        public string Register(string owner, IRealCashStore store)
        {
            if (string.IsNullOrWhiteSpace(owner)) return "CreditCashBridge: the owner must be named.";
            if (store == null) return "CreditCashBridge: a real cash store is required.";
            stores[owner] = store;
            return null;
        }

        public IRealCashStore FindStore(string owner)
        {
            return !string.IsNullOrWhiteSpace(owner) && stores.TryGetValue(owner, out IRealCashStore store)
                ? store : null;
        }

        /// <summary>
        /// Opens a workflow-compatible account window whose balance mirrors
        /// the real store right now. The caller must CommitWindow afterwards.
        /// </summary>
        public CreditCashAccount OpenWindow(string owner, List<string> diag)
        {
            IRealCashStore store = FindStore(owner);
            if (store == null)
            {
                diag?.Add($"CreditCashBridge: no real cash store is registered for '{owner}' — refusing to invent a balance.");
                return null;
            }
            var account = new CreditCashAccount(owner, store.ReadBalanceCents());
            openWindows[account] = new BoundWindow { Store = store, StartBalanceCents = account.BalanceCents };
            return account;
        }

        /// <summary>
        /// Pushes the window's delta through the real store. Positive delta is
        /// credited, negative delta debited; a refused store write fails the
        /// commit loudly and the window stays open for inspection.
        /// </summary>
        public string CommitWindow(CreditCashAccount window, int dayIndex, string purpose,
            string counterparty, List<string> diag)
        {
            if (window == null) return "CreditCashBridge: no window to commit.";
            if (!openWindows.TryGetValue(window, out BoundWindow bound))
                return "CreditCashBridge: this window was not opened by the bridge — refusing to guess its origin.";
            int delta = window.BalanceCents - bound.StartBalanceCents;
            if (delta == 0)
            {
                openWindows.Remove(window);
                return null;
            }
            string problem = delta > 0
                ? bound.Store.CreditCents(dayIndex, delta, "CreditSettlement", window.Owner,
                    purpose ?? "credit settlement", counterparty ?? string.Empty)
                : bound.Store.DebitCents(dayIndex, -delta,
                    purpose ?? "credit settlement", counterparty ?? string.Empty);
            if (problem != null)
            {
                diag?.Add($"CreditCashBridge: committing {delta}c for '{window.Owner}' was REFUSED by the real store: {problem}");
                return problem;
            }
            diag?.Add($"CreditCashBridge: committed {delta}c for '{window.Owner}' through the real store ({purpose}).");
            openWindows.Remove(window);
            return null;
        }

        /// <summary>
        /// Abandons a window without pushing anything — used when the workflow
        /// refused the operation and no cash may move.
        /// </summary>
        public void DiscardWindow(CreditCashAccount window)
        {
            if (window != null) openWindows.Remove(window);
        }
    }
}
