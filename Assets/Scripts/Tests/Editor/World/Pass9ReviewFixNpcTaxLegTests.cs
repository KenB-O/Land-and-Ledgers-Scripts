using System.Collections.Generic;
using LandLedgers.Civic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>Pass IX review R-07: the NPC tax leg gets real production
    /// inputs — a closed acquisition registers its holding, the daily drive
    /// levies the annual assessment into the service's own tax book, and
    /// that book persists across save/load so the audit can pay collector
    /// and treasury after a reload.</summary>
    [TestFixture]
    public sealed class Pass9ReviewFixNpcTaxLegTests
    {
        private sealed class Fixture
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public FinancialObligationAuthority Authority = new FinancialObligationAuthority();
            public CreditCashBridge Cash = new CreditCashBridge();
            public NpcPropertyLifeService Life = new NpcPropertyLifeService();
            public List<string> Diag = new List<string>();

            public HouseholdLedger RegisterHousehold(int householdId, int cashCents)
            {
                var ledger = new HouseholdLedger(householdId);
                Assert.IsNull(ledger.RecordInflow(0, cashCents, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
                Assert.IsNull(Cash.Register("household:" + householdId,
                    new HouseholdCashStore(householdId, ledger)));
                return ledger;
            }

            public PurseCashStore RegisterPurse(string owner, int balanceCents)
            {
                var purse = new PurseCashStore(owner, balanceCents, "test seed capital");
                Assert.IsNull(Cash.Register(owner, purse));
                return purse;
            }
        }

        [Test]
        public void ClosedAcquisition_RegistersTheHolding()
        {
            var f = new Fixture();
            var titles = new TitleAuthority();
            var housing = new HousingAuthority();
            Assert.IsNotNull(titles.RegisterParcel("parcel-1", "farmstead", 40f, "Abel",
                TitleBasis.Purchase, 0, f.Diag));
            Building building = housing.RegisterBuilding("bld-parcel-1", "parcel-1", "house", 0, "test farmstead", f.Diag);
            Assert.IsNotNull(building);
            AccommodationSpace space = housing.DefineSpace(building.BuildingId, 4, f.Diag);
            Assert.IsNotNull(space);
            f.RegisterHousehold(7, 150000);
            f.RegisterPurse("Abel", 0);

            var acquisition = new NpcPropertyAcquisitionService { PropertyLife = f.Life };
            var plan = new NpcAcquisitionPlan
            {
                PlanId = "plan-1",
                BuyerHouseholdId = 7,
                BuyerName = "household:7",
                SellerName = "Abel",
                ParcelId = "parcel-1",
                SpaceId = space.SpaceId,
                OccupantPersonIds = new List<int> { 101 },
                AgreedPriceCents = 100000,
                CashDownCents = 100000,
            };

            NpcAcquisitionResult result = acquisition.CloseAcquisition(
                f.Ids, plan, f.Authority, new CreditRegistry(), f.Cash, titles, housing,
                new SellerFinanceNegotiationBook(), new SellerFinanceClosingService(),
                new NpcCreditDecisionEngine(), new CreditOfferWorkflow(), 3, new CreditEventLog(), f.Diag);

            Assert.IsTrue(result.Closed, result.FailureReason);
            Assert.AreEqual(1, f.Life.Holdings.Count, "the closing registers the acquired holding");
            NpcPropertyHolding holding = null;
            foreach (NpcPropertyHolding candidate in f.Life.Holdings) holding = candidate;
            Assert.IsNotNull(holding);
            Assert.AreEqual("parcel-1", holding.ParcelId);
            Assert.AreEqual(7, holding.OwnerHouseholdId);
            Assert.AreEqual(100000, holding.AcquisitionPriceCents);
            Assert.AreEqual(NpcPropertyHoldingKind.OwnerOccupied, holding.Kind);
        }

        [Test]
        public void Drive_LeviesExactlyOneAssessmentPerHoldingPerTaxYear()
        {
            var f = new Fixture();
            // No cash stores registered: levies land, the audit leaves them
            // standing as arrears — the levy count is what this test pins.
            NpcPropertyHolding holding = f.Life.RegisterHolding("parcel-1", "bld-1", 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, f.Diag);
            var treasury = new CivicTreasuryService();

            NpcPropertyLifeReport day10 = f.Life.DriveNpcPropertyLife(
                f.Cash, f.Life.Taxes, f.Authority, treasury, 10, f.Diag, null, f.Ids);
            Assert.AreEqual(1, day10.TaxesLevied, "first drive of tax year 0 levies");
            Assert.AreEqual(1, f.Life.Taxes.AllAssessments.Count);
            TaxAssessment year0 = f.Life.Taxes.AllAssessments[0];
            Assert.AreEqual(0, year0.TaxYear);
            Assert.AreEqual(PropertyTaxCalibration.ComputeLevyCents(100000), year0.AmountCents);
            Assert.AreEqual(1, holding.TaxAssessmentIds.Count);

            NpcPropertyLifeReport day20 = f.Life.DriveNpcPropertyLife(
                f.Cash, f.Life.Taxes, f.Authority, treasury, 20, f.Diag, null, f.Ids);
            Assert.AreEqual(0, day20.TaxesLevied, "same tax year — never levied twice");
            Assert.AreEqual(1, f.Life.Taxes.AllAssessments.Count);

            NpcPropertyLifeReport year1 = f.Life.DriveNpcPropertyLife(
                f.Cash, f.Life.Taxes, f.Authority, treasury, 375, f.Diag, null, f.Ids);
            Assert.AreEqual(1, year1.TaxesLevied, "the next tax year levies once more");
            Assert.AreEqual(2, f.Life.Taxes.AllAssessments.Count);
            Assert.IsTrue(f.Life.Taxes.HasAssessmentForParcelYear("parcel-1", 1));
        }

        [Test]
        public void Drive_LeviedAssessment_AuditPaysTreasuryExactlyOnce()
        {
            var f = new Fixture();
            HouseholdLedger owner = f.RegisterHousehold(7, 10000);
            f.RegisterPurse("County Tax Collector", 0);
            f.Life.RegisterHolding("parcel-1", "bld-1", 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, f.Diag);
            var treasury = new CivicTreasuryService();

            NpcPropertyLifeReport report = f.Life.DriveNpcPropertyLife(
                f.Cash, f.Life.Taxes, f.Authority, treasury, 5, f.Diag, null, f.Ids);

            int levy = PropertyTaxCalibration.ComputeLevyCents(100000);
            Assert.AreEqual(1, report.TaxesLevied);
            Assert.AreEqual(levy, report.TaxesPaidCents, "the funded owner pays the drive-levied assessment");
            Assert.AreEqual(levy, report.TreasuryCreditedCents, "no penalties accrued — the whole payment is principal to the treasury");
            Assert.AreEqual(levy, treasury.BalanceCents);
            Assert.AreEqual(10000 - levy, owner.GetBalanceCents(), "owner debit == treasury credit");
        }

        [Test]
        public void SaveLoad_PreservesTheTaxBook_AuditStillPaysExactlyOnce()
        {
            var f = new Fixture();
            HouseholdLedger owner = f.RegisterHousehold(7, 10000);
            PurseCashStore collector = f.RegisterPurse("County Tax Collector", 0);
            NpcPropertyHolding holding = f.Life.RegisterHolding("parcel-1", "bld-1", 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, f.Diag);
            TaxAssessment assessment = f.Life.LevyPropertyTax(f.Life.Taxes, f.Ids, holding,
                0, 1200, 60, 100, 600, 5, f.Diag);
            Assert.IsNotNull(assessment);
            assessment.PenaltiesAccruedCents = 100;

            NpcPropertyLifeService.NpcPropertyLifeSaveDto dto = f.Life.CaptureSaveDto();
            Assert.IsNotNull(dto.Taxes, "the DTO carries the tax book");
            Assert.AreEqual(1, dto.Taxes.Assessments.Count);

            var restored = new NpcPropertyLifeService();
            restored.LoadFromSaveDto(dto, f.Diag);
            Assert.AreEqual(1, restored.Holdings.Count);
            Assert.AreEqual(1, restored.Taxes.AllAssessments.Count, "the tax book survives the round-trip");
            TaxAssessment restoredAssessment = restored.Taxes.GetAssessment(assessment.AssessmentId);
            Assert.IsNotNull(restoredAssessment);
            Assert.AreEqual(1200 + 100, restoredAssessment.TotalOwedCents());

            var treasury = new CivicTreasuryService();
            int credited = NpcPropertyLifeService.AuditTaxes(restored.Taxes, f.Cash,
                restored.Holdings, f.Authority, treasury, 70, f.Diag, out int paid);
            Assert.AreEqual(1300, paid);
            Assert.AreEqual(1200, credited, "principal to the treasury after reload");
            Assert.AreEqual(100, collector.ReadBalanceCents(), "fees to the collector after reload");
            Assert.AreEqual(10000 - 1300, owner.GetBalanceCents());

            int creditedAgain = NpcPropertyLifeService.AuditTaxes(restored.Taxes, f.Cash,
                restored.Holdings, f.Authority, treasury, 71, f.Diag, out int paidAgain);
            Assert.AreEqual(0, creditedAgain);
            Assert.AreEqual(0, paidAgain);
        }
    }
}
