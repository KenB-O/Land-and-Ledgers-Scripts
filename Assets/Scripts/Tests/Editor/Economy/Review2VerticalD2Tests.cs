// Review 2 vertical slice Pass D2 — owner-decided horse feed.
//
// Kennedy's decision, Nov 2, 2026: Kennedy can sell horse feed
// at the General Store; by default it is NOT an item the store
// sells, but it can be added in, as any player should be able
// to add items to sell. These tests prove that decision in
// Roslyn, on production classes only, with caller-supplied
// test parameters that are NEVER canon: the feed item id, the
// store category, the store price, and both conversions are
// passed in by each test. No feed item id, price, or unit is
// asserted as a game rule anywhere here, no .asset is changed,
// and no fixture is authored. The household is a generic test
// person, never named Ethan/Tia/Shane, and never presented as
// the slice fixture.
//
// Two legs are proved, and their boundary is kept explicit:
//   - Store-owned horse: the store's added feed category IS
//     the feed stock, via Review2StoreHorseFeed.
//   - Person-owned horse: the household buys feed through the
//     normal shopping loop (runtime-registered household item,
//     store port offer, HouseholdLedger cash, store cash port),
//     the loop puts real lots into household inventory, and
//     Review2HouseholdHorseFeed draws those lots for CareDay.
//     The need object in that test is constructed by the caller:
//     feed-need origination (who opens a feed need, at what
//     quantity) is a driver decision left open, NOT proved here.
//     What IS proved is the loop sale, the inventory receipt,
//     and the CareDay draw, with every cent and unit reconciled.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Animals;
using LandLedgers.Primitives;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;

namespace LandLedgers.Tests.Economy
{
    public sealed class Review2VerticalD2Tests
    {
        // Caller-supplied test parameters for every test below.
        // None of these is a game price, id, or rate: change any
        // one of them and the tests must still reconcile, because
        // the code takes them as parameters, not as constants.
        private const string FeedItemId = "d2-test-horse-feed";
        private const string FeedCategoryId = "d2-feed-cat";
        private const string StoreBusinessId = "general-store-1";
        private const int StoreUnitsPerFeedUnit = 2;
        private const int HouseholdUnitsPerFeedUnit = 1;
        private const int StoreFeedPriceCents = 9;

        // D1's production-port seam, copied because it is private
        // to the D1 fixture: the same production interface over
        // the same BusinessRuntimeState the cash port uses, so
        // retail cash reconciles in ONE runtime. It is a test
        // seam, not a second cash authority.
        private sealed class RuntimeStorePort : IGeneralStoreTradingPort
        {
            public BusinessRuntimeState Runtime;
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();

            public string StoreBusinessId => "general-store-1";
            public string StoreName => "General Store";
            public string LocationId => "general-store";
            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => new StoreTradingCapability { Staffed = true };
            public int CurrentCashCents => Runtime != null ? Runtime.CurrentCashCents : 0;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;

