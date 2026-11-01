namespace LandLedgers.Population
{
    /// <summary>
    /// W2B: the nutrition-link seam for prepared meals eaten outside the
    /// household-reserve channel. Canon §2.5: "restaurant/saloon meals or
    /// another real source can satisfy Persons" — a person who ate at the
    /// eating house genuinely ate; their household must not draw (or buy) a
    /// reserve meal for a stomach that is already full, and their nutrition
    /// state must credit the meal.
    ///
    /// The contract lives on the consumer side (DailyNeedsService) and is
    /// implemented by the producer (the restaurant's served-meal ledger, W2B).
    /// Implementations count real served meals for real person ids per day —
    /// never estimates, never aggregates without a diner.
    /// </summary>
    public interface IRestaurantMealDaySource
    {
        /// <summary>
        /// Meals the person (by person id) actually ate at a restaurant /
        /// eating house on the given day. Clamped by the consumer to a sane
        /// per-person daily range; negative or unknown persons return 0.
        /// </summary>
        int MealsEatenAtRestaurant(int personId, int dayIndex);
    }
}
