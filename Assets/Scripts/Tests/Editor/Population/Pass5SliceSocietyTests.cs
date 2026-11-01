using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// Pass V slices A, B, C, E, G on production authorities:
    /// A: a household's week — wages in, rent paid, food bought, savings
    ///    drawn down honestly, cash conserved across all parties.
    /// B: businesses react to real demand — finite store stock depletes;
    ///    scarcity propagates as open needs, never a magical restock.
    /// C: housing — rent settles from the ledger with exact cents; unpaid
    ///    remainder becomes a real Payable obligation.
    /// E: employment — agreed wages (never status-derived) drive expected
    ///    income and therefore different livelihood decisions.
    /// G: scenario-initialized society — the P1-5 seeder builds three
    ///    households coherently; autonomy decides sensibly for each.
    /// </summary>
    [TestFixture]
    public sealed class Pass5SliceSocietyTests
    {
        private sealed class TestStore : IItemResolvingSupplier
        {
            public string SupplierBusinessId { get; set; } = "store-1";
            public string SupplierName { get; set; } = "General Store";
            public string LocationId { get; set; } = "store";
            public readonly Dictionary<string, int> Stock = new Dictionary<string, int>();
            public readonly Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int CashReceived;

            public bool HasCategory(string categoryId) => true;
            public int StockUnits(string categoryId) => 0;
            public int PricePerUnitCents(string categoryId) => 0;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics) => 0;
            public bool OffersItem(string itemId) => Stock.ContainsKey(itemId);
            public string CategoryForItem(string itemId) => "general_goods";
            public int ItemStockUnits(string itemId) => Stock.TryGetValue(itemId, out int s) ? s : 0;
            public int ItemPricePerUnitCents(string itemId) => Prices.TryGetValue(itemId, out int p) ? p : 0;
            public string CheckTradingCapability(int absoluteDayIndex, int minuteOfDay) => null;
            public string LastReceiptId => "rcpt";

            public int SellItem(string itemId, int requestedUnits, int dayIndex, ShoppingSettlement settlement, List<string> diagnostics)
            {
                int have = ItemStockUnits(itemId);
                int sold = System.Math.Min(System.Math.Max(0, requestedUnits), have);
                if (sold > 0)
                {
                    Stock[itemId] = have - sold;
                    if (settlement != null && settlement.Mode == ShoppingSettlementMode.CashFromLedger)
                        CashReceived += settlement.AmountCents;
                }

                return sold;
            }
        }

        private sealed class FakePayeeDirectory : ITenancyPayeeDirectory
        {
            public readonly Dictionary<string, int> HouseholdPayees = new Dictionary<string, int>();
            public int FindPayeeHouseholdId(string payeeHolderId)
            {
                if (payeeHolderId != null && HouseholdPayees.TryGetValue(payeeHolderId, out int hid)) return hid;
                return -1;
            }

            public ITenancyPayeeCashSink FindPayeeBusinessSink(string payeeHolderId) => null;
        }

        private sealed class World
        {
            public readonly PopulationState Population = new PopulationState();
            public readonly HouseholdMembershipRegistry Memberships = new HouseholdMembershipRegistry();
            public readonly HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public readonly HouseholdInventoryRegistry Inventories = new HouseholdInventoryRegistry();
            public readonly HouseholdNeedRegistry Needs = new HouseholdNeedRegistry();
            public readonly EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public readonly ContactGraphService Contacts = new ContactGraphService();
            public readonly FinancialObligationAuthority Finance = new FinancialObligationAuthority();
            public readonly EntityIdRegistry Ids = new EntityIdRegistry();
            public readonly SupplierDirectory Directory = new SupplierDirectory();
            public readonly JourneyModel Journeys = new JourneyModel();
            public readonly TaskAuthority Tasks = new TaskAuthority();
            public readonly WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public readonly PersonScheduleTracker Schedules = new PersonScheduleTracker();
            public readonly ShopperSellerKnowledge Knowledge = new ShopperSellerKnowledge();
            public readonly HousingAuthority Housing = new HousingAuthority();
            public readonly TenancyPaymentService Tenancy = new TenancyPaymentService();
            public readonly FakePayeeDirectory Payees = new FakePayeeDirectory();
            public readonly HouseholdDecisionService Decisions = new HouseholdDecisionService();
            public readonly HouseholdDecisionDriver DecisionDriver;
            public readonly HouseholdAutonomyDriver Autonomy = new HouseholdAutonomyDriver();
            public readonly HouseholdErrandCoordinator Errands = new HouseholdErrandCoordinator();
            public readonly TestStore Store = new TestStore();
            public readonly List<string> Diag = new List<string>();
            public readonly Dictionary<int, string> Locations = new Dictionary<int, string>();

            public World()
            {
                DecisionDriver = new HouseholdDecisionDriver(Contacts, Employments, null, Finance, Ids);
                Journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Farm", 0f, 0f));
                Journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "Store", 2f, 0f));
                Journeys.AddEdge("home", "store", 2.0f, "road");
                Directory.Register(Store);
                HouseholdShoppingLoop.RegisterTaskDefinitions(Tasks);
            }

            public void AddHousehold(int householdId, string name, int memberPersonId, int openingCashCents)
            {
                Population.households.Add(new HouseholdState { id = householdId, householdName = name });
                Population.people.Add(new PersonState
                {
                    id = memberPersonId, firstName = "P" + memberPersonId, lastName = name, age = 34,
                    ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                    householdId = householdId,
                });
                Memberships.Register(new HouseholdMembership
                {
                    MembershipId = householdId * 100, PersonId = memberPersonId, HouseholdId = householdId,
                    Lifecycle = HouseholdMembershipLifecycle.Active, StartDayIndex = 0,
                });
                if (openingCashCents > 0)
                {
                    Ledgers.GetOrCreate(householdId).RecordInflow(0, openingCashCents,
                        HouseholdIncomeSource.WageEmployment, "emp-seed", "seed wages", "test");
                }

                Knowledge.LearnSupplier(memberPersonId, "store-1", "same town", 0);
                Locations[memberPersonId] = "home";
            }

            public void Employ(int personId, int weeklyWageCents)
            {
                Employments.Register(new EmploymentRelationship
                {
                    Id = "emp-" + personId, EmployeePersonId = personId,
                    EmployerBusinessId = "mill-1", RoleDisplayName = "hand",
                    LifecycleState = EmploymentLifecycleState.Active,
                    Compensation = CompensationTerms.FromWeeklyWage(weeklyWageCents, ""),
                });
            }

            public HouseholdShoppingLoop Loop()
            {
                return new HouseholdShoppingLoop(
                    Population, Memberships, Ledgers, Inventories, Needs, Directory, Journeys,
                    Tasks, Budgets, Schedules, Knowledge,
                    obligations: null, entityIds: null,
                    personEntityId: pid => EntityId.For(EntityKind.Person, pid),
                    spendingPolicyProvider: null);
            }

            public ShoppingLoopOptions Options(int personId)
            {
                return new ShoppingLoopOptions
                {
                    PersonLocationId = pid => Locations.TryGetValue(pid, out string l) ? l : "home",
                    SetPersonLocation = (pid, l) => Locations[pid] = l,
                    DepartureMinuteOfDay = 600,
                    InStoreMinutes = 15,
                    AllowDelivery = false,
                    Diagnostics = Diag,
                };
            }

            public HouseholdErrandCoordinator.CoordinatorContext ErrandsContext(World world)
            {
                return new HouseholdErrandCoordinator.CoordinatorContext
                {
                    Population = Population,
                    Memberships = Memberships,
                    Ledgers = Ledgers,
                    Inventories = Inventories,
                    Needs = Needs,
                    Schedules = Schedules,
                    Tasks = Tasks,
                    Budgets = Budgets,
                    PersonEntityId = pid => EntityId.For(EntityKind.Person, pid),
                };
            }

            public HouseholdAutonomyDriver.AutonomyContext AutonomyContext()
            {
                return new HouseholdAutonomyDriver.AutonomyContext
                {
                    Ledgers = Ledgers,
                    Memberships = Memberships,
                    Employments = Employments,
                    ContactGraph = Contacts,
                    Decisions = Decisions,
                    DecisionDriver = DecisionDriver,
                    FinancialObligations = Finance,
                    Ids = Ids,
                    RentObligationsFor = hid => new List<RentObligationState>(),
                };
            }

            public string SetupTenancy(int tenantPersonId, int tenantHouseholdId, long rentCents, string payeeKey, int payeeHouseholdId)
            {
                Housing.RegisterBuilding("b1", "parcel-1", "house", 0, "test", Diag);
                AccommodationSpace space = Housing.DefineSpace("b1", 4, Diag);
                Assert.IsNotNull(space);
                ResidentialOccupancy occ = Housing.Occupy(tenantPersonId, tenantHouseholdId, space.SpaceId,
                    AccommodationArrangement.Rental, 0, Diag);
                Assert.IsNotNull(occ);
                PropertyAgreement agreement = Housing.RecordAgreement("rental", "Landlord", "Tenant",
                    space.SpaceId, "monthly rent", 0, -1, string.Empty, Diag);
                Assert.IsNotNull(agreement);
                Payees.HouseholdPayees[payeeKey] = payeeHouseholdId;
                Assert.IsNull(Housing.AttachPaymentTerms(agreement.AgreementId, new TenancyPaymentTerms
                {
                    AmountCents = rentCents, PeriodDays = 30, DueDayOfPeriod = 1,
                    StartDay = 0, PayeeHolderId = payeeKey, TenantHouseholdId = tenantHouseholdId,
                }, Diag));
                return agreement.AgreementId;
            }
        }

        [Test]
        public void A_WeekInTheLife_WagesRentFood_Conserved()
        {
            var w = new World();
            w.AddHousehold(7, "Morrow", 71, 8000);
            w.AddHousehold(9, "Landlord", 91, 0);
            w.Employ(71, 2100); // 300c/day
            w.Store.Stock[HouseholdItemCatalog.FlourId] = 100;
            w.Store.Prices[HouseholdItemCatalog.FlourId] = 120;
            w.Store.Stock[HouseholdItemCatalog.SaltId] = 100;
            w.Store.Prices[HouseholdItemCatalog.SaltId] = 40;
            w.SetupTenancy(71, 7, 2500, "household:9", 9);

            var loop = w.Loop();
            var ctx = w.AutonomyContext();

            // Day 0: wages arrive (payroll settlement), autonomy, shopping.
            w.Ledgers.Get(7).RecordInflow(0, 300, HouseholdIncomeSource.WageEmployment, "emp-71", "day wage", "mill-1");
            w.Autonomy.EvaluateHouseholdDay(7, 0, ctx, w.Diag);
            Assert.AreEqual(HouseholdDecisionKind.NoAction, w.Decisions.LastDecisionFor("7").Kind);

            HouseholdPurchasingNeed flour = w.Needs.CreateNeed(7, HouseholdItemCatalog.FlourId, 0);
            flour.UnitsNeeded = 10; flour.IsNecessity = true; flour.Urgency01 = 0.9f;
            HouseholdPurchasingNeed salt = w.Needs.CreateNeed(7, HouseholdItemCatalog.SaltId, 0);
            salt.UnitsNeeded = 5; salt.IsNecessity = true; salt.Urgency01 = 0.8f;

            var plans = w.Errands.PlanDay(7, 0, w.ErrandsContext(w), w.Diag);
            Assert.AreEqual(1, plans.Count);
            var batch = w.Errands.ExecutePlan(plans[0], 0, w.ErrandsContext(w), loop, w.Options(71), w.Diag);
            Assert.IsTrue(batch.TripExecuted, batch.FailureReason);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, flour.Status);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, salt.Status);

            // Day 1: rent due (period 1 due day 30? no — StartDay 0, DueDayOfPeriod 1 → due day 0).
            // Settle at day 0: the first period is already due.
            var results = w.Tenancy.SettleDueRents(w.Housing, 0, w.Ledgers, w.Payees, w.Finance, w.Ids, w.Diag);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("settled", results[0].Outcome);

            // Reconciliation: every cent accounted for, nobody invented.
            int householdBalance = w.Ledgers.Get(7).GetBalanceCents();
            int landlordBalance = w.Ledgers.Get(9).GetBalanceCents();
            int expectedHousehold = 8000 + 300 - (10 * 120 + 5 * 40) - 2500;
            Assert.AreEqual(expectedHousehold, householdBalance);
            Assert.AreEqual(2500, landlordBalance);
            Assert.AreEqual(10 * 120 + 5 * 40, w.Store.CashReceived);
            Assert.AreEqual(8000 + 300, householdBalance + landlordBalance + w.Store.CashReceived,
                "cash conserved across household, landlord, and store");
            Assert.AreEqual(0, w.Finance.Obligations.Count, "no debt when the household can pay");
        }

        [Test]
        public void B_FiniteStock_ScarcityPropagates_NoMagicalRestock()
        {
            var w = new World();
            w.AddHousehold(7, "Morrow", 71, 20000);
            w.AddHousehold(8, "Novak", 81, 20000);
            w.Store.Stock[HouseholdItemCatalog.FlourId] = 12;
            w.Store.Prices[HouseholdItemCatalog.FlourId] = 120;

            var loop = w.Loop();

            // Household 7 buys 10 of 12 units.
            HouseholdPurchasingNeed need7 = w.Needs.CreateNeed(7, HouseholdItemCatalog.FlourId, 0);
            need7.UnitsNeeded = 10; need7.IsNecessity = true; need7.Urgency01 = 0.9f;
            var plans7 = w.Errands.PlanDay(7, 0, w.ErrandsContext(w), w.Diag);
            var batch7 = w.Errands.ExecutePlan(plans7[0], 0, w.ErrandsContext(w), loop, w.Options(71), w.Diag);
            Assert.IsTrue(batch7.TripExecuted, batch7.FailureReason);
            Assert.AreEqual(2, w.Store.ItemStockUnits(HouseholdItemCatalog.FlourId));

            // Household 8 wants 10 but only 2 remain: partial, need stays open.
            HouseholdPurchasingNeed need8 = w.Needs.CreateNeed(8, HouseholdItemCatalog.FlourId, 0);
            need8.UnitsNeeded = 10; need8.IsNecessity = true; need8.Urgency01 = 0.9f;
            var plans8 = w.Errands.PlanDay(8, 0, w.ErrandsContext(w), w.Diag);
            var batch8 = w.Errands.ExecutePlan(plans8[0], 0, w.ErrandsContext(w), loop, w.Options(81), w.Diag);
            Assert.IsTrue(batch8.TripExecuted, batch8.FailureReason);
            Assert.AreEqual(0, w.Store.ItemStockUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(PurchasingNeedStatus.Open, need8.Status, "scarcity leaves the need open — no magical restock");

            // Flour conserved: 12 = 10 (H7) + 2 (H8) + 0 (store).
            int h7flour = w.Inventories.Get(7).GetAvailableUnits(HouseholdItemCatalog.FlourId);
            int h8flour = w.Inventories.Get(8).GetAvailableUnits(HouseholdItemCatalog.FlourId);
            Assert.AreEqual(12, h7flour + h8flour + w.Store.ItemStockUnits(HouseholdItemCatalog.FlourId));
            // Cash conserved: H7 paid 1200, H8 paid 240.
            Assert.AreEqual(1200 + 240, w.Store.CashReceived);
        }

        [Test]
        public void C_RentSettlesExactly_ArrearsBecomeRealPayables()
        {
            var w = new World();
            w.AddHousehold(7, "Morrow", 71, 10000);
            w.AddHousehold(8, "Novak", 81, 1000);
            w.AddHousehold(9, "Landlord", 91, 0);
            w.SetupTenancy(71, 7, 2500, "household:9", 9);
            w.SetupTenancy(81, 8, 2500, "household:9", 9);

            var results = w.Tenancy.SettleDueRents(w.Housing, 0, w.Ledgers, w.Payees, w.Finance, w.Ids, w.Diag);
            Assert.AreEqual(2, results.Count);

            // Household 7 paid in full: exact cents both sides.
            Assert.AreEqual(7500, w.Ledgers.Get(7).GetBalanceCents());
            // Household 8 paid what it had; the remainder is a REAL Payable.
            Assert.AreEqual(0, w.Ledgers.Get(8).GetBalanceCents());
            Assert.AreEqual(3500, w.Ledgers.Get(9).GetBalanceCents());
            int payableCount = 0;
            foreach (FinancialObligation o in w.Finance.Obligations)
            {
                if (o.Kind == FinancialObligationKind.Payable)
                {
                    payableCount++;
                    Assert.AreEqual(1500, o.OutstandingPrincipalCents);
                }
            }

            Assert.AreEqual(1, payableCount, "unpaid rent is a real obligation, not informal debt");
            // Conservation: 11000 started; 7500 + 0 + 3500 = 11000.
            Assert.AreEqual(11000,
                w.Ledgers.Get(7).GetBalanceCents() + w.Ledgers.Get(8).GetBalanceCents() + w.Ledgers.Get(9).GetBalanceCents());
        }

        [Test]
        public void E_AgreedWages_DriveDifferentLivelihoodDecisions()
        {
            var w = new World();
            w.AddHousehold(7, "Morrow", 71, 2000);
            w.AddHousehold(8, "Novak", 81, 400);
            w.Employ(71, 4200); // 600c/day — comfortable
            w.Employ(81, 700);  // 100c/day — tight
            var ctx = w.AutonomyContext();
            ctx.RentObligationsFor = hid => new List<RentObligationState>
            {
                new RentObligationState
                {
                    AgreementId = "agree-" + hid, AmountDueCents = 6000, PaidCents = 0,
                    Status = RentObligationStatus.Current,
                },
            };

            w.Autonomy.EvaluateHouseholdDay(7, 0, ctx, w.Diag);
            w.Autonomy.EvaluateHouseholdDay(8, 0, ctx, w.Diag);

            HouseholdDecision d7 = w.Decisions.LastDecisionFor("7");
            HouseholdDecision d8 = w.Decisions.LastDecisionFor("8");
            Assert.AreEqual(HouseholdDecisionKind.NoAction, d7.Kind,
                "600c/day covers 6000c rent with cushion: " + d7.Reason);
            Assert.AreEqual(HouseholdDecisionKind.Economize, d8.Kind,
                "100c/day cannot cover 6000c rent and nothing else is available: " + d8.Reason);
        }

        [Test]
        public void G_InitializedSociety_IsCoherent_AndDecidesSensibly()
        {
            var w = new World();
            // Three households through the P1-5 seeder: comfortable, tight, lender.
            var specs = new List<StartingHouseholdEconomy.StartingHouseholdSpec>
            {
                new StartingHouseholdEconomy.StartingHouseholdSpec
                {
                    HouseholdId = 7, OpeningCashCents = 8000,
                    OpeningCashSource = "wages saved before the move west",
                    Employments = new List<StartingHouseholdEconomy.StartingEmploymentSpec>
                    {
                        new StartingHouseholdEconomy.StartingEmploymentSpec
                        { PersonId = 71, BusinessId = "mill-1", RoleDisplayName = "hand", WeeklyWageCents = 2100 },
                    },
                    PantryLots = new List<StartingHouseholdEconomy.StartingPantryLotSpec>
                    {
                        new StartingHouseholdEconomy.StartingPantryLotSpec
                        { ItemId = HouseholdItemCatalog.FlourId, Units = 20, Source = "starting pantry (world-gen)" },
                    },
                },
                new StartingHouseholdEconomy.StartingHouseholdSpec
                {
                    HouseholdId = 8, OpeningCashCents = 400,
                    OpeningCashSource = "odd jobs before the move west",
                    Employments = new List<StartingHouseholdEconomy.StartingEmploymentSpec>
                    {
                        new StartingHouseholdEconomy.StartingEmploymentSpec
                        { PersonId = 81, BusinessId = "mill-1", RoleDisplayName = "sweeper", WeeklyWageCents = 700 },
                    },
                    Debts = new List<StartingHouseholdEconomy.StartingDebtSpec>
                    {
                        new StartingHouseholdEconomy.StartingDebtSpec
                        { Creditor = "land office", PrincipalCents = 12000, Terms = "12 monthly installments", Purpose = "land claim" },
                    },
                },
                new StartingHouseholdEconomy.StartingHouseholdSpec
                {
                    HouseholdId = 9, OpeningCashCents = 30000,
                    OpeningCashSource = "sale of the eastern farm",
                    Lender = new StartingHouseholdEconomy.StartingLenderSpec
                    {
                        PersonId = 91, LenderName = "Abel Hart", CapitalCents = 20000,
                        CapitalSource = "inheritance documented in the county will register",
                    },
                },
            };
            w.AddHousehold(7, "Morrow", 71, 0);
            w.AddHousehold(8, "Novak", 81, 0);
            w.AddHousehold(9, "Hart", 91, 0);
            var seederCtx = new StartingHouseholdEconomy.SeederContext
            {
                Ledgers = w.Ledgers, Employments = w.Employments, Obligations = w.Finance,
                Ids = w.Ids, Inventories = w.Inventories, Lenders = new HouseholdLenderDirectory(),
                HouseholdExists = hid => hid == 7 || hid == 8 || hid == 9,
                PersonExists = pid => pid == 71 || pid == 81 || pid == 91,
                BusinessExists = bid => bid == "mill-1",
            };
            int seeded = StartingHouseholdEconomy.Seed(specs, seederCtx, 0, w.Diag);
            Assert.AreEqual(3, seeded);

            List<string> problems = StartingHouseholdEconomy.ValidateCoherence(new[] { 7, 8, 9 }, seederCtx);
            Assert.AreEqual(0, problems.Count, string.Join(" | ", problems));

            // The lender exists for the world the households live in.
            Assert.IsTrue(seederCtx.Lenders.HasLender("Abel Hart"));

            // Autonomy decides sensibly per household on day 0 (rent 6000 due).
            var ctx = w.AutonomyContext();
            ctx.RentObligationsFor = hid => new List<RentObligationState>
            {
                new RentObligationState
                {
                    AgreementId = "agree-" + hid, AmountDueCents = 6000, PaidCents = 0,
                    Status = RentObligationStatus.Current,
                },
            };
            w.Autonomy.EvaluateHouseholdDay(7, 0, ctx, w.Diag);
            w.Autonomy.EvaluateHouseholdDay(8, 0, ctx, w.Diag);

            // Household 7: 8000 + 9000 income vs 6000 obligations → cushion → NoAction.
            Assert.AreEqual(HouseholdDecisionKind.NoAction, w.Decisions.LastDecisionFor("7").Kind);
            // Household 8: 400 + 3000 vs 6000, debt on the books, no known lender → Economize.
            Assert.AreEqual(HouseholdDecisionKind.Economize, w.Decisions.LastDecisionFor("8").Kind);

            // Society cash conserved: 8000 + 400 + 30000 = 38400.
            Assert.AreEqual(38400,
                w.Ledgers.Get(7).GetBalanceCents() + w.Ledgers.Get(8).GetBalanceCents() + w.Ledgers.Get(9).GetBalanceCents());
        }
    }
}
