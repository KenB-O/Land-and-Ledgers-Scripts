// Land & Ledgers — run artifact reader (Lane 8, Tool 10/11).
//
// Reads a runner run directory's own artifacts — daily-summary.csv,
// console-events.jsonl, metadata.json — into the structured summaries the
// Scenario Lab and the Regression Comparator evaluate. The reader never
// invents a value: a missing file, a missing column, or a missing field
// stays NOT_CAPTURED (-1 / empty / false), never zero and never a guess.
// Warnings are grouped by (signature, destination), never by signature
// alone (Agent N §1.2): the 2,878 StartNotWalkable lines are one signature
// across at least three destinations, and the report must say so.
//
// The reader needs no live Unity session: it is analysis code over
// artifacts, so the six-section acceptance report can already run against
// the retained runs and fail loudly and specifically.

using System;
using System.Collections.Generic;

namespace LandLedgers.Diagnostics
{
    /// <summary>One day's row from daily-summary.csv.</summary>
    public sealed class DailySummaryRow
    {
        /// <summary>Absolute day index (1-based in the file).</summary>
        public int DayIndex;

        /// <summary>Meals eaten that day.</summary>
        public int MealsEaten;

        /// <summary>Meals missed that day.</summary>
        public int MealsMissed;

        /// <summary>Household lot units at the day boundary.</summary>
        public int HouseholdLotUnits;

        /// <summary>Retail lot units at the day boundary.</summary>
        public int RetailLotUnits;

        /// <summary>Household cash in cents at the day boundary.</summary>
        public int HouseholdCashCents;

        /// <summary>Business cash in cents at the day boundary.</summary>
        public int BusinessCashCents;

        /// <summary>Physical shopping purchases that day (-1 = NOT_CAPTURED: the runner hardcodes -1).</summary>
        public int MealPathPurchases;

        /// <summary>Physical shopping spend in cents that day (-1 = NOT_CAPTURED).</summary>
        public int MealPathSpendCents;

        /// <summary>Active shopping trips at the day boundary.</summary>
        public int ActiveShoppingTripCount;

        /// <summary>Tasks in Assigned state at the day boundary.</summary>
        public int AssignedTasks;
    }

    /// <summary>One warning/error signature, counted per destination.</summary>
    public sealed class RunSignatureCount
    {
        public RunSignatureCount(string signature, string destination, int count)
        {
            Signature = signature ?? string.Empty;
            Destination = destination ?? string.Empty;
            Count = count;
        }

        /// <summary>Normalised message (digit runs replaced with N).</summary>
        public string Signature { get; }

        /// <summary>Destination named in the message (e.g. "building:N"), empty when the message names none.</summary>
        public string Destination { get; }

        /// <summary>Occurrences of this (signature, destination) pair.</summary>
        public int Count { get; set; }

        /// <summary>Stable key for the pair.</summary>
        public string Key
        {
            get { return Signature + " || " + Destination; }
        }
    }

    /// <summary>What console-events.jsonl says about a run, counted — never interpreted beyond the counts.</summary>
    public sealed class ConsoleEventSummary
    {
        /// <summary>Total console events counted.</summary>
        public int TotalEvents;

        /// <summary>Warning-severity events.</summary>
        public int WarningCount;

        /// <summary>Error/exception/assert events (must be 0 for acceptance).</summary>
        public int ErrorCount;

        /// <summary>Log-severity events (routine volume, counted, never hidden, never counted as diagnostics).</summary>
        public int LogCount;

        /// <summary>Distinct (signature, destination) pairs, in first-seen order.</summary>
        public List<RunSignatureCount> Signatures = new List<RunSignatureCount>();

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

        /// <summary>StartNotWalkable warnings (one signature; destinations in Signatures).</summary>
        public int StartNotWalkableCount;

        /// <summary>"travel-task-succeeded-without-arrival" anomalies (must be 0).</summary>
        public int TravelWithoutArrivalCount;

        /// <summary>"task-no-progress" anomalies.</summary>
        public int TaskNoProgressCount;

        /// <summary>"task-disappeared-without-terminal" anomalies.</summary>
        public int TaskDisappearedCount;

        /// <summary>Count for one (signature, destination) pair, 0 when absent.</summary>
        public int CountFor(string signature, string destination)
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

