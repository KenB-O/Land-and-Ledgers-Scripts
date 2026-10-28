using System.Collections.Generic;
using LandLedgers.Reporting;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Reporting
{
    /// <summary>
    /// Pass XII Worker C (LL-PLAY-013): financial-report aggregation
    /// integrity. The builders already separate financing from
    /// operating profit (Pass VIII) and dedup ledger rows (Pass XI
    /// WS5) — but the MONEY fields of a monthly rollup had no
    /// equivalent protection: a duplicated week doubled revenue and
    /// payroll while its ledger rows deduped cleanly (an internally
    /// inconsistent report), a week from another month summed in
    /// unchallenged, weeks were bridged in caller order rather than
    /// chronological order (so opening/closing cash could invert),
    /// and the same business twice in a portfolio rollup doubled the
    /// portfolio. These tests reproduce those defects before the fix.
    /// The rent/tax/unknown-inflow/yearly tests in the companion
    /// Pass12CReportingExtensionTests file cover additive fields and
    /// are not reproductions.
    /// </summary>
    [TestFixture]
    public sealed class Pass12CReportingIntegrityTests
    {
        private static SimulationDate Day(int absolute)
        {
            return new SimulationDate(absolute, 0, 1, 1, 1, 3, 1892);
        }

        private static BusinessWeeklySummary Week(
            string businessId, int startDay, int endDay, int revenueCents,
            int openingCashCents, int closingCashCents)
        {
            var week = new BusinessWeeklySummary();
            week.header.businessId = businessId;
            week.header.businessDisplayName = businessId;
            week.header.period = ReportPeriod.Weekly(Day(startDay), Day(endDay));
            week.financial.openingCashCents = openingCashCents;
            week.financial.closingCashCents = closingCashCents;
            week.financial.revenueCents = revenueCents;
            week.financial.payrollExpenseCents = 100;
            week.financial.knownOperatingExpenseCents = 100;
            week.financial.knownExpenseCents = 100;
            week.financial.netCashChangeCents = closingCashCents - openingCashCents;
            week.financial.knownProfitProxyCents = revenueCents - 100;
            return week;
        }

        private static bool HasGap(BusinessMonthlySummary month, string code)
        {
            foreach (BusinessReportGap gap in month.gaps)
                if (gap != null && gap.code == code) return true;
            return false;
        }

        [Test]
        public void MonthlyRollup_DuplicateWeek_CountsMoneyOnce()
        {
            BusinessWeeklySummary week = Week("biz", 0, 6, 5000, 1000, 1500);
            BusinessMonthlySummary month = BusinessReportBuilder.BuildMonthlyFromWeeklySummaries(
                ReportPeriod.Monthly(Day(0), Day(27)), Day(27),
                new List<BusinessWeeklySummary> { week, week });

            Assert.AreEqual(5000, month.financial.revenueCents,
                "The same week supplied twice must not double revenue.");
            Assert.AreEqual(1, month.includedWeekCount);
            Assert.IsTrue(HasGap(month, "duplicate_weekly_summary"),
                "The exclusion must be disclosed, not silent.");
        }

        [Test]
        public void MonthlyRollup_WeekOutsidePeriod_IsExcluded()
        {
            BusinessWeeklySummary inside = Week("biz", 0, 6, 5000, 1000, 1500);
            BusinessWeeklySummary outside = Week("biz", 35, 41, 9000, 2000, 2900);
            BusinessMonthlySummary month = BusinessReportBuilder.BuildMonthlyFromWeeklySummaries(
                ReportPeriod.Monthly(Day(0), Day(27)), Day(27),
                new List<BusinessWeeklySummary> { inside, outside });

            Assert.AreEqual(5000, month.financial.revenueCents,
                "A week entirely outside the monthly period must not inflate the month.");
            Assert.AreEqual(1, month.includedWeekCount);
            Assert.IsTrue(HasGap(month, "weekly_summary_outside_period"),
                "The exclusion must be disclosed, not silent.");
        }

        [Test]
        public void MonthlyRollup_OutOfOrderWeeks_BridgeChronologically()
        {
            BusinessWeeklySummary early = Week("biz", 0, 6, 5000, 1000, 1500);
            BusinessWeeklySummary late = Week("biz", 7, 13, 7000, 1500, 2200);
            BusinessMonthlySummary month = BusinessReportBuilder.BuildMonthlyFromWeeklySummaries(
                ReportPeriod.Monthly(Day(0), Day(27)), Day(27),
                new List<BusinessWeeklySummary> { late, early });

            Assert.AreEqual(1000, month.financial.openingCashCents,
                "Opening cash comes from the chronologically first week, not list order.");
            Assert.AreEqual(2200, month.financial.closingCashCents,
                "Closing cash comes from the chronologically last week, not list order.");
            Assert.AreEqual(1200, month.financial.netCashChangeCents);
        }

        [Test]
        public void PortfolioRollup_DuplicateBusiness_CountsOnce()
        {
            var snapshot = new BusinessReportSnapshot
            {
                businessId = "biz",
                period = ReportPeriod.Weekly(Day(0), Day(6)),
                generatedOnDate = Day(6),
                openingCashCents = 1000,
                closingCashCents = 1500,
                revenueCents = 5000,
                payrollExpenseCents = 100,
            };
            var portfolio = new PortfolioReportSnapshot
            {
                period = ReportPeriod.Weekly(Day(0), Day(6)),
                generatedOnDate = Day(6),
                ownedBusinessCount = 1,
                businesses = new List<BusinessReportSnapshot> { snapshot, snapshot },
            };
            PortfolioWeeklySummary summary = PortfolioReportBuilder.BuildWeekly(portfolio);

            Assert.AreEqual(1, summary.businessCount,
                "The same business snapshot twice must not double the portfolio.");
            Assert.AreEqual(5000, summary.financial.totalRevenueCents);
            bool disclosed = false;
            foreach (BusinessReportGap gap in summary.gaps)
                if (gap != null && gap.code == "duplicate_business_snapshot") disclosed = true;
            Assert.IsTrue(disclosed, "The exclusion must be disclosed, not silent.");
        }
    }
}
