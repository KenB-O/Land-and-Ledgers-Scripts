using System;
using System.Collections.Generic;

namespace LandLedgers.World.Regional
{
    /// <summary>
    /// Slice 13 (2026-11-05): the regional business registry — what each
    /// Ashford / Redfield business stocks, at what wholesale price, and how
    /// fast it replenishes. Pure authored data; no cash or stock moves here.
    ///
    /// Price rule: every wholesale price is the store asset's
    /// baseWholesaleCostCents where one exists, otherwise ~55–65% of the
    /// retail baseline — always below the retail baseline and inside or
    /// below the asset's local market band. Prices are non-identical
    /// across towns and NEITHER town is cheaper for everything (checked by
    /// test): Ashford is cheaper on salt, sugar, soap, candles, lamp oil,
    /// beef, cheese, cloth, flannel, and nails; Redfield is cheaper on
    /// flour, potatoes, and bacon.
    ///
    /// Stock rule (bounded, auditable — no infinite inventory): StartingUnits
    /// is 2x the store asset's starting stock for the item; weekly production
    /// max is 4x the store's reorder lot (min is half of max); the target
    /// reserve is 2x starting stock. A business that runs out STAYS out until
    /// its weekly production refills it; the buyer then chooses the other town
    /// or waits. Millbrook's rows are NOT re-authored here — its bakery
    /// flour/sugar/yeast/wheat inventory and production live in
    /// MillbrookGrainCo.asset (230 sacks, 40–80/week, reserve 460) and are
    /// preserved exactly. Only Millbrook's NEW store-flour offer is here.
    ///
    /// Coverage (checked by test): Ashford 19/27, Redfield 20/27, union
    /// 26/27. The 27th, bread, comes from the home bakery, not a regional
    /// town. The 10 essentials in both towns are flour, potatoes, salt,
    /// sugar, soap, candles, lamp oil, beef, bacon, and cheese. Coffee is
    /// Ashford-only; tea is Redfield-only. The register below is the
    /// authority; the test pins the exact lists.
    /// </summary>
    public static class RegionalBusinessRegistry
    {
        /// <summary>One item a regional business offers wholesale.</summary>
        public sealed class RegionalOffer
        {
            public string ItemId = string.Empty;
            public string Unit = string.Empty;
            /// <summary>Wholesale price in cents per order unit.</summary>
            public int WholesalePriceCents;
            /// <summary>Units on hand when the region opens. Bounded; see the stock rule above.</summary>
            public int StartingUnits;
            public int WeeklyMinimumUnits;
            public int WeeklyMaximumUnits;
            public int TargetReserveUnits;
        }

        /// <summary>One regional business: identity, town, cash, offers.</summary>
        public sealed class RegionalBusinessDefinition
        {
            public string BusinessId = string.Empty;
            public string DisplayName = string.Empty;
            public string TownId = string.Empty;
            public string BusinessKind = string.Empty;
            public int OpeningCashCents;
            public RegionalOffer[] Offers = Array.Empty<RegionalOffer>();
        }

        private static RegionalOffer Offer(
            string itemId, string unit, int wholesalePriceCents,
            int startingUnits, int weeklyMin, int weeklyMax, int reserve)
        {
            return new RegionalOffer
            {
                ItemId = itemId,
                Unit = unit,
                WholesalePriceCents = wholesalePriceCents,
                StartingUnits = startingUnits,
                WeeklyMinimumUnits = weeklyMin,
                WeeklyMaximumUnits = weeklyMax,
                TargetReserveUnits = reserve,
            };
        }

        // ---- Ashford (19 items) ----

        public static readonly RegionalBusinessDefinition MillbrookGrainCo = new RegionalBusinessDefinition
        {
            BusinessId = "millbrook_grain_co",
            DisplayName = "Millbrook Grain Co.",
            TownId = "ashford",
            BusinessKind = "grain_mill",
            OpeningCashCents = 20000,
            // Bakery rows (flour sacks at 48c, wheat, sugar, yeast) live in
            // MillbrookGrainCo.asset and are NOT duplicated here. This is
            // the NEW store-facing offer: the store's flour SKU (10 lb sack)
            // at the asset's wholesale anchor plus 2c for the 50-mile haul.
            Offers = new[]
            {
                Offer("flour", "10 lb sack", 20, 96, 24, 48, 192),
            },
        };

