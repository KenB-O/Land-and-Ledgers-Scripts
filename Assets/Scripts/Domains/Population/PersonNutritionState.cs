using System;
using UnityEngine;

namespace LandLedgers.Population
{
    /// <summary>
    /// NX-1B: per-person nutrition state. Tech X §2.9 TECH-adjacent rule:
    /// "Nutrition/food-adequacy state derives from actual meal/access history
    /// and household reserves rather than a parallel consumable currency."
    /// Missed meals degrade nutrition; sustained undernourishment reduces work
    /// capacity (historically grounded: near-starvation, "family members would
    /// have found it difficult to provide a full day's work" — Humphries et
    /// al., Oxford Economic and Social History). All rates are calibration,
    /// marked as such; the structure (history-derived, no parallel currency)
    /// is the canon point.
    /// </summary>
    [Serializable]
    public sealed class PersonNutritionState
    {
        /// <summary>1.0 = fully nourished; 0 = starving. Derived from meal history.</summary>
        [Range(0f, 1f)]
        public float nutrition01 = 1f;

        public int consecutiveUndernourishedDays;
        public int mealsMissedTrailing7Days;
        public int lastUpdateDayIndex = -1;

        // Calibration (not canon): one missed meal costs 0.18; a fully-fed day
        // recovers 0.30. An adult missing all meals hits 0.25 capacity in ~4 days.
        public const float NutritionCostPerMissedMeal = 0.18f;
        public const float NutritionRecoveryPerFedDay = 0.30f;

        public PersonNutritionState() { }

        /// <summary>Applies one day's meals. Returns true when undernourished.</summary>
        public bool ApplyDay(int mealsEaten, int mealsNeeded, int dayIndex)
        {
            lastUpdateDayIndex = dayIndex;
            int missed = Math.Max(0, mealsNeeded - Math.Max(0, mealsEaten));
            if (missed <= 0)
            {
                nutrition01 = Mathf.Min(1f, nutrition01 + NutritionRecoveryPerFedDay);
                consecutiveUndernourishedDays = 0;
                return false;
            }

            nutrition01 = Mathf.Max(0f, nutrition01 - NutritionCostPerMissedMeal * missed);
            consecutiveUndernourishedDays++;
            mealsMissedTrailing7Days += missed;
            return true;
        }

        /// <summary>
        /// Work capacity multiplier from nutrition. Calibration bands,
        /// historically grounded in the starvation→labor link.
        /// </summary>
        public float WorkCapacityMultiplier =>
            nutrition01 >= 0.70f ? 1.00f :
            nutrition01 >= 0.40f ? 0.75f :
            nutrition01 >= 0.15f ? 0.50f :
                                   0.25f;

        public bool IsUndernourished => nutrition01 < 0.70f;

        /// <summary>
        /// P2: deep copy for save capture (the owning class keeps its own
        /// save-DTO method). Nutrition is plain value state — no sharing.
        /// </summary>
        public PersonNutritionState Clone()
        {
            return new PersonNutritionState
            {
                nutrition01 = nutrition01,
                consecutiveUndernourishedDays = consecutiveUndernourishedDays,
                mealsMissedTrailing7Days = mealsMissedTrailing7Days,
                lastUpdateDayIndex = lastUpdateDayIndex,
            };
        }
    }
}
