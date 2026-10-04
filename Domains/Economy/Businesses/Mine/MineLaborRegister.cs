using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Mine
{
    /// <summary>
    /// W8C: mine roles per Canon Part V occupation reference (prospector /
    /// placer miner, hard-rock miner, mine laborer / mucker, hoist operator,
    /// pump operator, ore / stamp-mill worker, assayer). No invented roles.
    /// </summary>
    public enum MineRole
    {
        Unspecified = 0,
        Prospector = 1,
        HardRockMiner = 2,
        Mucker = 3,
        HoistOperator = 4,
        PumpOperator = 5,
        OreMillWorker = 6,
        Assayer = 7,
    }

    /// <summary>W8C: shift structure as scheduling data (day / night). No invented consequences.</summary>
    public enum MineShift
    {
        Unspecified = 0,
        Day = 1,
        Night = 2,
    }

    /// <summary>
    /// W8C: one miner's crew assignment. The employment itself lives on
    /// EmploymentRelationship (PKG-6 authority); this assignment is the
    /// mine's roster view — role, shift, and the per-shift wage the mine
    /// actually pays. Wages move only through HouseholdLedger authorities.
    /// </summary>
    [Serializable]
    public sealed class MineCrewAssignment
    {
        [SerializeField]
        private EntityId employmentId = EntityId.Invalid;

        [SerializeField]
        private EntityId personId = EntityId.Invalid;

        [SerializeField]
        private MineRole role = MineRole.Unspecified;

        [SerializeField]
        private MineShift shift = MineShift.Unspecified;

        [SerializeField, Min(0)]
        private int wagePerShiftCents;

        [SerializeField, Min(0)]
        private int startDayIndex;

        /// <summary>-1 while active; otherwise the day the assignment ended.</summary>
        [SerializeField]
        private int endDayIndex = -1;

        public EntityId EmploymentId => employmentId;
        public EntityId PersonId => personId;
        public MineRole Role => role;
        public MineShift Shift => shift;
        public int WagePerShiftCents => Math.Max(0, wagePerShiftCents);
        public int StartDayIndex => Math.Max(0, startDayIndex);
        public int EndDayIndex => endDayIndex;
        public bool IsActive => endDayIndex < 0;

        public MineCrewAssignment() { }

        public MineCrewAssignment(EntityId employmentId, EntityId personId, MineRole role,
            MineShift shift, int wagePerShiftCents, int startDayIndex)
        {
            this.employmentId = employmentId;
            this.personId = personId;
            this.role = role;
            this.shift = shift;
            this.wagePerShiftCents = Math.Max(0, wagePerShiftCents);
            this.startDayIndex = Math.Max(0, startDayIndex);
        }

        public void End(int dayIndex)
        {
            endDayIndex = Math.Max(StartDayIndex, dayIndex);
        }

        public MineCrewAssignmentSaveDto CaptureSaveDto()
        {
            return new MineCrewAssignmentSaveDto
            {
                employmentKind = (int)employmentId.Kind,
                employmentSeq = employmentId.Id,
                personKind = (int)personId.Kind,
                personSeq = personId.Id,
                role = role,
                shift = shift,
                wagePerShiftCents = WagePerShiftCents,
                startDayIndex = StartDayIndex,
                endDayIndex = endDayIndex,
            };
        }

        public static MineCrewAssignment FromSaveDto(MineCrewAssignmentSaveDto dto)
        {
            if (dto == null)
                return null;
            var assignment = new MineCrewAssignment(
                new EntityId { Kind = (EntityKind)dto.employmentKind, Id = dto.employmentSeq },
                new EntityId { Kind = (EntityKind)dto.personKind, Id = dto.personSeq },
                dto.role, dto.shift, dto.wagePerShiftCents, dto.startDayIndex);
            assignment.endDayIndex = dto.endDayIndex;
            return assignment;
        }
    }

    /// <summary>
    /// W8C: the mine's crew roster. Miners are hired through the existing
    /// RecruitmentService (T2B channels — Canon §6.2); the roster records
    /// role, shift, and per-shift wage against real EmploymentRelationship
    /// ids. DANGER / ATTRITION / INJURY / DEATH: the Canon names no mine
    /// accident mechanics, rates, or consequences, so none are modeled here
    /// (genuine design fork — recorded in the W8 report). The existing
    /// MineRuntimeState.safetyRisk01 remains the read-only support signal.
    /// </summary>
    [Serializable]
    public sealed class MineLaborRegister
    {
        [SerializeField]
        private List<MineCrewAssignment> assignments = new List<MineCrewAssignment>();

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<MineCrewAssignment> Assignments => assignments;

        public MineLaborRegister() { }

        /// <summary>Canon Part V role display names for recruitment efforts.</summary>
        public static string GetRoleDisplayName(MineRole role)
        {
            return role switch
            {
                MineRole.Prospector => "Prospector",
                MineRole.HardRockMiner => "Hard-rock miner",
                MineRole.Mucker => "Mucker (mine laborer)",
                MineRole.HoistOperator => "Hoist operator",
                MineRole.PumpOperator => "Pump operator",
                MineRole.OreMillWorker => "Ore / stamp-mill worker",
                MineRole.Assayer => "Assayer",
                _ => "Mine worker",
            };
        }

        /// <summary>
        /// Opens a hiring effort for a mine role through the existing
        /// recruitment channels (T2B). Returns the effort, or null with a
        /// diagnostic when the channel refuses.
        /// </summary>
        public RecruitmentEffort OpenHiringEffort(RecruitmentService recruitment,
            string businessInstanceId, MineRole role, RecruitmentChannel channel, int dayIndex, int seed,
            List<string> callerDiagnostics)
        {
            callerDiagnostics = callerDiagnostics ?? new List<string>();
            if (recruitment == null)
            {
                callerDiagnostics.Add("MineLaborRegister.OpenHiringEffort: RecruitmentService is required.");
                return null;
            }
            if (role == MineRole.Unspecified)
            {
                callerDiagnostics.Add("MineLaborRegister.OpenHiringEffort: a real mine role is required.");
                return null;
            }
            return recruitment.OpenEffort(businessInstanceId, GetRoleDisplayName(role), channel, dayIndex, seed, callerDiagnostics);
        }

        /// <summary>
        /// Puts a hired miner on the roster. The employment must be a real
        /// EmploymentRelationship id (PKG-6); the roster never invents one.
        /// </summary>
        public MineCrewAssignment AssignMiner(EntityId employmentId, EntityId personId, MineRole role,
            MineShift shift, int wagePerShiftCents, int startDayIndex, List<string> callerDiagnostics)
        {
            callerDiagnostics = callerDiagnostics ?? new List<string>();
            if (employmentId.Equals(EntityId.Invalid) || employmentId.Kind != EntityKind.EmploymentRelationship)
            {
                callerDiagnostics.Add("MineLaborRegister.AssignMiner: a real EmploymentRelationship id is required — the roster never invents employment.");
                return null;
            }
            if (personId.Equals(EntityId.Invalid) || personId.Kind != EntityKind.Person)
            {
                callerDiagnostics.Add("MineLaborRegister.AssignMiner: a real person is required.");
                return null;
            }
            if (role == MineRole.Unspecified)
            {
                callerDiagnostics.Add("MineLaborRegister.AssignMiner: a real mine role is required.");
                return null;
            }
            if (shift == MineShift.Unspecified)
            {
                callerDiagnostics.Add("MineLaborRegister.AssignMiner: a shift (day/night) is required.");
                return null;
            }
            if (wagePerShiftCents <= 0)
            {
                callerDiagnostics.Add("MineLaborRegister.AssignMiner: per-shift wage must be positive.");
                return null;
            }
            foreach (MineCrewAssignment existing in assignments)
            {
                if (existing.IsActive && existing.PersonId.Equals(personId))
                {
                    callerDiagnostics.Add($"MineLaborRegister.AssignMiner: P{personId.Id} already holds an active crew assignment.");
                    return null;
                }
            }

            var assignment = new MineCrewAssignment(employmentId, personId, role, shift, wagePerShiftCents, startDayIndex);
            assignments.Add(assignment);
            return assignment;
        }

        /// <summary>Ends a miner's crew assignment (history retained, per EmploymentRelationship doctrine).</summary>
        public string ReleaseMiner(EntityId personId, int dayIndex)
        {
            foreach (MineCrewAssignment assignment in assignments)
            {
                if (assignment.IsActive && assignment.PersonId.Equals(personId))
                {
                    assignment.End(dayIndex);
                    return null;
                }
            }
            return $"MineLaborRegister.ReleaseMiner: no active crew assignment for P{personId.Id}.";
        }

        public List<MineCrewAssignment> ActiveAssignments()
        {
            var active = new List<MineCrewAssignment>();
            foreach (MineCrewAssignment assignment in assignments)
            {
                if (assignment.IsActive)
                    active.Add(assignment);
            }
            return active;
        }

        public int ActiveCountByRole(MineRole role)
        {
            int count = 0;
            foreach (MineCrewAssignment assignment in assignments)
            {
                if (assignment.IsActive && assignment.Role == role)
                    count++;
            }
            return count;
        }

        /// <summary>Total per-shift payroll for the active crew (cents).</summary>
        public int ShiftWageBillCents()
        {
            int total = 0;
            foreach (MineCrewAssignment assignment in assignments)
            {
                if (assignment.IsActive)
                    total = Math.Max(0, total + assignment.WagePerShiftCents);
            }
            return total;
        }

        /// <summary>
        /// Pays one shift's wages for the active crew. The mine's operating
        /// cash records an outflow per miner (purpose required); each
        /// worker's household ledger records the wage inflow through the
        /// EmploymentRelationship id (PKG-6 provenance). When the household
        /// lookup is unavailable, the worker-side inflow is skipped loudly —
        /// the employment relationship remains the provenance link.
        /// Returns the number of miners paid.
        /// </summary>
        public int SettleShiftWages(HouseholdLedger businessCash,
            Func<EntityId, HouseholdLedger> workerHouseholdLookup,
            int dayIndex, string periodLabel, List<string> callerDiagnostics)
        {
            callerDiagnostics = callerDiagnostics ?? new List<string>();
            if (businessCash == null)
            {
                callerDiagnostics.Add("MineLaborRegister.SettleShiftWages: the mine's operating cash ledger is required.");
                return 0;
            }
            int paid = 0;
            foreach (MineCrewAssignment assignment in assignments)
            {
                if (!assignment.IsActive)
                    continue;
                string employmentRef = assignment.EmploymentId.ToString();
                string rejection = businessCash.RecordOutflow(dayIndex, assignment.WagePerShiftCents,
                    $"miner wages {periodLabel}: {GetRoleDisplayName(assignment.Role)} P{assignment.PersonId.Id} via {employmentRef}",
                    "miner payroll");
                if (rejection != null)
                {
                    callerDiagnostics.Add($"MineLaborRegister.SettleShiftWages: P{assignment.PersonId.Id} not paid — {rejection}");
                    continue;
                }
                if (workerHouseholdLookup != null)
                {
                    HouseholdLedger workerLedger = workerHouseholdLookup(assignment.PersonId);
                    if (workerLedger != null)
                    {
                        string wageRejection = workerLedger.RecordWagePayment(dayIndex, employmentRef,
                            assignment.PersonId.Id, assignment.WagePerShiftCents, periodLabel);
                        if (wageRejection != null)
                            callerDiagnostics.Add($"MineLaborRegister.SettleShiftWages: worker inflow failed for P{assignment.PersonId.Id} — {wageRejection}");
                    }
                    else
                    {
                        callerDiagnostics.Add($"MineLaborRegister.SettleShiftWages: no household ledger for P{assignment.PersonId.Id} — worker inflow skipped, employment {employmentRef} is the provenance link.");
                    }
                }
                else
                {
                    callerDiagnostics.Add($"MineLaborRegister.SettleShiftWages: no household lookup — worker inflow for P{assignment.PersonId.Id} skipped, employment {employmentRef} is the provenance link.");
                }
                paid++;
            }
            return paid;
        }

        /// <summary>W8C: mining labor as task-system data. Crew tasks cite the crew's roles.</summary>
        public static class MineLaborTaskCatalog
        {
            public const string BreakOreTaskId = "mine.break-ore";
            public const string MuckOreTaskId = "mine.muck-ore";

            /// <summary>TUNING (calibration): work-minutes to break one ton of ore.</summary>
            public const int BreakMinutesPerTon = 120;

            /// <summary>TUNING (calibration): work-minutes to muck/load one ton of ore.</summary>
            public const int MuckMinutesPerTon = 90;

            public static void Register(TaskAuthority authority, List<string> diagnostics)
            {
                diagnostics = diagnostics ?? new List<string>();
                if (authority == null)
                {
                    diagnostics.Add("MineLaborTaskCatalog.Register: TaskAuthority is required.");
                    return;
                }
                string ignored;

                var breakOre = new TaskDefinition(BreakOreTaskId, "Break ore (per ton)", BreakMinutesPerTon);
                breakOre.SetRequiredSkill(MineShaftTaskCatalog.MiningSkillId, new[] { MineShaftTaskCatalog.MiningSkillId });
                breakOre.SetDefaultPriority(TaskPriority.Normal);
                breakOre.DomainTags.Add("mining");
                breakOre.DomainTags.Add("ore-breaking");
                breakOre.RequiredCapabilityTags.Add("mine-breaking");
                breakOre.EquipmentClasses.Add(
                    LandLedgers.Economy.Equipment.EquipmentRequirementCodes.Kit(MineShaftTaskCatalog.MinerHandKitId));
                authority.RegisterDefinition(breakOre, out ignored);

                var muck = new TaskDefinition(MuckOreTaskId, "Muck and load ore (per ton)", MuckMinutesPerTon);
                muck.SetRequiredSkill(MineShaftTaskCatalog.MiningSkillId, new[] { MineShaftTaskCatalog.MiningSkillId });
                muck.SetDefaultPriority(TaskPriority.Normal);
                muck.DomainTags.Add("mining");
                muck.DomainTags.Add("ore-mucking");
                muck.RequiredCapabilityTags.Add("mine-mucking");
                muck.EquipmentClasses.Add(
                    LandLedgers.Economy.Equipment.EquipmentRequirementCodes.Kit(MineShaftTaskCatalog.MinerHandKitId));
                authority.RegisterDefinition(muck, out ignored);
            }
        }

        public MineLaborRegisterSaveDto CaptureSaveDto()
        {
            var dto = new MineLaborRegisterSaveDto();
            foreach (MineCrewAssignment assignment in assignments)
                dto.assignments.Add(assignment.CaptureSaveDto());
            return dto;
        }

        public static MineLaborRegister FromSaveDto(MineLaborRegisterSaveDto dto)
        {
            var register = new MineLaborRegister();
            if (dto != null)
            {
                foreach (MineCrewAssignmentSaveDto assignmentDto in dto.assignments)
                {
                    MineCrewAssignment assignment = MineCrewAssignment.FromSaveDto(assignmentDto);
                    if (assignment != null)
                        register.assignments.Add(assignment);
                }
            }
            return register;
        }
    }

    /// <summary>W8C: save DTOs for the crew roster. Owned by the mine runtime (standing rule).</summary>
    [Serializable]
    public sealed class MineCrewAssignmentSaveDto
    {
        public int employmentKind;
        public int employmentSeq;
        public int personKind;
        public int personSeq;
        public MineRole role;
        public MineShift shift;
        public int wagePerShiftCents;
        public int startDayIndex;
        public int endDayIndex = -1;
    }

    [Serializable]
    public sealed class MineLaborRegisterSaveDto
    {
        public List<MineCrewAssignmentSaveDto> assignments = new List<MineCrewAssignmentSaveDto>();
    }
}
