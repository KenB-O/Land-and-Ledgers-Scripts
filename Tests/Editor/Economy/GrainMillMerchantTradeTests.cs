using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GrainMill;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D2E: the gristmill's merchant books. The mill buys merchant grain
    /// from named sellers (farm/dealer/elevator) at named or policy-booked
    /// prices, and sells mill-owned flour/meal/feed/bran to named buyers;
    /// costs and revenues are recorded for the ledger authority to post
    /// (money never moves in the runtime). Provenance runs all the way
    /// through: farm <- dealer <- mill <- buyer.
    /// </summary>
    [TestFixture]
    public sealed class GrainMillMerchantTradeTests
    {
        private static CropLot GrainLot(EntityIdRegistry registry, CropKind crop, int units)
        {
            return new CropLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                Crop = crop,
                ProductKind = "grain",
                QuantityUnits = units,
                FieldId = "field-9",
                FarmId = "farm-7",
                HarvestDayIndex = 200,
                SeedSource = "saved seed",
            };
        }

        private static GrainMillShopRuntime NewMill(EntityIdRegistry registry)
        {
            return new GrainMillShopRuntime("grist-mill-1", registry);
        }

        [Test]
        public void BuyMerchantLot_BooksNamedSeller_AndReturnsCostForLedger()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            int cost = mill.BuyMerchantLot(
                GrainLot(registry, CropKind.Wheat, 100),
                "elevator-1", "Prairie Grain Elevator", 210, diag);

            // Policy book "buy:wheat" = 6c; 100u -> 600c.
            Assert.AreEqual(600, cost, "cost returned for the ledger authority: " + string.Join(" | ", diag));
            Assert.AreEqual(100, mill.GrainStock.MerchantUnitsOnHand(CropKind.Wheat));

            Assert.AreEqual(1, mill.PurchaseHistory.Count);
            var purchase = mill.PurchaseHistory[0];
            Assert.AreEqual(CropKind.Wheat, purchase.Crop);
            Assert.AreEqual(100, purchase.Units);
            Assert.AreEqual(6, purchase.PricePerUnitCents);
            Assert.AreEqual(600, purchase.CostCents);
            Assert.AreEqual("elevator-1", purchase.SellerId);
            Assert.AreEqual("Prairie Grain Elevator", purchase.SellerName);
            Assert.AreEqual("farm-7", purchase.FarmId, "farm provenance off the lot survives the buy");
        }

        [Test]
        public void BuyMerchantLot_ExplicitPriceWinsOverPolicyBook()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            int cost = mill.BuyMerchantLot(
                GrainLot(registry, CropKind.Corn, 50),
                "dealer-2", "Ada the Dealer", 211, diag, pricePerUnitCents: 5);

            Assert.AreEqual(250, cost, "explicit 5c price beats the policy 4c row: " + string.Join(" | ", diag));
            Assert.AreEqual(5, mill.PurchaseHistory[0].PricePerUnitCents);
            Assert.AreEqual("dealer-2", mill.PurchaseHistory[0].SellerId);
        }

        [Test]
        public void BuyMerchantLot_RefusesAnonymousSeller_BooksNothing()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            int cost = mill.BuyMerchantLot(
                GrainLot(registry, CropKind.Wheat, 50), "", "", 212, diag);

            Assert.AreEqual(-1, cost, "anonymous seller refused");
            Assert.AreEqual(0, mill.GrainStock.MerchantUnitsOnHand(CropKind.Wheat), "no grain in the mill's books");
            Assert.AreEqual(0, mill.PurchaseHistory.Count, "no purchase booked");
        }

        [Test]
        public void BuyMerchantLot_RefusesBadLot_BooksNothing()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            var hay = GrainLot(registry, CropKind.Hay, 50);
            int cost = mill.BuyMerchantLot(hay, "farm-3", "Gus Farmer", 213, diag);

            Assert.AreEqual(-1, cost, "hay is not millable grain");
            Assert.AreEqual(0, mill.PurchaseHistory.Count);
        }

        [Test]
        public void SellProductLot_FlourSaleToNamedBuyer_RevenueRecordedWithChain()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            var grain = GrainLot(registry, CropKind.Wheat, 100);
            string sourceLotId = grain.LotId.ToString();
            Assert.AreEqual(600, mill.BuyMerchantLot(grain, "dealer-1", "Otto the Dealer", 214, diag));
            var batch = mill.RunGristBatch(
                GristBatchMode.Merchant, CropKind.Wheat, 100,
                registry.Allocate(EntityKind.Person), 215, diag);
            Assert.IsNotNull(batch, string.Join(" | ", diag));

            var sale = mill.SellProductLot("flour", 50, "bakery-1", "Town Bakery", 216, diag);

            // Policy book "sell:flour" = 12c; 50u -> 600c.
            Assert.IsNotNull(sale, "the sale should fill: " + string.Join(" | ", diag));
            Assert.AreEqual(50, sale.Lot.QuantityUnits);
            Assert.AreEqual(600, sale.Record.RevenueCents, "revenue returned for the ledger authority");
            Assert.AreEqual(12, sale.Record.PricePerUnitCents);
            Assert.AreEqual("Town Bakery", sale.Record.BuyerName);
            StringAssert.Contains(sourceLotId, sale.Record.SourceGrainLotIds, "the grain chain survives the sale");
            Assert.AreEqual(20, mill.ProductStock.UnitsOnHand("flour"), "20u remain in the mill's stock");
            Assert.AreEqual(1, mill.SaleHistory.Count);

            // The whole merchant loop: 100u wheat at 6c = 600c in, 50u flour
            // at 12c = 600c out on half the flour; the margin is in the books.
            Assert.AreEqual(600, mill.PurchaseHistory[0].CostCents);
        }

        [Test]
        public void SellProductLot_ExplicitPriceWinsOverPolicyBook()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            Assert.AreEqual(300, mill.BuyMerchantLot(
                GrainLot(registry, CropKind.Oats, 100), "farm-8", "Harlan Farmer", 217, diag));
            var batch = mill.RunGristBatch(
                GristBatchMode.Merchant, CropKind.Oats, 100,
                registry.Allocate(EntityKind.Person), 218, diag);
            Assert.IsNotNull(batch, string.Join(" | ", diag));

            var sale = mill.SellProductLot("feed", 90, "ranch-1", "Bar-Z Ranch", 219, diag, pricePerUnitCents: 4);

            Assert.IsNotNull(sale, string.Join(" | ", diag));
            Assert.AreEqual(360, sale.Record.RevenueCents, "explicit 4c price beats the policy 3c row");
            Assert.AreEqual(4, sale.Record.PricePerUnitCents);
            Assert.AreEqual("Bar-Z Ranch", sale.Record.BuyerName);
            Assert.AreEqual(0, mill.ProductStock.UnitsOnHand("feed"));
        }

        [Test]
        public void SellProductLot_RefusesUnnamedBuyer()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            Assert.AreEqual(600, mill.BuyMerchantLot(
                GrainLot(registry, CropKind.Wheat, 100), "dealer-1", "Otto the Dealer", 220, diag));
            var batch = mill.RunGristBatch(
                GristBatchMode.Merchant, CropKind.Wheat, 100,
                registry.Allocate(EntityKind.Person), 221, diag);
            Assert.IsNotNull(batch, string.Join(" | ", diag));

            var sale = mill.SellProductLot("flour", 10, "", "", 222, diag);
            Assert.IsNull(sale, "anonymous buyers refused — revenue needs provenance");
            Assert.AreEqual(70, mill.ProductStock.UnitsOnHand("flour"), "no units dispensed");
            Assert.AreEqual(0, mill.SaleHistory.Count);
        }

        [Test]
        public void SellProductLot_RefusesShortfall_NothingConjured()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            var sale = mill.SellProductLot("flour", 10, "bakery-1", "Town Bakery", 223, diag);
            Assert.IsNull(sale, "the mill cannot sell flour it does not hold");
            Assert.AreEqual(0, mill.SaleHistory.Count);
        }

        [Test]
        public void SellProductLot_RefusesUnpricedProductKind()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            Assert.AreEqual(600, mill.BuyMerchantLot(
                GrainLot(registry, CropKind.Wheat, 100), "dealer-1", "Otto the Dealer", 224, diag));
            var batch = mill.RunGristBatch(
                GristBatchMode.Merchant, CropKind.Wheat, 100,
                registry.Allocate(EntityKind.Person), 225, diag);
            Assert.IsNotNull(batch, string.Join(" | ", diag));

            var sale = mill.SellProductLot("cornmeal-extra", 10, "store-1", "General Store", 226, diag);
            Assert.IsNull(sale, "unpriced product kinds are never sold on assumed prices");
            Assert.AreEqual(0, mill.SaleHistory.Count);
            Assert.AreEqual(70, mill.ProductStock.UnitsOnHand("flour"), "stock untouched");
        }

        [Test]
        public void SellProductLot_BranSale_RoutesFeedChainDirectly()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            Assert.AreEqual(600, mill.BuyMerchantLot(
                GrainLot(registry, CropKind.Wheat, 100), "dealer-1", "Otto the Dealer", 227, diag));
            var batch = mill.RunGristBatch(
                GristBatchMode.Merchant, CropKind.Wheat, 100,
                registry.Allocate(EntityKind.Person), 228, diag);
            Assert.IsNotNull(batch, string.Join(" | ", diag));

            // A flour-and-feed merchant buys bran straight from the mill
            // (Canon R6 §1.2 feed commerce).
            var sale = mill.SellProductLot("bran", 10, "feed-merchant-1", "Flour and Feed Merchant", 229, diag);

            Assert.IsNotNull(sale, string.Join(" | ", diag));
            Assert.AreEqual(20, sale.Record.RevenueCents, "10u bran at policy 2c");
            Assert.AreEqual(0, mill.ProductStock.UnitsOnHand("bran"));
        }

        [Test]
        public void SaveLoad_RoundTripPreservesMerchantBooks()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            Assert.AreEqual(600, mill.BuyMerchantLot(
                GrainLot(registry, CropKind.Wheat, 100), "dealer-1", "Otto the Dealer", 230, diag));
            var batch = mill.RunGristBatch(
                GristBatchMode.Merchant, CropKind.Wheat, 100,
                registry.Allocate(EntityKind.Person), 231, diag);
            Assert.IsNotNull(batch, string.Join(" | ", diag));
            var sale = mill.SellProductLot("flour", 20, "bakery-1", "Town Bakery", 232, diag);
            Assert.IsNotNull(sale, string.Join(" | ", diag));

            var dto = mill.CaptureSaveDto();
            var restored = NewMill(new EntityIdRegistry());
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.PurchaseHistory.Count);
            Assert.AreEqual(600, restored.PurchaseHistory[0].CostCents);
            Assert.AreEqual("Otto the Dealer", restored.PurchaseHistory[0].SellerName);
            Assert.AreEqual(1, restored.SaleHistory.Count);
            Assert.AreEqual(240, restored.SaleHistory[0].RevenueCents, "20u flour at 12c");
            Assert.AreEqual("Town Bakery", restored.SaleHistory[0].BuyerName);
            Assert.AreEqual(50, restored.ProductStock.UnitsOnHand("flour"), "50u remain after the 20u sale");
        }

        [Test]
        public void TollCalibration_StaysOneSixteenthInKind_TwoCentsCash()
        {
            var policy = new GristMillPolicy();
            Assert.AreEqual(2, policy.TollCashCentsPerUnit, "cash toll unchanged");
            Assert.AreEqual(1f / 16f, policy.TollInKindFlourShare01, "in-kind toll unchanged");
        }
    }
}
