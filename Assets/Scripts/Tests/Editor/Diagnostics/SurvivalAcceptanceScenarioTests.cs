// Land & Ledgers — Scenario Lab survival acceptance test tests (Lane 8,
// Tool 10, Agent N §2).
//
// The scenario definition's eight stages assert exactly what Agent N
// specified, including the S7 caveat that State == Done alone does NOT
// prove receipt. A passing fixture run is Accepted; the baseline collapse
// shape fails loudly at the stage that breaks.

using System.Collections.Generic;
using System.Linq;
using LandLedgers.Diagnostics;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Diagnostics
{
    public sealed class SurvivalAcceptanceScenarioTests
    {
        private static SurvivalRunEvidence PassingEvidence()
        {
            var evidence = new SurvivalRunEvidence
            {
                HasLiveRun = true,
                RunId = "test-run-15d",
                Seed = 1886,
                Speed = "10x",
                TargetDays = 15,
                DaysCompleted = 15,
                SettleLineCount = 5,
                ReceiveLineCount = 5,
                ReceiptIdsMatched = 5,
                DoubleSettlements = 0,
                TripsAbortedNoRoute = 0,
                CounterServiceExpiries = 0,
                TripsStuckPlannedOver24h = 0,
                TripsDoneWithZeroUnits = 0,
                MealsFromGenericStaple = 0,
                LarderFullMissedMeals = 0,
                NegativeCashHouseholds = 0,
                MaxConsecutiveZeroMealDays = 0,
                MinTripsPerHouseholdPer3Days = 1,
                NeedsOpenedWithin1Day = true,
                Stage1BlockedByA7 = false,
                ShoppersChosenSameDay = true,
                TripsLeftPlannedWithin24h = true,
                ServedViaTendCustomer = true,
                SettlementReceiptsMatch = true,
                LotDepositedAndNeedDecremented = true,
                LotTracingAvailable = true,
                ContinueMidpointRun = false,
            };
            for (int day = 1; day <= 15; day++)
            {
                evidence.MealsEatenPerDay.Add(day <= 7 ? 42 : 40);
                evidence.MealsMissedPerDay.Add(day <= 7 ? 0 : 2);
                evidence.HouseholdLotsPerDay.Add(100 + day);
            }
            return evidence;
        }

        // --- Definition ---------------------------------------------------------

        [Test]
        public void Config_IsFixed()
        {
            Assert.AreEqual(1886, SurvivalAcceptanceScenario.Seed);
            Assert.AreEqual(15, SurvivalAcceptanceScenario.TargetDays);
            Assert.AreEqual("10x", SurvivalAcceptanceScenario.Speed);
            Assert.AreEqual("NewGame", SurvivalAcceptanceScenario.StartupMode);
            Assert.AreEqual("millbrook-development-town", SurvivalAcceptanceScenario.ScenarioId);
            Assert.AreEqual(14, SurvivalAcceptanceScenario.ExpectedResidents);
            Assert.AreEqual(8, SurvivalAcceptanceScenario.ExpectedHouseholds);
            Assert.AreEqual(7, SurvivalAcceptanceScenario.ExpectedBusinesses);
            Assert.AreEqual(86400, SurvivalAcceptanceScenario.DayLengthSeconds);
            Assert.AreEqual(7, SurvivalAcceptanceScenario.SaveDayIndex);
            Assert.AreEqual(42, SurvivalAcceptanceScenario.MealSlotsPerDay);
            Assert.AreEqual(38, SurvivalAcceptanceScenario.Days11To15MinimumAverageMeals);
        }

        [Test]
        public void StageSpecs_EightStagesWithExactAssertions()
        {
            Assert.AreEqual(8, SurvivalAcceptanceScenario.StageSpecs.Count);
            for (int i = 0; i < 8; i++)
            {
                Assert.AreEqual("S" + (i + 1), SurvivalAcceptanceScenario.StageSpecs[i].StageId);
                Assert.IsTrue(SurvivalAcceptanceScenario.StageSpecs[i].Assertion.Length > 0);
                Assert.IsTrue(SurvivalAcceptanceScenario.StageSpecs[i].EvidenceSource.Length > 0);
            }
            SurvivalStageSpec s7 = SurvivalAcceptanceScenario.StageSpecs[6];
            Assert.IsTrue(s7.Assertion.Contains("does NOT prove receipt"),
                "S7 must carry the caveat that State == Done alone does not prove receipt (HouseholdShoppingLoop.cs:3246).");
            Assert.IsTrue(s7.Assertion.Contains("result.Success == true"));
            Assert.IsTrue(s7.Assertion.Contains("UnitsAcquired > 0"));
            SurvivalStageSpec s1 = SurvivalAcceptanceScenario.StageSpecs[0];
            Assert.IsFalse(s1.ReadyFromVerifiedSource);
            Assert.IsTrue(s1.RequiredPriorRepair.Contains("A7"));
            Assert.IsTrue(SurvivalAcceptanceScenario.StageSpecs[1].ReadyFromVerifiedSource);
        }

        [Test]
        public void PassBars_EightBarsAllProposed()
        {
            Assert.AreEqual(8, SurvivalAcceptanceScenario.PassBars.Count);
            foreach (SurvivalPassBar bar in SurvivalAcceptanceScenario.PassBars)
            {
                Assert.IsTrue(bar.IsProposed, bar.BarId + " is PROPOSED until the manager confirms it (N-14).");
            }
            Assert.IsTrue(SurvivalAcceptanceScenario.PassBars[1].Text.Contains("38/42"));
            Assert.IsTrue(SurvivalAcceptanceScenario.PassBars[3].Text.Contains("3 consecutive days"));
        }

        [Test]
        public void ScenarioCatalog_HasSurvivalPlusFourteenTargetedScenarios()
        {
            Assert.AreEqual(15, SurvivalAcceptanceScenario.ScenarioCatalog.Count);
            foreach (LabScenarioDefinition scenario in SurvivalAcceptanceScenario.ScenarioCatalog)
            {
                Assert.AreEqual(1886, scenario.Seed, scenario.Id);
                Assert.IsTrue(scenario.Acceptance.Length > 0, scenario.Id);
            }
            Assert.IsTrue(SurvivalAcceptanceScenario.ScenarioCatalog.Any(s => s.Id == "survival-acceptance"));
            Assert.IsTrue(SurvivalAcceptanceScenario.ScenarioCatalog.Any(s => s.Id == "food-runout"));
            Assert.IsTrue(SurvivalAcceptanceScenario.ScenarioCatalog.Any(s => s.Id == "save-continue-during-service"));
            Assert.IsTrue(SurvivalAcceptanceScenario.ScenarioCatalog.Any(s => s.Id == "lost-settlement-authority"));
        }

        // --- No live run ----------------------------------------------------------

        [Test]
        public void Evaluate_NoLiveRun_AllBlocked()
        {
            SurvivalAcceptanceResult result = SurvivalAcceptanceScenario.Evaluate(new SurvivalRunEvidence());
            Assert.AreEqual(SurvivalOverallVerdict.NoLiveRun, result.Overall);
            Assert.IsFalse(result.Accepted);
            foreach (SurvivalStageSpec spec in SurvivalAcceptanceScenario.StageSpecs)
            {
                Assert.AreEqual(SurvivalVerdict.Blocked, result.StageVerdict(spec.StageId).Verdict, spec.StageId);
                Assert.IsTrue(result.StageVerdict(spec.StageId).Reason.Contains("No live run"));
            }
            foreach (SurvivalPassBar bar in SurvivalAcceptanceScenario.PassBars)
            {
                Assert.AreEqual(SurvivalVerdict.Blocked, result.BarVerdict(bar.BarId).Verdict, bar.BarId);
            }
        }

        [Test]
        public void Evaluate_WrongConfiguration_Blocked()
        {
            SurvivalRunEvidence evidence = PassingEvidence();
            evidence.Seed = 1887;
            SurvivalAcceptanceResult result = SurvivalAcceptanceScenario.Evaluate(evidence);
            Assert.AreEqual(SurvivalOverallVerdict.NoLiveRun, result.Overall);
            Assert.IsTrue(result.FirstFailure.Contains("not valid on any other configuration"));
        }

        // --- Passing run ------------------------------------------------------------

        [Test]
        public void Evaluate_PassingRun_Accepted()
        {
            SurvivalAcceptanceResult result = SurvivalAcceptanceScenario.Evaluate(PassingEvidence());
            Assert.AreEqual(SurvivalOverallVerdict.Accepted, result.Overall);
            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(string.Empty, result.FirstFailure);
            foreach (SurvivalStageSpec spec in SurvivalAcceptanceScenario.StageSpecs)
            {
                Assert.AreEqual(SurvivalVerdict.Pass, result.StageVerdict(spec.StageId).Verdict, spec.StageId);
            }
            foreach (SurvivalPassBar bar in SurvivalAcceptanceScenario.PassBars)
            {
                Assert.AreEqual(SurvivalVerdict.Pass, result.BarVerdict(bar.BarId).Verdict, bar.BarId);
            }
        }

        // --- Failing runs fail loudly at the breaking stage ---------------------------

        [Test]
        public void Evaluate_DoneWithZeroUnits_FailsStage7()
        {
            SurvivalRunEvidence evidence = PassingEvidence();
            evidence.TripsDoneWithZeroUnits = 1;
            SurvivalAcceptanceResult result = SurvivalAcceptanceScenario.Evaluate(evidence);
            Assert.AreEqual(SurvivalOverallVerdict.Failed, result.Overall);
            Assert.AreEqual(SurvivalVerdict.Fail, result.StageVerdict("S7").Verdict);
            Assert.IsTrue(result.StageVerdict("S7").Reason.Contains("Done alone does not prove receipt"));
            Assert.IsTrue(result.FirstFailure.Contains("S7"));
        }

        [Test]
        public void Evaluate_ZeroSettleLines_FailsStage6()
        {
            SurvivalRunEvidence evidence = PassingEvidence();
            evidence.SettleLineCount = 0;
            evidence.ReceiveLineCount = 0;
            evidence.ReceiptIdsMatched = 0;
            SurvivalAcceptanceResult result = SurvivalAcceptanceScenario.Evaluate(evidence);
            Assert.AreEqual(SurvivalOverallVerdict.Failed, result.Overall);
            Assert.AreEqual(SurvivalVerdict.Fail, result.StageVerdict("S6").Verdict);
            Assert.IsTrue(result.StageVerdict("S6").Reason.Contains("Zero '[FoodEconomy] purchase settled' lines"));
        }

        [Test]
        public void Evaluate_BaselineCollapseShape_FailsAtStages4And6()
        {
            SurvivalRunEvidence evidence = PassingEvidence();
            evidence.TripsAbortedNoRoute = 850;
            evidence.CounterServiceExpiries = 151;
            evidence.SettleLineCount = 0;
            evidence.ReceiveLineCount = 0;
            evidence.ReceiptIdsMatched = 0;
            evidence.MealsEatenPerDay.Clear();
            evidence.MealsMissedPerDay.Clear();
            for (int day = 1; day <= 15; day++)
            {
                evidence.MealsEatenPerDay.Add(day <= 7 ? 42 : 0);
                evidence.MealsMissedPerDay.Add(day <= 7 ? 0 : 42);
            }
            SurvivalAcceptanceResult result = SurvivalAcceptanceScenario.Evaluate(evidence);
            Assert.AreEqual(SurvivalOverallVerdict.Failed, result.Overall);
            Assert.AreEqual(SurvivalVerdict.Fail, result.StageVerdict("S4").Verdict);
            Assert.IsTrue(result.StageVerdict("S4").Reason.Contains("850"));
            Assert.AreEqual(SurvivalVerdict.Fail, result.StageVerdict("S6").Verdict);
            Assert.AreEqual(SurvivalVerdict.Fail, result.BarVerdict("P-2").Verdict);
        }

        [Test]
        public void Evaluate_Stage1BlockedByA7_StaysBlockedNotPassed()
        {
            SurvivalRunEvidence evidence = PassingEvidence();
            evidence.Stage1BlockedByA7 = true;
            SurvivalAcceptanceResult result = SurvivalAcceptanceScenario.Evaluate(evidence);
            Assert.AreEqual(SurvivalVerdict.Blocked, result.StageVerdict("S1").Verdict);
            Assert.IsTrue(result.StageVerdict("S1").Reason.Contains("A7"));
            Assert.AreEqual(SurvivalOverallVerdict.NoLiveRun, result.Overall);
        }

        [Test]
        public void Evaluate_Stage8BlockedWithoutLotTracing()
        {
            SurvivalRunEvidence evidence = PassingEvidence();
            evidence.LotTracingAvailable = false;
            SurvivalAcceptanceResult result = SurvivalAcceptanceScenario.Evaluate(evidence);
            Assert.AreEqual(SurvivalVerdict.Blocked, result.StageVerdict("S8").Verdict);
            Assert.IsTrue(result.StageVerdict("S8").Reason.Contains("A6"));
            Assert.IsFalse(result.Accepted);
        }

        [Test]
        public void Evaluate_GenericStapleMeal_FailsStage8()
        {
            SurvivalRunEvidence evidence = PassingEvidence();
            evidence.MealsFromGenericStaple = 3;
            SurvivalAcceptanceResult result = SurvivalAcceptanceScenario.Evaluate(evidence);
            Assert.AreEqual(SurvivalVerdict.Fail, result.StageVerdict("S8").Verdict);
            Assert.IsTrue(result.StageVerdict("S8").Reason.Contains("staple_food_unspecified"));
        }

        [Test]
        public void DefinitionMarkdown_NamesConfigStagesAndBars()
        {
            string markdown = SurvivalAcceptanceScenario.DefinitionMarkdown();
            Assert.IsTrue(markdown.Contains("seed 1886, 15 days, 10x"));
            Assert.IsTrue(markdown.Contains("S7"));
            Assert.IsTrue(markdown.Contains("P-1"));
            Assert.IsTrue(markdown.Contains("PROPOSED"));
            Assert.IsTrue(markdown.Contains("cannot execute a live run here"));
        }
    }
}
