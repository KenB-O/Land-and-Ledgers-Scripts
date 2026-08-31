using System;
using System.Collections.Generic;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Reporting
{
    public static class BusinessReportBuilder
    {
        private const float DefaultLowStockThreshold01 = 0.25f;

        public static BusinessWeeklySummary BuildWeekly(BusinessReportSnapshot snapshot)
        {
            BusinessWeeklySummary summary = new();
            if (snapshot == null)
            {
                AddDefaultGaps(summary.gaps);
                AddReportGap(summary.gaps, "missing_snapshot", "No business snapshot was provided.", BusinessReportGapSeverity.Blocking);
                return summary;
            }

            ValidateSnapshot(summary.gaps, snapshot, ReportPeriodKind.Weekly);
            FillHeader(summary.header, snapshot);
            FillFinancial(summary.financial, snapshot);
            FillOperations(summary.operations, snapshot);
            FillCapacity(summary.capacity, snapshot);
            FillInventory(summary.inventory, snapshot.categories, snapshot.stockHealth01, DefaultLowStockThreshold01, summary.gaps);
            FillStaffing(summary.staffing, snapshot.workers, snapshot.payrollExpenseCents, summary.gaps);
            CopyLedgerEntries(summary.ledgerEntries, snapshot.ledgerEntries);
            AddSnapshotLedgerEntries(summary.ledgerEntries, snapshot, "weekly");
            FillGaps(summary.gaps, snapshot.gapNotes);
            AddDefaultGaps(summary.gaps);
            return summary;
        }

        public static BusinessMonthlySummary BuildMonthly(BusinessReportSnapshot snapshot)
        {
            BusinessMonthlySummary summary = new();
            if (snapshot == null)
            {
                AddDefaultGaps(summary.gaps);
                AddReportGap(summary.gaps, "missing_snapshot", "No business snapshot was provided.", BusinessReportGapSeverity.Blocking);
                return summary;
            }

            ValidateSnapshot(summary.gaps, snapshot, ReportPeriodKind.Monthly);
            FillHeader(summary.header, snapshot);
            FillFinancial(summary.financial, snapshot);
            FillOperations(summary.operations, snapshot);
            FillCapacity(summary.capacity, snapshot);
            FillInventory(summary.inventory, snapshot.categories, snapshot.stockHealth01, DefaultLowStockThreshold01, summary.gaps);
            FillStaffing(summary.staffing, snapshot.workers, snapshot.payrollExpenseCents, summary.gaps);
            summary.includedWeekCount = 0;
            CopyLedgerEntries(summary.ledgerEntries, snapshot.ledgerEntries);
            AddSnapshotLedgerEntries(summary.ledgerEntries, snapshot, "monthly");
            FillGaps(summary.gaps, snapshot.gapNotes);
            AddDefaultGaps(summary.gaps);
            AddReportGap(summary.gaps, "monthly_snapshot_source", "Monthly summary was built from a direct monthly snapshot; weekly cadence fields require weekly summaries.", BusinessReportGapSeverity.Caution);
            return summary;
        }

        public static BusinessMonthlySummary BuildMonthlyFromWeeklySummaries(
            ReportPeriod period,
            SimulationDate generatedOnDate,
            IReadOnlyList<BusinessWeeklySummary> weeklySummaries)
        {
            BusinessMonthlySummary summary = new();
            summary.header.period = period;
            summary.header.generatedOnDate = generatedOnDate;
            summary.header.isCompletePeriod = period.IsCompletePeriod;

            if (weeklySummaries == null || weeklySummaries.Count == 0)
            {
                AddDefaultGaps(summary.gaps);
                AddReportGap(summary.gaps, "missing_weekly_summaries", "No weekly summaries were provided for monthly aggregation.", BusinessReportGapSeverity.Blocking);
                return summary;
            }

            List<BusinessWeeklySummary> validWeeks = new();
            string businessId = string.Empty;
            for (int i = 0; i < weeklySummaries.Count; i++)
            {
                BusinessWeeklySummary week = weeklySummaries[i];
                if (week == null || week.header == null)
                {
                    AddReportGap(summary.gaps, "invalid_weekly_summary", $"Weekly summary at index {i} was missing and was excluded from monthly aggregation.", BusinessReportGapSeverity.Blocking);
                    continue;
                }

                if (week.header.period.Kind != ReportPeriodKind.Weekly)
                {
                    AddReportGap(summary.gaps, "non_weekly_summary", $"Summary for {week.header.businessDisplayName ?? week.header.businessId ?? "unknown business"} was not weekly and was excluded from weekly-to-monthly aggregation.", BusinessReportGapSeverity.Blocking);
                    continue;
                }

                string currentBusinessId = week.header.businessId ?? string.Empty;
                if (validWeeks.Count == 0)
                {
                    businessId = currentBusinessId;
                }
                else if (!string.Equals(businessId, currentBusinessId, StringComparison.Ordinal))
                {
                    AddReportGap(summary.gaps, "mixed_business_weekly_summaries", $"Weekly summary for business '{currentBusinessId}' was excluded from monthly aggregation for '{businessId}'.", BusinessReportGapSeverity.Blocking);
                    continue;
                }

                validWeeks.Add(week);
            }

            if (validWeeks.Count == 0)
            {
                AddDefaultGaps(summary.gaps);
                AddReportGap(summary.gaps, "no_valid_weekly_summaries", "No valid weekly summaries remained for monthly aggregation.", BusinessReportGapSeverity.Blocking);
                return summary;
            }

            BusinessWeeklySummary first = validWeeks[0];
            BusinessWeeklySummary last = validWeeks[validWeeks.Count - 1];
            CopyHeaderForMonthly(summary.header, first.header, period, generatedOnDate);
            summary.financial.openingCashCents = first.financial.openingCashCents;
            summary.financial.closingCashCents = last.financial.closingCashCents;
            summary.includedWeekCount = validWeeks.Count;

            int bestRevenue = int.MinValue;
            int weakestRevenue = int.MaxValue;
            float stockHealthTotal = 0f;
            float reliabilityTotal = 0f;
            float competitionTotal = 0f;
            Dictionary<string, InventoryAccumulator> categoryTotals = new(StringComparer.Ordinal);

            for (int i = 0; i < validWeeks.Count; i++)
            {
                BusinessWeeklySummary week = validWeeks[i];
                summary.financial.revenueCents = AddCents(summary.financial.revenueCents, week.financial.revenueCents);
                summary.financial.costOfGoodsSoldCents = AddCents(summary.financial.costOfGoodsSoldCents, week.financial.costOfGoodsSoldCents);
                summary.financial.payrollExpenseCents = AddCents(summary.financial.payrollExpenseCents, week.financial.payrollExpenseCents);
                summary.financial.reorderExpenseCents = AddCents(summary.financial.reorderExpenseCents, week.financial.reorderExpenseCents);
                summary.financial.localSupplyExpenseCents = AddCents(summary.financial.localSupplyExpenseCents, week.financial.localSupplyExpenseCents);
                summary.financial.ownerDistributionCents = AddCents(summary.financial.ownerDistributionCents, week.financial.ownerDistributionCents);
                summary.financial.cashTransferInCents = AddCents(summary.financial.cashTransferInCents, week.financial.cashTransferInCents);
                summary.financial.cashTransferOutCents = AddCents(summary.financial.cashTransferOutCents, week.financial.cashTransferOutCents);
                summary.financial.otherExpenseCents = AddCents(summary.financial.otherExpenseCents, week.financial.otherExpenseCents);
                summary.operations.unitsSold = AddNonNegativeCount(summary.operations.unitsSold, week.operations.unitsSold);
                summary.operations.customerVisits = AddNonNegativeCount(summary.operations.customerVisits, week.operations.customerVisits);
                summary.operations.reorderUnitsQueued = AddNonNegativeCount(summary.operations.reorderUnitsQueued, week.operations.reorderUnitsQueued);
                summary.operations.reorderUnitsReceived = AddNonNegativeCount(summary.operations.reorderUnitsReceived, week.operations.reorderUnitsReceived);
                summary.operations.reorderTriggered |= week.operations.reorderTriggered;
                stockHealthTotal += Clamp01(week.inventory.stockHealth01);
                reliabilityTotal += Clamp01(week.operations.reliability01);
                competitionTotal += Clamp01(week.operations.competitionPressure01);
                CopyStaffing(summary.staffing, week.staffing);
                AccumulateInventoryCategories(categoryTotals, week.inventory);
                CopyLedgerEntries(summary.ledgerEntries, week.ledgerEntries);
                CopyGaps(summary.gaps, week.gaps);

                if (week.financial.revenueCents > bestRevenue)
                {
                    bestRevenue = week.financial.revenueCents;
                    summary.bestRevenueWeek = week.header.period.Week;
                    summary.bestRevenueWeekCents = week.financial.revenueCents;
                }

                if (week.financial.revenueCents < weakestRevenue)
                {
                    weakestRevenue = week.financial.revenueCents;
                    summary.weakestRevenueWeek = week.header.period.Week;
                    summary.weakestRevenueWeekCents = week.financial.revenueCents;
                }
            }

            summary.financial.knownExpenseCents = SumKnownExpenses(summary.financial);
            summary.financial.netCashChangeCents = SubtractCents(summary.financial.closingCashCents, summary.financial.openingCashCents);
            summary.financial.knownProfitProxyCents = SubtractCents(AddCents(summary.financial.revenueCents, summary.financial.cashTransferInCents), summary.financial.knownExpenseCents);
            summary.financial.unknownExpenseCents = Math.Max(0, SubtractCents(summary.financial.knownProfitProxyCents, summary.financial.netCashChangeCents));
            summary.financial.survivalReserveCents = last.financial.survivalReserveCents;
            summary.financial.cashRunwayWeeks = last.financial.cashRunwayWeeks;
            summary.financial.primaryBlockedReason = last.financial.primaryBlockedReason;
            summary.financial.cashBridgeSummary = BuildCashBridgeSummary(summary.financial);
            summary.operations.averageRevenuePerVisitCents = DivideCents(summary.financial.revenueCents, summary.operations.customerVisits);
            summary.operations.averageRevenuePerUnitCents = DivideCents(summary.financial.revenueCents, summary.operations.unitsSold);
            summary.operations.reliability01 = Clamp01(reliabilityTotal / validWeeks.Count);
            summary.operations.competitionPressure01 = Clamp01(competitionTotal / validWeeks.Count);
            CopyCapacity(summary.capacity, last.capacity);
            ApplyInventoryAggregation(summary.inventory, categoryTotals, stockHealthTotal, validWeeks.Count);
            summary.averageWeeklyRevenueCents = DivideCents(summary.financial.revenueCents, validWeeks.Count);
            summary.averageWeeklyPayrollCents = DivideCents(summary.financial.payrollExpenseCents, validWeeks.Count);
            summary.averageWeeklyReorderExpenseCents = DivideCents(summary.financial.reorderExpenseCents, validWeeks.Count);
            AddReportGap(summary.gaps, "monthly_staffing_ending_state", "Monthly staffing rows show the latest included weekly staffing state; payroll totals remain in the financial rollup.", BusinessReportGapSeverity.Informational);
            AddReportGap(summary.gaps, "monthly_capacity_ending_state", "Monthly capacity shows the latest included weekly capacity state rather than a weekly average.", BusinessReportGapSeverity.Informational);
            AddDefaultGaps(summary.gaps);
            return summary;
        }

        private static void FillHeader(BusinessReportHeader header, BusinessReportSnapshot snapshot)
        {
            header.businessId = snapshot.businessId ?? string.Empty;
            header.businessDisplayName = string.IsNullOrWhiteSpace(snapshot.businessDisplayName) ? header.businessId : snapshot.businessDisplayName;
            header.businessTypeKey = snapshot.businessTypeKey ?? string.Empty;
            header.buildingId = snapshot.buildingId ?? string.Empty;
            header.focusId = snapshot.focusId ?? string.Empty;
            header.focusDisplayName = snapshot.focusDisplayName ?? string.Empty;
            header.focusSummary = snapshot.focusSummary ?? string.Empty;
            header.period = snapshot.period;
            header.generatedOnDate = snapshot.generatedOnDate;
            header.isCompletePeriod = snapshot.period.IsCompletePeriod;
        }

        private static void CopyHeaderForMonthly(BusinessReportHeader target, BusinessReportHeader source, ReportPeriod period, SimulationDate generatedOnDate)
        {
            target.businessId = source.businessId;
            target.businessDisplayName = source.businessDisplayName;
            target.businessTypeKey = source.businessTypeKey;
            target.buildingId = source.buildingId;
            target.focusId = source.focusId;
            target.focusDisplayName = source.focusDisplayName;
            target.focusSummary = source.focusSummary;
            target.period = period;
            target.generatedOnDate = generatedOnDate;
            target.isCompletePeriod = period.IsCompletePeriod;
        }

        private static void FillFinancial(BusinessFinancialRollup financial, BusinessReportSnapshot snapshot)
        {
            financial.openingCashCents = Math.Max(0, snapshot.openingCashCents);
            financial.closingCashCents = Math.Max(0, snapshot.closingCashCents);
            financial.revenueCents = Math.Max(0, snapshot.revenueCents);
            financial.costOfGoodsSoldCents = Math.Max(0, snapshot.costOfGoodsSoldCents);
            financial.payrollExpenseCents = Math.Max(0, snapshot.payrollExpenseCents);
            financial.reorderExpenseCents = Math.Max(0, snapshot.reorderExpenseCents);
            financial.localSupplyExpenseCents = Math.Max(0, snapshot.localSupplyExpenseCents);
            financial.ownerDistributionCents = Math.Max(0, snapshot.ownerDistributionCents);
            financial.cashTransferInCents = Math.Max(0, snapshot.cashTransferInCents);
            financial.cashTransferOutCents = Math.Max(0, snapshot.cashTransferOutCents);
            financial.otherExpenseCents = Math.Max(0, snapshot.otherExpenseCents);
            financial.knownExpenseCents = SumKnownExpenses(financial);
            financial.netCashChangeCents = SubtractCents(financial.closingCashCents, financial.openingCashCents);
            financial.knownProfitProxyCents = SubtractCents(AddCents(financial.revenueCents, financial.cashTransferInCents), financial.knownExpenseCents);
            financial.unknownExpenseCents = Math.Max(0, SubtractCents(financial.knownProfitProxyCents, financial.netCashChangeCents));
            financial.survivalReserveCents = Math.Max(0, snapshot.survivalReserveCents);
            financial.cashRunwayWeeks = snapshot.cashRunwayWeeks > 0 ? snapshot.cashRunwayWeeks : EstimateCashRunwayWeeks(financial);
            financial.primaryBlockedReason = snapshot.primaryBlockedReason ?? string.Empty;
            financial.cashBridgeSummary = BuildCashBridgeSummary(financial);
        }

        private static void FillOperations(BusinessOperationsSummary operations, BusinessReportSnapshot snapshot)
        {
            operations.unitsSold = Math.Max(0, snapshot.unitsSold);
            operations.customerVisits = Math.Max(0, snapshot.customerVisits);
            operations.averageRevenuePerVisitCents = DivideCents(snapshot.revenueCents, snapshot.customerVisits);
            operations.averageRevenuePerUnitCents = DivideCents(snapshot.revenueCents, snapshot.unitsSold);
            operations.reorderTriggered = snapshot.reorderTriggered;
            operations.reorderUnitsQueued = Math.Max(0, snapshot.reorderUnitsQueued);
            operations.reorderUnitsReceived = Math.Max(0, snapshot.reorderUnitsReceived);
            operations.reliability01 = Clamp01(snapshot.reliability01);
            operations.competitionPressure01 = Clamp01(snapshot.competitionPressure01);
        }

        private static void FillCapacity(BusinessCapacitySummary capacity, BusinessReportSnapshot snapshot)
        {
            capacity.storageCapacityUnits = Math.Max(0, snapshot.storageCapacityUnits);
            capacity.currentStoredUnits = Math.Max(0, snapshot.currentStoredUnits);
            capacity.processingCapacityUnitsPerWeek = Math.Max(0, snapshot.processingCapacityUnitsPerWeek);
            capacity.currentProcessingUnitsPerWeek = Math.Max(0, snapshot.currentProcessingUnitsPerWeek);
            capacity.serviceCapacityVisitsPerDay = Math.Max(0, snapshot.serviceCapacityVisitsPerDay);
            capacity.currentServiceVisitsPerDay = Math.Max(0, snapshot.currentServiceVisitsPerDay);
            capacity.bottleneckKey = snapshot.capacityBottleneckKey ?? string.Empty;
        }

        private static void CopyCapacity(BusinessCapacitySummary target, BusinessCapacitySummary source)
        {
            if (target == null || source == null)
            {
                return;
            }

            target.storageCapacityUnits = source.storageCapacityUnits;
            target.currentStoredUnits = source.currentStoredUnits;
            target.processingCapacityUnitsPerWeek = source.processingCapacityUnitsPerWeek;
            target.currentProcessingUnitsPerWeek = source.currentProcessingUnitsPerWeek;
            target.serviceCapacityVisitsPerDay = source.serviceCapacityVisitsPerDay;
            target.currentServiceVisitsPerDay = source.currentServiceVisitsPerDay;
            target.bottleneckKey = source.bottleneckKey;
        }

        private static void FillInventory(
            BusinessInventorySummary inventory,
            IReadOnlyList<BusinessCategorySnapshot> categories,
            float stockHealth01,
            float lowStockThreshold01,
            ICollection<BusinessReportGap> gaps)
        {
            inventory.stockHealth01 = Clamp01(stockHealth01);
            if (categories == null || categories.Count == 0)
            {
                return;
            }

            float categoryHealthTotal = 0f;
            int validCategoryCount = 0;
            HashSet<string> seenCategoryIds = new(StringComparer.Ordinal);
            for (int i = 0; i < categories.Count; i++)
            {
                BusinessCategorySnapshot source = categories[i];
                if (source == null)
                {
                    AddReportGap(gaps, "null_category_snapshot", $"Category snapshot at index {i} was missing and was excluded.", BusinessReportGapSeverity.Caution);
                    continue;
                }

                string categoryId = source.categoryId ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(categoryId) && !seenCategoryIds.Add(categoryId))
                {
                    AddReportGap(gaps, "duplicate_category_snapshot", $"Category '{categoryId}' appeared more than once in the business snapshot.", BusinessReportGapSeverity.Caution);
                }

                float categoryHealth = ResolveCategoryStockHealth(source, gaps);
                BusinessCategorySummary category = new()
                {
                    categoryId = categoryId,
                    displayName = string.IsNullOrWhiteSpace(source.displayName) ? categoryId : source.displayName,
                    unitsSold = Math.Max(0, source.unitsSold),
                    revenueCents = Math.Max(0, source.revenueCents),
                    currentStockUnits = Math.Max(0, source.currentStockUnits),
                    targetStockUnits = Math.Max(0, source.targetStockUnits),
                    pendingReorderUnits = Math.Max(0, source.pendingReorderUnits),
                    stockHealth01 = categoryHealth
                };
                category.isLowStock = category.stockHealth01 <= Clamp01(lowStockThreshold01);
                if (category.isLowStock)
                {
                    inventory.lowStockCategoryCount++;
                }

                categoryHealthTotal += category.stockHealth01;
                validCategoryCount++;
                inventory.categories.Add(category);
            }

            if (validCategoryCount > 0)
            {
                float derivedHealth = Clamp01(categoryHealthTotal / validCategoryCount);
                if (Invalid01(stockHealth01))
                {
                    inventory.stockHealth01 = derivedHealth;
                    AddReportGap(gaps, "stock_health_derived", "Aggregate stock health was invalid and was derived from category stock health.", BusinessReportGapSeverity.Caution);
                }
                else if (derivedHealth < inventory.stockHealth01 - 0.01f)
                {
                    inventory.stockHealth01 = derivedHealth;
                    AddReportGap(gaps, "stock_health_reconciled", "Category stock details showed weaker stock posture than the aggregate stock health, so the report uses the more conservative read.", BusinessReportGapSeverity.Caution);
                }
            }
        }

        private static float ResolveCategoryStockHealth(BusinessCategorySnapshot source, ICollection<BusinessReportGap> gaps)
        {
            float supplied = Clamp01(source.stockHealth01);
            if (Invalid01(source.stockHealth01))
            {
                AddReportGap(gaps, "invalid_category_stock_health", $"Category '{source.categoryId ?? string.Empty}' had invalid stock health and was clamped.", BusinessReportGapSeverity.Caution);
            }

            if (source.targetStockUnits > 0)
            {
                float ratio = Clamp01((float)Math.Max(0, source.currentStockUnits) / source.targetStockUnits);
                if (ratio < supplied - 0.01f)
                {
                    AddReportGap(gaps, "category_stock_ratio_reconciled", $"Category '{source.categoryId ?? string.Empty}' current stock was weaker than its supplied health value.", BusinessReportGapSeverity.Caution);
                    return ratio;
                }
            }

            return supplied;
        }

        private static void FillStaffing(BusinessStaffingSummary staffing, IReadOnlyList<BusinessWorkerSnapshot> workers, int fallbackWeeklyPayrollCents, ICollection<BusinessReportGap> gaps)
        {
            if (staffing == null)
            {
                return;
            }

            staffing.filledWorkerSlots = 0;
            staffing.openWorkerSlots = 0;
            staffing.weeklyPayrollCents = 0;
            staffing.filledWorkerPayrollCents = 0;
            staffing.openSlotPlannedPayrollCents = 0;
            staffing.payrollSource = BusinessStaffingPayrollSource.Unknown;
            staffing.usesFallbackPayroll = false;
            staffing.payrollReadSummary = string.Empty;
            staffing.workers.Clear();

            int fallbackPayroll = Math.Max(0, fallbackWeeklyPayrollCents);
            if (workers == null || workers.Count == 0)
            {
                if (fallbackPayroll > 0)
                {
                    staffing.weeklyPayrollCents = fallbackPayroll;
                    staffing.payrollSource = BusinessStaffingPayrollSource.RuntimeSnapshotFallback;
                    staffing.usesFallbackPayroll = true;
                    staffing.payrollReadSummary = "Payroll uses runtime snapshot fallback; no worker rows were supplied.";
                    AddReportGap(gaps, "staffing_payroll_fallback", "Staffing payroll used runtime payroll because worker rows were not supplied.", BusinessReportGapSeverity.Caution);
                }
                else
                {
                    staffing.payrollSource = BusinessStaffingPayrollSource.NoPayrollObserved;
                    staffing.payrollReadSummary = "No staffing payroll was observed.";
                }

                return;
            }

            int filledPayroll = 0;
            int openPlannedPayroll = 0;
            bool sawFilledWage = false;
            HashSet<string> seenSlots = new(StringComparer.Ordinal);
            for (int i = 0; i < workers.Count; i++)
            {
                BusinessWorkerSnapshot source = workers[i];
                if (source == null)
                {
                    AddReportGap(gaps, "null_worker_snapshot", $"Worker snapshot at index {i} was missing and was excluded.", BusinessReportGapSeverity.Caution);
                    continue;
                }

                string slotId = source.slotId ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(slotId) && !seenSlots.Add(slotId))
                {
                    AddReportGap(gaps, "duplicate_worker_slot", $"Worker slot '{slotId}' appeared more than once in the business snapshot.", BusinessReportGapSeverity.Caution);
                }

                int wage = Math.Max(0, source.weeklyWageCents);
                if (source.weeklyWageCents < 0)
                {
                    AddReportGap(gaps, "negative_worker_wage", $"Worker slot '{slotId}' had negative wage data and was clamped.", BusinessReportGapSeverity.Caution);
                }

                BusinessWorkerSummary worker = new()
                {
                    slotId = slotId,
                    slotDisplayName = string.IsNullOrWhiteSpace(source.slotDisplayName) ? slotId : source.slotDisplayName,
                    workerId = source.workerId ?? string.Empty,
                    workerDisplayName = source.workerDisplayName ?? string.Empty,
                    weeklyWageCents = wage,
                    isFilled = source.isFilled
                };

                if (worker.isFilled)
                {
                    staffing.filledWorkerSlots++;
                    filledPayroll = AddCents(filledPayroll, wage);
                    sawFilledWage |= wage > 0;
                    if (string.IsNullOrWhiteSpace(worker.workerId) && string.IsNullOrWhiteSpace(worker.workerDisplayName))
                    {
                        AddReportGap(gaps, "filled_worker_missing_identity", $"Filled worker slot '{slotId}' had no worker identity.", BusinessReportGapSeverity.Caution);
                    }
                }
                else
                {
                    staffing.openWorkerSlots++;
                    openPlannedPayroll = AddCents(openPlannedPayroll, wage);
                }

                staffing.workers.Add(worker);
            }

            staffing.filledWorkerPayrollCents = filledPayroll;
            staffing.openSlotPlannedPayrollCents = openPlannedPayroll;
            if (filledPayroll > 0 || sawFilledWage)
            {
                staffing.weeklyPayrollCents = filledPayroll;
                staffing.payrollSource = BusinessStaffingPayrollSource.FilledWorkerRows;
                staffing.payrollReadSummary = "Payroll uses filled worker wage rows.";
                if (fallbackPayroll > 0 && Math.Abs(fallbackPayroll - filledPayroll) > 0)
                {
                    AddReportGap(gaps, "staffing_payroll_reconciled", "Filled worker payroll differed from runtime payroll expense; staffing shows worker-row payroll while finance keeps the runtime payroll expense.", BusinessReportGapSeverity.Caution);
                }
            }
            else if (fallbackPayroll > 0)
            {
                staffing.weeklyPayrollCents = fallbackPayroll;
                staffing.payrollSource = BusinessStaffingPayrollSource.RuntimeSnapshotFallback;
                staffing.usesFallbackPayroll = true;
                staffing.payrollReadSummary = "Payroll uses runtime snapshot fallback.";
                AddReportGap(gaps, "staffing_payroll_fallback", "Staffing payroll used runtime payroll because filled worker wages were unavailable.", BusinessReportGapSeverity.Caution);
            }
            else
            {
                staffing.payrollSource = BusinessStaffingPayrollSource.NoPayrollObserved;
                staffing.payrollReadSummary = staffing.filledWorkerSlots > 0
                    ? "Filled workers were present, but no payroll amount was observed."
                    : "No staffing payroll was observed.";
                if (staffing.filledWorkerSlots > 0)
                {
                    AddReportGap(gaps, "staffing_payroll_missing", "Filled worker slots were present, but no worker wages or runtime payroll were available.", BusinessReportGapSeverity.Caution);
                }
            }

            if (openPlannedPayroll > 0)
            {
                AddReportGap(gaps, "open_slot_planned_payroll", "Open-slot planned wages are tracked separately from filled-worker payroll.", BusinessReportGapSeverity.Informational);
            }
        }

        private static void CopyStaffing(BusinessStaffingSummary target, BusinessStaffingSummary source)
        {
            if (target == null || source == null)
            {
                return;
            }

            target.filledWorkerSlots = source.filledWorkerSlots;
            target.openWorkerSlots = source.openWorkerSlots;
            target.weeklyPayrollCents = source.weeklyPayrollCents;
            target.filledWorkerPayrollCents = source.filledWorkerPayrollCents;
            target.openSlotPlannedPayrollCents = source.openSlotPlannedPayrollCents;
            target.payrollSource = source.payrollSource;
            target.usesFallbackPayroll = source.usesFallbackPayroll;
            target.payrollReadSummary = source.payrollReadSummary;
            target.workers.Clear();
            if (source.workers == null)
            {
                return;
            }

            for (int i = 0; i < source.workers.Count; i++)
            {
                BusinessWorkerSummary worker = source.workers[i];
                if (worker == null)
                {
                    continue;
                }

                target.workers.Add(new BusinessWorkerSummary
                {
                    slotId = worker.slotId,
                    slotDisplayName = worker.slotDisplayName,
                    workerId = worker.workerId,
                    workerDisplayName = worker.workerDisplayName,
                    weeklyWageCents = worker.weeklyWageCents,
                    isFilled = worker.isFilled
                });
            }
        }

        private static void AddSnapshotLedgerEntries(ICollection<LedgerEntry> entries, BusinessReportSnapshot snapshot, string cadence)
        {
            if (snapshot == null || ContainsExplicitRuntimeLedgerEntries(entries))
            {
                return;
            }

            // Generated rows are fallback report summaries. Runtime ledger rows remain authoritative when supplied.
            AddLedgerEntry(entries, snapshot, cadence, "sales", LedgerEntryKind.Revenue, LedgerEntryCategory.Sales, Math.Max(0, snapshot.revenueCents), Math.Max(0, snapshot.unitsSold), "units", $"{cadence} sales summary");
            AddLedgerEntry(entries, snapshot, cadence, "cogs", LedgerEntryKind.Expense, LedgerEntryCategory.Inventory, -Math.Max(0, snapshot.costOfGoodsSoldCents), 0, string.Empty, $"{cadence} cost of goods summary");
            AddLedgerEntry(entries, snapshot, cadence, "payroll", LedgerEntryKind.Expense, LedgerEntryCategory.Payroll, -Math.Max(0, snapshot.payrollExpenseCents), 0, string.Empty, $"{cadence} payroll summary");
            AddLedgerEntry(entries, snapshot, cadence, "reorder", LedgerEntryKind.Expense, LedgerEntryCategory.Reorder, -Math.Max(0, snapshot.reorderExpenseCents), Math.Max(0, snapshot.reorderUnitsReceived), "units", $"{cadence} reorder spend summary");
            AddLedgerEntry(entries, snapshot, cadence, "local_supply", LedgerEntryKind.Expense, LedgerEntryCategory.Reorder, -Math.Max(0, snapshot.localSupplyExpenseCents), 0, string.Empty, $"{cadence} local supply spend summary");
            AddLedgerEntry(entries, snapshot, cadence, "owner_draw", LedgerEntryKind.Expense, LedgerEntryCategory.Other, -Math.Max(0, snapshot.ownerDistributionCents), 0, string.Empty, $"{cadence} owner draw summary");
            AddLedgerEntry(entries, snapshot, cadence, "cash_transfer_in", LedgerEntryKind.Revenue, LedgerEntryCategory.Other, Math.Max(0, snapshot.cashTransferInCents), 0, string.Empty, $"{cadence} owner cash transfer in summary");
            AddLedgerEntry(entries, snapshot, cadence, "cash_transfer_out", LedgerEntryKind.Expense, LedgerEntryCategory.Other, -Math.Max(0, snapshot.cashTransferOutCents), 0, string.Empty, $"{cadence} owner cash transfer out summary");
        }

        private static void AddLedgerEntry(
            ICollection<LedgerEntry> entries,
            BusinessReportSnapshot snapshot,
            string cadence,
            string idSuffix,
            LedgerEntryKind kind,
            LedgerEntryCategory category,
            int amountCents,
            int quantity,
            string unit,
            string label)
        {
            if (entries == null || snapshot == null || amountCents == 0 && quantity == 0)
            {
                return;
            }

            LedgerEntry entry = new(
                BuildReportSummaryEntryId(snapshot, cadence, idSuffix),
                kind,
                category,
                ResolveReportSummarySource(cadence),
                snapshot.generatedOnDate,
                snapshot.businessId,
                label,
                amountCents,
                quantity,
                unit,
                snapshot.period.GetDisplayLabel(),
                true,
                BuildReportSummaryKey(snapshot, cadence, idSuffix));

            if (!ContainsLedgerEntry(entries, entry))
            {
                entries.Add(entry);
            }
        }

        private static void ValidateSnapshot(ICollection<BusinessReportGap> gaps, BusinessReportSnapshot snapshot, ReportPeriodKind expectedKind)
        {
            if (snapshot == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(snapshot.businessId))
            {
                AddReportGap(gaps, "missing_business_id", "Business snapshot was missing a business ID.", BusinessReportGapSeverity.Blocking);
            }

            if (snapshot.period.Kind != expectedKind)
            {
                AddReportGap(gaps, "period_kind_mismatch", $"Business snapshot period was {snapshot.period.Kind}, but the report expected {expectedKind}.", BusinessReportGapSeverity.Blocking);
            }

            FlagNegative(gaps, snapshot.revenueCents, "negative_revenue", "Revenue was negative and was clamped for reporting.");
            FlagNegative(gaps, snapshot.costOfGoodsSoldCents, "negative_cogs", "Cost of goods was negative and was clamped for reporting.");
            FlagNegative(gaps, snapshot.payrollExpenseCents, "negative_payroll", "Payroll expense was negative and was clamped for reporting.");
            FlagNegative(gaps, snapshot.reorderExpenseCents, "negative_reorder", "Reorder expense was negative and was clamped for reporting.");
            FlagNegative(gaps, snapshot.localSupplyExpenseCents, "negative_local_supply", "Local supply expense was negative and was clamped for reporting.");
            FlagNegative(gaps, snapshot.unitsSold, "negative_units_sold", "Units sold was negative and was clamped for reporting.");
            FlagNegative(gaps, snapshot.customerVisits, "negative_customer_visits", "Customer visits was negative and was clamped for reporting.");
            if (Invalid01(snapshot.stockHealth01))
            {
                AddReportGap(gaps, "invalid_stock_health", "Aggregate stock health was outside the 0-1 range and was clamped or derived.", BusinessReportGapSeverity.Caution);
            }

            if (Invalid01(snapshot.reliability01))
            {
                AddReportGap(gaps, "invalid_reliability", "Reliability was outside the 0-1 range and was clamped.", BusinessReportGapSeverity.Caution);
            }

            if (Invalid01(snapshot.competitionPressure01))
            {
                AddReportGap(gaps, "invalid_competition_pressure", "Competition pressure was outside the 0-1 range and was clamped.", BusinessReportGapSeverity.Caution);
            }

            if (snapshot.currentStoredUnits > Math.Max(0, snapshot.storageCapacityUnits) && snapshot.storageCapacityUnits > 0)
            {
                AddReportGap(gaps, "storage_over_capacity", "Stored inventory exceeded reported storage capacity.", BusinessReportGapSeverity.Caution);
            }

            if (snapshot.currentProcessingUnitsPerWeek > Math.Max(0, snapshot.processingCapacityUnitsPerWeek) && snapshot.processingCapacityUnitsPerWeek > 0)
            {
                AddReportGap(gaps, "processing_over_capacity", "Current processing load exceeded reported processing capacity.", BusinessReportGapSeverity.Caution);
            }

            if (snapshot.currentServiceVisitsPerDay > Math.Max(0, snapshot.serviceCapacityVisitsPerDay) && snapshot.serviceCapacityVisitsPerDay > 0)
            {
                AddReportGap(gaps, "service_over_capacity", "Current service visits exceeded reported service capacity.", BusinessReportGapSeverity.Caution);
            }

            ValidateLedgerRows(gaps, snapshot);
        }

        private static void ValidateLedgerRows(ICollection<BusinessReportGap> gaps, BusinessReportSnapshot snapshot)
        {
            if (snapshot.ledgerEntries == null)
            {
                return;
            }

            for (int i = 0; i < snapshot.ledgerEntries.Count; i++)
            {
                LedgerEntry entry = snapshot.ledgerEntries[i];
                if (entry.IsGeneratedReportSummary)
                {
                    AddReportGap(gaps, "generated_ledger_in_snapshot", "Runtime business snapshot contained generated report summary ledger rows.", BusinessReportGapSeverity.Caution);
                }

                if (!string.IsNullOrWhiteSpace(entry.BusinessId)
                    && !string.IsNullOrWhiteSpace(snapshot.businessId)
                    && !string.Equals(entry.BusinessId, snapshot.businessId, StringComparison.Ordinal))
                {
                    AddReportGap(gaps, "ledger_business_mismatch", $"Ledger row '{entry.EntryId}' belongs to another business and was copied only as supplied source data.", BusinessReportGapSeverity.Caution);
                }

                if (entry.Quantity < 0)
                {
                    AddReportGap(gaps, "negative_ledger_quantity", $"Ledger row '{entry.EntryId}' had negative quantity and was normalized when copied.", BusinessReportGapSeverity.Caution);
                }
            }
        }

        private static void FillGaps(ICollection<BusinessReportGap> gaps, IReadOnlyList<string> gapNotes)
        {
            if (gaps == null || gapNotes == null)
            {
                return;
            }

            for (int i = 0; i < gapNotes.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(gapNotes[i]))
                {
                    AddReportGap(gaps, "snapshot_gap", gapNotes[i], BusinessReportGapSeverity.Blocking);
                }
            }
        }

        internal static void CopyGaps(ICollection<BusinessReportGap> target, IReadOnlyList<BusinessReportGap> source)
        {
            if (target == null || source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                BusinessReportGap gap = source[i];
                if (gap != null)
                {
                    AddReportGap(target, gap.code, gap.message, gap.severity);
                }
            }
        }

        internal static void AddDefaultGaps(ICollection<BusinessReportGap> gaps)
        {
            if (gaps == null)
            {
                return;
            }

            AddReportGap(gaps, "cost_detail_partial", "Cost bridge fields are shown when runtime snapshots provide them; taxes, rent, and depreciation remain outside the bridge.", BusinessReportGapSeverity.Caution);
            AddReportGap(gaps, "supplier_history_missing", "Supplier identity, terms, and invoice history are not available yet.", BusinessReportGapSeverity.Informational);
            AddReportGap(gaps, "non_operating_expenses_missing", "Taxes, debt, rent, maintenance, spoilage, and depreciation are not available yet.", BusinessReportGapSeverity.Caution);
        }

        internal static void AddReportGap(ICollection<BusinessReportGap> gaps, string code, string message, BusinessReportGapSeverity severity = BusinessReportGapSeverity.Blocking)
        {
            if (gaps == null || string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            string safeCode = code ?? string.Empty;
            string safeMessage = message ?? string.Empty;
            foreach (BusinessReportGap existing in gaps)
            {
                if (existing != null
                    && string.Equals(existing.code ?? string.Empty, safeCode, StringComparison.Ordinal)
                    && string.Equals(existing.message ?? string.Empty, safeMessage, StringComparison.Ordinal)
                    && existing.severity == severity)
                {
                    return;
                }
            }

            gaps.Add(new BusinessReportGap(safeCode, safeMessage, severity));
        }

        internal static int CountReportNotes(IEnumerable<BusinessReportGap> gaps)
        {
            if (gaps == null)
            {
                return 0;
            }

            int count = 0;
            foreach (BusinessReportGap gap in gaps)
            {
                if (gap != null)
                {
                    count = AddNonNegativeCount(count, 1);
                }
            }

            return count;
        }

        internal static int CountBlockingGaps(IEnumerable<BusinessReportGap> gaps)
        {
            if (gaps == null)
            {
                return 0;
            }

            int count = 0;
            foreach (BusinessReportGap gap in gaps)
            {
                if (gap != null && gap.IsBlocking)
                {
                    count = AddNonNegativeCount(count, 1);
                }
            }

            return count;
        }

        internal static void CopyLedgerEntries(ICollection<LedgerEntry> target, IReadOnlyList<LedgerEntry> source)
        {
            if (target == null || source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                LedgerEntry entry = NormalizeLedgerEntry(source[i]);
                if (!ContainsLedgerEntry(target, entry))
                {
                    target.Add(entry);
                }
            }
        }

        private static bool ContainsExplicitRuntimeLedgerEntries(ICollection<LedgerEntry> entries)
        {
            if (entries == null)
            {
                return false;
            }

            foreach (LedgerEntry entry in entries)
            {
                if (!entry.IsGeneratedReportSummary)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsLedgerEntry(IEnumerable<LedgerEntry> entries, LedgerEntry candidate)
        {
            if (entries == null)
            {
                return false;
            }

            foreach (LedgerEntry existing in entries)
            {
                if (AreSameLedgerEntry(existing, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AreSameLedgerEntry(LedgerEntry left, LedgerEntry right)
        {
            if (!string.IsNullOrWhiteSpace(left.ReportSummaryKey) || !string.IsNullOrWhiteSpace(right.ReportSummaryKey))
            {
                return string.Equals(left.ReportSummaryKey ?? string.Empty, right.ReportSummaryKey ?? string.Empty, StringComparison.Ordinal);
            }

            if (!string.IsNullOrWhiteSpace(left.EntryId) || !string.IsNullOrWhiteSpace(right.EntryId))
            {
                return string.Equals(left.EntryId ?? string.Empty, right.EntryId ?? string.Empty, StringComparison.Ordinal);
            }

            return left.Kind == right.Kind
                && left.Category == right.Category
                && left.Source == right.Source
                && string.Equals(left.BusinessId ?? string.Empty, right.BusinessId ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(left.Label ?? string.Empty, right.Label ?? string.Empty, StringComparison.Ordinal)
                && left.AmountCents == right.AmountCents
                && left.Quantity == right.Quantity
                && string.Equals(left.Unit ?? string.Empty, right.Unit ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(left.SourceId ?? string.Empty, right.SourceId ?? string.Empty, StringComparison.Ordinal);
        }

        private static LedgerEntry NormalizeLedgerEntry(LedgerEntry entry)
        {
            return new LedgerEntry(
                entry.EntryId,
                entry.Kind,
                entry.Category,
                entry.Source,
                entry.Date,
                entry.BusinessId,
                entry.Label,
                entry.AmountCents,
                Math.Max(0, entry.Quantity),
                entry.Unit,
                entry.SourceId,
                entry.IsGeneratedReportSummary,
                entry.ReportSummaryKey);
        }

        private static LedgerEntrySource ResolveReportSummarySource(string cadence)
        {
            return string.Equals(cadence, "monthly", StringComparison.OrdinalIgnoreCase)
                ? LedgerEntrySource.MonthlySummary
                : LedgerEntrySource.WeeklySummary;
        }

        private static string BuildReportSummaryEntryId(BusinessReportSnapshot snapshot, string cadence, string idSuffix)
        {
            return $"report_{NormalizeLedgerToken(snapshot.businessId)}_{NormalizeLedgerToken(snapshot.period.GetStableKey())}_{NormalizeLedgerToken(cadence)}_{NormalizeLedgerToken(idSuffix)}";
        }

        private static string BuildReportSummaryKey(BusinessReportSnapshot snapshot, string cadence, string idSuffix)
        {
            return $"{NormalizeLedgerToken(snapshot.businessId)}|{NormalizeLedgerToken(snapshot.period.GetStableKey())}|{NormalizeLedgerToken(cadence)}|{NormalizeLedgerToken(idSuffix)}";
        }

        private static string NormalizeLedgerToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "none";
            }

            string trimmed = value.Trim().ToLowerInvariant();
            char[] chars = new char[trimmed.Length];
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                chars[i] = char.IsLetterOrDigit(c) ? c : '_';
            }

            return new string(chars).Trim('_');
        }

        private static void AccumulateInventoryCategories(Dictionary<string, InventoryAccumulator> accumulators, BusinessInventorySummary inventory)
        {
            if (accumulators == null || inventory?.categories == null)
            {
                return;
            }

            for (int i = 0; i < inventory.categories.Count; i++)
            {
                BusinessCategorySummary category = inventory.categories[i];
                if (category == null)
                {
                    continue;
                }

                string key = string.IsNullOrWhiteSpace(category.categoryId) ? $"category_{i}" : category.categoryId;
                if (!accumulators.TryGetValue(key, out InventoryAccumulator accumulator))
                {
                    accumulator = new InventoryAccumulator
                    {
                        CategoryId = category.categoryId,
                        DisplayName = category.displayName
                    };
                    accumulators[key] = accumulator;
                }

                accumulator.UnitsSold = AddNonNegativeCount(accumulator.UnitsSold, category.unitsSold);
                accumulator.RevenueCents = AddCents(accumulator.RevenueCents, category.revenueCents);
                accumulator.CurrentStockUnits = category.currentStockUnits;
                accumulator.TargetStockUnits = category.targetStockUnits;
                accumulator.PendingReorderUnits = category.pendingReorderUnits;
                accumulator.StockHealthTotal += Clamp01(category.stockHealth01);
                accumulator.ObservationCount = AddNonNegativeCount(accumulator.ObservationCount, 1);
                accumulator.WasLowStock |= category.isLowStock;
            }
        }

        private static void ApplyInventoryAggregation(BusinessInventorySummary inventory, Dictionary<string, InventoryAccumulator> accumulators, float weeklyAggregateStockHealthTotal, int weekCount)
        {
            if (inventory == null)
            {
                return;
            }

            inventory.categories.Clear();
            inventory.lowStockCategoryCount = 0;
            float categoryHealthTotal = 0f;
            int categoryCount = 0;
            foreach (InventoryAccumulator accumulator in accumulators.Values)
            {
                float categoryHealth = accumulator.ObservationCount > 0
                    ? Clamp01(accumulator.StockHealthTotal / accumulator.ObservationCount)
                    : 1f;
                BusinessCategorySummary category = new()
                {
                    categoryId = accumulator.CategoryId ?? string.Empty,
                    displayName = string.IsNullOrWhiteSpace(accumulator.DisplayName) ? accumulator.CategoryId ?? string.Empty : accumulator.DisplayName,
                    unitsSold = accumulator.UnitsSold,
                    revenueCents = accumulator.RevenueCents,
                    currentStockUnits = accumulator.CurrentStockUnits,
                    targetStockUnits = accumulator.TargetStockUnits,
                    pendingReorderUnits = accumulator.PendingReorderUnits,
                    stockHealth01 = categoryHealth,
                    isLowStock = accumulator.WasLowStock || categoryHealth <= DefaultLowStockThreshold01
                };

                if (category.isLowStock)
                {
                    inventory.lowStockCategoryCount++;
                }

                categoryHealthTotal += category.stockHealth01;
                categoryCount++;
                inventory.categories.Add(category);
            }

            float weeklyAverage = weekCount > 0 ? Clamp01(weeklyAggregateStockHealthTotal / weekCount) : 1f;
            float categoryAverage = categoryCount > 0 ? Clamp01(categoryHealthTotal / categoryCount) : weeklyAverage;
            inventory.stockHealth01 = Mathf.Min(weeklyAverage, categoryAverage);
        }

        internal static int SumKnownExpenses(BusinessFinancialRollup financial)
        {
            return AddCents(
                AddCents(
                    AddCents(
                        AddCents(Math.Max(0, financial.costOfGoodsSoldCents), Math.Max(0, financial.payrollExpenseCents)),
                        Math.Max(0, financial.reorderExpenseCents)),
                    Math.Max(0, financial.localSupplyExpenseCents)),
                AddCents(
                    AddCents(Math.Max(0, financial.ownerDistributionCents), Math.Max(0, financial.cashTransferOutCents)),
                    Math.Max(0, financial.otherExpenseCents)));
        }

        internal static int EstimateCashRunwayWeeks(BusinessFinancialRollup financial)
        {
            int weeklyBurn = Math.Max(0, financial.knownExpenseCents - financial.revenueCents - financial.cashTransferInCents);
            if (weeklyBurn <= 0)
            {
                return 99;
            }

            int cashAboveReserve = Math.Max(0, financial.closingCashCents - financial.survivalReserveCents);
            return Math.Max(0, cashAboveReserve / weeklyBurn);
        }

        internal static string BuildCashBridgeSummary(BusinessFinancialRollup financial)
        {
            if (financial == null)
            {
                return string.Empty;
            }

            return $"Cash changed because: sales {FormatMoney(financial.revenueCents)}, COGS {FormatMoney(financial.costOfGoodsSoldCents)}, payroll {FormatMoney(financial.payrollExpenseCents)}, stock buys {FormatMoney(AddCents(financial.reorderExpenseCents, financial.localSupplyExpenseCents))}, owner draw {FormatMoney(financial.ownerDistributionCents)}, transfers {FormatSignedMoney(SubtractCents(financial.cashTransferInCents, financial.cashTransferOutCents))}.";
        }

        internal static string FormatMoney(int cents)
        {
            return $"${Math.Max(0, cents) / 100f:0.00}";
        }

        internal static string FormatSignedMoney(int cents)
        {
            int amount = Math.Abs(cents);
            string sign = cents > 0 ? "+" : cents < 0 ? "-" : string.Empty;
            return $"{sign}${amount / 100f:0.00}";
        }

        internal static int DivideCents(int cents, int divisor)
        {
            return divisor <= 0 ? 0 : Mathf.RoundToInt((float)cents / divisor);
        }

        internal static float Clamp01(float value)
        {
            return Mathf.Clamp01(float.IsNaN(value) || float.IsInfinity(value) ? 0f : value);
        }

        private static bool Invalid01(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > 1f;
        }

        private static void FlagNegative(ICollection<BusinessReportGap> gaps, int value, string code, string message)
        {
            if (value < 0)
            {
                AddReportGap(gaps, code, message, BusinessReportGapSeverity.Caution);
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
            long total = (long)Math.Max(0, current) + Math.Max(0, delta);
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        private sealed class InventoryAccumulator
        {
            public string CategoryId;
            public string DisplayName;
            public int UnitsSold;
            public int RevenueCents;
            public int CurrentStockUnits;
            public int TargetStockUnits;
            public int PendingReorderUnits;
            public float StockHealthTotal;
            public int ObservationCount;
            public bool WasLowStock;
        }
    }
}
