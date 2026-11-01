using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Financing;
using LandLedgers.Orchestration.Player;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

/// <summary>
/// Pass XL phase 9: tests proving the review findings in
/// Docs/PassXlPhase9Review.md, and closing composed-path test gaps
/// found by reading passes 1-8 together. Plain C#,
/// Roslyn-harness runnable. All rates, thresholds, weights,
/// distances, and prices in these tests are TEST PARAMETERS, never
/// canon. The six-person slice gate is tested, not bypassed: no
/// test here turns immigration, traveler arrivals, or outmigration
/// on for the slice.
/// </summary>
[TestFixture]
public sealed class PassXlPhase9ReviewTests
{
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

    private static PersonState TreeParentOf(World world, PersonState resident)
    {
        var diag = new List<string>();
        FamilyTreeBuilder.EnsureCompleteTree(world.Population, world.Kinship, 7, 1900, diag);
        List<int> parents = world.Kinship.GetParents(resident.id);
        Assert.AreEqual(2, parents.Count, "a resident must gain exactly two parents");
        PersonState parent = world.Population.people.Find(p => p.id == parents[0]);
        Assert.IsNotNull(parent);
        Assert.IsTrue(parent.isOffMap, "a tree parent starts off map");
        return parent;
    }

    // ---- P9-01: tree relative straight into SettleAndApply composes ----

    [Test]
    public void TreeRelative_SettleAndApply_Composes_NoDuplicatePerson()
    {
        World world = NewWorld();
        PersonState resident = NewPerson(world.Population, "Ken", "Test", 30, false, false);
        HouseholdState household = NewHousehold(world.Population, "Test");
        PersonState parent = TreeParentOf(world, resident);
        int peopleBefore = world.Population.people.Count;

        ArrivalSettlementOutcome outcome = ArrivalSettlementDriver.SettleAndApply(
            world.Population, world.Lifecycle, world.Memberships, world.Employments,
            NewRequest(parent.id, household.id, true, 1), 5, "clerk",
            CompensationTerms.FromWeeklyWage(900, "test"), new List<string>());

        Assert.IsTrue(outcome.SettledAndApplied, string.Join("; ", outcome.Record.Reasons));
        Assert.IsTrue(outcome.Application.Applied);
        Assert.AreEqual(peopleBefore, world.Population.people.Count, "settlement reuses the tree record, never a second person");
        Assert.IsFalse(parent.isOffMap);
        Assert.AreEqual(household.id, world.Memberships.GetActiveHouseholdId(parent.id));
        Assert.AreEqual(1, world.Memberships.ActiveCount);
        Assert.AreEqual(1, world.Employments.GetByEmployee(parent.id).Count);
    }

    // ---- P9-01: materialise-first then settle is refused ----

    [Test]
    public void MaterialiseFirst_ThenSettle_IsRefused_DocumentedOrder()
    {
        World world = NewWorld();
        PersonState resident = NewPerson(world.Population, "Ken", "Test", 30, false, false);
        HouseholdState household = NewHousehold(world.Population, "Test");
        PersonState parent = TreeParentOf(world, resident);
        Assert.IsTrue(FamilyTreeBuilder.MaterialiseOffMapRelative(
            world.Population, parent.id, household.id, 1, new List<string>()));
        Assert.IsFalse(parent.isOffMap);

        ArrivalSettlementOutcome outcome = ArrivalSettlementDriver.SettleAndApply(
            world.Population, world.Lifecycle, world.Memberships, world.Employments,
            NewRequest(parent.id, household.id, true, 1), 5, "clerk",
            CompensationTerms.FromWeeklyWage(900, "test"), new List<string>());

        Assert.IsFalse(outcome.SettledAndApplied, "settlement must refuse an already on-map person");
        Assert.IsFalse(outcome.Record.Settled);
        Assert.IsNull(outcome.Application, "a refused settlement applies nothing");
        StringAssert.Contains("already on map", string.Join("; ", outcome.Record.Reasons));
    }

    // ---- P9-02: credit obligation + procurement consumption round-trip ----

