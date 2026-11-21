// Land & Ledgers — automated run acceptance report (Lane 8, Agent N §3).
//
// Replaces "calendar completed" as the verdict with six explicit sections —
// calendar completed, economic integrity, household survival, physical
// legitimacy, Save/Continue, diagnostic health — each reported separately so
// a 60-day calendar completion with a broken economy cannot read as a pass.
// A run is ACCEPTED only when all six sections pass; otherwise the report
// names the failing section, the failing check, and the evidence.
//
// Routine vs actionable, per Agent N §3.1: routine logs are COUNTED and
// listed in a per-section "routine volume" line, never hidden and never
// counted as diagnostics. A section that cannot measure something writes
// NOT_CAPTURED with the seam name — never zero, never a guess. The report
// is generated post-run from the run's own artifacts; the generator needs
// no live Unity session.
//
// "Zero warnings" is NOT the bar: the bar is zero unexplained or invalid
// warning conditions, with legitimate hardship reported appropriately.

using System;
using System.Collections.Generic;
using System.Text;

namespace LandLedgers.Diagnostics
{
    /// <summary>Verdict of one acceptance section or check.</summary>
    public enum RunAcceptanceVerdict
    {
        /// <summary>Every check in the section passes.</summary>
        Pass = 0,

        /// <summary>At least one check fails; the report names it.</summary>
        Fail = 1,

        /// <summary>The section did not run (e.g. no Continue midpoint) — not a pass.</summary>
        NotRun = 2,

        /// <summary>The section cannot measure its checks: a seam is missing. Named, never zero, never a guess.</summary>
        NotCaptured = 3,

        /// <summary>Evidence satisfies proposed checks that do not yet have owner approval.</summary>
        Provisional = 4,
    }

    /// <summary>One of the six acceptance sections (Agent N §3.2–§3.7).</summary>
    public enum RunAcceptanceSectionKind
    {
        /// <summary>§3.2 Calendar completed.</summary>
        Calendar = 0,

        /// <summary>§3.3 Economic integrity.</summary>
        EconomicIntegrity = 1,

        /// <summary>§3.4 Household survival.</summary>
        HouseholdSurvival = 2,

        /// <summary>§3.5 Physical legitimacy.</summary>
        PhysicalLegitimacy = 3,

        /// <summary>§3.6 Save/Continue.</summary>
        SaveContinue = 4,

        /// <summary>§3.7 Diagnostic health.</summary>
        DiagnosticHealth = 5,
    }

    /// <summary>One check inside an acceptance section.</summary>
    public sealed class RunAcceptanceCheck
    {
        public RunAcceptanceCheck(string name, RunAcceptanceVerdict verdict, string evidence)
        {
            Name = name ?? string.Empty;
            Verdict = verdict;
            Evidence = evidence ?? string.Empty;
        }

        /// <summary>Check name.</summary>
        public string Name { get; }

        /// <summary>Check verdict.</summary>
        public RunAcceptanceVerdict Verdict { get; }

        /// <summary>Evidence pointer (artifact + row/sequence, or the seam name when NOT_CAPTURED).</summary>
        public string Evidence { get; }
    }

    /// <summary>One of the six sections, with its checks and its verdict.</summary>
    public sealed class RunAcceptanceSection
    {
        public RunAcceptanceSection(RunAcceptanceSectionKind kind, RunAcceptanceVerdict verdict, List<RunAcceptanceCheck> checks, string routineVolume)
        {
            Kind = kind;
            Verdict = verdict;
            Checks = checks ?? new List<RunAcceptanceCheck>();
            RoutineVolume = routineVolume ?? string.Empty;
        }

        /// <summary>Section kind.</summary>
        public RunAcceptanceSectionKind Kind { get; }

        /// <summary>Section verdict.</summary>
        public RunAcceptanceVerdict Verdict { get; }

        /// <summary>Checks in this section.</summary>
        public List<RunAcceptanceCheck> Checks { get; }

        /// <summary>Routine-volume line: routine logs counted, never hidden, never counted as diagnostics.</summary>
        public string RoutineVolume { get; }

        /// <summary>Section title (Agent N §3).</summary>
        public string Title
        {
            get
            {
                switch (Kind)
                {
                    case RunAcceptanceSectionKind.Calendar: return "Calendar completed";
                    case RunAcceptanceSectionKind.EconomicIntegrity: return "Economic integrity";
                    case RunAcceptanceSectionKind.HouseholdSurvival: return "Household survival";
                    case RunAcceptanceSectionKind.PhysicalLegitimacy: return "Physical legitimacy";
                    case RunAcceptanceSectionKind.SaveContinue: return "Save/Continue";
                    case RunAcceptanceSectionKind.DiagnosticHealth: return "Diagnostic health";
                    default: return "Unknown";
                }
            }
        }
    }

