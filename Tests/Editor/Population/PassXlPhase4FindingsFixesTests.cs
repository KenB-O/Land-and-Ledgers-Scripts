using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Postal;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Time;
using NUnit.Framework;

/// <summary>
/// Pass XL phase 4: tests proving the Stage 2 fixes for the findings in
/// Docs/PassXlPhase4Findings.md, and proving no double charge, double
/// lot, double person, or double departure. Plain C#, Roslyn-harness
/// runnable. All rates, thresholds, weights, distances, and prices in
/// these tests are TEST PARAMETERS, never canon. The six-person slice
/// gate is tested, not bypassed: no test here turns immigration,
/// traveler arrivals, or outmigration on for the slice.
/// </summary>
[TestFixture]
public sealed class PassXlPhase4FindingsFixesTests
{
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

    private static ArrivalSettlementRecord SettleNewcomer(
        World world, PersonState newcomer, int existingHouseholdId, bool withJob)
    {
        var request = new ArrivalSettlementRequest
        {
            PersonId = newcomer.id,
            HousingUnitId = "unit-1",
            AvailableHousingUnits = 1,
            ExistingHouseholdId = existingHouseholdId,
            JobBusinessId = withJob ? "general-store" : string.Empty,
            OpenJobSlotsAtBusiness = withJob ? 1 : 0,
            ExplicitUnemployedWithPlan = !withJob,
            DayIndex = 5,
        };
        var diag = new List<string>();
        ArrivalSettlementRecord record = ArrivalSettlement.Settle(world.Population, request, diag);
        Assert.IsTrue(record.Settled, "Test setup: settlement must pass.");
        return record;
    }

    // ---- F02: ArrivalSettlementDriver ----

    [Test]
    public void SettlementDriver_ExistingHousehold_AppliesOnce_SecondApplyDuplicatesNothing()
    {
        World world = NewWorld();
        HouseholdState household = NewHousehold(world.Population, "Hale");
        PersonState newcomer = NewPerson(world.Population, "Ned", "Hale", 28, false, true);
        ArrivalSettlementRecord record = SettleNewcomer(world, newcomer, household.id, true);

        var diag = new List<string>();
        ArrivalSettlementApplication applied = ArrivalSettlementDriver.Apply(
            world.Population, world.Lifecycle, world.Memberships, world.Employments,
            record, 5, "clerk", CompensationTerms.FromWeeklyWage(900, "test"), diag);
        Assert.IsTrue(applied.Applied);
        Assert.IsFalse(applied.Refused);
        Assert.AreEqual(household.id, world.Memberships.GetActiveHouseholdId(newcomer.id));
        Assert.AreEqual(1, world.Memberships.ActiveCount);
        Assert.IsTrue(applied.EmploymentCreated);
        Assert.AreEqual(1, world.Employments.GetByEmployee(newcomer.id).Count);
        Assert.IsTrue(applied.HousingAssignmentPending); // residence is Codex work, never claimed

        ArrivalSettlementApplication again = ArrivalSettlementDriver.Apply(
            world.Population, world.Lifecycle, world.Memberships, world.Employments,
            record, 5, "clerk", CompensationTerms.FromWeeklyWage(900, "test"), new List<string>());
        Assert.IsTrue(again.AlreadyApplied);
        Assert.AreEqual(1, world.Memberships.ActiveCount); // no duplicate membership
        Assert.AreEqual(1, world.Employments.GetByEmployee(newcomer.id).Count); // no duplicate employment
        Assert.AreEqual(1, world.Population.households.Count);
    }

    [Test]
    public void SettlementDriver_NewHousehold_UsesPreallocatedId_NoSecondHousehold()
    {
        World world = NewWorld();
        PersonState newcomer = NewPerson(world.Population, "Ned", "Hale", 28, false, true);
        ArrivalSettlementRecord record = SettleNewcomer(world, newcomer, -1, true);
        Assert.IsTrue(record.IsNewHousehold);

        ArrivalSettlementApplication applied = ArrivalSettlementDriver.Apply(
            world.Population, world.Lifecycle, world.Memberships, world.Employments,
            record, 5, "clerk", CompensationTerms.FromWeeklyWage(900, "test"), new List<string>());
        Assert.IsTrue(applied.Applied);
        Assert.IsTrue(applied.HouseholdCreated);
        Assert.AreEqual(1, world.Population.households.Count);
        Assert.IsNotNull(world.Population.GetHousehold(record.HouseholdId));
        Assert.AreEqual(record.HouseholdId, world.Memberships.GetActiveHouseholdId(newcomer.id));
    }

