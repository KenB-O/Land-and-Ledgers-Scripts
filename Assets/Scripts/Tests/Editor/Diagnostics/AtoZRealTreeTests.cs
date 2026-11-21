// Land & Ledgers — A-to-Z real-tree integration tests (Lane 8, §20).
//
// The same checks as the fixture tests, run against the real tree and the
// real retained 60-day run (20261120T192311Z-343a8cd2). This is the proof
// the audit reproduces the known manual findings from real evidence — the
// 24-loaf wholesale removal, the SF-7 byproduct settlement, the Firewood
// gap, the bakery fuel-death config, and the survival-loop collapse shape
// (0 settle lines, 850 aborted trips, 326 eaten / 2,194 missed meals).
//
// These tests read the worktree via LAND_LEDGERS_REPO (the harness sets
// it). They assert the current HEAD's state; if a later lane repairs one
// of these defects, the corresponding assertion must be updated to the new
// status — never weakened to keep the test green.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LandLedgers.Diagnostics;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Diagnostics
{
    public sealed class AtoZRealTreeTests
    {
        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, "Assets/Core/Population/DevelopmentTownConfiguration.asset")))
            {
                return fromEnv;
            }

            string dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "Assets/Core/Population/DevelopmentTownConfiguration.asset")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("Could not locate the Land & Ledgers repo (LAND_LEDGERS_REPO not set and no ancestor holds the config asset).");
        }

        private static string Read(string repoRoot, string relativePath)
        {
            string fullPath = Path.Combine(repoRoot, relativePath);
            Assert.IsTrue(File.Exists(fullPath), "Expected file missing: " + fullPath);
            return File.ReadAllText(fullPath);
        }

        private static AtoZFinding Find(AtoZAuditReport report, string id)
        {
            return report.Findings.First(f => f.Definition.Id == id);
        }

        // --- Real source tree ------------------------------------------------------

        [Test]
        public void RealTree_AuditReproducesKnownFindings()
        {
            string repoRoot = FindRepoRoot();
            var evidence = new AtoZSourceEvidence
            {
                ConfigAssetText = Read(repoRoot, "Assets/Core/Population/DevelopmentTownConfiguration.asset"),
                WholesaleAccountsText = Read(repoRoot, "Assets/Scripts/Domains/Economy/Equipment/ScenarioOpeningWholesaleAccounts.cs"),
                ByproductSettlementText = Read(repoRoot, "Assets/Scripts/Domains/Economy/Core/FuelDealerByproductSettlement.cs"),
                SawmillTransferText = Read(repoRoot, "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs"),
                HouseholdMealsText = Read(repoRoot, "Assets/Scripts/Domains/Population/HouseholdMeals.cs"),
            };
            AtoZAuditReport report = AtoZAuditRunner.Evaluate(evidence);

            Assert.AreEqual(AtoZFindingStatus.ImplementedUnverified, Find(report, "A-06-WHOLESALE-CONFIG").Status);
            Assert.AreEqual(AtoZFindingStatus.ImplementedUnverified, Find(report, "A-06-WHOLESALE-FALLBACK").Status);
            Assert.AreEqual(AtoZFindingStatus.SuspectedFix, Find(report, "W-SF7-BYPRODUCT").Status);
            Assert.AreEqual(AtoZFindingStatus.Unresolved, Find(report, "B-FIREWOOD").Status);
            Assert.AreEqual(AtoZFindingStatus.Unresolved, Find(report, "E-14-BAKERY-FUEL").Status);
            Assert.AreEqual(AtoZFindingStatus.ImplementedUnverified, Find(report, "A-08-STAPLE-FOOD").Status);
            Assert.AreEqual(AtoZFindingStatus.ImplementedUnverified, Find(report, "A-05-MISSED-MEAL-REASON").Status);
        }

        [Test]
        public void RealTree_FirewoodCountIsZeroAndBakeryFuelIsZero()
        {
            string repoRoot = FindRepoRoot();
            string config = Read(repoRoot, "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            Assert.AreEqual(0, AtoZAuditRunner.CountActivityType(config, 10));
            Assert.AreEqual(33, AtoZAuditRunner.CountActivityType(config, 0));
            Assert.AreEqual(12, AtoZAuditRunner.CountActivityType(config, 1));
            Assert.AreEqual(19, AtoZAuditRunner.CountActivityType(config, 14));
            string bakery = AtoZAuditRunner.BakeryBlock(config);
            Assert.AreEqual(0, AtoZAuditRunner.ConfigIntValue(bakery, "fuelDealerIntakeUnitsPerBakeryUnit"));
            Assert.AreEqual(40, AtoZAuditRunner.ConfigIntValue(bakery, "bakeryFuelUnits"));
        }

        // --- Real retained 60-day run ------------------------------------------------

        private static RunSummary ReadRetained60DayRun(string repoRoot, out SurvivalRunEvidence survivalEvidence)
        {
            string runDir = Path.Combine(repoRoot, "Reports/Playtests/Runs/20261120T192311Z-343a8cd2");
            Assert.IsTrue(Directory.Exists(runDir), "Retained 60-day run directory missing: " + runDir);
            string metadataText = Read(repoRoot, "Reports/Playtests/Runs/20261120T192311Z-343a8cd2/metadata.json");
            string csvText = Read(repoRoot, "Reports/Playtests/Runs/20261120T192311Z-343a8cd2/daily-summary.csv");
            string jsonlText = Read(repoRoot, "Reports/Playtests/Runs/20261120T192311Z-343a8cd2/console-events.jsonl");

            RunIdentity identity = RunArtifactReader.ParseMetadataJson(metadataText);
            List<DailySummaryRow> rows = RunArtifactReader.ParseDailySummaryCsv(csvText);
            ConsoleEventSummary console = RunArtifactReader.ParseConsoleEventsJsonl(jsonlText);

            survivalEvidence = RunArtifactReader.BuildSurvivalEvidence(identity.RunId, identity, rows, console);
            RunSummary summary = RunArtifactReader.BuildRunSummary(identity.RunId, identity, rows, console, 24085, -1);
            RunArtifactReader.ApplyMetadataToSummary(summary, metadataText);
            return summary;
        }

        [Test]
        public void RealRun_ReaderReproducesAgentNCounts()
        {
            string repoRoot = FindRepoRoot();
            SurvivalRunEvidence evidence;
            RunSummary summary = ReadRetained60DayRun(repoRoot, out evidence);

            Assert.AreEqual("20261120T192311Z-343a8cd2", summary.RunId);
            Assert.AreEqual(60, summary.DaysCompleted);
            Assert.AreEqual(8271, summary.TotalConsoleEvents);
            Assert.AreEqual(6317, summary.WarningCount);
            Assert.AreEqual(0, summary.ErrorCount);
            Assert.AreEqual(326, summary.MealsEaten);
            Assert.AreEqual(2194, summary.MealsMissed);
            Assert.AreEqual(159552, summary.Day1BusinessCashCents);
            Assert.AreEqual(0, summary.SettleLineCount);
            Assert.AreEqual(0, summary.ReceiveLineCount);
            Assert.AreEqual(850, summary.TripsAbortedNoRoute);
            Assert.AreEqual(151, summary.CounterServiceExpiries);
            Assert.AreEqual(0, evidence.SettleLineCount);
            Assert.AreEqual(850, evidence.TripsAbortedNoRoute);
            Assert.AreEqual(42, summary.MealsEatenPerDay[0]);
            Assert.AreEqual(0, summary.MealsEatenPerDay[9]);
            Assert.AreEqual(80, summary.HouseholdLotUnits[59]);
            Assert.AreEqual(2878, summary.SignatureCount(
                "Pathing request failed (StartNotWalkable): Point is outside the authored walkable floor area on floor N.", string.Empty));
        }

        [Test]
        public void RealRun_SurvivalTestIsNotValidOnTheRetainedConfiguration()
        {
            string repoRoot = FindRepoRoot();
            SurvivalRunEvidence evidence;
            ReadRetained60DayRun(repoRoot, out evidence);

            // The retained run is 250x / 60 days; the acceptance test is
            // 10x / 15 days. The evaluator must refuse to judge it, not
            // silently evaluate the wrong configuration.
            SurvivalAcceptanceResult result = SurvivalAcceptanceScenario.Evaluate(evidence);
            Assert.AreEqual(SurvivalOverallVerdict.NoLiveRun, result.Overall);
            Assert.IsTrue(result.FirstFailure.Contains("not valid on any other configuration"));
        }

        [Test]
        public void RealRun_AcceptanceReportFailsWhileCalendarPasses()
        {
            string repoRoot = FindRepoRoot();
            SurvivalRunEvidence evidence;
            RunSummary summary = ReadRetained60DayRun(repoRoot, out evidence);

            RunAcceptanceReport report = RunAcceptanceReportBuilder.Build(summary, null, false);
            Assert.AreEqual(RunAcceptanceOverall.Failed, report.Overall);
            Assert.IsFalse(report.Accepted);
            Assert.AreEqual(RunAcceptanceVerdict.Pass, report.Section(RunAcceptanceSectionKind.Calendar).Verdict);
            Assert.AreEqual(RunAcceptanceVerdict.Fail, report.Section(RunAcceptanceSectionKind.EconomicIntegrity).Verdict);
            Assert.AreEqual(RunAcceptanceVerdict.Fail, report.Section(RunAcceptanceSectionKind.PhysicalLegitimacy).Verdict);
            Assert.AreEqual(RunAcceptanceVerdict.NotRun, report.Section(RunAcceptanceSectionKind.SaveContinue).Verdict);
        }

        [Test]
        public void RealRun_ComparatorRefusesRetainedRunAsSameSource()
        {
            string repoRoot = FindRepoRoot();
            SurvivalRunEvidence evidence;
            RunSummary retained = ReadRetained60DayRun(repoRoot, out evidence);

            RunSummary candidate = new RunSummary
            {
                RunId = "hypothetical-muse-branch-run",
                Identity = new RunIdentity
                {
                    RunId = "hypothetical-muse-branch-run",
                    Seed = 1886,
                    Scenario = "millbrook-development-town",
                    StartupMode = "NewGame",
                    TargetDays = 60,
                    CompletedDays = 60,
                    Speed = "Speed250x",
                    Branch = "muse/lane-8",
                    CommitSha = "0000000000000000000000000000000000000000",
                    DirtyTree = false,
                },
            };

            // Different source state AND dirty-vs-clean: the gate refuses.
            RunComparisonResult refused = RegressionComparator.Compare(retained, candidate, false, false, null);
            Assert.AreEqual(Comparability.Refused, refused.Comparability);

            // Same dirty tree, same speed and days, different source state
            // (not the explicit subject): BASELINE SHAPE ONLY, never before/after.
            candidate.Identity.DirtyTree = true;
            RunComparisonResult shapeOnly = RegressionComparator.Compare(retained, candidate, false, false, null);
            Assert.AreEqual(Comparability.BaselineShapeOnly, shapeOnly.Comparability);
            Assert.IsFalse(shapeOnly.IsBeforeAfter);
            Assert.IsTrue(shapeOnly.ToMarkdown().Contains("BASELINE SHAPE ONLY"));
        }
    }
}
