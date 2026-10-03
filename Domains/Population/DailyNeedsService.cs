using System;
using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Population
{
    /// <summary>
    /// T2C: NPC daily-needs execution. HF-4 plans consumption but nothing
    /// executed it — this is the other half. Each day, every household's
    /// members eat (meals need a source — GHOST-DEF-006), reserves are
    /// consumed causally, shortfalls become real ProcurementNeeds executed by
    /// an acting household member through the T1A embodied-purchase executor,
    /// and the planner's remaining needs are executed the same way.
    /// Nothing is spawned, nothing is faked: no acting adult means unmet
    /// needs; no supplier or no time means missed meals, logged honestly.
    /// </summary>
    public sealed class DailyNeedsService
    {
        /// <summary>Meals per person per day (calibration).</summary>
        public const int MealsPerPersonPerDay = 2;
        /// <summary>Reserve category that feeds meals.</summary>
        public const string MealCategoryId = "staple_food";

        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        public sealed class DayReport
        {
            public int DayIndex;
            public int HouseholdsServed;
            public int MealsEaten;
            public int MealsMissed;
            public int PurchasesMade;
            public int SpendCents;
        }

        /// <summary>
        /// Executes one day of needs for every household in the population.
        /// The optional budget store receives each person's nutrition-derived
        /// work capacity (NX-1B: missed meals bite as fewer usable minutes).
        /// </summary>
        public DayReport ExecuteDay(
            PopulationState population,
            HouseholdConsumptionPlanner planner,
            IEmbodiedPurchaseExecutor executor,
            int dayIndex,
            List<string> diag,
            WorkTimeBudgetStore budgetStore = null)
        {
            diag = diag ?? diagnostics;
            var report = new DayReport { DayIndex = dayIndex };
            if (population == null || planner == null || executor == null)
            {
                diag.Add("DailyNeedsService.ExecuteDay: missing population, planner, or executor — no needs executed.");
                return report;
            }

            foreach (HouseholdState household in population.households)
            {
                if (household == null) continue;
                ExecuteHousehold(population, household, planner, executor, dayIndex, report, diag, budgetStore);
            }
            return report;
        }

        private void ExecuteHousehold(
            PopulationState population, HouseholdState household,
            HouseholdConsumptionPlanner planner, IEmbodiedPurchaseExecutor executor,
            int dayIndex, DayReport report, List<string> diag,
            WorkTimeBudgetStore budgetStore)
        {
            int actingPersonId = FindActingAdult(population, household);
            if (actingPersonId < 0)
            {
                diag.Add($"DailyNeedsService: H{household.id} has no acting adult — needs unmet, nothing faked.");
                return;
            }
            report.HouseholdsServed++;

            // Member states for per-person allocation (NX-1B).
            var members = new List<PersonState>();
            if (household.memberIds != null)
                foreach (int memberId in household.memberIds)
                {
                    PersonState p = population.GetPerson(memberId);
                    if (p != null) members.Add(p);
                }

            // 1. Meals (GHOST-DEF-006): every member eats; each meal consumes
            //    one unit of staple_food from household reserves.
            int mealsNeeded = members.Count * MealsPerPersonPerDay;
            int mealsAvailable = 0;
            if (mealsNeeded > 0)
            {
                HouseholdReserveState mealReserve = FindReserve(household, MealCategoryId);
                int onHand = mealReserve != null ? Math.Max(0, mealReserve.currentUnits) : 0;
                int fromStores = Math.Min(onHand, mealsNeeded);
                if (mealReserve != null) mealReserve.currentUnits -= fromStores;
                mealsAvailable += fromStores;

                int shortfall = mealsNeeded - fromStores;
                if (shortfall > 0)
                {
                    // The shortfall is shopped for by the acting member, like
                    // any other need — an embodied purchase, not conjured food.
                    var need = new ProcurementNeed
                    {
                        HouseholdId = household.id,
                        CategoryId = MealCategoryId,
                        UnitsNeeded = shortfall,
                        Priority01 = 1.0f,
                        RequestingPersonId = actingPersonId,
                        Reason = $"meals: {shortfall} of {mealsNeeded} not covered by stores",
                        DayIndex = dayIndex,
                    };
                    PurchaseExecutionResult result = executor.Execute(need, actingPersonId, dayIndex);
                    if (result != null && result.Success)
                    {
                        report.PurchasesMade++;
                        report.SpendCents += result.AmountPaidCents;
                        // The executor credited reserves; take what arrived.
                        HouseholdReserveState after = FindReserve(household, MealCategoryId);
                        int available = after != null ? Math.Max(0, after.currentUnits) : 0;
                        int eatNow = Math.Min(available, shortfall);
                        if (after != null) after.currentUnits -= eatNow;
                        mealsAvailable += eatNow;
                    }
                }
            }

            // NX-1B: scarcity allocation — meals go per person by priority
            // (children → workers → others); each person's nutrition derives
            // from their actual meals (Tech X §2.9).
            Dictionary<int, int> allocation = MealAllocator.Allocate(members, mealsAvailable, MealsPerPersonPerDay);
            int mealsEaten = 0;
            int undernourished = 0;
            foreach (PersonState p in members)
            {
                int eaten = allocation.TryGetValue(p.id, out int a) ? a : 0;
                mealsEaten += eaten;
                bool missedAny = p.nutrition.ApplyDay(eaten, MealsPerPersonPerDay, dayIndex);
                if (missedAny) undernourished++;

                // Teeth: nutrition sets tomorrow's usable work minutes.
                if (budgetStore != null)
                {
                    WorkTimeBudget budget = budgetStore.GetOrCreate(
                        EntityId.For(EntityKind.Person, p.id));
                    budget.SetCapacityMultiplier(p.nutrition.WorkCapacityMultiplier);
                }
            }
            int missed = mealsNeeded - mealsEaten;
            report.MealsEaten += mealsEaten;
            report.MealsMissed += Math.Max(0, missed);
            if (missed > 0)
                diag.Add($"DailyNeedsService: H{household.id} missed {missed} meals on day {dayIndex} ({undernourished} undernourished) — no supplier, no time, or no money. Logged, not faked.");

            // 2. The planner's remaining needs, executed through the same
            //    embodied channel (built on post-meal reserve levels, so the
            //    meal purchase above is never double-bought).
            List<ProcurementNeed> plan = planner.BuildPlan(household, actingPersonId, dayIndex);
            foreach (ProcurementNeed need in plan)
            {
                PurchaseExecutionResult result = executor.Execute(need, actingPersonId, dayIndex);
                if (result != null && result.Success)
                {
                    report.PurchasesMade++;
                    report.SpendCents += result.AmountPaidCents;
                }
            }
        }

        private int FindActingAdult(PopulationState population, HouseholdState household)
        {
            if (household.memberIds == null) return -1;
            foreach (int memberId in household.memberIds)
            {
                PersonState p = population.GetPerson(memberId);
                if (p != null && p.ageBand == AgeBand.Adult18Plus && p.laborAccessLevel != LaborAccessLevel.None)
                    return p.id;
            }
            return -1;
        }

        private HouseholdReserveState FindReserve(HouseholdState household, string categoryId)
        {
            if (household.reserves == null) return null;
            foreach (HouseholdReserveState reserve in household.reserves)
            {
                if (reserve != null && string.Equals(reserve.categoryId, categoryId, StringComparison.OrdinalIgnoreCase))
                    return reserve;
            }
            return null;
        }
    }
}
