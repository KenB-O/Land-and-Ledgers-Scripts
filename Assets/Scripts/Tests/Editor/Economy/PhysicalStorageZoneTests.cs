using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy.Storage;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    public sealed class PhysicalStorageZoneTests
    {
        [Test]
        public void UnknownCapacityAndLoadRemainExplicitWithoutInventedLimits()
        {
            PhysicalStorageZone zone = MakeZone(StorageCapacityLimit.Unknown, StorageCapacityLimit.Unknown);
            StoredLotReference lot = Lot("business-inventory", "lot-1", StorageLoadMeasurement.Unknown);

            Assert.IsTrue(zone.TryPlace(lot, out string refusal), refusal);
            Assert.AreEqual(1, zone.Placements.Count);
            Assert.AreEqual("site-1", zone.Definition.LocationId);
            Assert.AreEqual(StorageExposure.Outdoor, zone.Definition.Exposure);

            StorageCapacitySnapshot snapshot = zone.CapacitySnapshot;
            Assert.IsFalse(snapshot.MassCapacityKg.IsKnown);
            Assert.IsFalse(snapshot.VolumeCapacityCubicMeters.IsKnown);
            Assert.IsFalse(snapshot.CurrentMassKnown);
            Assert.IsFalse(snapshot.CurrentVolumeKnown);
        }

        [Test]
        public void BoundedMassAndVolumeAreEnforcedAgainstExistingLotReferences()
        {
            PhysicalStorageZone zone = MakeZone(StorageCapacityLimit.Known(10f), StorageCapacityLimit.Known(2f));
            Assert.IsTrue(zone.TryPlace(Lot("inventory", "lot-1", StorageLoadMeasurement.MassAndVolume(6f, 1.2f)), out _));

            Assert.IsFalse(zone.TryPlace(Lot("inventory", "lot-2", StorageLoadMeasurement.MassAndVolume(5f, 0.2f)), out string massRefusal));
            StringAssert.Contains("Mass capacity exceeded", massRefusal);
            Assert.AreEqual(1, zone.Placements.Count);

            Assert.IsFalse(zone.TryPlace(Lot("inventory", "lot-3", StorageLoadMeasurement.MassAndVolume(1f, 0.9f)), out string volumeRefusal));
            StringAssert.Contains("Volume capacity exceeded", volumeRefusal);
            Assert.AreEqual(1, zone.Placements.Count);

            Assert.IsTrue(zone.TryPlace(Lot("inventory", "lot-4", StorageLoadMeasurement.MassAndVolume(4f, 0.8f)), out _));
            Assert.AreEqual(10f, zone.CapacitySnapshot.CurrentMassKg, 0.001f);
            Assert.AreEqual(2f, zone.CapacitySnapshot.CurrentVolumeCubicMeters, 0.001f);
        }

        [Test]
        public void BoundedDimensionRefusesWhenIncomingMeasurementIsUnknown()
        {
            PhysicalStorageZone zone = MakeZone(StorageCapacityLimit.Known(100f), StorageCapacityLimit.Unknown);

            Assert.IsFalse(zone.TryPlace(Lot("inventory", "lot-1", StorageLoadMeasurement.VolumeOnly(0.5f)), out string refusal));
            StringAssert.Contains("incoming lot mass is unknown", refusal);
            Assert.AreEqual(0, zone.Placements.Count);
        }

        [Test]
        public void BoundedDimensionRefusesWhenExistingPlacementCannotBeMeasured()
        {
            PhysicalStorageZone zone = MakeZone(StorageCapacityLimit.Unknown, StorageCapacityLimit.Known(3f));
            // This represents a previously serialized placement from before
            // the zone had a volume limit. A newly placed lot with unknown
            // volume is correctly rejected by TryPlace, so it cannot be used
            // to construct this legacy/restored state through the public API.
            FieldInfo placementsField = typeof(PhysicalStorageZone).GetField(
                "placements", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(placementsField, "The serialized placement field is part of this restore scenario.");
            var restoredPlacements = (List<StoredLotReference>)placementsField.GetValue(zone);
            restoredPlacements.Add(Lot("inventory", "lot-1", StorageLoadMeasurement.MassOnly(2f)));

            Assert.IsFalse(zone.TryPlace(Lot("inventory", "lot-2", StorageLoadMeasurement.VolumeOnly(1f)), out string refusal));
            StringAssert.Contains("existing placed lot has unknown volume", refusal);
            Assert.AreEqual(1, zone.Placements.Count);
        }

        [Test]
        public void LotReferenceIsUniquePerInventoryAuthorityAndCanBeRemoved()
        {
            PhysicalStorageZone zone = MakeZone(StorageCapacityLimit.Unknown, StorageCapacityLimit.Unknown);
            StorageLoadMeasurement load = StorageLoadMeasurement.MassOnly(1f);
            Assert.IsTrue(zone.TryPlace(Lot("inventory-a", "lot-1", load), out _));

            Assert.IsFalse(zone.TryPlace(Lot("inventory-a", "lot-1", load), out string duplicateRefusal));
            StringAssert.Contains("already placed", duplicateRefusal);

            // Lot identifiers are scoped by their owning inventory authority.
            Assert.IsTrue(zone.TryPlace(Lot("inventory-b", "lot-1", load), out _));
            Assert.AreEqual(2, zone.Placements.Count);
            Assert.IsTrue(zone.Remove("inventory-a", "lot-1"));
            Assert.IsFalse(zone.Contains("inventory-a", "lot-1"));
            Assert.IsTrue(zone.Contains("inventory-b", "lot-1"));
        }

        [Test]
        public void ZoneStoresNoItemQuantityOrOwnershipAuthority()
        {
            StoredLotReference lot = Lot("existing-inventory", "stable-lot-id", StorageLoadMeasurement.Unknown);

            Assert.AreEqual("existing-inventory", lot.InventoryAuthorityId);
            Assert.AreEqual("stable-lot-id", lot.LotId);
            Assert.IsFalse(lot.Load.HasMass);
            Assert.IsFalse(lot.Load.HasVolume);
        }

        [Test]
        public void PlacementRequiresTheReferencedLotToResolveInItsInventoryAuthority()
        {
            PhysicalStorageZone noLookup = new PhysicalStorageZone(
                new PhysicalStorageZoneDefinition("zone-1", "site-1", StorageExposure.Indoor,
                    StorageCapacityLimit.Unknown, StorageCapacityLimit.Unknown));
            Assert.IsFalse(noLookup.TryPlace(Lot("inventory", "lot-1", StorageLoadMeasurement.Unknown), out string missingLookup));
            StringAssert.Contains("no existing-inventory lot lookup", missingLookup);

            PhysicalStorageZone missingLot = new PhysicalStorageZone(
                new PhysicalStorageZoneDefinition("zone-2", "site-2", StorageExposure.Covered,
                    StorageCapacityLimit.Unknown, StorageCapacityLimit.Unknown),
                (authority, lotId) => authority == "inventory" && lotId == "known-lot");
            Assert.IsFalse(missingLot.TryPlace(Lot("inventory", "orphan-lot", StorageLoadMeasurement.Unknown), out string unresolved));
            StringAssert.Contains("does not resolve", unresolved);
            Assert.AreEqual(0, missingLot.Placements.Count);
        }

        private static PhysicalStorageZone MakeZone(StorageCapacityLimit mass, StorageCapacityLimit volume) =>
            new PhysicalStorageZone(new PhysicalStorageZoneDefinition(
                "zone-1", "site-1", StorageExposure.Outdoor, mass, volume),
                (authority, lotId) => !string.IsNullOrWhiteSpace(authority) && !string.IsNullOrWhiteSpace(lotId));

        private static StoredLotReference Lot(string authority, string lotId, StorageLoadMeasurement load) =>
            new StoredLotReference(authority, lotId, load);
    }
}
