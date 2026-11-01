using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Hotel;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D2A: the hotel's heating-fuel wood store (Canon §8.1A capacity
    /// bounded by heating; §8.1E heating as work). Cordwood lots carry
    /// upstream provenance (fuel dealer or import order) or the explicit
    /// bootstrap flag; burns are FIFO and atomic; the runtime burns one
    /// unit per occupied bed-night and runs cold loudly when short.
    /// </summary>
    [TestFixture]
    public sealed class HotelFuelTests
    {
        private static HotelFuelLot DealerLot(EntityIdRegistry registry, int units)
        {
            return new HotelFuelLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                FuelName = HotelFuelSupply.FuelMaterialId,
                Units = units,
                AcquiredDayIndex = 5,
                FuelDealerBusinessId = "fuel-dealer-1",
                SourceFuelLotId = "dealer-yard-lot-7",
            };
        }

        [Test]
        public void ReceiveLot_RefusesOrphanFuel_DemandsProvenance()
        {
            var stock = new HotelFuelStock();
            var diag = new List<string>();
            var registry = new EntityIdRegistry();
            Assert.IsNull(stock.ReceiveLot(DealerLot(registry, 40), diag));
            var orphan = new HotelFuelLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                FuelName = HotelFuelSupply.FuelMaterialId,
                Units = 10,
                AcquiredDayIndex = 6,
            };
            Assert.IsNotNull(stock.ReceiveLot(orphan, diag), "orphan fuel refused — no synthetic stock");
            Assert.AreEqual(40, stock.UnitsOnHand(HotelFuelSupply.FuelMaterialId));
        }

        [Test]
        public void TryBurnUnits_IsAtomic_AndFifo()
        {
            var stock = new HotelFuelStock();
            var diag = new List<string>();
            var registry = new EntityIdRegistry();
            stock.ReceiveLot(DealerLot(registry, 10), diag);
            stock.ReceiveLot(DealerLot(registry, 10), diag);

            Assert.IsNull(stock.TryBurnUnits(HotelFuelSupply.FuelMaterialId, 30, 8, diag), "a shortfall burns nothing");
            Assert.AreEqual(20, stock.UnitsOnHand(HotelFuelSupply.FuelMaterialId));

            var lines = stock.TryBurnUnits(HotelFuelSupply.FuelMaterialId, 15, 8, diag);
            Assert.IsNotNull(lines);
            Assert.AreEqual(5, stock.UnitsOnHand(HotelFuelSupply.FuelMaterialId));
            Assert.IsTrue(lines[0].ProvenanceChain.Contains("fuel dealer fuel-dealer-1"));
        }

        [Test]
        public void ExecuteDay_BurnsHeatPerBedNight_AndRunsColdLoudly()
        {
            var diag = new List<string>();
            var hotel = new HotelShopRuntime("hotel-inst-fuel", new EntityIdRegistry());
            hotel.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            hotel.AddRoom(HotelRoomClass.DoubleRoom, 2, diag);
            hotel.ApplyOpeningLinenEndowment(1, diag);
            string single = hotel.RoomInventory.Rooms[0].RoomNumber;
            hotel.CheckInGuest(1201, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Weekly, 10, 0, diag);

            // No fuel endowment: the house runs cold, loudly.
            hotel.ExecuteDay(10, 60, diag);
            Assert.IsTrue(diag[diag.Count - 1].Contains("house cold: True"), "the cold night is on the record");

            // With fuel: the heat burns cleanly.
            hotel.ApplyOpeningFuelEndowment(10, diag);
            hotel.ExecuteDay(11, 60, diag);
            Assert.AreEqual(HotelFuelSupply.BootstrapFuelUnits - 1,
                hotel.FuelStock.UnitsOnHand(HotelFuelSupply.FuelMaterialId),
                "one cordwood unit burned for one occupied bed-night");
        }

        [Test]
        public void BootstrapEndowment_IsOneTime_AndMarked()
        {
            var diag = new List<string>();
            var hotel = new HotelShopRuntime("hotel-inst-fuel2", new EntityIdRegistry());
            hotel.ApplyOpeningFuelEndowment(3, diag);
            Assert.AreEqual(HotelFuelSupply.BootstrapFuelUnits, hotel.FuelStock.UnitsOnHand(HotelFuelSupply.FuelMaterialId));
            Assert.IsTrue(hotel.FuelStock.Lots[0].IsBootstrapEndowment);
            Assert.IsTrue(hotel.FuelStock.Lots[0].ProvenanceChain().Contains("BOOTSTRAP"));
        }

        [Test]
        public void SaveLoad_RoundTripsFuelLots()
        {
            var diag = new List<string>();
            var hotel = new HotelShopRuntime("hotel-inst-fuel3", new EntityIdRegistry());
            hotel.ApplyOpeningFuelEndowment(3, diag);
            var reloaded = new HotelShopRuntime("hotel-inst-fuel3", new EntityIdRegistry());
            reloaded.LoadFromSaveDto(hotel.CaptureSaveDto());
            Assert.AreEqual(HotelFuelSupply.BootstrapFuelUnits,
                reloaded.FuelStock.UnitsOnHand(HotelFuelSupply.FuelMaterialId));
        }
    }
}
