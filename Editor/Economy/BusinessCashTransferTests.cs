using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Persistence;
using LandLedgers.Reporting;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    public sealed class BusinessCashTransferTests
    {
        [Test]
        public void ManualDepositMovesOwnerCashIntoBusinessCash()
        {
            List<Object> cleanup = new();
            try
            {
                PlayerPortfolioManager portfolio = CreatePortfolio(15000, cleanup);
                BusinessInstanceState business = CreateBusiness(BusinessType.GeneralStore, BusinessOwnerIdentity.Player(), 5000);

                Assert.IsTrue(portfolio.TryTransferOwnerBusinessCash(business, 1000, 2000, out string message), message);

                Assert.AreEqual(14000, portfolio.OwnerCashCents);
                Assert.AreEqual(6000, business.RuntimeState.CurrentCashCents);
                StringAssert.Contains("Deposited", business.CashTransferRule.LastTransferSummary);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void ManualWithdrawalMovesBusinessCashIntoOwnerCash()
        {
            List<Object> cleanup = new();
            try
            {
                PlayerPortfolioManager portfolio = CreatePortfolio(1000, cleanup);
                BusinessInstanceState business = CreateBusiness(BusinessType.Blacksmith, BusinessOwnerIdentity.Player(), 12000);

                Assert.IsTrue(portfolio.TryTransferOwnerBusinessCash(business, -1000, 5000, out string message), message);

                Assert.AreEqual(2000, portfolio.OwnerCashCents);
                Assert.AreEqual(11000, business.RuntimeState.CurrentCashCents);
                StringAssert.Contains("Withdrew", business.CashTransferRule.LastTransferSummary);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void OwnerCashTransfersDoNotChangeOperatingNetPerformance()
        {
            List<Object> cleanup = new();
            try
            {
                PlayerPortfolioManager portfolio = CreatePortfolio(15000, cleanup);
                BusinessInstanceState business = CreateBusiness(BusinessType.Blacksmith, BusinessOwnerIdentity.Player(), 10000);
                business.RuntimeState.ResetWeekToDateSales();
                int allTimeBefore = business.RuntimeState.AllTimeNetCents;

                Assert.IsTrue(portfolio.TryTransferOwnerBusinessCash(business, 1000, 2000, out string depositMessage), depositMessage);
                Assert.IsTrue(portfolio.TryTransferOwnerBusinessCash(business, -1500, 2000, out string withdrawalMessage), withdrawalMessage);
                portfolio.RegisterCurrentBusinessCashCheckpoint(business, 1);
                business.RuntimeState.SetCurrentCashCents(16000);
                int distributed = portfolio.ResolveOwnerDistribution(business, 2000, 3, "Blacksmith", 3000);

                Assert.Greater(distributed, 0);
                Assert.AreEqual(0, business.RuntimeState.WeekToDateNetCents);
                Assert.AreEqual(allTimeBefore, business.RuntimeState.AllTimeNetCents);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void ManualDepositBlocksWhenOwnerCashIsInsufficient()
        {
            List<Object> cleanup = new();
            try
            {
                PlayerPortfolioManager portfolio = CreatePortfolio(500, cleanup);
                BusinessInstanceState business = CreateBusiness(BusinessType.Butcher, BusinessOwnerIdentity.Player(), 8000);

                Assert.IsFalse(portfolio.TryTransferOwnerBusinessCash(business, 1000, 2000, out string message));

                Assert.AreEqual(500, portfolio.OwnerCashCents);
                Assert.AreEqual(8000, business.RuntimeState.CurrentCashCents);
                StringAssert.Contains("owner cash", message);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void ManualWithdrawalCanBreachProtectedReserveWithWarning()
        {
            List<Object> cleanup = new();
            try
            {
                PlayerPortfolioManager portfolio = CreatePortfolio(1000, cleanup);
                BusinessInstanceState business = CreateBusiness(BusinessType.CropFarm, BusinessOwnerIdentity.Player(), 7000);

                Assert.IsTrue(portfolio.TryTransferOwnerBusinessCash(business, -3000, 5000, out string message), message);

                Assert.AreEqual(4000, portfolio.OwnerCashCents);
                Assert.AreEqual(4000, business.RuntimeState.CurrentCashCents);
                StringAssert.Contains("Reserve shortfall", message);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void ManualDepositRebalancesLiquidityWithoutChangingNetWorth()
        {
            List<Object> cleanup = new();
            try
            {
                PlayerPortfolioManager portfolio = CreatePortfolio(15000, cleanup);
                BusinessInstanceState business = CreateBusiness(BusinessType.GeneralStore, BusinessOwnerIdentity.Player(), 5000);

                Assert.IsTrue(portfolio.TryTransferOwnerBusinessCash(business, 1000, 2000, out string message), message);

                BusinessPortfolioSummary summary = business.BuildPortfolioSummary(2000, 2000);
                PlayerWealthSnapshot wealth = PlayerPortfolioManager.CalculateWealthSnapshot(
                    portfolio.OwnerCashCents,
                    new[] { summary },
                    assetValueCents: 0,
                    debtLiabilityCents: 0,
                    winTargetCents: 0);

                Assert.AreEqual(14000, wealth.LiquidCashCents);
                Assert.AreEqual(6000, wealth.BusinessCashCents);
                Assert.AreEqual(20000, wealth.NetWorthCents);
                Assert.AreEqual(4000, summary.TransferableCashCents);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void ReserveWarningWithdrawalLeavesNoPositiveTransferableCashWhileNetWorthStaysWhole()
        {
            List<Object> cleanup = new();
            try
            {
                PlayerPortfolioManager portfolio = CreatePortfolio(1000, cleanup);
                BusinessInstanceState business = CreateBusiness(BusinessType.CropFarm, BusinessOwnerIdentity.Player(), 7000);

                Assert.IsTrue(portfolio.TryTransferOwnerBusinessCash(business, -3000, 5000, out string message), message);

                BusinessPortfolioSummary summary = business.BuildPortfolioSummary(5000, 5000);
                PlayerWealthSnapshot wealth = PlayerPortfolioManager.CalculateWealthSnapshot(
                    portfolio.OwnerCashCents,
                    new[] { summary },
                    assetValueCents: 0,
                    debtLiabilityCents: 0,
                    winTargetCents: 0);

                StringAssert.Contains("Reserve shortfall", message);
                Assert.AreEqual(4000, wealth.LiquidCashCents);
                Assert.AreEqual(4000, wealth.BusinessCashCents);
                Assert.AreEqual(8000, wealth.NetWorthCents);
                Assert.AreEqual(0, summary.TransferableCashCents);
                Assert.AreEqual(BusinessContinuityStatus.CashBelowSurvivalReserve, summary.ContinuityStatus);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void NonPlayerBusinessTransferBlocks()
        {
            List<Object> cleanup = new();
            try
            {
                PlayerPortfolioManager portfolio = CreatePortfolio(10000, cleanup);
                BusinessInstanceState business = CreateBusiness(BusinessType.Ranch, BusinessOwnerIdentity.Npc(7, "Morgan", "Vale"), 5000);

                Assert.IsFalse(portfolio.TryTransferOwnerBusinessCash(business, 1000, 2000, out string message));

                Assert.AreEqual(10000, portfolio.OwnerCashCents);
                Assert.AreEqual(5000, business.RuntimeState.CurrentCashCents);
                StringAssert.Contains("not player-owned", message);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void AutoLowerTopUpRespectsOwnerCashAndMinimumTransferAmount()
        {
            List<Object> cleanup = new();
            try
            {
                PlayerPortfolioManager portfolio = CreatePortfolio(4000, cleanup);
                BusinessInstanceState business = CreateBusiness(BusinessType.Blacksmith, BusinessOwnerIdentity.Player(), 3000);
                Assert.IsTrue(portfolio.TryAdjustBusinessCashLowerThreshold(business, 10000, 0, out _));
                Assert.IsTrue(portfolio.TrySetBusinessAutoTransferEnabled(business, true, 0, out _));

                Assert.IsTrue(portfolio.TryResolveAutomaticBusinessCashTransfer(
                    business,
                    0,
                    3,
                    BusinessCashAutoTransferMode.LowerOnly,
                    out int transferred,
                    out string message), message);

                Assert.AreEqual(4000, transferred);
                Assert.AreEqual(0, portfolio.OwnerCashCents);
                Assert.AreEqual(7000, business.RuntimeState.CurrentCashCents);

                PlayerPortfolioManager smallPortfolio = CreatePortfolio(50, cleanup);
                BusinessInstanceState smallBusiness = CreateBusiness(BusinessType.Butcher, BusinessOwnerIdentity.Player(), 9950);
                Assert.IsTrue(smallPortfolio.TryAdjustBusinessCashLowerThreshold(smallBusiness, 10000, 0, out _));
                Assert.IsTrue(smallPortfolio.TrySetBusinessAutoTransferEnabled(smallBusiness, true, 0, out _));

                Assert.IsFalse(smallPortfolio.TryResolveAutomaticBusinessCashTransfer(
                    smallBusiness,
                    0,
                    4,
                    BusinessCashAutoTransferMode.LowerOnly,
                    out int skippedTransfer,
                    out string skippedMessage));

                Assert.AreEqual(0, skippedTransfer);
                Assert.AreEqual(50, smallPortfolio.OwnerCashCents);
                Assert.AreEqual(9950, smallBusiness.RuntimeState.CurrentCashCents);
                StringAssert.Contains("skipped", skippedMessage);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void AutoUpperSweepPreservesProtectedReserve()
        {
            List<Object> cleanup = new();
            try
            {
                PlayerPortfolioManager portfolio = CreatePortfolio(1000, cleanup);
                BusinessInstanceState business = CreateBusiness(BusinessType.Ranch, BusinessOwnerIdentity.Player(), 18000);
                Assert.IsTrue(portfolio.TryAdjustBusinessCashUpperThreshold(business, -4900, 5000, out _));
                Assert.IsTrue(portfolio.TrySetBusinessAutoTransferEnabled(business, true, 5000, out _));

                Assert.IsTrue(portfolio.TryResolveAutomaticBusinessCashTransfer(
                    business,
                    5000,
                    5,
                    BusinessCashAutoTransferMode.UpperOnly,
                    out int transferred,
                    out string message), message);

                Assert.AreEqual(7900, transferred);
                Assert.AreEqual(8900, portfolio.OwnerCashCents);
                Assert.AreEqual(10100, business.RuntimeState.CurrentCashCents);
                Assert.GreaterOrEqual(business.RuntimeState.CurrentCashCents, 5000);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void TransferRulesPersistThroughBusinessSaveDto()
        {
            BusinessInstanceState source = CreateBusiness(BusinessType.CropFarm, BusinessOwnerIdentity.Player(), 9000);
            source.CashTransferRule.SetAutoTransferEnabled(true);
            source.CashTransferRule.AdjustLowerThresholdCents(4000, 2000);
            source.CashTransferRule.AdjustUpperThresholdCents(12000, 2000);
            source.CashTransferRule.RecordTransferSummary("Auto deposited $10.00 into test business.", 8);

            BusinessInstanceSaveDto dto = source.CaptureSaveDto();
            BusinessInstanceState restored = BusinessInstanceState.FromSaveDto(dto);

            Assert.NotNull(restored);
            Assert.IsTrue(dto.cashTransferRule.autoTransferEnabled);
            Assert.IsTrue(restored.CashTransferRule.AutoTransferEnabled);
            Assert.AreEqual(source.CashTransferRule.LowerThresholdCents, restored.CashTransferRule.LowerThresholdCents);
            Assert.AreEqual(source.CashTransferRule.UpperThresholdCents, restored.CashTransferRule.UpperThresholdCents);
            Assert.AreEqual(8, restored.CashTransferRule.LastTransferWeekKey);
            StringAssert.Contains("Auto deposited", restored.CashTransferRule.LastTransferSummary);
        }

        [Test]
        public void RequiredStaffAloneProvidesStarterBusinessEfficiency()
        {
            BusinessInstanceState business = CreateBusiness(BusinessType.Blacksmith, BusinessOwnerIdentity.Player(), 22000);
            WorkerSlotState required = business.RuntimeState.WorkerSlots[0];

            Assert.AreEqual(0.45f, business.OperatingEfficiency01, 0.0001f);

            required.Assign("smith", "Smith", required.WeeklyWageCents);
            required.MarkPaidActive();

            Assert.AreEqual(0.8f, business.OperatingEfficiency01, 0.0001f);

            WorkerSlotState optional = business.RuntimeState.WorkerSlots[1];
            optional.Assign("helper", "Helper", optional.WeeklyWageCents);
            optional.MarkPaidActive();

            Assert.AreEqual(1f, business.OperatingEfficiency01, 0.0001f);
        }

        [Test]
        public void OwnerDistributionRespectsStartupGraceAndSurvivalReserve()
        {
            List<Object> cleanup = new();
            try
            {
                PlayerPortfolioManager portfolio = CreatePortfolio(0, cleanup);
                BusinessInstanceState business = CreateBusiness(BusinessType.GeneralStore, BusinessOwnerIdentity.Player(), 20000);
                portfolio.RegisterCurrentBusinessCashCheckpoint(business, 1, true);
                business.RuntimeState.AddCashCents(10000);

                int startupDistributed = portfolio.ResolveOwnerDistribution(business, 5000, 2, "General Store", 8000);

                Assert.AreEqual(0, startupDistributed);
                Assert.AreEqual(0, portfolio.OwnerCashCents);

                business.RuntimeState.AddCashCents(10000);
                int postGraceDistributed = portfolio.ResolveOwnerDistribution(business, 5000, 5, "General Store", 8000);

                Assert.AreEqual(2500, postGraceDistributed);
                Assert.AreEqual(2500, portfolio.OwnerCashCents);

                BusinessInstanceState thinBusiness = CreateBusiness(BusinessType.Butcher, BusinessOwnerIdentity.Player(), 8000);
                portfolio.RegisterCurrentBusinessCashCheckpoint(thinBusiness, 4);
                thinBusiness.RuntimeState.AddCashCents(1000);

                int thinDistributed = portfolio.ResolveOwnerDistribution(thinBusiness, 9000, 5, "Butcher", 2000);

                Assert.AreEqual(0, thinDistributed);
                Assert.AreEqual(2500, portfolio.OwnerCashCents);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void DefaultBusinessCashTransferRulesUseSurvivalReserve()
        {
            List<Object> cleanup = new();
            try
            {
                PlayerPortfolioManager portfolio = CreatePortfolio(10000, cleanup);
                BusinessInstanceState business = CreateBusiness(BusinessType.Blacksmith, BusinessOwnerIdentity.Player(), 5000);
                WorkerSlotState required = business.RuntimeState.WorkerSlots[0];
                required.Assign("smith", "Smith", required.WeeklyWageCents);

                portfolio.ConfigureDefaultBusinessCashTransfers(business, 2500, 3500, 7);

                int expectedLower = PlayerPortfolioManager.CalculateSurvivalReserveCents(2500, required.WeeklyWageCents, 3500);
                Assert.IsTrue(business.CashTransferRule.AutoTransferEnabled);
                Assert.AreEqual(expectedLower, business.CashTransferRule.LowerThresholdCents);
                Assert.Greater(business.CashTransferRule.UpperThresholdCents, expectedLower);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void ExactBusinessCashThresholdsClampToReserveAndMinimumSweepGap()
        {
            List<Object> cleanup = new();
            try
            {
                PlayerPortfolioManager portfolio = CreatePortfolio(10000, cleanup);
                BusinessInstanceState business = CreateBusiness(BusinessType.GeneralStore, BusinessOwnerIdentity.Player(), 5000);

                Assert.IsTrue(portfolio.TrySetBusinessCashThresholds(business, 1000, 1000, 5000, out string message), message);

                Assert.AreEqual(5000, business.CashTransferRule.LowerThresholdCents);
                Assert.AreEqual(5000 + BusinessCashTransferRuleState.MinimumThresholdGapCents, business.CashTransferRule.UpperThresholdCents);
                Assert.IsTrue(business.CashTransferRule.AutoTransferEnabled);
                StringAssert.Contains("refill below", message);
                StringAssert.Contains("sweep above", message);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void DefaultBusinessCashTransferRulesStartAutoReserveOnAfterConfiguration()
        {
            List<Object> cleanup = new();
            try
            {
                PlayerPortfolioManager portfolio = CreatePortfolio(10000, cleanup);
                BusinessInstanceState business = CreateBusiness(BusinessType.GeneralStore, BusinessOwnerIdentity.Player(), 5000);

                portfolio.ConfigureDefaultBusinessCashTransfers(business, 5000, 3500, 7);

                Assert.IsTrue(business.CashTransferRule.AutoTransferEnabled);
                Assert.GreaterOrEqual(business.CashTransferRule.LowerThresholdCents, 5000);
                Assert.GreaterOrEqual(
                    business.CashTransferRule.UpperThresholdCents,
                    business.CashTransferRule.LowerThresholdCents + BusinessCashTransferRuleState.MinimumThresholdGapCents);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void BusinessReportCashBridgeIncludesSurvivalFields()
        {
            BusinessReportSnapshot snapshot = new()
            {
                businessId = "store",
                businessDisplayName = "General Store",
                openingCashCents = 30000,
                closingCashCents = 26000,
                revenueCents = 12000,
                costOfGoodsSoldCents = 4000,
                payrollExpenseCents = 1200,
                reorderExpenseCents = 3000,
                localSupplyExpenseCents = 800,
                ownerDistributionCents = 600,
                cashTransferInCents = 1000,
                cashTransferOutCents = 500,
                survivalReserveCents = 15000,
                primaryBlockedReason = "cash-limited reorder"
            };

            BusinessWeeklySummary summary = BusinessReportBuilder.BuildWeekly(snapshot);

            Assert.AreEqual(4000, summary.financial.costOfGoodsSoldCents);
            Assert.AreEqual(15000, summary.financial.survivalReserveCents);
            Assert.AreEqual("cash-limited reorder", summary.financial.primaryBlockedReason);
            StringAssert.Contains("Cash changed because", summary.financial.cashBridgeSummary);
            StringAssert.Contains("owner draw $6.00", summary.financial.cashBridgeSummary);
        }

        [Test]
        public void OldSaveDefaultsAutoTransferOffWithSafeEffectiveThresholds()
        {
            BusinessInstanceState source = CreateBusiness(BusinessType.GeneralStore, BusinessOwnerIdentity.Player(), 9000);
            BusinessInstanceSaveDto dto = source.CaptureSaveDto();
            dto.cashTransferRule = null;

            BusinessInstanceState restored = BusinessInstanceState.FromSaveDto(dto);

            Assert.NotNull(restored);
            Assert.IsFalse(restored.CashTransferRule.AutoTransferEnabled);
            Assert.AreEqual(5000, restored.CashTransferRule.GetEffectiveLowerThresholdCents(5000));
            Assert.AreEqual(15000, restored.CashTransferRule.GetEffectiveUpperThresholdCents(5000));
        }

        private static PlayerPortfolioManager CreatePortfolio(int ownerCashCents, List<Object> cleanup)
        {
            GameObject gameObject = new("Business Cash Transfer Portfolio Test");
            cleanup.Add(gameObject);
            PlayerPortfolioManager portfolio = gameObject.AddComponent<PlayerPortfolioManager>();
            portfolio.LoadFromSaveDto(new PlayerPortfolioSaveDto
            {
                initialized = true,
                ownerCashCents = ownerCashCents
            });
            return portfolio;
        }

        private static BusinessInstanceState CreateBusiness(BusinessType type, BusinessOwnerIdentity owner, int cashCents)
        {
            BusinessInstanceState business = BusinessInstanceState.Create(
                $"test_{type}_cash_transfer",
                LoadProfile(type),
                12,
                owner);
            SetBusinessCash(business, cashCents);
            return business;
        }

        private static void SetBusinessCash(BusinessInstanceState business, int cashCents)
        {
            int target = Mathf.Max(0, cashCents);
            int current = business.RuntimeState.CurrentCashCents;
            if (current > target)
            {
                business.RuntimeState.SpendCents(current - target);
            }
            else if (target > current)
            {
                business.RuntimeState.AddCashCents(target - current);
            }
        }

        private static BusinessProfileDefinition LoadProfile(BusinessType businessType)
        {
            BusinessProfileDefinition[] profiles = Resources.LoadAll<BusinessProfileDefinition>("Core/Economy/BusinessProfiles");
            for (int i = 0; i < profiles.Length; i++)
            {
                if (profiles[i] != null && profiles[i].Business.BusinessType == businessType)
                {
                    return profiles[i];
                }
            }

            Assert.Fail($"{businessType} profile should be present in Resources.");
            return null;
        }

        private static void DestroyAll(List<Object> objects)
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                {
                    Object.DestroyImmediate(objects[i]);
                }
            }
        }
    }
}
