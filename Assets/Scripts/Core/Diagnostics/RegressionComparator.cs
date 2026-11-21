// Land & Ledgers — Regression Comparator (Lane 8, Tool 11, Agent N §3.7–§3.8).
//
// Compares two runs' structured outputs — warning/error counts and
// signatures, meal outcomes, shopping, production, freight, inventories,
// transactions — and reports per-signature deltas.
//
// COMPARABILITY GATE (binding): two runs are comparable ONLY when all of
// the following match: same seed (1886), same scenario
// (millbrook-development-town), same startup mode (New Game), same target
// days, same speed (or the speed difference is recorded as a variable with
// the tick-granularity caveat, A F5), same source state or the difference is
// the explicit subject of the comparison, and same capture seams for the
// fields being compared. A run on a dirty tree is comparable only to
// another run on the SAME dirty tree — never to a clean-tree run.
//
// The retained 60-day run (20261120T192311Z-343a8cd2, on commit 8537f52f
// dirty tree) is BASELINE SHAPE ONLY, never a same-source comparator: any
// comparison against it is labelled "BASELINE SHAPE ONLY — different source
// state", never "before/after". The first valid before/after comparison is
// between the first run on the Muse branch and the next run on the same
// branch after a repair.
//
// A reduction in warning count alone is NOT a repair (W R-13): for each
// signature that disappeared, the comparator shows the repair that removed
// it (register entry + file:line) or marks it UNEXPLAINED.

using System;
using System.Collections.Generic;

namespace LandLedgers.Diagnostics
{
    /// <summary>What a run is: the identity the comparability gate checks.</summary>
    public sealed class RunIdentity
    {
        /// <summary>Run id (e.g. 20261120T192311Z-343a8cd2).</summary>
        public string RunId = string.Empty;

        /// <summary>Deterministic seed (1886 for the acceptance runs).</summary>
        public int Seed = -1;

        /// <summary>Scenario id (millbrook-development-town).</summary>
        public string Scenario = string.Empty;

        /// <summary>Startup mode (NewGame).</summary>
        public string StartupMode = string.Empty;

        /// <summary>Target days.</summary>
        public int TargetDays = -1;

        /// <summary>Days actually completed (-1 = NOT_CAPTURED).</summary>
        public int CompletedDays = -1;

        /// <summary>Speed preset as recorded (e.g. "Speed250x", "Speed10x").</summary>
        public string Speed = string.Empty;

        /// <summary>Branch the run executed on.</summary>
        public string Branch = string.Empty;

        /// <summary>Full commit SHA the run executed on.</summary>
        public string CommitSha = string.Empty;

        /// <summary>True when the run executed on a dirty working tree.</summary>
        public bool DirtyTree;

        /// <summary>Runner version, or empty when NOT_CAPTURED.</summary>
        public string RunnerVersion = string.Empty;

        /// <summary>Capture seams present in this run's artifacts (e.g. "mealPathPurchases").</summary>
        public List<string> CaptureSeams = new List<string>();

        /// <summary>
        /// The retained 60-day run's identity, from its metadata.json:
        /// commit 8537f52f, dirty tree (119 changed tracked files), seed
        /// 1886, 250x, 60 days. BASELINE SHAPE ONLY — never a same-source
        /// comparator.
        /// </summary>
        public static RunIdentity Retained60DayBaseline()
        {
            return new RunIdentity
            {
                RunId = "20261120T192311Z-343a8cd2",
                Seed = 1886,
                Scenario = "millbrook-development-town",
                StartupMode = "NewGame",
                TargetDays = 60,
                CompletedDays = 60,
                Speed = "Speed250x",
                Branch = "codex/player-economic-participation-2026-11-18",
                CommitSha = "8537f52fb7dea263fa3ba62da6cbd4d6b5aefbf8",
                DirtyTree = true,
            };
        }

        /// <summary>
        /// The retained 5-day run's identity: seed 1886, 10x, 5 days.
        /// Same source-state limits apply as for the 60-day run.
        /// </summary>
        public static RunIdentity Retained5DayRun()
        {
            return new RunIdentity
            {
                RunId = "20261120T205228Z-13c30187",
                Seed = 1886,
                Scenario = "millbrook-development-town",
                StartupMode = "NewGame",
                TargetDays = 5,
                Speed = "Speed10x",
                Branch = string.Empty,
                CommitSha = string.Empty,
                DirtyTree = true,
            };
        }
    }

