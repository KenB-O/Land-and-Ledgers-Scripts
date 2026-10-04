using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.Hotel
{
    /// <summary>
    /// D2A: the named service roles a hotel's payroll can carry. Canon
    /// §8.1E: larger hotels "may need cooks, waiters, cleaners, clerks or
    /// other helpers"; the Canon labor table names the "Porter / hotel
    /// general hand" outright (hand cart/luggage equipment, cleaning kit,
    /// lamps, keys, basic maintenance hand tools). "Chambermaid" is the
    /// period name for the canon's "cleaners"; the night clerk is desk
    /// coverage after hours — the canon's "clerks" on the night shift.
    /// The desk clerk itself (W3A) is the proprietor when no clerk is
    /// named — Canon §8.1E proprietor dependence.
    /// </summary>
    public enum HotelStaffRole
    {
        /// <summary>Front-desk coverage by day — guest handling, bookkeeping, the register.</summary>
        DeskClerk = 0,
        /// <summary>Front-desk coverage overnight — late arrivals, the night watch, locking up.</summary>
        NightClerk = 1,
        /// <summary>Canon "Porter / hotel general hand": luggage, lamps, keys, errands, heavy lifting.</summary>
        Porter = 2,
        /// <summary>Canon §8.1E "cleaners": chamber work, bed turnover, laundry.</summary>
        Chambermaid = 3,
        /// <summary>Canon §8.1E "cooks": the dining room and its range.</summary>
        Cook = 4,
        /// <summary>Canon §8.1E "waiters": the dining room floor.</summary>
        Waiter = 5,
    }

    /// <summary>
    /// D2A: one named service hand on the hotel roster — a REAL PERSON
    /// (person id), never anonymous. Skill drives service speed and
    /// quality (1-10 scale, the same calibration shape as the
    /// boarding-house chamber staff); the daily minute budget is the
    /// labor the caller can draw on for that role's work.
    /// </summary>
    [Serializable]
    public sealed class HotelStaffAssignment
    {
        /// <summary>The service hand — a real person id, never anonymous.</summary>
        public int PersonId;

        public HotelStaffRole Role = HotelStaffRole.Porter;

        /// <summary>Service skill level, 1-10 (1 = untrained beginner). Drives work speed.</summary>
        public int SkillLevel = 1;

        /// <summary>Hands-on minutes this person gives the role per day.</summary>
        public int MinutesPerDay;

        public int AssignedFromDayIndex;
        public bool IsActive = true;

        public HotelStaffAssignment() { }

        public bool IsOnDuty(int dayIndex)
        {
            return IsActive && PersonId > 0 && dayIndex >= AssignedFromDayIndex && MinutesPerDay > 0;
        }
    }

    /// <summary>
    /// D2A: the hotel's service-staff roster. Labor is the scale
    /// transition Canon §8.1E names: adding rooms without adding service
    /// hands buys dirty rooms and cold meals, not more revenue. Roles are
    /// queried separately (chambermaids turn beds, porters move luggage,
    /// cooks/waiters run the dining room, clerks keep the desk) — the
    /// runtime hands minutes to each subsystem from the role's pool.
    ///
    /// An empty roster is the Canon §8.1E small-house case: the proprietor
    /// and family do the service work (explicit fallback minutes supplied
    /// by the caller, never conjured here). A roster whose staff have ALL
    /// left is different: the desk goes unkept, and the house says so
    /// loudly (the "loss of a strong ... proprietor" continuity case).
    /// </summary>
    public sealed class HotelStaff
    {
        // TUNING: default hands-on minutes per role per day (Canon Part
        // XV calibration; proprietor policy per house via AssignStaff).
        public const int DeskClerkMinutesPerDay = 600;
        public const int NightClerkMinutesPerDay = 300;
        public const int PorterMinutesPerDay = 540;
        public const int ChambermaidMinutesPerDay = 480;
        public const int CookMinutesPerDay = 600;
        public const int WaiterMinutesPerDay = 540;

        private readonly List<string> diagnostics = new List<string>();
        private readonly List<HotelStaffAssignment> staff = new List<HotelStaffAssignment>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<HotelStaffAssignment> Staff => staff;

        /// <summary>True once the hotel has ever named staff — separates "proprietor works the house" from "the staff all left".</summary>
        public bool HasRoster => staff.Count > 0;

        /// <summary>
        /// Names a service hand to the roster (or re-rosters an active one).
        /// Returns a rejection string, or null on success.
        /// </summary>
        public string AssignStaff(int personId, HotelStaffRole role, int skillLevel, int dayIndex,
            List<string> diag, int minutesPerDay = 0)
        {
            diag = diag ?? diagnostics;
            if (personId <= 0)
                return "HotelStaff: service staff need a real person id — anonymous hands are not rostered.";
            if (dayIndex < 0)
                return "HotelStaff: staff need a real assignment day.";

            int clampedSkill = Math.Max(1, Math.Min(10, skillLevel));
            int minutes = minutesPerDay > 0 ? minutesPerDay : DefaultMinutesForRole(role);

            foreach (HotelStaffAssignment existing in staff)
            {
                if (existing != null && existing.PersonId == personId && existing.Role == role && existing.IsActive)
                {
                    existing.SkillLevel = clampedSkill;
                    existing.MinutesPerDay = minutes;
                    existing.AssignedFromDayIndex = Math.Min(existing.AssignedFromDayIndex, dayIndex);
                    diag.Add($"HotelStaff: {role} {personId} re-rostered (skill {clampedSkill}, {minutes}m/day).");
                    return null;
                }
            }

            staff.Add(new HotelStaffAssignment
            {
                PersonId = personId,
                Role = role,
                SkillLevel = clampedSkill,
                MinutesPerDay = minutes,
                AssignedFromDayIndex = dayIndex,
                IsActive = true,
            });
            diag.Add($"HotelStaff: {role} {personId} joined the roster (skill {clampedSkill}, {minutes}m/day from day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// A service hand leaves (or is let go). The assignment deactivates
        /// but stays on the books as history. Returns a rejection string,
        /// or null on success.
        /// </summary>
        public string ReleaseStaff(int personId, HotelStaffRole role, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            foreach (HotelStaffAssignment existing in staff)
            {
                if (existing != null && existing.PersonId == personId && existing.Role == role && existing.IsActive)
                {
                    existing.IsActive = false;
                    bool anyLeft = HasActiveRole(role, dayIndex);
                    diag.Add($"HotelStaff: {role} {personId} left the roster (day {dayIndex}) — " +
                        (anyLeft
                            ? $"the house still has {role} coverage."
                            : $"NO {role} REMAINS: the proprietor must cover it, or the work goes undone (Canon §8.1E)."));
                    return null;
                }
            }

            return $"HotelStaff: no active {role} {personId} to release — the roster is unchanged.";
        }

        /// <summary>True when at least one hand of the role is on duty today.</summary>
        public bool HasActiveRole(HotelStaffRole role, int dayIndex)
        {
            foreach (HotelStaffAssignment hand in staff)
            {
                if (hand != null && hand.Role == role && hand.IsOnDuty(dayIndex)) return true;
            }

            return false;
        }

        /// <summary>Total hands-on minutes available today for one role (the subsystem labor budget).</summary>
        public int TotalMinutesToday(HotelStaffRole role, int dayIndex)
        {
            int total = 0;
            foreach (HotelStaffAssignment hand in staff)
            {
                if (hand != null && hand.Role == role && hand.IsOnDuty(dayIndex))
                    total += Math.Max(0, hand.MinutesPerDay);
            }

            return total;
        }

        /// <summary>The most skilled on-duty hand's level for the role today; 0 when nobody is on duty.</summary>
        public int LeadingSkill(HotelStaffRole role, int dayIndex)
        {
            int best = 0;
            foreach (HotelStaffAssignment hand in staff)
            {
                if (hand != null && hand.Role == role && hand.IsOnDuty(dayIndex))
                    best = Math.Max(best, Math.Max(1, Math.Min(10, hand.SkillLevel)));
            }

            return best;
        }

        /// <summary>
        /// TUNING: work speed by skill — the same calibration shape as the
        /// boarding-house chamber staff (a strong hand turns rooms faster,
        /// a green one slower). Not a canon claim; proprietor-editable
        /// only by replacing this method's calibration.
        /// </summary>
        public static int EffectiveWorkMinutes(int baseWorkMinutes, int staffSkillLevel)
        {
            if (baseWorkMinutes <= 0) return 0;
            double factor = 1.0;
            if (staffSkillLevel >= 6) factor = 0.8;
            else if (staffSkillLevel >= 3) factor = 1.0;
            else if (staffSkillLevel > 0) factor = 1.25;
            return Math.Max(1, (int)Math.Ceiling(baseWorkMinutes * factor));
        }

        public static int DefaultMinutesForRole(HotelStaffRole role)
        {
            switch (role)
            {
                case HotelStaffRole.DeskClerk: return DeskClerkMinutesPerDay;
                case HotelStaffRole.NightClerk: return NightClerkMinutesPerDay;
                case HotelStaffRole.Porter: return PorterMinutesPerDay;
                case HotelStaffRole.Chambermaid: return ChambermaidMinutesPerDay;
                case HotelStaffRole.Cook: return CookMinutesPerDay;
                case HotelStaffRole.Waiter: return WaiterMinutesPerDay;
                default: return 480;
            }
        }

        #region Save / Load
        [Serializable]
        public sealed class HotelStaffSaveDto
        {
            public List<HotelStaffAssignment> Staff = new List<HotelStaffAssignment>();
        }

        public HotelStaffSaveDto CaptureSaveDto()
        {
            var dto = new HotelStaffSaveDto();
            foreach (HotelStaffAssignment hand in staff)
            {
                if (hand != null) dto.Staff.Add(hand);
            }

            return dto;
        }

        public void LoadFromSaveDto(HotelStaffSaveDto dto)
        {
            staff.Clear();
            if (dto?.Staff == null) return;
            foreach (HotelStaffAssignment hand in dto.Staff)
            {
                if (hand == null) continue;
                staff.Add(hand);
            }
        }
        #endregion
    }
}
