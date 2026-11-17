using System.Collections.Generic;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// M4-R1 (F-M1-7): open housing-search needs were lost on save and load.
    /// HousingSearchNeedRegistry.ExportState and ImportState existed but had
    /// zero production callers — the hub never called them and the save DTO
    /// carried no needs slot. These tests prove the registry round-trips
    /// through export/import exactly, that a load replaces rather than
    /// merges, and that imported need ids are never reused.
    /// </summary>
    [TestFixture]
    public sealed class HousingSearchNeedsSaveRoundTripM4Tests
    {
        [Test]
        public void OpenNeeds_SurviveExportImportRoundTrip()
        {
            var source = new HousingSearchNeedRegistry();
            HousingSearchNeed open = source.OpenNeed(1, 2, ResidentialConditionKind.Unsheltered, "no shelter", 10);
            HousingSearchNeed resolved = source.OpenNeed(2, 4, ResidentialConditionKind.PrecariousCondition, "evicted", 10);
            source.MarkResolved(2, 11);

            var exported = new List<HousingSearchNeed>();
            source.ExportState(exported);
            Assert.AreEqual(2, exported.Count);

            var restored = new HousingSearchNeedRegistry();
            restored.ImportState(exported);

            HousingSearchNeed restoredOpen = restored.GetOpenNeed(1);
            Assert.IsNotNull(restoredOpen, "open need for H1 did not survive the round trip.");
            Assert.AreEqual(open.NeedId, restoredOpen.NeedId);
            Assert.AreEqual(2, restoredOpen.RequiredSleepingPlaces);
            Assert.AreEqual(ResidentialConditionKind.Unsheltered, restoredOpen.CurrentCondition);
            Assert.AreEqual("no shelter", restoredOpen.TriggerSummary);
            Assert.AreEqual(HousingSearchNeedStatus.Open, restoredOpen.Status);
            Assert.IsTrue(restored.GetOpenNeed(2) == null, "resolved need for H2 must not read as open after the round trip.");

            var reExported = new List<HousingSearchNeed>();
            restored.ExportState(reExported);
            Assert.AreEqual(2, reExported.Count);
            HousingSearchNeed restoredResolved = reExported.Find(n => n.HouseholdId == 2);
            Assert.IsNotNull(restoredResolved, "resolved need for H2 was lost in the round trip.");
            Assert.AreEqual(resolved.NeedId, restoredResolved.NeedId);
            Assert.AreEqual(HousingSearchNeedStatus.Resolved, restoredResolved.Status);
            Assert.AreEqual(4, restoredResolved.RequiredSleepingPlaces);
        }

        [Test]
        public void ImportState_ClearsStaleNeedsInsteadOfMerging()
        {
            var registry = new HousingSearchNeedRegistry();
            registry.OpenNeed(9, 1, ResidentialConditionKind.Unsheltered, "stale need", 5);
            Assert.IsNotNull(registry.GetOpenNeed(9));

            var incoming = new List<HousingSearchNeed>
            {
                new HousingSearchNeed
                {
                    NeedId = "hsn-0",
                    HouseholdId = 1,
                    RequiredSleepingPlaces = 3,
                    CurrentCondition = ResidentialConditionKind.PrecariousCondition,
                    CreatedDayIndex = 12,
                    LastEvaluatedDayIndex = 12,
                    Status = HousingSearchNeedStatus.Open,
                    TriggerSummary = "fresh save",
                },
            };
            registry.ImportState(incoming);

            Assert.IsTrue(registry.GetOpenNeed(9) == null, "stale need for H9 survived a load; ImportState must clear first.");
            Assert.IsNotNull(registry.GetOpenNeed(1), "imported need for H1 is missing after the load.");
        }

        [Test]
        public void ImportState_SecondImportReplacesFirst()
        {
            var registry = new HousingSearchNeedRegistry();
            registry.ImportState(new List<HousingSearchNeed>
            {
                new HousingSearchNeed { NeedId = "hsn-0", HouseholdId = 1, RequiredSleepingPlaces = 2, Status = HousingSearchNeedStatus.Open },
            });
            Assert.IsNotNull(registry.GetOpenNeed(1));

            registry.ImportState(new List<HousingSearchNeed>
            {
                new HousingSearchNeed { NeedId = "hsn-0", HouseholdId = 2, RequiredSleepingPlaces = 2, Status = HousingSearchNeedStatus.Open },
            });

            Assert.IsTrue(registry.GetOpenNeed(1) == null, "first import survived a second load.");
            Assert.IsNotNull(registry.GetOpenNeed(2));
        }

        [Test]
        public void OpenNeed_AfterImport_NeverReusesAnImportedId()
        {
            var source = new HousingSearchNeedRegistry();
            source.OpenNeed(1, 2, ResidentialConditionKind.Unsheltered, "first", 10);
            source.OpenNeed(2, 2, ResidentialConditionKind.Unsheltered, "second", 10);

            var exported = new List<HousingSearchNeed>();
            source.ExportState(exported);

            var restored = new HousingSearchNeedRegistry();
            restored.ImportState(exported);

            HousingSearchNeed third = restored.OpenNeed(3, 1, ResidentialConditionKind.Unsheltered, "third", 12);
            Assert.AreEqual("hsn-2", third.NeedId, "imported ids hsn-0/hsn-1 were not accounted for; the next need reused one.");
        }

        [Test]
        public void ImportState_ToleratesNullAndEmpty()
        {
            var registry = new HousingSearchNeedRegistry();
            registry.ImportState(null);
            registry.ImportState(new List<HousingSearchNeed>());
            Assert.IsTrue(registry.GetOpenNeed(1) == null);

            registry.OpenNeed(1, 1, ResidentialConditionKind.Unsheltered, "kept", 3);
            registry.ImportState(new List<HousingSearchNeed>());
            Assert.IsTrue(registry.GetOpenNeed(1) == null, "importing an empty save must clear the registry, not leave stale needs.");

            var source = new HousingSearchNeedRegistry();
            source.ExportState(null);
        }
    }
}
