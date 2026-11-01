using System;
using System.Collections.Generic;

namespace LandLedgers.Population.TownGrowth
{
    /// <summary>
    /// Pass 5 (town growth, last content pass): lead enrichment and
    /// settlement-input model for role-based recruitment.
    ///
    /// This file is ADDITIVE ONLY. It does not edit, rename, or replace
    /// RecruitmentConversation, RecruitmentLead, ArrivalSettlement, or
    /// ArrivalSettlementDriver. Those types keep their existing fields
    /// and behaviour; this model carries the extra data they do not
    /// carry (role, off-map location/address, profession, job
    /// disposition, and a saveable lead record) as plain fields, so a
    /// production driver can use it without rewriting the old types.
    ///
    /// Rules, all from Kennedy's direction and the town precursor audit:
    ///   - Family-only is the honest bound today. No friend graph
    ///     exists. A friend source is refused unless the caller
    ///     supplies friend edges built from real shared history
    ///     (see Pass5ConversationChain).
    ///   - Fredericton is Kennedy's EXAMPLE, never canon. No town name
    ///     is hard-coded anywhere in this pass. Every location is
    ///     caller-supplied; an empty location means generic off-map,
    ///     exactly as PostalService.OffMapOfficeId ("offmap") does today.
    ///   - Nothing is invented at ask time. Profession, disposition,
    ///     wage, location, and role must be supplied by the caller
    ///     (tree build or lead time, from a real source). Missing data
    ///     stays Unknown/empty and is diagnosed, never filled with a
    ///     plausible-sounding guess.
    ///   - Settlement refusal rules mirror ArrivalSettlement: no
    ///     housing unit/vacancy, or no job terms and no explicit
    ///     unemployed-with-plan flag, means refused. A refused
    ///     newcomer is never silently made homeless by settlement;
    ///     the legacy monthly roll is the only path that admits
    ///     without those checks, and this model preserves that
    ///     distinction instead of hiding it.
    ///   - Housing assignment is ALWAYS pending in this model.
    ///     The string HousingUnitId to int homeBuildingId mapping
    ///     belongs to the HousingResidence runtime (Codex). No caller
    ///     may mistake this model for a residence assignment.
    ///   - Heating/stove data is NOT authored here. Slice households
    ///     have no stove/fuel/retention data, and heating
    ///     implementation is blocked on Kennedy decisions. Settlement
    ///     inputs therefore record whether stove/retention/fuel data
    ///     was authored or is a default, so a default is never
    ///     mistaken for authored data.
    /// Plain C#, no Unity dependency, Roslyn-testable.
    /// </summary>
    public enum Pass5ContactDisposition
    {
        Unknown = 0,
        Employed = 1,
        RecentlyLostJob = 2,
        SeekingChange = 3,
    }

    public sealed class Pass5OffMapLocation
    {
        /// <summary>Caller-supplied town name. Empty means generic off-map, never a guessed town.</summary>
        public string TownName = string.Empty;
        /// <summary>Caller-supplied postal destination id. Empty means the generic "offmap" office.</summary>
        public string PostalDestinationId = string.Empty;

        public bool IsNamedTown
        {
            get { return !string.IsNullOrWhiteSpace(TownName); }
        }

        public string EffectivePostalDestination()
        {
            if (!string.IsNullOrWhiteSpace(PostalDestinationId)) return PostalDestinationId;
            return "offmap";
        }
    }

    /// <summary>
    /// An enriched recruitment lead. ContactPersonId/Name and
    /// RecommenderPersonId/Name mirror RecruitmentLead; everything
    /// else is data RecruitmentLead does not carry today.
    /// RoleDisplayName must come from the business's real roles
    /// (caller-supplied list, validated by the conversation chain),
    /// never free-text invention.
    /// </summary>
    public sealed class Pass5EnrichedLead
    {
        public int ContactPersonId = -1;
        public string ContactName = string.Empty;
        public int RecommenderPersonId = -1;
        public string RecommenderName = string.Empty;
        public string BusinessId = string.Empty; // WorkAtBusiness only
        public string RoleId = string.Empty; // real business role id, empty for MoveToTown
        public string RoleDisplayName = string.Empty; // e.g. "driver" - caller-supplied, never invented
        public Pass5OffMapLocation Location = new Pass5OffMapLocation();
        public string ProfessionId = string.Empty;
        public string ProfessionName = string.Empty;
        public Pass5ContactDisposition Disposition = Pass5ContactDisposition.Unknown;
        public bool HasCurrentWageCents;
        public int CurrentWageCents;
        public int DayIndex;
    }

