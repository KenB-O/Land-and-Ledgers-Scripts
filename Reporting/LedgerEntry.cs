using System;
using LandLedgers.Time;

namespace LandLedgers.Reporting
{
    public enum LedgerEntryKind
    {
        Revenue = 0,
        Expense = 1,
        InventoryAdjustment = 2,
        CashAdjustment = 3,
        Informational = 4
    }

    public enum LedgerEntryCategory
    {
        Sales = 0,
        Payroll = 1,
        Reorder = 2,
        Inventory = 3,
        Acquisition = 4,
        Maintenance = 5,
        Tax = 6,
        Debt = 7,
        Other = 100
    }

    public enum LedgerEntrySource
    {
        Unknown = 0,
        Snapshot = 1,
        WeeklySummary = 2,
        MonthlySummary = 3,
        PortfolioRollup = 4,
        FutureLedger = 100
    }

    [Serializable]
    public struct LedgerEntry
    {
        public string EntryId;
        public LedgerEntryKind Kind;
        public LedgerEntryCategory Category;
        public LedgerEntrySource Source;
        public SimulationDate Date;
        public string BusinessId;
        public string Label;
        public int AmountCents;
        public int Quantity;
        public string Unit;
        public string SourceId;
        public bool IsGeneratedReportSummary;
        public string ReportSummaryKey;

        public LedgerEntry(
            string entryId,
            LedgerEntryKind kind,
            LedgerEntryCategory category,
            LedgerEntrySource source,
            SimulationDate date,
            string businessId,
            string label,
            int amountCents,
            int quantity,
            string unit,
            string sourceId,
            bool isGeneratedReportSummary = false,
            string reportSummaryKey = "")
        {
            EntryId = entryId ?? string.Empty;
            Kind = kind;
            Category = category;
            Source = source;
            Date = date;
            BusinessId = businessId ?? string.Empty;
            Label = label ?? string.Empty;
            AmountCents = amountCents;
            Quantity = Math.Max(0, quantity);
            Unit = unit ?? string.Empty;
            SourceId = sourceId ?? string.Empty;
            IsGeneratedReportSummary = isGeneratedReportSummary;
            ReportSummaryKey = reportSummaryKey ?? string.Empty;
        }
    }
}
