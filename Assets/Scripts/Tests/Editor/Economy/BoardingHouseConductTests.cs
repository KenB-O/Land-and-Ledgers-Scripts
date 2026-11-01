using System.Collections.Generic;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1F: boarder conduct — incidents are recorded as facts (Canon §8.1G:
    /// reputation emerges from actual experiences); the proprietor's
    /// ejection threshold is policy as data (Canon §8.1D). Crossing the
    /// threshold ejects the boarder. The canon describes no formal rulebook,
    /// so nothing here invents one.
    /// </summary>
    [TestFixture]
    public sealed class BoardingHouseConductTests
    {
        private (BoardingHouseShopRuntime house, List<string> diag) NewHouse(int dayIndex)
        {
            var diag = new List<string>();
            var house = new BoardingHouseShopRuntime("bh-1", new EntityIdRegistry());
            house.ApplyOpeningPantryEndowment(dayIndex, diag);
            house.ApplyOpeningFuelEndowment(dayIndex, diag);
            Assert.Null(house.AddRoom(BoardingRoomType.SharedBed, 2, diag));
            Assert.Null(house.CheckInBoarder(11, "bh-room-1", 0, BoarderStayKind.Weekly, false, dayIndex, 0, diag));
            Assert.Null(house.CheckInBoarder(12, "bh-room-1", 1, BoarderStayKind.Weekly, false, dayIndex, 0, diag));
            return (house, diag);
        }

        [Test]
        public void RecordConductIncident_TracksStrikesPerPerson()
        {
            var (house, diag) = NewHouse(100);

            house.RecordConductIncident(11, BoardingConductIncidentKind.Disturbance, 101, "loud after hours", diag);
            house.RecordConductIncident(11, BoardingConductIncidentKind.Theft, 102, "missing spoon", diag);

            Assert.AreEqual(2, house.ConductLog.StrikesFor(11));
            Assert.AreEqual(0, house.ConductLog.StrikesFor(12), "strikes follow the person, not the house");
            Assert.AreEqual(2, house.ConductLog.IncidentsFor(11).Count);
            Assert.IsNotNull(house.BoarderRegister.FindRecord(11), "two strikes do not eject at the default threshold of 3");
        }

        [Test]
        public void RecordConductIncident_ThirdStrikeEjects()
        {
            var (house, diag) = NewHouse(100);

            house.RecordConductIncident(11, BoardingConductIncidentKind.Disturbance, 101, "", diag);
            house.RecordConductIncident(11, BoardingConductIncidentKind.PropertyDamage, 102, "broken chair", diag);
            house.RecordConductIncident(11, BoardingConductIncidentKind.Disturbance, 103, "", diag);

            Assert.IsNull(house.BoarderRegister.FindRecord(11), "third strike ejects the boarder");
            Assert.IsFalse(house.RoomInventory.PersonHoldsAnyBed(11), "ejection vacates the bed");
            Assert.IsNotNull(house.BoarderRegister.FindRecord(12), "the quiet boarder stays");
            Assert.AreEqual(3, house.ConductLog.StrikesFor(11), "the facts stay on the books after ejection");
        }

        [Test]
        public void RecordConductIncident_PolicyThresholdIsSettable()
        {
            var (house, diag) = NewHouse(100);
            house.ConductPolicy.EjectAfterStrikes = 1;

            house.RecordConductIncident(11, BoardingConductIncidentKind.UnpaidBill, 101, "", diag);

            Assert.IsNull(house.BoarderRegister.FindRecord(11), "a one-strike house ejects on the first incident");
        }

        [Test]
        public void RecordConductIncident_RefusesBadFacts()
        {
            var diag = new List<string>();
            var log = new BoardingHouseConductLog();

            Assert.NotNull(log.RecordIncident(0, BoardingConductIncidentKind.Theft, 100, "", diag),
                "anonymous incidents refused");
            Assert.NotNull(log.RecordIncident(11, BoardingConductIncidentKind.Theft, -1, "", diag),
                "dateless incidents refused");
            Assert.AreEqual(0, log.StrikesFor(11));
        }

        [Test]
        public void Conduct_SaveLoad_RoundTrips()
        {
            var (house, diag) = NewHouse(100);
            house.ConductPolicy.EjectAfterStrikes = 5;
            house.RecordConductIncident(11, BoardingConductIncidentKind.Disturbance, 101, "note", diag);

            var restored = new BoardingHouseShopRuntime("bh-1", new EntityIdRegistry());
            restored.LoadFromSaveDto(house.CaptureSaveDto());

            Assert.AreEqual(1, restored.ConductLog.StrikesFor(11));
            Assert.AreEqual(5, restored.ConductPolicy.EjectAfterStrikes);
            Assert.AreEqual(BoardingConductIncidentKind.Disturbance, restored.ConductLog.IncidentsFor(11)[0].Kind);
        }
    }
}
