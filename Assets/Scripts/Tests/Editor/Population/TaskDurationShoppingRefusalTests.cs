using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// Priority 4 Slice 4 — the shopping-loop refusal feed (plan §5 Slice 4
    /// item 3). The loop's leg assign/start refusals are fed into the
    /// task-duration diagnostic via the loop's TimingObserver seam, so a
    /// repeatedly refused leg classifies L6 instead of vanishing (G7).
    ///
    /// The fixture mirrors HouseholdShoppingLoopTests.Fixture (same person,
    /// household, store port, journeys, and loop construction) because that
    /// file's fixture is private and its test file is harness-excluded
    /// (sources.txt); this file is the harness-visible home for the feed.
    /// </summary>
    [TestFixture]
    public sealed class TaskDurationShoppingRefusalTests
    {
        private static EntityId Person(int id)
        {
            return EntityId.For(EntityKind.Person, id);
        }

        private static EntityId Business(int id)
        {
            return EntityId.For(EntityKind.Business, id);
        }

        // Slice 4: a shopping leg whose StartTask is refused (the shopper
        // already holds an InProgress task) feeds that refusal into the
        // diagnostic: the leg task's record carries the refusal verbatim,
        // the trip fails without moving money or stock, and the need stays
        // open. Pre-fix this test does not compile: HouseholdShoppingLoop
        // has no TimingObserver seam (CS1061).
        [Test]
        public void ShoppingLeg_StartRefusedWhenWorkerBusy_FeedsDiagnosticsRefusal()
        {
            Fixture f = Fixture.Create();
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            // The hub wiring (Slice 4): the diagnostic observes the loop's
            // authority, and the loop feeds its leg refusals into it.
            var diagnostics = new TaskDurationDiagnostics();
            f.Tasks.SetTimingObserver(diagnostics);
            f.Loop.TimingObserver = diagnostics;

            // Occupy the shopper with a 1-minute task. The window search
            // checks budget + reservations only, so the trip is still
            // planned and its legs still assigned — but the outbound leg
            // cannot start while the shopper holds an InProgress task
            // (TaskAuthority one-InProgress-task rule).
            string reason;
            Assert.IsTrue(f.Tasks.RegisterDefinition(new TaskDefinition("test.sweep", "Sweep", 1), out reason), reason);
            f.Budgets.EnsureDay(f.Day);
            WorkTask busy = f.Tasks.CreateTask("test.sweep", Business(7), f.Day);
            Assert.IsTrue(f.Tasks.AssignTask(busy.TaskId, Person(1), f.Budgets, f.Day, out reason), reason);
            Assert.IsTrue(f.Tasks.StartTask(busy.TaskId, f.Day, out reason), reason);

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsTrue(!result.Success, "the trip must fail when its outbound leg cannot start");
            StringAssert.Contains("could not start", result.FailureReason);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "the need stays open");
            Assert.AreEqual(0, result.UnitsAcquired, "nothing was bought");
            Assert.AreEqual(10000, f.Balance(), "no money moved");
            Assert.AreEqual(100, f.StoreStock(), "no stock moved");

            TaskTimingRecord refused = null;
            foreach (TaskTimingRecord record in diagnostics.GetAllRecords())
            {
                if (!string.IsNullOrEmpty(record.LastRefusal))
                {
                    refused = record;
                    break;
                }
            }

            Assert.IsNotNull(refused, "no leg task carries the fed refusal — the feed is not wired");
            Assert.AreEqual("StartTask", refused.LastRefusalMethod);
            StringAssert.Contains("in-progress", refused.LastRefusal);
            Assert.AreEqual(1, refused.ConsecutiveRefusalDays);
            Assert.AreEqual(f.Day, refused.LastRefusalDay);
        }

        // ---- Minimal fixture: mirrors HouseholdShoppingLoopTests.Fixture ----

        private sealed class FakeStorePort : IGeneralStoreTradingPort
        {
            public string StoreBusinessId { get; set; } = "store-1";
            public string StoreName { get; set; } = "General Store";
            public string LocationId { get; set; } = "store";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public Dictionary<string, int> Targets = new Dictionary<string, int>();
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();
            public StoreTradingCapability Capability { get; set; } = new StoreTradingCapability();
            public int CashBalance = 50000;

            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => Capability;
            public int CurrentCashCents => CashBalance;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;

            public int CategoryStockUnits(string categoryId) => Stock.TryGetValue(categoryId, out int s) ? s : 0;
            public int CategoryTargetStockUnits(string categoryId) => Targets.TryGetValue(categoryId, out int t) ? t : 0;
            public int CategoryPricePerUnitCents(string categoryId) => Prices.TryGetValue(categoryId, out int p) ? p : 0;

            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                int have = CategoryStockUnits(categoryId);
                int take = System.Math.Min(System.Math.Max(0, units), have);
                if (take <= 0) return false;
                Stock[categoryId] = have - take;
                consumedUnits = take;
                return true;
            }

            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += System.Math.Max(0, revenueCents);
            }

            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId)
            {
            }

            public string SpendCash(int amountCents, string purpose)
            {
                int spend = System.Math.Max(0, amountCents);
                if (spend <= 0) return null;
                if (CashBalance < spend) return $"cannot afford {spend}c: cash is {CashBalance}c";
                CashBalance -= spend;
                return null;
            }

            public string ReceiveStock(string categoryId, int units, string provenanceLabel)
            {
                if (units <= 0) return "nothing to receive";
                Stock[categoryId] = CategoryStockUnits(categoryId) + units;
                return null;
            }

            public int CategoryReceivableUnits(string categoryId)
            {
                int target = CategoryTargetStockUnits(categoryId);
                return target > 0 ? System.Math.Max(0, target - CategoryStockUnits(categoryId)) : int.MaxValue;
            }

            public int DeliveryFeeCents(string itemId, int units) => 0;
        }

        private sealed class Fixture
        {
            public PopulationState Population;
            public HouseholdMembershipRegistry Membership;
            public HouseholdLedgerRegistry Ledgers;
            public HouseholdInventoryRegistry Inventories;
            public HouseholdNeedRegistry Needs;
            public SupplierDirectory Directory;
            public JourneyModel Journeys;
            public TaskAuthority Tasks;
            public WorkTimeBudgetStore Budgets;
            public PersonScheduleTracker Tracker;
            public ShopperSellerKnowledge Knowledge;
            public FinancialObligationAuthority Obligations;
            public EntityIdRegistry EntityIds;
            public HouseholdShoppingLoop Loop;
            public FakeStorePort Port;
            public Dictionary<int, string> PersonLocations = new Dictionary<int, string>();

            public int HouseholdId = 7;
            public int Day = 0; // Monday (DayOfWeekIndex 0)

            public static Fixture Create()
            {
                var f = new Fixture();
                f.Population = new PopulationState();
                f.Population.people.Add(new PersonState
                {
                    id = 1, firstName = "Tomas", lastName = "Morrow", age = 34, ageBand = AgeBand.Adult18Plus,
                });
                f.Population.households.Add(new HouseholdState { id = 7 });
                f.Membership = new HouseholdMembershipRegistry();
                string regProblem = f.Membership.Register(new HouseholdMembership
                {
                    MembershipId = 1, PersonId = 1, HouseholdId = 7, StartDayIndex = 0,
                });
                Assert.IsNull(regProblem, regProblem);
                var child = new PersonState
                {
                    id = 2, firstName = "Petra", lastName = "Morrow", age = 8, ageBand = AgeBand.Child0To9,
                };
                f.Population.people.Add(child);
                regProblem = f.Membership.Register(new HouseholdMembership
                {
                    MembershipId = 2, PersonId = 2, HouseholdId = 7, StartDayIndex = 0,
                });
                Assert.IsNull(regProblem, regProblem);

                f.Ledgers = new HouseholdLedgerRegistry();
                f.Inventories = new HouseholdInventoryRegistry();
                f.Needs = new HouseholdNeedRegistry();
                f.Journeys = new JourneyModel();
                f.Journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Morrow Farm", 0f, 0f));
                f.Journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "General Store", 2f, 0f));
                f.Journeys.AddEdge("home", "store", 2.0f, "river road");

                f.Tasks = new TaskAuthority();
                HouseholdShoppingLoop.RegisterTaskDefinitions(f.Tasks);
                f.Budgets = new WorkTimeBudgetStore();
                f.Tracker = new PersonScheduleTracker();
                f.Knowledge = new ShopperSellerKnowledge();
                f.Obligations = new FinancialObligationAuthority();
                f.EntityIds = new EntityIdRegistry();

                f.Port = new FakeStorePort();
                f.Port.OfferList.Add(new GeneralStoreCategoryOffer
                {
                    CategoryId = "staple_food",
                    OfferedItemIds = new List<string> { HouseholdItemCatalog.FlourId },
                    StoreUnitsPerItemUnit = 1,
                });
                f.Port.Stock["staple_food"] = 100;
                f.Port.Prices["staple_food"] = 120;
                f.Port.Targets["staple_food"] = 100;

                f.Directory = new SupplierDirectory();
                GeneralStoreSupplierAdapter.RegisterIn(f.Directory, f.Port);
                f.Knowledge.LearnSupplier(1, "store-1", "lives in the same town", 0);

                f.Loop = new HouseholdShoppingLoop(
                    f.Population, f.Membership, f.Ledgers, f.Inventories, f.Needs,
                    f.Directory, f.Journeys, f.Tasks, f.Budgets, f.Tracker, f.Knowledge,
                    f.Obligations, f.EntityIds);

                f.PersonLocations[1] = "home";
                f.PersonLocations[2] = "home";
                return f;
            }

            public ShoppingLoopOptions Options()
            {
                var fixture = this;
                return new ShoppingLoopOptions
                {
                    PersonLocationId = pid => fixture.PersonLocations.TryGetValue(pid, out string loc) ? loc : "home",
                    SetPersonLocation = (pid, loc) => fixture.PersonLocations[pid] = loc,
                    DepartureMinuteOfDay = 600,
                    InStoreMinutes = 15,
                    Diagnostics = new List<string>(),
                };
            }

            public void Fund(int cents)
            {
                HouseholdLedger ledger = Ledgers.GetOrCreate(HouseholdId);
                string problem = ledger.RecordInflow(0, cents, HouseholdIncomeSource.OwnerContribution,
                    "test-capital", "test funding", "test");
                Assert.IsNull(problem, problem);
            }

            public HouseholdPurchasingNeed OpenNeed(string itemId, int units)
            {
                HouseholdPurchasingNeed need = Needs.CreateNeed(HouseholdId, itemId, Day);
                need.UnitsNeeded = units;
                need.Urgency01 = 0.9f;
                need.ReasonSummary = "test need";
                return need;
            }

            public int Balance() => Ledgers.GetOrCreate(HouseholdId).GetBalanceCents();

            public int StoreCash() => Port.CashBalance;

            public int StoreStock() => Port.CategoryStockUnits("staple_food");
        }
    }
}