    [Test]
    public void SettlementDriver_UnsettledRecord_Refused_NothingWritten()
    {
        World world = NewWorld();
        PersonState newcomer = NewPerson(world.Population, "Ned", "Hale", 28, false, true);
        var record = new ArrivalSettlementRecord { Settled = false, PersonId = newcomer.id, HouseholdId = 1 };

        ArrivalSettlementApplication result = ArrivalSettlementDriver.Apply(
            world.Population, world.Lifecycle, world.Memberships, world.Employments,
            record, 5, "clerk", CompensationTerms.FromWeeklyWage(900, "test"), new List<string>());
        Assert.IsTrue(result.Refused);
        Assert.AreEqual(0, world.Memberships.ActiveCount);
        Assert.AreEqual(0, world.Population.households.Count);
        Assert.AreEqual(0, world.Employments.GetByEmployee(newcomer.id).Count);
        Assert.IsTrue(newcomer.isOffMap); // still off map, no silent arrival
    }

    [Test]
    public void SettlementDriver_NoCompensation_EmploymentPending_NotInvented()
    {
        World world = NewWorld();
        HouseholdState household = NewHousehold(world.Population, "Hale");
        PersonState newcomer = NewPerson(world.Population, "Ned", "Hale", 28, false, true);
        ArrivalSettlementRecord record = SettleNewcomer(world, newcomer, household.id, true);

        ArrivalSettlementApplication applied = ArrivalSettlementDriver.Apply(
            world.Population, world.Lifecycle, world.Memberships, world.Employments,
            record, 5, "clerk", null, new List<string>());
        Assert.IsTrue(applied.Applied); // household join still applies
        Assert.IsTrue(applied.EmploymentPending);
        Assert.IsFalse(applied.EmploymentCreated);
        Assert.AreEqual(0, world.Employments.GetByEmployee(newcomer.id).Count);
    }

    [Test]
    public void SettlementDriver_ConflictingActiveMembership_Refused_NotTransferred()
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
        ArrivalSettlementRecord record = SettleNewcomer(world, newcomer, second.id, false);

