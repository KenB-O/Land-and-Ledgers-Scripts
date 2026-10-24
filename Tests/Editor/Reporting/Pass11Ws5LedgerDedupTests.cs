using System.Collections.Generic;
using LandLedgers.Reporting;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Reporting
{
    /// <summary>
    /// Pass XI WS5 (Part B): equivalence tests for the indexed ledger
    /// copy. The old implementation scanned the target list per row
    /// (quadratic); the new one indexes it once (linear). These tests
    /// pin the observable behaviour — same rows kept, same order,
    /// same dedup rule — so the speedup cannot silently change a
    /// report. The identity rule under test is AreSameLedgerEntry's:
    /// non-blank ReportSummaryKey wins, else non-blank EntryId, else
    /// the full field comparison.
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws5LedgerDedupTests
    {
        private static SimulationDate Day(int absolute)
        {
            return new SimulationDate(absolute, 0, 1, 1, 1, 3, 1892);
        }

        private static LedgerEntry Entry(string id, string key, int amount, string label)
        {
            return new LedgerEntry(
                id, LedgerEntryKind.Revenue, LedgerEntryCategory.Sales, LedgerEntrySource.Snapshot,
                Day(6), "biz", label, amount, 1, "units", "week-1", false, key);
        }

        private static BusinessReportSnapshot WeeklySnapshot(List<LedgerEntry> entries)
        {
            return new BusinessReportSnapshot
            {
                businessId = "biz",
                period = ReportPeriod.Weekly(Day(0), Day(6)),
                generatedOnDate = Day(6),
                openingCashCents = 1000,
                closingCashCents = 1000,
                ledgerEntries = entries,
            };
        }

        [Test]
        public void WeeklyBuild_DropsDuplicateEntryIdsPreservingOrder()
        {
            BusinessWeeklySummary summary = BusinessReportBuilder.BuildWeekly(WeeklySnapshot(new List<LedgerEntry>
            {
                Entry("a", "", 100, "first"),
                Entry("b", "", 200, "second"),
                Entry("a", "", 100, "first"),
                Entry("c", "", 300, "third"),
                Entry("b", "", 200, "second"),
            }));
            Assert.AreEqual(3, summary.ledgerEntries.Count);
            Assert.AreEqual("a", summary.ledgerEntries[0].EntryId);
            Assert.AreEqual("b", summary.ledgerEntries[1].EntryId);
            Assert.AreEqual("c", summary.ledgerEntries[2].EntryId);
        }

        [Test]
        public void WeeklyBuild_KeyedRowsDedupByKeyEvenWhenFieldsDiffer()
        {
            BusinessWeeklySummary summary = BusinessReportBuilder.BuildWeekly(WeeklySnapshot(new List<LedgerEntry>
            {
                Entry("id-1", "summary-key", 100, "first"),
                Entry("id-2", "summary-key", 999, "different fields, same key"),
            }));
            Assert.AreEqual(1, summary.ledgerEntries.Count);
            Assert.AreEqual("id-1", summary.ledgerEntries[0].EntryId);
        }

        [Test]
        public void WeeklyBuild_KeylessRowsDedupOnlyOnFullFieldEquality()
        {
            BusinessWeeklySummary summary = BusinessReportBuilder.BuildWeekly(WeeklySnapshot(new List<LedgerEntry>
            {
                Entry("", "", 100, "same"),
                Entry("", "", 100, "same"),
                Entry("", "", 101, "same"),
                Entry("", "", 100, "different label"),
            }));
            Assert.AreEqual(3, summary.ledgerEntries.Count);
        }

        [Test]
        public void WeeklyBuild_KeyedAndIdRowsNeverCollide()
        {
            // Under AreSameLedgerEntry a keyed row vs an id-only row
            // compares key-vs-blank: unequal, so both survive.
            BusinessWeeklySummary summary = BusinessReportBuilder.BuildWeekly(WeeklySnapshot(new List<LedgerEntry>
            {
                Entry("", "k1", 100, "keyed"),
                Entry("x", "", 100, "id-only"),
                Entry("", "k1", 100, "keyed"),
                Entry("x", "", 100, "id-only"),
            }));
            Assert.AreEqual(2, summary.ledgerEntries.Count);
        }

        [Test]
        public void MonthlyBuild_DedupsAcrossWeeksKeepingFirst()
        {
            var weeks = new List<BusinessWeeklySummary>();
            for (int w = 0; w < 2; w++)
            {
                var week = new BusinessWeeklySummary();
                week.header.businessId = "biz";
                week.header.period = ReportPeriod.Weekly(Day(w * 7), Day(w * 7 + 6));
                week.ledgerEntries.Add(Entry("shared", "", 100, "shared"));
                week.ledgerEntries.Add(Entry("w" + w, "", 100, "week-" + w));
                weeks.Add(week);
            }
            BusinessMonthlySummary monthly = BusinessReportBuilder.BuildMonthlyFromWeeklySummaries(
                ReportPeriod.Monthly(Day(0), Day(13)), Day(13), weeks);
            Assert.AreEqual(3, monthly.ledgerEntries.Count);
            Assert.AreEqual("shared", monthly.ledgerEntries[0].EntryId);
            Assert.AreEqual("w0", monthly.ledgerEntries[1].EntryId);
            Assert.AreEqual("w1", monthly.ledgerEntries[2].EntryId);
        }
    }
}
