using System.Collections.Generic;
using LandLedgers.Economy.Liabilities;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.ReadModels.Valuation;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// SWN-3: the business liability ledger. Debt comes only from real events
    /// (borrowing, buying on credit); every balance change posts to the BIZ-5
    /// valuation read model; the ledger round-trips through save/load.
    /// </summary>
    [TestFixture]
    public sealed class BusinessLiabilityLedgerTests
    {
        private (BusinessLiabilityLedger, EnterpriseValuationReadModel) LedgerWithValuation()
        {
            var valuation = new EnterpriseValuationReadModel();
            valuation.RegisterBusiness("farm-biz-1", "player", true);
            var ledger = new BusinessLiabilityLedger();
            ledger.AttachValuation(valuation);
            return (ledger, valuation);
        }

        [Test]
        public void Borrow_CreatesLiabilityAndPostsToValuation()
        {
            var (ledger, valuation) = LedgerWithValuation();
            var ids = new EntityIdRegistry();
            var borrowerLedger = new HouseholdLedger(7);
            var diagnostics = new List<string>();

            BusinessLiability loan = ledger.Borrow(ids, "farm-biz-1", "First Bank of Town",
                50000, "8% annual, due day 900", "seed loan for the season",
                100, borrowerLedger, diagnostics);

            Assert.IsNotNull(loan, "Borrow failed: " + string.Join(" | ", diagnostics));
            Assert.AreEqual(LiabilityKind.Loan, loan.Kind);
            Assert.AreEqual(50000, loan.BalanceCents);
            Assert.AreEqual(50000, ledger.TotalLiabilitiesFor("farm-biz-1"));
            Assert.IsTrue(loan.LiabilityId.IsValid, "Liabilities carry real ids (EntityKind.Contract).");
        }

        [Test]
        public void Borrow_RefusesAnonymousLenders()
        {
            var (ledger, _) = LedgerWithValuation();
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();

            Assert.IsNull(ledger.Borrow(ids, "farm-biz-1", "", 50000, "terms", "reason",
                100, new HouseholdLedger(7), diagnostics),
                "There are no anonymous lenders.");
        }

        [Test]
        public void BuyOnCredit_CreatesPayableWithoutCashMoving()
        {
            var (ledger, _) = LedgerWithValuation();
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();

            BusinessLiability payable = ledger.BuyOnCredit(ids, "farm-biz-1", "Lumber Yard",
                2500, "coop lumber", 110, diagnostics);

            Assert.IsNotNull(payable);
            Assert.AreEqual(LiabilityKind.Payable, payable.Kind);
            Assert.AreEqual(2500, ledger.TotalLiabilitiesFor("farm-biz-1"));
        }

        [Test]
        public void Repay_ReducesBalanceAndRefusesOverpayment()
        {
            var (ledger, _) = LedgerWithValuation();
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var payerLedger = new HouseholdLedger(7);

            BusinessLiability loan = ledger.Borrow(ids, "farm-biz-1", "First Bank of Town",
                10000, "8% annual", "operating loan", 100, payerLedger, diagnostics);
            Assert.IsNotNull(loan);

            Assert.IsNotNull(ledger.Repay(loan.LiabilityId.ToString(), 15000, 200, payerLedger, diagnostics),
                "Overpayment must be refused — money does not vanish.");
            Assert.IsNull(ledger.Repay(loan.LiabilityId.ToString(), 4000, 200, payerLedger, diagnostics));
            Assert.AreEqual(6000, ledger.TotalLiabilitiesFor("farm-biz-1"));
            Assert.IsNull(ledger.Repay(loan.LiabilityId.ToString(), 6000, 210, payerLedger, diagnostics));
            Assert.AreEqual(0, ledger.TotalLiabilitiesFor("farm-biz-1"), "Settled liabilities drop out of the total.");
            Assert.IsTrue(loan.Settled);
        }

        [Test]
        public void AccrueInterest_IncreasesBalanceFromTerms()
        {
            var (ledger, _) = LedgerWithValuation();
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();

            BusinessLiability loan = ledger.Borrow(ids, "farm-biz-1", "First Bank of Town",
                10000, "8% annual", "operating loan", 100, new HouseholdLedger(7), diagnostics);
            Assert.IsNotNull(loan);

            Assert.IsNull(ledger.AccrueInterest(loan.LiabilityId.ToString(), 800, 465, diagnostics));
            Assert.AreEqual(10800, ledger.TotalLiabilitiesFor("farm-biz-1"),
                "Interest accrues from the recorded terms — the rate is never invented here.");
        }

        [Test]
        public void SaveRoundTrip_PreservesLiabilities()
        {
            var (ledger, _) = LedgerWithValuation();
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();

            ledger.Borrow(ids, "farm-biz-1", "First Bank of Town", 10000, "8% annual",
                "operating loan", 100, new HouseholdLedger(7), diagnostics);
            ledger.BuyOnCredit(ids, "farm-biz-1", "Lumber Yard", 2500, "coop lumber", 110, diagnostics);

            LiabilityLedgerSaveDto dto = ledger.CaptureSaveDto();

            var loaded = new BusinessLiabilityLedger();
            loaded.LoadFromSaveDto(dto);
            Assert.AreEqual(12500, loaded.TotalLiabilitiesFor("farm-biz-1"));
            Assert.AreEqual(2, loaded.All.Count);
        }
    }
}
