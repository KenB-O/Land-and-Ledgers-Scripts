using System.Collections.Generic;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy.Farming
{
    /// <summary>
    /// CRP-3: the grain chain. Dealer aggregation bears real working-capital
    /// exposure; the elevator stores with finite capacity; the miller splits
    /// grain into flour/feed/bran lots; the feed merchant closes the loop to
    /// livestock farms. Middlemen are capabilities (Canon R6), not classes.
    /// </summary>
    [TestFixture]
    public sealed class GrainChainTests
    {
        private CropLot GrainLot(int units)
        {
            return new CropLot
            {
                LotId = EntityId.For(EntityKind.Lot, 5001),
                Crop = CropKind.Wheat,
                ProductKind = "grain",
                QuantityUnits = units,
                FieldId = "wheat-40",
                FarmId = "farm-1",
                HarvestDayIndex = 200,
                SeedSource = "general store",
            };
        }

        [Test]
        public void Dealer_BuyGrainRespectsWorkingCapital()
        {
            var dealer = new GrainDealer("dealer-1", workingCapitalCents: 1000);
            var ledger = new HouseholdLedger(7);
            var diagnostics = new List<string>();

            // 100 units at 50c = 5000c > 1000c capital: refused.
            string problem = dealer.BuyGrain(GrainLot(100), 50, 210, ledger, diagnostics);
            Assert.IsNotNull(problem, "The dealer bears real capital exposure (Canon R6 §1.3).");
            Assert.AreEqual(0, dealer.InventoryUnits);

            // Affordable purchase works and records provenance.
            Assert.IsNull(dealer.BuyGrain(GrainLot(10), 50, 210, ledger, diagnostics));
            Assert.AreEqual(10, dealer.InventoryUnits);
            Assert.AreEqual(500, dealer.WorkingCapitalCents, "Working capital drops by the purchase cost.");
        }

        [Test]
        public void Dealer_CannotSellWhatItDoesNotHold()
        {
            var dealer = new GrainDealer("dealer-1", 100000);
            var diagnostics = new List<string>();
            Assert.IsNull(dealer.SellGrain(50, 60, 215, null, diagnostics),
                "No phantom grain — the dealer cannot sell what it does not hold (MR-P001).");
        }

        [Test]
        public void Elevator_StorageIsFinite()
        {
            var elevator = new GrainElevator("elevator-1", capacityUnits: 100);
            var diagnostics = new List<string>();

            Assert.IsNull(elevator.ReceiveGrain(GrainLot(80), 210, diagnostics));
            string problem = elevator.ReceiveGrain(GrainLot(50), 211, diagnostics);
            Assert.IsNotNull(problem, "Storage capacity is real (Canon §7.4L).");
            Assert.AreEqual(80, elevator.StoredUnits);
        }

        [Test]
        public void Miller_SplitsGrainIntoFlourFeedBran()
        {
            var ids = new EntityIdRegistry();
            var miller = new Miller("mill-1");
            var diagnostics = new List<string>();

            MillingBatch batch = miller.MillGrain(ids, GrainLot(100), EntityId.Invalid, 220, diagnostics);
            Assert.IsNotNull(batch);
            Assert.AreEqual(70, batch.FlourUnits);
            Assert.AreEqual(20, batch.FeedUnits);
            Assert.AreEqual(10, batch.BranUnits, "Mill ratios are calibration (Canon Part XV).");

            MillLot flour = miller.TakeProduct(ids, batch, "flour", diagnostics);
            Assert.IsNotNull(flour);
            Assert.AreEqual(70, flour.QuantityUnits);
            Assert.AreEqual(batch.GrainLotId, flour.SourceGrainLotId, "Mill lots carry upstream provenance.");
        }

        [Test]
        public void FeedMerchant_ClosesTheLoopToLivestockFarms()
        {
            var merchant = new MillFeedSupplier("feed-store-1", "Feed Store", pricePerUnitCents: 5);
            var diagnostics = new List<string>();

            // Orphan restock refused.
            Assert.IsNotNull(merchant.RestockFeed(100, "", 220), "Feed must name its upstream — no orphan inputs.");

            Assert.IsNull(merchant.RestockFeed(100, "mill mill-1 ← dealer dealer-1 ← farm farm-1", 220));
            var feedLoop = new FeedLoop(0);
            var ledger = new HouseholdLedger(9);
            int bought = FeedMarket.BuyFeed(feedLoop, merchant, 40, 221, ledger, diagnostics);
            Assert.AreEqual(40, bought);
            Assert.AreEqual(40, feedLoop.FeedStockUnits, "Bought feed lands as farm stock, never a stat boost.");
        }

        [Test]
        public void FeedOwnGrain_IsInternalUseNotASale()
        {
            var feedLoop = new FeedLoop(0);
            var diagnostics = new List<string>();
            CropLot grain = GrainLot(100);

            int fed = GrainChainEconomics.FeedOwnGrain(feedLoop, grain, 30, 222, diagnostics);
            Assert.AreEqual(30, fed);
            Assert.AreEqual(70, grain.QuantityUnits);
            Assert.AreEqual(30, feedLoop.FeedStockUnits);
            StringAssert.Contains("no sale", feedLoop.LastFeedSource,
                "Own grain to the trough is internal use — no money moves, no phantom revenue.");
        }

        [Test]
        public void GrainCapabilities_RegisterAsData()
        {
            var registry = new BusinessCapabilityRegistry();
            var diagnostics = new List<string>();
            GrainCapabilities.RegisterAll(registry, diagnostics);
            Assert.IsTrue(registry.TryGet(GrainCapabilities.GrainDealingCapabilityId, out _));
            Assert.IsTrue(registry.TryGet(GrainCapabilities.GrainStorageCapabilityId, out _));
            Assert.IsTrue(registry.TryGet(GrainCapabilities.MillingCapabilityId, out _));
            Assert.IsTrue(registry.TryGet(GrainCapabilities.FeedRetailCapabilityId, out _));
        }

        [Test]
        public void Miller_RegistersSkillAndTask()
        {
            var skills = new SkillService();
            var diagnostics = new List<string>();
            Miller.RegisterSkills(skills, diagnostics);
            Assert.IsNotNull(skills.GetSkill(Miller.MillingSkillId),
                "Milling registers through the TTS-3 extension path.");

            var tasks = new TaskAuthority();
            Miller.RegisterTaskDefinitions(tasks);
            Assert.IsNotNull(tasks.GetDefinition(CropChain.MillGrainTaskId));
        }
    }
}
