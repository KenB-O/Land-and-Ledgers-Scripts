using System.Collections.Generic;
using System.Text.RegularExpressions;
using LandLedgers.Persistence;
using LandLedgers.Population;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
        public void PopulationSaveLoad_RoundTripsLifecycleMembershipHistoryAndNextId()
        {
            GameObject sourceObject = new("Membership Save Source");
            GameObject targetObject = new("Membership Save Target");
            try
            {
                PopulationManager source = sourceObject.AddComponent<PopulationManager>();
                source.LoadFromSaveDto(new LandLedgers.Persistence.PopulationSaveDto());
                PopulationState sourceState = source.State;
                sourceState.RestorePersonIdAllocator(10);
                sourceState.RestoreHouseholdIdAllocator(20);
                for (int i = 0; i < 3; i++)
                {
                    int personId = sourceState.AllocateNextPersonId();
                    sourceState.people.Add(new PersonState
                    {
                        id = personId,
                        firstName = $"Person{personId}",
                        age = 30,
                        birthDate = new BirthDate(1850, 1, 1, BirthDateProvenance.GeneratedExact),
                        deathDayIndex = -1,
                    });
                }

                HouseholdState first = source.Lifecycle.FormHousehold(
                    "First", "Household", 10, 0, HouseholdMembershipSource.Authored);
                Assert.NotNull(first);
                Assert.IsNull(source.Lifecycle.AddMember(
                    first.id, 11, 0, HouseholdMembershipSource.Manual, "joined"));
                HouseholdState second = source.Lifecycle.FormHousehold(
                    "Second", "Household", 12, 1, HouseholdMembershipSource.Authored);
                Assert.NotNull(second);
                Assert.IsNull(source.Lifecycle.TransferMember(11, second.id, 2));
                Assert.IsTrue(source.Lifecycle.RecordDeath(10, 3, "test fixture"));
                Assert.AreEqual(-1, sourceState.GetPerson(10).householdId,
                    "Legacy Person membership fields remain projections of the active membership authority.");
                CollectionAssert.DoesNotContain(first.memberIds, 10);
                CollectionAssert.DoesNotContain(first.memberIds, 11);
                CollectionAssert.AreEqual(new[] { 11, 12 }, second.memberIds);

                PopulationSaveDto saved = source.CaptureSaveDto();
                Assert.NotNull(saved.householdMemberships);
                Assert.AreEqual(4, saved.householdMemberships.memberships.Count,
                    "The save must retain ended memberships as well as current membership.");
                Assert.IsTrue(source.Lifecycle.RecordDeath(12, 4, "post-capture mutation"));

                PopulationManager target = targetObject.AddComponent<PopulationManager>();
                target.LoadFromSaveDto(saved);

                Assert.AreEqual(-1, target.Memberships.GetActiveHouseholdId(10),
                    "The deceased Person has no active household membership.");
                Assert.AreEqual(second.id, target.Memberships.GetActiveHouseholdId(11));
                Assert.AreEqual(second.id, target.Memberships.GetActiveHouseholdId(12),
                    "The captured DTO must not alias later source mutations.");
                Assert.AreEqual(-1, target.State.GetPerson(10).householdId);
                Assert.AreEqual(second.id, target.State.GetPerson(11).householdId);
                Assert.AreEqual(4, target.Memberships.AllMemberships().Count);
                CollectionAssert.AreEqual(new[] { 11, 12 }, target.Memberships.GetActiveMembers(second.id));
                CollectionAssert.AreEqual(new[] { 11, 12 }, target.State.GetHousehold(second.id).memberIds);

                PopulationSaveDto repeatedSave = target.CaptureSaveDto();
                target.LoadFromSaveDto(repeatedSave);
                Assert.AreEqual(4, target.Memberships.AllMemberships().Count,
                    "Repeated restore must replace membership state rather than multiply records.");
                Assert.AreEqual(second.id, target.Memberships.GetActiveHouseholdId(11));

                PersonState child = target.Lifecycle.RecordBirth(
                    second.id, 11, 12, "Newborn", "Household", 5,
                    new BirthDate(1880, 1, 6, BirthDateProvenance.Exact, "Save test"));
                Assert.NotNull(child);
                HouseholdMembership childMembership = target.Memberships.GetActiveMembership(child.id);
                Assert.NotNull(childMembership);
                Assert.AreEqual(4, childMembership.MembershipId,
                    "The allocator resumes after the highest persisted membership ID.");
                Assert.AreEqual(second.id, childMembership.HouseholdId);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void PopulationSaveLoad_UsesLegacyHouseholdMembersWhenMembershipDtoIsAbsent()
        {
            GameObject root = new("Legacy Membership Save Target");
            try
            {
                var legacy = new LandLedgers.Persistence.PopulationSaveDto
                {
                    nextPersonId = 2,
                    nextHouseholdId = 8,
                };
                legacy.people.Add(new LandLedgers.Persistence.PersonSaveDto
                {
                    id = 1,
                    firstName = "Legacy",
                    lastName = "Resident",
                    age = 30,
                    householdId = 7,
                    birthDate = new BirthDate(1850, 4, 3, BirthDateProvenance.Estimated),
                });
                legacy.households.Add(new LandLedgers.Persistence.HouseholdSaveDto
                {
                    id = 7,
                    householdName = "Legacy household",
                    memberIds = new List<int> { 1 },
                });

                PopulationManager target = root.AddComponent<PopulationManager>();
                target.LoadFromSaveDto(legacy);

                Assert.IsNull(legacy.householdMemberships,
                    "This represents a pre-migration save without authoritative membership history.");
                Assert.AreEqual(7, target.Memberships.GetActiveHouseholdId(1));
                Assert.AreEqual(1, target.Memberships.ActiveCount);
                Assert.AreEqual(HouseholdMembershipSource.Authored,
                    target.Memberships.GetActiveMembership(1).Source);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PopulationSaveLoad_MaximumMembershipIdDoesNotOverflowAllocatorCursor()
        {
            var save = new LandLedgers.Persistence.PopulationSaveDto();
            save.people.Add(new LandLedgers.Persistence.PersonSaveDto
            {
                id = 4,
                firstName = "Exhausted",
                lastName = "Identity",
                age = 35,
                householdId = 2,
                deathDayIndex = -1,
                birthDate = new BirthDate(1851, 5, 1, BirthDateProvenance.Exact),
            });
            save.households.Add(new LandLedgers.Persistence.HouseholdSaveDto
            {
                id = 2,
                householdName = "Exhausted identity household",
                memberIds = new List<int> { 4 },
            });
            save.householdMemberships = new HouseholdMembershipRegistrySaveDto();
            save.householdMemberships.memberships.Add(new HouseholdMembership
            {
                MembershipId = int.MaxValue,
                PersonId = 4,
                HouseholdId = 2,
                Lifecycle = HouseholdMembershipLifecycle.Active,
                StartDayIndex = 0,
            });

            GameObject root = new("Exhausted Membership ID Save Target");
            try
            {
                PopulationManager target = root.AddComponent<PopulationManager>();
                LogAssert.Expect(LogType.Warning, new Regex("membership identity space is exhausted"));
                Assert.DoesNotThrow(() => target.LoadFromSaveDto(save));
                Assert.AreEqual(2, target.Memberships.GetActiveHouseholdId(4));
                Assert.AreEqual(2, target.State.GetPerson(4).householdId);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
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
