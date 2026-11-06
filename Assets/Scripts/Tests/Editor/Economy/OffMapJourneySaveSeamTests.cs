using System;
using System.Collections.Generic;
using LandLedgers.Persistence;
using LandLedgers.World.Journeys;
using LandLedgers.World.Property;
using LandLedgers.World.Regional;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Slice S9 (Objective 26, preparatory half): the Pass 9 save seam
    /// for off-map journeys, plus the return-drive behaviour the
    /// production drive relies on.
    ///
    /// Harness-tested here: the DTO/state field round-trip through
    /// Series2Pass9SaveState.Capture/Restore, the authority's deep
    /// snapshot, and CompleteDueJourneys' exactly-once semantics.
    ///
    /// Source-reviewed only (NOT harness-tested): the SimulationDrivers
    /// push/pull (CaptureOffMapJourneys / RestoreOffMapJourneys /
    /// SyncOffMapJourneysToSaveState), the bootstrapper restore call in
    /// MarkRestoredFromSave, and the production return drive
    /// ProcessFreightJourneyReturns — SimulationDrivers is harness-
    /// excluded. The return-drive tests below drive the authority
    /// method directly and the file header says so.
    /// </summary>
    [TestFixture]
    public sealed class OffMapJourneySaveSeamTests
    {
        private JourneyModel model;
        private OffMapJourneyAuthority authority;
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            model = new JourneyModel();
            string problem = model.RegisterLocation(new JourneyLocation("town-center", JourneyLocationKind.Waypoint, "Town Centre", 0f, 0f));
            Assert.IsNull(problem);
            RegionalTownRegistry.ApplyAll(model, new List<string>());
            authority = new OffMapJourneyAuthority(model);
            diag = new List<string>();
        }

        private OffMapJourneyRecord DepartMillbrookWagon(int dayIndex)
        {
            OffMapJourneyRecord record = authority.DepartVehicle(
                "opening-freight-wagon", "town-center", "Millbrook Grain Co.", 0,
                "Millbrook flour pickup", null,
                new List<string> { "freight-agreement:request-1" },
                dayIndex, null, null, diag);
            Assert.IsNotNull(record, "Millbrook departure should succeed: " + string.Join("; ", diag));
            return record;
        }

        [Test]
        public void SaveSeam_RoundTripsActiveJourney()
        {
            OffMapJourneyRecord record = DepartMillbrookWagon(10);
            Assert.AreEqual(12, record.DueReturnDayIndex);
            Assert.AreEqual(OffMapJourneyState.Departed, record.State);

            var state = new Series2Pass9SaveState();
            state.OffMapJourneys = authority.CaptureSaveDto();
            Series2Pass9SaveDto dto = state.Capture(null);

            var restored = new Series2Pass9SaveState();
            restored.Restore(dto, null, diag);
            Assert.IsNotNull(restored.OffMapJourneys);
            Assert.AreEqual(1, restored.OffMapJourneys.journeys.Count);
            Assert.AreEqual(record.JourneyId, restored.OffMapJourneys.journeys[0].JourneyId);
            Assert.AreEqual(12, restored.OffMapJourneys.journeys[0].DueReturnDayIndex);
            Assert.AreEqual(OffMapJourneyState.Departed, restored.OffMapJourneys.journeys[0].State);

            var reloaded = new OffMapJourneyAuthority(model);
            reloaded.LoadFromSaveDto(restored.OffMapJourneys);
            Assert.IsTrue(reloaded.IsOffMapVehicle("opening-freight-wagon"));
            OffMapJourneyRecord back = reloaded.FindActiveByVehicle("opening-freight-wagon");
            Assert.IsNotNull(back);
            Assert.AreEqual(record.JourneyId, back.JourneyId);
            Assert.AreEqual(12, back.DueReturnDayIndex);
        }

        [Test]
        public void SaveSeam_OldSaveWithoutSection_RestoresEmpty()
        {
            var restored = new Series2Pass9SaveState();
            restored.Restore(new Series2Pass9SaveDto(), null, diag);
            Assert.IsNull(restored.OffMapJourneys);

            var fresh = new OffMapJourneyAuthority(model);
            fresh.LoadFromSaveDto(null);
            Assert.AreEqual(0, fresh.ActiveJourneyCount);
            Assert.IsTrue(!fresh.IsOffMapVehicle("opening-freight-wagon"));
        }

        [Test]
        public void SaveSeam_CaptureIsDeepSnapshot()
        {
            OffMapJourneyRecord record = DepartMillbrookWagon(10);
            OffMapJourneyAuthoritySaveDto dto = authority.CaptureSaveDto();

            List<OffMapJourneyRecord> completed = authority.CompleteDueJourneys(12, null, diag);
            Assert.AreEqual(1, completed.Count);
            Assert.AreEqual(OffMapJourneyState.Returned, record.State);

            Assert.AreEqual(1, dto.journeys.Count);
            Assert.AreEqual(OffMapJourneyState.Departed, dto.journeys[0].State,
                "post-capture completion must not rewrite the saved section");
        }

        [Test]
        public void SaveSeam_RestoreTwice_IsIdempotent()
        {
            OffMapJourneyRecord record = DepartMillbrookWagon(10);
            var state = new Series2Pass9SaveState();
            state.OffMapJourneys = authority.CaptureSaveDto();
            Series2Pass9SaveDto dto = state.Capture(null);

            var first = new Series2Pass9SaveState();
            first.Restore(dto, null, diag);
            var second = new Series2Pass9SaveState();
            second.Restore(dto, null, diag);

            Assert.AreEqual(1, first.OffMapJourneys.journeys.Count);
            Assert.AreEqual(1, second.OffMapJourneys.journeys.Count);
            Assert.AreEqual(record.JourneyId, first.OffMapJourneys.journeys[0].JourneyId);
            Assert.AreEqual(record.JourneyId, second.OffMapJourneys.journeys[0].JourneyId);

            var firstAuthority = new OffMapJourneyAuthority(model);
            firstAuthority.LoadFromSaveDto(first.OffMapJourneys);
            Assert.AreEqual(1, firstAuthority.ActiveJourneyCount);
            var secondAuthority = new OffMapJourneyAuthority(model);
            secondAuthority.LoadFromSaveDto(second.OffMapJourneys);
            Assert.AreEqual(1, secondAuthority.ActiveJourneyCount);
        }

        [Test]
        public void ReturnDrive_CompletesDueJourneyExactlyOnce()
        {
            OffMapJourneyRecord record = DepartMillbrookWagon(10);
            Assert.AreEqual(12, record.DueReturnDayIndex);

            List<OffMapJourneyRecord> early = authority.CompleteDueJourneys(11, null, diag);
            Assert.AreEqual(0, early.Count, "nothing completes before its due day");
            Assert.AreEqual(OffMapJourneyState.Departed, record.State);

            List<OffMapJourneyRecord> done = authority.CompleteDueJourneys(12, null, diag);
            Assert.AreEqual(1, done.Count);
            Assert.AreEqual(OffMapJourneyState.Returned, record.State);

            List<OffMapJourneyRecord> rerun = authority.CompleteDueJourneys(12, null, diag);
            Assert.AreEqual(0, rerun.Count, "re-running a day is idempotent");
        }

        [Test]
        public void ReturnDrive_PersonJourney_KeepsBedArrangement()
        {
            OffMapJourneyRecord record = authority.DepartPerson(
                7, "town-center", "Millbrook Grain Co.", 0, 2,
                "drive the flour wagon", null, null, 10, item => 0, null, diag);
            Assert.IsNotNull(record, "Ethan-shaped departure should succeed: " + string.Join("; ", diag));
            Assert.AreEqual(12, record.DueReturnDayIndex);

            Assert.IsTrue(AbsenceArrangementPolicies.ActiveReturnJourney(authority, 7, 11),
                "an active return journey keeps the bed on day 11");
            Assert.IsTrue(!AbsenceArrangementPolicies.ActiveReturnJourney(authority, 7, 13),
                "past the due day the arrangement no longer holds");
        }
    }
}
