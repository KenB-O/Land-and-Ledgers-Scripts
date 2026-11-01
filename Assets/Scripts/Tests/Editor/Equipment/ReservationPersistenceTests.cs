using System.Collections.Generic;
using LandLedgers.Economy.DraftPower;
using LandLedgers.Persistence;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Economy.Equipment.Tests
{
    /// <summary>
    /// Pass IV Worker 2: save/load round-trips — the unified authority, draft
    /// holds, and capacity registrations survive; a restored game never
    /// double-books and never silently drops a live reservation.
    /// </summary>
    [TestFixture]
    public sealed class ReservationPersistenceTests
    {
        [Test]
        public void Authority_RoundTrip_PreservesHoldsAndCapacities()
        {
            var authority = new EquipmentReservationService();
            Assert.IsNull(authority.RegisterResourceCapacity("workstation:oven-bay", 4f));

            var request = new ReservationRequest("task-a", "Baking");
            request.Items.Add(new ReservationItem("workstation:oven-bay", 3f, 0f, 3600f));
            request.Items.Add(new ReservationItem("asset:plow-1", 1f, 0f, 3600f));
            Assert.IsNull(authority.TryReserveAll(request, out var handle));

            EquipmentReservationAuthoritySaveDto dto = authority.CaptureSaveDto();

            var restored = new EquipmentReservationService();
            restored.LoadFromSaveDto(dto);

            Assert.IsTrue(restored.IsReserved("asset:plow-1", 100f),
                "Restored game keeps the live reservation.");
            Assert.AreEqual(1f, restored.AvailableCapacity("workstation:oven-bay", 100f), 1e-4f,
                "Capacity limits survive the round-trip.");
            Assert.AreEqual(1, restored.LiveReservationCount);

            // No double-booking after restore: the held plow refuses a rival.
            var rival = new ReservationRequest("task-b", "Rival");
            rival.Items.Add(new ReservationItem("asset:plow-1", 1f, 100f, 200f));
            string conflict = restored.TryReserveAll(rival, out _);
            Assert.IsNotNull(conflict, "Restored reservations still refuse double-booking.");
        }

        [Test]
        public void Authority_LoadIsIdempotent_ClearThenLoad()
        {
            var authority = new EquipmentReservationService();
            var request = new ReservationRequest("task-a", "Work");
            request.Items.Add(new ReservationItem("asset:plow-1", 1f, 0f, 100f));
            Assert.IsNull(authority.TryReserveAll(request, out _));
            EquipmentReservationAuthoritySaveDto dto = authority.CaptureSaveDto();

            var restored = new EquipmentReservationService();
            restored.LoadFromSaveDto(dto);
            restored.LoadFromSaveDto(dto); // twice: same state, no duplicates
            Assert.AreEqual(1, restored.LiveReservationCount,
                "Idempotent restore: loading twice yields the same state.");

            restored.LoadFromSaveDto(null); // clear-then-load with nothing: empty
            Assert.AreEqual(0, restored.LiveReservationCount);
        }

        [Test]
        public void Authority_LoadSkipsConflictingDuplicates()
        {
            var dto = new EquipmentReservationAuthoritySaveDto();
            var items = new List<ReservationItem>
                { new ReservationItem("asset:plow-1", 1f, 0f, 100f) };
            dto.handles.Add(new ReservationHandle("res-dup", "task-a", items));
            dto.handles.Add(new ReservationHandle("res-dup", "task-a", items)); // duplicate id
            var rivalItems = new List<ReservationItem>
                { new ReservationItem("asset:plow-1", 1f, 50f, 150f) };
            dto.handles.Add(new ReservationHandle("res-rival", "task-b", rivalItems)); // conflicts

            var restored = new EquipmentReservationService();
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(1, restored.LiveReservationCount,
                "Duplicates and conflicting stale entries are skipped, never force-applied.");
        }

        [Test]
        public void DraftPower_RoundTrip_HoldsSurviveAndReleaseWorks()
        {
            var authority = new EquipmentReservationService();
            var draft = new DraftPowerService(authority);
            var diag = new List<string>();
            var animals = new List<EntityId>
            {
                EntityId.For(EntityKind.Animal, 11),
                EntityId.For(EntityKind.Animal, 12),
            };
            var drivers = new List<EntityId> { EntityId.For(EntityKind.Person, 21) };

            Assert.IsTrue(draft.TryReserveAnimals(animals, 2, null, "plowing", out _, diag),
                string.Join("; ", diag));
            Assert.IsTrue(draft.TryReserveDriver(drivers, out _, diag), string.Join("; ", diag));

            EquipmentReservationAuthoritySaveDto authorityDto = authority.CaptureSaveDto();
            DraftPowerServiceSaveDto draftDto = draft.CaptureSaveDto();

            var restoredAuthority = new EquipmentReservationService();
            restoredAuthority.LoadFromSaveDto(authorityDto);
            var restoredDraft = new DraftPowerService(restoredAuthority);
            restoredDraft.LoadFromSaveDto(draftDto);

            Assert.IsTrue(restoredDraft.IsAnimalReserved(EntityId.For(EntityKind.Animal, 11)),
                "Draft animal hold survives the round-trip.");
            Assert.IsTrue(restoredDraft.IsDriverReserved(EntityId.For(EntityKind.Person, 21)),
                "Driver hold survives the round-trip.");
            Assert.AreEqual(3, restoredDraft.HoldCount,
                "Hold index restored (2 animals + 1 driver).");

            // A rival cannot take the held animals after restore...
            var rivalAnimals = new List<EntityId>
            {
                EntityId.For(EntityKind.Animal, 11),
                EntityId.For(EntityKind.Animal, 13),
            };
            Assert.IsFalse(restoredDraft.TryReserveAnimals(rivalAnimals, 2, null, "hauling",
                out _, diag), "No double-booking after restore.");

            // ...and release still works through the restored index.
            restoredDraft.ReleaseAnimals(new List<EntityId> { EntityId.For(EntityKind.Animal, 11) });
            Assert.IsFalse(restoredDraft.IsAnimalReserved(EntityId.For(EntityKind.Animal, 11)));
            Assert.IsTrue(restoredDraft.IsAnimalReserved(EntityId.For(EntityKind.Animal, 12)),
                "Releasing one animal keeps the other held.");
        }

        [Test]
        public void CapacityRegistrations_RoundTrip()
        {
            var authority = new EquipmentReservationService();
            var capacities = new EquipmentCapacityService(authority);
            EquipmentConditionProfiles.RegisterAll(capacities);

            EquipmentCapacityServiceSaveDto dto = capacities.CaptureSaveDto();
            Assert.IsTrue(dto.registrations.Count > 0, "Kind profiles captured.");

            var restoredAuthority = new EquipmentReservationService();
            var restored = new EquipmentCapacityService(restoredAuthority);
            restored.LoadFromSaveDto(dto);

            // Authority capacity limits restore in step (oven: 4 batch slots).
            Assert.AreEqual(4f, restoredAuthority.AvailableCapacity("kind:oven", 0f), 1e-4f,
                "PassiveProcess batch capacity restores with the registration.");
            Assert.AreEqual(0.75f, restored.UsableCapacityFraction("kind:oven", 0.39f), 1e-4f,
                "Condition profile restores with the registration.");
        }
    }
}
