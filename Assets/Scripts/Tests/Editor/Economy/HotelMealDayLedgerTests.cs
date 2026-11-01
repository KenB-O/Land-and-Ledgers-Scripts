using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Hotel;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W3B: the hotel meal day ledger — served meals are day-scoped facts
    /// for real persons (the IHotelMealDaySource contract), old days prune
    /// explicitly, and the ledger round-trips through save/load.
    /// </summary>
    [TestFixture]
    public sealed class HotelMealDayLedgerTests
    {
        [Test]
        public void ReportServedMeal_Records_RealPersons_RealDays()
        {
            var diag = new List<string>();
            var ledger = new HotelMealDayLedger();

            Assert.IsNull(ledger.ReportServedMeal(101, 10, diag));
            Assert.IsNull(ledger.ReportServedMeal(101, 10, diag));
            Assert.IsNull(ledger.ReportServedMeal(102, 10, diag));

            Assert.AreEqual(2, ledger.MealsEatenAtHotel(101, 10));
            Assert.AreEqual(1, ledger.MealsEatenAtHotel(102, 10));
            Assert.AreEqual(3, ledger.ServedMealsOnDay(10));
            Assert.AreEqual(0, ledger.MealsEatenAtHotel(101, 9), "day-scoped");
            Assert.AreEqual(0, ledger.MealsEatenAtHotel(101, 11), "day-scoped");
        }

        [Test]
        public void ReportServedMeal_Refuses_Anonymous_And_Dayless()
        {
            var diag = new List<string>();
            var ledger = new HotelMealDayLedger();

            Assert.IsNotNull(ledger.ReportServedMeal(0, 10, diag), "anonymous diners are not recorded");
            Assert.IsNotNull(ledger.ReportServedMeal(-3, 10, diag));
            Assert.IsNotNull(ledger.ReportServedMeal(101, -1, diag), "a served meal needs a real day index");

            Assert.AreEqual(0, ledger.ServedMealsOnDay(10));
            Assert.AreEqual(0, ledger.MealsEatenAtHotel(0, 10));
        }

        [Test]
        public void PruneBefore_Drops_OldDays()
        {
            var diag = new List<string>();
            var ledger = new HotelMealDayLedger();
            ledger.ReportServedMeal(101, 10, diag);
            ledger.ReportServedMeal(101, 11, diag);

            ledger.PruneBefore(11, diag);

            Assert.AreEqual(0, ledger.MealsEatenAtHotel(101, 10), "yesterday's meals never feed tomorrow's nutrition");
            Assert.AreEqual(1, ledger.MealsEatenAtHotel(101, 11));
        }

        [Test]
        public void CompositeHotelMealSource_Sums_AcrossHotels()
        {
            var diag = new List<string>();
            var ledgerA = new HotelMealDayLedger();
            var ledgerB = new HotelMealDayLedger();
            ledgerA.ReportServedMeal(101, 10, diag);
            ledgerB.ReportServedMeal(101, 10, diag);
            ledgerB.ReportServedMeal(101, 10, diag);

            IHotelMealDaySource composite = new CompositeHotelMealSource(
                new List<IHotelMealDaySource> { ledgerA, ledgerB });

            Assert.AreEqual(3, composite.MealsEatenAtHotel(101, 10), "a town with several hotels sums them");
            Assert.AreEqual(0, composite.MealsEatenAtHotel(999, 10));
        }

        [Test]
        public void SaveLoad_RoundTrips_Ledger()
        {
            var diag = new List<string>();
            var ledger = new HotelMealDayLedger();
            ledger.ReportServedMeal(101, 10, diag);
            ledger.ReportServedMeal(101, 10, diag);
            ledger.ReportServedMeal(102, 11, diag);

            HotelMealDayLedger.HotelMealDayLedgerSaveDto dto = ledger.CaptureSaveDto();
            var restored = new HotelMealDayLedger();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(2, restored.MealsEatenAtHotel(101, 10));
            Assert.AreEqual(1, restored.MealsEatenAtHotel(102, 11));
            Assert.AreEqual(2, restored.ServedMealsOnDay(10));
        }
    }
}