    /// <summary>Overall verdict of the acceptance report.</summary>
    public enum RunAcceptanceOverall
    {
        /// <summary>All six sections pass.</summary>
        Accepted = 0,

        /// <summary>At least one section fails.</summary>
        Failed = 1,

        /// <summary>No live run was evaluated.</summary>
        NotRun = 2,

        /// <summary>One or more sections cannot measure their checks; nothing is accepted on a guess.</summary>
        NotCaptured = 3,

        /// <summary>Measured criteria pass, but an owner-controlled acceptance rule remains unapproved.</summary>
        Provisional = 4,
    }

    /// <summary>The six-section run acceptance report (Agent N §3).</summary>
    public sealed class RunAcceptanceReport
    {
        public RunAcceptanceReport(string runId, RunAcceptanceOverall overall, List<RunAcceptanceSection> sections, string firstFailure)
        {
            RunId = runId ?? string.Empty;
            Overall = overall;
            Sections = sections ?? new List<RunAcceptanceSection>();
            FirstFailure = firstFailure ?? string.Empty;
        }

        /// <summary>Run id evaluated.</summary>
        public string RunId { get; }

        /// <summary>Overall verdict. Accepted only when all six sections pass.</summary>
        public RunAcceptanceOverall Overall { get; }

        /// <summary>The six sections, in order.</summary>
        public List<RunAcceptanceSection> Sections { get; }

        /// <summary>First failing section/check, in words; empty when Accepted.</summary>
        public string FirstFailure { get; }

        /// <summary>True only when the run is Accepted.</summary>
        public bool Accepted
        {
            get { return Overall == RunAcceptanceOverall.Accepted; }
        }

        /// <summary>Section by kind; null when absent.</summary>
        public RunAcceptanceSection Section(RunAcceptanceSectionKind kind)
        {
            foreach (RunAcceptanceSection section in Sections)
            {
                if (section.Kind == kind)
                {
                    return section;
                }
            }
            return null;
        }