    /// <summary>
    /// Lane 8 — reads a run directory's artifacts into structured summaries.
    /// Pure functions over supplied file text: tests feed fixtures, the
    /// Editor window reads the run directory from disk. No Unity dependency.
    /// </summary>
    public static class RunArtifactReader
    {
        /// <summary>NOT_CAPTURED sentinel for integer measures.</summary>
        public const int NotCaptured = -1;

        /// <summary>Parses daily-summary.csv into day rows. A missing column leaves that measure at NOT_CAPTURED (-1).</summary>
        public static List<DailySummaryRow> ParseDailySummaryCsv(string csvText)
        {
            var rows = new List<DailySummaryRow>();
            if (string.IsNullOrEmpty(csvText))
            {
                return rows;
            }

            string[] lines = SplitLines(csvText);
            if (lines.Length < 2)
            {
                return rows;
            }

            List<string> header = ParseCsvLine(lines[0]);
            int dayIndexCol = header.IndexOf("absoluteDayIndex");
            int mealsEatenCol = header.IndexOf("mealsEaten");
            int mealsMissedCol = header.IndexOf("mealsMissed");
            int householdLotsCol = header.IndexOf("householdLotUnits");
            int retailLotsCol = header.IndexOf("retailLotUnits");
            int householdCashCol = header.IndexOf("householdCashCents");
            int businessCashCol = header.IndexOf("businessCashCents");
            int mealPathPurchasesCol = header.IndexOf("mealPathPurchases");
            int mealPathSpendCol = header.IndexOf("mealPathSpendCents");
            int activeTripsCol = header.IndexOf("activeShoppingTripCount");
            int assignedTasksCol = header.IndexOf("assignedTasks");

            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length == 0)
                {
                    continue;
                }

                List<string> fields = ParseCsvLine(lines[i]);
                var row = new DailySummaryRow
                {
                    DayIndex = IntField(fields, dayIndexCol),
                    MealsEaten = IntField(fields, mealsEatenCol),
                    MealsMissed = IntField(fields, mealsMissedCol),
                    HouseholdLotUnits = IntField(fields, householdLotsCol),
                    RetailLotUnits = IntField(fields, retailLotsCol),
                    HouseholdCashCents = IntField(fields, householdCashCol),
                    BusinessCashCents = IntField(fields, businessCashCol),
                    MealPathPurchases = IntField(fields, mealPathPurchasesCol),
                    MealPathSpendCents = IntField(fields, mealPathSpendCol),
                    ActiveShoppingTripCount = IntField(fields, activeTripsCol),
                    AssignedTasks = IntField(fields, assignedTasksCol),
                };
                rows.Add(row);
            }

            return rows;
        }

