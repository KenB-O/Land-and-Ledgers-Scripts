using LandLedgers.Editor.Playtests;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Orchestration
{
    public sealed class Playtest60DayRecoveryTests
    {
        [Test]
        public void RuntimeDayListener_IsRestoredOnlyForRunningPlayModeWithClock()
        {
            Assert.IsTrue(Playtest60DayRunCoordinator.ShouldRestoreDayChangedSubscription(
                "RUNNING", editorIsPlaying: true, hasTimeManager: true));
            Assert.IsFalse(Playtest60DayRunCoordinator.ShouldRestoreDayChangedSubscription(
                "ARMED", editorIsPlaying: true, hasTimeManager: true));
            Assert.IsFalse(Playtest60DayRunCoordinator.ShouldRestoreDayChangedSubscription(
                "RUNNING", editorIsPlaying: false, hasTimeManager: true));
            Assert.IsFalse(Playtest60DayRunCoordinator.ShouldRestoreDayChangedSubscription(
                "RUNNING", editorIsPlaying: true, hasTimeManager: false));
        }

        [Test]
        public void CompletedRun_IsFinalizedAfterLogCaptureDetaches()
        {
            Assert.IsTrue(Playtest60DayRunCoordinator.ShouldFinalizeAfterCaptureDetach("COMPLETED"));
            Assert.IsFalse(Playtest60DayRunCoordinator.ShouldFinalizeAfterCaptureDetach("RUNNING"));
            Assert.IsFalse(Playtest60DayRunCoordinator.ShouldFinalizeAfterCaptureDetach("ABORTED"));
        }

        [Test]
        public void MissingSnapshotOffsets_IncludesFinalDayAndDoesNotRepeatRecordedDays()
        {
            CollectionAssert.AreEqual(
                new[] { 9, 10, 11 },
                Playtest60DayRunCoordinator.MissingSnapshotOffsets(lastCapturedOffset: 8, throughOffset: 11));
            CollectionAssert.IsEmpty(
                Playtest60DayRunCoordinator.MissingSnapshotOffsets(lastCapturedOffset: 11, throughOffset: 11));
        }

        [Test]
        public void MissingSnapshotOffsets_ReportsGapsIndependentlyOfRequestedDuration()
        {
            CollectionAssert.AreEqual(new[] { 4, 5 },
                Playtest60DayRunCoordinator.MissingSnapshotOffsets(3, 5));
            CollectionAssert.IsEmpty(Playtest60DayRunCoordinator.MissingSnapshotOffsets(5, 4));
        }

        [Test]
        public void UnexpectedDomainReload_IsInvalidOnlyForAnActivePlaytest()
        {
            Assert.IsTrue(Playtest60DayRunCoordinator.IsUnexpectedDomainReload(
                "RUNNING", true, "old-domain", "new-domain"));
            Assert.IsFalse(Playtest60DayRunCoordinator.IsUnexpectedDomainReload(
                "ARMED", true, "old-domain", "new-domain"));
            Assert.IsFalse(Playtest60DayRunCoordinator.IsUnexpectedDomainReload(
                "RUNNING", false, "old-domain", "new-domain"));
        }
    }
}
