// Pass IX (WS2 — People and Lifecycle Wiring): production-path regression
// tests for the A2 findings. Each test exercises the actual production
// code the live game runs (drivers, registries, payroll paths), never a
// test-only substitute:
//   A2-01 post-generation marriages conceive (membership authority, not
//         the stale legacy householdId field — with distinct non-zero
//         household ids so the old 0==0 masking cannot recur)
//   A2-02 widows/widowers remarry; historical spouse edges preserved
//   A2-10 migration ends employment; payroll refuses off-map ghosts
//   A2-05 production weekly attendance accumulator gates payroll
//   A2-04 domestic labor executes from the tick path; pending list prunes
//   A2-03 daily activity planner reserves sleep/meals/job-seeking
//   A2-06/A2-07 daily social drive produces knowledge; job-security routing
//   A2-09 estates advance one stage per day to closure
//   A2-11 adult-child split reads the employment registry
//   A2-13 production absence sweep passes the lenient predicate
//   A2-14 household id 0 is inspectable (guard < 0, not <= 0)
using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Estates;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class Pass9PeopleLifecycleWiringTests
    {
        private static readonly CalendarAnchor Anchor = CalendarAnchor.Default;
        private static int Day1892(int month, int day) => Anchor.ToAbsoluteDay(1892, month, day);

        // ---------- shared demographic fixture ----------

        private sealed class DemoWorld
        {
            public PopulationState Population = new PopulationState();
            public KinshipRegistry Kinship = new KinshipRegistry();
            public HouseholdMembershipRegistry Memberships = new HouseholdMembershipRegistry();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public HouseholdLifecycleManager Lifecycle;
            public EstateService Estates = new EstateService();
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public SimpleDueEventScheduler Scheduler = new SimpleDueEventScheduler();
            public DemographicDriverContext Ctx;

            public DemoWorld(int day, int seed = 1234)
            {
                Lifecycle = new HouseholdLifecycleManager(Population, Memberships, Kinship, Ids);
                Ctx = new DemographicDriverContext
                {
                    Population = Population,
                    Kinship = Kinship,
                    Lifecycle = Lifecycle,
                    Memberships = Memberships,
                    Estates = Estates,
                    Ids = Ids,
                    Scheduler = Scheduler,
                    Ledgers = Ledgers,
                    Employments = Employments,
                    Anchor = Anchor,
                    CurrentDayIndex = day,
                    CurrentDate = Anchor.BuildDate(day),
                    WorldSeed = seed,
                    MaleNames = new[] { "John", "William" },
                    FemaleNames = new[] { "Mary", "Anna" },
                    GameSecondsPerDay = 86400f,
                };
            }

            public PersonState MakePerson(int birthYear, int birthMonth, int birthDay, bool isFemale, string lastName = "Test")
            {
                var person = new PersonState
                {
                    id = Population.AllocateNextPersonId(),
                    firstName = isFemale ? "Mary" : "John",
                    lastName = lastName,
                    isFemale = isFemale,
                    birthDate = new BirthDate(birthYear, birthMonth, birthDay, BirthDateProvenance.GeneratedExact),
                    deathDayIndex = -1,
                };
                Anchor.FromAbsoluteDay(Ctx.CurrentDayIndex, out int y, out int m, out int d);
                person.RefreshDerivedAge(y, m, d);
                Population.people.Add(person);
                Ids.SeedKind(EntityKind.Person, person.id + 1);
                return person;
            }

            public void SetDay(int day)
            {
                Ctx.CurrentDayIndex = day;
                Ctx.CurrentDate = Anchor.BuildDate(day);
            }
        }

        // ---------- A2-01 ----------

        [Test]
        public void A2_01_PostGenerationMarriage_Conceives_ViaMembershipAuthority()
        {
            var w = new DemoWorld(Day1892(3, 15));
            // Two dummy households first, so the couple's GENERATED households
            // carry distinct NON-ZERO ids (the old 0==0 masking cannot recur).
            PersonState dummyA = w.MakePerson(1860, 1, 1, false, "DummyA");
            PersonState dummyB = w.MakePerson(1861, 1, 1, false, "DummyB");
            w.Lifecycle.FormHousehold("DummyA Household", "DummyA", dummyA.id, w.Ctx.CurrentDayIndex, HouseholdMembershipSource.Authored);
            w.Lifecycle.FormHousehold("DummyB Household", "DummyB", dummyB.id, w.Ctx.CurrentDayIndex, HouseholdMembershipSource.Authored);

            PersonState husband = w.MakePerson(1865, 4, 1, false, "Husband");
            PersonState wife = w.MakePerson(1867, 6, 1, true, "Wife");
            HouseholdState husbandHousehold = w.Lifecycle.FormHousehold(
                "Husband Household", "Husband", husband.id, w.Ctx.CurrentDayIndex, HouseholdMembershipSource.Authored);
            HouseholdState wifeHousehold = w.Lifecycle.FormHousehold(
                "Wife Household", "Wife", wife.id, w.Ctx.CurrentDayIndex, HouseholdMembershipSource.Authored);
            Assert.AreNotEqual(husbandHousehold.id, wifeHousehold.id);
            Assert.AreNotEqual(0, husbandHousehold.id, "generated household ids must be non-zero for this regression");
            Assert.AreNotEqual(0, wifeHousehold.id);

            // The legacy fields are STALE in opposite directions (as they are
            // after a real MarriageDriver marriage, which never writes them):
            // if the driver still read them, this couple could never conceive.
            husband.householdId = wifeHousehold.id;
            wife.householdId = husbandHousehold.id;

            // Post-generation marriage: spouse edge + wife transfers into the
            // husband's household through the membership authority.
            w.Kinship.AddSpouses(husband.id, wife.id, "marriage");
            Assert.IsNull(w.Lifecycle.TransferMember(wife.id, husbandHousehold.id, w.Ctx.CurrentDayIndex));
            int authorityHousehold = w.Memberships.GetActiveHouseholdId(wife.id);
            Assert.AreEqual(husbandHousehold.id, authorityHousehold);
            Assert.AreEqual(authorityHousehold, w.Memberships.GetActiveHouseholdId(husband.id));

            bool conceived = false;
            for (int i = 0; i < 2000 && !conceived; i++)
            {
                w.SetDay(Day1892(3, 15) + i);
                BirthDriver.EvaluateConceptions(w.Ctx);
                conceived = w.Ctx.Pregnancies.Count > 0;
            }

            Assert.IsTrue(conceived, "a post-generation married couple sharing a household (membership authority) must be able to conceive");
            PregnancyRecord pregnancy = w.Ctx.Pregnancies[0];
            Assert.AreEqual(authorityHousehold, pregnancy.householdId,
                "the pregnancy is stamped with the membership-authority household, not a legacy field");
            Assert.AreEqual(wife.id, pregnancy.motherPersonId);
            Assert.AreEqual(husband.id, pregnancy.fatherPersonId);
        }

        // ---------- A2-02 ----------

        [Test]
        public void A2_02_Widower_Remarries_HistoricalSpouseEdgePreserved()
        {
            var w = new DemoWorld(Day1892(3, 15));
            PersonState husband = w.MakePerson(1866, 2, 2, false, "Widower");
            PersonState firstWife = w.MakePerson(1869, 9, 9, true, "First");
            PersonState secondWife = w.MakePerson(1870, 5, 5, true, "Second");
            HouseholdState h1 = w.Lifecycle.FormHousehold("H1", "Widower", husband.id, w.Ctx.CurrentDayIndex, HouseholdMembershipSource.Authored);
            Assert.IsNull(w.Lifecycle.TransferMember(firstWife.id, h1.id, w.Ctx.CurrentDayIndex));
            w.Lifecycle.FormHousehold("H2", "Second", secondWife.id, w.Ctx.CurrentDayIndex, HouseholdMembershipSource.Authored);
            w.Kinship.AddSpouses(husband.id, firstWife.id, "marriage");

            MortalityDriver.RecordDeath(w.Ctx, firstWife, "test death");
            Assert.IsTrue(firstWife.deathDayIndex >= 0, "first wife is dead");

            // History preserved; current spouse derivation says: none.
            CollectionAssert.Contains(w.Kinship.GetSpouses(husband.id), firstWife.id,
                "the historical spouse edge is preserved for estate/kinship history");
            Assert.AreEqual(-1, MarriageDriver.CurrentSpouseId(w.Ctx, husband.id),
                "a dead spouse is not the current spouse");

            // Monthly marriage evaluation can now pair the widower again.
            bool remarried = false;
            for (int month = 0; month < 600 && !remarried; month++)
            {
                int year = 1892 + month / 12;
                int monthOfYear = month % 12 + 1;
                w.SetDay(Anchor.ToAbsoluteDay(year, monthOfYear, 15));
                MarriageDriver.EvaluateMonth(w.Ctx);
                remarried = MarriageDriver.CurrentSpouseId(w.Ctx, husband.id) == secondWife.id;
            }

            Assert.IsTrue(remarried, "the widower remarries through the real marriage driver");
            CollectionAssert.Contains(w.Kinship.GetSpouses(husband.id), firstWife.id,
                "remarriage does not erase the first marriage from kinship history");
        }

        // ---------- A2-11 ----------

        [Test]
        public void A2_11_AdultChildSplit_ReadsEmploymentRegistry_NotStaleField()
        {
            var w = new DemoWorld(Day1892(3, 15));
            PersonState parent = w.MakePerson(1860, 1, 1, false, "Parent");
            PersonState child = w.MakePerson(1872, 1, 1, false, "Child");
            HouseholdState home = w.Lifecycle.FormHousehold("Home", "Parent", parent.id, w.Ctx.CurrentDayIndex, HouseholdMembershipSource.Authored);
            Assert.IsNull(w.Lifecycle.TransferMember(child.id, home.id, w.Ctx.CurrentDayIndex));
            w.Kinship.AddParentChild(parent.id, child.id, "test");
            // The stale legacy field says "no workplace" — registry hiring
            // never writes it. The registry is the only truth here.
            child.workplaceBuildingId = -1;

            int parentHousehold = w.Memberships.GetActiveHouseholdId(child.id);
            Assert.AreEqual(home.id, parentHousehold);

            // Control: unemployed adult child never splits, whatever the seed.
            for (int seed = 0; seed < 40; seed++)
            {
                w.Ctx.WorldSeed = seed;
                HouseholdSplitDriver.EvaluateMonth(w.Ctx);
                Assert.AreEqual(home.id, w.Memberships.GetActiveHouseholdId(child.id),
                    "unemployed adult child stays (seed " + seed + ")");
            }

            // Registry-employed: a forcing seed splits them into their own household.
            Assert.IsNull(w.Employments.Register(new EmploymentRelationship
            {
                Id = "split-emp",
                EmployeePersonId = child.id,
                EmployerBusinessId = "mine-1",
                RoleDisplayName = "Miner",
                Kind = EmploymentKind.Permanent,
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(500, "test"),
                StartDayIndex = 0,
                Source = EmploymentSource.Authored,
            }));

            bool split = false;
            for (int seed = 0; seed < 400 && !split; seed++)
            {
                w.Ctx.WorldSeed = seed;
                HouseholdSplitDriver.EvaluateMonth(w.Ctx);
                split = w.Memberships.GetActiveHouseholdId(child.id) != home.id;
            }

            Assert.IsTrue(split, "a registry-employed adult child can split into their own household");
            Assert.AreNotEqual(-1, w.Memberships.GetActiveHouseholdId(child.id));
        }

        // ---------- A2-10 + A2-05 (ghost wages) ----------

        private sealed class PayrollFixture
        {
            public BusinessInstanceState Business;
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public HouseholdMembershipRegistry Memberships = new HouseholdMembershipRegistry();
            public WeeklyAttendanceAccumulator Attendance = new WeeklyAttendanceAccumulator();
            public HashSet<int> Unavailable = new HashSet<int>();

            public PayrollFixture()
            {
                BusinessProfileDefinition profile = BusinessProfileDefinition.CreateFallback(
                    BusinessType.Blacksmith, "Test Business");
                Business = BusinessInstanceState.Create(
                    "pass9_payroll", profile, 0, BusinessOwnerIdentity.Player());
                Business.RuntimeState.EmploymentRegistry = Employments;
                Business.RuntimeState.HouseholdLedgers = Ledgers;
                Business.RuntimeState.PersonHouseholdIdLookup = Memberships.GetActiveHouseholdId;
                Business.RuntimeState.SetCurrentCashCents(10000);
                Business.RuntimeState.PersonUnavailableForPayrollLookup = id => Unavailable.Contains(id);
                Attendance.InstallOn(Business.RuntimeState, Employments, id => Unavailable.Contains(id));
            }

            public EmploymentRelationship Hire(int personId, int householdId, int wage)
            {
                var employment = new EmploymentRelationship
                {
                    Id = "pass9-emp-" + personId,
                    EmployeePersonId = personId,
                    EmployerBusinessId = Business.RuntimeState.BusinessId,
                    RoleDisplayName = "Hand",
                    Kind = EmploymentKind.Permanent,
                    LifecycleState = EmploymentLifecycleState.Active,
                    Compensation = CompensationTerms.FromWeeklyWage(wage, "test"),
                    StartDayIndex = 0,
                    Source = EmploymentSource.Authored,
                };
                Assert.IsNull(Employments.Register(employment));
                Assert.IsNull(Memberships.Register(new HouseholdMembership
                {
                    MembershipId = personId + 1000,
                    PersonId = personId,
                    HouseholdId = householdId,
                    Lifecycle = HouseholdMembershipLifecycle.Active,
                    StartDayIndex = 0,
                    EndDayIndex = -1,
                    Source = HouseholdMembershipSource.Authored,
                }));
                Ledgers.GetOrCreate(householdId);
                return employment;
            }
        }

        [Test]
        public void A2_10_Migration_EndsEmployment_WithMigratedOutReason()
        {
            var w = new DemoWorld(Day1892(3, 15));
            PersonState migrant = w.MakePerson(1870, 1, 1, false, "Migrant");
            w.Lifecycle.FormHousehold("Migrant Household", "Migrant", migrant.id, w.Ctx.CurrentDayIndex, HouseholdMembershipSource.Authored);
            migrant.laborUrgency01 = 0.95f; // distressed: monthly departure probability 0.02
            var employment = new EmploymentRelationship
            {
                Id = "migrant-emp",
                EmployeePersonId = migrant.id,
                EmployerBusinessId = "mine-1",
                RoleDisplayName = "Miner",
                Kind = EmploymentKind.Permanent,
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(500, "test"),
                StartDayIndex = 0,
                Source = EmploymentSource.Authored,
            };
            Assert.IsNull(w.Employments.Register(employment));

            bool migrated = false;
            for (int seed = 0; seed < 2000 && !migrated; seed++)
            {
                w.Ctx.WorldSeed = seed;
                MigrationDriver.EvaluateMonth(w.Ctx);
                migrated = migrant.isOffMap;
            }

            Assert.IsTrue(migrated, "the distressed adult migrates out");
            Assert.AreEqual(EmploymentLifecycleState.Ended, employment.LifecycleState,
                "migration ends the local employment");
            Assert.AreEqual(EmploymentEndReason.MigratedOut, employment.EndReason,
                "the end reason is the appended MigratedOut value (existing values never renumbered)");
            Assert.AreEqual(5, (int)EmploymentEndReason.Death, "append-only: Death keeps its serialized value");
            Assert.AreEqual(6, (int)EmploymentEndReason.MigratedOut);
            Assert.AreEqual(0, w.Employments.GetActiveByEmployee(migrant.id).Count);
        }

        [Test]
        public void A2_10_Payroll_RefusesOffMapGhost_EvenWithRecordedMinutes()
        {
            var f = new PayrollFixture();
            EmploymentRelationship ghost = f.Hire(21, 210, 800);
            EmploymentRelationship worker = f.Hire(22, 220, 800);
            f.Unavailable.Add(21); // off-map (or dead): payroll must refuse

            // The ghost "worked" earlier in the week; the present worker too.
            f.Attendance.RecordWorkedMinutes(21, 8, 300);
            f.Attendance.RecordWorkedMinutes(22, 8, 300);
            f.Business.RuntimeState.PayrollDayIndex = 13; // week 1

            f.Business.RuntimeState.ResolveWeeklyPayroll();

            Assert.AreEqual(9200, f.Business.RuntimeState.CurrentCashCents,
                "only the present, attended worker is paid");
            Assert.AreEqual(0, f.Ledgers.Get(210).GetBalanceCents(), "the ghost's household receives nothing");
            Assert.AreEqual(800, f.Ledgers.Get(220).GetBalanceCents());
            StringAssert.Contains("off-map",
                string.Join(" ", f.Business.RuntimeState.LastWeeklyPayrollDiagnostics),
                "the ghost refusal is loud");
            Assert.AreEqual(EmploymentLifecycleState.Active, ghost.LifecycleState,
                "the guard refuses payment; it does not rewrite the record (migration/death end records at the source)");
        }

        [Test]
        public void A2_05_AttendanceAccumulator_PaysOnlyWeeksActuallyWorked()
        {
            var f = new PayrollFixture();
            f.Hire(31, 310, 800);
            f.Hire(32, 320, 700);

            // Week 1 (days 7..13): worker 31 works through the real budget
            // store; worker 32 records nothing. Snapshot BEFORE the day roll,
            // exactly as SimulationDrivers.OnDayChanged does.
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(8);
            budgets.GetOrCreate(EntityId.For(EntityKind.Person, 31)).RecordWorked(240);
            int observed = f.Attendance.ObserveDay(budgets, 8);
            Assert.AreEqual(1, observed);
            budgets.EnsureDay(9); // rollover resets MinutesWorked
            Assert.AreEqual(0, f.Attendance.ObserveDay(budgets, 9), "no minutes, nothing observed");
            Assert.AreEqual(0, f.Attendance.ObserveDay(budgets, 9), "same-day re-observation cannot double-count");

            f.Business.RuntimeState.PayrollDayIndex = 13; // week 1
            f.Business.RuntimeState.ResolveWeeklyPayroll();

            Assert.AreEqual(9200, f.Business.RuntimeState.CurrentCashCents, "only the worked week is paid");
            Assert.AreEqual(800, f.Ledgers.Get(310).GetBalanceCents());
            Assert.AreEqual(0, f.Ledgers.Get(320).GetBalanceCents(), "a mere employment record earns no wage");

            // DTO-rebuilt runtime state: a fresh state carrying only the
            // registry/ledgers gets the provider installed the same way and
            // behaves identically (week 2: nobody worked -> nobody paid).
            BusinessProfileDefinition profile = BusinessProfileDefinition.CreateFallback(
                BusinessType.Blacksmith, "Rebuilt Business");
            BusinessInstanceState rebuilt = BusinessInstanceState.Create(
                "pass9_payroll", profile, 0, BusinessOwnerIdentity.Player());
            rebuilt.RuntimeState.EmploymentRegistry = f.Employments;
            rebuilt.RuntimeState.HouseholdLedgers = f.Ledgers;
            rebuilt.RuntimeState.PersonHouseholdIdLookup = f.Memberships.GetActiveHouseholdId;
            rebuilt.RuntimeState.SetCurrentCashCents(10000);
            f.Attendance.InstallOn(rebuilt.RuntimeState, f.Employments, id => f.Unavailable.Contains(id));
            rebuilt.RuntimeState.PayrollDayIndex = 20; // week 2
            rebuilt.RuntimeState.ResolveWeeklyPayroll();
            Assert.AreEqual(10000, rebuilt.RuntimeState.CurrentCashCents,
                "rebuilt runtime: no attendance in week 2, no wages");
        }

        // ---------- A2-04 ----------

        [Test]
        public void A2_04_DomesticLabor_ExecutesFromTickPath_AndPendingDoesNotGrow()
        {
            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();
            var schedules = new PersonScheduleTracker();
            var domestic = new DomesticLaborService();
            var driver = new DomesticLaborDriver();
            var familyLabor = new FamilyLaborLedger();
            var dueEvents = new SimpleDueEventScheduler();
            var diag = new List<string>();
            DomesticTaskCatalog.RegisterAll(tasks, diag);

            var inputs = new DomesticLaborDriver.DomesticDayInputs
            {
                HouseholdId = 7,
                MemberIds = new List<int> { 3, 4 },
                AgeBandFor = _ => AgeBand.Adult18Plus,
                MealsAtHomeCount = 2,
                ColdSeason = false,
                HasAnimals = false,
            };

            int maxPending = 0;
            for (int day = 100; day < 103; day++)
            {
                driver.DriveDay(inputs, day, tasks, budgets, schedules, domestic, dueEvents, diag);
                // The production tick path (SimulationDrivers.OnShortTick):
                // AdvanceTick starts due tasks; due events route domestic
                // completions through HasInFlightTask -> HandleDueEvent.
                for (int tick = 0; tick < 288; tick++)
                {
                    float now = day * 86400f + tick * 300f;
                    driver.AdvanceTick(now, tasks, budgets, schedules, domestic, dueEvents, diag);
                    foreach (DueEvent ev in dueEvents.PopDue(now))
                    {
                        if (driver.HasInFlightTask(ev.PayloadRef))
                        {
                            driver.HandleDueEvent(ev, tasks, budgets, schedules, domestic, familyLabor, dueEvents, day, diag);
                        }
                    }
                    if (driver.Pending.Count > maxPending) maxPending = driver.Pending.Count;
                }
            }

            Assert.Greater(familyLabor.ContributionCount, 0,
                "domestic tasks planned on the day path actually execute on the tick path\n" + string.Join("\n", diag));
            Assert.LessOrEqual(maxPending, 4,
                "the pending list is pruned as days pass — it cannot grow monotonically (saw " + maxPending + ")");
            foreach (DomesticLaborDriver.PendingDomesticTask p in driver.Pending)
            {
                Assert.AreEqual(102, p.DayIndex, "only today's unstarted plans remain pending");
            }
        }

        // ---------- A2-03 ----------

        [Test]
        public void A2_03_DailyPlanner_ReservesSleepMealsAndJobSeeking_WorkCannotOverlapSleep()
        {
            var schedules = new PersonScheduleTracker();
            var budgets = new WorkTimeBudgetStore();
            var planner = new DailyActivityPlanner();
            var diag = new List<string>();
            const int day = 200;

            // The exact production call SimulationDrivers.DriveDailyActivityPlans makes.
            List<DailyActivityPlanner.PersonDayPlan> plans = planner.PlanHouseholdDay(
                new List<int> { 41, 42 },
                personId => new DailyActivityPlanner.PersonDayPlanInputs
                {
                    PersonId = personId,
                    HouseholdId = 9,
                    AgeBand = AgeBand.Adult18Plus,
                    IsJobSeeker = personId == 42, // 41 employed, 42 seeking
                    IsManager = false,
                },
                day, schedules, budgets, diag);

            Assert.AreEqual(2, plans.Count);
            foreach (int personId in new[] { 41, 42 })
            {
                bool hasSleep = false, hasMeal = false;
                foreach (PersonActivityReservation r in schedules.ActiveFor(personId, day))
                {
                    if (r.Activity == PersonActivityKind.Sleeping) hasSleep = true;
                    if (r.Activity == PersonActivityKind.Eating) hasMeal = true;
                }
                Assert.IsTrue(hasSleep, $"P{personId} holds a real Sleeping reservation");
                Assert.IsTrue(hasMeal, $"P{personId} holds a real Eating reservation");
            }

            bool seekerHasBlock = false, employedHasBlock = false;
            foreach (PersonActivityReservation r in schedules.ActiveFor(42, day))
                if (r.Activity == PersonActivityKind.JobSeeking) seekerHasBlock = true;
            foreach (PersonActivityReservation r in schedules.ActiveFor(41, day))
                if (r.Activity == PersonActivityKind.JobSeeking) employedHasBlock = true;
            Assert.IsTrue(seekerHasBlock, "the job-seeker's JobSeeking block is really reserved");
            Assert.IsFalse(employedHasBlock, "the employed member gets no JobSeeking block");

            // Canon §22: work cannot be scheduled over sleep (01:00 is inside
            // the 00:00-07:00 sleep segment).
            string refusal = schedules.TryReserve(41, PersonActivityKind.Work, day, 60, 60, "night work");
            Assert.IsNotNull(refusal, "a Work reservation overlapping sleep is refused by the tracker");
        }

        // ---------- A2-06 + A2-07 ----------

        [Test]
        public void A2_06_A2_07_SocialDrive_ProducesKnowledge_AndRoutesCorroboratedSeekWork()
        {
            const int day = 5000;
            var population = new PopulationState();
            var memberships = new HouseholdMembershipRegistry();
            var kinship = new KinshipRegistry();
            var ids = new EntityIdRegistry();
            var lifecycle = new HouseholdLifecycleManager(population, memberships, kinship, ids);
            var schedules = new PersonScheduleTracker();
            var employments = new EmploymentRelationshipRegistry();
            var knowledge = new KnowledgeTransmissionStore();
            var recruiting = new RecruitmentService();
            var diag = new List<string>();

            PersonState MakeSocialPerson(int id, int birthYear)
            {
                var person = new PersonState
                {
                    id = id,
                    firstName = "P" + id,
                    lastName = "Social",
                    birthDate = new BirthDate(birthYear, 1, 1, BirthDateProvenance.GeneratedExact),
                    deathDayIndex = -1,
                };
                Anchor.FromAbsoluteDay(day, out int y, out int m, out int d);
                person.RefreshDerivedAge(y, m, d);
                population.people.Add(person);
                return person;
            }

            // 101 was dismissed from mine-1 (firsthand layoff knowledge);
            // 102 and 103 still work there.
            MakeSocialPerson(101, 1865);
            MakeSocialPerson(102, 1866);
            MakeSocialPerson(103, 1867);
            HouseholdState home = lifecycle.FormHousehold("Social Household", "Social", 101, day, HouseholdMembershipSource.Authored);
            Assert.IsNull(lifecycle.TransferMember(102, home.id, day));
            Assert.IsNull(lifecycle.TransferMember(103, home.id, day));

            void Employ(string id, int personId, string business, EmploymentLifecycleState state, EmploymentEndReason endReason)
            {
                Assert.IsNull(employments.Register(new EmploymentRelationship
                {
                    Id = id,
                    EmployeePersonId = personId,
                    EmployerBusinessId = business,
                    RoleDisplayName = "Miner",
                    Kind = EmploymentKind.Permanent,
                    LifecycleState = state,
                    EndReason = endReason,
                    EndDayIndex = state == EmploymentLifecycleState.Ended ? day - 1 : -1,
                    Compensation = CompensationTerms.FromWeeklyWage(600, "test"),
                    StartDayIndex = day - 60,
                    Source = EmploymentSource.Authored,
                }));
            }
            Employ("s-emp-101", 101, "mine-1", EmploymentLifecycleState.Ended, EmploymentEndReason.Dismissed);
            Employ("s-emp-102", 102, "mine-1", EmploymentLifecycleState.Active, EmploymentEndReason.None);
            Employ("s-emp-103", 103, "mine-1", EmploymentLifecycleState.Active, EmploymentEndReason.None);

            var businesses = new Dictionary<string, BusinessInstanceState>(StringComparer.Ordinal);
            foreach (string businessId in new[] { "saloon-1", "mine-1", "mine-2" })
            {
                BusinessProfileDefinition profile = BusinessProfileDefinition.CreateFallback(BusinessType.Saloon, businessId);
                businesses[businessId] = BusinessInstanceState.Create(businessId, profile, 0, BusinessOwnerIdentity.Player());
            }

            RecruitmentEffort effort = recruiting.OpenEffort("mine-2", "miner", RecruitmentChannel.WordOfMouth, day, 4242, diag);
            Assert.IsNotNull(effort, "mine-2 runs a real hiring effort");

            // Pick a world seed for which the drive's deterministic visit
            // draws send both the teller (101) and the listener (102) out.
            int seed = -1;
            for (int candidate = 0; candidate < 5000; candidate++)
            {
                if (DemographicRng.Draw01(candidate, day, 101, "social-visit") < DailySocialDrive.DailyVisitProbability
                    && DemographicRng.Draw01(candidate, day, 102, "social-visit") < DailySocialDrive.DailyVisitProbability)
                {
                    seed = candidate;
                    break;
                }
            }
            Assert.GreaterOrEqual(seed, 0, "a seed exists where both visit");

            var ctx = new DailySocialDriveContext
            {
                Population = population,
                Schedules = schedules,
                Employments = employments,
                Knowledge = knowledge,
                Recruiting = recruiting,
                BusinessById = id => businesses.TryGetValue(id, out BusinessInstanceState b) ? b : null,
                VenueBusinessIds = new List<string> { "saloon-1" },
                DayIndex = day,
                WorldSeed = seed,
            };

            DailySocialDayReport report = DailySocialDrive.DriveDay(ctx, diag);

            Assert.GreaterOrEqual(report.VisitsPlanned, 2, "visits planned on real person time\n" + string.Join("\n", diag));
            Assert.GreaterOrEqual(report.PacketsRecorded, 1, "the conversation recorded a knowledge packet\n" + string.Join("\n", diag));
            string subject = SocialEvidenceEvaluator.LayoffRiskSubject("mine-1");
            Assert.GreaterOrEqual(knowledge.PacketsFor(102, subject).Count, 1,
                "P102 actually received the layoff telling (provenance-carrying packet)");
            Assert.AreEqual(1, knowledge.IndependentSourceCount(102, subject),
                "one telling, one chain root — no fabricated independent sources");
            Assert.GreaterOrEqual(report.SeekWorkDecisions, 1,
                "the corroborated firsthand telling moves P102 to seek work");
            Assert.AreEqual(1, report.InquiriesFiled,
                "the seek-work decision became a real CandidateInquiry at mine-2");

            bool inquiryExists = false;
            foreach (CandidateInquiry inquiry in recruiting.Inquiries)
            {
                if (inquiry.PersonId == 102 && inquiry.EffortId == effort.EffortId) inquiryExists = true;
            }
            Assert.IsTrue(inquiryExists, "the inquiry is on the real recruitment service");

            // Day 2: the same telling retold changes nothing — duplicate
            // transmissions are refused and source counts do not inflate.
            ctx.DayIndex = day + 1;
            DailySocialDrive.DriveDay(ctx, diag);
            Assert.AreEqual(1, knowledge.IndependentSourceCount(102, subject),
                "retelling never manufactures a second independent source");
        }

        // ---------- A2-09 ----------

        [Test]
        public void A2_09_Estates_AdvanceOneStagePerDay_FromDeathToClosure()
        {
            int day = Day1892(3, 1);
            var w = new DemoWorld(day);
            PersonState decedent = w.MakePerson(1847, 3, 1, false, "Decedent");
            PersonState spouse = w.MakePerson(1849, 6, 1, true, "Spouse");
            HouseholdState home = w.Lifecycle.FormHousehold("Estate Household", "Decedent", decedent.id, day, HouseholdMembershipSource.Authored);
            Assert.IsNull(w.Lifecycle.TransferMember(spouse.id, home.id, day));
            w.Kinship.AddSpouses(decedent.id, spouse.id, "test");
            w.Ledgers.GetOrCreate(home.id);

            MortalityDriver.RecordDeath(w.Ctx, decedent, "test death");
            Assert.AreEqual(1, w.Estates.AllEstates().Count, "mortality opened exactly one estate");
            Estate estate = w.Estates.AllEstates()[0];
            Assert.AreEqual(EstateStatus.Open, estate.Status);

            var driverCtx = new EstateLifecycleDriverContext
            {
                Estates = w.Estates,
                Administration = new EstateAdministrationService(),
                Ids = w.Ids,
                Kinship = w.Kinship,
                Population = w.Population,
                Anchor = Anchor,
                Obligations = new LandLedgers.Economy.Financing.FinancialObligationAuthority(),
                Memberships = w.Memberships,
                Ledgers = w.Ledgers,
                Contests = new WillContestService(),
                Probate = new ProbateService(),
                CurrentDayIndex = day,
            };
            var driver = new EstateLifecycleDriver(driverCtx);
            var diag = new List<string>();

            var stages = new List<EstateLifecycleStage> { driver.StageOf(estate.EstateId) };
            for (int i = 1; i <= 6 && driver.StageOf(estate.EstateId) != EstateLifecycleStage.Closed; i++)
            {
                int advanced = driver.AdvanceOpenEstatesOneStage(day + i, diag);
                Assert.AreEqual(1, advanced, "exactly one stage advances per day\n" + string.Join("\n", diag));
                stages.Add(driver.StageOf(estate.EstateId));
            }

            Assert.AreEqual(EstateLifecycleStage.Closed, driver.StageOf(estate.EstateId),
                "the estate reaches closure through daily advancement\n" + string.Join("\n", diag));
            Assert.AreEqual(EstateStatus.Closed, estate.Status);
            // Stages moved in order, one per day.
            Assert.AreEqual(EstateLifecycleStage.Opened, stages[0]);
            Assert.AreEqual(EstateLifecycleStage.Inventoried, stages[1]);
            Assert.AreEqual(EstateLifecycleStage.ClaimsAdjudicated, stages[2]);
            Assert.AreEqual(EstateLifecycleStage.Distributed, stages[3]);
            Assert.AreEqual(EstateLifecycleStage.Closed, stages[4]);
            // Idempotent: further days advance nothing and never re-distribute.
            Assert.AreEqual(0, driver.AdvanceOpenEstatesOneStage(day + 30, diag));
        }

        // ---------- A2-13 ----------

        [Test]
        public void A2_13_ProductionSweepComposition_LenientPredicate_KeepsReturnJourney_FreesOneWayMigrant()
        {
            var housing = new HousingAuthority();
            var diag = new List<string>();
            var model = new JourneyModel();
            model.RegisterLocation(new JourneyLocation("mill-site", JourneyLocationKind.Mill, "Grist Mill", 0.5f, 3f));
            model.RegisterLocation(new JourneyLocation("town-depot", JourneyLocationKind.OffMapGateway, "Rail Depot", 0f, 0f));
            model.AddEdge("mill-site", "town-depot", 3f, "depot road");
            var journeys = new OffMapJourneyAuthority(model);
            // The production seam SimulationDrivers installs: the dead never travel.
            journeys.IsPersonUnavailable = personId => personId == 99;

            housing.RegisterBuilding("b-1", "p-1", "house", 0, "test", diag);
            AccommodationSpace space = housing.DefineSpace("b-1", 4, diag);
            Assert.IsNotNull(space);
            Assert.IsNull(housing.TryOccupyUnderAgreement(1, 10, space.SpaceId, AccommodationArrangement.Rental, 0, "", diag, out _));
            Assert.IsNull(housing.TryOccupyUnderAgreement(2, 10, space.SpaceId, AccommodationArrangement.Rental, 0, "", diag, out _));

            // P1: legitimate return journey. P2: one-way departure.
            Assert.IsNotNull(journeys.DepartPerson(1, "mill-site", "off-map destination", 2, 10, "freight run", null, null, 5, item => 0, null, diag));
            Assert.IsNotNull(journeys.DepartPerson(2, "mill-site", "off-map destination", 2, -1, "migration", null, null, 5, item => 0, null, diag));

            // Exactly the predicate SimulationDrivers now passes (was null):
            // lenient policy + the dead never exempted.
            var sweep = new HousingAbsenceSweep();
            HousingAbsenceSweepResult result = sweep.Sweep(
                housing,
                personId => personId == 1 || personId == 2, // both departed/absent
                6,
                diag,
                hasValidAbsenceArrangement: AbsenceArrangementPolicies.LenientPolicy(
                    housing, journeys, 6, isDeceased: personId => personId == 99));

            Assert.AreEqual(1, result.OccupanciesEnded, "only the one-way migrant loses the bed");
            Assert.AreEqual(1, result.ExemptedPersonIds.Count);
            Assert.AreEqual(1, result.ExemptedPersonIds[0]);
            Assert.AreEqual(1, housing.OccupantCount(space.SpaceId), "the traveler's bed is held");
        }

        // ---------- A2-14 ----------

        // NOTE: PopulationManager itself is harness-excluded (Unity scene
        // dependencies — see harness sources.txt), so BuildHouseholdInspection
        // Summary cannot run here. The production fix is the one-line guard
        // correction in PopulationManager.FindHouseholdById (<= 0 -> < 0).
        // This test pins the state-layer facts that fix depends on: the
        // household allocator really does start at 0, and id 0 resolves.
        [Test]
        public void A2_14_HouseholdIdZero_IsARealResolvableHousehold()
        {
            var state = new PopulationState();
            int firstId = state.AllocateNextHouseholdId();
            Assert.AreEqual(0, firstId, "household ids are allocated from 0 — id 0 is a real household");

            var household = new HouseholdState
            {
                id = firstId,
                householdName = "First Household",
                surname = "First",
                lifecycleState = HouseholdLifecycleState.Active,
            };
            state.households.Add(household);

            Assert.AreSame(household, state.GetHousehold(0),
                "household 0 resolves at the state layer; only negative ids are invalid");
            Assert.IsNull(state.GetHousehold(-1));
        }
    }
}
