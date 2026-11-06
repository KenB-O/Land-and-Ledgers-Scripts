using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using LandLedgers.Animals;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
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
            var availability = new AnimalAvailabilityAuthority(new EquipmentReservationService());
            Assert.That(HorseAuthority.Configure(horse, HorseUseCapabilities.GeneralPurpose, "farm-1", "stable-1", 0), Is.Null);
            Assert.That(HorseAuthority.TryReserveUse(
                horse, HorseUseKind.Draft, "shipment-1", availability, 0f, 3600f, out string reservationId), Is.Null);
            Assert.IsFalse(string.IsNullOrWhiteSpace(reservationId));
            Assert.That(HorseAuthority.TryReserveUse(
                horse, HorseUseKind.Mounted, "journey-1", availability, 0f, 3600f, out _), Does.Contain("already assigned"));
            HorseAuthority.ReleaseUse(horse, availability);
            Assert.That(horse.AnimalId.Kind, Is.EqualTo(EntityKind.Animal));
            // After release the horse is free again on the shared record.
            Assert.That(HorseAuthority.TryReserveUse(
                horse, HorseUseKind.Mounted, "journey-2", availability, 0f, 3600f, out _), Is.Null);
            HorseAuthority.ReleaseUse(horse, availability);
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
            var availability = new AnimalAvailabilityAuthority(new EquipmentReservationService());
            Assert.That(WagonAuthority.TryAssignDraftTeam(
                wagon, new List<AnimalState> { horse }, "P1", "S1", availability, 0f, 3600f), Is.Null);
            Assert.That(wagon.DraftAnimalIds, Has.Count.EqualTo(1));
            Assert.That(wagon.CurrentDriverPersonId, Is.EqualTo("P1"));
            Assert.That(wagon.CurrentShipmentId, Is.EqualTo("S1"));
            Assert.That(horse.CurrentUse, Is.EqualTo(HorseUseKind.Draft));
            WagonAuthority.ReleaseDraftTeam(wagon, new List<AnimalState> { horse }, availability);
            Assert.That(horse.CurrentUse, Is.EqualTo(HorseUseKind.None));
        }

        [Test]
        public void MountAndDismountRequireProximityAndPreserveIdentity()
        {
            var ids = new EntityIdRegistry();
            AnimalState horse = new AnimalRegistry(ids).RegisterAnimal(AnimalSpecies.Horse, "saddle", AnimalSex.Female, AnimalOwnerKind.Household, "H-1", 0, "opening stock");
            HorseAuthority.Configure(horse, HorseUseCapabilities.Saddle, "yard-1", "stable-1", 0);
            var availability = new AnimalAvailabilityAuthority(new EquipmentReservationService());
            Assert.That(TransportInteraction.Mount(horse, "P1", 0.5f, "yard-1", availability, 0f, out _), Does.Contain("close enough"));
            Assert.That(TransportInteraction.Mount(horse, "P1", 0.01f, "yard-1", availability, 0f, out TransportActivity activity), Is.Null);
            Assert.That(activity.HorseId, Is.EqualTo(horse.AnimalId.ToString()));
            Assert.That(TransportInteraction.Dismount(horse, activity, "store-1", availability), Is.Null);
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

        // Slice S6 (2026-11-06, Objective 20): pins the rotation half of
        // the alignment repair. The controller used to instantiate horse
        // and wagon with the SAME `facing` rotation, but the horse's
        // native forward is +Z while the wagon's length runs along its
        // local +X — so with a shared rotation the pair stood 90 degrees
        // apart. The controller now composes the horse's baked Y90 onto
        // `facing`; this test pins that the two rotations agree on one
        // travel axis. Unity Test Runner only (this file is
        // harness-excluded): the harness Quaternion shim stubs Euler /
        // composition to identity, so it cannot evaluate this.
        [Test]
        public void HorseRotation_ComposesFacingWithBakedYaw()
        {
            Quaternion facing = Quaternion.Euler(0f, 90f, 0f);
            Quaternion horseFacing = facing * Quaternion.Euler(0f, 90f, 0f);

            // The horse's forward (native +Z, rotated by the composed
            // yaw) must equal the travel axis — the wagon's length axis.
            Vector3 horseForward = horseFacing * Vector3.forward;
            Vector3 travelAxis = facing * Vector3.right;
            Assert.That(horseForward.x, Is.EqualTo(travelAxis.x).Within(0.001f));
            Assert.That(horseForward.y, Is.EqualTo(travelAxis.y).Within(0.001f));
            Assert.That(horseForward.z, Is.EqualTo(travelAxis.z).Within(0.001f));

            // And the wagon's length axis is the same vector.
            Vector3 wagonLengthAxis = facing * Vector3.right;
            Assert.That(wagonLengthAxis.x, Is.EqualTo(horseForward.x).Within(0.001f));
            Assert.That(wagonLengthAxis.y, Is.EqualTo(horseForward.y).Within(0.001f));
            Assert.That(wagonLengthAxis.z, Is.EqualTo(horseForward.z).Within(0.001f));
        }

        // Slice S6 (2026-11-06, Objective 20): documents the trap that
        // made the rotation repair necessary (Note-5). Instantiate with
        // an explicit rotation OVERWRITES the prefab root's baked
        // rotation — Horse.prefab's root Y90 never reaches the live game
        // through the controller's Instantiate call, which is why the
        // controller must compose the yaw at the call site instead of
        // relying on the prefab. Unity Test Runner only (this file is
        // harness-excluded). CODER VERIFICATION POINT (plan 06 §4.4):
        // if this behaves otherwise in the project's Unity version,
        // report it — do NOT weaken the rotation composition to make
        // any test pass.
        [Test]
        public void InstantiateWithRotation_DiscardsPrefabRootYaw()
        {
            GameObject horsePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Horse.prefab");
            Assert.That(horsePrefab, Is.Not.Null);
            // The prefab really does bake a Y90 on its root.
            Assert.That(Quaternion.Angle(horsePrefab.transform.rotation, Quaternion.Euler(0f, 90f, 0f)),
                Is.LessThan(0.01f), "Horse.prefab is expected to bake a Y90 root rotation.");

            var host = new GameObject(nameof(InstantiateWithRotation_DiscardsPrefabRootYaw));
            try
            {
                GameObject instance = Object.Instantiate(horsePrefab, Vector3.zero, Quaternion.identity, host.transform);
                Assert.That(Quaternion.Angle(instance.transform.rotation, Quaternion.identity),
                    Is.LessThan(0.01f),
                    "Instantiate with an explicit rotation must yield the given rotation, not the prefab's baked root yaw.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void AttachedStationaryHorseRemainsIdleUnderNeutralTransportRoot()
        {
            var ids = new EntityIdRegistry();
            AnimalState horse = new AnimalRegistry(ids).RegisterAnimal(
                AnimalSpecies.Horse, "draft", AnimalSex.Male, AnimalOwnerKind.Business, "B-1", 0, "test");
            horse.TransportState = HorseTransportState.AttachedToWagon;
            horse.AttachedWagonAssetId = "WG-idle-1";
            var wagon = new EquipmentAsset
            {
                AssetId = "WG-idle-1",
                Kind = WagonAuthority.DefaultKind,
                HitchingAnimalIds = new List<string> { horse.AnimalId.ToString() },
            };

            var host = new GameObject(nameof(AttachedStationaryHorseRemainsIdleUnderNeutralTransportRoot));
            try
            {
                Transform neutralRoot = new GameObject("Neutral Transport Root").transform;
                neutralRoot.SetParent(host.transform, false);
                GameObject horseObject = new GameObject("Horse View");
                horseObject.transform.SetParent(host.transform, false);
                HorseWorldView horseView = horseObject.AddComponent<HorseWorldView>();
                horseObject.AddComponent<TransportAnimatorState>();
                GameObject wagonObject = new GameObject("Wagon View");
                wagonObject.transform.SetParent(host.transform, false);
                WagonWorldView wagonView = wagonObject.AddComponent<WagonWorldView>();

                OpeningTransportPresentationController presentation = host.AddComponent<OpeningTransportPresentationController>();
                SetPrivateField(presentation, "transportGroup", neutralRoot);
                SetPrivateField(presentation, "activeHorseView", horseView);
                SetPrivateField(presentation, "activeWagonView", wagonView);

                presentation.RefreshPresentationState(horse, wagon);

                Assert.That(horseObject.transform.parent, Is.SameAs(neutralRoot));
                Assert.That(wagonObject.transform.parent, Is.SameAs(neutralRoot));
                Assert.That(horseObject.transform.parent, Is.SameAs(wagonObject.transform.parent));
                Assert.That(horseObject.GetComponent<TransportAnimatorState>().State, Is.EqualTo(TransportVisualState.Idle),
                    "A prepared but stationary horse must not loop its driving animation.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        // Slice R2 (2026-11-06, Objective 22): pins that the Man rig's
        // animator controller carries the seated DrivingWagon state the
        // director's TrySetPersonSitting cross-fades to, driven by the
        // clip imported from X Bot@Driving.fbx — and that the Locomotion
        // Blend Tree (the dismount restore target) still exists. Unity
        // Test Runner only (this file is harness-excluded in the main
        // harness): it loads the .controller asset, which Roslyn cannot.
        // No Roslyn test count rises in R2; this test is Codex's to run.
        [Test]
        public void ManAnimController_HasDrivingWagonSeatedState()
        {
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
                "Assets/Characters/Animations/Man Anim Controller.controller");
            Assert.That(controller, Is.Not.Null, "Man Anim Controller.controller must load.");
            Assert.That(controller.layers.Length, Is.GreaterThan(0), "The controller must have at least one layer.");
            UnityEditor.Animations.ChildAnimatorState[] states = controller.layers[0].stateMachine.states;

            AnimatorState driving = null;
            AnimatorState locomotion = null;
            foreach (UnityEditor.Animations.ChildAnimatorState child in states)
            {
                if (child.state == null) continue;
                if (child.state.name == "DrivingWagon") driving = child.state;
                if (child.state.name == "Locomotion") locomotion = child.state;
            }

            Assert.That(driving, Is.Not.Null, "The Man controller must have a DrivingWagon state.");
            Assert.That(driving.motion, Is.Not.Null, "The DrivingWagon state must have a motion.");
            Assert.That(AssetDatabase.GetAssetPath(driving.motion),
                Is.EqualTo("Assets/Characters/Animations/X Bot@Driving.fbx"),
                "The DrivingWagon motion must be the clip imported from X Bot@Driving.fbx.");

            Assert.That(locomotion, Is.Not.Null, "The Man controller must keep its Locomotion state (the dismount restore target).");
            Assert.That(locomotion.motion, Is.InstanceOf<BlendTree>(),
                "The Locomotion state must still be the Idle/Walking Blend Tree.");
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Expected presentation seam field " + fieldName);
            field.SetValue(target, value);
        }
    }
}
