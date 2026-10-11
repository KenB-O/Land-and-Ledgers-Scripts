using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Labor;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Worker C Phase E: daily work scheduling. Deterministic. Covers:
    /// budget commits, person-time exclusivity, descriptive occupation
    /// (novice assigned, noted slower), meaningful refusal (fatigue/contract),
    /// equipment gating, sequential multi-family assignment, priority order,
    /// and the never-invent-a-worker rule.
    /// </summary>
    [TestFixture]
    public sealed class WorkSchedulerTests
    {
        private static SchedulerAvailableWorker Worker(int id, int minutes, bool fatigued = false, string restrictions = "")
        {
            return new SchedulerAvailableWorker
            {
                PersonId = id,
                AvailableMinutes = minutes,
                SkillEvidence = new Dictionary<string, float>(),
                Fatigued = fatigued,
                ContractRestrictions = restrictions
            };
        }

        private static SchedulerOpenWorkItem Item(string id, string family, int minutes, string equipment = null)
        {
            return new SchedulerOpenWorkItem
            {
                TaskId = id,
                Family = family,
                MinutesRequired = minutes,
                RequiredEquipment = equipment,
                LocationId = "store-1"
            };
        }

        private static SchedulerInputs Inputs(List<SchedulerOpenWorkItem> work, List<SchedulerAvailableWorker> workers)
        {
            return new SchedulerInputs
            {
                BusinessInstanceId = "store-1",
                DayIndex = 404,
                OpenWork = work,
                Workers = workers
            };
        }

        private static WorkAssignment Find(List<WorkAssignment> list, string taskId)
        {
            foreach (var a in list) if (a.TaskId == taskId) return a;
            return null;
        }

        [Test]
        public void AssignsOpenWorkWithinBudgets()
        {
            var scheduler = new WorkScheduler();
            var inputs = Inputs(
                new List<SchedulerOpenWorkItem> { Item("t1", "TendCustomer", 60), Item("t2", "StockShelves", 90) },
                new List<SchedulerAvailableWorker> { Worker(1, 480), Worker(2, 480) });

            WorkAssignmentReport r = scheduler.AssignDailyWork(inputs, new List<string>());

            Assert.AreEqual("store-1", r.BusinessInstanceId);
            Assert.AreEqual(404, r.DayIndex);
            Assert.AreEqual(2, r.Assignments.Count);
            Assert.AreEqual(0, r.UnassignedTaskIds.Count);
            WorkAssignment a1 = Find(r.Assignments, "t1");
            Assert.AreEqual(1, a1.PersonId);
            Assert.AreEqual(60, a1.MinutesCommitted);
        }

        [Test]
        public void NoOverCommit_OverBudgetItemUnassignedWithReason()
        {
            var scheduler = new WorkScheduler();
            var inputs = Inputs(
                new List<SchedulerOpenWorkItem> { Item("t1", "TendCustomer", 40), Item("t2", "StockShelves", 40) },
                new List<SchedulerAvailableWorker> { Worker(1, 60) });

            WorkAssignmentReport r = scheduler.AssignDailyWork(inputs, new List<string>());

            Assert.AreEqual(1, Find(r.Assignments, "t1").PersonId);
            WorkAssignment a2 = Find(r.Assignments, "t2");
            Assert.AreEqual(-1, a2.PersonId, "over-commit refused, never invented");
            Assert.AreEqual(0, a2.MinutesCommitted);
            Assert.IsTrue(r.UnassignedTaskIds.Contains("t2"), "t2 unassigned");
            StringAssert.Contains("remaining", a2.Note);
        }

        [Test]
        public void PersonTimeExclusivity_NoDoubleCommit()
        {
            // 480-minute budget; 400 + 200 cannot both land on one worker.
            var scheduler = new WorkScheduler();
            var inputs = Inputs(
                new List<SchedulerOpenWorkItem> { Item("t1", "ServeCustomer", 400), Item("t2", "Clean", 200) },
                new List<SchedulerAvailableWorker> { Worker(1, 480) });

            WorkAssignmentReport r = scheduler.AssignDailyWork(inputs, new List<string>());

            Assert.AreEqual(1, Find(r.Assignments, "t1").PersonId);
            Assert.AreEqual(-1, Find(r.Assignments, "t2").PersonId, "no overlapping assignment on P1");
            Assert.IsTrue(r.UnassignedTaskIds.Contains("t2"), "t2 unassigned");
        }

        [Test]
        public void NoviceAssignedUnfamiliarWork_NotedSlower_NeverRefused()
        {
            // Occupation is descriptive (CAN-CE1-03.2): no evidence at all,
            // yet the worker is assigned and the note says slower.
            var w = Worker(1, 480); // empty SkillEvidence
            var scheduler = new WorkScheduler();
            var inputs = Inputs(
                new List<SchedulerOpenWorkItem> { Item("t1", "Blacksmithing", 120) },
                new List<SchedulerAvailableWorker> { w });

            WorkAssignmentReport r = scheduler.AssignDailyWork(inputs, new List<string>());

            WorkAssignment a = Find(r.Assignments, "t1");
            Assert.AreEqual(1, a.PersonId, "novice is NOT refused unfamiliar work");
            StringAssert.Contains("slower", a.Note);
        }

        [Test]
        public void LowSkillEvidence_DescriptiveOnly_Assigned()
        {
            var w = Worker(1, 480);
            w.SkillEvidence["Mining"] = 0.05f;
            var scheduler = new WorkScheduler();
            var inputs = Inputs(
                new List<SchedulerOpenWorkItem> { Item("t1", "Mining", 120) },
                new List<SchedulerAvailableWorker> { w });

            WorkAssignmentReport r = scheduler.AssignDailyWork(inputs, new List<string>());

            WorkAssignment a = Find(r.Assignments, "t1");
            Assert.AreEqual(1, a.PersonId);
            StringAssert.Contains("novice", a.Note);
        }

        [Test]
        public void FatiguedWorker_SkippedWithReason()
        {
            var scheduler = new WorkScheduler();
            var inputs = Inputs(
                new List<SchedulerOpenWorkItem> { Item("t1", "TendCustomer", 60) },
                new List<SchedulerAvailableWorker> { Worker(1, 480, fatigued: true) });

            WorkAssignmentReport r = scheduler.AssignDailyWork(inputs, new List<string>());

            WorkAssignment a = Find(r.Assignments, "t1");
            Assert.AreEqual(-1, a.PersonId);
            StringAssert.Contains("fatigued", a.Note);
            Assert.IsTrue(r.UnassignedTaskIds.Contains("t1"), "t1 unassigned");
        }

        [Test]
        public void ContractRestriction_SkippedWithReason()
        {
            var scheduler = new WorkScheduler();
            var inputs = Inputs(
                new List<SchedulerOpenWorkItem> { Item("t1", "TendCustomer", 60) },
                new List<SchedulerAvailableWorker> { Worker(1, 480, restrictions: "no night work") });

            WorkAssignmentReport r = scheduler.AssignDailyWork(inputs, new List<string>());

            WorkAssignment a = Find(r.Assignments, "t1");
            Assert.AreEqual(-1, a.PersonId);
            StringAssert.Contains("contract restriction", a.Note);
        }

        [Test]
        public void EquipmentGate_AssignsOnlyQualifiedWorker()
        {
            var scheduler = new WorkScheduler();
            var inputs = Inputs(
                new List<SchedulerOpenWorkItem> { Item("t1", "Forge", 60, "forge") },
                new List<SchedulerAvailableWorker> { Worker(1, 480), Worker(2, 480) });

            WorkAssignmentReport r = scheduler.AssignDailyWork(inputs,
                (equipment, personId) => personId == 2, new List<string>());

            Assert.AreEqual(2, Find(r.Assignments, "t1").PersonId);
        }

        [Test]
        public void EquipmentGateNull_NoGate_FirstFit()
        {
            var scheduler = new WorkScheduler();
            var inputs = Inputs(
                new List<SchedulerOpenWorkItem> { Item("t1", "Forge", 60, "forge") },
                new List<SchedulerAvailableWorker> { Worker(1, 480), Worker(2, 480) });

            WorkAssignmentReport r = scheduler.AssignDailyWork(inputs, new List<string>());

            Assert.AreEqual(1, Find(r.Assignments, "t1").PersonId, "null equipmentCheck = no gate");
        }

        [Test]
        public void EquipmentGate_AllFail_UnassignedNeverInventsWorker()
        {
            var scheduler = new WorkScheduler();
            var inputs = Inputs(
                new List<SchedulerOpenWorkItem> { Item("t1", "Forge", 60, "forge") },
                new List<SchedulerAvailableWorker> { Worker(1, 480) });

            WorkAssignmentReport r = scheduler.AssignDailyWork(inputs,
                (equipment, personId) => false, new List<string>());

            WorkAssignment a = Find(r.Assignments, "t1");
            Assert.AreEqual(-1, a.PersonId, "never invents a worker");
            StringAssert.Contains("equipment", a.Note);
            Assert.IsTrue(r.UnassignedTaskIds.Contains("t1"), "t1 unassigned");
        }

        [Test]
        public void NoWorkers_AllUnassigned_NeverInventsWorker()
        {
            var scheduler = new WorkScheduler();
            var inputs = Inputs(
                new List<SchedulerOpenWorkItem> { Item("t1", "TendCustomer", 60), Item("t2", "Clean", 30) },
                new List<SchedulerAvailableWorker>());

            WorkAssignmentReport r = scheduler.AssignDailyWork(inputs, new List<string>());

            foreach (var a in r.Assignments) Assert.AreEqual(-1, a.PersonId);
            Assert.AreEqual(2, r.UnassignedTaskIds.Count);
        }

        [Test]
        public void MultipleCompatibleFamiliesSequentially_OK()
        {
            // One worker does two different families back-to-back: minutes
            // accumulate within the budget (CAN-CE1-03.6: sequential is fine).
            var scheduler = new WorkScheduler();
            var inputs = Inputs(
                new List<SchedulerOpenWorkItem>
                {
                    Item("t1", "TendCustomer", 120),
                    Item("t2", "StockShelves", 120)
                },
                new List<SchedulerAvailableWorker> { Worker(1, 480) });

            WorkAssignmentReport r = scheduler.AssignDailyWork(inputs, new List<string>());

            Assert.AreEqual(1, Find(r.Assignments, "t1").PersonId);
            Assert.AreEqual(1, Find(r.Assignments, "t2").PersonId, "same worker sequentially");
            Assert.AreEqual(0, r.UnassignedTaskIds.Count);
        }

        [Test]
        public void PriorityOrder_OpenWorkProcessedAsListed()
        {
            // t1 is listed first and must be considered before t2.
            var scheduler = new WorkScheduler();
            var inputs = Inputs(
                new List<SchedulerOpenWorkItem> { Item("t1", "TendCustomer", 50), Item("t2", "Clean", 50) },
                new List<SchedulerAvailableWorker> { Worker(1, 60), Worker(2, 60) });

            WorkAssignmentReport r = scheduler.AssignDailyWork(inputs, new List<string>());

            Assert.AreEqual(1, Find(r.Assignments, "t1").PersonId, "t1 takes P1 first (priority + first-fit)");
            Assert.AreEqual(2, Find(r.Assignments, "t2").PersonId, "t2 then goes to P2 (P1 over budget)");
        }
    }
}
