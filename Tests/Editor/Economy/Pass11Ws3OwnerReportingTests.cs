using System.Collections.Generic;
using LandLedgers.Reporting;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass XI WS3 (PASS9-009): owner-reporting distinctions in the
    /// report builder. Financing flows are already separated in Pass
    /// VIII (profit proxy is operating-only; owner draw and cash
    /// transfers ride LedgerEntryKind.Financing) and are locked here
    /// alongside the builder gap that remained: owner worker rows
    /// were counted into staffing payroll even though production
    /// payroll never pays owner slots (worker id prefix "owner:").
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws3OwnerReportingTests
    {
        private static BusinessReportSnapshot SnapshotWithWorkers()
        {
            return new BusinessReportSnapshot
            {
                businessId = "store",
                businessDisplayName = "General Store",
                openingCashCents = 30000,
                closingCashCents = 26000,
                revenueCents = 12000,
                costOfGoodsSoldCents = 4000,
                payrollExpenseCents = 1200,
                reorderExpenseCents = 3000,
                localSupplyExpenseCents = 800,
                ownerDistributionCents = 600,
                cashTransferInCents = 1000,
                cashTransferOutCents = 500,
                workers = new List<BusinessWorkerSnapshot>
                {
                    new BusinessWorkerSnapshot
                    {
                        slotId = "hand-1", slotDisplayName = "Hand",
                        workerId = "42", workerDisplayName = "Marta",
                        weeklyWageCents = 1200, isFilled = true,
                    },
                    new BusinessWorkerSnapshot
                    {
                        slotId = "owner-slot", slotDisplayName = "Owner",
                        workerId = "owner:player", workerDisplayName = "Owner",
                        weeklyWageCents = 2000, isFilled = true,
                    },
                },
            };
        }

        [Test]
        public void OwnerWorkerRows_ExcludedFromStaffingPayroll_TrackedSeparately()
        {
            BusinessWeeklySummary summary = BusinessReportBuilder.BuildWeekly(SnapshotWithWorkers());

            Assert.AreEqual(2, summary.staffing.filledWorkerSlots, "the owner slot is still a filled slot");
            Assert.AreEqual(1200, summary.staffing.filledWorkerPayrollCents,
                "only the hired hand's wage counts as worker payroll");
            Assert.AreEqual(1200, summary.staffing.weeklyPayrollCents,
                "weekly payroll follows the worker rows, owner wage excluded");
            Assert.AreEqual(1, summary.staffing.ownerLaborSlots);
            Assert.AreEqual(2000, summary.staffing.ownerLaborWageCents,
                "the owner wage is visible, just not as payroll");
            bool noted = false;
            foreach (BusinessReportGap gap in summary.gaps)
                if (gap.code == "owner_labor_excluded_from_payroll") noted = true;
            Assert.IsTrue(noted, "the exclusion is disclosed as a gap note, not silent");
        }

        [Test]
        public void MonthlyRollup_CarriesOwnerLaborSeparationThroughCopyStaffing()
        {
            BusinessWeeklySummary week = BusinessReportBuilder.BuildWeekly(SnapshotWithWorkers());
            BusinessMonthlySummary month = BusinessReportBuilder.BuildMonthlyFromWeeklySummaries(
                new ReportPeriod { Kind = ReportPeriodKind.Monthly, Year = 1, Month = 1, IsCompletePeriod = true },
                new SimulationDate(0, 0, 1, 1, 1, 1, 1),
                new List<BusinessWeeklySummary> { week });

            Assert.AreEqual(1200, month.staffing.filledWorkerPayrollCents);
            Assert.AreEqual(1200, month.staffing.weeklyPayrollCents);
            Assert.AreEqual(1, month.staffing.ownerLaborSlots);
            Assert.AreEqual(2000, month.staffing.ownerLaborWageCents);
        }

        [Test]
        public void FinancingFlows_NeverAppearAsOperatingProfit_OrOperatingExpense()
        {
            BusinessWeeklySummary summary = BusinessReportBuilder.BuildWeekly(SnapshotWithWorkers());

            // Operating expense = COGS 4000 + payroll 1200 + reorder 3000
            // + local supply 800 = 9000. Owner draw 600 and cash transfer
            // out 500 are financing, not expense; cash transfer in 1000
            // is financing, not revenue.
            Assert.AreEqual(9000, summary.financial.knownOperatingExpenseCents);
            Assert.AreEqual(3000, summary.financial.knownProfitProxyCents,
                "12000 revenue minus 9000 operating expense — financing in or out moves neither side");

            LedgerEntry? draw = null;
            LedgerEntry? transferIn = null;
            foreach (LedgerEntry entry in summary.ledgerEntries)
            {
                if (entry.ReportSummaryKey != null && entry.ReportSummaryKey.Contains("owner_draw")) draw = entry;
                if (entry.ReportSummaryKey != null && entry.ReportSummaryKey.Contains("cash_transfer_in")) transferIn = entry;
            }
            Assert.IsTrue(draw.HasValue, "owner draw is still reported, as financing");
            Assert.AreEqual(LedgerEntryKind.Financing, draw.Value.Kind);
            Assert.IsTrue(transferIn.HasValue, "owner cash transfer in is still reported, as financing");
            Assert.AreEqual(LedgerEntryKind.Financing, transferIn.Value.Kind);
        }
    }
}
