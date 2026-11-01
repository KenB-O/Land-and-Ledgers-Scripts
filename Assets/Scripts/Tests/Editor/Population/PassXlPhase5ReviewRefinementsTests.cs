using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Postal;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

/// <summary>
/// Pass XL phase 5: tests proving the review refinements in
/// Docs/PassXlPhase5Review.md (findings R01-R06), and proving no double
/// charge, double lot, double person, double delivery, or double
/// departure on the composed paths. Plain C#, Roslyn-harness runnable.
/// All rates, thresholds, weights, distances, and prices in these tests
/// are TEST PARAMETERS, never canon. The six-person slice gate is tested,
/// not bypassed: no test here turns immigration, traveler arrivals, or
/// outmigration on for the slice.
/// </summary>
[TestFixture]
public sealed class PassXlPhase5ReviewRefinementsTests
{
    private sealed class TestPayer : IPostalSenderCashAuthority
    {
        public int Balance;
        public TestPayer(int startingBalance) { Balance = startingBalance; }
        public bool TryPayPostage(int postageCents, int dayIndex, string purpose, List<string> diag)
        {
            if (postageCents < 0 || postageCents > Balance) return false;
            Balance -= postageCents;
            return true;
        }
    }

    private sealed class FakeBusinessCash : IBusinessCashPort
    {
        public int Balance;
        public FakeBusinessCash(int startingBalance) { Balance = startingBalance; }
        public int BalanceCents => Balance;
        public string TryDebit(int amountCents, string reason)
        {
            if (amountCents < 0 || amountCents > Balance) return "test: debit refused";
            Balance -= amountCents;
            return null;
        }
        public string Credit(int amountCents, string reason)
        {
            if (amountCents < 0) return "test: credit refused";
            Balance += amountCents;
            return null;
        }
    }

    private sealed class World
    {
        public PopulationState Population;
        public KinshipRegistry Kinship;
        public HouseholdMembershipRegistry Memberships;
        public EmploymentRelationshipRegistry Employments;
        public HouseholdLifecycleManager Lifecycle;
    }

    private static World NewWorld()
    {
        var population = new PopulationState();
        var kinship = new KinshipRegistry();
        var memberships = new HouseholdMembershipRegistry();
        var employments = new EmploymentRelationshipRegistry();
        var lifecycle = new HouseholdLifecycleManager(population, memberships, kinship, new EntityIdRegistry());
        return new World
        {
            Population = population,
            Kinship = kinship,
            Memberships = memberships,
            Employments = employments,
            Lifecycle = lifecycle,
        };
    }

    private static PersonState NewPerson(PopulationState population, string first, string last, int age, bool isFemale, bool isOffMap)
    {
        var person = new PersonState
        {
            id = population.AllocateNextPersonId(),
            firstName = first,
            lastName = last,
            age = age,
            isFemale = isFemale,
            isOffMap = isOffMap,
            deathDayIndex = -1,
            householdId = -1,
            homeBuildingId = -1,
        };
        population.people.Add(person);
        return person;
    }

    private static HouseholdState NewHousehold(PopulationState population, string surname)
    {
        var household = new HouseholdState
        {
            id = population.AllocateNextHouseholdId(),
            householdName = $"Household {population.NextHouseholdId}",
            surname = surname,
            lifecycleState = HouseholdLifecycleState.Active,
            entityId = EntityId.For(EntityKind.Household, population.NextHouseholdId),
        };
        population.households.Add(household);
        return household;
    }

    private static ArrivalSettlementRequest NewRequest(int personId, int existingHouseholdId, bool withJob, int availableHousing)
    {
        return new ArrivalSettlementRequest
        {
            PersonId = personId,
            HousingUnitId = "unit-1",
            AvailableHousingUnits = availableHousing,
            ExistingHouseholdId = existingHouseholdId,
            JobBusinessId = withJob ? "general-store" : string.Empty,
            OpenJobSlotsAtBusiness = withJob ? 1 : 0,
            ExplicitUnemployedWithPlan = !withJob,
            DayIndex = 5,
        };
    }

    // ---- R01: composed SettleAndApply, with rollback on driver refusal ----

