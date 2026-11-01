namespace LandLedgers.Population
{
    /// <summary>
    /// W3B: the nutrition-link seam for dining-room meals eaten at a hotel.
    /// Canon §2.5: hotel board meals "can satisfy Persons" — a guest who ate
    /// in the hotel's dining room genuinely ate; their household must not
    /// draw (or buy) a reserve meal for a stomach that is already full, and
    /// their nutrition state must credit the meal.
    ///
    /// The contract lives on the consumer side (DailyNeedsService) and is
    /// implemented by the producer (the hotel's served-meal ledger, W3B).
    /// Implementations count real served meals for real person ids per day —
    /// never estimates, never aggregates without a diner. This is the exact
    /// parallel of <see cref="IRestaurantMealDaySource"/> (W2B) and
    /// <see cref="IBoardingHouseMealDaySource"/> (W2C); the three sources are
    /// consulted independently and summed under the same daily per-person
    /// cap so a guest who also ate elsewhere is never double-fed.
    /// </summary>
    public interface IHotelMealDaySource
    {
        /// <summary>
        /// Meals the person (by person id) actually ate in a hotel dining
        /// room on the given day. Clamped by the consumer to a sane
        /// per-person daily range; negative or unknown persons return 0.
        /// </summary>
        int MealsEatenAtHotel(int personId, int dayIndex);
    }
}
