using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Tailor;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1C: the bespoke vs ready-made fork — ready-made workwear batching.
    /// Default mode is Disabled (W1C bespoke-only preserved); the WorkwearBatches
    /// mode sews ready-made work garments from real cloth, notions, labor, and
    /// table-days, emitting batch records the caller feeds to the clothing outlet.
    /// </summary>
    [TestFixture]
    public sealed class TailorReadyMadeWorkTests
    {
        private static Func<string, WorkstationComponentView?> TableFinder(params string[] goodAssetIds)
        {
            var good = new HashSet<string>(goodAssetIds);
            return assetId => good.Contains(assetId)
                ? (WorkstationComponentView?)new WorkstationComponentView
                {
                    AssetId = assetId,
                    Kind = "cutting-table",
                    Condition01 = 0.9f,
                    IsUsable = true,
                }
                : null;
        }

        private static TailorShopRuntime OneTableShop(List<string> diag)
        {
            var runtime = new TailorShopRuntime("tail-rm-1");
            var registry = new EntityIdRegistry();
            TailorClothBootstrap.ApplyBootstrapEndowment(runtime.ClothStock, registry, 290, diag);
            Assert.Null(runtime.AddCuttingTable("shop-floor", new List<string> { "table-asset-1" }, diag));
            return runtime;
        }

        private static int Work(TailorShopRuntime runtime, int day, List<string> diag, params string[] tableAssets)
        {
            return runtime.WorkDay(day, true,
                WorkstationCatalog.TailorCuttingTableStation, TableFinder(tableAssets), diag);
        }

        [Test]
        public void ReadyMadeMode_DefaultsToDisabled()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            Assert.AreEqual(TailorReadyMadeMode.Disabled, runtime.ReadyMadeMode,
                "Canon is silent on the bespoke/ready-made split — the shop defaults to W1C bespoke-only.");
        }

        [Test]
        public void DisabledMode_SewsNothingEvenWithIdleShop()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);

            int advances = Work(runtime, 300, diag, "table-asset-1");

            Assert.AreEqual(0, advances);
            Assert.AreEqual(0, runtime.ReadyMadeBatches.Count);
            Assert.AreEqual(30, runtime.ClothStock.UnitsOnHand(TailorGarmentCatalog.ClothItemId),
                "No cloth moves while ready-made work is disabled.");
        }

        [Test]
        public void WorkwearBatches_SewsOneGarmentPerTableDay_FromRealLots()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            runtime.SetReadyMadeMode(TailorReadyMadeMode.WorkwearBatches, diag);

            int advances = Work(runtime, 300, diag, "table-asset-1");

            Assert.AreEqual(1, advances);
            Assert.AreEqual(1, runtime.ReadyMadeBatches.Count);
            TailorReadyMadeBatch batch = runtime.ReadyMadeBatches[0];
            Assert.AreEqual(1, batch.UnitsSewn, "One table → one bench garment per day (W1C table model).");
            Assert.AreEqual(TailorGarmentCatalog.ShirtSewMinutes + TailorGarmentCatalog.ShirtPressMinutes,
                batch.LaborMinutesSpent, "Ready-made work is sew + press only — no measuring, no fittings.");
            Assert.AreEqual(1, batch.TableDaysUsed);
            Assert.AreEqual(30 - TailorGarmentCatalog.ShirtClothYards,
                runtime.ClothStock.UnitsOnHand(TailorGarmentCatalog.ClothItemId));
            Assert.AreEqual(20 - TailorGarmentCatalog.StandardNotionsUnits,
                runtime.ClothStock.UnitsOnHand(TailorGarmentCatalog.NotionsItemId));
            Assert.Greater(batch.ClothProvenance.Count, 0);
            Assert.Greater(batch.NotionsProvenance.Count, 0);
            foreach (var line in batch.ClothProvenance)
                StringAssert.Contains("BOOTSTRAP", line.ProvenanceChain,
                    "Every ready-made yard traces to its lot.");
        }

        [Test]
        public void WorkwearBatches_TwoTables_SewsTwoGarments()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            Assert.Null(runtime.AddCuttingTable("shop-floor", new List<string> { "table-asset-2" }, diag));
            runtime.SetReadyMadeMode(TailorReadyMadeMode.WorkwearBatches, diag);

            Work(runtime, 300, diag, "table-asset-1", "table-asset-2");

            Assert.AreEqual(1, runtime.ReadyMadeBatches.Count);
            Assert.AreEqual(2, runtime.ReadyMadeBatches[0].UnitsSewn,
                "Two table-days → two garments; labor (600m) is not the binding constraint.");
        }

        [Test]
        public void WorkwearBatches_ClothShortfall_ParksLoudly_NothingConjured()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            runtime.SetReadyMadeMode(TailorReadyMadeMode.WorkwearBatches, diag);

            // Drain the shelf below one garment's yardage.
            var drained = runtime.ClothStock.TryDispenseUnits(
                TailorGarmentCatalog.ClothItemId, 28, 299, diag);
            Assert.NotNull(drained);
            Assert.AreEqual(2, runtime.ClothStock.UnitsOnHand(TailorGarmentCatalog.ClothItemId));

            int advances = Work(runtime, 300, diag, "table-asset-1");

            Assert.AreEqual(0, advances);
            Assert.AreEqual(0, runtime.ReadyMadeBatches.Count,
                "No cloth for a garment means no garments — never conjured.");
            Assert.AreEqual(2, runtime.ClothStock.UnitsOnHand(TailorGarmentCatalog.ClothItemId));
            bool loud = false;
            foreach (string line in diag)
            {
                if (line.Contains("nothing sewable")) loud = true;
            }

            Assert.IsTrue(loud, "The shortfall must be loud, not silent.");
        }

        [Test]
        public void SewBatch_StaticCall_RespectsLaborAndTables()
        {
            var diag = new List<string>();
            var registry = new EntityIdRegistry();
            var stock = new TailorClothStock();
            TailorClothBootstrap.ApplyBootstrapEndowment(stock, registry, 290, diag);

            var tableFree = new Dictionary<int, bool> { { 0, true }, { 1, true } };
            int labor = 300; // only enough for one garment (260m)
            var batch = TailorReadyMadeProduction.SewBatch(
                stock, new TailorReadyMadePolicy(), TailorReadyMadeMode.WorkwearBatches,
                ref labor, tableFree, "rm-test-1", 300, diag);

            Assert.NotNull(batch);
            Assert.AreEqual(1, batch.UnitsSewn, "300m of labor binds before the two free tables do.");
            Assert.AreEqual(40, labor);
            Assert.IsFalse(tableFree[0]);
            Assert.IsTrue(tableFree[1], "Only the claimed table-day is consumed.");
        }

        [Test]
        public void SaveLoad_PreservesReadyMadeModeAndBatches()
        {
            var diag = new List<string>();
            var runtime = OneTableShop(diag);
            runtime.SetReadyMadeMode(TailorReadyMadeMode.WorkwearBatches, diag);
            Work(runtime, 300, diag, "table-asset-1");
            Assert.AreEqual(1, runtime.ReadyMadeBatches.Count);

            var restored = new TailorShopRuntime("tail-rm-1");
            restored.LoadFromSaveDto(runtime.CaptureSaveDto());

            Assert.AreEqual(TailorReadyMadeMode.WorkwearBatches, restored.ReadyMadeMode);
            Assert.AreEqual(1, restored.ReadyMadeBatches.Count);
            Assert.AreEqual(1, restored.ReadyMadeBatches[0].UnitsSewn);
            Assert.Greater(restored.ReadyMadeBatches[0].ClothProvenance.Count, 0,
                "Batch provenance survives the save round-trip.");
        }
    }
}
