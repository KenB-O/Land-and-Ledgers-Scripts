using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GrainMill;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W5A: the grain-mill runtime. Grist batches grind wheat->flour,
    /// corn->meal, oats->feed from real grain lots with full provenance;
    /// toll and merchant modes are policy data on each batch; bran/feed
    /// byproducts route honestly to feed merchants; dull/broken stones gate
    /// ALL grinding until the dresser works; save/load round-trips.
    /// </summary>
    [TestFixture]
    public sealed class GrainMillRuntimeTests
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
        public void MerchantBatch_GrindsWheatToFlourFeedBran()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            Assert.IsNull(mill.ReceiveMerchantLot(GrainLot(registry, CropKind.Wheat, 100), diag));

            var result = mill.RunGristBatch(
                GristBatchMode.Merchant, CropKind.Wheat, 100,
                registry.Allocate(EntityKind.Person), 210, diag);

            Assert.IsNotNull(result, "merchant batch should grind: " + string.Join(" | ", diag));
            var record = result.Record;
            Assert.AreEqual(70, record.MainUnits);
            Assert.AreEqual("flour", record.MainProductKind);
            Assert.AreEqual(20, record.FeedUnits);
            Assert.AreEqual(10, record.BranUnits);
            Assert.AreEqual(0, record.TollCashCents);
            Assert.AreEqual(70, mill.ProductStock.UnitsOnHand("flour"));
            Assert.AreEqual(20, mill.ProductStock.UnitsOnHand("feed"));
            Assert.AreEqual(10, mill.ProductStock.UnitsOnHand("bran"));
            Assert.AreEqual(0, mill.GrainStock.MerchantUnitsOnHand(CropKind.Wheat));
            Assert.AreEqual(1, mill.BatchHistory.Count);
        }

        [Test]
        public void MerchantBatch_GrindsCornToMeal()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            Assert.IsNull(mill.ReceiveMerchantLot(GrainLot(registry, CropKind.Corn, 100), diag));
            var result = mill.RunGristBatch(
                GristBatchMode.Merchant, CropKind.Corn, 100,
                registry.Allocate(EntityKind.Person), 211, diag);

            Assert.IsNotNull(result, string.Join(" | ", diag));
            Assert.AreEqual("meal", result.Record.MainProductKind);
            Assert.AreEqual(70, result.Record.MainUnits);
            Assert.AreEqual(70, mill.ProductStock.UnitsOnHand("meal"));
            Assert.AreEqual(0, mill.ProductStock.UnitsOnHand("flour"));
        }

        [Test]
        public void MerchantBatch_GrindsOatsToFeed()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            Assert.IsNull(mill.ReceiveMerchantLot(GrainLot(registry, CropKind.Oats, 100), diag));
            var result = mill.RunGristBatch(
                GristBatchMode.Merchant, CropKind.Oats, 100,
                registry.Allocate(EntityKind.Person), 212, diag);

            Assert.IsNotNull(result, string.Join(" | ", diag));
            Assert.AreEqual("feed", result.Record.MainProductKind);
            Assert.AreEqual(90, result.Record.MainUnits);
            Assert.AreEqual(1, result.Record.BranUnits);
        }

        [Test]
        public void MerchantBatch_RefusesHay_NoGristProfile()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            // Hay arrives as forage, never threshed "grain" — refused at intake...
            var hay = GrainLot(registry, CropKind.Hay, 50);
            Assert.IsNotNull(mill.ReceiveMerchantLot(hay, diag), "hay must be refused at intake");

            // ...and an (invalidly) forced hay batch is refused at grinding.
            var result = mill.RunGristBatch(
                GristBatchMode.Merchant, CropKind.Hay, 50,
                registry.Allocate(EntityKind.Person), 213, diag);
            Assert.IsNull(result, "hay has no grist profile — grinding refused");
        }

        [Test]
        public void MerchantBatch_RefusesWhenMillHoldsNoGrain()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            var result = mill.RunGristBatch(
                GristBatchMode.Merchant, CropKind.Wheat, 100,
                registry.Allocate(EntityKind.Person), 214, diag);
            Assert.IsNull(result, "the mill cannot grind grain it does not hold");
            Assert.AreEqual(0, mill.ProductStock.UnitsOnHand("flour"));
        }

        [Test]
        public void MerchantBatch_RefusesSplitAcrossLots_OneLotPerBatch()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            Assert.IsNull(mill.ReceiveMerchantLot(GrainLot(registry, CropKind.Wheat, 60), diag));
            Assert.IsNull(mill.ReceiveMerchantLot(GrainLot(registry, CropKind.Wheat, 60), diag));

            var result = mill.RunGristBatch(
                GristBatchMode.Merchant, CropKind.Wheat, 100,
                registry.Allocate(EntityKind.Person), 215, diag);
            Assert.IsNull(result, "a batch consumes from exactly one grain lot — 60u oldest lot cannot cover 100u");

            // The honest re-request: 60u from the oldest lot grinds fine.
            result = mill.RunGristBatch(
                GristBatchMode.Merchant, CropKind.Wheat, 60,
                registry.Allocate(EntityKind.Person), 215, diag);
            Assert.IsNotNull(result, string.Join(" | ", diag));
            Assert.AreEqual(42, result.Record.MainUnits);
        }

        [Test]
        public void ReceiveMerchantLot_RefusesAnonymousAndNonGrainLots()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            var anonymous = GrainLot(registry, CropKind.Wheat, 50);
            anonymous.LotId = EntityId.Invalid;
            Assert.IsNotNull(mill.ReceiveMerchantLot(anonymous, diag), "anonymous grain refused");

            var sheaves = GrainLot(registry, CropKind.Wheat, 50);
            sheaves.ProductKind = "sheaves";
            Assert.IsNotNull(mill.ReceiveMerchantLot(sheaves, diag), "unthreshed sheaves refused");

            Assert.AreEqual(0, mill.GrainStock.MerchantUnitsOnHand(CropKind.Wheat));
        }

        [Test]
        public void TollBatch_CustomerKeepsFlour_MillTakesToll()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            var tollLot = GrainLot(registry, CropKind.Wheat, 100);
            Assert.IsNull(mill.ReceiveTollLot(tollLot, "cust-1", "Aldous Farmer", 216, diag));

            var result = mill.RunGristBatch(
                GristBatchMode.Toll, CropKind.Wheat, 100,
                registry.Allocate(EntityKind.Person), 216, diag,
                tollLotId: tollLot.LotId.ToString());

            Assert.IsNotNull(result, string.Join(" | ", diag));
            var record = result.Record;
            // 70u flour; in-kind toll = floor(70 / 16) = 4u; customer keeps 66u.
            Assert.AreEqual(70, record.MainUnits);
            Assert.AreEqual(4, record.TollInKindUnits);
            Assert.AreEqual(66, record.MainUnits - record.TollInKindUnits);
            Assert.AreEqual(200, record.TollCashCents, "100u at 2c/u cash toll");
            Assert.AreEqual("cust-1", record.CustomerId);
            Assert.AreEqual("Aldous Farmer", record.CustomerName);

            // The mill owns only its toll share; the customer holds the rest in custody.
            Assert.AreEqual(4, mill.ProductStock.UnitsOnHand("flour"));
            Assert.AreEqual(66, CustomerUnits(mill, "cust-1", "flour"));
            Assert.AreEqual(20, CustomerUnits(mill, "cust-1", "feed"));
            Assert.AreEqual(10, CustomerUnits(mill, "cust-1", "bran"));
            Assert.AreEqual(0, mill.ProductStock.UnitsOnHand("feed"), "toll byproducts are customer custody, not mill stock");
        }

        [Test]
        public void ReceiveTollLot_RefusesAnonymousCustomer()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            var tollLot = GrainLot(registry, CropKind.Wheat, 100);
            Assert.IsNotNull(mill.ReceiveTollLot(tollLot, "", "", 217, diag),
                "toll grain without a named customer is refused");
            Assert.AreEqual(0, mill.GrainStock.TollUnitsInCustody());
        }

        [Test]
        public void TollBatch_NeverBorrowsFromMerchantStock()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            var tollLot = GrainLot(registry, CropKind.Wheat, 40);
            Assert.IsNull(mill.ReceiveTollLot(tollLot, "cust-2", "Beth Farmer", 218, diag));
            Assert.IsNull(mill.ReceiveMerchantLot(GrainLot(registry, CropKind.Wheat, 100), diag));

            var result = mill.RunGristBatch(
                GristBatchMode.Toll, CropKind.Wheat, 100,
                registry.Allocate(EntityKind.Person), 218, diag,
                tollLotId: tollLot.LotId.ToString());

            Assert.IsNull(result, "the 40u toll lot cannot cover 100u — merchant stock is never borrowed for toll grinds");
            Assert.AreEqual(100, mill.GrainStock.MerchantUnitsOnHand(CropKind.Wheat));
        }

        [Test]
        public void TollBatch_RefusesWithoutNamedTollLot()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            Assert.IsNull(mill.ReceiveMerchantLot(GrainLot(registry, CropKind.Wheat, 100), diag));
            var result = mill.RunGristBatch(
                GristBatchMode.Toll, CropKind.Wheat, 50,
                registry.Allocate(EntityKind.Person), 219, diag);
            Assert.IsNull(result, "toll mode without a named toll lot is refused");
        }

        [Test]
        public void StoneGate_GatesAllOutputUntilDressed()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            Assert.IsNull(mill.ReceiveMerchantLot(GrainLot(registry, CropKind.Wheat, 200), diag));
            mill.Stones.ApplyWear(0.70f); // stones now at 0.30 — dull

            var refused = mill.RunGristBatch(
                GristBatchMode.Merchant, CropKind.Wheat, 100,
                registry.Allocate(EntityKind.Person), 220, diag);
            Assert.IsNull(refused, "dull stones gate ALL grinding");
            Assert.AreEqual(200, mill.GrainStock.MerchantUnitsOnHand(CropKind.Wheat), "refused batch consumes nothing");

            Assert.IsNull(mill.RecordStoneDressing(registry.Allocate(EntityKind.Person), 221, diag));
            Assert.AreEqual(1f, mill.Stones.Condition01);

            var ground = mill.RunGristBatch(
                GristBatchMode.Merchant, CropKind.Wheat, 100,
                registry.Allocate(EntityKind.Person), 221, diag);
            Assert.IsNotNull(ground, "dressed stones grind again: " + string.Join(" | ", diag));
        }

        [Test]
        public void StoneGate_BrokenStonesRefuseDistinctly()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            mill.Stones.ApplyWear(0.95f); // 0.05 — broken
            string refusal = mill.Stones.CheckStoneGate(222, diag);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("BROKEN", refusal);
        }

        [Test]
        public void OfferProductLot_FlourFlowsToBakery_WithChainIntact()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            var grain = GrainLot(registry, CropKind.Wheat, 100);
            string sourceLotId = grain.LotId.ToString();
            Assert.IsNull(mill.ReceiveMerchantLot(grain, diag));
            var result = mill.RunGristBatch(
                GristBatchMode.Merchant, CropKind.Wheat, 100,
                registry.Allocate(EntityKind.Person), 223, diag);
            Assert.IsNotNull(result, string.Join(" | ", diag));

            MillLot offered = mill.OfferProductLot("flour", 50, diag);
            Assert.IsNotNull(offered, "flour offer should fill: " + string.Join(" | ", diag));
            Assert.AreEqual(50, offered.QuantityUnits);
            Assert.AreEqual("flour", offered.ProductKind);
            Assert.AreEqual("grist-mill-1", offered.MillerBusinessId);
            StringAssert.Contains(sourceLotId, offered.SourceGrainLotId);

            // The W2A bakery takes the mill's flour lot with the chain intact.
            var bin = new BakeryFlourStock();
            string refusal = BakeryFlourSupply.ReceiveMillLot(bin, offered, 224, registry, diag);
            Assert.IsNull(refusal, "bakery should accept the mill flour lot: " + refusal);
            Assert.AreEqual(50, bin.UnitsOnHand(BakeryFlourSupply.FlourMaterialId));
            Assert.AreEqual(20, mill.ProductStock.UnitsOnHand("flour"), "20u remain in the mill's stock");
        }

        [Test]
        public void RouteFeedToSupplier_MovesRealUnitsWithNamedUpstream()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            Assert.IsNull(mill.ReceiveMerchantLot(GrainLot(registry, CropKind.Wheat, 100), diag));
            var result = mill.RunGristBatch(
                GristBatchMode.Merchant, CropKind.Wheat, 100,
                registry.Allocate(EntityKind.Person), 225, diag);
            Assert.IsNotNull(result, string.Join(" | ", diag));

            var supplier = new MillFeedSupplier("feed-merchant-1", "Feed Merchant", 5);
            int routed = mill.RouteFeedToSupplier(supplier, 226, diag);
            Assert.AreEqual(30, routed, "20u feed + 10u bran");
            Assert.AreEqual(30, supplier.FeedStockUnits);
            Assert.AreEqual(0, mill.ProductStock.UnitsOnHand("feed"));
            Assert.AreEqual(0, mill.ProductStock.UnitsOnHand("bran"));
            StringAssert.Contains("grist-mill-1", supplier.UpstreamSource);
        }

        [Test]
        public void RouteFeedToSupplier_KeepsLotsWhenSupplierRefuses()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            Assert.IsNull(mill.ReceiveMerchantLot(GrainLot(registry, CropKind.Wheat, 100), diag));
            var result = mill.RunGristBatch(
                GristBatchMode.Merchant, CropKind.Wheat, 100,
                registry.Allocate(EntityKind.Person), 227, diag);
            Assert.IsNotNull(result, string.Join(" | ", diag));

            // A null supplier (no named merchant) keeps the lots in the yard.
            int routed = mill.RouteFeedToSupplier(null, 228, diag);
            Assert.AreEqual(0, routed);
            Assert.AreEqual(20, mill.ProductStock.UnitsOnHand("feed"));
        }

        [Test]
        public void BuyTollByproducts_ConvertsCustomerByproductsToMillStock()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            var tollLot = GrainLot(registry, CropKind.Wheat, 100);
            Assert.IsNull(mill.ReceiveTollLot(tollLot, "cust-3", "Cora Farmer", 229, diag));
            var result = mill.RunGristBatch(
                GristBatchMode.Toll, CropKind.Wheat, 100,
                registry.Allocate(EntityKind.Person), 229, diag,
                tollLotId: tollLot.LotId.ToString());
            Assert.IsNotNull(result, string.Join(" | ", diag));

            int costCents = mill.BuyTollByproducts("cust-3", 3, 230, diag);
            Assert.AreEqual(90, costCents, "30u byproducts at 3c/u — posted by the ledger authority");
            Assert.AreEqual(20, mill.ProductStock.UnitsOnHand("feed"));
            Assert.AreEqual(10, mill.ProductStock.UnitsOnHand("bran"));
            Assert.AreEqual(66, CustomerUnits(mill, "cust-3", "flour"), "the customer's flour is untouched");
        }

        [Test]
        public void ReleaseTollProducts_ReturnsCustomerLots()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            var tollLot = GrainLot(registry, CropKind.Wheat, 100);
            Assert.IsNull(mill.ReceiveTollLot(tollLot, "cust-4", "Dan Farmer", 231, diag));
            var result = mill.RunGristBatch(
                GristBatchMode.Toll, CropKind.Wheat, 100,
                registry.Allocate(EntityKind.Person), 231, diag,
                tollLotId: tollLot.LotId.ToString());
            Assert.IsNotNull(result, string.Join(" | ", diag));

            var released = mill.ReleaseTollProducts("cust-4", diag);
            Assert.AreEqual(3, released.Count, "flour + feed + bran custody lots");
            Assert.AreEqual(0, mill.TollOutbox.Count);
        }

        [Test]
        public void SaveLoad_RoundTripPreservesStocksStonesAndHistory()
        {
            var registry = new EntityIdRegistry();
            var mill = NewMill(registry);
            var diag = new List<string>();

            Assert.IsNull(mill.ReceiveMerchantLot(GrainLot(registry, CropKind.Wheat, 100), diag));
            var tollLot = GrainLot(registry, CropKind.Corn, 60);
            Assert.IsNull(mill.ReceiveTollLot(tollLot, "cust-5", "Eli Farmer", 232, diag));

            var r1 = mill.RunGristBatch(GristBatchMode.Merchant, CropKind.Wheat, 100,
                registry.Allocate(EntityKind.Person), 232, diag);
            var r2 = mill.RunGristBatch(GristBatchMode.Toll, CropKind.Corn, 60,
                registry.Allocate(EntityKind.Person), 233, diag, tollLotId: tollLot.LotId.ToString());
            Assert.IsNotNull(r1, string.Join(" | ", diag));
            Assert.IsNotNull(r2, string.Join(" | ", diag));
            mill.Stones.ApplyWear(0.2f);
            Assert.IsNull(mill.RecordStoneDressing(registry.Allocate(EntityKind.Person), 234, diag));

            var dto = mill.CaptureSaveDto();
            var restored = NewMill(new EntityIdRegistry());
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(70, restored.ProductStock.UnitsOnHand("flour"));
            Assert.AreEqual(20, restored.ProductStock.UnitsOnHand("feed"));
            Assert.AreEqual(10, restored.ProductStock.UnitsOnHand("bran"));
            Assert.AreEqual(1f, restored.Stones.Condition01, "dressing event survived the round trip");
            Assert.AreEqual(2, restored.BatchHistory.Count);
            Assert.AreEqual(GristBatchMode.Toll, restored.BatchHistory[1].Mode);
            Assert.AreEqual("Eli Farmer", restored.BatchHistory[1].CustomerName);
            Assert.AreEqual(40, CustomerUnits(restored, "cust-5", "meal"), "toll meal custody survived");
            Assert.AreEqual(0, restored.GrainStock.MerchantUnitsOnHand(CropKind.Wheat), "ground grain is gone");
        }

        [Test]
        public void TollPolicy_InKindShareRoundsDown_CustomerKeepsRest()
        {
            var policy = new GristMillPolicy();
            Assert.AreEqual(4, policy.TollInKindUnits(70), "floor(70/16)");
            Assert.AreEqual(0, policy.TollInKindUnits(0));
            policy.TollInKindFlourShare01 = 0f;
            Assert.AreEqual(0, policy.TollInKindUnits(70), "zero share disables the in-kind toll");
        }

        [Test]
        public void MerchantPrice_UnnamedKeyReturnsZero_NeverAssumed()
        {
            var policy = new GristMillPolicy();
            Assert.AreEqual(12, policy.MerchantPrice("sell:flour"));
            Assert.AreEqual(0, policy.MerchantPrice("sell:nonexistent"));
            Assert.AreEqual(0, policy.MerchantPrice(""));
        }

        [Test]
        public void ConversionProfiles_NameCrpRatios_HayHasNone()
        {
            var wheat = GristMillConversionData.ProfileFor(CropKind.Wheat);
            Assert.IsNotNull(wheat);
            Assert.AreEqual("flour", wheat.MainProductKind);
            Assert.AreEqual(7, wheat.MainUnitsPer10, "names the CRP-3 Miller calibration");
            Assert.AreEqual(2, wheat.FeedUnitsPer10);
            Assert.AreEqual(1, wheat.BranUnitsPer10);

            var corn = GristMillConversionData.ProfileFor(CropKind.Corn);
            Assert.IsNotNull(corn);
            Assert.AreEqual("meal", corn.MainProductKind);

            Assert.IsNull(GristMillConversionData.ProfileFor(CropKind.Hay), "hay is not ground");
        }

        private static int CustomerUnits(GrainMillShopRuntime mill, string customerId, string productKind)
        {
            int total = 0;
            foreach (var custody in mill.TollOutbox)
            {
                if (custody != null && custody.Lot != null
                    && string.Equals(custody.CustomerId, customerId, System.StringComparison.Ordinal)
                    && string.Equals(custody.Lot.ProductKind, productKind, System.StringComparison.OrdinalIgnoreCase))
                {
                    total += custody.Lot.QuantityUnits;
                }
            }
            return total;
        }
    }
}
