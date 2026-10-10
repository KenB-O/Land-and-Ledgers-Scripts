using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Phase B (Real People): transitional bridge between the legacy
    /// category-unit reserves (HouseholdReserveState) and the real lot
    /// inventory (HouseholdInventory).
    ///
    /// Direction of truth: LOTS are the consumption truth. Reserve category
    /// units are a legacy planning mirror until Phase C replaces the
    /// category-based shopping loop with item-level purchasing. The bridge:
    ///   - seeds lots once from existing reserve units (deterministic,
    ///     provenance-labeled; staple composition honestly recorded as
    ///     unspecified rather than invented),
    ///   - mirrors reserve credits (shopping) into lots,
    ///   - mirrors lot consumption (meals) back into reserves so legacy
    ///     readouts do not double-count food.
    ///
    /// The category-to-item mapping below is a DOCUMENTED TRANSITIONAL
    /// APPROXIMATION for the Phase B bridge — not canon, not calibration.
    /// Phase C removes it with item-level purchasing.
    /// </summary>
    public static class HouseholdInventoryReserveBridge
    {
        private static readonly Dictionary<string, string> CategoryToItemId =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "staple_food", HouseholdItemCatalog.FlourId },
                { "meat", HouseholdItemCatalog.PreservedMeatId },
                { "fuel_wood", HouseholdItemCatalog.FuelWoodId },
                { "household_goods", HouseholdItemCatalog.SoapId },
                { "clothing", HouseholdItemCatalog.ClothingTextilesId },
            };

        /// <summary>
        /// One-time deterministic conversion of a household's existing
        /// reserve units into lots. Idempotent via SeededFromReserves.
        /// Staple food books as the unspecified migration item (composition
        /// honestly unknown); meat books as preserved meat (stored meat in
        /// this period is preserved — documented assumption); fuel wood maps
        /// directly. Mixed non-food categories are left unseeded; their
        /// legacy flow continues untouched until Phase C.
        /// </summary>
        public static void SeedLotsFromReserves(
            HouseholdState household,
            HouseholdInventory inventory,
            int dayIndex,
            List<string> diagnostics)
        {
            if (household == null || inventory == null)
            {
                return;
            }

            if (inventory.State.SeededFromReserves)
            {
                return;
            }

            household.EnsureHouseholdReservesInitialized();
            SeedCategory(household, inventory, "staple_food",
                HouseholdItemCatalog.UnspecifiedStapleFoodId,
                "legacy reserve migration (composition unspecified)", dayIndex, diagnostics);
            SeedCategory(household, inventory, "meat",
                HouseholdItemCatalog.PreservedMeatId,
                "legacy reserve migration (stored meat recorded as preserved)", dayIndex, diagnostics);
            SeedCategory(household, inventory, "fuel_wood",
                HouseholdItemCatalog.FuelWoodId,
                "legacy reserve migration", dayIndex, diagnostics);

            inventory.State.SeededFromReserves = true;
        }

        /// <summary>
        /// Mirrors a reserve credit (e.g. General Store shopping) into a lot
        /// with purchase provenance. Categories without an item mapping
        /// (medicine, tools) credit the reserve only — no lot is invented.
        /// </summary>
        public static void MirrorReserveCreditToLots(
            HouseholdInventory inventory,
            string categoryId,
            int units,
            int dayIndex,
            string sourceLabel,
            List<string> diagnostics)
        {
            if (inventory == null || units <= 0 || string.IsNullOrWhiteSpace(categoryId))
            {
                return;
            }

            if (!CategoryToItemId.TryGetValue(categoryId, out string itemId))
            {
                return;
            }

            string rejection = inventory.AddLot(
                itemId, units, dayIndex,
                string.IsNullOrWhiteSpace(sourceLabel) ? $"reserve credit ({categoryId})" : sourceLabel);
            if (rejection != null)
            {
                diagnostics?.Add($"Reserve bridge: {rejection}");
            }
        }

        /// <summary>
        /// Mirrors lot consumption back into the mapped reserve category so
        /// legacy reserve readouts stay consistent with the lot truth.
        /// </summary>
        public static void MirrorLotConsumptionToReserve(
            HouseholdState household,
            string itemId,
            int units)
        {
            if (household == null || units <= 0)
            {
                return;
            }

            HouseholdItemDefinition definition = HouseholdItemCatalog.Get(itemId);
            if (definition == null || string.IsNullOrWhiteSpace(definition.ReserveCategoryId))
            {
                return;
            }

            HouseholdReserveState reserve = household.GetReserve(definition.ReserveCategoryId);
            if (reserve != null)
            {
                reserve.currentUnits = Math.Max(0, reserve.currentUnits - units);
            }
        }

        private static void SeedCategory(
            HouseholdState household,
            HouseholdInventory inventory,
            string categoryId,
            string itemId,
            string sourceLabel,
            int dayIndex,
            List<string> diagnostics)
        {
            HouseholdReserveState reserve = household.GetReserve(categoryId);
            int units = reserve != null ? Math.Max(0, reserve.currentUnits) : 0;
            if (units <= 0)
            {
                return;
            }

            string rejection = inventory.AddLot(itemId, units, dayIndex, sourceLabel);
            if (rejection != null)
            {
                diagnostics?.Add($"Reserve bridge seed: {rejection}");
            }
        }
    }
}
