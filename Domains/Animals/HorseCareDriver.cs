using System;
using System.Collections.Generic;

namespace LandLedgers.Animals
{
    /// <summary>
    /// Pass VII Workstream B (P1-5): the finite feed stock horses eat from.
    /// The production implementation wraps the farm FeedLoop; tests supply a
    /// stub. Feed is never free: TakeFeedUnits returns only what the stock
    /// actually holds.
    /// </summary>
    public interface IHorseFeedStock
    {
        int FeedStockUnits { get; }

        /// <summary>Takes up to requestedUnits from the stock; returns units actually taken.</summary>
        int TakeFeedUnits(int requestedUnits);
    }

    /// <summary>
    /// Pass VII Workstream B (P1-5): production daily horse care. Every active
    /// horse is fed from a real, finite feed stock (real cost); a fully fed
    /// horse recovers through HorseAuthority.ApplyCare, an underfed horse
    /// degrades instead. Horses never tire in production and never need care
    /// is over: condition now moves every day in both directions.
    ///
    /// Calibration (tuning, not canon): daily rations match the feed loop's
    /// horse rates; a working horse eats more than an idle one. Water and rest
    /// come from the keeper's daily round — a horse holding a named housing
    /// arrangement is watered; a horse off duty rests.
    /// </summary>
    public static class HorseCareDriver
    {
        /// <summary>Daily feed units for an idle horse (matches FeedLoop.DailyFeedPerHorse).</summary>
        public const float DailyFeedUnitsPerHorse = 1.2f;

        /// <summary>Daily feed units for a working horse (matches FeedLoop.DailyFeedPerWorkingHorse).</summary>
        public const float DailyFeedUnitsPerWorkingHorse = 2.5f;

        /// <summary>Calibration: nutrition lost per day of full shortfall.</summary>
        public const float StarvationNutritionLoss01 = 0.15f;

        /// <summary>Calibration: fatigue gained per day of full shortfall.</summary>
        public const float StarvationFatigueGain01 = 0.10f;

        /// <summary>
        /// One day of care for every active horse in the registry. Returns the
        /// number of horses that went unfed (shortfall). Diagnostics name each
        /// shortfall loudly; a silent hungry horse is a bookkeeping lie.
        /// </summary>
        public static int CareDay(
            AnimalRegistry registry,
            IHorseFeedStock feedStock,
            Func<AnimalState, bool> isWorking,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (registry == null)
            {
                diagnostics.Add("HorseCareDriver: no animal registry — horses went unfed.");
                return 0;
            }
            if (feedStock == null)
            {
                diagnostics.Add("HorseCareDriver: no feed stock — horses went unfed.");
                return 0;
            }

            int unfedCount = 0;
            foreach (AnimalState horse in registry.ActiveAnimals)
            {
                if (horse == null || horse.Species != AnimalSpecies.Horse || !horse.IsActive) continue;
                bool working = isWorking != null && isWorking(horse);
                float need = working ? DailyFeedUnitsPerWorkingHorse : DailyFeedUnitsPerHorse;
                int needUnits = Math.Max(1, (int)Math.Ceiling(need));
                int taken = Math.Max(0, feedStock.TakeFeedUnits(needUnits));
                float fed01 = needUnits > 0 ? Math.Min(1f, (float)taken / needUnits) : 0f;
                if (fed01 >= 1f)
                {
                    float water01 = string.IsNullOrWhiteSpace(horse.HousingId) ? 0.5f : 1f;
                    float rest01 = working ? 0.4f : 1f;
                    HorseAuthority.ApplyCare(horse, fed01, water01, rest01, dayIndex);
                }
                else
                {
                    unfedCount++;
                    float shortfall01 = 1f - fed01;
                    horse.Nutrition01 = Math.Max(0f, horse.Nutrition01 - StarvationNutritionLoss01 * shortfall01);
                    horse.Fatigue01 = Math.Min(1f, horse.Fatigue01 + StarvationFatigueGain01 * shortfall01);
                    horse.LastCareDayIndex = dayIndex;
                    diagnostics.Add(
                        $"HorseCareDriver: horse {horse.AnimalId} underfed on day {dayIndex} " +
                        $"(got {taken}/{needUnits} units) — condition degrades, no care applied.");
                }
            }
            return unfedCount;
        }
    }
}
