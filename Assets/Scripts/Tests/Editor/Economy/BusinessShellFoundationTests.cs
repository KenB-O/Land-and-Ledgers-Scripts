using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.FirstLedger;
using LandLedgers.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    public sealed class BusinessShellFoundationTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void BusinessRoleCoverageSnapshotSeparatesRequiredOptionalAndOwnerFallback()
        {
            BusinessInstanceState playerBusiness = CreateBusiness(BusinessType.Blacksmith, BusinessOwnerIdentity.Player());

            BusinessRoleCoverageSnapshot coverage = playerBusiness.BuildRoleCoverageSnapshot();

            Assert.Greater(coverage.RequiredWorkerCount, 0);
            Assert.AreEqual(0, coverage.ActiveRequiredWorkerCount);
            Assert.AreEqual(BusinessRoleCoverageStatus.OwnerOperatorFallback, coverage.Status);
            Assert.AreEqual(45, coverage.EfficiencyPercent);
            StringAssert.Contains("owner-operator", coverage.BuildSummary());

            WorkerSlotState requiredSlot = FindFirstRequiredSlot(playerBusiness);
            Assert.NotNull(requiredSlot);
            requiredSlot.Assign("worker:1", "Ada Briggs", requiredSlot.WeeklyWageCents);
            coverage = playerBusiness.BuildRoleCoverageSnapshot();

            Assert.AreEqual(BusinessRoleCoverageStatus.Covered, coverage.Status);
            Assert.GreaterOrEqual(coverage.EfficiencyPercent, 80);
            StringAssert.Contains("Required", coverage.BuildSummary());
        }

        [Test]
        public void BusinessPortfolioSummaryExposesCashEquityLiabilityAndContinuityRead()
        {
            BusinessInstanceState business = CreateBusiness(BusinessType.Butcher, BusinessOwnerIdentity.Player());
            business.RuntimeState.SetCurrentCashCents(24000);
            business.RuntimeState.AccrueOperatingLiabilityCents(3000, "regional freight invoice");

            BusinessPortfolioSummary summary = business.BuildPortfolioSummary(6000, 10000);

            Assert.AreEqual(business.InstanceId, summary.InstanceId);
            Assert.AreEqual(24000, summary.BusinessCashCents);
            Assert.AreEqual(6000, summary.OperatingReserveCents);
            Assert.AreEqual(10000, summary.SurvivalReserveCents);
            Assert.AreEqual(11000, summary.EquityContributionCents);
            Assert.AreEqual(3000, summary.BusinessLiabilityCents);
            Assert.AreEqual(14000, summary.TransferableCashCents);
            Assert.AreEqual(BusinessContinuityStatus.Operating, summary.ContinuityStatus);
            StringAssert.Contains("Business Cash", summary.BuildLedgerLine());
            StringAssert.Contains("Operating Reserve", summary.BuildLedgerLine());
            StringAssert.Contains("Survival Reserve", summary.BuildLedgerLine());
            StringAssert.Contains("Coverage", summary.BuildLedgerLine());
        }

        [Test]
        public void PortfolioNetWorthCanBeCalculatedFromExplicitBusinessSummaries()
        {
            BusinessPortfolioSummary[] summaries =
            {
                new("store", "Player General Store", BusinessType.GeneralStore, "Player", 17000, 12000, 17000, 5000, 0, 12000, BusinessContinuityStatus.Operating, "operating", default),
                new("smith", "Player Blacksmith", BusinessType.Blacksmith, "Player", 9000, 6000, 9000, 3000, 2000, 1000, BusinessContinuityStatus.Pressured, "pressured", default)
            };

            PlayerWealthSnapshot wealth = PlayerPortfolioManager.CalculateWealthSnapshot(
                ownerCashCents: 25000,
                businessSummaries: summaries,
                assetValueCents: 80000,
                debtLiabilityCents: 10000,
                winTargetCents: 100000);

            Assert.AreEqual(25000, wealth.LiquidCashCents);
            Assert.AreEqual(26000, wealth.BusinessCashCents);
            Assert.AreEqual(6000, wealth.BusinessEquityCents);
            Assert.AreEqual(2000, wealth.BusinessLiabilityCents);
            Assert.AreEqual(101000, wealth.NetWorthCents);
            Assert.IsTrue(wealth.WinReached);
        }

        [Test]
        public void BusinessRuntimeAccruesLiabilitiesForMissedPayrollAndShortPaidTransferCosts()
        {
            BusinessInstanceState business = CreateBusiness(BusinessType.Blacksmith, BusinessOwnerIdentity.Player());
            WorkerSlotState requiredSlot = FindFirstRequiredSlot(business);
            Assert.NotNull(requiredSlot);
            requiredSlot.Assign("worker:1", "Ada Briggs", requiredSlot.WeeklyWageCents);

            business.RuntimeState.SetCurrentCashCents(300);
            business.RuntimeState.BeginWeeklySettlementCadence(null, 0f, false);
            business.RuntimeState.ResolveWeeklyPayroll();
            business.RuntimeState.AddWeeklyLocalTransferCost(500, "fuel and freight");

            Assert.Greater(business.RuntimeState.AccruedLiabilityCents, 0);
            Assert.Greater(business.RuntimeState.LastWeeklyAccruedLiabilityCents, 0);

            BusinessRuntimeSaveDto dto = business.RuntimeState.CaptureSaveDto();
            BusinessRuntimeState restored = BusinessRuntimeState.FromSaveDto(dto, business.BusinessType);

            Assert.AreEqual(business.RuntimeState.AccruedLiabilityCents, restored.AccruedLiabilityCents);
            Assert.AreEqual(business.RuntimeState.LastWeeklyAccruedLiabilityCents, restored.LastWeeklyAccruedLiabilityCents);
        }

        [Test]
        public void BusinessPortfolioSummaryUsesContinuityStatusForMissedPayrollAndReserveShortfall()
        {
            BusinessInstanceState business = CreateBusiness(BusinessType.Blacksmith, BusinessOwnerIdentity.Player());
            WorkerSlotState requiredSlot = FindFirstRequiredSlot(business);
            Assert.NotNull(requiredSlot);
            requiredSlot.Assign("worker:1", "Ada Briggs", requiredSlot.WeeklyWageCents);

            business.RuntimeState.SetCurrentCashCents(0);
            business.RuntimeState.BeginWeeklySettlementCadence(null, 0f, false);
            business.RuntimeState.ResolveWeeklyPayroll();

            BusinessPortfolioSummary missedPayroll = business.BuildPortfolioSummary(2000, 4000);
            Assert.AreEqual(BusinessContinuityStatus.MissedPayroll, missedPayroll.ContinuityStatus);

            business.RuntimeState.SetCurrentCashCents(1000);
            business.RuntimeState.BeginWeeklySettlementCadence(null, 0f, false);
            BusinessPortfolioSummary belowReserve = business.BuildPortfolioSummary(2000, 4000);
            Assert.AreEqual(BusinessContinuityStatus.CashBelowSurvivalReserve, belowReserve.ContinuityStatus);
        }

        [Test]
        public void OwnedBusinessPortfolioSummariesUseStoreAndSharedReserveAuthorities()
        {
            GameObject root = new("Business Summary Authority Test");
            try
            {
                GeneralStoreRuntimeManager storeRuntime = root.AddComponent<GeneralStoreRuntimeManager>();
                SharedBusinessRuntimeManager sharedRuntime = root.AddComponent<SharedBusinessRuntimeManager>();

                BusinessInstanceState storeBusiness = CreateBusiness(BusinessType.GeneralStore, BusinessOwnerIdentity.Player());
                BusinessInstanceState blacksmith = CreateBusiness(BusinessType.Blacksmith, BusinessOwnerIdentity.Player());

                SetPrivateField(storeRuntime, "currentBusiness", storeBusiness);
                SetPrivateField(storeRuntime, "runtimeState", storeBusiness.RuntimeState);
                SetPrivateField(sharedRuntime, "businesses", new List<BusinessInstanceState> { blacksmith });

                List<BusinessPortfolioSummary> summaries = PlayerPortfolioManager.CalculateOwnedBusinessPortfolioSummaries(storeRuntime, sharedRuntime);
                Assert.AreEqual(2, summaries.Count);

                BusinessPortfolioSummary storeSummary = summaries.Find(summary => summary.BusinessType == BusinessType.GeneralStore);
                BusinessPortfolioSummary sharedSummary = summaries.Find(summary => summary.BusinessType == BusinessType.Blacksmith);

                Assert.AreEqual(storeRuntime.ProtectedBusinessCashReserveCents, storeSummary.SurvivalReserveCents);
                Assert.AreEqual(
                    SharedBusinessRuntimeManager.CalculateSharedSurvivalCashReserveCents(blacksmith),
                    sharedSummary.SurvivalReserveCents);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BusinessPortfolioSummaryClampsTransferableCashAtZeroWhenBelowSurvivalReserve()
        {
            BusinessInstanceState business = CreateBusiness(BusinessType.Butcher, BusinessOwnerIdentity.Player());
            business.RuntimeState.SetCurrentCashCents(2500);

            BusinessPortfolioSummary summary = business.BuildPortfolioSummary(2000, 4000);

            Assert.AreEqual(0, summary.TransferableCashCents);
            Assert.AreEqual(BusinessContinuityStatus.CashBelowSurvivalReserve, summary.ContinuityStatus);
            StringAssert.Contains("Survival Reserve", summary.BuildLedgerLine());
        }

        [Test]
        public void BusinessTypeMineIsAppendedWithoutChangingLegacyValues()
        {
            Assert.AreEqual(0, (int)BusinessType.GeneralStore);
            Assert.AreEqual(6, (int)BusinessType.Sawmill);
            Assert.AreEqual(17, (int)BusinessType.Wheelwright);
            Assert.AreEqual(18, (int)BusinessType.Mine);
        }

        private static BusinessInstanceState CreateBusiness(BusinessType businessType, BusinessOwnerIdentity owner)
        {
            BusinessProfileDefinition profile = LoadProfile(businessType);
            return BusinessInstanceState.Create($"test_{businessType}", profile, 0, owner);
        }

        private static WorkerSlotState FindFirstRequiredSlot(BusinessInstanceState business)
        {
            for (int i = 0; i < business.RuntimeState.WorkerSlots.Count; i++)
            {
                if (business.RuntimeState.WorkerSlots[i].RequiredForOpening)
                {
                    return business.RuntimeState.WorkerSlots[i];
                }
            }

            return null;
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

            Assert.Fail($"Missing profile for {businessType}.");
            return null;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
            Assert.NotNull(field, $"{fieldName} should exist.");
            field.SetValue(target, value);
        }
    }
}