        ArrivalSettlementApplication result = ArrivalSettlementDriver.Apply(
            world.Population, world.Lifecycle, world.Memberships, world.Employments,
            record, 5, string.Empty, null, new List<string>());
        Assert.IsTrue(result.Refused);
        Assert.AreEqual(first.id, world.Memberships.GetActiveHouseholdId(newcomer.id)); // untouched
    }

    // ---- F01: FamilyTreeWiring seam ----

    [Test]
    public void FamilyTreeWiring_ApplyAfterGeneration_IsIdempotent()
    {
        var population = new PopulationState();
        var kinship = new KinshipRegistry();
        NewPerson(population, "Jacob", "Boniecki", 34, false, false);
        NewPerson(population, "Grace", "Boniecki", 30, true, false);

        FamilyTreeBuilder.Report first = FamilyTreeWiring.ApplyAfterGeneration(
            population, kinship, seed: 7, referenceYear: 1880, new List<string>());
        Assert.IsTrue(first.ParentsCreated >= 4);
        int peopleAfterFirst = population.people.Count;
        int edgesAfterFirst = kinship.EdgeCount;

        FamilyTreeBuilder.Report second = FamilyTreeWiring.ApplyAfterGeneration(
            population, kinship, seed: 7, referenceYear: 1880, new List<string>());
        Assert.AreEqual(0, second.ParentsCreated);
        Assert.AreEqual(0, second.GrandparentsCreated);
        Assert.AreEqual(0, second.SiblingEdgesAdded);
        Assert.AreEqual(peopleAfterFirst, population.people.Count); // no duplicate persons
        Assert.AreEqual(edgesAfterFirst, kinship.EdgeCount); // no duplicate edges
    }

    // ---- F05: OffMapQuoteToOrder bridge ----

    private static ExternalMarketAuthority MarketWithNails(int units)
    {
        var market = new ExternalMarketAuthority();
        var supplier = new ExternalCounterparty
        {
            CounterpartyId = "test-iron",
            DisplayName = "Test Iron Co.",
            InitialBudgetCents = 50000,
            TransitDays = 2,
        };
        supplier.SupplyUnits["nails"] = units;
        Assert.IsNull(market.RegisterCounterparty(supplier));
        return market;
    }

    private static OffMapFreightQuote Quote(
        string sku, string supplier, int qty, int goods, int freight,
        FreightOptionKind option, int total)
    {
        return new OffMapFreightQuote
        {
            SupplierId = supplier, SupplierName = "Test Iron Co.", DistanceMiles = 100,
            SkuId = sku, QuantityUnits = qty, FreightOption = option,
            GoodsCents = goods, FreightCents = freight, DeliveredTotalCents = total,
        };
    }

    [Test]
    public void QuoteToOrder_SellerFreightQuote_PlacesExactOrder_NoDoubleCharge()
    {
        ExternalMarketAuthority market = MarketWithNails(200);
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, new FakeBusinessCash(0));

        NamedSkuOrder order = OffMapQuoteToOrder.PlaceOrderFromQuote(
            Quote("nails", "test-iron", 10, 600, 300, FreightOptionKind.SellerFreight, 900),
            service, 10, NamedSkuPaymentMode.CashOnArrival, null, new List<string>());
        Assert.IsNotNull(order);
        Assert.AreEqual(900, order.TotalCostCents);
        Assert.AreEqual(600, order.UnitsOrdered * order.UnitGoodsCostCents);
        Assert.AreEqual(300, order.FreightSurchargeCents);
        Assert.AreEqual(1, service.Orders.Count);
        Assert.AreEqual(190, market.GetCounterparty("test-iron").SupplyUnits["nails"]); // finite supply debited once
    }

    [Test]
    public void QuoteToOrder_FreightBusinessQuote_PassesFreightAsSurchargeOnly()
    {
        ExternalMarketAuthority market = MarketWithNails(200);
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, new FakeBusinessCash(0));

        NamedSkuOrder order = OffMapQuoteToOrder.PlaceOrderFromQuote(
            Quote("nails", "test-iron", 10, 600, 350, FreightOptionKind.FreightBusiness, 950),
            service, 10, NamedSkuPaymentMode.CashOnArrival, null, new List<string>());
        Assert.IsNotNull(order);
        Assert.AreEqual(950, order.TotalCostCents);
        Assert.AreEqual(350, order.FreightSurchargeCents);
        // The bridge creates no freight business object: the only artifact is the order.
        Assert.AreEqual(1, service.Orders.Count);
    }

    [Test]
    public void QuoteToOrder_TamperedTotal_Refused_NothingOrdered()
    {
        ExternalMarketAuthority market = MarketWithNails(200);
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, new FakeBusinessCash(0));

        NamedSkuOrder order = OffMapQuoteToOrder.PlaceOrderFromQuote(
            Quote("nails", "test-iron", 10, 600, 300, FreightOptionKind.SellerFreight, 850),
            service, 10, NamedSkuPaymentMode.CashOnArrival, null, new List<string>());
        Assert.IsNull(order);
        Assert.AreEqual(0, service.Orders.Count);
        Assert.AreEqual(200, market.GetCounterparty("test-iron").SupplyUnits["nails"]);
    }

    [Test]
    public void QuoteToOrder_SelfCollect_Refused_NotTeleported()
    {
        ExternalMarketAuthority market = MarketWithNails(200);
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, new FakeBusinessCash(0));

        NamedSkuOrder order = OffMapQuoteToOrder.PlaceOrderFromQuote(
            Quote("nails", "test-iron", 10, 600, 0, FreightOptionKind.SelfCollect, 600),
            service, 10, NamedSkuPaymentMode.CashOnArrival, null, new List<string>());
        Assert.IsNull(order);
        Assert.AreEqual(0, service.Orders.Count);
        Assert.AreEqual(200, market.GetCounterparty("test-iron").SupplyUnits["nails"]);
    }

    [Test]
    public void QuoteToOrder_NonDivisibleGoods_Refused_NoCentsRoundedAway()
    {
        ExternalMarketAuthority market = MarketWithNails(200);
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, new FakeBusinessCash(0));

        NamedSkuOrder order = OffMapQuoteToOrder.PlaceOrderFromQuote(
            Quote("nails", "test-iron", 10, 605, 300, FreightOptionKind.SellerFreight, 905),
            service, 10, NamedSkuPaymentMode.CashOnArrival, null, new List<string>());
        Assert.IsNull(order);
        Assert.AreEqual(0, service.Orders.Count);
    }

    [Test]
    public void QuoteToOrder_UnknownSupplier_Refused_NothingOrdered()
    {
        ExternalMarketAuthority market = MarketWithNails(200);
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, new FakeBusinessCash(0));

        NamedSkuOrder order = OffMapQuoteToOrder.PlaceOrderFromQuote(
            Quote("nails", "no-such-supplier", 10, 600, 300, FreightOptionKind.SellerFreight, 900),
            service, 10, NamedSkuPaymentMode.CashOnArrival, null, new List<string>());
        Assert.IsNull(order);
        Assert.AreEqual(0, service.Orders.Count);
        Assert.AreEqual(200, market.GetCounterparty("test-iron").SupplyUnits["nails"]);
    }

    // ---- F03: RecruitmentLetterPoster ----

    private static PostalService PostalWithGeneralStoreOffice()
    {
        var postal = new PostalService();
        var diag = new List<string>();
        Assert.IsNull(postal.RegisterOffice("general-store", "town-square", "biz-gs", new List<int> { 0 }, 7, diag));
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
    public void LetterPoster_RegisteredOffice_PostsLetter_PostageDebitedOnce()
    {
        PostalService postal = PostalWithGeneralStoreOffice();
        var payer = new TestPayer(100);

        MailItem posted = RecruitmentLetterPoster.PostLetter(
            LetterRequest(42, "Anna Hale"), postal, "general-store", null, payer, 5, new List<string>());
        Assert.IsNotNull(posted);
        Assert.AreEqual(MailKind.Letter, posted.Kind);
        Assert.AreEqual("general-store", posted.FromOfficeId);
        Assert.AreEqual(PostalService.OffMapOfficeId, posted.ToOfficeId);
        Assert.AreEqual(42, posted.RecipientPersonId);
        Assert.AreEqual(100 - PostalService.LetterPostageCents, payer.Balance); // exactly one postage debit
    }

    [Test]
    public void LetterPoster_UnregisteredOrigin_Refused_NoCharge()
    {
        var postal = new PostalService(); // no offices registered: the F04 production state
        var payer = new TestPayer(100);

        MailItem posted = RecruitmentLetterPoster.PostLetter(
            LetterRequest(42, "Anna Hale"), postal, "general-store", null, payer, 5, new List<string>());
        Assert.IsNull(posted);
        Assert.AreEqual(100, payer.Balance);
    }

    [Test]
    public void LetterPoster_BrokePayer_Refused_NoMail()
    {
        PostalService postal = PostalWithGeneralStoreOffice();
        var payer = new TestPayer(0); // cannot pay 3c postage

        MailItem posted = RecruitmentLetterPoster.PostLetter(
            LetterRequest(42, "Anna Hale"), postal, "general-store", null, payer, 5, new List<string>());
        Assert.IsNull(posted);
        Assert.AreEqual(0, payer.Balance);
    }

    [Test]
    public void LetterPoster_NullPayer_Refused_NoFreeCarriage()
    {
        PostalService postal = PostalWithGeneralStoreOffice();

        MailItem posted = RecruitmentLetterPoster.PostLetter(
            LetterRequest(42, "Anna Hale"), postal, "general-store", null, null, 5, new List<string>());
        Assert.IsNull(posted);
    }

    // ---- F09: AllowDepartures gate mechanism (NOT wired to the slice) ----

    [Test]
    public void DepartureGate_DefaultsToTrue()
    {
        Assert.IsTrue(new DemographicDriverContext().AllowDepartures);
    }

    private static (World, PersonState) DistressedAdultWorld()
    {
        World world = NewWorld();
        // MigrateIn only fires into an Active host household; without one
        // no arrival can ever occur and the arrivals-gate test is vacuous.
        NewHousehold(world.Population, "Hale");
        var person = new PersonState
        {
            id = world.Population.AllocateNextPersonId(),
            firstName = "Distressed",
            lastName = "Hale",
            age = 40,
            isFemale = false,
            isOffMap = false,
            deathDayIndex = -1,
            householdId = -1,
            homeBuildingId = -1,
            laborUrgency01 = 1.0f,
            birthDate = new BirthDate(1800, 1, 1, BirthDateProvenance.GeneratedExact, "test"),
        };
        world.Population.people.Add(person);
        return (world, person);
    }

    private static DemographicDriverContext DepartureContext(World world, int seed, bool allowDepartures)
    {
        return new DemographicDriverContext
        {
            Population = world.Population,
            Lifecycle = world.Lifecycle,
            Anchor = CalendarAnchor.Default,
            CurrentDayIndex = 100,
            WorldSeed = seed,
            MaleNames = new[] { "Test" },
            FemaleNames = new[] { "Test" },
            Diagnostics = new List<string>(),
            AllowNewArrivals = false,
            AllowDepartures = allowDepartures,
        };
    }

    [Test]
    public void DepartureGate_False_SuppressesDepartures_ThatFireWhenTrue()
    {
        int firingSeed = -1;
        for (int seed = 0; seed < 5000; seed++)
        {
            (World probe, PersonState person) = DistressedAdultWorld();
            MigrationDriver.EvaluateMonth(DepartureContext(probe, seed, true));
            if (person.isOffMap)
            {
                firingSeed = seed;
                break;
            }
        }
        Assert.IsTrue(firingSeed >= 0, "No seed in 0..4999 produced a departure; the gate cannot be exercised.");

        (World suppressed, PersonState stayed) = DistressedAdultWorld();
        DemographicDriverContext ctx = DepartureContext(suppressed, firingSeed, false);
        MigrationDriver.EvaluateMonth(ctx);
        Assert.IsFalse(stayed.isOffMap); // no departure on the gated path
        bool sawSuppression = false;
        foreach (string line in ctx.Diagnostics)
            if (line.Contains("departures suppressed")) sawSuppression = true;
        Assert.IsTrue(sawSuppression, "Expected a departures-suppressed diagnostic on the gated run.");
    }

    [Test]
    public void DepartureGate_ArrivalsGate_UnaffectedByNewFlag()
    {
        // The arrivals gate still works exactly as before: find a seed
        // where an arrival fires with the gate open, then confirm the
        // same seed adds nobody with the gate closed — regardless of
        // the new AllowDepartures flag (left true here).
        int firingSeed = -1;
        for (int seed = 0; seed < 5000; seed++)
        {
            (World probe, PersonState person) = DistressedAdultWorld();
            DemographicDriverContext open = DepartureContext(probe, seed, true);
            open.AllowNewArrivals = true;
            MigrationDriver.EvaluateMonth(open);
            if (probe.Population.people.Count == 2)
            {
                firingSeed = seed;
                break;
            }
        }
        Assert.IsTrue(firingSeed >= 0, "No seed in 0..4999 produced an arrival; the gate cannot be exercised.");

        (World world, PersonState person2) = DistressedAdultWorld();
        DemographicDriverContext ctx = DepartureContext(world, firingSeed, true);
        ctx.AllowNewArrivals = false;
        MigrationDriver.EvaluateMonth(ctx);
        Assert.AreEqual(1, world.Population.people.Count); // no arrival added
    }
}
