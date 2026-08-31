using System;
using System.Collections.Generic;
using System.Globalization;
using LandLedgers.Time;

namespace LandLedgers.Reporting
{
    public static class PortfolioReportBuilder
    {
        public static PortfolioWeeklySummary BuildWeekly(PortfolioReportSnapshot snapshot)
        {
            PortfolioWeeklySummary summary = new();
            if (snapshot == null)
            {
                BusinessReportBuilder.AddDefaultGaps(summary.gaps);
                BusinessReportBuilder.AddReportGap(summary.gaps, "missing_snapshot", "No portfolio snapshot was provided.", BusinessReportGapSeverity.Blocking);
                return summary;
            }

            ValidatePortfolioSnapshot(summary.gaps, snapshot, ReportPeriodKind.Weekly);
            FillCommon(summary, snapshot);
            return summary;
        }

        public static PortfolioMonthlySummary BuildMonthly(PortfolioReportSnapshot snapshot)
        {
            PortfolioMonthlySummary summary = new();
            if (snapshot == null)
            {
                BusinessReportBuilder.AddDefaultGaps(summary.gaps);
                BusinessReportBuilder.AddReportGap(summary.gaps, "missing_snapshot", "No portfolio snapshot was provided.", BusinessReportGapSeverity.Blocking);
                return summary;
            }

            ValidatePortfolioSnapshot(summary.gaps, snapshot, ReportPeriodKind.Monthly);
            FillCommon(summary, snapshot);
            summary.includedWeekCount = 0;
            return summary;
        }

        public static PortfolioMonthlySummary BuildMonthlyFromBusinessMonthlySummaries(
            ReportPeriod period,
            SimulationDate generatedOnDate,
            IReadOnlyList<BusinessMonthlySummary> businessSummaries,
            int ownedLandCount,
            int ownedBusinessCount)
        {
            PortfolioMonthlySummary summary = new()
            {
                period = period,
                generatedOnDate = generatedOnDate,
                ownedLandCount = Math.Max(0, ownedLandCount),
                ownedBusinessCount = Math.Max(0, ownedBusinessCount)
            };

            if (businessSummaries == null || businessSummaries.Count == 0)
            {
                BusinessReportBuilder.AddDefaultGaps(summary.gaps);
                BusinessReportBuilder.AddReportGap(summary.gaps, "missing_business_summaries", "No business monthly summaries were provided for portfolio aggregation.", BusinessReportGapSeverity.Blocking);
                return summary;
            }

            double stockHealthTotal = 0d;
            int validSummaryCount = 0;
            int maxIncludedWeekCount = 0;
            int firstIncludedWeekCount = -1;
            bool sawDirectMonthlySnapshot = false;
            bool sawWeeklyDerivedMonthly = false;

            for (int i = 0; i < businessSummaries.Count; i++)
            {
                BusinessMonthlySummary business = businessSummaries[i];
                if (business == null || business.header == null)
                {
                    BusinessReportBuilder.AddReportGap(summary.gaps, "missing_business_summary", $"Business monthly summary at index {i} was missing and was excluded from the portfolio rollup.", BusinessReportGapSeverity.Blocking);
                    continue;
                }

                if (business.header.period.Kind != ReportPeriodKind.Monthly)
                {
                    BusinessReportBuilder.AddReportGap(summary.gaps, "non_monthly_business_summary", $"Business summary for '{business.header.businessDisplayName ?? business.header.businessId ?? "unknown business"}' was not monthly and was excluded from the portfolio rollup.", BusinessReportGapSeverity.Blocking);
                    continue;
                }

                validSummaryCount = AddNonNegativeCount(validSummaryCount, 1);
                AddFinancial(summary.financial, business.financial);
                AddStaffingTotals(summary, business.staffing);
                stockHealthTotal += SafeStockHealth01(business.inventory.stockHealth01);
                if (IsLowStockBusiness(business.inventory))
                {
                    summary.lowStockBusinessCount = AddNonNegativeCount(summary.lowStockBusinessCount, 1);
                }

                maxIncludedWeekCount = Math.Max(maxIncludedWeekCount, business.includedWeekCount);
                if (firstIncludedWeekCount < 0)
                {
                    firstIncludedWeekCount = business.includedWeekCount;
                }
                else if (firstIncludedWeekCount != business.includedWeekCount)
                {
                    BusinessReportBuilder.AddReportGap(summary.gaps, "mixed_monthly_coverage", "Business monthly summaries used different included-week counts.", BusinessReportGapSeverity.Caution);
                }

                sawDirectMonthlySnapshot |= business.includedWeekCount <= 0;
                sawWeeklyDerivedMonthly |= business.includedWeekCount > 0;
                summary.businessLines.Add(BuildLine(
                    business.header,
                    business.financial,
                    business.inventory,
                    business.staffing,
                    business.operations,
                    business.capacity,
                    business.gaps));
                BusinessReportBuilder.CopyLedgerEntries(summary.ledgerEntries, business.ledgerEntries);
                BusinessReportBuilder.CopyGaps(summary.gaps, business.gaps);
            }

            summary.businessCount = validSummaryCount;
            summary.includedWeekCount = maxIncludedWeekCount;
            summary.averageStockHealth01 = CalculateAverageStockHealth(stockHealthTotal, validSummaryCount);
            if (sawDirectMonthlySnapshot && sawWeeklyDerivedMonthly)
            {
                BusinessReportBuilder.AddReportGap(summary.gaps, "mixed_monthly_source", "Portfolio monthly rollup mixed direct monthly snapshots with weekly-derived business summaries.", BusinessReportGapSeverity.Caution);
            }

            if (summary.ownedBusinessCount > 0 && summary.ownedBusinessCount != validSummaryCount)
            {
                BusinessReportBuilder.AddReportGap(summary.gaps, "owned_business_count_mismatch", "Owned business count did not match the number of valid business summaries included in the portfolio report.", BusinessReportGapSeverity.Caution);
            }

            BusinessReportBuilder.AddDefaultGaps(summary.gaps);
            return summary;
        }

