using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// Pass III Worker B: the general rent/board payment loop.
    /// TenancyPaymentTerms attach additively to PropertyAgreement; the loop
    /// turns terms into due events: tenant HouseholdLedger debit → payee
    /// credit (household ledger or business sink) → unpaid remainder as a
    /// REAL Payable obligation through FinancialObligationAuthority.
    /// No second rent-cash authority. No automatic eviction.
    /// Canon: XXXI Part VI §§6.1–6.8; Part IV §4.6; Rev XXIII §1.1; §12.7H.
    /// </summary>
    [TestFixture]
    public sealed class TenancyPaymentServiceTests
    {
        private sealed class FakePayeeSink : ITenancyPayeeCashSink
        {
            public string PayeeHolderId { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public int CashCents { get; private set; }
            public readonly List<string> Labels = new List<string>();
            public void RecordCashInflow(int cents, string label, int dayIndex)
            {
                CashCents += cents;
                Labels.Add(label);
            }
        }

        private sealed class FakePayeeDirectory : ITenancyPayeeDirectory
        {
            public readonly Dictionary<string, int> HouseholdPayees = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly Dictionary<string, FakePayeeSink> BusinessSinks = new Dictionary<string, FakePayeeSink>(StringComparer.Ordinal);
            public int FindPayeeHouseholdId(string payeeHolderId)
            {
                if (payeeHolderId != null && HouseholdPayees.TryGetValue(payeeHolderId, out int householdId))
                    return householdId;
                return -1;
            }
            public ITenancyPayeeCashSink FindPayeeBusinessSink(string payeeHolderId)
            {
                if (payeeHolderId != null && BusinessSinks.TryGetValue(payeeHolderId, out FakePayeeSink sink))
                    return sink;
                return null;
            }
        }

        private sealed class FakeFootprintDirectory : IBuildingFootprintDirectory
        {
            public readonly Dictionary<string, (int plotId, GridRect footprint)> Records =
                new Dictionary<string, (int, GridRect)>(StringComparer.Ordinal);
            public bool TryGetPlacedFootprint(string buildingKey, out int plotId, out GridRect footprint, out string note)
            {
                if (Records.TryGetValue(buildingKey, out var record))
                {
                    plotId = record.plotId;
                    footprint = record.footprint;
                    note = "test placed record";
                    return true;
                }
                plotId = -1;
                footprint = default(GridRect);
                note = string.Empty;
                return false;
            }
        }

        private sealed class Fixture
        {
            public TenancyPaymentService Service = new TenancyPaymentService();
            public HousingAuthority Housing = new HousingAuthority();
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public FakePayeeDirectory Payees = new FakePayeeDirectory();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public List<string> Diag = new List<string>();

            public void FundHousehold(int householdId, int cents)
            {
                HouseholdLedger ledger = Ledgers.GetOrCreate(householdId);
                string refusal = ledger.RecordInflow(0, cents, HouseholdIncomeSource.OtherDocumented,
                    "test-funding", "test funding", "test");
                Assert.IsNull(refusal);
            }

            public int Balance(int householdId) => Ledgers.GetOrCreate(householdId).GetBalanceCents();

            /// <summary>Building + one space + one occupant + rental agreement, no terms yet.</summary>
            public string SetupTenancy(int tenantPersonId, int tenantHouseholdId, string buildingId, string parcelId)
            {
                Housing.RegisterBuilding(buildingId, parcelId, "house", 0, "test", Diag);
                AccommodationSpace space = Housing.DefineSpace(buildingId, 4, Diag);
                Assert.IsNotNull(space);
                ResidentialOccupancy occ = Housing.Occupy(tenantPersonId, tenantHouseholdId, space.SpaceId,
                    AccommodationArrangement.Rental, 0, Diag);
                Assert.IsNotNull(occ);
                PropertyAgreement agreement = Housing.RecordAgreement("rental", "Test Landlord", "Test Tenant",
                    space.SpaceId, "monthly rent", 0, -1, string.Empty, Diag);
                Assert.IsNotNull(agreement);
                return agreement.AgreementId;
            }

            public TenancyPaymentTerms MonthlyTerms(long amountCents, string payeeHolderId, int tenantHouseholdId = -1)
            {
                return new TenancyPaymentTerms
                {
                    AmountCents = amountCents,
                    PeriodDays = 30,
                    DueDayOfPeriod = 1,
                    StartDay = 0,
                    PayeeHolderId = payeeHolderId,
                    TenantHouseholdId = tenantHouseholdId,
                };
            }
        }

        [Test]
        public void FullPayment_DebitsTenantCreditsLandlordExactCents()
        {
            var f = new Fixture();
            string agreementId = f.SetupTenancy(tenantPersonId: 1, tenantHouseholdId: 1, buildingId: "b1", parcelId: "parcel-1");
            f.Payees.HouseholdPayees["household:2"] = 2;
            f.FundHousehold(1, 10000);
            f.FundHousehold(2, 500);

            Assert.IsNull(f.Housing.AttachPaymentTerms(agreementId, f.MonthlyTerms(2500, "household:2", tenantHouseholdId: 1), f.Diag));
            List<TenancySettlementResult> results = f.Service.SettleDueRents(
                f.Housing, 0, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);

            Assert.AreEqual(1, results.Count);
            TenancySettlementResult r = results[0];
            Assert.AreEqual("settled", r.Outcome);
            Assert.AreEqual(RentObligationStatus.Current, r.Status);
            Assert.AreEqual(2500, r.PaidCents);
            // Exact cents, both sides: tenant out == landlord in.
            Assert.AreEqual(7500, f.Balance(1));
            Assert.AreEqual(3000, f.Balance(2));
            Assert.AreEqual(string.Empty, r.ArrearsObligationId);
        }

        [Test]
        public void PartialPayment_BooksArrearsLiability()
        {
            var f = new Fixture();
            string agreementId = f.SetupTenancy(1, 1, "b1", "parcel-1");
            f.Payees.HouseholdPayees["household:2"] = 2;
            f.FundHousehold(1, 1000);

            Assert.IsNull(f.Housing.AttachPaymentTerms(agreementId, f.MonthlyTerms(2500, "household:2", tenantHouseholdId: 1), f.Diag));
            List<TenancySettlementResult> results = f.Service.SettleDueRents(
                f.Housing, 0, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);

            TenancySettlementResult r = results[0];
            Assert.AreEqual("settled-partial", r.Outcome);
            Assert.AreEqual(RentObligationStatus.Partial, r.Status);
            Assert.AreEqual(1000, r.PaidCents);
            Assert.AreEqual(0, f.Balance(1));
            Assert.AreEqual(1000, f.Balance(2));
            // The remainder is a REAL obligation, not informal debt.
            Assert.IsFalse(string.IsNullOrWhiteSpace(r.ArrearsObligationId));
            FinancialObligation obligation = f.Obligations.Find(r.ArrearsObligationId);
            Assert.IsNotNull(obligation);
            Assert.AreEqual(FinancialObligationKind.Payable, obligation.Kind);
            Assert.AreEqual(1500, obligation.OutstandingPrincipalCents);
            Assert.AreEqual("household:1", obligation.Debtor);
        }

        [Test]
        public void NoFunds_OverdueWithoutEviction()
        {
            var f = new Fixture();
            string agreementId = f.SetupTenancy(1, 1, "b1", "parcel-1");
            f.Payees.HouseholdPayees["household:2"] = 2;
            // Tenant household holds nothing.

            Assert.IsNull(f.Housing.AttachPaymentTerms(agreementId, f.MonthlyTerms(2500, "household:2", tenantHouseholdId: 1), f.Diag));
            List<TenancySettlementResult> results = f.Service.SettleDueRents(
                f.Housing, 0, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);

            TenancySettlementResult r = results[0];
            Assert.AreEqual("settled-overdue", r.Outcome);
            Assert.AreEqual(RentObligationStatus.Overdue, r.Status);
            Assert.AreEqual(0, r.PaidCents);
            Assert.AreEqual(0, f.Balance(1));
            Assert.AreEqual(0, f.Balance(2));
            // Arrears booked for the full amount…
            FinancialObligation obligation = f.Obligations.Find(r.ArrearsObligationId);
            Assert.IsNotNull(obligation);
            Assert.AreEqual(2500, obligation.OutstandingPrincipalCents);
            // …but the tenancy is NOT terminated: occupancy stays current and
            // this service has no eviction machinery (Canon XXXI Part VI —
            // enforcement is its own legitimate process).
            List<ResidentialOccupancy> occupancies = f.Housing.CurrentOccupanciesForPerson(1);
            Assert.AreEqual(1, occupancies.Count);
            Assert.IsTrue(occupancies[0].IsCurrent);
        }

        [Test]
        public void MultiTenantBuilding_DifferentPayees()
        {
            var f = new Fixture();
            // One building, two independent spaces/tenancies, two landlords.
            f.Housing.RegisterBuilding("b1", "parcel-1", "double-house", 0, "test", f.Diag);
            AccommodationSpace s1 = f.Housing.DefineSpace("b1", 4, f.Diag);
            AccommodationSpace s2 = f.Housing.DefineSpace("b1", 3, f.Diag);
            f.Housing.Occupy(1, 1, s1.SpaceId, AccommodationArrangement.Rental, 0, f.Diag);
            f.Housing.Occupy(2, 2, s2.SpaceId, AccommodationArrangement.Rental, 0, f.Diag);
            PropertyAgreement a1 = f.Housing.RecordAgreement("rental", "Landlord One", "Tenant One",
                s1.SpaceId, "terms", 0, -1, string.Empty, f.Diag);
            PropertyAgreement a2 = f.Housing.RecordAgreement("rental", "Landlord Two", "Tenant Two",
                s2.SpaceId, "terms", 0, -1, string.Empty, f.Diag);
            f.Payees.HouseholdPayees["household:10"] = 10;
            f.Payees.HouseholdPayees["household:20"] = 20;
            f.FundHousehold(1, 5000);
            f.FundHousehold(2, 5000);
            Assert.IsNull(f.Housing.AttachPaymentTerms(a1.AgreementId, f.MonthlyTerms(2000, "household:10", tenantHouseholdId: 1), f.Diag));
            Assert.IsNull(f.Housing.AttachPaymentTerms(a2.AgreementId, f.MonthlyTerms(3000, "household:20", tenantHouseholdId: 2), f.Diag));

            List<TenancySettlementResult> results = f.Service.SettleDueRents(
                f.Housing, 0, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);

            Assert.AreEqual(2, results.Count);
            // Rights stay separate: each tenant paid, each landlord credited.
            Assert.AreEqual(3000, f.Balance(1));
            Assert.AreEqual(2000, f.Balance(2));
            Assert.AreEqual(2000, f.Balance(10));
            Assert.AreEqual(3000, f.Balance(20));
        }

        [Test]
        public void BusinessPayee_CreditsBusinessSink()
        {
            var f = new Fixture();
            string agreementId = f.SetupTenancy(1, 1, "b1", "parcel-1");
            var sink = new FakePayeeSink { PayeeHolderId = "business:store-1", DisplayName = "General Store" };
            f.Payees.BusinessSinks["business:store-1"] = sink;
            f.FundHousehold(1, 10000);

            Assert.IsNull(f.Housing.AttachPaymentTerms(agreementId, f.MonthlyTerms(1500, "business:store-1", tenantHouseholdId: 1), f.Diag));
            List<TenancySettlementResult> results = f.Service.SettleDueRents(
                f.Housing, 0, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);

            Assert.AreEqual("settled", results[0].Outcome);
            Assert.AreEqual(8500, f.Balance(1));
            Assert.AreEqual(1500, sink.CashCents);
        }

        [Test]
        public void TenantResolvedFromOccupancy_WhenTermsNameNoHousehold()
        {
            var f = new Fixture();
            string agreementId = f.SetupTenancy(1, 7, "b1", "parcel-1");
            f.Payees.HouseholdPayees["household:2"] = 2;
            f.FundHousehold(7, 4000);
            // Terms name no tenant household: occupancy resolves it.
            Assert.IsNull(f.Housing.AttachPaymentTerms(agreementId, f.MonthlyTerms(1200, "household:2"), f.Diag));

            List<TenancySettlementResult> results = f.Service.SettleDueRents(
                f.Housing, 0, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);

            Assert.AreEqual("settled", results[0].Outcome);
            Assert.AreEqual(7, results[0].TenantHouseholdId);
            Assert.AreEqual(2800, f.Balance(7));
            Assert.AreEqual(1200, f.Balance(2));
        }

        [Test]
        public void UnresolvablePayee_NoDebit()
        {
            var f = new Fixture();
            string agreementId = f.SetupTenancy(1, 1, "b1", "parcel-1");
            f.FundHousehold(1, 10000);
            // No payee registered for "household:99".
            Assert.IsNull(f.Housing.AttachPaymentTerms(agreementId, f.MonthlyTerms(2500, "household:99", tenantHouseholdId: 1), f.Diag));

            List<TenancySettlementResult> results = f.Service.SettleDueRents(
                f.Housing, 0, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);

            Assert.AreEqual("skipped-no-payee", results[0].Outcome);
            // Conservation: nothing left the tenant when the payee was unknown.
            Assert.AreEqual(10000, f.Balance(1));
        }

        [Test]
        public void Idempotent_DoubleSettleSameDay()
        {
            var f = new Fixture();
            string agreementId = f.SetupTenancy(1, 1, "b1", "parcel-1");
            f.Payees.HouseholdPayees["household:2"] = 2;
            f.FundHousehold(1, 10000);
            Assert.IsNull(f.Housing.AttachPaymentTerms(agreementId, f.MonthlyTerms(2500, "household:2", tenantHouseholdId: 1), f.Diag));

            f.Service.SettleDueRents(f.Housing, 0, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);
            List<TenancySettlementResult> second = f.Service.SettleDueRents(
                f.Housing, 0, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);

            Assert.AreEqual(1, second.Count);
            Assert.AreEqual("already-settled", second[0].Outcome);
            Assert.AreEqual(7500, f.Balance(1));
            Assert.AreEqual(2500, f.Balance(2));
        }

        [Test]
        public void NextPeriod_SettlesIndependently()
        {
            var f = new Fixture();
            string agreementId = f.SetupTenancy(1, 1, "b1", "parcel-1");
            f.Payees.HouseholdPayees["household:2"] = 2;
            f.FundHousehold(1, 10000);
            Assert.IsNull(f.Housing.AttachPaymentTerms(agreementId, f.MonthlyTerms(2500, "household:2", tenantHouseholdId: 1), f.Diag));

            f.Service.SettleDueRents(f.Housing, 0, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);
            List<TenancySettlementResult> results = f.Service.SettleDueRents(
                f.Housing, 30, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);

            // Period 0 is already-settled; period 1 settles on its due day.
            Assert.AreEqual(2, results.Count);
            Assert.AreEqual("already-settled", results[0].Outcome);
            Assert.AreEqual("settled", results[1].Outcome);
            Assert.AreEqual(1, results[1].PeriodIndex);
            Assert.AreEqual(30, results[1].DueDay);
            Assert.AreEqual(5000, f.Balance(1));
            Assert.AreEqual(5000, f.Balance(2));
        }

        [Test]
        public void SaveLoad_RentOwing_NoDoubleRent()
        {
            var f = new Fixture();
            string agreementId = f.SetupTenancy(1, 1, "b1", "parcel-1");
            f.Payees.HouseholdPayees["household:2"] = 2;
            f.FundHousehold(1, 10000);
            Assert.IsNull(f.Housing.AttachPaymentTerms(agreementId, f.MonthlyTerms(2500, "household:2", tenantHouseholdId: 1), f.Diag));
            f.Service.SettleDueRents(f.Housing, 0, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);

            // Save → fresh service → load (clear-then-load).
            TenancyPaymentService.TenancyPaymentSaveDto dto = f.Service.CaptureSaveDto();
            var restored = new TenancyPaymentService();
            restored.LoadFromSaveDto(dto);

            RentObligationState state = restored.FindObligation(agreementId, 0);
            Assert.IsNotNull(state);
            Assert.AreEqual(2500, state.PaidCents);
            Assert.AreEqual(RentObligationStatus.Current, state.Status);

            // Re-settling the same day is a no-op: no double rent.
            List<TenancySettlementResult> results = restored.SettleDueRents(
                f.Housing, 0, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);
            Assert.AreEqual("already-settled", results[0].Outcome);
            Assert.AreEqual(7500, f.Balance(1));
            Assert.AreEqual(2500, f.Balance(2));
        }

        [Test]
        public void SaveLoad_PartialPayment_PreservesArrearsState()
        {
            var f = new Fixture();
            string agreementId = f.SetupTenancy(1, 1, "b1", "parcel-1");
            f.Payees.HouseholdPayees["household:2"] = 2;
            f.FundHousehold(1, 1000);
            Assert.IsNull(f.Housing.AttachPaymentTerms(agreementId, f.MonthlyTerms(2500, "household:2", tenantHouseholdId: 1), f.Diag));
            f.Service.SettleDueRents(f.Housing, 0, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);
            int obligationCountBefore = 0;
            foreach (FinancialObligation o in f.Obligations.Obligations) obligationCountBefore++;

            var restored = new TenancyPaymentService();
            restored.LoadFromSaveDto(f.Service.CaptureSaveDto());

            RentObligationState state = restored.FindObligation(agreementId, 0);
            Assert.IsNotNull(state);
            Assert.AreEqual(RentObligationStatus.Partial, state.Status);
            Assert.AreEqual(1000, state.PaidCents);
            Assert.IsFalse(string.IsNullOrWhiteSpace(state.ArrearsObligationId));

            // The next period settles independently; the old period is NOT
            // re-booked (no duplicate arrears obligation).
            f.FundHousehold(1, 5000);
            List<TenancySettlementResult> results = restored.SettleDueRents(
                f.Housing, 30, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);
            Assert.AreEqual(2, results.Count);
            Assert.AreEqual("already-settled", results[0].Outcome);
            Assert.AreEqual("settled", results[1].Outcome);
            int obligationCountAfter = 0;
            foreach (FinancialObligation o in f.Obligations.Obligations) obligationCountAfter++;
            Assert.AreEqual(obligationCountBefore, obligationCountAfter);
        }

        [Test]
        public void AttachTerms_RejectsInvalidTerms()
        {
            var f = new Fixture();
            string agreementId = f.SetupTenancy(1, 1, "b1", "parcel-1");

            Assert.IsNotNull(f.Housing.AttachPaymentTerms(agreementId,
                new TenancyPaymentTerms { AmountCents = 0, PeriodDays = 30, DueDayOfPeriod = 1, StartDay = 0, PayeeHolderId = "household:2" }, f.Diag));
            Assert.IsNotNull(f.Housing.AttachPaymentTerms(agreementId,
                new TenancyPaymentTerms { AmountCents = 100, PeriodDays = 0, DueDayOfPeriod = 1, StartDay = 0, PayeeHolderId = "household:2" }, f.Diag));
            Assert.IsNotNull(f.Housing.AttachPaymentTerms(agreementId,
                new TenancyPaymentTerms { AmountCents = 100, PeriodDays = 30, DueDayOfPeriod = 31, StartDay = 0, PayeeHolderId = "household:2" }, f.Diag));
            Assert.IsNotNull(f.Housing.AttachPaymentTerms(agreementId,
                new TenancyPaymentTerms { AmountCents = 100, PeriodDays = 30, DueDayOfPeriod = 1, StartDay = 0, PayeeHolderId = "" }, f.Diag));
            Assert.IsNotNull(f.Housing.AttachPaymentTerms("no-such-agreement",
                f.MonthlyTerms(100, "household:2"), f.Diag));

            // And a valid attach succeeds exactly once.
            Assert.IsNull(f.Housing.AttachPaymentTerms(agreementId, f.MonthlyTerms(100, "household:2"), f.Diag));
            Assert.IsNotNull(f.Housing.AttachPaymentTerms(agreementId, f.MonthlyTerms(100, "household:2"), f.Diag));
        }

        [Test]
        public void AgreementTerms_RoundTripThroughHousingSaveDto()
        {
            var f = new Fixture();
            string agreementId = f.SetupTenancy(1, 1, "b1", "parcel-1");
            var terms = f.MonthlyTerms(2500, "household:2", tenantHouseholdId: 1);
            Assert.IsNull(f.Housing.AttachPaymentTerms(agreementId, terms, f.Diag));

            HousingAuthority.HousingSaveDto dto = f.Housing.CaptureSaveDto();
            var restored = new HousingAuthority();
            restored.LoadFromSaveDto(dto);

            List<PropertyAgreement> withTerms = restored.AgreementsWithPaymentTerms();
            Assert.AreEqual(1, withTerms.Count);
            Assert.AreEqual(agreementId, withTerms[0].AgreementId);
            Assert.AreEqual(2500, withTerms[0].PaymentTerms.AmountCents);
            Assert.AreEqual("household:2", withTerms[0].PaymentTerms.PayeeHolderId);
            Assert.AreEqual(1, withTerms[0].PaymentTerms.TenantHouseholdId);
        }

        [Test]
        public void FootprintLinker_ReportsPlacedFootprintReadOnly()
        {
            var f = new Fixture();
            f.Housing.RegisterBuilding("b1", "parcel-1", "house", 0, "test", f.Diag);
            f.Housing.RegisterBuilding("b2", "parcel-2", "barn", 0, "test", f.Diag);
            var directory = new FakeFootprintDirectory();
            var rect = new GridRect(10, 20, 6, 8);
            directory.Records["b1"] = (42, rect);
            var linker = new BuildingFootprintLinker();

            List<BuildingFootprintView> views = linker.Link(f.Housing, directory, f.Diag);
            Assert.AreEqual(2, views.Count);

            BuildingFootprintView v1 = views.Find(v => v.BuildingId == "b1");
            Assert.IsTrue(v1.HasPlacedRecord);
            Assert.AreEqual(42, v1.PlotId);
            Assert.AreEqual("parcel-1", v1.ParcelId);
            Assert.AreEqual(rect, v1.Footprint);

            // A building with no placed record is reported, not invented.
            BuildingFootprintView v2 = views.Find(v => v.BuildingId == "b2");
            Assert.IsFalse(v2.HasPlacedRecord);
            Assert.AreEqual(-1, v2.PlotId);
        }
    }
}
