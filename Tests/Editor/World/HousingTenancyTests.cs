using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// Pass VIII Worker 2 (Housing and Boarding): production-authority
    /// proofs for the live housing connections.
    /// - A household cannot occupy nonexistent accommodation.
    /// - A room cannot be rented beyond capacity (commercial lodging).
    /// - Housing discovery never creates free housing.
    /// - Rent accrues and arrears become real obligations through the debt
    ///   authority (one cash writer, one debt authority).
    /// - Departure frees the space; a new occupant can take it.
    /// - Capacity changes are respected.
    /// - Closure/transfer resolves tenancies without orphaning occupants
    ///   or obligations.
    /// - Save/load preserves tenancy state exactly once.
    /// - Absent persons do not hold accommodation without a valid arrangement.
    /// </summary>
    [TestFixture]
    public sealed class HousingTenancyTests
    {
        private sealed class FakePayeeDirectory : ITenancyPayeeDirectory
        {
            public readonly Dictionary<string, int> HouseholdPayees = new Dictionary<string, int>(StringComparer.Ordinal);
            public int FindPayeeHouseholdId(string payeeHolderId)
            {
                if (payeeHolderId != null && HouseholdPayees.TryGetValue(payeeHolderId, out int id)) return id;
                return -1;
            }
            public ITenancyPayeeCashSink FindPayeeBusinessSink(string payeeHolderId) => null;
        }

        private sealed class Fixture
        {
            public HousingAuthority Housing = new HousingAuthority();
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public FakePayeeDirectory Payees = new FakePayeeDirectory();
            public List<string> Diag = new List<string>();

            public string BuildingWithSpace(string buildingId, string parcelId, int capacity)
            {
                Housing.RegisterBuilding(buildingId, parcelId, "house", 0, "test", Diag);
                AccommodationSpace space = Housing.DefineSpace(buildingId, capacity, Diag);
                Assert.IsNotNull(space);
                return space.SpaceId;
            }

            public void FundHousehold(int householdId, int cents)
            {
                if (cents <= 0) return;
                HouseholdLedger ledger = Ledgers.GetOrCreate(householdId);
                string refusal = ledger.RecordInflow(0, cents, HouseholdIncomeSource.OtherDocumented,
                    "test-funding", "test funding", "test");
                Assert.IsNull(refusal);
            }

            public int Balance(int householdId) => Ledgers.GetOrCreate(householdId).GetBalanceCents();
        }

        [Test]
        public void OccupancyOfNonexistentAccommodationIsRefused()
        {
            var f = new Fixture();
            ResidentialOccupancy occ = f.Housing.Occupy(1, 10, "space-that-does-not-exist",
                AccommodationArrangement.Rental, 0, f.Diag);
            Assert.IsNull(occ);

            string refusal = f.Housing.TryOccupyUnderAgreement(1, 10, "space-that-does-not-exist",
                AccommodationArrangement.Rental, 0, "ag-1", f.Diag, out ResidentialOccupancy guarded);
            Assert.IsNotNull(refusal);
            Assert.IsNull(guarded);
            StringAssert.Contains("nonexistent", refusal);
        }

        [Test]
        public void OverCapacityRentalIsRefused()
        {
            var f = new Fixture();
            string spaceId = f.BuildingWithSpace("b-1", "p-1", 2);

            string r1 = f.Housing.TryOccupyUnderAgreement(1, 10, spaceId, AccommodationArrangement.Rental,
                0, "ag-1", f.Diag, out ResidentialOccupancy o1);
            string r2 = f.Housing.TryOccupyUnderAgreement(2, 10, spaceId, AccommodationArrangement.Rental,
                0, "ag-1", f.Diag, out ResidentialOccupancy o2);
            Assert.IsNull(r1);
            Assert.IsNull(r2);
            Assert.IsNotNull(o1);
            Assert.IsNotNull(o2);

            string r3 = f.Housing.TryOccupyUnderAgreement(3, 11, spaceId, AccommodationArrangement.Rental,
                0, "ag-2", f.Diag, out ResidentialOccupancy o3);
            Assert.IsNotNull(r3, "a room cannot be rented beyond capacity");
            Assert.IsNull(o3);
            Assert.AreEqual(2, f.Housing.OccupantCount(spaceId));
        }

        [Test]
        public void PrivateRoomCannotBeRentedToTwoPeople()
        {
            var f = new Fixture();
            string spaceId = f.BuildingWithSpace("b-1", "p-1", 1);
            string r1 = f.Housing.TryOccupyUnderAgreement(1, 10, spaceId, AccommodationArrangement.RoomRental,
                0, "ag-1", f.Diag, out _);
            Assert.IsNull(r1);
            string r2 = f.Housing.TryOccupyUnderAgreement(2, 11, spaceId, AccommodationArrangement.RoomRental,
                0, "ag-2", f.Diag, out ResidentialOccupancy o2);
            Assert.IsNotNull(r2);
            Assert.IsNull(o2);
        }

        [Test]
        public void DiscoveryNeverCreatesFreeHousing_RentalWithPayeeAttachesRealTerms()
        {
            var f = new Fixture();
            string spaceId = f.BuildingWithSpace("b-1", "p-1", 3);
            f.FundHousehold(10, 10000);
            f.FundHousehold(99, 0);
            f.Payees.HouseholdPayees["household:99"] = 99;

            var source = new HousingOpportunityDirectory();
            source.AddRental(new HousingSearchOption
            {
                OptionId = "r-1",
                PathKind = HousingSearchPathKind.Rental,
                ProviderName = "Landlord Ames",
                PayeeHolderId = "household:99",
                Description = "upper rooms",
                SpaceId = spaceId,
                BuildingId = "b-1",
                Arrangement = AccommodationArrangement.Rental,
                PlacesProvided = 3,
                UpfrontCostCents = 0,
                RecurringMonthlyCents = 3000,
            });
            var needs = new HousingSearchNeedRegistry();
            HousingSearchNeed need = needs.OpenNeed(10, 1, ResidentialConditionKind.Unsheltered, "test", 0);
            var planner = new HousingSearchPlanner();
            var composition = new HouseholdCompositionSummary { HouseholdId = 10, AdultCount = 1 };
            HousingSearchDecision decision = planner.Plan(need, composition, 10000, 5000, source, 0, f.Diag);
            Assert.IsTrue(decision.Resolved);

            var executor = new HousingSearchExecutor();
            var members = new List<PersonState>
            {
                new PersonState { id = 1, age = 30, householdId = 10, deathDayIndex = -1 },
            };
            HousingSearchExecutor.ExecutionResult result = executor.Execute(
                decision, members, f.Housing, null, new SellerFinanceNegotiationBook(),
                new LenderApproachService(), f.Ids, new CreditOfferWorkflow(), 0, f.Diag);
            Assert.IsTrue(result.Executed, result.FailureReason);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.AgreementId));

            // The tenancy carries REAL payment terms — the rent loop charges
            // real cash or books real arrears. No free housing.
            PropertyAgreement agreement = f.Housing.FindAgreement(result.AgreementId);
            Assert.IsNotNull(agreement.PaymentTerms);
            Assert.AreEqual(3000, agreement.PaymentTerms.AmountCents);

            var payments = new TenancyPaymentService();
            List<TenancySettlementResult> results = payments.SettleDueRents(
                f.Housing, 30, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);
            // Catch-up: period 0 (due day 0) and period 1 (due day 30) both
            // settle — rent is real for every due period, never free.
            Assert.AreEqual(2, results.Count);
            Assert.AreEqual("settled", results[0].Outcome);
            Assert.AreEqual("settled", results[1].Outcome);
            Assert.AreEqual(6000, results[0].PaidCents + results[1].PaidCents);
            Assert.AreEqual(4000, f.Balance(10));
            Assert.AreEqual(6000, f.Balance(99));
        }

        [Test]
        public void RentArrearsBecomeRealObligationsThroughTheDebtAuthority()
        {
            var f = new Fixture();
            string spaceId = f.BuildingWithSpace("b-1", "p-1", 2);
            f.FundHousehold(10, 1000); // cannot cover 3000c rent
            f.FundHousehold(99, 0);
            f.Payees.HouseholdPayees["household:99"] = 99;

            string refusal = f.Housing.TryOccupyUnderAgreement(1, 10, spaceId, AccommodationArrangement.Rental,
                0, string.Empty, f.Diag, out _);
            Assert.IsNull(refusal);
            PropertyAgreement agreement = f.Housing.RecordAgreement("rental", "Landlord", "H10",
                spaceId, "monthly", 0, -1, string.Empty, f.Diag);
            string tref = f.Housing.AttachPaymentTerms(agreement.AgreementId, new TenancyPaymentTerms
            {
                AmountCents = 3000, PeriodDays = 30, DueDayOfPeriod = 1, StartDay = 0,
                PayeeHolderId = "household:99", TenantHouseholdId = 10,
            }, f.Diag);
            Assert.IsNull(tref);

            // Catch-up: period 0 (due day 0) and period 1 (due day 30)
            // both come due. Period 0 settles partially (1000c of 3000c);
            // period 1 finds an empty ledger and goes fully overdue.
            var payments = new TenancyPaymentService();
            List<TenancySettlementResult> results = payments.SettleDueRents(
                f.Housing, 30, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);
            Assert.AreEqual(2, results.Count);
            Assert.AreEqual("settled-partial", results[0].Outcome);
            Assert.AreEqual(1000, results[0].PaidCents);
            Assert.AreEqual(0, f.Balance(10));
            Assert.AreEqual(1000, f.Balance(99));
            Assert.AreEqual("settled-overdue", results[1].Outcome);
            Assert.AreEqual(0, results[1].PaidCents);

            // The 2000c remainder is a REAL obligation, not informal debt.
            Assert.IsFalse(string.IsNullOrWhiteSpace(results[0].ArrearsObligationId));
            FinancialObligation obligation = f.Obligations.Find(results[0].ArrearsObligationId);
            Assert.IsNotNull(obligation);
            Assert.AreEqual(2000, obligation.OutstandingPrincipalCents);
            Assert.AreEqual("household:10", obligation.Debtor);
            // Period 1's full 3000c is a second real obligation.
            Assert.IsFalse(string.IsNullOrWhiteSpace(results[1].ArrearsObligationId));
            FinancialObligation obligation2 = f.Obligations.Find(results[1].ArrearsObligationId);
            Assert.IsNotNull(obligation2);
            Assert.AreEqual(3000, obligation2.OutstandingPrincipalCents);
        }

        [Test]
        public void DepartureFreesTheSpaceAndANewOccupantCanTakeIt()
        {
            var f = new Fixture();
            string spaceId = f.BuildingWithSpace("b-1", "p-1", 1);
            var mover = new HouseholdMoveService();

            HouseholdMoveResult first = mover.MoveHousehold(10, new List<int> { 1 }, spaceId,
                AccommodationArrangement.Rental, "rental", "Landlord", 0, f.Housing, f.Diag);
            Assert.IsTrue(first.Moved, first.Refusal);
            Assert.AreEqual(1, f.Housing.OccupantCount(spaceId));
            Assert.AreEqual(0, f.Housing.VacantSpaces().Count);

            // P1 departs: the space is genuinely free again.
            f.Housing.EndOccupanciesForPerson(1, 5, f.Diag);
            Assert.AreEqual(0, f.Housing.OccupantCount(spaceId));
            Assert.AreEqual(1, f.Housing.VacantSpaces().Count);
            Assert.AreEqual(spaceId, f.Housing.VacantSpaces()[0].SpaceId);

            HouseholdMoveResult second = mover.MoveHousehold(11, new List<int> { 2 }, spaceId,
                AccommodationArrangement.Rental, "rental", "Landlord", 6, f.Housing, f.Diag);
            Assert.IsTrue(second.Moved, second.Refusal);
            Assert.AreEqual(1, f.Housing.OccupantCount(spaceId));
        }

        [Test]
        public void CapacityChangesAreRespected()
        {
            var f = new Fixture();
            string spaceId = f.BuildingWithSpace("b-1", "p-1", 3);
            Assert.IsNull(f.Housing.TryOccupyUnderAgreement(1, 10, spaceId, AccommodationArrangement.Rental,
                0, "ag-1", f.Diag, out _));
            Assert.IsNull(f.Housing.TryOccupyUnderAgreement(2, 10, spaceId, AccommodationArrangement.Rental,
                0, "ag-1", f.Diag, out _));

            // Growing is fine.
            Assert.IsNull(f.Housing.ChangeSpaceCapacity(spaceId, 4, 1, f.Diag));
            Assert.AreEqual(4, f.Housing.FindSpace(spaceId).SleepingCapacity);

            // Shrinking below current occupants is refused — people move out first.
            string refusal = f.Housing.ChangeSpaceCapacity(spaceId, 1, 2, f.Diag);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(4, f.Housing.FindSpace(spaceId).SleepingCapacity);

            // After one departure, shrinking to 2 is allowed.
            f.Housing.EndOccupanciesForPerson(1, 3, f.Diag);
            Assert.IsNull(f.Housing.ChangeSpaceCapacity(spaceId, 2, 3, f.Diag));
            Assert.AreEqual(2, f.Housing.FindSpace(spaceId).SleepingCapacity);

            Assert.IsNotNull(f.Housing.ChangeSpaceCapacity("no-such-space", 2, 3, f.Diag));
            Assert.IsNotNull(f.Housing.ChangeSpaceCapacity(spaceId, -1, 3, f.Diag));
        }

        [Test]
        public void BuildingClosureResolvesTenanciesWithoutOrphans()
        {
            var f = new Fixture();
            string spaceId = f.BuildingWithSpace("b-1", "p-1", 2);
            f.FundHousehold(10, 5000);
            f.Payees.HouseholdPayees["household:99"] = 99;

            PropertyAgreement agreement = f.Housing.RecordAgreement("rental", "Landlord", "H10",
                spaceId, "monthly", 0, -1, string.Empty, f.Diag);
            Assert.IsNull(f.Housing.AttachPaymentTerms(agreement.AgreementId, new TenancyPaymentTerms
            {
                AmountCents = 1000, PeriodDays = 30, DueDayOfPeriod = 1, StartDay = 0,
                PayeeHolderId = "household:99", TenantHouseholdId = 10,
            }, f.Diag));
            Assert.IsNull(f.Housing.TryOccupyUnderAgreement(1, 10, spaceId, AccommodationArrangement.Rental,
                0, agreement.AgreementId, f.Diag, out _));

            HousingAuthority.BuildingClosureResult closure = f.Housing.CloseBuilding("b-1", 10, "test closure", f.Diag);
            Assert.IsTrue(string.IsNullOrEmpty(closure.Refusal), closure.Refusal);
            Assert.AreEqual(1, closure.OccupanciesEnded);
            Assert.AreEqual(1, closure.AgreementsEnded);

            // No occupant left behind, the space is free, the agreement is ended.
            Assert.AreEqual(0, f.Housing.OccupantCount(spaceId));
            Assert.AreEqual(10, f.Housing.FindAgreement(agreement.AgreementId).EndDayIndex);

            // Closure stops NEW charges: period 0 (due day 0, before closure)
            // still settles legitimately — the household owes rent for the
            // days it was housed — but period 1 (due day 30, after the
            // day-10 closure) never comes due. No orphaned occupant, no
            // orphaned charge.
            var payments = new TenancyPaymentService();
            List<TenancySettlementResult> results = payments.SettleDueRents(
                f.Housing, 40, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(0, results[0].PeriodIndex);
            Assert.AreEqual("settled", results[0].Outcome);
            Assert.AreEqual(4000, f.Balance(10));
        }

        [Test]
        public void PropertyTransferKeepsOccupantsAndObligationLinks()
        {
            var f = new Fixture();
            string spaceId = f.BuildingWithSpace("b-1", "p-1", 2);
            f.FundHousehold(10, 500); // partial: arrears will exist
            f.Payees.HouseholdPayees["household:99"] = 99;
            f.Payees.HouseholdPayees["household:100"] = 100;

            PropertyAgreement agreement = f.Housing.RecordAgreement("rental", "Old Landlord", "H10",
                spaceId, "monthly", 0, -1, string.Empty, f.Diag);
            Assert.IsNull(f.Housing.AttachPaymentTerms(agreement.AgreementId, new TenancyPaymentTerms
            {
                AmountCents = 3000, PeriodDays = 30, DueDayOfPeriod = 1, StartDay = 0,
                PayeeHolderId = "household:99", TenantHouseholdId = 10,
            }, f.Diag));
            Assert.IsNull(f.Housing.TryOccupyUnderAgreement(1, 10, spaceId, AccommodationArrangement.Rental,
                0, agreement.AgreementId, f.Diag, out _));

            var payments = new TenancyPaymentService();
            List<TenancySettlementResult> first = payments.SettleDueRents(
                f.Housing, 30, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);
            Assert.AreEqual("settled-partial", first[0].Outcome);
            string arrearsId = first[0].ArrearsObligationId;
            Assert.IsFalse(string.IsNullOrWhiteSpace(arrearsId));

            // Transfer: occupants stay, the agreement id is unchanged (the
            // arrears obligation stays linked), only grantor and payee move.
            string refusal = f.Housing.TransferAgreement(agreement.AgreementId, "New Landlord",
                "household:100", 31, "test sale", f.Diag);
            Assert.IsNull(refusal);
            Assert.AreEqual("New Landlord", f.Housing.FindAgreement(agreement.AgreementId).GrantorName);
            Assert.AreEqual("household:100",
                f.Housing.FindAgreement(agreement.AgreementId).PaymentTerms.PayeeHolderId);
            Assert.AreEqual(1, f.Housing.OccupantCount(spaceId));
            Assert.IsNotNull(f.Obligations.Find(arrearsId), "arrears obligation must survive the transfer");

            // Next periods' rent goes to the NEW payee.
            f.FundHousehold(10, 10000);
            List<TenancySettlementResult> second = payments.SettleDueRents(
                f.Housing, 60, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);
            // Periods 2 (due day 60) settles; periods 0 and 1 were already
            // settled before the transfer (catch-up at day 30).
            Assert.AreEqual(3, second.Count);
            Assert.AreEqual("already-settled", second[0].Outcome);
            Assert.AreEqual("already-settled", second[1].Outcome);
            Assert.AreEqual("settled", second[2].Outcome);
            Assert.AreEqual(3000, f.Balance(100), "new payee receives the post-transfer rent");
            Assert.AreEqual(500, f.Balance(99), "old payee keeps only the pre-transfer partial");
        }

        [Test]
        public void SaveLoadPreservesTenancyStateExactlyOnce()
        {
            var f = new Fixture();
            string spaceId = f.BuildingWithSpace("b-1", "p-1", 2);
            f.FundHousehold(10, 10000);
            f.Payees.HouseholdPayees["household:99"] = 99;

            PropertyAgreement agreement = f.Housing.RecordAgreement("rental", "Landlord", "H10",
                spaceId, "monthly", 0, -1, string.Empty, f.Diag);
            Assert.IsNull(f.Housing.AttachPaymentTerms(agreement.AgreementId, new TenancyPaymentTerms
            {
                AmountCents = 2000, PeriodDays = 30, DueDayOfPeriod = 1, StartDay = 0,
                PayeeHolderId = "household:99", TenantHouseholdId = 10,
            }, f.Diag));
            Assert.IsNull(f.Housing.TryOccupyUnderAgreement(1, 10, spaceId, AccommodationArrangement.Rental,
                0, agreement.AgreementId, f.Diag, out _));

            var payments = new TenancyPaymentService();
            List<TenancySettlementResult> before = payments.SettleDueRents(
                f.Housing, 30, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);
            Assert.AreEqual("settled", before[0].Outcome);

            // Round-trip both authorities through their save DTOs.
            HousingAuthority.HousingSaveDto housingDto = f.Housing.CaptureSaveDto();
            TenancyPaymentService.TenancyPaymentSaveDto paymentsDto = payments.CaptureSaveDto();
            var housing2 = new HousingAuthority();
            housing2.LoadFromSaveDto(housingDto);
            var payments2 = new TenancyPaymentService();
            payments2.LoadFromSaveDto(paymentsDto);

            // State survived exactly once: the occupancy, the agreement with
            // terms, and the settled-period marker.
            Assert.AreEqual(1, housing2.OccupantCount(spaceId));
            Assert.IsNotNull(housing2.FindAgreement(agreement.AgreementId));
            Assert.AreEqual(2000, housing2.FindAgreement(agreement.AgreementId).PaymentTerms.AmountCents);
            Assert.IsNotNull(payments2.FindObligation(agreement.AgreementId, 0));

            // Re-settling the same day settles nothing twice (idempotent
            // across save/load): the period is already settled.
            f.FundHousehold(10, 10000);
            List<TenancySettlementResult> after = payments2.SettleDueRents(
                housing2, 30, f.Ledgers, f.Payees, f.Obligations, f.Ids, f.Diag);
            foreach (TenancySettlementResult r in after)
                Assert.AreEqual("already-settled", r.Outcome);

            // Clear-then-load: loading twice does not duplicate.
            payments2.LoadFromSaveDto(paymentsDto);
            Assert.IsNotNull(payments2.FindObligation(agreement.AgreementId, 0));
        }

        [Test]
        public void AbsentPersonsDoNotHoldAccommodationWithoutAValidArrangement()
        {
            var f = new Fixture();
            string spaceId = f.BuildingWithSpace("b-1", "p-1", 2);
            Assert.IsNull(f.Housing.TryOccupyUnderAgreement(1, 10, spaceId, AccommodationArrangement.Rental,
                0, string.Empty, f.Diag, out _));
            Assert.IsNull(f.Housing.TryOccupyUnderAgreement(2, 11, spaceId, AccommodationArrangement.Rental,
                0, string.Empty, f.Diag, out _));
            Assert.AreEqual(0, f.Housing.VacantSpaces().Count);

            var sweep = new HousingAbsenceSweep();
            var absent = new HashSet<int> { 1 }; // P1 dead/departed; P2 still around
            HousingAbsenceSweepResult result = sweep.Sweep(
                f.Housing, personId => absent.Contains(personId), 10, f.Diag);

            Assert.AreEqual(1, result.PersonsResolved);
            Assert.AreEqual(1, result.OccupanciesEnded);
            Assert.AreEqual(1, f.Housing.OccupantCount(spaceId), "P2 keeps their bed");
            Assert.AreEqual(0, f.Housing.CurrentOccupanciesForPerson(1).Count);

            // P1's departure freed one bed: a newcomer can take it.
            string refusal = f.Housing.TryOccupyUnderAgreement(3, 12, spaceId, AccommodationArrangement.Rental,
                11, string.Empty, f.Diag, out _);
            Assert.IsNull(refusal);
            Assert.AreEqual(2, f.Housing.OccupantCount(spaceId));
        }

        [Test]
        public void AbsenceSweepHonorsAValidArrangement()
        {
            var f = new Fixture();
            string spaceId = f.BuildingWithSpace("b-1", "p-1", 2);
            PropertyAgreement agreement = f.Housing.RecordAgreement("boarding", "House", "P1",
                spaceId, "prepaid weekly", 0, -1, string.Empty, f.Diag);
            Assert.IsNull(f.Housing.TryOccupyUnderAgreement(1, 10, spaceId, AccommodationArrangement.BoardingOrLodging,
                0, agreement.AgreementId, f.Diag, out _));

            var sweep = new HousingAbsenceSweep();
            HousingAbsenceSweepResult result = sweep.Sweep(
                f.Housing, personId => personId == 1, 10, f.Diag,
                hasValidAbsenceArrangement: occ => HousingAbsenceSweep.AgreementStillStands(f.Housing, occ, 10));

            Assert.AreEqual(0, result.PersonsResolved);
            Assert.AreEqual(1, result.ExemptedPersonIds.Count);
            Assert.AreEqual(1, f.Housing.OccupantCount(spaceId), "a standing agreement holds the bed");

            // Once the agreement ends, the same absence resolves.
            Assert.IsNull(f.Housing.EndAgreement(agreement.AgreementId, 11, false, "stay over", f.Diag));
            HousingAbsenceSweepResult result2 = sweep.Sweep(
                f.Housing, personId => personId == 1, 12, f.Diag,
                hasValidAbsenceArrangement: occ => HousingAbsenceSweep.AgreementStillStands(f.Housing, occ, 12));
            Assert.AreEqual(1, result2.PersonsResolved);
            Assert.AreEqual(0, f.Housing.OccupantCount(spaceId));
        }

        [Test]
        public void HouseholdMoveRefusesBeforeAnythingChanges()
        {
            var f = new Fixture();
            string homeSpace = f.BuildingWithSpace("b-1", "p-1", 3);
            string fullSpace = f.BuildingWithSpace("b-2", "p-2", 1);
            var mover = new HouseholdMoveService();

            Assert.IsTrue(mover.MoveHousehold(10, new List<int> { 1, 2 }, homeSpace,
                AccommodationArrangement.Rental, "rental", "Landlord", 0, f.Housing, f.Diag).Moved);
            Assert.IsTrue(mover.MoveHousehold(11, new List<int> { 3 }, fullSpace,
                AccommodationArrangement.Rental, "rental", "Landlord", 0, f.Housing, f.Diag).Moved);

            // H10 (2 people) tries to move into the 1-bed space: refused,
            // and the household is still home — no partial move.
            HouseholdMoveResult failed = mover.MoveHousehold(10, new List<int> { 1, 2 }, fullSpace,
                AccommodationArrangement.Rental, "rental", "Landlord", 5, f.Housing, f.Diag);
            Assert.IsFalse(failed.Moved);
            Assert.IsFalse(string.IsNullOrWhiteSpace(failed.Refusal));
            Assert.AreEqual(2, f.Housing.OccupantCount(homeSpace));
            Assert.AreEqual(1, f.Housing.OccupantCount(fullSpace));

            // A move that fits works and vacates the old space.
            string bigSpace = f.BuildingWithSpace("b-3", "p-3", 4);
            HouseholdMoveResult moved = mover.MoveHousehold(10, new List<int> { 1, 2 }, bigSpace,
                AccommodationArrangement.Rental, "rental", "Landlord", 6, f.Housing, f.Diag);
            Assert.IsTrue(moved.Moved, moved.Refusal);
            Assert.AreEqual(2, f.Housing.OccupantCount(bigSpace));
            Assert.AreEqual(0, f.Housing.OccupantCount(homeSpace));
            Assert.IsFalse(string.IsNullOrWhiteSpace(moved.AgreementId));
        }

        [Test]
        public void DiscoveryOverRealAvailabilityFindsNothingWhenNothingIsVacant()
        {
            var f = new Fixture();
            string spaceId = f.BuildingWithSpace("b-1", "p-1", 1);
            Assert.IsNull(f.Housing.TryOccupyUnderAgreement(1, 10, spaceId, AccommodationArrangement.Rental,
                0, string.Empty, f.Diag, out _));

            var adapter = new HousingOpportunityAdapter(f.Housing);
            Assert.AreEqual(0, adapter.RentalListings(1).Count, "no vacant space means no rental options");
            Assert.AreEqual(0, adapter.BoardingVacancies(1).Count);

            // And the planner fails loudly with reasons instead of inventing.
            var needs = new HousingSearchNeedRegistry();
            HousingSearchNeed need = needs.OpenNeed(12, 1, ResidentialConditionKind.Unsheltered, "test", 0);
            var planner = new HousingSearchPlanner();
            HousingSearchDecision decision = planner.Plan(need,
                new HouseholdCompositionSummary { HouseholdId = 12, AdultCount = 1 },
                10000, 5000, adapter, 0, f.Diag);
            Assert.IsFalse(decision.Resolved);
            Assert.IsFalse(string.IsNullOrWhiteSpace(decision.FailureReason));
        }

        [Test]
        public void UnmetDemandSignalCountsOpenNeeds()
        {
            var needs = new HousingSearchNeedRegistry();
            needs.OpenNeed(10, 3, ResidentialConditionKind.Unsheltered, "evicted", 0);
            needs.OpenNeed(11, 2, ResidentialConditionKind.TemporaryCondition, "fire", 0);
            needs.MarkResolved(11, 1);
            Assert.AreEqual(3, HousingOpportunityAdapter.UnmetSleepingPlaces(needs));
            Assert.AreEqual(0, HousingOpportunityAdapter.UnmetSleepingPlaces(null));
        }
    }
}