        /// <summary>Human-readable report (run-acceptance-report.md content).</summary>
        public string ToMarkdown()
        {
            var lines = new List<string>();
            lines.Add("# Run acceptance report — " + RunId);
            lines.Add(string.Empty);
            switch (Overall)
            {
                case RunAcceptanceOverall.Accepted:
                    lines.Add("Overall: ACCEPTED — all six sections pass.");
                    break;
                case RunAcceptanceOverall.Failed:
                    lines.Add("Overall: FAILED — " + FirstFailure);
                    break;
                case RunAcceptanceOverall.Provisional:
                    lines.Add("Overall: PROVISIONAL — " + FirstFailure);
                    break;
                case RunAcceptanceOverall.NotRun:
                    lines.Add("Overall: NOT RUN — no live run was evaluated. Calendar completion alone accepts nothing.");
                    break;
                default:
                    lines.Add("Overall: NOT CAPTURED — " + FirstFailure);
                    break;
            }
            lines.Add(string.Empty);
            lines.Add("A run is ACCEPTED only when all six sections pass and owner-controlled criteria are approved. A pass in the calendar section accepts nothing else.");
            lines.Add(string.Empty);
            foreach (RunAcceptanceSection section in Sections)
            {
                lines.Add("## " + section.Title + " — " + VerdictLabel(section.Verdict));
                if (section.RoutineVolume.Length > 0)
                {
                    lines.Add("Routine volume: " + section.RoutineVolume);
                }
                foreach (RunAcceptanceCheck check in section.Checks)
                {
                    lines.Add("- [" + VerdictLabel(check.Verdict) + "] " + check.Name + " — " + check.Evidence);
                }
                lines.Add(string.Empty);
            }
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>Machine-readable report (run-acceptance.json content). Hand-rolled: no JSON library in the harness closure.</summary>
        public string ToJson()
        {
            var builder = new StringBuilder();
            builder.Append("{\"runId\":");
            AppendJsonString(builder, RunId);
            builder.Append(",\"overall\":");
            AppendJsonString(builder, OverallLabel(Overall));
            builder.Append(",\"firstFailure\":");
            AppendJsonString(builder, FirstFailure);
            builder.Append(",\"sections\":[");
            for (int i = 0; i < Sections.Count; i++)
            {
                RunAcceptanceSection section = Sections[i];
                if (i > 0)
                {
                    builder.Append(',');
                }
                builder.Append("{\"kind\":");
                AppendJsonString(builder, section.Title);
                builder.Append(",\"verdict\":");
                AppendJsonString(builder, VerdictLabel(section.Verdict));
                builder.Append(",\"routineVolume\":");
                AppendJsonString(builder, section.RoutineVolume);
                builder.Append(",\"checks\":[");
                for (int j = 0; j < section.Checks.Count; j++)
                {
                    RunAcceptanceCheck check = section.Checks[j];
                    if (j > 0)
                    {
                        builder.Append(',');
                    }
                    builder.Append("{\"name\":");
                    AppendJsonString(builder, check.Name);
                    builder.Append(",\"verdict\":");
                    AppendJsonString(builder, VerdictLabel(check.Verdict));
                    builder.Append(",\"evidence\":");
                    AppendJsonString(builder, check.Evidence);
                    builder.Append('}');
                }
                builder.Append("]}");
            }
            builder.Append("]}");
            return builder.ToString();
        }

        /// <summary>Stable label for a section/check verdict.</summary>
        public static string VerdictLabel(RunAcceptanceVerdict verdict)
        {
            switch (verdict)
            {
                case RunAcceptanceVerdict.Pass: return "PASS";
                case RunAcceptanceVerdict.Fail: return "FAIL";
                case RunAcceptanceVerdict.NotRun: return "NOT RUN";
                case RunAcceptanceVerdict.Provisional: return "PROVISIONAL";
                default: return "NOT CAPTURED";
            }
        }

        /// <summary>Stable label for the overall verdict.</summary>
        public static string OverallLabel(RunAcceptanceOverall overall)
        {
            switch (overall)
            {
                case RunAcceptanceOverall.Accepted: return "ACCEPTED";
                case RunAcceptanceOverall.Failed: return "FAILED";
                case RunAcceptanceOverall.NotRun: return "NOT RUN";
                case RunAcceptanceOverall.Provisional: return "PROVISIONAL";
                default: return "NOT CAPTURED";
            }
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');
            if (value != null)
            {
                foreach (char c in value)
                {
                    switch (c)
                    {
                        case '"': builder.Append("\\\""); break;
                        case '\\': builder.Append("\\\\"); break;
                        case '\n': builder.Append("\\n"); break;
                        case '\r': builder.Append("\\r"); break;
                        case '\t': builder.Append("\\t"); break;
                        default: builder.Append(c); break;
                    }
                }
            }
            builder.Append('"');
        }
    }

    /// <summary>
    /// Lane 8 (Agent N §3) — builds the six-section run acceptance report
    /// from a run's structured summary and its survival-test result. Pure
    /// functions over supplied evidence; the Editor window supplies the
    /// artifacts, tests supply fixtures.
    /// </summary>
    public static class RunAcceptanceReportBuilder
    {
        /// <summary>Day-1 business cash the config authorises (D-36 standing check: config 162,000c).</summary>
        public const int ConfigDay1BusinessCashCents = 162000;

