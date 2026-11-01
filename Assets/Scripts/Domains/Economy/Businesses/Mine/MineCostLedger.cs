using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Mine
{
    /// <summary>
    /// D3C: mine cost causes per Canon 21.7Q — "Mining reports should separate
    /// major economic causes such as exploration/development, extraction
    /// labor, support/timber, hoisting/haulage, pumping, power/fuel,
    /// treatment, freight, maintenance and administration." No invented
    /// causes; the ledger separates what the operation actually spends.
    /// </summary>
    public enum MineCostCause
    {
        Unspecified = 0,
        Exploration = 1,     // prospecting, claim work, sampling ahead of production
        Development = 2,     // sinking, driving, timbering — opening the ground
        ExtractionLabor = 3, // miners/muckers breaking and loading ore
        SupportTimber = 4,   // timber sets and ground support
        HoistingHaulage = 5, // hoisting plant work, internal haulage
        Pumping = 6,         // dewatering labor and pump running costs
        PowerFuel = 7,       // steam coal, lighting, motive power
        Treatment = 8,       // milling/smelter treatment charges
        Freight = 9,         // ore and supply haulage to market
        Maintenance = 10,    // repair, rope replacement, plant upkeep
        Administration = 11, // superintendent, assay office fees, books
    }

    /// <summary>D3C: one booked mine cost — day, cause, cents, memo, and the source record id for audit.</summary>
    [Serializable]
    public sealed class MineCostEntry
    {
        [SerializeField, Min(0)]
        private int dayIndex;

        [SerializeField]
        private MineCostCause cause = MineCostCause.Unspecified;

        [SerializeField, Min(0)]
        private int cents;

        [SerializeField, TextArea(1, 2)]
        private string memo = string.Empty;

        [SerializeField]
        private string sourceRecordId = string.Empty;

        public int DayIndex => Math.Max(0, dayIndex);
        public MineCostCause Cause => cause;
        public int Cents => Math.Max(0, cents);
        public string Memo => memo ?? string.Empty;
        public string SourceRecordId => sourceRecordId ?? string.Empty;

        public MineCostEntry() { }

        public MineCostEntry(int dayIndex, MineCostCause cause, int cents, string memo, string sourceRecordId)
        {
            this.dayIndex = Math.Max(0, dayIndex);
            this.cause = cause;
            this.cents = Math.Max(0, cents);
            this.memo = memo ?? string.Empty;
            this.sourceRecordId = sourceRecordId ?? string.Empty;
        }

        public MineCostEntrySaveDto CaptureSaveDto()
        {
            return new MineCostEntrySaveDto
            {
                dayIndex = DayIndex,
                cause = cause,
                cents = Cents,
                memo = Memo,
                sourceRecordId = SourceRecordId,
            };
        }

        public static MineCostEntry FromSaveDto(MineCostEntrySaveDto dto)
        {
            if (dto == null)
                return null;
            return new MineCostEntry(dto.dayIndex, dto.cause, dto.cents, dto.memo ?? string.Empty,
                dto.sourceRecordId ?? string.Empty);
        }
    }

    /// <summary>
    /// D3C: the mine's cost-cause ledger (Canon 21.7Q). Bookkeeping improves
    /// owner interpretation, but the costs themselves come from the real
    /// operation — every entry cites a source record (wage settlement, timber
    /// record, shipment settlement) and is booked only through the explicit
    /// feed helpers below or direct RecordCost calls by the caller.
    /// </summary>
    [Serializable]
    public sealed class MineCostLedger
    {
        [SerializeField]
        private List<MineCostEntry> entries = new List<MineCostEntry>();

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<MineCostEntry> Entries => entries;

        public MineCostLedger() { }

        /// <summary>Books a cost. Refuses unspecified causes and non-positive cents loudly.</summary>
        public string RecordCost(int dayIndex, MineCostCause cause, int cents, string memo, string sourceRecordId)
        {
            if (cause == MineCostCause.Unspecified)
                return "MineCostLedger.RecordCost: a real cost cause is required — unclassified costs are not booked.";
            if (cents <= 0)
                return "MineCostLedger.RecordCost: cost cents must be positive — zero-cost entries carry no information.";
            entries.Add(new MineCostEntry(dayIndex, cause, cents, memo, sourceRecordId));
            return null;
        }

        public int TotalCentsByCause(MineCostCause cause)
        {
            int total = 0;
            foreach (MineCostEntry entry in entries)
            {
                if (entry.Cause == cause)
                    total = Math.Max(0, total + entry.Cents);
            }
            return total;
        }

        public int TotalCents
        {
            get
            {
                int total = 0;
                foreach (MineCostEntry entry in entries)
                    total = Math.Max(0, total + entry.Cents);
                return total;
            }
        }

        public static string GetCauseDisplayName(MineCostCause cause)
        {
            return cause switch
            {
                MineCostCause.Exploration => "Exploration",
                MineCostCause.Development => "Development",
                MineCostCause.ExtractionLabor => "Extraction labor",
                MineCostCause.SupportTimber => "Support / timber",
                MineCostCause.HoistingHaulage => "Hoisting / haulage",
                MineCostCause.Pumping => "Pumping",
                MineCostCause.PowerFuel => "Power / fuel",
                MineCostCause.Treatment => "Treatment",
                MineCostCause.Freight => "Freight",
                MineCostCause.Maintenance => "Maintenance",
                MineCostCause.Administration => "Administration",
                _ => "Unspecified",
            };
        }

        /// <summary>Canon 21.7Q mining report: costs separated by cause, dollars and cents.</summary>
        public string BuildCostCauseReport(string mineName)
        {
            var lines = new List<string>
            {
                $"Mine cost report — {mineName ?? "mine"}: {entries.Count} entries, {TotalCents}c total.",
            };
            foreach (MineCostCause cause in Enum.GetValues(typeof(MineCostCause)))
            {
                if (cause == MineCostCause.Unspecified)
                    continue;
                int total = TotalCentsByCause(cause);
                if (total > 0)
                    lines.Add($"  {GetCauseDisplayName(cause)}: {total}c.");
            }
            return string.Join("\n", lines.ToArray());
        }

        public MineCostLedgerSaveDto CaptureSaveDto()
        {
            var dto = new MineCostLedgerSaveDto();
            foreach (MineCostEntry entry in entries)
                dto.entries.Add(entry.CaptureSaveDto());
            return dto;
        }

        public static MineCostLedger FromSaveDto(MineCostLedgerSaveDto dto)
        {
            var ledger = new MineCostLedger();
            if (dto != null)
            {
                foreach (MineCostEntrySaveDto entryDto in dto.entries)
                {
                    MineCostEntry entry = MineCostEntry.FromSaveDto(entryDto);
                    if (entry != null)
                        ledger.entries.Add(entry);
                }
            }
            return ledger;
        }
    }

    /// <summary>
    /// D3C: explicit feed helpers — the caller-side wiring that books real
    /// operating events into the cost ledger. Each helper takes the source
    /// record produced by the mine runtime (wage settlement, timbering
    /// record, hoist, shipment breakdown) so costs always trace to real work.
    /// </summary>
    public static class MineCostFeed
    {
        /// <summary>Books one shift's crew payroll as extraction labor (from a wage settlement).</summary>
        public static string FeedShiftWages(MineLaborRegister register, MineCostLedger ledger,
            int dayIndex, string periodLabel, List<string> callerDiagnostics)
        {
            callerDiagnostics = callerDiagnostics ?? new List<string>();
            if (register == null || ledger == null)
            {
                callerDiagnostics.Add("MineCostFeed.FeedShiftWages: register and ledger are required.");
                return "MineCostFeed.FeedShiftWages: register and ledger are required.";
            }
            int bill = register.ShiftWageBillCents();
            if (bill <= 0)
                return null; // no active crew — nothing to book, not an error
            return ledger.RecordCost(dayIndex, MineCostCause.ExtractionLabor, bill,
                $"crew payroll {periodLabel}: {register.ActiveAssignments().Count} active assignments",
                $"wages-{periodLabel}");
        }

        /// <summary>Books timber consumption as support/timber (from a timbering record + the lumber price paid).</summary>
        public static string FeedTimbering(MineTimberingRecord record, MineCostLedger ledger,
            int lumberCostCents, List<string> callerDiagnostics)
        {
            callerDiagnostics = callerDiagnostics ?? new List<string>();
            if (record == null || ledger == null)
            {
                callerDiagnostics.Add("MineCostFeed.FeedTimbering: record and ledger are required.");
                return "MineCostFeed.FeedTimbering: record and ledger are required.";
            }
            if (lumberCostCents <= 0)
            {
                callerDiagnostics.Add("MineCostFeed.FeedTimbering: lumber cost must be positive — the yard sale price is the cost basis.");
                return "MineCostFeed.FeedTimbering: lumber cost must be positive.";
            }
            return ledger.RecordCost(record.DayIndex, MineCostCause.SupportTimber, lumberCostCents,
                $"timbered {record.FeetTimbered} ft on shaft {record.ShaftId}: {record.TimberSetsConsumed} sets",
                $"timber-{record.ShaftId}-day{record.DayIndex}");
        }

        /// <summary>Books hoist repair / rope replacement as maintenance.</summary>
        public static string FeedHoistMaintenance(MineHoist hoist, MineCostLedger ledger, int dayIndex,
            int costCents, string workKind, List<string> callerDiagnostics)
        {
            callerDiagnostics = callerDiagnostics ?? new List<string>();
            if (hoist == null || ledger == null)
            {
                callerDiagnostics.Add("MineCostFeed.FeedHoistMaintenance: hoist and ledger are required.");
                return "MineCostFeed.FeedHoistMaintenance: hoist and ledger are required.";
            }
            if (costCents <= 0)
            {
                callerDiagnostics.Add("MineCostFeed.FeedHoistMaintenance: maintenance cost must be positive.");
                return "MineCostFeed.FeedHoistMaintenance: maintenance cost must be positive.";
            }
            return ledger.RecordCost(dayIndex, MineCostCause.Maintenance, costCents,
                $"{workKind}: hoist {hoist.HoistId} ({MineHoist.GetHoistKindDisplayName(hoist.HoistKind)}) on shaft {hoist.ShaftId}",
                $"hoist-maint-{hoist.HoistId}-day{dayIndex}");
        }

        /// <summary>Books a grade-based smelter settlement's treatment and freight as their own causes.</summary>
        public static string FeedShipmentSettlement(MineOreShipmentOrder order, MineSettlementBreakdown breakdown,
            MineCostLedger ledger, int dayIndex, List<string> callerDiagnostics)
        {
            callerDiagnostics = callerDiagnostics ?? new List<string>();
            if (order == null || breakdown == null || ledger == null)
            {
                callerDiagnostics.Add("MineCostFeed.FeedShipmentSettlement: order, breakdown and ledger are required.");
                return "MineCostFeed.FeedShipmentSettlement: order, breakdown and ledger are required.";
            }
            string rejection = null;
            if (breakdown.TreatmentCents > 0)
            {
                rejection = ledger.RecordCost(dayIndex,
                    MineCostCause.Treatment, breakdown.TreatmentCents,
                    $"smelter treatment: {order.Tons} tons to {order.SmelterName}", order.OrderId);
            }
            if (rejection == null && breakdown.FreightCents > 0)
            {
                rejection = ledger.RecordCost(dayIndex,
                    MineCostCause.Freight, breakdown.FreightCents,
                    $"ore freight: {order.Tons} tons to {order.SmelterName}", order.OrderId);
            }
            if (rejection != null)
                callerDiagnostics.Add($"MineCostFeed.FeedShipmentSettlement: {rejection}");
            return rejection;
        }
    }

    /// <summary>D3C: save DTOs for the cost ledger. Owned by the mine runtime (standing rule).</summary>
    [Serializable]
    public sealed class MineCostEntrySaveDto
    {
        public int dayIndex;
        public MineCostCause cause = MineCostCause.Unspecified;
        public int cents;
        public string memo = string.Empty;
        public string sourceRecordId = string.Empty;
    }

    [Serializable]
    public sealed class MineCostLedgerSaveDto
    {
        public List<MineCostEntrySaveDto> entries = new List<MineCostEntrySaveDto>();
    }
}
