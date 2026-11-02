using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using LandLedgers.World;
using LandLedgers.World.Property;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Orchestration
{
    public sealed class SimulationDriversSceneBindingTests
    {
        private sealed class StorePort : IGeneralStoreTradingPort
        {
            public string StoreBusinessId { get; set; }
            public string StoreName => "Local General Store";
            public string LocationId => "town-store";
            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => new List<GeneralStoreCategoryOffer>();
            public StoreTradingCapability TradingCapability => new StoreTradingCapability();
            public int CurrentCashCents => 0;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;
            public int CategoryStockUnits(string categoryId) => 0;
            public int CategoryPricePerUnitCents(string categoryId) => 0;
            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            { consumedUnits = 0; return false; }
            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents) { }
            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }
            public string SpendCash(int amountCents, string purpose) => "not used";
            public string ReceiveStock(string categoryId, int units, string provenanceLabel) => "not used";
            public int CategoryReceivableUnits(string categoryId) => 0;
            public int CategoryTargetStockUnits(string categoryId) => 0;
            public int DeliveryFeeCents(string itemId, int units) => 0;
        }

        private sealed class OtherSupplier : IGoodsSupplier
        {
            public string SupplierBusinessId => "mill-1";
            public string SupplierName => "Mill";
            public string LocationId => "mill-road";
            public bool HasCategory(string categoryId) => false;
            public int StockUnits(string categoryId) => 0;
            public int PricePerUnitCents(string categoryId) => 0;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics) => 0;
        }

        [Test]
        public void Rebind_AttachesDiscoveredTownWorldToHubParcelAuthorities()
        {
            GameObject hubObject = new("Scene Binding Hub");
            GameObject worldObject = new("Scene Binding Town World");
            GameObject driversObject = new("Scene Binding Drivers");
            try
            {
                SimulationSystemsHub hub = hubObject.AddComponent<SimulationSystemsHub>();
                worldObject.AddComponent<TownWorldController>();
                SimulationDrivers drivers = driversObject.AddComponent<SimulationDrivers>();

                drivers.Rebind();

                Assert.IsInstanceOf<TownWorldParcelBuildingIndex>(
                    hub.ParcelSubdivisions.BuildingIndex,
                    "A freshly rebound driver must attach the real scene world to parcel subdivision checks.");
            }
            finally
            {
                Object.DestroyImmediate(driversObject);
                Object.DestroyImmediate(worldObject);
                Object.DestroyImmediate(hubObject);
            }
        }

        [Test]
        public void LocalGeneralStoreKnowledge_IsIdempotentAndExcludesOtherOrPlaceholderSuppliers()
        {
            var people = new List<PersonState>
            {
                new PersonState { id = 1, deathDayIndex = -1 },
                new PersonState { id = 2, deathDayIndex = 9 },
                new PersonState { id = 3, isOffMap = true },
            };
            var actualStore = new GeneralStoreSupplierAdapter(new StorePort { StoreBusinessId = "general_store_060" });
            var placeholderStore = new GeneralStoreSupplierAdapter(new StorePort { StoreBusinessId = "general-store" });
            var suppliers = new List<IGoodsSupplier> { actualStore, placeholderStore, new OtherSupplier() };
            var knowledge = new ShopperSellerKnowledge();

            SimulationDrivers.SeedLocalGeneralStoreKnowledge(people, suppliers, knowledge, 12);
            SimulationDrivers.SeedLocalGeneralStoreKnowledge(people, suppliers, knowledge, 13);

            Assert.IsTrue(knowledge.Knows(1, "general_store_060"));
            Assert.IsFalse(knowledge.Knows(2, "general_store_060"), "Dead residents are not seeded as current local shoppers.");
            Assert.IsFalse(knowledge.Knows(3, "general_store_060"), "Off-map Persons are not residents of this local store's town.");
            Assert.IsFalse(knowledge.Knows(1, "general-store"), "The generic placeholder identity must never be learned as B60.");
            Assert.IsFalse(knowledge.Knows(1, "mill-1"), "Registering B60 must not reveal unrelated suppliers.");
            Assert.That(knowledge.Records.Count, Is.EqualTo(1), "Repeated bootstrap calls must not duplicate knowledge records.");
            Assert.That(knowledge.Records[0].LearnedDayIndex, Is.EqualTo(12), "Idempotence preserves the original discovery event.");
        }
    }
}
