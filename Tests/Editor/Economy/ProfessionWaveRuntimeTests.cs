using System.Collections.Generic;
using System.Reflection;
using System.Text;
using LandLedgers.Economy;
using LandLedgers.Persistence;
using LandLedgers.Population;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class ProfessionWaveRuntimeTests
    {
        [Test]
        public void ProfessionWaveProfilesLoadWithGroundedArchetypes()
        {
            Dictionary<BusinessType, BusinessProfileDefinition> profiles = LoadProfilesByType();

            AssertProfile(profiles, BusinessType.LiveryFreight, BusinessArchetype.LogisticsMovement, BusinessThroughputMode.Service);
            AssertProfile(profiles, BusinessType.Builder, BusinessArchetype.ServiceThroughput, BusinessThroughputMode.Service);
            AssertProfile(profiles, BusinessType.FuelDealer, BusinessArchetype.GoodsTransformer, BusinessThroughputMode.Converter);
            AssertProfile(profiles, BusinessType.GrainMill, BusinessArchetype.GoodsTransformer, BusinessThroughputMode.Converter);
            AssertProfile(profiles, BusinessType.Bakery, BusinessArchetype.GoodsTransformer, BusinessThroughputMode.Converter);
            AssertProfile(profiles, BusinessType.Tailor, BusinessArchetype.GoodsTransformer, BusinessThroughputMode.Converter);
            AssertProfile(profiles, BusinessType.Saloon, BusinessArchetype.ServiceThroughput, BusinessThroughputMode.Service);
            AssertProfile(profiles, BusinessType.Barber, BusinessArchetype.ServiceThroughput, BusinessThroughputMode.Service);
            AssertProfile(profiles, BusinessType.Wheelwright, BusinessArchetype.GoodsTransformer, BusinessThroughputMode.Converter);
            AssertProfile(profiles, BusinessType.Mine, BusinessArchetype.GoodsTransformer, BusinessThroughputMode.Producer);
        }

        [Test]
        public void ProfessionProfilesUseCanonicalDisplayNames()
        {
            Dictionary<BusinessType, BusinessProfileDefinition> profiles = LoadProfilesByType();
            BusinessType[] expected =
            {
                BusinessType.GeneralStore,
                BusinessType.Blacksmith,
                BusinessType.Butcher,
                BusinessType.Ranch,
                BusinessType.CropFarm,
                BusinessType.Doctor,
                BusinessType.Sawmill,
                BusinessType.LumberYard,
                BusinessType.BoardingHouse,
                BusinessType.LiveryFreight,
                BusinessType.Builder,
                BusinessType.FuelDealer,
                BusinessType.GrainMill,
                BusinessType.Bakery,
                BusinessType.Tailor,
                BusinessType.Saloon,
                BusinessType.Barber,
                BusinessType.Wheelwright,
                BusinessType.Mine
            };

            for (int i = 0; i < expected.Length; i++)
            {
                BusinessType businessType = expected[i];
                Assert.IsTrue(profiles.TryGetValue(businessType, out BusinessProfileDefinition profile), $"{businessType} profile missing.");
                Assert.AreEqual(BusinessRuntimeNaming.GetBusinessTypeDisplayName(businessType), profile.Business.DisplayName, $"{businessType} display name should be canonical.");
            }
        }

        [Test]
        public void ProfessionProfilesDeclareCoherentWorkflowShapes()
        {
            Dictionary<BusinessType, BusinessProfileDefinition> profiles = LoadProfilesByType();

            AssertProfileShape(profiles, BusinessType.BoardingHouse, BusinessThroughputMode.Service, hasInput: true, hasService: true, hasOutput: false);
            AssertProfileShape(profiles, BusinessType.LiveryFreight, BusinessThroughputMode.Service, hasInput: true, hasService: true, hasOutput: true);
            AssertProfileShape(profiles, BusinessType.Builder, BusinessThroughputMode.Service, hasInput: false, hasService: true, hasOutput: false);
            AssertProfileShape(profiles, BusinessType.FuelDealer, BusinessThroughputMode.Converter, hasInput: true, hasService: false, hasOutput: true);
            AssertProfileShape(profiles, BusinessType.GrainMill, BusinessThroughputMode.Converter, hasInput: true, hasService: false, hasOutput: true);
            AssertProfileShape(profiles, BusinessType.Bakery, BusinessThroughputMode.Converter, hasInput: true, hasService: false, hasOutput: true);
            AssertProfileShape(profiles, BusinessType.Tailor, BusinessThroughputMode.Converter, hasInput: true, hasService: true, hasOutput: true);
            AssertProfileShape(profiles, BusinessType.Saloon, BusinessThroughputMode.Service, hasInput: true, hasService: true, hasOutput: false);
            AssertProfileShape(profiles, BusinessType.Barber, BusinessThroughputMode.Service, hasInput: true, hasService: true, hasOutput: false);
            AssertProfileShape(profiles, BusinessType.Wheelwright, BusinessThroughputMode.Converter, hasInput: true, hasService: true, hasOutput: true);
            AssertProfileShape(profiles, BusinessType.Mine, BusinessThroughputMode.Producer, hasInput: true, hasService: false, hasOutput: true);
        }

        [Test]
        public void ProfessionProfilesDeclareExplicitAuthorityMetadata()
        {
            Dictionary<BusinessType, BusinessProfileDefinition> profiles = LoadProfilesByType();

            foreach (BusinessProfileDefinition profile in profiles.Values)
            {
                SerializedObject serialized = new(profile);
                SerializedProperty archetype = serialized.FindProperty("archetype");
                Assert.NotNull(archetype, $"{profile.name} archetype field missing.");
                Assert.AreNotEqual(
                    (int)BusinessArchetype.Unspecified,
                    archetype.enumValueIndex,
                    $"{profile.name} should declare an explicit business archetype.");

                SerializedProperty mainFocus = serialized.FindProperty("mainFocus");
                Assert.NotNull(mainFocus, $"{profile.name} main focus field missing.");
                AssertConfiguredText(mainFocus.FindPropertyRelative("focusId"), profile.name, "focus id", "balanced");
                AssertConfiguredText(mainFocus.FindPropertyRelative("displayName"), profile.name, "focus display name", "Balanced");
                AssertConfiguredText(mainFocus.FindPropertyRelative("playerFacingSummary"), profile.name, "focus summary");
            }
        }

        [Test]
        public void NewBusinessTypeRoundTripsThroughBusinessSaveDto()
        {
            BusinessProfileDefinition profile = LoadProfile(BusinessType.GrainMill);
            BusinessInstanceState source = BusinessInstanceState.Create(
                "test_grain_mill",
                profile,
                12,
                BusinessOwnerIdentity.Npc(1201, "Owen Miller", "Miller"));
            source.EnsureOwnerOperatorStaffing(source.Owner);
            source.ResolveWeeklyBaselineThroughput();

            BusinessInstanceSaveDto dto = source.CaptureSaveDto();
            BusinessInstanceState restored = BusinessInstanceState.FromSaveDto(dto);

            Assert.NotNull(restored);
            Assert.AreEqual(BusinessType.GrainMill, restored.BusinessType);
            Assert.AreEqual(BusinessThroughputMode.Converter, restored.ThroughputMode);
            Assert.AreEqual(source.BaselineWeeklyThroughputUnits, restored.BaselineWeeklyThroughputUnits);
            Assert.NotNull(restored.RuntimeState.GetCategoryStock("grain"));
            Assert.NotNull(restored.RuntimeState.GetCategoryStock("flour"));
            Assert.Greater(restored.RuntimeState.WorkerSlots.Count, 0);
        }

        [Test]
        public void FuelReservePrefersFuelWoodBeforeSawmillByproducts()
        {
            HouseholdReserveDefinition definition = HouseholdReserveCatalog.Get(HouseholdReserveCatalog.FuelWoodCategoryId);

            Assert.NotNull(definition);
            Assert.GreaterOrEqual(definition.LocalSellerCategoryIds.Count, 3);
            Assert.AreEqual("fuel_wood", definition.LocalSellerCategoryIds[0]);
            Assert.AreEqual("slabs_offcuts", definition.LocalSellerCategoryIds[1]);
        }

        [Test]
        public void StaffedLiveryReducesSawmillToLumberYardHaulingFriction()
        {
            GameObject runtimeObject = new("Livery Hauling Test");
            try
            {
                SharedBusinessRuntimeManager manager = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
                BusinessInstanceState sawmill = CreateStaffedBusiness(BusinessType.Sawmill, "test_sawmill", 1);
                BusinessInstanceState yard = CreateStaffedBusiness(BusinessType.LumberYard, "test_lumber_yard", 2);
                BusinessInstanceState livery = CreateStaffedBusiness(BusinessType.LiveryFreight, "test_livery", 3);

                SetBusinesses(manager, new List<BusinessInstanceState> { sawmill, yard });
                int unsupportedCost = InvokeSawmillDeliveryCost(manager, sawmill, yard);

                SetBusinesses(manager, new List<BusinessInstanceState> { sawmill, yard, livery });
                int supportedCost = InvokeSawmillDeliveryCost(manager, sawmill, yard);

                Assert.Greater(unsupportedCost, supportedCost);
                Assert.Greater(supportedCost, 0);
            }
            finally
            {
                Object.DestroyImmediate(runtimeObject);
            }
        }

        [Test]
        public void BuilderAddsConstructionLaborCapacity()
        {
            GameObject runtimeObject = new("Builder Capacity Test");
            try
            {
                SharedBusinessRuntimeManager manager = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
                BusinessInstanceState builder = CreateStaffedBusiness(BusinessType.Builder, "test_builder", 4);
                SetBusinesses(manager, new List<BusinessInstanceState> { builder });

                Assert.Greater(manager.GetBuilderLaborCapacityUnits(), 0);
            }
            finally
            {
                Object.DestroyImmediate(runtimeObject);
            }
        }

        [Test]
        public void ServiceBusinessInputsConstrainVisits()
        {
            GameObject runtimeObject = new("Service Input Constraint Test");
            try
            {
                SharedBusinessRuntimeManager manager = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
                BusinessInstanceState barber = CreateStaffedBusiness(BusinessType.Barber, "test_barber", 5);
                barber.RuntimeState.SetCurrentCashCents(0);
                barber.RuntimeState.GetCategoryStock("barber_consumables").SetCurrentStockForTests(0);

                InvokeBusinessOperation(manager, barber);

                Assert.AreEqual(0, barber.LastDailyServiceVisits);
                StringAssert.Contains("input shortage: barber_consumables", barber.LastWeeklyBlockedReason);
            }
            finally
            {
                Object.DestroyImmediate(runtimeObject);
            }
        }

        [Test]
        public void BakeryRequiresFuelAsSecondaryInput()
        {
            GameObject runtimeObject = new("Bakery Secondary Input Test");
            try
            {
                SharedBusinessRuntimeManager manager = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
                BusinessInstanceState bakery = CreateStaffedBusiness(BusinessType.Bakery, "test_bakery", 6);
                bakery.RuntimeState.SetCurrentCashCents(0);
                bakery.RuntimeState.GetCategoryStock("flour").SetCurrentStockForTests(20);
                bakery.RuntimeState.GetCategoryStock("fuel_wood").SetCurrentStockForTests(0);

                InvokeBusinessOperation(manager, bakery);

                Assert.AreEqual(0, bakery.LastWeeklyThroughputUnits);
                StringAssert.Contains("input shortage: fuel_wood", bakery.LastWeeklyBlockedReason);
            }
            finally
            {
                Object.DestroyImmediate(runtimeObject);
            }
        }

        [Test]
        public void DefaultRecurringOrdersChainProductionServicesAndLogistics()
        {
            IReadOnlyList<LocalRecurringOrderTemplate> templates = LocalRecurringOrderTemplate.CreateDefaultTemplates();

            AssertTemplate(templates, BusinessType.CropFarm, "crop_food", BusinessType.GrainMill, "grain");
            AssertTemplate(templates, BusinessType.GrainMill, "flour", BusinessType.Bakery, "flour");
            AssertTemplate(templates, BusinessType.Bakery, "bread", BusinessType.BoardingHouse, "staple_food");
            AssertTemplate(templates, BusinessType.Ranch, "livestock_inputs", BusinessType.Butcher, "livestock_inputs");
            AssertTemplate(templates, BusinessType.Butcher, "meat", BusinessType.BoardingHouse, "meat");
            AssertTemplate(templates, BusinessType.FuelDealer, "fuel_wood", BusinessType.Saloon, "fuel_wood");
            AssertTemplate(templates, BusinessType.LumberYard, "lumber", BusinessType.Builder, "lumber");
            AssertTemplate(templates, BusinessType.Blacksmith, "tools_hardware", BusinessType.Wheelwright, "repair_inputs");
            AssertTemplate(templates, BusinessType.GeneralStore, "household_goods", BusinessType.Tailor, "cloth_notions");
            AssertTemplate(templates, BusinessType.Tailor, "clothing", BusinessType.GeneralStore, "clothing");
            AssertTemplate(templates, BusinessType.Wheelwright, "wheelwright_repairs", BusinessType.LiveryFreight, "wheelwright_repairs");
            AssertTemplate(templates, BusinessType.CropFarm, "crop_food", BusinessType.LiveryFreight, "livery_feed_upkeep");
            AssertTemplate(templates, BusinessType.GeneralStore, "staple_food", BusinessType.BoardingHouse, "staple_food");
            AssertTemplate(templates, BusinessType.GeneralStore, "staple_food", BusinessType.Saloon, "meal_inputs");
            AssertTemplate(templates, BusinessType.GeneralStore, "tools_hardware", BusinessType.Builder, "tools_hardware");
            AssertTemplate(templates, BusinessType.GeneralStore, "medicine_remedies", BusinessType.Doctor, "medicine_remedies");
        }

        [Test]
        public void RecurringOrdersMoveLocalSuppliesIntoBoardingHouseAndLivery()
        {
            GameObject runtimeObject = new("Recurring Order Chain Test");
            try
            {
                SharedBusinessRuntimeManager manager = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
                BusinessInstanceState cropFarm = CreateStaffedBusiness(BusinessType.CropFarm, "test_crop_farm", 10);
                BusinessInstanceState bakery = CreateStaffedBusiness(BusinessType.Bakery, "test_bakery_chain", 11);
                BusinessInstanceState butcher = CreateStaffedBusiness(BusinessType.Butcher, "test_butcher", 12);
                BusinessInstanceState fuelDealer = CreateStaffedBusiness(BusinessType.FuelDealer, "test_fuel_dealer", 13);
                BusinessInstanceState wheelwright = CreateStaffedBusiness(BusinessType.Wheelwright, "test_wheelwright", 14);
                BusinessInstanceState boardingHouse = CreateStaffedBusiness(BusinessType.BoardingHouse, "test_boarding_house", 15);
                BusinessInstanceState livery = CreateStaffedBusiness(BusinessType.LiveryFreight, "test_livery_chain", 16);
                BusinessInstanceState generalStore = CreateStaffedBusiness(BusinessType.GeneralStore, "test_general_store_supplier", 17);
                BusinessInstanceState saloon = CreateStaffedBusiness(BusinessType.Saloon, "test_saloon_chain", 18);
                BusinessInstanceState builder = CreateStaffedBusiness(BusinessType.Builder, "test_builder_chain", 19);
                BusinessInstanceState doctor = CreateStaffedBusiness(BusinessType.Doctor, "test_doctor_chain", 20);

                boardingHouse.RuntimeState.GetCategoryStock("staple_food").SetCurrentStockForTests(0);
                boardingHouse.RuntimeState.GetCategoryStock("meat").SetCurrentStockForTests(0);
                boardingHouse.RuntimeState.GetCategoryStock("fuel_wood").SetCurrentStockForTests(0);
                livery.RuntimeState.GetCategoryStock("livery_feed_upkeep").SetCurrentStockForTests(0);
                livery.RuntimeState.GetCategoryStock("wheelwright_repairs").SetCurrentStockForTests(0);
                saloon.RuntimeState.GetCategoryStock("meal_inputs").SetCurrentStockForTests(0);
                builder.RuntimeState.GetCategoryStock("tools_hardware").SetCurrentStockForTests(0);
                doctor.RuntimeState.GetCategoryStock("medicine_remedies").SetCurrentStockForTests(0);

                SetBusinesses(manager, new List<BusinessInstanceState>
                {
                    cropFarm,
                    bakery,
                    butcher,
                    fuelDealer,
                    wheelwright,
                    boardingHouse,
                    livery,
                    generalStore,
                    saloon,
                    builder,
                    doctor
                });

                int fulfilled = InvokeRecurringLocalOrders(manager);

                Assert.Greater(fulfilled, 0);
                Assert.Greater(boardingHouse.RuntimeState.GetCategoryStock("staple_food").CurrentStockUnits, 0);
                Assert.Greater(boardingHouse.RuntimeState.GetCategoryStock("meat").CurrentStockUnits, 0);
                Assert.Greater(boardingHouse.RuntimeState.GetCategoryStock("fuel_wood").CurrentStockUnits, 0);
                Assert.Greater(livery.RuntimeState.GetCategoryStock("livery_feed_upkeep").CurrentStockUnits, 0);
                Assert.Greater(livery.RuntimeState.GetCategoryStock("wheelwright_repairs").CurrentStockUnits, 0);
                Assert.Greater(saloon.RuntimeState.GetCategoryStock("meal_inputs").CurrentStockUnits, 0);
                Assert.Greater(builder.RuntimeState.GetCategoryStock("tools_hardware").CurrentStockUnits, 0);
                Assert.Greater(doctor.RuntimeState.GetCategoryStock("medicine_remedies").CurrentStockUnits, 0);
            }
            finally
            {
                Object.DestroyImmediate(runtimeObject);
            }
        }

        [Test]
        public void LocalRelationshipHealthAdjustsSurvivalReservePressure()
        {
            GameObject runtimeObject = new("Local Relationship Reserve Test");
            try
            {
                SharedBusinessRuntimeManager manager = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
                BusinessInstanceState cropFarm = CreateStaffedBusiness(BusinessType.CropFarm, "test_reserve_crop", 21);
                BusinessInstanceState livery = CreateStaffedBusiness(BusinessType.LiveryFreight, "test_reserve_livery", 22);
                livery.RuntimeState.GetCategoryStock("livery_feed_upkeep").SetCurrentStockForTests(0);
                SetBusinesses(manager, new List<BusinessInstanceState> { cropFarm, livery });

                int baselineReserve = manager.GetLocalTradeAdjustedSurvivalCashReserveCents(livery);
                InvokeRecurringLocalOrders(manager);
                int supportedReserve = manager.GetLocalTradeAdjustedSurvivalCashReserveCents(livery);

                Assert.Less(supportedReserve, baselineReserve);

                cropFarm.RuntimeState.GetCategoryStock("crop_food").SetCurrentStockForTests(0);
                livery.RuntimeState.GetCategoryStock("livery_feed_upkeep").SetCurrentStockForTests(0);
                InvokeRecurringLocalOrders(manager);
                int strainedReserve = manager.GetLocalTradeAdjustedSurvivalCashReserveCents(livery);

                Assert.Greater(strainedReserve, supportedReserve);
            }
            finally
            {
                Object.DestroyImmediate(runtimeObject);
            }
        }

        [Test]
        public void CommerceLedgerNamesStrainedAndCancelledLocalLinks()
        {
            GameObject runtimeObject = new("Local Commerce Ledger Strain Test");
            try
            {
                SharedBusinessRuntimeManager manager = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
                BusinessInstanceState cropFarm = CreateStaffedBusiness(BusinessType.CropFarm, "test_ledger_crop", 23);
                BusinessInstanceState livery = CreateStaffedBusiness(BusinessType.LiveryFreight, "test_ledger_livery", 24);
                SetBusinesses(manager, new List<BusinessInstanceState> { cropFarm, livery });

                for (int i = 0; i < 3; i++)
                {
                    cropFarm.RuntimeState.GetCategoryStock("crop_food").SetCurrentStockForTests(0);
                    livery.RuntimeState.GetCategoryStock("livery_feed_upkeep").SetCurrentStockForTests(0);
                    InvokeRecurringLocalOrders(manager);
                }

                string ledger = manager.BuildTownCommerceLedgerSummary();

                StringAssert.Contains("cancelled", ledger.ToLowerInvariant());
                StringAssert.Contains("test_ledger_crop", ledger);
                StringAssert.Contains("test_ledger_livery", ledger);
                StringAssert.Contains("livery_feed_upkeep", ledger);
            }
            finally
            {
                Object.DestroyImmediate(runtimeObject);
            }
        }

        [Test]
        public void StartupSeedingKeepsSecondWaveOutOfLaunchTown()
        {
            Assert.IsTrue(InvokeStaticBusinessTypePredicate("CanAutoSeedAtStart", BusinessType.FuelDealer));
            Assert.IsTrue(InvokeStaticBusinessTypePredicate("CanAutoSeedAtStart", BusinessType.GrainMill));
            Assert.IsTrue(InvokeStaticBusinessTypePredicate("CanAutoSeedAtStart", BusinessType.Bakery));

            Assert.IsFalse(InvokeStaticBusinessTypePredicate("IsCoreTownBusiness", BusinessType.FuelDealer));
            Assert.IsFalse(InvokeStaticBusinessTypePredicate("IsCoreTownBusiness", BusinessType.GrainMill));
            Assert.IsFalse(InvokeStaticBusinessTypePredicate("IsCoreTownBusiness", BusinessType.Bakery));

            Assert.IsFalse(InvokeStaticBusinessTypePredicate("CanAutoSeedAtStart", BusinessType.Tailor));
            Assert.IsFalse(InvokeStaticBusinessTypePredicate("CanAutoSeedAtStart", BusinessType.Saloon));
            Assert.IsFalse(InvokeStaticBusinessTypePredicate("CanAutoSeedAtStart", BusinessType.Barber));
            Assert.IsFalse(InvokeStaticBusinessTypePredicate("CanAutoSeedAtStart", BusinessType.Wheelwright));
        }

        private static void AssertProfile(
            IReadOnlyDictionary<BusinessType, BusinessProfileDefinition> profiles,
            BusinessType businessType,
            BusinessArchetype archetype,
            BusinessThroughputMode throughputMode)
        {
            Assert.IsTrue(profiles.ContainsKey(businessType), $"{businessType} profile missing.");
            BusinessProfileDefinition profile = profiles[businessType];
            Assert.AreEqual(archetype, profile.Archetype);
            Assert.AreEqual(throughputMode, profile.ThroughputMode);
        }

        private static void AssertProfileShape(
            IReadOnlyDictionary<BusinessType, BusinessProfileDefinition> profiles,
            BusinessType businessType,
            BusinessThroughputMode throughputMode,
            bool hasInput,
            bool hasService,
            bool hasOutput)
        {
            Assert.IsTrue(profiles.TryGetValue(businessType, out BusinessProfileDefinition profile), $"{businessType} profile missing.");
            Assert.AreEqual(throughputMode, profile.ThroughputMode);
            Assert.AreEqual(hasInput, profile.InputCategoryIds.Length > 0, $"{businessType} input shape mismatch.");
            Assert.AreEqual(hasService, profile.ServiceCategoryIds.Length > 0, $"{businessType} service shape mismatch.");
            Assert.AreEqual(hasOutput, profile.OutputCategoryIds.Length > 0, $"{businessType} output shape mismatch.");
        }

        private static void AssertTemplate(
            IReadOnlyList<LocalRecurringOrderTemplate> templates,
            BusinessType sellerType,
            string sellerCategoryId,
            BusinessType buyerType,
            string buyerCategoryId)
        {
            for (int i = 0; i < templates.Count; i++)
            {
                LocalRecurringOrderTemplate template = templates[i];
                if (template.SellerType == sellerType
                    && template.BuyerType == buyerType
                    && template.SellerCategoryId == sellerCategoryId
                    && template.BuyerCategoryId == buyerCategoryId)
                {
                    return;
                }
            }

            Assert.Fail($"Missing recurring order {sellerType}:{sellerCategoryId}->{buyerType}:{buyerCategoryId}.");
        }

        private static void AssertConfiguredText(SerializedProperty property, string profileName, string label, string defaultValue = null)
        {
            Assert.NotNull(property, $"{profileName} {label} field missing.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(property.stringValue), $"{profileName} {label} should be configured.");
            if (!string.IsNullOrWhiteSpace(defaultValue))
            {
                Assert.AreNotEqual(defaultValue, property.stringValue, $"{profileName} {label} should not rely on the default {defaultValue} value.");
            }
        }

        private static BusinessInstanceState CreateStaffedBusiness(BusinessType businessType, string instanceId, int buildingId)
        {
            BusinessInstanceState business = BusinessInstanceState.Create(
                instanceId,
                LoadProfile(businessType),
                buildingId,
                BusinessOwnerIdentity.Npc(2000 + buildingId, $"{businessType} Owner", $"{businessType}"));
            business.EnsureOwnerOperatorStaffing(business.Owner);
            return business;
        }

        private static int InvokeSawmillDeliveryCost(
            SharedBusinessRuntimeManager manager,
            BusinessInstanceState sawmill,
            BusinessInstanceState yard)
        {
            MethodInfo method = typeof(SharedBusinessRuntimeManager).GetMethod(
                "GetSawmillToLumberYardDeliveryCostPerUnitCents",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (int)method.Invoke(manager, new object[] { sawmill, yard });
        }

        private static void InvokeBusinessOperation(SharedBusinessRuntimeManager manager, BusinessInstanceState business)
        {
            MethodInfo method = typeof(SharedBusinessRuntimeManager).GetMethod(
                "ResolveBusinessOperation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(manager, new object[] { business, new StringBuilder("Weekly shared operations: ") });
        }

        private static int InvokeRecurringLocalOrders(SharedBusinessRuntimeManager manager)
        {
            MethodInfo method = typeof(SharedBusinessRuntimeManager).GetMethod(
                "ResolveRecurringLocalOrders",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (int)method.Invoke(manager, new object[] { new StringBuilder("Weekly shared operations: ") });
        }

        private static bool InvokeStaticBusinessTypePredicate(string methodName, BusinessType businessType)
        {
            MethodInfo method = typeof(SharedBusinessRuntimeManager).GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (bool)method.Invoke(null, new object[] { businessType });
        }

        private static void SetBusinesses(SharedBusinessRuntimeManager manager, List<BusinessInstanceState> businesses)
        {
            FieldInfo field = typeof(SharedBusinessRuntimeManager).GetField("businesses", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(manager, businesses);
        }

        private static BusinessProfileDefinition LoadProfile(BusinessType businessType)
        {
            Dictionary<BusinessType, BusinessProfileDefinition> profiles = LoadProfilesByType();
            Assert.IsTrue(profiles.TryGetValue(businessType, out BusinessProfileDefinition profile), $"{businessType} profile missing.");
            return profile;
        }

        private static Dictionary<BusinessType, BusinessProfileDefinition> LoadProfilesByType()
        {
            BusinessProfileDefinition[] profiles = Resources.LoadAll<BusinessProfileDefinition>("Core/Economy/BusinessProfiles");
            Dictionary<BusinessType, BusinessProfileDefinition> byType = new();
            for (int i = 0; i < profiles.Length; i++)
            {
                BusinessProfileDefinition profile = profiles[i];
                if (profile != null)
                {
                    byType[profile.Business.BusinessType] = profile;
                }
            }

            return byType;
        }
    }
}
