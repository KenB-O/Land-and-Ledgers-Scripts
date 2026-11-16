// CAMPAIGN-B (DEF-055), package 13: no single cash writer; primitives clamp
// silently. DIAGNOSTIC LAYER ONLY — no clamp-to-refuse change, no ledger.
//
// VERDICT: diagnostic layer added. SpendCents, SetCurrentCashCents, and
// AddCashCents on the REAL BusinessRuntimeState now warn (via the new
// BusinessCashClampDiagnostics helper) naming the business, the requested
// amount, the balance, and the clamped result whenever a clamp bites.
// Per-occurrence, not day-keyed: the primitives carry no day index
// (PayrollDayIndex is payroll-specific and -1 when unknown), and day-keying
// would mean threading a day through six call sites and changing public
// signatures for a diagnostic. Every live caller pre-checks the balance or
// bounds the spend (GeneralStoreTradingPort refuses when CurrentCashCents
// < spend; SharedBusinessWeeklyProcurement spends only affordableUnits;
// PlayerPortfolioManager spends only the surplus above the protected
// reserve; AssetSaleAppraisal verifies the balance first;
// CalculatePostAcquisitionOpeningCashCents can never return negative), so a
// bite is exceptional by construction, and callers are daily / weekly /
// transactional drivers, not per-frame, so per-occurrence volume stays
// proportional to anomalies.
//
// WHY A HELPER: these tests execute the helper's behaviour directly AND pin
// the wiring by source inspection, because the REAL BusinessRuntimeState
// cannot compile in the Roslyn harness — its closure runs through
// BusinessInstanceState.cs (for BusinessOwnerIdentity), which pulls
// ScriptableObject and ~130 further types (round-2 missing-type list:
// TownPlot, ManagerPolicyPreset, BusinessControlState, MineRuntimeState,
// and 126 more, including Unity-only ScriptableObject). A second harness
// project (campaign-b-cash.csproj) was attempted and ABANDONED at that
// point: the closure does not converge. The helper owns the clamp decision
// and the warning text, so the diagnostic logic itself IS behaviourally
// tested; the only gap is the call itself, which the structural tests pin
// by reading the production source (packages 2/5/6/7 precedent). The
// warning text is identical to the inline Debug.LogWarning text the
// primitives carried before this extraction, so the observable diagnostic
// is unchanged.
//
// SCOPE: AddCashCents is included although the brief names only SpendCents
// and SetCurrentCashCents — the register's own evidence cites AddCashCents
// (BusinessRuntimeState.cs:3073-3079) as part of the same silent-clamp
// family, and it is the same one-line pattern in the same file.
// AdjustCashCents needs no separate hunk: it routes through
// SetCurrentCashCents, so a negative delta past zero logs there (pinned by
// AdjustCashCents_RoutesThroughSet). TrySpendCents is NOT a clamp — it
// refuses (returns false) — and emits no clamp diagnostic (pinned by
// TrySpendCents_RefusesWithoutDiagnostic).
//
// SCOPE LIMITS (honest): the diagnostic fires in the harness on the helper
// and the wiring is pinned by source inspection; the Unity Console
// rendering of the warning (and its volume in a live multi-day run) is
// Unity-verified only. No live Unity run has yet shown a clamp bite in the
// dev scene — the diagnostic exists precisely so the first one is visible
// instead of silent.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LandLedgers.Economy;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class CampaignBCashClampDiagnosticsTests
    {
        private const string BusinessId = "general-store";
        private const string RuntimeStatePath =
            "Assets/Scripts/Domains/Economy/Core/BusinessRuntimeState.cs";
        private const string HelperPath =
            "Assets/Scripts/Domains/Economy/Core/BusinessCashClampDiagnostics.cs";

        /// <summary>
        /// The harness shim's Debug.LogWarning writes to Console, so capture
        /// Console.Out around the action and return everything written.
        /// </summary>
        private static string CaptureConsole(Action action)
        {
            TextWriter oldOut = Console.Out;
            var writer = new StringWriter();
            Console.SetOut(writer);
            try
            {
                action();
            }
            finally
            {
                Console.SetOut(oldOut);
            }

            return writer.ToString();
        }

        // ---------- (1) Behavioural: the helper's clamp decision + text ----------

        [Test]
        public void WarnSpendClamp_OverBalance_LogsWarning()
        {
            string log = CaptureConsole(() =>
                BusinessCashClampDiagnostics.WarnSpendClamp(BusinessId, 500, 120, 120));
            Assert.IsTrue(log.Contains("[BusinessCashClamp]"), "expected a clamp warning, got: " + log);
            Assert.IsTrue(log.Contains("Business 'general-store'"), "warning must name the business, got: " + log);
            Assert.IsTrue(log.Contains("SpendCents"), "warning must name the primitive, got: " + log);
            Assert.IsTrue(log.Contains("500c"), "warning must name the requested amount, got: " + log);
            Assert.IsTrue(log.Contains("balance 120c"), "warning must name the balance, got: " + log);
            Assert.IsTrue(log.Contains("spent 120c"), "warning must name the clamped result, got: " + log);
            Assert.IsTrue(log.Contains("shortfall 380c"), "warning must name the shortfall, got: " + log);
        }

        [Test]
        public void SpendClampMessage_OverBalance_ExactText()
        {
            Assert.AreEqual(
                "[BusinessCashClamp] Business 'general-store' SpendCents requested 500c with balance 120c; spent 120c (shortfall 380c).",
                BusinessCashClampDiagnostics.SpendClampMessage(BusinessId, 500, 120, 120));
        }

        [Test]
        public void WarnSpendClamp_ExactBalance_NoWarning()
        {
            string log = CaptureConsole(() =>
                BusinessCashClampDiagnostics.WarnSpendClamp(BusinessId, 120, 120, 120));
            Assert.IsFalse(log.Contains("[BusinessCashClamp]"), "exact-balance spend must not warn, got: " + log);
        }

        [Test]
        public void WarnSpendClamp_WithinBalance_NoWarning()
        {
            string log = CaptureConsole(() =>
                BusinessCashClampDiagnostics.WarnSpendClamp(BusinessId, 50, 120, 50));
            Assert.IsFalse(log.Contains("[BusinessCashClamp]"), "in-balance spend must not warn, got: " + log);
        }

        [Test]
        public void WarnSpendClamp_ZeroRequest_NoWarning()
        {
            string log = CaptureConsole(() =>
                BusinessCashClampDiagnostics.WarnSpendClamp(BusinessId, 0, 120, 0));
            Assert.IsFalse(log.Contains("[BusinessCashClamp]"), "zero spend must not warn, got: " + log);
        }

        [Test]
        public void WarnSpendClamp_NegativeRequest_LogsWarning()
        {
            string log = CaptureConsole(() =>
                BusinessCashClampDiagnostics.WarnSpendClamp(BusinessId, -50, 120, 0));
            Assert.IsTrue(log.Contains("[BusinessCashClamp]"), "expected a clamp warning, got: " + log);
            Assert.IsTrue(log.Contains("(negative)"), "warning must say the request was negative, got: " + log);
            Assert.IsTrue(log.Contains("no cash moved"), "warning must say no cash moved, got: " + log);
        }

        [Test]
        public void WarnSpendClamp_ZeroBalance_LogsWarning()
        {
            string log = CaptureConsole(() =>
                BusinessCashClampDiagnostics.WarnSpendClamp(BusinessId, 10, 0, 0));
            Assert.IsTrue(log.Contains("[BusinessCashClamp]"), "expected a clamp warning, got: " + log);
            Assert.IsTrue(log.Contains("shortfall 10c"), "warning must name the shortfall, got: " + log);
        }

        [Test]
        public void SpendClamps_Predicate()
        {
            Assert.IsTrue(BusinessCashClampDiagnostics.SpendClamps(500, 120));
            Assert.IsTrue(BusinessCashClampDiagnostics.SpendClamps(-1, 120));
            Assert.IsTrue(BusinessCashClampDiagnostics.SpendClamps(10, 0));
            Assert.IsFalse(BusinessCashClampDiagnostics.SpendClamps(120, 120));
            Assert.IsFalse(BusinessCashClampDiagnostics.SpendClamps(50, 120));
            Assert.IsFalse(BusinessCashClampDiagnostics.SpendClamps(0, 120));
        }

        [Test]
        public void WarnSetClamp_Negative_LogsWarning()
        {
            string log = CaptureConsole(() =>
                BusinessCashClampDiagnostics.WarnSetClamp(BusinessId, -50, 120));
            Assert.IsTrue(log.Contains("[BusinessCashClamp]"), "expected a clamp warning, got: " + log);
            Assert.IsTrue(log.Contains("SetCurrentCashCents"), "warning must name the primitive, got: " + log);
            Assert.IsTrue(log.Contains("-50c"), "warning must name the requested amount, got: " + log);
            Assert.IsTrue(log.Contains("balance was 120c"), "warning must name the prior balance, got: " + log);
            Assert.IsTrue(log.Contains("clamped to 0c"), "warning must name the clamped result, got: " + log);
        }

        [Test]
        public void WarnSetClamp_NormalSet_NoWarning()
        {
            string log = CaptureConsole(() =>
                BusinessCashClampDiagnostics.WarnSetClamp(BusinessId, 80, 120));
            Assert.IsFalse(log.Contains("[BusinessCashClamp]"), "ordinary set must not warn, got: " + log);
        }

        [Test]
        public void WarnSetClamp_SetToZero_NoWarning()
        {
            string log = CaptureConsole(() =>
                BusinessCashClampDiagnostics.WarnSetClamp(BusinessId, 0, 120));
            Assert.IsFalse(log.Contains("[BusinessCashClamp]"), "setting cash to zero is a legitimate set, not a clamp, got: " + log);
        }

        [Test]
        public void WarnAddClamp_Negative_LogsWarning()
        {
            string log = CaptureConsole(() =>
                BusinessCashClampDiagnostics.WarnAddClamp(BusinessId, -30, 120));
            Assert.IsTrue(log.Contains("[BusinessCashClamp]"), "expected a clamp warning, got: " + log);
            Assert.IsTrue(log.Contains("AddCashCents"), "warning must name the primitive, got: " + log);
            Assert.IsTrue(log.Contains("-30c"), "warning must name the requested amount, got: " + log);
        }

        [Test]
        public void WarnAddClamp_Positive_NoWarning()
        {
            string log = CaptureConsole(() =>
                BusinessCashClampDiagnostics.WarnAddClamp(BusinessId, 30, 120));
            Assert.IsFalse(log.Contains("[BusinessCashClamp]"), "ordinary add must not warn, got: " + log);
        }

        // ---------- (2) Structural: the primitives actually call the helper ----------

        [Test]
        public void SpendCents_CallsWarnSpendClamp_AndKeepsClampMath()
        {
            string body = MethodBody("public void SpendCents(int cents)", "public void AddCashCents(int cents)");
            Assert.IsTrue(body.Contains("BusinessCashClampDiagnostics.WarnSpendClamp"),
                "SpendCents must call the clamp diagnostic. Body: " + body);
            Assert.AreEqual(1, CountOccurrences(body, "WarnSpendClamp("),
                "SpendCents must call the diagnostic exactly once. Body: " + body);
            Assert.IsTrue(body.Contains("Mathf.Min(currentCashCents, Mathf.Max(0, cents))"),
                "SpendCents clamp math must be unchanged. Body: " + body);
            Assert.IsFalse(body.Contains("Debug.LogWarning"),
                "SpendCents must not log inline any more — the helper owns the text. Body: " + body);
        }

        [Test]
        public void AddCashCents_CallsWarnAddClamp_AndKeepsClampMath()
        {
            string body = MethodBody("public void AddCashCents(int cents)", "public void SetCurrentCashCents(int cents, bool recordNetDelta = false)");
            Assert.IsTrue(body.Contains("BusinessCashClampDiagnostics.WarnAddClamp"),
                "AddCashCents must call the clamp diagnostic. Body: " + body);
            Assert.AreEqual(1, CountOccurrences(body, "WarnAddClamp("),
                "AddCashCents must call the diagnostic exactly once. Body: " + body);
            Assert.IsTrue(body.Contains("Mathf.Max(0, cents)"),
                "AddCashCents clamp math must be unchanged. Body: " + body);
            Assert.IsFalse(body.Contains("Debug.LogWarning"),
                "AddCashCents must not log inline any more — the helper owns the text. Body: " + body);
        }

        [Test]
        public void SetCurrentCashCents_CallsWarnSetClamp_AndKeepsClampMath()
        {
            string body = MethodBody("public void SetCurrentCashCents(int cents, bool recordNetDelta = false)", "public void AdjustCashCents(int deltaCents, bool recordNetDelta = false)");
            Assert.IsTrue(body.Contains("BusinessCashClampDiagnostics.WarnSetClamp"),
                "SetCurrentCashCents must call the clamp diagnostic. Body: " + body);
            Assert.AreEqual(1, CountOccurrences(body, "WarnSetClamp("),
                "SetCurrentCashCents must call the diagnostic exactly once. Body: " + body);
            Assert.IsTrue(body.Contains("Mathf.Max(0, cents)"),
                "SetCurrentCashCents clamp math must be unchanged. Body: " + body);
            Assert.IsFalse(body.Contains("Debug.LogWarning"),
                "SetCurrentCashCents must not log inline any more — the helper owns the text. Body: " + body);
        }

        [Test]
        public void TrySpendCents_RefusesWithoutDiagnostic()
        {
            // TrySpendCents is the checked primitive: it refuses (returns
            // false) instead of clamping, so it must stay silent — no clamp
            // diagnostic call in its body.
            string body = MethodBody("public bool TrySpendCents(int cents)", "public void RecordWeeklyInputProcurement(int units, int spendCents)");
            Assert.IsTrue(body.Contains("return false"),
                "TrySpendCents must still refuse on insufficient balance. Body: " + body);
            Assert.IsFalse(body.Contains("BusinessCashClampDiagnostics"),
                "TrySpendCents must not call the clamp diagnostic — it refuses, it does not clamp. Body: " + body);
        }

        [Test]
        public void AdjustCashCents_RoutesThroughSet()
        {
            // A negative delta past zero logs via SetCurrentCashCents; no
            // separate hunk is needed (or wanted) in AdjustCashCents.
            string body = MethodBody("public void AdjustCashCents(int deltaCents, bool recordNetDelta = false)", "internal BusinessRuntimeTransactionSnapshot CaptureTransactionSnapshot()");
            Assert.IsTrue(body.Contains("SetCurrentCashCents(CurrentCashCents + deltaCents"),
                "AdjustCashCents must route through SetCurrentCashCents. Body: " + body);
            Assert.IsFalse(body.Contains("BusinessCashClampDiagnostics"),
                "AdjustCashCents must not call the diagnostic directly — it logs via Set. Body: " + body);
        }

        [Test]
        public void HelperDefinedExactlyOnce()
        {
            var holders = new List<string>();
            foreach (string file in ProductionCsFiles())
            {
                string code = StripNonCode(File.ReadAllText(file));
                if (code.Contains("class BusinessCashClampDiagnostics"))
                {
                    holders.Add(Relative(file));
                }
            }
            Assert.AreEqual(1, holders.Count, "Files defining BusinessCashClampDiagnostics: " + string.Join(", ", holders));
            Assert.AreEqual(HelperPath, holders[0]);
        }

        [Test]
        public void NoOtherProductionCallerOfTheWarnMethods()
        {
            // The three Warn* methods have exactly one production call site
            // each, all in BusinessRuntimeState.cs — the diagnostic cannot
            // fire from anywhere else.
            foreach (string file in ProductionCsFiles())
            {
                if (Relative(file) == HelperPath || Relative(file) == RuntimeStatePath)
                {
                    continue;
                }

                string code = StripNonCode(File.ReadAllText(file));
                Assert.IsFalse(code.Contains("WarnSpendClamp("), "unexpected WarnSpendClamp call in " + Relative(file));
                Assert.IsFalse(code.Contains("WarnSetClamp("), "unexpected WarnSetClamp call in " + Relative(file));
                Assert.IsFalse(code.Contains("WarnAddClamp("), "unexpected WarnAddClamp call in " + Relative(file));
            }
        }

        // ---------- repo-location helpers (B-7 precedent) ----------

        private static string MethodBody(string startSignature, string endSignature)
        {
            string source = ReadRepoFile(RuntimeStatePath);
            int start = source.IndexOf(startSignature, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "method not found: " + startSignature);
            int end = source.IndexOf(endSignature, start, StringComparison.Ordinal);
            Assert.IsTrue(end > start, "end signature not found after " + startSignature + ": " + endSignature);
            return StripNonCode(source.Substring(start, end - start));
        }

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, RuntimeStatePath)))
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
                        if (File.Exists(Path.Combine(candidate, RuntimeStatePath)))
                        {
                            return candidate;
                        }
                    }

                    DirectoryInfo parent = System.IO.Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign B (DEF-055): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign B (DEF-055): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }

        private static string Relative(string fullPath)
        {
            string root = FindRepoRoot();
            string full = Path.GetFullPath(fullPath);
            string base_ = Path.GetFullPath(root);
            if (full.StartsWith(base_, StringComparison.Ordinal))
            {
                return full.Substring(base_.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            return full;
        }

        private static IEnumerable<string> ProductionCsFiles()
        {
            string scripts = Path.Combine(FindRepoRoot(), "Assets", "Scripts");
            foreach (string file in System.IO.Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                string[] parts = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                bool inTests = false;
                foreach (string part in parts)
                {
                    if (string.Equals(part, "Tests", StringComparison.Ordinal))
                    {
                        inTests = true;
                        break;
                    }
                }
                if (!inTests)
                {
                    yield return file;
                }
            }
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

        /// <summary>Source with comments, string literals and char literals
        /// blanked (newlines preserved), so assertions cannot be fooled by a
        /// mention inside a comment or a log string. (B-6 precedent.)</summary>
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
                            out_.Append(source[i] == '\n' ? '\n' : ' ');
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
                            out_.Append(source[i] == '\n' ? '\n' : ' ');
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