    /// <summary>One run's structured outputs, as the comparator compares them.</summary>
    public sealed class RunSummary
    {
        /// <summary>Run id.</summary>
        public string RunId = string.Empty;

        /// <summary>Run identity (seed, scenario, source state, seams).</summary>
        public RunIdentity Identity = new RunIdentity();

        /// <summary>Days completed (rows in daily-summary.csv).</summary>
        public int DaysCompleted;

        /// <summary>Total warning-severity console events.</summary>
        public int WarningCount;

        /// <summary>Total error/exception/assert console events (must be 0 for acceptance).</summary>
        public int ErrorCount;

        /// <summary>Total console events counted.</summary>
        public int TotalConsoleEvents;

        /// <summary>Routine Log-severity events (counted, never hidden, never counted as diagnostics).</summary>
        public int RoutineLogCount;

        /// <summary>Distinct (signature, destination) pairs with counts.</summary>
        public List<RunSignatureCount> Signatures = new List<RunSignatureCount>();

        /// <summary>Total meals eaten across the run.</summary>
        public int MealsEaten;

        /// <summary>Total meals missed across the run.</summary>
        public int MealsMissed;

        /// <summary>Meals eaten per day (index 0 = day 1).</summary>
        public List<int> MealsEatenPerDay = new List<int>();

        /// <summary>Meals missed per day (index 0 = day 1).</summary>
        public List<int> MealsMissedPerDay = new List<int>();

        /// <summary>Household lot units per day.</summary>
        public List<int> HouseholdLotUnits = new List<int>();

        /// <summary>Retail lot units per day.</summary>
        public List<int> RetailLotUnits = new List<int>();

        /// <summary>Household cash in cents per day.</summary>
        public List<int> HouseholdCashCents = new List<int>();

        /// <summary>Business cash in cents per day.</summary>
        public List<int> BusinessCashCents = new List<int>();

        /// <summary>Business cash on day 1 (D-36 standing check: config 162,000c vs run day-1).</summary>
        public int Day1BusinessCashCents = -1;

        /// <summary>Active shopping trips per day.</summary>
        public List<int> ActiveShoppingTripCounts = new List<int>();

        /// <summary>Assigned tasks per day.</summary>
        public List<int> AssignedTaskCounts = new List<int>();

        /// <summary>Physical shopping purchases summed across days; meaningful only when MealPathPurchasesCaptured is true.</summary>
        public int MealPathPurchases;

        /// <summary>False when any day's mealPathPurchases was NOT_CAPTURED (-1): the field cannot be compared.</summary>
        public bool MealPathPurchasesCaptured = true;

        /// <summary>"[FoodEconomy] purchase settled" lines.</summary>
        public int SettleLineCount;

        /// <summary>"[FoodEconomy] purchase received" lines.</summary>
        public int ReceiveLineCount;

        /// <summary>Receipt ids appearing in both a settle line and a receive line.</summary>
        public int ReceiptIdsMatched;

        /// <summary>Receipt ids settled twice.</summary>
        public int DoubleSettlements;

        /// <summary>Trips ABORTED with "No physical route reaches seller location".</summary>
        public int TripsAbortedNoRoute;

        /// <summary>"Counter service expired" warnings.</summary>
        public int CounterServiceExpiries;

        /// <summary>"travel-task-succeeded-without-arrival" anomalies (must be 0).</summary>
        public int TravelWithoutArrivalCount;

        /// <summary>"task-no-progress" anomalies.</summary>
        public int TaskNoProgressCount;

        /// <summary>"task-disappeared-without-terminal" anomalies.</summary>
        public int TaskDisappearedCount;

        /// <summary>Trace events dropped by the recorder (a high count caps the confidence of every other check).</summary>
        public int DroppedTraceEvents;

        /// <summary>Missing daily snapshot offsets in this run (0 when none; -1 = NOT_CAPTURED).</summary>
        public int MissingDailySnapshotCount = -1;

