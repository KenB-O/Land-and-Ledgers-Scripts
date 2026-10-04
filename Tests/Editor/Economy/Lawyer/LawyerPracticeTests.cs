using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Lawyer;
using LandLedgers.Economy.Core;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// W9A: the law practice — retainer + per-matter billing against real
    /// clients, office requirements as a precondition, and receivables that
    /// are genuinely owed (never erased, never overpaid, never conjured).
    /// </summary>
    [TestFixture]
    public sealed class LawyerPracticeTests
    {
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            diag = new List<string>();
        }

        private static LawyerPracticeRuntime FitPractice(string id = "law-1")
        {
            var practice = new LawyerPracticeRuntime(id);
            practice.SetOffice(new LawyerOfficeRequirements
            {
                HasDesk = true,
                HasLawBooks = true,
                HasWritingSupplies = true,
                HasDocumentForms = true,
                HasSeal = true,
                HasSecureRecords = true,
            });
            return practice;
        }

        [Test]
        public void BusinessType_Lawyer_Is27_AndValues0To26_Intact()
        {
            Assert.AreEqual(27, (int)BusinessType.Lawyer);
            Assert.AreEqual(22, (int)BusinessType.Restaurant, "Restaurant must stay 22 — append-only.");
            Assert.AreEqual(26, (int)BusinessType.Bank);
        }

        [Test]
        public void OpenMatter_RequiresFitOffice_AndRealNamedClient()
        {
            var bare = new LawyerPracticeRuntime("law-bare");
            Assert.IsNull(bare.OpenMatter(null, 5, "Kennedy Baldwin-ooms", LegalMatterKind.WillDrafting, "a will", 10, diag: diag),
                "No office fitness, no matter.");

            var practice = FitPractice();
            Assert.IsNull(practice.OpenMatter(null, -1, "Kennedy Baldwin-ooms", LegalMatterKind.WillDrafting, "a will", 10, diag: diag),
                "Anonymous clients are refused.");
            Assert.IsNull(practice.OpenMatter(null, 5, "", LegalMatterKind.WillDrafting, "a will", 10, diag: diag),
                "Unnamed clients are refused.");
            Assert.IsNull(practice.OpenMatter(null, 5, "Kennedy Baldwin-ooms", LegalMatterKind.Unspecified, "?", 10, diag: diag),
                "Matter kind must be stated.");

            var matter = practice.OpenMatter(null, 5, "Kennedy Baldwin-ooms", LegalMatterKind.WillDrafting, "a will", 10, diag: diag);
            Assert.IsNotNull(matter);
            Assert.AreEqual(MatterStatus.Open, matter.Status);
            Assert.AreEqual(5, matter.ClientPersonId);
        }

        [Test]
        public void FullBillingCycle_RetainerApplied_ReceivablePaid_RefundReturned()
        {
            var practice = FitPractice();
            var matter = practice.OpenMatter(null, 5, "Kennedy Baldwin-ooms", LegalMatterKind.WillDrafting, "a will", 10, diag: diag);

            Assert.IsNull(practice.ReceiveRetainer(matter.MatterId, 1000, diag), "Retainer received.");
            Assert.IsNull(practice.AccrueFee(matter.MatterId, "Will drafting, flat", 500, diag));
            Assert.IsNull(practice.AccrueFee(matter.MatterId, "Consultation", 400, diag));

            var invoice = practice.IssueInvoice(null, matter.MatterId, 11, diag);
            Assert.IsNotNull(invoice);
            Assert.AreEqual(900, invoice.TotalCents);
            Assert.AreEqual(900, invoice.RetainerAppliedCents, "Trust balance pays the invoice first.");
            Assert.AreEqual(0, invoice.BalanceCents);
            Assert.AreEqual(100, practice.TotalRetainerTrustCents(), "Unearned retainer stays in trust.");

            var refund = -1;
            Assert.IsNull(practice.CloseMatter(matter.MatterId, 12, out refund, diag));
            Assert.AreEqual(100, refund, "Unearned retainer is refunded, never kept.");
        }

        [Test]
        public void UnpaidBalance_IsARealReceivable()
        {
            var practice = FitPractice();
            var matter = practice.OpenMatter(null, 6, "Shannon Cole", LegalMatterKind.ContractDrafting, "supply contract", 10, diag: diag);
            Assert.IsNull(practice.AccrueFee(matter.MatterId, "Contract drafting", 400, diag));
            var invoice = practice.IssueInvoice(null, matter.MatterId, 11, diag);

            Assert.AreEqual(400, invoice.BalanceCents);
            Assert.AreEqual(400, practice.OutstandingReceivablesCents());

            // Overpayment is refused, not pocketed.
            Assert.IsNotNull(practice.RecordPayment(invoice.InvoiceId, 500, diag));
            Assert.IsNull(practice.RecordPayment(invoice.InvoiceId, 250, diag));
            Assert.AreEqual(150, invoice.BalanceCents);
            Assert.IsNull(practice.RecordPayment(invoice.InvoiceId, 150, diag));
            Assert.AreEqual(0, practice.OutstandingReceivablesCents());
        }

        [Test]
        public void FeesAccrueOnlyToOpenMatters()
        {
            var practice = FitPractice();
            var matter = practice.OpenMatter(null, 7, "Emma Baldwin-ooms", LegalMatterKind.DeedDrafting, "a deed", 10, diag: diag);
            Assert.IsNull(practice.AbandonMatter(matter.MatterId, 11, diag));
            Assert.AreEqual(MatterStatus.Abandoned, practice.GetMatter(matter.MatterId).Status);
            Assert.IsNotNull(practice.AccrueFee(matter.MatterId, "late fee", 100, diag), "Abandoned matters take no fees.");
        }

        [Test]
        public void Abandonment_NeverErasesTheBalance()
        {
            var practice = FitPractice();
            var matter = practice.OpenMatter(null, 8, "Arnold Baldwin", LegalMatterKind.LegalAdvice, "bond surety advice", 10, diag: diag);
            Assert.IsNull(practice.AccrueFee(matter.MatterId, "Advice", 300, diag));
            practice.IssueInvoice(null, matter.MatterId, 11, diag);
            Assert.IsNull(practice.AbandonMatter(matter.MatterId, 12, diag));
            Assert.AreEqual(300, practice.OutstandingReceivablesCents(),
                "Walking away does not erase what the client owes.");
        }

        [Test]
        public void CloseMatter_BlockedUntilReceivablesCollected()
        {
            var practice = FitPractice();
            var matter = practice.OpenMatter(null, 9, "Grace Millward", LegalMatterKind.TitleAbstract, "abstract", 10, diag: diag);
            Assert.IsNull(practice.AccrueFee(matter.MatterId, "Abstract work", 250, diag));
            practice.IssueInvoice(null, matter.MatterId, 11, diag);

            var refund = -1;
            Assert.IsNotNull(practice.CloseMatter(matter.MatterId, 12, out refund, diag),
                "Cannot close with an open receivable.");
        }

        [Test]
        public void SaveRoundTrip_PreservesMatters_Invoices_Trust()
        {
            var practice = FitPractice();
            var matter = practice.OpenMatter(null, 5, "Kennedy Baldwin-ooms", LegalMatterKind.WillDrafting, "a will", 10, diag: diag);
            Assert.IsNull(practice.ReceiveRetainer(matter.MatterId, 1000, diag));
            Assert.IsNull(practice.AccrueFee(matter.MatterId, "Will drafting, flat", 500, diag));
            practice.IssueInvoice(null, matter.MatterId, 11, diag);

            var dto = practice.CaptureSaveDto();
            var restored = new LawyerPracticeRuntime("law-2");
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(500, restored.OutstandingReceivablesCents() + 0 + restored.TotalRetainerTrustCents(),
                "Invoice paid in full from retainer; trust holds the rest.");
            Assert.AreEqual(1, restored.Matters.Count);
            Assert.AreEqual(1, restored.Invoices.Count);
            Assert.AreEqual("Kennedy Baldwin-ooms", restored.GetMatter(matter.MatterId).ClientName);
        }

        [Test]
        public void FeeSchedule_DefaultsAreCalibration_NotCanonConstants()
        {
            var schedule = new LawyerFeeSchedule();
            Assert.Greater(schedule.DefaultRetainerCents, 0);
            Assert.Greater(schedule.DefaultRetainerFor(LegalMatterKind.DisputeRepresentation), 0);
            Assert.AreEqual("Law Office", BusinessRuntimeNaming.GetBusinessTypeDisplayName(BusinessType.Lawyer));
        }
    }
}
