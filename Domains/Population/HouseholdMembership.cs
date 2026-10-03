using System;
using System.Collections.Generic;
using LandLedgers.Persistence;

namespace LandLedgers.Population
{
    /// <summary>
    /// Membership lifecycle. At most one Active membership per person (3A-D14); Transitional
    /// covers households migrating with economic/estate state but no current members.
    /// </summary>
    public enum HouseholdMembershipLifecycle
    {
        Active = 0,
        Transitional = 1,
        Ended = 2,
    }

    /// <summary>Where a membership record came from.</summary>
    public enum HouseholdMembershipSource
    {
        Authored = 0,
        LegacyMigration = 1,
        Manual = 2,
    }

    /// <summary>
    /// PKG-8 (PL-22): HouseholdMembership is the SOLE household-membership authority (3A-D14).
    /// Replaces the independently-persisted Person.householdId / Household.memberIds dual
    /// writes, which were manually synchronized competing truths (3A-D07: "Remove reciprocal
    /// persisted competing truth"). Reverse indexes (household -> members) are DERIVED from
    /// this registry, never separately persisted.
    /// </summary>
    [Serializable]
    public sealed class HouseholdMembership
    {
        public int MembershipId = -1;
        public int PersonId = -1;
        public int HouseholdId = -1;
        public HouseholdMembershipLifecycle Lifecycle = HouseholdMembershipLifecycle.Active;
        public int StartDayIndex = -1;
        public int EndDayIndex = -1; // -1 = open-ended
        public HouseholdMembershipSource Source = HouseholdMembershipSource.Authored;
        public string LegacyDiagnostic = string.Empty;

        public bool IsActive => Lifecycle == HouseholdMembershipLifecycle.Active;
    }

    /// <summary>
    /// The sole household-membership authority. Enforces at most one Active membership per
    /// person; conflicting registrations are rejected deterministically (first wins) with a
    /// diagnostic - never silently overwritten. Member lists are derived reverse indexes
    /// (3A-D07), rebuilt from this registry.
    /// </summary>
    public sealed class HouseholdMembershipRegistry
    {
        private readonly Dictionary<int, HouseholdMembership> activeByPerson = new Dictionary<int, HouseholdMembership>();
        private readonly Dictionary<int, HouseholdMembership> byMembershipId = new Dictionary<int, HouseholdMembership>();
        private readonly List<HouseholdMembership> ended = new List<HouseholdMembership>();
        private readonly List<string> diagnostics = new List<string>();

        /// <summary>
        /// Registers a membership. A second Active membership for the same person is rejected
        /// (first wins) with a diagnostic; end the existing one first or use TransferMembership.
        /// </summary>
        public string Register(HouseholdMembership membership)
        {
            if (membership == null || membership.PersonId < 0 || membership.HouseholdId < 0)
            {
                string diagnostic = "Rejected household membership registration: null or invalid person/household id.";
                diagnostics.Add(diagnostic);
                return diagnostic;
            }

            if (byMembershipId.ContainsKey(membership.MembershipId))
            {
                string diagnostic =
                    $"Duplicate membership id rejected: {membership.MembershipId} (first registration wins).";
                diagnostics.Add(diagnostic);
                return diagnostic;
            }

            if (membership.IsActive && activeByPerson.TryGetValue(membership.PersonId, out HouseholdMembership existing))
            {
                string diagnostic =
                    $"Rejected second Active membership for P{membership.PersonId}: already active in " +
                    $"H{existing.HouseholdId} (membership {existing.MembershipId}). End it first or transfer. " +
                    "At most one Active membership per person (3A-D14).";
                diagnostics.Add(diagnostic);
                return diagnostic;
            }

            byMembershipId[membership.MembershipId] = membership;
            if (membership.IsActive)
            {
                activeByPerson[membership.PersonId] = membership;
            }
            else
            {
                ended.Add(membership);
            }

            return null;
        }

        /// <summary>Ends a person's active membership (move-out, split, dissolve).</summary>
        public bool EndMembership(int personId, int endDayIndex, string reason)
        {
            if (!activeByPerson.TryGetValue(personId, out HouseholdMembership membership))
            {
                return false;
            }

            membership.Lifecycle = HouseholdMembershipLifecycle.Ended;
            membership.EndDayIndex = endDayIndex;
            membership.LegacyDiagnostic = string.IsNullOrWhiteSpace(membership.LegacyDiagnostic)
                ? reason ?? string.Empty
                : $"{membership.LegacyDiagnostic} | Ended: {reason}";
            activeByPerson.Remove(personId);
            ended.Add(membership);
            return true;
        }

        /// <summary>Atomic household transfer: ends the current active membership and registers the new one.</summary>
        public string TransferMembership(int personId, int toHouseholdId, int startDayIndex, int nextMembershipId)
        {
            EndMembership(personId, startDayIndex, $"Transferred to H{toHouseholdId}");
            return Register(new HouseholdMembership
            {
                MembershipId = nextMembershipId,
                PersonId = personId,
                HouseholdId = toHouseholdId,
                Lifecycle = HouseholdMembershipLifecycle.Active,
                StartDayIndex = startDayIndex,
                EndDayIndex = -1,
                Source = HouseholdMembershipSource.Manual,
            });
        }

