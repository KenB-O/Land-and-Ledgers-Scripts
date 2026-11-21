using System.Collections.Generic;
using LandLedgers.Diagnostics;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Diagnostics
{
    /// <summary>
    /// Lane 6 (Tool 12) — focused tests for the Simulation Health and Issue
    /// Control Center: the 7-day escalation sweep (escalates a 7-day-old
    /// standing incident, not a 6-day-old one), threshold changes taking
    /// effect, dedup counts surviving sweep and close, the [CLOSED]
    /// emitter (E W-5), filters, per-signature snapshot diffs, and the
    /// issue register. Tests only; no production code.
    /// </summary>
    [TestFixture]
    public sealed class SimulationHealthControlCenterTests
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

        private static IncidentReport Report(string signatureId, IncidentSeverity severity, int day,
            string summary = "test condition", string owningSystem = "Test")
        {
            return new IncidentReport
            {
                SignatureId = signatureId,
                Severity = severity,
                Summary = summary,
                DayIndex = day,
                SimSeconds = day * 86400d,
                OwningSystem = owningSystem,
                RecoveryAuthority = "TestDriver",
            };
        }

        [Test]
        public void EscalationSweep_EscalatesSevenDayOldStandingIncident()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            authority.Emit(Report("CI-TEST-SWEEP-7", IncidentSeverity.Warning, 0));
            int warningsBeforeSweep = sink.Warnings.Count;

            IReadOnlyList<string> escalated = authority.EvaluateEscalationSweep(7, 7 * 86400d);

            Assert.AreEqual(1, escalated.Count);
            Assert.AreEqual("CI-TEST-SWEEP-7", escalated[0]);
            SimulationIncident incident = authority.TryGetIncident("CI-TEST-SWEEP-7");
            Assert.AreEqual(IncidentStatus.Escalated, incident.Status);
            // The status history grew by the escalation entry.
            Assert.AreEqual(2, incident.StatusHistory.Count);
            Assert.AreEqual(IncidentStatus.Escalated, incident.StatusHistory[1].ToStatus);
            // The sweep emits one [ESCALATED] line naming system + recovery owner (W §7).
            Assert.AreEqual(warningsBeforeSweep + 1, sink.Warnings.Count);
            StringAssert.Contains("[ESCALATED]", sink.Warnings[sink.Warnings.Count - 1]);
            StringAssert.Contains("CI-TEST-SWEEP-7", sink.Warnings[sink.Warnings.Count - 1]);
            StringAssert.Contains("open 7 day(s)", sink.Warnings[sink.Warnings.Count - 1]);
            StringAssert.Contains("system Test", sink.Warnings[sink.Warnings.Count - 1]);
            StringAssert.Contains("recovery TestDriver", sink.Warnings[sink.Warnings.Count - 1]);
            // The sweep records no occurrence: dedup counts survive it.
            Assert.AreEqual(1, incident.OccurrenceCount);
            Assert.AreEqual(1, authority.TotalOccurrences);
        }

        [Test]
        public void EscalationSweep_DoesNotEscalateSixDayOldIncident()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            authority.Emit(Report("CI-TEST-SWEEP-6", IncidentSeverity.Warning, 0));
            int warningsBeforeSweep = sink.Warnings.Count;

            IReadOnlyList<string> escalated = authority.EvaluateEscalationSweep(6, 6 * 86400d);

            Assert.AreEqual(0, escalated.Count);
            Assert.AreEqual(IncidentStatus.Open, authority.TryGetIncident("CI-TEST-SWEEP-6").Status);
            Assert.AreEqual(warningsBeforeSweep, sink.Warnings.Count);
        }

        [Test]
        public void EscalationSweep_DoesNotReEscalateOrTouchTerminalIncidents()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            authority.Emit(Report("CI-TEST-SWEEP-RE", IncidentSeverity.Warning, 0));
            authority.Emit(Report("CI-TEST-SWEEP-RESOLVED", IncidentSeverity.Warning, 0));
            authority.Emit(Report("CI-TEST-SWEEP-CLOSED", IncidentSeverity.Warning, 0));
            authority.TryGetIncident("CI-TEST-SWEEP-RESOLVED")
                .ChangeStatus(IncidentStatus.Resolved, 3, 3 * 86400d, "test resolve");
            authority.CloseIncident("CI-TEST-SWEEP-CLOSED", 3, 3 * 86400d, "fixed");

            IReadOnlyList<string> first = authority.EvaluateEscalationSweep(7, 7 * 86400d);
            Assert.AreEqual(1, first.Count);
            Assert.AreEqual("CI-TEST-SWEEP-RE", first[0]);

            IReadOnlyList<string> second = authority.EvaluateEscalationSweep(14, 14 * 86400d);
            Assert.AreEqual(0, second.Count);
            Assert.AreEqual(IncidentStatus.Escalated, authority.TryGetIncident("CI-TEST-SWEEP-RE").Status);
            Assert.AreEqual(IncidentStatus.Resolved, authority.TryGetIncident("CI-TEST-SWEEP-RESOLVED").Status);
            Assert.AreEqual(IncidentStatus.Closed, authority.TryGetIncident("CI-TEST-SWEEP-CLOSED").Status);
        }

        [Test]
        public void EscalationHorizonThresholdChange_TakesEffect()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            authority.Emit(Report("CI-TEST-HORIZON", IncidentSeverity.Warning, 0));

            authority.Settings.EscalationHorizonDays = 3;
            Assert.AreEqual(0, authority.EvaluateEscalationSweep(2, 2 * 86400d).Count);
            Assert.AreEqual(1, authority.EvaluateEscalationSweep(3, 3 * 86400d).Count);

            // A longer horizon holds a 7-day-old incident back.
            var secondSink = new CapturingSink();
            IncidentWarningAuthority second = secondSink.CreateAuthority();
            second.Emit(Report("CI-TEST-HORIZON-10", IncidentSeverity.Warning, 0));
            second.Settings.EscalationHorizonDays = 10;
            Assert.AreEqual(0, second.EvaluateEscalationSweep(7, 7 * 86400d).Count);
            Assert.AreEqual(IncidentStatus.Open, second.TryGetIncident("CI-TEST-HORIZON-10").Status);
        }

        [Test]
        public void WarningCapThresholdChange_TakesEffect()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            authority.Settings.MaxWarningsPerDay = 1;

            authority.Emit(Report("CI-TEST-CAP-A", IncidentSeverity.Warning, 5));
            authority.Emit(Report("CI-TEST-CAP-B", IncidentSeverity.Warning, 5));

            Assert.AreEqual(1, sink.Warnings.Count);
            Assert.AreEqual(1, authority.OmittedWarningsToday);
            Assert.AreEqual(2, authority.TotalOccurrences);
            // The omitted signature is named by the daily summary, not silently dropped.
            string summary = authority.EmitDailySummary(5);
            StringAssert.Contains("1 omitted by cap", summary);
            StringAssert.Contains("CI-TEST-CAP-B", sink.Warnings[sink.Warnings.Count - 1]);
        }

        [Test]
        public void RetentionBoundThresholdChange_TakesEffect()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            authority.Settings.MaxRetainedIncidents = 1;

            authority.Emit(Report("CI-TEST-BOUND-A", IncidentSeverity.Warning, 5));
            SimulationIncident dropped = authority.Emit(Report("CI-TEST-BOUND-B", IncidentSeverity.Warning, 5));

            Assert.IsNull(dropped);
            Assert.AreEqual(1, authority.RetainedCount);
            Assert.AreEqual(1, authority.DroppedIncidentCount);
            Assert.IsNotNull(authority.TryGetIncident("CI-TEST-BOUND-A"));
            Assert.IsNull(authority.TryGetIncident("CI-TEST-BOUND-B"));
        }

        [Test]
        public void DedupCountsSurviveSweepAndClose()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            authority.Emit(Report("CI-TEST-DEDUP-SURVIVES", IncidentSeverity.Warning, 0));
            authority.Emit(Report("CI-TEST-DEDUP-SURVIVES", IncidentSeverity.Warning, 1));
            authority.Emit(Report("CI-TEST-DEDUP-SURVIVES", IncidentSeverity.Warning, 7));

            authority.EvaluateEscalationSweep(7, 7 * 86400d);
            Assert.IsTrue(authority.CloseIncident("CI-TEST-DEDUP-SURVIVES", 7, 7 * 86400d, "fixed"));

            SimulationIncident incident = authority.TryGetIncident("CI-TEST-DEDUP-SURVIVES");
            Assert.AreEqual(3, incident.OccurrenceCount);
            Assert.AreEqual(1, incident.OccurrencesOnDay(0));
            Assert.AreEqual(1, incident.OccurrencesOnDay(1));
            Assert.AreEqual(1, incident.OccurrencesOnDay(7));
            Assert.AreEqual(3, authority.TotalOccurrences);
            Assert.AreEqual(IncidentStatus.Closed, incident.Status);
        }

        [Test]
        public void CloseIncident_EmitsClosedLine_AndIsIdempotent()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            authority.Emit(Report("CI-TEST-CLOSE", IncidentSeverity.Warning, 4));

            Assert.IsTrue(authority.CloseIncident("CI-TEST-CLOSE", 5, 5 * 86400d, "explained-legitimate"));
            Assert.AreEqual(IncidentStatus.Closed, authority.TryGetIncident("CI-TEST-CLOSE").Status);
            StringAssert.Contains("[CLOSED]", sink.Warnings[sink.Warnings.Count - 1]);
            StringAssert.Contains("CI-TEST-CLOSE", sink.Warnings[sink.Warnings.Count - 1]);
            StringAssert.Contains("explained-legitimate", sink.Warnings[sink.Warnings.Count - 1]);

            int warningsAfterClose = sink.Warnings.Count;
            Assert.IsFalse(authority.CloseIncident("CI-TEST-CLOSE", 6, 6 * 86400d, "fixed"));
            Assert.AreEqual(warningsAfterClose, sink.Warnings.Count);
            Assert.IsFalse(authority.CloseIncident("CI-TEST-NOPE", 6, 6 * 86400d, "fixed"));
        }

        [Test]
        public void DriveDailyHealth_RunsSweepOnSharedAuthority()
        {
            IncidentWarningAuthority shared = IncidentDiagnostics.SharedAuthority;
            var warnings = new List<string>();
            shared.LogSink = line => warnings.Add(line);
            shared.InfoSink = line => { };
            shared.Emit(Report("CI-TEST-DAILY-HOOK", IncidentSeverity.Warning, 0, "standing test incident"));

            IReadOnlyList<string> escalated = IncidentDiagnostics.DriveDailyHealth(7, 7 * 86400d);

            Assert.AreEqual(1, escalated.Count);
            Assert.AreEqual("CI-TEST-DAILY-HOOK", escalated[0]);
            Assert.AreEqual(IncidentStatus.Escalated, shared.TryGetIncident("CI-TEST-DAILY-HOOK").Status);
            // The hook never throws on a bad day index either.
            Assert.AreEqual(0, IncidentDiagnostics.DriveDailyHealth(-1, -1).Count);
        }

        [Test]
        public void Filters_FilterBySeveritySubsystemEntityCauseAndText()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            authority.Emit(Report("CI-TEST-FILTER-A", IncidentSeverity.Warning, 0, "bakery batch blocked", "Bakery"));
            authority.Emit(Report("CI-TEST-FILTER-B", IncidentSeverity.Critical, 0, "custody moved unpaid", "Freight"));
            IncidentReport withTask = Report("CI-TEST-FILTER-C", IncidentSeverity.Information, 0, "worker idle", "Bakery");
            withTask.TaskId = "T9";
            withTask.CauseClass = RefusalCauseClass.DestinationMissing;
            authority.Emit(withTask);

            HealthControlCenterSnapshot snapshot = SimulationHealthControlCenter.BuildSnapshot(authority, 0, null, new TraceCompleteness());
            Assert.AreEqual(3, snapshot.Rows.Count);
            Assert.AreEqual(3, snapshot.TotalOccurrences);

            Assert.AreEqual(1, SimulationHealthControlCenter.ApplyFilters(snapshot.Rows,
                new HealthControlCenterFilters { Severity = IncidentSeverity.Critical }).Count);
            Assert.AreEqual(2, SimulationHealthControlCenter.ApplyFilters(snapshot.Rows,
                new HealthControlCenterFilters { OwningSystem = "bakery" }).Count);
            Assert.AreEqual(1, SimulationHealthControlCenter.ApplyFilters(snapshot.Rows,
                new HealthControlCenterFilters { EntityId = "T9" }).Count);
            Assert.AreEqual(1, SimulationHealthControlCenter.ApplyFilters(snapshot.Rows,
                new HealthControlCenterFilters { CauseClass = RefusalCauseClass.DestinationMissing }).Count);
            Assert.AreEqual(1, SimulationHealthControlCenter.ApplyFilters(snapshot.Rows,
                new HealthControlCenterFilters { Text = "custody" }).Count);
            Assert.AreEqual(0, SimulationHealthControlCenter.ApplyFilters(snapshot.Rows,
                new HealthControlCenterFilters { Text = "no-such-text" }).Count);
            Assert.AreEqual(3, SimulationHealthControlCenter.ApplyFilters(snapshot.Rows, null).Count);
        }

        [Test]
        public void SnapshotDiff_ShowsPerSignatureDecreaseNewAndRemoved()
        {
            var beforeSink = new CapturingSink();
            IncidentWarningAuthority beforeAuthority = beforeSink.CreateAuthority();
            beforeAuthority.Emit(Report("CI-TEST-DIFF-DOWN", IncidentSeverity.Warning, 0));
            beforeAuthority.Emit(Report("CI-TEST-DIFF-DOWN", IncidentSeverity.Warning, 0));
            beforeAuthority.Emit(Report("CI-TEST-DIFF-DOWN", IncidentSeverity.Warning, 0));
            beforeAuthority.Emit(Report("CI-TEST-DIFF-GONE", IncidentSeverity.Warning, 0));
            beforeAuthority.Emit(Report("CI-TEST-DIFF-SAME", IncidentSeverity.Warning, 0));
            HealthOccurrenceSnapshot before = SimulationHealthControlCenter.CaptureOccurrences(beforeAuthority, 0);

            var afterSink = new CapturingSink();
            IncidentWarningAuthority afterAuthority = afterSink.CreateAuthority();
            afterAuthority.Emit(Report("CI-TEST-DIFF-DOWN", IncidentSeverity.Warning, 0));
            afterAuthority.Emit(Report("CI-TEST-DIFF-SAME", IncidentSeverity.Warning, 0));
            afterAuthority.Emit(Report("CI-TEST-DIFF-NEW", IncidentSeverity.Warning, 0));
            HealthOccurrenceSnapshot after = SimulationHealthControlCenter.CaptureOccurrences(afterAuthority, 1);

            List<HealthSignatureDelta> deltas = SimulationHealthControlCenter.DiffOccurrences(before, after);
            Assert.AreEqual(4, deltas.Count);

            HealthSignatureDelta down = FindDelta(deltas, "CI-TEST-DIFF-DOWN");
            Assert.AreEqual(HealthSignatureChangeKind.Decreased, down.Kind);
            Assert.AreEqual(3, down.BeforeOccurrences);
            Assert.AreEqual(1, down.AfterOccurrences);
            Assert.AreEqual(-2, down.Delta);

            HealthSignatureDelta gone = FindDelta(deltas, "CI-TEST-DIFF-GONE");
            Assert.AreEqual(HealthSignatureChangeKind.Removed, gone.Kind);
            Assert.AreEqual(1, gone.BeforeOccurrences);
            Assert.AreEqual(0, gone.AfterOccurrences);

            HealthSignatureDelta same = FindDelta(deltas, "CI-TEST-DIFF-SAME");
            Assert.AreEqual(HealthSignatureChangeKind.Unchanged, same.Kind);

            HealthSignatureDelta added = FindDelta(deltas, "CI-TEST-DIFF-NEW");
            Assert.AreEqual(HealthSignatureChangeKind.New, added.Kind);
            Assert.AreEqual(0, added.BeforeOccurrences);
            Assert.AreEqual(1, added.AfterOccurrences);
        }

        [Test]
        public void OccurrenceSnapshot_RoundTrip()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            authority.Emit(Report("CI-TEST-ROUNDTRIP-A", IncidentSeverity.Warning, 2));
            authority.Emit(Report("CI-TEST-ROUNDTRIP-A", IncidentSeverity.Warning, 2));
            authority.Emit(Report("CI-TEST-ROUNDTRIP-B", IncidentSeverity.Critical, 2));

            HealthOccurrenceSnapshot captured = SimulationHealthControlCenter.CaptureOccurrences(authority, 2);
            string text = SimulationHealthControlCenter.WriteOccurrenceSnapshot(captured);

            HealthOccurrenceSnapshot parsed;
            Assert.IsTrue(SimulationHealthControlCenter.TryParseOccurrenceSnapshot(text, out parsed));
            Assert.AreEqual(2, parsed.DayIndex);
            Assert.AreEqual(2, parsed.RetainedCount);
            Assert.AreEqual(3, parsed.TotalOccurrences);
            Assert.AreEqual(2, parsed.OccurrencesBySignature["CI-TEST-ROUNDTRIP-A"]);
            Assert.AreEqual(1, parsed.OccurrencesBySignature["CI-TEST-ROUNDTRIP-B"]);
            Assert.AreEqual("WARNING", parsed.SeverityBySignature["CI-TEST-ROUNDTRIP-A"]);
            Assert.AreEqual("CRITICAL", parsed.SeverityBySignature["CI-TEST-ROUNDTRIP-B"]);

            HealthOccurrenceSnapshot bad;
            Assert.IsFalse(SimulationHealthControlCenter.TryParseOccurrenceSnapshot("not a snapshot", out bad));
            Assert.IsNull(bad);
            Assert.IsFalse(SimulationHealthControlCenter.TryParseOccurrenceSnapshot(string.Empty, out bad));
        }

        [Test]
        public void Register_SetAndGetStatus_DefaultsToOpen()
        {
            var register = new IssueRegister();
            Assert.AreEqual(IssueRegisterStatus.Open, register.StatusOf("CI-TEST-REG-UNKNOWN"));
            Assert.IsNull(register.TryGetEntry("CI-TEST-REG-UNKNOWN"));

            IssueRegisterEntry entry = register.SetStatus("CI-TEST-REG-KNOWN", IssueRegisterStatus.ImplementedUnverified,
                "Bakery", "verify on next run", "bakery-audit:day-41");
            Assert.AreEqual(IssueRegisterStatus.ImplementedUnverified, entry.Status);
            Assert.AreEqual("Bakery", entry.OwningAuthority);
            Assert.AreEqual("verify on next run", entry.NextAction);
            Assert.AreEqual(IssueRegisterStatus.ImplementedUnverified, register.StatusOf("CI-TEST-REG-KNOWN"));
            Assert.AreEqual(1, register.Entries.Count);

            // The snapshot carries the register status onto the row.
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            authority.Emit(Report("CI-TEST-REG-KNOWN", IncidentSeverity.Warning, 0));
            authority.Emit(Report("CI-TEST-REG-UNKNOWN", IncidentSeverity.Warning, 0));
            HealthControlCenterSnapshot snapshot = SimulationHealthControlCenter.BuildSnapshot(authority, 0, register, new TraceCompleteness());
            Assert.AreEqual(IssueRegisterStatus.ImplementedUnverified, FindRow(snapshot, "CI-TEST-REG-KNOWN").RegisterStatus);
            Assert.IsTrue(FindRow(snapshot, "CI-TEST-REG-KNOWN").IsRegistered);
            Assert.AreEqual(IssueRegisterStatus.Open, FindRow(snapshot, "CI-TEST-REG-UNKNOWN").RegisterStatus);
            Assert.IsFalse(FindRow(snapshot, "CI-TEST-REG-UNKNOWN").IsRegistered);

            // Register-status filtering actually filters.
            Assert.AreEqual(1, SimulationHealthControlCenter.ApplyFilters(snapshot.Rows,
                new HealthControlCenterFilters { RegisterStatus = IssueRegisterStatus.ImplementedUnverified }).Count);
            Assert.AreEqual(1, SimulationHealthControlCenter.ApplyFilters(snapshot.Rows,
                new HealthControlCenterFilters { RegisterStatus = IssueRegisterStatus.Open }).Count);
        }

        [Test]
        public void ChecklistBaseline_MatchesVerifiedTally()
        {
            // G R1 CONFIRMED: 354 rows, 354 distinct CI ids, occurrence sum 645.
            Assert.AreEqual(354, ChecklistBaseline.Signatures);
            Assert.AreEqual(645, ChecklistBaseline.Occurrences);
            Assert.AreEqual(287, ChecklistBaseline.Open);
            Assert.AreEqual(58, ChecklistBaseline.ImplementedUnverified);
            Assert.AreEqual(6, ChecklistBaseline.ExplainedLegitimate);
            Assert.AreEqual(2, ChecklistBaseline.BlockedWithReason);
            Assert.AreEqual(1, ChecklistBaseline.Investigating);
        }

        private static HealthSignatureDelta FindDelta(List<HealthSignatureDelta> deltas, string signatureId)
        {
            foreach (HealthSignatureDelta delta in deltas)
            {
                if (delta.SignatureId == signatureId)
                {
                    return delta;
                }
            }

            Assert.IsTrue(false, "No delta for " + signatureId);
            return null;
        }

        private static HealthIncidentRow FindRow(HealthControlCenterSnapshot snapshot, string signatureId)
        {
            foreach (HealthIncidentRow row in snapshot.Rows)
            {
                if (row.SignatureId == signatureId)
                {
                    return row;
                }
            }

            Assert.IsTrue(false, "No row for " + signatureId);
            return null;
        }
    }
}
