using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Doctor;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1A: fee settlement — Canon §13.3C: paid at the visit, carried on an
    /// account, paid partly later, covered by an employer, or supported by an
    /// institution. Unpaid invoices are real receivables; a busy doctor can
    /// hold substantial receivables while lacking cash.
    /// </summary>
    [TestFixture]
    public sealed class DoctorSettlementTests
    {
        private static DoctorTreatmentInvoice NewInvoice(DoctorPaymentMode mode)
        {
            return new DoctorTreatmentInvoice
            {
                BusinessInstanceId = "doc-biz-1",
                IssuedDayIndex = 300,
                DebtorHouseholdId = "household-7",
                DebtorName = "Miller household",
                PaymentMode = mode,
                Lines = new List<DoctorInvoiceLine>
                {
                    new DoctorInvoiceLine("house call out", 250),
                    new DoctorInvoiceLine("bedside treatment", 100),
                },
            };
        }

        [Test]
        public void IssueInvoice_StrictPosture_RefusesAccount()
        {
            var ledger = new DoctorReceivablesLedger();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            string rejection = ledger.IssueInvoice(NewInvoice(DoctorPaymentMode.Account), DoctorCreditPosture.Strict, registry, diag);

            Assert.NotNull(rejection, "Strict credit posture: cash at visit, no account.");
            Assert.AreEqual(0, ledger.Invoices.Count);
            Assert.IsTrue(diag.Count > 0, "The refusal must be loud.");
        }

        [Test]
        public void IssueInvoice_GenerousPosture_AllowsAccount()
        {
            var ledger = new DoctorReceivablesLedger();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            string rejection = ledger.IssueInvoice(NewInvoice(DoctorPaymentMode.Account), DoctorCreditPosture.Generous, registry, diag);

            Assert.Null(rejection);
            Assert.AreEqual(1, ledger.Invoices.Count);
            Assert.AreEqual(350, ledger.Invoices[0].TotalCents);
            Assert.AreEqual(350, ledger.Invoices[0].BalanceCents);
            Assert.IsTrue(ledger.Invoices[0].InvoiceId.IsValid, "Invoice ids are allocated, never invented.");
            Assert.AreEqual(350, ledger.OutstandingReceivablesCents(),
                "A busy doctor can hold substantial receivables while lacking cash (Canon §13.3C).");
        }

        [Test]
        public void RecordPayment_ShrinksReceivable_OverpaymentRefused()
        {
            var ledger = new DoctorReceivablesLedger();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            ledger.IssueInvoice(NewInvoice(DoctorPaymentMode.Account), DoctorCreditPosture.Generous, registry, diag);
            EntityId invoiceId = ledger.Invoices[0].InvoiceId;

            Assert.Null(ledger.RecordPayment(invoiceId, 150, 305, diag));
            Assert.AreEqual(200, ledger.Invoices[0].BalanceCents);
            Assert.AreEqual(150, ledger.CashCollectedCents, "Collecting is cash, not a second sale.");

            Assert.NotNull(ledger.RecordPayment(invoiceId, 250, 306, diag),
                "Overpayment is refused, never pocketed.");
            Assert.AreEqual(200, ledger.Invoices[0].BalanceCents);

            Assert.Null(ledger.RecordPayment(invoiceId, 200, 307, diag));
            Assert.AreEqual(0, ledger.Invoices[0].BalanceCents);
            Assert.AreEqual(350, ledger.CashCollectedCents);
            Assert.AreEqual(0, ledger.OutstandingReceivablesCents());
        }

        [Test]
        public void RecordPayment_UnknownInvoiceOrBadAmount_Refused()
        {
            var ledger = new DoctorReceivablesLedger();
            var diag = new List<string>();

            Assert.NotNull(ledger.RecordPayment(EntityId.For(EntityKind.Contract, 9999), 100, 300, diag));
            Assert.NotNull(ledger.RecordPayment(EntityId.Invalid, 0, 300, diag));
        }

        [Test]
        public void WriteOffAgedReceivables_AgedDebtBecomesBadDebt()
        {
            var ledger = new DoctorReceivablesLedger();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            ledger.IssueInvoice(NewInvoice(DoctorPaymentMode.Account), DoctorCreditPosture.Generous, registry, diag);

            int writtenOffEarly = ledger.WriteOffAgedReceivables(310, 30, diag);
            Assert.AreEqual(0, writtenOffEarly, "Not yet aged.");
            Assert.IsFalse(ledger.Invoices[0].IsBadDebt);

            int writtenOff = ledger.WriteOffAgedReceivables(331, 30, diag);
            Assert.AreEqual(350, writtenOff);
            Assert.IsTrue(ledger.Invoices[0].IsBadDebt);
            Assert.AreEqual(0, ledger.OutstandingReceivablesCents(), "Written-off debt leaves the outstanding book.");
            Assert.AreEqual(0, ledger.Invoices[0].BalanceCents);

            Assert.NotNull(ledger.RecordPayment(ledger.Invoices[0].InvoiceId, 100, 332, diag),
                "Written-off invoices don't take payments until re-opened.");
        }

        [Test]
        public void IssueInvoice_RequiresNamedDebtorAndChargeableLines()
        {
            var ledger = new DoctorReceivablesLedger();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            var anonymous = NewInvoice(DoctorPaymentMode.CashAtVisit);
            anonymous.DebtorHouseholdId = string.Empty;
            Assert.NotNull(ledger.IssueInvoice(anonymous, DoctorCreditPosture.Strict, registry, diag));

            var empty = NewInvoice(DoctorPaymentMode.CashAtVisit);
            empty.Lines.Clear();
            Assert.NotNull(ledger.IssueInvoice(empty, DoctorCreditPosture.Strict, registry, diag));
        }

        [Test]
        public void SaveLoad_RoundTripsInvoicesAndCash()
        {
            var ledger = new DoctorReceivablesLedger();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            ledger.IssueInvoice(NewInvoice(DoctorPaymentMode.Account), DoctorCreditPosture.Generous, registry, diag);
            ledger.RecordPayment(ledger.Invoices[0].InvoiceId, 100, 305, diag);

            var dto = ledger.CaptureSaveDto();
            var restored = new DoctorReceivablesLedger();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.Invoices.Count);
            Assert.AreEqual(250, restored.Invoices[0].BalanceCents);
            Assert.AreEqual(100, restored.CashCollectedCents);
        }
    }
}
