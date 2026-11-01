using System.Collections.Generic;
using LandLedgers.Economy;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass XL phase 5 (finding R06): the two closeout legs that
    /// Outmigration.Depart deliberately did not do, mirroring exactly
    /// what MigrationDriver.MigrateOut already does for the legacy path.
    ///
    /// The gap: Outmigration.Depart flips isOffMap and records the
    /// destination, but leaves household membership and employment
    /// active. The legacy driver ends both, and its own comment warns
    /// that no payroll path checks isOffMap — so a person departed on
    /// the new channel alone would keep drawing wages and keep a live
    /// household membership while off map. This helper closes those
    /// two legs, and only those two legs:
    ///   - household membership: HouseholdLifecycleManager.RemoveMember
    ///     (ends the active membership with reason, history retained);
    ///   - employment: EmploymentRelationshipRegistry
    ///     .EndEmploymentsForDeparture (ends active employments with
    ///     EndReason.MigratedOut, history retained).
    ///
    /// What this helper does NOT do, deliberately: household
    /// dissolution, property retain/transfer, and debt retention are
    /// still the production driver's explicit decisions (phase 4
    /// finding F08, still open). The result reports them verbatim as
    /// StillRequired, so no caller can mistake this closeout for the
    /// full post-departure execution. Identity, kinship, ownership,
    /// and debts are never deleted here; Depart already preserved
    /// them and this helper deletes nothing.
    ///
    /// Idempotency: a second close of the same record ends nothing
    /// twice (no active membership remains, no active employment
    /// remains). A null lifecycle or registry skips that leg with a
    /// diagnostic instead of crashing; a record that never departed
    /// closes nothing. Plain C#, no Unity dependency, Roslyn-testable.
    /// </summary>
    public sealed class OutmigrationCloseoutResult
    {
        public bool MembershipEnded;
        public int EmploymentsEnded;
        public List<string> StillRequired = new List<string>();
        public List<string> Reasons = new List<string>();
    }

    public static class OutmigrationCloseout
    {
        public static OutmigrationCloseoutResult CloseAfterDeparture(
            PopulationState population,
            HouseholdLifecycleManager lifecycle,
            EmploymentRelationshipRegistry employments,
            OutmigrationRecord record,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            var result = new OutmigrationCloseoutResult();
            if (population == null || record == null)
            {
                result.Reasons.Add("OutmigrationCloseout: null population or record; nothing closed.");
                diagnostics.AddRange(result.Reasons);
                return result;
            }
            if (!record.Departed)
            {
                result.Reasons.Add($"OutmigrationCloseout: P{record.PersonId} has no departure record; nothing closed.");
                diagnostics.AddRange(result.Reasons);
                return result;
            }
            PersonState person = population.GetPerson(record.PersonId);
            if (person == null)
            {
                result.Reasons.Add($"OutmigrationCloseout: P{record.PersonId} unknown; nothing closed.");
                diagnostics.AddRange(result.Reasons);
                return result;
            }
            if (!person.isOffMap)
            {
                result.Reasons.Add($"OutmigrationCloseout: P{record.PersonId} is on map despite a departure record; refusing to close membership/employment for a resident.");
                diagnostics.AddRange(result.Reasons);
                return result;
            }

            if (lifecycle != null)
            {
                result.MembershipEnded = lifecycle.RemoveMember(
                    record.PersonId, dayIndex, "migrated off-map (outmigration)");
                result.Reasons.Add(result.MembershipEnded
                    ? $"OutmigrationCloseout: household membership ended for P{record.PersonId}."
                    : $"OutmigrationCloseout: P{record.PersonId} held no active household membership to end.");
            }
            else
            {
                result.Reasons.Add("OutmigrationCloseout: no lifecycle manager supplied; household membership NOT closed.");
            }

            if (employments != null)
            {
                result.EmploymentsEnded = employments.EndEmploymentsForDeparture(record.PersonId, dayIndex);
                result.Reasons.Add(result.EmploymentsEnded > 0
                    ? $"OutmigrationCloseout: ended {result.EmploymentsEnded} active employment(s) for P{record.PersonId} (MigratedOut, history retained)."
                    : $"OutmigrationCloseout: P{record.PersonId} held no active employment to end.");
            }
            else
            {
                result.Reasons.Add("OutmigrationCloseout: no employment registry supplied; employments NOT closed.");
            }

            result.StillRequired = new List<string>(record.PostDepartureHandlingRequired);
            result.Reasons.Add(
                "OutmigrationCloseout: household dissolution, property retain/transfer, and debt retention are STILL REQUIRED " +
                "(phase 4 finding F08); this closeout covers membership and employment only.");
            diagnostics.AddRange(result.Reasons);
            return result;
        }
    }
}
