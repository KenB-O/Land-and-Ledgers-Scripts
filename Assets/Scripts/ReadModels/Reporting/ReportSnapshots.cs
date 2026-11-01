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

    /// <summary>
    /// Pass XII Worker C (LL-PLAY-013) producer contract — the builder
    /// sums these fields as supplied, so the feeder (the future
    /// snapshot producer, not this DTO) must attest: (1) revenue is
    /// real customer/counterparty sales only; loan proceeds, owner
    /// capital and deposits never enter revenueCents (they move cash
    /// and appear, when known, via cashTransferInCents or the cash
    /// bridge's unexplained-inflow field); (2) costOfGoodsSoldCents
    /// and reorderExpenseCents/localSupplyExpenseCents must never
    /// contain the same spend twice — report procurement spend in
    /// reorder/localSupply and leave COGS zero, or report COGS and
    /// leave the procurement fields zero for those units; the builder
    /// sums all three into operating expense, and no builder-side
    /// check can un-merge one spend reported in two fields; (3)
    /// payrollExpenseCents is cash wages actually paid in the period;
    /// accrued-but-unpaid wages belong in accruedLiabilityCents, never
    /// in payroll expense and never silently dropped; (4) rent is an
    /// operating expense (rentExpenseCents), tax is a cash expense
    /// outside operating profit (taxExpenseCents); (5) owner draws
    /// and owner cash transfers are financing, never revenue or
    /// operating expense. A snapshot that violates (1)–(5) produces a
    /// report that is wrong by construction, and the builder can only
    /// flag what it can see (see BusinessReportBuilder gaps).
    /// </summary>
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
        // Pass XII Worker C (appended — never reorder serialized
        // fields): rent is operating expense, tax is a cash expense
        // outside operating profit, accrued liability is wages (or
        // other obligations) owed but not yet paid.
        public int rentExpenseCents;
        public int taxExpenseCents;
        public int accruedLiabilityCents;
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
