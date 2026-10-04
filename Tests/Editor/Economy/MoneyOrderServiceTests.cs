using System.Collections.Generic;
using LandLedgers.Economy.Postal;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D4E: postal money orders — the 1864 US system adapted to the game's
    /// 1870 setting (Canon Part VII §7.1-7.2; researched fee schedule, advice
    /// procedure, 90-day validity, duplicates, office classes, and postal
    /// settlement between offices).
    /// </summary>
    [TestFixture]
    public sealed class MoneyOrderServiceTests
    {
        private static MoneyOrderService TwoOffices(out List<string> diag, int reserveA = 0)
        {
            var postal = new PostalService();
            diag = new List<string>();
            Assert.IsNull(postal.RegisterOffice("po-a", "town-a", "biz-po-a", new List<int> { 0 }, 7, diag));
            Assert.IsNull(postal.RegisterOffice("po-b", "town-b", "biz-po-b", new List<int> { 0 }, 9, diag));
            var svc = new MoneyOrderService(postal);
            Assert.IsNull(svc.RegisterMoneyOrderOffice("po-b", MoneyOrderOfficeClass.Depository, null, 0, diag));
            Assert.IsNull(svc.RegisterMoneyOrderOffice("po-a", MoneyOrderOfficeClass.Ordinary, "po-b", reserveA, diag));
            return svc;
        }

        [Test]
        public void FeeSchedule_Matches1866Reform()
        {
            // 1866 reform (in force in 1870): 10c for $20 or less, 25c above; min $1, max $50.
            Assert.AreEqual(10, MoneyOrderFeeSchedule.FeeForCents(100));
            Assert.AreEqual(10, MoneyOrderFeeSchedule.FeeForCents(2000), "$20 exactly pays the low fee.");
            Assert.AreEqual(25, MoneyOrderFeeSchedule.FeeForCents(2001));
            Assert.AreEqual(25, MoneyOrderFeeSchedule.FeeForCents(5000));
            Assert.AreEqual(-1, MoneyOrderFeeSchedule.FeeForCents(99), "Below the $1 minimum.");
            Assert.AreEqual(-1, MoneyOrderFeeSchedule.FeeForCents(5001), "Above the $50 maximum.");
            Assert.IsTrue(MoneyOrderFeeSchedule.IsIssuable(100));
            Assert.IsTrue(MoneyOrderFeeSchedule.IsIssuable(5000));
            Assert.IsFalse(MoneyOrderFeeSchedule.IsIssuable(99));
            Assert.IsFalse(MoneyOrderFeeSchedule.IsIssuable(5001));
        }

        [Test]
        public void IssueOrder_RecordsSerialFeeAndAdvice()
        {
            var svc = TwoOffices(out List<string> diag);
            PostalMoneyOrder order = svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 2500, 0, diag, (a, b) => 2);
            Assert.IsNotNull(order);
            Assert.AreEqual(1, order.SerialNumber, "Serials run consecutively per issuing office from 1.");
            Assert.AreEqual(25, order.FeeCents, "$25 > $20 so the 25c fee applies.");
            Assert.AreEqual(MoneyOrderStatus.Issued, order.Status);
            Assert.AreEqual(2500, svc.MoneyOrderCashCents("po-a"), "The sender's cash enters the issuing office's money-order fund.");
            PostalMoneyOrderAdvice advice = svc.FindAdvice("po-a", 1);
            Assert.IsNotNull(advice);
            Assert.AreEqual(2, advice.DueArrivalDayIndex, "Advice travels by mail — it does not teleport.");
            Assert.AreEqual(-1, advice.ReceivedDayIndex);
            Assert.AreEqual(25, svc.CollectMoneyOrderFeeRevenue("po-a", diag), "Fees are post-office revenue (real lots).");
            Assert.AreEqual(0, svc.CollectMoneyOrderFeeRevenue("po-a", diag), "Revenue collects once.");
        }

        [Test]
        public void IssueOrder_RefusesOutOfRangeAmounts()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 6000, 0, diag), "Max $50 (1866).");
            Assert.IsNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 99, 0, diag), "Min $1.");
            Assert.IsNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 0, 0, diag));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 100, 0, diag));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 5000, 0, diag));
            Assert.AreEqual(1, svc.FindOrder("po-a", 1).SerialNumber);
            Assert.AreEqual(2, svc.FindOrder("po-a", 2).SerialNumber);
        }

        [Test]
        public void IssueOrder_RefusesSundays()
        {
            var svc = TwoOffices(out List<string> diag);
            // Day 6 = Sunday on the 0=Monday calibration convention.
            Assert.IsNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 500, 6, diag),
                "1864 instructions §59: no money-order business on Sundays.");
        }

        [Test]
        public void RedeemOrder_RefusesBeforeAdviceArrives()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNull(svc.EstablishReserve("po-b", 5000, 0, diag));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 2500, 0, diag, (a, b) => 2));
            string refused = svc.RedeemOrder("po-a", 1, "Emma", 1, diag);
            Assert.IsNotNull(refused, "Postmasters are prohibited from paying an order of which the advice has not been received.");
            StringAssert.Contains("advice", refused.ToLowerInvariant());
            Assert.AreEqual(MoneyOrderStatus.Issued, svc.FindOrder("po-a", 1).Status);
            Assert.AreEqual(0, svc.Claims.Count, "No claim is born from a refused redemption.");
        }

        [Test]
        public void AdvanceDay_DeliversAdviceThenPaysAndCreatesClaim()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNull(svc.EstablishReserve("po-b", 5000, 0, diag));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 2500, 0, diag, (a, b) => 2));
            svc.AdvanceDay(2, diag);
            Assert.AreEqual(2, svc.FindAdvice("po-a", 1).ReceivedDayIndex);
            Assert.IsNull(svc.RedeemOrder("po-a", 1, "Emma", 2, diag));
            PostalMoneyOrder order = svc.FindOrder("po-a", 1);
            Assert.AreEqual(MoneyOrderStatus.Paid, order.Status);
            Assert.AreEqual("Emma", order.PaidToName);
            Assert.AreEqual(2500, svc.MoneyOrderCashCents("po-b"), "5000 reserve minus 2500 paid out.");
            Assert.AreEqual(1, svc.Claims.Count);
            PostalMoneyOrderClaim claim = new List<PostalMoneyOrderClaim>(svc.Claims)[0];
            Assert.AreEqual("po-a", claim.DebtorOfficeId, "The issuing office took the sender's cash.");
            Assert.AreEqual("po-b", claim.CreditorOfficeId, "The paying office paid real cash out.");
            Assert.AreEqual(2500, claim.AmountCents);
            Assert.AreEqual(PostalClaimStatus.Open, claim.Status);
        }

        [Test]
        public void RedeemOrder_RefusesWrongPresenter()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNull(svc.EstablishReserve("po-b", 5000, 0, diag));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 2500, 0, diag, (a, b) => 2));
            svc.AdvanceDay(2, diag);
            Assert.IsNotNull(svc.RedeemOrder("po-a", 1, "Mallory", 2, diag), "Only the named payee (or indorsee) is paid.");
            Assert.AreEqual(MoneyOrderStatus.Issued, svc.FindOrder("po-a", 1).Status);
        }

        [Test]
        public void RedeemOrder_FlagsAlteredOrder()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNull(svc.EstablishReserve("po-b", 5000, 0, diag));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 1000, 0, diag, (a, b) => 2));
            svc.AdvanceDay(2, diag);
            // Simulate a raised order: the paper no longer matches the advice.
            svc.FindOrder("po-a", 1).AmountCents = 2000;
            string refused = svc.RedeemOrder("po-a", 1, "Emma", 2, diag);
            Assert.IsNotNull(refused, "A forged/altered order is refused loudly — no auto-honor.");
            PostalMoneyOrder order = svc.FindOrder("po-a", 1);
            Assert.IsTrue(order.FraudFlagged);
            Assert.IsNotNull(svc.RedeemOrder("po-a", 1, "Emma", 2, diag), "A flagged order is never honored later.");
            Assert.AreEqual(MoneyOrderStatus.Issued, order.Status, "Still unpaid — the money never moved.");
        }

        [Test]
        public void RedeemOrder_RefusesShortTill()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 2500, 0, diag, (a, b) => 2));
            svc.AdvanceDay(2, diag);
            string refused = svc.RedeemOrder("po-a", 1, "Emma", 2, diag);
            Assert.IsNotNull(refused, "The office pays from its cash — a short till refuses, never fakes.");
            Assert.AreEqual(MoneyOrderStatus.Issued, svc.FindOrder("po-a", 1).Status);
            Assert.AreEqual(0, svc.Claims.Count);
        }

        [Test]
        public void IndorseOrder_AllowsSingleIndorsement()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNull(svc.EstablishReserve("po-b", 5000, 0, diag));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 500, 0, diag, (a, b) => 2));
            Assert.IsNull(svc.IndorseOrder("po-a", 1, "Wade", 1, diag));
            svc.AdvanceDay(2, diag);
            Assert.IsNotNull(svc.RedeemOrder("po-a", 1, "Emma", 2, diag), "After indorsement the payee no longer presents.");
            Assert.IsNull(svc.RedeemOrder("po-a", 1, "Wade", 2, diag));
            Assert.AreEqual(MoneyOrderStatus.Paid, svc.FindOrder("po-a", 1).Status);
            Assert.IsNotNull(svc.IndorseOrder("po-a", 1, "Ruby", 3, diag), "More than one indorsement is prohibited.");
        }

        [Test]
        public void RecordBankCollection_BankPresentsWithoutConsumingIndorsement()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNull(svc.EstablishReserve("po-b", 5000, 0, diag));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 1000, 0, diag, (a, b) => 2));
            Assert.IsNull(svc.RecordBankCollection("po-a", 1, "First National Bank", 1, diag));
            svc.AdvanceDay(2, diag);
            // W7/D4A link: the bank presents as the payee's agent; the post office pays cash.
            Assert.IsNull(svc.RedeemOrder("po-a", 1, "First National Bank", 2, diag));
            PostalMoneyOrder order = svc.FindOrder("po-a", 1);
            Assert.AreEqual(MoneyOrderStatus.Paid, order.Status);
            Assert.AreEqual("", order.IndorsedToName, "A bank's collection stamp is not an indorsement.");
            Assert.AreEqual(1, svc.Claims.Count, "The postal claim is on the issuing office — no interbank claim.");
        }

        [Test]
        public void IssueDuplicate_ReplacesLostOrderFree()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNull(svc.EstablishReserve("po-b", 5000, 0, diag));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 500, 0, diag, (a, b) => 2));
            PostalMoneyOrder duplicate = svc.IssueDuplicate("po-a", 1, 3, diag);
            Assert.IsNotNull(duplicate);
            Assert.AreEqual(2, duplicate.SerialNumber);
            Assert.AreEqual(0, duplicate.FeeCents, "No charge is made for a duplicate.");
            Assert.AreEqual(1, duplicate.ReplacesSerial);
            Assert.AreEqual(MoneyOrderStatus.Duplicated, svc.FindOrder("po-a", 1).Status);
            Assert.IsNotNull(svc.RedeemOrder("po-a", 1, "Emma", 4, diag), "The replaced original can never be paid.");
            Assert.IsNotNull(svc.IssueDuplicate("po-a", 1, 4, diag), "No duplicate of a non-live order.");
            // The duplicate's own advice must arrive before it pays (due day 4).
            svc.AdvanceDay(4, diag);
            Assert.IsNull(svc.RedeemOrder("po-a", 2, "Emma", 4, diag));
            Assert.AreEqual(MoneyOrderStatus.Paid, svc.FindOrder("po-a", 2).Status);
            Assert.AreEqual(4500, svc.MoneyOrderCashCents("po-b"));
        }

        [Test]
        public void RedeemOrder_NinetyDayValidity()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNull(svc.EstablishReserve("po-b", 5000, 0, diag));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 500, 0, diag, (a, b) => 2));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 500, 0, diag, (a, b) => 2));
            svc.AdvanceDay(2, diag);
            // Day 89 is within ninety days of issue — still payable (day 90 would be a Sunday).
            Assert.IsNull(svc.RedeemOrder("po-a", 1, "Emma", 89, diag));
            // Day 91 is not.
            svc.AdvanceDay(91, diag);
            Assert.AreEqual(MoneyOrderStatus.Expired, svc.FindOrder("po-a", 2).Status);
            string refused = svc.RedeemOrder("po-a", 2, "Emma", 91, diag);
            Assert.IsNotNull(refused);
            StringAssert.Contains("expired", refused.ToLowerInvariant());
        }

        [Test]
        public void RepayOrder_ReturnsFaceAtIssueOffice()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 1000, 0, diag));
            Assert.AreEqual(1000, svc.MoneyOrderCashCents("po-a"));
            Assert.IsNull(svc.RepayOrder("po-a", 1, 5, diag));
            Assert.AreEqual(MoneyOrderStatus.Repaid, svc.FindOrder("po-a", 1).Status);
            Assert.AreEqual(0, svc.MoneyOrderCashCents("po-a"));
            Assert.AreEqual(10, svc.CollectMoneyOrderFeeRevenue("po-a", diag), "The fee is earned — not refunded.");
            Assert.IsNotNull(svc.RedeemOrder("po-a", 1, "Emma", 5, diag), "A repaid order is not payable.");
        }

        [Test]
        public void SweepSurplusToDepository_MovesAboveReserve()
        {
            var svc = TwoOffices(out List<string> diag, reserveA: 500);
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 2500, 0, diag));
            Assert.AreEqual(2000, svc.SweepSurplusToDepository("po-a", 1, diag));
            Assert.AreEqual(500, svc.MoneyOrderCashCents("po-a"), "The reserve stays.");
            Assert.AreEqual(2000, svc.MoneyOrderCashCents("po-b"));
            Assert.AreEqual(0, svc.SweepSurplusToDepository("po-a", 2, diag), "Nothing above the reserve — nothing moves.");
            Assert.IsNull(svc.DrawOnDepository("po-a", 1000, 2, diag));
            Assert.AreEqual(1500, svc.MoneyOrderCashCents("po-a"));
            Assert.AreEqual(1000, svc.MoneyOrderCashCents("po-b"));
            Assert.IsNotNull(svc.DrawOnDepository("po-a", 5000, 2, diag), "The depository is short — refused.");
        }

        [Test]
        public void RunSettlementCycle_NetsAndSettlesInFull()
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
            // po-a fund: 3500 issued − 500 paid = 3000. po-b fund: 4000 reserve + 500 issued − 3500 paid = 1000.
            Assert.AreEqual(3000, svc.MoneyOrderCashCents("po-a"));
            Assert.AreEqual(1000, svc.MoneyOrderCashCents("po-b"));

            svc.RunSettlementCycle(4, diag);
            Assert.AreEqual(1, svc.Positions.Count);
            PostalSettlementPosition position = new List<PostalSettlementPosition>(svc.Positions)[0];
            Assert.AreEqual("po-a", position.DebtorOfficeId);
            Assert.AreEqual("po-b", position.CreditorOfficeId);
            Assert.AreEqual(2500, position.NetAmountCents, "3500 − 500 netted bilaterally.");
            Assert.AreEqual(3, position.ComprisedClaimIds.Count);
            Assert.AreEqual(PostalPositionStatus.Settled, position.Status);
            Assert.AreEqual(500, svc.MoneyOrderCashCents("po-a"), "3000 − 2500 settled in full.");
            Assert.AreEqual(3500, svc.MoneyOrderCashCents("po-b"), "1000 + 2500.");
            foreach (PostalMoneyOrderClaim claim in svc.Claims)
                Assert.AreEqual(PostalClaimStatus.Settled, claim.Status);
        }

        [Test]
        public void RunSettlementCycle_ShortDebtorStaysOpenThenRetries()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNull(svc.EstablishReserve("po-b", 5000, 0, diag));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 2500, 0, diag, (a, b) => 2));
            // po-a sweeps its whole fund to the depository, then po-b pays the order.
            Assert.AreEqual(2500, svc.SweepSurplusToDepository("po-a", 1, diag));
            svc.AdvanceDay(2, diag);
            Assert.IsNull(svc.RedeemOrder("po-a", 1, "Emma", 2, diag));

            svc.RunSettlementCycle(3, diag);
            PostalSettlementPosition position = new List<PostalSettlementPosition>(svc.Positions)[0];
            Assert.AreEqual(PostalPositionStatus.Open, position.Status, "No partials — the short debtor leaves the position open.");
            Assert.AreEqual(PostalClaimStatus.Netted, new List<PostalMoneyOrderClaim>(svc.Claims)[0].Status);

            Assert.IsNull(svc.DrawOnDepository("po-a", 2500, 3, diag));
            svc.RunSettlementCycle(4, diag);
            Assert.AreEqual(PostalPositionStatus.Settled, position.Status);
            Assert.AreEqual(PostalClaimStatus.Settled, new List<PostalMoneyOrderClaim>(svc.Claims)[0].Status);
            Assert.AreEqual(0, svc.MoneyOrderCashCents("po-a"));
            Assert.AreEqual(5000, svc.MoneyOrderCashCents("po-b"), "5000 reserve + 2500 sweep − 2500 paid + 2500 settled = 5000.");
        }

        [Test]
        public void RegisterMoneyOrderOffice_ValidatesDesignation()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNotNull(svc.RegisterMoneyOrderOffice("po-nowhere", MoneyOrderOfficeClass.Ordinary, "po-b", 0, diag),
                "Designation is for real post offices.");
            Assert.IsNotNull(svc.RegisterMoneyOrderOffice("po-a", MoneyOrderOfficeClass.Ordinary, null, 0, diag),
                "An ordinary office must name its depository.");
            Assert.IsNotNull(svc.RegisterMoneyOrderOffice("po-a", MoneyOrderOfficeClass.Ordinary, "po-b", 0, diag),
                "No duplicate designations.");

            // A post office that exists but was never designated cannot issue money orders.
            var postal = new PostalService();
            var onlyPostal = new MoneyOrderService(postal);
            var d2 = new List<string>();
            Assert.IsNull(postal.RegisterOffice("po-x", "town-x", "biz-x", new List<int> { 0 }, 1, d2));
            Assert.IsNull(postal.RegisterOffice("po-y", "town-y", "biz-y", new List<int> { 0 }, 2, d2));
            Assert.IsNull(onlyPostal.IssueOrder("po-x", "po-y", "Kennedy", "Emma", 500, 0, d2),
                "Not every post office is a money-order office.");
        }

        [Test]
        public void SaveRoundTrip_PreservesMoneyOrderBooks()
        {
            var svc = TwoOffices(out List<string> diag);
            Assert.IsNull(svc.EstablishReserve("po-b", 4000, 0, diag));
            Assert.IsNotNull(svc.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 2500, 0, diag, (a, b) => 2));
            svc.AdvanceDay(2, diag);
            Assert.IsNull(svc.RedeemOrder("po-a", 1, "Emma", 2, diag));

            MoneyOrderServiceSaveDto dto = svc.CaptureSaveDto();
            var restored = new MoneyOrderService();
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(2, restored.Offices.Count);
            Assert.AreEqual(1, restored.Orders.Count);
            Assert.AreEqual(1, restored.Claims.Count);
            Assert.AreEqual(2500, restored.MoneyOrderCashCents("po-a"));
            Assert.AreEqual(1500, restored.MoneyOrderCashCents("po-b"), "4000 − 2500 paid.");
            Assert.AreEqual(MoneyOrderStatus.Paid, restored.FindOrder("po-a", 1).Status);
            // Serials continue after restore: next issue at po-a is serial 2.
            PostalMoneyOrder next = restored.IssueOrder("po-a", "po-b", "Kennedy", "Emma", 500, 3, new List<string>());
            Assert.AreEqual(2, next.SerialNumber);
        }

        [Test]
        public void RegisterTaskDefinitions_RegistersCounterWork()
        {
            var svc = TwoOffices(out List<string> diag);
            var authority = new TaskAuthority();
            svc.RegisterTaskDefinitions(authority);
            Assert.IsNotNull(authority.GetDefinition(MoneyOrderService.IssueOrderTaskId));
            Assert.IsNotNull(authority.GetDefinition(MoneyOrderService.PayOrderTaskId));
        }
    }
}
