using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Population;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Plan 11 (2026-11-06) slice M2: pin the live retail bread price and
    /// the store's seeded flour sacks. TESTS ONLY — no production code is
    /// touched, and a failing pin is the finding, never a reason to edit
    /// production code or adjust a fixture.
    ///
    /// Method note, stated plainly: GeneralStoreRuntimeManager is
    /// harness-excluded (not in harness-overnight/sources.txt), so the
    /// live read `GetItemSellingPriceCents("bread")` cannot be called
    /// here. The pins below run the SAME computation the manager
    /// performs, through the harness-included pricing authority
    /// (RetailPricingDefinition.GetCostPlusPriceCents) on fixtures that
    /// mirror the authored definition values exactly (GeneralStoreProfile
    /// .asset), plus the manager's 5c minimum floor as the documented
    /// constant from source (GeneralStoreRuntimeManager.cs:166,
    /// minimumCategoryUnitPriceCents = 5). The flour pin follows the
    /// Plan 02 precedent (BakeryFlourSupplierComparisonTests test 1).
    /// The category-average pin runs the same averaging the manager's
    /// GetAverageCategorySellingPriceCents performs (average of the
    /// category items' cost-plus prices, floored at 5c).
    ///
    /// Seed resolution (4 vs 48): the profile's flour
    /// `startingStockUnits: 48` is in the item's unit ("10 lb sack"),
    /// but BusinessRuntimeState.CreateFrom explicitly keeps catalog
    /// StartingStockUnits out of physical stock (category stock is
    /// seeded 0; only a settled receipt creates inventory) — so the 48
    /// seeds ZERO physical sacks. The live seeded count comes from the
    /// DevelopmentTownConfiguration opening inventory (flour minimum 4,
    /// maximum 8), resolved deterministically per scenario seed by
    /// TryResolveOpeningInventory. Plan 02's "4 seeded sacks" is the
    /// value for the config's seedOffset 9217, i.e. the low end of the
    /// 4–8 range, not a contradiction of the 48.
    /// Unity compilation, Test Runner, PlayMode, and the forwarder call
    /// site are NOT claimed; SimulationDrivers is harness-excluded, so
    /// the forwarder is source-reviewed only.
    /// </summary>
    [TestFixture]
    public sealed class BakeryMarginLiveValuesTests
    {
        private const float StapleFoodMarkup = 1.45f;
        private const int MinimumCategoryUnitPriceCents = 5;

        private static RetailPricingDefinition Pricing(int baseline, int wholesale, int importCost,
            int bandMin, int bandMax, float sensitivity = 0.25f) => new RetailPricingDefinition
        {
            baselineCents = baseline,
            baseWholesaleCostCents = wholesale,
            importCostCents = importCost,
            localMarketBand = new PriceBand { minCents = bandMin, maxCents = bandMax },
            markupMultiplier = 1f,
            supplyDemandSensitivity = sensitivity,
        };

        private static void SetPrivateField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        [Test]
        public void BreadRetailPrice_FiveCents_ViaMinimumFloor()
        {
            // Authored bread definition (GeneralStoreProfile.asset):
            // baseline 5c, wholesale 2c, import 0c, band [4,8], markup 1.
            RetailPricingDefinition bread = Pricing(5, 2, 0, 4, 8);
            Assert.AreEqual(2, bread.GetLandedCostCents(),
                "landed = wholesale 2c + import 0c");
            Assert.AreEqual(3, Mathf.RoundToInt(2 * 1f * StapleFoodMarkup),
                "raw cost-plus before any floor: 2c x 1 x 1.45 = 2.9c -> 3c");
            Assert.AreEqual(4, bread.GetCostPlusPriceCents(StapleFoodMarkup, 0f),
                "the [4,8] band lifts the raw 3c to its 4c floor");
            int itemPrice = Mathf.Max(MinimumCategoryUnitPriceCents,
                bread.GetCostPlusPriceCents(StapleFoodMarkup, 0f));
            Assert.AreEqual(5, itemPrice,
                "the manager's 5c minimum floor binds over the band-clamped 4c: bread sells at 5c");
        }

        [Test]
        public void FlourRetailPrice_RePinned_TwentyNineCents()
        {
            // Authored flour definition (GeneralStoreProfile.asset):
            // baseline 35c, wholesale 18c, import 2c, band [28,45],
            // markup 1; staple_food markup 1.45.
            RetailPricingDefinition flour = Pricing(35, 18, 2, 28, 45, 0.35f);
            Assert.AreEqual(20, flour.GetLandedCostCents(),
                "landed = wholesale 18c + import 2c");
            Assert.AreEqual(29, flour.GetCostPlusPriceCents(StapleFoodMarkup, 0f),
                "cost-plus = 20c x 1 x 1.45 = 29c; the [28,45] band and the 5c floor do not bind");
            int itemPrice = Mathf.Max(MinimumCategoryUnitPriceCents,
                flour.GetCostPlusPriceCents(StapleFoodMarkup, 0f));
            Assert.AreEqual(29, itemPrice, "flour sells at 29c");
        }

        [Test]
        public void StapleFoodCategoryAverage_TwentyTwoCents()
        {
            // The category-average path (GetAverageCategorySellingPriceCents)
            // averages the cost-plus price of every staple_food item in
            // the authored profile, floored at 5c. Values verified against
            // the real pricing code by the M2 probe (deleted after the
            // run): coffee's landed cost is RoundToInt(25 x 0.7) = 18c.
            var items = new Dictionary<string, RetailPricingDefinition>
            {
                { "flour", Pricing(35, 18, 2, 28, 45, 0.35f) },
                { "bread", Pricing(5, 2, 0, 4, 8) },
                { "potatoes", Pricing(25, 12, 1, 18, 35, 0.4f) },
                { "salt", Pricing(10, 0, 0, 8, 15, 0.2f) },
                { "sugar", Pricing(8, 0, 0, 6, 12) },
                { "coffee", Pricing(25, 0, 0, 18, 35, 0.35f) },
                { "tea", Pricing(60, 0, 0, 45, 80, 0.3f) },
            };
            var expected = new Dictionary<string, int>
            {
                { "flour", 29 }, { "bread", 4 }, { "potatoes", 19 }, { "salt", 10 },
                { "sugar", 9 }, { "coffee", 26 }, { "tea", 61 },
            };
            int total = 0;
            foreach (var kv in items)
            {
                int costPlus = kv.Value.GetCostPlusPriceCents(StapleFoodMarkup, 0f);
                Assert.AreEqual(expected[kv.Key], costPlus, $"cost-plus for {kv.Key}");
                total += costPlus;
            }
            Assert.AreEqual(158, total, "sum of the seven staple_food cost-plus prices");
            int average = Mathf.Max(MinimumCategoryUnitPriceCents, total / items.Count);
            Assert.AreEqual(22, average,
                "category average = 158 / 7 = 22c (integer division); the 5c floor does not bind");
        }

        [Test]
        public void SeededFlourSacks_OpeningInventoryRange_NotFortyEight()
        {
            // Fixture mirrors the authored DevelopmentTownConfiguration
            // opening inventory for the general store (flour minimum 4,
            // maximum 8) under the authored configuration id.
            var config = ScriptableObject.CreateInstance<DevelopmentTownConfiguration>();
            SetPrivateField(config, "configurationId", "millbrook-development-town");
            SetPrivateField(config, "openingInventory", new[]
            {
                new DevelopmentTownConfiguration.BusinessOpeningInventory
                {
                    businessKey = "general-store",
                    items = new[]
                    {
                        new DevelopmentTownConfiguration.OpeningInventoryItem
                        { itemId = "flour", minimumUnits = 4, maximumUnits = 8 },
                    },
                },
            });

            // The live seeded count is seed-dependent but always inside
            // the authored 4–8 range, and deterministic per seed.
            foreach (int seed in new[] { 0, 1, 9217, 12345, 99999 })
            {
                Assert.IsTrue(config.TryResolveOpeningInventory("general-store", "flour", seed, out int qty),
                    $"the authored opening inventory resolves for seed {seed}");
                Assert.IsTrue(qty >= 4 && qty <= 8,
                    $"seeded flour for seed {seed} is inside the authored 4–8 range; got {qty}");
            }
            Assert.IsTrue(config.TryResolveOpeningInventory("general-store", "flour", 9217, out int qty9217)
                && config.TryResolveOpeningInventory("general-store", "flour", 9217, out int qty9217Again)
                && qty9217 == qty9217Again,
                "the seeded count is deterministic for a given seed");
            Assert.AreEqual(4, qty9217,
                "seed 9217 (the config's seedOffset) seeds 4 sacks — Plan 02's recorded value, the low end of the range");

            // The profile's 48 is in the item's unit ("10 lb sack") —
            // and it seeds no physical stock at all.
            var flourItem = new ItemDefinition();
            SetPrivateField(flourItem, "itemId", "flour");
            SetPrivateField(flourItem, "categoryId", "staple_food");
            SetPrivateField(flourItem, "unit", "10 lb sack");
            SetPrivateField(flourItem, "startingStockUnits", 48);
            SetPrivateField(flourItem, "reorderTargetUnits", 60);
            SetPrivateField(flourItem, "pricing", Pricing(35, 18, 2, 28, 45, 0.35f));
            Assert.AreEqual(48, flourItem.StartingStockUnits, "the profile carries 48");
            Assert.AreEqual("10 lb sack", flourItem.Unit, "the 48 is in 10-lb sacks");

            var category = new ItemCategoryDefinition();
            SetPrivateField(category, "categoryId", "staple_food");
            SetPrivateField(category, "defaultMarkupMultiplier", StapleFoodMarkup);
            SetPrivateField(category, "itemIds", new[] { "flour", "bread", "potatoes", "salt", "sugar", "coffee", "tea" });
            var business = new BusinessDefinition(BusinessType.GeneralStore, "general-store-1", "General Store");
            SetPrivateField(business, "ownedCategoryIds", new[] { "staple_food" });
            BusinessRuntimeState state = BusinessRuntimeState.CreateFrom(
                business,
                new List<ItemCategoryDefinition> { category },
                new List<ItemDefinition> { flourItem },
                null,
                BusinessThroughputMode.Retail,
                0,
                0);
            Assert.AreEqual(0, state.GetRetailItemStockUnits("flour"),
                "the 48 catalog units create zero physical retail flour lots");
            CategoryStockState stock = state.GetCategoryStock("staple_food");
            Assert.IsNotNull(stock, "the category stock exists");
            Assert.AreEqual(0, stock.CurrentStockUnits,
                "category stock is seeded 0 — StartingStockUnits is not an inventory receipt");
        }
    }
}
