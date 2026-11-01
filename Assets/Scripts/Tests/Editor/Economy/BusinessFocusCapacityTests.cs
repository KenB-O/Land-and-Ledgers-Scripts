using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    public sealed class BusinessFocusCapacityTests
    {
        [Test]
        public void CoreBusinessProfilesDeclareMainFocus()
        {
            Dictionary<BusinessType, BusinessProfileDefinition> profiles = LoadProfilesByType();

            AssertFocus(profiles, BusinessType.GeneralStore, "balanced_staples", "Balanced Staples");
            AssertFocus(profiles, BusinessType.Blacksmith, "repair_support", "Repair Support");
            AssertFocus(profiles, BusinessType.Butcher, "fresh_sales", "Fresh Sales");
            AssertFocus(profiles, BusinessType.CropFarm, "staple_production", "Staple Production");
            AssertFocus(profiles, BusinessType.Ranch, "beef", "Beef");
            AssertFocus(profiles, BusinessType.Doctor, "balanced_service", "Balanced Service");
            AssertFocus(profiles, BusinessType.Sawmill, "small_sawmill", "Small Sawmill");
            AssertFocus(profiles, BusinessType.LumberYard, "lumber_yard", "Lumber Yard");
        }

        [Test]
        public void RuntimeCapacitySeparatesStorageProcessingAndService()
        {
            Dictionary<BusinessType, BusinessProfileDefinition> profiles = LoadProfilesByType();

            BusinessInstanceState store = CreateResolvedInstance(profiles[BusinessType.GeneralStore]);
            Assert.Greater(store.Capacity.StorageCapacityUnits, 0);
            Assert.AreEqual(profiles[BusinessType.GeneralStore].BaselineWeeklyThroughputUnits, store.Capacity.ProcessingCapacityUnitsPerWeek);
            Assert.AreEqual(0, store.Capacity.ServiceCapacityVisitsPerDay);

            BusinessInstanceState blacksmith = CreateResolvedInstance(profiles[BusinessType.Blacksmith]);
            Assert.Greater(blacksmith.Capacity.StorageCapacityUnits, 0);
            Assert.AreEqual(14, blacksmith.Capacity.ProcessingCapacityUnitsPerWeek);
            Assert.AreEqual(4, blacksmith.Capacity.ServiceCapacityVisitsPerDay);

            BusinessInstanceState doctor = CreateResolvedInstance(profiles[BusinessType.Doctor]);
            Assert.Greater(doctor.Capacity.StorageCapacityUnits, 0);
            Assert.AreEqual(0, doctor.Capacity.ProcessingCapacityUnitsPerWeek);
            Assert.AreEqual(8, doctor.Capacity.ServiceCapacityVisitsPerDay);

            BusinessInstanceState sawmill = CreateResolvedInstance(profiles[BusinessType.Sawmill]);
            Assert.Greater(sawmill.Capacity.StorageCapacityUnits, 0);
            Assert.AreEqual(40, sawmill.Capacity.ProcessingCapacityUnitsPerWeek);
            Assert.AreEqual(0, sawmill.Capacity.ServiceCapacityVisitsPerDay);
        }

        [Test]
        public void SawmillProfileDefinesSmallSawmillWorkersAndStocks()
        {
            Dictionary<BusinessType, BusinessProfileDefinition> profiles = LoadProfilesByType();
            BusinessProfileDefinition sawmill = profiles[BusinessType.Sawmill];

            Assert.AreEqual("sawmill_small_sawmill", sawmill.Business.BusinessId);
            Assert.AreEqual(BusinessThroughputMode.Converter, sawmill.ThroughputMode);
            Assert.NotNull(sawmill.FindCategory("standing_timber"));
            Assert.NotNull(sawmill.FindCategory("sawmill_logs"));
            Assert.NotNull(sawmill.FindCategory("lumber"));
            Assert.NotNull(sawmill.FindCategory("slabs_offcuts"));
            AssertWorkerSlot(sawmill, "sawyer", true);
            AssertWorkerSlot(sawmill, "logger", false);
            AssertWorkerSlot(sawmill, "yard_teamster", false);
            AssertWorkerSlot(sawmill, "foreman", false);
        }

        [Test]
        public void LumberYardProfileDefinesTownFacingStockAndWorkers()
        {
            Dictionary<BusinessType, BusinessProfileDefinition> profiles = LoadProfilesByType();
            BusinessProfileDefinition yard = profiles[BusinessType.LumberYard];

            Assert.AreEqual("lumber_yard", yard.Business.BusinessId);
            Assert.AreEqual(BusinessThroughputMode.Retail, yard.ThroughputMode);
            Assert.NotNull(yard.FindCategory("lumber"));
            AssertWorkerSlot(yard, "yard_manager", true);
            AssertWorkerSlot(yard, "yard_hand", false);
            AssertWorkerSlot(yard, "delivery_teamster", false);
            AssertWorkerSlot(yard, "bookkeeper", false);
        }

        [Test]
        public void CapacityBottleneckUsesRelevantBusinessContract()
        {
            Dictionary<BusinessType, BusinessProfileDefinition> profiles = LoadProfilesByType();

            BusinessInstanceState blacksmith = CreateResolvedInstance(profiles[BusinessType.Blacksmith]);
            Assert.AreEqual(BusinessCapacityBottleneck.Processing, blacksmith.Capacity.Bottleneck);

            BusinessInstanceState doctor = CreateResolvedInstance(profiles[BusinessType.Doctor]);
            Assert.AreEqual(BusinessCapacityBottleneck.Service, doctor.Capacity.Bottleneck);
        }

        private static Dictionary<BusinessType, BusinessProfileDefinition> LoadProfilesByType()
        {
            BusinessProfileDefinition[] profiles = Resources.LoadAll<BusinessProfileDefinition>("Core/Economy/BusinessProfiles");
            Dictionary<BusinessType, BusinessProfileDefinition> byType = new();
            for (int i = 0; i < profiles.Length; i++)
            {
                if (profiles[i] != null)
                {
                    byType[profiles[i].Business.BusinessType] = profiles[i];
                }
            }

            AssertProfileLoaded(byType, BusinessType.GeneralStore);
            AssertProfileLoaded(byType, BusinessType.Blacksmith);
            AssertProfileLoaded(byType, BusinessType.Butcher);
            AssertProfileLoaded(byType, BusinessType.CropFarm);
            AssertProfileLoaded(byType, BusinessType.Ranch);
            AssertProfileLoaded(byType, BusinessType.Doctor);
            AssertProfileLoaded(byType, BusinessType.Sawmill);
            AssertProfileLoaded(byType, BusinessType.LumberYard);
            return byType;
        }

        private static BusinessInstanceState CreateResolvedInstance(BusinessProfileDefinition profile)
        {
            BusinessInstanceState instance = BusinessInstanceState.Create($"test_{profile.Business.BusinessId}", profile, 0, BusinessOwnerIdentity.Player());
            instance.ResolveWeeklyBaselineThroughput();
            instance.ResolveDailyBaselineService();
            return instance;
        }

        private static void AssertFocus(
            IReadOnlyDictionary<BusinessType, BusinessProfileDefinition> profiles,
            BusinessType businessType,
            string expectedId,
            string expectedLabel)
        {
            BusinessMainFocusState focus = profiles[businessType].MainFocus;
            Assert.AreEqual(expectedId, focus.FocusId);
            Assert.AreEqual(expectedLabel, focus.DisplayName);
            Assert.IsFalse(string.IsNullOrWhiteSpace(focus.PlayerFacingSummary));
        }

        private static void AssertProfileLoaded(
            IReadOnlyDictionary<BusinessType, BusinessProfileDefinition> profiles,
            BusinessType businessType)
        {
            Assert.IsTrue(profiles.ContainsKey(businessType), $"{businessType} profile should be present in Resources.");
        }

        private static void AssertWorkerSlot(BusinessProfileDefinition profile, string slotId, bool required)
        {
            ReadOnlySpan<WorkerSlotDefinition> slots = profile.Business.WorkerSlots;
            for (int i = 0; i < slots.Length; i++)
            {
                if (string.Equals(slots[i].SlotId, slotId, System.StringComparison.OrdinalIgnoreCase))
                {
                    Assert.AreEqual(required, slots[i].RequiredForOpening);
                    return;
                }
            }

            Assert.Fail($"Expected worker slot {slotId}.");
        }
    }
}
