// Land & Ledgers — run acceptance report tests (Lane 8, Agent N §3).
//
// Calendar completion alone never accepts a run: a run whose calendar
// section passes but whose economy is broken is FAILED, and a run whose
// seams are missing is NOT CAPTURED, never accepted on a guess.

using System.Collections.Generic;
using LandLedgers.Diagnostics;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Diagnostics
{
    public sealed class RunAcceptanceReportTests
    {
        private static RunSummary Retained60DayShapeSummary()
        {
            var identity = RunIdentity.Retained60DayBaseline();
            var summary = new RunSummary
            {
                RunId = "20261120T192311Z-343a8cd2",
                Identity = identity,
                DaysCompleted = 60,
                WarningCount = 6317,
                ErrorCount = 0,
                TotalConsoleEvents = 8271,
                RoutineLogCount = 1954,
                MealsEaten = 326,
                MealsMissed = 2194,
                Day1BusinessCashCents = 159552,
                SettleLineCount = 0,
                ReceiveLineCount = 0,
                ReceiptIdsMatched = 0,
                DoubleSettlements = 0,
                TripsAbortedNoRoute = 850,
                CounterServiceExpiries = 151,
                TravelWithoutArrivalCount = 0,
                TaskNoProgressCount = 0,
                TaskDisappearedCount = 0,
                DroppedTraceEvents = 24085,
                MissingDailySnapshotCount = 0,
                UnexpectedDomainReloadCount = 0,
                ActualResidents = 14,
                ActualHouseholds = 8,
                ActualBusinesses = 7,
            };
            for (int day = 1; day <= 60; day++)
            {
                summary.MealsEatenPerDay.Add(day <= 7 ? 42 : (day == 8 ? 30 : (day == 9 ? 2 : 0)));
                summary.MealsMissedPerDay.Add(day <= 7 ? 0 : (day == 8 ? 12 : (day == 9 ? 40 : 42)));
                summary.HouseholdLotUnits.Add(day <= 7 ? 646 - day * 10 : 80);
            }
            summary.Signatures.Add(new RunSignatureCount(
                "Pathing request failed (StartNotWalkable): Point is outside the authored walkable floor area on floor N.",
                string.Empty, 2878));
            return summary;
        }

        private static SurvivalAcceptanceResult FailedSurvival()
        {
            var stages = new Dictionary<string, SurvivalStageVerdict>();
            foreach (SurvivalStageSpec spec in SurvivalAcceptanceScenario.StageSpecs)
            {
                stages[spec.StageId] = new SurvivalStageVerdict(SurvivalVerdict.Fail, "Baseline collapse: zero settle lines.");
            }
            var bars = new Dictionary<string, SurvivalStageVerdict>();
            foreach (SurvivalPassBar bar in SurvivalAcceptanceScenario.PassBars)
            {
                bars[bar.BarId] = new SurvivalStageVerdict(SurvivalVerdict.Fail, "Baseline collapse.");
            }
            return new SurvivalAcceptanceResult(SurvivalOverallVerdict.Failed,
                "20261120T192311Z-343a8cd2", stages, bars, "Stage S6 failed: zero settle lines.");
        }

        // --- Construction -----------------------------------------------------------

        [Test]
        public void Build_NullSummary_IsNotRun()
        {
            RunAcceptanceReport report = RunAcceptanceReportBuilder.Build(null, null, false);
            Assert.AreEqual(RunAcceptanceOverall.NotRun, report.Overall);
            Assert.AreEqual(6, report.Sections.Count);
            Assert.IsTrue(report.ToMarkdown().Contains("NOT RUN"));
        }

        [Test]
        public void Combine_FailWinsThenNotCapturedThenNotRun()
        {
            Assert.AreEqual(RunAcceptanceVerdict.Pass, RunAcceptanceReportBuilder.Combine(
                new List<RunAcceptanceCheck> { new RunAcceptanceCheck("a", RunAcceptanceVerdict.Pass, "") }));
            Assert.AreEqual(RunAcceptanceVerdict.NotCaptured, RunAcceptanceReportBuilder.Combine(
                new List<RunAcceptanceCheck>
                {
                    new RunAcceptanceCheck("a", RunAcceptanceVerdict.Pass, ""),
                    new RunAcceptanceCheck("b", RunAcceptanceVerdict.NotCaptured, ""),
                }));
            Assert.AreEqual(RunAcceptanceVerdict.Fail, RunAcceptanceReportBuilder.Combine(
                new List<RunAcceptanceCheck>
                {
                    new RunAcceptanceCheck("a", RunAcceptanceVerdict.NotCaptured, ""),
                    new RunAcceptanceCheck("b", RunAcceptanceVerdict.Fail, ""),
                }));
            Assert.AreEqual(RunAcceptanceVerdict.NotRun, RunAcceptanceReportBuilder.Combine(
                new List<RunAcceptanceCheck> { new RunAcceptanceCheck("a", RunAcceptanceVerdict.NotRun, "") }));
        }

        // --- Retained 60-day shape: calendar passes, run fails ------------------------

        [Test]
        public void Build_Retained60DayShape_CalendarPassesButRunFails()
        {
            RunAcceptanceReport report = RunAcceptanceReportBuilder.Build(
                Retained60DayShapeSummary(), FailedSurvival(), false);
            Assert.AreEqual(RunAcceptanceOverall.Failed, report.Overall);
            Assert.IsFalse(report.Accepted);
            RunAcceptanceSection calendar = report.Section(RunAcceptanceSectionKind.Calendar);
            Assert.AreEqual(RunAcceptanceVerdict.Pass, calendar.Verdict);
            Assert.AreEqual(6, report.Sections.Count);
            Assert.IsTrue(report.FirstFailure.Length > 0);
            Assert.IsTrue(report.ToMarkdown().Contains("Calendar completed — PASS"));
            Assert.IsTrue(report.ToMarkdown().Contains("A pass in the calendar section accepts nothing else"));
        }

        [Test]
        public void Build_Retained60DayShape_NamesDay1CashGap()
        {
            RunAcceptanceReport report = RunAcceptanceReportBuilder.Build(
                Retained60DayShapeSummary(), FailedSurvival(), false);
            RunAcceptanceSection economic = report.Section(RunAcceptanceSectionKind.EconomicIntegrity);
            Assert.AreEqual(RunAcceptanceVerdict.Fail, economic.Verdict);
            Assert.IsTrue(report.ToMarkdown().Contains("2,448c") || report.ToMarkdown().Contains("2448c"));
        }

        [Test]
        public void Build_Retained60DayShape_SaveContinueIsNotRunNotPassed()
        {
            RunAcceptanceReport report = RunAcceptanceReportBuilder.Build(
                Retained60DayShapeSummary(), FailedSurvival(), false);
            RunAcceptanceSection saveContinue = report.Section(RunAcceptanceSectionKind.SaveContinue);
            Assert.AreEqual(RunAcceptanceVerdict.NotRun, saveContinue.Verdict);
            Assert.IsTrue(report.ToMarkdown().Contains("NOT RUN"));
        }

        [Test]
        public void Build_Retained60DayShape_PhysicalLegitimacyFailsOnAborts()
        {
            RunAcceptanceReport report = RunAcceptanceReportBuilder.Build(
                Retained60DayShapeSummary(), FailedSurvival(), false);
            RunAcceptanceSection physical = report.Section(RunAcceptanceSectionKind.PhysicalLegitimacy);
            Assert.AreEqual(RunAcceptanceVerdict.Fail, physical.Verdict);
            Assert.IsTrue(report.ToMarkdown().Contains("850 trip(s) ABORTED"));
        }

        // --- Best case today: seams missing means NOT CAPTURED, never accepted ----------

        [Test]
        public void Build_BestCaseToday_IsNotCapturedNotAccepted()
        {
            RunSummary summary = Retained60DayShapeSummary();
            summary.MealsEaten = 630;
            summary.MealsMissed = 0;
            summary.TripsAbortedNoRoute = 0;
            summary.CounterServiceExpiries = 0;
            summary.Day1BusinessCashCents = 162000;
            summary.MealsEatenPerDay.Clear();
            summary.MealsMissedPerDay.Clear();
            for (int day = 1; day <= 60; day++)
            {
                summary.MealsEatenPerDay.Add(42);
                summary.MealsMissedPerDay.Add(0);
            }

            var stages = new Dictionary<string, SurvivalStageVerdict>();
            foreach (SurvivalStageSpec spec in SurvivalAcceptanceScenario.StageSpecs)
            {
                stages[spec.StageId] = new SurvivalStageVerdict(SurvivalVerdict.Pass, "Pass.");
            }
            var bars = new Dictionary<string, SurvivalStageVerdict>();
            foreach (SurvivalPassBar bar in SurvivalAcceptanceScenario.PassBars)
            {
                bars[bar.BarId] = new SurvivalStageVerdict(SurvivalVerdict.Pass, "Pass.");
            }
            var survival = new SurvivalAcceptanceResult(SurvivalOverallVerdict.Accepted,
                "20261120T192311Z-343a8cd2", stages, bars, string.Empty);

            RunAcceptanceReport report = RunAcceptanceReportBuilder.Build(summary, survival, false);
            Assert.IsFalse(report.Accepted);
            Assert.AreEqual(RunAcceptanceOverall.NotCaptured, report.Overall);
            Assert.IsTrue(report.ToMarkdown().Contains("NOT CAPTURED"));
        }

        [Test]
        public void Build_SaveContinueMidpointWithoutTool07_IsNotAccepted()
        {
            RunAcceptanceReport report = RunAcceptanceReportBuilder.Build(
                Retained60DayShapeSummary(), FailedSurvival(), true);
            RunAcceptanceSection saveContinue = report.Section(RunAcceptanceSectionKind.SaveContinue);
            Assert.AreNotEqual(RunAcceptanceVerdict.Pass, saveContinue.Verdict);
            Assert.IsTrue(report.ToMarkdown().Contains("F-S4"));
        }

        // --- Serialisation ----------------------------------------------------------------

        [Test]
        public void Report_ToJson_IsMachineReadable()
        {
            RunAcceptanceReport report = RunAcceptanceReportBuilder.Build(
                Retained60DayShapeSummary(), FailedSurvival(), false);
            string json = report.ToJson();
            Assert.IsTrue(json.Contains("\"runId\":\"20261120T192311Z-343a8cd2\""));
            Assert.IsTrue(json.Contains("\"overall\":\"FAILED\""));
            Assert.IsTrue(json.Contains("\"kind\":\"Calendar completed\""));
            Assert.IsTrue(json.Contains("\"verdict\":\"PASS\""));
            Assert.IsTrue(json.Contains("\"verdict\":\"FAIL\""));
            Assert.IsTrue(json.StartsWith("{") && json.EndsWith("}"));
        }

        [Test]
        public void VerdictLabels_AreStable()
        {
            Assert.AreEqual("PASS", RunAcceptanceReport.VerdictLabel(RunAcceptanceVerdict.Pass));
            Assert.AreEqual("FAIL", RunAcceptanceReport.VerdictLabel(RunAcceptanceVerdict.Fail));
            Assert.AreEqual("NOT RUN", RunAcceptanceReport.VerdictLabel(RunAcceptanceVerdict.NotRun));
            Assert.AreEqual("NOT CAPTURED", RunAcceptanceReport.VerdictLabel(RunAcceptanceVerdict.NotCaptured));
            Assert.AreEqual("ACCEPTED", RunAcceptanceReport.OverallLabel(RunAcceptanceOverall.Accepted));
            Assert.AreEqual("NOT CAPTURED", RunAcceptanceReport.OverallLabel(RunAcceptanceOverall.NotCaptured));
        }
    }
}
