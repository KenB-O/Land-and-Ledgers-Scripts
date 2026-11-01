using System;
using System.Collections.Generic;
using System.Diagnostics;
using LandLedgers.Tasks;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Time;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Integration
{
    /// <summary>
    /// Performance stress fixtures: hundreds of persons, many concurrent tasks,
    /// shared equipment, simultaneous due events, multi-day accelerated sim.
    /// Reports measured wall-clock time; bounds are generous (Roslyn harness,
    /// not Unity) and exist to catch algorithmic blowups, not to tune frames.
    /// </summary>
    [TestFixture]
    public sealed class PerformanceStressTests
    {
        private static void Report(string name, long ms, long bytesAllocated = -1)
        {
            string mem = bytesAllocated >= 0 ? string.Format(", allocated ~{0}KB", bytesAllocated / 1024) : string.Empty;
            Console.WriteLine("PERF " + name + ": " + ms + "ms" + mem);
        }

        [Test]
        public void Stress_DailyPlanner_300Persons()
        {
            // 300 persons across 100 households: plan a full day each.
            var planner = new DailyActivityPlanner();
            var schedules = new PersonScheduleTracker();
            var budgets = new WorkTimeBudgetStore();
            var diag = new List<string>();
            var sw = Stopwatch.StartNew();
            long before = GC.GetTotalMemory(false);
            for (int p = 1; p <= 300; p++)
            {
                var inputs = new DailyActivityPlanner.PersonDayPlanInputs
                {
                    PersonId = p,
                    HouseholdId = 1000 + (p % 100),
                    IsManager = p % 50 == 0,
                    IsJobSeeker = p % 3 == 0,
                };
                var plan = planner.PlanPersonDay(inputs, 200, schedules, budgets, diag);
                Assert.IsNotNull(plan);
            }

            sw.Stop();
            long after = GC.GetTotalMemory(false);
            Report("DailyPlanner 300 persons", sw.ElapsedMilliseconds, after - before);
            Assert.Less(sw.ElapsedMilliseconds, 60000L, "Planning 300 persons should take well under 60s.");
        }

        [Test]
        public void Stress_Allocator_100Tasks_50Workers()
        {
            // 100 open tasks competing for 50 workers.
            var allocator = new ManagerWorkAllocator();
            var tasks = new TaskAuthority();
            string rejection;
            tasks.RegisterDefinition(new TaskDefinition("perf.field-work", "Field work", 60), out rejection);
            tasks.RegisterDefinition(new TaskDefinition("perf.thresh-grain", "Thresh grain", 90), out rejection);
            var owner = EntityId.For(EntityKind.Household, 1);
            for (int i = 0; i < 100; i++)
            {
                string def = i % 3 == 0 ? "perf.thresh-grain" : "perf.field-work";
                tasks.CreateTaskWithPlannedMinutes(def, owner, 200, 60);
            }

            var workers = new List<ManagerWorkAllocator.WorkerAvailability>();
            for (int w = 1; w <= 50; w++)
            {
                workers.Add(new ManagerWorkAllocator.WorkerAvailability
                {
                    PersonId = w,
                    BusinessInstanceId = "farm-1",
                    FreeWindows = new List<ManagerWorkAllocator.WorkWindow>
                    {
                        new ManagerWorkAllocator.WorkWindow { StartMinuteOfDay = 540, DurationMinutes = 480 },
                    },
                });
            }

            var diag = new List<string>();
            var inputs = new ManagerWorkAllocator.AllocationInputs
            {
                BusinessInstanceId = "farm-1",
                BusinessOwner = owner,
                ManagerPersonId = 7,
                DelegatedScopes = new List<string> { ManagerScopeNames.Scheduling, ManagerScopeNames.Staffing },
                Tasks = tasks,
                Budgets = new WorkTimeBudgetStore(),
                Schedules = new PersonScheduleTracker(),
                Workers = workers,
                DayIndex = 200,
                NowGameSeconds = 200 * 86400f,
                ManagerTimeCostMinutes = 0,
            };
            var sw = Stopwatch.StartNew();
            var report = allocator.Allocate(inputs, diag);
            sw.Stop();
            Report("Allocator 100 tasks/50 workers (" + report.Assignments.Count + " assigned)", sw.ElapsedMilliseconds);
            Assert.Less(sw.ElapsedMilliseconds, 60000L);
        }

        [Test]
        public void Stress_Scheduler_10000Events()
        {
            // 10,000 due events: schedule, then drain.
            var scheduler = new SimpleDueEventScheduler();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 10000; i++)
            {
                string eventId;
                scheduler.Schedule(
                    DueEventKind.ActivityWindowDue,
                    200 * 86400f + (i % 1440) * 60f,
                    "payload-" + i,
                    out eventId);
            }

            long scheduleMs = sw.ElapsedMilliseconds;
            int popped = 0;
            var due = scheduler.PopDue(201 * 86400f);
            popped = due.Count;
            sw.Stop();
            Report("Scheduler 10000 events (schedule " + scheduleMs + "ms, drain " + (sw.ElapsedMilliseconds - scheduleMs) + "ms)", sw.ElapsedMilliseconds);
            Assert.AreEqual(10000, popped);
            Assert.Less(sw.ElapsedMilliseconds, 60000L);
        }

        [Test]
        public void Stress_MultiDayAccelerated_14Days_LargeSteps()
        {
            // The full 14-day living-world sim at 100x step size: measures the
            // accelerated path end to end.
            var sim = new LivingWorldSim();
            sim.Build();
            var sw = Stopwatch.StartNew();
            long before = GC.GetTotalMemory(false);
            sim.AdvanceTo((LivingWorldSim.StartDay + LivingWorldSim.SimDays) * 86400f, 6000f);
            sw.Stop();
            long after = GC.GetTotalMemory(false);
            Report("14-day sim at 6000s steps", sw.ElapsedMilliseconds, after - before);
            var findings = sim.Reconcile();
            Assert.AreEqual(0, findings.Count, "Reconciliation must hold under acceleration: " + string.Join("; ", findings));
            Assert.Less(sw.ElapsedMilliseconds, 300000L);
        }
    }
}
