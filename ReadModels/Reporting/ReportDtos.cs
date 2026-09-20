using System;
using System.Collections.Generic;
using LandLedgers.Time;

namespace LandLedgers.Reporting
{
    [Serializable]
    public sealed class BusinessReportHeader
    {
        public string businessId;
        public string businessDisplayName;
        public string businessTypeKey;
        public string buildingId;
        public string focusId;
        public string focusDisplayName;
        public string focusSummary;
        public ReportPeriod period;
        public SimulationDate generatedOnDate;
        public bool isCompletePeriod;
    }

    [Serializable]
    public sealed class BusinessFinancialRollup
    {
        public int openingCashCents;
        public int closingCashCents;
        public int revenueCents;
        public int costOfGoodsSoldCents;
        public int payrollExpenseCents;
        public int reorderExpenseCents;
        public int localSupplyExpenseCents;
        public int ownerDistributionCents;
        public int cashTransferInCents;
        public int cashTransferOutCents;
        public int otherExpenseCents;
        public int knownExpenseCents;
        public int netCashChangeCents;
        public int knownProfitProxyCents;
        public int unknownExpenseCents;
        public int survivalReserveCents;
        public int cashRunwayWeeks;
        public string primaryBlockedReason;
        public string cashBridgeSummary;
    }

    [Serializable]
    public sealed class BusinessOperationsSummary
    {
        public int unitsSold;
        public int customerVisits;
        public int averageRevenuePerVisitCents;
        public int averageRevenuePerUnitCents;
        public bool reorderTriggered;
        public int reorderUnitsQueued;
        public int reorderUnitsReceived;
        public float reliability01;
        public float competitionPressure01;
    }

    [Serializable]
    public sealed class BusinessCapacitySummary
    {
        public int storageCapacityUnits;
        public int currentStoredUnits;
        public int processingCapacityUnitsPerWeek;
        public int currentProcessingUnitsPerWeek;
        public int serviceCapacityVisitsPerDay;
        public int currentServiceVisitsPerDay;
        public string bottleneckKey;
    }

    [Serializable]
    public sealed class BusinessCategorySummary
    {
        public string categoryId;
        public string displayName;
        public int unitsSold;
        public int revenueCents;
        public int currentStockUnits;
        public int targetStockUnits;
        public int pendingReorderUnits;
        public float stockHealth01;
        public bool isLowStock;
    }

    [Serializable]
    public sealed class BusinessInventorySummary
    {
        public float stockHealth01 = 1f;
        public int lowStockCategoryCount;
        public List<BusinessCategorySummary> categories = new();
    }

    [Serializable]
    public sealed class BusinessWorkerSummary
    {
        public string slotId;
        public string slotDisplayName;
        public string workerId;
        public string workerDisplayName;
        public int weeklyWageCents;
        public bool isFilled;
    }

    public enum BusinessStaffingPayrollSource
    {
        Unknown = 0,
        FilledWorkerRows = 1,
        RuntimeSnapshotFallback = 2,
        NoPayrollObserved = 3
    }

    [Serializable]
    public sealed class BusinessStaffingSummary
    {
        public int filledWorkerSlots;
        public int openWorkerSlots;
        public int weeklyPayrollCents;
        public int filledWorkerPayrollCents;
        public int openSlotPlannedPayrollCents;
        public BusinessStaffingPayrollSource payrollSource;
        public bool usesFallbackPayroll;
        public string payrollReadSummary;
        public List<BusinessWorkerSummary> workers = new();
    }

    public enum BusinessReportGapSeverity
    {
        Informational = 0,
        Caution = 1,
        Blocking = 2
    }

    [Serializable]
    public sealed class BusinessReportGap
    {
        public BusinessReportGap(string code, string message)
            : this(code, message, BusinessReportGapSeverity.Blocking)
        {
        }

        public BusinessReportGap(string code, string message, BusinessReportGapSeverity severity)
        {
            this.code = code ?? string.Empty;
            this.message = message ?? string.Empty;
            this.severity = severity;
        }

        public string code;
        public string message;
        public BusinessReportGapSeverity severity = BusinessReportGapSeverity.Blocking;
        public bool IsBlocking => severity == BusinessReportGapSeverity.Blocking;
    }

    [Serializable]
    public sealed class BusinessWeeklySummary
    {
        public BusinessReportHeader header = new();
        public BusinessFinancialRollup financial = new();
        public BusinessOperationsSummary operations = new();
        public BusinessCapacitySummary capacity = new();
        public BusinessInventorySummary inventory = new();
        public BusinessStaffingSummary staffing = new();
        public List<LedgerEntry> ledgerEntries = new();
        public List<BusinessReportGap> gaps = new();
    }

