using System.Collections.Generic;
using LandLedgers.Economy.Postal;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass XI WS3 (RUN-011): production-side money-order integrity.
    /// These tests assert the CORRECT semantics and do not edit the
    /// historical MoneyOrderServiceTests assertions. Two of those
    /// historical tests are genuinely wrong (see the WS3 report):
    /// IssueDuplicate line 202 asserts IsNotNull for an object-return
    /// refusal (null is the refusal), and RunSettlementCycle asserts a
    /// 2500 net for a 3500 minus 500 bilateral netting (3000).
    /// This file locks the correct behaviour so a future pass can see
    /// the distinction without re-litigating it.
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws3MoneyOrderIntegrityTests
    {
        private static MoneyOrderService TwoOffices(out List<string> diag)
        {
            var postal = new PostalService();
            diag = new List<string>();
            Assert.IsNull(postal.RegisterOffice("po-a", "town-a", "biz-po-a", new List<int> { 0 }, 7, diag));
            Assert.IsNull(postal.RegisterOffice("po-b", "town-b", "biz-po-b", new List<int> { 0 }, 9, diag));
            var svc = new MoneyOrderService(postal);
            Assert.IsNull(svc.RegisterMoneyOrderOffice("po-b", MoneyOrderOfficeClass.Depository, null, 0, diag));
            Assert.IsNull(svc.RegisterMoneyOrderOffice("po-a", MoneyOrderOfficeClass.Ordinary, "po-b", 0, diag));
            return svc;
        }

        [Test]
        public void Indorsement_TransfersPresentment_PayeeNoLongerPresents()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNull(svc.EstablishReserve("po-b", 5000, 0, diag));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 500, 0, diag, (a, b) => 2));
            Assert.IsNull(svc.IndorseOrder("po-a", 1, "Wade", 1, diag));
            svc.AdvanceDay(2, diag);
            Assert.IsNotNull(svc.RedeemOrder("po-a", 1, "Emma", 2, diag),
                "After indorsement the original payee must not collect the indorsee's money.");
            Assert.IsNull(svc.RedeemOrder("po-a", 1, "Wade", 2, diag));
            Assert.AreEqual(MoneyOrderStatus.Paid, svc.FindOrder("po-a", 1).Status);
            Assert.AreEqual("Wade", svc.FindOrder("po-a", 1).PaidToName);
        }

        [Test]
        public void Indorsement_BankCollectionWithoutIndorsement_StillPresents()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNull(svc.EstablishReserve("po-b", 5000, 0, diag));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 1000, 0, diag, (a, b) => 2));
            Assert.IsNull(svc.RecordBankCollection("po-a", 1, "First National Bank", 1, diag));
            svc.AdvanceDay(2, diag);
            Assert.IsNull(svc.RedeemOrder("po-a", 1, "First National Bank", 2, diag));
            Assert.AreEqual("", svc.FindOrder("po-a", 1).IndorsedToName, "A bank collection stamp is not an indorsement.");
        }

        [Test]
        public void Duplicate_OfNonLiveOrder_IsRefusedWithNull()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNull(svc.EstablishReserve("po-b", 5000, 0, diag));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 500, 0, diag, (a, b) => 2));
            Assert.IsNotNull(svc.IssueDuplicate("po-a", 1, 3, diag));
            Assert.AreEqual(MoneyOrderStatus.Duplicated, svc.FindOrder("po-a", 1).Status);
            // Object-return convention: null is the refusal, matching
            // IssueOrder refusals. A duplicate of a non-live order must
            // never be issued, or one loss could mint two live orders.
            Assert.IsNull(svc.IssueDuplicate("po-a", 1, 4, diag), "No duplicate of a non-live order.");
            Assert.AreEqual(2, new List<PostalMoneyOrder>(svc.Orders).Count, "No second duplicate was minted.");
        }

        [Test]
        public void Settlement_BilateralNetting_IsGrossMinusGross()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNull(svc.EstablishReserve("po-b", 4000, 0, diag));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 2500, 0, diag, (a, b) => 2));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 1000, 0, diag, (a, b) => 2));
            Assert.IsNotNull(svc.IssueOrder("po-b", "po-a", "Emma", "Kennedy", 500, 1, diag, (a, b) => 2));
            svc.AdvanceDay(3, diag);
            Assert.IsNull(svc.RedeemOrder("po-a", 1, "Emma", 3, diag));
            Assert.IsNull(svc.RedeemOrder("po-a", 2, "Emma", 3, diag));
            Assert.IsNull(svc.RedeemOrder("po-b", 1, "Kennedy", 3, diag));
            svc.RunSettlementCycle(4, diag);
            Assert.AreEqual(1, new List<PostalSettlementPosition>(svc.Positions).Count);
            PostalSettlementPosition position = new List<PostalSettlementPosition>(svc.Positions)[0];
            Assert.AreEqual("po-a", position.DebtorOfficeId);
            Assert.AreEqual("po-b", position.CreditorOfficeId);
            Assert.AreEqual(3000, position.NetAmountCents, "3500 owed by po-a minus 500 owed by po-b nets to 3000.");
            Assert.AreEqual(0, svc.MoneyOrderCashCents("po-a"), "3000 fund minus 3000 settled.");
            Assert.AreEqual(4000, svc.MoneyOrderCashCents("po-b"), "1000 fund plus 3000 settled.");
        }
    }
}
