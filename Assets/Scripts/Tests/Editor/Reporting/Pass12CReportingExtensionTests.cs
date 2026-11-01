using System.Collections.Generic;
using LandLedgers.Reporting;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Reporting
{
    /// <summary>
    /// Pass XII Worker C (LL-PLAY-013): additive reporting coverage.
    /// These are new-feature tests, not defect reproductions (the
    /// companion Pass12CReportingIntegrityTests file holds the
    /// reproductions): rent and tax attribution, the unexplained
    /// inflow direction of the cash bridge, unstamped-period handling,
    /// accrued liability as ending-state stock, and the new yearly
    /// and all-time rollups.
    /// </summary>
    [TestFixture]
    public sealed class Pass12CReportingExtensionTests
    {
        private static SimulationDate Day(int absolute, int month = 3)
        {
            return new SimulationDate(absolute, 0, 1, 1, 1, month, 1892);
        }

        private static bool HasGap(IReadOnlyList<BusinessReportGap> gaps, string code)
        {
            foreach (BusinessReportGap gap in gaps)
                if (gap != null && gap.code == code) return true;
            return false;
        }

        [Test]
        public void WeeklyBuild_RentIsOperating_TaxIsCashOnly()
        {
            var snapshot = new BusinessReportSnapshot
            {
                businessId = "biz",
                period = ReportPeriod.Weekly(Day(0), Day(6)),
                generatedOnDate = Day(6),
                openingCashCents = 30000,
                closingCashCents = 32100,
                revenueCents = 12000,
                costOfGoodsSoldCents = 4000,
                payrollExpenseCents = 1200,
                reorderExpenseCents = 3000,
                localSupplyExpenseCents = 800,
                rentExpenseCents = 500,
                taxExpenseCents = 300,
                ownerDistributionCents = 600,
                cashTransferInCents = 1000,
                cashTransferOutCents = 500,
            };
            BusinessWeeklySummary summary = BusinessReportBuilder.BuildWeekly(snapshot);

            Assert.AreEqual(9500, summary.financial.knownOperatingExpenseCents,
                "COGS 4000 + payroll 1200 + reorder 3000 + local 800 + rent 500.");
            Assert.AreEqual(2500, summary.financial.knownProfitProxyCents,
                "Rent lowers operating profit; tax does not (it is not an operating expense).");
            Assert.AreEqual(10900, summary.financial.knownExpenseCents,
                "Operating 9500 + draw 600 + transfer out 500 + tax 300.");
            Assert.AreEqual(0, summary.financial.unknownExpenseCents);
            Assert.AreEqual(0, summary.financial.unknownInflowCents,
                "Proxy 2500 + transfer in 1000 - transfer out 500 - draw 600 - tax 300 = 2100 = cash change.");
            bool rentRow = false, taxRow = false;
            foreach (LedgerEntry entry in summary.ledgerEntries)
            {
                if (entry.Category == LedgerEntryCategory.Rent && entry.AmountCents == -500) rentRow = true;
                if (entry.Category == LedgerEntryCategory.Tax && entry.AmountCents == -300) taxRow = true;
            }
            Assert.IsTrue(rentRow, "Rent appears as its own ledger row.");
            Assert.IsTrue(taxRow, "Tax appears as its own ledger row.");
        }

        [Test]
        public void WeeklyBuild_UnexplainedInflow_IsFlaggedNotHidden()
        {
            var snapshot = new BusinessReportSnapshot
            {
                businessId = "biz",
                period = ReportPeriod.Weekly(Day(0), Day(6)),
                generatedOnDate = Day(6),
                openingCashCents = 0,
                closingCashCents = 5000,
            };
            BusinessWeeklySummary summary = BusinessReportBuilder.BuildWeekly(snapshot);

            Assert.AreEqual(5000, summary.financial.unknownInflowCents,
                "Cash rose 5000c with no explained flow — previously the bridge clamped this direction to zero silently.");
            Assert.AreEqual(0, summary.financial.unknownExpenseCents);
            StringAssert.Contains("Unexplained inflow", summary.financial.cashBridgeSummary);
        }

        [Test]
        public void MonthlyRollup_UnstampedWeek_IncludedWithCaution()
        {
            var snapshot = new BusinessReportSnapshot
            {
                businessId = "biz",
                revenueCents = 4000,
                payrollExpenseCents = 100,
            };
            BusinessWeeklySummary week = BusinessReportBuilder.BuildWeekly(snapshot);
            BusinessMonthlySummary month = BusinessReportBuilder.BuildMonthlyFromWeeklySummaries(
                new ReportPeriod { Kind = ReportPeriodKind.Monthly, Year = 1, Month = 1, IsCompletePeriod = true },
                new SimulationDate(0, 0, 1, 1, 1, 1, 1),
                new List<BusinessWeeklySummary> { week });

            Assert.AreEqual(4000, month.financial.revenueCents,
                "An unstamped week is included (legacy/default shape), not dropped.");
            Assert.IsTrue(HasGap(month.gaps, "weekly_period_unstamped"));
        }

        [Test]
        public void MonthlyRollup_AccruedLiability_IsEndingStateNotSum()
        {
            BusinessWeeklySummary Week(int start, int accrued)
            {
                var week = new BusinessWeeklySummary();
                week.header.businessId = "biz";
                week.header.period = ReportPeriod.Weekly(Day(start), Day(start + 6));
                week.financial.openingCashCents = 1000;
                week.financial.closingCashCents = 1000;
                week.financial.accruedLiabilityCents = accrued;
                return week;
            }

            BusinessMonthlySummary month = BusinessReportBuilder.BuildMonthlyFromWeeklySummaries(
                ReportPeriod.Monthly(Day(0), Day(27)), Day(27),
                new List<BusinessWeeklySummary> { Week(0, 700), Week(7, 900) });

            Assert.AreEqual(900, month.financial.accruedLiabilityCents,
                "Accrued liability is a stock: the month shows the latest week's stock, not 700 + 900.");
        }

        private static BusinessMonthlySummary Month(string businessId, int startDay, int endDay, int month, int revenueCents, int openingCashCents, int closingCashCents)
        {
            var summary = new BusinessMonthlySummary();
            summary.header.businessId = businessId;
            summary.header.businessDisplayName = businessId;
            summary.header.period = ReportPeriod.Monthly(Day(startDay, month), Day(endDay, month));
            summary.financial.openingCashCents = openingCashCents;
            summary.financial.closingCashCents = closingCashCents;
            summary.financial.revenueCents = revenueCents;
            summary.financial.payrollExpenseCents = 100;
            summary.financial.reorderExpenseCents = 200;
            return summary;
        }

        [Test]
        public void YearlyRollup_FromMonthlies_SumsDedupsAndOrders()
        {
            BusinessMonthlySummary jan = Month("biz", 0, 30, 1, 5000, 1000, 1500);
            BusinessMonthlySummary feb = Month("biz", 31, 58, 2, 7000, 1500, 2200);
            BusinessYearlySummary year = BusinessReportBuilder.BuildYearlyFromMonthlySummaries(
                ReportPeriod.Yearly(Day(0, 1), Day(364, 12)), Day(364, 12),
                new List<BusinessMonthlySummary> { feb, jan, jan });

            Assert.AreEqual(12000, year.financial.revenueCents, "Duplicate January counts once.");
            Assert.AreEqual(2, year.includedMonthCount);
            Assert.AreEqual(1000, year.financial.openingCashCents, "Chronological first month, not list order.");
            Assert.AreEqual(2200, year.financial.closingCashCents);
            Assert.AreEqual(6000, year.averageMonthlyRevenueCents);
            Assert.AreEqual(2, year.bestRevenueMonth, "February is the best month.");
            Assert.AreEqual(7000, year.bestRevenueMonthCents);
            Assert.IsTrue(HasGap(year.gaps, "duplicate_monthly_summary"));
        }

        [Test]
        public void YearlyRollup_MonthOutsidePeriod_IsExcluded()
        {
            BusinessMonthlySummary inside = Month("biz", 0, 30, 1, 5000, 1000, 1500);
            BusinessMonthlySummary outside = Month("biz", 400, 430, 2, 9000, 2000, 2900);
            BusinessYearlySummary year = BusinessReportBuilder.BuildYearlyFromMonthlySummaries(
                ReportPeriod.Yearly(Day(0, 1), Day(364, 12)), Day(364, 12),
                new List<BusinessMonthlySummary> { inside, outside });

            Assert.AreEqual(5000, year.financial.revenueCents);
            Assert.AreEqual(1, year.includedMonthCount);
            Assert.IsTrue(HasGap(year.gaps, "monthly_summary_outside_period"));
        }

        [Test]
        public void AllTimeRollup_FromMonthlies_SumsAcrossYears()
        {
            BusinessMonthlySummary m1 = Month("biz", 0, 30, 1, 5000, 1000, 1500);
            BusinessMonthlySummary m2 = Month("biz", 365, 395, 1, 6000, 1500, 2100);
            BusinessMonthlySummary m3 = Month("biz", 396, 426, 2, 1000, 2100, 2200);
            BusinessAllTimeSummary allTime = BusinessReportBuilder.BuildAllTimeFromMonthlySummaries(
                ReportPeriod.AllTime(Day(0, 1), Day(426, 2)), Day(426, 2),
                new List<BusinessMonthlySummary> { m3, m1, m2 });

            Assert.AreEqual(12000, allTime.financial.revenueCents);
            Assert.AreEqual(3, allTime.includedMonthCount);
            Assert.AreEqual(1000, allTime.financial.openingCashCents);
            Assert.AreEqual(2200, allTime.financial.closingCashCents);
            Assert.AreEqual(ReportPeriodKind.AllTime, allTime.header.period.Kind);
        }
    }
}
