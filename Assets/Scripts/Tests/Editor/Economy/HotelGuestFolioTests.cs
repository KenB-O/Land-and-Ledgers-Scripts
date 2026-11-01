using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Hotel;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D2A: guest folios — running accounts per guest (Canon §8.1D
    /// "credit/account tolerance"). Charges post from the daily cycle,
    /// payments record when the proprietor records them, over-tolerance
    /// posts flag loudly, and checkout with a debt closes as a debt
    /// record — never silently forgiven. Cash guests (no folio) settle at
    /// the desk. Save round-trips open folios.
    /// </summary>
    [TestFixture]
    public sealed class HotelGuestFolioTests
    {
        private static (HotelShopRuntime, List<string>) NewHotel()
        {
            var diag = new List<string>();
            var hotel = new HotelShopRuntime("hotel-inst-folio", new EntityIdRegistry());
            hotel.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            hotel.ApplyOpeningLinenEndowment(1, diag);
            return (hotel, diag);
        }

        [Test]
        public void AccountGuest_ChargesPost_PaymentReduces_OverToleranceFlags()
        {
            var (hotel, diag) = NewHotel();
            string single = hotel.RoomInventory.Rooms[0].RoomNumber;
            Assert.IsNull(hotel.CheckInGuest(1001, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly,
                10, 3, diag, 0, null, 100));

            HotelGuestFolio folio = hotel.GuestFolios.FindFolio(1001);
            Assert.IsNotNull(folio, "an account guest opens a folio at check-in");
            Assert.AreEqual(100, folio.CreditToleranceCents);

            bool over = hotel.GuestFolios.PostCharge(1001, 10, HotelFolioLineKind.RoomNightCharge, "room night", 40, diag);
            Assert.IsFalse(over, "40¢ is inside the 100¢ tolerance");
            over = hotel.GuestFolios.PostCharge(1001, 10, HotelFolioLineKind.BoardCharge, "dining room board", 70, diag);
            Assert.IsTrue(over, "110¢ is over the 100¢ tolerance — flagged loudly, not settled here");

            Assert.IsNull(hotel.GuestFolios.PostPayment(1001, 11, 50, diag));
            Assert.AreEqual(60, folio.BalanceCents);
            Assert.IsTrue(folio.WithinTolerance, "the payment brings the account back inside tolerance");
        }

        [Test]
        public void Checkout_WithDebt_RecordsDebtLoudly_NeverForgiven()
        {
            var (hotel, diag) = NewHotel();
            string single = hotel.RoomInventory.Rooms[0].RoomNumber;
            hotel.CheckInGuest(1002, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly,
                10, 2, diag, 0, null, 100);
            hotel.GuestFolios.PostCharge(1002, 10, HotelFolioLineKind.RoomNightCharge, "room night", 40, diag);
            hotel.GuestFolios.PostCharge(1002, 10, HotelFolioLineKind.RoomNightCharge, "room night", 40, diag);

            int balance = hotel.GuestFolios.CloseFolio(1002, 11, diag);
            Assert.AreEqual(80, balance, "the debt is reported to the ledger caller");
            Assert.IsNull(hotel.GuestFolios.FindFolio(1002), "the folio closed");
            Assert.IsTrue(diag[diag.Count - 1].Contains("owing 80¢"), "the debt is said loudly");
        }

        [Test]
        public void CashGuest_HasNoFolio_ChargesSettleAtDesk()
        {
            var (hotel, diag) = NewHotel();
            string single = hotel.RoomInventory.Rooms[0].RoomNumber;
            hotel.CheckInGuest(1003, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly,
                10, 2, diag);

            Assert.IsNull(hotel.GuestFolios.FindFolio(1003), "cash terms open no folio");
            bool over = hotel.GuestFolios.PostCharge(1003, 10, HotelFolioLineKind.RoomNightCharge, "room night", 40, diag);
            Assert.IsFalse(over);
        }

        [Test]
        public void PostCharge_Refuses_Negative_Payment_Refuses_Zero()
        {
            var (hotel, diag) = NewHotel();
            string single = hotel.RoomInventory.Rooms[0].RoomNumber;
            hotel.CheckInGuest(1004, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly,
                10, 1, diag, 0, null, 100);
            Assert.IsNotNull(hotel.GuestFolios.PostPayment(1004, 10, 0, diag));
            Assert.IsNotNull(hotel.GuestFolios.PostPayment(1004, 10, -5, diag));
            Assert.IsNotNull(hotel.GuestFolios.OpenFolio(1004, 50, diag), "one person holds one folio");
        }

        [Test]
        public void ExecuteDay_PostsNightlyCharges_ToOpenFolios()
        {
            var (hotel, diag) = NewHotel();
            string single = hotel.RoomInventory.Rooms[0].RoomNumber;
            hotel.CheckInGuest(1005, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly,
                10, 2, diag, 0, null, 500);
            hotel.ApplyOpeningFuelEndowment(10, diag);

            var sales = hotel.ExecuteDay(10, 60, diag);
            Assert.AreEqual(1, sales.Count);
            Assert.AreEqual(40, sales[0].CentsCharged);
            HotelGuestFolio folio = hotel.GuestFolios.FindFolio(1005);
            Assert.AreEqual(40, folio.BalanceCents, "the nightly charge posted to the account folio");
            Assert.AreEqual(1, folio.Lines.Count);
        }

        [Test]
        public void SaveLoad_RoundTripsOpenFolios_WithLines()
        {
            var (hotel, diag) = NewHotel();
            string single = hotel.RoomInventory.Rooms[0].RoomNumber;
            hotel.CheckInGuest(1006, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly,
                10, 2, diag, 0, null, 200);
            hotel.GuestFolios.PostCharge(1006, 10, HotelFolioLineKind.RoomNightCharge, "room night", 40, diag);

            var reloaded = new HotelShopRuntime("hotel-inst-folio", new EntityIdRegistry());
            reloaded.LoadFromSaveDto(hotel.CaptureSaveDto());
            HotelGuestFolio folio = reloaded.GuestFolios.FindFolio(1006);
            Assert.IsNotNull(folio);
            Assert.AreEqual(40, folio.BalanceCents);
            Assert.AreEqual(200, folio.CreditToleranceCents);
        }
    }
}