        /// <summary>Builds the report. With no summary the report is NOT RUN. Calendar completion alone never yields ACCEPTED.</summary>
        public static RunAcceptanceReport Build(
            RunSummary summary, SurvivalAcceptanceResult survival, bool continueMidpointRun)
        {
            if (summary == null)
            {
                var sections = new List<RunAcceptanceSection>();
                foreach (RunAcceptanceSectionKind kind in AllKinds())
                {
                    sections.Add(new RunAcceptanceSection(kind, RunAcceptanceVerdict.NotRun,
                        new List<RunAcceptanceCheck>
                        {
                            new RunAcceptanceCheck("No live run evaluated", RunAcceptanceVerdict.NotRun, "No run exists on this HEAD."),
                        }, string.Empty));
                }
                return new RunAcceptanceReport(string.Empty, RunAcceptanceOverall.NotRun, sections,
                    "No live run was evaluated.");
            }

            var built = new List<RunAcceptanceSection>();
            built.Add(BuildCalendar(summary));
            built.Add(BuildEconomicIntegrity(summary));
            built.Add(BuildHouseholdSurvival(summary, survival));
            built.Add(BuildPhysicalLegitimacy(summary));
            built.Add(BuildSaveContinue(summary, continueMidpointRun));
            built.Add(BuildDiagnosticHealth(summary));

            string firstFailure = string.Empty;
            bool anyFail = false;
            bool anyNotCaptured = false;
            bool anyProvisional = false;
            foreach (RunAcceptanceSection section in built)
            {
                if (section.Verdict == RunAcceptanceVerdict.Fail && !anyFail)
                {
                    anyFail = true;
                    foreach (RunAcceptanceCheck check in section.Checks)
                    {
                        if (check.Verdict == RunAcceptanceVerdict.Fail)
                        {
                            firstFailure = section.Title + ": " + check.Name + " — " + check.Evidence;
                            break;
                        }
                    }
                    if (firstFailure.Length == 0)
                    {
                        firstFailure = section.Title + " failed.";
                    }
                }
                if (section.Verdict == RunAcceptanceVerdict.NotCaptured)
                {
                    anyNotCaptured = true;
                }
                if (section.Verdict == RunAcceptanceVerdict.Provisional)
                {
                    anyProvisional = true;
                    if (firstFailure.Length == 0)
                    {
                        foreach (RunAcceptanceCheck check in section.Checks)
                        {
                            if (check.Verdict == RunAcceptanceVerdict.Provisional)
                            {
                                firstFailure = check.Evidence;
                                break;
                            }
                        }
                    }
                }
            }

            RunAcceptanceOverall overall;
            if (anyFail)
            {
                overall = RunAcceptanceOverall.Failed;
            }
            else if (anyNotCaptured)
            {
                overall = RunAcceptanceOverall.NotCaptured;
                if (firstFailure.Length == 0)
                {
                    firstFailure = "One or more sections cannot measure their checks; nothing is accepted on a guess.";
                }
            }
            else
            {
                bool allPassOrProvisional = true;
                foreach (RunAcceptanceSection section in built)
                {
                    if (section.Verdict != RunAcceptanceVerdict.Pass
                        && section.Verdict != RunAcceptanceVerdict.Provisional)
                    {
                        allPassOrProvisional = false;
                    }
                }
                overall = allPassOrProvisional
                    ? (anyProvisional ? RunAcceptanceOverall.Provisional : RunAcceptanceOverall.Accepted)
                    : RunAcceptanceOverall.NotRun;
                if (overall == RunAcceptanceOverall.Provisional && firstFailure.Length == 0)
                {
                    firstFailure = "One or more owner-controlled criteria remain proposed; the measured result is provisional.";
                }
                if (!allPassOrProvisional && firstFailure.Length == 0)
                {
                    firstFailure = "One or more sections did not run (e.g. no Continue midpoint); the run is not accepted.";
                }
            }

            return new RunAcceptanceReport(summary.RunId, overall, built, firstFailure);
        }

        private static List<RunAcceptanceSectionKind> AllKinds()
        {
            return new List<RunAcceptanceSectionKind>
            {
                RunAcceptanceSectionKind.Calendar,
                RunAcceptanceSectionKind.EconomicIntegrity,
                RunAcceptanceSectionKind.HouseholdSurvival,
                RunAcceptanceSectionKind.PhysicalLegitimacy,
                RunAcceptanceSectionKind.SaveContinue,
                RunAcceptanceSectionKind.DiagnosticHealth,
            };
        }

        /// <summary>Combines checks; provisional checks never become Pass.</summary>
        public static RunAcceptanceVerdict Combine(IReadOnlyList<RunAcceptanceCheck> checks)
        {
            bool anyNotCaptured = false;
            bool anyNotRun = false;
            bool anyProvisional = false;
            foreach (RunAcceptanceCheck check in checks)
            {
                if (check.Verdict == RunAcceptanceVerdict.Fail)
                {
                    return RunAcceptanceVerdict.Fail;
                }
                if (check.Verdict == RunAcceptanceVerdict.NotCaptured)
                {
                    anyNotCaptured = true;
                }
                if (check.Verdict == RunAcceptanceVerdict.NotRun)
                {
                    anyNotRun = true;
                }
                if (check.Verdict == RunAcceptanceVerdict.Provisional)
                {
                    anyProvisional = true;
                }
            }
            if (anyNotCaptured)
            {
                return RunAcceptanceVerdict.NotCaptured;
            }
            if (anyNotRun)
            {
                return RunAcceptanceVerdict.NotRun;
            }
            return anyProvisional ? RunAcceptanceVerdict.Provisional : RunAcceptanceVerdict.Pass;
        }

