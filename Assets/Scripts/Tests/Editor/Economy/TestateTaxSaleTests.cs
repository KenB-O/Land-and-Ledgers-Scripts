using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Population;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.Economy.Estates.Tests
{
    /// <summary>
    /// NX-3C: testate succession (wills + probate) and property-tax sales.
    /// Canon §17.7 (staged tax collection); 1870s will/probate practice.
    /// </summary>
    public sealed class TestateTaxSaleTests
    {
        private List<string> diag;
        private EntityIdRegistry ids;
        private PopulationState population;
        private TitleAuthority titles;

        private static PersonState Person(int id, string name)
        {
            return new PersonState
            {
                id = id, firstName = name, lastName = "Test",
                age = 40, ageBand = AgeBand.Adult18Plus,
                laborAccessLevel = LaborAccessLevel.FullLaborMarket, householdId = 1,
            };
        }

        [SetUp]
        public void SetUp()
        {
            diag = new List<string>();
            ids = new EntityIdRegistry();
            population = new PopulationState();
            population.people.Add(Person(1, "Testator"));
            population.people.Add(Person(2, "Heir"));
            population.people.Add(Person(3, "Witness"));
            population.people.Add(Person(4, "Witness2"));
            population.people.Add(Person(5, "Executor"));
            titles = new TitleAuthority();
        }

        [Test]
        public void WillRequiresTwoWitnesses()
        {
            var probate = new ProbateService();
            var oneWitness = probate.RecordWill(ids, 1, "Testator", 5,
                new List<int> { 3 }, 10, population, diag);
            Assert.IsNull(oneWitness, "1870s practice: at least two attesting witnesses.");
        }

        [Test]
        public void InterestedWitnessHoldsBequest()
        {
            var probate = new ProbateService();
            var estates = new EstateService();
            // P2 is both witness and beneficiary — the classic interested witness.
            Will will = probate.RecordWill(ids, 1, "Testator", 5,
                new List<int> { 2, 3 }, 10, population, diag);
            Assert.IsNotNull(will);
            Assert.IsNull(probate.AddBequest(will, new Bequest
            {
                Kind = BequestKind.CashAmount, BeneficiaryPersonId = 2,
                BeneficiaryName = "Heir", CashCents = 1000,
            }, population, diag));

            titles.RegisterParcel("p-1", "lot", 1f, "Testator", TitleBasis.HomesteadClaim, 0, diag);
            Estate estate = estates.OpenEstate(ids, 1, "Testator", new KinshipRegistry(), 20, diag);
            Assert.IsNull(probate.ProbateWill(will, estate, titles, 20, diag));
            Assert.IsTrue(will.Bequests[0].HeldForWitnessConflict,
                "Interested witness: bequest held and flagged, not voided by fiat.");
        }

        [Test]
        public void ParcelBequestRequiresTestatorToHold()
        {
            var probate = new ProbateService();
            var estates = new EstateService();
            Will will = probate.RecordWill(ids, 1, "Testator", 5,
                new List<int> { 3, 4 }, 10, population, diag);
            probate.AddBequest(will, new Bequest
            {
                Kind = BequestKind.Parcel, BeneficiaryPersonId = 2,
                BeneficiaryName = "Heir", ParcelId = "p-other",
            }, population, diag);

            // The testator holds p-1, not p-other.
            titles.RegisterParcel("p-1", "lot", 1f, "Testator", TitleBasis.HomesteadClaim, 0, diag);
            titles.RegisterParcel("p-other", "lot", 1f, "Stranger", TitleBasis.HomesteadClaim, 0, diag);
            Estate estate = estates.OpenEstate(ids, 1, "Testator", new KinshipRegistry(), 20, diag);
            Assert.IsNull(probate.ProbateWill(will, estate, titles, 20, diag));
            Assert.IsTrue(will.Bequests[0].HeldForWitnessConflict,
                "A devise of land the testator never held conveys nothing — flagged.");
        }

        [Test]
        public void ContestedWillPausesDistribution()
        {
            var probate = new ProbateService();
            var estates = new EstateService();
            Will will = probate.RecordWill(ids, 1, "Testator", 5,
                new List<int> { 3, 4 }, 10, population, diag);
            probate.AddBequest(will, new Bequest
            {
                Kind = BequestKind.Parcel, BeneficiaryPersonId = 2,
                BeneficiaryName = "Heir", ParcelId = "p-1",
            }, population, diag);
            titles.RegisterParcel("p-1", "lot", 1f, "Testator", TitleBasis.HomesteadClaim, 0, diag);
            Estate estate = estates.OpenEstate(ids, 1, "Testator", new KinshipRegistry(), 20, diag);
            estates.RegisterDecedentParcel(estate, "p-1", titles, diag);
            Assert.IsNull(probate.ProbateWill(will, estate, titles, 20, diag));

            Assert.IsNull(probate.ContestWill(will, 2, "undue influence alleged", population, diag));
            Assert.IsTrue(will.Contested);
            string refused = probate.DistributeBequest(will, estate, will.Bequests[0], ids, titles, 21, diag);
            Assert.IsNotNull(refused, "Contested wills pause — never resolved by fiat.");
            Assert.AreEqual("Testator", titles.CurrentHolder("p-1"));
        }

        [Test]
        public void TestateDistributionTransfersTitle()
        {
            var probate = new ProbateService();
            var estates = new EstateService();
            Will will = probate.RecordWill(ids, 1, "Testator", 5,
                new List<int> { 3, 4 }, 10, population, diag);
            probate.AddBequest(will, new Bequest
            {
                Kind = BequestKind.Parcel, BeneficiaryPersonId = 2,
                BeneficiaryName = "Heir", ParcelId = "p-1",
            }, population, diag);
            titles.RegisterParcel("p-1", "lot", 1f, "Testator", TitleBasis.HomesteadClaim, 0, diag);
            Estate estate = estates.OpenEstate(ids, 1, "Testator", new KinshipRegistry(), 20, diag);
            estates.RegisterDecedentParcel(estate, "p-1", titles, diag);
            Assert.IsNull(probate.ProbateWill(will, estate, titles, 20, diag));
            Assert.AreEqual(will.WillId, estate.TestateWillId);

            Assert.IsNull(probate.DistributeBequest(will, estate, will.Bequests[0], ids, titles, 21, diag));
            Assert.AreEqual("Heir", titles.CurrentHolder("p-1"));
            Assert.IsTrue(will.Bequests[0].Distributed);
        }

        [Test]
        public void TaxSaleFollowsTheStagedProcess()
        {
            var tax = new PropertyTaxService();
            titles.RegisterParcel("p-tax", "40 acres", 40f, "Owner", TitleBasis.HomesteadClaim, 0, diag);

            TaxAssessment assessment = tax.LevyAssessment(ids, "p-tax", "Owner", 1878, 2000, 100, 100, 600, diag);
            Assert.IsNotNull(assessment);

            // Before the due date: nothing happens.
            tax.AdvanceDay(50, diag);
            Assert.AreEqual(TaxAssessmentStatus.Assessed, assessment.Status);

            // Past due: delinquent, penalties accrue — staged, not confiscation.
            tax.AdvanceDay(135, diag);
            Assert.AreEqual(TaxAssessmentStatus.Delinquent, assessment.Status);
            Assert.Greater(assessment.PenaltiesAccruedCents, 0);

            // Sale before the lien is refused — the stages are mandatory.
            var earlyBids = new List<TaxSaleBid> { new TaxSaleBid { BidderName = "Buyer", AmountCents = 5000 } };
            Assert.IsNotNull(tax.ConductTaxSale(assessment, earlyBids, null, 200, 140, ids, titles, null, diag));

            // Lien, then sale: 5000 bid, 200 costs → 4800. Taxes owed exceed
            // that, so the county takes all 4800 and a claim survives honestly.
            var credit = new Financing.CreditRegistry();
            Assert.IsNull(tax.FileTaxLien(assessment, ids, credit, 140, diag));
            Assert.IsNull(tax.ConductTaxSale(assessment, earlyBids, null, 200, 150, ids, titles, credit, diag));
            Assert.AreEqual("Buyer", titles.CurrentHolder("p-tax"));
            Assert.AreEqual(1, tax.SaleIds.Count);
            Assert.AreEqual(4800, tax.GetSale(tax.SaleIds[0]).TaxesRecoveredCents);
        }

        [Test]
        public void TaxRedemptionRestoresTitle()
        {
            var tax = new PropertyTaxService();
            titles.RegisterParcel("p-red", "lot", 1f, "Owner", TitleBasis.HomesteadClaim, 0, diag);
            // Small levy so the bid covers everything and leaves a surplus.
            TaxAssessment assessment = tax.LevyAssessment(ids, "p-red", "Owner", 1878, 1000, 100, 100, 600, diag);
            tax.AdvanceDay(135, diag);
            var credit = new Financing.CreditRegistry();
            tax.FileTaxLien(assessment, ids, credit, 140, diag);
            var bids = new List<TaxSaleBid> { new TaxSaleBid { BidderName = "Buyer", AmountCents = 5000 } };
            Assert.IsNull(tax.ConductTaxSale(assessment, bids, null, 200, 150, ids, titles, credit, diag));
            string saleId = tax.SaleIds[0];
            TaxSale sale = tax.GetSale(saleId);
            Assert.Greater(sale.SurplusToOwnerCents, 0, "Surplus goes to the owner — never kept.");

            Assert.IsNull(tax.Redeem(saleId, 5200, 200, ids, titles, diag));
            Assert.AreEqual("Owner", titles.CurrentHolder("p-red"));
            Assert.IsTrue(sale.Redeemed);
        }
    }
}
