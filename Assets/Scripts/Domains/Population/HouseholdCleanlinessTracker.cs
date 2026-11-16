using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Campaign C, package 24 — DEF-010 (domestic labour: 3 of 4 need triggers
    /// hard-coded off), owner Decision 6 (Nov 15, 2026): cleaning comes from
    /// accumulated household use, occupancy and cleanliness state.
    ///
    /// No cleanliness state existed anywhere in the repo (repo-wide grep for
    /// cleanliness / daysSinceClean / lastClean: zero production hits), so
    /// this is the new measurable need state. One record per household:
    /// use accumulates one unit per occupant present per day, and a
    /// household needs cleaning once its accumulated use reaches the
    /// threshold. Completing a Chores task resets the accumulation — the
    /// need is then re-earned by further use, never by a timer alone.
    ///
    /// The threshold is a named, documented calibration value (Decision 1
    /// precedent: starting values are calibration, not permanent universal
    /// rules). 14 person-days means a 2-person household cleans about
    /// weekly, a 1-person household about fortnightly, and a 7-person
    /// household about every two days — use scales with occupancy, as
    /// Decision 6 requires. It is a schedule parameter, not a consumption
    /// quantity: no fuel, food, or cash is consumed by cleaning.
    ///
    /// The driver (DomesticLaborDriver) owns one instance and persists its
    /// records through DomesticLaborDriverSaveDto (clear-then-load, same as
    /// the driver's pending/in-flight state). Accumulation is idempotent
    /// per household per day, so a repeated DriveDay for the same day can
    /// never double-count use.
    /// </summary>
    [Serializable]
    public sealed class HouseholdCleanlinessRecord
    {
        public int HouseholdId;
        public int AccumulatedUseUnits;
        public int LastAccumulatedDayIndex = -1;
        public int LastCleanedDayIndex = -1;
    }

    public sealed class HouseholdCleanlinessTracker
    {
        /// <summary>
        /// Person-days of accumulated household use that trigger a cleaning
        /// need. Provisional calibration (see the class doc): 14 person-days.
        /// </summary>
        public const int CleaningUseThresholdUnits = 14;

        private readonly List<HouseholdCleanlinessRecord> records = new List<HouseholdCleanlinessRecord>();

        public IReadOnlyList<HouseholdCleanlinessRecord> Records => records;

        /// <summary>
        /// Adds one use unit per occupant present for this household-day.
        /// Idempotent per household per day: a second call for the same day
        /// adds nothing.
        /// </summary>
        public void Accumulate(int householdId, int membersAtHome, int dayIndex)
        {
            if (householdId < 0 || membersAtHome <= 0)
            {
                return;
            }

            HouseholdCleanlinessRecord record = GetOrCreate(householdId);
            if (record.LastAccumulatedDayIndex == dayIndex)
            {
                return;
            }

            record.AccumulatedUseUnits = Math.Max(0, record.AccumulatedUseUnits) + membersAtHome;
            record.LastAccumulatedDayIndex = dayIndex;
        }

        /// <summary>True once the household's accumulated use reaches the threshold.</summary>
        public bool NeedsCleaning(int householdId)
        {
            HouseholdCleanlinessRecord record = Find(householdId);
            return record != null && record.AccumulatedUseUnits >= CleaningUseThresholdUnits;
        }

        /// <summary>Accumulated use units for a household (0 when untracked).</summary>
        public int AccumulatedUseUnits(int householdId)
        {
            HouseholdCleanlinessRecord record = Find(householdId);
            return record != null ? Math.Max(0, record.AccumulatedUseUnits) : 0;
        }

        /// <summary>
        /// A Chores task completed: the household is clean again. The
        /// accumulation resets to zero; the need is re-earned by further use.
        /// A household with no record is already clean — nothing to reset.
        /// </summary>
        public void ResetAfterCleaning(int householdId, int dayIndex)
        {
            HouseholdCleanlinessRecord record = Find(householdId);
            if (record == null)
            {
                return;
            }

            record.AccumulatedUseUnits = 0;
            record.LastCleanedDayIndex = dayIndex;
        }

        /// <summary>
        /// Save: deep copies, so later accumulation never mutates a captured DTO.
        /// </summary>
        public List<HouseholdCleanlinessRecord> CaptureRecords()
        {
            var copy = new List<HouseholdCleanlinessRecord>(records.Count);
            foreach (HouseholdCleanlinessRecord record in records)
            {
                if (record == null)
                {
                    continue;
                }

                copy.Add(new HouseholdCleanlinessRecord
                {
                    HouseholdId = record.HouseholdId,
                    AccumulatedUseUnits = record.AccumulatedUseUnits,
                    LastAccumulatedDayIndex = record.LastAccumulatedDayIndex,
                    LastCleanedDayIndex = record.LastCleanedDayIndex,
                });
            }

            return copy;
        }

        /// <summary>Load: clear-then-load; incoming records are copied, never aliased.</summary>
        public void LoadRecords(IEnumerable<HouseholdCleanlinessRecord> incoming)
        {
            records.Clear();
            if (incoming == null)
            {
                return;
            }

            foreach (HouseholdCleanlinessRecord record in incoming)
            {
                if (record == null)
                {
                    continue;
                }

                records.Add(new HouseholdCleanlinessRecord
                {
                    HouseholdId = record.HouseholdId,
                    AccumulatedUseUnits = record.AccumulatedUseUnits,
                    LastAccumulatedDayIndex = record.LastAccumulatedDayIndex,
                    LastCleanedDayIndex = record.LastCleanedDayIndex,
                });
            }
        }

        private HouseholdCleanlinessRecord Find(int householdId)
        {
            foreach (HouseholdCleanlinessRecord record in records)
            {
                if (record != null && record.HouseholdId == householdId)
                {
                    return record;
                }
            }

            return null;
        }

        private HouseholdCleanlinessRecord GetOrCreate(int householdId)
        {
            HouseholdCleanlinessRecord record = Find(householdId);
            if (record != null)
            {
                return record;
            }

            record = new HouseholdCleanlinessRecord { HouseholdId = householdId };
            records.Add(record);
            return record;
        }
    }
}
