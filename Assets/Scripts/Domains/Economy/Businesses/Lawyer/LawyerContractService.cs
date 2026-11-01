using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Lawyer
{
    /// <summary>W9B: contract lifecycle. Canon Part VI §6.1: conditional or final acceptance, obligations, performance, dispute, completion/termination.</summary>
    public enum DraftedContractStatus
    {
        Unspecified = 0,
        Draft = 1,      // being written; obligations may be added
        Executed = 2,   // both parties agreed; obligations live
        Completed = 3,  // all obligations performed (world-side, recorded here)
        Terminated = 4, // ended by agreement/breach mechanics (world-side, recorded here)
    }

    /// <summary>
    /// W9B: one contract obligation. Canon Part VI §6.2: obligations are smaller
    /// than agreements — each has its own obligor, due date, performance
    /// standard and consequences. The obligor must be one of the parties.
    /// </summary>
    [Serializable]
    public sealed class ContractObligation
    {
        public int ObligorPersonId = -1;
        public string ObligorName = string.Empty;
        public string ObligationText = string.Empty;
        public int DueDayIndex = -1; // -1 = no fixed date
        public string PerformanceStandard = string.Empty;

        public ContractObligation() { }
    }

    /// <summary>
    /// W9B: one lawyer-drafted contract between two REAL parties. The contract
    /// records what the parties agreed — it never invents obligations neither
    /// party took on. Performance happens in the world; this record only
    /// tracks execution/completion/termination as reported by the systems
    /// that actually perform or adjudicate.
    /// </summary>
    [Serializable]
    public sealed class LawyerDraftedContract
    {
        public string ContractId = string.Empty; // EntityKind.Contract
        public string MatterId = string.Empty;
        public int PartyAPersonId = -1;
        public string PartyAName = string.Empty;
        public int PartyBPersonId = -1;
        public string PartyBName = string.Empty;
        public string Title = string.Empty;
        public List<ContractObligation> Obligations = new List<ContractObligation>();
        public DraftedContractStatus Status = DraftedContractStatus.Draft;
        public int DraftedDayIndex;
        public int ExecutedDayIndex = -1;
        public int EffectiveDayIndex = -1;
        public int ClosedDayIndex = -1;
        public string CloseNote = string.Empty;

        public LawyerDraftedContract() { }
    }

    /// <summary>
    /// W9B: contract drafting producing real contract records between real
    /// parties. The lawyer drafts and records the agreement; they do not
    /// perform the obligations (the world does) and do not declare breach
    /// or termination by decree — completion/termination is reported through
    /// the dispute/agreement mechanics and only RECORDED here.
    /// </summary>
    public sealed class LawyerContractService
    {
        private readonly Dictionary<string, LawyerDraftedContract> contracts =
            new Dictionary<string, LawyerDraftedContract>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>Drafts a contract between two real, named, distinct persons for an open matter.</summary>
        public LawyerDraftedContract DraftContract(
            EntityIdRegistry ids, LawyerPracticeRuntime practice, string matterId,
            int partyAPersonId, string partyAName, int partyBPersonId, string partyBName,
            string title, int effectiveDayIndex, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (practice == null || practice.GetMatter(matterId) == null)
            {
                diag.Add("LawyerContractService.DraftContract: contracts are drafted only for open practice matters.");
                return null;
            }
            if (practice.GetMatter(matterId).Status != MatterStatus.Open)
            {
                diag.Add($"LawyerContractService.DraftContract: matter {matterId} is not open.");
                return null;
            }
            if (partyAPersonId < 0 || string.IsNullOrWhiteSpace(partyAName))
            {
                diag.Add("LawyerContractService.DraftContract: party A must be a real, named person.");
                return null;
            }
            if (partyBPersonId < 0 || string.IsNullOrWhiteSpace(partyBName))
            {
                diag.Add("LawyerContractService.DraftContract: party B must be a real, named person.");
                return null;
            }
            if (partyAPersonId == partyBPersonId)
            {
                diag.Add("LawyerContractService.DraftContract: a contract needs two distinct parties.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(title))
            {
                diag.Add("LawyerContractService.DraftContract: the contract must be titled.");
                return null;
            }

            var contract = new LawyerDraftedContract
            {
                ContractId = ids != null ? ids.Allocate(EntityKind.Contract).ToString() : Guid.NewGuid().ToString("N"),
                MatterId = matterId,
                PartyAPersonId = partyAPersonId,
                PartyAName = partyAName.Trim(),
                PartyBPersonId = partyBPersonId,
                PartyBName = partyBName.Trim(),
                Title = title.Trim(),
                Status = DraftedContractStatus.Draft,
                DraftedDayIndex = dayIndex,
                EffectiveDayIndex = effectiveDayIndex,
            };
            contracts[contract.ContractId] = contract;
            diag.Add($"LawyerContractService: contract {contract.ContractId} drafted — '{contract.Title}' between '{contract.PartyAName}' and '{contract.PartyBName}'.");
            return contract;
        }

        /// <summary>
        /// Adds an obligation to a draft contract. The obligor must be one of
        /// the two parties — a lawyer cannot obligate a stranger.
        /// </summary>
        public string AddObligation(
            string contractId, int obligorPersonId, string obligationText,
            int dueDayIndex, string performanceStandard, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!contracts.TryGetValue(contractId, out LawyerDraftedContract contract))
                return $"AddObligation: unknown contract '{contractId}'.";
            if (contract.Status != DraftedContractStatus.Draft)
                return $"AddObligation: contract '{contractId}' is {contract.Status} — obligations are written before execution.";
            if (obligorPersonId != contract.PartyAPersonId && obligorPersonId != contract.PartyBPersonId)
                return $"AddObligation: P{obligorPersonId} is not a party to contract '{contractId}' — the lawyer cannot obligate strangers.";
            if (string.IsNullOrWhiteSpace(obligationText))
                return "AddObligation: the obligation must be described.";

            contract.Obligations.Add(new ContractObligation
            {
                ObligorPersonId = obligorPersonId,
                ObligorName = obligorPersonId == contract.PartyAPersonId ? contract.PartyAName : contract.PartyBName,
                ObligationText = obligationText.Trim(),
                DueDayIndex = dueDayIndex,
                PerformanceStandard = performanceStandard ?? string.Empty,
            });
            diag.Add($"LawyerContractService: obligation added to contract {contractId} — P{obligorPersonId}: '{obligationText.Trim()}'.");
            return null;
        }

        /// <summary>Executes a draft contract: both parties have agreed. Requires at least one obligation (Canon §6.2).</summary>
        public string ExecuteContract(string contractId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!contracts.TryGetValue(contractId, out LawyerDraftedContract contract))
                return $"ExecuteContract: unknown contract '{contractId}'.";
            if (contract.Status != DraftedContractStatus.Draft)
                return $"ExecuteContract: contract '{contractId}' is {contract.Status}.";
            if (contract.Obligations.Count == 0)
                return $"ExecuteContract: contract '{contractId}' obligates nobody to do anything — an empty promise is not executed.";

            contract.Status = DraftedContractStatus.Executed;
            contract.ExecutedDayIndex = dayIndex;
            diag.Add($"LawyerContractService: contract {contractId} EXECUTED (day {dayIndex}) — {contract.Obligations.Count} obligation(s) live between '{contract.PartyAName}' and '{contract.PartyBName}'.");
            return null;
        }

        /// <summary>
        /// Records completion as reported by the systems that actually saw the
        /// performance. The lawyer records; the world decides.
        /// </summary>
        public string RecordCompletion(string contractId, string note, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!contracts.TryGetValue(contractId, out LawyerDraftedContract contract))
                return $"RecordCompletion: unknown contract '{contractId}'.";
            if (contract.Status != DraftedContractStatus.Executed)
                return $"RecordCompletion: contract '{contractId}' is {contract.Status} — only executed contracts complete.";

            contract.Status = DraftedContractStatus.Completed;
            contract.ClosedDayIndex = dayIndex;
            contract.CloseNote = note ?? string.Empty;
            diag.Add($"LawyerContractService: contract {contractId} recorded COMPLETE (day {dayIndex}): {contract.CloseNote}");
            return null;
        }

        /// <summary>
        /// Records termination as reported by the dispute/agreement mechanics
        /// (breach, settlement, repudiation — Canon §6.3). Never by decree here.
        /// </summary>
        public string RecordTermination(string contractId, string reason, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!contracts.TryGetValue(contractId, out LawyerDraftedContract contract))
                return $"RecordTermination: unknown contract '{contractId}'.";
            if (contract.Status != DraftedContractStatus.Executed && contract.Status != DraftedContractStatus.Draft)
                return $"RecordTermination: contract '{contractId}' is {contract.Status}.";
            if (string.IsNullOrWhiteSpace(reason))
                return $"RecordTermination: the terminating ground must be stated — the lawyer does not end contracts silently.";

            contract.Status = DraftedContractStatus.Terminated;
            contract.ClosedDayIndex = dayIndex;
            contract.CloseNote = reason.Trim();
            diag.Add($"LawyerContractService: contract {contractId} recorded TERMINATED (day {dayIndex}): {contract.CloseNote}");
            return null;
        }

        public LawyerDraftedContract GetContract(string contractId)
        {
            return contracts.TryGetValue(contractId, out LawyerDraftedContract c) ? c : null;
        }

        #region Save / Load

        [Serializable]
        public sealed class LawyerContractSaveDto
        {
            public List<LawyerDraftedContract> Contracts = new List<LawyerDraftedContract>();
        }

        public LawyerContractSaveDto CaptureSaveDto()
        {
            var dto = new LawyerContractSaveDto();
            foreach (var contract in contracts.Values) dto.Contracts.Add(contract);
            return dto;
        }

        public void LoadFromSaveDto(LawyerContractSaveDto dto)
        {
            contracts.Clear();
            if (dto == null) return;
            foreach (var contract in dto.Contracts)
            {
                if (contract == null) continue;
                contracts[contract.ContractId] = contract;
            }
        }

        #endregion
    }
}
