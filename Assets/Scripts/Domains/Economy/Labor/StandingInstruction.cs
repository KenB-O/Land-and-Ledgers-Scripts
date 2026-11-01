// Worker D: Autonomous Labor, Phase G - standing instructions.
// Canon CE1-13.2 instruction semantics:
//   Do Once          - one specified action, then consumed. The manager may
//                      execute the named act exactly one time; after execution
//                      the instruction is spent and never fires again.
//   Manager Judgment - an ongoing objective the manager pursues with
//                      managerial discretion, inside explicit bounds. The
//                      manager chooses HOW (timing, vendor, quantity) but never
//                      leaves the bounds.
//   Owner Locked     - a persistent prohibition or strategic boundary. It is
//                      NOT overridable by the manager under any circumstance;
//                      only a new owner authorization (a new instruction or a
//                      revocation by the owner) changes it. Violations are
//                      refused and escalated, never executed.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Labor
{
    /// <summary>Canon CE1-13.2 instruction kinds.</summary>
    public enum InstructionKind
    {
        /// <summary>One specified action, then consumed.</summary>
        DoOnce = 0,
        /// <summary>Ongoing objective with managerial discretion, inside bounds.</summary>
        ManagerJudgment = 1,
        /// <summary>Persistent prohibition/strategic boundary; not manager-overridable.</summary>
        OwnerLocked = 2
    }

    /// <summary>
    /// One standing instruction from an owner to a delegated manager. Plain
    /// data - the ManagerBrain interprets it; this type carries no authority
    /// of its own.
    /// </summary>
    [Serializable]
    public sealed class StandingInstruction
    {
        public string InstructionId = string.Empty;
        public string BusinessInstanceId = string.Empty;
        public InstructionKind Kind = InstructionKind.ManagerJudgment;

        /// <summary>
        /// Well-known act kinds, e.g. "OpenRecruitment", "Procure", "Schedule",
        /// "PriceChange", "OrderMaintenance", "Borrow", "SellAsset". Compared
        /// ordinally; empty means the instruction carries no act targeting.
        /// </summary>
        public string TargetActKind = string.Empty;

        /// <summary>Human-readable objective or prohibition text (canon 13.2).</summary>
        public string Objective = string.Empty;

        /// <summary>
        /// Maximum spend authorized per act, in cents. Negative = no cap from
        /// this instruction (other bounds may still apply). Zero = this
        /// instruction authorizes no spending at all.
        /// </summary>
        public int MaxSpendCentsPerAct = -1;

        /// <summary>
        /// Act kinds this instruction prohibits (ordinal strings). Used by
        /// OwnerLocked; also honored when set on other kinds.
        /// </summary>
        public List<string> ProhibitedActs = new List<string>();

        /// <summary>
        /// Maximum borrowing the owner permits, in cents. Zero = no borrowing
        /// authorized. The manager can NEVER exceed this - attempts escalate.
        /// </summary>
        public int MaxBorrowingCents;

        public int CreatedByPersonId = -1;
        public int CreatedDayIndex = -1;

        /// <summary>DoOnce only: set true the moment the specified action executes.</summary>
        public bool Consumed;

        public int RevokedDayIndex = -1; // -1 = not revoked
        public string RevokeNote = string.Empty;

        public bool IsRevoked => RevokedDayIndex >= 0;

        /// <summary>
        /// True when this instruction targets the act and may still fire
        /// (not revoked; DoOnce not yet consumed).
        /// </summary>
        public bool TargetsAct(string actKind)
        {
            if (IsRevoked || string.IsNullOrWhiteSpace(actKind))
            {
                return false;
            }

            if (Kind == InstructionKind.DoOnce && Consumed)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(TargetActKind)
                && string.Equals(TargetActKind, actKind, StringComparison.Ordinal))
            {
                return true;
            }

            return ProhibitedActs != null && ProhibitedActs.Contains(actKind);
        }
    }

    /// <summary>
    /// Save shape for a standing-instruction set. Round-trips verbatim - owner
    /// restrictions are never erased on load.
    /// </summary>
    [Serializable]
    public sealed class StandingInstructionSaveDto
    {
        public List<StandingInstruction> Instructions = new List<StandingInstruction>();
    }

    /// <summary>
    /// Register of standing instructions for the businesses a manager serves.
    /// First-wins on instruction id; consumption is sticky.
    /// </summary>
    public sealed class StandingInstructionRegister
    {
        private readonly Dictionary<string, StandingInstruction> byId =
            new Dictionary<string, StandingInstruction>(StringComparer.Ordinal);

        public int Count => byId.Count;

        /// <summary>Registers an instruction. Duplicate ids are rejected (first wins).</summary>
        public string Register(StandingInstruction instruction, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (instruction == null || string.IsNullOrWhiteSpace(instruction.InstructionId))
            {
                return "StandingInstructionRegister: an instruction id is required.";
            }

            if (byId.ContainsKey(instruction.InstructionId))
            {
                diag.Add($"StandingInstructionRegister: duplicate instruction '{instruction.InstructionId}' rejected - first registration wins.");
                return $"duplicate instruction '{instruction.InstructionId}'";
            }

            byId[instruction.InstructionId] = instruction;
            return null;
        }

        public bool TryGet(string instructionId, out StandingInstruction instruction)
        {
            instruction = null;
            return !string.IsNullOrWhiteSpace(instructionId) && byId.TryGetValue(instructionId, out instruction);
        }

        /// <summary>All non-revoked instructions for a business.</summary>
        public List<StandingInstruction> ForBusiness(string businessInstanceId)
        {
            var results = new List<StandingInstruction>();
            foreach (StandingInstruction instruction in byId.Values)
            {
                if (instruction != null && !instruction.IsRevoked
                    && string.Equals(instruction.BusinessInstanceId, businessInstanceId, StringComparison.Ordinal))
                {
                    results.Add(instruction);
                }
            }

            return results;
        }

        /// <summary>
        /// Marks a DoOnce instruction consumed after its action executes.
        /// Returns an error when the instruction is not a live DoOnce.
        /// </summary>
        public string ConsumeDoOnce(string instructionId, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (!TryGet(instructionId, out StandingInstruction instruction) || instruction == null)
            {
                return $"StandingInstructionRegister.ConsumeDoOnce: unknown instruction '{instructionId}'.";
            }

            if (instruction.Kind != InstructionKind.DoOnce)
            {
                return $"StandingInstructionRegister.ConsumeDoOnce: '{instructionId}' is {instruction.Kind}, not DoOnce.";
            }

            if (instruction.Consumed)
            {
                return $"StandingInstructionRegister.ConsumeDoOnce: '{instructionId}' is already consumed - a DoOnce fires exactly once.";
            }

            instruction.Consumed = true;
            diag.Add($"StandingInstructionRegister: DoOnce '{instructionId}' consumed on day {dayIndex}.");
            return null;
        }

        /// <summary>
        /// True when any live OwnerLocked instruction prohibits the act for
        /// the business. Only the owner (a new instruction / revocation) can
        /// change this - the manager may never override it.
        /// </summary>
        public bool IsActProhibited(string businessInstanceId, string actKind)
        {
            if (string.IsNullOrWhiteSpace(actKind))
            {
                return false;
            }

            foreach (StandingInstruction instruction in ForBusiness(businessInstanceId))
            {
                if (instruction.Kind == InstructionKind.OwnerLocked && instruction.TargetsAct(actKind))
                {
                    return true;
                }
            }

            return false;
        }

        public StandingInstructionSaveDto CaptureSaveDto()
        {
            var dto = new StandingInstructionSaveDto();
            foreach (StandingInstruction instruction in byId.Values)
            {
                if (instruction != null)
                {
                    dto.Instructions.Add(instruction);
                }
            }

            return dto;
        }

        /// <summary>Clear-then-load: the set is replaced, never merged.</summary>
        public void LoadFromSaveDto(StandingInstructionSaveDto dto)
        {
            byId.Clear();
            if (dto == null || dto.Instructions == null)
            {
                return;
            }

            foreach (StandingInstruction instruction in dto.Instructions)
            {
                if (instruction != null && !string.IsNullOrWhiteSpace(instruction.InstructionId))
                {
                    byId[instruction.InstructionId] = instruction;
                }
            }
        }
    }
}
