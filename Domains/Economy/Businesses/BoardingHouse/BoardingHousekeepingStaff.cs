using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.BoardingHouse
{
    /// <summary>
    /// D1F: one chambermaid (or chamber hand) on the housekeeping roster — a
    /// REAL PERSON (person id) with a Cleaning skill level on the 1-10 scale
    /// and a daily minute budget. Canon §8.1E: boarding houses "may need
    /// cooks, waiters, cleaners, clerks"; "cooking, cleaning, laundry, bed
    /// turnover ... all consume work." Chamber staff turn vacated rooms
    /// (strip beds, sweep, fresh bedding) so beds can be re-let.
    /// </summary>
    [Serializable]
    public sealed class BoardingHousekeeperAssignment
    {
        /// <summary>The chambermaid — a real person id, never anonymous.</summary>
        public int PersonId;

        /// <summary>Cleaning skill level, 1-10 (1 = untrained beginner). Drives turnover speed.</summary>
        public int CleaningSkillLevel = 1;

        /// <summary>Hands-on minutes this person gives housekeeping per day. Defaults to the house calibration.</summary>
        public int MinutesPerDay = BoardingHouseShopRuntime.HousekeeperMinutesPerDay;

        public int AssignedFromDayIndex;
        public bool IsActive = true;

        public BoardingHousekeeperAssignment() { }

        public bool IsOnDuty(int dayIndex)
        {
            return IsActive && PersonId > 0 && dayIndex >= AssignedFromDayIndex && MinutesPerDay > 0;
        }
    }

    /// <summary>
    /// D1F: the house's chamber-staff roster. Capacity is the SUM of on-duty
    /// staff minutes (Canon §8.1E scale transition: more rooms without more
    /// service hands buys dirty rooms); turnover runs faster under a skilled
    /// hand and slower under a green one. An empty roster means the
    /// proprietor and family do the chamber work (Canon §8.1E: "small houses
    /// can rely heavily on proprietor and family labor") — the fallback,
    /// behavior explicit. A roster whose staff have ALL left is different:
    /// nobody turns the rooms, and the house says so loudly (the
    /// housekeeper-loss case: "loss of a strong ... housekeeper ... can
    /// damage the establishment even when the building is undamaged").
    /// </summary>
    public sealed class BoardingHousekeepingStaff
    {
        private readonly List<BoardingHousekeeperAssignment> staff = new List<BoardingHousekeeperAssignment>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<BoardingHousekeeperAssignment> Staff => staff;

        /// <summary>True once the house has ever named chamber staff — separates "proprietor cleans" from "the staff all left".</summary>
        public bool HasRoster => staff.Count > 0;

        /// <summary>
        /// Names a chambermaid to the roster. Re-naming active staff updates
        /// their skill/minutes rather than duplicating them. Returns a
        /// rejection string, or null on success.
        /// </summary>
        public string AssignChambermaid(int personId, int skillLevel, int dayIndex, List<string> diag, int minutesPerDay = 0)
        {
            diag = diag ?? diagnostics;
            if (personId <= 0)
                return "BoardingHousekeepingStaff: chamber staff need a real person id — anonymous hands are not rostered.";
            if (dayIndex < 0)
                return "BoardingHousekeepingStaff: chamber staff need a real assignment day.";

            int clampedSkill = Math.Max(1, Math.Min(10, skillLevel));
            int minutes = minutesPerDay > 0 ? minutesPerDay : BoardingHouseShopRuntime.HousekeeperMinutesPerDay;

            foreach (BoardingHousekeeperAssignment existing in staff)
            {
                if (existing != null && existing.PersonId == personId && existing.IsActive)
                {
                    existing.CleaningSkillLevel = clampedSkill;
                    existing.MinutesPerDay = minutes;
                    existing.AssignedFromDayIndex = Math.Min(existing.AssignedFromDayIndex, dayIndex);
                    diag.Add($"BoardingHousekeepingStaff: chamber hand {personId} re-rostered (skill {clampedSkill}, {minutes}m/day).");
                    return null;
                }
            }

            staff.Add(new BoardingHousekeeperAssignment
            {
                PersonId = personId,
                CleaningSkillLevel = clampedSkill,
                MinutesPerDay = minutes,
                AssignedFromDayIndex = dayIndex,
                IsActive = true,
            });
            diag.Add($"BoardingHousekeepingStaff: chamber hand {personId} joined the roster (skill {clampedSkill}, {minutes}m/day from day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// A chamber hand leaves (or is let go). The assignment deactivates
        /// but stays on the books as history — the house remembers it had
        /// chamber staff. Returns a rejection string, or null on success.
        /// </summary>
        public string ReleaseChambermaid(int personId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            foreach (BoardingHousekeeperAssignment existing in staff)
            {
                if (existing != null && existing.PersonId == personId && existing.IsActive)
                {
                    existing.IsActive = false;
                    diag.Add($"BoardingHousekeepingStaff: chamber hand {personId} left the roster (day {dayIndex}) — " +
                        (HasActiveStaff(dayIndex)
                            ? "the house still has chamber hands."
                            : "NO CHAMBER STAFF remain: vacated rooms wait unturned until someone is named (Canon §8.1E)."));
                    return null;
                }
            }

            return $"BoardingHousekeepingStaff: no active chamber hand {personId} to release — the roster is unchanged.";
        }

        public bool HasActiveStaff(int dayIndex)
        {
            foreach (BoardingHousekeeperAssignment hand in staff)
            {
                if (hand != null && hand.IsOnDuty(dayIndex)) return true;
            }

            return false;
        }

        /// <summary>Total hands-on housekeeping minutes available today (the turnover labor budget).</summary>
        public int TotalMinutesToday(int dayIndex)
        {
            int total = 0;
            foreach (BoardingHousekeeperAssignment hand in staff)
            {
                if (hand != null && hand.IsOnDuty(dayIndex)) total += Math.Max(0, hand.MinutesPerDay);
            }

            return total;
        }

        /// <summary>The most skilled on-duty hand's level today; 0 when nobody is on duty.</summary>
        public int LeadingStaffSkill(int dayIndex)
        {
            int best = 0;
            foreach (BoardingHousekeeperAssignment hand in staff)
            {
                if (hand != null && hand.IsOnDuty(dayIndex))
                    best = Math.Max(best, Math.Max(1, Math.Min(10, hand.CleaningSkillLevel)));
            }

            return best;
        }

        /// <summary>
        /// TUNING: turnover speed by skill — a strong hand turns rooms
        /// faster, a green hand slower. Applied to the per-room turnover
        /// minutes (the same calibration shape as the restaurant's cook
        /// prep efficiency; not a canon claim).
        /// </summary>
        public static int EffectiveTurnoverMinutes(int baseTurnoverMinutes, int staffSkillLevel)
        {
            if (baseTurnoverMinutes <= 0) return 0;
            double factor = 1.0;
            if (staffSkillLevel >= 6) factor = 0.8;
            else if (staffSkillLevel >= 3) factor = 1.0;
            else if (staffSkillLevel > 0) factor = 1.25;
            return Math.Max(1, (int)Math.Ceiling(baseTurnoverMinutes * factor));
        }

        #region Save / Load
        [Serializable]
        public sealed class BoardingHousekeepingStaffSaveDto
        {
            public List<BoardingHousekeeperAssignment> Staff = new List<BoardingHousekeeperAssignment>();
        }

        public BoardingHousekeepingStaffSaveDto CaptureSaveDto()
        {
            var dto = new BoardingHousekeepingStaffSaveDto();
            foreach (BoardingHousekeeperAssignment hand in staff)
            {
                if (hand != null) dto.Staff.Add(hand);
            }

            return dto;
        }

        public void LoadFromSaveDto(BoardingHousekeepingStaffSaveDto dto)
        {
            staff.Clear();
            if (dto?.Staff == null) return;
            foreach (BoardingHousekeeperAssignment hand in dto.Staff)
            {
                if (hand != null) staff.Add(hand);
            }
        }
        #endregion
    }
}
