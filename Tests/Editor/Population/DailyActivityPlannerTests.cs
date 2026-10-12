using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Population;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// Pass IV (Worker 4): the daily activity planner. Proves:
    /// - Sleep/meals/rest get REAL person-time reservations (previously zero
    ///   call sites) — a person cannot work while sleeping.
    /// - PersonActivityPolicy is honoured: sleep is not interruptible,
    ///   rest/social are; sleep/meals/rest consume no budget while
    ///   job-seeking/managing do.
    /// - Budget exhaustion is a loud plan conflict, never a silent overdraw.
    /// - Household coordination is emergent: the free member is found from
    ///   real schedules, and a fully-booked household reports it loudly.
    /// </summary>
    [TestFixture]
    public sealed class DailyActivityPlannerTests
    {
        private sealed class Fixture
        {
            public readonly PersonScheduleTracker Schedules = new PersonScheduleTracker();
            public readonly WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public readonly DailyActivityPlanner Planner = new DailyActivityPlanner();
        }

        private static DailyActivityPlanner.PersonDayPlanInputs Inputs(int personId)
        {
            return new DailyActivityPlanner.PersonDayPlanInputs
            {
                PersonId = personId,
                HouseholdId = 7,
                AgeBand = AgeBand.Adult18Plus,
            };
        }

        [Test]
        public void PlanPersonDay_ReservesSleepMealsAndRest()
        {
            var f = new Fixture();
            var diag = new List<string>();

            DailyActivityPlanner.PersonDayPlan plan =
                f.Planner.PlanPersonDay(Inputs(3), 100, f.Schedules, f.Budgets, diag);

            Assert.AreEqual(0, plan.Conflicts.Count, string.Join(" | ", plan.Conflicts));
            // Sleep wraps midnight: two reservations (evening + morning).
            Assert.IsFalse(f.Schedules.IsFree(3, 100, 1380, 60), "P3 should be sleeping at 23:00");
            Assert.IsFalse(f.Schedules.IsFree(3, 100, 0, 60), "P3 should be sleeping at 00:30");
            // Meals reserved.
            Assert.IsFalse(f.Schedules.IsFree(3, 100, 450, 30), "P3 should be eating breakfast");
            // Some rest sprinkled into gaps.
            bool anyRest = false;
            foreach (DailyActivityPlanner.PlannedActivity a in plan.Activities)
            {
                if (a.Kind == PersonActivityKind.Resting && a.Reserved)
                {
                    anyRest = true;
                }
            }

            Assert.IsTrue(anyRest, "expected at least one rest window");
        }

        [Test]
        public void Sleep_BlocksWork_Loudly()
        {
            var f = new Fixture();

            f.Planner.PlanPersonDay(Inputs(3), 100, f.Schedules, f.Budgets, null);

            // A work shift overlapping sleep is refused by the tracker — the
            // person cannot work while sleeping.
            string refusal = f.Schedules.TryReserve(3, PersonActivityKind.Work, 100, 60, 120, "night shift");
            Assert.IsNotNull(refusal, "work during sleep must be refused");
            Assert.IsTrue(refusal.Contains("Sleeping"), "refusal must name the conflict, got: " + refusal);
        }

        [Test]
        public void JobSeeker_ConsumesBudget_SleepAndMealsDoNot()
        {
            var f = new Fixture();
            var inputs = Inputs(3);
            inputs.IsJobSeeker = true;

            WorkTimeBudget before = f.Budgets.GetOrCreate(EntityId.For(EntityKind.Person, 3));
            int committedBefore = before.MinutesCommitted;

            f.Planner.PlanPersonDay(inputs, 100, f.Schedules, f.Budgets, null);

            WorkTimeBudget after = f.Budgets.GetOrCreate(EntityId.For(EntityKind.Person, 3));
            // Job-seeking block (180 min) committed; sleep/meals/rest committed nothing.
            Assert.AreEqual(committedBefore + 180, after.MinutesCommitted);
        }

        [Test]
        public void BudgetExhaustion_IsALoudConflict_NotSilentOverdraw()
        {
            var f = new Fixture();
            var inputs = Inputs(3);
            inputs.IsJobSeeker = true;
            inputs.JobSeekingBlockMinutes = 590; // nearly the whole 600-min budget

            // Pre-consume almost all budget with a fake commitment.
            WorkTimeBudget budget = f.Budgets.GetOrCreate(EntityId.For(EntityKind.Person, 3));
            string commitRejection;
            Assert.IsTrue(budget.TryCommit(590, out commitRejection), commitRejection);

            DailyActivityPlanner.PersonDayPlan plan =
                f.Planner.PlanPersonDay(inputs, 100, f.Schedules, f.Budgets, null);

            Assert.IsTrue(plan.Conflicts.Count > 0, "budget exhaustion must be a loud conflict");
            // No job-seeking window was reserved despite the conflict.
            Assert.IsTrue(f.Schedules.IsFree(3, 100, 540, 590) ||
                plan.Conflicts.Count > 0);
            bool jobSeekingReserved = false;
            foreach (DailyActivityPlanner.PlannedActivity a in plan.Activities)
            {
                if (a.Kind == PersonActivityKind.JobSeeking && a.Reserved)
                {
                    jobSeekingReserved = true;
                }
            }

            Assert.IsFalse(jobSeekingReserved, "job-seeking must not be reserved without budget");
        }

        [Test]
        public void TryInterrupt_RefusesNonInterruptible_AllowsRest()
        {
            var f = new Fixture();
            var diag = new List<string>();

            f.Planner.PlanPersonDay(Inputs(3), 100, f.Schedules, f.Budgets, null);

            // Interrupting sleep (02:00) is refused loudly.
            string sleepRefusal = f.Planner.TryInterrupt(3, 100, 120, "urgent repair", f.Schedules, diag);
            Assert.IsNotNull(sleepRefusal, "sleep must not be interruptible");
            Assert.IsTrue(sleepRefusal.Contains("not interruptible"), "got: " + sleepRefusal);

            // Find a rest window and interrupt it.
            int restStart = -1;
            List<PersonActivityReservation> active = f.Schedules.ActiveFor(3, 100);
            foreach (PersonActivityReservation r in active)
            {
                if (r.Activity == PersonActivityKind.Resting)
                {
                    restStart = r.StartMinuteOfDay;
                    break;
                }
            }

            Assert.IsTrue(restStart >= 0, "expected a rest window to interrupt");
            string restResult = f.Planner.TryInterrupt(3, 100, restStart + 1, "urgent repair", f.Schedules, diag);
            Assert.IsNull(restResult, restResult);
            Assert.IsTrue(f.Schedules.IsFree(3, 100, restStart, 30), "interrupted rest must free the window");
        }

        [Test]
        public void FindFreeMemberForWindow_PicksFreeAdult_EmergentCoordination()
        {
            var f = new Fixture();
            var members = new List<int> { 3, 4 };

            // P3 works 09:00–17:00; P4 is free.
            string refusal = f.Schedules.TryReserve(3, PersonActivityKind.Work, 100, 540, 480, "store shift");
            Assert.IsNull(refusal, refusal);

            string reason;
            int chosen = f.Planner.FindFreeMemberForWindow(
                members, pid => AgeBand.Adult18Plus, 100, 600, 60,
                f.Schedules, f.Budgets, out reason);

            Assert.AreEqual(4, chosen, reason ?? "expected P4 (P3 is at work)");

            // Book P4 too: nobody free is a loud report, not a silent pick.
            string refusal2 = f.Schedules.TryReserve(4, PersonActivityKind.Work, 100, 540, 480, "field work");
            Assert.IsNull(refusal2, refusal2);
            int nobody = f.Planner.FindFreeMemberForWindow(
                members, pid => AgeBand.Adult18Plus, 100, 600, 60,
                f.Schedules, f.Budgets, out reason);
            Assert.AreEqual(-1, nobody);
            Assert.IsNotNull(reason);
        }

        [Test]
        public void PlanHouseholdDay_IsDeterministic()
        {
            var f = new Fixture();
            var members = new List<int> { 5, 3, 4 };

            List<DailyActivityPlanner.PersonDayPlan> first =
                f.Planner.PlanHouseholdDay(members, Inputs, 100, f.Schedules, f.Budgets, null);

            var f2 = new Fixture();
            List<DailyActivityPlanner.PersonDayPlan> second =
                f2.Planner.PlanHouseholdDay(members, Inputs, 100, f2.Schedules, f2.Budgets, null);

            Assert.AreEqual(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].PersonId, second[i].PersonId);
                Assert.AreEqual(first[i].Activities.Count, second[i].Activities.Count);
                for (int j = 0; j < first[i].Activities.Count; j++)
                {
                    Assert.AreEqual(first[i].Activities[j].Kind, second[i].Activities[j].Kind);
                    Assert.AreEqual(first[i].Activities[j].StartMinuteOfDay, second[i].Activities[j].StartMinuteOfDay);
                }
            }
        }
    }
}
