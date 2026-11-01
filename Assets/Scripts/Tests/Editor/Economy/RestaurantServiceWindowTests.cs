using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Restaurant;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1E: mealtime service windows (Canon §2.3: meals are day events) —
    /// declared services, batch planning against a service, per-service
    /// served counts, and loud honesty for meals outside a declared service.
    /// </summary>
    [TestFixture]
    public sealed class RestaurantServiceWindowTests
    {
        private static Func<string, WorkstationComponentView?> KitchenFinder()
        {
            var kinds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "stove-1", "stove-range" },
                { "cookware-1", "cookware-set" },
                { "pantry-1", "pantry-bins" },
            };
            return assetId => kinds.TryGetValue(assetId, out string kind)
                ? (WorkstationComponentView?)new WorkstationComponentView
                {
                    AssetId = assetId,
                    Kind = kind,
                    Condition01 = 0.9f,
                    IsUsable = true,
                }
                : null;
        }

        private static RestaurantShopRuntime ReadyShop(List<string> diag, int day = 300)
        {
            var runtime = new RestaurantShopRuntime("rest-biz-1");
            var registry = new EntityIdRegistry();
            RestaurantFoodBootstrap.ApplyBootstrapEndowment(runtime.FoodStock, registry, day, diag);
            RestaurantFuelBootstrap.ApplyBootstrapEndowment(runtime.FuelStock, registry, day, diag);
            Assert.Null(runtime.AddKitchen("cookhouse", new List<string>
            {
                "stove-1", "cookware-1", "pantry-1",
            }, diag), "kitchen installation should succeed");
            return runtime;
        }

        private static int WorkDay(RestaurantShopRuntime runtime, int day, List<string> diag)
        {
            var registry = new EntityIdRegistry();
            return runtime.WorkDay(day, cookKitUsable: true,
                WorkstationCatalog.RestaurantKitchen, KitchenFinder(), diag, registry);
        }

        [Test]
        public void DeclareServiceWindow_AcceptsAndRejectsDuplicates()
        {
            var diag = new List<string>();
            var runtime = ReadyShop(diag);

            Assert.Null(runtime.DeclareServiceWindow("dinner", "Dinner", 300, 12, diag));
            Assert.Null(runtime.DeclareServiceWindow("supper", "Supper", 300, 8, diag));

            string duplicate = runtime.DeclareServiceWindow("dinner", "Dinner", 300, 12, diag);
            Assert.NotNull(duplicate, "the same service is not declared twice on one day");

            Assert.Null(runtime.DeclareServiceWindow("dinner", "Dinner", 301, 12, diag),
                "the same service may repeat on another day");
            Assert.AreEqual(20, runtime.ServiceSchedule.PlannedCoversOnDay(300));
            Assert.AreEqual(12, runtime.ServiceSchedule.PlannedCoversOnDay(301));
        }

        [Test]
        public void PlanMealBatch_CanTargetAService()
        {
            var diag = new List<string>();
            var runtime = ReadyShop(diag);
            Assert.Null(runtime.DeclareServiceWindow("supper", "Supper", 300, 8, diag));

            var ids = runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag, "supper");

            Assert.NotNull(ids);
            Assert.AreEqual("supper", runtime.Batches[0].TargetServiceWindowId, "the batch names its planned service");
        }

        [Test]
        public void ServeMeal_AtDeclaredService_CountsTheCover()
        {
            var diag = new List<string>();
            var runtime = ReadyShop(diag);
            Assert.Null(runtime.DeclareServiceWindow("dinner", "Dinner", 300, 12, diag));
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            var record = runtime.ServeMeal(RestaurantMealCatalog.BeefStewId, 5, 300, diag, "dinner");

            Assert.NotNull(record);
            Assert.AreEqual("dinner", record.ServiceWindowId);
            var window = runtime.ServiceSchedule.FindWindow("dinner", 300);
            Assert.NotNull(window);
            Assert.AreEqual(1, window.ServedMeals);
            Assert.AreEqual(11, window.UnservedPlan, "12 planned, 1 served");
        }

        [Test]
        public void ServeMeal_OutsideDeclaredService_ServedAnyway_Loud()
        {
            var diag = new List<string>();
            var runtime = ReadyShop(diag);
            Assert.Null(runtime.DeclareServiceWindow("dinner", "Dinner", 300, 12, diag));
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            var record = runtime.ServeMeal(RestaurantMealCatalog.BeefStewId, 5, 300, diag, "midnight");

            Assert.NotNull(record, "the meal is real and is served regardless");
            Assert.AreEqual(string.Empty, record.ServiceWindowId,
                "an undeclared service is not recorded as one");
            StringAssert.Contains("no declared service", string.Join("\n", diag));
        }

        [Test]
        public void ServiceSchedule_RoundTripsSaveLoad()
        {
            var diag = new List<string>();
            var runtime = ReadyShop(diag);
            Assert.Null(runtime.DeclareServiceWindow("dinner", "Dinner", 300, 12, diag));
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));
            Assert.NotNull(runtime.ServeMeal(RestaurantMealCatalog.BeefStewId, 5, 300, diag, "dinner"));

            var dto = runtime.CaptureSaveDto();
            var restored = new RestaurantShopRuntime("rest-biz-1");
            restored.LoadFromSaveDto(dto);

            var window = restored.ServiceSchedule.FindWindow("dinner", 300);
            Assert.NotNull(window, "declared services survive save/load");
            Assert.AreEqual(12, window.PlannedMeals);
            Assert.AreEqual(1, window.ServedMeals);
            Assert.AreEqual("dinner", restored.ServedMeals[0].ServiceWindowId,
                "the served meal's service survives save/load");
        }
    }
}
