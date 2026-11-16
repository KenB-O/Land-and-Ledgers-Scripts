// Campaign C, package 21 — DEF-020 (failed construction procurement
// strands the package; no retry exists in production), Nov 15, 2026.
//
// VERDICT: DIAGNOSTIC SLICE IMPLEMENTED (diagnostic layer only).
// On a procurement failure FundAndStage marks the funding Stranded = true
// and adds the package to blockedProcurementPackages, after which every
// later drive returns "procurement previously failed … retry is blocked
// until AllowProcurementRetry is called" — and no production caller of
// AllowProcurementRetry exists. The strand is durable across save/load
// (DeriveBlockedProcurementPackages rebuilds the block from the persisted
// Stranded markers on every drive). The stall surfaced only as a
// per-project "waits" diagnostic string inside the daily construction
// diag dump, plus the unread BlockedProcurementPackageKeys property —
// no single line ever named every stranded package, how long each had
// been stranded, or why.
//
// This package gives the stall a surface: ConstructionProcurementStrandSummary
// (plain C#, no Unity dependency) remembers the first day each blocked
// key was seen and renders one day-keyed summary line naming every
// stranded package (identity + days stranded + recorded failure reason).
// NpcConstructionDriver calls it once per Drive and adds the line to the
// diag list, which DriveNpcConstruction logs once per day as
// "[SimulationDrivers] npc construction day N: ...". No retry, no
// cancellation, no escalation, no disposition is invented — that is owner
// question (r), and the real fix is sequenced after DEF-004. No UI is
// invented; a diagnostic/summary surface only.
//
// Harness limits (stated honestly): NpcConstructionDriver itself is NOT
// in the harness closure (its closure pulls the commissioner, executor,
// catalog, housing, and cash authorities), so the thin wiring (one
// TryBuildSummaryLine call in Drive plus the StrandedReasonForKey
// resolver) is verified by parse check + structural tests here — the
// C-15/C-17/C-19 precedent. Everything testable without Unity (first-seen
// tracking, day-keyed decision, formatting, deterministic order) is
// tested behaviorally against the helper. Unity-run proof is still owed
// (a failed procurement must show the stranded line in the Console, and
// a live run must show the age growing day over day) — no Unity
// verification is claimed by this package.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LandLedgers.Economy;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class CampaignCProcurementStrandSummaryTests
    {
        private const string DriverPath =
            "Assets/Scripts/Domains/Economy/Businesses/Construction/NpcConstructionDriver.cs";
        private const string DriversPath =
            "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";

        // ---------- DEF-020: helper behaviour ----------

        [Test]
        public void FirstDay_NamesPackageWithZeroDaysAndReason()
        {
            var summary = new ConstructionProcurementStrandSummary();
            bool built = summary.TryBuildSummaryLine(
                new[] { "P1|K1" },
                key => "supplier refused the order",
                5, out string line);
            Assert.IsTrue(built, "Campaign C (DEF-020): one blocked package must build a line on the first day.");
            Assert.IsTrue(line.Contains("procurement-stranded day 5"),
                "Campaign C (DEF-020): the line must carry its day key. Line: " + line);
            Assert.IsTrue(line.Contains("1 package stranded"),
                "Campaign C (DEF-020): the line must carry the stranded count. Line: " + line);
            Assert.IsTrue(line.Contains("project 'P1'") && line.Contains("package 'K1'"),
                "Campaign C (DEF-020): the line must name the stranded package identity. Line: " + line);
            Assert.IsTrue(line.Contains("stranded 0 days"),
                "Campaign C (DEF-020): a package first seen today is stranded 0 days. Line: " + line);
            Assert.IsTrue(line.Contains("(supplier refused the order)"),
                "Campaign C (DEF-020): the line must carry the recorded failure reason. Line: " + line);
            Assert.IsTrue(line.Contains("Retry is blocked until AllowProcurementRetry is called."),
                "Campaign C (DEF-020): the line must say the retry is blocked, not that it will happen. Line: " + line);
        }

        [Test]
        public void LaterDay_ReportsDaysStranded()
        {
            var summary = new ConstructionProcurementStrandSummary();
            Assert.IsTrue(summary.TryBuildSummaryLine(new[] { "P1|K1" }, key => "no lumber", 3, out _),
                "Campaign C (DEF-020): setup — first sighting on day 3 must build.");
            Assert.IsTrue(summary.TryBuildSummaryLine(new[] { "P1|K1" }, key => "no lumber", 10, out string line),
                "Campaign C (DEF-020): day 10 must build again (new day).");
            Assert.IsTrue(line.Contains("stranded 7 days"),
                "Campaign C (DEF-020): day 10 minus first-seen day 3 is 7 days stranded. Line: " + line);
        }

        [Test]
        public void MultiplePackages_AllNamedInOneLine()
        {
            var summary = new ConstructionProcurementStrandSummary();
            bool built = summary.TryBuildSummaryLine(
                new[] { "P1|K1", "P2|K2" },
                key => "supplier refused",
                4, out string line);
            Assert.IsTrue(built, "Campaign C (DEF-020): two blocked packages must build one line.");
            Assert.IsTrue(line.Contains("2 packages stranded"),
                "Campaign C (DEF-020): the line must carry the stranded count. Line: " + line);
            Assert.IsTrue(line.Contains("project 'P1'") && line.Contains("package 'K1'"),
                "Campaign C (DEF-020): the first package must be named. Line: " + line);
            Assert.IsTrue(line.Contains("project 'P2'") && line.Contains("package 'K2'"),
                "Campaign C (DEF-020): the second package must be named. Line: " + line);
        }

        [Test]
        public void NoBlockedPackages_NoLine()
        {
            var summary = new ConstructionProcurementStrandSummary();
            bool built = summary.TryBuildSummaryLine(new string[0], key => "reason", 5, out string line);
            Assert.IsFalse(built, "Campaign C (DEF-020): no blocked packages must build no line.");
            Assert.IsNull(line, "Campaign C (DEF-020): the out line must stay null when nothing is blocked.");
            bool builtNull = summary.TryBuildSummaryLine(null, key => "reason", 5, out string lineNull);
            Assert.IsFalse(builtNull, "Campaign C (DEF-020): a null set must build no line.");
            Assert.IsNull(lineNull, "Campaign C (DEF-020): the out line must stay null for a null set.");
        }

        [Test]
        public void SameDaySecondCall_NoLine()
        {
            var summary = new ConstructionProcurementStrandSummary();
            Assert.IsTrue(summary.TryBuildSummaryLine(new[] { "P1|K1" }, key => "r", 5, out _),
                "Campaign C (DEF-020): setup — first call on day 5 must build.");
            bool second = summary.TryBuildSummaryLine(new[] { "P1|K1" }, key => "r", 5, out string line);
            Assert.IsFalse(second, "Campaign C (DEF-020): the line is day-keyed — a second build on the same day must not emit.");
            Assert.IsNull(line, "Campaign C (DEF-020): the out line must stay null on the suppressed second call.");
        }

        [Test]
        public void NextDay_LineAgain()
        {
            var summary = new ConstructionProcurementStrandSummary();
            Assert.IsTrue(summary.TryBuildSummaryLine(new[] { "P1|K1" }, key => "r", 5, out _),
                "Campaign C (DEF-020): setup — day 5 must build.");
            Assert.IsFalse(summary.TryBuildSummaryLine(new[] { "P1|K1" }, key => "r", 5, out _),
                "Campaign C (DEF-020): setup — day 5 again must not build.");
            Assert.IsTrue(summary.TryBuildSummaryLine(new[] { "P1|K1" }, key => "r", 6, out string line),
                "Campaign C (DEF-020): day 6 must build again — the day key resets per day.");
            Assert.IsTrue(line.Contains("stranded 1 day"),
                "Campaign C (DEF-020): day 6 minus first-seen day 5 is 1 day (singular). Line: " + line);
        }

        [Test]
        public void UnblockedKey_DropsOutAndAgeRestarts()
        {
            var summary = new ConstructionProcurementStrandSummary();
            Assert.IsTrue(summary.TryBuildSummaryLine(new[] { "P1|K1" }, key => "r", 3, out _),
                "Campaign C (DEF-020): setup — day 3 must build.");
            Assert.IsFalse(summary.TryBuildSummaryLine(new string[0], key => "r", 5, out _),
                "Campaign C (DEF-020): setup — an empty set on day 5 must build nothing.");
            bool rebuilt = summary.TryBuildSummaryLine(new[] { "P1|K1" }, key => "r", 8, out string line);
            Assert.IsTrue(rebuilt, "Campaign C (DEF-020): the key blocked again on day 8 must build.");
            Assert.IsTrue(line.Contains("stranded 0 days"),
                "Campaign C (DEF-020): a re-blocked key restarts its age at the new first-seen day. Line: " + line);
        }

        [Test]
        public void EmptyReason_OmitsReasonSegment()
        {
            var summary = new ConstructionProcurementStrandSummary();
            bool built = summary.TryBuildSummaryLine(new[] { "P1|K1" }, key => string.Empty, 5, out string line);
            Assert.IsTrue(built, "Campaign C (DEF-020): a package with no recorded reason must still build a line.");
            Assert.IsFalse(line.Contains("("),
                "Campaign C (DEF-020): an empty reason must leave no dangling reason segment. Line: " + line);
            Assert.IsTrue(line.Contains("project 'P1'") && line.Contains("stranded 0 days"),
                "Campaign C (DEF-020): identity and age must still be present. Line: " + line);
        }

        [Test]
        public void KeyOrder_IsDeterministic()
        {
            var first = new ConstructionProcurementStrandSummary();
            var second = new ConstructionProcurementStrandSummary();
            Assert.IsTrue(first.TryBuildSummaryLine(new[] { "P2|K2", "P1|K1" }, key => "r", 5, out string lineA),
                "Campaign C (DEF-020): setup — first ordering must build.");
            Assert.IsTrue(second.TryBuildSummaryLine(new[] { "P1|K1", "P2|K2" }, key => "r", 5, out string lineB),
                "Campaign C (DEF-020): setup — second ordering must build.");
            Assert.AreEqual(lineA, lineB,
                "Campaign C (DEF-020): the line must be byte-stable regardless of the order keys arrive in.");
        }

        // ---------- DEF-020: driver wiring (structural) ----------

        [Test]
        public void Driver_EmitsStrandSummaryIntoDiagInDrive()
        {
            string code = StripNonCode(ReadRepoFile(DriverPath));
            string drive = MethodBody(code, "public int Drive(int dayIndex, List<string> diag)");
            Assert.IsTrue(code.Contains("private readonly ConstructionProcurementStrandSummary strandSummary = new ConstructionProcurementStrandSummary();"),
                "Campaign C (DEF-020): the driver must own a strand-summary tracker field.");
            Assert.IsTrue(drive.Contains("strandSummary.TryBuildSummaryLine(blockedProcurementPackages, StrandedReasonForKey, dayIndex, out string strandedLine)"),
                "Campaign C (DEF-020): Drive must build the stranded summary from the live blocked set.");
            Assert.IsTrue(drive.Contains("diag.Add(strandedLine)"),
                "Campaign C (DEF-020): the summary line must go into the diag list — the daily dump channel — not a new log surface.");
            int loopAt = drive.IndexOf("if (DriveProject(project, dayIndex, diag)) advanced++;", StringComparison.Ordinal);
            int callAt = drive.IndexOf("strandSummary.TryBuildSummaryLine", StringComparison.Ordinal);
            int returnAt = drive.IndexOf("return advanced;", StringComparison.Ordinal);
            Assert.IsTrue(loopAt >= 0 && callAt > loopAt && returnAt > callAt,
                "Campaign C (DEF-020): the summary must be built after the per-project loop (so a package stranded this drive is included) and before the return.");
        }

        [Test]
        public void Driver_ReasonResolverReadsFundingRecord()
        {
            string code = StripNonCode(ReadRepoFile(DriverPath));
            string resolver = MethodBody(code, "private string StrandedReasonForKey(string key)");
            Assert.IsTrue(resolver.Contains("commissioner.FindRecordForProject"),
                "Campaign C (DEF-020): the reason resolver must read the commissioner's record for the project.");
            Assert.IsTrue(resolver.Contains("record.FindFunding"),
                "Campaign C (DEF-020): the reason resolver must read the funding line for the package.");
            Assert.IsTrue(resolver.Contains("funding.StrandedReason"),
                "Campaign C (DEF-020): the reason resolver must return the recorded StrandedReason.");
        }

        [Test]
        public void Driver_SummaryGoesToDiagNotDebugLog()
        {
            string source = ReadRepoFile(DriverPath);
            Assert.AreEqual(0, CountOccurrences(source, "Debug.Log"),
                "Campaign C (DEF-020): the driver must contain no Debug.Log — the diag list is its only channel (E-22: no Debug.Log exists in the driver). Found "
                + CountOccurrences(source, "Debug.Log") + ".");
            string drivers = ReadRepoFile(DriversPath);
            Assert.IsTrue(drivers.Contains("DriveNpcConstruction(absoluteDayIndex);"),
                "Campaign C (DEF-020): the daily drive must call DriveNpcConstruction (the surface point's caller).");
            Assert.IsTrue(drivers.Contains("[SimulationDrivers] npc construction day "),
                "Campaign C (DEF-020): the daily drive must log the construction diag dump once per day — that is the summary's surface.");
        }

        // ---------- repo-file helpers (CampaignCCoupledTransportDriversCacheTests precedent) ----------

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, DriverPath)))
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
                        Path.Combine(dir, "work-c"),
                    })
                    {
                        tried.AppendLine("  " + candidate);
                        if (File.Exists(Path.Combine(candidate, DriverPath)))
                        {
                            return candidate;
                        }
                    }
                    DirectoryInfo parent = System.IO.Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign C (DEF-020): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign C (DEF-020): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }

        private static string MethodBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "Campaign C (DEF-020): method not found: " + signature);
            int next = source.Length;
            foreach (string marker in new[] { "\n        private ", "\n        public ", "\n        protected ", "\n        internal " })
            {
                int at = source.IndexOf(marker, start + signature.Length, StringComparison.Ordinal);
                if (at >= 0 && at < next) next = at;
            }
            return source.Substring(start, next - start);
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

        /// <summary>
        /// Returns source with comments, string literals and char literals
        /// blanked (newlines preserved), so assertions cannot be fooled by a
        /// mention inside a comment or a log string.
        /// </summary>
        internal static string StripNonCode(string source)
        {
            var outText = new StringBuilder(source.Length);
            int i = 0;
            while (i < source.Length)
            {
                char c = source[i];
                if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    while (i < source.Length && source[i] != '\n') { outText.Append(' '); i++; }
                }
                else if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    outText.Append(' ');
                    outText.Append(' ');
                    i += 2;
                    while (i < source.Length && !(source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/'))
                    {
                        outText.Append(source[i] == '\n' ? '\n' : ' ');
                        i++;
                    }
                    if (i < source.Length) { outText.Append(' '); outText.Append(' '); i += 2; }
                }
                else if (c == '"')
                {
                    outText.Append(' ');
                    i++;
                    while (i < source.Length && source[i] != '"')
                    {
                        if (source[i] == '\\' && i + 1 < source.Length) { outText.Append(' '); i++; outText.Append(' '); i++; }
                        else { outText.Append(source[i] == '\n' ? '\n' : ' '); i++; }
                    }
                    if (i < source.Length) { outText.Append(' '); i++; }
                }
                else if (c == '\'')
                {
                    outText.Append(' ');
                    i++;
                    while (i < source.Length && source[i] != '\'')
                    {
                        if (source[i] == '\\' && i + 1 < source.Length) { outText.Append(' '); i++; outText.Append(' '); i++; }
                        else { outText.Append(source[i] == '\n' ? '\n' : ' '); i++; }
                    }
                    if (i < source.Length) { outText.Append(' '); i++; }
                }
                else
                {
                    outText.Append(c);
                    i++;
                }
            }
            return outText.ToString();
        }
    }
}