        /// <summary>Unexpected domain reloads during the run (0 when none; -1 = NOT_CAPTURED).</summary>
        public int UnexpectedDomainReloadCount = -1;

        /// <summary>Actual residents at the startup gate (14 expected; -1 = NOT_CAPTURED).</summary>
        public int ActualResidents = -1;

        /// <summary>Actual households at the startup gate (8 expected; -1 = NOT_CAPTURED).</summary>
        public int ActualHouseholds = -1;

        /// <summary>Actual businesses at the startup gate (7 expected; -1 = NOT_CAPTURED).</summary>
        public int ActualBusinesses = -1;

        /// <summary>Achieved throughput in simulated days per wall-clock minute (-1 = NOT_CAPTURED).</summary>
        public double ThroughputDaysPerMinute = -1;

        /// <summary>Count for one (signature, destination) pair, 0 when absent.</summary>
        public int SignatureCount(string signature, string destination)
        {
            foreach (RunSignatureCount entry in Signatures)
            {
                if (entry.Signature == signature && entry.Destination == destination)
                {
                    return entry.Count;
                }
            }
            return 0;
        }
    }

    /// <summary>One signature's repair attribution: the repair that removed it, or nothing (UNEXPLAINED).</summary>
    public sealed class RepairAttribution
    {
        public RepairAttribution(string signature, string destination, string registerEntry, string fileLine)
        {
            Signature = signature ?? string.Empty;
            Destination = destination ?? string.Empty;
            RegisterEntry = registerEntry ?? string.Empty;
            FileLine = fileLine ?? string.Empty;
        }

        /// <summary>Normalised signature.</summary>
        public string Signature { get; }

        /// <summary>Destination (empty when the signature names none).</summary>
        public string Destination { get; }

        /// <summary>§22 register entry for the repair.</summary>
        public string RegisterEntry { get; }

        /// <summary>File:line of the repair.</summary>
        public string FileLine { get; }
    }

    /// <summary>How two runs relate under the comparability gate.</summary>
    public enum Comparability
    {
        /// <summary>Genuinely comparable: same seed, scenario, mode, days, speed (or a recorded speed variable), same source state, same seams.</summary>
        Comparable = 0,

        /// <summary>Comparable as a shape baseline only — source states differ. Never labelled "before/after".</summary>
        BaselineShapeOnly = 1,

        /// <summary>Refused: the gate's hard conditions do not match. The result names why.</summary>
        Refused = 2,
    }

    /// <summary>One per-signature delta between two runs.</summary>
    public sealed class SignatureDelta
    {
        public SignatureDelta(string signature, string destination, int baselineCount, int candidateCount)
        {
            Signature = signature ?? string.Empty;
            Destination = destination ?? string.Empty;
            BaselineCount = baselineCount;
            CandidateCount = candidateCount;
        }

        /// <summary>Normalised signature.</summary>
        public string Signature { get; }

        /// <summary>Destination (empty when the signature names none).</summary>
        public string Destination { get; }

        /// <summary>Occurrences in the baseline run.</summary>
        public int BaselineCount { get; }

        /// <summary>Occurrences in the candidate run.</summary>
        public int CandidateCount { get; }

        /// <summary>Candidate minus baseline.</summary>
        public int Delta
        {
            get { return CandidateCount - BaselineCount; }
        }

        /// <summary>True when the signature vanished in the candidate.</summary>
        public bool Disappeared
        {
            get { return BaselineCount > 0 && CandidateCount == 0; }
        }

        /// <summary>True when the signature is new in the candidate (a NEW REGRESSION candidate until investigated).</summary>
        public bool IsNew
        {
            get { return BaselineCount == 0 && CandidateCount > 0; }
        }

        /// <summary>Repair attribution for a disappeared signature; null when none was supplied (UNEXPLAINED).</summary>
        public RepairAttribution Attribution { get; set; }

        /// <summary>True when the signature disappeared with no repair attribution: a reduction in warning count alone is NOT a repair.</summary>
        public bool DisappearedUnexplained
        {
            get { return Disappeared && Attribution == null; }
        }
    }

