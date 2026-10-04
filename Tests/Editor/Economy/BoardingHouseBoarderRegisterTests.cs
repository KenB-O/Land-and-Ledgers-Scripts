using System.Collections.Generic;
using LandLedgers.Economy.Businesses.BoardingHouse;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W2C: the boarder register — agreements lock rates at check-in, every
    /// boarder resolves to a boarding bed in a named room (the nightly-state
    /// hook W3 builds on), weekly rent and transient nights settle from
    /// real agreements, and the register round-trips through save/load.
    /// </summary>
    [TestFixture]
    public sealed class BoardingHouseBoarderRegisterTests
    {
        private BoardingRoomInventory NewInventory(List<string> diag)
        {
            var inventory = new BoardingRoomInventory();
            Assert.Null(inventory.AddRoom(BoardingRoomType.SharedBed, 2, diag));
            Assert.Null(inventory.AddRoom(BoardingRoomType.PrivateRoom, 1, diag));
            return inventory;
        }

        [Test]
        public void CheckIn_LocksRateAndPlacesNightlyState()
        {
            var diag = new List<string>();
            var inventory = NewInventory(diag);
            var register = new BoardingHouseBoarderRegister();

            Assert.Null(register.CheckIn(11, "bh-room-1", 0, BoarderStayKind.Weekly,
                boardIncluded: true, weeklyRateCents: 275, transientNightlyRateCents: 45,
                startDayIndex: 100, transientNights: 0, inventory, diag));

            BoarderRecord record = register.FindRecord(11);
            Assert.IsNotNull(record);
            Assert.AreEqual(275, record.WeeklyRateCents);
            Assert.AreEqual(BoardingNightlyState.BoardingBed, record.NightlyState);

            Assert.IsTrue(register.TryGetNightlyPlacement(11, "bh-1", out BoardingNightlyPlacement placement));
            Assert.AreEqual(11, placement.PersonId);
            Assert.AreEqual(BoardingNightlyState.BoardingBed, placement.NightlyState);
            Assert.AreEqual("bh-room-1", placement.RoomId);
            Assert.AreEqual(0, placement.BedIndex);
            Assert.AreEqual("bh-1", placement.BusinessInstanceId);

            Assert.IsFalse(register.TryGetNightlyPlacement(99, "bh-1", out _),
                "non-boarders are not this house's to place");
        }

        [Test]
        public void CheckIn_RefusesBadAgreements()
        {
            var diag = new List<string>();
            var inventory = NewInventory(diag);
            var register = new BoardingHouseBoarderRegister();

            Assert.NotNull(register.CheckIn(0, "bh-room-1", 0, BoarderStayKind.Weekly, true, 275, 45, 100, 0, inventory, diag),
                "anonymous agreements refused");
            Assert.NotNull(register.CheckIn(11, "bh-room-1", 0, BoarderStayKind.Transient, true, 0, 45, 100, 0, inventory, diag),
                "transient stays need paid nights");
            Assert.Null(register.CheckIn(11, "bh-room-1", 0, BoarderStayKind.Weekly, true, 275, 45, 100, 0, inventory, diag));
            Assert.NotNull(register.CheckIn(11, "bh-room-1", 1, BoarderStayKind.Weekly, true, 275, 45, 100, 0, inventory, diag),
                "no second agreement for the same person");
            Assert.NotNull(register.CheckIn(12, "bh-room-1", 0, BoarderStayKind.Weekly, true, 275, 45, 100, 0, inventory, diag),
                "no double-booking the bed");

            Assert.NotNull(register.CheckOut(99, inventory, diag), "cannot check out a stranger");
            Assert.Null(register.CheckOut(11, inventory, diag));
            Assert.IsFalse(inventory.PersonHoldsAnyBed(11), "checkout vacates the bed");
        }

        [Test]
        public void RentDueWeekly_UsesLockedRate()
        {
            var diag = new List<string>();
            var inventory = NewInventory(diag);
            var register = new BoardingHouseBoarderRegister();

            register.CheckIn(11, "bh-room-1", 0, BoarderStayKind.Weekly, true, 275, 45, 100, 0, inventory, diag);
            register.CheckIn(12, "bh-room-1", 1, BoarderStayKind.Weekly, false, 150, 25, 100, 0, inventory, diag);
            register.CheckIn(13, "bh-room-2", 0, BoarderStayKind.Transient, true, 0, 45, 100, 3, inventory, diag);

            List<BoarderRentDue> due = register.RentDueWeekly(107, diag);

            Assert.AreEqual(2, due.Count, "transient stays are not in the weekly settlement");
            Assert.AreEqual(275, due[0].CentsDue);
            Assert.AreEqual(150, due[1].CentsDue);
            Assert.AreEqual(7, due[0].NightsCovered);
        }

        [Test]
        public void SettleTransientNight_ChargesNightsThenExpires()
        {
            var diag = new List<string>();
            var inventory = NewInventory(diag);
            var register = new BoardingHouseBoarderRegister();

            register.CheckIn(13, "bh-room-1", 0, BoarderStayKind.Transient, true, 0, 45, 100, 2, inventory, diag);

            List<BoarderRentDue> night1 = register.SettleTransientNight(100, inventory, diag);
            Assert.AreEqual(1, night1.Count);
            Assert.AreEqual(45, night1[0].CentsDue);
            Assert.AreEqual(1, night1[0].NightsCovered);
            Assert.IsNotNull(register.FindRecord(13), "one paid night left");

            List<BoarderRentDue> night2 = register.SettleTransientNight(101, inventory, diag);
            Assert.AreEqual(1, night2.Count);
            Assert.IsNull(register.FindRecord(13), "paid nights exhausted — checked out");
            Assert.IsFalse(inventory.PersonHoldsAnyBed(13), "expired stay releases the bed");
        }

        [Test]
        public void BoardIncludedBoarders_FeedTheKitchenList()
        {
            var diag = new List<string>();
            var inventory = NewInventory(diag);
            var register = new BoardingHouseBoarderRegister();

            register.CheckIn(11, "bh-room-1", 0, BoarderStayKind.Weekly, true, 275, 45, 100, 0, inventory, diag);
            register.CheckIn(12, "bh-room-1", 1, BoarderStayKind.Weekly, false, 150, 25, 100, 0, inventory, diag);

            List<int> fed = register.BoarderPersonIdsWithBoardIncluded();
            Assert.AreEqual(1, fed.Count);
            Assert.Contains(11, fed);
        }

        [Test]
        public void Register_SaveLoad_RoundTrips()
        {
            var diag = new List<string>();
            var inventory = NewInventory(diag);
            var register = new BoardingHouseBoarderRegister();

            register.CheckIn(11, "bh-room-1", 0, BoarderStayKind.Weekly, true, 275, 45, 100, 0, inventory, diag);
            register.CheckIn(13, "bh-room-2", 0, BoarderStayKind.Transient, false, 0, 25, 100, 2, inventory, diag);

            var restored = new BoardingHouseBoarderRegister();
            restored.LoadFromSaveDto(register.CaptureSaveDto());

            Assert.AreEqual(2, restored.BoarderCount);
            Assert.AreEqual(275, restored.FindRecord(11).WeeklyRateCents);
            Assert.AreEqual(BoardingNightlyState.BoardingBed, restored.FindRecord(13).NightlyState);
            Assert.AreEqual(2, restored.FindRecord(13).TransientNightsRemaining);
        }
    }
}
