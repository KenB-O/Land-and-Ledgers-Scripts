using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Restaurant;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W2B: the restaurant menu is data; prices come from the schedule,
    /// unknown meals price at zero and never schedule; prep/cook task
    /// definitions carry the cooking skill and the NX-1 equipment gates.
    /// </summary>
    [TestFixture]
    public sealed class RestaurantMealCatalogTests
    {
        [Test]
        public void UnknownMeal_NeverKnown_NeverScheduled()
        {
            Assert.IsFalse(RestaurantMealCatalog.IsKnownMeal("rest.tasting-menu-1870"));
            Assert.IsFalse(RestaurantMealCatalog.IsKnownMeal(null));
            Assert.IsFalse(RestaurantMealCatalog.IsKnownMeal(string.Empty));

            Assert.IsTrue(RestaurantMealCatalog.IsKnownMeal(RestaurantMealCatalog.BeefStewId));
            Assert.IsTrue(RestaurantMealCatalog.IsKnownMeal(RestaurantMealCatalog.RoastPlateId));
            Assert.IsTrue(RestaurantMealCatalog.IsKnownMeal(RestaurantMealCatalog.CreamedStewId));
        }

        [Test]
        public void Spec_DataSane_PositiveIngredientsPositiveYield()
        {
            foreach (string mealId in new[]
                { RestaurantMealCatalog.BeefStewId, RestaurantMealCatalog.RoastPlateId, RestaurantMealCatalog.CreamedStewId })
            {
                var spec = RestaurantMealCatalog.GetSpec(mealId);
                Assert.AreEqual(mealId, spec.MealId);
                int ingredientKinds = 0;
                if (spec.MeatLbsPerBatch > 0) ingredientKinds++;
                if (spec.BreadLoavesPerBatch > 0) ingredientKinds++;
                if (spec.ProduceUnitsPerBatch > 0) ingredientKinds++;
                if (spec.DairyUnitsPerBatch > 0) ingredientKinds++;
                Assert.Greater(ingredientKinds, 0, $"{mealId}: a menu item needs at least one real ingredient");
                Assert.Greater(spec.MealsYieldPerBatch, 0, $"{mealId}: yield per batch");
                Assert.Greater(spec.PrepMinutes, 0, $"{mealId}: prep minutes");
                Assert.Greater(spec.CookMinutes, 0, $"{mealId}: cook minutes");
            }
        }

        [Test]
        public void Menu_UsesAllFourFoodGroups()
        {
            // Canon §8.1B: commercial meal ingredient economics across meat,
            // bread, produce, dairy — every pantry kind has a menu role.
            bool meat = false, bread = false, produce = false, dairy = false;
            foreach (string mealId in new[]
                { RestaurantMealCatalog.BeefStewId, RestaurantMealCatalog.RoastPlateId, RestaurantMealCatalog.CreamedStewId })
            {
                var spec = RestaurantMealCatalog.GetSpec(mealId);
                meat |= spec.MeatLbsPerBatch > 0;
                bread |= spec.BreadLoavesPerBatch > 0;
                produce |= spec.ProduceUnitsPerBatch > 0;
                dairy |= spec.DairyUnitsPerBatch > 0;
            }

            Assert.IsTrue(meat, "meat appears on the menu");
            Assert.IsTrue(bread, "bread appears on the menu");
            Assert.IsTrue(produce, "produce appears on the menu");
            Assert.IsTrue(dairy, "dairy appears on the menu");
        }

        [Test]
        public void FreshPrices_ComeFromSchedule_NeverGuessed()
        {
            var schedule = new RestaurantPriceSchedule(
                beefStewCents: 16, roastPlateCents: 26, creamedStewCents: 19, dayOldDiscountPct: 50);

            Assert.AreEqual(16, RestaurantMealCatalog.GetFreshPriceCents(RestaurantMealCatalog.BeefStewId, schedule));
            Assert.AreEqual(26, RestaurantMealCatalog.GetFreshPriceCents(RestaurantMealCatalog.RoastPlateId, schedule));
            Assert.AreEqual(19, RestaurantMealCatalog.GetFreshPriceCents(RestaurantMealCatalog.CreamedStewId, schedule));
            Assert.AreEqual(0, RestaurantMealCatalog.GetFreshPriceCents("rest.tasting-menu-1870", schedule));
        }

        [Test]
        public void DayOldPrice_AppliesDiscountFromSchedule()
        {
            var schedule = new RestaurantPriceSchedule(
                beefStewCents: 15, roastPlateCents: 25, creamedStewCents: 18, dayOldDiscountPct: 50);

            Assert.AreEqual(7, RestaurantMealCatalog.GetDayOldPriceCents(RestaurantMealCatalog.BeefStewId, schedule));
            Assert.AreEqual(12, RestaurantMealCatalog.GetDayOldPriceCents(RestaurantMealCatalog.RoastPlateId, schedule));
            Assert.AreEqual(9, RestaurantMealCatalog.GetDayOldPriceCents(RestaurantMealCatalog.CreamedStewId, schedule));
        }

        [Test]
        public void StageTaskId_BuildsStableIds()
        {
            Assert.AreEqual("rest.prep.rest.beef-stew",
                RestaurantMealCatalog.StageTaskId("prep", RestaurantMealCatalog.BeefStewId));
            Assert.AreEqual("rest.cook.rest.roast-plate",
                RestaurantMealCatalog.StageTaskId("cook", RestaurantMealCatalog.RoastPlateId));
        }

        [Test]
        public void RegisterAll_RegistersPrepAndCookForEveryMenuItem()
        {
            var authority = new TaskAuthority();
            RestaurantMealCatalog.RegisterAll(authority);

            foreach (string mealId in new[]
                { RestaurantMealCatalog.BeefStewId, RestaurantMealCatalog.RoastPlateId, RestaurantMealCatalog.CreamedStewId })
            {
                var spec = RestaurantMealCatalog.GetSpec(mealId);
                TaskDefinition prep = authority.GetDefinition(RestaurantMealCatalog.StageTaskId("prep", mealId));
                TaskDefinition cook = authority.GetDefinition(RestaurantMealCatalog.StageTaskId("cook", mealId));

                Assert.NotNull(prep, $"{mealId}: prep task registers.");
                Assert.NotNull(cook, $"{mealId}: cook task registers.");
                Assert.AreEqual(spec.PrepMinutes, prep.BaseMinutes, $"{mealId}: prep minutes");
                Assert.AreEqual(spec.CookMinutes, cook.BaseMinutes, $"{mealId}: cook minutes");
            }
        }

        [Test]
        public void RegisterAll_MealWorkRequiresCookingSkillKitAndKitchen()
        {
            var authority = new TaskAuthority();
            RestaurantMealCatalog.RegisterAll(authority);

            foreach (string mealId in new[]
                { RestaurantMealCatalog.BeefStewId, RestaurantMealCatalog.RoastPlateId, RestaurantMealCatalog.CreamedStewId })
            {
                TaskDefinition prep = authority.GetDefinition(RestaurantMealCatalog.StageTaskId("prep", mealId));
                TaskDefinition cook = authority.GetDefinition(RestaurantMealCatalog.StageTaskId("cook", mealId));

                Assert.AreEqual(RestaurantMealCatalog.CookingSkillId, prep.RequiredSkillId,
                    $"{mealId}: prep requires the cooking skill.");
                Assert.AreEqual(RestaurantMealCatalog.CookingSkillId, cook.RequiredSkillId,
                    $"{mealId}: cook requires the cooking skill.");
                Assert.Contains("kit:cook-hand-kit", prep.EquipmentClasses,
                    $"{mealId}: NX-1 teeth gate — prep needs a usable cook's hand kit.");
                Assert.Contains("kit:cook-hand-kit", cook.EquipmentClasses,
                    $"{mealId}: NX-1 teeth gate — cook needs a usable cook's hand kit.");
                Assert.Contains("workstation:restaurant-kitchen", cook.EquipmentClasses,
                    $"{mealId}: cooking happens at the restaurant kitchen — the room is not enough.");
                Assert.Contains("food-production", prep.DomainTags, $"{mealId}: food-production tag.");
                Assert.Contains("restaurant", cook.DomainTags, $"{mealId}: restaurant tag.");
            }
        }

        [Test]
        public void RegisterAll_PrepInputsDeclareRealIngredientItems()
        {
            var authority = new TaskAuthority();
            RestaurantMealCatalog.RegisterAll(authority);

            // Beef stew: meat + produce only — no bread, no dairy.
            TaskDefinition stewPrep = authority.GetDefinition(
                RestaurantMealCatalog.StageTaskId("prep", RestaurantMealCatalog.BeefStewId));
            var inputIds = new List<string>();
            foreach (var input in stewPrep.Inputs) inputIds.Add(input.ItemId);
            Assert.Contains(RestaurantMealCatalog.MeatItemId, inputIds);
            Assert.Contains(RestaurantMealCatalog.ProduceItemId, inputIds);
            Assert.IsFalse(inputIds.Contains(RestaurantMealCatalog.BreadItemId), "stew has no bread input");
            Assert.IsFalse(inputIds.Contains(RestaurantMealCatalog.DairyItemId), "stew has no dairy input");
        }
    }
}
