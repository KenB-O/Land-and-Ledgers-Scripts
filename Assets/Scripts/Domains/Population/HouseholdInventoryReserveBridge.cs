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
    /// Category-only legacy purchases do not identify their contained SKU.
    /// This bridge records those credits as explicit unspecified category
    /// lots instead of relabeling pooled stock as flour, preserved meat, soap,
    /// or clothing textiles. Exact item shopping writes exact SKU lots in the
    /// Phase C path.
    /// </summary>
    public static class HouseholdInventoryReserveBridge
    {
        private static readonly Dictionary<string, string> CategoryToItemId =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "staple_food", HouseholdItemCatalog.UnspecifiedStapleFoodId },
                { "meat", HouseholdItemCatalog.UnspecifiedMeatId },
                { "fuel_wood", HouseholdItemCatalog.FuelWoodId },
                { "household_goods", HouseholdItemCatalog.UnspecifiedHouseholdGoodsId },
                { "clothing", HouseholdItemCatalog.UnspecifiedClothingId },
            };

        /// <summary>
        /// One-time deterministic conversion of a household's existing
        /// reserve units into lots. Idempotent via SeededFromReserves.
        /// Categories without SKU composition use an unspecified category
        /// item; fuel wood retains its exact identity.
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

            household.EnsureHouseholdReservesInitialized(
                migrateLegacyFoodReserve: household.reserves == null || household.reserves.Count == 0);
            SeedCategory(household, inventory, "staple_food",
                HouseholdItemCatalog.UnspecifiedStapleFoodId,
                "legacy reserve migration (composition unspecified)", dayIndex, diagnostics);
            SeedCategory(household, inventory, "meat",
                HouseholdItemCatalog.UnspecifiedMeatId,
                "legacy reserve migration (composition unspecified)", dayIndex, diagnostics);
            SeedCategory(household, inventory, "fuel_wood",
                HouseholdItemCatalog.FuelWoodId,
                "legacy reserve migration", dayIndex, diagnostics);

            inventory.State.SeededFromReserves = true;
        }

        /// <summary>
        /// Mirrors a reserve credit (e.g. General Store shopping) into an
        /// exact SKU lot only for single-identity categories, otherwise an
        /// explicitly unspecified category lot. Categories without a
        /// supported category identity (medicine, tools) credit the reserve
        /// only — no lot is invented.
        ///
        /// PHASE C: RETIRED from the shopping path. The Phase C shopping loop
        /// (HouseholdShoppingLoop) resolves real items through the store's own
        /// declared offers (IItemResolvingSupplier) and books lots directly —
        /// this bridge only records exact SKU identity when the category has
        /// one unambiguous item; pooled categories remain explicitly
        /// unspecified. Kept for the legacy HF-4 EmbodiedPurchaseExecutor
        /// path until DailyNeedsService migrates.
        /// </summary>
        [Obsolete("Legacy category-credit bridge. New retail should resolve declared item offers through IItemResolvingSupplier.")]
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
