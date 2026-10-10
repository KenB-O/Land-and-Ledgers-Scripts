using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// Phase F (F4): post-acquisition property life — tax levied and paid
    /// from real cash (delinquency → senior lien on the real path),
    /// maintenance funded or degrading, renting out with real collection,
    /// and resale with an outstanding seller note settled or assumed (never
    /// magically restored). All run in the Roslyn harness.
    /// </summary>
    [TestFixture]
    public sealed class PhaseFNpcPropertyLifeTests
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

            public NpcPropertyHolding RegisterOwnedHouse(string parcelId, int ownerHouseholdId,
                string sellerNoteObligationId = "")
            {
                Assert.IsNotNull(Titles.RegisterParcel(parcelId, "house parcel", 5f,
                    "household:" + ownerHouseholdId, TitleBasis.Purchase, 0, Diag));
                Building building = Housing.RegisterBuilding("bld-" + parcelId, parcelId,
                    "house", 0, "test house", Diag);
                Assert.IsNotNull(building);
                Assert.IsNotNull(Housing.DefineSpace(building.BuildingId, 4, Diag));
                return Life.RegisterHolding(parcelId, building.BuildingId, ownerHouseholdId,
                    0, 100000, sellerNoteObligationId, "", NpcPropertyHoldingKind.OwnerOccupied, Diag);
            }

            public FinancialObligation SellerNote(string parcelId, int debtorHouseholdId,
                string creditorName, int principalCents)
            {
                FinancialObligation note = Authority.Create(Ids, FinancialObligationKind.SellerFinance,
                    "household:" + debtorHouseholdId, creditorName, principalCents, 0,
                    "600bps, 720 days", "seller-financed purchase of " + parcelId);
                Assert.IsNotNull(note);
                Assert.IsNotNull(Authority.AttachSecurity(Ids, note.ObligationId, creditorName,
                    "household:" + debtorHouseholdId, new[] { parcelId }, 0, 1, true));
                return note;
            }
        }

        [Test]
        public void PropertyTax_Levied_And_PaidFromRealCash()
        {
            var f = new Fixture();
            HouseholdLedger ledger = f.RegisterHousehold(7, 10000);
            PurseCashStore collector = f.RegisterPurse("County Tax Collector", 0);
            NpcPropertyHolding holding = f.RegisterOwnedHouse("parcel-1", 7);

            TaxAssessment assessment = f.Life.LevyPropertyTax(f.Taxes, f.Ids, holding,
                2026, 1200, 100, 100, 600, 10, f.Diag);
            Assert.IsNotNull(assessment);
            Assert.AreEqual(TaxAssessmentStatus.Assessed, assessment.Status);
            Assert.IsTrue(holding.TaxAssessmentIds.Contains(assessment.AssessmentId));

            Assert.IsNull(f.Life.PayPropertyTax(f.Taxes, f.Cash, holding, assessment, 1200, 50, f.Diag));
            Assert.AreEqual(TaxAssessmentStatus.Satisfied, assessment.Status);
            Assert.AreEqual(10000 - 1200, ledger.GetBalanceCents());
            Assert.AreEqual(1200, collector.ReadBalanceCents());
        }

        [Test]
        public void PropertyTax_Delinquency_FilesSeniorLien_RealEnforcementPath()
        {
            var f = new Fixture();
            f.RegisterHousehold(7, 10000);
            f.RegisterPurse("County Tax Collector", 0);
            NpcPropertyHolding holding = f.RegisterOwnedHouse("parcel-1", 7);

            TaxAssessment assessment = f.Life.LevyPropertyTax(f.Taxes, f.Ids, holding,
                2026, 1200, 100, 100, 600, 10, f.Diag);
            Assert.IsNotNull(assessment);

            // Day 200, still unpaid: AdvanceDay marks it delinquent and the
            // service files the SENIOR tax lien — the real enforcement path.
            Assert.IsNull(f.Life.ProcessTaxDelinquency(f.Taxes, f.Registry, f.Ids, holding,
                assessment, 200, f.Diag));
            Assert.AreEqual(TaxAssessmentStatus.LienFiled, assessment.Status);
            Assert.IsFalse(string.IsNullOrWhiteSpace(assessment.TaxLienInstrumentId));
            Assert.IsTrue(holding.Decisions.Count > 1, "the lien is in the holding's observable history.");
        }

        [Test]
        public void Maintenance_FundedOrDegrades()
        {
            var f = new Fixture();
            HouseholdLedger ledger = f.RegisterHousehold(7, 20000);
            f.RegisterPurse("maintenance:parcel-1", 0);
            NpcPropertyHolding holding = f.RegisterOwnedHouse("parcel-1", 7);

            NpcPropertyMaintenanceNeed roof = f.Life.ReportMaintenanceNeed(holding,
                "replace rotted roof boards", 5000, 10, f.Diag);
            Assert.IsNotNull(roof);
            Assert.IsNull(f.Life.FundMaintenance(f.Cash, holding, roof.NeedId, 20, f.Diag));
            Assert.IsTrue(roof.Funded);
            Assert.AreEqual(20000 - 5000, ledger.GetBalanceCents());

            NpcPropertyMaintenanceNeed porch = f.Life.ReportMaintenanceNeed(holding,
                "rehang porch door", 800, 10, f.Diag);
            Assert.IsNotNull(porch);
            float before = holding.Condition01;
            f.Life.DegradeForNeglect(holding, 200, f.Diag); // 190 days neglected > 90-day threshold
            Assert.Less(holding.Condition01, before, "neglect must degrade the holding, observably.");
            Assert.IsFalse(porch.Funded);
        }

        [Test]
        public void RentOut_BillCollect_ArrearsStayReal()
        {
            var f = new Fixture();
            HouseholdLedger landlordLedger = f.RegisterHousehold(7, 10000);
            HouseholdLedger tenantLedger = f.RegisterHousehold(9, 5000);
            NpcPropertyHolding holding = f.RegisterOwnedHouse("parcel-1", 7);
            string spaceId = f.Housing.SpacesForBuilding("bld-parcel-1")[0].SpaceId;

            NpcPropertyTenancy tenancy = f.Life.EstablishTenancy(f.Housing, holding, spaceId,
                9, new List<int> { 201 }, 3000, 30, f.Diag);
            Assert.IsNotNull(tenancy);
            Assert.AreEqual(AccommodationArrangement.Rental,
                f.Housing.CurrentOccupanciesForPerson(201)[0].Arrangement);

            FinancialObligation bill1 = f.Life.BillRent(f.Ids, f.Authority, holding,
                tenancy.TenancyId, 60, f.Diag);
            Assert.IsNotNull(bill1);
            int collected = f.Life.CollectRent(f.Cash, f.Authority, f.Workflow, holding,
                tenancy.TenancyId, bill1.ObligationId, 61, f.Diag);
            Assert.AreEqual(3000, collected);
            Assert.AreEqual(5000 - 3000, tenantLedger.GetBalanceCents());
            Assert.AreEqual(10000 + 3000, landlordLedger.GetBalanceCents());

            // Second period: the tenant is short — partial payment, the rest stands as arrears.
            FinancialObligation bill2 = f.Life.BillRent(f.Ids, f.Authority, holding,
                tenancy.TenancyId, 90, f.Diag);
            int collected2 = f.Life.CollectRent(f.Cash, f.Authority, f.Workflow, holding,
                tenancy.TenancyId, bill2.ObligationId, 91, f.Diag);
            Assert.AreEqual(2000, collected2);
            FinancialObligation arrears = f.Authority.Find(bill2.ObligationId);
            Assert.AreEqual(1000, arrears.TotalOutstandingCents);
            Assert.AreEqual(FinancialObligationStatus.Delinquent, arrears.Status);
        }

        [Test]
        public void Sale_WithOutstandingSellerNote_SettledFromProceeds_NoMagicalRestoration()
        {
            var f = new Fixture();
            HouseholdLedger sellerLedger = f.RegisterHousehold(7, 5000);
            HouseholdLedger buyerLedger = f.RegisterHousehold(8, 120000);
            PurseCashStore abel = f.RegisterPurse("Abel", 0);

            // H7 bought parcel-1 from Abel with an 80000c seller note still outstanding.
            FinancialObligation note = f.SellerNote("parcel-1", 7, "Abel", 80000);
            NpcPropertyHolding holding = f.RegisterOwnedHouse("parcel-1", 7, note.ObligationId);

            NpcPropertySaleResult result = f.Life.SellProperty(f.Ids, f.Authority, f.Cash,
                f.Titles, f.Housing, f.Workflow, holding, 8, 100000,
                buyerAssumesNote: false, creditorConsentsToAssumption: false,
                dayIndex: 100, events: f.Events, diag: f.Diag);

            Assert.IsTrue(result.Sold, result.FailureReason);
            // The note is settled IN FULL from the proceeds: 80000 to Abel.
            Assert.AreEqual(80000, result.SellerNoteSettledCents);
            Assert.AreEqual(FinancialObligationStatus.Satisfied,
                f.Authority.Find(note.ObligationId).Status);
            Assert.AreEqual(80000, abel.ReadBalanceCents());
            // The seller keeps the net: 100000 - 80000.
            Assert.AreEqual(5000 + 20000, sellerLedger.GetBalanceCents());
            Assert.AreEqual(20000, buyerLedger.GetBalanceCents());
            // Title went to the BUYER — the original seller does NOT get the property back.
            Assert.AreEqual("household:8", f.Titles.CurrentHolder("parcel-1"));
            Assert.AreEqual("household:8", result.NewHolder);
            Assert.IsTrue(string.IsNullOrWhiteSpace(holding.SellerNoteObligationId));
            Assert.AreEqual(8, holding.OwnerHouseholdId);
        }

        [Test]
        public void Sale_NoteExceedsPrice_BuyerAssumesWithConsent()
        {
            var f = new Fixture();
            f.RegisterHousehold(7, 5000);
            HouseholdLedger buyerLedger = f.RegisterHousehold(8, 80000);
            f.RegisterPurse("Abel", 0);

            FinancialObligation note = f.SellerNote("parcel-1", 7, "Abel", 80000);
            NpcPropertyHolding holding = f.RegisterOwnedHouse("parcel-1", 7, note.ObligationId);

            // 60000c price cannot clear the 80000c note — the buyer assumes it (creditor consents).
            NpcPropertySaleResult result = f.Life.SellProperty(f.Ids, f.Authority, f.Cash,
                f.Titles, f.Housing, f.Workflow, holding, 8, 60000,
                buyerAssumesNote: true, creditorConsentsToAssumption: true,
                dayIndex: 100, events: f.Events, diag: f.Diag);

            Assert.IsTrue(result.Sold, result.FailureReason);
            Assert.IsTrue(result.NoteAssumedByBuyer);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.AssumptionId));
            FinancialObligation assumed = f.Authority.Find(note.ObligationId);
            Assert.AreEqual("household:8", assumed.Debtor, "novation moved the debt to the buyer.");
            Assert.AreEqual(80000, assumed.TotalOutstandingCents, "the claim survives in full — nothing forgiven.");
            Assert.AreEqual(80000 - 60000, buyerLedger.GetBalanceCents());
            Assert.AreEqual("household:8", f.Titles.CurrentHolder("parcel-1"));
        }

        [Test]
        public void Sale_NoteExceedsPrice_BuyerWillNotAssume_RefusedLoudly()
        {
            var f = new Fixture();
            HouseholdLedger sellerLedger = f.RegisterHousehold(7, 5000);
            HouseholdLedger buyerLedger = f.RegisterHousehold(8, 80000);
            f.RegisterPurse("Abel", 0);

            FinancialObligation note = f.SellerNote("parcel-1", 7, "Abel", 80000);
            NpcPropertyHolding holding = f.RegisterOwnedHouse("parcel-1", 7, note.ObligationId);

            NpcPropertySaleResult result = f.Life.SellProperty(f.Ids, f.Authority, f.Cash,
                f.Titles, f.Housing, f.Workflow, holding, 8, 60000,
                buyerAssumesNote: false, creditorConsentsToAssumption: false,
                dayIndex: 100, events: f.Events, diag: f.Diag);

            Assert.IsFalse(result.Sold);
            Assert.IsNotEmpty(result.FailureReason);
            // Nothing moved: title, cash, and the note are exactly where they were.
            Assert.AreEqual("household:7", f.Titles.CurrentHolder("parcel-1"));
            Assert.AreEqual(5000, sellerLedger.GetBalanceCents());
            Assert.AreEqual(80000, buyerLedger.GetBalanceCents());
            Assert.AreEqual(80000, f.Authority.Find(note.ObligationId).TotalOutstandingCents);
            Assert.AreEqual(7, holding.OwnerHouseholdId);
        }

        [Test]
        public void SaveLoad_RoundTrip_NoDuplicatedHoldings()
        {
            var f = new Fixture();
            f.RegisterHousehold(7, 10000);
            NpcPropertyHolding holding = f.RegisterOwnedHouse("parcel-1", 7);
            f.Life.ReportMaintenanceNeed(holding, "fix fence", 300, 10, f.Diag);
            string spaceId = f.Housing.SpacesForBuilding("bld-parcel-1")[0].SpaceId;
            f.RegisterHousehold(9, 5000);
            f.Life.EstablishTenancy(f.Housing, holding, spaceId, 9,
                new List<int> { 201 }, 1500, 30, f.Diag);

            NpcPropertyLifeService.NpcPropertyLifeSaveDto dto = f.Life.CaptureSaveDto();
            var restored = new NpcPropertyLifeService();
            restored.LoadFromSaveDto(dto, f.Diag);
            restored.LoadFromSaveDto(dto, f.Diag); // loading twice must not duplicate

            NpcPropertyHolding found = restored.FindHolding(holding.HoldingId);
            Assert.IsNotNull(found);
            Assert.AreEqual("parcel-1", found.ParcelId);
            Assert.AreEqual(7, found.OwnerHouseholdId);
            Assert.AreEqual(1, found.MaintenanceNeeds.Count);
            Assert.AreEqual(1, found.TenancyIds.Count);
            NpcPropertyLifeService.NpcPropertyLifeSaveDto dto2 = restored.CaptureSaveDto();
            Assert.AreEqual(1, dto2.Holdings.Count);
            Assert.AreEqual(1, dto2.Tenancies.Count);
        }
    }
}
