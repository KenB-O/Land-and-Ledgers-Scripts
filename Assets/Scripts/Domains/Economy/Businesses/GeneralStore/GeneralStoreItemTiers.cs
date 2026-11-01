using System.Collections.Generic;
using LandLedgers.Population;

namespace LandLedgers.Economy.Businesses.GeneralStore
{
    /// <summary>
    /// Pass XL phase 3, item 4: tiered General Store inventory.
    ///
    /// Priority 1 = what the vertical slice needs to work: food staples
    /// for two meals a day, heating fuel, and basic household goods.
    /// Most Priority 1 SKUs already existed in HouseholdItemCatalog;
    /// sugar, coffee, and tea were in the authored staple_food category
    /// pool (see OffMapNamedSkuProcurementService) but NOT in the
    /// household catalog, so HouseholdInventory.AddLot rejected them.
    /// Adding them here closes that gap; it does not grant stock.
    ///
    /// Secondary = house and household goods a household needs to run
    /// a home: light, cooking, bedding, tools, tableware.
    /// Tertiary = luxury goods and the rest of a plausible store:
    /// tobacco, spirits, books, jewelry, perfume, chocolate, silk.
    /// Tertiary items are definitions only. No price is set here:
    /// HouseholdItemCatalog carries no price, and retail prices live in
    /// the GeneralStoreBusinessDefinition ScriptableObject asset, which
    /// is outside this repo. Codex must add ItemDefinitions with these
    /// same ids to that asset; a tier id with no retail definition
    /// cannot be sold from category stock, and must not be faked by
    /// labelling category stock as that item (the single-SKU guard).
    ///
    /// Tier lists are disjoint and append-only: new items are added,
    /// never renamed, because HouseholdInventory lots and saves key on
    /// these stable string ids.
    /// </summary>
    public static class GeneralStoreItemTiers
    {
        public static readonly string[] Priority1 =
        {
            HouseholdItemCatalog.FlourId,      // flour
            HouseholdItemCatalog.BreadId,      // bread
            HouseholdItemCatalog.PotatoesId,   // potatoes
            HouseholdItemCatalog.PreservedFoodId,
            HouseholdItemCatalog.PreservedMeatId,
            HouseholdItemCatalog.FuelWoodId,   // fuel_wood, heating
            HouseholdItemCatalog.SoapId,
            HouseholdItemCatalog.SaltId,
            "sugar",
            "coffee",
            "tea",
        };

        public static readonly string[] Secondary =
        {
            "candle",
            "lamp_oil",
            "cookware",
            "bedding",
            "tool_hand",
            "tableware",
            HouseholdItemCatalog.ClothingTextilesId, // clothing_textiles
        };

        public static readonly string[] Tertiary =
        {
            "cigarettes",
            "tobacco",
            "whiskey",
            "book",
            "jewelry",
            "perfume",
            "chocolate",
            "silk_cloth",
        };

        /// <summary>Every tier id must exist in HouseholdItemCatalog; returns missing ids (empty = complete).</summary>
        public static List<string> MissingCatalogIds()
        {
            var missing = new List<string>();
            foreach (string id in Priority1) if (HouseholdItemCatalog.Get(id) == null) missing.Add(id);
            foreach (string id in Secondary) if (HouseholdItemCatalog.Get(id) == null) missing.Add(id);
            foreach (string id in Tertiary) if (HouseholdItemCatalog.Get(id) == null) missing.Add(id);
            return missing;
        }

        public static bool TiersAreDisjoint()
        {
            var seen = new HashSet<string>();
            foreach (string id in Priority1) if (!seen.Add(id)) return false;
            foreach (string id in Secondary) if (!seen.Add(id)) return false;
            foreach (string id in Tertiary) if (!seen.Add(id)) return false;
            return true;
        }
    }
}
