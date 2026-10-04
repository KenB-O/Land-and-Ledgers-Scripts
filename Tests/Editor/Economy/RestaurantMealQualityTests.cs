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
    /// D1E: meal quality bands (Canon §8.1B) — skill-derived bands, the
    /// quality-aware price schedule, cooked-lot quality, served-meal quality
    /// prices, and the quality-aware nutrition-link seam.
    /// </summary>
    [TestFixture]
    public sealed class RestaurantMealQualityTests
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
        public void DeriveBand_FollowsSkillThresholds()
        {
            var policy = new RestaurantMealQualityPolicy();
            Assert.AreEqual(RestaurantMealQualityBand.Fine, policy.DeriveBand(10));
            Assert.AreEqual(RestaurantMealQualityBand.Fine, policy.DeriveBand(6));
            Assert.AreEqual(RestaurantMealQualityBand.House, policy.DeriveBand(5));
            Assert.AreEqual(RestaurantMealQualityBand.House, policy.DeriveBand(3));
            Assert.AreEqual(RestaurantMealQualityBand.Rough, policy.DeriveBand(2));
            Assert.AreEqual(RestaurantMealQualityBand.Rough, policy.DeriveBand(1));
            Assert.AreEqual(RestaurantMealQualityBand.House, policy.DeriveBand(0),
                "unknown/implicit cook rates the house standard, never punished");
        }

        [Test]
        public void QualityWeight01_BandsWeightTheCountLink()
        {
            Assert.AreEqual(1.15f, RestaurantMealQualityPolicy.QualityWeight01(RestaurantMealQualityBand.Fine));
            Assert.AreEqual(1.00f, RestaurantMealQualityPolicy.QualityWeight01(RestaurantMealQualityBand.House));
            Assert.AreEqual(0.70f, RestaurantMealQualityPolicy.QualityWeight01(RestaurantMealQualityBand.Rough));
            Assert.AreEqual(1.00f, RestaurantMealQualityPolicy.QualityWeight01(RestaurantMealQualityBand.Unspecified));
        }

        [Test]
        public void QualityAdjustedFreshPrice_MovesWithBand()
        {
            var prices = new RestaurantPriceSchedule(); // stew 15¢, fine +25%, rough -25%
            Assert.AreEqual(18, RestaurantMealPricing.QualityAdjustedFreshPriceCents(
                RestaurantMealCatalog.BeefStewId, RestaurantMealQualityBand.Fine, prices), "15 * 125 / 100");
            Assert.AreEqual(15, RestaurantMealPricing.QualityAdjustedFreshPriceCents(
                RestaurantMealCatalog.BeefStewId, RestaurantMealQualityBand.House, prices));
            Assert.AreEqual(11, RestaurantMealPricing.QualityAdjustedFreshPriceCents(
                RestaurantMealCatalog.BeefStewId, RestaurantMealQualityBand.Rough, prices), "15 * 75 / 100");
            Assert.AreEqual(15, RestaurantMealPricing.QualityAdjustedFreshPriceCents(
                RestaurantMealCatalog.BeefStewId, RestaurantMealQualityBand.Unspecified, prices),
                "old lots without a band price at the house standard");
        }

        [Test]
        public void SkilledCook_CooksFineMeal_ServedAtPremium()
        {
            var diag = new List<string>();
            var runtime = ReadyShop(diag);
            Assert.Null(runtime.AssignCook(personId: 11, cookingSkillLevel: 8, dayIndex: 300, diag: diag));
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);

            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            var lot = runtime.MealShelf[0];
            Assert.AreEqual(RestaurantMealQualityBand.Fine, lot.QualityBand, "a skill-8 cook's stew rates Fine");

            var record = runtime.ServeMeal(RestaurantMealCatalog.BeefStewId, 5, 300, diag);
            Assert.NotNull(record);
            Assert.AreEqual(RestaurantMealQualityBand.Fine, record.QualityBand);
            Assert.AreEqual(18, record.PriceCents, "Fine stew serves at the premium price");
        }

        [Test]
        public void DayOldDiscount_AppliesOnTopOfQualityPrice()
        {
            var diag = new List<string>();
            var runtime = ReadyShop(diag);
            Assert.Null(runtime.AssignCook(11, 8, 300, diag));
            runtime.PlanMealBatch(RestaurantMealCatalog.RoastPlateId, 1, 300, diag);
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            var record = runtime.ServeMeal(RestaurantMealCatalog.RoastPlateId, 5, 301, diag);

            Assert.NotNull(record);
            Assert.AreEqual(RestaurantMealCondition.DayOld, record.ConditionServedAt);
            Assert.AreEqual(RestaurantMealQualityBand.Fine, record.QualityBand);
            // Fresh Fine roast plate: 25 * 125 / 100 = 31; day-old: 31 * 50 / 100 = 15.
            Assert.AreEqual(15, record.PriceCents, "the leftover discount applies to the quality price");
        }

        [Test]
        public void GreenCook_CooksRoughMeal_ServedAtDiscount()
        {
            var diag = new List<string>();
            var runtime = ReadyShop(diag);
            Assert.Null(runtime.AssignCook(12, 1, 300, diag));
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);

            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            Assert.AreEqual(RestaurantMealQualityBand.Rough, runtime.MealShelf[0].QualityBand);
            var record = runtime.ServeMeal(RestaurantMealCatalog.BeefStewId, 5, 300, diag);
            Assert.NotNull(record);
            Assert.AreEqual(11, record.PriceCents, "Rough stew serves at the discount price");
        }

        [Test]
        public void Ledger_TracksQualityWeightPerDiner()
        {
            var diag = new List<string>();
            var runtime = ReadyShop(diag);
            Assert.Null(runtime.AssignCook(11, 8, 300, diag));
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));
            Assert.NotNull(runtime.ServeMeal(RestaurantMealCatalog.BeefStewId, 5, 300, diag));

            var ledger = (IRestaurantMealQualitySource)runtime.MealDaySource;
            Assert.AreEqual(1.15f, ledger.AverageMealQuality01(5, 300), 0.001f);
            Assert.AreEqual(1.0f, ledger.AverageMealQuality01(6, 300), 0.001f,
                "a diner with no recorded meals reports the house standard, not zero");
        }

        [Test]
        public void CompositeSource_AveragesQualityWeightedByCount()
        {
            var diag = new List<string>();
            var ledgerA = new RestaurantMealDayLedger();
            var ledgerB = new RestaurantMealDayLedger();
            Assert.Null(ledgerA.ReportServedMeal(5, 300, 1.15f, diag)); // Fine at house A
            Assert.Null(ledgerB.ReportServedMeal(5, 300, 0.70f, diag)); // Rough at house B
            Assert.Null(ledgerB.ReportServedMeal(5, 300, 0.70f, diag)); // twice at B

            var composite = new CompositeRestaurantMealSource(
                new IRestaurantMealDaySource[] { ledgerA, ledgerB });

            Assert.AreEqual(3, composite.MealsEatenAtRestaurant(5, 300));
            float expected = (1.15f + 0.70f + 0.70f) / 3f;
            Assert.AreEqual(expected, composite.AverageMealQuality01(5, 300), 0.001f,
                "the house eaten at twice weighs twice");
            Assert.AreEqual(1.0f, composite.AverageMealQuality01(9, 300), 0.001f);
        }
    }
}
