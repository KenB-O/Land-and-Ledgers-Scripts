// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Agent F, slice F2 (expanded campaign 2026-11-16): furniture retail tests.
// Pins: the eight first-wave furniture ids map to the dedicated "furniture"
// retail category (O-F4, plan's proposed default) in the code-side category
// table; the F2 store SKUs price coherently through the existing cost-plus
// mechanism (neutral price inside the authored band, above landed cost and
// above the plan section 4.3 manufacture cost; scarcity raises, surplus
// lowers and clamps to the band floor); the furniture category holds exactly
// the eight ids with the authored markup and restock targets; the new
// Furniture demand group is 6 and the existing groups are unchanged.
//
// The store asset (GeneralStoreBusinessDefinition.asset) is the authority
// for the SKUs and the category. The builder below reproduces its F2 values
// in code because Resources.Load returns null in the Roslyn harness (no
// asset library there) — the Review2PassAGeneralTests precedent. If the
// asset changes, update this builder in the same change. Unity loads the
// real asset; this builder is harness-only scaffolding. The asset values
// were verified against the plan by a static YAML check at implementation
// time; Codex confirms the asset loads in Unity.
//
// No behaviour change to pricing: every test here asserts the existing
// cost-plus mechanism (RetailPricingDefinition.GetCostPlusPriceCents with
// the category markup and DEF-013 pressure) at furniture scale.

