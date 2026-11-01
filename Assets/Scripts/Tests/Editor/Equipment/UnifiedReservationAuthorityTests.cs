using System.Collections.Generic;
using NUnit.Framework;

namespace LandLedgers.Economy.Equipment.Tests
{
    /// <summary>
    /// Pass IV Worker 2: unified reservation authority correctness —
    /// double-booking refusal, atomic rollback, idempotent release, time
    /// windows, capacity floors, and legacy API compatibility.
    /// </summary>
    [TestFixture]
    public sealed class UnifiedReservationAuthorityTests
    {
        private EquipmentReservationService authority;

        [SetUp]
        public void SetUp()
        {
            authority = new EquipmentReservationService();
        }

        private static ReservationRequest Request(string ownerTaskId, string ownerLabel,
            params (string key, float qty, float start, float end)[] items)
        {
            var request = new ReservationRequest(ownerTaskId, ownerLabel);
            foreach (var item in items)
                request.Items.Add(new ReservationItem(item.key, item.qty, item.start, item.end));
            return request;
        }

        [Test]
        public void DoubleBooking_RefusedAndNamesHolder()
        {
            string refusal = authority.TryReserveAll(
                Request("task-a", "Plow field 7", ("asset:plow-1", 1f, 0f, 100f)), out var handleA);
            Assert.IsNull(refusal, "First reservation must succeed: " + refusal);
            Assert.IsNotNull(handleA);

            string conflict = authority.TryReserveAll(
                Request("task-b", "Plow field 9", ("asset:plow-1", 1f, 50f, 150f)), out var handleB);
            Assert.IsNotNull(conflict, "Overlapping second reservation must be refused.");
            Assert.IsNull(handleB, "Refused request must not produce a handle.");
            Assert.IsTrue(conflict.Contains("Plow field 7") || conflict.Contains("task-a"),
                "Refusal must name the conflicting holder: " + conflict);
        }

        [Test]
        public void AtomicRollback_PartialConflictReservesNothing()
        {
            Assert.IsNull(authority.TryReserveAll(
                Request("task-a", "First", ("asset:plow-1", 1f, 0f, 100f)), out _));

            // Second request: first item free, second conflicts — nothing may be held.
            string conflict = authority.TryReserveAll(
                Request("task-b", "Second",
                    ("asset:harrow-1", 1f, 0f, 100f),
                    ("asset:plow-1", 1f, 0f, 100f)),
                out var handleB);
            Assert.IsNotNull(conflict, "Conflicting multi-item request must be refused.");
            Assert.IsNull(handleB);
            Assert.IsFalse(authority.IsReserved("asset:harrow-1", 10f),
                "Atomic rollback: the free item must not be held after refusal.");
            Assert.AreEqual(1, authority.LiveReservationCount,
                "Only the first task's reservation may exist.");
        }

        [Test]
        public void NonOverlappingWindows_DoNotConflict()
        {
            Assert.IsNull(authority.TryReserveAll(
                Request("task-a", "Morning", ("asset:plow-1", 1f, 0f, 100f)), out _));
            Assert.IsNull(authority.TryReserveAll(
                Request("task-b", "Afternoon", ("asset:plow-1", 1f, 100f, 200f)), out _),
                "Adjacent windows must not conflict.");
            Assert.IsTrue(authority.IsReserved("asset:plow-1", 50f));
            Assert.IsTrue(authority.IsReserved("asset:plow-1", 150f));
            Assert.IsFalse(authority.IsReserved("asset:plow-1", 200f));
        }

        [Test]
        public void Release_IsIdempotent()
        {
            authority.TryReserveAll(
                Request("task-a", "Work", ("asset:plow-1", 1f, 0f, 100f)), out var handle);
            ((IUnifiedReservationAuthority)authority).Release(handle.ReservationId);
            Assert.IsFalse(authority.IsReserved("asset:plow-1", 50f));
            // Second release of the same id: no-op, no throw.
            ((IUnifiedReservationAuthority)authority).Release(handle.ReservationId);
            ((IUnifiedReservationAuthority)authority).Release("res-does-not-exist");
            Assert.AreEqual(0, authority.LiveReservationCount);
        }

        [Test]
        public void ReleaseAllForTask_ReleasesOwnedOnly()
        {
            authority.TryReserveAll(Request("task-a", "A", ("asset:plow-1", 1f, 0f, 100f)), out _);
            authority.TryReserveAll(Request("task-a", "A", ("asset:harrow-1", 1f, 0f, 100f)), out _);
            authority.TryReserveAll(Request("task-b", "B", ("kit:field-kit-1", 1f, 0f, 100f)), out _);

            authority.ReleaseAllForTask("task-a");
            Assert.IsFalse(authority.IsReserved("asset:plow-1", 50f));
            Assert.IsFalse(authority.IsReserved("asset:harrow-1", 50f));
            Assert.IsTrue(authority.IsReserved("kit:field-kit-1", 50f),
                "Another task's reservation must survive.");
            // Unknown task: no-op.
            authority.ReleaseAllForTask("task-ghost");
            Assert.AreEqual(1, authority.LiveReservationCount);
        }

        [Test]
        public void AvailableCapacity_NeverNegative_PartialCapacity()
        {
            Assert.IsNull(authority.RegisterResourceCapacity("workstation:oven-bay", 4f));
            Assert.IsNull(authority.TryReserveAll(
                Request("task-a", "Batches", ("workstation:oven-bay", 3f, 0f, 100f)), out _));
            Assert.AreEqual(1f, authority.AvailableCapacity("workstation:oven-bay", 50f), 1e-4f);

            string conflict = authority.TryReserveAll(
                Request("task-b", "More", ("workstation:oven-bay", 2f, 0f, 100f)), out _);
            Assert.IsNotNull(conflict, "Over-capacity must be refused.");

            // Force-check the floor: reserve exactly to capacity, then confirm zero not negative.
            Assert.IsNull(authority.TryReserveAll(
                Request("task-c", "Last", ("workstation:oven-bay", 1f, 0f, 100f)), out _));
            Assert.AreEqual(0f, authority.AvailableCapacity("workstation:oven-bay", 50f), 1e-4f);
            Assert.IsTrue(authority.AvailableCapacity("workstation:oven-bay", 50f) >= 0f);
        }

