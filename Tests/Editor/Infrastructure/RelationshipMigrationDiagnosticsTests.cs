using System.Collections.Generic;
using LandLedgers.Persistence;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Infrastructure
{
    /// <summary>
    /// PKG-3 (PL-23): deterministic legacy relationship-migration diagnostics with quarantine
    /// (3A-D27 household table, 3A-D28 employment table). Contradictions are quarantined with
    /// diagnostics, never silently merged.
    /// </summary>
    [TestFixture]
    public sealed class RelationshipMigrationDiagnosticsTests
    {
        private static LegacyPersonClaim Person(int id, int householdId, int workplaceBuildingId = -1, int wageCents = 0)
        {
            return new LegacyPersonClaim
            {
                PersonId = id,
                DisplayName = $"Person {id}",
                HouseholdId = householdId,
                PersonWageCents = wageCents,
                WorkplaceBuildingId = workplaceBuildingId,
            };
        }

        private static LegacyHouseholdClaim Household(int id, params int[] memberIds)
        {
            return new LegacyHouseholdClaim
            {
                HouseholdId = id,
                MemberIds = new List<int>(memberIds),
            };
        }

        private static LegacySlotClaim Slot(
            string businessId,
            int buildingId,
            string slotId,
            string workerId,
            int wageCents,
            bool paidActive = true,
            bool suspended = false)
        {
            return new LegacySlotClaim
            {
                BusinessId = businessId,
                BusinessBuildingId = buildingId,
                SlotId = slotId,
                AssignedWorkerId = workerId,
                AssignedWorkerDisplayName = $"Worker {workerId}",
                WeeklyWageCents = wageCents,
                PaidActive = paidActive,
                SuspendedForMissedPayroll = suspended,
                RequiredForOpening = false,
            };
        }

        [Test]
        public void Household_MutualAgreement_IsCorroborated()
        {
            var persons = new List<LegacyPersonClaim> { Person(1, 10) };
            var households = new List<LegacyHouseholdClaim> { Household(10, 1) };

            RelationshipMigrationAuditReport report = RelationshipMigrationAuditor.AuditHouseholds(persons, households);

            Assert.AreEqual(1, report.CountByOutcome(MigrationDiagnosticOutcome.Corroborated));
            Assert.IsFalse(report.HasQuarantines);
        }

        [Test]
        public void Household_NoPointer_MultipleLists_IsQuarantined()
        {
            var persons = new List<LegacyPersonClaim> { Person(1, -1) };
            var households = new List<LegacyHouseholdClaim> { Household(10, 1), Household(11, 1) };

            RelationshipMigrationAuditReport report = RelationshipMigrationAuditor.AuditHouseholds(persons, households);

            Assert.IsTrue(report.HasQuarantines);
            Assert.AreEqual(1, report.Quarantines.Count);
            Assert.AreEqual(2, report.Quarantines[0].ConflictingClaims.Count);
            Assert.AreEqual(1, report.CountByOutcome(MigrationDiagnosticOutcome.Quarantined));
        }

        [Test]
        public void Household_PersonPointsToH1_WhileH2ListsPerson_PointerWins()
        {
            var persons = new List<LegacyPersonClaim> { Person(1, 10) };
            var households = new List<LegacyHouseholdClaim> { Household(10), Household(11, 1) };

            RelationshipMigrationAuditReport report = RelationshipMigrationAuditor.AuditHouseholds(persons, households);

            Assert.IsFalse(report.HasQuarantines);
            Assert.AreEqual(1, report.CountByOutcome(MigrationDiagnosticOutcome.ConflictResolved));
            Assert.AreEqual("H10", report.Diagnostics[0].CounterpartyId);
        }

        [Test]
        public void Household_PersonPointsToMissingHousehold_NoMembershipCreated()
        {
            var persons = new List<LegacyPersonClaim> { Person(1, 99) };
            var households = new List<LegacyHouseholdClaim> { Household(10, 1) };

            RelationshipMigrationAuditReport report = RelationshipMigrationAuditor.AuditHouseholds(persons, households);

            Assert.IsFalse(report.HasQuarantines);
            Assert.AreEqual(1, report.CountByOutcome(MigrationDiagnosticOutcome.Informational));
            Assert.IsTrue(report.Diagnostics[0].Decision.Contains("No active membership"));
        }

        [Test]
        public void Household_MissingPersonReference_IsDiagnosed_NotManufactured()
        {
            var persons = new List<LegacyPersonClaim> { Person(1, 10) };
            var households = new List<LegacyHouseholdClaim> { Household(10, 1, 777) };

            RelationshipMigrationAuditReport report = RelationshipMigrationAuditor.AuditHouseholds(persons, households);

            Assert.IsFalse(report.HasQuarantines);
            Assert.IsTrue(report.Diagnostics.Exists(d => d.PersonId == 777 && d.Decision.Contains("not manufactured")));
        }

        [Test]
        public void Employment_ReciprocalWageMatch_IsCorroborated()
        {
            var persons = new List<LegacyPersonClaim> { Person(1, 10, workplaceBuildingId: 100, wageCents: 1200) };
            var slots = new List<LegacySlotClaim> { Slot("biz-a", 100, "clerk", "1", 1200) };

            RelationshipMigrationAuditReport report = RelationshipMigrationAuditor.AuditEmployment(persons, slots);

            Assert.AreEqual(1, report.CountByOutcome(MigrationDiagnosticOutcome.Corroborated));
            Assert.IsFalse(report.HasQuarantines);
        }

        [Test]
        public void Employment_WageConflict_SlotWageWins()
        {
            var persons = new List<LegacyPersonClaim> { Person(1, 10, workplaceBuildingId: 100, wageCents: 900) };
            var slots = new List<LegacySlotClaim> { Slot("biz-a", 100, "clerk", "1", 1200) };

            RelationshipMigrationAuditReport report = RelationshipMigrationAuditor.AuditEmployment(persons, slots);

            Assert.IsFalse(report.HasQuarantines);
            Assert.AreEqual(1, report.CountByOutcome(MigrationDiagnosticOutcome.ConflictResolved));
            Assert.IsTrue(report.Diagnostics[0].Decision.Contains("Slot wage wins"));
        }

        [Test]
        public void Employment_PersonInTwoBusinesses_NoMutualCorroboration_IsQuarantined()
        {
            var persons = new List<LegacyPersonClaim> { Person(1, 10, workplaceBuildingId: 300, wageCents: 1200) };
            var slots = new List<LegacySlotClaim>
            {
                Slot("biz-a", 100, "clerk", "1", 1200),
                Slot("biz-b", 200, "clerk", "1", 1200),
            };

            RelationshipMigrationAuditReport report = RelationshipMigrationAuditor.AuditEmployment(persons, slots);

            Assert.IsTrue(report.HasQuarantines);
            Assert.AreEqual(1, report.Quarantines.Count);
            Assert.AreEqual(2, report.Quarantines[0].ConflictingClaims.Count);
        }

        [Test]
        public void Employment_DuplicateSlotsSameBusiness_NoCorroboratedSlot_IsQuarantined()
        {
            var persons = new List<LegacyPersonClaim> { Person(1, 10, workplaceBuildingId: -1, wageCents: 1200) };
            var slots = new List<LegacySlotClaim>
            {
                Slot("biz-a", 100, "clerk", "1", 1200),
                Slot("biz-a", 100, "porter", "1", 1200),
            };

            RelationshipMigrationAuditReport report = RelationshipMigrationAuditor.AuditEmployment(persons, slots);

            Assert.IsTrue(report.HasQuarantines);
            Assert.IsTrue(report.Quarantines[0].Reason.Contains("no double-pay"));
        }

        [Test]
        public void Employment_SyntheticOwnerWorker_IsCompatibilityPath_NotPerson()
        {
            var persons = new List<LegacyPersonClaim> { Person(1, 10) };
            var slots = new List<LegacySlotClaim> { Slot("biz-a", 100, "manager", "owner:1", 0) };

            RelationshipMigrationAuditReport report = RelationshipMigrationAuditor.AuditEmployment(persons, slots);

            Assert.IsFalse(report.HasQuarantines);
            Assert.IsTrue(report.Diagnostics.Exists(d => d.Decision.Contains("do not become fake Persons") || d.Details.Contains("fake Persons")));
        }

        [Test]
        public void Employment_SuspendedForMissedPayroll_MapsToSuspendedState()
        {
            var persons = new List<LegacyPersonClaim> { Person(1, 10, workplaceBuildingId: 100, wageCents: 1200) };
            var slots = new List<LegacySlotClaim> { Slot("biz-a", 100, "clerk", "1", 1200, paidActive: false, suspended: true) };

            RelationshipMigrationAuditReport report = RelationshipMigrationAuditor.AuditEmployment(persons, slots);

            Assert.IsTrue(report.Diagnostics.Exists(d => d.Decision.Contains("EmployerPaymentDefault")));
        }

        [Test]
        public void Audit_IsDeterministic_AcrossRuns()
        {
            var persons = new List<LegacyPersonClaim>
            {
                Person(2, -1),
                Person(1, 10, workplaceBuildingId: 100, wageCents: 900),
            };
            var households = new List<LegacyHouseholdClaim> { Household(11, 2), Household(12, 2), Household(10, 1) };
            var slots = new List<LegacySlotClaim> { Slot("biz-a", 100, "clerk", "1", 1200) };

            RelationshipMigrationAuditReport firstHouseholds = RelationshipMigrationAuditor.AuditHouseholds(persons, households);
            RelationshipMigrationAuditReport secondHouseholds = RelationshipMigrationAuditor.AuditHouseholds(persons, households);
            RelationshipMigrationAuditReport firstEmployment = RelationshipMigrationAuditor.AuditEmployment(persons, slots);
            RelationshipMigrationAuditReport secondEmployment = RelationshipMigrationAuditor.AuditEmployment(persons, slots);

            Assert.AreEqual(firstHouseholds.BuildSummary(), secondHouseholds.BuildSummary());
            Assert.AreEqual(firstEmployment.BuildSummary(), secondEmployment.BuildSummary());
        }
    }
}
