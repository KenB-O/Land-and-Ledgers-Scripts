using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// C-8 (expanded campaign 2026-11-16): sawmill business authorship, read
// from the authored town config FILE. The harness shim returns null for
// AssetDatabase.LoadAssetAtPath, so the Unity-side
// DevelopmentTownConfigurationTests pin the same facts for Unity while
// these tests pin them in the harness by parsing the .asset text — the
// only harness-visible way to prove what the scenario actually authors.
// Owner-directed (Kennedy 2026-11-16): Enzo Kwint runs the sawmill with
// his wife Emma; Josh Saunders labours for Enzo and boards with the
// Kwints (D-10/D-11 owns his permanent residence). Shane Bryan stays on
// as a sawmill hand under Enzo — never a second sawmill owner. Ages are
// provisional calibrations (flagged in the asset), the owner corrects
// them. Emma Kwint is a distinct person from Emma Baldwin-Ooms.
namespace LandLedgers.Editor.Population
{
    [TestFixture]
    public sealed class SawmillAuthorshipC8Tests
    {
        private const string AssetRelativePath = "Core/Population/DevelopmentTownConfiguration.asset";

        /// <summary>
        /// Resolves the authored config in the tree under test — never the
        /// main-repo fallback: the main repo does not carry this branch's
        /// authorship. Fails loudly when no candidate exists.
        /// </summary>
        private static string ResolveAssetPath()
        {
            var candidates = new List<string>();
            // 1. The harness exports LAND_LEDGERS_REPO=<worktree>/Assets/Scripts.
            string fromEnv = System.Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv))
                candidates.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(fromEnv, "..", AssetRelativePath)));
            // 2. Unity: Application.dataPath is the Assets folder.
            candidates.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, AssetRelativePath)));
            // 3. Harness: Application.dataPath is the worktree root (CWD).
            candidates.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "Assets", AssetRelativePath)));
            // 4. Current directory.
            candidates.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Assets", AssetRelativePath)));
            foreach (string candidate in candidates)
                if (System.IO.File.Exists(candidate)) return candidate;
            Assert.Fail("DevelopmentTownConfiguration.asset not found in the tree under test. Tried:\n"
                + string.Join("\n", candidates));
            return null;
        }

        private static string[] ReadAssetLines()
        {
            return System.IO.File.ReadAllLines(ResolveAssetPath());
        }

        /// <summary>
        /// Parses one top-level list section of the .asset YAML into
        /// field dictionaries. An entry starts with "  - " and its fields
        /// follow at 4-space indent until the next entry or section; a
        /// "memberPersonKeys:" field collects its "- " items as a
        /// comma-joined value. YAML comments are skipped. Only the fields
        /// these tests assert on are read — the shape is the asset's own.
        /// </summary>
        private static List<Dictionary<string, string>> ParseSection(string[] lines, string sectionName)
        {
            var entries = new List<Dictionary<string, string>>();
            bool inSection = false;
            Dictionary<string, string> current = null;
            string currentListKey = null;
            foreach (string raw in lines)
            {
                string line = raw.TrimEnd();
                if (line.Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                if (line.StartsWith("  ", StringComparison.Ordinal) && !line.StartsWith("   ", StringComparison.Ordinal)
                    && line.TrimEnd().EndsWith(":", StringComparison.Ordinal) && !line.TrimStart().StartsWith("-", StringComparison.Ordinal))
                {
                    string key = line.Trim().TrimEnd(':');
                    if (inSection) break;
                    if (key == sectionName) inSection = true;
                    continue;
                }
                if (!inSection) continue;
                if (line.StartsWith("  - ", StringComparison.Ordinal))
                {
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    entries.Add(current);
                    currentListKey = null;
                    line = "    " + line.Substring(4);
                }
                if (current == null || !line.StartsWith("    ", StringComparison.Ordinal)) continue;
                string field = line.Trim();
                if (field.StartsWith("- ", StringComparison.Ordinal))
                {
                    if (currentListKey != null)
                        current[currentListKey] = current[currentListKey].Length == 0
                            ? field.Substring(2).Trim()
                            : current[currentListKey] + "," + field.Substring(2).Trim();
                    continue;
                }
                int colon = field.IndexOf(':');
                if (colon < 0) continue;
                string name = field.Substring(0, colon).Trim();
                string value = field.Substring(colon + 1).Trim();
                if (name == "memberPersonKeys")
                {
                    currentListKey = name;
                    current[name] = string.Empty;
                    continue;
                }
                currentListKey = null;
                current[name] = value;
            }
            return entries;
        }

        private static Dictionary<string, string> SingleEntry(
            List<Dictionary<string, string>> entries, string field, string value, string what)
        {
            var matches = entries.Where(e => e.TryGetValue(field, out string v) && v == value).ToList();
            Assert.AreEqual(1, matches.Count, $"Expected exactly one {what} with {field} '{value}'; found {matches.Count}.");
            return matches[0];
        }

        [Test]
        public void C8_SawmillOwner_IsEnzoKwint_AndShaneOwnsNothing()
        {
            string[] lines = ReadAssetLines();
            var businesses = ParseSection(lines, "businesses");
            Assert.AreEqual(7, businesses.Count, "C-8 adds no business — Enzo takes over the existing sawmill; the seventh is the carpentry workshop from F1 part 2.");

            var sawmill = SingleEntry(businesses, "key", "sawmill", "business");
            Assert.AreEqual("enzo", sawmill["ownerPersonKey"], "Enzo Kwint runs the sawmill (Kennedy 2026-11-16).");
            Assert.AreEqual("15000", sawmill["openingCashCents"], "The business keeps its authored opening cash; only the operator changes.");

            Assert.IsFalse(businesses.Any(b => b.TryGetValue("ownerPersonKey", out string owner) && owner == "shane"),
                "Shane Bryan owns no business — there is exactly one sawmill owner.");

            var cashTargets = ParseSection(lines, "businessCashTargets");
            var sawmillTarget = SingleEntry(cashTargets, "businessKey", "sawmill", "business cash target");
            Assert.AreEqual("15000", sawmillTarget["targetCashCents"]);
        }

        [Test]
        public void C8_KwintHousehold_HoldsEnzoEmmaAndBoarderJosh()
        {
            string[] lines = ReadAssetLines();
            var people = ParseSection(lines, "people");
            Assert.AreEqual(15, people.Count, "Thirteen at C-8 (ten residents before, plus Enzo, Emma Kwint, and Josh), plus Don and Grace Millward from F1 part 2.");

            var enzo = SingleEntry(people, "key", "enzo", "person");
            Assert.AreEqual("Enzo", enzo["firstName"]);
            Assert.AreEqual("Kwint", enzo["lastName"]);
            Assert.AreEqual("40", enzo["age"]);
            Assert.AreEqual("1", enzo["ageIsProvisionalCalibration"], "Enzo's age is a provisional calibration.");
            Assert.AreEqual("0", enzo["isFemale"]);
            Assert.AreEqual("Sawmill owner/operator", enzo["occupation"]);
            Assert.AreEqual("6", enzo["homeBuildingOrdinal"]);

            var emmaKwint = SingleEntry(people, "key", "emma-kwint", "person");
            Assert.AreEqual("Emma", emmaKwint["firstName"]);
            Assert.AreEqual("Kwint", emmaKwint["lastName"]);
            Assert.AreEqual("34", emmaKwint["age"]);
            Assert.AreEqual("1", emmaKwint["ageIsProvisionalCalibration"]);
            Assert.AreEqual("1", emmaKwint["isFemale"]);
            Assert.AreEqual("Unspecified", emmaKwint["occupation"]);
            Assert.AreEqual("6", emmaKwint["homeBuildingOrdinal"]);

            var josh = SingleEntry(people, "key", "josh", "person");
            Assert.AreEqual("Josh", josh["firstName"]);
            Assert.AreEqual("Saunders", josh["lastName"]);
            Assert.AreEqual("26", josh["age"]);
            Assert.AreEqual("1", josh["ageIsProvisionalCalibration"]);
            Assert.AreEqual("0", josh["isFemale"]);
            Assert.AreEqual("Sawmill labourer", josh["occupation"]);
            Assert.AreEqual("6", josh["homeBuildingOrdinal"], "Josh boards in the Kwint home.");

            var households = ParseSection(lines, "households");
            Assert.AreEqual(8, households.Count, "Seven at C-8 (six households before, plus the Kwints'), plus the Millwards' from F1 part 2.");
            var kwint = SingleEntry(households, "key", "enzo-emma", "household");
            Assert.AreEqual("Kwint", kwint["surname"]);
            Assert.AreEqual("enzo,emma-kwint,josh", kwint["memberPersonKeys"]);
            Assert.AreEqual("6", kwint["homeBuildingOrdinal"]);
            Assert.AreEqual("0", kwint["openingCashCents"]);

            var relationships = ParseSection(lines, "relationships");
            Assert.AreEqual(7, relationships.Count);
            Assert.IsTrue(relationships.Any(r =>
                r.TryGetValue("firstPersonKey", out string first) && first == "enzo"
                && r.TryGetValue("secondPersonKey", out string second) && second == "emma-kwint"
                && r.TryGetValue("relationship", out string rel) && rel == "0"),
                "Enzo and Emma Kwint are spouses (relationship 0).");
        }

        [Test]
        public void C8_SawmillCrew_SilasShaneJosh_EnzoIsNotAnEmployee()
        {
            string[] lines = ReadAssetLines();
            var employees = ParseSection(lines, "openingEmployees");
            Assert.AreEqual(3, employees.Count, "One mill hand before C-8 (Silas); Shane and Josh join the crew.");

            var silas = SingleEntry(employees, "personKey", "silas", "opening employee");
            Assert.AreEqual("sawmill", silas["businessKey"]);
            Assert.AreEqual("logger", silas["role"]);
            Assert.AreEqual("750", silas["weeklyWageCents"]);

            var shane = SingleEntry(employees, "personKey", "shane", "opening employee");
            Assert.AreEqual("sawmill", shane["businessKey"]);
            Assert.AreEqual("logger", shane["role"], "Shane stays on as a logger under the new owner.");
            Assert.AreEqual("750", shane["weeklyWageCents"], "The miller precedent wage (Silas, 750c/week).");

            var josh = SingleEntry(employees, "personKey", "josh", "opening employee");
            Assert.AreEqual("sawmill", josh["businessKey"]);
            Assert.AreEqual("labourer", josh["role"], "Josh labours for Enzo.");
            Assert.AreEqual("750", josh["weeklyWageCents"]);

            Assert.IsFalse(employees.Any(e => e.TryGetValue("personKey", out string person) && person == "enzo"),
                "The owner is never his own opening employee (Validate forbids it).");

            var people = ParseSection(lines, "people");
            var shanePerson = SingleEntry(people, "key", "shane", "person");
            Assert.AreEqual("Sawmill hand", shanePerson["occupation"], "Shane is employed now, not seeking work.");
        }

        [Test]
        public void C8_TwoEmmas_AreDistinctPeople_AndKwintHouseholdHasCashTarget()
        {
            string[] lines = ReadAssetLines();
            var people = ParseSection(lines, "people");
            var emmaBaldwinOoms = SingleEntry(people, "key", "base:1", "person");
            Assert.AreEqual("Emma", emmaBaldwinOoms["firstName"]);
            Assert.AreEqual("Baldwin-Ooms", emmaBaldwinOoms["lastName"]);
            var emmaKwint = SingleEntry(people, "key", "emma-kwint", "person");
            Assert.AreEqual("Emma", emmaKwint["firstName"]);
            Assert.AreEqual("Kwint", emmaKwint["lastName"], "Same first name, different person — full names and distinct ids everywhere.");

            var households = ParseSection(lines, "households");
            var baldwinOoms = SingleEntry(households, "key", "base-household:0", "household");
            var kwint = SingleEntry(households, "key", "enzo-emma", "household");
            Assert.AreNotEqual(baldwinOoms["memberPersonKeys"], kwint["memberPersonKeys"]);
            Assert.IsFalse(kwint["memberPersonKeys"].Split(',').Contains("base:1"),
                "Emma Baldwin-Ooms is not a member of the Kwint household.");

            var cashTargets = ParseSection(lines, "householdCashTargets");
            Assert.AreEqual(8, cashTargets.Count, "Seven at C-8 (six household targets before, plus the Kwints'), plus the Millwards' from F1 part 2.");
            var kwintTarget = SingleEntry(cashTargets, "anchorPersonKey", "enzo", "household cash target");
            Assert.AreEqual("40000", kwintTarget["targetCashCents"]);
            var personKeys = new HashSet<string>(people.Select(p => p["key"]), StringComparer.OrdinalIgnoreCase);
            Assert.IsTrue(cashTargets.All(t => t.TryGetValue("anchorPersonKey", out string anchor) && personKeys.Contains(anchor)),
                "Every household cash target anchors on a configured person.");
        }
    }
}