        private static RunAcceptanceSection BuildCalendar(RunSummary summary)
        {
            var checks = new List<RunAcceptanceCheck>();
            RunIdentity identity = summary.Identity;

            if (identity.TargetDays >= 0 && summary.DaysCompleted >= 0)
            {
                checks.Add(summary.DaysCompleted == identity.TargetDays
                    ? new RunAcceptanceCheck("Requested days completed (" + summary.DaysCompleted + "/" + identity.TargetDays + ")", RunAcceptanceVerdict.Pass, "metadata.json completedDays=" + summary.DaysCompleted + ", targetDays=" + identity.TargetDays)
                    : new RunAcceptanceCheck("Requested days completed (" + summary.DaysCompleted + "/" + identity.TargetDays + ")", RunAcceptanceVerdict.Fail, "metadata.json completedDays=" + summary.DaysCompleted + ", targetDays=" + identity.TargetDays));
            }
            else
            {
                checks.Add(new RunAcceptanceCheck("Requested days completed", RunAcceptanceVerdict.NotCaptured, "Seam: metadata.json targetDays/completedDays"));
            }

            if (summary.ActualResidents >= 0 && summary.ActualHouseholds >= 0 && summary.ActualBusinesses >= 0)
            {
                bool gate = summary.ActualResidents == SurvivalAcceptanceScenario.ExpectedResidents
                    && summary.ActualHouseholds == SurvivalAcceptanceScenario.ExpectedHouseholds
                    && summary.ActualBusinesses == SurvivalAcceptanceScenario.ExpectedBusinesses;
                checks.Add(new RunAcceptanceCheck("Startup gate counts (14 residents / 8 households / 7 businesses)",
                    gate ? RunAcceptanceVerdict.Pass : RunAcceptanceVerdict.Fail,
                    "metadata.json actualResidents=" + summary.ActualResidents + ", actualHouseholds=" + summary.ActualHouseholds + ", actualBusinesses=" + summary.ActualBusinesses));
            }
            else
            {
                checks.Add(new RunAcceptanceCheck("Startup gate counts", RunAcceptanceVerdict.NotCaptured, "Seam: metadata.json actualResidents/actualHouseholds/actualBusinesses"));
            }

            if (summary.MissingDailySnapshotCount >= 0)
            {
                checks.Add(summary.MissingDailySnapshotCount == 0
                    ? new RunAcceptanceCheck("Zero missing daily snapshots", RunAcceptanceVerdict.Pass, "metadata.json missingDailySnapshotOffsets=[]")
                    : new RunAcceptanceCheck("Zero missing daily snapshots", RunAcceptanceVerdict.Fail, "metadata.json missingDailySnapshotOffsets count=" + summary.MissingDailySnapshotCount));
            }
            else
            {
                checks.Add(new RunAcceptanceCheck("Zero missing daily snapshots", RunAcceptanceVerdict.NotCaptured, "Seam: metadata.json missingDailySnapshotOffsets"));
            }

            if (summary.UnexpectedDomainReloadCount >= 0)
            {
                checks.Add(summary.UnexpectedDomainReloadCount == 0
                    ? new RunAcceptanceCheck("Zero unexpected domain reloads", RunAcceptanceVerdict.Pass, "metadata.json unexpectedDomainReloadCount=0")
                    : new RunAcceptanceCheck("Zero unexpected domain reloads", RunAcceptanceVerdict.Fail, "metadata.json unexpectedDomainReloadCount=" + summary.UnexpectedDomainReloadCount));
            }
            else
            {
                checks.Add(new RunAcceptanceCheck("Zero unexpected domain reloads", RunAcceptanceVerdict.NotCaptured, "Seam: metadata.json unexpectedDomainReloadCount"));
            }

            string routine = summary.RoutineLogCount + " routine Log events; " + summary.TotalConsoleEvents + " console events total.";
            return new RunAcceptanceSection(RunAcceptanceSectionKind.Calendar, Combine(checks), checks, routine);
        }

