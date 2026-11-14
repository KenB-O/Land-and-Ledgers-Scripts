using System;
using System.Collections.Generic;
using LandLedgers.Civic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>R31 (A1 plan 29): the NPC tax delinquency clock runs in the
    /// daily drive and the audit pays only at/past the assessment due date.
    /// The drive advances the tax book exactly once per drive, then files a
    /// senior tax lien per delinquent assessment through the real credit
    /// registry; paying a LienFiled assessment in full releases the lien,
    /// a partial payment leaves it Active; with no named buyer a distress
    /// sale refuses honestly and no tax sale is faked. All run in the
    /// Roslyn harness. Worked figures: 15,000c levy, due day 100, drive
    /// day 200 — 100 days overdue, 3 months, penalty 450c, daily interest
    /// round((15000 + 450) * 0.06 / 365) = 3c, lien 15,453c.</summary>
    [TestFixture]
    public sealed class R31DelinquencyClockTests
    {
        private sealed class Fixture
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public FinancialObligationAuthority Authority = new FinancialObligationAuthority();
            public CreditOfferWorkflow Workflow = new CreditOfferWorkflow();
            public CreditRegistry Registry = new CreditRegistry();
            public CreditCashBridge Cash = new CreditCashBridge();
            public TitleAuthority Titles = new TitleAuthority();
            public HousingAuthority Housing = new HousingAuthority();
            public PropertyTaxService Taxes = new PropertyTaxService();
            public CreditWorkoutService Workout = new CreditWorkoutService();
            public ForeclosureService Foreclosure = new ForeclosureService();
            public CreditEventLog Events = new CreditEventLog();
            public NpcPropertyLifeService Life = new NpcPropertyLifeService();
            public List<string> Diag = new List<string>();

            public Fixture()
            {
                Registry.AttachFinancialAuthority(Authority);
            }

            public PurseCashStore RegisterPurse(string owner, int balanceCents)
            {
                var purse = new PurseCashStore(owner, balanceCents, "test seed capital");
                Assert.IsNull(Cash.Register(owner, purse));
                return purse;
            }

            public HouseholdLedger RegisterHousehold(int householdId, int cashCents)
            {
                var ledger = new HouseholdLedger(householdId);
                Assert.IsNull(ledger.RecordInflow(0, cashCents, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
                Assert.IsNull(Cash.Register("household:" + householdId,
                    new HouseholdCashStore(householdId, ledger)));
                return ledger;
            }

            public NpcPropertyHolding RegisterOwnedHouse(string parcelId, int ownerHouseholdId)
            {
                Assert.IsNotNull(Titles.RegisterParcel(parcelId, "house parcel", 5f,
                    "household:" + ownerHouseholdId, TitleBasis.Purchase, 0, Diag));
                Building building = Housing.RegisterBuilding("bld-" + parcelId, parcelId,
                    "house", 0, "test house", Diag);
                Assert.IsNotNull(building);
                Assert.IsNotNull(Housing.DefineSpace(building.BuildingId, 4, Diag));
                return Life.RegisterHolding(parcelId, building.BuildingId, ownerHouseholdId,
                    0, 100000, "", "", NpcPropertyHoldingKind.OwnerOccupied, Diag);
            }
        }

        private static bool DiagContains(List<string> diag, string needle)
        {
            foreach (string line in diag)
                if (line != null && line.IndexOf(needle, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        [Test]
        public void Clock_AdvancesPastDue_FilesLienWithPenaltyAndInterest()
        {
            var f = new Fixture();
            NpcPropertyHolding holding = f.RegisterOwnedHouse("parcel-1", 7);
            TaxAssessment assessment = f.Life.LevyPropertyTax(f.Life.Taxes, f.Ids, holding,
                0, 15000, 100, 100, 600, 10, f.Diag);
            Assert.IsNotNull(assessment);
            var treasury = new CivicTreasuryService();

            NpcPropertyLifeReport report = f.Life.DriveNpcPropertyLife(
                f.Cash, f.Life.Taxes, f.Authority, treasury, 200, f.Diag, null, f.Ids, f.Registry);

            Assert.AreEqual(TaxAssessmentStatus.LienFiled, assessment.Status);
            Assert.AreEqual(450, assessment.PenaltiesAccruedCents);
            Assert.AreEqual(3, assessment.InterestAccruedCents);
            Assert.AreEqual(1, report.LiensFiled);
            Assert.AreEqual(1, report.TaxSalesAwaitingBidders);
        }

        [Test]
        public void LienFiled_IsRecordedInTheCreditRegistry()
        {
            var f = new Fixture();
            NpcPropertyHolding holding = f.RegisterOwnedHouse("parcel-1", 7);
            TaxAssessment assessment = f.Life.LevyPropertyTax(f.Life.Taxes, f.Ids, holding,
                0, 15000, 100, 100, 600, 10, f.Diag);
            Assert.IsNotNull(assessment);
            var treasury = new CivicTreasuryService();

            f.Life.DriveNpcPropertyLife(
                f.Cash, f.Life.Taxes, f.Authority, treasury, 200, f.Diag, null, f.Ids, f.Registry);

            Assert.IsFalse(string.IsNullOrWhiteSpace(assessment.TaxLienInstrumentId));
            Assert.AreEqual(1, f.Registry.InstrumentCount);
            CreditRegistry.CreditRegistrySaveDto dto = f.Registry.CaptureSaveDto();
            Assert.AreEqual(1, dto.Liens.Count);
            Assert.AreEqual("County Tax Collector", dto.Liens[0].ClaimantName);
            Assert.AreEqual(15453, dto.Liens[0].AmountCents);
            Assert.AreEqual(CreditInstrumentStatus.Active, dto.Liens[0].Status);
            Assert.IsTrue(holding.Decisions.Count > 1, "the filing is in the holding's observable history.");
        }

        [Test]
        public void MissingRegistry_ClockStillAdvances_LienRefused()
        {
            var f = new Fixture();
            NpcPropertyHolding holding = f.RegisterOwnedHouse("parcel-1", 7);
            TaxAssessment assessment = f.Life.LevyPropertyTax(f.Life.Taxes, f.Ids, holding,
                0, 15000, 100, 100, 600, 10, f.Diag);
            Assert.IsNotNull(assessment);
            var treasury = new CivicTreasuryService();

            NpcPropertyLifeReport report = f.Life.DriveNpcPropertyLife(
                f.Cash, f.Life.Taxes, f.Authority, treasury, 200, f.Diag);

            Assert.AreEqual(TaxAssessmentStatus.Delinquent, assessment.Status);
            Assert.AreEqual(0, report.LiensFiled);
            Assert.AreEqual(0, report.TaxSalesAwaitingBidders);
            Assert.AreEqual(0, report.TaxesPaidCents);
            Assert.IsTrue(DiagContains(f.Diag,
                "NpcPropertyLife.Drive: NpcPropertyLife.FileTaxLienIfDelinquent: " +
                "PropertyTaxService.FileTaxLien: the credit registry is required"));
        }

        [Test]
        public void DueDateGuard_BoundaryAtDuePays()
        {
            var f = new Fixture();
            HouseholdLedger owner = f.RegisterHousehold(7, 10000);
            PurseCashStore collector = f.RegisterPurse("County Tax Collector", 0);
            NpcPropertyHolding holding = f.RegisterOwnedHouse("parcel-1", 7);
            TaxAssessment assessment = f.Life.LevyPropertyTax(f.Life.Taxes, f.Ids, holding,
                0, 1200, 100, 100, 600, 10, f.Diag);
            Assert.IsNotNull(assessment);
            var treasury = new CivicTreasuryService();

            NpcPropertyLifeReport day50 = f.Life.DriveNpcPropertyLife(
                f.Cash, f.Life.Taxes, f.Authority, treasury, 50, f.Diag, null, f.Ids, f.Registry);
            Assert.AreEqual(0, day50.TaxesLevied, "the day-50 drive levies nothing — year 0 is already assessed");
            Assert.AreEqual(0, day50.TaxesPaidCents, "not yet due — the audit pays nothing");
            Assert.AreEqual(0, treasury.BalanceCents);
            Assert.AreEqual(0, assessment.AmountPaidCents);
            Assert.AreEqual(TaxAssessmentStatus.Assessed, assessment.Status);

            NpcPropertyLifeReport day100 = f.Life.DriveNpcPropertyLife(
                f.Cash, f.Life.Taxes, f.Authority, treasury, 100, f.Diag, null, f.Ids, f.Registry);
            Assert.AreEqual(1200, day100.TaxesPaidCents, "at-due pays in full");
            Assert.AreEqual(1200, treasury.BalanceCents);
            Assert.AreEqual(TaxAssessmentStatus.Satisfied, assessment.Status);
            Assert.AreEqual(10000 - 1200, owner.GetBalanceCents());
            Assert.AreEqual(0, collector.ReadBalanceCents(), "no fees accrued — the whole payment is principal");
        }

        [Test]
        public void AuditPaymentInFull_ReleasesTheLien()
        {
            var f = new Fixture();
            HouseholdLedger owner = f.RegisterHousehold(7, 20000);
            PurseCashStore collector = f.RegisterPurse("County Tax Collector", 0);
            NpcPropertyHolding holding = f.RegisterOwnedHouse("parcel-1", 7);
            TaxAssessment assessment = f.Life.LevyPropertyTax(f.Life.Taxes, f.Ids, holding,
                0, 15000, 100, 100, 600, 10, f.Diag);
            Assert.IsNotNull(assessment);
            var treasury = new CivicTreasuryService();

            NpcPropertyLifeReport day200 = f.Life.DriveNpcPropertyLife(
                f.Cash, f.Life.Taxes, f.Authority, treasury, 200, f.Diag, null, f.Ids, f.Registry);
            Assert.AreEqual(TaxAssessmentStatus.Satisfied, assessment.Status);
            Assert.AreEqual(1, day200.LiensFiled);
            Assert.AreEqual(1, day200.TaxSalesAwaitingBidders);
            Assert.AreEqual(15000, treasury.BalanceCents);
            Assert.AreEqual(453, collector.ReadBalanceCents());
            Assert.AreEqual(CreditInstrumentStatus.Satisfied,
                f.Registry.CaptureSaveDto().Liens[0].Status);
            Assert.AreEqual(1, f.Registry.InstrumentCount);
            Assert.AreEqual(20000 - 15453, owner.GetBalanceCents());

            NpcPropertyLifeReport day201 = f.Life.DriveNpcPropertyLife(
                f.Cash, f.Life.Taxes, f.Authority, treasury, 201, f.Diag, null, f.Ids, f.Registry);
            Assert.AreEqual(0, day201.LiensFiled, "a satisfied assessment files no second lien");
            Assert.AreEqual(1, f.Registry.InstrumentCount);
        }

        [Test]
        public void AuditPartialPayment_KeepsTheLienActive()
        {
            var f = new Fixture();
            HouseholdLedger owner = f.RegisterHousehold(7, 5000);
            PurseCashStore collector = f.RegisterPurse("County Tax Collector", 0);
            NpcPropertyHolding holding = f.RegisterOwnedHouse("parcel-1", 7);
            TaxAssessment assessment = f.Life.LevyPropertyTax(f.Life.Taxes, f.Ids, holding,
                0, 15000, 100, 100, 600, 10, f.Diag);
            Assert.IsNotNull(assessment);
            var treasury = new CivicTreasuryService();

            NpcPropertyLifeReport day200 = f.Life.DriveNpcPropertyLife(
                f.Cash, f.Life.Taxes, f.Authority, treasury, 200, f.Diag, null, f.Ids, f.Registry);
            Assert.AreEqual(TaxAssessmentStatus.LienFiled, assessment.Status);
            Assert.AreEqual(1, day200.LiensFiled);
            Assert.AreEqual(4547, treasury.BalanceCents);
            Assert.AreEqual(453, collector.ReadBalanceCents());
            Assert.AreEqual(0, owner.GetBalanceCents());
            Assert.AreEqual(CreditInstrumentStatus.Active,
                f.Registry.CaptureSaveDto().Liens[0].Status);

            NpcPropertyLifeReport day201 = f.Life.DriveNpcPropertyLife(
                f.Cash, f.Life.Taxes, f.Authority, treasury, 201, f.Diag, null, f.Ids, f.Registry);
            Assert.AreEqual(0, day201.LiensFiled, "no duplicate filing on the next drive");
            Assert.AreEqual(1, day201.TaxSalesAwaitingBidders, "the lien still stands");
            Assert.AreEqual(1, f.Registry.InstrumentCount);
            Assert.AreEqual(4547, treasury.BalanceCents, "no second payment — the owner is empty");
        }

        [Test]
        public void DistressSale_WithNoNamedBuyer_RefusesHonestly()
        {
            var f = new Fixture();
            NpcPropertyHolding holding = f.RegisterOwnedHouse("parcel-1", 7);
            TaxAssessment assessment = f.Life.LevyPropertyTax(f.Life.Taxes, f.Ids, holding,
                0, 1200, 100, 100, 600, 10, f.Diag);
            Assert.IsNotNull(assessment);
            var treasury = new CivicTreasuryService();
            var saleContext = new NpcDistressSaleContext
            {
                Ids = f.Ids, Authority = f.Authority, Cash = f.Cash, Titles = f.Titles,
                Housing = f.Housing, Workflow = new CreditOfferWorkflow(), Events = new CreditEventLog(),
                TaxService = f.Life.Taxes, BuyerHouseholdId = -1, SalePriceCents = 90000,
            };

            NpcPropertyLifeReport report = f.Life.DriveNpcPropertyLife(
                f.Cash, f.Life.Taxes, f.Authority, treasury, 200, f.Diag, saleContext, f.Ids, f.Registry);

            Assert.AreEqual(1, report.SalesAttempted);
            Assert.IsFalse(report.SaleCompleted);
            Assert.IsTrue(report.SaleNote != null &&
                report.SaleNote.IndexOf("no real, different buyer household named", StringComparison.Ordinal) >= 0,
                report.SaleNote);
            Assert.AreEqual("household:7", f.Titles.CurrentHolder("parcel-1"));
            Assert.AreEqual(0, f.Life.Taxes.SaleIds.Count, "no tax sale faked");
            Assert.AreEqual(1, report.TaxSalesAwaitingBidders);
            Assert.AreEqual(1, report.LiensFiled);
        }

        [Test]
        public void SaveLoad_PreservesLien_NoDuplicateFiling()
        {
            var f = new Fixture();
            NpcPropertyHolding holding = f.RegisterOwnedHouse("parcel-1", 7);
            TaxAssessment assessment = f.Life.LevyPropertyTax(f.Life.Taxes, f.Ids, holding,
                0, 1200, 100, 100, 600, 10, f.Diag);
            Assert.IsNotNull(assessment);
            var treasury = new CivicTreasuryService();

            NpcPropertyLifeReport day200 = f.Life.DriveNpcPropertyLife(
                f.Cash, f.Life.Taxes, f.Authority, treasury, 200, f.Diag, null, f.Ids, f.Registry);
            Assert.AreEqual(TaxAssessmentStatus.LienFiled, assessment.Status);
            Assert.AreEqual(1, f.Registry.InstrumentCount);

            NpcPropertyLifeService.NpcPropertyLifeSaveDto dto = f.Life.CaptureSaveDto();
            CreditRegistry.CreditRegistrySaveDto registryDto = f.Registry.CaptureSaveDto();
            var restored = new NpcPropertyLifeService();
            restored.LoadFromSaveDto(dto, f.Diag);
            var restoredRegistry = new CreditRegistry();
            restoredRegistry.LoadFromSaveDto(registryDto);

            TaxAssessment restoredAssessment = restored.Taxes.GetAssessment(assessment.AssessmentId);
            Assert.IsNotNull(restoredAssessment);
            Assert.AreEqual(TaxAssessmentStatus.LienFiled, restoredAssessment.Status);
            Assert.AreEqual(assessment.TaxLienInstrumentId, restoredAssessment.TaxLienInstrumentId);
            Assert.AreEqual(1, restoredRegistry.InstrumentCount);
            Assert.AreEqual(CreditInstrumentStatus.Active,
                restoredRegistry.CaptureSaveDto().Liens[0].Status);

            NpcPropertyLifeReport day201 = restored.DriveNpcPropertyLife(
                f.Cash, restored.Taxes, f.Authority, treasury, 201, f.Diag, null, f.Ids, restoredRegistry);
            Assert.AreEqual(0, day201.LiensFiled, "the restored lien is not filed twice");
            Assert.AreEqual(1, restoredRegistry.InstrumentCount);
            Assert.AreEqual(1, day201.TaxSalesAwaitingBidders);
            Assert.AreEqual(TaxAssessmentStatus.LienFiled, restoredAssessment.Status);
        }

        [Test]
        public void ClockAdvancesExactlyOncePerDrive_AcrossTwoHoldings()
        {
            var f = new Fixture();
            NpcPropertyHolding first = f.RegisterOwnedHouse("parcel-1", 7);
            NpcPropertyHolding second = f.RegisterOwnedHouse("parcel-2", 7);
            TaxAssessment firstAssessment = f.Life.LevyPropertyTax(f.Life.Taxes, f.Ids, first,
                0, 15000, 100, 100, 600, 10, f.Diag);
            TaxAssessment secondAssessment = f.Life.LevyPropertyTax(f.Life.Taxes, f.Ids, second,
                0, 15000, 100, 100, 600, 10, f.Diag);
            Assert.IsNotNull(firstAssessment);
            Assert.IsNotNull(secondAssessment);
            var treasury = new CivicTreasuryService();

            NpcPropertyLifeReport report = f.Life.DriveNpcPropertyLife(
                f.Cash, f.Life.Taxes, f.Authority, treasury, 200, f.Diag, null, f.Ids, f.Registry);

            Assert.AreEqual(TaxAssessmentStatus.LienFiled, firstAssessment.Status);
            Assert.AreEqual(TaxAssessmentStatus.LienFiled, secondAssessment.Status);
            Assert.AreEqual(450, firstAssessment.PenaltiesAccruedCents);
            Assert.AreEqual(3, firstAssessment.InterestAccruedCents);
            Assert.AreEqual(450, secondAssessment.PenaltiesAccruedCents);
            Assert.AreEqual(3, secondAssessment.InterestAccruedCents,
                "one advance per drive — not once per holding");
            Assert.AreEqual(2, report.LiensFiled);
        }

        [Test]
        public void DirectPayment_ReleasesTheLien()
        {
            var f = new Fixture();
            HouseholdLedger owner = f.RegisterHousehold(7, 10000);
            PurseCashStore collector = f.RegisterPurse("County Tax Collector", 0);
            NpcPropertyHolding holding = f.RegisterOwnedHouse("parcel-1", 7);
            TaxAssessment assessment = f.Life.LevyPropertyTax(f.Life.Taxes, f.Ids, holding,
                0, 1200, 100, 100, 600, 10, f.Diag);
            Assert.IsNotNull(assessment);

            // The Phase F direct-call shape: delinquency at day 200 files the lien.
            Assert.IsNull(f.Life.ProcessTaxDelinquency(f.Life.Taxes, f.Registry, f.Ids, holding,
                assessment, 200, f.Diag));
            Assert.AreEqual(TaxAssessmentStatus.LienFiled, assessment.Status);
            // 1200c levy + 36c penalty (3 months overdue) + 0c interest = 1236c owed.
            // Paying the levied 1200c alone would leave 36c owed and the lien
            // standing, so the test pays the full owed amount (plan deviation,
            // see the checkpoint entry — the plan's literal 1200c cannot satisfy).
            Assert.AreEqual(1236, assessment.TotalOwedCents());
            int owed = assessment.TotalOwedCents();

            Assert.IsNull(f.Life.PayPropertyTax(f.Life.Taxes, f.Cash, holding, assessment,
                owed, 200, f.Diag, f.Registry));
            Assert.AreEqual(TaxAssessmentStatus.Satisfied, assessment.Status);
            Assert.AreEqual(CreditInstrumentStatus.Satisfied,
                f.Registry.CaptureSaveDto().Liens[0].Status);
            Assert.AreEqual(10000 - owed, owner.GetBalanceCents());
            Assert.AreEqual(owed, collector.ReadBalanceCents());
        }
    }
}
