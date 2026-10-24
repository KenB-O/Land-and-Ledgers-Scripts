using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Integration
{
    /// <summary>
    /// G-09/G-10/G-12: machine-checkable unwired-systems registry.
    /// Parses Docs/UnwiredSystems.md (the single source of truth) and asserts
    /// registry-vs-reality in both directions:
    ///  (A) a registered-unwired type must not gain a production caller;
    ///  (B) every known candidate type with no production caller must be registered.
    /// A production caller is a reference to the type name from a .cs file that is
    /// not the type's own file, not under Tests/, not inside the type's owning
    /// directory (intra-feature helpers are part of the same unwired feature),
    /// and not on the entry's allowlist. Comments are stripped before matching.
    /// The repository root comes from LAND_LEDGERS_REPO, falling back to the
    /// default checkout path. A missing registry document fails loudly.
    /// </summary>
    [TestFixture]
    public sealed class UnwiredSystemsRegistryTests
    {
        private sealed class RegistryEntry
        {
            public string TypeName;
            public string SourceFile;
            public string OwningDirectory;
            public readonly List<string> Allowlist = new List<string>();
        }

        // The brief's enumerated candidate set: thirteen detailed business runtimes,
        // the four staged-production types, and StoreRestockService.
        // Pass IX WS6: RealDemandSource added (A3-09) — registered unwired by
        // decision; the candidate list stays authoritative for hand-audited
        // entries, and Pass9RegistryAssemblyScanTests sweeps the rest.
        // Pass XI WS1: BankRuntime added — the thirteenth detailed runtime,
        // previously invisible to both registry tests because its directory
        // sits outside the harness subset.
        private static readonly string[] CandidateTypes =
        {
            "BakeryShopRuntime", "BarberShopRuntime", "DoctorPracticeRuntime",
            "GrainElevatorShopRuntime", "GrainMillShopRuntime", "HotelShopRuntime",
            "LumberYardShopRuntime", "NewspaperRuntime", "RestaurantShopRuntime",
            "SaloonRuntime", "SawmillShopRuntime", "TailorShopRuntime",
            "BankRuntime",
            "StagedProductionProcess", "ProductionDueEventDriver",
            "BakeryStagedProductionChain", "ForgeRepairChain",
            "StoreRestockService",
            "RealDemandSource",
        };

        private static string ResolveRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv) && System.IO.Directory.Exists(fromEnv))
                return System.IO.Path.GetFullPath(fromEnv);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string fallback = System.IO.Path.Combine(home, "workspace", "land-and-ledgers", "repo");
            return fallback;
        }

        private static List<RegistryEntry> ParseRegistry(string repoRoot)
        {
            string docPath = System.IO.Path.Combine(repoRoot, "Docs", "UnwiredSystems.md");
            Assert.IsTrue(System.IO.File.Exists(docPath),
                "UnwiredSystems registry document not found at " + docPath +
                ". Set LAND_LEDGERS_REPO to the repository checkout to check.");
            var entries = new List<RegistryEntry>();
            bool inTable = false;
            foreach (string rawLine in System.IO.File.ReadAllLines(docPath))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("| Type |", StringComparison.Ordinal))
                {
                    inTable = true;
                    continue;
                }
                if (!inTable)
                    continue;
                if (!line.StartsWith("|", StringComparison.Ordinal))
                    break;
                if (line.StartsWith("|---", StringComparison.Ordinal))
                    continue;
                string[] cells = line.Split('|')
                    .Select(c => c.Trim())
                    .Where(c => c.Length > 0)
                    .ToArray();
                if (cells.Length < 4)
                    continue;
                var entry = new RegistryEntry
                {
                    TypeName = cells[0],
                    SourceFile = cells[1].Replace('\\', '/'),
                    OwningDirectory = cells[2].Replace('\\', '/').TrimEnd('/'),
                };
                if (!cells[3].Equals("(none)", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string allowed in cells[3].Split(';'))
                    {
                        string trimmed = allowed.Trim().Replace('\\', '/');
                        if (trimmed.Length > 0)
                            entry.Allowlist.Add(trimmed);
                    }
                }
                entries.Add(entry);
            }
            return entries;
        }

        private sealed class SourceIndex
        {
            public readonly List<KeyValuePair<string, string>> Files =
                new List<KeyValuePair<string, string>>();

            public SourceIndex(string repoRoot)
            {
                foreach (string file in System.IO.Directory.EnumerateFiles(repoRoot, "*.cs", SearchOption.AllDirectories))
                {
                    string rel = file.Substring(repoRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');
                    if (rel.StartsWith(".git/", StringComparison.Ordinal) || rel.Contains("/worktrees/"))
                        continue;
                    string text;
                    try { text = StripComments(System.IO.File.ReadAllText(file)); }
                    catch (IOException) { continue; }
                    Files.Add(new KeyValuePair<string, string>(rel, text));
                }
            }

            private static string StripComments(string text)
            {
                text = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
                string[] lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    int idx = lines[i].IndexOf("//", StringComparison.Ordinal);
                    if (idx >= 0)
                        lines[i] = lines[i].Substring(0, idx);
                }
                return string.Join("\n", lines);
            }
        }

        private static List<string> FindProductionCallers(
            SourceIndex index, string typeName, string ownFile, string ownDir, List<string> allowlist)
        {
            var pattern = new Regex(@"\b" + Regex.Escape(typeName) + @"\b");
            var callers = new List<string>();
            foreach (var file in index.Files)
            {
                string rel = file.Key;
                if (rel.Equals(ownFile, StringComparison.Ordinal))
                    continue;
                if (rel.StartsWith("Tests/", StringComparison.Ordinal))
                    continue;
                if (rel.StartsWith(ownDir + "/", StringComparison.Ordinal))
                    continue;
                if (allowlist != null && allowlist.Contains(rel))
                    continue;
                if (pattern.IsMatch(file.Value))
                    callers.Add(rel);
            }
            return callers;
        }

        private static string FindDeclaringFile(SourceIndex index, string typeName)
        {
            var pattern = new Regex(@"\bclass\s+" + Regex.Escape(typeName) + @"\b");
            foreach (var file in index.Files)
            {
                if (file.Key.StartsWith("Tests/", StringComparison.Ordinal))
                    continue;
                if (pattern.IsMatch(file.Value))
                    return file.Key;
            }
            return null;
        }

        [Test]
        public void Registry_ParsesAndEntriesResolve()
        {
            string repoRoot = ResolveRepoRoot();
            List<RegistryEntry> entries = ParseRegistry(repoRoot);
            Assert.IsNotEmpty(entries, "The registry table parsed to zero entries.");
            var index = new SourceIndex(repoRoot);
            foreach (RegistryEntry entry in entries)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.TypeName), "Blank type name in registry.");
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(repoRoot, entry.SourceFile)),
                    "Registered source file missing: " + entry.SourceFile);
                string declaring = FindDeclaringFile(index, entry.TypeName);
                Assert.IsNotNull(declaring, "Registered type has no class declaration: " + entry.TypeName);
                Assert.AreEqual(entry.SourceFile, declaring,
                    "Registered source file does not declare " + entry.TypeName);
            }
        }

        [Test]
        public void RegisteredTypes_HaveNoProductionCallers()
        {
            string repoRoot = ResolveRepoRoot();
            List<RegistryEntry> entries = ParseRegistry(repoRoot);
            var index = new SourceIndex(repoRoot);
            var violations = new List<string>();
            foreach (RegistryEntry entry in entries)
            {
                List<string> callers = FindProductionCallers(
                    index, entry.TypeName, entry.SourceFile, entry.OwningDirectory, entry.Allowlist);
                foreach (string caller in callers)
                    violations.Add(entry.TypeName + " referenced by " + caller);
            }
            Assert.IsEmpty(violations,
                "Registered-unwired types gained production callers. Either complete the wiring " +
                "deliberately and update Docs/UnwiredSystems.md, or revert the reference:\n" +
                string.Join("\n", violations));
        }

        [Test]
        public void UnwiredCandidates_AreAllRegistered()
        {
            string repoRoot = ResolveRepoRoot();
            List<RegistryEntry> entries = ParseRegistry(repoRoot);
            var registered = new HashSet<string>(entries.Select(e => e.TypeName), StringComparer.Ordinal);
            var byName = entries.ToDictionary(e => e.TypeName, StringComparer.Ordinal);
            var index = new SourceIndex(repoRoot);
            var missing = new List<string>();
            foreach (string candidate in CandidateTypes)
            {
                string declaring = FindDeclaringFile(index, candidate);
                Assert.IsNotNull(declaring, "Candidate type not found in sources: " + candidate);
                string ownDir = declaring.Contains("/")
                    ? declaring.Substring(0, declaring.LastIndexOf('/'))
                    : string.Empty;
                RegistryEntry entry;
                List<string> allowlist = byName.TryGetValue(candidate, out entry) ? entry.Allowlist : null;
                List<string> callers = FindProductionCallers(index, candidate, declaring, ownDir, allowlist);
                if (callers.Count == 0 && !registered.Contains(candidate))
                    missing.Add(candidate);
            }
            Assert.IsEmpty(missing,
                "Unwired types with no production caller are missing from Docs/UnwiredSystems.md. " +
                "Register each with its story:\n" + string.Join("\n", missing));
        }
    }
}
