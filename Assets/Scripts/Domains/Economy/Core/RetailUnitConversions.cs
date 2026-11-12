// Explicit General Store package definitions. Store package labels are
// display-only; SKU-specific conversion numbers here drive finite purchases
// and receipts. Lamp oil uses four quarts per gallon, flour one 10 lb sack
// is 10 lb, development-town potatoes use a 50 lb sack, and salt a 5 lb
// small bag. Unsupported unit pairs remain unavailable with a clear reason.
using System;

namespace LandLedgers.Economy
{
    public static class RetailUnitConversions
    {
        /// <summary>
        /// Returns true when a household can buy this item from a store that
        /// sells it in <paramref name="storeUnit"/> while the household
        /// requests <paramref name="householdUnit"/>, and sets
        /// <paramref name="storeUnitsPerHouseholdUnit"/> to the whole number
        /// of store units consumed per household unit sold. Returns false
        /// with <paramref name="reason"/> naming the missing package
        /// definition or mechanism when the pair is not convertible.
        /// </summary>
        public static bool TryGetStoreUnitsPerHouseholdUnit(
            string itemId, string storeUnit, string householdUnit,
            out int storeUnitsPerHouseholdUnit, out string reason)
        {
            storeUnitsPerHouseholdUnit = 1;
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                reason = "no item id was supplied, so no package definition can apply.";
                return false;
            }

            string store = (storeUnit ?? string.Empty).Trim();
            string household = (householdUnit ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(store) || string.IsNullOrWhiteSpace(household))
            {
                reason = $"'{itemId.Trim()}' has no authored store unit or no household unit, so no package definition can apply.";
                return false;
            }

            if (string.Equals(store, household, StringComparison.OrdinalIgnoreCase))
                return true;

            string id = itemId.Trim().ToLowerInvariant();
            string storeLower = store.ToLowerInvariant();
            string householdLower = household.ToLowerInvariant();

            // Lamp oil: the store sells quarts, households request gallons.
            // Basis: Nov 5 playtest (store quarts vs household gallons);
            // household unit "gal" per HouseholdItemCatalog; store unit
            // "quart" per GeneralStoreBusinessDefinition.asset (unpublished);
            // 1 gal = 4 qt is a standard volume measure, not game fiction.
            if (id == "lamp_oil" && storeLower == "quart" && householdLower == "gal")
            {
                storeUnitsPerHouseholdUnit = 4;
                return true;
            }

            if (id == "flour")
            {
                reason = "flour is sold as 10 lb sacks, a package bigger than the household lb: " +
                    "the subdivision factor does not apply — use TryGetHouseholdUnitsPerStoreUnit for whole-package purchase " +
                    "(1 sack bought = 10 lb received).";
                return false;
            }

            reason = $"no package definition for '{itemId.Trim()}' sold as '{store}' against household '{household}': " +
                "the package size is not authored, so no conversion is invented and the offer stays dropped.";
            return false;
        }

        /// <summary>
        /// W5 — the PACKAGE direction: returns true when one store unit of
        /// this item is a whole package holding a whole number of household
        /// units, and sets <paramref name="householdUnitsPerStoreUnit"/> to
        /// that count (flour: 1 sack = 10 lb). The shopping loop buys whole
        /// packages (ceil(need / package size)) and multiplies the receipt
        /// (packages sold × package size). Returns false with
        /// <paramref name="reason"/> naming the missing package definition
        /// when no supported SKU-specific conversion exists — loud, not silent.
        /// Identical units resolve to 1: one store unit is one household unit.
        /// </summary>
        public static bool TryGetHouseholdUnitsPerStoreUnit(
            string itemId, string storeUnit, string householdUnit,
            out int householdUnitsPerStoreUnit, out string reason)
        {
            householdUnitsPerStoreUnit = 1;
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                reason = "no item id was supplied, so no package definition can apply.";
                return false;
            }

            string store = (storeUnit ?? string.Empty).Trim();
            string household = (householdUnit ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(store) || string.IsNullOrWhiteSpace(household))
            {
                reason = $"'{itemId.Trim()}' has no authored store unit or no household unit, so no package definition can apply.";
                return false;
            }

            if (string.Equals(store, household, StringComparison.OrdinalIgnoreCase))
                return true;

            string id = itemId.Trim().ToLowerInvariant();
            string storeLower = store.ToLowerInvariant();
            string householdLower = household.ToLowerInvariant();

            // Flour: the store sells 10 lb sacks, households request lb.
            // Basis: the authored store unit "10 lb sack" per
            // GeneralStoreBusinessDefinition.asset; the number 10 lives
            // here, visibly, as the package size — the label is display
            // text and is never parsed for its number.
            if (id == "flour" && storeLower == "10 lb sack" && householdLower == "lb")
            {
                householdUnitsPerStoreUnit = 10;
                return true;
            }

            // Development-town package calibration, explicit by SKU and
            // authored display unit. Inventory remains whole finite packages.
            if (id == "potatoes" && storeLower == "sack" && householdLower == "lb")
            {
                householdUnitsPerStoreUnit = 50;
                return true;
            }

            if (id == "salt" && storeLower == "small bag" && householdLower == "lb")
            {
                householdUnitsPerStoreUnit = 5;
                return true;
            }

            reason = $"no package definition for '{itemId.Trim()}' sold as '{store}' against household '{household}': " +
                "the package size is not authored, so no conversion is invented and the offer stays dropped.";
            return false;
        }
    }
}
