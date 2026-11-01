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
    /// W2B: the restaurant shop runtime — kitchen capacity gating the daily
    /// cook, meal-batch custody of ingredients, loud shortfall refusal,
    /// prepared-meal aging (leftover discounts, spoiled waste), per-person
    /// meal service feeding the nutrition link, and save/load.
    /// </summary>
    [TestFixture]
    public sealed class RestaurantShopRuntimeTests
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

        private static RestaurantShopRuntime StockedShop(
            List<string> diag, int kitchenCount = 1, int day = 290)
        {
            var runtime = new RestaurantShopRuntime("rest-biz-1");
            var registry = new EntityIdRegistry();
            RestaurantFoodBootstrap.ApplyBootstrapEndowment(runtime.FoodStock, registry, day, diag);
            // D1E: the stove-fuel endowment — batch cooking burns fuel.
            RestaurantFuelBootstrap.ApplyBootstrapEndowment(runtime.FuelStock, registry, day, diag);
            for (int i = 0; i < kitchenCount; i++)
            {
                Assert.Null(runtime.AddKitchen("cookhouse", new List<string>
                {
                    "stove-1", "cookware-1", "pantry-1",
                }, diag), "kitchen installation should succeed");
            }

            return runtime;
        }

        private static int WorkDay(RestaurantShopRuntime runtime, int day, List<string> diag, bool kitUsable = true)
        {
            var registry = new EntityIdRegistry();
            return runtime.WorkDay(day, cookKitUsable: kitUsable,
                WorkstationCatalog.RestaurantKitchen, KitchenFinder(), diag, registry);
        }

        [Test]
        public void PlanMealBatch_RefusesUnknownMenuItem_Loudly()
        {
            var runtime = new RestaurantShopRuntime("rest-biz-1");
            var diag = new List<string>();

            var ids = runtime.PlanMealBatch("rest.tasting-menu-1870", 1, 300, diag);
            Assert.IsNull(ids, "unknown menu item must refuse (not schedule)");
            StringAssert.Contains("RestaurantShopRuntime:", string.Join("\n", diag));
            Assert.AreEqual(0, runtime.Batches.Count);
        }

        [Test]
        public void WorkDay_WithoutCookKit_CooksNothing_BatchesParked()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 2, 300, diag);

            int cooked = WorkDay(runtime, 300, diag, kitUsable: false);

            Assert.AreEqual(0, cooked);
            Assert.AreEqual(2, runtime.Batches.Count);
            foreach (var batch in runtime.Batches)
                Assert.AreEqual(RestaurantMealBatchStage.Planned, batch.Stage, "batches stay planned without the kit");
            StringAssert.Contains("hand kit", string.Join("\n", diag));
        }

        [Test]
        public void WorkDay_WithoutReadyKitchen_CooksNothing()
        {
            var diag = new List<string>();
            var runtime = new RestaurantShopRuntime("rest-biz-1");
            var registry = new EntityIdRegistry();
            RestaurantFoodBootstrap.ApplyBootstrapEndowment(runtime.FoodStock, registry, 290, diag);
            // No kitchen installed at all — cooking requires the stove.
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);

            int cooked = WorkDay(runtime, 300, diag);

            Assert.AreEqual(0, cooked);
            Assert.AreEqual(RestaurantMealBatchStage.Planned, runtime.Batches[0].Stage);
            StringAssert.Contains("no ready kitchen", string.Join("\n", diag));
        }

        [Test]
        public void WorkDay_CooksBatches_MealLotCarriesProvenance()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);

            int cooked = WorkDay(runtime, 300, diag);

            Assert.AreEqual(1, cooked);
            Assert.AreEqual(RestaurantMealBatchStage.Cooked, runtime.Batches[0].Stage);
            Assert.AreEqual(1, runtime.MealShelf.Count);

            var lot = runtime.MealShelf[0];
            Assert.AreEqual(RestaurantMealCatalog.BeefStewId, lot.MealId);
            Assert.AreEqual(RestaurantMealCatalog.BeefStewYieldMeals, lot.MealsRemaining);
            Assert.AreEqual(RestaurantMealCondition.Fresh, lot.Condition);
            Assert.Greater(lot.InputProvenance.Count, 0, "ingredient provenance cooked into the lot");
            StringAssert.Contains("restaurant-meat", string.Join(" | ", lot.InputProvenance));
            Assert.AreEqual(0, runtime.Batches[0].IngredientsInCustody.Count,
                "custody moved into the lot — no double counting");
        }

        [Test]
        public void WorkDay_PantryShortfall_ParksBatch_NothingConjured()
        {
            var diag = new List<string>();
            var runtime = new RestaurantShopRuntime("rest-biz-1");
            Assert.Null(runtime.AddKitchen("cookhouse",
                new List<string> { "stove-1", "cookware-1", "pantry-1" }, diag));
            // Empty pantry — nothing to cook with.
            runtime.PlanMealBatch(RestaurantMealCatalog.RoastPlateId, 1, 300, diag);

            int cooked = WorkDay(runtime, 300, diag);

            Assert.AreEqual(0, cooked);
            Assert.AreEqual(RestaurantMealBatchStage.Planned, runtime.Batches[0].Stage);
            Assert.AreEqual(0, runtime.MealShelf.Count);
            StringAssert.Contains("shortfall", string.Join("\n", diag));
        }

        [Test]
        public void WorkDay_LaborBindsBeforeKitchenSlots()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            // 7 stew batches: each needs 45 prep + 60 cook = 105 min; the
            // cook's day holds 600 min → 5 cook, the 6th preps but waits,
            // the 7th stays planned.
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 7, 300, diag);

            int cooked = WorkDay(runtime, 300, diag);

            Assert.AreEqual(5, cooked, "600 cook-minutes / 105 per stew batch = 5");
            int prepped = 0, planned = 0;
            foreach (var batch in runtime.Batches)
            {
                if (batch.Stage == RestaurantMealBatchStage.Prepped) prepped++;
                if (batch.Stage == RestaurantMealBatchStage.Planned) planned++;
            }

            Assert.AreEqual(1, prepped, "one batch prepped but out of cook minutes");
            Assert.AreEqual(1, planned, "one batch never started");
        }

        [Test]
        public void ServeMeal_ServesOldestLot_FreshPrice_RecordsDiner()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            var record = runtime.ServeMeal(RestaurantMealCatalog.BeefStewId, personId: 5, dayIndex: 300, diag);

            Assert.NotNull(record, "service succeeds when the shelf holds meals");
            Assert.AreEqual(5, record.PersonId, "the diner is a real person — nutrition credit has a target");
            Assert.AreEqual(RestaurantMealCatalog.BeefStewId, record.MealId);
            Assert.AreEqual(new RestaurantPriceSchedule().BeefStewCents, record.PriceCents, "fresh meal, fresh price");
            Assert.AreEqual(RestaurantMealCondition.Fresh, record.ConditionServedAt);
            StringAssert.Contains("restaurant-meat", record.ProvenanceChain, "provenance reaches the served plate");
            Assert.AreEqual(RestaurantMealCatalog.BeefStewYieldMeals - 1, runtime.MealShelf[0].MealsRemaining);
            Assert.AreEqual(1, runtime.MealDaySource.MealsEatenAtRestaurant(5, 300),
                "the nutrition-link ledger sees the served meal");
            Assert.AreEqual(0, runtime.MealDaySource.MealsEatenAtRestaurant(6, 300),
                "other persons are unaffected");
        }

        [Test]
        public void ServeMeal_EmptyShelf_RefusesLoudly()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);

            var record = runtime.ServeMeal(RestaurantMealCatalog.BeefStewId, 5, 300, diag);

            Assert.IsNull(record, "no meals, no service — finite supply");
            StringAssert.Contains("RestaurantShopRuntime:", string.Join("\n", diag));
            Assert.AreEqual(0, runtime.ServedMeals.Count);
        }

        [Test]
        public void ServeMeal_AnonymousDiner_Refused()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            var record = runtime.ServeMeal(RestaurantMealCatalog.BeefStewId, personId: 0, dayIndex: 300, diag);

            Assert.IsNull(record, "the nutrition link credits real diners only — no anonymous meals");
            Assert.AreEqual(RestaurantMealCatalog.BeefStewYieldMeals, runtime.MealShelf[0].MealsRemaining,
                "nothing left the shelf");
        }

        [Test]
        public void ServeMeal_LeftoverDay_ServesAtDiscount()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            runtime.PlanMealBatch(RestaurantMealCatalog.RoastPlateId, 1, 300, diag);
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            var record = runtime.ServeMeal(RestaurantMealCatalog.RoastPlateId, 5, 301, diag);

            Assert.NotNull(record);
            Assert.AreEqual(RestaurantMealCondition.DayOld, record.ConditionServedAt);
            Assert.AreEqual(new RestaurantPriceSchedule().RoastPlateCents / 2, record.PriceCents,
                "leftover meals sell at the schedule discount");
        }

        [Test]
        public void AgeShelf_SpoiledMealsBecomeWaste_NeverServed()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            runtime.AgeShelf(302, diag); // two days later: spoiled

            Assert.AreEqual(RestaurantMealCondition.Spoiled, runtime.MealShelf[0].Condition);
            Assert.AreEqual(1, runtime.Waste.Count, "one loud write-off");
            Assert.Greater(runtime.Waste[0].ProvenanceChain.Length, 0, "provenance retained on waste");

            var record = runtime.ServeMeal(RestaurantMealCatalog.BeefStewId, 5, 302, diag);
            Assert.IsNull(record, "spoiled meals are never served");
        }

        [Test]
        public void PopulateWorkstations_AliasesFirstReadyKitchen()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);

            var registry = new BusinessWorkstations();
            runtime.PopulateWorkstations(registry, WorkstationCatalog.RestaurantKitchen, KitchenFinder(), diag);

            Assert.NotNull(registry.Get("restaurant-kitchen"),
                "the declared kitchen id resolves so the equipment gate can check it");
            Assert.NotNull(registry.Get("restaurant-kitchen-0"), "per-kitchen instance registered");
        }

        [Test]
        public void SaveLoad_RoundTripsPantryShelfServiceAndLedger()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            runtime.PlanMealBatch(RestaurantMealCatalog.CreamedStewId, 1, 300, diag);
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));
            Assert.NotNull(runtime.ServeMeal(RestaurantMealCatalog.CreamedStewId, 7, 300, diag));

            var dto = runtime.CaptureSaveDto();
            var restored = new RestaurantShopRuntime("rest-biz-1");
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.MealShelf.Count);
            Assert.AreEqual(RestaurantMealCatalog.CreamedStewYieldMeals - 1, restored.MealShelf[0].MealsRemaining);
            Assert.AreEqual(1, restored.ServedMeals.Count);
            Assert.AreEqual(7, restored.ServedMeals[0].PersonId);
            Assert.AreEqual(1, restored.MealDaySource.MealsEatenAtRestaurant(7, 300),
                "the served-meal ledger survives save/load");
            Assert.Greater(restored.FoodStock.UnitsOnHand(RestaurantMealCatalog.MeatItemId), 0,
                "pantry survives save/load");
            Assert.AreEqual(1, restored.Kitchens.Count, "kitchens survive save/load");
        }
    }
}