        public static string BuildWeeklyOwnerReviewText(PortfolioWeeklySummary summary)
        {
            if (summary == null)
            {
                return "Weekly Portfolio Review — no portfolio summary was provided.";
            }

            return $"Weekly Portfolio Review — {FormatBusinessCount(summary.businessCount)} | cash drift {FormatMoney(summary.financial.totalNetCashChangeCents)} | stock health {FormatPercent01(summary.averageStockHealth01)} | staffing {FormatStaffing(summary.totalFilledWorkerSlots, summary.totalOpenWorkerSlots)} | payroll read {FormatPayrollRead(summary.fallbackPayrollBusinessCount, summary.totalOpenSlotPlannedPayrollCents)} | low-stock businesses {ClampNonNegativeCount(summary.lowStockBusinessCount)} | notes {BusinessReportBuilder.CountReportNotes(summary.gaps)} / blockers {BusinessReportBuilder.CountBlockingGaps(summary.gaps)}. Focus: {BuildWeeklyOwnerFocus(summary)}";
        }

        public static string BuildMonthlyOwnerReviewText(PortfolioMonthlySummary summary)
        {
            if (summary == null)
            {
                return "Monthly Portfolio Review — no portfolio summary was provided.";
            }

            PortfolioBalanceDriftSummary drift = summary.balanceDrift ?? new PortfolioBalanceDriftSummary();
            string wealthDrift = drift.HasWealthAndDebtRead
                ? $"net worth drift {FormatMoney(drift.netWorthDriftCents)} | debt drift {FormatMoney(drift.debtDriftCents)}"
                : "net worth/debt drift not supplied";
            string businessCashRead = drift.hasBusinessCashSnapshots
                ? $"business cash drift {FormatMoney(drift.businessCashDriftCents)}"
                : $"business net cash change {FormatMoney(summary.financial.totalNetCashChangeCents)}";
            string header = "Monthly Portfolio Review" + FormatReviewWindowSuffix(drift.reviewWindowLabel);
            return $"{header} — {FormatBusinessCount(summary.businessCount)} {FormatMonthlyCoverage(summary)} | {wealthDrift} | {businessCashRead} | known profit proxy {FormatMoney(summary.financial.totalKnownProfitProxyCents)} | owner distributions {FormatMoney(summary.financial.totalOwnerDistributionCents)} | stock health {FormatPercent01(summary.averageStockHealth01)} | staffing {FormatStaffing(summary.totalFilledWorkerSlots, summary.totalOpenWorkerSlots)} | payroll read {FormatPayrollRead(summary.fallbackPayrollBusinessCount, summary.totalOpenSlotPlannedPayrollCents)} | notes {BusinessReportBuilder.CountReportNotes(summary.gaps)} / blockers {BusinessReportBuilder.CountBlockingGaps(summary.gaps)}. Focus: {BuildMonthlyOwnerFocus(summary)}";
        }

