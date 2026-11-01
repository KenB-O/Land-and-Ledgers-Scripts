using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    public sealed class BusinessTransferAgreementTests
    {
        [Test]
        public void AgreementSaveDtoRoundTripsCoreFields()
        {
            BusinessTransferAgreementState agreement = new();
            agreement.Configure(
                "agreement:test",
                "source_a",
                "destination_b",
                "lumber",
                "lumber",
                BusinessTransferAllocationMode.PercentOfAvailable,
                60,
                BusinessCadence.Weekly,
                2,
                BusinessTransferPricingMode.CustomModifier,
                0.85f,
                4,
                LocalRecurringOrderHaulingResponsibility.Buyer,
                true,
                9);
            agreement.RecordAttemptScheduled(9, 24, 2400, "scheduled");

            BusinessTransferAgreementSaveDto dto = agreement.CaptureSaveDto();
            BusinessTransferAgreementState restored = BusinessTransferAgreementState.FromSaveDto(dto);

            Assert.AreEqual("agreement:test", restored.AgreementId);
            Assert.AreEqual("source_a", restored.SourceBusinessInstanceId);
            Assert.AreEqual("destination_b", restored.DestinationBusinessInstanceId);
            Assert.AreEqual("lumber", restored.SourceCategoryId);
            Assert.AreEqual("lumber", restored.DestinationCategoryId);
            Assert.AreEqual(BusinessTransferAllocationMode.PercentOfAvailable, restored.AllocationMode);
            Assert.AreEqual(60, restored.AllocationValue);
            Assert.AreEqual(BusinessCadence.Weekly, restored.Cadence);
            Assert.AreEqual(2, restored.CadenceInterval);
            Assert.AreEqual(BusinessTransferPricingMode.CustomModifier, restored.PricingMode);
            Assert.AreEqual(0.85f, restored.PriceModifier, 0.0001f);
            Assert.AreEqual(4, restored.SourceReserveUnits);
            Assert.AreEqual(LocalRecurringOrderHaulingResponsibility.Buyer, restored.HaulingResponsibility);
            Assert.IsTrue(restored.Active);
            Assert.AreEqual(9, restored.NextDueWeekKey);
            Assert.AreEqual(24, restored.LastScheduledUnits);
            Assert.AreEqual(2400, restored.LastScheduledTotalPriceCents);
            StringAssert.Contains("scheduled", restored.LastFulfillmentSummary);
        }

        [Test]
        public void FixedUnitOwnedAgreementSchedulesShipmentAndDefersLedgerUntilDelivery()
        {
            List<Object> cleanup = new();
            try
            {
                SharedBusinessRuntimeManager runtime = CreateRuntime(cleanup, out LogisticsRuntimeManager logistics);
                BusinessInstanceState ranch = CreateBusiness(BusinessType.Ranch, "owned_ranch", BusinessOwnerIdentity.Player());
                BusinessInstanceState butcher = CreateBusiness(BusinessType.Butcher, "owned_butcher", BusinessOwnerIdentity.Player());
                SetBusinesses(runtime, ranch, butcher);
                SetStock(ranch, "livestock_inputs", 12);
                SetStock(butcher, "livestock_inputs", 0);
                SetCash(ranch, 10000);
                SetCash(butcher, 10000);

                BusinessTransferAgreementState agreement = new();
                agreement.Configure(
                    "owned_ranch_to_butcher",
                    ranch.InstanceId,
                    butcher.InstanceId,
                    "livestock_inputs",
                    "livestock_inputs",
                    BusinessTransferAllocationMode.FixedUnits,
                    5,
                    BusinessCadence.Weekly,
                    1,
                    BusinessTransferPricingMode.Market,
                    1f,
                    2,
                    LocalRecurringOrderHaulingResponsibility.Seller,
                    true,
                    1);
                SetTransferAgreements(runtime, agreement);

                int scheduled = runtime.ResolveOwnedBusinessTransferAgreementsForTests(1);

                Assert.AreEqual(5, scheduled);
                Assert.AreEqual(7, ranch.RuntimeState.GetCategoryStock("livestock_inputs").CurrentStockUnits);
                Assert.AreEqual(0, butcher.RuntimeState.GetCategoryStock("livestock_inputs").CurrentStockUnits);
                Assert.AreEqual(0, ranch.RuntimeState.LastWeeklyLocalTransferRevenueCents);
                Assert.AreEqual(0, butcher.RuntimeState.LastWeeklyLocalTransferCostCents);
                Assert.AreEqual(1, logistics.Shipments.Count);
                Assert.AreEqual("owned_ranch_to_butcher", logistics.Shipments[0].TransferAgreementId);
                Assert.AreEqual(LogisticsSupplierClass.InternalSupplier, logistics.Shipments[0].supplierClass);

                AdvanceShipmentToCompletion(logistics);

                Assert.AreEqual(5, butcher.RuntimeState.GetCategoryStock("livestock_inputs").CurrentStockUnits);
                Assert.Greater(ranch.RuntimeState.LastWeeklyLocalTransferRevenueCents, 0);
                Assert.Greater(butcher.RuntimeState.LastWeeklyLocalTransferCostCents, 0);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void PercentOfAvailableAgreementRespectsSourceReserve()
        {
            List<Object> cleanup = new();
            try
            {
                SharedBusinessRuntimeManager runtime = CreateRuntime(cleanup, out _);
                BusinessInstanceState sawmill = CreateBusiness(BusinessType.Sawmill, "owned_sawmill", BusinessOwnerIdentity.Player());
                BusinessInstanceState lumberYard = CreateBusiness(BusinessType.LumberYard, "owned_lumber_yard", BusinessOwnerIdentity.Player());
                SetBusinesses(runtime, sawmill, lumberYard);
                SetStock(sawmill, "lumber", 20);
                SetStock(lumberYard, "lumber", 0);

                BusinessTransferAgreementState agreement = new();
                agreement.Configure(
                    "owned_sawmill_to_yard",
                    sawmill.InstanceId,
                    lumberYard.InstanceId,
                    "lumber",
                    "lumber",
                    BusinessTransferAllocationMode.PercentOfAvailable,
                    50,
                    BusinessCadence.Weekly,
                    1,
                    BusinessTransferPricingMode.Free,
                    1f,
                    6,
                    LocalRecurringOrderHaulingResponsibility.Seller,
                    true,
                    2);
                SetTransferAgreements(runtime, agreement);

                int scheduled = runtime.ResolveOwnedBusinessTransferAgreementsForTests(2);

                Assert.AreEqual(7, scheduled);
                Assert.AreEqual(13, sawmill.RuntimeState.GetCategoryStock("lumber").CurrentStockUnits);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void FreeTransferDeliversWithoutLedgerCashEffect()
        {
            List<Object> cleanup = new();
            try
            {
                SharedBusinessRuntimeManager runtime = CreateRuntime(cleanup, out LogisticsRuntimeManager logistics);
                BusinessInstanceState blacksmith = CreateBusiness(BusinessType.Blacksmith, "owned_blacksmith", BusinessOwnerIdentity.Player());
                BusinessInstanceState builder = CreateBusiness(BusinessType.Builder, "owned_builder", BusinessOwnerIdentity.Player());
                SetBusinesses(runtime, blacksmith, builder);
                SetStock(blacksmith, "tools_hardware", 6);
                SetStock(builder, "tools_hardware", 0);
                SetCash(blacksmith, 7000);
                SetCash(builder, 8000);

                BusinessTransferAgreementState agreement = new();
                agreement.Configure(
                    "owned_blacksmith_to_builder",
                    blacksmith.InstanceId,
                    builder.InstanceId,
                    "tools_hardware",
                    "tools_hardware",
                    BusinessTransferAllocationMode.FixedUnits,
                    3,
                    BusinessCadence.Weekly,
                    1,
                    BusinessTransferPricingMode.Free,
                    1f,
                    0,
                    LocalRecurringOrderHaulingResponsibility.Shared,
                    true,
                    3);
                SetTransferAgreements(runtime, agreement);

                runtime.ResolveOwnedBusinessTransferAgreementsForTests(3);
                AdvanceShipmentToCompletion(logistics);

                Assert.AreEqual(3, builder.RuntimeState.GetCategoryStock("tools_hardware").CurrentStockUnits);
                Assert.AreEqual(7000, blacksmith.RuntimeState.CurrentCashCents);
                Assert.AreEqual(8000, builder.RuntimeState.CurrentCashCents);
                Assert.AreEqual(0, blacksmith.RuntimeState.LastWeeklyLocalTransferRevenueCents);
                Assert.AreEqual(0, builder.RuntimeState.LastWeeklyLocalTransferCostCents);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void AgreementSkipsWhenDestinationCategoryIsFull()
        {
            List<Object> cleanup = new();
            try
            {
                SharedBusinessRuntimeManager runtime = CreateRuntime(cleanup, out LogisticsRuntimeManager logistics);
                BusinessInstanceState ranch = CreateBusiness(BusinessType.Ranch, "owned_ranch_full", BusinessOwnerIdentity.Player());
                BusinessInstanceState butcher = CreateBusiness(BusinessType.Butcher, "owned_butcher_full", BusinessOwnerIdentity.Player());
                SetBusinesses(runtime, ranch, butcher);
                SetStock(ranch, "livestock_inputs", 10);
                CategoryStockState destinationStock = butcher.RuntimeState.GetCategoryStock("livestock_inputs");
                SetStock(butcher, "livestock_inputs", destinationStock.TargetStockUnits);

                BusinessTransferAgreementState agreement = new();
                agreement.Configure(
                    "full_destination",
                    ranch.InstanceId,
                    butcher.InstanceId,
                    "livestock_inputs",
                    "livestock_inputs",
                    BusinessTransferAllocationMode.FixedUnits,
                    4,
                    BusinessCadence.Weekly,
                    1,
                    BusinessTransferPricingMode.Market,
                    1f,
                    0,
                    LocalRecurringOrderHaulingResponsibility.Seller,
                    true,
                    1);
                SetTransferAgreements(runtime, agreement);

                int scheduled = runtime.ResolveOwnedBusinessTransferAgreementsForTests(1);

                Assert.AreEqual(0, scheduled);
                Assert.AreEqual(0, logistics.Shipments.Count);
                StringAssert.Contains("full", agreement.LastFulfillmentSummary.ToLowerInvariant());
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        private static SharedBusinessRuntimeManager CreateRuntime(List<Object> cleanup, out LogisticsRuntimeManager logistics)
        {
            GameObject sharedObject = new("Business Transfer Runtime Test");
            GameObject logisticsObject = new("Business Transfer Logistics Test");
            cleanup.Add(sharedObject);
            cleanup.Add(logisticsObject);

            SharedBusinessRuntimeManager runtime = sharedObject.AddComponent<SharedBusinessRuntimeManager>();
            logistics = logisticsObject.AddComponent<LogisticsRuntimeManager>();
            SetPrivateField(runtime, "logisticsRuntime", logistics);
            SetPrivateField(logistics, "sharedBusinessRuntime", runtime);
            return runtime;
        }

        private static void AdvanceShipmentToCompletion(LogisticsRuntimeManager logistics)
        {
            MethodInfo method = typeof(LogisticsRuntimeManager).GetMethod(
                "AdvanceShipments",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            while (logistics.Shipments.Count > 0
                && logistics.Shipments[0].Status != LogisticsShipmentStatus.Completed
                && logistics.Shipments[0].Status != LogisticsShipmentStatus.Partial
                && logistics.Shipments[0].Status != LogisticsShipmentStatus.Failed)
            {
                method.Invoke(logistics, new object[] { 86400f });
            }
        }

        private static BusinessInstanceState CreateBusiness(BusinessType businessType, string instanceId, BusinessOwnerIdentity owner)
        {
            return BusinessInstanceState.Create(instanceId, LoadProfile(businessType), 0, owner);
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

        private static void SetBusinesses(SharedBusinessRuntimeManager manager, params BusinessInstanceState[] businesses)
        {
            SetPrivateField(manager, "businesses", new List<BusinessInstanceState>(businesses));
        }

        private static void SetTransferAgreements(SharedBusinessRuntimeManager manager, params BusinessTransferAgreementState[] agreements)
        {
            SetPrivateField(manager, "transferAgreements", new List<BusinessTransferAgreementState>(agreements));
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

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, $"{target.GetType().Name}.{fieldName} should exist.");
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
