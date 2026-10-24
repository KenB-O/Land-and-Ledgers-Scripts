using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Financing
{
    /// <summary>
    /// Pass XI WS2 (audit finding A-06, RUN-003): the adapter contract for
    /// player-owned bank ledger bindings. The problem it answers: two bank
    /// cash authorities exist in source — the live business lender's
    /// internal deposit ledger (charter capital and loan repayments only;
    /// depositor deposits never reach it) and BankRuntime's deposit ledger
    /// (where TellerOperations, DepositorConfidence and interbank flows
    /// actually post deposits, but which has zero production instances).
    /// Binding both ledgers for one bank would create a second cash
    /// authority and double-count deposits, so this registry binds at most
    /// ONE ledger per bank business instance id, fail-loud on a second
    /// distinct ledger, and idempotent on re-binding the same instance.
    ///
    /// What this contract is and is not:
    /// - It holds references only. It creates no balances, no accounts and
    ///   no debt, and it stores no cached balance: every position read
    ///   re-reads the bound ledger, so an underwriting decision that reads
    ///   through this registry always sees live deposits (the refresh is
    ///   the read itself — there is no snapshot to go stale).
    /// - It does NOT rewire BusinessLenderService: the live lender keeps
    ///   lending from its own charter ledger until a decision consolidates
    ///   the two ledgers. Wiring this registry into underwriting as a
    ///   second funding source would be an unfunded-loan bug, not a fix.
    /// - The scene hookup is explicitly outstanding (G4): a future Unity
    ///   step binds either a scene ledger object or a constructed
    ///   BankRuntime ledger for a player-owned bank, never both — the
    ///   first bind wins and the second distinct bind is refused here.
    ///   Until that step lands, this registry is a contract with tests,
    ///   not a live binding, and must never be described as wired.
    /// </summary>
    public sealed class BankLedgerBindingRegistry
    {
        private readonly Dictionary<string, BankDepositLedger> bound =
            new Dictionary<string, BankDepositLedger>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Binds a deposit ledger as the single authoritative ledger for a
        /// bank business. Returns null on success, a refusal otherwise.
        /// A second, different ledger for the same business is refused
        /// loudly; re-binding the identical instance is a no-op.
        /// </summary>
        public string BindBankLedger(string businessInstanceId, BankDepositLedger ledger, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(businessInstanceId))
                return "BankLedgerBindingRegistry.BindBankLedger: a bank business instance id is required.";
            if (ledger == null)
                return $"BankLedgerBindingRegistry.BindBankLedger: no ledger supplied for '{businessInstanceId}' — a bank without a ledger binds nothing.";
            if (bound.TryGetValue(businessInstanceId, out BankDepositLedger existing))
            {
                if (ReferenceEquals(existing, ledger))
                {
                    diag.Add($"BankLedgerBindingRegistry: '{businessInstanceId}' already bound to this ledger — rebind is a no-op.");
                    return null;
                }
                return $"BankLedgerBindingRegistry.BindBankLedger: '{businessInstanceId}' is already bound to a different ledger " +
                    $"('{existing.BankName}') — a second ledger would double-count deposits. Refusing; the first binding stands.";
            }
            bound[businessInstanceId] = ledger;
            diag.Add($"BankLedgerBindingRegistry: '{businessInstanceId}' bound to ledger '{ledger.BankName}'.");
            return null;
        }

        /// <summary>Returns the bound ledger for a bank business, or null when nothing is bound.</summary>
        public BankDepositLedger FindLedger(string businessInstanceId)
        {
            if (string.IsNullOrWhiteSpace(businessInstanceId)) return null;
            bound.TryGetValue(businessInstanceId, out BankDepositLedger ledger);
            return ledger;
        }

        /// <summary>
        /// Reads a bound bank's live deposit position straight from its
        /// ledger: cash actually held, deposits actually owed, and the
        /// difference. No figure is cached here, so a decision that calls
        /// this immediately before underwriting reads live deposits.
        /// Returns false (with zeros) when no ledger is bound.
        /// </summary>
        public bool TryReadDepositPosition(string businessInstanceId,
            out int cashOnHandCents, out int depositsOwedCents, out int netCents)
        {
            cashOnHandCents = 0;
            depositsOwedCents = 0;
            netCents = 0;
            BankDepositLedger ledger = FindLedger(businessInstanceId);
            if (ledger == null) return false;
            cashOnHandCents = Math.Max(0, ledger.CashOnHandCents);
            depositsOwedCents = Math.Max(0, ledger.DepositsOwedCents());
            netCents = cashOnHandCents - depositsOwedCents;
            return true;
        }
    }
}
