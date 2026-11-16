using System;
using System.IO;
using System.Text;
using LandLedgers.Economy.Freight;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>CAMPAIGN-B (DEF-007), package 5: the freight meeting gate.
    ///
    /// VERDICT — the gate is INTENTIONAL, the silence was the defect.
    /// The Nov 13 design decision settles that new agreements require the
    /// people to physically meet (handoff §14.7; "management menus do not
    /// create agreements"), and the register entry records that the work is
    /// making the meeting reliably happen (scheduling, windows, retry), not
    /// removing the gate. The pipeline already retries daily at every stage:
    /// DriveMillbrookFreightNegotiation re-creates Glen's contact travel
    /// every day, StartQueuedFreightMeetingTasks retries every AtMeeting
    /// request every tick, refused requests reopen while the lot is still
    /// shippable, and meeting-completion refusals defer transiently and
    /// retry. What was broken was diagnostics: TryScheduleFreightMeeting
    /// returned SILENTLY when one of the two people was not physically at
    /// the staging building, and again when no common window remained
    /// today — so a meeting that never schedules produced nothing after
    /// Glen's arrival log (Check 10 §5: "mostly silent after day 1").
    /// Both returns now log once per day per request through the existing
    /// day-keyed refusal logger, naming the reason.
    ///
    /// What the Roslyn harness can reach: the stall wording (pure helper)
    /// and the wiring (structural reads of SimulationDrivers.cs — the
    /// driver is too large a closure for the focused harness, same class
    /// as the package-2 resolver wiring). The live meeting itself still
    /// needs a Unity run (Codex): whether Glen and Ethan actually align
    /// in the dev scenario is a runtime question this harness cannot
    /// answer.</summary>
    [TestFixture]
    public sealed class CampaignBFreightMeetingGateTests
    {
        private const string DriversPath =
            "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";

        // ---------- (1) Stall wording ----------

        [Test]
        public void BothPresent_ReturnsNull()
        {
            Assert.IsNull(FreightMeetingStallReason.DescribePresenceStall(
                "freight-request:lot-1", 3, 5, 12, true, true));
        }

        [Test]
        public void RequesterAbsent_NamesGlenAndBuilding()
        {
            string reason = FreightMeetingStallReason.DescribePresenceStall(
                "freight-request:lot-1", 3, 5, 12, false, true);
            Assert.IsTrue(reason != null, "Expected a stall reason, got null.");
            Assert.IsTrue(reason.Contains("freight-request:lot-1"), reason);
            Assert.IsTrue(reason.Contains("Glen P3"), reason);
            Assert.IsTrue(reason.Contains("12"), reason);
            Assert.IsFalse(reason.Contains("Ethan P5 is not"), reason);
        }

        [Test]
        public void CarrierAbsent_NamesEthanAndBuilding()
        {
            string reason = FreightMeetingStallReason.DescribePresenceStall(
                "freight-request:lot-1", 3, 5, 12, true, false);
            Assert.IsTrue(reason != null, "Expected a stall reason, got null.");
            Assert.IsTrue(reason.Contains("freight-request:lot-1"), reason);
            Assert.IsTrue(reason.Contains("Ethan P5"), reason);
            Assert.IsTrue(reason.Contains("12"), reason);
            Assert.IsFalse(reason.Contains("Glen P3 is not"), reason);
        }

        [Test]
        public void BothAbsent_NamesBothPeople()
        {
            string reason = FreightMeetingStallReason.DescribePresenceStall(
                "freight-request:lot-1", 3, 5, 12, false, false);
            Assert.IsTrue(reason != null, "Expected a stall reason, got null.");
            Assert.IsTrue(reason.Contains("Glen P3"), reason);
            Assert.IsTrue(reason.Contains("Ethan P5"), reason);
        }

        [Test]
        public void NoWindow_NamesBothPeopleAndMinutes()
        {
            string reason = FreightMeetingStallReason.DescribeNoWindowStall(
                "freight-request:lot-1", 3, 5, 900, 5);
            Assert.IsTrue(reason.Contains("freight-request:lot-1"), reason);
            Assert.IsTrue(reason.Contains("Glen P3"), reason);
            Assert.IsTrue(reason.Contains("Ethan P5"), reason);
            Assert.IsTrue(reason.Contains("5-minute"), reason);
            Assert.IsTrue(reason.Contains("900"), reason);
        }

        // ---------- (2) Structural: the two silent returns now log ----------

        [Test]
        public void PresenceStall_IsLoggedDayKeyedInTryScheduleFreightMeeting()
        {
            string body = MethodBody(ReadRepoFile(DriversPath),
                "private void TryScheduleFreightMeeting(FreightServiceRequest request, int dayIndex)");
            Assert.IsTrue(body.Contains("DescribePresenceStall"),
                "TryScheduleFreightMeeting must call the presence-stall helper.");
            Assert.IsTrue(body.Contains("freight-meeting-presence:"),
                "The presence stall must be logged under a per-request day key.");
            Assert.IsTrue(body.Contains("LogRefusalDayKeyed(\"freight-meeting-presence:\" + request.RequestId, dayIndex, presenceStall)"),
                "The presence stall must go through the day-keyed logger, not the once-per-session set.");
        }

        [Test]
        public void NoWindowStall_IsLoggedDayKeyedInTryScheduleFreightMeeting()
        {
            string body = MethodBody(ReadRepoFile(DriversPath),
                "private void TryScheduleFreightMeeting(FreightServiceRequest request, int dayIndex)");
            Assert.IsTrue(body.Contains("DescribeNoWindowStall"),
                "TryScheduleFreightMeeting must call the no-window-stall helper.");
            Assert.IsTrue(body.Contains("freight-meeting-window:"),
                "The no-window stall must be logged under a per-request day key.");
            Assert.IsTrue(body.Contains("LogRefusalDayKeyed(\"freight-meeting-window:\" + request.RequestId, dayIndex,"),
                "The no-window stall must go through the day-keyed logger, not the once-per-session set.");
        }

        // ---------- (3) Structural: the gate and the retry are intact ----------

        [Test]
        public void GateIsIntentional_AtMeetingStillFlowsThroughNegotiationDriver()
        {
            // The early return in DriveMillbrookFreightNegotiation skips
            // Accepted / Refused / MeetingScheduled — AtMeeting is NOT in
            // that list, so an AtMeeting request still flows through to
            // Glen's contact-travel (re)creation every day. If AtMeeting
            // were added to the early return, a stalled meeting would never
            // re-send Glen and the daily retry would die.
            string body = MethodBody(ReadRepoFile(DriversPath),
                "private void DriveMillbrookFreightNegotiation(int dayIndex)");
            string code = StripNonCode(body);
            Assert.IsTrue(code.Contains("FreightServiceRequestStatus.MeetingScheduled) return;"),
                "The negotiation driver must still skip MeetingScheduled requests.");
            Assert.IsFalse(code.Contains("AtMeeting"),
                "AtMeeting must not appear in the negotiation driver — it must flow through to daily travel retry.");
        }

        [Test]
        public void DailyRetry_NegotiationDriverStillRunsEveryDay()
        {
            string source = ReadRepoFile(DriversPath);
            int calls = CountOccurrences(source, "DriveMillbrookFreightNegotiation(absoluteDayIndex)");
            Assert.IsTrue(calls >= 2,
                "DriveMillbrookFreightNegotiation must still be called daily from DriveFreightCompanies "
                + "(and from the no-company early path); found " + calls + " call sites.");
            Assert.IsTrue(source.Contains("StartQueuedFreightMeetingTasks(context.Date.AbsoluteDayIndex,"),
                "AtMeeting requests must still be retried every tick via StartQueuedFreightMeetingTasks.");
        }

        // ---------- repo-file helpers (package-3 / C-4 precedent) ----------

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, DriversPath)))
            {
                return fromEnv;
            }

            var starts = new[]
            {
                System.IO.Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory,
            };
            var tried = new StringBuilder();
            foreach (string start in starts)
            {
                string dir = start;
                while (!string.IsNullOrEmpty(dir))
                {
                    foreach (string candidate in new[]
                    {
                        dir,
                        Path.Combine(dir, "work-b"),
                    })
                    {
                        tried.AppendLine("  " + candidate);
                        if (File.Exists(Path.Combine(candidate, DriversPath)))
                        {
                            return candidate;
                        }
                    }
                    DirectoryInfo parent = System.IO.Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign B (DEF-007): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign B (DEF-007): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }

        private static string MethodBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "Campaign B (DEF-007): method not found in SimulationDrivers.cs: " + signature);
            int next = source.Length;
            foreach (string marker in new[] { "\n        private ", "\n        public ", "\n        protected ", "\n        internal " })
            {
                int at = source.IndexOf(marker, start + signature.Length, StringComparison.Ordinal);
                if (at >= 0 && at < next) next = at;
            }
            return source.Substring(start, next - start);
        }

        /// <summary>Source with comments, string literals and char literals
        /// blanked (newlines preserved), so assertions cannot be fooled by a
        /// mention inside a comment or a log string.</summary>
        private static string StripNonCode(string source)
        {
            var out_ = new StringBuilder(source.Length);
            int i = 0;
            while (i < source.Length)
            {
                char c = source[i];
                char next = i + 1 < source.Length ? source[i + 1] : '\0';
                if (c == '/' && next == '/')
                {
                    while (i < source.Length && source[i] != '\n')
                    {
                        out_.Append(' ');
                        i++;
                    }
                }
                else if (c == '/' && next == '*')
                {
                    out_.Append("  ");
                    i += 2;
                    while (i < source.Length && !(source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/'))
                    {
                        out_.Append(source[i] == '\n' ? '\n' : ' ');
                        i++;
                    }
                    if (i < source.Length)
                    {
                        out_.Append("  ");
                        i += 2;
                    }
                }
                else if (c == '"')
                {
                    out_.Append(' ');
                    i++;
                    while (i < source.Length && source[i] != '"')
                    {
                        if (source[i] == '\\' && i + 1 < source.Length)
                        {
                            out_.Append("  ");
                            i += 2;
                        }
                        else
                        {
                            out_.Append(source[i] == '\n' ? '\n' : ' ');
                            i++;
                        }
                    }
                    if (i < source.Length)
                    {
                        out_.Append(' ');
                        i++;
                    }
                }
                else if (c == '\'')
                {
                    out_.Append(' ');
                    i++;
                    while (i < source.Length && source[i] != '\'')
                    {
                        if (source[i] == '\\' && i + 1 < source.Length)
                        {
                            out_.Append("  ");
                            i += 2;
                        }
                        else
                        {
                            out_.Append(source[i] == '\n' ? '\n' : ' ');
                            i++;
                        }
                    }
                    if (i < source.Length)
                    {
                        out_.Append(' ');
                        i++;
                    }
                }
                else
                {
                    out_.Append(c);
                    i++;
                }
            }
            return out_.ToString();
        }

        private static int CountOccurrences(string source, string needle)
        {
            int count = 0;
            int at = 0;
            while ((at = source.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += needle.Length;
            }
            return count;
        }
    }
}
