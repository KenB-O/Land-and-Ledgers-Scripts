// Slice 10 (2026-11-05): explicit retail package definitions for the
// General Store. When the store sells a good in a different unit than
// households request, the offer is convertible only when an entry here
// declares the package size as a NUMBER with its basis. The authored unit
// label (for example "10 lb sack") is never parsed for its number: the
// label is display text, and the number lives here, visibly, so Kennedy
// can see it and correct it. When no entry exists, the item stays
// unoffered and the reason names the missing definition, instead of the
// old bare "unit does not match" drop.
//
// Only the SUBDIVISION shape is convertible here: the store unit is smaller
// than the household unit, so a whole number of store units makes one
// household unit (lamp oil: 4 quarts = 1 gallon). The existing offer field
// StoreUnitsPerItemUnit (int, >= 1) expresses exactly that shape, and the
// category and item-level port math already applies it.
//
// The PACKAGE shape (store unit bigger than the household unit) is
// convertible only through TryGetHouseholdUnitsPerStoreUnit below: buying
// needs whole packages, and the receipt must multiply packages into
// household units. Flour converts that way (1 sack bought = 10 lb
// received, W5); potatoes and salt have no authored package weight
// anywhere (D-5 open), so they stay dropped with the reason naming the
// missing D-5 weight. Nothing here invents a sack weight: the potato sack
// weight and the salt small-bag weight are unauthored anywhere, so they
// have no entry either.
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

            if (id == "potatoes")
            {
                reason = "potatoes are sold by the sack and no D-5 sack weight is authored anywhere, " +
                    "so no conversion is invented and the offer stays dropped.";
                return false;
            }

            if (id == "salt")
            {
                reason = "salt is sold in small bags and no D-5 bag weight is authored anywhere, " +
                    "so no conversion is invented and the offer stays dropped.";
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
        /// <paramref name="reason"/> naming the missing D-5 weight when the
        /// package size is unauthored (potatoes, salt) — loud, not silent.
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

            if (id == "potatoes")
            {
                reason = "potatoes are sold by the sack and no D-5 sack weight is authored anywhere, " +
                    "so no whole-package purchase is possible and the offer stays dropped.";
                return false;
            }

            if (id == "salt")
            {
                reason = "salt is sold in small bags and no D-5 bag weight is authored anywhere, " +
                    "so no whole-package purchase is possible and the offer stays dropped.";
                return false;
            }

            reason = $"no package definition for '{itemId.Trim()}' sold as '{store}' against household '{household}': " +
                "the package size is not authored, so no conversion is invented and the offer stays dropped.";
            return false;
        }
    }
}
