// Copyright (c) 2026 Land & Ledgers. All rights reserved.
// Overnight 2026-11-04, slice 1: development-town mortality and fertility
// switches. Mortality is gated at MortalityDriver.RecordDeath, the single
// production death funnel; fertility is gated at BirthDriver conceptions
// (no new pregnancy) and births (due pregnancy deferred, not deleted).
// Deterministic seeds: seed 53 / day 100 / person 7 is a would-die draw for
// a seriously ill 80-year-old (draw 0.01094 < daily probability 0.01254);
// seed 5 / day 100 / wife 2 is a would-conceive draw (0.126 < 0.22).
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy;
using LandLedgers.Economy.Estates;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class DevelopmentMortalityFertilityTests
    {
        private const int Day = 100;
        private const int MortalitySeed = 53;
        private const int ConceptionSeed = 5;

        private CalendarAnchor anchor;
        private PopulationState population;
        private KinshipRegistry kinship;
        private HouseholdMembershipRegistry memberships;
        private EntityIdRegistry ids;
        private HouseholdLifecycleManager lifecycle;
        private EstateService estates;
        private SimpleDueEventScheduler scheduler;
        private EmploymentRelationshipRegistry employments;

        [SetUp]
        public void SetUp()
        {
            anchor = CalendarAnchor.Default;
            population = new PopulationState();
            kinship = new KinshipRegistry();
            memberships = new HouseholdMembershipRegistry();
            ids = new EntityIdRegistry();
            lifecycle = new HouseholdLifecycleManager(population, memberships, kinship, ids);
            estates = new EstateService();
            scheduler = new SimpleDueEventScheduler();
            employments = new EmploymentRelationshipRegistry();
        }

        private PersonState MakePerson(int id, int birthYear, int birthMonth, int birthDay, bool isFemale)
        {
            anchor.FromAbsoluteDay(Day, out int y, out int m, out int d);
            var person = new PersonState
            {
                id = id,
                firstName = isFemale ? "Mary" : "John",
                lastName = "Test",
                isFemale = isFemale,
                birthDate = new BirthDate(birthYear, birthMonth, birthDay, BirthDateProvenance.GeneratedExact),
            };
            person.RefreshDerivedAge(y, m, d);
            population.people.Add(person);
            ids.SeedKind(EntityKind.Person, id + 1);
            return person;
        }

        private DemographicDriverContext BuildCtx(
            int seed, bool enableMortality, bool enableFertility,
            List<PregnancyRecord> pregnancies, List<string> diagnostics,
            HashSet<int> suppressed = null, HashSet<int> deferred = null)
        {
            return new DemographicDriverContext
            {
                Population = population,
                Kinship = kinship,
                Lifecycle = lifecycle,
                Memberships = memberships,
                Estates = estates,
                Ids = ids,
                Scheduler = scheduler,
                Employments = employments,
                Anchor = anchor,
                CurrentDayIndex = Day,
                WorldSeed = seed,
                MaleNames = new[] { "John", "William", "James" },
                FemaleNames = new[] { "Mary", "Anna", "Sarah" },
                Pregnancies = pregnancies,
                Diagnostics = diagnostics,
                GameSecondsPerDay = 10f,
                EnableMortality = enableMortality,
                EnableFertility = enableFertility,
                SuppressedDeathPersonIds = suppressed ?? new HashSet<int>(),
                DeferredBirthMotherIds = deferred ?? new HashSet<int>(),
            };
        }

        private (PersonState elder, HouseholdState household) BuildElderHousehold()
        {
            PersonState elder = MakePerson(7, 1800, 1, 10, false);
            elder.health.conditionKind = HealthConditionKind.Illness;
            elder.health.severity = HealthConditionSeverity.Serious;
            elder.health.remainingDays = 30;
            HouseholdState household = lifecycle.FormHousehold(
                "Elder Household", "Test", elder.id, Day, HouseholdMembershipSource.Authored);
            employments.Register(new EmploymentRelationship
            {
                Id = "test-employment-7",
                EmployeePersonId = elder.id,
                EmployerBusinessId = "test-business",
                RoleDisplayName = "Labourer",
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(100, "test"),
                StartDayIndex = Day,
            });
            return (elder, household);
        }

        private (PersonState father, PersonState mother, HouseholdState household) BuildCouple()
        {
            PersonState father = MakePerson(1, 1855, 1, 10, false);
            PersonState mother = MakePerson(2, 1858, 6, 20, true);
            HouseholdState household = lifecycle.FormHousehold(
                "Couple Household", "Test", father.id, Day, HouseholdMembershipSource.Authored);
            Assert.IsNull(lifecycle.TransferMember(mother.id, household.id, Day));
            kinship.AddSpouses(father.id, mother.id, "test-setup");
            return (father, mother, household);
        }

        [Test]
        public void DevelopmentTownConfiguration_DefaultsDisableMortalityAndFertility()
        {
            var configuration = ScriptableObject.CreateInstance<DevelopmentTownConfiguration>();
            Assert.IsFalse(configuration.EnableMortality);
            Assert.IsFalse(configuration.EnableFertility);
        }

        [Test]
        public void SuppressedDeath_LeavesPersonEmploymentAndMembershipIntact()
        {
            (PersonState elder, HouseholdState household) = BuildElderHousehold();
            var diagnostics = new List<string>();
            var suppressed = new HashSet<int>();
            var ctx = BuildCtx(MortalitySeed, false, true,
                new List<PregnancyRecord>(), diagnostics, suppressed);

            MortalityDriver.RecordDeath(ctx, elder, "old age");

            Assert.AreEqual(-1, elder.deathDayIndex);
            Assert.AreEqual(household.id, memberships.GetActiveHouseholdId(elder.id));
            Assert.IsTrue(employments.GetByEmployee(elder.id).Single().IsActive);
            Assert.AreEqual(1, diagnostics.Count);
            Assert.IsTrue(diagnostics[0].Contains("[DevelopmentPopulation] Death suppressed"));
            Assert.IsTrue(diagnostics[0].Contains("Person=P7"));
            Assert.IsTrue(diagnostics[0].Contains("Cause=old age"));
            Assert.IsTrue(diagnostics[0].Contains("Mortality disabled"));
            Assert.IsFalse(diagnostics.Any(d => d.Contains("estate")));

            // A second prevented death for the same person is not logged again.
            MortalityDriver.RecordDeath(ctx, elder, "old age");
            Assert.AreEqual(1, diagnostics.Count);
        }

        [Test]
        public void EnabledDeath_RecordsDeathEndsEmploymentAndOpensEstate()
        {
            (PersonState elder, HouseholdState household) = BuildElderHousehold();
            var diagnostics = new List<string>();
            var ctx = BuildCtx(MortalitySeed, true, true,
                new List<PregnancyRecord>(), diagnostics);

            MortalityDriver.RecordDeath(ctx, elder, "old age");

            Assert.AreEqual(Day, elder.deathDayIndex);
            Assert.Less(memberships.GetActiveHouseholdId(elder.id), 0);
            Assert.IsFalse(employments.GetByEmployee(elder.id).Single().IsActive);
            Assert.IsTrue(diagnostics.Any(d => d.Contains("died: old age")));
            Assert.IsTrue(diagnostics.Any(d => d.Contains("estate")));
            _ = household;
        }

        [Test]
        public void EvaluateDay_SuppressesDeterministicDeathWhenMortalityDisabled()
        {
            (PersonState elder, _) = BuildElderHousehold();
            var diagnostics = new List<string>();
            var ctx = BuildCtx(MortalitySeed, false, true,
                new List<PregnancyRecord>(), diagnostics);

            MortalityDriver.EvaluateDay(ctx);

            Assert.AreEqual(-1, elder.deathDayIndex);
            Assert.IsTrue(diagnostics.Any(d => d.Contains("[DevelopmentPopulation] Death suppressed")));
            Assert.IsTrue(diagnostics.Any(d => d.Contains("Person=P7")));
        }

        [Test]
        public void EvaluateDay_RecordsSameDeterministicDeathWhenMortalityEnabled()
        {
            (PersonState elder, _) = BuildElderHousehold();
            var diagnostics = new List<string>();
            var ctx = BuildCtx(MortalitySeed, true, true,
                new List<PregnancyRecord>(), diagnostics);

            MortalityDriver.EvaluateDay(ctx);

            Assert.AreEqual(Day, elder.deathDayIndex);
            Assert.IsTrue(diagnostics.Any(d => d.Contains("died:")));
        }

        [Test]
        public void FertilityDisabled_BlocksConceptionThatEnabledAllows()
        {
            (PersonState father, PersonState mother, _) = BuildCouple();
            var disabledCtx = BuildCtx(ConceptionSeed, true, false,
                new List<PregnancyRecord>(), new List<string>());
            BirthDriver.EvaluateConceptions(disabledCtx);
            Assert.AreEqual(0, disabledCtx.Pregnancies.Count);

            var enabledCtx = BuildCtx(ConceptionSeed, true, true,
                new List<PregnancyRecord>(), new List<string>());
            BirthDriver.EvaluateConceptions(enabledCtx);
            Assert.AreEqual(1, enabledCtx.Pregnancies.Count);
            Assert.AreEqual(mother.id, enabledCtx.Pregnancies[0].motherPersonId);
            Assert.AreEqual(father.id, enabledCtx.Pregnancies[0].fatherPersonId);
            _ = mother;
        }

        [Test]
        public void FertilityDisabled_DefersDueBirthAndEnabledDeliversIt()
        {
            (PersonState father, PersonState mother, HouseholdState household) = BuildCouple();
            var pregnancies = new List<PregnancyRecord>
            {
                new PregnancyRecord
                {
                    motherPersonId = mother.id,
                    fatherPersonId = father.id,
                    householdId = household.id,
                    conceptionDayIndex = Day - 280,
                    dueDayIndex = Day,
                },
            };
            var diagnostics = new List<string>();
            var deferred = new HashSet<int>();
            var ctx = BuildCtx(ConceptionSeed, true, false, pregnancies, diagnostics, null, deferred);

            BirthDriver.EvaluateBirths(ctx);
            Assert.AreEqual(2, population.people.Count);
            Assert.AreEqual(1, ctx.Pregnancies.Count);
            Assert.AreEqual(1, diagnostics.Count(d => d.Contains("birth deferred")));

            // Deferred again: no duplicate diagnostic, still no birth.
            BirthDriver.EvaluateBirths(ctx);
            Assert.AreEqual(2, population.people.Count);
            Assert.AreEqual(1, ctx.Pregnancies.Count);
            Assert.AreEqual(1, diagnostics.Count(d => d.Contains("birth deferred")));

            // Re-enabled: the deferred pregnancy delivers.
            ctx.EnableFertility = true;
            BirthDriver.EvaluateBirths(ctx);
            Assert.AreEqual(3, population.people.Count);
            Assert.AreEqual(0, ctx.Pregnancies.Count);
            Assert.IsTrue(diagnostics.Any(d => d.Contains("born to")));
        }
    }
}
