// W5 (2026-11-06): whole-package flour purchase through the real
// household shopping loop. Self-contained fixture (the loop's shared
// fixture lives in HouseholdShoppingLoopTests.cs, which is excluded from
// the Roslyn harness by sources.txt — three of its tests fail there for
// pre-existing reasons unrelated to W5 — so these tests carry their own
// minimal port + loop setup and run in the harness).
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
    [TestFixture]
    public sealed class HouseholdWholePackagePurchaseTests
    {
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
            public List<string> Diagnostics = new List<string>();

            public int HouseholdId = 7;
            public int Day = 0; // Monday (DayOfWeekIndex 0)

            public static Fixture Create()
            {
                var f = new Fixture();
                f.Population = new PopulationState();
                f.Population.people.Add(new PersonState
                    { id = 1, firstName = "Tomas", lastName = "Morrow", age = 34, ageBand = AgeBand.Adult18Plus });
                f.Population.households.Add(new HouseholdState { id = 7 });
                f.Membership = new HouseholdMembershipRegistry();
                string regProblem = f.Membership.Register(new HouseholdMembership
                    { MembershipId = 1, PersonId = 1, HouseholdId = 7, StartDayIndex = 0 });
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
        }

        private static void OfferFlourByTheSack(Fixture f)
        {
            // The fixture's staple_food stock is sacks at 120c per sack;
            // the offer carries the W5 package size so the loop plans in
            // sacks and credits lb at receipt.
            f.Port.OfferList[0].HouseholdUnitsPerStoreUnit = 10;
        }

        [Test]
        public void Flour_WholePackagePurchase_OneSackIsTenPounds()
        {
            Fixture f = Fixture.Create();
            OfferFlourByTheSack(f);
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(10, result.UnitsAcquired);
            Assert.AreEqual(120, result.BuyerOutflowCents, "1 sack at 120c per sack.");
            Assert.AreEqual(120, result.StoreCashInCents);
            Assert.AreEqual(9880, f.Balance());
            Assert.AreEqual(99, f.StoreStock(), "1 sack left the shelf.");
            Assert.AreEqual(10, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            Assert.AreEqual(0, result.UnitsStillNeeded);
        }

        [Test]
        public void Flour_WholePackagePurchase_TwentyFivePoundsIsThreeSacks()
        {
            Fixture f = Fixture.Create();
            OfferFlourByTheSack(f);
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 25);

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(30, result.UnitsAcquired, "3 sacks × 10 lb received.");
            Assert.AreEqual(360, result.BuyerOutflowCents, "3 sacks at 120c per sack — exact cash.");
            Assert.AreEqual(360, result.StoreCashInCents);
            Assert.AreEqual(9640, f.Balance());
            Assert.AreEqual(97, f.StoreStock(), "3 sacks left the shelf.");
            Assert.AreEqual(30, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            Assert.AreEqual(0, result.UnitsStillNeeded);
        }

        [Test]
        public void Flour_WholePackagePurchase_StockLimitedSellsWholeSacksOnly()
        {
            Fixture f = Fixture.Create();
            OfferFlourByTheSack(f);
            f.Fund(10000);
            f.Port.Stock["staple_food"] = 2;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 25);

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(20, result.UnitsAcquired, "2 sacks × 10 lb — no fractional sack.");
            Assert.AreEqual(240, result.BuyerOutflowCents);
            Assert.AreEqual(0, f.StoreStock());
            Assert.AreEqual(20, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(5, result.UnitsStillNeeded);
            Assert.AreNotEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            Assert.IsTrue(result.PartialFillReason.Contains("stock-limited"),
                $"partial fill should name the stock limit, was: {result.PartialFillReason}");
        }

        [Test]
        public void Potatoes_NoPackageOffer_NeedStaysOpenWithoutAFakedSale()
        {
            // Potatoes have no D-5 sack weight, so no offer converts them:
            // the known seller advertises no purchasable potatoes offer and
            // the need stays open — never a faked sale. (The D-5 reason
            // itself is pinned at the conversion level and in the
            // port-build log, which is source-reviewed.)
            Fixture f = Fixture.Create();
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.PotatoesId, 5);

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.FailureReason.Contains("Known seller(s) exist"),
                $"failure should name the known seller with no purchasable offer, was: {result.FailureReason}");
            Assert.IsTrue(result.FailureReason.Contains("potatoes"),
                $"failure should name the item, was: {result.FailureReason}");
            Assert.AreEqual(10000, f.Balance(), "No money moved.");
            Assert.AreEqual(100, f.StoreStock(), "No stock moved.");
            Assert.AreEqual(0, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.PotatoesId));
            Assert.AreNotEqual(PurchasingNeedStatus.Fulfilled, need.Status);
        }
    }
}
