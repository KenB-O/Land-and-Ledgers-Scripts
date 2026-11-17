using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.LumberYard
{
    /// <summary>
    /// F1 (workstream F, glass leg): the yard's window-glass lot. One
    /// unit is one pane — one window light, single-strength common window
    /// glass, roughly 8x10 to 10x12 in. Glass is an off-map MANUFACTURED
    /// good (Pittsburgh glassworks, via railhead — registered by
    /// LumberYardSupply.EnsureGlassImportable); there is no local
    /// glassmaking industry in this pass, so the ONLY intake is a named
    /// import order. No bootstrap endowment exists for glass: a yard
    /// opens with zero panes, and the first glazed build waits for the
    /// 14-day import — glass never appears in the yard (or at a building
    /// site) without a paid order behind it.
    /// </summary>
    [Serializable]
    public sealed class LumberYardGlassLot
    {
        public EntityId LotId = EntityId.Invalid; // yard-side custody id (EntityKind.Lot)
        public int GlassPanes;
        public int AcquiredDayIndex;

        // Provenance: exactly one path is named per lot — a named import
        // order. Never anonymous.
        public string ImportOrderId = string.Empty; // the import order that brought it
        public string OriginName = string.Empty;
        public int UnitCostCents;

        public LumberYardGlassLot() { }

        public string ProvenanceChain()
        {
            var parts = new List<string>();
            parts.Add("window glass");
            if (!string.IsNullOrWhiteSpace(ImportOrderId)) parts.Add($"import {ImportOrderId}");
            if (!string.IsNullOrWhiteSpace(OriginName)) parts.Add($"origin {OriginName}");
            parts.Add($"yard lot {LotId}");
            return string.Join(" | ", parts.ToArray());
        }
    }

    /// <summary>
    /// F1: one withdrawal of glass panes into a sale — the audit line of
    /// what left the yard, preserving each source lot's provenance.
    ///
    /// The line also carries the source lot's full snapshot, so a voided
    /// sale can restore a lot that was fully drained — and therefore
    /// removed from stock — with its provenance intact (the G1 pattern).
    /// </summary>
    [Serializable]
    public sealed class LumberYardGlassDispenseLine
    {
        public EntityId LotId = EntityId.Invalid;
        public int UnitsTaken;
        public string ProvenanceChain = string.Empty;

        // Source-lot snapshot for an exact void restock.
        public int AcquiredDayIndex;
        public string ImportOrderId = string.Empty;
        public string OriginName = string.Empty;
        public int UnitCostCents;

        public LumberYardGlassDispenseLine() { }

        /// <summary>F1: builds the dispense line for a withdrawal from a real lot, snapshot included.</summary>
        public static LumberYardGlassDispenseLine For(LumberYardGlassLot lot, int take)
        {
            if (lot == null) return new LumberYardGlassDispenseLine { UnitsTaken = take };
            return new LumberYardGlassDispenseLine
            {
                LotId = lot.LotId,
                UnitsTaken = take,
                ProvenanceChain = lot.ProvenanceChain(),
                AcquiredDayIndex = lot.AcquiredDayIndex,
                ImportOrderId = lot.ImportOrderId,
                OriginName = lot.OriginName,
                UnitCostCents = lot.UnitCostCents,
            };
        }
    }

    /// <summary>
    /// F1: the yard's window-glass inventory — lots in, withdrawn panes
    /// out, FIFO so stock ages honestly. Every lot names its import order
    /// and origin. The stock never auto-replenishes: panes arrive only
    /// through ReceiveImportArrival, driven by the glass import driver.
    /// </summary>
    public sealed class LumberYardGlassStock
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<LumberYardGlassLot> lots = new List<LumberYardGlassLot>();
        private readonly string yardBusinessId;

        public LumberYardGlassStock(string yardBusinessId)
        {
            this.yardBusinessId = yardBusinessId ?? string.Empty;
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<LumberYardGlassLot> Lots => lots;
        public string YardBusinessId => yardBusinessId;

        public int TotalGlassPanes
        {
            get
            {
                int total = 0;
                foreach (var lot in lots) total += Math.Max(0, lot.GlassPanes);
                return total;
            }
        }

        public int AvailableUnits()
        {
            return TotalGlassPanes;
        }

        private void SortFifo()
        {
            lots.Sort((a, b) =>
            {
                int day = a.AcquiredDayIndex.CompareTo(b.AcquiredDayIndex);
                return day != 0 ? day : a.LotId.ToString().CompareTo(b.LotId.ToString());
            });
        }

        private static string ValidateNewLot(LumberYardGlassLot lot, string path)
        {
            if (lot == null)
                return $"LumberYardGlassStock.{path}: no lot offered — glass is not conjured.";
            if (lot.LotId == EntityId.Invalid)
                return $"LumberYardGlassStock.{path}: the lot needs an EntityId — anonymous stock is refused.";
            if (lot.GlassPanes <= 0)
                return $"LumberYardGlassStock.{path}: the lot needs positive panes.";
            return null;
        }

        /// <summary>
        /// Receives glass panes arriving on a named import order
        /// (LumberYardSupply.GlassImportMaterialId). The import order id
        /// and origin are required. Returns the refusal, or null.
        /// </summary>
        public string ReceiveImportArrival(
            EntityId yardLotId,
            int panes,
            string importOrderId,
            string originName,
            int acquiredDayIndex,
            int unitCostCents,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lot = new LumberYardGlassLot
            {
                LotId = yardLotId,
                GlassPanes = panes,
                AcquiredDayIndex = acquiredDayIndex,
                UnitCostCents = Math.Max(0, unitCostCents),
            };
            string refusal = ValidateNewLot(lot, "ReceiveImportArrival");
            if (refusal != null) return refusal;
            if (string.IsNullOrWhiteSpace(importOrderId))
                return "LumberYardGlassStock.ReceiveImportArrival: the import order id must be named — no orphan stock.";
            if (string.IsNullOrWhiteSpace(originName))
                return "LumberYardGlassStock.ReceiveImportArrival: the origin must be named — no anonymous sources.";

            lot.ImportOrderId = importOrderId.Trim();
            lot.OriginName = originName.Trim();

            lots.Add(lot);
            diag.Add($"LumberYardGlassStock ({yardBusinessId}): import arrival {panes} glass panes "
                + $"(yard lot {yardLotId}) on order '{importOrderId}' from {originName}.");
            return null;
        }

        /// <summary>
        /// Withdraws up to the requested panes, oldest acquisitions
        /// first. Records the dispense lines with provenance. A
        /// shortfall returns fewer lines — never invented panes.
        /// </summary>
        public List<LumberYardGlassDispenseLine> TryWithdrawUnits(
            int panes, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lines = new List<LumberYardGlassDispenseLine>();
            if (panes <= 0) return lines;

            SortFifo();

            int remaining = panes;
            foreach (var lot in lots)
            {
                if (remaining <= 0) break;
                int available = Math.Max(0, lot.GlassPanes);
                if (available <= 0) continue;
                int take = Math.Min(remaining, available);
                lot.GlassPanes -= take;
                remaining -= take;
                lines.Add(LumberYardGlassDispenseLine.For(lot, take));
            }

            lots.RemoveAll(l => l.GlassPanes <= 0);

            if (remaining > 0)
            {
                diag.Add($"LumberYardGlassStock ({yardBusinessId}): shortfall — requested {panes}, withdrew {panes - remaining}. "
                    + "Empty shelves stay empty; nothing invented.");
            }
            return lines;
        }

        /// <summary>F1: finds a lot by its yard custody id, or null.</summary>
        public LumberYardGlassLot FindLot(EntityId lotId)
        {
            foreach (var lot in lots)
            {
                if (lot != null && lot.LotId.Equals(lotId)) return lot;
            }
            return null;
        }

        /// <summary>
        /// F1: returns voided-sale panes to saleable stock from a
        /// dispense line's snapshot. A lot that still exists is topped
        /// up; a lot the sale fully drained — and that withdrawal
        /// removed — is recreated under its SAME yard lot id with its
        /// provenance intact. Returns the refusal, or null.
        /// </summary>
        public string ReturnVoidedUnits(LumberYardGlassDispenseLine line, int panes, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (line == null) return "LumberYardGlassStock.ReturnVoidedUnits: no dispense line — nothing to return.";
            if (panes <= 0) return "LumberYardGlassStock.ReturnVoidedUnits: returned panes must be positive.";
            if (panes > line.UnitsTaken)
                return $"LumberYardGlassStock.ReturnVoidedUnits: lot {line.LotId} dispensed only {line.UnitsTaken} panes — {panes} requested.";
            if (line.LotId == EntityId.Invalid)
                return "LumberYardGlassStock.ReturnVoidedUnits: the dispense line names no yard lot — refusing.";
            LumberYardGlassLot lot = FindLot(line.LotId);
            if (lot == null)
            {
                lot = new LumberYardGlassLot
                {
                    LotId = line.LotId,
                    GlassPanes = panes,
                    AcquiredDayIndex = line.AcquiredDayIndex,
                    ImportOrderId = line.ImportOrderId,
                    OriginName = line.OriginName,
                    UnitCostCents = line.UnitCostCents,
                };
                lots.Add(lot);
                diag.Add($"LumberYardGlassStock ({yardBusinessId}): voided sale restored lot {line.LotId} "
                    + $"({panes} glass panes) — the drained lot was recreated with its provenance intact.");
            }
            else
            {
                lot.GlassPanes += panes;
                diag.Add($"LumberYardGlassStock ({yardBusinessId}): voided sale returned {panes} glass panes to lot {line.LotId} "
                    + $"(now {lot.GlassPanes} panes).");
            }
            return null;
        }

        /// <summary>F1 save contract: lives inside the owning stock class.</summary>
        [Serializable]
        public sealed class LumberYardGlassStockSaveDto
        {
            public List<LumberYardGlassLot> Lots = new List<LumberYardGlassLot>();
        }

        public LumberYardGlassStockSaveDto CaptureSaveDto()
        {
            var dto = new LumberYardGlassStockSaveDto();
            foreach (var lot in lots)
            {
                dto.Lots.Add(new LumberYardGlassLot
                {
                    LotId = lot.LotId,
                    GlassPanes = lot.GlassPanes,
                    AcquiredDayIndex = lot.AcquiredDayIndex,
                    ImportOrderId = lot.ImportOrderId,
                    OriginName = lot.OriginName,
                    UnitCostCents = lot.UnitCostCents,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(LumberYardGlassStockSaveDto dto)
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
