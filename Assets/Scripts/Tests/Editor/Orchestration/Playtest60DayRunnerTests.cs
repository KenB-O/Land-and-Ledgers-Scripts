using System.Collections.Generic;
using System.IO;
using LandLedgers.Editor.Playtests;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Orchestration
{
    public sealed class Playtest60DayRunnerTests
    {
        [TestCase(5)]
        [TestCase(10)]
        [TestCase(15)]
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(90)]
        public void DurationPresets_ArePositiveAndResolveDirectly(int days)
        {
            Assert.IsTrue(Playtest60DayRunCoordinator.IsValidDuration(days));
            Assert.AreEqual(days, Playtest60DayRunCoordinator.ResolveDurationSelection(days, 123));
        }

        [TestCase(1)]
        [TestCase(123)]
        [TestCase(365)]
        public void CustomDuration_AcceptsPositiveDayCounts(int days)
        {
            Assert.IsTrue(Playtest60DayRunCoordinator.IsValidDuration(
                Playtest60DayRunCoordinator.ResolveDurationSelection(
                    Playtest60DayRunCoordinator.CustomDurationChoice, days)));
        }

        [Test]
        public void CustomDuration_RejectsZeroAndNegativeValues()
        {
            Assert.IsFalse(Playtest60DayRunCoordinator.IsValidDuration(0));
            Assert.IsFalse(Playtest60DayRunCoordinator.IsValidDuration(-1));
        }

        [Test]
        public void DurationMenu_ContainsRequestedPresetsAndCustomChoice()
        {
            CollectionAssert.AreEqual(new[] { 5, 10, 15, 30, 60, 90, Playtest60DayRunCoordinator.CustomDurationChoice },
                Playtest60DayRunCoordinator.DurationChoices);
        }

        [Test]
        public void WorkingTreeStatus_CountsAllPorcelainLines()
        {
            const string status = " M Assets/Scripts/A.cs\r\n?? owner-local.asset\r\nA  Assets/Scripts/B.cs\r\n";

            Assert.AreEqual(3, Playtest60DayRunCoordinator.CountNonEmptyLines(status));
            Assert.AreEqual("DIRTY", Playtest60DayRunCoordinator.ResolveWorkingTreeState(3));
            Assert.AreEqual("dirty (3 changed tracked files)",
                Playtest60DayRunCoordinator.FormatWorkingTreeSummary("DIRTY", 3));
        }

        [Test]
        public void WorkingTreeStatus_EmptyGitResultMeansClean()
        {
            Assert.AreEqual(0, Playtest60DayRunCoordinator.CountNonEmptyLines(string.Empty));
            Assert.AreEqual("CLEAN", Playtest60DayRunCoordinator.ResolveWorkingTreeState(0));
            Assert.AreEqual("clean", Playtest60DayRunCoordinator.FormatWorkingTreeSummary("CLEAN", 0));
        }

        [Test]
        public void WorkingTreeStatus_UnavailableGitResultIsNeverReportedAsClean()
        {
            int changeCount = Playtest60DayRunCoordinator.CountNonEmptyLines("NOT_CAPTURED");

            Assert.AreEqual(-1, changeCount);
            Assert.AreEqual("NOT_CAPTURED", Playtest60DayRunCoordinator.ResolveWorkingTreeState(changeCount));
            StringAssert.Contains("NOT_CAPTURED",
                Playtest60DayRunCoordinator.FormatWorkingTreeSummary("NOT_CAPTURED", changeCount));
            StringAssert.DoesNotContain("clean",
                Playtest60DayRunCoordinator.FormatWorkingTreeSummary("NOT_CAPTURED", changeCount));
        }

        [Test]
        public void ConsoleTotals_DistinguishInformationalLogsFromAllCapturedEvents()
        {
            Assert.AreEqual("334 total events (285 informational logs, 47 warnings, 2 errors, 0 exceptions)",
                Playtest60DayRunCoordinator.FormatConsoleTotals(334, 285, 47, 2, 0));
        }

        [Test]
        public void GitStatus_ReadsPorcelainOutputWithoutTimingOutOnDirtyWorktree()
        {
            string projectRoot = System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            string status = Playtest60DayRunCoordinator.ReadGit(
                "status --porcelain --untracked-files=no", projectRoot);

            Assert.AreNotEqual("NOT_CAPTURED", status);
            int count = Playtest60DayRunCoordinator.CountNonEmptyLines(status);
            Assert.GreaterOrEqual(count, 0);
            Assert.AreEqual(Playtest60DayRunCoordinator.ResolveWorkingTreeState(count),
                count > 0 ? "DIRTY" : "CLEAN");
        }

        [Test]
        public void StartupGate_ReadyRuntimeWinsOverElapsedEditorTimeout()
        {
            Assert.AreEqual(Playtest60DayRunCoordinator.StartupGateDecision.Ready,
                Playtest60DayRunCoordinator.ResolveStartupGateDecision(
                    gatePassed: true, bootstrapInitialized: true, bootstrapFailed: false, timeoutElapsed: true));
        }

        [Test]
        public void StartupGate_InitializedButIncompleteRuntimeFailsWithItsValidationResult()
        {
            Assert.AreEqual(Playtest60DayRunCoordinator.StartupGateDecision.Invalid,
                Playtest60DayRunCoordinator.ResolveStartupGateDecision(
                    gatePassed: false, bootstrapInitialized: true, bootstrapFailed: false, timeoutElapsed: true));
        }

        [Test]
        public void StartupGate_FailedBootstrapFailsImmediately()
        {
            Assert.AreEqual(Playtest60DayRunCoordinator.StartupGateDecision.Invalid,
                Playtest60DayRunCoordinator.ResolveStartupGateDecision(
                    gatePassed: false, bootstrapInitialized: false, bootstrapFailed: true, timeoutElapsed: false));
        }

        [Test]
        public void StartupGate_OnlyTimesOutWhileStartupIsStillPending()
        {
            Assert.AreEqual(Playtest60DayRunCoordinator.StartupGateDecision.Waiting,
                Playtest60DayRunCoordinator.ResolveStartupGateDecision(
                    gatePassed: false, bootstrapInitialized: false, bootstrapFailed: false, timeoutElapsed: false));
            Assert.AreEqual(Playtest60DayRunCoordinator.StartupGateDecision.TimedOut,
                Playtest60DayRunCoordinator.ResolveStartupGateDecision(
                    gatePassed: false, bootstrapInitialized: false, bootstrapFailed: false, timeoutElapsed: true));
        }

        [Test]
        public void ActivityTaskTrace_CapturesOpenTaskProgressAndUnreleasedPersonWindows()
        {
            var tasks = new TaskAuthority();
            Assert.IsTrue(tasks.RegisterDefinition(new TaskDefinition("trace.work", "Trace work", 20), out string reason), reason);
            WorkTask task = tasks.CreateTask("trace.work", EntityId.For(EntityKind.Household, 4), 7);
            var timing = new TaskDurationDiagnostics();
            timing.OnTransition(task, "CreateTask", 420f, 7);
            var schedules = new PersonScheduleTracker();
            Assert.IsNull(schedules.TryReserve(9, PersonActivityKind.Work, 7, 540, 25, "task:" + task.TaskId));
            Assert.IsNull(schedules.TryReserve(9, PersonActivityKind.Sleeping, 8, 1320, 120, "sleep"));

            var trace = Playtest60DayRunCoordinator.BuildActivityTaskTraceSnapshots(
                "trace-run", 7, 480f, tasks, timing, schedules);

            Assert.AreEqual("snapshot-summary", trace[0].recordType);
            Assert.AreEqual(1, trace[0].authoritativeOpenTaskCount);
            Assert.AreEqual(1, trace[0].openTasksWithTimingRecord);
            Assert.AreEqual(0, trace[0].openTasksWithoutTimingRecord);
            Assert.AreEqual(1, trace[0].currentDayReservationCount,
                "future-day reservations are not attributed to today's schedule");
            Playtest60DayRunCoordinator.ActivityTaskTraceSnapshot taskTrace =
                trace.Find(item => item.recordType == "task");
            Assert.IsNotNull(taskTrace);
            Assert.AreEqual(task.TaskId.ToString(), taskTrace.taskId);
            Assert.AreEqual("Queued", taskTrace.taskStatus);
            Assert.AreEqual(20, taskTrace.plannedMinutes);
            Assert.AreEqual(0, taskTrace.workedMinutes);
            Assert.AreEqual(20, taskTrace.remainingMinutes);
            Assert.AreEqual("CAPTURED_FROM_TaskDurationDiagnostics", taskTrace.timingRecordStatus);
            StringAssert.Contains("NOT_APPLICABLE", taskTrace.physicalRouteState,
                "an unassigned task has no person route to observe");
            Playtest60DayRunCoordinator.ActivityTaskTraceSnapshot reservationTrace =
                trace.Find(item => item.recordType == "activity-reservation");
            Assert.IsNotNull(reservationTrace);
            Assert.AreEqual(9, reservationTrace.personId);
            Assert.AreEqual("Work", reservationTrace.activity);
            Assert.AreEqual("task:" + task.TaskId, reservationTrace.reservationLabel);
            Assert.IsFalse(reservationTrace.released);
        }

        [Test]
        public void ActivityTaskTrace_CapturesPhysicalRouteForAssignedPersonAndSchedule()
        {
            var tasks = new TaskAuthority();
            Assert.IsTrue(tasks.RegisterDefinition(new TaskDefinition("trace.walk", "Walk", 20), out string reason), reason);
            WorkTask task = tasks.CreateTask("trace.walk", EntityId.For(EntityKind.Household, 4), 7);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(7);
            EntityId person = EntityId.For(EntityKind.Person, 9);
            Assert.IsTrue(tasks.AssignTask(task.TaskId, person, budgets, out reason), reason);

            var schedules = new PersonScheduleTracker();
            Assert.IsNull(schedules.TryReserve(9, PersonActivityKind.Work, 7, 540, 25, "task:" + task.TaskId));
            Playtest60DayRunCoordinator.PersonMovementReadoutProvider movement =
                (int personId, out string status, out float remainingDistance, out float eta) =>
                {
                    Assert.AreEqual(9, personId);
                    status = "Walking; 12.5 m remaining";
                    remainingDistance = 12.5f;
                    eta = 10.4f;
                    return true;
                };
            Playtest60DayRunCoordinator.PersonOperationalReadoutProvider operational =
                (int personId, out int homeBuildingId, out int workplaceBuildingId,
                    out int currentDestinationBuildingId, out string scheduleState,
                    out string routeMode, out bool moverVisible, out bool routeActive) =>
                {
                    Assert.AreEqual(9, personId);
                    homeBuildingId = 6;
                    workplaceBuildingId = 0;
                    currentDestinationBuildingId = 0;
                    scheduleState = "AtWork";
                    routeMode = "None";
                    moverVisible = true;
                    routeActive = false;
                    return true;
                };

            List<Playtest60DayRunCoordinator.ActivityTaskTraceSnapshot> trace =
                Playtest60DayRunCoordinator.BuildActivityTaskTraceSnapshots(
                    "trace-route-run", 7, 480f, tasks, null, schedules, movement, operational);

            Playtest60DayRunCoordinator.ActivityTaskTraceSnapshot summary = trace[0];
            Assert.AreEqual(2, summary.physicalRouteReadoutsAttempted,
                "the assigned task and same-day schedule each retain their own person-linked route observation");
            Assert.AreEqual(2, summary.physicalRouteReadoutsAvailable);
            Assert.AreEqual(0, summary.physicalRouteReadoutsUnavailable);
            Playtest60DayRunCoordinator.ActivityTaskTraceSnapshot taskTrace =
                trace.Find(item => item.recordType == "task");
            Assert.AreEqual(9, taskTrace.physicalRoutePersonId);
            Assert.AreEqual("Walking; 12.5 m remaining", taskTrace.physicalRouteState);
            Assert.AreEqual(12.5f, taskTrace.physicalRouteRemainingDistanceMeters, 0.001f);
            Assert.AreEqual(10.4f, taskTrace.physicalRouteEstimatedGameSecondsToArrival, 0.001f);
            Assert.AreEqual(6, taskTrace.personHomeBuildingId);
            Assert.AreEqual(0, taskTrace.personWorkplaceBuildingId);
            Assert.AreEqual(0, taskTrace.personCurrentDestinationBuildingId);
            Assert.AreEqual("AtWork", taskTrace.personScheduleState);
            Assert.AreEqual("None", taskTrace.personRouteMode);
            Assert.IsTrue(taskTrace.personMoverVisible);
            Assert.IsFalse(taskTrace.personRouteActive);
            Assert.AreEqual("CAPTURED_FROM_PopulationPathingDirector", taskTrace.personOperationalReadoutStatus);
            Playtest60DayRunCoordinator.ActivityTaskTraceSnapshot reservationTrace =
                trace.Find(item => item.recordType == "activity-reservation");
            Assert.AreEqual(9, reservationTrace.physicalRoutePersonId);
            Assert.AreEqual(12.5f, reservationTrace.physicalRouteRemainingDistanceMeters, 0.001f);
            Assert.AreEqual(0, reservationTrace.personCurrentDestinationBuildingId,
                "scheduled activities retain the person's actual destination alongside the route readout");
        }

        [Test]
        public void ActivityTaskTrace_ExplicitlyReportsAssignedPersonWithoutManagedMover()
        {
            var tasks = new TaskAuthority();
            Assert.IsTrue(tasks.RegisterDefinition(new TaskDefinition("trace.no-mover", "No mover", 15), out string reason), reason);
            WorkTask task = tasks.CreateTask("trace.no-mover", EntityId.For(EntityKind.Household, 2), 3);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(3);
            Assert.IsTrue(tasks.AssignTask(task.TaskId, EntityId.For(EntityKind.Person, 12), budgets, out reason), reason);

            Playtest60DayRunCoordinator.PersonMovementReadoutProvider movement =
                (int personId, out string status, out float remainingDistance, out float eta) =>
                {
                    status = string.Empty;
                    remainingDistance = 0f;
                    eta = 0f;
                    return false;
                };
            List<Playtest60DayRunCoordinator.ActivityTaskTraceSnapshot> trace =
                Playtest60DayRunCoordinator.BuildActivityTaskTraceSnapshots(
                    "trace-missing-mover-run", 3, 180f, tasks, null, null, movement);

            Assert.AreEqual(1, trace[0].physicalRouteReadoutsAttempted);
            Assert.AreEqual(0, trace[0].physicalRouteReadoutsAvailable);
            Assert.AreEqual(1, trace[0].physicalRouteReadoutsUnavailable);
            Assert.AreEqual(task.TaskId.ToString(), trace[1].taskId,
                "missing movement telemetry must not remove the authoritative task from the trace");
            Assert.AreEqual("UNAVAILABLE: no managed mover readout for person P12", trace[1].physicalRouteState);
            Assert.AreEqual(-1f, trace[1].physicalRouteRemainingDistanceMeters);
        }

        [Test]
        public void ActivityTaskTrace_ReportsMissingTimingSidecarWithoutHidingAuthoritativeTask()
        {
            var tasks = new TaskAuthority();
            Assert.IsTrue(tasks.RegisterDefinition(new TaskDefinition("trace.unobserved", "Unobserved", 15), out string reason), reason);
            WorkTask task = tasks.CreateTask("trace.unobserved", EntityId.For(EntityKind.Household, 2), 3);

            var trace = Playtest60DayRunCoordinator.BuildActivityTaskTraceSnapshots(
                "trace-run", 3, 180f, tasks, null, null);

            Assert.AreEqual(2, trace.Count, "one coverage row and one authoritative open task are retained");
            Assert.AreEqual(1, trace[0].authoritativeOpenTaskCount);
            Assert.AreEqual(0, trace[0].openTasksWithTimingRecord);
            Assert.AreEqual(1, trace[0].openTasksWithoutTimingRecord);
            StringAssert.Contains("some timing sidecars were not observed", trace[0].traceCoverage);
            Assert.AreEqual(task.TaskId.ToString(), trace[1].taskId);
            Assert.AreEqual("NOT_OBSERVED", trace[1].timingRecordStatus);
        }

        [TestCase(14, 14, true)]
        [TestCase(98, 14, false)]
        [TestCase(13, 14, false)]
        [TestCase(0, -1, false)]
        public void StartupGate_ConfiguredPopulationCountsRequireExactMatch(int actual, int expected, bool matches)
        {
            Assert.AreEqual(matches, Playtest60DayRunCoordinator.HasExpectedStartupCount(actual, expected));
        }
    }
}