    /// <summary>
    /// Enrichment that refuses to invent. Every enrichment input is
    /// optional; a missing input leaves the field Unknown/empty and
    /// adds a diagnostic, so a driver can see exactly what is known
    /// and what is not. This method never assigns a profession,
    /// disposition, wage, location, or role on its own.
    /// </summary>
    public static class Pass5LeadEnrichment
    {
        public static Pass5EnrichedLead Enrich(
            int contactPersonId, string contactName,
            int recommenderPersonId, string recommenderName,
            string businessId, string roleId, string roleDisplayName,
            Pass5OffMapLocation location, string professionId, string professionName,
            Pass5ContactDisposition disposition, bool hasWage, int wageCents,
            int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var lead = new Pass5EnrichedLead
            {
                ContactPersonId = contactPersonId,
                ContactName = contactName ?? string.Empty,
                RecommenderPersonId = recommenderPersonId,
                RecommenderName = recommenderName ?? string.Empty,
                BusinessId = businessId ?? string.Empty,
                RoleId = roleId ?? string.Empty,
                RoleDisplayName = roleDisplayName ?? string.Empty,
                Location = location ?? new Pass5OffMapLocation(),
                ProfessionId = professionId ?? string.Empty,
                ProfessionName = professionName ?? string.Empty,
                Disposition = disposition,
                HasCurrentWageCents = hasWage,
                CurrentWageCents = hasWage ? wageCents : 0,
                DayIndex = dayIndex,
            };
            if (!string.IsNullOrWhiteSpace(businessId) && string.IsNullOrWhiteSpace(roleDisplayName))
            {
                diagnostics.Add("Pass5LeadEnrichment: work lead has a business but no role; role must come from the business's real roles, not invented here.");
            }
            if (string.IsNullOrWhiteSpace(professionId))
            {
                diagnostics.Add("Pass5LeadEnrichment: contact profession unknown; do not match this lead to a role by profession until profession is authored.");
            }
            if (!lead.Location.IsNamedTown)
            {
                diagnostics.Add("Pass5LeadEnrichment: contact location is generic off-map; letter routes to 'offmap', not to a named town.");
            }
            if (disposition == Pass5ContactDisposition.Unknown)
            {
                diagnostics.Add("Pass5LeadEnrichment: contact disposition unknown; do not assume employed, job-seeking, or willing to move.");
            }
            return lead;
        }
    }

    /// <summary>
    /// Saveable lead record. RecruitmentLead has no save DTO today,
    /// so leads are lost on reload (Phase 4 F29). This record is the
    /// model for a saveable lead: all fields are plain strings/ints,
    /// round-trippable without the population registries, and it
    /// never claims the existing RecruitmentLead is persisted - the
    /// production driver must adopt this record (or an equivalent
    /// DTO) and wire it into save/load. Until then, leads remain
    /// transient, and this record documents that gap instead of
    /// hiding it.
    /// </summary>
    public sealed class Pass5LeadSaveRecord
    {
        public int ContactPersonId = -1;
        public string ContactName = string.Empty;
        public int RecommenderPersonId = -1;
        public string RecommenderName = string.Empty;
        public string BusinessId = string.Empty;
        public string RoleId = string.Empty;
        public string RoleDisplayName = string.Empty;
        public string LocationTownName = string.Empty;
        public string LocationPostalDestinationId = string.Empty;
        public string ProfessionId = string.Empty;
        public string ProfessionName = string.Empty;
        public int Disposition;
        public bool HasCurrentWageCents;
        public int CurrentWageCents;
        public int DayIndex;

        public static Pass5LeadSaveRecord FromLead(Pass5EnrichedLead lead)
        {
            if (lead == null) return new Pass5LeadSaveRecord();
            return new Pass5LeadSaveRecord
            {
                ContactPersonId = lead.ContactPersonId,
                ContactName = lead.ContactName,
                RecommenderPersonId = lead.RecommenderPersonId,
                RecommenderName = lead.RecommenderName,
                BusinessId = lead.BusinessId,
                RoleId = lead.RoleId,
                RoleDisplayName = lead.RoleDisplayName,
                LocationTownName = lead.Location != null ? lead.Location.TownName : string.Empty,
                LocationPostalDestinationId = lead.Location != null ? lead.Location.PostalDestinationId : string.Empty,
                ProfessionId = lead.ProfessionId,
                ProfessionName = lead.ProfessionName,
                Disposition = (int)lead.Disposition,
                HasCurrentWageCents = lead.HasCurrentWageCents,
                CurrentWageCents = lead.CurrentWageCents,
                DayIndex = lead.DayIndex,
            };
        }

        public Pass5EnrichedLead ToLead()
        {
            return new Pass5EnrichedLead
            {
                ContactPersonId = ContactPersonId,
                ContactName = ContactName,
                RecommenderPersonId = RecommenderPersonId,
                RecommenderName = RecommenderName,
                BusinessId = BusinessId,
                RoleId = RoleId,
                RoleDisplayName = RoleDisplayName,
                Location = new Pass5OffMapLocation
                {
                    TownName = LocationTownName,
                    PostalDestinationId = LocationPostalDestinationId,
                },
                ProfessionId = ProfessionId,
                ProfessionName = ProfessionName,
                Disposition = (Pass5ContactDisposition)Disposition,
                HasCurrentWageCents = HasCurrentWageCents,
                CurrentWageCents = CurrentWageCents,
                DayIndex = DayIndex,
            };
        }
    }