        /// <summary>Sole authority read: which household is this person actively in?</summary>
        public HouseholdMembership GetActiveMembership(int personId)
        {
            return activeByPerson.TryGetValue(personId, out HouseholdMembership membership) ? membership : null;
        }

        public int GetActiveHouseholdId(int personId)
        {
            HouseholdMembership membership = GetActiveMembership(personId);
            return membership != null ? membership.HouseholdId : -1;
        }

        /// <summary>
        /// Derived reverse index (3A-D07): current members of a household, rebuilt from the
        /// registry. Never persisted separately.
        /// </summary>
        public List<int> GetActiveMembers(int householdId)
        {
            var members = new List<int>();
            foreach (KeyValuePair<int, HouseholdMembership> entry in activeByPerson)
            {
                if (entry.Value.HouseholdId == householdId)
                {
                    members.Add(entry.Key);
                }
            }

            members.Sort();
            return members;
        }

        public int ActiveCount => activeByPerson.Count;

        public IReadOnlyList<string> Diagnostics => diagnostics;
    }

    /// <summary>
    /// Result of projecting legacy household copies into HouseholdMembership records.
    /// </summary>
    [Serializable]
    public sealed class HouseholdMembershipProjectionResult
    {
        public List<HouseholdMembership> Memberships = new List<HouseholdMembership>();
        public List<MigrationQuarantineRecord> Quarantines = new List<MigrationQuarantineRecord>();
        public List<string> Diagnostics = new List<string>();
    }

    /// <summary>
    /// Projects legacy Person.householdId / Household.memberIds copies into HouseholdMembership
    /// records using the 3A-D27 decision table (via <see cref="RelationshipMigrationAuditor"/>).
    /// Corroborated, one-sided and conflict-resolved claims become Active memberships;
    /// quarantined contradictions produce NO membership and preserve their quarantine records.
    /// Membership ids are assigned deterministically in audit order.
    /// </summary>
    public static class HouseholdMembershipProjector
    {
        public static HouseholdMembershipProjectionResult ProjectFromLegacy(
            IReadOnlyList<LegacyPersonClaim> persons,
            IReadOnlyList<LegacyHouseholdClaim> households,
            int startDayIndex)
        {
            var result = new HouseholdMembershipProjectionResult();
            RelationshipMigrationAuditReport audit =
                RelationshipMigrationAuditor.AuditHouseholds(persons, households);

            result.Quarantines.AddRange(audit.Quarantines);

            int nextMembershipId = 0;
            for (int i = 0; i < audit.Diagnostics.Count; i++)
            {
                RelationshipMigrationDiagnostic diagnostic = audit.Diagnostics[i];
                if (diagnostic.Category != RelationshipMigrationCategory.Household)
                {
                    continue;
                }

                switch (diagnostic.Outcome)
                {
                    case MigrationDiagnosticOutcome.Corroborated:
                    case MigrationDiagnosticOutcome.OneSidedClaim:
                    case MigrationDiagnosticOutcome.ConflictResolved:
                        if (TryParseHouseholdCounterparty(diagnostic.CounterpartyId, out int householdId))
                        {
                            result.Memberships.Add(new HouseholdMembership
                            {
                                MembershipId = nextMembershipId++,
                                PersonId = diagnostic.PersonId,
                                HouseholdId = householdId,
                                Lifecycle = HouseholdMembershipLifecycle.Active,
                                StartDayIndex = startDayIndex,
                                EndDayIndex = -1,
                                Source = HouseholdMembershipSource.LegacyMigration,
                                LegacyDiagnostic = $"{diagnostic.RuleReference}: {diagnostic.Decision}",
                            });
                        }
                        else
                        {
                            result.Diagnostics.Add(
                                $"P{diagnostic.PersonId}: {diagnostic.Outcome} claim has unparseable counterparty " +
                                $"'{diagnostic.CounterpartyId}'; no membership created.");
                        }

                        break;

                    case MigrationDiagnosticOutcome.Quarantined:
                        // No Active membership until migration repair; quarantine record preserved above.
                        result.Diagnostics.Add(
                            $"P{diagnostic.PersonId}: quarantined ({diagnostic.RuleReference}); no Active membership created.");
                        break;

                    default:
                        // Informational: valid zero-membership, missing endpoints - nothing to project.
                        break;
                }
            }

            return result;
        }

        private static bool TryParseHouseholdCounterparty(string counterpartyId, out int householdId)
        {
            householdId = -1;
            if (string.IsNullOrWhiteSpace(counterpartyId) || counterpartyId.Length < 2 || counterpartyId[0] != 'H')
            {
                return false;
            }

            return int.TryParse(counterpartyId.Substring(1), out householdId) && householdId >= 0;
        }
    }
}