    [Test]
    public void ComposedSettleAndApply_Success_AppliesMembershipAndEmploymentOnce()
    {
        World world = NewWorld();
        HouseholdState household = NewHousehold(world.Population, "Hale");
        PersonState newcomer = NewPerson(world.Population, "Ned", "Hale", 28, false, true);

        ArrivalSettlementOutcome outcome = ArrivalSettlementDriver.SettleAndApply(
            world.Population, world.Lifecycle, world.Memberships, world.Employments,
            NewRequest(newcomer.id, household.id, true, 1), 5, "clerk",
            CompensationTerms.FromWeeklyWage(900, "test"), new List<string>());

        Assert.IsTrue(outcome.Record.Settled);
        Assert.IsTrue(outcome.SettledAndApplied);
        Assert.IsFalse(outcome.RolledBack);
        Assert.IsTrue(outcome.Application.Applied);
        Assert.IsTrue(outcome.Application.HousingAssignmentPending); // residence still Codex
        Assert.IsFalse(newcomer.isOffMap);
        Assert.AreEqual(household.id, world.Memberships.GetActiveHouseholdId(newcomer.id));
        Assert.AreEqual(1, world.Memberships.ActiveCount);
        Assert.IsTrue(outcome.Application.EmploymentCreated);
        Assert.AreEqual(1, world.Employments.GetByEmployee(newcomer.id).Count);
    }

    [Test]
    public void ComposedSettleAndApply_DissolvedHousehold_Refusal_RollsPersonBackOffMap()
    {
        World world = NewWorld();
        HouseholdState household = NewHousehold(world.Population, "Hale");
        household.lifecycleState = HouseholdLifecycleState.Dissolved;
        PersonState newcomer = NewPerson(world.Population, "Ned", "Hale", 28, false, true);

        ArrivalSettlementOutcome outcome = ArrivalSettlementDriver.SettleAndApply(
            world.Population, world.Lifecycle, world.Memberships, world.Employments,
            NewRequest(newcomer.id, household.id, true, 1), 5, "clerk",
            CompensationTerms.FromWeeklyWage(900, "test"), new List<string>());

        Assert.IsTrue(outcome.Record.Settled); // the gate passed; the driver refused
        Assert.IsTrue(outcome.Application.Refused);
        Assert.IsFalse(outcome.SettledAndApplied);
        Assert.IsTrue(outcome.RolledBack);
        Assert.IsTrue(newcomer.isOffMap); // no stranded on-map person
        Assert.AreEqual(-1, newcomer.householdId);
        Assert.AreEqual(-1, newcomer.homeBuildingId);
        Assert.AreEqual(-1, world.Memberships.GetActiveHouseholdId(newcomer.id));
        Assert.AreEqual(0, world.Employments.GetByEmployee(newcomer.id).Count);
    }

    [Test]
    public void ComposedSettleAndApply_MissingHousehold_Refusal_RollsPersonBackOffMap()
    {
        World world = NewWorld();
        PersonState newcomer = NewPerson(world.Population, "Ned", "Hale", 28, false, true);

        ArrivalSettlementOutcome outcome = ArrivalSettlementDriver.SettleAndApply(
            world.Population, world.Lifecycle, world.Memberships, world.Employments,
            NewRequest(newcomer.id, 999, false, 1), 5, string.Empty, null, new List<string>());

        Assert.IsTrue(outcome.Application.Refused);
        Assert.IsTrue(outcome.RolledBack);
        Assert.IsTrue(newcomer.isOffMap);
        Assert.AreEqual(-1, newcomer.householdId);
        Assert.AreEqual(-1, world.Memberships.GetActiveHouseholdId(newcomer.id));
        Assert.AreEqual(0, world.Population.households.Count); // no orphan household created
    }

    [Test]
    public void ComposedSettleAndApply_ConflictingMembership_Refusal_RollsBack_AndKeepsOriginalMembership()
    {
        World world = NewWorld();
        HouseholdState first = NewHousehold(world.Population, "Hale");
        HouseholdState second = NewHousehold(world.Population, "Hale");
        PersonState newcomer = NewPerson(world.Population, "Ned", "Hale", 28, false, true);
        // Off-map person holding a stale active membership elsewhere.
        Assert.IsNull(world.Memberships.Register(new HouseholdMembership
        {
            MembershipId = 1, PersonId = newcomer.id, HouseholdId = first.id,
            Lifecycle = HouseholdMembershipLifecycle.Active, StartDayIndex = 0, EndDayIndex = -1,
            Source = HouseholdMembershipSource.Authored,
        }));

        ArrivalSettlementOutcome outcome = ArrivalSettlementDriver.SettleAndApply(
            world.Population, world.Lifecycle, world.Memberships, world.Employments,
            NewRequest(newcomer.id, second.id, false, 1), 5, string.Empty, null, new List<string>());

        Assert.IsTrue(outcome.Application.Refused);
        Assert.IsTrue(outcome.RolledBack);
        Assert.IsTrue(newcomer.isOffMap);
        Assert.AreEqual(first.id, world.Memberships.GetActiveHouseholdId(newcomer.id)); // untouched, never transferred
    }