        public static string BuildMonthlyOwnerReviewText(
            PortfolioMonthlySummary summary,
            int openingNetWorthCents,
            int closingNetWorthCents,
            int openingDebtCents,
            int closingDebtCents)
        {
            ApplyMonthlyBalanceDrift(summary, openingNetWorthCents, closingNetWorthCents, openingDebtCents, closingDebtCents);
            return BuildMonthlyOwnerReviewText(summary);
        }

        public static string BuildMonthlyOwnerReviewText(
            PortfolioMonthlySummary summary,
            int openingNetWorthCents,
            int closingNetWorthCents,
            int openingDebtCents,
            int closingDebtCents,
            int openingBusinessCashCents,
            int closingBusinessCashCents)
        {
            ApplyMonthlyBalanceDrift(summary, openingNetWorthCents, closingNetWorthCents, openingDebtCents, closingDebtCents, openingBusinessCashCents, closingBusinessCashCents);
            return BuildMonthlyOwnerReviewText(summary);
        }

        public static string BuildMonthlyOwnerReviewText(
            PortfolioMonthlySummary summary,
            string reviewWindowLabel,
            int openingNetWorthCents,
            int closingNetWorthCents,
            int openingDebtCents,
            int closingDebtCents,
            int openingBusinessCashCents,
            int closingBusinessCashCents)
        {
            ApplyMonthlyBalanceDrift(summary, reviewWindowLabel, openingNetWorthCents, closingNetWorthCents, openingDebtCents, closingDebtCents, openingBusinessCashCents, closingBusinessCashCents);
            return BuildMonthlyOwnerReviewText(summary);
        }

        public static void ApplyMonthlyBalanceDrift(
            PortfolioMonthlySummary summary,
            int openingNetWorthCents,
            int closingNetWorthCents,
            int openingDebtCents,
            int closingDebtCents)
        {
            ApplyMonthlyBalanceDrift(summary, string.Empty, openingNetWorthCents, closingNetWorthCents, openingDebtCents, closingDebtCents, 0, 0, true, false);
        }

        public static void ApplyMonthlyBalanceDrift(
            PortfolioMonthlySummary summary,
            int openingNetWorthCents,
            int closingNetWorthCents,
            int openingDebtCents,
            int closingDebtCents,
            int openingBusinessCashCents,
            int closingBusinessCashCents)
        {
            ApplyMonthlyBalanceDrift(summary, string.Empty, openingNetWorthCents, closingNetWorthCents, openingDebtCents, closingDebtCents, openingBusinessCashCents, closingBusinessCashCents, true, true);
        }

        public static void ApplyMonthlyBalanceDrift(
            PortfolioMonthlySummary summary,
            string reviewWindowLabel,
            int openingNetWorthCents,
            int closingNetWorthCents,
            int openingDebtCents,
            int closingDebtCents,
            int openingBusinessCashCents,
            int closingBusinessCashCents)
        {
            ApplyMonthlyBalanceDrift(summary, reviewWindowLabel, openingNetWorthCents, closingNetWorthCents, openingDebtCents, closingDebtCents, openingBusinessCashCents, closingBusinessCashCents, true, true);
        }

        private static void ApplyMonthlyBalanceDrift(
            PortfolioMonthlySummary summary,
            string reviewWindowLabel,
            int openingNetWorthCents,
            int closingNetWorthCents,
            int openingDebtCents,
            int closingDebtCents,
            int openingBusinessCashCents,
            int closingBusinessCashCents,
            bool includeWealthDrift,
            bool includeBusinessCashDrift)
        {
            if (summary == null)
            {
                return;
            }

            if (summary.balanceDrift == null)
            {
                summary.balanceDrift = new PortfolioBalanceDriftSummary();
            }
            summary.balanceDrift.reviewWindowLabel = NormalizeReviewWindowLabel(reviewWindowLabel);
            summary.balanceDrift.hasNetWorthSnapshots = includeWealthDrift;
            summary.balanceDrift.openingNetWorthCents = openingNetWorthCents;
            summary.balanceDrift.closingNetWorthCents = closingNetWorthCents;
            summary.balanceDrift.netWorthDriftCents = SubtractCents(closingNetWorthCents, openingNetWorthCents);
            summary.balanceDrift.hasDebtSnapshots = includeWealthDrift;
            summary.balanceDrift.openingDebtCents = openingDebtCents;
            summary.balanceDrift.closingDebtCents = closingDebtCents;
            summary.balanceDrift.debtDriftCents = SubtractCents(closingDebtCents, openingDebtCents);
            summary.balanceDrift.hasBusinessCashSnapshots = includeBusinessCashDrift;
            summary.balanceDrift.openingBusinessCashCents = openingBusinessCashCents;
            summary.balanceDrift.closingBusinessCashCents = closingBusinessCashCents;
            summary.balanceDrift.businessCashDriftCents = SubtractCents(closingBusinessCashCents, openingBusinessCashCents);
        }

