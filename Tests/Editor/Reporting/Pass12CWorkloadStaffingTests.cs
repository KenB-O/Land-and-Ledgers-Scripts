using System.Collections.Generic;
using LandLedgers.Reporting;
using NUnit.Framework;

namespace LandLedgers.Editor.Reporting
{
    /// <summary>
    /// Pass XII Worker C (LL-PLAY-012): the workload-derived staffing
    /// read model. Staffing demand comes from remaining minutes of
    /// real open tasks — never from template slot counts — and the
    /// model's explanatory rows (allocator labor-demand reasons) are
    /// never added into demand, so the same unassigned work is not
    /// counted twice.
    /// </summary>
    [TestFixture]
    public sealed class Pass12CWorkloadStaffingTests
    {
        [Test]
        public void Demand_FromTasks_AssignedVsUnassigned_ExcludesCompleted()
        {
            var model = WorkloadStaffingReadModel.Build("biz", 10,
                new List<WorkloadTaskInput>
                {
                    new WorkloadTaskInput { TaskId = "a", Status = "Active", PlannedMinutes = 240, ProgressPercent = 50, AssigneePersonId = 1 },
                    new WorkloadTaskInput { TaskId = "b", Status = "Open", PlannedMinutes = 180, ProgressPercent = 0, AssigneePersonId = -1 },
                    new WorkloadTaskInput { TaskId = "c", Status = "Completed", PlannedMinutes = 60, ProgressPercent = 100, AssigneePersonId = 2 },
                },
                new List<WorkloadWorkerInput>(),
                new List<WorkloadLaborDemandInput>(),
                new Dictionary<string, int>(),
                new List<WorkloadWageInput>());

            Assert.AreEqual(300, model.TotalDemandMinutes, "120 remaining on A + 180 on B; completed C excluded.");
            Assert.AreEqual(0.5f, model.DemandFullTimeEquivalents, 0.001f, "300 minutes at the 600-minute workday convention = 0.5 FTE.");
            Assert.AreEqual(120, model.AssignedMinutes);
            Assert.AreEqual(180, model.UnassignedMinutes);
            Assert.AreEqual(0.4f, model.Coverage01, 0.001f);
            Assert.AreEqual(1, model.AssignedTaskCount);
            Assert.AreEqual(1, model.UnassignedTaskCount);
            Assert.AreEqual(1, model.UnfilledShifts, "Unfilled shifts are tasks with no assignee — not a quota.");
            Assert.AreEqual(1, model.ExcludedCompletedTaskCount);
        }

        [Test]
        public void Blockers_AndLaborDemandReasons_AreExplanatory_NotAdditive()
        {
            var model = WorkloadStaffingReadModel.Build("biz", 10,
                new List<WorkloadTaskInput>
                {
                    new WorkloadTaskInput { TaskId = "b", Status = "Waiting", PlannedMinutes = 120, ProgressPercent = 0, AssigneePersonId = -1, Blocker = "waiting for lumber" },
                },
                new List<WorkloadWorkerInput>(),
                new List<WorkloadLaborDemandInput>
                {
                    new WorkloadLaborDemandInput { Reason = "no worker available", UnassignedMinutes = 120 },
                },
                new Dictionary<string, int> { { "saw", 90 } },
                new List<WorkloadWageInput>());

            Assert.AreEqual(120, model.TotalDemandMinutes,
                "The allocator's 120 unassigned minutes explain the same work as task B; they must not be added again.");
            Assert.AreEqual(1, model.BlockedTaskCount);
            Assert.AreEqual("waiting for lumber", model.Blockers[0]);
            Assert.AreEqual(1, model.LaborDemandByReason.Count);
            Assert.AreEqual("no worker available", model.LaborDemandByReason[0].Reason);
            Assert.AreEqual(90, model.EquipmentOccupancyMinutes["saw"]);
            Assert.IsTrue(model.Notes.Count > 0, "The non-additivity is disclosed in notes.");
        }

        [Test]
        public void WorkerUtilization_AndUnknownAvailability_AreHonest()
        {
            var model = WorkloadStaffingReadModel.Build("biz", 10,
                new List<WorkloadTaskInput>(),
                new List<WorkloadWorkerInput>
                {
                    new WorkloadWorkerInput { PersonId = 1, DisplayName = "Ada", MinutesWorkedToday = 240, AvailableMinutes = 480, CurrentTaskId = "a" },
                    new WorkloadWorkerInput { PersonId = 2, DisplayName = "Bo", MinutesWorkedToday = 60, AvailableMinutes = -1, ConflictNote = "schedule unknown" },
                },
                new List<WorkloadLaborDemandInput>(),
                new Dictionary<string, int>(),
                new List<WorkloadWageInput>());

            Assert.AreEqual(0.5f, model.Workers[0].Utilization01, 0.001f);
            Assert.AreEqual(-1f, model.Workers[1].Utilization01, "Unknown availability reports -1, never a guessed ratio.");
            Assert.AreEqual(-1, model.TotalAvailableMinutes, "One unknown availability makes the total unknown.");
            Assert.AreEqual(1, model.WorkersWithUnknownAvailability);
            Assert.AreEqual(300, model.TotalWorkedMinutes);
        }

        [Test]
        public void WageObligations_SplitCurrentVsArrears()
        {
            var model = WorkloadStaffingReadModel.Build("biz", 10,
                new List<WorkloadTaskInput>(),
                new List<WorkloadWorkerInput>(),
                new List<WorkloadLaborDemandInput>(),
                new Dictionary<string, int>(),
                new List<WorkloadWageInput>
                {
                    new WorkloadWageInput { EmploymentId = "e1", PersonId = 1, WeeklyWageCents = 1000, IsSuspended = false },
                    new WorkloadWageInput { EmploymentId = "e2", PersonId = 2, WeeklyWageCents = 1000, IsSuspended = true },
                });

            Assert.AreEqual(2000, model.WageObligationCents);
            Assert.AreEqual(1000, model.WageCurrentCents);
            Assert.AreEqual(1000, model.WageArrearsCents, "Suspended wages are arrears, not current obligation and not paid cash.");
            StringAssert.Contains("2000c", model.Explanation);
        }

        [Test]
        public void EmptyWorkload_ExplainsNoDemand()
        {
            var model = WorkloadStaffingReadModel.Build("biz", 10,
                new List<WorkloadTaskInput>(),
                new List<WorkloadWorkerInput>(),
                new List<WorkloadLaborDemandInput>(),
                new Dictionary<string, int>(),
                new List<WorkloadWageInput>());

            Assert.AreEqual(0, model.TotalDemandMinutes);
            Assert.AreEqual(0, model.UnfilledShifts);
            StringAssert.Contains("No open work", model.Explanation);
        }

        [Test]
        public void SkillRequirement_WithoutEvidence_IsFlaggedNotAssumed()
        {
            var model = WorkloadStaffingReadModel.Build("biz", 10,
                new List<WorkloadTaskInput>
                {
                    new WorkloadTaskInput { TaskId = "forge", Status = "Open", PlannedMinutes = 120, ProgressPercent = 0, AssigneePersonId = -1, RequiredSkill = "blacksmithing" },
                },
                new List<WorkloadWorkerInput>(),
                new List<WorkloadLaborDemandInput>(),
                new Dictionary<string, int>(),
                new List<WorkloadWageInput>());

            Assert.AreEqual(1, model.TasksWithSkillRequirementButNoEvidence);
            Assert.IsTrue(model.Notes.Count > 0);
        }
    }
}
