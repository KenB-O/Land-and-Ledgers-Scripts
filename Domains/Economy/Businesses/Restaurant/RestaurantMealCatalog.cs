using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Restaurant
{
    /// <summary>
    /// W2B: the restaurant's retail price schedule as data. Meal prices are
    /// retail policy (Canon §8.1B: menu abstracted to meal quality — an
    /// eating house sells hot meals, not ingredient line-items), NOT task
    /// execution fees — money still moves only through ledger authorities;
    /// this schedule is data the shop runtime and service paths read.
    /// Day-old (leftover) meals sell at a discount; spoiled meals are waste,
    /// never sold. All rates are calibration (Canon Part XV).
    /// </summary>
    [Serializable]
    public sealed class RestaurantPriceSchedule
    {
        [SerializeField, Min(0)]
        private int beefStewCents = 15;

        [SerializeField, Min(0)]
        private int roastPlateCents = 25;

        [SerializeField, Min(0)]
        private int creamedStewCents = 18;

        [SerializeField, Range(0, 100)]
        private int dayOldDiscountPct = 50;

        public RestaurantPriceSchedule() { }

        public RestaurantPriceSchedule(int beefStewCents, int roastPlateCents, int creamedStewCents, int dayOldDiscountPct)
        {
            this.beefStewCents = Math.Max(0, beefStewCents);
            this.roastPlateCents = Math.Max(0, roastPlateCents);
            this.creamedStewCents = Math.Max(0, creamedStewCents);
            this.dayOldDiscountPct = Math.Max(0, Math.Min(100, dayOldDiscountPct));
        }

        public int BeefStewCents => Math.Max(0, beefStewCents);
        public int RoastPlateCents => Math.Max(0, roastPlateCents);
        public int CreamedStewCents => Math.Max(0, creamedStewCents);

        /// <summary>Percent of the fresh price leftover (day-old) meals sell for (100 = no discount).</summary>
        public int DayOldDiscountPct => Math.Max(0, Math.Min(100, dayOldDiscountPct));
    }

    /// <summary>
    /// W2B: one menu item as data — ingredient inputs per cook batch, meals
    /// yielded per batch, and the labor minutes of each production stage.
    /// Frontier eating-house staples (Canon §8.1B: meal quality, not recipe
    /// minutiae): stew stretches meat across many plates, the roast plate is
    /// the full dinner, the creamed stew puts dairy to work. All quantities
    /// are calibration (Canon Part XV).
    /// </summary>
    public struct RestaurantMealSpec
    {
        public string MealId;
        public string DisplayName;
        public int MeatLbsPerBatch;
        public int BreadLoavesPerBatch;
        public int ProduceUnitsPerBatch;
        public int DairyUnitsPerBatch;
        public int MealsYieldPerBatch;
        public int PrepMinutes; // knife work: portion meat, chop veg, measure
        public int CookMinutes; // stove time
    }

    /// <summary>
    /// W2B: menu + meal work as task-system data (W2A bakery pattern).
    /// Canon Part III §3.1 analog: a restaurant generates meaningful tasks —
    /// portion meat, chop vegetables, build the stew pot, tend the stove,
    /// plate and serve — collapsed here to two meaningful stages (prep,
    /// cook), not animation granularity. The cook stage requires the
    /// restaurant kitchen workstation (stove + cookware — the room is not
    /// enough, mirroring the bake-oven gate, Tech X §3.5).
    /// </summary>
    public static class RestaurantMealCatalog
    {
        public const string BeefStewId = "rest.beef-stew";
        public const string RoastPlateId = "rest.roast-plate";
        public const string CreamedStewId = "rest.creamed-stew";

        /// <summary>W2B: item ids meal tasks declare as inputs. Resolved to real lots by the runtime.</summary>
        public const string MeatItemId = "restaurant-meat";       // lb, from the butcher
        public const string BreadItemId = "restaurant-bread";     // loaves, from the bakery
        public const string ProduceItemId = "restaurant-produce"; // units, from farms
        public const string DairyItemId = "restaurant-dairy";     // units (milk/butter), from the dairy

        /// <summary>W2B: cooking skill — already in the core TTS-3 starter set (SkillIds.Cooking); never re-registered.</summary>
        public const string CookingSkillId = SkillIds.Cooking;

        /// <summary>W2B: the NX-1 teeth gate for meal work — the cook's hand tools.</summary>
        public const string CookHandKitId = "cook-hand-kit";

        /// <summary>W2B: the kitchen workstation id. Defined in WorkstationCatalog (restaurant kitchen: stove-range + cookware-set + pantry-bins).</summary>
        public const string RestaurantKitchenStationId = "restaurant-kitchen";

        /// <summary>TUNING: batch cookings available per ready kitchen per day.</summary>
        public const int BatchesPerKitchenPerDay = 6;

        /// <summary>TUNING: beef stew — 2 lb meat + 3 produce → 6 meals.</summary>
        public const int BeefStewMeatLbs = 2;
        public const int BeefStewProduceUnits = 3;
        public const int BeefStewYieldMeals = 6;
        public const int BeefStewPrepMinutes = 45;
        public const int BeefStewCookMinutes = 60;

        /// <summary>TUNING: roast plate — 3 lb meat + 2 loaves + 2 produce → 6 meals.</summary>
        public const int RoastPlateMeatLbs = 3;
        public const int RoastPlateBreadLoaves = 2;
        public const int RoastPlateProduceUnits = 2;
        public const int RoastPlateYieldMeals = 6;
        public const int RoastPlatePrepMinutes = 60;
        public const int RoastPlateCookMinutes = 90;

        /// <summary>TUNING: creamed stew — 1 lb meat + 2 dairy + 2 produce → 5 meals.</summary>
        public const int CreamedStewMeatLbs = 1;
        public const int CreamedStewDairyUnits = 2;
        public const int CreamedStewProduceUnits = 2;
        public const int CreamedStewYieldMeals = 5;
        public const int CreamedStewPrepMinutes = 45;
        public const int CreamedStewCookMinutes = 60;

        /// <summary>Menu item specs as data, for the shop runtime's scheduler.</summary>
        public static RestaurantMealSpec GetSpec(string mealId)
        {
            if (string.Equals(mealId, BeefStewId, StringComparison.Ordinal))
                return new RestaurantMealSpec
                {
                    MealId = BeefStewId, DisplayName = "Beef stew",
                    MeatLbsPerBatch = BeefStewMeatLbs, BreadLoavesPerBatch = 0,
                    ProduceUnitsPerBatch = BeefStewProduceUnits, DairyUnitsPerBatch = 0,
                    MealsYieldPerBatch = BeefStewYieldMeals,
                    PrepMinutes = BeefStewPrepMinutes, CookMinutes = BeefStewCookMinutes,
                };
            if (string.Equals(mealId, RoastPlateId, StringComparison.Ordinal))
                return new RestaurantMealSpec
                {
                    MealId = RoastPlateId, DisplayName = "Roast plate",
                    MeatLbsPerBatch = RoastPlateMeatLbs, BreadLoavesPerBatch = RoastPlateBreadLoaves,
                    ProduceUnitsPerBatch = RoastPlateProduceUnits, DairyUnitsPerBatch = 0,
                    MealsYieldPerBatch = RoastPlateYieldMeals,
                    PrepMinutes = RoastPlatePrepMinutes, CookMinutes = RoastPlateCookMinutes,
                };
            if (string.Equals(mealId, CreamedStewId, StringComparison.Ordinal))
                return new RestaurantMealSpec
                {
                    MealId = CreamedStewId, DisplayName = "Creamed stew",
                    MeatLbsPerBatch = CreamedStewMeatLbs, BreadLoavesPerBatch = 0,
                    ProduceUnitsPerBatch = CreamedStewProduceUnits, DairyUnitsPerBatch = CreamedStewDairyUnits,
                    MealsYieldPerBatch = CreamedStewYieldMeals,
                    PrepMinutes = CreamedStewPrepMinutes, CookMinutes = CreamedStewCookMinutes,
                };
            return default;
        }

        /// <summary>True for the three menu items; false for anything else (unknown ids never schedule).</summary>
        public static bool IsKnownMeal(string mealId)
        {
            return !string.IsNullOrEmpty(GetSpec(mealId).MealId);
        }

        /// <summary>Reads the fresh retail price from the schedule. Unknown meals price at zero, never guessed.</summary>
        public static int GetFreshPriceCents(string mealId, RestaurantPriceSchedule prices)
        {
            prices = prices ?? new RestaurantPriceSchedule();
            if (string.Equals(mealId, BeefStewId, StringComparison.Ordinal)) return prices.BeefStewCents;
            if (string.Equals(mealId, RoastPlateId, StringComparison.Ordinal)) return prices.RoastPlateCents;
            if (string.Equals(mealId, CreamedStewId, StringComparison.Ordinal)) return prices.CreamedStewCents;
            return 0;
        }

        /// <summary>Leftover (day-old) price = fresh price × the schedule's day-old percent. Unknown meals price at zero.</summary>
        public static int GetDayOldPriceCents(string mealId, RestaurantPriceSchedule prices)
        {
            int fresh = GetFreshPriceCents(mealId, prices);
            prices = prices ?? new RestaurantPriceSchedule();
            return fresh * prices.DayOldDiscountPct / 100;
        }

        /// <summary>Task id for a meal stage (e.g. "rest.prep.rest.beef-stew").</summary>
        public static string StageTaskId(string stage, string mealId)
        {
            return $"rest.{stage}.{mealId}";
        }

        /// <summary>Registers the meal task definitions. Safe to call once at boot.</summary>
        public static void RegisterAll(TaskAuthority authority)
        {
            if (authority == null) throw new ArgumentNullException(nameof(authority));
            string ignored;

            // NX-1 teeth gate: every stage requires a usable cook's hand kit.
            string kit = EquipmentRequirementCodes.Kit(CookHandKitId);
            // Kitchen gate as data: cooking requires the stove — the room is
            // not enough (mirrors the Tech X §3.5 bake-oven gate). The shop
            // runtime pools kitchens and assigns batch cookings per batch.
            string kitchen = EquipmentRequirementCodes.Workstation(RestaurantKitchenStationId);

            foreach (string mealId in new[] { BeefStewId, RoastPlateId, CreamedStewId })
            {
                RestaurantMealSpec spec = GetSpec(mealId);

                var prep = new TaskDefinition(StageTaskId("prep", mealId),
                    $"Meal prep ({spec.DisplayName})", Math.Max(1, spec.PrepMinutes));
                prep.SetRequiredSkill(CookingSkillId, new[] { CookingSkillId });
                prep.SetDefaultPriority(TaskPriority.Normal);
                prep.DomainTags.Add("food-production");
                prep.DomainTags.Add("restaurant");
                prep.EquipmentClasses.Add(kit);
                AddInputs(prep, spec);
                authority.RegisterDefinition(prep, out ignored);

                var cook = new TaskDefinition(StageTaskId("cook", mealId),
                    $"Meal cook ({spec.DisplayName})", Math.Max(1, spec.CookMinutes));
                cook.SetRequiredSkill(CookingSkillId, new[] { CookingSkillId });
                cook.SetDefaultPriority(TaskPriority.Normal);
                cook.DomainTags.Add("food-production");
                cook.DomainTags.Add("restaurant");
                cook.EquipmentClasses.Add(kit);
                cook.EquipmentClasses.Add(kitchen);
                authority.RegisterDefinition(cook, out ignored);
            }
        }

        private static void AddInputs(TaskDefinition task, RestaurantMealSpec spec)
        {
            if (spec.MeatLbsPerBatch > 0) task.Inputs.Add(new TaskMaterial(MeatItemId, spec.MeatLbsPerBatch));
            if (spec.BreadLoavesPerBatch > 0) task.Inputs.Add(new TaskMaterial(BreadItemId, spec.BreadLoavesPerBatch));
            if (spec.ProduceUnitsPerBatch > 0) task.Inputs.Add(new TaskMaterial(ProduceItemId, spec.ProduceUnitsPerBatch));
            if (spec.DairyUnitsPerBatch > 0) task.Inputs.Add(new TaskMaterial(DairyItemId, spec.DairyUnitsPerBatch));
        }
    }
}
