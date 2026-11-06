using System;
using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using LandLedgers.World.Regional;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Slice F3 (2026-11-06, Obj 19/20, Obj 29-adjacent): off-map
    /// person-time guard.
    ///
    /// SCOPE NOTE (harness adaptation, F2 precedent): the guard itself
    /// lives in SimulationDrivers.TryStartBusinessPersonTask, which is
    /// harness-excluded, so the guard wiring is SOURCE-REVIEWED ONLY —
    /// the guard sits before the already-scheduled scan and every
    /// BeginScheduledBusinessPersonTask call site in the business starters
    /// (feed, meeting, hitch, hitch travel, dispatch travel, dispatch) is
    /// preceded by a TryStartBusinessPersonTask call in the same starter.
    /// What IS harness-tested here is the exact predicate the guard reads
    /// (OffMapJourneyAuthority.IsOffMapPerson) and the domestic-driver
    /// guard (W2, Obj 45): the driver takes the authority as a handed-in
    /// property and plans/starts zero domestic tasks for an off-map person
    /// (the F3 probe, flipped in W2), resumes on return, and does not
    /// re-adopt a requeued task while the person is away. The
    /// DriveDomesticLaborDay handoff (SimulationDrivers, excluded) is
    /// source-reviewed only.
    /// </summary>
    [TestFixture]
    public sealed class FreightOffMapPersonGuardTests
    {
        private const int Ethan = 7;
        private const int Day = 10;

        private JourneyModel model;
        private OffMapJourneyAuthority authority;
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            model = new JourneyModel();
            model.RegisterLocation(new JourneyLocation("farmstead-f1", JourneyLocationKind.Farmstead, "Home Farm", 0f, 6f));
            model.RegisterLocation(new JourneyLocation("town-depot", JourneyLocationKind.OffMapGateway, "Rail Depot", 0f, 0f));
            model.RegisterLocation(new JourneyLocation("mill", JourneyLocationKind.Mill, "Grist Mill", 2f, 3f));
            model.AddEdge("farmstead-f1", "mill", 4f, "river road");
            model.AddEdge("mill", "town-depot", 3f, "depot road");
            authority = new OffMapJourneyAuthority(model);
            diag = new List<string>();
        }

        private OffMapJourneyRecord DepartEthan()
        {
            OffMapJourneyRecord record = authority.DepartPerson(
                Ethan, "farmstead-f1", "Millbrook", 1, 2, "freight",
                new Dictionary<string, int>(), new List<string>(), Day,
                _ => 0, (_, _) => null, diag);
            Assert.IsNotNull(record, "DepartPerson must create the journey record: " + string.Join("; ", diag));
            return record;
        }

        [Test]
        public void OffMapPerson_IsOffMapWhileJourneyActive()
        {
            DepartEthan();

            Assert.IsTrue(authority.IsOffMapPerson(Ethan),
                "while the freight journey is active, the guard predicate must flag Ethan off-map");
            Assert.IsFalse(authority.IsOffMapPerson(3),
                "a person with no journey is not off-map");
        }

        [Test]
        public void OffMapPerson_VehicleJourneyDoesNotFlagPerson()
        {
            OffMapJourneyRecord record = authority.DepartVehicle(
                "opening-freight-wagon", "farmstead-f1", "Millbrook", 1, "freight",
                new Dictionary<string, int>(), new List<string>(), Day,
                _ => 0, (_, _) => null, diag);
            Assert.IsNotNull(record, "DepartVehicle must create the journey record: " + string.Join("; ", diag));

            Assert.IsTrue(authority.IsOffMapVehicle("opening-freight-wagon"));
            Assert.IsFalse(authority.IsOffMapPerson(Ethan),
                "a vehicle journey must not flag the person off-map — the guard reads the person record only");
        }

        [Test]
        public void OffMapPerson_TaskStartAllowedWhenHome()
        {
            Assert.IsFalse(authority.IsOffMapPerson(Ethan),
                "with no journey, the guard predicate must allow task starts");

            DepartEthan();
            Assert.IsTrue(authority.IsOffMapPerson(Ethan));

            // Due day is day 12 (departed day 10, expected return in 2 days).
            List<OffMapJourneyRecord> completed = authority.CompleteDueJourneys(Day + 2, null, diag);
            Assert.AreEqual(1, completed.Count, string.Join("; ", diag));
            Assert.IsFalse(authority.IsOffMapPerson(Ethan),
                "after the journey returns, the guard predicate must allow task starts again");
        }

        /// <summary>
        /// W2 (Obj 45 follow-up — the F3 probe, now asserting the FIXED
        /// behaviour): with the off-map authority handed to the domestic
        /// driver, it plans ZERO tasks for Ethan while his freight journey
        /// says he is off-map, and starts none. The business-task guard
        /// (F3) covers only the business starters; this is the domestic
        /// funnel's own guard, reading the same predicate.
        /// </summary>
        [Test]
        public void OffMapPerson_DomesticAssignmentProbe()
        {
            DepartEthan();
            Assert.IsTrue(authority.IsOffMapPerson(Ethan),
                "precondition: the journey authority says Ethan is off-map");

            // Fixture shape copied from DomesticLaborDriverTests (parameterless
            // service/driver; DriveDay/AdvanceTick take the authorities as args).
            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();
            var schedules = new PersonScheduleTracker();
            var dueEvents = new SimpleDueEventScheduler();
            var service = new DomesticLaborService();
            var driver = new DomesticLaborDriver();
            driver.OffMapJourneys = authority;
            DomesticTaskCatalog.RegisterAll(tasks, new List<string>());

            var inputs = new DomesticLaborDriver.DomesticDayInputs
            {
                HouseholdId = Ethan,
                MemberIds = new List<int> { Ethan },
                AgeBandFor = _ => AgeBand.Adult18Plus,
                MealsAtHomeCount = 2,
                ColdSeason = false,
                HasAnimals = false,
            };
            var diag = new List<string>();
            driver.DriveDay(inputs, Day, tasks, budgets, schedules, service, dueEvents, diag);
            Assert.AreEqual(0, driver.Pending.Count,
                "W2: the domestic driver plans 0 tasks for an off-map person\n" + string.Join("\n", diag));
            Assert.IsTrue(diag.Exists(d => d.Contains("off-map")),
                "the skip is logged (day-keyed), not silent\n" + string.Join("\n", diag));

            // Chores are planned for minute 660; advancing the clock there
            // must start nothing for off-map Ethan.
            driver.AdvanceTick(Day * 86400f + 660f * 60f, tasks, budgets, schedules,
                service, dueEvents, new List<string>());

            Assert.AreEqual(0, driver.InFlight.Count,
                "W2: the domestic driver starts no tasks for an off-map person.");
        }

        /// <summary>
        /// W2: domestic work resumes on return — after the journey completes,
        /// the same DriveDay plans tasks for Ethan again.
        /// </summary>
        [Test]
        public void OffMapPerson_DomesticWorkResumesAfterReturn()
        {
            DepartEthan();
            Assert.IsTrue(authority.IsOffMapPerson(Ethan));

            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();
            var schedules = new PersonScheduleTracker();
            var dueEvents = new SimpleDueEventScheduler();
            var service = new DomesticLaborService();
            var driver = new DomesticLaborDriver();
            driver.OffMapJourneys = authority;
            DomesticTaskCatalog.RegisterAll(tasks, new List<string>());

            var inputs = new DomesticLaborDriver.DomesticDayInputs
            {
                HouseholdId = Ethan,
                MemberIds = new List<int> { Ethan },
                AgeBandFor = _ => AgeBand.Adult18Plus,
                MealsAtHomeCount = 2,
                ColdSeason = false,
                HasAnimals = false,
            };
            driver.DriveDay(inputs, Day, tasks, budgets, schedules, service, dueEvents, new List<string>());
            Assert.AreEqual(0, driver.Pending.Count, "off-map: 0 tasks planned");

            // Due day is day 12 (departed day 10, expected return in 2 days).
            List<OffMapJourneyRecord> completed = authority.CompleteDueJourneys(Day + 2, null, diag);
            Assert.AreEqual(1, completed.Count, string.Join("; ", diag));
            Assert.IsFalse(authority.IsOffMapPerson(Ethan),
                "after the journey returns, the guard predicate must allow task starts again");

            driver.DriveDay(inputs, Day + 2, tasks, budgets, schedules, service, dueEvents, new List<string>());
            Assert.IsTrue(driver.Pending.Count > 0,
                "on return, the domestic driver plans need-driven tasks for Ethan again");
        }

        /// <summary>
        /// W2: an off-map person with an in-flight domestic task is not
        /// re-adopted. The orphaned Assigned task is requeued by
        /// ReconcileStalled, but AdoptRequeued finds no free member — Ethan
        /// is off-map — so the task stays Queued for his return instead of
        /// being adopted (and started) while he is away.
        /// </summary>
        [Test]
        public void OffMapPerson_RequeuedDomesticTask_NotReadoptedWhileOffMap()
        {
            DepartEthan();
            Assert.IsTrue(authority.IsOffMapPerson(Ethan));

            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();
            var schedules = new PersonScheduleTracker();
            var dueEvents = new SimpleDueEventScheduler();
            var service = new DomesticLaborService();
            var driver = new DomesticLaborDriver();
            driver.OffMapJourneys = authority;
            DomesticTaskCatalog.RegisterAll(tasks, new List<string>());

            WorkTask original = tasks.CreateTask(DomesticTaskCatalog.ChoresId,
                EntityId.For(EntityKind.Household, Ethan), Day);
            Assert.IsTrue(tasks.AssignTask(original.TaskId, EntityId.For(EntityKind.Person, Ethan),
                budgets, Day, out string rejection), rejection);

            var inputs = new DomesticLaborDriver.DomesticDayInputs
            {
                HouseholdId = Ethan,
                MemberIds = new List<int> { Ethan },
                AgeBandFor = _ => AgeBand.Adult18Plus,
                MealsAtHomeCount = 0,
                ColdSeason = false,
                HasAnimals = false,
            };
            driver.DriveDay(inputs, Day, tasks, budgets, schedules, service, dueEvents, new List<string>());

            Assert.AreEqual(0, driver.Pending.Count,
                "W2: the requeued task is not re-adopted for an off-map person");
            Assert.AreEqual(TaskStatus.Queued, original.Status,
                "the orphaned task stays Queued for Ethan's return, not adopted while he is away");
        }
    }
}