        [Test]
        public void SameOwner_StagedReservationDoesNotConflictWithItself()
        {
            Assert.IsNull(authority.TryReserveAll(
                Request("task-a", "Stage 1", ("asset:plow-1", 1f, 0f, 100f)), out _));
            // Same task extending its window: one logical holder, no self-conflict.
            Assert.IsNull(authority.TryReserveAll(
                Request("task-a", "Stage 2", ("asset:plow-1", 1f, 50f, 150f)), out _),
                "A task must be able to stage reservations on its own holds.");
        }

        [Test]
        public void AllKeyPrefixes_Supported()
        {
            string[] keys =
            {
                ReservationKeyConventions.Asset("a1"),
                ReservationKeyConventions.Kit("k1"),
                ReservationKeyConventions.Workstation("w1"),
                ReservationKeyConventions.Person("person-5"),
                ReservationKeyConventions.Lot("lot-1"),
                ReservationKeyConventions.Workspace("shop"),
                ReservationKeyConventions.Animal("animal-2"),
                ReservationKeyConventions.MotivePower("horse-team"),
            };
            var request = new ReservationRequest("task-all", "Everything");
            foreach (string key in keys)
                request.Items.Add(new ReservationItem(key, 1f, 0f, 60f));
            Assert.IsNull(authority.TryReserveAll(request, out var handle), "All key prefixes must reserve.");
            Assert.AreEqual(keys.Length, handle.Items.Count);
            foreach (string key in keys)
                Assert.IsTrue(authority.IsReserved(key, 30f), "Key held: " + key);
        }

        [Test]
        public void MalformedRequests_RefusedWithoutSideEffects()
        {
            Assert.IsNotNull(authority.TryReserveAll(null, out _));
            Assert.IsNotNull(authority.TryReserveAll(new ReservationRequest("", "No owner"), out _));
            var empty = new ReservationRequest("task-x", "Empty");
            Assert.IsNotNull(authority.TryReserveAll(empty, out _));

            var badQty = Request("task-x", "Bad", ("asset:plow-1", 0f, 0f, 10f));
            Assert.IsNotNull(authority.TryReserveAll(badQty, out _));
            var badWindow = Request("task-x", "Bad", ("asset:plow-1", 1f, 10f, 10f));
            Assert.IsNotNull(authority.TryReserveAll(badWindow, out _));
            Assert.AreEqual(0, authority.LiveReservationCount, "Malformed requests reserve nothing.");
        }

        // ---------- legacy API compatibility (RentalFleet path) ----------

        [Test]
        public void LegacyApi_ReserveReleaseRoundTrip()
        {
            var diag = new List<string>();
            Assert.IsNull(authority.Reserve("asset:wagon-9", "renter-3", "rental: wagon-9"));
            Assert.IsTrue(authority.IsReserved("asset:wagon-9"));
            Assert.AreEqual(1, authority.ReservedCount);

            string refusal = authority.Reserve("asset:wagon-9", "renter-4", "rental: wagon-9");
            Assert.IsNotNull(refusal, "Legacy double-booking must be refused.");
            Assert.IsTrue(refusal.Contains("renter-3"), "Legacy refusal names the holder: " + refusal);

            Assert.IsNull(authority.ReserveAll(
                new List<string> { "asset:a", "asset:b" }, "renter-3", "rental", diag));
            string conflict = authority.ReserveAll(
                new List<string> { "asset:c", "asset:a" }, "renter-5", "rental", diag);
            Assert.IsNotNull(conflict, "Legacy atomic set must refuse on conflict.");
            Assert.IsFalse(authority.IsReserved("asset:c"), "Legacy atomic rollback holds nothing.");

            authority.Release("asset:wagon-9");
            Assert.IsFalse(authority.IsReserved("asset:wagon-9"));
            authority.ReleaseAll(new List<string> { "asset:a", "asset:b" });
            Assert.AreEqual(0, authority.ReservedCount);
        }

        [Test]
        public void LegacyAndWindowedHolds_ShareOneKeySpace()
        {
            // Legacy open-ended hold blocks a windowed request on the same key.
            Assert.IsNull(authority.Reserve("asset:plow-1", "renter-1", "rental"));
            string conflict = authority.TryReserveAll(
                Request("task-b", "Plowing", ("asset:plow-1", 1f, 0f, 100f)), out _);
            Assert.IsNotNull(conflict, "Legacy and windowed holds share one key space.");
            authority.Release("asset:plow-1");
            Assert.IsNull(authority.TryReserveAll(
                Request("task-b", "Plowing", ("asset:plow-1", 1f, 0f, 100f)), out _));
        }

        [Test]
        public void ReservationsForTask_ReturnsOwnedHandles()
        {
            authority.TryReserveAll(Request("task-a", "A", ("asset:plow-1", 1f, 0f, 100f)), out _);
            authority.TryReserveAll(Request("task-a", "A", ("kit:k1", 1f, 0f, 100f)), out _);
            var owned = authority.ReservationsForTask("task-a");
            Assert.AreEqual(2, owned.Count);
            Assert.AreEqual(0, authority.ReservationsForTask("task-ghost").Count);
        }
    }
}
