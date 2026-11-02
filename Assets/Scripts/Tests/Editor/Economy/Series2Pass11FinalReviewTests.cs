using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using LandLedgers.Persistence;
using LandLedgers.Population.TownGrowth;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Series 2 Pass 11 (final review): Roslyn tests for the one
    /// production-wiring contract Pass 11 changes. Pass 10 shipped
    /// the Pass 10 coordinator overloads but no SaveLoadManager
    /// property, so the production save path could never carry
    /// the Pass 10 section. Pass 11 adds
    /// SaveLoadManager.Series2Pass10State and wires it into the
    /// production Capture/Restore call sites (a Unity-side edit,
    /// so Unity compilation of SaveLoadManager itself remains
    /// Codex/Unity AI work). These tests prove the coordinator
    /// contract that wiring relies on: a null Pass 10 state is
    /// safe on both sides of the call, and a present section
    /// with no state is diagnosed, never silently dropped.
    /// No fixture inputs, prices, or decisions are invented here.
    /// </summary>
    [TestFixture]
    public sealed class Series2Pass11FinalReviewTests
    {
        private sealed class FakeMigrationHolder : IPassXlMigrationFlagHolder
        {
            public bool SuppressMigrantArrivals { get; set; }
        }

        private sealed class FakePlayerHolder : IPassXlPlayerFlagHolder
        {
            public bool TreatPlayerAsNpc { get; set; }
        }

        [Test]
        public void Pass11SaveLoadManagerContract_NullPass10State_CapturesNullSectionAndRestoresEmptyWithDiagnostic()
        {
            var source10 = new Series2Pass10SaveState();
            source10.Leads.Add(new Pass5LeadSaveRecord { ContactName = "Test Contact", BusinessId = "test-business" });
            var source9 = new Series2Pass9SaveState { Catalog = new LandLedgers.Economy.Ordering.Pass3StoreCatalog() };

            // Capture through the production-shaped overload with a
            // null Pass 10 state: the Pass 10 section must be null
            // (not an invented empty-looking section), while the
            // Pass 9 section still rides.
            PassXlSliceSaveDto dtoWithNull10 = PassXlSliceSaveCoordinator.Capture(
                new FakeMigrationHolder(), new FakePlayerHolder(), null, source9, null);
            Assert.IsNull(dtoWithNull10.series2Pass10, "a null Pass 10 state captures a null section");
            Assert.IsNotNull(dtoWithNull10.series2Pass9, "the Pass 9 section still rides when only the Pass 10 state is null");

            // Restore a DTO carrying a Pass 10 section with no state
            // supplied: diagnosed, never silently dropped, never crashed.
            PassXlSliceSaveDto dtoWithSection = PassXlSliceSaveCoordinator.Capture(
                new FakeMigrationHolder(), new FakePlayerHolder(), null, source9, source10);
            Assert.IsNotNull(dtoWithSection.series2Pass10);
            List<string> diag = PassXlSliceSaveCoordinator.Restore(
                dtoWithSection, new FakeMigrationHolder(), new FakePlayerHolder(), null,
                new Series2Pass9SaveState { Catalog = new LandLedgers.Economy.Ordering.Pass3StoreCatalog() },
                null, null);
            Assert.IsTrue(diag.Any(d => d.Contains("Pass 10 section present")),
                "a present Pass 10 section with no state must be diagnosed, not silently dropped");
        }

        [Test]
        public void Pass11SaveLoadManagerContract_NullBothStates_OldSaveRestoresEmptyWithoutInvention()
        {
            var target10 = new Series2Pass10SaveState();
            target10.Leads.Add(new Pass5LeadSaveRecord { ContactName = "Stale Contact" });
            List<string> diag = PassXlSliceSaveCoordinator.Restore(
                new PassXlSliceSaveDto(), new FakeMigrationHolder(), new FakePlayerHolder(), null,
                null, null, target10);
            Assert.AreEqual(0, target10.Leads.Count, "an old save with no sections restores empty, clearing stale state");
            Assert.IsNull(target10.Snapshot, "no snapshot on an old save is null, never a placeholder score");
            Assert.IsTrue(diag.Any(d => d.Contains("no Pass 10 section")),
                "the missing Pass 10 section must be diagnosed on an old save");
        }
    }
}
