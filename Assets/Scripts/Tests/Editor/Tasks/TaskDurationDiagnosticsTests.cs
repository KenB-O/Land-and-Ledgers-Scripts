using System;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Tasks
{
    /// <summary>
    /// Priority 4 Slice 0 — discriminator pins (plan 04-plan-task-duration-diagnostics.md §5 Slice 0).
    /// Tests only; no production code. Each test pins a gap from plan §1.9 (G1–G11) as a
    /// fact about the real authorities (real TaskAuthority, real WorkTask, real estimators,
    /// real WorkTimeBudgetStore). All 7 are expected to PASS: they prove the gaps as written.
    /// If any pin fails, the plan's §1 is wrong and Slice 0 stops and rewrites it.
    /// </summary>
    [TestFixture]
    public sealed class TaskDurationDiagnosticsTests
    {
        private static EntityId Person(int id)
        {
            return EntityId.For(EntityKind.Person, id);
        }

        private static EntityId Business(int id)
        {
            return EntityId.For(EntityKind.Business, id);
        }

        private static TaskAuthority CreateAuthority(string definitionId = "test.sweep", int baseMinutes = 10)
        {
            var authority = new TaskAuthority();
            string reason;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition(definitionId, "Sweep", baseMinutes), out reason), reason);
            return authority;
        }

        /// <summary>
        /// Asserts no public property or field on the type has a name containing any of the
        /// given substrings (case-insensitive). A pin that a piece of information is stored
        /// nowhere on the record.
        /// </summary>
        private static void AssertNoPublicMemberNamedContaining(Type type, params string[] substrings)
        {
            foreach (System.Reflection.PropertyInfo property in type.GetProperties())
            {
                foreach (string substring in substrings)
                {
                    Assert.IsTrue(property.Name.IndexOf(substring, StringComparison.OrdinalIgnoreCase) < 0,
                        type.Name + " exposes a public property named '" + property.Name + "' containing '" + substring + "'.");
                }
            }

            foreach (System.Reflection.FieldInfo field in type.GetFields())
            {
                foreach (string substring in substrings)
                {
                    Assert.IsTrue(field.Name.IndexOf(substring, StringComparison.OrdinalIgnoreCase) < 0,
                        type.Name + " exposes a public field named '" + field.Name + "' containing '" + substring + "'.");
                }
            }
        }

        private static bool OpenTasksContain(TaskAuthority authority, EntityId taskId)
        {
            foreach (WorkTask task in authority.GetAllOpenTasks())
            {
                if (task.TaskId == taskId)
                {
                    return true;
                }
            }

            return false;
        }

        // G1/G2: no clock timestamp on any task transition (day indexes only); no last-transition record.
        [Test]
        public void WorkTask_HasDayIndexesOnly_NoClockTimestamp()
        {
            TaskAuthority authority = CreateAuthority();
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 2);

            Assert.AreEqual(2, task.CreatedDayIndex);
            Assert.AreEqual(-1, task.StartedDayIndex);
            Assert.AreEqual(-1, task.CompletedDayIndex);

            AssertNoPublicMemberNamedContaining(typeof(WorkTask), "GameSeconds", "Timestamp", "LastTransition");
        }

        // G1: two tasks created on the same day at different clock times are
        // indistinguishable from WorkTask fields alone — day resolution only.
        // (The test would use different clock times; the tasks carry no clock.)
        [Test]
        public void TwoTasksSameDay_DifferentClockTimes_Indistinguishable()
        {
            TaskAuthority authority = CreateAuthority();
            WorkTask first = authority.CreateTask("test.sweep", Business(1), 2);
            WorkTask second = authority.CreateTask("test.sweep", Business(1), 2);

            Assert.AreNotEqual(first.TaskId, second.TaskId, "two distinct tasks");
            Assert.AreEqual(first.CreatedDayIndex, second.CreatedDayIndex);
            Assert.AreEqual(first.StartedDayIndex, second.StartedDayIndex);
            Assert.AreEqual(first.CompletedDayIndex, second.CompletedDayIndex);
            Assert.AreEqual(first.MinutesWorked, second.MinutesWorked);
            Assert.AreEqual(first.Status, second.Status);
        }

        // G5: the estimator Basis is discarded at assignment — the adapter returns an int,
        // and no WorkTask field holds which estimator or basis produced PlannedMinutes.
        [Test]
        public void EstimatorBasis_DiscardedAtAssignment()
        {
            TaskAuthority authority = CreateAuthority("fr.load-freight", 1);
            var universal = new UniversalTaskDurationEstimator(
                new ITaskDurationEstimatorV2[] { new HandlingDurationEstimator() },
                inputs => DurationEstimate.FromGameSeconds(
                    Math.Max(1, inputs.BaseMinutes) * 60f, "test-fallback"));

            // The basis exists at the estimator level: the handling estimator owns this id.
            DurationEstimate direct = universal.Estimate(new TaskDurationInputs
            {
                DefinitionId = "fr.load-freight",
                BaseMinutes = 1,
            });
            StringAssert.StartsWith("handling:", direct.Basis);

            authority.SetDurationEstimatorV2(universal);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            string reason;
            WorkTask task = authority.CreateTask("fr.load-freight", Business(1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, out reason), reason);
            Assert.Greater(task.PlannedMinutes, 0, "assignment must set a planned duration");

            AssertNoPublicMemberNamedContaining(typeof(WorkTask), "Basis", "Estimator");
        }

        // §0: a synchronous task is born and dies inside one day drive, invisible to any
        // once-a-day poll — polling alone cannot see it.
        [Test]
        public void SynchronousTask_BornAndCompletedInsideOneDayDrive_IsInvisibleToDailyPoll()
        {
            TaskAuthority authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            string reason;
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 1, out reason), reason);
            Assert.IsTrue(authority.RecordWork(task.TaskId, task.PlannedMinutes, budgets, 1, out reason), reason);

            Assert.AreEqual(TaskStatus.Complete, task.Status);
            Assert.AreEqual(TaskOutcome.Success, task.Outcome);
            Assert.AreEqual(1, task.CompletedDayIndex);
            Assert.IsTrue(!OpenTasksContain(authority, task.TaskId),
                "a task completed inside the day drive is no longer in GetAllOpenTasks(); a daily poll never sees it");
            Assert.AreEqual(0, authority.GetOpenTasks(Business(1)).Count);
        }

        // The detectable defect class (§3.5 shape): a task assigned and started on day 0
        // with no RecordWork is still InProgress when the fixture day advances to day 2.
        [Test]
        public void MultiDayStuckTask_IsVisibleAtDayBoundaries()
        {
            TaskAuthority authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(0);

            string reason;
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 0);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 0, out reason), reason);

            // Advance the fixture day to 2 with no RecordWork.
            budgets.EnsureDay(2);

            Assert.AreEqual(TaskStatus.InProgress, task.Status);
            Assert.IsTrue(task.IsOpen);
            Assert.AreEqual(0, task.MinutesWorked);
            Assert.AreEqual(0, task.CreatedDayIndex);
            Assert.AreEqual(0, task.StartedDayIndex);
            Assert.AreEqual(-1, task.CompletedDayIndex);
            Assert.IsTrue(OpenTasksContain(authority, task.TaskId));
        }

        // G7: assign/start refusal reasons are returned to the caller and stored nowhere.
        [Test]
        public void AssignRefusalReason_StoredNowhere()
        {
            TaskAuthority authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            // Exhaust the worker's 600-minute daily budget outright.
            string commitReason;
            Assert.IsTrue(budgets.GetOrCreate(Person(1)).TryCommit(WorkTimeBudget.DefaultDailyWorkMinutes, out commitReason), commitReason);
            Assert.AreEqual(0, budgets.GetOrCreate(Person(1)).MinutesRemaining);

            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);
            string refusal;
            Assert.IsFalse(authority.AssignTask(task.TaskId, Person(1), budgets, out refusal));
            Assert.IsTrue(!string.IsNullOrEmpty(refusal), "the refusal reason exists in the returned string");
            StringAssert.Contains("minutes remaining", refusal);
            Assert.AreEqual(TaskStatus.Queued, task.Status, "the refused task stays queued");

            // The refusal exists only in the returned string: the task carries no refusal field.
            AssertNoPublicMemberNamedContaining(typeof(WorkTask), "Refusal", "Rejection");
        }

        // G6: no transition events on TaskAuthority (verified by grep in Core/Tasks and Core/Time).
        [Test]
        public void NoTransitionEvents_OnTaskAuthority()
        {
            Assert.AreEqual(0, typeof(TaskAuthority).GetEvents().Length,
                "TaskAuthority exposes no public events; there is nothing for a diagnostic to subscribe to");
        }
    }
}
