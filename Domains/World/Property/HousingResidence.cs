using System;
using System.Collections.Generic;
using LandLedgers.Population;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// Phase D (Real People): an explicit condition for a Person who holds NO
    /// current <see cref="ResidentialOccupancy"/>. Canon §12.6I: "No stable
    /// shelter is a pressure condition, not a permanent population state."
    /// A Person either has a valid current occupancy (with a real arrangement
    /// — owner-occupied, rental, boarding, kin hosting, temporary camp,
    /// precarious, ...) OR an explicit record here. Append-only.
    /// </summary>
    public enum ResidentialConditionKind
    {
        SuitablyHoused = 0,      // has a current occupancy that meets the household requirement
        TemporaryCondition = 1,  // transient / unsheltered but expected to resolve (search in motion)
        PrecariousCondition = 2, // no occupancy and no reliable fallback (eviction, displacement)
        Unsheltered = 3,          // no stable shelter — explicit, never implied
    }

    /// <summary>
    /// Phase D: one Person's explicit residential condition. Created only for
    /// persons with no current occupancy; cleared (not deleted) when an
    /// occupancy is established, so the history of unsheltered episodes stays
    /// in the record. Person != Residential Occupancy — this is the
    /// accommodation-side fact for people with no occupancy record at all.
    /// </summary>
    [Serializable]
    public sealed class ResidentialConditionRecord
    {
        public string RecordId = string.Empty;
        public int PersonId = -1;
        public int HouseholdId = -1;
        public ResidentialConditionKind Condition = ResidentialConditionKind.Unsheltered;
        public int SinceDayIndex;
        public int UntilDayIndex = -1; // -1 = current
        public int LastEvaluatedDayIndex;
        public int NextSearchDayIndex = -1; // pressure condition: the day search pressure re-triggers
        public string Reason = string.Empty;
        public string Notes = string.Empty;

        public bool IsCurrent => UntilDayIndex < 0;

        public ResidentialConditionRecord() { }
    }

    /// <summary>
    /// Phase D: the registry guaranteeing every Person has either a valid
    /// current <see cref="ResidentialOccupancy"/> or an explicit condition
    /// record. Accommodation states are arrangements, never tiers
    /// (Canon §12.6I).
    /// </summary>
    public sealed class ResidentialConditionRegistry
    {
        private readonly Dictionary<int, ResidentialConditionRecord> currentByPerson =
            new Dictionary<int, ResidentialConditionRecord>();
        private readonly List<ResidentialConditionRecord> history = new List<ResidentialConditionRecord>();
        private readonly List<string> diagnostics = new List<string>();
        private int sequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<ResidentialConditionRecord> History => history;

        /// <summary>
        /// Phase D: reconciles one Person's accommodation side. If the
        /// housing authority shows a current occupancy for the person, any
        /// open condition record is closed (the person is housed). Otherwise
        /// an explicit condition record is created or refreshed — a person
        /// is NEVER silently unhoused.
        /// </summary>
        public ResidentialConditionRecord EnsurePersonCondition(
            int personId, int householdId, HousingAuthority housing,
            int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (personId < 0)
            {
                diag.Add("ResidentialConditionRegistry: a condition needs a real person id.");
                return null;
            }

            bool hasCurrentOccupancy = housing != null
                && housing.CurrentOccupanciesForPerson(personId).Count > 0;

            if (hasCurrentOccupancy)
            {
                CloseCurrentRecord(personId, dayIndex, diag);
                return null;
            }

            if (currentByPerson.TryGetValue(personId, out ResidentialConditionRecord existing)
                && existing != null && existing.IsCurrent)
            {
                existing.LastEvaluatedDayIndex = dayIndex;
                existing.HouseholdId = householdId;
                return existing;
            }

            var record = new ResidentialConditionRecord
            {
                RecordId = $"rcond-{sequence++}",
                PersonId = personId,
                HouseholdId = householdId,
                Condition = ResidentialConditionKind.Unsheltered,
                SinceDayIndex = dayIndex,
                LastEvaluatedDayIndex = dayIndex,
                NextSearchDayIndex = dayIndex,
                Reason = $"no current residential occupancy for P{personId} — explicit unsheltered condition recorded (Canon 12.6I).",
            };
            currentByPerson[personId] = record;
            history.Add(record);
            diag.Add($"ResidentialConditionRegistry: P{personId} (H{householdId}) has no current occupancy — unsheltered condition '{record.RecordId}' opened.");
            return record;
        }

        /// <summary>
        /// Phase D: escalates or de-escalates an open condition (e.g. a
        /// temporary condition becoming precarious after eviction).
        /// </summary>
        public string SetConditionKind(int personId, ResidentialConditionKind kind, string reason, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!currentByPerson.TryGetValue(personId, out ResidentialConditionRecord record)
                || record == null || !record.IsCurrent)
            {
                return $"ResidentialConditionRegistry.SetConditionKind: P{personId} has no open condition record.";
            }

            record.Condition = kind;
            record.LastEvaluatedDayIndex = dayIndex;
            if (!string.IsNullOrWhiteSpace(reason)) record.Notes = reason;
            diag.Add($"ResidentialConditionRegistry: P{personId} condition now {kind} — {reason}.");
            return null;
        }

        public ResidentialConditionRecord CurrentConditionFor(int personId)
        {
            return currentByPerson.TryGetValue(personId, out ResidentialConditionRecord record)
                && record != null && record.IsCurrent ? record : null;
        }

        private void CloseCurrentRecord(int personId, int dayIndex, List<string> diag)
        {
            if (currentByPerson.TryGetValue(personId, out ResidentialConditionRecord record)
                && record != null && record.IsCurrent)
            {
                record.UntilDayIndex = dayIndex;
                record.LastEvaluatedDayIndex = dayIndex;
                currentByPerson.Remove(personId);
                diag.Add($"ResidentialConditionRegistry: P{personId} condition '{record.RecordId}' closed — a current occupancy now exists.");
            }
        }

        #region Save / Load
        [Serializable]
        public sealed class ResidentialConditionRegistrySaveDto
        {
            public List<ResidentialConditionRecord> History = new List<ResidentialConditionRecord>();
        }

        public ResidentialConditionRegistrySaveDto CaptureSaveDto()
        {
            var dto = new ResidentialConditionRegistrySaveDto();
            foreach (ResidentialConditionRecord record in history)
            {
                if (record != null) dto.History.Add(record);
            }
            return dto;
        }

        public void LoadFromSaveDto(ResidentialConditionRegistrySaveDto dto)
        {
            currentByPerson.Clear();
            history.Clear();
            if (dto?.History == null) return;
            foreach (ResidentialConditionRecord record in dto.History)
            {
                if (record == null || record.PersonId < 0) continue;
                history.Add(record);
                if (record.IsCurrent && !currentByPerson.ContainsKey(record.PersonId))
                    currentByPerson[record.PersonId] = record;
                // A save with two open records for one person is corrupt data —
                // keep the first, quarantine the rest as closed history.
                else if (record.IsCurrent)
                {
                    record.UntilDayIndex = Math.Max(0, record.LastEvaluatedDayIndex);
                    diagnostics.Add($"LoadFromSaveDto: duplicate open condition '{record.RecordId}' for P{record.PersonId} quarantined as closed.");
                }
            }
        }
        #endregion
    }

    /// <summary>
    /// Phase D: a household's shelter requirement derived from its actual
    /// composition. TUNING (Canon sets no sleeping-place arithmetic): each
    /// adult needs one sleeping place; children under 18 may share, two to a
    /// place. A vacant bunk does NOT satisfy a family — requirements scale
    /// with household composition, not with having a HouseholdId.
    /// </summary>
    [Serializable]
    public sealed class HouseholdCompositionSummary
    {
        public int HouseholdId;
        public int AdultCount;
        public int ChildCount;
        public int TotalPersons => AdultCount + ChildCount;
        /// <summary>TUNING: adults need one place each; children may share two to a place.</summary>
        public int RequiredSleepingPlaces => AdultCount + (ChildCount + 1) / 2;
    }

    /// <summary>
    /// Phase D: the suitability verdict for one household. An EVALUATION,
    /// never a refusal: Canon §12.7 gives households freedom with consequence
    /// rather than "Cannot Move In because capacity is exceeded", so an
    /// overcrowded household is recorded as overcrowded, not blocked.
    /// </summary>
    [Serializable]
    public sealed class HouseholdShelterSuitability
    {
        public int HouseholdId;
        public HouseholdCompositionSummary Composition = new HouseholdCompositionSummary();
        public int ProvidedSleepingPlaces;
        public int GapPlaces => Math.Max(0, Composition.RequiredSleepingPlaces - ProvidedSleepingPlaces);
        public bool IsSuitable => GapPlaces <= 0;
        public List<int> UncoveredPersonIds = new List<int>();
        public List<string> SpaceIdsEvaluated = new List<string>();
        public string Summary = string.Empty;
    }

    /// <summary>
    /// Phase D: evaluates whether a household's CURRENT occupancies provide
    /// enough sleeping places for its composition. Suitability is
    /// actor-specific (Canon §12.6I) and suitability != having an
    /// arrangement: a family in one bunk is housed but UNSUITABLY housed.
    /// </summary>
    public sealed class HouseholdShelterSuitabilityEvaluator
    {
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public HouseholdCompositionSummary SummarizeComposition(int householdId, IReadOnlyList<PersonState> members)
        {
            var summary = new HouseholdCompositionSummary { HouseholdId = householdId };
            if (members == null) return summary;
            foreach (PersonState member in members)
            {
                if (member == null || member.deathDayIndex >= 0) continue;
                if (member.age >= 18) summary.AdultCount++;
                else summary.ChildCount++;
            }
            return summary;
        }

        /// <summary>
        /// Sums the sleeping capacity of every space the household's members
        /// currently occupy and compares it to the composition requirement.
        /// Persons with NO current occupancy are listed as uncovered.
        /// </summary>
        public HouseholdShelterSuitability Evaluate(
            int householdId,
            IReadOnlyList<PersonState> members,
            HousingAuthority housing,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            var verdict = new HouseholdShelterSuitability { HouseholdId = householdId };
            verdict.Composition = SummarizeComposition(householdId, members);
            if (housing == null)
            {
                verdict.Summary = "no housing authority — suitability unevaluated.";
                return verdict;
            }

            var evaluatedSpaces = new HashSet<string>(StringComparer.Ordinal);
            if (members != null)
            {
                foreach (PersonState member in members)
                {
                    if (member == null || member.deathDayIndex >= 0) continue;
                    bool hasOccupancy = false;
                    foreach (ResidentialOccupancy occ in housing.CurrentOccupanciesForPerson(member.id))
                    {
                        hasOccupancy = true;
                        AccommodationSpace space = housing.FindSpace(occ.SpaceId);
                        if (space != null)
                        {
                            evaluatedSpaces.Add(space.SpaceId);
                        }
                    }
                    if (!hasOccupancy) verdict.UncoveredPersonIds.Add(member.id);
                }
            }

            int provided = 0;
            foreach (string spaceId in evaluatedSpaces)
            {
                AccommodationSpace space = housing.FindSpace(spaceId);
                if (space != null)
                {
                    provided += Math.Max(0, space.SleepingCapacity);
                    verdict.SpaceIdsEvaluated.Add(spaceId);
                }
            }
            verdict.ProvidedSleepingPlaces = provided;
            verdict.Summary = verdict.IsSuitable
                ? $"H{householdId}: {provided} sleeping places for {verdict.Composition.RequiredSleepingPlaces} required — suitably housed."
                : $"H{householdId}: {provided} sleeping places for {verdict.Composition.RequiredSleepingPlaces} required — SHORT by {verdict.GapPlaces} (a vacant bunk does not house a family).";
            diag.Add(verdict.Summary);
            return verdict;
        }
    }
}
