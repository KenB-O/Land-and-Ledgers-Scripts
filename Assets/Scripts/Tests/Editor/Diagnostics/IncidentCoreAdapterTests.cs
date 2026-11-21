// Muse Pass — coordinator (Nov 20, 2026). Tests for the Lane 1/2 seam:
// the adapter translating Lane 2 IncidentFindings into Lane 1
// IncidentReports, and the shared-core wiring. Tests only; no
// production code.
using System.Collections.Generic;
using LandLedgers.Diagnostics;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Diagnostics
{
    [TestFixture]
    public sealed class IncidentCoreAdapterTests
    {
        private sealed class CapturingSink
        {
            public readonly List<string> Warnings = new List<string>();
            public readonly List<string> Info = new List<string>();

            public IncidentWarningAuthority CreateAuthority()
            {
                var authority = new IncidentWarningAuthority();
                authority.LogSink = line => Warnings.Add(line);
                authority.InfoSink = line => Info.Add(line);
                return authority;
            }
        }

        private sealed class RecordingCore : IIncidentCore
        {
            public readonly List<IncidentFinding> Received = new List<IncidentFinding>();
            public void EmitClassifiedIncident(IncidentFinding finding) { Received.Add(finding); }
        }

        private static IncidentFinding Finding(string id)
        {
            return new IncidentFinding
            {
                IncidentId = id,
                Title = "Fuel-handling stall",
                Severity = IncidentSeverity.Warning,
                BlockerKind = DestinationBlockerKind.DestinationMissing,
                ResponsibleAuthority = "DomesticLaborDriver",
                AuthoritySourcePath = "Assets/Scripts/Domains/Population/DomesticLaborDriver.cs",
                AuthoritySourceLine = 123,
                EarliestBlocker = "No Firewood activity point authored for household H7",
                EarliestBlockerSeconds = 7200,
                Explanation = "Interrupted domestic.fuel-handling tasks, never re-queued.",
                TaskId = "T9",
                PersonNumericId = 42,
                HouseholdId = 7,
                EvidenceComplete = true,
            };
        }

        [Test]
        public void ToReport_MapsFindingFields()
        {
            IncidentReport report = IncidentCoreAdapter.ToReport(Finding("CI-TEST-ADAPTER"));

            Assert.AreEqual("CI-TEST-ADAPTER", report.SignatureId);
            Assert.AreEqual(IncidentSeverity.Warning, report.Severity);
            Assert.AreEqual("Fuel-handling stall", report.Summary);
            Assert.AreEqual(-1, report.DayIndex);
            Assert.AreEqual(7200d, report.SimSeconds);
            Assert.AreEqual("DomesticLaborDriver", report.OwningSystem);
            Assert.AreEqual("DomesticLaborDriver", report.RecoveryAuthority);
            Assert.AreEqual(RefusalCauseClass.DestinationMissing, report.CauseClass);
            Assert.AreEqual("No Firewood activity point authored for household H7", report.CauseEvidence);
            Assert.AreEqual("T9", report.TaskId);
            Assert.AreEqual("P42", report.PersonId);
            Assert.AreEqual("H7", report.HouseholdId);
            Assert.AreEqual("Assets/Scripts/Domains/Population/DomesticLaborDriver.cs:123", report.EvidenceReference);
            Assert.AreEqual("No Firewood activity point authored for household H7", report.PhysicalCapabilityBlocker);
        }

        [Test]
        public void MapBlockerKind_CoversAllClasses()
        {
            Assert.AreEqual(RefusalCauseClass.Unclassified, IncidentCoreAdapter.MapBlockerKind(DestinationBlockerKind.None));
            Assert.AreEqual(RefusalCauseClass.DestinationMissing, IncidentCoreAdapter.MapBlockerKind(DestinationBlockerKind.DestinationMissing));
            Assert.AreEqual(RefusalCauseClass.DestinationInaccessible, IncidentCoreAdapter.MapBlockerKind(DestinationBlockerKind.DestinationInaccessible));
            Assert.AreEqual(RefusalCauseClass.DestinationOccupiedOrReserved, IncidentCoreAdapter.MapBlockerKind(DestinationBlockerKind.DestinationOccupiedOrReserved));
            Assert.AreEqual(RefusalCauseClass.WorkerCannotPerform, IncidentCoreAdapter.MapBlockerKind(DestinationBlockerKind.WorkerCannotPerform));
            Assert.AreEqual(RefusalCauseClass.OtherObligationTakesPriority, IncidentCoreAdapter.MapBlockerKind(DestinationBlockerKind.OtherObligationTakesPriority));
            Assert.AreEqual(RefusalCauseClass.OriginInvalid, IncidentCoreAdapter.MapBlockerKind(DestinationBlockerKind.OriginInvalid));
        }

        [Test]
        public void EmitClassifiedIncident_IdempotentForRepeatedIncidentId()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            var adapter = new IncidentCoreAdapter(authority);

            adapter.EmitClassifiedIncident(Finding("CI-TEST-REPEAT"));
            adapter.EmitClassifiedIncident(Finding("CI-TEST-REPEAT"));

            Assert.AreEqual(1, authority.RetainedCount);
            Assert.AreEqual(2, authority.TotalOccurrences);
            Assert.AreEqual(1, sink.Warnings.Count);
            SimulationIncident incident = authority.TryGetIncident("CI-TEST-REPEAT");
            Assert.IsNotNull(incident);
            Assert.AreEqual(IncidentSeverity.Warning, incident.Severity);
            Assert.AreEqual(RefusalCauseClass.DestinationMissing, incident.CauseClass);
        }

        [Test]
        public void EmitClassifiedIncident_NullFinding_DoesNotThrow()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            var adapter = new IncidentCoreAdapter(authority);

            adapter.EmitClassifiedIncident(null);

            Assert.AreEqual(0, authority.RetainedCount);
        }

        [Test]
        public void EmitClassifiedIncident_DisabledAuthority_DoesNotThrow()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            authority.Enabled = false;
            var adapter = new IncidentCoreAdapter(authority);

            adapter.EmitClassifiedIncident(Finding("CI-TEST-DISABLED"));

            Assert.AreEqual(0, authority.RetainedCount);
            Assert.AreEqual(0, sink.Warnings.Count);
        }

        [Test]
        public void EmitToCore_ForwardsWhenWired()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            IIncidentCore previous = IncidentInvestigator.IncidentCore;
            try
            {
                IncidentInvestigator.IncidentCore = new IncidentCoreAdapter(authority);
                IncidentInvestigator.EmitToCore(Finding("CI-TEST-FORWARD"));
            }
            finally
            {
                IncidentInvestigator.IncidentCore = previous;
            }

            Assert.AreEqual(1, authority.RetainedCount);
            Assert.IsNotNull(authority.TryGetIncident("CI-TEST-FORWARD"));
        }

        [Test]
        public void WireInvestigator_DoesNotOverrideExistingCore()
        {
            var stub = new RecordingCore();
            IIncidentCore previous = IncidentInvestigator.IncidentCore;
            try
            {
                IncidentInvestigator.IncidentCore = stub;
                IncidentDiagnostics.WireInvestigator();
                Assert.AreSame(stub, IncidentInvestigator.IncidentCore);
            }
            finally
            {
                IncidentInvestigator.IncidentCore = previous;
            }
        }
    }
}