        private static void FillCommon(PortfolioWeeklySummary summary, PortfolioReportSnapshot snapshot)
        {
            summary.period = snapshot.period;
            summary.generatedOnDate = snapshot.generatedOnDate;
            summary.ownedLandCount = Math.Max(0, snapshot.ownedLandCount);
            summary.ownedBusinessCount = Math.Max(0, snapshot.ownedBusinessCount);
            FillFromBusinessSnapshots(
                snapshot.businesses,
                summary.financial,
                summary.businessLines,
                summary.ledgerEntries,
                summary.gaps,
                ReportPeriodKind.Weekly,
                out int businessCount,
                out float averageStockHealth,
                out int lowStockBusinessCount,
                out int totalFilledSlots,
                out int totalOpenSlots,
                out int openSlotPlannedPayroll,
                out int fallbackPayrollBusinessCount);
            summary.businessCount = businessCount;
            summary.averageStockHealth01 = averageStockHealth;
            summary.lowStockBusinessCount = lowStockBusinessCount;
            summary.totalFilledWorkerSlots = totalFilledSlots;
            summary.totalOpenWorkerSlots = totalOpenSlots;
            summary.totalOpenSlotPlannedPayrollCents = openSlotPlannedPayroll;
            summary.fallbackPayrollBusinessCount = fallbackPayrollBusinessCount;
            FillPortfolioGaps(summary.gaps, snapshot.gapNotes);
            BusinessReportBuilder.CopyLedgerEntries(summary.ledgerEntries, snapshot.ledgerEntries);
            BusinessReportBuilder.AddDefaultGaps(summary.gaps);
        }

        private static void FillCommon(PortfolioMonthlySummary summary, PortfolioReportSnapshot snapshot)
        {
            summary.period = snapshot.period;
            summary.generatedOnDate = snapshot.generatedOnDate;
            summary.ownedLandCount = Math.Max(0, snapshot.ownedLandCount);
            summary.ownedBusinessCount = Math.Max(0, snapshot.ownedBusinessCount);
            FillFromBusinessSnapshots(
                snapshot.businesses,
                summary.financial,
                summary.businessLines,
                summary.ledgerEntries,
                summary.gaps,
                ReportPeriodKind.Monthly,
                out int businessCount,
                out float averageStockHealth,
                out int lowStockBusinessCount,
                out int totalFilledSlots,
                out int totalOpenSlots,
                out int openSlotPlannedPayroll,
                out int fallbackPayrollBusinessCount);
            summary.businessCount = businessCount;
            summary.averageStockHealth01 = averageStockHealth;
            summary.lowStockBusinessCount = lowStockBusinessCount;
            summary.totalFilledWorkerSlots = totalFilledSlots;
            summary.totalOpenWorkerSlots = totalOpenSlots;
            summary.totalOpenSlotPlannedPayrollCents = openSlotPlannedPayroll;
            summary.fallbackPayrollBusinessCount = fallbackPayrollBusinessCount;
            FillPortfolioGaps(summary.gaps, snapshot.gapNotes);
            BusinessReportBuilder.CopyLedgerEntries(summary.ledgerEntries, snapshot.ledgerEntries);
            BusinessReportBuilder.AddDefaultGaps(summary.gaps);
        }

