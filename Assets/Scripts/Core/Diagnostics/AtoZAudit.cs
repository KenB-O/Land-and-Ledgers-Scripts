// Land & Ledgers — A-to-Z automated audit (Lane 8, §20).
//
// The manual A–N checks become automated, re-runnable checks. Every check
// produces a finding in the §22 status vocabulary, citing exact evidence
// (file:line, test id, run id), and every finding converts to Lane 1's
// incident-core shape (SimulationIncident / IncidentReport) — never a
// parallel register. The audit OBSERVES authoritative state; it holds no
// ledger, no inventory, no cash, and it never calls a state-changing method.
//
// Three check kinds (Codex rule 9: do NOT re-run checks whose evidence is
// sufficient):
//   - Source: evaluated from supplied source text. Tests feed fixture text;
//     the Editor window reads the repo tree from disk and passes the real
//     files in. Missing evidence leaves the check BLOCKED, never guessed.
//   - Recorded: the manual evidence is sufficient and is NOT re-run. The
//     finding carries the recorded status and the exact evidence (run id,
//     harness test id, file:line). It is re-opened only if current source or
//     fresh results contradict it.
//   - Run: needs a live run. With no run on this HEAD the finding is BLOCKED
//     and names what is missing. With run evidence (SurvivalRunEvidence) the
//     survival stages are evaluated by the Scenario Lab evaluator and mapped
//     here.
//
// No run exists on this HEAD: every run check says so wherever it matters.

using System;
using System.Collections.Generic;
using System.Linq;

namespace LandLedgers.Diagnostics
{
    /// <summary>How an A-to-Z check gets its evidence.</summary>
    public enum AtoZCheckKind
    {
        /// <summary>Evaluated from supplied source text (config, .cs).</summary>
        Source = 0,

        /// <summary>Manual evidence is sufficient; not re-run. Re-open only on contradicting evidence.</summary>
        Recorded = 1,

        /// <summary>Needs a live run; BLOCKED until run evidence exists.</summary>
        Run = 2,
    }

    /// <summary>One automated A-to-Z check: its identity, its evidence, and how it is evaluated.</summary>
    public sealed class AtoZCheckDefinition
    {
        public AtoZCheckDefinition(
            string id, string section, string title, AtoZCheckKind kind,
            string owningSystem, string recoveryAuthority,
            RefusalCauseClass causeClass, IReadOnlyList<string> evidenceReferences)
        {
            Id = id ?? string.Empty;
            Section = section ?? string.Empty;
            Title = title ?? string.Empty;
            Kind = kind;
            OwningSystem = owningSystem ?? string.Empty;
            RecoveryAuthority = recoveryAuthority ?? string.Empty;
            CauseClass = causeClass;
            EvidenceReferences = evidenceReferences ?? new List<string>();
        }

        /// <summary>Stable check id, e.g. "A-06-WHOLESALE-CONFIG".</summary>
        public string Id { get; }

        /// <summary>A-to-Z section letter (A–N, W).</summary>
        public string Section { get; }

        /// <summary>One-line title of the manual check this automates.</summary>
        public string Title { get; }

        /// <summary>How the check gets its evidence.</summary>
        public AtoZCheckKind Kind { get; }

        /// <summary>True for run checks: a live run is required before this can pass.</summary>
        public bool RequiresLiveRun
        {
            get { return Kind == AtoZCheckKind.Run; }
        }

        /// <summary>Owning system (the subsystem that owns the question).</summary>
        public string OwningSystem { get; }

        /// <summary>Recovery authority: who/what can resolve this.</summary>
        public string RecoveryAuthority { get; }

        /// <summary>Causal class (M-R14) when the check's failure mode has one; Unclassified otherwise.</summary>
        public RefusalCauseClass CauseClass { get; }

        /// <summary>Evidence references (file:line, test id, run id) — the static citations for this check.</summary>
        public IReadOnlyList<string> EvidenceReferences { get; }
    }

    /// <summary>One evaluated A-to-Z finding: a check plus its §22 status and exact evidence.</summary>
    public sealed class AtoZFinding
    {
        public AtoZFinding(AtoZCheckDefinition definition, AtoZFindingStatus status, string detail, string evidence)
        {
            Definition = definition ?? throw new ArgumentNullException("definition");
            Status = status;
            Detail = detail ?? string.Empty;
            Evidence = evidence ?? string.Empty;
        }

        /// <summary>The check this finding evaluates.</summary>
        public AtoZCheckDefinition Definition { get; }

        /// <summary>§22 status of this finding.</summary>
        public AtoZFindingStatus Status { get; }

        /// <summary>What was found, in words, including what is still owed (live run, prior repair).</summary>
        public string Detail { get; }

        /// <summary>Exact evidence: file:line, test id, run id, or the observed value.</summary>
        public string Evidence { get; }

