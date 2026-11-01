// Generic Business Reconstruction, sections 9 and 10.
// Premises rights and site eligibility, without modern zoning gates.
// A business may operate from owned, leased, rented, shared, or
// mobile premises, or another historically and legally valid
// arrangement - but never from space it has no right to use.
// Plot zone is a RECOMMENDATION, not a hard gate: the original Canon
// explicitly discourages modern zoning-style hard gates. The old
// BusinessSiteRequirementDefinition.Meets still treats AllowedPlotZones
// as a gate for legacy callers; this evaluator is the replacement
// path Unity AI should migrate callers to. Zone mismatch produces a
// warning, never a refusal, except where a real agricultural site
// role is required (a physical/legal constraint, preserved).
// Every other refusal here is a real constraint supplied by the
// caller (right, lease, access, physical suitability, nuisance,
// safety, legal, infrastructure). If the caller does not supply a
// constraint, this evaluator does not invent one.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.GenericBusiness
{
    public enum PremisesRightKind
    {
        None = 0,
        Owned = 1,
        Leased = 2,
        Rented = 3,
        Shared = 4,
        Mobile = 5,
        OtherValidArrangement = 6,
    }

    public sealed class SiteEvaluationInput
    {
        public PremisesRightKind RightKind = PremisesRightKind.None;
        public string RightDescription = string.Empty;
        public bool LeaseTermsSatisfied = true;
        public bool HasAccess = true;
        public bool PhysicallySuitable = true;
        public bool NuisanceAcceptable = true;
        public bool SafetySatisfied = true;
        public bool LegalRequirementsSatisfied = true;
        public bool InfrastructureAvailable = true;
        public string PlotZoneLabel = string.Empty;
        public List<string> RecommendedPlotZones = new List<string>();
        public bool RequiresAgriculturalSiteRole;
        public bool HasRequiredAgriculturalSiteRole;
        public string AgriculturalSiteRole = string.Empty;
    }

    public sealed class SiteEvaluationResult
    {
        public bool CanOperate;
        public List<string> RefusalReasons = new List<string>();
        public List<string> Warnings = new List<string>();
    }

    public static class SiteEligibility
    {
        public static SiteEvaluationResult Evaluate(SiteEvaluationInput input)
        {
            var result = new SiteEvaluationResult();
            if (input == null)
            {
                result.RefusalReasons.Add("No site evaluation input supplied.");
                result.CanOperate = false;
                return result;
            }
            if (input.RightKind == PremisesRightKind.None)
            {
                result.RefusalReasons.Add("Business has no right to use this space (no ownership, lease, rental, shared, mobile, or other valid arrangement).");
            }
            if (!input.LeaseTermsSatisfied)
            {
                result.RefusalReasons.Add("Lease terms are not satisfied for this use.");
            }
            if (!input.HasAccess)
            {
                result.RefusalReasons.Add("Site has no access for customers or deliveries.");
            }
            if (!input.PhysicallySuitable)
            {
                result.RefusalReasons.Add("Site is not physically suitable for this business.");
            }
            if (!input.NuisanceAcceptable)
            {
                result.RefusalReasons.Add("Site use would create an unacceptable nuisance here.");
            }
            if (!input.SafetySatisfied)
            {
                result.RefusalReasons.Add("Safety requirements are not satisfied.");
            }
            if (!input.LegalRequirementsSatisfied)
            {
                result.RefusalReasons.Add("Historically applicable legal requirements are not satisfied.");
            }
            if (!input.InfrastructureAvailable)
            {
                result.RefusalReasons.Add("Required infrastructure is not available at this site.");
            }
            if (input.RequiresAgriculturalSiteRole && !input.HasRequiredAgriculturalSiteRole)
            {
                result.RefusalReasons.Add($"Business requires an agricultural site role ({input.AgriculturalSiteRole}) that this site does not have.");
            }
            if (!string.IsNullOrWhiteSpace(input.PlotZoneLabel) && input.RecommendedPlotZones != null && input.RecommendedPlotZones.Count > 0)
            {
                bool matches = false;
                foreach (string zone in input.RecommendedPlotZones)
                {
                    if (string.Equals(zone, input.PlotZoneLabel, StringComparison.OrdinalIgnoreCase)) { matches = true; break; }
                }
                // A MixedUse PLOT accepts many businesses, so a business on
                // a MixedUse plot matches any recommendation. The reverse
                // is NOT true: a recommendation that includes MixedUse
                // does not make a Residential or Agricultural plot match.
                if (string.Equals(input.PlotZoneLabel, "MixedUse", StringComparison.OrdinalIgnoreCase)) { matches = true; }
                if (!matches)
                {
                    result.Warnings.Add($"Plot zone {input.PlotZoneLabel} is not the recommended zone for this business; this is a warning, not a block. Recommended: {string.Join("/", input.RecommendedPlotZones)}.");
                }
            }
            result.CanOperate = result.RefusalReasons.Count == 0;
            return result;
        }

        /// <summary>
        /// The one-line answer to section 10: a plot-zone label alone
        /// never blocks legitimate commercial activity in this model.
        /// Callers that still block on zone are legacy and should be
        /// migrated to Evaluate above.
        /// </summary>
        public static bool ZoneAloneBlocksActivity()
        {
            return false;
        }
    }
}
