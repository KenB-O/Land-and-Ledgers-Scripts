using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.FirstLedger;
using LandLedgers.Population;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Series 2 Pass 7 update: the old version of this test asserted that
    /// staple_food is NEVER offered, because the old trading port only
    /// offered a category when exactly one item resolved in it, and
    /// selling the 7-item staple_food aggregate as "flour" would have
    /// been a false product identity. Pass 7 replaces that guard with
    /// per-item offers (Series2TradingPortBuilder): staple_food IS now
    /// offered, but as seven separate one-item offers (flour, bread,
    /// potatoes, salt, sugar, coffee, tea), each drawing from the
    /// shared category pool — never as one aggregate labelled flour.
    /// The safety invariant this test protects is therefore restated,
    /// not removed:
    ///   - every offer carries exactly one item id;
    ///   - every offered item is an authored item in its category AND
    ///     a real household item (HouseholdItemCatalog);
    ///   - no offer sells a multi-item category under a representative
    ///     id (no "preserved_meat" for beef/bacon/cheese, no
    ///     "clothing_textiles" for cloth/workwear/etc.);
    ///   - single-item categories keep the legacy behaviour exactly
    ///     (one offer, the authored item, matching the old mapping).
    /// Total sales across items in one category are capped by the
    /// shared category pool; that conservation is proven in the
    /// Roslyn Pass 7 tests (Series2Pass7SellingTests), not here,
    /// because this test runs against the live manager port.
    /// </summary>
    public sealed class GeneralStoreItemOfferIdentityTests
    {
        [Test]
        public void HouseholdTradingPort_OffersRealItemsPerItem_NeverAnAggregateAsOneItem()
        {
            GeneralStoreBusinessDefinition definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(
                "Assets/Core/Economy/GeneralStoreBusinessDefinition.asset");
            Assert.IsNotNull(definition, "The authored General Store profile is required for this integration test.");

            GameObject host = new GameObject("GeneralStoreItemOfferIdentityTests");
            try
            {
                GeneralStoreRuntimeManager manager = host.AddComponent<GeneralStoreRuntimeManager>();
                FieldInfo definitionField = typeof(GeneralStoreRuntimeManager).GetField(
                    "storeDefinition", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(definitionField, "The manager's authored store-profile binding changed.");
                definitionField.SetValue(manager, definition);

                GeneralStoreTradingPort port = manager.BuildHouseholdTradingPort();
                Assert.IsNotNull(port);
                Assert.Greater(port.CategoryOffers.Count, 0, "The port must offer at least the staple_food items; an empty port is the old bug, not a pass.");

                var offeredByCategory = new Dictionary<string, List<string>>();
                foreach (GeneralStoreCategoryOffer offer in port.CategoryOffers)
                {
                    Assert.IsNotNull(offer);
                    Assert.AreEqual(1, offer.OfferedItemIds.Count,
                        $"Category '{offer.CategoryId}' must resolve to exactly one item per offer (per-item offers, never an aggregate).");

                    string itemId = offer.OfferedItemIds[0];
                    Assert.IsNotNull(HouseholdItemCatalog.Get(itemId),
                        $"Offered item '{itemId}' in '{offer.CategoryId}' must be a real household item.");

                    bool authoredInCategory = false;
                    foreach (ItemDefinition item in definition.Items)
                    {
                        if (item != null && string.Equals(item.CategoryId, offer.CategoryId,
                                System.StringComparison.OrdinalIgnoreCase)
                            && string.Equals(item.ItemId, itemId, System.StringComparison.OrdinalIgnoreCase))
                        {
                            authoredInCategory = true;
                            break;
                        }
                    }
                    Assert.IsTrue(authoredInCategory,
                        $"Offered item '{itemId}' must be an authored item in category '{offer.CategoryId}', not a representative id.");

                    if (!offeredByCategory.TryGetValue(offer.CategoryId, out List<string> items))
                    {
                        items = new List<string>();
                        offeredByCategory[offer.CategoryId] = items;
                    }
                    items.Add(itemId);
                }

                // Staple_food is now offered per-item, not skipped, and
                // not as flour alone: all seven authored household items.
                Assert.IsTrue(offeredByCategory.TryGetValue("staple_food", out List<string> staple),
                    "staple_food must now be offered per-item (Pass 7 fix for the shopping-loop block).");
                foreach (string expected in new[] { "flour", "bread", "potatoes", "salt", "sugar", "coffee", "tea" })
                {
                    Assert.Contains(expected, staple,
                        $"staple_food must offer '{expected}' as its own item.");
                }

                // No representative aggregates: meat and clothing stay
                // un-offered through this path, because their authored
                // items are not household items.
                Assert.IsFalse(offeredByCategory.ContainsKey("meat"),
                    "meat must not be offered as preserved_meat — beef/bacon/cheese are not household items.");
                Assert.IsFalse(offeredByCategory.ContainsKey("clothing"),
                    "clothing must not be offered as clothing_textiles — cloth/workwear/etc. are not household items.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
