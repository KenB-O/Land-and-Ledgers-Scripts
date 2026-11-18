using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
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
        [Test]
        public void DueEventFailureClassifier_DoesNotPromoteSuccessfulCapacityEvidenceToWarning()
        {
            const string capacityEvidence = "[V1] capacity consulted at settle: P9 carrying 1u potatoes "
                + "(live cap 50u) — Fits: On-foot carry is within the live cap. "
                + "Mass and volume remain Unknown — no authored person mass or volume limit exists.";

            Assert.IsFalse(ClassifyDueEventDiagnostic(capacityEvidence),
                "A successful Fits result remains informational even if its evidence mentions Unknown limits.");
            Assert.IsTrue(ClassifyDueEventDiagnostic("Trip aborted: outbound route failed."),
                "Actual failed transitions must remain warning-worthy.");
        }

        [Test]
        public void StagedBakeryBatch_DoesNotStartWithoutDistinctDemandOrAlongsideActiveBatch()
        {
            Assert.IsFalse(
                SimulationDrivers.ShouldBeginStagedBakeryBatch(false, false),
                "The live wholesale plan is not a second staged order; no distinct demand means no extra 24-loaf batch.");
            Assert.IsFalse(
                SimulationDrivers.ShouldBeginStagedBakeryBatch(true, true),
                "A second staged batch cannot overlap one already in progress.");
            Assert.IsTrue(
                SimulationDrivers.ShouldBeginStagedBakeryBatch(true, false),
                "A future explicitly authorized, distinct staged order may use the existing staged driver.");
        }

        private static bool ClassifyDueEventDiagnostic(string message)
        {
            MethodInfo classifier = typeof(SimulationDrivers).GetMethod(
                "IsDueEventFailureDiagnostic", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(classifier, "The due-event diagnostic classifier must remain present.");
            return (bool)classifier.Invoke(null, new object[] { message });
        }

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

        private sealed class BakeryBusinessCash : IBusinessIdentityCashPort
        {
            public BakeryBusinessCash(string businessId) => BusinessId = businessId;
            public string BusinessId { get; }
            public int BalanceCents { get; private set; } = 5000;
            public string TryDebit(int amountCents, string reason)
            {
                if (amountCents < 0 || amountCents > BalanceCents) return "insufficient test cash";
                BalanceCents -= amountCents;
                return null;
            }
            public string Credit(int amountCents, string reason)
            {
                if (amountCents < 0) return "negative test credit";
                BalanceCents += amountCents;
                return null;
            }
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

        [Test]
        public void BakerySupplierRegistration_UsesMatchingBusinessCashAndSeedsOnlyLocalResidents()
        {
            var directory = new SupplierDirectory();
            var firstRuntime = new BakeryShopRuntime("bakery-1");
            BakeryBusinessCash cash = new BakeryBusinessCash("bakery-1");
            Series2BakerySupplierAdapter first = SimulationDrivers.RegisterBakerySupplierInDirectory(
                directory, firstRuntime, cash, "bakery-1", "Glenn's Bakery", "building:42");

            Assert.IsNotNull(first);
            Assert.AreEqual(1, directory.All.Count);
            Assert.IsTrue(first.OffersItem(HouseholdItemCatalog.BreadId));
            Assert.IsFalse(first.OffersItem("cooked_meal"), "A bread seller must not silently advertise an unrelated item.");
            Assert.AreEqual(0, first.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "Registration must not create opening bread stock.");

            var restoredRuntime = new BakeryShopRuntime("bakery-1");
            Series2BakerySupplierAdapter restored = SimulationDrivers.RegisterBakerySupplierInDirectory(
                directory, restoredRuntime, new BakeryBusinessCash("bakery-1"),
                "bakery-1", "Glenn's Bakery", "building:42");
            Assert.AreEqual(1, directory.All.Count, "Restore must replace the old runtime adapter, not leave a second seller.");
            Assert.AreSame(restored, directory.All[0]);

            Assert.IsNull(SimulationDrivers.RegisterBakerySupplierInDirectory(
                directory, new BakeryShopRuntime("bakery-2"), cash,
                "bakery-2", "Another Bakery", "building:43"),
                "A bakery must not be wired to a different business's cash authority.");
            Assert.IsNull(SimulationDrivers.RegisterBakerySupplierInDirectory(
                directory, new BakeryShopRuntime("bakery-3"), new BakeryBusinessCash("bakery-3"),
                "bakery-3", "Unplaced Bakery", string.Empty),
                "A seller without an authored building location cannot create a walkable shopping destination.");

            var people = new List<PersonState>
            {
                new PersonState { id = 1, deathDayIndex = -1 },
                new PersonState { id = 2, deathDayIndex = 10 },
                new PersonState { id = 3, isOffMap = true },
            };
            var knowledge = new ShopperSellerKnowledge();
            SimulationDrivers.SeedLocalBakeryKnowledge(people, directory.All, knowledge, 12);
            SimulationDrivers.SeedLocalBakeryKnowledge(people, directory.All, knowledge, 13);

            Assert.IsTrue(knowledge.Knows(1, "bakery-1"));
            Assert.IsFalse(knowledge.Knows(2, "bakery-1"));
            Assert.IsFalse(knowledge.Knows(3, "bakery-1"));
            Assert.AreEqual(1, knowledge.Records.Count, "Repeated bootstrap must not duplicate seller knowledge.");
            Assert.AreEqual(12, knowledge.Records[0].LearnedDayIndex);
        }
    }
}