    /// <summary>
    /// Caller-supplied settlement inputs for one newcomer. Mirrors
    /// ArrivalSettlement's gates; it does not settle anyone itself.
    /// Stove/retention/fuel fields are flags, not values invented
    /// here: if a field was not authored, the model records the
    /// code default explicitly and flags it as a default.
    /// </summary>
    public sealed class Pass5SettlementInputs
    {
        public int ContactPersonId = -1;
        public bool IsAlreadyOnMap;
        public string HousingUnitId = string.Empty;
        public int HousingVacancies;
        public bool HasCompensationTerms;
        public bool ExplicitUnemployedWithPlan;
        public bool StoveDataAuthored;
        public bool HeatRetentionDataAuthored;
        public bool PreferredFuelDataAuthored;
        // Defaults, recorded only when the matching Authored flag is false.
        public string DefaultStoveTier = "Basic";
        public string DefaultHeatRetentionTier = "Basic";
        public string DefaultPreferredFuel = "Wood";
    }

    public sealed class Pass5SettlementReadiness
    {
        public bool Ready;
        public bool EmploymentPending;
        public bool HousingAssignmentPending = true; // always true: HousingResidence (Codex) owns the mapping
        public bool UsesDefaultStoveData;
        public bool UsesDefaultRetentionData;
        public bool UsesDefaultFuelData;
        public List<string> Reasons = new List<string>();
    }

    public static class Pass5SettlementReadinessModel
    {
        /// <summary>
        /// Pure gate check. Ready means ArrivalSettlementDriver
        /// could be called with these inputs; it does not call it,
        /// does not create a household, and does not assign a home.
        /// EmploymentPending is true when terms are absent but the
        /// explicit unemployed-with-plan flag is present - the driver
        /// must then supply terms or register job-seeking, never
        /// invent employment.
        /// </summary>
        public static Pass5SettlementReadiness Check(Pass5SettlementInputs inputs)
        {
            var result = new Pass5SettlementReadiness();
            if (inputs == null)
            {
                result.Reasons.Add("Pass5Settlement: no settlement inputs supplied; refused.");
                return result;
            }
            if (inputs.IsAlreadyOnMap)
            {
                result.Reasons.Add("Pass5Settlement: contact is already on-map; settlement refused, never a second person.");
            }
            if (string.IsNullOrWhiteSpace(inputs.HousingUnitId) || inputs.HousingVacancies <= 0)
            {
                result.Reasons.Add("Pass5Settlement: no housing unit id or zero vacancies; refused, newcomer stays off-map, never homeless by settlement.");
            }
            if (!inputs.HasCompensationTerms && !inputs.ExplicitUnemployedWithPlan)
            {
                result.Reasons.Add("Pass5Settlement: no job terms and no explicit unemployed-with-plan flag; refused, never assumed.");
            }
            result.EmploymentPending = !inputs.HasCompensationTerms && inputs.ExplicitUnemployedWithPlan;
            result.UsesDefaultStoveData = !inputs.StoveDataAuthored;
            result.UsesDefaultRetentionData = !inputs.HeatRetentionDataAuthored;
            result.UsesDefaultFuelData = !inputs.PreferredFuelDataAuthored;
            if (result.UsesDefaultStoveData) result.Reasons.Add("Pass5Settlement: stove data is a default (Basic), not authored data.");
            if (result.UsesDefaultRetentionData) result.Reasons.Add("Pass5Settlement: heat-retention data is a default (Basic), not authored data.");
            if (result.UsesDefaultFuelData) result.Reasons.Add("Pass5Settlement: preferred fuel is a default (Wood), not authored data.");
            // Ready when no refusal reason exists. Default-data notes
            // do not block readiness, they only flag it.
            result.Ready = !inputs.IsAlreadyOnMap
                && !string.IsNullOrWhiteSpace(inputs.HousingUnitId) && inputs.HousingVacancies > 0
                && (inputs.HasCompensationTerms || inputs.ExplicitUnemployedWithPlan);
            return result;
        }
    }

    /// <summary>
    /// R05 employment id format, accepted as already in driver code:
    /// "arrival-&lt;person&gt;-&lt;business&gt;-&lt;day&gt;". This helper
    /// builds exactly that format and nothing else, so Pass 5 does
    /// not silently change it. Kennedy should still accept the
    /// format before a driver runs in production (Phase 5 R05).
    /// </summary>
    public static class Pass5EmploymentId
    {
        public static string Build(int personId, string businessId, int dayIndex)
        {
            return "arrival-" + personId + "-" + (businessId ?? string.Empty) + "-" + dayIndex;
        }
    }
}
