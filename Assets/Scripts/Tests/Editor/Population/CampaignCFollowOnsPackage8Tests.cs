// Campaign C, package 8 — three small save/load follow-ons, Nov 15, 2026.
//
//  (1) D3 ordering wrinkle (DEF-008 follow-on, per watchdog E-08): on a
//      first Continue the feed registration at SaveLoadManager :455 records
//      the feed catalog item into Review2VerticalD3SaveState.RuntimeItems,
//      and the D3 restore at :554 clear-then-loads that list, dropping the
//      just-recorded item from the save record (live catalog unaffected,
//      self-healing next fresh session). The wrinkle does NOT reproduce in
//      this lane — the C tree registers no catalog item at :455 — and the
//      only in-tree fix candidate (Restore merging instead of clearing)
//      changes the documented clear-then-load contract for ALL runtime
//      items, which is a broader change: documented, not forced. The tests
//      below pin the mechanism so a future fix attempt has a signal.
//  (2) DEF-045 follow-on: loads apply the saved time settings to a runtime
//      copy of the authored asset (never the asset), and a New Game restores
//      the authored asset.
//  (3) E-06 hardening: DriveTaskPruning refuses to prune — loudly, once —
//      when no live-trip predicate is installed, instead of pruning
//      unguarded.
//
// Two test kinds, same file:
//  (1) One real unit test for the copy semantics the TimeManager wiring
//      relies on (apply to a copy leaves the authored instance untouched).
//  (2) Structural tests pinning the Unity-bound wiring by reading the
//      production files — TimeManager, SimulationDrivers, SaveLoadManager
//      and the D3 save state are too large a closure for the focused
//      harness, so the wiring itself (not Unity behaviour) is what is
//      asserted here, exactly like the package-2/3/4/5/7 tests.
// The repo root comes from LAND_LEDGERS_REPO, then a walk up from the
// current directory and the test assembly location (campaign layout:
// <root>/work-c/Assets/...). A missing repo fails loudly, never silently
// passes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    [TestFixture]
    public sealed class CampaignCFollowOnsPackage8Tests
    {
        private const string SaveLoadManagerPath =
            "Assets/Scripts/Infrastructure/Persistence/SaveLoadManager.cs";
        private const string D3SavePath =
            "Assets/Scripts/Infrastructure/Persistence/Review2VerticalD3Save.cs";
        private const string BootstrapperPath =
            "Assets/Scripts/Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs";
        private const string TimeManagerPath =
            "Assets/Scripts/Core/Time/TimeManager.cs";
        private const string DriversPath =
            "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";

        // ---------- (2) Copy semantics (pure) ----------

        private static SimulationTimeSettings NonDefaultSettings()
        {
            SimulationTimeSettings settings = new SimulationTimeSettings();
            settings.realSecondsPerGameDayAt1x = 720f;
            settings.shortTickIntervalGameMinutes = 30f;
            settings.speed1xMultiplier = 1f;
            settings.speed2xMultiplier = 2f;
            settings.speed4xMultiplier = 3f;
            settings.speed5xMultiplier = 5f;
            settings.speed6xMultiplier = 6f;
            settings.speed10xMultiplier = 9f;
            settings.speed25xMultiplier = 24f;
            settings.speed50xMultiplier = 49f;
            settings.speed100xMultiplier = 90f;
            settings.speed250xMultiplier = 200f;
            settings.speed500xMultiplier = 400f;
            settings.maxShortTicksPerFrame = 32;
            settings.maxDayRolloversPerFrame = 3;
            return settings;
        }

        [Test]
        public void ApplyToCopyLeavesAuthoredUntouched()
        {
            // The wiring clones the authored asset and applies the snapshot
            // to the clone; this pins the snapshot half of that contract —
            // applying to one instance never touches another.
            SimulationTimeSettings authored = NonDefaultSettings();
            SimulationTimeSettings saved = NonDefaultSettings();
            saved.realSecondsPerGameDayAt1x = 1440f;
            saved.maxShortTicksPerFrame = 8;
            TimeSettingsSnapshot snapshot = TimeSettingsSnapshot.Capture(saved);
            SimulationTimeSettings copy = NonDefaultSettings();

            List<string> differing = snapshot.ApplyTo(copy);

            Assert.AreEqual(720f, authored.realSecondsPerGameDayAt1x, 0.0001f);
            Assert.AreEqual(32, authored.maxShortTicksPerFrame);
            Assert.AreEqual(1440f, copy.realSecondsPerGameDayAt1x, 0.0001f);
            Assert.AreEqual(8, copy.maxShortTicksPerFrame);
            Assert.IsTrue(differing.Contains("realSecondsPerGameDayAt1x"));
            Assert.IsTrue(differing.Contains("maxShortTicksPerFrame"));
        }

        // ---------- (1) D3 ordering wrinkle: mechanism ----------

        [Test]
        public void LoadOrdersRegistrationBeforeD3Restore()
        {
            // SaveLoadManager.LoadSlotAtPath registers procurement (the
            // :455 call) before the Pass XL restore (the :554 call) that
            // runs the D3 clear-then-load. That order is the wrinkle: any
            // catalog item recorded at :455 is dropped from the save record
            // by the restore. If this order ever changes, the wrinkle's
            // premise has changed and this test must be revisited.
            string code = StripNonCode(ReadRepoFile(SaveLoadManagerPath));
            int register = code.IndexOf("bootstrapper.EnsureNotionsProcurementForRestore();", StringComparison.Ordinal);
            int restore = code.IndexOf("PassXlSliceSaveCoordinator.Restore(", StringComparison.Ordinal);
            Assert.IsTrue(register >= 0, "LoadSlotAtPath must call EnsureNotionsProcurementForRestore");
            Assert.IsTrue(restore >= 0, "LoadSlotAtPath must call PassXlSliceSaveCoordinator.Restore");
            Assert.IsTrue(register < restore,
                "Procurement registration must run before the Pass XL / D3 restore (D3 ordering wrinkle premise)");
        }

        [Test]
        public void D3RestoreClearsRuntimeItems()
        {
            // Review2VerticalD3SaveState.Restore is clear-then-load: the
            // Clear below is what drops a catalog item recorded during this
            // same load. Pinning it so the contract change (merge instead of
            // clear) is a deliberate, visible decision, not a silent drift.
            string code = StripNonCode(ReadRepoFile(D3SavePath));
            string restore = MethodBody(code, "public void Restore(Review2VerticalD3SaveDto dto, List<string> diag)");
            Assert.IsTrue(restore.Contains("RuntimeItems.Clear();"),
                "D3 Restore must clear-then-load RuntimeItems (documented contract; see package-8 report)");
        }

        [Test]
        public void ContinueRestoreRegistersNoCatalogItemInThisLane()
        {
            // In THIS lane (C branch) EnsureNotionsProcurementForRestore
            // registers only the notions counterparty — no catalog item is
            // recorded at :455, so the D3 ordering wrinkle cannot reproduce
            // here. The wrinkle needs Agent B's RegisterReleaseHorseFeed
            // (commit 648a4bc7), which records the feed catalog item via
            // review2D3SaveState.RegisterRuntimeItem. If this lane ever
            // gains a catalog registration on the restore path, this test
            // goes red and the wrinkle must be re-examined.
            string code = StripNonCode(ReadRepoFile(BootstrapperPath));
            string restore = MethodBody(code, "public void EnsureNotionsProcurementForRestore()");
            Assert.IsTrue(restore.Contains("RegisterNotionsProcurement"),
                "EnsureNotionsProcurementForRestore must call RegisterNotionsProcurement (extraction sanity)");
            Assert.IsFalse(restore.Contains("RegisterRuntimeItem"),
                "EnsureNotionsProcurementForRestore must not register a catalog item in this lane (D3 wrinkle premise)");
        }

        // ---------- (2) Runtime copy + New Game reset (structural) ----------

        [Test]
        public void LoadFromSaveDtoClonesSettingsBeforeApplying()
        {
            string raw = ReadRepoFile(TimeManagerPath);
            string code = StripNonCode(raw);
            string load = MethodBody(code, "public void LoadFromSaveDto(TimeSaveDto dto)");
            Assert.IsTrue(load.Contains("authoredSettings = settings"),
                "LoadFromSaveDto must capture the authored asset before cloning it");
            Assert.IsTrue(load.Contains("Instantiate(authoredSettings)"),
                "LoadFromSaveDto must clone the authored settings instead of mutating the asset");
            Assert.IsTrue(load.Contains("HideFlags.DontSave"),
                "The runtime settings copy must be marked DontSave (TownWorldController precedent)");
            Assert.IsTrue(raw.Contains("(Runtime Time Copy)"),
                "The runtime settings copy must be named as a runtime copy");
            int capture = load.IndexOf("authoredSettings = settings", StringComparison.Ordinal);
            int clone = load.IndexOf("Instantiate(authoredSettings)", StringComparison.Ordinal);
            int apply = load.IndexOf("ApplyTo(settings)", StringComparison.Ordinal);
            Assert.IsTrue(capture >= 0 && clone > capture && apply > clone,
                "LoadFromSaveDto must capture, then clone, then apply — in that order");
        }

        [Test]
        public void StartNewScenarioRestoresAuthoredSettings()
        {
            string code = StripNonCode(ReadRepoFile(TimeManagerPath));
            string start = MethodBody(code, "public bool StartNewScenarioAtDate(int year, int month, int day)");
            Assert.IsTrue(start.Contains("settings = authoredSettings"),
                "StartNewScenarioAtDate must restore the authored settings so a New Game after a Continue does not inherit restored values");
            int reset = start.IndexOf("settings = authoredSettings", StringComparison.Ordinal);
            int sanitize = start.IndexOf("settings.Sanitize()", StringComparison.Ordinal);
            Assert.IsTrue(reset >= 0 && sanitize > reset,
                "The authored-settings reset must run before Sanitize in StartNewScenarioAtDate");
        }

        [Test]
        public void AuthoredSettingsFieldIsRuntimeOnly()
        {
            string code = ReadRepoFile(TimeManagerPath);
            Assert.IsTrue(code.Contains("private SimulationTimeSettings authoredSettings;"),
                "TimeManager must keep the authored settings asset in a runtime-only field");
            int field = code.IndexOf("private SimulationTimeSettings authoredSettings;", StringComparison.Ordinal);
            string before = code.Substring(Math.Max(0, field - 400), Math.Min(400, field));
            Assert.IsFalse(before.Contains("[SerializeField]"),
                "authoredSettings must never be serialized (it exists only to restore the asset on New Game)");
        }

        // ---------- (3) Null-predicate loud failure (structural) ----------

        [Test]
        public void DriveTaskPruningRefusesWithoutPredicate()
        {
            string raw = ReadRepoFile(DriversPath);
            string code = StripNonCode(raw);
            string prune = MethodBody(code, "private void DriveTaskPruning(int absoluteDayIndex)");
            Assert.IsTrue(prune.Contains("IsTaskExternallyReferenced == null"),
                "DriveTaskPruning must check for a missing live-trip predicate (E-06 hardening)");
            Assert.IsTrue(prune.Contains("Debug.LogError"),
                "DriveTaskPruning must fail LOUDLY when the predicate is missing, not prune silently");
            Assert.IsTrue(raw.Contains("no live-trip external-reference predicate installed"),
                "The loud error must name the missing predicate");
            int guard = prune.IndexOf("IsTaskExternallyReferenced == null", StringComparison.Ordinal);
            int call = prune.IndexOf("PruneTerminalTasks(absoluteDayIndex)", StringComparison.Ordinal);
            Assert.IsTrue(guard >= 0 && call > guard,
                "The null-predicate guard must run before PruneTerminalTasks is called");
        }

        [Test]
        public void DriveTaskPruningLogsMissingPredicateOnce()
        {
            // DEF-023 pattern: one error when the failure is newly observed,
            // then silent until a guarded prune actually runs (which resets
            // the flag, so a later regression re-logs).
            string code = StripNonCode(ReadRepoFile(DriversPath));
            string prune = MethodBody(code, "private void DriveTaskPruning(int absoluteDayIndex)");
            Assert.IsTrue(prune.Contains("taskPruneMissingPredicateWarningLogged"),
                "DriveTaskPruning must gate the loud error on a sticky once-only flag");
            Assert.IsTrue(prune.Contains("taskPruneMissingPredicateWarningLogged = true;"),
                "The flag must be set when the missing-predicate error is logged");
            Assert.IsTrue(prune.Contains("taskPruneMissingPredicateWarningLogged = false;"),
                "The flag must reset when a guarded prune runs");
            int guard = prune.IndexOf("IsTaskExternallyReferenced == null", StringComparison.Ordinal);
            int set = prune.IndexOf("taskPruneMissingPredicateWarningLogged = true;", StringComparison.Ordinal);
            int reset = prune.IndexOf("taskPruneMissingPredicateWarningLogged = false;", StringComparison.Ordinal);
            int call = prune.IndexOf("PruneTerminalTasks(absoluteDayIndex)", StringComparison.Ordinal);
            Assert.IsTrue(guard >= 0 && set > guard,
                "The once-only flag must be set inside the null-predicate branch, not elsewhere");
            Assert.IsTrue(set >= 0 && reset > set && call > reset,
                "The flag must reset before PruneTerminalTasks runs, so a later missing-predicate failure re-logs");
        }

        [Test]
        public void DriveTaskPruningStillPrunesWhenPredicateInstalled()
        {
            // The hardening is fail-closed, not prune-removed: with a
            // predicate installed the daily prune still runs.
            string code = StripNonCode(ReadRepoFile(DriversPath));
            string prune = MethodBody(code, "private void DriveTaskPruning(int absoluteDayIndex)");
            Assert.IsTrue(prune.Contains("hub.Tasks.PruneTerminalTasks(absoluteDayIndex)"),
                "DriveTaskPruning must still call PruneTerminalTasks when the predicate is installed");
            Assert.IsTrue(prune.Contains("hub?.Tasks == null"),
                "DriveTaskPruning must keep its existing null-hub early return");
        }

        // ---------- Repo helpers (C-5 / C-7 precedent) ----------

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
                Directory.GetCurrentDirectory(),
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
                    DirectoryInfo parent = Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign C (package 8): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign C (package 8): expected repo file missing: " + full);
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
                            continue;
                        }
                        if (source[i] == '"') break;
                        out_.Append(source[i] == '\n' ? '\n' : ' ');
                        i++;
                    }
                    if (i < source.Length)
                    {
                        out_.Append(' ');
                        i++;
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
                            continue;
                        }
                        if (source[i] == '"') break;
                        out_.Append(' ');
                        i++;
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
                    while (i < source.Length && source[i] != '\n')
                    {
                        if (source[i] == '\\' && i + 1 < source.Length)
                        {
                            out_.Append("  ");
                            i += 2;
                            continue;
                        }
                        if (source[i] == '\'') break;
                        out_.Append(' ');
                        i++;
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

        /// <summary>
        /// Returns the brace-matched body of the first method whose
        /// signature line starts with <paramref name="signature"/>, from
        /// code already stripped by StripNonCode. Fails the test when the
        /// method is not found.
        /// </summary>
        internal static string MethodBody(string strippedCode, string signature)
        {
            int sig = strippedCode.IndexOf(signature, StringComparison.Ordinal);
            Assert.IsTrue(sig >= 0, "Campaign C (package 8): method not found: " + signature);
            int open = strippedCode.IndexOf('{', sig);
            Assert.IsTrue(open >= 0, "Campaign C (package 8): method has no body: " + signature);
            int depth = 0;
            for (int i = open; i < strippedCode.Length; i++)
            {
                if (strippedCode[i] == '{') depth++;
                else if (strippedCode[i] == '}')
                {
                    depth--;
                    if (depth == 0) return strippedCode.Substring(open, i - open + 1);
                }
            }
            Assert.IsTrue(false, "Campaign C (package 8): unbalanced braces in method: " + signature);
            return null;
        }
    }
}
