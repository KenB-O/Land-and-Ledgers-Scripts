using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Primitives;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass XL phase 4 (finding F02): the production driver step that
    /// ArrivalSettlement deliberately did not do. ArrivalSettlement.Settle
    /// is a gate + record: it refuses unsettleable arrivals, allocates
    /// the new-household id from PopulationState, and materialises the
    /// person (same record, same id, isOffMap flipped, household pointer
    /// set). This driver applies a SETTLED record to the registries:
    ///
    ///   1. Household: for an existing household, HouseholdLifecycleManager
    ///      .AddMember joins the person (the membership registry is the
    ///      sole membership authority). For a new household, the driver
    ///      creates the HouseholdState with the record's PRE-ALLOCATED id
    ///      — it must NOT call FormHousehold, which would allocate a
    ///      second, different id and orphan the record's id — then joins
    ///      the person to it. If the join is rejected after a household
    ///      was created here, the empty household is removed again, so a
    ///      failed apply never leaves an orphan household behind.
    ///   2. Employment: when the record names a business AND the caller
    ///      supplies compensation terms, an EmploymentRelationship is
    ///      registered (deterministic id
    ///      "arrival-&lt;personId&gt;-&lt;businessId&gt;-&lt;day&gt;", first-wins
    ///      on duplicates; the business id is part of the id since
    ///      phase 5, so two businesses on the same day cannot collide).
    ///      When terms are NOT supplied, no employment is invented: the
    ///      result reports EmploymentPending and the caller must supply
    ///      terms or register job-seeking.
    ///   3. Housing/residence: NOT done here. The record's HousingUnitId
    ///      is a string unit id supplied by the housing systems, while
    ///      PersonState.homeBuildingId is an int; mapping one to the
    ///      other belongs to the HousingResidence runtime (Codex). The
    ///      result always reports HousingAssignmentPending = true so no
    ///      caller can mistake this driver for a residence assignment.
    ///
    /// Idempotency: applying the same settled record twice creates
    /// nothing twice. A person who already holds an active membership
    /// in the record's household applies as AlreadyApplied (no duplicate
    /// membership, no duplicate employment). A person holding an active
    /// membership in a DIFFERENT household is refused, never silently
    /// transferred — a transfer is an explicit, separate decision.
    ///
    /// Pass XL phase 5 (finding R01): prefer the composed entry point
    /// <see cref="ArrivalSettlementDriver.SettleAndApply"/> over calling
    /// Settle then Apply by hand. Settle materialises the person
    /// (flips isOffMap, sets household/home pointers) BEFORE the driver
    /// runs, so a hand-rolled sequence that reaches a driver refusal
    /// (dissolved or missing household, conflicting membership, join
    /// rejection) strands the person on map with no membership.
    /// SettleAndApply rolls the person's flags back to their pre-settle
    /// values on a driver refusal (a household the driver created is
    /// already removed by Apply itself; the allocated household id gap
    /// remains, which is harmless — allocator ids are monotonic, not
    /// dense). Raw Settle + Apply behaviour is unchanged.
    /// Plain C#, no Unity dependency, Roslyn-testable.
    /// </summary>
    public sealed class ArrivalSettlementOutcome
    {
        public ArrivalSettlementRecord Record;
        public ArrivalSettlementApplication Application; // null when settlement itself refused
        public bool SettledAndApplied; // settled AND driver did not refuse (Applied or AlreadyApplied)
        public bool RolledBack; // driver refused after materialisation; person flags restored
    }
    public sealed class ArrivalSettlementApplication
    {
        public bool Applied;
        public bool AlreadyApplied;
        public bool Refused;
        public bool HouseholdCreated;
        public bool EmploymentCreated;
        public bool EmploymentPending;
        public bool HousingAssignmentPending = true;
        public int HouseholdId = -1;
        public List<string> Reasons = new List<string>();
    }

    public static class ArrivalSettlementDriver
    {
        /// <summary>
        /// Pass XL phase 5 (finding R01): the composed settle-then-apply
        /// path. Runs ArrivalSettlement.Settle and, only if it settles,
        /// Apply, in one call. If Apply refuses after Settle already
        /// materialised the person, the person's pre-settle flags are
        /// restored (isOffMap, householdId, homeBuildingId), so a
        /// refused arrival leaves no stranded on-map person with no
        /// membership. A household Apply created and then removed on a
        /// join rejection stays removed. The allocated household id gap
        /// on a rolled-back new-household settlement remains (allocator
        /// ids are monotonic, not dense) and is harmless. When
        /// settlement itself refuses, nothing is materialised and no
        /// application is attempted (Application is null). An
        /// AlreadyApplied result counts as settled-and-applied: the
        /// person is on map with the membership already in place and
        /// nothing was duplicated.
        /// </summary>
        public static ArrivalSettlementOutcome SettleAndApply(
            PopulationState population,
            HouseholdLifecycleManager lifecycle,
            HouseholdMembershipRegistry memberships,
            EmploymentRelationshipRegistry employments,
            ArrivalSettlementRequest request,
            int dayIndex,
            string roleDisplayName,
            CompensationTerms? compensation,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            var outcome = new ArrivalSettlementOutcome();
            PersonState personBefore = request != null && population != null
                ? population.GetPerson(request.PersonId)
                : null;
            bool wasOffMap = personBefore != null && personBefore.isOffMap;
            int prevHouseholdId = personBefore != null ? personBefore.householdId : -1;
            int prevHomeBuildingId = personBefore != null ? personBefore.homeBuildingId : -1;

            ArrivalSettlementRecord record = ArrivalSettlement.Settle(population, request, diagnostics);
            outcome.Record = record;
            if (record == null || !record.Settled)
            {
                diagnostics.Add("ArrivalSettlementDriver: settlement refused; nothing materialised and the driver was not run.");
                return outcome;
            }

            ArrivalSettlementApplication application = Apply(
                population, lifecycle, memberships, employments, record,
                dayIndex, roleDisplayName, compensation, diagnostics);
            outcome.Application = application;
            if (application != null && application.Refused)
            {
                PersonState person = population.GetPerson(record.PersonId);
                if (person != null && wasOffMap)
                {
                    person.isOffMap = true;
                    person.householdId = prevHouseholdId;
                    person.homeBuildingId = prevHomeBuildingId;
                }
                outcome.RolledBack = true;
                diagnostics.Add(
                    $"ArrivalSettlementDriver: driver refused after settlement for P{record.PersonId}; " +
                    "person flags rolled back to pre-settle values (off map, no membership from this arrival).");
                return outcome;
            }

            outcome.SettledAndApplied = true;
            return outcome;
        }

        public static ArrivalSettlementApplication Apply(
            PopulationState population,
            HouseholdLifecycleManager lifecycle,
            HouseholdMembershipRegistry memberships,
            EmploymentRelationshipRegistry employments,
            ArrivalSettlementRecord record,
            int dayIndex,
            string roleDisplayName,
            CompensationTerms? compensation,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            var result = new ArrivalSettlementApplication();
            if (population == null || population.people == null || lifecycle == null || memberships == null || record == null)
            {
                result.Refused = true;
                result.Reasons.Add("ArrivalSettlementDriver: null population, lifecycle, memberships, or record; nothing applied.");
                diagnostics.AddRange(result.Reasons);
                return result;
            }
            if (!record.Settled)
            {
                result.Refused = true;
                result.Reasons.Add($"ArrivalSettlementDriver: P{record.PersonId} has no settled record; settlement must pass ArrivalSettlement.Settle first.");
                diagnostics.AddRange(result.Reasons);
                return result;
            }
            PersonState person = population.GetPerson(record.PersonId);
            if (person == null)
            {
                result.Refused = true;
                result.Reasons.Add($"ArrivalSettlementDriver: P{record.PersonId} unknown; nothing applied.");
                diagnostics.AddRange(result.Reasons);
                return result;
            }
            if (person.deathDayIndex >= 0)
            {
                result.Refused = true;
                result.Reasons.Add($"ArrivalSettlementDriver: P{record.PersonId} is deceased; nothing applied.");
                diagnostics.AddRange(result.Reasons);
                return result;
            }
            result.HouseholdId = record.HouseholdId;

            int activeHouseholdId = memberships.GetActiveHouseholdId(record.PersonId);
            if (activeHouseholdId == record.HouseholdId)
            {
                result.AlreadyApplied = true;
                result.Reasons.Add($"ArrivalSettlementDriver: P{record.PersonId} already holds an active membership in H{record.HouseholdId}; nothing duplicated.");
                diagnostics.AddRange(result.Reasons);
                ApplyEmploymentIfNeeded(result, employments, record, dayIndex, roleDisplayName, compensation, diagnostics);
                return result;
            }
            if (activeHouseholdId >= 0)
            {
                result.Refused = true;
                result.Reasons.Add($"ArrivalSettlementDriver: P{record.PersonId} already holds an active membership in H{activeHouseholdId}, not H{record.HouseholdId}; refusing rather than silently transferring.");
                diagnostics.AddRange(result.Reasons);
                return result;
            }

            bool createdHousehold = false;
            HouseholdState household = population.GetHousehold(record.HouseholdId);
            if (household == null)
            {
                if (!record.IsNewHousehold)
                {
                    result.Refused = true;
                    result.Reasons.Add($"ArrivalSettlementDriver: existing household H{record.HouseholdId} does not exist; nothing applied.");
                    diagnostics.AddRange(result.Reasons);
                    return result;
                }
                household = new HouseholdState
                {
                    id = record.HouseholdId,
                    householdName = $"Household {record.HouseholdId}",
                    surname = person.lastName ?? string.Empty,
                    lifecycleState = HouseholdLifecycleState.Active,
                    entityId = EntityId.For(EntityKind.Household, record.HouseholdId),
                };
                population.households.Add(household);
                createdHousehold = true;
                result.HouseholdCreated = true;
            }
            if (household.lifecycleState == HouseholdLifecycleState.Dissolved)
            {
                result.Refused = true;
                result.Reasons.Add($"ArrivalSettlementDriver: household H{record.HouseholdId} is dissolved; nothing applied.");
                diagnostics.AddRange(result.Reasons);
                return result;
            }

            string rejection = lifecycle.AddMember(
                record.HouseholdId, record.PersonId, dayIndex,
                HouseholdMembershipSource.Authored, "arrival settlement");
            if (rejection != null)
            {
                if (createdHousehold)
                {
                    population.households.Remove(household);
                    result.HouseholdCreated = false;
                }
                result.Refused = true;
                result.Reasons.Add($"ArrivalSettlementDriver: membership join rejected ({rejection}); nothing applied.");
                diagnostics.AddRange(result.Reasons);
                return result;
            }

            result.Applied = true;
            result.Reasons.Add(
                $"ArrivalSettlementDriver: P{record.PersonId} joined H{record.HouseholdId}" +
                (createdHousehold ? " (household created with the record's pre-allocated id)" : string.Empty) +
                $"; housing unit '{record.HousingUnitId}' still needs a residence assignment (Codex, HousingResidence runtime).");
            diagnostics.AddRange(result.Reasons);
            ApplyEmploymentIfNeeded(result, employments, record, dayIndex, roleDisplayName, compensation, diagnostics);
            return result;
        }

        private static void ApplyEmploymentIfNeeded(
            ArrivalSettlementApplication result,
            EmploymentRelationshipRegistry employments,
            ArrivalSettlementRecord record,
            int dayIndex,
            string roleDisplayName,
            CompensationTerms? compensation,
            List<string> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(record.JobBusinessId))
            {
                result.EmploymentPending = record.UnemployedWithPlan;
                if (record.UnemployedWithPlan)
                {
                    diagnostics.Add($"ArrivalSettlementDriver: P{record.PersonId} settled unemployed with an explicit job-seeking plan; no employment created.");
                }
                return;
            }
            if (employments == null)
            {
                result.EmploymentPending = true;
                diagnostics.Add($"ArrivalSettlementDriver: no employment registry supplied; job at '{record.JobBusinessId}' for P{record.PersonId} is pending, not created.");
                return;
            }
            foreach (EmploymentRelationship existing in employments.GetActiveByEmployee(record.PersonId))
            {
                if (existing != null && string.Equals(existing.EmployerBusinessId, record.JobBusinessId, StringComparison.Ordinal))
                {
                    diagnostics.Add($"ArrivalSettlementDriver: P{record.PersonId} already actively employed at '{record.JobBusinessId}'; no duplicate employment.");
                    return;
                }
            }
            if (compensation == null)
            {
                result.EmploymentPending = true;
                diagnostics.Add($"ArrivalSettlementDriver: no compensation terms supplied; job at '{record.JobBusinessId}' for P{record.PersonId} is pending, not invented.");
                return;
            }
            var relationship = new EmploymentRelationship
            {
                Id = $"arrival-{record.PersonId}-{record.JobBusinessId}-{Math.Max(0, dayIndex)}",
                EmployeePersonId = record.PersonId,
                EmployerBusinessId = record.JobBusinessId,
                RoleDisplayName = roleDisplayName ?? string.Empty,
                Kind = EmploymentKind.Permanent,
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = compensation.Value,
                StartDayIndex = Math.Max(0, dayIndex),
                EndDayIndex = -1,
                Source = EmploymentSource.Authored,
                LegacyNotes = "arrival settlement",
            };
            string rejection = employments.Register(relationship);
            if (rejection != null)
            {
                result.EmploymentPending = true;
                diagnostics.Add($"ArrivalSettlementDriver: employment registration rejected ({rejection}); job pending, not duplicated.");
                return;
            }
            result.EmploymentCreated = true;
            diagnostics.Add($"ArrivalSettlementDriver: employment '{relationship.Id}' created for P{record.PersonId} at '{record.JobBusinessId}'.");
        }
    }
}
