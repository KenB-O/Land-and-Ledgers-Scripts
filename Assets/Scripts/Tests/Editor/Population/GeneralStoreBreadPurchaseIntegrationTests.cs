using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Financing;
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
        public void KnownHouseholdPurchasesB60BreadAndConsumesTheReceivedLotOnce()
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
                Assert.IsNull(store.RuntimeState.TryAddRetailItemLot(
                    "lot:B60:bread:opening", bread.ItemId, bread.CategoryId, bread.Unit, 2,
                    store.InstanceId, store.InstanceId, "scenario-opening-endowment:test", 0));

                GeneralStoreTradingPort port = manager.BuildHouseholdTradingPort(store);
                var supplier = new GeneralStoreSupplierAdapter(port);
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
