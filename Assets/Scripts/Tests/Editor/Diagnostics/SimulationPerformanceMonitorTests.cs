// Land & Ledgers — Simulation Performance and Memory Monitor tests (Lane 7, Tool 09).
//
// Known cases reproduced from the current tree's source-reviewed
// evidence (no run exists on this HEAD): the retained 60-day run's
// DEF-061 numbers (250x requested 15,000 game-s/real-s vs achieved
// 2,712–32,573), the throughput math from the runner's own fields, and
// the memory honesty rules (managed heap and working set are measured;
// neither is physical RAM).

using System.Collections.Generic;
using LandLedgers.Diagnostics;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Diagnostics
{
    public sealed class SimulationPerformanceMonitorTests
    {
        [Test]
        public void ComputeThroughput_SixtyDays_KnownWallTime_MatchesArithmetic()
        {
            // 60 simulated days in 1 wall hour at 250x: 60*86400/3600 = 1440 game-s/real-s.
            RunThroughput throughput = SimulationPerformanceMonitor.ComputeThroughput(
                "2026-11-20T19:23:11Z", "2026-11-20T20:23:11Z", 60, 60, 250f);
            Assert.IsTrue(throughput.Valid);
            Assert.AreEqual(3600.0, throughput.WallSeconds, 0.001);
            Assert.AreEqual(1440.0, throughput.AchievedGameSecondsPerRealSecond, 0.001);
            Assert.AreEqual(15000.0, throughput.RequestedGameSecondsPerRealSecond, 0.001);
            Assert.AreEqual(1.0, throughput.SimulatedDaysPerMinute, 0.001);
            Assert.AreEqual(60.0, throughput.WallSecondsPerSimulatedDay, 0.001);
        }

        [Test]
        public void ComputeThroughput_RetainedRunLowEnd_SuggestsStepDown()
        {
            // DEF-061 low end: achieved 2,712 of requested 15,000 (~18%) — step down and re-measure.
            // 60 days in ~31.9 wall minutes achieves ~2,712 game-s/real-s.
            RunThroughput low = SimulationPerformanceMonitor.ComputeThroughput(
                "2026-11-20T19:23:11Z", "2026-11-20T19:55:03Z", 60, 60, 250f);
            Assert.IsTrue(low.Valid);
            Assert.IsTrue(low.AchievedGameSecondsPerRealSecond > 2600.0 && low.AchievedGameSecondsPerRealSecond < 2800.0);
            Assert.IsTrue(low.AchievedRatio < 0.2);
            Assert.IsTrue(low.SuggestedAction.Contains("Step down"));
        }

        [Test]
        public void ComputeThroughput_HoldingPreset_SaysHold()
        {
            // 60 days in 6 wall minutes at 250x: achieved 14,400 of 15,000 (96%) — hold.
            RunThroughput throughput = SimulationPerformanceMonitor.ComputeThroughput(
                "2026-11-20T19:23:11Z", "2026-11-20T19:29:11Z", 60, 60, 250f);
            Assert.IsTrue(throughput.Valid);
            Assert.IsTrue(throughput.AchievedRatio >= 0.9);
            Assert.IsTrue(throughput.SuggestedAction.StartsWith("Hold"));
        }

        [Test]
        public void ComputeThroughput_UnparseableWallTimes_IsInvalid_NeverFabricated()
        {
            RunThroughput throughput = SimulationPerformanceMonitor.ComputeThroughput(
                "not-a-date", "also-not-a-date", 60, 60, 250f);
            Assert.IsFalse(throughput.Valid);
            Assert.IsFalse(string.IsNullOrWhiteSpace(throughput.InvalidReason));
            Assert.AreEqual(0.0, throughput.AchievedGameSecondsPerRealSecond);
        }

        [Test]
        public void ComputeThroughput_EndBeforeStart_IsInvalid()
        {
            RunThroughput throughput = SimulationPerformanceMonitor.ComputeThroughput(
                "2026-11-20T20:23:11Z", "2026-11-20T19:23:11Z", 60, 60, 250f);
            Assert.IsFalse(throughput.Valid);
        }

        [Test]
        public void ComputeThroughput_ZeroDaysCompleted_IsInvalid()
        {
            RunThroughput throughput = SimulationPerformanceMonitor.ComputeThroughput(
                "2026-11-20T19:23:11Z", "2026-11-20T20:23:11Z", 0, 60, 250f);
            Assert.IsFalse(throughput.Valid);
        }

        [Test]
        public void RequestedRate_OneX_IsSixtyGameSecondsPerRealSecond()
        {
            RunThroughput throughput = SimulationPerformanceMonitor.ComputeThroughput(
                "2026-11-20T19:23:11Z", "2026-11-20T19:24:11Z", 1, 60, 1f);
            Assert.IsTrue(throughput.Valid);
            Assert.AreEqual(60.0, throughput.RequestedGameSecondsPerRealSecond, 0.001);
        }

        [Test]
        public void RetainedRunReference_NamesDef061Numbers()
        {
            string reference = SimulationPerformanceMonitor.RetainedRunThroughputReference();
            Assert.IsTrue(reference.Contains("15,000"));
            Assert.IsTrue(reference.Contains("2,712"));
            Assert.IsTrue(reference.Contains("32,573"));
            Assert.IsTrue(reference.Contains("DEF-061"));
        }

        [Test]
        public void PendingQueueWarning_MoreThanOneDay_Warns_ExactlyOneDay_DoesNot()
        {
            Assert.IsTrue(SimulationPerformanceMonitor.ShouldWarnPendingQueue(86401.0, 86400f));
            Assert.IsFalse(SimulationPerformanceMonitor.ShouldWarnPendingQueue(86400.0, 86400f));
        }

        [Test]
        public void SummarizeMemory_NoSamples_SaysNothingMeasured()
        {
            MemorySummary summary = SimulationPerformanceMonitor.SummarizeMemory(new List<MemorySample>());
            Assert.AreEqual(0, summary.SampleCount);
            Assert.IsTrue(summary.Assessment.Contains("No memory samples"));
        }

        [Test]
        public void SummarizeMemory_TwoSamples_ReportsGrowthPerDay()
        {
            var samples = new List<MemorySample>
            {
                new MemorySample { TimestampUtc = "2026-11-20T19:23:11Z", DayIndex = 0, ManagedHeapBytes = 100 * 1024 * 1024, WorkingSetBytes = 500 * 1024 * 1024 },
                new MemorySample { TimestampUtc = "2026-11-20T20:23:11Z", DayIndex = 10, ManagedHeapBytes = 120 * 1024 * 1024, WorkingSetBytes = 540 * 1024 * 1024 },
            };
            MemorySummary summary = SimulationPerformanceMonitor.SummarizeMemory(samples);
            Assert.AreEqual(2, summary.SampleCount);
            Assert.AreEqual(20 * 1024 * 1024, summary.ManagedHeapGrowthBytes);
            Assert.AreEqual(40 * 1024 * 1024, summary.WorkingSetGrowthBytes);
            Assert.AreEqual(2.0 * 1024 * 1024, summary.ManagedHeapGrowthBytesPerDay, 1.0);
            Assert.IsTrue(summary.Assessment.Contains("Neither number is physical RAM"));
        }

        [Test]
        public void SummarizeMemory_OneSample_IsALevel_NotATrend()
        {
            var samples = new List<MemorySample>
            {
                new MemorySample { TimestampUtc = "2026-11-20T19:23:11Z", DayIndex = 0, ManagedHeapBytes = 100 * 1024 * 1024, WorkingSetBytes = 500 * 1024 * 1024 },
            };
            MemorySummary summary = SimulationPerformanceMonitor.SummarizeMemory(samples);
            Assert.IsTrue(summary.Assessment.Contains("a level, not a trend"));
            Assert.AreEqual(-1, summary.ManagedHeapGrowthBytesPerDay);
        }

        [Test]
        public void FormatMegabytes_Negative_IsUnknown()
        {
            Assert.AreEqual("unknown", SimulationPerformanceMonitor.FormatMegabytes(-1));
            Assert.AreEqual("100.0 MB", SimulationPerformanceMonitor.FormatMegabytes(100 * 1024 * 1024));
        }
    }
}