using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Storage;
using LandLedgers.World.Regional;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class FurnitureRetailF2Tests
    {
        private const string FurnitureCategoryId = "furniture";

        // The furniture category's authored default markup (asset).
        // GetItemSellingPriceCents passes this through as the category
        // markup multiplier; the item markup is 1 for every furniture SKU.
        private const float FurnitureCategoryMarkup = 1.4f;

        // The F2 authored SKUs: id, display name, baseline, band floor,
        // band ceiling, starting stock, reorder target, reorder lot size,
        // plan section 4.3 manufacture cost (input costs at live prices +
        // labour at the negotiated wage; the chair cost is per chair, the
        // recipe makes a batch of four). Baselines and bands are the plan
        // section 4.3 proposed numbers; stock targets are the plan section
        // 7.2 proposed scale (slow durables 2/4/1, cheaper faster movers
        // 4/6/2). Sensitivity is 0.25 for all eight, markup 1, no authored
        // wholesale cost (derived 70% first-pass; the carpenter-to-store
        // price is not authored yet — G procurement wiring).
        private static readonly (string, string, int, int, int, int, int, int, int)[] Expected =
        {
            ("furniture.bed", "Bed", 300, 250, 450, 2, 4, 1, 250),
            ("furniture.table", "Table", 225, 175, 325, 2, 4, 1, 180),
            ("furniture.chair", "Chair", 125, 100, 175, 4, 6, 2, 93),
            ("furniture.cabinet-small", "Small Cabinet", 325, 250, 450, 2, 4, 1, 258),
            ("furniture.cabinet-large", "Large Cabinet", 425, 325, 600, 2, 4, 1, 348),
            ("furniture.shelf", "Shelf", 80, 60, 125, 4, 6, 2, 65),
            ("furniture.crate", "Crate", 75, 50, 125, 4, 6, 2, 59),
            ("furniture.bedroll", "Bedroll", 60, 40, 100, 4, 6, 2, 45),
        };

        private static void SetPrivateField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static RetailPricingDefinition Pricing(int baseline, int bandMin, int bandMax) =>
            new RetailPricingDefinition
            {
                baselineCents = baseline,
                baseWholesaleCostCents = 0,
                importCostCents = 0,
                localMarketBand = new PriceBand { minCents = bandMin, maxCents = bandMax },
                markupMultiplier = 1f,
                supplyDemandSensitivity = 0.25f,
            };

        private static ItemCategoryDefinition FurnitureCategory()
        {
            var category = new ItemCategoryDefinition();
            SetPrivateField(category, "categoryId", FurnitureCategoryId);
            SetPrivateField(category, "displayName", "Furniture");
            SetPrivateField(category, "role", RetailCategoryRole.Core);
            SetPrivateField(category, "demandGroup", RetailDemandGroup.Furniture);
            SetPrivateField(category, "sortOrder", 70);
            SetPrivateField(category, "defaultMarkupMultiplier", FurnitureCategoryMarkup);
            SetPrivateField(category, "lowStockThreshold01", 0.25f);
            SetPrivateField(category, "playerFacingSummary", "Beds, tables, chairs and storage for frontier homes.");
            SetPrivateField(category, "itemIds", FurnitureItemSpecs.AllIds);
            return category;
        }

        private static ItemDefinition Item(
            string itemId, string displayName, int baseline, int bandMin, int bandMax,
            int startingStock, int reorderTarget, int reorderLot)
        {
            var item = new ItemDefinition();
            SetPrivateField(item, "itemId", itemId);
            SetPrivateField(item, "displayName", displayName);
            SetPrivateField(item, "categoryId", FurnitureCategoryId);
            SetPrivateField(item, "unit", "each");
            SetPrivateField(item, "pricing", Pricing(baseline, bandMin, bandMax));
            SetPrivateField(item, "startingStockUnits", startingStock);
            SetPrivateField(item, "reorderTargetUnits", reorderTarget);
            SetPrivateField(item, "reorderLotSize", reorderLot);
            SetPrivateField(item, "coreStock", true);
            SetPrivateField(item, "producerTags", new[] { "carpenter" });
            SetPrivateField(item, "consumerTags", new[] { "household" });
            return item;
        }

        private static List<ItemDefinition> FurnitureItems()
        {
            var items = new List<ItemDefinition>();
            foreach ((string id, string name, int baseline, int bandMin, int bandMax,
                int starting, int target, int lot, int _) in Expected)
            {
                items.Add(Item(id, name, baseline, bandMin, bandMax, starting, target, lot));
            }

            return items;
        }

        private static int NeutralPrice(RetailPricingDefinition pricing) =>
            pricing.GetCostPlusPriceCents(FurnitureCategoryMarkup, 0f);

        [Test]
        public void CategoryForItem_AllEightFurnitureIds_ReturnFurniture()
        {
            Assert.AreEqual(8, FurnitureItemSpecs.AllIds.Length, "F2 retails exactly F0's eight items.");
            foreach (string id in FurnitureItemSpecs.AllIds)
            {
                Assert.AreEqual(FurnitureCategoryId, RegionalItemCategories.CategoryForItem(id),
                    $"Furniture id '{id}' must map to the dedicated furniture category, not household_goods.");
            }
        }

        [Test]
        public void CategoryForItem_ExistingMappings_Unchanged()
        {
            Assert.AreEqual("staple_food", RegionalItemCategories.CategoryForItem("flour"));
            Assert.AreEqual("staple_food", RegionalItemCategories.CategoryForItem("bread"));
            Assert.AreEqual("meat", RegionalItemCategories.CategoryForItem("beef"));
            Assert.AreEqual("clothing", RegionalItemCategories.CategoryForItem("boots"));
            Assert.AreEqual("household_goods", RegionalItemCategories.CategoryForItem("soap"));
            Assert.AreEqual("tools_hardware", RegionalItemCategories.CategoryForItem("nails_hardware_lot"));
            Assert.AreEqual("medicine_remedies", RegionalItemCategories.CategoryForItem("patent_remedy"));
        }

        [Test]
        public void CategoryForItem_UnknownOrEmpty_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, RegionalItemCategories.CategoryForItem("no-such-item"));
            Assert.AreEqual(string.Empty, RegionalItemCategories.CategoryForItem(string.Empty));
            Assert.AreEqual(string.Empty, RegionalItemCategories.CategoryForItem("   "));
            Assert.AreEqual(string.Empty, RegionalItemCategories.CategoryForItem(null));
        }

        [Test]
        public void CategoryForItem_FurnitureIds_AreCaseInsensitive()
        {
            Assert.AreEqual(FurnitureCategoryId, RegionalItemCategories.CategoryForItem("FURNITURE.BED"));
            Assert.AreEqual(FurnitureCategoryId, RegionalItemCategories.CategoryForItem("Furniture.Cabinet-Large"));
        }

        [Test]
        public void DemandGroup_Furniture_IsSixAndExistingGroups_Unchanged()
        {
            Assert.AreEqual(0, (int)RetailDemandGroup.DailyStaple);
            Assert.AreEqual(1, (int)RetailDemandGroup.FoodProtein);
            Assert.AreEqual(2, (int)RetailDemandGroup.Clothing);
            Assert.AreEqual(3, (int)RetailDemandGroup.Household);
            Assert.AreEqual(4, (int)RetailDemandGroup.Hardware);
            Assert.AreEqual(5, (int)RetailDemandGroup.Medicine);
            Assert.AreEqual(6, (int)RetailDemandGroup.Furniture);
        }

        [Test]
        public void FurnitureCategory_ContainsExactlyTheEightFurnitureItems()
        {
            ItemCategoryDefinition category = FurnitureCategory();
            Assert.AreEqual(FurnitureCategoryId, category.CategoryId);
            Assert.AreEqual("Furniture", category.DisplayName);
            Assert.AreEqual(RetailCategoryRole.Core, category.Role);
            Assert.AreEqual(RetailDemandGroup.Furniture, category.DemandGroup);
            Assert.AreEqual(70, category.SortOrder);
            Assert.AreEqual(FurnitureCategoryMarkup, category.DefaultMarkupMultiplier, 0.001f);
            Assert.AreEqual(0.25f, category.LowStockThreshold01, 0.001f);

            var held = new List<string>();
            foreach (string id in category.ItemIds) held.Add(id);
            Assert.AreEqual(FurnitureItemSpecs.AllIds.Length, held.Count);
            foreach (string id in FurnitureItemSpecs.AllIds)
            {
                Assert.IsTrue(held.Contains(id), $"Furniture category must list '{id}'.");
            }

            Assert.IsFalse(category.ContainsItem("soap"),
                "Furniture pressure must never move with soap stock: no household_goods item may sit in the furniture category.");
            Assert.IsTrue(category.ContainsItem("furniture.bed"));
        }

        [Test]
        public void FurnitureItems_AllInFurnitureCategory_WithCarpenterProducer()
        {
            foreach (ItemDefinition item in FurnitureItems())
            {
                Assert.AreEqual(FurnitureCategoryId, item.CategoryId);
                Assert.AreEqual("each", item.Unit);
                var producers = new List<string>();
                foreach (string tag in item.ProducerTags) producers.Add(tag);
                Assert.AreEqual(1, producers.Count);
                Assert.AreEqual("carpenter", producers[0]);
                var consumers = new List<string>();
                foreach (string tag in item.ConsumerTags) consumers.Add(tag);
                Assert.AreEqual(1, consumers.Count);
                Assert.AreEqual("household", consumers[0]);
            }
        }

        [Test]
        public void FurnitureItems_RestockTargets_AreSane()
        {
            foreach (ItemDefinition item in FurnitureItems())
            {
                Assert.Greater(item.StartingStockUnits, 0,
                    $"{item.ItemId}: a retail SKU needs opening stock to sell from.");
                Assert.GreaterOrEqual(item.ReorderTargetUnits, item.StartingStockUnits,
                    $"{item.ItemId}: reorder target must cover starting stock.");
                Assert.GreaterOrEqual(item.ReorderLotSize, 1);
                Assert.LessOrEqual(item.ReorderLotSize, item.ReorderTargetUnits,
                    $"{item.ItemId}: a reorder lot must fit inside the target.");
                Assert.IsTrue(item.CoreStock, $"{item.ItemId}: furniture is a carried line, not flex stock.");
            }
        }

        [Test]
        public void FurniturePricing_NeutralPrice_WithinBandAboveLandedCostAndAboveManufactureCost()
        {
            foreach ((string id, string _, int baseline, int bandMin, int bandMax,
                int _, int _, int _, int manufactureCost) in Expected)
            {
                RetailPricingDefinition pricing = Pricing(baseline, bandMin, bandMax);
                int landed = pricing.GetLandedCostCents();
                int neutral = NeutralPrice(pricing);
                Assert.GreaterOrEqual(neutral, bandMin, $"{id}: neutral price must not fall below the band floor.");
                Assert.LessOrEqual(neutral, bandMax, $"{id}: neutral price must not exceed the band ceiling.");
                Assert.Greater(neutral, landed,
                    $"{id}: the store must earn a margin over landed cost at neutral pressure (landed {landed}c, neutral {neutral}c).");
                Assert.Greater(neutral, manufactureCost,
                    $"{id}: the retail price must clear the plan section 4.3 manufacture cost ({manufactureCost}c), or the carpenter's trade loses money on every sale.");
            }
        }

        [Test]
        public void FurniturePricing_Bed_NeutralPrice_Is294()
        {
            // Exact pin with no rounding ambiguity: landed 70% of 300 is
            // exactly 210; 210 x the 1.4 furniture category markup is
            // exactly 294, inside the 250-450 band.
            RetailPricingDefinition bed = Pricing(300, 250, 450);
            Assert.AreEqual(210, bed.GetLandedCostCents());
            Assert.AreEqual(294, NeutralPrice(bed));
        }

        [Test]
        public void FurniturePricing_ScarcityRaisesSurplusLowersAndClampsToBand()
        {
            RetailPricingDefinition bed = Pricing(300, 250, 450);
            int neutral = NeutralPrice(bed);
            int scarcity = bed.GetCostPlusPriceCents(FurnitureCategoryMarkup, 1f);
            int surplus = bed.GetCostPlusPriceCents(FurnitureCategoryMarkup, -1f);
            Assert.Greater(scarcity, neutral, "Empty furniture stock must lift the bed price (scarcity premium).");
            Assert.LessOrEqual(scarcity, 450, "Scarcity must never push the bed price past the band ceiling.");
            Assert.AreEqual(250, surplus,
                "Full furniture stock must cut the bed price to the band floor, never below it.");
        }

        [Test]
        public void FurniturePricing_SurplusPrice_NeverBelowLandedCost()
        {
            // Even at full surplus (-1 pressure) the store never sells a
            // piece below its derived landed cost: the 1.4 category markup
            // against the 0.75 surplus factor is 1.05x landed, and the band
            // floors sit above landed cost where the factor alone would not
            // cover it (bed: floor 250c vs landed 210c).
            foreach ((string id, string _, int baseline, int bandMin, int bandMax,
                int _, int _, int _, int _) in Expected)
            {
                RetailPricingDefinition pricing = Pricing(baseline, bandMin, bandMax);
                int surplus = pricing.GetCostPlusPriceCents(FurnitureCategoryMarkup, -1f);
                Assert.GreaterOrEqual(surplus, pricing.GetLandedCostCents(),
                    $"{id}: surplus price must never fall below landed cost.");
            }
        }

        [Test]
        public void FurniturePricing_PressureResolution_PointsAndClamps()
        {
            Assert.AreEqual(1f, RetailPricingDefinition.ResolveSupplyDemandPressure01(0f), 0.001f);
            Assert.AreEqual(0f, RetailPricingDefinition.ResolveSupplyDemandPressure01(0.5f), 0.001f);
            Assert.AreEqual(-1f, RetailPricingDefinition.ResolveSupplyDemandPressure01(1f), 0.001f);
            Assert.AreEqual(-1f, RetailPricingDefinition.ResolveSupplyDemandPressure01(2f), 0.001f);
            Assert.AreEqual(1f, RetailPricingDefinition.ResolveSupplyDemandPressure01(-0.5f), 0.001f);
        }
    }
}
