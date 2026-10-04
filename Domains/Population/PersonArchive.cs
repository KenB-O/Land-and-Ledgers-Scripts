using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using UnityEngine;

namespace LandLedgers.Population
{
    /// <summary>
    /// T2H (PL-27): one archived person. Departed persons are archived, NOT
    /// deleted — employment, kinship, and property references survive through
    /// these records. Mirrors the HF-2 HistoricalAnimalRecord pattern.
    /// </summary>
    [Serializable]
    public sealed class HistoricalPersonRecord
    {
        public int PersonId = -1;
        public string FullName = string.Empty;
        public int AgeAtDeath;
        public int DeathDayIndex = -1;
        public int FinalHouseholdId = -1;
        public string DispositionSummary = string.Empty;
        /// <summary>Employment relationship ids (PKG-6 registry) — preserved, not orphaned.</summary>
        public List<string> EmploymentReferenceIds = new List<string>();
        /// <summary>Property/parcel references held at death (T2F).</summary>
        public List<string> PropertyReferenceIds = new List<string>();
        public int ArchivedDayIndex = -1;

        public HistoricalPersonRecord() { }
    }

    /// <summary>
    /// T2H (PL-27): the person archive. When a person dies (deathDayIndex set),
    /// they move here with their references intact. The living population never
    /// reuses the id — the allocator only moves forward.
    /// </summary>
    public sealed class PersonArchive
    {
        private readonly Dictionary<int, HistoricalPersonRecord> historical =
            new Dictionary<int, HistoricalPersonRecord>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public int HistoricalCount => historical.Count;

        public HistoricalPersonRecord GetHistorical(int personId)
        {
            return historical.TryGetValue(personId, out HistoricalPersonRecord record) ? record : null;
        }

        /// <summary>
        /// T2H: archives the departed. Returns the archived count.
        /// </summary>
        public int ArchiveDeparted(
            PopulationState population,
            EmploymentRelationshipRegistry employment,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (population == null) return 0;
            var toArchive = new List<PersonState>();
            foreach (PersonState p in population.people)
            {
                if (p != null && p.deathDayIndex >= 0 && p.deathDayIndex <= dayIndex && !historical.ContainsKey(p.id))
                    toArchive.Add(p);
            }
            foreach (PersonState p in toArchive)
            {
                var record = new HistoricalPersonRecord
                {
                    PersonId = p.id,
                    FullName = $"{p.firstName} {p.lastName}".Trim(),
                    AgeAtDeath = p.age,
                    DeathDayIndex = p.deathDayIndex,
                    FinalHouseholdId = p.householdId,
                    DispositionSummary = $"died day {p.deathDayIndex}",
                    ArchivedDayIndex = dayIndex,
                };
                if (employment != null)
                {
                    foreach (EmploymentRelationship rel in employment.GetByEmployee(p.id))
                    {
                        if (!string.IsNullOrWhiteSpace(rel.Id))
                            record.EmploymentReferenceIds.Add(rel.Id);
                    }
                }
                historical[p.id] = record;
                population.people.Remove(p);
                diag.Add($"PersonArchive: P{p.id} ({record.FullName}) archived — {record.EmploymentReferenceIds.Count} employment references preserved.");
            }
            return toArchive.Count;
        }

        #region Save / Load
        [Serializable]
        public sealed class PersonArchiveSaveDto
        {
            public List<HistoricalPersonRecord> Records = new List<HistoricalPersonRecord>();
        }

        public PersonArchiveSaveDto CaptureSaveDto()
        {
            return new PersonArchiveSaveDto { Records = new List<HistoricalPersonRecord>(historical.Values) };
        }

        public void LoadFromSaveDto(PersonArchiveSaveDto dto)
        {
            historical.Clear();
            if (dto == null) return;
            foreach (var record in dto.Records) historical[record.PersonId] = record;
        }
        #endregion
    }
}
