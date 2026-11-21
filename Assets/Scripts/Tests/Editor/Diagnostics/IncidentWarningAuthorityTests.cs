using System;
using System.Collections.Generic;
using System.IO;
using LandLedgers.Diagnostics;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Diagnostics
{
    /// <summary>
    /// Lane 1 (§4/§17) — focused tests for the shared incident
    /// infrastructure: enable/disable, bounded retention (drop counters
    /// increment and are reported), dedup (same signature twice in a day =
    /// one line + count 2), classification (each severity level
    /// constructible and distinguishable), stable signature ids, status
    /// history, occurrence counting, and structured export.
    /// Tests only; no production code.
    /// </summary>
    [TestFixture]
    public sealed class IncidentWarningAuthorityTests
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

        private static IncidentReport Report(string signatureId, IncidentSeverity severity, int day, string summary = "test condition")
        {
            return new IncidentReport
            {
                SignatureId = signatureId,
                Severity = severity,
                Summary = summary,
                DayIndex = day,
                SimSeconds = day * 86400d,
                OwningSystem = "Test",
            };
        }

        [Test]
        public void DisabledAuthority_RecordsNothingAndEmitsNothing()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            authority.Enabled = false;

            SimulationIncident incident = authority.Emit(Report("CI-TEST-DISABLED", IncidentSeverity.Warning, 7));

            Assert.IsNull(incident);
            Assert.AreEqual(0, authority.RetainedCount);
            Assert.AreEqual(0, authority.TotalOccurrences);
            Assert.AreEqual(0, sink.Warnings.Count);

            authority.Enabled = true;
            incident = authority.Emit(Report("CI-TEST-DISABLED", IncidentSeverity.Warning, 7));
            Assert.IsNotNull(incident);
            Assert.AreEqual(1, authority.RetainedCount);
            Assert.AreEqual(1, sink.Warnings.Count);
        }

        [Test]
        public void SameSignatureTwiceInADay_IsOneLineWithCountTwo()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();

            authority.Emit(Report("CI-TEST-DEDUP", IncidentSeverity.Warning, 7, "refusal repeats"));
            authority.Emit(Report("CI-TEST-DEDUP", IncidentSeverity.Warning, 7, "refusal repeats"));

            Assert.AreEqual(1, sink.Warnings.Count);
            Assert.AreEqual(1, authority.RetainedCount);
            Assert.AreEqual(2, authority.TotalOccurrences);
            Assert.AreEqual(2, authority.OccurrencesOnDay("CI-TEST-DEDUP", 7));
            // The one line is emitted on the first occurrence (count 1 at that
            // moment); the second occurrence is counted, not re-emitted.
            // The count-2 total is visible on the incident, the daily summary
            // and the structured export — not as a second Console line.
            StringAssert.Contains("CI-TEST-DEDUP", sink.Warnings[0]);
            StringAssert.Contains("day 7", sink.Warnings[0]);
            Assert.AreEqual(2, authority.TryGetIncident("CI-TEST-DEDUP").OccurrenceCount);
        }

        [Test]
        public void SameSignatureNextDay_EmitsANewLine()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();

            authority.Emit(Report("CI-TEST-DEDUP-DAY", IncidentSeverity.Warning, 7));
            authority.Emit(Report("CI-TEST-DEDUP-DAY", IncidentSeverity.Warning, 7));
            authority.Emit(Report("CI-TEST-DEDUP-DAY", IncidentSeverity.Warning, 8));

            Assert.AreEqual(2, sink.Warnings.Count);
            Assert.AreEqual(2, authority.OccurrencesOnDay("CI-TEST-DEDUP-DAY", 7));
            Assert.AreEqual(1, authority.OccurrencesOnDay("CI-TEST-DEDUP-DAY", 8));
            Assert.AreEqual(3, authority.TotalOccurrences);
        }

        [Test]
        public void SessionOnce_EmitsOneLineEver()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();

            authority.EmitSessionOnce(Report("CI-TEST-ONCE", IncidentSeverity.Warning, 7));
            authority.EmitSessionOnce(Report("CI-TEST-ONCE", IncidentSeverity.Warning, 8));
            authority.EmitSessionOnce(Report("CI-TEST-ONCE", IncidentSeverity.Warning, 9));

            Assert.AreEqual(1, sink.Warnings.Count);
            Assert.AreEqual(3, authority.TotalOccurrences);
            Assert.AreEqual(3, authority.TryGetIncident("CI-TEST-ONCE").OccurrenceCount);
        }

        [Test]
        public void EachSeverityLevel_IsConstructibleAndDistinguishable()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();

            authority.Emit(Report("CI-TEST-SEV-INFO", IncidentSeverity.Information, 7, "routine notice"));
            authority.Emit(Report("CI-TEST-SEV-WARN", IncidentSeverity.Warning, 7, "real defect"));
            authority.Emit(Report("CI-TEST-SEV-CRIT", IncidentSeverity.Critical, 7, "integrity violation"));
            authority.Emit(Report("CI-TEST-SEV-UNK", IncidentSeverity.Unknown, 7, "insufficient evidence"));

            Assert.AreEqual(4, authority.RetainedCount);
            Assert.AreEqual(IncidentSeverity.Information, authority.TryGetIncident("CI-TEST-SEV-INFO").Severity);
            Assert.AreEqual(IncidentSeverity.Warning, authority.TryGetIncident("CI-TEST-SEV-WARN").Severity);
            Assert.AreEqual(IncidentSeverity.Critical, authority.TryGetIncident("CI-TEST-SEV-CRIT").Severity);
            Assert.AreEqual(IncidentSeverity.Unknown, authority.TryGetIncident("CI-TEST-SEV-UNK").Severity);
            Assert.AreEqual("INFORMATION", IncidentSeverityText.Label(IncidentSeverity.Information));
            Assert.AreEqual("WARNING", IncidentSeverityText.Label(IncidentSeverity.Warning));
            Assert.AreEqual("CRITICAL", IncidentSeverityText.Label(IncidentSeverity.Critical));
            Assert.AreEqual("UNKNOWN", IncidentSeverityText.Label(IncidentSeverity.Unknown));
            StringAssert.Contains("INFORMATION", sink.Warnings[0]);
            StringAssert.Contains("WARNING", sink.Warnings[1]);
            StringAssert.Contains("CRITICAL", sink.Warnings[2]);
            StringAssert.Contains("UNKNOWN", sink.Warnings[3]);
        }

        [Test]
        public void SeverityUpgradeSameDay_EmitsEscalatedLine()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();

            authority.Emit(Report("CI-TEST-ESCALATE", IncidentSeverity.Warning, 7, "batch blocked"));
            authority.Emit(Report("CI-TEST-ESCALATE", IncidentSeverity.Critical, 7, "batch blocked"));

            Assert.AreEqual(2, sink.Warnings.Count);
            StringAssert.Contains("[ESCALATED]", sink.Warnings[1]);
            Assert.AreEqual(IncidentSeverity.Critical, authority.TryGetIncident("CI-TEST-ESCALATE").Severity);
            Assert.AreEqual(IncidentSeverity.Warning, authority.TryGetIncident("CI-TEST-ESCALATE").FirstSeverity);
        }

        [Test]
        public void CauseChangeSameDay_EmitsCauseChangedLine()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();

            var first = Report("CI-TEST-CAUSE", IncidentSeverity.Warning, 7, "route refused");
            first.CauseClass = RefusalCauseClass.DestinationMissing;
            authority.Emit(first);

            var second = Report("CI-TEST-CAUSE", IncidentSeverity.Warning, 7, "route refused");
            second.CauseClass = RefusalCauseClass.OriginInvalid;
            authority.Emit(second);

            Assert.AreEqual(2, sink.Warnings.Count);
            StringAssert.Contains("[CAUSE-CHANGED]", sink.Warnings[1]);
            Assert.AreEqual(RefusalCauseClass.OriginInvalid, authority.TryGetIncident("CI-TEST-CAUSE").CauseClass);
        }

        [Test]
        public void EachCauseClass_IsConstructibleAndDistinguishable()
        {
            var classes = new[]
            {
                RefusalCauseClass.Unclassified,
                RefusalCauseClass.DestinationMissing,
                RefusalCauseClass.DestinationInaccessible,
                RefusalCauseClass.DestinationOccupiedOrReserved,
                RefusalCauseClass.WorkerCannotPerform,
                RefusalCauseClass.OtherObligationTakesPriority,
                RefusalCauseClass.OriginInvalid,
            };
            var labels = new HashSet<string>();
            foreach (RefusalCauseClass causeClass in classes)
            {
                Assert.IsTrue(labels.Add(RefusalCauseClassifier.Label(causeClass)),
                    "Duplicate label for " + causeClass);
            }

            Assert.AreEqual(7, labels.Count);
        }

        [Test]
        public void Classifier_MapsKnownRefusalStrings()
        {
            Assert.AreEqual(RefusalCauseClass.OriginInvalid,
                RefusalCauseClassifier.Classify("Pathing request failed (StartNotWalkable): Point is outside the authored walkable floor area on floor 0."));
            Assert.AreEqual(RefusalCauseClass.OriginInvalid,
                RefusalCauseClassifier.Classify("Trip trip-9-174 aborted: No physical route reaches seller location 'building:0'."));
            Assert.AreEqual(RefusalCauseClass.DestinationInaccessible,
                RefusalCauseClassifier.Classify("Target cell (12, 7) is not walkable."));
            Assert.AreEqual(RefusalCauseClass.DestinationInaccessible,
                RefusalCauseClassifier.Classify("No path found from (1, 2) to (9, 9)."));
            Assert.AreEqual(RefusalCauseClass.DestinationOccupiedOrReserved,
                RefusalCauseClassifier.Classify("Reservation refused: bed-h1-a is already reserved (P3) — no double-booking (Tech X §3.8)."));
            Assert.AreEqual(RefusalCauseClass.DestinationOccupiedOrReserved,
                RefusalCauseClassifier.Classify("Task T44: workstation is already occupied by task T12 (InProgress)."));
            Assert.AreEqual(RefusalCauseClass.OtherObligationTakesPriority,
                RefusalCauseClassifier.Classify("Refused: P7 is already Work [480-960] on day 3 (bakery shift); cannot also do Sleep [1320-1440]. Person time is authoritative — one activity at a time."));
            Assert.AreEqual(RefusalCauseClass.OtherObligationTakesPriority,
                RefusalCauseClassifier.Classify("Domestic activity is deferred while the resident physically approaches an assigned freight wagon."));
            // A malformed request (runs past midnight) is not an obligation conflict.
            Assert.AreEqual(RefusalCauseClass.Unclassified,
                RefusalCauseClassifier.Classify("Refused: P7 Sleep [1320-1440] runs past midnight — split across days instead."));
            Assert.AreEqual(RefusalCauseClass.DestinationMissing,
                RefusalCauseClassifier.Classify("DomesticLaborDriver: home has no authored Firewood location."));
            Assert.AreEqual(RefusalCauseClass.DestinationMissing,
                RefusalCauseClassifier.Classify("firewood: no firewood storage is authored on the home."));
            Assert.AreEqual(RefusalCauseClass.WorkerCannotPerform,
                RefusalCauseClassifier.Classify("No visible on-map presentation"));
            Assert.AreEqual(RefusalCauseClass.Unclassified,
                RefusalCauseClassifier.Classify("unassignable: no workers available"));
            Assert.AreEqual(RefusalCauseClass.Unclassified, RefusalCauseClassifier.Classify("some new refusal nobody has seen"));
            Assert.AreEqual(RefusalCauseClass.Unclassified, RefusalCauseClassifier.Classify(null));
            Assert.AreEqual(RefusalCauseClass.Unclassified, RefusalCauseClassifier.Classify(string.Empty));
        }

        [Test]
        public void ChecklistSignatureIds_PassThroughUnchanged()
        {
            Assert.AreEqual("CI-C98D8F66BE", IncidentSignature.Normalize("CI-C98D8F66BE"));
            Assert.AreEqual("CI-5F80FDC40F", IncidentSignature.Normalize("CI-5F80FDC40F"));
        }

        [Test]
        public void DerivedSignatureIds_AreStableAcrossCalls()
        {
            string first = IncidentSignature.DeriveFromSummary("Pathing", "StartNotWalkable on floor 0");
            string second = IncidentSignature.DeriveFromSummary("Pathing", "StartNotWalkable on floor 0");
            Assert.AreEqual(first, second);
            Assert.IsTrue(first.StartsWith("SIG-", StringComparison.Ordinal));
        }

        [Test]
        public void DedupKey_StripsVolatileIdsAndTimes()
        {
            string first = IncidentSignature.NormalizeDedupKey("task T9 refused day 7 person P3 at 14:32");
            string second = IncidentSignature.NormalizeDedupKey("task T10 refused day 8 person P7 at 09:15");
            Assert.AreEqual(first, second);
            string different = IncidentSignature.NormalizeDedupKey("task T9 completed day 7 person P3");
            Assert.AreNotEqual(first, different);
        }

        [Test]
        public void StatusHistory_RecordsOpenResolveClose()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();

            SimulationIncident incident = authority.Emit(Report("CI-TEST-STATUS", IncidentSeverity.Warning, 7));
            Assert.AreEqual(IncidentStatus.Open, incident.Status);
            Assert.AreEqual(1, incident.StatusHistory.Count);

            incident.ChangeStatus(IncidentStatus.Resolved, 9, 9 * 86400d, "route reachable again");
            incident.ChangeStatus(IncidentStatus.Closed, 9, 9 * 86400d, "verified fixed");
            Assert.AreEqual(IncidentStatus.Closed, incident.Status);
            Assert.AreEqual(3, incident.StatusHistory.Count);
            Assert.AreEqual(IncidentStatus.Open, incident.StatusHistory[0].ToStatus);
            Assert.AreEqual(IncidentStatus.Resolved, incident.StatusHistory[1].ToStatus);
            Assert.AreEqual(IncidentStatus.Closed, incident.StatusHistory[2].ToStatus);

            // A move to the current status with no note is a no-op.
            incident.ChangeStatus(IncidentStatus.Closed, 9, 9 * 86400d, null);
            Assert.AreEqual(3, incident.StatusHistory.Count);
        }

        [Test]
        public void BoundedRetention_EvictsTerminalFirstAndCountsIt()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            authority.Settings = new IncidentAuthoritySettings { MaxRetainedIncidents = 2, MaxWarningsPerDay = 12, MaxWarningsPerSignaturePerDay = 3 };

            authority.Emit(Report("CI-TEST-KEEP-OPEN", IncidentSeverity.Warning, 7));
            SimulationIncident closed = authority.Emit(Report("CI-TEST-EVICT", IncidentSeverity.Warning, 7));
            closed.ChangeStatus(IncidentStatus.Closed, 7, 7 * 86400d, "explained legitimate");

            SimulationIncident third = authority.Emit(Report("CI-TEST-THIRD", IncidentSeverity.Warning, 7));

            Assert.IsNotNull(third);
            Assert.AreEqual(2, authority.RetainedCount);
            Assert.AreEqual(1, authority.EvictedIncidentCount);
            Assert.AreEqual(0, authority.DroppedIncidentCount);
            Assert.IsNull(authority.TryGetIncident("CI-TEST-EVICT"));
            Assert.IsNotNull(authority.TryGetIncident("CI-TEST-KEEP-OPEN"));
            Assert.IsNotNull(authority.TryGetIncident("CI-TEST-THIRD"));
        }

        [Test]
        public void BoundedRetention_DropsNewIncidentWhenOnlyOpenIncidentsRemain()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            authority.Settings = new IncidentAuthoritySettings { MaxRetainedIncidents = 2, MaxWarningsPerDay = 12, MaxWarningsPerSignaturePerDay = 3 };

            authority.Emit(Report("CI-TEST-OPEN-A", IncidentSeverity.Warning, 7));
            authority.Emit(Report("CI-TEST-OPEN-B", IncidentSeverity.Warning, 7));

            SimulationIncident dropped = authority.Emit(Report("CI-TEST-OPEN-C", IncidentSeverity.Warning, 7));

            Assert.IsNull(dropped);
            Assert.AreEqual(2, authority.RetainedCount);
            Assert.AreEqual(1, authority.DroppedIncidentCount);
            Assert.AreEqual(0, authority.EvictedIncidentCount);
        }

        [Test]
        public void DailySummary_CountsOmittedByCapAndNamesThem()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            authority.Settings = new IncidentAuthoritySettings { MaxRetainedIncidents = 512, MaxWarningsPerDay = 1, MaxWarningsPerSignaturePerDay = 3 };

            authority.Emit(Report("CI-TEST-CAP-FIRST", IncidentSeverity.Warning, 7));
            authority.Emit(Report("CI-TEST-CAP-OMITTED", IncidentSeverity.Warning, 7));

            Assert.AreEqual(1, sink.Warnings.Count);
            string summary = authority.EmitDailySummary(7);
            Assert.IsNotNull(summary);
            StringAssert.Contains("1 omitted by cap", summary);
            // Summary line + omitted-identities line = 2 more lines (3 total).
            Assert.AreEqual(3, sink.Warnings.Count);
            StringAssert.Contains("CI-TEST-CAP-OMITTED", sink.Warnings[2]);

            // The summary is emitted at most once per day.
            Assert.IsNull(authority.EmitDailySummary(7));
            Assert.AreEqual(3, sink.Warnings.Count);
        }

        [Test]
        public void DailySummary_ReportsEvictedIdentities()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();
            authority.Settings = new IncidentAuthoritySettings { MaxRetainedIncidents = 1, MaxWarningsPerDay = 12, MaxWarningsPerSignaturePerDay = 3 };

            SimulationIncident first = authority.Emit(Report("CI-TEST-EVICTED", IncidentSeverity.Warning, 7));
            first.ChangeStatus(IncidentStatus.Resolved, 7, 7 * 86400d, "resolved");
            authority.Emit(Report("CI-TEST-REPLACEMENT", IncidentSeverity.Warning, 7));

            Assert.AreEqual(1, authority.EvictedIncidentCount);
            authority.EmitDailySummary(7);
            Assert.AreEqual(1, sink.Info.Count);
            StringAssert.Contains("CI-TEST-EVICTED", sink.Info[0]);
        }

        [Test]
        public void Export_ContainsManifestCountsAndIncidentLines()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();

            var report = Report("CI-C98D8F66BE", IncidentSeverity.Warning, 7, "StartNotWalkable origin failure");
            report.TaskId = "T123";
            report.PersonId = "P7";
            report.HouseholdId = "H4";
            report.CauseClass = RefusalCauseClass.OriginInvalid;
            report.EvidenceReference = "Reports/Playtests/Runs/20261120T192311Z-343a8cd2/console-events.jsonl";
            authority.Emit(report);
            authority.Emit(Report("CI-C98D8F66BE", IncidentSeverity.Warning, 7, "StartNotWalkable origin failure"));

            var json = new StringWriter();
            authority.ExportJsonLines(json);
            string jsonText = json.ToString();
            StringAssert.Contains("\"kind\":\"incident-manifest\"", jsonText);
            StringAssert.Contains("\"retainedIncidents\":1", jsonText);
            StringAssert.Contains("\"totalOccurrences\":2", jsonText);
            StringAssert.Contains("\"signatureId\":\"CI-C98D8F66BE\"", jsonText);
            StringAssert.Contains("\"causeClass\":\"origin-invalid\"", jsonText);
            StringAssert.Contains("\"personId\":\"P7\"", jsonText);
            StringAssert.Contains("\"occurrenceCount\":2", jsonText);

            var summary = new StringWriter();
            authority.ExportSummary(summary);
            string summaryText = summary.ToString();
            StringAssert.Contains("1 retained; 0 dropped; 0 evicted; 2 total occurrences", summaryText);
            StringAssert.Contains("CI-C98D8F66BE", summaryText);
        }

        [Test]
        public void CorrelationIds_FirstAndLastAreKept()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();

            var first = Report("CI-TEST-CORR", IncidentSeverity.Warning, 7, "refusal");
            first.TaskId = "T9";
            first.PersonId = "P3";
            first.HouseholdId = "H1";
            authority.Emit(first);

            var second = Report("CI-TEST-CORR", IncidentSeverity.Warning, 8, "refusal");
            second.TaskId = "T10";
            second.PersonId = "P7";
            second.HouseholdId = "H4";
            authority.Emit(second);

            SimulationIncident incident = authority.TryGetIncident("CI-TEST-CORR");
            Assert.AreEqual("T9", incident.FirstTaskId);
            Assert.AreEqual("T10", incident.LastTaskId);
            Assert.AreEqual("P3", incident.FirstPersonId);
            Assert.AreEqual("P7", incident.LastPersonId);
            Assert.AreEqual("H1", incident.FirstHouseholdId);
            Assert.AreEqual("H4", incident.LastHouseholdId);
            Assert.AreEqual(7, incident.FirstSeenDay);
            Assert.AreEqual(8, incident.LastSeenDay);
            Assert.AreEqual(2, incident.OccurrenceCount);
        }

        [Test]
        public void MissingSignatureId_DerivesStableIdFromSummary()
        {
            var sink = new CapturingSink();
            IncidentWarningAuthority authority = sink.CreateAuthority();

            var report = Report(null, IncidentSeverity.Warning, 7, "StartNotWalkable on floor 0");
            SimulationIncident incident = authority.Emit(report);
            Assert.IsNotNull(incident);
            Assert.IsTrue(incident.SignatureId.StartsWith("SIG-", StringComparison.Ordinal));

            var second = Report(string.Empty, IncidentSeverity.Warning, 7, "StartNotWalkable on floor 0");
            SimulationIncident same = authority.Emit(second);
            Assert.AreSame(incident, same);
        }

        [Test]
        public void TaskDurationRecord_TagsRefusalCauseClass()
        {
            var authority = new TaskAuthority();
            string reason;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition("test.sweep", "Sweep", 10), out reason), reason);
            WorkTask task = authority.CreateTask("test.sweep", EntityId.For(EntityKind.Business, 1), 2);
            var diagnostics = new TaskDurationDiagnostics();
            diagnostics.OnTransition(task, "CreateTask", 2 * 86400f, 2);

            diagnostics.RecordRefusal(task.TaskId.ToString(), "StartTask",
                "Pathing request failed (StartNotWalkable): Point is outside the authored walkable floor area on floor 0.",
                2, RefusalCauseClass.OriginInvalid);

            TaskTimingRecord record = diagnostics.GetRecord(task.TaskId.ToString());
            Assert.IsNotNull(record);
            Assert.AreEqual(RefusalCauseClass.OriginInvalid, record.LastRefusalCauseClass);
            StringAssert.Contains("StartNotWalkable", record.LastRefusal);

            // The 4-parameter overload leaves the class Unclassified.
            diagnostics.RecordRefusal(task.TaskId.ToString(), "StartTask", "another refusal", 2);
            Assert.AreEqual(RefusalCauseClass.Unclassified, record.LastRefusalCauseClass);
        }
    }
}
