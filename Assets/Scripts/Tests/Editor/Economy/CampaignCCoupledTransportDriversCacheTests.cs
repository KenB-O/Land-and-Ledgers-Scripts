// Campaign C (DEF-030 + DEF-031, one commit) — wagon arrival confirmation
// does a scene lookup; the "regional destination trigger required" log line
// is misleading.
//
// DEF-030: CoupledTransportMovementController confirmed arrival with the
// drivers object via a per-arrival FindAnyObjectByType<SimulationDrivers>()
// scene lookup (origin arrival in AdvanceSimulationTime, destination
// arrival in OnRegionalDestinationTrigger). This package resolves the
// drivers reference once at bind time (StartLeg, StartRegionalReturn,
// RestoreLeg) and holds it in a cached field; both arrival sites use the
// cache through ResolveDrivers(), which re-resolves on demand when the
// cache is empty, so a drivers object that appears after bind time is
// still found at arrival, exactly as the old per-arrival lookup would
// have found it. No behaviour the lookup or the arrival gates: the cached
// instance is the one a fresh lookup would return (the scene holds
// exactly one drivers object), and the null/missing-drivers refusals are
// unchanged.
//
// DEF-031: the regional-boundary log line said "regional destination
// trigger required", which reads as if a trigger mechanism were missing
// for completion. The regional leg completes on the simulation-time timer
// and confirms arrival itself; the line describes the presentation
// handoff (the team continues beyond the boundary, hidden, while the
// regional leg runs on simulation time). This package rewords the line
// to say that, at the same level (Debug.Log) and under the same
// conditions. No movement, trigger, or completion change.
//
// All tests here are structural: the controller is harness-excluded
// (its closure pulls PopulationPathingDirector, HorseWorldView and
// WagonWorldView), so the wiring and wording are asserted by reading the
// production file — the CampaignCCancelTaskDueEventTests precedent. A
// missing repo fails loudly, never silently passes. Unity-run proof is
// still owed for both defects (a Millbrook haul must complete its
// regional leg in a run; the arrival confirmations must resolve to the
// live drivers object) — no Unity verification is claimed by this
// package.
using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class CampaignCCoupledTransportDriversCacheTests
    {
        private const string ControllerPath =
            "Assets/Scripts/Domains/Economy/Transport/CoupledTransportMovementController.cs";

        // ---------- DEF-030: cached drivers reference ----------

        [Test]
        public void DriversLookup_ExistsExactlyOnce_InsideResolveDrivers()
        {
            string code = StripNonCode(ReadRepoFile(ControllerPath));
            int count = CountOccurrences(code, "FindAnyObjectByType<SimulationDrivers>()");
            Assert.AreEqual(1, count,
                "Campaign C (DEF-030): exactly one scene lookup for the drivers object must remain, "
                + "inside ResolveDrivers — the two per-arrival lookups are gone, and no new one "
                + "was added. Found " + count + ".");
            string resolver = MethodBody(code, "private SimulationDrivers ResolveDrivers()");
            Assert.IsTrue(resolver.Contains("FindAnyObjectByType<SimulationDrivers>()"),
                "Campaign C (DEF-030): the single remaining lookup must live in ResolveDrivers");
        }

        [Test]
        public void CachedReference_FieldAndResolverExistWithOnDemandReResolve()
        {
            string code = StripNonCode(ReadRepoFile(ControllerPath));
            Assert.IsTrue(code.Contains("private SimulationDrivers cachedDrivers;"),
                "Campaign C (DEF-030): the cached drivers field is missing");
            string resolver = MethodBody(code, "private SimulationDrivers ResolveDrivers()");
            Assert.IsTrue(resolver.Contains("if (cachedDrivers == null)"),
                "Campaign C (DEF-030): ResolveDrivers must re-resolve when the cache is empty, "
                + "so a drivers object that appears after bind time is still found at arrival");
            Assert.IsTrue(resolver.Contains("cachedDrivers = FindAnyObjectByType<SimulationDrivers>();"),
                "Campaign C (DEF-030): ResolveDrivers must fill the cache from the scene lookup");
            Assert.IsTrue(resolver.Contains("return cachedDrivers;"),
                "Campaign C (DEF-030): ResolveDrivers must return the cached instance");
        }

        [Test]
        public void ArrivalSites_UseCachedReference()
        {
            string code = StripNonCode(ReadRepoFile(ControllerPath));

            string advance = MethodBody(code, "public void AdvanceSimulationTime(float simulationDeltaSeconds)");
            Assert.IsTrue(advance.Contains("SimulationDrivers drivers = ResolveDrivers();"),
                "Campaign C (DEF-030): the origin-arrival confirmation in AdvanceSimulationTime "
                + "must use the cached reference");
            Assert.IsTrue(advance.Contains("ConfirmPhysicalFreightOriginArrival("),
                "Campaign C (DEF-030): the origin-arrival confirmation call itself is untouched");
            Assert.IsFalse(advance.Contains("FindAnyObjectByType<SimulationDrivers>()"),
                "Campaign C (DEF-030): AdvanceSimulationTime must not do its own scene lookup anymore");

            string trigger = MethodBody(code, "public bool OnRegionalDestinationTrigger(string locationId, string businessId)");
            Assert.IsTrue(trigger.Contains("SimulationDrivers drivers = ResolveDrivers();"),
                "Campaign C (DEF-030): the destination-arrival confirmation in "
                + "OnRegionalDestinationTrigger must use the cached reference");
            Assert.IsTrue(trigger.Contains("ConfirmPhysicalFreightDestinationArrival("),
                "Campaign C (DEF-030): the destination-arrival confirmation call itself is untouched");
            Assert.IsFalse(trigger.Contains("FindAnyObjectByType<SimulationDrivers>()"),
                "Campaign C (DEF-030): OnRegionalDestinationTrigger must not do its own scene lookup anymore");
        }

        [Test]
        public void BindTime_PrimesTheCache()
        {
            string code = StripNonCode(ReadRepoFile(ControllerPath));
            foreach (string signature in new[]
            {
                "public bool StartLeg(",
                "public bool StartRegionalReturn(",
                "public bool RestoreLeg(WagonLegSaveDto dto)",
            })
            {
                string body = MethodBody(code, signature);
                Assert.IsTrue(body.Contains("ResolveDrivers();"),
                    "Campaign C (DEF-030): " + signature + " must resolve the drivers reference "
                    + "once at bind time, so the arrival sites never pay for a scene lookup");
            }
        }

        // ---------- DEF-031: regional-boundary log line ----------

        [Test]
        public void RegionalBoundaryLog_StatesPresentationHandoff_NotMissingTrigger()
        {
            string source = ReadRepoFile(ControllerPath);
            Assert.IsTrue(source.Contains("regional leg continues on simulation time (presentation hidden beyond the boundary)"),
                "Campaign C (DEF-031): the regional-boundary log line must describe the "
                + "presentation handoff — the leg continues on simulation time, hidden");
            Assert.IsFalse(source.Contains("regional destination trigger required"),
                "Campaign C (DEF-031): the misleading 'regional destination trigger required' "
                + "wording must be gone — it read as if a trigger mechanism were missing for "
                + "completion, when the leg completes on the timer and confirms arrival itself");
        }

        [Test]
        public void RegionalBoundaryLog_FiresAtSameLevelUnderSameConditions()
        {
            string source = ReadRepoFile(ControllerPath);
            string advance = MethodBody(source, "public void AdvanceSimulationTime(float simulationDeltaSeconds)");
            Assert.IsTrue(advance.Contains("Debug.Log($\"[CoupledTransport] continuing physically beyond the town boundary"),
                "Campaign C (DEF-031): the regional-boundary line stays a Debug.Log at the same "
                + "call site — its level does not change");
            // Search forward from each anchor: the same
            // "!string.IsNullOrWhiteSpace(physicalJourneyId)" guard also
            // appears earlier in the method (the DEF-048 refusal record in
            // the origin-arrival block), so a first-occurrence search would
            // land before the hideOnArrival branch.
            int hideAt = advance.IndexOf("if (hideOnArrival)", StringComparison.Ordinal);
            int bindingAt = hideAt >= 0
                ? advance.IndexOf("!string.IsNullOrWhiteSpace(physicalJourneyId)", hideAt, StringComparison.Ordinal)
                : -1;
            int logAt = bindingAt >= 0
                ? advance.IndexOf("continuing physically beyond the town boundary", bindingAt, StringComparison.Ordinal)
                : -1;
            int elseAt = logAt >= 0
                ? advance.IndexOf("physical destination binding is incomplete", logAt, StringComparison.Ordinal)
                : -1;
            Assert.IsTrue(hideAt >= 0 && bindingAt > hideAt && logAt > bindingAt && elseAt > logAt,
                "Campaign C (DEF-031): the log line must still fire only inside the hideOnArrival "
                + "branch, after the physical-destination completeness check, with the "
                + "incomplete-binding warning still present in the else branch — same conditions, "
                + "same level, only the wording changed");
        }

        // ---------- repo-file helpers (CampaignCCancelTaskDueEventTests precedent) ----------

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, ControllerPath)))
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
                        if (File.Exists(Path.Combine(candidate, ControllerPath)))
                        {
                            return candidate;
                        }
                    }
                    DirectoryInfo parent = System.IO.Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign C (DEF-030/031): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign C (DEF-030/031): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }

        private static string MethodBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "Campaign C (DEF-030/031): method not found: " + signature);
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
