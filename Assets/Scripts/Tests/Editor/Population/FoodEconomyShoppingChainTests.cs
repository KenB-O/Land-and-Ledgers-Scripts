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
    /// Overnight 2026-11-04 slice 2: the household food-economy chain through
    /// the production batch shopping loop, against a fake item-level store
    /// port (no scene managers, so this runs in the Roslyn harness).
    ///
    /// Locks in the brief's required validation: one legitimate purchase
    /// with real identified stock, correct buyer debit, correct seller
    /// credit, correct inventory deduction, correct household receipt, a
    /// valid refusal when stock is unavailable, a valid refusal when the
    /// seller is closed, no double purchase, and no duplicate receipt.
    /// It also pins the two honest failure shapes the November 4 playtest
    /// reported as "unavailable sellers": no known seller, and a known
    /// seller with no servable stock.
    /// </summary>
    [TestFixture]
    public sealed class FoodEconomyShoppingChainTests
    {
        private sealed class FakeStorePort : IItemLevelGeneralStoreTradingPort
        {
            public string StoreBusinessId { get; set; } = "store-1";
            public string StoreName { get; set; } = "General Store";
            public string LocationId { get; set; } = "store";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public Dictionary<string, int> Targets = new Dictionary<string, int>();
            public Dictionary<string, int> ItemStock = new Dictionary<string, int>();
            public Dictionary<string, int> ItemPrices = new Dictionary<string, int>();
            public HashSet<string> ItemLevelItems = new HashSet<string>();
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();
            public StoreTradingCapability Capability { get; set; } = new StoreTradingCapability();
            public int CashBalance = 50000;
            public int RecordedCashRevenue;
            public int RecordedItemCashRevenue;
            public bool OffersDeliveryValue;
            public int DeliveryFeeValue;
            public bool OffersTradeCreditValue;
            public int TradeCreditLimitValue;

            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => Capability;
            public int CurrentCashCents => CashBalance;
            public bool OffersDelivery => OffersDeliveryValue;
            public bool OffersTradeCredit => OffersTradeCreditValue;
            public int TradeCreditLimitCents => TradeCreditLimitValue;

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
                RecordedCashRevenue += System.Math.Max(0, revenueCents);
            }

            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }

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

            public int DeliveryFeeCents(string itemId, int units) => System.Math.Max(0, DeliveryFeeValue);

            public bool UsesItemLevelInventory(string itemId) => ItemLevelItems.Contains(itemId);

            public int ItemStockUnits(string itemId) => ItemStock.TryGetValue(itemId, out int s) ? s : 0;

            public int ItemPricePerUnitCents(string itemId) => ItemPrices.TryGetValue(itemId, out int p) ? p : 0;

            public bool TryConsumeItemStockUnits(string itemId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                int have = ItemStockUnits(itemId);
                int take = System.Math.Min(System.Math.Max(0, units), have);
                if (take <= 0) return false;
                ItemStock[itemId] = have - take;
                consumedUnits = take;
                return true;
            }

            public void RecordItemCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += System.Math.Max(0, revenueCents);
                RecordedItemCashRevenue += System.Math.Max(0, revenueCents);
            }
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
            public List<string> Diagnostics = new List<string>();

            public int HouseholdId = 7;
            public int Day = 0; // Monday (day-of-week index 0)

            public static Fixture Create(bool withKnowledge = true)
            {
                var f = new Fixture();
                f.Population = new PopulationState();
                f.Population.people.Add(new PersonState
                {
                    id = 1, firstName = "Tomas", lastName = "Morrow", age = 34, ageBand = AgeBand.Adult18Plus
                });
                f.Population.households.Add(new HouseholdState { id = 7 });
                f.Membership = new HouseholdMembershipRegistry();
                string regProblem = f.Membership.Register(new HouseholdMembership
                {
                    MembershipId = 1, PersonId = 1, HouseholdId = 7, StartDayIndex = 0
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
                if (withKnowledge)
                {
                    f.Knowledge.LearnSupplier(1, "store-1", "lives in the same town", 0);
                }

                f.Loop = new HouseholdShoppingLoop(
                    f.Population, f.Membership, f.Ledgers, f.Inventories, f.Needs,
                    f.Directory, f.Journeys, f.Tasks, f.Budgets, f.Tracker, f.Knowledge,
                    f.Obligations, f.EntityIds);

                f.PersonLocations[1] = "home";
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
                    Diagnostics = fixture.Diagnostics,
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
            public int StoreStock() => Port.CategoryStockUnits("staple_food");
            public int StoreCash() => Port.CashBalance;
        }

        [Test]
        public void BatchPurchase_PooledOffer_ConservesCashStockAndReceipt()
        {
            Fixture f = Fixture.Create();
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            int storeCashBefore = f.StoreCash();
            int storeStockBefore = f.StoreStock();

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "food-chain-happy");

            Assert.IsTrue(batch.TripExecuted, batch.FailureReason);
            Assert.AreEqual(1, batch.ServedNeedCount);
            Assert.AreEqual(10, batch.UnitsAcquiredTotal);
            Assert.AreEqual(10000 - 1200, f.Balance(), "Buyer debit must equal units x price.");
            Assert.AreEqual(storeCashBefore + 1200, f.StoreCash(), "Seller cash must gain exactly the buyer debit.");
            Assert.AreEqual(storeStockBefore - 10, f.StoreStock(), "Store stock must fall by exactly the units sold.");
            Assert.AreEqual(10, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId),
                "Household inventory grows only upon physical receipt.");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            Assert.AreEqual(1200, f.Port.RecordedCashRevenue);
        }

        [Test]
        public void BatchPurchase_UnknownSeller_FailsWithoutMovingMoneyOrStock()
        {
            Fixture f = Fixture.Create(withKnowledge: false);
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "food-chain-unknown-seller");

            Assert.IsFalse(batch.TripExecuted);
            Assert.IsFalse(string.IsNullOrWhiteSpace(batch.FailureReason));
            Assert.AreEqual(10000, f.Balance(), "A refused purchase must not move buyer money.");
            Assert.AreEqual(100, f.StoreStock(), "A refused purchase must not move seller stock.");
            Assert.AreEqual(0, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "The unmet need stays open for a later day.");
        }

        [Test]
        public void BatchPurchase_OutOfStock_FailsWithoutMovingMoney()
        {
            Fixture f = Fixture.Create();
            f.Fund(10000);
            f.Port.Stock["staple_food"] = 0;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "food-chain-out-of-stock");

            Assert.IsFalse(batch.TripExecuted);
            Assert.AreEqual(10000, f.Balance());
            Assert.AreEqual(0, f.StoreStock());
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
        }

        [Test]
        public void BatchPurchase_ClosedSunday_FailsWithoutMovingMoney()
        {
            Fixture f = Fixture.Create();
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            // Day 6 is Sunday under StoreTradingHours (open days 0-5).
            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, 6, f.Options(), "food-chain-sunday");

            Assert.IsFalse(batch.TripExecuted);
            Assert.AreEqual(10000, f.Balance());
            Assert.AreEqual(100, f.StoreStock());
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
        }

        [Test]
        public void BatchPurchase_UnstaffedStore_FailsWithoutMovingMoney()
        {
            Fixture f = Fixture.Create();
            f.Fund(10000);
            f.Port.Capability.Staffed = false;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "food-chain-unstaffed");

            Assert.IsFalse(batch.TripExecuted);
            Assert.AreEqual(10000, f.Balance());
            Assert.AreEqual(100, f.StoreStock());
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
        }

        [Test]
        public void BatchPurchase_ItemLevelOffer_ConsumesIdentifiedLots()
        {
            Fixture f = Fixture.Create();
            f.Fund(10000);
            // Production NPC ports aggregate one category into a single
            // item-level offer; mirror that shape, not two same-category offers.
            f.Port.OfferList.Clear();
            f.Port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { HouseholdItemCatalog.BreadId },
                StoreUnitsPerItemUnit = 1,
                UsesItemLevelInventory = true,
            });
            f.Port.ItemLevelItems.Add(HouseholdItemCatalog.BreadId);
            f.Port.ItemStock[HouseholdItemCatalog.BreadId] = 16;
            f.Port.ItemPrices[HouseholdItemCatalog.BreadId] = 50;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.BreadId, 10);

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "food-chain-item-level");

            Assert.IsTrue(batch.TripExecuted, batch.FailureReason);
            Assert.AreEqual(10, batch.UnitsAcquiredTotal);
            Assert.AreEqual(6, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "Identified lot stock must fall by exactly the units sold.");
            Assert.AreEqual(500, f.Port.RecordedItemCashRevenue);
            Assert.AreEqual(10000 - 500, f.Balance());
            Assert.AreEqual(10, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.BreadId));
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
        }
    }
}
