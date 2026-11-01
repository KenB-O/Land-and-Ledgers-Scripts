using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.FirstLedger;
using LandLedgers.Persistence;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    public sealed class LocalRecurringOrderManagerTests
    {
        [Test]
        public void CleanRanchToButcherOrderMovesStockCashAndRecordsRelationship()
        {
            LocalRecurringOrderManager manager = new();
            LocalRecurringOrderTemplate template = RanchToButcherTemplate();
            BusinessInstanceState ranch = CreateBusiness(BusinessType.Ranch, "test_ranch_clean");
            BusinessInstanceState butcher = CreateBusiness(BusinessType.Butcher, "test_butcher_clean");
            SetStock(ranch, "livestock_inputs", 8);
            SetStock(butcher, "livestock_inputs", 0);
            SetCash(ranch, 10000);
            SetCash(butcher, 20000);

            LocalRecurringOrderFulfillmentResult result = manager.ResolveOrder(CreateRequest(template, ranch, butcher, 1, 100, 2500));

            Assert.AreEqual(LocalRecurringOrderFulfillmentStatus.Clean, result.Status);
            Assert.AreEqual(4, result.RequestedUnits);
            Assert.AreEqual(4, result.FulfilledUnits);
            Assert.AreEqual(400, result.PaidCents);
            Assert.AreEqual(4, ranch.RuntimeState.GetCategoryStock("livestock_inputs").CurrentStockUnits);
            Assert.AreEqual(4, butcher.RuntimeState.GetCategoryStock("livestock_inputs").CurrentStockUnits);
            Assert.AreEqual(10400, ranch.RuntimeState.CurrentCashCents);
            Assert.AreEqual(19600, butcher.RuntimeState.CurrentCashCents);

            Assert.AreEqual(1, manager.Relationships.Count);
            LocalRecurringOrderRelationshipState relationship = manager.Relationships[0];
            Assert.AreEqual(1, relationship.CleanFulfillmentCount);
            Assert.AreEqual(1, relationship.ConsecutiveCleanFulfillmentWeeks);
            Assert.AreEqual(0, relationship.ConsecutiveBreachWeeks);
            Assert.Greater(relationship.SupplierRelationship.reliability01, 0.5f);
            Assert.Greater(relationship.SupplierRelationship.paymentHistory01, 0.5f);
        }

        [Test]
        public void ShortFulfillmentRecordsBreachAndReducesSupplierReliability()
        {
            LocalRecurringOrderManager manager = new();
            LocalRecurringOrderTemplate template = RanchToButcherTemplate();
            BusinessInstanceState ranch = CreateBusiness(BusinessType.Ranch, "test_ranch_short");
            BusinessInstanceState butcher = CreateBusiness(BusinessType.Butcher, "test_butcher_short");
            SetStock(ranch, "livestock_inputs", 1);
            SetStock(butcher, "livestock_inputs", 0);
            SetCash(butcher, 20000);

            LocalRecurringOrderFulfillmentResult result = manager.ResolveOrder(CreateRequest(template, ranch, butcher, 1, 100, 2500));

            Assert.AreEqual(LocalRecurringOrderFulfillmentStatus.Short, result.Status);
            Assert.AreEqual(4, result.RequestedUnits);
            Assert.AreEqual(1, result.FulfilledUnits);
            LocalRecurringOrderRelationshipState relationship = manager.Relationships[0];
            Assert.AreEqual(1, relationship.ShortFulfillmentCount);
            Assert.AreEqual(1, relationship.ConsecutiveBreachWeeks);
            StringAssert.Contains("seller short", relationship.LastBreachReason);
            Assert.Less(relationship.SupplierRelationship.reliability01, 0.5f);
            Assert.Less(relationship.SupplierRelationship.trust01, 0.5f);
        }

        [Test]
        public void BuyerCashLimitationRecordsBuyerBreachWithoutSellerReliabilityPenalty()
        {
            LocalRecurringOrderManager manager = new();
            LocalRecurringOrderTemplate template = RanchToButcherTemplate();
            BusinessInstanceState ranch = CreateBusiness(BusinessType.Ranch, "test_ranch_cash_breach");
            BusinessInstanceState butcher = CreateBusiness(BusinessType.Butcher, "test_butcher_cash_breach");
            SetStock(ranch, "livestock_inputs", 8);
            SetStock(butcher, "livestock_inputs", 0);
            SetCash(butcher, 2500);

            LocalRecurringOrderFulfillmentResult result = manager.ResolveOrder(CreateRequest(template, ranch, butcher, 1, 100, 2500));

            Assert.AreEqual(LocalRecurringOrderFulfillmentStatus.BuyerCashBreach, result.Status);
            Assert.AreEqual(0, result.FulfilledUnits);
            LocalRecurringOrderRelationshipState relationship = manager.Relationships[0];
            Assert.AreEqual(1, relationship.BuyerCashBreachCount);
            Assert.AreEqual(0, relationship.ShortFulfillmentCount);
            Assert.AreEqual(0, relationship.FailedFulfillmentCount);
            Assert.AreEqual(0.5f, relationship.SupplierRelationship.reliability01, 0.0001f);
            Assert.Less(relationship.SupplierRelationship.paymentHistory01, 0.5f);
        }

        [Test]
        public void FullBuyerStockSkipsWithoutBreach()
        {
            LocalRecurringOrderManager manager = new();
            LocalRecurringOrderTemplate template = RanchToButcherTemplate();
            BusinessInstanceState ranch = CreateBusiness(BusinessType.Ranch, "test_ranch_skip");
            BusinessInstanceState butcher = CreateBusiness(BusinessType.Butcher, "test_butcher_skip");
            CategoryStockState buyerStock = butcher.RuntimeState.GetCategoryStock("livestock_inputs");
            SetStock(ranch, "livestock_inputs", 8);
            SetStock(butcher, "livestock_inputs", buyerStock.TargetStockUnits);
            int ranchCashBefore = ranch.RuntimeState.CurrentCashCents;
            int butcherCashBefore = butcher.RuntimeState.CurrentCashCents;

            LocalRecurringOrderFulfillmentResult result = manager.ResolveOrder(CreateRequest(template, ranch, butcher, 1, 100, 2500));

            Assert.AreEqual(LocalRecurringOrderFulfillmentStatus.SkippedNoNeed, result.Status);
            Assert.AreEqual(0, result.RequestedUnits);
            Assert.AreEqual(0, result.FulfilledUnits);
            LocalRecurringOrderRelationshipState relationship = manager.Relationships[0];
            Assert.AreEqual(0, relationship.CleanFulfillmentCount);
            Assert.AreEqual(0, relationship.ShortFulfillmentCount);
            Assert.AreEqual(0, relationship.FailedFulfillmentCount);
            Assert.AreEqual(0, relationship.BuyerCashBreachCount);
            Assert.AreEqual(ranchCashBefore, ranch.RuntimeState.CurrentCashCents);
            Assert.AreEqual(butcherCashBefore, butcher.RuntimeState.CurrentCashCents);
        }

        [Test]
        public void RepeatedWeeklyResolutionPreservesRelationshipAndIncrementsCleanStreak()
        {
            LocalRecurringOrderManager manager = new();
            LocalRecurringOrderTemplate template = RanchToButcherTemplate();
            BusinessInstanceState ranch = CreateBusiness(BusinessType.Ranch, "test_ranch_repeat");
            BusinessInstanceState butcher = CreateBusiness(BusinessType.Butcher, "test_butcher_repeat");
            SetStock(ranch, "livestock_inputs", 8);
            SetStock(butcher, "livestock_inputs", 0);
            SetCash(butcher, 20000);

            manager.ResolveOrder(CreateRequest(template, ranch, butcher, 1, 100, 2500));
            manager.ResolveOrder(CreateRequest(template, ranch, butcher, 2, 100, 2500));

            Assert.AreEqual(1, manager.Relationships.Count);
            LocalRecurringOrderRelationshipState relationship = manager.Relationships[0];
            Assert.AreEqual(2, relationship.CleanFulfillmentCount);
            Assert.AreEqual(2, relationship.ConsecutiveCleanFulfillmentWeeks);
            Assert.AreEqual(2, relationship.LastResolvedWeekKey);
            Assert.AreEqual(8, butcher.RuntimeState.GetCategoryStock("livestock_inputs").CurrentStockUnits);
        }

        [Test]
        public void SaveLoadRestoresRecurringRelationshipAndContinuesCleanStreak()
        {
            LocalRecurringOrderManager manager = new();
            LocalRecurringOrderTemplate template = RanchToButcherTemplate();
            BusinessInstanceState ranch = CreateBusiness(BusinessType.Ranch, "test_ranch_persist");
            BusinessInstanceState butcher = CreateBusiness(BusinessType.Butcher, "test_butcher_persist");
            SetStock(ranch, "livestock_inputs", 8);
            SetStock(butcher, "livestock_inputs", 0);
            SetCash(ranch, 10000);
            SetCash(butcher, 20000);

            manager.BeginWeeklyResolution();
            manager.ResolveOrder(CreateRequest(template, ranch, butcher, 1, 100, 2500));
            manager.CompleteWeeklyResolution();

            List<LocalRecurringOrderRelationshipSaveDto> relationshipDtos = manager.CaptureSaveDtos();
            Assert.AreEqual(1, relationshipDtos.Count);
            Assert.AreEqual(1, relationshipDtos[0].cleanFulfillmentCount);
            StringAssert.Contains("Recurring local orders:", manager.LastWeeklySummary);

            LocalRecurringOrderManager restored = new();
            restored.LoadFromSaveDtos(relationshipDtos, manager.LastWeeklySummary);

            Assert.AreEqual(1, restored.Relationships.Count);
            LocalRecurringOrderRelationshipState restoredRelationship = restored.Relationships[0];
            Assert.AreEqual(1, restoredRelationship.CleanFulfillmentCount);
            Assert.AreEqual(1, restoredRelationship.ConsecutiveCleanFulfillmentWeeks);
            Assert.AreEqual(1, restoredRelationship.LastResolvedWeekKey);
            Assert.Greater(restoredRelationship.SupplierRelationship.reliability01, 0.5f);
            Assert.AreEqual(manager.LastWeeklySummary, restored.LastWeeklySummary);

            SetStock(butcher, "livestock_inputs", 0);
            restored.ResolveOrder(CreateRequest(template, ranch, butcher, 2, 100, 2500));

            Assert.AreEqual(1, restored.Relationships.Count);
            Assert.AreEqual(2, restored.Relationships[0].CleanFulfillmentCount);
            Assert.AreEqual(2, restored.Relationships[0].ConsecutiveCleanFulfillmentWeeks);
            Assert.AreEqual(2, restored.Relationships[0].LastResolvedWeekKey);
        }

        [Test]
        public void CleanStreakRenewsRecurringRelationship()
        {
            LocalRecurringOrderManager manager = new();
            LocalRecurringOrderTemplate template = RanchToButcherTemplate();
            BusinessInstanceState ranch = CreateBusiness(BusinessType.Ranch, "test_ranch_renew");
            BusinessInstanceState butcher = CreateBusiness(BusinessType.Butcher, "test_butcher_renew");
            SetCash(butcher, 50000);

            for (int week = 1; week <= 4; week++)
            {
                SetStock(ranch, "livestock_inputs", 8);
                SetStock(butcher, "livestock_inputs", 0);
                manager.ResolveOrder(CreateRequest(template, ranch, butcher, week, 100, 2500));
            }

            Assert.AreEqual(1, manager.Relationships.Count);
            Assert.AreEqual(1, manager.Relationships[0].RenewalCount);
            Assert.AreEqual(LocalRecurringOrderFulfillmentStatus.Renewed, manager.Relationships[0].LastStatus);
            Assert.Greater(manager.Relationships[0].RelationshipHealth01, 0.5f);
        }

        [Test]
        public void RepeatedBreachesCancelRecurringRelationship()
        {
            LocalRecurringOrderManager manager = new();
            LocalRecurringOrderTemplate template = RanchToButcherTemplate();
            BusinessInstanceState ranch = CreateBusiness(BusinessType.Ranch, "test_ranch_cancel");
            BusinessInstanceState butcher = CreateBusiness(BusinessType.Butcher, "test_butcher_cancel");
            SetCash(butcher, 50000);

            for (int week = 1; week <= 3; week++)
            {
                SetStock(ranch, "livestock_inputs", 0);
                SetStock(butcher, "livestock_inputs", 0);
                manager.ResolveOrder(CreateRequest(template, ranch, butcher, week, 100, 2500));
            }

            Assert.AreEqual(1, manager.Relationships.Count);
            Assert.IsFalse(manager.Relationships[0].Active);
            Assert.AreEqual(LocalRecurringOrderFulfillmentStatus.Cancelled, manager.Relationships[0].LastStatus);
            StringAssert.Contains("seller stock unavailable", manager.Relationships[0].CancellationReason);
        }

        [Test]
        public void GeneralStoreBuyerOrderUsesReceiveLocalSupplySummary()
        {
            List<Object> cleanup = new();
            try
            {
                LocalRecurringOrderManager manager = new();
                LocalRecurringOrderTemplate template = new(
                    "test_crop_to_store",
                    BusinessType.CropFarm,
                    "crop_food",
                    BusinessType.GeneralStore,
                    "staple_food",
                    10);
                BusinessInstanceState cropFarm = CreateBusiness(BusinessType.CropFarm, "test_crop_store_supplier");
                BusinessInstanceState storeBusiness = CreateBusiness(BusinessType.GeneralStore, "test_general_store_buyer", BusinessOwnerIdentity.Player());
                SetStock(cropFarm, "crop_food", 20);
                SetStock(storeBusiness, "staple_food", 0);
                SetCash(storeBusiness, 20000);

                GameObject storeObject = new("General Store Local Order Test");
                cleanup.Add(storeObject);
                GeneralStoreRuntimeManager storeRuntime = storeObject.AddComponent<GeneralStoreRuntimeManager>();
                ConfigureStoreRuntimeForReceiveLocalSupply(storeRuntime, storeBusiness);

                LocalRecurringOrderFulfillmentResult result = manager.ResolveOrder(new LocalRecurringOrderFulfillmentRequest
                {
                    Template = template,
                    Seller = cropFarm,
                    Buyer = storeBusiness,
                    WeekKey = 3,
                    UnitPriceCents = 25,
                    BuyerCashReserveCents = 0,
                    SellerCanFulfill = true,
                    BuyerCanReceive = true,
                    ReceiveBuyerSupply = storeRuntime.ReceiveLocalSupply
                });

                Assert.AreEqual(LocalRecurringOrderFulfillmentStatus.Clean, result.Status);
                Assert.AreEqual(10, result.FulfilledUnits);
                Assert.AreEqual(10, storeRuntime.LastWeeklyLocalSupplyUnitsReceived);
                Assert.AreEqual(250, storeRuntime.LastWeeklyLocalSupplySpendCents);
                StringAssert.Contains(cropFarm.RuntimeDisplayName, storeRuntime.LastWeeklyLocalSupplySummary);
                Assert.AreEqual(1, manager.Relationships[0].CleanFulfillmentCount);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void GeneralStoreSellerOrderUsesGenericBusinessTransfer()
        {
            LocalRecurringOrderManager manager = new();
            LocalRecurringOrderTemplate template = new(
                "test_store_to_saloon",
                BusinessType.GeneralStore,
                "staple_food",
                BusinessType.Saloon,
                "meal_inputs",
                4);
            BusinessInstanceState store = CreateBusiness(BusinessType.GeneralStore, "test_general_store_seller", BusinessOwnerIdentity.Player());
            BusinessInstanceState saloon = CreateBusiness(BusinessType.Saloon, "test_saloon_buyer");
            SetStock(store, "staple_food", 10);
            SetStock(saloon, "meal_inputs", 0);
            SetCash(store, 10000);
            SetCash(saloon, 20000);

            LocalRecurringOrderFulfillmentResult result = manager.ResolveOrder(CreateRequest(template, store, saloon, 4, 48, 0));

            Assert.AreEqual(LocalRecurringOrderFulfillmentStatus.Clean, result.Status);
            Assert.AreEqual(4, result.FulfilledUnits);
            Assert.AreEqual(6, store.RuntimeState.GetCategoryStock("staple_food").CurrentStockUnits);
            Assert.AreEqual(4, saloon.RuntimeState.GetCategoryStock("meal_inputs").CurrentStockUnits);
            StringAssert.Contains("recurring order sold", store.RuntimeState.LastWeeklyTransferSummary);
            StringAssert.Contains("recurring order bought", saloon.RuntimeState.LastWeeklyTransferSummary);
            Assert.Greater(manager.Relationships[0].RelationshipHealth01, 0.5f);
        }

        private static LocalRecurringOrderTemplate RanchToButcherTemplate()
        {
            return new LocalRecurringOrderTemplate(
                "test_ranch_to_butcher",
                BusinessType.Ranch,
                "livestock_inputs",
                BusinessType.Butcher,
                "livestock_inputs",
                4);
        }

        private static LocalRecurringOrderFulfillmentRequest CreateRequest(
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

        private static BusinessInstanceState CreateBusiness(BusinessType businessType, string instanceId, BusinessOwnerIdentity owner = null)
        {
            return BusinessInstanceState.Create(instanceId, LoadProfile(businessType), 0, owner ?? BusinessOwnerIdentity.Npc(1, "Test Owner", "Test"));
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

        private static void ConfigureStoreRuntimeForReceiveLocalSupply(GeneralStoreRuntimeManager storeRuntime, BusinessInstanceState storeBusiness)
        {
            GeneralStoreBusinessDefinition definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(
                "Assets/Core/Economy/GeneralStoreBusinessDefinition.asset");
            Assert.NotNull(definition);
            SetPrivateField(storeRuntime, "storeDefinition", definition);
            SetPrivateField(storeRuntime, "currentBusiness", storeBusiness);
            SetPrivateField(storeRuntime, "runtimeState", storeBusiness.RuntimeState);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, $"{fieldName} should exist.");
            field.SetValue(target, value);
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
