using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// B6 (Agent B, expanded campaign 2026-11-16): fixture vs movable
    /// classification for household furnishings — the contract Agent D's
    /// property-contents slices (D-6/D-7/D-8) consume, and Agent F's
    /// fixture/structure valuation split (F6) reads.
    ///
    /// Rule (D plan §4.1, documented for the owner to correct): fixtures
    /// are items annexed to the building by the method and purpose of
    /// annexation, under historically applicable law; movables are personal
    /// property that happens to be inside. The default that follows from
    /// it (D plan §7 Q2, still open): fixtures pass with the realty unless
    /// the contract explicitly reserves them; movables are excluded unless
    /// explicitly included. The contract, not the visual appearance of the
    /// building, determines the transfer.
    ///
    /// Defaults adopted here (owner-correctable, one kind = one answer —
    /// a kind cannot be fixture in one house and movable in another):
    ///  - Fixture: the cooking stove (fixed flue/hearth connection, iron,
    ///    annexed by weight and connection — the D plan's own example is
    ///    "a stove may stay while a bed goes"), the kitchen counter
    ///    (built-in work surface), the shelf (wall-mounted fixed shelving;
    ///    B5's display policy treats shelves as fixed display).
    ///  - Movable: bed, table, chair, cabinet-small, cabinet-large,
    ///    bedroll, crate, barrel — freestanding personal property. Both
    ///    cabinet kinds are movable: the tracked cabinet prefabs are
    ///    freestanding furniture, and the wall-mounted cabinet prefab is
    ///    local-only, not one of the first-wave kinds.
    /// Unknown or empty kinds classify Unspecified — never guessed. A
    /// contents selection refuses to include an unclassified item (see
    /// PropertyContentsSelection), so an unknown kind is a loud gap,
    /// not a silent default.
    /// </summary>
    public enum FurnishingFixtureKind
    {
        Unspecified = 0,
        /// <summary>Annexed to the building; passes with the realty unless reserved.</summary>
        Fixture = 1,
        /// <summary>Personal property inside the building; excluded unless included.</summary>
        Movable = 2,
    }

    /// <summary>
    /// B6: the fixture/movable classification. Pure function over the
    /// asset kind string — no register, no prices, no production caller
    /// needed. The 8 first-wave carpentry kinds (F0) plus the stove,
    /// kitchen counter and barrel kinds the B slices use.
    /// </summary>
    public static class FurnishingClassification
    {
        public const string BedKind = "furniture.bed";
        public const string TableKind = "furniture.table";
        public const string ChairKind = "furniture.chair";
        public const string CabinetSmallKind = "furniture.cabinet-small";
        public const string CabinetLargeKind = "furniture.cabinet-large";
        public const string ShelfKind = "furniture.shelf";
        public const string CrateKind = "furniture.crate";
        public const string BedrollKind = "furniture.bedroll";
        public const string CookingStoveKind = "furniture.cooking-stove";
        public const string KitchenCounterKind = "furniture.kitchen-counter";
        public const string BarrelKind = "furniture.barrel";

        /// <summary>
        /// The 8 first-wave carpentry kinds (F0 item specs), in spec order.
        /// </summary>
        public static readonly string[] FirstWaveKinds =
        {
            BedKind, TableKind, ChairKind, CabinetSmallKind, CabinetLargeKind,
            ShelfKind, CrateKind, BedrollKind,
        };

        /// <summary>
        /// Classifies one asset kind. Unknown or empty kinds return
        /// Unspecified — the caller must refuse them loudly, never guess.
        /// </summary>
        public static FurnishingFixtureKind Classify(string assetKind)
        {
            if (string.IsNullOrWhiteSpace(assetKind)) return FurnishingFixtureKind.Unspecified;
            switch (assetKind)
            {
                case CookingStoveKind:
                case KitchenCounterKind:
                case ShelfKind:
                    return FurnishingFixtureKind.Fixture;
                case BedKind:
                case TableKind:
                case ChairKind:
                case CabinetSmallKind:
                case CabinetLargeKind:
                case BedrollKind:
                case CrateKind:
                case BarrelKind:
                    return FurnishingFixtureKind.Movable;
                default:
                    return FurnishingFixtureKind.Unspecified;
            }
        }

        /// <summary>True when the kind has a classification (fixture or movable).</summary>
        public static bool IsKnownKind(string assetKind)
        {
            return Classify(assetKind) != FurnishingFixtureKind.Unspecified;
        }
    }
}