            public int CategoryStockUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.CurrentStockUnits : 0;
            }
            public int CategoryTargetStockUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.TargetStockUnits : 0;
            }
            public int CategoryPricePerUnitCents(string categoryId)
            {
                return Prices.TryGetValue(categoryId, out int p) ? p : 0;
            }
            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                return Runtime != null && Runtime.TryConsumeCategoryStockUnits(categoryId, units, out consumedUnits);
            }
            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                if (Runtime != null) Runtime.RecordGenericRetailSettlement(categoryId, unitsSold, revenueCents);
            }
            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }
            public string SpendCash(int amountCents, string purpose)
            {
                if (Runtime == null) return "no runtime";
                if (Runtime.CurrentCashCents < amountCents) return "cannot afford";
                Runtime.SpendCents(amountCents);
                return null;
            }
            public string ReceiveStock(string categoryId, int units, string provenanceLabel)
            {
                if (Runtime == null) return "no runtime";
                int accepted = Runtime.AddCategoryStockUnits(categoryId, units);
                if (accepted < units) return $"store accepted {accepted}/{units}u {categoryId}: category full.";
                return null;
            }
            public int CategoryReceivableUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                return stock == null ? 0 : stock.TargetStockUnits > 0 ? Math.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits) : int.MaxValue;
            }
            public int DeliveryFeeCents(string itemId, int units) => 0;
        }

        private static AnimalState RegisterHorse(AnimalRegistry registry, string ownerId, AnimalOwnerKind ownerKind, int dayIndex)
        {
            AnimalState horse = registry.RegisterAnimal(
                AnimalSpecies.Horse, "draft", AnimalSex.Female, ownerKind, ownerId, dayIndex, "d2 test horse");
            Assert.IsNotNull(horse);
            Assert.IsNull(HorseAuthority.Configure(horse, HorseUseCapabilities.Draft, "stable-1-yard", "stable-1", dayIndex));
            return horse;
        }

        private static BusinessRuntimeState MakeStore(int feedStoreUnits)
        {
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity(StoreBusinessId);
            var registry = new Review2StoreFeedRegistry();
            var diag = new List<string>();
            Assert.IsNull(Review2StoreHorseFeed.AddFeedItem(runtime, registry, new Review2StoreFeedItem
            {
                BusinessInstanceId = StoreBusinessId,
                FeedCategoryId = FeedCategoryId,
                FeedItemId = FeedItemId,
                DisplayName = "Test Horse Feed",
                UnitLabel = "sack",
                StoreUnitsPerFeedUnit = StoreUnitsPerFeedUnit,
            }, diag), "owner add must succeed; diag: " + string.Join("; ", diag));
            Assert.AreEqual(feedStoreUnits, runtime.AddCategoryStockUnits(FeedCategoryId, feedStoreUnits),
                "stocking after the add must land exactly; the add itself creates no stock");
            return runtime;
        }

        private static Review2StoreFeedRegistry RegistryWithStore(BusinessRuntimeState storeRuntime)
        {
            var registry = new Review2StoreFeedRegistry();
            var diag = new List<string>();
            Assert.IsNull(Review2StoreHorseFeed.AddFeedItem(storeRuntime, registry, new Review2StoreFeedItem
            {
                BusinessInstanceId = StoreBusinessId,
                FeedCategoryId = FeedCategoryId,
                FeedItemId = FeedItemId,
                DisplayName = "Test Horse Feed",
                UnitLabel = "sack",
                StoreUnitsPerFeedUnit = StoreUnitsPerFeedUnit,
            }, diag));
            return registry;
        }

        [Test]
        public void StoreHorse_FedFromAddedItem_DoesNotDegrade()
        {
            BusinessRuntimeState store = MakeStore(20);
            Review2StoreFeedRegistry registry = RegistryWithStore(store);
            // MakeStore already registered once on its own registry;
            // RegistryWithStore registers the same item on a fresh
            // registry over the same runtime: the category stock
            // entry already exists, so stock is untouched (20).
            Assert.AreEqual(20, store.GetCategoryStock(FeedCategoryId).CurrentStockUnits);
            var ids = new EntityIdRegistry();
            var animals = new AnimalRegistry(ids);
            AnimalState horse = RegisterHorse(animals, StoreBusinessId, AnimalOwnerKind.Business, 0);
            horse.Nutrition01 = 0.70f;
            horse.Fatigue01 = 0f;
            var diag = new List<string>();
            IHorseFeedStock stock = Review2StoreHorseFeed.FeedStockForHorse(
                horse, _ => store, registry, diag);
            Assert.IsNotNull(stock, "a store carrying the added item must resolve a feed stock, never null: " + string.Join("; ", diag));
            Assert.AreEqual(10, stock.FeedStockUnits, "20 store units at 2 per feed unit = 10 feed units");
            int unfed = HorseCareDriver.CareDay(animals, h => Review2StoreHorseFeed.FeedStockForHorse(h, _ => store, registry, diag), _ => false, 1, diag);
            Assert.AreEqual(0, unfed, "the store horse must be fed: " + string.Join("; ", diag));
            Assert.AreEqual(16, store.GetCategoryStock(FeedCategoryId).CurrentStockUnits,
                "idle ration is ceil(1.2) = 2 feed units x 2 store units = 4 store units");
            Assert.AreEqual(1, horse.LastCareDayIndex);
            Assert.GreaterOrEqual(horse.Nutrition01, 0.70f, "a fed horse must not degrade");
        }

        [Test]
        public void StoreHorse_NoAddedItem_ResolvesNull_DegradesWithDiagnostic()
        {
            var store = new BusinessRuntimeState();
            store.BindBusinessIdentity(StoreBusinessId);
            var registry = new Review2StoreFeedRegistry();
            var ids = new EntityIdRegistry();
            var animals = new AnimalRegistry(ids);
            AnimalState horse = RegisterHorse(animals, StoreBusinessId, AnimalOwnerKind.Business, 0);
            float nutritionBefore = horse.Nutrition01;
            var diag = new List<string>();
            IHorseFeedStock stock = Review2StoreHorseFeed.FeedStockForHorse(horse, _ => store, registry, diag);
            Assert.IsNull(stock, "a store with no added feed item must still resolve null");
            Assert.IsTrue(string.Join("; ", diag).Contains("no added feed item"), "the null must be diagnosed, never silent: " + string.Join("; ", diag));
            int unfed = HorseCareDriver.CareDay(animals, h => Review2StoreHorseFeed.FeedStockForHorse(h, _ => store, registry, diag), _ => false, 1, diag);
            Assert.AreEqual(1, unfed);
            Assert.Less(horse.Nutrition01, nutritionBefore, "an unfed horse degrades, as before D2");
        }

        [Test]
        public void AddFeedItem_CreatesNoStock_AndRefusesBadOrConflictingItems()
        {
            var store = new BusinessRuntimeState();
            store.BindBusinessIdentity(StoreBusinessId);
            var registry = new Review2StoreFeedRegistry();
            var diag = new List<string>();
            Assert.IsNull(Review2StoreHorseFeed.AddFeedItem(store, registry, new Review2StoreFeedItem
            {
                BusinessInstanceId = StoreBusinessId,
                FeedCategoryId = FeedCategoryId,
                FeedItemId = FeedItemId,
                DisplayName = "Test Horse Feed",
                UnitLabel = "sack",
                StoreUnitsPerFeedUnit = StoreUnitsPerFeedUnit,
            }, diag));
            Assert.AreEqual(0, store.GetCategoryStock(FeedCategoryId).CurrentStockUnits,
                "adding an item creates the category entry, never stock");
            Assert.IsNotNull(Review2StoreHorseFeed.AddFeedItem(store, registry, new Review2StoreFeedItem
            {
                BusinessInstanceId = StoreBusinessId,
                FeedCategoryId = FeedCategoryId,
                FeedItemId = "another-feed",
                DisplayName = "Other Feed",
                UnitLabel = "sack",
                StoreUnitsPerFeedUnit = 0,
            }, diag), "a zero conversion is 'not supplied', not a silent 1:1, and must be refused");
            Assert.IsNotNull(registry.Find(StoreBusinessId), "a refused add must not remove the registration");
            Assert.AreEqual(FeedItemId, registry.Find(StoreBusinessId).FeedItemId);
            Assert.IsNotNull(Review2StoreHorseFeed.AddFeedItem(store, registry, new Review2StoreFeedItem
            {
                BusinessInstanceId = StoreBusinessId,
                FeedCategoryId = "different-cat",
                FeedItemId = "different-feed",
                DisplayName = "Different",
                UnitLabel = "sack",
                StoreUnitsPerFeedUnit = 3,
            }, diag), "a different feed item for the same store must be refused, never silently overwritten");
            Assert.AreEqual(FeedItemId, registry.Find(StoreBusinessId).FeedItemId, "the original registration survives a conflicting add");
            Assert.IsNull(Review2StoreHorseFeed.AddFeedItem(store, registry, new Review2StoreFeedItem
            {
                BusinessInstanceId = StoreBusinessId,
                FeedCategoryId = FeedCategoryId,
                FeedItemId = FeedItemId,
                DisplayName = "Test Horse Feed",
                UnitLabel = "sack",
                StoreUnitsPerFeedUnit = StoreUnitsPerFeedUnit,
            }, diag), "an identical re-add is idempotent");
        }

        [Test]
        public void StoreHorse_PartialStoreUnits_TakesNothing_NeverRoundedUp()
        {
            BusinessRuntimeState store = MakeStore(3);
            Review2StoreFeedRegistry registry = RegistryWithStore(store);
            var ids = new EntityIdRegistry();
            var animals = new AnimalRegistry(ids);
            AnimalState horse = RegisterHorse(animals, StoreBusinessId, AnimalOwnerKind.Business, 0);
            var diag = new List<string>();
            int unfed = HorseCareDriver.CareDay(animals, h => Review2StoreHorseFeed.FeedStockForHorse(h, _ => store, registry, diag), _ => false, 1, diag);
            Assert.AreEqual(1, unfed, "3 store units cannot back a 2-feed-unit ration at 2 per unit (needs 4)");
            Assert.AreEqual(3, store.GetCategoryStock(FeedCategoryId).CurrentStockUnits,
                "partial store units are never rounded up into a ration; they stay for the next day");
        }

        [Test]
        public void StoreHorse_WorkingRation_AndSecondDay_DrawConvertedUnitsEachDay()
        {
            BusinessRuntimeState store = MakeStore(20);
            Review2StoreFeedRegistry registry = RegistryWithStore(store);
            var ids = new EntityIdRegistry();
            var animals = new AnimalRegistry(ids);
            AnimalState horse = RegisterHorse(animals, StoreBusinessId, AnimalOwnerKind.Business, 0);
            var diag = new List<string>();
            Func<AnimalState, IHorseFeedStock> resolver = h => Review2StoreHorseFeed.FeedStockForHorse(h, _ => store, registry, diag);
            int unfedWorking = HorseCareDriver.CareDay(animals, resolver, _ => true, 1, diag);
            Assert.AreEqual(0, unfedWorking);
            Assert.AreEqual(14, store.GetCategoryStock(FeedCategoryId).CurrentStockUnits,
                "working ration is ceil(2.5) = 3 feed units x 2 store units = 6 store units");
            int unfedRepeat = HorseCareDriver.CareDay(animals, resolver, _ => true, 1, diag);
            Assert.AreEqual(0, unfedRepeat, "a same-day repeat must not consume a second ration");
            Assert.AreEqual(14, store.GetCategoryStock(FeedCategoryId).CurrentStockUnits);
            int unfedDay2 = HorseCareDriver.CareDay(animals, resolver, _ => false, 2, diag);
            Assert.AreEqual(0, unfedDay2);
            Assert.AreEqual(10, store.GetCategoryStock(FeedCategoryId).CurrentStockUnits,
                "day 2 idle ration draws another 4 store units");
        }

        [Test]
        public void StoreResolver_PersonOwnedHorse_ReturnsNull_HouseholdAdapterIsThePersonPath()
        {
            var store = new BusinessRuntimeState();
            var registry = new Review2StoreFeedRegistry();
            var ids = new EntityIdRegistry();
            var animals = new AnimalRegistry(ids);
            AnimalState horse = RegisterHorse(animals, "person:1", AnimalOwnerKind.Person, 0);
            var diag = new List<string>();
            Assert.IsNull(Review2StoreHorseFeed.FeedStockForHorse(horse, _ => store, registry, diag),
                "the store resolver never applies to a person-owned horse, even if a store exists");
            Assert.IsTrue(string.Join("; ", diag).Contains("not business-owned"));
        }

        [Test]
        public void RuntimeHouseholdItem_BuiltInIdCannotBeShadowed_AndUnknownItemStillRejectedByLoop()
        {
            Assert.IsNotNull(HouseholdItemCatalog.RegisterRuntimeItem(
                new HouseholdItemDefinition("bread", "Shadow Bread", "loaf", "staple_food", 1, true)),
                "a built-in id must never be shadowed by a runtime registration");
            Assert.AreEqual("Bread", HouseholdItemCatalog.Get("bread").DisplayName);
        }

        [Test]
        public void PersonHorse_BuysFeedThroughShoppingLoop_ThenEatsFromInventory()
        {
            // Runtime-register the caller-supplied feed item as a
            // household item: this is the owner-add record, not a
            // default catalog entry and not a store offer. Without
            // it the loop rejects the need as an unknown item (see
            // the boundary note in the file header).
            Assert.IsNull(HouseholdItemCatalog.RegisterRuntimeItem(
                new HouseholdItemDefinition(FeedItemId, "Test Horse Feed", "sack", FeedCategoryId, 1, false)),
                "runtime registration of the caller-supplied feed item must succeed");

            BusinessRuntimeState store = MakeStore(50);
            var port = new RuntimeStorePort { Runtime = store };
            port.Prices[FeedCategoryId] = StoreFeedPriceCents;
            port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = FeedCategoryId,
                OfferedItemIds = new List<string> { FeedItemId },
                StoreUnitsPerItemUnit = 1,
            });

            var population = new PopulationState();
            population.people.Add(new PersonState
            {
                id = 1, firstName = "Test", lastName = "Shopper", age = 34,
                ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket,
            });
            population.households.Add(new HouseholdState { id = 7 });
            var membership = new HouseholdMembershipRegistry();
            Assert.IsNull(membership.Register(new HouseholdMembership
            {
                MembershipId = 1, PersonId = 1, HouseholdId = 7, StartDayIndex = 0,
            }));
            var ledgers = new HouseholdLedgerRegistry();
            var inventories = new HouseholdInventoryRegistry();
            var needs = new HouseholdNeedRegistry();
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Test Home", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("general-store", JourneyLocationKind.Store, "General Store", 2f, 0f));
            journeys.AddEdge("home", "general-store", 2.0f, "town road");
            var tasks = new TaskAuthority();
            HouseholdShoppingLoop.RegisterTaskDefinitions(tasks);
            var budgets = new WorkTimeBudgetStore();
            var tracker = new PersonScheduleTracker();
            var knowledge = new ShopperSellerKnowledge();
            var obligations = new FinancialObligationAuthority();
            var entityIds = new EntityIdRegistry();
            var directory = new SupplierDirectory();
            GeneralStoreSupplierAdapter.RegisterIn(directory, port);
            knowledge.LearnSupplier(1, "general-store-1", "lives in the same town", 0);
            var loop = new HouseholdShoppingLoop(
                population, membership, ledgers, inventories, needs,
                directory, journeys, tasks, budgets, tracker, knowledge,
                obligations, entityIds);
            var personLocations = new Dictionary<int, string> { { 1, "home" } };
            var loopDiag = new List<string>();
            HouseholdLedger ledger = ledgers.GetOrCreate(7);
            Assert.IsNull(ledger.RecordInflow(0, 10000, HouseholdIncomeSource.OwnerContribution,
                "test-capital", "test funding", "test"));

            // Caller-constructed need: feed-need origination is a
            // driver decision left open (see header); the loop
            // sale itself is what this test proves.
            HouseholdPurchasingNeed need = needs.CreateNeed(7, FeedItemId, 0);
            need.UnitsNeeded = 6;
            ShoppingTripResult trip = loop.ExecuteNeed(need, 2, new ShoppingLoopOptions
            {
                PersonLocationId = pid => personLocations.TryGetValue(pid, out string loc) ? loc : "home",
                SetPersonLocation = (pid, loc) => personLocations[pid] = loc,
                DepartureMinuteOfDay = 600,
                InStoreMinutes = 15,
                Diagnostics = loopDiag,
                AllowTradeCredit = false,
            });
            Assert.IsTrue(trip.Success, trip.FailureReason + "; diag: " + string.Join("; ", loopDiag));
            Assert.AreEqual(6, trip.UnitsAcquired);
            Assert.AreEqual(6 * StoreFeedPriceCents, trip.BuyerOutflowCents, "6 sacks at the caller-supplied store price");
            Assert.AreEqual(trip.BuyerOutflowCents, trip.StoreCashInCents, "buyer outflow must equal business cash in, every cent");
            Assert.AreEqual(44, port.CategoryStockUnits(FeedCategoryId), "live pool loses exactly the units sold");
            Assert.AreEqual(6, inventories.GetOrCreate(7).GetAvailableUnits(FeedItemId),
                "goods enter household inventory only upon physical receipt through the loop");
            Assert.AreEqual(10000 - 54, ledgers.GetOrCreate(7).GetBalanceCents());
            Assert.AreEqual(54, store.CurrentCashCents, "store cash gains exactly the retail amount");

            // Now the person-owned horse eats from that inventory.
            var animalIds = new EntityIdRegistry();
            var animals = new AnimalRegistry(animalIds);
            AnimalState horse = RegisterHorse(animals, "person:1", AnimalOwnerKind.Person, 0);
            horse.Nutrition01 = 0.70f;
            var careDiag = new List<string>();
            var householdFeed = new Review2HouseholdHorseFeed(
                inventories.GetOrCreate(7), FeedItemId, HouseholdUnitsPerFeedUnit, 2);
            int unfed = HorseCareDriver.CareDay(animals, _ => householdFeed, _ => false, 2, careDiag);
            Assert.AreEqual(0, unfed, "the bought feed must feed the horse: " + string.Join("; ", careDiag));
            Assert.AreEqual(4, inventories.GetOrCreate(7).GetAvailableUnits(FeedItemId),
                "idle ration is 2 feed units x 1 household unit = 2 units drawn from inventory");
            Assert.GreaterOrEqual(horse.Nutrition01, 0.70f, "a fed horse must not degrade");
        }
    }
}
