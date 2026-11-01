using System;
using System.Collections.Generic;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.PassV
{
    /// <summary>
    /// Pass V (Worker 4): off-map continuity regression tests.
    /// Covers brief section 25 item D (wagon leaving and returning from an
    /// external route) and the OffMapJourneyAuthority contract: identity,
    /// route, time, cargo, and obligations are preserved, and the SAME entity
    /// returns with no duplicated inventory and no teleporting goods.
    /// </summary>
    [TestFixture]
    public sealed class OffMapJourneyAuthorityTests
    {
        private JourneyModel model;
        private OffMapJourneyAuthority authority;
        private Dictionary<string, int> stock;
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            model = new JourneyModel();
            model.RegisterLocation(new JourneyLocation("farmstead-f1", JourneyLocationKind.Farmstead, "Home Farm", 0f, 6f));
            model.RegisterLocation(new JourneyLocation("town-depot", JourneyLocationKind.OffMapGateway, "Rail Depot", 0f, 0f));
            model.RegisterLocation(new JourneyLocation("mill", JourneyLocationKind.Mill, "Grist Mill", 2f, 3f));
            model.AddEdge("farmstead-f1", "mill", 4f, "river road");
            model.AddEdge("mill", "town-depot", 3f, "depot road");

            authority = new OffMapJourneyAuthority(model);
            stock = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "flour", 100 },
                { "wheat", 200 },
            };
            diag = new List<string>();
        }

        private int UnitsOf(string item)
        {
            int have;
            return stock.TryGetValue(item, out have) ? have : 0;
        }

        private string TakeCargo(string item, int units)
        {
            int have = UnitsOf(item);
            if (have < units) return $"only {have}u of {item} available";
            stock[item] = have - units;
            return null;
        }

        [Test]
        public void WagonExternalRoute_SameEntityReturns_NoDuplicatedInventory()
        {
            var cargo = new Dictionary<string, int> { { "flour", 40 } };
            OffMapJourneyRecord record = authority.DepartVehicle(
                "wagon-1", "farmstead-f1", "Kingston markets", 2,
                "sell flour off-map", cargo,
                new List<string> { "contract-freight-1" },
                10, UnitsOf, TakeCargo, diag);

            Assert.IsNotNull(record, "departure should succeed: " + string.Join("; ", diag));
            Assert.AreEqual(OffMapJourneyState.Departed, record.State);
            Assert.AreEqual(60, UnitsOf("flour"), "cargo taken once at departure");
            Assert.IsTrue(authority.IsOffMapVehicle("wagon-1"));
            Assert.AreEqual("off-map:" + record.JourneyId,
                authority.TravelerLocationLabel(OffMapTravelerKind.Vehicle, -1, "wagon-1"));

            // A second departure for the same wagon is refused.
            OffMapJourneyRecord dup = authority.DepartVehicle(
                "wagon-1", "farmstead-f1", "Kingston markets", 2,
                "second run", cargo, null, 10, UnitsOf, TakeCargo, diag);
            Assert.IsNull(dup, "the same wagon cannot depart twice");

            // Early completion is impossible: no teleporting.
            int dueDay = record.DueReturnDayIndex;
            Assert.Greater(dueDay, 10, "a round trip takes real days");
            var early = authority.CompleteDueJourneys(dueDay - 1, (item, units) => { stock[item] = UnitsOf(item) + units; }, diag);
            Assert.AreEqual(0, early.Count, "nothing completes before its due day");
            Assert.AreEqual(OffMapJourneyState.Departed, record.State);

            // On the due day the SAME vehicle returns; cargo delivered once.
            var done = authority.CompleteDueJourneys(dueDay, (item, units) => { stock[item] = UnitsOf(item) + units; }, diag);
            Assert.AreEqual(1, done.Count);
            Assert.AreEqual(OffMapJourneyState.Returned, record.State);
            Assert.AreEqual(dueDay, record.ActualReturnDayIndex);
            Assert.AreEqual(100, UnitsOf("flour"), "cargo delivered exactly once on return");
            Assert.IsFalse(authority.IsOffMapVehicle("wagon-1"));

            // Re-running the same day is idempotent: no double delivery.
            var rerun = authority.CompleteDueJourneys(dueDay, (item, units) => { stock[item] = UnitsOf(item) + units; }, diag);
            Assert.AreEqual(0, rerun.Count);
            Assert.AreEqual(100, UnitsOf("flour"), "no duplicated inventory on re-run");
        }

        [Test]
        public void Departure_RefusedWhenCargoInsufficient_NothingTaken()
        {
            var cargo = new Dictionary<string, int> { { "flour", 500 } };
            OffMapJourneyRecord record = authority.DepartVehicle(
                "wagon-2", "farmstead-f1", "Kingston markets", 2,
                "overloaded run", cargo, null, 10, UnitsOf, TakeCargo, diag);

            Assert.IsNull(record, "departure must refuse when cargo is insufficient");
            Assert.AreEqual(100, UnitsOf("flour"), "all-or-nothing: nothing taken on refusal");
            Assert.IsFalse(authority.IsOffMapVehicle("wagon-2"));
        }

        [Test]
        public void PersonMigration_OneWay_IdentityPreserved_ObligationsKept()
        {
            OffMapJourneyRecord record = authority.DepartPerson(
                42, "farmstead-f1", "Uncle's farm, two counties over", 5, -1,
                "seeking work", new Dictionary<string, int>(), new List<string> { "debt-note-7" },
                20, UnitsOf, TakeCargo, diag);

            Assert.IsNotNull(record);
            Assert.AreEqual(42, record.PersonId, "the SAME person id departs");
            Assert.IsFalse(record.IsRoundTrip, "migration is one-way");
            Assert.AreEqual(-1, record.DueReturnDayIndex);
            Assert.IsTrue(authority.IsOffMapPerson(42));
            Assert.AreEqual(1, record.ObligationIds.Count, "obligations travel with the person");
            Assert.AreEqual("debt-note-7", record.ObligationIds[0]);

            // A one-way journey never "completes" by the clock.
            var done = authority.CompleteDueJourneys(2000, null, diag);
            Assert.AreEqual(0, done.Count);
            Assert.IsTrue(authority.IsOffMapPerson(42), "the migrant stays off-map");
        }

        [Test]
        public void CancelledJourney_CargoReturnedOnce_TravelerFree()
        {
            var cargo = new Dictionary<string, int> { { "wheat", 50 } };
            OffMapJourneyRecord record = authority.DepartVehicle(
                "wagon-3", "farmstead-f1", "Kingston markets", 2,
                "called-off run", cargo, null, 10, UnitsOf, TakeCargo, diag);
            Assert.IsNotNull(record);
            Assert.AreEqual(150, UnitsOf("wheat"));

            string refusal = authority.CancelJourney(record.JourneyId, "road washed out",
                (item, units) => { stock[item] = UnitsOf(item) + units; }, diag);
            Assert.IsNull(refusal);
            Assert.AreEqual(OffMapJourneyState.Cancelled, record.State);
            Assert.AreEqual(200, UnitsOf("wheat"), "cargo returned to origin exactly once");
            Assert.IsFalse(authority.IsOffMapVehicle("wagon-3"));

            string second = authority.CancelJourney(record.JourneyId, "again",
                (item, units) => { stock[item] = UnitsOf(item) + units; }, diag);
            Assert.IsNotNull(second, "a settled journey cannot cancel twice");
            Assert.AreEqual(200, UnitsOf("wheat"), "no double return of cargo");
        }

        [Test]
        public void SaveLoad_ClearThenLoad_PreservesActiveJourneys()
        {
            OffMapJourneyRecord record = authority.DepartVehicle(
                "wagon-9", "farmstead-f1", "Kingston markets", 2,
                "save/load run", new Dictionary<string, int> { { "flour", 10 } },
                new List<string> { "contract-9" }, 10, UnitsOf, TakeCargo, diag);
            Assert.IsNotNull(record);

            OffMapJourneyAuthoritySaveDto dto = authority.CaptureSaveDto();
            var reloaded = new OffMapJourneyAuthority(model);
            reloaded.LoadFromSaveDto(dto);

            Assert.AreEqual(1, reloaded.ActiveJourneyCount);
            Assert.IsTrue(reloaded.IsOffMapVehicle("wagon-9"));
            OffMapJourneyRecord back = reloaded.FindActiveByVehicle("wagon-9");
            Assert.AreEqual(record.JourneyId, back.JourneyId);
            Assert.AreEqual(10, back.CargoUnits["flour"]);
            Assert.AreEqual("contract-9", back.ObligationIds[0]);

            // Clear-then-load: loading an empty dto wipes the authority.
            reloaded.LoadFromSaveDto(new OffMapJourneyAuthoritySaveDto());
            Assert.AreEqual(0, reloaded.ActiveJourneyCount);
            Assert.IsFalse(reloaded.IsOffMapVehicle("wagon-9"));
        }

        [Test]
        public void Departure_WithoutGatewayRoute_IsRefused()
        {
            var lonely = new JourneyModel();
            lonely.RegisterLocation(new JourneyLocation("island", JourneyLocationKind.Farmstead, "Nowhere", 50f, 50f));
            var lonelyAuthority = new OffMapJourneyAuthority(lonely);

            OffMapJourneyRecord record = lonelyAuthority.DepartVehicle(
                "wagon-x", "island", "Kingston markets", 2,
                "impossible run", null, null, 10, UnitsOf, TakeCargo, diag);
            Assert.IsNull(record, "no gateway, no departure: travel is never assumed free");
        }
    }
}
