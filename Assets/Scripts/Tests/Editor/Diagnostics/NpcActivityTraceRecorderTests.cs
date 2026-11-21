using System;
using System.IO;
using System.Linq;
using LandLedgers.Diagnostics;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.World.Journeys;
using NUnit.Framework;
using Vector3 = UnityEngine.Vector3;

namespace LandLedgers.EditorTests.Diagnostics
{
    public sealed class NpcActivityTraceRecorderTests
    {
        private static EntityId Person(int id) => EntityId.For(EntityKind.Person, id);
        private static EntityId Business(int id) => EntityId.For(EntityKind.Business, id);

        private static TaskAuthority CreateAuthority()
        {
            var authority = new TaskAuthority();
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition("trace.test", "Trace test", 10), out string reason), reason);
            return authority;
        }

        [Test]
        public void ObserverIsAdditiveAndCapturesTaskLifecycleAndRefusal()
        {
            var authority = CreateAuthority();
            double now = 100;
            var primary = new NpcActivityTraceRecorder(() => (float)now, id => id == 7 ? 44 : -1);
            var additional = new NpcActivityTraceRecorder(() => (float)now, id => id == 7 ? 44 : -1);
            primary.BeginMonitoring();
            additional.BeginMonitoring();
            primary.SetSelection(new[] { 7 }, Array.Empty<int>());
            additional.SetSelection(new[] { 7 }, Array.Empty<int>());
            primary.StartRecording(NpcTraceLevel.Detailed);
            additional.StartRecording(NpcTraceLevel.Detailed);
            authority.SetTimingObserver(primary);
            Assert.IsTrue(authority.AddTimingObserver(additional));

            WorkTask task = authority.CreateTask("trace.test", Business(2), 3);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(7), null, 3, out string reason), reason);
            // The task begins as business-owned. Once assigned to a selected
            // person, the recorder promotes its bounded prehistory so the
            // person timeline includes the original creation event.
            Assert.AreEqual("task-transition", primary.Events[1].eventType);
            Assert.AreEqual(task.TaskId.ToString(), primary.Events[1].taskId);
            Assert.AreEqual(44, primary.Events[1].householdId);
            Assert.IsFalse(authority.AssignTask(task.TaskId, Person(7), null, 3, out reason));
            Assert.IsTrue(authority.StartTask(task.TaskId, 3, out reason), reason);
            Assert.IsTrue(authority.RecordWorkProgress(task.TaskId, 2, null, 3, out _, out reason), reason);

            Assert.AreEqual(primary.Events.Count, additional.Events.Count, "Adding a trace observer must not displace the existing observer.");
            Assert.IsTrue(primary.Events.Any(item => item.eventType == "task-refusal" && item.reason.Contains("not Queued")));
            Assert.IsTrue(primary.Events.Any(item => item.transition == "RecordWorkProgress" && item.workedMinutes == 2));
            Assert.IsTrue(primary.Events.Zip(primary.Events.Skip(1), (left, right) => left.sequence < right.sequence).All(value => value));
        }

        [Test]
        public void HouseholdSelectionUsesLiveResolverAndDeepStallAutoselectsWorker()
        {
            var authority = CreateAuthority();
            var trace = new NpcActivityTraceRecorder(() => 0, id => id == 19 ? 88 : -1)
            {
                AutoStallDetection = true,
                AutoSelectAnomalies = true,
                SampleIntervalSeconds = 60
            };
            trace.BeginMonitoring();
            trace.SetSelection(Array.Empty<int>(), new[] { 88 });
            trace.StartRecording(NpcTraceLevel.Basic);
            authority.AddTimingObserver(trace);
            WorkTask task = authority.CreateTask("trace.test", Business(2), 0);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(19), null, 0, out string reason), reason);
            trace.SampleDeep(authority, 0, 0);
            trace.SampleDeep(authority, 13 * 3600d, 0);

            Assert.IsTrue(trace.Anomalies.Any(item => item.category == "task-no-progress" && item.personId == "P19" && item.householdId == 88));
            Assert.Contains(19, trace.SelectedPeople.ToArray());
            Assert.AreEqual(NpcTraceLevel.Detailed, trace.Level);
            Assert.IsTrue(trace.Events.Any(item => item.personId == "P19" && item.householdId == 88));
        }

        [Test]
        public void DeepStallIncludesOwningSystemRecoveryAndCapabilityBlocker()
        {
            var authority = CreateAuthority();
            var trace = new NpcActivityTraceRecorder(() => 0, id => id == 19 ? 88 : -1)
            {
                AutoStallDetection = true,
                AutoSelectAnomalies = true,
                SampleIntervalSeconds = 60
            };
            trace.BeginMonitoring();
            trace.SetSelection(new[] { 19 }, Array.Empty<int>());
            trace.StartRecording(NpcTraceLevel.Deep, includePrehistory: false);
            authority.AddTimingObserver(trace);
            WorkTask task = authority.CreateTask("trace.test", Business(2), 0);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(19), null, 0, out string reason), reason);
            string recovery = "owner=DomesticLaborDriver; state=WAITING_FOR_CAPABILITY_CHANGE; retryAfterDay=2147483647; activityLocation=firewood: no firewood storage is authored on the home.; workerReservation=none";
            Func<string, string> readRecovery = id => id == task.TaskId.ToString() ? recovery : string.Empty;

            trace.SampleDeep(authority, 0, 0, null, readRecovery);
            trace.SampleDeep(authority, 13 * 3600d, 0, null, readRecovery);

            NpcActivityTraceAnomaly anomaly = trace.Anomalies.Single(item => item.taskId == task.TaskId.ToString());
            Assert.AreEqual("task-no-progress-with-recovery", anomaly.category);
            StringAssert.Contains("DomesticLaborDriver", anomaly.reason);
            StringAssert.Contains("WAITING_FOR_CAPABILITY_CHANGE", anomaly.recoveryState);
            StringAssert.Contains("firewood", anomaly.recoveryState);
            StringAssert.Contains("workerReservation=none", anomaly.recoveryState);
            NpcActivityTraceEvent eventRecord = trace.Events.Last(item => item.eventType == "anomaly");
            Assert.AreEqual(anomaly.recoveryState, eventRecord.recoveryState);
            StringAssert.Contains("WAITING_FOR_CAPABILITY_CHANGE", UnityEngine.JsonUtility.ToJson(eventRecord));
        }

        [Test]
        public void HistoryRetentionAndExportsAreBoundedAndParseable()
        {
            var authority = CreateAuthority();
            var trace = new NpcActivityTraceRecorder(() => 42, id => -1) { HistoryLimit = 2, RetentionLimit = 3 };
            trace.BeginMonitoring();
            authority.AddTimingObserver(trace);
            WorkTask task = authority.CreateTask("trace.test", Business(1), 0);
            trace.StartRecording(NpcTraceLevel.Basic);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), null, 0, out string reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 0, out reason), reason);
            Assert.IsTrue(authority.RecordWorkProgress(task.TaskId, 1, null, 0, out _, out reason), reason);

            Assert.LessOrEqual(trace.Events.Count, 3);
            Assert.Greater(trace.DroppedSessionEvents, 0);
            string path = Path.Combine(Path.GetTempPath(), "npc-activity-trace-" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                trace.WriteJsonLines(path);
                string[] lines = File.ReadAllLines(path);
                Assert.GreaterOrEqual(lines.Length, 2);
                Assert.AreEqual(trace.SessionId, UnityEngine.JsonUtility.FromJson<NpcActivityTraceManifest>(lines[0]).sessionId);
                for (int i = 1; i < lines.Length; i++)
                    Assert.IsNotNull(UnityEngine.JsonUtility.FromJson<NpcActivityTraceEvent>(lines[i]), "Every JSONL event must parse.");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void RestoreMarkerKeepsTraceSessionOutsideSimulationSaveState()
        {
            var trace = new NpcActivityTraceRecorder(() => 12, id => -1);
            trace.BeginMonitoring();
            trace.StartRecording(NpcTraceLevel.Deep);
            trace.RecordAuthorityRestored(4);
            Assert.IsTrue(trace.Events.Any(item => item.eventType == "authority-reattached" && item.dayIndex == 4));
            Assert.AreEqual(0, trace.DroppedSessionEvents);
        }

        [Test]
        public void ShoppingEventsUseDurableTripCorrelationAndHouseholdSelection()
        {
            var trace = new NpcActivityTraceRecorder(() => 7200, id => id == 8 ? 51 : -1);
            trace.BeginMonitoring();
            trace.SetSelection(Array.Empty<int>(), new[] { 51 });
            trace.StartRecording(NpcTraceLevel.Detailed, includePrehistory: false);

            trace.RecordShoppingEvent(new LandLedgers.Population.ShoppingDiagnosticEvent
            {
                TripId = "trip-4-19",
                DayIndex = 4,
                Kind = LandLedgers.Population.ShoppingEventKind.Receipt,
                HouseholdId = 51,
                PersonId = 8,
                NeedSequence = 12,
                Summary = "Household food physically received",
                Details = "receipt=receipt-2; lot=lot-6; units=3"
            });

            NpcActivityTraceEvent item = trace.Events.Last();
            Assert.AreEqual("shopping", item.eventType);
            Assert.AreEqual("Receipt", item.transition);
            Assert.AreEqual("trip-4-19", item.operationId);
            Assert.AreEqual("trip-4-19", item.shoppingTripId);
            Assert.AreEqual("P8", item.personId);
            Assert.AreEqual(51, item.householdId);
            Assert.IsTrue(item.detail.Contains("receipt-2"));
        }

        [Test]
        public void BasicLevelRetainsActivityTransitionWithoutDetailedPayload()
        {
            var trace = new NpcActivityTraceRecorder(() => 3, id => -1);
            trace.BeginMonitoring();
            trace.StartRecording(NpcTraceLevel.Basic, includePrehistory: false);
            trace.RecordShoppingEvent(new LandLedgers.Population.ShoppingDiagnosticEvent
            {
                TripId = "trip-1-1",
                Kind = LandLedgers.Population.ShoppingEventKind.Failure,
                HouseholdId = 1,
                PersonId = 3,
                NeedSequence = 5,
                Summary = "Journey refused",
                Details = "private detailed diagnostic"
            });

            NpcActivityTraceEvent item = trace.Events.Last();
            Assert.AreEqual("shopping", item.eventType);
            Assert.AreEqual("Failure", item.transition);
            Assert.AreEqual("Journey refused", item.target);
            Assert.IsEmpty(item.detail);
        }

        [Test]
        public void DeepMovementStallAutoselectsPersonAndBoundsAnomalyRetention()
        {
            var trace = new NpcActivityTraceRecorder(() => 0, id => id == 8 ? 51 : 52)
            {
                AnomalyLimit = 1,
                AutoStallDetection = true,
                AutoSelectAnomalies = true
            };
            trace.BeginMonitoring();
            trace.StartRecording(NpcTraceLevel.Deep, includePrehistory: false);

            trace.RecordPersonSnapshot(8, 51, 0, 0, "Walking", 7, Vector3.zero, "Walking", 10, 20, "task=T1",
                "P8:route:2", 2, 1, 5, new Vector3(10, 0, 0), "Journey", "Travel", "door-1", true, true, "Moving");
            trace.RecordPersonSnapshot(8, 51, 0, 1800, "Walking", 7, Vector3.zero, "Walking", 10, 20, "task=T1",
                "P8:route:2", 2, 1, 5, new Vector3(10, 0, 0), "Journey", "Travel", "door-1", true, true, "Moving");
            trace.RecordPersonSnapshot(9, 52, 0, 0, "Walking", 8, Vector3.zero, "Walking", 10, 20, "task=T2",
                "P9:route:4", 4, 1, 5, new Vector3(10, 0, 0), "Journey", "Travel", "door-2", true, true, "Moving");
            trace.RecordPersonSnapshot(9, 52, 0, 1800, "Walking", 8, Vector3.zero, "Walking", 10, 20, "task=T2",
                "P9:route:4", 4, 1, 5, new Vector3(10, 0, 0), "Journey", "Travel", "door-2", true, true, "Moving");

            Assert.AreEqual(1, trace.Anomalies.Count);
            Assert.AreEqual("P9:route:4", trace.Anomalies[0].anomalyId.Substring("route-no-progress|".Length));
            Assert.AreEqual(1, trace.DroppedAnomalies);
            Assert.Contains(9, trace.SelectedPeople.ToArray());
            Assert.IsTrue(trace.Events.Any(item => item.eventType == "anomaly" && item.operationId == "P9:route:4"));
        }

        [Test]
        public void SuccessfulTravelTaskUsesObservedPhysicalArrivalInsteadOfCreditedMinutes()
        {
            var authority = new TaskAuthority();
            Assert.IsTrue(authority.RegisterDefinition(
                new TaskDefinition(JourneyTravel.TravelTaskId, "Travel", 10), out string reason), reason);
            double now = 100;
            var trace = new NpcActivityTraceRecorder(() => (float)now, id => id == 7 ? 44 : -1);
            trace.BeginMonitoring();
            trace.SetSelection(new[] { 7 }, Array.Empty<int>());
            trace.StartRecording(NpcTraceLevel.Deep, includePrehistory: false);
            authority.SetTimingObserver(trace);
            authority.SetTimingClock(() => (float)now);

            WorkTask travel = authority.CreateTaskWithPlannedMinutes(JourneyTravel.TravelTaskId, Business(2), 0, 10);
            Assert.IsTrue(authority.AssignTask(travel.TaskId, Person(7), null, 0, out reason), reason);
            Assert.IsTrue(authority.StartTask(travel.TaskId, 0, out reason), reason);
            now = 101;
            trace.RecordMovement(7, 0, 101, "started", "Moving", "building:9", 12f,
                "P7:route:1", destinationBuildingId: 9, routeGeneration: 1, routeMode: "TaskDirected",
                scheduleState: "GoingToTask", moverState: "Moving", routeDestination: new Vector3(10, 0, 0),
                moverVisible: true, routeActive: true, associatedTask: travel);
            trace.RecordMovement(7, 0, 110, "arrived", "ArrivedVisible", "building:9", 0f,
                "P7:route:1", destinationBuildingId: 9, routeGeneration: 1, routeMode: "TaskDirected",
                scheduleState: "AtTask", moverState: "ArrivedVisible", moverVisible: true, routeActive: false,
                associatedTask: travel);
            now = 110;

            Assert.IsTrue(authority.CompleteTask(travel.TaskId, TaskOutcome.Success, 0, null));

            NpcActivityTraceEvent arrival = trace.Events.Last(item => item.eventType == "movement" && item.transition == "arrived");
            Assert.AreEqual(travel.TaskId.ToString(), arrival.taskId);
            Assert.AreEqual(JourneyTravel.TravelTaskId, arrival.taskDefinitionId);
            Assert.AreEqual(TaskStatus.InProgress.ToString(), arrival.taskStatus);
            Assert.IsFalse(trace.Anomalies.Any(item => item.taskId == travel.TaskId.ToString()),
                "A travel task completed after a physical arrival must not be mislabeled as unworked output.");
        }

        [Test]
        public void MovementArrivalRetainsTaskIdentityWhenTaskTerminatesBeforeCallback()
        {
            var authority = CreateAuthority();
            var trace = new NpcActivityTraceRecorder(() => 100, id => id == 7 ? 44 : -1);
            trace.BeginMonitoring();
            trace.SetSelection(new[] { 7 }, Array.Empty<int>());
            trace.StartRecording(NpcTraceLevel.Deep, includePrehistory: false);
            authority.SetTimingObserver(trace);

            WorkTask task = authority.CreateTask("trace.test", Business(2), 0);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(7), null, 0, out string reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 0, out reason), reason);
            trace.RecordMovement(7, 0, 101, "started", "Moving", "building:9", 12f,
                "P7:route:1", destinationBuildingId: 9, routeGeneration: 1, routeMode: "TaskDirected",
                associatedTask: task);
            Assert.IsTrue(authority.CancelTask(task.TaskId, null));

            trace.RecordMovement(7, 0, 110, "arrived", "ArrivedVisible", "building:9", 0f,
                "P7:route:1", destinationBuildingId: 9, routeGeneration: 1, routeMode: "TaskDirected");

            NpcActivityTraceEvent arrival = trace.Events.Last(item => item.eventType == "movement" && item.transition == "arrived");
            Assert.AreEqual(task.TaskId.ToString(), arrival.taskId,
                "The physical arrival must remain attributable after cancellation changed task state.");
            Assert.AreEqual(TaskStatus.Cancelled.ToString(), arrival.taskStatus);
            Assert.AreEqual("building:9", arrival.target);
        }

        [Test]
        public void SuccessfulTravelTaskWithoutObservedArrivalIsReportedAtDetailedTraceLevel()
        {
            var authority = new TaskAuthority();
            Assert.IsTrue(authority.RegisterDefinition(
                new TaskDefinition(JourneyTravel.TravelTaskId, "Travel", 10), out string reason), reason);
            var trace = new NpcActivityTraceRecorder(() => 100, id => id == 7 ? 44 : -1);
            trace.BeginMonitoring();
            trace.SetSelection(new[] { 7 }, Array.Empty<int>());
            trace.StartRecording(NpcTraceLevel.Detailed, includePrehistory: false);
            authority.SetTimingObserver(trace);

            WorkTask travel = authority.CreateTaskWithPlannedMinutes(JourneyTravel.TravelTaskId, Business(2), 0, 10);
            Assert.IsTrue(authority.AssignTask(travel.TaskId, Person(7), null, 0, out reason), reason);
            Assert.IsTrue(authority.StartTask(travel.TaskId, 0, out reason), reason);
            Assert.IsTrue(authority.CompleteTask(travel.TaskId, TaskOutcome.Success, 0, null));

            NpcActivityTraceAnomaly anomaly = trace.Anomalies.Single(item => item.taskId == travel.TaskId.ToString());
            Assert.AreEqual("travel-task-succeeded-without-arrival", anomaly.category);
            Assert.AreEqual("P7", anomaly.personId);
            Assert.AreEqual(44, anomaly.householdId);
        }

        [Test]
        public void AutomaticSelectionPromotesRelevantPrehistory()
        {
            var authority = CreateAuthority();
            var trace = new NpcActivityTraceRecorder(() => 0, id => id == 29 ? 88 : -1)
            {
                AutoStallDetection = true,
                AutoSelectAnomalies = true,
                SampleIntervalSeconds = 60
            };
            trace.BeginMonitoring();
            trace.SetSelection(Array.Empty<int>(), new[] { 77 });
            trace.StartRecording(NpcTraceLevel.Basic, includePrehistory: false);
            authority.AddTimingObserver(trace);
            WorkTask task = authority.CreateTask("trace.test", Business(4), 0);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(29), null, 0, out string reason), reason);
            Assert.IsFalse(trace.Events.Any(item => item.taskId == task.TaskId.ToString()),
                "Unselected worker activity should remain only in bounded prehistory until an anomaly selects the worker.");

            trace.SampleDeep(authority, 0, 0);
            trace.SampleDeep(authority, 13 * 3600d, 0);

            Assert.Contains(29, trace.SelectedPeople.ToArray());
            Assert.IsTrue(trace.Events.Any(item => item.taskId == task.TaskId.ToString() && item.transition == "CreateTask"),
                "Auto-selection must promote the selected worker's relevant creation prehistory.");
            Assert.IsTrue(trace.Events.Any(item => item.taskId == task.TaskId.ToString() && item.transition == "AssignTask"));
        }

        [Test]
        public void DetailedMovementSamplesDetectRouteStallsWithoutPositionPayload()
        {
            var trace = new NpcActivityTraceRecorder(() => 0, id => 51)
            {
                AutoStallDetection = true,
                AutoSelectAnomalies = true
            };
            trace.BeginMonitoring();
            trace.StartRecording(NpcTraceLevel.Detailed, includePrehistory: false);
            trace.RecordPersonSnapshot(8, 51, 0, 0, "Walking", 7, new Vector3(2, 0, 3), "Walking", 10, 20, "task=T1",
                "P8:route:2", 2, 1, 5, new Vector3(10, 0, 0), "Journey", "Travel", "door-1", true, true, "Moving");
            trace.RecordPersonSnapshot(8, 51, 0, 1800, "Walking", 7, new Vector3(2, 0, 3), "Walking", 10, 20, "task=T1",
                "P8:route:2", 2, 1, 5, new Vector3(10, 0, 0), "Journey", "Travel", "door-1", true, true, "Moving");

            NpcActivityTraceEvent sample = trace.Events.First(item => item.eventType == "movement-snapshot");
            Assert.IsFalse(sample.hasWorldPosition, "Detailed tracing must not include Deep physical position data.");
            Assert.AreEqual("P8:route:2", sample.routeIdentity);
            Assert.IsTrue(trace.Anomalies.Any(item => item.category == "route-no-progress" && item.personId == "P8"));
        }

        [Test]
        public void MovementEventsRetainRouteRequestRefusalAndArrivalContext()
        {
            var trace = new NpcActivityTraceRecorder(() => 90, id => id == 8 ? 51 : -1);
            trace.BeginMonitoring();
            trace.SetSelection(new[] { 8 }, null, "movement lifecycle test");
            trace.StartRecording(NpcTraceLevel.Deep, includePrehistory: false);

            trace.RecordMovement(8, 2, 90, "refused", "PathFailed", "building:0", 0,
                reason: "No walkable destination", destinationBuildingId: 0,
                scheduleState: "GoingShopping", moverState: "ArrivedVisible",
                currentDestinationBuildingId: 9,
                physicalGridX: 12, physicalGridZ: 34, physicalCellBuildingId: 9);
            trace.RecordMovement(8, 2, 95, "started", "Moving", "building:0", 24,
                routeId: "P8:route:4", destinationBuildingId: 0, routeGeneration: 4,
                routeMode: "Scheduled", scheduleState: "GoingShopping", moverState: "Moving",
                routeDestination: new Vector3(12, 1.5f, 9), activityLocationId: "counter:service-0",
                worldPosition: new Vector3(4.25f, 0.8f, -2.5f), moverVisible: true, routeActive: true,
                shoppingTripId: "trip-2-77");

            NpcActivityTraceEvent refused = trace.Events[0];
            Assert.AreEqual(2, refused.schemaVersion);
            Assert.AreEqual("P8", refused.personId);
            Assert.AreEqual(51, refused.householdId);
            Assert.AreEqual(0, refused.destinationBuildingId);
            Assert.AreEqual(0, refused.requestedTargetBuildingId);
            Assert.AreEqual(9, refused.currentDestinationBuildingId,
                "A failed seller route must retain the actor's prior logical building separately from the requested seller.");
            Assert.AreEqual(12, refused.physicalGridX);
            Assert.AreEqual(34, refused.physicalGridZ);
            Assert.AreEqual(9, refused.physicalCellBuildingId);
            Assert.AreEqual("PathFailed", refused.movementStatus);
            Assert.AreEqual("No walkable destination", refused.reason);
            Assert.IsFalse(refused.hasRouteDestination, "A refused route must not claim a resolved path destination.");

            NpcActivityTraceEvent started = trace.Events[1];
            Assert.AreEqual("P8:route:4", started.routeIdentity);
            Assert.AreEqual(4, started.routeGeneration);
            Assert.AreEqual("Scheduled", started.routeMode);
            Assert.IsTrue(started.hasRouteDestination);
            Assert.AreEqual(1.5f, started.destinationY);
            Assert.AreEqual("counter:service-0", started.activityLocationId);
            Assert.IsTrue(started.hasWorldPosition);
            Assert.AreEqual(4.25f, started.positionX);
            Assert.AreEqual(0.8f, started.positionY);
            Assert.AreEqual(-2.5f, started.positionZ);
            Assert.IsTrue(started.moverVisible);
            Assert.IsTrue(started.routeActive);
            Assert.AreEqual("trip-2-77", started.shoppingTripId);
            Assert.AreEqual("trip-2-77", started.operationId);
        }

        [Test]
        public void MovementEventsRetainObservedWorldPositionAndVisibilityWhenRouteIsRefused()
        {
            var trace = new NpcActivityTraceRecorder(() => 90, id => id == 8 ? 51 : -1);
            trace.BeginMonitoring();
            trace.SetSelection(new[] { 8 }, null, "movement refusal position test");
            trace.StartRecording(NpcTraceLevel.Deep, includePrehistory: false);

            trace.RecordMovement(8, 2, 90, "refused", "PathFailed", "building:0", 0,
                reason: "No walkable destination", destinationBuildingId: 0,
                scheduleState: "GoingShopping", moverState: "ArrivedVisible",
                worldPosition: new Vector3(38.5f, 0.7f, 54f), moverVisible: true, routeActive: false);

            NpcActivityTraceEvent refused = trace.Events[0];
            Assert.IsTrue(refused.hasWorldPosition);
            Assert.AreEqual(38.5f, refused.positionX);
            Assert.AreEqual(0.7f, refused.positionY);
            Assert.AreEqual(54f, refused.positionZ);
            Assert.IsTrue(refused.moverVisible);
            Assert.IsFalse(refused.routeActive);
        }

        [Test]
        public void SamplingCadenceDependsOnSimulatedTimeNotCallbackCount()
        {
            var authority = CreateAuthority();
            var trace = new NpcActivityTraceRecorder(() => 0, id => -1) { SampleIntervalSeconds = 300 };
            trace.BeginMonitoring();
            trace.StartRecording(NpcTraceLevel.Deep, includePrehistory: false);
            authority.AddTimingObserver(trace);
            authority.CreateTask("trace.test", Business(4), 0);
            Assert.IsTrue(trace.SampleDeep(authority, 0, 0));
            int afterInitialSample = trace.Events.Count;
            for (int i = 0; i < 40; i++) Assert.IsFalse(trace.SampleDeep(authority, 10, 0));
            Assert.AreEqual(afterInitialSample, trace.Events.Count);
            Assert.IsTrue(trace.SampleDeep(authority, 300, 0));
            Assert.Greater(trace.Events.Count, afterInitialSample);
        }

        [Test]
        public void TraceObserversDoNotChangeTaskAuthorityResults()
        {
            TaskAuthority plain = CreateAuthority();
            TaskAuthority observed = CreateAuthority();
            var trace = new NpcActivityTraceRecorder(() => 123, id => -1);
            trace.BeginMonitoring();
            trace.StartRecording(NpcTraceLevel.Deep, includePrehistory: false);
            observed.AddTimingObserver(trace);

            WorkTask plainTask = plain.CreateTask("trace.test", Business(4), 2);
            WorkTask observedTask = observed.CreateTask("trace.test", Business(4), 2);
            Assert.IsTrue(plain.AssignTask(plainTask.TaskId, Person(29), null, 2, out string plainReason), plainReason);
            Assert.IsTrue(observed.AssignTask(observedTask.TaskId, Person(29), null, 2, out string observedReason), observedReason);
            Assert.IsTrue(plain.StartTask(plainTask.TaskId, 2, out plainReason), plainReason);
            Assert.IsTrue(observed.StartTask(observedTask.TaskId, 2, out observedReason), observedReason);
            Assert.IsTrue(plain.RecordWorkProgress(plainTask.TaskId, 2, null, 2, out _, out plainReason), plainReason);
            Assert.IsTrue(observed.RecordWorkProgress(observedTask.TaskId, 2, null, 2, out _, out observedReason), observedReason);

            Assert.AreEqual(plainTask.Status, observedTask.Status);
            Assert.AreEqual(plainTask.MinutesWorked, observedTask.MinutesWorked);
            Assert.AreEqual(plainTask.MinutesRemaining, observedTask.MinutesRemaining);
            Assert.AreEqual(plainTask.Outcome, observedTask.Outcome);
        }
    }
}