    /// <summary>The comparison of two runs: gate verdict, per-signature deltas, and outcome deltas.</summary>
    public sealed class RunComparisonResult
    {
        /// <summary>Gate verdict.</summary>
        public Comparability Comparability;

        /// <summary>Gate reasons and caveats (why refused, why baseline-shape-only, which seams differ).</summary>
        public List<string> GateNotes = new List<string>();

        /// <summary>Per-(signature, destination) deltas, baseline order first, then candidate-only signatures.</summary>
        public List<SignatureDelta> SignatureDeltas = new List<SignatureDelta>();

        /// <summary>Meals eaten: candidate minus baseline.</summary>
        public int MealsEatenDelta;

        /// <summary>Meals missed: candidate minus baseline.</summary>
        public int MealsMissedDelta;

        /// <summary>Warnings: candidate minus baseline.</summary>
        public int WarningDelta;

        /// <summary>Errors: candidate minus baseline.</summary>
        public int ErrorDelta;

        /// <summary>Settle lines: candidate minus baseline.</summary>
        public int SettleLineDelta;

        /// <summary>Receive lines: candidate minus baseline.</summary>
        public int ReceiveLineDelta;

        /// <summary>Trips aborted for no route: candidate minus baseline.</summary>
        public int TripsAbortedDelta;

        /// <summary>True when the comparison may be read as a before/after (gate Comparable).</summary>
        public bool IsBeforeAfter
        {
            get { return Comparability == Comparability.Comparable; }
        }

