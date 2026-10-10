using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.Economy.Financing.Tests
{
    /// <summary>
    /// NX-3B: banking depth. Canon §18.8–18.10: a bank is a business with
    /// assets AND liabilities; deposits are owed money, not free cash.
    /// </summary>
    public sealed class BankingDepthTests
    {
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            diag = new List<string>();
        }

        [Test]
        public void DepositsAreLiabilitiesNotFreeCash()
        {
            var bank = new BankDepositLedger("First Bank");
            bank.OpenAccount("a1", "Samuel", DepositKind.Demand, 10000, 0, diag: diag);
            Assert.AreEqual(10000, bank.CashOnHandCents);
            Assert.AreEqual(10000, bank.DepositsOwedCents());

            // Lending moves cash out; the liability stays — the §18.9 gap.
            Assert.IsNull(bank.DisburseLoan(6000, "loan-1", diag));
            Assert.AreEqual(4000, bank.CashOnHandCents);
            Assert.AreEqual(10000, bank.DepositsOwedCents(), "Deposits are owed — lending does not erase them.");
        }

        [Test]
        public void WithdrawalRefusedWhenBankLacksCash()
        {
            var bank = new BankDepositLedger("First Bank");
            bank.OpenAccount("a1", "Samuel", DepositKind.Demand, 10000, 0, diag: diag);
            bank.DisburseLoan(9000, "loan-1", diag);
            string refused = bank.Withdraw("a1", 5000, 1, false, diag);
            Assert.IsNotNull(refused, "A run is an acceleration of a real liability — never faked (Canon §18.10).");
            Assert.AreEqual(1000, bank.CashOnHandCents, "Refused withdrawals move nothing.");
        }

        [Test]
        public void TermDepositLocksToMaturity()
        {
            var bank = new BankDepositLedger("First Bank");
            bank.OpenAccount("t1", "Widow", DepositKind.Term, 5000, 0, 90, 400, diag);
            string early = bank.Withdraw("t1", 1000, 10, false, diag);
            Assert.IsNotNull(early, "Term deposits lock to maturity.");
            Assert.IsNull(bank.Withdraw("t1", 1000, 95, false, diag), "After maturity, the money is theirs.");
        }

        [Test]
        public void PrivateLenderCannotLendWhatItLacks()
        {
            var lender = new PrivateLenderFunds("Moneybags", 20000);
            Assert.IsNull(lender.CommitFunds("c1", "note-1", 15000, 0, diag));
            Assert.AreEqual(5000, lender.AvailableCents());
            string refused = lender.CommitFunds("c2", "note-2", 6000, 0, diag);
            Assert.IsNotNull(refused, "Private lenders lend their own capital — no invention.");
            Assert.IsNull(lender.ReleaseCommitment("note-1", diag));
            Assert.AreEqual(20000, lender.AvailableCents());
        }

        [Test]
        public void ForeclosureWaterfallAndRedemption()
        {
            var ids = new EntityIdRegistry();
            var credit = new CreditRegistry();
            var titles = new TitleAuthority();
            titles.RegisterParcel("p-40ac", "40 acres", 40f, "Farmer", TitleBasis.HomesteadClaim, 0, diag);

            MortgageDeed mortgage = credit.IssueMortgage(ids, "Farmer", "Bank", "p-40ac", "40 acres",
                20000, "5 years, 8%", 0, diag);
            PropertyLien junior = credit.FileLien(ids, "Smith", "Farmer", "p-40ac", 3000, "unpaid smithy bill", 10, diag);

            var foreclosure = new ForeclosureService();
            ForeclosureCase kase = foreclosure.RecordDefault(ids, mortgage, 20000, 100, diag);
            Assert.IsNotNull(kase);
            Assert.IsNull(foreclosure.IssueNotice(kase, 100, diag));

            // Auction before the notice period ends is refused.
            var earlyBids = new System.Collections.Generic.List<ForeclosureBid>
                { new ForeclosureBid { BidderName = "Buyer", AmountCents = 25000 } };
            Assert.IsNotNull(foreclosure.ConductSale(kase, earlyBids, null, 500, 110, ids, titles, credit, diag));

            // Proper sale: 26000 bid, 500 costs → 25500. Mortgage takes 20000,
            // junior lien 3000, surplus 2500 to the borrower. No deficiency.
            var bids = new System.Collections.Generic.List<ForeclosureBid>
            {
                new ForeclosureBid { BidderName = "Buyer", AmountCents = 26000 },
                new ForeclosureBid { BidderName = "Lowball", AmountCents = 15000 },
            };
            var liens = new System.Collections.Generic.List<PropertyLien> { junior };
            Assert.IsNull(foreclosure.ConductSale(kase, bids, liens, 500, 140, ids, titles, credit, diag));
            Assert.AreEqual("Buyer", kase.BuyerName);
            Assert.AreEqual(2500, kase.SurplusToBorrowerCents);
            Assert.AreEqual(0, kase.DeficiencyCents);
            Assert.AreEqual("Buyer", titles.CurrentHolder("p-40ac"));
            Assert.AreEqual(CreditInstrumentStatus.Satisfied, junior.Status);

            // Redemption within the year restores title.
            Assert.IsNull(foreclosure.Redeem(kase, 26500, 200, ids, titles, diag));
            Assert.AreEqual("Farmer", titles.CurrentHolder("p-40ac"));
            Assert.AreEqual(ForeclosureStage.Redeemed, kase.Stage);
        }

        [Test]
        public void ForeclosureDeficiencyBecomesRecordedDebt()
        {
            var ids = new EntityIdRegistry();
            var credit = new CreditRegistry();
            var titles = new TitleAuthority();
            titles.RegisterParcel("p-10ac", "10 acres", 10f, "Farmer", TitleBasis.HomesteadClaim, 0, diag);

            MortgageDeed mortgage = credit.IssueMortgage(ids, "Farmer", "Bank", "p-10ac", "10 acres",
                20000, "5 years, 8%", 0, diag);
            var foreclosure = new ForeclosureService();
            ForeclosureCase kase = foreclosure.RecordDefault(ids, mortgage, 20000, 100, diag);
            foreclosure.IssueNotice(kase, 100, diag);

            // Fire sale: 12000 bid on 20000 owed → 8000 deficiency, recorded as a note.
            var bids = new System.Collections.Generic.List<ForeclosureBid>
                { new ForeclosureBid { BidderName = "Buyer", AmountCents = 12000 } };
            Assert.IsNull(foreclosure.ConductSale(kase, bids, null, 500, 140, ids, titles, credit, diag));
            Assert.AreEqual(8500, kase.DeficiencyCents, "20000 owed − 11500 net proceeds.");
            Assert.IsNotNull(credit.CaptureSaveDto().Notes.Find(
                note => note != null && note.MakerName == "Farmer" && note.PrincipalCents == 8500),
                "The deficiency was recorded as a real instrument, not wished away.");
        }

        [Test]
        public void VoluntarySurrenderRecordsWhetherDebtWasActuallySatisfied()
        {
            var ids = new EntityIdRegistry();
            var credit = new CreditRegistry();
            MortgageDeed mortgage = credit.IssueMortgage(ids, "Farmer", "Bank", "p-surrender", "farm",
                20000, "terms", 0, diag);
            var foreclosure = new ForeclosureService();
            ForeclosureCase caseFile = foreclosure.RecordDefault(ids, mortgage, 20000, 10, diag);
            foreclosure.IssueNotice(caseFile, 10, diag);
            Assert.IsNull(foreclosure.RecordVoluntarySurrender(caseFile, 15, false, diag));
            Assert.AreEqual(ForeclosureStage.VoluntarySurrendered, caseFile.Stage);
            Assert.IsFalse(caseFile.SurrenderSatisfiesDebt);
            Assert.AreEqual(20000, caseFile.DebtOwedCents);
        }
    }
}
