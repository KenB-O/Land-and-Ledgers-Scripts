using System;
using System.Collections.Generic;
using LandLedgers.Economy;

namespace LandLedgers.Population
{
    /// <summary>
    /// Opening-world invariant pass over the existing Population and Business
    /// authorities. This is deliberately diagnostic: it reports contradictions and
    /// never fabricates a missing Person, Household, owner, or residence.
    /// </summary>
    public sealed class SettlementFoundationReconciliation
    {
        public IReadOnlyList<string> Errors { get; }
        public IReadOnlyList<string> Warnings { get; }
        public bool IsValid => Errors.Count == 0;

        private SettlementFoundationReconciliation(List<string> errors, List<string> warnings)
        {
            Errors = errors;
            Warnings = warnings;
        }

        public static SettlementFoundationReconciliation Check(
            PopulationState population,
            IReadOnlyList<BusinessInstanceState> businesses)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            var people = new Dictionary<int, PersonState>();
            var households = new Dictionary<int, HouseholdState>();

            foreach (PersonState person in population?.people ?? new List<PersonState>())
            {
                if (person == null) continue;
                if (!people.TryAdd(person.id, person))
                {
                    errors.Add($"Duplicate Person id {person.id}.");
                    continue;
                }

                if (person.deathDayIndex < 0 && population.GetHousehold(person.householdId) == null)
                {
                    errors.Add($"Living Person {person.id} has no Household {person.householdId}.");
                }

                if (person.deathDayIndex < 0 && person.homeBuildingId < 0
                    && person.settlementArrangement != SettlementArrangement.Transient)
                {
                    errors.Add($"Resident Person {person.id} has no accommodation building.");
                }
            }

            foreach (HouseholdState household in population?.households ?? new List<HouseholdState>())
            {
                if (household == null) continue;
                if (!households.TryAdd(household.id, household))
                {
                    errors.Add($"Duplicate Household id {household.id}.");
                    continue;
                }

                if (household.memberIds == null || household.memberIds.Count == 0)
                {
                    errors.Add($"Household {household.id} has no members.");
                    continue;
                }

                foreach (int memberId in household.memberIds)
                {
                    if (!people.TryGetValue(memberId, out PersonState member))
                    {
                        errors.Add($"Household {household.id} references missing Person {memberId}.");
                    }
                    else if (member.householdId != household.id)
                    {
                        warnings.Add($"Person {member.id} household mirror is {member.householdId}; membership authority is Household {household.id}.");
                    }
                }
            }

            foreach (BusinessInstanceState business in businesses ?? Array.Empty<BusinessInstanceState>())
            {
                if (business == null) continue;
                BusinessOwnerIdentity owner = business.Owner;
                if (owner == null)
                {
                    errors.Add($"Business {business.InstanceId} has no owner.");
                }
                else if (owner.OwnerKind == BusinessOwnerKind.Npc && !people.ContainsKey(owner.PersonId))
                {
                    errors.Add($"Business {business.InstanceId} names missing owner Person {owner.PersonId}.");
                }

                // Freight is route/mobile work and is allowed to have no fixed
                // premises. Other opening businesses must carry a concrete site once
                // the formation authority has accepted them.
                if (business.BusinessType != BusinessType.LiveryFreight
                    && business.AssignedBuildingId < 0)
                {
                    warnings.Add($"Business {business.InstanceId} has no fixed premises yet.");
                }
            }

            return new SettlementFoundationReconciliation(errors, warnings);
        }

        public string BuildSummary()
        {
            if (IsValid && Warnings.Count == 0) return "Opening settlement reconciliation passed.";
            return $"Opening settlement reconciliation: {Errors.Count} error(s), {Warnings.Count} warning(s).";
        }
    }
}
