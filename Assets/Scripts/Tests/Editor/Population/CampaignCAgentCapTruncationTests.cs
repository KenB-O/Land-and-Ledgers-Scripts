// Campaign C (DEF-021) — the PopulationPathingDirector agent cap truncates
// silently: the code default is still 14 (scene override 50) and residents
// past the cap never get a visual agent, with no log when the cut happens.
// This package makes the cut visible (count, cap value, resident ids) without
// changing the cap number or any scheduling/pathing behaviour.
//
// Two test kinds, same file:
//  (1) Real unit tests for the pure AgentCapTruncation rule (zero Unity deps).
//  (2) Structural tests pinning the director wiring by reading the production
//      file — PopulationPathingDirector is too large a closure for the focused
//      harness, so the wiring itself (not Unity behaviour) is what is asserted
//      here, exactly like the package-2 guard and package-3 rebind tests.
// The repo root comes from LAND_LEDGERS_REPO, then a walk up from the
// current directory and the test assembly location (campaign layout:
// <root>/work-c/Assets/...), mirroring UnwiredSystemsRegistryTests. A
// missing repo fails loudly, never silently passes.
using System;
using System.IO;
using System.Text;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    [TestFixture]
    public sealed class CampaignCAgentCapTruncationTests
    {
        private const string DirectorPath =
            "Assets/Scripts/Domains/Population/PopulationPathingDirector.cs";

        // ---------- (1) AgentCapTruncation rule ----------

        [Test]
        public void NoCut_WhenWalkExaminedEveryone()
        {
            Assert.AreEqual(0, AgentCapTruncation.CutCount(9, 9));
            Assert.AreEqual(0, AgentCapTruncation.CutIndices(9, 9).Length);
        }

        [Test]
        public void NoCut_WhenAgentCountLandsExactlyOnCapWithNobodyLeft()
        {
            // 14 people, cap 14: every resident was examined, so nothing is
            // cut even though agents.Count == cap. The cut is the unexamined
            // tail, not "agents hit the cap".
            Assert.AreEqual(0, AgentCapTruncation.CutCount(14, 14));
            Assert.AreEqual(0, AgentCapTruncation.CutIndices(14, 14).Length);
        }

        [Test]
        public void CutOne_WhenOnePersonUnexamined()
        {
            Assert.AreEqual(1, AgentCapTruncation.CutCount(15, 14));
            int[] indices = AgentCapTruncation.CutIndices(15, 14);
            Assert.AreEqual(1, indices.Length);
            Assert.AreEqual(14, indices[0]);
        }

        [Test]
        public void CutMany_ReturnsOrderedTailIndices()
        {
            // 60 residents, walk stopped after 50 examined (scene cap 50).
            Assert.AreEqual(10, AgentCapTruncation.CutCount(60, 50));
            int[] indices = AgentCapTruncation.CutIndices(60, 50);
            Assert.AreEqual(10, indices.Length);
            for (int i = 0; i < 10; i++) Assert.AreEqual(50 + i, indices[i]);
        }

        [Test]
        public void NoCut_WhenNoPeople()
        {
            Assert.AreEqual(0, AgentCapTruncation.CutCount(0, 0));
            Assert.AreEqual(0, AgentCapTruncation.CutIndices(0, 0).Length);
        }

        [Test]
        public void OutOfRangeInputs_ClampToZeroNeverNegative()
        {
            Assert.AreEqual(0, AgentCapTruncation.CutCount(5, 9));
            Assert.AreEqual(0, AgentCapTruncation.CutIndices(5, 9).Length);
            Assert.AreEqual(0, AgentCapTruncation.CutCount(-3, 0));
        }

        [Test]
        public void NegativeExamined_ClampsToZeroAndCutsFromStart()
        {
            Assert.AreEqual(5, AgentCapTruncation.CutCount(5, -1));
            int[] indices = AgentCapTruncation.CutIndices(5, -1);
            Assert.AreEqual(5, indices.Length);
            for (int i = 0; i < 5; i++) Assert.AreEqual(i, indices[i]);
        }

        [Test]
        public void CutCount_MatchesCutIndicesLength()
        {
            int[][] pairs = { new[] { 9, 9 }, new[] { 15, 14 }, new[] { 60, 50 },
                              new[] { 1, 0 }, new[] { 0, 0 }, new[] { 51, 50 } };
            foreach (int[] pair in pairs)
            {
                Assert.AreEqual(AgentCapTruncation.CutCount(pair[0], pair[1]),
                    AgentCapTruncation.CutIndices(pair[0], pair[1]).Length,
                    "people=" + pair[0] + ", examined=" + pair[1]);
            }
        }

        // ---------- (2) Director wiring ----------

        [Test]
        public void DirectorCapDefaultStill14()
        {
            // The package must NOT change the cap number; that is an
            // owner/behaviour matter. Only the diagnostics change.
            string code = StripNonCode(ReadRepoFile(DirectorPath));
            Assert.IsTrue(code.Contains("private int maxActiveAgents = 14;"),
                "Campaign C (DEF-021): expected the code default 'private int maxActiveAgents = 14;' to be unchanged.");
        }

        [Test]
        public void PopulateLogsCutWithCountCapAndResidents()
        {
            string body = MethodBody(ReadRepoFile(DirectorPath), "private void PopulateManagedAgents()");
            string code = StripNonCode(body);
            Assert.IsTrue(code.Contains("AgentCapTruncation.CutIndices"),
                "Campaign C (DEF-021): PopulateManagedAgents must derive the cut set via AgentCapTruncation.CutIndices.");
            Assert.IsTrue(code.Contains("residentsCutByAgentCap = cutIndices.Length"),
                "Campaign C (DEF-021): PopulateManagedAgents must record the cut count.");
            Assert.IsTrue(code.Contains("lastAgentCapApplied = maxActiveAgents"),
                "Campaign C (DEF-021): PopulateManagedAgents must record the cap value in effect.");
            Assert.IsTrue(code.Contains("cutResidentIds.Add"),
                "Campaign C (DEF-021): PopulateManagedAgents must keep the cut resident ids for the arrival check.");
            Assert.IsTrue(code.Contains("Debug.LogWarning"),
                "Campaign C (DEF-021): PopulateManagedAgents must warn when the cap cuts residents.");
            // Raw body, not stripped: the warning's distinctive wording lives
            // inside a string literal, so StripNonCode would blank it. These
            // two assertions are what go red if the warning itself is removed
            // while the surrounding cut computation stays (mutation-checked).
            Assert.IsTrue(body.Contains("resident(s) at the agent cap"),
                "Campaign C (DEF-021): the populate warning must name the cut count and the agent cap.");
            Assert.IsTrue(body.Contains("cut={lastCutResidentSummary}"),
                "Campaign C (DEF-021): the populate warning must name the cut residents.");
        }

        [Test]
        public void DiagnosticsFieldsDeclared()
        {
            string code = StripNonCode(ReadRepoFile(DirectorPath));
            Assert.IsTrue(code.Contains("private int residentsCutByAgentCap;"),
                "Campaign C (DEF-021): expected serialized field residentsCutByAgentCap.");
            Assert.IsTrue(code.Contains("private int lastAgentCapApplied;"),
                "Campaign C (DEF-021): expected serialized field lastAgentCapApplied.");
            Assert.IsTrue(code.Contains("private string lastCutResidentSummary;"),
                "Campaign C (DEF-021): expected serialized field lastCutResidentSummary.");
            Assert.IsTrue(code.Contains("private int explicitArrivalChecksForCutResidents;"),
                "Campaign C (DEF-021): expected serialized field explicitArrivalChecksForCutResidents.");
        }

        [Test]
        public void DiagnosticsSummaryIncludesCutCount()
        {
            // Raw body, not stripped: the asserted content IS a string literal
            // (the interpolated summary), so StripNonCode would blank it.
            string body = MethodBody(ReadRepoFile(DirectorPath), "public string BuildDiagnosticsSummary()");
            Assert.IsTrue(body.Contains("CutByAgentCap={residentsCutByAgentCap}"),
                "Campaign C (DEF-021): BuildDiagnosticsSummary must surface the cut count.");
        }

        [Test]
        public void ArrivalCheckNamesCutResidents()
        {
            string body = MethodBody(ReadRepoFile(DirectorPath), "public bool TryGetExplicitArrival(int personId, int targetBuildingId,");
            string code = StripNonCode(body);
            Assert.IsTrue(code.Contains("cutResidentIds.Contains(personId)"),
                "Campaign C (DEF-021): TryGetExplicitArrival must distinguish cut residents from other agent-less persons.");
            Assert.IsTrue(code.Contains("explicitArrivalChecksForCutResidents++"),
                "Campaign C (DEF-021): arrival checks for cut residents must be counted.");
            Assert.IsTrue(code.Contains("warnedCutArrivalPersonIds.Add(personId)"),
                "Campaign C (DEF-021): the per-person arrival warning must fire once, not every tick.");
        }

        [Test]
        public void ResetClearsCapDiagnostics()
        {
            string body = MethodBody(ReadRepoFile(DirectorPath), "public void ResetForLoadedState()");
            string code = StripNonCode(body);
            Assert.IsTrue(code.Contains("residentsCutByAgentCap = 0;"),
                "Campaign C (DEF-021): ResetForLoadedState must clear the cut count.");
            Assert.IsTrue(code.Contains("cutResidentIds.Clear();"),
                "Campaign C (DEF-021): ResetForLoadedState must clear the cut resident set.");
            Assert.IsTrue(code.Contains("warnedCutArrivalPersonIds.Clear();"),
                "Campaign C (DEF-021): ResetForLoadedState must clear the warned-arrival set.");
        }

        [Test]
        public void ChangesAreMarkedCampaignC()
        {
            string raw = ReadRepoFile(DirectorPath);
            Assert.IsTrue(raw.Contains("Campaign C (DEF-021)"),
                "Campaign C (DEF-021): director changes must be marked so the watchdog can attribute them.");
        }

        // ---------- repo-file helpers (package-3 precedent) ----------

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, DirectorPath)))
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
                        if (File.Exists(Path.Combine(candidate, DirectorPath)))
                        {
                            return candidate;
                        }
                    }
                    DirectoryInfo parent = System.IO.Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign C (DEF-021): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign C (DEF-021): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }

        private static string MethodBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "Campaign C (DEF-021): method not found in director: " + signature);
            int next = source.Length;
            foreach (string marker in new[] { "\n        private ", "\n        public ", "\n        protected ", "\n        internal " })
            {
                int at = source.IndexOf(marker, start + signature.Length, StringComparison.Ordinal);
                if (at >= 0 && at < next) next = at;
            }
            return source.Substring(start, next - start);
        }

        /// <summary>
        /// Returns source with comments, string literals and char literals
        /// blanked (newlines preserved), so assertions cannot be fooled by a
        /// mention inside a comment or a log string.
        /// </summary>
        internal static string StripNonCode(string source)
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
                else if (c == '@' && next == '"')
                {
                    out_.Append("  ");
                    i += 2;
                    while (i < source.Length)
                    {
                        if (source[i] == '"' && i + 1 < source.Length && source[i + 1] == '"')
                        {
                            out_.Append("  ");
                            i += 2;
                        }
                        else if (source[i] == '"')
                        {
                            out_.Append(' ');
                            i++;
                            break;
                        }
                        else
                        {
                            out_.Append(source[i] == '\n' ? '\n' : ' ');
                            i++;
                        }
                    }
                }
                else if (c == '"')
                {
                    out_.Append(' ');
                    i++;
                    while (i < source.Length && source[i] != '\n')
                    {
                        if (source[i] == '\\' && i + 1 < source.Length)
                        {
                            out_.Append("  ");
                            i += 2;
                        }
                        else if (source[i] == '"')
                        {
                            out_.Append(' ');
                            i++;
                            break;
                        }
                        else
                        {
                            out_.Append(' ');
                            i++;
                        }
                    }
                }
                else if (c == '\'')
                {
                    out_.Append(' ');
                    i++;
                    while (i < source.Length && source[i] != '\n')
                    {
                        if (source[i] == '\\' && i + 1 < source.Length)
                        {
                            out_.Append("  ");
                            i += 2;
                        }
                        else if (source[i] == '\'')
                        {
                            out_.Append(' ');
                            i++;
                            break;
                        }
                        else
                        {
                            out_.Append(' ');
                            i++;
                        }
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
    }
}
