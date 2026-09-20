using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.MVP;
using LandLedgers.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace LandLedgers.EditorTests.UI
{
    public sealed class LandLedgersHudFinanceReadoutTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        [Test]
        public void WealthReadoutShowsNetWorthAndOwnerCash()
        {
            GameObject hudObject = null;
            try
            {
                LandLedgersHUDController hud = CreateHud(out hudObject);

                hud.SetWealthCents(1750000, 250000);

                Assert.AreEqual("Net Worth $17,500 | Cash $2,500", hud.CurrentMoneyReadoutText);
            }
            finally
            {
                Object.DestroyImmediate(hudObject);
            }
        }

        [Test]
        public void WealthReadoutDoesNotTreatBusinessCashAsSpendableCash()
        {
            GameObject hudObject = null;
            try
            {
                LandLedgersHUDController hud = CreateHud(out hudObject);

                hud.SetWealthCents(1750000, 250000);

                StringAssert.Contains("Net Worth $17,500", hud.CurrentMoneyReadoutText);
                StringAssert.Contains("Cash $2,500", hud.CurrentMoneyReadoutText);
                Assert.IsFalse(hud.CurrentMoneyReadoutText.Contains("Cash $17,500"));
            }
            finally
            {
                Object.DestroyImmediate(hudObject);
            }
        }

        [Test]
        public void WealthSnapshotIncludesOwnedBusinessCashButNotAsLiquidCash()
        {
            BusinessPortfolioSummary storeSummary = new(
                "store",
                "General Store",
                BusinessType.GeneralStore,
                "Player",
                businessCashCents: 500000,
                operatingReserveCents: 600000,
                survivalReserveCents: 600000,
                transferableCashCents: 0,
                businessLiabilityCents: 0,
                equityContributionCents: 0,
                BusinessContinuityStatus.CashBelowSurvivalReserve,
                "below reserve",
                default);

            PlayerWealthSnapshot snapshot = PlayerPortfolioManager.CalculateWealthSnapshot(
                ownerCashCents: 250000,
                businessSummaries: new[] { storeSummary },
                assetValueCents: 100000,
                debtLiabilityCents: 50000,
                winTargetCents: 0);

            Assert.AreEqual(250000, snapshot.LiquidCashCents);
            Assert.AreEqual(500000, snapshot.BusinessCashCents);
            Assert.AreEqual(800000, snapshot.NetWorthCents);
        }

        [Test]
        public void GeneralStoreCashFallbackDoesNotOverwriteOwnerCashHudReadout()
        {
            GameObject hudObject = null;
            GameObject storeObject = null;
            try
            {
                LandLedgersHUDController hud = CreateHud(out hudObject);
                hud.SetWealthCents(1750000, 250000);

                storeObject = new GameObject("General Store HUD Cash Authority Test");
                GeneralStoreRuntimeManager store = storeObject.AddComponent<GeneralStoreRuntimeManager>();
                BusinessRuntimeState runtimeState = new();
                runtimeState.AddCashCents(999900);

                SetPrivateField(store, "hudController", hud);
                SetPrivateField(store, "runtimeState", runtimeState);
                SetPrivateField(store, "playerPortfolioManager", null);

                InvokePrivate(store, "PushCashToHud");

                Assert.AreEqual("Net Worth $17,500 | Cash $2,500", hud.CurrentMoneyReadoutText);
            }
            finally
            {
                Object.DestroyImmediate(storeObject);
                Object.DestroyImmediate(hudObject);
            }
        }

        [Test]
        public void WealthSnapshotDebtReducesNetWorthWithoutReducingLiquidCash()
        {
            BusinessPortfolioSummary storeSummary = new(
                "store",
                "General Store",
                BusinessType.GeneralStore,
                "Player",
                businessCashCents: 500000,
                operatingReserveCents: 600000,
                survivalReserveCents: 600000,
                transferableCashCents: 0,
                businessLiabilityCents: 0,
                equityContributionCents: 0,
                BusinessContinuityStatus.CashBelowSurvivalReserve,
                "below reserve",
                default);

            PlayerWealthSnapshot noDebt = PlayerPortfolioManager.CalculateWealthSnapshot(
                ownerCashCents: 250000,
                businessSummaries: new[] { storeSummary },
                assetValueCents: 100000,
                debtLiabilityCents: 0,
                winTargetCents: 0);
            PlayerWealthSnapshot withDebt = PlayerPortfolioManager.CalculateWealthSnapshot(
                ownerCashCents: 250000,
                businessSummaries: new[] { storeSummary },
                assetValueCents: 100000,
                debtLiabilityCents: 50000,
                winTargetCents: 0);

            Assert.AreEqual(noDebt.LiquidCashCents, withDebt.LiquidCashCents);
            Assert.AreEqual(noDebt.BusinessCashCents, withDebt.BusinessCashCents);
            Assert.AreEqual(noDebt.NetWorthCents - 50000, withDebt.NetWorthCents);
        }

        private static LandLedgersHUDController CreateHud(out GameObject hudObject)
        {
            hudObject = new GameObject("HUD Finance Readout Test");
            GameObject cashObject = new("HUD_CashText", typeof(RectTransform), typeof(TextMeshProUGUI));
            cashObject.transform.SetParent(hudObject.transform, false);

            LandLedgersHUDController hud = hudObject.AddComponent<LandLedgersHUDController>();
            SetPrivateField(hud, "cashText", cashObject.GetComponent<TMP_Text>());
            return hud;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
            Assert.NotNull(field, $"{target.GetType().Name}.{fieldName} should exist.");
            field.SetValue(target, value);
        }

        private static object InvokePrivate(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, PrivateInstance);
            Assert.NotNull(method, $"{target.GetType().Name}.{methodName} should exist.");
            return method.Invoke(target, null);
        }
    }
}
