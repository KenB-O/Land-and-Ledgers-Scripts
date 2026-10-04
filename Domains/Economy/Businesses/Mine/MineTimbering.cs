using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Mine
{
    /// <summary>
    /// W8D: one timbering record — feet of shaft timbered and the timber
    /// sets consumed, with the lumber lots' provenance chains attached.
    /// Timber sets come from real lumber lots (W4 sawmill/lumberyard);
    /// the caller withdraws from the lumber stock and passes the provenance
    /// here — this ledger never conjures timber.
    /// </summary>
    [Serializable]
    public sealed class MineTimberingRecord
    {
        [SerializeField]
        private string shaftId = string.Empty;

        [SerializeField, Min(0)]
        private int feetTimbered;

        [SerializeField, Min(0)]
        private int timberSetsConsumed;

        [SerializeField, Min(0)]
        private int dayIndex;

        [SerializeField]
        private List<string> lumberProvenanceChains = new List<string>();

        public string ShaftId => shaftId ?? string.Empty;
        public int FeetTimbered => Math.Max(0, feetTimbered);
        public int TimberSetsConsumed => Math.Max(0, timberSetsConsumed);
        public int DayIndex => Math.Max(0, dayIndex);
        public IReadOnlyList<string> LumberProvenanceChains => lumberProvenanceChains;

        public MineTimberingRecord() { }

        public MineTimberingRecord(string shaftId, int feetTimbered, int timberSetsConsumed, int dayIndex,
            List<string> lumberProvenanceChains)
        {
            this.shaftId = shaftId ?? string.Empty;
            this.feetTimbered = Math.Max(0, feetTimbered);
            this.timberSetsConsumed = Math.Max(0, timberSetsConsumed);
            this.dayIndex = Math.Max(0, dayIndex);
            if (lumberProvenanceChains != null)
                this.lumberProvenanceChains.AddRange(lumberProvenanceChains);
        }

        public MineTimberingRecordSaveDto CaptureSaveDto()
        {
            return new MineTimberingRecordSaveDto
            {
                shaftId = ShaftId,
                feetTimbered = FeetTimbered,
                timberSetsConsumed = TimberSetsConsumed,
                dayIndex = DayIndex,
                lumberProvenanceChains = new List<string>(lumberProvenanceChains),
            };
        }

        public static MineTimberingRecord FromSaveDto(MineTimberingRecordSaveDto dto)
        {
            if (dto == null)
                return null;
            return new MineTimberingRecord(dto.shaftId ?? string.Empty, dto.feetTimbered,
                dto.timberSetsConsumed, dto.dayIndex, dto.lumberProvenanceChains);
        }
    }

    /// <summary>
    /// W8D: timbering demand and consumption. Shaft supports consume lumber
    /// (W4 demand link): TimberSetsNeeded computes the open demand from the
    /// shaft plan; RecordTimbering books real consumption with lumber-lot
    /// provenance. Untimbered sunk depth is visible demand, never auto-filled.
    /// </summary>
    [Serializable]
    public sealed class MineTimberingLedger
    {
        [SerializeField]
        private List<MineTimberingRecord> records = new List<MineTimberingRecord>();

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<MineTimberingRecord> Records => records;

        public MineTimberingLedger() { }

        /// <summary>Timber sets needed to timber a shaft's currently untimbered sunk depth.</summary>
        public static int TimberSetsNeeded(MineShaft shaft)
        {
            if (shaft == null)
                return 0;
            int untimberedFeet = Math.Max(0, shaft.SunkDepthFeet - shaft.TimberedDepthFeet);
            return untimberedFeet * MineShaftTaskCatalog.TimberSetsPerSunkFoot;
        }

        /// <summary>Timber sets needed to drive planned feet of level/drive.</summary>
        public static int TimberSetsNeededForDrive(int feet)
        {
            return Math.Max(0, feet) * MineShaftTaskCatalog.TimberSetsPerDrivenFoot;
        }

        /// <summary>
        /// Books timber consumption against a shaft. The lumber must already
        /// have been withdrawn from a real lumber stock by the caller; its
        /// provenance chains are required (no anonymous timber). A mismatch
        /// with the expected sets-per-foot rate is diagnosed, not refused —
        /// the foreman's judgment on the ground stands.
        /// </summary>
        public MineTimberingRecord RecordTimbering(string shaftId, int feetTimbered, int timberSetsConsumed,
            int dayIndex, List<string> lumberProvenanceChains, List<string> callerDiagnostics)
        {
            callerDiagnostics = callerDiagnostics ?? new List<string>();
            if (string.IsNullOrWhiteSpace(shaftId))
            {
                callerDiagnostics.Add("MineTimberingLedger.RecordTimbering: shaft is required.");
                return null;
            }
            if (feetTimbered <= 0)
            {
                callerDiagnostics.Add("MineTimberingLedger.RecordTimbering: timbered feet must be positive.");
                return null;
            }
            if (timberSetsConsumed <= 0)
            {
                callerDiagnostics.Add("MineTimberingLedger.RecordTimbering: consumed timber sets must be positive — no phantom timber.");
                return null;
            }
            if (lumberProvenanceChains == null || lumberProvenanceChains.Count == 0)
            {
                callerDiagnostics.Add("MineTimberingLedger.RecordTimbering: lumber provenance is required — timber must trace to real lumber lots.");
                return null;
            }

            int expected = feetTimbered * MineShaftTaskCatalog.TimberSetsPerSunkFoot;
            if (timberSetsConsumed != expected)
            {
                callerDiagnostics.Add($"MineTimberingLedger.RecordTimbering: {timberSetsConsumed} sets for {feetTimbered} ft differs from the {expected}-set plan rate — recorded as worked.");
            }

            var record = new MineTimberingRecord(shaftId, feetTimbered, timberSetsConsumed, dayIndex, lumberProvenanceChains);
            records.Add(record);
            return record;
        }

        /// <summary>Total timber sets consumed across all records.</summary>
        public int TotalTimberSetsConsumed
        {
            get
            {
                int total = 0;
                foreach (MineTimberingRecord record in records)
                    total = Math.Max(0, total + record.TimberSetsConsumed);
                return total;
            }
        }

        public MineTimberingLedgerSaveDto CaptureSaveDto()
        {
            var dto = new MineTimberingLedgerSaveDto();
            foreach (MineTimberingRecord record in records)
                dto.records.Add(record.CaptureSaveDto());
            return dto;
        }

        public static MineTimberingLedger FromSaveDto(MineTimberingLedgerSaveDto dto)
        {
            var ledger = new MineTimberingLedger();
            if (dto != null)
            {
                foreach (MineTimberingRecordSaveDto recordDto in dto.records)
                {
                    MineTimberingRecord record = MineTimberingRecord.FromSaveDto(recordDto);
                    if (record != null)
                        ledger.records.Add(record);
                }
            }
            return ledger;
        }
    }

    /// <summary>W8D: save DTOs for timbering records. Owned by the mine runtime (standing rule).</summary>
    [Serializable]
    public sealed class MineTimberingRecordSaveDto
    {
        public string shaftId = string.Empty;
        public int feetTimbered;
        public int timberSetsConsumed;
        public int dayIndex;
        public List<string> lumberProvenanceChains = new List<string>();
    }

    [Serializable]
    public sealed class MineTimberingLedgerSaveDto
    {
        public List<MineTimberingRecordSaveDto> records = new List<MineTimberingRecordSaveDto>();
    }
}