        public static readonly RegionalBusinessDefinition AshfordMercantile = new RegionalBusinessDefinition
        {
            BusinessId = "ashford_general_mercantile",
            DisplayName = "Ashford General Mercantile",
            TownId = "ashford",
            BusinessKind = "general_store",
            OpeningCashCents = 20000,
            Offers = new[]
            {
                Offer("potatoes", "sack", 13, 60, 20, 40, 120),
                Offer("salt", "small bag", 7, 72, 24, 48, 144),
                Offer("sugar", "lb", 5, 100, 20, 40, 200),
                Offer("coffee", "lb", 17, 80, 20, 40, 160),
                Offer("soap", "bar", 2, 96, 24, 48, 192),
                Offer("candles", "bundle", 4, 72, 24, 48, 144),
                Offer("lamp_oil", "quart", 10, 48, 24, 48, 96),
                Offer("thread", "spool", 3, 60, 20, 40, 120),
            },
        };

        public static readonly RegionalBusinessDefinition AshfordButcher = new RegionalBusinessDefinition
        {
            BusinessId = "ashford_butcher",
            DisplayName = "Ashford Butcher & Provisions",
            TownId = "ashford",
            BusinessKind = "butcher",
            OpeningCashCents = 12000,
            Offers = new[]
            {
                Offer("beef", "lb", 6, 72, 24, 48, 144),
                Offer("bacon", "lb", 10, 64, 16, 32, 128),
                Offer("cheese", "lb", 10, 48, 16, 32, 96),
            },
        };

        public static readonly RegionalBusinessDefinition AshfordDryGoods = new RegionalBusinessDefinition
        {
            BusinessId = "ashford_dry_goods",
            DisplayName = "Ashford Dry Goods & Tailor",
            TownId = "ashford",
            BusinessKind = "dry_goods",
            OpeningCashCents = 15000,
            Offers = new[]
            {
                Offer("basic_cloth", "yard", 8, 80, 20, 40, 160),
                Offer("flannel", "yard", 24, 40, 14, 28, 80),
                Offer("blanket", "each", 85, 20, 8, 16, 40),
                Offer("buttons", "card", 3, 40, 20, 40, 80),
            },
        };

        public static readonly RegionalBusinessDefinition AshfordBlacksmith = new RegionalBusinessDefinition
        {
            BusinessId = "ashford_blacksmith",
            DisplayName = "Ashford Blacksmith",
            TownId = "ashford",
            BusinessKind = "blacksmith",
            OpeningCashCents = 15000,
            Offers = new[]
            {
                Offer("nails_hardware_lot", "lot", 12, 60, 20, 40, 120),
                Offer("hand_tool", "each", 70, 16, 8, 16, 32),
            },
        };

        public static readonly RegionalBusinessDefinition AshfordApothecary = new RegionalBusinessDefinition
        {
            BusinessId = "ashford_apothecary",
            DisplayName = "Ashford Apothecary",
            TownId = "ashford",
            BusinessKind = "apothecary",
            OpeningCashCents = 10000,
            Offers = new[]
            {
                Offer("patent_remedy", "bottle", 35, 24, 8, 16, 48),
            },
        };

        // Ashford livery and sawmill carry no store items: the livery sells
        // freight service and animals (not SKUs); the sawmill sells lumber
        // and offcuts (no retail item id). They are sites, not offer lists.

        // ---- Redfield (19 items) ----

        public static readonly RegionalBusinessDefinition RedfieldGrainFeed = new RegionalBusinessDefinition
        {
            BusinessId = "redfield_grain_feed",
            DisplayName = "Redfield Grain & Feed Co.",
            TownId = "redfield",
            BusinessKind = "grain_mill",
            OpeningCashCents = 20000,
            Offers = new[]
            {
                Offer("flour", "10 lb sack", 18, 96, 24, 48, 192),
                Offer("potatoes", "sack", 11, 60, 20, 40, 120),
            },
        };

        public static readonly RegionalBusinessDefinition RedfieldGeneralStore = new RegionalBusinessDefinition
        {
            BusinessId = "redfield_general_store",
            DisplayName = "Redfield General Store",
            TownId = "redfield",
            BusinessKind = "general_store",
            OpeningCashCents = 20000,
            Offers = new[]
            {
                Offer("salt", "small bag", 8, 72, 24, 48, 144),
                Offer("sugar", "lb", 6, 100, 20, 40, 200),
                Offer("tea", "lb", 42, 24, 12, 24, 48),
                Offer("soap", "bar", 3, 96, 24, 48, 192),
                Offer("candles", "bundle", 5, 72, 24, 48, 144),
                Offer("lamp_oil", "quart", 11, 48, 24, 48, 96),
                Offer("lantern", "each", 70, 12, 4, 8, 24),
            },
        };

        public static readonly RegionalBusinessDefinition RedfieldButcher = new RegionalBusinessDefinition
        {
            BusinessId = "redfield_butcher",
            DisplayName = "Redfield Butcher",
            TownId = "redfield",
            BusinessKind = "butcher",
            OpeningCashCents = 12000,
            Offers = new[]
            {
                Offer("beef", "lb", 7, 72, 24, 48, 144),
                Offer("bacon", "lb", 11, 64, 16, 32, 128),
                Offer("cheese", "lb", 11, 48, 16, 32, 96),
            },
        };

