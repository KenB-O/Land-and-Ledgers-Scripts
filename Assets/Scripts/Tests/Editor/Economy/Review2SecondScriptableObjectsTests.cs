// Review 2 second ScriptableObjects pass — tests for the diagnostic-only
// Review2StoreOfferGapCheck. No behaviour is changed by the code under
// test, so these tests assert diagnostics only. All feed ids, categories,
// and prices here are caller-supplied test parameters, never canon.

using System.Collections.Generic;
using LandLedgers.Economy;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class Review2SecondScriptableObjectsTests
    {
        private static Dictionary<string, IReadOnlyCollection<string>> DefinitionItems()
        {
            return new Dictionary<string, IReadOnlyCollection<string>>
            {
                { "staple_food", new[] { "flour", "bread", "potatoes", "salt", "sugar", "coffee", "tea" } },
                { "meat", new[] { "beef", "bacon", "cheese" } },
            };
        }

        private static List<string> DefinitionCategories()
        {
            return new List<string> { "staple_food", "meat", "clothing", "household_goods", "tools_hardware", "medicine_remedies" };
        }

        [Test]
        public void NewCategoryFeedItem_IsGap_NoDefinitionOffer()
        {
            var registration = new Review2StoreFeedItem
            {
                BusinessInstanceId = "store-1",
                FeedCategoryId = "horse_feed",
                FeedItemId = "test_feed_sack",
                StoreUnitsPerFeedUnit = 2,
            };
            List<string> diagnostics = Review2StoreOfferGapCheck.CheckFeedOffer(registration, DefinitionCategories(), DefinitionItems());
            Assert.AreEqual(1, diagnostics.Count);
            Assert.IsTrue(diagnostics[0].Contains("GAP"), diagnostics[0]);
            Assert.IsTrue(diagnostics[0].Contains("horse_feed"), diagnostics[0]);
            Assert.IsTrue(diagnostics[0].Contains("BuildHouseholdTradingPort"), diagnostics[0]);
        }

        [Test]
        public void NewItemInAuthoredCategory_IsGap_NoDefinitionOffer()
        {
            var registration = new Review2StoreFeedItem
            {
                BusinessInstanceId = "store-1",
                FeedCategoryId = "staple_food",
                FeedItemId = "test_feed_sack",
                StoreUnitsPerFeedUnit = 2,
            };
            List<string> diagnostics = Review2StoreOfferGapCheck.CheckFeedOffer(registration, DefinitionCategories(), DefinitionItems());
            Assert.AreEqual(1, diagnostics.Count);
            Assert.IsTrue(diagnostics[0].Contains("GAP"), diagnostics[0]);
            Assert.IsTrue(diagnostics[0].Contains("test_feed_sack"), diagnostics[0]);
        }

        [Test]
        public void AuthoredItemFeed_IsSharedPoolWarning_NotGap()
        {
            var registration = new Review2StoreFeedItem
            {
                BusinessInstanceId = "store-1",
                FeedCategoryId = "staple_food",
                FeedItemId = "flour",
                StoreUnitsPerFeedUnit = 1,
            };
            List<string> diagnostics = Review2StoreOfferGapCheck.CheckFeedOffer(registration, DefinitionCategories(), DefinitionItems());
            Assert.AreEqual(1, diagnostics.Count);
            Assert.IsTrue(diagnostics[0].Contains("SHARED POOL"), diagnostics[0]);
            Assert.IsTrue(diagnostics[0].Contains("single aggregate pool"), diagnostics[0]);
            Assert.IsFalse(diagnostics[0].Contains("GAP"), diagnostics[0]);
        }

        [Test]
        public void NoRegistration_NoDiagnostics()
        {
            List<string> diagnostics = Review2StoreOfferGapCheck.CheckFeedOffer(null, DefinitionCategories(), DefinitionItems());
            Assert.AreEqual(0, diagnostics.Count);
        }

        [Test]
        public void MissingCategoryId_Diagnostic_NotGapClaim()
        {
            var registration = new Review2StoreFeedItem
            {
                BusinessInstanceId = "store-1",
                FeedCategoryId = "",
                FeedItemId = "test_feed_sack",
                StoreUnitsPerFeedUnit = 2,
            };
            List<string> diagnostics = Review2StoreOfferGapCheck.CheckFeedOffer(registration, DefinitionCategories(), DefinitionItems());
            Assert.AreEqual(1, diagnostics.Count);
            Assert.IsTrue(diagnostics[0].Contains("no category id"), diagnostics[0]);
        }
    }
}