        private static RunAcceptanceSection BuildEconomicIntegrity(RunSummary summary)
        {
            var checks = new List<RunAcceptanceCheck>();

            checks.Add(summary.DoubleSettlements == 0
                ? new RunAcceptanceCheck("No double settlement (no receipt id in two settle lines)", RunAcceptanceVerdict.Pass, "console-events.jsonl receipt matching: 0 double settlements")
                : new RunAcceptanceCheck("No double settlement (no receipt id in two settle lines)", RunAcceptanceVerdict.Fail, "console-events.jsonl receipt matching: " + summary.DoubleSettlements + " double settlement(s)"));

            if (summary.Day1BusinessCashCents >= 0)
            {
                int gap = ConfigDay1BusinessCashCents - summary.Day1BusinessCashCents;
                checks.Add(gap == 0
                    ? new RunAcceptanceCheck("Day-1 business cash reconciles to config (D-36)", RunAcceptanceVerdict.Pass, "daily-summary.csv day 1 businessCashCents=" + summary.Day1BusinessCashCents + " == config 162,000c")
                    : new RunAcceptanceCheck("Day-1 business cash reconciles to config (D-36)", RunAcceptanceVerdict.Fail, "daily-summary.csv day 1 businessCashCents=" + summary.Day1BusinessCashCents + " vs config 162,000c: unattributed gap of " + gap + "c. The gap must be shown on day 1 of every run until attributed."));
            }
            else
            {
                checks.Add(new RunAcceptanceCheck("Day-1 business cash reconciles to config (D-36)", RunAcceptanceVerdict.NotCaptured, "Seam: daily-summary.csv businessCashCents"));
            }

            checks.Add(new RunAcceptanceCheck("Day-boundary cash conservation to the cent (E C-01)", RunAcceptanceVerdict.NotCaptured, "Seam: transaction-event stream (N-01/N-02) — settle lines carry debit/credit, but no day-boundary reconciliation seam exists yet"));
            checks.Add(new RunAcceptanceCheck("Identified lots conserved across transfers (E C-06)", RunAcceptanceVerdict.NotCaptured, "Seam: entity-snapshots.jsonl lot entries — not parsed by this reader yet"));
            checks.Add(new RunAcceptanceCheck("No production without required inputs (E C-09)", RunAcceptanceVerdict.NotCaptured, "Seam: production driver records — no daily aggregate exists yet"));

            string routine = summary.SettleLineCount + " settle lines; " + summary.ReceiveLineCount + " receive lines; " + summary.ReceiptIdsMatched + " receipt ids matched.";
            return new RunAcceptanceSection(RunAcceptanceSectionKind.EconomicIntegrity, Combine(checks), checks, routine);
        }

        private static RunAcceptanceSection BuildHouseholdSurvival(RunSummary summary, SurvivalAcceptanceResult survival)
        {
            var checks = new List<RunAcceptanceCheck>();

            // Days 1–7 control period (P-1): 42/42, 0 missed. The days 11–15
            // bar (P-2) is judged by the survival-test check below, not here.
            if (summary.MealsEatenPerDay.Count >= 7 && summary.MealsMissedPerDay.Count >= 7)
            {
                bool control = true;
                string controlEvidence = string.Empty;
                for (int day = 0; day < 7; day++)
                {
                    if (summary.MealsEatenPerDay[day] != SurvivalAcceptanceScenario.MealSlotsPerDay
                        || summary.MealsMissedPerDay[day] != 0)
                    {
                        control = false;
                        controlEvidence = "daily-summary.csv day " + (day + 1) + ": eaten=" + summary.MealsEatenPerDay[day] + ", missed=" + summary.MealsMissedPerDay[day];
                        break;
                    }
                }
                checks.Add(new RunAcceptanceCheck("Days 1–7 control period: 42/42 slots eaten, 0 missed (P-1)",
                    control ? RunAcceptanceVerdict.Pass : RunAcceptanceVerdict.Fail,
                    control ? "daily-summary.csv days 1–7: 42 eaten / 0 missed every day" : controlEvidence));
            }
            else
            {
                checks.Add(new RunAcceptanceCheck("Days 1–7 control period: 42/42 slots eaten, 0 missed (P-1)", RunAcceptanceVerdict.NotCaptured, "Seam: daily-summary.csv mealsEaten/mealsMissed for days 1–7"));
            }

            checks.Add(new RunAcceptanceCheck("Meal totals across the run",
                RunAcceptanceVerdict.Pass,
                "daily-summary.csv totals: eaten=" + summary.MealsEaten + ", missed=" + summary.MealsMissed + " (reported, not judged here — the bars judge them)"));

            if (survival == null)
            {
                checks.Add(new RunAcceptanceCheck("Survival acceptance test (§2: eight stages, eight bars)", RunAcceptanceVerdict.NotRun, "No survival result supplied: the Scenario Lab has not evaluated this run."));
            }
            else if (survival.Overall == SurvivalOverallVerdict.Accepted)
            {
                checks.Add(new RunAcceptanceCheck("Survival acceptance test (§2: eight stages, eight bars)", RunAcceptanceVerdict.Pass, "Scenario Lab: all eight stages and all eight bars pass on run " + survival.RunId));
            }
            else if (survival.Overall == SurvivalOverallVerdict.Failed)
            {
                checks.Add(new RunAcceptanceCheck("Survival acceptance test (§2: eight stages, eight bars)", RunAcceptanceVerdict.Fail, "Scenario Lab: " + survival.FirstFailure));
            }
            else if (survival.Overall == SurvivalOverallVerdict.Provisional)
            {
                checks.Add(new RunAcceptanceCheck("Survival acceptance test (§2: eight stages, eight bars)",
                    RunAcceptanceVerdict.Provisional,
                    "Scenario Lab: " + survival.AcceptanceNote + " Run configuration: " + survival.ConfigurationSummary));
            }
            else
            {
                checks.Add(new RunAcceptanceCheck("Survival acceptance test (§2: eight stages, eight bars)", RunAcceptanceVerdict.NotRun, "Scenario Lab: " + survival.FirstFailure));
            }

            checks.Add(new RunAcceptanceCheck("Missed-meal classification (A5 enum)", RunAcceptanceVerdict.NotCaptured, "Seam: MissedMealRecord + A5 enum — NOT_CAPTURED until A5 lands; until then missed meals are UNCLASSIFIED"));
            checks.Add(new RunAcceptanceCheck("Heating / fuel burned per day", RunAcceptanceVerdict.NotCaptured, "Seam: daily fuel burned — Lane 9b landed the ResolveDailyUse daily drive, so the heating economy is no longer dormant; the daily fuel-burned figures are NOT_CAPTURED until a run records them, listed as a failing check, not as zero burn"));

            string routine = "Household lots trajectory: " + LotTrajectory(summary) + ".";
            return new RunAcceptanceSection(RunAcceptanceSectionKind.HouseholdSurvival, Combine(checks), checks, routine);
        }

