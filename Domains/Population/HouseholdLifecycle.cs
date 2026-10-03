using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Population
{
    /// <summary>Household lifecycle states (extends the PKG-8 membership lifecycle to the household itself).</summary>
    public enum HouseholdLifecycleState
    {
        Active = 0,
        Transitional = 1, // no current members but migrating economic/estate state
        Dissolved = 2,
    }

    /// <summary>
    /// Kinship relations (Tech X §2.2 TECH LOCK). Kinship/family-tree edges are stored
    /// SEPARATELY from household membership: referrals, inheritance and family history
    /// span settlements and generations without rewriting Person identity.
    /// </summary>
    public enum KinshipRelation
    {
        Parent = 0, // FromPersonId is the parent of ToPersonId
        Child = 1, // FromPersonId is the child of ToPersonId
        Spouse = 2, // symmetric
        Sibling = 3, // symmetric
    }

    /// <summary>One kinship edge between two Persons. Never a substitute for membership.</summary>
    [Serializable]
    public sealed class KinshipEdge
    {
        public int FromPersonId;
        public int ToPersonId;
        public KinshipRelation Relation;
        public string Source = string.Empty;
        public string Notes = string.Empty;
    }

    /// <summary>
    /// HF-3: the kinship authority. Edges are stored independently of
    /// <see cref="HouseholdMembershipRegistry"/> (Tech X §2.2).
    /// </summary>
    public sealed class KinshipRegistry
    {
        private readonly List<KinshipEdge> edges = new List<KinshipEdge>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public int EdgeCount => edges.Count;

        /// <summary>
        /// Adds a kinship edge. Symmetric relations (Spouse, Sibling) and the Parent/Child
        /// inverse are recorded automatically; exact duplicates are ignored.
        /// </summary>
        public void AddEdge(int fromPersonId, int toPersonId, KinshipRelation relation, string source)
        {
            if (fromPersonId < 0 || toPersonId < 0 || fromPersonId == toPersonId)
            {
                diagnostics.Add($"Rejected kinship edge {fromPersonId}->{toPersonId}: invalid person ids.");
                return;
            }

            AddDirectedEdge(fromPersonId, toPersonId, relation, source);
            switch (relation)
            {
                case KinshipRelation.Spouse:
                case KinshipRelation.Sibling:
                    AddDirectedEdge(toPersonId, fromPersonId, relation, source);
                    break;
                case KinshipRelation.Parent:
                    AddDirectedEdge(toPersonId, fromPersonId, KinshipRelation.Child, source);
                    break;
                case KinshipRelation.Child:
                    AddDirectedEdge(toPersonId, fromPersonId, KinshipRelation.Parent, source);
                    break;
            }
        }

        public void AddParentChild(int parentId, int childId, string source)
        {
            AddEdge(parentId, childId, KinshipRelation.Parent, source);
        }

        public void AddSpouses(int personA, int personB, string source)
        {
            AddEdge(personA, personB, KinshipRelation.Spouse, source);
        }

        public List<int> GetRelated(int personId, KinshipRelation relation)
        {
            var result = new List<int>();
            for (int i = 0; i < edges.Count; i++)
            {
                KinshipEdge edge = edges[i];
                if (edge != null && edge.FromPersonId == personId && edge.Relation == relation)
                {
                    result.Add(edge.ToPersonId);
                }
            }

            result.Sort();
            return result;
        }

        public List<int> GetParents(int personId)
        {
            return GetRelated(personId, KinshipRelation.Child);
        }

        public List<int> GetChildren(int personId)
        {
            return GetRelated(personId, KinshipRelation.Parent);
        }

        public List<int> GetSpouses(int personId)
        {
            return GetRelated(personId, KinshipRelation.Spouse);
        }

        private void AddDirectedEdge(int fromPersonId, int toPersonId, KinshipRelation relation, string source)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                KinshipEdge existing = edges[i];
                if (existing != null
                    && existing.FromPersonId == fromPersonId
                    && existing.ToPersonId == toPersonId
                    && existing.Relation == relation)
                {
                    return; // exact duplicate: ignore
                }
            }

            edges.Add(new KinshipEdge
            {
                FromPersonId = fromPersonId,
                ToPersonId = toPersonId,
                Relation = relation,
                Source = source ?? string.Empty,
            });
        }
    }

    /// <summary>
    /// HF-3: household lifecycle authority. Builds on the PKG-8
    /// <see cref="HouseholdMembershipRegistry"/> (the sole membership authority) and the
    /// HF-1 <see cref="EntityIdRegistry"/> (typed household IDs).
    ///
    /// Lifecycle: formation -> member join/leave/transfer -> birth/aging-in -> departure /
    /// death -> dissolution. Kinship edges are recorded separately from membership
    /// (Tech X §2.2 TECH LOCK).
    /// </summary>
    public sealed class HouseholdLifecycleManager
    {
        private readonly PopulationState population;
        private readonly HouseholdMembershipRegistry memberships;
        private readonly KinshipRegistry kinship;
        private readonly EntityIdRegistry ids;
        private readonly List<string> diagnostics = new List<string>();

        public HouseholdLifecycleManager(
            PopulationState population,
            HouseholdMembershipRegistry memberships,
            KinshipRegistry kinship,
            EntityIdRegistry ids)
        {
            this.population = population ?? throw new ArgumentNullException(nameof(population));
            this.memberships = memberships ?? throw new ArgumentNullException(nameof(memberships));
            this.kinship = kinship ?? throw new ArgumentNullException(nameof(kinship));
            this.ids = ids ?? throw new ArgumentNullException(nameof(ids));
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Backfills HF-1 typed IDs for pre-existing households: EntityId(Household, id).
        /// Safe because the Household kind sequence is seeded from the legacy household
        /// allocator, so the numeric values cannot collide with future allocations.
        /// </summary>
        public void EnsureEntityIds()
        {
            if (population.households == null)
            {
                return;
            }

            ids.SeedKind(EntityKind.Household, population.NextHouseholdId);
            for (int i = 0; i < population.households.Count; i++)
            {
                HouseholdState household = population.households[i];
                if (household == null || household.entityId.IsValid)
                {
                    continue;
                }

                household.entityId = EntityId.For(EntityKind.Household, household.id);
                ids.SeedKind(EntityKind.Household, household.id + 1);
            }
        }

        /// <summary>
        /// Forms a new household around a founder. The founder must already exist as a Person.
        /// </summary>
        public HouseholdState FormHousehold(
            string householdName,
            string surname,
            int founderPersonId,
            int dayIndex,
            HouseholdMembershipSource source)
        {
            PersonState founder = population.GetPerson(founderPersonId);
            if (founder == null)
            {
                diagnostics.Add($"FormHousehold: unknown founder person {founderPersonId}.");
                return null;
            }

            if (founder.deathDayIndex >= 0)
            {
                diagnostics.Add($"FormHousehold: founder P{founderPersonId} is deceased.");
                return null;
            }

            var household = new HouseholdState
            {
                id = population.AllocateNextHouseholdId(),
                householdName = householdName ?? $"Household {population.NextHouseholdId}",
                surname = surname ?? founder.lastName ?? string.Empty,
                lifecycleState = HouseholdLifecycleState.Active,
            };
            // The typed ID reuses the legacy int value exactly: the M1 int allocator remains
            // the authority for the household id space, so no renumbering ever occurs.
            household.entityId = EntityId.For(EntityKind.Household, household.id);
            ids.SeedKind(EntityKind.Household, household.id + 1);

            population.households.Add(household);
            RegisterMembership(founderPersonId, household.id, dayIndex, source, "household founder");
            return household;
        }

        /// <summary>
        /// GHOST-DEF-006: a scenario declares its player household and the household EXISTS
        /// as a real simulated household (members, membership records, typed ID) rather
        /// than being omitted from domestic simulation (Canon XIII 13.1).
        /// </summary>
        public HouseholdState DeclarePlayerHousehold(
            string scenarioId,
            string householdName,
            List<int> founderPersonIds,
            int dayIndex)
        {
            if (founderPersonIds == null || founderPersonIds.Count == 0)
            {
                diagnostics.Add("DeclarePlayerHousehold: no founders supplied.");
                return null;
            }

            HouseholdState household = FormHousehold(
                householdName, null, founderPersonIds[0], dayIndex, HouseholdMembershipSource.Authored);
            if (household == null)
            {
                return null;
            }

            household.isPlayerHousehold = true;
            household.playerScenarioId = scenarioId ?? string.Empty;
            for (int i = 1; i < founderPersonIds.Count; i++)
            {
                AddMember(household.id, founderPersonIds[i], dayIndex, HouseholdMembershipSource.Authored, "player household founder");
            }

            return household;
        }

        /// <summary>Adds a member to a household (join / move-in / kin absorption).</summary>
        public string AddMember(
            int householdId,
            int personId,
            int dayIndex,
            HouseholdMembershipSource source,
            string reason)
        {
            HouseholdState household = population.GetHousehold(householdId);
            if (household == null)
            {
                return $"AddMember: unknown household {householdId}.";
            }

            if (household.lifecycleState == HouseholdLifecycleState.Dissolved)
            {
                return $"AddMember: household H{householdId} is dissolved.";
            }

            PersonState person = population.GetPerson(personId);
            if (person == null)
            {
                return $"AddMember: unknown person {personId}.";
            }

            if (person.deathDayIndex >= 0)
            {
                return $"AddMember: P{personId} is deceased.";
            }

            return RegisterMembership(personId, householdId, dayIndex, source, reason);
        }

        /// <summary>Removes a member (departure / move-out / split).</summary>
        public bool RemoveMember(int personId, int dayIndex, string reason)
        {
            return memberships.EndMembership(personId, dayIndex, reason);
        }

        /// <summary>
        /// Atomic household transfer. Reuses the PKG-8 registry path (at-most-one-active
        /// membership, 3A-D14); conflicting legacy copies are handled by the PKG-3
        /// migration decision tables at import time, not here.
        /// </summary>
        public string TransferMember(int personId, int toHouseholdId, int dayIndex)
        {
            if (population.GetHousehold(toHouseholdId) == null)
            {
                return $"TransferMember: unknown destination household {toHouseholdId}.";
            }

            int nextMembershipId = ids.Allocate(EntityKind.Membership).Id;
            return memberships.TransferMembership(personId, toHouseholdId, dayIndex, nextMembershipId);
        }

        /// <summary>
        /// Records a birth: new Person (fresh HF-1 PersonId, never reused), kinship edges to
        /// both parents, and Active membership in the household. Kinship and membership are
        /// separate records (Tech X §2.2).
        /// </summary>
        public PersonState RecordBirth(
            int householdId,
            int motherPersonId,
            int fatherPersonId,
            string firstName,
            string lastName,
            int dayIndex)
        {
            HouseholdState household = population.GetHousehold(householdId);
            if (household == null)
            {
                diagnostics.Add($"RecordBirth: unknown household {householdId}.");
                return null;
            }

            var child = new PersonState
            {
                id = population.AllocateNextPersonId(),
                firstName = firstName ?? "Child",
                lastName = lastName ?? household.surname ?? string.Empty,
                age = 0,
                ageBand = AgeBand.Child0To9,
                deathDayIndex = -1,
            };
            population.people.Add(child);
            ids.SeedKind(EntityKind.Person, child.id + 1);

            if (motherPersonId >= 0)
            {
                kinship.AddParentChild(motherPersonId, child.id, "birth");
            }

            if (fatherPersonId >= 0)
            {
                kinship.AddParentChild(fatherPersonId, child.id, "birth");
            }

            RegisterMembership(child.id, householdId, dayIndex, HouseholdMembershipSource.Authored, "birth");
            return child;
        }

        /// <summary>
        /// Records a death: ends the active membership, marks the Person record. The Person
        /// record and its PersonId are retained — identity is never rewritten (Tech X §2.2).
        /// </summary>
        public bool RecordDeath(int personId, int dayIndex, string cause)
        {
            PersonState person = population.GetPerson(personId);
            if (person == null)
            {
                diagnostics.Add($"RecordDeath: unknown person {personId}.");
                return false;
            }

            person.deathDayIndex = dayIndex;
            memberships.EndMembership(personId, dayIndex, $"death: {cause}");
            return true;
        }

        /// <summary>Ages a person by whole years, maintaining the age band.</summary>
        public void AgePerson(PersonState person, int years)
        {
            if (person == null || years <= 0)
            {
                return;
            }

            person.age += years;
            person.ageBand = AgeBandForAge(person.age);
        }

        /// <summary>
        /// Dissolves a household: ends every active membership, marks the household
        /// Dissolved. Economic/estate state migration is handled by the caller (HF-4).
        /// </summary>
        public bool DissolveHousehold(int householdId, int dayIndex, string reason)
        {
            HouseholdState household = population.GetHousehold(householdId);
            if (household == null)
            {
                diagnostics.Add($"DissolveHousehold: unknown household {householdId}.");
                return false;
            }

            List<int> members = memberships.GetActiveMembers(householdId);
            foreach (int personId in members)
            {
                memberships.EndMembership(personId, dayIndex, $"household dissolved: {reason}");
            }

            household.lifecycleState = HouseholdLifecycleState.Dissolved;
            return true;
        }

        public List<int> GetMembers(int householdId)
        {
            return memberships.GetActiveMembers(householdId);
        }

        public static AgeBand AgeBandForAge(int age)
        {
            if (age < 10)
            {
                return AgeBand.Child0To9;
            }

            if (age < 13)
            {
                return AgeBand.Helper10To12;
            }

            if (age < 16)
            {
                return AgeBand.JuniorWorker13To15;
            }

            if (age < 18)
            {
                return AgeBand.YoungWorker16To17;
            }

            return AgeBand.Adult18Plus;
        }

        private string RegisterMembership(
            int personId,
            int householdId,
            int dayIndex,
            HouseholdMembershipSource source,
            string reason)
        {
            var membership = new HouseholdMembership
            {
                MembershipId = ids.Allocate(EntityKind.Membership).Id,
                PersonId = personId,
                HouseholdId = householdId,
                Lifecycle = HouseholdMembershipLifecycle.Active,
                StartDayIndex = dayIndex,
                EndDayIndex = -1,
                Source = source,
                LegacyDiagnostic = reason ?? string.Empty,
            };
            string rejection = memberships.Register(membership);
            if (rejection != null)
            {
                diagnostics.Add(rejection);
            }

            return rejection;
        }
    }
}
