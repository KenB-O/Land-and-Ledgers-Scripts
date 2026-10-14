// Pass VIII (Worker 9): birthday non-skipping under large time steps, driven
// through the production clock (TimeManager) and the production due-event
// path (SimpleDueEventScheduler + BirthdayService), mirroring how
// SimulationDrivers dispatches birthday due-events on DayChanged.
//
// Proves: every person ages by the Gregorian simulation calendar; off-screen
// (off-map) persons keep aging; birthday events are never skipped under
// accelerated time (the collapse path still runs every crossed day); a large
// single jump converges with day-by-day stepping; the deceased are excluded.
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Population;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Time
{
    [TestFixture]
    public sealed class Pass8BirthdayLargeStepTests
    {
        private const float SecondsPerDay = 86400f;
        private static readonly CalendarAnchor Anchor = CalendarAnchor.Default;

        private sealed class ClockHarness
        {
            public TimeManager Manager;
            public PopulationState Population;
            public SimpleDueEventScheduler Scheduler;
            public readonly Dictionary<int, int> FiringsByPerson = new Dictionary<int, int>();
            public readonly List<string> Diagnostics = new List<string>();

            public ClockHarness(int startAbsoluteDay, int maxDayRolloversPerFrame)
            {
                Manager = new TimeManager();
                var settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
                settings.shortTickIntervalGameMinutes = 15f;
                settings.maxShortTicksPerFrame = 64;
                settings.maxDayRolloversPerFrame = maxDayRolloversPerFrame;
                settings.Sanitize();
                SetField(Manager, "settings", settings);
                SetField(Manager, "absoluteDayIndex", startAbsoluteDay);
                SetField(Manager, "gameSecondsIntoDay", 0f);
                SetField(Manager, "shortTickAccumulatorGameSeconds", 0f);
                SetField(Manager, "shortTickIndex", 0);
                // Mirror TimeManager.InitializeState: currentDate must derive
                // from the anchor, otherwise the first rollover compares
                // against a default date and fires spurious Week/Month events.
                SetField(Manager, "currentDate", settings.BuildDateFromAbsoluteDay(startAbsoluteDay));
                Manager.DayChanged += _ => DrainDueEvents();
                Population = new PopulationState();
                Scheduler = new SimpleDueEventScheduler();
            }

            public int AddPerson(int birthY, int birthM, int birthD, bool deceased = false, bool offMap = false)
            {
                var person = new PersonState
                {
                    id = Population.AllocateNextPersonId(),
                    firstName = "Test",
                    lastName = "Person",
                    birthDate = new BirthDate(birthY, birthM, birthD, BirthDateProvenance.Exact),
                    isOffMap = offMap,
                };
                if (deceased)
                {
                    person.deathDayIndex = CurrentDay();
                }

                Anchor.FromAbsoluteDay(CurrentDay(), out int y, out int m, out int d);
                person.RefreshDerivedAge(y, m, d);
                Population.people.Add(person);
                return person.id;
            }

            public void EnsureScheduled()
            {
                BirthdayService.EnsureScheduledForPopulation(
                    Population, Scheduler, Anchor, CurrentDay(), SecondsPerDay, Diagnostics);
            }

            public int CurrentDay() => Manager.CurrentAbsoluteDayIndex;

            public void AdvanceOneBigStep(int days)
            {
                InvokePrivate(Manager, "AdvanceGameSeconds", days * SecondsPerDay);
                DrainDueEvents();
            }

            public void AdvanceDayByDay(int days)
            {
                for (int i = 0; i < days; i++)
                {
                    InvokePrivate(Manager, "AdvanceGameSeconds", SecondsPerDay);
                }

                DrainDueEvents();
            }

            private void DrainDueEvents()
            {
                foreach (DueEvent e in Scheduler.PopDue(Manager.AbsoluteGameSeconds))
                {
                    if (e.Kind != DueEventKind.Birthday)
                    {
                        continue;
                    }

                    if (BirthdayService.HandleBirthdayDueEvent(
                        e, Population, Scheduler, Anchor, CurrentDay(), SecondsPerDay, Diagnostics))
                    {
                        int personId = ParsePersonId(e);
                        FiringsByPerson[personId] = (FiringsByPerson.TryGetValue(personId, out int c) ? c : 0) + 1;
                    }
                }
            }

            private static int ParsePersonId(DueEvent e)
            {
                string id = e.EventId ?? string.Empty;
                if (id.StartsWith(AgeAuthority.BirthdayEventIdPrefix, System.StringComparison.Ordinal)
                    && int.TryParse(id.Substring(AgeAuthority.BirthdayEventIdPrefix.Length), out int parsed))
                {
                    return parsed;
                }

                return int.TryParse(e.PayloadRef, out int fromPayload) ? fromPayload : -1;
            }
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = typeof(TimeManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "TimeManager field '" + name + "' must exist");
            field.SetValue(target, value);
        }

        private static void InvokePrivate(object target, string name, params object[] args)
        {
            MethodInfo method = typeof(TimeManager).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "TimeManager method '" + name + "' must exist");
            method.Invoke(target, args);
        }

        private static int StartDay() => Anchor.ToAbsoluteDay(1892, 3, 1);

        private static int DaysTo1897() => Anchor.ToAbsoluteDay(1897, 3, 1) - StartDay();

        private static int[] BuildCohort(ClockHarness h)
        {
            return new[]
            {
                h.AddPerson(1860, 5, 10),                       // ordinary birthday
                h.AddPerson(1880, 2, 29),                        // leap-day birth observes Feb 28
                h.AddPerson(1875, 12, 25),                      // year-end birthday
                h.AddPerson(1891, 3, 2),                        // birthday the day after start
                h.AddPerson(1850, 1, 15, deceased: true),       // deceased: excluded
                h.AddPerson(1870, 7, 4, offMap: true),          // off-map: keeps aging
            };
        }

        [Test]
        public void FiveYearJump_FiresEveryBirthdayExactlyOncePerYear_NoSkips()
        {
            // Force the collapse path: only 3 rollovers per call, so a 5-year
            // jump goes through CollapseRemainingDayRollovers.
            var h = new ClockHarness(StartDay(), maxDayRolloversPerFrame: 3);
            int[] ids = BuildCohort(h);
            h.EnsureScheduled();

            h.AdvanceOneBigStep(DaysTo1897());

            Assert.AreEqual(StartDay() + DaysTo1897(), h.CurrentDay(),
                "the clock must land on the exact target day after the big jump");
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(5, h.FiringsByPerson.TryGetValue(ids[i], out int c) ? c : 0,
                    $"living P{ids[i]} must fire exactly 5 birthdays over 5 years — none skipped, none doubled");
            }

            Assert.AreEqual(0, h.FiringsByPerson.TryGetValue(ids[4], out int dc) ? dc : 0,
                "the deceased must never fire a birthday");
            Assert.AreEqual(5, h.FiringsByPerson.TryGetValue(ids[5], out int oc) ? oc : 0,
                "an off-map person keeps aging and fires birthdays");

            Anchor.FromAbsoluteDay(h.CurrentDay(), out int y, out int m, out int d);
            var expected = new Dictionary<int, int>
            {
                { ids[0], AgeAuthority.CompletedYears(new BirthDate(1860, 5, 10), y, m, d) },
                { ids[1], AgeAuthority.CompletedYears(new BirthDate(1880, 2, 29), y, m, d) },
                { ids[2], AgeAuthority.CompletedYears(new BirthDate(1875, 12, 25), y, m, d) },
                { ids[3], AgeAuthority.CompletedYears(new BirthDate(1891, 3, 2), y, m, d) },
                { ids[5], AgeAuthority.CompletedYears(new BirthDate(1870, 7, 4), y, m, d) },
            };
            foreach (KeyValuePair<int, int> kv in expected)
            {
                PersonState person = h.Population.GetPerson(kv.Key);
                Assert.AreEqual(kv.Value, person.age,
                    $"P{kv.Key} age must be the completed-years derivation from the birth date");
            }
        }

        [Test]
        public void BigJump_ConvergesWithDayByDayStepping()
        {
            var big = new ClockHarness(StartDay(), maxDayRolloversPerFrame: 3);
            int[] bigIds = BuildCohort(big);
            big.EnsureScheduled();
            big.AdvanceOneBigStep(DaysTo1897());

            var daily = new ClockHarness(StartDay(), maxDayRolloversPerFrame: 7);
            int[] dailyIds = BuildCohort(daily);
            daily.EnsureScheduled();
            daily.AdvanceDayByDay(DaysTo1897());

            Assert.AreEqual(big.CurrentDay(), daily.CurrentDay());
            for (int i = 0; i < bigIds.Length; i++)
            {
                int bigFirings = big.FiringsByPerson.TryGetValue(bigIds[i], out int b) ? b : 0;
                int dailyFirings = daily.FiringsByPerson.TryGetValue(dailyIds[i], out int dd) ? dd : 0;
                Assert.AreEqual(dailyFirings, bigFirings,
                    $"P{bigIds[i]}: big-jump firings must match day-by-day firings");
                Assert.AreEqual(
                    daily.Population.GetPerson(dailyIds[i]).age,
                    big.Population.GetPerson(bigIds[i]).age,
                    $"P{bigIds[i]}: big-jump age must match day-by-day age");
            }
        }
    }
}
