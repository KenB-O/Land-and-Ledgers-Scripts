namespace LandLedgers.Population
{
    /// <summary>
    /// W2C: the nutrition-link seam for board-included meals eaten at a
    /// boarding house. Canon §2.5: "board-included meals ... can satisfy
    /// Persons" — a boarder who ate at the house's table genuinely ate;
    /// their household must not draw (or buy) a reserve meal for a stomach
    /// that is already full, and their nutrition state must credit the meal.
    ///
    /// The contract lives on the consumer side (DailyNeedsService) and is
    /// implemented by the producer (the boarding house's served-meal ledger,
    /// W2C). Implementations count real served meals for real person ids per
    /// day — never estimates, never aggregates without a diner. This is the
    /// exact parallel of <see cref="IRestaurantMealDaySource"/>; the two
    /// sources are consulted independently and summed under the same daily
    /// per-person cap so a boarder who also ate at an eating house is never
    /// double-fed.
    /// </summary>
    public interface IBoardingHouseMealDaySource
    {
        /// <summary>
        /// Meals the person (by person id) actually ate as board-included
        /// meals at a boarding house on the given day. Clamped by the
        /// consumer to a sane per-person daily range; negative or unknown
        /// persons return 0.
        /// </summary>
        int MealsEatenAtBoardingHouse(int personId, int dayIndex);
    }
}
