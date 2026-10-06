using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LandLedgers.Animals;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Transport;
using LandLedgers.Economy;
using LandLedgers.Primitives;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class TransportFoundationTests
    {
        [Test]
        public void HorseUsesAnimalIdentityAndCannotBeDoubleBooked()
        {
            var ids = new EntityIdRegistry();
            AnimalState horse = new AnimalRegistry(ids).RegisterAnimal(
                AnimalSpecies.Horse, "draft", AnimalSex.Male, AnimalOwnerKind.Household, "H-1", 0, "opening stock");
            Assert.That(HorseAuthority.Configure(horse, HorseUseCapabilities.GeneralPurpose, "farm-1", "stable-1", 0), Is.Null);
            Assert.That(HorseAuthority.TryReserveUse(horse, HorseUseKind.Draft, "shipment-1"), Is.Null);
            Assert.That(HorseAuthority.TryReserveUse(horse, HorseUseKind.Mounted, "journey-1"), Does.Contain("already assigned"));
            HorseAuthority.ReleaseUse(horse);
            Assert.That(horse.AnimalId.Kind, Is.EqualTo(EntityKind.Animal));
        }

        [Test]
        public void StableCapacityIsFiniteAndWagonUsesMassAndVolume()
        {
            var housing = new HorseHousingRegister();
            Assert.That(housing.AddHousing("stable-1", 1), Is.Null);
            Assert.That(housing.Assign("stable-1", "A1"), Is.Null);
            Assert.That(housing.Assign("stable-1", "A2"), Does.Contain("full"));

            var wagon = new EquipmentAsset { AssetId = "WG-1", Kind = WagonAuthority.DefaultKind, Condition01 = 1f };
            Assert.That(WagonAuthority.Configure(wagon, "GeneralFarmFreight", 450f, 4f, "farm-yard-1"), Is.Null);
            Assert.That(WagonAuthority.CanCarry(wagon, 449f, 3.9f), Is.True);
            Assert.That(WagonAuthority.CanCarry(wagon, 451f, 1f), Is.False);
            Assert.That(WagonAuthority.CanCarry(wagon, 1f, 4.1f), Is.False);
        }

        [Test]
        public void DraftAssignmentKeepsHorseWagonAndDriverSeparate()
        {
            var ids = new EntityIdRegistry();
            var registry = new AnimalRegistry(ids);
            AnimalState horse = registry.RegisterAnimal(AnimalSpecies.Horse, "draft", AnimalSex.Male, AnimalOwnerKind.Business, "B-1", 0, "purchase");
            HorseAuthority.Configure(horse, HorseUseCapabilities.Draft, "yard-1", "stable-1", 0);
            var wagon = new EquipmentAsset { AssetId = "WG-2", Kind = WagonAuthority.DefaultKind, Condition01 = 1f };
            WagonAuthority.Configure(wagon, "GeneralFarmFreight", 450f, 4f, "yard-1");
            Assert.That(WagonAuthority.TryAssignDraftTeam(wagon, new List<AnimalState> { horse }, "P1", "S1"), Is.Null);
            Assert.That(wagon.DraftAnimalIds, Has.Count.EqualTo(1));
            Assert.That(wagon.CurrentDriverPersonId, Is.EqualTo("P1"));
            Assert.That(wagon.CurrentShipmentId, Is.EqualTo("S1"));
            Assert.That(horse.CurrentUse, Is.EqualTo(HorseUseKind.Draft));
            WagonAuthority.ReleaseDraftTeam(wagon, new List<AnimalState> { horse });
            Assert.That(horse.CurrentUse, Is.EqualTo(HorseUseKind.None));
        }

        [Test]
        public void MountAndDismountRequireProximityAndPreserveIdentity()
        {
            var ids = new EntityIdRegistry();
            AnimalState horse = new AnimalRegistry(ids).RegisterAnimal(AnimalSpecies.Horse, "saddle", AnimalSex.Female, AnimalOwnerKind.Household, "H-1", 0, "opening stock");
            HorseAuthority.Configure(horse, HorseUseCapabilities.Saddle, "yard-1", "stable-1", 0);
            Assert.That(TransportInteraction.Mount(horse, "P1", 0.5f, "yard-1", out _), Does.Contain("close enough"));
            Assert.That(TransportInteraction.Mount(horse, "P1", 0.01f, "yard-1", out TransportActivity activity), Is.Null);
            Assert.That(activity.HorseId, Is.EqualTo(horse.AnimalId.ToString()));
            Assert.That(TransportInteraction.Dismount(horse, activity, "store-1"), Is.Null);
            Assert.That(horse.PhysicalLocationId, Is.EqualTo("store-1"));
            Assert.That(horse.CurrentUse, Is.EqualTo(HorseUseKind.None));
        }

        [Test]
        public void WagonRegistryRoundTripsAssetIdentityAndTransportState()
        {
            var registry = new TransportAssetRegistry();
            var wagon = new EquipmentAsset { AssetId = "WG-save-1", Kind = WagonAuthority.DefaultKind, OwnerKind = "business", OwnerId = "B-1", Condition01 = .8f };
            WagonAuthority.Configure(wagon, "GeneralFarmFreight", 450f, 4f, "yard-1");
            wagon.CurrentShipmentId = "S-1";
            Assert.That(registry.Register(wagon), Is.Null);
            var restored = new TransportAssetRegistry();
            restored.LoadFromSaveDto(registry.CaptureSaveDto());
            Assert.That(restored.Get("WG-save-1"), Is.Not.Null);
            Assert.That(restored.Get("WG-save-1").CurrentShipmentId, Is.EqualTo("S-1"));
            Assert.That(restored.Get("WG-save-1").PayloadMassCapacityKg, Is.EqualTo(450f));
        }

        [Test]
        public void PhysicalShipmentProjectionRoundTripsLocationAndProgress()
        {
            var shipment = new LogisticsShipmentState
            {
                shipmentId = "S-transport-save-1",
                state = LogisticsShipmentStatus.InTransit,
                physicalTransportBound = true,
                physicalWagonAssetId = "WG-save-2",
                physicalDriverPersonId = "42",
                physicalDraftAnimalIds = new List<string> { "A-1" },
                physicalLocationId = "shipment:S-transport-save-1:in-transit",
                physicalProgress01 = 0.42f,
            };

            LogisticsShipmentState restored = LogisticsShipmentState.FromSaveDto(shipment.CaptureSaveDto());
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.physicalTransportBound, Is.True);
            Assert.That(restored.physicalWagonAssetId, Is.EqualTo("WG-save-2"));
            Assert.That(restored.physicalDraftAnimalIds, Is.EqualTo(new[] { "A-1" }));
            Assert.That(restored.physicalLocationId, Is.EqualTo("shipment:S-transport-save-1:in-transit"));
            Assert.That(restored.physicalProgress01, Is.EqualTo(0.42f).Within(0.0001f));
        }

        [Test]
        public void WagonTripWearChangesConditionWithoutDestroyingAssetIdentity()
        {
            var wagon = new EquipmentAsset
            {
                AssetId = "WG-wear-1",
                Kind = WagonAuthority.DefaultKind,
                Condition01 = 1f,
            };
            Assert.That(WagonAuthority.Configure(wagon, "GeneralFarmFreight", 450f, 4f, "yard-1"), Is.Null);

            WagonAuthority.ApplyTripWear(wagon, 100f, 0.75f);

            Assert.That(wagon.AssetId, Is.EqualTo("WG-wear-1"));
            Assert.That(wagon.Condition01, Is.LessThan(1f));
            Assert.That(wagon.MaintenanceLog, Has.Count.EqualTo(1));
        }

        [Test]
        public void ProductionTransportPrefabsExposeIdentityAnchorsAndDrivingController()
        {
            GameObject horse = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Horse.prefab");
            GameObject wagon = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Wagon.prefab");
            Assert.That(horse, Is.Not.Null);
            Assert.That(wagon, Is.Not.Null);
            Assert.That(horse.GetComponent<HorseWorldView>(), Is.Not.Null);
            Assert.That(wagon.GetComponentInChildren<WagonWorldView>(true), Is.Not.Null);
            Assert.That(horse.transform.Find("RiderMount"), Is.Not.Null);
            Assert.That(horse.transform.Find("DismountLeft"), Is.Not.Null);
            Assert.That(horse.transform.Find("DismountRight"), Is.Not.Null);
            Assert.That(wagon.transform.Find("DriverSeat"), Is.Not.Null);
            Assert.That(wagon.transform.Find("HitchPoint"), Is.Not.Null);
            Assert.That(wagon.transform.Find("CargoRoot"), Is.Not.Null);

            Animator animator = wagon.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.applyRootMotion, Is.False);
            Assert.That(animator.runtimeAnimatorController, Is.Not.Null);
            Assert.That(animator.runtimeAnimatorController.name, Is.EqualTo("Transport Anim Controller"));
        }
    }
}