        private static void FillFromBusinessSnapshots(
            IReadOnlyList<BusinessReportSnapshot> snapshots,
            PortfolioFinancialRollup financial,
            ICollection<PortfolioBusinessSummaryLine> lines,
            ICollection<LedgerEntry> ledgerEntries,
            ICollection<BusinessReportGap> gaps,
            ReportPeriodKind businessSummaryKind,
            out int businessCount,
            out float averageStockHealth,
            out int lowStockBusinessCount,
            out int totalFilledSlots,
            out int totalOpenSlots,
            out int totalOpenSlotPlannedPayrollCents,
            out int fallbackPayrollBusinessCount)
        {
            businessCount = 0;
            lowStockBusinessCount = 0;
            totalFilledSlots = 0;
            totalOpenSlots = 0;
            totalOpenSlotPlannedPayrollCents = 0;
            fallbackPayrollBusinessCount = 0;
            double stockHealthTotal = 0d;

            if (snapshots == null || snapshots.Count == 0)
            {
                averageStockHealth = 1f;
                BusinessReportBuilder.AddReportGap(gaps, "missing_business_snapshots", "No business snapshots were provided for portfolio aggregation.", BusinessReportGapSeverity.Blocking);
                return;
            }

            for (int i = 0; i < snapshots.Count; i++)
            {
                if (snapshots[i] == null)
                {
                    BusinessReportBuilder.AddReportGap(gaps, "missing_business_snapshot", $"Business snapshot at index {i} was missing and was excluded from the portfolio rollup.", BusinessReportGapSeverity.Blocking);
                    continue;
                }

                BusinessWeeklySummary weekly = null;
                BusinessMonthlySummary monthly = null;
                BusinessReportHeader header;
                BusinessFinancialRollup businessFinancial;
                BusinessInventorySummary inventory;
                BusinessStaffingSummary staffing;
                BusinessOperationsSummary operations;
                BusinessCapacitySummary capacity;
                IReadOnlyList<BusinessReportGap> businessGaps;
                IReadOnlyList<LedgerEntry> businessLedgerEntries;

                if (businessSummaryKind == ReportPeriodKind.Monthly)
                {
                    monthly = BusinessReportBuilder.BuildMonthly(snapshots[i]);
                    header = monthly.header;
                    businessFinancial = monthly.financial;
                    inventory = monthly.inventory;
                    staffing = monthly.staffing;
                    operations = monthly.operations;
                    capacity = monthly.capacity;
                    businessGaps = monthly.gaps;
                    businessLedgerEntries = monthly.ledgerEntries;
                }
                else
                {
                    weekly = BusinessReportBuilder.BuildWeekly(snapshots[i]);
                    header = weekly.header;
                    businessFinancial = weekly.financial;
                    inventory = weekly.inventory;
                    staffing = weekly.staffing;
                    operations = weekly.operations;
                    capacity = weekly.capacity;
                    businessGaps = weekly.gaps;
                    businessLedgerEntries = weekly.ledgerEntries;
                }

                businessCount = AddNonNegativeCount(businessCount, 1);
                AddFinancial(financial, businessFinancial);
                stockHealthTotal += SafeStockHealth01(inventory.stockHealth01);
                if (IsLowStockBusiness(inventory))
                {
                    lowStockBusinessCount = AddNonNegativeCount(lowStockBusinessCount, 1);
                }

                totalFilledSlots = AddNonNegativeCount(totalFilledSlots, staffing.filledWorkerSlots);
                totalOpenSlots = AddNonNegativeCount(totalOpenSlots, staffing.openWorkerSlots);
                totalOpenSlotPlannedPayrollCents = AddCents(totalOpenSlotPlannedPayrollCents, staffing.openSlotPlannedPayrollCents);
                if (staffing.usesFallbackPayroll)
                {
                    fallbackPayrollBusinessCount = AddNonNegativeCount(fallbackPayrollBusinessCount, 1);
                }

                lines?.Add(BuildLine(header, businessFinancial, inventory, staffing, operations, capacity, businessGaps));
                BusinessReportBuilder.CopyLedgerEntries(ledgerEntries, businessLedgerEntries);
                BusinessReportBuilder.CopyGaps(gaps, businessGaps);
            }

            averageStockHealth = CalculateAverageStockHealth(stockHealthTotal, businessCount);
        }

