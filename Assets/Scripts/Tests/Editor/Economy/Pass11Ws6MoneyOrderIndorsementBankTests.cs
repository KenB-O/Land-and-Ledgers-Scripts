using System.Collections.Generic;
using LandLedgers.Economy.Postal;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass XI WS6 first-gate (M5): indorsement transfers presentment
    /// completely. RUN-011 stopped the payee presenting after indorsement
    /// but left the payee's bank agent able to present the same order —
    /// a misdirection of a single payment, not a double payment (Paid
    /// blocks a second redemption). IndorseOrder now clears the bank
    /// collection agency, and RedeemOrder additionally requires no
    /// indorsement for bank presentment, so a bank recorded after the
    /// indorsement cannot present either. Bank presentment with no
    /// indorsement is unchanged (covered by the WS3 integrity tests).
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws6MoneyOrderIndorsementBankTests
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
        public void IndorsedOrder_PayeeBankAgentCannotRedeem_IndorseeCan_SinglePaymentOnly()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNull(svc.EstablishReserve("po-b", 5000, 0, diag));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 500, 0, diag, (a, b) => 2));
            Assert.IsNull(svc.RecordBankCollection("po-a", 1, "First National Bank", 1, diag));
            Assert.IsNull(svc.IndorseOrder("po-a", 1, "Wade", 1, diag));
            Assert.AreEqual("", svc.FindOrder("po-a", 1).BankCollectionAgent,
                "Indorsement ends the payee's bank agency on the order itself.");

            svc.AdvanceDay(2, diag);
            Assert.IsNotNull(svc.RedeemOrder("po-a", 1, "First National Bank", 2, diag),
                "The payee's bank agent must not collect money that now belongs to the indorsee.");
            Assert.IsNotNull(svc.RedeemOrder("po-a", 1, "Emma", 2, diag),
                "The payee herself still cannot present after indorsement.");
            Assert.IsNotNull(svc.IndorseOrder("po-a", 1, "Ruby", 2, diag),
                "A second indorsement is still prohibited.");
            Assert.IsNull(svc.RedeemOrder("po-a", 1, "Wade", 2, diag),
                "The indorsee can still present.");
            Assert.AreEqual(MoneyOrderStatus.Paid, svc.FindOrder("po-a", 1).Status);
            Assert.AreEqual("Wade", svc.FindOrder("po-a", 1).PaidToName);
            Assert.AreEqual(4500, svc.MoneyOrderCashCents("po-b"),
                "Exactly one payment left the payable office's fund.");
        }

        [Test]
        public void IndorsedOrder_BankRecordedAfterIndorsementStillCannotPresent()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNull(svc.EstablishReserve("po-b", 5000, 0, diag));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 500, 0, diag, (a, b) => 2));
            Assert.IsNull(svc.IndorseOrder("po-a", 1, "Wade", 1, diag));
            // Recording a bank after indorsement must not reopen presentment:
            // the agency belonged to the payee, and presentment has transferred.
            Assert.IsNull(svc.RecordBankCollection("po-a", 1, "First National Bank", 1, diag));
            svc.AdvanceDay(2, diag);
            Assert.IsNotNull(svc.RedeemOrder("po-a", 1, "First National Bank", 2, diag),
                "Bank presentment requires no indorsement, even when the bank was recorded after it.");
            Assert.IsNull(svc.RedeemOrder("po-a", 1, "Wade", 2, diag));
            Assert.AreEqual("Wade", svc.FindOrder("po-a", 1).PaidToName);
        }
    }
}
