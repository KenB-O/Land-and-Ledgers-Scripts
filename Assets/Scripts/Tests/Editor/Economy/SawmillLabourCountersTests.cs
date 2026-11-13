using System.Linq;
using NUnit.Framework;
using LandLedgers.Economy.Businesses.Sawmill;

// PORT NOTE (F6 Tier 1, 2026-11-13): ported verbatim from the forestry
// branch (main repo at 346d8746, 135 lines) onto this line's baseline
// 2cb4781. Only the header note and the namespace changed: the
// namespace is LandLedgers.Editor.Economy (the namespace of
// SawmillRuntimeTests.cs in this folder); the forestry file used
// LandLedgers.Tests.Editor.Economy, a third variant that does not live
// in this folder. NUnit discovery is by [Test], so the namespace
// change is functionally inert.
namespace LandLedgers.Editor.Economy
{
    public sealed class SawmillLabourCountersTests
    {
        [Test]
        public void SawMinutes_AccumulatePerSawmill_AndReadInTotal()
        {
            SawmillLabourCounters counters = new SawmillLabourCounters();
            counters.RecordCreditedSawMinutes("sawmill-1", 30);
            counters.RecordCreditedSawMinutes("sawmill-1", 60);
            counters.RecordCreditedSawMinutes("sawmill-2", 30);

            Assert.AreEqual(90, counters.SawMinutesForBusiness("sawmill-1"));
            Assert.AreEqual(30, counters.SawMinutesForBusiness("sawmill-2"));
            Assert.AreEqual(120, counters.TotalSawMinutes);
            Assert.AreEqual(0, counters.SawMinutesForBusiness("sawmill-3"));
            Assert.IsTrue(!counters.IsSawMinutesZero);
            Assert.IsTrue(counters.IsLumberZero);
        }

        [Test]
        public void LumberUnits_AccumulatePerSawmill_AndReadInTotal()
        {
            SawmillLabourCounters counters = new SawmillLabourCounters();
            counters.RecordLumberProduced("sawmill-1", 8);
            counters.RecordLumberProduced("sawmill-1", 4);
            counters.RecordLumberProduced("sawmill-2", 12);

            Assert.AreEqual(12, counters.LumberUnitsForBusiness("sawmill-1"));
            Assert.AreEqual(12, counters.LumberUnitsForBusiness("sawmill-2"));
            Assert.AreEqual(24, counters.TotalLumberUnits);
            Assert.AreEqual(0, counters.LumberUnitsForBusiness("sawmill-3"));
            Assert.IsTrue(!counters.IsLumberZero);
            Assert.IsTrue(counters.IsSawMinutesZero);
        }

        [Test]
        public void FreshHolder_BothCountersReadZero()
        {
            SawmillLabourCounters counters = new SawmillLabourCounters();

            Assert.AreEqual(0, counters.TotalSawMinutes);
            Assert.AreEqual(0, counters.TotalLumberUnits);
            Assert.IsTrue(counters.IsSawMinutesZero);
            Assert.IsTrue(counters.IsLumberZero);
            Assert.AreEqual(0, counters.BusinessIds.Count);
        }

        [Test]
        public void NonPositiveValues_AreIgnored()
        {
            SawmillLabourCounters counters = new SawmillLabourCounters();
            counters.RecordCreditedSawMinutes("sawmill-1", 0);
            counters.RecordCreditedSawMinutes("sawmill-1", -30);
            counters.RecordLumberProduced("sawmill-1", 0);
            counters.RecordLumberProduced("sawmill-1", -4);

            Assert.AreEqual(0, counters.TotalSawMinutes);
            Assert.AreEqual(0, counters.TotalLumberUnits);
            Assert.AreEqual(0, counters.BusinessIds.Count);
        }

        [Test]
        public void BlankBusinessId_LandsUnderUnknownSawmillSentinel()
        {
            SawmillLabourCounters counters = new SawmillLabourCounters();
            counters.RecordCreditedSawMinutes(null, 30);
            counters.RecordCreditedSawMinutes("", 30);
            counters.RecordCreditedSawMinutes("   ", 30);
            counters.RecordLumberProduced(null, 4);
            counters.RecordLumberProduced("   ", 8);

            Assert.AreEqual(90, counters.SawMinutesForBusiness(SawmillLabourCounters.UnknownSawmillId));
            Assert.AreEqual(12, counters.LumberUnitsForBusiness(SawmillLabourCounters.UnknownSawmillId));
            Assert.AreEqual(1, counters.BusinessIds.Count);
            Assert.AreEqual(SawmillLabourCounters.UnknownSawmillId, counters.BusinessIds[0]);
        }

        [Test]
        public void SaveRoundTrip_PreservesBothCounters_WithKeys_DoubleLoadIsIdempotent()
        {
            SawmillLabourCounters counters = new SawmillLabourCounters();
            counters.RecordCreditedSawMinutes("sawmill-1", 90);
            counters.RecordCreditedSawMinutes("sawmill-2", 30);
            counters.RecordLumberProduced("sawmill-1", 12);
            counters.RecordLumberProduced("sawmill-2", 12);

            SawmillLabourCounters restored = new SawmillLabourCounters();
            restored.LoadFromSaveDto(counters.ToSaveDto());

            Assert.AreEqual(90, restored.SawMinutesForBusiness("sawmill-1"));
            Assert.AreEqual(30, restored.SawMinutesForBusiness("sawmill-2"));
            Assert.AreEqual(12, restored.LumberUnitsForBusiness("sawmill-1"));
            Assert.AreEqual(12, restored.LumberUnitsForBusiness("sawmill-2"));
            Assert.AreEqual(120, restored.TotalSawMinutes);
            Assert.AreEqual(24, restored.TotalLumberUnits);

            restored.LoadFromSaveDto(counters.ToSaveDto());
            Assert.AreEqual(120, restored.TotalSawMinutes);
            Assert.AreEqual(24, restored.TotalLumberUnits);
        }

        [Test]
        public void NullLoad_ClearsToEmpty_WithBothZeroChecksTrue()
        {
            SawmillLabourCounters counters = new SawmillLabourCounters();
            counters.RecordCreditedSawMinutes("sawmill-1", 30);
            counters.RecordLumberProduced("sawmill-1", 4);

            counters.LoadFromSaveDto(null);

            Assert.AreEqual(0, counters.TotalSawMinutes);
            Assert.AreEqual(0, counters.TotalLumberUnits);
            Assert.IsTrue(counters.IsSawMinutesZero);
            Assert.IsTrue(counters.IsLumberZero);
            Assert.AreEqual(0, counters.BusinessIds.Count);
        }

        [Test]
        public void BusinessIds_EnumeratesKeys_FromBothCounters()
        {
            SawmillLabourCounters counters = new SawmillLabourCounters();
            counters.RecordCreditedSawMinutes("sawmill-1", 30);
            counters.RecordLumberProduced("sawmill-2", 4);

            Assert.AreEqual(2, counters.BusinessIds.Count);
            Assert.IsTrue(counters.BusinessIds.Contains("sawmill-1"));
            Assert.IsTrue(counters.BusinessIds.Contains("sawmill-2"));
        }
    }
}