        private static PortfolioBusinessSummaryLine BuildLine(
            BusinessReportHeader header,
            BusinessFinancialRollup financial,
            BusinessInventorySummary inventory,
            BusinessStaffingSummary staffing,
            BusinessOperationsSummary operations,
            BusinessCapacitySummary capacity,
            IReadOnlyList<BusinessReportGap> gaps)
        {
            return new PortfolioBusinessSummaryLine
            {
                businessId = header.businessId,
                businessDisplayName = header.businessDisplayName,
                businessTypeKey = header.businessTypeKey,
                buildingId = header.buildingId,
                revenueCents = financial.revenueCents,
                costOfGoodsSoldCents = financial.costOfGoodsSoldCents,
                knownExpenseCents = financial.knownExpenseCents,
                netCashChangeCents = financial.netCashChangeCents,
                closingCashCents = financial.closingCashCents,
                survivalReserveCents = financial.survivalReserveCents,
                cashRunwayWeeks = financial.cashRunwayWeeks,
                primaryBlockedReason = financial.primaryBlockedReason,
                cashBridgeSummary = financial.cashBridgeSummary,
                focusDisplayName = header.focusDisplayName,
                capacityBottleneckKey = capacity != null ? capacity.bottleneckKey : string.Empty,
                stockHealth01 = SafeStockHealth01(inventory.stockHealth01),
                lowStockCategoryCount = inventory.lowStockCategoryCount,
                filledWorkerSlots = ClampNonNegativeCount(staffing.filledWorkerSlots),
                openWorkerSlots = ClampNonNegativeCount(staffing.openWorkerSlots),
                weeklyPayrollCents = Math.Max(0, staffing.weeklyPayrollCents),
                filledWorkerPayrollCents = Math.Max(0, staffing.filledWorkerPayrollCents),
                openSlotPlannedPayrollCents = Math.Max(0, staffing.openSlotPlannedPayrollCents),
                payrollSource = staffing.payrollSource,
                usesFallbackPayroll = staffing.usesFallbackPayroll,
                payrollReadSummary = staffing.payrollReadSummary ?? string.Empty,
                reorderTriggered = operations.reorderTriggered,
                hasReportGaps = BusinessReportBuilder.CountBlockingGaps(gaps) > 0,
                reportNoteCount = BusinessReportBuilder.CountReportNotes(gaps),
                blockingReportGapCount = BusinessReportBuilder.CountBlockingGaps(gaps)
            };
        }

        private static void AddFinancial(PortfolioFinancialRollup portfolio, BusinessFinancialRollup business)
        {
            portfolio.totalOpeningCashCents = AddCents(portfolio.totalOpeningCashCents, business.openingCashCents);
            portfolio.totalClosingCashCents = AddCents(portfolio.totalClosingCashCents, business.closingCashCents);
            portfolio.totalRevenueCents = AddCents(portfolio.totalRevenueCents, business.revenueCents);
            portfolio.totalCostOfGoodsSoldCents = AddCents(portfolio.totalCostOfGoodsSoldCents, business.costOfGoodsSoldCents);
            portfolio.totalPayrollExpenseCents = AddCents(portfolio.totalPayrollExpenseCents, business.payrollExpenseCents);
            portfolio.totalReorderExpenseCents = AddCents(portfolio.totalReorderExpenseCents, business.reorderExpenseCents);
            portfolio.totalLocalSupplyExpenseCents = AddCents(portfolio.totalLocalSupplyExpenseCents, business.localSupplyExpenseCents);
            portfolio.totalOwnerDistributionCents = AddCents(portfolio.totalOwnerDistributionCents, business.ownerDistributionCents);
            portfolio.totalCashTransferInCents = AddCents(portfolio.totalCashTransferInCents, business.cashTransferInCents);
            portfolio.totalCashTransferOutCents = AddCents(portfolio.totalCashTransferOutCents, business.cashTransferOutCents);
            portfolio.totalKnownExpenseCents = AddCents(portfolio.totalKnownExpenseCents, business.knownExpenseCents);
            portfolio.totalNetCashChangeCents = AddCents(portfolio.totalNetCashChangeCents, business.netCashChangeCents);
            portfolio.totalKnownProfitProxyCents = AddCents(portfolio.totalKnownProfitProxyCents, business.knownProfitProxyCents);
        }

        private static void AddStaffingTotals(PortfolioMonthlySummary summary, BusinessStaffingSummary staffing)
        {
            summary.totalFilledWorkerSlots = AddNonNegativeCount(summary.totalFilledWorkerSlots, staffing.filledWorkerSlots);
            summary.totalOpenWorkerSlots = AddNonNegativeCount(summary.totalOpenWorkerSlots, staffing.openWorkerSlots);
            summary.totalOpenSlotPlannedPayrollCents = AddCents(summary.totalOpenSlotPlannedPayrollCents, staffing.openSlotPlannedPayrollCents);
            if (staffing.usesFallbackPayroll)
            {
                summary.fallbackPayrollBusinessCount = AddNonNegativeCount(summary.fallbackPayrollBusinessCount, 1);
            }
        }

        private static bool IsLowStockBusiness(BusinessInventorySummary inventory)
        {
            return inventory != null
                && (inventory.lowStockCategoryCount > 0 || SafeStockHealth01(inventory.stockHealth01) < 0.6f);
        }

