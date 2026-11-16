// Campaign B (DEF-006), package 9 — MakeOffer half of autonomous hiring.
//
// VERDICT (verify-then-document, DEF-051/DEF-005 pattern): NEVER AUTHORED, not
// a mechanical break. The negotiated-offer lifecycle is 90% wired and the
// candidate-side decision rule exists fully implemented — but the two halves
// that would make an offer live have no production driver at all:
//
//   - EmploymentNegotiationService.MakeOffer has ZERO production callers, and
//     no production code ever creates an EmploymentOffer. The service itself
//     is instantiated at the hub (SimulationSystemsHub.cs:724-725, with the
//     owner-or-HireStaff OfferAuthorityPredicate wired), saved/loaded
//     (CaptureSaveDto / LoadFromSaveDto), and advanced daily — but the daily
//     drive (SimulationDrivers.DriveLaborMarketDay) calls ONLY AdvanceDay,
//     i.e. expiry. An offer made today would sit Offered until AdvanceDay
//     lapses it on day 15. The expiry half runs; creation and response do not.
//   - PersonJobDecisions.EvaluateOffer — the candidate-side decision rule
//     (Accept / Refuse / Counter / Delay, deterministic, tested) — has ZERO
//     production callers, and no production code ever constructs a
//     JobDecisionInput. The rule is Canon-shaped (Canon XXXI §9.6 labor-market
//     lifecycle: mutual evaluation -> offer -> negotiation/counteroffer ->
//     acceptance/refusal; §14 recruitment loop; §9.3/§24.4 workers with
//     agency), but the driver that would build the input from live state and
//     apply the decision (AcceptOffer / RecordCounter / RecordRefusal) does
//     not exist. Those response methods also have zero production callers.
//   - The input mapping itself is unbuildable today without design decisions:
//     three JobDecisionInput fields have NO live production source anywhere
//     (CommuteMinutesOneWay, ConditionsScore01, EmployerReliability01 — they
//     appear in production only in LaborContracts.cs and
//     PersonJobDecisions.cs). And EmployerReliability01 <= 0 forces Delay
//     ("will not accept blind"), so a naive wiring with reliability defaulting
//     to 0 would Delay every offer until expiry — the trap is pinned here.
//   - The consent boundary is proposal 107 (owner assignment vs worker
//     consent), PENDING (proposals 106-145 NOT approved). Canon doctrine says
//     workers have agency and may refuse; the exact boundary of when an owner
//     may assign without negotiation is undecided. The evaluator's own header
//     cites CAN-CE1-03.3/03.4/13.1, but those are v2.3 cross-references, NOT
//     governing-source clauses (approved-proposals register), and proposal 107
//     is the pending decision on the point.
//   - Check 7 never mentions PersonJobDecisions/EvaluateOffer at all: F7-01
//     records MakeOffer's zero callers but misses that the candidate side is
//     equally unwired. This package records that second half.
//   - The HireFromInquiry half is NOT part of this defect any more: package 1
//     (DelegatedHiringService, 61021d17) wired it to a production daily
//     driver. Consent there is implied by the application itself (the
//     candidate applied via ApplyForOpening, or showed interest via
//     DirectApproach). One loose end on that side: DirectApproach's "wants
//     better terms" outcome (20% roll) returns null with no inquiry and no
//     negotiation thread — the candidate simply never appears again.
//
// What this package does NOT do: it changes NO production file. No offer is
// wired to a guessed acceptance rule, no wage/price/term is invented, the
// 14-day expiry is untouched, and no driver is built — the driver needs the
// owner decisions recorded in the checkpoint (proposal 107; the three input
// sources; who answers a counter on the employer side). The tests below pin
// the current state so it fails loudly if it changes without a decision:
// behavioural tests execute the real service + evaluator (the mechanism is
// sound when driven; the strand is real when it is not), structural tests
// read the repo and pin the zero-caller wiring gap.
//
// Harness fidelity: the behavioural tests execute real production code
// (EmploymentNegotiationService, PersonJobDecisions, LaborContracts). The
// structural tests read source from disk (repo-root walk-up, B-5/B-6/B-7/B-8
// precedent); they pin wiring, not behaviour.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using LandLedgers.Economy;
using LandLedgers.Economy.Labor;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class CampaignBMakeOfferWiringTests
    {
        private const int OwnerId = 1;
        private const int CandidateId = 2;
        private const string BusinessId = "store-1";

        private const string NegotiationServicePath =
            "Assets/Scripts/Domains/Economy/Labor/EmploymentNegotiationService.cs";
        private const string RecruitmentServicePath =
            "Assets/Scripts/Domains/Economy/Recruitment/RecruitmentService.cs";

        // ---------- behavioural: the mechanism is sound when driven ----------

        private static EmploymentNegotiationService ServiceWithAuthority() =>
            new EmploymentNegotiationService((biz, person, act) => person == OwnerId);

        private static EmploymentOffer MakeStandardOffer(
            EmploymentNegotiationService service, int dayIndex, List<string> diag) =>
            service.MakeOffer(
                BusinessId, CandidateId, OwnerId,
                wageCentsPerWeek: 300, duties: "Clerk", scheduleText: "dawn to dusk, six days",
                startDayIndex: 404, trialDays: 14, workLocation: "store-1 counter",
                durationDays: 0, housingNote: "", authorityScope: "serve customers, keep the till",
                conditions: "sober on duty", terminationNoticeDays: 7,
                deliveryChannel: "InPerson", howHeard: "referred-by:P5", dayIndex: dayIndex, diag: diag);

        [Test]
        public void OfferMade_NeverAnswered_ExpiresAfter14Days()
        {
            // The strand, executed for real: an offer with no candidate-side
            // driver sits Offered until AdvanceDay lapses it. This is exactly
            // what wiring MakeOffer alone would produce in production.
            var diag = new List<string>();
            var service = ServiceWithAuthority();
            var registry = new EmploymentRelationshipRegistry();

            EmploymentOffer offer = MakeStandardOffer(service, 400, diag);
            Assert.IsNotNull(offer);
            Assert.AreEqual(OfferState.Offered, offer.State);
            Assert.AreEqual(414, offer.ExpiresDayIndex, "14-day validity");

            service.AdvanceDay(413, registry, diag);
            Assert.AreEqual(OfferState.Offered, offer.State, "still open the day before expiry");

            service.AdvanceDay(415, registry, diag);
            Assert.AreEqual(OfferState.Expired, offer.State, "lapsed unanswered past day 414");
        }

        [Test]
        public void OfferMade_AcceptOffer_WorksWhenDriven()
        {
            // The mechanism is sound: driven by hand, the offer accepts and
            // registers a PendingStart employment that goes Active on the
            // start day. The gap is the driver, not the service.
            var diag = new List<string>();
            var service = ServiceWithAuthority();
            var registry = new EmploymentRelationshipRegistry();

            EmploymentOffer offer = MakeStandardOffer(service, 400, diag);
            Assert.IsNotNull(offer);

            string error = service.AcceptOffer(offer.OfferId, 401, registry, diag);
            Assert.IsNull(error, "AcceptOffer should succeed when driven: " + error);
            Assert.AreEqual(OfferState.Accepted, offer.State);
            Assert.IsTrue(
                registry.TryGetById("emp-" + offer.OfferId, out EmploymentRelationship rel),
                "acceptance must register the employment");
            Assert.AreEqual(EmploymentLifecycleState.PendingStart, rel.LifecycleState);

            service.AdvanceDay(404, registry, diag);
            Assert.AreEqual(EmploymentLifecycleState.Active, rel.LifecycleState, "active from the start day");
        }

        [Test]
        public void OfferMade_CounterThenEmployerAccepts_WorksWhenDriven()
        {
            // The counter path is also sound when driven: candidate counters,
            // employer accepts the revised terms, then acceptance binds at
            // the counter wage. Both halves need a driver; neither has one.
            var diag = new List<string>();
            var service = ServiceWithAuthority();
            var registry = new EmploymentRelationshipRegistry();

            EmploymentOffer offer = MakeStandardOffer(service, 400, diag);
            Assert.IsNotNull(offer);

            string counterError = service.RecordCounter(offer.OfferId, 350, 401, diag);
            Assert.IsNull(counterError, "RecordCounter should succeed: " + counterError);
            Assert.AreEqual(OfferState.Countered, offer.State);
            Assert.AreEqual(350, offer.CounterWageCentsPerWeek);

            string early = service.AcceptOffer(offer.OfferId, 402, registry, diag);
            Assert.IsNotNull(early, "acceptance before the employer accepts the counter must fail");
            Assert.AreEqual(OfferState.Countered, offer.State);

            string acceptCounter = service.AcceptCounterOffer(offer.OfferId, OwnerId, 402, diag);
            Assert.IsNull(acceptCounter, "AcceptCounterOffer should succeed: " + acceptCounter);

            string accept = service.AcceptOffer(offer.OfferId, 403, registry, diag);
            Assert.IsNull(accept, "AcceptOffer after the counter is accepted should succeed: " + accept);
            Assert.AreEqual(OfferState.Accepted, offer.State);
        }

        [Test]
        public void OfferMade_RecordRefusal_Terminal()
        {
            var diag = new List<string>();
            var service = ServiceWithAuthority();
            var registry = new EmploymentRelationshipRegistry();

            EmploymentOffer offer = MakeStandardOffer(service, 400, diag);
            Assert.IsNotNull(offer);

            string refusal = service.RecordRefusal(offer.OfferId, "wage too low", 401, diag);
            Assert.IsNull(refusal, "RecordRefusal should succeed: " + refusal);
            Assert.AreEqual(OfferState.Refused, offer.State);

            string accept = service.AcceptOffer(offer.OfferId, 402, registry, diag);
            Assert.IsNotNull(accept, "a refused offer cannot be accepted afterwards");
        }

        [Test]
        public void Evaluator_UnknownReliability_Delays()
        {
            // THE RELIABILITY TRAP: the evaluator refuses a blind accept when
            // employer reliability is unknown (0). A driver that wired the
            // evaluator with reliability defaulting to 0 would Delay every
            // offer until AdvanceDay expired it — the strand, by another name.
            var input = UrgentCandidateInput();
            input.EmployerReliability01 = 0f;

            JobDecision decision = PersonJobDecisions.EvaluateOffer(input, new List<string>());
            Assert.AreEqual(JobDecisionKind.Delay, decision.Kind);
            StringAssert.Contains("reliability", decision.Reason);
        }

        [Test]
        public void Evaluator_DefaultConstructedInput_Delays()
        {
            // Same trap from the input side: a default-constructed input
            // (everything zero except the wage) delays, because reliability,
            // conditions, reserves and trend all read as unknown/empty.
            var input = new JobDecisionInput { PersonId = CandidateId, OfferWageCentsPerWeek = 300 };

            JobDecision decision = PersonJobDecisions.EvaluateOffer(input, new List<string>());
            Assert.AreEqual(JobDecisionKind.Delay, decision.Kind);
        }

        [Test]
        public void Evaluator_UrgentCandidate_AcceptsWorkableOffer()
        {
            // The rule itself exists and runs: an unemployed candidate with
            // dependents and declining reserves accepts a workable offer.
            // The missing piece is the driver that would build this input
            // from live state and apply the decision — not the rule.
            JobDecision decision = PersonJobDecisions.EvaluateOffer(UrgentCandidateInput(), new List<string>());
            Assert.AreEqual(JobDecisionKind.Accept, decision.Kind);
            StringAssert.Contains("urgency", decision.Reason);
        }

        [Test]
        public void OfferMade_NoAuthorityPredicate_Refused()
        {
            // The employer-side authority gate is live: with no predicate
            // wired, MakeOffer refuses. (The hub does wire it — the gate is
            // not the gap; the missing MakeOffer caller is.)
            var diag = new List<string>();
            var service = new EmploymentNegotiationService();

            EmploymentOffer offer = MakeStandardOffer(service, 400, diag);
            Assert.IsNull(offer, "no authority wiring must refuse the offer");
        }

        private static JobDecisionInput UrgentCandidateInput() =>
            new JobDecisionInput
            {
                PersonId = CandidateId,
                HouseholdId = 10,
                ReservesCents = 120,
                Dependents = 2,
                CurrentWageCentsPerWeek = 0,
                CurrentlyEmployed = false,
                OfferWageCentsPerWeek = 300,
                CommuteMinutesOneWay = 20,
                ConditionsScore01 = 0.7f,
                EmployerReliability01 = 0.8f,
                HousingOffered = false,
                KnownAlternativeCount = 0,
                ReservesTrendCentsPerDay = -10,
                DayIndex = 401
            };

        // ---------- structural: the wiring gap, pinned by source reads ----------

        [Test]
        public void MakeOffer_HasZeroProductionCallers()
        {
            // No production file ever calls .MakeOffer( — the definition in
            // EmploymentNegotiationService.cs is the only MakeOffer in the
            // tree outside tests. (The definition itself is " MakeOffer(",
            // not ".MakeOffer(", so it does not count.)
            int calls = CountInProduction(@"\.MakeOffer\(");
            Assert.AreEqual(0, calls,
                "CAMPAIGN-B (DEF-006): MakeOffer must have zero production callers; " +
                "a caller would mean the offer path was wired without a decision.");
        }

        [Test]
        public void NegotiationService_ProductionSurface_IsExpirySaveLoadOnly()
        {
            // Every production touch on hub.EmploymentNegotiation must be one
            // of: AdvanceDay (expiry), CaptureSaveDto / LoadFromSaveDto
            // (save/load). If AcceptOffer, RecordCounter, RecordRefusal,
            // RecordWithdraw or AcceptCounterOffer ever appears here, the
            // candidate-response half was wired and this test must be
            // revisited with the driver, not silently passed.
            var members = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in ProductionFiles())
            {
                string blanked = BlankCommentsAndStrings(File.ReadAllText(file));
                foreach (Match m in Regex.Matches(blanked, @"EmploymentNegotiation\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)"))
                    members.Add(m.Groups[1].Value);
            }

            Assert.Contains("AdvanceDay", new List<string>(members),
                "the daily expiry drive must be present");
            foreach (string member in members)
            {
                Assert.IsTrue(
                    member == "AdvanceDay" || member == "CaptureSaveDto" || member == "LoadFromSaveDto",
                    "CAMPAIGN-B (DEF-006): unexpected production member on the negotiation service: "
                    + member + " (full set: " + string.Join(", ", new List<string>(members)) + ")");
            }
        }

        [Test]
        public void EvaluateOffer_HasZeroProductionCallers()
        {
            // The candidate-side rule exists and is tested, but no production
            // code ever asks a candidate what they think of an offer.
            int calls = CountInProduction(@"PersonJobDecisions\.EvaluateOffer");
            Assert.AreEqual(0, calls,
                "CAMPAIGN-B (DEF-006): PersonJobDecisions.EvaluateOffer must have zero production callers.");
        }

        [Test]
        public void NoProductionJobDecisionInputConstructed()
        {
            int built = CountInProduction(@"new\s+JobDecisionInput");
            Assert.AreEqual(0, built,
                "CAMPAIGN-B (DEF-006): no production code ever builds a JobDecisionInput — " +
                "the evaluator's input mapping (the 'integrator') does not exist.");
        }

        [Test]
        public void NoProductionOfferEverCreated()
        {
            // No production code creates an offer, and none ever reads one
            // back: the offer store is write-only in production (nothing
            // writes, nothing reads — only AdvanceDay expiry touches it).
            int created = CountInProduction(@"new\s+EmploymentOffer", NegotiationServicePath);
            Assert.AreEqual(0, created,
                "CAMPAIGN-B (DEF-006): no production code ever creates an EmploymentOffer.");
            int readBack = CountInProduction(@"\.TryGetOffer\(");
            Assert.AreEqual(0, readBack,
                "CAMPAIGN-B (DEF-006): no production code ever reads an offer back via TryGetOffer.");
        }

        [Test]
        public void NoLiveSourceForReliabilityConditionsCommute()
        {
            // Three JobDecisionInput fields have no live production source:
            // they appear in production only where they are defined
            // (LaborContracts.cs) and where they are read (PersonJobDecisions.cs).
            // A candidate-response driver cannot be built until the owner /
            // manager decides what supplies each one.
            AssertInputFieldHasNoLiveSource("EmployerReliability01");
            AssertInputFieldHasNoLiveSource("ConditionsScore01");
            AssertInputFieldHasNoLiveSource("CommuteMinutesOneWay");
        }

        [Test]
        public void DirectApproach_NegotiateOutcome_CreatesNothing()
        {
            // The recruitment side's loose end: DirectApproach's "wants better
            // terms" outcome returns null with no inquiry and no negotiation
            // thread — the candidate simply never appears again. Exactly one
            // inquiry is created in the whole method (the interested branch).
            string method = ExtractMethod(ReadRepoFile(RecruitmentServicePath), "DirectApproach");
            Assert.IsNotNull(method, "DirectApproach method must exist in RecruitmentService.cs");
            Assert.IsTrue(method.Contains("wants better terms first (negotiate)"),
                "the negotiate outcome diagnostic must exist");
            Assert.AreEqual(1, CountOccurrences(method, "new CandidateInquiry"),
                "CAMPAIGN-B (DEF-006): DirectApproach must create exactly one inquiry (the interested branch); " +
                "the negotiate branch must create nothing.");
        }

        [Test]
        public void WillWorkToday_DailyWillingnessOverload_IsWired_EvaluateOfferIsNot()
        {
            // Precision correction, caught by this very test on its first
            // run: WillWorkToday DOES have a production caller — but it is
            // the daily-willingness overload (personId, fatigued, danger,
            // contract restrictions), called once by WorkScheduler.cs:107
            // per worker per scheduling item. That is the one live method in
            // PersonJobDecisions.cs. The candidate-side half — EvaluateOffer
            // (pinned above) — has none. So the file is partially live: the
            // daily willingness check runs, the offer evaluation never does.
            // This test pins the split so a future driver cannot be mistaken
            // for already existing, and so the live overload cannot be
            // silently dropped while fixing the dead half.
            var callers = new List<string>();
            foreach (string file in ProductionFiles())
            {
                string blanked = BlankCommentsAndStrings(File.ReadAllText(file));
                int hits = Regex.Matches(blanked, @"PersonJobDecisions\.WillWorkToday\(").Count;
                for (int i = 0; i < hits; i++)
                    callers.Add(Path.GetFileName(file));
            }

            Assert.AreEqual(1, callers.Count,
                "CAMPAIGN-B (DEF-006): WillWorkToday must have exactly one production call site (WorkScheduler.cs); found: "
                + string.Join(", ", callers));
            Assert.AreEqual("WorkScheduler.cs", callers[0],
                "the single WillWorkToday call site must be WorkScheduler.cs");
        }

        private void AssertInputFieldHasNoLiveSource(string fieldName)
        {
            var files = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in ProductionFiles())
            {
                string blanked = BlankCommentsAndStrings(File.ReadAllText(file));
                if (blanked.Contains(fieldName))
                    files.Add(Path.GetFileName(file));
            }

            Assert.AreEqual(2, files.Count,
                "CAMPAIGN-B (DEF-006): " + fieldName + " must appear in production only in " +
                "LaborContracts.cs (definition) and PersonJobDecisions.cs (read); found in: " +
                string.Join(", ", new List<string>(files)));
            Assert.Contains("LaborContracts.cs", new List<string>(files));
            Assert.Contains("PersonJobDecisions.cs", new List<string>(files));
        }

        // ---------- repo-walking helpers (B-5/B-6/B-7/B-8 precedent) ----------

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, NegotiationServicePath)))
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
                        Path.Combine(dir, "work-b"),
                    })
                    {
                        tried.AppendLine("  " + candidate);
                        if (File.Exists(Path.Combine(candidate, NegotiationServicePath)))
                            return candidate;
                    }
                    DirectoryInfo parent = Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign B (DEF-006): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign B (DEF-006): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }

        private static IEnumerable<string> ProductionFiles()
        {
            string scripts = Path.Combine(FindRepoRoot(), "Assets", "Scripts");
            foreach (string file in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                string normalized = file.Replace('\\', '/');
                if (normalized.Contains("/Tests/"))
                    continue;
                yield return file;
            }
        }

        private static int CountInProduction(string pattern, string excludeRelativePath = null)
        {
            string excludeFull = excludeRelativePath == null
                ? null
                : Path.GetFullPath(Path.Combine(FindRepoRoot(), excludeRelativePath));
            int total = 0;
            foreach (string file in ProductionFiles())
            {
                if (excludeFull != null && string.Equals(
                        Path.GetFullPath(file), excludeFull, StringComparison.OrdinalIgnoreCase))
                    continue;
                string blanked = BlankCommentsAndStrings(File.ReadAllText(file));
                total += Regex.Matches(blanked, pattern).Count;
            }
            return total;
        }

        private static int CountOccurrences(string text, string needle)
        {
            int count = 0;
            int at = 0;
            while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                count++;
                at += needle.Length;
            }
            return count;
        }

        private static string ExtractMethod(string source, string methodName)
        {
            int start = source.IndexOf(methodName + "(", StringComparison.Ordinal);
            if (start < 0)
                return null;
            // Back up to the start of the line holding the signature.
            int lineStart = source.LastIndexOf('\n', start);
            start = lineStart < 0 ? 0 : lineStart + 1;
            int next = source.IndexOf("\n        public ", start + 1, StringComparison.Ordinal);
            if (next < 0)
                next = source.IndexOf("\n        private ", start + 1, StringComparison.Ordinal);
            return next < 0 ? source.Substring(start) : source.Substring(start, next - start);
        }

        /// <summary>
        /// Blanks // and /* */ comments, string literals (regular and
        /// verbatim) and char literals, preserving newlines, so structural
        /// counts see code only — never a comment or a diagnostic string that
        /// merely mentions a method name.
        /// </summary>
        private static string BlankCommentsAndStrings(string text)
        {
            var outChars = new char[text.Length];
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n')
                        outChars[i++] = ' ';
                }
                else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    outChars[i++] = ' ';
                    outChars[i++] = ' ';
                    while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                    {
                        if (text[i] != '\n')
                            outChars[i] = ' ';
                        i++;
                    }
                    if (i + 1 < text.Length)
                    {
                        outChars[i++] = ' ';
                        outChars[i++] = ' ';
                    }
                }
                else if (c == '@' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    outChars[i++] = ' ';
                    outChars[i++] = ' ';
                    while (i < text.Length)
                    {
                        if (text[i] == '"' && i + 1 < text.Length && text[i + 1] == '"')
                        {
                            outChars[i++] = ' ';
                            outChars[i++] = ' ';
                            continue;
                        }
                        if (text[i] == '"')
                        {
                            outChars[i++] = ' ';
                            break;
                        }
                        if (text[i] != '\n')
                            outChars[i] = ' ';
                        i++;
                    }
                }
                else if (c == '"')
                {
                    outChars[i++] = ' ';
                    while (i < text.Length && text[i] != '\n')
                    {
                        if (text[i] == '\\' && i + 1 < text.Length)
                        {
                            outChars[i++] = ' ';
                            outChars[i++] = ' ';
                            continue;
                        }
                        if (text[i] == '"')
                        {
                            outChars[i++] = ' ';
                            break;
                        }
                        outChars[i++] = ' ';
                    }
                }
                else if (c == '\'')
                {
                    outChars[i++] = ' ';
                    while (i < text.Length && text[i] != '\n')
                    {
                        if (text[i] == '\\' && i + 1 < text.Length)
                        {
                            outChars[i++] = ' ';
                            outChars[i++] = ' ';
                            continue;
                        }
                        if (text[i] == '\'')
                        {
                            outChars[i++] = ' ';
                            break;
                        }
                        outChars[i++] = ' ';
                    }
                }
                else
                {
                    outChars[i++] = c;
                }
            }
            return new string(outChars);
        }
    }
}
