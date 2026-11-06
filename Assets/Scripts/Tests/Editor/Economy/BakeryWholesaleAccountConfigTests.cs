using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Equipment;
using LandLedgers.Population;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Plan 01 (2026-11-06) slice 1A: scenario-authored bakery wholesale
    /// accounts — config schema, applicator, and the demand-derived
    /// fallback. Pins that accounts are authorable per seller key, zero
    /// by default, idempotent via the endowment marker plus defensive
    /// dedup, validated before any mutation, and that the fallback books
    /// exactly one 24-unit daily 2c account only when the config lists no
    /// wholesale accounts at all. Unity compilation, Test Runner,
    /// PlayMode, and the bootstrapper call site are NOT claimed; the
    /// bootstrapper is harness-excluded, so its call is source-reviewed
    /// only and exercised here through the same public API it calls.
    /// </summary>
    [TestFixture]
    public sealed class BakeryWholesaleAccountConfigTests
    {
        private const string BakeryKey = "bakery";
        private const string StoreKey = "general-store";
        private const string BakeryInstanceId = "test-bakery-1";
        private const string StoreInstanceId = "test-store-1";

        private static void SetPrivateField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static DevelopmentTownConfiguration BuildConfig(
            DevelopmentTownConfiguration.BakeryWholesaleAccountDefinition[] wholesaleAccounts)
        {
            var config = ScriptableObject.CreateInstance<DevelopmentTownConfiguration>();
            SetPrivateField(config, "configurationId", "test-town");
            SetPrivateField(config, "seedOffset", 0);
            SetPrivateField(config, "people", new[]
            {
                new DevelopmentTownConfiguration.PersonDefinition
                { key = "person-0", firstName = "Test", lastName = "Baker", age = 30, homeBuildingOrdinal = 0 },
            });
            SetPrivateField(config, "households", new[]
            {
                new DevelopmentTownConfiguration.HouseholdDefinition
                {
                    key = "household-0", surname = "Baker", displayName = "Baker Household",
                    memberPersonKeys = new[] { "person-0" }, homeBuildingOrdinal = 0, openingCashCents = 0,
                },
            });
            SetPrivateField(config, "relationships", Array.Empty<DevelopmentTownConfiguration.RelationshipDefinition>());
            SetPrivateField(config, "businesses", new[]
            {
                new DevelopmentTownConfiguration.BusinessDefinition
                {
                    key = BakeryKey, operationId = "bakery", ownerPersonKey = "person-0",
                    businessId = BakeryInstanceId, premisesRequired = false,
                },
                new DevelopmentTownConfiguration.BusinessDefinition
                {
                    key = StoreKey, operationId = "general-store", ownerPersonKey = "person-0",
                    businessId = StoreInstanceId, premisesRequired = false,
                },
            });
            SetPrivateField(config, "freightBusinessKey", BakeryKey);
            SetPrivateField(config, "openingHorseId", "A0");
            SetPrivateField(config, "openingWagonId", "test-wagon");
            SetPrivateField(config, "wholesaleAccounts",
                wholesaleAccounts ?? Array.Empty<DevelopmentTownConfiguration.BakeryWholesaleAccountDefinition>());
            return config;
        }

        private static DevelopmentTownConfiguration.BakeryWholesaleAccountDefinition AccountEntry(
            string buyerBusinessKey, string productId, int unitsPerDelivery, int deliveryCadenceDays,
            int pricePerUnitCents, BakeryWholesaleFreshness freshness, int firstDeliveryDayIndex) =>
            new DevelopmentTownConfiguration.BakeryWholesaleAccountDefinition
            {
                sellerBusinessKey = BakeryKey,
                buyerBusinessKey = buyerBusinessKey,
                productId = productId,
                unitsPerDelivery = unitsPerDelivery,
                deliveryCadenceDays = deliveryCadenceDays,
                pricePerUnitCents = pricePerUnitCents,
                freshness = freshness,
                firstDeliveryDayIndex = firstDeliveryDayIndex,
            };

        private static BusinessInstanceState StoreBusiness()
        {
            var business = new BusinessInstanceState();
            SetPrivateField(business, "instanceId", StoreInstanceId);
            SetPrivateField(business, "businessType", BusinessType.GeneralStore);
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity(StoreInstanceId);
            SetPrivateField(business, "runtimeState", runtime);
            return business;
        }

        private static bool Apply(
            DevelopmentTownConfiguration config, BakeryShopRuntime bakery, BusinessRuntimeState state,
            BusinessInstanceState store, int dayIndex, List<string> diag, out string report, out string error) =>
            ScenarioOpeningWholesaleAccounts.TryApply(
                config, BakeryKey, bakery, state, store, dayIndex, diag, out report, out error);

        [Test]
        public void WholesaleAccounts_InvalidEntries_RefuseWithoutBooking()
        {
            // Unknown buyer business key.
            var diag = new List<string>();
            DevelopmentTownConfiguration unknownBuyer = BuildConfig(new[]
            {
                AccountEntry("no-such-buyer", BakeryBreadCatalog.BreadLoafId, 24, 1, 2,
                    BakeryWholesaleFreshness.FreshOrDayOld, 0),
            });
            var bakery = new BakeryShopRuntime(BakeryInstanceId);
            var state = new BusinessRuntimeState();
            Assert.IsTrue(!Apply(unknownBuyer, bakery, state, StoreBusiness(), 0, diag, out _, out string buyerError),
                "an unknown buyer business key refuses the apply");
            StringAssert.Contains("no-such-buyer", buyerError, "refusal names the unknown buyer; error: " + buyerError);
            Assert.AreEqual(0, bakery.WholesaleAccounts.Count, "nothing is booked on refusal");

            // Unknown product.
            DevelopmentTownConfiguration unknownProduct = BuildConfig(new[]
            {
                AccountEntry(StoreKey, "bake.no-such-product", 24, 1, 2,
                    BakeryWholesaleFreshness.FreshOrDayOld, 0),
            });
            var bakery2 = new BakeryShopRuntime(BakeryInstanceId);
            var state2 = new BusinessRuntimeState();
            Assert.IsTrue(!Apply(unknownProduct, bakery2, state2, StoreBusiness(), 0, diag, out _, out string productError),
                "an unknown product refuses the apply");
            StringAssert.Contains("bake.no-such-product", productError, "refusal names the unknown product; error: " + productError);
            Assert.AreEqual(0, bakery2.WholesaleAccounts.Count, "nothing is booked on refusal");

            // Zero units.
            DevelopmentTownConfiguration zeroUnits = BuildConfig(new[]
            {
                AccountEntry(StoreKey, BakeryBreadCatalog.BreadLoafId, 0, 1, 2,
                    BakeryWholesaleFreshness.FreshOrDayOld, 0),
            });
            var bakery3 = new BakeryShopRuntime(BakeryInstanceId);
            var state3 = new BusinessRuntimeState();
            Assert.IsTrue(!Apply(zeroUnits, bakery3, state3, StoreBusiness(), 0, diag, out _, out string unitsError),
                "zero units per delivery refuses the apply");
            Assert.AreEqual(0, bakery3.WholesaleAccounts.Count, "nothing is booked on refusal");
        }

        [Test]
        public void WholesaleAccounts_Twice_BooksExactlyOne()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[]
            {
                AccountEntry(StoreKey, BakeryBreadCatalog.BreadLoafId, 24, 1, 2,
                    BakeryWholesaleFreshness.FreshOrDayOld, 0),
            });
            Assert.IsTrue(config.Validate(out string configError), configError);
            var bakery = new BakeryShopRuntime(BakeryInstanceId);
            var state = new BusinessRuntimeState();
            BusinessInstanceState store = StoreBusiness();

            Assert.IsTrue(Apply(config, bakery, state, store, 0, diag, out _, out string firstError), firstError);
            Assert.IsTrue(Apply(config, bakery, state, store, 0, diag, out string secondReport, out string secondError), secondError);
            StringAssert.Contains("already recorded", secondReport, "the second apply is refused as already applied; report: " + secondReport);

            Assert.AreEqual(1, bakery.WholesaleAccounts.Count, "no duplicate account on re-apply");
            Assert.AreEqual($"{BakeryInstanceId}-wholesale-0", bakery.WholesaleAccounts[0].AccountId,
                "the account id is assigned by AddWholesaleAccount");
        }

        [Test]
        public void WholesaleAccounts_NoEntries_BooksDemandDerivedFallback()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(null);
            Assert.IsTrue(config.Validate(out string configError), configError);
            var bakery = new BakeryShopRuntime(BakeryInstanceId);
            var state = new BusinessRuntimeState();

            Assert.IsTrue(Apply(config, bakery, state, StoreBusiness(), 2, diag, out string report, out string error), error);

            Assert.AreEqual(1, bakery.WholesaleAccounts.Count, "the fallback books exactly one account");
            BakeryWholesaleAccount account = bakery.WholesaleAccounts[0];
            Assert.AreEqual(StoreInstanceId, account.BuyerBusinessId, "the fallback buyer is the General Store");
            Assert.AreEqual(BusinessType.GeneralStore, account.BuyerType);
            Assert.AreEqual(BakeryBreadCatalog.BreadLoafId, account.ProductId);
            Assert.AreEqual(24, account.UnitsPerDelivery, "24 units/day = one 24-loaf batch/day (day-1 demand was 24 loaves)");
            Assert.AreEqual(1, account.DeliveryCadenceDays, "daily cadence");
            Assert.AreEqual(2, account.PricePerUnitCents, "2c wholesale (documented evidence value)");
            Assert.AreEqual(BakeryWholesaleFreshness.FreshOrDayOld, account.Freshness);
            Assert.AreEqual(2, account.NextDeliveryDayIndex, "due on the booking day");
            Assert.IsTrue(account.IsActive);

            string allText = report + " | " + string.Join(" | ", diag);
            StringAssert.Contains("demand-derived fallback", allText, "the fallback is labelled calibration; text: " + allText);
            StringAssert.Contains("D-1 open", allText, "D-1 stays open; text: " + allText);
        }

        [Test]
        public void WholesaleAccounts_ConfigEntry_WinsOverFallback()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[]
            {
                AccountEntry(StoreKey, BakeryBreadCatalog.BreadLoafId, 48, 2, 3,
                    BakeryWholesaleFreshness.FreshOnly, 5),
            });
            Assert.IsTrue(config.Validate(out string configError), configError);
            var bakery = new BakeryShopRuntime(BakeryInstanceId);
            var state = new BusinessRuntimeState();

            Assert.IsTrue(Apply(config, bakery, state, StoreBusiness(), 2, diag, out string report, out string error), error);

            Assert.AreEqual(1, bakery.WholesaleAccounts.Count, "the config entry books one account and no fallback account");
            BakeryWholesaleAccount account = bakery.WholesaleAccounts[0];
            Assert.AreEqual(StoreInstanceId, account.BuyerBusinessId);
            Assert.AreEqual(48, account.UnitsPerDelivery, "the configured units win");
            Assert.AreEqual(2, account.DeliveryCadenceDays, "the configured cadence wins");
            Assert.AreEqual(3, account.PricePerUnitCents, "the configured price wins");
            Assert.AreEqual(BakeryWholesaleFreshness.FreshOnly, account.Freshness, "the configured freshness wins");
            Assert.AreEqual(5, account.NextDeliveryDayIndex, "the configured first delivery day wins");
            Assert.IsTrue(!report.Contains("fallback"), "no fallback is mentioned when the config authors terms; report: " + report);
        }
    }
}