    [Serializable]
    public sealed class BusinessMonthlySummary
    {
        public BusinessReportHeader header = new();
        public BusinessFinancialRollup financial = new();
        public BusinessOperationsSummary operations = new();
        public BusinessCapacitySummary capacity = new();
        public BusinessInventorySummary inventory = new();
        public BusinessStaffingSummary staffing = new();
        public int includedWeekCount;
        public int averageWeeklyRevenueCents;
        public int averageWeeklyPayrollCents;
        public int averageWeeklyReorderExpenseCents;
        public int bestRevenueWeek;
        public int bestRevenueWeekCents;
        public int weakestRevenueWeek;
        public int weakestRevenueWeekCents;
        public List<LedgerEntry> ledgerEntries = new();
        public List<BusinessReportGap> gaps = new();
    }

    [Serializable]
    public sealed class PortfolioFinancialRollup
    {
        public int totalOpeningCashCents;
        public int totalClosingCashCents;
        public int totalRevenueCents;
        public int totalCostOfGoodsSoldCents;
        public int totalPayrollExpenseCents;
        public int totalReorderExpenseCents;
        public int totalLocalSupplyExpenseCents;
        public int totalOwnerDistributionCents;
        public int totalCashTransferInCents;
        public int totalCashTransferOutCents;
        public int totalKnownExpenseCents;
        public int totalNetCashChangeCents;
        public int totalKnownProfitProxyCents;
    }

    [Serializable]
    public sealed class PortfolioBusinessSummaryLine
    {
        public string businessId;
        public string businessDisplayName;
        public string businessTypeKey;
        public string buildingId;
        public int revenueCents;
        public int costOfGoodsSoldCents;
        public int knownExpenseCents;
        public int netCashChangeCents;
        public int closingCashCents;
        public int survivalReserveCents;
        public int cashRunwayWeeks;
        public string primaryBlockedReason;
        public string cashBridgeSummary;
        public string focusDisplayName;
        public string capacityBottleneckKey;
        public float stockHealth01;
        public int lowStockCategoryCount;
        public int filledWorkerSlots;
        public int openWorkerSlots;
        public int weeklyPayrollCents;
        public int filledWorkerPayrollCents;
        public int openSlotPlannedPayrollCents;
        public BusinessStaffingPayrollSource payrollSource;
        public bool usesFallbackPayroll;
        public string payrollReadSummary;
        public bool reorderTriggered;
        public bool hasReportGaps;
        public int reportNoteCount;
        public int blockingReportGapCount;
    }

    [Serializable]
    public sealed class PortfolioWeeklySummary
    {
        public ReportPeriod period;
        public SimulationDate generatedOnDate;
        public int businessCount;
        public int ownedLandCount;
        public int ownedBusinessCount;
        public PortfolioFinancialRollup financial = new();
        public float averageStockHealth01 = 1f;
        public int lowStockBusinessCount;
        public int totalFilledWorkerSlots;
        public int totalOpenWorkerSlots;
        public int totalOpenSlotPlannedPayrollCents;
        public int fallbackPayrollBusinessCount;
        public List<PortfolioBusinessSummaryLine> businessLines = new();
        public List<LedgerEntry> ledgerEntries = new();
        public List<BusinessReportGap> gaps = new();
    }

    [Serializable]
    public sealed class PortfolioBalanceDriftSummary
    {
        public bool hasNetWorthSnapshots;
        public int openingNetWorthCents;
        public int closingNetWorthCents;
        public int netWorthDriftCents;
        public bool hasDebtSnapshots;
        public int openingDebtCents;
        public int closingDebtCents;
        public int debtDriftCents;
        public bool hasBusinessCashSnapshots;
        public int openingBusinessCashCents;
        public int closingBusinessCashCents;
        public int businessCashDriftCents;
        public string reviewWindowLabel;

        public bool HasWealthAndDebtRead => hasNetWorthSnapshots && hasDebtSnapshots;
        public bool HasFullBalanceRead => HasWealthAndDebtRead && hasBusinessCashSnapshots;
        public bool IsActivityOnly => !HasWealthAndDebtRead && !hasBusinessCashSnapshots;
    }

    [Serializable]
    public sealed class PortfolioMonthlySummary
    {
        public ReportPeriod period;
        public SimulationDate generatedOnDate;
        public int businessCount;
        public int ownedLandCount;
        public int ownedBusinessCount;
        public int includedWeekCount;
        public PortfolioFinancialRollup financial = new();
        public float averageStockHealth01 = 1f;
        public int lowStockBusinessCount;
        public int totalFilledWorkerSlots;
        public int totalOpenWorkerSlots;
        public int totalOpenSlotPlannedPayrollCents;
        public int fallbackPayrollBusinessCount;
        public PortfolioBalanceDriftSummary balanceDrift = new();
        public List<PortfolioBusinessSummaryLine> businessLines = new();
        public List<LedgerEntry> ledgerEntries = new();
        public List<BusinessReportGap> gaps = new();
    }
}