        /// <summary>True when the fix is in source but a live run must still confirm the behaviour.</summary>
        public bool LiveRunStillOwed
        {
            get
            {
                return Status == AtoZFindingStatus.ImplementedUnverified
                    || Status == AtoZFindingStatus.SuspectedFix;
            }
        }

        /// <summary>
        /// Emits this finding in Lane 1's incident-core shape. The signature
        /// id is the check id; severity follows the §22 status (a NEW
        /// REGRESSION or a REGRESSION VERIFIED is CRITICAL; an UNRESOLVED or
        /// SUSPECTED FIX defect is WARNING; a fix, a legitimate explanation
        /// or a non-reproduction is INFORMATION; a BLOCKED check is UNKNOWN
        /// because the evidence is insufficient by construction).
        /// </summary>
        public IncidentReport ToIncidentReport()
        {
            IncidentReport report = new IncidentReport();
            report.SignatureId = Definition.Id;
            report.Severity = MapStatusToSeverity(Status);
            report.Summary = Definition.Title;
            report.DayIndex = -1;
            report.SimSeconds = -1;
            report.OwningSystem = Definition.OwningSystem;
            report.RecoveryAuthority = Definition.RecoveryAuthority;
            report.CauseClass = Definition.CauseClass;
            report.CauseEvidence = Detail;
            report.EvidenceReference = Evidence;
            return report;
        }

        /// <summary>Maps a §22 status onto the incident-core severity scale.</summary>
        public static IncidentSeverity MapStatusToSeverity(AtoZFindingStatus status)
        {
            switch (status)
            {
                case AtoZFindingStatus.RegressionVerified:
                case AtoZFindingStatus.NewRegression:
                    return IncidentSeverity.Critical;
                case AtoZFindingStatus.Unresolved:
                case AtoZFindingStatus.SuspectedFix:
                    return IncidentSeverity.Warning;
                case AtoZFindingStatus.Blocked:
                    return IncidentSeverity.Unknown;
                default:
                    return IncidentSeverity.Information;
            }
        }
    }

    /// <summary>Source text supplied to the source checks (Editor reads the real files; tests feed fixtures).</summary>
    public sealed class AtoZSourceEvidence
    {
        /// <summary>Assets/Core/Population/DevelopmentTownConfiguration.asset text.</summary>
        public string ConfigAssetText = string.Empty;

        /// <summary>ScenarioOpeningWholesaleAccounts.cs text.</summary>
        public string WholesaleAccountsText = string.Empty;

        /// <summary>FuelDealerByproductSettlement.cs text.</summary>
        public string ByproductSettlementText = string.Empty;

        /// <summary>SimulationDrivers.cs text (or the TransferSawmillByproductsToFuelDealer excerpt).</summary>
        public string SawmillTransferText = string.Empty;

        /// <summary>HouseholdMeals.cs text.</summary>
        public string HouseholdMealsText = string.Empty;
    }

    /// <summary>The evaluated audit: every catalog finding, with counts by §22 status.</summary>
    public sealed class AtoZAuditReport
    {
        public AtoZAuditReport(List<AtoZFinding> findings)
        {
            Findings = findings ?? new List<AtoZFinding>();
        }

        /// <summary>Every evaluated finding, in catalog order.</summary>
        public List<AtoZFinding> Findings { get; }

