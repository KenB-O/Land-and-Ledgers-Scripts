using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Ordering;
using LandLedgers.FirstLedger;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// Exercises the real item-level General Store port through household
    /// discovery, travel, payment, receipt, and the existing lot-backed meal
    /// service. Fixture money is explicitly a test input, not scenario policy.
    /// </summary>
    public sealed class GeneralStoreBreadPurchaseIntegrationTests
    {
        private const string DefinitionPath = "Assets/Core/Economy/GeneralStoreBusinessDefinition.asset";

        [Test]
        public void BootstrapPlayerPortSwitchesToIdentifiedStockAndPriceAfterFormation()
        {
            var definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(DefinitionPath);
            var owner = new GameObject(nameof(BootstrapPlayerPortSwitchesToIdentifiedStockAndPriceAfterFormation));
            try
            {
                var manager = owner.AddComponent<GeneralStoreRuntimeManager>();
                typeof(GeneralStoreRuntimeManager).GetField("storeDefinition", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(manager, definition);
                var bootstrapPort = manager.BuildHouseholdTradingPort();
                var supplier = new GeneralStoreSupplierAdapter(bootstrapPort);
                var business = BusinessInstanceState.Create("formed-player", definition, 60, BusinessOwnerIdentity.Player());
                business.RuntimeState.SetCurrentCashCents(100);
                business.RuntimeState.GetCategoryStock("staple_food").SetCurrentStockForTests(20);
                typeof(GeneralStoreRuntimeManager).GetField("currentBusiness", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(manager, business);
                Assert.IsTrue(bootstrapPort.UsesItemLevelInventory("bread"));
                Assert.AreEqual(5, supplier.ItemPricePerUnitCents("bread"));
                Assert.AreEqual(0, supplier.ItemStockUnits("flour"), "Category stock must not become fictional flour.");
                Assert.IsNull(bootstrapPort.ReceivePaidStock("real-player-receipt", "bread", "staple_food", "loaf",
                    2, 4, "explicit test purchase", 1));
                Assert.AreEqual(1, supplier.SellItem("bread", 1, 1, null, new List<string>()));
                Assert.AreEqual(101, business.RuntimeState.CurrentCashCents);
                Assert.AreEqual(2, business.RuntimeState.LastDailyRetailCostOfGoodsSoldCents);
                Assert.AreEqual(1, supplier.ItemStockUnits("bread"));
                Assert.IsFalse(bootstrapPort.TryConsumeStoreUnits("staple_food", 1, out _));
                var explicitPort = manager.BuildHouseholdTradingPort(business);
                var restored = BusinessInstanceState.Create("formed-player", definition, 60, BusinessOwnerIdentity.Player());
                typeof(GeneralStoreRuntimeManager).GetField("currentBusiness", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(manager, restored);
                Assert.AreEqual(0, supplier.ItemStockUnits("bread"));
                Assert.AreEqual(0, explicitPort.ItemStockUnits("bread"), "Explicit player ports must follow the restored owner too.");
                Assert.AreEqual(0, restored.RuntimeState.CurrentCashCents);
                Assert.AreEqual(101, business.RuntimeState.CurrentCashCents, "Restoration must not touch the detached till.");
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void RegisteringRestoredStoreReplacesTheDetachedSupplier()
        {
            var definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(DefinitionPath);
            var owner = new GameObject(nameof(RegisteringRestoredStoreReplacesTheDetachedSupplier));
            try
            {
                var manager = owner.AddComponent<GeneralStoreRuntimeManager>();
                typeof(GeneralStoreRuntimeManager).GetField("storeDefinition", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(manager, definition);
                var oldBusiness = BusinessInstanceState.Create("same-store", definition, 60,
                    BusinessOwnerIdentity.Npc(1, "Test owner", "Test"));
                var restoredBusiness = BusinessInstanceState.Create("same-store", definition, 60,
                    BusinessOwnerIdentity.Npc(1, "Test owner", "Test"));
                var oldPort = manager.BuildHouseholdTradingPort(oldBusiness);
                var restoredPort = manager.BuildHouseholdTradingPort(restoredBusiness);
                Assert.IsNull(restoredPort.ReceiveCostedRetailItemLot("restored-lot", "bread", "staple_food",
                    "loaf", 2, "test restored receipt", 1, 4));
                var directory = new SupplierDirectory();
                GeneralStoreSupplierAdapter.RegisterIn(directory, oldPort);
                GeneralStoreSupplierAdapter.RegisterIn(directory, restoredPort);
                GeneralStoreSupplierAdapter.RegisterIn(directory, restoredPort);
                Assert.AreEqual(1, directory.All.Count, "One business must not retain detached suppliers after restoration.");
                var active = (GeneralStoreSupplierAdapter)directory.All[0];
                Assert.AreEqual(1, active.SellItem("bread", 1, 1, null, new List<string>()));
                Assert.AreEqual(1, restoredBusiness.RuntimeState.GetRetailItemStockUnits("bread"));
                Assert.AreEqual(5, restoredBusiness.RuntimeState.CurrentCashCents);
                Assert.AreEqual(2, restoredBusiness.RuntimeState.LastDailyRetailCostOfGoodsSoldCents);
                Assert.AreEqual(0, oldBusiness.RuntimeState.CurrentCashCents);
                Assert.AreEqual(0, oldBusiness.RuntimeState.GetRetailItemStockUnits("bread"));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [TestCase(1)]
        [TestCase(4)]
        public void RestockAndRetailConsultTheSameConfiguredItemPrice(int storeUnitsPerPurchase)
        {
            var definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(DefinitionPath);
            var owner = new GameObject(nameof(RestockAndRetailConsultTheSameConfiguredItemPrice));
            try
            {
                var manager = owner.AddComponent<GeneralStoreRuntimeManager>();
                typeof(GeneralStoreRuntimeManager).GetField("storeDefinition", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(manager, definition);
                var business = BusinessInstanceState.Create("price-fixture", definition, 60,
                    BusinessOwnerIdentity.Npc(1, "Test owner", "Test"));
                var port = manager.BuildHouseholdTradingPort(business);
                foreach (GeneralStoreCategoryOffer offer in port.CategoryOffers)
                    if (offer.OfferedItemIds.Contains("bread"))
                    {
                        offer.ItemPriceOverrideCents = 7; // Explicit test offer, not a production price.
                        offer.StoreUnitsPerItemUnit = storeUnitsPerPurchase;
                    }
                Assert.AreEqual(7 * storeUnitsPerPurchase, port.ItemPricePerUnitCents("bread"));
                Assert.AreEqual(7, port.ItemRetailPricePerStoreUnitCents("bread"),
                    "Procurement must compare costs with the actual offer, normalized to store units.");
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void PaidLotSalesRetainExactCostRemaindersAcrossSerialization()
        {
            var definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(DefinitionPath);
            var owner = new GameObject(nameof(PaidLotSalesRetainExactCostRemaindersAcrossSerialization));
            try
            {
                var manager = owner.AddComponent<GeneralStoreRuntimeManager>();
                typeof(GeneralStoreRuntimeManager).GetField("storeDefinition", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(manager, definition);
                var business = BusinessInstanceState.Create("costed-receipt-test", definition, 60,
                    BusinessOwnerIdentity.Npc(1, "Test owner", "Test"));
                var port = manager.BuildHouseholdTradingPort(business);
                var supplier = new GeneralStoreSupplierAdapter(port);
                ItemDefinition bread = FindItem(definition, "bread");
                business.RuntimeState.SetCurrentCashCents(100);
                Assert.IsNull(port.ReceivePaidStock("costed-lot", bread.ItemId, bread.CategoryId, bread.Unit,
                    3, 10, "three units actually purchased for ten cents", 1));
                Assert.AreEqual(1, supplier.SellItem("bread", 1, 1, null, new List<string>()));
                Assert.AreEqual(3, business.RuntimeState.LastDailyRetailCostOfGoodsSoldCents);
                string firstReceipt = supplier.LastReceiptId;
                Assert.AreEqual(7, business.RuntimeState.FindRetailItemLot("costed-lot").RemainingAcquisitionCostCents);
                var dto = JsonUtility.FromJson<LandLedgers.Persistence.BusinessRuntimeSaveDto>(
                    JsonUtility.ToJson(business.RuntimeState.CaptureSaveDto()));
                typeof(BusinessInstanceState).GetField("runtimeState", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(business, BusinessRuntimeState.FromSaveDto(dto, BusinessType.GeneralStore));
                supplier = new GeneralStoreSupplierAdapter(port);
                Assert.AreEqual(1, supplier.SellItem("bread", 1, 1, null, new List<string>()));
                Assert.AreNotEqual(firstReceipt, supplier.LastReceiptId, "A fresh adapter after a same-day load must not reuse a receipt.");
                string secondReceipt = supplier.LastReceiptId;
                Assert.AreEqual(6, business.RuntimeState.LastDailyRetailCostOfGoodsSoldCents);
                supplier = new GeneralStoreSupplierAdapter(manager.BuildHouseholdTradingPort(business));
                Assert.AreEqual(1, supplier.SellItem("bread", 1, 1, null, new List<string>()));
                Assert.AreNotEqual(firstReceipt, supplier.LastReceiptId);
                Assert.AreNotEqual(secondReceipt, supplier.LastReceiptId);
                Assert.AreEqual(10, business.RuntimeState.LastDailyRetailCostOfGoodsSoldCents);
                Assert.AreEqual(0, business.RuntimeState.FindRetailItemLot("costed-lot").RemainingAcquisitionCostCents);
                Assert.AreEqual(0, business.RuntimeState.LastDailyUncostedRetailUnitsSold);
                Assert.AreEqual(105, port.CurrentCashCents, "100 - 10 acquisition + three actual 5c bread sales.");
                Assert.AreEqual(0, supplier.SellItem("bread", 1, 1, null, new List<string>()));
                Assert.AreEqual(10, business.RuntimeState.LastDailyRetailCostOfGoodsSoldCents);
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void LegacyUncostedLotDoesNotInventAnAcquisitionPrice()
        {
            var lot = BusinessRetailItemLotState.FromSaveDto(new LandLedgers.Persistence.BusinessRetailItemLotSaveDto
            {
                lotId = "old-save", itemId = "bread", categoryId = "staple_food", quantityUnits = 2
            });
            Assert.IsFalse(lot.AcquisitionCostKnown);
            Assert.IsTrue(lot.TryConsume(1, out int cost, out bool known));
            Assert.AreEqual(0, cost);
            Assert.IsFalse(known, "An absent old-save cost is unknown, not proof that goods were acquired free.");
        }

        [Test]
        public void PaidReceiptRefusalAndRestoredDuplicateLeaveRealStoreCashAndStockUnchanged()
        {
            var definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(DefinitionPath);
            var owner = new GameObject(nameof(PaidReceiptRefusalAndRestoredDuplicateLeaveRealStoreCashAndStockUnchanged));
            try
            {
                var manager = owner.AddComponent<GeneralStoreRuntimeManager>();
                typeof(GeneralStoreRuntimeManager).GetField("storeDefinition", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(manager, definition);
                var business = BusinessInstanceState.Create("receipt-test", definition, 60,
                    BusinessOwnerIdentity.Npc(1, "Test owner", "Test"));
                var port = manager.BuildHouseholdTradingPort(business);
                ItemDefinition bread = FindItem(definition, "bread");
                business.RuntimeState.SetCurrentCashCents(100);
                int before = port.CategoryStockUnits(bread.CategoryId);
                Assert.IsNull(port.ReceivePaidStock("paid-receipt-1", bread.ItemId, bread.CategoryId, bread.Unit,
                    1, 7, "explicit paid receipt fixture", 1));
                Assert.AreEqual(93, port.CurrentCashCents);
                Assert.AreEqual(before + 1, port.CategoryStockUnits(bread.CategoryId));
                var dto = JsonUtility.FromJson<LandLedgers.Persistence.BusinessRuntimeSaveDto>(
                    JsonUtility.ToJson(business.RuntimeState.CaptureSaveDto()));
                typeof(BusinessInstanceState).GetField("runtimeState", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(business, BusinessRuntimeState.FromSaveDto(dto, BusinessType.GeneralStore));
                Assert.IsNotNull(port.ReceivePaidStock("paid-receipt-1", bread.ItemId, bread.CategoryId, bread.Unit,
                    1, 7, "duplicate after real serialization", 1));
                Assert.IsNotNull(port.ReceivePaidStock("paid-receipt-2", bread.ItemId, "missing-category", bread.Unit,
                    1, 7, "refused category", 1));
                Assert.IsNotNull(port.ReceivePaidStock("paid-receipt-3", bread.ItemId, bread.CategoryId, bread.Unit,
                    1, 100, "unaffordable receipt", 1));
                Assert.AreEqual(93, port.CurrentCashCents);
                Assert.AreEqual(before + 1, port.CategoryStockUnits(bread.CategoryId));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void KnownHouseholdPurchasesB60BreadAndConsumesTheReceivedLotOnce(bool receiveWholesaleStock)
        {
            GeneralStoreBusinessDefinition definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(DefinitionPath);
            Assert.IsNotNull(definition);

            var managerObject = new GameObject(nameof(KnownHouseholdPurchasesB60BreadAndConsumesTheReceivedLotOnce));
            try
            {
                GeneralStoreRuntimeManager manager = managerObject.AddComponent<GeneralStoreRuntimeManager>();
                typeof(GeneralStoreRuntimeManager)
                    .GetField("storeDefinition", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(manager, definition);

                BusinessInstanceState store = BusinessInstanceState.Create(
                    "general_store_060", definition, 60,
                    BusinessOwnerIdentity.Npc(1, "Kennedy Baldwin-Ooms", "Baldwin-Ooms"));
                Assert.IsTrue(store.RuntimeState.TryAssignOwnerOperator(store.Owner),
                    "A staffed store is required for a real counter sale.");
                ItemDefinition bread = FindItem(definition, "bread");
                Assert.IsNotNull(bread);
                GeneralStoreTradingPort port = manager.BuildHouseholdTradingPort(store);
                var supplier = new GeneralStoreSupplierAdapter(port);
                Assert.AreEqual(0, supplier.ItemStockUnits("bread"));
                if (receiveWholesaleStock)
                {
                    // Test input represents an already settled wholesale receipt.
                    // Exercise production stocking into the actual live Store port,
                    // then purchase and consume through the same chain as opening stock.
                    var catalog = new Pass3StoreCatalog();
                    var stockingDiagnostics = new List<string>();
                    Assert.IsTrue(catalog.AddProduct(new Pass3ProductLine
                    {
                        ProductId = bread.ItemId,
                        DisplayName = "Test received bread",
                        PriceCents = supplier.ItemPricePerUnitCents("bread"),
                    }, stockingDiagnostics));
                    // AddProduct intentionally starts stock-free; install the
                    // explicitly declared receipt fixture after registration.
                    catalog.Find(bread.ItemId).ShelfUnits = 2;
                    Review2StockingResult stocked = Review2BakeryToStoreWholesaleDriver.StockShelfToLive(
                        catalog, bread.ItemId, 2, bread.CategoryId, port, stockingDiagnostics,
                        bread.Unit, "test-settled-bakery-lot", 0);
                    Assert.IsTrue(stocked.Stocked, stocked.Refusal + " " + string.Join(" | ", stockingDiagnostics));
                    Assert.AreEqual(0, catalog.Find(bread.ItemId).ShelfUnits);
                }
                else
                {
                    Assert.IsNull(store.RuntimeState.TryAddRetailItemLot(
                        "lot:B60:bread:opening", bread.ItemId, bread.CategoryId, bread.Unit, 2,
                        store.InstanceId, store.InstanceId, "scenario-opening-endowment:test", 0));
                }
                Assert.AreEqual(2, supplier.ItemStockUnits("bread"),
                    "The existing adapter must see received stock without rebuilding offers.");
                Assert.AreEqual(1, store.RuntimeState.RetailItemLots.Count,
                    "Receiving must create one identified lot rather than parallel stock.");
                Assert.IsTrue(supplier.OffersItem("bread"));
                int breadPrice = supplier.ItemPricePerUnitCents("bread");
                Assert.Greater(breadPrice, 0);

                var population = new PopulationState();
                population.people.Add(new PersonState
                {
                    id = 1,
                    firstName = "Test",
                    lastName = "Shopper",
                    age = 30,
                    ageBand = AgeBand.Adult18Plus,
                    laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                    homeBuildingId = 10,
                });
                HouseholdState household = new HouseholdState { id = 7, householdName = "Test Household" };
                population.households.Add(household);
                var memberships = new HouseholdMembershipRegistry();
                Assert.IsNull(memberships.Register(new HouseholdMembership
                {
                    MembershipId = 1,
                    PersonId = 1,
                    HouseholdId = 7,
                    StartDayIndex = 0,
                }));

                var ledgers = new HouseholdLedgerRegistry();
                HouseholdLedger householdLedger = ledgers.GetOrCreate(7);
                const int openingTestBalance = 10000;
                Assert.IsNull(householdLedger.RecordInflow(0, openingTestBalance,
                    HouseholdIncomeSource.OwnerContribution, "test-input", "test-only opening balance", "test"));
                var inventories = new HouseholdInventoryRegistry();
                var needs = new HouseholdNeedRegistry();
                HouseholdPurchasingNeed need = needs.CreateNeed(7, "bread", 0);
                need.UnitsNeeded = 1;
                need.IsNecessity = true;

                var suppliers = new SupplierDirectory();
                suppliers.Register(supplier);
                var knowledge = new ShopperSellerKnowledge();
                knowledge.LearnSupplier(1, store.InstanceId, "test fixture: known local shop", 0);
                var journeys = new JourneyModel();
                journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Home", 0f, 0f));
                // The trading port derives its journey location from the real
                // assigned building identity (building:60). Keep the test
                // network bound to that same authoritative destination.
                journeys.RegisterLocation(new JourneyLocation("building:60", JourneyLocationKind.Store, "General Store", 1f, 0f));
                journeys.AddEdge("home", "building:60", 1f, "local road");
                var tasks = new TaskAuthority();
                HouseholdShoppingLoop.RegisterTaskDefinitions(tasks);
                var loop = new HouseholdShoppingLoop(
                    population, memberships, ledgers, inventories, needs, suppliers, journeys,
                    tasks, new global::LandLedgers.Time.WorkTimeBudgetStore(), new PersonScheduleTracker(), knowledge,
                    new FinancialObligationAuthority(), new EntityIdRegistry());

                string personLocation = "home";
                var diagnostics = new List<string>();
                ShoppingTripResult purchase = loop.ExecuteNeed(need, 0, new ShoppingLoopOptions
                {
                    PersonLocationId = _ => personLocation,
                    SetPersonLocation = (_, location) => personLocation = location,
                    DepartureMinuteOfDay = 10 * 60,
                    InStoreMinutes = 15,
                    Diagnostics = diagnostics,
                });

                Assert.IsTrue(purchase.Success, purchase.FailureReason + " " + string.Join(" | ", diagnostics));
                Assert.AreEqual(1, purchase.UnitsAcquired);
                Assert.AreEqual(breadPrice, purchase.BuyerOutflowCents);
                Assert.AreEqual(breadPrice, purchase.StoreCashInCents);
                Assert.AreEqual(openingTestBalance - breadPrice, householdLedger.GetBalanceCents());
                Assert.AreEqual(breadPrice, store.RuntimeState.CurrentCashCents);
                Assert.AreEqual(1, supplier.ItemStockUnits("bread"));
                HouseholdInventory householdInventory = inventories.GetOrCreate(7);
                Assert.AreEqual(1, householdInventory.GetAvailableUnits("bread"));
                Assert.AreEqual(1, householdInventory.Lots.Count);
                int receivedLotId = householdInventory.Lots[0].LotId;
                StringAssert.Contains("purchased 1u bread from", householdInventory.Lots[0].SourceLabel);
                Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);

                var mealLog = new HouseholdMealLogRegistry();
                var workBudgets = new global::LandLedgers.Time.WorkTimeBudgetStore();
                var dailyNeeds = new DailyNeedsService();
                var dailyDiagnostics = new List<string>();
                DailyNeedsService.DayReport consumed = dailyNeeds.ExecuteDay(
                    population, new HouseholdConsumptionPlanner(), null, 1, dailyDiagnostics,
                    workBudgets, inventories: inventories, mealLog: mealLog,
                    mealPolicy: MealSchedulingPolicy.Default, memberships: memberships);
                Assert.AreEqual(1, consumed.MealsEaten);
                Assert.Greater(consumed.MealsMissed, 0,
                    "Only one bread unit was purchased; additional meals must not be invented.");
                Assert.AreEqual(0, householdInventory.GetAvailableUnits("bread"));
                Assert.AreEqual(1, population.GetPerson(1).nutrition.lastUpdateDayIndex);
                Assert.That(population.GetPerson(1).nutrition.nutrition01, Is.EqualTo(0.82f).Within(0.000001f),
                    "Nutrition must derive from one actual bread meal and one missed scheduled meal; " +
                    "the deliberately skipped midday slot is not a missed meal.");
                IReadOnlyList<HouseholdMealRecord> records = mealLog.GetMeals(7);
                Assert.AreEqual(1, records.Count);
                Assert.AreEqual(1, records[0].ItemsConsumed.Count);
                Assert.AreEqual("bread", records[0].ItemsConsumed[0].ItemId);
                Assert.AreEqual(receivedLotId, records[0].ItemsConsumed[0].LotId,
                    "Meal consumption must reference the exact received household lot.");
            }
            finally
            {
                Object.DestroyImmediate(managerObject);
            }
        }

        private static ItemDefinition FindItem(GeneralStoreBusinessDefinition definition, string itemId)
        {
            foreach (ItemDefinition item in definition.Items)
                if (item != null && string.Equals(item.ItemId, itemId, System.StringComparison.OrdinalIgnoreCase))
                    return item;
            return null;
        }
    }
}