        /// <summary>Parses console-events.jsonl into counted summaries. Lines that are not JSON objects with a message are skipped, never guessed.</summary>
        public static ConsoleEventSummary ParseConsoleEventsJsonl(string jsonlText)
        {
            var summary = new ConsoleEventSummary();
            if (string.IsNullOrEmpty(jsonlText))
            {
                return summary;
            }

            var settleReceipts = new Dictionary<string, int>(StringComparer.Ordinal);
            var receiveReceipts = new Dictionary<string, int>(StringComparer.Ordinal);

            string[] lines = SplitLines(jsonlText);
            foreach (string line in lines)
            {
                if (line.Trim().Length == 0)
                {
                    continue;
                }

                string severity = JsonStringField(line, "severity");
                string message = JsonStringField(line, "message");
                if (message == null)
                {
                    continue;
                }

                summary.TotalEvents++;
                if (severity == "Warning")
                {
                    summary.WarningCount++;
                }
                else if (severity == "Error" || severity == "Exception" || severity == "Assert")
                {
                    summary.ErrorCount++;
                }
                else
                {
                    summary.LogCount++;
                }

                string signature = NormalizeSignature(message);
                string destination = ExtractDestination(message);
                AddSignature(summary, signature, destination);

                if (message.IndexOf("[FoodEconomy] purchase settled", StringComparison.Ordinal) >= 0)
                {
                    summary.SettleLineCount++;
                    string receipt = ExtractReceipt(message);
                    if (receipt.Length > 0)
                    {
                        int count;
                        settleReceipts.TryGetValue(receipt, out count);
                        settleReceipts[receipt] = count + 1;
                    }
                }
                if (message.IndexOf("[FoodEconomy] purchase received", StringComparison.Ordinal) >= 0)
                {
                    summary.ReceiveLineCount++;
                    string receipt = ExtractReceipt(message);
                    if (receipt.Length > 0)
                    {
                        int count;
                        receiveReceipts.TryGetValue(receipt, out count);
                        receiveReceipts[receipt] = count + 1;
                    }
                }
                if (message.IndexOf("ABORTED", StringComparison.Ordinal) >= 0
                    && message.IndexOf("No physical route reaches seller location", StringComparison.Ordinal) >= 0)
                {
                    summary.TripsAbortedNoRoute++;
                }
                if (message.IndexOf("counter service expired", StringComparison.Ordinal) >= 0)
                {
                    summary.CounterServiceExpiries++;
                }
                if (message.IndexOf("StartNotWalkable", StringComparison.Ordinal) >= 0)
                {
                    summary.StartNotWalkableCount++;
                }
                if (message.IndexOf("travel-task-succeeded-without-arrival", StringComparison.Ordinal) >= 0)
                {
                    summary.TravelWithoutArrivalCount++;
                }
                if (message.IndexOf("task-no-progress", StringComparison.Ordinal) >= 0)
                {
                    summary.TaskNoProgressCount++;
                }
                if (message.IndexOf("task-disappeared-without-terminal", StringComparison.Ordinal) >= 0)
                {
                    summary.TaskDisappearedCount++;
                }
            }

            foreach (KeyValuePair<string, int> pair in settleReceipts)
            {
                if (pair.Value > 1)
                {
                    summary.DoubleSettlements++;
                }
                if (receiveReceipts.ContainsKey(pair.Key))
                {
                    summary.ReceiptIdsMatched++;
                }
            }

            return summary;
        }

        /// <summary>Reads a run identity out of metadata.json text. Missing fields stay NOT_CAPTURED / empty, never guessed.</summary>
        public static RunIdentity ParseMetadataJson(string metadataText)
        {
            var identity = new RunIdentity();
            if (string.IsNullOrEmpty(metadataText))
            {
                return identity;
            }

            identity.RunId = JsonStringField(metadataText, "runId") ?? string.Empty;
            identity.Branch = JsonStringField(metadataText, "branch") ?? string.Empty;
            identity.CommitSha = JsonStringField(metadataText, "commit") ?? string.Empty;
            identity.Scenario = JsonStringField(metadataText, "scenario") ?? string.Empty;
            identity.StartupMode = JsonStringField(metadataText, "startupMode") ?? string.Empty;
            identity.Speed = JsonStringField(metadataText, "speedName") ?? string.Empty;
            identity.Seed = JsonIntField(metadataText, "seed");
            identity.TargetDays = JsonIntField(metadataText, "targetDays");
            identity.CompletedDays = JsonIntField(metadataText, "completedDays");
            identity.DirtyTree = JsonBoolField(metadataText, "trackedWorkingTreeDirty");

            return identity;
        }

        /// <summary>
        /// Applies metadata.json values that live on the summary (not the
        /// identity): startup-gate counts, missing snapshots, unexpected
        /// domain reloads. Missing fields stay NOT_CAPTURED (-1).
        /// </summary>
        public static void ApplyMetadataToSummary(RunSummary summary, string metadataText)
        {
            if (summary == null || string.IsNullOrEmpty(metadataText))
            {
                return;
            }

            summary.ActualResidents = JsonIntField(metadataText, "actualResidents");
            summary.ActualHouseholds = JsonIntField(metadataText, "actualHouseholds");
            summary.ActualBusinesses = JsonIntField(metadataText, "actualBusinesses");
            summary.UnexpectedDomainReloadCount = JsonIntField(metadataText, "unexpectedDomainReloadCount");
            summary.MissingDailySnapshotCount = CountJsonArrayEntries(metadataText, "missingDailySnapshotOffsets");
        }

