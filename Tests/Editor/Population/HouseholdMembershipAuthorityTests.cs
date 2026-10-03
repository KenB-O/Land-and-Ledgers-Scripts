using System.Collections.Generic;
using LandLedgers.Persistence;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// PKG-8 (PL-22): HouseholdMembership as the sole household-membership authority (3A-D14).
    /// At most one Active membership per person; member lists are derived reverse indexes;
    /// legacy projection follows the 3A-D27 decision table with quarantine preserved.
    /// </summary>
    [TestFixture]
    public sealed class HouseholdMembershipAuthorityTests
    {
        private static HouseholdMembership ActiveMembership(int membershipId, int personId, int householdId)
        {
            return new HouseholdMembership
            {
                MembershipId = membershipId,
                PersonId = personId,
                HouseholdId = householdId,
                Lifecycle = HouseholdMembershipLifecycle.Active,
                StartDayIndex = 0,
                Source = HouseholdMembershipSource.Authored,
            };
        }

        [Test]
        public void Registry_EnforcesAtMostOneActiveMembership()
        {
            var registry = new HouseholdMembershipRegistry();

            Assert.IsNull(registry.Register(ActiveMembership(0, 1, 10)));
            string diagnostic = registry.Register(ActiveMembership(1, 1, 11));

            Assert.IsNotNull(diagnostic);
            Assert.AreEqual(10, registry.GetActiveHouseholdId(1));
            Assert.AreEqual(1, registry.ActiveCount);
            Assert.AreEqual(1, registry.Diagnostics.Count);
        }

        [Test]
        public void Registry_TransferMembership_EndsOldAndRegistersNew()
        {
            var registry = new HouseholdMembershipRegistry();
            registry.Register(ActiveMembership(0, 1, 10));

            Assert.IsNull(registry.TransferMembership(1, 11, startDayIndex: 50, nextMembershipId: 1));

            Assert.AreEqual(11, registry.GetActiveHouseholdId(1));
            HouseholdMembership current = registry.GetActiveMembership(1);
            Assert.AreEqual(1, current.MembershipId);
            Assert.AreEqual(50, current.StartDayIndex);
        }

        [Test]
        public void Registry_EndMembership_AllowsReRegistration()
        {
            var registry = new HouseholdMembershipRegistry();
            registry.Register(ActiveMembership(0, 1, 10));

            Assert.IsTrue(registry.EndMembership(1, endDayIndex: 50, reason: "moved out"));
            Assert.AreEqual(-1, registry.GetActiveHouseholdId(1));
            Assert.IsNull(registry.Register(ActiveMembership(1, 1, 12)));
            Assert.AreEqual(12, registry.GetActiveHouseholdId(1));
        }

        [Test]
        public void Registry_GetActiveMembers_IsDerivedReverseIndex()
        {
            var registry = new HouseholdMembershipRegistry();
            registry.Register(ActiveMembership(0, 1, 10));
            registry.Register(ActiveMembership(1, 2, 10));
            registry.Register(ActiveMembership(2, 3, 11));

            List<int> members = registry.GetActiveMembers(10);

            Assert.AreEqual(2, members.Count);
            Assert.IsTrue(members.Contains(1));
            Assert.IsTrue(members.Contains(2));
            Assert.IsFalse(members.Contains(3));
        }

        [Test]
        public void Projector_CreatesMemberships_ForCorroboratedClaims()
        {
            var persons = new List<LegacyPersonClaim>
            {
                new LegacyPersonClaim { PersonId = 1, DisplayName = "P1", HouseholdId = 10, WorkplaceBuildingId = -1 },
                new LegacyPersonClaim { PersonId = 2, DisplayName = "P2", HouseholdId = -1, WorkplaceBuildingId = -1 },
            };
            var households = new List<LegacyHouseholdClaim>
            {
                new LegacyHouseholdClaim { HouseholdId = 10, MemberIds = new List<int> { 1 } },
                new LegacyHouseholdClaim { HouseholdId = 11, MemberIds = new List<int> { 2 } },
            };

            HouseholdMembershipProjectionResult result =
                HouseholdMembershipProjector.ProjectFromLegacy(persons, households, startDayIndex: 0);

            Assert.AreEqual(2, result.Memberships.Count);
            Assert.AreEqual(0, result.Quarantines.Count);
            Assert.AreEqual(HouseholdMembershipSource.LegacyMigration, result.Memberships[0].Source);
        }

        [Test]
        public void Projector_QuarantinedClaims_ProduceNoMembership()
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

            HouseholdMembershipProjectionResult result =
                HouseholdMembershipProjector.ProjectFromLegacy(persons, households, startDayIndex: 0);

            Assert.AreEqual(0, result.Memberships.Count);
            Assert.AreEqual(1, result.Quarantines.Count);
            Assert.AreEqual(2, result.Quarantines[0].ConflictingClaims.Count);
        }

        [Test]
        public void Projector_MembershipIds_AreDeterministic()
        {
            var persons = new List<LegacyPersonClaim>
            {
                new LegacyPersonClaim { PersonId = 2, DisplayName = "P2", HouseholdId = 10, WorkplaceBuildingId = -1 },
                new LegacyPersonClaim { PersonId = 1, DisplayName = "P1", HouseholdId = 10, WorkplaceBuildingId = -1 },
            };
            var households = new List<LegacyHouseholdClaim>
            {
                new LegacyHouseholdClaim { HouseholdId = 10, MemberIds = new List<int> { 1, 2 } },
            };

            HouseholdMembershipProjectionResult first =
                HouseholdMembershipProjector.ProjectFromLegacy(persons, households, startDayIndex: 0);
            HouseholdMembershipProjectionResult second =
                HouseholdMembershipProjector.ProjectFromLegacy(persons, households, startDayIndex: 0);

            Assert.AreEqual(2, first.Memberships.Count);
            for (int i = 0; i < first.Memberships.Count; i++)
            {
                Assert.AreEqual(first.Memberships[i].MembershipId, second.Memberships[i].MembershipId);
                Assert.AreEqual(first.Memberships[i].PersonId, second.Memberships[i].PersonId);
                Assert.AreEqual(first.Memberships[i].HouseholdId, second.Memberships[i].HouseholdId);
            }
        }
    }
}
