using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Tailor;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1C: the cloth shelf's reorder policy. The bootstrap endowment is
    /// one-time and lots never auto-replenish (upstream-provenance doctrine),
    /// so the shop raises visible reorder signals before the shelf runs dry.
    /// Signals are DATA — ordering is the caller's real import order.
    /// </summary>
    [TestFixture]
    public sealed class TailorClothReorderTests
    {
        private static TailorClothStock BootstrappedStock(List<string> diag)
        {
            var stock = new TailorClothStock();
            var registry = new EntityIdRegistry();
            TailorClothBootstrap.ApplyBootstrapEndowment(stock, registry, 290, diag);
            return stock;
        }

        [Test]
        public void HealthyShelf_NoSignal()
        {
            var diag = new List<string>();
            var stock = BootstrappedStock(diag);

            var signals = TailorClothReorderEvaluator.EvaluateReorderSignals(
                stock, new TailorClothReorderPolicy(), 300, diag);

            Assert.AreEqual(0, signals.Count, "30 cloth yards and 20 notions are above threshold.");
        }

        [Test]
        public void LowCloth_SignalsClothOnly()
        {
            var diag = new List<string>();
            var stock = BootstrappedStock(diag);
            var drained = stock.TryDispenseUnits(TailorGarmentCatalog.ClothItemId, 25, 300, diag);
            Assert.NotNull(drained);

            var signals = TailorClothReorderEvaluator.EvaluateReorderSignals(
                stock, new TailorClothReorderPolicy(), 300, diag);

            Assert.AreEqual(1, signals.Count);
            TailorClothReorderSignal signal = signals[0];
            Assert.AreEqual(TailorGarmentCatalog.ClothItemId, signal.MaterialName);
            Assert.AreEqual(5, signal.UnitsOnHand);
            Assert.AreEqual(9, signal.ThresholdUnits);
            Assert.AreEqual(30, signal.SuggestedOrderUnits);
            Assert.AreEqual(300, signal.DayIndex);
        }

        [Test]
        public void LowNotions_SignalsNotionsOnly()
        {
            var diag = new List<string>();
            var stock = BootstrappedStock(diag);
            var drained = stock.TryDispenseUnits(TailorGarmentCatalog.NotionsItemId, 16, 300, diag);
            Assert.NotNull(drained);

            var signals = TailorClothReorderEvaluator.EvaluateReorderSignals(
                stock, new TailorClothReorderPolicy(), 300, diag);

            Assert.AreEqual(1, signals.Count);
            Assert.AreEqual(TailorGarmentCatalog.NotionsItemId, signals[0].MaterialName);
            Assert.AreEqual(4, signals[0].UnitsOnHand);
        }

        [Test]
        public void Signal_CarriesLeadTimeAndLoudReason()
        {
            var diag = new List<string>();
            var stock = BootstrappedStock(diag);
            stock.TryDispenseUnits(TailorGarmentCatalog.ClothItemId, 25, 300, diag);
            stock.TryDispenseUnits(TailorGarmentCatalog.NotionsItemId, 18, 300, diag);

            var signals = TailorClothReorderEvaluator.EvaluateReorderSignals(
                stock, new TailorClothReorderPolicy(), 300, diag);

            Assert.AreEqual(2, signals.Count);
            foreach (var signal in signals)
            {
                Assert.AreEqual(TailorClothReorderEvaluator.ImportLeadTimeDays, signal.LeadTimeDays,
                    "Lead time matches the real import transit (TailorClothSupply: 14 days via railhead).");
                StringAssert.Contains("import order", signal.Reason,
                    "The signal points at a real import order — lots never auto-replenish.");
            }

            bool loud = false;
            foreach (string line in diag)
            {
                if (line.Contains("REORDER SIGNAL")) loud = true;
            }

            Assert.IsTrue(loud, "Reorder signals are loud diagnostics.");
        }

        [Test]
        public void CustomThresholds_AreHonored()
        {
            var diag = new List<string>();
            var stock = BootstrappedStock(diag);
            var policy = new TailorClothReorderPolicy { ClothThresholdYards = 40, NotionsThresholdUnits = 25 };

            var signals = TailorClothReorderEvaluator.EvaluateReorderSignals(stock, policy, 300, diag);

            Assert.AreEqual(2, signals.Count, "Shop-settable thresholds drive the signals.");
        }

        [Test]
        public void NullStock_EvaluatesEmpty_NoCrash()
        {
            var diag = new List<string>();
            var signals = TailorClothReorderEvaluator.EvaluateReorderSignals(
                null, new TailorClothReorderPolicy(), 300, diag);
            Assert.AreEqual(0, signals.Count);
        }
    }
}
