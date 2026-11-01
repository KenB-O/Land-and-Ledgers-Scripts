using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.FirstLedger;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class GeneralStoreItemOfferIdentityTests
    {
        [Test]
        public void HouseholdTradingPort_DoesNotExposeAggregatedMultiSkuCategoriesAsSingleItems()
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

                foreach (GeneralStoreCategoryOffer offer in port.CategoryOffers)
                {
                    Assert.IsNotNull(offer);
                    Assert.AreEqual(1, offer.OfferedItemIds.Count,
                        $"Category '{offer.CategoryId}' must resolve to exactly one item.");

                    string resolvedItemId = string.Empty;
                    int matchingItems = 0;
                    foreach (ItemDefinition item in definition.Items)
                    {
                        if (item != null && string.Equals(item.CategoryId, offer.CategoryId,
                                System.StringComparison.OrdinalIgnoreCase))
                        {
                            resolvedItemId = item.ItemId;
                            matchingItems++;
                        }
                    }

                    Assert.AreEqual(1, matchingItems,
                        $"Category '{offer.CategoryId}' aggregates multiple authored SKUs and cannot safely back an item offer.");
                    Assert.AreEqual(resolvedItemId, offer.OfferedItemIds[0],
                        $"Category '{offer.CategoryId}' must expose its actual authored SKU.");
                }

                Assert.IsFalse(HasOffer(port, "staple_food"),
                    "The authored staple_food category pools flour, bread, potatoes, salt, sugar, coffee and tea; its aggregate cannot be sold as flour.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static bool HasOffer(GeneralStoreTradingPort port, string categoryId)
        {
            foreach (GeneralStoreCategoryOffer offer in port.CategoryOffers)
            {
                if (offer != null && string.Equals(offer.CategoryId, categoryId,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
