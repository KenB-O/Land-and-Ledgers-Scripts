using System.Collections.Generic;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W2C: the boarding-house kitchen — board meals are prepared from real
    /// pantry lots with provenance, served to real boarders, and recorded
    /// in the day ledger. Labor and pantry shortfalls short the boarders
    /// honestly; unserved meals become waste; nothing is invented.
    /// </summary>
    [TestFixture]
    public sealed class BoardingHouseKitchenTests
    {
        private (BoardingHouseKitchen kitchen, BoardingHouseBoarderRegister register, BoardingRoomInventory inventory, List<string> diag)
            NewHouse(int dayIndex)
        {
            var diag = new List<string>();
            var registry = new EntityIdRegistry();
            var kitchen = new BoardingHouseKitchen(registry);
            BoardingHouseFoodBootstrap.ApplyBootstrapEndowment(kitchen.FoodStock, registry, dayIndex, diag);
            var inventory = new BoardingRoomInventory();
            inventory.AddRoom(BoardingRoomType.SharedBed, 2, diag);
            var register = new BoardingHouseBoarderRegister();
            return (kitchen, register, inventory, diag);
        }

        [Test]
        public void ExecuteDay_ServesTwoBoardMealsPerBoarder_WithProvenance()
        {
            var (kitchen, register, inventory, diag) = NewHouse(100);
            register.CheckIn(11, "bh-room-1", 0, BoarderStayKind.Weekly, true, 275, 45, 100, 0, inventory, diag);
            register.CheckIn(12, "bh-room-1", 1, BoarderStayKind.Weekly, true, 275, 45, 100, 0, inventory, diag);

            int fullyServed = kitchen.ExecuteDay(register, 100, kitchenLaborMinutes: 480, diag);

            Assert.AreEqual(2, fullyServed);
            Assert.AreEqual(2, kitchen.MealLedger.MealsEatenAtBoardingHouse(11, 100));
            Assert.AreEqual(2, kitchen.MealLedger.MealsEatenAtBoardingHouse(12, 100));
            Assert.AreEqual(4, kitchen.MealLedger.ServedMealsOnDay(100));
            Assert.AreEqual(0, kitchen.MealLedger.MealsEatenAtBoardingHouse(11, 101), "day-scoped");

            Assert.AreEqual(4, kitchen.ServedMeals.Count);
            foreach (BoardingHouseServedMealRecord record in kitchen.ServedMeals)
            {
                StringAssert.Contains("BOOTSTRAP", record.ProvenanceChain, "served meals carry provenance");
                Assert.AreEqual(100, record.DayIndex);
            }
        }

        [Test]
        public void ExecuteDay_RoomOnlyBoardersAreNotFed()
        {
            var (kitchen, register, inventory, diag) = NewHouse(100);
            register.CheckIn(11, "bh-room-1", 0, BoarderStayKind.Weekly, boardIncluded: false, 150, 25, 100, 0, inventory, diag);

            int fullyServed = kitchen.ExecuteDay(register, 100, 480, diag);

            Assert.AreEqual(0, fullyServed);
            Assert.AreEqual(0, kitchen.MealLedger.ServedMealsOnDay(100));
            Assert.AreEqual(0, kitchen.FoodStock.UnitsOnHand(BoardingHouseFoodSupply.BreadMaterialId)
                - 40, "pantry untouched when nobody is on board");
        }

        [Test]
        public void ExecuteDay_LaborShortfall_ShortsHonestly()
        {
            var (kitchen, register, inventory, diag) = NewHouse(100);
            register.CheckIn(11, "bh-room-1", 0, BoarderStayKind.Weekly, true, 275, 45, 100, 0, inventory, diag);

            // 8 minutes buys breakfast only (8m), dinner needs 12m more.
            int fullyServed = kitchen.ExecuteDay(register, 100, kitchenLaborMinutes: 8, diag);

            Assert.AreEqual(0, fullyServed, "one boarder got breakfast but not dinner — not fully served");
            Assert.AreEqual(1, kitchen.MealLedger.MealsEatenAtBoardingHouse(11, 100));
            StringAssert.Contains("labor", string.Join(" ", diag).ToLowerInvariant());
        }

        [Test]
        public void ExecuteDay_EmptyPantry_ShortsHonestly_NoInventedMeals()
        {
            var diag = new List<string>();
            var registry = new EntityIdRegistry();
            var kitchen = new BoardingHouseKitchen(registry); // no bootstrap — empty pantry
            var inventory = new BoardingRoomInventory();
            inventory.AddRoom(BoardingRoomType.SharedBed, 2, diag);
            var register = new BoardingHouseBoarderRegister();
            register.CheckIn(11, "bh-room-1", 0, BoarderStayKind.Weekly, true, 275, 45, 100, 0, inventory, diag);

            int fullyServed = kitchen.ExecuteDay(register, 100, 480, diag);

            Assert.AreEqual(0, fullyServed);
            Assert.AreEqual(0, kitchen.MealLedger.ServedMealsOnDay(100), "no food, no meals — never invented");
            StringAssert.Contains("shortfall", string.Join(" ", diag).ToLowerInvariant());
        }

        [Test]
        public void Ledger_RefusesAnonymousDinersAndPrunes()
        {
            var ledger = new BoardingHouseMealDayLedger();
            var diag = new List<string>();

            Assert.NotNull(ledger.ReportServedMeal(0, 100, diag));
            Assert.Null(ledger.ReportServedMeal(11, 100, diag));
            Assert.AreEqual(1, ledger.MealsEatenAtBoardingHouse(11, 100));

            ledger.PruneBefore(101, diag);
            Assert.AreEqual(0, ledger.MealsEatenAtBoardingHouse(11, 100));
        }

        [Test]
        public void Ledger_SaveLoad_RoundTrips()
        {
            var ledger = new BoardingHouseMealDayLedger();
            var diag = new List<string>();
            ledger.ReportServedMeal(11, 100, diag);
            ledger.ReportServedMeal(11, 100, diag);

            var restored = new BoardingHouseMealDayLedger();
            restored.LoadFromSaveDto(ledger.CaptureSaveDto());

            Assert.AreEqual(2, restored.MealsEatenAtBoardingHouse(11, 100));
        }

        [Test]
        public void CompositeSource_SumsAcrossHouses()
        {
            var a = new BoardingHouseMealDayLedger();
            var b = new BoardingHouseMealDayLedger();
            var diag = new List<string>();
            a.ReportServedMeal(11, 100, diag);
            b.ReportServedMeal(11, 100, diag);
            b.ReportServedMeal(11, 100, diag);

            var composite = new CompositeBoardingHouseMealSource(new[] { a, b });

            Assert.AreEqual(3, composite.MealsEatenAtBoardingHouse(11, 100));
            Assert.AreEqual(0, composite.MealsEatenAtBoardingHouse(11, 101));
        }

        [Test]
        public void Kitchen_SaveLoad_RoundTripsPantryShelfAndLedger()
        {
            var (kitchen, register, inventory, diag) = NewHouse(100);
            register.CheckIn(11, "bh-room-1", 0, BoarderStayKind.Weekly, true, 275, 45, 100, 0, inventory, diag);
            kitchen.ExecuteDay(register, 100, 480, diag);

            var registry = new EntityIdRegistry();
            var restored = new BoardingHouseKitchen(registry);
            restored.LoadFromSaveDto(kitchen.CaptureSaveDto());

            Assert.AreEqual(kitchen.FoodStock.UnitsOnHand(BoardingHouseFoodSupply.MeatMaterialId),
                restored.FoodStock.UnitsOnHand(BoardingHouseFoodSupply.MeatMaterialId));
            Assert.AreEqual(2, restored.MealDaySource.MealsEatenAtBoardingHouse(11, 100));
            Assert.AreEqual(kitchen.ServedMeals.Count, restored.ServedMeals.Count);
        }
    }
}
