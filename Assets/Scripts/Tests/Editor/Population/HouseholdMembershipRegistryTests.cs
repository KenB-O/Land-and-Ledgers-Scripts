using System.Collections.Generic;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// Plan 01 Slice 0(a): pure-registry harness tests for
    /// <see cref="HouseholdMembershipRegistry"/> in isolation. This file
    /// references ONLY the registry - never PopulationManager,
    /// PopulationGenerator, or any GameObject/AddComponent - so it runs in
    /// the Roslyn harness (unlike HouseholdMembershipAuthorityTests.cs,
    /// which drives PopulationManager and is harness-excluded).
    ///
    /// Input data is the authored 9-person / 5-household shape exactly as
    /// PopulationManager.RebuildMembershipRegistry constructs it:
    /// person IDs 1-9, household IDs 1-5, membership IDs 0-8 in household
    /// then member order (H1: P1, P2; H2: P3, P4; H3: P5, P6; H4: P7, P8;
    /// H5: P9). These tests prove the registry is a faithful authority when
    /// fed the generation inputs; they do NOT prove generation feeds it
    /// correctly - that is the Unity Test Runner part (Slice 0(c)).
    /// </summary>
    [TestFixture]
    public sealed class HouseholdMembershipRegistryTests
    {
        // Authored households in ID order: H1..H5.
        private static readonly int[][] AuthoredHouseholds =
        {
            new[] { 1, 2 },
            new[] { 3, 4 },
            new[] { 5, 6 },
            new[] { 7, 8 },
            new[] { 9 },
        };

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

        /// <summary>Registers the authored nine onto a fresh registry, in the same order RebuildMembershipRegistry does.</summary>
        private static HouseholdMembershipRegistry BuildAuthoredNine()
        {
            var registry = new HouseholdMembershipRegistry();
            int nextMembershipId = 0;
            for (int h = 0; h < AuthoredHouseholds.Length; h++)
            {
                foreach (int personId in AuthoredHouseholds[h])
                {
                    string rejection = registry.Register(ActiveMembership(nextMembershipId++, personId, h + 1));
                    Assert.IsNull(rejection, $"P{personId} into H{h + 1} must register cleanly.");
                }
            }
            return registry;
        }

        private static int AuthoredHouseholdOf(int personId)
        {
            for (int h = 0; h < AuthoredHouseholds.Length; h++)
            {
                foreach (int memberId in AuthoredHouseholds[h])
                {
                    if (memberId == personId)
                    {
                        return h + 1;
                    }
                }
            }
            return -1;
        }

        [Test]
        public void AuthoredNine_RegisterAll_ReturnsNullAndActiveCountNine()
        {
            var registry = new HouseholdMembershipRegistry();
            int nextMembershipId = 0;
            for (int h = 0; h < AuthoredHouseholds.Length; h++)
            {
                foreach (int personId in AuthoredHouseholds[h])
                {
                    Assert.IsNull(registry.Register(ActiveMembership(nextMembershipId++, personId, h + 1)));
                }
            }

            Assert.AreEqual(9, registry.ActiveCount);
            Assert.AreEqual(0, registry.Diagnostics.Count, "No Register rejection may fire on the generation inputs.");
        }

        [Test]
        public void AuthoredNine_MembershipIds_AreZeroThroughEightInHouseholdOrder()
        {
            HouseholdMembershipRegistry registry = BuildAuthoredNine();
            int expectedMembershipId = 0;
            for (int h = 0; h < AuthoredHouseholds.Length; h++)
            {
                foreach (int personId in AuthoredHouseholds[h])
                {
                    HouseholdMembership membership = registry.GetActiveMembership(personId);
                    Assert.IsNotNull(membership, $"P{personId} must have an active membership.");
                    Assert.AreEqual(expectedMembershipId++, membership.MembershipId,
                        $"P{personId} membership id must follow household-then-member allocation order.");
                    Assert.AreEqual(h + 1, membership.HouseholdId);
                    Assert.IsTrue(membership.IsActive);
                }
            }
        }

        [Test]
        public void AuthoredNine_GetActiveMembers_ReturnsAuthoredMembersPerHousehold()
        {
            HouseholdMembershipRegistry registry = BuildAuthoredNine();

            for (int h = 0; h < AuthoredHouseholds.Length; h++)
            {
                CollectionAssert.AreEqual(AuthoredHouseholds[h], registry.GetActiveMembers(h + 1),
                    $"H{h + 1} members must be exactly the authored members.");
            }
            CollectionAssert.IsEmpty(registry.GetActiveMembers(6), "No household 6 exists in the authored town.");
        }

        [Test]
        public void AuthoredNine_GetActiveHouseholdId_ReturnsAuthoredHouseholdPerResident()
        {
            HouseholdMembershipRegistry registry = BuildAuthoredNine();

            for (int personId = 1; personId <= 9; personId++)
            {
                Assert.AreEqual(AuthoredHouseholdOf(personId), registry.GetActiveHouseholdId(personId),
                    $"P{personId} must resolve to its authored household.");
                Assert.IsNotNull(registry.GetActiveMembership(personId));
            }
            Assert.AreEqual(-1, registry.GetActiveHouseholdId(10), "No person 10 exists in the authored town.");
            Assert.IsNull(registry.GetActiveMembership(10));
        }

        [Test]
        public void Registry_EnforcesAtMostOneActiveMembership_PureForm()
        {
            // Pure-form reproduction of HouseholdMembershipAuthorityTests.
            // Registry_EnforcesAtMostOneActiveMembership (that file is
            // harness-excluded because it drives PopulationManager).
            HouseholdMembershipRegistry registry = BuildAuthoredNine();

            string diagnostic = registry.Register(ActiveMembership(9, 1, 2));

            Assert.IsNotNull(diagnostic, "A second Active membership for the same person must be rejected.");
            Assert.AreEqual(1, registry.GetActiveHouseholdId(1), "First registration wins.");
            Assert.AreEqual(9, registry.ActiveCount, "ActiveCount must not grow on a rejected registration.");
            Assert.AreEqual(1, registry.Diagnostics.Count);
            Assert.AreEqual(diagnostic, registry.Diagnostics[0]);
        }

        [Test]
        public void Registry_RejectsDuplicateMembershipId_FirstWins()
        {
            HouseholdMembershipRegistry registry = BuildAuthoredNine();

            string diagnostic = registry.Register(ActiveMembership(0, 9, 1));

            Assert.IsNotNull(diagnostic, "A reused membership id must be rejected.");
            Assert.AreEqual(5, registry.GetActiveHouseholdId(9), "P9 stays in its authored household H5.");
            Assert.AreEqual(9, registry.ActiveCount);
        }

        [Test]
        public void Registry_RejectsNullOrInvalidIds()
        {
            var registry = new HouseholdMembershipRegistry();

            Assert.IsNotNull(registry.Register(null));
            Assert.IsNotNull(registry.Register(ActiveMembership(-1, 1, 1)));
            Assert.IsNotNull(registry.Register(ActiveMembership(0, -1, 1)));
            Assert.IsNotNull(registry.Register(ActiveMembership(0, 1, -1)));
            Assert.AreEqual(0, registry.ActiveCount);
        }

        [Test]
        public void Registry_RebuildOnFreshInstance_ReproducesIdenticalActiveSet()
        {
            HouseholdMembershipRegistry first = BuildAuthoredNine();
            HouseholdMembershipRegistry second = BuildAuthoredNine();

            Assert.AreEqual(first.ActiveCount, second.ActiveCount);
            for (int personId = 1; personId <= 9; personId++)
            {
                Assert.AreEqual(first.GetActiveHouseholdId(personId), second.GetActiveHouseholdId(personId));
                Assert.AreEqual(first.GetActiveMembership(personId).MembershipId,
                    second.GetActiveMembership(personId).MembershipId);
            }
            for (int householdId = 1; householdId <= 5; householdId++)
            {
                CollectionAssert.AreEqual(first.GetActiveMembers(householdId), second.GetActiveMembers(householdId));
            }
        }

        [Test]
        public void Registry_EndMembership_AllowsReRegistrationPerContract()
        {
            HouseholdMembershipRegistry registry = BuildAuthoredNine();

            Assert.IsTrue(registry.EndMembership(1, endDayIndex: 50, reason: "moved out"));
            Assert.AreEqual(-1, registry.GetActiveHouseholdId(1));
            Assert.AreEqual(8, registry.ActiveCount);
            Assert.IsNull(registry.Register(ActiveMembership(9, 1, 2)));
            Assert.AreEqual(2, registry.GetActiveHouseholdId(1));
            Assert.AreEqual(9, registry.ActiveCount);
            CollectionAssert.AreEqual(new[] { 1, 3, 4 }, registry.GetActiveMembers(2));
        }

        [Test]
        public void Registry_PersonIdentityStable_AcrossRebuildSequences()
        {
            // Person IDs are the registry's keys: no rebuild sequence may
            // renumber, duplicate, or drop a resident.
            HouseholdMembershipRegistry registry = BuildAuthoredNine();
            registry.EndMembership(9, endDayIndex: 10, reason: "test move-out");
            Assert.IsNull(registry.Register(ActiveMembership(9, 9, 4)));

            var seen = new HashSet<int>();
            for (int personId = 1; personId <= 9; personId++)
            {
                Assert.IsTrue(registry.GetActiveMembership(personId) != null);
                Assert.IsTrue(seen.Add(personId), "Each person id must appear exactly once.");
            }
            Assert.AreEqual(9, registry.ActiveCount);
            Assert.AreEqual(9, seen.Count);

            HouseholdMembershipRegistry rebuilt = BuildAuthoredNine();
            for (int personId = 1; personId <= 9; personId++)
            {
                Assert.AreEqual(AuthoredHouseholdOf(personId), rebuilt.GetActiveHouseholdId(personId),
                    "A fresh rebuild restores the authored person-to-household mapping exactly.");
            }
        }
    }
}
