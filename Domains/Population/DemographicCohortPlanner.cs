using System;
using System.Collections.Generic;
using LandLedgers.Time;

namespace LandLedgers.Population
{
    /// <summary>Role of a planned person inside a family unit.</summary>
    public enum PlannedPersonRole
    {
        Head = 0,
        Spouse = 1,
        Child = 2,
        Single = 3,
    }

    /// <summary>
    /// Pass V P1-3: one planned family unit. Households are formed FROM these
    /// units (Canon 12.6G step 5): the member list order is stable, and
    /// spouse/parent plan indices address members of the same unit.
    /// </summary>
    public sealed class FamilyUnitPlan
    {
        public List<PlannedPerson> members = new List<PlannedPerson>();
    }

    /// <summary>
    /// Pass V P1-3: one planned person in the persons-first generation order
    /// (Canon 12.6G). Family links are unit-local member indices, resolved to
    /// real PersonIds when the cohort is materialized.
    /// </summary>
    public sealed class PlannedPerson
    {
        public int age;
        public bool isFemale;
        public int familyUnitIndex;
        public PlannedPersonRole role;
        public int spousePlanIndex = -1;
        public int motherPlanIndex = -1;
        public int fatherPlanIndex = -1;
    }

    /// <summary>
    /// Pass V P1-3: persons-first demographic planning (Canon 12.6G step 1-4).
    ///
    /// Plans a whole-population cohort BEFORE any household exists: real persons
    /// with plausible ages (infants through older residents), then kinship and
    /// marital relationships with temporally coherent ages (parents 17-44 years
    /// older than their children; spouses within a plausible gap). Households
    /// are formed FROM these relationships by the generator; accommodation,
    /// economic roles, and validation follow.
    ///
    /// Pure and deterministic: same (targets, seed) = same cohort. The 55-95
    /// Canon calibration is honored by the caller (targets come from
    /// PopulationGenerationSettings, untouched).
    /// </summary>
    public static class DemographicCohortPlanner
    {
        /// <summary>Minimum parent/child age gap in years (biological plausibility).</summary>
        public const int MinParentChildGapYears = 17;

        /// <summary>Maximum parent/child age gap in years (biological plausibility).</summary>
        public const int MaxParentChildGapYears = 44;

        /// <summary>Maximum spouse age gap in years (distribution sanity bound).</summary>
        public const int MaxSpouseGapYears = 12;

        /// <summary>
        /// Plans the cohort as family units (Canon 12.6G steps 1-4). Pure and
        /// deterministic: same (targets, seed) = same units.
        /// </summary>
        public static List<FamilyUnitPlan> PlanCohort(int targetResidents, int targetHouseholds, int seed)
        {
            var units = new List<FamilyUnitPlan>();
            if (targetResidents <= 0 || targetHouseholds <= 0)
            {
                return units;
            }

            var random = new Random(seed);
            int remainingResidents = targetResidents;
            for (int unit = 0; unit < targetHouseholds; unit++)
            {
                int remainingUnits = targetHouseholds - unit;
                // Reserve at least one person per remaining unit.
                int maxForUnit = Math.Max(1, remainingResidents - (remainingUnits - 1));
                var members = PlanFamilyUnit(unit, Math.Min(6, maxForUnit), random);
                if (members.Count > maxForUnit)
                {
                    members.RemoveRange(maxForUnit, members.Count - maxForUnit);
                    RepairFamilyLinks(members);
                }

                units.Add(new FamilyUnitPlan { members = members });
                remainingResidents -= members.Count;
                if (remainingResidents <= 0)
                {
                    break;
                }
            }

            return units;
        }

        /// <summary>Flattens planned units to a person list (birth-date assignment).</summary>
        public static List<PlannedPerson> Flatten(List<FamilyUnitPlan> units)
        {
            var flat = new List<PlannedPerson>();
            if (units == null)
            {
                return flat;
            }

            for (int i = 0; i < units.Count; i++)
            {
                if (units[i] != null && units[i].members != null)
                {
                    flat.AddRange(units[i].members);
                }
            }

            return flat;
        }

        /// <summary>
        /// Assigns GeneratedExact birth dates to a planned cohort relative to
        /// the scenario start date. Deterministic in seed.
        /// </summary>
        public static List<BirthDate> AssignBirthDates(
            List<PlannedPerson> plan,
            int startYear, int startMonth, int startDay,
            int seed,
            string provenanceNote)
        {
            var dates = new List<BirthDate>();
            var random = new Random(seed ^ 0xB17DA7E);
            if (plan == null)
            {
                return dates;
            }

            for (int i = 0; i < plan.Count; i++)
            {
                int birthYear = startYear - Math.Max(0, plan[i].age);
                int birthMonth = 1 + random.Next(0, 12);
                int birthDay = 1 + random.Next(0, GregorianDateMath.DaysInMonth(birthYear, birthMonth));
                dates.Add(new BirthDate(
                    birthYear, birthMonth, birthDay,
                    BirthDateProvenance.GeneratedExact,
                    provenanceNote ?? string.Empty));
            }

            return dates;
        }

        private static List<PlannedPerson> PlanFamilyUnit(int unitIndex, int maxMembers, Random random)
        {
            var members = new List<PlannedPerson>();
            double roll = random.NextDouble();
            if (roll < 0.55)
            {
                PlanCoupleWithChildren(members, unitIndex, maxMembers, random);
            }
            else if (roll < 0.70)
            {
                PlanCouple(members, unitIndex, maxMembers, random, false);
            }
            else if (roll < 0.80)
            {
                PlanSingleParent(members, unitIndex, maxMembers, random);
            }
            else if (roll < 0.92)
            {
                PlanSingleAdult(members, unitIndex, random);
            }
            else
            {
                PlanCouple(members, unitIndex, maxMembers, random, true);
            }

            return members;
        }