        /// <summary>Disappeared signatures with no repair attribution.</summary>
        public int UnexplainedDisappearances
        {
            get
            {
                int count = 0;
                foreach (SignatureDelta delta in SignatureDeltas)
                {
                    if (delta.DisappearedUnexplained)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>New signatures in the candidate (NEW REGRESSION candidates until investigated).</summary>
        public int NewSignatures
        {
            get
            {
                int count = 0;
                foreach (SignatureDelta delta in SignatureDeltas)
                {
                    if (delta.IsNew)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>Human-readable comparison report.</summary>
        public string ToMarkdown()
        {
            var lines = new List<string>();
            lines.Add("# Regression comparison");
            lines.Add(string.Empty);
            switch (Comparability)
            {
                case Comparability.Comparable:
                    lines.Add("Comparability: COMPARABLE (before/after).");
                    break;
                case Comparability.BaselineShapeOnly:
                    lines.Add("Comparability: BASELINE SHAPE ONLY — different source state. Never read as before/after.");
                    break;
                default:
                    lines.Add("Comparability: REFUSED — these runs are not genuinely comparable.");
                    break;
            }
            foreach (string note in GateNotes)
            {
                lines.Add("- " + note);
            }
            lines.Add(string.Empty);
            if (Comparability == Comparability.Refused)
            {
                lines.Add("No deltas are reported for a refused comparison.");
                return string.Join("\n", lines.ToArray());
            }
            lines.Add("Outcomes: meals eaten " + MealsEatenDelta + "; meals missed " + MealsMissedDelta
                + "; warnings " + WarningDelta + "; errors " + ErrorDelta
                + "; settle lines " + SettleLineDelta + "; receive lines " + ReceiveLineDelta
                + "; trips aborted for no route " + TripsAbortedDelta + ".");
            lines.Add("Disappeared signatures without repair attribution (UNEXPLAINED): " + UnexplainedDisappearances
                + ". New signatures (NEW REGRESSION candidates): " + NewSignatures + ".");
            lines.Add(string.Empty);
            lines.Add("## Per-signature deltas (signature, destination)");
            foreach (SignatureDelta delta in SignatureDeltas)
            {
                if (delta.Delta == 0)
                {
                    continue;
                }
                string line = "- [" + delta.Signature + "] @ '" + delta.Destination + "': "
                    + delta.BaselineCount + " -> " + delta.CandidateCount + " (" + (delta.Delta > 0 ? "+" : "") + delta.Delta + ")";
                if (delta.DisappearedUnexplained)
                {
                    line += " — UNEXPLAINED: a reduction in warning count alone is NOT a repair.";
                }
                else if (delta.Disappeared && delta.Attribution != null)
                {
                    line += " — removed by " + delta.Attribution.RegisterEntry + " (" + delta.Attribution.FileLine + ").";
                }
                else if (delta.IsNew)
                {
                    line += " — NEW REGRESSION candidate until investigated.";
                }
                lines.Add(line);
            }
            return string.Join("\n", lines.ToArray());
        }
    }

    /// <summary>
    /// Lane 8 (Tool 11, Agent N §3.8) — the Regression Comparator. Pure
    /// functions over structured run summaries: no file I/O, no live game.
    /// The gate refuses non-comparable pairs and prominently flags
    /// baseline-shape-only comparisons; it never invents comparability.
    /// </summary>
    public static class RegressionComparator
    {
        /// <summary>
        /// Runs the comparability gate and returns its verdict plus the
        /// reasons. Hard refusals: different seed, scenario, startup mode,
        /// target days, unrecorded speed difference, or a dirty-tree run
        /// compared against a clean-tree run. Source-state differences that
        /// are NOT the explicit subject of the comparison yield
        /// BaselineShapeOnly, never a refusal and never a before/after.
        /// </summary>
        public static Comparability CheckComparability(
            RunIdentity baseline, RunIdentity candidate,
            bool speedDifferenceRecordedAsVariable, bool sourceChangeIsSubject,
            out List<string> notes)
        {
            notes = new List<string>();
            if (baseline == null || candidate == null)
            {
                notes.Add("Refused: one or both run identities are missing.");
                return Comparability.Refused;
            }

            if (baseline.Seed >= 0 && candidate.Seed >= 0 && baseline.Seed != candidate.Seed)
            {
                notes.Add("Refused: seeds differ (baseline " + baseline.Seed + ", candidate " + candidate.Seed + ").");
                return Comparability.Refused;
            }
            if (baseline.Scenario.Length > 0 && candidate.Scenario.Length > 0
                && baseline.Scenario != candidate.Scenario)
            {
                notes.Add("Refused: scenarios differ (baseline '" + baseline.Scenario + "', candidate '" + candidate.Scenario + "').");
                return Comparability.Refused;
            }
            if (baseline.StartupMode.Length > 0 && candidate.StartupMode.Length > 0
                && baseline.StartupMode != candidate.StartupMode)
            {
                notes.Add("Refused: startup modes differ (baseline '" + baseline.StartupMode + "', candidate '" + candidate.StartupMode + "').");
                return Comparability.Refused;
            }
            if (baseline.TargetDays >= 0 && candidate.TargetDays >= 0 && baseline.TargetDays != candidate.TargetDays)
            {
                notes.Add("Refused: target days differ (baseline " + baseline.TargetDays + ", candidate " + candidate.TargetDays + ").");
                return Comparability.Refused;
            }
            if (baseline.Speed.Length > 0 && candidate.Speed.Length > 0 && baseline.Speed != candidate.Speed)
            {
                if (!speedDifferenceRecordedAsVariable)
                {
                    notes.Add("Refused: speeds differ (baseline '" + baseline.Speed + "', candidate '" + candidate.Speed + "') and the difference is not recorded as a variable.");
                    return Comparability.Refused;
                }
                notes.Add("Caveat: speeds differ (baseline '" + baseline.Speed + "', candidate '" + candidate.Speed + "') — recorded as a variable with the tick-granularity caveat (A F5) attached. Meal timing is not comparable across speeds.");
            }
            if (baseline.DirtyTree != candidate.DirtyTree)
            {
                notes.Add("Refused: one run is on a dirty tree and the other on a clean tree. A run on a dirty tree is comparable only to another run on the same dirty tree.");
                return Comparability.Refused;
            }

            bool sameSource = baseline.CommitSha.Length > 0 && candidate.CommitSha.Length > 0
                && baseline.CommitSha == candidate.CommitSha
                && baseline.Branch == candidate.Branch;
            if (!sameSource)
            {
                if (sourceChangeIsSubject)
                {
                    notes.Add("Source states differ (baseline " + ShortSha(baseline.CommitSha) + " on '" + baseline.Branch
                        + "', candidate " + ShortSha(candidate.CommitSha) + " on '" + candidate.Branch
                        + "') — the difference is the explicit subject of this comparison, so the comparison is Comparable with that caveat.");
                    return Comparability.Comparable;
                }
                notes.Add("BASELINE SHAPE ONLY — different source state: baseline " + ShortSha(baseline.CommitSha)
                    + " on '" + baseline.Branch + "' (dirty: " + baseline.DirtyTree + "), candidate "
                    + ShortSha(candidate.CommitSha) + " on '" + candidate.Branch + "' (dirty: " + candidate.DirtyTree
                    + "). Legitimate uses: the pre-fix collapse shape, the warning-signature inventory, and the proof that the survival loop never completed. Never 'before/after'.");
                return Comparability.BaselineShapeOnly;
            }

            notes.Add("Comparable: same seed, scenario, startup mode, target days, speed and source state.");
            return Comparability.Comparable;
        }

        /// <summary>
        /// Compares two runs. A refused gate yields a result with no deltas.
        /// Disappeared signatures are matched against the supplied repair
        /// attributions; a disappeared signature with no attribution is
        /// UNEXPLAINED, never silently a repair.
        /// </summary>
        public static RunComparisonResult Compare(
            RunSummary baseline, RunSummary candidate,
            bool speedDifferenceRecordedAsVariable, bool sourceChangeIsSubject,
            IReadOnlyList<RepairAttribution> repairAttributions)
        {
            if (baseline == null)
            {
                throw new ArgumentNullException("baseline");
            }
            if (candidate == null)
            {
                throw new ArgumentNullException("candidate");
            }

            List<string> notes;
            Comparability comparability = CheckComparability(
                baseline.Identity, candidate.Identity,
                speedDifferenceRecordedAsVariable, sourceChangeIsSubject, out notes);

            var result = new RunComparisonResult
            {
                Comparability = comparability,
                GateNotes = notes,
            };

            if (comparability == Comparability.Refused)
            {
                return result;
            }

            result.MealsEatenDelta = candidate.MealsEaten - baseline.MealsEaten;
            result.MealsMissedDelta = candidate.MealsMissed - baseline.MealsMissed;
            result.WarningDelta = candidate.WarningCount - baseline.WarningCount;
            result.ErrorDelta = candidate.ErrorCount - baseline.ErrorCount;
            result.SettleLineDelta = candidate.SettleLineCount - baseline.SettleLineCount;
            result.ReceiveLineDelta = candidate.ReceiveLineCount - baseline.ReceiveLineCount;
            result.TripsAbortedDelta = candidate.TripsAbortedNoRoute - baseline.TripsAbortedNoRoute;

            var seen = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (RunSignatureCount entry in baseline.Signatures)
            {
                int candidateCount = candidate.SignatureCount(entry.Signature, entry.Destination);
                var delta = new SignatureDelta(entry.Signature, entry.Destination, entry.Count, candidateCount);
                delta.Attribution = FindAttribution(repairAttributions, entry.Signature, entry.Destination);
                result.SignatureDeltas.Add(delta);
                seen[entry.Key] = true;
            }
            foreach (RunSignatureCount entry in candidate.Signatures)
            {
                if (seen.ContainsKey(entry.Key))
                {
                    continue;
                }
                result.SignatureDeltas.Add(new SignatureDelta(entry.Signature, entry.Destination, 0, entry.Count));
            }

            if (!baseline.MealPathPurchasesCaptured || !candidate.MealPathPurchasesCaptured)
            {
                result.GateNotes.Add("mealPathPurchases is NOT_CAPTURED in one or both runs (the runner hardcodes -1); it cannot be compared on that field.");
            }

            return result;
        }

        private static RepairAttribution FindAttribution(IReadOnlyList<RepairAttribution> attributions, string signature, string destination)
        {
            if (attributions == null)
            {
                return null;
            }
            foreach (RepairAttribution attribution in attributions)
            {
                if (attribution.Signature == signature && attribution.Destination == destination)
                {
                    return attribution;
                }
            }
            return null;
        }

        private static string ShortSha(string sha)
        {
            if (string.IsNullOrEmpty(sha))
            {
                return "unknown";
            }
            return sha.Length > 8 ? sha.Substring(0, 8) : sha;
        }
    }
}
