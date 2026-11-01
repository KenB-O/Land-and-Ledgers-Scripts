using System;
using System.Collections.Generic;

namespace LandLedgers.World.Property
{
    /// <summary>Pass VIII Worker 2: the outcome of moving one household.</summary>
    [Serializable]
    public sealed class HouseholdMoveResult
    {
        public bool Moved;
        public string Refusal = string.Empty;
        public string AgreementId = string.Empty;
        public List<string> OccupancyIds = new List<string>();
        public List<string> VacatedSpaceIds = new List<string>();

        public HouseholdMoveResult() { }
    }

    /// <summary>
    /// Pass VIII Worker 2: household movement through the real authorities.
    /// A move is validated BEFORE anything changes: the target space must
    /// exist, the arrangement must be stated, and commercial lodging must
    /// have the beds. Then the old occupancies end (departure frees each
    /// old space — a later household can take it) and the new occupancies
    /// are created under a recorded agreement. Nothing is invented and no
    /// partial move is left behind: validation failures refuse before any
    /// occupancy ends.
    /// </summary>
    public sealed class HouseholdMoveService
    {
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public HouseholdMoveResult MoveHousehold(
            int householdId,
            IReadOnlyList<int> personIds,
            string targetSpaceId,
            AccommodationArrangement arrangement,
            string agreementKind,
            string grantorName,
            int dayIndex,
            HousingAuthority housing,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            var result = new HouseholdMoveResult();
            if (housing == null)
            {
                result.Refusal = "HouseholdMoveService.MoveHousehold: no housing authority.";
                diag.Add(result.Refusal);
                return result;
            }
            AccommodationSpace target = housing.FindSpace(targetSpaceId);
            if (target == null)
            {
                result.Refusal = $"HouseholdMoveService.MoveHousehold: unknown space '{targetSpaceId}' — a household cannot move into nonexistent accommodation.";
                diag.Add(result.Refusal);
                return result;
            }
            if (arrangement == AccommodationArrangement.Unspecified)
            {
                result.Refusal = "HouseholdMoveService.MoveHousehold: the arrangement must be stated — arrangements are not tiers.";
                diag.Add(result.Refusal);
                return result;
            }
            var movers = new List<int>();
            if (personIds != null)
            {
                foreach (int personId in personIds)
                {
                    if (personId < 0)
                    {
                        result.Refusal = $"HouseholdMoveService.MoveHousehold: person id {personId} is not a real person.";
                        diag.Add(result.Refusal);
                        return result;
                    }
                    if (!movers.Contains(personId)) movers.Add(personId);
                }
            }
            if (movers.Count == 0)
            {
                result.Refusal = "HouseholdMoveService.MoveHousehold: no people to move.";
                diag.Add(result.Refusal);
                return result;
            }

            // Capacity pre-check for commercial lodging: current occupants
            // of the target who are NOT moving, plus movers not already
            // there, must fit the beds.
            if (HousingAuthority.IsCommercialLodgingArrangement(arrangement))
            {
                var staying = new HashSet<int>();
                foreach (ResidentialOccupancy occ in housing.CurrentOccupants(targetSpaceId))
                {
                    if (occ != null && !movers.Contains(occ.PersonId))
                        staying.Add(occ.PersonId);
                }
                int incoming = 0;
                foreach (int personId in movers)
                {
                    bool alreadyThere = false;
                    foreach (ResidentialOccupancy occ in housing.CurrentOccupanciesForPerson(personId))
                    {
                        if (occ != null && string.Equals(occ.SpaceId, targetSpaceId, StringComparison.Ordinal))
                        {
                            alreadyThere = true;
                            break;
                        }
                    }
                    if (!alreadyThere) incoming++;
                }
                if (staying.Count + incoming > Math.Max(0, target.SleepingCapacity))
                {
                    result.Refusal = $"HouseholdMoveService.MoveHousehold: '{targetSpaceId}' sleeps {target.SleepingCapacity} " +
                                     $"but would hold {staying.Count + incoming} — the move is refused, not overcrowded.";
                    diag.Add(result.Refusal);
                    return result;
                }
            }

            // Validation passed: end the old occupancies (departure frees
            // each old space), then create the new ones.
            var vacated = new HashSet<string>(StringComparer.Ordinal);
            foreach (int personId in movers)
            {
                foreach (ResidentialOccupancy occ in housing.CurrentOccupanciesForPerson(personId))
                {
                    if (occ != null && !string.Equals(occ.SpaceId, targetSpaceId, StringComparison.Ordinal))
                        vacated.Add(occ.SpaceId);
                }
                housing.EndOccupanciesForPerson(personId, dayIndex, diag);
            }
            result.VacatedSpaceIds.AddRange(vacated);

            PropertyAgreement agreement = housing.RecordAgreement(
                string.IsNullOrWhiteSpace(agreementKind) ? arrangement.ToString() : agreementKind,
                string.IsNullOrWhiteSpace(grantorName) ? "unstated grantor" : grantorName,
                $"H{householdId}", targetSpaceId,
                $"household move day {dayIndex} as {arrangement}",
                dayIndex, -1, string.Empty, diag);
            result.AgreementId = agreement != null ? agreement.AgreementId : string.Empty;

            foreach (int personId in movers)
            {
                string refusal = housing.TryOccupyUnderAgreement(
                    personId, householdId, targetSpaceId, arrangement, dayIndex,
                    result.AgreementId, diag, out ResidentialOccupancy occupancy);
                if (refusal != null)
                {
                    // Unreachable after the pre-checks above; loud if it ever happens.
                    result.Refusal = $"HouseholdMoveService.MoveHousehold: post-validation occupancy failed for P{personId}: {refusal}";
                    diag.Add(result.Refusal);
                    return result;
                }
                if (occupancy != null) result.OccupancyIds.Add(occupancy.OccupancyId);
            }

            result.Moved = true;
            diag.Add($"HouseholdMoveService: H{householdId} moved {movers.Count} person(s) into '{targetSpaceId}' as {arrangement} " +
                     $"(agreement '{result.AgreementId}'); {result.VacatedSpaceIds.Count} old space(s) freed.");
            return result;
        }
    }
}