        private static void PlanCoupleWithChildren(List<PlannedPerson> members, int unitIndex, int maxMembers, Random random)
        {
            int motherAge = random.Next(23, 45);
            int fatherAge = Math.Max(20, motherAge + random.Next(-2, 8));
            int motherIndex = members.Count;
            members.Add(new PlannedPerson
            {
                age = motherAge, isFemale = true, familyUnitIndex = unitIndex,
                role = PlannedPersonRole.Head, spousePlanIndex = motherIndex + 1,
            });
            members.Add(new PlannedPerson
            {
                age = fatherAge, isFemale = false, familyUnitIndex = unitIndex,
                role = PlannedPersonRole.Spouse, spousePlanIndex = motherIndex,
            });

            int childSlots = Math.Max(0, maxMembers - 2);
            int childCount = Math.Min(childSlots, 1 + random.Next(0, 4));
            for (int c = 0; c < childCount; c++)
            {
                int maxChildAge = Math.Min(17, motherAge - MinParentChildGapYears);
                if (maxChildAge < 0)
                {
                    break;
                }

                int childAge = random.Next(0, maxChildAge + 1);
                members.Add(new PlannedPerson
                {
                    age = childAge, isFemale = random.NextDouble() < 0.5, familyUnitIndex = unitIndex,
                    role = PlannedPersonRole.Child, motherPlanIndex = motherIndex, fatherPlanIndex = motherIndex + 1,
                });
            }
        }

        private static void PlanCouple(List<PlannedPerson> members, int unitIndex, int maxMembers, Random random, bool elderly)
        {
            int baseAge = elderly ? random.Next(60, 81) : random.Next(20, 61);
            int headIndex = members.Count;
            members.Add(new PlannedPerson
            {
                age = baseAge, isFemale = random.NextDouble() < 0.5, familyUnitIndex = unitIndex,
                role = PlannedPersonRole.Head, spousePlanIndex = headIndex + 1,
            });
            if (maxMembers >= 2)
            {
                int spouseAge = Math.Max(18, baseAge + random.Next(-4, 5));
                members.Add(new PlannedPerson
                {
                    age = spouseAge, isFemale = !members[headIndex].isFemale, familyUnitIndex = unitIndex,
                    role = PlannedPersonRole.Spouse, spousePlanIndex = headIndex,
                });
            }
            else
            {
                members[headIndex].spousePlanIndex = -1;
                members[headIndex].role = PlannedPersonRole.Single;
            }
        }

        private static void PlanSingleParent(List<PlannedPerson> members, int unitIndex, int maxMembers, Random random)
        {
            bool mother = random.NextDouble() < 0.7;
            int parentAge = random.Next(22, 51);
            int parentIndex = members.Count;
            members.Add(new PlannedPerson
            {
                age = parentAge, isFemale = mother, familyUnitIndex = unitIndex,
                role = PlannedPersonRole.Head,
            });

            int childSlots = Math.Max(0, maxMembers - 1);
            int childCount = Math.Min(childSlots, 1 + random.Next(0, 3));
            for (int c = 0; c < childCount; c++)
            {
                int maxChildAge = Math.Min(17, parentAge - MinParentChildGapYears);
                if (maxChildAge < 0)
                {
                    break;
                }

                int childAge = random.Next(0, maxChildAge + 1);
                var child = new PlannedPerson
                {
                    age = childAge, isFemale = random.NextDouble() < 0.5, familyUnitIndex = unitIndex,
                    role = PlannedPersonRole.Child,
                };
                if (mother)
                {
                    child.motherPlanIndex = parentIndex;
                }
                else
                {
                    child.fatherPlanIndex = parentIndex;
                }

                members.Add(child);
            }
        }

        private static void PlanSingleAdult(List<PlannedPerson> members, int unitIndex, Random random)
        {
            members.Add(new PlannedPerson
            {
                age = random.Next(18, 71), isFemale = random.NextDouble() < 0.5,
                familyUnitIndex = unitIndex, role = PlannedPersonRole.Single,
            });
        }

        /// <summary>
        /// Repairs dangling plan indices after truncation (keeps every remaining
        /// link pointing at a surviving member).
        /// </summary>
        internal static void RepairFamilyLinks(List<PlannedPerson> members)
        {
            for (int i = 0; i < members.Count; i++)
            {
                PlannedPerson p = members[i];
                if (p.spousePlanIndex < 0 || p.spousePlanIndex >= members.Count)
                {
                    p.spousePlanIndex = -1;
                    if (p.role == PlannedPersonRole.Spouse)
                    {
                        p.role = PlannedPersonRole.Single;
                    }
                }

                if (p.motherPlanIndex < 0 || p.motherPlanIndex >= members.Count)
                {
                    p.motherPlanIndex = -1;
                }

                if (p.fatherPlanIndex < 0 || p.fatherPlanIndex >= members.Count)
                {
                    p.fatherPlanIndex = -1;
                }

                if (p.motherPlanIndex < 0 && p.fatherPlanIndex < 0 && p.role == PlannedPersonRole.Child)
                {
                    p.role = PlannedPersonRole.Single;
                }
            }
        }
    }
}
