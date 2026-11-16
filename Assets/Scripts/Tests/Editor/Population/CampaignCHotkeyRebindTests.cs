// Campaign C (DEF-065, corrected) — the dev-hotkey load path MUST reach
// SimulationDrivers.Rebind, because Rebind runs the supplier-adapter,
// diagnostics, task-definition and shopping-trip-rehydration restore work.
// Check 9 F-03 claimed the hotkey path never rebinds; it missed the link:
// every load funnels through SaveLoadManager.LoadSlotAtPath, whose success
// path calls FirstLedgerSliceBootstrapper.MarkRestoredFromSave, which calls
// Rebind. These tests pin that chain structurally by reading the three
// production files — SimulationDrivers is too large a closure for the
// focused harness, so the wiring itself (not Unity behaviour) is what is
// asserted here, exactly like the package-2 guard tests pin guard state.
// The repo root comes from LAND_LEDGERS_REPO, then a walk up from the
// current directory and the test assembly location (campaign layout:
// <root>/work-c/Assets/...), mirroring UnwiredSystemsRegistryTests. A
// missing repo fails loudly, never silently passes.
using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    [TestFixture]
    public sealed class CampaignCHotkeyRebindTests
    {
        private const string SaveLoadManagerPath =
            "Assets/Scripts/Infrastructure/Persistence/SaveLoadManager.cs";
        private const string BootstrapperPath =
            "Assets/Scripts/Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs";
        private const string SimulationDriversPath =
            "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, SaveLoadManagerPath)))
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
                        if (File.Exists(Path.Combine(candidate, SaveLoadManagerPath)))
                        {
                            return candidate;
                        }
                    }
                    DirectoryInfo parent = System.IO.Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign C (DEF-065): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign C (DEF-065): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }

        /// <summary>
        /// Returns source with comments, string literals and char literals
        /// blanked (newlines preserved), so brace matching and call counting
        /// cannot be fooled by a mention inside a comment or a log string.
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

        /// <summary>
        /// Extracts the body of the first method whose declaration contains
        /// <paramref name="signature"/> (e.g. "private bool LoadSlotAtPath(").
        /// Returns null when the signature is absent. Brace matching runs on
        /// the comment/string-stripped source, so braces inside log strings
        /// (including interpolated strings) cannot unbalance it; the returned
        /// body is the stripped slice, so a call mentioned only in a comment
        /// or a log string cannot satisfy a call assertion.
        /// </summary>
        internal static string ExtractMethodBody(string source, string signature)
        {
            if (!TryExtractSpan(source, signature, out int start, out int end))
            {
                return null;
            }
            return StripNonCode(source).Substring(start, end - start);
        }

        /// <summary>
        /// Same span as <see cref="ExtractMethodBody"/>, sliced from the raw
        /// source. Use only for asserting on log-message text, which the
        /// stripped slice blanks out.
        /// </summary>
        internal static string ExtractMethodBodyRaw(string source, string signature)
        {
            if (!TryExtractSpan(source, signature, out int start, out int end))
            {
                return null;
            }
            return source.Substring(start, end - start);
        }

        private static bool TryExtractSpan(string source, string signature, out int start, out int end)
        {
            start = 0;
            end = 0;
            string code = StripNonCode(source);
            int at = code.IndexOf(signature, StringComparison.Ordinal);
            if (at < 0)
            {
                return false;
            }
            int open = code.IndexOf('{', at + signature.Length);
            if (open < 0)
            {
                return false;
            }
            int depth = 0;
            for (int i = open; i < code.Length; i++)
            {
                if (code[i] == '{')
                {
                    depth++;
                }
                else if (code[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        start = open + 1;
                        end = i;
                        return true;
                    }
                }
            }
            return false;
        }

        private static int CountOccurrences(string code, string needle)
        {
            int count = 0;
            int at = 0;
            while ((at = code.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += needle.Length;
            }
            return count;
        }

        [Test]
        public void HotkeyUpdate_CallsLoadDefaultSlot()
        {
            string source = ReadRepoFile(SaveLoadManagerPath);
            string body = ExtractMethodBody(source, "private void Update()");
            Assert.IsNotNull(body, "SaveLoadManager.Update not found.");
            Assert.IsTrue(body.Contains("LoadDefaultSlot("),
                "DEF-065: the dev-hotkey Update must load via LoadDefaultSlot — the entry point of the load chain.");
        }

        [Test]
        public void LoadDefaultSlot_FunnelsThroughLoadSlotAtPath()
        {
            string source = ReadRepoFile(SaveLoadManagerPath);
            string body = ExtractMethodBody(source, "public bool LoadDefaultSlot(");
            Assert.IsNotNull(body, "SaveLoadManager.LoadDefaultSlot not found.");
            Assert.IsTrue(body.Contains("LoadSlotAtPath("),
                "DEF-065: LoadDefaultSlot must funnel through LoadSlotAtPath — the single load funnel.");
        }

        [Test]
        public void LoadSlotAtPath_SuccessPath_CallsMarkRestoredFromSave()
        {
            string source = ReadRepoFile(SaveLoadManagerPath);
            string body = ExtractMethodBody(source, "private bool LoadSlotAtPath(");
            Assert.IsNotNull(body, "SaveLoadManager.LoadSlotAtPath not found.");
            Assert.IsTrue(body.Contains("MarkRestoredFromSave("),
                "DEF-065: LoadSlotAtPath must call bootstrapper.MarkRestoredFromSave on its success path — "
                + "this is the link Check 9 F-03 missed, and the only route by which the hotkey path reaches Rebind.");
        }

        [Test]
        public void LoadSlotAtPath_MissingBootstrapper_LogsLoudWarning()
        {
            string source = ReadRepoFile(SaveLoadManagerPath);
            // Log-message text lives inside a string literal, which the
            // stripped body blanks out — assert on the raw slice instead.
            string body = ExtractMethodBodyRaw(source, "private bool LoadSlotAtPath(");
            Assert.IsNotNull(body, "SaveLoadManager.LoadSlotAtPath not found.");
            Assert.IsTrue(body.Contains("Rebind restore step skipped"),
                "DEF-065: a null bootstrapper at the MarkRestoredFromSave hook must log loudly — "
                + "a silent skip is the only way Rebind's restore work is lost on a load.");
        }

        [Test]
        public void MarkRestoredFromSave_CallsSimulationDriversRebind()
        {
            string source = ReadRepoFile(BootstrapperPath);
            string body = ExtractMethodBody(source, "public void MarkRestoredFromSave(");
            Assert.IsNotNull(body, "FirstLedgerSliceBootstrapper.MarkRestoredFromSave not found.");
            Assert.IsTrue(body.Contains(".Rebind()"),
                "DEF-065: MarkRestoredFromSave must call SimulationDrivers.Rebind — the restore step itself.");
        }

        [Test]
        public void Rebind_RunsSupplierDiagnosticsTaskDefinitionsAndTripRehydration()
        {
            string source = ReadRepoFile(SimulationDriversPath);
            string body = ExtractMethodBody(source, "public void Rebind()");
            Assert.IsNotNull(body, "SimulationDrivers.Rebind not found.");
            Assert.IsTrue(body.Contains("RegisterFuelDealerSuppliers("),
                "DEF-065: Rebind must rebind supplier adapters.");
            Assert.IsTrue(body.Contains("EnsureTaskDurationDiagnostics("),
                "DEF-065: Rebind must re-ensure diagnostics.");
            Assert.IsTrue(body.Contains("RegisterDefinition("),
                "DEF-065: Rebind must register task definitions.");
            Assert.IsTrue(body.Contains("ApplyRestoredShoppingTrips("),
                "DEF-065: Rebind must rehydrate restored shopping trips.");
        }

        [Test]
        public void LoadSlotAtPath_IsTheSingleRebindFunnel()
        {
            string source = ReadRepoFile(SaveLoadManagerPath);
            string code = StripNonCode(source);
            int calls = CountOccurrences(code, "MarkRestoredFromSave(");
            Assert.AreEqual(1, calls,
                "DEF-065: SaveLoadManager must call MarkRestoredFromSave exactly once — "
                + "the single funnel for every load path. A second call site (or none) "
                + "means the load→Rebind chain has been split or dropped; update this test deliberately.");
        }

        [Test]
        public void BodyExtraction_MissingMethod_ReturnsNull()
        {
            string source = "public sealed class X\n{\n    public void Present()\n    {\n        DoWork();\n    }\n}\n";
            Assert.IsNull(ExtractMethodBody(source, "public void Absent("));
        }

        [Test]
        public void BodyExtraction_NestedBracesAndLogStrings_ExtractsBalancedBody()
        {
            string source = "public void Example()\n{\n"
                + "    if (ready)\n    {\n"
                + "        Debug.Log($\"Loaded {name} from {path}.\");\n"
                + "        Run();\n"
                + "    }\n"
                + "}\n";
            string body = ExtractMethodBody(source, "public void Example()");
            Assert.IsNotNull(body);
            Assert.IsTrue(body.Contains("Run();"));
            Assert.IsFalse(body.Contains("public void Example()"));
        }

        [Test]
        public void BodyExtraction_BodyWithoutCall_DoesNotMatch()
        {
            string source = "public void Example()\n{\n    Run();\n}\n";
            string body = ExtractMethodBody(source, "public void Example()");
            Assert.IsNotNull(body);
            Assert.IsFalse(body.Contains("MarkRestoredFromSave("),
                "The extractor must be able to go red: a body without the call must not match it.");
        }
    }
}
