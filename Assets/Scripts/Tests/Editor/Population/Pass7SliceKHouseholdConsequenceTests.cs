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

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// Pass VII slice K: household economic consequence, proved end to end
    /// against the production authorities. Existing coverage proves the first
    /// half of the chain:
    /// - EstateLifecycleDriverTests.SliceE_SurvivingHouseholdFinancesReflectActualIncomeLoss
    ///   (provider death: the household keeps exactly what was earned, no
    ///   replacement income, surviving employment untouched).
    /// - Pass5SliceHardshipTests.I_IncomeLoss_LeadsToEconomize_NoHiddenRescue
    ///   (job loss: the household economizes, a real 50% discretionary
    ///   spending policy exists, no obligations invented, no cash appeared).
    /// The gap both left open is the BUSINESS side: the household's reduced
    /// spending must produce real demand changes at real businesses, and a
    /// genuine cost change (not just income loss) must drive the same
    /// consequence chain. These tests close that gap:
    /// income loss -> economize -> halved discretionary purchases -> the
    /// store sells fewer units (real demand drop) -> cash conserved across
    /// the boundary, no phantom income manufactured anywhere.
    /// </summary>
    [TestFixture]
    public sealed class Pass7SliceKHouseholdConsequenceTests
    {
        private sealed class SliceKStore : IItemResolvingSupplier
        {
            public string SupplierBusinessId { get; set; } = "store-1";
            public string SupplierName { get; set; } = "General Store";
            public string LocationId { get; set; } = "store";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int CashReceived;
            public string LastReceiptIdValue = string.Empty;
            public int ReceiptSequence;

            public bool HasCategory(string categoryId) => true;
            public int StockUnits(string categoryId) => 0;
            public int PricePerUnitCents(string categoryId) => 0;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics) => 0;

            public bool OffersItem(string itemId) => Stock.ContainsKey(itemId);
            public string CategoryForItem(string itemId) => "staple_food";
            public int ItemStockUnits(string itemId) => Stock.TryGetValue(itemId, out int s) ? s : 0;
            public int ItemPricePerUnitCents(string itemId) => Prices.TryGetValue(itemId, out int p) ? p : 0;
            public string CheckTradingCapability(int absoluteDayIndex, int minuteOfDay) => null;
            public string LastReceiptId => LastReceiptIdValue;

            public int SellItem(string itemId, int requestedUnits, int dayIndex, ShoppingSettlement settlement, List<string> diagnostics)
            {
                int have = ItemStockUnits(itemId);
                int sold = System.Math.Min(System.Math.Max(0, requestedUnits), have);
                if (sold > 0)
                {
                    Stock[itemId] = have - sold;
                    LastReceiptIdValue = $"rcpt-{++ReceiptSequence}";
                    if (settlement != null && settlement.Mode == ShoppingSettlementMode.CashFromLedger)
                    {
                        CashReceived += settlement.AmountCents;
                    }
                }

                return sold;
            }
        }

        private sealed class Fixture
        {
            public const int HouseholdId = 7;
            public const int ShopperId = 1;
            public const int Day = 100;
            public const int FlourPriceCents = 120;

            public readonly PopulationState Population = new PopulationState();
            public readonly HouseholdMembershipRegistry Membership = new HouseholdMembershipRegistry();
            public readonly HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public readonly HouseholdInventoryRegistry Inventories = new HouseholdInventoryRegistry();
            public readonly HouseholdNeedRegistry Needs = new HouseholdNeedRegistry();
            public readonly SupplierDirectory Directory = new SupplierDirectory();
            public readonly JourneyModel Journeys = new JourneyModel();
            public readonly TaskAuthority Tasks = new TaskAuthority();
            public readonly WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public readonly PersonScheduleTracker Tracker = new PersonScheduleTracker();
            public readonly ShopperSellerKnowledge Knowledge = new ShopperSellerKnowledge();
            public readonly SimpleDueEventScheduler DueEvents = new SimpleDueEventScheduler();
            public readonly SliceKStore Store = new SliceKStore();
            public readonly HouseholdShoppingLoop Loop;
            public readonly ShoppingTripDriver TripDriver;

            public readonly EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public readonly ContactGraphService Contacts = new ContactGraphService();
            public readonly FinancialObligationAuthority Finance = new FinancialObligationAuthority();
            public readonly EntityIdRegistry Ids = new EntityIdRegistry();
            public readonly CreditOfferWorkflow Workflow = new CreditOfferWorkflow();
            public readonly CreditCashBridge Cash = new CreditCashBridge();
            public readonly CreditEventLog Events = new CreditEventLog();
            public readonly HouseholdLenderDirectory Lenders = new HouseholdLenderDirectory();
            public readonly HouseholdDecisionService Decisions = new HouseholdDecisionService();
            public readonly HouseholdDecisionDriver DecisionDriver;
            public readonly HouseholdAutonomyDriver Autonomy = new HouseholdAutonomyDriver();
            public readonly HousingSearchNeedRegistry HousingNeeds = new HousingSearchNeedRegistry();

            public readonly Dictionary<int, string> Locations = new Dictionary<int, string>();
            public readonly List<string> Diag = new List<string>();
            public readonly ShoppingLoopOptions LoopOptions;
            public readonly List<RentObligationState> Rent = new List<RentObligationState>();

            public Fixture()
            {
                Population.people.Add(new PersonState { id = ShopperId, firstName = "Tomas", lastName = "Morrow", age = 34, ageBand = AgeBand.Adult18Plus });
                Population.households.Add(new HouseholdState { id = HouseholdId, householdName = "Morrow" });
                Membership.Register(new HouseholdMembership
                {
                    MembershipId = 1,
                    PersonId = ShopperId,
                    HouseholdId = HouseholdId,
                    Lifecycle = HouseholdMembershipLifecycle.Active,
                    StartDayIndex = 0,
                });

                Journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Morrow Farm", 0f, 0f));
                Journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "General Store", 2f, 0f));
                Journeys.AddEdge("home", "store", 2.0f, "river road");

                Store.Stock[HouseholdItemCatalog.FlourId] = 100;
                Store.Prices[HouseholdItemCatalog.FlourId] = FlourPriceCents;
                Directory.Register(Store);
                Knowledge.LearnSupplier(ShopperId, "store-1", "lives in the same town", 0);

                Locations[ShopperId] = "home";

                HouseholdShoppingLoop.RegisterTaskDefinitions(Tasks);
                LoopOptions = new ShoppingLoopOptions
                {
                    PersonLocationId = pid => Locations.TryGetValue(pid, out string l) ? l : "home",
                    SetPersonLocation = (pid, l) => Locations[pid] = l,
                    DepartureMinuteOfDay = 600,
                    InStoreMinutes = 15,
                    AllowDelivery = false,
                    Diagnostics = Diag,
                };

                DecisionDriver = new HouseholdDecisionDriver(Contacts, Employments, HousingNeeds, Finance, Ids, Cash);
                Loop = new HouseholdShoppingLoop(
                    Population, Membership, Ledgers, Inventories, Needs, Directory, Journeys,
                    Tasks, Budgets, Tracker, Knowledge,
                    obligations: null, entityIds: null,
                    personEntityId: pid => EntityId.For(EntityKind.Person, pid),
                    spendingPolicyProvider: hid => DecisionDriver.GetSpendingPolicy(hid));
                TripDriver = new ShoppingTripDriver(Loop);

                Lenders.BindHubServices(Workflow, Finance, Cash, Ids, Events);
                Rent.Add(new RentObligationState
                {
                    AgreementId = "agree-7",
                    AmountDueCents = 6000,
                    PaidCents = 0,
                    Status = RentObligationStatus.Current,
                });
            }

            public void SeedHousehold(int openingCents)
            {
                HouseholdLedger ledger = Ledgers.GetOrCreate(HouseholdId);
                Assert.IsNull(ledger.RecordInflow(0, openingCents, HouseholdIncomeSource.WageEmployment,
                    "emp-seed", "seed wages", "test"));
                Cash.Register("household:" + HouseholdId, new HouseholdCashStore(HouseholdId, ledger));
            }

            public void Employ(int personId, int weeklyWageCents)
            {
                Assert.IsNull(Employments.Register(new EmploymentRelationship
                {
                    Id = "emp-" + personId,
                    EmployeePersonId = personId,
                    EmployerBusinessId = "mill-1",
                    RoleDisplayName = "hand",
                    LifecycleState = EmploymentLifecycleState.Active,
                    Compensation = CompensationTerms.FromWeeklyWage(weeklyWageCents, ""),
                }));
            }

            public void EndEmployment(int personId)
            {
                EmploymentRelationship rel;
                if (Employments.TryGetById("emp-" + personId, out rel) && rel != null)
                {
                    rel.LifecycleState = EmploymentLifecycleState.Ended;
                }
            }

            public HouseholdAutonomyDriver.AutonomyContext AutonomyContext()
            {
                return new HouseholdAutonomyDriver.AutonomyContext
                {
                    Ledgers = Ledgers,
                    Memberships = Membership,
                    Employments = Employments,
                    ContactGraph = Contacts,
                    Decisions = Decisions,
                    DecisionDriver = DecisionDriver,
                    HousingSearchNeeds = HousingNeeds,
                    FinancialObligations = Finance,
                    Ids = Ids,
                    Lenders = Lenders,
                    PersonNameOf = pid => "Person " + pid,
                    AssetSale = new HouseholdToHouseholdAssetSalePort(null),
                    SaleableSurplusValueFor = hid => 0,
                    RentObligationsFor = hid => Rent,
                };
            }

            public HouseholdPurchasingNeed OpenDiscretionaryNeed(int units, int dayIndex)
            {
                HouseholdPurchasingNeed need = Needs.CreateNeed(HouseholdId, HouseholdItemCatalog.FlourId, dayIndex);
                need.UnitsNeeded = units;
                need.IsNecessity = false;
                return need;
            }

            /// <summary>
            /// Runs one full real-time shopping trip for a discretionary need
            /// and returns the units the store actually sold (its real demand).
            /// </summary>
            public int BuyDiscretionary(int units, int dayIndex)
            {
                int stockBefore = Store.Stock[HouseholdItemCatalog.FlourId];
                HouseholdPurchasingNeed need = OpenDiscretionaryNeed(units, dayIndex);
                TripDriver.PlanForHousehold(HouseholdId, Needs, dayIndex, LoopOptions, Diag);
                Assert.Greater(TripDriver.ActiveTrips.Count, 0,
                    "the trip must plan\n" + string.Join("\n", Diag.ToArray()));
                RunUntilDone(300, dayIndex);
                Assert.AreEqual(0, TripDriver.ActiveTrips.Count,
                    "the trip must complete\n" + string.Join("\n", Diag.ToArray()));
                Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status,
                    "the capped need must be fulfilled by the capped purchase");
                return stockBefore - Store.Stock[HouseholdItemCatalog.FlourId];
            }

            public void RunUntilDone(int tickSeconds, int dayIndex, int maxTicks = 2000)
            {
                float start = dayIndex * 86400f;
                for (int t = 0; t < maxTicks && TripDriver.ActiveTrips.Count > 0; t++)
                {
                    float now = start + t * tickSeconds;
                    TripDriver.AdvanceTick(now, dayIndex, DueEvents, Diag);
                    DrainDue(now, dayIndex);
                }

                for (int i = 0; i < 50 && (DueEvents.ScheduledCount > 0 || TripDriver.ActiveTrips.Count > 0); i++)
                {
                    DrainDue(start + 3 * 86400f, dayIndex);
                }
            }

            private void DrainDue(float now, int dayIndex)
            {
                foreach (DueEvent ev in DueEvents.PopDue(now))
                {
                    TripDriver.HandleDueEvent(ev, dayIndex, ev.DueGameSeconds, DueEvents, Diag);
                }
            }

            public void AssertNoPhantomIncome(int expectedInflows, int expectedInflowTotal)
            {
                var entries = new List<HouseholdLedgerEntry>(Ledgers.GetOrCreate(HouseholdId).Entries);
                int inflowCount = 0;
                int inflowTotal = 0;
                foreach (HouseholdLedgerEntry e in entries)
                {
                    if (!e.IsInflow) continue;
                    inflowCount++;
                    inflowTotal += e.AmountCents;
                    Assert.AreEqual(HouseholdIncomeSource.WageEmployment, e.Source,
                        "every inflow must be real documented wages, got: " + e.Reason);
                }

                Assert.AreEqual(expectedInflows, inflowCount,
                    "no balancing system may manufacture replacement income");
                Assert.AreEqual(expectedInflowTotal, inflowTotal);
            }
        }

        // ---------- Slice K: income loss -> reduced spending -> real demand drop ----------

        [Test]
        public void K_IncomeLoss_ReducedSpending_CutsRealBusinessDemand()
        {
            var f = new Fixture();
            f.SeedHousehold(8000);
            f.Employ(Fixture.ShopperId, 2100); // 300c/day expected income

            // Day 100: employed, rent 6000c covered with cushion — no action.
            f.Autonomy.EvaluateHouseholdDay(Fixture.HouseholdId, Fixture.Day, f.AutonomyContext(), f.Diag);
            HouseholdDecision day100 = f.Decisions.LastDecisionFor(Fixture.HouseholdId.ToString());
            Assert.AreEqual(HouseholdDecisionKind.NoAction, day100.Kind,
                "employed household must take no action, got: " + day100.Reason);

            // Baseline real demand: 10u of discretionary flour at 120c.
            int soldBaseline = f.BuyDiscretionary(10, Fixture.Day);
            Assert.AreEqual(10, soldBaseline, "baseline trip must fill the full discretionary need");
            Assert.AreEqual(1200, f.Store.CashReceived, "the store's real revenue from baseline demand");
            Assert.AreEqual(6800, f.Ledgers.GetOrCreate(Fixture.HouseholdId).GetBalanceCents());

            // Day 101: the job ends. No contacts, no lenders, no surplus —
            // the household's own decision ladder must choose economize.
            f.EndEmployment(Fixture.ShopperId);
            f.Autonomy.EvaluateHouseholdDay(Fixture.HouseholdId, Fixture.Day + 1, f.AutonomyContext(), f.Diag);
            HouseholdDecision day101 = f.Decisions.LastDecisionFor(Fixture.HouseholdId.ToString());
            Assert.AreEqual(HouseholdDecisionKind.Economize, day101.Kind,
                "job loss with no other legitimate channel must economize, got: " + day101.Reason);

            HouseholdSpendingPolicy policy = f.DecisionDriver.GetSpendingPolicy(Fixture.HouseholdId);
            Assert.IsNotNull(policy, "economize must leave a real standing spending policy");
            Assert.AreEqual(50, policy.DiscretionaryCapPercent);

            // Post-loss demand: the identical 10u need is halved by the policy.
            int soldAfterLoss = f.BuyDiscretionary(10, Fixture.Day + 1);
            Assert.AreEqual(5, soldAfterLoss,
                "the store experiences the household's reduced spending as real demand drop: 5u, not 10u");

            // Conservation across the buyer/seller boundary.
            Assert.AreEqual(1800, f.Store.CashReceived,
                "store revenue = baseline 1200c + post-loss 600c, nothing else");
            Assert.AreEqual(6200, f.Ledgers.GetOrCreate(Fixture.HouseholdId).GetBalanceCents());
            Assert.AreEqual(1800, 8000 - f.Ledgers.GetOrCreate(Fixture.HouseholdId).GetBalanceCents(),
                "every cent the household spent arrived at the store");

            // No balancing system manufactured replacement income.
            f.AssertNoPhantomIncome(1, 8000);
        }

        // ---------- Slice K: genuine cost increase -> reduced spending -> real demand drop ----------

        [Test]
        public void K_GenuineCostIncrease_RentRise_CutsRealBusinessDemand()
        {
            var f = new Fixture();
            f.SeedHousehold(5700);
            f.Employ(Fixture.ShopperId, 2100); // 300c/day expected income, STILL employed

            // Day 100: rent 6000c covered with cushion — no action.
            f.Autonomy.EvaluateHouseholdDay(Fixture.HouseholdId, Fixture.Day, f.AutonomyContext(), f.Diag);
            HouseholdDecision day100 = f.Decisions.LastDecisionFor(Fixture.HouseholdId.ToString());
            Assert.AreEqual(HouseholdDecisionKind.NoAction, day100.Kind,
                "household must take no action before the cost increase, got: " + day100.Reason);

            // Baseline real demand: 10u of discretionary flour at 120c.
            int soldBaseline = f.BuyDiscretionary(10, Fixture.Day);
            Assert.AreEqual(10, soldBaseline, "baseline trip must fill the full discretionary need");
            Assert.AreEqual(4500, f.Ledgers.GetOrCreate(Fixture.HouseholdId).GetBalanceCents());

            // Day 101: the landlord genuinely raises the rent 6000c -> 12000c
            // (a real obligation-state change; the worker is still employed,
            // so this is a cost increase, not an income loss).
            f.Rent[0].AmountDueCents = 12000;
            f.Autonomy.EvaluateHouseholdDay(Fixture.HouseholdId, Fixture.Day + 1, f.AutonomyContext(), f.Diag);
            HouseholdDecision day101 = f.Decisions.LastDecisionFor(Fixture.HouseholdId.ToString());
            Assert.AreEqual(HouseholdDecisionKind.Economize, day101.Kind,
                "a genuine cost increase with no other legitimate channel must economize, got: " + day101.Reason);
            Assert.AreEqual(1, f.Employments.GetActiveByEmployee(Fixture.ShopperId).Count,
                "employment is untouched by the cost-increase path");

            HouseholdSpendingPolicy policy = f.DecisionDriver.GetSpendingPolicy(Fixture.HouseholdId);
            Assert.IsNotNull(policy, "economize must leave a real standing spending policy");
            Assert.AreEqual(50, policy.DiscretionaryCapPercent);

            // Post-rise demand: the identical 10u need is halved by the policy.
            int soldAfterRise = f.BuyDiscretionary(10, Fixture.Day + 1);
            Assert.AreEqual(5, soldAfterRise,
                "the store experiences the household's reduced spending as real demand drop: 5u, not 10u");

            // Conservation across the buyer/seller boundary.
            Assert.AreEqual(1800, f.Store.CashReceived,
                "store revenue = baseline 1200c + post-rise 600c, nothing else");
            Assert.AreEqual(3900, f.Ledgers.GetOrCreate(Fixture.HouseholdId).GetBalanceCents());
            Assert.AreEqual(1800, 5700 - f.Ledgers.GetOrCreate(Fixture.HouseholdId).GetBalanceCents(),
                "every cent the household spent arrived at the store");

            // No balancing system manufactured replacement income.
            f.AssertNoPhantomIncome(1, 5700);
        }
    }
}
