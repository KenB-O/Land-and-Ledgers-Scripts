using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Persistence;
using LandLedgers.Population;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Infrastructure
{
    /// <summary>
    /// W-G1 (Agent G, expanded campaign 2026-11-16): hub-to-state
    /// register sync. Proves the state method directly (no hub, no
    /// Unity): a non-empty hub-only register is adopted into the Pass 9
    /// state lists so the next Capture persists it; a read-created
    /// empty register is NOT adopted; an already-present register is
    /// replaced by id with the live hub object; a state-only register
    /// is never deleted; sync is idempotent; null inputs never throw;
    /// and an adopted register survives a real Capture/Restore round
    /// trip. The SaveLoadManager call site and the hub accessors are
    /// harness-excluded and source-reviewed only — no test here claims
    /// Unity compilation or PlayMode.
    /// </summary>
    [TestFixture]
    public sealed class HubRegisterSyncWg1Tests
    {
        private static BusinessEquipmentRegister MakeEquipmentRegister(string businessInstanceId, bool withAsset = false, bool withKit = false)
        {
            var register = new BusinessEquipmentRegister { BusinessInstanceId = businessInstanceId };
            if (withAsset)
            {
                register.Assets.Add(new EquipmentAsset
                {
                    AssetId = businessInstanceId + "-asset-1",
                    Kind = "carpenter-hand-tool-kit",
                    DisplayName = "Carpenter Hand Tool Kit",
                    Condition01 = 1f,
                    OwnerKind = "business",
                    OwnerId = businessInstanceId,
                });
            }
            if (withKit)
            {
                register.Kits.Add(new ToolKitInstance
                {
                    InstanceId = businessInstanceId + "-kit-1",
                    KitId = "carpenter-hand-tool-kit",
                    Condition01 = 1f,
                    OwnerKind = "business",
                    OwnerId = businessInstanceId,
                });
            }
            return register;
        }

        private static HouseholdFurnishingRegister MakeFurnishingRegister(int householdId, bool withAsset = false, bool withBinding = false)
        {
            var register = new HouseholdFurnishingRegister { HouseholdId = householdId };
            if (withAsset)
            {
                register.Assets.Add(new EquipmentAsset
                {
                    AssetId = "household-" + householdId + "-bed-1",
                    Kind = "furniture.bed",
                    DisplayName = "Bed",
                    Condition01 = 1f,
                    OwnerKind = "household",
                    OwnerId = householdId.ToString(),
                });
            }
            if (withBinding)
            {
                register.Bindings.Add(new FurnishingPlacementBinding
                {
                    BindingId = "household-" + householdId + "-binding-1",
                    AssetId = "household-" + householdId + "-bed-1",
                    BuildingInstanceId = 101,
                    PlacementId = "placement-1",
                    FloorIndex = 0,
                    LocalPosition = Vector3.zero,
                    LocalRotation = Quaternion.identity,
                    DefinitionId = "furniture.bed",
                    CapabilityKind = FurnishingCapabilityKind.SleepingPlace,
                    CapabilityReferenceId = "household-" + householdId + "-bed-1",
                    AcquisitionSource = FurnishingAcquisitionSource.Purchase,
                    AcquisitionSourceId = "purchase-1",
                });
            }
            return register;
        }

        [Test]
        public void SyncRegistersFromHub_AdoptsNonEmptyHubOnlyEquipmentRegister()
        {
            var state = new Series2Pass9SaveState();
            BusinessEquipmentRegister hubRegister = MakeEquipmentRegister("carpenter", withAsset: true);
            var diag = new List<string>();

            state.SyncRegistersFromHub(new[] { hubRegister }, new HouseholdFurnishingRegister[0], diag);

            Assert.AreEqual(1, state.EquipmentRegisters.Count);
            Assert.IsTrue(ReferenceEquals(hubRegister, state.EquipmentRegisters[0]), "Adoption must keep the live hub object, not a copy, so later hub mutations stay visible to Capture.");
            Assert.IsTrue(diag[diag.Count - 1].Contains("adopted 1"), "Summary line must report the adoption.");
        }

        [Test]
        public void SyncRegistersFromHub_SkipsEmptyHubOnlyEquipmentRegister()
        {
            var state = new Series2Pass9SaveState();
            BusinessEquipmentRegister hubRegister = MakeEquipmentRegister("carpenter");
            var diag = new List<string>();

            state.SyncRegistersFromHub(new[] { hubRegister }, new HouseholdFurnishingRegister[0], diag);

            Assert.AreEqual(0, state.EquipmentRegisters.Count, "A read-created empty register must not pollute the save.");
            Assert.IsTrue(diag[diag.Count - 1].Contains("skipped-empty 1"), "Summary line must report the skipped empty register.");
        }

        [Test]
        public void SyncRegistersFromHub_AdoptsKitsOnlyEquipmentRegister()
        {
            var state = new Series2Pass9SaveState();
            BusinessEquipmentRegister hubRegister = MakeEquipmentRegister("carpenter", withKit: true);
            var diag = new List<string>();

            state.SyncRegistersFromHub(new[] { hubRegister }, new HouseholdFurnishingRegister[0], diag);

            Assert.AreEqual(1, state.EquipmentRegisters.Count, "A register holding only a kit is non-empty and must be adopted.");
            Assert.IsTrue(ReferenceEquals(hubRegister, state.EquipmentRegisters[0]));
        }

        [Test]
        public void SyncRegistersFromHub_ReplacesExistingRegisterById()
        {
            var state = new Series2Pass9SaveState();
            state.EquipmentRegisters.Add(MakeEquipmentRegister("bakery", withAsset: true));
            BusinessEquipmentRegister hubRegister = MakeEquipmentRegister("bakery", withAsset: true);
            hubRegister.Assets[0].AssetId = "bakery-asset-2";
            var diag = new List<string>();

            state.SyncRegistersFromHub(new[] { hubRegister }, new HouseholdFurnishingRegister[0], diag);

            Assert.AreEqual(1, state.EquipmentRegisters.Count, "Replace-by-id must not duplicate the register.");
            Assert.IsTrue(ReferenceEquals(hubRegister, state.EquipmentRegisters[0]), "Live hub data is authoritative at save time.");
            Assert.AreEqual("bakery-asset-2", state.EquipmentRegisters[0].Assets[0].AssetId);
            Assert.IsTrue(diag[diag.Count - 1].Contains("replaced 1"), "Summary line must report the replacement.");
        }

        [Test]
        public void SyncRegistersFromHub_KeepsStateOnlyRegister()
        {
            var state = new Series2Pass9SaveState();
            state.EquipmentRegisters.Add(MakeEquipmentRegister("mill", withAsset: true));
            BusinessEquipmentRegister hubRegister = MakeEquipmentRegister("bakery", withAsset: true);
            var diag = new List<string>();

            state.SyncRegistersFromHub(new[] { hubRegister }, new HouseholdFurnishingRegister[0], diag);

            Assert.AreEqual(2, state.EquipmentRegisters.Count, "A state entry missing from the hub must never be deleted.");
            Assert.IsTrue(diag[diag.Count - 1].Contains("state-only kept 1"), "Summary line must report the kept state-only entry.");
            Assert.IsTrue(diag[diag.Count - 1].Contains("mill"), "Summary line must name the kept state-only entry.");
        }

        [Test]
        public void SyncRegistersFromHub_IsIdempotent()
        {
            var state = new Series2Pass9SaveState();
            BusinessEquipmentRegister hubRegister = MakeEquipmentRegister("carpenter", withAsset: true);
            BusinessEquipmentRegister emptyHubRegister = MakeEquipmentRegister("yard");
            var diag = new List<string>();

            state.SyncRegistersFromHub(new[] { hubRegister, emptyHubRegister }, new HouseholdFurnishingRegister[0], diag);
            state.SyncRegistersFromHub(new[] { hubRegister, emptyHubRegister }, new HouseholdFurnishingRegister[0], diag);

            Assert.AreEqual(1, state.EquipmentRegisters.Count, "Syncing twice must not duplicate the adopted register.");
            Assert.IsTrue(ReferenceEquals(hubRegister, state.EquipmentRegisters[0]));
            Assert.AreEqual(0, state.HouseholdFurnishingRegisters.Count);
        }

        [Test]
        public void SyncRegistersFromHub_NullEnumerablesAreNoOp()
        {
            var state = new Series2Pass9SaveState();
            state.EquipmentRegisters.Add(MakeEquipmentRegister("mill", withAsset: true));
            var diag = new List<string>();

            state.SyncRegistersFromHub(null, null, diag);

            Assert.AreEqual(1, state.EquipmentRegisters.Count, "Null hub enumerables must leave the state untouched.");
            Assert.AreEqual(0, state.HouseholdFurnishingRegisters.Count);
            Assert.AreEqual(3, diag.Count, "Two skip lines plus the summary line.");
        }

        [Test]
        public void SyncRegistersFromHub_NullDiagDoesNotThrow()
        {
            var state = new Series2Pass9SaveState();
            BusinessEquipmentRegister hubRegister = MakeEquipmentRegister("carpenter", withAsset: true);

            state.SyncRegistersFromHub(new[] { hubRegister }, new HouseholdFurnishingRegister[0], null);

            Assert.AreEqual(1, state.EquipmentRegisters.Count, "A null diag list must not throw and must not block adoption.");
        }

        [Test]
        public void SyncRegistersFromHub_AdoptsNonEmptyHubOnlyFurnishingRegister()
        {
            var state = new Series2Pass9SaveState();
            HouseholdFurnishingRegister hubRegister = MakeFurnishingRegister(7, withAsset: true);
            var diag = new List<string>();

            state.SyncRegistersFromHub(new BusinessEquipmentRegister[0], new[] { hubRegister }, diag);

            Assert.AreEqual(1, state.HouseholdFurnishingRegisters.Count);
            Assert.IsTrue(ReferenceEquals(hubRegister, state.HouseholdFurnishingRegisters[0]), "Adoption must keep the live hub object, not a copy.");
            Assert.IsTrue(diag[diag.Count - 1].Contains("adopted 1"), "Summary line must report the furnishing adoption.");
        }

        [Test]
        public void SyncRegistersFromHub_SkipsEmptyHubOnlyFurnishingRegister()
        {
            var state = new Series2Pass9SaveState();
            HouseholdFurnishingRegister hubRegister = MakeFurnishingRegister(7);
            var diag = new List<string>();

            state.SyncRegistersFromHub(new BusinessEquipmentRegister[0], new[] { hubRegister }, diag);

            Assert.AreEqual(0, state.HouseholdFurnishingRegisters.Count, "A read-created empty furnishing register must not pollute the save.");
            Assert.IsTrue(diag[diag.Count - 1].Contains("skipped-empty 1"), "Summary line must report the skipped empty furnishing register.");
        }

        [Test]
        public void SyncRegistersFromHub_FurnishingReplaceByIdAndKeepStateOnly()
        {
            var state = new Series2Pass9SaveState();
            state.HouseholdFurnishingRegisters.Add(MakeFurnishingRegister(7, withAsset: true));
            state.HouseholdFurnishingRegisters.Add(MakeFurnishingRegister(8, withAsset: true));
            HouseholdFurnishingRegister hubRegister = MakeFurnishingRegister(7, withAsset: true);
            hubRegister.Assets[0].AssetId = "household-7-bed-2";
            var diag = new List<string>();

            state.SyncRegistersFromHub(new BusinessEquipmentRegister[0], new[] { hubRegister }, diag);

            Assert.AreEqual(2, state.HouseholdFurnishingRegisters.Count);
            Assert.IsTrue(ReferenceEquals(hubRegister, state.HouseholdFurnishingRegisters[0]), "Live hub data is authoritative at save time.");
            Assert.AreEqual("household-7-bed-2", state.HouseholdFurnishingRegisters[0].Assets[0].AssetId);
            Assert.AreEqual(8, state.HouseholdFurnishingRegisters[1].HouseholdId, "The state-only household 8 register must be kept, never deleted.");
            Assert.IsTrue(diag[diag.Count - 1].Contains("replaced 1"), "Summary line must report the replacement.");
            Assert.IsTrue(diag[diag.Count - 1].Contains("state-only kept 1"), "Summary line must report the kept state-only entry.");
        }

        [Test]
        public void SyncRegistersFromHub_AdoptedRegistersSurviveCaptureRestore()
        {
            var source = new Series2Pass9SaveState();
            BusinessEquipmentRegister hubEquipment = MakeEquipmentRegister("carpenter", withAsset: true);
            HouseholdFurnishingRegister hubFurnishings = MakeFurnishingRegister(7, withAsset: true, withBinding: true);
            var diag = new List<string>();

            source.SyncRegistersFromHub(new[] { hubEquipment }, new[] { hubFurnishings }, diag);
            Assert.IsTrue(ReferenceEquals(hubEquipment, source.EquipmentRegisters[0]), "Adoption must keep the live hub object before Capture.");
            Assert.IsTrue(ReferenceEquals(hubFurnishings, source.HouseholdFurnishingRegisters[0]));

            Series2Pass9SaveDto dto = source.Capture(diag);
            var target = new Series2Pass9SaveState();
            target.Restore(dto, null, diag);

            Assert.AreEqual(1, target.EquipmentRegisters.Count, "The adopted equipment register must survive the save round trip.");
            Assert.AreEqual("carpenter", target.EquipmentRegisters[0].BusinessInstanceId);
            Assert.AreEqual(1, target.EquipmentRegisters[0].Assets.Count);
            Assert.AreEqual("carpenter-asset-1", target.EquipmentRegisters[0].Assets[0].AssetId);
            Assert.AreEqual(1, target.HouseholdFurnishingRegisters.Count, "The adopted furnishing register must survive the save round trip.");
            Assert.AreEqual(7, target.HouseholdFurnishingRegisters[0].HouseholdId);
            Assert.AreEqual(1, target.HouseholdFurnishingRegisters[0].Assets.Count);
            Assert.AreEqual("household-7-bed-1", target.HouseholdFurnishingRegisters[0].Assets[0].AssetId);
            Assert.AreEqual(1, target.HouseholdFurnishingRegisters[0].Bindings.Count);
            Assert.AreEqual("household-7-binding-1", target.HouseholdFurnishingRegisters[0].Bindings[0].BindingId);
            Assert.AreEqual("household-7-bed-1", target.HouseholdFurnishingRegisters[0].Bindings[0].AssetId);
        }
    }
}
