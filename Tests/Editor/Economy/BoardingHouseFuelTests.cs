using System.Collections.Generic;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1F: the boarding house's stove-fuel store (Canon §8.1B fuel demand).
    /// Cordwood lots carry upstream provenance (fuel dealer, import order,
    /// or marked bootstrap); burns are atomic FIFO; the kitchen refuses to
    /// cook on a cold stove, loudly. Mirrors the D1E restaurant fuel tests.
    /// </summary>
    [TestFixture]
    public sealed class BoardingHouseFuelTests
    {
        private BoardingHouseFuelStock NewStock(int dayIndex, List<string> diag, out EntityIdRegistry registry)
        {
            registry = new EntityIdRegistry();
            var stock = new BoardingHouseFuelStock();
            BoardingHouseFuelBootstrap.ApplyBootstrapEndowment(stock, registry, dayIndex, diag);
            return stock;
        }

        private BoardingHouseFuelLot DealerLot(EntityIdRegistry registry, int units, int dayIndex)
        {
            return new BoardingHouseFuelLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                FuelName = BoardingHouseFuelSupply.FuelMaterialId,
                Units = units,
                AcquiredDayIndex = dayIndex,
                FuelDealerBusinessId = "fuel-yard-1",
                SourceFuelLotId = "dealer-lot-7",
            };
        }

        [Test]
        public void ReceiveLot_EnforcesProvenance()
        {
            var diag = new List<string>();
            var registry = new EntityIdRegistry();
            var stock = new BoardingHouseFuelStock();

            var orphan = new BoardingHouseFuelLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                FuelName = BoardingHouseFuelSupply.FuelMaterialId,
                Units = 10,
                AcquiredDayIndex = 100,
            };
            Assert.NotNull(stock.ReceiveLot(orphan, diag), "orphan fuel refused — no synthetic stock");

            Assert.Null(stock.ReceiveLot(DealerLot(registry, 10, 100), diag));
            Assert.AreEqual(10, stock.UnitsOnHand(BoardingHouseFuelSupply.FuelMaterialId));
        }

        [Test]
        public void TryBurnUnits_IsAtomicFifo()
        {
            var diag = new List<string>();
            var stock = NewStock(100, diag, out EntityIdRegistry registry);
            Assert.Null(stock.ReceiveLot(DealerLot(registry, 10, 101), diag));
            int before = stock.UnitsOnHand(BoardingHouseFuelSupply.FuelMaterialId);

            Assert.IsNull(stock.TryBurnUnits(BoardingHouseFuelSupply.FuelMaterialId, before + 1, 102, diag),
                "shortfall burns nothing");
            Assert.AreEqual(before, stock.UnitsOnHand(BoardingHouseFuelSupply.FuelMaterialId), "atomic: nothing burned");

            var lines = stock.TryBurnUnits(BoardingHouseFuelSupply.FuelMaterialId, 5, 102, diag);
            Assert.IsNotNull(lines);
            Assert.AreEqual(before - 5, stock.UnitsOnHand(BoardingHouseFuelSupply.FuelMaterialId));
            StringAssert.Contains("BOOTSTRAP", lines[0].ProvenanceChain, "FIFO: oldest (bootstrap) lot burns first");
        }

        [Test]
        public void Kitchen_BurnsFuelPerMeal_AndCarriesProvenance()
        {
            var diag = new List<string>();
            var registry = new EntityIdRegistry();
            var kitchen = new BoardingHouseKitchen(registry);
            BoardingHouseFoodBootstrap.ApplyBootstrapEndowment(kitchen.FoodStock, registry, 100, diag);
            kitchen.FuelStock = NewStock(100, diag, out _);

            var inventory = new BoardingRoomInventory();
            inventory.AddRoom(BoardingRoomType.SharedBed, 2, diag);
            var register = new BoardingHouseBoarderRegister();
            register.CheckIn(11, "bh-room-1", 0, BoarderStayKind.Weekly, true, 275, 45, 100, 0, inventory, diag);

            int fuelBefore = kitchen.FuelStock.UnitsOnHand(BoardingHouseFuelSupply.FuelMaterialId);
            int fullyServed = kitchen.ExecuteDay(register, 100, kitchenLaborMinutes: 480, diag);
            Assert.AreEqual(1, fullyServed);
            Assert.AreEqual(fuelBefore - 2 * BoardingHouseFuelSupply.FuelUnitsPerBoardMeal,
                kitchen.FuelStock.UnitsOnHand(BoardingHouseFuelSupply.FuelMaterialId),
                "two board meals burn two fuel units");

            foreach (BoardingHouseServedMealRecord record in kitchen.ServedMeals)
                StringAssert.Contains("fuel:", record.ProvenanceChain, "fuel provenance rides the served meal");
        }

        [Test]
        public void Kitchen_NoFuelStoreOrEmptyStore_StopsLoudly()
        {
            var diag = new List<string>();
            var registry = new EntityIdRegistry();

            var unwired = new BoardingHouseKitchen(registry);
            BoardingHouseFoodBootstrap.ApplyBootstrapEndowment(unwired.FoodStock, registry, 100, diag);
            var inventory = new BoardingRoomInventory();
            inventory.AddRoom(BoardingRoomType.SharedBed, 2, diag);
            var register = new BoardingHouseBoarderRegister();
            register.CheckIn(11, "bh-room-1", 0, BoarderStayKind.Weekly, true, 275, 45, 100, 0, inventory, diag);

            Assert.AreEqual(0, unwired.ExecuteDay(register, 100, 480, diag),
                "no fuel store wired — the stove stays cold");

            var empty = new BoardingHouseKitchen(registry);
            BoardingHouseFoodBootstrap.ApplyBootstrapEndowment(empty.FoodStock, registry, 100, diag);
            empty.FuelStock = new BoardingHouseFuelStock();
            Assert.AreEqual(0, empty.ExecuteDay(register, 100, 480, diag),
                "empty fuel store — nothing cooks on faked fuel");

            empty.RequireFuel = false;
            Assert.AreEqual(1, empty.ExecuteDay(register, 100, 480, diag),
                "RequireFuel=false routes around unmodeled contexts");
        }

        [Test]
        public void Fuel_SaveLoad_RoundTrips()
        {
            var diag = new List<string>();
            var stock = NewStock(100, diag, out EntityIdRegistry registry);
            Assert.Null(stock.ReceiveLot(DealerLot(registry, 10, 101), diag));

            var restored = new BoardingHouseFuelStock();
            restored.LoadFromSaveDto(stock.CaptureSaveDto());

            Assert.AreEqual(stock.UnitsOnHand(BoardingHouseFuelSupply.FuelMaterialId),
                restored.UnitsOnHand(BoardingHouseFuelSupply.FuelMaterialId));
            Assert.AreEqual(2, restored.Lots.Count);
        }
    }
}
