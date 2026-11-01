using System.Collections.Generic;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// T1E: the two flagged upstream-provenance gaps — seed below the general store
    /// and timber below the sawmill. Every input traces to a real supplier or an
    /// explicit endowment; suppliers need sources too.
    /// </summary>
    [TestFixture]
    public sealed class SeedTimberChainTests
    {
        private static CropLot GrainHarvest(int units)
        {
            return new CropLot
            {
                LotId = EntityId.For(EntityKind.Lot, 9001),
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
        public void SeedSaving_HoldsBack_FromOwnHarvest_NoPhantomSeed()
        {
            var ids = new EntityIdRegistry();
            CropLot harvest = GrainHarvest(100);
            var diagnostics = new List<string>();

            SeedLot seed = SeedSaving.HoldBackSeed(ids, harvest, 12, 210, diagnostics);

            Assert.NotNull(seed);
            Assert.AreEqual(12, seed.Units);
            Assert.AreEqual(88, harvest.QuantityUnits, "Held-back seed never leaves the farm's grain.");
            Assert.IsTrue(seed.LotId.IsValid);
            Assert.IsTrue(seed.SourceDescription.Contains("farm-1"));
        }

        [Test]
        public void SeedSaving_Refuses_MoreThanHarvestHolds()
        {
            var ids = new EntityIdRegistry();
            CropLot harvest = GrainHarvest(5);
            var diagnostics = new List<string>();

            SeedLot seed = SeedSaving.HoldBackSeed(ids, harvest, 12, 210, diagnostics);

            Assert.IsNull(seed);
            Assert.AreEqual(5, harvest.QuantityUnits, "Failed hold-back touches nothing.");
        }

        [Test]
        public void SeedSupplier_RestocksOnly_FromNamedUpstream()
        {
            var supplier = new GeneralStoreSeedSupplier("store-1", "General Store");
            supplier.SetPrice(CropKind.Wheat, 40);

            string orphan = supplier.RestockSeed(CropKind.Wheat, 50, "", 210);
            Assert.NotNull(orphan, "Orphan restocks are refused.");
            Assert.AreEqual(0, supplier.SeedStockUnits(CropKind.Wheat));

            Assert.IsNull(supplier.RestockSeed(CropKind.Wheat, 50, "seed farm: Miller's Select", 210));
            Assert.AreEqual(50, supplier.SeedStockUnits(CropKind.Wheat));

            var diagnostics = new List<string>();
            Assert.AreEqual(20, supplier.SellSeed(CropKind.Wheat, 20, 211, diagnostics));
            Assert.AreEqual(30, supplier.SeedStockUnits(CropKind.Wheat));
            Assert.AreEqual(-1, supplier.SellSeed(CropKind.Oats, 5, 211, diagnostics), "No stock, no sale.");
        }

        [Test]
        public void TimberHarvest_FellsAgainstRights_AndDepletesStand()
        {
            var ids = new EntityIdRegistry();
            var stand = new TimberStand
            {
                StandId = "stand-1",
                DisplayName = "North Forty Pines",
                TimberRightsHolderId = "mill-1",
                Species = "white pine",
                StandingTimberUnits = 10,
            };
            var diagnostics = new List<string>();

            LogLot logs = TimberHarvest.FellLogs(ids, stand, "mill-1", 4,
                EntityId.For(EntityKind.Person, 3), 220, diagnostics);

            Assert.NotNull(logs);
            Assert.AreEqual(4, logs.LogUnits);
            Assert.AreEqual(6, stand.StandingTimberUnits);
            Assert.IsTrue(logs.LotId.IsValid);
            Assert.AreEqual("stand-1", logs.StandId);

            // Over-claim clamps to what's standing.
            LogLot more = TimberHarvest.FellLogs(ids, stand, "mill-1", 99,
                EntityId.For(EntityKind.Person, 3), 221, diagnostics);
            Assert.NotNull(more);
            Assert.AreEqual(6, more.LogUnits);
            Assert.AreEqual(0, stand.StandingTimberUnits);
        }

        [Test]
        public void TimberHarvest_Refuses_WithoutTimberRights()
        {
            var ids = new EntityIdRegistry();
            var stand = new TimberStand
            {
                StandId = "stand-1",
                TimberRightsHolderId = "mill-1",
                StandingTimberUnits = 10,
            };
            var diagnostics = new List<string>();

            LogLot logs = TimberHarvest.FellLogs(ids, stand, "poacher", 4,
                EntityId.For(EntityKind.Person, 3), 220, diagnostics);

            Assert.IsNull(logs, "Timber theft is a claim, not a harvest.");
            Assert.AreEqual(10, stand.StandingTimberUnits);
        }

        [Test]
        public void Sawmill_TurnsLogs_IntoTracedLumber()
        {
            var ids = new EntityIdRegistry();
            var mill = new Sawmill("mill-1", "Local Sawmill", lumberPerLog: 4);
            var logs = new LogLot
            {
                LotId = EntityId.For(EntityKind.Lot, 9100),
                LogUnits = 5,
                StandId = "stand-1",
                Species = "white pine",
            };
            var diagnostics = new List<string>();

            LumberLot lumber = mill.SawLogs(ids, logs,
                EntityId.For(EntityKind.Person, 4), 222, diagnostics);

            Assert.NotNull(lumber);
            Assert.AreEqual(20, lumber.LumberUnits);
            Assert.IsTrue(lumber.LotId.IsValid);
            Assert.AreEqual(logs.LotId.ToString(), lumber.SourceLogLotId);
            Assert.AreEqual(0, logs.LogUnits, "Sawn logs are consumed — one lot never in two places.");
        }

        [Test]
        public void LumberYard_RestocksOnly_FromNamedMill()
        {
            var yard = new LumberYard("yard-1", "Town Lumber Yard", 85);

            Assert.NotNull(yard.RestockLumber(100, "", 223), "Orphan restocks are refused.");
            Assert.AreEqual(0, yard.StockUnits);

            Assert.IsNull(yard.RestockLumber(100, "mill-1", 223));
            Assert.AreEqual(100, yard.StockUnits);
            Assert.AreEqual("mill-1", yard.UpstreamMill);

            var diagnostics = new List<string>();
            Assert.AreEqual(40, yard.SellLumber(40, 224, diagnostics));
            Assert.AreEqual(60, yard.StockUnits);
            Assert.AreEqual(-1, yard.SellLumber(999, 224, diagnostics));
            Assert.AreEqual(60, yard.StockUnits, "Oversell refused; stock untouched.");
        }
    }
}
