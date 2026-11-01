using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Hotel;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W3A: housekeeping turns over linen with provenance, queues the
    /// dirty sets, and launders them with real soap and real labor —
    /// limited by the scarcest of dirty sets, soap, and labor minutes.
    /// </summary>
    [TestFixture]
    public sealed class HotelHousekeepingTests
    {
        private static (HotelHousekeeping, List<string>) NewHousekeeping()
        {
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            var hk = new HotelHousekeeping(registry);
            hk.ApplyOpeningLinenEndowment(1, diag);
            return (hk, diag);
        }

        [Test]
        public void TurnOver_DispensesOneSet_QueuesDirty_WithProvenance()
        {
            var (hk, diag) = NewHousekeeping();
            int before = hk.LinenStock.UnitsOnHand(HotelConsumableSupply.LinenMaterialId);

            var lines = hk.TurnOverForGuestNight(2, diag);
            Assert.IsNotNull(lines);
            Assert.AreEqual(before - 1, hk.LinenStock.UnitsOnHand(HotelConsumableSupply.LinenMaterialId));
            Assert.AreEqual(1, hk.DirtySetsCount);
            Assert.IsFalse(string.IsNullOrWhiteSpace(lines[0].ProvenanceChain));
            Assert.AreEqual(1, hk.DirtyLinen.Count);
            Assert.IsTrue(hk.DirtyLinen[0].SourceProvenanceChain.Contains("BOOTSTRAP"),
                "dirty set remembers its bootstrap lot");
        }

        [Test]
        public void TurnOver_Shortfall_ReturnsNull_DiagnosticsLoud()
        {
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            var hk = new HotelHousekeeping(registry); // no endowment: empty shelf
            Assert.IsNull(hk.TurnOverForGuestNight(2, diag));
            Assert.AreEqual(0, hk.DirtySetsCount);
        }

        [Test]
        public void Launder_ConsumesSoapAndLabor_ReturnsCleanSetsWithProvenance()
        {
            var (hk, diag) = NewHousekeeping();
            hk.TurnOverForGuestNight(2, diag);
            hk.TurnOverForGuestNight(2, diag);
            int soapBefore = hk.LinenStock.UnitsOnHand(HotelConsumableSupply.SoapMaterialId);
            int linenBefore = hk.LinenStock.UnitsOnHand(HotelConsumableSupply.LinenMaterialId);

            int washed = hk.Launder(3, 90, out int laborConsumed, diag);
            Assert.AreEqual(2, washed);
            Assert.AreEqual(2 * HotelHousekeeping.LaborMinutesPerLinenSet, laborConsumed);
            Assert.AreEqual(soapBefore - 2 * HotelHousekeeping.SoapPortionsPerLinenSet,
                hk.LinenStock.UnitsOnHand(HotelConsumableSupply.SoapMaterialId));
            Assert.AreEqual(linenBefore + 2, hk.LinenStock.UnitsOnHand(HotelConsumableSupply.LinenMaterialId));
            Assert.AreEqual(0, hk.DirtySetsCount);

            var lastLot = hk.LinenStock.Lots[hk.LinenStock.Lots.Count - 1];
            Assert.IsTrue(lastLot.IsLaundryReturn);
            Assert.IsTrue(lastLot.ProvenanceChain().Contains("LAUNDRY return"));
            Assert.IsTrue(lastLot.ProvenanceChain().Contains("BOOTSTRAP"),
                "laundry return names its source lots");
        }

        [Test]
        public void Launder_LimitedByLaborMinutes()
        {
            var (hk, diag) = NewHousekeeping();
            hk.TurnOverForGuestNight(2, diag);
            hk.TurnOverForGuestNight(2, diag);

            int washed = hk.Launder(3, 20, out int laborConsumed, diag);
            Assert.AreEqual(0, washed, "20 minutes buys no 30-minute wash");
            Assert.AreEqual(0, laborConsumed);
            Assert.AreEqual(2, hk.DirtySetsCount, "dirty linen waits for labor");
        }

        [Test]
        public void Launder_LimitedBySoap()
        {
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            var hk = new HotelHousekeeping(registry);
            // Endow linen but drain the soap first.
            HotelConsumableBootstrap.ApplyBootstrapEndowment(hk.LinenStock, registry, 1, diag);
            hk.LinenStock.TryDispenseUnits(HotelConsumableSupply.SoapMaterialId,
                hk.LinenStock.UnitsOnHand(HotelConsumableSupply.SoapMaterialId), 1, diag);

            hk.TurnOverForGuestNight(2, diag);
            int washed = hk.Launder(3, 1000, out _, diag);
            Assert.AreEqual(0, washed, "no soap, no wash");
            Assert.AreEqual(1, hk.DirtySetsCount);
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesStockAndDirtyQueue()
        {
            var (hk, diag) = NewHousekeeping();
            hk.TurnOverForGuestNight(2, diag);

            var dto = hk.CaptureSaveDto();
            var registry = new EntityIdRegistry();
            var reloaded = new HotelHousekeeping(registry);
            reloaded.LoadFromSaveDto(dto);

            Assert.AreEqual(hk.DirtySetsCount, reloaded.DirtySetsCount);
            Assert.AreEqual(
                hk.LinenStock.UnitsOnHand(HotelConsumableSupply.LinenMaterialId),
                reloaded.LinenStock.UnitsOnHand(HotelConsumableSupply.LinenMaterialId));
        }
    }
}
