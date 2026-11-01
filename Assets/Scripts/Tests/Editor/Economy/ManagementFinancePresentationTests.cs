using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.FirstLedger;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    public sealed class ManagementFinancePresentationTests
    {
        private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

        [Test]
        public void FinanceHealthMarksBelowReserveOrCashLimitedBusinessesAsStarving()
        {
            Assert.AreEqual(
                "Starving",
                InvokeString(
                    "ClassifyFinanceBusinessHealth",
                    2400,
                    2500,
                    2,
                    2,
                    0.85f,
                    string.Empty,
                    new List<string>()));

            Assert.AreEqual(
                "Starving",
                InvokeString(
                    "ClassifyFinanceBusinessHealth",
                    8000,
                    2500,
                    2,
                    2,
                    0.85f,
                    "cash-limited input procurement",
                    new List<string>()));
        }

        [Test]
        public void FinanceHealthSeparatesPressuredStableAndHealthyStates()
        {
            Assert.AreEqual(
                "Pressured",
                InvokeString(
                    "ClassifyFinanceBusinessHealth",
                    7000,
                    2500,
                    1,
                    2,
                    0.8f,
                    string.Empty,
                    new List<string>()));

            Assert.AreEqual(
                "Stable",
                InvokeString(
                    "ClassifyFinanceBusinessHealth",
                    6000,
                    2500,
                    2,
                    2,
                    0.65f,
                    string.Empty,
                    new List<string>()));

            Assert.AreEqual(
                "Healthy",
                InvokeString(
                    "ClassifyFinanceBusinessHealth",
                    9000,
                    2500,
                    2,
                    2,
                    0.9f,
                    string.Empty,
                    new List<string>()));
        }

        [Test]
        public void FinanceAffordabilityTreatsBusinessCashAsManuallyDrawable()
        {
            Assert.AreEqual(8000, InvokeInt("CalculateWithdrawableBusinessCashCents", 8000, 5000));
            Assert.AreEqual(3000, InvokeInt("CalculateWithdrawableBusinessCashCents", 3000, 5000));
            Assert.AreEqual(0, InvokeInt("CalculateWithdrawableBusinessCashCents", -100, 5000));
        }

        [Test]
        public void FinanceSafetyReflectsDebtAndBusinessPressure()
        {
            Assert.AreEqual("Unsafe", InvokeString("BuildFinanceSafetyLabel", 1000, 2500, 0, 0, false));
            Assert.AreEqual("Unsafe", InvokeString("BuildFinanceSafetyLabel", 5000, 0, 1, 0, false));
            Assert.AreEqual("Tight", InvokeString("BuildFinanceSafetyLabel", 5000, 2500, 0, 0, false));
            Assert.AreEqual("Tight", InvokeString("BuildFinanceSafetyLabel", 5000, 0, 0, 1, false));
            Assert.AreEqual("Safe", InvokeString("BuildFinanceSafetyLabel", 5000, 1000, 0, 0, false));
        }

        [Test]
        public void StoreCashLedgerUsesManagementLabelsAndOmitsOwnerCash()
        {
            string ledger = InvokeString("BuildStoreCashLedgerText", 12500, 5000);

            StringAssert.Contains("Store Cash", ledger);
            StringAssert.Contains("Operating Reserve", ledger);
            StringAssert.Contains("Available to Transfer", ledger);
            Assert.False(ledger.Contains("Protected Reserve"), ledger);
            Assert.False(ledger.Contains("Free to Move"), ledger);
            Assert.False(ledger.Contains("Owner Liquid Cash"), ledger);
        }

        [Test]
        public void StoreNetPerformanceReadoutUsesRowsInsteadOfCompressedSlashLine()
        {
            BusinessRuntimeState runtime = new();
            SetField(runtime, "lastDailyNetCents", 1200);
            SetField(runtime, "weekToDateNetCents", -350);
            SetField(runtime, "monthToDateNetCents", 2400);
            SetField(runtime, "yearToDateNetCents", -100);
            SetField(runtime, "allTimeNetCents", 9000);

            string readout = InvokeString("BuildBusinessNetReadout", runtime);

            StringAssert.Contains("Net Performance", readout);
            StringAssert.Contains("Today", readout);
            StringAssert.Contains("Week", readout);
            StringAssert.Contains("Month", readout);
            StringAssert.Contains("Year", readout);
            StringAssert.Contains("All-Time", readout);
            StringAssert.Contains("<color=", readout);
            Assert.False(readout.Contains("Net Today / Week / Month / Year / All-Time"), readout);
            Assert.False(readout.Contains(" / "), readout);
        }

        [Test]
        public void CashTransferExactMoneyParserAcceptsCurrencyStyleInput()
        {
            AssertParsedMoney("25", 2500);
            AssertParsedMoney("$25", 2500);
            AssertParsedMoney("25.50", 2550);
            AssertParsedMoney("1,250.00", 125000);

            AssertInvalidMoney(string.Empty);
            AssertInvalidMoney("abc");
            AssertInvalidMoney("-1");
            AssertInvalidMoney("0");
        }

        [Test]
        public void StoreDetailTextUsesFinalUpperHierarchyLabels()
        {
            string overview = InvokeString("BuildStoreUnavailableOverviewFallback", true);

            StringAssert.Contains("Store Status", overview);
            Assert.False(overview.Contains("Operating Snapshot"), overview);
            Assert.False(overview.Contains("Manager Brief"), overview);
        }

        [Test]
        public void CashTransferSubmitRoutingMapsInputsToExactActions()
        {
            Assert.AreEqual(
                "DepositExactBusinessCash",
                InvokeString("ResolveCashTransferSubmitActionName", "BusinessCashTransfer_DepositInput"));
            Assert.AreEqual(
                "WithdrawExactBusinessCash",
                InvokeString("ResolveCashTransferSubmitActionName", "BusinessCashTransfer_WithdrawInput"));
            Assert.AreEqual(
                "ApplyExactBusinessCashThresholds",
                InvokeString("ResolveCashTransferSubmitActionName", "BusinessCashTransfer_LowerThresholdInput"));
            Assert.AreEqual(
                "ApplyExactBusinessCashThresholds",
                InvokeString("ResolveCashTransferSubmitActionName", "BusinessCashTransfer_UpperThresholdInput"));
        }



        [Test]
        public void WealthReadSummariesAreReadableAndGoalAware()
        {
            UnityEngine.GameObject go = new("Portfolio Test Target");
            try
            {
                var portfolio = go.AddComponent<LandLedgers.Economy.PlayerPortfolioManager>();
                SetField(portfolio, "ownerCashCents", 250000);
                SetField(portfolio, "initialized", true);
                SetField(portfolio, "lastBusinessCashCents", 500000);
                SetField(portfolio, "lastAssetValueCents", 750000);
                SetField(portfolio, "lastDebtLiabilityCents", 100000);
                SetField(portfolio, "lastNetWorthCents", 1400000);
                SetField(portfolio, "netWorthWinTargetCents", 1000000);
                SetField(portfolio, "netWorthWinReached", true);

                string headline = portfolio.BuildWealthHeadline();
                string breakdown = portfolio.BuildWealthBreakdownSummary();
                string progress = portfolio.BuildWealthGoalProgressSummary();
                string saveRead = portfolio.BuildWealthSaveReadSummary();

                StringAssert.Contains("Net Worth", headline);
                StringAssert.Contains("Liquid", headline);
                StringAssert.Contains("Goal reached", headline);
                StringAssert.Contains("Business Equity", breakdown);
                StringAssert.Contains("Business Cash", breakdown);
                StringAssert.Contains("Assets", breakdown);
                StringAssert.Contains("Liabilities", breakdown);
                StringAssert.Contains("goal reached", progress.ToLowerInvariant());
                StringAssert.Contains("Wealth Read", saveRead);
                StringAssert.Contains("Business Equity", saveRead);
                StringAssert.Contains("Liabilities", saveRead);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void WealthHeadlineKeepsBusinessCashOutOfLiquidLabel()
        {
            UnityEngine.GameObject go = new("Portfolio Liquid Separation Test");
            try
            {
                var portfolio = go.AddComponent<LandLedgers.Economy.PlayerPortfolioManager>();
                SetField(portfolio, "ownerCashCents", 250000);
                SetField(portfolio, "initialized", true);
                SetField(portfolio, "lastBusinessCashCents", 500000);
                SetField(portfolio, "lastAssetValueCents", 0);
                SetField(portfolio, "lastDebtLiabilityCents", 0);
                SetField(portfolio, "lastNetWorthCents", 750000);

                string headline = portfolio.BuildWealthHeadline();
                string breakdown = portfolio.BuildWealthBreakdownSummary();

                StringAssert.Contains("Liquid", headline);
                StringAssert.Contains("$2,500", headline);
                Assert.False(headline.Contains("Business Cash"), headline);
                Assert.False(headline.Contains("Liquid $7,500"), headline);
                StringAssert.Contains("Business Cash", breakdown);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }


        [Test]
        public void WealthReadinessSummariesExplainLiquidityAndLoanPressure()
        {
            UnityEngine.GameObject go = new("Portfolio Loan Read Test");
            try
            {
                var portfolio = go.AddComponent<LandLedgers.Economy.PlayerPortfolioManager>();
                SetField(portfolio, "ownerCashCents", 125000);
                SetField(portfolio, "initialized", true);
                SetField(portfolio, "lastBusinessCashCents", 400000);
                SetField(portfolio, "lastAssetValueCents", 600000);
                SetField(portfolio, "lastDebtLiabilityCents", 90000);
                SetField(portfolio, "lastNetWorthCents", 1035000);

                string liquidity = portfolio.BuildLiquidityPressureSummary();
                string loanRead = portfolio.BuildLoanReadinessSummary(300000, 18000);

                StringAssert.Contains("Liquid", liquidity);
                StringAssert.Contains("Loan Read", loanRead);
                StringAssert.Contains("Financing Gap", loanRead);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void WealthCommitmentSummariesExplainStagedReadiness()
        {
            UnityEngine.GameObject go = new("Portfolio Commitment Read Test");
            try
            {
                var portfolio = go.AddComponent<LandLedgers.Economy.PlayerPortfolioManager>();
                SetField(portfolio, "ownerCashCents", 180000);
                SetField(portfolio, "initialized", true);
                SetField(portfolio, "lastBusinessCashCents", 250000);
                SetField(portfolio, "lastAssetValueCents", 450000);
                SetField(portfolio, "lastDebtLiabilityCents", 50000);
                SetField(portfolio, "lastNetWorthCents", 830000);

                string staged = portfolio.BuildStagedCommitmentSummary(240000, 60000, 12000);
                string ownerRead = portfolio.BuildAcquisitionOwnerReadSummary(240000, 60000, 40000);

                StringAssert.Contains("Commitment Read", staged);
                StringAssert.Contains("Owner Read", ownerRead);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void QuietLeadAndCivicImplicationBodiesDoNotRepeatPanelHeaders()
        {
            string quietLeadBody = InvokeString("BuildOffMarketBodyText", 3, 5, 2);
            string civicImplicationBody = InvokeString("BuildGovernmentOwnerImplicationsBodyText", "Town Hall: Built | Civic confidence +12% | Maturity +9%");

            StringAssert.Contains("Quiet opportunities", quietLeadBody);
            StringAssert.Contains("Implication:", quietLeadBody);
            Assert.False(quietLeadBody.Contains("Quiet / Off-Market Leads"), quietLeadBody);
            Assert.False(quietLeadBody.Contains("Selected Lead / Readiness"), quietLeadBody);

            StringAssert.Contains("Town Hall: Built", civicImplicationBody);
            Assert.False(civicImplicationBody.Contains("Owner Implications"), civicImplicationBody);
            Assert.False(civicImplicationBody.Contains("Civic Effects / Pressure"), civicImplicationBody);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"{fieldName} should exist for finance presentation coverage.");
            field.SetValue(target, value);
        }

        private static string InvokeString(string methodName, params object[] args)
        {
            object result = Invoke(methodName, args);
            return result as string;
        }

        private static int InvokeInt(string methodName, params object[] args)
        {
            object result = Invoke(methodName, args);
            return result is int value ? value : int.MinValue;
        }

        private static object Invoke(string methodName, params object[] args)
        {
            MethodInfo method = typeof(GeneralStorePanelController).GetMethod(methodName, PrivateStatic);
            Assert.NotNull(method, $"{methodName} should exist for finance presentation coverage.");
            return method.Invoke(null, args);
        }

        private static void AssertParsedMoney(string value, int expectedCents)
        {
            MethodInfo method = typeof(GeneralStorePanelController).GetMethod("TryParseMoneyInputToCents", PrivateStatic);
            Assert.NotNull(method, "TryParseMoneyInputToCents should exist for exact transfer entry.");
            object[] args = { value, 0 };

            bool parsed = (bool)method.Invoke(null, args);

            Assert.IsTrue(parsed, value);
            Assert.AreEqual(expectedCents, args[1], value);
        }

        private static void AssertInvalidMoney(string value)
        {
            MethodInfo method = typeof(GeneralStorePanelController).GetMethod("TryParseMoneyInputToCents", PrivateStatic);
            Assert.NotNull(method, "TryParseMoneyInputToCents should exist for exact transfer entry.");
            object[] args = { value, 0 };

            bool parsed = (bool)method.Invoke(null, args);

            Assert.IsFalse(parsed, value);
        }

        [Test]
        public void FormalProcessLedgerSummary_CombinesWealthLiquidityAndCommitmentRead()
        {
            UnityEngine.GameObject go = new("Portfolio Process Ledger Test");
            try
            {
                var portfolio = go.AddComponent<PlayerPortfolioManager>();
                SetField(portfolio, "ownerCashCents", 85000);
                SetField(portfolio, "initialized", true);
                SetField(portfolio, "lastBusinessCashCents", 150000);
                SetField(portfolio, "lastAssetValueCents", 60000);
                SetField(portfolio, "lastDebtLiabilityCents", 10000);
                SetField(portfolio, "lastNetWorthCents", 1000000);

                string summary = portfolio.BuildFormalProcessLedgerSummary(120000, 20000, 5000);

                StringAssert.Contains("Net Worth", summary);
                StringAssert.Contains("Liquidity", summary);
                StringAssert.Contains("Commitment Read", summary);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PortfolioHelpers_ReportRunwayPressureAndCommitmentClimate()
        {
            UnityEngine.GameObject go = new("Portfolio Runway Climate Test");
            try
            {
                var portfolio = go.AddComponent<PlayerPortfolioManager>();
                SetField(portfolio, "ownerCashCents", 500000);
                SetField(portfolio, "initialized", true);

                string runway = portfolio.BuildRunwayPressureSummary(12000, 2, 1);
                StringAssert.Contains("Runway Read", runway);

                string climate = portfolio.BuildOwnerCommitmentClimateSummary(240000, 40000, 12000, 2, 1);
                StringAssert.Contains("Owner Read", climate);
                StringAssert.Contains("Runway Read", climate);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }


        [Test]
        public void OwnerExecutionLoadSummary_ReflectsProcessPressure()
        {
            UnityEngine.GameObject go = new("Portfolio Execution Load Test");
            try
            {
                var portfolio = go.AddComponent<LandLedgers.Economy.PlayerPortfolioManager>();
                SetField(portfolio, "ownerCashCents", 180000);
                SetField(portfolio, "initialized", true);
                SetField(portfolio, "lastBusinessCashCents", 250000);
                SetField(portfolio, "lastAssetValueCents", 450000);
                SetField(portfolio, "lastDebtLiabilityCents", 50000);
                SetField(portfolio, "lastNetWorthCents", 830000);

                string execution = portfolio.BuildExecutionLoadSummary(4, 2);
                string climate = portfolio.BuildOwnerExecutionClimateSummary(240000, 60000, 12000, 4, 2);

                StringAssert.Contains("Execution Load", execution);
                StringAssert.Contains("Execution Load", climate);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
