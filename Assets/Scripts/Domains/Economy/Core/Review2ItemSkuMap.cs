// Review 2 Pass C (scripts): item / SKU translation between the identity
// spaces the second read-only review verified in the published tree.
// This is a MAP, not a new catalog, and it does NOT rename any id and
// does NOT merge any price. Prices stay per-space: a store flour sack
// (35c) and a bakery profile flour sack (58c) are different SKUs with
// different prices even though both translate to the same bakery code
// item; translating an id never translates a price.
//
// Spaces, with their sources (all verified in the Pass B / review-2
// evidence, not invented here):
//   Household  - HouseholdItemCatalog (Domains/Population/HouseholdInventory.cs):
//                flour "flour" (unit "lb" in the catalog entry, sold as a
//                store sack in the definition - see the note on entry 1),
//                bread "bread" (loaf), fuel_wood "fuel_wood" (bundle).
//   Store      - GeneralStoreBusinessDefinition.asset / GeneralStoreProfile.asset
//                (identical after stripping script/name/class, Pass B):
//                flour (10 lb sack), bread (loaf), and the other 25 items.
//   BakeryCode - BakeryBreadCatalog (Domains/Economy/Businesses/Bakery/
//                BakeryBreadCatalog.cs): bakery-flour (lb), bakery-notions,
//                bake.bread-loaf (loaf), bake.rolls, bake.pie,
//                bakery-fuelwood (cordwood units).
//   BakeryProfile - BakeryProfile.asset items: bakery_flour_sack (sack,
//                58c), bakery_fuel_bundle (bundle, 28c), bread_batch
//                (batch, 36c).
//   FuelProfile - FuelDealerProfile.asset items: fuel_wood_bundle
//                (bundle, 28c), fuel_offcut_bundle (bundle, 22c).
//   RestaurantCode - RestaurantMealCatalog (Domains/Economy/Businesses/
//                Restaurant/RestaurantMealCatalog.cs): restaurant-bread
//                (loaves, comment: "from the bakery").
//
// Conversion rule, stated plainly and enforced by the API: a packaging
// translation (sack -> lb, batch -> loaf, bundle -> bakery fuel unit)
// NEVER supplies its own factor. The caller supplies the factor as a
// parameter, and a missing or non-positive factor is a refusal, never
// a silent 1. That is the established Pass 6 / Pass 7 / Pass 8 pattern
// (LbPerStoreUnit, FuelUnitsPerHouseholdUnit, IntakeUnitsPerBakeryUnit
// are all caller parameters), and it is what keeps this map from
// canonising a conversion Kennedy has not decided: the 10 lb sack
// description, the Pass 8 factor 2, and the Pass 7 1=1 default are all
// test parameters, never canon, and none of them is a default here.
// Direct translations (loaf -> loaf) need no factor: 1:1 is identity
// of the unit, not a decided rate.
//
// Deliberately UNMAPPED (a translation failure with a diagnostic, not
// a guessed entry):
//   - bakery-notions: no store or profile equivalent was found. Notions
//     supply is via Series2NotionsSupply procurement, not a store item.
//     Inventing a store notions id would fake a seller that does not exist.
//   - bake.rolls / bake.pie: the store offers only a bread loaf, not
//     rolls or pies, and no profile item names them.
//   - fuel_offcut_bundle -> bakery-fuelwood: offcuts are a different
//     profile item (22c) from fuel_wood_bundle (28c). Whether offcuts
//     can fire an oven is a Kennedy/Codex decision, not a mapping.
//   - Any price: this map translates ids and units only. A caller that
//     needs a price reads it from the target space's own schedule.
//
// Existing adapters are NOT rewired by this pass. The Pass 7 bakery
// adapter already maps household "bread" to "bake.bread-loaf" inside
// itself, and the Pass 7 fuel adapter already maps household
// "fuel_wood" to dealer intake units with its own caller parameter.
// Those adapters keep working exactly as pinned by Pass A. This map
// gives the NEXT binding pass one place to look the translation up
// instead of a fourth adapter-local mapping, and Codex must wire it
// explicitly - it is not auto-applied to any sale, stock, or lot.
// Plain C#, no Unity dependency, Roslyn-testable.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy
{
    /// <summary>An identity space an item id belongs to. New in Pass C; not serialized anywhere.</summary>
    public enum Review2ItemSpace
    {
        Household = 0,
        Store = 1,
        BakeryCode = 2,
        BakeryProfile = 3,
        FuelProfile = 4,
        RestaurantCode = 5,
    }

    /// <summary>
    /// How a translation converts units. Identity and Direct need no
    /// factor; Packaging requires a caller-supplied factor (see
    /// <see cref="Review2ConversionParameters"/>).
    /// </summary>
    public enum Review2ConversionKind
    {
        Identity = 0,
        Direct = 1,
        Packaging = 2,
    }

    /// <summary>One directed translation entry. All fields are data, never a price.</summary>
    public sealed class Review2ItemTranslation
    {
        public Review2ItemSpace FromSpace;
        public string FromId;
        public string FromUnit;
        public Review2ItemSpace ToSpace;
        public string ToId;
        public string ToUnit;
        public Review2ConversionKind Kind;
        /// <summary>Which caller parameter supplies the factor, or empty for Identity/Direct.</summary>
        public string FactorName;
        /// <summary>Evidence for this entry: file / adapter / asset, never "assumed".</summary>
        public string Basis;
    }

    /// <summary>
    /// Caller-supplied packaging factors. Every field defaults to 0,
    /// which means "not supplied" and is refused by
    /// <see cref="Review2ItemSkuMap.TryConvertUnits"/> - there is
    /// deliberately no factor default of 1 for a packaging entry.
    /// </summary>
    public struct Review2ConversionParameters
    {
        /// <summary>lb of bakery flour per store flour sack.</summary>
        public int LbPerStoreFlourSack;
        /// <summary>lb of bakery flour per bakery-profile flour sack.</summary>
        public int LbPerBakeryFlourSack;
        /// <summary>Loaves per bakery-profile bread batch.</summary>
        public int LoavesPerBreadBatch;
        /// <summary>Bakery fuel units per bakery-profile fuel bundle.</summary>
        public int BakeryFuelUnitsPerBakeryFuelBundle;
        /// <summary>Bakery fuel units per fuel-dealer fuel_wood bundle.</summary>
        public int BakeryFuelUnitsPerFuelWoodBundle;
    }

    /// <summary>Review 2 Pass C item/SKU map. Pure translation; no stock, cash, or sale moves here.</summary>
    public static class Review2ItemSkuMap
    {
        private static readonly Review2ItemTranslation[] Translations =
        {
            // 1. Store flour sack -> bakery flour (lb). The store definition
            //    says a flour unit is a "10 lb sack"; the bakery code item
            //    is lb. The description is NOT the factor: the caller
            //    supplies LbPerStoreFlourSack (Pass 6 LbPerStoreUnit pattern).
            new Review2ItemTranslation
            {
                FromSpace = Review2ItemSpace.Store, FromId = "flour", FromUnit = "sack",
                ToSpace = Review2ItemSpace.BakeryCode, ToId = "bakery-flour", ToUnit = "lb",
                Kind = Review2ConversionKind.Packaging, FactorName = "LbPerStoreFlourSack",
                Basis = "GeneralStoreBusinessDefinition.asset flour unit '10 lb sack'; BakeryBreadCatalog.FlourItemId 'bakery-flour' (lb); Pass 6 caller-parameter conversion.",
            },
            // 2. Reverse of 1: bakery flour (lb) -> store flour sack, with a
            //    remainder in lb when the lb total is not a whole number of sacks.
            new Review2ItemTranslation
            {
                FromSpace = Review2ItemSpace.BakeryCode, FromId = "bakery-flour", FromUnit = "lb",
                ToSpace = Review2ItemSpace.Store, ToId = "flour", ToUnit = "sack",
                Kind = Review2ConversionKind.Packaging, FactorName = "LbPerStoreFlourSack",
                Basis = "Reverse of entry 1; same caller factor, division with remainder, never rounded up.",
            },
            // 3. Bakery-profile flour sack -> bakery flour (lb). A different
            //    SKU from the store sack (58c vs 35c, different asset); the
            //    price is NOT carried across, and the factor is its own
            //    caller parameter, not the store sack's factor.
            new Review2ItemTranslation
            {
                FromSpace = Review2ItemSpace.BakeryProfile, FromId = "bakery_flour_sack", FromUnit = "sack",
                ToSpace = Review2ItemSpace.BakeryCode, ToId = "bakery-flour", ToUnit = "lb",
                Kind = Review2ConversionKind.Packaging, FactorName = "LbPerBakeryFlourSack",
                Basis = "BakeryProfile.asset bakery_flour_sack (sack, 58c); BakeryBreadCatalog.FlourItemId (lb).",
            },
            // 4. Store bread loaf -> bakery bread loaf. Loaf to loaf, 1:1.
            //    Basis is the existing Pass 7 equivalence: the bakery
            //    adapter sells household 'bread' from 'bake.bread-loaf'
            //    shelf lots, and the store item id IS the household id
            //    ('bread'). This does NOT claim every store loaf was
            //    baked by this bakery; it claims the ids name the same
            //    household-consumable loaf unit, which is what the
            //    adapter already relies on.
            new Review2ItemTranslation
            {
                FromSpace = Review2ItemSpace.Store, FromId = "bread", FromUnit = "loaf",
                ToSpace = Review2ItemSpace.BakeryCode, ToId = "bake.bread-loaf", ToUnit = "loaf",
                Kind = Review2ConversionKind.Direct, FactorName = string.Empty,
                Basis = "Series2BakerySupplierAdapter: household 'bread' sold from BakeryBreadCatalog.BreadLoafId shelf lots; store item id equals HouseholdItemCatalog.BreadId.",
            },
            // 5. Reverse of 4.
            new Review2ItemTranslation
            {
                FromSpace = Review2ItemSpace.BakeryCode, FromId = "bake.bread-loaf", FromUnit = "loaf",
                ToSpace = Review2ItemSpace.Store, ToId = "bread", ToUnit = "loaf",
                Kind = Review2ConversionKind.Direct, FactorName = string.Empty,
                Basis = "Reverse of entry 4.",
            },
            // 6. Bakery bread loaf -> restaurant bread loaf. Loaf to loaf.
            new Review2ItemTranslation
            {
                FromSpace = Review2ItemSpace.BakeryCode, FromId = "bake.bread-loaf", FromUnit = "loaf",
                ToSpace = Review2ItemSpace.RestaurantCode, ToId = "restaurant-bread", ToUnit = "loaf",
                Kind = Review2ConversionKind.Direct, FactorName = string.Empty,
                Basis = "RestaurantMealCatalog.BreadItemId 'restaurant-bread' (loaves, from the bakery).",
            },
            // 7. Household bread IS store bread: the ids are the same
            //    string, so this is an identity entry, recorded here so
            //    a caller can ask the map instead of assuming it.
            new Review2ItemTranslation
            {
                FromSpace = Review2ItemSpace.Household, FromId = "bread", FromUnit = "loaf",
                ToSpace = Review2ItemSpace.Store, ToId = "bread", ToUnit = "loaf",
                Kind = Review2ConversionKind.Identity, FactorName = string.Empty,
                Basis = "HouseholdItemCatalog.BreadId equals the GeneralStore definition/profile bread item id.",
            },
            // 8. Household flour IS store flour by id, but NOT by unit:
            //    the household catalog entry names the unit 'lb' while
            //    the store definition sells a '10 lb sack' under the
            //    same id. The id identity is recorded, and the unit
            //    conversion is Packaging with the same caller factor
            //    as entry 1 (lb -> sack, divide with remainder). An
            //    Identity entry here would silently turn lb counts
            //    into sack counts 1:1, which is exactly the fudge
            //    Pass 6 refused to hide.
            new Review2ItemTranslation
            {
                FromSpace = Review2ItemSpace.Household, FromId = "flour", FromUnit = "lb",
                ToSpace = Review2ItemSpace.Store, ToId = "flour", ToUnit = "sack",
                Kind = Review2ConversionKind.Packaging, FactorName = "LbPerStoreFlourSack",
                Basis = "HouseholdItemCatalog.FlourId equals the GeneralStore definition/profile flour item id; units differ (lb vs sack), so the entry 1 factor applies.",
            },
            // 9. Household fuel_wood (bundle) -> fuel-dealer fuel_wood_bundle.
            //    Bundle to bundle. This is NOT the dealer intake conversion:
            //    Pass 7's FuelUnitsPerHouseholdUnit maps dealer intake
            //    units to household units, a different conversion that
            //    this map does not touch.
            new Review2ItemTranslation
            {
                FromSpace = Review2ItemSpace.Household, FromId = "fuel_wood", FromUnit = "bundle",
                ToSpace = Review2ItemSpace.FuelProfile, ToId = "fuel_wood_bundle", ToUnit = "bundle",
                Kind = Review2ConversionKind.Direct, FactorName = string.Empty,
                Basis = "HouseholdItemCatalog.FuelWoodId 'fuel_wood' (bundle); FuelDealerProfile.asset fuel_wood_bundle (bundle, 28c).",
            },
            // 10. Bakery-profile fuel bundle -> bakery fuel units. The 28c
            //     price convergence with fuel_wood_bundle is noted in
            //     Pass B as evidence the bundles intend the same good,
            //     but it is NOT used as a factor: the caller supplies
            //     BakeryFuelUnitsPerBakeryFuelBundle.
            new Review2ItemTranslation
            {
                FromSpace = Review2ItemSpace.BakeryProfile, FromId = "bakery_fuel_bundle", FromUnit = "bundle",
                ToSpace = Review2ItemSpace.BakeryCode, ToId = "bakery-fuelwood", ToUnit = "unit",
                Kind = Review2ConversionKind.Packaging, FactorName = "BakeryFuelUnitsPerBakeryFuelBundle",
                Basis = "BakeryProfile.asset bakery_fuel_bundle (bundle, 28c); BakeryBreadCatalog.FuelItemId 'bakery-fuelwood'.",
            },
            // 11. Fuel-dealer fuel_wood bundle -> bakery fuel units, with
            //     its OWN caller factor, not the bakery-profile bundle's
            //     factor. The two bundles are different SKUs (different
            //     assets); merging their factors would assume they are
            //     the same packaging, which is not proven.
            new Review2ItemTranslation
            {
                FromSpace = Review2ItemSpace.FuelProfile, FromId = "fuel_wood_bundle", FromUnit = "bundle",
                ToSpace = Review2ItemSpace.BakeryCode, ToId = "bakery-fuelwood", ToUnit = "unit",
                Kind = Review2ConversionKind.Packaging, FactorName = "BakeryFuelUnitsPerFuelWoodBundle",
                Basis = "FuelDealerProfile.asset fuel_wood_bundle (bundle, 28c); BakeryBreadCatalog.FuelItemId 'bakery-fuelwood'; factor is caller-supplied, not Pass 7's intake default.",
            },
            // 12. Bakery-profile bread batch -> bakery bread loaf. A batch
            //     is NOT a loaf: the caller supplies LoavesPerBreadBatch.
            //     (The existing wheat-loaf recipe yields 24 loaves per
            //     batch, but that recipe value is the caller's evidence
            //     to pass in, not a default of this map.)
            new Review2ItemTranslation
            {
                FromSpace = Review2ItemSpace.BakeryProfile, FromId = "bread_batch", FromUnit = "batch",
                ToSpace = Review2ItemSpace.BakeryCode, ToId = "bake.bread-loaf", ToUnit = "loaf",
                Kind = Review2ConversionKind.Packaging, FactorName = "LoavesPerBreadBatch",
                Basis = "BakeryProfile.asset bread_batch (batch, 36c); BakeryBreadCatalog.BreadLoafId (loaf); BakeryBreadCatalog.BreadYieldPerBatch is the recipe evidence a caller passes as the factor.",
            },
        };

        /// <summary>All directed translations in the map, in entry order. Read-only view; callers must not mutate entries.</summary>
        public static IReadOnlyList<Review2ItemTranslation> AllTranslations => Translations;

        /// <summary>
        /// Looks a directed translation up. Returns null with a diagnostic
        /// when no entry exists - including the deliberately unmapped ids
        /// (bakery-notions, bake.rolls, bake.pie, fuel_offcut_bundle as a
        /// bakery fuel) - never a guessed translation.
        /// </summary>
        public static Review2ItemTranslation TryTranslate(
            Review2ItemSpace fromSpace, string fromId,
            Review2ItemSpace toSpace, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (string.IsNullOrWhiteSpace(fromId))
            {
                diag.Add("Review2ItemSkuMap: a source item id is required; nothing translated.");
                return null;
            }

            for (int i = 0; i < Translations.Length; i++)
            {
                Review2ItemTranslation entry = Translations[i];
                if (entry.FromSpace == fromSpace
                    && string.Equals(entry.FromId, fromId, StringComparison.OrdinalIgnoreCase)
                    && entry.ToSpace == toSpace)
                {
                    return entry;
                }
            }

            diag.Add($"Review2ItemSkuMap: no translation from {fromSpace} '{fromId}' to {toSpace} " +
                "(deliberately unmapped ids include bakery-notions, bake.rolls, bake.pie, and " +
                "fuel_offcut_bundle as bakery fuel; inventing one would fake a seller or a fuel source).");
            return null;
        }

        /// <summary>
        /// Converts a quantity across a translation. Identity/Direct:
        /// converted = source, remainder = 0. Packaging forward (the
        /// translation's FromUnit is the packaged unit, e.g. sack/batch/
        /// bundle): converted = source * factor, remainder = 0.
        /// Packaging reverse (lb -> sack): converted = source / factor,
        /// remainder = source % factor, never rounded up. A missing or
        /// non-positive factor, a negative quantity, or a null
        /// translation is a refusal (converted = 0, remainder = source
        /// units left unconverted), never a silent 1.
        /// </summary>
        public static bool TryConvertUnits(
            Review2ItemTranslation translation, int sourceUnits,
            Review2ConversionParameters parameters,
            out int convertedUnits, out int remainderUnits, List<string> diag)
        {
            diag = diag ?? new List<string>();
            convertedUnits = 0;
            remainderUnits = Math.Max(0, sourceUnits);
            if (translation == null)
            {
                diag.Add("Review2ItemSkuMap: no translation supplied; nothing converted.");
                return false;
            }

            if (sourceUnits < 0)
            {
                diag.Add($"Review2ItemSkuMap: negative quantity ({sourceUnits}) for '{translation.FromId}' refused; nothing converted.");
                return false;
            }

            if (translation.Kind == Review2ConversionKind.Identity
                || translation.Kind == Review2ConversionKind.Direct)
            {
                convertedUnits = sourceUnits;
                remainderUnits = 0;
                return true;
            }

            int factor = FactorFor(translation, parameters);
            if (factor <= 0)
            {
                diag.Add($"Review2ItemSkuMap: packaging translation '{translation.FromId}' ({translation.FromUnit}) -> " +
                    $"'{translation.ToId}' ({translation.ToUnit}) requires caller factor '{translation.FactorName}' > 0; " +
                    "none was supplied, so nothing was converted and no factor was assumed.");
                return false;
            }

            // Forward vs reverse is decided by unit, not by space: a
            // packaged unit (sack/batch/bundle) converts by multiply;
            // a bulk unit (lb) converts back by divide with remainder.
            // The map's entries are written so FromUnit tells the truth.
            if (string.Equals(translation.FromUnit, "lb", StringComparison.OrdinalIgnoreCase))
            {
                convertedUnits = sourceUnits / factor;
                remainderUnits = sourceUnits % factor;
            }
            else
            {
                convertedUnits = sourceUnits * factor;
                remainderUnits = 0;
            }

            return true;
        }

        private static int FactorFor(Review2ItemTranslation translation, Review2ConversionParameters parameters)
        {
            switch (translation.FactorName)
            {
                case "LbPerStoreFlourSack": return parameters.LbPerStoreFlourSack;
                case "LbPerBakeryFlourSack": return parameters.LbPerBakeryFlourSack;
                case "LoavesPerBreadBatch": return parameters.LoavesPerBreadBatch;
                case "BakeryFuelUnitsPerBakeryFuelBundle": return parameters.BakeryFuelUnitsPerBakeryFuelBundle;
                case "BakeryFuelUnitsPerFuelWoodBundle": return parameters.BakeryFuelUnitsPerFuelWoodBundle;
                default: return 0;
            }
        }
    }
}
