using System;
using System.Collections.Generic;
using LandLedgers.Economy.Estates;
using LandLedgers.Population;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Lawyer
{
    /// <summary>W9B: the practice's ledger entry for one will it drafted.</summary>
    [Serializable]
    public sealed class DraftedWillEntry
    {
        public string WillId = string.Empty;
        public string MatterId = string.Empty;
        public int TestatorPersonId = -1;
        public string TestatorName = string.Empty;
        public int DraftedDayIndex;

        public DraftedWillEntry() { }
    }

    /// <summary>
    /// W9B: will drafting that produces Will records valid for the NX-3C
    /// probate flow. The lawyer DRAFTS; the NX-3C ProbateService VALIDATES —
    /// witnesses, executor eligibility, parcel ownership are the probate
    /// service's findings, never the lawyer's assurances. This service keeps
    /// only a drafting ledger (which wills this practice wrote, for which
    /// matter) and forwards every validation question to probate.
    /// </summary>
    public sealed class LawyerWillDraftingService
    {
        private readonly Dictionary<string, DraftedWillEntry> draftedWills =
            new Dictionary<string, DraftedWillEntry>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Drafts a will by recording it with the probate service. If probate
        /// rejects (bad testator, fewer than two witnesses, self-witnessing),
        /// nothing is drafted — the rejection IS the validation.
        /// </summary>
        public Will DraftWill(
            EntityIdRegistry ids, LawyerPracticeRuntime practice, string matterId,
            ProbateService probate, int testatorPersonId, string testatorName,
            int executorPersonId, List<int> witnessPersonIds,
            int signedDayIndex, PopulationState population, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (practice == null || practice.GetMatter(matterId) == null ||
                practice.GetMatter(matterId).Status != MatterStatus.Open)
            {
                diag.Add("LawyerWillDraftingService.DraftWill: wills are drafted only for open practice matters.");
                return null;
            }
            if (probate == null)
            {
                diag.Add("LawyerWillDraftingService.DraftWill: no probate service — the lawyer does not validate wills alone.");
                return null;
            }

            Will will = probate.RecordWill(ids, testatorPersonId, testatorName,
                executorPersonId, witnessPersonIds, signedDayIndex, population, diag);
            if (will == null)
            {
                diag.Add("LawyerWillDraftingService.DraftWill: probate rejected the will — nothing drafted.");
                return null;
            }

            draftedWills[will.WillId] = new DraftedWillEntry
            {
                WillId = will.WillId,
                MatterId = matterId,
                TestatorPersonId = testatorPersonId,
                TestatorName = testatorName ?? string.Empty,
                DraftedDayIndex = signedDayIndex,
            };
            diag.Add($"LawyerWillDraftingService: will {will.WillId} drafted for '{testatorName}' — probate holds it; the practice holds the drafting ledger entry.");
            return will;
        }

        /// <summary>
        /// Adds a bequest to a will this practice drafted, through probate.
        /// The beneficiary must be a real person (probate checks); the parcel
        /// check happens at probate time, not here.
        /// </summary>
        public string AddBequest(
            LawyerPracticeRuntime practice, ProbateService probate,
            string willId, Bequest bequest, PopulationState population, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!draftedWills.TryGetValue(willId, out DraftedWillEntry entry))
                return $"AddBequest: will '{willId}' was not drafted by this practice — the lawyer does not amend strangers' wills.";
            if (probate == null)
                return "AddBequest: no probate service given.";

            Will will = probate.Get(willId);
            if (will == null)
                return $"AddBequest: will '{willId}' is not held by probate.";

            string err = probate.AddBequest(will, bequest, population, diag);
            if (err != null)
            {
                diag.Add($"LawyerWillDraftingService.AddBequest: probate refused: {err}");
                return err;
            }

            diag.Add($"LawyerWillDraftingService: bequest added to will {willId} (testator '{entry.TestatorName}') — probate accepted it.");
            return null;
        }

        public DraftedWillEntry GetDraftedWill(string willId)
        {
            return draftedWills.TryGetValue(willId, out DraftedWillEntry e) ? e : null;
        }

        #region Save / Load

        [Serializable]
        public sealed class LawyerWillDraftingSaveDto
        {
            public List<DraftedWillEntry> DraftedWills = new List<DraftedWillEntry>();
        }

        public LawyerWillDraftingSaveDto CaptureSaveDto()
        {
            var dto = new LawyerWillDraftingSaveDto();
            foreach (var entry in draftedWills.Values) dto.DraftedWills.Add(entry);
            return dto;
        }

        public void LoadFromSaveDto(LawyerWillDraftingSaveDto dto)
        {
            draftedWills.Clear();
            if (dto == null) return;
            foreach (var entry in dto.DraftedWills)
            {
                if (entry == null) continue;
                draftedWills[entry.WillId] = entry;
            }
        }

        #endregion
    }
}
