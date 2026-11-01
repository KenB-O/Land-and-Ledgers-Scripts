using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass VIII Worker 2 (Housing and Boarding): the live connection
    /// between a boarding business and the housing authority.
    /// - A business cannot sell beds it never registered.
    /// - Check-in records bed + boarder agreement + housing occupancy
    ///   under a boarding agreement; over-capacity check-in is refused.
    /// - Check-out frees the bed (soiled for turnover), ends the housing
    ///   occupancy, and closes the agreement — departure frees the space.
    /// - Closure checks every boarder out and ends registrations.
    /// - Room capacity changes apply in lockstep or not at all.
    /// - Save/load preserves the bridge state exactly once.
    /// </summary>
    [TestFixture]
    public sealed class BoardingLodgingBridgeTests
    {
        private sealed class FakeHouse : IBoardingHouseLodgingSide
        {
            public string BusinessInstanceId => "bh-1";
            public string BusinessName => "Test Boarding House";
            public BoardingRoomInventory RoomInventory { get; } = new BoardingRoomInventory();
            public BoardingHouseBoarderRegister BoarderRegister { get; } = new BoardingHouseBoarderRegister();
            public BoardingRateSchedule RateSchedule { get; } = new BoardingRateSchedule();
        }

        private sealed class Fixture
        {
            public FakeHouse House = new FakeHouse();
            public HousingAuthority Housing = new HousingAuthority();
            public LodgingRegistry Registry = new LodgingRegistry();
            public BoardingLodgingBridge Bridge = new BoardingLodgingBridge();
            public List<string> Diag = new List<string>();

            public Fixture()
            {
                Assert.IsNull(House.RoomInventory.AddRoom(BoardingRoomType.SharedBed, 2, Diag));
                Assert.IsNull(House.RoomInventory.AddRoom(BoardingRoomType.PrivateRoom, 1, Diag));
            }

            public string RegisterHouse()
            {
                string refusal = Bridge.RegisterHouse(House, "parcel-7", 0, Housing, Registry, Diag);
                Assert.IsNull(refusal, refusal);
                return refusal;
            }

            public string CheckIn(int personId, int householdId, string roomId, int bedIndex,
                BoarderStayKind stayKind = BoarderStayKind.Weekly)
            {
                return Bridge.CheckIn(House, personId, householdId, roomId, bedIndex,
                    stayKind, false, 0, 0, Housing, Registry, Diag);
            }
        }

        [Test]
        public void CheckInToUnregisteredRoomIsRefused()
        {
            var f = new Fixture();
            // Deliberately NOT registering the house.
            string refusal = f.CheckIn(1, 10, "bh-room-1", 0);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("never registered", refusal);
            Assert.AreEqual(0, f.House.BoarderRegister.BoarderCount);
            Assert.AreEqual(0, f.Housing.AllCurrentOccupancies().Count);
        }

        [Test]
        public void CheckInRecordsBedAgreementAndHousingOccupancy()
        {
            var f = new Fixture();
            f.RegisterHouse();

            string refusal = f.CheckIn(1, 10, "bh-room-1", 0);
            Assert.IsNull(refusal, refusal);

            // The bed is held.
            Assert.AreEqual(1, f.House.BoarderRegister.BoarderCount);
            Assert.IsTrue(f.House.RoomInventory.PersonHoldsAnyBed(1));

            // The housing authority knows where P1 sleeps tonight.
            List<ResidentialOccupancy> occupancies = f.Housing.CurrentOccupanciesForPerson(1);
            Assert.AreEqual(1, occupancies.Count);
            Assert.AreEqual(AccommodationArrangement.BoardingOrLodging, occupancies[0].Arrangement);
            Assert.AreEqual(10, occupancies[0].HouseholdId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(occupancies[0].AgreementId));

            // The boarding agreement exists as the rights record — with NO
            // payment terms, so the tenancy loop never double-charges.
            PropertyAgreement agreement = f.Housing.FindAgreement(occupancies[0].AgreementId);
            Assert.IsNotNull(agreement);
            Assert.AreEqual("boarding", agreement.Kind);
            Assert.IsNull(agreement.PaymentTerms);
        }

        [Test]
        public void OverCapacityCheckInIsRefusedAtTheBed()
        {
            var f = new Fixture();
            f.RegisterHouse();

            Assert.IsNull(f.CheckIn(1, 10, "bh-room-1", 0));
            Assert.IsNull(f.CheckIn(2, 10, "bh-room-1", 1));
            // Bed 0 is taken; bed 5 does not exist; no third bed exists.
            Assert.IsNotNull(f.CheckIn(3, 11, "bh-room-1", 0));
            Assert.IsNotNull(f.CheckIn(3, 11, "bh-room-1", 5));
            Assert.AreEqual(2, f.House.BoarderRegister.BoarderCount);
            Assert.AreEqual(0, f.Housing.CurrentOccupanciesForPerson(3).Count);
        }

        [Test]
        public void CheckOutFreesBedEndsOccupancyAndClosesAgreement()
        {
            var f = new Fixture();
            f.RegisterHouse();
            Assert.IsNull(f.CheckIn(1, 10, "bh-room-1", 0));

            string agreementId = f.Housing.CurrentOccupanciesForPerson(1)[0].AgreementId;
            string refusal = f.Bridge.CheckOut(f.House, 1, 3, f.Housing, f.Registry, f.Diag);
            Assert.IsNull(refusal, refusal);

            // Bed freed but soiled: turnover before re-letting.
            Assert.IsFalse(f.House.RoomInventory.PersonHoldsAnyBed(1));
            Assert.AreEqual(0, f.House.BoarderRegister.BoarderCount);
            Assert.AreEqual(0, f.Housing.CurrentOccupanciesForPerson(1).Count);
            Assert.AreEqual(3, f.Housing.FindAgreement(agreementId).EndDayIndex);

            // The space is genuinely free for a newcomer after turnover.
            Assert.AreEqual(1, f.House.RoomInventory.SoiledBedCount());
            Assert.AreEqual(1, f.House.RoomInventory.ClearRoomTurnover("bh-room-1", f.Diag));
            Assert.IsNull(f.CheckIn(7, 12, "bh-room-1", 0));
            Assert.AreEqual(1, f.Housing.CurrentOccupanciesForPerson(7).Count);
        }

        [Test]
        public void FailedCheckInRollsBackTheBed()
        {
            var f = new Fixture();
            f.RegisterHouse();
            Assert.IsNull(f.CheckIn(1, 10, "bh-room-1", 0));

            // Occupy the housing-side space directly past the bed count via
            // the permissive path, then try a bridge check-in for bed 1:
            // the guarded housing capacity refuses, and the bed rolls back.
            LodgingRegistration reg = f.Registry.FindActiveRegistration("bh-1", "bh-room-1");
            Assert.IsNotNull(reg);
            Assert.IsNotNull(f.Housing.Occupy(50, 50, reg.SpaceId, AccommodationArrangement.DoubledUpOrShared, 1, f.Diag));

            string refusal = f.CheckIn(2, 11, "bh-room-1", 1);
            Assert.IsNotNull(refusal, "housing-side capacity must refuse the second bed");
            Assert.IsFalse(f.House.RoomInventory.PersonHoldsAnyBed(2), "the bed must roll back");
            Assert.IsNull(f.House.BoarderRegister.FindRecord(2), "no phantom boarder agreement");
        }

        [Test]
        public void CloseHouseChecksEveryoneOutAndEndsRegistrations()
        {
            var f = new Fixture();
            f.RegisterHouse();
            Assert.IsNull(f.CheckIn(1, 10, "bh-room-1", 0));
            Assert.IsNull(f.CheckIn(2, 10, "bh-room-2", 0));

            string refusal = f.Bridge.CloseHouse(f.House, 9, f.Housing, f.Registry, f.Diag);
            Assert.IsNull(refusal, refusal);

            Assert.AreEqual(0, f.House.BoarderRegister.BoarderCount);
            Assert.AreEqual(0, f.Housing.AllCurrentOccupancies().Count);
            Assert.AreEqual(0, f.Registry.AllActiveRegistrations().Count);

            // After closure, beds cannot be sold until re-registered.
            Assert.IsNotNull(f.CheckIn(3, 11, "bh-room-1", 0));
        }

        [Test]
        public void RoomCapacityChangeAppliesInLockstepOrNotAtAll()
        {
            var f = new Fixture();
            f.RegisterHouse();
            Assert.IsNull(f.CheckIn(1, 10, "bh-room-1", 0));
            Assert.IsNull(f.CheckIn(2, 10, "bh-room-1", 1));
            LodgingRegistration reg = f.Registry.FindActiveRegistration("bh-1", "bh-room-1");

            // Growing works everywhere at once.
            Assert.IsNull(f.Bridge.ChangeRoomCapacity(f.House, "bh-room-1", 3, 5, f.Housing, f.Registry, f.Diag));
            Assert.AreEqual(3, f.House.RoomInventory.FindRoom("bh-room-1").BedCount);
            Assert.AreEqual(3, f.Housing.FindSpace(reg.SpaceId).SleepingCapacity);
            Assert.AreEqual(3, f.Registry.FindActiveRegistration("bh-1", "bh-room-1").BedCount);

            // Shrinking below occupied beds is refused on both sides.
            string refusal = f.Bridge.ChangeRoomCapacity(f.House, "bh-room-1", 1, 6, f.Housing, f.Registry, f.Diag);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(3, f.House.RoomInventory.FindRoom("bh-room-1").BedCount);
            Assert.AreEqual(3, f.Housing.FindSpace(reg.SpaceId).SleepingCapacity);

            // After both boarders leave, shrinking works.
            Assert.IsNull(f.Bridge.CheckOut(f.House, 1, 7, f.Housing, f.Registry, f.Diag));
            Assert.IsNull(f.Bridge.CheckOut(f.House, 2, 7, f.Housing, f.Registry, f.Diag));
            Assert.AreEqual(2, f.House.RoomInventory.ClearRoomTurnover("bh-room-1", f.Diag));
            Assert.IsNull(f.Bridge.ChangeRoomCapacity(f.House, "bh-room-1", 1, 8, f.Housing, f.Registry, f.Diag));
            Assert.AreEqual(1, f.House.RoomInventory.FindRoom("bh-room-1").BedCount);
            Assert.AreEqual(1, f.Housing.FindSpace(reg.SpaceId).SleepingCapacity);
        }

        [Test]
        public void RegistrationRefusesMismatchAndDoubleSale()
        {
            var f = new Fixture();
            f.RegisterHouse();

            // Same space cannot be sold by a second business.
            string refusal = f.Registry.RegisterLodging("bh-2", "room-x", 2,
                f.Registry.FindActiveRegistration("bh-1", "bh-room-1").SpaceId,
                100, 600, 1, f.Housing, f.Diag);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("already registered", refusal);

            // Bed count must match the space's sleeping capacity.
            var housing2 = new HousingAuthority();
            var diag = new List<string>();
            housing2.RegisterBuilding("b-9", "p-9", "house", 0, "test", diag);
            AccommodationSpace space = housing2.DefineSpace("b-9", 4, diag);
            string mismatch = f.Registry.RegisterLodging("bh-1", "bh-room-9", 2, space.SpaceId,
                100, 600, 1, housing2, diag);
            Assert.IsNotNull(mismatch);
            StringAssert.Contains("must agree", mismatch);

            // Unknown space: no registration against thin air.
            string unknown = f.Registry.RegisterLodging("bh-1", "bh-room-9", 2, "space-nope",
                100, 600, 1, housing2, diag);
            Assert.IsNotNull(unknown);
        }

        [Test]
        public void SaveLoadPreservesBridgeStateExactlyOnce()
        {
            var f = new Fixture();
            f.RegisterHouse();
            Assert.IsNull(f.CheckIn(1, 10, "bh-room-1", 0));

            LodgingRegistry.LodgingRegistrySaveDto dto = f.Registry.CaptureSaveDto();
            var restored = new LodgingRegistry();
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(2, restored.AllActiveRegistrations().Count);

            // Clear-then-load: loading twice does not duplicate.
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(2, restored.AllActiveRegistrations().Count);

            LodgingRegistration reg = restored.FindActiveRegistration("bh-1", "bh-room-1");
            Assert.IsNotNull(reg);
            Assert.AreEqual(2, reg.BedCount);
        }

        [Test]
        public void LodgingVacanciesFeedDiscoveryWithRealBedsOnly()
        {
            var f = new Fixture();
            f.RegisterHouse();
            Assert.IsNull(f.CheckIn(1, 10, "bh-room-1", 0));

            var adapter = new HousingOpportunityAdapter(f.Housing, f.Registry,
                openBedsForRegistration: registrationId =>
                {
                    LodgingRegistration reg = f.Registry.AllActiveRegistrations()
                        .Find(r => r.RegistrationId == registrationId);
                    if (reg == null) return 0;
                    BoardingRoom room = f.House.RoomInventory.FindRoom(reg.RoomId);
                    int open = 0;
                    for (int i = 0; i < room.BedCount; i++)
                        if (!room.IsBedOccupied(i) && !room.BedNeedsTurnover(i)) open++;
                    return open;
                });

            // bh-room-1 has 1 of 2 beds taken; bh-room-2 (private, 1 bed) is free.
            List<HousingSearchOption> vacancies = adapter.BoardingVacancies(1);
            Assert.AreEqual(2, vacancies.Count);
            List<HousingSearchOption> forTwo = adapter.BoardingVacancies(2);
            Assert.AreEqual(0, forTwo.Count, "no room has 2 open beds — discovery reports what exists");
            foreach (HousingSearchOption option in vacancies)
                Assert.AreEqual(HousingSearchPathKind.Boarding, option.PathKind);
        }
    }
}
