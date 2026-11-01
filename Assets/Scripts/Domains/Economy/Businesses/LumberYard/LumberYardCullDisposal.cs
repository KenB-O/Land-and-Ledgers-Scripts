using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.LumberYard
{
    /// <summary>D2C: why cull stock left the yard without being sold.</summary>
    public enum LumberYardCullDisposalReason
    {
        Burned = 0,        // refuse stock burned (fuel/heat or waste fire)
        Discarded = 1,     // hauled off / dumped as unusable
        GivenAway = 2,     // given to a named recipient (recorded, not sold)
    }

    /// <summary>
    /// D2C: one audited cull-disposal event. Canon §4.5: "Losses, discards and
    /// downgraded sales must remain visible in inventory and ledgers." Cull
    /// that is burned, dumped or given away leaves the physical stock but
    /// never silently: the units, the source lots and the reason are recorded
    /// here for the ledger to see. (Downgraded SALES are already visible —
    /// every sale dispense line carries its yard grade id.)
    /// </summary>
    [Serializable]
    public sealed class LumberYardCullDisposalRecord
    {
        public EntityId RecordId = EntityId.Invalid; // EntityKind.Contract
        public List<EntityId> SourceLotIds = new List<EntityId>();
        public int Units;
        public LumberYardCullDisposalReason Reason = LumberYardCullDisposalReason.Burned;
        public string Note = string.Empty;
        public int DayIndex;

        public LumberYardCullDisposalRecord() { }
    }

    /// <summary>
    /// D2C: the yard's cull-disposal register. Disposal touches ONLY
    /// cull-grade lots, FIFO — merchantable or clear stock is never burned
    /// through this path (a refusal says so loudly). Earmarked project stock
    /// is never disposed: reservations are promises. Every disposal is
    /// recorded; nothing about cull leaves the yard quietly.
    /// </summary>
    public sealed class LumberYardCullDisposalRegister
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<LumberYardCullDisposalRecord> records = new List<LumberYardCullDisposalRecord>();
        private readonly string yardBusinessId;

        public LumberYardCullDisposalRegister(string yardBusinessId)
        {
            this.yardBusinessId = yardBusinessId ?? string.Empty;
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<LumberYardCullDisposalRecord> Records => records;
        public string YardBusinessId => yardBusinessId;

        /// <summary>
        /// Disposes up to the requested cull units (burned, discarded, or
        /// given away). ATOMIC: on insufficient cull stock the call is refused
        /// loudly and nothing is disposed. Returns the refusal, or null (the
        /// record is appended to Records).
        /// </summary>
        public string DisposeCull(
            LumberYardLumberStock stock,
            int units,
            LumberYardCullDisposalReason reason,
            string note,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (stock == null) return "LumberYardCullDisposalRegister: no stock — nothing to dispose.";
            units = Math.Max(0, units);
            if (units == 0)
            {
                diag.Add("LumberYardCullDisposalRegister: nothing requested — no disposal recorded.");
                return null;
            }
            if (idRegistry == null)
                return "LumberYardCullDisposalRegister: no EntityIdRegistry — disposal refused.";

            int available = stock.DisposableCullUnits();
            if (available < units)
            {
                diag.Add($"LumberYardCullDisposalRegister ({yardBusinessId}): DISPOSAL REFUSED — "
                    + $"only {available} cull units available, {units} requested. "
                    + "Merchantable and clear stock is never burned through this path; earmarked stock is never touched.");
                return "LumberYardCullDisposalRegister: insufficient cull stock — disposal refused, nothing disposed.";
            }

            var taken = stock.DisposeCullUnits(units, diag);
            var record = new LumberYardCullDisposalRecord
            {
                RecordId = idRegistry.Allocate(EntityKind.Contract),
                Units = 0,
                Reason = reason,
                Note = note ?? string.Empty,
                DayIndex = dayIndex,
            };
            foreach (var line in taken)
            {
                if (line == null) continue;
                record.SourceLotIds.Add(line.LotId);
                record.Units += line.UnitsTaken;
            }
            records.Add(record);
            diag.Add($"LumberYardCullDisposalRegister ({yardBusinessId}): disposed {record.Units} cull units "
                + $"({reason}) on day {dayIndex} — record {record.RecordId}. "
                + "Losses stay visible in inventory and ledgers (Canon §4.5).");
            return null;
        }

        /// <summary>D2C save contract: lives inside the owning register class.</summary>
        [Serializable]
        public sealed class LumberYardCullDisposalRegisterSaveDto
        {
            public List<LumberYardCullDisposalRecord> Records = new List<LumberYardCullDisposalRecord>();
        }

        public LumberYardCullDisposalRegisterSaveDto CaptureSaveDto()
        {
            var dto = new LumberYardCullDisposalRegisterSaveDto();
            foreach (var r in records)
            {
                if (r == null) continue;
                var clone = new LumberYardCullDisposalRecord
                {
                    RecordId = r.RecordId,
                    Units = r.Units,
                    Reason = r.Reason,
                    Note = r.Note,
                    DayIndex = r.DayIndex,
                };
                foreach (var id in r.SourceLotIds) clone.SourceLotIds.Add(id);
                dto.Records.Add(clone);
            }
            return dto;
        }

        public void LoadFromSaveDto(LumberYardCullDisposalRegisterSaveDto dto)
        {
            records.Clear();
            if (dto == null || dto.Records == null) return;
            foreach (var r in dto.Records)
            {
                if (r == null) continue;
                records.Add(r);
            }
        }
    }
}