    [Test]
    public void ComposedSettleAndApply_UnsettledRequest_NothingMaterialised_DriverNotRun()
    {
        World world = NewWorld();
        HouseholdState household = NewHousehold(world.Population, "Hale");
        PersonState newcomer = NewPerson(world.Population, "Ned", "Hale", 28, false, true);

        ArrivalSettlementOutcome outcome = ArrivalSettlementDriver.SettleAndApply(
            world.Population, world.Lifecycle, world.Memberships, world.Employments,
            NewRequest(newcomer.id, household.id, true, 0), 5, "clerk",
            CompensationTerms.FromWeeklyWage(900, "test"), new List<string>());

        Assert.IsFalse(outcome.Record.Settled);
        Assert.IsNull(outcome.Application); // driver never ran
        Assert.IsFalse(outcome.SettledAndApplied);
        Assert.IsFalse(outcome.RolledBack);
        Assert.IsTrue(newcomer.isOffMap);
        Assert.AreEqual(-1, world.Memberships.GetActiveHouseholdId(newcomer.id));
    }

    // ---- R05: employment ids include the business, so same-day jobs do not collide ----

    [Test]
    public void SettlementDriver_TwoBusinessesSameDay_BothEmploymentsRegistered_DistinctIds()
    {
        World world = NewWorld();
        HouseholdState household = NewHousehold(world.Population, "Hale");
        PersonState newcomer = NewPerson(world.Population, "Ned", "Hale", 28, false, true);
        var first = new ArrivalSettlementRecord
        {
            Settled = true, PersonId = newcomer.id, HouseholdId = household.id,
            IsNewHousehold = false, JobBusinessId = "general-store",
        };
        ArrivalSettlementApplication applied = ArrivalSettlementDriver.Apply(
            world.Population, world.Lifecycle, world.Memberships, world.Employments,
            first, 5, "clerk", CompensationTerms.FromWeeklyWage(900, "test"), new List<string>());
        Assert.IsTrue(applied.Applied);
        Assert.IsTrue(applied.EmploymentCreated);

        // Second settled record, same person, same household, same day,
        // different business: the AlreadyApplied path must still create it.
        var second = new ArrivalSettlementRecord
        {
            Settled = true, PersonId = newcomer.id, HouseholdId = household.id,
            IsNewHousehold = false, JobBusinessId = "bakery",
        };
        ArrivalSettlementApplication again = ArrivalSettlementDriver.Apply(
            world.Population, world.Lifecycle, world.Memberships, world.Employments,
            second, 5, "baker", CompensationTerms.FromWeeklyWage(800, "test"), new List<string>());
        Assert.IsTrue(again.AlreadyApplied); // membership already in place, nothing duplicated
        Assert.IsTrue(again.EmploymentCreated); // but the second business's job IS created

        List<EmploymentRelationship> jobs = world.Employments.GetByEmployee(newcomer.id);
        Assert.AreEqual(2, jobs.Count);
        Assert.AreNotEqual(jobs[0].Id, jobs[1].Id);
        Assert.AreEqual(1, world.Memberships.ActiveCount); // still exactly one membership
    }

    // ---- R02: letter poster origin cross-check ----

    private static PostalService PostalWithOffices(params string[] officeIds)
    {
        var postal = new PostalService();
        var diag = new List<string>();
        foreach (string officeId in officeIds)
            Assert.IsNull(postal.RegisterOffice(officeId, "town-square", "biz-" + officeId, new List<int> { 0 }, 7, diag));
        return postal;
    }

    private static RecruitmentLetterRequest LetterRequest(int recipientId, string recipientName)
    {
        return new RecruitmentLetterRequest
        {
            SenderPersonId = 1,
            SenderName = "Jacob Boniecki",
            RecipientPersonId = recipientId,
            RecipientName = recipientName,
            BusinessId = "general-store",
            Origin = "general-store",
            Purpose = "move-to-town",
            DayIndex = 5,
        };
    }

    [Test]
    public void LetterPoster_BlankOriginOffice_FallsBackToRequestOrigin()
    {
        PostalService postal = PostalWithOffices("general-store");
        var payer = new TestPayer(100);

        MailItem posted = RecruitmentLetterPoster.PostLetter(
            LetterRequest(42, "Anna Hale"), postal, string.Empty, null, payer, 5, new List<string>());
        Assert.IsNotNull(posted);
        Assert.AreEqual("general-store", posted.FromOfficeId);
        Assert.AreEqual(100 - PostalService.LetterPostageCents, payer.Balance); // exactly one postage debit
    }

