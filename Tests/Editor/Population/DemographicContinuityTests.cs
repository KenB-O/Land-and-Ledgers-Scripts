// Copyright (c) 2026 Land & Ledgers. All rights reserved.
// Pass V slice M: multi-year demographic continuity. A small population runs
// three simulated years through the daily/monthly drivers plus birthday
// handling: ages stay derived from birth dates, no person id ever duplicates,
// kinship stays temporally coherent, deaths open estates with kin heirs, and
// the whole run is deterministic in the world seed.
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy.Estates;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class DemographicContinuityTests
    {
        private const int WorldSeed = 1234;
        private const float GameSecondsPerDay = 10f;

        private CalendarAnchor anchor;
        private SimulationTimeSettings timeSettings;
        private PopulationState population;
        private KinshipRegistry kinship;
        private HouseholdMembershipRegistry memberships;
        private EntityIdRegistry ids;
        private HouseholdLifecycleManager lifecycle;
        private EstateService estates;
        private SimpleDueEventScheduler scheduler;
        private DemographicDriverContext ctx;

        private int StartDay => timeSettings.GetScenarioStartAbsoluteDayIndex();

        [SetUp]
        public void SetUp()
        {
            anchor = CalendarAnchor.Default;
            timeSettings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            population = new PopulationState();
            kinship = new KinshipRegistry();
            memberships = new HouseholdMembershipRegistry();
            ids = new EntityIdRegistry();
            lifecycle = new HouseholdLifecycleManager(population, memberships, kinship, ids);
            estates = new EstateService();
            scheduler = new SimpleDueEventScheduler();

            ctx = new DemographicDriverContext
            {
                Population = population,
                Kinship = kinship,
                Lifecycle = lifecycle,
                Memberships = memberships,
                Estates = estates,
                Ids = ids,
                Scheduler = scheduler,
                Anchor = anchor,
                CurrentDayIndex = StartDay,
                WorldSeed = WorldSeed,
                MaleNames = new[] { "John", "William", "James" },
                FemaleNames = new[] { "Mary", "Anna", "Sarah" },
                GameSecondsPerDay = GameSecondsPerDay,
            };

            BuildStarterPopulation();
        }

        private PersonState MakePerson(int birthYear, int birthMonth, int birthDay, bool isFemale)
        {
            var person = new PersonState
            {
                id = population.AllocateNextPersonId(),
                firstName = isFemale ? "Mary" : "John",
                lastName = "Test",
                isFemale = isFemale,
                birthDate = new BirthDate(birthYear, birthMonth, birthDay, BirthDateProvenance.GeneratedExact),
            };
            anchor.FromAbsoluteDay(StartDay, out int y, out int m, out int d);
            person.RefreshDerivedAge(y, m, d);
            population.people.Add(person);
            ids.SeedKind(EntityKind.Person, person.id + 1);
            return person;
        }

        private void BuildStarterPopulation()
        {
            // Household A: married couple with one child.
            PersonState fatherA = MakePerson(1855, 1, 10, false);
            PersonState motherA = MakePerson(1858, 6, 20, true);
            HouseholdState householdA = lifecycle.FormHousehold(
                "A Household", "Test", fatherA.id, StartDay, HouseholdMembershipSource.Authored);
            Assert.IsNull(lifecycle.TransferMember(motherA.id, householdA.id, StartDay));
            kinship.AddSpouses(fatherA.id, motherA.id, "test-setup");

            lifecycle.RecordBirth(householdA.id, motherA.id, fatherA.id,
                "Child", "Test", StartDay,
                new BirthDate(1885, 3, 5, BirthDateProvenance.Exact));

            // Household B: younger married couple, no children yet.
            PersonState fatherB = MakePerson(1866, 2, 2, false);
            PersonState motherB = MakePerson(1869, 9, 9, true);
            HouseholdState householdB = lifecycle.FormHousehold(
                "B Household", "Test", fatherB.id, StartDay, HouseholdMembershipSource.Authored);
            Assert.IsNull(lifecycle.TransferMember(motherB.id, householdB.id, StartDay));
            kinship.AddSpouses(fatherB.id, motherB.id, "test-setup");
        }

        [Test]
        public void JanuaryFirstPopulationGeneration_DistributesBirthdaysWithoutChangingPlannedAges()
        {
            const int year = 1886;
            const int cohortSize = 200;
            var plan = new List<PlannedPerson>(cohortSize);
            for (int i = 0; i < cohortSize; i++)
            {
                plan.Add(new PlannedPerson { age = i % 70 });
            }

            List<BirthDate> dates = DemographicCohortPlanner.AssignBirthDates(
                plan, year, 1, 1, WorldSeed, "January-first regression");

            Assert.AreEqual(cohortSize, dates.Count);
            Assert.Greater(dates.Select(birth => birth.Month).Distinct().Count(), 9,
                "A Jan 1 scenario opening must not force the entire cohort's birthdays into January.");
            Assert.LessOrEqual(dates.Count(birth => birth.Month == 1 && birth.Day == 1), 3,
                "Only the small, naturally sampled Jan 1 share may have an opening-day birthday.");

            for (int i = 0; i < cohortSize; i++)
            {
                Assert.AreEqual(plan[i].age,
                    AgeAuthority.CompletedYears(dates[i], year, 1, 1),
                    $"Generated P{i} retains the planned completed-years age.");
            }

            var missingDates = plan.Select((person, index) => new PersonState
            {
                id = index,
                age = person.age,
            }).ToList();
            int assigned = DemographicCohortPlanner.AssignMissingBirthDates(
                missingDates, year, 1, 1, WorldSeed, "January-first save migration regression");

            Assert.AreEqual(cohortSize, assigned);
            Assert.Greater(missingDates.Select(person => person.birthDate.Month).Distinct().Count(), 9,
                "Missing-date migration must use the same non-concentrated birthday distribution.");
            foreach (PersonState person in missingDates)
            {
                Assert.AreEqual(person.age,
                    AgeAuthority.CompletedYears(person.birthDate, year, 1, 1),
                    $"Migrated P{person.id} retains its saved age.");
            }
        }

        [Test]
        public void LeapYearPopulationBirthdaysRemainValidInAgePreservingBirthYears()
        {
            const int year = 1888;
            const int month = 2;
            const int day = 28;
            var plan = Enumerable.Range(0, 500)
                .Select(index => new PlannedPerson { age = index % 80 })
                .ToList();

            List<BirthDate> dates = DemographicCohortPlanner.AssignBirthDates(
                plan, year, month, day, WorldSeed + 19, "leap-year regression");

            Assert.AreEqual(plan.Count, dates.Count);
            for (int i = 0; i < dates.Count; i++)
            {
                BirthDate birth = dates[i];
                Assert.LessOrEqual(birth.Day,
                    GregorianDateMath.DaysInMonth(birth.Year, birth.Month),
                    $"P{i} must receive a valid Gregorian date even when the opening year is leap.");
                Assert.AreEqual(plan[i].age,
                    AgeAuthority.CompletedYears(birth, year, month, day),
                    $"P{i} retains the planned completed-years age at the scenario opening.");
            }
        }

        private void RunDays(int days)
        {
            for (int i = 0; i < days; i++)
            {
                int day = StartDay + i;
                ctx.CurrentDayIndex = day;
                ctx.CurrentDate = timeSettings.BuildDateFromAbsoluteDay(day);

                DemographicDriverSchedule.EvaluateDay(ctx);
                DemographicDriverSchedule.EvaluateMonth(ctx);

                BirthdayService.EnsureScheduledForPopulation(
                    population, scheduler, anchor, day, GameSecondsPerDay, null);
                foreach (DueEvent e in scheduler.PopDue(day * GameSecondsPerDay))
                {
                    if (e.Kind == DueEventKind.Birthday)
                    {
                        BirthdayService.HandleBirthdayDueEvent(
                            e, population, scheduler, anchor, day, GameSecondsPerDay, null);
                    }
                }
            }
        }

        private void AssertPopulationInvariants(int endDay)
        {
            anchor.FromAbsoluteDay(endDay, out int y, out int m, out int d);

            var seen = new HashSet<int>();
            foreach (PersonState p in population.people)
            {
                Assert.IsTrue(seen.Add(p.id), $"Duplicate person id {p.id}.");
                Assert.IsTrue(p.birthDate.IsValid, $"P{p.id} has no valid birth date.");
                Assert.AreEqual(p.GetTrueAge(y, m, d), p.age,
                    $"P{p.id} age cache disagrees with the birth date.");
            }

            // Spouse edges are symmetric and only between living-or-dead real persons.
            foreach (PersonState p in population.people)
            {
                foreach (int spouseId in kinship.GetSpouses(p.id))
                {
                    Assert.IsNotNull(population.GetPerson(spouseId),
                        $"P{p.id} has a spouse edge to unknown P{spouseId}.");
                    CollectionAssert.Contains(kinship.GetSpouses(spouseId), p.id,
                        $"Spouse edge P{p.id} <-> P{spouseId} is not symmetric.");
                }

                foreach (int parentId in kinship.GetParents(p.id))
                {
                    PersonState parent = population.GetPerson(parentId);
                    Assert.IsNotNull(parent, $"P{p.id} has a parent edge to unknown P{parentId}.");
                    anchor.FromAbsoluteDay(endDay, out int ey, out int em, out int ed);
                    int gap = parent.GetTrueAge(ey, em, ed) - p.GetTrueAge(ey, em, ed);
                    Assert.GreaterOrEqual(gap, 17,
                        $"Parent/child age gap P{parentId} -> P{p.id} is {gap}, incoherent.");
                }
            }

            // Membership: every living, on-map person is in exactly one household.
            foreach (PersonState p in population.people)
            {
                if (p.deathDayIndex < 0 && !p.isOffMap)
                {
                    int householdId = memberships.GetActiveHouseholdId(p.id);
                    Assert.GreaterOrEqual(householdId, 0,
                        $"Living on-map P{p.id} has no household (ids start at 0; -1 means none).");
                }
            }
        }

        [Test]
        public void ThreeYearRun_KeepsInvariants_AgesAdvance()
        {
            int peopleBefore = population.people.Count;
            int ageSumBefore = population.people.Sum(p => p.age);

            RunDays(365 * 3);
            int endDay = StartDay + 365 * 3 - 1;

            AssertPopulationInvariants(endDay);

            int ageSumAfter = population.people.Where(p => p.deathDayIndex < 0).Sum(p => p.age);
            Assert.Greater(ageSumAfter, ageSumBefore - peopleBefore,
                "Living persons aged over the three-year run.");

            // Birthdays kept firing: the scheduler still holds exactly one event
            // per living person with a valid birth date.
            int living = population.people.Count(p => p.deathDayIndex < 0 && p.birthDate.IsValid);
            Assert.AreEqual(living, scheduler.ScheduledCount);
        }

        [Test]
        public void ThreeYearRun_IsDeterministicInWorldSeed()
        {
            RunDays(365 * 3);
            int countA = population.people.Count;
            int ageSumA = population.people.Sum(p => p.age);
            int edgesA = kinship.AllEdges.Count;
            int pregnanciesA = ctx.Pregnancies.Count;

            SetUp();
            RunDays(365 * 3);

            Assert.AreEqual(countA, population.people.Count, "Person count is deterministic.");
            Assert.AreEqual(ageSumA, population.people.Sum(p => p.age), "Ages are deterministic.");
            Assert.AreEqual(edgesA, kinship.AllEdges.Count, "Kinship growth is deterministic.");
            Assert.AreEqual(pregnanciesA, ctx.Pregnancies.Count, "Pregnancy outcomes are deterministic.");
        }

        [Test]
        public void RecordedDeath_OpensEstateWithKinHeirs_AndCancelsBirthday()
        {
            PersonState fatherA = population.people[0];
            int spouseId = kinship.GetSpouses(fatherA.id)[0];

            BirthdayService.EnsureScheduledForPopulation(
                population, scheduler, anchor, StartDay, GameSecondsPerDay, null);
            Assert.AreEqual(population.people.Count, scheduler.ScheduledCount,
                "Precondition: one birthday event per person.");
            int scheduledBefore = scheduler.ScheduledCount;

            MortalityDriver.RecordDeath(ctx, fatherA, "test-cause");

            Assert.GreaterOrEqual(fatherA.deathDayIndex, 0);
            Assert.AreEqual(scheduledBefore - 1, scheduler.ScheduledCount,
                "The decedent's birthday is cancelled at death.");

            IReadOnlyList<Estate> all = estates.AllEstates();
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual(fatherA.id, all[0].DecedentPersonId);
            CollectionAssert.IsNotEmpty(all[0].Heirs);
            Assert.IsTrue(all[0].Heirs.Any(h => h.PersonId == spouseId),
                "The surviving spouse is an heir.");

            // Estate continuity: import round-trips into a fresh service.
            var fresh = new EstateService();
            Assert.AreEqual(0, fresh.AllEstates().Count, "Fresh service starts empty.");

            foreach (Estate e in all)
            {
                fresh.ImportEstate(e);
            }

            Assert.AreEqual(1, fresh.AllEstates().Count);
            Assert.AreEqual(fatherA.id, fresh.AllEstates()[0].DecedentPersonId);
        }

        [Test]
        public void MarriageHazard_PeaksAt1890Medians_AndNeverTriggers()
        {
            // Distribution anchors: male peak 26, female peak 22 (calibration).
            Assert.Greater(MarriageDriver.MonthlyHazard(26, false), MarriageDriver.MonthlyHazard(40, false));
            Assert.Greater(MarriageDriver.MonthlyHazard(22, true), MarriageDriver.MonthlyHazard(40, true));

            // Smooth biological-plausibility ramp: vanishingly rare in childhood,
            // but no hard threshold where marriage "switches on". The ramp is a
            // smoothstep over 15-19, so hazard is exactly 0 below 15 and rises
            // smoothly after; it is a distribution shape, never a legal rule.
            Assert.Less(MarriageDriver.MonthlyHazard(14, true), MarriageDriver.MonthlyHazard(20, true));
            Assert.AreEqual(0.0, MarriageDriver.MonthlyHazard(14, true));
            Assert.Greater(MarriageDriver.MonthlyHazard(16, true), 0.0);
            Assert.Greater(MarriageDriver.MonthlyHazard(60, false), 0.0,
                "No invented upper cutoff either.");
        }

        [Test]
        public void Newborn_EntersThroughLifecycleAuthority_WithExactDate()
        {
            HouseholdState household = population.households[0];
            PersonState mother = population.people[1];

            anchor.FromAbsoluteDay(StartDay + 100, out int y, out int m, out int d);
            var birthDate = new BirthDate(y, m, d, BirthDateProvenance.Exact);
            PersonState baby = lifecycle.RecordBirth(
                household.id, mother.id, population.people[0].id,
                "New", "Test", StartDay + 100, birthDate);

            Assert.IsNotNull(baby);
            Assert.AreEqual(BirthDateProvenance.Exact, baby.birthDate.Provenance);
            Assert.AreEqual(birthDate, baby.birthDate);
            Assert.AreEqual(0, baby.age);
            CollectionAssert.Contains(kinship.GetParents(baby.id), mother.id);

            // No duplicate: the id was allocated once.
            Assert.AreEqual(1, population.people.Count(p => p.id == baby.id));
        }
    }
}