        public static readonly RegionalBusinessDefinition RedfieldIronworks = new RegionalBusinessDefinition
        {
            BusinessId = "redfield_ironworks",
            DisplayName = "Redfield Ironworks & Hardware",
            TownId = "redfield",
            BusinessKind = "ironworks",
            OpeningCashCents = 15000,
            Offers = new[]
            {
                Offer("nails_hardware_lot", "lot", 13, 60, 20, 40, 120),
                Offer("farm_tool", "each", 175, 10, 4, 8, 20),
            },
        };

        public static readonly RegionalBusinessDefinition RedfieldTextiles = new RegionalBusinessDefinition
        {
            BusinessId = "redfield_textiles",
            DisplayName = "Redfield Textile Merchant",
            TownId = "redfield",
            BusinessKind = "textiles",
            OpeningCashCents = 15000,
            Offers = new[]
            {
                Offer("basic_cloth", "yard", 9, 80, 20, 40, 160),
                Offer("flannel", "yard", 26, 40, 14, 28, 80),
                Offer("workwear", "set", 105, 20, 8, 16, 40),
                Offer("boots", "pair", 175, 16, 8, 16, 32),
                Offer("hat", "each", 52, 16, 8, 16, 32),
            },
        };

        public static readonly RegionalBusinessDefinition RedfieldApothecary = new RegionalBusinessDefinition
        {
            BusinessId = "redfield_apothecary",
            DisplayName = "Redfield Apothecary & Sundries",
            TownId = "redfield",
            BusinessKind = "apothecary",
            OpeningCashCents = 10000,
            Offers = new[]
            {
                // 40c, not 52c: at the store's 12-unit reorder target the
                // delivered cost must clear the medicine category's average
                // selling price (62c). At 52c goods the 12-unit delivered
                // cost is 67c and the chain would never order it; at 40c
                // it is 59c and the offer is live. The retail baseline
                // (75c) is unchanged.
                Offer("medicine_bottle", "bottle", 40, 16, 8, 16, 32),
            },
        };

        // Redfield livery and fuel yard carry no store items: the livery
        // sells freight service; the fuel yard sells fuel bundles (a
        // fuel-dealer SKU, not a store item).

        public static readonly RegionalBusinessDefinition[] All =
        {
            MillbrookGrainCo, AshfordMercantile, AshfordButcher, AshfordDryGoods,
            AshfordBlacksmith, AshfordApothecary,
            RedfieldGrainFeed, RedfieldGeneralStore, RedfieldButcher,
            RedfieldIronworks, RedfieldTextiles, RedfieldApothecary,
        };

        /// <summary>Every business in a town.</summary>
        public static List<RegionalBusinessDefinition> BusinessesInTown(string townId)
        {
            var result = new List<RegionalBusinessDefinition>();
            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i].TownId, townId, StringComparison.OrdinalIgnoreCase))
                    result.Add(All[i]);
            return result;
        }

        /// <summary>Distinct item ids offered anywhere in a town.</summary>
        public static HashSet<string> ItemsCoveredInTown(string townId)
        {
            var items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RegionalBusinessDefinition business in BusinessesInTown(townId))
                for (int i = 0; i < business.Offers.Length; i++)
                    items.Add(business.Offers[i].ItemId);
            return items;
        }

        /// <summary>Distinct item ids offered anywhere in the region.</summary>
        public static HashSet<string> ItemsCoveredInRegion()
        {
            var items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < All.Length; i++)
                for (int j = 0; j < All[i].Offers.Length; j++)
                    items.Add(All[i].Offers[j].ItemId);
            return items;
        }

        /// <summary>Businesses offering an item, cheapest wholesale price first.</summary>
        public static List<RegionalBusinessDefinition> SuppliersForItem(string itemId)
        {
            var result = new List<RegionalBusinessDefinition>();
            for (int i = 0; i < All.Length; i++)
            {
                for (int j = 0; j < All[i].Offers.Length; j++)
                {
                    if (string.Equals(All[i].Offers[j].ItemId, itemId, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(All[i]);
                        break;
                    }
                }
            }
            result.Sort((a, b) => WholesalePrice(a, itemId).CompareTo(WholesalePrice(b, itemId)));
            return result;
        }

        public static int WholesalePrice(RegionalBusinessDefinition business, string itemId)
        {
            if (business == null) return int.MaxValue;
            for (int i = 0; i < business.Offers.Length; i++)
                if (string.Equals(business.Offers[i].ItemId, itemId, StringComparison.OrdinalIgnoreCase))
                    return business.Offers[i].WholesalePriceCents;
            return int.MaxValue;
        }
    }
}