    [Test]
    public void LetterPoster_MismatchedOriginOffice_Refused_NoCharge()
    {
        PostalService postal = PostalWithOffices("general-store", "other-office");
        var payer = new TestPayer(100);

        MailItem posted = RecruitmentLetterPoster.PostLetter(
            LetterRequest(42, "Anna Hale"), postal, "other-office", null, payer, 5, new List<string>());
        Assert.IsNull(posted);
        Assert.AreEqual(100, payer.Balance); // refusal happens before any postage debit
    }

    // ---- R03: quote bridge refuses an Unspecified freight option ----

    [Test]
    public void QuoteToOrder_UnspecifiedFreightOption_Refused_NothingOrdered()
    {
        var market = new ExternalMarketAuthority();
        var supplier = new ExternalCounterparty
        {
            CounterpartyId = "test-iron",
            DisplayName = "Test Iron Co.",
            InitialBudgetCents = 50000,
            TransitDays = 2,
        };
        supplier.SupplyUnits["nails"] = 200;
        Assert.IsNull(market.RegisterCounterparty(supplier));
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, new FakeBusinessCash(0));
        var quote = new OffMapFreightQuote
        {
            SupplierId = "test-iron", SupplierName = "Test Iron Co.", DistanceMiles = 100,
            SkuId = "nails", QuantityUnits = 10, FreightOption = FreightOptionKind.Unspecified,
            GoodsCents = 600, FreightCents = 300, DeliveredTotalCents = 900,
        };

