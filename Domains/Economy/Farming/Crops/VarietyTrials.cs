using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Crops
{
    /// <summary>
    /// D2G: the recorded outcome of a variety trial, as DATA. A trial is a
    /// researcher's or farmer's recorded observation that a variety performed
    /// a certain way in a certain place over a certain period. It is history,
    /// not calibration: recording a trial NEVER touches yield numbers or
    /// formulas (the W5C recorded fork — variety agronomic effects are
    /// data-only).
    /// </summary>
    public enum VarietyTrialOutcome
    {
        /// <summary>No outcome recorded — a trial record must state one.</summary>
        Unrecorded = 0,

        /// <summary>Recorded as performing well in this place/period.</summary>
        Thrived = 1,

        /// <summary>Recorded as performing adequately.</summary>
        Adequate = 2,

        /// <summary>Recorded as performing poorly.</summary>
        Poor = 3,

        /// <summary>Recorded with mixed or inconsistent results.</summary>
        Mixed = 4,
    }

    /// <summary>
    /// D2G: one recorded variety trial — which variety, where, when, and what
    /// the record says. The note carries the historical source (e.g. the W5C
    /// research pass) so the record is auditable rather than asserted.
    /// </summary>
    [Serializable]
    public sealed class VarietyTrialRecord
    {
        public EntityId RecordId = EntityId.Invalid; // EntityKind.Lot (HF-1), record id
        public string VarietyId = string.Empty;
        public string LocationName = string.Empty;   // e.g. "Otonabee Township, Peterborough County, Upper Canada"
        public string PeriodNote = string.Empty;     // e.g. "1842–early 1900s"
        public VarietyTrialOutcome Outcome = VarietyTrialOutcome.Unrecorded;
        public string Note = string.Empty;           // historical source / context
        public int RecordedDayIndex = -1;

        public VarietyTrialRecord() { }
    }

    /// <summary>
    /// D2G: the variety trial book — recorded trial results as data. Trials
    /// are registered explicitly (by a scenario author or a research pass),
    /// never auto-generated: an unrecorded performance is an honest gap, not a
    /// default. Every record names a registered variety (the
    /// CropVarietyCatalog's originator rule applies — no anonymous varieties)
    /// and a real place. Genuine design fork, recorded here and never guessed
    /// into the numbers: trial methodology and any yield/hardiness effect of
    /// variety performance.
    /// </summary>
    [Serializable]
    public sealed class VarietyTrialBook
    {
        private readonly List<VarietyTrialRecord> records = new List<VarietyTrialRecord>();

        public VarietyTrialBook() { }

        public IReadOnlyList<VarietyTrialRecord> Records => records;

        /// <summary>
        /// Creates a book pre-seeded with the one research-backed datum the
        /// W5C pass established: Red Fife's dominance in Canada from the
        /// mid-1800s to the early 1900s (per CropVarietyCatalog's OriginNote).
        /// Explicitly recorded, not defaulted.
        /// </summary>
        public static VarietyTrialBook CreateWithResearchDatum(
            EntityIdRegistry idRegistry, int dayIndex, List<string> diagnostics)
        {
            var book = new VarietyTrialBook();
            book.RecordTrial(
                idRegistry,
                "red-fife",
                "Otonabee Township, Peterborough County, Upper Canada (and across Canada)",
                "1842–early 1900s",
                VarietyTrialOutcome.Thrived,
                "W5C research pass: the dominant Canadian spring wheat from the mid-1800s to the early 1900s; " +
                "first grown 1842 by David Fife. Recorded dominance is the trial datum — the agronomic reasons " +
                "are a design fork, not calibration.",
                dayIndex,
                diagnostics);
            return book;
        }

        public VarietyTrialRecord FindRecord(EntityId recordId)
        {
            foreach (var record in records)
            {
                if (record != null && record.RecordId.Equals(recordId)) return record;
            }
            return null;
        }

        public List<VarietyTrialRecord> TrialsForVariety(string varietyId)
        {
            var result = new List<VarietyTrialRecord>();
            if (string.IsNullOrWhiteSpace(varietyId)) return result;
            foreach (var record in records)
            {
                if (record != null && string.Equals(record.VarietyId, varietyId, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(record);
                }
            }
            return result;
        }

        /// <summary>
        /// Records a trial. Refuses loudly when the variety is not registered,
        /// the place is unnamed, or no outcome is stated — a trial without a
        /// variety, a place, and a verdict is not a record.
        /// </summary>
        public VarietyTrialRecord RecordTrial(
            EntityIdRegistry idRegistry,
            string varietyId,
            string locationName,
            string periodNote,
            VarietyTrialOutcome outcome,
            string note,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (idRegistry == null)
            {
                diagnostics.Add("VarietyTrialBook: recording needs an id registry.");
                return null;
            }
            if (CropVarietyCatalog.Get(varietyId) == null)
            {
                diagnostics.Add($"VarietyTrialBook: variety '{varietyId}' is not registered — trials attach to catalogued varieties, never invented ones.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(locationName))
            {
                diagnostics.Add("VarietyTrialBook: a trial must name where it was observed.");
                return null;
            }
            if (outcome == VarietyTrialOutcome.Unrecorded)
            {
                diagnostics.Add("VarietyTrialBook: a trial must state its recorded outcome.");
                return null;
            }

            var record = new VarietyTrialRecord
            {
                RecordId = idRegistry.Allocate(EntityKind.Lot),
                VarietyId = varietyId,
                LocationName = locationName,
                PeriodNote = periodNote ?? string.Empty,
                Outcome = outcome,
                Note = note ?? string.Empty,
                RecordedDayIndex = dayIndex,
            };
            records.Add(record);
            diagnostics.Add(
                $"VarietyTrialBook: recorded {CropVarietyCatalog.DisplayNameOf(varietyId)} at {locationName} — {outcome} ({record.PeriodNote}).");
            return record;
        }

        /// <summary>Save support (CLN-1 pattern): DTO lives inside the owning book class.</summary>
        public VarietyTrialBookSaveDto CaptureSaveDto()
        {
            return new VarietyTrialBookSaveDto
            {
                records = new List<VarietyTrialRecord>(records),
            };
        }

        /// <summary>Save support (CLN-1 pattern).</summary>
        public void LoadFromSaveDto(VarietyTrialBookSaveDto dto)
        {
            records.Clear();
            if (dto == null) return;
            if (dto.records != null)
            {
                foreach (var record in dto.records)
                {
                    if (record != null) records.Add(record);
                }
            }
        }

        /// <summary>Save DTO for the trial book (CLN-1 pattern).</summary>
        [Serializable]
        public sealed class VarietyTrialBookSaveDto
        {
            public List<VarietyTrialRecord> records = new List<VarietyTrialRecord>();
        }
    }
}
