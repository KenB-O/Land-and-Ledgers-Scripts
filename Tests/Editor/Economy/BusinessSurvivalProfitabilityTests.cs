using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.FirstLedger;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.Economy
{
    public sealed class BusinessSurvivalProfitabilityTests
    {
        [Test]
        public void SurvivalReserveIncludesBaseTwoPayrollsAndFullWeeklyReserve()
        {
            Assert.AreEqual(
                16500,
                PlayerPortfolioManager.CalculateSurvivalReserveCents(
                    protectedReserveCents: 7500,
                    filledWeeklyPayrollCents: 1800,
                    weeklyOperatingReserveCents: 5400));
        }

        [Test]
        public void PlayerOwnedStaffingUsesOwnerFallbackThenRequiredBaselineThenOptionalCapacity()
        {
            List<Object> cleanup = new();
            try
            {
                SharedBusinessRuntimeManager manager = CreateSharedRuntime(cleanup);
                BusinessInstanceState business = CreateBusiness(BusinessType.Blacksmith, BusinessOwnerIdentity.Player(), 22000);

                Assert.AreEqual(0.45f, business.OperatingEfficiency01, 0.0001f);
                Assert.AreEqual(0.45f, manager.GetHealthAdjustedOperatingEfficiency01(business), 0.0001f);

                WorkerSlotState required = FindRequiredSlot(business);
                required.Assign("smith", "Smith", required.WeeklyWageCents);
                required.MarkPaidActive();

                Assert.AreEqual(0.8f, business.OperatingEfficiency01, 0.0001f);
                Assert.AreEqual(0.8f, manager.GetHealthAdjustedOperatingEfficiency01(business), 0.0001f);

                WorkerSlotState optional = FindOptionalSlot(business);
                optional.Assign("helper", "Helper", optional.WeeklyWageCents);
                optional.MarkPaidActive();

                Assert.AreEqual(1f, business.OperatingEfficiency01, 0.0001f);
                Assert.AreEqual(1f, manager.GetHealthAdjustedOperatingEfficiency01(business), 0.0001f);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void GeneralStoreFallbackEfficiencyPreservesOneUnitSaleRequest()
        {
            GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
            GameObject gameObject = new("GeneralStoreFallbackSaleTest");
            try
            {
                GeneralStoreRuntimeManager manager = gameObject.AddComponent<GeneralStoreRuntimeManager>();
                BusinessInstanceState business = BusinessInstanceState.Create(
                    "test_general_store_fallback_sale",
                    definition,
                    1,
                    BusinessOwnerIdentity.Player());
                SetStoreRuntime(manager, business);

                MethodInfo method = typeof(GeneralStoreRuntimeManager).GetMethod(
                    "ScaleUnitsByOperatingEfficiency",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(method);

                Assert.AreEqual(1, method.Invoke(manager, new object[] { 1 }));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void OwnerDistributionGraceReserveAndTwentyFivePercentGrowth()
        {
            List<Object> cleanup = new();
            try
            {
                PlayerPortfolioManager portfolio = CreatePortfolio(0, cleanup);
                BusinessInstanceState business = CreateBusiness(BusinessType.Blacksmith, BusinessOwnerIdentity.Player(), 20000);
                int protectedReserve = SharedBusinessRuntimeManager.CalculateSharedOperatingCashReserveCents(business);
                int weeklyReserve = business.RuntimeState.LastWeeklyReorderBudgetCents;

                portfolio.RegisterCurrentBusinessCashCheckpoint(business, 0, true);
                SetBusinessCash(business, 40000);

                Assert.AreEqual(0, portfolio.ResolveOwnerDistribution(business, protectedReserve, 1, "Blacksmith", weeklyReserve));
                Assert.AreEqual(0, portfolio.ResolveOwnerDistribution(business, protectedReserve, 2, "Blacksmith", weeklyReserve));
                Assert.AreEqual(0, portfolio.ResolveOwnerDistribution(business, protectedReserve, 3, "Blacksmith", weeklyReserve));
                Assert.AreEqual(0, portfolio.OwnerCashCents);

                business.RuntimeState.AddCashCents(8000);
                int distributed = portfolio.ResolveOwnerDistribution(business, protectedReserve, 4, "Blacksmith", weeklyReserve);

                Assert.AreEqual(2000, distributed);
                Assert.AreEqual(2000, portfolio.OwnerCashCents);
                StringAssert.Contains("kept", portfolio.LastWeeklyDistributionSummary);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void ManualWithdrawalCanBreachSharedBusinessSurvivalReserveWithWarning()
        {
            List<Object> cleanup = new();
            try
            {
                PlayerPortfolioManager portfolio = CreatePortfolio(0, cleanup);
                BusinessInstanceState business = CreateBusiness(BusinessType.Blacksmith, BusinessOwnerIdentity.Player(), 30000);
                int survivalReserve = SharedBusinessRuntimeManager.CalculateSharedSurvivalCashReserveCents(business);
                SetBusinessCash(business, survivalReserve + 1000);

                bool withdrew = portfolio.TryTransferOwnerBusinessCash(business, -2000, survivalReserve, out string message);
                Assert.IsTrue(withdrew, message);
                Assert.AreEqual(survivalReserve - 1000, business.RuntimeState.CurrentCashCents);
                Assert.AreEqual(2000, portfolio.OwnerCashCents);
                StringAssert.Contains("Reserve shortfall", message);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void GeneralStoreProtectedReserveIsFullSurvivalReserveForManualWithdrawal()
        {
            GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
            List<Object> cleanup = new();
            try
            {
                PlayerPortfolioManager portfolio = CreatePortfolio(0, cleanup);
                GameObject gameObject = new("GeneralStoreSurvivalReserveTest");
                cleanup.Add(gameObject);
                GeneralStoreRuntimeManager manager = gameObject.AddComponent<GeneralStoreRuntimeManager>();
                BusinessInstanceState business = BusinessInstanceState.Create(
                    "test_general_store_survival_reserve",
                    definition,
                    1,
                    BusinessOwnerIdentity.Player());
                SetStoreRuntime(manager, business);

                int reserve = manager.ProtectedBusinessCashReserveCents;
                SetBusinessCash(business, reserve + 1000);
                int baseReserve = ManagerPolicyEffects.CalculateCashReserveCents(
                    15000,
                    business.ControlState,
                    business.ManagerPolicy);
                int reorderReserve = ManagerPolicyEffects.CalculateReorderBudgetCents(
                    definition.Business.Economy.WeeklyReorderReserveCents,
                    business.ControlState,
                    business.ManagerPolicy);
                int expected = PlayerPortfolioManager.CalculateSurvivalReserveCents(
                    baseReserve,
                    business.RuntimeState.FilledWeeklyPayrollCents,
                    reorderReserve);
                Assert.AreEqual(expected, reserve);

                bool withdrew = portfolio.TryTransferOwnerBusinessCash(business, -1100, reserve, out string message);
                Assert.IsTrue(withdrew, message);
                StringAssert.Contains("Reserve shortfall", message);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        private static SharedBusinessRuntimeManager CreateSharedRuntime(List<Object> cleanup)
        {
            GameObject gameObject = new("Shared Business Survival Test");
            cleanup.Add(gameObject);
            return gameObject.AddComponent<SharedBusinessRuntimeManager>();
        }

        private static PlayerPortfolioManager CreatePortfolio(int ownerCashCents, List<Object> cleanup)
        {
            GameObject gameObject = new("Business Survival Portfolio Test");
            cleanup.Add(gameObject);
            PlayerPortfolioManager portfolio = gameObject.AddComponent<PlayerPortfolioManager>();
            portfolio.LoadFromSaveDto(new LandLedgers.Persistence.PlayerPortfolioSaveDto
            {
                initialized = true,
                ownerCashCents = ownerCashCents
            });
            return portfolio;
        }

        private static BusinessInstanceState CreateBusiness(BusinessType type, BusinessOwnerIdentity owner, int cashCents)
        {
            BusinessInstanceState business = BusinessInstanceState.Create(
                $"test_{type}_survival",
                LoadProfile(type),
                12,
                owner);
            SetBusinessCash(business, cashCents);
            return business;
        }

        private static GeneralStoreBusinessDefinition LoadGeneralStoreDefinition()
        {
            GeneralStoreBusinessDefinition definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(
                "Assets/Core/Economy/GeneralStoreBusinessDefinition.asset");
            Assert.NotNull(definition);
            return definition;
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

        private static WorkerSlotState FindRequiredSlot(BusinessInstanceState business)
        {
            for (int i = 0; i < business.RuntimeState.WorkerSlots.Count; i++)
            {
                WorkerSlotState slot = business.RuntimeState.WorkerSlots[i];
                if (slot != null && slot.RequiredForOpening)
                {
                    return slot;
                }
            }

            Assert.Fail("Expected a required worker slot.");
            return null;
        }

        private static WorkerSlotState FindOptionalSlot(BusinessInstanceState business)
        {
            for (int i = 0; i < business.RuntimeState.WorkerSlots.Count; i++)
            {
                WorkerSlotState slot = business.RuntimeState.WorkerSlots[i];
                if (slot != null && !slot.RequiredForOpening)
                {
                    return slot;
                }
            }

            Assert.Fail("Expected an optional worker slot.");
            return null;
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

        private static void SetStoreRuntime(GeneralStoreRuntimeManager manager, BusinessInstanceState business)
        {
            FieldInfo businessField = typeof(GeneralStoreRuntimeManager).GetField(
                "currentBusiness",
                BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo runtimeField = typeof(GeneralStoreRuntimeManager).GetField(
                "runtimeState",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(businessField);
            Assert.NotNull(runtimeField);
            businessField.SetValue(manager, business);
            runtimeField.SetValue(manager, business.RuntimeState);
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
