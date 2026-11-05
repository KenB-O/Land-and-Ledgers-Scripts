using System.Collections.Generic;
using LandLedgers.World.Regional;
using NUnit.Framework;

namespace LandLedgers.Tests.World
{
    /// <summary>Slice 13: regional business offers, prices, and coverage.</summary>
    public sealed class RegionalBusinessRegistryTests
    {
        // Retail baselines from GeneralStoreBusinessDefinition.asset. The test
        // pins wholesale < retail for every offer; the asset is the authority.
        private static readonly Dictionary<string, int> RetailBaselineCents = new Dictionary<string, int>
        {
            { "flour", 35 }, { "bread", 5 }, { "potatoes", 25 }, { "salt", 10 },
            { "sugar", 8 }, { "coffee", 25 }, { "tea", 60 }, { "beef", 12 },
            { "bacon", 15 }, { "cheese", 15 }, { "basic_cloth", 12 }, { "flannel", 35 },
            { "workwear", 150 }, { "boots", 250 }, { "blanket", 125 }, { "hat", 75 },
            { "soap", 5 }, { "candles", 10 }, { "lamp_oil", 20 }, { "lantern", 100 },
            { "thread", 5 }, { "buttons", 5 }, { "nails_hardware_lot", 25 },
            { "hand_tool", 100 }, { "farm_tool", 250 }, { "patent_remedy", 50 },
            { "medicine_bottle", 75 },
        };

        private static readonly string[] EssentialsInBothTowns =
        {
            "flour", "potatoes", "salt", "sugar", "soap", "candles", "lamp_oil",
            "beef", "bacon", "cheese",
        };

        [Test]
        public void Coverage_AshfordNineteen_RedfieldTwenty_UnionTwentySix()
        {
            HashSet<string> ashford = RegionalBusinessRegistry.ItemsCoveredInTown("ashford");
            HashSet<string> redfield = RegionalBusinessRegistry.ItemsCoveredInTown("redfield");
            HashSet<string> union = RegionalBusinessRegistry.ItemsCoveredInRegion();
            Assert.AreEqual(19, ashford.Count, "Ashford must cover 19 of 27 store items (~70%).");
            Assert.AreEqual(20, redfield.Count, "Redfield must cover 20 of 27 store items (~74%).");
            Assert.AreEqual(26, union.Count, "The two towns together must cover 26 of 27 store items.");
            Assert.IsTrue(!union.Contains("bread"),
                "Bread is supplied by the home bakery, not a regional town.");
            Assert.AreEqual(27, RetailBaselineCents.Count, "Test table must list all 27 store items.");
            foreach (string item in RetailBaselineCents.Keys)
                if (item != "bread") Assert.IsTrue(union.Contains(item), $"No regional source for {item}.");
        }

        [Test]
        public void Essentials_AreOfferedInBothTowns()
        {
            HashSet<string> ashford = RegionalBusinessRegistry.ItemsCoveredInTown("ashford");
            HashSet<string> redfield = RegionalBusinessRegistry.ItemsCoveredInTown("redfield");
            foreach (string item in EssentialsInBothTowns)
            {
                Assert.IsTrue(ashford.Contains(item), $"Ashford must carry essential {item}.");
                Assert.IsTrue(redfield.Contains(item), $"Redfield must carry essential {item}.");
            }
        }

        [Test]
        public void FlourAndNails_HaveCompetingSuppliers()
        {
            List<RegionalBusinessRegistry.RegionalBusinessDefinition> flour =
                RegionalBusinessRegistry.SuppliersForItem("flour");
            Assert.AreEqual(2, flour.Count, "Flour needs two competing suppliers.");
            Assert.AreEqual("redfield_grain_feed", flour[0].BusinessId, "Redfield must be the cheaper flour source.");
            Assert.AreEqual("millbrook_grain_co", flour[1].BusinessId);

            List<RegionalBusinessRegistry.RegionalBusinessDefinition> nails =
                RegionalBusinessRegistry.SuppliersForItem("nails_hardware_lot");
            Assert.AreEqual(2, nails.Count, "Hardware lots need two competing suppliers.");
        }

