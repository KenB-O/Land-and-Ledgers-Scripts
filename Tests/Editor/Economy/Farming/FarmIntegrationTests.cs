using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Butcher;
using LandLedgers.Economy.Farming;
using LandLedgers.Economy.Farming.Bootstrap;
using LandLedgers.Economy.Farming.Delivery;
using LandLedgers.Economy.Farming.Dairy;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy.Farming
{
    /// <summary>
    /// FVS-4: vertical integration — chickens/eggs into produce sales, the feed
    /// loop with real suppliers, construction ordering, labor planning, the
    /// cull circle, and save round-trips. Upstream provenance: every input
    /// traces to a real supplier or an explicit bootstrap endowment.
    /// </summary>
    [TestFixture]
    public sealed class FarmIntegrationTests
    {
        private AnimalRegistry registry;
        private EntityIdRegistry ids;

        [SetUp]
        public void SetUp()
        {
            ids = new EntityIdRegistry();
            registry = new AnimalRegistry(ids);
        }

        private AnimalState NewHen()
        {
            var hen = registry.RegisterAnimal(AnimalSpecies.Chicken, "Plymouth Rock",
                AnimalSex.Female, AnimalOwnerKind.Business, "farm:test", 100, "test");
            hen.BiologicalState = AnimalBiologicalState.EligibleOrCycling;
            return hen;
        }

        private AnimalState NewCow()
        {
            var cow = registry.RegisterAnimal(AnimalSpecies.Cattle, "Shorthorn",
                AnimalSex.Female, AnimalOwnerKind.Business, "farm:test", 100, "test");
            cow.BiologicalState = AnimalBiologicalState.Lactating;
            cow.Parity = 2;
            return cow;
        }

        [Test]
        public void EggsFlow_HenToProduceLot()
        {
            var flock = new PoultryChain();
            var hen = NewHen();
            flock.RegisterHen(hen.AnimalId);
            var diagnostics = new List<string>();

            var rng = new System.Random(7);
            var batches = flock.LayDaily(registry, rng, 100, 3, 1f, diagnostics);
            Assert.Greater(batches.Count, 0, "A laying hen in spring should lay.");

            FarmProduceLot lot = flock.CollectBatchToProduceLot(registry, batches[0].BatchId,
                "farm:test", "Test Farm", 100, diagnostics);
            Assert.IsNotNull(lot, "Collect failed: " + string.Join(" | ", diagnostics));
            Assert.AreEqual("eggs", lot.ProductKind);
            Assert.Greater(lot.QuantityUnits, 0);
            Assert.IsTrue(lot.LotId.Contains(batches[0].BatchId.ToString()), "Lot keeps batch provenance.");

            // Eggs sell exactly once.
            diagnostics.Clear();
            Assert.IsNull(flock.CollectBatchToProduceLot(registry, batches[0].BatchId,
                "farm:test", "Test Farm", 100, diagnostics));
        }

        [Test]
        public void IncubationReservesEggs_FromSale()
        {
            var flock = new PoultryChain();
            var hen = NewHen();
            flock.RegisterHen(hen.AnimalId);
            var diagnostics = new List<string>();
            var batches = flock.LayDaily(registry, new System.Random(7), 100, 3, 1f, diagnostics);
            Assert.Greater(batches.Count, 0);

            Assert.IsNull(flock.MarkForIncubation(registry, batches[0].BatchId));
            Assert.IsNull(flock.CollectBatchToProduceLot(registry, batches[0].BatchId,
                "farm:test", "Test Farm", 100, diagnostics),
                "Incubation eggs are unavailable for sale (Canon §9.8).");

            var chicks = flock.HatchBatch(registry, batches[0].BatchId, 121, 1, 1, diagnostics);
            Assert.AreEqual(2, chicks.Count, "Hatch records explicit sexes — no silent drops (Tech X §2.3).");
        }

        [Test]
        public void FeedLoop_StarvesHonestly()
        {
            var dairy = new DairyChain();
            var cow = NewCow();
            dairy.RegisterCow(new DairyCowState(cow.AnimalId, 40, 1f));
            var feed = new FeedLoop(2); // almost no feed
            var diagnostics = new List<string>();

            int shortfall = feed.ConsumeDay(6, 0, FarmSeason.Winter, dairy,
                new List<EntityId> { cow.AnimalId }, null, diagnostics);

            Assert.Greater(shortfall, 0, "6 cows on 2 feed units in winter must starve.");
            Assert.Less(dairy.GetCowState(cow.AnimalId).Condition01, 1f,
                "Underfeeding drops condition → milk yields drop (Tech X §8.1).");
            Assert.IsTrue(diagnostics.Count > 0, "Shortfall must be loud.");
        }

        [Test]
        public void FeedMarket_BuysFromRealSupplier()
        {
            var feed = new FeedLoop(0);
            var store = new GeneralStoreFeedSupplier("store-1", "General Store", 4);
            Assert.IsNull(store.RestockFeed(100, "Grain Mill (ground from Miller's crop farm)", 90));
            var ledger = new HouseholdLedger(7);
            var diagnostics = new List<string>();

            int bought = FeedMarket.BuyFeed(feed, store, 40, 100, ledger, diagnostics);

            Assert.AreEqual(40, bought);
            Assert.AreEqual(40, feed.FeedStockUnits);
            Assert.AreEqual(-160, ledger.GetBalanceCents(), "Feed costs real money with provenance.");
            Assert.AreEqual(60, store.FeedStockUnits, "Supplier stock is finite.");
        }

        [Test]
        public void FeedSupplier_RefusesOrphanRestock()
        {
            var store = new GeneralStoreFeedSupplier("store-1", "General Store", 4);
            string rejection = store.RestockFeed(100, "", 90);
            Assert.IsNotNull(rejection, "Feed without a named upstream source is an orphan input — refused.");
        }

        [Test]
        public void Construction_EnforcesMaterialsFirst()
        {
            var project = new FarmConstructionProject("coop-1", FarmConstructionKind.Coop, "second coop");
            var diagnostics = new List<string>();

            string refusal = FarmConstruction.StartConstruction(project,
                EntityId.For(EntityKind.Person, 3), 100, diagnostics);
            Assert.IsNotNull(refusal, "Construction without materials must be refused — ordering enforced.");
            Assert.AreEqual(FarmConstructionStatus.Planned, project.Status);

            var lumberYard = new TestLumberYard(1000, 2);
            var ledger = new HouseholdLedger(7);
            Assert.IsNull(FarmConstruction.BuyMaterials(project, lumberYard, 100, ledger, diagnostics));
            Assert.IsTrue(project.MaterialsReady);

            Assert.IsNull(FarmConstruction.StartConstruction(project,
                EntityId.For(EntityKind.Person, 3), 101, diagnostics));
            Assert.AreEqual(FarmConstructionStatus.InProgress, project.Status);

            var building = FarmConstruction.CompleteConstruction(project, 110, diagnostics);
            Assert.IsNotNull(building);
            Assert.AreEqual(FarmBuildingKind.Coop, building.Kind);
            Assert.IsTrue(building.Condition.Contains("lumber"), "Building carries material provenance.");
            Assert.Less(ledger.GetBalanceCents(), 0, "Lumber was purchased, not conjured.");
        }

        [Test]
        public void LaborPlan_AssignsRealPeopleAndReportsOverload()
        {
            var plan = new FarmLaborPlan();
            plan.Workers.Add(new FarmWorker(EntityId.For(EntityKind.Person, 1), "Operator", FarmWorkerKind.Family, 120));

            var day = plan.PlanDay(lactatingCows: 6, totalCattle: 8, hens: 12,
                churnBatches: 2, deliveryHandling: true, constructionActive: true, feedHarvestToday: 1);

            Assert.Greater(day.Tasks.Count, 0, "Some tasks must be assigned.");
            foreach (var task in day.Tasks)
            {
                Assert.IsTrue(task.AssignedWorkerId.IsValid, "Every planned task needs a real worker.");
            }
            Assert.IsTrue(day.Overloaded, "120 minutes cannot cover a full dairy day — overload must be reported, not hidden.");
            Assert.Greater(day.Unassigned.Count, 0, "Unassigned work goes to the overload queue (TTS-2).");
        }

        [Test]
        public void CullFlow_ClosesTheCircle()
        {
            var cow = NewCow(); // owned by farm:test
            var butcher = new ButcherRuntime("butcher-1", "yard", "counter");
            var ledger = new HouseholdLedger(7);
            var diagnostics = new List<string>();

            FarmLivestockSale sale = CullFlow.CullAndSellCow(registry, cow.AnimalId, "age: 12, dried off",
                "test", "Test Farm", butcher, "butcher-1", 4500, 100, ledger, diagnostics);

            Assert.IsNotNull(sale, "Cull sale failed: " + string.Join(" | ", diagnostics));
            Assert.AreEqual(AnimalCommercialStatus.Cull, cow.CommercialStatus);
            Assert.Greater(ledger.GetBalanceCents(), 0, "Cull proceeds post with provenance.");
        }

        [Test]
        public void CullFlow_RefusesForeignAnimals()
        {
            var stranger = registry.RegisterAnimal(AnimalSpecies.Cattle, "Shorthorn",
                AnimalSex.Female, AnimalOwnerKind.Business, "farm:other", 100, "test");
            var diagnostics = new List<string>();

            var sale = CullFlow.CullAndSellCow(registry, stranger.AnimalId, "age",
                "test", "Test Farm", new ButcherRuntime("b", "y", "c"), "butcher-1",
                4500, 100, new HouseholdLedger(7), diagnostics);

            Assert.IsNull(sale, "Cannot cull another farm's animal.");
        }

        [Test]
        public void FarmSlice_SaveRoundTrips()
        {
            var systems = new FarmSliceSystems();
            var cow = NewCow();
            systems.Dairy.RegisterCow(new DairyCowState(cow.AnimalId, 40, 1f));
            systems.SetFeedLoop(new FeedLoop(77));
            var hen = NewHen();
            systems.Poultry.RegisterHen(hen.AnimalId);
            systems.DeliveryJobs.Add(new DeliveryJob { JobId = "delivery-test", Status = DeliveryJobStatus.Planned });

            var dto = systems.CaptureSaveDto();
            var restored = new FarmSliceSystems();
            restored.LoadFromSaveDto(dto);

            Assert.IsNotNull(restored.Dairy.GetCowState(cow.AnimalId), "Dairy cow state must round-trip.");
            Assert.AreEqual(77, restored.Feed.FeedStockUnits, "Feed stock must round-trip.");
            Assert.AreEqual(1, restored.Poultry.HenCount, "Flock must round-trip.");
            Assert.AreEqual(1, restored.DeliveryJobs.Count, "Delivery jobs must round-trip.");
        }

        [Test]
        public void Seasons_ShapeTheLoop()
        {
            Assert.AreEqual(FarmSeason.Winter, FarmSeasons.SeasonForDayIndex(300));
            Assert.Greater(FarmSeasons.FeedMultiplierFor(FarmSeason.Winter),
                FarmSeasons.FeedMultiplierFor(FarmSeason.Summer), "Winter feed costs more.");
            Assert.AreEqual(0f, FarmSeasons.PastureGrazingShare(FarmSeason.Winter), "No winter grazing.");
            Assert.Less(FarmSeasons.LayingFactorFor(FarmSeason.Winter),
                FarmSeasons.LayingFactorFor(FarmSeason.Spring), "Laying is seasonal (Canon §9.8).");
            Assert.IsTrue(FarmSeasons.ShouldDryOff(310), "Long lactations should end in dry-off.");
        }

        private sealed class TestLumberYard : ILumberSupplier
        {
            public string SupplierBusinessId => "lumber-1";
            public string SupplierName => "Lumber Yard";
            public int LumberStockUnits { get; private set; }
            public int PricePerUnitCents { get; private set; }

            public TestLumberYard(int stock, int price)
            {
                LumberStockUnits = stock;
                PricePerUnitCents = price;
            }

            public int SellLumber(int requestedUnits, int dayIndex, List<string> diagnostics)
            {
                int sold = System.Math.Min(requestedUnits, LumberStockUnits);
                LumberStockUnits -= sold;
                return sold;
            }
        }
    }
}
