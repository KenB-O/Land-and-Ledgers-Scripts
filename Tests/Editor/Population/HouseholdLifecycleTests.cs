using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// HF-3: household formation, join/leave/transfer, birth with kinship, death,
    /// dissolution, kinship/membership separation (Tech X §2.2), and the player-household
    /// bootstrap (GHOST-DEF-006).
    /// </summary>
    [TestFixture]
    public sealed class HouseholdLifecycleTests
    {
        private PopulationState population;
        private HouseholdMembershipRegistry memberships;
        private KinshipRegistry kinship;
        private EntityIdRegistry ids;
        private HouseholdLifecycleManager lifecycle;

        [SetUp]
        public void SetUp()
        {
            population = new PopulationState();
            memberships = new HouseholdMembershipRegistry();
            kinship = new KinshipRegistry();
            ids = new EntityIdRegistry();
            lifecycle = new HouseholdLifecycleManager(population, memberships, kinship, ids);
        }

        private PersonState AddPerson(string firstName, string lastName, int age)
        {
            var person = new PersonState
            {
                id = population.AllocateNextPersonId(),
                firstName = firstName,
                lastName = lastName,
                age = age,
                ageBand = HouseholdLifecycleManager.AgeBandForAge(age),
            };
            population.people.Add(person);
            ids.SeedKind(EntityKind.Person, person.id + 1);
            return person;
        }

        [Test]
        public void FormHousehold_CreatesHousehold_WithTypedId_AndFounderMembership()
        {
            PersonState founder = AddPerson("Anna", "Morrow", 30);

            HouseholdState household = lifecycle.FormHousehold("Morrow Household", "Morrow", founder.id, 100, HouseholdMembershipSource.Authored);

            Assert.IsNotNull(household);
            Assert.AreEqual(EntityKind.Household, household.entityId.Kind);
            Assert.AreEqual(household.id, household.entityId.Id); // typed id reuses int value, no renumbering
            Assert.AreEqual(HouseholdLifecycleState.Active, household.lifecycleState);
            Assert.AreEqual(household.id, memberships.GetActiveHouseholdId(founder.id));
            CollectionAssert.AreEqual(new[] { founder.id }, lifecycle.GetMembers(household.id));
        }

        [Test]
        public void Birth_CreatesPerson_KinshipEdges_AndMembership()
        {
            PersonState mother = AddPerson("Anna", "Morrow", 30);
            PersonState father = AddPerson("Jonas", "Morrow", 32);
            HouseholdState household = lifecycle.FormHousehold("Morrow Household", "Morrow", mother.id, 100, HouseholdMembershipSource.Authored);
            lifecycle.AddMember(household.id, father.id, 100, HouseholdMembershipSource.Authored, "spouse");

            PersonState child = lifecycle.RecordBirth(household.id, mother.id, father.id, "Petra", "Morrow", 200);

            Assert.IsNotNull(child);
            Assert.AreEqual(0, child.age);
            Assert.AreEqual(household.id, memberships.GetActiveHouseholdId(child.id));
            CollectionAssert.AreEquivalent(new[] { mother.id, father.id }, kinship.GetParents(child.id));
            CollectionAssert.Contains(kinship.GetChildren(mother.id), child.id);
        }

        [Test]
        public void Kinship_IsSeparateFromMembership()
        {
            PersonState mother = AddPerson("Anna", "Morrow", 30);
            PersonState father = AddPerson("Jonas", "Morrow", 32);
            HouseholdState h1 = lifecycle.FormHousehold("Morrow Household", "Morrow", mother.id, 100, HouseholdMembershipSource.Authored);
            HouseholdState h2 = lifecycle.FormHousehold("Jonas Household", "Morrow", father.id, 100, HouseholdMembershipSource.Authored);

            kinship.AddSpouses(mother.id, father.id, "marriage");
            // Spouses live in different households: kinship edges do not imply membership.
            CollectionAssert.Contains(kinship.GetSpouses(mother.id), father.id);
            Assert.AreEqual(h1.id, memberships.GetActiveHouseholdId(mother.id));
            Assert.AreEqual(h2.id, memberships.GetActiveHouseholdId(father.id));
        }

        [Test]
        public void TransferMember_MovesPerson_Atomically()
        {
            PersonState person = AddPerson("Anna", "Morrow", 30);
            PersonState other = AddPerson("Jonas", "Morrow", 32);
            HouseholdState h1 = lifecycle.FormHousehold("H1", "Morrow", person.id, 100, HouseholdMembershipSource.Authored);
            HouseholdState h2 = lifecycle.FormHousehold("H2", "Morrow", other.id, 100, HouseholdMembershipSource.Authored);

            string rejection = lifecycle.TransferMember(person.id, h2.id, 200);

            Assert.IsNull(rejection);
            Assert.AreEqual(h2.id, memberships.GetActiveHouseholdId(person.id));
            CollectionAssert.IsEmpty(lifecycle.GetMembers(h1.id));
        }

        [Test]
        public void Death_EndsMembership_KeepsPersonRecord()
        {
            PersonState person = AddPerson("Anna", "Morrow", 70);
            HouseholdState household = lifecycle.FormHousehold("Morrow Household", "Morrow", person.id, 100, HouseholdMembershipSource.Authored);

            bool ok = lifecycle.RecordDeath(person.id, 300, "old age");

            Assert.IsTrue(ok);
            Assert.AreEqual(300, person.deathDayIndex);
            Assert.AreEqual(-1, memberships.GetActiveHouseholdId(person.id)); // no active membership
            Assert.IsNotNull(population.GetPerson(person.id)); // record retained, id never rewritten
        }

        [Test]
        public void DissolveHousehold_EndsAllMemberships_MarksDissolved()
        {
            PersonState a = AddPerson("Anna", "Morrow", 30);
            PersonState b = AddPerson("Jonas", "Morrow", 32);
            HouseholdState household = lifecycle.FormHousehold("Morrow Household", "Morrow", a.id, 100, HouseholdMembershipSource.Authored);
            lifecycle.AddMember(household.id, b.id, 100, HouseholdMembershipSource.Authored, "spouse");

            bool ok = lifecycle.DissolveHousehold(household.id, 400, "emigrated");

            Assert.IsTrue(ok);
            Assert.AreEqual(HouseholdLifecycleState.Dissolved, household.lifecycleState);
            CollectionAssert.IsEmpty(lifecycle.GetMembers(household.id));
            Assert.IsNotNull(lifecycle.AddMember(household.id, a.id, 401, HouseholdMembershipSource.Manual, "rejoin"),
                "Rejoining a dissolved household must be rejected.");
        }

        [Test]
        public void DeclarePlayerHousehold_CreatesRealSimulatedHousehold()
        {
            PersonState founder = AddPerson("Kennedy", "Player", 28);
            PersonState spouse = AddPerson("Emma", "Player", 27);

            HouseholdState player = lifecycle.DeclarePlayerHousehold(
                "ghost-town", "Player Household", new List<int> { founder.id, spouse.id }, 0);

            // GHOST-DEF-006: the player household must EXIST in the simulation.
            Assert.IsNotNull(player);
            Assert.IsTrue(player.isPlayerHousehold);
            Assert.AreEqual("ghost-town", player.playerScenarioId);
            Assert.IsNotNull(population.GetHousehold(player.id));
            CollectionAssert.AreEquivalent(
                new[] { founder.id, spouse.id }, lifecycle.GetMembers(player.id));
            Assert.AreEqual(player.id, memberships.GetActiveHouseholdId(founder.id));
        }

        [Test]
        public void EnsureEntityIds_BackfillsLegacyHouseholds()
        {
            population.households.Add(new HouseholdState { id = 0, householdName = "Legacy" });
            population.RestoreHouseholdIdAllocator(1);

            lifecycle.EnsureEntityIds();

            Assert.AreEqual(EntityKind.Household, population.households[0].entityId.Kind);
            Assert.AreEqual(0, population.households[0].entityId.Id);
            Assert.AreEqual(1, ids.PeekNext(EntityKind.Household)); // no collision with future ids
        }
    }
}