    [Test]
    public void CreditObligation_AndProcurementConsumption_SurviveDtoRoundTrip()
    {
        var store = new BusinessRuntimeState();
        store.BindBusinessIdentity("general-store-1");
        store.AddCashCents(5000);
        var market = new ExternalMarketAuthority();
        var supplier = new ExternalCounterparty
        {
            CounterpartyId = "test-grain",
            DisplayName = "Test Grain Co.",
            InitialBudgetCents = 50000,
            TransitDays = 1,
        };
        supplier.SupplyUnits["flour"] = 200;
        Assert.IsNull(market.RegisterCounterparty(supplier));
        var service = new OffMapNamedSkuProcurementService("general-store-1", market, new BusinessRuntimeCashPort(store));
        Assert.IsNotNull(service.PlaceOrder("flour", 100, 10, 250, "test-grain", 0, NamedSkuPaymentMode.CashOnArrival, null));
        Assert.AreEqual(1, service.ReceiveArrivals(1, null).Count);

        var ledger = new HouseholdLedger(1);
        var inventory = new HouseholdInventoryRegistry().GetOrCreate(1);
        var authority = new FinancialObligationAuthority();
        var ids = new EntityIdRegistry();
        var book = new TradeCreditBook();
        var diag = new List<string>();
        var policy = new BusinessCreditPolicy
        {
            OfferCredit = true,
            PerSaleLimitCents = 500,
            PerCustomerOutstandingLimitCents = 800,
            TotalOutstandingLimitCents = 1000,
            NetDays = 30,
            BlockNewCreditWhenPastDue = true,
        };
        SliceCreditSaleResult sale = SliceBusinessCredit.SellOnCredit(
            service, policy, authority, ids, book, ledger, inventory, 1, 1, "person:1", "flour", 20, 20, 0, diag);
        Assert.IsTrue(sale.Success, sale.Diagnostic);
        Assert.AreEqual(400, authority.TotalOutstandingFor("household:1"));
        Assert.AreEqual(1, service.LotConsumptions.Count);

        // Restore both DTOs into fresh authorities/services and prove
        // nothing is duplicated or lost: the obligation is still owed
        // once, the consumption is still recorded once, the lot is
        // still 80u sellable, and the same consumption is not replayed.
        var restoredAuthority = new FinancialObligationAuthority();
        restoredAuthority.LoadFromSaveDto(authority.CaptureSaveDto());
        Assert.AreEqual(400, restoredAuthority.TotalOutstandingFor("household:1"));
        Assert.AreEqual(1, restoredAuthority.Obligations.Count);

        var restoredService = new OffMapNamedSkuProcurementService("general-store-1", market, new BusinessRuntimeCashPort(store));
        restoredService.ImportState(service.ExportState());
        Assert.AreEqual(80, restoredService.SellableUnits("flour"));
        Assert.AreEqual(1, restoredService.LotConsumptions.Count);
        restoredService.ImportState(service.ExportState());
        Assert.AreEqual(80, restoredService.SellableUnits("flour"), "re-importing the same DTO must not double-consume");
        Assert.AreEqual(1, restoredService.LotConsumptions.Count);
    }

    // ---- P9-03: fuel sale + intake save/restore ----

    [Test]
    public void FuelSale_ThenIntakeSaveRestore_TotalUnchanged_OverSaleStillRefused()
    {
        var intake = new FuelYardByproductIntake();
        var diag = new List<string>();
        Assert.IsNull(intake.ReceiveByproductLot("fuel-dealer-1", new SlabOffcutFuelLot
        {
            LotId = EntityId.For(EntityKind.Lot, 1),
            FuelWoodUnits = 8,
            SourceMillBusinessId = "mill-1",
            SourceLogLotId = "log-1",
            StandId = "stand-1",
            Species = "white-pine",
            ProducedDayIndex = 0,
        }, diag));
        Assert.AreEqual(8, intake.TotalFuelWoodUnitsFor("fuel-dealer-1"));

        var dealer = new BusinessRuntimeState();
        dealer.BindBusinessIdentity("fuel-dealer-1");
        var ledger = new HouseholdLedger(1);
        Assert.IsNull(ledger.RecordInflow(0, 1000, HouseholdIncomeSource.OtherDocumented,
            "passxl-phase9-test-input", "test funding for a fuel purchase, labelled, never slice earnings", "test-input"));
        var inventory = new HouseholdInventoryRegistry().GetOrCreate(1);
        ThreeBusinessFuelSaleResult sale = ThreeBusinessSlice.SellFuelToHousehold(
            intake, "fuel-dealer-1", dealer, ledger, inventory, 1, 1, 4, 10, 0, diag);
        Assert.IsTrue(sale.Success, sale.Diagnostic);
        Assert.AreEqual(4, intake.TotalFuelWoodUnitsFor("fuel-dealer-1"));

        var restored = new FuelYardByproductIntake();
        restored.LoadFromSaveDto(intake.CaptureSaveDto());
        Assert.AreEqual(4, restored.TotalFuelWoodUnitsFor("fuel-dealer-1"), "the 4u sold stay sold after save/restore");
        ThreeBusinessFuelSaleResult overSale = ThreeBusinessSlice.SellFuelToHousehold(
            restored, "fuel-dealer-1", dealer, ledger, inventory, 1, 1, 5, 10, 0, diag);
        Assert.IsFalse(overSale.Success, "an over-sale after restore must still refuse");
        Assert.AreEqual(4, restored.TotalFuelWoodUnitsFor("fuel-dealer-1"));
    }
}
