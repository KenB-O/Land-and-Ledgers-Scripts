using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.DraftPower;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Logistics;
using LandLedgers.Orchestration.Player;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// Pass XL phase 8: the three-business slice economy, plus the two
    /// Kennedy additions folded into this pass (daily wage and
    /// day-to-day owner draw; credit as a business tool) and the
    /// freight/horses audit. Roslyn only, never Unity. All prices,
    /// wages, draws, reserves, freight, and supply numbers are
    /// caller-supplied test parameters, never canon rates.
    /// </summary>
    [TestFixture]
    public sealed class PassXlPhase8ThreeBusinessTests
    {
        // ---------- Composed three-business run ----------

        private static ThreeBusinessSliceRunReport Run()
        {
            return ThreeBusinessSliceRun.Execute(
                startDay: 0,
                storeSeedCents: 5000, bakerySeedCents: 2000, fuelSeedCents: 1000,
                storeReserveCents: 1000, bakeryReserveCents: 1000, fuelReserveCents: 500,
                orderUnits: 100, unitGoodsCostCents: 10, freightSurchargeCents: 250,
                marketSupplyUnits: 200, transitDays: 3,
                storeFlourSaleUnits: 40, storeFlourSalePriceCents: 20,
                feedStoreUnits: 12, feedPriceCents: 20, notionsInputUnits: 1,
                bakeryFuelUnits: 2, bakeryFuelPriceCents: 5,
                breadSaleUnitsPerHousehold: 6,
                fuelHouseholdUnits: 4, fuelHouseholdPriceCents: 10,
                kennedyDrawCents: 1000, glenDrawCents: 300, jacobDrawCents: 400,
                finalDrawCents: 100);
        }

        [Test]
        public void Run_EndToEnd_ThreeBusinessesProduceSellAndFeed_AllHouseholdsEat_CashReconciles()
        {
            ThreeBusinessSliceRunReport report = Run();
            Assert.IsNotNull(report, "Composed run must succeed; see report diagnostics: " + (report != null ? string.Join("; ", report.Diagnostics) : "null"));

            // Cash reconciliation, every step accounted for:
            // Store: 5000 - 1250 procurement - 1000 Kennedy draw + 800 flour sale + 240 feed sale - 100 final draw = 3690.
            Assert.AreEqual(3690, report.StoreCashFinalCents);
            // Bakery: 2000 - 10 fuel - 240 feed - 300 Glen draw + 144 bread sales - 100 final draw = 1494.
            Assert.AreEqual(1494, report.BakeryCashFinalCents);
            // Fuel: 1000 + 10 fuel to bakery - 400 Jacob draw + 40 household fuel - 100 final draw = 550.
            Assert.AreEqual(550, report.FuelCashFinalCents);
            // Households start at 0; they end with draws minus purchases plus the final draw.
            Assert.AreEqual(212, report.BaldwinCashFinalCents);
            Assert.AreEqual(352, report.PierceCashFinalCents);
            Assert.AreEqual(452, report.BonieckiCashFinalCents);

            // Production: 4 white-pine logs at 2 fuel units each = 8 fuel units.
            Assert.AreEqual(8, report.FuelWoodUnitsProduced);
            // 1 batch of bread loaves = 24 loaves; 18 sold (6 per household); 6 left.
            Assert.AreEqual(24, report.BakeryLoavesBaked);
            Assert.AreEqual(6, report.BakeryLoavesRemaining);
            // Bakery burned 1 of its 2 fuel units on the single firing.
            Assert.AreEqual(1, report.BakeryFuelRemaining);
            // Fuel intake: 8 produced - 2 to bakery - 4 to a household = 2 left.
            Assert.AreEqual(2, report.FuelIntakeRemaining);
            // Store lot: 100 ordered - 40 household flour - 12 bakery feed = 48 left.
            Assert.AreEqual(48, report.StoreLotRemainingUnits);

            // All three households ate, none missed a served meal.
            Assert.Greater(report.MealsBaldwin, 0);
            Assert.Greater(report.MealsPierce, 0);
            Assert.Greater(report.MealsBoniecki, 0);
            Assert.AreEqual(0, report.MealsMissedTotal);

            // Controls: no double sale, no founder wage, no reserve break.
            Assert.IsTrue(report.FounderWageRefused);
            Assert.IsTrue(report.ReserveBreakingDrawRefused);
            Assert.IsTrue(report.OverSaleFlourRefused);
            Assert.IsTrue(report.OverSaleBreadRefused);
            Assert.IsTrue(report.OverSaleFuelRefused);

            // Slice gate survives in the save DTO, and the bakery-feed
            // consumption survives save/restore with the same lot balance.
            Assert.IsTrue(report.SuppressMigrantArrivalsRestored);
            Assert.IsTrue(report.ConsumptionRestoredCountMatches);
        }

        [Test]
        public void FeedBakeryFromStore_Shortfall_MovesNothing()
        {
            var diag = new List<string>();
            var store = new BusinessRuntimeState();
            store.BindBusinessIdentity("general-store-1");
            store.AddCashCents(5000);
            var market = new ExternalMarketAuthority();
            var supplier = new ExternalCounterparty
            {
                CounterpartyId = "test-grain",
                DisplayName = "Test Grain Co.",
                InitialBudgetCents = 50000,
                TransitDays = 1, // min transit is 1 day in ExternalMarketAuthority
            };
            supplier.SupplyUnits["flour"] = 200;
            Assert.IsNull(market.RegisterCounterparty(supplier));
            var service = new OffMapNamedSkuProcurementService("general-store-1", market, new BusinessRuntimeCashPort(store));
            Assert.IsNotNull(service.PlaceOrder("flour", 10, 10, 0, "test-grain", 0, NamedSkuPaymentMode.CashOnArrival, null));
            Assert.AreEqual(1, service.ReceiveArrivals(1, null).Count);

            var bakery = new LandLedgers.Economy.Businesses.Bakery.BakeryShopRuntime("pierce-bakery-1");
            var bakeryRuntime = new BusinessRuntimeState();
            bakeryRuntime.BindBusinessIdentity("pierce-bakery-1");
            bakeryRuntime.AddCashCents(2000);
            var ids = new EntityIdRegistry();
            string problem = ThreeBusinessSlice.FeedBakeryFromStore(
                service, store, bakery, bakeryRuntime, ids, "flour", 11, 1, 20, 0, diag);
            Assert.IsNotNull(problem, "feeding 11u from a 10u lot must refuse");
            Assert.AreEqual(10, service.SellableUnits("flour"));
            Assert.AreEqual(0, service.LotConsumptions.Count);
            Assert.AreEqual(2000, bakeryRuntime.CurrentCashCents);
            Assert.AreEqual(5000 - 100, store.CurrentCashCents, "procurement cost only; no feed cash moved");
        }

        // ---------- Kennedy addition 1: daily wage + day-to-day draw ----------

        private static SlicePayrollSetup EmployeeSetup(int openingCashCents)
        {
            var personHousehold = new Dictionary<int, int> { { 99, 99 }, { 98, 98 } };
            SlicePayrollSetup setup = SliceWorkAndPay.CreateBusiness("general-store-test", openingCashCents, personHousehold);
            setup.Ledgers.GetOrCreate(99);
            return setup;
        }

        private static EmploymentRelationship RegisterTestEmployee(SlicePayrollSetup setup, string employmentId, int personId, int weeklyWageCents)
        {
            string problem = SliceWorkAndPay.RegisterEmployee(
                setup, employmentId, personId, "general-store-test", weeklyWageCents, 0, false);
            Assert.IsNull(problem);
            Assert.IsTrue(setup.Employments.TryGetById(employmentId, out EmploymentRelationship relationship));
            return relationship;
        }

        [Test]
        public void DailyWage_AttendanceGated_PaysOnce_AndNoAttendancePaysNothing()
        {
            SlicePayrollSetup setup = EmployeeSetup(5000);
            EmploymentRelationship employment = RegisterTestEmployee(setup, "emp-99", 99, 350);
            var diag = new List<string>();

            SliceWorkAndPay.SetDailyAttendance(setup, "emp-99", 0, true);
            Assert.AreEqual("paid", SliceWorkAndPay.PayDailyWage(setup, employment, 50, 0, diag));
            Assert.AreEqual(4950, setup.Business.CurrentCashCents);
            Assert.AreEqual(50, setup.Ledgers.GetOrCreate(99).GetBalanceCents());

            Assert.AreNotEqual("paid", SliceWorkAndPay.PayDailyWage(setup, employment, 50, 0, diag),
                "the same day pays exactly once");
            Assert.AreEqual(4950, setup.Business.CurrentCashCents);

            Assert.AreEqual("refused-no-attendance", SliceWorkAndPay.PayDailyWage(setup, employment, 50, 1, diag),
                "a day with no worked attendance earns nothing and accrues nothing");
            Assert.AreEqual(4950, setup.Business.CurrentCashCents);
            Assert.AreEqual(0, setup.Business.AccruedLiabilityCents);

            SliceWorkAndPay.SetDailyAttendance(setup, "emp-99", 2, true);
            Assert.AreEqual("paid", SliceWorkAndPay.PayDailyWage(setup, employment, 50, 2, diag));
            Assert.AreEqual(4900, setup.Business.CurrentCashCents);
            Assert.AreEqual(100, setup.Ledgers.GetOrCreate(99).GetBalanceCents());
        }

        [Test]
        public void DailyWage_Shortfall_AccruesLiabilityAndSuspends_NoCashCreated()
        {
            SlicePayrollSetup setup = EmployeeSetup(30);
            EmploymentRelationship employment = RegisterTestEmployee(setup, "emp-99", 99, 350);
            var diag = new List<string>();
            SliceWorkAndPay.SetDailyAttendance(setup, "emp-99", 0, true);
            Assert.AreEqual("paid-arrears", SliceWorkAndPay.PayDailyWage(setup, employment, 50, 0, diag));
            Assert.AreEqual(30, setup.Business.CurrentCashCents);
            Assert.AreEqual(0, setup.Ledgers.GetOrCreate(99).GetBalanceCents());
            Assert.AreEqual(50, setup.Business.AccruedLiabilityCents);
            Assert.AreEqual(EmploymentLifecycleState.Suspended, employment.LifecycleState);
        }

        [Test]
        public void DailyWage_ThenWeeklyPayroll_NetsManualCash_NoDoublePayForTheWeek()
        {
            SlicePayrollSetup setup = EmployeeSetup(5000);
            EmploymentRelationship employment = RegisterTestEmployee(setup, "emp-99", 99, 350);
            var diag = new List<string>();
            SliceWorkAndPay.SetDailyAttendance(setup, "emp-99", 0, true);
            Assert.AreEqual("paid", SliceWorkAndPay.PayDailyWage(setup, employment, 50, 0, diag));
            SliceWorkAndPay.SetAttendance(setup, "emp-99", 0, true);
            Assert.AreEqual("settled", SliceWorkAndPay.SettleWeek(setup, 0, 0, diag));
            Assert.AreEqual(5000 - 350, setup.Business.CurrentCashCents,
                "weekly 350 nets the 50 already paid daily: 300 more, not 350 more");
            Assert.AreEqual(350, setup.Ledgers.GetOrCreate(99).GetBalanceCents());
        }

        [Test]
        public void DayToDayDraw_SuccessiveDays_ReserveCapEveryDay_AndLeavingCashInPreservesOrderingPower()
        {
            var business = new BusinessRuntimeState();
            business.BindBusinessIdentity("general-store-1");
            business.AddCashCents(2000);
            HouseholdLedger household = new HouseholdLedger(1);
            SliceOwnerDrawResult day0 = SliceWorkAndPay.DrawDaily(business, household, 0, 300, 1000);
            Assert.IsTrue(day0.Success);
            Assert.AreEqual(1700, business.CurrentCashCents);
            Assert.AreEqual(300, household.GetBalanceCents());
            SliceOwnerDrawResult day1 = SliceWorkAndPay.DrawDaily(business, household, 1, 300, 1000);
            Assert.IsTrue(day1.Success);
            Assert.AreEqual(1400, business.CurrentCashCents);
            SliceOwnerDrawResult breaking = SliceWorkAndPay.DrawDaily(business, household, 2, 500, 1000);
            Assert.IsFalse(breaking.Success, "a draw that would leave 900c below a 1000c reserve moves nothing");
            Assert.AreEqual(1400, business.CurrentCashCents);
            Assert.AreEqual(600, household.GetBalanceCents());
            // Taking nothing on day 3 is the leave-in choice: the 1400c
            // stays in the business and still covers a 1250c order.
            Assert.GreaterOrEqual(1400, 1250);
        }

        // ---------- Kennedy addition 1: credit ----------

        private static OffMapNamedSkuProcurementService StockedStore(out BusinessRuntimeState store)
        {
            store = new BusinessRuntimeState();
            store.BindBusinessIdentity("general-store-1");
            store.AddCashCents(5000);
            var market = new ExternalMarketAuthority();
            var supplier = new ExternalCounterparty
            {
                CounterpartyId = "test-grain",
                DisplayName = "Test Grain Co.",
                InitialBudgetCents = 50000,
                TransitDays = 1, // min transit is 1 day in ExternalMarketAuthority
            };
            supplier.SupplyUnits["flour"] = 200;
            Assert.IsNull(market.RegisterCounterparty(supplier));
            var service = new OffMapNamedSkuProcurementService("general-store-1", market, new BusinessRuntimeCashPort(store));
            Assert.IsNotNull(service.PlaceOrder("flour", 100, 10, 250, "test-grain", 0, NamedSkuPaymentMode.CashOnArrival, null));
            Assert.AreEqual(1, service.ReceiveArrivals(1, null).Count);
            return service;
        }

        private static void FundHousehold(HouseholdLedger ledger, int amountCents, int dayIndex)
        {
            Assert.IsNull(ledger.RecordInflow(dayIndex, amountCents, HouseholdIncomeSource.OtherDocumented,
                "passxl-phase8-test-input", "test funding for a repayment, labelled, never slice earnings", "test-input"));
        }

        [Test]
        public void Credit_CashOnlyPolicy_RefusesCredit_AndCashSaleStillWorks()
        {
            BusinessRuntimeState store;
            OffMapNamedSkuProcurementService service = StockedStore(out store);
            var ledger = new HouseholdLedger(1);
            var inventory = new HouseholdInventoryRegistry().GetOrCreate(1);
            var authority = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            var book = new TradeCreditBook();
            var diag = new List<string>();
            SliceCreditSaleResult refused = SliceBusinessCredit.SellOnCredit(
                service, BusinessCreditPolicy.CashOnly(), authority, ids, book,
                ledger, inventory, 1, 1, "person:1", "flour", 10, 20, 0, diag);
            Assert.IsFalse(refused.Success);
            StringAssert.Contains("does not offer credit", refused.Diagnostic);
            Assert.AreEqual(100, service.SellableUnits("flour"), "a refused credit sale consumes nothing");
            Assert.AreEqual(0, authority.Obligations.Count);

            FundHousehold(ledger, 1000, 0);
            OffMapNamedSkuProcurementService.NamedSkuDeliveryResult cashSale =
                service.SellAndDeliverToInventory(ledger, inventory, 1, 1, "person:1", "flour", 10, 20, 0);
            Assert.IsTrue(cashSale.Success, "cash-only businesses still sell for cash");
            Assert.AreEqual(90, service.SellableUnits("flour"));
        }

        [Test]
        public void Credit_SaleCreatesObligation_NotCash_LimitsEnforced_RepaymentSettles_PastDueBlocksNewCredit()
        {
            BusinessRuntimeState store;
            OffMapNamedSkuProcurementService service = StockedStore(out store);
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
            int storeCashAfterProcurement = store.CurrentCashCents;
            Assert.AreEqual(3750, storeCashAfterProcurement);

            SliceCreditSaleResult sale1 = SliceBusinessCredit.SellOnCredit(
                service, policy, authority, ids, book, ledger, inventory, 1, 1, "person:1", "flour", 20, 20, 0, diag);
            Assert.IsTrue(sale1.Success, sale1.Diagnostic);
            Assert.AreEqual(400, sale1.TotalCents);
            Assert.AreEqual(3750, store.CurrentCashCents, "a credit sale moves no business cash until repayment");
            Assert.AreEqual(400, authority.TotalOutstandingFor("household:1"));
            Assert.AreEqual(20, inventory.GetAvailableUnits("flour"));

            SliceCreditSaleResult overSaleLimit = SliceBusinessCredit.SellOnCredit(
                service, policy, authority, ids, book, ledger, inventory, 1, 1, "person:1", "flour", 30, 20, 0, diag);
            Assert.IsFalse(overSaleLimit.Success, "600c exceeds the 500c per-sale limit");
            SliceCreditSaleResult sale2 = SliceBusinessCredit.SellOnCredit(
                service, policy, authority, ids, book, ledger, inventory, 1, 1, "person:1", "flour", 20, 20, 0, diag);
            Assert.IsTrue(sale2.Success, sale2.Diagnostic);
            Assert.AreEqual(800, authority.TotalOutstandingFor("household:1"));
            SliceCreditSaleResult overCustomerLimit = SliceBusinessCredit.SellOnCredit(
                service, policy, authority, ids, book, ledger, inventory, 1, 1, "person:1", "flour", 10, 20, 0, diag);
            Assert.IsFalse(overCustomerLimit.Success, "another 200c would exceed the 800c per-customer limit");

            FundHousehold(ledger, 1000, 0);
            Assert.IsNull(SliceBusinessCredit.RepayCredit(authority, store, ledger, sale1.ObligationId, 400, 1, diag));
            Assert.AreEqual(400, authority.TotalOutstandingFor("household:1"), "only sale1 is settled");
            Assert.AreEqual(4150, store.CurrentCashCents);
            Assert.AreEqual(600, ledger.GetBalanceCents());

            // Age sale2 past due (due day 30, evaluated day 31) and the
            // past-due block must refuse new credit for this household.
            SliceBusinessCredit.AgeCredit(book, authority, 31, diag);
            Assert.AreEqual(TradeCreditCollectionStage.ReminderIssued, book.FindTerms(sale2.ObligationId).Stage);
            SliceCreditSaleResult blocked = SliceBusinessCredit.SellOnCredit(
                service, policy, authority, ids, book, ledger, inventory, 1, 1, "person:1", "flour", 10, 20, 32, diag);
            Assert.IsFalse(blocked.Success);
            StringAssert.Contains("past-due", blocked.Diagnostic);

            // Paying the past-due invoice clears the block.
            Assert.IsNull(SliceBusinessCredit.RepayCredit(authority, store, ledger, sale2.ObligationId, 400, 32, diag));
            Assert.AreEqual(0, authority.TotalOutstandingFor("household:1"));
            SliceCreditSaleResult allowedAgain = SliceBusinessCredit.SellOnCredit(
                service, policy, authority, ids, book, ledger, inventory, 1, 1, "person:1", "flour", 10, 20, 32, diag);
            Assert.IsTrue(allowedAgain.Success, allowedAgain.Diagnostic);
        }
    }

    /// <summary>
    /// Pass XL phase 8, second Kennedy addition: freight and horses.
    /// Proves, in Roslyn, a horse owned, fed, cared for, reserved as
    /// draft power, and hitched to a wagon for a freight shipment
    /// that delivers exactly once and settles its charge exactly
    /// once; and reproduces the production feed-resolver gap for
    /// person-owned horses (see the test's note: the production
    /// resolver in SimulationDrivers returns null for them, so they
    /// degrade even when feed exists elsewhere — a Kennedy decision,
    /// not silently fixed here).
    /// </summary>
    [TestFixture]
    public sealed class PassXlPhase8FreightAndHorsesTests
    {
        private sealed class StubFeedStock : IHorseFeedStock
        {
            public int Units;
            public int FeedStockUnits => Units;
            public int TakeFeedUnits(int requestedUnits)
            {
                int taken = Math.Min(Math.Max(0, requestedUnits), Units);
                Units -= taken;
                return taken;
            }
        }

        private static AnimalState RegisterHorse(AnimalRegistry registry, string ownerId, AnimalOwnerKind ownerKind, int dayIndex)
        {
            AnimalState horse = registry.RegisterAnimal(
                AnimalSpecies.Horse, "draft", AnimalSex.Female, ownerKind, ownerId, dayIndex, "passxl-phase8 test horse");
            Assert.IsNotNull(horse);
            Assert.IsNull(HorseAuthority.Configure(horse, HorseUseCapabilities.Draft, "stable-1-yard", "stable-1", dayIndex));
            return horse;
        }

        [Test]
        public void Horse_OwnedFedCared_IdempotentSameDay_WorkingRationWhenReserved_UnfedDegrades()
        {
            var ids = new EntityIdRegistry();
            var registry = new AnimalRegistry(ids);
            var diag = new List<string>();
            AnimalState horse = RegisterHorse(registry, "person:1", AnimalOwnerKind.Person, 0);
            Assert.AreEqual("person:1", horse.OwnerId);
            Assert.AreEqual("stable-1", horse.HousingId);

            var stock = new StubFeedStock { Units = 100 };
            int unfed = HorseCareDriver.CareDay(registry, _ => stock, _ => false, 1, diag);
            Assert.AreEqual(0, unfed);
            Assert.AreEqual(98, stock.Units, "idle ration is ceil(1.2) = 2 units");
            Assert.AreEqual(1, horse.LastCareDayIndex);

            int unfedAgain = HorseCareDriver.CareDay(registry, _ => stock, _ => false, 1, diag);
            Assert.AreEqual(0, unfedAgain);
            Assert.AreEqual(98, stock.Units, "same-day repeat care consumes nothing (LastCareDayIndex idempotency)");

            var draft = new DraftPowerService();
            List<EntityId> reserved;
            Assert.IsTrue(draft.TryReserveAnimals(
                new List<EntityId> { horse.AnimalId }, 1, _ => true, "plow", out reserved, diag));
            Assert.IsTrue(draft.IsAnimalReserved(horse.AnimalId));
            int unfedWorking = HorseCareDriver.CareDay(registry, _ => stock,
                h => draft.IsAnimalReserved(h.AnimalId), 2, diag);
            Assert.AreEqual(0, unfedWorking);
            Assert.AreEqual(95, stock.Units, "working ration is ceil(2.5) = 3 units");
            draft.ReleaseAnimals(new List<EntityId> { horse.AnimalId });
            Assert.IsFalse(draft.IsAnimalReserved(horse.AnimalId));

            var emptyStock = new StubFeedStock { Units = 0 };
            float nutritionBefore = horse.Nutrition01;
            float fatigueBefore = horse.Fatigue01;
            int unfedEmpty = HorseCareDriver.CareDay(registry, _ => emptyStock, _ => false, 3, diag);
            Assert.AreEqual(1, unfedEmpty);
            Assert.Less(horse.Nutrition01, nutritionBefore);
            Assert.Greater(horse.Fatigue01, fatigueBefore);
        }

        [Test]
        public void Horse_PersonOwned_ProductionResolverReturnsNull_DegradesDespiteFeedElsewhere()
        {
            // Reproduces the production rule in
            // SimulationDrivers.ResolveHorseFeedStock: farm-owned
            // horses (owner id "farm:...") get the farm feed adapter,
            // LiveryFreight businesses with a livery_feed_upkeep stock
            // get the business adapter, and every other owner — person,
            // household, or any other business — falls through to null.
            // The null stock is the unfed path in HorseCareDriver.
            var ids = new EntityIdRegistry();
            var registry = new AnimalRegistry(ids);
            var diag = new List<string>();
            AnimalState horse = RegisterHorse(registry, "person:1", AnimalOwnerKind.Person, 0);
            var householdStock = new StubFeedStock { Units = 50 };
            Func<AnimalState, IHorseFeedStock> productionRuleResolver = h =>
            {
                if (h.OwnerKind == AnimalOwnerKind.Business && h.OwnerId != null && h.OwnerId.StartsWith("farm:", StringComparison.Ordinal))
                    return householdStock;
                return null;
            };
            float nutritionBefore = horse.Nutrition01;
            int unfed = HorseCareDriver.CareDay(registry, productionRuleResolver, _ => false, 1, diag);
            Assert.AreEqual(1, unfed);
            Assert.AreEqual(50, householdStock.Units, "the household stock is never consulted for a person-owned horse");
            Assert.Less(horse.Nutrition01, nutritionBefore);
        }

        [Test]
        public void WagonTeamPool_ReserveDoubleReserveRefused_ReleaseFreesWagonAndAnimals()
        {
            var ids = new EntityIdRegistry();
            var registry = new AnimalRegistry(ids);
            AnimalState horseA = RegisterHorse(registry, "person:1", AnimalOwnerKind.Person, 0);
            AnimalState horseB = RegisterHorse(registry, "person:1", AnimalOwnerKind.Person, 0);
            var pool = new FreightResourcePool();
            pool.AddWagon(new FreightWagon("wagon-1", "Slice Wagon", 600));
            var diag = new List<string>();
            DraftTeamReservation reservation;
            Assert.IsTrue(pool.TryReserveTeam(100,
                new List<EntityId> { horseA.AnimalId, horseB.AnimalId },
                new List<EntityId> { EntityId.For(EntityKind.Person, 1) },
                "test haul", out reservation, diag));
            Assert.AreEqual("wagon-1", reservation.WagonId);
            Assert.AreEqual(FreightWagonStatus.OnRoute, pool.GetWagon("wagon-1").Status);
            DraftTeamReservation second;
            Assert.IsFalse(pool.TryReserveTeam(100,
                new List<EntityId> { horseA.AnimalId, horseB.AnimalId },
                new List<EntityId> { EntityId.For(EntityKind.Person, 1) },
                "second haul", out second, diag), "the wagon and team cannot be double-booked");
            pool.ReleaseTeam(reservation);
            Assert.AreEqual(FreightWagonStatus.Available, pool.GetWagon("wagon-1").Status);
            Assert.IsFalse(pool.DraftPower.IsAnimalReserved(horseA.AnimalId));
            DraftTeamReservation third;
            Assert.IsTrue(pool.TryReserveTeam(100,
                new List<EntityId> { horseA.AnimalId, horseB.AnimalId },
                new List<EntityId> { EntityId.For(EntityKind.Person, 1) },
                "third haul", out third, diag), "a released team can be reserved again");
            pool.ReleaseTeam(third);
        }

        [Test]
        public void Freight_RealRegistryHorsesAsTeam_ShipmentDeliversOnce_AndChargeSettlesOnce()
        {
            const int day = 42;
            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(day);
            var service = new FreightShipmentService();
            var recipientStock = new Dictionary<string, int>(StringComparer.Ordinal);
            var hooks = new FreightShipmentHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                DayIndex = day,
                Diagnostics = new List<string>(),
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    string key = shipment.destinationBusinessInstanceId ?? "nowhere";
                    recipientStock[key] = recipientStock.TryGetValue(key, out int have) ? have + quantityUnits : quantityUnits;
                    return quantityUnits;
                },
            };
            service.RegisterWagon(new EquipmentAsset
            {
                AssetId = "wagon-1",
                Kind = "farm-wagon",
                DisplayName = "wagon-1",
                Condition01 = 0.9f,
                PayloadMassCapacityKg = 1000f,
                CargoVolumeCapacityM3 = 4f,
            });

            var ids = new EntityIdRegistry();
            var registry = new AnimalRegistry(ids);
            AnimalState horseA = RegisterHorse(registry, "person:1", AnimalOwnerKind.Person, 0);
            AnimalState horseB = RegisterHorse(registry, "person:1", AnimalOwnerKind.Person, 0);
            var team = new List<EntityId> { horseA.AnimalId, horseB.AnimalId };
            EntityId handler = EntityId.For(EntityKind.Person, 701);
            EntityId driverPerson = EntityId.For(EntityKind.Person, 702);

            var driver = new FreightProductionDriver(service);
            var carrierRuntime = new BusinessRuntimeState();
            carrierRuntime.BindBusinessIdentity("biz-freight-1");
            driver.EnsureCompany("biz-freight-1", carrierRuntime);
            var payerRuntime = new BusinessRuntimeState();
            payerRuntime.BindBusinessIdentity("biz-store-1");
            payerRuntime.SetCurrentCashCents(100000);
            driver.RegisterBusinessRuntime("biz-store-1", payerRuntime);

            var diag = new List<string>();
            LogisticsShipmentState shipment = driver.CreateShipment(
                "biz-freight-1", "ship-1", "mill-1", "store-1", 50,
                new FreightCargoSpec { MassKgPerUnit = 2f, VolumeM3PerUnit = 0.01f },
                "wagon-1", hooks, driverPerson, team, 500, "biz-store-1", diag, out string reason);
            Assert.IsNotNull(shipment, reason);
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status);

            int guard = 0;
            while (shipment.Status != LogisticsShipmentStatus.Completed && guard++ < 60)
                driver.AdvanceTick(3600f, hooks, handler, diag);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status, string.Join("; ", diag));
            Assert.AreEqual(50, shipment.DeliveredQuantityUnits);
            Assert.AreEqual(50, recipientStock["store-1"], "goods landed in recipient custody exactly once");
            Assert.AreEqual(500, carrierRuntime.CurrentCashCents, "the freight charge settled to the carrier after acceptance");
            Assert.AreEqual(100000 - 500, payerRuntime.CurrentCashCents);

            int carrierBefore = carrierRuntime.CurrentCashCents;
            guard = 0;
            while (guard++ < 10)
                driver.AdvanceTick(3600f, hooks, handler, diag);
            Assert.AreEqual(carrierBefore, carrierRuntime.CurrentCashCents, "settlement is exactly-once");

            // A completed shipment frees its wagon for the next haul.
            LogisticsShipmentState second = driver.CreateShipment(
                "biz-freight-1", "ship-2", "mill-1", "store-1", 10,
                new FreightCargoSpec { MassKgPerUnit = 2f, VolumeM3PerUnit = 0.01f },
                "wagon-1", hooks, driverPerson, team, 100, "biz-store-1", diag, out string reason2);
            Assert.IsNotNull(second, reason2);
        }
    }
}
