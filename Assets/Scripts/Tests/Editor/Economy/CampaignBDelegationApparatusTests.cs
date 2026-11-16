using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LandLedgers.Economy.Delegation;
using LandLedgers.Economy.Labor;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>CAMPAIGN-B (DEF-051), package 7: the delegation apparatus is inert.
    ///
    /// VERDICT — uninvoked apparatus, NOT a mechanical break. The whole chain
    /// exists and is wired: DelegationService grants (GrantAuthority), the
    /// standing-instruction register (StandingInstructionRegister), the
    /// ManagerBrain cycle (DailyCycle/WeeklyCycle, all seven act executors),
    /// and the execution service (ManagerDecisionExecution, OpenRecruitment
    /// calling back into ExecuteManagerRecruitment). The manager cycle in
    /// DriveAutonomousLabor is gated on hub.Delegation.GetActiveAuthority
    /// returning a grant — and GrantAuthority has ZERO production callers, so
    /// the loop `continue`s for every worker on every business, every day.
    /// No StandingInstruction is ever constructed in production; the only
    /// production Register call is ManagerBrain.SyncInstructions, which only
    /// re-registers instructions already present in the cycle inputs (which
    /// come from the same register) — a closed loop: instructions can only
    /// enter from a save file that already has them.
    ///
    /// There is a SECOND, parallel delegation system that never meets the
    /// first: BusinessControlState (PlayerManaged/Assisted/ManagerRun) with
    /// ManagerPolicy presets. NPC-owned businesses default to ManagerRun and
    /// are forced out of PlayerManaged by SanitizeControlState — and that
    /// layer is LIVE (reorder thresholds, budgets, reserves, margins). But
    /// no production code connects ControlState to GrantAuthority, and a
    /// business carries no manager-person field at all (no ManagerPersonId
    /// on BusinessInstanceState), so a bridge would have to invent the
    /// delegate person, the scopes, the purchase limit, and the skill gate.
    /// That is an owner decision, not a code fix. This package therefore
    /// fixes what is mechanically fixable — the wrong Canon citation
    /// (F7-15: code cites "Canon CE1-13.2"; the verified source is
    /// Canon XXXI Part IX §§9.1-9.6 + §5.2 — B-7 corrected it to
    /// Part V §§5.2/9.1, and the v3.2 intake (Nov 15, 2026) corrected
    /// the part label to Part IX, verified against the v3.2 Canon text;
    /// B-14 applies the re-label) — and pins the inert state with tests
    /// that fail loudly if it ever changes without an explicit decision.
    ///
    /// The package-1 hiring service agrees with the apparatus: its
    /// "Staffing delegate" test IS the grant check (GetActiveAuthority +
    /// Staffing scope), so it activates automatically if grants ever exist;
    /// the NPC-owner fallback is what hires today. The offer predicate is
    /// likewise live for owners and dead for delegates without grants.
    ///
    /// What the Roslyn harness can reach: the apparatus semantics (real
    /// execution of DelegationService + StandingInstructionRegister) and the
    /// wiring (structural reads of the repo — the driver and the brain are
    /// too large a closure for the focused harness, same class as the
    /// package-2/5/6 wiring). A live manager cycle still needs a grant to
    /// exist first, which is an owner decision; no Unity run can observe
    /// one today.</summary>
    [TestFixture]
    public sealed class CampaignBDelegationApparatusTests
    {
        private const string DelegationServicePath =
            "Assets/Scripts/Domains/Economy/Delegation/DelegationService.cs";
        private const string StandingInstructionPath =
            "Assets/Scripts/Domains/Economy/Labor/StandingInstruction.cs";
        private const string ManagerBrainPath =
            "Assets/Scripts/Domains/Economy/Labor/ManagerBrain.cs";
        private const string DriversPath =
            "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";
        private const string HiringServicePath =
            "Assets/Scripts/Domains/Economy/Labor/DelegatedHiringService.cs";

        private const string StoreId = "general_store_001";
        private const string BakeryId = "bakery_001";

        // ---------- (1) The production state: a fresh service holds nothing ----------

        [Test]
        public void FreshService_HoldsNoAuthorityForAnyWorker()
        {
            var service = new DelegationService();
            Assert.IsNull(service.GetActiveAuthority(StoreId, 1, 0));
            Assert.IsNull(service.GetActiveAuthority(BakeryId, 3, 10));
            Assert.IsNull(service.GetActiveAuthority(StoreId, 5, 100));
            Assert.AreEqual(0, service.Decisions.Count);
        }

        [Test]
        public void FreshService_HireStaffCheckDenied()
        {
            // The delegate branch of the hub's offer predicate
            // (OfferAuthorityPredicate: owner OR CheckAct(HireStaff) ==
            // Permitted) is dead in the production state: with zero grants,
            // a non-owner delegate can never be permitted to hire.
            var service = new DelegationService();
            AuthorityVerdict verdict = service.CheckAct(
                StoreId, 2, DelegatedAct.HireStaff, 0, 0, new List<string>());
            Assert.AreEqual(AuthorityVerdict.Denied, verdict);
            Assert.AreEqual(1, service.Decisions.Count);
            Assert.AreEqual(AuthorityVerdict.Denied, service.Decisions[0].Verdict);
        }

        [Test]
        public void FreshRegister_HoldsNoInstructions()
        {
            var register = new StandingInstructionRegister();
            Assert.AreEqual(0, register.Count);
            Assert.AreEqual(0, register.ForBusiness(StoreId).Count);
            Assert.IsFalse(register.IsActProhibited(StoreId, "OpenRecruitment"));
        }

        // ---------- (2) The apparatus semantics the hiring service relies on ----------

        [Test]
        public void GrantWithStaffingScope_PermitsHireStaff()
        {
            // The package-1 hiring service's "Staffing delegate" test is
            // exactly this: GetActiveAuthority + Staffing scope. If grants
            // ever exist, the hiring path activates without a code change.
            var service = new DelegationService();
            DelegatedAuthority authority = Grant(service, StoreId, 2, DelegationScope.AllRoutine);
            Assert.IsNotNull(authority);
            Assert.IsNotNull(service.GetActiveAuthority(StoreId, 2, 0));
            AuthorityVerdict verdict = service.CheckAct(
                StoreId, 2, DelegatedAct.HireStaff, 0, 0, new List<string>());
            Assert.AreEqual(AuthorityVerdict.Permitted, verdict);
        }

        [Test]
        public void GrantWithoutStaffingScope_DeniesHireStaff()
        {
            var service = new DelegationService();
            DelegatedAuthority authority = Grant(service, StoreId, 2, DelegationScope.Purchasing);
            Assert.IsNotNull(authority);
            AuthorityVerdict verdict = service.CheckAct(
                StoreId, 2, DelegatedAct.HireStaff, 0, 0, new List<string>());
            Assert.AreEqual(AuthorityVerdict.Denied, verdict);
        }

        [Test]
        public void ReservedAct_EscalatesEvenWithGrant()
        {
            // The owner-reserved boundary holds no matter how broad the
            // grant: SellBusiness escalates even under AllRoutine.
            var service = new DelegationService();
            Assert.IsNotNull(Grant(service, StoreId, 2, DelegationScope.AllRoutine));
            AuthorityVerdict verdict = service.CheckAct(
                StoreId, 2, DelegatedAct.SellBusiness, 0, 0, new List<string>());
            Assert.AreEqual(AuthorityVerdict.Escalated, verdict);
        }

        [Test]
        public void OwnerLockedInstruction_ProhibitsAct()
        {
            var register = new StandingInstructionRegister();
            var locked = new StandingInstruction
            {
                InstructionId = "lock-1",
                BusinessInstanceId = StoreId,
                Kind = InstructionKind.OwnerLocked,
                TargetActKind = "OpenRecruitment",
                Objective = "Never recruit without the owner.",
            };
            Assert.IsNull(register.Register(locked, new List<string>()));
            Assert.AreEqual(1, register.ForBusiness(StoreId).Count);
            Assert.IsTrue(register.IsActProhibited(StoreId, "OpenRecruitment"));
            Assert.IsFalse(register.IsActProhibited(StoreId, "Procure"));
            Assert.IsFalse(register.IsActProhibited(BakeryId, "OpenRecruitment"));
        }

        [Test]
        public void DoOnceInstruction_ConsumedAfterUse()
        {
            var register = new StandingInstructionRegister();
            var once = new StandingInstruction
            {
                InstructionId = "once-1",
                BusinessInstanceId = StoreId,
                Kind = InstructionKind.DoOnce,
                TargetActKind = "Procure",
            };
            Assert.IsNull(register.Register(once, new List<string>()));
            Assert.IsTrue(once.TargetsAct("Procure"));
            Assert.IsNull(register.ConsumeDoOnce("once-1", 5, new List<string>()));
            Assert.IsFalse(once.TargetsAct("Procure"));
            Assert.IsNotNull(register.ConsumeDoOnce("once-1", 6, new List<string>()));
        }

        // ---------- (3) Structural pins: the break points, named ----------

        [Test]
        public void GrantAuthority_HasZeroProductionCallers()
        {
            // The first break (F7-01): nobody ever grants authority in
            // production. The only production file mentioning GrantAuthority
            // in code is DelegationService.cs itself (the definition).
            var holders = new List<string>();
            foreach (string file in ProductionCsFiles())
            {
                string code = StripNonCode(File.ReadAllText(file));
                if (code.Contains("GrantAuthority"))
                {
                    holders.Add(Relative(file));
                }
            }
            Assert.AreEqual(1, holders.Count, "Files mentioning GrantAuthority in code: " + string.Join(", ", holders));
            Assert.AreEqual(DelegationServicePath, holders[0]);
        }

        [Test]
        public void NoProductionConstructionOfStandingInstruction()
        {
            // The second break (F7-04): no standing instruction is ever
            // constructed in production. (The DTO type
            // StandingInstructionSaveDto is a different name; the
            // word-boundary check keeps it out.)
            foreach (string file in ProductionCsFiles())
            {
                string code = StripNonCode(File.ReadAllText(file));
                Assert.IsFalse(ContainsWord(code, "new StandingInstruction"),
                    "Production construction of StandingInstruction in " + Relative(file));
            }
        }

        [Test]
        public void ManagerActKinds_HasNoHireKind()
        {
            // Hiring itself is not a manager decision in code: a manager
            // can at most open recruitment. The Hire half lives in the
            // package-1 hiring service (NPC owner / Staffing delegate).
            string source = ReadRepoFile(ManagerBrainPath);
            int start = source.IndexOf("public static class ManagerActKinds", StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "ManagerActKinds not found in ManagerBrain.cs.");
            int end = source.IndexOf("public static class ManagerScopeNames", start, StringComparison.Ordinal);
            Assert.IsTrue(end > start, "ManagerScopeNames not found after ManagerActKinds.");
            string kinds = StripNonCode(source.Substring(start, end - start));
            Assert.IsFalse(kinds.Contains("Hire"),
                "ManagerActKinds unexpectedly contains a Hire kind: " + kinds);
            Assert.IsTrue(kinds.Contains("OpenRecruitment"), "ManagerActKinds lost OpenRecruitment.");
        }

        [Test]
        public void InstructionRegister_OnlyProductionCallerIsSyncInstructions()
        {
            // The closed loop (F7-04): the only production call that adds
            // an instruction to the register is ManagerBrain.SyncInstructions,
            // which only re-registers instructions already in the cycle
            // inputs — which come from the same register. Instructions can
            // only enter from a save file that already has them.
            var holders = new List<string>();
            int occurrences = 0;
            foreach (string file in ProductionCsFiles())
            {
                string code = StripNonCode(File.ReadAllText(file));
                int count = CountOccurrences(code, "instructionRegister.Register");
                if (count > 0)
                {
                    holders.Add(Relative(file));
                    occurrences += count;
                }
            }
            Assert.AreEqual(1, holders.Count, "Files calling instructionRegister.Register: " + string.Join(", ", holders));
            Assert.AreEqual(ManagerBrainPath, holders[0]);
            Assert.AreEqual(1, occurrences, "instructionRegister.Register should occur exactly once (SyncInstructions).");
        }

        [Test]
        public void NoBridgeFromControlStateToGrants()
        {
            // The two delegation systems never meet: the live policy layer
            // (BusinessControlState.ManagerRun for NPC businesses) never
            // issues a grant, and the authority layer never reads the
            // policy. No production file mentions both in code.
            foreach (string file in ProductionCsFiles())
            {
                string code = StripNonCode(File.ReadAllText(file));
                Assert.IsFalse(code.Contains("ControlState") && code.Contains("GrantAuthority"),
                    "A ControlState-to-grant bridge exists in " + Relative(file)
                    + " — the DEF-051 verdict (uninvoked apparatus) no longer holds.");
            }
        }

        [Test]
        public void DelegatedHiringService_StaffingDelegateIsTheGrantCheck()
        {
            // The package-1 agreement, pinned — with the check in the
            // right place. The grant check lives in the driver's
            // DriveDelegatedHiring, not in the service: it calls
            // GetActiveAuthority and requires the Staffing scope before
            // naming a candidate a StaffingDelegate; the service then
            // consumes the HiringAuthorityBasis it is handed. (An earlier
            // draft of this test asserted the check inside the service
            // file — wrong location; the driver is where the authority
            // is actually read.)
            string driver = StripNonCode(ReadRepoFile(DriversPath));
            int at = driver.IndexOf("void DriveDelegatedHiring(int absoluteDayIndex)", StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, "DriveDelegatedHiring definition not found in SimulationDrivers.cs.");
            string region = driver.Substring(at, Math.Min(9000, driver.Length - at));
            Assert.IsTrue(region.Contains("GetActiveAuthority"),
                "DriveDelegatedHiring no longer checks GetActiveAuthority.");
            Assert.IsTrue(region.Contains("DelegationScope.Staffing"),
                "DriveDelegatedHiring no longer requires the Staffing scope.");
            Assert.IsTrue(region.Contains("HiringAuthorityBasis.StaffingDelegate"),
                "DriveDelegatedHiring no longer names the StaffingDelegate basis.");
            string service = StripNonCode(ReadRepoFile(HiringServicePath));
            Assert.IsTrue(service.Contains("StaffingDelegate"),
                "DelegatedHiringService no longer consumes the StaffingDelegate basis.");
        }

        [Test]
        public void ManagerCycle_GatedOnActiveAuthority()
        {
            // The gate itself, pinned: exactly two production call sites
            // read GetActiveAuthority — the manager cycle and the delegated
            // hiring delegate check. Both are dormant without grants.
            string code = StripNonCode(ReadRepoFile(DriversPath));
            Assert.AreEqual(2, CountOccurrences(code, "GetActiveAuthority("),
                "Expected exactly 2 GetActiveAuthority call sites in SimulationDrivers.cs.");
        }

        [Test]
        public void CanonCitation_CorrectedToPartIX()
        {
            // F7-15: the code cited "Canon CE1-13.2" for the three modes;
            // §13.2 covers other topics. B-7 corrected it to Part V
            // §§5.2/9.1; the v3.2 intake (Nov 15, 2026) corrected the
            // part label: the delegation doctrine is Canon XXXI Part IX
            // §§9.1-9.6 + §5.2 (§9.1 is in Part IX; §5.2 is in Part V),
            // verified directly against the v3.2 Canon text. B-14 applies
            // the re-label (the one remaining "Part V §5.2" citation, on
            // the Objective field, is correct as-is — §5.2 is in Part V —
            // and is not part of this assertion).
            foreach (string path in new[] { StandingInstructionPath, ManagerBrainPath })
            {
                string source = ReadRepoFile(path);
                Assert.IsFalse(source.Contains("13.2"),
                    path + " still cites 13.2 for the delegation doctrine.");
                Assert.IsFalse(source.Contains("CE1-13.2"),
                    path + " still cites CE1-13.2 for the delegation doctrine.");
                Assert.IsFalse(source.Contains("Part V §§5.2/9.1"),
                    path + " still cites Part V §§5.2/9.1 for the delegation doctrine.");
                Assert.IsTrue(source.Contains("Part IX"),
                    path + " does not cite Part IX for the delegation doctrine.");
            }
        }

        // ---------- helpers ----------

        private static DelegatedAuthority Grant(
            DelegationService service, string businessId, int personId, DelegationScope scopes)
        {
            return service.GrantAuthority(
                new EntityIdRegistry(), businessId, personId, scopes,
                5000, string.Empty, null, 0, new List<string>());
        }

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, DelegationServicePath)))
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
                        if (File.Exists(Path.Combine(candidate, DelegationServicePath)))
                        {
                            return candidate;
                        }
                    }
                    DirectoryInfo parent = System.IO.Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign B (DEF-051): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign B (DEF-051): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }

        private static string Relative(string fullPath)
        {
            string root = FindRepoRoot();
            string full = Path.GetFullPath(fullPath);
            string base_ = Path.GetFullPath(root);
            if (full.StartsWith(base_, StringComparison.OrdinalIgnoreCase))
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

        private static bool ContainsWord(string source, string phrase)
        {
            int at = 0;
            while ((at = source.IndexOf(phrase, at, StringComparison.Ordinal)) >= 0)
            {
                int after = at + phrase.Length;
                char next = after < source.Length ? source[after] : '\0';
                if (!(char.IsLetterOrDigit(next) || next == '_'))
                {
                    return true;
                }
                at += phrase.Length;
            }
            return false;
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
