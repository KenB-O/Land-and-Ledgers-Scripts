using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Reputation;
using LandLedgers.MVP;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    public sealed class CrossSystemInterplayRuntimeTests
    {
        [Test]
        public void HouseholdAffinityCanPullRepeatBuyerToFamiliarSharedSeller()
        {
            GameObject runtimeObject = new("Affinity Shared Seller Runtime Test");
            try
            {
                SharedBusinessRuntimeManager manager = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
                BusinessProfileDefinition butcherProfile = LoadProfile(BusinessType.Butcher);
                BusinessInstanceState alternative = CreateBusiness(BusinessType.Butcher, "aaa_alternative_butcher", butcherProfile);
                BusinessInstanceState familiar = CreateBusiness(BusinessType.Butcher, "zzz_familiar_butcher", butcherProfile);
                ActivateAllWorkers(alternative.RuntimeState);
                ActivateAllWorkers(familiar.RuntimeState);
                SetBusinesses(manager, new List<BusinessInstanceState> { alternative, familiar });

                SetStock(alternative, "meat", 0);
                SetStock(familiar, "meat", 4);
                int budget = 100000;
                int firstSold = manager.TrySellHouseholdReserveUnits("meat", new[] { "meat" }, 1, ref budget, 42, out _);
                Assert.AreEqual(1, firstSold);
                Assert.AreEqual(3, familiar.RuntimeState.GetCategoryStock("meat").CurrentStockUnits);

                SetStock(alternative, "meat", 4);
                SetStock(familiar, "meat", 4);
                budget = 100000;
                int alternativeBefore = alternative.RuntimeState.GetCategoryStock("meat").CurrentStockUnits;
                int familiarBefore = familiar.RuntimeState.GetCategoryStock("meat").CurrentStockUnits;

                int repeatSold = manager.TrySellHouseholdReserveUnits("meat", new[] { "meat" }, 1, ref budget, 42, out _);

                Assert.AreEqual(1, repeatSold);
                Assert.AreEqual(alternativeBefore, alternative.RuntimeState.GetCategoryStock("meat").CurrentStockUnits);
                Assert.AreEqual(familiarBefore - 1, familiar.RuntimeState.GetCategoryStock("meat").CurrentStockUnits);
            }
            finally
            {
                Object.DestroyImmediate(runtimeObject);
            }
        }

        [Test]
        public void HouseholdAffinityDoesNotOverrideOutOfStockFamiliarSeller()
        {
            GameObject runtimeObject = new("Affinity Stock Gate Runtime Test");
            try
            {
                SharedBusinessRuntimeManager manager = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
                BusinessProfileDefinition butcherProfile = LoadProfile(BusinessType.Butcher);
                BusinessInstanceState alternative = CreateBusiness(BusinessType.Butcher, "aaa_stocked_butcher", butcherProfile);
                BusinessInstanceState familiar = CreateBusiness(BusinessType.Butcher, "zzz_empty_familiar_butcher", butcherProfile);
                ActivateAllWorkers(alternative.RuntimeState);
                ActivateAllWorkers(familiar.RuntimeState);
                SetBusinesses(manager, new List<BusinessInstanceState> { alternative, familiar });

                SetStock(alternative, "meat", 0);
                SetStock(familiar, "meat", 2);
                int budget = 100000;
                manager.TrySellHouseholdReserveUnits("meat", new[] { "meat" }, 1, ref budget, 77, out _);

                SetStock(alternative, "meat", 3);
                SetStock(familiar, "meat", 0);
                budget = 100000;
                int sold = manager.TrySellHouseholdReserveUnits("meat", new[] { "meat" }, 1, ref budget, 77, out _);

                Assert.AreEqual(1, sold);
                Assert.AreEqual(2, alternative.RuntimeState.GetCategoryStock("meat").CurrentStockUnits);
                Assert.AreEqual(0, familiar.RuntimeState.GetCategoryStock("meat").CurrentStockUnits);
            }
            finally
            {
                Object.DestroyImmediate(runtimeObject);
            }
        }

        [Test]
        public void HouseholdAffinityTrustUsesLocalCategoryReputationNotBroadHeadline()
        {
            GameObject runtimeObject = new("Affinity Local Trust Runtime Test");
            try
            {
                SharedBusinessRuntimeManager manager = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
                BusinessProfileDefinition butcherProfile = LoadProfile(BusinessType.Butcher);
                BusinessInstanceState seller = CreateBusiness(BusinessType.Butcher, "local_trust_butcher", butcherProfile);
                ActivateAllWorkers(seller.RuntimeState);
                SetBusinesses(manager, new List<BusinessInstanceState> { seller });

                seller.BusinessReputation.stockReliability01 = 0.88f;
                seller.BusinessReputation.valueFairness01 = 0.88f;
                seller.BusinessReputation.serviceExperience01 = 0.84f;
                seller.BusinessReputation.conditionPresentationTrust01 = 0.86f;
                seller.BusinessReputation.productTradeConfidence01 = 0.85f;
                seller.BusinessReputation.RecordStockout("meat", 10, 1, 1f);
                seller.BusinessReputation.RecordStockout("meat", 11, 1, 1f);
                seller.BusinessReputation.RecordStockout("meat", 12, 1, 1f);
                SetStock(seller, "meat", 4);

                int budget = 100000;
                int sold = manager.TrySellHouseholdReserveUnits("meat", new[] { "meat" }, 1, ref budget, 19, out _);

                Assert.AreEqual(1, sold);
                Dictionary<string, HouseholdBusinessAffinityState> affinities =
                    GetPrivateField<Dictionary<string, HouseholdBusinessAffinityState>>(manager, "householdAffinitiesByKey");
                Assert.NotNull(affinities);
                Assert.IsTrue(affinities.TryGetValue("19:local_trust_butcher", out HouseholdBusinessAffinityState affinity));
                Assert.NotNull(affinity);
                Assert.Less(affinity.trustQualityMemory01, 0.62f);
            }
            finally
            {
                Object.DestroyImmediate(runtimeObject);
            }
        }

        [Test]
        public void SupplierTrustImprovesRecurringOrderTerms()
        {
            LocalRecurringOrderManager manager = new();
            LocalRecurringOrderTemplate template = RanchToButcherTemplate();
            BusinessInstanceState ranch = CreateBusiness(BusinessType.Ranch, "trusted_ranch", LoadProfile(BusinessType.Ranch));
            BusinessInstanceState butcher = CreateBusiness(BusinessType.Butcher, "trusted_butcher", LoadProfile(BusinessType.Butcher));
            SetStock(ranch, "livestock_inputs", 8);
            SetStock(butcher, "livestock_inputs", butcher.RuntimeState.GetCategoryStock("livestock_inputs").TargetStockUnits);

            manager.ResolveOrder(CreateRecurringRequest(template, ranch, butcher, 1, 100, 0));
            LocalRecurringOrderRelationshipState relationship = manager.Relationships[0];
            relationship.SupplierRelationship.trust01 = 0.95f;
            relationship.SupplierRelationship.reliability01 = 0.95f;
            relationship.SupplierRelationship.paymentHistory01 = 0.95f;
            relationship.SupplierRelationship.preferred = true;

            SetStock(ranch, "livestock_inputs", 8);
            SetStock(butcher, "livestock_inputs", 0);
            SetCash(butcher, 20000);
            LocalRecurringOrderFulfillmentResult result = manager.ResolveOrder(CreateRecurringRequest(template, ranch, butcher, 2, 100, 0));

            Assert.AreEqual(LocalRecurringOrderFulfillmentStatus.Clean, result.Status);
            Assert.AreEqual(4, result.FulfilledUnits);
            Assert.AreEqual(368, result.PaidCents);
        }

        [Test]
        public void LowSupplierReliabilityCapsRecurringOrderFillEvenWithStock()
        {
            LocalRecurringOrderManager manager = new();
            LocalRecurringOrderTemplate template = RanchToButcherTemplate();
            BusinessInstanceState ranch = CreateBusiness(BusinessType.Ranch, "weak_ranch", LoadProfile(BusinessType.Ranch));
            BusinessInstanceState butcher = CreateBusiness(BusinessType.Butcher, "weak_butcher", LoadProfile(BusinessType.Butcher));
            SetStock(ranch, "livestock_inputs", 8);
            SetStock(butcher, "livestock_inputs", butcher.RuntimeState.GetCategoryStock("livestock_inputs").TargetStockUnits);

            manager.ResolveOrder(CreateRecurringRequest(template, ranch, butcher, 1, 100, 0));
            LocalRecurringOrderRelationshipState relationship = manager.Relationships[0];
            relationship.SupplierRelationship.trust01 = 0.05f;
            relationship.SupplierRelationship.reliability01 = 0.05f;
            relationship.SupplierRelationship.paymentHistory01 = 0.05f;
            relationship.SupplierRelationship.emergencyOrderStrain01 = 0.5f;

            SetStock(ranch, "livestock_inputs", 8);
            SetStock(butcher, "livestock_inputs", 0);
            SetCash(butcher, 20000);
            LocalRecurringOrderFulfillmentResult result = manager.ResolveOrder(CreateRecurringRequest(template, ranch, butcher, 2, 100, 0));

            Assert.AreEqual(LocalRecurringOrderFulfillmentStatus.Short, result.Status);
            Assert.Greater(result.FulfilledUnits, 0);
            Assert.Less(result.FulfilledUnits, result.RequestedUnits);
            StringAssert.Contains("supplier trust", result.Reason);
        }

        [Test]
        public void TrustedSupplierDiscountCanKeepOrderInsideBuyerReserve()
        {
            LocalRecurringOrderManager manager = new();
            LocalRecurringOrderTemplate template = RanchToButcherTemplate();
            BusinessInstanceState ranch = CreateBusiness(BusinessType.Ranch, "reserve_ranch", LoadProfile(BusinessType.Ranch));
            BusinessInstanceState butcher = CreateBusiness(BusinessType.Butcher, "reserve_butcher", LoadProfile(BusinessType.Butcher));
            SetStock(ranch, "livestock_inputs", 8);
            SetStock(butcher, "livestock_inputs", butcher.RuntimeState.GetCategoryStock("livestock_inputs").TargetStockUnits);

            manager.ResolveOrder(CreateRecurringRequest(template, ranch, butcher, 1, 100, 0));
            LocalRecurringOrderRelationshipState relationship = manager.Relationships[0];
            relationship.SupplierRelationship.trust01 = 0.95f;
            relationship.SupplierRelationship.reliability01 = 0.95f;
            relationship.SupplierRelationship.paymentHistory01 = 0.95f;
            relationship.SupplierRelationship.preferred = true;

            SetStock(ranch, "livestock_inputs", 8);
            SetStock(butcher, "livestock_inputs", 0);
            SetCash(butcher, 370);
            LocalRecurringOrderFulfillmentResult result = manager.ResolveOrder(CreateRecurringRequest(template, ranch, butcher, 2, 100, 0));

            Assert.AreEqual(LocalRecurringOrderFulfillmentStatus.Clean, result.Status);
            Assert.AreEqual(368, result.PaidCents);
        }

        [Test]
        public void TownPulseUnmetHardwareDemandSpillsIntoBlacksmith()
        {
            GameObject storeObject = new("Town Pulse Store Spillover Test");
            GameObject sharedObject = new("Town Pulse Shared Spillover Test");
            GameObject pulseObject = new("Town Pulse Runtime Spillover Test");
            try
            {
                GeneralStoreRuntimeManager storeRuntime = storeObject.AddComponent<GeneralStoreRuntimeManager>();
                SharedBusinessRuntimeManager sharedRuntime = sharedObject.AddComponent<SharedBusinessRuntimeManager>();
                TownPulseRuntimeManager pulseRuntime = pulseObject.AddComponent<TownPulseRuntimeManager>();
                GeneralStoreBusinessDefinition storeDefinition = LoadGeneralStoreDefinition();
                BusinessInstanceState store = BusinessInstanceState.Create("pulse_store", storeDefinition, 0, BusinessOwnerIdentity.Player());
                BusinessInstanceState blacksmith = CreateBusiness(BusinessType.Blacksmith, "pulse_blacksmith", LoadProfile(BusinessType.Blacksmith));
                ActivateAllWorkers(store.RuntimeState);
                ActivateAllWorkers(blacksmith.RuntimeState);
                SetStock(store, "tools_hardware", 1);
                SetStock(blacksmith, "tools_hardware", 10);
                SetBusinesses(sharedRuntime, new List<BusinessInstanceState> { blacksmith });

                SetPrivateField(sharedRuntime, "generalStoreRuntime", storeRuntime);
                SetPrivateField(storeRuntime, "storeDefinition", storeDefinition);
                SetPrivateField(storeRuntime, "currentBusiness", store);
                SetPrivateField(storeRuntime, "runtimeState", store.RuntimeState);
                SetPrivateField(storeRuntime, "sharedBusinessRuntime", sharedRuntime);
                SetPrivateField(storeRuntime, "townPulseRuntime", pulseRuntime);

                InvokeResolveTownPulseDemand(storeRuntime, 4);

                Assert.AreEqual(3, pulseRuntime.RuntimeState.lastDailyFulfilledUnits);
                Assert.AreEqual(0, store.RuntimeState.GetCategoryStock("tools_hardware").CurrentStockUnits);
                Assert.AreEqual(8, blacksmith.RuntimeState.GetCategoryStock("tools_hardware").CurrentStockUnits);
            }
            finally
            {
                Object.DestroyImmediate(pulseObject);
                Object.DestroyImmediate(sharedObject);
                Object.DestroyImmediate(storeObject);
            }
        }

        [Test]
        public void TownPulseMeatDemandCanSpillIntoButcher()
        {
            GameObject storeObject = new("Town Pulse Store Meat Spillover Test");
            GameObject sharedObject = new("Town Pulse Shared Meat Spillover Test");
            GameObject pulseObject = new("Town Pulse Runtime Meat Spillover Test");
            try
            {
                GeneralStoreRuntimeManager storeRuntime = storeObject.AddComponent<GeneralStoreRuntimeManager>();
                SharedBusinessRuntimeManager sharedRuntime = sharedObject.AddComponent<SharedBusinessRuntimeManager>();
                TownPulseRuntimeManager pulseRuntime = pulseObject.AddComponent<TownPulseRuntimeManager>();
                GeneralStoreBusinessDefinition storeDefinition = LoadGeneralStoreDefinition();
                BusinessInstanceState store = BusinessInstanceState.Create("pulse_meat_store", storeDefinition, 0, BusinessOwnerIdentity.Player());
                BusinessInstanceState butcher = CreateBusiness(BusinessType.Butcher, "pulse_butcher", LoadProfile(BusinessType.Butcher));
                ActivateAllWorkers(store.RuntimeState);
                ActivateAllWorkers(butcher.RuntimeState);
                SetStock(store, "meat", 0);
                SetStock(butcher, "meat", 6);
                SetBusinesses(sharedRuntime, new List<BusinessInstanceState> { butcher });
                SetPrivateField(
                    pulseRuntime,
                    "pulseDefinitions",
                    new[]
                    {
                        new TownPulseDefinition(
                            "boarding_house_bulk_order",
                            "Boarding-House Bulk Order",
                            "Meat demand",
                            1,
                            new TownPulseDemandLine("meat", "Meat", 2))
                    });

                SetPrivateField(sharedRuntime, "generalStoreRuntime", storeRuntime);
                SetPrivateField(storeRuntime, "storeDefinition", storeDefinition);
                SetPrivateField(storeRuntime, "currentBusiness", store);
                SetPrivateField(storeRuntime, "runtimeState", store.RuntimeState);
                SetPrivateField(storeRuntime, "sharedBusinessRuntime", sharedRuntime);
                SetPrivateField(storeRuntime, "townPulseRuntime", pulseRuntime);

                InvokeResolveTownPulseDemand(storeRuntime, 4);

                Assert.AreEqual(2, pulseRuntime.RuntimeState.lastDailyFulfilledUnits);
                Assert.AreEqual(4, butcher.RuntimeState.GetCategoryStock("meat").CurrentStockUnits);
            }
            finally
            {
                Object.DestroyImmediate(pulseObject);
                Object.DestroyImmediate(sharedObject);
                Object.DestroyImmediate(storeObject);
            }
        }

        private static LocalRecurringOrderTemplate RanchToButcherTemplate()
        {
            return new LocalRecurringOrderTemplate(
                "test_ranch_to_butcher_interplay",
                BusinessType.Ranch,
                "livestock_inputs",
                BusinessType.Butcher,
                "livestock_inputs",
                4);
        }

        private static LocalRecurringOrderFulfillmentRequest CreateRecurringRequest(
            LocalRecurringOrderTemplate template,
            BusinessInstanceState seller,
            BusinessInstanceState buyer,
            int weekKey,
            int unitPriceCents,
            int buyerReserveCents)
        {
            return new LocalRecurringOrderFulfillmentRequest
            {
                Template = template,
                Seller = seller,
                Buyer = buyer,
                WeekKey = weekKey,
                UnitPriceCents = unitPriceCents,
                BuyerCashReserveCents = buyerReserveCents,
                SellerCanFulfill = true,
                BuyerCanReceive = true
            };
        }

        private static BusinessInstanceState CreateBusiness(BusinessType businessType, string instanceId, BusinessProfileDefinition profile)
        {
            return BusinessInstanceState.Create(instanceId, profile, 0, BusinessOwnerIdentity.Npc(1, $"{businessType} Owner", $"{businessType}"));
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
                slot.Assign($"interplay_worker_{i}", $"Interplay Worker {i}", slot.WeeklyWageCents);
                slot.MarkPaidActive();
            }
        }

        private static void SetStock(BusinessInstanceState business, string categoryId, int units)
        {
            CategoryStockState stock = business.RuntimeState.GetCategoryStock(categoryId);
            Assert.NotNull(stock, $"{business.BusinessType} should have {categoryId} stock.");
            stock.SetCurrentStockForTests(units);
        }

        private static void SetCash(BusinessInstanceState business, int cashCents)
        {
            int target = Mathf.Max(0, cashCents);
            int current = business.RuntimeState.CurrentCashCents;
            if (current > target)
            {
                business.RuntimeState.SpendCents(current - target);
                return;
            }

            business.RuntimeState.AddCashCents(target - current);
        }

        private static void SetBusinesses(SharedBusinessRuntimeManager manager, List<BusinessInstanceState> businesses)
        {
            SetPrivateField(manager, "businesses", businesses);
        }

        private static void InvokeResolveTownPulseDemand(GeneralStoreRuntimeManager manager, int dayIndex)
        {
            MethodInfo method = typeof(GeneralStoreRuntimeManager).GetMethod(
                "ResolveTownPulseDemand",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method.Invoke(manager, new object[] { dayIndex });
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, $"{target.GetType().Name}.{fieldName} should exist.");
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, $"{target.GetType().Name}.{fieldName} should exist.");
            return (T)field.GetValue(target);
        }
    }
}
