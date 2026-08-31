using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.MVP;
using LandLedgers.Persistence;
using LandLedgers.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class TownPulseFoundationTests
    {
        [Test]
        public void PulseRotationStartsAfterOpeningWindowAndRotatesWeekly()
        {
            GameObject pulseObject = new("Town Pulse Rotation Test");
            try
            {
                TownPulseRuntimeManager runtime = pulseObject.AddComponent<TownPulseRuntimeManager>();

                Assert.AreEqual(0, runtime.BeginDailyResolution(3).Count);
                Assert.IsFalse(runtime.HasActivePulse);

                IReadOnlyList<TownPulseDemandLine> firstPulse = runtime.BeginDailyResolution(4);

                Assert.AreEqual("repair_rush", runtime.ActivePulseId);
                Assert.AreEqual(1, firstPulse.Count);
                Assert.AreEqual("tools_hardware", firstPulse[0].CategoryId);
                Assert.AreEqual(3, firstPulse[0].UnitsPerDay);

                runtime.CompleteDailyResolution(4);
                runtime.BeginDailyResolution(5);
                runtime.CompleteDailyResolution(5);
                runtime.BeginDailyResolution(6);
                runtime.CompleteDailyResolution(6);

                Assert.AreEqual(0, runtime.BeginDailyResolution(7).Count);
                Assert.IsFalse(runtime.HasActivePulse);

                IReadOnlyList<TownPulseDemandLine> secondPulse = runtime.BeginDailyResolution(11);

                Assert.AreEqual("boarding_house_bulk_order", runtime.ActivePulseId);
                Assert.AreEqual(2, secondPulse.Count);
            }
            finally
            {
                Object.DestroyImmediate(pulseObject);
            }
        }

        [Test]
        public void GeneralStorePulseDemandIsCappedByStock()
        {
            GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
            GameObject storeObject = new("Town Pulse Store Fulfillment Test");
            try
            {
                GeneralStoreRuntimeManager manager = storeObject.AddComponent<GeneralStoreRuntimeManager>();
                BusinessInstanceState business = BusinessInstanceState.Create(
                    "pulse_store_test",
                    definition,
                    0,
                    BusinessOwnerIdentity.Player());

                ActivateAllWorkers(business.RuntimeState);
                CategoryStockState stock = business.RuntimeState.GetCategoryStock("tools_hardware");
                Assert.NotNull(stock);
                stock.SetCurrentStockForTests(1);
                business.RuntimeState.BeginDailySalesCadence();
                int cashBefore = business.RuntimeState.CurrentCashCents;

                SetPrivateField(manager, "storeDefinition", definition);
                SetPrivateField(manager, "currentBusiness", business);
                SetPrivateField(manager, "runtimeState", business.RuntimeState);

                int fulfilledUnits = manager.ResolveTownPulseCategoryDemand("tools_hardware", 3);

                Assert.AreEqual(1, fulfilledUnits);
                Assert.AreEqual(0, stock.CurrentStockUnits);
                Assert.Greater(business.RuntimeState.CurrentCashCents, cashBefore);
                Assert.AreEqual(1, manager.LastDailyUnitsSold);
            }
            finally
            {
                Object.DestroyImmediate(storeObject);
            }
        }

        [Test]
        public void PulseSaveLoadPreservesActiveState()
        {
            GameObject sourceObject = new("Town Pulse Save Source Test");
            GameObject targetObject = new("Town Pulse Save Target Test");
            try
            {
                TownPulseRuntimeManager source = sourceObject.AddComponent<TownPulseRuntimeManager>();
                source.BeginDailyResolution(4);
                source.RecordDailyFulfillment("tools_hardware", 3, 2);
                source.CompleteDailyResolution(4);

                TownPulseSaveDto dto = source.CaptureSaveDto();
                TownPulseRuntimeManager target = targetObject.AddComponent<TownPulseRuntimeManager>();
                target.LoadFromSaveDto(dto);

                Assert.IsTrue(target.HasActivePulse);
                Assert.AreEqual("repair_rush", target.ActivePulseId);
                Assert.AreEqual(3, target.RuntimeState.lastDailyRequestedUnits);
                Assert.AreEqual(2, target.RuntimeState.lastDailyFulfilledUnits);
                StringAssert.Contains("2/3", target.CurrentAlertText);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void HudAlertSetterShowsAndClearsTownPulseText()
        {
            GameObject hudObject = new("Town Pulse HUD Test");
            GameObject stripObject = new("HUD_AlertStrip", typeof(RectTransform));
            try
            {
                stripObject.transform.SetParent(hudObject.transform, false);
                LandLedgersHUDController hud = hudObject.AddComponent<LandLedgersHUDController>();
                SetPrivateField(hud, "alertStrip", stripObject.GetComponent<RectTransform>());

                hud.SetTownPulseAlert("Town Pulse: Repair Rush - Tools / Hardware demand for 3 days | today 0/3");

                Assert.IsTrue(hud.IsTownPulseAlertVisible);
                StringAssert.Contains("Repair Rush", hud.CurrentTownPulseAlertText);

                hud.SetTownPulseAlert(string.Empty);

                Assert.IsFalse(hud.IsTownPulseAlertVisible);
                Assert.AreEqual(string.Empty, hud.CurrentTownPulseAlertText);
            }
            finally
            {
                Object.DestroyImmediate(stripObject);
                Object.DestroyImmediate(hudObject);
            }
        }

        private static GeneralStoreBusinessDefinition LoadGeneralStoreDefinition()
        {
            GeneralStoreBusinessDefinition definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(
                "Assets/Core/Economy/GeneralStoreBusinessDefinition.asset");
            Assert.NotNull(definition);
            return definition;
        }

        private static void ActivateAllWorkers(BusinessRuntimeState runtime)
        {
            Assert.NotNull(runtime);
            for (int i = 0; i < runtime.WorkerSlots.Count; i++)
            {
                WorkerSlotState slot = runtime.WorkerSlots[i];
                slot.Assign($"pulse_worker_{i}", $"Pulse Worker {i}", slot.WeeklyWageCents);
                slot.MarkPaidActive();
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, $"{target.GetType().Name}.{fieldName} should exist.");
            field.SetValue(target, value);
        }
    }
}
