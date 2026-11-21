// Lane 2 (Muse Pass, Nov 20 2026) — harness tests for the Simulation
// Incident Investigator core (IncidentInvestigator). Pure-System tests;
// the Editor window itself cannot join the harness closure and is verified
// by ParseCheck instead.
using System.Collections.Generic;
using LandLedgers.Diagnostics;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Diagnostics
{
    public sealed class IncidentInvestigatorTests
    {
        private sealed class FakeIncidentCore : IIncidentCore
        {
            public readonly List<IncidentFinding> Received = new List<IncidentFinding>();
            public void EmitClassifiedIncident(IncidentFinding finding) { Received.Add(finding); }
        }

        private static StallEvidence IdleStall(double stalledHours, string status = "Queued")
        {
            return new StallEvidence
            {
                TaskId = "T1",
                TaskDefinitionId = "domestic.fuel-handling",
                TaskStatus = status,
                TaskOwnerId = "H7",
                HouseholdId = -1,
                WorkedMinutes = 0,
                RemainingMinutes = 41,
                FirstObservedSeconds = 0,
                LastProgressSeconds = 0,
                NowSeconds = stalledHours * 3600d,
            };
        }

        [Test]
        public void StallThresholdsArePerStateNotOneFixedValue()
        {
            Assert.AreEqual(6d * 3600d, IncidentInvestigator.StallThresholdSeconds("InProgress"));
            Assert.AreEqual(12d * 3600d, IncidentInvestigator.StallThresholdSeconds("Assigned"));
            Assert.AreEqual(12d * 3600d, IncidentInvestigator.StallThresholdSeconds("ResourcesReserved"));
            Assert.AreEqual(48d * 3600d, IncidentInvestigator.StallThresholdSeconds("WaitingOnDependency"));
            Assert.AreEqual(48d * 3600d, IncidentInvestigator.StallThresholdSeconds("WaitingOnPassiveProcess"));
            Assert.AreEqual(72d * 3600d, IncidentInvestigator.StallThresholdSeconds("Queued"));
            Assert.AreEqual(72d * 3600d, IncidentInvestigator.StallThresholdSeconds("Interrupted"));
            Assert.AreEqual(72d * 3600d, IncidentInvestigator.StallThresholdSeconds(""));
            Assert.AreEqual(72d * 3600d, IncidentInvestigator.StallThresholdSeconds(null));
        }

        [Test]
        public void BelowThresholdIsInformationNeverWarning()
        {
            StallClassification result = IncidentInvestigator.ClassifyStall(IdleStall(5, "InProgress"));
            Assert.AreEqual(IncidentSeverity.Information, result.Severity);
            StringAssert.Contains("Within the per-state stall threshold", result.Explanation);
        }

        [Test]
        public void OccupiedWorkstationIsInformation()
        {
            StallEvidence evidence = IdleStall(80);
            evidence.RefusalReason = "Destination is occupied by another worker";
            evidence.WorkstationOccupied = true;
            evidence.OccupantDescription = "P4 at the stove";
            StallClassification result = IncidentInvestigator.ClassifyStall(evidence);
            Assert.AreEqual(IncidentSeverity.Information, result.Severity);
            Assert.AreEqual(DestinationBlockerKind.DestinationOccupiedOrReserved, result.BlockerKind);
        }

        [Test]
        public void OtherObligationTakesPriorityIsInformation()
        {
            StallEvidence evidence = IdleStall(80);
            evidence.RefusalReason = "Worker has a higher priority obligation today";
            StallClassification result = IncidentInvestigator.ClassifyStall(evidence);
            Assert.AreEqual(IncidentSeverity.Information, result.Severity);
            Assert.AreEqual(DestinationBlockerKind.OtherObligationTakesPriority, result.BlockerKind);
        }

        [Test]
        public void IdleAllDayWithoutValidCauseIsWarning()
        {
            StallClassification result = IncidentInvestigator.ClassifyStall(IdleStall(85.5));
            Assert.AreEqual(IncidentSeverity.Warning, result.Severity);
            StringAssert.Contains("idle without cause", result.Explanation);
            Assert.IsTrue(result.MissingEvidence.Count > 0, "a warning without a named cause must name the missing evidence");
        }

        [Test]
        public void IntegrityViolationIsCritical()
        {
            StallEvidence evidence = IdleStall(85.5);
            evidence.IntegrityViolation = true;
            evidence.IntegrityDetail = "Settlement moved custody without payment.";
            StallClassification result = IncidentInvestigator.ClassifyStall(evidence);
            Assert.AreEqual(IncidentSeverity.Critical, result.Severity);
        }

        [Test]
        public void InsufficientEvidenceIsUnknown()
        {
            StallClassification result = IncidentInvestigator.ClassifyStall(new StallEvidence());
            Assert.AreEqual(IncidentSeverity.Unknown, result.Severity);
            Assert.IsTrue(result.MissingEvidence.Count > 0);
        }

        [Test]
        public void SkillOnlyCauseIsUnknownNeverSkillBlame()
        {
            // M-05: no refusal path reads capability. A diagnostic that
            // blames skill for a refusal is wrong by construction.
            StallEvidence evidence = IdleStall(85.5);
            evidence.SkillMentionedAsOnlyCause = true;
            evidence.RefusalReason = "worker skill level too low";
            StallClassification result = IncidentInvestigator.ClassifyStall(evidence);
            Assert.AreEqual(IncidentSeverity.Unknown, result.Severity);
            StringAssert.Contains("skill", result.Explanation.ToLowerInvariant());
            Assert.IsTrue(result.MissingEvidence.Count > 0);
            StringAssert.Contains("physical destination state", string.Join("; ", result.MissingEvidence));
        }

        [Test]
        public void RefusalReasonsClassifyIntoSixDestinationClasses()
        {
            Assert.AreEqual(DestinationBlockerKind.DestinationMissing,
                IncidentInvestigator.ClassifyRefusalReason("firewood: no firewood storage is authored on the home."));
            Assert.AreEqual(DestinationBlockerKind.OriginInvalid,
                IncidentInvestigator.ClassifyRefusalReason("Pathing request failed (StartNotWalkable): Point is outside the authored walkable floor area on floor 0."));
            Assert.AreEqual(DestinationBlockerKind.DestinationInaccessible,
                IncidentInvestigator.ClassifyRefusalReason("No physical route reaches seller location 'building:0'"));
            Assert.AreEqual(DestinationBlockerKind.DestinationOccupiedOrReserved,
                IncidentInvestigator.ClassifyRefusalReason("Resident already has an active task or scheduled route to building 4"));
            Assert.AreEqual(DestinationBlockerKind.WorkerCannotPerform,
                IncidentInvestigator.ClassifyRefusalReason("No worker available for this task"));
            Assert.AreEqual(DestinationBlockerKind.OtherObligationTakesPriority,
                IncidentInvestigator.ClassifyRefusalReason("Worker has a higher priority obligation"));
            Assert.AreEqual(DestinationBlockerKind.None,
                IncidentInvestigator.ClassifyRefusalReason("something unexplained happened"));
            Assert.AreEqual(DestinationBlockerKind.None,
                IncidentInvestigator.ClassifyRefusalReason(null));
        }

        [Test]
        public void HouseholdFallsBackToOwnerIdWhenAnomalyLosesIt()
        {
            // C R-18: the anomaly report records household=-1 while the
            // trace records ownerId H7/H4/H6.
            Assert.AreEqual(7, IncidentInvestigator.ResolveHouseholdId(-1, "H7"));
            Assert.AreEqual(4, IncidentInvestigator.ResolveHouseholdId(-1, "H4"));
            Assert.AreEqual(88, IncidentInvestigator.ResolveHouseholdId(88, "H7"));
            Assert.AreEqual(-1, IncidentInvestigator.ResolveHouseholdId(-1, ""));
            Assert.AreEqual(-1, IncidentInvestigator.ResolveHouseholdId(-1, null));
        }

        [Test]
        public void ProvingCaseFuelHandlingNamesAuthorityAndEarliestBlocker()
        {
            IncidentFinding finding = IncidentInvestigator.InvestigateKnownFuelHandlingStall();
            Assert.AreEqual(IncidentSeverity.Warning, finding.Severity);
            Assert.AreEqual(DestinationBlockerKind.DestinationMissing, finding.BlockerKind);
            StringAssert.Contains("DomesticLaborDriver", finding.ResponsibleAuthority);
            StringAssert.Contains("Firewood", finding.ResponsibleAuthority);
            StringAssert.Contains("firewood", finding.EarliestBlocker);
            Assert.AreEqual(2d * 3600d, finding.EarliestBlockerSeconds,
                "the earliest confirmed blocker is the interrupt at hour 2, not the anomaly detection at hour 85.5");
            Assert.AreEqual("INV-domestic.fuel-handling-T9", finding.IncidentId);
            Assert.IsTrue(finding.Timeline.Count >= 3);
            Assert.IsTrue(finding.Timeline[0].SimulatedSeconds < finding.Timeline[finding.Timeline.Count - 1].SimulatedSeconds);
        }

        [Test]
        public void ProvingCaseWholesaleNamesAuthorityAndBlockingGate()
        {
            IncidentFinding finding = IncidentInvestigator.InvestigateWholesaleObligation(
                IncidentInvestigator.KnownWholesaleObligationEvidence());
            Assert.AreEqual(IncidentSeverity.Warning, finding.Severity);
            StringAssert.Contains("DevelopmentTownConfiguration.asset", finding.ResponsibleAuthority);
            StringAssert.Contains("SeedBakeryWholesaleAccounts", finding.ResponsibleAuthority);
            StringAssert.Contains("ShouldBeginStagedBakeryBatch", finding.Explanation);
            StringAssert.Contains("24", finding.EarliestBlocker);
            Assert.AreEqual(0d, finding.EarliestBlockerSeconds,
                "the obligation exists from day 0 — the config entry itself is the earliest blocker");
            Assert.IsTrue(finding.EvidenceComplete);
        }

        [Test]
        public void InvestigateTaskStallWithNoEventsIsUnknownWithMissingEvidenceNamed()
        {
            IncidentFinding finding = IncidentInvestigator.InvestigateTaskStall(null, 0);
            Assert.AreEqual(IncidentSeverity.Unknown, finding.Severity);
            Assert.IsTrue(finding.MissingEvidence.Count > 0);
        }

        [Test]
        public void Lane1SeamEmitsClassifiedFindingToCore()
        {
            var core = new FakeIncidentCore();
            IncidentInvestigator.IncidentCore = core;
            try
            {
                IncidentFinding finding = IncidentInvestigator.InvestigateKnownFuelHandlingStall();
                Assert.AreEqual(1, core.Received.Count);
                Assert.AreSame(finding, core.Received[0]);
                Assert.AreEqual("INV-domestic.fuel-handling-T9", core.Received[0].IncidentId);
            }
            finally
            {
                IncidentInvestigator.IncidentCore = null;
            }
        }

        [Test]
        public void NoCoreAttachedEmitsNothingAndStillReturnsFinding()
        {
            IncidentInvestigator.IncidentCore = null;
            IncidentFinding finding = IncidentInvestigator.InvestigateKnownFuelHandlingStall();
            Assert.AreEqual(IncidentSeverity.Warning, finding.Severity);
        }
    }
}
