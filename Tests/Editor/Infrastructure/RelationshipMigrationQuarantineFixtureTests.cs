using System.Collections.Generic;
using LandLedgers.Persistence;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Infrastructure
{
    /// <summary>
    /// PKG-4 (PL-51, Phase-0): regression fixtures for the relationship-migration quarantine
    /// (PKG-3 / 3A-D27/D28). Locks in: quarantines are never silently merged, quarantined
    /// claims produce no active relationship decisions, and the audit is deterministic.
    /// </summary>
    [TestFixture]
    public sealed class RelationshipMigrationQuarantineFixtureTests
    {
        [Test]
        public void Quarantine_AlwaysPreservesMultipleConflictingClaims()
        {
            var persons = new List<LegacyPersonClaim>
            {
                new LegacyPersonClaim { PersonId = 1, DisplayName = "P1", HouseholdId = -1, WorkplaceBuildingId = -1 },
            };
            var households = new List<LegacyHouseholdClaim>
            {
                new LegacyHouseholdClaim { HouseholdId = 10, MemberIds = new List<int> { 1 } },
                new LegacyHouseholdClaim { HouseholdId = 11, MemberIds = new List<int> { 1 } },
            };

            RelationshipMigrationAuditReport report = RelationshipMigrationAuditor.AuditHouseholds(persons, households);

            Assert.IsTrue(report.HasQuarantines);
            for (int i = 0; i < report.Quarantines.Count; i++)
            {
                // A quarantine with a single claim would be pointless: the contradiction is the content.
                Assert.GreaterOrEqual(report.Quarantines[i].ConflictingClaims.Count, 2);
                Assert.IsFalse(string.IsNullOrWhiteSpace(report.Quarantines[i].RuleReference));
            }
        }

        [Test]
        public void QuarantinedPerson_ReceivesNoActiveRelationshipDecision()
        {
            var persons = new List<LegacyPersonClaim>
            {
                new LegacyPersonClaim { PersonId = 1, DisplayName = "P1", HouseholdId = -1, WorkplaceBuildingId = 300, PersonWageCents = 1200 },
            };
            var slots = new List<LegacySlotClaim>
            {
                new LegacySlotClaim { BusinessId = "biz-a", BusinessBuildingId = 100, SlotId = "clerk", AssignedWorkerId = "1", WeeklyWageCents = 1200, PaidActive = true },
                new LegacySlotClaim { BusinessId = "biz-b", BusinessBuildingId = 200, SlotId = "clerk", AssignedWorkerId = "1", WeeklyWageCents = 1200, PaidActive = true },
            };

            RelationshipMigrationAuditReport report = RelationshipMigrationAuditor.AuditEmployment(persons, slots);

            Assert.IsTrue(report.HasQuarantines);
            for (int i = 0; i < report.Diagnostics.Count; i++)
            {
                RelationshipMigrationDiagnostic diagnostic = report.Diagnostics[i];
                if (diagnostic.PersonId != 1 || diagnostic.Category != RelationshipMigrationCategory.Employment)
                {
                    continue;
                }

                // The quarantined person's claims must never resolve to an active relationship.
                Assert.AreNotEqual(MigrationDiagnosticOutcome.Corroborated, diagnostic.Outcome);
                Assert.AreNotEqual(MigrationDiagnosticOutcome.ConflictResolved, diagnostic.Outcome);
                Assert.AreNotEqual(MigrationDiagnosticOutcome.OneSidedClaim, diagnostic.Outcome);
            }
        }

        [Test]
        public void Quarantine_IsDeterministic_AcrossRuns()
        {
            var persons = new List<LegacyPersonClaim>
            {
                new LegacyPersonClaim { PersonId = 2, DisplayName = "P2", HouseholdId = -1, WorkplaceBuildingId = -1 },
                new LegacyPersonClaim { PersonId = 1, DisplayName = "P1", HouseholdId = 10, WorkplaceBuildingId = 100, PersonWageCents = 900 },
            };
            var households = new List<LegacyHouseholdClaim>
            {
                new LegacyHouseholdClaim { HouseholdId = 12, MemberIds = new List<int> { 2 } },
                new LegacyHouseholdClaim { HouseholdId = 11, MemberIds = new List<int> { 2 } },
                new LegacyHouseholdClaim { HouseholdId = 10, MemberIds = new List<int> { 1 } },
            };
            var slots = new List<LegacySlotClaim>
            {
                new LegacySlotClaim { BusinessId = "biz-a", BusinessBuildingId = 100, SlotId = "clerk", AssignedWorkerId = "1", WeeklyWageCents = 1200, PaidActive = true },
            };

            RelationshipMigrationAuditReport householdsFirst = RelationshipMigrationAuditor.AuditHouseholds(persons, households);
            RelationshipMigrationAuditReport householdsSecond = RelationshipMigrationAuditor.AuditHouseholds(persons, households);
            RelationshipMigrationAuditReport employmentFirst = RelationshipMigrationAuditor.AuditEmployment(persons, slots);
            RelationshipMigrationAuditReport employmentSecond = RelationshipMigrationAuditor.AuditEmployment(persons, slots);

            Assert.AreEqual(householdsFirst.BuildSummary(), householdsSecond.BuildSummary());
            Assert.AreEqual(employmentFirst.BuildSummary(), employmentSecond.BuildSummary());
            Assert.IsTrue(householdsFirst.HasQuarantines);
            Assert.AreEqual(
                householdsFirst.Quarantines[0].QuarantineId,
                householdsSecond.Quarantines[0].QuarantineId);
        }
    }
}
