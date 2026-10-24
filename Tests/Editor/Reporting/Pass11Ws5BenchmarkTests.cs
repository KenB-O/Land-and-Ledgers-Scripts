using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LandLedgers.Orchestration.Scenarios.Generation;
using LandLedgers.Reporting;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Reporting
{
    /// <summary>
    /// Pass XI WS5 (Part B): repeatable measurement workloads, NOT timing
    /// assertions. Each test runs its workload three times, records the
    /// fastest wall-clock time to /tmp/ws5-bench.txt and stdout, and only
    /// asserts the workload produced output (so a slow machine can never
    /// flake-fail the suite). Before/after comparisons in
    /// Docs/Pass11_Performance_Report.md come from these tests run on the
    /// same machine, same binary shape, before and after a fix.
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws5BenchmarkTests
    {
        private const string BenchPath = "/tmp/ws5-bench.txt";

        private static void Record(string workload, long bestMs, string detail)
        {
            string line = $"[WS5-BENCH] {workload} bestMs={bestMs} {detail}";
            Console.WriteLine(line);
            try { File.AppendAllText(BenchPath, line + Environment.NewLine); }
            catch { /* benchmark log is best-effort; the test result is what matters */ }
        }

        private static long FastestMs(Action work)
        {
            long best = long.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                Stopwatch sw = Stopwatch.StartNew();
                work();
                sw.Stop();
                if (sw.ElapsedMilliseconds < best) best = sw.ElapsedMilliseconds;
            }
            return best;
        }

        private static BusinessReportSnapshot SnapshotWithLedger(int entryCount, int duplicateEvery)
        {
            var snapshot = new BusinessReportSnapshot
            {
                businessId = "bench-biz",
                businessDisplayName = "Bench Business",
                period = ReportPeriod.Weekly(new SimulationDate(0, 0, 1, 1, 1, 3, 1892), new SimulationDate(6, 6, 7, 1, 1, 3, 1892)),
                generatedOnDate = new SimulationDate(6, 6, 7, 1, 1, 3, 1892),
                revenueCents = 0,
                openingCashCents = 1000,
                closingCashCents = 1000,
            };
            for (int i = 0; i < entryCount; i++)
            {
                snapshot.ledgerEntries.Add(new LedgerEntry(
                    "entry-" + i,
                    LedgerEntryKind.Revenue,
                    LedgerEntryCategory.Sales,
                    LedgerEntrySource.Snapshot,
                    snapshot.generatedOnDate,
                    "bench-biz",
                    "sale " + i,
                    100 + i,
                    1,
                    "units",
                    "week-1",
                    false,
                    string.Empty));
                if (duplicateEvery > 0 && i % duplicateEvery == 0)
                {
                    // Exact duplicate of the entry just added: dedup must drop it.
                    snapshot.ledgerEntries.Add(snapshot.ledgerEntries[snapshot.ledgerEntries.Count - 1]);
                }
            }
            return snapshot;
        }

        [Test]
        public void Bench_WeeklyLedgerDedup()
        {
            BusinessReportSnapshot snapshot = SnapshotWithLedger(1200, 2);
            int kept = 0;
            long ms = FastestMs(() =>
            {
                BusinessWeeklySummary summary = BusinessReportBuilder.BuildWeekly(snapshot);
                kept = summary.ledgerEntries.Count;
            });
            Assert.AreEqual(1200, kept);
            Record("weekly-ledger-dedup-1200uniq+600dup", ms, "kept=" + kept);
        }

        [Test]
        public void Bench_MonthlyLedgerAccumulation()
        {
            var weeks = new List<BusinessWeeklySummary>();
            for (int w = 0; w < 4; w++)
            {
                var week = new BusinessWeeklySummary();
                week.header.businessId = "bench-biz";
                week.header.period = ReportPeriod.Weekly(new SimulationDate(w * 7, 0, 1, 1, 1, 3, 1892), new SimulationDate(w * 7 + 6, 6, 7, 1, 1, 3, 1892));
                for (int i = 0; i < 600; i++)
                {
                    week.ledgerEntries.Add(new LedgerEntry(
                        "w" + w + "-entry-" + i,
                        LedgerEntryKind.Revenue,
                        LedgerEntryCategory.Sales,
                        LedgerEntrySource.WeeklySummary,
                        week.header.period.StartDate,
                        "bench-biz",
                        "sale",
                        100,
                        1,
                        "units",
                        "week-" + w,
                        false,
                        string.Empty));
                }
                weeks.Add(week);
            }
            int kept = 0;
            long ms = FastestMs(() =>
            {
                BusinessMonthlySummary monthly = BusinessReportBuilder.BuildMonthlyFromWeeklySummaries(
                    ReportPeriod.Monthly(new SimulationDate(0, 0, 1, 1, 1, 3, 1892), new SimulationDate(27, 6, 28, 4, 4, 3, 1892)),
                    new SimulationDate(27, 6, 28, 4, 4, 3, 1892),
                    weeks);
                kept = monthly.ledgerEntries.Count;
            });
            Assert.AreEqual(2400, kept);
            Record("monthly-ledger-accum-4x600", ms, "kept=" + kept);
        }

        [Test]
        public void Bench_ScenarioEvaluateManyGoals()
        {
            var service = new ScenarioObjectiveService();
            service.BeginScenario("bench-scenario");
            for (int i = 0; i < 300; i++)
            {
                int captured = i;
                service.RegisterDatedQualifier("goal-" + i, 1000000, () => captured % 2 == 0);
            }
            int latched = 0;
            long ms = FastestMs(() =>
            {
                // Fresh service per run would hide the latch-list cost, so
                // re-evaluate the same service: latched goals are skipped
                // via Contains, unlatched ones re-evaluate each run.
                service.Evaluate(10);
                latched = service.State.LatchedCompleteGoalIds.Count;
            });
            Assert.AreEqual(150, latched);
            Record("scenario-evaluate-300-goals", ms, "latched=" + latched);
        }

        [Test]
        public void Bench_DueEventQueueGrowth()
        {
            // Pass IX WS4 already heap-indexed this queue; this workload
            // evidences that it stays fast so WS5 leaves it alone.
            int popped = 0;
            long ms = FastestMs(() =>
            {
                var scheduler = new DueEventScheduler();
                for (int i = 0; i < 5000; i++)
                {
                    scheduler.Schedule(DueEventKind.TaskCompletionDue, i, "payload-" + i, out _);
                }
                popped = scheduler.PopDue(4999f).Count;
            });
            Assert.AreEqual(5000, popped);
            Record("due-event-queue-5000-schedule+pop", ms, "popped=" + popped);
        }

        [Test]
        public void Bench_WeeklyStaffingRows()
        {
            var snapshot = new BusinessReportSnapshot
            {
                businessId = "bench-biz",
                period = ReportPeriod.Weekly(new SimulationDate(0, 0, 1, 1, 1, 3, 1892), new SimulationDate(6, 6, 7, 1, 1, 3, 1892)),
                generatedOnDate = new SimulationDate(6, 6, 7, 1, 1, 3, 1892),
            };
            for (int i = 0; i < 200; i++)
            {
                snapshot.workers.Add(new BusinessWorkerSnapshot
                {
                    slotId = "slot-" + i,
                    workerId = "worker-" + i,
                    weeklyWageCents = 5000,
                    isFilled = true,
                });
            }
            int filled = 0;
            long ms = FastestMs(() =>
            {
                BusinessWeeklySummary summary = BusinessReportBuilder.BuildWeekly(snapshot);
                filled = summary.staffing.filledWorkerSlots;
            });
            Assert.AreEqual(200, filled);
            Record("weekly-staffing-200-rows", ms, "filled=" + filled);
        }
    }
}
