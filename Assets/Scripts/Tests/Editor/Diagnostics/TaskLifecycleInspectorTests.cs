// Lane 4 (Muse Pass, Nov 20 2026) — harness tests for the Task and
// Obligation Lifecycle Inspector core (TaskLifecycleInspector).
// Pure-System tests; the Editor window itself cannot join the harness
// closure and is verified by ParseCheck instead.
using System.Collections.Generic;
using LandLedgers.Diagnostics;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Diagnostics
{
    public sealed class TaskLifecycleInspectorTests
    {
        private sealed class FakeIncidentCore : IIncidentCore
        {
            public readonly List<IncidentFinding> Received = new List<IncidentFinding>();
            public void EmitClassifiedIncident(IncidentFinding finding) { Received.Add(finding); }
        }

        private const double Hour = 3600d;

        private static ObligationLifecycleSnapshot QueuedTask(double stalledHours, bool hasAssignee)
        {
            return new ObligationLifecycleSnapshot
            {
                ObligationId = "T1",
                Kind = ObligationKind.Task,
                DefinitionId = "domestic.fuel-handling",
                Label = "Fetch and handle household fuel",
                Status = "Queued",
                StateEnteredSimSeconds = 0,
                LastProgressSimSeconds = 0,
                FirstObservedSimSeconds = 0,
                NowSimSeconds = stalledHours * Hour,
                DayIndex = 3,
                OwnerId = "H7",
                HouseholdId = -1,
                WorkedMinutes = 0,
                RemainingMinutes = 41,
                PlannedMinutes = 41,
                HasAssignee = hasAssignee,
                HasLivenessBound = false,
            };
        }

        [Test]
        public void TerminalStatesRefuseAllTransitions()
        {
            Assert.IsTrue(TaskLifecycleInspector.IsTerminalStatus("Complete"));
            Assert.IsTrue(TaskLifecycleInspector.IsTerminalStatus("Cancelled"));
            Assert.IsTrue(TaskLifecycleInspector.IsTerminalStatus("ReworkRequired"));
            Assert.IsFalse(TaskLifecycleInspector.IsTerminalStatus("Failed"));
            Assert.IsFalse(TaskLifecycleInspector.IsTerminalStatus("Queued"));
            Assert.IsFalse(TaskLifecycleInspector.IsTerminalStatus("InProgress"));
            Assert.IsFalse(TaskLifecycleInspector.IsTerminalStatus("Interrupted"));
        }

        [Test]
        public void EveryTerminalDispositionIsNamed()
        {
            StringAssert.Contains("terminal", TaskLifecycleInspector.TerminalDispositionName("Complete"));
            StringAssert.Contains("no cancellation day stamp", TaskLifecycleInspector.TerminalDispositionName("Cancelled"));
            StringAssert.Contains("FollowUpTaskIds", TaskLifecycleInspector.TerminalDispositionName("ReworkRequired"));
            StringAssert.Contains("ReworkRequired", TaskLifecycleInspector.TerminalDispositionName("Failed"));
            Assert.IsNull(TaskLifecycleInspector.TerminalDispositionName("Queued"));
        }

        [Test]
        public void TransitionLegalityFollowsThePolicyTable()
        {
            Assert.IsTrue(TaskLifecycleInspector.IsLegalTransition("Queued", "Assigned"));
            Assert.IsTrue(TaskLifecycleInspector.IsLegalTransition("Interrupted", "Queued"));
            Assert.IsTrue(TaskLifecycleInspector.IsLegalTransition("Failed", "ReworkRequired"));
            Assert.IsFalse(TaskLifecycleInspector.IsLegalTransition("Queued", "Complete"));
            Assert.IsFalse(TaskLifecycleInspector.IsLegalTransition("Complete", "Queued"));
            Assert.IsFalse(TaskLifecycleInspector.IsLegalTransition("Complete", "Cancelled"));
            Assert.IsFalse(TaskLifecycleInspector.IsLegalTransition("Cancelled", "Queued"));
            Assert.IsFalse(TaskLifecycleInspector.IsLegalTransition("Failed", "Complete"));
            Assert.IsFalse(TaskLifecycleInspector.IsLegalTransition("Bogus", "Queued"));
            Assert.IsFalse(TaskLifecycleInspector.IsLegalTransition("Queued", "Bogus"));
        }

        [Test]
        public void BelowThresholdIsWithinThresholdNeverAnIncident()
        {
            LifecycleAssessment result = TaskLifecycleInspector.Inspect(QueuedTask(5, false));
            Assert.AreEqual(LifecycleDisposition.WithinThreshold, result.Disposition);
            Assert.AreEqual(IncidentSeverity.Information, result.Severity);
            Assert.IsFalse(result.IsIncident);
            Assert.AreEqual(72d * Hour, result.StallThresholdSeconds);
            Assert.AreEqual(5d * Hour, result.StalledSeconds);
        }

        [Test]
        public void QueuedPastThresholdWithNoAssigneeIsTheQueuedGap()
        {
            LifecycleAssessment result = TaskLifecycleInspector.Inspect(QueuedTask(80, false));
            Assert.AreEqual(LifecycleDisposition.StalledNoAssignee, result.Disposition);
            Assert.AreEqual(IncidentSeverity.Warning, result.Severity);
            Assert.IsTrue(result.IsIncident);
            // Threshold AND measured duration are both recorded (C-10).
            Assert.AreEqual(72d * Hour, result.StallThresholdSeconds);
            Assert.AreEqual(80d * Hour, result.StalledSeconds);
            // "No assignee" is distinguished from "assignee idle" in the text.
            StringAssert.Contains("NO assignee", result.Explanation);
            StringAssert.Contains("Queued gap", result.Explanation);
            StringAssert.Contains("Lane 9d", result.RepairOwner);
        }

        [Test]
        public void QueuedPastThresholdWithIdleAssigneeIsDistinctFromNoAssignee()
        {
            ObligationLifecycleSnapshot snapshot = QueuedTask(80, true);
            snapshot.PersonId = "P14";
            LifecycleAssessment result = TaskLifecycleInspector.Inspect(snapshot);
            Assert.AreEqual(LifecycleDisposition.StalledAssigneeIdle, result.Disposition);
            Assert.AreEqual(IncidentSeverity.Warning, result.Severity);
            Assert.IsTrue(result.HasAssignee);
            StringAssert.Contains("has an assignee", result.Explanation);
            StringAssert.Contains("No reservation is held", result.Explanation);
        }

        [Test]
        public void OccupiedWorkstationPastThresholdIsALegitimateWait()
        {
            ObligationLifecycleSnapshot snapshot = QueuedTask(80, true);
            snapshot.Status = "Assigned";
            snapshot.RefusalReason = "Reservation refused: stove-h6 is already reserved (P3)";
            snapshot.ReservationId = "res-stove-h6";
            snapshot.HasReservation = true;
            snapshot.WorkstationOccupied = true;
            snapshot.OccupantDescription = "P3";
            LifecycleAssessment result = TaskLifecycleInspector.Inspect(snapshot);
            Assert.AreEqual(LifecycleDisposition.StalledLegitimateWait, result.Disposition);
            Assert.AreEqual(IncidentSeverity.Information, result.Severity);
            Assert.IsFalse(result.IsIncident);
            Assert.AreEqual(RefusalCauseClass.DestinationOccupiedOrReserved, result.BlockerClass);
        }

        [Test]
        public void SkillOnlyCauseIsUnknownNeverABlameOnSkill()
        {
            ObligationLifecycleSnapshot snapshot = QueuedTask(80, true);
            snapshot.RefusalReason = "worker skill level too low for this task";
            snapshot.SkillMentionedAsOnlyCause = true;
            LifecycleAssessment result = TaskLifecycleInspector.Inspect(snapshot);
            Assert.AreEqual(IncidentSeverity.Unknown, result.Severity);
            StringAssert.Contains("skill", result.Explanation.ToLowerInvariant());
            StringAssert.Contains("physical destination state", string.Join("; ", result.MissingEvidence));
        }

        [Test]
        public void CompletedTaskIsTerminalCompletedWithNamedDisposition()
        {
            var snapshot = new ObligationLifecycleSnapshot
            {
                ObligationId = "T2",
                Kind = ObligationKind.Task,
                DefinitionId = "domestic.meal-prep",
                Status = "Complete",
                NowSimSeconds = 10 * Hour,
            };
            LifecycleAssessment result = TaskLifecycleInspector.Inspect(snapshot);
            Assert.AreEqual(LifecycleDisposition.TerminalCompleted, result.Disposition);
            Assert.AreEqual(IncidentSeverity.Information, result.Severity);
            Assert.IsFalse(result.IsIncident);
            StringAssert.Contains("Complete", result.TerminalDisposition);
        }

        [Test]
        public void FailedTaskNamesItsOnlyOnwardTransition()
        {
            var snapshot = new ObligationLifecycleSnapshot
            {
                ObligationId = "T3",
                Kind = ObligationKind.Task,
                DefinitionId = "domestic.meal-prep",
                Status = "Failed",
                NowSimSeconds = 10 * Hour,
            };
            LifecycleAssessment result = TaskLifecycleInspector.Inspect(snapshot);
            Assert.AreEqual(LifecycleDisposition.TerminalCompleted, result.Disposition);
            StringAssert.Contains("ReworkRequired", result.TerminalDisposition);
        }

        [Test]
        public void KnownFuelHandlingStrandIsInterruptedNeverRequeued()
        {
            LifecycleAssessment result = TaskLifecycleInspector.InspectKnownFuelHandlingStrand();
            Assert.AreEqual(LifecycleDisposition.InterruptedNeverRequeued, result.Disposition);
            Assert.AreEqual(IncidentSeverity.Warning, result.Severity);
            Assert.IsTrue(result.IsIncident);
            Assert.AreEqual(RefusalCauseClass.DestinationMissing, result.BlockerClass);
            StringAssert.Contains("DomesticLaborDriver", result.ResponsibleAuthority);
            StringAssert.Contains("firewood", result.EarliestBlocker);
            StringAssert.Contains("Lane 9c", result.RepairOwner);
            Assert.AreEqual(72d * Hour, result.StallThresholdSeconds);
            Assert.AreEqual(83.5d * Hour, result.StalledSeconds);
        }

        [Test]
        public void FreightRequestStrandIsTerminalButUnrecoveredCritical()
        {
            LifecycleAssessment result = TaskLifecycleInspector.InspectKnownFreightRequestStrand();
            Assert.AreEqual(LifecycleDisposition.TerminalButUnrecovered, result.Disposition);
            Assert.AreEqual(IncidentSeverity.Critical, result.Severity);
            Assert.IsTrue(result.IsIncident);
            StringAssert.Contains("FreightServiceAgreements", result.ResponsibleAuthority);
            StringAssert.Contains("ReopenRefusedRequest is Refused-only", result.Explanation);
            StringAssert.Contains("Lane 9d", result.RepairOwner);
            StringAssert.Contains("F-02", result.Explanation);
        }

        [Test]
        public void RefusedFreightRequestIsReopenableTerminal()
        {
            var snapshot = new ObligationLifecycleSnapshot
            {
                ObligationId = "request-9",
                Kind = ObligationKind.FreightRequest,
                Status = "Refused",
                RefusalReason = "No carrier capacity on the day.",
            };
            LifecycleAssessment result = TaskLifecycleInspector.Inspect(snapshot);
            Assert.AreEqual(LifecycleDisposition.TerminalCompleted, result.Disposition);
            StringAssert.Contains("ReopenRefusedRequest", result.TerminalDisposition);
        }

        [Test]
        public void AcceptedFreightRequestWithLiveAgreementIsHealthy()
        {
            var snapshot = new ObligationLifecycleSnapshot
            {
                ObligationId = "request-2",
                Kind = ObligationKind.FreightRequest,
                Status = "Accepted",
                RelatedAgreementId = "freight-agreement:request-2",
            };
            LifecycleAssessment result = TaskLifecycleInspector.Inspect(snapshot);
            Assert.AreEqual(LifecycleDisposition.Healthy, result.Disposition);
            Assert.IsFalse(result.IsIncident);
        }

        [Test]
        public void PreLoadFailedShipmentIsTerminalRecoveredNotAStrand()
        {
            var snapshot = new ObligationLifecycleSnapshot
            {
                ObligationId = "freight-shipment:request-3",
                Kind = ObligationKind.FreightShipment,
                Status = "Failed",
                IsPreLoad = true,
                AgreementCancelled = true,
                RecoveryAttempts = 2,
                RecoveryAttemptLimit = 2,
            };
            LifecycleAssessment result = TaskLifecycleInspector.Inspect(snapshot);
            Assert.AreEqual(LifecycleDisposition.TerminalRecovered, result.Disposition);
            Assert.AreEqual(IncidentSeverity.Information, result.Severity);
            Assert.IsFalse(result.IsIncident);
            StringAssert.Contains("Failed before load", result.TerminalDisposition);
        }

        [Test]
        public void PostLoadExhaustedShipmentIsTheSilentStrand()
        {
            var snapshot = new ObligationLifecycleSnapshot
            {
                ObligationId = "freight-shipment:request-4",
                Kind = ObligationKind.FreightShipment,
                Status = "Planned",
                IsPostLoadCommitted = true,
                RecoveryAttempts = 2,
                RecoveryAttemptLimit = 2,
                LastProgressSimSeconds = 5 * 86400d,
                NowSimSeconds = 6 * 86400d,
            };
            LifecycleAssessment result = TaskLifecycleInspector.Inspect(snapshot);
            Assert.AreEqual(LifecycleDisposition.TerminalButUnrecovered, result.Disposition);
            Assert.AreEqual(IncidentSeverity.Critical, result.Severity);
            StringAssert.Contains("remains held", result.Explanation);
            StringAssert.Contains("Lane 9d", result.RepairOwner);
        }

        [Test]
        public void StagingWaitWithNoDeadlineIsUnbounded()
        {
            LifecycleAssessment result = TaskLifecycleInspector.InspectKnownStagingWait();
            Assert.AreEqual(LifecycleDisposition.UnboundedWait, result.Disposition);
            Assert.AreEqual(IncidentSeverity.Warning, result.Severity);
            Assert.IsTrue(result.IsIncident);
            StringAssert.Contains("zero deadline", result.EarliestBlocker);
            StringAssert.Contains("FreightDispatchStaging", result.AuthoritySourcePath);
            StringAssert.Contains("Lane 9d", result.RepairOwner);
        }

        [Test]
        public void CompletedFurnitureInstallWithUnresolvedOrderIsTerminalButUnrecovered()
        {
            LifecycleAssessment result = TaskLifecycleInspector.InspectKnownFurnitureInstallStrand();
            Assert.AreEqual(LifecycleDisposition.TerminalButUnrecovered, result.Disposition);
            Assert.AreEqual(IncidentSeverity.Warning, result.Severity);
            Assert.IsTrue(result.IsIncident);
            StringAssert.Contains("FurnishingInstallTaskDriver.cs", result.AuthoritySourcePath);
            Assert.Greater(result.AuthoritySourceLine, 0);
            StringAssert.Contains("order-line advancement", result.RepairOwner);
            StringAssert.Contains("household custody", result.Explanation);
            StringAssert.Contains("Synthetic failure-shaped snapshot", result.SourceNote);
            StringAssert.Contains("current source includes", result.SourceNote);
        }

        [Test]
        public void FurniturePlacementRefusalIsTerminalWithCustodyNamed()
        {
            var snapshot = new ObligationLifecycleSnapshot
            {
                ObligationId = "furniture-order-2",
                Kind = ObligationKind.FurnitureOrder,
                Status = "Open",
                OrderPaid = true,
                PlacementRefused = true,
            };
            LifecycleAssessment result = TaskLifecycleInspector.Inspect(snapshot);
            Assert.AreEqual(LifecycleDisposition.TerminalCompleted, result.Disposition);
            StringAssert.Contains("household custody", result.TerminalDisposition);
        }

        [Test]
        public void ShoppingTripPastItsOutboundDeadlineIsPastDeadline()
        {
            var snapshot = new ObligationLifecycleSnapshot
            {
                ObligationId = "trip-2-77",
                Kind = ObligationKind.ShoppingTrip,
                Status = "Outbound",
                DeadlineSimSeconds = 24 * Hour,
                DeadlineKind = "Outbound 24h absolute deadline",
                NowSimSeconds = 30 * Hour,
                HasLivenessBound = true,
            };
            LifecycleAssessment result = TaskLifecycleInspector.Inspect(snapshot);
            Assert.AreEqual(LifecycleDisposition.PastDeadline, result.Disposition);
            Assert.AreEqual(IncidentSeverity.Warning, result.Severity);
            Assert.IsTrue(result.IsIncident);
        }

        [Test]
        public void ShoppingTripWithNoCapturedDeadlineIsUnknownNotUnbounded()
        {
            var snapshot = new ObligationLifecycleSnapshot
            {
                ObligationId = "trip-1",
                Kind = ObligationKind.ShoppingTrip,
                Status = "Outbound",
                DeadlineSimSeconds = -1,
                NowSimSeconds = 10 * Hour,
                HasLivenessBound = false,
            };
            LifecycleAssessment result = TaskLifecycleInspector.Inspect(snapshot);
            Assert.AreEqual(LifecycleDisposition.Unknown, result.Disposition);
            Assert.AreEqual(IncidentSeverity.Unknown, result.Severity);
            StringAssert.Contains("24h absolute deadline", result.Explanation);
        }

        [Test]
        public void ConstructionCommissionWithNoBoundIsUnboundedWait()
        {
            var snapshot = new ObligationLifecycleSnapshot
            {
                ObligationId = "commission-1",
                Kind = ObligationKind.ConstructionCommission,
                Status = "AwaitingMaterials",
                DeadlineSimSeconds = -1,
                NowSimSeconds = 10 * 86400d,
                HasLivenessBound = false,
            };
            LifecycleAssessment result = TaskLifecycleInspector.Inspect(snapshot);
            Assert.AreEqual(LifecycleDisposition.UnboundedWait, result.Disposition);
            Assert.AreEqual(IncidentSeverity.Warning, result.Severity);
        }

        [Test]
        public void NullSnapshotIsUnknownNeverAThrow()
        {
            LifecycleAssessment result = TaskLifecycleInspector.Inspect(null);
            Assert.AreEqual(LifecycleDisposition.Unknown, result.Disposition);
            Assert.AreEqual(IncidentSeverity.Unknown, result.Severity);
            Assert.AreEqual(1, result.MissingEvidence.Count);
        }

        [Test]
        public void SummarizeCountsAndNamesUnobservedKinds()
        {
            var assessments = new List<LifecycleAssessment>
            {
                TaskLifecycleInspector.Inspect(QueuedTask(80, false)),
                TaskLifecycleInspector.InspectKnownFuelHandlingStrand(),
                TaskLifecycleInspector.InspectKnownStagingWait(),
            };
            LifecycleCoverage coverage = TaskLifecycleInspector.Summarize(assessments);
            Assert.AreEqual(3, coverage.TotalAssessed);
            Assert.AreEqual(3, coverage.IncidentCount);
            Assert.AreEqual(1, coverage.ByDisposition[LifecycleDisposition.StalledNoAssignee]);
            Assert.AreEqual(1, coverage.ByDisposition[LifecycleDisposition.InterruptedNeverRequeued]);
            Assert.AreEqual(1, coverage.ByDisposition[LifecycleDisposition.UnboundedWait]);
            Assert.AreEqual(3, coverage.BySeverity[IncidentSeverity.Warning]);
            Assert.AreEqual(2, coverage.ByKind[ObligationKind.Task]);
            Assert.AreEqual(1, coverage.ByKind[ObligationKind.FreightShipment]);
            // Eight kinds were never observed — listed, not silently passed.
            Assert.AreEqual(8, coverage.KindsNotObserved.Count);
            CollectionAssert.Contains(coverage.KindsNotObserved, ObligationKind.ShoppingTrip);
            CollectionAssert.Contains(coverage.KindsNotObserved, ObligationKind.FurnitureOrder);
        }

        [Test]
        public void OnlyIncidentsAreEmittedToTheCore()
        {
            var core = new FakeIncidentCore();
            IncidentInvestigator.IncidentCore = core;
            try
            {
                TaskLifecycleInspector.InspectAndEmit(QueuedTask(5, false));
                Assert.AreEqual(0, core.Received.Count);
                TaskLifecycleInspector.InspectAndEmit(QueuedTask(80, false));
                Assert.AreEqual(1, core.Received.Count);
                Assert.AreEqual("LIFECYCLE-Task-T1", core.Received[0].IncidentId);
                Assert.AreEqual(IncidentSeverity.Warning, core.Received[0].Severity);
                TaskLifecycleInspector.InspectAndEmit(TaskLifecycleInspector.KnownFuelHandlingStrandSnapshot());
                Assert.AreEqual(2, core.Received.Count);
                Assert.AreEqual("LIFECYCLE-Task-T9", core.Received[1].IncidentId);
                Assert.AreEqual(DestinationBlockerKind.DestinationMissing, core.Received[1].BlockerKind);
            }
            finally
            {
                IncidentInvestigator.IncidentCore = null;
            }
        }
    }
}
