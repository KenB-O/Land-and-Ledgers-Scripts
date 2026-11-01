using System.Collections.Generic;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Core
{
    /// <summary>
    /// HF-1: universal entity identity — typed kinds, per-kind sequences, never-reuse,
    /// save/load cursor stability, legacy int migration, and the custom-kind adoption path.
    /// </summary>
    [TestFixture]
    public sealed class EntityIdentityTests
    {
        [Test]
        public void Allocate_AssignsPerKindSequencesStartingAtZero()
        {
            var registry = new EntityIdRegistry();

            EntityId p0 = registry.Allocate(EntityKind.Person);
            EntityId p1 = registry.Allocate(EntityKind.Person);
            EntityId h0 = registry.Allocate(EntityKind.Household);

            Assert.AreEqual(EntityKind.Person, p0.Kind);
            Assert.AreEqual(0, p0.Id);
            Assert.AreEqual(1, p1.Id);
            Assert.AreEqual(EntityKind.Household, h0.Kind);
            Assert.AreEqual(0, h0.Id);
        }

        [Test]
        public void EntityIds_AcrossKinds_AreDistinct()
        {
            var registry = new EntityIdRegistry();

            EntityId person = registry.Allocate(EntityKind.Person);
            EntityId household = registry.Allocate(EntityKind.Household);

            Assert.AreNotEqual(person, household);
            Assert.AreEqual("P0", person.ToString());
            Assert.AreEqual("H0", household.ToString());
        }

        [Test]
        public void Sequences_OnlyAdvance_NeverReused()
        {
            var registry = new EntityIdRegistry();

            EntityId first = registry.Allocate(EntityKind.Animal);
            // Logical "delete": nothing rewinds the sequence. The next allocation must move forward.
            EntityId second = registry.Allocate(EntityKind.Animal);

            Assert.Greater(second.Id, first.Id);
            Assert.AreEqual(2, registry.PeekNext(EntityKind.Animal));
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesCursors()
        {
            var registry = new EntityIdRegistry();
            registry.Allocate(EntityKind.Person);
            registry.Allocate(EntityKind.Person);
            registry.Allocate(EntityKind.Animal);

            List<EntityIdCursor> snapshot = registry.SnapshotCursors();

            var restored = new EntityIdRegistry();
            List<string> diagnostics = restored.RestoreCursors(snapshot);

            Assert.IsEmpty(diagnostics);
            Assert.AreEqual("P2", restored.Allocate(EntityKind.Person).ToString());
            Assert.AreEqual("A1", restored.Allocate(EntityKind.Animal).ToString());
        }

        [Test]
        public void RestoreCursors_RejectsDuplicates_And_KeepsFirst()
        {
            var registry = new EntityIdRegistry();
            var cursors = new List<EntityIdCursor>
            {
                new EntityIdCursor(EntityKind.Person, 10),
                new EntityIdCursor(EntityKind.Person, 999),
            };

            List<string> diagnostics = registry.RestoreCursors(cursors);

            Assert.IsNotEmpty(diagnostics);
            Assert.AreEqual(10, registry.PeekNext(EntityKind.Person));
        }

        [Test]
        public void LegacyMigration_MapsUnkindedInt_WithoutRenumbering()
        {
            EntityId migrated = EntityId.FromLegacyInt(EntityKind.Person, 42);

            Assert.AreEqual(EntityKind.Person, migrated.Kind);
            Assert.AreEqual(42, migrated.Id);
            Assert.AreEqual("P42", migrated.ToString());
        }

        [Test]
        public void SyncFromLegacy_NeverMovesCursorBackward()
        {
            var registry = new EntityIdRegistry();
            registry.SeedKind(EntityKind.Building, 100);

            registry.SyncFromLegacy(EntityKind.Building, 50); // stale value must not rewind
            Assert.AreEqual(100, registry.PeekNext(EntityKind.Building));

            registry.SyncFromLegacy(EntityKind.Building, 150); // newer value advances
            Assert.AreEqual(150, registry.PeekNext(EntityKind.Building));
        }

        [Test]
        public void CustomKind_Registration_AdoptionPath()
        {
            const int vehicleKind = 1000;
            EntityKindCatalog.RegisterCustomKind(vehicleKind, "Vehicle", "V");

            var registry = new EntityIdRegistry();
            EntityId vehicle = registry.Allocate((EntityKind)vehicleKind);

            Assert.AreEqual("V0", vehicle.ToString());
            Assert.IsTrue(EntityKindCatalog.IsKnown((EntityKind)vehicleKind));
        }

        [Test]
        public void CustomKind_RejectsReservedRange_And_Duplicates()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => EntityKindCatalog.RegisterCustomKind(999, "Reserved", "RSV"));

            const int anotherKind = 1001;
            EntityKindCatalog.RegisterCustomKind(anotherKind, "Another", "AN");
            Assert.Throws<System.InvalidOperationException>(
                () => EntityKindCatalog.RegisterCustomKind(anotherKind, "AnotherAgain", "AG"));
        }

        [Test]
        public void Invalid_EntityId_Detected()
        {
            Assert.IsFalse(EntityId.Invalid.IsValid);
            Assert.AreEqual("Invalid", EntityId.Invalid.ToString());
            Assert.Throws<System.ArgumentException>(() => EntityId.For(EntityKind.Unspecified, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => EntityId.For(EntityKind.Person, -1));
        }
    }
}
