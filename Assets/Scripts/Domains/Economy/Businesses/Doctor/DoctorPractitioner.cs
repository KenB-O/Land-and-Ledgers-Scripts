using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Doctor
{
    /// <summary>
    /// D1A: the practitioner's professional skill. Canon §13.3D: "professional
    /// skill and local trust are not contained in the building deed." Skill
    /// shifts treatment-outcome odds (DoctorTreatmentOutcomes); it never
    /// removes uncertainty. Levels are coarse on purpose — this is not a
    /// diagnosis simulator (Canon §13.3B).
    /// </summary>
    public enum DoctorPractitionerSkill
    {
        Basic = 0,
        Competent = 1,
        Experienced = 2,
    }

    /// <summary>
    /// D1A: what happened to the practitioner. Canon §13.3D: "If a doctor
    /// leaves, dies or is incapacitated, an acquired office may retain
    /// premises, records and some goodwill while losing the core professional
    /// capacity."
    /// </summary>
    public enum DoctorPractitionerStatus
    {
        Active = 0,
        Incapacitated = 1,
        Departed = 2,
        Deceased = 3,
    }

    /// <summary>
    /// D1A: the practitioner behind the practice. The building, the medicine
    /// cabinet and the fee schedule are the practice's; the skill and the
    /// availability are the person's. A practice with no available practitioner
    /// keeps its records and goodwill but has no professional capacity.
    /// </summary>
    [Serializable]
    public sealed class DoctorPractitioner
    {
        public EntityId PractitionerId = EntityId.Invalid; // EntityKind.Person
        public string DisplayName = string.Empty;
        public DoctorPractitionerSkill Skill = DoctorPractitionerSkill.Competent;
        public DoctorPractitionerStatus Status = DoctorPractitionerStatus.Active;
        public int StatusDayIndex = -1;

        public DoctorPractitioner() { }

        public bool IsAvailable => Status == DoctorPractitionerStatus.Active;
    }

    /// <summary>
    /// D1A: a search for another qualified practitioner. Canon §13.3D: "Hiring
    /// or attracting another qualified practitioner is therefore a real
    /// continuity problem" — the search costs money, takes days, and can fail
    /// or return someone below the wanted skill. Data only; the caller moves
    /// money through ledger authorities and advances the days.
    /// </summary>
    [Serializable]
    public sealed class PractitionerSearchRequest
    {
        public EntityId RequestId = EntityId.Invalid; // EntityKind.Contract (W1A precedent)
        public string BusinessInstanceId = string.Empty;
        public DoctorPractitionerSkill RequiredSkill = DoctorPractitionerSkill.Competent;
        public int DayStarted;
        public int MaxSearchDays = 30;
        public int SearchCostCents = 500;

        public PractitionerSearchRequest() { }
    }

    /// <summary>D1A: what the search turned up.</summary>
    public enum PractitionerSearchResult
    {
        StillSearching = 0,
        FoundAtSkill = 1,
        FoundBelowSkill = 2,
        NoCandidate = 3,
    }

    /// <summary>
    /// D1A: resolves a practitioner search. Deterministic given the caller's
    /// RNG; all chances are calibration (Canon Part XV). A below-skill
    /// candidate is one tier under the requirement, never below Basic.
    /// </summary>
    public static class DoctorPractitionerSearch
    {
        /// <summary>TUNING: base chance a search that has run its course finds any candidate.</summary>
        public const float FindCandidateChance = 0.55f;

        /// <summary>TUNING: of found candidates, share that meet the required skill.</summary>
        public const float AtSkillShare = 0.5f;

        public static PractitionerSearchResult ResolveSearch(
            PractitionerSearchRequest request,
            int dayIndex,
            System.Random rng,
            out DoctorPractitionerSkill foundSkill,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            foundSkill = DoctorPractitionerSkill.Basic;
            if (request == null)
            {
                diagnostics.Add("DoctorPractitionerSearch: null request — no search resolved.");
                return PractitionerSearchResult.NoCandidate;
            }

            if (dayIndex - request.DayStarted < Math.Max(1, request.MaxSearchDays))
            {
                return PractitionerSearchResult.StillSearching;
            }

            rng = rng ?? new System.Random(request.DayStarted * 31 + 7);
            if (rng.NextDouble() >= FindCandidateChance)
            {
                diagnostics.Add($"DoctorPractitionerSearch: search for {request.BusinessInstanceId} ran {request.MaxSearchDays} days — no qualified candidate answered. The continuity problem is real.");
                return PractitionerSearchResult.NoCandidate;
            }

            if (rng.NextDouble() < AtSkillShare)
            {
                foundSkill = request.RequiredSkill;
                diagnostics.Add($"DoctorPractitionerSearch: found a {foundSkill} practitioner for {request.BusinessInstanceId}.");
                return PractitionerSearchResult.FoundAtSkill;
            }

            foundSkill = request.RequiredSkill > DoctorPractitionerSkill.Basic
                ? request.RequiredSkill - 1
                : DoctorPractitionerSkill.Basic;
            diagnostics.Add($"DoctorPractitionerSearch: found only a {foundSkill} practitioner for {request.BusinessInstanceId} (wanted {request.RequiredSkill}).");
            return PractitionerSearchResult.FoundBelowSkill;
        }
    }
}
