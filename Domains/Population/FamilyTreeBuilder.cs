using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass XL phase 3: complete family trees on instantiation.
    ///
    /// Gap this closes: <see cref="KinshipRegistry"/> already stores parent,
    /// child, spouse, and sibling edges, and generation already writes spouse
    /// and in-cohort parent edges, but (a) sibling edges are READ by
    /// WorldInteractionController and WillContest and are NEVER written by
    /// generation, (b) no grandparents exist, and (c) no off-map relatives
    /// exist at all, so a resident has nobody to recommend when the player
    /// asks "do you know anyone who would move here?".
    ///
    /// What this builder does, idempotently, for every living person:
    ///   - ensures two parents (father + mother; an existing single parent
    ///     is kept and only the missing sex is created);
    ///   - ensures grandparents, i.e. parents of those parents, one level
    ///     only (no great-grandparents; that is a Kennedy scope decision);
    ///   - writes sibling edges between children sharing at least one parent;
    ///   - creates missing parents/grandparents as OFF-MAP PersonState
    ///     records with real, allocated person ids, so they are real
    ///     instantiable identities, not flavour strings. Off-map records
    ///     are NOT residents: they have no household, no home building,
    ///     and are not counted as on-map population. They become residents
    ///     only through <see cref="MaterialiseOffMapRelative"/>, which
    ///     flips the same record, never creates a second person.
    ///
    /// Ages are deterministic: parent age = child age + a 22-38 year gap
    /// drawn from a stable hash of (seed, child id), inside the existing
    /// DemographicCohortPlanner 17-44 year parent/child plausibility band.
    /// Birth dates are derived from the caller-supplied reference year
    /// (tests supply it; it is NOT canon) so RefreshDerivedAge reproduces
    /// the stated age. Names are seeded picks from small period lists;
    /// they are generation-style invented names, exactly as cohort
    /// generation invents names, not historical persons.
    ///
    /// This builder is OPT-IN, deliberately not called by
    /// PopulationGenerator: calling it inside generation would change the
    /// on-map people count in existing generation tests (off-map records
    /// share PopulationState.people). The production wiring point since
    /// Pass XL phase 4 is the tested seam FamilyTreeWiring
    /// .ApplyAfterGeneration: call it once after
    /// GeneratePopulationSnapshot, before the first save (the Codex call
    /// site in FirstLedgerSliceBootstrapper or the equivalent New Game
    /// path is still open, phase 4 finding F01). Household membership
    /// joining on materialisation belongs to the settlement driver
    /// (ArrivalSettlementDriver, phase 4), not to this builder: this
    /// builder only sets the legacy householdId/homeBuildingId pointers.
    /// Plain C#, no Unity dependency, Roslyn-testable.
    /// </summary>
    public static class FamilyTreeBuilder
    {
        public sealed class Report
        {
            public int ParentsCreated;
            public int GrandparentsCreated;
            public int SiblingEdgesAdded;
            public int PersonsProcessed;
            public int OffMapRelativesCreated => ParentsCreated + GrandparentsCreated;
        }

        private static readonly string[] MaleNames =
            { "James", "John", "William", "George", "Henry", "Thomas", "Charles", "Joseph" };
        private static readonly string[] FemaleNames =
            { "Mary", "Sarah", "Elizabeth", "Anna", "Margaret", "Emma", "Alice", "Clara" };

        public static Report EnsureCompleteTree(
            PopulationState population, KinshipRegistry kinship, int seed,
            int referenceYear, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            var report = new Report();
            if (population == null || population.people == null || kinship == null)
            {
                diagnostics.Add("FamilyTreeBuilder: null population or kinship, nothing built.");
                return report;
            }

            // Snapshot the original on-map residents first: parents created
            // in this pass must not be re-processed as residents.
            var residents = new List<PersonState>();
            foreach (PersonState person in population.people)
            {
                if (person != null && !person.isOffMap && person.deathDayIndex < 0)
                    residents.Add(person);
            }

            var parentsOfResidents = new List<int>();
            foreach (PersonState resident in residents)
            {
                report.PersonsProcessed++;
                List<int> parentIds = EnsureParents(population, kinship, resident, seed, referenceYear, report, diagnostics, isGrandparent: false);
                parentsOfResidents.AddRange(parentIds);
            }

            // Grandparents: parents of the parents, one level only.
            // (A duplicate parent id here — two residents sharing a
            // parent — is a harmless second pass: EnsureParents is a
            // no-op once both parents exist.)
            foreach (int parentId in parentsOfResidents)
            {
                PersonState parent = population.GetPerson(parentId);
                if (parent == null) continue;
                EnsureParents(population, kinship, parent, seed, referenceYear, report, diagnostics, isGrandparent: true);
            }

            // Siblings: children sharing at least one parent.
            var childrenByParent = new Dictionary<int, List<int>>();
            foreach (PersonState resident in residents)
            {
                foreach (int parentId in kinship.GetParents(resident.id))
                {
                    if (!childrenByParent.TryGetValue(parentId, out List<int> children))
                    {
                        children = new List<int>();
                        childrenByParent[parentId] = children;
                    }
                    if (!children.Contains(resident.id)) children.Add(resident.id);
                }
            }
            foreach (List<int> children in childrenByParent.Values)
            {
                for (int i = 0; i < children.Count; i++)
                {
                    for (int j = i + 1; j < children.Count; j++)
                    {
                        int before = kinship.EdgeCount;
                        kinship.AddEdge(children[i], children[j], KinshipRelation.Sibling, "family-tree");
                        if (kinship.EdgeCount > before) report.SiblingEdgesAdded++;
                    }
                }
            }

            diagnostics.Add(
                $"FamilyTreeBuilder: processed {report.PersonsProcessed} residents, " +
                $"created {report.ParentsCreated} parents and {report.GrandparentsCreated} grandparents " +
                $"({report.OffMapRelativesCreated} off-map relatives), added {report.SiblingEdgesAdded} sibling edges.");
            return report;
        }

        private static List<int> EnsureParents(
            PopulationState population, KinshipRegistry kinship, PersonState child,
            int seed, int referenceYear, Report report, List<string> diagnostics, bool isGrandparent)
        {
            var result = new List<int>(kinship.GetParents(child.id));
            bool hasFather = false, hasMother = false;
            foreach (int parentId in result)
            {
                PersonState parent = population.GetPerson(parentId);
                if (parent != null && parent.isFemale) hasMother = true;
                else if (parent != null) hasFather = true;
            }

            if (!hasFather)
            {
                int fatherId = CreateOffMapParent(population, kinship, child, seed, referenceYear, isFemale: false, diagnostics);
                result.Add(fatherId);
                if (isGrandparent) report.GrandparentsCreated++; else report.ParentsCreated++;
            }
            if (!hasMother)
            {
                int motherId = CreateOffMapParent(population, kinship, child, seed, referenceYear, isFemale: true, diagnostics);
                result.Add(motherId);
                if (isGrandparent) report.GrandparentsCreated++; else report.ParentsCreated++;
            }
            return result;
        }

        private static int CreateOffMapParent(
            PopulationState population, KinshipRegistry kinship, PersonState child,
            int seed, int referenceYear, bool isFemale, List<string> diagnostics)
        {
            int gap = 22 + StableHash(seed, child.id, isFemale ? 1 : 2) % 17; // 22-38
            int age = Math.Max(0, child.age + gap);
            string[] names = isFemale ? FemaleNames : MaleNames;
            string firstName = names[StableHash(seed, child.id, isFemale ? 7 : 3) % names.Length];
            int id = population.AllocateNextPersonId();
            var parent = new PersonState
            {
                id = id,
                firstName = firstName,
                lastName = child.lastName,
                isFemale = isFemale,
                isOffMap = true,
                householdId = -1,
                homeBuildingId = -1,
                workplaceBuildingId = -1,
                birthDate = new BirthDate(referenceYear - age, 1, 1, BirthDateProvenance.GeneratedExact, "family-tree"),
            };
            parent.RefreshDerivedAge(referenceYear, 1, 1);
            // RefreshDerivedAge recomputes from the birth date, which was
            // built to reproduce exactly this age; keep the explicit age
            // as the record if the calendar math ever disagrees.
            parent.age = age;
            population.people.Add(parent);
            kinship.AddParentChild(id, child.id, "family-tree");
            diagnostics.Add($"FamilyTreeBuilder: created off-map {(isFemale ? "mother" : "father")} P{id} for P{child.id}.");
            return id;
        }

        /// <summary>
        /// Turns an existing off-map relative into a resident, reusing the
        /// SAME person record and id. Refuses unknown ids, already on-map
        /// persons, and deceased persons. Does NOT join a household in the
        /// membership registry: that is the settlement step's job. It sets
        /// the legacy householdId/homeBuildingId pointers only, and the
        /// caller must run settlement (housing + household + job) after.
        /// </summary>
        public static bool MaterialiseOffMapRelative(
            PopulationState population, int personId, int householdId, int homeBuildingId,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (population == null || population.people == null)
            {
                diagnostics.Add("MaterialiseOffMapRelative: null population.");
                return false;
            }
            PersonState person = population.GetPerson(personId);
            if (person == null)
            {
                diagnostics.Add($"MaterialiseOffMapRelative: P{personId} unknown, nothing materialised.");
                return false;
            }
            if (!person.isOffMap)
            {
                diagnostics.Add($"MaterialiseOffMapRelative: P{personId} is already on map, no duplicate created.");
                return false;
            }
            if (person.deathDayIndex >= 0)
            {
                diagnostics.Add($"MaterialiseOffMapRelative: P{personId} is deceased, cannot materialise.");
                return false;
            }
            int before = population.people.Count;
            person.isOffMap = false;
            person.householdId = householdId;
            person.homeBuildingId = homeBuildingId;
            diagnostics.Add(
                $"MaterialiseOffMapRelative: P{personId} materialised as a resident " +
                $"(people count unchanged at {before}; household membership joining is the settlement step).");
            return before == population.people.Count;
        }

        private static int StableHash(int seed, int personId, int salt)
        {
            unchecked
            {
                int h = seed;
                h = h * 31 + personId;
                h = h * 31 + salt;
                uint x = (uint)h ^ 0x9E3779B9u;
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                return (int)((x & 0x7fffffff) % 100000);
            }
        }
    }
}