        private static string LotTrajectory(RunSummary summary)
        {
            if (summary.HouseholdLotUnits.Count == 0)
            {
                return "NOT_CAPTURED";
            }
            int first = summary.HouseholdLotUnits[0];
            int last = summary.HouseholdLotUnits[summary.HouseholdLotUnits.Count - 1];
            return first + " -> " + last + " household lot units over " + summary.HouseholdLotUnits.Count + " days";
        }

        private static RunAcceptanceSection BuildPhysicalLegitimacy(RunSummary summary)
        {
            var checks = new List<RunAcceptanceCheck>();

            checks.Add(summary.TripsAbortedNoRoute == 0
                ? new RunAcceptanceCheck("Zero trips aborted for 'no physical route'", RunAcceptanceVerdict.Pass, "console-events.jsonl: 0 [ShoppingTrip] ABORTED lines")
                : new RunAcceptanceCheck("Zero trips aborted for 'no physical route'", RunAcceptanceVerdict.Fail, "console-events.jsonl: " + summary.TripsAbortedNoRoute + " trip(s) ABORTED — No physical route reaches seller location"));

            checks.Add(summary.TravelWithoutArrivalCount == 0
                ? new RunAcceptanceCheck("Zero travel-task-succeeded-without-arrival anomalies", RunAcceptanceVerdict.Pass, "trace recorder: 0 travel-task-succeeded-without-arrival")
                : new RunAcceptanceCheck("Zero travel-task-succeeded-without-arrival anomalies", RunAcceptanceVerdict.Fail, "trace recorder: " + summary.TravelWithoutArrivalCount + " travel-task-succeeded-without-arrival anomaly/anomalies"));

            checks.Add(summary.TaskDisappearedCount == 0
                ? new RunAcceptanceCheck("Zero task-disappeared-without-terminal anomalies", RunAcceptanceVerdict.Pass, "trace recorder: 0 task-disappeared-without-terminal")
                : new RunAcceptanceCheck("Zero task-disappeared-without-terminal anomalies", RunAcceptanceVerdict.Fail, "trace recorder: " + summary.TaskDisappearedCount + " task-disappeared-without-terminal anomaly/anomalies"));

            checks.Add(new RunAcceptanceCheck("Zero tasks with 0 worked minutes after 72 simulated hours", RunAcceptanceVerdict.NotCaptured, "Seam: trace recorder task-no-progress listing (" + summary.TaskNoProgressCount + " task-no-progress anomalies counted, but the 72-hour zero-work rule needs per-task worked minutes)"));

            string droppedText = summary.DroppedTraceEvents >= 0
                ? summary.DroppedTraceEvents.ToString()
                : "NOT_CAPTURED (seam: npc-activity-manifest.json)";
            string routine = "Route outcomes grouped by (signature, destination): " + summary.Signatures.Count + " distinct pairs; "
                + "trace dropped events: " + droppedText + " (a high drop count caps the confidence of every other check in this section).";
            return new RunAcceptanceSection(RunAcceptanceSectionKind.PhysicalLegitimacy, Combine(checks), checks, routine);
        }

