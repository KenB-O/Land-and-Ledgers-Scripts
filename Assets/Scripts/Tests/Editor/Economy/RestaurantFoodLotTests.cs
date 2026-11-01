using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Restaurant;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W2B: the pantry — lots carry full upstream provenance, FIFO dispense,
    /// loud shortfall refusal, no orphan lots, no synthetic stock, and the
    /// one-time bootstrap endowment.
    /// </summary>
    [TestFixture]
    public sealed class RestaurantFoodLotTests
    {
        private static RestaurantFoodLot NamedLot(string foodName, int units, int day)
        {
            return new RestaurantFoodLot
            {
                LotId = new EntityIdRegistry().Allocate(EntityKind.Lot),
                FoodName = foodName,
                Units = units,
                AcquiredDayIndex = day,
                SupplierBusinessId = "butcher-1",
                SourceLotId = "butcher-lot-9",
                SupplierNote = "beef from animal A-12, produced day 40",
            };
        }

        [Test]
        public void ReceiveLot_RefusesOrphanLots_Loudly()
        {
            var stock = new RestaurantFoodStock();
            var diag = new List<string>();

            var orphan = NamedLot(RestaurantMealCatalog.MeatItemId, 10, 50);
            orphan.SupplierBusinessId = string.Empty;
            orphan.ImportOrderId = string.Empty;
            orphan.OriginName = string.Empty;

            string rejection = stock.ReceiveLot(orphan, diag);
            Assert.NotNull(rejection, "an orphan lot (no supplier, no import chain) must be refused");
            StringAssert.Contains("No orphan lots", rejection);
            Assert.AreEqual(0, stock.Lots.Count);
        }

        [Test]
        public void ReceiveLot_RefusesNullZeroAndDuplicateLots()
        {
            var stock = new RestaurantFoodStock();
            var diag = new List<string>();

            Assert.NotNull(stock.ReceiveLot(null, diag), "null lot refused");

            var zero = NamedLot(RestaurantMealCatalog.MeatItemId, 0, 50);
            Assert.NotNull(stock.ReceiveLot(zero, diag), "zero-unit lot refused");

            var nameless = NamedLot(string.Empty, 10, 50);
            Assert.NotNull(stock.ReceiveLot(nameless, diag), "nameless lot refused");

            var first = NamedLot(RestaurantMealCatalog.MeatItemId, 10, 50);
            Assert.Null(stock.ReceiveLot(first, diag), "first receipt succeeds");
            var dup = NamedLot(RestaurantMealCatalog.MeatItemId, 5, 51);
            dup.LotId = first.LotId;
            Assert.NotNull(stock.ReceiveLot(dup, diag), "duplicate lot id refused");
            Assert.AreEqual(1, stock.Lots.Count);
        }

        [Test]
        public void TryDispenseUnits_FifoOldestFirst_WithProvenanceLines()
        {
            var stock = new RestaurantFoodStock();
            var diag = new List<string>();

            Assert.Null(stock.ReceiveLot(NamedLot(RestaurantMealCatalog.ProduceItemId, 10, 60), diag));
            Assert.Null(stock.ReceiveLot(NamedLot(RestaurantMealCatalog.ProduceItemId, 10, 50), diag));

            var lines = stock.TryDispenseUnits(RestaurantMealCatalog.ProduceItemId, 12, 70, diag);
            Assert.NotNull(lines, "dispense succeeds when covered");
            Assert.AreEqual(2, lines.Count, "two lots touched");
            Assert.AreEqual(10, lines[0].UnitsTaken, "oldest lot (day 50) emptied first");
            Assert.AreEqual(2, lines[1].UnitsTaken, "remainder from the newer lot");
            StringAssert.Contains("butcher-lot-9", lines[0].ProvenanceChain, "provenance rides the dispense line");
            Assert.AreEqual(8, stock.UnitsOnHand(RestaurantMealCatalog.ProduceItemId));
            Assert.AreEqual(1, stock.Lots.Count, "emptied lot leaves the pantry — no ghost stock");
        }

        [Test]
        public void TryDispenseUnits_ShortfallRefusesLoudly_NothingConjured()
        {
            var stock = new RestaurantFoodStock();
            var diag = new List<string>();

            Assert.Null(stock.ReceiveLot(NamedLot(RestaurantMealCatalog.MeatItemId, 3, 50), diag));

            var lines = stock.TryDispenseUnits(RestaurantMealCatalog.MeatItemId, 10, 70, diag);
            Assert.IsNull(lines, "shortfall returns null — no units conjured");
            StringAssert.Contains("shortfall", string.Join("\n", diag));
            Assert.AreEqual(3, stock.UnitsOnHand(RestaurantMealCatalog.MeatItemId), "pantry untouched by the refusal");
        }

        [Test]
        public void BootstrapEndowment_MarksLotsExplicitly_NeverAutoReplenished()
        {
            var stock = new RestaurantFoodStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            RestaurantFoodBootstrap.ApplyBootstrapEndowment(stock, registry, 100, diag);

            Assert.AreEqual(4, stock.Lots.Count, "meat, bread, produce, dairy endowments");
            foreach (var lot in stock.Lots)
            {
                Assert.IsTrue(lot.IsBootstrapEndowment, $"{lot.FoodName}: explicitly marked");
                StringAssert.Contains("BOOTSTRAP", lot.ProvenanceChain());
                Assert.Greater(lot.Units, 0);
            }

            Assert.Greater(stock.UnitsOnHand(RestaurantMealCatalog.MeatItemId), 0);
            Assert.Greater(stock.UnitsOnHand(RestaurantMealCatalog.BreadItemId), 0);
            Assert.Greater(stock.UnitsOnHand(RestaurantMealCatalog.ProduceItemId), 0);
            Assert.Greater(stock.UnitsOnHand(RestaurantMealCatalog.DairyItemId), 0);
        }

        [Test]
        public void BootstrapEndowment_NullGuards_DiagnosticsNotCrashes()
        {
            var diag = new List<string>();
            RestaurantFoodBootstrap.ApplyBootstrapEndowment(null, new EntityIdRegistry(), 100, diag);
            RestaurantFoodBootstrap.ApplyBootstrapEndowment(new RestaurantFoodStock(), null, 100, diag);
            Assert.GreaterOrEqual(diag.Count, 2, "both failures land in diagnostics");
        }

        [Test]
        public void ReceiveButcherLot_PreservesCarcassChain()
        {
            var stock = new RestaurantFoodStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            var butcherLot = new LandLedgers.Economy.Butcher.ButcherLot(
                "butcher-lot-7",
                LandLedgers.Economy.Butcher.CarcassProduct.RetailCuts,
                EntityId.For(EntityKind.Animal, 12),
                producedDayIndex: 40,
                quantityLbs: 50,
                siteId: "butcher-shop",
                wholesalePricePerLbCents: 6,
                retailPricePerLbCents: 10);

            string rejection = RestaurantFoodSupply.ReceiveButcherLot(
                stock, butcherLot, 20, "butcher-1", 55, registry, diag);
            Assert.Null(rejection, $"receipt should succeed: {rejection}");
            Assert.AreEqual(20, stock.UnitsOnHand(RestaurantMealCatalog.MeatItemId));

            var lot = stock.Lots[0];
            Assert.AreEqual("butcher-1", lot.SupplierBusinessId);
            Assert.AreEqual("butcher-lot-7", lot.SourceLotId);
            StringAssert.Contains("butcher-lot-7", lot.ProvenanceChain());
        }

        [Test]
        public void ReceiveBakeryBreadLot_RefusesStaleBread()
        {
            var stock = new RestaurantFoodStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            // Build a stale bread lot honestly: baked day 10, evaluated at day 70.
            var breadLot = new LandLedgers.Economy.Businesses.Bakery.BakeryBreadLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                ProductId = RestaurantMealCatalog.BreadItemId,
                BakedDayIndex = 10,
                UnitCount = 12,
            };
            breadLot.AgeToDay(70);
            Assert.AreEqual(LandLedgers.Economy.Businesses.Bakery.BakeryGoodsCondition.Stale, breadLot.Condition);

            string rejection = RestaurantFoodSupply.ReceiveBakeryBreadLot(
                stock, breadLot, 12, "bakery-1", 70, registry, diag);
            Assert.NotNull(rejection, "stale bread never enters the pantry");
            Assert.AreEqual(0, stock.UnitsOnHand(RestaurantMealCatalog.BreadItemId));
        }

        [Test]
        public void FoodStock_SaveLoad_RoundTripsLotsWithProvenance()
        {
            var stock = new RestaurantFoodStock();
            var diag = new List<string>();
            Assert.Null(stock.ReceiveLot(NamedLot(RestaurantMealCatalog.DairyItemId, 8, 50), diag));

            var dto = stock.CaptureSaveDto();
            var restored = new RestaurantFoodStock();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.Lots.Count);
            Assert.AreEqual(8, restored.UnitsOnHand(RestaurantMealCatalog.DairyItemId));
            StringAssert.Contains("butcher-lot-9", restored.Lots[0].ProvenanceChain());
        }
    }
}