        private static void ValidatePortfolioSnapshot(ICollection<BusinessReportGap> gaps, PortfolioReportSnapshot snapshot, ReportPeriodKind expectedKind)
        {
            if (snapshot.period.Kind != expectedKind)
            {
                BusinessReportBuilder.AddReportGap(gaps, "portfolio_period_kind_mismatch", $"Portfolio snapshot period was {snapshot.period.Kind}, but the report expected {expectedKind}.", BusinessReportGapSeverity.Blocking);
            }

            if (snapshot.ownedLandCount < 0)
            {
                BusinessReportBuilder.AddReportGap(gaps, "negative_owned_land_count", "Owned land count was negative and was clamped for reporting.", BusinessReportGapSeverity.Caution);
            }

            if (snapshot.ownedBusinessCount < 0)
            {
                BusinessReportBuilder.AddReportGap(gaps, "negative_owned_business_count", "Owned business count was negative and was clamped for reporting.", BusinessReportGapSeverity.Caution);
            }

            if (snapshot.ownedBusinessCount > 0 && snapshot.businesses != null && snapshot.businesses.Count != snapshot.ownedBusinessCount)
            {
                BusinessReportBuilder.AddReportGap(gaps, "owned_business_count_mismatch", "Owned business count did not match provided business snapshots.", BusinessReportGapSeverity.Caution);
            }

            if (snapshot.ledgerEntries == null)
            {
                return;
            }

            for (int i = 0; i < snapshot.ledgerEntries.Count; i++)
            {
                LedgerEntry entry = snapshot.ledgerEntries[i];
                if (entry.IsGeneratedReportSummary)
                {
                    BusinessReportBuilder.AddReportGap(gaps, "generated_portfolio_ledger_in_snapshot", "Runtime portfolio snapshot contained generated report summary ledger rows.", BusinessReportGapSeverity.Caution);
                }

                if (entry.Quantity < 0)
                {
                    BusinessReportBuilder.AddReportGap(gaps, "negative_portfolio_ledger_quantity", $"Portfolio ledger row '{entry.EntryId}' had negative quantity and was normalized when copied.", BusinessReportGapSeverity.Caution);
                }
            }
        }

        private static string BuildWeeklyOwnerFocus(PortfolioWeeklySummary summary)
        {
            if (summary == null)
            {
                return "no weekly portfolio data.";
            }

            if (BusinessReportBuilder.CountBlockingGaps(summary.gaps) > 0)
            {
                return "resolve blocking report gaps before treating this weekly read as complete.";
            }

            if (summary.fallbackPayrollBusinessCount > 0)
            {
                return "payroll coverage includes fallback reads; verify staffing costs before major cash decisions.";
            }

            if (summary.financial.totalNetCashChangeCents < 0)
            {
                return "cash moved backward this week; review cost of goods, payroll, reorders, and owner transfers.";
            }

            if (summary.lowStockBusinessCount > 0 || SafeStockHealth01(summary.averageStockHealth01) < 0.6f)
            {
                return "stock discipline is the clearest near-term pressure.";
            }

            if (summary.totalOpenWorkerSlots > 0)
            {
                return "staffing coverage has open slots worth checking before the next trade rhythm.";
            }

            return "weekly posture is readable; watch Saturday readiness and small corrective actions.";
        }

        private static string BuildMonthlyOwnerFocus(PortfolioMonthlySummary summary)
        {
            if (summary == null)
            {
                return "no monthly portfolio data.";
            }

            PortfolioBalanceDriftSummary drift = summary.balanceDrift ?? new PortfolioBalanceDriftSummary();
            if (!drift.HasWealthAndDebtRead)
            {
                return "attach opening and closing net worth/debt snapshots before using this as the full monthly owner review.";
            }

            if (!drift.hasBusinessCashSnapshots)
            {
                return "attach opening and closing business cash snapshots when you need true balance drift rather than activity-only cash change.";
            }

            if (BusinessReportBuilder.CountBlockingGaps(summary.gaps) > 0)
            {
                return "resolve blocking report gaps before making sale, closure, or escalation calls.";
            }

            if (summary.fallbackPayrollBusinessCount > 0)
            {
                return "some payroll reads are fallback-based; verify staffing cost coverage before large capital moves.";
            }

            if (summary.financial.totalKnownProfitProxyCents < 0 || summary.financial.totalNetCashChangeCents < 0)
            {
                return "identify underperformers and decide whether they need stabilization, capital, sale, or closure review.";
            }

            if (summary.lowStockBusinessCount > 0 || SafeStockHealth01(summary.averageStockHealth01) < 0.65f)
            {
                return "inventory reliability is dragging the portfolio; review supplier and reorder posture.";
            }

            if (summary.totalOpenWorkerSlots > 0)
            {
                return "staffing gaps remain a portfolio-scale risk.";
            }

            return "monthly posture is stable enough for expansion, acquisition, or project review.";
        }