        /// <summary>Counts entries in a JSON array field ("[]" = 0); NOT_CAPTURED (-1) when the key is absent.</summary>
        public static int CountJsonArrayEntries(string jsonText, string key)
        {
            if (string.IsNullOrEmpty(jsonText) || string.IsNullOrEmpty(key))
            {
                return NotCaptured;
            }

            string marker = "\"" + key + "\"";
            int keyAt = jsonText.IndexOf(marker, StringComparison.Ordinal);
            if (keyAt < 0)
            {
                return NotCaptured;
            }

            int open = jsonText.IndexOf('[', keyAt + marker.Length);
            if (open < 0)
            {
                return NotCaptured;
            }
            int close = jsonText.IndexOf(']', open + 1);
            if (close < 0)
            {
                return NotCaptured;
            }

            string inside = jsonText.Substring(open + 1, close - open - 1).Trim();
            if (inside.Length == 0)
            {
                return 0;
            }

            int count = 1;
            foreach (char c in inside)
            {
                if (c == ',')
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Builds survival-test evidence from a run's artifacts. Only what the
        /// artifacts measure is filled: meals, lots, settle/receive lines,
        /// receipt matching, aborts, expiries. Shopper choice, trip
        /// departure, TendCustomer service, lot deposits and per-household
        /// streaks need seams the runner does not expose yet, so they stay
        /// NOT_CAPTURED here — the evaluator then reports those stages
        /// BLOCKED with the seam named, never passed on a guess.
        /// </summary>
        public static SurvivalRunEvidence BuildSurvivalEvidence(
            string runId, RunIdentity identity,
            List<DailySummaryRow> dailyRows, ConsoleEventSummary console)
        {
            var evidence = new SurvivalRunEvidence();
            evidence.HasLiveRun = dailyRows != null && dailyRows.Count > 0;
            evidence.RunId = runId ?? string.Empty;
            if (identity != null)
            {
                evidence.Branch = identity.Branch;
                evidence.CommitSha = identity.CommitSha;
                evidence.Seed = identity.Seed;
                evidence.Speed = identity.Speed;
                evidence.TargetDays = identity.TargetDays;
                evidence.DaysCompleted = identity.CompletedDays;
                evidence.Scenario = identity.Scenario;
                evidence.StartupMode = identity.StartupMode;
            }

            if (dailyRows != null)
            {
                foreach (DailySummaryRow row in dailyRows)
                {
                    evidence.MealsEatenPerDay.Add(row.MealsEaten);
                    evidence.MealsMissedPerDay.Add(row.MealsMissed);
                    evidence.HouseholdLotsPerDay.Add(row.HouseholdLotUnits);
                }
            }

            if (console != null)
            {
                evidence.SettleLineCount = console.SettleLineCount;
                evidence.ReceiveLineCount = console.ReceiveLineCount;
                evidence.ReceiptIdsMatched = console.ReceiptIdsMatched;
                evidence.DoubleSettlements = console.DoubleSettlements;
                evidence.TripsAbortedNoRoute = console.TripsAbortedNoRoute;
                evidence.CounterServiceExpiries = console.CounterServiceExpiries;
            }

            return evidence;
        }

        /// <summary>Builds a comparator run summary from a run's artifacts.</summary>
        public static RunSummary BuildRunSummary(
            string runId, RunIdentity identity,
            List<DailySummaryRow> dailyRows, ConsoleEventSummary console,
            int droppedTraceEvents, double throughputDaysPerMinute)
        {
            var summary = new RunSummary();
            summary.RunId = runId ?? string.Empty;
            summary.Identity = identity ?? new RunIdentity();
            summary.DroppedTraceEvents = droppedTraceEvents;
            summary.ThroughputDaysPerMinute = throughputDaysPerMinute;

            if (dailyRows != null)
            {
                foreach (DailySummaryRow row in dailyRows)
                {
                    summary.MealsEaten += row.MealsEaten;
                    summary.MealsMissed += row.MealsMissed;
                    summary.MealsEatenPerDay.Add(row.MealsEaten);
                    summary.MealsMissedPerDay.Add(row.MealsMissed);
                    summary.HouseholdLotUnits.Add(row.HouseholdLotUnits);
                    summary.RetailLotUnits.Add(row.RetailLotUnits);
                    summary.HouseholdCashCents.Add(row.HouseholdCashCents);
                    summary.BusinessCashCents.Add(row.BusinessCashCents);
                    summary.ActiveShoppingTripCounts.Add(row.ActiveShoppingTripCount);
                    summary.AssignedTaskCounts.Add(row.AssignedTasks);
                    if (row.MealPathPurchases >= 0)
                    {
                        summary.MealPathPurchases += row.MealPathPurchases;
                    }
                    else
                    {
                        summary.MealPathPurchasesCaptured = false;
                    }
                }
                if (dailyRows.Count > 0)
                {
                    summary.DaysCompleted = dailyRows.Count;
                    summary.Day1BusinessCashCents = dailyRows[0].BusinessCashCents;
                }
            }

            if (console != null)
            {
                summary.WarningCount = console.WarningCount;
                summary.ErrorCount = console.ErrorCount;
                summary.RoutineLogCount = console.LogCount;
                summary.TotalConsoleEvents = console.TotalEvents;
                summary.Signatures = console.Signatures;
                summary.SettleLineCount = console.SettleLineCount;
                summary.ReceiveLineCount = console.ReceiveLineCount;
                summary.ReceiptIdsMatched = console.ReceiptIdsMatched;
                summary.DoubleSettlements = console.DoubleSettlements;
                summary.TripsAbortedNoRoute = console.TripsAbortedNoRoute;
                summary.CounterServiceExpiries = console.CounterServiceExpiries;
                summary.TravelWithoutArrivalCount = console.TravelWithoutArrivalCount;
                summary.TaskNoProgressCount = console.TaskNoProgressCount;
                summary.TaskDisappearedCount = console.TaskDisappearedCount;
            }

            return summary;
        }

        // ---- Parsing helpers (pure, tested) ----

        /// <summary>Splits text into lines on \r\n or \n.</summary>
        public static string[] SplitLines(string text)
        {
            return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        }

        /// <summary>Parses one CSV line honouring double-quoted fields ("" = one quote).</summary>
        public static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            if (line == null)
            {
                return fields;
            }

            var current = new System.Text.StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Length = 0;
                }
                else
                {
                    current.Append(c);
                }
            }
            fields.Add(current.ToString());
            return fields;
        }

        private static int IntField(List<string> fields, int column)
        {
            if (column < 0 || column >= fields.Count)
            {
                return NotCaptured;
            }
            int value;
            return int.TryParse(fields[column].Trim(), out value) ? value : NotCaptured;
        }

        private static void AddSignature(ConsoleEventSummary summary, string signature, string destination)
        {
            foreach (RunSignatureCount entry in summary.Signatures)
            {
                if (entry.Signature == signature && entry.Destination == destination)
                {
                    entry.Count++;
                    return;
                }
            }
            summary.Signatures.Add(new RunSignatureCount(signature, destination, 1));
        }

        /// <summary>Normalises a console message into a signature: digit runs become N, whitespace collapses.</summary>
        public static string NormalizeSignature(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            var result = new System.Text.StringBuilder(message.Length);
            bool inDigits = false;
            bool lastWasSpace = false;
            foreach (char c in message)
            {
                if (c >= '0' && c <= '9')
                {
                    if (!inDigits)
                    {
                        result.Append('N');
                        inDigits = true;
                    }
                    lastWasSpace = false;
                }
                else
                {
                    inDigits = false;
                    if (c == ' ' || c == '\t')
                    {
                        if (!lastWasSpace)
                        {
                            result.Append(' ');
                        }
                        lastWasSpace = true;
                    }
                    else
                    {
                        result.Append(c);
                        lastWasSpace = false;
                    }
                }
            }
            return result.ToString().Trim();
        }

        /// <summary>
        /// Extracts the destination a message names: a seller location
        /// ('building:N'), a dining chair (chair:...), or empty when the
        /// message names none. The 2,878 StartNotWalkable lines name no
        /// destination in the message — they stay destination-less, and the
        /// report must say so rather than attributing them to one place.
        /// </summary>
        public static string ExtractDestination(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            int building = message.IndexOf("seller location 'building:", StringComparison.Ordinal);
            if (building >= 0)
            {
                int start = building + "seller location '".Length;
                int end = message.IndexOf('\'', start);
                if (end > start)
                {
                    return NormalizeSignature(message.Substring(start, end - start));
                }
            }

            int chair = message.IndexOf("chair:", StringComparison.Ordinal);
            if (chair >= 0)
            {
                int start = chair;
                int end = start;
                while (end < message.Length && message[end] != ' ' && message[end] != '\'' && message[end] != '"' && message[end] != ',')
                {
                    end++;
                }
                if (end > start)
                {
                    return NormalizeSignature(message.Substring(start, end - start));
                }
            }

            return string.Empty;
        }

        /// <summary>Extracts the receipt id from a settle/receive line ("receipt='R'"), empty when absent.</summary>
        public static string ExtractReceipt(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            const string marker = "receipt='";
            int start = message.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return string.Empty;
            }
            start += marker.Length;
            int end = message.IndexOf('\'', start);
            if (end <= start)
            {
                return string.Empty;
            }
            return message.Substring(start, end - start);
        }

        /// <summary>Extracts a JSON string field value from one JSON object line (handles \" and \\ escapes); null when the key is absent.</summary>
        public static string JsonStringField(string jsonLine, string key)
        {
            if (string.IsNullOrEmpty(jsonLine) || string.IsNullOrEmpty(key))
            {
                return null;
            }

            string marker = "\"" + key + "\"";
            int keyAt = jsonLine.IndexOf(marker, StringComparison.Ordinal);
            if (keyAt < 0)
            {
                return null;
            }

            int colon = jsonLine.IndexOf(':', keyAt + marker.Length);
            if (colon < 0)
            {
                return null;
            }

            int i = colon + 1;
            while (i < jsonLine.Length && (jsonLine[i] == ' ' || jsonLine[i] == '\t'))
            {
                i++;
            }
            if (i >= jsonLine.Length || jsonLine[i] != '"')
            {
                return null;
            }

            var value = new System.Text.StringBuilder();
            i++;
            while (i < jsonLine.Length)
            {
                char c = jsonLine[i];
                if (c == '\\' && i + 1 < jsonLine.Length)
                {
                    char next = jsonLine[i + 1];
                    switch (next)
                    {
                        case '"': value.Append('"'); break;
                        case '\\': value.Append('\\'); break;
                        case 'n': value.Append('\n'); break;
                        case 'r': value.Append('\r'); break;
                        case 't': value.Append('\t'); break;
                        default: value.Append(next); break;
                    }
                    i += 2;
                }
                else if (c == '"')
                {
                    return value.ToString();
                }
                else
                {
                    value.Append(c);
                    i++;
                }
            }
            return null;
        }

        /// <summary>Extracts a JSON integer field value; NOT_CAPTURED (-1) when absent or unparsable.</summary>
        public static int JsonIntField(string jsonLine, string key)
        {
            if (string.IsNullOrEmpty(jsonLine) || string.IsNullOrEmpty(key))
            {
                return NotCaptured;
            }

            string marker = "\"" + key + "\"";
            int keyAt = jsonLine.IndexOf(marker, StringComparison.Ordinal);
            if (keyAt < 0)
            {
                return NotCaptured;
            }

            int colon = jsonLine.IndexOf(':', keyAt + marker.Length);
            if (colon < 0)
            {
                return NotCaptured;
            }

            int i = colon + 1;
            while (i < jsonLine.Length && (jsonLine[i] == ' ' || jsonLine[i] == '\t'))
            {
                i++;
            }
            int start = i;
            if (i < jsonLine.Length && (jsonLine[i] == '-' || jsonLine[i] == '+'))
            {
                i++;
            }
            while (i < jsonLine.Length && jsonLine[i] >= '0' && jsonLine[i] <= '9')
            {
                i++;
            }
            if (i == start)
            {
                return NotCaptured;
            }

            int value;
            return int.TryParse(jsonLine.Substring(start, i - start), out value) ? value : NotCaptured;
        }

        /// <summary>Extracts a JSON boolean field value; false when absent.</summary>
        public static bool JsonBoolField(string jsonLine, string key)
        {
            if (string.IsNullOrEmpty(jsonLine) || string.IsNullOrEmpty(key))
            {
                return false;
            }

            string marker = "\"" + key + "\"";
            int keyAt = jsonLine.IndexOf(marker, StringComparison.Ordinal);
            if (keyAt < 0)
            {
                return false;
            }

            int colon = jsonLine.IndexOf(':', keyAt + marker.Length);
            if (colon < 0)
            {
                return false;
            }

            string rest = jsonLine.Substring(colon + 1).TrimStart();
            return rest.StartsWith("true", StringComparison.Ordinal);
        }
    }
}