        private static RunAcceptanceSection BuildSaveContinue(RunSummary summary, bool continueMidpointRun)
        {
            var checks = new List<RunAcceptanceCheck>();

            if (!continueMidpointRun)
            {
                checks.Add(new RunAcceptanceCheck("Continue midpoint executed (save day 7, fresh-scene load, resume to day 15)", RunAcceptanceVerdict.NotRun,
                    "A run with no Continue midpoint gets this section marked NOT RUN, not passed. A 60-day run that never saves proves nothing about Save/Continue."));
                return new RunAcceptanceSection(RunAcceptanceSectionKind.SaveContinue, RunAcceptanceVerdict.NotRun, checks,
                    "No Save/Continue boundary in this run.");
            }

            checks.Add(new RunAcceptanceCheck("Pre/post equality across the Continue boundary", RunAcceptanceVerdict.NotCaptured, "Seam: §07 Save/Continue State Comparator (Tool 07, Lane 7) exists, but no Continue-midpoint snapshots exist in this run; until a run saves mid-way, the comparison is PARTIAL"));
            checks.Add(summary.DoubleSettlements == 0
                ? new RunAcceptanceCheck("No duplicate settlement across the boundary", RunAcceptanceVerdict.Pass, "console-events.jsonl receipt matching: 0 double settlements")
                : new RunAcceptanceCheck("No duplicate settlement across the boundary", RunAcceptanceVerdict.Fail, "console-events.jsonl receipt matching: " + summary.DoubleSettlements + " double settlement(s)"));
            checks.Add(new RunAcceptanceCheck("Known defects recorded, not skipped (F-S4/F-S5/F-S6)", RunAcceptanceVerdict.Fail,
                "F-S4 now has business DTO capture/restore and focused process/task fixtures, but fresh-scene Main Scene Continue and consumed-input conservation remain unverified. F-S5 fresh-scene drops Pass 9/Millbrook/procurement and F-S6 spurious cabinet want remain open. These known risks stay visible; no persistence claim is inferred from source presence."));

            return new RunAcceptanceSection(RunAcceptanceSectionKind.SaveContinue, Combine(checks), checks,
                "Continue midpoint executed; pre/post comparison PARTIAL until Tool 07 lands.");
        }

        private static RunAcceptanceSection BuildDiagnosticHealth(RunSummary summary)
        {
            var checks = new List<RunAcceptanceCheck>();

            checks.Add(summary.ErrorCount == 0
                ? new RunAcceptanceCheck("Zero errors / exceptions / asserts", RunAcceptanceVerdict.Pass, "console-events.jsonl: 0 Error/Exception/Assert events")
                : new RunAcceptanceCheck("Zero errors / exceptions / asserts", RunAcceptanceVerdict.Fail, "console-events.jsonl: " + summary.ErrorCount + " error(s)"));

            checks.Add(new RunAcceptanceCheck("Signature table with (signature, destination) grouping and §22 register status",
                summary.Signatures.Count > 0 ? RunAcceptanceVerdict.Pass : RunAcceptanceVerdict.NotCaptured,
                "console-events.jsonl: " + summary.Signatures.Count + " distinct (signature, destination) pairs; register status per signature is NOT_CAPTURED until the §22 register is wired to the report"));

            checks.Add(new RunAcceptanceCheck("Every WARNING signature has a register entry with a suspected/confirmed cause", RunAcceptanceVerdict.NotCaptured, "Seam: §22 register lookup — not wired to the report yet"));
            checks.Add(new RunAcceptanceCheck("UNKNOWN-cause warnings older than 7 days: zero", RunAcceptanceVerdict.NotCaptured, "Seam: console-text → incident classification — Lane 1 landed the live warning core (RefusalCauseClassifier + IncidentWarningAuthority), but classifying run text into incidents is a different pipeline and stays PROVISIONAL until it is wired"));
            checks.Add(new RunAcceptanceCheck("Escalations applied (7-day standing, 3-day missed meals, 14-day open incident)", RunAcceptanceVerdict.NotCaptured, "Seam: [Incident] lifecycle events — escalation recomputation from console text is PROVISIONAL"));

            string routine = summary.RoutineLogCount + " routine Log events reported, not hidden, not counted as diagnostics. "
                + "\"Zero warnings\" is NOT the bar: the bar is zero unexplained or invalid warning conditions.";
            return new RunAcceptanceSection(RunAcceptanceSectionKind.DiagnosticHealth, Combine(checks), checks, routine);
        }
    }
}
