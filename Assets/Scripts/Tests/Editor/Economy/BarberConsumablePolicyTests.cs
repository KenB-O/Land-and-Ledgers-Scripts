using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Barber;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1B: the barber's consumable reorder policy. The bootstrap endowment is
    /// one-time and lots never auto-replenish (upstream-provenance doctrine),
    /// so the shop needs a visible reorder signal before the shelf runs dry.
    /// Signals are DATA — the caller places real import orders; the shelf is
    /// never silently replenished.
    /// </summary>
    [TestFixture]
    public sealed class BarberConsumablePolicyTests
    {
        private static BarberConsumableStock StockedShelf(List<string> diag, int soapUnits, int linenUnits)
        {
            var stock = new BarberConsumableStock();
            var registry = new EntityIdRegistry();
            Assert.Null(stock.ReceiveLot(new BarberConsumableLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                ConsumableName = BarberServiceCatalog.SoapItemId,
                Units = soapUnits,
                AcquiredDayIndex = 290,
                ImportOrderId = "IMP-1870-044",
                OriginName = "Off-map wholesale drug and toiletries house, via railhead",
            }, diag));
            Assert.Null(stock.ReceiveLot(new BarberConsumableLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                ConsumableName = BarberServiceCatalog.LinenItemId,
                Units = linenUnits,
                AcquiredDayIndex = 290,
                ImportOrderId = "IMP-1870-045",
                OriginName = "Off-map dry-goods wholesaler, via railhead",
            }, diag));
            return stock;
        }

        [Test]
        public void EvaluateReorderSignals_StockedShelf_NoSignal()
        {
            var diag = new List<string>();
            var stock = StockedShelf(diag, soapUnits: 40, linenUnits: 30);

            var signals = BarberConsumablePolicy.EvaluateReorderSignals(
                stock, new BarberConsumableReorderPolicy(), 300, diag);

            Assert.AreEqual(0, signals.Count, "Above both thresholds — no signal.");
        }

        [Test]
        public void EvaluateReorderSignals_BelowThreshold_SignalsWithLeadTime()
        {
            var diag = new List<string>();
            var stock = StockedShelf(diag, soapUnits: 5, linenUnits: 30);

            var signals = BarberConsumablePolicy.EvaluateReorderSignals(
                stock, new BarberConsumableReorderPolicy(), 300, diag);

            Assert.AreEqual(1, signals.Count, "Only the soap is low.");
            BarberConsumableReorderSignal signal = signals[0];
            Assert.AreEqual(BarberServiceCatalog.SoapItemId, signal.ConsumableName);
            Assert.AreEqual(5, signal.UnitsOnHand);
            Assert.AreEqual(12, signal.ThresholdUnits, "Default soap threshold.");
            Assert.AreEqual(40, signal.SuggestedOrderUnits, "Default soap order quantity.");
            Assert.AreEqual(14, signal.LeadTimeDays,
                "The signal carries the real import lead time (BarberConsumableSupply: 14 transit days via railhead).");
            Assert.AreEqual(300, signal.DayIndex);
            Assert.IsNotEmpty(signal.Reason);
        }

        [Test]
        public void EvaluateReorderSignals_BothLow_TwoSignals()
        {
            var diag = new List<string>();
            var stock = StockedShelf(diag, soapUnits: 5, linenUnits: 4);

            var signals = BarberConsumablePolicy.EvaluateReorderSignals(
                stock, new BarberConsumableReorderPolicy(), 300, diag);

            Assert.AreEqual(2, signals.Count);
        }

        [Test]
        public void EvaluateReorderSignals_CustomPolicy_Respected()
        {
            var diag = new List<string>();
            var stock = StockedShelf(diag, soapUnits: 5, linenUnits: 30);
            var policy = new BarberConsumableReorderPolicy { SoapThresholdUnits = 3, SoapOrderUnits = 100 };

            var signals = BarberConsumablePolicy.EvaluateReorderSignals(stock, policy, 300, diag);

            Assert.AreEqual(0, signals.Count, "5 units is above the custom threshold of 3.");
        }

        [Test]
        public void EvaluateReorderSignals_NeverReplenishes_NeverOrders()
        {
            var diag = new List<string>();
            var stock = StockedShelf(diag, soapUnits: 5, linenUnits: 4);

            var signals = BarberConsumablePolicy.EvaluateReorderSignals(
                stock, new BarberConsumableReorderPolicy(), 300, diag);

            Assert.AreEqual(2, signals.Count);
            Assert.AreEqual(5, stock.UnitsOnHand(BarberServiceCatalog.SoapItemId),
                "Evaluation is read-only — the shelf is never silently replenished.");
            Assert.AreEqual(4, stock.UnitsOnHand(BarberServiceCatalog.LinenItemId));
        }

        [Test]
        public void EvaluateReorderSignals_NullStockOrPolicy_NoCrash()
        {
            var diag = new List<string>();

            var noStock = BarberConsumablePolicy.EvaluateReorderSignals(null, null, 300, diag);
            Assert.AreEqual(0, noStock.Count);

            var stock = StockedShelf(diag, soapUnits: 40, linenUnits: 30);
            var nullPolicy = BarberConsumablePolicy.EvaluateReorderSignals(stock, null, 300, diag);
            Assert.AreEqual(0, nullPolicy.Count, "A null policy falls back to defaults.");
        }
    }
}
