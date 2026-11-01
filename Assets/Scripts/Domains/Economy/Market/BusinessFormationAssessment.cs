using System;
using LandLedgers.Population;

namespace LandLedgers.Economy.Market
{
    public enum BusinessFormationDecision
    {
        Unsupported = 0,
        NotReady = 1,
        Plausible = 2
    }

    /// <summary>
    /// Evaluates a real Person against a real opportunity. It is deliberately a
    /// decision/read model: it never creates a Business and never turns demand
    /// into revenue by itself.
    /// </summary>
    public sealed class BusinessFormationAssessment
    {
        public BusinessFormationDecision Decision { get; }
        public OccupationProfile FounderProfile { get; }
        public bool HasOpportunity { get; }
        public bool HasCapability { get; }
        public bool HasCapital { get; }
        public bool HasEquipmentAccess { get; }
        public bool HasPremisesAccess { get; }
        public string Explanation { get; }

        private BusinessFormationAssessment(BusinessFormationDecision decision, OccupationProfile profile,
            bool opportunity, bool capability, bool capital, bool equipment, bool premises, string explanation)
        {
            Decision = decision;
            FounderProfile = profile;
            HasOpportunity = opportunity;
            HasCapability = capability;
            HasCapital = capital;
            HasEquipmentAccess = equipment;
            HasPremisesAccess = premises;
            Explanation = explanation ?? string.Empty;
        }

        public static BusinessFormationAssessment Evaluate(
            BusinessOpportunity opportunity,
            PersonState founder,
            int availableCapitalCents,
            bool hasEquipmentAccess,
            bool hasPremisesAccess,
            bool hasTime)
        {
            if (opportunity == null || founder == null)
                return NotReady(null, false, false, false, false, false, "A named opportunity and real founder are required.");

            OccupationProfile profile = ResolveFounderProfile(opportunity.BusinessType, founder);
            bool supported = opportunity.Score01 > 0f;
            bool capable = profile != null;
            bool capital = availableCapitalCents > 0 || founder.startingCashCents > 0;
            bool premises = !RequiresFixedPremises(opportunity.BusinessType) || hasPremisesAccess;
            bool ready = supported && capable && capital && hasEquipmentAccess && premises && hasTime;
            string explanation = ready
                ? $"{founder.DisplayName} has a plausible {profile.DisplayName} path into {opportunity.BusinessType}."
                : BuildMissingReason(supported, capable, capital, hasEquipmentAccess, premises, hasTime);
            return new BusinessFormationAssessment(
                ready ? BusinessFormationDecision.Plausible : BusinessFormationDecision.NotReady,
                profile, supported, capable, capital, hasEquipmentAccess, premises, explanation);
        }

        private static BusinessFormationAssessment NotReady(OccupationProfile profile, bool opportunity, bool capability,
            bool capital, bool equipment, bool premises, string explanation)
        {
            return new BusinessFormationAssessment(BusinessFormationDecision.NotReady, profile,
                opportunity, capability, capital, equipment, premises, explanation);
        }

        private static OccupationProfile ResolveFounderProfile(BusinessType type, PersonState person)
        {
            OccupationProfile profile = OccupationProfileCatalog.Resolve(person);
            string expected = type switch
            {
                BusinessType.CropFarm => "farmer",
                BusinessType.LiveryFreight => "teamster",
                BusinessType.GeneralStore => "merchant",
                BusinessType.Blacksmith => "blacksmith_farrier",
                BusinessType.Hotel => "hospitality_keeper",
                _ => string.Empty
            };
            return profile != null && string.Equals(profile.Id, expected, StringComparison.OrdinalIgnoreCase) ? profile : null;
        }

        private static bool RequiresFixedPremises(BusinessType type)
        {
            return type != BusinessType.LiveryFreight;
        }

        private static string BuildMissingReason(bool opportunity, bool capability, bool capital,
            bool equipment, bool premises, bool time)
        {
            if (!opportunity) return "No supported demand opportunity is present.";
            if (!capability) return "No matching founder occupation/capability is evidenced.";
            if (!capital) return "Working capital is not evidenced.";
            if (!equipment) return "Required equipment access is not evidenced.";
            if (!premises) return "A suitable fixed premises/use right is not evidenced.";
            if (!time) return "The founder has no available time for formation.";
            return "Formation conditions are incomplete.";
        }
    }
}