        NamedSkuOrder order = OffMapQuoteToOrder.PlaceOrderFromQuote(
            quote, service, 10, NamedSkuPaymentMode.CashOnArrival, null, new List<string>());
        Assert.IsNull(order);
        Assert.AreEqual(0, service.Orders.Count);
        Assert.AreEqual(200, market.GetCounterparty("test-iron").SupplyUnits["nails"]); // supply untouched
    }

    // ---- R04: traveler channel respects reputation staleness ----

    private static TownReputationSnapshot Reputation(double score, int computedDay)
    {
        return new TownReputationSnapshot { Score01 = score, ComputedDayIndex = computedDay };
    }

    private static ImmigrationThresholds Thresholds()
    {
        return new ImmigrationThresholds { MinOpenJobs = 1, MinHousingVacancies = 1, MinReputationScore01 = 0.5 };
    }

    private static ThroughTravelerVisit LookingVisit()
    {
        return new ThroughTravelerVisit
        {
            TravelerId = "t-1", TravelerName = "Westbound Traveler", RouteDirection = "westbound",
            LookingForStay = true, DayIndex = 10, BoardingVacancies = 0,
            VisitedGeneralStore = true, VisitedLivery = false,
        };
    }

    [Test]
    public void Traveler_AtDay_StaleReputation_PassesOn_NoWordOfMouth()
    {
        TravelerOutcome outcome = ThroughTravelerImmigration.EvaluateVisitAtDay(
            LookingVisit(), Reputation(0.8, 0), 10, 7, 2, 2, true, Thresholds());
        Assert.AreEqual(TravelerOutcomeKind.PassesOn, outcome.Outcome);
        Assert.IsFalse(outcome.WordOfMouthSpread); // no word from an untrusted score
    }

    [Test]
    public void Traveler_AtDay_FreshReputation_Delegates_StaysWhenConditionsPass()
    {
        TravelerOutcome outcome = ThroughTravelerImmigration.EvaluateVisitAtDay(
            LookingVisit(), Reputation(0.8, 5), 10, 7, 2, 2, true, Thresholds());
        Assert.AreEqual(TravelerOutcomeKind.Stays, outcome.Outcome);
        Assert.IsTrue(outcome.WordOfMouthSpread);
    }

    [Test]
    public void Traveler_AtDay_MaxAgeBoundary_StillEvaluated()
    {
        // IsStale uses strictly-greater-than: exactly maxAge old is fresh.
        TravelerOutcome outcome = ThroughTravelerImmigration.EvaluateVisitAtDay(
            LookingVisit(), Reputation(0.8, 5), 12, 7, 2, 2, true, Thresholds());
        Assert.AreEqual(TravelerOutcomeKind.Stays, outcome.Outcome);
    }

    [Test]
    public void Traveler_AtDay_SliceGate_StillSuppressesArrival()
    {
        TravelerOutcome outcome = ThroughTravelerImmigration.EvaluateVisitAtDay(
            LookingVisit(), Reputation(0.8, 5), 10, 7, 2, 2, false, Thresholds());
        Assert.AreEqual(TravelerOutcomeKind.PassesOn, outcome.Outcome);
    }

    // ---- R06: outmigration closeout mirrors MigrateOut for membership + employment ----

    private static World WorldWithResidentAndJob(out PersonState resident, out HouseholdState household)
    {
        World world = NewWorld();
        household = NewHousehold(world.Population, "Hale");
        resident = NewPerson(world.Population, "Grace", "Hale", 30, true, false);
        Assert.IsNull(world.Lifecycle.AddMember(household.id, resident.id, 0, HouseholdMembershipSource.Authored, "test household"));
        Assert.IsNull(world.Employments.Register(new EmploymentRelationship
        {
            Id = "job-1", EmployeePersonId = resident.id, EmployerBusinessId = "general-store",
            RoleDisplayName = "clerk", Kind = EmploymentKind.Permanent,
            LifecycleState = EmploymentLifecycleState.Active,
            Compensation = CompensationTerms.FromWeeklyWage(900, "test"),
            StartDayIndex = 0, EndDayIndex = -1, Source = EmploymentSource.Authored, LegacyNotes = "test",
        }));
        return world;
    }

    private static OutmigrationRecord DepartResident(World world, PersonState resident)
    {
        OutmigrationRecord record = Outmigration.Depart(world.Population, world.Kinship, new OutmigrationRequest
        {
            PersonId = resident.id, Reason = OutmigrationReason.WorkDriedUp, HasWork = false,
            Destination = new OutmigrationDestination
            {
                DestinationId = "supplier-near", DestinationName = "Near Supplier Town",
                DestinationKind = "supplier-town", ReasonForChoice = "work at the supplier town",
            },
            DayIndex = 12, AllowDepartures = true,
        }, new List<string>());
        Assert.IsTrue(record.Departed);
        Assert.IsTrue(resident.isOffMap);
        return record;
    }

    [Test]
    public void OutmigrationCloseout_DepartedPerson_EndsMembershipAndEmployment_StillRequiresHouseholdPropertyDebt()
    {
        World world = WorldWithResidentAndJob(out PersonState resident, out HouseholdState household);
        OutmigrationRecord record = DepartResident(world, resident);

        OutmigrationCloseoutResult close = OutmigrationCloseout.CloseAfterDeparture(
            world.Population, world.Lifecycle, world.Employments, record, 13, new List<string>());

        Assert.IsTrue(close.MembershipEnded);
        Assert.AreEqual(1, close.EmploymentsEnded);
        Assert.AreEqual(-1, world.Memberships.GetActiveHouseholdId(resident.id));
        Assert.AreEqual(0, world.Employments.GetActiveByEmployee(resident.id).Count);
        Assert.AreEqual(EmploymentEndReason.MigratedOut, world.Employments.GetByEmployee(resident.id)[0].EndReason);
        Assert.AreEqual(3, close.StillRequired.Count); // household, property, debts: still the driver's decisions (F08)
        Assert.IsNotNull(world.Population.GetPerson(resident.id)); // identity preserved, nothing deleted
        Assert.AreEqual(household.id, world.Population.GetHousehold(household.id).id); // household state preserved
    }

    [Test]
    public void OutmigrationCloseout_SecondClose_DuplicatesNothing()
    {
        World world = WorldWithResidentAndJob(out PersonState resident, out _);
        OutmigrationRecord record = DepartResident(world, resident);
        OutmigrationCloseout.CloseAfterDeparture(
            world.Population, world.Lifecycle, world.Employments, record, 13, new List<string>());

        OutmigrationCloseoutResult again = OutmigrationCloseout.CloseAfterDeparture(
            world.Population, world.Lifecycle, world.Employments, record, 14, new List<string>());
        Assert.IsFalse(again.MembershipEnded);
        Assert.AreEqual(0, again.EmploymentsEnded);
        Assert.AreEqual(1, world.Employments.GetByEmployee(resident.id).Count); // history retained, not duplicated
    }

    [Test]
    public void OutmigrationCloseout_UndepartedRecord_ClosesNothing()
    {
        World world = WorldWithResidentAndJob(out PersonState resident, out HouseholdState household);
        var record = new OutmigrationRecord { Departed = false, PersonId = resident.id };

        OutmigrationCloseoutResult close = OutmigrationCloseout.CloseAfterDeparture(
            world.Population, world.Lifecycle, world.Employments, record, 13, new List<string>());
        Assert.IsFalse(close.MembershipEnded);
        Assert.AreEqual(0, close.EmploymentsEnded);
        Assert.AreEqual(household.id, world.Memberships.GetActiveHouseholdId(resident.id));
        Assert.AreEqual(1, world.Employments.GetActiveByEmployee(resident.id).Count);
    }
}