        private static string FormatReviewWindowSuffix(string reviewWindowLabel)
        {
            string label = string.IsNullOrWhiteSpace(reviewWindowLabel)
                ? string.Empty
                : reviewWindowLabel.Trim().Replace("\r", " ").Replace("\n", " ");
            return string.IsNullOrWhiteSpace(label) ? string.Empty : $" ({label})";
        }

        private static string NormalizeReviewWindowLabel(string reviewWindowLabel)
        {
            return string.IsNullOrWhiteSpace(reviewWindowLabel)
                ? string.Empty
                : reviewWindowLabel.Trim().Replace("\r", " ").Replace("\n", " ");
        }

        private static string FormatMonthlyCoverage(PortfolioMonthlySummary summary)
        {
            int weekCount = ClampNonNegativeCount(summary.includedWeekCount);
            return weekCount > 0
                ? $"across {weekCount} included weeks"
                : "from monthly snapshots";
        }

        private static string FormatPayrollRead(int fallbackBusinessCount, int openSlotPlannedPayrollCents)
        {
            int fallbackCount = ClampNonNegativeCount(fallbackBusinessCount);
            int openPlan = Math.Max(0, openSlotPlannedPayrollCents);
            if (fallbackCount <= 0 && openPlan <= 0)
            {
                return "direct worker rows";
            }

            if (fallbackCount > 0 && openPlan > 0)
            {
                return $"{fallbackCount} fallback, open-slot plan {FormatMoney(openPlan)}";
            }

            return fallbackCount > 0
                ? $"{fallbackCount} fallback"
                : $"open-slot plan {FormatMoney(openPlan)}";
        }

        private static string FormatBusinessCount(int businessCount)
        {
            int count = ClampNonNegativeCount(businessCount);
            return count == 1 ? "1 business" : $"{count} businesses";
        }

        private static string FormatStaffing(int filledSlots, int openSlots)
        {
            return $"{ClampNonNegativeCount(filledSlots)} filled / {ClampNonNegativeCount(openSlots)} open";
        }

        private static string FormatPercent01(float value)
        {
            float safe = SafeStockHealth01(value);
            return (safe * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        private static string FormatMoney(int cents)
        {
            long amount = cents;
            string sign = amount < 0 ? "-" : string.Empty;
            decimal dollars = Math.Abs(amount) / 100m;
            return $"{sign}${dollars:0.00}";
        }

        private static void FillPortfolioGaps(ICollection<BusinessReportGap> gaps, IReadOnlyList<string> gapNotes)
        {
            if (gaps == null || gapNotes == null)
            {
                return;
            }

            for (int i = 0; i < gapNotes.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(gapNotes[i]))
                {
                    BusinessReportBuilder.AddReportGap(gaps, "portfolio_snapshot_gap", gapNotes[i], BusinessReportGapSeverity.Blocking);
                }
            }
        }

        private static int AddCents(int currentCents, int deltaCents)
        {
            long total = (long)currentCents + deltaCents;
            if (total > int.MaxValue)
            {
                return int.MaxValue;
            }

            if (total < int.MinValue)
            {
                return int.MinValue;
            }

            return (int)total;
        }

        private static int SubtractCents(int endingCents, int startingCents)
        {
            long total = (long)endingCents - startingCents;
            if (total > int.MaxValue)
            {
                return int.MaxValue;
            }

            if (total < int.MinValue)
            {
                return int.MinValue;
            }

            return (int)total;
        }

        private static int AddNonNegativeCount(int current, int delta)
        {
            long total = (long)ClampNonNegativeCount(current) + ClampNonNegativeCount(delta);
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        private static int ClampNonNegativeCount(int value)
        {
            return value > 0 ? value : 0;
        }

        private static float SafeStockHealth01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 1f;
            }

            return BusinessReportBuilder.Clamp01(value);
        }

        private static float CalculateAverageStockHealth(double stockHealthTotal, int validCount)
        {
            if (validCount <= 0 || double.IsNaN(stockHealthTotal) || double.IsInfinity(stockHealthTotal))
            {
                return 1f;
            }

            double average = stockHealthTotal / validCount;
            if (double.IsNaN(average) || double.IsInfinity(average))
            {
                return 1f;
            }

            return BusinessReportBuilder.Clamp01((float)average);
        }
    }
}
