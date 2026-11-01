// Copyright (c) 2026 Land & Ledgers. All rights reserved.
// Pass V P1-3: the demographic cohort planner produces persons-first family
// units with temporally coherent ages (Canon 12.6G steps 1-4): parent/child
// gaps inside [17, 44], spouses within 12 years, birth dates that reproduce
// the planned ages, and deterministic output in the seed.
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class CohortPlannerTests
    {
        [Test]
        public void PlanCohort_MeetsResidentTarget_AndHouseholdEnvelope()
        {
            List<FamilyUnitPlan> units = DemographicCohortPlanner.PlanCohort(78, 18, seed: 42);

            Assert.AreEqual(18, units.Count);
            int total = units.Sum(u => u.members.Count);
            Assert.AreEqual(78, total, "Top-up reaches the resident target exactly.");
            Assert.IsTrue(total >= 55 && total <= 95, "Calibration range holds.");
            Assert.IsTrue(units.All(u => u.members.Count >= 1 && u.members.Count <= 6));
        }

        [Test]
        public void PlanCohort_ParentChildGaps_AreTemporallyCoherent()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                List<FamilyUnitPlan> units = DemographicCohortPlanner.PlanCohort(78, 18, seed);
                foreach (FamilyUnitPlan unit in units)
                {
                    foreach (PlannedPerson child in unit.members.Where(p => p.role == PlannedPersonRole.Child))
                    {
                        // Single-parent units legitimately leave one parent unknown.
                        Assert.IsTrue(child.motherPlanIndex >= 0 || child.fatherPlanIndex >= 0,
                            $"seed {seed}: child has no known parent.");
                        CheckParentGap(units, unit, child, child.motherPlanIndex, seed, "mother");
                        CheckParentGap(units, unit, child, child.fatherPlanIndex, seed, "father");
                    }
                }
            }
        }

        private static void CheckParentGap(
            List<FamilyUnitPlan> units, FamilyUnitPlan unit, PlannedPerson child,
            int parentPlanIndex, int seed, string label)
        {
            if (parentPlanIndex < 0)
            {
                return;
            }

            PlannedPerson parent = unit.members[parentPlanIndex];
            int gap = parent.age - child.age;
            Assert.GreaterOrEqual(gap, DemographicCohortPlanner.MinParentChildGapYears,
                $"seed {seed}: {label}/child gap {gap}.");
            Assert.LessOrEqual(gap, DemographicCohortPlanner.MaxParentChildGapYears,
                $"seed {seed}: {label}/child gap {gap}.");
        }

        [Test]
        public void PlanCohort_SpouseLinks_AreSymmetric_AndAgeCoherent()
        {
            for (int seed = 1; seed <= 8; seed++)
            {
                List<FamilyUnitPlan> units = DemographicCohortPlanner.PlanCohort(78, 18, seed);
                foreach (FamilyUnitPlan unit in units)
                {
                    foreach (PlannedPerson p in unit.members)
                    {
                        if (p.spousePlanIndex < 0)
                        {
                            continue;
                        }

                        PlannedPerson spouse = unit.members[p.spousePlanIndex];
                        Assert.AreEqual(unit.members.IndexOf(p), spouse.spousePlanIndex,
                            $"seed {seed}: spouse link is not symmetric.");
                        Assert.LessOrEqual(System.Math.Abs(p.age - spouse.age), 12,
                            $"seed {seed}: spouse age gap exceeds 12.");
                        Assert.AreNotEqual(p.isFemale, spouse.isFemale);
                    }
                }
            }
        }

        [Test]
        public void PlanCohort_FamilyLinks_AreUnitLocal()
        {
            List<FamilyUnitPlan> units = DemographicCohortPlanner.PlanCohort(78, 18, seed: 7);

            foreach (FamilyUnitPlan unit in units)
            {
                foreach (PlannedPerson p in unit.members)
                {
                    Assert.IsTrue(p.spousePlanIndex < 0 || p.spousePlanIndex < unit.members.Count);
                    Assert.IsTrue(p.motherPlanIndex < 0 || p.motherPlanIndex < unit.members.Count);
                    Assert.IsTrue(p.fatherPlanIndex < 0 || p.fatherPlanIndex < unit.members.Count);
                }
            }
        }

        [Test]
        public void AssignBirthDates_ReproducesPlannedAges_AtScenarioStart()
        {
            List<FamilyUnitPlan> units = DemographicCohortPlanner.PlanCohort(78, 18, seed: 11);
            List<PlannedPerson> flat = DemographicCohortPlanner.Flatten(units);
            List<BirthDate> dates = DemographicCohortPlanner.AssignBirthDates(
                flat, 1892, 3, 1, seed: 11, provenanceNote: "cohort-test");

            Assert.AreEqual(flat.Count, dates.Count);
            for (int i = 0; i < flat.Count; i++)
            {
                Assert.IsTrue(dates[i].IsValid, $"P{i}: invalid birth date.");
                Assert.AreEqual(BirthDateProvenance.GeneratedExact, dates[i].Provenance);
                int derived = AgeAuthority.CompletedYears(dates[i], 1892, 3, 1);
                Assert.AreEqual(flat[i].age, derived,
                    $"P{i}: planned age {flat[i].age} but birth date gives {derived}.");
            }
        }

        [Test]
        public void PlanCohort_IsDeterministicInSeed()
        {
            List<FamilyUnitPlan> a = DemographicCohortPlanner.PlanCohort(78, 18, seed: 99);
            List<FamilyUnitPlan> b = DemographicCohortPlanner.PlanCohort(78, 18, seed: 99);

            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].members.Count, b[i].members.Count);
                for (int j = 0; j < a[i].members.Count; j++)
                {
                    Assert.AreEqual(a[i].members[j].age, b[i].members[j].age);
                    Assert.AreEqual(a[i].members[j].isFemale, b[i].members[j].isFemale);
                    Assert.AreEqual(a[i].members[j].role, b[i].members[j].role);
                }
            }
        }

        [Test]
        public void PlanCohort_CoversInfantsToOlderResidents()
        {
            List<FamilyUnitPlan> units = DemographicCohortPlanner.PlanCohort(78, 18, seed: 5);
            List<int> ages = DemographicCohortPlanner.Flatten(units).Select(p => p.age).ToList();

            Assert.IsTrue(ages.Any(a => a <= 4), "Infants/toddlers are present.");
            Assert.IsTrue(ages.Any(a => a >= 18 && a <= 40), "Working-age adults are present.");
            Assert.IsTrue(ages.Any(a => a >= 60), "Older residents are present.");
        }
    }
}
