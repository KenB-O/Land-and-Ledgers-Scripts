using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.FirstLedger;
using LandLedgers.Population;
using LandLedgers.Persistence;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class GeneralStoreItemOfferIdentityTests
    {
        private const string DefinitionPath = "Assets/Core/Economy/GeneralStoreBusinessDefinition.asset";
        private const string FixturePath = "Assets/Core/Population/SixPersonDevelopmentFixture.asset";

        [Test]
        public void DevelopmentFixtureOpeningStock_IsDeterministicAndWithinConfiguredRanges()
        {
            SixPersonDevelopmentFixture fixture = AssetDatabase.LoadAssetAtPath<SixPersonDevelopmentFixture>(FixturePath);
            Assert.IsNotNull(fixture);
            Assert.IsTrue(fixture.Validate(out string error), error);

            bool seedVariesAStockLine = false;
            foreach (SixPersonDevelopmentFixture.OpeningRetailStockRange range in fixture.OpeningStoreStock)
            {
                Assert.IsTrue(fixture.TryResolveOpeningStoreStock(range.itemId, 1897, out int first));
                Assert.IsTrue(fixture.TryResolveOpeningStoreStock(range.itemId, 1897, out int repeated));
                Assert.AreEqual(first, repeated, range.itemId + " must be stable for the same scenario seed.");
                Assert.That(first, Is.InRange(range.minimumUnits, range.maximumUnits), range.itemId);
                Assert.IsTrue(fixture.TryResolveOpeningStoreStock(range.itemId, 1898, out int otherSeed));
                seedVariesAStockLine |= otherSeed != first;
            }
            Assert.IsTrue(seedVariesAStockLine, "At least one configured line should demonstrate seed variation.");
        }

        [Test]
        public void ItemLevelOffers_RequireIdentifiedStockAndExactHouseholdUnits()
        {
            GeneralStoreBusinessDefinition definition = LoadDefinition();
            GameObject host = new GameObject(nameof(ItemLevelOffers_RequireIdentifiedStockAndExactHouseholdUnits));
            try
            {
                GeneralStoreRuntimeManager manager = ConfigureManager(host, definition);
                BusinessInstanceState store = CreateNpcStore(definition);

                AddLot(store, definition, "bread", 14);
                AddLot(store, definition, "flour", 6);
                AddLot(store, definition, "potatoes", 5);
                AddLot(store, definition, "sugar", 8);
                AddLot(store, definition, "coffee", 4);
                AddLot(store, definition, "tea", 3);
                AddLot(store, definition, "salt", 6);

                GeneralStoreSupplierAdapter emptySupplier = new GeneralStoreSupplierAdapter(
                    manager.BuildHouseholdTradingPort(CreateNpcStore(definition)));
                Assert.IsFalse(emptySupplier.OffersItem("bread"), "A catalog entry without a physical lot is not an offer.");

                GeneralStoreTradingPort port = manager.BuildHouseholdTradingPort(store);
                var supplier = new GeneralStoreSupplierAdapter(port);
                Assert.IsTrue(supplier.OffersItem("bread"));
                Assert.IsTrue(supplier.OffersItem("sugar"));
                Assert.IsTrue(supplier.OffersItem("coffee"));
                Assert.IsTrue(supplier.OffersItem("tea"));
                Assert.IsFalse(supplier.OffersItem("flour"), "10 lb sacks cannot be advertised as household pounds without an authored conversion.");
                Assert.IsFalse(supplier.OffersItem("potatoes"), "Sacks cannot be advertised as pounds without an authored conversion.");
                Assert.IsFalse(supplier.OffersItem("salt"), "Small bags cannot be advertised as pounds without a conversion.");
                Assert.IsFalse(supplier.OffersItem("preserved_meat"));

                Assert.AreEqual(14, supplier.ItemStockUnits("bread"));
                Assert.AreEqual(6, store.RuntimeState.GetRetailItemStockUnits("flour"));
                Assert.AreEqual(46, store.RuntimeState.GetCategoryStock("staple_food").CurrentStockUnits,
                    "Category reporting must equal the sum of the seven seeded item lots exactly once.");
                Assert.Greater(supplier.ItemPricePerUnitCents("bread"), 0);

                int otherSkuBefore = supplier.ItemStockUnits("sugar");
                int breadPrice = supplier.ItemPricePerUnitCents("bread");
                var cashSettlement = new ShoppingSettlement { Mode = ShoppingSettlementMode.CashFromLedger };
                Assert.AreEqual(2, supplier.SellItem("bread", 2, 0, cashSettlement, new List<string>()));
                Assert.AreEqual(12, supplier.ItemStockUnits("bread"));
                Assert.AreEqual(otherSkuBefore, supplier.ItemStockUnits("sugar"));
                Assert.AreEqual(2 * breadPrice, store.RuntimeState.CurrentCashCents);

                Assert.AreEqual(12, supplier.SellItem("bread", 12, 0, cashSettlement, new List<string>()));
                Assert.IsFalse(supplier.OffersItem("bread"), "The final unit sale must remove live availability.");
                Assert.AreEqual(0, supplier.ItemStockUnits("bread"));
                Assert.AreEqual(14 * breadPrice, store.RuntimeState.CurrentCashCents,
                    "Sale proceeds must post to the Business cash authority at the authored item price.");
                Assert.IsFalse(port.TryConsumeStoreUnits("staple_food", 1, out _), "Identified SKU stock cannot be consumed by category only.");

                BusinessRuntimeSaveDto dto = store.RuntimeState.CaptureSaveDto();
                BusinessRuntimeState restored = BusinessRuntimeState.FromSaveDto(dto, BusinessType.GeneralStore);
                Assert.AreEqual(0, restored.GetRetailItemStockUnits("bread"));
                Assert.AreEqual(6, restored.GetRetailItemStockUnits("flour"));
                Assert.AreEqual(8, restored.GetRetailItemStockUnits("sugar"));
                Assert.AreEqual(0, restored.GetRetailItemPhysicalUnits("bread"));
                Assert.AreEqual("lot:bread", restored.RetailItemLots[0].LotId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ScenarioOpeningEndowment_IsFiniteAndAppliedExactlyOnce()
        {
            GeneralStoreBusinessDefinition definition = LoadDefinition();
            SixPersonDevelopmentFixture fixture = AssetDatabase.LoadAssetAtPath<SixPersonDevelopmentFixture>(FixturePath);
            Assert.IsNotNull(fixture);
            BusinessInstanceState store = CreateNpcStore(definition);

            Assert.IsTrue(ScenarioOpeningStockEndowment.TryApply(fixture, store, definition, 0, 0,
                out string report, out string error), error);
            Assert.That(report, Does.Contain("general_store_060"));
            int expectedLotCount = fixture.OpeningStoreStock.Count;
            Assert.AreEqual(expectedLotCount, store.RuntimeState.RetailItemLots.Count);
            int firstPhysicalQuantity = store.RuntimeState.GetRetailItemPhysicalUnits("bread");
            Assert.That(firstPhysicalQuantity, Is.InRange(12, 24));
            foreach (SixPersonDevelopmentFixture.OpeningRetailStockRange range in fixture.OpeningStoreStock)
            {
                Assert.IsTrue(fixture.TryResolveOpeningStoreStock(range.itemId, 0, out int expected));
                Assert.AreEqual(expected, store.RuntimeState.GetRetailItemPhysicalUnits(range.itemId), range.itemId);
            }

            Assert.IsTrue(ScenarioOpeningStockEndowment.TryApply(fixture, store, definition, 0, 0,
                out _, out error), error);
            Assert.AreEqual(expectedLotCount, store.RuntimeState.RetailItemLots.Count, "Repeat initialization must not create another lot.");
            Assert.AreEqual(firstPhysicalQuantity, store.RuntimeState.GetRetailItemPhysicalUnits("bread"));
            Assert.AreEqual(0, store.RuntimeState.CurrentCashCents, "The opening stock endowment does not grant operating cash.");
            Assert.IsTrue(store.RuntimeState.HasAppliedScenarioEndowment(
                $"scenario-opening-endowment:{fixture.ProfileId}:{store.InstanceId}:0"));

            BusinessInstanceState restoredStore = BusinessInstanceState.FromSaveDto(store.CaptureSaveDto());
            Assert.IsTrue(ScenarioOpeningStockEndowment.TryApply(fixture, restoredStore, definition, 0, 1,
                out report, out error), error);
            Assert.That(report, Does.Contain("already recorded"));
            Assert.AreEqual(expectedLotCount, restoredStore.RuntimeState.RetailItemLots.Count);
            Assert.AreEqual(firstPhysicalQuantity, restoredStore.RuntimeState.GetRetailItemPhysicalUnits("bread"));
        }

        [Test]
        public void GeneralStoreProfileAndPlayerFormation_DoNotGrantInventory()
        {
            GeneralStoreBusinessDefinition definition = LoadDefinition();
            BusinessInstanceState playerStore = BusinessInstanceState.Create(
                "player_general_store_test", definition, 3, BusinessOwnerIdentity.Player());
            Assert.AreEqual(0, playerStore.RuntimeState.RetailItemLots.Count);
            foreach (ItemCategoryDefinition category in definition.Categories)
                Assert.AreEqual(0, playerStore.RuntimeState.GetCategoryStock(category.CategoryId).CurrentStockUnits,
                    "Authored catalog/profile defaults are not a physical opening receipt.");
        }

        [Test]
        public void AppliedEndowmentMarkerAndLots_SurviveDoubleRestoreWithoutDuplication()
        {
            GeneralStoreBusinessDefinition definition = LoadDefinition();
            BusinessInstanceState store = CreateNpcStore(definition);
            AddLot(store, definition, "bread", 18);
            Assert.IsTrue(store.RuntimeState.RecordAppliedScenarioEndowment("fixture:B60:1897"));
            Assert.IsFalse(store.RuntimeState.RecordAppliedScenarioEndowment("fixture:B60:1897"));

            BusinessRuntimeState firstRestore = BusinessRuntimeState.FromSaveDto(
                store.RuntimeState.CaptureSaveDto(), BusinessType.GeneralStore);
            BusinessRuntimeState secondRestore = BusinessRuntimeState.FromSaveDto(
                firstRestore.CaptureSaveDto(), BusinessType.GeneralStore);
            Assert.IsTrue(secondRestore.HasAppliedScenarioEndowment("fixture:B60:1897"));
            Assert.AreEqual(18, secondRestore.GetRetailItemStockUnits("bread"));
            Assert.AreEqual(1, secondRestore.RetailItemLots.Count);
        }

        private static GeneralStoreBusinessDefinition LoadDefinition()
        {
            GeneralStoreBusinessDefinition definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(DefinitionPath);
            Assert.IsNotNull(definition, "The authored General Store business definition is required.");
            return definition;
        }

        private static GeneralStoreRuntimeManager ConfigureManager(GameObject host, GeneralStoreBusinessDefinition definition)
        {
            GeneralStoreRuntimeManager manager = host.AddComponent<GeneralStoreRuntimeManager>();
            FieldInfo field = typeof(GeneralStoreRuntimeManager).GetField("storeDefinition", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(manager, definition);
            return manager;
        }

        private static BusinessInstanceState CreateNpcStore(GeneralStoreBusinessDefinition definition)
        {
            return BusinessInstanceState.Create("general_store_060", definition, 60,
                BusinessOwnerIdentity.Npc(1, "Kennedy Baldwin-Ooms", "Baldwin-Ooms"));
        }

        private static void AddLot(BusinessInstanceState business, GeneralStoreBusinessDefinition definition,
            string itemId, int quantity)
        {
            ItemDefinition item = null;
            foreach (ItemDefinition candidate in definition.Items)
                if (candidate != null && string.Equals(candidate.ItemId, itemId, StringComparison.OrdinalIgnoreCase))
                { item = candidate; break; }
            Assert.IsNotNull(item, "Missing profile item " + itemId);
            string result = business.RuntimeState.TryAddRetailItemLot("lot:" + itemId, item.ItemId,
                item.CategoryId, item.Unit, quantity, business.InstanceId, business.InstanceId,
                "scenario-opening-endowment:test", 0);
            Assert.IsNull(result, result);
        }
    }
}
