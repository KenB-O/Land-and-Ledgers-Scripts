using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Doctor
{
    /// <summary>
    /// D1A: how a treatment charge is settled. Canon §13.3C: "Medical service
    /// can be paid at the visit, carried on an account, paid partly later,
    /// covered by an employer in selected workplace cases or supported through
    /// a specific institutional arrangement where historically justified."
    /// </summary>
    public enum DoctorPaymentMode
    {
        CashAtVisit = 0,
        Account = 1,
        EmployerArrangement = 2,
        Institutional = 3,
    }

    /// <summary>
    /// D1A: the practice's credit posture. Canon §13.3C: "A generous credit
    /// posture can improve access and reputation but create bad-debt exposure."
    /// This is a player choice, not a constant.
    /// </summary>
    public enum DoctorCreditPosture
    {
        Strict = 0,        // cash at visit only; account requests refused loudly
        AccountOnRequest = 1,
        Generous = 2,      // account freely offered; +trust, +bad-debt exposure
    }

    /// <summary>D1A: one line of a treatment invoice.</summary>
    [Serializable]
    public sealed class DoctorInvoiceLine
    {
        public string Description = string.Empty;
        public int Cents;

        public DoctorInvoiceLine() { }

        public DoctorInvoiceLine(string description, int cents)
        {
            Description = description ?? string.Empty;
            Cents = Math.Max(0, cents);
        }
    }

    /// <summary>
    /// D1A: one issued treatment invoice. An unpaid invoice is a REAL
    /// receivable owed by the named household (Canon Part VI §6.4 pattern,
    /// following the LawyerPractice precedent): billing records revenue and a
    /// receivable, never cash. Collecting the receivable is cash but not a
    /// second sale — payments only shrink the balance, overpayment is refused
    /// not pocketed, and write-off erases nothing but the collectability.
    /// </summary>
    [Serializable]
    public sealed class DoctorTreatmentInvoice
    {
        public EntityId InvoiceId = EntityId.Invalid; // EntityKind.Contract (W1A precedent)
        public string BusinessInstanceId = string.Empty;
        public int IssuedDayIndex;
        public string DebtorHouseholdId = string.Empty;
        public string DebtorName = string.Empty;
        public DoctorPaymentMode PaymentMode = DoctorPaymentMode.CashAtVisit;
        public string ArrangementId = string.Empty; // employer/institutional arrangement, when applicable
        public List<DoctorInvoiceLine> Lines = new List<DoctorInvoiceLine>();
        public int PaidCents;
        public int WrittenOffCents;
        public bool IsBadDebt;

        public DoctorTreatmentInvoice() { }

        public int TotalCents
        {
            get
            {
                int total = 0;
                foreach (var line in Lines) total += Math.Max(0, line.Cents);
                return total;
            }
        }

        /// <summary>Outstanding receivable in cents; never negative.</summary>
        public int BalanceCents => Math.Max(0, TotalCents - Math.Max(0, PaidCents) - Math.Max(0, WrittenOffCents));

        public bool IsAccountReceivable =>
            (PaymentMode == DoctorPaymentMode.Account || PaymentMode == DoctorPaymentMode.EmployerArrangement
                || PaymentMode == DoctorPaymentMode.Institutional)
            && BalanceCents > 0 && !IsBadDebt;
    }

    /// <summary>
    /// D1A: the practice's receivables ledger. Canon §13.3C: "a busy doctor can
    /// hold substantial receivables while lacking cash." Money moves only
    /// through ledger authorities — this ledger records who owes what;
    /// RecordPayment requires the caller's real money alongside the call.
    /// </summary>
    public sealed class DoctorReceivablesLedger
    {
        private readonly List<DoctorTreatmentInvoice> invoices = new List<DoctorTreatmentInvoice>();
        private readonly List<string> diagnostics = new List<string>();
        private int cashCollectedCents;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<DoctorTreatmentInvoice> Invoices => invoices;
        public int CashCollectedCents => cashCollectedCents;

        /// <summary>
        /// Issues an invoice under the practice's credit posture. A Strict
        /// practice refuses Account settlement loudly (the patient pays cash
        /// or goes unbilled); Generous posture is a trust event for the caller
        /// to record. Returns a rejection string, or null on success.
        /// </summary>
        public string IssueInvoice(
            DoctorTreatmentInvoice invoice,
            DoctorCreditPosture posture,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (invoice == null) return "DoctorReceivablesLedger: null invoice — nothing issued.";
            if (invoice.TotalCents <= 0)
                return "DoctorReceivablesLedger: invoice refused — no chargeable lines.";
            if (string.IsNullOrWhiteSpace(invoice.DebtorHouseholdId))
                return "DoctorReceivablesLedger: invoice refused — the debtor household must be named.";

            if (invoice.PaymentMode == DoctorPaymentMode.Account && posture == DoctorCreditPosture.Strict)
            {
                diag.Add($"DoctorReceivablesLedger: account settlement refused for '{invoice.DebtorHouseholdId}' — strict credit posture (cash at visit).");
                return "DoctorReceivablesLedger: strict credit posture — account settlement refused; cash at visit.";
            }

            if (!invoice.InvoiceId.IsValid)
            {
                if (idRegistry == null)
                    return "DoctorReceivablesLedger: invoice refused — no id registry for the invoice id.";
                invoice.InvoiceId = idRegistry.Allocate(EntityKind.Contract);
            }

            invoices.Add(invoice);
            diag.Add($"DoctorReceivablesLedger: invoice {invoice.InvoiceId} issued to '{invoice.DebtorHouseholdId}' — {invoice.TotalCents}c ({invoice.PaymentMode}).");
            return null;
        }

        /// <summary>
        /// Records a payment against an invoice. The caller's real money moves
        /// alongside this call; the ledger only shrinks the receivable.
        /// Overpayment is refused, never pocketed.
        /// </summary>
        public string RecordPayment(EntityId invoiceId, int cents, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            DoctorTreatmentInvoice invoice = null;
            foreach (var inv in invoices)
            {
                if (inv.InvoiceId.Equals(invoiceId)) { invoice = inv; break; }
            }

            if (invoice == null)
                return $"DoctorReceivablesLedger: no invoice {invoiceId} — payment refused.";
            if (cents <= 0)
                return "DoctorReceivablesLedger: payment refused — positive amount required.";
            if (invoice.IsBadDebt)
                return $"DoctorReceivablesLedger: invoice {invoiceId} was written off — payment refused (re-open it first).";
            if (cents > invoice.BalanceCents)
                return $"DoctorReceivablesLedger: payment of {cents}c refused — only {invoice.BalanceCents}c owed; overpayment is not pocketed.";

            invoice.PaidCents += cents;
            cashCollectedCents += cents;
            diag.Add($"DoctorReceivablesLedger: {cents}c collected on invoice {invoiceId} (day {dayIndex}) — {invoice.BalanceCents}c still owed.");
            return null;
        }

        /// <summary>Total outstanding receivables across all live invoices.</summary>
        public int OutstandingReceivablesCents()
        {
            int total = 0;
            foreach (var inv in invoices)
            {
                if (inv.IsAccountReceivable) total += inv.BalanceCents;
            }

            return total;
        }

        /// <summary>
        /// Ages receivables: invoices older than maxAgeDays with a balance are
        /// written off as bad debt. Write-off erases collectability, not the
        /// record — the loss is visible for the caller to book. Returns the
        /// cents written off this pass.
        /// </summary>
        public int WriteOffAgedReceivables(int dayIndex, int maxAgeDays, List<string> diag)
        {
            diag = diag ?? diagnostics;
            int writtenOff = 0;
            foreach (var inv in invoices)
            {
                if (inv.IsBadDebt || inv.BalanceCents <= 0) continue;
                if (dayIndex - inv.IssuedDayIndex < Math.Max(1, maxAgeDays)) continue;

                inv.WrittenOffCents += inv.BalanceCents;
                inv.IsBadDebt = true;
                writtenOff += inv.BalanceCents;
                diag.Add($"DoctorReceivablesLedger: invoice {inv.InvoiceId} ({inv.BalanceCents}c from '{inv.DebtorHouseholdId}') written off as bad debt after {dayIndex - inv.IssuedDayIndex} days.");
            }

            return writtenOff;
        }

        #region Save / Load
        [Serializable]
        public sealed class DoctorReceivablesSaveDto
        {
            public List<DoctorTreatmentInvoice> Invoices = new List<DoctorTreatmentInvoice>();
            public int CashCollectedCents;
        }

        public DoctorReceivablesSaveDto CaptureSaveDto()
        {
            var dto = new DoctorReceivablesSaveDto { CashCollectedCents = cashCollectedCents };
            dto.Invoices.AddRange(invoices);
            return dto;
        }

        public void LoadFromSaveDto(DoctorReceivablesSaveDto dto)
        {
            invoices.Clear();
            cashCollectedCents = 0;
            if (dto == null) return;
            if (dto.Invoices != null) invoices.AddRange(dto.Invoices);
            cashCollectedCents = Math.Max(0, dto.CashCollectedCents);
        }
        #endregion
    }
}
