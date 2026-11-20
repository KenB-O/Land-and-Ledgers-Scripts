using System;
using System.IO;
using System.Text;
using LandLedgers.Economy;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>CAMPAIGN-B (DEF-044), package 6: support-node weekly
    /// production cuts timber but saws zero lumber.
    ///
    /// VERDICT — formation-blocked AND mechanically broken, in two layers.
    /// Layer 1 (formation, NOT fixed here): sawing is gated on 1 unit/week
    /// of blacksmith hardware maintenance and the regional fallback
    /// (438c/unit) requires a paying Sawmill business account; the dev
    /// scenario has no blacksmith and no Sawmill, so the gate fails every
    /// week and lumber output is zero. Whether the dev scenario should
    /// form a Sawmill/LumberYard, and whether the blacksmith should be
    /// the sawing gate at all, are owner decisions (Check 8 Q1 and Q10;
    /// DEF-004 family). This package answers neither and forms nothing.
    /// Layer 2 (mechanical, FIXED here): the local fallback path cut
    /// standing timber FIRST and only then discovered sawing was blocked,
    /// so every week it converted 10 units of a finite resource (80
    /// standing timber) into staged logs that have no consumer — staged
    /// logs in the local path are consumed only by ProcessLogsToLumber —
    /// while lumber output stayed zero from week one. The shared path (a
    /// real Sawmill business) does not have this failure mode: a failed
    /// maintenance gate there only halves saw capacity instead of blocking
    /// sawing outright, so a cut still leads to sawing. The resolve method
    /// now plans the week's cut/saw from pre-cut state, checks the
    /// hardware gate only when the plan says there is something to saw,
    /// and cuts only when the gate passes — so a blocked week preserves
    /// the standing timber instead of piling up unusable logs. No
    /// capacity, price, gate rule, or block reason changes.
    ///
    /// What the Roslyn harness can reach: the planning arithmetic (pure
    /// helper) and the wiring (structural reads of
    /// AcquisitionMarketManager.cs — the manager is too large a closure
    /// for the focused harness, same class as the package-2/5 driver
    /// wiring). A live weekly resolution in a real run (Codex) still
    /// needs a Unity run: whether the weekly summary shows preserved
    /// timber across blocked weeks is a runtime question.</summary>
    [TestFixture]
    public sealed class CampaignBSupportNodeSawingTests
    {
        private const string ManagerPath =
            "Assets/Scripts/Domains/Economy/Acquisitions/AcquisitionMarketManager.cs";

        // Dev-scenario asset values (LocalSawmillSupport.asset): 80 standing
        // timber, 8 staged logs, cut 10/wk, saw 10/wk, 4 lumber/log, 360
        // lumber storage, 1 hardware/wk. Labor 72 = 9 residents x 8 units.

        // ---------- (1) Planner arithmetic ----------

        [Test]
        public void DevScenario_PlansFullCutAndSaw()
        {
            LocalSawmillWeekPlan plan = LocalSawmillWeekPlanner.Plan(80, 8, 10, 10, 4, 360, 72);
            Assert.AreEqual(10, plan.CutLogs);
            Assert.AreEqual(10, plan.LogsToProcess);
            Assert.AreEqual(90, plan.LogsByStorage);
        }

        [Test]
        public void ThinLabor_CapsCutAndSawAtLabor()
        {
            LocalSawmillWeekPlan plan = LocalSawmillWeekPlanner.Plan(80, 8, 10, 10, 4, 360, 5);
            Assert.AreEqual(5, plan.CutLogs);
            Assert.AreEqual(5, plan.LogsToProcess);
        }

        [Test]
        public void LowStandingTimber_CutClampedSawUsesStagedStock()
        {
            LocalSawmillWeekPlan plan = LocalSawmillWeekPlanner.Plan(3, 8, 10, 10, 4, 360, 72);
            Assert.AreEqual(3, plan.CutLogs);
            Assert.AreEqual(10, plan.LogsToProcess);
        }

        [Test]
        public void NoStandingTimberNoStock_NothingPlanned()
        {
            LocalSawmillWeekPlan plan = LocalSawmillWeekPlanner.Plan(0, 0, 10, 10, 4, 360, 72);
            Assert.AreEqual(0, plan.CutLogs);
            Assert.AreEqual(0, plan.LogsToProcess);
        }

        [Test]
        public void NoStandingTimberButStagedStock_SawsStockOnly()
        {
            LocalSawmillWeekPlan plan = LocalSawmillWeekPlanner.Plan(0, 8, 10, 10, 4, 360, 72);
            Assert.AreEqual(0, plan.CutLogs);
            Assert.AreEqual(8, plan.LogsToProcess);
        }

        [Test]
        public void StorageRoomZero_NothingToSaw()
        {
            // The prospective cut is still 10, but LogsToProcess is 0, so
            // the resolve method never reaches the gate and never cuts.
            LocalSawmillWeekPlan plan = LocalSawmillWeekPlanner.Plan(80, 8, 10, 10, 4, 0, 72);
            Assert.AreEqual(10, plan.CutLogs);
            Assert.AreEqual(0, plan.LogsByStorage);
            Assert.AreEqual(0, plan.LogsToProcess);
        }

        [Test]
        public void SawCapacityBinds()
        {
            LocalSawmillWeekPlan plan = LocalSawmillWeekPlanner.Plan(80, 8, 10, 6, 4, 360, 72);
            Assert.AreEqual(10, plan.CutLogs);
            Assert.AreEqual(6, plan.LogsToProcess);
        }

        [Test]
        public void ZeroLumberPerLog_NothingToSaw()
        {
            LocalSawmillWeekPlan plan = LocalSawmillWeekPlanner.Plan(80, 8, 10, 10, 0, 360, 72);
            Assert.AreEqual(0, plan.LogsByStorage);
            Assert.AreEqual(0, plan.LogsToProcess);
        }

        [Test]
        public void NegativeInputs_ClampedToZero()
        {
            LocalSawmillWeekPlan plan = LocalSawmillWeekPlanner.Plan(-5, -2, 10, 10, 4, 360, 72);
            Assert.AreEqual(0, plan.CutLogs);
            Assert.AreEqual(0, plan.LogsToProcess);
        }

        // ---------- (2) Structural: gate before cut in the resolve ----------

        [Test]
        public void GateIsCheckedBeforeTheCut()
        {
            string body = StripNonCode(MethodBody(ReadRepoFile(ManagerPath),
                "public bool ResolveWeeklyConstructionSupportProduction()"));
            int gate = body.IndexOf("TryConsumeSawmillMaintenanceHardware(maintenanceUnits", StringComparison.Ordinal);
            Assert.IsTrue(gate >= 0, "The resolve must still check the hardware gate.");
            int cut = body.IndexOf("CutStandingTimberToLogs(weekPlan.CutLogs)", StringComparison.Ordinal);
            Assert.IsTrue(cut >= 0, "The resolve must cut from the week's plan.");
            Assert.IsTrue(gate < cut,
                "The hardware gate must be checked BEFORE the cut runs — a blocked week must not consume standing timber.");
            Assert.AreEqual(1, CountOccurrences(body, "CutStandingTimberToLogs("),
                "The resolve must have exactly one cut call, inside the gate-pass branch.");
            Assert.AreEqual(1, CountOccurrences(body, "TryConsumeSawmillMaintenanceHardware(maintenanceUnits"),
                "The resolve must have exactly one hardware-gate check.");
        }

        [Test]
        public void PlanIsComputedFromPreCutState()
        {
            string body = StripNonCode(MethodBody(ReadRepoFile(ManagerPath),
                "public bool ResolveWeeklyConstructionSupportProduction()"));
            int plan = body.IndexOf("LocalSawmillWeekPlanner.Plan(", StringComparison.Ordinal);
            Assert.IsTrue(plan >= 0, "The resolve must plan the week through LocalSawmillWeekPlanner.");
            int gate = body.IndexOf("TryConsumeSawmillMaintenanceHardware(maintenanceUnits", StringComparison.Ordinal);
            Assert.IsTrue(plan < gate, "The plan must be computed before the hardware gate is checked.");
            Assert.IsTrue(body.Contains("sawmill.StandingTimberUnits"),
                "The plan must read pre-cut standing timber.");
            Assert.IsTrue(body.Contains("sawmill.LogStockUnits"),
                "The plan must read pre-cut staged-log stock.");
        }

        [Test]
        public void BlockReasonsUnchanged()
        {
            string body = MethodBody(ReadRepoFile(ManagerPath),
                "public bool ResolveWeeklyConstructionSupportProduction()");
            Assert.IsTrue(body.Contains("no staged logs at the sawmill"),
                "The no-staged-logs block reason must survive the reorder.");
            Assert.IsTrue(body.Contains("sawmill lumber storage full"),
                "The storage-full block reason must survive the reorder.");
            Assert.IsTrue(body.Contains("blockedReasons.Add(hardwareBlockReason)"),
                "The hardware block reason must still be recorded when the gate fails.");
        }

        [Test]
        public void SharedPathStillFirst()
        {
            string body = StripNonCode(MethodBody(ReadRepoFile(ManagerPath),
                "public bool ResolveWeeklyConstructionSupportProduction()"));
            int shared = body.IndexOf("TryResolveWeeklySawmillProduction(", StringComparison.Ordinal);
            Assert.IsTrue(shared >= 0, "The resolve must still try the shared Sawmill-business path first.");
            int plan = body.IndexOf("LocalSawmillWeekPlanner.Plan(", StringComparison.Ordinal);
            Assert.IsTrue(shared < plan,
                "The local fallback plan must come after the shared-path attempt, not before it.");
        }

        [Test]
        public void WeeklySupportProductionYieldsOnlyToTheMatchingPhysicalMill()
        {
            Assert.IsTrue(SharedBusinessRuntimeManager.ShouldYieldWeeklyProductionToPhysicalProducer(
                "sawmill_small_sawmill_004", "sawmill_small_sawmill_004"));
            Assert.IsFalse(SharedBusinessRuntimeManager.ShouldYieldWeeklyProductionToPhysicalProducer(
                "sawmill_small_sawmill_004", "sawmill_other_005"));
            Assert.IsFalse(SharedBusinessRuntimeManager.ShouldYieldWeeklyProductionToPhysicalProducer(
                string.Empty, "sawmill_small_sawmill_004"));
        }

        [Test]
        public void SharedSawmillPathChecksPhysicalOwnerBeforeMirroringOrProducing()
        {
            string body = StripNonCode(MethodBody(ReadRepoFile(
                    "Assets/Scripts/Domains/Economy/Core/SharedBusinessRuntimeManager.cs"),
                "public bool TryResolveWeeklySawmillProduction("));
            int authorityCheck = body.IndexOf("HasMatchingPhysicalSawmillProducer(business.InstanceId)", StringComparison.Ordinal);
            int supportMirror = body.IndexOf("SyncLinkedSawmillSupportFromBusiness(business)", StringComparison.Ordinal);
            int produce = body.IndexOf("ResolveSawmillBusinessProduction(business, sawmill, weekKey, null)", StringComparison.Ordinal);
            Assert.GreaterOrEqual(authorityCheck, 0);
            Assert.Greater(supportMirror, authorityCheck,
                "The physical production owner must be checked before synchronizing or consuming compatibility stock.");
            Assert.Greater(produce, authorityCheck,
                "The weekly producer must be unreachable when the matching physical producer owns the mill.");
        }

        // ---------- repo-file helpers (package-5 / C-4 precedent) ----------

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, ManagerPath)))
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
                        if (File.Exists(Path.Combine(candidate, ManagerPath)))
                        {
                            return candidate;
                        }
                    }
                    DirectoryInfo parent = System.IO.Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign B (DEF-044): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign B (DEF-044): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }

        private static string MethodBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "Campaign B (DEF-044): method not found in AcquisitionMarketManager.cs: " + signature);
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