        /// <summary>Findings with the given §22 status.</summary>
        public int CountWithStatus(AtoZFindingStatus status)
        {
            int count = 0;
            foreach (AtoZFinding finding in Findings)
            {
                if (finding.Status == status)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>Findings that still need a live run before they can pass.</summary>
        public int LiveRunOwedCount
        {
            get
            {
                int count = 0;
                foreach (AtoZFinding finding in Findings)
                {
                    if (finding.LiveRunStillOwed || finding.Status == AtoZFindingStatus.Blocked)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>Every finding in Lane 1's incident-core shape.</summary>
        public List<IncidentReport> ToIncidentReports()
        {
            var reports = new List<IncidentReport>(Findings.Count);
            foreach (AtoZFinding finding in Findings)
            {
                reports.Add(finding.ToIncidentReport());
            }
            return reports;
        }

        /// <summary>Human-readable audit report (one line per finding with status and evidence).</summary>
        public string ToMarkdown()
        {
            var lines = new List<string>();
            lines.Add("# A-to-Z automated audit");
            lines.Add(string.Empty);
            lines.Add("Findings: " + Findings.Count
                + " | VERIFIED FIXED: " + CountWithStatus(AtoZFindingStatus.VerifiedFixed)
                + " | IMPLEMENTED UNVERIFIED: " + CountWithStatus(AtoZFindingStatus.ImplementedUnverified)
                + " | SUSPECTED FIX: " + CountWithStatus(AtoZFindingStatus.SuspectedFix)
                + " | UNRESOLVED: " + CountWithStatus(AtoZFindingStatus.Unresolved)
                + " | BLOCKED: " + CountWithStatus(AtoZFindingStatus.Blocked)
                + " | Live run still owed: " + LiveRunOwedCount);
            lines.Add(string.Empty);
            lines.Add("No run exists on this HEAD: every BLOCKED finding names what a live run must supply.");
            lines.Add(string.Empty);
            foreach (AtoZFinding finding in Findings)
            {
                lines.Add("- [" + finding.Definition.Id + "] "
                    + AtoZFindingStatusText.Label(finding.Status)
                    + " — " + finding.Definition.Title);
                lines.Add("  Detail: " + finding.Detail);
                lines.Add("  Evidence: " + finding.Evidence);
            }
            return string.Join("\n", lines.ToArray());
        }
    }

    /// <summary>
    /// Lane 8 (§20) — the automated A-to-Z audit. The catalog is the manual
    /// A–N checks as automated definitions; Evaluate runs the source and
    /// recorded checks against supplied evidence; EvaluateRunChecks maps a
    /// Scenario Lab result onto the run checks. Nothing here reads a live
    /// game: the Editor window supplies file text, the lab supplies run
    /// evidence, tests supply fixtures.
    /// </summary>
    public static class AtoZAuditRunner
    {
        /// <summary>The full check catalog, in section order.</summary>
        public static List<AtoZCheckDefinition> BuildCatalog()
        {
            var catalog = new List<AtoZCheckDefinition>();

            catalog.Add(new AtoZCheckDefinition(
                "A-06-WHOLESALE-CONFIG", "A",
                "Bakery wholesale obligation removed from config (24x bake.bread-loaf daily)",
                AtoZCheckKind.Source, "Bakery", "Lane 9a (H-1)",
                RefusalCauseClass.Unclassified,
                new List<string> { "Assets/Core/Population/DevelopmentTownConfiguration.asset:1810", "Agent H H-1" }));

            catalog.Add(new AtoZCheckDefinition(
                "A-06-WHOLESALE-FALLBACK", "A",
                "Demand-derived wholesale fallback books no account when config has no entries",
                AtoZCheckKind.Source, "Bakery", "Lane 9a (H-1)",
                RefusalCauseClass.Unclassified,
                new List<string> { "Assets/Scripts/Domains/Economy/Equipment/ScenarioOpeningWholesaleAccounts.cs:137-159", "Agent H H-1" }));

            catalog.Add(new AtoZCheckDefinition(
                "W-SF7-BYPRODUCT", "W",
                "Sawmill byproduct settlement: custody never moves unpaid (SF-7)",
                AtoZCheckKind.Source, "FuelDealer", "FuelDealerByproductSettlement",
                RefusalCauseClass.Unclassified,
                new List<string> { "Assets/Scripts/Domains/Economy/Core/FuelDealerByproductSettlement.cs", "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs:916", "Agent E C-04" }));

            catalog.Add(new AtoZCheckDefinition(
                "B-FIREWOOD", "B",
                "Firewood activity points authored (zero in config per M-13 inventory)",
                AtoZCheckKind.Source, "World", "Town authoring (config)",
                RefusalCauseClass.DestinationMissing,
                new List<string> { "Assets/Core/Population/DevelopmentTownConfiguration.asset", "Agent M M-13", "Agent B B-01" }));

            catalog.Add(new AtoZCheckDefinition(
                "E-14-BAKERY-FUEL", "E",
                "Bakery fuel replenishment configured (fuelDealerIntakeUnitsPerBakeryUnit; 0 = day-41 death)",
                AtoZCheckKind.Source, "Bakery", "Owner decision (bakery fuel supply, open)",
                RefusalCauseClass.Unclassified,
                new List<string> { "Assets/Core/Population/DevelopmentTownConfiguration.asset:1807", "Agent E E-14" }));

            catalog.Add(new AtoZCheckDefinition(
                "A-08-STAPLE-FOOD", "A",
                "Generic unspecified staple_food never served as a meal and never counted toward supply",
                AtoZCheckKind.Source, "HouseholdMeals", "Lane 9a (A8)",
                RefusalCauseClass.Unclassified,
                new List<string> { "Assets/Scripts/Domains/Population/HouseholdMeals.cs:635", "Agent A A8" }));

            catalog.Add(new AtoZCheckDefinition(
                "A-05-MISSED-MEAL-REASON", "A",
                "Missed meals classified by reason (MissedMealReasonKind on the record)",
                AtoZCheckKind.Source, "HouseholdMeals", "Lane 9a (A5)",
                RefusalCauseClass.Unclassified,
                new List<string> { "Assets/Scripts/Domains/Population/HouseholdMeals.cs:86", "Agent A A5" }));

            catalog.Add(new AtoZCheckDefinition(
                "DEF-046-DOUBLE-TIMELINE", "G",
                "Double timeline has exactly three production consumers (DEF-046)",
                AtoZCheckKind.Recorded, "Time", "DEF-046 owner",
                RefusalCauseClass.Unclassified,
                new List<string> { "CampaignCFloat32TimelineTests.DoubleTimeline_ExistsButHasTwoProductionConsumers", "Harness: Expected 3, got 7" }));

            catalog.Add(new AtoZCheckDefinition(
                "W-STARTNOTWALKABLE", "W",
                "StartNotWalkable warnings grouped by (signature, destination), origin-invalid class",
                AtoZCheckKind.Recorded, "Pathing", "Lane 9h (M-R9 attribution)",
                RefusalCauseClass.OriginInvalid,
                new List<string> { "Reports/Playtests/Runs/20261120T192311Z-343a8cd2/console-events.jsonl", "2,878 occurrences: 2,116 sleep, 729 meal, 33 other" }));

            catalog.Add(new AtoZCheckDefinition(
                "D-36-DAY1-CASH-GAP", "D",
                "Day-1 business-cash gap of 2,448c attributed (config 162,000c vs run day-1 159,552c)",
                AtoZCheckKind.Recorded, "Economy", "Agent E E R-03",
                RefusalCauseClass.Unclassified,
                new List<string> { "Reports/Playtests/Runs/20261120T192311Z-343a8cd2/daily-summary.csv", "Agent N §3.3" }));

            catalog.Add(new AtoZCheckDefinition(
                "F-S4-FURNITURE-CONTINUE", "F",
                "In-progress furniture production survives Save/Continue (ActiveProcesses has no save path)",
                AtoZCheckKind.Recorded, "Save/Continue", "Lane 9f (F-S4)",
                RefusalCauseClass.Unclassified,
                new List<string> { "Agent F §4-§5", "Q04FreshSceneContinueTests.cs" }));

            catalog.Add(new AtoZCheckDefinition(
                "F-S7-RUNNER-CONTINUE", "F",
                "Playtest runner has a Continue mode (save day N, load, resume)",
                AtoZCheckKind.Recorded, "Save/Continue", "Lane 8 (F-S7)",
                RefusalCauseClass.Unclassified,
                new List<string> { "Assets/Scripts/Editor/Playtests/Playtest60DayRunnerWindow.cs:472-474", "Agent F F-S7" }));

            // Run checks: one per survival stage, plus the two checks that
            // need a run's day-boundary evidence. Evaluated by
            // EvaluateRunChecks from Scenario Lab evidence.
            foreach (SurvivalStageSpec spec in SurvivalAcceptanceScenario.StageSpecs)
            {
                catalog.Add(new AtoZCheckDefinition(
                    "N-" + spec.StageId, "N",
                    "Survival stage " + spec.StageId + ": " + spec.Title,
                    AtoZCheckKind.Run, "Household survival", "Agent N §2.3",
                    RefusalCauseClass.Unclassified,
                    new List<string> { spec.EvidenceSource }));
            }

            catalog.Add(new AtoZCheckDefinition(
                "N-ECONOMIC-RECONCILIATION", "N",
                "Day-boundary cash/lot reconciliation reconciles to the cent (E C-01–C-19)",
                AtoZCheckKind.Run, "Economy", "Agent E C-01–C-19",
                RefusalCauseClass.Unclassified,
                new List<string> { "Agent N §3.3", "Seam: transaction-event stream (N-01/N-02)" }));

            catalog.Add(new AtoZCheckDefinition(
                "N-SAVE-CONTINUE", "N",
                "Save/Continue midpoint: pre/post equality, no duplicate settlement (Agent N §2.5)",
                AtoZCheckKind.Run, "Save/Continue", "Tool 07 comparator",
                RefusalCauseClass.Unclassified,
                new List<string> { "Agent N §2.5", "Needs F-S5 holder setup and F-S7 runner Continue mode" }));

            return catalog;
        }

        /// <summary>Evaluates the source and recorded checks. Run checks are BLOCKED here (no run evidence in this call).</summary>
        public static AtoZAuditReport Evaluate(AtoZSourceEvidence evidence)
        {
            if (evidence == null)
            {
                throw new ArgumentNullException("evidence");
            }

            var findings = new List<AtoZFinding>();
            foreach (AtoZCheckDefinition check in BuildCatalog())
            {
                switch (check.Kind)
                {
                    case AtoZCheckKind.Source:
                        findings.Add(EvaluateSourceCheck(check, evidence));
                        break;
                    case AtoZCheckKind.Recorded:
                        findings.Add(RecordedFinding(check));
                        break;
                    default:
                        findings.Add(new AtoZFinding(check, AtoZFindingStatus.Blocked,
                            "Needs a live run. No run exists on this HEAD, so this check cannot be evaluated.",
                            "No run exists on this HEAD."));
                        break;
                }
            }
            return new AtoZAuditReport(findings);
        }

        /// <summary>
        /// Maps a Scenario Lab result onto the run checks. With no live run
        /// the stages stay BLOCKED; with a run, a passed stage is VERIFIED
        /// FIXED, a failed stage is UNRESOLVED, and a stage blocked by a
        /// prior repair stays BLOCKED with the repair named.
        /// </summary>
        public static List<AtoZFinding> EvaluateRunChecks(SurvivalAcceptanceResult survival)
        {
            var findings = new List<AtoZFinding>();
            if (survival == null)
            {
                throw new ArgumentNullException("survival");
            }

            foreach (AtoZCheckDefinition check in BuildCatalog())
            {
                if (check.Kind != AtoZCheckKind.Run)
                {
                    continue;
                }

                if (check.Id.StartsWith("N-S", StringComparison.Ordinal))
                {
                    SurvivalStageVerdict verdict = survival.StageVerdict(check.Id.Substring(2));
                    AtoZFindingStatus status;
                    switch (verdict.Verdict)
                    {
                        case SurvivalVerdict.Pass: status = AtoZFindingStatus.VerifiedFixed; break;
                        case SurvivalVerdict.Fail: status = AtoZFindingStatus.Unresolved; break;
                        default: status = AtoZFindingStatus.Blocked; break;
                    }
                    findings.Add(new AtoZFinding(check, status, verdict.Reason,
                        survival.RunId.Length > 0 ? survival.RunId : "No run exists on this HEAD."));
                }
                else
                {
                    findings.Add(new AtoZFinding(check, AtoZFindingStatus.Blocked,
                        "Needs a live run with the transaction-event stream / Continue midpoint seams. No run exists on this HEAD.",
                        "No run exists on this HEAD."));
                }
            }
            return findings;
        }

        /// <summary>Recorded findings: the manual evidence is sufficient and is not re-run.</summary>
        public static AtoZFinding RecordedFinding(AtoZCheckDefinition check)
        {
            switch (check.Id)
            {
                case "DEF-046-DOUBLE-TIMELINE":
                    return new AtoZFinding(check, AtoZFindingStatus.Unresolved,
                        "Pre-existing harness failure, do not weaken the test: 7 production files reference the double timeline, expected 3 (TimeManager, PopulationPathingDirector, WorldInteractionController, NpcActivityMonitorWindow, Playtest60DayRunnerWindow, SimulationDrivers, AgentMover).",
                        "CampaignCFloat32TimelineTests.DoubleTimeline_ExistsButHasTwoProductionConsumers: Expected 3, got 7");
                case "W-STARTNOTWALKABLE":
                    return new AtoZFinding(check, AtoZFindingStatus.Unresolved,
                        "Retained 60-day run: 2,878 occurrences of one signature, one mechanism (origin-invalid, M-06), at least three originating task families (2,116 sleep, 729 meal, 33 other domestic) plus 850 seller-route aborts under a separate signature. Must be grouped by (signature, destination), never by signature alone.",
                        "Reports/Playtests/Runs/20261120T192311Z-343a8cd2/console-events.jsonl: 2,878 StartNotWalkable");
                case "D-36-DAY1-CASH-GAP":
                    return new AtoZFinding(check, AtoZFindingStatus.Unresolved,
                        "Day-1 business-cash gap of 2,448c (config 162,000c vs run day-1 159,552c), present in both the 30-day and 60-day retained runs. The acceptance report must show the gap on day 1 of every run until it is attributed; it must not be normalised away by starting the reconciliation at day 2.",
                        "Reports/Playtests/Runs/20261120T192311Z-343a8cd2/daily-summary.csv: businessCashCents day 1 = 159,552");
                case "F-S4-FURNITURE-CONTINUE":
                    return new AtoZFinding(check, AtoZFindingStatus.Unresolved,
                        "In-progress furniture production vanishes on Continue (GenericConfiguration.ActiveProcesses has no save path; consumed inputs are destroyed with no refund or record). Recorded as EXPECTED-FAIL until repaired, not silently skipped.",
                        "Agent F §4-§5; Q04FreshSceneContinueTests.cs");
                case "F-S7-RUNNER-CONTINUE":
                    return new AtoZFinding(check, AtoZFindingStatus.Unresolved,
                        "The runner has no Continue mode: it requires StartupIntent == NewGame and a grep for continue returns only continue; statements. The Save/Continue midpoint cannot be built until F-S5 holder setup and F-S7 land.",
                        "Assets/Scripts/Editor/Playtests/Playtest60DayRunnerWindow.cs:472-474");
                default:
                    return new AtoZFinding(check, AtoZFindingStatus.Blocked,
                        "Recorded check with no recorded evaluation on this HEAD.",
                        string.Join("; ", check.EvidenceReferences.ToArray()));
            }
        }

        private static AtoZFinding EvaluateSourceCheck(AtoZCheckDefinition check, AtoZSourceEvidence evidence)
        {
            switch (check.Id)
            {
                case "A-06-WHOLESALE-CONFIG":
                    return CheckWholesaleConfig(check, evidence.ConfigAssetText);
                case "A-06-WHOLESALE-FALLBACK":
                    return CheckWholesaleFallback(check, evidence.WholesaleAccountsText);
                case "W-SF7-BYPRODUCT":
                    return CheckByproductSettlement(check, evidence.ByproductSettlementText, evidence.SawmillTransferText);
                case "B-FIREWOOD":
                    return CheckFirewood(check, evidence.ConfigAssetText);
                case "E-14-BAKERY-FUEL":
                    return CheckBakeryFuel(check, evidence.ConfigAssetText);
                case "A-08-STAPLE-FOOD":
                    return CheckGenericStaple(check, evidence.HouseholdMealsText);
                case "A-05-MISSED-MEAL-REASON":
                    return CheckMissedMealReason(check, evidence.HouseholdMealsText);
                default:
                    return new AtoZFinding(check, AtoZFindingStatus.Blocked,
                        "No evaluator for this source check.", string.Empty);
            }
        }

        private static AtoZFinding MissingEvidence(AtoZCheckDefinition check, string file)
        {
            return new AtoZFinding(check, AtoZFindingStatus.Blocked,
                "Evidence file not supplied (" + file + "); the check cannot be evaluated, never guessed.",
                file + " not supplied");
        }

        /// <summary>Extracts the bakery business block (from "key: bakery" to the next business key) from the config asset text.</summary>
        public static string BakeryBlock(string configAssetText)
        {
            if (string.IsNullOrEmpty(configAssetText))
            {
                return string.Empty;
            }

            string[] lines = configAssetText.Split(new[] { '\n' }, StringSplitOptions.None);
            int start = -1;
            int end = lines.Length;
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed == "- key: bakery")
                {
                    start = i;
                }
                else if (start >= 0 && i > start && trimmed.StartsWith("- key:", StringComparison.Ordinal))
                {
                    end = i;
                    break;
                }
            }

            if (start < 0)
            {
                return string.Empty;
            }

            var block = new List<string>();
            for (int i = start; i < end; i++)
            {
                block.Add(lines[i]);
            }
            return string.Join("\n", block.ToArray());
        }

        /// <summary>Reads an integer config value (e.g. "bakeryFuelUnits: 40") from a block; -1 when absent or unparsable.</summary>
        public static int ConfigIntValue(string block, string key)
        {
            if (string.IsNullOrEmpty(block) || string.IsNullOrEmpty(key))
            {
                return -1;
            }

            string[] lines = block.Split(new[] { '\n' }, StringSplitOptions.None);
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith(key + ":", StringComparison.Ordinal))
                {
                    continue;
                }

                string value = trimmed.Substring(key.Length + 1).Trim();
                int parsed;
                if (int.TryParse(value, out parsed))
                {
                    return parsed;
                }
                return -1;
            }
            return -1;
        }

        private static AtoZFinding CheckWholesaleConfig(AtoZCheckDefinition check, string configText)
        {
            if (string.IsNullOrEmpty(configText))
            {
                return MissingEvidence(check, "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            }

            string block = BakeryBlock(configText);
            if (block.Length == 0)
            {
                return new AtoZFinding(check, AtoZFindingStatus.Unresolved,
                    "No bakery business block found in the config asset; the wholesale obligation cannot be confirmed removed.",
                    "DevelopmentTownConfiguration.asset: no '- key: bakery' block");
            }

            if (block.IndexOf("wholesaleAgreements: []", StringComparison.Ordinal) >= 0)
            {
                return new AtoZFinding(check, AtoZFindingStatus.ImplementedUnverified,
                    "Bakery wholesaleAgreements is empty in config (Lane 9a H-1 removal present in source). Live-run confirmation that no wholesale starvation occurs is still owed.",
                    "Assets/Core/Population/DevelopmentTownConfiguration.asset:1810 wholesaleAgreements: []");
            }

            return new AtoZFinding(check, AtoZFindingStatus.Unresolved,
                "Bakery block still carries wholesale agreements in config.",
                "DevelopmentTownConfiguration.asset bakery block: wholesaleAgreements not empty");
        }

        private static AtoZFinding CheckWholesaleFallback(AtoZCheckDefinition check, string accountsText)
        {
            if (string.IsNullOrEmpty(accountsText))
            {
                return MissingEvidence(check, "Assets/Scripts/Domains/Economy/Equipment/ScenarioOpeningWholesaleAccounts.cs");
            }

            bool booksNothing = accountsText.IndexOf("nothing was booked", StringComparison.Ordinal) >= 0
                && accountsText.IndexOf("No wholesale accounts are configured", StringComparison.Ordinal) >= 0;
            if (booksNothing)
            {
                return new AtoZFinding(check, AtoZFindingStatus.ImplementedUnverified,
                    "No demand-derived fallback: a config with no wholesale entries books nothing (Lane 9a H-1). Live-run confirmation still owed.",
                    "Assets/Scripts/Domains/Economy/Equipment/ScenarioOpeningWholesaleAccounts.cs:147");
            }

            return new AtoZFinding(check, AtoZFindingStatus.Unresolved,
                "The demand-derived fallback is still present or its removal cannot be confirmed from the supplied text.",
                "ScenarioOpeningWholesaleAccounts.cs: no 'nothing was booked' path found");
        }

        private static AtoZFinding CheckByproductSettlement(AtoZCheckDefinition check, string settleText, string transferText)
        {
            if (string.IsNullOrEmpty(settleText))
            {
                return MissingEvidence(check, "Assets/Scripts/Domains/Economy/Core/FuelDealerByproductSettlement.cs");
            }
            if (string.IsNullOrEmpty(transferText))
            {
                return MissingEvidence(check, "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs (TransferSawmillByproductsToFuelDealer)");
            }

            bool preCheck = transferText.IndexOf("BalanceCents < heldPriceCents", StringComparison.Ordinal) >= 0
                && transferText.IndexOf("TransferByproductsToFuelDealer", StringComparison.Ordinal)
                    > transferText.IndexOf("BalanceCents < heldPriceCents", StringComparison.Ordinal);
            bool loudWarning = transferText.IndexOf("byproduct settlement failed after custody moved", StringComparison.Ordinal) >= 0;
            bool settlePays = settleText.IndexOf("TryDebit", StringComparison.Ordinal) >= 0
                && settleText.IndexOf("Credit(", StringComparison.Ordinal) >= 0
                && settleText.IndexOf("ByproductPriceCents", StringComparison.Ordinal) >= 0;

            if (preCheck && loudWarning && settlePays)
            {
                return new AtoZFinding(check, AtoZFindingStatus.SuspectedFix,
                    "Compensating path present in source: affordability is pre-checked before custody moves, settlement is on the measured intake delta, and a settlement failure after custody moved logs loudly. The original SF-7 run failure has not been re-observed in a new run, so this is a SUSPECTED FIX, not a verified one.",
                    "SimulationDrivers.cs:916 pre-check + loud warning; FuelDealerByproductSettlement.cs Settle debits dealer, credits mill @ 1c/u (D-F3)");
            }

            return new AtoZFinding(check, AtoZFindingStatus.Unresolved,
                "Byproduct settlement compensating path incomplete in the supplied source (pre-check: " + preCheck
                + ", loud warning: " + loudWarning + ", settle pays: " + settlePays + ").",
                "FuelDealerByproductSettlement.cs / SimulationDrivers.cs TransferSawmillByproductsToFuelDealer");
        }

        private static AtoZFinding CheckFirewood(AtoZCheckDefinition check, string configText)
        {
            if (string.IsNullOrEmpty(configText))
            {
                return MissingEvidence(check, "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            }

            int count = CountActivityType(configText, 10);
            if (count == 0)
            {
                return new AtoZFinding(check, AtoZFindingStatus.Unresolved,
                    "Zero Firewood activity points authored in the dev-town config (BuildingActivityLocationType.Firewood = 10; config inventory Bed 33, Dining 19, Stove 12, Workshop 10, Oven 1, Counter 1, Receiving 1, Firewood 0). domestic.fuel-handling tasks can never arrive.",
                    "DevelopmentTownConfiguration.asset: 0x 'activityType: 10' (M-13 inventory)");
            }

            return new AtoZFinding(check, AtoZFindingStatus.ImplementedUnverified,
                count + " Firewood activity point(s) authored in config. Live-run confirmation that fuel-handling tasks arrive is still owed.",
                "DevelopmentTownConfiguration.asset: " + count + "x 'activityType: 10'");
        }

        /// <summary>Counts authored activity points of one BuildingActivityLocationType value in a config asset text.</summary>
        public static int CountActivityType(string configAssetText, int activityType)
        {
            if (string.IsNullOrEmpty(configAssetText))
            {
                return 0;
            }

            int count = 0;
            string wanted = "activityType: " + activityType;
            string[] lines = configAssetText.Split(new[] { '\n' }, StringSplitOptions.None);
            foreach (string line in lines)
            {
                if (line.Trim() == wanted)
                {
                    count++;
                }
            }
            return count;
        }

        private static AtoZFinding CheckBakeryFuel(AtoZCheckDefinition check, string configText)
        {
            if (string.IsNullOrEmpty(configText))
            {
                return MissingEvidence(check, "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            }

            string block = BakeryBlock(configText);
            if (block.Length == 0)
            {
                return new AtoZFinding(check, AtoZFindingStatus.Unresolved,
                    "No bakery business block found in the config asset; the bakery fuel configuration cannot be confirmed.",
                    "DevelopmentTownConfiguration.asset: no '- key: bakery' block");
            }

            int intakePerUnit = ConfigIntValue(block, "fuelDealerIntakeUnitsPerBakeryUnit");
            int openingFuel = ConfigIntValue(block, "bakeryFuelUnits");
            if (intakePerUnit == 0)
            {
                return new AtoZFinding(check, AtoZFindingStatus.Unresolved,
                    "Bakery fuel is a CONFIG value, not missing code: fuelDealerIntakeUnitsPerBakeryUnit is 0 with "
                    + openingFuel + "u opening fuel, so the bakery dies on day 41 of every 60-day run and stays dead (E-14). Owner decision on bakery fuel supply is still open.",
                    "DevelopmentTownConfiguration.asset:1807 fuelDealerIntakeUnitsPerBakeryUnit: 0; bakeryFuelUnits: " + openingFuel);
            }

            if (intakePerUnit > 0)
            {
                return new AtoZFinding(check, AtoZFindingStatus.ImplementedUnverified,
                    "Bakery fuel conversion is configured at " + intakePerUnit + " intake unit(s) per bakery fuel unit. Live-run confirmation that the bakery survives past day 41 is still owed.",
                    "DevelopmentTownConfiguration.asset bakery block: fuelDealerIntakeUnitsPerBakeryUnit: " + intakePerUnit);
            }

            return new AtoZFinding(check, AtoZFindingStatus.Unresolved,
                "fuelDealerIntakeUnitsPerBakeryUnit is absent or unparsable in the bakery block.",
                "DevelopmentTownConfiguration.asset bakery block");
        }

        private static AtoZFinding CheckGenericStaple(AtoZCheckDefinition check, string mealsText)
        {
            if (string.IsNullOrEmpty(mealsText))
            {
                return MissingEvidence(check, "Assets/Scripts/Domains/Population/HouseholdMeals.cs");
            }

            bool guard = mealsText.IndexOf("IsUnspecifiedStaple", StringComparison.Ordinal) >= 0
                && mealsText.IndexOf("Never a", StringComparison.Ordinal) >= 0
                && mealsText.IndexOf("UnspecifiedStapleFoodId", StringComparison.Ordinal) >= 0;
            if (guard)
            {
                return new AtoZFinding(check, AtoZFindingStatus.ImplementedUnverified,
                    "Generic unspecified staple is never a servable meal item, never counted toward supply, never reported as a meal item (Lane 9a A8 guard in HouseholdMeals). Live-run confirmation that zero meals are served from staple_food_unspecified is still owed.",
                    "Assets/Scripts/Domains/Population/HouseholdMeals.cs:635 IsUnspecifiedStaple");
            }

            return new AtoZFinding(check, AtoZFindingStatus.Unresolved,
                "No IsUnspecifiedStaple guard found in the supplied HouseholdMeals text; generic staple_food may still satisfy meals.",
                "HouseholdMeals.cs: no IsUnspecifiedStaple guard");
        }

        private static AtoZFinding CheckMissedMealReason(AtoZCheckDefinition check, string mealsText)
        {
            if (string.IsNullOrEmpty(mealsText))
            {
                return MissingEvidence(check, "Assets/Scripts/Domains/Population/HouseholdMeals.cs");
            }

            bool hasEnum = mealsText.IndexOf("MissedMealReasonKind", StringComparison.Ordinal) >= 0
                && mealsText.IndexOf("ReasonKind", StringComparison.Ordinal) >= 0;
            if (hasEnum)
            {
                return new AtoZFinding(check, AtoZFindingStatus.ImplementedUnverified,
                    "MissedMealReasonKind enum exists and is carried on the missed-meal record (Lane 9a A5). Live-run confirmation that missed meals are classified at close time instead of listed UNCLASSIFIED is still owed.",
                    "Assets/Scripts/Domains/Population/HouseholdMeals.cs:86 MissedMealReasonKind");
            }

            return new AtoZFinding(check, AtoZFindingStatus.Unresolved,
                "No MissedMealReasonKind classification found in the supplied HouseholdMeals text.",
                "HouseholdMeals.cs: no MissedMealReasonKind");
        }
    }
}
