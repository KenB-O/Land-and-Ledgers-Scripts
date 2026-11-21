// Land & Ledgers — run artifact reader tests (Lane 8, Tool 10/11).
//
// Fixtures mirror the real retained-run artifacts (daily-summary.csv with
// quoted fields, console-events.jsonl with severity/message, metadata.json).
// A missing field stays NOT_CAPTURED, never zero and never a guess.

using System.Collections.Generic;
using LandLedgers.Diagnostics;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Diagnostics
{
    public sealed class RunArtifactReaderTests
    {
        private const string DailySummaryCsv =
            "absoluteDayIndex,gameDate,wallUtc,speed,residents,personRecords,activeHouseholdMembershipCount,households,businesses,householdCashCents,businessCashCents,householdLotUnits,retailLotUnits,mealDayIndex,householdsServedMeals,mealsEaten,mealsMissed,mealPathPurchases,mealPathSpendCents,queuedTasks,assignedTasks,inProgressTasks,pausedTasks,dependencyWaitingTasks,passiveWaitingTasks,resourcesReservedTasks,reworkTasks,interruptedTasks,completedTasksToday,failedTasksToday,cancelledTasksRetained,mealTotals,taskCompletion,movementOutcomes,freightAndConstruction,activeShoppingTripCount,activeShoppingTrips\n"
            + "\"1\",\"Year 1870, Month 3, Week 1, Day 2\",\"2026-11-20T19:24:04.6177331Z\",\"Speed250x\",14,98,14,8,7,320000,159552,646,176,0,8,42,0,-1,-1,0,2,0,0,0,0,0,0,0,2,0,2,\"completedDay=0\",\"open: queued=0\",\"NOT_CAPTURED\",\"NOT_CAPTURED\",0,\"none\"\n"
            + "\"8\",\"Year 1870, Month 3, Week 1, Day 9\",\"2026-11-20T19:24:40.6177331Z\",\"Speed250x\",14,98,14,8,7,320000,150410,84,602,7,8,30,12,-1,-1,0,51,0,0,0,0,0,0,0,2,0,4,\"completedDay=7\",\"open: queued=0\",\"NOT_CAPTURED\",\"NOT_CAPTURED\",17,\"planned\"\n";

        private const string ConsoleEventsJsonl =
            "{\"sequence\":1,\"wallUtc\":\"2026-11-20T19:23:40.7681919Z\",\"severity\":\"Log\",\"simulationContext\":\"NOT_STARTED\",\"message\":\"[WorldStartup] TownWorldController instance (bootstrapper).\"}\n"
            + "{\"sequence\":2,\"severity\":\"Warning\",\"message\":\"Pathing request failed (StartNotWalkable): Point is outside the authored walkable floor area on floor 0.\"}\n"
            + "{\"sequence\":3,\"severity\":\"Warning\",\"message\":\"Pathing request failed (StartNotWalkable): Point is outside the authored walkable floor area on floor 0.\"}\n"
            + "{\"sequence\":4,\"severity\":\"Warning\",\"message\":\"[ShoppingTrip] Day 8: shopping trip trip-8-1 ABORTED — No physical route reaches seller location 'building:3': Point is outside the authored walkable floor area\"}\n"
            + "{\"sequence\":5,\"severity\":\"Log\",\"message\":\"[FoodEconomy] purchase settled: buyer='Kennedy', household=H1, seller='General Store', item='bake.bread-loaf', units=12, unit price=8c, total settled=96c, household debit=96c, seller cash credit=96c, receipt='R-1'.\"}\n"
            + "{\"sequence\":6,\"severity\":\"Log\",\"message\":\"[FoodEconomy] purchase received: buyer='Kennedy', household=H1, seller='General Store', item='bake.bread-loaf', units received=12, household stock after=120, receipt='R-1', custody=household inventory.\"}\n"
            + "{\"sequence\":7,\"severity\":\"Warning\",\"message\":\"[ShoppingTrip] ShoppingTripDriver: trip trip-8-2 need 5: counter service expired (No physically present clerk completed service within the customer's finite waiting window.)\"}\n"
            + "{\"sequence\":8,\"severity\":\"Error\",\"message\":\"Something broke.\"}\n";

        private const string MetadataJson =
            "{\"runId\":\"20261120T192311Z-343a8cd2\",\"branch\":\"codex/player-economic-participation-2026-11-18\",\"commit\":\"8537f52fb7dea263fa3ba62da6cbd4d6b5aefbf8\",\"trackedWorkingTreeDirty\":true,\"scenario\":\"millbrook-development-town\",\"startupMode\":\"NewGame\",\"seed\":1886,\"targetDays\":60,\"completedDays\":60,\"speedName\":\"Speed250x\",\"actualResidents\":14,\"actualHouseholds\":8,\"actualBusinesses\":7,\"unexpectedDomainReloadCount\":0,\"missingDailySnapshotOffsets\":[]}";

        // --- CSV ------------------------------------------------------------------

        [Test]
        public void ParseDailySummaryCsv_ParsesQuotedFieldsAndCounts()
        {
            List<DailySummaryRow> rows = RunArtifactReader.ParseDailySummaryCsv(DailySummaryCsv);
            Assert.AreEqual(2, rows.Count);
            Assert.AreEqual(1, rows[0].DayIndex);
            Assert.AreEqual(42, rows[0].MealsEaten);
            Assert.AreEqual(0, rows[0].MealsMissed);
            Assert.AreEqual(646, rows[0].HouseholdLotUnits);
            Assert.AreEqual(176, rows[0].RetailLotUnits);
            Assert.AreEqual(159552, rows[0].BusinessCashCents);
            Assert.AreEqual(-1, rows[0].MealPathPurchases);
            Assert.AreEqual(8, rows[1].DayIndex);
            Assert.AreEqual(30, rows[1].MealsEaten);
            Assert.AreEqual(12, rows[1].MealsMissed);
            Assert.AreEqual(84, rows[1].HouseholdLotUnits);
            Assert.AreEqual(17, rows[1].ActiveShoppingTripCount);
            Assert.AreEqual(51, rows[1].AssignedTasks);
        }

        [Test]
        public void ParseCsvLine_HonoursQuotesAndEscapedQuotes()
        {
            List<string> fields = RunArtifactReader.ParseCsvLine("\"Year 1870, Month 3\",42,\"say \"\"hi\"\"\",-1");
            Assert.AreEqual(4, fields.Count);
            Assert.AreEqual("Year 1870, Month 3", fields[0]);
            Assert.AreEqual("42", fields[1]);
            Assert.AreEqual("say \"hi\"", fields[2]);
            Assert.AreEqual("-1", fields[3]);
        }

        [Test]
        public void ParseDailySummaryCsv_EmptyText_ReturnsNoRows()
        {
            Assert.AreEqual(0, RunArtifactReader.ParseDailySummaryCsv(string.Empty).Count);
            Assert.AreEqual(0, RunArtifactReader.ParseDailySummaryCsv(null).Count);
        }

        // --- JSONL ------------------------------------------------------------------

        [Test]
        public void ParseConsoleEventsJsonl_CountsSeveritiesAndSettleReceive()
        {
            ConsoleEventSummary summary = RunArtifactReader.ParseConsoleEventsJsonl(ConsoleEventsJsonl);
            Assert.AreEqual(8, summary.TotalEvents);
            Assert.AreEqual(4, summary.WarningCount);
            Assert.AreEqual(1, summary.ErrorCount);
            Assert.AreEqual(3, summary.LogCount);
            Assert.AreEqual(1, summary.SettleLineCount);
            Assert.AreEqual(1, summary.ReceiveLineCount);
            Assert.AreEqual(1, summary.ReceiptIdsMatched);
            Assert.AreEqual(0, summary.DoubleSettlements);
            Assert.AreEqual(1, summary.TripsAbortedNoRoute);
            Assert.AreEqual(1, summary.CounterServiceExpiries);
            Assert.AreEqual(2, summary.StartNotWalkableCount);
        }

        [Test]
        public void ParseConsoleEventsJsonl_GroupsBySignatureAndDestination()
        {
            ConsoleEventSummary summary = RunArtifactReader.ParseConsoleEventsJsonl(ConsoleEventsJsonl);
            Assert.AreEqual(2, summary.CountFor("Pathing request failed (StartNotWalkable): Point is outside the authored walkable floor area on floor N.", string.Empty));
            Assert.AreEqual(0, summary.CountFor("Pathing request failed (StartNotWalkable): Point is outside the authored walkable floor area on floor N.", "building:N"));
            Assert.AreEqual(1, summary.CountFor("[ShoppingTrip] Day N: shopping trip trip-N-N ABORTED — No physical route reaches seller location 'building:N': Point is outside the authored walkable floor area", "building:N"));
        }

        [Test]
        public void NormalizeSignature_ReplacesDigitRuns()
        {
            Assert.AreEqual("Day N: trip trip-N-N ABORTED",
                RunArtifactReader.NormalizeSignature("Day 8: trip trip-8-1 ABORTED"));
            Assert.AreEqual("floor N.", RunArtifactReader.NormalizeSignature("floor 0."));
        }

        [Test]
        public void ExtractDestination_FindsSellerLocationAndChair()
        {
            Assert.AreEqual("building:N", RunArtifactReader.ExtractDestination(
                "[ShoppingTrip] Day 8: shopping trip trip-8-1 ABORTED — No physical route reaches seller location 'building:3': Point is outside"));
            Assert.AreEqual("chair:hN-b-chair-right", RunArtifactReader.ExtractDestination(
                "[Meal] Movement to 'chair:h1-b-chair-right' is unsupported for P3"));
            Assert.AreEqual(string.Empty, RunArtifactReader.ExtractDestination(
                "Pathing request failed (StartNotWalkable): Point is outside the authored walkable floor area on floor 0."));
        }

        [Test]
        public void ExtractReceipt_FindsReceiptId()
        {
            Assert.AreEqual("R-1", RunArtifactReader.ExtractReceipt(
                "[FoodEconomy] purchase settled: buyer='Kennedy', receipt='R-1'."));
            Assert.AreEqual(string.Empty, RunArtifactReader.ExtractReceipt("no receipt here"));
        }

        [Test]
        public void ParseConsoleEventsJsonl_DoubleSettlementCounted()
        {
            const string jsonl =
                "{\"severity\":\"Log\",\"message\":\"[FoodEconomy] purchase settled: receipt='R-9'.\"}\n"
                + "{\"severity\":\"Log\",\"message\":\"[FoodEconomy] purchase settled: receipt='R-9'.\"}\n";
            ConsoleEventSummary summary = RunArtifactReader.ParseConsoleEventsJsonl(jsonl);
            Assert.AreEqual(2, summary.SettleLineCount);
            Assert.AreEqual(1, summary.DoubleSettlements);
        }

        // --- metadata.json ------------------------------------------------------------

        [Test]
        public void ParseMetadataJson_ReadsRetained60DayIdentity()
        {
            RunIdentity identity = RunArtifactReader.ParseMetadataJson(MetadataJson);
            Assert.AreEqual("20261120T192311Z-343a8cd2", identity.RunId);
            Assert.AreEqual("codex/player-economic-participation-2026-11-18", identity.Branch);
            Assert.AreEqual("8537f52fb7dea263fa3ba62da6cbd4d6b5aefbf8", identity.CommitSha);
            Assert.IsTrue(identity.DirtyTree);
            Assert.AreEqual("millbrook-development-town", identity.Scenario);
            Assert.AreEqual("NewGame", identity.StartupMode);
            Assert.AreEqual(1886, identity.Seed);
            Assert.AreEqual(60, identity.TargetDays);
            Assert.AreEqual(60, identity.CompletedDays);
            Assert.AreEqual("Speed250x", identity.Speed);
        }

        [Test]
        public void CountJsonArrayEntries_CountsMissingSnapshots()
        {
            Assert.AreEqual(0, RunArtifactReader.CountJsonArrayEntries(MetadataJson, "missingDailySnapshotOffsets"));
            Assert.AreEqual(3, RunArtifactReader.CountJsonArrayEntries("{\"missingDailySnapshotOffsets\":[1, 5, 9]}", "missingDailySnapshotOffsets"));
            Assert.AreEqual(-1, RunArtifactReader.CountJsonArrayEntries("{}", "missingDailySnapshotOffsets"));
        }

        [Test]
        public void JsonStringField_UnescapesQuotes()
        {
            Assert.AreEqual("say \"hi\"", RunArtifactReader.JsonStringField(
                "{\"message\":\"say \\\"hi\\\"\"}", "message"));
            Assert.AreEqual(null, RunArtifactReader.JsonStringField("{}", "message"));
        }

        // --- Build summaries ------------------------------------------------------------

        [Test]
        public void BuildSurvivalEvidence_FillsMeasuredFieldsLeavesSeamsNotCaptured()
        {
            RunIdentity identity = RunArtifactReader.ParseMetadataJson(MetadataJson);
            List<DailySummaryRow> rows = RunArtifactReader.ParseDailySummaryCsv(DailySummaryCsv);
            ConsoleEventSummary console = RunArtifactReader.ParseConsoleEventsJsonl(ConsoleEventsJsonl);
            SurvivalRunEvidence evidence = RunArtifactReader.BuildSurvivalEvidence(
                "20261120T192311Z-343a8cd2", identity, rows, console);
            Assert.IsTrue(evidence.HasLiveRun);
            Assert.AreEqual(1, evidence.SettleLineCount);
            Assert.AreEqual(1, evidence.ReceiveLineCount);
            Assert.AreEqual(1, evidence.ReceiptIdsMatched);
            Assert.AreEqual(1, evidence.TripsAbortedNoRoute);
            Assert.AreEqual(1, evidence.CounterServiceExpiries);
            Assert.AreEqual(2, evidence.MealsEatenPerDay.Count);
            Assert.AreEqual(42, evidence.MealsEatenPerDay[0]);
            Assert.AreEqual(null, evidence.NeedsOpenedWithin1Day);
            Assert.AreEqual(null, evidence.ShoppersChosenSameDay);
            Assert.IsFalse(evidence.LotTracingAvailable);
        }

        [Test]
        public void BuildRunSummary_SumsDaysAndFlagsNotCapturedPurchases()
        {
            RunIdentity identity = RunArtifactReader.ParseMetadataJson(MetadataJson);
            List<DailySummaryRow> rows = RunArtifactReader.ParseDailySummaryCsv(DailySummaryCsv);
            ConsoleEventSummary console = RunArtifactReader.ParseConsoleEventsJsonl(ConsoleEventsJsonl);
            RunSummary summary = RunArtifactReader.BuildRunSummary(
                "20261120T192311Z-343a8cd2", identity, rows, console, 24085, -1);
            Assert.AreEqual(72, summary.MealsEaten);
            Assert.AreEqual(12, summary.MealsMissed);
            Assert.AreEqual(159552, summary.Day1BusinessCashCents);
            Assert.AreEqual(4, summary.WarningCount);
            Assert.AreEqual(1, summary.ErrorCount);
            Assert.IsFalse(summary.MealPathPurchasesCaptured);
            Assert.AreEqual(24085, summary.DroppedTraceEvents);
            RunArtifactReader.ApplyMetadataToSummary(summary, MetadataJson);
            Assert.AreEqual(14, summary.ActualResidents);
            Assert.AreEqual(8, summary.ActualHouseholds);
            Assert.AreEqual(7, summary.ActualBusinesses);
            Assert.AreEqual(0, summary.MissingDailySnapshotCount);
            Assert.AreEqual(0, summary.UnexpectedDomainReloadCount);
        }
    }
}
