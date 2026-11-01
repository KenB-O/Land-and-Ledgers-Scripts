using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Butcher;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.GeneralStore;
using LandLedgers.Primitives;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// FRM-3: the general store's butchery counter. Meat retail is added via BIZ-2
    /// TryAddOperation; the store buys wholesale cuts through the BIZ-4 link and
    /// retails them at a margin; slaughter is rejected on a storefront per the BIZ-1
    /// premises validation (yard + sanitation required).
    /// </summary>
    [TestFixture]
    public sealed class StoreButcheryTests
    {
        private static BusinessCapabilityRegistry BuildRegistry()
        {
            var registry = new BusinessCapabilityRegistry();
            var diagnostics = new List<string>();
            StoreButcheryCapabilities.RegisterAll(registry, diagnostics);
            Assert.IsTrue(registry.TryGet(StoreButcheryCapabilities.MeatRetailCapabilityId, out _));
            Assert.IsTrue(registry.TryGet(StoreButcheryCapabilities.SlaughterCapabilityId, out _));
            return registry;
        }

        private static BusinessInstanceState BuildStore()
        {
            var profile = ScriptableObject.CreateInstance<BusinessProfileDefinition>();
            var business = BusinessInstanceState.Create(
                "store_001", profile, -1, BusinessOwnerIdentity.Player());
            business.AddCapability("retail");
            return business;
        }

        private static BusinessRuntimeState NewStoreState()
        {
            var definition = new BusinessDefinition(BusinessType.GeneralStore, "store_001", "Test Store");
            return BusinessRuntimeState.CreateFrom(
                definition,
                new List<ItemCategoryDefinition>(),
                new List<ItemDefinition>(),
                null,
                BusinessThroughputMode.Retail,
                0,
                0);
        }

        private static ButcherRuntime BuildButcherWithMeat(out string lotId)
        {
            var ids = new EntityIdRegistry();
            var registry = new AnimalRegistry(ids);
            AnimalState animal = registry.RegisterAnimal(
                AnimalSpecies.Cattle, "Shorthorn", AnimalSex.Male,
                AnimalOwnerKind.Person, "P0", 10, "test purchase");

            var butcher = new ButcherRuntime("biz_butcher_001", "site-prod", "site-retail");
            var diagnostics = new List<string>();
            Assert.IsTrue(butcher.TryBuyLivestock(registry, animal.AnimalId, 2000, 11, diagnostics));
            Assert.IsNotNull(butcher.Slaughter(registry, animal.AnimalId, 1000, 12, diagnostics));

            lotId = null;
            foreach (ButcherLot lot in butcher.Lots)
            {
                if (lot.CanSell && lot.QuantityLbs > 0)
                {
                    lotId = lot.LotId;
                    break;
                }
            }

            Assert.IsNotNull(lotId, "Butcher should hold a sellable lot after slaughter.");
            return butcher;
        }

        [Test]
        public void MeatRetailCapability_AddsToStorefront()
        {
            BusinessInstanceState store = BuildStore();
            var registry = BuildRegistry();
            var diagnostics = new List<string>();

            bool ok = BusinessExpansion.TryAddOperation(
                store, new[] { StoreButcheryCapabilities.MeatRetailCapabilityId }, registry,
                PremisesKind.DedicatedStorefront, diagnostics);

            Assert.IsTrue(ok, string.Join("; ", diagnostics));
            Assert.IsTrue(store.HasCapability(StoreButcheryCapabilities.MeatRetailCapabilityId));
        }

        [Test]
        public void WholesaleToRetail_MarginFlow()
        {
            ButcherRuntime butcher = BuildButcherWithMeat(out string lotId);
            BusinessRuntimeState store = NewStoreState();
            var diagnostics = new List<string>();

            WholesaleCutPurchase purchase = StoreButchery.BuyWholesaleCuts(
                butcher, lotId, 10, store, "store_001", "Test Store",
                "Test Butcher", 1.15f, 100, diagnostics);

            Assert.IsNotNull(purchase, string.Join("; ", diagnostics));
            Assert.AreEqual(10, purchase.LbsTaken);
            Assert.Greater(purchase.WholesaleCostCents, 0);
            Assert.Greater(purchase.RetailPricePerLbCents, purchase.WholesaleCostPerLbCents,
                "Retail must clear wholesale cost — the counter's margin.");

            CategoryStockState meat = store.GetCategoryStock("meat");
            Assert.IsNotNull(meat);
            Assert.AreEqual(10, meat.CurrentStockUnits);
        }

        [Test]
        public void SlaughterCapability_RejectedOnStorefront()
        {
            BusinessInstanceState store = BuildStore();
            var registry = BuildRegistry();
            var diagnostics = new List<string>();

            bool ok = BusinessExpansion.TryAddOperation(
                store, new[] { StoreButcheryCapabilities.SlaughterCapabilityId }, registry,
                PremisesKind.DedicatedStorefront, diagnostics);

            Assert.IsFalse(ok, "A storefront slaughterhouse must be rejected, not fudged.");
            Assert.IsFalse(store.HasCapability(StoreButcheryCapabilities.SlaughterCapabilityId));
        }
    }
}
