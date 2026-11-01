using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.LumberYard
{
    /// <summary>
    /// W4B: the two kinds inside the Canon "nails and hardware" cost bucket
    /// (Canon §5.2). Cut nails dominate 1870s construction (wire nails arrive
    /// in the 1880s-90s); simple hardware is hinges, latches, strap iron and
    /// the like. Both are forged locally from iron stock or arrive by rail
    /// from eastern ironworks — tracked separately for honesty, sold as one
    /// bucket (the construction quote line is already "Nails / Simple
    /// Hardware").
    /// </summary>
    public enum LumberYardHardwareKind
    {
        Nails = 0,
        SimpleHardware = 1,
    }

    /// <summary>
    /// W4B: the yard's own nails/hardware lot. Survey result: nails and simple
    /// hardware are currently supplied by the local BLACKSMITH, forged from
    /// EQU-1 imported iron stock ("Pittsburgh ironworks, via railhead") — the
    /// construction system consumes the blacksmith's "tools_hardware" category
    /// stock directly. A real supplier therefore exists, and the yard's intake
    /// names that smith: every lot traces to a named blacksmith business
    /// instance (plus the iron import order when known), a named import order,
    /// or an explicit one-time bootstrap endowment. Never anonymous.
    /// </summary>
    [Serializable]
    public sealed class LumberYardHardwareLot
    {
        public EntityId LotId = EntityId.Invalid; // yard-side custody id (EntityKind.Lot)
        public int HardwareUnits;
        public LumberYardHardwareKind HardwareKind = LumberYardHardwareKind.Nails;
        public int AcquiredDayIndex;

        // Provenance: exactly one of these paths is named per lot.
        public string SourceBlacksmithBusinessId = string.Empty; // the smith who forged it
        public int ForgedDayIndex = -1;
        public string IronImportOrderId = string.Empty; // the iron it was forged from, when known
        public string ImportOrderId = string.Empty;     // direct hardware import order
        public string OriginName = string.Empty;
        public bool IsBootstrapEndowment;
        public string SupplierNote = string.Empty;
        public int UnitCostCents;

        public LumberYardHardwareLot() { }

        public string ProvenanceChain()
        {
            if (IsBootstrapEndowment)
            {
                return "OPENING ENDOWMENT (one-time; never auto-replenishes)"
                    + $" | {HardwareKind}";
            }

            var parts = new List<string>();
            parts.Add(HardwareKind.ToString());
            if (!string.IsNullOrWhiteSpace(SourceBlacksmithBusinessId))
                parts.Add($"forged by {SourceBlacksmithBusinessId}");
            if (ForgedDayIndex >= 0) parts.Add($"forged day {ForgedDayIndex}");
            if (!string.IsNullOrWhiteSpace(IronImportOrderId))
                parts.Add($"iron order {IronImportOrderId}");
            if (!string.IsNullOrWhiteSpace(ImportOrderId)) parts.Add($"import {ImportOrderId}");
            if (!string.IsNullOrWhiteSpace(OriginName)) parts.Add($"origin {OriginName}");
            if (!string.IsNullOrWhiteSpace(SupplierNote)) parts.Add(SupplierNote);
            parts.Add($"yard lot {LotId}");
            return string.Join(" | ", parts.ToArray());
        }
    }

    /// <summary>
    /// W4B: one withdrawal of hardware units into a sale — the audit line of
    /// what left the yard, preserving each source lot's provenance.
    /// </summary>
    [Serializable]
    public sealed class LumberYardHardwareDispenseLine
    {
        public EntityId LotId = EntityId.Invalid;
        public int UnitsTaken;
        public LumberYardHardwareKind HardwareKind = LumberYardHardwareKind.Nails;
        public string ProvenanceChain = string.Empty;

        public LumberYardHardwareDispenseLine() { }
    }

    /// <summary>
    /// W4B: the yard's nails/simple-hardware inventory — lots in, withdrawn
    /// units out, FIFO so stock ages honestly. Every lot names its real
    /// supplier (the forging blacksmith, an import order, or the one-time
    /// opening endowment).
    /// </summary>
    public sealed class LumberYardHardwareStock
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<LumberYardHardwareLot> lots = new List<LumberYardHardwareLot>();
        private readonly string yardBusinessId;

        public LumberYardHardwareStock(string yardBusinessId)
        {
            this.yardBusinessId = yardBusinessId ?? string.Empty;
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<LumberYardHardwareLot> Lots => lots;
        public string YardBusinessId => yardBusinessId;

        public int TotalHardwareUnits
        {
            get
            {
                int total = 0;
                foreach (var lot in lots) total += Math.Max(0, lot.HardwareUnits);
                return total;
            }
        }

        public int AvailableUnits(LumberYardHardwareKind? kindFilter)
        {
            int total = 0;
            foreach (var lot in lots)
            {
                if (kindFilter.HasValue && lot.HardwareKind != kindFilter.Value) continue;
                total += Math.Max(0, lot.HardwareUnits);
            }
            return total;
        }

        private void SortFifo()
        {
            lots.Sort((a, b) =>
            {
                int day = a.AcquiredDayIndex.CompareTo(b.AcquiredDayIndex);
                return day != 0 ? day : a.LotId.ToString().CompareTo(b.LotId.ToString());
            });
        }

        private static string ValidateNewLot(LumberYardHardwareLot lot, string path)
        {
            if (lot == null)
                return $"LumberYardHardwareStock.{path}: no lot offered — hardware is not conjured.";
            if (lot.LotId == EntityId.Invalid)
                return $"LumberYardHardwareStock.{path}: the lot needs an EntityId — anonymous stock is refused.";
            if (lot.HardwareUnits <= 0)
                return $"LumberYardHardwareStock.{path}: the lot needs positive units.";
            return null;
        }

        /// <summary>
        /// Receives hardware forged by a NAMED blacksmith business instance —
        /// the primary honest supply path (the smith forges from imported iron
        /// stock; the iron import order is recorded when the smith reports
        /// it). Returns the refusal, or null.
        /// </summary>
        public string ReceiveFromBlacksmith(
            EntityId yardLotId,
            LumberYardHardwareKind kind,
            int units,
            string blacksmithBusinessId,
            int forgedDayIndex,
            string ironImportOrderId,
            int acquiredDayIndex,
            int unitCostCents,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lot = new LumberYardHardwareLot
            {
                LotId = yardLotId,
                HardwareKind = kind,
                HardwareUnits = units,
                AcquiredDayIndex = acquiredDayIndex,
                UnitCostCents = Math.Max(0, unitCostCents),
            };
            string refusal = ValidateNewLot(lot, "ReceiveFromBlacksmith");
            if (refusal != null) return refusal;
            if (string.IsNullOrWhiteSpace(blacksmithBusinessId))
                return "LumberYardHardwareStock.ReceiveFromBlacksmith: the forging blacksmith must be named — anonymous hardware is refused.";

            lot.SourceBlacksmithBusinessId = blacksmithBusinessId.Trim();
            lot.ForgedDayIndex = forgedDayIndex;
            lot.IronImportOrderId = ironImportOrderId ?? string.Empty;

            lots.Add(lot);
            diag.Add($"LumberYardHardwareStock ({yardBusinessId}): received {units} {kind} units "
                + $"(yard lot {yardLotId}) forged by '{blacksmithBusinessId}' — {lot.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Receives hardware arriving on a named import order (EQU-1 off-map
        /// trade link — see LumberYardSupply.HardwareImportMaterialId). The
        /// import order id and origin are required. Returns the refusal, or
        /// null.
        /// </summary>
        public string ReceiveImportArrival(
            EntityId yardLotId,
            LumberYardHardwareKind kind,
            int units,
            string importOrderId,
            string originName,
            int acquiredDayIndex,
            int unitCostCents,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lot = new LumberYardHardwareLot
            {
                LotId = yardLotId,
                HardwareKind = kind,
                HardwareUnits = units,
                AcquiredDayIndex = acquiredDayIndex,
                UnitCostCents = Math.Max(0, unitCostCents),
            };
            string refusal = ValidateNewLot(lot, "ReceiveImportArrival");
            if (refusal != null) return refusal;
            if (string.IsNullOrWhiteSpace(importOrderId))
                return "LumberYardHardwareStock.ReceiveImportArrival: the import order id must be named — no orphan stock.";
            if (string.IsNullOrWhiteSpace(originName))
                return "LumberYardHardwareStock.ReceiveImportArrival: the origin must be named — no anonymous sources.";

            lot.ImportOrderId = importOrderId.Trim();
            lot.OriginName = originName.Trim();

            lots.Add(lot);
            diag.Add($"LumberYardHardwareStock ({yardBusinessId}): import arrival {units} {kind} units "
                + $"(yard lot {yardLotId}) on order '{importOrderId}' from {originName}.");
            return null;
        }

        /// <summary>
        /// One-time opening endowment path. Only lots explicitly marked as
        /// bootstrap endowments are accepted here. The once-guard lives on the
        /// owning runtime — this stock never auto-replenishes.
        /// </summary>
        public string ReceiveBootstrapEndowment(LumberYardHardwareLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            string refusal = ValidateNewLot(lot, "ReceiveBootstrapEndowment");
            if (refusal != null) return refusal;
            if (!lot.IsBootstrapEndowment)
                return "LumberYardHardwareStock.ReceiveBootstrapEndowment: not marked as a bootstrap endowment — use a real intake path.";

            lots.Add(lot);
            diag.Add($"LumberYardHardwareStock ({yardBusinessId}): BOOTSTRAP endowment {lot.HardwareUnits} {lot.HardwareKind} units "
                + $"(yard lot {lot.LotId}) — one-time opening stock, never auto-replenishes.");
            return null;
        }

        /// <summary>
        /// Withdraws up to the requested units, oldest acquisitions first,
        /// nails before simple hardware when no kind filter is given (the
        /// construction bucket is "Nails / Simple Hardware" — nails are the
        /// primary fill). Records the dispense lines with provenance. A
        /// shortfall returns fewer lines — never invented units.
        /// </summary>
        public List<LumberYardHardwareDispenseLine> TryWithdrawUnits(
            int units, LumberYardHardwareKind? kindFilter, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lines = new List<LumberYardHardwareDispenseLine>();
            if (units <= 0) return lines;

            SortFifo();

            int remaining = units;
            // Nails-first pass, then simple hardware, when unfiltered.
            var passKinds = new List<LumberYardHardwareKind?>();
            if (kindFilter.HasValue)
            {
                passKinds.Add(kindFilter.Value);
            }
            else
            {
                passKinds.Add(LumberYardHardwareKind.Nails);
                passKinds.Add(LumberYardHardwareKind.SimpleHardware);
            }

            foreach (var passKind in passKinds)
            {
                if (remaining <= 0) break;
                foreach (var lot in lots)
                {
                    if (remaining <= 0) break;
                    if (lot.HardwareKind != passKind.Value) continue;
                    int available = Math.Max(0, lot.HardwareUnits);
                    if (available <= 0) continue;
                    int take = Math.Min(remaining, available);
                    lot.HardwareUnits -= take;
                    remaining -= take;
                    lines.Add(new LumberYardHardwareDispenseLine
                    {
                        LotId = lot.LotId,
                        UnitsTaken = take,
                        HardwareKind = lot.HardwareKind,
                        ProvenanceChain = lot.ProvenanceChain(),
                    });
                }
            }

            lots.RemoveAll(l => l.HardwareUnits <= 0);

            if (remaining > 0)
            {
                diag.Add($"LumberYardHardwareStock ({yardBusinessId}): shortfall — requested {units}, withdrew {units - remaining}. "
                    + "Empty shelves stay empty; nothing invented.");
            }
            return lines;
        }

        /// <summary>W4B save contract: lives inside the owning stock class.</summary>
        [Serializable]
        public sealed class LumberYardHardwareStockSaveDto
        {
            public List<LumberYardHardwareLot> Lots = new List<LumberYardHardwareLot>();
        }

        public LumberYardHardwareStockSaveDto CaptureSaveDto()
        {
            var dto = new LumberYardHardwareStockSaveDto();
            foreach (var lot in lots)
            {
                dto.Lots.Add(new LumberYardHardwareLot
                {
                    LotId = lot.LotId,
                    HardwareUnits = lot.HardwareUnits,
                    HardwareKind = lot.HardwareKind,
                    AcquiredDayIndex = lot.AcquiredDayIndex,
                    SourceBlacksmithBusinessId = lot.SourceBlacksmithBusinessId,
                    ForgedDayIndex = lot.ForgedDayIndex,
                    IronImportOrderId = lot.IronImportOrderId,
                    ImportOrderId = lot.ImportOrderId,
                    OriginName = lot.OriginName,
                    IsBootstrapEndowment = lot.IsBootstrapEndowment,
                    SupplierNote = lot.SupplierNote,
                    UnitCostCents = lot.UnitCostCents,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(LumberYardHardwareStockSaveDto dto)
        {
            lots.Clear();
            if (dto == null) return;
            foreach (var lot in dto.Lots)
            {
                if (lot == null) continue;
                lots.Add(lot);
            }
        }
    }
}