        [Test]
        public void Prices_ArePositive_AndBelowRetailBaseline_AndMixedAcrossTowns()
        {
            int ashfordCheaper = 0;
            int redfieldCheaper = 0;
            foreach (RegionalBusinessRegistry.RegionalBusinessDefinition business in RegionalBusinessRegistry.All)
            {
                Assert.Greater(business.OpeningCashCents, 0, $"{business.BusinessId} needs opening cash.");
                Assert.Greater(business.Offers.Length, 0, $"{business.BusinessId} needs at least one offer.");
                var seen = new HashSet<string>();
                foreach (RegionalBusinessRegistry.RegionalOffer offer in business.Offers)
                {
                    Assert.IsTrue(seen.Add(offer.ItemId), $"{business.BusinessId} offers {offer.ItemId} twice.");
                    Assert.Greater(offer.WholesalePriceCents, 0, $"{offer.ItemId} needs a positive price.");
                    Assert.IsTrue(RetailBaselineCents.TryGetValue(offer.ItemId, out int retail),
                        $"Unknown item {offer.ItemId} — is it a real store item?");
                    Assert.Less(offer.WholesalePriceCents, retail,
                        $"{business.BusinessId} charges {offer.WholesalePriceCents}c for {offer.ItemId}, above the {retail}c retail baseline.");
                    Assert.IsTrue(!string.IsNullOrWhiteSpace(offer.Unit), $"{offer.ItemId} needs a unit.");
                }
            }

            HashSet<string> ashford = RegionalBusinessRegistry.ItemsCoveredInTown("ashford");
            HashSet<string> redfield = RegionalBusinessRegistry.ItemsCoveredInTown("redfield");
            foreach (string item in ashford)
            {
                if (!redfield.Contains(item)) continue;
                int ashfordPrice = PriceInTown("ashford", item);
                int redfieldPrice = PriceInTown("redfield", item);
                Assert.AreNotEqual(ashfordPrice, redfieldPrice, $"Prices for {item} must differ between towns.");
                if (ashfordPrice < redfieldPrice) ashfordCheaper++;
                else redfieldCheaper++;
            }
            Assert.Greater(ashfordCheaper, 0, "Ashford must be cheaper for something.");
            Assert.Greater(redfieldCheaper, 0, "Redfield must be cheaper for something.");
        }

        [Test]
        public void Stock_IsBounded_WithProductionRows()
        {
            foreach (RegionalBusinessRegistry.RegionalBusinessDefinition business in RegionalBusinessRegistry.All)
            {
                foreach (RegionalBusinessRegistry.RegionalOffer offer in business.Offers)
                {
                    Assert.Greater(offer.StartingUnits, 0, $"{offer.ItemId} needs starting stock.");
                    Assert.GreaterOrEqual(offer.WeeklyMinimumUnits, 0);
                    Assert.Greater(offer.WeeklyMaximumUnits, offer.WeeklyMinimumUnits,
                        $"{offer.ItemId} weekly max must exceed its min.");
                    Assert.GreaterOrEqual(offer.TargetReserveUnits, offer.StartingUnits,
                        $"{offer.ItemId} reserve must cover starting stock.");
                }
            }
        }

        [Test]
        public void Millbrook_PreservesItsAssetRows_AndAddsOnlyAStoreFlourOffer()
        {
            // The registry must not duplicate Millbrook's asset inventory
            // (bakery flour sacks, wheat, sugar, yeast). Only the new
            // store-facing flour offer belongs here.
            Assert.AreEqual(1, RegionalBusinessRegistry.MillbrookGrainCo.Offers.Length);
            Assert.AreEqual("flour", RegionalBusinessRegistry.MillbrookGrainCo.Offers[0].ItemId);
            Assert.AreEqual("10 lb sack", RegionalBusinessRegistry.MillbrookGrainCo.Offers[0].Unit);
            Assert.AreEqual(20, RegionalBusinessRegistry.MillbrookGrainCo.Offers[0].WholesalePriceCents);
        }

        [Test]
        public void EverySiteBusiness_HasARegistryEntry_AndViceVersa()
        {
            // Slice 12 sites that carry store items must appear here; the
            // livery, sawmill, and fuel yard legitimately carry none.
            var registryIds = new HashSet<string>();
            foreach (RegionalBusinessRegistry.RegionalBusinessDefinition business in RegionalBusinessRegistry.All)
                registryIds.Add(business.BusinessId);
            foreach (RegionalTownRegistry.RegionalTownDefinition town in RegionalTownRegistry.All)
            {
                foreach (RegionalTownRegistry.RegionalBusinessSite site in town.Sites)
                {
                    bool carriesStoreItems = site.BusinessKind != "livery_freight"
                        && site.BusinessKind != "sawmill" && site.BusinessKind != "fuel_yard";
                    Assert.AreEqual(carriesStoreItems, registryIds.Contains(site.BusinessId),
                        $"Site {site.BusinessId} (kind {site.BusinessKind}) registry mismatch.");
                }
            }
        }

        private static int PriceInTown(string townId, string itemId)
        {
            foreach (RegionalBusinessRegistry.RegionalBusinessDefinition business in
                RegionalBusinessRegistry.BusinessesInTown(townId))
            {
                int price = RegionalBusinessRegistry.WholesalePrice(business, itemId);
                if (price != int.MaxValue) return price;
            }
            return int.MaxValue;
        }
    }
}
