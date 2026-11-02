using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.Restaurant;
using LandLedgers.Population;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Review 2 Pass C (scripts): Roslyn acceptance for the item/SKU
    /// map and the driver-binding checklist, plus the Pass B weekly
    /// input decision recorded in code terms. Production classes as
    /// the system under test; no orchestrator shortcuts. All
    /// conversions are test parameters, never canon: the factors
    /// below (10 lb per sack, 24 loaves per batch, 2 bakery fuel
    /// units per bundle) are supplied by the tests to prove the
    /// parameter mechanism, not asserted as game rules. No fixture
    /// data, no Ethan/Tia/Shane data, no nine-person fixture is
    /// used here. Unity compilation, Test Runner, PlayMode, and
    /// scene wiring are NOT claimed; the weekly-converter guard
    /// itself lives in the Unity-side SharedBusinessRuntimeManager
    /// and is covered by a separate Unity-only test file that this
    /// Roslyn suite does not run.
    /// </summary>
    [TestFixture]
    public sealed class Review2PassCScriptsTests
    {
        private static Review2ItemTranslation MustTranslate(
            Review2ItemSpace fromSpace, string fromId, Review2ItemSpace toSpace)
        {
            var diag = new List<string>();
            Review2ItemTranslation entry = Review2ItemSkuMap.TryTranslate(fromSpace, fromId, toSpace, diag);
            Assert.IsNotNull(entry, $"expected a translation for {fromSpace} '{fromId}' -> {toSpace}; diag: {string.Join(" | ", diag)}");
            return entry;
        }

        [Test]
        public void MapIdsMatchCatalogConstantsNotDriftedLiterals()
        {
            // If a catalog constant ever changes, the map's literals
            // must change with it; these assertions catch the drift.
            Assert.AreEqual(BakeryBreadCatalog.BreadLoafId, MustTranslate(Review2ItemSpace.Store, "bread", Review2ItemSpace.BakeryCode).ToId);
            Assert.AreEqual(BakeryBreadCatalog.FlourItemId, MustTranslate(Review2ItemSpace.Store, "flour", Review2ItemSpace.BakeryCode).ToId);
            Assert.AreEqual(HouseholdItemCatalog.BreadId, MustTranslate(Review2ItemSpace.Household, "bread", Review2ItemSpace.Store).ToId);
            Assert.AreEqual(HouseholdItemCatalog.FlourId, MustTranslate(Review2ItemSpace.Household, "flour", Review2ItemSpace.Store).ToId);
            Assert.AreEqual(RestaurantMealCatalog.BreadItemId, MustTranslate(Review2ItemSpace.BakeryCode, "bake.bread-loaf", Review2ItemSpace.RestaurantCode).ToId);
            Assert.AreEqual(BakeryBreadCatalog.FuelItemId, MustTranslate(Review2ItemSpace.BakeryProfile, "bakery_fuel_bundle", Review2ItemSpace.BakeryCode).ToId);
        }

        [Test]
        public void StoreFlourSackConvertsByCallerFactorNotByDescription()
        {
            Review2ItemTranslation entry = MustTranslate(Review2ItemSpace.Store, "flour", Review2ItemSpace.BakeryCode);
            Assert.AreEqual(Review2ConversionKind.Packaging, entry.Kind);
            var diag = new List<string>();
            Assert.IsTrue(Review2ItemSkuMap.TryConvertUnits(entry, 2, new Review2ConversionParameters { LbPerStoreFlourSack = 10 }, out int converted, out int remainder, diag));
            Assert.AreEqual(20, converted);
            Assert.AreEqual(0, remainder);
        }

        [Test]
        public void StoreFlourConversionRefusesWithoutCallerFactor()
        {
            Review2ItemTranslation entry = MustTranslate(Review2ItemSpace.Store, "flour", Review2ItemSpace.BakeryCode);
            var diag = new List<string>();
            Assert.IsFalse(Review2ItemSkuMap.TryConvertUnits(entry, 2, new Review2ConversionParameters(), out int converted, out int remainder, diag));
            Assert.AreEqual(0, converted);
            Assert.AreEqual(2, remainder);
            StringAssert.Contains("LbPerStoreFlourSack", string.Join(" ", diag));
        }

        [Test]
        public void BakeryFlourBackToSacksDividesWithRemainderNeverRoundsUp()
        {
            Review2ItemTranslation entry = MustTranslate(Review2ItemSpace.BakeryCode, "bakery-flour", Review2ItemSpace.Store);
            var diag = new List<string>();
            Assert.IsTrue(Review2ItemSkuMap.TryConvertUnits(entry, 25, new Review2ConversionParameters { LbPerStoreFlourSack = 10 }, out int converted, out int remainder, diag));
            Assert.AreEqual(2, converted);
            Assert.AreEqual(5, remainder);
        }

        [Test]
        public void HouseholdFlourLbToStoreSackUsesSameFactorWithRemainder()
        {
            Review2ItemTranslation entry = MustTranslate(Review2ItemSpace.Household, "flour", Review2ItemSpace.Store);
            Assert.AreEqual(Review2ConversionKind.Packaging, entry.Kind);
            var diag = new List<string>();
            Assert.IsTrue(Review2ItemSkuMap.TryConvertUnits(entry, 15, new Review2ConversionParameters { LbPerStoreFlourSack = 10 }, out int converted, out int remainder, diag));
            Assert.AreEqual(1, converted);
            Assert.AreEqual(5, remainder);
        }

        [Test]
        public void BreadLoafTranslatesDirectlyBothWaysAndHouseholdIsStoreBread()
        {
            var diag = new List<string>();
            Assert.IsTrue(Review2ItemSkuMap.TryConvertUnits(MustTranslate(Review2ItemSpace.Store, "bread", Review2ItemSpace.BakeryCode), 7, new Review2ConversionParameters(), out int converted, out int remainder, diag));
            Assert.AreEqual(7, converted);
            Assert.AreEqual(0, remainder);
            Assert.IsTrue(Review2ItemSkuMap.TryConvertUnits(MustTranslate(Review2ItemSpace.BakeryCode, "bake.bread-loaf", Review2ItemSpace.Store), 7, new Review2ConversionParameters(), out converted, out remainder, diag));
            Assert.AreEqual(7, converted);
            Review2ItemTranslation identity = MustTranslate(Review2ItemSpace.Household, "bread", Review2ItemSpace.Store);
            Assert.AreEqual(Review2ConversionKind.Identity, identity.Kind);
        }

        [Test]
        public void BakeryLoafToRestaurantLoafIsDirect()
        {
            Review2ItemTranslation entry = MustTranslate(Review2ItemSpace.BakeryCode, "bake.bread-loaf", Review2ItemSpace.RestaurantCode);
            Assert.AreEqual(Review2ConversionKind.Direct, entry.Kind);
            var diag = new List<string>();
            Assert.IsTrue(Review2ItemSkuMap.TryConvertUnits(entry, 2, new Review2ConversionParameters(), out int converted, out int remainder, diag));
            Assert.AreEqual(2, converted);
        }

        [Test]
        public void BreadBatchConvertsOnlyWithCallerLoavesPerBatch()
        {
            Review2ItemTranslation entry = MustTranslate(Review2ItemSpace.BakeryProfile, "bread_batch", Review2ItemSpace.BakeryCode);
            var diag = new List<string>();
            Assert.IsFalse(Review2ItemSkuMap.TryConvertUnits(entry, 1, new Review2ConversionParameters(), out _, out _, diag));
            Assert.IsTrue(Review2ItemSkuMap.TryConvertUnits(entry, 2, new Review2ConversionParameters { LoavesPerBreadBatch = 24 }, out int converted, out int remainder, diag));
            Assert.AreEqual(48, converted);
            Assert.AreEqual(0, remainder);
        }

        [Test]
        public void FuelBundleFactorsAreIndependentPerSku()
        {
            Review2ItemTranslation bakeryBundle = MustTranslate(Review2ItemSpace.BakeryProfile, "bakery_fuel_bundle", Review2ItemSpace.BakeryCode);
            Review2ItemTranslation fuelBundle = MustTranslate(Review2ItemSpace.FuelProfile, "fuel_wood_bundle", Review2ItemSpace.BakeryCode);
            Assert.AreNotEqual(bakeryBundle.FactorName, fuelBundle.FactorName);
            var diag = new List<string>();
            var onlyBakeryFactor = new Review2ConversionParameters { BakeryFuelUnitsPerBakeryFuelBundle = 2 };
            Assert.IsTrue(Review2ItemSkuMap.TryConvertUnits(bakeryBundle, 3, onlyBakeryFactor, out int converted, out _, diag));
            Assert.AreEqual(6, converted);
            Assert.IsFalse(Review2ItemSkuMap.TryConvertUnits(fuelBundle, 3, onlyBakeryFactor, out _, out _, diag));
            Assert.IsTrue(Review2ItemSkuMap.TryConvertUnits(fuelBundle, 3, new Review2ConversionParameters { BakeryFuelUnitsPerFuelWoodBundle = 2 }, out converted, out _, diag));
            Assert.AreEqual(6, converted);
        }

        [Test]
        public void DeliberatelyUnmappedIdsRefuseWithDiagnostic()
        {
            var diag = new List<string>();
            Assert.IsNull(Review2ItemSkuMap.TryTranslate(Review2ItemSpace.BakeryCode, BakeryBreadCatalog.NotionsItemId, Review2ItemSpace.Store, diag));
            Assert.IsNull(Review2ItemSkuMap.TryTranslate(Review2ItemSpace.BakeryCode, BakeryBreadCatalog.RollsId, Review2ItemSpace.Store, diag));
            Assert.IsNull(Review2ItemSkuMap.TryTranslate(Review2ItemSpace.BakeryCode, BakeryBreadCatalog.PieId, Review2ItemSpace.Store, diag));
            Assert.IsNull(Review2ItemSkuMap.TryTranslate(Review2ItemSpace.FuelProfile, "fuel_offcut_bundle", Review2ItemSpace.BakeryCode, diag));
            Assert.IsNull(Review2ItemSkuMap.TryTranslate(Review2ItemSpace.Store, "freight_goods", Review2ItemSpace.BakeryCode, diag));
            Assert.IsNull(Review2ItemSkuMap.TryTranslate(Review2ItemSpace.Store, "staple_food", Review2ItemSpace.BakeryCode, diag));
            StringAssert.Contains("bakery-notions", string.Join(" ", diag));
        }

        [Test]
        public void NegativeQuantitiesAndNullTranslationsRefuse()
        {
            Review2ItemTranslation entry = MustTranslate(Review2ItemSpace.Store, "bread", Review2ItemSpace.BakeryCode);
            var diag = new List<string>();
            Assert.IsFalse(Review2ItemSkuMap.TryConvertUnits(entry, -1, new Review2ConversionParameters(), out int converted, out _, diag));
            Assert.AreEqual(0, converted);
            Assert.IsFalse(Review2ItemSkuMap.TryConvertUnits(null, 1, new Review2ConversionParameters(), out _, out _, diag));
        }

        [Test]
        public void EveryMapEntryCarriesEvidenceAndDirectEntriesNeedNoFactor()
        {
            foreach (Review2ItemTranslation entry in Review2ItemSkuMap.AllTranslations)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Basis), $"entry {entry.FromId} -> {entry.ToId} must carry evidence.");
                if (entry.Kind == Review2ConversionKind.Identity || entry.Kind == Review2ConversionKind.Direct)
                {
                    Assert.IsTrue(string.IsNullOrWhiteSpace(entry.FactorName), $"direct/identity entry {entry.FromId} must not name a factor.");
                }
                else
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(entry.FactorName), $"packaging entry {entry.FromId} must name its caller factor.");
                }
            }
            Assert.AreEqual(12, Review2ItemSkuMap.AllTranslations.Count);
        }

        [Test]
        public void BindingChecklistListsAdapterRegistrationAndDaySettingFirst()
        {
            IReadOnlyList<string> steps = Review2DriverBindingChecklist.BindingSteps;
            Assert.AreEqual(10, steps.Count);
            StringAssert.Contains("Register the bakery and fuel supplier adapters", steps[0]);
            StringAssert.Contains("DayIndex at EVERY day start", steps[1]);
            StringAssert.Contains("Pass 6 bakery pre-check", steps[2]);
            StringAssert.Contains("Series2BusinessRuntimeCashPort", steps[3]);
        }

        [Test]
        public void BindingChecklistRefusesUnsetBakeryDayAndWarnsOnMismatchWithoutInventingRefusal()
        {
            var diag = new List<string>();
            Assert.IsNotNull(Review2DriverBindingChecklist.CheckBakeryAdapterDay(-1, 5, diag));
            diag.Clear();
            Assert.IsNull(Review2DriverBindingChecklist.CheckBakeryAdapterDay(4, 5, diag));
            Assert.AreEqual(1, diag.Count);
            StringAssert.Contains("does NOT enforce day equality", diag[0]);
            diag.Clear();
            Assert.IsNull(Review2DriverBindingChecklist.CheckBakeryAdapterDay(5, 5, diag));
            Assert.AreEqual(0, diag.Count);
        }

        [Test]
        public void BindingChecklistFuelOrderRequiresPrecheckOrExplicitPartialAcceptance()
        {
            var diag = new List<string>();
            Assert.IsNotNull(Review2DriverBindingChecklist.CheckFuelSupplyOrder(false, false, diag));
            Assert.IsNull(Review2DriverBindingChecklist.CheckFuelSupplyOrder(true, false, diag));
            diag.Clear();
            Assert.IsNull(Review2DriverBindingChecklist.CheckFuelSupplyOrder(false, true, diag));
            Assert.AreEqual(1, diag.Count);
            StringAssert.Contains("explicitly accepted", diag[0]);
        }

        [Test]
        public void BindingChecklistSaveStatesListEveryMissingAssignment()
        {
            var diag = new List<string>();
            string refusal = Review2DriverBindingChecklist.CheckSaveStateAssignments(false, false, false, diag);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("Series2Pass9State", refusal);
            StringAssert.Contains("Series2Pass10State", refusal);
            StringAssert.Contains("PassXlProcurementService", refusal);
            Assert.IsNull(Review2DriverBindingChecklist.CheckSaveStateAssignments(true, true, true, diag));
        }
    }
}
