// Copyright (c) 2026 Land & Ledgers. All rights reserved.
// Pass V slice K: birthday/lifecycle chronology. Feb 29 births observe Feb 28,
// small ticks and accelerated jumps converge to the same ages with exactly one
// birthday firing per person, scheduling is idempotent across save/load, the
// deceased are excluded, and off-map persons keep aging.
using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class BirthdayChronologyTests
    {
        private CalendarAnchor anchor;
        private PopulationState population;
        private SimpleDueEventScheduler scheduler;
        private const float GameSecondsPerDay = 10f;

        [SetUp]
        public void SetUp()
        {
            anchor = CalendarAnchor.Default;
            population = new PopulationState();
            scheduler = new SimpleDueEventScheduler();
        }

        private PersonState AddPerson(int age, int birthYear, int birthMonth, int birthDay,
            BirthDateProvenance provenance = BirthDateProvenance.Exact)
        {
            var person = new PersonState
            {
                id = population.AllocateNextPersonId(),
                firstName = "Test",
                lastName = "Person",
                birthDate = new BirthDate(birthYear, birthMonth, birthDay, provenance),
            };
            anchor.FromAbsoluteDay(CurrentDay(), out int y, out int m, out int d);
            person.RefreshDerivedAge(y, m, d);
            population.people.Add(person);
            return person;
        }

        private int CurrentDay() => anchor.ToAbsoluteDay(1892, 3, 1);

        private float DueSeconds(int absoluteDay) => absoluteDay * GameSecondsPerDay;

        private int PopAndHandleBirthdays(int currentAbsoluteDay)
        {
            List<DueEvent> due = scheduler.PopDue(DueSeconds(currentAbsoluteDay));
            int handled = 0;
            foreach (DueEvent e in due)
            {
                if (e.Kind == DueEventKind.Birthday &&
                    BirthdayService.HandleBirthdayDueEvent(
                        e, population, scheduler, anchor, currentAbsoluteDay,
                        GameSecondsPerDay, null))
                {
                    handled++;
                }
            }

            return handled;
        }

        [Test]
        public void Feb29Birth_ObservesFeb28_AndSkipsToNextLeapYear()
        {
            var person = new PersonState
            {
                id = population.AllocateNextPersonId(),
                birthDate = new BirthDate(1880, 2, 29, BirthDateProvenance.Exact),
            };
            population.people.Add(person);

            int beforeAnniversary = anchor.ToAbsoluteDay(1881, 2, 27);
            string refusal = BirthdayService.EnsureScheduledForPerson(
                person, scheduler, anchor, beforeAnniversary, GameSecondsPerDay);
            Assert.IsNull(refusal);

            // Fire the Feb 28 observed birthday.
            int anniversary = anchor.ToAbsoluteDay(1881, 2, 28);
            int handled = PopAndHandleBirthdays(anniversary);
            Assert.AreEqual(1, handled);
            Assert.AreEqual(1, person.age);

            // Next birthday observes Feb 28 in the non-leap years (the project
            // convention), so it lands on 1882-02-28, not the next leap day.
            Assert.AreEqual(1, scheduler.ScheduledCount);
            int next = AgeAuthority.NextBirthdayAbsoluteDay(
                person.birthDate, anniversary + 1, anchor.Year, anchor.Month, anchor.Day);
            anchor.FromAbsoluteDay(next, out int ny, out int nm, out int nd);
            Assert.AreEqual(1882, ny);
            Assert.AreEqual(2, nm);
            Assert.AreEqual(28, nd);
        }

        [Test]
        public void SmallTicks_AndAcceleratedJump_ConvergeToSameAge_OneFiring()
        {
            // Daily-tick simulation.
            var tickPop = new PopulationState();
            var tickPerson = new PersonState
            {
                id = tickPop.AllocateNextPersonId(),
                birthDate = new BirthDate(1860, 5, 10, BirthDateProvenance.Exact),
            };
            tickPop.people.Add(tickPerson);
            var tickScheduler = new SimpleDueEventScheduler();
            int tickFirings = 0;
            for (int day = CurrentDay(); day < CurrentDay() + 365; day++)
            {
                BirthdayService.EnsureScheduledForPopulation(
                    tickPop, tickScheduler, anchor, day, GameSecondsPerDay, null);
                foreach (DueEvent e in tickScheduler.PopDue(DueSeconds(day)))
                {
                    if (e.Kind == DueEventKind.Birthday &&
                        BirthdayService.HandleBirthdayDueEvent(
                            e, tickPop, tickScheduler, anchor, day, GameSecondsPerDay, null))
                    {
                        tickFirings++;
                    }
                }
            }

            // Accelerated jump: schedule at the old date, jump, then sweep.
            // The missed birthday fires exactly once at the new date.
            var jumpPop = new PopulationState();
            var jumpPerson = new PersonState
            {
                id = jumpPop.AllocateNextPersonId(),
                birthDate = new BirthDate(1860, 5, 10, BirthDateProvenance.Exact),
            };
            jumpPop.people.Add(jumpPerson);
            var jumpScheduler = new SimpleDueEventScheduler();
            BirthdayService.EnsureScheduledForPopulation(
                jumpPop, jumpScheduler, anchor, CurrentDay(), GameSecondsPerDay, null);
            int endDay = CurrentDay() + 365;
            int jumpFirings = 0;
            foreach (DueEvent e in jumpScheduler.PopDue(DueSeconds(endDay)))
            {
                if (e.Kind == DueEventKind.Birthday &&
                    BirthdayService.HandleBirthdayDueEvent(
                        e, jumpPop, jumpScheduler, anchor, endDay, GameSecondsPerDay, null))
                {
                    jumpFirings++;
                }
            }

            // 1860-05-10 to 1893-03-01: birthday passed -> 32 in both.
            Assert.AreEqual(32, tickPerson.age);
            Assert.AreEqual(32, jumpPerson.age);
            Assert.AreEqual(1, tickFirings, "Daily ticks fire the birthday exactly once.");
            Assert.AreEqual(1, jumpFirings, "The jump fires the missed birthday exactly once.");
        }

        [Test]
        public void SaveLoad_IsIdempotent_NoDuplicateBirthdays()
        {
            AddPerson(30, 1862, 3, 1);
            AddPerson(25, 1866, 7, 14);
            AddPerson(5, 1887, 2, 29 - 1); // 1887-02-28, non-leap safe

            BirthdayService.EnsureScheduledForPopulation(
                population, scheduler, anchor, CurrentDay(), GameSecondsPerDay, null);
            Assert.AreEqual(3, scheduler.ScheduledCount);

            // Save/load round-trip into a fresh scheduler.
            SimpleDueEventSchedulerSaveDto dto = scheduler.CaptureSaveDto();
            var reloaded = new SimpleDueEventScheduler();
            reloaded.LoadFromSaveDto(dto);
            Assert.AreEqual(3, reloaded.ScheduledCount);

            // Re-ensuring after load must replace, not duplicate.
            BirthdayService.EnsureScheduledForPopulation(
                population, reloaded, anchor, CurrentDay(), GameSecondsPerDay, null);
            Assert.AreEqual(3, reloaded.ScheduledCount);

            // 400 days later each birthday fires exactly once.
            int endDay = CurrentDay() + 400;
            var fireCounts = new Dictionary<int, int>();
            foreach (DueEvent e in reloaded.PopDue(DueSeconds(endDay)))
            {
                if (e.Kind != DueEventKind.Birthday)
                {
                    continue;
                }

                bool handled = BirthdayService.HandleBirthdayDueEvent(
                    e, population, reloaded, anchor, endDay, GameSecondsPerDay, null);
                Assert.IsTrue(handled);
                int pid = int.Parse(e.PayloadRef);
                fireCounts[pid] = fireCounts.TryGetValue(pid, out int c) ? c + 1 : 1;
            }

            foreach (PersonState p in population.people)
            {
                Assert.AreEqual(1, fireCounts[p.id], $"P{p.id} birthday fired exactly once.");
            }

            Assert.AreEqual(31, population.people[0].age);
            Assert.AreEqual(26, population.people[1].age);
        }

        [Test]
        public void Deceased_AreExcluded_FromSchedulingAndHandling()
        {
            PersonState person = AddPerson(40, 1852, 4, 2);
            person.deathDayIndex = CurrentDay() - 10;

            string refusal = BirthdayService.EnsureScheduledForPerson(
                person, scheduler, anchor, CurrentDay(), GameSecondsPerDay);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(0, scheduler.ScheduledCount);

            // A stale event for the deceased is consumed without effect.
            scheduler.ScheduleWithEventId(
                AgeAuthority.BirthdayEventId(person.id),
                DueEventKind.Birthday,
                DueSeconds(CurrentDay()),
                person.id.ToString());
            List<DueEvent> due = scheduler.PopDue(DueSeconds(CurrentDay()));
            Assert.AreEqual(1, due.Count);
            bool handled = BirthdayService.HandleBirthdayDueEvent(
                due[0], population, scheduler, anchor, CurrentDay(), GameSecondsPerDay, null);
            Assert.IsFalse(handled);
            Assert.AreEqual(0, scheduler.ScheduledCount, "No reschedule for the deceased.");
        }

        [Test]
        public void OffMapPersons_KeepAging()
        {
            PersonState person = AddPerson(30, 1862, 3, 1);
            person.isOffMap = true;

            string refusal = BirthdayService.EnsureScheduledForPerson(
                person, scheduler, anchor, CurrentDay(), GameSecondsPerDay);
            Assert.IsNull(refusal, "Off-map persons are still scheduled.");

            int handled = PopAndHandleBirthdays(CurrentDay());
            Assert.AreEqual(1, handled);
            Assert.AreEqual(30, person.age, "The off-map person turns 30 on their birthday.");
        }

        [Test]
        public void SaveMigration_AssignsEstimatedBirthDate_ConsistentWithAge()
        {
            var person = new PersonState
            {
                id = population.AllocateNextPersonId(),
                age = 45,
                // No valid birth date: pre-authority save.
            };
            population.people.Add(person);

            int migrated = PopulationSaveMigration.EnsureBirthDates(
                population, anchor, CurrentDay(), null);

            Assert.AreEqual(1, migrated);
            Assert.IsTrue(person.birthDate.IsValid);
            Assert.AreEqual(BirthDateProvenance.Estimated, person.birthDate.Provenance);
            anchor.FromAbsoluteDay(CurrentDay(), out int y, out int m, out int d);
            Assert.AreEqual(45, person.GetTrueAge(y, m, d),
                "The estimated date reproduces the saved age.");
        }

        [Test]
        public void InvalidBirthDate_IsRefused()
        {
            var person = new PersonState
            {
                id = population.AllocateNextPersonId(),
                // default BirthDate is invalid.
            };
            population.people.Add(person);

            string refusal = BirthdayService.EnsureScheduledForPerson(
                person, scheduler, anchor, CurrentDay(), GameSecondsPerDay);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(0, scheduler.ScheduledCount);
        }
    }
}
