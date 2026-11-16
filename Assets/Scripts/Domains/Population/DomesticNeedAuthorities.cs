using System;

namespace LandLedgers.Population
{
    /// <summary>
    /// Campaign C, package 24 — DEF-010 (domestic labour: 3 of 4 need triggers
    /// hard-coded off), owner Decision 6 (Nov 15, 2026).
    ///
    /// The domestic labour driver (DomesticLaborDriver) was already wired
    /// end-to-end, but its three need inputs were hard-coded off in
    /// SimulationDrivers.DriveDomesticLaborDay (MealsAtHomeCount = 0,
    /// FuelHandlingNeeded = false, CleaningNeeded = false), so households
    /// never cooked, handled fuel, or cleaned — only animal care ever ran.
    ///
    /// These are the three missing need authorities, each computed from real
    /// state (Decision 6: cooking from meal requirements, available
    /// ingredients, occupants, work schedules and cooking equipment; fuel
    /// handling from actual stove/heating consumption, reserves and
    /// equipment; cleaning from accumulated household use, occupancy and
    /// cleanliness state). Pure static functions over explicit state, so the
    /// Roslyn harness can pin them without the SimulationDrivers closure.
    ///
    /// What these authorities do NOT do (by design):
    /// - They never change meal accounting. MealsAtHomeToday reads the meal
    ///   authority's own served-meal records (HouseholdMealLogRegistry);
    ///   planning a meal-prep task credits family labour only — it serves
    ///   no meal and changes no count (DEF-015 meal-count behaviour
    ///   untouched).
    /// - They never move fuel or cash. FuelHandlingNeeded reads the heating
    ///   burn forecast and the fuel reserve on hand; the fuel itself is
    ///   still consumed only by HouseholdReserveEvaluator (DEF-019 fuel
    ///   reserve-credit behaviour untouched).
    /// - They never assign a person. Selection stays with the driver's
    ///   availability-first FindFreeMemberForTaskWindow (lowest planned
    ///   domestic load among available adults; no sex, no fabricated role).
    /// </summary>
    public static class DomesticNeedAuthorities
    {
        /// <summary>
        /// Cooking requirement: meals the household eats at home today, read
        /// from the meal authority's own served-meal records. A meal counts
        /// as at-home when it was served from the household inventory
        /// (HouseholdInventory) or prepared in-house from raw ingredients
        /// (HouseholdProduction). Meals served out of home (PurchasedMeal,
        /// BoardingArrangement, EmployerProvided) and missed meals (never
        /// recorded as served) do not count.
        ///
        /// DriveDailyNeeds runs before DriveDomesticLaborDay in the same
        /// OnDayChanged, so today's records are already written when this
        /// is read — the count is a measurement of the day's at-home eating,
        /// not a forecast. A household whose larder is empty serves no meals
        /// at home and gets no meal-prep task: the shortfall is a shopping
        /// need, not a cooking task.
        ///
        /// Cooking equipment: every household has a stove (HouseholdState.
        /// stoveTier is Basic or Efficient; there is no stove-less tier),
        /// so no separate equipment gate is needed here — the stove tier
        /// already shapes heating fuel use in HouseholdHeatingEvaluator.
        /// </summary>
        public static int MealsAtHomeToday(HouseholdMealLogRegistry mealLog, int householdId, int dayIndex)
        {
            if (mealLog == null || householdId < 0)
            {
                return 0;
            }

            int count = 0;
            foreach (HouseholdMealRecord record in mealLog.GetMeals(householdId))
            {
                if (record == null || record.DayIndex != dayIndex)
                {
                    continue;
                }

                if (IsAtHomeMealSource(record.SourceKind))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// True when a meal source kind means the meal was eaten at home.
        /// </summary>
        public static bool IsAtHomeMealSource(MealSourceKind source)
        {
            return source == MealSourceKind.HouseholdInventory
                || source == MealSourceKind.HouseholdProduction;
        }

        /// <summary>
        /// Fuel-handling need: true when the household burns fuel today AND
        /// has fuel on hand to handle. The burn is the heating authority's
        /// own forecast (HouseholdHeatingEvaluator.CalculateDailyFuelUseUnits:
        /// season strain by month, member load, heat-retention and stove
        /// multipliers) — zero outside heating season, at least one unit in
        /// season. The on-hand check reads the fuel-wood reserve directly
        /// (no initialisation side effect, mirroring DailyNeedsService's
        /// private FindReserve): a household with no fuel has nothing to
        /// handle, and its shortfall is a procurement need, not a handling
        /// task. The check is order-independent: whether today's burn was
        /// already deducted or not, fuel remaining on hand still needs
        /// handling to reach the stove.
        /// </summary>
        public static bool FuelHandlingNeeded(HouseholdState household, int dayIndex)
        {
            if (household == null)
            {
                return false;
            }

            if (HouseholdHeatingEvaluator.CalculateDailyFuelUseUnits(household, dayIndex) <= 0)
            {
                return false;
            }

            if (household.reserves == null)
            {
                return false;
            }

            foreach (HouseholdReserveState reserve in household.reserves)
            {
                if (reserve != null && string.Equals(reserve.categoryId,
                    HouseholdReserveCatalog.FuelWoodCategoryId, StringComparison.OrdinalIgnoreCase))
                {
                    return reserve.currentUnits > 0;
                }
            }

            return false;
        }
    }
}
