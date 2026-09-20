using System;
using System.Collections.Generic;
using LandLedgers.Time;

namespace LandLedgers.Reporting
{
    [Serializable]
    public sealed class BusinessCategorySnapshot
    {
        public string categoryId;
        public string displayName;
        public int unitsSold;
        public int revenueCents;
        public int currentStockUnits;
        public int targetStockUnits;
        public int pendingReorderUnits;
        public float stockHealth01 = 1f;
    }

    [Serializable]
    public sealed class BusinessWorkerSnapshot
    {
        public string slotId;
        public string slotDisplayName;
        public string workerId;
        public string workerDisplayName;
        public int weeklyWageCents;
        public bool isFilled;
    }

    [Serializable]
    public sealed class BusinessReportSnapshot
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
        public int survivalReserveCents;
        public int cashRunwayWeeks;
        public string primaryBlockedReason;
        public int unitsSold;
        public int customerVisits;
        public bool reorderTriggered;
        public int reorderUnitsQueued;
        public int reorderUnitsReceived;
        public float stockHealth01 = 1f;
        public float reliability01 = 1f;
        public float competitionPressure01;
        public int storageCapacityUnits;
        public int currentStoredUnits;
        public int processingCapacityUnitsPerWeek;
        public int currentProcessingUnitsPerWeek;
        public int serviceCapacityVisitsPerDay;
        public int currentServiceVisitsPerDay;
        public string capacityBottleneckKey;
        public List<BusinessCategorySnapshot> categories = new();
        public List<BusinessWorkerSnapshot> workers = new();
        public List<LedgerEntry> ledgerEntries = new();
        public List<string> gapNotes = new();
    }

    [Serializable]
    public sealed class PortfolioReportSnapshot
    {
        public ReportPeriod period;
        public SimulationDate generatedOnDate;
        public int ownedLandCount;
        public int ownedBusinessCount;
        public List<BusinessReportSnapshot> businesses = new();
        public List<LedgerEntry> ledgerEntries = new();
        public List<string> gapNotes = new();
    }
}
