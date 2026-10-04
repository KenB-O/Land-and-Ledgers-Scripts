using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.LumberYard
{
    /// <summary>
    /// D2C: the yard's credit posture toward contractor accounts. Canon Part
    /// VI §6.4: "Credit sale can create revenue/receivable without cash."
    /// Canon §7.3 (construction & contracting): contracts may use "deposits,
    /// delivered-material payments, milestone/progress payments, completion
    /// balances or other historically appropriate terms. Exact percentages are
    /// contract/project data rather than universal Canon values." So the
    /// POSTURE is a yard policy choice and the TERMS are per-account data —
    /// neither is a canon constant.
    /// </summary>
    public enum LumberYardCreditPosture
    {
        Strict = 0,        // cash on delivery/pickup only; account requests refused loudly
        AccountOnRequest = 1,
        Generous = 2,      // account freely offered; +trust, +bad-debt exposure
    }

    /// <summary>
    /// D2C: settlement terms for one contractor account — per-account DATA
    /// (Canon §7.3: exact terms are contract/project data, never universal
    /// Canon values). DueDay is computed from the invoice's issued day.
    /// </summary>
    [Serializable]
    public sealed class LumberYardSettlementTerms
    {
        /// <summary>Days from invoice to due. Positive; data, not canon.</summary>
        public int TermsDays = 30;
        /// <summary>Credit limit in cents for account sales. Data, not canon.</summary>
        public int CreditLimitCents;

        public LumberYardSettlementTerms() { }

        public LumberYardSettlementTerms(int termsDays, int creditLimitCents)
        {
            TermsDays = Math.Max(1, termsDays);
            CreditLimitCents = Math.Max(0, creditLimitCents);
        }
    }

    /// <summary>
    /// D2C: one contractor account — a NAMED builder/construction business
    /// (BusinessType.Builder / Construction) the yard sells to on account.
    /// Anonymous accounts are refused; the debtor is always named.
    /// </summary>
    [Serializable]
    public sealed class LumberYardContractorAccount
    {
        public EntityId AccountId = EntityId.Invalid; // EntityKind.Contract — an account is an agreement
        public string ContractorBusinessId = string.Empty; // the named builder
        public string ContractorLabel = string.Empty;
        public LumberYardCreditPosture CreditPosture = LumberYardCreditPosture.Strict;
        public LumberYardSettlementTerms Terms = new LumberYardSettlementTerms();
        public int OpenedDayIndex;

        public LumberYardContractorAccount() { }
    }

    /// <summary>D2C: one line of a contractor invoice (materials, delivery charge, ...).</summary>
    [Serializable]
    public sealed class LumberYardContractorInvoiceLine
    {
        public string Description = string.Empty;
        public int Cents;

        public LumberYardContractorInvoiceLine() { }

        public LumberYardContractorInvoiceLine(string description, int cents)
        {
            Description = description ?? string.Empty;
            Cents = Math.Max(0, cents);
        }
    }

    /// <summary>
    /// D2C: one contractor invoice. An unpaid invoice is a REAL receivable
    /// owed by the named contractor (Canon Part VI §6.4 pattern, following
    /// the D1A DoctorReceivablesLedger / LawyerPractice precedent): billing
    /// records revenue and a receivable, never cash. Collecting the
    /// receivable is cash but not a second sale — payments only shrink the
    /// balance, overpayment is refused not pocketed, and write-off erases
    /// nothing but the collectability. Money itself moves only through ledger
    /// authorities; this records WHO OWES WHAT.
    /// </summary>
    [Serializable]
    public sealed class LumberYardContractorInvoice
    {
        public EntityId InvoiceId = EntityId.Invalid; // EntityKind.Contract (W1A/W4B precedent)
        public string ContractorBusinessId = string.Empty;
        public EntityId SaleId = EntityId.Invalid; // the yard sale this invoice bills, when known
        public EntityId DeliveryOrderId = EntityId.Invalid; // delivery billed on this invoice, when any
        public int IssuedDayIndex;
        public int DueDayIndex;
        public List<LumberYardContractorInvoiceLine> Lines = new List<LumberYardContractorInvoiceLine>();
        public int PaidCents;
        public int WrittenOffCents;
        public bool IsBadDebt;

        public LumberYardContractorInvoice() { }

        public int TotalCents
        {
            get
            {
                int total = 0;
                foreach (var line in Lines)
                {
                    if (line == null) continue;
                    total += Math.Max(0, line.Cents);
                }
                return total;
            }
        }

        /// <summary>Outstanding receivable in cents; never negative.</summary>
        public int BalanceCents => Math.Max(0, TotalCents - Math.Max(0, PaidCents) - Math.Max(0, WrittenOffCents));

        public bool IsAccountReceivable => BalanceCents > 0 && !IsBadDebt;
    }

    /// <summary>
    /// D2C: the yard's contractor receivables ledger. Follows the D1A
    /// DoctorReceivablesLedger precedent for trade credit (PlayerDebtManager
    /// is the BANK-loan authority — the wrong instrument for trade
    /// receivables; it is deliberately NOT used here). Money moves only
    /// through ledger authorities — this ledger records who owes what;
    /// RecordPayment requires the caller's real money alongside the call and
    /// only ever shrinks a balance.
    /// </summary>
    public sealed class LumberYardContractorLedger
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<LumberYardContractorAccount> accounts = new List<LumberYardContractorAccount>();
        private readonly List<LumberYardContractorInvoice> invoices = new List<LumberYardContractorInvoice>();
        private readonly string yardBusinessId;
        private int cashCollectedCents;

        public LumberYardContractorLedger(string yardBusinessId)
        {
            this.yardBusinessId = yardBusinessId ?? string.Empty;
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<LumberYardContractorAccount> Accounts => accounts;
        public IReadOnlyList<LumberYardContractorInvoice> Invoices => invoices;
        public int CashCollectedCents => cashCollectedCents;
        public string YardBusinessId => yardBusinessId;

        /// <summary>
        /// Opens a contractor account for a NAMED builder. Anonymous debtors
        /// are refused. Returns the refusal, or null.
        /// </summary>
        public string OpenAccount(
            LumberYardContractorAccount account,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (account == null) return "LumberYardContractorLedger: null account — nothing opened.";
            if (string.IsNullOrWhiteSpace(account.ContractorBusinessId))
                return "LumberYardContractorLedger: the contractor must be named — anonymous accounts refused.";
            foreach (var a in accounts)
            {
                if (a != null && string.Equals(a.ContractorBusinessId, account.ContractorBusinessId.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                    return $"LumberYardContractorLedger: contractor '{account.ContractorBusinessId.Trim()}' already has an account.";
            }
            if (!account.AccountId.IsValid)
            {
                if (idRegistry == null)
                    return "LumberYardContractorLedger: account refused — no id registry for the account id.";
                account.AccountId = idRegistry.Allocate(EntityKind.Contract);
            }
            account.ContractorBusinessId = account.ContractorBusinessId.Trim();
            accounts.Add(account);
            diag.Add($"LumberYardContractorLedger ({yardBusinessId}): account {account.AccountId} opened for contractor "
                + $"'{account.ContractorBusinessId}' — posture {account.CreditPosture}, terms {account.Terms.TermsDays} days, "
                + $"limit {account.Terms.CreditLimitCents}c.");
            return null;
        }

        public LumberYardContractorAccount FindAccount(string contractorBusinessId)
        {
            if (string.IsNullOrWhiteSpace(contractorBusinessId)) return null;
            foreach (var a in accounts)
            {
                if (a != null && string.Equals(a.ContractorBusinessId, contractorBusinessId.Trim(),
                    StringComparison.OrdinalIgnoreCase)) return a;
            }
            return null;
        }

        /// <summary>
        /// Issues an invoice against a contractor account under its credit
        /// posture. A Strict account refuses account billing loudly (the
        /// contractor pays cash or goes unbilled). Account sales beyond the
        /// credit limit are refused loudly — the yard never invents money.
        /// Returns the refusal, or null on success.
        /// </summary>
        public string IssueInvoice(
            LumberYardContractorInvoice invoice,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (invoice == null) return "LumberYardContractorLedger: null invoice — nothing issued.";
            if (invoice.TotalCents <= 0)
                return "LumberYardContractorLedger: invoice refused — no chargeable lines.";

            var account = FindAccount(invoice.ContractorBusinessId);
            if (account == null)
                return $"LumberYardContractorLedger: no account for contractor '{invoice.ContractorBusinessId}' — open one first.";
            if (account.CreditPosture == LumberYardCreditPosture.Strict)
            {
                diag.Add($"LumberYardContractorLedger ({yardBusinessId}): account billing refused for "
                    + $"'{invoice.ContractorBusinessId}' — strict credit posture (cash on delivery/pickup).");
                return "LumberYardContractorLedger: strict credit posture — account billing refused; cash terms.";
            }
            int outstanding = OutstandingForContractor(invoice.ContractorBusinessId);
            if (outstanding + invoice.TotalCents > Math.Max(0, account.Terms.CreditLimitCents))
            {
                diag.Add($"LumberYardContractorLedger ({yardBusinessId}): invoice refused for "
                    + $"'{invoice.ContractorBusinessId}' — over credit limit "
                    + $"(outstanding {outstanding}c + {invoice.TotalCents}c > limit {account.Terms.CreditLimitCents}c).");
                return "LumberYardContractorLedger: invoice refused — over the account's credit limit.";
            }

            if (!invoice.InvoiceId.IsValid)
            {
                if (idRegistry == null)
                    return "LumberYardContractorLedger: invoice refused — no id registry for the invoice id.";
                invoice.InvoiceId = idRegistry.Allocate(EntityKind.Contract);
            }
            invoice.DueDayIndex = invoice.IssuedDayIndex + Math.Max(1, account.Terms.TermsDays);

            invoices.Add(invoice);
            diag.Add($"LumberYardContractorLedger ({yardBusinessId}): invoice {invoice.InvoiceId} issued to "
                + $"'{invoice.ContractorBusinessId}' — {invoice.TotalCents}c, due day {invoice.DueDayIndex}. "
                + "A receivable, not cash; the ledger authority posts it.");
            return null;
        }

        /// <summary>Outstanding receivable for one contractor, cents.</summary>
        public int OutstandingForContractor(string contractorBusinessId)
        {
            int total = 0;
            foreach (var inv in invoices)
            {
                if (inv == null) continue;
                if (!string.Equals(inv.ContractorBusinessId, contractorBusinessId?.Trim(),
                    StringComparison.OrdinalIgnoreCase)) continue;
                if (inv.IsAccountReceivable) total += inv.BalanceCents;
            }
            return total;
        }

        /// <summary>Total outstanding receivables across all live invoices.</summary>
        public int OutstandingReceivablesCents()
        {
            int total = 0;
            foreach (var inv in invoices)
            {
                if (inv == null) continue;
                if (inv.IsAccountReceivable) total += inv.BalanceCents;
            }
            return total;
        }

        /// <summary>
        /// Records a payment against an invoice. The caller's real money moves
        /// alongside this call; the ledger only shrinks the receivable.
        /// Overpayment is refused, never pocketed. Returns the refusal, or
        /// null.
        /// </summary>
        public string RecordPayment(EntityId invoiceId, int cents, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            LumberYardContractorInvoice invoice = null;
            foreach (var inv in invoices)
            {
                if (inv != null && inv.InvoiceId.Equals(invoiceId)) { invoice = inv; break; }
            }
            if (invoice == null)
                return $"LumberYardContractorLedger: no invoice {invoiceId} — payment refused.";
            if (cents <= 0)
                return "LumberYardContractorLedger: payment refused — positive amount required.";
            if (invoice.IsBadDebt)
                return $"LumberYardContractorLedger: invoice {invoiceId} was written off — payment refused (re-open it first).";
            if (cents > invoice.BalanceCents)
                return $"LumberYardContractorLedger: payment of {cents}c refused — only {invoice.BalanceCents}c owed; overpayment is not pocketed.";

            invoice.PaidCents += cents;
            cashCollectedCents += cents;
            diag.Add($"LumberYardContractorLedger ({yardBusinessId}): {cents}c collected on invoice {invoiceId} (day {dayIndex}) — "
                + $"{invoice.BalanceCents}c still owed by '{invoice.ContractorBusinessId}'.");
            return null;
        }

        /// <summary>
        /// Ages receivables: invoices older than maxAgeDays with a balance are
        /// written off as bad debt. Write-off erases collectability, not the
        /// record — the loss stays visible for the caller to book. Returns the
        /// cents written off this pass.
        /// </summary>
        public int WriteOffAgedReceivables(int dayIndex, int maxAgeDays, List<string> diag)
        {
            diag = diag ?? diagnostics;
            int writtenOff = 0;
            foreach (var inv in invoices)
            {
                if (inv == null || inv.IsBadDebt || inv.BalanceCents <= 0) continue;
                if (dayIndex - inv.IssuedDayIndex < Math.Max(1, maxAgeDays)) continue;

                inv.WrittenOffCents += inv.BalanceCents;
                inv.IsBadDebt = true;
                writtenOff += inv.BalanceCents;
                diag.Add($"LumberYardContractorLedger ({yardBusinessId}): invoice {inv.InvoiceId} "
                    + $"({inv.BalanceCents}c from '{inv.ContractorBusinessId}') written off as bad debt after "
                    + $"{dayIndex - inv.IssuedDayIndex} days. The record remains; collectability is gone.");
            }
            return writtenOff;
        }

        /// <summary>D2C save contract: lives inside the owning ledger class.</summary>
        [Serializable]
        public sealed class LumberYardContractorLedgerSaveDto
        {
            public List<LumberYardContractorAccount> Accounts = new List<LumberYardContractorAccount>();
            public List<LumberYardContractorInvoice> Invoices = new List<LumberYardContractorInvoice>();
            public int CashCollectedCents;
        }

        public LumberYardContractorLedgerSaveDto CaptureSaveDto()
        {
            var dto = new LumberYardContractorLedgerSaveDto { CashCollectedCents = cashCollectedCents };
            foreach (var a in accounts)
            {
                if (a == null) continue;
                dto.Accounts.Add(new LumberYardContractorAccount
                {
                    AccountId = a.AccountId,
                    ContractorBusinessId = a.ContractorBusinessId,
                    ContractorLabel = a.ContractorLabel,
                    CreditPosture = a.CreditPosture,
                    Terms = new LumberYardSettlementTerms(a.Terms.TermsDays, a.Terms.CreditLimitCents),
                    OpenedDayIndex = a.OpenedDayIndex,
                });
            }
            foreach (var inv in invoices)
            {
                if (inv == null) continue;
                var clone = new LumberYardContractorInvoice
                {
                    InvoiceId = inv.InvoiceId,
                    ContractorBusinessId = inv.ContractorBusinessId,
                    SaleId = inv.SaleId,
                    DeliveryOrderId = inv.DeliveryOrderId,
                    IssuedDayIndex = inv.IssuedDayIndex,
                    DueDayIndex = inv.DueDayIndex,
                    PaidCents = inv.PaidCents,
                    WrittenOffCents = inv.WrittenOffCents,
                    IsBadDebt = inv.IsBadDebt,
                };
                foreach (var line in inv.Lines)
                {
                    if (line == null) continue;
                    clone.Lines.Add(new LumberYardContractorInvoiceLine(line.Description, line.Cents));
                }
                dto.Invoices.Add(clone);
            }
            return dto;
        }

        public void LoadFromSaveDto(LumberYardContractorLedgerSaveDto dto)
        {
            accounts.Clear();
            invoices.Clear();
            cashCollectedCents = 0;
            if (dto == null) return;
            if (dto.Accounts != null) accounts.AddRange(dto.Accounts);
            if (dto.Invoices != null) invoices.AddRange(dto.Invoices);
            cashCollectedCents = Math.Max(0, dto.CashCollectedCents);
        }
    }
}
