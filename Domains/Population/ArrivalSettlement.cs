using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass XL phase 3, item 5B.1: arrival settlement.
    ///
    /// The gap: nothing in the immigration design settled a newcomer
    /// once they arrived. A recruited family contact or traveler who
    /// arrived homeless, household-less, and unemployed would break
    /// daily needs, meals, and employment, because those systems read
    /// housing, household, and employment state that was never set.
    ///
    /// This step is a GATE and a RECORD, deliberately not a registry
    /// writer. It refuses an arrival that cannot be settled, and it
    /// records the three things a settled arrival must have:
    ///   1. housing placement — a caller-supplied housing unit id
    ///      (existing housing/residence systems supply the id; this
    ///      class does not invent a house, and a vacancy count of 0
    ///      refuses settlement);
    ///   2. household — either an existing household id to join, or a
    ///      newly allocated household id that the production driver
    ///      must create through HouseholdLifecycleManager; this class
    ///      allocates the id from PopulationState but does NOT create
    ///      the HouseholdState or write HouseholdMembershipRegistry,
    ///      because that registry is the sole membership authority;
    ///   3. job — either a business id with at least one open slot
    ///      supplied by the caller, or an EXPLICIT unemployed-with-plan
    ///      flag the caller acknowledges. A caller who supplies neither
    ///      a job nor the explicit flag is refused: an arrival without
    ///      a job does not silently succeed.
    ///
    /// When, and only when, all three are present, the person is
    /// materialised (same record, same id, via FamilyTreeBuilder) and
    /// the settlement record is returned. The production driver must
    /// then run, in order: HouseholdLifecycleManager household creation
    /// / joining, housing/residence assignment for the recorded unit,
    /// and EmploymentRelationship creation for the recorded business
    /// (or job-seeking registration for the unemployed-with-plan case).
    /// Those three driver steps are the exact missing link if they are
    /// not yet wired; they are NOT faked here.
    /// Plain C#, no Unity dependency.
    /// </summary>
    public sealed class ArrivalSettlementRequest
    {
        public int PersonId = -1;
        public string HousingUnitId = string.Empty;
        public int AvailableHousingUnits;
        public int ExistingHouseholdId = -1; // -1 = create a new household
        public string JobBusinessId = string.Empty;
        public int OpenJobSlotsAtBusiness;
        public bool ExplicitUnemployedWithPlan; // caller acknowledges: no job yet, job-seeking plan
        public int DayIndex;
    }

    public sealed class ArrivalSettlementRecord
    {
        public bool Settled;
        public int PersonId = -1;
        public string HousingUnitId = string.Empty;
        public int HouseholdId = -1;
        public bool IsNewHousehold;
        public string JobBusinessId = string.Empty;
        public bool UnemployedWithPlan;
        public List<string> Reasons = new List<string>();
    }

    public static class ArrivalSettlement
    {
        public static ArrivalSettlementRecord Settle(
            PopulationState population, ArrivalSettlementRequest request, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            var record = new ArrivalSettlementRecord { PersonId = request?.PersonId ?? -1 };
            if (population == null || population.people == null || request == null)
            {
                record.Reasons.Add("ArrivalSettlement: null population or request; arrival refused.");
                diagnostics.AddRange(record.Reasons);
                return record;
            }
            PersonState person = population.GetPerson(request.PersonId);
            if (person == null)
            {
                record.Reasons.Add($"ArrivalSettlement: P{request.PersonId} unknown; arrival refused.");
                diagnostics.AddRange(record.Reasons);
                return record;
            }
            if (person.deathDayIndex >= 0)
            {
                record.Reasons.Add($"ArrivalSettlement: P{request.PersonId} is deceased; arrival refused.");
                diagnostics.AddRange(record.Reasons);
                return record;
            }
            if (!person.isOffMap)
            {
                record.Reasons.Add($"ArrivalSettlement: P{request.PersonId} is already on map; settlement is not an arrival.");
                diagnostics.AddRange(record.Reasons);
                return record;
            }
            if (string.IsNullOrWhiteSpace(request.HousingUnitId) || request.AvailableHousingUnits <= 0)
            {
                record.Reasons.Add("ArrivalSettlement: no housing placement (unit id missing or no vacancies); arrival refused, newcomer stays off map.");
                diagnostics.AddRange(record.Reasons);
                return record;
            }
            bool hasJob = !string.IsNullOrWhiteSpace(request.JobBusinessId) && request.OpenJobSlotsAtBusiness > 0;
            if (!hasJob && !request.ExplicitUnemployedWithPlan)
            {
                record.Reasons.Add("ArrivalSettlement: no job slot and no explicit unemployed-with-plan flag; arrival refused, it would not silently succeed as unemployed.");
                diagnostics.AddRange(record.Reasons);
                return record;
            }
            if (!hasJob && !string.IsNullOrWhiteSpace(request.JobBusinessId) && request.OpenJobSlotsAtBusiness <= 0)
            {
                record.Reasons.Add($"ArrivalSettlement: business '{request.JobBusinessId}' has no open slots; falling back to explicit unemployed-with-plan is required, not assumed.");
                diagnostics.AddRange(record.Reasons);
                return record;
            }

            int householdId = request.ExistingHouseholdId >= 0
                ? request.ExistingHouseholdId
                : population.AllocateNextHouseholdId();
            bool materialised = FamilyTreeBuilder.MaterialiseOffMapRelative(
                population, request.PersonId, householdId, homeBuildingId: -1, diagnostics);
            if (!materialised)
            {
                record.Reasons.Add($"ArrivalSettlement: P{request.PersonId} could not be materialised; arrival refused.");
                diagnostics.AddRange(record.Reasons);
                return record;
            }

            record.Settled = true;
            record.HousingUnitId = request.HousingUnitId;
            record.HouseholdId = householdId;
            record.IsNewHousehold = request.ExistingHouseholdId < 0;
            record.JobBusinessId = hasJob ? request.JobBusinessId : string.Empty;
            record.UnemployedWithPlan = !hasJob && request.ExplicitUnemployedWithPlan;
            record.Reasons.Add(
                $"ArrivalSettlement: P{request.PersonId} settled — housing {record.HousingUnitId}, " +
                $"household {record.HouseholdId} {(record.IsNewHousehold ? "(new, driver must create via HouseholdLifecycleManager)" : "(existing, driver must join via membership registry)")}, " +
                $"{(record.UnemployedWithPlan ? "unemployed with an explicit job-seeking plan" : $"job at {record.JobBusinessId} (driver must create the EmploymentRelationship)")}.");
            diagnostics.AddRange(record.Reasons);
            return record;
        }
    }
}
