using System.Collections.Generic;
using System.Reflection;
using System.Text;
using LandLedgers.Economy;
using LandLedgers.Population;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Review 2 Pass C (scripts): Unity-only tests for the weekly
    /// converter guard added in this pass. NOT run by the Roslyn
    /// harness (SharedBusinessRuntimeManager is a MonoBehaviour and
    /// profiles load via Resources), and NOT claimed as passing
    /// here - Codex/Unity AI must run them in the Unity Test
    /// Runner. Pattern follows ProfessionWaveRuntimeTests
    /// (GameObject + AddComponent, reflection into the private
    /// ResolveBusinessOperation, CreateStaffedBusiness shape).
    ///
    /// What the guard proves, when run: a staffed General Store
    /// whose profile now has an empty inputCategoryIds (Pass B
    /// removed the dangling freight_goods label) must NOT gain
    /// staple_food stock from the weekly converter. Before Pass C
    /// the converter would have credited planned throughput units
    /// of the first output category (staple_food) with no input
    /// consumed and no cash spent - free stock. The store's real
    /// restock path is unaffected by this test: per-category
    /// pending reorders, costed and delivered by off-map inbound
    /// shipment in GeneralStoreRuntimeManager, plus local-supply
    /// transfers. Service businesses are unaffected by the guard
    /// (the Service path returns before it), and no existing
    /// ProfessionWave test invokes the converter for a General
    /// Store, so no existing expectation relies on minting.
    /// </summary>
    public sealed class Review2PassCWeeklyConverterTests
    {
        [Test]
        public void WeeklyConverterMintsNothingForGeneralStoreWithEmptyInputs()
        {
            GameObject runtimeObject = new("Pass C Weekly Converter Test");
            try
            {
                SharedBusinessRuntimeManager manager = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
                BusinessInstanceState store = CreateStaffedBusiness(BusinessType.GeneralStore, "test_general_store", 21);
                CategoryStockState stapleFood = store.RuntimeState.GetCategoryStock("staple_food");
                Assert.IsNotNull(stapleFood, "General Store runtime must carry a staple_food category.");
                stapleFood.SetCurrentStockForTests(5);
                int stockBefore = stapleFood.CurrentStockUnits;

                InvokeBusinessOperation(manager, store);

                Assert.AreEqual(stockBefore, stapleFood.CurrentStockUnits, "Weekly conversion must not mint staple_food from an empty input list.");
                Assert.AreEqual(0, store.LastWeeklyThroughputUnits, "A refused conversion produces no throughput units.");
                StringAssert.Contains("no input category", store.LastWeeklyBlockedReason);
            }
            finally
            {
                Object.DestroyImmediate(runtimeObject);
            }
        }

        private static BusinessInstanceState CreateStaffedBusiness(BusinessType businessType, string instanceId, int buildingId)
        {
            Dictionary<BusinessType, BusinessProfileDefinition> profiles = LoadProfilesByType();
            Assert.IsTrue(profiles.TryGetValue(businessType, out BusinessProfileDefinition profile), $"{businessType} profile missing.");
            BusinessInstanceState business = BusinessInstanceState.Create(
                instanceId,
                profile,
                buildingId,
                BusinessOwnerIdentity.Npc(2000 + buildingId, $"{businessType} Owner", $"{businessType}"));
            business.EnsureOwnerOperatorStaffing(business.Owner);
            return business;
        }

        private static Dictionary<BusinessType, BusinessProfileDefinition> LoadProfilesByType()
        {
            BusinessProfileDefinition[] profiles = Resources.LoadAll<BusinessProfileDefinition>("Core/Economy/BusinessProfiles");
            var byType = new Dictionary<BusinessType, BusinessProfileDefinition>();
            for (int i = 0; i < profiles.Length; i++)
            {
                if (profiles[i] != null)
                {
                    byType[profiles[i].Business.BusinessType] = profiles[i];
                }
            }

            return byType;
        }

        private static void InvokeBusinessOperation(SharedBusinessRuntimeManager manager, BusinessInstanceState business)
        {
            MethodInfo method = typeof(SharedBusinessRuntimeManager).GetMethod(
                "ResolveBusinessOperation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(manager, new object[] { business, new StringBuilder("Weekly shared operations: ") });
        }
    }
}
